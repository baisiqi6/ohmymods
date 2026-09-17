// 火铳手·运行时（runtime slice，operator 契约导出面；评审修订版）。
//
// 身份由 MusketeerIdentity 负责（marker/GUID/sidecar/存档桥）；本文件只负责
// **原生 Archer 上的战斗包**：箭 SO 克隆、原生 cadence 字段绝对写入、射程/扫描器、
// 射击决策地面过滤（临时、可逆）、地面岗位门、自有子弹闸，以及"配置关/失权/换世界"时的 CAS 归还。
//
// 契约导出（Operator 接线）：
// * <see cref="Tick"/>            —— ModPanel.Update：`MusketeerIdentity.Tick(); MusketeerRuntime.Tick();`
// * <see cref="IsMusketeer"/>     —— 身份谓词（= MusketeerIdentity.IsUnit，异常 fail-closed）
// * <see cref="StatusText"/>      —— 面板只读状态字符串（可选接线）
// * MusketeerCombat.TryHandleShot —— ArrowAttack.FireArrowInternal prefix（Operator 侧）
// * MusketeerVisuals.Sync        —— ModPanel.LateUpdate
//
// 容量与一致性（评审修订）：
// * 单位表 = 哈希表（pointer + GameObject InstanceID 复合键）+ 活动列表，容量对齐 identity sidecar 的
//   4096 条职业上限；每帧只遍历**活动**列表（无全表扫描），第 65 名及以后照常装包，绝不静默封顶。
// * 射速只认**未修改 prefab 基线**（含 min/max attempts；实例值与基线不一致 → 整包 refuse），写入是绝对的
//   ×2（区间收敛到中值，冷却按 E[N] 摊分），原生周期精确等于闸值；闸只作安全网并带帧量化容差。
// * 敌人扫描器**完全不改**（谓词/缓存都不动，ShouldFlee 的威胁视野原样保留）；射击决策改为在
//   原生 `ShouldShootEnemy` 返回边界上按原生候选顺序重选地面敌人（不装过滤、无缓存残留）。
// * 打猎目标收窄（不是放行白名单）：`_wildlifeScanner.additionalRequirements` 叠加"白天可猎普通鹿"
//   判据并与第三方先验 **AND**（任一侧异常 fail-closed）；兔子等小动物/夜猎/编队狩猎一概不放行，
//   `ShouldShootWildlife` 的编队/骑士/乘船门与 `ShouldHunt` 追猎返城行为原样保留。
// * 所有字段写入都有 before/applied 回执 + CAS 归还；回归失败保留回执重试，第三方接管则让位。

using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 单位表键：native pointer + GameObject InstanceID（IL2CPP wrapper 每帧可能是不同 C# 对象，
/// 绝不用引用相等认人；池复用导致指针复用时 InstanceID 不同）。
/// </summary>
internal readonly struct MusketeerUnitKey : IEquatable<MusketeerUnitKey>
{
    internal MusketeerUnitKey(IntPtr pointer, int goId)
    {
        Pointer = pointer;
        GoId = goId;
    }

    internal IntPtr Pointer { get; }
    internal int GoId { get; }

    public bool Equals(MusketeerUnitKey other) => Pointer == other.Pointer && GoId == other.GoId;
    public override bool Equals(object obj) => obj is MusketeerUnitKey other && Equals(other);
    public override int GetHashCode() => unchecked(Pointer.GetHashCode() * 397 ^ GoId);
}

/// <summary>运行时单位条目：一个精确 Archer 实例的全部借用凭据 + 闸。</summary>
internal sealed class MusketeerUnit
{
    internal Archer Archer;
    internal IntPtr Pointer;
    internal int GoId;
    internal bool Applied;
    internal bool ClaimLost;
    internal bool PendingDestroy;
    internal bool Released;

    // 箭 SO
    internal ArrowAttack BaseAttack;
    internal ArrowAttack Clone;
    internal bool CloneOwned;
    internal bool AttackRedirected;

    // cadence：绝对写入 + before 回执（三项一起记账）
    internal bool CadenceApplied;
    internal float PrepBefore;
    internal float PrepWrite;
    internal Vector2 IntervalBefore;
    internal Vector2 IntervalWrite;
    internal float CooldownBefore;
    internal float CooldownWrite;
    internal bool AttemptsApplied;
    internal int AttemptsMinBefore;
    internal int AttemptsMaxBefore;
    internal bool PerfectApplied;
    internal float PerfectBefore;
    internal float GateSeconds;

    // 射程（shootRange + 敌人扫描器）
    internal bool RangeApplied;
    internal float RangeBefore;
    internal float ScannerRangeBefore;
    internal float ScannerRangeBehindBefore;
    internal IntPtr ScannerPointer;
    internal float Range;

    // 射击决策：地面目标重选（原生 ShouldShootEnemy 边界；不改扫描器谓词/缓存）
    internal bool ReselectionLogged;

    // 打猎目标收窄（常驻：白天普通鹿；与第三方先验谓词 AND 组合）
    internal bool WildlifeFilterApplied;
    internal Scanner.ObjectCondition WildlifeFilterBefore;
    internal Scanner.ObjectCondition WildlifeFilterOurs;

    // 玩法闸
    internal float NextEligibleShotTime;
    internal int NativeAttempts;
    internal float LastShotTime;

    /// <summary>
    /// 绑定 lease（单调、绝不重用）：每成功装包一次 + 每次原生 OnEnable 回收（新 life）时递增。
    /// 自有子弹发射时快照它；命中鹿时比对——同 GO/Pointer 回池再生成新火铳手的旧弹绝不伤鹿。
    /// 只用于鹿资格，不改敌弹旧语义；不写存档 schema。
    /// </summary>
    internal long BindingLease;

    // 槽位收口（骑士/塔位）
    internal int EvictAttempts;
    internal float EvictRetryAt;
    internal bool EvictBusy;
}

/// <summary>世界原生 Archer prefab 的未修改基线（只读快照）。</summary>
internal struct MusketeerBaseline
{
    internal float ShootRange;
    internal float PrepTime;
    internal float CooldownTime;
    internal float CooldownReduction;
    internal Vector2 IntervalRange;
    internal int MinAttempts;
    internal int MaxAttempts;
    internal bool Valid;
}

internal static class MusketeerRuntime
{
    /// <summary>同时管理的火铳手上限 = identity sidecar 的职业记录上限（4096）；超出只限频记录，绝不静默丢。</summary>
    internal const int MaxUnits = 4096;
    /// <summary>槽位收口重试上限/间隔（原生 ExitGuardSlot 可能被同帧重排打断）。</summary>
    private const int MaxEvictAttempts = 3;
    private const float EvictRetrySeconds = 1f;
    private const string CloneName = "KEM_MusketeerAttack";
    private const float RangeMultiplier = 1.5f;

    private static readonly Dictionary<MusketeerUnitKey, MusketeerUnit> Table
        = new Dictionary<MusketeerUnitKey, MusketeerUnit>(256);
    private static readonly List<MusketeerUnit> Active = new List<MusketeerUnit>(256);
    private static readonly List<Archer> Scratch = new List<Archer>(64);
    private static readonly HashSet<string> Logged = new HashSet<string>();
    private static MusketeerBaseline _baseline;
    private static int _baselineWorldId;
    private static bool _loggedCapacity;
    /// <summary>绑定 lease 计数（进程内单调递增、绝不重用；<see cref="MusketeerUnit.BindingLease"/>）。</summary>
    private static long _bindingLeaseCounter;

    /// <summary>下一个绑定 lease（单调；Clear/换世界都不重置，只有测试钩子归零）。</summary>
    private static long NextBindingLease() => ++_bindingLeaseCounter;

    /// <summary>
    /// 该 Archer 当前绑定 lease（0 = 未装包/失权/未知）。供弹丸发射时快照。
    /// </summary>
    internal static long BindingLease(Archer archer)
    {
        try
        {
            MusketeerUnit unit = Find(archer);
            return unit != null && unit.Applied && !unit.ClaimLost ? unit.BindingLease : 0L;
        }
        catch (Exception)
        {
            return 0L;
        }
    }

    /// <summary>
    /// 快照的绑定 lease 是否仍是**同一 life**（0/未装包/已失权/被回收重用 → false）。
    /// 鹿命中专属：命中时用它证明射手与发射时是同一 life（InstanceID/Pointer 会被池复用）。
    /// </summary>
    internal static bool MatchesBindingLease(Archer archer, long lease)
    {
        try
        {
            if (lease == 0L || archer == null) return false;
            MusketeerUnit unit = Find(archer);
            return unit != null && unit.Applied && !unit.ClaimLost && unit.BindingLease == lease;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ============================================================
    // 契约导出
    // ============================================================

    /// <summary>ModPanel.Update 每帧调用（必须在 MusketeerIdentity.Tick 之后）。异常绝不外抛。</summary>
    internal static void Tick()
    {
        bool playing = MusketeerAccess.Playing;
        try { Reconcile(); }
        catch (Exception e) { Once("tick", "reconcile failed; musketeer package left as-is: " + e.GetType().Name); }
        try { EvictSlotted(); }
        catch (Exception e) { Once("evict", "slot eviction failed; native slot stays until retry: " + e.GetType().Name); }
        try { MusketeerCombat.Tick(Time.deltaTime, playing); }
        catch (Exception e) { Once("bullets", "bullet tick failed; bullets stay for the next frame: " + e.GetType().Name); }
    }

    /// <summary>该 Archer 是否带 MOD 火铳手身份（身份读取异常一律 fail-closed）。</summary>
    internal static bool IsMusketeer(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return false;
            return MusketeerIdentity.IsUnit(archer);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>身份有效且战斗包已装好（原生箭压制/自有子弹的前提）。</summary>
    internal static bool IsArmedMusketeer(Archer archer)
    {
        MusketeerUnit unit = Find(archer);
        return unit != null && unit.Applied && !unit.ClaimLost && !unit.PendingDestroy && MusketeerAccess.Enabled;
    }

    /// <summary>面板只读状态（无副作用；异常安全）。</summary>
    internal static string StatusText
    {
        get
        {
            try
            {
                int armed = 0;
                for (int i = 0; i < Active.Count; i++)
                    if (Active[i] != null && Active[i].Applied && !Active[i].ClaimLost) armed++;
                return "armed=" + armed + " shots=" + MusketeerCombat.SuppressedShotCount
                    + " bullets=" + MusketeerCombat.LiveCount
                    + " skipped=" + MusketeerCombat.SkippedFullTableShots;
            }
            catch (Exception)
            {
                return "unavailable";
            }
        }
    }

    /// <summary>显式闸是否打开（Combat 出膛前调用；未装包/未知 = false）。</summary>
    internal static bool GateOpen(Archer archer)
    {
        MusketeerUnit unit = Find(archer);
        if (unit == null || !unit.Applied || unit.ClaimLost) return false;
        float now;
        try { now = Time.time; }
        catch (Exception) { return false; }
        return now >= unit.NextEligibleShotTime;
    }

    /// <summary>
    /// 消费一次射击闸并返回新的 nextEligibleShotTime（视觉 Reload 窗口与玩法冷却同源）。
    /// 截止时间带 <see cref="MusketeerCadence.GateToleranceSeconds"/> 容差：合法周期早到几毫秒不被吞。
    /// </summary>
    internal static float ConsumeShotGate(Archer archer)
    {
        MusketeerUnit unit = Find(archer);
        if (unit == null || !unit.Applied) return 0f;
        float now;
        try { now = Time.time; }
        catch (Exception) { return 0f; }
        float deadline = MusketeerCadence.GateDeadline(now, unit.GateSeconds);
        unit.NextEligibleShotTime = deadline;
        unit.LastShotTime = now;
        return deadline;
    }

    /// <summary>该火铳手的子弹射程（基线 ×1.5）；未装包返回 0（调用方 fail-closed）。</summary>
    internal static float ConfiguredRange(Archer archer)
    {
        MusketeerUnit unit = Find(archer);
        return unit != null && unit.Applied ? unit.Range : 0f;
    }

    /// <summary>原生开火尝试观测（有界计数，只做诊断；绝不因此放行原生箭）。</summary>
    internal static void ObserveNativeShotAttempt(Archer archer)
    {
        MusketeerUnit unit = Find(archer);
        if (unit != null && unit.NativeAttempts < int.MaxValue) unit.NativeAttempts++;
    }

    /// <summary>自有子弹真实出膛观测（视觉 Fire/Reload 锚点已经由 Combat 直接通知 Visuals）。</summary>
    internal static void ObserveOwnShot(Archer archer, float nextEligibleShotTime)
    {
        MusketeerUnit unit = Find(archer);
        if (unit == null) return;
        unit.LastShotTime = Time.time;
        unit.NextEligibleShotTime = nextEligibleShotTime;
    }

    /// <summary>整体卸载/换世界：归还全部字段、销毁自有克隆与弹丸池、清空表（身份由 identity 模块负责）。</summary>
    internal static void Clear()
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            MusketeerUnit unit = Active[i];
            if (unit != null) Strip(unit, destroyAllowed: true);
        }
        Active.Clear();
        Table.Clear();
        MusketeerCombat.DespawnAll();
        MusketeerCombat.ResetPool();
        try { MusketeerVisuals.Clear(); } catch (Exception) { }
        _baseline = default;
        _baselineWorldId = 0;
        Logged.Clear();
        _loggedCapacity = false;
    }

    /// <summary>测试钩子：只清本地表/日志。</summary>
    internal static void ResetForTests()
    {
        Active.Clear();
        Table.Clear();
        _bindingLeaseCounter = 0L;   // 仅测试：生产单调不重置（子弹会随 Clear/ResetPool 一并丢弃）
        Logged.Clear();
        _baseline = default;
        _baselineWorldId = 0;
        _loggedCapacity = false;
        MusketeerCombat.ResetForTests();
    }

    // ============================================================
    // 每帧对账：装包 / 自愈 / 归还
    // ============================================================

    private static void Reconcile()
    {
        if (!MusketeerAccess.Enabled)
        {
            MusketeerCombat.DespawnAll();
            for (int i = 0; i < Active.Count; i++)
            {
                MusketeerUnit unit = Active[i];
                if (unit != null) Strip(unit, destroyAllowed: true);
            }
            CompactActive();
            return;
        }

        // 1) 已登记条目（只遍历活动列表，无全表扫描）：身份丢失 → 归还；待收尾 → 收尾；否则自愈。
        for (int i = 0; i < Active.Count; i++)
        {
            MusketeerUnit unit = Active[i];
            if (unit == null || unit.Released) continue;
            if (unit.PendingDestroy)
            {
                Strip(unit, destroyAllowed: true);
                continue;
            }
            Archer archer = unit.Archer;
            if (archer == null || archer.gameObject == null || !SameActor(unit, archer) || !IsMusketeer(archer))
            {
                Strip(unit, destroyAllowed: true);
                continue;
            }
            if (!unit.Applied) continue;                       // 等下一轮身份名单（或挂起）再装
            ReconcileApplied(unit, archer);
        }
        CompactActive();

        // 2) 身份名单（Identity 维护的当前有效绑定实例）：给未登记的实例装包（字典查找，O(1)）。
        Scratch.Clear();
        try { MusketeerIdentity.CopyUnits(Scratch); }
        catch (Exception e)
        {
            Once("copy-units", "identity unit list unavailable: " + e.GetType().Name);
            return;
        }
        for (int i = 0; i < Scratch.Count; i++)
        {
            Archer archer = Scratch[i];
            if (archer == null || archer.gameObject == null) continue;
            if (!MusketeerAccess.InWorld(archer)) continue;
            MusketeerUnit unit = Find(archer);
            if (unit == null)
            {
                unit = Rent(archer);
                if (unit == null) continue;
            }
            if (!unit.Applied) Apply(unit, archer);
            else ReconcileApplied(unit, archer);
        }
    }

    /// <summary>活动列表原地压缩（原地 RemoveAll 语义，无委托分配）。</summary>
    private static void CompactActive()
    {
        int write = 0;
        for (int read = 0; read < Active.Count; read++)
        {
            MusketeerUnit unit = Active[read];
            if (unit == null || unit.Released) continue;
            Active[write++] = unit;
        }
        if (write < Active.Count) Active.RemoveRange(write, Active.Count - write);
    }

    /// <summary>已装包实例的自愈：原生重置把 ActiveArrowAttack 写回基础箭时补回克隆；归还失败的回执继续重试。</summary>
    private static void ReconcileApplied(MusketeerUnit unit, Archer archer)
    {
        if (unit.PendingDestroy || unit.ClaimLost) return;
        try
        {
            if (!MusketeerVisuals.HasVisual(archer)) MusketeerVisuals.Apply(archer);
        }
        catch (Exception)
        {
        }
        try
        {
            ArrowAttack baseAttack = unit.BaseAttack;
            ArrowAttack clone = unit.Clone;
            if (clone == null || baseAttack == null) return;

            ArrowAttack current = archer._arrowAttack;
            if (current == null) return;
            ArrowAttack activeNow = archer.ActiveArrowAttack;
            // 火矢 buff（_fireArrowAttack）期间绝不改写 ActiveArrowAttack（原生 buff 拥有该字段）。
            ArrowAttack fire = archer._fireArrowAttack;
            if (activeNow != null && fire != null && activeNow.Pointer == fire.Pointer) return;
            if (current.Pointer == clone.Pointer)
            {
                if (activeNow == null || activeNow.Pointer != clone.Pointer) archer.ActiveArrowAttack = clone;
                return;
            }
            if (current.Pointer == baseAttack.Pointer && unit.AttackRedirected)
            {
                // 原生（Pool respawn/反序列化）把基础箭写回来了：补回我们的克隆。
                archer._arrowAttack = clone;
                archer.ActiveArrowAttack = clone;
                return;
            }
            // 第三方换走了箭 SO：让出所有权（fail-closed：不再压制原生箭，绝不覆盖别人的资产）。
            unit.ClaimLost = true;
            Once("claim-lost", "a third party replaced the archer arrow attack; musketeer package yields");
        }
        catch (Exception e)
        {
            Once("reconcile", "reconcile failed; package stays until the next frame: " + e.GetType().Name);
        }
    }

    // ============================================================
    // 装包
    // ============================================================

    private static void Apply(MusketeerUnit unit, Archer archer)
    {
        if (!MusketeerAccess.Enabled || unit.ClaimLost) return;
        if (!TryReadBaseline(out MusketeerBaseline baseline)) return;

        // 防御：绝不与弩手身份叠包（identity 合同是"一职业一身份"，这里只做最后一道门）。
        try
        {
            if (PatchRoles_Crossbowman.IsCrossbowman(archer))
            {
                Once("crossbow", "instance is a crossbowman; musketeer package refused");
                return;
            }
        }
        catch (Exception)
        {
        }

        ArrowAttack baseAttack = archer._arrowAttack;
        if (baseAttack == null || IsOwnedClone(baseAttack)) return;

        // 射速规划：E[N] 与三段时间都取**未修改 prefab 基线**；写入是绝对 ×2 且自检周期 == 闸。
        MusketeerCadenceValues prefab = new MusketeerCadenceValues(baseline.PrepTime, baseline.IntervalRange,
            baseline.CooldownTime, baseline.CooldownReduction, baseline.MinAttempts, baseline.MaxAttempts);
        MusketeerCadenceValues live = new MusketeerCadenceValues(archer.shootPrepTime, archer._shootIntervalRange,
            archer.shootCooldownTime, archer._cooldownReduction, archer.minAttempts, archer.maxAttempts);
        if (!MusketeerCadence.TryPlan(prefab, live, out MusketeerCadencePlan plan, out string reason))
        {
            Once("cadence-" + reason, "musketeer cadence refused (" + reason + "); instance stays native");
            return;
        }

        ArrowAttack clone = null;
        try
        {
            clone = UnityEngine.Object.Instantiate(baseAttack);
            if (clone == null) return;
            clone.name = CloneName;
            float magnitude = baseAttack._shotMagnitude;
            if (!float.IsFinite(magnitude) || magnitude <= 0f)
            {
                UnityEngine.Object.Destroy(clone);
                return;
            }
            // 射程 ∝ v²：×1.5 射程 = 初速 ×sqrt(1.5)；**绝不**改重力（不伪造抛物线当直线弹）。
            float speedFactor = Mathf.Sqrt(RangeMultiplier);
            clone._shotMagnitude = magnitude * speedFactor;
            float boosted = baseAttack._boostedShotMagnitude;
            if (float.IsFinite(boosted) && boosted > 0f) boosted *= speedFactor;
            else boosted = clone._shotMagnitude;
            clone._boostedShotMagnitude = boosted;

            unit.BaseAttack = baseAttack;
            unit.Clone = clone;
            unit.CloneOwned = true;

            // 1) 箭 SO 重定向（CAS：只有当前仍指向基线才写）。
            ArrowAttack currentAttack = archer._arrowAttack;
            if (currentAttack == null || currentAttack.Pointer != baseAttack.Pointer)
            {
                Strip(unit, destroyAllowed: true);   // 竞态/第三方替换：绝不留下未接线的克隆
                return;
            }
            archer._arrowAttack = clone;
            archer.ActiveArrowAttack = clone;
            unit.AttackRedirected = true;

            // 2) cadence 绝对写入（回执先记后写）。
            unit.GateSeconds = plan.GateSeconds;
            if (!ApplyCadenceWrites(unit, archer, plan))
            {
                Strip(unit, destroyAllowed: true);
                return;
            }

            // 3) 射程：未修改 prefab 基线 ×1.5（不硬编码 12）。
            float range = baseline.ShootRange * RangeMultiplier;
            if (!float.IsFinite(range) || range <= 0f)
            {
                Strip(unit, destroyAllowed: true);
                return;
            }
            unit.Range = range;
            if (!ApplyRangeFields(unit, archer, range))
            {
                Strip(unit, destroyAllowed: true);
                return;
            }

            // 4) 打猎目标收窄（常驻：白天普通鹿；与第三方先验谓词 AND）。射击决策的地面重选是**临时**的。
            ApplyWildlifeFilter(unit, archer);

            unit.NextEligibleShotTime = 0f;    // 装好即可射第一发（由显式闸接管后续节奏）
            unit.BindingLease = NextBindingLease();   // 新 life：单调 lease（旧弹的鹿资格随即失效）
            unit.Applied = true;
            unit.Archer = archer;
        }
        catch (Exception e)
        {
            Once("apply", "apply failed; rolled back to native: " + e.GetType().Name);
            Strip(unit, destroyAllowed: true);
        }
    }

    /// <summary>
    /// cadence 三处绝对写入（回执先记后写；写入值来自规划，绝不按现值乘）。
    /// 一份回执同时管三项：任何一项 CAS 不匹配就整体跳过（第三方改过的不覆盖）。
    /// </summary>
    private static bool ApplyCadenceWrites(MusketeerUnit unit, Archer archer, in MusketeerCadencePlan plan)
    {
        if (!float.IsFinite(plan.PrepWrite) || !float.IsFinite(plan.IntervalWrite.x)
            || !float.IsFinite(plan.CooldownWrite))
            return false;

        unit.PrepBefore = archer.shootPrepTime;
        unit.IntervalBefore = archer._shootIntervalRange;
        unit.CooldownBefore = archer.shootCooldownTime;
        unit.PrepWrite = plan.PrepWrite;
        unit.IntervalWrite = plan.IntervalWrite;
        unit.CooldownWrite = plan.CooldownWrite;
        unit.CadenceApplied = true;

        archer.shootPrepTime = unit.PrepWrite;
        archer._shootIntervalRange = unit.IntervalWrite;
        archer.shootCooldownTime = unit.CooldownWrite;

        // 一次满射 = 一发子弹：原生 attempts 随机门压成 1；perfect 随机门清零（固定伤害 2，无 perfect）。
        if (!unit.AttemptsApplied)
        {
            unit.AttemptsMinBefore = archer.minAttempts;
            unit.AttemptsMaxBefore = archer.maxAttempts;
            unit.AttemptsApplied = true;
            archer.minAttempts = 1;
            archer.maxAttempts = 1;
        }
        if (!unit.PerfectApplied)
        {
            unit.PerfectBefore = archer.perfectArrowProbability;
            unit.PerfectApplied = true;
            archer.perfectArrowProbability = 0f;
        }
        return true;
    }

    /// <summary>射程（shootRange + 敌人扫描器 range/rangeBehind）字段级 CAS。</summary>
    private static bool ApplyRangeFields(MusketeerUnit unit, Archer archer, float range)
    {
        if (!unit.RangeApplied)
        {
            unit.RangeBefore = archer.shootRange;
            unit.ScannerRangeBefore = float.NaN;
            unit.ScannerRangeBehindBefore = float.NaN;
            unit.RangeApplied = true;
        }
        archer.shootRange = range;

        Scanner scanner = archer._enemyScanner;
        if (scanner == null) return false;
        IntPtr scannerPointer = scanner.Pointer;
        if (scannerPointer == IntPtr.Zero) return false;
        if (unit.ScannerPointer != IntPtr.Zero && unit.ScannerPointer != scannerPointer) return false;   // 换了实例：让位
        unit.ScannerPointer = scannerPointer;
        if (float.IsNaN(unit.ScannerRangeBefore))
        {
            unit.ScannerRangeBefore = scanner.range;
            unit.ScannerRangeBehindBefore = scanner.rangeBehind;
        }
        scanner.range = range;
        scanner.rangeBehind = range;
        return true;
    }

    /// <summary>
    /// 打猎目标收窄：wildlife 扫描器叠加"白天可猎普通鹿"判据，并与**原生先验条件 AND**
    /// （第三方 mod 的条件仍在链上；不是放行白名单，兔子等小动物一律被拒）。
    ///
    /// 原生证据（Archer.cs `ShouldShootWildlife`）：扫描器只负责挑目标（`GetClosest`），
    /// 编队/骑士/乘船/昼夜（含 tutorial 夜间例外）等门由原生方法自己把；`additionalRequirements`
    /// 是扫描器唯一的过滤位。所以这里只把候选收窄到普通鹿（Deer 根组件 + 鹿自己的 Damageable +
    /// 白天），绝不触碰敌人扫描器（`ShouldFlee` 的威胁视野零改动）。
    ///
    /// 组合判据任一侧抛异常 → false（fail-closed：绝不把异常泄进原生扫描器调用链）。
    /// 闭包入口先判**射手 root 快照**（存活/活动/world）再判先验：池复用旧 life / 失活射手
    /// 直接拒绝，且绝不在 predicate 入口外读 `archer.gameObject`（对象销毁时该 getter 可能抛异常，
    /// 绕过内部 fail-closed 直接泄进原生链）。第三方 `scanner.AdditionalRequirements` 原样保留。
    /// 扫描器缓存 ≤1s（maxRate=1）且可能跨昼夜：发射前由 `MusketeerFoeFilter.IsDeerShotAllowed`
    /// 再复核一次（弹道命中侧同样复核）。回执先记后写；归还路径保持字段级 CAS + 第三方接管让位。
    /// </summary>
    private static void ApplyWildlifeFilter(MusketeerUnit unit, Archer archer)
    {
        if (unit.WildlifeFilterApplied) return;
        try
        {
            Scanner scanner = archer._wildlifeScanner;
            if (scanner == null) return;
            // 快照射手 root：闭包只读这个快照（不触碰 archer 的 getter），世界/生命周期门由组合判据内部判。
            GameObject sourceRoot = archer.gameObject;
            if (sourceRoot == null) return;
            Scanner.ObjectCondition prior = scanner.additionalRequirements;
            // source 有效 AND 先验 AND 白天普通鹿：猎杀目标只会比第三方条件更窄，绝不会更宽。
            System.Func<GameObject, bool> predicate = candidate =>
                MusketeerFoeFilter.PassesWildlifeCondition(prior, candidate, sourceRoot);
            Scanner.ObjectCondition composed = predicate;
            if (composed == null) return;

            unit.WildlifeFilterBefore = prior;
            unit.WildlifeFilterOurs = composed;
            unit.WildlifeFilterApplied = true;      // 先记所有权（setter 可能写一半才抛）
            scanner.SetExtraCondition(composed);
            Scanner.ObjectCondition readBack = scanner.additionalRequirements;
            if (readBack == null || readBack.Pointer != composed.Pointer)
            {
                RestoreWildlifeFilter(unit, archer);   // 写入未被接受：立即按回执归还
                return;
            }
        }
        catch (Exception e)
        {
            // 回执已在：留给 Tick/回归路径继续归还（绝不假装没装过）。
            Once("wildlife-filter", "hunting filter install failed; receipt kept for retry: " + e.GetType().Name);
        }
    }

    // ============================================================
    // 射击决策：地面目标重选（原生 ShouldShootEnemy 边界；扫描器缓存与撤退视野零接触）
    // ============================================================

    /// <summary>
    /// 原生 `ShouldShootEnemy` 返回后：若它选中了非地面目标（飞行/未验证/无效），
    /// 就地从**原生扫描器已缓存的候选列表**里重选第一个合法地面敌人；没有则本届不开火。
    ///
    /// 为什么不装临时扫描器过滤：`Scanner.Refresh` 会把过滤结果**缓存** ≤0.5s，
    /// 归还谓词也擦不掉那份缓存 → `ShouldFlee` 在同窗口内仍会"看不见"飞行单位。
    /// 这里改为只改本次决策的目标字段：扫描器缓存保持原生全量，撤退视野完全不受影响。
    /// 若该私有 helper 被 IL2CPP 内联/绕过（pit 17），钩子不命中时退化为
    /// "原生选目标 + 弹道侧地面校验"（仍然安全：绝不伤害非地面目标）；命中时有一次性 entered 日志。
    /// </summary>
    internal static void ReselectGroundTarget(Archer archer, ref bool result)
    {
        try
        {
            if (!result || archer == null || archer.gameObject == null) return;
            MusketeerUnit unit = Find(archer);
            if (unit == null || !unit.Applied || unit.ClaimLost) return;

            GameObject current = archer._shootingTarget;
            if (MusketeerFoeFilter.IsValidGroundFoe(current, archer.gameObject)) return;   // 原生选的地面敌人：不动

            if (!unit.ReselectionLogged)
            {
                unit.ReselectionLogged = true;
                Once("decision-reselect", "shoot-decision ground re-selection entered (native ShouldShootEnemy hook is live)");
            }

            GameObject replacement = FindFirstGroundFoe(archer);
            if (replacement != null)
            {
                archer._shootingTarget = replacement;   // 破掉飞行单位对射击选择的垄断
                return;
            }
            archer._shootingTarget = null;
            result = false;                             // 只有飞行/未验证目标：本方不开火
        }
        catch (Exception e)
        {
            Once("decision-reselect", "ground re-selection failed: " + e.GetType().Name);
        }
    }

    /// <summary>
    /// 从原生扫描器**已缓存**的候选列表里取第一个合法地面敌人（与原生 ShouldShootEnemy 的
    /// "结果顺序里第一个非低优先级候选"同序；不新增扫描、不改缓存、不装谓词）。
    /// </summary>
    private static GameObject FindFirstGroundFoe(Archer archer)
    {
        try
        {
            Scanner scanner = archer._enemyScanner;
            if (scanner == null) return null;
            int count = scanner.GetAll(out Il2CppReferenceArray<GameObject> candidates);
            if (candidates == null) return null;

            float selfX = archer.transform != null ? archer.transform.position.x : 0f;
            ArrowAttack active = archer.ActiveArrowAttack;
            for (int i = 0; i < count && i < candidates.Length; i++)
            {
                GameObject candidate = candidates[i];
                if (candidate == null || !candidate.activeInHierarchy) continue;
                if (!MusketeerFoeFilter.IsValidGroundFoe(candidate, archer.gameObject)) continue;
                if (active != null)
                {
                    float dx = Mathf.Abs(candidate.transform.position.x - selfX);
                    if (!(active.Range >= dx)) continue;      // 原生距离门（SO.Range，克隆已 ×1.5）
                }
                return candidate;
            }
            return null;
        }
        catch (Exception)
        {
            return null;   // 读不到一律不开火（fail-closed）
        }
    }

    private static bool RestoreWildlifeFilter(MusketeerUnit unit, Archer archer)
    {
        Scanner scanner;
        try { scanner = archer != null ? archer._wildlifeScanner : null; }
        catch (Exception) { return false; }
        return RestoreScannerFilter(scanner, ref unit.WildlifeFilterApplied,
            ref unit.WildlifeFilterOurs, ref unit.WildlifeFilterBefore);
    }

    /// <summary>
    /// 归还一次扫描器谓词：只回收**仍指向我们安装的那个委托**的字段（回读核对）；
    /// 第三方已接管（指针不同）→ 让位并清账；setter 抛异常/回读不匹配 → **保留回执**，下一帧重试。
    /// </summary>
    private static bool RestoreScannerFilter(Scanner scanner, ref bool applied,
        ref Scanner.ObjectCondition ours, ref Scanner.ObjectCondition before)
    {
        if (!applied) return true;
        try
        {
            if (scanner == null)
            {
                ClearFilterReceipt(ref applied, ref ours, ref before);   // 扫描器没了：无责任
                return true;
            }
            Scanner.ObjectCondition current = scanner.additionalRequirements;
            if (current == null || ours == null || current.Pointer != ours.Pointer)
            {
                ClearFilterReceipt(ref applied, ref ours, ref before);   // 第三方接管/字段已被换：清账让位
                return true;
            }
            scanner.SetExtraCondition(before);
            Scanner.ObjectCondition after = scanner.additionalRequirements;
            bool restored = (before == null && after == null)
                || (before != null && after != null && after.Pointer == before.Pointer);
            if (!restored) return false;                                  // 保留回执，下一帧重试
            ClearFilterReceipt(ref applied, ref ours, ref before);
            return true;
        }
        catch (Exception)
        {
            return false;                                                 // 保留回执
        }
    }

    private static void ClearFilterReceipt(ref bool applied, ref Scanner.ObjectCondition ours,
        ref Scanner.ObjectCondition before)
    {
        applied = false;
        ours = null;
        before = null;
    }

    // ============================================================
    // 归还（身份丢失 / 配置关 / 失权 / 换世界）
    // ============================================================

    /// <summary>
    /// 归还全部字段（字段级 CAS：只回收仍是我们写入的值）。destroyAllowed=false 时
    /// **不做任何 Destroy**（OnEnable prefix 可能位于物理回调里），把克隆销毁交给下一帧 Tick；
    /// 字段归还照做（写字段在物理回调里是安全的，销毁不是）。
    /// 只有「字段归还全部成功或已无责任」且「克隆已销毁/从未创建」时才 Release——否则保留回执重试。
    /// </summary>
    private static void Strip(MusketeerUnit unit, bool destroyAllowed)
    {
        if (unit == null) return;
        try
        {
            unit.Applied = false;
            unit.ClaimLost = false;
            Archer archer = unit.Archer;
            // 字段归还与销毁分离：prefix 路径（可能处于物理回调）照样归还字段，但不做任何 Destroy。
            bool ownerOk = archer != null && archer.gameObject != null && SameActor(unit, archer);

            bool restoresOk = true;
            if (ownerOk)
            {
                restoresOk &= RestoreWildlifeFilter(unit, archer);
                restoresOk &= RestoreRangeFields(unit, archer);
                restoresOk &= RestoreCadenceFields(unit, archer);
                restoresOk &= RestoreAttackRedirect(unit, archer);
            }
            else
            {
                ClearLedgerFlags(unit);
            }

            bool cloneSettled = SettleClone(unit, archer, ownerOk, destroyAllowed);
            if (restoresOk && cloneSettled)
            {
                unit.PendingDestroy = false;
                Release(unit);
            }
            else
            {
                unit.PendingDestroy = true;    // 保留回执：下一帧 Tick 继续收尾
            }
        }
        catch (Exception e)
        {
            unit.PendingDestroy = true;    // 归还失败保留条目，下一帧继续（绝不丢责任）
            Once("strip", "strip incomplete; retry next frame: " + e.GetType().Name);
        }
    }

    /// <summary>没有 owner（对象已销毁/换人）时清账：没有字段可归还，只清本地回执。</summary>
    private static void ClearLedgerFlags(MusketeerUnit unit)
    {
        ClearFilterReceipt(ref unit.WildlifeFilterApplied, ref unit.WildlifeFilterOurs, ref unit.WildlifeFilterBefore);
        unit.RangeApplied = false;
        unit.CadenceApplied = false;
        unit.AttemptsApplied = false;
        unit.PerfectApplied = false;
        unit.AttackRedirected = false;
    }

    /// <summary>
    /// 视觉克隆收尾：true = 已销毁或从未创建（可以 Release）；false = 仍被引用或本帧不允许销毁。
    /// </summary>
    private static bool SettleClone(MusketeerUnit unit, Archer archer, bool ownerOk, bool destroyAllowed)
    {
        if (!unit.CloneOwned || unit.Clone == null) return true;
        if (ownerOk && Referenced(archer, unit.Clone)) return false;   // 仍被 actor 字段引用：绝不销毁
        if (!destroyAllowed)
        {
            unit.PendingDestroy = true;      // 物理回调里不 Destroy：下一帧 Tick 收尾
            return false;
        }
        DestroyQuietly(unit.Clone);
        unit.CloneOwned = false;
        unit.Clone = null;
        return true;
    }

    private static bool RestoreAttackRedirect(MusketeerUnit unit, Archer archer)
    {
        if (!unit.AttackRedirected || unit.Clone == null)
        {
            unit.AttackRedirected = false;
            return true;
        }
        try
        {
            if (archer._arrowAttack != null && archer._arrowAttack.Pointer == unit.Clone.Pointer)
                archer._arrowAttack = unit.BaseAttack;
            ArrowAttack active = archer.ActiveArrowAttack;
            if (active != null && active.Pointer == unit.Clone.Pointer)
                archer.ActiveArrowAttack = unit.BaseAttack;
            unit.AttackRedirected = false;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool RestoreCadenceFields(MusketeerUnit unit, Archer archer)
    {
        try
        {
            if (unit.CadenceApplied)
            {
                if (MusketeerCadence.Near(archer.shootPrepTime, unit.PrepWrite))
                    archer.shootPrepTime = unit.PrepBefore;
                Vector2 interval = archer._shootIntervalRange;
                if (MusketeerCadence.Near(interval.x, unit.IntervalWrite.x)
                    && MusketeerCadence.Near(interval.y, unit.IntervalWrite.y))
                    archer._shootIntervalRange = unit.IntervalBefore;
                if (MusketeerCadence.Near(archer.shootCooldownTime, unit.CooldownWrite))
                    archer.shootCooldownTime = unit.CooldownBefore;
                unit.CadenceApplied = false;
            }
            if (unit.AttemptsApplied)
            {
                // CAS：只回收我们写入的 1；第三方改过的不动。
                if (archer.minAttempts == 1) archer.minAttempts = unit.AttemptsMinBefore;
                if (archer.maxAttempts == 1) archer.maxAttempts = unit.AttemptsMaxBefore;
                unit.AttemptsApplied = false;
            }
            if (unit.PerfectApplied)
            {
                if (MusketeerCadence.Near(archer.perfectArrowProbability, 0f))
                    archer.perfectArrowProbability = unit.PerfectBefore;
                unit.PerfectApplied = false;
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool RestoreRangeFields(MusketeerUnit unit, Archer archer)
    {
        if (!unit.RangeApplied) return true;
        try
        {
            if (MusketeerCadence.Near(archer.shootRange, unit.Range)) archer.shootRange = unit.RangeBefore;
            Scanner scanner = archer._enemyScanner;
            if (scanner != null && scanner.Pointer == unit.ScannerPointer)
            {
                if (!float.IsNaN(unit.ScannerRangeBefore) && MusketeerCadence.Near(scanner.range, unit.Range))
                    scanner.range = unit.ScannerRangeBefore;
                if (!float.IsNaN(unit.ScannerRangeBehindBefore) && MusketeerCadence.Near(scanner.rangeBehind, unit.Range))
                    scanner.rangeBehind = unit.ScannerRangeBehindBefore;
            }
            unit.RangeApplied = false;
            unit.ScannerPointer = IntPtr.Zero;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ============================================================
    // 岗位门（骑士/塔位）：只挡这两个岗位；已在槽位 → 原生 ExitGuardSlot 收口
    // ============================================================

    private static void EvictSlotted()
    {
        if (!MusketeerAccess.Enabled) return;
        for (int i = 0; i < Active.Count; i++)
        {
            MusketeerUnit unit = Active[i];
            if (unit == null || unit.Released || !unit.Applied || unit.ClaimLost) continue;
            Archer archer = unit.Archer;
            if (archer == null || archer.gameObject == null || !SameActor(unit, archer)) continue;
            GuardSlot slot;
            bool inSlot;
            try
            {
                slot = archer._guardSlot;
                inSlot = archer.inGuardSlot;
            }
            catch (Exception)
            {
                continue;
            }
            if (slot == null && !inSlot)
            {
                unit.EvictAttempts = 0;
                continue;
            }
            if (unit.EvictBusy || unit.EvictAttempts >= MaxEvictAttempts) continue;
            float now;
            try { now = Time.unscaledTime; }
            catch (Exception) { continue; }
            if (now < unit.EvictRetryAt) continue;
            unit.EvictBusy = true;
            unit.EvictAttempts++;
            unit.EvictRetryAt = now + EvictRetrySeconds;
            try
            {
                // 原生 ExitGuardSlot 负责 renderer/碰撞/移动/扫描器/槽位/钱包还原。
                archer.ExitGuardSlot();
            }
            catch (Exception e)
            {
                Once("evict-exit", "native ExitGuardSlot failed: " + e.GetType().Name);
            }
            finally
            {
                unit.EvictBusy = false;
            }
        }
    }

    private static bool AllowJob(Archer archer, GameObject job)
    {
        try
        {
            if (job == null || !IsMusketeer(archer)) return true;
            // 骑士岗位（jobObject 带 Knight）与塔位槽（GuardSlot）都拒绝；其余原生任务照旧。
            if (job.GetComponent<Knight>() != null) return false;
            return job.GetComponent<GuardSlot>() == null;
        }
        catch (Exception)
        {
            return true;   // 未知一律交还原生调用方（fail-open：岗位不是身份，误放不会毁身份）
        }
    }

    private static bool AllowSlot(Archer archer, GuardSlot slot)
    {
        try
        {
            if (slot == null) return true;
            return !IsMusketeer(archer);
        }
        catch (Exception)
        {
            return true;
        }
    }

    // ============================================================
    // 基线 / 表管理 / 小工具
    // ============================================================

    /// <summary>
    /// 读当前世界未修改的原生 Archer prefab 基线（Holder.tagCharacterPairs["Archer"]，
    /// 与弩手 EnsureAssets 同源）。holder/字段缺失 → false，整包不装（下次入口重试）。
    /// </summary>
    private static bool TryReadBaseline(out MusketeerBaseline baseline)
    {
        baseline = _baseline;
        int worldId = CurrentWorldId();
        if (baseline.Valid && worldId != 0 && worldId == _baselineWorldId) return true;
        baseline = default;
        try
        {
            Managers managers = Managers.Inst;
            Holder holder = managers != null ? managers.holder : null;
            if (holder == null || holder.tagCharacterPairs == null) return false;

            Character character = null;
            if (!holder.tagCharacterPairs.TryGetValue("Archer", out character) || character == null) return false;
            Archer prefabArcher = character.GetComponent<Archer>();
            if (prefabArcher == null) return false;
            if (prefabArcher._arrowAttack == null) return false;

            baseline = new MusketeerBaseline
            {
                ShootRange = prefabArcher.shootRange,
                PrepTime = prefabArcher.shootPrepTime,
                CooldownTime = prefabArcher.shootCooldownTime,
                CooldownReduction = prefabArcher._cooldownReduction,
                IntervalRange = prefabArcher._shootIntervalRange,
                MinAttempts = prefabArcher.minAttempts,
                MaxAttempts = prefabArcher.maxAttempts,
            };
            if (!float.IsFinite(baseline.ShootRange) || baseline.ShootRange <= 0f) return false;
            baseline.Valid = true;
            _baseline = baseline;
            _baselineWorldId = worldId;
            return true;
        }
        catch (Exception e)
        {
            Once("baseline", "native archer prefab baseline unavailable: " + e.GetType().Name);
            return false;
        }
    }

    private static MusketeerUnit Rent(Archer archer)
    {
        IntPtr pointer;
        int goId;
        if (!Identity(archer, out pointer, out goId)) return null;
        if (Table.Count >= MaxUnits)
        {
            if (!_loggedCapacity)
            {
                _loggedCapacity = true;
                Once("cap", "musketeer table at the identity career cap (" + MaxUnits + "); extra instances stay native");
            }
            return null;
        }

        MusketeerUnit unit = new MusketeerUnit
        {
            Archer = archer,
            Pointer = pointer,
            GoId = goId,
        };
        Table[new MusketeerUnitKey(pointer, goId)] = unit;
        Active.Add(unit);
        return unit;
    }

    private static void Release(MusketeerUnit unit)
    {
        if (unit.Released) return;
        unit.Released = true;
        Table.Remove(new MusketeerUnitKey(unit.Pointer, unit.GoId));

        unit.Archer = null;
        unit.Pointer = IntPtr.Zero;
        unit.GoId = 0;
        unit.BaseAttack = null;
        unit.Clone = null;
        unit.CloneOwned = false;
        unit.AttackRedirected = false;
        unit.Applied = false;
        unit.ClaimLost = false;
        unit.PendingDestroy = false;
        unit.CadenceApplied = false;
        unit.AttemptsApplied = false;
        unit.PerfectApplied = false;
        unit.RangeApplied = false;
        unit.WildlifeFilterApplied = false;
        unit.ReselectionLogged = false;
        unit.ScannerPointer = IntPtr.Zero;
        unit.Range = 0f;
        unit.GateSeconds = 0f;
        unit.BindingLease = 0L;   // 归还即无 life 资格（即使对象被池化复用也绝不复用旧 lease）
        unit.NextEligibleShotTime = 0f;
        unit.NativeAttempts = 0;
        unit.EvictAttempts = 0;
        unit.EvictRetryAt = 0f;
        unit.EvictBusy = false;
        // 活动列表由 CompactActive 原地压缩（绝不在这里做 O(n) 删除/全表扫描）。
    }

    private static MusketeerUnit Find(Archer archer)
    {
        IntPtr pointer;
        int goId;
        if (!Identity(archer, out pointer, out goId)) return null;
        MusketeerUnit unit;
        return Table.TryGetValue(new MusketeerUnitKey(pointer, goId), out unit) ? unit : null;
    }

    /// <summary>当前世界标识（gameLayer 的 InstanceID；取不到返回 0 = 不缓存基线）。</summary>
    private static int CurrentWorldId()
    {
        try
        {
            Transform world = MusketeerAccess.World;
            return world != null && world.gameObject != null ? world.gameObject.GetInstanceID() : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static bool Identity(Archer archer, out IntPtr pointer, out int goId)
    {
        pointer = IntPtr.Zero;
        goId = 0;
        if (archer == null || archer.gameObject == null) return false;
        try
        {
            pointer = archer.Pointer;
            goId = archer.gameObject.GetInstanceID();
        }
        catch (Exception)
        {
            return false;
        }
        return pointer != IntPtr.Zero && goId != 0;
    }

    private static bool SameActor(MusketeerUnit unit, Archer archer)
    {
        IntPtr pointer;
        int goId;
        return Identity(archer, out pointer, out goId) && unit.Pointer == pointer && unit.GoId == goId;
    }

    private static bool IsOwnedClone(ArrowAttack so)
    {
        if (so == null) return false;
        foreach (MusketeerUnit unit in Active)
        {
            if (unit == null || unit.Clone == null) continue;
            if (so.Pointer == unit.Clone.Pointer) return true;
        }
        return false;
    }

    private static bool Referenced(Archer archer, ArrowAttack clone)
    {
        if (clone == null || archer == null) return false;
        try
        {
            if (archer._arrowAttack != null && archer._arrowAttack.Pointer == clone.Pointer) return true;
            return archer.ActiveArrowAttack != null && archer.ActiveArrowAttack.Pointer == clone.Pointer;
        }
        catch (Exception)
        {
            return true;   // 读不到一律保守认为仍被引用（绝不销毁在用的克隆）
        }
    }

    private static void DestroyQuietly(UnityEngine.Object target)
    {
        if (target == null) return;
        try { UnityEngine.Object.Destroy(target); } catch (Exception) { }
    }

    private static void Once(string key, string message)
    {
        try
        {
            if (Logged.Count >= 64) return;
            if (Logged.Add(key)) KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[Musketeer] " + message);
        }
        catch (Exception) { }
    }

    // ============================================================
    // 钩子（长方法/稳定边界；与既有弩手/英雄钩子共用同一批原生目标）
    // ============================================================

    /// <summary>
    /// Archer.OnEnable prefix：池复用/停用重开的清理点。
    /// * 先无条件让**旧 life 的自有视觉退场**（`MusketeerVisuals.BeforeReuse`；即使新 life 仍是火铳手、
    ///   同一 pointer+GoId，也绝不沿用旧的 Fire/Reload/idle 时钟与旧的 native-hide 责任）；
    /// * 身份已失效（identity 名单不为真）→ 归还字段（**不 Destroy**：OnEnable 可能发生在物理回调里，
    ///   销毁交给下一帧 Tick/Sync）；
    /// * 身份仍在（普通隐藏重开）→ 保留包，原生主体重置 ActiveArrowAttack 后由 Tick 自愈。
    /// </summary>
    [HarmonyPatch(typeof(Archer), "OnEnable")]
    internal static class Archer_OnEnable_MusketeerPackage_Patch
    {
        [HarmonyPrefix]
        internal static void Prefix(Archer __instance)
        {
            try
            {
                if (__instance == null || __instance.gameObject == null) return;
                try { MusketeerVisuals.BeforeReuse(__instance); } catch (Exception) { }
                MusketeerUnit unit = Find(__instance);
                if (unit == null) return;
                // 原生 OnEnable 回收点 = 新 life：单调 lease 递增（即使同 GO/Pointer 被池复用，
                // 旧弹快照的 lease 也再不会匹配——鹿资格只在同一 life 内有效）。
                unit.BindingLease = NextBindingLease();
                if (IsMusketeer(__instance)) return;
                Strip(unit, destroyAllowed: false);
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>
    /// 射击决策地面重选（评审修订）：在原生 `ShouldShootEnemy` 返回后，若它选中了非地面目标
    /// （飞行/未验证/无效），就地按原生候选顺序重选第一个合法地面敌人；没有则本次不开火。
    /// 扫描器谓词与缓存**完全不动** → `ShouldFlee` 仍看到原始威胁列表（撤退行为保留），
    /// 也不会出现"过滤结果被 Scanner 缓存 ≤0.5s"的残留。私有 helper 若被 IL2CPP 内联（pit 17），
    /// 钩子不命中时退化为"原生选目标 + 弹道侧地面校验"（仍然安全）；命中时有一次性 entered 日志。
    /// </summary>
    [HarmonyPatch(typeof(Archer), "ShouldShootEnemy")]
    internal static class Archer_ShouldShootEnemy_MusketeerGroundSelection_Patch
    {
        [HarmonyPostfix]
        internal static void After(Archer __instance, ref bool __result)
        {
            try { ReselectGroundTarget(__instance, ref __result); }
            catch (Exception) { }
        }
    }

    /// <summary>骑士招募排除（Kingdom.FetchArchersForJob 逐个调 IsAvailableForJob）。</summary>
    [HarmonyPatch(typeof(Archer), nameof(Archer.IsAvailableForJob), new[] { typeof(GameObject) })]
    internal static class Archer_IsAvailableForJob_MusketeerExclusion_Patch
    {
        [HarmonyPostfix]
        internal static void After(Archer __instance, GameObject __0, ref bool __result)
        {
            if (!__result) return;
            try
            {
                if (!AllowJob(__instance, __0)) __result = false;
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>AssignJob 里内联了槽位写入，单靠 SetGuardSlot 门盖不住正常分配。</summary>
    [HarmonyPatch(typeof(Archer), nameof(Archer.AssignJob), new[] { typeof(GameObject) })]
    internal static class Archer_AssignJob_MusketeerExclusion_Patch
    {
        [HarmonyPrefix]
        internal static bool Before(Archer __instance, GameObject __0)
        {
            try { return AllowJob(__instance, __0); }
            catch (Exception) { return true; }
        }
    }

    [HarmonyPatch(typeof(Archer), "SetGuardSlot", new[] { typeof(GuardSlot) })]
    internal static class Archer_SetGuardSlot_MusketeerExclusion_Patch
    {
        [HarmonyPrefix]
        internal static bool Before(Archer __instance, GuardSlot __0)
        {
            try { return AllowSlot(__instance, __0); }
            catch (Exception) { return true; }
        }
    }

    [HarmonyPatch(typeof(Archer), "EnterGuardSlot", new[] { typeof(GuardSlot) })]
    internal static class Archer_EnterGuardSlot_MusketeerExclusion_Patch
    {
        [HarmonyPrefix]
        internal static bool Before(Archer __instance, GuardSlot __0)
        {
            try { return AllowSlot(__instance, __0); }
            catch (Exception) { return true; }
        }
    }
}
