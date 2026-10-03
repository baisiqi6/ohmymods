using KingdomEnhancedMod;

var cases = new (string Name, Action Run)[]
{
    ("direction and damage classification", DirectionAndDamageClassification),
    ("four direct hits and one deferred demotion", FourHitsAndDemotion),
    ("death supersedes deferred demotion", DeathSupersedesDemotion),
    ("demotion lease confirms only successful native replacement", DemotionLease),
    ("bash target identity and cooldown", BashTargetsAndCooldown),
    ("reset on load and pool reuse", ResetOnLoadAndReuse),
};

foreach (var test in cases)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}
Console.WriteLine($"PASS {cases.Length} heavy shield policy scenarios");

static HeavyShieldBlockPolicy Ready()
{
    var policy = new HeavyShieldBlockPolicy();
    Check(policy.Activate(), "first activation");
    return policy;
}

static HeavyShieldBlockPolicy.Hit Direct(float source, float defender, int facing) =>
    new(true, true, source, defender, facing);

static HeavyShieldBlockPolicy.HitResult ApplyNativeHit(
    HeavyShieldBlockPolicy policy, HeavyShieldBlockPolicy.Hit hit, int damage, ref int nativeHealth)
{
    var result = policy.EvaluateHit(hit);
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
    Check(policy.EvaluateHit(Direct(9, 10, 1)) == pass, "rear strike passes through");
    Check(policy.EvaluateHit(new(false, true, 11, 10, 1)) == pass, "front area damage passes through");
    Check(policy.EvaluateHit(new(true, false, 11, 10, 1)) == pass, "missing source passes through");
    Check(policy.EvaluateHit(Direct(10, 10, 1)) == pass, "neutral source passes through");
    Check(policy.EvaluateHit(Direct(11, 10, 0)) == pass, "unknown facing passes through");
    Check(policy.EvaluateHit(Direct(float.NaN, 10, 1)) == pass, "invalid coordinate passes through");
    Check(policy.Durability == 4, "pass-through never spends shield");
    Check(policy.EvaluateHit(Direct(9, 10, -1)) == HeavyShieldBlockPolicy.HitResult.Blocked,
        "mirrored facing blocks its front");
    Check(policy.Durability == 3, "mirrored front hit spends one durability");
}

static void FourHitsAndDemotion()
{
    var policy = Ready();
    var front = Direct(11, 10, 1);
    Check(policy.EvaluateHit(front) == HeavyShieldBlockPolicy.HitResult.Blocked && policy.Durability == 3,
        "first direct hit");
    Check(policy.EvaluateHit(front) == HeavyShieldBlockPolicy.HitResult.Blocked && policy.Durability == 2,
        "second direct hit");
    Check(policy.EvaluateHit(front) == HeavyShieldBlockPolicy.HitResult.Blocked && policy.Durability == 1,
        "third direct hit retains the half shield");
    Check(policy.EvaluateHit(front) == HeavyShieldBlockPolicy.HitResult.ShieldBroken && policy.Durability == 0,
        "fourth hit breaks shield");
    Check(policy.State == HeavyShieldBlockPolicy.LifeState.PendingDemote,
        "breaking only queues conversion for after the damage chain");
    Check(policy.EvaluateHit(front) == HeavyShieldBlockPolicy.HitResult.PassThrough,
        "no fifth hit is blocked while conversion is pending");
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
            && policy.Durability == 3,
        "predicted lethality cannot bypass frontal protection");

    foreach (var areaDamage in new[] { false, true })
    {
        policy = Ready();
        nativeHealth = 1;
        for (var i = 0; i < 4; i++) policy.EvaluateHit(Direct(11, 10, 1));
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
    for (var i = 0; i < 4; i++) policy.EvaluateHit(Direct(11, 10, 1));
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
    for (var i = 0; i < 4; i++) policy.EvaluateHit(Direct(11, 10, 1));
    Check(policy.TryBeginDemote(out var dyingLease), "conversion attempt starts");
    policy.MarkDead();
    Check(!policy.ConfirmDemote(dyingLease) && !policy.AbortDemoteUnchanged(dyingLease)
            && policy.State == HeavyShieldBlockPolicy.LifeState.Dead,
        "death during native attempt invalidates its lease");

    policy = Ready();
    for (var i = 0; i < 4; i++) policy.EvaluateHit(Direct(11, 10, 1));
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
    Check(!policy.TryBeginBash(107.999f), "eight second cooldown");
    Check(policy.TryBeginBash(108), "cooldown boundary");
    Check(policy.TryRegisterBashTarget(10, true), "new bash has independent target cache");
    policy.EvaluateHit(Direct(11, 10, 1));
    policy.EvaluateHit(Direct(11, 10, 1));
    policy.EvaluateHit(Direct(11, 10, 1));
    Check(policy.BashActive && policy.Durability == 1, "half shield remains combat capable");
    policy.EvaluateHit(Direct(11, 10, 1));
    Check(!policy.BashActive && !policy.TryRegisterBashTarget(50, true),
        "shield break closes an active bash");
}

static void ResetOnLoadAndReuse()
{
    var policy = Ready();
    Check(policy.TryBeginBash(0) && policy.TryRegisterBashTarget(1001, true), "old life entered combat");
    policy.EvaluateHit(Direct(11, 10, 1));
    policy.Reset(); // Both load reconstruction and pool reuse must pass this boundary.
    Check(policy.State == HeavyShieldBlockPolicy.LifeState.Inactive && policy.Durability == 0,
        "unbound instance cannot retain a paid role");
    Check(!policy.TryRegisterBashTarget(1002, true) && !policy.TryBeginDemote(out _),
        "old impact and conversion callbacks have no state to consume");
    Check(policy.Activate() && policy.Durability == 4, "new purchase starts with fresh shield");
    Check(policy.TryBeginBash(0) && policy.TryRegisterBashTarget(1001, true),
        "new life has no old cooldown or target cache");
    for (var i = 0; i < 4; i++) policy.EvaluateHit(Direct(11, 10, 1));
    Check(policy.TryBeginDemote(out var oldLease), "old life can hold a conversion lease");
    policy.Reset();
    Check(policy.Activate(), "new pooled life activates");
    for (var i = 0; i < 4; i++) policy.EvaluateHit(Direct(11, 10, 1));
    Check(policy.TryBeginDemote(out var newLease) && newLease != oldLease,
        "pooled life uses a new conversion lease");
    Check(!policy.ConfirmDemote(oldLease) && policy.AbortDemoteUnchanged(newLease),
        "stale completion cannot convert pooled life");
    policy.MarkDead();
    policy.Reset();
    Check(policy.Activate(), "dead pooled object can be bound to another life");
}

static void Check(bool condition, string explanation)
{
    if (!condition) throw new Exception(explanation);
}
