// 金币哥布林运行时切片套件：链接未修改的生产文件（CoinCourierRuntimeRules、CoinCourierShop
// 的纯付款核、CoinCourierCampaignState、CoinCourierPurse），验证审核要求的行为：
// 阶段机一次交接、闸门顺序、Ready/暂停语义、招募价冻结与重复 Completed、退款/LatchedUnknown、
// 场景清场与就地冻结都不触碰 campaign 余额。
//
// 边界：CoinCourierRuntime.cs 本身依赖 Unity/Il2Cpp（Managers/Knight/Banker/Physics2D/Scanner），
// 不在本离线套件内；它的原生调用由 2.4 interop 构建与实机验证覆盖。
using System;
using KingdomEnhancedMod;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

int checks = 0;
void Verify(bool condition, string message)
{
    Check(condition, message);
    checks++;
}

static CoinCourierPurse NewPurse(int coins)
{
    if (!CoinCourierPurse.TryRestore(new CoinCourierPurseSnapshot(coins, false, 0, default),
            out CoinCourierPurse purse, out CoinCourierReason reason))
        throw new Exception("purse restore failed: " + reason);
    return purse;
}

Console.WriteLine("[phase] fixed-duration machine fires each entry action exactly once");
CoinCourierPhaseStep step = CoinCourierPhaseMachine.Step(CoinCourierPhase.Collect, 0f, 5f, false);
Verify(step.Action == CoinCourierAction.BagOne && step.Completed,
    "one giant frame still yields exactly one bag action and completes the segment");
step = CoinCourierPhaseMachine.Step(CoinCourierPhase.Collect, 0f, 5f, true);
Verify(step.Action == CoinCourierAction.None && step.Completed,
    "a consumed entry action is never repeated for the same segment");
step = CoinCourierPhaseMachine.Step(CoinCourierPhase.Collect, 0f, 0f, false);
Verify(step.Action == CoinCourierAction.None && !step.Completed,
    "a paused delta neither advances nor fires an action");
step = CoinCourierPhaseMachine.Step(CoinCourierPhase.Collect, 0f, float.NaN, false);
Verify(step.Action == CoinCourierAction.None && !step.Completed, "a non-finite delta is rejected");
step = CoinCourierPhaseMachine.Step(CoinCourierPhase.Deliver, 0f, 0.1f, false);
Verify(step.Action == CoinCourierAction.DeliverOne && !step.Completed,
    "deliver fires its entry action on the first step");
Verify(CoinCourierPhaseMachine.Step(CoinCourierPhase.Wait, 10f, 10f, false).Action == CoinCourierAction.None,
    "wait has no entry action");
Verify(CoinCourierPhaseMachine.Step(CoinCourierPhase.TeleportIn, 0.18f, 0.01f, true).Completed,
    "an elapsed fixed segment completes on the next step");
Verify(!CoinCourierPhaseMachine.IsFixed(CoinCourierPhase.Approach)
    && !CoinCourierPhaseMachine.IsFixed(CoinCourierPhase.Frozen)
    && !CoinCourierPhaseMachine.IsFixed(CoinCourierPhase.Wait),
    "approach/wait/frozen are driven by distance or external facts");

Verify(CoinCourierPhaseMachine.Duration(CoinCourierPhase.Collect) == 0.72f, "collect = 4 x 0.18s");
Verify(CoinCourierPhaseMachine.Duration(CoinCourierPhase.Deliver) == 0.80f, "deliver = 4 x 0.20s");
Verify(CoinCourierPhaseMachine.Duration(CoinCourierPhase.JumpOut) == 0.40f, "jump = 4 x 0.10s");
Verify(CoinCourierPhaseMachine.Duration(CoinCourierPhase.TeleportIn) == 0.18f,
    "teleport matches the shared strip lifetime");
Verify(CoinCourierPhaseMachine.Duration(CoinCourierPhase.Fall) == 0.24f, "fall = 2 x 0.12s");
Verify(CoinCourierPhaseMachine.Duration(CoinCourierPhase.Land) == 0.24f, "land = 2 x 0.12s");

int actions = 0;
float elapsed = 0f;
bool consumed = false;
for (int i = 0; i < 400; i++)
{
    step = CoinCourierPhaseMachine.Step(CoinCourierPhase.Collect, elapsed, 1f / 60f, consumed);
    if (step.Action == CoinCourierAction.BagOne)
    {
        actions++;
        consumed = true;
    }
    elapsed += 1f / 60f;
    if (step.Completed) break;
}
Verify(actions == 1, "a 60 fps collect segment bags exactly once");
Verify(elapsed >= CoinCourierTiming.CollectSeconds - 0.001f, "the segment completes at its authored duration");

Console.WriteLine("[gate] hard identity/permission/world first, then pause/save freeze");
Verify(CoinCourierGates.EvaluateBehavior(false, true, false, true, true, true, false, false, true, false)
    == CoinCourierBehaviorGate.ModDisabled, "feature off wins over everything");
Verify(CoinCourierGates.EvaluateBehavior(true, false, false, true, true, true, true, false, true, false)
    == CoinCourierBehaviorGate.NoAuthority, "lost authority clears even while paused");
Verify(CoinCourierGates.EvaluateBehavior(true, true, true, true, true, true, true, false, true, false)
    == CoinCourierBehaviorGate.Online, "an online session clears even while paused");
Verify(CoinCourierGates.EvaluateBehavior(true, true, false, false, true, true, true, false, true, false)
    == CoinCourierBehaviorGate.WorldNotReady, "an invalid world clears even while paused");
Verify(CoinCourierGates.EvaluateBehavior(true, true, false, true, false, true, true, false, true, false)
    == CoinCourierBehaviorGate.NoState, "an unbound owner clears immediately, even while paused");
Verify(CoinCourierGates.EvaluateBehavior(true, true, false, true, true, false, true, false, true, false)
    == CoinCourierBehaviorGate.NotOwned, "an unowned campaign clears immediately, even while paused");
Verify(CoinCourierGates.EvaluateBehavior(true, true, false, true, true, true, true, false, true, false)
    == CoinCourierBehaviorGate.Paused, "a normal menu pause freezes only after hard facts pass");
Verify(CoinCourierGates.EvaluateBehavior(true, true, false, true, true, true, false, true, true, false)
    == CoinCourierBehaviorGate.Saving, "a save freezes only after hard facts pass");
Verify(CoinCourierGates.EvaluateBehavior(true, true, false, true, true, true, false, false, false, false)
    == CoinCourierBehaviorGate.NotReady, "Ready=false freezes the economy");
Verify(CoinCourierGates.EvaluateBehavior(true, true, false, true, true, true, false, false, true, true)
    == CoinCourierBehaviorGate.Faulted, "a latched purse fault freezes the economy");
Verify(CoinCourierGates.EvaluateBehavior(true, true, false, true, true, true, false, false, true, false)
    == CoinCourierBehaviorGate.Open, "all facts true opens behavior");

foreach (CoinCourierBehaviorGate gate in Enum.GetValues(typeof(CoinCourierBehaviorGate)))
{
    bool economy = CoinCourierGates.EconomyAllowed(gate);
    Verify(economy == (gate == CoinCourierBehaviorGate.Open), "only Open allows economy: " + gate);
    if (gate == CoinCourierBehaviorGate.Open) continue;
    bool clear = CoinCourierSceneLifecycle.ShouldClearScene(gate);
    bool freeze = CoinCourierSceneLifecycle.ShouldFreezeInPlace(gate);
    Verify(clear != freeze, "every non-open gate is exactly one of clear/freeze: " + gate);
}
Verify(CoinCourierSceneLifecycle.ShouldFreezeInPlace(CoinCourierBehaviorGate.Saving),
    "a save freezes in place instead of clearing the scene");
Verify(CoinCourierSceneLifecycle.ShouldFreezeInPlace(CoinCourierBehaviorGate.Paused),
    "a menu pause freezes in place instead of clearing the scene");
Verify(CoinCourierSceneLifecycle.ShouldFreezeInPlace(CoinCourierBehaviorGate.NotReady),
    "Ready=false freezes in place instead of clearing");
Verify(CoinCourierSceneLifecycle.ShouldClearScene(CoinCourierBehaviorGate.NoState),
    "an unbound owner clears the scene immediately");
Verify(CoinCourierSceneLifecycle.ShouldClearScene(CoinCourierBehaviorGate.NotOwned),
    "an unowned campaign clears any stale scene");

Console.WriteLine("[campaign] clear/freeze never touch ownership or the purse");
CoinCourierPurse purse = NewPurse(5);
FakeState state = new FakeState { Ready = true, Owned = true, Purse = purse };
CoinCourierBehaviorGate clearGate = CoinCourierGates.EvaluateBehavior(
    true, true, false, false, true, true, false, false, true, false);
Verify(CoinCourierSceneLifecycle.ShouldClearScene(clearGate)
    && !CoinCourierGates.EconomyAllowed(clearGate), "a world loss clears the scene and keeps economy closed");
Verify(purse.Capture().Coins == 5 && state.Owned && purse.Capture().Fault.Kind == CoinCourierFaultKind.None,
    "a world unload preserves Owned and the purse balance");
CoinCourierBehaviorGate readyGate = CoinCourierGates.EvaluateBehavior(
    true, true, false, true, true, true, false, false, false, false);
Verify(CoinCourierSceneLifecycle.ShouldFreezeInPlace(readyGate)
    && !CoinCourierGates.EconomyAllowed(readyGate), "Ready=false freezes without economy");
Verify(purse.Capture().Coins == 5 && state.Owned && state.RecordCalls == 0,
    "freezing never zeroes the balance, drops ownership or writes the record");

Console.WriteLine("[access] unbound/throwing owner fails closed");
ThrowingState broken = new ThrowingState();
Verify(!CoinCourierCampaignAccess.TryReadReady(null, out _), "unbound state is not ready");
Verify(!CoinCourierCampaignAccess.TryReadReady(broken, out _), "a throwing Ready getter fails closed");
Verify(!CoinCourierCampaignAccess.TryReadEconomy(broken, out _, out _, out _, out _),
    "a throwing owner never yields an economy");
Verify(!CoinCourierCampaignAccess.TryRecordRecruitment(broken, out bool recorded)
    && !recorded, "a throwing record call is unknown, not a fake success");
Verify(!CoinCourierCampaignAccess.TryRecordRecruitment(null, out _), "unbound record is unknown");
Verify(!CoinCourierCampaignAccess.TryReadPurse(new FakeState { Purse = null }, out _),
    "a null purse is never invented");

Console.WriteLine("[recruit] frozen price, single record, refund and unknown latch");
CoinCourierShopPayment payment = new CoinCourierShopPayment();
Verify(!payment.Consume(1, true, 8), "consume before arm is refused");
payment.Arm(1, 8);
Verify(payment.IsArmed(1) && !payment.IsArmed(2) && payment.Price == 8,
    "arm freezes the price for one payer");
Verify(!payment.Consume(1, true, 9),
    "a different floating count (price change) cannot settle the frozen transaction");
Verify(!payment.Consume(1, false, 8), "only a completed payment may settle");
Verify(payment.Consume(1, true, 8), "the frozen price consumes once");
Verify(payment.AlreadySettled(1) && !payment.IsArmed(1), "the receipt records the settled payer");
Verify(!payment.Consume(1, true, 8), "a repeated Completed callback cannot settle twice");
payment.Clear();
Verify(!payment.AlreadySettled(1) && !payment.IsArmed(1), "clear resets both armed and settled");

FakeState recruit = new FakeState { Ready = true, Owned = false, Purse = NewPurse(0), RecordResult = true };
CoinCourierShopPayment pay = new CoinCourierShopPayment();
pay.Arm(7, 8);
CoinCourierSettleResult result = CoinCourierRecruitmentFlow.Settle(pay, 7, true, 8, recruit.TryRecordRecruitment, null);
Verify(result == CoinCourierSettleResult.Owned && recruit.RecordCalls == 1,
    "a completed payment records ownership exactly once");
result = CoinCourierRecruitmentFlow.Settle(pay, 7, true, 8, recruit.TryRecordRecruitment, null);
Verify(result == CoinCourierSettleResult.Ignored && recruit.RecordCalls == 1,
    "a repeated Completed never records ownership twice");

CoinCourierShopPayment pay2 = new CoinCourierShopPayment();
pay2.Arm(7, 8);
result = CoinCourierRecruitmentFlow.Settle(pay2, 7, true, 9, recruit.TryRecordRecruitment, null);
Verify(result == CoinCourierSettleResult.Ignored && recruit.RecordCalls == 1,
    "a post-arm price change cannot settle the frozen transaction");
result = CoinCourierRecruitmentFlow.Settle(pay2, 7, true, 8, recruit.TryRecordRecruitment, null);
Verify(result == CoinCourierSettleResult.Owned && recruit.RecordCalls == 2,
    "the frozen price still settles after an unrelated amount callback");

FakeState refuse = new FakeState { Ready = true, Owned = false, Purse = NewPurse(0), RecordResult = false };
CoinCourierShopPayment pay3 = new CoinCourierShopPayment();
pay3.Arm(3, 8);
result = CoinCourierRecruitmentFlow.Settle(pay3, 3, true, 8, refuse.TryRecordRecruitment, null);
Verify(result == CoinCourierSettleResult.Refunded && refuse.RecordCalls == 1,
    "a definite no-write refunds exactly once");
Verify(!refuse.Owned && refuse.Purse.Coins == 0, "a refused recruitment leaves no goblin and no bag credit");
pay3.Clear();
pay3.Arm(3, 8);
result = CoinCourierRecruitmentFlow.Settle(pay3, 3, true, 8, refuse.TryRecordRecruitment, null);
Verify(result == CoinCourierSettleResult.Refunded && refuse.RecordCalls == 2,
    "after a refund the same context may try again exactly once more");

FakeState brokenRecord = new FakeState { Ready = true, Owned = false, Purse = NewPurse(0), ThrowOnRecord = true };
CoinCourierShopPayment pay4 = new CoinCourierShopPayment();
pay4.Arm(4, 8);
result = CoinCourierRecruitmentFlow.Settle(pay4, 4, true, 8, brokenRecord.TryRecordRecruitment, null);
Verify(result == CoinCourierSettleResult.LatchedUnknown, "an unknown record outcome latches the payment context");
result = CoinCourierRecruitmentFlow.Settle(pay4, 4, true, 8, brokenRecord.TryRecordRecruitment, null);
Verify(result == CoinCourierSettleResult.Ignored && brokenRecord.RecordCalls == 1,
    "a latched context is never retried by the payment callback");
Verify(!brokenRecord.Owned && brokenRecord.Purse.Coins == 0, "an unknown outcome never fakes ownership or a refund");

Verify(CoinCourierGates.RecruitmentAllowed(true, false, true, true, false, false),
    "a ready, unowned, unbound-free campaign may recruit");
Verify(!CoinCourierGates.RecruitmentAllowed(true, true, true, true, false, false), "saving blocks recruitment");
Verify(!CoinCourierGates.RecruitmentAllowed(true, false, false, true, false, false),
    "an unbound save owner blocks recruitment");
Verify(!CoinCourierGates.RecruitmentAllowed(true, false, true, false, false, false),
    "Ready=false blocks recruitment");
Verify(!CoinCourierGates.RecruitmentAllowed(true, false, true, true, true, false),
    "an owned campaign blocks a second goblin");
Verify(!CoinCourierGates.RecruitmentAllowed(true, false, true, true, false, true),
    "a locked unknown outcome blocks recruitment");
Verify(!CoinCourierGates.RecruitmentAllowed(false, false, true, true, false, false),
    "a missing live shop blocks recruitment");

Console.WriteLine("[geometry] knight-relative landing, approach, fall and the display coin arc");
Verify(CoinCourierPlacementRules.TryLandingX(10f, 1, out float lx) && lx == 8f,
    "a right-facing Knight lands two units to its left (behind)");
Verify(CoinCourierPlacementRules.TryLandingX(10f, -1, out lx) && lx == 12f,
    "a left-facing Knight lands two units to its right (behind)");
Verify(CoinCourierPlacementRules.TryLandingX(-10f, 1, out lx) && lx == -12f,
    "the left-side Knight mirror is symmetric");
Verify(CoinCourierPlacementRules.TryLandingX(-10f, -1, out lx) && lx == -8f,
    "the right-side Knight mirror is symmetric");
Verify(!CoinCourierPlacementRules.TryLandingX(10f, 0, out _)
    && !CoinCourierPlacementRules.TryLandingX(10f, 2, out _)
    && !CoinCourierPlacementRules.TryLandingX(10f, -2, out _),
    "an invalid/unknown side is rejected instead of guessing a side");
Verify(!CoinCourierPlacementRules.TryLandingX(float.NaN, 1, out _),
    "a non-finite target is rejected");
Verify(CoinCourierPlacementRules.WithinBudgetReach(4.7f)
    && !CoinCourierPlacementRules.WithinBudgetReach(4.71f), "budget reach threshold (3.5 + 1.2)");
Verify(CoinCourierPlacementRules.WithinHandoffRange(1.2f)
    && !CoinCourierPlacementRules.WithinHandoffRange(1.21f), "handoff range threshold (1.2)");
Verify(CoinCourierPlacementRules.ApproachReached(0.55f)
    && !CoinCourierPlacementRules.ApproachReached(0.56f), "approach stop threshold");
Verify(!CoinCourierPlacementRules.WithinBudgetReach(float.PositiveInfinity),
    "an infinite distance cancels the drop");
Verify(CoinCourierPlacementRules.RemainingMoveBudget(0f) == 3.5f
    && CoinCourierPlacementRules.RemainingMoveBudget(3.5f) == 0f
    && CoinCourierPlacementRules.RemainingMoveBudget(4f) == 0f
    && CoinCourierPlacementRules.RemainingMoveBudget(float.NaN) == 0f,
    "the visit budget never goes negative and rejects non-finite progress");
Verify(CoinCourierPlacementRules.FallHeight(0f, 0.24f, 2f) == 2f, "fall starts at the full height");
Verify(CoinCourierPlacementRules.FallHeight(0.24f, 0.24f, 2f) == 0f, "fall ends on the ground");
float mid = CoinCourierPlacementRules.FallHeight(0.12f, 0.24f, 2f);
Verify(mid < 2f && mid > 0f, "fall passes through the middle");
Verify(CoinCourierPlacementRules.FallHeight(float.NaN, 0.24f, 2f) == 2f
    && CoinCourierPlacementRules.FallHeight(0f, 0f, 2f) == 0f, "degenerate fall inputs stay safe");

Verify(CoinCourierPlacementRules.TryCoinFlightSample(0f, 0f, 10f, 0f, 0.5f, out float fx, out float fy)
    && fx == 5f && fy > 0f, "the display coin arcs above the straight line");
Verify(CoinCourierPlacementRules.TryCoinFlightSample(0f, 0f, 10f, 0f, 1.5f, out fx, out fy)
    && fx == 10f && fy == 0f, "the display coin lands exactly on the target");
Verify(CoinCourierPlacementRules.TryCoinFlightSample(0f, 0f, 10f, 0f, -1f, out fx, out fy)
    && fx == 0f && fy == 0f, "a negative progress stays at the start");
Verify(!CoinCourierPlacementRules.TryCoinFlightSample(float.NaN, 0f, 10f, 0f, 0.5f, out _, out _),
    "a non-finite display flight is rejected");

Console.WriteLine("[policies] visit outcome, cleanup responsibility, owner identity, cancel refunds");
Verify(CoinCourierVisitPolicy.MayChainNextVisit(CoinCourierVisitOutcome.Completed, 3),
    "a completed visit with coins left may chain to the next Knight");
Verify(!CoinCourierVisitPolicy.MayChainNextVisit(CoinCourierVisitOutcome.Aborted, 3),
    "an aborted visit (enemy/invalid/too-far/refused) must return to the bank, never chain");
Verify(!CoinCourierVisitPolicy.MayChainNextVisit(CoinCourierVisitOutcome.Completed, 0),
    "an empty purse returns to the bank");

CoinCourierCleanupGuard cleanup = new CoinCourierCleanupGuard();
Verify(!cleanup.Pending && !cleanup.Due(0f), "a guard is idle before any cleanup");
cleanup.Begin("recruited", 10f);
Verify(cleanup.Pending && cleanup.Due(10f) && cleanup.Reason == "recruited",
    "a begun cleanup is due immediately and keeps its reason");
cleanup.Begin("other-reason", 11f);
Verify(cleanup.Reason == "recruited", "a second Begin never overwrites the first responsibility");
cleanup.Defer(10f, 1f);
Verify(!cleanup.Due(10.5f) && cleanup.Due(11f) && cleanup.Pending,
    "a deferred retry stays pending and blocks new shops until due");
cleanup.Complete();
Verify(!cleanup.Pending && !cleanup.Due(12f) && cleanup.Reason.Length == 0,
    "a fully successful cleanup releases the responsibility");
cleanup.Begin("again", 12f);
Verify(cleanup.Pending, "a released guard can be re-armed for the next shop");

object ownerA = new object();
object ownerB = new object();
Verify(CoinCourierOwnerPolicy.MayClearCurrent(ownerA, ownerA),
    "the current owner instance may clear its shop");
Verify(!CoinCourierOwnerPolicy.MayClearCurrent(ownerA, ownerB),
    "an old owner instance may never clear a later shop");
Verify(!CoinCourierOwnerPolicy.MayClearCurrent(null, ownerA),
    "a released shop has no owner to clear");

Verify(CoinCourierCancelPolicy.MayCancel(true, false, false, false),
    "an in-flight, unsettled transaction may be cancelled with a native refund");
Verify(!CoinCourierCancelPolicy.MayCancel(true, true, false, false),
    "a settled (paid) payment is never refunded again during cleanup");
Verify(!CoinCourierCancelPolicy.MayCancel(true, false, true, false),
    "a Completed payment is never treated as unfinished during cleanup");
Verify(!CoinCourierCancelPolicy.MayCancel(true, false, false, true),
    "an unknown (latched) outcome is never refunded during cleanup");
Verify(!CoinCourierCancelPolicy.MayCancel(false, false, false, false),
    "an idle player has nothing to cancel");

Console.WriteLine("ALL PASS — " + checks + " checks");
return 0;

internal sealed class FakeState : ICoinCourierCampaignState
{
    public bool Ready { get; set; }
    public bool Owned { get; set; }
    public CoinCourierPurse Purse { get; set; }
    public bool RecordResult { get; set; }
    public bool ThrowOnRecord { get; set; }
    public int RecordCalls { get; set; }

    public CoinCourierAvailabilityInfo Availability
        => CoinCourierAvailabilityInfo.Of(Ready ? CoinCourierAvailability.Ready : CoinCourierAvailability.NotBound);

    public bool TryRecordRecruitment()
    {
        RecordCalls++;
        if (ThrowOnRecord) throw new InvalidOperationException("record unavailable");
        return RecordResult;
    }
}

internal sealed class ThrowingState : ICoinCourierCampaignState
{
    public CoinCourierAvailabilityInfo Availability => throw new InvalidOperationException("availability");
    public bool Ready => throw new InvalidOperationException("ready");
    public bool Owned => throw new InvalidOperationException("owned");
    public CoinCourierPurse Purse => throw new InvalidOperationException("purse");
    public bool TryRecordRecruitment() => throw new InvalidOperationException("record");
}
