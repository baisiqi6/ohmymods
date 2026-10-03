using System;

namespace KingdomEnhancedMod;

/// <summary>
/// 金币哥布林运行时的纯规则层：阶段时长与"进入动作"、行为/招募闸门顺序、
/// 落点与接近判据、掉落/金币飞行的几何公式、场景清场与就地冻结策略。
///
/// 本文件没有 Unity/Il2Cpp/配置引用，也没有任何经济副作用：运行时
/// （<see cref="CoinCourierRuntime"/>）只把它当作"该做什么"的判据，真正的取币/配送
/// 仍然只有 <see cref="CoinCourierEconomy"/> 的两个窄入口。测试直接链接本文件。
/// </summary>
internal enum CoinCourierPhase
{
    Wait = 0,
    Collect = 1,
    JumpOut = 2,
    TeleportIn = 3,
    Fall = 4,
    Land = 5,
    Approach = 6,
    Deliver = 7,
    Frozen = 8,
}

/// <summary>阶段的"进入动作"：每个阶段实例最多执行一次，低帧率跨过整段也不漏不重。</summary>
internal enum CoinCourierAction
{
    None = 0,
    BagOne = 1,
    DeliverOne = 2,
}

/// <summary>JumpOut/TeleportIn 的终点语义。</summary>
internal enum CoinCourierTravel
{
    Bank = 0,
    Target = 1,
}

/// <summary>场景时长/距离/上限常量；姿态时长与 CoinCourierPoseTable 帧表同源。</summary>
internal static class CoinCourierTiming
{
    // 姿态秒/帧 × 帧数：Collect 4×0.18、Deliver 4×0.20、Jump 4×0.10、Fall/Land 2×0.12。
    internal const float CollectSeconds = 0.72f;
    internal const float DeliverSeconds = 0.80f;
    internal const float JumpSeconds = 0.40f;
    internal const float TeleportSeconds = 0.18f;   // 与共享传送短线同一生命期
    internal const float FallSeconds = 0.24f;
    internal const float LandSeconds = 0.24f;

    internal const float WaitCheckSeconds = 0.75f;      // 银行空闲重查节奏
    internal const float BankRetrySeconds = 1.5f;       // 银行空/无目标后的退避
    internal const float ArrivalPauseSeconds = 0.25f;   // 回银行显形后的收势

    internal const float LeisureRadius = 1.0f;          // 仅围绕固定主城锚的小范围闲走
    internal const float LeisureWalkSpeed = 0.6f;
    internal const float LeisurePauseSeconds = 4.4f;    // Idle 2s + Leisure 2.4s，各完整播一次
    internal const float LeisureMaxGroundStep = 0.5f;

    internal const float ThreatRadius = 5.0f;           // 敌情中止半径（局部扫描）
    internal const float ThreatRetrySeconds = 2.5f;     // 目标因敌情/落点被跳过后的重试间隔

    internal const float LandingBackOffset = 2.0f;      // 骑士"后方"（背离其 Side 朝向）水平位移
    internal const float LandingHeight = 2.0f;          // 约两个人高的显形高度
    internal const float MaxLandingToTarget = 3.5f;     // 短程上限：落点放弃与追逐预算共用（唯一常量）
    internal const float VisitMoveBudget = MaxLandingToTarget;  // 单次访问从 landingX 起的总位移上限
    internal const float VisitDeadlineSeconds = 8.0f;   // 单次访问（接近+交接）的游戏秒上限
    internal const float ApproachStopDistance = 0.55f;  // 接近停步：进入交接的位置
    internal const float CoinHandoffRange = 1.2f;       // 逐枚交接的最大距离（约一个人身位的抛币距离）
    internal const float RunSpeed = 7.5f;

    internal const float HomeOffsetX = -3f;             // 固定主城锚：营火 x + 该偏移（负=左侧），首轮试玩值
    internal const float GroundProbeDepth = 6.0f;       // 局部地面探测线段长度
    internal const float LandingClearance = 0.45f;      // 落点/显形点的净空探测高度

    internal const float CoinFlightSeconds = 0.45f;     // 纯显示金币飞行时长
    internal const float CoinFlightArc = 0.55f;         // 弧高（相对直线）
    internal const int CoinFlightCapacity = 16;         // 同时存在的纯显示金币上限
    internal const float CollectPointUp = 0.45f;        // 袋口/手部偏移
    internal const int NextEligibleCapacity = 128;      // 同骑士冷却表上限
}

/// <summary>一次阶段推进的结果：本步是否走完整段、是否有"进入动作"要执行（至多一次）。</summary>
internal readonly struct CoinCourierPhaseStep
{
    internal readonly bool Completed;
    internal readonly CoinCourierAction Action;

    internal CoinCourierPhaseStep(bool completed, CoinCourierAction action)
    {
        Completed = completed;
        Action = action;
    }
}

/// <summary>
/// 固定时长阶段的状态机。规则刻意做成"进入动作在第一次推进时给出、此后由调用方
/// 记 actionConsumed"：即使一帧跨过整段（卡帧/低帧率），每个阶段实例也只会拿到
/// 一次 BagOne/DeliverOne，绝不会漏发或重发；暂停（delta &lt;= 0）不推进也不给动作。
/// </summary>
internal static class CoinCourierPhaseMachine
{
    internal static float Duration(CoinCourierPhase phase)
    {
        switch (phase)
        {
            case CoinCourierPhase.Collect: return CoinCourierTiming.CollectSeconds;
            case CoinCourierPhase.Deliver: return CoinCourierTiming.DeliverSeconds;
            case CoinCourierPhase.JumpOut: return CoinCourierTiming.JumpSeconds;
            case CoinCourierPhase.TeleportIn: return CoinCourierTiming.TeleportSeconds;
            case CoinCourierPhase.Fall: return CoinCourierTiming.FallSeconds;
            case CoinCourierPhase.Land: return CoinCourierTiming.LandSeconds;
            default: return 0f;   // Wait/Approach/Frozen 由距离或外部条件驱动
        }
    }

    internal static bool IsFixed(CoinCourierPhase phase) => Duration(phase) > 0f;

    internal static CoinCourierAction EntryAction(CoinCourierPhase phase)
    {
        switch (phase)
        {
            case CoinCourierPhase.Collect: return CoinCourierAction.BagOne;
            case CoinCourierPhase.Deliver: return CoinCourierAction.DeliverOne;
            default: return CoinCourierAction.None;
        }
    }

    internal static CoinCourierPhaseStep Step(CoinCourierPhase phase, float elapsed, float delta, bool actionConsumed)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0f) elapsed = 0f;
        float duration = Duration(phase);
        if (duration <= 0f) return default;
        if (!float.IsFinite(delta) || delta <= 0f) return default;
        bool completed = (double)elapsed + delta >= duration;
        return new CoinCourierPhaseStep(completed,
            actionConsumed ? CoinCourierAction.None : EntryAction(phase));
    }
}

/// <summary>
/// 行为闸门（顺序敏感）：第一个不满足的条件即结论。Ready=false 只冻结经济，
/// 不清任何 state；未绑定/未拥有则根本不生成角色。
/// </summary>
internal enum CoinCourierBehaviorGate
{
    Open = 0,
    ModDisabled = 1,
    Paused = 2,
    Saving = 3,
    NoAuthority = 4,
    Online = 5,
    WorldNotReady = 6,
    NoState = 7,
    NotReady = 8,
    Faulted = 9,
    NotOwned = 10,
}

internal static class CoinCourierGates
{
    /// <summary>
    /// 顺序敏感：先硬身份/权限/世界失效（清场），再正常菜单暂停/保存/存档未就绪/故障（原地冻结）。
    /// <paramref name="worldValid"/> 必须包含"当前 world/kingdom/gameLayer 存在、处于 Playing 或
    /// Menu、且驻留场景仍属同一 world/layer"——世界替换即使发生在暂停中也属硬失效。
    /// </summary>
    internal static CoinCourierBehaviorGate EvaluateBehavior(bool modEnabled, bool hasWorldAuth, bool online,
        bool worldValid, bool bound, bool owned, bool paused, bool saving, bool stateReady, bool faulted)
    {
        if (!modEnabled) return CoinCourierBehaviorGate.ModDisabled;
        if (!hasWorldAuth) return CoinCourierBehaviorGate.NoAuthority;
        if (online) return CoinCourierBehaviorGate.Online;
        if (!worldValid) return CoinCourierBehaviorGate.WorldNotReady;
        if (!bound) return CoinCourierBehaviorGate.NoState;
        if (!owned) return CoinCourierBehaviorGate.NotOwned;
        if (paused) return CoinCourierBehaviorGate.Paused;
        if (saving) return CoinCourierBehaviorGate.Saving;
        if (!stateReady) return CoinCourierBehaviorGate.NotReady;
        if (faulted) return CoinCourierBehaviorGate.Faulted;
        return CoinCourierBehaviorGate.Open;
    }

    internal static bool EconomyAllowed(CoinCourierBehaviorGate gate)
        => gate == CoinCourierBehaviorGate.Open;

    /// <summary>
    /// 招募付款闸门：只有正在可玩世界、未保存、存档 owner 已绑定且 Ready、
    /// 尚未拥有、且本上下文未锁定时才允许开支付。
    /// </summary>
    internal static bool RecruitmentAllowed(bool sceneActive, bool saving, bool bound,
        bool stateReady, bool owned, bool locked)
        => sceneActive && !saving && bound && stateReady && !owned && !locked;
}

/// <summary>
/// 场景生命周期策略：真正的损失（关功能/失权/联机/世界未就绪/未绑定/未拥有）
/// 立刻清场；暂停、存档未就绪与已冻结只在原地冻结。清场与冻结都不得改动
/// campaign 的 Owned/钱袋（由调用方保证，本层只给结论）。
/// </summary>
internal static class CoinCourierSceneLifecycle
{
    internal static bool ShouldClearScene(CoinCourierBehaviorGate gate)
    {
        switch (gate)
        {
            case CoinCourierBehaviorGate.ModDisabled:
            case CoinCourierBehaviorGate.NoAuthority:
            case CoinCourierBehaviorGate.Online:
            case CoinCourierBehaviorGate.WorldNotReady:
            case CoinCourierBehaviorGate.NoState:
            case CoinCourierBehaviorGate.NotOwned:
                return true;
            default:
                return false;
        }
    }

    internal static bool ShouldFreezeInPlace(CoinCourierBehaviorGate gate)
    {
        switch (gate)
        {
            case CoinCourierBehaviorGate.Paused:
            case CoinCourierBehaviorGate.Saving:
            case CoinCourierBehaviorGate.NotReady:
            case CoinCourierBehaviorGate.Faulted:
                return true;
            default:
                return false;
        }
    }
}

/// <summary>一次目标访问的结束原因：只有正常完成才允许直接飞下一处。</summary>
internal enum CoinCourierVisitOutcome
{
    Completed = 0,
    /// <summary>敌情/目标失效/超距/明确拒绝：携余币回银行，绝不改投下一名骑士。</summary>
    Aborted = 1,
}

internal static class CoinCourierVisitPolicy
{
    internal static bool MayChainNextVisit(CoinCourierVisitOutcome outcome, int availableCoins)
        => outcome == CoinCourierVisitOutcome.Completed && availableCoins > 0;
}

/// <summary>
/// 退役/清理责任闩（纯逻辑）：一次清理只要未完整成功，就保持"清理未完成"，
/// 挡住新建第二家/新的付款；失败按既有维护节奏延后重试，成功才释放责任。
/// </summary>
internal sealed class CoinCourierCleanupGuard
{
    private bool _pending;
    private string _reason = "";
    private float _nextAttemptAt;

    internal bool Pending => _pending;

    internal string Reason => _reason;

    /// <summary>开始一次清理；已在清理中则保留第一次的原因与节奏（不覆盖）。</summary>
    internal void Begin(string reason, float now)
    {
        if (_pending) return;
        _pending = true;
        _reason = reason ?? "";
        _nextAttemptAt = now;
    }

    internal bool Due(float now) => _pending && now >= _nextAttemptAt;

    internal void Defer(float now, float retryDelay)
    {
        if (!_pending) return;
        float delay = float.IsFinite(retryDelay) && retryDelay > 0f ? retryDelay : 1f;
        _nextAttemptAt = now + delay;
    }

    internal void Complete()
    {
        _pending = false;
        _reason = "";
        _nextAttemptAt = 0f;
    }
}

/// <summary>
/// owner 身份策略（纯逻辑）：旧 owner 的 OnDisable 只允许清理"同一实例"的当前店，
/// 绝不清理后来新建的店；没有当前 owner（已清理完成）时同样不允许。
/// </summary>
internal static class CoinCourierOwnerPolicy
{
    internal static bool MayClearCurrent(object currentOwner, object caller)
        => currentOwner != null && ReferenceEquals(currentOwner, caller);
}

/// <summary>
/// 取消退款策略（纯逻辑）：只有"进行中且未结算"的交易才允许走原生取消退款。
/// 已结算（已付）、结果未知（锁存）、以及已到达 Completed 的交易，清理路径绝不再次退款。
/// </summary>
internal static class CoinCourierCancelPolicy
{
    internal static bool MayCancel(bool hasPendingTransaction, bool receiptSettled,
        bool payStateCompleted, bool recruitmentLocked)
        => hasPendingTransaction && !receiptSettled && !payStateCompleted && !recruitmentLocked;
}

/// <summary>落点/接近/掉落/纯显示金币飞行的纯几何。</summary>
internal static class CoinCourierPlacementRules
{
    /// <summary>
    /// 骑士"后方"= 背离其 Side 朝向的一侧：landingX = targetX - side * LandingBackOffset
    /// （side=+1 朝右 → 落在骑士左侧；side=-1 朝左 → 落在骑士右侧）。
    /// 无效 side（0/其他）一律拒绝；不猜测营火位置，也不默认右侧。
    /// </summary>
    internal static bool TryLandingX(float targetX, int side, out float landingX)
    {
        landingX = 0f;
        if (!float.IsFinite(targetX)) return false;
        if (side != -1 && side != 1) return false;
        double x = (double)targetX - side * (double)CoinCourierTiming.LandingBackOffset;
        if (x < float.MinValue || x > float.MaxValue) return false;
        landingX = (float)x;
        return float.IsFinite(landingX);
    }

    /// <summary>
    /// 从当前落点出发，剩余预算内能否把距离压到交接范围内（落地前判"这次访问值不值得落地"）。
    /// 3.5(u) + 1.2(u) 是唯一的一组短程常量，不再有第二个"目标距离上限"。
    /// </summary>
    internal static bool WithinBudgetReach(float dx)
        => float.IsFinite(dx)
        && Math.Abs(dx) <= CoinCourierTiming.VisitMoveBudget + CoinCourierTiming.CoinHandoffRange;

    internal static bool ApproachReached(float dx)
        => float.IsFinite(dx) && Math.Abs(dx) <= CoinCourierTiming.ApproachStopDistance;

    /// <summary>逐枚交接距离：只在 <= CoinHandoffRange 时允许调用经济入口（超距=确定未尝试）。</summary>
    internal static bool WithinHandoffRange(float dx)
        => float.IsFinite(dx) && Math.Abs(dx) <= CoinCourierTiming.CoinHandoffRange;

    /// <summary>本次访问从 landingX 起还能移动多少（夹住单步不越预算）。</summary>
    internal static float RemainingMoveBudget(float moved)
    {
        if (!float.IsFinite(moved) || moved < 0f) return 0f;
        float left = CoinCourierTiming.VisitMoveBudget - moved;
        return left > 0f ? left : 0f;
    }

    /// <summary>掉落高度：t=0 为全高，t≥1 为 0，二次加速（下落感）且单调递减。</summary>
    internal static float FallHeight(float elapsed, float total, float height)
    {
        if (!float.IsFinite(height) || height <= 0f) return 0f;
        if (!float.IsFinite(total) || total <= 0f) return 0f;
        if (!float.IsFinite(elapsed) || elapsed <= 0f) return height;
        if (elapsed >= total) return 0f;
        float t = elapsed / total;
        float value = height * (1f - t * t);
        return value > 0f ? value : 0f;
    }

    /// <summary>
    /// 纯显示金币的抛物线采样：progress≤0 起点、≥1 终点，中段带弧高。
    /// 非有限输入一律拒绝（调用方保留上一位置，不生成不可信画面）。
    /// </summary>
    internal static bool TryCoinFlightSample(float fromX, float fromY, float toX, float toY,
        float progress, out float x, out float y)
    {
        x = fromX;
        y = fromY;
        if (!float.IsFinite(fromX) || !float.IsFinite(fromY)
            || !float.IsFinite(toX) || !float.IsFinite(toY) || !float.IsFinite(progress)) return false;
        if (progress <= 0f) return true;
        if (progress >= 1f)
        {
            x = toX;
            y = toY;
            return true;
        }
        x = fromX + (toX - fromX) * progress;
        float arc = CoinCourierTiming.CoinFlightArc * 4f * progress * (1f - progress);
        y = fromY + (toY - fromY) * progress + arc;
        return float.IsFinite(x) && float.IsFinite(y);
    }
}
