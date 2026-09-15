using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 散射额外箭的淡金色生命周期（combat 模块）：只写 runtime SpriteRenderer 实例色 + 复用现有 softsim 消息。
///
/// 契约（本文件只依赖，不在此实现）：
/// - 资格判定在 <see cref="PatchArcher_Options"/>：只有它生成的额外箭才调用 <see cref="Apply"/>；主箭永不调用。
///   本模块绝不改 sharedMaterial / 原生伤害 / perfect / trail / 物理，也不新增扫描/驱动/RPC 槽。
/// - Arrow.OnEnable 前缀（既有 patch）无条件调用 <see cref="ResetArrow"/>：池复用/新生命前必须先归还旧色，
///   且不受 Scatter 开关限制（关闭只停止新增，已染色的额外箭保留到回收，不加关闭广播/RPC）。
/// - ModPanel.Update 每帧调用 <see cref="Tick"/>：只扫自有 ≤128 条回执，绝不全场扫描。
/// - 发送侧只用 <see cref="WriteInitialisePayload"/> 写额外箭 payload，之后仍走原生顺序
///   （ByteBuffer.PrepWriteBuffer(); softsim.SendVelocity(...)），不新增 RPC、不在 OnEnable 发送。
///
/// 颜色：written = lerp(base.rgb, 淡金 (1,.92,.68), 0.65)，alpha 保持 base.alpha；只写 renderer 实例色。
///
/// 回执状态机（每条回执 = native 身份 + base + written + world/layer/scene）：
/// - Tinted：写色已确认，live 期间保持；池回收/世界失效/OnEnable 时归还。
/// - PendingRestore：必须先归还（写色抛异常、Reset 失败、世界失效、池回收都进这个状态）。此后**只**尝试
///   恢复 base，绝不重染——初次写色失败时 Apply=false，发送侧不会带 tint 标记，重染会让 host/client 颜色分叉。
/// 归还用 CAS：当前色仍等于 written 才写回 base，外部已改色或已是 base 都只丢回执。身份 = arrowGOid +
/// GO指针 + arrow指针 + renderer指针；每次写色（染色与归还）前都复核回执原身份，读身份异常一律当
/// 「未知」保留回执 + 0.5s 退避，绝不当作已换对象/已销毁；确证换对象或 renderer 已销毁才退休。
/// 身份不符（非同 native 对象）绝不写新对象；同 GO/renderer 已有别的 arrow 组件回执时拒绝新建第二条。
///
/// 网络：原生 Arrow.ReceiveInitialise 严格检查剩余长度 ==1 后只读 1 个 bool，因此本模块把标记追加在
/// 原始 payload 之后（总长恒为 6）：[0]=perfect(0/1) [1..4]='S','C','T','1' [5]=version(1)。
/// 接收侧 Prefix 先无副作用 peek（PollDataAvailableLength + PollIndex + bufferAccess），形态全对才接管；
/// 接管在第一次移动游标前锁定（owned），此后任何 ReadByte/PerfectShot/Apply 异常都 return false，
/// 绝不再次运行原生。消费只用标准 ReadByte × 6（不直接写 ByteBuffer.index）。非 fireArrow 才按 perfect
/// 调 PerfectShot（fireArrow 学原生早退不 Perfect）；客户端（!HasWorldAuth）对这条有效自有额外箭染色，
/// 该路径绕过本地 Scatter 开关（主机决定有没有额外箭）但仍要求 master mod / world / arrow 当前 scope。
/// 其他任意 payload 不消费、原样交回原生；绝不把标记塞进 perfect bool（原生 ReadBool 把非零字节当 perfect）。
/// </summary>
internal static class ScatterArrowTint
{
    /// <summary>自有 payload 长度：[perfect][magic 'SCT1'][version]。</summary>
    internal const int OwnPayloadLength = 6;
    /// <summary>回执表硬上限：满时不染该箭（不删/挪任何箭，也不驱逐已有回执）。</summary>
    internal const int Capacity = 128;

    private const byte Version = 1;
    private const float GoldMix = 0.65f;
    private const float GoldR = 1f;
    private const float GoldG = 0.92f;
    private const float GoldB = 0.68f;
    /// <summary>颜色相等容差：只用于 CAS 判定与重复 Apply 判定，远小于任何有意改色。</summary>
    private const float ColorEpsilon = 1e-3f;
    /// <summary>读/写失败后的重试间隔，避免每帧或重复 Apply 绕过退避。</summary>
    private const float RetrySeconds = 0.5f;

    /// <summary>一条染色回执：native 身份 + 基准色/本次写色 + world/layer/scene + 状态。</summary>
    private sealed class TintEntry
    {
        internal IntPtr GoPtr;
        internal int GoId;
        internal IntPtr ArrowPtr;
        internal IntPtr RendererPtr;
        internal Arrow Arrow;
        internal SpriteRenderer Renderer;
        internal IntPtr WorldPtr;
        internal IntPtr LayerPtr;
        internal int SceneHandle;
        internal Color Base;
        internal Color Written;
        /// <summary>true = 必须先归还，此后只能恢复（绝不重染）；false = 已确认染色。</summary>
        internal bool PendingRestore;
        internal float NextRetry;
    }

    private static readonly TintEntry[] Slots = new TintEntry[Capacity];
    private static readonly HashSet<string> Logged = new HashSet<string>();

    private static void Fail(string key, string message)
    {
        try
        {
            if (Logged.Add(key)) KingdomEnhancedPlugin.Instance?.LogSource?.LogError("[ScatterTint] " + message);
        }
        catch (Exception) { }
    }

    /// <summary>当前在案回执数（含待归还），测试与日志用。</summary>
    internal static int TrackedCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < Capacity; i++) if (Slots[i] != null) count++;
            return count;
        }
    }

    // ============================================================
    // 一、染色（仅额外箭；资格由 PatchArcher_Options 决定）
    // ============================================================

    /// <summary>
    /// host 调用侧入口：要求本地 Scatter 开关 + master/scope 门；返回是否已由本模块染色
    /// （true 才应在 payload 里带 tint 标记）。
    /// </summary>
    internal static bool Apply(Arrow arrow) => ApplyAuthorized(arrow, false);

    /// <summary>
    /// 染色内部入口。<paramref name="bypassScatterSwitch"/> 只给接收侧（客机）用：主机决定有没有额外箭，
    /// 客机本地开关默认 false 也要显示金色；master mod / 当前 world / arrow 属于当前 scope 的守卫不减。
    /// </summary>
    private static bool ApplyAuthorized(Arrow arrow, bool bypassScatterSwitch)
    {
        try
        {
            if (!ArcherOptionsScope.IsActive) return false;                     // master：mod + biome scope
            if (!bypassScatterSwitch && !ScatterSwitchOn()) return false;       // host 调用侧开关
            if (arrow == null || arrow.gameObject == null) return false;
            if (!ArcherOptionsScope.IsCurrent(arrow)) return false;             // 只染当前 world/layer/scene 的箭

            IntPtr arrowPtr = arrow.Pointer;
            if (arrowPtr == IntPtr.Zero) return false;
            int goId;
            IntPtr goPtr;
            if (!TryGoIdentity(arrow.gameObject, out goPtr, out goId)) return false;

            IntPtr world;
            IntPtr layer;
            int scene;
            if (!WorldContext(out world, out layer, out scene)) return false;

            SpriteRenderer renderer = ResolveRenderer(arrow);
            if (renderer == null || renderer.gameObject == null) return false;
            IntPtr rendererPtr;
            try { rendererPtr = renderer.Pointer; }
            catch (Exception) { return false; }
            if (rendererPtr == IntPtr.Zero) return false;

            int slot = FindExact(goPtr, goId, arrowPtr, rendererPtr);
            if (slot >= 0)
            {
                TintEntry existing = Slots[slot];
                // 待归还：拒绝（不重读染色值当 base、不刷新身份、不清退避），只能等归还落地。
                if (existing.PendingRestore) return false;
                return !ExternallyChanged(existing);            // 已染色：仍是本模块颜色才回报 true，绝不重写
            }
            if (HasReceiptFor(goPtr, goId, rendererPtr))
            {
                // 同 GO/renderer 上已有别的 arrow 组件身份的回执：绝不认领它，也不新建第二条。
                Fail("conflict", "same GO/renderer already has a receipt owned by another arrow component; refused");
                return false;
            }

            slot = Allocate();
            if (slot < 0)
            {
                Fail("cap", "tint receipt table full (" + Capacity + "); extra arrow left untinted (no arrow moved)");
                return false;
            }

            Color baseColor;
            try { baseColor = renderer.color; }
            catch (Exception e) { Fail("read", "renderer color read failed: " + e); return false; }

            TintEntry entry = new TintEntry
            {
                GoPtr = goPtr,
                GoId = goId,
                ArrowPtr = arrowPtr,
                RendererPtr = rendererPtr,
                Arrow = arrow,
                Renderer = renderer,
                WorldPtr = world,
                LayerPtr = layer,
                SceneHandle = scene,
                Base = baseColor,
                Written = Mix(baseColor),
                PendingRestore = true,                          // 写色确认前一律待归还
                NextRetry = 0f,
            };
            Slots[slot] = entry;                                // 先登记回执，再写颜色
            return WriteTint(slot);
        }
        catch (Exception e)
        {
            Fail("apply", "tint apply failed: " + e);
            return false;
        }
    }

    /// <summary>
    /// OnEnable（池复用/新生命）前无条件归还本模块颜色：不受 Scatter 开关限制。先置 PendingRestore
    /// 再归还（失败后只能继续恢复）；只写回执里记录的那个 renderer；identity 读失败时按 arrow 指针兜底
    /// 匹配，绝不因此丢账。
    /// </summary>
    internal static void ResetArrow(Arrow arrow)
    {
        try
        {
            if (arrow == null || arrow.gameObject == null) return;
            IntPtr arrowPtr;
            try { arrowPtr = arrow.Pointer; }
            catch (Exception) { return; }

            int goId;
            IntPtr goPtr;
            bool haveIdentity = TryGoIdentity(arrow.gameObject, out goPtr, out goId);

            for (int i = 0; i < Capacity; i++)
            {
                TintEntry entry = Slots[i];
                if (entry == null) continue;
                bool match = haveIdentity
                    ? entry.GoId == goId && entry.GoPtr == goPtr
                    : arrowPtr != IntPtr.Zero && entry.ArrowPtr == arrowPtr;
                if (!match) continue;
                entry.PendingRestore = true;                    // 新生命之前必须先归还；之后只恢复不重染
                TryRestore(i);                                  // 失败自会保留回执 + 退避
            }
        }
        catch (Exception e) { Fail("reset", "tint reset failed: " + e); }
    }

    /// <summary>
    /// 面板每帧调用：只扫自有 ≤128 条回执。renderer 已销毁→丢弃；池回收（activeSelf=false）、
    /// 世界未知或 world/layer/scene 变化→置 PendingRestore 并归还；待归还回执只重试恢复，绝不重染。
    /// </summary>
    internal static void Tick()
    {
        try
        {
            float now = Now();
            bool hasWorld = WorldContext(out IntPtr world, out IntPtr layer, out int scene);
            for (int i = 0; i < Capacity; i++)
            {
                TintEntry entry = Slots[i];
                if (entry == null || entry.NextRetry > now) continue;

                if (entry.Renderer == null || entry.Renderer.gameObject == null)
                {
                    Slots[i] = null;                            // renderer 已销毁：无处可写
                    continue;
                }

                bool recycled;
                try { recycled = !entry.Renderer.gameObject.activeSelf; }
                catch (Exception) { recycled = false; }

                bool contextChanged = !hasWorld
                    || entry.WorldPtr != world || entry.LayerPtr != layer || entry.SceneHandle != scene;
                if (recycled || contextChanged || entry.PendingRestore)
                {
                    entry.PendingRestore = true;
                    TryRestore(i);                              // 失败保留回执 + 退避
                }
            }
        }
        catch (Exception e) { Fail("tick", "tint tick failed: " + e); }
    }

    // ============================================================
    // 二、网络 payload（复用现有 softsim 通道，不新增 RPC）
    // ============================================================

    /// <summary>
    /// 写额外箭初始化 payload：永远先写原生 perfect bool（1 字节，0/1）；tinted 时再追加 5 字节
    /// 自有标记（magic 'SCT1' + version），总 6 字节。调用方随后照原路径继续（本方法不 reset buffer、
    /// 不发送、不 prepend 运动信息；底层写失败直接向调用者抛出，由散射循环隔离——抛出后不会再发这条
    /// payload，绝不静默发出半截包）。
    /// </summary>
    internal static void WriteInitialisePayload(bool perfectShot, bool tinted)
    {
        ByteBuffer.Write(perfectShot);
        if (!tinted) return;
        ByteBuffer.Write((byte)'S');
        ByteBuffer.Write((byte)'C');
        ByteBuffer.Write((byte)'T');
        ByteBuffer.Write((byte)'1');
        ByteBuffer.Write(Version);
    }

    /// <summary>
    /// Arrow.ReceiveInitialise 前缀：只接管形态正确的自有 6 字节格式，其他一切 payload（含原生 1 字节）
    /// 不消费、原样交回原生。返回 true = 继续原生，false = 已接管（跳过原生，避免它对 6 字节报长度错误）。
    /// </summary>
    internal static bool OnReceiveInitialise(Arrow arrow)
    {
        bool owned = false;
        try
        {
            if (arrow == null) return true;

            bool perfect;
            if (!TryPeekOwnPayload(out perfect)) return true;    // 无副作用 peek 失败：原样交原生

            owned = true;                                        // 首次移动游标前锁定：之后绝不再交回原生
            for (int i = 0; i < OwnPayloadLength; i++) ByteBuffer.ReadByte();   // 只用标准读 API 消费 6 字节

            // 已接管：后续任何异常都隔离，绝不回退游标、绝不二次消费、绝不再次运行原生。
            // 非 fireArrow 才按 perfect 调 PerfectShot：fireArrow 遵循原生早退，不做 perfect。
            try { if (!arrow.isFireArrow && perfect) arrow.PerfectShot(); }
            catch (Exception e) { Fail("perfect", "own-payload PerfectShot failed: " + e); }

            try { if (!NetworkBigBoss.HasWorldAuth) ApplyAuthorized(arrow, true); }
            catch (Exception e) { Fail("client", "client tint apply failed: " + e); }

            return false;
        }
        catch (Exception e)
        {
            Fail("receive", "own-payload receive failed: " + e);
            return !owned;
        }
    }

    /// <summary>
    /// 无副作用 peek：剩余恰好 6、perfect 字节 ∈ {0,1}、magic/version 全对才算自有格式。
    /// 只读 PollDataAvailableLength / PollIndex / bufferAccess，绝不移动读游标。
    /// </summary>
    private static bool TryPeekOwnPayload(out bool perfect)
    {
        perfect = false;
        if (ByteBuffer.PollDataAvailableLength() != OwnPayloadLength) return false;
        int index = ByteBuffer.PollIndex();
        if (index < 0) return false;
        byte head = ByteBuffer.bufferAccess(index);
        if (head > 1) return false;                              // perfect 只允许 0/1（高 bit 不伪装 bool）
        if (ByteBuffer.bufferAccess(index + 1) != (byte)'S') return false;
        if (ByteBuffer.bufferAccess(index + 2) != (byte)'C') return false;
        if (ByteBuffer.bufferAccess(index + 3) != (byte)'T') return false;
        if (ByteBuffer.bufferAccess(index + 4) != (byte)'1') return false;
        if (ByteBuffer.bufferAccess(index + 5) != Version) return false;
        perfect = head == 1;
        return true;
    }

    // ============================================================
    // 三、颜色写入 / 归还（CAS）
    // ============================================================

    /// <summary>
    /// 本次唯一一次写色（绝不用于重试重染）：先复核回执原身份，当前仍是 base 才写 written；
    /// 当前已是 written 视为已落地；其他值（外部已改色）放弃回执且不覆盖。
    /// 任何读/写异常 → 回执转 PendingRestore + 0.5s 退避，返回 false（发送侧不带 tint 标记）。
    /// </summary>
    private static bool WriteTint(int slot)
    {
        TintEntry entry = Slots[slot];
        OwnerCheck owner = CheckOwner(entry);
        if (owner == OwnerCheck.Different) { Slots[slot] = null; return false; }
        if (owner == OwnerCheck.Unknown) { Keep(slot); return false; }

        Color current;
        try { current = entry.Renderer.color; }
        catch (Exception e) { Fail("write-read", "renderer color read failed: " + e); Keep(slot); return false; }

        if (SameColor(current, entry.Written))
        {
            entry.PendingRestore = false;
            return true;
        }
        if (!SameColor(current, entry.Base))
        {
            Slots[slot] = null;                                  // 外部已改色：本模块不再接管
            return false;
        }
        try
        {
            entry.Renderer.color = entry.Written;
            entry.PendingRestore = false;
            return true;
        }
        catch (Exception e)
        {
            Fail("write", "renderer color write failed: " + e);
            Keep(slot);                                          // 可能已落地：回执保留，只能恢复
            return false;
        }
    }

    /// <summary>
    /// CAS 归还：只写回执里记录的 renderer（不碰可能被替换的新 renderer，也不要求 Arrow 组件还活着）。
    /// 身份复核：仍同 GO 指针 + InstanceID 才 CAS 写回 base（当前仍是 written）；已是 base 或外部已改色
    /// 都只丢回执；identity 读异常 = 未知 → 保留回执 + 退避；确证换对象 / renderer 已销毁才退休。
    /// </summary>
    private static void TryRestore(int slot)
    {
        TintEntry entry = Slots[slot];
        if (entry.Renderer == null || entry.Renderer.gameObject == null)
        {
            Slots[slot] = null;                                  // renderer 已销毁：无处可写，退休
            return;
        }

        OwnerCheck owner = CheckOwner(entry);
        if (owner == OwnerCheck.Different) { Slots[slot] = null; return; }   // 确证换对象：绝不写新对象
        if (owner == OwnerCheck.Unknown) { Keep(slot); return; }             // 读异常：未知，保留回执

        Color current;
        try { current = entry.Renderer.color; }
        catch (Exception) { Keep(slot); return; }

        if (SameColor(current, entry.Base)) { Slots[slot] = null; return; }  // 本模块颜色已不在
        if (!SameColor(current, entry.Written)) { Slots[slot] = null; return; }   // 外部已改色：不覆盖
        try
        {
            entry.Renderer.color = entry.Base;
            Slots[slot] = null;
        }
        catch (Exception) { Keep(slot); }                        // 写异常：保留恢复回执，Tick 重试
    }

    /// <summary>回执保留：标记待归还并退避 0.5s（读/写失败一律走这里，绝不丢账也绝不绕过退避）。</summary>
    private static void Keep(int slot)
    {
        TintEntry entry = Slots[slot];
        entry.PendingRestore = true;
        entry.NextRetry = Now() + RetrySeconds;
    }

    // ============================================================
    // 四、工具
    // ============================================================

    private enum OwnerCheck
    {
        /// <summary>renderer 仍属于回执记录的原 native 对象。</summary>
        Match,
        /// <summary>确证：renderer 已销毁，或其 renderer 指针 / GO 指针 / InstanceID 与回执不符。</summary>
        Different,
        /// <summary>身份读取抛异常：未知，既不算换对象也不算销毁。</summary>
        Unknown,
    }

    /// <summary>每次写色前复核回执原身份（renderer 指针 → 其 GO 指针 + GO InstanceID）。</summary>
    private static OwnerCheck CheckOwner(TintEntry entry)
    {
        if (entry.Renderer == null || entry.Renderer.gameObject == null) return OwnerCheck.Different;
        try
        {
            if (entry.Renderer.Pointer != entry.RendererPtr) return OwnerCheck.Different;
            if (entry.Renderer.gameObject.Pointer != entry.GoPtr) return OwnerCheck.Different;
            if (entry.Renderer.gameObject.GetInstanceID() != entry.GoId) return OwnerCheck.Different;
            return OwnerCheck.Match;
        }
        catch (Exception) { return OwnerCheck.Unknown; }
    }

    /// <summary>四元身份精确匹配（GO 指针 + InstanceID + arrow 指针 + renderer 指针）。</summary>
    private static int FindExact(IntPtr goPtr, int goId, IntPtr arrowPtr, IntPtr rendererPtr)
    {
        for (int i = 0; i < Capacity; i++)
        {
            TintEntry entry = Slots[i];
            if (entry != null && entry.GoPtr == goPtr && entry.GoId == goId
                && entry.ArrowPtr == arrowPtr && entry.RendererPtr == rendererPtr) return i;
        }
        return -1;
    }

    /// <summary>同 GO + 同 renderer 上是否已有任何回执（不论 arrow 组件身份/状态）。</summary>
    private static bool HasReceiptFor(IntPtr goPtr, int goId, IntPtr rendererPtr)
    {
        for (int i = 0; i < Capacity; i++)
        {
            TintEntry entry = Slots[i];
            if (entry != null && entry.GoPtr == goPtr && entry.GoId == goId && entry.RendererPtr == rendererPtr) return true;
        }
        return false;
    }

    /// <summary>取空闲槽位；满时返回 -1（绝不驱逐已有回执、绝不动任何箭）。</summary>
    private static int Allocate()
    {
        for (int i = 0; i < Capacity; i++) if (Slots[i] == null) return i;
        return -1;
    }

    /// <summary>当前色是否已被外部改走（本模块只在颜色仍是 written 时有接管权）。</summary>
    private static bool ExternallyChanged(TintEntry entry)
    {
        try { return !SameColor(entry.Renderer.color, entry.Written); }
        catch (Exception) { return true; }
    }

    private static SpriteRenderer ResolveRenderer(Arrow arrow)
    {
        try
        {
            SpriteRenderer renderer = arrow._spriteRenderer;
            if (renderer != null && renderer.gameObject != null) return renderer;
        }
        catch (Exception) { }
        try
        {
            GameObject go = arrow.gameObject;
            return go != null ? go.GetComponent<SpriteRenderer>() : null;
        }
        catch (Exception) { return null; }
    }

    private static bool TryGoIdentity(GameObject gameObject, out IntPtr goPtr, out int goId)
    {
        goPtr = IntPtr.Zero;
        goId = 0;
        try
        {
            goPtr = gameObject.Pointer;
            goId = gameObject.GetInstanceID();
            return goPtr != IntPtr.Zero && goId != 0;
        }
        catch (Exception) { return false; }
    }

    private static bool WorldContext(out IntPtr world, out IntPtr layer, out int scene)
    {
        world = IntPtr.Zero;
        layer = IntPtr.Zero;
        scene = 0;
        try
        {
            if (!ArcherOptionsScope.TryGetContext(out world, out layer, out scene))
            {
                world = IntPtr.Zero;
                layer = IntPtr.Zero;
                scene = 0;
                return false;
            }
            return world != IntPtr.Zero;
        }
        catch (Exception)
        {
            world = IntPtr.Zero;
            layer = IntPtr.Zero;
            scene = 0;
            return false;
        }
    }

    /// <summary>本地 Scatter 开关（配置未初始化/读取异常一律 fail-closed）。</summary>
    private static bool ScatterSwitchOn()
    {
        try
        {
            var entry = ModConfig.ArcherScatterEnabled;
            return entry != null && entry.Value;
        }
        catch (Exception) { return false; }
    }

    private static Color Mix(Color baseColor)
    {
        return new Color(
            baseColor.r + (GoldR - baseColor.r) * GoldMix,
            baseColor.g + (GoldG - baseColor.g) * GoldMix,
            baseColor.b + (GoldB - baseColor.b) * GoldMix,
            baseColor.a);
    }

    private static bool SameColor(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) <= ColorEpsilon
            && Mathf.Abs(a.g - b.g) <= ColorEpsilon
            && Mathf.Abs(a.b - b.b) <= ColorEpsilon
            && Mathf.Abs(a.a - b.a) <= ColorEpsilon;
    }

    private static float Now() => Time.unscaledTime;

    // ---------- 测试钩子（仅 internal，供 direct-linked 测试隔离用例） ----------

    /// <summary>清空全部回执，供测试用例之间隔离；不做任何游戏写入、不销毁任何对象。</summary>
    internal static void ResetForTests()
    {
        for (int i = 0; i < Capacity; i++) Slots[i] = null;
        Logged.Clear();
    }
}

/// <summary>
/// 原生 <c>Arrow.ReceiveInitialise</c>（2.4 已核 unique 0x4c93b0 / 336B / same_slots 1）唯一钩子：
/// 只接管形态正确的自有 6 字节额外箭 payload，其余原样放行。
/// </summary>
[HarmonyPatch(typeof(Arrow), "ReceiveInitialise")]
internal static class Arrow_ReceiveInitialise_Tint_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(Arrow __instance) => ScatterArrowTint.OnReceiveInitialise(__instance);
}
