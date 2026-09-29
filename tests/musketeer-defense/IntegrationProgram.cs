using KingdomEnhancedMod;
using UnityEngine;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; }
void NextFrame() { Time.frameCount++; Time.unscaledTime += .1f; MusketeerIdentity.Tick(); }
MusketeerIdentity.IslandState Setup()
{
    Managers.Inst = new Managers { world = new World { gameLayer = new GameObject().Add(new Transform()) }, kingdom = new Kingdom() };
    GlobalSaveData.loaded = new GlobalSaveData { currentCampaign = 1 };
    CampaignSaveData.current = new CampaignSaveData { CurrentIsland = new IslandSaveData { land = 1, isNew = false } };
    MusketeerIdentity.Islands.Clear();
    MusketeerAccess.TrackAllowed = true;
    MusketeerAccess.Enabled = true;
    MusketeerAccess.Playing = true;
    MusketeerIdentity.InvalidateContextCache();
    var state = new MusketeerIdentity.IslandState
    {
        ContextKey = MusketeerArchive.ContextKey("global-v35", 1, 0, 1),
        World = MusketeerIdentity.WorldKey(), Ready = true
    };
    MusketeerIdentity.InstallState(state.ContextKey, state);
    NextFrame();
    return state;
}
(Character character, Archer archer) Spawn(float x)
{
    var go = new GameObject();
    var character = go.Add(new Character());
    var archer = go.Add(new Archer { _character = character });
    go.transform.position = new Vector3 { x = x };
    Managers.Inst.kingdom.Archers.Add(archer);
    return (character, archer);
}
Archer Promote(MusketeerIdentity.IslandState state, int n)
{
    var gunGo = new GameObject();
    var gun = gunGo.Add(new DroppableTool());
    var career = new MusketeerIdentity.Career { State = state, Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun };
    state.Careers.Add(career);
    Check(MusketeerIdentity.Bind(career, MusketeerIdentity.RootOf(gunGo, true), null, null, gun, -1), "paid gun binding");
    MusketeerIdentity.OnGunPickupBegin(gun, out var promotion);
    Check(promotion.Engaged, "real identity promotion prefix engaged");
    var unit = Spawn(10 + n);
    Check(!MusketeerIdentity.IsUnit(unit.archer), "OnEnable successor not yet marked");
    Managers.Inst.kingdom.DistributeFreeArchers(); // native OnEnable, before Promote postfix
    MusketeerIdentity.OnGunPickupEnd(unit.character, promotion);
    Check(MusketeerIdentity.IsUnit(unit.archer), "real Promote postfix binds successor");
    int before = Managers.Inst.kingdom.DistributeCalls;
    NextFrame(); // deliberately still inside promotion: must defer even across frame
    Check(Managers.Inst.kingdom.DistributeCalls == before, "no native re-entry while promotion active");
    MusketeerIdentity.OnGunPickupAbort(promotion); // real finalizer closes scope
    return unit.archer;
}

var state = Setup();
var ordinary = Spawn(200).archer;
var recruits = new List<Archer>();
for (int i = 0; i < 4; i++)
{
    recruits.Add(Promote(state, i));
    int before = Managers.Inst.kingdom.DistributeCalls;
    NextFrame();
    Check(Managers.Inst.kingdom.DistributeCalls == before + 1, "one post-bind native event per completed purchase");
    Check(Math.Abs(recruits.Count(x => x._guardSide == Side.Left) - recruits.Count(x => x._guardSide == Side.Right)) <= 1,
        "after each completed purchase subset differs by at most one");
}
Check(recruits.Count(x => x._guardSide == Side.Right) == 2, "four sequential purchases finish 2/2 without unrelated event");
Check(recruits.Where(x => x._guardSide == Side.Right).All(x => x._guardDepth != ordinary._guardDepth), "fresh native ordinary slot respected after final purchase");
Check(ordinary._guardSide == Side.Right, "ordinary archer keeps its native side (no policy write)");
Check(ordinary.Writes.All(w => w.Side == Side.Right), "ordinary archer only receives same-side rank writes");
int completed = Managers.Inst.kingdom.DistributeCalls;
for (int i = 0; i < 20; i++) NextFrame();
Check(Managers.Inst.kingdom.DistributeCalls == completed, "consumed event does not poll native on later frames");

// Real load-row binding and real LoadCapture.End: no archive commit is requested by this fixture.
state = Setup();
state.Ready = false;
var loaded = new List<Archer>();
var load = new MusketeerPersistence.LoadCapture();
typeof(MusketeerPersistence.LoadCapture).GetField("State", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(load, state);
for (int i = 0; i < 4; i++)
{
    string id = "loaded-" + i;
    state.Careers.Add(new MusketeerIdentity.Career { State = state, Id = Guid.NewGuid(), Kind = MusketeerCareer.KindUnit, NativeId = id });
    var unit = Spawn(20 + i);
    loaded.Add(unit.archer);
    Managers.Inst.kingdom.DistributeFreeArchers();
    Check(MusketeerIdentity.CaptureLoadRow(state, id, unit.character.gameObject.Add(new Persistent()), out _), "real load row captured");
    int before = Managers.Inst.kingdom.DistributeCalls;
    NextFrame();
    Check(Managers.Inst.kingdom.DistributeCalls == before, "incomplete load does not redistribute");
}
Check(loaded.All(x => x._guardSide == Side.Left), "saved four-left state reproduced before ready");
int loadBefore = Managers.Inst.kingdom.DistributeCalls;
load.End(true);
NextFrame();
Check(state.Ready && Managers.Inst.kingdom.DistributeCalls == loadBefore + 1, "load completion flushes one coalesced native event");
Check(loaded.Count(x => x._guardSide == Side.Right) == 2, "loaded four-left units finish 2/2");
NextFrame();
Check(Managers.Inst.kingdom.DistributeCalls == loadBefore + 1, "load event consumed once");

state = Setup();
Promote(state, 1);
Promote(state, 2); // both binds happen before flushing final pending event
var failureVictim = Spawn(200).archer;               // sits right of the model split
Managers.Inst.kingdom.FollowerLeft = 4;
Managers.Inst.kingdom.FollowerRight = 4;             // polluted quota: the native right counter goes negative
Managers.Inst.kingdom.ThrowNative = true;
int failureBefore = Managers.Inst.kingdom.DistributeCalls;
for (int i = 0; i < 10; i++) NextFrame();
Check(Managers.Inst.kingdom.DistributeCalls == failureBefore + 3, "native failures exhaust exactly three event attempts, stale capture self-recovers");
Check(state.DefenseAttempts == 0, "failure retry budget fully consumed");
Check(Managers.Inst.kingdom.LastNativeMinDepth < 0 && failureVictim._guardDepth == Managers.Inst.kingdom.LastNativeMinDepth,
    "native throw: the broken rank stays broken - A never repairs outside the boundary");
Managers.Inst.kingdom.ThrowNative = false;
Managers.Inst.kingdom.FollowerLeft = 0;
Managers.Inst.kingdom.FollowerRight = 0;
Managers.Inst.kingdom.DistributeFreeArchers();       // normal boundary after the failures
Check(failureVictim._guardDepth >= 0, "normal boundary after failures: ranks repaired");

state = Setup();
Promote(state, 1);
int disabledBefore = Managers.Inst.kingdom.DistributeCalls;
MusketeerAccess.Enabled = false;
NextFrame();
Check(Managers.Inst.kingdom.DistributeCalls == disabledBefore, "disabled feature never forces native redistribution");
Check(state.DefenseAttempts == 0, "disabled event consumed without retry loop");

state = Setup();
MusketeerAccess.Playing = false; // Menu retains authority and identities, defers pending work
var paused = new List<Archer>();
for (int i = 0; i < 4; i++) paused.Add(Promote(state, i));
int pausedBefore = Managers.Inst.kingdom.DistributeCalls;
for (int i = 0; i < 10; i++) NextFrame();
Check(Managers.Inst.kingdom.DistributeCalls == pausedBefore && state.DefenseAttempts == 3,
    "paused batch neither redistributes nor consumes finite retries");
MusketeerAccess.Playing = true;
NextFrame();
Check(Managers.Inst.kingdom.DistributeCalls == pausedBefore + 1 && paused.Count(x => x._guardSide == Side.Right) == 2,
    "resume coalesces four bindings into one pass and 2/2");
int pausedWrites = paused.Sum(x => x.Writes.Count);
for (int i = 0; i < 10; i++) { MusketeerAccess.Playing = i % 2 == 0; NextFrame(); }
Check(Managers.Inst.kingdom.DistributeCalls == pausedBefore + 1 && paused.Sum(x => x.Writes.Count) == pausedWrites,
    "repeated pause/resume causes no native redistribution or guard writes after event consumed");

state = Setup();
Promote(state, 1);
int onlineBefore = Managers.Inst.kingdom.DistributeCalls;
MusketeerAccess.TrackAllowed = false;
for (int i = 0; i < 5; i++) NextFrame();
Check(Managers.Inst.kingdom.DistributeCalls == onlineBefore, "online authority gate never distributes pending event");

state = Setup();
Promote(state, 1);
var oldState = state;
state = Setup(); // a different world/current state owns the next Tick
int newWorldBefore = Managers.Inst.kingdom.DistributeCalls;
NextFrame();
Check(Managers.Inst.kingdom.DistributeCalls == newWorldBefore && state.DefenseAttempts == 0,
    "previous world's pending binding does not run in replacement current state");

state = Setup();
Promote(state, 1);
Check(state.DefenseAttempts == 3, "same-context world switch starts with an unconsumed event");
long previousWorld = state.World;
Managers.Inst.world.gameLayer = new GameObject().Add(new Transform());
Managers.Inst.kingdom = new Kingdom();
// Preserve GlobalSaveData, CampaignSaveData, Islands and the exact IslandState instance.
NextFrame();
Check(ReferenceEquals(MusketeerIdentity.Current, state) && state.World != previousWorld,
    "same-context world switch preserves state and updates current runtime world");
Check(Managers.Inst.kingdom.DistributeCalls == 0 && state.DefenseAttempts == 0,
    "same-context world switch cancels old binding event before invoking new kingdom");
var newWorldA = Promote(state, 2);
var newWorldB = Promote(state, 3);
int newBindingBefore = Managers.Inst.kingdom.DistributeCalls;
NextFrame();
Check(Managers.Inst.kingdom.DistributeCalls == newBindingBefore + 1,
    "fresh bindings in replacement world enqueue a new coalesced event");
Check(newWorldA._guardSide != newWorldB._guardSide,
    "fresh replacement-world binding event still balances new marked units");

// ---- issue-78 root-cause vector through the full boundary --------------------
// 20 free archers (10 left / 10 right by the model split) with the measured follower quota
// pollution: the native counter hands out 5..-4; the real A boundary must deliver 9..0.
state = Setup();
var vecLeft = new List<Archer>();
var vecRight = new List<Archer>();
for (int i = 0; i < 10; i++) vecLeft.Add(Spawn(i).archer);
for (int i = 0; i < 10; i++) vecRight.Add(Spawn(200 + i).archer);
Managers.Inst.kingdom.FollowerLeft = 4;
Managers.Inst.kingdom.FollowerRight = 4;
Managers.Inst.kingdom.DistributeFreeArchers();
Check(Managers.Inst.kingdom.LastNativeMinDepth == -4, "vector: native model produced the measured 5..-4 right ranks");
Check(vecRight.Select(x => x._guardDepth).SequenceEqual(Enumerable.Range(0, 10).Reverse()),
    "vector: right ranks delivered as 9..0");
Check(vecLeft.Select(x => x._guardDepth).SequenceEqual(Enumerable.Range(0, 10)), "vector: left ranks delivered as 0..9");
Check(vecLeft.All(x => x._guardSide == Side.Left) && vecRight.All(x => x._guardSide == Side.Right),
    "vector: native sides preserved");
Check(vecLeft.Concat(vecRight).All(x => x._guardDepth >= 0), "vector: no negative rank delivered");
int vecWrites = vecLeft.Concat(vecRight).Sum(x => x.Writes.Count);
GuardRankDistribution.ReindexAfterNative(Managers.Inst.kingdom);
Check(vecLeft.Concat(vecRight).Sum(x => x.Writes.Count) == vecWrites, "vector: repeat pass on the corrected state writes nothing");

Console.WriteLine($"PASS {passed} integration assertions (real Defense + Identity + Persistence + Archive; modeled native OnEnable ordering)");
