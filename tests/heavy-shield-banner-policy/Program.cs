using KingdomEnhancedMod;

var cases = new (string Name, Action Run)[]
{
    ("only the nearest eligible soldier on the raised side is selected", NearestSameSide),
    ("selection uses this activation's flag position", CurrentFlagPosition),
    ("equal distance resolves by receipt regardless of input order", StableTie),
    ("opposite side and repairing soldiers leave an empty escort", NoEligibleSoldier),
    ("invalid side and flag coordinates cannot select", InvalidSelectionContext),
    ("invalid candidate identities and coordinates cannot select", InvalidCandidates),
    ("selection rejects oversized and ambiguous duplicate rosters", InvalidRoster),
    ("finite extreme coordinates retain nearest-distance ordering", ExtremeCoordinates),
    ("selection does not mutate the supplied roster", RosterPreserved),
    ("binding accepts exactly the selected life and activation", ValidBinding),
    ("repeated and stale activations cannot replace an escort", RepeatedActivation),
    ("same formation can start a newer activation", NewActivation),
    ("empty activation is consumed and cannot refill later", EmptyActivation),
    ("new invalid candidate ends the prior escort and consumes activation", InvalidBindingCandidate),
    ("invalid outer context does not consume an activation", InvalidBindingContext),
    ("ended or lost escort cannot be replaced in the same activation", NoRefillAfterEnd),
    ("inactive and every context mismatch permanently end the binding", ContextMismatchEnds),
    ("invalid current identity and context never revive an escort", InvalidCurrentContext),
    ("owner teardown reset permits a new world token sequence", OwnerReset),
    ("maximum activation cannot wrap or replay", ActivationLimit),
};

var failed = 0;
foreach (var test in cases)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failed++; Console.WriteLine($"FAIL {test.Name}: {error.Message}"); }
}
Console.WriteLine($"{cases.Length - failed}/{cases.Length} banner policy scenarios passed; {Checks.Count} assertions; {failed} failed");
return failed == 0 ? 0 : 1;

static HeavyShieldBannerCandidate Soldier(int id, int side = 1, float x = 0f, long life = 1, bool eligible = true) =>
    new(new Guid(id, 0, 0, new byte[8]), life, side, x, eligible);

static void Check(bool actual, string message) => Checks.That(actual, message);

static void Selects(HeavyShieldBannerCandidate[] roster, int side, float flagX, HeavyShieldBannerCandidate expected)
{
    Check(HeavyShieldBannerSelection.TrySelect(roster, side, flagX, out var chosen), "must choose an escort");
    Check(chosen == expected, $"expected receipt {expected.Receipt}, got {chosen.Receipt}");
}

static void Rejects(IReadOnlyList<HeavyShieldBannerCandidate> roster, int side, float flagX)
{
    Check(!HeavyShieldBannerSelection.TrySelect(roster, side, flagX, out var chosen), "selection must fail");
    Check(chosen == default, "failed selection must clear output");
}

static void NearestSameSide()
{
    var near = Soldier(2, x: 8);
    var far = Soldier(1, x: 3);
    var opposite = Soldier(3, side: -1, x: 10);
    var repairing = Soldier(4, x: 10, eligible: false);
    Selects([far, opposite, repairing, near], 1, 10, near);
    Selects([near, far, opposite, repairing], -1, 10, opposite);
}

static void CurrentFlagPosition()
{
    var left = Soldier(1, x: -20);
    var right = Soldier(2, x: 20);
    Selects([left, right], 1, -15, left);
    Selects([left, right], 1, 15, right);
}

static void StableTie()
{
    // Guid.CompareTo compares the first field as unsigned; this fixture catches signed/string shortcuts.
    var high = new HeavyShieldBannerCandidate(new Guid("ffffffff-0000-0000-0000-000000000000"), 1, 1, 7, true);
    var low = new HeavyShieldBannerCandidate(new Guid("00000001-0000-0000-0000-000000000000"), 1, 1, 13, true);
    Selects([high, low], 1, 10, low);
    Selects([low, high], 1, 10, low);
}

static void NoEligibleSoldier()
{
    Rejects([], 1, 0);
    Rejects([Soldier(1, side: -1)], 1, 0);
    Rejects([Soldier(1, eligible: false), Soldier(2, eligible: false)], 1, 0);
}

static void InvalidSelectionContext()
{
    var roster = new[] { Soldier(1) };
    foreach (var side in new[] { 0, -2, 2, int.MinValue, int.MaxValue }) Rejects(roster, side, 0);
    foreach (var x in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity }) Rejects(roster, 1, x);
}

static HeavyShieldBannerCandidate[] BadCandidates() =>
[
    default,
    Soldier(1) with { Receipt = Guid.Empty },
    Soldier(1, life: 0), Soldier(1, life: -1),
    Soldier(1, side: 0), Soldier(1, side: 2), Soldier(1, side: -2),
    Soldier(1, x: float.NaN), Soldier(1, x: float.PositiveInfinity), Soldier(1, x: float.NegativeInfinity),
    Soldier(1, eligible: false),
];

static void InvalidCandidates()
{
    foreach (var bad in BadCandidates())
    {
        Rejects([bad], 1, 0);
        Selects([bad, Soldier(5)], 1, 0, Soldier(5));
    }
}

static void InvalidRoster()
{
    Rejects(null!, 1, 0);
    Rejects([Soldier(1), Soldier(2), Soldier(3), Soldier(4), Soldier(5)], 1, 0);
    Rejects([Soldier(1), Soldier(1)], 1, 0);
    Rejects([Soldier(1), Soldier(1, life: 2)], 1, 0);
    Rejects([Soldier(1), Soldier(1, side: -1, eligible: false)], 1, 0);
}

static void ExtremeCoordinates()
{
    var farther = Soldier(1, x: -float.MaxValue);
    var closer = Soldier(2, x: -float.MaxValue / 2);
    Selects([farther, closer], 1, float.MaxValue, closer);
}

static void RosterPreserved()
{
    var roster = new[] { Soldier(4, x: 10), Soldier(1, x: 2), Soldier(3, side: -1), Soldier(2, eligible: false) };
    var before = (HeavyShieldBannerCandidate[])roster.Clone();
    Selects(roster, 1, 0, roster[1]);
    Check(roster.SequenceEqual(before), "selector must not reorder or rewrite caller roster");
}

static HeavyShieldBannerBinding Bound(long activation = 10)
{
    var binding = new HeavyShieldBannerBinding();
    Check(binding.TryBind(activation, 20, 30, 1, Soldier(1)), "first legal binding succeeds");
    return binding;
}

static bool Current(HeavyShieldBannerBinding binding, long activation = 10, long formation = 20, long world = 30,
    int side = 1, bool active = true, int receipt = 1, long life = 1) =>
    binding.IsCurrent(activation, formation, world, side, active, Soldier(receipt).Receipt, life);

static void ValidBinding()
{
    var binding = Bound();
    Check(Current(binding), "exact active tuple is current");
    Check(Current(binding), "reading valid current tuple is idempotent");
    var left = new HeavyShieldBannerBinding();
    Check(left.TryBind(1, 1, 1, -1, Soldier(2, side: -1)), "left-side binding accepted");
    Check(left.IsCurrent(1, 1, 1, -1, true, Soldier(2).Receipt, 1), "left tuple is current");
}

static void RepeatedActivation()
{
    var binding = Bound();
    Check(!binding.TryBind(10, 20, 30, 1, Soldier(1)), "same activation cannot be replayed");
    Check(!binding.TryBind(10, 21, 30, 1, Soldier(2)), "formation replacement cannot replay token");
    Check(!binding.TryBind(9, 20, 30, 1, Soldier(2)), "stale token cannot replace current escort");
    Check(Current(binding), "rejected activation cannot disturb legal current binding");
}

static void NewActivation()
{
    var binding = Bound();
    Check(binding.TryBind(11, 20, 30, 1, Soldier(2)), "same formation accepts newer activation");
    Check(Current(binding, activation: 11, receipt: 2), "new choice is bound");
    Check(!binding.TryBind(10, 20, 30, 1, Soldier(1)), "old choice cannot replace newer choice");
    Check(Current(binding, activation: 11, receipt: 2), "newer choice survives rejected old bind");
}

static void EmptyActivation()
{
    var binding = new HeavyShieldBannerBinding();
    Check(!binding.TryBind(10, 20, 30, 1, default), "empty activation binds nobody");
    Check(!binding.TryBind(10, 20, 30, 1, Soldier(1)), "late soldier cannot fill consumed empty activation");
    Check(!Current(binding), "empty activation has no current soldier");
    Check(binding.TryBind(11, 20, 30, 1, Soldier(1)), "later activation can choose a soldier");
    Check(Current(binding, activation: 11), "new activation is current");
}

static void InvalidBindingCandidate()
{
    foreach (var bad in BadCandidates().Append(Soldier(1, side: -1)))
    {
        var binding = Bound();
        Check(!binding.TryBind(11, 20, 30, 1, bad), "invalid candidate leaves new activation empty");
        Check(!Current(binding), "new activation ended prior escort");
        Check(!binding.TryBind(11, 20, 30, 1, Soldier(2)), "invalid-candidate activation cannot refill");
        Check(binding.TryBind(12, 20, 30, 1, Soldier(2)), "next activation remains usable");
    }
}

static void InvalidBindingContext()
{
    var invalid = new (long Activation, long Formation, long World, int Side)[]
    {
        (0,20,30,1), (-1,20,30,1), (11,0,30,1), (11,-1,30,1),
        (11,20,0,1), (11,20,-1,1), (11,20,30,0), (11,20,30,2), (11,20,30,-2),
    };
    foreach (var context in invalid)
    {
        var binding = Bound();
        Check(!binding.TryBind(context.Activation, context.Formation, context.World, context.Side, Soldier(2)), "invalid outer context refused");
        Check(Current(binding), "invalid outer request cannot disturb legal binding");
        Check(binding.TryBind(11, 20, 30, 1, Soldier(2)), "invalid outer context did not consume newer token");
    }
}

static void NoRefillAfterEnd()
{
    var binding = Bound();
    binding.End();
    binding.End();
    Check(!Current(binding), "End clears bound escort");
    Check(!binding.TryBind(10, 20, 30, 1, Soldier(2)), "same activation cannot replace lost escort");
    Check(binding.TryBind(11, 20, 30, 1, Soldier(2)), "later activation can recruit again");
}

static void ContextMismatchEnds()
{
    var mismatches = new Func<HeavyShieldBannerBinding, bool>[]
    {
        b => Current(b, active: false), b => Current(b, activation: 9), b => Current(b, activation: 11),
        b => Current(b, formation: 21), b => Current(b, world: 31), b => Current(b, side: -1),
        b => Current(b, receipt: 2), b => Current(b, life: 2),
    };
    foreach (var mismatch in mismatches)
    {
        var binding = Bound();
        Check(!mismatch(binding), "mismatch is not current");
        Check(!Current(binding), "returning to old facts cannot revive terminated escort");
        Check(!binding.TryBind(10, 20, 30, 1, Soldier(2)), "terminated activation cannot refill");
    }
}

static void InvalidCurrentContext()
{
    var invalid = new Func<HeavyShieldBannerBinding, bool>[]
    {
        b => Current(b, activation: 0), b => Current(b, formation: 0), b => Current(b, world: 0),
        b => Current(b, side: 0), b => Current(b, side: 2), b => Current(b, life: 0), b => Current(b, life: -1),
        b => b.IsCurrent(10, 20, 30, 1, true, Guid.Empty, 1),
    };
    foreach (var observation in invalid)
    {
        var binding = Bound();
        Check(!observation(binding), "invalid observation refused");
        Check(!Current(binding), "invalid observation ended binding");
    }
    Check(!Current(new HeavyShieldBannerBinding()), "unbound instance cannot invent an escort");
}

static void OwnerReset()
{
    var binding = Bound();
    binding.Reset();
    Check(!Current(binding), "teardown clears binding");
    Check(binding.TryBind(1, 2, 3, -1, Soldier(4, side: -1, life: 5)), "new owner may restart tokens in new world");
    Check(binding.IsCurrent(1, 2, 3, -1, true, Soldier(4).Receipt, 5), "new world binding current");
}

static void ActivationLimit()
{
    var binding = Bound(long.MaxValue);
    Check(!binding.TryBind(long.MinValue, 20, 30, 1, Soldier(2)), "overflowed token refused");
    Check(!binding.TryBind(1, 20, 30, 1, Soldier(2)), "wrapped token refused");
    Check(Current(binding, activation: long.MaxValue), "refused tokens preserve last current binding");
    binding.End();
    Check(!binding.TryBind(long.MaxValue, 20, 30, 1, Soldier(2)), "max token cannot replay after End");
}

internal static class Checks
{
    internal static int Count { get; private set; }
    internal static void That(bool value, string message)
    {
        Count++;
        if (!value) throw new InvalidOperationException(message);
    }
}
