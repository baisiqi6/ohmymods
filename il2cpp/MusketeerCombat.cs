// 火铳手·射击与弹道（runtime slice，评审修订版）。
//
// 职责边界（本文件只做这些）：
// 1. 原生箭的**无条件压制**入口：Operator 的 ArrowAttack.FireArrowInternal prefix 调
//    <see cref="MusketeerCombat.TryHandleShot"/>。活动火铳手（身份 + 战斗包已装）**一律压制**
//    原生箭（即使目标非法/还在冷却/未装填），绝不出现"火铳手射出弓矢"。
// 2. 自有直线子弹：显式时间闸 + 合法地面目标 → 从「已举枪」Aim 枪口锚点 [48,14] 出膛；
//    平飞、有限射程/寿命、命中首个有效地面敌人即停。不生成原生 Arrow 组件（因此不继承原生
//    Crusher 反弹/canBounce/随机火矢，也不继承原生箭/火焰材质与染色），不调用任何 SO 发声回调。
// 3. 地面判定与原生等价门：目标校验与弹道碰撞过滤**同一套判据**。
//
// 明确的"不做"：无穿透、无 AOE、无灼烧、无 perfect 倍率、无随机散射/误差、无落水概率、
// 不写任何原生 HP 字段（伤害只经 Damageable.ReceiveDamage(2, source, DamageSource.Arrow)，
// 原生 shield/preDamage 处理原样保留）、不新增 RPC、不碰池。
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
/// 地面敌人判据（目标选择与弹道碰撞**共用**，保证两处一致）。
///
/// 判据来源与边界：
/// * 飞行/未验证一律拒绝：目标(或父级)带 <c>Squid</c> 组件 → false（Call of Olympus 的空中抓人单位，
///   仓库已有组件级排除先例 PatchDivine_FriendlyTroll）；带 <c>Enemy</c> 组件时 <c>Enemy.Type</c>
///   必须在**地面白名单**（TrollWeak/TrollMedium/ToughTroll/Ogre/Stealer/Crusher/Knight/Archer，
///   与 2.1 EnemyType 枚举一致）内，Squid 与**未验证的 Boss 系**（Boss/KillerBoss/GauntletBoss/
///   BossWithStealer）一律拒绝——这是保守选择，**不声称已覆盖全部地面 Boss**，待实测证据再放行。
/// * 不带 Enemy 组件的敌方结构（静态地面目标）允许：它们没有飞行能力；最终是否成敌仍由原生门决定。
/// * 其余门与原生普通地面弓手 ShouldShootEnemy 等价：Enemies 层、非 EnemySpawn/Unspittable/
///   QuestStructure、Damageable 存活/启用/IsDamagedBy(Arrow)、invulnerable 且 ignoredWhenInvulnerable
///   时排除；另排除友军巨魔。
/// </summary>
internal static class MusketeerFoeFilter
{
    private const string EnemiesLayerName = "Enemies";
    private const string EnemySpawnTag = "EnemySpawn";
    private const string UnspittableTag = "Unspittable";
    private const string QuestStructureTag = "QuestStructure";

    private static int _enemiesLayer = -1;

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

    /// <summary>原生等价的弹道目标校验：首个满足条件者才允许吃弹。</summary>
    internal static bool IsValidGroundFoe(GameObject candidate, GameObject shooterRoot)
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

            if (candidate.CompareTag(EnemySpawnTag) || candidate.CompareTag(UnspittableTag)
                || candidate.CompareTag(QuestStructureTag))
                return false;   // 原生普通地面弓手（无编队/无塔位/未上船）同样排除

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

/// <summary>弹道候选（Unity 侧填充，纯选择器消费）：距离 + 是否可命中 + 是否免疫消费。</summary>
internal struct MusketeerHitCandidate
{
    internal float Distance;
    internal bool Valid;
    internal bool ImmunityConsume;
    internal Damageable Target;
}

/// <summary>
/// 最近有效命中的纯选择器：与回调顺序无关（遍历取严格更近者）。
/// 非法候选（友军/飞行/死亡/自己）不阻挡、不伤害；返回 -1 = 本段无有效命中。
/// </summary>
internal static class MusketeerHitSelection
{
    internal static int SelectNearest(MusketeerHitCandidate[] candidates, int count, out bool immunityConsume)
    {
        immunityConsume = false;
        if (candidates == null) return -1;
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < count && i < candidates.Length; i++)
        {
            if (!candidates[i].Valid) continue;
            float distance = candidates[i].Distance;
            if (!float.IsFinite(distance)) continue;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        if (best >= 0) immunityConsume = candidates[best].ImmunityConsume;
        return best;
    }
}

/// <summary>一条自有子弹（纯托管对象 + 池化可视化；不入原生池、不挂原生组件）。</summary>
internal sealed class MusketeerBullet
{
    internal GameObject Visual;
    internal SpriteRenderer Renderer;
    internal Vector2 Position;
    internal float Direction;
    internal float Speed;
    internal float MaxDistance;
    internal float Travelled;
    internal float Age;
    internal float Lifetime;
    internal bool Alive;
    internal GameObject ShooterRoot;
}

/// <summary>
/// 火铳手射击实现（Unity 侧）：一次射击的完整事务 + 有界可增长子弹表。
/// 所有入口异常隔离（绝不外抛进原生调用链）；物理缓冲按需增长且可复用，稳态零逐帧分配。
/// </summary>
internal static class MusketeerCombat
{
    /// <summary>
    /// 同时存活子弹上限 = 支持的职业总数上限（与 identity sidecar / <see cref="MusketeerRuntime.MaxUnits"/>
    /// 的 4096 对齐）：一次全职业齐射也不会丢弹，绝不拿"平均占用"当容量论证；只有真到 4096 发同时在空
    /// 才限频记录并跳过（诊断计数 `SkippedFullTableShots`）。
    /// </summary>
    internal const int MaxLiveBullets = MusketeerRuntime.MaxUnits;

    /// <summary>单帧最大推进步长：长卡帧也把扫掠段限制在 Speed×MaxStep 之内。</summary>
    internal const float MaxStepSeconds = 0.1f;
    /// <summary>子弹直线速度（units/s；本地显示常量，非随机、非原生 SO 参数）。</summary>
    internal const float BulletSpeed = 30f;
    /// <summary>寿命安全上限（秒）：射程/速度之外的第二道界。</summary>
    internal const float MaxLifetimeSeconds = 1.2f;
    /// <summary>子弹伤害（用户拍板基础伤害 2；无 perfect 倍率、无 AOE、无灼烧）。</summary>
    internal const int BulletDamage = 2;

    /// <summary>物理命中缓冲/候选的初始容量与硬上限（饱和即保守终止，绝不在证据不全时打远处敌人）。</summary>
    private const int InitialHitCapacity = 32;
    private const int MaxHitCapacity = 256;

    /// <summary>退休回执上限与重试间隔：Destroy 抛异常时保留自有引用，绝不因为一次异常丢掉所有权。</summary>
    private const int MaxRetiredVisuals = 64;
    private const float RetiredRetrySeconds = 1f;

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
    private static MusketeerHitCandidate[] _candidates = new MusketeerHitCandidate[InitialHitCapacity];
    private static Il2CppStructArray<RaycastHit2D> _hitBuffer;
    private static readonly List<GameObject> VisualPool = new List<GameObject>(16);
    /// <summary>Destroy 抛异常时保留的自有弹丸引用（退休回执）：下次机会继续销毁，绝不静默丢引用。</summary>
    private static readonly List<GameObject> RetiredVisuals = new List<GameObject>(4);

    private static IntPtr _worldPointer;
    private static int _worldGoId;
    private static float _nextRetiredRetryAt;

    private static int _count;
    private static int _suppressedShots;
    private static int _skippedFullTableShots;
    private static bool _loggedMissingVisual;
    private static bool _loggedPhysicsUnavailable;
    private static bool _loggedSaturatedCasts;
    private static bool _loggedTableFull;
    private static BulletSpriteState _bulletSpriteState;
    private static Sprite _bulletSprite;

    /// <summary>当前存活子弹数（只读诊断）。</summary>
    internal static int LiveCount => _count;

    /// <summary>被压制的原生箭次数（只读诊断）。</summary>
    internal static int SuppressedShotCount => _suppressedShots;

    /// <summary>因子弹表满而跳过的射击次数（诊断；绝不静默）。</summary>
    internal static int SkippedFullTableShots => _skippedFullTableShots;

    /// <summary>物理缓冲容量（诊断/测试）。</summary>
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
    /// 一次自有射击：显式时间闸 + 合法地面目标 + 出膛。任一前置不满足都不消耗闸（下一发重试）。
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
        GameObject shooterRoot = archer.gameObject;
        if (!MusketeerFoeFilter.IsValidGroundFoe(target, shooterRoot)) return false;
        if (!ShooterReady(archer)) return false;

        if (!TryComputeMuzzle(archer, out Vector2 origin, out float direction)) return false;
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

        if (!TryCreateBullet(archer, archer.ActiveArrowAttack, origin, direction, range, world)) return false;

        float nextEligible = MusketeerRuntime.ConsumeShotGate(archer);
        MusketeerRuntime.ObserveOwnShot(archer, nextEligible);
        MusketeerVisuals.NotifyShot(archer, nextEligible);
        return true;
    }

    /// <summary>
    /// 每帧推进（runtime Tick 调用；暂停/关闭/离线不推进、不伤害）。
    /// 步长在**施法前**按剩余射程与剩余寿命钳制；扫掠段 [prev,next]、最近有效命中即消费
    /// （先丢子弹再做伤害，重入不可能二次命中）。
    /// </summary>
    internal static void Tick(float deltaSeconds, bool playing)
    {
        // 世界作用域守卫先跑：换岛/失活旧层时先丢掉自有池与在场子弹（同世界的暂停/开关切换不动它）。
        EnsureWorldScope();
        if (_count == 0) return;
        if (!playing) return;
        if (!(deltaSeconds > 0f) || !float.IsFinite(deltaSeconds)) return;
        if (deltaSeconds > MaxStepSeconds) deltaSeconds = MaxStepSeconds;

        for (int i = _count - 1; i >= 0; i--)
        {
            MusketeerBullet bullet = Bullets[i];
            if (bullet == null || !bullet.Alive)
            {
                RemoveAt(i);
                continue;
            }

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
            Vector2 to = new Vector2(from.x + bullet.Direction * allowed, from.y);

            if (TryResolveSegment(from, to, bullet.ShooterRoot, out Damageable hitTarget, out bool immunityConsume))
            {
                // 消费闩先于伤害：先把子弹从表里摘掉并归还可视化，再调用 ReceiveDamage。
                bullet.Alive = false;
                DespawnVisual(bullet);
                RemoveAt(i);
                if (!immunityConsume && hitTarget != null)
                {
                    // 原生 shield/preDamage/Invulnerable 处理全部保留；本模块不写任何 HP 字段。
                    hitTarget.ReceiveDamage(BulletDamage, bullet.ShooterRoot, DamageSource.Arrow);
                }
                continue;
            }

            bullet.Position = to;
            bullet.Travelled += allowed;
            bullet.Age += deltaSeconds;

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
        _worldPointer = IntPtr.Zero;
        _worldGoId = 0;
        _nextRetiredRetryAt = 0f;
        _suppressedShots = 0;
        _skippedFullTableShots = 0;
        _loggedMissingVisual = false;
        _loggedPhysicsUnavailable = false;
        _loggedSaturatedCasts = false;
        _loggedTableFull = false;
        _candidates = new MusketeerHitCandidate[InitialHitCapacity];
        _hitBuffer = null;
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
    /// 翻转沿用原生 renderer.flipX 的符号修正，绝不使用原生 2.5 前移或别的硬编码偏移。
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
            Vector3 local = new Vector3(localX * artSign, localY, 0f);
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

    /// <summary>
    /// 本段是否消费了子弹（命中有效地面敌人 → 输出目标与免疫位；否则 false = 继续飞）。
    /// 最近有效命中由纯选择器决定（与命中回调顺序无关）；友军/飞行/未知/死亡/自己为非法候选。
    /// 物理缓冲饱和（即使扩到硬上限）时**保守终止**：消费子弹但不造成任何伤害——
    /// 绝不把"证据不全"变成"打中更远的敌人"。
    /// </summary>
    private static bool TryResolveSegment(Vector2 from, Vector2 to, GameObject shooterRoot,
        out Damageable hitTarget, out bool immunityConsume)
    {
        hitTarget = null;
        immunityConsume = false;
        int count = Sweep(from, to, shooterRoot, out bool saturated);
        if (saturated)
        {
            if (!_loggedSaturatedCasts)
            {
                _loggedSaturatedCasts = true;
                Log("physics cast saturated at the hard limit; bullets terminate without damage instead of guessing");
            }
            return true;
        }
        int best = MusketeerHitSelection.SelectNearest(_candidates, count, out immunityConsume);
        if (best < 0)
        {
            ClearCandidates(count);
            return false;
        }
        hitTarget = _candidates[best].Target;
        ClearCandidates(count);
        return true;
    }

    private static void ClearCandidates(int count)
    {
        for (int i = 0; i < count && i < _candidates.Length; i++) _candidates[i] = default;
    }

    /// <summary>
    /// 扫掠一次（本段）并填充候选表：Enemies 层；最近有效命中由纯选择器决定（与命中回调顺序无关）。
    /// 缓冲按需倍增（32→256）并复用；到硬上限仍饱和 → saturated=true（调用方保守终止）。
    /// **唯一的物理调用点**：若 2.4 实际 interop 签名与此不同，只需改这一个方法。
    /// </summary>
    private static int Sweep(Vector2 from, Vector2 to, GameObject shooterRoot, out bool saturated)
    {
        saturated = false;
        int layer = MusketeerFoeFilter.EnemiesLayerIndex();
        if (layer < 0) return 0;

        // Once enlarged, keep the nonalloc buffer instead of allocating 32/64/... again
        // for every crowded segment. Only active bullets are iterated.
        int capacity = _hitBuffer != null ? _hitBuffer.Length : InitialHitCapacity;
        while (true)
        {
            EnsureHitCapacity(capacity);
            int hits;
            try
            {
                hits = Physics2D.LinecastNonAlloc(from, to, _hitBuffer, 1 << layer);
            }
            catch (Exception)
            {
                if (!_loggedPhysicsUnavailable)
                {
                    _loggedPhysicsUnavailable = true;
                    Log("physics sweep unavailable; musketeer bullets deal no damage until fixed");
                }
                saturated = true; // unknown segment: consume without damage, never skip a possible front enemy
                return 0;
            }
            if (hits < capacity) return Classify(hits, from, to, shooterRoot);
            if (capacity >= MaxHitCapacity)
            {
                saturated = true;
                return 0;
            }
            capacity = Mathf.Min(MaxHitCapacity, capacity * 2);
        }
    }

    private static void EnsureHitCapacity(int capacity)
    {
        if (_hitBuffer == null || _hitBuffer.Length != capacity)
            _hitBuffer = new Il2CppStructArray<RaycastHit2D>(capacity);
        if (_candidates.Length < capacity)
            _candidates = new MusketeerHitCandidate[capacity];
    }

    private static int Classify(int hits, Vector2 from, Vector2 to, GameObject shooterRoot)
    {
        if (hits <= 0) return 0;
        if (hits > _hitBuffer.Length) hits = _hitBuffer.Length;
        if (hits > _candidates.Length) hits = _candidates.Length;

        float segmentLength = Mathf.Abs(to.x - from.x);
        int used = 0;
        for (int i = 0; i < hits; i++)
        {
            RaycastHit2D hit = _hitBuffer[i];
            Collider2D collider = hit.collider;
            if (collider == null) continue;
            GameObject candidate = collider.gameObject;
            if (candidate == null) continue;

            float distance = hit.distance;
            // 段外/非法命中：本段证据不成立 → 直接跳过（绝不把"更远的敌人"当最近命中）。
            if (!float.IsFinite(distance) || distance < 0f || distance > segmentLength + 1e-4f) continue;

            bool valid = MusketeerFoeFilter.IsValidGroundFoe(candidate, shooterRoot);
            _candidates[used] = new MusketeerHitCandidate
            {
                Distance = distance,
                Valid = valid,
                ImmunityConsume = valid && MusketeerFoeFilter.IsImmunityConsume(candidate),
                Target = valid ? MusketeerFoeFilter.GetFoeDamageable(candidate) : null,
            };
            used++;
        }
        return used;
    }

    private static bool TryCreateBullet(Archer archer, ArrowAttack attack, Vector2 origin, float direction,
        float range, Transform world)
    {
        if (world == null) return false;
        if (!TryAcquireVisual(direction, attack, world, out GameObject visual, out SpriteRenderer renderer))
            return false;

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
            return false;
        }

        MusketeerBullet bullet = new MusketeerBullet
        {
            Visual = visual,
            Renderer = renderer,
            Position = origin,
            Direction = direction,
            Speed = BulletSpeed,
            MaxDistance = range,
            Travelled = 0f,
            Age = 0f,
            Lifetime = Mathf.Min(MaxLifetimeSeconds, Mathf.Max(0.05f, range / BulletSpeed)),
            Alive = true,
            ShooterRoot = archer.gameObject,
        };

        Bullets[_count] = bullet;
        _count++;
        return true;
    }

    /// <summary>
    /// 取一个池化子弹视觉（复用同一个 GameObject/SpriteRenderer，无逐发分配）。
    /// 贴图资产缺失 → **不构造可见弹丸**（fail-closed：绝不打隐形伤害），并限频记录一次。
    /// 材质保持 SpriteRenderer 默认（不继承原生箭/火焰材质与染色），只复制排序层/序。
    /// </summary>
    private static bool TryAcquireVisual(float direction, ArrowAttack attack, Transform world,
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
            renderer.flipX = direction < 0f;
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
        Bullets[index] = Bullets[_count - 1];
        Bullets[_count - 1] = null;
        _count--;
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
    /// 直线弹在枪口高度平飞时通常不会触发；这是"世界地面终止"的确定性兜底。
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

    private static void Log(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[Musketeer] " + message); }
        catch (Exception) { }
    }
}
