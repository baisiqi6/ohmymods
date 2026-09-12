using System.Reflection;
using System.Collections;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    private static readonly List<Knight> Knights = new();
    private static int passed, failed;
    private static void Eq<T>(T expected, T actual, string message)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{message}: expected {expected}, got {actual}"); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Test(string name, Action action)
    {
        foreach (var knight in Knights) { DisableHook(knight); Scheduler.StopOwnerSilently(knight); }
        Knights.Clear(); Scheduler.Reset(); SamuraiDashVisuals.Reset(); UnitScanCache.Archers = Array.Empty<Archer>(); UnitScanCache.Calls = 0;
        Physics2D.Hits = Array.Empty<Collider2D>(); Physics2D.Scans = 0;
        Physics2D.Buffers.Clear(); Physics2D.LastRadius = 0;
        Time.time = 0; Time.deltaTime = .02f; Time.timeScale = 1; Time.frameCount = 0;
        ModConfig.Enabled.Value = true; NetworkBigBoss.HasWorldAuth = true; Managers.Inst = new();
        KingdomEnhancedPlugin.Instance.LogSource.Errors.Clear();
        try { action(); Eq(0, KingdomEnhancedPlugin.Instance.LogSource.Errors.Count, "production error logs"); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message); }
    }
    private static void InvokeHook(Type type, string name, Knight knight)
    { type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { knight }); }
    private static void UpdateHook(Knight knight) => InvokeHook(typeof(Knight_Update_SamuraiPowerDash_Patch), "Postfix", knight);
    private static void DisableHook(Knight knight) { var type = typeof(Knight_OnDisable_SamuraiPowerDash_Patch); type.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, new object[] { knight }); InvokeHook(type, "Postfix", knight); }
    private static Knight NewKnight(float x = 0)
    {
        var go = new GameObject(); var k = go.AddComponent<Knight>(); k.transform.position = new(x);
        k._mover = go.AddComponent<Mover>(); k._damageable = go.AddComponent<Damageable>();
        k._character = go.AddComponent<Character>(); k._trail = go.AddComponent<TrailRenderer>(); k._animator = go.AddComponent<Animator>();
        Knights.Add(k); return k;
    }
    private static Archer Follower(Knight knight, float x)
    {
        var go = new GameObject(); var follower = go.AddComponent<Archer>(); follower._knight = knight;
        follower._damageable = go.AddComponent<Damageable>(); follower._character = go.AddComponent<Character>();
        follower.transform.position = new(x);
        UnitScanCache.Archers = UnitScanCache.Archers.Append(follower).ToArray(); return follower;
    }
    private static Damageable Enemy(Knight knight, float x)
    {
        var go = new GameObject(); go.transform.position = new(x); var enemy = go.AddComponent<Damageable>();
        Physics2D.Hits = new[] { go.AddComponent<Collider2D>() }; knight._enemyScanner.Closest = go; return enemy;
    }
    private static Damageable HitTarget(float x = 0)
    {
        var go = new GameObject(); go.transform.position = new(x);
        var target = go.AddComponent<Damageable>(); go.AddComponent<Collider2D>(); return target;
    }
    private static void Supply(params Damageable[] targets) => Physics2D.Hits = targets.Select(t => t.GetComponent<Collider2D>()).ToArray();
    private static Knight PrepareBurst(bool returning)
    {
        var k = NewKnight(returning ? 20 : 0); Follower(k, 0);
        if (!returning) Enemy(k, 3);
        Physics2D.Hits = Array.Empty<Collider2D>(); return k;
    }
    private static void Frame(float dt = .02f, bool move = true)
    {
        Time.frameCount++; Time.deltaTime = dt; Time.time += dt;
        if (move) foreach (var knight in Knights) knight._mover?.Step(dt);
        foreach (var knight in Knights) if (knight.gameObject.activeInHierarchy) UpdateHook(knight);
        Scheduler.Advance();
    }
    private static void Frames(int count, float dt = .02f, bool move = true)
    { for (int i = 0; i < count; i++) Frame(dt, move); }
    private static bool ShouldSlash(Knight knight)
    {
        var type = typeof(PatchRoles_SamuraiPowerDash).Assembly.GetTypes().FirstOrDefault(t =>
            t.GetCustomAttributes<HarmonyLib.HarmonyPatch>().Any(a => a.TargetType == typeof(Knight) && a.MethodName == "ShouldSlash"));
        Check(type != null, "Expected narrow ShouldSlash hook during return");
        var method = type.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
        var parameters = method.GetParameters(); object[] args = parameters.Select(p => p.Name == "__instance" ? (object)knight : true).ToArray();
        bool run = (bool)method.Invoke(null, args);
        if (run) return true;
        for (int i = 0; i < parameters.Length; i++) if (parameters[i].Name == "__result") return (bool)args[i];
        return false;
    }
    private static void AssertStartedReturn(Knight knight, float followerX)
    {
        UpdateHook(knight);
        Eq(Mover.GoalMode.Position, knight._mover.goalMode, "return issued position goal");
        Check(MathF.Abs(followerX - knight._mover._goalPosition) < MathF.Abs(followerX - knight.transform.position.x), "return goal moves toward follower");
        Check(MathF.Abs(knight._mover._goalPosition - knight.transform.position.x) > .1f, "return goal differs from current position");
        Check(!ShouldSlash(knight), "normal slash suppressed during real return");
    }
    private static void NativeFrame(Knight k)
    {
        typeof(Knight_Update_SamuraiPowerDash_Patch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, new object[] { k });
        k._fsm.Update();
        UpdateHook(k);
    }
    private static void NightRegressions()
    {
        Test("Night leash hands off once; native owns retreat and damage stays untouched", () => {
            Managers.Inst.kingdom.isDaytime = false;
            var k = NewKnight(12); Follower(k, 0); var enemy = Enemy(k, 15);
            k._fsm.OnEnter = state => {
                if (state == Knight.State.GoToWall) { k.isRetreating = true; k._mover.SetGoal(1, 2); }
            };
            UpdateHook(k);
            Eq(0, k._fsm.Requests, "postfix does not leave a queue across frames");
            typeof(Knight_Update_SamuraiPowerDash_Patch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, new object[] { k });
            Eq(1, k._fsm.Requests, "one native request"); Eq(Knight.State.Stand, k._fsm.Current, "request does not enter immediately");
            Eq(0, k._mover.GoalWrites, "mod does not supply return goal"); Eq(0, enemy.HitCount, "return is not an attack");
            typeof(Knight_Update_SamuraiPowerDash_Patch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, new object[] { k }); Eq(1, k._fsm.Requests, "same frame cannot requeue");
            k._fsm.Update(); UpdateHook(k); Eq(Knight.State.GoToWall, k._fsm.Current, "next native update enters wall task");
            Check(k.isRetreating, "native fixture supplies retreat posture"); Eq(1f, k._mover._goalPosition, "current native target used");
            Eq(0, SamuraiDashVisuals.BeginCount(k), "no mod return effects");
        });
        Test("Night attack crossing ten stops owned dash then permits native handoff", () => {
            Managers.Inst.kingdom.isDaytime = false;
            var k = NewKnight(8); Follower(k, 0); Enemy(k, 13); UpdateHook(k);
            Check(k._damageable.invulnerable, "forward attack began");
            k.transform.position = new(10.1f); UpdateHook(k); NativeFrame(k);
            Eq(1, k._fsm.Requests, "over-leash idle motion restored through native request");
            Check(!k._damageable.invulnerable && !k._trail.enabled, "forward effects retired");
        });
        Test("Night in-range finish retains full cooldown and can attack again", () => {
            Managers.Inst.kingdom.isDaytime = false;
            var k = NewKnight(); Follower(k, 0); Enemy(k, 3); UpdateHook(k);
            Time.time = .61f; Time.frameCount++; Scheduler.Advance();
            Eq(0, k._fsm.Requests, "ordinary finish does not force wall");
            Time.time = 3.60f; UpdateHook(k); Eq(1, Scheduler.Started, "full three seconds preserved");
            Time.time = 3.62f; UpdateHook(k); Eq(2, Scheduler.Started, "next legal attack still available");
        });
        foreach (bool afterObservation in new[] { false, true })
        foreach (var interrupt in new (string Name, Action<Knight> Apply)[] {
            ("external goal", k => k._mover.SetGoal(90, 5)),
            ("object goal", k => { k._mover.goalMode = Mover.GoalMode.Object; k._mover._goalObject = new GameObject(); }),
            ("new queued task", k => k._fsm.GoToState(Knight.State.Charge)),
            ("charge", k => k._shouldCharge = true), ("formation", k => k.Formation = new()),
            ("embark", k => k._embarkee.IsTargetingEmbarkable = true), ("control", k => k.ControlRequested = true),
            ("pillar", k => k.helPuzzlePillar = new()), ("Assemble", k => k._fsm.Current = Knight.State.Assemble),
            ("dead", k => k._damageable.isDead = true), ("disabled", k => k.enabled = false),
            ("pause", k => Time.timeScale = 0), ("mover pause", k => k._mover._pauseTimeout = 1),
            ("authority", k => NetworkBigBoss.HasWorldAuth = false), ("mod off", k => ModConfig.Enabled.Value = false),
            ("follower lost", k => UnitScanCache.Archers[0]._damageable.isDead = true),
            ("mover replacement", k => k._mover = k.gameObject.AddComponent<Mover>())
        }) Test("Night handoff respects " + interrupt.Name + (afterObservation ? " after observation" : " before observation"), () => {
            Managers.Inst.kingdom.isDaytime = false;
            var k = NewKnight(12); Follower(k, 0);
            if (afterObservation) UpdateHook(k);
            interrupt.Apply(k);
            NativeFrame(k);
            Check(k._fsm.Current != Knight.State.GoToWall, "must not enter wall over newer constraint");
            Check(!k._fsm._executeQueuedState || k._fsm._queuedState != Knight.State.GoToWall, "must not leave unsafe wall queued");
            if (interrupt.Name == "external goal") Eq(90f, k._mover._goalPosition, "new goal preserved");
            if (interrupt.Name == "new queued task") Check(k._fsm.Current == Knight.State.Charge, "new queued task preserved");
        });
        Test("Night no followers retains forward attack", () => {
            Managers.Inst.kingdom.isDaytime = false; var k = NewKnight(12); Enemy(k, 15); UpdateHook(k);
            Eq(0, k._fsm.Requests, "no made-up leash anchor"); Eq(1, Scheduler.Started, "no-follower attack retained");
        });
        Test("Night refreshes stopped current wall task once with its latest native destination", () => {
            Managers.Inst.kingdom.isDaytime = false; var k = NewKnight(12); Follower(k, 0); k._fsm.Current = Knight.State.GoToWall;
            UpdateHook(k); float latestWall = 2;
            k._fsm.OnEnter = _ => k._mover.SetGoal(latestWall, 2);
            latestWall = 3; NativeFrame(k); NativeFrame(k); NativeFrame(k);
            Eq(1, k._fsm.Requests, "native Position ownership prevents repeat restarts"); Eq(3f, k._mover._goalPosition, "no stale saved wall position");
        });
        Test("Night ten is inclusive and still permits the next forward attack", () => {
            Managers.Inst.kingdom.isDaytime = false; var k = NewKnight(10); Follower(k, 0); Enemy(k, 13); NativeFrame(k);
            Eq(1, Scheduler.Started, "exact ten can attack"); Eq(0, k._fsm.Requests, "no wall request at boundary");
        });
        Test("Dusk retires active daytime return without further dash hits", () => {
            var k = NewKnight(12); Follower(k, 0); UpdateHook(k); Check(k._damageable.invulnerable, "day return exists");
            Managers.Inst.kingdom.isDaytime = false; var enemy = Enemy(k, 10); UpdateHook(k);
            Eq(0, enemy.HitCount, "dusk return does not add attack hits"); Check(!k._damageable.invulnerable, "day burst effects ended");
            NativeFrame(k); Eq(1, k._fsm.Requests, "night native return takes over");
        });
        Test("Actor replacement with reused instance id cannot inherit old night request", () => {
            Managers.Inst.kingdom.isDaytime = false; var old = NewKnight(12); Follower(old, 0); UpdateHook(old);
            var replacement = NewKnight(12); replacement.gameObject.Id = old.gameObject.Id;
            NativeFrame(replacement); Eq(0, replacement._fsm.Requests, "new actor does not inherit old follower");
            Eq(0, old._fsm.Requests, "old state machine untouched");
        });
        Test("In-flight new native queued task stops old hits without overwriting its queue", () => {
            Managers.Inst.kingdom.isDaytime = false; var k = NewKnight(); Follower(k, 0); Enemy(k, 3); UpdateHook(k);
            var enemy = HitTarget(); Supply(enemy); k._fsm.GoToState(Knight.State.Charge);
            Time.time = .1f; Time.frameCount++; Scheduler.Advance();
            Eq(0, enemy.HitCount, "native queued task cancels old burst ownership"); Eq(Knight.State.Charge, k._fsm._queuedState, "queued task preserved");
        });
        Test("Night same Stop callback external goal wins over later handoff", () => {
            Managers.Inst.kingdom.isDaytime = false; var k = NewKnight(8); Follower(k, 0); Enemy(k, 13); UpdateHook(k);
            k._mover.OnStop = () => k._mover.SetGoal(70, 4);
            k.transform.position = new(11); UpdateHook(k); UpdateHook(k);
            Eq(70f, k._mover._goalPosition, "callback goal preserved"); Eq(0, k._fsm.Requests, "no native override queued");
        });
    }
    private static void Main()
    {
        NightRegressions();
        Test("No enemy and native attack cooldown do not prevent >10 return", () => {
            var k = NewKnight(20); Follower(k, 0); k._cooldown = 2.8f;
            AssertStartedReturn(k, 0); Eq(0, k._enemyScanner.Calls, "no enemy search before return");
        });
        Test("Return takes priority over still-running attack cooldown", () => {
            var k = NewKnight(); var follower = Follower(k, 0); Enemy(k, 3);
            UpdateHook(k); Check(k._damageable.invulnerable, "attack actually started");
            Frames(40); k._enemyScanner.Closest = null; Physics2D.Hits = Array.Empty<Collider2D>();
            k.transform.position = new(20); follower.transform.position = new(0); k._cooldown = 3;
            Frames(12, .02f, false); AssertStartedReturn(k, 0);
        });
        Test("10/4 hysteresis and ordinary completion", () => {
            var k = NewKnight(10); var f = Follower(k, 0); UpdateHook(k); Eq(0, k._mover.GoalWrites, "exact 10 does not enter");
            k.transform.position = new(10.01f); Frames(12, .02f, false); Check(k._mover.GoalWrites > 0, "above 10 enters");
            k.transform.position = new(4); Frame(.02f, false); Check(ShouldSlash(k), "exact 4 completes return");
            Eq(false, k._damageable.invulnerable, "completion restores vulnerability");
            int starts = Scheduler.Started, writes = k._mover.GoalWrites; k.transform.position = new(8); Frames(30, .02f, false);
            Eq(starts, Scheduler.Started, "8 does not restart burst"); Eq(writes, k._mover.GoalWrites, "8 does not reissue return goals");
        });
        Test("Return burst applies normal attack damage exactly once to an in-window target", () => {
            var k = NewKnight(20); Follower(k, 0); var enemy = Enemy(k, 18);
            AssertStartedReturn(k, 0); Eq(1, enemy.HitCount, "entry frame hits"); Frames(30, .01f);
            Eq(1, enemy.HitCount, "one hit for whole burst"); Eq(k._attackDamage, enemy.TotalDamage, "normal attack damage"); Eq(1.2f, Physics2D.LastRadius, "same forward hit radius");
        });
        Test("Active return does not restart visual burst every frame", () => {
            var k = NewKnight(20); Follower(k, 0); AssertStartedReturn(k, 0); int glows = SamuraiDashVisuals.BeginCount(k);
            Frames(10, .01f, false); Eq(glows, SamuraiDashVisuals.BeginCount(k), "one visual burst while actively returning");
        });
        Test("Inside hysteresis band active return cannot also start attacking", () => {
            var k = NewKnight(12); Follower(k, 0); var enemy = Enemy(k, 5); AssertStartedReturn(k, 0);
            k.transform.position = new(8); int glows = SamuraiDashVisuals.BeginCount(k); Frames(25, .01f, false);
            Eq(glows, SamuraiDashVisuals.BeginCount(k), "no concurrent attack or repeated visual burst"); Eq(1, enemy.HitCount, "one return-motion hit without concurrent attack"); Check(!ShouldSlash(k), "still returning above 4");
        });
        Test("Long return ends invulnerable burst within 0.6 seconds then runs normally", () => {
            var k = NewKnight(25); Follower(k, 0); AssertStartedReturn(k, 0); Check(k._damageable.invulnerable, "initial return burst");
            Frames(61, .01f); Eq(false, k._damageable.invulnerable, "burst vulnerability restored"); Eq(false, k._trail.enabled, "burst trail restored");
            Check(k._mover.InvulnerableDistance <= 7.2f, "invulnerable distance bounded by seven plus one physics step");
            Eq(k._runSpeed, k._mover._goalSpeed, "remaining return uses native run speed");
            Check(k.transform.position.x > 4, "ordinary running phase was necessary");
            for (int i = 0; i < 270 && !ShouldSlash(k); i++) Frame(.01f);
            Check(k.transform.position.x <= 4.1f, "longer than 17 gap closes with ordinary running");
        });
        Test("Normal 10-to-11 gap closes in one bounded white-trail burst", () => {
            var k = NewKnight(10.8f); Follower(k, 0); AssertStartedReturn(k, 0);
            Check(k._damageable.invulnerable && k._trail.enabled, "visible protected starting burst");
            int glows = SamuraiDashVisuals.BeginCount(k);
            for (int i = 0; i < 60 && !ShouldSlash(k); i++) Frame(.01f);
            Check(k.transform.position.x <= 4.01f, "normal gap closes to exit threshold");
            Eq(glows, SamuraiDashVisuals.BeginCount(k), "one burst only"); Eq(false, k._damageable.invulnerable, "finished burst restored protection");
            Check(Time.time <= .61f, "normal return completes within burst window");
        });
        foreach (var interrupt in new (string Name, Action<Knight> Apply)[] {
            ("config", k => ModConfig.Enabled.Value = false), ("style", k => k.Style = 1),
            ("authority", k => NetworkBigBoss.HasWorldAuth = false), ("manual control", k => k.ControlRequested = true),
            ("already controlled", k => k._beingControlled = true), ("retreating", k => k.isRetreating = true),
            ("embarked", k => k._embarkee.IsEmbarked = true), ("embark target", k => k._embarkee.IsTargetingEmbarkable = true),
            ("formation", k => k.Formation = new()), ("dead", k => k._damageable.isDead = true),
            ("charging", k => k.isCharging = true), ("charge pending", k => k._shouldCharge = true),
            ("inert", k => k._character.inert = true), ("grabbed", k => k._character.grabbed = true),
            ("stationary", k => k._character.isStationary = true), ("other FSM task", k => k._fsm.Current = (int)Knight.State.GrabCoin)
        }) Test("Return interruption: " + interrupt.Name, () => {
            var k = NewKnight(20); Follower(k, 0); AssertStartedReturn(k, 0); interrupt.Apply(k); Frame(.02f, false);
            Eq(false, k._damageable.invulnerable, "interruption releases invulnerability"); Eq(false, k._trail.enabled, "interruption releases trail");
            Check(ShouldSlash(k), "no stale return-specific slash suppression");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "still-owned motion stopped");
        });
        Test("Config-off OnDisable is reachable and restores preexisting flags", () => {
            var k = NewKnight(20); Follower(k, 0); k._damageable.invulnerable = true; k._trail.enabled = true;
            AssertStartedReturn(k, 0); ModConfig.Enabled.Value = false; DisableHook(k); Scheduler.StopOwnerSilently(k);
            Eq(true, k._damageable.invulnerable, "preexisting invulnerability preserved"); Eq(true, k._trail.enabled, "preexisting trail preserved");
            Check(ShouldSlash(k), "no stale owner after disable");
        });
        Test("Ordinary end of burst preserves preexisting invulnerability and trail", () => {
            var k = NewKnight(25); Follower(k, 0); k._damageable.invulnerable = true; k._trail.enabled = true;
            AssertStartedReturn(k, 0); Frames(65, .01f); Eq(true, k._damageable.invulnerable, "initial invulnerability remains"); Eq(true, k._trail.enabled, "initial trail remains");
        });
        Test("External new position goal is never stopped or overwritten by cleanup", () => {
            var k = NewKnight(20); Follower(k, 0); AssertStartedReturn(k, 0);
            k._mover.SetGoalNoHaglet(100, 3); int stops = k._mover.StopCalls, writes = k._mover.GoalWrites;
            Frame(.02f, false); Eq(stops, k._mover.StopCalls, "no stop of external goal"); Eq(writes, k._mover.GoalWrites, "no overwrite");
            Eq(100f, k._mover._goalPosition, "external position retained"); Eq(3f, k._mover._goalSpeed, "external speed retained");
            Eq(false, k._damageable.invulnerable, "motion loss releases buff"); Check(ShouldSlash(k), "motion loss exits return");
        });
        Test("Cleanup respects an externally cleared preexisting invulnerability flag", () => {
            var k = NewKnight(25); Follower(k, 0); k._damageable.invulnerable = true; k._trail.enabled = true;
            AssertStartedReturn(k, 0); k._damageable.invulnerable = false; k._trail.enabled = false; ModConfig.Enabled.Value = false;
            Frame(.02f, false); Eq(false, k._damageable.invulnerable, "external false is not overwritten by saved true"); Eq(false, k._trail.enabled, "external trail false retained");
        });
        Test("External object goal and replacement mover survive cleanup", () => {
            var k = NewKnight(20); Follower(k, 0); AssertStartedReturn(k, 0); var old = k._mover;
            old.goalMode = Mover.GoalMode.Object; old._goalObject = new GameObject(); old._goalSpeed = 5;
            Frame(.02f, false); Eq(0, old.StopCalls, "external object goal not stopped");
            DisableHook(k); k._mover = k.gameObject.AddComponent<Mover>(); k._mover.SetGoalNoHaglet(80, 4);
            Scheduler.Advance(); Eq(0, k._mover.StopCalls, "replacement mover not stopped by old routine"); Eq(80f, k._mover._goalPosition, "replacement goal retained");
        });
        Test("External goal installed at burst deadline survives transition to running", () => {
            var k = NewKnight(25); Follower(k, 0); AssertStartedReturn(k, 0);
            k._mover.SetGoalNoHaglet(100, 3); int writes = k._mover.GoalWrites, stops = k._mover.StopCalls;
            Time.time = .61f; Time.deltaTime = .61f; Time.frameCount++; Scheduler.Advance(); UpdateHook(k);
            Eq(writes, k._mover.GoalWrites, "transition never overwrites external position"); Eq(stops, k._mover.StopCalls, "transition never stops external position");
            Eq(100f, k._mover._goalPosition, "deadline external goal retained"); Eq(false, k._damageable.invulnerable, "deadline releases burst visuals");
        });
        Test("Replacement mover identity interrupts return without stopping old or new external owner", () => {
            var k = NewKnight(25); Follower(k, 0); AssertStartedReturn(k, 0); var old = k._mover;
            k._mover = k.gameObject.AddComponent<Mover>(); k._mover.SetGoalNoHaglet(100, 3);
            Frame(.02f, false); Eq(0, old.StopCalls, "cleanup does not stop no-longer-owned mover"); Eq(0, k._mover.StopCalls, "new mover not stopped");
            Eq(100f, k._mover._goalPosition, "new mover goal retained"); Eq(false, k._damageable.invulnerable, "mover replacement releases burst visuals");
            Check(ShouldSlash(k), "mover replacement releases return state");
        });
        Test("No followers means no invented return destination", () => {
            var k = NewKnight(20); Frames(30); Eq(0, k._mover.GoalWrites, "no follower destination"); Eq(0, Scheduler.Started, "no follower burst");
        });
        foreach (var change in new (string Name, Action<Archer> Apply)[] {
            ("death", f => f._damageable.isDead = true), ("leaves squad", f => f._knight = null),
            ("inactive", f => f.gameObject.activeInHierarchy = false), ("destroyed", f => f.Destroyed = true)
        }) Test("Active follower invalidation: " + change.Name, () => {
            var k = NewKnight(20); var f = Follower(k, 0); AssertStartedReturn(k, 0); change.Apply(f); Frame(.02f, false);
            Eq(false, k._damageable.invulnerable, "missing follower releases invulnerability"); Check(ShouldSlash(k), "missing follower exits return"); Eq(Mover.GoalMode.Off, k._mover.goalMode, "missing follower stops owned goal");
        });
        Test("Nearest alive owned follower selected; active target follows changing position", () => {
            var k = NewKnight(20); var invalid = Follower(k, 19); invalid._damageable.isDead = true;
            var foreign = Follower(NewKnight(100), 19); var f = Follower(k, 0); AssertStartedReturn(k, 0);
            f.transform.position = new(-3); Frames(35, .02f);
            Check(MathF.Abs(k._mover._goalPosition - f.transform.position.x) <= 3.1f, "goal tracks moving valid follower");
        });
        Test("Stuck return times out and backs off instead of restarting each frame", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.Blocked = true; AssertStartedReturn(k, 0);
            Frames(170); Eq(false, k._damageable.invulnerable, "no permanent invulnerability while stuck");
            Check(SamuraiDashVisuals.BeginCount(k) <= 3, "bounded number of blocked bursts over 3.4 seconds");
            int glows = SamuraiDashVisuals.BeginCount(k); Frames(5); Check(SamuraiDashVisuals.BeginCount(k) - glows <= 1, "no per-frame retry loop");
        });
        Test("Continuously receding follower cannot keep one return episode alive indefinitely", () => {
            var k = NewKnight(25); var follower = Follower(k, 0); AssertStartedReturn(k, 0);
            int steps = 0;
            while (!ShouldSlash(k) && steps++ < 165) { follower.transform.position = new(follower.transform.position.x - .2f); Frame(.02f); }
            Check(ShouldSlash(k), "first return episode bounded to roughly three scaled seconds despite ongoing movement");
            Eq(false, k._damageable.invulnerable, "timed-out episode releases protection");
            int glows = SamuraiDashVisuals.BeginCount(k); Frames(5); Eq(glows, SamuraiDashVisuals.BeginCount(k), "timed-out episode backs off");
        });
        Test("Pause starts no burst; active pause consumes neither scaled time nor deadline", () => {
            var k = NewKnight(20); Follower(k, 0); Time.timeScale = 0; Time.deltaTime = 0;
            Frames(30, 0); Eq(0, Scheduler.Started, "no paused burst start"); Eq(false, k._damageable.invulnerable, "no paused initial invulnerability");
            Time.timeScale = 1; Time.deltaTime = .02f; AssertStartedReturn(k, 0); float stamp = Time.time; int glows = SamuraiDashVisuals.BeginCount(k);
            Time.timeScale = 0; Frames(100, 0); Eq(stamp, Time.time, "scaled time unchanged"); Eq(glows, SamuraiDashVisuals.BeginCount(k), "no burst restart while paused");
            Time.timeScale = 1; Frames(61, .01f); Eq(false, k._damageable.invulnerable, "burst ends after resumed scaled time");
        });
        Test("External mover pause is not cleared", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover._pauseTimeout = 2; UpdateHook(k);
            Check(k._mover._pauseTimeout >= 2, "external pause unchanged by start"); Frames(5, .02f, false);
            Check(k._mover._pauseTimeout >= 2, "return never unpauses external owner");
        });
        Test("Return disable and optional late coroutine cleanup cannot erase pool-reused owner", () => {
            var k = NewKnight(20); Follower(k, 0); AssertStartedReturn(k, 0);
            var old = Scheduler.All.LastOrDefault(c => ReferenceEquals(c.Owner, k) && c.Active); DisableHook(k); if (old != null) Scheduler.StopSilently(old);
            k.transform.position = new(20); Time.time += .25f; Time.frameCount++; AssertStartedReturn(k, 0);
            int stops = k._mover.StopCalls; float goal = k._mover._goalPosition;
            if (old?.Iterator is IDisposable disposable) disposable.Dispose();
            Eq(stops, k._mover.StopCalls, "old finally cannot stop new goal"); Eq(goal, k._mover._goalPosition, "new goal retained");
            Eq(true, k._damageable.invulnerable, "new burst invulnerability retained"); Check(!ShouldSlash(k), "new owner remains returning");
        });
        Test("Whole follower cache is not searched every active return frame", () => {
            var k = NewKnight(30); Follower(k, 0); k._mover.Blocked = true; AssertStartedReturn(k, 0);
            int calls = UnitScanCache.Calls; Frames(30, .01f); Check(UnitScanCache.Calls - calls <= 3, "at most interval-based cache lookups over 0.3 seconds");
        });
        Test("Whole follower cache is not searched every attack dash frame", () => {
            var k = NewKnight(); Follower(k, 0); Enemy(k, 3); UpdateHook(k); int calls = UnitScanCache.Calls;
            Frames(20, .01f); Check(UnitScanCache.Calls - calls <= 3, "attack leash uses interval checks, not whole-array lookup each frame");
        });
        Test("OnDisable during attack releases owned invulnerability and trail", () => {
            var k = NewKnight(); Follower(k, 0); Enemy(k, 3); UpdateHook(k); Check(k._damageable.invulnerable, "attack actually active");
            DisableHook(k); Scheduler.StopOwnerSilently(k);
            Eq(false, k._damageable.invulnerable, "attack disable restores invulnerability"); Eq(false, k._trail.enabled, "attack disable restores trail");
        });
        Test("OnDisable during attack preserves preexisting invulnerability and trail", () => {
            var k = NewKnight(); Follower(k, 0); Enemy(k, 3); k._damageable.invulnerable = true; k._trail.enabled = true;
            UpdateHook(k); DisableHook(k); Scheduler.StopOwnerSilently(k);
            Eq(true, k._damageable.invulnerable, "attack disable retains original invulnerability"); Eq(true, k._trail.enabled, "attack disable retains original trail");
        });
        Test("Attack cleanup also preserves a newer external mover destination", () => {
            var k = NewKnight(); Follower(k, 0); Enemy(k, 3); UpdateHook(k);
            k._mover.SetGoalNoHaglet(100, 4); int stops = k._mover.StopCalls;
            Time.time = .61f; Time.deltaTime = .61f; Time.frameCount++; Scheduler.Advance(); UpdateHook(k);
            Eq(stops, k._mover.StopCalls, "attack tail does not stop external goal"); Eq(100f, k._mover._goalPosition, "external attack-tail goal preserved");
        });
        Test("Optional late attack coroutine finally cannot clear new return owner after reuse", () => {
            var k = NewKnight(); var follower = Follower(k, 0); Enemy(k, 3); UpdateHook(k);
            var old = Scheduler.All.LastOrDefault(c => ReferenceEquals(c.Owner, k) && c.Active);
            DisableHook(k); if (old != null) Scheduler.StopSilently(old); k.transform.position = new(20); k._enemyScanner.Closest = null;
            Time.time = .25f; Time.frameCount++; AssertStartedReturn(k, 0); int stops = k._mover.StopCalls;
            if (old?.Iterator is IDisposable disposable) disposable.Dispose();
            Eq(stops, k._mover.StopCalls, "old attack cannot stop return goal"); Eq(true, k._damageable.invulnerable, "old attack cannot clear new return invulnerability");
            Eq(true, k._trail.enabled, "old attack cannot clear new return trail"); Check(!ShouldSlash(k), "new return still owns state");
        });
        foreach (bool returning in new[] { false, true })
        {
            string direction = returning ? "Return" : "Forward";
            Test(direction + " burst retains same-frame damage to two different valid enemies", () => {
                var k = PrepareBurst(returning); var first = HitTarget(); var second = HitTarget(); Supply(first, second);
                UpdateHook(k); Eq(1, first.HitCount, "first target hit on entry frame"); Eq(1, second.HitCount, "second distinct target also hit on same entry frame");
                Eq(k._attackDamage, first.TotalDamage, "first ordinary damage"); Eq(k._attackDamage, second.TotalDamage, "second ordinary damage");
            });
            Test(direction + " burst deduplicates multiple colliders per Damageable and reuses its scan buffer", () => {
                var k = PrepareBurst(returning); var target = HitTarget(); var immune = HitTarget(); immune.CanBeDamaged = false;
                Physics2D.Hits = new[] { target.GetComponent<Collider2D>(), target.gameObject.AddComponent<Collider2D>(), immune.GetComponent<Collider2D>() };
                UpdateHook(k); Eq(1, target.HitCount, "first frame hits once despite duplicate colliders");
                Frames(20, .01f, false); Eq(1, target.HitCount, "later frames do not hit the same target again"); Eq(0, immune.HitCount, "immune target filtered");
                Eq(k._attackDamage, target.TotalDamage, "damage equals ordinary attack once"); Eq(k.gameObject, target.LastAttacker, "same attacker attribution");
                Eq(DamageSource.Knight, target.LastSource, "Knight damage source"); Eq(1.2f, Physics2D.LastRadius, "shared radius"); Eq(1, Physics2D.Buffers.Count, "one buffer for this motion, not one per frame");
            });
            Test(direction + " burst can hit a new target entering the supplied physics window later", () => {
                var k = PrepareBurst(returning); var first = HitTarget(); var late = HitTarget(); Supply(first); UpdateHook(k);
                Frames(5, .02f, false); Supply(first, late); Frame(.02f, false);
                Eq(1, first.HitCount, "initial target retains one hit"); Eq(1, late.HitCount, "newly entered target hit once");
            });
            Test(direction + " burst stops damage at timeout before a newly supplied target", () => {
                var k = PrepareBurst(returning); UpdateHook(k); var late = HitTarget(); Supply(late);
                Time.time = .61f; Time.deltaTime = .61f; Time.frameCount++; UpdateHook(k); Scheduler.Advance();
                Eq(0, late.HitCount, "no late timeout damage");
            });
            Test(direction + " burst stops damage at seven-unit travel boundary", () => {
                var k = PrepareBurst(returning); UpdateHook(k); var late = HitTarget(); Supply(late);
                float x = k.transform.position.x; k.transform.position = new(x + (returning ? -7 : 7));
                Frame(.1f, false); Eq(0, late.HitCount, "no hit at completed travel boundary");
            });
            Test(direction + " paused burst does no scans or damage", () => {
                var k = PrepareBurst(returning); UpdateHook(k); var late = HitTarget(); Supply(late); int scans = Physics2D.Scans;
                Time.timeScale = 0; Frames(5, 0, false); Eq(0, late.HitCount, "paused target not damaged"); Eq(scans, Physics2D.Scans, "no paused hit scans");
            });
            foreach (var interrupt in new (string Name, Action<Knight> Apply)[] {
                ("OnDisable", k => DisableHook(k)),
                ("external goal", k => k._mover.SetGoalNoHaglet(100, 3)),
                ("authority loss", k => NetworkBigBoss.HasWorldAuth = false),
                ("manual control", k => k.ControlRequested = true),
                ("config loss", k => ModConfig.Enabled.Value = false)
            }) Test(direction + " damage callback interrupts old frame: " + interrupt.Name, () => {
                var k = PrepareBurst(returning); var first = HitTarget(); var second = HitTarget(); Supply(first, second);
                first.OnReceiveDamage = _ => interrupt.Apply(k);
                UpdateHook(k); Eq(1, first.HitCount, "first callback actually invoked"); Eq(0, second.HitCount, "old frame stops before second target");
                if (interrupt.Name == "external goal") { Eq(100f, k._mover._goalPosition, "new external destination retained"); Eq(3f, k._mover._goalSpeed, "new external speed retained"); }
                Frame(.02f, false); Eq(0, second.HitCount, "no resumed old-frame damage after interruption");
            });
            Test(direction + " ReceiveDamage callback replacing the motion leaves new return goal and effects intact", () => {
                var k = PrepareBurst(returning); var first = HitTarget(); var second = HitTarget(); Supply(first, second);
                float replacementGoal = float.NaN; int replacementStops = -1;
                first.OnReceiveDamage = _ => {
                    first.OnReceiveDamage = null; DisableHook(k); Scheduler.StopOwnerSilently(k);
                    k.transform.position = new(20); k._enemyScanner.Closest = null; Physics2D.Hits = Array.Empty<Collider2D>();
                    UpdateHook(k); replacementGoal = k._mover._goalPosition; replacementStops = k._mover.StopCalls;
                };
                UpdateHook(k); Eq(1, first.HitCount, "reentrant callback executed"); Eq(0, second.HitCount, "old frame cannot damage second target");
                Eq(replacementGoal, k._mover._goalPosition, "replacement goal unchanged after old caller returns"); Eq(replacementStops, k._mover.StopCalls, "old caller does not stop new motion");
                Eq(true, k._damageable.invulnerable, "replacement effects still owned"); Eq(true, k._trail.enabled, "replacement trail retained"); Check(!ShouldSlash(k), "new return remains active");
            });
        }
        Test("Forward and subsequent independent return may each hit the same Damageable once", () => {
            var k = PrepareBurst(false); var target = HitTarget(); Supply(target); UpdateHook(k); Eq(1, target.HitCount, "forward hit");
            Time.time = .61f; Time.deltaTime = .61f; Time.frameCount++; Scheduler.Advance(); UpdateHook(k);
            k.transform.position = new(20); k._enemyScanner.Closest = null; Frames(12, .02f, false);
            Eq(2, target.HitCount, "fresh return lease gets its own dedup set"); Eq(2 * k._attackDamage, target.TotalDamage, "one ordinary hit per separate motion");
        });
        Test("Ordinary return running phase has no hit scans or damage", () => {
            var k = NewKnight(25); Follower(k, 0); UpdateHook(k); Frames(61, .01f);
            Eq(k._runSpeed, k._mover._goalSpeed, "ordinary run phase reached"); Eq(false, k._damageable.invulnerable, "burst effects ended");
            var late = HitTarget(); Supply(late); int scans = Physics2D.Scans; Frames(20, .01f);
            Eq(0, late.HitCount, "ordinary run does not damage"); Eq(scans, Physics2D.Scans, "ordinary run does not scan hits");
        });
        Test("Arrival inside four while burst still valid may hit once, never after completed return", () => {
            var k = NewKnight(10.5f); Follower(k, 0); UpdateHook(k); var arrival = HitTarget(); Supply(arrival);
            k.transform.position = new(4); Frame(.3f, false); Eq(1, arrival.HitCount, "valid final burst arrival frame hits");
            var late = HitTarget(); Supply(late); Frame(.02f, false); Eq(0, late.HitCount, "already-ended return cannot hit new target");
        });
        Test("Finish Stop callback replacing motion within same actor cannot be erased by old finally", () => {
            var k = NewKnight(20); var oldFollower = Follower(k, 0); UpdateHook(k); float replacementGoal = float.NaN;
            k._mover.OnStop = () => {
                k._mover.OnStop = null;
                // Nested Tick sees the stopped old goal and retires that motion, retaining ActorState.
                k.transform.position = new(20); UpdateHook(k);
                // A genuinely new valid follower lets normal target selection start a new episode.
                oldFollower._knight = null; Follower(k, 0); Time.time += .25f;
                UpdateHook(k); replacementGoal = k._mover._goalPosition;
            };
            k.transform.position = new(4); Frame(.02f, false);
            Eq(replacementGoal, k._mover._goalPosition, "Stop callback new goal retained"); Eq(true, k._damageable.invulnerable, "old Finish finally keeps new effects"); Check(!ShouldSlash(k), "old Finish finally cannot erase new owner");
        });
        Test("Motion begins one visual token without old GlowOverlay call", () => {
            var k = PrepareBurst(false); UpdateHook(k); Eq(1, SamuraiDashVisuals.BeginCount(k), "one helper Begin");
            Eq(0, k._character.spriteFX.GlowCount, "old timed GlowOverlay removed");
            Check(SamuraiDashVisuals.Current.ContainsKey(k.gameObject.GetInstanceID()), "visual token held during burst");
        });
        Test("Return running tail ends its visual burst token", () => {
            var k = PrepareBurst(true); UpdateHook(k); var token = SamuraiDashVisuals.Current[k.gameObject.GetInstanceID()];
            Frames(61, .01f); Check(token.Ended, "End called at burst to running transition");
            Check(!SamuraiDashVisuals.Current.ContainsKey(k.gameObject.GetInstanceID()), "no body flash during ordinary running");
            Eq(1, SamuraiDashVisuals.EndCalls.Count(t => ReferenceEquals(t, token)), "one End per burst token");
        });
        Test("OnDisable prefix clears visuals before mover cleanup callbacks", () => {
            var k = PrepareBurst(false); UpdateHook(k); bool clearedBeforeStop = false;
            k._mover.OnStop = () => clearedBeforeStop = SamuraiDashVisuals.Clears.Contains(k);
            DisableHook(k); Check(clearedBeforeStop, "prefix Clear precedes native/motion cleanup");
            Check(!SamuraiDashVisuals.Current.ContainsKey(k.gameObject.GetInstanceID()), "no surviving active token");
        });
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed"); Environment.ExitCode = failed == 0 ? 0 : 1;
    }
}
