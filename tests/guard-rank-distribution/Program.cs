using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

int passed = 0;

void Check(bool ok, string name)
{
    if (!ok) throw new Exception("FAIL: " + name);
    passed++;
}

var kingdom = new Kingdom();
var rivals = new Kingdom();

void WorldContext()
{
    Managers.Inst = new Managers
    {
        kingdom = kingdom,
        world = new World { gameLayer = new Transform { gameObject = new GameObject() } }
    };
    ModConfig.Enabled.Value = true;
    NetworkBigBoss.IsOnline = false;
    NetworkBigBoss.HasWorldAuth = true;
}

void Reset()
{
    kingdom._availableArchersCache = new List<Archer>();
    WorldContext();
}

Archer Unit()
{
    var archer = new Archer();
    new GameObject().Add(archer);
    kingdom._availableArchersCache.Add(archer);
    return archer;
}

// ---- 2.4 native distribution fixture ----------------------------------------
// Fixture only: GuardRankDistribution (production) is what runs under test. The arithmetic
// mirrors the measured native loop (Kingdom.DistributeFreeArchers RVA 0x59D0B1-0x59D1D9):
//   total  = free + leftFollowers + rightFollowers
//   quota  = max((total - leftFollowers + rightFollowers) / 2, 0)
//   right counter starts at free - quota and decrements; left counter starts at 0 and increments
//   item 0 forced Left, last item forced Right, middles Left while (leftFollowers + leftSoFar) < quota
List<Archer> NativePass(int free, int leftFollowers, int rightFollowers)
{
    kingdom._availableArchersCache = new List<Archer>();
    var units = new List<Archer>();
    for (int i = 0; i < free; i++) units.Add(Unit());
    int total = free + leftFollowers + rightFollowers;
    int quota = Math.Max((total - leftFollowers + rightFollowers) / 2, 0);
    int leftCounter = 0, rightCounter = free - quota, leftSoFar = leftFollowers;
    for (int i = 0; i < free; i++)
    {
        bool goLeft = i == 0 || (i != free - 1 && leftSoFar < quota);
        if (goLeft)
        {
            units[i]._guardSide = Side.Left;
            units[i]._guardDepth = leftCounter++;
            leftSoFar++;
        }
        else
        {
            units[i]._guardSide = Side.Right;
            units[i]._guardDepth = --rightCounter;
        }
    }
    return units;
}

int TotalWrites(IEnumerable<Archer> units) => units.Sum(u => u.Writes.Count);

bool Ranks(IEnumerable<Archer> units, params int[] expected)
    => units.Select(u => u._guardDepth).SequenceEqual(expected);

bool Contiguous(IEnumerable<Archer> units)
{
    var depths = units.Select(u => u._guardDepth).OrderBy(d => d).ToList();
    for (int i = 0; i < depths.Count; i++) if (depths[i] != i) return false;
    return true;
}

bool SidesIntact(IEnumerable<Archer> left, IEnumerable<Archer> right)
    => left.All(u => u._guardSide == Side.Left) && right.All(u => u._guardSide == Side.Right);

void RunA() => GuardRankDistribution.ReindexAfterNative(kingdom);

var descending9 = new[] { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 };
var ascending9 = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

// ---- 0. per-call snapshot structure ------------------------------------------
// The batch buffers must be locals: no static field may retain an Archer/Entry reference past
// the call (issue-78 review). Log keys and the event counter are the only allowed cross-call
// state.
var entryType = typeof(GuardRankDistribution).GetNestedType("Entry", BindingFlags.NonPublic);
bool HoldsUnitReferences(Type type)
{
    if (type == typeof(Archer)) return true;
    if (entryType != null && type == entryType) return true;
    if (type.IsArray) return HoldsUnitReferences(type.GetElementType());
    if (type.IsGenericType) return type.GetGenericArguments().Any(HoldsUnitReferences);
    return false;
}
var staticUnitFields = typeof(GuardRankDistribution)
    .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
    .Where(f => HoldsUnitReferences(f.FieldType))
    .Select(f => f.Name)
    .ToList();
Check(staticUnitFields.Count == 0, "no static field retains Archer/Entry references: " + string.Join(",", staticUnitFields));
var probeFields = typeof(StaticSnapshotProbe)
    .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
    .Where(f => HoldsUnitReferences(f.FieldType))
    .Select(f => f.Name)
    .ToList();
Check(probeFields.Contains("Units"), "snapshot detector finds a static Archer list (self-check)");

// ---- 1. measured failure vectors (issue-78 evidence) ------------------------

Reset();
var v = NativePass(20, 4, 4);
var lefts = v.Take(10).ToList();
var rights = v.Skip(10).ToList();
Check(Ranks(rights, 5, 4, 3, 2, 1, 0, -1, -2, -3, -4), "fixture: lf4/rf4 native right ranks 5..-4");
Check(Ranks(lefts, ascending9), "fixture: lf4/rf4 native left ranks 0..9");
RunA();
Check(Ranks(rights, descending9), "lf4/rf4: right ranks corrected to 9..0");
Check(Ranks(lefts, ascending9), "lf4/rf4: left ranks stay 0..9");
Check(SidesIntact(lefts, rights), "lf4/rf4: native sides preserved");
Check(rights.All(u => u._guardDepth >= 0) && lefts.All(u => u._guardDepth >= 0), "lf4/rf4: no negative rank delivered");
Check(Contiguous(rights) && Contiguous(lefts), "lf4/rf4: each side contiguous from zero");
Check(TotalWrites(rights) == 10 && TotalWrites(lefts) == 0, "lf4/rf4: only the ten mismatching right ranks written");
Check(KingdomEnhancedPlugin.Instance.LogSource.Info.Any(m => m.Contains("corrected=10")), "lf4/rf4: bounded correction event logged");
RunA();
Check(TotalWrites(v) == 10, "lf4/rf4 repeat pass on an A-consistent batch: zero writes");

Reset();
v = NativePass(20, 40, 40);
lefts = v.Take(10).ToList();
rights = v.Skip(10).ToList();
Check(Ranks(rights, new[] { -31, -32, -33, -34, -35, -36, -37, -38, -39, -40 }), "fixture: lf40/rf40 native right -31..-40");
RunA();
Check(Ranks(rights, descending9), "lf40/rf40: right ranks corrected to 9..0");
Check(SidesIntact(lefts, rights), "lf40/rf40: native sides preserved");
Check(rights.All(u => u._guardDepth >= 0), "lf40/rf40: no negative rank delivered");
Check(TotalWrites(rights) == 10 && TotalWrites(lefts) == 0, "lf40/rf40: only the ten mismatching right ranks written");

// Minimal reproduction: free4/lf1/rf1 -> native right 0..-1 (last forced Right), repaired to 1..0.
Reset();
v = NativePass(4, 1, 1);
lefts = v.Take(2).ToList();
rights = v.Skip(2).ToList();
Check(Ranks(rights, 0, -1), "fixture: free4/lf1/rf1 native right ranks 0..-1");
RunA();
Check(Ranks(rights, 1, 0), "free4/lf1/rf1: right ranks corrected to 1..0");
Check(Ranks(lefts, 0, 1) && SidesIntact(lefts, rights), "free4/lf1/rf1: left ranks and sides untouched");
Check(TotalWrites(rights) == 2 && TotalWrites(lefts) == 0, "free4/lf1/rf1: exactly the two right ranks written");

// No-negative counterexample: the polluted quota still splits 18/2 and the right counter stays
// non-negative, so a healthy batch must pass through with zero writes and untouched sides.
Reset();
v = NativePass(20, 0, 8);
lefts = v.Take(18).ToList();
rights = v.Skip(18).ToList();
Check(Ranks(rights, 1, 0), "fixture: lf0/rf8 native right ranks 1..0 (non-negative)");
RunA();
Check(TotalWrites(v) == 0, "lf0/rf8: healthy batch writes nothing");
Check(Ranks(lefts, Enumerable.Range(0, 18).ToArray()), "lf0/rf8: left ranks stay 0..17");
Check(SidesIntact(lefts, rights), "lf0/rf8: 18/2 native split preserved");

Reset();
v = NativePass(20, 0, 0);
lefts = v.Take(10).ToList();
rights = v.Skip(10).ToList();
Check(Ranks(rights, descending9), "fixture: no-follower native right ranks 9..0");
RunA();
Check(TotalWrites(v) == 0, "no followers: healthy batch writes nothing");
Check(SidesIntact(lefts, rights), "no followers: 10/10 native split preserved");

// ---- 2. small unit counts and forced first/last -----------------------------

foreach (int n in new[] { 0, 1, 2, 8, 9, 19 })
{
    Reset();
    v = NativePass(n, 0, 0);
    lefts = v.Where(u => u._guardSide == Side.Left).ToList();
    rights = v.Where(u => u._guardSide == Side.Right).ToList();
    if (n >= 2)
        Check(v[0]._guardSide == Side.Left && v[n - 1]._guardSide == Side.Right, $"free{n}: first forced Left, last forced Right");
    RunA();
    Check(TotalWrites(v) == 0, $"free{n}: healthy batch writes nothing");
    Check(SidesIntact(lefts, rights), $"free{n}: sides preserved");
    Check(Contiguous(lefts) && Contiguous(rights), $"free{n}: both sides contiguous");
    if (n == 9) Check(lefts.Count == 4 && rights.Count == 5, "free9: native parity 4/5");
    if (n == 8) Check(lefts.Count == 4 && rights.Count == 4, "free8: native parity 4/4");
}

// ---- 3. cache order, ties and interleaved sides ------------------------------

Reset();
var e1 = Unit(); var e2 = Unit(); var e3 = Unit(); var e4 = Unit(); var e5 = Unit();
e1._guardSide = Side.Right; e1._guardDepth = 9;
e2._guardSide = Side.Left; e2._guardDepth = 4;
e3._guardSide = Side.Right; e3._guardDepth = 9;
e4._guardSide = Side.Left; e4._guardDepth = 4;
e5._guardSide = Side.Right; e5._guardDepth = 9;
RunA();
Check(e1._guardDepth == 2 && e3._guardDepth == 1 && e5._guardDepth == 0, "interleaved: right ranks R-1..0 in cache order");
Check(e2._guardDepth == 0 && e4._guardDepth == 1, "interleaved: left ranks 0..L-1 in cache order");
Check(e1._guardSide == Side.Right && e2._guardSide == Side.Left && e3._guardSide == Side.Right
    && e4._guardSide == Side.Left && e5._guardSide == Side.Right, "interleaved: no side changed");
Check(TotalWrites(new[] { e1, e2, e3, e4, e5 }) == 5 && e1.Writes.Single().Side == Side.Right,
    "interleaved: writes keep the item's native side");

Reset();
var u1 = Unit(); u1.transform.position = new Vector3 { x = 9f };
u1._guardSide = Side.Right; u1._guardDepth = 0;
var u2 = Unit(); u2.transform.position = new Vector3 { x = 1f };
u2._guardSide = Side.Right; u2._guardDepth = 0;
RunA();
Check(u1._guardDepth == 1 && u2._guardDepth == 0, "unsorted x: cache order wins over transform.x");

Reset();
var safeRight = new List<Archer>();
for (int i = 0; i < 4; i++) { var s = Unit(); s._guardSide = Side.Left; s._guardDepth = 7; }
for (int i = 0; i < 16; i++) { var s = Unit(); s._guardSide = Side.Right; s._guardDepth = 7; safeRight.Add(s); }
RunA();
Check(Ranks(safeRight, Enumerable.Range(0, 16).Reverse().ToArray()), "safe-side shape: right ranks 15..0");
Check(Contiguous(safeRight) && safeRight.All(u => u._guardSide == Side.Right), "safe-side shape: sides and ranks intact");

Reset();
var safeLeft = new List<Archer>();
safeRight = new List<Archer>();
for (int i = 0; i < 4; i++) { var s = Unit(); s._guardSide = Side.Left; s._guardDepth = i; safeLeft.Add(s); }
for (int i = 0; i < 16; i++) { var s = Unit(); s._guardSide = Side.Right; s._guardDepth = 15 - i; safeRight.Add(s); }
RunA();
Check(TotalWrites(safeLeft) + TotalWrites(safeRight) == 0, "safe-side shape: a first batch already healthy writes nothing");

// ---- 4. world gates (refuse without any write) ------------------------------

void Gate(string name, Action mutate, Action restore)
{
    Reset();
    var g = new List<Archer>();
    for (int i = 0; i < 3; i++) { var x = Unit(); x._guardSide = Side.Right; x._guardDepth = 9; g.Add(x); }
    mutate();
    RunA();
    Check(TotalWrites(g) == 0, name + ": refused");
    Check(g.All(x => x._guardDepth == 9), name + ": no partial write");
    restore();
}

Gate("config off", () => ModConfig.Enabled.Value = false, () => ModConfig.Enabled.Value = true);
Gate("online", () => NetworkBigBoss.IsOnline = true, () => NetworkBigBoss.IsOnline = false);
Gate("no world authority", () => NetworkBigBoss.HasWorldAuth = false, () => NetworkBigBoss.HasWorldAuth = true);
Gate("no managers", () => Managers.Inst = null, WorldContext);
Gate("other kingdom", () => Managers.Inst.kingdom = rivals, () => Managers.Inst.kingdom = kingdom);
Gate("no world", () => Managers.Inst.world = null, WorldContext);
Gate("no game layer", () => Managers.Inst.world.gameLayer = null, WorldContext);

bool nullTolerated = true;
try { GuardRankDistribution.ReindexAfterNative(null); } catch { nullTolerated = false; }
Check(nullTolerated, "null kingdom never throws");

// ---- 5. broken batches are refused whole (no half write) ---------------------

Reset();
var b = new List<Archer>();
for (int i = 0; i < 3; i++) { var x = Unit(); x._guardSide = Side.Right; x._guardDepth = 9; b.Add(x); }
kingdom._availableArchersCache.Insert(1, null);
RunA();
Check(TotalWrites(b) == 0 && b.All(x => x._guardDepth == 9), "null item: batch refused without partial writes");

Reset();
var dup = Unit(); dup._guardSide = Side.Right; dup._guardDepth = 9;
kingdom._availableArchersCache.Add(dup);
RunA();
Check(dup.Writes.Count == 0 && dup._guardDepth == 9, "duplicate object: batch refused");

Reset();
b = new List<Archer>();
for (int i = 0; i < 3; i++) { var x = Unit(); x._guardSide = Side.Right; x._guardDepth = 9; b.Add(x); }
b[1].isAvailable = false;
RunA();
Check(TotalWrites(b) == 0 && b.All(x => x._guardDepth == 9), "native unavailable item: batch refused without partial writes");

Reset();
b = new List<Archer>();
for (int i = 0; i < 3; i++) { var x = Unit(); x._guardSide = Side.Right; x._guardDepth = 9; b.Add(x); }
b[1]._guardSide = default;
int warnBefore = KingdomEnhancedPlugin.Instance.LogSource.Warn.Count;
RunA();
Check(TotalWrites(b) == 0 && b.All(x => x._guardDepth == 9), "invalid side: batch refused without partial writes");
Check(KingdomEnhancedPlugin.Instance.LogSource.Warn.Count == warnBefore + 1
    && KingdomEnhancedPlugin.Instance.LogSource.Warn.Last().Contains("invalid-side"), "invalid side: one bounded log line");
RunA();
Check(KingdomEnhancedPlugin.Instance.LogSource.Warn.Count == warnBefore + 1, "repeat refusal: logging stays bounded (deduplicated)");

Reset();
b = new List<Archer>();
for (int i = 0; i < 3; i++) { var x = Unit(); x._guardSide = Side.Right; x._guardDepth = 9; b.Add(x); }
b[1].ThrowOnAvailable = true;
bool readThrew = false;
try { RunA(); } catch { readThrew = true; }
Check(!readThrew, "isAvailable fault: boundary never throws");
Check(TotalWrites(b) == 0 && b.All(x => x._guardDepth == 9), "isAvailable fault: batch refused without partial writes");

Reset();
b = new List<Archer>();
for (int i = 0; i < 3; i++) { var x = Unit(); x._guardSide = Side.Right; x._guardDepth = 9; b.Add(x); }
b[1].gameObject = null;
RunA();
Check(TotalWrites(b) == 0 && b.All(x => x._guardDepth == 9), "missing gameObject: batch refused without partial writes");

Reset();
kingdom._availableArchersCache = null;
RunA();
Check(true, "missing native cache: tolerated");

// ---- 6. per-write isolation and honest partial diagnostics -------------------

Reset();
var w1 = Unit(); w1._guardSide = Side.Right; w1._guardDepth = 9;
var w2 = Unit(); w2._guardSide = Side.Right; w2._guardDepth = 9; w2.ThrowOnWrite = true;
var w3 = Unit(); w3._guardSide = Side.Right; w3._guardDepth = 9;
bool writeThrew = false;
try { RunA(); } catch { writeThrew = true; }
Check(!writeThrew, "setter fault: boundary never throws");
Check(w1._guardDepth == 2 && w3._guardDepth == 0, "setter fault: remaining items are still ranked");
Check(w2._guardDepth == 9 && w2.Writes.Count == 0, "setter fault before assignment: item unchanged in the fixture, no retry");
Check(KingdomEnhancedPlugin.Instance.LogSource.Warn.Any(m => m.Contains("may be partial") && m.Contains("state is unknown")),
    "setter fault: unknown-state warning logged");
Check(KingdomEnhancedPlugin.Instance.LogSource.Info.Any(m => m.Contains("failed=1") && m.Contains("partial") && m.Contains("failed state unknown")),
    "setter fault: bounded summary reports corrected/failed and partial");

// A fault after the assignment must not be reported as "kept the old value": the summary stays
// unknown for the failed item.
Reset();
var p1 = Unit(); p1._guardSide = Side.Right; p1._guardDepth = 9;
var p2 = Unit(); p2._guardSide = Side.Right; p2._guardDepth = 9; p2.ThrowAfterWrite = true;
int infoBefore = KingdomEnhancedPlugin.Instance.LogSource.Info.Count;
RunA();
Check(p1._guardDepth == 1 && p2._guardDepth == 0, "post-assign fault: fixture did assign before throwing");
Check(KingdomEnhancedPlugin.Instance.LogSource.Info.Skip(infoBefore).Any(m => m.Contains("failed=1") && m.Contains("failed state unknown")),
    "post-assign fault: summary still reports the failed state as unknown, never as kept");

// All writes failing must still produce the bounded summary (corrected = 0).
Reset();
var a1 = Unit(); a1._guardSide = Side.Right; a1._guardDepth = 9; a1.ThrowOnWrite = true;
var a2 = Unit(); a2._guardSide = Side.Right; a2._guardDepth = 9; a2.ThrowOnWrite = true;
int allFailBefore = KingdomEnhancedPlugin.Instance.LogSource.Info.Count;
RunA();
Check(a1._guardDepth == 9 && a2._guardDepth == 9 && TotalWrites(new[] { a1, a2 }) == 0, "all writes failing: items unchanged");
Check(KingdomEnhancedPlugin.Instance.LogSource.Info.Skip(allFailBefore).Any(m => m.Contains("corrected=0") && m.Contains("failed=2") && m.Contains("partial")),
    "all writes failing: bounded summary still emitted");

Console.WriteLine($"PASS {passed} assertions (production GuardRankDistribution; fixture models 2.4 native arithmetic)");
