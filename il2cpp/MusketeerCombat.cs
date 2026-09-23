// 火铳手·射击与弹道（runtime slice，评审修订版）。
//
// 职责边界（本文件只做这些）：
// 1. 原生箭的**无条件压制**入口：Operator 的 ArrowAttack.FireArrowInternal prefix 调
//    <see cref="MusketeerCombat.TryHandleShot"/>。活动火铳手（身份 + 战斗包已装）**一律压制**
//    原生箭（即使目标非法/还在冷却/未装填），绝不出现"火铳手射出弓矢"。
// 2. 自有直线子弹：显式时间闸 + 合法目标 → 从「已举枪」Aim 枪口锚点 [48,14] 出膛；
//    默认平飞（敌方/其它目标永远水平），**仅**合法鹿且枪口水平线不穿它自己的真实 Collider2D 时，
//    朝该碰撞体中部做一次固定直线微调（实际 bounds 计算、无 homing/曲线/加射程——2.4 Greek 鹿
//    低于枪口线，普通世界本来可平射命中）；有限射程/寿命、命中最近有效目标即停（兔/鸟等小动物完全透明）。
//    推进一步用**完整有效 dt**（低帧不再慢弹），
//    扫掠端点按剩余射程/寿命钳制（Linecast 本身即连续线段，大 dt 既不隧穿也不越界）。
//    弹道证据：可复用数组（稳态零 List 开销）→ 数组在软上限仍饱和时走官方
//    Physics2D.Linecast(ContactFilter2D, Il2CppSystem.Collections.Generic.List<RaycastHit2D>) 全量结果
//    （Unity 自动扩容并复用该 List）；流式取**最近有效**命中，绝不假设返回顺序、绝不因饱和吞伤。
//    弹丸记录池化复用（清旧 source/visual/world 引用）。不生成原生 Arrow 组件（因此不继承原生
//    Crusher 反弹/canBounce/随机火矢，也不继承原生箭/火焰材质与染色），不调用任何 SO 发声回调。
// 3. 类型门（射击/命中分开但共用判据）：敌人仍用原生等价的地面敌人判据；鹿用"Deer 根组件 +
//    鹿根自己的 Damageable + 白天"判据（发射前再复核编队/骑士/乘船）；兔子等小动物两侧都不放行。
//
// 明确的"不做"：无穿透、无 AOE、无灼烧、无 perfect 倍率、无随机散射/误差、无落水概率、
// 不写任何原生 HP 字段（伤害只经共同 CombatDamage.Submit(target, 2, source, DamageSource.Arrow)
// → 原生 ReceiveDamage；原生 shield/preDamage 处理原样保留；单目标异常隔离、绝不重试）、
// 不新增 RPC、不碰原生箭池。
//
// 射速权威（评审修订：单一、确定、可证）：
// * 基线取**未修改的原生 Archer prefab**（runtime 读取并传进来，min/max attempts 也在内），
//   绝不用可能被第三方改过的实例值当基线；实例值与基线不一致时**整包 fail-closed**（refuse，不装半套）。
//   "普通持续间隔" = 期望值 `prep + 区间中值 + max(0,cooldown-reduction)/E[N]`
//   （E[N] = Random.Range(int,int) 的期望：max>min 时 (min+max-1)/2，max==min 时 min；
//   每发都要 prep+interval，cooldown 每轮只结算一次）。
// * 写入是**绝对**的：prep×2、区间**收敛到中值×2**（去掉随机区间，周期因此确定）、
//   冷却写 `2×max(0,cooldown-reduction)/E[N] + reduction`、minAttempts=maxAttempts=1（火铳手一发一装填）、
//   perfect=0。于是本体周期**精确等于**闸 = 2×普通持续间隔，不存在"原生命中落在闸内被吞、整周期重来"。
// * 闸保留为安全网（第三方改字段/异常时仍限速），并带一个帧量化容差：
//   `nextEligible = t0 + 2×普通持续间隔 - GateToleranceSeconds`。
// * 这是**期望**节奏（非 perfect、不间断）：目标丢失/撤退/编队只会让原生更慢，不声称逐帧精确。

using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>一次射速规划的原生输入（值语义，便于纯测试）。</summary>
internal readonly struct MusketeerCadenceValues
{
    internal MusketeerCadenceValues(float prep, Vector2 interval, float cooldown, float reduction,
        int minAttempts, int maxAttempts)
    {
        Prep = prep;
        Interval = interval;
        Cooldown = cooldown;
        Reduction = reduction;
        MinAttempts = minAttempts;
        MaxAttempts = maxAttempts;
    }

    internal float Prep { get; }
    internal Vector2 Interval { get; }
    internal float Cooldown { get; }
    internal float Reduction { get; }
    internal int MinAttempts { get; }
    internal int MaxAttempts { get; }
}

/// <summary>射速规划的确定性结果：三处绝对写入 + 闸/普通间隔（秒）。</summary>
internal readonly struct MusketeerCadencePlan
{
    internal float PrepWrite { get; }
    internal Vector2 IntervalWrite { get; }
    internal float CooldownWrite { get; }
    internal float GateSeconds { get; }
    internal float OrdinarySeconds { get; }

    internal MusketeerCadencePlan(float prepWrite, Vector2 intervalWrite, float cooldownWrite,
        float gateSeconds, float ordinarySeconds)
    {
        PrepWrite = prepWrite;
        IntervalWrite = intervalWrite;
        CooldownWrite = cooldownWrite;
        GateSeconds = gateSeconds;
        OrdinarySeconds = ordinarySeconds;
    }
}

/// <summary>
/// 射速/冷却的纯数学（无 Unity 状态、无分配）：把"原生**持续**射击间隔"显式化并给出 ×2 的确定写入。
///
/// 原生证据（game-source/Assembly-CSharp-2.1.0/Archer.cs Shoot 协程约 1000–1060 行，2.4 字段同名已核）：
/// 协程开头 <c>int attempts = UnityEngine.Random.Range(minAttempts, maxAttempts)</c>（int 重载 **max 独占**；
/// max == min 时返回 min），随后**每次 attempt** 都做 Prepare → 等 <c>shootPrepTime</c> → FireArrow →
/// 等 Random(<c>_shootIntervalRange</c>.x,.y)（编队/撤退走 _shootIntervalRangeFormation）；
/// 协程结束才写一次 <c>_cooldown = shootCooldownTime</c>（骑士用 shootCooldownWithKnightTime、
/// 编队 override 更优先），再 <c>_cooldown -= _cooldownReduction</c>；<c>ShouldShootEnemy</c>
/// 以 <c>_cooldown &gt; 0</c> 为门。所以 N 发一轮里：prep+interval 各 N 次、cooldown 只结算一次，
/// "普通持续间隔"（非 perfect、不间断的期望值）= prep + 区间中值 + max(0, cooldown-reduction) / E[N]。
/// 这是**期望**节奏：目标丢失/撤退/编队会让原生更晚开火（实际只会更慢，不会更快）。
/// <c>ArrowAttack.ShotCooldownSeconds</c> 是 SO 上的随机值（`_shotCooldownSeconds.Random()`），
/// **不是** Archer 射击协程的一部分，故不采用它当基线。
/// </summary>
internal static class MusketeerCadence
{
    /// <summary>目标倍率：原生普通持续间隔 ×2（用户拍板"间隔约普通弓手 2 倍"）。</summary>
    internal const float Multiplier = 2f;

    /// <summary>闸容差（秒）：原生 Wait 的帧量化可能让合法周期早到几毫秒，绝不因此吞掉合法射击。</summary>
    internal const double GateToleranceSeconds = 0.05d;

    /// <summary>基线的合法区间（秒）：超出即视为读取失败，整包 fail-closed。</summary>
    internal const double MinOrdinarySeconds = 0.05d;
    internal const double MaxOrdinarySeconds = 60d;

    /// <summary>实例值 vs prefab 基线的允许偏差（原生/本模块都不该改实例上的这些字段）。</summary>
    internal const float SourceEpsilon = 1e-3f;

    /// <summary>写入后自检的允许误差（秒）：写入周期必须精确等于闸。</summary>
    private const double WriteCheckEpsilon = 1e-4d;

    /// <summary>
    /// 由（未修改的原生字段）推导"普通满射**持续**间隔"：
    /// `prep + 区间中值 + max(0, cooldown-reduction) / E[N]`。
    /// 原生 `Archer.Shoot`（game-source/Assembly-CSharp-2.1.0/Archer.cs 约 1000–1060 行）每轮
    /// `int attempts = UnityEngine.Random.Range(minAttempts, maxAttempts)`：**每个 attempt 都要
    /// prep + interval，cooldown 每轮只结算一次**，所以每次射击摊到的冷却要被 N 除。
    /// 任何非有限/负值/超界值返回 false（调用方整包 refuse）。
    /// </summary>
    internal static bool TryDeriveOrdinarySeconds(double shootPrepTime, double shootIntervalMid,
        double shootCooldownTime, double cooldownReduction, double expectedAttempts, out double ordinarySeconds)
    {
        ordinarySeconds = 0d;
        if (!double.IsFinite(shootPrepTime) || !double.IsFinite(shootIntervalMid)
            || !double.IsFinite(shootCooldownTime) || !double.IsFinite(cooldownReduction)
            || !double.IsFinite(expectedAttempts))
            return false;
        if (shootPrepTime < 0d || shootIntervalMid < 0d) return false;
        if (!(expectedAttempts >= 1d)) return false;

        double cooldown = shootCooldownTime - cooldownReduction;
        if (cooldown < 0d) cooldown = 0d;
        double total = shootPrepTime + shootIntervalMid + cooldown / expectedAttempts;
        if (!(total >= MinOrdinarySeconds) || total > MaxOrdinarySeconds) return false;
        ordinarySeconds = total;
        return true;
    }

    /// <summary>
    /// 原生 attempts 的期望值 E[N]：`UnityEngine.Random.Range(int,int)` 的 max **独占**，
    /// 因此 max &gt; min 时 E[N] = (min + max - 1) / 2；max == min 时 Range 直接返回 min（含端点）。
    /// 合法要求：min ≥ 1、max ≥ min、有限。非法 → false（整包 refuse）。
    /// </summary>
    internal static bool TryExpectedAttempts(int minAttempts, int maxAttempts, out double expected)
    {
        expected = 0d;
        if (minAttempts < 1 || maxAttempts < minAttempts) return false;
        expected = maxAttempts > minAttempts
            ? (minAttempts + (double)maxAttempts - 1d) * 0.5d
            : minAttempts;
        return double.IsFinite(expected) && expected >= 1d;
    }

    /// <summary>目标间隔（普通间隔 ×2）。</summary>
    internal static double TargetSeconds(double ordinarySeconds) => ordinarySeconds * Multiplier;

    /// <summary>区间中值（确定性基线；原生区间是随机，收敛到中值后周期确定，绝不再叠加随机）。</summary>
    internal static double IntervalMidpoint(Vector2 range) => (range.x + range.y) * 0.5d;

    /// <summary>
    /// 规划 ×2 的确定性写入（只接受"实例值仍等于未修改 prefab 基线"的实例）：
    /// `prep' = 2·prep`；`interval' = (2·mid, 2·mid)`（收敛到中值，周期确定）；
    /// `cooldown' = 2·max(0,cooldown-reduction)/E[N] + reduction`；
    /// 火铳手本体仍强制 one-shot（attempts=1），所以写入周期
    /// `prep' + interval' + (cooldown'-reduction)` 精确等于闸 = 2×普通持续间隔。
    /// E[N] 由 **prefab** min/max 推导（绝不用被改过的实例值）；尝试次数非法 → refuse。
    /// </summary>
    internal static bool TryPlan(in MusketeerCadenceValues prefab, in MusketeerCadenceValues live,
        out MusketeerCadencePlan plan, out string reason)
    {
        plan = default;
        reason = null;

        if (!TryExpectedAttempts(prefab.MinAttempts, prefab.MaxAttempts, out double expectedAttempts))
        {
            reason = "attempts-invalid";
            return false;
        }
        if (!TryDeriveOrdinarySeconds(prefab.Prep, IntervalMidpoint(prefab.Interval),
                prefab.Cooldown, prefab.Reduction, expectedAttempts, out double ordinary))
        {
            reason = "baseline-invalid";
            return false;
        }
        if (!Matches(prefab, live))
        {
            reason = "live-drift";
            return false;
        }

        float mid = (float)IntervalMidpoint(prefab.Interval);
        float prepWrite = prefab.Prep * Multiplier;
        Vector2 intervalWrite = new Vector2(mid * Multiplier, mid * Multiplier);
        float effective = prefab.Cooldown - prefab.Reduction;
        if (effective < 0f) effective = 0f;
        float cooldownWrite = (float)(effective * Multiplier / expectedAttempts) + prefab.Reduction;
        if (!float.IsFinite(prepWrite) || !float.IsFinite(intervalWrite.x) || !float.IsFinite(cooldownWrite))
        {
            reason = "write-nonfinite";
            return false;
        }

        double gate = TargetSeconds(ordinary);
        double appliedCycle = prepWrite + intervalWrite.x + (cooldownWrite - prefab.Reduction);
        if (Math.Abs(appliedCycle - gate) > WriteCheckEpsilon)
        {
            reason = "write-inconsistent";
            return false;
        }

        plan = new MusketeerCadencePlan(prepWrite, intervalWrite, cooldownWrite, (float)gate, (float)ordinary);
        return true;
    }

    /// <summary>闸值 → 实际写入的时间戳：带容差（合法周期早到几毫秒也放行）。</summary>
    internal static float GateDeadline(float now, float gateSeconds)
        => (float)(now + gateSeconds - GateToleranceSeconds);

    /// <summary>实例值与 prefab 基线是否一致（三段时间 + reduction + attempts）。</summary>
    internal static bool Matches(in MusketeerCadenceValues prefab, in MusketeerCadenceValues live)
        => Near(prefab.Prep, live.Prep)
            && Near(prefab.Interval.x, live.Interval.x)
            && Near(prefab.Interval.y, live.Interval.y)
            && Near(prefab.Cooldown, live.Cooldown)
            && Near(prefab.Reduction, live.Reduction)
            && prefab.MinAttempts == live.MinAttempts
            && prefab.MaxAttempts == live.MaxAttempts;

    /// <summary>CAS 归还比较（相对误差 1e-4，相对量级自适应）。</summary>
    internal static bool Near(float a, float b)
        => Mathf.Abs(a - b) <= 1e-4f * Mathf.Max(1f, Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)));
}

/// <summary>
/// 原生 `Archer.ShouldShootEnemy` 里与**射手状态**相关的 tag 判定输入（值语义；可整体快照、整体清空）：
/// * <see cref="TagBypassArmed"/> = 原生内层绕过臂：`_currentFormation != null ∥ inGuardSlot ∥
///   _embarkee.CanShootWhileEmbarked ∥ (_knight != null && _knight.isCharging)`。成立时
///   EnemySpawn/Unspittable/QuestStructure 不再被排除——原生普通地面弓手只在**自由站立**时排除这三类；
/// * <see cref="Embarked"/> = `_embarkee.IsEmbarked`：原生外层臂的输入——QuestStructure 且乘船时，
///   无论内层臂如何都一律排除。
/// 弹道命中只用**发射时快照**的本结构（<see cref="MusketeerBullet.FoeTagState"/>），绝不在飞行中重读
/// 射手状态：池复用/收旗/换 life 都不改变已出膛的弹。
/// </summary>
internal readonly struct GroundFoeTagState
{
    internal GroundFoeTagState(bool tagBypassArmed, bool embarked)
    {
        TagBypassArmed = tagBypassArmed;
        Embarked = embarked;
    }

    internal bool TagBypassArmed { get; }
    internal bool Embarked { get; }

    /// <summary>自由站立（无绕过臂）：读不到射手状态时的 fail-closed 回落——保持三标签排除，
    /// 绝不因读不到而放行（此时外层臂的 Embarked=false 不会放宽任何 tag，见 tag 门短路顺序）。</summary>
    internal static GroundFoeTagState FreeStanding => default;
}

/// <summary>
/// 射击/命中类型门（两处共用，保证一致）：
/// * **敌人**（地面）：原生等价判据（飞行/未验证拒绝、Enemies 层、tag/Damageable/invulnerable 门），
///   目标选择与弹道碰撞同用 <see cref="IsValidGroundFoe"/>——鹿绝不走这条门。
/// * **普通鹿**（白天狩猎）：`Deer` 根组件 + 鹿根自己的 Damageable + 白天；兔子等小动物、
///   Hind 坐骑、石化物一律拒绝。发射用 <see cref="IsDeerShotAllowed"/>（再复核原生猎鹿前置），
///   弹道用 <see cref="TryResolveShotTarget"/>（与敌人分支合流成"有效命中目标"解析）。
/// * 不采信名字/tag：候选可能挂在鹿根的子 collider 上（其他 mod 可能改层/加子碰撞），
///   身份只看原生组件。
///
/// 敌人判据来源与边界：
/// * 飞行/未验证一律拒绝：目标(或父级)带 <c>Squid</c> 组件 → false（Call of Olympus 的空中抓人单位，
///   仓库已有组件级排除先例 PatchDivine_FriendlyTroll）；带 <c>Enemy</c> 组件时 <c>Enemy.Type</c>
///   必须在**地面白名单**（TrollWeak/TrollMedium/ToughTroll/Ogre/Stealer/Crusher/Knight/Archer，
///   与 2.1 EnemyType 枚举一致）内，Squid 与**未验证的 Boss 系**（Boss/KillerBoss/GauntletBoss/
///   BossWithStealer）一律拒绝——这是保守选择，**不声称已覆盖全部地面 Boss**，待实测证据再放行。
/// * 不带 Enemy 组件的敌方结构（静态地面目标）允许：它们没有飞行能力；最终是否成敌仍由原生门决定。
/// * 其余门与原生普通地面弓手 ShouldShootEnemy 等价：Enemies 层、Damageable 存活/启用/
///   IsDamagedBy(Arrow)、invulnerable 且 ignoredWhenInvulnerable 时排除；另排除友军巨魔。
/// * 三标签（EnemySpawn/Unspittable/QuestStructure）按原生**两层臂**判定
///   （<see cref="GroundFoeTagState"/>）：只有射手"自由站立"（无编队、无守位、未乘船可射、
///   非骑士冲锋）才排除；编队/守位/乘船可射/骑士冲锋时绕过（编队出征可打传送门 QuestStructure 等），
///   而 QuestStructure 且乘船时被外层臂额外排除。弹道命中用**发射时快照**，绝不在飞行中重读射手状态。
/// </summary>
internal static class MusketeerFoeFilter
{
    private const string EnemiesLayerName = "Enemies";
    private const string WildlifeLayerName = "Wildlife";
    private const string EnemySpawnTag = "EnemySpawn";
    private const string UnspittableTag = "Unspittable";
    private const string QuestStructureTag = "QuestStructure";

    private static int _enemiesLayer = -1;
    private static int _wildlifeLayer = -1;

    /// <summary>Enemies 层索引（惰性缓存一次；解析失败返回 -1 → 调用方 fail-closed 且下次重试）。</summary>
    internal static int EnemiesLayerIndex()
    {
        if (_enemiesLayer >= 0) return _enemiesLayer;
        try
        {
            string name = null;
            try { name = Layers.Enemies; } catch (Exception) { }
            _enemiesLayer = LayerMask.NameToLayer(string.IsNullOrEmpty(name) ? EnemiesLayerName : name);
        }
        catch (Exception)
        {
            _enemiesLayer = -1;
        }
        return _enemiesLayer;
    }

    /// <summary>Wildlife 层索引（惰性缓存一次；解析失败返回 -1 → 弹道退回纯敌方层，绝不破坏夜战）。</summary>
    internal static int WildlifeLayerIndex()
    {
        if (_wildlifeLayer >= 0) return _wildlifeLayer;
        try { _wildlifeLayer = LayerMask.NameToLayer(WildlifeLayerName); }
        catch (Exception) { _wildlifeLayer = -1; }
        return _wildlifeLayer;
    }

    /// <summary>
    /// 弹道查询层掩码：Enemies | Wildlife（鹿在 Wildlife 层）。
    /// 敌方层解析失败 → 0（调用方 fail-closed：本段无命中证据，与旧行为一致）；
    /// Wildlife 层解析失败 → 只查敌方层（鹿打不到，但夜战/敌人弹道一个字节不变）。
    /// </summary>
    internal static int CombatLayerMask()
    {
        int enemies = EnemiesLayerIndex();
        if (enemies < 0) return 0;
        int mask = 1 << enemies;
        int wildlife = WildlifeLayerIndex();
        if (wildlife >= 0) mask |= 1 << wildlife;
        return mask;
    }

    /// <summary>EnemyType 地面白名单（2.1 枚举逐值；Squid/Boss 系一律拒绝）。</summary>
    internal static bool IsGroundEnemyType(EnemyType type)
    {
        switch (type)
        {
            case EnemyType.TrollWeak:
            case EnemyType.TrollMedium:
            case EnemyType.ToughTroll:
            case EnemyType.Ogre:
            case EnemyType.Stealer:
            case EnemyType.Crusher:
            case EnemyType.Knight:
            case EnemyType.Archer:
                return true;
            default:
                return false;   // Squid/Boss/KillerBoss/BossWithStealer/GauntletBoss/未知
        }
    }

    /// <summary>
    /// 扫描器过滤判据（只在"射击决策"临时安装，见 MusketeerRuntime.BeginShootDecision）：
    /// 只看"是否地面类"，不重复原生 per-target 门，避免比原生更严而改变职业的合法目标集。
    /// </summary>
    internal static bool IsGroundClassified(GameObject candidate)
    {
        try
        {
            if (candidate == null) return false;
            if (candidate.GetComponentInParent<Squid>() != null) return false;
            Enemy enemy = candidate.GetComponentInParent<Enemy>();
            if (enemy == null) return true;                  // 敌方结构：无飞行能力
            return IsGroundEnemyType(enemy.Type);
        }
        catch (Exception)
        {
            return false;   // 读不到一律拒绝（fail-closed）
        }
    }

    /// <summary>
    /// 原生等价的 tag 门（`ShouldShootEnemy` 对三类 tag 的逐字语义，两层臂）：
    /// `(!QuestStructure ∥ !IsEmbarked) && (绕过臂 ∥ (!EnemySpawn && !Unspittable && !QuestStructure))`。
    /// </summary>
    internal static bool PassesFoeTagGate(GameObject candidate, GroundFoeTagState tagState)
    {
        bool questStructure = candidate.CompareTag(QuestStructureTag);
        if (questStructure && tagState.Embarked) return false;   // 外层臂：乘船时 QuestStructure 一律不可选中
        if (tagState.TagBypassArmed) return true;                // 内层臂：编队/守位/乘船可射/骑士冲锋时三标签放行
        return !questStructure
            && !candidate.CompareTag(EnemySpawnTag)
            && !candidate.CompareTag(UnspittableTag);            // 自由站立：三标签仍排除（旧行为）
    }

    /// <summary>
    /// 从活体射手读取原生 tag 状态（发射门与重选器用）。**无条件读全每一臂**：任一字段读取异常或
    /// 不可得（例如 `_embarkee` 为空——真实 `Archer.Awake` 恒注入 Embarkee，空值只可能是池前/异常态）
    /// → 整组回落自由站立（= 保持三标签排除），绝不因读不到而放行、绝不让"读到的部分臂"放宽排除。
    /// </summary>
    internal static GroundFoeTagState ReadGroundFoeTagState(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return GroundFoeTagState.FreeStanding;
            Formation formation = archer._currentFormation;
            bool inGuardSlot = archer.inGuardSlot;
            Knight knight = archer._knight;
            bool knightCharging = knight != null && knight.isCharging;
            Embarkee embarkee = archer._embarkee;
            if (embarkee == null) return GroundFoeTagState.FreeStanding;
            bool embarked = embarkee.IsEmbarked;
            bool canShootWhileEmbarked = embarkee.CanShootWhileEmbarked;
            bool bypassArmed = formation != null || inGuardSlot || canShootWhileEmbarked || knightCharging;
            return new GroundFoeTagState(bypassArmed, embarked);
        }
        catch (Exception)
        {
            return GroundFoeTagState.FreeStanding;
        }
    }

    /// <summary>
    /// 原生等价的弹道目标校验（活体射手）：按**当前**射手状态求值 tag 门。
    /// 调用点 = 发射门（<see cref="IsValidShotTarget"/>）与射击决策重选器（ReselectGroundTarget/
    /// FindFirstGroundFoe）；弹道命中走快照重载，绝不在这里重读射手。
    /// </summary>
    internal static bool IsValidGroundFoe(GameObject candidate, Archer shooter)
    {
        try
        {
            if (shooter == null || shooter.gameObject == null) return false;
            return IsValidGroundFoe(candidate, shooter.gameObject, ReadGroundFoeTagState(shooter));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>原生等价的弹道目标校验：首个满足条件者才允许吃弹（tag 门用给定时点的状态：弹道命中 = 发射时快照）。</summary>
    internal static bool IsValidGroundFoe(GameObject candidate, GameObject shooterRoot, GroundFoeTagState tagState)
    {
        try
        {
            if (candidate == null || !candidate.activeInHierarchy) return false;
            if (shooterRoot == null || shooterRoot.transform == null) return false;

            Transform candidateTransform = candidate.transform;
            if (candidateTransform == null) return false;
            // 绝不打自己（枪口在身前，但几何上仍可能与本体重叠）。
            if (candidateTransform == shooterRoot.transform
                || candidateTransform.IsChildOf(shooterRoot.transform))
                return false;

            int layer = candidate.layer;
            if (layer != EnemiesLayerIndex()) return false;

            // 三标签（EnemySpawn/Unspittable/QuestStructure）：原生只在射手"自由站立"时排除；
            // 编队/守位/乘船可射/骑士冲锋时绕过，QuestStructure 乘船时被外层臂额外排除。
            if (!PassesFoeTagGate(candidate, tagState)) return false;

            if (candidate.GetComponentInParent<FriendlyTroll>() != null) return false;   // 友军巨魔绝不挨弹

            if (!IsGroundClassified(candidate)) return false;

            Damageable damageable = GetFoeDamageable(candidate);
            if (damageable == null || !damageable.enabled || damageable.isDead) return false;
            if (damageable.invulnerable && damageable.ignoredWhenInvulnerable) return false;
            return damageable.IsDamagedBy(DamageSource.Arrow);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// source（射手）有效门：**活着**（Damageable 未死/未禁用、Character 非 inert/grabbed）、
    /// 所属 GO 活动、当前 world。只判 GO active 不够（死亡/被抓/石化期间 GO 仍可能是 active）。
    /// 读不到 → false。放在**先验之前**求值：池复用旧射手/旧世界绝不再参与。
    /// </summary>
    internal static bool IsUsableSource(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return false;
            if (!archer.enabled || !archer.gameObject.activeInHierarchy) return false;
            // Identity can be released synchronously before Runtime.Tick strips its package.
            if (!MusketeerRuntime.IsMusketeer(archer)) return false;
            Damageable damageable = archer._damageable;
            if (damageable == null || !damageable.enabled || damageable.isDead) return false;
            Character character = archer._character;
            if (character == null || character.inert || character.grabbed) return false;
            return MusketeerAccess.InWorld(archer);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 子弹记录的 shooter root → 当前 Archer 实例（命中时才解析；拿不到 = null，鹿门 fail-closed）。
    /// 不缓存实例：池复用/换 life 后必须按当前对象重新判定。
    /// </summary>
    internal static Archer ResolveSourceArcher(GameObject shooterRoot)
    {
        try
        {
            if (shooterRoot == null) return null;
            return shooterRoot.TryGetComponent<Archer>(out Archer archer) ? archer : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// "白天可猎普通鹿"判据（打猎扫描器与弹道命中共用，发射前另有 <see cref="IsDeerShotAllowed"/> 复核）：
    /// * source（射手）必须**活着**/活动/当前 world（先判它，池复用旧身份直接拒绝；由调用方传入已解析的实例）；
    /// * 候选 → 父链必须找到原生 `Deer` 根组件（绝不认名字或 tag；其他 mod 加的子 collider 也认得），
    ///   且鹿根必须**当前 world**（旧世界/池化残留的鹿绝不参与）；
    /// * 只认**鹿根自己的** Damageable：启用/未死/接受箭伤/非"无敌且被忽略"；
    /// * 鹿根石化（Petrifiable.IsPetrified）→ 拒绝（石化雕像不碰）；
    /// * 白天（`Kingdom.isDaytime`）：原生 tutorial 夜间例外**不适用**于火铳手，读不到一律拒绝（fail-closed）；
    /// * Hind 坐骑没有 Deer 根组件 → 自然拒绝；兔子/鸟/鱼等小动物同理。
    /// 返回 true 时输出可提交的原生 Damageable（鹿根上的那一个），且绝不指向射手自己。
    /// </summary>
    internal static bool TryGetHuntableDeer(Archer shooter, GameObject candidate, out Damageable damageable)
    {
        damageable = null;
        try
        {
            if (!IsUsableSource(shooter)) return false;
            if (candidate == null || !candidate.activeInHierarchy) return false;
            GameObject shooterRoot = shooter.gameObject;
            if (shooterRoot.transform == null) return false;

            Deer deer = candidate.GetComponentInParent<Deer>();
            if (deer == null || deer.gameObject == null || !deer.gameObject.activeInHierarchy) return false;
            if (!MusketeerAccess.InWorld(deer.gameObject)) return false;   // 鹿根必须当前 world
            Transform root = deer.transform;
            if (root == null) return false;
            // 绝不打自己（枪口在身前，几何上仍可能与本体重叠）。
            if (root == shooterRoot.transform || root.IsChildOf(shooterRoot.transform)) return false;

            Petrifiable petrifiable = deer.GetComponent<Petrifiable>();
            if (petrifiable != null && petrifiable.IsPetrified) return false;
            // 骑乘用坐骑（Steed/Hind）绝不当猎物：仓库既有"普通鹿"判据同款组件排除
            // （PatchWorld_DeerPopulation：Deer 且非 Steed/Hind 才算普通鹿）。
            if (deer.GetComponent<Steed>() != null || deer.GetComponent<Hind>() != null) return false;

            Damageable own = deer.GetComponent<Damageable>();
            if (own == null || !own.enabled || own.isDead) return false;
            if (own.invulnerable && own.ignoredWhenInvulnerable) return false;
            if (!own.IsDamagedBy(DamageSource.Arrow)) return false;
            if (!IsDaytimeNow()) return false;

            damageable = own;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>当前是否白天（`Kingdom.isDaytime`）。读不到一律 false（fail-closed：绝不夜猎）。</summary>
    internal static bool IsDaytimeNow()
    {
        try
        {
            Managers managers = Managers.Inst;
            Kingdom kingdom = managers != null ? managers.kingdom : null;
            return kingdom != null && kingdom.isDaytime;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 鹿猎适用性（发射与**命中**共用）：source 有效（活着/活动/world）+ 身份/战斗包仍在
    /// （<see cref="MusketeerRuntime.IsArmedMusketeer"/>，池复用旧 life/失权后绝不再猎鹿）+
    /// 原生 `ShouldShootWildlife` 同类前置（不在编队 / 不是骑士随从 / 未乘船）。
    /// 敌人分支**绝不调用这里**（夜战/编队射击不受影响）。任一读不到 → false。
    /// </summary>
    internal static bool IsDeerHuntApplicable(Archer archer)
    {
        try
        {
            if (!IsUsableSource(archer)) return false;
            if (!MusketeerRuntime.IsArmedMusketeer(archer)) return false;
            if (archer.GetFormation() != null) return false;      // 原生猎鹿排除：编队（含 PlayerFormation）
            if (archer._knight != null) return false;             // 原生猎鹿排除：骑士随从
            Embarkee embarkee = archer._embarkee;
            if (embarkee != null && embarkee.IsEmbarked) return false;   // 原生猎鹿排除：乘船
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 鹿目标解析（发射/命中共用）：适用性（source/身份/编队/骑士/乘船）**且**白天普通鹿有效。
    /// 返回可提交的鹿根 Damageable。
    /// </summary>
    internal static bool TryResolveDeerTarget(Archer archer, GameObject candidate, out Damageable damageable)
    {
        damageable = null;
        try
        {
            if (!IsDeerHuntApplicable(archer)) return false;
            return TryGetHuntableDeer(archer, candidate, out damageable);
        }
        catch (Exception)
        {
            damageable = null;
            return false;
        }
    }

    /// <summary>
    /// wildlife 扫描器的组合判据：**source 有效 AND 原生先验（其他 mod 的条件，可为 null）AND 白天普通鹿**。
    /// 先解析并判 source（捕获时快照的射手 root；**绝不在入口外读 `archer.gameObject`**，
    /// 也绝不让异常泄出闭包），再判先验、再判鹿；任一侧抛异常 → false（fail-closed：
    /// 宁可本次无目标，绝不把异常泄进原生扫描器调用链，也不放宽第三方条件）。
    /// 缓存/昼夜/编队变化由发射与命中侧再复核。
    /// </summary>
    internal static bool PassesWildlifeCondition(Scanner.ObjectCondition prior, GameObject candidate,
        GameObject sourceRoot)
    {
        Archer source;
        try
        {
            source = ResolveSourceArcher(sourceRoot);
            if (!IsUsableSource(source)) return false;       // 池复用旧身份/死亡/失活射手：先拒，再谈先验
            if (prior != null && !prior.Invoke(candidate)) return false;
        }
        catch (Exception)
        {
            return false;
        }
        return TryGetHuntableDeer(source, candidate, out _);
    }

    /// <summary>
    /// 猎鹿发射最终门（扫描器缓存可能跨昼夜/编队/身份变化，发射前必须复核）：
    /// source 有效 + 身份/战斗包仍在 + 白天普通鹿 + 原生 `ShouldShootWildlife` 同类前置
    /// （不在编队 / 不是骑士随从 / 未乘船）。
    /// 敌人**不走这里**（敌人继续用 <see cref="IsValidGroundFoe"/>）。任一读不到 → false。
    /// </summary>
    internal static bool IsDeerShotAllowed(Archer archer, GameObject target)
        => TryResolveDeerTarget(archer, target, out _);

    /// <summary>候选父链上的原生 `Deer` 根组件（鹿身份的唯一入口；不认名字/tag/层）。null = 不是鹿。</summary>
    internal static Deer GetDeerRoot(GameObject candidate)
    {
        try
        {
            return candidate != null ? candidate.GetComponentInParent<Deer>() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>候选父链上是否有原生 `Deer` 根组件（鹿语义的入口；不认名字/tag/层）。</summary>
    internal static bool IsDeerCandidate(GameObject candidate) => GetDeerRoot(candidate) != null;

    /// <summary>
    /// 发射/命中共用的"有效目标"总门（类型门分开）：
    /// * 鹿候选（父链带 Deer，**不管它在哪一层**）→ 一律走鹿门 <see cref="IsDeerShotAllowed"/>：
    ///   白天 + 原生猎鹿前置 + 鹿根自有 Damageable。夜里的鹿绝不当敌人打（不发弹、也不浪费子弹）。
    /// * 其余候选 → 原生等价的地面敌人门 <see cref="IsValidGroundFoe"/>（按当前射手状态求值 tag 臂：
    ///   编队/守位/乘船可射/骑士冲锋时 EnemySpawn/Unspittable/QuestStructure 不再排除，
    ///   QuestStructure 乘船时仍被外层臂排除）。
    /// * 兔子等小动物两条门都进不去（不在这里开白名单）。
    /// 弹道侧用 <see cref="TryResolveShotTarget"/> 复用同一套判据（tag 门改用发射时快照）并解析出可提交的 Damageable。
    /// </summary>
    internal static bool IsValidShotTarget(Archer archer, GameObject target)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return false;
            if (IsDeerCandidate(target)) return IsDeerShotAllowed(archer, target);
            return IsValidGroundFoe(target, archer);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 弹道命中的统一解析（发射/命中同一套判据）：返回 false = 该候选对火铳弹完全透明
    /// （不伤害也不阻挡——兔子等小动物正是这条路径）。
    /// * 鹿优先：候选父链带 `Deer` → 必须整体通过鹿门（**命中时重新复核** source 活着/活动/world、
    ///   身份/战斗包、**发射时快照的绑定 lease**、白天、编队/骑士/乘船、鹿根自有 Damageable）。
    ///   射手发射后加入编队/骑士、被停用/死亡/失权、或同一 GO 回池后作为新 life 重新武装
    ///   （lease 变化）→ 本次鹿命中作废（透明），等下一次决策。
    /// * 其余候选走原生等价的地面敌人判据（**不加昼夜/编队/身份/lease 门**：敌弹旧语义不变），
    ///   其中 tag 门用**发射时快照** <paramref name="tagState"/>（不在飞行中重读射手状态：
    ///   收旗/入队/乘船/池复用都不改变已出膛的弹）；Crusher 免疫位照旧消费子弹、不掉血。
    /// </summary>
    internal static bool TryResolveShotTarget(GameObject candidate, GameObject shooterRoot, long shooterLease,
        GroundFoeTagState tagState, out Damageable target, out bool immunityConsume)
    {
        target = null;
        immunityConsume = false;
        try
        {
            if (candidate == null || !candidate.activeInHierarchy) return false;
            if (IsDeerCandidate(candidate))
            {
                Archer shooter = ResolveSourceArcher(shooterRoot);
                // 同一 life 证明：InstanceID/Pointer 会被池复用，只有单调 lease 能证明"就是发射那一发的那条命"。
                if (!MusketeerRuntime.MatchesBindingLease(shooter, shooterLease)) return false;
                if (!TryResolveDeerTarget(shooter, candidate, out Damageable deerDamageable)) return false;
                target = deerDamageable;
                return true;
            }
            if (!IsValidGroundFoe(candidate, shooterRoot, tagState)) return false;
            target = GetFoeDamageable(candidate);
            if (target == null) return false;
            immunityConsume = IsImmunityConsume(candidate);
            return true;
        }
        catch (Exception)
        {
            target = null;
            immunityConsume = false;
            return false;
        }
    }

    /// <summary>命中即被消费但不掉血的免疫态：Crusher 且 !IsStunned（原生 Arrow.HitObject 同判据）。</summary>
    internal static bool IsImmunityConsume(GameObject candidate)
    {
        try
        {
            if (candidate == null) return false;
            Crusher crusher = candidate.GetComponentInParent<Crusher>();
            return crusher != null && !crusher.IsStunned;
        }
        catch (Exception)
        {
            return false;   // 读不到免疫位时不作免疫处理（仍受 IsValidGroundFoe 的原生门约束）
        }
    }

    internal static Damageable GetFoeDamageable(GameObject candidate)
    {
        try
        {
            return candidate != null ? candidate.GetComponentInParent<Damageable>() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// 一条自有子弹（纯托管对象 + 池化可视化；不入原生池、不挂原生组件）。记录本身也池化复用：
/// 归还前会清空视觉/射击者等全部引用，绝不把旧发射者、旧世界或已归还的视觉二次消费。
/// </summary>
internal sealed class MusketeerBullet
{
    internal GameObject Visual;
    internal SpriteRenderer Renderer;
    internal Vector2 Position;
    /// <summary>单位方向向量：默认水平 (±1, 0)；仅合法鹿的"水平线不穿其碰撞体"时朝鹿身中部微倾。</summary>
    internal Vector2 Direction;
    internal float Speed;
    internal float MaxDistance;
    internal float Travelled;
    internal float Age;
    internal float Lifetime;
    internal bool Alive;
    internal GameObject ShooterRoot;
    /// <summary>发射时快照的射手绑定 lease（鹿命中用它证明"同一 life"；0 = 未装包快照）。</summary>
    internal long ShooterLease;
    /// <summary>发射时快照的射手 tag 状态（<see cref="GroundFoeTagState.TagBypassArmed"/> 内层绕过臂 +
    /// <see cref="GroundFoeTagState.Embarked"/> 外层臂）：命中解析只用这份快照，绝不在飞行中重读射手状态。
    /// <see cref="ReleaseRecord"/> 归还时必须整体清空——池复用残留会让未武装的弹 fail-open。</summary>
    internal GroundFoeTagState FoeTagState;
    /// <summary>租约序号（每次从池中租出时递增；long 不会在正常玩法下溢出，生产路径无需重置）：
    /// 重入回调后的旧帧绝不推进新租的同一条记录。</summary>
    internal long Lease;
}

/// <summary>
/// 火铳手射击实现（Unity 侧）：一次射击的完整事务 + 有界可增长子弹表。
/// 所有入口异常隔离（绝不外抛进原生调用链）；物理缓冲按需增长且可复用（数组饱和由完整 List 兜底），
/// 弹丸记录/视觉池化复用，稳态零逐帧分配；共同伤害提交一次、绝不重试。
/// </summary>
internal static class MusketeerCombat
{
    /// <summary>
    /// 同时存活子弹上限 = 支持的职业总数上限（与 identity sidecar / <see cref="MusketeerRuntime.MaxUnits"/>
    /// 的 4096 对齐）：一次全职业齐射也不会丢弹，绝不拿"平均占用"当容量论证；只有真到 4096 发同时在空
    /// 才限频记录并跳过（诊断计数 `SkippedFullTableShots`）。
    /// </summary>
    internal const int MaxLiveBullets = MusketeerRuntime.MaxUnits;

    /// <summary>子弹直线速度（units/s；本地显示常量，非随机、非原生 SO 参数）。</summary>
    internal const float BulletSpeed = 30f;
    /// <summary>寿命安全上限（秒）：射程/速度之外的第二道界。</summary>
    internal const float MaxLifetimeSeconds = 1.2f;
    /// <summary>子弹伤害（用户拍板基础伤害 2；无 perfect 倍率、无 AOE、无灼烧）。</summary>
    internal const int BulletDamage = 2;

    /// <summary>
    /// 物理命中数组的初始容量与软上限：数组按需 32→256 扩容并复用（低密度场景零 List 索引开销）；
    /// 到软上限仍饱和 → 换官方 List overload 拿**完整**结果，绝不再有"到硬上限直接吞弹"的路径。
    /// </summary>
    private const int InitialHitCapacity = 32;
    private const int MaxArrayCapacity = 256;

    /// <summary>退休回执上限与重试间隔：Destroy 抛异常时保留自有引用，绝不因为一次异常丢掉所有权。</summary>
    private const int MaxRetiredVisuals = 64;
    private const float RetiredRetrySeconds = 1f;

    /// <summary>
    /// 鹿弹微调的最小水平距（几何退化保护：枪口几乎正对鹿身时不做近垂直/反向射击，
    /// 留给原生转身后的下一次决策）。这是几何退化阈值，不是任何世界/生物的尺寸常量。
    /// </summary>
    private const float MinDeerAimDx = 0.05f;

    /// <summary>鹿相关被动事件日志上限（每世界前 N 条；换世界重置，绝不每帧刷屏、无全场扫描）。</summary>
    private const int MaxDeerLogsPerWorld = 8;

    /// <summary>Operator 提供的弹丸贴图（5x3、Point、PPU32、pivot 居中）。</summary>
    private const string BulletResourceName = "KingdomEnhancedMod.MusketeerBullet.png";
    private const int BulletSpriteWidth = 5;
    private const int BulletSpriteHeight = 3;
    private const float BulletSpritePixelsPerUnit = 32f;

    private enum BulletSpriteState
    {
        Unknown = 0,
        Ready = 1,
        Unavailable = 2,
    }

    private static readonly MusketeerBullet[] Bullets = new MusketeerBullet[MaxLiveBullets];
    /// <summary>空闲弹丸记录（LIFO 复用；归还前已清空全部引用），避免每发 new MusketeerBullet。</summary>
    private static readonly List<MusketeerBullet> RecordPool = new List<MusketeerBullet>(16);
    private static Il2CppStructArray<RaycastHit2D> _hitBuffer;
    /// <summary>数组饱和时的完整结果列表（惰性创建一次；官方 List overload 会自动扩容并复用该 List）。</summary>
    private static Il2CppSystem.Collections.Generic.List<RaycastHit2D> _hitList;
    private static readonly List<GameObject> VisualPool = new List<GameObject>(16);
    /// <summary>Destroy 抛异常时保留的自有弹丸引用（退休回执）：下次机会继续销毁，绝不静默丢引用。</summary>
    private static readonly List<GameObject> RetiredVisuals = new List<GameObject>(4);

    private static IntPtr _worldPointer;
    private static int _worldGoId;
    private static float _nextRetiredRetryAt;

    private static int _count;
    /// <summary>租约计数器（<see cref="MusketeerBullet.Lease"/> 用 long：正常玩法不可能溢出，
    /// 生产路径无需重置，也就不会出现“重置后旧租约大于新帧边界”的判定混淆）。</summary>
    private static long _leaseCounter;
    /// <summary>推进重入门：伤害回调里的嵌套 Tick 一律 no-op（同一颗弹一帧只推进一次）。</summary>
    private static bool _ticking;
    private static int _suppressedShots;
    private static int _skippedFullTableShots;
    private static bool _loggedMissingVisual;
    private static bool _loggedPhysicsUnavailable;
    private static bool _loggedTableFull;
    private static BulletSpriteState _bulletSpriteState;
    private static Sprite _bulletSprite;
    /// <summary>本世界已输出的鹿事件日志条数（上限 <see cref="MaxDeerLogsPerWorld"/>；随世界身份重置）。</summary>
    private static int _deerLogs;

    /// <summary>当前存活子弹数（只读诊断）。</summary>
    internal static int LiveCount => _count;

    /// <summary>被压制的原生箭次数（只读诊断）。</summary>
    internal static int SuppressedShotCount => _suppressedShots;

    /// <summary>因子弹表满而跳过的射击次数（诊断；绝不静默）。</summary>
    internal static int SkippedFullTableShots => _skippedFullTableShots;

    /// <summary>物理命中数组容量（诊断/测试；饱和时由完整 List 兜底，不再是吞伤上限）。</summary>
    internal static int HitCapacity => _hitBuffer != null ? _hitBuffer.Length : 0;

    /// <summary>池中空闲弹丸视觉数（诊断/测试）。</summary>
    internal static int PooledVisualCount => VisualPool.Count;

    /// <summary>退休回执数（Destroy 失败后仍被我们持有的自有引用；诊断/测试）。</summary>
    internal static int RetiredVisualCount => RetiredVisuals.Count;

    /// <summary>当前绑定的世界（gameLayer InstanceID；0 = 未绑定）。</summary>
    internal static int BoundWorldGoId => _worldGoId;

    /// <summary>测试钩子：取第 index 条在场子弹的可视化（越界返回 null）。</summary>
    internal static GameObject VisualForTests(int index)
        => index >= 0 && index < _count ? Bullets[index].Visual : null;

    /// <summary>测试钩子：取第 index 条在场子弹的记录（越界返回 null）；验证池化复用与字段清理。</summary>
    internal static MusketeerBullet LiveRecordForTests(int index)
        => index >= 0 && index < _count ? Bullets[index] : null;

    /// <summary>测试钩子：把一条视觉塞进空闲池（模拟跨世界/失活父层留下的旧池条目）。</summary>
    internal static void InjectPooledVisualForTests(GameObject visual)
    {
        if (visual == null) return;
        if (VisualPool.Count >= MaxLiveBullets) return;
        VisualPool.Add(visual);
    }

    /// <summary>测试钩子：读第 index 条空闲池条目（越界返回 null）。</summary>
    internal static GameObject PooledVisualForTests(int index)
        => index >= 0 && index < VisualPool.Count ? VisualPool[index] : null;

    // ============================================================
    // 世界作用域（换岛/换世界的生命周期守卫）
    // ============================================================

    /// <summary>当前世界是否可用：gameLayer 存在、未销毁、且层级处于激活状态。</summary>
    private static bool TryGetUsableWorld(out Transform world, out IntPtr pointer, out int goId)
    {
        world = null;
        pointer = IntPtr.Zero;
        goId = 0;
        try
        {
            Transform candidate = MusketeerAccess.World;
            if (candidate == null || candidate.gameObject == null) return false;
            if (!candidate.gameObject.activeInHierarchy) return false;   // 失活旧层：绝不出膛
            pointer = candidate.Pointer;
            goId = candidate.gameObject.GetInstanceID();
            if (pointer == IntPtr.Zero || goId == 0) return false;
            world = candidate;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 世界作用域守卫（Tick 与出膛前必调）：
    /// * 世界身份（pointer + InstanceID）变化 或 当前世界不可用（失活/已销毁）→ 立刻丢弃自有在场子弹
    ///   （旧世界的弹绝不在新世界继续飞/伤害）并销毁自有弹丸池（只动自有对象）；
    /// * 同一世界内的暂停、开关切换、镜头移动一律不做任何事（池与在场子弹原样保留）；
    /// * 贴图/材质与世界无关：这里绝不重置、绝不重载。
    /// 返回当前可用世界（null = 本帧不射击）。
    /// </summary>
    private static Transform EnsureWorldScope()
    {
        Transform world;
        IntPtr pointer;
        int goId;
        if (!TryGetUsableWorld(out world, out pointer, out goId))
        {
            _worldPointer = IntPtr.Zero;
            _worldGoId = 0;
            if (_count > 0) DespawnAll();
            ResetPoolObjects();
            RetryRetired();
            return null;
        }

        if (pointer != _worldPointer || goId != _worldGoId)
        {
            _worldPointer = pointer;
            _worldGoId = goId;
            if (_count > 0) DespawnAll();   // 先在旧世界里丢弹，再丢掉承载它们的池对象
            ResetPoolObjects();
            _deerLogs = 0;                  // 每世界重置鹿事件日志预算（复用同一处世界身份检测）
        }
        RetryRetired();
        return world;
    }

    /// <summary>销毁池中全部自有视觉（不含退休回执；Destroy 失败者转入退休回执）。</summary>
    private static void ResetPoolObjects()
    {
        for (int i = VisualPool.Count - 1; i >= 0; i--)
        {
            GameObject visual = VisualPool[i];
            VisualPool.RemoveAt(i);
            Retire(visual);
        }
    }

    /// <summary>销毁一条自有视觉；<c>Destroy</c> 抛异常时保留退休回执（绝不丢掉自有引用）。</summary>
    private static void Retire(GameObject visual)
    {
        if (visual == null) return;   // 已被销毁/为空：无需回执
        try
        {
            UnityEngine.Object.Destroy(visual);
        }
        catch (Exception)
        {
            if (!RetiredVisuals.Contains(visual))
                RetiredVisuals.Add(visual);
        }
    }

    /// <summary>重试退休回执（限频；已销毁的条目直接销账）。</summary>
    private static void RetryRetired()
    {
        if (RetiredVisuals.Count == 0) return;
        float now;
        try { now = Time.unscaledTime; }
        catch (Exception) { return; }
        if (now < _nextRetiredRetryAt) return;
        _nextRetiredRetryAt = now + RetiredRetrySeconds;

        for (int i = RetiredVisuals.Count - 1; i >= 0; i--)
        {
            GameObject visual = RetiredVisuals[i];
            if (visual == null)
            {
                RetiredVisuals.RemoveAt(i);
                continue;
            }
            try
            {
                UnityEngine.Object.Destroy(visual);
                RetiredVisuals.RemoveAt(i);
            }
            catch (Exception)
            {
                // 保留回执，下一轮再试。
            }
        }
    }

    /// <summary>
    /// Operator prefix 入口（ArrowAttack.FireArrowInternal）：
    /// 活动火铳手**一律压制**原生箭（true = 跳过原方法）；其它情况一律放行（false = 原生照旧）。
    /// 身份/开关读取失败一律 fail-closed 放行，绝不在不确定时吞掉原生箭。
    /// </summary>
    internal static bool TryHandleShot(ArrowAttack attack, GameObject source)
    {
        try
        {
            if (!MusketeerAccess.Enabled) return false;
            if (attack == null || source == null) return false;

            if (!source.TryGetComponent<Archer>(out Archer archer) || archer == null) return false;
            if (!MusketeerRuntime.IsMusketeer(archer)) return false;
            // 身份在但战斗包未装好（资产失败/世界变更窗口）：放行原生，绝不让火铳手无弹可用。
            if (!MusketeerRuntime.IsArmedMusketeer(archer)) return false;

            _suppressedShots++;
            MusketeerRuntime.ObserveNativeShotAttempt(archer);
            try
            {
                TryFireOwnBullet(archer);
            }
            catch (Exception)
            {
                // 自有弹失败不影响压制：原生箭已被吞掉（避免弓矢外观），下一发按闸重试。
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 一次自有射击：显式时间闸 + 合法目标 + 出膛。任一前置不满足都不消耗闸（下一发重试）。
    /// 类型门分开：敌人走原生等价的 <see cref="MusketeerFoeFilter.IsValidGroundFoe"/>；
    /// 鹿只在"白天 + 原生猎鹿前置（无编队/无骑士/未乘船）"下放行（<see cref="MusketeerFoeFilter.IsDeerShotAllowed"/>），
    /// 兔子等小动物不在这两条门里（弹道侧同样透明）。
    /// </summary>
    internal static bool TryFireOwnBullet(Archer archer)
    {
        if (!MusketeerAccess.Playing) return false;
        // 出膛前先过世界守卫：换世界时丢弃旧池/旧弹；当前世界失活则本帧不出膛（绝不打隐形伤害）。
        Transform world = EnsureWorldScope();
        if (world == null) return false;
        if (!MusketeerRuntime.GateOpen(archer)) return false;

        GameObject target;
        try { target = archer._shootingTarget; }
        catch (Exception) { return false; }
        // 发射门：鹿候选一律走鹿门（白天 + 原生猎鹿前置），其余走原生等价敌人门；
        // 兔子等小动物两条门都进不去（弹道侧同样透明）。
        if (!MusketeerFoeFilter.IsValidShotTarget(archer, target)) return false;
        if (!ShooterReady(archer)) return false;

        if (!TryComputeMuzzle(archer, out Vector2 origin, out float facing)) return false;
        // 鹿瞄准证据不足/几何退化 → 本次不开火（敌人恒水平发射，完全不受影响）。
        if (!TryComputeShotDirection(target, origin, facing, out Vector2 direction)) return false;
        float range = MusketeerRuntime.ConfiguredRange(archer);
        if (!(range > 0f) || !float.IsFinite(range)) return false;

        if (_count >= MaxLiveBullets)
        {
            _skippedFullTableShots++;
            if (!_loggedTableFull)
            {
                _loggedTableFull = true;
                Log("bullet table full (" + MaxLiveBullets + "); further shots are skipped until bullets land");
            }
            return false;
        }

        // 绑定 lease 快照（鹿资格命中时比对；敌方命中不使用）：同 GO 回池再武装的新 life 与旧弹分离。
        long shooterLease = MusketeerRuntime.BindingLease(archer);
        // tag 状态快照（敌方命中解析用它；发射门刚按同一射手状态放行——命中绝不在飞行中重读射手。
        // 与 ShooterLease 同款"发射时快照"语义；池回收清理见 ReleaseRecord）。
        GroundFoeTagState foeTagState = MusketeerFoeFilter.ReadGroundFoeTagState(archer);
        if (!TryCreateBullet(archer, archer.ActiveArrowAttack, origin, direction, range, world, shooterLease, foeTagState))
            return false;

        LogDeerShot(target, origin, direction, range);   // 被动、每世界有界；非鹿直接 no-op
        float nextEligible = MusketeerRuntime.ConsumeShotGate(archer);
        MusketeerRuntime.ObserveOwnShot(archer, nextEligible);
        MusketeerVisuals.NotifyShot(archer, nextEligible);
        return true;
    }

    /// <summary>
    /// 本次出膛方向（单位向量）；**false = 本次不开火**（鹿缺瞄准证据/几何退化 → 等原生转身或下一轮决策）。
    /// * 非鹿目标（含一切敌人）：恒 `(facing, 0)` 平射并返回 true——敌方弹道绝不受影响；
    /// * 鹿候选：只认**鹿根自身**的 `Collider2D`（**绝不**用 `GetComponentInChildren`：可能拿到物理脚圈
    ///   而不是 Wildlife body），要求 collider enabled、所在 GO 活动、bounds 有限且非空；鹿根必须
    ///   当前 world、且非 `Steed`/`Hind` 坐骑。枪口水平线穿其 bounds 且目标在面向一侧 → 平射
    ///   （原尺寸鹿的常态）；否则朝 bounds 中心做一次固定直线微调——但目标在枪口背后，或
    ///   `|Δx| < MinDeerAimDx` 时**拒绝**（绝不反向背射、绝不近垂直猜射）。
    /// 方向在**发射时快照**：不追踪移动的鹿、无 homing、无曲线、不增加射程（Travelled/MaxDistance
    /// 仍是同一欧氏预算）。
    /// </summary>
    private static bool TryComputeShotDirection(GameObject target, Vector2 origin, float facing,
        out Vector2 direction)
    {
        direction = new Vector2(facing, 0f);
        try
        {
            Deer deer = MusketeerFoeFilter.GetDeerRoot(target);
            if (deer == null) return true;                                   // 非鹿：恒平射
            if (deer.gameObject == null) return false;
            if (!MusketeerAccess.InWorld(deer.gameObject)) return false;     // 鹿根必须当前 world
            if (deer.GetComponent<Steed>() != null || deer.GetComponent<Hind>() != null) return false;

            Collider2D body = deer.GetComponent<Collider2D>();               // 只认鹿根自身的 body collider
            if (body == null || !body.enabled) return false;
            if (body.gameObject == null || !body.gameObject.activeInHierarchy) return false;
            Bounds bounds = body.bounds;
            float minX = bounds.min.x;
            float maxX = bounds.max.x;
            float minY = bounds.min.y;
            float maxY = bounds.max.y;
            if (!float.IsFinite(minX) || !float.IsFinite(maxX)
                || !float.IsFinite(minY) || !float.IsFinite(maxY))
                return false;
            if (!(maxX > minX) || !(maxY > minY)) return false;              // 空 bounds：无瞄准证据

            float dx = bounds.center.x - origin.x;
            bool ahead = facing >= 0f ? dx > 0f : dx < 0f;
            if (!ahead) return false;                                        // 目标在枪口背后：不背射，等原生转身
            if (origin.y >= minY && origin.y <= maxY) return true;           // 平射本来穿身：照旧水平
            if (Mathf.Abs(dx) < MinDeerAimDx) return false;                  // Δx 近 0（退化）：不猜

            float dy = bounds.center.y - origin.y;
            float length = Mathf.Sqrt(dx * dx + dy * dy);
            if (!(length > 1e-4f) || !float.IsFinite(length)) return false;  // 归一化必须有限非零
            direction = new Vector2(dx / length, dy / length);
            return true;
        }
        catch (Exception)
        {
            direction = new Vector2(facing, 0f);
            return false;
        }
    }

    /// <summary>
    /// 每帧推进（runtime Tick 调用；暂停/关闭/离线不推进、不伤害）。
    /// 步长在**施法前**按剩余射程与剩余寿命钳制（用完整有效 dt，低帧不再慢弹；大 dt 由连续
    /// Linecast 线段保证不隧穿/不越界）；扫掠段 [prev,next]、最近有效命中即消费。
    /// 重入与边界（评审门）：
    /// * 同步伤害回调里的嵌套 Tick 一律 no-op（<c>_ticking</c> + try/finally）：同一颗弹一帧只推进一次，
    ///   回调期间新租的记录也绝不被嵌套 Tick 或本帧推进；
    /// * 每次**真实伤害回调**返回时最小复核 world 身份/可用性、功能开关与暂停/失权门（不做逐单位扫描），
    ///   任一已变 → 中止本帧剩余推进（旧 world 的其它弹绝不在新状态下继续结算）；
    /// * 租约序号保证表被回调重排后旧帧不碰新租记录。
    /// </summary>
    internal static void Tick(float deltaSeconds, bool playing)
    {
        if (_ticking) return;   // 嵌套 Tick（伤害回调内）：本帧推进已在进行，绝不二次推进任何弹
        _ticking = true;
        try
        {
            // 世界作用域守卫先跑：换岛/失活旧层时先丢掉自有池与在场子弹（同世界的暂停/开关切换不动它）。
            EnsureWorldScope();
            if (_count == 0) return;
            if (!playing) return;
            if (!(deltaSeconds > 0f) || !float.IsFinite(deltaSeconds)) return;

            // 本帧边界：world 身份 + 此刻已租出的记录（Lease ≤ frameLease）才属于本帧。
            IntPtr frameWorldPointer = _worldPointer;
            int frameWorldGoId = _worldGoId;
            long frameLease = _leaseCounter;
            for (int i = _count - 1; i >= 0; i--)
            {
                if (i >= _count) continue;                              // 回调重入收缩了表：该槽已不存在
                MusketeerBullet bullet = Bullets[i];
                if (bullet == null || !bullet.Alive)
                {
                    RemoveAt(i);
                    continue;
                }
                if (bullet.Lease > frameLease) continue;                // 新租记录：不属于本帧

                float remaining = bullet.MaxDistance - bullet.Travelled;
                float lifeLeft = bullet.Lifetime - bullet.Age;
                float step = bullet.Speed * deltaSeconds;
                float allowed = Mathf.Min(step, Mathf.Min(remaining, bullet.Speed * lifeLeft));
                if (!(allowed > 0f) || !float.IsFinite(allowed))
                {
                    // 射程/寿命已到：绝不再向前施法。
                    bullet.Alive = false;
                    DespawnVisual(bullet);
                    RemoveAt(i);
                    continue;
                }

                Vector2 from = bullet.Position;
                Vector2 to = new Vector2(from.x + bullet.Direction.x * allowed, from.y + bullet.Direction.y * allowed);

                // 斜向弹先裁到**实际地面交点**，只扫地上段（水平弹/敌方弹完全不受影响）：
                // 绝不先把整段扫完再"落到地下才消失"——那会穿过地面命中地下单位。
                float groundY = GroundSurfaceY();
                bool reachedGround = false;
                if (bullet.Direction.y < 0f && to.y < groundY)
                {
                    if (from.y <= groundY)
                    {
                        // 起点已在地面之下（异常几何）：立即终止，绝不下扫。
                        bullet.Alive = false;
                        DespawnVisual(bullet);
                        RemoveAt(i);
                        continue;
                    }
                    float t = (groundY - from.y) / (to.y - from.y);
                    if (!(t > 0f)) t = 0f;
                    if (t > 1f) t = 1f;
                    to = new Vector2(from.x + (to.x - from.x) * t, groundY);
                    reachedGround = true;
                }

                if (TryResolveSegment(from, to, bullet.ShooterRoot, bullet.ShooterLease, bullet.FoeTagState,
                        out Damageable hitTarget, out bool immunityConsume))
                {
                    // 消费闩先于伤害：先把子弹从表里摘掉并归还可视化/记录，再调用共同 Submit。
                    // 归还记录会清空 source 引用 —— 先快照提交参数（评审门：清字段后绝不再读）。
                    GameObject source = bullet.ShooterRoot;
                    bool deerHit = hitTarget != null && MusketeerFoeFilter.GetDeerRoot(hitTarget.gameObject) != null;
                    bullet.Alive = false;
                    DespawnVisual(bullet);
                    RemoveAt(i);
                    if (!immunityConsume && hitTarget != null)
                    {
                        SubmitBulletDamage(hitTarget, source);
                        if (deerHit) LogDeerSubmit(hitTarget);
                        // 伤害回调同步返回边界：状态可能已变（换世界/关闭/失权/暂停）→
                        // 旧 world 的本轮剩余弹绝不再继续推进（world 变化/失活时顺手完成收尾）。
                        if (!FrameStillAdvancing(frameWorldPointer, frameWorldGoId))
                        {
                            EnsureWorldScope();
                            return;
                        }
                    }
                    continue;
                }

                if (reachedGround)
                {
                    // 该步只扫到地面交点且没有命中有效目标：子弹落在真实地面上，终止。
                    bullet.Alive = false;
                    DespawnVisual(bullet);
                    RemoveAt(i);
                    continue;
                }

                bullet.Position = to;
                bullet.Travelled += allowed;
                bullet.Age += deltaSeconds;                              // 完整有效 dt（不再截断为 0.1s）

                if (bullet.Travelled >= bullet.MaxDistance || bullet.Age >= bullet.Lifetime
                    || bullet.Position.y <= GroundSurfaceY())
                {
                    bullet.Alive = false;
                    DespawnVisual(bullet);
                    RemoveAt(i);
                    continue;
                }

                try
                {
                    if (bullet.Visual != null) bullet.Visual.transform.position = bullet.Position;
                }
                catch (Exception) { }
            }
        }
        finally
        {
            _ticking = false;
        }
    }

    /// <summary>把在场子弹归还视觉池（功能关/换世界；池对象保留复用，世界销毁时由 ResetPool 清）。</summary>
    internal static void DespawnAll()
    {
        for (int i = _count - 1; i >= 0; i--)
        {
            MusketeerBullet bullet = Bullets[i];
            if (bullet != null)
            {
                bullet.Alive = false;
                DespawnVisual(bullet);
            }
            Bullets[i] = null;
            ReleaseRecord(bullet);
        }
        _count = 0;
    }

    /// <summary>
    /// 销毁全部池化视觉与在场子弹（换世界/卸载；正产路径由 <see cref="EnsureWorldScope"/> 自动触发）。
    /// Destroy 失败者转入退休回执重试；**贴图/材质与世界无关，这里绝不重置、绝不重载**（不重复解码、不漏纹理）。
    /// </summary>
    internal static void ResetPool()
    {
        DespawnAll();
        ResetPoolObjects();
        RetryRetired();
    }

    /// <summary>测试钩子：只清本地表/池/世界绑定与诊断计数。</summary>
    internal static void ResetForTests()
    {
        ResetPool();
        RetiredVisuals.Clear();
        RecordPool.Clear();
        _worldPointer = IntPtr.Zero;
        _worldGoId = 0;
        _nextRetiredRetryAt = 0f;
        _leaseCounter = 0;
        _ticking = false;
        _suppressedShots = 0;
        _skippedFullTableShots = 0;
        _loggedMissingVisual = false;
        _loggedPhysicsUnavailable = false;
        _loggedTableFull = false;
        _deerLogs = 0;
        _hitBuffer = null;
        _hitList = null;
    }

    /// <summary>测试钩子：注入弹丸精灵（测试程序集没有嵌入资源；生产走 EnsureBulletSprite）。</summary>
    internal static void SetBulletSpriteForTests(Sprite sprite)
    {
        _bulletSprite = sprite;
        _bulletSpriteState = sprite != null ? BulletSpriteState.Ready : BulletSpriteState.Unknown;
    }

    // ============================================================
    // 出膛与扫掠
    // ============================================================

    private static bool ShooterReady(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null || !archer.gameObject.activeInHierarchy) return false;
            if (!archer.enabled || archer.harmless) return false;
            Character character = archer._character;
            if (character == null || character.inert || character.grabbed) return false;
            Damageable damageable = archer._damageable;
            return damageable != null && !damageable.isDead;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 出膛原点/朝向：已举枪 Aim 枪口锚点 [48,14] → 角色本地 → 世界（TransformPoint 自带朝向符号）；
    /// 锚点本地偏移乘共用外观缩放 <see cref="MusketeerAtlas.AppearanceScale"/>（与自有 sprite 的
    /// localScale 同源：出膛点与所见的人物+手持枪严格一致）；翻转沿用原生 renderer.flipX 的符号修正，
    /// 绝不使用原生 2.5 前移或别的硬编码偏移。
    /// </summary>
    internal static bool TryComputeMuzzle(Archer archer, out Vector2 origin, out float direction)
    {
        origin = default;
        direction = 1f;
        try
        {
            if (archer == null || archer.gameObject == null) return false;
            Transform root = archer.transform;
            if (root == null) return false;

            MusketeerAtlas.PreparedMuzzleLocal(out float localX, out float localY);
            SpriteRenderer native = archer._spriteRenderer;
            bool flip = native != null && native.flipX;
            float artSign = flip ? -1f : 1f;
            // 与自有 sprite 的 localScale 同一常量：枪口随 0.9 外观缩放一起收，方向规则不变。
            Vector3 local = new Vector3(localX * artSign * MusketeerAtlas.AppearanceScale,
                localY * MusketeerAtlas.AppearanceScale, 0f);
            Vector3 world = root.TransformPoint(local);
            if (!float.IsFinite(world.x) || !float.IsFinite(world.y)) return false;

            float scaleX = root.lossyScale.x;
            if (!float.IsFinite(scaleX) || Mathf.Abs(scaleX) < 1e-6f) return false;
            direction = (scaleX < 0f ? -1f : 1f) * artSign;
            origin = new Vector2(world.x, world.y);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>本段是否消费子弹（true = 消费：命中有效目标，或证据不完整时保守终止；false = 继续飞）。</summary>
    private static bool TryResolveSegment(Vector2 from, Vector2 to, GameObject shooterRoot, long shooterLease,
        GroundFoeTagState tagState, out Damageable hitTarget, out bool immunityConsume)
    {
        hitTarget = null;
        immunityConsume = false;
        int mask = MusketeerFoeFilter.CombatLayerMask();
        if (mask == 0) return false;   // 敌方层解析失败：本段无命中证据（fail-closed：继续飞，与旧行为一致）
        if (!TryQuerySegment(from, to, mask, shooterRoot, shooterLease, tagState, out hitTarget, out immunityConsume))
            return true;               // 证据不完整（已限频记录）：保守消费，绝不伪造命中
        return hitTarget != null;
    }

    /// <summary>
    /// 查询本段并流式选最近有效命中：true = 证据完整（target 可为 null = 本段无有效目标）；
    /// false = 物理证据不完整（真实 API 故障）——调用方保守消费、不伤害。
    /// 查询层 = Enemies | Wildlife（鹿所在层；解析不到 Wildlife 时退回纯敌方层）；
    /// 常态走可复用数组（低密度场景零 List 索引开销）；数组到软上限仍饱和 → 官方 List overload
    /// 拿全量结果（Unity 自动扩容并复用；不设第二个吞伤上限，也绝不无限循环）。
    /// 选择策略：流式遍历全部结果取严格更近者（与返回顺序无关；友军/飞行/小动物/未知/死亡/自己只跳过）；
    /// 鹿候选另需发射时快照的绑定 lease 仍是同一 life。
    /// </summary>
    private static bool TryQuerySegment(Vector2 from, Vector2 to, int layerMask, GameObject shooterRoot,
        long shooterLease, GroundFoeTagState tagState, out Damageable target, out bool immunityConsume)
    {
        target = null;
        immunityConsume = false;
        float segmentX = to.x - from.x;
        float segmentY = to.y - from.y;
        // 段长必须按整段算（鹿弹可能微倾；水平时等价于旧的 |Δx|）。
        float segmentLength = Mathf.Sqrt(segmentX * segmentX + segmentY * segmentY);
        int capacity = _hitBuffer != null ? _hitBuffer.Length : InitialHitCapacity;
        while (true)
        {
            EnsureHitArrayCapacity(capacity);
            int hits;
            try
            {
                hits = Physics2D.LinecastNonAlloc(from, to, _hitBuffer, layerMask);
            }
            catch (Exception)
            {
                LogPhysicsUnavailableOnce();
                return false;
            }
            if (hits < 0 || hits > _hitBuffer.Length)
            {
                LogPhysicsUnavailableOnce();   // 越界返回值：证据不可信
                return false;
            }
            if (hits < capacity)
            {
                SelectNearestFromArray(_hitBuffer, hits, segmentLength, shooterRoot, shooterLease, tagState,
                    out target, out immunityConsume);
                return true;
            }
            if (capacity >= MaxArrayCapacity) break;
            capacity = Mathf.Min(MaxArrayCapacity, capacity * 2);
        }

        // 数组在软上限仍饱和：换官方完整 List overload（结果完整、可复用；只信返回的 count）。
        try
        {
            EnsureHitList();
            ContactFilter2D filter = BuildLegacyContactFilter(layerMask);
            int hits = Physics2D.Linecast(from, to, filter, _hitList);
            if (hits < 0 || hits > _hitList.Count)
            {
                LogPhysicsUnavailableOnce();   // 返回值超出列表内容：证据不可信
                return false;
            }
            SelectNearestFromList(_hitList, hits, segmentLength, shooterRoot, shooterLease, tagState,
                out target, out immunityConsume);
            return true;
        }
        catch (Exception)
        {
            LogPhysicsUnavailableOnce();
            return false;
        }
    }

    /// <summary>
    /// 与旧 `LinecastNonAlloc(..., layerMask)` 内部 `ContactFilter2D.CreateLegacyFilter` 同语义：
    /// triggers 由全局 `Physics2D.queriesHitTriggers` 决定、层掩码与数组路径完全一致
    /// （Enemies | Wildlife）、不做深度/法线过滤。
    /// </summary>
    private static ContactFilter2D BuildLegacyContactFilter(int layerMask)
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = Physics2D.queriesHitTriggers;
        filter.useLayerMask = true;
        filter.layerMask = layerMask;
        filter.useDepth = false;
        filter.useNormalAngle = false;
        return filter;
    }

    private static void SelectNearestFromArray(Il2CppStructArray<RaycastHit2D> hits, int count,
        float segmentLength, GameObject shooterRoot, long shooterLease, GroundFoeTagState tagState,
        out Damageable target, out bool immunityConsume)
    {
        float bestDistance = float.MaxValue;
        Damageable bestTarget = null;
        bool bestImmunity = false;
        for (int i = 0; i < count && i < hits.Length; i++)
            ConsiderHit(hits[i], segmentLength, shooterRoot, shooterLease, tagState,
                ref bestDistance, ref bestTarget, ref bestImmunity);
        target = bestTarget;
        immunityConsume = bestImmunity;
    }

    private static void SelectNearestFromList(Il2CppSystem.Collections.Generic.List<RaycastHit2D> hits, int count,
        float segmentLength, GameObject shooterRoot, long shooterLease, GroundFoeTagState tagState,
        out Damageable target, out bool immunityConsume)
    {
        float bestDistance = float.MaxValue;
        Damageable bestTarget = null;
        bool bestImmunity = false;
        for (int i = 0; i < count; i++)
            ConsiderHit(hits[i], segmentLength, shooterRoot, shooterLease, tagState,
                ref bestDistance, ref bestTarget, ref bestImmunity);
        target = bestTarget;
        immunityConsume = bestImmunity;
    }

    /// <summary>
    /// 流式最近命中（绝不重建候选对象）：段外/透明候选只跳过，不阻挡也不伤害。
    /// 透明 = 友军/飞行/死亡/自己 + **兔子等小动物**（有 Damageable 但既不是敌人也不是白天可猎鹿）；
    /// 鹿命中必须通过鹿根组件 + 鹿根自己的 Damageable 校验（被其他 mod 改层/加子 collider 也照此），
    /// 且发射时快照的绑定 lease 仍是同一 life（池复用旧弹绝不伤鹿）；
    /// 敌人 tag 门用发射时快照 <paramref name="tagState"/>（不在飞行中重读射手状态）；
    /// 严格更近才替换（同距保持先遇到的，结果与回调顺序无关）。
    /// </summary>
    private static void ConsiderHit(RaycastHit2D hit, float segmentLength, GameObject shooterRoot, long shooterLease,
        GroundFoeTagState tagState, ref float bestDistance, ref Damageable bestTarget, ref bool bestImmunity)
    {
        float distance = hit.distance;
        if (!float.IsFinite(distance) || distance < 0f || distance > segmentLength + 1e-4f) return;
        if (!(distance < bestDistance)) return;

        Collider2D collider = hit.collider;
        if (collider == null) return;
        GameObject candidate = collider.gameObject;
        if (candidate == null) return;
        if (!MusketeerFoeFilter.TryResolveShotTarget(candidate, shooterRoot, shooterLease, tagState,
                out Damageable target, out bool immunityConsume))
            return;

        bestDistance = distance;
        bestTarget = target;
        bestImmunity = immunityConsume;
    }

    private static void EnsureHitArrayCapacity(int capacity)
    {
        if (_hitBuffer == null || _hitBuffer.Length != capacity)
            _hitBuffer = new Il2CppStructArray<RaycastHit2D>(capacity);
    }

    private static void EnsureHitList()
    {
        if (_hitList == null) _hitList = new Il2CppSystem.Collections.Generic.List<RaycastHit2D>();
    }

    private static void LogPhysicsUnavailableOnce()
    {
        if (_loggedPhysicsUnavailable) return;
        _loggedPhysicsUnavailable = true;
        Log("physics sweep unavailable; musketeer bullets deal no damage until fixed");
    }

    /// <summary>
    /// 共同伤害入口（唯一伤害调用点）：同步 Submit 到原生 ReceiveDamage 路径；异常绝不外抛、
    /// 绝不重试（已部分生效的伤害不补打），同帧其它子弹继续；返回值语义（Skipped/Submitted/Faulted）
    /// 由共享 helper 负责，调用方不把它当 HP 必减。
    /// </summary>
    private static void SubmitBulletDamage(Damageable target, GameObject source)
    {
        try
        {
            CombatDamage.Submit(target, BulletDamage, source, DamageSource.Arrow);
        }
        catch (Exception)
        {
            // 共享 helper 契约内已捕获单目标回调异常；这里只做最后一道隔离（契约被破坏也不阻塞同帧其它弹）。
        }
    }

    /// <summary>
    /// 伤害回调返回边界的最小复核（评审门，不做逐单位扫描）：功能开关、暂停/失权门、本轮 world
    /// 身份与可用性。任一已变（含状态读取异常 = 无法证明仍有效）→ false，调用方中止本帧剩余推进，
    /// 旧 world 的其它弹绝不在新状态下继续结算。
    /// </summary>
    private static bool FrameStillAdvancing(IntPtr frameWorldPointer, int frameWorldGoId)
    {
        try
        {
            if (!MusketeerAccess.Enabled) return false;
            if (!MusketeerAccess.Playing) return false;      // 暂停/失权：保持原语义（不推进、不伤害）
            if (!TryGetUsableWorld(out _, out IntPtr pointer, out int goId)) return false;
            return pointer == frameWorldPointer && goId == frameWorldGoId;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryCreateBullet(Archer archer, ArrowAttack attack, Vector2 origin, Vector2 direction,
        float range, Transform world, long shooterLease, GroundFoeTagState foeTagState)
    {
        if (world == null) return false;

        MusketeerBullet bullet = AcquireRecord();
        if (!TryAcquireVisual(direction.x < 0f ? -1f : 1f, attack, world, out GameObject visual, out SpriteRenderer renderer))
        {
            ReleaseRecord(bullet);   // 租到但没用上：立刻归还，绝不把记录留在池外
            return false;
        }

        try
        {
            if (visual != null)
            {
                visual.transform.position = origin;
                visual.SetActive(true);
            }
        }
        catch (Exception)
        {
            ReleaseVisual(visual);
            ReleaseRecord(bullet);
            return false;
        }

        bullet.Visual = visual;
        bullet.Renderer = renderer;
        bullet.Position = origin;
        bullet.Direction = direction;
        bullet.Speed = BulletSpeed;
        bullet.MaxDistance = range;
        bullet.Travelled = 0f;
        bullet.Age = 0f;
        bullet.Lifetime = Mathf.Min(MaxLifetimeSeconds, Mathf.Max(0.05f, range / BulletSpeed));
        bullet.Alive = true;
        bullet.ShooterRoot = archer.gameObject;
        bullet.ShooterLease = shooterLease;
        bullet.FoeTagState = foeTagState;   // 发射时快照：命中解析的唯一 tag 依据
        bullet.Lease = ++_leaseCounter;   // 本帧边界之后的租约：重入回调里的旧帧不会推进它

        Bullets[_count] = bullet;
        _count++;
        return true;
    }

    /// <summary>租一条弹丸记录（优先复用池内记录，稳态零逐发分配；池空才新建，总量 ≤ 4096）。</summary>
    private static MusketeerBullet AcquireRecord()
    {
        int last = RecordPool.Count - 1;
        if (last < 0) return new MusketeerBullet();
        MusketeerBullet pooled = RecordPool[last];
        RecordPool.RemoveAt(last);
        return pooled;
    }

    /// <summary>
    /// 归还记录前清空全部引用/状态：旧视觉、旧 renderer、旧射击者（=旧世界来源）、位置与进度，
    /// 绝不让池内记录被二次消费或把旧发射者带进下一发。
    /// </summary>
    private static void ReleaseRecord(MusketeerBullet bullet)
    {
        if (bullet == null) return;
        bullet.Alive = false;
        bullet.Visual = null;
        bullet.Renderer = null;
        bullet.ShooterRoot = null;
        bullet.ShooterLease = 0L;
        bullet.FoeTagState = GroundFoeTagState.FreeStanding;   // 池复用绝不残留上一发的绕过位（fail-open 防护）
        bullet.Position = default;
        bullet.Direction = default;
        bullet.Speed = 0f;
        bullet.MaxDistance = 0f;
        bullet.Travelled = 0f;
        bullet.Age = 0f;
        bullet.Lifetime = 0f;
        bullet.Lease = 0;
        RecordPool.Add(bullet);
    }

    /// <summary>
    /// 取一个池化子弹视觉（复用同一个 GameObject/SpriteRenderer，无逐发分配）。
    /// 贴图资产缺失 → **不构造可见弹丸**（fail-closed：绝不打隐形伤害），并限频记录一次。
    /// 材质保持 SpriteRenderer 默认（不继承原生箭/火焰材质与染色），只复制排序层/序。
    /// </summary>
    private static bool TryAcquireVisual(float facing, ArrowAttack attack, Transform world,
        out GameObject visual, out SpriteRenderer renderer)
    {
        visual = null;
        renderer = null;
        if (!EnsureBulletSprite())
        {
            if (!_loggedMissingVisual)
            {
                _loggedMissingVisual = true;
                Log("bullet sprite unavailable; musketeer shots stay suppressed without dealing damage");
            }
            return false;
        }
        if (world == null) return false;

        try
        {
            // 从池里取：跳过并销毁失效条目（已销毁/父层不在当前世界），同一次调用继续租下一个——
            // 绝不因为一条旧世界的坏记录丢掉这一发付费射击。
            while (visual == null && VisualPool.Count > 0)
            {
                int last = VisualPool.Count - 1;
                GameObject candidate = VisualPool[last];
                VisualPool.RemoveAt(last);
                visual = ValidatePooled(candidate, world);
            }

            if (visual == null)
            {
                // Keep failed-destroy receipts; apply backpressure to creation instead of
                // discarding ownership when a repeated cleanup failure fills the backlog.
                if (RetiredVisuals.Count >= MaxRetiredVisuals) return false;
                visual = new GameObject("KEM_MusketeerBullet");
                visual.transform.SetParent(world, false);
            }

            renderer = visual.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = visual.AddComponent<SpriteRenderer>();
            if (renderer == null)
            {
                ReleaseVisual(visual);
                visual = null;
                return false;
            }
            renderer.sprite = _bulletSprite;
            renderer.color = Color.white;
            renderer.flipX = facing < 0f;
            ApplyBulletSorting(renderer, attack);
            return true;
        }
        catch (Exception)
        {
            ReleaseVisual(visual);
            visual = null;
            renderer = null;
            return false;
        }
    }

    /// <summary>
    /// 复用一条池化视觉前的校验：已销毁 → 丢弃（无需回执）；父层不是当前世界 → **重挂到当前层**
    /// （旧世界销毁/失活留下的父层绝不再被激活），失败 → 退休（保留回执）。
    /// </summary>
    private static GameObject ValidatePooled(GameObject candidate, Transform world)
    {
        if (candidate == null) return null;   // 已销毁：直接丢弃
        try
        {
            Transform parent = candidate.transform != null ? candidate.transform.parent : null;
            if (parent != world) candidate.transform.SetParent(world, false);
            return candidate;
        }
        catch (Exception)
        {
            Retire(candidate);
            return null;
        }
    }

    /// <summary>只复制原生箭 renderer 的排序层/序（材质与染色一律不继承）。</summary>
    private static void ApplyBulletSorting(SpriteRenderer renderer, ArrowAttack attack)
    {
        try
        {
            Arrow prefab = attack != null ? attack._arrowPrefab : null;
            if (prefab != null && prefab.gameObject != null)
            {
                SpriteRenderer source = prefab.GetComponent<SpriteRenderer>();
                if (source != null)
                {
                    renderer.sortingLayerID = source.sortingLayerID;
                    renderer.sortingOrder = source.sortingOrder;
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private static void DespawnVisual(MusketeerBullet bullet)
    {
        if (bullet == null) return;
        GameObject visual = bullet.Visual;
        bullet.Visual = null;
        bullet.Renderer = null;
        ReleaseVisual(visual);
    }

    private static void ReleaseVisual(GameObject visual)
    {
        if (visual == null) return;
        try
        {
            visual.SetActive(false);
            if (VisualPool.Count < MaxLiveBullets && !VisualPool.Contains(visual))
            {
                VisualPool.Add(visual);
                return;
            }
        }
        catch (Exception)
        {
        }
        // 回不了池（已销毁/异常/池满）：直接销毁；Destroy 失败则由退休回执保留引用，绝不静默丢掉。
        Retire(visual);
    }

    private static void RemoveAt(int index)
    {
        if (index < 0 || index >= _count) return;
        MusketeerBullet removed = Bullets[index];
        Bullets[index] = Bullets[_count - 1];
        Bullets[_count - 1] = null;
        _count--;
        ReleaseRecord(removed);   // 记录回池并清字段（旧 source/visual 引用绝不残留）
    }

    /// <summary>
    /// 弹丸贴图（Operator 嵌入的 5x3、Point、PPU32、pivot 居中）：惰性解码一次；
    /// 尺寸/内容不符或读取失败 → 整块不可用（fail-closed，调用方不构造弹丸）。
    /// </summary>
    private static bool EnsureBulletSprite()
    {
        if (_bulletSpriteState == BulletSpriteState.Ready) return true;
        if (_bulletSpriteState == BulletSpriteState.Unavailable) return false;
        _bulletSpriteState = BulletSpriteState.Unavailable;
        Texture2D texture = null;
        try
        {
            System.Reflection.Assembly assembly = typeof(MusketeerCombat).Assembly;
            using (System.IO.Stream stream = assembly.GetManifestResourceStream(BulletResourceName))
            {
                if (stream == null) return false;
                long length = stream.Length;
                if (length <= 0 || length > 256L * 1024L) return false;
                byte[] bytes = new byte[(int)length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int step = stream.Read(bytes, read, bytes.Length - read);
                    if (step <= 0) break;
                    read += step;
                }
                if (read != bytes.Length) return false;

                texture = new Texture2D(BulletSpriteWidth, BulletSpriteHeight, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes, false)) { DestroyQuietly(texture); return false; }
                if (texture.width != BulletSpriteWidth || texture.height != BulletSpriteHeight)
                {
                    DestroyQuietly(texture);
                    return false;
                }
                Color32[] pixels = texture.GetPixels32();
                if (pixels == null || pixels.Length != BulletSpriteWidth * BulletSpriteHeight)
                {
                    DestroyQuietly(texture);
                    return false;
                }
                bool anyVisible = false;
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (pixels[i].a != 0) { anyVisible = true; break; }
                }
                if (!anyVisible)
                {
                    DestroyQuietly(texture);
                    return false;
                }

                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.anisoLevel = 0;
                _bulletSprite = Sprite.Create(texture, new Rect(0f, 0f, BulletSpriteWidth, BulletSpriteHeight),
                    new Vector2(0.5f, 0.5f), BulletSpritePixelsPerUnit, 0u, SpriteMeshType.FullRect);
                if (_bulletSprite == null)
                {
                    DestroyQuietly(texture);
                    return false;
                }
                _bulletSpriteState = BulletSpriteState.Ready;
                return true;
            }
        }
        catch (Exception e)
        {
            Log("bullet sprite decode failed: " + e.GetType().Name);
            return false;
        }
    }

    private static void DestroyQuietly(UnityEngine.Object target)
    {
        if (target == null) return;
        try { UnityEngine.Object.Destroy(target); } catch (Exception) { }
    }

    /// <summary>
    /// 地面表面高度：优先取世界 GroundCollider 顶面；取不到时退回原生 Arrow 的"地下"阈值 0.875。
    /// 水平弹在枪口高度平飞时通常不会触发；这是"世界地面终止"的确定性兜底（斜向鹿弹在
    /// <see cref="Tick"/> 里先按它裁剪步段，绝不穿地）。
    /// </summary>
    private static float GroundSurfaceY()
    {
        try
        {
            BoxCollider2D ground = World.GroundCollider;
            if (ground != null) return ground.bounds.max.y;
        }
        catch (Exception)
        {
        }
        return 0.875f;
    }

    /// <summary>
    /// 鹿**发射**被动日志（非鹿直接 no-op）：枪口 Y、鹿根自身 body bounds、快照瞄准方向、射程。
    /// 供用户实机核对几何（例如 Greek 鹿低于枪口线时的实际倾角）。
    /// </summary>
    private static void LogDeerShot(GameObject target, Vector2 origin, Vector2 direction, float range)
    {
        if (_deerLogs >= MaxDeerLogsPerWorld) return;
        try
        {
            Deer deer = MusketeerFoeFilter.GetDeerRoot(target);
            if (deer == null) return;
            string band = "bodyY=n/a";
            Collider2D body = deer.GetComponent<Collider2D>();
            if (body != null)
            {
                Bounds bounds = body.bounds;
                band = "bodyY=[" + F(bounds.min.y) + ".." + F(bounds.max.y) + "]";
            }
            LogDeerOnce("deer shot: muzzleY=" + F(origin.y) + " " + band
                + " aim=(" + F(direction.x) + "," + F(direction.y) + ") range=" + F(range));
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// 鹿**命中提交**被动日志：只报告尝试提交伤害，不断言原生 ReceiveDamage 已执行或 HP 实际扣减
    /// （invulnerable/护盾/已结算等原生分支可能早退）；掉钱/死亡仍归原生。
    /// </summary>
    private static void LogDeerSubmit(Damageable damageable)
    {
        if (_deerLogs >= MaxDeerLogsPerWorld) return;
        try
        {
            string state = "n/a";
            if (damageable != null)
            {
                state = damageable.isDead ? "dead" : "alive";
                if (!damageable.enabled) state += "/disabled";
            }
            LogDeerOnce("deer hit: attempted damage submission of 2 (DamageSource.Arrow);"
                + " damageable=" + state + " (HP/death/coins are native-owned, submit is not a HP promise)");
        }
        catch (Exception)
        {
        }
    }

    /// <summary>鹿事件日志（有界：每世界前 <see cref="MaxDeerLogsPerWorld"/> 条；被动触发，绝无逐帧扫描）。</summary>
    private static void LogDeerOnce(string message)
    {
        try
        {
            if (_deerLogs >= MaxDeerLogsPerWorld) return;
            _deerLogs++;
            KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[Musketeer] " + message);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>日志数值格式化（invariant：不随系统区域变小数点）。</summary>
    private static string F(float value) => value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

    private static void Log(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[Musketeer] " + message); }
        catch (Exception) { }
    }
}
