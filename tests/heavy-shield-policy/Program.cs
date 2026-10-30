using KingdomEnhancedMod;

var cases = new (string Name, Action Run)[]
{
    ("direction and damage classification", DirectionAndDamageClassification),
    ("natural-second charge gate", NaturalSecondChargeGate),
    ("ninety direct hits and one deferred demotion", NinetyHitsAndDemotion),
    ("death supersedes deferred demotion", DeathSupersedesDemotion),
    ("demotion lease confirms only successful native replacement", DemotionLease),
    ("bash target identity and cooldown", BashTargetsAndCooldown),
    ("reset on load and pool reuse", ResetOnLoadAndReuse),
    ("checked repair cannot revive or overwrite damage", CheckedRepair),
};

foreach (var test in cases)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}
Console.WriteLine($"PASS {cases.Length} heavy shield policy scenarios");

// A successful legacy scenario models one qualified impact per natural second.
static double NextTime() => NaturalClock.Next();

static HeavyShieldBlockPolicy Ready()
{
    var policy = new HeavyShieldBlockPolicy();
    Check(policy.Activate(), "first activation");
    return policy;
}

static HeavyShieldBlockPolicy.Hit Direct(float source, float defender, int facing) =>
    new(true, true, source, defender, facing);

static HeavyShieldBlockPolicy.HitResult Eval(HeavyShieldBlockPolicy policy, HeavyShieldBlockPolicy.Hit hit)
    => policy.EvaluateHit(hit, NextTime());

static HeavyShieldBlockPolicy.HitResult EvalAt(HeavyShieldBlockPolicy policy, HeavyShieldBlockPolicy.Hit hit, double at)
    => policy.EvaluateHit(hit, at);

static HeavyShieldBlockPolicy.HitResult ApplyNativeHit(
    HeavyShieldBlockPolicy policy, HeavyShieldBlockPolicy.Hit hit, int damage, ref int nativeHealth)
{
    var result = Eval(policy, hit);
    if (result == HeavyShieldBlockPolicy.HitResult.PassThrough)
    {
        nativeHealth -= damage;
        if (nativeHealth <= 0) policy.MarkDead(); // Native death callback, after damage resolves.
    }
    return result;
}

static void DirectionAndDamageClassification()
{
    var policy = Ready();
    var pass = HeavyShieldBlockPolicy.HitResult.PassThrough;
    Check(Eval(policy, Direct(9, 10, 1)) == pass, "rear strike passes through");
    Check(Eval(policy, new(false, true, 11, 10, 1)) == pass, "front area damage passes through");
    Check(Eval(policy, new(true, false, 11, 10, 1)) == pass, "missing source passes through");
    Check(Eval(policy, Direct(10, 10, 1)) == pass, "neutral source passes through");
    Check(Eval(policy, Direct(11, 10, 0)) == pass, "unknown facing passes through");
    Check(Eval(policy, Direct(float.NaN, 10, 1)) == pass, "invalid coordinate passes through");
    Check(policy.Durability == 90, "pass-through never spends shield");
    Check(Eval(policy, Direct(9, 10, -1)) == HeavyShieldBlockPolicy.HitResult.Blocked,
        "mirrored facing blocks its front");
    Check(policy.Durability == 89, "mirrored front hit spends one durability");
}

static void NaturalSecondChargeGate()
{
    var hit = Direct(11, 10, 1);

    var policy = Ready();
    Check(EvalAt(policy, hit, 10d) == HeavyShieldBlockPolicy.HitResult.Blocked && policy.Durability == 89,
        "first valid natural sample charges one");
    for (var i = 0; i < 100; i++)
        Check(EvalAt(policy, hit, 10d) == HeavyShieldBlockPolicy.HitResult.Blocked && policy.Durability == 89,
            "same natural instant never spends another point");
    Check(EvalAt(policy, hit, 10.999d) == HeavyShieldBlockPolicy.HitResult.Blocked && policy.Durability == 89,
        "0.999 seconds is inside the half-open interval");
    Check(EvalAt(policy, hit, 11d) == HeavyShieldBlockPolicy.HitResult.Blocked && policy.Durability == 88,
        "exactly one second charges again");
    for (var i = 0; i < 100; i++) EvalAt(policy, hit, 11d);
    Check(policy.Durability == 88, "repeated equal sample after a charge stays free");

    // Invalid samples keep the original per-hit wear and never move the stamp.
    Check(EvalAt(policy, hit, double.NaN) == HeavyShieldBlockPolicy.HitResult.Blocked && policy.Durability == 87,
        "NaN falls back to one point per hit");
    Check(EvalAt(policy, hit, double.PositiveInfinity) == HeavyShieldBlockPolicy.HitResult.Blocked && policy.Durability == 86,
        "positive infinity falls back to one point per hit");
    Check(EvalAt(policy, hit, double.NegativeInfinity) == HeavyShieldBlockPolicy.HitResult.Blocked && policy.Durability == 85,
        "negative infinity falls back to one point per hit");
    Check(EvalAt(policy, hit, 10.5d) == HeavyShieldBlockPolicy.HitResult.Blocked && policy.Durability == 84,
        "backwards sample falls back to one point per hit");
    Check(EvalAt(policy, hit, 10.5d) == HeavyShieldBlockPolicy.HitResult.Blocked && policy.Durability == 83,
        "repeated backwards samples cannot become free blocks");
    Check(EvalAt(policy, hit, 11.999d) == HeavyShieldBlockPolicy.HitResult.Blocked && policy.Durability == 83,
        "valid forward sample still measures from the last written valid charge");
    Check(EvalAt(policy, hit, 12d) == HeavyShieldBlockPolicy.HitResult.Blocked && policy.Durability == 82,
        "forward half-open boundary remains exact after invalid samples");

    // Each life owns its own transient stamp.
    var left = Ready();
    var right = Ready();
    Check(EvalAt(left, hit, 5d) == HeavyShieldBlockPolicy.HitResult.Blocked && left.Durability == 89, "left actor first charge");
    Check(EvalAt(right, hit, 5d) == HeavyShieldBlockPolicy.HitResult.Blocked && right.Durability == 89, "right actor independent first charge");
    EvalAt(left, hit, 5.5d); EvalAt(right, hit, 5.5d);
    Check(left.Durability == 89 && right.Durability == 89, "actors do not share a charge interval");

    // Reset starts a new life with no stamp; repeat activation is rejected and cannot refresh.
    left.Reset();
    Check(left.Activate() && left.Durability == 90, "reset then activate is a fresh life");
    Check(EvalAt(left, hit, 5.5d) == HeavyShieldBlockPolicy.HitResult.Blocked && left.Durability == 89,
        "new life first valid sample charges despite the old life stamp");
    var restored = new HeavyShieldBlockPolicy();
    Check(restored.Activate() && EvalAt(restored, hit, 20d) == HeavyShieldBlockPolicy.HitResult.Blocked
        && restored.Durability == 89, "restored life first charge");
    Check(!restored.Restore(89, false, false) && restored.Durability == 89,
        "repeat attachment cannot replace the active life");
    Check(EvalAt(restored, hit, 20.5d) == HeavyShieldBlockPolicy.HitResult.Blocked && restored.Durability == 89,
        "repeat attachment does not refresh the natural-second window");

    // Repair restores full durability but does not refresh the transient charge stamp.
    Check(restored.TryRepair(89) && restored.Durability == 90, "checked repair restores the full ninety");
    Check(EvalAt(restored, hit, 20.5d) == HeavyShieldBlockPolicy.HitResult.Blocked && restored.Durability == 90,
        "repair does not reset the remaining interval");
    Check(EvalAt(restored, hit, 21d) == HeavyShieldBlockPolicy.HitResult.Blocked && restored.Durability == 89,
        "post-repair boundary still charges after one natural second");

    var dying = Ready();
    for (var i = 0; i < 89; i++) EvalAt(dying, hit, i);
    Check(dying.Durability == 1 && EvalAt(dying, hit, 89d) == HeavyShieldBlockPolicy.HitResult.ShieldBroken,
        "the ninetieth charged hit still blocks and then breaks");
    Check(dying.Durability == 0 && dying.State == HeavyShieldBlockPolicy.LifeState.PendingDemote,
        "broken shield queues retirement only after the final block");
}

static void NinetyHitsAndDemotion()
{
    var policy = Ready();
    var front = Direct(11, 10, 1);
    for (int hit = 1; hit < 90; hit++)
        Check(Eval(policy, front) == HeavyShieldBlockPolicy.HitResult.Blocked && policy.Durability == 90 - hit,
            "each qualified hit spends one of ninety points");
    Check(Eval(policy, front) == HeavyShieldBlockPolicy.HitResult.ShieldBroken && policy.Durability == 0,
        "ninetieth hit still blocks, then breaks");
    Check(policy.State == HeavyShieldBlockPolicy.LifeState.PendingDemote,
        "breaking only queues conversion for after the damage chain");
    Check(Eval(policy, front) == HeavyShieldBlockPolicy.HitResult.PassThrough,
        "no ninety-first hit is blocked while conversion is pending");
    Check(!policy.TryBeginBash(20), "broken shield cannot bash");
    Check(policy.TryBeginDemote(out var lease), "deferred conversion can begin after damage chain");
    Check(policy.ConfirmDemote(lease), "confirmed native conversion commits once");
    Check(!policy.TryBeginDemote(out _) && policy.State == HeavyShieldBlockPolicy.LifeState.Demoted,
        "duplicate callback cannot create another Peasant");
    Check(!policy.Activate(), "fresh purchase needs reset and a new life binding");
}

static void DeathSupersedesDemotion()
{
    var policy = Ready();
    var nativeHealth = 1;
    Check(ApplyNativeHit(policy, Direct(11, 10, 1), 99, ref nativeHealth)
            == HeavyShieldBlockPolicy.HitResult.Blocked,
        "a high-damage frontal hit is still absorbed by the shield");
    Check(nativeHealth == 1 && policy.State == HeavyShieldBlockPolicy.LifeState.Guarding
            && policy.Durability == 89,
        "predicted lethality cannot bypass frontal protection");

    foreach (var areaDamage in new[] { false, true })
    {
        policy = Ready();
        nativeHealth = 1;
        for (var i = 0; i < 90; i++) Eval(policy, Direct(11, 10, 1));
        Check(policy.State == HeavyShieldBlockPolicy.LifeState.PendingDemote, "shield broke");
        var hit = areaDamage ? new HeavyShieldBlockPolicy.Hit(false, true, 11, 10, 1)
                             : Direct(9, 10, 1);
        Check(ApplyNativeHit(policy, hit, 2, ref nativeHealth)
                == HeavyShieldBlockPolicy.HitResult.PassThrough,
            "rear or area damage reaches native health");
        Check(policy.State == HeavyShieldBlockPolicy.LifeState.Dead
                && !policy.TryBeginDemote(out _),
            "native death callback cancels pending Peasant conversion");
    }
}

static void DemotionLease()
{
    var policy = Ready();
    for (var i = 0; i < 90; i++) Eval(policy, Direct(11, 10, 1));
    Check(policy.TryBeginDemote(out var first), "first native attempt begins");
    Check(!policy.TryBeginDemote(out _), "reentrant callback cannot start a second replacement");
    Check(!policy.ConfirmDemote(first + 1) && !policy.AbortDemoteUnchanged(first + 1),
        "foreign lease cannot mutate active attempt");
    Check(policy.State == HeavyShieldBlockPolicy.LifeState.Demoting,
        "an uncertain native result holds the lease and cannot be retried");
    Check(policy.AbortDemoteUnchanged(first), "verified no replacement returns to pending");
    Check(!policy.TryBeginDemote(out _) && policy.RetirementUnknown,
        "one native attempt remains unknown even when unchanged; never replay");

    policy = Ready();
    for (var i = 0; i < 90; i++) Eval(policy, Direct(11, 10, 1));
    Check(policy.TryBeginDemote(out var dyingLease), "conversion attempt starts");
    policy.MarkDead();
    Check(!policy.ConfirmDemote(dyingLease) && !policy.AbortDemoteUnchanged(dyingLease)
            && policy.State == HeavyShieldBlockPolicy.LifeState.Dead,
        "death during native attempt invalidates its lease");

    policy = Ready();
    for (var i = 0; i < 90; i++) Eval(policy, Direct(11, 10, 1));
    for (var i = 0; i < HeavyShieldBlockPolicy.MaxDemoteAttempts; i++)
    {
        Check(policy.TryBeginDemote(out var lease), "bounded retry starts");
        Check(policy.AbortDemoteUnchanged(lease), "verified failed conversion remains pending");
    }
    Check(!policy.TryBeginDemote(out _) && policy.State == HeavyShieldBlockPolicy.LifeState.Demoting,
        "retry exhaustion requires bridge reconciliation, not unbounded native calls");
    var loaded = new HeavyShieldBlockPolicy();
    Check(loaded.Restore(0, true, true) && !loaded.TryBeginDemote(out _), "loaded unknown retirement never repeats native call");
    loaded = new HeavyShieldBlockPolicy();
    Check(loaded.Restore(1, false, false) && loaded.Durability == 1, "saved wear preserved");
    Check(!loaded.Activate() && !loaded.Restore(3, false, false), "repeated binding cannot repair shield");
    Check(!new HeavyShieldBlockPolicy().Restore(0, false, false), "invalid zero without pending break rejected");
}

static void BashTargetsAndCooldown()
{
    var policy = Ready();
    Check(policy.TryBeginBash(100), "first bash starts immediately");
    Check(!policy.TryBeginBash(100), "one bash cannot be opened twice");
    Check(policy.TryRegisterBashTarget(10, true), "ordinary target A");
    Check(!policy.TryRegisterBashTarget(10, true), "A is hit once even if queried twice");
    Check(!policy.TryRegisterBashTarget(20, false), "boss/hunt/building is not eligible");
    Check(!policy.TryRegisterBashTarget(0, true), "unknown identity is unsafe to deduplicate");
    Check(policy.TryRegisterBashTarget(20, true) && policy.TryRegisterBashTarget(30, true),
        "three ordinary targets are accepted");
    Check(!policy.TryRegisterBashTarget(40, true), "fourth target is excluded");
    policy.EndBash();
    Check(!policy.TryRegisterBashTarget(50, true), "no late hit after impact window");
    Check(!policy.TryBeginBash(108), "old eight-second boundary stays on cooldown");
    Check(!policy.TryBeginBash(109.999f), "ten second cooldown");
    Check(policy.TryBeginBash(110), "cooldown boundary");
    Check(policy.TryRegisterBashTarget(10, true), "new bash has independent target cache");
    for (int hit = 0; hit < 89; hit++) Eval(policy, Direct(11, 10, 1));
    Check(policy.BashActive && policy.Durability == 1, "half shield remains combat capable");
    Eval(policy, Direct(11, 10, 1));
    Check(!policy.BashActive && !policy.TryRegisterBashTarget(50, true),
        "shield break closes an active bash");
}

static void ResetOnLoadAndReuse()
{
    var policy = Ready();
    Check(policy.TryBeginBash(0) && policy.TryRegisterBashTarget(1001, true), "old life entered combat");
    Eval(policy, Direct(11, 10, 1));
    policy.Reset(); // Both load reconstruction and pool reuse must pass this boundary.
    Check(policy.State == HeavyShieldBlockPolicy.LifeState.Inactive && policy.Durability == 0,
        "unbound instance cannot retain a paid role");
    Check(!policy.TryRegisterBashTarget(1002, true) && !policy.TryBeginDemote(out _),
        "old impact and conversion callbacks have no state to consume");
    Check(policy.Activate() && policy.Durability == 90, "new purchase starts with fresh shield");
    Check(policy.TryBeginBash(0) && policy.TryRegisterBashTarget(1001, true),
        "new life has no old cooldown or target cache");
    for (var i = 0; i < 90; i++) Eval(policy, Direct(11, 10, 1));
    Check(policy.TryBeginDemote(out var oldLease), "old life can hold a conversion lease");
    policy.Reset();
    Check(policy.Activate(), "new pooled life activates");
    for (var i = 0; i < 90; i++) Eval(policy, Direct(11, 10, 1));
    Check(policy.TryBeginDemote(out var newLease) && newLease != oldLease,
        "pooled life uses a new conversion lease");
    Check(!policy.ConfirmDemote(oldLease) && policy.AbortDemoteUnchanged(newLease),
        "stale completion cannot convert pooled life");
    policy.MarkDead();
    policy.Reset();
    Check(policy.Activate(), "dead pooled object can be bound to another life");
}

static void CheckedRepair()
{
    var policy = new HeavyShieldBlockPolicy();
    Check(!policy.TryRepair(20), "inactive career cannot repair");
    Check(policy.Restore(20, false, false), "worn career restored");
    Check(!policy.TryRepair(19) && policy.Durability == 20, "stale expected wear cannot overwrite current state");
    Check(policy.TryRepair(20) && policy.Durability == 90, "checked completed work repairs to full");
    Check(!policy.TryRepair(20) && !policy.TryRepair(90), "same completion and full shield cannot repair again");
    policy = new HeavyShieldBlockPolicy(); policy.Restore(20, false, false);
    Check(policy.TryBeginBash(0) && !policy.TryRepair(20), "combat animation cannot be repaired mid-impact");
    policy.EndBash(); policy.MarkDead();
    Check(!policy.TryRepair(0) && policy.Durability == 0, "dead career cannot revive");
    policy = new HeavyShieldBlockPolicy(); policy.Restore(0, true, false);
    Check(!policy.TryRepair(0) && policy.PendingBreak, "broken shield retains native retirement");
    Check(policy.TryBeginDemote(out _) && !policy.TryRepair(0), "unknown native retirement cannot repair");
}

static void Check(bool condition, string explanation)
{
    if (!condition) throw new Exception(explanation);
}

internal static class NaturalClock
{
    private static double _value = 1000d;
    internal static double Next() => _value += 1d;
}
