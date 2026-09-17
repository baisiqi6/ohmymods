using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod;

/// <summary>
/// 英雄移动速度 ×1.5（用户 2026-09-15 要求 +50%）：**actor 自有 walk/run 字段的认领式提升**。
///
/// 设计由来（review 2026-09-15 阻断项 1/2）：初版在 `Mover.Update` 帧内临时改写 `_goalSpeed/_moveSpeed`，
/// 但 (a) 嵌套/重入 Update 会让同一次提升叠加成 ×2.25（缺同 mover 作用域租约），
/// (b) finalizer 无法证明 Update 重算出的 `_moveSpeed` 一定属于我们——「值仍落在提升后的 lerp 包络内」
/// 不是所有权证明（反例：savedGoal=2/savedMove=1，提升 3/1.5 后原生回调 `SetSpeedToGoal(2)` 不改 goalMode，
/// 现值 2 落在包络 [1.2,3] 内，会被误 ÷1.5 成 1.333，覆盖原生写入）。
/// 现改为**不引入任何逐帧作用域改写**：只认领 actor 实例字段 `Archer.walkSpeed/runSpeed`（×1.5），
/// 帧内没有任何临时状态 → 重入/半写/所有权证明问题在结构上不存在；归还走 Banker/HeroArcherRange 同款
/// 逐字段所有权回执 + CAS（第三方写入一律保留）。
///
/// 覆盖证据（**2.1 参考源**；2.4 以真实 interop 编译与实机为准，禁止把 2.1 源码当 2.4 已验证）：
///   2.1.0 `Archer.cs` 的全部地面移动指令都取自这两个 actor 字段——
///   编队行走 L459/465（`runSpeed`；仅当 `Formation.overrideMoveSpeed > 0` 才用编队值，该字段默认 -1f
///   且 2.1.0 全源码无赋值）、L486/L502/L515/L542/L551/L557/L605/L608（`runSpeed`）、
///   L592/L598 边界狩猎（`runSpeed`/`walkSpeed`）、可控制路径 L2034（玩家控制，英雄资格已排除）。
///   若 2.4 实机存在把 `Formation.overrideMoveSpeed` 设为正值的编队，该路径不受本提升覆盖（已知边界）。
///
/// 生命周期（robust lifecycle）：
///   * <see cref="Reconcile"/>：当选时与每帧 Tick 调用——死地随从让位（DL 的 Mover 提升同倍率 ×1.5）
///     或幂等安装/维持认领；**Pending 未归还前绝不再次提升**。
///   * <see cref="Restore"/>：runtime 退役漏斗（死亡/role/side/world/换 life/关闭）调用，CAS 归还；
///     写失败保留 Pending 条目（有界强引用 ≤2），由 <see cref="RetryCleanup"/> 继续（功能关闭时也服务）。
///   * <see cref="Clear"/>：全量归还并清表；Pending 条目绝不丢弃。
///
/// 不变量：
///   * 最多 <see cref="MaxHeroes"/> 个 actor；每 actor 幂等（重复安装绝不二次 ×1.5）；
///   * 回执在任何字段写入**之前**登记（Owned/Original/Written 逐字段），写入抛错也不丢归还责任；
///   * 只写 actor 实例字段（非共享 SO/资产；不碰 Mover 的临时字段、不碰朝向/goal/AI/_multiplier）；
///   * 所有入口异常隔离，绝不外抛进原生调用链；日志与容量都有界。
/// </summary>
internal static class HeroArcherMovement
{
    /// <summary>移动速度倍率（用户要求 +50%）。</summary>
    internal const float SpeedMultiplier = 1.5f;

    /// <summary>同时受管的英雄上限（每侧 1 名 → 最多 2；与 HeroArcherRange.MaxHeroes 同步）。</summary>
    internal const int MaxHeroes = 2;

    /// <summary>单字段所有权回执（Banker `Claim` 同款语义）。</summary>
    internal struct OwnedSpeed
    {
        internal bool Owned;
        internal float Original;
        internal float Written;
    }

    private sealed class Entry
    {
        internal IntPtr Pointer;      // Archer 的 native 指针（认人键之一）
        internal int GoId;            // Archer 所在 GameObject 的 InstanceID（认人键之二）
        internal Archer Archer;       // 有界强引用（≤2），仅供 pending 归还重试
        internal OwnedSpeed Walk;
        internal OwnedSpeed Run;
        internal bool Pending;        // 归还写失败：未归还前不可再次提升
    }

    private static readonly Entry[] Entries = new Entry[MaxHeroes];
    private static readonly HashSet<string> Logged = new HashSet<string>();
    private static bool _loggedFirstBoost;

    // ============================================================
    // 纯逻辑（可测）：倍率与有限性
    // ============================================================

    /// <summary>
    /// 单字段提升：只接受有限且为正、且提升结果仍有限的输入；任何非有限/非正值一律 fail-closed
    /// （review 阻断项 3：输出也必须是有限值，绝不把溢出成 ±Inf 的结果写进字段）。
    /// </summary>
    internal static bool TryScale(float current, out float scaled)
    {
        scaled = current;
        if (!float.IsFinite(current) || current <= 0f) return false;
        float value = current * SpeedMultiplier;
        if (!float.IsFinite(value)) return false;
        scaled = value;
        return true;
    }

    // ============================================================
    // 对外契约（runtime 接入面）
    // ============================================================

    /// <summary>当选时与每帧 Tick 调用：死地让位或幂等安装/维持。任何失败都不影响其它英雄效果。</summary>
    internal static void Reconcile(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            if (DeadlandsCovers(archer))
            {
                Restore(archer);          // 让 DL 的同倍率提升承担英雄提速，绝不给同一 mover 叠加
                return;
            }
            AttachCore(archer);
        }
        catch (Exception e)
        {
            LogOnce("reconcile", "movement reconcile failed; hero keeps native speeds: " + e.GetType().Name);
        }
    }

    /// <summary>幂等安装/维持认领（供测试与 runtime 直呼；死地覆盖时不做任何事）。</summary>
    internal static bool Attach(Archer archer) => AttachCore(archer, deadlandsCovers: DeadlandsCovers(archer));

    private static bool AttachCore(Archer archer, bool deadlandsCovers = false)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return false;
            if (deadlandsCovers) return false;                                  // DL 接管：让位
            IntPtr pointer; int goId;
            if (!Identity(archer, out pointer, out goId)) return false;

            Entry entry = Find(archer);
            if (entry == null)
            {
                entry = Rent();
                if (entry == null) return false;
                entry.Pointer = pointer;
                entry.GoId = goId;
                entry.Archer = archer;
            }
            else if (entry.Pending)
            {
                // 未归还前绝不再次提升；归还成功会释放该条目，这里重新登记身份。
                if (!Cleanup(entry, entry.Archer)) return false;
                entry.Pointer = pointer;
                entry.GoId = goId;
                entry.Archer = archer;
            }
            else
            {
                entry.Archer = archer;
                if (!StillOwner(entry, archer)) return false;
            }

            bool walkOk = ClaimField(entry, archer, walk: true);
            bool runOk = ClaimField(entry, archer, walk: false);
            if (walkOk || runOk)
            {
                LogFirstBoost(entry);
                return true;
            }
            LogOnce("unusable", "hero walk/run speeds unusable (non-finite or non-positive); movement left native");
            return false;
        }
        catch (Exception e)
        {
            LogOnce("attach", "movement attach failed; left native: " + e.GetType().Name);
            return false;
        }
    }

    /// <summary>
    /// CAS 归还（退役漏斗调用）。失败会把该条目保留为 Pending，由 <see cref="RetryCleanup"/> 继续；
    /// 对象已销毁/换主时没有字段可写，正常丢弃归还责任。
    /// </summary>
    internal static void Restore(Archer archer)
    {
        try
        {
            Entry entry = Find(archer);
            if (entry == null) return;
            entry.Archer = archer;                 // 保留一个可用 wrapper 供重试
            Cleanup(entry, archer);
        }
        catch (Exception e)
        {
            // Unknown identity is not destruction. Queue the bounded owned table for
            // verification using its saved wrappers; never discard restoration duties.
            for (int i = 0; i < Entries.Length; i++)
                if (Entries[i] != null) Entries[i].Pending = true;
            LogOnce("restore", "movement restore failed: " + e.GetType().Name);
        }
    }

    /// <summary>服务所有 Pending 归还（runtime Tick 每帧调用，且在任何开关门之前）。返回是否仍有 Pending。</summary>
    internal static bool RetryCleanup()
    {
        for (int i = 0; i < Entries.Length; i++)
        {
            Entry entry = Entries[i];
            if (entry == null || !entry.Pending) continue;
            try { Cleanup(entry, entry.Archer); }
            catch (Exception e) { LogOnce("retry", "pending movement restore failed; kept for the next retry: " + e.GetType().Name); }
        }
        return PendingCleanupCount > 0;
    }

    /// <summary>待归还条目数（runtime 可观察；0 = 无残留）。</summary>
    internal static int PendingCleanupCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < Entries.Length; i++)
                if (Entries[i] != null && Entries[i].Pending) count++;
            return count;
        }
    }

    /// <summary>已认领字段的条目数（测试/诊断用）。</summary>
    internal static int ClaimedCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < Entries.Length; i++)
            {
                Entry entry = Entries[i];
                if (entry != null && (entry.Walk.Owned || entry.Run.Owned)) count++;
            }
            return count;
        }
    }

    /// <summary>整体归还并清空；**绝不丢弃 Pending 条目**（仍被引用的归还责任要留到重试成功）。</summary>
    internal static void Clear()
    {
        for (int i = 0; i < Entries.Length; i++)
        {
            Entry entry = Entries[i];
            if (entry == null) continue;
            try { Cleanup(entry, entry.Archer); } catch (Exception) { }
            if (entry.Pending || entry.Walk.Owned || entry.Run.Owned)
            {
                LogOnce("clear-pending", "movement restore still pending after Clear; receipt kept for RetryCleanup");
                continue;
            }
            Entries[i] = null;
        }
        Logged.Clear();
    }

    /// <summary>清空全部表与日志（测试；只应在没有在用认领时调用）。</summary>
    internal static void ResetForTests()
    {
        for (int i = 0; i < Entries.Length; i++) Entries[i] = null;
        Logged.Clear();
        _loggedFirstBoost = false;
    }

    // ============================================================
    // Deadlands 让位（两条独立 ×1.5 绝不叠加）
    // ============================================================

    /// <summary>
    /// Deadlands 的 Mover.Update 提升是否会在本帧覆盖这个英雄：是 → 本模块让位（其倍率同为 ×1.5，
    /// 正好满足英雄 +50%）。逐条镜像 Mover_Update_DeadlandsSpeed_Patch 的准入
    /// （GameplayActive → ByMover 注册 → 非骑士 → 死地随从）；探针异常按「会让位」处理。
    /// </summary>
    internal static bool DeadlandsCovers(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return false;
            if (!PatchRoles_DeadlandsPowers.GameplayActive()) return false;
            Mover mover = archer._mover;
            if (mover == null) return false;
            if (!PatchRoles_DeadlandsPowers.ByMover.TryGetValue(mover.Pointer,
                out PatchRoles_DeadlandsPowers.UnitRef unit)) return false;
            if (unit.Knight != null) return false;                              // 骑士 mover：英雄不可能是骑士
            if (unit.Archer == null || unit.Archer.Pointer != archer.Pointer) return false;
            return PatchRoles_DeadlandsPowers.IsDeadlandsFollower(archer);
        }
        catch (Exception e)
        {
            LogOnce("deadlands", "deadlands probe failed; hero movement yields this frame: " + e.GetType().Name);
            return true;
        }
    }

    // ============================================================
    // 内部：身份 / 认领 / 归还
    // ============================================================

    private static bool Identity(Archer archer, out IntPtr pointer, out int goId)
    {
        pointer = IntPtr.Zero;
        goId = 0;
        if (archer == null || archer.gameObject == null) return false;
        // Let a failed read reach the caller. false means confirmed absent/mismatched,
        // whereas exceptions mean unknown and must keep the cleanup receipt.
        pointer = archer.Pointer;
        goId = archer.gameObject.GetInstanceID();
        if (pointer == IntPtr.Zero) return false;
        if (goId == 0) throw new InvalidOperationException("movement owner identity unavailable");
        return true;
    }

    private static Entry Find(Archer archer)
    {
        IntPtr pointer; int goId;
        if (!Identity(archer, out pointer, out goId)) return null;
        for (int i = 0; i < Entries.Length; i++)
        {
            Entry entry = Entries[i];
            if (entry == null || entry.Pointer == IntPtr.Zero) continue;
            if (entry.Pointer == pointer && entry.GoId == goId) return entry;
        }
        return null;
    }

    /// <summary>写字段前的 owner 复核：当前 wrapper 必须仍指向登记的那个 actor。</summary>
    private static bool StillOwner(Entry entry, Archer archer)
    {
        IntPtr pointer; int goId;
        if (!Identity(archer, out pointer, out goId)) return false;
        return entry.Pointer == pointer && entry.GoId == goId;
    }

    private static Entry Rent()
    {
        for (int i = 0; i < Entries.Length; i++)
        {
            Entry entry = Entries[i];
            if (entry != null && (entry.Pending || entry.Walk.Owned || entry.Run.Owned || entry.Archer != null
                || entry.Pointer != IntPtr.Zero)) continue;                 // 只在完全空闲的条目上出租
            if (entry == null)
            {
                entry = new Entry();
                Entries[i] = entry;
            }
            return entry;
        }
        LogOnce("cap", "hero movement table full (" + MaxHeroes + "); extra hero keeps native speeds");
        return null;
    }

    /// <summary>
    /// 单字段认领：**先登记回执**（Original/Written/Owned）再写字段；字段仍等于我们上次写入值时幂等返回
    /// （绝不二次 ×1.5）；第三方改过/上次写失败则以**当前值**为新基线重新认领（与 Banker 同语义，
    /// 归还时回到该基线，绝不覆盖对方的历史值）；非有限/非正值释放所有权并 fail-closed。
    /// </summary>
    private static bool ClaimField(Entry entry, Archer archer, bool walk)
    {
        float current;
        try { current = walk ? archer.walkSpeed : archer.runSpeed; }
        catch (Exception e)
        {
            LogOnce("read-" + Field(walk), "speed field unreadable: " + e.GetType().Name);
            return false;
        }

        bool owned = walk ? entry.Walk.Owned : entry.Run.Owned;
        float written = walk ? entry.Walk.Written : entry.Run.Written;
        if (owned && current == written) return true;              // 幂等：字段仍是我们的提升值

        if (!TryScale(current, out float target))
        {
            if (walk) entry.Walk.Owned = false; else entry.Run.Owned = false;
            LogOnce("unusable-" + Field(walk), "speed field unusable; ownership released");
            return false;
        }

        if (walk)
        {
            entry.Walk.Original = current;
            entry.Walk.Owned = true;
            entry.Walk.Written = target;
            try { archer.walkSpeed = target; }
            catch (Exception e)
            {
                LogOnce("write-walk", "walk speed write failed; receipt kept: " + e.GetType().Name);
                return false;
            }
            return true;
        }

        entry.Run.Original = current;
        entry.Run.Owned = true;
        entry.Run.Written = target;
        try { archer.runSpeed = target; }
        catch (Exception e)
        {
            LogOnce("write-run", "run speed write failed; receipt kept: " + e.GetType().Name);
            return false;
        }
        return true;
    }

    /// <summary>CAS 归还一个条目；返回 true = 完全释放（条目可再用）。</summary>
    private static bool Cleanup(Entry entry, Archer archer)
    {
        entry.Pending = true; // write before identity or field reads that can throw
        bool failed = false;
        bool ownerOk = archer != null && StillOwner(entry, archer);

        if (ownerOk)
        {
            if (!ReleaseField(entry, archer, walk: true)) failed = true;
            if (!ReleaseField(entry, archer, walk: false)) failed = true;
        }
        else
        {
            // 对象已销毁/换主：没有字段可写，放弃归还责任（绝不去猜别的 actor 的字段）。
            entry.Walk.Owned = false;
            entry.Run.Owned = false;
        }

        if (failed)
        {
            entry.Pending = true;
            LogOnce("pending", "movement restore incomplete; receipt kept for retry");
            return false;
        }

        ReleaseEntry(entry);
        return true;
    }

    /// <summary>
    /// 单字段归还：现值仍等于我们写入值才写回原值；第三方改过 → 保留对方值、放弃该字段所有权、绝不覆盖。
    /// 写失败返回 false（保留回执，由重试继续）。
    /// </summary>
    private static bool ReleaseField(Entry entry, Archer archer, bool walk)
    {
        OwnedSpeed slot = walk ? entry.Walk : entry.Run;
        if (!slot.Owned) return true;

        float current;
        try { current = walk ? archer.walkSpeed : archer.runSpeed; }
        catch (Exception e)
        {
            LogOnce("restore-read-" + Field(walk), "speed field unreadable during restore: " + e.GetType().Name);
            return false;
        }
        if (current != slot.Written)
        {
            if (walk) entry.Walk.Owned = false; else entry.Run.Owned = false;
            LogOnce("foreign-" + Field(walk), "speed field changed by another writer; value preserved");
            return true;
        }

        try
        {
            if (walk) archer.walkSpeed = slot.Original; else archer.runSpeed = slot.Original;
        }
        catch (Exception e)
        {
            LogOnce("restore-write-" + Field(walk), "speed restore write failed: " + e.GetType().Name);
            return false;
        }
        if (walk) entry.Walk.Owned = false; else entry.Run.Owned = false;
        return true;
    }

    private static void ReleaseEntry(Entry entry)
    {
        entry.Pointer = IntPtr.Zero;
        entry.GoId = 0;
        entry.Archer = null;
        entry.Walk = default;
        entry.Run = default;
        entry.Pending = false;
    }

    private static string Field(bool walk) => walk ? "walk" : "run";

    // ============================================================
    // 日志（有界）
    // ============================================================

    internal static void LogOnce(string key, string message)
    {
        try
        {
            if (Logged.Add(key)) KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[HeroMove] " + message);
        }
        catch (Exception) { }
    }

    private static void LogFirstBoost(Entry entry)
    {
        if (_loggedFirstBoost) return;
        _loggedFirstBoost = true;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[HeroMove] hero movement ×" + SpeedMultiplier
                + " active actor=" + entry.GoId + " walk=" + entry.Walk.Written + " run=" + entry.Run.Written);
        }
        catch (Exception) { }
    }
}
