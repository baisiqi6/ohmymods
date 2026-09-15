using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Hermes 头饰（HermesStaff 转化 → FriendlyTroll 头饰抽选）核心。
///
/// 范围：只有 “HermesStaff 权杖转化出的新 FriendlyTroll” 才有收据与外观。
/// 抽选在主机（world auth）侧、转化上下文内、Init 时一次性完成：30%（可配）命中
/// 则通过独立游标轮流取固定码 0..43，未命中/功能关闭时显式写 choice=-1。原生
/// maskIndex/trollHealth/toughTroll、stats、永久控制一律不改；头饰伪装选敌由独立规则读取当前外观。
///
/// 所有权边界：
/// - 收据（receipt：Guid token + choice，可落盘、可上网）归本类所有；
/// - 所有 sprite/GameObject 视觉状态归 <c>HermesHeadwearVisuals</c>，本类只在
///   “原生 SpawnMask 之后” 重新调用 Apply/Clear；失败返回 false 时按同一 choice
///   做有界重试，绝不重抽。
/// - 运行时映射（GameObject InstanceID / Pointer / CRPCHeader）只描述“当前世代”，
///   从不作为持久化键；换世界/换层/死池回收/死亡都立即释放。
///
/// 盘上扩展：<c>IslandSaveData.ObjectData(Persistent, bool)</c> 且
/// useNetworkData == false 时，在精确的 <c>FriendlyTrollData</c> 组件 payload 里
/// 注入/替换唯一属性 <c>kemHermesHeadwear</c>（其余字节逐字节保留）；
/// <c>TryCreateOrFind</c> 后缀在原生 ApplyData 之前把收据绑回返回的 Persistent。
/// useNetworkData == true 的网络数据流一律不碰（那里走序列化尾巴）。
///
/// 网络：原生 PoolSpawn 不含组件数据，因此
/// 1) <c>FriendlyTroll.GetSerializationData</c> 后缀把 30 字节版本化尾巴追加到原生
///    3 个字段之后（后期加入者 CatchupCRPC / 主机世界流自动带上）；
/// 2) <c>CRPCHeader.RegisterComponents</c> 返回后为“含 FriendlyTroll 的 header”
///    追加一个自有 RPC 槽（绝不改原生槽位与编号），主机在 Init/配置变化后发送
///    token+choice+hostEnabled+revision；客户端只应用主机判定，不本地随机。
///    同世代 token + 单调 revision 挡过期更新，已退役 token 挡跨世代更新；
///    <c>Flush</c> 清空所有权。
/// </summary>
internal static class PatchDivine_HermesHeadwear
{
    internal const int DefaultChancePercent = 30;

    /// <summary>单次 Tick 最多推进几次外观重试（避免同一帧对大量对象做工）。</summary>
    private const int MaxVisualAttemptsPerTick = 2;

    /// <summary>外观重试退避上限（visual 资源 30s 补齐窗口），超过即稳定在 30s 继续重试。</summary>
    private const float VisualRetryBackoffCapSeconds = 30f;

    /// <summary>外观重试起始间隔。</summary>
    private const float VisualRetryInitialSeconds = 1f;

    /// <summary>一次决策 “真实失败”（非 header 未就绪、非未追平的顺延）的最大发送次数。</summary>
    private const int MaxSendFailures = 6;

    private const float SendRetryIntervalSeconds = 1f;
    private const float SweepIntervalSeconds = 1f;
    private const float RetiredTokenTtlSeconds = 300f;

    /// <summary>读档刚绑定的收据在扫描里享受的保护窗口（层/父物体可能尚未就绪）。</summary>
    private const float LoadGraceSeconds = 5f;

    private const int MaxBindingAppendsPerHeader = 2;

    /// <summary>原生 RPC function id 是字节：追加槽位索引必须 &lt; 256，达到上限就放弃注册/发送。</summary>
    private const int MaxRpcSlots = 256;

    /// <summary>绑定写入后多久才允许“当前 header 探测”淘汰（避免注册进行中的误判）。</summary>
    private const float BindingPruneGraceSeconds = 5f;

    private enum SendOutcome
    {
        Sent,
        Deferred,
        Failed,
    }

    /// <summary>一个 FriendlyTroll “当前世代” 的运行时状态（不是持久化键）。</summary>
    internal sealed class TrollState
    {
        internal FriendlyTroll Troll;
        internal int InstanceId;
        internal IntPtr GameObjectPointer;

        /// <summary>建立该世代时的世界指针：换世界后旧世代按“不属于当前世界”淘汰，绝不误清新读入的对象。</summary>
        internal IntPtr WorldPointer;

        /// <summary>读档绑定收据后的保护窗口终点（层/父物体可能还没就绪，扫描不得丢弃）。</summary>
        internal float ProtectUntil;

        /// <summary>收据身份；<see cref="Guid.Empty"/> = 尚无有效收据（不落盘扩展、不发 RPC）。</summary>
        internal Guid Token = Guid.Empty;
        internal int Choice = HermesHeadwearCodec.ChoiceNone;
        internal bool HostEnabled;
        internal int Revision;

        /// <summary>
        /// 未知 schema（v != 1 等）的扩展原始 JSON：原样保留、存档时逐字注回，
        /// 绝不显示、绝不抽选、绝不动原生字段。非 null 时优先于 token/choice。
        /// </summary>
        internal string OpaqueMetadataJson;

        /// <summary>true = 主机侧（或已采纳主机判定的）活决策，跨 token 不得静默覆盖。</summary>
        internal bool Sealed;

        internal bool VisualApplied;

        /// <summary>当前外观重试退避（秒），成功后回到起始值；上限 30s，永不永久放弃。</summary>
        internal float VisualRetryDelay = VisualRetryInitialSeconds;
        internal float NextApplyAt;
        internal bool InVisualQueue;

        internal bool SendPending;
        internal int SendFailures;
        internal float NextSendAt;
        internal bool InSendQueue;

        internal HeaderBinding Binding;
    }

    /// <summary>“含 FriendlyTroll 的 CRPCHeader” 上我们追加的 RPC 槽所有权。</summary>
    internal sealed class HeaderBinding
    {
        internal CRPCHeader Header;
        internal IntPtr HeaderPointer;
        internal GameObject Owner;
        internal int OwnerId;

        /// <summary>注册拿到槽位；-1 = 尚未注册成功。</summary>
        internal int SlotIndex = -1;

        /// <summary>强持有：header 存活期间托管委托与原生委托都必须活着。</summary>
        internal Action ManagedDelegate;
        internal NetworkPostbox.DynAction NativeDelegate;
        internal int AppendAttempts;

        /// <summary>写入时刻：绑定淘汰探测前的保护期。</summary>
        internal float CreatedAt;
    }

    private struct RetiredToken
    {
        internal Guid Token;
        internal float ExpireAt;
    }

    private static readonly Dictionary<int, TrollState> States = new Dictionary<int, TrollState>();
    private static readonly Dictionary<IntPtr, HeaderBinding> BindingsByHeader = new Dictionary<IntPtr, HeaderBinding>();
    private static readonly Dictionary<int, HeaderBinding> BindingsByOwner = new Dictionary<int, HeaderBinding>();
    private static readonly Dictionary<int, RetiredToken> RetiredTokens = new Dictionary<int, RetiredToken>();
    private static readonly List<TrollState> SendQueue = new List<TrollState>();
    private static readonly List<TrollState> VisualQueue = new List<TrollState>();
    private static readonly List<int> SweepScratch = new List<int>();
    private static readonly List<IntPtr> SweepBindingsScratch = new List<IntPtr>();
    private static readonly HashSet<string> LoggedErrors = new HashSet<string>();

    /// <summary>读写共用（两条路径不会嵌套）；仅尾部 30 字节，热路径零分配。</summary>
    private static readonly byte[] TailScratch = new byte[HermesHeadwearCodec.TailBytes];

    private static int _conversionDepth;
    private static volatile bool _settingsDirty;
    private static bool _lastGlobalEnabled;
    private static bool _lastEffectiveEnabled;
    private static float _nextSweepAt;
    private static IntPtr _worldPointer;
    private static bool _sendGateOpen;
    private static bool _loggedDelegateFailure;

    /// <summary>当前跟踪的世代数（诊断/测试观察用）。</summary>
    internal static int TrackedStateCount => States.Count;

    // ------------------------------------------------------------------ 入口

    /// <summary>ModPanel.Update 每帧调用：只处理自有状态，不扫描场景。</summary>
    internal static void Tick()
    {
        try
        {
            HermesHeadwearVisuals.Tick();
        }
        catch (Exception e)
        {
            LogOnce("visual-tick", e);
        }

        try
        {
            float now = Time.time;
            ReconcileConfig();
            UpdateSendGate();
            ProcessVisualRetries(now);
            ProcessSendQueue(now);
            if (now >= _nextSweepAt)
            {
                _nextSweepAt = now + SweepIntervalSeconds;
                Sweep(now);
            }
        }
        catch (Exception e)
        {
            LogOnce("tick", e);
        }
    }

    /// <summary>
    /// 配置变化入口（root 经 SettingChanged 接线）。可能来自 BepInEx 文件监视线程，
    /// 因此这里**只置脏标记**：绝不触碰 Unity/原生、绝不遍历状态、绝不 Apply/发送。
    /// 主线程 Tick 读取该标记后统一收敛（值语义，不依赖边沿）。
    /// </summary>
    internal static void OnSettingsChanged()
    {
        _settingsDirty = true;
    }

    // ------------------------------------------------------------------ 转化上下文

    internal static void EnterConversionContext()
    {
        _conversionDepth++;
    }

    internal static void ExitConversionContext()
    {
        if (_conversionDepth > 0) _conversionDepth--;
    }

    internal static bool IsConversionContextActive()
    {
        return _conversionDepth > 0;
    }

    // ------------------------------------------------------------------ 原生钩子实现

    /// <summary>FriendlyTroll.Init 后缀：转化上下文内、主机侧、每世代只抽选一次。</summary>
    internal static void HandleNativeInit(FriendlyTroll troll)
    {
        if (troll == null) return;
        if (!IsConversionContextActive()) return;
        if (!IsHostAuthority()) return;

        GameObject owner = SafeGameObject(troll);
        if (owner == null) return;
        int instanceId = SafeInstanceId(owner);
        if (instanceId == 0) return;

        TrollState state = null;
        if (States.TryGetValue(instanceId, out TrollState existing) && IsSameGeneration(existing, owner))
        {
            state = existing;
            if (state.Token != Guid.Empty || state.OpaqueMetadataJson != null)
            {
                // Init 重复：同一世代沿用同一收据，绝不重抽。
                state.Troll = troll;
                ApplyVisual(troll, state);
                return;
            }
        }

        if (state == null) state = CreateState(troll, owner, instanceId);

        state.Token = Guid.NewGuid();
        state.Choice = RollChoice();
        state.Revision = 1;
        state.Sealed = true;
        state.HostEnabled = EffectiveHostEnabled();
        state.VisualRetryDelay = VisualRetryInitialSeconds;
        HermesHeadwearDiagnostics.Decision(troll, state.Choice, state.HostEnabled);
        state.OpaqueMetadataJson = null; // 新收据取代旧的未知 schema 记录
        ClearRetiredToken(instanceId);

        QueueSend(state);
        ApplyVisual(troll, state);
    }

    /// <summary>FriendlyTroll.ApplyData 后缀（主机读档路径）：原生 SpawnMask 之后重放自有外观。</summary>
    internal static void HandleNativeApplyData(FriendlyTroll troll)
    {
        TrollState state = FindState(troll);
        if (state == null) return;
        ApplyVisual(troll, state);
    }

    /// <summary>FriendlyTroll.DeserializeFromData 后缀（客户端路径）：只读有界尾巴。</summary>
    internal static void HandleNativeDeserialize(FriendlyTroll troll)
    {
        if (troll == null) return;

        bool sawTail = false;
        HermesHeadwearCodec.Tail tail = default(HermesHeadwearCodec.Tail);
        try
        {
            sawTail = TryReadTailFromByteBuffer(out tail);
        }
        catch (Exception e)
        {
            LogOnce("tail-read", e);
        }

        TrollState state = FindState(troll);
        if (sawTail)
        {
            if (state == null) state = CreateStateFor(troll);
            if (state != null)
            {
                // 客户端读档/追平路径：对象可能刚由池/世界流生成，层身份尚未就绪。
                state.ProtectUntil = Time.time + LoadGraceSeconds;
                if (TryAdoptHostDecision(state, tail)) ApplyVisual(troll, state);
            }
            return;
        }

        // 旧版 payload（无尾巴）：不报错、不重抽；已有收据保持不动。
        if (state != null) ApplyVisual(troll, state);
    }

    /// <summary>FriendlyTroll.GetSerializationData 后缀：把当前收据追加到原生 3 字段之后。</summary>
    internal static void HandleNativeSerialization(FriendlyTroll troll)
    {
        TrollState state = FindState(troll);
        if (state == null || state.Token == Guid.Empty) return;
        WriteTailToByteBuffer(state.Token, state.Choice, state.HostEnabled, state.Revision);
    }

    /// <summary>FriendlyTroll.ResetAndDespawn 前缀：死亡/复原/池回收 → 释放本世代。</summary>
    internal static void HandleNativeResetAndDespawn(FriendlyTroll troll)
    {
        TrollState state = FindState(troll);
        if (state == null) return;
        HermesHeadwearDiagnostics.Removed(troll, state.Choice, "native-reset-despawn");
        DropState(state, clearVisual: true);
    }

    /// <summary>Pool.FastSpawn 后缀：复用实例前清掉上一世代的收据与外观。</summary>
    internal static void HandlePoolSpawn(GameObject spawned)
    {
        if (spawned == null) return;
        if (States.Count == 0) return;

        int instanceId = SafeInstanceId(spawned);
        if (instanceId == 0) return;
        if (!States.TryGetValue(instanceId, out TrollState state)) return;

        if (!IsSameGeneration(state, spawned))
        {
            // InstanceID 命中但指针不同：旧对象已销毁，只丢引用（不触碰旧实例）。
            RetireState(state);
            RemoveState(state);
            return;
        }

        HermesHeadwearDiagnostics.Removed(state.Troll, state.Choice, "pool-fast-spawn");
        DropState(state, clearVisual: true);
    }

    /// <summary>Persistent.OnDisable 前缀：只处理“我们跟踪的 FriendlyTroll 宿主”。</summary>
    internal static void HandlePersistentDisable(Persistent persistent)
    {
        if (persistent == null) return;
        if (States.Count == 0) return;

        GameObject owner = SafeGameObject(persistent);
        if (owner == null) return;
        int instanceId = SafeInstanceId(owner);
        if (instanceId == 0) return;
        if (!States.TryGetValue(instanceId, out TrollState state)) return;
        // 只有“这个 Persistent 所在 GameObject 上的 FriendlyTroll root”才处理：
        // 子物体的 Persistent 不在 map 里，且这里再做一次精确类型确认，绝不误清子物体。
        if (SafeGetComponent<FriendlyTroll>(owner) == null) return;

        if (!IsSameGeneration(state, owner))
        {
            RetireState(state);
            RemoveState(state);
            return;
        }

        // 暂停/失活/池缓存：先撤外观，收据与盘上元数据保持（下次原生触发再重放）。
        HermesHeadwearDiagnostics.Removed(state.Troll, state.Choice, "persistent-disable");
        DetachVisual(state);
    }

    /// <summary>ObjectData(Persistent, bool) 后缀：只给本地存档补收据，且只认精确 FriendlyTrollData。</summary>
    internal static void HandleObjectDataBuilt(IslandSaveData.ObjectData objectData, Persistent persistent, bool useNetworkData)
    {
        if (useNetworkData) return;
        if (objectData == null || persistent == null) return;
        if (States.Count == 0) return;

        GameObject owner = SafeGameObject(persistent);
        if (owner == null) return;
        int instanceId = SafeInstanceId(owner);
        if (instanceId == 0) return;
        if (!States.TryGetValue(instanceId, out TrollState state)) return;
        if (!IsSameGeneration(state, owner)) return;

        // 未知 schema 优先逐字注回（原样保留）；否则写当前收据。功能开关不影响写入。
        string metadataJson = state.OpaqueMetadataJson;
        if (metadataJson == null && state.Token != Guid.Empty)
        {
            metadataJson = HermesHeadwearCodec.BuildMetadataJson(state.Token, state.Choice);
        }
        if (metadataJson == null) return;

        var components = objectData.componentData2;
        if (components == null) return;

        for (int i = 0; i < components.Count; i++)
        {
            var component = components[i];
            if (component == null) continue;
            if (!IsFriendlyTrollDataComponent(component.name, component.type)) continue;

            string updated = HermesHeadwearCodec.InjectMetadata(component.data, metadataJson);
            if (updated == null)
            {
                LogOnce("metadata-inject", null);
                return;
            }
            component.data = updated;
            return;
        }
    }

    // ------------------------------------------------------------------ Save 桥（薄）

    /// <summary>
    /// 一次原生 <c>IslandSaveData.Save(campaign, land, challengeId)</c> 调用内的 snapshot 捕获：
    /// 只把本次 GetID 观察到的 native uniqueID 映射到持有“已决收据 / opaque 记录”的 Friendly owner，
    /// 并在 Save 返回后据此把扩展注回本次 ObjectData。
    ///
    /// 这是**调用期内**的临时关联，不是持久化键：唯一持久身份仍是收据里的 Guid；
    /// 不接受 InstanceID / netID 作为长期 key，且不会把 native uniqueID 写进我们的记录。
    /// </summary>
    internal sealed class SaveCapture
    {
        /// <summary>外层（嵌套 Save）上下文，finalizer 恢复用。</summary>
        internal SaveCapture Previous;

        /// <summary>本次真实正在保存的 island（原生 finally 会清 static，这里保持引用）。</summary>
        internal IslandSaveData Island;

        /// <summary>native uniqueID → 该对象上的 Persistent（仅本次 snapshot）。</summary>
        internal readonly Dictionary<string, Persistent> OwnersByUniqueId =
            new Dictionary<string, Persistent>(StringComparer.Ordinal);
    }

    private static SaveCapture _saveCapture;

    /// <summary>当前是否有 Save 上下文（诊断/测试观察用）。</summary>
    internal static bool HasActiveSaveCapture => _saveCapture != null;

    /// <summary>当前 Save 上下文里登记的 owner 数（无上下文为 0）。</summary>
    internal static int CapturedOwnerCount =>
        _saveCapture == null ? 0 : _saveCapture.OwnersByUniqueId.Count;

    /// <summary>
    /// 原生 Save 前缀：建立本次上下文，返回它作为 Harmony <c>__state</c>（内含 Previous 供 finalizer 恢复）。
    /// 只分配对象，不触碰 Unity/原生。
    /// </summary>
    internal static SaveCapture BeginSaveCapture()
    {
        SaveCapture capture = new SaveCapture { Previous = _saveCapture };
        _saveCapture = capture;
        return capture;
    }

    /// <summary>Harmony finalizer：无条件恢复上一个上下文，并原样返回异常（绝不吞）。</summary>
    internal static Exception EndSaveCapture(Exception exception, SaveCapture capture)
    {
        if (capture == null)
        {
            // prefix 没跑成（异常路径）：绝不清掉更外层的上下文
            if (_saveCapture != null) LogOnce("save-scope-unbalanced", null);
            return exception;
        }
        _saveCapture = capture.Previous;
        return exception;
    }

    /// <summary>
    /// 原生 <c>GetID(Persistent)</c> 后缀：只在 Save 上下文存在时登记（无上下文零副作用）。
    /// 只记录 Friendly 且当前持有已决收据/opaque 的 owner；不修改返回的 ID。
    /// </summary>
    internal static void HandleGetId(Persistent forObject, string uniqueId)
    {
        SaveCapture capture = _saveCapture;
        if (capture == null) return;
        if (forObject == null || string.IsNullOrEmpty(uniqueId)) return;
        if (!IsRelevantSaveOwner(forObject)) return;

        // 真实 island 引用在 ObjectData 构造期间已设好；native finally 会清 static，context 保持引用。
        if (capture.Island == null) capture.Island = CurrentSavingIsland();
        if (capture.OwnersByUniqueId.ContainsKey(uniqueId)) return;
        capture.OwnersByUniqueId[uniqueId] = forObject;
    }

    /// <summary>
    /// 原生 Save 后缀（native 主体已完成、static 已被 native finally 清掉之后）：
    /// 用捕获的 island.objects 枚举本次 snapshot 的记录，按 uniqueID 找回 owner 并再次核其合法性，
    /// 复用 <see cref="HandleObjectDataBuilt"/> 注入（selected / miss / unknown schema / 功能关闭都照写）。
    /// </summary>
    internal static void ApplySaveCapture(SaveCapture capture)
    {
        if (capture == null) return;
        if (capture.OwnersByUniqueId.Count == 0) return; // 本次 snapshot 没有我们的对象：noop
        if (capture.Island == null) capture.Island = CurrentSavingIsland();
        if (capture.Island == null)
        {
            LogOnce("save-island-missing", null);
            return;
        }

        var records = capture.Island.objects;
        if (records == null) return;
        for (int i = 0; i < records.Count; i++)
        {
            var record = records[i];
            if (record == null) continue;
            string uniqueId = record.uniqueID;
            if (string.IsNullOrEmpty(uniqueId)) continue;
            if (!capture.OwnersByUniqueId.TryGetValue(uniqueId, out Persistent owner)) continue;
            if (!IsRelevantSaveOwner(owner)) continue; // 再核 owner 仍合法
            try
            {
                HandleObjectDataBuilt(record, owner, false);
            }
            catch (Exception e)
            {
                LogOnce("save-record-inject", e);
            }
        }
    }

    /// <summary>owner 仍活、是 FriendlyTroll 宿主、且当前持有已决收据或 opaque 记录。</summary>
    private static bool IsRelevantSaveOwner(Persistent owner)
    {
        GameObject ownerGo = SafeGameObject(owner);
        if (ownerGo == null) return false;
        if (SafeGetComponent<FriendlyTroll>(ownerGo) == null) return false;
        int instanceId = SafeInstanceId(ownerGo);
        if (instanceId == 0) return false;
        if (!States.TryGetValue(instanceId, out TrollState state)) return false;
        if (!IsSameGeneration(state, ownerGo)) return false;
        return state.OpaqueMetadataJson != null || state.Token != Guid.Empty;
    }

    /// <summary>原生 Save 期间真实正在保存的 island 引用（属性优先，字段兜底；异常时返回 null）。</summary>
    private static IslandSaveData CurrentSavingIsland()
    {
        try
        {
            IslandSaveData island = IslandSaveData.CurrentlySavingIsland;
            if (island != null) return island;
        }
        catch (Exception e)
        {
            LogOnce("saving-island", e);
        }
        try
        {
            return IslandSaveData._currentlySavingIsland;
        }
        catch (Exception e)
        {
            LogOnce("saving-island-field", e);
            return null;
        }
    }

    /// <summary>TryCreateOrFind 后缀：原生 ApplyData 之前把盘上收据绑到真正返回的 Persistent。</summary>
    internal static void HandleTryCreateOrFind(IslandSaveData.ObjectData objectData, Persistent result)
    {
        if (objectData == null || result == null) return;

        GameObject owner = SafeGameObject(result);
        if (owner == null) return;
        if (SafeGetComponent<FriendlyTroll>(owner) == null) return;

        HermesHeadwearCodec.ReceiptInfo receipt = FindFriendlyTrollReceipt(objectData);
        if (!receipt.Found) return; // 完全没有我们的扩展（旧档）：保持原生，绝不抽选

        // 未知 schema：原生 FromJson 会丢掉这个字段，原生 RetrieveData 也不会带回它，
        // 所以必须把**原始扩展 JSON** 留在世代状态里，存档时逐字注回；绝不显示/抽选。
        string opaque = null;
        if (!receipt.Recognized)
        {
            opaque = FindFriendlyTrollRawMetadata(objectData);
            if (string.IsNullOrEmpty(opaque)) return;
        }

        int instanceId = SafeInstanceId(owner);
        if (instanceId == 0) return;

        TrollState state = null;
        if (States.TryGetValue(instanceId, out TrollState existing) && IsSameGeneration(existing, owner))
        {
            state = existing;
            if (state.Token != Guid.Empty && state.Sealed) return; // 该世代已有活决策
        }
        if (state == null) state = CreateStateFor(owner, instanceId);
        if (state == null) return;

        state.OpaqueMetadataJson = opaque;
        if (opaque != null)
        {
            state.Token = Guid.Empty;
            state.Choice = HermesHeadwearCodec.ChoiceNone;
            state.HostEnabled = false;
            state.Revision = 0;
            state.Sealed = false;
            state.VisualRetryDelay = VisualRetryInitialSeconds;
            state.ProtectUntil = Time.time + LoadGraceSeconds;
            return;
        }

        state.Token = receipt.Token;
        state.Choice = receipt.Choice;
        state.Revision = 1;
        state.Sealed = IsHostAuthority();
        state.HostEnabled = EffectiveHostEnabled();
        state.VisualRetryDelay = VisualRetryInitialSeconds;
        state.ProtectUntil = Time.time + LoadGraceSeconds;
        state.OpaqueMetadataJson = null;
        ClearRetiredToken(instanceId);
    }

    /// <summary>CRPCHeader.RegisterComponents 后缀：跟在原生注册之后追加自有槽位。</summary>
    internal static void HandleHeaderRegistered(CRPCHeader header, GameObject owner, bool registered)
    {
        if (!registered) return;
        if (header == null || owner == null) return;
        if (SafeGetComponent<FriendlyTroll>(owner) == null) return;

        HeaderBinding binding = EnsureBinding(header, owner);
        if (binding == null) return;

        int instanceId = SafeInstanceId(owner);
        if (instanceId == 0) return;
        if (!States.TryGetValue(instanceId, out TrollState state)) return;
        if (!IsSameGeneration(state, owner)) return;

        state.Binding = binding;
        if (state.Token != Guid.Empty) QueueSend(state);
    }

    /// <summary>CRPCHeader.Flush 后缀：header 被清空，槽位所有权随之作废。</summary>
    internal static void HandleHeaderFlushed(CRPCHeader header)
    {
        if (header == null) return;
        IntPtr pointer;
        try
        {
            pointer = header.Pointer;
        }
        catch (Exception e)
        {
            LogOnce("header-pointer", e);
            return;
        }

        if (BindingsByHeader.TryGetValue(pointer, out HeaderBinding binding)) DropBinding(binding);
    }

    /// <summary>自有 RPC 槽被调用（客户端侧）：只解读主机判定。</summary>
    private static void OnRemoteDecision(HeaderBinding binding)
    {
        try
        {
            if (binding == null) return;
            if (IsHostAuthority()) return; // 主机不接受任何客户端外观判定

            GameObject owner = binding.Owner;
            if (owner == null) return;
            CRPCHeader header = binding.Header;
            if (header != null)
            {
                GameObject referenced = null;
                try
                {
                    referenced = header.referencedGO;
                }
                catch (Exception e)
                {
                    LogOnce("header-go", e);
                    return;
                }
                if (referenced == null || !IsSameGameObject(referenced, owner)) return;
            }

            FriendlyTroll troll = SafeGetComponent<FriendlyTroll>(owner);
            if (troll == null) return;

            if (!TryReadTailFromByteBuffer(out HermesHeadwearCodec.Tail tail)) return; // 载荷非法：保持原状

            TrollState state = FindState(troll);
            if (state == null) state = CreateStateFor(troll);
            if (state == null) return;
            if (!TryAdoptHostDecision(state, tail)) return;
            ApplyVisual(troll, state);
        }
        catch (Exception e)
        {
            LogOnce("remote-decision", e);
        }
    }

    // ------------------------------------------------------------------ 决策与外观

    /// <summary>按独立配额累计发放0..43轮换头饰；默认每10只3只，旧收据不消耗配额。</summary>
    internal static int RollChoice()
    {
        if (!EffectiveHostEnabled()) return HermesHeadwearCodec.ChoiceNone;
        int chance = ReadChancePercent();
        if (chance <= 0) return HermesHeadwearCodec.ChoiceNone;
        return HermesHeadwearCycle.TryAssign(chance, out int choice) ? choice : HermesHeadwearCodec.ChoiceNone;
    }

    /// <summary>只读当前世代已显示的主机头饰；供伪装选敌规则使用，不创建收据或重抽。</summary>
    internal static bool HasDisguiseHeadwear(FriendlyTroll troll)
    {
        try
        {
            if (!IsHostAuthority() || !EffectiveHostEnabled() || troll == null) return false;
            GameObject owner = SafeGameObject(troll);
            if (owner == null || !States.TryGetValue(SafeInstanceId(owner), out TrollState state)) return false;
            return IsSameGeneration(state, owner) && state.Troll != null
                && state.Troll.Pointer == troll.Pointer && state.WorldPointer == CurrentWorldPointer()
                && state.Token != Guid.Empty && state.OpaqueMetadataJson == null && state.Sealed
                && state.HostEnabled && state.VisualApplied && state.Choice >= 0
                && state.Choice <= HermesHeadwearCodec.MaxChoice
                && HermesHeadwearVisuals.IsApplied(troll, state.Choice);
        }
        catch { return false; }
    }

#if HERMES_HEADWEAR_TEST
    internal static Func<int, int, int> SampleOverride;
#endif
    private static int SampleCosmeticInt(int minimum, int maximum)
    {
#if HERMES_HEADWEAR_TEST
        if (SampleOverride != null) return SampleOverride(minimum, maximum);
#endif
        // Cosmetic decisions must not consume the game's combat/spawn RNG stream.
        return System.Security.Cryptography.RandomNumberGenerator.GetInt32(minimum, maximum);
    }

    /// <summary>同世代 token + 单调 revision 才采纳；跨世代/已退役 token 一律不覆盖。</summary>
    internal static bool TryAdoptHostDecision(TrollState state, HermesHeadwearCodec.Tail tail)
    {
        if (state == null) return false;
        if (tail.Token == Guid.Empty) return false;
        if (IsTokenRetired(state.InstanceId, tail.Token)) return false;

        if (state.Token != Guid.Empty && state.Token != tail.Token)
        {
            if (state.Sealed) return false;
        }
        else if (state.Token == tail.Token && tail.Revision < state.Revision)
        {
            return false;
        }

        state.Token = tail.Token;
        state.Choice = tail.Choice;
        state.HostEnabled = tail.HostEnabled;
        state.Revision = tail.Revision;
        state.Sealed = true;
        state.VisualRetryDelay = VisualRetryInitialSeconds;
        state.OpaqueMetadataJson = null; // 主机判定取代旧的未知 schema 记录
        ClearRetiredToken(state.InstanceId);
        return true;
    }

    private static void ApplyVisual(FriendlyTroll troll, TrollState state)
    {
        if (troll == null || state == null) return;
        // 未知 schema 记录：不是我们的收据，不显示也不清理视觉层。
        if (state.OpaqueMetadataJson != null) return;

        bool wantHeadwear = state.HostEnabled
            && HermesHeadwearCodec.IsHeadwearChoice(state.Choice)
            && GlobalEnabled();

        if (!wantHeadwear)
        {
            HermesHeadwearDiagnostics.Removed(troll, state.Choice, "disabled-or-no-choice");
            // 关闭/未命中：不需要外观（也不需要重试），但收据与盘上元数据照旧保留。
            try
            {
                HermesHeadwearVisuals.Clear(troll);
            }
            catch (Exception e)
            {
                LogOnce("visual-clear", e);
            }
            state.VisualApplied = false;
            state.VisualRetryDelay = VisualRetryInitialSeconds;
            RemoveFromVisualQueue(state);
            return;
        }

        bool applied = false;
        try
        {
            HermesHeadwearDiagnostics.Decision(troll, state.Choice, state.HostEnabled);
            applied = HermesHeadwearVisuals.Apply(troll, state.Choice);
        }
        catch (Exception e)
        {
            LogOnce("visual-apply", e);
        }

        if (!applied)
            HermesHeadwearDiagnostics.Visual(troll, state.Choice, false, null, null, "apply-failed");
        if (applied)
        {
            state.VisualApplied = true;
            state.VisualRetryDelay = VisualRetryInitialSeconds;
            RemoveFromVisualQueue(state);
            return;
        }

        // 资源尚未就绪等失败：保持同一 choice 继续重试（1s→2s→…→上限 30s），
        // 永不永久放弃、永不重抽；visual 层自己的 Tick 也负责它持有的资源补齐。
        state.VisualApplied = false;
        state.NextApplyAt = Time.time + state.VisualRetryDelay;
        float nextDelay = state.VisualRetryDelay * 2f;
        state.VisualRetryDelay = nextDelay > VisualRetryBackoffCapSeconds
            ? VisualRetryBackoffCapSeconds
            : nextDelay;
        AddToVisualQueue(state);
    }

    private static void DetachVisual(TrollState state)
    {
        if (state == null) return;
        FriendlyTroll troll = state.Troll;
        if (troll != null)
        {
            try
            {
                HermesHeadwearVisuals.Clear(troll);
            }
            catch (Exception e)
            {
                LogOnce("visual-detach", e);
            }
        }
        state.VisualApplied = false;
        state.VisualRetryDelay = VisualRetryInitialSeconds;
        RemoveFromVisualQueue(state);
    }

    // ------------------------------------------------------------------ 队列

    private static void QueueSend(TrollState state)
    {
        if (state == null || state.Token == Guid.Empty) return;
        state.SendPending = true;
        state.SendFailures = 0;
        state.NextSendAt = 0f;
        if (state.InSendQueue) return;
        state.InSendQueue = true;
        SendQueue.Add(state);
    }

    private static void AddToVisualQueue(TrollState state)
    {
        if (state.InVisualQueue) return;
        state.InVisualQueue = true;
        VisualQueue.Add(state);
    }

    private static void RemoveFromVisualQueue(TrollState state)
    {
        if (!state.InVisualQueue) return;
        state.InVisualQueue = false;
        for (int i = VisualQueue.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(VisualQueue[i], state)) VisualQueue.RemoveAt(i);
        }
    }

    private static void ProcessVisualRetries(float now)
    {
        int attempts = 0;
        for (int i = VisualQueue.Count - 1; i >= 0; i--)
        {
            if (attempts >= MaxVisualAttemptsPerTick) return; // 每 Tick 上限：不对大量对象同帧做工

            TrollState state = VisualQueue[i];
            if (!state.InVisualQueue)
            {
                VisualQueue.RemoveAt(i);
                continue;
            }
            if (now < state.NextApplyAt) continue;

            FriendlyTroll troll = state.Troll;
            if (troll == null || SafeGameObject(troll) == null)
            {
                state.InVisualQueue = false;
                VisualQueue.RemoveAt(i);
                continue;
            }

            attempts++;
            ApplyVisual(troll, state);
        }
    }

    private static void ProcessSendQueue(float now)
    {
        for (int i = SendQueue.Count - 1; i >= 0; i--)
        {
            TrollState state = SendQueue[i];
            if (!state.SendPending)
            {
                state.InSendQueue = false;
                SendQueue.RemoveAt(i);
                continue;
            }
            if (now < state.NextSendAt) continue;

            SendOutcome outcome = TrySendDecision(state);
            if (outcome == SendOutcome.Sent)
            {
                state.SendPending = false;
                state.InSendQueue = false;
                SendQueue.RemoveAt(i);
                continue;
            }
            if (outcome == SendOutcome.Deferred)
            {
                state.NextSendAt = now + SendRetryIntervalSeconds;
                continue;
            }

            state.SendFailures++;
            if (state.SendFailures >= MaxSendFailures)
            {
                state.SendPending = false;
                state.InSendQueue = false;
                SendQueue.RemoveAt(i);
                LogOnce("send-exhausted", null);
                continue;
            }
            state.NextSendAt = now + SendRetryIntervalSeconds;
        }
    }

    private static SendOutcome TrySendDecision(TrollState state)
    {
        if (state == null) return SendOutcome.Sent;
        if (state.Token == Guid.Empty) return SendOutcome.Sent;
        if (!IsHostAuthority()) return SendOutcome.Deferred; // 非主机不发
        // 原生 FinalGrab 可能排在 PoolSpawn 之前，不能依赖 CallMethodRemotely 的 catchup 顺序：
        // 只有对端已在线、已出现且已追平才发送，其余阶段保留待发（catchup tail 保真）。
        if (!IsOnline() || !IsClientPresent() || !HasClientCaughtUp()) return SendOutcome.Deferred;

        FriendlyTroll troll = state.Troll;
        if (troll == null) return SendOutcome.Sent;
        GameObject owner = SafeGameObject(troll);
        if (owner == null) return SendOutcome.Sent;

        HeaderBinding binding = state.Binding;
        if (binding == null || binding.Header == null)
        {
            binding = EnsureBindingForOwner(owner);
            if (binding == null) return SendOutcome.Deferred; // header 尚未注册：Tick 顺延
            state.Binding = binding;
        }
        if (!IsBindingUsable(binding, owner)) return SendOutcome.Deferred;

        if (!VerifyBindingSlot(binding))
        {
            // 槽位被回收/表被重排：重追加到末尾，并且**显式换成新 slot + 新 binding**，
            // 绝不用旧 SlotIndex 调原生方法（旧槽可能已经属于别的 RPC）。
            binding = ReappendBinding(binding);
            if (binding == null) return SendOutcome.Failed;
            state.Binding = binding;
            if (!IsBindingUsable(binding, owner) || !VerifyBindingSlot(binding)) return SendOutcome.Failed;
        }

        try
        {
            ByteBuffer.PrepWriteBuffer();
            WriteTailToByteBuffer(state.Token, state.Choice, state.HostEnabled, state.Revision);
            binding.Header.CallMethodRemotely(binding.SlotIndex);
            state.SendFailures = 0;
            return SendOutcome.Sent;
        }
        catch (Exception e)
        {
            LogOnce("send", e);
            return SendOutcome.Failed;
        }
    }

    /// <summary>
    /// 对端追平瞬间：把当前仍有效的收据全部重排一次待发（含此前已发送过的），
    /// 让重连/后期加入后的客户端拿回稳定外观；不重新抽选、不改 token/choice。
    /// </summary>
    private static void UpdateSendGate()
    {
        bool open = IsHostAuthority() && IsOnline() && IsClientPresent() && HasClientCaughtUp();
        if (open && !_sendGateOpen) RequeueAllDecisions();
        _sendGateOpen = open;
    }

    private static void RequeueAllDecisions()
    {
        if (States.Count == 0) return;
        foreach (TrollState state in States.Values)
        {
            if (state.Token == Guid.Empty) continue;
            QueueSend(state);
        }
    }

    // ------------------------------------------------------------------ 配置

    private static bool GlobalEnabled()
    {
        try
        {
            var entry = ModConfig.Enabled;
            return entry == null || entry.Value;
        }
        catch (Exception e)
        {
            LogOnce("config-global", e);
            return false;
        }
    }

    private static bool HeadwearEnabled()
    {
        try
        {
            var entry = ModConfig.HermesHeadwearEnabled;
            return entry == null || entry.Value;
        }
        catch (Exception e)
        {
            LogOnce("config-headwear", e);
            return false;
        }
    }

    private static int ReadChancePercent()
    {
        int chance = DefaultChancePercent;
        try
        {
            var entry = ModConfig.HermesHeadwearChancePercent;
            if (entry != null) chance = entry.Value;
        }
        catch (Exception e)
        {
            LogOnce("config-chance", e);
        }
        if (chance < 0) return 0;
        if (chance > 100) return 100;
        return chance;
    }

    private static bool EffectiveHostEnabled()
    {
        return GlobalEnabled() && HeadwearEnabled();
    }

    /// <summary>
    /// 配置收敛：本地总开关变化 → 只重放/清除本地外观；主机功能开关变化 →
    /// 同时推进 revision 并重发（客户端据此清除），任何情况下都不重抽。
    /// 标记只由 <see cref="OnSettingsChanged"/> 置位，实际收敛只在主线程 Tick 里做。
    /// </summary>
    private static void ReconcileConfig()
    {
        bool forced = _settingsDirty;
        if (forced) _settingsDirty = false;

        bool global = GlobalEnabled();
        bool effective = global && HeadwearEnabled();
        bool globalChanged = global != _lastGlobalEnabled;
        bool effectiveChanged = effective != _lastEffectiveEnabled;
        if (!forced && !globalChanged && !effectiveChanged) return;

        bool authority = IsHostAuthority();
        if (States.Count > 0)
        {
            foreach (TrollState state in States.Values)
            {
                if (authority && (effectiveChanged || forced))
                {
                    state.HostEnabled = effective;
                    state.Revision = state.Revision < 1 ? 1 : state.Revision + 1;
                    state.VisualRetryDelay = VisualRetryInitialSeconds;
                    if (state.Token != Guid.Empty) QueueSend(state);
                }

                FriendlyTroll troll = state.Troll;
                if (troll != null && (globalChanged || effectiveChanged || forced))
                {
                    ApplyVisual(troll, state);
                }
            }
        }

        _lastGlobalEnabled = global;
        _lastEffectiveEnabled = effective;
    }

    // ------------------------------------------------------------------ 生命周期与清理

    private static void Sweep(float now)
    {
        IntPtr world = CurrentWorldPointer();
        if (world != _worldPointer)
        {
            _worldPointer = world;
            bool anyInCurrentWorld = false;
            foreach (TrollState candidate in States.Values)
            {
                if (candidate.WorldPointer == world)
                {
                    anyInCurrentWorld = true;
                    break;
                }
            }
            // 只有“所有世代都属于旧世界”才是真的换世界；读档新建的世代属于当前世界，
            // 绝不在这里被误清（世界指针本身也可能在加载期间才刚变化）。
            if (!anyInCurrentWorld) ReleaseAllStates();
        }

        if (States.Count > 0)
        {
            bool globalEnabled = GlobalEnabled();
            bool popping = IsPoppingObjectsToScene();
            SweepScratch.Clear();
            foreach (KeyValuePair<int, TrollState> pair in States)
            {
                TrollState state = pair.Value;
                FriendlyTroll troll = state.Troll;

                if (state.WorldPointer != IntPtr.Zero && world != IntPtr.Zero
                    && state.WorldPointer != world)
                {
                    SweepScratch.Add(pair.Key); // 上一世界的残留
                    continue;
                }

                // 原生建场景/读档窗口 + 刚绑定收据的保护窗口：对象可能还没挂回 gameLayer，
                // 此时不得淘汰（否则刚读入的收据会在内存里丢失，后续存档也就丢了元数据）。
                // 注意：保护窗口只挡“淘汰”，不挡下面的重新激活重放。
                bool protectedWindow = popping || now < state.ProtectUntil;
                if (!protectedWindow)
                {
                    bool live;
                    try
                    {
                        live = troll != null && SafeGameObject(troll) != null
                            && GreekBankScope.IsInCurrentLayer(troll);
                    }
                    catch (Exception e)
                    {
                        LogOnce("sweep-live", e);
                        live = false;
                    }
                    if (!live)
                    {
                        SweepScratch.Add(pair.Key);
                        continue;
                    }
                }

                // 失活/暂停后重新活跃、或显示被抹掉（root/sprite 被销毁）：收据不变，只把外观补回去。
                if (state.VisualApplied && troll != null && state.HostEnabled && globalEnabled
                    && HermesHeadwearCodec.IsHeadwearChoice(state.Choice))
                {
                    bool stillApplied = true;
                    try
                    {
                        stillApplied = HermesHeadwearVisuals.IsApplied(troll, state.Choice);
                    }
                    catch (Exception e)
                    {
                        LogOnce("visual-is-applied", e);
                    }
                    if (!stillApplied)
                    {
                        state.VisualApplied = false;
                        state.VisualRetryDelay = VisualRetryInitialSeconds;
                    }
                }

                if (state.VisualApplied || state.InVisualQueue) continue;
                if (!state.HostEnabled || !globalEnabled
                    || !HermesHeadwearCodec.IsHeadwearChoice(state.Choice))
                    continue;
                bool active;
                try
                {
                    GameObject owner = SafeGameObject(troll);
                    active = owner != null && owner.activeInHierarchy;
                }
                catch (Exception e)
                {
                    LogOnce("sweep-active", e);
                    active = false;
                }
                if (!active) continue;

                state.NextApplyAt = now;
                AddToVisualQueue(state);
            }
            for (int i = 0; i < SweepScratch.Count; i++)
            {
                if (States.TryGetValue(SweepScratch[i], out TrollState stale))
                {
                    // 旧世界/已死对象也要撤掉自有外观，否则换世界后残影会常驻视觉层。
                    HermesHeadwearDiagnostics.Removed(stale.Troll, stale.Choice, "sweep-world-or-layer");
                    DropState(stale, clearVisual: true);
                }
            }
        }

        PruneBindings(now);
        SweepRetiredTokens(now);
    }

    /// <summary>
    /// header 绑定只在“原生真的不用这个 header 了”时释放：owner 已销毁，或它已不是该对象的
    /// 当前 header（已注销/重注册过）。**换世界不释放**——原生 header 与其槽位都还在，
    /// 丢掉记录会导致重复追加 slot 并让主机/客户端 RPC 索引分叉。
    /// </summary>
    private static void PruneBindings(float now)
    {
        if (BindingsByHeader.Count == 0) return;

        SweepBindingsScratch.Clear();
        foreach (KeyValuePair<IntPtr, HeaderBinding> pair in BindingsByHeader)
        {
            HeaderBinding binding = pair.Value;
            if (binding == null || binding.Header == null)
            {
                SweepBindingsScratch.Add(pair.Key);
                continue;
            }
            if (now - binding.CreatedAt < BindingPruneGraceSeconds) continue; // 注册进行中的保护期
            if (IsDeadGameObject(binding.Owner) || !IsCurrentHeader(binding))
            {
                SweepBindingsScratch.Add(pair.Key);
            }
        }
        for (int i = 0; i < SweepBindingsScratch.Count; i++)
        {
            if (BindingsByHeader.TryGetValue(SweepBindingsScratch[i], out HeaderBinding stale))
            {
                DropBinding(stale);
            }
        }
    }

    /// <summary>Unity/IL2CPP 语义：已销毁对象上访问组件/transform 返回 null。</summary>
    private static bool IsDeadGameObject(GameObject gameObject)
    {
        if (gameObject == null) return true;
        try
        {
            return gameObject.transform == null;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// 绑定是否仍指向该 owner 当前注册的 header：
    /// - 查到 header：必须就是我们的那个；
    /// - 查不到（已注销/池内失活）：只有当我们记的 header 也不再引用该 owner 时才判定失效，
    ///   以免把“池中失活但 header 仍在”的对象误判成 stale（会造成重复追加槽位）。
    /// - 查询异常：保守保留。
    /// </summary>
    private static bool IsCurrentHeader(HeaderBinding binding)
    {
        NetworkPostbox postbox;
        try
        {
            postbox = NetworkPostbox.Instance;
        }
        catch (Exception e)
        {
            LogOnce("postbox", e);
            return true;
        }
        if (postbox == null) return true;

        try
        {
            CRPCHeader current = postbox.GetHeaderFromObject(binding.Owner, false);
            if (current != null) return current.Pointer == binding.HeaderPointer;

            GameObject referenced;
            try
            {
                referenced = binding.Header.referencedGO;
            }
            catch (Exception e)
            {
                LogOnce("header-go", e);
                return true;
            }
            return referenced != null && IsSameGameObject(referenced, binding.Owner);
        }
        catch (Exception e)
        {
            LogOnce("header-lookup", e);
            return true;
        }
    }

    private static void SweepRetiredTokens(float now)
    {
        if (RetiredTokens.Count == 0) return;
        SweepScratch.Clear();
        foreach (KeyValuePair<int, RetiredToken> pair in RetiredTokens)
        {
            if (pair.Value.ExpireAt <= now) SweepScratch.Add(pair.Key);
        }
        for (int i = 0; i < SweepScratch.Count; i++) RetiredTokens.Remove(SweepScratch[i]);
    }

    private static IntPtr CurrentWorldPointer()
    {
        try
        {
            Managers managers = Managers.Inst;
            World world = managers != null ? managers.world : null;
            return world != null ? world.Pointer : IntPtr.Zero;
        }
        catch (Exception e)
        {
            LogOnce("world-pointer", e);
            return _worldPointer;
        }
    }

    /// <summary>
    /// 换世界/换层/换场景：只释放自有世代、队列与视觉层。
    /// **不动 header 绑定**——原生没有 Flush 这些 header，槽位与委托都还在，
    /// 丢掉记录会在复用后重复追加槽位、让主机/客户端 RPC 索引分叉。
    /// </summary>
    private static void ReleaseAllStates()
    {
        if (States.Count == 0 && SendQueue.Count == 0 && VisualQueue.Count == 0)
        {
            return;
        }

        try
        {
            HermesHeadwearVisuals.ClearAll();
        }
        catch (Exception e)
        {
            LogOnce("visual-clear-all", e);
        }

        foreach (TrollState state in States.Values)
        {
            HermesHeadwearDiagnostics.Removed(state.Troll, state.Choice, "release-all");
            state.InSendQueue = false;
            state.InVisualQueue = false;
            state.SendPending = false;
            state.Binding = null;
            state.Troll = null;
        }
        States.Clear();
        SendQueue.Clear();
        VisualQueue.Clear();
    }

    /// <summary>
    /// 进程/上下文边界（测试隔离、真正的整场重载）用：连 header 槽所有权与调度都清空，
    /// 因为此时原生 header 集合整体消失。换世界**不要**走这里（见 <see cref="ReleaseAllStates"/>）。
    /// </summary>
    internal static void ResetForProcessBoundary()
    {
        ReleaseAllStates();
        BindingsByHeader.Clear();
        BindingsByOwner.Clear();
        RetiredTokens.Clear();
        SendQueue.Clear();
        VisualQueue.Clear();
        _saveCapture = null;
        _sendGateOpen = false;
        _settingsDirty = false;
        _nextSweepAt = 0f;
    }

    private static void DropState(TrollState state, bool clearVisual)
    {
        if (state == null) return;
        if (clearVisual) DetachVisual(state);
        RetireState(state);
        RemoveState(state);
    }

    private static void RetireState(TrollState state)
    {
        if (state == null || state.Token == Guid.Empty) return;
        if (state.InstanceId == 0) return;
        RetiredTokens[state.InstanceId] = new RetiredToken
        {
            Token = state.Token,
            ExpireAt = Time.time + RetiredTokenTtlSeconds,
        };
    }

    private static void RemoveState(TrollState state)
    {
        if (state == null) return;
        state.SendPending = false;
        if (state.InSendQueue)
        {
            state.InSendQueue = false;
            for (int i = SendQueue.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(SendQueue[i], state)) SendQueue.RemoveAt(i);
            }
        }
        RemoveFromVisualQueue(state);
        if (state.InstanceId != 0 && States.TryGetValue(state.InstanceId, out TrollState current)
            && ReferenceEquals(current, state))
        {
            States.Remove(state.InstanceId);
        }
    }

    private static bool IsTokenRetired(int instanceId, Guid token)
    {
        return instanceId != 0 && RetiredTokens.TryGetValue(instanceId, out RetiredToken retired)
            && retired.Token == token;
    }

    private static void ClearRetiredToken(int instanceId)
    {
        if (instanceId != 0) RetiredTokens.Remove(instanceId);
    }

    // ------------------------------------------------------------------ 世代映射

    private static TrollState CreateStateFor(FriendlyTroll troll)
    {
        if (troll == null) return null;
        GameObject owner = SafeGameObject(troll);
        if (owner == null) return null;
        int instanceId = SafeInstanceId(owner);
        if (instanceId == 0) return null;
        return CreateState(troll, owner, instanceId);
    }

    private static TrollState CreateStateFor(GameObject owner, int instanceId)
    {
        if (owner == null || instanceId == 0) return null;
        FriendlyTroll troll = SafeGetComponent<FriendlyTroll>(owner);
        return troll != null ? CreateState(troll, owner, instanceId) : null;
    }

    private static TrollState CreateState(FriendlyTroll troll, GameObject owner, int instanceId)
    {
        TrollState state = new TrollState
        {
            Troll = troll,
            InstanceId = instanceId,
            GameObjectPointer = SafePointer(owner),
            WorldPointer = CurrentWorldPointer(),
        };
        States[instanceId] = state;
        return state;
    }

    private static TrollState FindState(FriendlyTroll troll)
    {
        if (troll == null) return null;
        GameObject owner = SafeGameObject(troll);
        if (owner == null) return null;
        int instanceId = SafeInstanceId(owner);
        if (instanceId == 0) return null;
        if (!States.TryGetValue(instanceId, out TrollState state)) return null;
        if (!IsSameGeneration(state, owner)) return null;
        state.Troll = troll;
        return state;
    }

    private static bool IsSameGeneration(TrollState state, GameObject owner)
    {
        if (state == null || owner == null) return false;
        IntPtr pointer = SafePointer(owner);
        return state.GameObjectPointer != IntPtr.Zero && state.GameObjectPointer == pointer;
    }

    private static bool IsSameGameObject(GameObject left, GameObject right)
    {
        if (left == null || right == null) return false;
        IntPtr leftPointer = SafePointer(left);
        IntPtr rightPointer = SafePointer(right);
        return leftPointer != IntPtr.Zero && leftPointer == rightPointer;
    }

    // ------------------------------------------------------------------ header 绑定

    private static HeaderBinding EnsureBindingForOwner(GameObject owner)
    {
        if (owner == null) return null;
        int instanceId = SafeInstanceId(owner);
        if (instanceId == 0) return null;

        if (BindingsByOwner.TryGetValue(instanceId, out HeaderBinding existing)
            && IsBindingUsable(existing, owner))
        {
            return existing;
        }

        NetworkPostbox postbox;
        try
        {
            postbox = NetworkPostbox.Instance;
        }
        catch (Exception e)
        {
            LogOnce("postbox", e);
            return null;
        }
        if (postbox == null) return null;

        CRPCHeader header;
        try
        {
            header = postbox.GetHeaderFromObject(owner, false);
        }
        catch (Exception e)
        {
            LogOnce("header-lookup", e);
            return null;
        }
        if (header == null) return null;
        return EnsureBinding(header, owner);
    }

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

        int priorAttempts = 0;
        TrollState ownerState = null;
        bool stateReferenced = false;
        if (BindingsByHeader.TryGetValue(headerPointer, out HeaderBinding existing))
        {
            if (IsBindingUsable(existing, owner)) return existing;
            priorAttempts = existing.AppendAttempts;
            stateReferenced = States.TryGetValue(instanceId, out ownerState)
                && ReferenceEquals(ownerState.Binding, existing);
            DropBinding(existing);
        }
        if (BindingsByOwner.TryGetValue(instanceId, out HeaderBinding byOwner))
        {
            if (byOwner.AppendAttempts > priorAttempts) priorAttempts = byOwner.AppendAttempts;
            if (!stateReferenced)
            {
                stateReferenced = States.TryGetValue(instanceId, out ownerState)
                    && ReferenceEquals(ownerState.Binding, byOwner);
            }
            DropBinding(byOwner);
        }
        if (priorAttempts >= MaxBindingAppendsPerHeader) return null; // 有界：不在同一 header 上反复追加

        HeaderBinding binding = AppendBinding(header, headerPointer, owner, instanceId);
        if (binding == null) return null;
        binding.AppendAttempts = priorAttempts + 1;
        if (stateReferenced && ownerState != null) ownerState.Binding = binding;
        return binding;
    }

    /// <summary>
    /// 追加自有 RPC 槽：**始终追加到原生 RemoteMethodList 末尾**，绝不复用原生空槽
    /// （复用会让主机/客户端索引分叉并打乱原生 RPC 编号）。function id 只有一个字节，
    /// 列表达到 <see cref="MaxRpcSlots"/> 时拒绝注册（宁可不发，也不越界调用）。
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

        binding.ManagedDelegate = new Action(() => OnRemoteDecision(binding));
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
        binding.CreatedAt = Time.time;
        BindingsByHeader[headerPointer] = binding;
        BindingsByOwner[instanceId] = binding;
        return binding;
    }

    /// <summary>槽位仍在原生列表原处、且仍是同一个委托对象，才算有效。</summary>
    private static bool VerifyBindingSlot(HeaderBinding binding)
    {
        if (binding == null || binding.Header == null || binding.SlotIndex < 0) return false;
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

    /// <summary>
    /// 槽位身份失效时重追加：返回**替换后的 binding**（调用方必须用它替换本地/state 引用）。
    /// 旧槽位从此不再被本核心使用——即便原生把它回收给了别的 RPC，也绝不会误调。
    /// </summary>
    private static HeaderBinding ReappendBinding(HeaderBinding binding)
    {
        if (binding == null || binding.Header == null || binding.Owner == null) return null;
        if (binding.AppendAttempts >= MaxBindingAppendsPerHeader) return null;

        CRPCHeader header = binding.Header;
        GameObject owner = binding.Owner;
        IntPtr headerPointer = binding.HeaderPointer;
        int ownerId = binding.OwnerId;
        int attempts = binding.AppendAttempts;

        // DropBinding 会把 state.Binding 置 null，所以要在它之前记下“state 是否引用旧 binding”。
        bool stateReferenced = States.TryGetValue(ownerId, out TrollState state)
            && ReferenceEquals(state.Binding, binding);

        DropBinding(binding);
        HeaderBinding replacement = AppendBinding(header, headerPointer, owner, ownerId);
        if (replacement == null) return null;
        replacement.AppendAttempts = attempts + 1;
        if (stateReferenced && state != null) state.Binding = replacement;
        return replacement;
    }

    private static bool IsBindingUsable(HeaderBinding binding, GameObject owner)
    {
        if (binding == null || binding.Header == null || binding.Owner == null) return false;
        if (!IsSameGameObject(binding.Owner, owner)) return false;
        if (!IsCurrentHeader(binding)) return false; // 已注销/已被新 header 取代：必须重绑定

        GameObject referenced;
        try
        {
            referenced = binding.Header.referencedGO;
        }
        catch (Exception e)
        {
            LogOnce("header-go", e);
            return false;
        }
        return referenced != null && IsSameGameObject(referenced, owner);
    }

    private static void DropBinding(HeaderBinding binding)
    {
        if (binding == null) return;
        if (binding.HeaderPointer != IntPtr.Zero) BindingsByHeader.Remove(binding.HeaderPointer);
        if (binding.OwnerId != 0 && BindingsByOwner.TryGetValue(binding.OwnerId, out HeaderBinding current)
            && ReferenceEquals(current, binding))
        {
            BindingsByOwner.Remove(binding.OwnerId);
        }

        if (States.TryGetValue(binding.OwnerId, out TrollState state) && ReferenceEquals(state.Binding, binding))
        {
            state.Binding = null;
        }
    }

    // ------------------------------------------------------------------ 尾巴 I/O

    /// <summary>
    /// 只读地验证并解析尾巴：先用 <c>bufferAccess</c> 把候选字节拷进 scratch 校验
    /// （magic/版本/长度/choice/token），只有整体合法才推进 ByteBuffer 游标；
    /// magic 不符、未知 schema、超长或越界时游标保持不动、原生 3 字段不动、
    /// 客户端也不会因此抽选。绝不使用 internalBuffer 长度当作 payload 长度。
    /// </summary>
    private static bool TryReadTailFromByteBuffer(out HermesHeadwearCodec.Tail tail)
    {
        tail = default(HermesHeadwearCodec.Tail);
        int available = ByteBuffer.PollDataAvailableLength();
        if (available < HermesHeadwearCodec.TailBytes) return false;

        int index = ByteBuffer.PollIndex();
        if (index < 0) return false;

        for (int i = 0; i < HermesHeadwearCodec.TailBytes; i++) TailScratch[i] = ByteBuffer.bufferAccess(index + i);
        if (!HermesHeadwearCodec.TryReadTail(TailScratch, out tail)) return false;

        // 校验通过后才消费：恰好推进 TailBytes，落点可预测。
        for (int i = 0; i < HermesHeadwearCodec.TailBytes; i++) ByteBuffer.ReadByte();
        return true;
    }

    private static void WriteTailToByteBuffer(Guid token, int choice, bool hostEnabled, int revision)
    {
        if (!HermesHeadwearCodec.WriteTail(TailScratch, token, choice, hostEnabled, revision)) return;
        for (int i = 0; i < TailScratch.Length; i++) ByteBuffer.Write(TailScratch[i]);
    }

    // ------------------------------------------------------------------ 原生薄封装

    private static bool IsHostAuthority()
    {
        try
        {
            return NetworkBigBoss.HasWorldAuth;
        }
        catch (Exception e)
        {
            LogOnce("authority", e);
            return false;
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

    /// <summary>原生读档/建场景窗口：此时对象可能还没挂回 gameLayer，扫描不得据此淘汰刚读入的收据。</summary>
    private static bool IsPoppingObjectsToScene()
    {
        try
        {
            return IslandSaveData.poppingObjectsToScene;
        }
        catch (Exception e)
        {
            LogOnce("popping-objects", e);
            return false;
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

    /// <summary>精确判据：组件名 FriendlyTroll + 数据类型 FriendlyTrollData（排除 IRPCData 网络数据流）。</summary>
    private static bool IsFriendlyTrollDataComponent(string name, string type)
    {
        return string.Equals(name, "FriendlyTroll", StringComparison.Ordinal)
            && string.Equals(type, "FriendlyTrollData", StringComparison.Ordinal);
    }

    private static HermesHeadwearCodec.ReceiptInfo FindFriendlyTrollReceipt(IslandSaveData.ObjectData objectData)
    {
        var components = objectData.componentData2;
        if (components == null) return HermesHeadwearCodec.ReceiptInfo.None;
        for (int i = 0; i < components.Count; i++)
        {
            var component = components[i];
            if (component == null) continue;
            if (!IsFriendlyTrollDataComponent(component.name, component.type)) continue;
            return HermesHeadwearCodec.ReadComponentMetadata(component.data);
        }
        return HermesHeadwearCodec.ReceiptInfo.None;
    }

    /// <summary>精确 FriendlyTrollData 组件里 <c>kemHermesHeadwear</c> 的原始值文本（未知 schema 原样保留用）。</summary>
    private static string FindFriendlyTrollRawMetadata(IslandSaveData.ObjectData objectData)
    {
        var components = objectData.componentData2;
        if (components == null) return null;
        for (int i = 0; i < components.Count; i++)
        {
            var component = components[i];
            if (component == null) continue;
            if (!IsFriendlyTrollDataComponent(component.name, component.type)) continue;
            return HermesHeadwearCodec.ExtractMetadataValue(component.data);
        }
        return null;
    }

    private static void LogOnce(string key, Exception e)
    {
        if (!LoggedErrors.Add(key)) return;
        try
        {
            string message = "[HermesHeadwear] " + key;
            if (e != null) message += ": " + e.GetType().Name + " " + e.Message;
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(message);
        }
        catch
        {
            // 日志失败不影响功能。
        }
    }

    // ------------------------------------------------------------------ 原生包装类（显式独立；主类本身不带 Harmony 特性）

    [HarmonyPatch(typeof(HermesStaff._StartAbilityRoutine_d__17), "MoveNext")]
    private static class HermesConversionContextPatch
    {
        [HarmonyPrefix]
        private static void DepthEnter()
        {
            EnterConversionContext();
        }

        [HarmonyFinalizer]
        private static Exception DepthLeave(Exception __exception)
        {
            ExitConversionContext();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(FriendlyTroll), "SpawnMask")]
    private static class FriendlyTrollNativeMaskPatch
    {
        [HarmonyPostfix]
        private static void AfterNativeMask(FriendlyTroll __instance)
        {
            try { HermesHeadwearVisuals.OnNativeMaskSpawned(__instance); }
            catch (Exception e) { LogOnce("native-mask-notify", e); }
        }
    }

    [HarmonyPatch(typeof(FriendlyTroll), nameof(FriendlyTroll.Init))]
    private static class FriendlyTrollInitPatch
    {
        [HarmonyPostfix]
        private static void AfterInit(FriendlyTroll __instance)
        {
            try
            {
                HandleNativeInit(__instance);
            }
            catch (Exception e)
            {
                LogOnce("init", e);
            }
        }
    }

    [HarmonyPatch(typeof(FriendlyTroll), nameof(FriendlyTroll.ApplyData))]
    private static class FriendlyTrollApplyDataPatch
    {
        [HarmonyPostfix]
        private static void AfterApplyData(FriendlyTroll __instance)
        {
            try
            {
                HandleNativeApplyData(__instance);
            }
            catch (Exception e)
            {
                LogOnce("apply-data", e);
            }
        }
    }

    [HarmonyPatch(typeof(FriendlyTroll), nameof(FriendlyTroll.DeserializeFromData))]
    private static class FriendlyTrollDeserializePatch
    {
        [HarmonyPostfix]
        private static void AfterDeserialize(FriendlyTroll __instance)
        {
            try
            {
                HandleNativeDeserialize(__instance);
            }
            catch (Exception e)
            {
                LogOnce("deserialize", e);
            }
        }
    }

    [HarmonyPatch(typeof(FriendlyTroll), nameof(FriendlyTroll.GetSerializationData))]
    private static class FriendlyTrollSerializationPatch
    {
        [HarmonyPostfix]
        private static void AfterSerialization(FriendlyTroll __instance)
        {
            try
            {
                HandleNativeSerialization(__instance);
            }
            catch (Exception e)
            {
                LogOnce("serialize", e);
            }
        }
    }

    [HarmonyPatch(typeof(FriendlyTroll), nameof(FriendlyTroll.ResetAndDespawn))]
    private static class FriendlyTrollResetPatch
    {
        [HarmonyPrefix]
        private static void BeforeResetAndDespawn(FriendlyTroll __instance)
        {
            try
            {
                HandleNativeResetAndDespawn(__instance);
            }
            catch (Exception e)
            {
                LogOnce("reset-despawn", e);
            }
        }
    }

    [HarmonyPatch(typeof(Pool), nameof(Pool.FastSpawn))]
    private static class PoolFastSpawnPatch
    {
        [HarmonyPostfix]
        private static void AfterFastSpawn(GameObject __result)
        {
            try
            {
                HandlePoolSpawn(__result);
            }
            catch (Exception e)
            {
                LogOnce("pool-spawn", e);
            }
        }
    }

    [HarmonyPatch(typeof(Persistent), nameof(Persistent.OnDisable))]
    private static class PersistentOnDisablePatch
    {
        [HarmonyPrefix]
        private static void BeforeOnDisable(Persistent __instance)
        {
            try
            {
                HandlePersistentDisable(__instance);
            }
            catch (Exception e)
            {
                LogOnce("persistent-disable", e);
            }
        }
    }

    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.Save),
        new[] { typeof(int), typeof(int), typeof(int) })]
    private static class IslandSaveDataSavePatch
    {
        [HarmonyPrefix]
        private static void BeforeSave(out SaveCapture __state)
        {
            __state = BeginSaveCapture();
        }

        [HarmonyPostfix]
        private static void AfterSave(SaveCapture __state)
        {
            try
            {
                ApplySaveCapture(__state);
            }
            catch (Exception e)
            {
                LogOnce("save-inject", e);
            }
        }

        [HarmonyFinalizer]
        private static Exception FinallySave(Exception __exception, SaveCapture __state)
        {
            return EndSaveCapture(__exception, __state);
        }
    }

    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.GetID), new[] { typeof(Persistent) })]
    private static class IslandSaveDataGetIdPatch
    {
        [HarmonyPostfix]
        private static void AfterGetId(Persistent __0, string __result)
        {
            try
            {
                HandleGetId(__0, __result);
            }
            catch (Exception e)
            {
                LogOnce("get-id", e);
            }
        }
    }

    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.TryCreateOrFind))]
    private static class TryCreateOrFindPatch
    {
        [HarmonyPostfix]
        private static void AfterTryCreateOrFind(IslandSaveData.ObjectData __0, ref Persistent __result)
        {
            try
            {
                HandleTryCreateOrFind(__0, __result);
            }
            catch (Exception e)
            {
                LogOnce("try-create-or-find", e);
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
                LogOnce("register-components", e);
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
                LogOnce("header-flush", e);
            }
        }
    }
}
