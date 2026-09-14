using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 可选植被 QoL：两枚互相独立的默认关闭开关。可用世界由 root 的 OptionalQoLScope 决定
/// （本文件不假设任何单一 biome，scope 关闭时既不新增也不触碰 world）。
///
/// 一、密灌木 DenseThickets（ModConfig.DenseThicketsEnabled）
///   开启：只在 World.CanSpawnThicket 单次调用窗口内把 world.thicketSpacing 临时减半，让原生
///   间距判定按半间距放行。2.4 原生 CanSpawnThicket 只有 auth + 左右城墙 strict 内排除 + 所有
///   其他 Grass 间距三个条件，本次改写只影响其中第三条；Stage7、冬季、Decay、非草地等条件由
///   原生 Grass.GrassUpdate 调用者负责，本 mod 不手工生成任何对象（额外灌木全部来自原生
///   Grass.SpawnThicket）。
///   归属：World.AddThicket postfix 用“原始间距 + 不含额外实例的原生锚点”判定新灌木是否为额外
///   实例；只有判定为额外的实例才登记所有权，因此新增的自然灌木不会被误标成额外。
///   生命周期：Grass.RemoveThicket postfix 在原生确实解除旧 _thicket 后立刻作废对应登记；
///   每次 AddThicket 事件都作废旧登记（池会原样复用同一 native 对象的指针与 InstanceID，
///   仅凭指针相同绝不能继续认定新生命仍属于本 mod）并只在判定为额外时重建。冬季 / Stage 低于 7
///   的原生清除因此会同步更新记录，不会留下指向已回收对象的责任。
///   关闭：只回收登记在案的额外实例，先快速枯萎淡出，再调用原生 Grass.RemoveThicket 回收，并在
///   同一帧核对 grass._thicket 身份后才算清完；全部清完前拒绝再次开启。回收批次一旦开始，即使
///   配置仍为开启也保持停用（同一次持有意图在批次结束后自动恢复）；回收期内任何再次开启
///   （面板 TrySet 或外部直接改配置）都会被拒绝并纠正回关闭。
///   淡出：真实 Thicket prefab（GO21717）没有 SpriteRendererFX，只有根与 Back L / Back R 三层
///   Foliage SpriteRenderer，所以主路径是把该实例所有子层 SpriteRenderer 一起按 alpha 渐变，
///   并在原生回收前把每个 renderer 的整色还原（池化复用不留改过的颜色）。存在 FX 的实例优先
///   复用 SpriteRendererFX.FadeOut。
///   整色交还入口：Grass.RemoveThicket prefix（冬季 / Stage 低于 7 / 城墙等原生删除在真正解除
///   _thicket 之前先还原，不拦截原生删除）与本 mod 自己的回收路径都调用同一个幂等还原函数。
///   凭据只在三层全部还原成功、对象已销毁、或对象已被新生命占用（不再写）时丢弃；写入失败保留
///   凭据，由 Tick 有界重试，且在对象重新可见（可能已被池复用）前绝不写。
///   写权边界：本 mod 只覆盖自己最后一次写入的颜色（逐层 lastAppliedColor）；当前值若既不是基色也
///   不是 lastApplied（Foliage 季节色、精灵交换或其他 mod 改过），该层直接放弃，绝不覆盖外部写入。
///   每次 World.AddThicket 新生命周期都会立即作废该 grass 与该 thicket 对象上所有旧登记（含已 dead）
///   的颜色写权；已 dead 的登记此后只允许交还“仍在池中、未被复用”对象的颜色，永不再回收或删除对象。
///   清理期间：CanSpawnThicket 判定改为“原生非间距条件 + 原始间距，额外实例不参与阻拦”，既有原生
///   灌木不会因为额外实例尚未淡完而被反向清除；尚无灌木的草则连额外实例一起作为阻拦，不会在旧
///   额外实例旁再生新灌木。postfix 只会把原生结果进一步置 false，绝不把原生 false 改成 true。
///   临时 scalar：调用窗口内的 thicketSpacing 由调用栈 token 持有；postfix 与 finalizer 各自尝试
///   交还一次，失败则转入独立待恢复表（仿 PatchWorld_DeerPopulation），下一次进入该 world 或
///   Tick 兜底时重试。未交还的值不会被当成新基线（否则 /2 会叠加成 /4），读取异常不算对象死亡。
///   换 world / 对象死亡只清登记，绝不向旧对象或新 world 写入。
///
/// 二、快速森林退缩 FastForestRecede（ModConfig.FastForestRecedeEnabled）
///   开启：ForestItem.FadeAndRemove 的 Prefix 把等待时间缩到 1/3（delay &gt; 0 时 delay / 3，
///   否则 removeDelay * Random(0.5, 1.5) / 3）。原生把 delay 交给 StartCoroutine 的淡出协程，
///   本 patch 只改本次调用的传参：不改全局时间，不碰原生淡出、Destroy、森林边界更新链，也不改
///   removeDelay 字段，不 hook 协程 factory 或 MoveNext。
///   范围：只对非 controlsForestSize、且 _forest 属于当前 world 的 gameLayer、item 与当前
///   gameLayer 同 scene 的实例生效。视差背景里的 ForestItem 可能不是 gameLayer 子孙，因此只校验
///   _forest 归属与 item 所在 scene，不以 item 自身的 IsChildOf(gameLayer) 判断。
///
/// root 契约（本文件只依赖，不在此实现）：ModConfig.DenseThicketsEnabled 与
/// FastForestRecedeEnabled 为 ConfigEntry&lt;bool&gt;；OptionalQoLScope.IsActive 由 root 决定可用
/// 世界；OptionalQoLScope.IsCurrent(Component) 判断组件属于当前 world 层/场景；
/// ModPanel.Update 每帧调用 Tick()。TrySetDenseThickets 只写配置，实际启停与回收全部发生在主线程 Tick。
///
/// 联机边界：本 mod 只在具备世界权威时写 world（新增/回收/临时 scalar 都不在客机发生）；原生
/// Thicket 对象本身不进 Persistent 存档、也不在客机同步，完整联机视觉表现需实机验证。
/// </summary>
internal static class PatchWorld_OptionalVegetation
{
    /// <summary>原生森林退缩等待时间的除数：3 即等待缩到 1/3。用户若要 1/2 或 1/5 只改这里。</summary>
    internal const float ForestRecedeMultiplier = 3f;

    /// <summary>开启时原生 thicketSpacing 的除数：2 即间距减半，密度约翻倍。</summary>
    private const float DenseSpacingDivisor = 2f;

    /// <summary>额外实例枯萎淡出的时长（秒，游戏时间）；淡完才调用原生 RemoveThicket。</summary>
    private const float ExtraThicketFadeSeconds = 0.4f;

    /// <summary>每次 Tick 最多执行的清理动作数（起淡、淡化推进、回收调用），常驻开销有界。</summary>
    private const int MaxCleanupActionsPerTick = 8;

    /// <summary>每次 Tick 最多重试交还的临时 scalar 条数。</summary>
    private const int MaxPendingRestoresPerTick = 4;

    /// <summary>回收调用未生效时（原生守卫未满足）的重试间隔，避免同帧反复调用原生。</summary>
    private const float RemovalRetrySeconds = 0.5f;

    private enum ThicketState : byte
    {
        Extra = 0,
        Fading = 1
    }

    internal enum CanSpawnMode : byte
    {
        Passthrough = 0,
        Dense = 1,
        Cleanup = 2
    }

    private enum Liveness : byte
    {
        Alive = 0,
        Gone = 1,
        Deferred = 2
    }

    private enum ColorOwnership : byte
    {
        Bound = 0,
        Detached = 1,
        Foreign = 2,
        Dead = 3,
        Deferred = 4
    }

    /// <summary>额外灌木所有权：世界、草、灌木三重实例身份 + 池化复用后的生命周期事件。</summary>
    private sealed class ExtraThicket
    {
        internal World World;
        internal Grass Grass;
        internal GameObject Thicket;
        internal IntPtr WorldPointer;
        internal IntPtr GrassPointer;
        internal IntPtr ThicketPointer;
        internal int GrassId;
        internal int ThicketId;
        internal ThicketState State;
        internal float FadeStart;
        internal float NextAttempt;
        internal bool FxFade;
        internal SpriteRenderer[] Sprites;
        internal Color[] BaseColors;
        internal Color[] Applied;      // 逐层 lastAppliedColor：只有本 mod 最后写入的值才允许被归还覆盖
        internal bool ColorRevoked;    // 该对象已被新生命周期接管：旧凭据不再拥有任何颜色写权
        internal bool Dead;
    }

    /// <summary>CanSpawnThicket 单次调用的租约：临时 scalar、调用栈 token 与判定模式。</summary>
    internal struct CanSpawnLease
    {
        internal World World;
        internal IntPtr WorldPointer;
        internal float Original;
        internal float Applied;
        internal byte Written;
        internal ulong Token;
        internal CanSpawnMode Mode;
    }

    private static readonly Dictionary<IntPtr, ExtraThicket> Owned = new();
    private static readonly List<ExtraThicket> Records = new();
    private static readonly Dictionary<IntPtr, CanSpawnLease> Held = new();
    private static readonly Dictionary<IntPtr, CanSpawnLease> Pending = new();
    private static readonly List<CanSpawnLease> PendingScratch = new();
    private static readonly HashSet<string> Logged = new();
    private static ulong _nextToken;
    private static int _cursor;
    private static int _pendingColorCount;
    private static bool _loggedDenseApplied;
    private static bool _cleanupPending;
    private static bool _denseConfigSeen;

    // ------------------------------------------------------------------ 面板契约

    /// <summary>回收期：仍有本 mod 额外实例未完成回收时拒绝再次开启（unknown 时保守为真）。</summary>
    internal static bool IsCleaning
    {
        get
        {
            if (Records.Count == 0) return false;
            if (_cleanupPending) return true; // 回收批次一旦开始就不再回滚为开启
            try
            {
                World current = CurrentWorld();
                if (current == null) return true;
                return !DenseRequested(current);
            }
            catch (Exception error)
            {
                FailOnce(error);
                return true;
            }
        }
    }

    /// <summary>面板可选状态文案。</summary>
    internal static string DenseStatus
    {
        get
        {
            try
            {
                if (IsCleaning) return "回收中 " + Records.Count + " 个额外实例";
                World current = CurrentWorld();
                if (current != null && DenseRequested(current)) return "已开启";
                return "未激活";
            }
            catch (Exception error)
            {
                FailOnce(error);
                return "未激活";
            }
        }
    }

    /// <summary>
    /// 面板开关入口：只写配置。开启请求在回收期被拒绝，并把可能被外部强设的 true 纠正回 false；
    /// 关闭请求只落配置，实际淡出与原生回收全部由 Tick 在主线程推进。
    /// </summary>
    internal static bool TrySetDenseThickets(bool enabled)
    {
        try
        {
            ConfigEntry<bool> entry = ModConfig.DenseThicketsEnabled;
            if (entry == null) return false;
            if (enabled && IsCleaning)
            {
                if (entry.Value) entry.Value = false;
                Once("set-refused", "额外实例尚未回收完，拒绝再次开启密灌木");
                return false;
            }
            entry.Value = enabled;
            return true;
        }
        catch (Exception error)
        {
            FailOnce(error);
            return false;
        }
    }

    /// <summary>由 ModPanel.Update 每帧调用（主线程）。无待恢复 scalar、无额外实例时零开销。</summary>
    internal static void Tick()
    {
        try
        {
            DrainPending();
            ObserveDenseConfig();
            if (Records.Count == 0)
            {
                if (_cleanupPending)
                {
                    _cleanupPending = false;
                    _loggedDenseApplied = false;
                    Info("额外灌木登记已清空（原生回收完成或随旧世界销毁），可以再次开启密灌木");
                }
                return;
            }
            Reclaim(CurrentWorld());
        }
        catch (Exception error)
        {
            FailOnce(error);
        }
    }

    // ------------------------------------------------------------------ 密灌木 hook 主体

    /// <summary>World.CanSpawnThicket prefix：仅在本次调用窗口内临时改写 thicketSpacing。</summary>
    internal static void BeginCanSpawn(World world, out CanSpawnLease lease)
    {
        lease = default;
        try
        {
            if (world == null) return;
            IntPtr pointer = world.Pointer;
            lease.World = world;
            lease.WorldPointer = pointer;

            // 历史失败先交还：未交还的临时值绝不能当作新基线（否则每次 /2 越缩越密）。
            if (Pending.TryGetValue(pointer, out CanSpawnLease pending))
            {
                if (!SameLeaseIdentity(pending, world)) Pending.Remove(pointer);
                else
                {
                    RestoreSpacing(pending);
                    if (Pending.ContainsKey(pointer)) return;
                }
            }

            // 重入：外层调用栈已持有该 world 的 scalar，本次继承其语义但不再改写、不再交还。
            if (Held.TryGetValue(pointer, out CanSpawnLease outer))
            {
                lease.Mode = outer.Mode;
                lease.Original = outer.Original;
                lease.Applied = outer.Applied;
                lease.Written = 0;
                return;
            }

            bool dense = DenseRequested(world);
            if (!dense && !HasOwnedInWorld(world)) return; // 关闭且无登记：完全不触碰 world 字段

            float original = world.thicketSpacing;
            lease.Original = original;
            if (!float.IsFinite(original) || !(original > 0f)) return;
            CanSpawnMode mode = dense ? CanSpawnMode.Dense : CanSpawnMode.Cleanup;
            // IL2CPP ICollection does not expose CLR enumeration. Verify the
            // native backing collection before changing its spacing window.
            if (world._grassWithThicket == null
                || world._grassWithThicket.TryCast<Il2CppSystem.Collections.Generic.HashSet<Grass>>() == null) return;

            float applied = mode == CanSpawnMode.Dense ? original / DenseSpacingDivisor : 0f;
            if (!float.IsFinite(applied)) return;

            ulong token = ++_nextToken;
            if (token == 0) token = ++_nextToken;
            lease.Token = token;
            lease.Mode = mode;
            lease.Applied = applied;
            lease.Written = 1; // 先登记再写：interop setter 可能已落地再抛异常，仍须由本租约交还
            Held[pointer] = lease;
            world.thicketSpacing = applied;
            if (mode == CanSpawnMode.Dense && !_loggedDenseApplied)
            {
                _loggedDenseApplied = true;
                Info("密灌木开启：CanSpawnThicket 调用窗口内 thicketSpacing " + original + " -> " + applied
                    + "，调用结束立即还原；额外灌木仍由原生 Grass.SpawnThicket 生成");
            }
        }
        catch (Exception error)
        {
            RestoreSpacing(lease);
            FailOnce(error);
        }
    }

    /// <summary>World.CanSpawnThicket finalizer：异常路径也要尝试交还临时 scalar。</summary>
    internal static Exception AbortCanSpawn(Exception exception, CanSpawnLease lease)
    {
        RestoreSpacing(lease);
        return exception;
    }

    /// <summary>
    /// World.CanSpawnThicket postfix：先尝试交还临时 scalar（与开关状态无关），再在回收期用原始
    /// 间距和不含额外实例的原生锚点把结果进一步置 false（只降不升）。
    /// </summary>
    internal static void FinishCanSpawn(World world, Grass grass, ref bool result, CanSpawnLease lease)
    {
        RestoreSpacing(lease);
        if (lease.Mode != CanSpawnMode.Cleanup) return;
        try
        {
            bool hasThicket = grass != null && grass._thicket != null;
            if (hasThicket && IsExtra(grass, world)) return; // 正在淡出的额外实例由本 mod 负责回收，不按间距提前踢掉
            if (HasBlockingMember(world, grass, lease.Original, hasThicket)) result = false;
        }
        catch (Exception error)
        {
            FailOnce(error);
        }
    }

    /// <summary>
    /// World.AddThicket postfix：原生确认加厚灌木后，先作废该 grass 与该 thicket 对象上所有旧登记
    /// （含已 dead）的颜色写权、并作废该草的旧生命周期登记，再按“原始间距 + 不含额外实例的原生锚点”
    /// 决定是否重建为额外登记。
    /// </summary>
    internal static void OnThicketAdded(World world, Grass grass)
    {
        try
        {
            if (world == null || grass == null) return;
            GameObject thicket = grass._thicket;
            if (thicket == null) return;
            // 新生命周期优先：该 grass 或该 thicket 对象（池化复用后同一 native 对象）上所有旧登记，
            // 包括已 dead 的，立即失去颜色写权，绝不为恢复旧色写坏新对象。
            RevokeColorWrites(grass, thicket);
            RetireOwned(grass); // 本次事件就是新生命周期：旧记录即使指针/ID 相同也不再有效
            if (!DenseRequested(world)) return;
            float spacing = world.thicketSpacing;
            if (!float.IsFinite(spacing) || !(spacing > 0f)) return;
            if (!HasBlockingMember(world, grass, spacing, true)) return; // 无原生锚点落在原始间距内：自然灌木
            RegisterExtra(world, grass, thicket);
        }
        catch (Exception error)
        {
            FailOnce(error);
        }
    }

    /// <summary>
    /// Grass.RemoveThicket prefix：原生删除（冬季 / Stage 低于 7 / 城墙等）真正解除 _thicket 之前，
    /// 先交还本 mod 改过的整色，避免对象带着未还原的颜色入池。只写仍然同属一条 grass/thicket 生命
    /// 或已解绑但仍在池里的对象；失败保留凭据交给 Tick 重试；绝不拦截原生删除。
    /// </summary>
    internal static void OnNativeRemoveThicket(Grass grass)
    {
        try
        {
            if (grass == null) return;
            if (Owned.TryGetValue(grass.Pointer, out ExtraThicket record)
                && record.GrassId == grass.GetInstanceID() && HasPendingColors(record))
            {
                RestoreOwnedColors(record);
                return;
            }
            if (_pendingColorCount == 0) return; // 常态零开销：只有仍有未交还颜色时才扫描登记
            for (int i = 0; i < Records.Count; i++)
            {
                ExtraThicket candidate = Records[i];
                if (candidate.GrassPointer != grass.Pointer || candidate.GrassId != grass.GetInstanceID()) continue;
                if (!HasPendingColors(candidate)) continue;
                RestoreOwnedColors(candidate);
            }
        }
        catch (Exception error)
        {
            FailOnce(error);
        }
    }

    /// <summary>
    /// Grass.RemoveThicket postfix：只在原生确实解除旧 _thicket 后作废对应生命周期登记；
    /// 原生守卫未通过（_thicket 原样保留）时登记不动，仍然由回收流程负责。
    /// </summary>
    internal static void OnThicketRemoved(Grass grass)
    {
        try
        {
            if (grass == null) return;
            if (!Owned.TryGetValue(grass.Pointer, out ExtraThicket record) || record.Dead) return;
            if (record.GrassId != grass.GetInstanceID())
            {
                record.Dead = true; // 槽位已被新生命复用
                return;
            }
            GameObject live = grass._thicket;
            if (live != null && live.Pointer == record.ThicketPointer) return; // 未真正解除
            record.Dead = true;
        }
        catch (Exception error)
        {
            FailOnce(error);
        }
    }

    /// <summary>
    /// 交还本租约持有的临时 scalar：postfix / finalizer 各调用一次，失败转入待恢复表由下一次进入
    /// 该 world 或 Tick 重试。旧 token 不覆写更新的 lease；读取异常不算对象死亡。
    /// </summary>
    private static void RestoreSpacing(CanSpawnLease lease)
    {
        if (lease.Token == 0 || lease.Written == 0) return;
        bool hasHeld = Held.TryGetValue(lease.WorldPointer, out CanSpawnLease held);
        if (hasHeld && held.Token != lease.Token) return; // 更新的调用栈已接管，旧 finalizer 不得改写
        bool hasPending = Pending.TryGetValue(lease.WorldPointer, out CanSpawnLease pending);
        if (!hasHeld && (!hasPending || pending.Token != lease.Token)) return; // 既非当前也非待恢复
        if (!hasHeld) lease = pending; // 只重试仍未交还的部分
        byte unresolved = lease.Written;
        try
        {
            World world = lease.World;
            if (!SameLeaseIdentity(lease, world))
            {
                unresolved = 0; // 对象死亡或槽位换对象：本租约作废，不再写任何 field
                return;
            }
            try
            {
                // 单字段：写回自己的值；实测外部改写（值不再等于 Applied）由外部写入者拥有。
                if (world.thicketSpacing == lease.Applied) world.thicketSpacing = lease.Original;
                unresolved = 0;
            }
            catch (Exception error)
            {
                FailOnce(error); // 读取/写入异常不当死亡：保留待恢复记录等下一次重试
            }
        }
        finally
        {
            if (Held.TryGetValue(lease.WorldPointer, out held) && held.Token == lease.Token) Held.Remove(lease.WorldPointer);
            // 新的调用栈 / 更新的待恢复记录优先，旧租约不覆盖。
            if (!Held.ContainsKey(lease.WorldPointer)
                && (!Pending.TryGetValue(lease.WorldPointer, out pending) || pending.Token == lease.Token))
            {
                if (unresolved == 0) Pending.Remove(lease.WorldPointer);
                else
                {
                    lease.Written = unresolved;
                    Pending[lease.WorldPointer] = lease;
                }
            }
        }
    }

    /// <summary>失败待恢复的临时 scalar：换 world、失权、关闭都不放弃归还责任。</summary>
    private static void DrainPending()
    {
        if (Pending.Count == 0) return;
        PendingScratch.Clear();
        foreach (KeyValuePair<IntPtr, CanSpawnLease> pair in Pending) PendingScratch.Add(pair.Value);
        int restored = 0;
        for (int i = 0; i < PendingScratch.Count && restored < MaxPendingRestoresPerTick; i++)
        {
            RestoreSpacing(PendingScratch[i]);
            restored++;
        }
    }

    private static bool SameLeaseIdentity(CanSpawnLease lease, World world)
        => world != null && world.Pointer == lease.WorldPointer;

    private static void RegisterExtra(World world, Grass grass, GameObject thicket)
    {
        IntPtr key = grass.Pointer;
        var record = new ExtraThicket
        {
            World = world,
            Grass = grass,
            Thicket = thicket,
            WorldPointer = world.Pointer,
            GrassPointer = key,
            ThicketPointer = thicket.Pointer,
            GrassId = grass.GetInstanceID(),
            ThicketId = thicket.GetInstanceID(),
            State = ThicketState.Extra
        };
        Owned[key] = record;
        Records.Add(record);
        Once("first-extra", "首个额外灌木已登记（对象由原生 SpawnThicket 生成，这里只登记归属）");
    }

    private static void RetireOwned(Grass grass)
    {
        if (grass == null) return;
        if (!Owned.TryGetValue(grass.Pointer, out ExtraThicket record)) return;
        record.Dead = true;
    }

    /// <summary>
    /// 新生命周期接管后作废旧颜色写权：命中同一 grass 或同一 thicket 对象（池返还会复用同一 native
    /// 对象）的所有登记，包括已 dead 的，立刻失去写权并丢弃凭据；不为恢复旧色写坏新对象。
    /// </summary>
    private static void RevokeColorWrites(Grass grass, GameObject thicket)
    {
        if (Records.Count == 0) return;
        for (int i = 0; i < Records.Count; i++)
        {
            ExtraThicket record = Records[i];
            if (record.GrassPointer != grass.Pointer && record.ThicketPointer != thicket.Pointer) continue;
            record.ColorRevoked = true;
            ClearColorReceipt(record);
        }
    }

    private static bool IsExtra(Grass grass, World world)
    {
        if (grass == null || world == null) return false;
        try
        {
            if (!Owned.TryGetValue(grass.Pointer, out ExtraThicket record)) return false;
            return !record.Dead && record.WorldPointer == world.Pointer
                && record.GrassId == grass.GetInstanceID();
        }
        catch (Exception error)
        {
            FailOnce(error);
            return false;
        }
    }

    /// <summary>
    /// 原始间距下是否存在阻拦锚点：excludeExtras 为真时（既有灌木的保留判定、额外归属判定）
    /// 额外实例不参与阻拦；为假时（原生生成判定）额外实例同样阻拦。
    /// </summary>
    private static bool HasBlockingMember(World world, Grass grass, float spacing, bool excludeExtras)
    {
        if (world == null || grass == null || !float.IsFinite(spacing) || !(spacing > 0f)) return false;
        var members = world._grassWithThicket;
        if (members == null) return false;
        float x = grass.transform.position.x;
        foreach (Grass other in members.Cast<Il2CppSystem.Collections.Generic.HashSet<Grass>>())
        {
            if (other == null) continue;
            if (other.Pointer == grass.Pointer) continue;
            if (excludeExtras && IsExtra(other, world)) continue;
            if (Mathf.Abs(other.transform.position.x - x) < spacing) return true;
        }
        return false;
    }

    private static bool DenseRequested(World world)
    {
        try
        {
            if (_cleanupPending) return false; // 回收批次未完成前，配置即使仍为开启也保持停用
            ConfigEntry<bool> entry = ModConfig.DenseThicketsEnabled;
            return entry != null && entry.Value && OptionalQoLScope.IsActive
                && NetworkBigBoss.HasWorldAuth && IsCurrentWorld(world);
        }
        catch (Exception error)
        {
            FailOnce(error);
            return false;
        }
    }

    private static bool FastRecedeRequested()
    {
        try
        {
            ConfigEntry<bool> entry = ModConfig.FastForestRecedeEnabled;
            return entry != null && entry.Value && OptionalQoLScope.IsActive;
        }
        catch (Exception error)
        {
            FailOnce(error);
            return false;
        }
    }

    private static bool HasOwnedInWorld(World world)
    {
        if (world == null) return false;
        for (int i = 0; i < Records.Count; i++)
        {
            if (!Records[i].Dead && Records[i].WorldPointer == world.Pointer) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ 快速森林退缩

    /// <summary>
    /// ForestItem.FadeAndRemove prefix：只缩放本次调用的等待时间，其余原生链路原样保留。
    /// </summary>
    internal static void ScaleForestRecedeDelay(ForestItem item, ref float delay)
    {
        try
        {
            if (!FastRecedeRequested()) return;
            if (item == null || item.controlsForestSize || item.removedByForest) return;
            if (!ForestBelongsToCurrentWorld(item)) return;
            float baseDelay = delay > 0f ? delay : item.removeDelay * UnityEngine.Random.Range(0.5f, 1.5f);
            if (!float.IsFinite(baseDelay) || !(baseDelay > 0f)) return;
            float scaled = baseDelay / ForestRecedeMultiplier;
            if (!float.IsFinite(scaled) || !(scaled > 0f)) return;
            delay = scaled;
        }
        catch (Exception error)
        {
            FailOnce(error);
        }
    }

    private static bool ForestBelongsToCurrentWorld(ForestItem item)
    {
        World world = CurrentWorld();
        if (world == null || world.gameLayer == null) return false;
        Forest forest = item._forest;
        if (forest == null || forest.gameObject == null) return false;
        if (!OptionalQoLScope.IsCurrent(forest)) return false;
        GameObject layer = world.gameLayer.gameObject;
        GameObject itemObject = item.gameObject;
        if (layer == null || itemObject == null) return false;
        // 视差背景里的 item 可能不是 gameLayer 子孙，因此只要求同一场景。
        return itemObject.scene.handle == layer.scene.handle
            && forest.gameObject.scene.handle == layer.scene.handle;
    }

    // ------------------------------------------------------------------ 回收推进

    private static void Reclaim(World current)
    {
        bool dense = current != null && DenseRequested(current);
        CompactRecords();
        int actions = 0;
        int scanned = 0;
        int budget = Records.Count;
        while (Records.Count > 0 && scanned < budget && actions < MaxCleanupActionsPerTick)
        {
            int index = _cursor;
            if (index >= Records.Count) index = 0;
            _cursor = index + 1;
            ExtraThicket record = Records[index];
            scanned++;
            if (record.Dead)
            {
                // 已死登记只允许继续交还“仍在池里”的颜色；绝不再进入生命周期判定或回收路径。
                // 池化复用会让同一 ptr+ID 重新变成 Bound，旧登记绝不允许据此删除新自然灌木。
                if (HasPendingColors(record))
                {
                    RestoreOwnedColors(record);
                    actions++;
                }
                continue;
            }
            Liveness life = Inspect(record, current);
            if (life == Liveness.Gone)
            {
                record.Dead = true;
                if (HasPendingColors(record))
                {
                    RestoreOwnedColors(record); // 原生已解除：仍在池里的对象也必须先交还颜色
                    actions++;
                }
                continue;
            }
            if (life == Liveness.Deferred || dense) continue; // 开启期间只核对身份，不做回收
            if (TryReclaimOnce(record, current)) actions++;
        }
        CompactRecords();
    }

    private static Liveness Inspect(ExtraThicket record, World current)
    {
        try
        {
            if (record.World == null || record.Grass == null || record.Thicket == null) return Liveness.Gone;
            if (record.World.Pointer != record.WorldPointer) return Liveness.Gone;
            if (current == null) return Liveness.Deferred; // 关卡切换中：无法核对，保留所有权
            if (current.Pointer != record.WorldPointer) return Liveness.Gone; // 已换 world：旧对象不再触碰
            if (record.Grass.Pointer != record.GrassPointer || record.Grass.GetInstanceID() != record.GrassId)
                return Liveness.Gone;
            if (record.Thicket.Pointer != record.ThicketPointer || record.Thicket.GetInstanceID() != record.ThicketId)
                return Liveness.Gone;
            GameObject live = record.Grass._thicket;
            if (live == null) return Liveness.Gone; // 原生已回收
            if (live.Pointer != record.ThicketPointer) return Liveness.Gone; // 已被新的灌木替换
            return Liveness.Alive;
        }
        catch (Exception error)
        {
            FailOnce(error);
            return Liveness.Deferred;
        }
    }

    private static bool TryReclaimOnce(ExtraThicket record, World current)
    {
        if (record.Dead) return false; // 已死登记永不再回收（只由 RestoreOwnedColors 处理颜色）
        if (record.State == ThicketState.Extra)
        {
            if (Time.time < record.NextAttempt) return false;
            if (!CanWriteWorld(record, current)) return false;
            StartFade(record);
            return true;
        }
        float elapsed = Time.time - record.FadeStart;
        if (record.FxFade)
        {
            if (elapsed < ExtraThicketFadeSeconds) return false;
        }
        else
        {
            float progress = ExtraThicketFadeSeconds > 0f ? elapsed / ExtraThicketFadeSeconds : 1f;
            if (progress < 1f)
            {
                ApplyFallbackFade(record, progress);
                return true;
            }
            RestoreOwnedColors(record);
        }
        if (Time.time < record.NextAttempt) return false;
        if (!CanWriteWorld(record, current)) return false;
        IssueRemoval(record);
        return true;
    }

    /// <summary>client 不写 world：仅在具备世界权威、同一 world 且同场景时才触碰原生回收。</summary>
    private static bool CanWriteWorld(ExtraThicket record, World current)
    {
        try
        {
            if (!NetworkBigBoss.HasWorldAuth) return false; // authority 未知时保留所有权，等回来后继续
            if (current == null || current.Pointer != record.WorldPointer) return false;
            GameObject layer = current.gameLayer != null ? current.gameLayer.gameObject : null;
            GameObject grassObject = record.Grass.gameObject;
            GameObject thicketObject = record.Thicket;
            if (layer == null || grassObject == null || thicketObject == null) return false;
            return grassObject.scene.handle == layer.scene.handle
                && thicketObject.scene.handle == layer.scene.handle;
        }
        catch (Exception error)
        {
            FailOnce(error);
            return false;
        }
    }

    private static void StartFade(ExtraThicket record)
    {
        record.State = ThicketState.Fading;
        record.FadeStart = Time.time;
        record.FxFade = TryStartFxFade(record.Thicket, ExtraThicketFadeSeconds);
        if (!record.FxFade) CaptureSprites(record);
        if (!_cleanupPending)
        {
            _cleanupPending = true;
            Info("密灌木已关闭：开始回收 " + Records.Count + " 个额外实例（快速枯萎后调用原生 Grass.RemoveThicket）");
        }
    }

    /// <summary>存在 SpriteRendererFX 的实例优先走原生淡出；真实 Thicket prefab 没有该组件。</summary>
    private static bool TryStartFxFade(GameObject thicket, float seconds)
    {
        try
        {
            SpriteRendererFX[] effects = thicket.GetComponentsInChildren<SpriteRendererFX>(true);
            if (effects == null || effects.Length == 0) return false;
            bool started = false;
            for (int i = 0; i < effects.Length; i++)
            {
                SpriteRendererFX effect = effects[i];
                if (effect == null || !effect.isActiveAndEnabled) continue;
                effect.FadeOut(seconds, BaseSpriteFX.EndAction.None);
                started = true;
            }
            return started;
        }
        catch (Exception error)
        {
            FailOnce(error);
            return false;
        }
    }

    /// <summary>
    /// 记录该实例所有子层 SpriteRenderer 的整色（真实 Thicket 是根 + Back L / Back R 三层 Foliage），
    /// 淡出与还原都按这份快照进行，池化复用不会留下改过的颜色。
    /// </summary>
    private static void CaptureSprites(ExtraThicket record)
    {
        try
        {
            SpriteRenderer[] sprites = record.Thicket.GetComponentsInChildren<SpriteRenderer>(true);
            if (sprites == null || sprites.Length == 0) return;
            var kept = new SpriteRenderer[sprites.Length];
            var colors = new Color[sprites.Length];
            var applied = new Color[sprites.Length];
            int count = 0;
            for (int i = 0; i < sprites.Length; i++)
            {
                SpriteRenderer sprite = sprites[i];
                if (sprite == null) continue;
                try
                {
                    kept[count] = sprite;
                    colors[count] = sprite.color;
                    applied[count] = colors[count]; // 尚未写过：lastApplied 从基色起步
                    count++;
                }
                catch (Exception error)
                {
                    FailOnce(error);
                }
            }
            if (count == 0) return;
            if (count < kept.Length)
            {
                Array.Resize(ref kept, count);
                Array.Resize(ref colors, count);
                Array.Resize(ref applied, count);
            }
            if (record.Sprites == null) _pendingColorCount++;
            record.Sprites = kept;
            record.BaseColors = colors;
            record.Applied = applied;
        }
        catch (Exception error)
        {
            FailOnce(error);
        }
    }

    private static void ApplyFallbackFade(ExtraThicket record, float progress)
    {
        SpriteRenderer[] sprites = record.Sprites;
        Color[] colors = record.BaseColors;
        Color[] applied = record.Applied;
        if (sprites == null || colors == null) return;
        float amount = Mathf.Clamp01(progress);
        for (int i = 0; i < sprites.Length && i < colors.Length; i++)
        {
            SpriteRenderer sprite = sprites[i];
            if (sprite == null) continue;
            try
            {
                if (applied == null || i >= applied.Length || !SameColor(sprite.color, applied[i]))
                {
                    sprites[i] = null; // 外部新颜色由外部拥有，后续淡出和归还都不得覆盖。
                    continue;
                }
                Color color = colors[i];
                color.a = Mathf.Lerp(colors[i].a, 0f, amount);
                sprite.color = color;
                if (applied != null && i < applied.Length) applied[i] = color; // 只有成功写入才更新 lastApplied
            }
            catch (Exception error)
            {
                FailOnce(error); // 写入失败保留该层基色凭据，回收前必须要回
            }
        }
    }

    /// <summary>
    /// 逐层交还本 mod 改过的整色（幂等，可被回收路径与原生删除 prefix 重复调用）。
    /// 每层写入成功或当前值已等于基色才标记为已解决；只有全部层解决、对象已销毁、或对象已被
    /// 新生命占用（拒绝写）时才丢弃凭据。写入/读取失败保留凭据，等 Tick 或下一次原生删除前重试。
    /// </summary>
    private static void RestoreOwnedColors(ExtraThicket record)
    {
        SpriteRenderer[] sprites = record.Sprites;
        Color[] colors = record.BaseColors;
        Color[] applied = record.Applied;
        if (sprites == null || colors == null)
        {
            ClearColorReceipt(record);
            return;
        }
        if (record.ColorRevoked)
        {
            Once("color-revoked", "旧生命颜色凭据已被新生命周期作废，放弃补写");
            ClearColorReceipt(record);
            return;
        }
        ColorOwnership ownership = ColorOwnershipOf(record);
        if (ownership == ColorOwnership.Dead)
        {
            ClearColorReceipt(record); // 对象已销毁：没有可写的 renderer
            return;
        }
        if (ownership == ColorOwnership.Foreign)
        {
            Once("color-reused", "灌木对象已被新生命占用，放弃交还颜色以避免写坏池化复用对象");
            ClearColorReceipt(record);
            return;
        }
        if (ownership == ColorOwnership.Deferred) return; // 读取异常不当死亡：保留凭据
        for (int i = 0; i < sprites.Length && i < colors.Length; i++)
        {
            SpriteRenderer sprite = sprites[i];
            if (sprite == null) continue;
            try
            {
                Color current = sprite.color;
                if (SameColor(current, colors[i]))
                {
                    sprites[i] = null; // 已是基色：无需写
                    continue;
                }
                if (applied == null || i >= applied.Length || !SameColor(current, applied[i]))
                {
                    // 当前值既不是基色也不是本 mod 最后一次写入：Foliage 或其他 writer 拥有该颜色，放弃该层
                    sprites[i] = null;
                    continue;
                }
                sprite.color = colors[i]; // 只覆盖本 mod 自己写过的值
                sprites[i] = null;
            }
            catch (Exception error)
            {
                FailOnce(error); // 该层保留凭据，绝不因失败丢责任
            }
        }
        if (!HasPendingColors(record)) ClearColorReceipt(record);
    }

    /// <summary>写权限判定：同一 grass/thicket 生命可写；已解绑但仍留在池里（未激活）可写；
    /// 对象已销毁或已重新可见（可能被池拿去复用）绝不写。</summary>
    private static ColorOwnership ColorOwnershipOf(ExtraThicket record)
    {
        try
        {
            GameObject thicket = record.Thicket;
            if (thicket == null || thicket.Pointer != record.ThicketPointer
                || thicket.GetInstanceID() != record.ThicketId)
                return ColorOwnership.Dead;
            Grass grass = record.Grass;
            if (grass != null && grass.Pointer == record.GrassPointer
                && grass.GetInstanceID() == record.GrassId
                && grass._thicket != null && grass._thicket.Pointer == record.ThicketPointer)
                return ColorOwnership.Bound;
            return thicket.activeInHierarchy ? ColorOwnership.Foreign : ColorOwnership.Detached;
        }
        catch (Exception error)
        {
            FailOnce(error);
            return ColorOwnership.Deferred;
        }
    }

    private static bool HasPendingColors(ExtraThicket record)
    {
        SpriteRenderer[] sprites = record.Sprites;
        if (sprites == null) return false;
        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] != null) return true;
        }
        return false;
    }

    private static void ClearColorReceipt(ExtraThicket record)
    {
        if (record.Sprites == null)
        {
            record.BaseColors = null;
            record.Applied = null;
            return;
        }
        record.Sprites = null;
        record.BaseColors = null;
        record.Applied = null;
        if (_pendingColorCount > 0) _pendingColorCount--;
    }

    private static bool SameColor(Color left, Color right)
        => left.r == right.r && left.g == right.g && left.b == right.b && left.a == right.a;

    private static void IssueRemoval(ExtraThicket record)
    {
        record.NextAttempt = Time.time + RemovalRetrySeconds;
        try
        {
            RestoreOwnedColors(record); // 幂等：与原生删除前的 prefix 用同一入口，测试路径也覆盖
            record.Grass.RemoveThicket();
            GameObject live = record.Grass._thicket;
            // 清完的判据是原生真实交还了 _thicket（移除钩子也会作废登记），不是计时器到点。
            if (live == null || live.Pointer != record.ThicketPointer) record.Dead = true;
        }
        catch (Exception error)
        {
            FailOnce(error);
        }
    }

    private static void CompactRecords()
    {
        int write = 0;
        for (int read = 0; read < Records.Count; read++)
        {
            ExtraThicket record = Records[read];
            if (record.Dead)
            {
                if (HasPendingColors(record))
                {
                    Records[write++] = record; // 颜色凭据未交还：留在列表里由 Tick 有界重试
                    continue;
                }
                if (Owned.TryGetValue(record.GrassPointer, out ExtraThicket owned) && ReferenceEquals(owned, record))
                    Owned.Remove(record.GrassPointer);
                continue;
            }
            Records[write++] = record;
        }
        if (write < Records.Count) Records.RemoveRange(write, Records.Count - write);
        if (_cursor >= Records.Count) _cursor = 0;
    }

    /// <summary>
    /// Tick 每帧观察配置跳变：回收未完成期间出现 false 到 true 的再次开启（面板直写配置、
    /// 外部改 cfg 等任何路径）一律纠正回关闭。清理开始前就是 true 的连续持有意图不改写，
    /// 只在本批次内保持停用，批次结束后自动恢复。
    /// </summary>
    private static void ObserveDenseConfig()
    {
        try
        {
            ConfigEntry<bool> entry = ModConfig.DenseThicketsEnabled;
            if (entry == null) return;
            bool value = entry.Value;
            bool roseWhileCleaning = value && !_denseConfigSeen && Records.Count > 0 && IsCleaning;
            _denseConfigSeen = value;
            if (!roseWhileCleaning) return;
            entry.Value = false;
            _denseConfigSeen = false;
            Once("forced-off", "回收未完成期间检测到再次开启，已纠正回关闭；额外实例清完后才能再次开启");
        }
        catch (Exception error)
        {
            FailOnce(error);
        }
    }

    private static World CurrentWorld()
    {
        try
        {
            Managers managers = Managers.Inst;
            return managers != null ? managers.world : null;
        }
        catch (Exception error)
        {
            FailOnce(error);
            return null;
        }
    }

    private static bool IsCurrentWorld(World world)
    {
        World current = CurrentWorld();
        return current != null && world != null && current.Pointer == world.Pointer;
    }

    private static void Info(string message)
    {
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[OptionalVegetation] " + message);
        }
        catch
        {
        }
    }

    private static void Once(string key, string message)
    {
        if (!Logged.Add(key)) return;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[OptionalVegetation] " + message);
        }
        catch
        {
        }
    }

    private static void FailOnce(Exception error)
    {
        Once("failure:" + error.GetType().Name, "原生访问暂时失败，保留所有权并重试：" + error.GetType().Name);
    }
}

[HarmonyPatch(typeof(World), nameof(World.CanSpawnThicket))]
internal static class World_CanSpawnThicket_OptionalVegetation_Patch
{
    [HarmonyPrefix]
    internal static void Prefix(World __instance, out PatchWorld_OptionalVegetation.CanSpawnLease __state)
        => PatchWorld_OptionalVegetation.BeginCanSpawn(__instance, out __state);

    [HarmonyPostfix]
    internal static void Postfix(World __instance, Grass grass, ref bool __result,
        PatchWorld_OptionalVegetation.CanSpawnLease __state)
        => PatchWorld_OptionalVegetation.FinishCanSpawn(__instance, grass, ref __result, __state);

    [HarmonyFinalizer]
    internal static Exception Finalizer(Exception __exception, PatchWorld_OptionalVegetation.CanSpawnLease __state)
        => PatchWorld_OptionalVegetation.AbortCanSpawn(__exception, __state);
}

[HarmonyPatch(typeof(World), nameof(World.AddThicket))]
internal static class World_AddThicket_OptionalVegetation_Patch
{
    [HarmonyPostfix]
    internal static void Postfix(World __instance, Grass grass)
        => PatchWorld_OptionalVegetation.OnThicketAdded(__instance, grass);
}

[HarmonyPatch(typeof(Grass), nameof(Grass.RemoveThicket))]
internal static class Grass_RemoveThicket_OptionalVegetation_Patch
{
    [HarmonyPrefix]
    internal static void Prefix(Grass __instance)
        => PatchWorld_OptionalVegetation.OnNativeRemoveThicket(__instance);

    [HarmonyPostfix]
    internal static void Postfix(Grass __instance)
        => PatchWorld_OptionalVegetation.OnThicketRemoved(__instance);
}

[HarmonyPatch(typeof(ForestItem), nameof(ForestItem.FadeAndRemove))]
internal static class ForestItem_FadeAndRemove_OptionalVegetation_Patch
{
    [HarmonyPrefix]
    internal static void Prefix(ForestItem __instance, ref float delay)
        => PatchWorld_OptionalVegetation.ScaleForestRecedeDelay(__instance, ref delay);
}
