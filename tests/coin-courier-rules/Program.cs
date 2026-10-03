// Pure rule suite: links the real il2cpp/CoinCourierRules.cs and never mirrors its formulas.
// CoinCourierTargeting/CoinCourierBankScope need game + Unity types, so they stay outside this
// offline suite; the real 2.4 interop build and static scope review cover them.
using KingdomEnhancedMod;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static CoinCourierRules.KnightSnapshot Knight(
    long life, int side, int coins, int capacity,
    bool alive = true, bool sameWorld = true, bool inTransit = false, float next = 0f)
    => new(life, side, coins, capacity, alive, sameWorld, inTransit, next);

int checks = 0;
void Verify(bool condition, string message) { Check(condition, message); checks++; }

// ---- original selection/bounds scenarios (explicit four-coin quota) ----
var roster = new[]
{
    Knight(11, -1, 7, 8),
    Knight(12, 1, 2, 8),
    Knight(13, -1, 5, 12),
};
Verify(CoinCourierRules.TryPlan(roster, 100, 20f, -1, 4, out var plan), "a depleted Knight is selected");
Verify(plan.LifeId == 12 && plan.CoinsToSend == 4, "lowest fill ratio gets a four-coin visit");

roster = new[] { Knight(21, -1, 4, 8), Knight(22, 1, 4, 8) };
Verify(CoinCourierRules.TryPlan(roster, 12, 20f, -1, 4, out plan) && plan.LifeId == 22,
    "an equal deficit visits the opposite side");
Verify(CoinCourierRules.TryPlan(roster, 12, 20f, 1, 4, out plan) && plan.LifeId == 21,
    "the next equal deficit alternates back");

roster = new[] { Knight(31, -1, 0, 8, next: 25f), Knight(32, 1, 6, 8) };
Verify(CoinCourierRules.TryPlan(roster, 12, 20f, 0, 4, out plan) && plan.LifeId == 32,
    "a courier does not immediately revisit a cooldown Knight");
Verify(CoinCourierRules.TryPlan(roster, 12, 25f, 0, 4, out plan) && plan.LifeId == 31,
    "cooldown expiry restores eligibility");

roster = new[]
{
    Knight(41, -1, 0, 8, alive: false),
    Knight(42, 1, 0, 8, sameWorld: false),
    Knight(43, -1, 0, 8, inTransit: true),
    Knight(44, 0, 0, 8),
    Knight(45, 1, 8, 8),
};
Verify(!CoinCourierRules.TryPlan(roster, 12, 20f, 0, 4, out _),
    "dead, foreign, traveling, side-less and full Knights are never selected");
Verify(!CoinCourierRules.TryPlan(new[] { Knight(50, -1, 0, 8) }, 0, 20f, 0, 4, out _),
    "an empty purse cannot dispatch a visit");
Verify(CoinCourierRules.TryPlan(new[] { Knight(51, -1, 0, 8) }, 2, 20f, 0, 4, out plan)
    && plan.CoinsToSend == 2, "purse balance bounds the visit");
Verify(CoinCourierRules.TryPlan(new[] { Knight(52, -1, 7, 8) }, 20, 20f, 0, 4, out plan)
    && plan.CoinsToSend == 1, "remaining capacity bounds the visit");

var pending = new CoinCourierRules.VisitPlan(61, 1, 4);
Verify(CoinCourierRules.CanSendNextCoin(pending, 61, 3, 8, 10, 0),
    "a valid life may receive the next coin");
Verify(!CoinCourierRules.CanSendNextCoin(pending, 62, 3, 8, 10, 0),
    "a recycled or replaced Knight life cannot receive a stale visit");
Verify(!CoinCourierRules.CanSendNextCoin(pending, 61, 8, 8, 10, 0),
    "capacity becoming full between coins stops transfer");
Verify(!CoinCourierRules.CanSendNextCoin(pending, 61, 3, 8, 0, 0),
    "purse exhaustion between coins stops transfer");
Verify(!CoinCourierRules.CanSendNextCoin(pending, 61, 3, 8, 10, 4),
    "a visit cannot exceed its reserved four-coin ceiling");

roster = new[] { Knight(71, -1, int.MaxValue - 1, int.MaxValue), Knight(72, 1, 1, int.MaxValue) };
Verify(CoinCourierRules.TryPlan(roster, int.MaxValue, 20f, 0, 4, out plan) && plan.LifeId == 72,
    "large wallet capacities do not overflow fill-ratio ordering");

// ---- caller-supplied per-visit quota ----
roster = new[] { Knight(81, -1, 0, 8) };
Verify(CoinCourierRules.TryPlan(roster, 100, 20f, 0, 1, out plan) && plan.CoinsToSend == 1,
    "a one-coin quota bounds the visit");
Verify(CoinCourierRules.TryPlan(roster, 100, 20f, 0, 2, out plan) && plan.CoinsToSend == 2,
    "a two-coin quota bounds the visit");
Verify(CoinCourierRules.TryPlan(roster, 100, 20f, 0, 7, out plan) && plan.CoinsToSend == 7,
    "a seven-coin quota bounds the visit");
Verify(!CoinCourierRules.TryPlan(roster, 100, 20f, 0, 0, out _), "a zero quota rejects the plan");
Verify(!CoinCourierRules.TryPlan(roster, 100, 20f, 0, -3, out _), "a negative quota rejects the plan");
Verify(CoinCourierRules.TryPlan(new[] { Knight(82, -1, 5, 8) }, 100, 20f, 0, 7, out plan)
    && plan.CoinsToSend == 3, "wallet shortage bounds a larger quota");
Verify(CoinCourierRules.TryPlan(roster, 5, 20f, 0, 7, out plan) && plan.CoinsToSend == 5,
    "purse balance bounds a larger quota");
Verify(CoinCourierRules.TryPlan(new[] { Knight(83, -1, 0, int.MaxValue) }, int.MaxValue, 20f, 0, int.MaxValue, out plan)
    && plan.CoinsToSend == int.MaxValue, "maximal quota, deficit and purse do not overflow");

// ---- caller-supplied cooldown deadline ----
Verify(CoinCourierRules.TryNextEligibleAt(100f, 0f, out var readyAt) && readyAt == 100f,
    "a zero cooldown yields the current time");
Verify(CoinCourierRules.TryNextEligibleAt(20f, 15f, out readyAt) && readyAt == 35f,
    "a fifteen second cooldown yields now plus fifteen");
Verify(CoinCourierRules.TryNextEligibleAt(100f, 30f, out readyAt) && readyAt == 130f,
    "a thirty second cooldown yields now plus thirty");
Verify(CoinCourierRules.TryNextEligibleAt(10f, CoinCourierRules.KnightCooldownSeconds, out readyAt)
    && readyAt == 25f, "the retained fifteen second default yields now plus fifteen");
Verify(!CoinCourierRules.TryNextEligibleAt(100f, -1f, out _), "a negative cooldown is rejected");
Verify(!CoinCourierRules.TryNextEligibleAt(100f, float.NaN, out _)
    && !CoinCourierRules.TryNextEligibleAt(float.NaN, 15f, out _), "NaN clock or cooldown is rejected");
Verify(!CoinCourierRules.TryNextEligibleAt(100f, float.PositiveInfinity, out _)
    && !CoinCourierRules.TryNextEligibleAt(float.PositiveInfinity, 15f, out _)
    && !CoinCourierRules.TryNextEligibleAt(100f, float.NegativeInfinity, out _)
    && !CoinCourierRules.TryNextEligibleAt(float.NegativeInfinity, 15f, out _),
    "infinite clock or cooldown is rejected");
Verify(!CoinCourierRules.TryNextEligibleAt(float.MaxValue, float.MaxValue, out _)
    && !CoinCourierRules.TryNextEligibleAt(float.MaxValue, 1e30f, out _),
    "a deadline beyond the float range is rejected");
Verify(CoinCourierRules.TryNextEligibleAt(float.MaxValue, 0f, out readyAt) && readyAt == float.MaxValue,
    "a zero cooldown at the float ceiling still fits");

roster = new[] { Knight(91, -1, 0, 8, next: 35f) };
Verify(!CoinCourierRules.TryPlan(roster, 12, 34.99f, 0, 4, out _),
    "a Knight stays cooling down before the caller-computed deadline");
Verify(CoinCourierRules.TryPlan(roster, 12, 35f, 0, 4, out plan) && plan.LifeId == 91,
    "the caller-computed deadline restores eligibility");

// ---- non-finite and retained rejection gates ----
Verify(!CoinCourierRules.TryPlan(new[] { Knight(92, -1, 0, 8) }, 12, float.NaN, 0, 4, out _),
    "NaN now is rejected");
Verify(!CoinCourierRules.TryPlan(new[] { Knight(92, -1, 0, 8) }, 12, float.PositiveInfinity, 0, 4, out _),
    "infinite now cannot bypass the readiness gate");
Verify(!CoinCourierRules.TryPlan(new[] { Knight(93, -1, 0, 8, next: float.NegativeInfinity) }, 12, 20f, 0, 4, out _),
    "a negative-infinite readyAt cannot bypass the readiness gate");
Verify(!CoinCourierRules.TryPlan(new[] { Knight(94, -1, 0, 8, next: float.PositiveInfinity) }, 12, 20f, 0, 4, out _),
    "an infinite readyAt never becomes eligible");
Verify(!CoinCourierRules.TryPlan(new[] { Knight(95, -1, -1, 8) }, 12, 20f, 0, 4, out _),
    "a negative wallet balance is rejected");
Verify(!CoinCourierRules.TryPlan(new[] { Knight(96, -1, 0, 0) }, 12, 20f, 0, 4, out _),
    "a zero wallet capacity is rejected");
Verify(!CoinCourierRules.TryPlan(new[] { Knight(97, -1, 0, 8) }, -5, 20f, 0, 4, out _),
    "a negative purse balance cannot dispatch a visit");

Console.WriteLine($"PASS coin courier rules: {checks} scenarios");
