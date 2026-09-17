using System;
using System.Collections.Generic;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

int passed = 0;

void Check(bool ok, string name)
{
    if (!ok) throw new Exception("FAIL: " + name);
    passed++;
}

// ---- fixture ---------------------------------------------------------------

var kingdom = new Kingdom();
var rivals = new Kingdom();
var managers = new Managers { kingdom = kingdom, enemies = new EnemyManager() };
Managers.Inst = managers;
MusketeerAccess.World = new Transform { gameObject = new GameObject() };
MusketeerAccess.Enabled = true;

Archer Musketeer(float x, Side prior = default)
{
    var archer = new Archer
    {
        _character = new Character(),
        _damageable = new Damageable(),
        _guardSide = prior
    };
    new GameObject().Add(archer);
    archer.transform.position = new Vector3 { x = x };
    MusketeerIdentity.Registered.Add(archer);
    kingdom._availableArchersCache.Add(archer);
    return archer;
}

// A non-musketeer archer: never in the identity registry, never written by the policy, but visible
// to the shared Archer census.
Archer OrdinaryArcher(float x, Side side, int depth)
{
    var archer = new Archer
    {
        _character = new Character(),
        _damageable = new Damageable(),
        _guardSide = side,
        _guardDepth = depth
    };
    new GameObject().Add(archer);
    archer.transform.position = new Vector3 { x = x };
    kingdom._availableArchersCache.Add(archer);
    return archer;
}

void Reset()
{
    MusketeerIdentity.Registered.Clear();
    MusketeerIdentity.NotUnits.Clear();
    MusketeerAccess.Foreign.Clear();
    MusketeerAccess.Enabled = true;
    UnitScanCache.Archers = Array.Empty<Archer>();
    UnitScanCache.Calls = 0;
    managers.kingdom = kingdom;
    kingdom.overrideGuard = false;
    kingdom.campaignOverride = false;
    kingdom.fallbackOverride = float.NaN;
    kingdom._availableArchersCache = new();
    kingdom.NativeDistribution = null;
    managers.enemies.safeLeft = false;
    managers.enemies.safeRight = false;
    kingdom.intactWall[Side.Left] = new GameObject();
    kingdom.intactWall[Side.Right] = new GameObject();
    Time.frameCount++;
    Time.unscaledTime += 1f;
    Probe.TotalWrites = 0;
}

var prefix = typeof(Kingdom_DistributeFreeArchers_MusketeerDefense_Patch)
    .GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
var postfix = typeof(Kingdom_DistributeFreeArchers_MusketeerDefense_Patch)
    .GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static);

bool Begin(Kingdom target)
{
    object[] args = { target, null };
    prefix.Invoke(null, args);
    return (bool)args[1];
}

void End(Kingdom target, bool owns)
{
    if (!owns) return;
    postfix.Invoke(null, new object[] { target, true });
}

// Fixture model: Begin = prefix (prior sides captured), simulateNative = what the native pass
// does between prefix and postfix (usually assigning a side to the fresh batch), End = postfix.
void Distribute(Kingdom target, Action simulateNative = null)
{
    bool owns = Begin(target);
    simulateNative?.Invoke();
    End(target, owns);
}

int WritesOn(params Archer[] archers)
{
    int total = 0;
    foreach (var archer in archers) total += archer.Writes.Count;
    return total;
}

void ClearWrites(params Archer[] archers)
{
    foreach (var archer in archers) archer.Writes.Clear();
}

int Canaries() => KingdomEnhancedPlugin.Instance.LogSource.Info.FindAll(m => m.Contains("canary")).Count;

int DestroyBaseline = UnityEngine.Object.DestroyCount;
int FindBaseline = UnityEngine.Object.FindCount;

// ---- 1. native all-left batch is split evenly (the reported case) ----------

Reset();
var a = Musketeer(10f, Side.Left);
var b = Musketeer(11f, Side.Left);
var c = Musketeer(12f, Side.Left);
var d = Musketeer(13f, Side.Left);
Distribute(kingdom);
Check(a._guardSide == Side.Left && b._guardSide == Side.Left, "four-left: leftmost pair stays left");
Check(c._guardSide == Side.Right && d._guardSide == Side.Right, "four-left: rightmost pair moves right");
Check(a.Writes.Count == 0 && b.Writes.Count == 0, "four-left: units that match their side are not rewritten");
Check(WritesOn(c, d) == 2, "four-left: exactly the two movers are written");
Check(c._guardDepth != d._guardDepth, "four-left: movers take distinct depths");
Check(c._guardDepth >= 0 && d._guardDepth <= 2 && d._guardDepth >= 0 && c._guardDepth <= 2, "four-left: movers take shallow free depths");

// ---- 2. repeat pass is stable ----------------------------------------------

ClearWrites(a, b, c, d);
Distribute(kingdom);
Check(WritesOn(a, b, c, d) == 0, "repeat: a balanced pass writes nothing");
Check(a._guardSide == Side.Left && b._guardSide == Side.Left
    && c._guardSide == Side.Right && d._guardSide == Side.Right, "repeat: assignment is stable");

// ---- 3. native re-flip is enforced through the recorded slot ---------------

ClearWrites(a, b, c, d);
int recorded = c._guardDepth;
bool owns = Begin(kingdom);                     // prior sides captured: L,L,R,R
c._guardSide = Side.Left;                       // native overwrites the decided side again
c._guardDepth = 0;
End(kingdom, owns);
Check(c._guardSide == Side.Right, "enforcement: flipped unit returns to its decided side");
Check(c._guardDepth == recorded, "enforcement: its recorded depth is reused");
Check(WritesOn(c) == 1 && WritesOn(a, b, d) == 0, "enforcement: only the flipped unit is written");

// ---- 4. two all-left -> one each -------------------------------------------

Reset();
var a2 = Musketeer(20f, Side.Left);
var b2 = Musketeer(21f, Side.Left);
Distribute(kingdom);
Check(a2._guardSide == Side.Left && b2._guardSide == Side.Right, "two-left: one unit per side");

// ---- 5. odd batch keeps the difference at one ------------------------------

Reset();
var five = new Archer[5];
for (int i = 0; i < 5; i++) five[i] = Musketeer(30f + i, Side.Left);
Distribute(kingdom);
int l = 0, r = 0;
foreach (var m in five) { if (m._guardSide == Side.Left) l++; else if (m._guardSide == Side.Right) r++; }
Check(l == 3 && r == 2, "odd five-left: counts differ by one (3/2)");
Check(five[3]._guardSide == Side.Right && five[4]._guardSide == Side.Right, "odd five-left: the pair nearest the wall moves");
Check(WritesOn(five) == 2, "odd five-left: exactly two writes");

// ---- 6. a new recruit fills the smaller side -------------------------------

Reset();
var l1 = Musketeer(40f, Side.Left);
var l2 = Musketeer(41f, Side.Left);
var r1 = Musketeer(42f, Side.Right);
var newcomer = Musketeer(43f, default);          // no prior side: freshly promoted career
Distribute(kingdom, () => newcomer._guardSide = Side.Left);   // native rank puts it left after the capture
Check(newcomer._guardSide == Side.Right, "newcomer: fills the smaller side");
Check(newcomer._guardDepth != r1._guardDepth, "newcomer: depth does not stack on the resident");
Check(WritesOn(newcomer) == 1 && WritesOn(l1, l2, r1) == 0, "newcomer: only the newcomer is written");
Check(l1._guardSide == Side.Left && l2._guardSide == Side.Left && r1._guardSide == Side.Right, "newcomer: existing sides unchanged");

// ---- 7. a neutral newcomer on a tie goes left (native single parity) -------

Reset();
var t1 = Musketeer(50f, Side.Left);
var t2 = Musketeer(51f, Side.Right);
var tie = Musketeer(52f, default);
Distribute(kingdom, () => tie._guardSide = Side.Right);      // native rank puts the fresh career right
Check(tie._guardSide == Side.Left, "neutral tie: goes left");
Check(WritesOn(tie) == 1 && WritesOn(t1, t2) == 0, "neutral tie: only the newcomer is written");

// ---- 8. death/removal shrinks the subset without churn ---------------------

Reset();
var d1 = Musketeer(60f, Side.Left);
var d2 = Musketeer(61f, Side.Left);
var d3 = Musketeer(62f, Side.Right);
var d4 = Musketeer(63f, Side.Right);
d4._damageable.isDead = true;                    // dying units leave the subset
Distribute(kingdom);
Check(WritesOn(d1, d2, d3, d4) == 0, "dying unit: remaining 2/1 stays inside the tolerance");
Check(d4.Writes.Count == 0, "dying unit: never written");

MusketeerIdentity.Registered.Remove(d3);         // despawned/unbound: registry no longer lists it
Distribute(kingdom);
Check(d1._guardSide == Side.Left && d2._guardSide == Side.Right, "removed unit: the remaining pair rebalances to 1/1");
Check(WritesOn(d2) == 1 && WritesOn(d1) == 0, "removed unit: only the moved unit is written");

// ---- 9. ordinary archers are never touched ---------------------------------

Reset();
var m1 = Musketeer(70f, Side.Left);
var ordinary = new Archer { _character = new Character(), _damageable = new Damageable() };
new GameObject().Add(ordinary);
ordinary.transform.position = new Vector3 { x = 71f };
Distribute(kingdom);
Check(ordinary.Writes.Count == 0, "ordinary archer: never written");
Check(WritesOn(m1) == 0, "single musketeer: not force-split");

// ---- 10. feature off / online passes through -------------------------------

Reset();
var off1 = Musketeer(80f, Side.Left);
var off2 = Musketeer(81f, Side.Left);
MusketeerAccess.Enabled = false;                 // config off or online: same access gate
Check(!Begin(kingdom), "off: no capture");
End(kingdom, false);
MusketeerDefense.End(kingdom);                   // direct postfix path with no capture
Check(WritesOn(off1, off2) == 0 && off1._guardSide == Side.Left && off2._guardSide == Side.Left, "off: nothing written");
MusketeerAccess.Enabled = true;

// ---- 11. foreign-world units are excluded ----------------------------------

Reset();
var f1 = Musketeer(90f, Side.Left);
var f2 = Musketeer(91f, Side.Left);
var f3 = Musketeer(92f, Side.Left);
MusketeerAccess.Foreign.Add(f3);
Distribute(kingdom);
Check(f3.Writes.Count == 0 && f3._guardSide == Side.Left, "foreign world: unit untouched");
Check(f1._guardSide == Side.Left && f2._guardSide == Side.Right, "foreign world: unit not counted (remaining pair 1/1)");

// ---- 12. horn override skips the split -------------------------------------

Reset();
var o1 = Musketeer(100f, Side.Left);
var o2 = Musketeer(101f, Side.Left);
kingdom.overrideGuard = true;
Check(!Begin(kingdom), "override: native concentration is respected");
Check(WritesOn(o1, o2) == 0, "override: no writes");
kingdom.overrideGuard = false;

// ---- 13. missing walls use native campfire/minimum-extent fallback ---------

Reset();
var w1 = Musketeer(110f, Side.Left);
var w2 = Musketeer(111f, Side.Left);
kingdom.intactWall[Side.Left] = null;
Distribute(kingdom);
Check(w1._guardSide == Side.Left && w2._guardSide == Side.Right, "missing left wall: valid fallback splits pair");
kingdom.intactWall[Side.Right] = null;
w2._guardSide = Side.Left;
Distribute(kingdom);
Check(w2._guardSide == Side.Right, "both walls absent: valid fallback splits pair");
kingdom.campaignOverride = true;
Check(!Begin(kingdom), "campaign target override: split declined");
kingdom.campaignOverride = false;
kingdom.fallbackOverride = float.PositiveInfinity;
Check(!Begin(kingdom), "invalid native guard target: split declined");
kingdom.intactWall[Side.Left] = new GameObject();

// ---- 14. one completely safe side still splits the firearm subset ----------
// Native danger allocation would park this whole native batch on the safe side (<=4 home-guard
// slots). The firearm subset is requested to split evenly anyway; ordinary archers stay native.

Reset();
var s1 = Musketeer(120f, Side.Left);
var s2 = Musketeer(121f, Side.Left);
var s3 = Musketeer(122f, Side.Left);
var s4 = Musketeer(123f, Side.Left);
managers.enemies.safeLeft = true;                // left completely safe, right threatened
Distribute(kingdom);
Check(s1._guardSide == Side.Left && s2._guardSide == Side.Left
    && s3._guardSide == Side.Right && s4._guardSide == Side.Right,
    "one-side-safe: firearm batch splits 2/2");
Check(WritesOn(s1, s2) == 0 && WritesOn(s3, s4) == 2, "one-side-safe: only the movers are written");
ClearWrites(s1, s2, s3, s4);
Distribute(kingdom);
Check(WritesOn(s1, s2, s3, s4) == 0, "one-side-safe: repeat pass writes nothing");
Check(s1._guardSide == Side.Left && s2._guardSide == Side.Left
    && s3._guardSide == Side.Right && s4._guardSide == Side.Right,
    "one-side-safe: repeat pass keeps 2/2");
managers.enemies.safeLeft = false;

// ---- 15. per-unit native exclusions ----------------------------------------

var exclusions = new List<(string Name, Action<Archer> Apply)>
{
    ("knight follower", m => m._knight = new Knight()),
    ("tower slot", m => m._guardSlot = new GuardSlot()),
    ("tower flag", m => m.inGuardSlot = true),
    ("native unavailable", m => m.isAvailable = false),
    ("embarked", m => m._embarkee = new Embarkee { IsEmbarked = true }),
    ("boarding", m => m._embarkee = new Embarkee { EmbarkableTarget = new GameObject() }),
    ("formation member", m => m.formation = new Formation()),
    ("player controlled", m => m.playerControlled = true),
    ("hidden", m => m.harmless = true),
    ("inert", m => m._character.inert = true),
    ("grabbed", m => m._character.grabbed = true),
    ("stationary", m => m._character.isStationary = true),
    ("dying", m => m._damageable.isDead = true),
    ("tower height", m => m.transform.position = new Vector3 { x = m.transform.position.x, y = 3f }),
    ("unmarked", m => MusketeerIdentity.NotUnits.Add(m)),
    ("inactive", m => m.gameObject.activeInHierarchy = false),
    ("off-world", m => MusketeerAccess.Foreign.Add(m))
};

foreach (var (name, apply) in exclusions)
{
    Reset();
    var e1 = Musketeer(200f, Side.Left);
    var e2 = Musketeer(201f, Side.Left);
    var e3 = Musketeer(202f, Side.Left);
    var e4 = Musketeer(203f, Side.Left);
    apply(e4);
    Distribute(kingdom);
    Check(e4.Writes.Count == 0 && e4._guardSide != Side.Right, name + ": excluded unit untouched");
    Check(e1._guardSide == Side.Left && e2._guardSide == Side.Left && e3._guardSide == Side.Right, name + ": remaining three rebalance 2/1");
    Check(WritesOn(e1, e2, e3) == 1, name + ": exactly one write");
}

// ---- 16. movers never stack on one depth -----------------------------------

Reset();
var six = new Archer[6];
for (int i = 0; i < 6; i++) six[i] = Musketeer(500f + i, Side.Left);
Distribute(kingdom);
int sixLeft = 0;
var rightDepths = new HashSet<int>();
foreach (var m in six)
{
    if (m._guardSide == Side.Left) sixLeft++;
    else rightDepths.Add(m._guardDepth);
}
Check(sixLeft == 3 && rightDepths.Count == 3, "six-left: 3/3 with distinct mover depths");

// ---- 17. nested native distribution ----------------------------------------

Reset();
var n1 = Musketeer(300f, Side.Left);
var n2 = Musketeer(301f, Side.Left);
var n3 = Musketeer(302f, Side.Left);
var n4 = Musketeer(303f, Side.Left);
bool outer = Begin(kingdom);
Check(outer, "nested: outer call owns the capture");
Check(!Begin(kingdom), "nested: inner call does not capture");
foreach (var m in new[] { n1, n2, n3, n4 }) { m._guardSide = Side.Left; m._guardDepth = 0; }
End(kingdom, outer);
Check(n3._guardSide == Side.Right && n4._guardSide == Side.Right, "nested: outer postfix still rebalances");
postfix.Invoke(null, new object[] { kingdom, false });      // inner postfix with __state=false
Check(WritesOn(n1, n2, n3, n4) == 2, "nested: inner postfix adds no writes");

// ---- 18. abandoned capture (native throw) is dropped -----------------------

Reset();
var st1 = Musketeer(310f, Side.Left);
var st2 = Musketeer(311f, Side.Left);
Check(Begin(kingdom), "stale: capture in flight");
Check(!Begin(kingdom), "stale: same frame+time is treated as nested");
Time.frameCount++;
Time.unscaledTime += 1f;
bool fresh = Begin(kingdom);
Check(fresh, "stale: a later distribution drops the abandoned capture");
End(kingdom, fresh);
Check(st2._guardSide == Side.Right, "stale: the fresh capture still rebalances");

// ---- 19. enforcement keeps the unit's own native depth ---------------------

Reset();
var p1 = Musketeer(320f, Side.Right);
p1._guardDepth = 99;                             // whatever rank native last gave it on that side
var p2 = Musketeer(321f, Side.Left); // balanced prior assignment: this case tests restoration, not rebalancing
p2._guardDepth = 0;
bool pOwns = Begin(kingdom);
p1._guardSide = Side.Left;                       // native flips it to the wrong side
p1._guardDepth = 5;
End(kingdom, pOwns);
Check(p1._guardSide == Side.Right, "depth reuse: flipped unit restored");
Check(p1._guardDepth == 99, "depth reuse: the native depth is kept, no invented cap");

var q1 = Musketeer(330f, Side.Right);
q1._guardDepth = 3;
bool qOwns = Begin(kingdom);
q1._guardSide = Side.Left;
End(kingdom, qOwns);
Check(q1._guardSide == Side.Right && q1._guardDepth == 3, "depth reuse: sound recorded depth is kept");

// ---- 20. canary is one line per world --------------------------------------

Reset();
MusketeerAccess.World = new Transform { gameObject = new GameObject() };   // new world
Distribute(kingdom);
int canary = Canaries();
Check(canary >= 1, "canary: logged on the first gated distribution of a world");
Distribute(kingdom);
Check(Canaries() == canary, "canary: not repeated inside one world");
MusketeerAccess.World = new Transform { gameObject = new GameObject() };
Distribute(kingdom);
Check(Canaries() == canary + 1, "canary: repeats once per new world");

// ---- 21. context refusals --------------------------------------------------

Check(!Begin(null), "null kingdom refused");
Check(!Begin(rivals), "foreign kingdom instance refused");
MusketeerAccess.World = null;
Check(!Begin(kingdom), "missing world layer refused");
MusketeerAccess.World = new Transform { gameObject = new GameObject() };

// ---- 22. empty / stale registry and no side effects ------------------------

Reset();
Distribute(kingdom);
Check(Probe.TotalWrites == 0, "empty registry: no writes");
var x1 = Musketeer(400f, Side.Left);
var x2 = Musketeer(401f, Side.Left);
MusketeerIdentity.Registered.Add(null);
Distribute(kingdom);
Check(x1._guardSide == Side.Left && x2._guardSide == Side.Right, "stale null registry slot is skipped");

// ---- 23. mover depth avoids slots ordinary archers already hold -------------

Reset();
var g1 = Musketeer(600f, Side.Left);
var g2 = Musketeer(601f, Side.Left);
var residentA = OrdinaryArcher(602f, Side.Right, 0);
var residentB = OrdinaryArcher(603f, Side.Right, 1);
UnitScanCache.Archers = new Archer[] { g1, g2, residentA, residentB };
Distribute(kingdom);
Check(g2._guardSide == Side.Right && g2._guardDepth == 2, "census: mover skips ordinary occupied slots");
Check(residentA._guardSide == Side.Right && residentA._guardDepth == 0
    && residentB._guardSide == Side.Right && residentB._guardDepth == 1, "census: ordinary residents untouched");
Check(WritesOn(g1, residentA, residentB) == 0, "census: only the mover is written");
Check(UnitScanCache.Calls == 0, "census: stale shared scan never consulted");

Reset();
var fresh1 = Musketeer(610f, Side.Left);
var fresh2 = Musketeer(611f, Side.Left);
UnitScanCache.Archers = new[] { fresh1, fresh2 }; // cache predates ordinary archer OnEnable
var justEnabled = OrdinaryArcher(612f, Side.Right, 0);
Distribute(kingdom);
Check(fresh2._guardDepth == 1, "fresh census: same-frame ordinary native index reserves depth zero");
Check(justEnabled.Writes.Count == 0 && UnitScanCache.Calls == 0, "fresh census: ordinary unit and old cache untouched");

Reset();
var missing1 = Musketeer(620f, Side.Left);
var missing2 = Musketeer(621f, Side.Left);
kingdom._availableArchersCache = null;
Distribute(kingdom);
Check(WritesOn(missing1, missing2) == 0, "missing native census: decline writes rather than guess free slots");

// ---- 24. no census when nothing needs a write ------------------------------

Reset();
var h1 = Musketeer(700f, Side.Left);
var h2 = Musketeer(701f, Side.Right);
UnitScanCache.Archers = new Archer[] { h1, h2, OrdinaryArcher(702f, Side.Right, 0) };
Distribute(kingdom);
Check(WritesOn(h1, h2) == 0, "census: balanced pass writes nothing");
Check(UnitScanCache.Calls == 0, "census: no scan when nothing needs a write");

Reset();
var event1 = Musketeer(710f, Side.Left);
var event2 = Musketeer(711f, Side.Left);
int nativeBefore = kingdom.DistributeCalls;
Check(MusketeerDefense.RedistributeAfterBindings(), "binding event: native redistribution handled");
Check(kingdom.DistributeCalls == nativeBefore + 1 && event2._guardSide == Side.Right,
    "binding event: native body runs before marked subset is balanced");
kingdom.NativeDistribution = () => Check(!MusketeerDefense.RedistributeAfterBindings(), "binding event: no recursive native distribution");
Check(MusketeerDefense.RedistributeAfterBindings(), "binding event: outer call completes");

Check(UnityEngine.Object.DestroyCount == DestroyBaseline, "policy never destroys objects");
Check(UnityEngine.Object.FindCount == FindBaseline, "policy never runs its own scene search");
Check(MusketeerIdentity.CopyCalls > 0, "discovery is the identity registry only");

Console.WriteLine($"PASS {passed} assertions (production MusketeerDefense + hook bodies)");
