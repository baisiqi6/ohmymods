// 骑士稳定身份（GUID + 固定风格 0..4）的网络同步模块 —— nonce 握手版（Operator 修订契约）。
//
// 分工：身份（GUID/style/世代）由 KnightIdentityRuntime + KnightIdentityArchive 负责；
// 本模块只负责把主机的“已决收据”按需、可验证地送到客户端，并在客户端交给 Runtime 判定。
// 本模块不产身份、不抽风格、不读写 sidecar、不改任何原生存档字段。
//
// 协议：同一个自有 RPC 槽上做双向 nonce 握手（没有 nonce 的收据绝不推送）。
//   请求 kind=1：magic 'KNI1'(4) | version(1) | kind(1) | nonce int64 LE(8)                 = 14 字节
//   响应 kind=2：magic 'KNI1'(4) | version(1) | kind(1) | echoNonce int64 LE(8) | tail(33)  = 47 字节
//   其中 tail（33 字节）沿用序列化尾巴格式：magic 'KNI1'(4) | version(1) | Guid(16) | style int32(4) | hostLifetime int64(8)
//
//   客户端：当前 binding + 本地 runtime life 首次就绪时生成私有随机非零 nonce
//           （同 life 重试不换；life / Flush / 绑定 generation 变化立刻换新）。
//           现有 5s Sync 遍历传入缓存：无有效收据 + 已注册 + 已追平 → 每拍最多 16 条请求，周期重试。
//           只接受同时满足【当前 binding + 当前 localRuntimeLifetime + 当前 pending nonce】的响应，
//           随后交给 Runtime.ApplyHostReceipt（旧 nonce / 旧 life 响应一律拒绝，与历史 GUID 无关）。
//   主机：收到请求仅在【主机 + 对端在场且已追平 + header 注册有效 + owner 当前 world/life 有效 + 有收据】
//         时把响应排入**有界队列**，由下一次 Sync 发出（最多 16 条/拍，按 binding+nonce 去重）。
//         绝不在 RPC 回调里写 ByteBuffer（原生可能正在读同一缓冲），因此绝不在回调里发送。
//
// 序列化：Knight.GetSerializationData 之后仍追加 33 字节尾巴（原生字段原样保留）。
//         客户端 DeserializeFromData 只校验并消费（游标对齐），**不采纳**——身份只由 kind=2 响应写入，
//         杜绝 catchup 旧 tail 抢占身份。
//
// 槽位：CRPCHeader.RegisterComponents 返回 true 后为“含 Knight 组件的 header”追加一个自有槽
//   （含 Squire：rank 变化双端不错位）。始终追加到原生 RemoteMethodList 末尾、绝不复用原生槽、
//   绝不超过 256。重复注册只有**已有槽仍有效**时才复用；槽位失效（被回收/换主）时 fail closed，
//   等下一次真正的原生 RegisterComponents 重建（每 header 追加次数有界，绝不单边重追加换 index）。
//
// 兼容性：联机双方必须同版本 mod；缺模块/混用版本时槽位索引与载荷都不一致，不保证协议兼容。
// 原版存档单人兼容是另一件事（本模块不写存档）。

using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class KnightIdentityNetwork
{
    // ------------------------------------------------------------------ 协议常量

    /// <summary>尾巴（收据）字节数：magic(4) + version(1) + Guid(16) + style(4) + hostLifetime(8)。</summary>
    internal const int TailBytes = 33;

    /// <summary>magic（小端写入即 'K','N','I','1'）。</summary>
    internal const int TailMagic = 0x31494E4B;

    /// <summary>尾巴 schema 版本。</summary>
    internal const byte TailVersion = 1;

    /// <summary>magic 的字节序列，供无副作用探测。</summary>
    internal static readonly byte[] TailMagicBytes = { 0x4B, 0x4E, 0x49, 0x31 };

    /// <summary>握手包版本（请求/响应的公共前缀）。</summary>
    internal const byte PacketVersion = 1;

    /// <summary>客户端 → 主机：请求收据。</summary>
    internal const byte KindRequest = 1;

    /// <summary>主机 → 客户端：携带收据的响应。</summary>
    internal const byte KindResponse = 2;

    /// <summary>请求包长度：magic(4) + version(1) + kind(1) + nonce(8)。</summary>
    internal const int RequestBytes = 14;

    /// <summary>响应包长度：请求前缀 + 33 字节收据尾巴。</summary>
    internal const int ResponseBytes = RequestBytes + TailBytes;

    // ------------------------------------------------------------------ 有界常量

    /// <summary>单次 Sync 最多推进的发送条目（请求 + 响应合计）。</summary>
    internal const int MaxSendsPerPass = 16;

    /// <summary>原生 RPC function id 是单字节：追加槽位索引必须 &lt; 256。</summary>
    private const int MaxRpcSlots = 256;

    /// <summary>同一个 header 上允许的追加次数（只在真正的原生 RegisterComponents 之后累计）。</summary>
    private const int MaxTrackedBindings = 2048;

    /// <summary>待发响应的最大条数（溢出丢最旧）。</summary>
    private const int MaxPendingResponses = 512;

    /// <summary>同一条响应的发送尝试上限；到顶只丢队列条目（客户端会重发请求）。</summary>
    private const int MaxResponseAttempts = 3;

    /// <summary>跟踪的骑士状态上限（满时不再新建，绝不无界增长）。</summary>
    private const int MaxTrackedKnights = 512;

    /// <summary>连续缺席几拍后淘汰本地跟踪状态（只是本地握手记忆）。</summary>
    private const int MissingPassesBeforePrune = 2;

    // ------------------------------------------------------------------ 状态

    /// <summary>“含 Knight 组件的 CRPCHeader”上我们追加的 RPC 槽所有权。</summary>
    private sealed class HeaderBinding
    {
        internal CRPCHeader Header;
        internal IntPtr HeaderPointer;
        internal GameObject Owner;
        internal int OwnerId;
        internal int SlotIndex = -1;

        /// <summary>强持有：header 存活期间托管委托与原生委托都必须活着。</summary>
        internal Action ManagedDelegate;
        internal NetworkPostbox.DynAction NativeDelegate;

        /// <summary>原生 RegisterComponents 之后实际发生过的追加次数（有界）。</summary>

        /// <summary>绑定世代：每次新建 binding 递增，用于握手 nonce 的“绑定变化换 nonce”判定。</summary>
        internal int Generation;

        /// <summary>已作废（Flush/复用判定失效/上限）：闭包与原生槽都不再被本模块使用。</summary>
        internal bool Retired;
    }

    /// <summary>主机的待发响应（有界队列；按 binding 世代 + nonce 去重）。</summary>
    private sealed class PendingResponse
    {
        internal HeaderBinding Binding;
        internal int BindingGeneration;
        internal IntPtr HeaderPointer;
        internal long Nonce;
        internal int Attempts;
    }

    /// <summary>一名骑士在本进程的握手状态（不是持久化键；身份以 GOID + GOPointer + Knight.Pointer 为准）。</summary>
    private sealed class KnightSyncState
    {
        internal int InstanceId;
        internal IntPtr GameObjectPointer;
        internal IntPtr KnightPointer;
        internal Knight Knight;

        /// <summary>最近一次确认可用的绑定（只作“绑定变化换 nonce”的判定来源）。</summary>
        internal HeaderBinding Binding;

        /// <summary>当前 pending nonce；0 = 尚未生成。</summary>
        internal long Nonce;
        internal long NonceLifetime = -1L;
        internal int NonceBindingGeneration;

        internal int LastSeenPass;
    }

    private static readonly Dictionary<int, KnightSyncState> States = new Dictionary<int, KnightSyncState>();
    private static readonly Dictionary<IntPtr, HeaderBinding> BindingsByHeader = new Dictionary<IntPtr, HeaderBinding>();
    private static readonly Dictionary<int, HeaderBinding> BindingsByOwner = new Dictionary<int, HeaderBinding>();
    private static readonly List<PendingResponse> PendingResponses = new List<PendingResponse>();
    private static readonly List<int> PruneScratch = new List<int>();
    private static readonly HashSet<string> LoggedKeys = new HashSet<string>();

    private static int _pass;
    private static int _bindingGeneration;
    private static int _requestCursor;
    private static bool _loggedDelegateFailure;
    private static int _sendsTotal;

    /// <summary>当前跟踪的 RPC 槽数量（诊断/测试观察用）。</summary>
    internal static int TrackedBindingCount { get { return BindingsByHeader.Count; } }

    /// <summary>当前跟踪的骑士握手状态数量（诊断/测试观察用）。</summary>
    internal static int TrackedStateCount { get { return States.Count; } }

    /// <summary>待发响应条数（诊断/测试观察用）。</summary>
    internal static int PendingResponseCount { get { return PendingResponses.Count; } }

    /// <summary>本进程累计发出的请求 + 响应包数（诊断/测试观察用）。</summary>
    internal static int TotalSendCount { get { return _sendsTotal; } }

    // ------------------------------------------------------------------ 入口：5s 巡检

    /// <summary>
    /// 现有 KnightStyle IntegrityPass（每 5s）调用：只观察传入缓存 + 自有状态，不新增 driver、
    /// 不做场景扫描、不逐帧。客户端侧发请求（无收据 + 已注册 + 已追平，≤16/拍）；
    /// 主机侧把上一拍收到的请求转成响应发出（≤16/拍）。
    /// </summary>
    internal static void Sync(Knight[] currentKnights)
    {
        try
        {
            bool host = HasWorldAuth();
            _pass++;

            int budget = MaxSendsPerPass;
            if (!host) ObserveClientKnights(currentKnights, ref budget);
            PruneMissingStates();
            if (host) ProcessPendingResponses(ref budget);
        }
        catch (Exception e)
        {
            LogOnce("sync", e);
        }
    }

    /// <summary>主机发送闸门：主机 + 在线 + 对端在场 + 已追平（每次现算，不依赖调用顺序）。</summary>
    private static bool HostGateOpen()
    {
        return HasWorldAuth() && IsOnline() && IsClientPresent() && HasClientCaughtUp();
    }

    private static void ObserveClientKnights(Knight[] currentKnights, ref int budget)
    {
        if (currentKnights == null || currentKnights.Length == 0) return;
        bool gate = IsOnline() && HasClientCaughtUp();
        int count = currentKnights.Length;
        int start = _requestCursor < count ? _requestCursor : 0;
        int firstSkipped = -1;

        for (int offset = 0; offset < count; offset++)
        {
            int index = (start + offset) % count;
            Knight knight = currentKnights[index];
            if (knight == null) continue;
            GameObject owner = SafeGameObject(knight);
            if (owner == null || !owner.activeInHierarchy) continue;
            if (!IsInCurrentLayer(knight)) continue;
            int instanceId = SafeInstanceId(owner);
            if (instanceId == 0) continue;

            KnightSyncState state = GetOrCreateState(knight, owner, instanceId);
            if (state == null) continue;
            state.LastSeenPass = _pass;

            long life = GetLifetime(knight);
            if (life <= 0) continue;
            if (TryGetReceipt(knight, out KnightIdentityReceipt receipt) && receipt.IsValid) continue; // 已有收据：不再请求

            HeaderBinding binding = FindBindingForOwner(instanceId);
            if (binding == null || !IsBindingUsable(binding, owner)) continue; // 未注册/已失效：不发
            state.Binding = binding;

            long nonce = EnsureNonce(state, life, binding);
            if (nonce == 0) continue;
            if (!gate) continue;
            if (budget <= 0)
            {
                // 本拍预算用完：记下起点，下一拍从这里继续（轮转，绝不饿死后面的对象）。
                if (firstSkipped < 0) firstSkipped = index;
                continue;
            }

            budget--;
            TrySendRequest(binding, nonce);
        }

        _requestCursor = firstSkipped >= 0 ? firstSkipped : 0;
    }

    private static void PruneMissingStates()
    {
        if (States.Count == 0) return;
        PruneScratch.Clear();
        foreach (KeyValuePair<int, KnightSyncState> pair in States)
        {
            if (_pass - pair.Value.LastSeenPass >= MissingPassesBeforePrune) PruneScratch.Add(pair.Key);
        }
        for (int i = 0; i < PruneScratch.Count; i++) States.Remove(PruneScratch[i]);
    }

    /// <summary>生成/复用 pending nonce：同 life + 同 binding 世代沿用，否则立刻换新。</summary>
    private static long EnsureNonce(KnightSyncState state, long life, HeaderBinding binding)
    {
        if (state.Nonce != 0 && state.NonceLifetime == life && state.NonceBindingGeneration == binding.Generation)
        {
            return state.Nonce;
        }
        long nonce = NextNonce();
        state.Nonce = nonce;
        state.NonceLifetime = life;
        state.NonceBindingGeneration = binding.Generation;
        return nonce;
    }

    /// <summary>私有随机非零 64 位 nonce（不用 Unity 随机，不用游戏随机序列）。</summary>
    private static long NextNonce()
    {
        byte[] bytes = Guid.NewGuid().ToByteArray();
        long nonce = BitConverter.ToInt64(bytes, 0);
        return nonce != 0 ? nonce : 1L;
    }

    // ------------------------------------------------------------------ 主机响应队列

    private static void ProcessPendingResponses(ref int budget)
    {
        if (PendingResponses.Count == 0 || !HostGateOpen()) return;

        int index = 0;
        while (index < PendingResponses.Count && budget > 0)
        {
            PendingResponse pending = PendingResponses[index];
            HeaderBinding binding = pending != null ? pending.Binding : null;
            GameObject owner = binding != null ? binding.Owner : null;
            Knight knight = owner != null ? SafeGetComponent<Knight>(owner) : null;

            // 发送前完整复核：绑定仍是当前所有权、槽位仍有效、parentHeaderRef 匹配、仍在当前层、收据仍有效。
            if (pending.BindingGeneration != (binding != null ? binding.Generation : 0))
            {
                PendingResponses.RemoveAt(index); // 绑定已换代：旧响应作废（客户端下一拍重发请求）
                continue;
            }
            if (!IsBindingHealthy(binding, owner, knight))
            {
                PendingResponses.RemoveAt(index);
                continue;
            }

            long life = GetLifetime(knight);
            if (life <= 0 || !TryGetReceipt(knight, out KnightIdentityReceipt receipt) || !receipt.IsValid)
            {
                PendingResponses.RemoveAt(index); // 无收据：不响应
                continue;
            }

            budget--;
            if (TrySendResponse(binding, pending.Nonce, receipt, life))
            {
                PendingResponses.RemoveAt(index);
                continue;
            }
            pending.Attempts++;
            if (pending.Attempts >= MaxResponseAttempts)
            {
                PendingResponses.RemoveAt(index);
                LogOnce("response-exhausted", null);
                continue;
            }
            index++;
        }
    }

    private static void EnqueueResponse(HeaderBinding binding, long nonce)
    {
        for (int i = 0; i < PendingResponses.Count; i++)
        {
            PendingResponse existing = PendingResponses[i];
            if (existing.HeaderPointer == binding.HeaderPointer && existing.BindingGeneration == binding.Generation
                && existing.Nonce == nonce)
            {
                return; // 去重：binding 世代 + nonce
            }
        }

        if (PendingResponses.Count >= MaxPendingResponses)
        {
            PendingResponses.RemoveAt(0); // 有界：丢最旧（客户端会重发请求）
            LogOnce("response-queue-full", null);
        }

        PendingResponses.Add(new PendingResponse
        {
            Binding = binding,
            BindingGeneration = binding.Generation,
            HeaderPointer = binding.HeaderPointer,
            Nonce = nonce,
        });
    }

    // ------------------------------------------------------------------ 原生钩子

    /// <summary>CRPCHeader.RegisterComponents 后缀：原生注册成功后追加自有槽（重复注册不重复追加）。</summary>
    internal static void HandleHeaderRegistered(CRPCHeader header, GameObject owner, bool registered)
    {
        if (!registered || header == null || owner == null) return;
        try
        {
            // 含 Squire：标签/rank 在双端可能不同步，槽位必须在双端一致，否则原生 RPC 索引分叉。
            if (SafeGetComponent<Knight>(owner) == null) return;
            EnsureBinding(header, owner);
        }
        catch (Exception e)
        {
            LogOnce("register-components", e);
        }
    }

    /// <summary>CRPCHeader.Flush 后缀：header 已被清空，槽位所有权随之作废（旧 nonce 一并失效）。</summary>
    internal static void HandleHeaderFlushed(CRPCHeader header)
    {
        if (header == null) return;
        try
        {
            IntPtr pointer = header.Pointer;
            if (BindingsByHeader.TryGetValue(pointer, out HeaderBinding binding))
            {
                DropBinding(binding);
                BindingsByHeader.Remove(pointer); // Native Flush has cleared the callback slots.
            }
        }
        catch (Exception e)
        {
            LogOnce("header-flush", e);
        }
    }

    /// <summary>Knight.GetSerializationData 后缀：原生字段之后追加 33 字节尾巴（不动原生 out 参数）。</summary>
    internal static void HandleNativeSerialization(Knight knight)
    {
        if (knight == null) return;
        try
        {
            if (!TryGetReceipt(knight, out KnightIdentityReceipt receipt) || !receipt.IsValid) return;
            long lifetime = GetLifetime(knight);
            if (lifetime <= 0) return;
            WriteTailToBuffer(receipt.Id, receipt.Style, lifetime);
        }
        catch (Exception e)
        {
            LogOnce("serialize", e); // 绝不影响原生序列化
        }
    }

    /// <summary>
    /// Knight.DeserializeFromData 后缀：只做游标对齐——尾巴结构合法时消费 33 字节，但**不采纳身份**。
    /// 身份只由 nonce 握手（kind=2 响应）写入，避免 catchup 旧尾巴抢占。
    /// </summary>
    internal static void HandleNativeDeserialize(Knight knight)
    {
        if (knight == null) return;
        try
        {
            TryReadTailFromByteBuffer(out _, out _, out _);
        }
        catch (Exception e)
        {
            LogOnce("deserialize", e); // 绝不影响原生反序列化
        }
    }

    // ------------------------------------------------------------------ 客户端接收

    /// <summary>自有 RPC 槽被调用：按 kind 分派（主机处理请求，客户端处理响应）。</summary>
    private static void OnRemotePacket(HeaderBinding binding)
    {
        try
        {
            if (binding == null || binding.Retired) return;
            if (!VerifyBindingSlot(binding)) { DropBinding(binding); return; }
            if (!IsCurrentBinding(binding))
            {
                LogOnce("packet-stale-binding", null);
                return;
            }

            if (!TryReadPacketFromByteBuffer(out byte kind, out long nonce, out Guid id, out int style, out long hostLifetime))
            {
                LogOnce("packet-invalid", null); // 损坏/非本协议：fail closed，一个字节都不消费
                return;
            }

            GameObject owner = binding.Owner;
            if (owner == null) { LogOnce("packet-owner", null); return; }
            GameObject referenced = SafeReferencedGo(binding.Header);
            if (referenced == null || !IsSameGameObject(referenced, owner)) { LogOnce("packet-owner", null); return; }

            Knight knight = SafeGetComponent<Knight>(owner);
            if (knight == null) { LogOnce("packet-knight", null); return; }
            if (!MatchesParentHeader(knight, binding)) { LogOnce("packet-parent-header", null); return; }
            if (!IsInCurrentSceneForReceive(knight)) { LogOnce("packet-world", null); return; }

            if (kind == KindRequest) HostHandleRequest(binding, nonce);
            else HandleResponse(binding, knight, owner, nonce, id, style, hostLifetime);
        }
        catch (Exception e)
        {
            LogOnce("remote-packet", e); // 绝不影响原生 RPC 处理
        }
    }

    /// <summary>主机侧：请求入队（有界 + 去重），由下一次 Sync 发送——绝不在回调里写 ByteBuffer。</summary>
    private static void HostHandleRequest(HeaderBinding binding, long nonce)
    {
        if (!HostGateOpen())
        {
            LogOnce("request-gate", null);
            return;
        }
        GameObject owner = binding.Owner;
        Knight knight = owner != null ? SafeGetComponent<Knight>(owner) : null;
        if (knight == null || !MatchesParentHeader(knight, binding) || !IsInCurrentSceneForReceive(knight))
        {
            LogOnce("request-owner", null);
            return;
        }
        if (GetLifetime(knight) <= 0 || !TryGetReceipt(knight, out KnightIdentityReceipt receipt) || !receipt.IsValid)
        {
            LogOnce("request-no-receipt", null); // 无收据：不响应（客户端下一拍重试）
            return;
        }
        EnqueueResponse(binding, nonce);
    }

    /// <summary>客户端侧：响应必须同时匹配当前 binding + 当前 life + 当前 pending nonce 才交给 Runtime。</summary>
    private static void HandleResponse(HeaderBinding binding, Knight knight, GameObject owner,
        long nonce, Guid id, int style, long hostLifetime)
    {
        if (HasWorldAuth())
        {
            LogOnce("response-on-host", null); // 主机不采纳任何收据
            return;
        }

        KnightSyncState state = FindState(knight, owner);
        if (state == null)
        {
            LogOnce("response-no-state", null); // 没有 pending 请求：不受理
            return;
        }

        long life = GetLifetime(knight);
        if (life <= 0) { LogOnce("response-life", null); return; }
        if (state.Nonce == 0 || state.Nonce != nonce) { LogOnce("response-nonce", null); return; }           // 旧/错 nonce
        if (state.NonceLifetime != life) { LogOnce("response-lifetime", null); return; }                     // 旧 life
        if (state.NonceBindingGeneration != binding.Generation) { LogOnce("response-binding", null); return; } // 换过绑定

        KnightIdentityReceipt receipt = new KnightIdentityReceipt(id, style);
        if (!receipt.IsValid) { LogOnce("response-invalid", null); return; } // GUID/style/hostLifetime 范围已验证

        bool applied;
        try
        {
            applied = KnightIdentityRuntime.ApplyHostReceipt(knight, life, receipt);
        }
        catch (Exception e)
        {
            LogOnce("apply-receipt", e);
            return;
        }
        if (!applied) LogOnce("response-rejected", null); // Runtime 的判定（life/GUID/世界）
    }

    // ------------------------------------------------------------------ 绑定（RPC 槽）

    private static HeaderBinding FindBindingForOwner(int instanceId)
    {
        if (instanceId == 0) return null;
        return BindingsByOwner.TryGetValue(instanceId, out HeaderBinding binding) ? binding : null;
    }

    /// <summary>
    /// 原生 RegisterComponents 之后调用：已有槽仍有效则复用（不追加）；失效则作废旧所有权并追加一次
    /// （每 header 有上限）。槽位失效后**绝不**在别处单边重追加换 index——只在真正的原生注册后重建。
    /// </summary>
    private static HeaderBinding EnsureBinding(CRPCHeader header, GameObject owner)
    {
        if (header == null || owner == null) return null;

        IntPtr headerPointer;
        try
        {
            headerPointer = header.Pointer;
        }
        catch (Exception e)
        {
            LogOnce("header-pointer", e);
            return null;
        }
        int instanceId = SafeInstanceId(owner);
        if (instanceId == 0) return null;

        if (BindingsByHeader.TryGetValue(headerPointer, out HeaderBinding byHeader))
        {
            if (IsBindingUsable(byHeader, owner) && VerifyBindingSlot(byHeader)) return byHeader;
            DropBinding(byHeader);
            return null; // Poisoned header: only an observed native Flush restores registration eligibility.
        }
        if (BindingsByHeader.Count >= MaxTrackedBindings)
        {
            LogOnce("binding-capacity", null);
            return null;
        }
        if (BindingsByOwner.TryGetValue(instanceId, out HeaderBinding byOwner)) DropBinding(byOwner);
        HeaderBinding binding = AppendBinding(header, headerPointer, owner, instanceId);
        return binding;
    }

    /// <summary>
    /// 追加自有 RPC 槽：始终追加到原生 RemoteMethodList 末尾，绝不复用原生槽位
    /// （复用会让双端索引分叉并打乱原生 RPC 编号）。列表达到上限时拒绝注册。
    /// </summary>
    private static HeaderBinding AppendBinding(CRPCHeader header, IntPtr headerPointer, GameObject owner, int instanceId)
    {
        HeaderBinding binding = new HeaderBinding
        {
            Header = header,
            HeaderPointer = headerPointer,
            Owner = owner,
            OwnerId = instanceId,
        };

        binding.ManagedDelegate = new Action(() => OnRemotePacket(binding));
        try
        {
            binding.NativeDelegate = DelegateSupport.ConvertDelegate<NetworkPostbox.DynAction>(binding.ManagedDelegate);
        }
        catch (Exception e)
        {
            if (!_loggedDelegateFailure)
            {
                _loggedDelegateFailure = true;
                LogOnce("delegate-convert", e);
            }
            return null;
        }
        if (binding.NativeDelegate == null) return null;

        int slotIndex;
        try
        {
            var methods = header.RemoteMethodList;
            if (methods == null) return null;
            if (methods.Count >= MaxRpcSlots)
            {
                LogOnce("rpc-slot-limit", null);
                return null;
            }
            slotIndex = methods.Count;
            binding.SlotIndex = slotIndex;
            binding.Generation = ++_bindingGeneration;
            // Root BEFORE Add. An exception after insertion cannot leave a native callback without a managed root.
            binding.Retired = true;
            BindingsByHeader[headerPointer] = binding;
            BindingsByOwner[instanceId] = binding;
            methods.Add(binding.NativeDelegate);
            NetworkPostbox.DynAction appended = methods[slotIndex];
            if (appended == null || appended.Pointer != binding.NativeDelegate.Pointer)
            {
                LogOnce("rpc-append-verify", null);
                return null;
            }
        }
        catch (Exception e)
        {
            LogOnce("rpc-register", e);
            return null;
        }

        binding.SlotIndex = slotIndex;
        binding.Retired = false;
        BindingsByHeader[headerPointer] = binding;
        BindingsByOwner[instanceId] = binding;
        return binding;
    }

    /// <summary>槽位仍在原生列表原处、且仍是同一个委托对象（由原生注册重建过的列表才算），才算有效。</summary>
    private static bool VerifyBindingSlot(HeaderBinding binding)
    {
        if (binding == null || binding.Retired || binding.Header == null || binding.SlotIndex < 0) return false;
        try
        {
            var methods = binding.Header.RemoteMethodList;
            if (methods == null) return false;
            if (binding.SlotIndex >= methods.Count) return false;
            NetworkPostbox.DynAction entry = methods[binding.SlotIndex];
            if (entry == null || binding.NativeDelegate == null) return false;
            return entry.Pointer == binding.NativeDelegate.Pointer;
        }
        catch (Exception e)
        {
            LogOnce("slot-verify", e);
            return false;
        }
    }

    /// <summary>绑定是否仍指向该 owner 当前注册的 header，且 referencedGO 就是该 owner。</summary>
    private static bool IsBindingUsable(HeaderBinding binding, GameObject owner)
    {
        if (binding == null || binding.Retired || binding.Header == null || binding.Owner == null) return false;
        if (!IsSameGameObject(binding.Owner, owner)) return false;
        if (!IsCurrentHeader(binding)) return false;

        GameObject referenced = SafeReferencedGo(binding.Header);
        return referenced != null && IsSameGameObject(referenced, owner);
    }

    /// <summary>
    /// 绑定是否仍指向该 owner 当前注册的 header：
    /// - 查到 header：必须就是我们的那个；
    /// - 查不到（已注销/池内失活）：只有当我们记的 header 也不再引用该 owner 时才判失效，
    ///   以免把“池中失活但 header 仍在”的对象误判成 stale（会造成重复追加槽位）；
    /// - 查询异常：保守保留。
    /// </summary>
    private static bool IsCurrentHeader(HeaderBinding binding)
    {
        NetworkPostbox postbox = SafePostbox();
        if (postbox == null) return true;

        try
        {
            CRPCHeader current = postbox.GetHeaderFromObject(binding.Owner, false);
            if (current != null) return current.Pointer == binding.HeaderPointer;

            GameObject referenced = SafeReferencedGo(binding.Header);
            return referenced != null && IsSameGameObject(referenced, binding.Owner);
        }
        catch (Exception e)
        {
            LogOnce("header-lookup", e);
            return true;
        }
    }

    /// <summary>闭包里的 binding 是否仍是该 header 的当前所有权（旧 binding 一律 fail closed）。</summary>
    private static bool IsCurrentBinding(HeaderBinding binding)
    {
        if (binding == null || binding.Retired) return false;
        if (!BindingsByHeader.TryGetValue(binding.HeaderPointer, out HeaderBinding current)) return false;
        return ReferenceEquals(current, binding);
    }

    private static void DropBinding(HeaderBinding binding)
    {
        if (binding == null) return;
        binding.Retired = true; // 旧闭包/旧原生槽立即失效
        // Keep the retired binding and delegate roots until native Flush; never repair slots unilaterally.
        if (binding.OwnerId != 0 && BindingsByOwner.TryGetValue(binding.OwnerId, out HeaderBinding current)
            && ReferenceEquals(current, binding))
        {
            BindingsByOwner.Remove(binding.OwnerId);
        }
        // Queue traversal owns removal. Removing here could delete the next unrelated response after a failed slot check.
        foreach (KnightSyncState state in States.Values)
        {
            if (ReferenceEquals(state.Binding, binding)) state.Binding = null;
        }
    }

    /// <summary>knight.parentHeaderRef 必须就是我们要发/收的那个 header。</summary>
    private static bool MatchesParentHeader(Knight knight, HeaderBinding binding)
    {
        if (knight == null || binding == null || binding.Header == null) return false;
        try
        {
            CRPCHeader parent = knight.parentHeaderRef;
            return parent != null && parent.Pointer != IntPtr.Zero && parent.Pointer == binding.HeaderPointer;
        }
        catch (Exception e)
        {
            LogOnce("parent-header", e);
            return false;
        }
    }

    // ------------------------------------------------------------------ 包编解码

    /// <summary>写 14 字节请求包；nonce 为 0 或目标过小返回 false 且不改动目标。</summary>
    internal static bool WriteRequest(Span<byte> destination, long nonce)
    {
        if (destination.Length < RequestBytes) return false;
        if (nonce == 0) return false;

        int offset = 0;
        WriteInt32(destination, ref offset, TailMagic);
        destination[offset++] = PacketVersion;
        destination[offset++] = KindRequest;
        WriteInt64(destination, ref offset, nonce);
        return offset == RequestBytes;
    }

    /// <summary>解析 14 字节请求包；magic/版本/kind/nonce 任一不符即 false。</summary>
    internal static bool TryReadRequest(ReadOnlySpan<byte> source, out long nonce)
    {
        return TryReadPrefix(source, KindRequest, out nonce);
    }

    /// <summary>写 47 字节响应包：请求前缀 + 33 字节收据尾巴。</summary>
    internal static bool WriteResponse(Span<byte> destination, long nonce, Guid id, int style, long hostLifetime)
    {
        if (destination.Length < ResponseBytes) return false;
        if (nonce == 0) return false;

        int offset = 0;
        WriteInt32(destination, ref offset, TailMagic);
        destination[offset++] = PacketVersion;
        destination[offset++] = KindResponse;
        WriteInt64(destination, ref offset, nonce);
        return WriteTail(destination.Slice(offset), id, style, hostLifetime);
    }

    /// <summary>解析 47 字节响应包；前缀或收据尾巴任一非法即 false。</summary>
    internal static bool TryReadResponse(ReadOnlySpan<byte> source, out long nonce, out Guid id, out int style, out long hostLifetime)
    {
        id = Guid.Empty;
        style = -1;
        hostLifetime = -1;
        if (!TryReadPrefix(source, KindResponse, out nonce)) return false;
        return TryReadTail(source.Slice(RequestBytes), out id, out style, out hostLifetime);
    }

    private static bool TryReadPrefix(ReadOnlySpan<byte> source, byte expectedKind, out long nonce)
    {
        nonce = 0;
        if (source.Length < RequestBytes) return false;

        int offset = 0;
        if (ReadInt32(source, ref offset) != TailMagic) return false;
        if (source[offset++] != PacketVersion) return false;
        if (source[offset++] != expectedKind) return false;
        long parsed = ReadInt64(source, ref offset);
        if (parsed == 0) return false;
        nonce = parsed;
        return offset == RequestBytes;
    }

    // ------------------------------------------------------------------ 尾巴编解码

    /// <summary>写 33 字节尾巴；id 为空 / style 越界 / lifetime 为负 / 目标过小一律 false 且不改动目标。</summary>
    internal static bool WriteTail(Span<byte> destination, Guid id, int style, long hostLifetime)
    {
        if (destination.Length < TailBytes) return false;
        if (id == Guid.Empty) return false;
        if (!KnightIdentityReceipt.IsValidStyle(style)) return false;
        if (hostLifetime <= 0) return false;

        int offset = 0;
        WriteInt32(destination, ref offset, TailMagic);
        destination[offset] = TailVersion;
        offset++;
        if (!id.TryWriteBytes(destination.Slice(offset, 16))) return false;
        offset += 16;
        WriteInt32(destination, ref offset, style);
        WriteInt64(destination, ref offset, hostLifetime);
        return offset == TailBytes;
    }

    /// <summary>解析 33 字节尾巴；magic/version/style 范围/guid 非空/lifetime 非负任一不符即 false。</summary>
    internal static bool TryReadTail(ReadOnlySpan<byte> source, out Guid id, out int style, out long hostLifetime)
    {
        id = Guid.Empty;
        style = -1;
        hostLifetime = -1;
        if (source.Length < TailBytes) return false;

        int offset = 0;
        if (ReadInt32(source, ref offset) != TailMagic) return false;
        if (source[offset] != TailVersion) return false;
        offset++;

        Guid parsedId = new Guid(source.Slice(offset, 16));
        offset += 16;
        int parsedStyle = ReadInt32(source, ref offset);
        long parsedLifetime = ReadInt64(source, ref offset);

        if (offset != TailBytes) return false;
        if (parsedId == Guid.Empty) return false;
        if (!KnightIdentityReceipt.IsValidStyle(parsedStyle)) return false;
        if (parsedLifetime < 0) return false;

        id = parsedId;
        style = parsedStyle;
        hostLifetime = parsedLifetime;
        return true;
    }

    private static void WriteInt32(Span<byte> destination, ref int offset, int value)
    {
        destination[offset] = (byte)value;
        destination[offset + 1] = (byte)(value >> 8);
        destination[offset + 2] = (byte)(value >> 16);
        destination[offset + 3] = (byte)(value >> 24);
        offset += 4;
    }

    private static void WriteInt64(Span<byte> destination, ref int offset, long value)
    {
        for (int i = 0; i < 8; i++) destination[offset + i] = (byte)(value >> (8 * i));
        offset += 8;
    }

    private static int ReadInt32(ReadOnlySpan<byte> source, ref int offset)
    {
        int value = source[offset] | (source[offset + 1] << 8) | (source[offset + 2] << 16) | (source[offset + 3] << 24);
        offset += 4;
        return value;
    }

    private static long ReadInt64(ReadOnlySpan<byte> source, ref int offset)
    {
        long value = 0;
        for (int i = 0; i < 8; i++) value |= (long)source[offset + i] << (8 * i);
        offset += 8;
        return value;
    }

    /// <summary>写尾巴进共享 ByteBuffer：栈上 scratch，杜绝被 callback 重入弄乱。</summary>
    private static bool WriteTailToBuffer(Guid id, int style, long hostLifetime)
    {
        Span<byte> scratch = stackalloc byte[TailBytes];
        if (!WriteTail(scratch, id, style, hostLifetime)) return false;
        for (int i = 0; i < TailBytes; i++) ByteBuffer.Write(scratch[i]);
        return true;
    }

    /// <summary>
    /// 只读地验证并解析尾巴：先 peek 全量候选字节，只有整体合法才用 ReadByte 推进游标（恰好 33 字节）。
    /// </summary>
    private static bool TryReadTailFromByteBuffer(out Guid id, out int style, out long hostLifetime)
    {
        id = Guid.Empty;
        style = -1;
        hostLifetime = -1;

        int available = ByteBuffer.PollDataAvailableLength();
        if (available < TailBytes) return false; // 含 legacy 无尾巴：不猜
        int index = ByteBuffer.PollIndex();
        if (index < 0) return false;

        Span<byte> scratch = stackalloc byte[TailBytes];
        for (int i = 0; i < TailBytes; i++) scratch[i] = ByteBuffer.bufferAccess(index + i);
        if (!TryReadTail(scratch, out id, out style, out hostLifetime)) return false;

        for (int i = 0; i < TailBytes; i++) ByteBuffer.ReadByte();
        return true;
    }

    /// <summary>
    /// 只读地验证并解析握手包：先按 kind 判定长度，整体合法才推进游标
    /// （请求 14 字节 / 响应 47 字节）。unknown kind / 长度不足 / 结构非法：一个字节都不消费。
    /// </summary>
    private static bool TryReadPacketFromByteBuffer(out byte kind, out long nonce, out Guid id, out int style, out long hostLifetime)
    {
        kind = 0;
        nonce = 0;
        id = Guid.Empty;
        style = -1;
        hostLifetime = -1;

        int available = ByteBuffer.PollDataAvailableLength();
        if (available < RequestBytes) return false;
        int index = ByteBuffer.PollIndex();
        if (index < 0) return false;

        Span<byte> prefix = stackalloc byte[RequestBytes];
        for (int i = 0; i < RequestBytes; i++) prefix[i] = ByteBuffer.bufferAccess(index + i);

        if (!TryReadPrefix(prefix, KindRequest, out nonce))
        {
            // 不是请求：只可能是响应；其余（未知 kind / 损坏）一律 fail closed、不消费。
            if (!TryReadPrefix(prefix, KindResponse, out nonce)) return false;
            if (available < ResponseBytes) return false;

            Span<byte> full = stackalloc byte[ResponseBytes];
            for (int i = 0; i < ResponseBytes; i++) full[i] = ByteBuffer.bufferAccess(index + i);
            if (!TryReadTail(full.Slice(RequestBytes), out id, out style, out hostLifetime)) return false;

            for (int i = 0; i < ResponseBytes; i++) ByteBuffer.ReadByte();
            kind = KindResponse;
            return true;
        }

        for (int i = 0; i < RequestBytes; i++) ByteBuffer.ReadByte();
        kind = KindRequest;
        return true;
    }

    // ------------------------------------------------------------------ 发送（只有 Sync 调用；绝不从 RPC 回调里发）

    /// <summary>客户端请求：走自有槽，14 字节，不含任何身份信息。</summary>
    private static void TrySendRequest(HeaderBinding binding, long nonce)
    {
        GameObject owner = binding != null ? binding.Owner : null;
        Knight knight = owner != null ? SafeGetComponent<Knight>(owner) : null;
        if (!IsBindingHealthy(binding, owner, knight))
        {
            LogOnce("request-blocked", null); // 未注册/槽损坏/不在当前层：fail closed，下一拍再试
            return;
        }

        Span<byte> scratch = stackalloc byte[RequestBytes];
        if (!WriteRequest(scratch, nonce)) return;

        try
        {
            ByteBuffer.PrepWriteBuffer();
            for (int i = 0; i < RequestBytes; i++) ByteBuffer.Write(scratch[i]);
            binding.Header.CallMethodRemotely(binding.SlotIndex);
            _sendsTotal++;
        }
        catch (Exception e)
        {
            LogOnce("request-send", e);
        }
    }

    /// <summary>主机响应：走自有槽，47 字节（echoNonce + 33 字节收据尾巴）。</summary>
    private static bool TrySendResponse(HeaderBinding binding, long nonce, KnightIdentityReceipt receipt, long lifetime)
    {
        Span<byte> scratch = stackalloc byte[ResponseBytes];
        if (!WriteResponse(scratch, nonce, receipt.Id, receipt.Style, lifetime)) return false;

        try
        {
            ByteBuffer.PrepWriteBuffer();
            for (int i = 0; i < ResponseBytes; i++) ByteBuffer.Write(scratch[i]);
            binding.Header.CallMethodRemotely(binding.SlotIndex);
            _sendsTotal++;
            return true;
        }
        catch (Exception e)
        {
            LogOnce("response-send", e);
            return false;
        }
    }

    /// <summary>
    /// 发送前的绑定健康复核：槽位失效（被回收/换主）时作废所有权并 fail closed——
    /// 绝不单边重追加换 index（远端无法保证同 index，会串 RPC），等下一次真正的原生 RegisterComponents 重建。
    /// </summary>
    private static bool IsBindingHealthy(HeaderBinding binding, GameObject owner, Knight knight)
    {
        if (binding == null || owner == null || knight == null) return false;
        if (!IsBindingUsable(binding, owner) || !VerifyBindingSlot(binding))
        {
            LogOnce("binding-unhealthy", null);
            DropBinding(binding);
            return false;
        }
        if (!MatchesParentHeader(knight, binding)) return false;
        return IsInCurrentLayer(knight);
    }

    // ------------------------------------------------------------------ 世界/身份薄封装

    private static KnightSyncState GetOrCreateState(Knight knight, GameObject owner, int instanceId)
    {
        IntPtr knightPointer = SafePointer(knight);
        IntPtr ownerPointer = SafePointer(owner);
        if (knightPointer == IntPtr.Zero || ownerPointer == IntPtr.Zero) return null; // 身份不可核：不使用本地记忆

        if (States.TryGetValue(instanceId, out KnightSyncState state))
        {
            // 身份以 GOID + GameObject.Pointer + Knight.Pointer 为准（managed wrapper 复用不算新对象）。
            bool sameTarget = state.KnightPointer == knightPointer && state.GameObjectPointer == ownerPointer;
            if (!sameTarget) ResetState(state); // 池复用/换世：丢握手记忆，绝不沿用旧 nonce
            state.Knight = knight;
            state.KnightPointer = knightPointer;
            state.GameObjectPointer = ownerPointer;
            return state;
        }

        if (States.Count >= MaxTrackedKnights)
        {
            LogOnce("state-capacity", null); // 满时不瞎分配
            return null;
        }

        state = new KnightSyncState
        {
            InstanceId = instanceId,
            Knight = knight,
            KnightPointer = knightPointer,
            GameObjectPointer = ownerPointer,
        };
        States[instanceId] = state;
        return state;
    }

    private static KnightSyncState FindState(Knight knight, GameObject owner)
    {
        int instanceId = SafeInstanceId(owner);
        if (instanceId == 0) return null;
        IntPtr knightPointer = SafePointer(knight);
        IntPtr ownerPointer = SafePointer(owner);
        if (knightPointer == IntPtr.Zero || ownerPointer == IntPtr.Zero) return null;
        if (!States.TryGetValue(instanceId, out KnightSyncState state)) return null;
        if (state.KnightPointer != knightPointer || state.GameObjectPointer != ownerPointer) return null;
        return state;
    }

    private static void ResetState(KnightSyncState state)
    {
        state.Nonce = 0;
        state.NonceLifetime = -1L;
        state.NonceBindingGeneration = 0;
        state.Binding = null;
        state.Knight = null;
        state.KnightPointer = IntPtr.Zero;
        state.GameObjectPointer = IntPtr.Zero;
    }

    private static bool IsInCurrentLayer(Knight knight)
    {
        try
        {
            GameObject owner = SafeGameObject(knight);
            if (owner == null || !owner.activeInHierarchy) return false;
            Transform layer = CurrentLayer();
            if (layer == null || layer.gameObject == null || !layer.gameObject.activeInHierarchy) return false;
            Transform self = knight.transform;
            if (self == null || !self.IsChildOf(layer)) return false;
            return owner.scene.handle == layer.gameObject.scene.handle;
        }
        catch (Exception e)
        {
            LogOnce("world", e);
            return false;
        }
    }

    /// <summary>
    /// 接收路径（RPC/catchup）的宽松判据：只要仍在当前 gameLayer 的同一场景即可，
    /// 不要求父子关系已就绪——读档/池生成的瞬间 SetParent 可能还没跑完。
    /// </summary>
    private static bool IsInCurrentSceneForReceive(Knight knight)
    {
        try
        {
            GameObject owner = SafeGameObject(knight);
            if (owner == null || !owner.activeInHierarchy) return false;
            Transform layer = CurrentLayer();
            if (layer == null || layer.gameObject == null) return false;
            return owner.scene.handle == layer.gameObject.scene.handle;
        }
        catch (Exception e)
        {
            LogOnce("world-receive", e);
            return false;
        }
    }

    private static Transform CurrentLayer()
    {
        Managers managers = Managers.Inst;
        World world = managers != null ? managers.world : null;
        return world != null ? world.gameLayer : null;
    }

    private static bool TryGetReceipt(Knight knight, out KnightIdentityReceipt receipt)
    {
        receipt = default(KnightIdentityReceipt);
        try
        {
            return KnightIdentityRuntime.TryGetReceipt(knight, out receipt);
        }
        catch (Exception e)
        {
            LogOnce("runtime-receipt", e);
            return false;
        }
    }

    private static long GetLifetime(Knight knight)
    {
        try
        {
            return KnightIdentityRuntime.GetLifetime(knight);
        }
        catch (Exception e)
        {
            LogOnce("runtime-lifetime", e);
            return -1L;
        }
    }

    private static bool HasWorldAuth()
    {
        try
        {
            return NetworkBigBoss.HasWorldAuth;
        }
        catch (Exception e)
        {
            LogOnce("authority", e);
            return true; // 未知身份按“不能当客户端”处理（更保守：不发请求也不采纳）
        }
    }

    private static bool IsOnline()
    {
        try
        {
            return NetworkBigBoss.IsOnline;
        }
        catch (Exception e)
        {
            LogOnce("online", e);
            return false;
        }
    }

    private static bool IsClientPresent()
    {
        try
        {
            return NetworkBigBoss.IsClientPresent;
        }
        catch (Exception e)
        {
            LogOnce("client-present", e);
            return false;
        }
    }

    private static bool HasClientCaughtUp()
    {
        try
        {
            return NetworkBigBoss.HasClientCaughtUp;
        }
        catch (Exception e)
        {
            LogOnce("client-caught-up", e);
            return false;
        }
    }

    private static NetworkPostbox SafePostbox()
    {
        try
        {
            return NetworkPostbox.Instance;
        }
        catch (Exception e)
        {
            LogOnce("postbox", e);
            return null;
        }
    }

    private static GameObject SafeReferencedGo(CRPCHeader header)
    {
        try
        {
            return header != null ? header.referencedGO : null;
        }
        catch (Exception e)
        {
            LogOnce("header-go", e);
            return null;
        }
    }

    private static GameObject SafeGameObject(Component component)
    {
        if (component == null) return null;
        try
        {
            return component.gameObject;
        }
        catch (Exception e)
        {
            LogOnce("component-go", e);
            return null;
        }
    }

    private static int SafeInstanceId(GameObject gameObject)
    {
        if (gameObject == null) return 0;
        try
        {
            return gameObject.GetInstanceID();
        }
        catch (Exception e)
        {
            LogOnce("instance-id", e);
            return 0;
        }
    }

    private static IntPtr SafePointer(Component component)
    {
        if (component == null) return IntPtr.Zero;
        try
        {
            return component.Pointer;
        }
        catch (Exception e)
        {
            LogOnce("pointer", e);
            return IntPtr.Zero;
        }
    }

    private static IntPtr SafePointer(GameObject gameObject)
    {
        if (gameObject == null) return IntPtr.Zero;
        try
        {
            return gameObject.Pointer;
        }
        catch (Exception e)
        {
            LogOnce("pointer", e);
            return IntPtr.Zero;
        }
    }

    private static T SafeGetComponent<T>(GameObject gameObject) where T : Component
    {
        if (gameObject == null) return null;
        try
        {
            return gameObject.GetComponent<T>();
        }
        catch (Exception e)
        {
            LogOnce("get-component", e);
            return null;
        }
    }

    private static bool IsSameGameObject(GameObject left, GameObject right)
    {
        if (left == null || right == null) return false;
        if (ReferenceEquals(left, right)) return true;
        try
        {
            IntPtr leftPointer = left.Pointer;
            IntPtr rightPointer = right.Pointer;
            return leftPointer != IntPtr.Zero && leftPointer == rightPointer;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void LogOnce(string key, Exception e)
    {
        if (!LoggedKeys.Add(key)) return;
        try
        {
            string message = "[KnightIdentityNet] " + key;
            if (e != null) message += ": " + e.GetType().Name + " " + e.Message;
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(message);
        }
        catch
        {
            // 日志失败不影响功能。
        }
    }

    /// <summary>
    /// 只给测试与真正的进程/整场边界用：清空握手状态、槽位所有权与待发响应。
    /// 换世界/换层**不要**调用——原生 header 还在，丢记录会导致重复追加槽位、RPC 索引分叉。
    /// </summary>
    internal static void ResetForProcessBoundary()
    {
        foreach (HeaderBinding binding in BindingsByHeader.Values) binding.Retired = true;
        BindingsByHeader.Clear();
        BindingsByOwner.Clear();
        States.Clear();
        PendingResponses.Clear();
        LoggedKeys.Clear();
        _pass = 0;
        _bindingGeneration = 0;
        _requestCursor = 0;
        _sendsTotal = 0;
        _loggedDelegateFailure = false;
    }

    // ------------------------------------------------------------------ 原生包装类（显式独立；主类本身不带 Harmony 特性）

    [HarmonyPatch(typeof(Knight), nameof(Knight.GetSerializationData))]
    private static class KnightSerializationPatch
    {
        [HarmonyPostfix]
        private static void AfterSerialization(Knight __instance)
        {
            try
            {
                HandleNativeSerialization(__instance);
            }
            catch (Exception e)
            {
                LogOnce("serialize-hook", e);
            }
        }
    }

    [HarmonyPatch(typeof(Knight), nameof(Knight.DeserializeFromData))]
    private static class KnightDeserializePatch
    {
        [HarmonyPostfix]
        private static void AfterDeserialize(Knight __instance)
        {
            try
            {
                HandleNativeDeserialize(__instance);
            }
            catch (Exception e)
            {
                LogOnce("deserialize-hook", e);
            }
        }
    }

    [HarmonyPatch(typeof(CRPCHeader), nameof(CRPCHeader.RegisterComponents))]
    private static class RegisterComponentsPatch
    {
        [HarmonyPostfix]
        private static void AfterRegisterComponents(CRPCHeader __instance, GameObject __0, bool __result)
        {
            try
            {
                HandleHeaderRegistered(__instance, __0, __result);
            }
            catch (Exception e)
            {
                LogOnce("register-hook", e);
            }
        }
    }

    [HarmonyPatch(typeof(CRPCHeader), nameof(CRPCHeader.Flush))]
    private static class HeaderFlushPatch
    {
        [HarmonyPostfix]
        private static void AfterFlush(CRPCHeader __instance)
        {
            try
            {
                HandleHeaderFlushed(__instance);
            }
            catch (Exception e)
            {
                LogOnce("flush-hook", e);
            }
        }
    }
}
