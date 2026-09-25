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
        Physics2D.Buffers.Clear(); Physics2D.LastRadius = 0; Physics2D.LastMask = 0;
        Scanner.ScanTargets.Clear(); Scanner.ScanCalls = 0; Scanner.LastLayers = 0;
        Scanner.LastRange = 0; Scanner.LastRangeBehind = 0; Scanner.LastHeight = 0; Scanner.LastExcludeDead = false;
        Time.time = 0; Time.deltaTime = .02f; Time.timeScale = 1; Time.frameCount = 0;
        ModConfig.Enabled.Value = true; NetworkBigBoss.HasWorldAuth = true; Managers.Inst = new();
        KingdomEnhancedPlugin.Instance.LogSource.Errors.Clear();
        KingdomEnhancedPlugin.Instance.LogSource.Infos.Clear();
        // The turn narrative is bounded by a session-global line budget; isolate it per test.
        typeof(PatchRoles_SamuraiPowerDash).GetField("TurnLogs", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, 0);
        // Same for the choreography's early-exit narrative and the per-controller HasState
        // memory: pose verdicts must be opted into per test, not inherited from earlier ones.
        typeof(PatchRoles_SamuraiPowerDash).GetField("ChoreoLogs", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, 0);
        (typeof(PatchRoles_SamuraiPowerDash).GetField("PowerSlashByController", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null) as System.Collections.IDictionary)?.Clear();
        try { action(); Eq(0, KingdomEnhancedPlugin.Instance.LogSource.Errors.Count, "production error logs"); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message); }
    }
    private static void InvokeHook(Type type, string name, Knight knight)
    { type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { knight }); }
    private static void UpdateHook(Knight knight) => InvokeHook(typeof(Knight_Update_SamuraiPowerDash_Patch), "Postfix", knight);
    private static void DisableHook(Knight knight) { var type = typeof(Knight_OnDisable_SamuraiPowerDash_Patch); type.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, new object[] { knight }); InvokeHook(type, "Postfix", knight); }
    private static bool TripActive(Knight knight) => PatchRoles_SamuraiPowerDash.IsChoreoActive(knight);
    // The native path the flag suppression exists for: Knight.Update calls ShouldSlash(), and the
    // native Slash() pauses the mover on its first line. The stub replays exactly that write, so
    // the suppression carries a real root-cause discriminator instead of a comment.
    private static void NativeSlashAttempt(Knight knight)
    {
        if (ShouldSlash(knight) && knight._mover != null) knight._mover._pauseTimeout = .5f;
    }
    // One complete choreographed trip in the stub world: the outbound leg arrives, the reverse
    // cut turns the same motion home and the finale closes it at the start point.
    private static void RunTripCycle(Knight k)
    {
        UpdateHook(k);                       // the trigger opens the trip toward ±7
        Frames(40);                          // outbound arrival + turn
        Frames(60);                          // home arrival + finale
        k.transform.position = new(0);
    }
    private static void CloseTrip(Knight k)
    {
        for (int i = 0; i < 400 && TripActive(k); i++) Frame(.02f);
        Check(!TripActive(k), "the choreography closed");
    }
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
        Physics2D.Hits = new[] { go.AddComponent<Collider2D>() }; Scanner.ScanTargets[knight.gameObject.GetInstanceID()] = go; return enemy;
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
        Check(ShouldSlash(knight), "a plain walk never suppresses the native slash");
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
        Test("Night trips are target-agnostic: no leash, home is the start point", () => {
            Managers.Inst.kingdom.isDaytime = false;
            var k = NewKnight(8); Follower(k, 0); Enemy(k, 13); UpdateHook(k);
            Check(k._damageable.invulnerable, "forward attack began");
            Eq(15f, k._mover._goalPosition, "the outbound goal is start + fixed seven");
            k.transform.position = new(13.6f); UpdateHook(k);      // the old attack leash distance: irrelevant now
            NativeFrame(k);
            Eq(0, k._fsm.Requests, "no native handoff while the trip runs");
            Frames(90);                                            // the trip turns and closes at homeX = 8
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "arrival released the goal");
            Check(!k._damageable.invulnerable && !k._trail.enabled, "effects retired at the close");
            Check(MathF.Abs(k.transform.position.x - 8f) <= .3f, "home is the start point, not the follower");
        });
        Test("The attack cooldown anchors at the trigger, not at the trip's end", () => {
            Managers.Inst.kingdom.isDaytime = false;
            var k = NewKnight(); Follower(k, 0); Enemy(k, 3); UpdateHook(k);
            Frames(60);                                            // the trip closes long before the cooldown
            Eq(0, k._fsm.Requests, "ordinary finish does not force wall");
            Time.time = 2.9f; Time.frameCount++; UpdateHook(k);
            Eq(1, Scheduler.Started, "inside the three seconds no new attack opens");
            Time.time = 3.05f; Time.frameCount++; UpdateHook(k);
            Eq(2, Scheduler.Started, "the trigger-time anchor opens the next attack");
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
        Test("Dusk retires the plain walk home without further dash hits", () => {
            var k = NewKnight(12); Follower(k, 0); UpdateHook(k);
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "the day walk owns a run-speed goal");
            Eq(k._runSpeed, k._mover._goalSpeed, "walk speed");
            Managers.Inst.kingdom.isDaytime = false; var enemy = Enemy(k, 10); UpdateHook(k);
            Eq(0, enemy.HitCount, "the walk home never damages");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "dusk released the walk goal");
            NativeFrame(k); Eq(1, k._fsm.Requests, "night native return takes over");
        });
        Test("Actor replacement with reused instance id cannot inherit old night request", () => {
            Managers.Inst.kingdom.isDaytime = false; var old = NewKnight(12); Follower(old, 0); UpdateHook(old);
            var replacement = NewKnight(12); replacement.gameObject.Id = old.gameObject.Id;
            NativeFrame(replacement); Eq(0, replacement._fsm.Requests, "new actor does not inherit old follower");
            Eq(0, old._fsm.Requests, "old state machine untouched");
        });
        Test("In-flight new native queued task keeps its queue and the trip keeps its hits", () => {
            Managers.Inst.kingdom.isDaytime = false; var k = NewKnight(); Follower(k, 0); Enemy(k, 3); UpdateHook(k);
            var enemy = HitTarget(); Supply(enemy); k._fsm.GoToState(Knight.State.Charge);
            Time.time = .1f; Time.frameCount++; Scheduler.Advance();
            Eq(1, enemy.HitCount, "a native queued task no longer silences the trip's hit round");
            Eq(Knight.State.Charge, k._fsm._queuedState, "queued task preserved");
            Check(TripActive(k), "the trip is still live");
        });
        Test("A night goal steal mid-dash is reclaimed by the every-frame re-assert", () => {
            Managers.Inst.kingdom.isDaytime = false; var k = NewKnight(0); Follower(k, 0); Enemy(k, 3); UpdateHook(k);
            k._mover.SetGoal(1, 2);                        // native GoToWall supplies its own goal
            Eq(1f, k._mover._goalPosition, "the steal is visible before the routine resumes");
            Frame(.02f, false);                            // the coroutine's own step re-asserts
            Eq(7f, k._mover._goalPosition, "the trip reclaimed its own goal within one frame");
            Eq(18f, k._mover._goalSpeed, "with the trip's own speed");
            Eq(0, k._mover.StopCalls, "the foreign goal is never stopped");
            NativeFrame(k);
            Eq(0, k._fsm.Requests, "the mod never queues over the trip");
            Check(k._damageable.invulnerable, "the trip kept its protection through the steal");
        });
    }
    // 2026-09-25 用户裁定：脱离随从只回普通步速 walk（追随冲刺/回退梯/无敌/命中/视觉
    // 全链删除）。本组钉住：>10 同帧开程、runSpeed 目标指向随从站位、到站释放、失随从/
    // 失所有权/黄昏收口、暂停门、不压制原生斩击、不产视觉 token、不碰 trail/姿态/朝向。
    private static void WalkHomeRegressions()
    {
        Test("A broken leash opens a plain run-speed walk home on the same tick", () => {
            var k = NewKnight(20); Follower(k, 0);
            UpdateHook(k);
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "walk issued a position goal");
            Eq(k._runSpeed, k._mover._goalSpeed, "walk uses the native run speed");
            Eq(2.5f, k._mover._goalPosition, "walk heads for the follower station");
            Check(!k._damageable.invulnerable, "a plain walk never gains protection");
            Check(!k._trail.enabled, "a plain walk never enables the trail");
            Eq(0, SamuraiDashVisuals.BeginCount(k), "no visual token for a walk");
            Check(ShouldSlash(k), "the walk never suppresses the native slash");
        });
        Test("The walk keeps a live goal while blocked and finishes when the path clears", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.Blocked = true; UpdateHook(k);
            int live = 0;
            for (int i = 0; i < 40; i++) { if (k._mover.goalMode == Mover.GoalMode.Position) live++; Frame(.02f, false); }
            Eq(40, live, "no tick leaves the stranded knight without a goal");
            Eq(0, SamuraiDashVisuals.BeginCount(k), "a stuck walk never spawns anything");
            k._mover.Blocked = false;
            // Arrival arithmetic: 16 u at the native run speed (6 u/s stub) ≈ 2.7 s = 134 frames.
            for (int i = 0; i < 900 && k.transform.position.x > 4.01f; i++) Frame(.02f);
            Check(k.transform.position.x <= 4.01f, "the walk itself reached the return threshold");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "arrival released the walk goal");
            Eq(0, SamuraiDashVisuals.BeginCount(k), "the whole walk never spawned visuals");
        });
        Test("Repeated hit-stun pauses end and restart the walk without any burst", () => {
            var k = NewKnight(20); Follower(k, 0); UpdateHook(k);
            for (int i = 0; i < 4; i++)
            {
                k._mover._pauseTimeout = 1f;      // native HandleOnReceiveDamage -> Mover.Pause(1f)
                Frame(.02f, false);               // the external pause ends the live walk
                Eq(Mover.GoalMode.Off, k._mover.goalMode, "the paused walk released its goal");
                k._mover._pauseTimeout = 0f;      // ... and expires in scaled time
                Time.time += 1f;
                Frame(.02f, false);               // the tick answers instead of locking
                Eq(Mover.GoalMode.Position, k._mover.goalMode, "the walk reopened");
            }
            Eq(0, SamuraiDashVisuals.BeginCount(k), "no bursts exist to retry");
            Eq(false, k._damageable.invulnerable, "no protection to leak");
        });
        Test("A walk home never takes a facing lease, a dash pose or a state Play", () => {
            var k = NewKnight(20); Follower(k, 0); k.side = Side.Left; UpdateHook(k);
            Eq(Mover.FacingMode.Ahead, k._mover.facingMode, "walk leaves the facing native");
            Eq(0, k._animator.TriggerCount, "walks never replay PowerSlash");
            Eq(0, k._animator.PlayCalls, "walks never Play a state");
            for (int i = 0; i < 900 && k.transform.position.x > 4.01f; i++) Frame(.02f);
            Check(k.transform.position.x <= 4.01f, "the walk reached the station");
            Eq(Mover.FacingMode.Ahead, k._mover.facingMode, "facing untouched across the whole walk");
            Eq(0, k._animator.TriggerCount, "no dash pose across the whole walk");
            Eq(0, k._animator.PlayCalls, "no state Play across the whole walk");
        });
        Test("A foreign fixed facing survives the whole walk", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.facingMode = Mover.FacingMode.Target;
            UpdateHook(k);
            Eq(Mover.FacingMode.Target, k._mover.facingMode, "native Target facing never taken over");
            Check(k._mover.facingTarget == null, "no invented facing target");
            ModConfig.Enabled.Value = false;
            Frame(.02f, false);
            Eq(Mover.FacingMode.Target, k._mover.facingMode, "cleanup never stomps a foreign facing");
        });
        foreach (var interrupt in new (string Name, Action<Knight> Apply)[] {
            ("formation", k => k.Formation = new()), ("retreating", k => k.isRetreating = true),
            ("charging", k => k.isCharging = true), ("charge pending", k => k._shouldCharge = true),
            ("dead", k => k._damageable.isDead = true), ("manual control", k => k.ControlRequested = true),
            ("config", k => ModConfig.Enabled.Value = false), ("authority", k => NetworkBigBoss.HasWorldAuth = false)
        }) Test("Walking withdrawal honours " + interrupt.Name, () => {
            var k = NewKnight(20); Follower(k, 0); UpdateHook(k);
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "walk live before the interruption");
            interrupt.Apply(k); int writes = k._mover.GoalWrites;
            Frames(30, .02f, false);
            Eq(writes, k._mover.GoalWrites, "no replacement goal after " + interrupt.Name);
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "walk goal released");
        });
        Test("Night hands the walk over: no mod goal, native guard-slot task only", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.Blocked = true; UpdateHook(k);
            Frames(40, .02f, false);   // the walk is live but blocked
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "day walk live before dusk");
            Managers.Inst.kingdom.isDaytime = false;
            int writes = k._mover.GoalWrites;
            Frames(90, .02f, false);
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "mod goal released at dusk");
            Eq(writes, k._mover.GoalWrites, "no walk goal after the night handoff");
            Eq(0, SamuraiDashVisuals.BeginCount(k), "no visual token at any point");
            k._mover.Blocked = false;
            k._fsm.OnEnter = state => {
                if (state == Knight.State.GoToWall) { k.isRetreating = true; k._mover.SetGoal(1, 2); }
            };
            NativeFrame(k);   // fixture stands in for native GoToWall's own guard-slot goal
            Eq(1, k._fsm.Requests, "one wall request for the guard slot");
            Eq(1f, k._mover._goalPosition, "the native task owns the way home");
            Eq(writes + 1, k._mover.GoalWrites, "only the native task wrote a goal");
            for (int i = 0; i < 60; i++) Frame(.02f, false);
            Eq(writes + 1, k._mover.GoalWrites, "the mod never fights the native guard-slot goal");
            Eq(1f, k._mover._goalPosition, "guard-slot destination unchanged");
        });
    }


    private static void ScanRegressions()
    {
        Test("A knight facing its home side still finds the enemy behind it", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, -3);
            UpdateHook(k);
            Eq(1, Scheduler.Started, "the enemy behind the knight opened an attack");
            Check(k._damageable.invulnerable, "the attack burst is running");
            Check(k._mover._goalPosition < 0, "the dash heads toward the enemy's own side");
            Eq(0, Scanner.LastLayers & 2, "the self-scan never asks for Wildlife");
            Eq(1, Scanner.LastLayers & 1, "the self-scan asks for the Enemies layer");
            Eq(10.5f, Scanner.LastRange, "the self-scan reaches the widened distance");
            Eq(2, Physics2D.LastMask & 2, "the shared damage scan keeps Wildlife");
        });
        Test("Attack selection keeps its own .2 s cadence and never queries the native scanner", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 1);   // inside the 1.5 aim floor: scans, never attacks
            Time.time = .25f; UpdateHook(k);
            Eq(1, Scanner.ScanCalls, "one self-scan at the cadence boundary");
            Frames(21, .01f, false);
            Eq(2, Scanner.ScanCalls, "one more scan once the interval elapses");
            Frames(18, .01f, false);
            Eq(2, Scanner.ScanCalls, "no scan inside the interval");
            Eq(0, k._enemyScanner.Calls, "the native scanner is never queried");
            Check(Scanner.LastRangeBehind > 0, "the self-scan also covers the knight's back");
            Eq(1f, Scanner.LastHeight, "the self-scan keeps the replaced scanner's own column");
            Eq(true, Scanner.LastExcludeDead, "dead targets are excluded on purpose");
        });
    }

    // 2026-09-25 编舞式往返：触发带 [1.5, 7+1.2]、固定 7 格出程的直接断言、命中穿透、
    // trail lifetime 写入/归还纪律（值纪律，无相位重基）与 turn 有界日志三态。
    private static void FixedDashRegressions()
    {
        Test("Near, very near and behind enemies all get the fixed seven-unit goal", () => {
            foreach (float enemyX in new[] { 3f, 1.6f })
            {
                var k = NewKnight(0); Follower(k, 0); Enemy(k, enemyX);
                UpdateHook(k);
                Eq(7f, k._mover._goalPosition, "enemy at " + enemyX + " no longer shortens the dash");
                Eq(Mover.GoalMode.Position, k._mover.goalMode, "the pinned dash owns a position goal");
                Eq(18f, k._mover._goalSpeed, "dash speed unchanged");
            }
            var behind = NewKnight(0); Follower(behind, 0); Enemy(behind, -3);
            UpdateHook(behind);
            Eq(-7f, behind._mover._goalPosition, "an enemy behind gets the mirrored fixed seven");
        });
        Test("The trigger band is [1.5, 7+1.2] and 9.0/10.5 enemies never fire", () => {
            float edge = 7f + 1.2f;
            foreach ((float enemyX, bool fires) in new[] { (1.4f, false), (1.5f, true), (edge, true), (edge + .05f, false), (9f, false), (10.5f, false) })
            {
                var k = NewKnight(0); Follower(k, 0); Enemy(k, enemyX);
                int started = Scheduler.Started;
                UpdateHook(k);
                Check(Scanner.ScanCalls > 0, "the band decision came from a real scan at x=" + enemyX);
                Eq(fires ? 1 : 0, Scheduler.Started - started, "trip at enemy x=" + enemyX);
                if (fires) Eq(7f, k._mover._goalPosition, "fixed seven from the start point for x=" + enemyX);
                else Eq(0, k._mover.GoalWrites, "no goal was issued for x=" + enemyX);
            }
        });
        Test("The trigger gate still rejects a retreating knight", () => {
            foreach (bool day in new[] { true, false })
            {
                Managers.Inst.kingdom.isDaytime = day;
                var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
                k.isRetreating = true;
                UpdateHook(k);
                Eq(0, Scheduler.Started, "no trip opens for a retreating knight (day=" + day + ")");
                Eq(0, k._mover.GoalWrites, "no goal is issued for a retreating knight (day=" + day + ")");
                Check(!k._damageable.invulnerable, "no protection is applied for a retreating knight (day=" + day + ")");
            }
        });
        Test("The pinned seven-unit dash pierces the near enemy and reaches the one behind it", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            Physics2D.Hits = Array.Empty<Collider2D>();
            var near = HitTarget(3); Supply(near);
            UpdateHook(k);
            Eq(7f, k._mover._goalPosition, "a near enemy no longer shortens the dash");
            Eq(1, near.HitCount, "the near target is struck on the way past");
            var deep = HitTarget(6); Supply(near, deep);
            Frames(15);                                     // .3 s: the knight runs on through the pair
            Eq(1, near.HitCount, "the passed target is not struck twice");
            Eq(1, deep.HitCount, "a target behind the near enemy is also reached once");
            Check(k.transform.position.x > 5f, "the knight really ran through the pair");
        });
        Test("The trip never writes the trail; ghosts only", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            k._trail.time = .4f;                            // the native default
            UpdateHook(k);
            Check(!k._trail.enabled, "the mod no longer enables the continuous trail");
            Eq(.4f, k._trail.time, "the mod no longer pins the lifetime");
            Frames(30);                                     // the outbound leg arrives; the turn fires in-trip
            Check(!k._trail.enabled, "the reverse cut still writes nothing");
            Eq(.4f, k._trail.time, "no lifetime write on the turn");
            Frames(40);                                     // the home leg closes the trip
            Check(!k._trail.enabled, "no enable at the close");
            Eq(.4f, k._trail.time, "no lifetime write at the close");
            Eq(1, SamuraiDashVisuals.BeginCount(k), "one ghost Begin per trip");
            Eq(1, SamuraiDashVisuals.EndCalls.Count, "one ghost End per trip");
        });
        Test("An externally enabled trail stays on through a whole trip", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            k._trail.enabled = true; k._trail.time = .7f;   // someone else's continuous trail
            UpdateHook(k);
            Frames(70);                                     // the trip closes at the start point
            Check(k._trail.enabled, "the external true is never switched off");
            Eq(.7f, k._trail.time, "the external lifetime is never rewritten");
        });
        Test("Turn logging is bounded to twelve lines at six seconds per knight", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            RunTripCycle(k);
            Eq(1, TurnLines(), "the first turn logs once");
            Time.time = 3.7f; Time.frameCount++;            // past the 3 s attack cooldown, inside the 6 s throttle
            RunTripCycle(k);
            Eq(1, TurnLines(), "a turn inside six seconds of the last line stays quiet");
            Time.time = 8.5f; Time.frameCount++;            // past both the cooldown and the throttle
            RunTripCycle(k);
            Eq(2, TurnLines(), "a turn past six seconds logs again");
            for (int i = 0; i < 12; i++) { Time.time = 15f + 7.5f * i; Time.frameCount++; RunTripCycle(k); }
            Eq(12, TurnLines(), "the session budget caps the turn narrative at twelve lines");
        });
    }

    // 2026-09-25b 真实控制器姿态契约：两腿直接 Play 已验证 fullPath 状态（腿前清 Land），
    // 终幕对仍在 PowerSlash 的同一 animator/controller 发一次 Land（含硬失效）；未知/被替换
    // 控制器退触发器且永不发 Land；死亡/别的状态/不可读动画机绝不覆盖。
    private static void PoseContractRegressions()
    {
        Test("A deferred Play followed by a same-frame startup failure still queues Land", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            k._animator.StateHash = Hash("Stand");
            k._animator.FullPathHash = Hash("Base Layer.Stand");
            int pending = 0;
            k._animator.OnPlay = (hash, layer, time) => { pending = hash; return false; };
            k.ThrowOnStartCoroutine = true;
            UpdateHook(k);
            Eq(Hash("Base Layer.PowerSlash"), pending, "PowerSlash is queued for the next animation evaluation");
            Check(!TripActive(k) && !k._damageable.invulnerable, "startup failure closes motion and protection");
            Eq(1, KingdomEnhancedPlugin.Instance.LogSource.Errors.Count, "startup failure was diagnosed");
            KingdomEnhancedPlugin.Instance.LogSource.Errors.Clear();
            Eq(1, k._animator.SetTriggers.Count(t => t == Hash("Land")), "Land must be ready when queued Play evaluates");
        });
        Test("Both legs play the verified state from frame zero with Land cleared first", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            Eq(1, k._animator.PlayCalls, "the outbound leg plays the verified state");
            Eq(Hash("Base Layer.PowerSlash"), k._animator.FullPathHash, "the play targets the full path");
            Eq(0, k._animator.TriggerCount, "the verified path never fires the PowerSlash trigger");
            Eq(2, k._animator.ResetCount, "old PowerSlash and Land are cleared before the leg");
            Eq(Hash("PowerSlash"), k._animator.ResetTriggers[0], "the cut trigger is cleared first");
            Eq(Hash("Land"), k._animator.ResetTriggers[1], "the exit trigger is cleared before playback");
            Frames(21);                                     // the outbound leg arrives; the turn fires
            Eq(2, k._animator.PlayCalls, "the reverse cut replays the state from frame zero");
            Eq(4, k._animator.ResetCount, "the turn cleared PowerSlash then Land");
            Eq(Hash("PowerSlash"), k._animator.ResetTriggers[^2], "the turn clears the finished cut first");
            Eq(Hash("Land"), k._animator.ResetTriggers[^1], "then the exit trigger before the replay");
            Eq(0, k._animator.TriggerCount, "still no trigger path on a verified controller");
        });
        Test("A complete close fires Land exactly once while sitting in PowerSlash", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            k._animator.FullPathHash = Hash("Base Layer.PowerSlash");   // settled in the slash state
            CloseTrip(k);
            Eq(1, k._animator.SetTriggers.Count(t => t == Hash("Land")), "Land fired exactly once");
            Eq(0, k._animator.SetTriggers.Count(t => t == Hash("PowerSlash")), "no PowerSlash trigger on the verified path");
        });
        Test("Land is withheld when another state took over or the animator is unreadable", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            Frames(21);                                    // the reverse leg has already replayed PowerSlash
            k._animator.StateHash = 999;                    // Block takes over during the home leg
            k._animator.FullPathHash = 999;
            CloseTrip(k);
            Eq(0, k._animator.SetTriggers.Count(t => t == Hash("Land")), "no Land over another state");

            var b = NewKnight(0); Follower(b, 0); Enemy(b, 3);
            UpdateHook(b);
            b._animator.StateHash = Hash("PowerSlash");
            b._animator.FullPathHash = Hash("Base Layer.PowerSlash");
            b._animator.enabled = false;                    // unreadable animator
            CloseTrip(b);
            Eq(0, b._animator.SetTriggers.Count(t => t == Hash("Land")), "no Land from an unreadable animator");
        });
        Test("A hard-invalidated trip still lands its stuck pose (config or style off)", () => {
            foreach (var hard in new (string Name, Action<Knight> Apply)[] {
                ("config", k => ModConfig.Enabled.Value = false),
                ("style", k => k.Style = 1)
            })
            {
                ModConfig.Enabled.Value = true;             // the config iteration above leaves it off
                var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
                UpdateHook(k);
                k._animator.StateHash = Hash("PowerSlash"); // the pose is stuck when the trip dies
                hard.Apply(k);
                Frames(3, .02f, false);
                Check(!TripActive(k), "the hard invalidation closed the trip (" + hard.Name + ")");
                Eq(1, k._animator.SetTriggers.Count(t => t == Hash("Land")),
                    "Land still recovered the pose (" + hard.Name + ")");
            }
        });
        Test("Death never receives Land", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            k._animator.StateHash = Hash("PowerSlash");
            k._damageable.isDead = true;
            Frames(3, .02f, false);
            Eq(0, k._animator.SetTriggers.Count(t => t == Hash("Land")), "death keeps the native Die resolution");
        });
        Test("A replaced controller gets no Land and no turn replay", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            k._animator.StateHash = Hash("PowerSlash");
            k._animator.FullPathHash = Hash("Base Layer.PowerSlash");
            int resets = k._animator.ResetCount;
            k._animator.runtimeAnimatorController = new RuntimeAnimatorController();  // swapped mid-trip
            CloseTrip(k);
            Eq(1, k._animator.PlayCalls, "only the original outbound leg played");
            Eq(0, k._animator.TriggerCount, "no trigger belongs to the replacement controller");
            Eq(resets, k._animator.ResetCount, "no reset belongs to the replacement controller");
            Eq(0, k._animator.SetTriggers.Count(t => t == Hash("Land")), "no Land on a controller never verified");
        });
        Test("Replacing the animator receives no old-trip animation writes", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3); UpdateHook(k);
            var replacement = new GameObject().AddComponent<Animator>();
            k._animator = replacement;
            CloseTrip(k);
            Eq(0, replacement.PlayCalls + replacement.TriggerCount + replacement.ResetCount,
                "the replacement animator receives no replay, trigger or reset");
        });
        Test("An initially unknown controller loses its fallback writes when replaced", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            k._animator.OnHasState = (layer, id) => false;
            UpdateHook(k);
            int triggers = k._animator.TriggerCount, resets = k._animator.ResetCount;
            k._animator.runtimeAnimatorController = new RuntimeAnimatorController();
            CloseTrip(k);
            Eq(triggers, k._animator.TriggerCount, "fallback is limited to the original controller");
            Eq(resets, k._animator.ResetCount, "no reset leaks to the replacement controller");
        });
        Test("An unknown controller falls back to the trigger and closes without Land", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            k._animator.OnHasState = (layer, id) => false;  // a controller without the state
            UpdateHook(k);
            Eq(0, k._animator.PlayCalls, "no Play on an unverified controller");
            Eq(1, k._animator.SetTriggers.Count(t => t == Hash("PowerSlash")), "the outbound leg used the trigger");
            k._animator.StateHash = Hash("PowerSlash");
            CloseTrip(k);
            Eq(2, k._animator.SetTriggers.Count(t => t == Hash("PowerSlash")), "the turn used the trigger too");
            Eq(0, k._animator.SetTriggers.Count(t => t == Hash("Land")), "the fallback path never receives Land");
            Eq(0, k._animator.PlayCalls, "still no Play");
        });
        Test("The HasState verdict is remembered per controller pointer", () => {
            var a = NewKnight(0); Follower(a, 0); Enemy(a, 3);
            int probes = 0;
            a._animator.OnHasState = (layer, id) => { probes++; return true; };
            UpdateHook(a);
            Eq(1, probes, "the first trip probed the controller");
            CloseTrip(a);
            Time.time = 3.2f; Time.frameCount++;            // past the cooldown for a fresh trip
            var b = NewKnight(0); Follower(b, 0); Enemy(b, 3);
            b._animator.runtimeAnimatorController = a._animator.runtimeAnimatorController;  // shared instance
            b._animator.OnHasState = (layer, id) => { probes++; return true; };
            UpdateHook(b);
            Eq(1, probes, "the shared controller answered from the cache");
            CloseTrip(b);
            Time.time += 3.2f; Time.frameCount++;
            var c = NewKnight(0); Follower(c, 0); Enemy(c, 3);   // its own controller instance
            c._animator.OnHasState = (layer, id) => { probes++; return false; };
            UpdateHook(c);
            Eq(2, probes, "a different controller family probes for itself");
            Eq(0, c._animator.PlayCalls, "its negative verdict took the trigger path");
        });
    }

    private static int Hash(string name) => Animator.StringToHash(name);


    private static int TurnLines() =>
        KingdomEnhancedPlugin.Instance.LogSource.Infos.Count(m => m.StartsWith("[SamuraiDash/roundtrip-turn]"));

    // 2026-09-25 编舞式往返：一次攻击 = 单协程完整动作（出程固定 7 格 → 反斩回家 → 终幕），
    // Tick 不再推进动作，只查硬上限。以下断言钉住：两条腿完整走完、回家点=出发 x、目标无关、
    // 同帧反向、命中回合重置、整段一条受保护动作、暂停迟滞、Tick 硬上限与 ShouldSlash 全程压制。
    private static void RoundTripPhases()
    {
        Test("A trip runs both legs, closes at the start point and ends one protected motion", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            Eq(7f, k._mover._goalPosition, "the outbound goal is the fixed seven");
            Eq(18f, k._mover._goalSpeed, "dash speed");
            Check(k._damageable.invulnerable, "one protected motion");
            Check(!k._trail.enabled, "no continuous trail: ghosts only");
            Check(SamuraiDashVisuals.Current.ContainsKey(k.gameObject.GetInstanceID()), "one visual token");
            int scans = Scanner.ScanCalls;
            Frames(80);                                     // outbound arrival, reverse cut, home arrival
            Eq(scans, Scanner.ScanCalls, "no further target scans during the trip");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "the trip released its goal");
            Check(!k._damageable.invulnerable && !k._trail.enabled, "effects retired at the close");
            Check(MathF.Abs(k.transform.position.x) <= .3f, "home is the start point");
            Check(!TripActive(k), "the flag cleared");
            Eq(1, SamuraiDashVisuals.BeginCount(k), "one Begin for the trip");
            Eq(1, SamuraiDashVisuals.EndCalls.Count, "one End for the trip");
        });
        Test("An enemy dying mid-trip changes nothing: no target survives the trigger", () => {
            var k = NewKnight(0); Follower(k, 0); var enemy = Enemy(k, 3);
            UpdateHook(k);
            Frames(5);
            enemy.isDead = true;                            // the chosen enemy dies mid-flight
            Scanner.ScanTargets.Clear();
            Physics2D.Hits = Array.Empty<Collider2D>();
            Frames(80);
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "both legs still ran to the close");
            Check(MathF.Abs(k.transform.position.x) <= .3f, "home is still the start point");
            Check(!k._damageable.invulnerable && !k._trail.enabled, "effects still retired");
            Check(!TripActive(k), "no target knowledge survived: the trip is target-agnostic");
        });
        Test("The turn replays the slash state, faces home and repairs the y scale", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3); k._animator.StateHash = 111;
            k.transform.localScale = new Vector3(-1, 1.3f, 1);
            UpdateHook(k);
            Frames(21);                                     // the outbound leg arrives; the turn fires in-trip
            Check(TripActive(k), "the return leg is still running");
            Eq(2, k._animator.PlayCalls, "the reverse cut replayed the verified state");
            Eq(0, k._animator.TriggerCount, "the verified path re-draws without any trigger");
            Eq(Hash("PowerSlash"), k._animator.ResetTriggers[^2], "the finished cut clears its trigger first");
            Eq(Hash("Land"), k._animator.ResetTriggers[^1], "then the exit trigger before the replay");
            Eq("play", k._animator.Ops[^1], "the replay is the turn's last animator write");
            Eq(Mover.FacingMode.Left, k._mover.facingMode, "the reverse cut faces the way home");
            Eq(1, k._mover.DirectionWrites, "SetDirection ran once at the turn");
            Eq(-1f, k.transform.localScale.x, "the travel direction lands on the x scale");
            Eq(1.3f, k.transform.localScale.y, "the native y wipe is repaired");
            Check(k._mover._goalPosition <= k.transform.position.x + .01f, "the goal reversed toward home");
            Eq(18f, k._mover._goalSpeed, "the reverse cut runs at dash speed");
            Eq(1, Scheduler.Started, "no second coroutine for the turn");
            Eq(1, SamuraiDashVisuals.BeginCount(k), "still the one token");
            Check(!ShouldSlash(k), "the whole trip suppresses the native slash");
        });
        Test("The reverse cut strikes again: the hit round resets at the turn", () => {
            var k = NewKnight(0); Follower(k, 0); var enemy = Enemy(k, 3);
            UpdateHook(k);
            Frames(5);
            Eq(1, enemy.HitCount, "the outbound round hits once");
            Frames(20);                                     // the outbound leg arrives; the turn opens a fresh round
            Eq(2, enemy.HitCount, "the reverse cut opens its own deduplicated round");
            Frames(3);
            Eq(2, enemy.HitCount, "the turn round stays deduplicated");
        });
        Test("Paused frames hold the trip without scans or damage; the resume continues it", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            var late = HitTarget(); Supply(late);
            int scans = Physics2D.Scans;
            Time.timeScale = 0;
            Frames(10, 0, false);
            Eq(scans, Physics2D.Scans, "no paused hit scans");
            Eq(0, late.HitCount, "no paused damage");
            Check(k._damageable.invulnerable && TripActive(k), "the paused trip keeps its flag and effects");
            Time.timeScale = 1;
            Time.time += .1f; Time.frameCount++;            // the hit cadence elapses on the resume
            Frames(2, .02f, false);
            Eq(1, late.HitCount, "the resumed outbound leg strikes the supplied target once");
        });
        Test("Two consecutive trips leak no effects (value discipline)", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            k._damageable.invulnerable = true; k._trail.enabled = true; k._trail.time = .4f;
            RunTripCycle(k);
            Eq(true, k._damageable.invulnerable, "preexisting invulnerability returned");
            Eq(true, k._trail.enabled, "an external trail stays on: the mod never writes it");
            Eq(.4f, k._trail.time, "the external lifetime is never rewritten");
            Time.time += 3.2f; Time.frameCount++; UpdateHook(k);   // past the trigger-anchored cooldown
            Check(TripActive(k), "the second trip opened");
            CloseTrip(k);
            Eq(true, k._damageable.invulnerable, "second trip returned the flag again");
            Eq(true, k._trail.enabled, "external trail survived the second trip");
            Eq(.4f, k._trail.time, "external lifetime survived the second trip");
            Eq(2, SamuraiDashVisuals.BeginCount(k), "two Begin calls, no leak");
            Eq(2, SamuraiDashVisuals.EndCalls.Count, "two Ends, no leak");
        });
        Test("The Tick hard cap closes a trip that cannot advance", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3); k._mover.Blocked = true;
            UpdateHook(k);
            Frames(10, .02f, false);                        // the trip is stuck at the start, still live
            Check(TripActive(k) && k._mover.goalMode == Mover.GoalMode.Position, "the stuck trip still owns a live goal");
            Time.time = 3.1f; Time.frameCount++; UpdateHook(k);   // one frame past the 3 s budget
            Check(!TripActive(k), "the cap closed the trip");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "the cap released the owned goal");
            Check(!k._damageable.invulnerable && !k._trail.enabled, "no effects outlive the cap");
            Check(!SamuraiDashVisuals.Current.ContainsKey(k.gameObject.GetInstanceID()), "the token ended with the cap");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Infos.Any(m =>
                m.StartsWith("[SamuraiDash/choreo]") && m.Contains("tick-cap")), "the cap left a budgeted reason line");
        });
        Test("The hard cap still closes a trip whose coroutine died under a soft flag", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3); k._mover.Blocked = true;
            UpdateHook(k);
            Scheduler.StopOwnerSilently(k);                  // a third party stopped it: no finally runs
            k.isRetreating = true;                           // the flag a soft gate would return on before the cap
            Frames(10, .02f, false);
            Check(TripActive(k), "a soft native flag never closes the trip by itself");
            Time.time = 3.1f; Time.frameCount++; UpdateHook(k);
            Check(!TripActive(k), "the cap closed the orphaned trip despite the soft flag");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "the cap released the owned goal");
            Check(!k._damageable.invulnerable && !k._trail.enabled, "no effects outlive the cap");
        });
        Test("Two samurai run their own trips independently toward their own start points", () => {
            var a = NewKnight(0); Follower(a, 0); Enemy(a, 3);
            var b = NewKnight(5); Follower(b, 5); Enemy(b, 2);
            UpdateHook(a); UpdateHook(b);
            Eq(2, Scheduler.Started, "both outbound dashes opened");
            Eq(7f, a._mover._goalPosition, "A heads seven from its own start");
            Eq(-2f, b._mover._goalPosition, "B heads seven toward its own enemy side");
            Frames(90);                                     // both trips close
            Eq(Mover.GoalMode.Off, a._mover.goalMode, "A finished");
            Eq(Mover.GoalMode.Off, b._mover.goalMode, "B finished");
            Check(MathF.Abs(a.transform.position.x) <= .3f, "A is home");
            Check(MathF.Abs(b.transform.position.x - 5f) <= .3f, "B is home");
            Eq(1, SamuraiDashVisuals.BeginCount(a), "A one token");
            Eq(1, SamuraiDashVisuals.BeginCount(b), "B one token");
        });
        Test("Follower death mid-trip changes nothing: the trip closes at its own start point", () => {
            var k = NewKnight(0); var f = Follower(k, 6); Enemy(k, 3);
            UpdateHook(k);
            Frames(3);
            f._damageable.isDead = true;                    // the follower dies mid-dash
            UnitScanCache.Archers = Array.Empty<Archer>();
            Frames(80);
            Check(!k._damageable.invulnerable && !k._trail.enabled, "the trip closed and retired its effects");
            Check(MathF.Abs(k.transform.position.x) <= .3f, "home is the start point, not the follower");
            Check(!TripActive(k), "the trip closed");
            Eq(0, k._fsm.Requests, "the mod never queued a wall task");
        });
        Test("A paused frame never opens a trip", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            Time.timeScale = 0; Time.deltaTime = 0; UpdateHook(k);
            Eq(0, Scheduler.Started, "no trip starts while paused");
            Check(!k._damageable.invulnerable, "no protection is applied while paused");
        });
        Test("A native slash attempt during the trip never pauses the mover (root-cause discriminator)", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            for (int i = 0; i < 80 && TripActive(k); i++) { NativeSlashAttempt(k); Frame(.02f); }
            Check(!TripActive(k), "the trip still completed with the native attempts on");
            Eq(0f, k._mover._pauseTimeout, "native Slash() never reached its Mover.Pause during the trip");
            Check(MathF.Abs(k.transform.position.x) <= .3f, "the trip came home");
            Check(!k._damageable.invulnerable && !k._trail.enabled, "effects retired normally");
        });
        Test("The slash suppression releases once the trip closes", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            CloseTrip(k);
            Check(!TripActive(k), "the trip closed");
            Check(ShouldSlash(k), "the prefix no longer suppresses");
            NativeSlashAttempt(k);
            Eq(.5f, k._mover._pauseTimeout, "the native slash is free again once the flag clears");
        });
    }

    // 中断矩阵（单相位：协程自行推进，无 Tick 相位）：编舞存续只认硬失效（actor 身份/enabled/
    // config/authority/style/mover/dead/inert/grabbed）——原生行为旗标（retreating/charging/
    // charge pending/formation/FSM/手动控制/embark 等）是"骑士在墙外"的正常反应，不再打断两腿；
    // 硬失效一律以终幕结束并归还效果；纯外目标不终幕（每帧重申下一协程帧夺回）；外部暂停迟滞不终幕。
    private static void RoundTripInterruptions()
    {
        foreach (var soft in new (string Name, Action<Knight> Apply)[] {
            ("retreating", k => k.isRetreating = true),
            ("charging", k => k.isCharging = true),
            ("charge pending", k => k._shouldCharge = true),
            ("formation", k => k.Formation = new()),
            ("other FSM task", k => k._fsm.Current = (int)Knight.State.GrabCoin),
            ("manual control", k => k.ControlRequested = true),
            ("being controlled", k => k._beingControlled = true),
            ("harmless", k => k._harmless = true),
            ("stationary", k => k._character.isStationary = true),
            ("embark target", k => k._embarkee.IsTargetingEmbarkable = true),
            ("pillar", k => k.helPuzzlePillar = new())
        }) Test("In-flight soft flag never cuts the trip: " + soft.Name, () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            Check(TripActive(k), "the trip opened");
            Frames(6);                                      // the outbound dash is under way
            soft.Apply(k);
            Frames(80);                                     // outbound arrival, reverse cut, home arrival
            Check(!TripActive(k), "the trip closed on its own terms despite " + soft.Name);
            Check(Mathf.Abs(k.transform.position.x) <= .3f, "the trip came home despite " + soft.Name);
            Check(!k._damageable.invulnerable && !k._trail.enabled, "the natural close retired the effects");
            Check(!SamuraiDashVisuals.Current.ContainsKey(k.gameObject.GetInstanceID()), "the visual token ended");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "the owned goal was released at the close");
        });
        foreach (var hard in new (string Name, Action<Knight> Apply)[] {
            ("config", k => ModConfig.Enabled.Value = false),
            ("authority", k => NetworkBigBoss.HasWorldAuth = false),
            ("dead", k => k._damageable.isDead = true),
            ("grabbed", k => k._character.grabbed = true),
            ("inert", k => k._character.inert = true),
            ("disabled", k => k.enabled = false),
            ("style changed", k => k.Style = 1),
            ("style unresolved", k => k.Qualified = false)
        }) Test("In-flight hard invalidation closes the trip: " + hard.Name, () => {
            var k = NewKnight(0); Follower(k, 6); Enemy(k, 3);
            UpdateHook(k);
            Check(TripActive(k), "the trip opened");
            hard.Apply(k);
            Frames(60, .02f, false);
            Check(!TripActive(k), "the hard invalidation closed the trip");
            Check(!k._damageable.invulnerable, "the close released invulnerability");
            Check(!k._trail.enabled, "the close released the trail");
            Check(!SamuraiDashVisuals.Current.ContainsKey(k.gameObject.GetInstanceID()), "the visual token ended");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "the owned goal was released");
        });
        Test("In-flight hard invalidation closes the trip: reused instance id (owner lost)", () => {
            var k = NewKnight(0); Follower(k, 6); Enemy(k, 3);
            UpdateHook(k);
            Check(TripActive(k), "the trip opened");
            var replacement = NewKnight(0);
            replacement.gameObject.Id = k.gameObject.Id;     // pooled reuse of the same instance id
            UpdateHook(replacement);
            Check(!TripActive(k), "the stale owner's trip closed on the identity mismatch");
            Check(!k._damageable.invulnerable && !k._trail.enabled, "the close released the stale owner's effects");
            Check(!TripActive(replacement), "the replacement knight inherited nothing");
            Eq(0, replacement._mover.GoalWrites, "the replacement mover received nothing");
        });
        Test("A hard invalidation names its clause in the bounded close line", () => {
            var k = NewKnight(0); Follower(k, 6); Enemy(k, 3);
            UpdateHook(k);
            k._damageable.isDead = true;
            Frames(2, .02f, false);
            Check(!TripActive(k), "the trip closed");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Infos.Any(m =>
                m.StartsWith("[SamuraiDash/choreo]") && m.Contains("step=hard-invalid:dead")),
                "the close line names the hard clause");
        });
        Test("In-flight interruption: a foreign goal is reclaimed the very next routine frame", () => {
            var k = NewKnight(0); Follower(k, 6); Enemy(k, 3);
            UpdateHook(k);
            k._mover.SetGoalNoHaglet(100, 3);               // native GoToWall takes the mover
            Eq(100f, k._mover._goalPosition, "the steal is real before the routine resumes");
            Frame(.02f, false);                             // the coroutine's own step re-asserts
            Eq(7f, k._mover._goalPosition, "the trip reclaimed its own goal within one frame");
            Eq(18f, k._mover._goalSpeed, "with the trip's own speed");
            Check(TripActive(k) && k._damageable.invulnerable, "the trip kept running through the steal");
            Eq(0, k._mover.StopCalls, "no stop was issued for the foreign goal");
        });
        Test("In-flight interruption: mover replacement closes with zero writes to the new mover", () => {
            var k = NewKnight(0); Follower(k, 6); Enemy(k, 3);
            UpdateHook(k);
            var old = k._mover;
            k._mover = k.gameObject.AddComponent<Mover>();
            Frames(3, .02f, false);
            Eq(0, old.StopCalls, "the replaced mover is not stopped");
            Eq(0, k._mover.GoalWrites, "the replacement mover receives nothing");
            Check(!k._damageable.invulnerable, "the close released its effects");
            Check(!TripActive(k), "the trip closed");
        });
        Test("In-flight interruption: an external mover pause latches instead of closing", () => {
            var k = NewKnight(0); Follower(k, 6); Enemy(k, 3);
            UpdateHook(k);
            float goal = k._mover._goalPosition;
            k._mover._pauseTimeout = 1f;                    // native hit-stun pause
            Frames(3, .02f, false);
            Check(TripActive(k), "the pause does not close the trip");
            Eq(goal, k._mover._goalPosition, "the live goal stays ours");
            Eq(0, k._mover.StopCalls, "no stop from the pause");
            Check(k._damageable.invulnerable, "the protection stays with the latched trip");
        });
    }

    // 夜跨黄昏：编舞往返不因入夜丢动作；出发点是本次动作的私有锚（随从/站位变化不影响）；
    // 期间原生夜墙队列让位，动作结束后夜列队恢复可用。
    private static void RoundTripNight()
    {
        Test("Dusk mid-trip keeps the trip and its own home anchor", () => {
            var k = NewKnight(0); var f = Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            Frames(5);                                      // the dash is under way
            Managers.Inst.kingdom.isDaytime = false;        // dusk falls mid-trip
            f.transform.position = new(12);                 // the squad walked to the wall: irrelevant to the trip
            Frames(25);                                     // the outbound leg arrives; the turn fires at dusk
            Check(k._damageable.invulnerable, "the trip survives dusk");
            Check(k._mover._goalPosition <= k.transform.position.x + .01f, "the turn targets its own start point");
            Eq(18f, k._mover._goalSpeed, "still the reverse cut");
            NativeFrame(k);
            Eq(0, k._fsm.Requests, "no native wall queue while the trip runs");
            Frames(60);
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "the trip closed at its anchor");
            Check(MathF.Abs(k.transform.position.x) <= .3f, "home is the start point, not the live station");
        });
        Test("A native goal steal at dusk is reclaimed, and the queue waits for the close", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            Managers.Inst.kingdom.isDaytime = false;
            k._mover.SetGoal(1, 2);                         // native GoToWall supplies its own guard-slot goal
            Frame(.02f, false);                             // one routine frame is enough now
            Eq(7f, k._mover._goalPosition, "the trip reclaimed its own goal over the native steal");
            Eq(0, k._mover.StopCalls, "the foreign goal was never stopped");
            NativeFrame(k);
            Eq(0, k._fsm.Requests, "the mod never queues over the trip");
            Check(TripActive(k), "the trip is still running");
        });
        Test("The night handoff resumes only after the trip closes", () => {
            var k = NewKnight(0); var f = Follower(k, 0); Enemy(k, 3);
            Managers.Inst.kingdom.isDaytime = false;
            UpdateHook(k);
            Eq(1, Scheduler.Started, "night attacks still dash");
            Frames(40);                                     // the outbound leg arrives; the turn fires
            NativeFrame(k);
            Eq(0, k._fsm.Requests, "no wall queue while the trip runs");
            Frames(60);                                     // the trip closes at its start point
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "the trip finished");
            f.transform.position = new(20);                 // the squad moved on
            Time.time += .25f; Time.frameCount++; NativeFrame(k);
            Eq(1, k._fsm.Requests, "the night handoff resumes once the trip is gone");
        });
        Test("A retreat onset during an outbound hit callback preserves remaining hits", () => {
            Managers.Inst.kingdom.isDaytime = false;
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            var first = HitTarget(); var second = HitTarget(); Supply(first, second);
            first.OnReceiveDamage = _ => k.isRetreating = true;
            UpdateHook(k);
            Eq(1, first.HitCount, "the first outbound hit sets retreating");
            Eq(1, second.HitCount, "the second outbound hit is not silenced by retreating");
            Check(TripActive(k), "the same trip survives the callback");
            CloseTrip(k);
            Eq(2, first.HitCount, "first target is hit once per leg");
            Eq(2, second.HitCount, "second target is hit once per leg");
            Check(Mathf.Abs(k.transform.position.x) <= .3f, "the trip finishes at home");
        });
        Test("Retreating first becoming true on the home leg still reaches home", () => {
            Managers.Inst.kingdom.isDaytime = false;
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3); UpdateHook(k);
            Frames(26);
            Check(TripActive(k), "home leg is active before the native retreat change");
            Check(Mathf.Abs(k._mover._goalPosition) <= .01f, "the reverse cut already targets home");
            k.isRetreating = true;
            CloseTrip(k);
            Check(Mathf.Abs(k.transform.position.x) <= .3f, "the retreating home leg reaches its start");
            Check(!k._damageable.invulnerable && !k._trail.enabled, "the finale restores effects");
        });
        Test("A retreat onset mid-outbound persists through home without cutting the trip or hits", () => {
            Managers.Inst.kingdom.isDaytime = false;
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            var target = HitTarget(3); Supply(target);
            UpdateHook(k);
            Check(TripActive(k), "the trip opened while retreating was still false");
            Frames(6);
            Eq(1, target.HitCount, "the outbound round struck once");
            k.isRetreating = true;                          // native GoToWall marks the knight at the wall line
            Frames(20);                                     // outbound arrival + the reverse cut under the flag
            Check(TripActive(k), "the home leg is still running under the retreat flag");
            Eq(2, target.HitCount, "the reverse cut struck under the flag: hits are not gated by retreating");
            Check(k._mover._goalPosition <= k.transform.position.x + .01f, "the turn targets the start point");
            Frames(30);                                     // home arrival
            Check(!TripActive(k), "both legs completed under the retreat flag");
            Check(Mathf.Abs(k.transform.position.x) <= .3f, "the trip came home to the start point");
            Eq(2, target.HitCount, "each leg kept its own deduplicated round");
        });
    }

    // 2026-09-25b 行程两端冻结与到点判别：越点判定（方向在腿起冻结）防大 dt 跳容差；
    // home 超时以 home-deadline 关闭（绝不谎报 complete），out 超时仍转身回原 home 但保留
    // 超时事实；每帧重申压掉逐帧偷写；回家目标恒为本次出发点，不追移动随从。
    private static void ChoreoEndpointRegressions()
    {
        Test("A big step that jumps past the goal plus tolerance still lands", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            k.transform.position = new(7.6f);               // one large dt overshot 7 by more than the tolerance
            Frame(.02f, false);
            Check(TripActive(k), "the outbound leg landed despite the overshoot");
            Eq(0f, k._mover._goalPosition, "the turn already targets home");
            k.transform.position = new(-.6f);               // the home leg overshoots the start point too
            Frame(.02f, false);
            Check(!TripActive(k), "the home leg landed despite the overshoot");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "the close released the goal");
        });
        Test("A blocked home leg closes as home-deadline, never as complete", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            Frames(21);                                     // the outbound leg arrives; the turn fires
            Check(TripActive(k), "the home leg is running");
            k._mover.Blocked = true;                        // a real wall blocks the way home
            Frames(70, .02f, false);                        // the home window expires blocked
            Check(!TripActive(k), "the home deadline closed the trip");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "the deadline released the owned goal");
            Check(!k._damageable.invulnerable, "the deadline restored the effects");
            var line = KingdomEnhancedPlugin.Instance.LogSource.Infos.LastOrDefault(m => m.StartsWith("[SamuraiDash/choreo]"));
            Check(line != null && line.Contains("step=home-deadline"), "the close names the deadline");
            Check(line != null && line.Contains("home=") && line.Contains("outGoal=") && line.Contains("turnX=") &&
                line.Contains("endX=") && line.Contains("elapsed="), "the close carries the endpoint fields");
        });
        Test("A blocked outbound leg still turns home with the timeout recorded", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3); k._mover.Blocked = true;
            UpdateHook(k);
            Frames(70, .02f, false);                        // the outbound window expires with the knight blocked at home
            Check(KingdomEnhancedPlugin.Instance.LogSource.Infos.Any(m =>
                m.StartsWith("[SamuraiDash/choreo]") && m.Contains("step=out-deadline")), "the outbound timeout left its line");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Infos.Any(m =>
                m.StartsWith("[SamuraiDash/choreo]") && m.Contains("turnTimedOut=True")), "the fact is carried on the line");
            Check(!TripActive(k), "the turned trip completed at home");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "the close released the goal");
        });
        Test("A moving follower never moves the home target", () => {
            var k = NewKnight(0); var f = Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            Frames(21);                                     // the turn targets the frozen home
            f.transform.position = new(19);                 // the squad walks on mid-trip
            f._damageable.isDead = true;                    // ... and dies before the close: no walk may chase it
            UnitScanCache.Archers = Array.Empty<Archer>();
            Frames(3);
            Eq(0f, k._mover._goalPosition, "the home goal stays the trip's own start point");
            Frames(60);
            Check(Mathf.Abs(k.transform.position.x) <= .3f, "the trip came home, not to the squad");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "nothing reopened after the close");
        });
        Test("Every-frame re-assert keeps the goal ours after per-frame steals", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            for (int i = 0; i < 12 && TripActive(k); i++)
            {
                k._mover.SetGoalNoHaglet(50, 3);            // a native writer steals every frame
                Frame(.02f, false);
                if (TripActive(k)) Eq(7f, k._mover._goalPosition, "the routine reclaimed the goal in the same frame");
            }
            Eq(0, k._mover.StopCalls, "the foreign goal was never stopped");
        });
    }


    // Night formation (PatchRoles_SamuraiNightFormation) reads this probe to leave
    // lease-owned goals untouched; pin the real accessor against a live lease.
    private static void NightFormationLeaseRegressions()
    {
        Test("Night formation lease probe follows a live dash lease until it retires", () => {
            var k = PrepareBurst(false); UpdateHook(k);
            Check(PatchRoles_SamuraiPowerDash.HasActiveMotion(k), "forward dash owns a live lease");
            DisableHook(k);
            Check(!PatchRoles_SamuraiPowerDash.HasActiveMotion(k), "retired lease is no longer reported");
        });
        Test("Night formation lease probe is false without an actor record", () => {
            var fresh = NewKnight(0);
            Check(!PatchRoles_SamuraiPowerDash.HasActiveMotion(fresh), "untracked knight has no lease");
            Check(!PatchRoles_SamuraiPowerDash.HasActiveMotion(null), "null knight is safe");
        });
    }

    private static void Main()
    {
        NightRegressions();
        WalkHomeRegressions();
        ScanRegressions();
        FixedDashRegressions();
        PoseContractRegressions();
        ChoreoEndpointRegressions();
        Test("No enemy and native attack cooldown do not prevent >10 return", () => {
            var k = NewKnight(20); Follower(k, 0); k._cooldown = 2.8f;
            AssertStartedReturn(k, 0); Eq(0, Scanner.ScanCalls, "no enemy search before return");
        });
        Test("Return ladder opens over a live attack cadence after the trip closes", () => {
            var k = NewKnight(); var follower = Follower(k, 0); Enemy(k, 3);
            UpdateHook(k); Check(k._damageable.invulnerable, "attack actually started");
            Frames(80);                                          // the round trip closes at the station
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "the trip finished");
            Scanner.ScanTargets.Clear(); Physics2D.Hits = Array.Empty<Collider2D>();
            k.transform.position = new(20); follower.transform.position = new(0); k._cooldown = 3;
            UpdateHook(k);
            AssertStartedReturn(k, 0);
        });
        Test("10/4 hysteresis and ordinary completion", () => {
            var k = NewKnight(10); var f = Follower(k, 0); UpdateHook(k); Eq(0, k._mover.GoalWrites, "exact 10 does not enter");
            k.transform.position = new(10.01f); Frames(12, .02f, false); Check(k._mover.GoalWrites > 0, "above 10 enters");
            k.transform.position = new(4); Frame(.02f, false); Check(ShouldSlash(k), "exact 4 completes return");
            Eq(false, k._damageable.invulnerable, "completion restores vulnerability");
            int starts = Scheduler.Started, writes = k._mover.GoalWrites; k.transform.position = new(8); Frames(30, .02f, false);
            Eq(starts, Scheduler.Started, "8 does not restart burst"); Eq(writes, k._mover.GoalWrites, "8 does not reissue return goals");
        });
        Test("A walk issues no visual token at all", () => {
            var k = NewKnight(20); Follower(k, 0); UpdateHook(k);
            Eq(0, SamuraiDashVisuals.BeginCount(k), "the withdrawal family begins no visuals");
            Frames(10, .01f, false);
            Eq(0, SamuraiDashVisuals.BeginCount(k), "no visuals appear mid-walk either");
        });
        Test("Inside hysteresis band the walk blocks the attack trigger", () => {
            var k = NewKnight(12); Follower(k, 0); var enemy = Enemy(k, 5); UpdateHook(k);
            k.transform.position = new(8); Frames(25, .01f, false);
            Eq(0, enemy.HitCount, "a walk never damages and no concurrent attack opened");
            Eq(0, SamuraiDashVisuals.BeginCount(k), "no visual token while walking");
            Check(ShouldSlash(k), "the walk suppresses nothing");
        });
        Test("A long gap closes by plain walk at run speed without any protection", () => {
            var k = NewKnight(25); Follower(k, 0); UpdateHook(k);
            Check(!k._damageable.invulnerable && !k._trail.enabled, "no protection exists to hold");
            Eq(k._runSpeed, k._mover._goalSpeed, "the whole way home is native run speed");
            for (int i = 0; i < 400 && k.transform.position.x > 4.1f; i++) Frame(.01f);
            Check(k.transform.position.x <= 4.1f, "a longer-than-seventeen gap still closes");
            Eq(0, SamuraiDashVisuals.BeginCount(k), "no burst ever existed");
        });
        Test("A normal 10-to-11 gap closes by plain walk with no protection or trail", () => {
            var k = NewKnight(10.8f); Follower(k, 0); UpdateHook(k);
            Eq(k._runSpeed, k._mover._goalSpeed, "run speed from the first frame");
            Check(!k._damageable.invulnerable && !k._trail.enabled, "no burst, no trail, ever");
            for (int i = 0; i < 150 && k.transform.position.x > 4.01f; i++) Frame(.01f);
            Check(k.transform.position.x <= 4.01f, "normal gap closes to exit threshold");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "arrival released the goal");
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
        Test("A walk preserves preexisting invulnerability and trail untouched", () => {
            var k = NewKnight(25); Follower(k, 0); k._damageable.invulnerable = true; k._trail.enabled = true;
            UpdateHook(k); Frames(65, .01f);
            Eq(true, k._damageable.invulnerable, "the walk never touches protection flags");
            Eq(true, k._trail.enabled, "the walk never touches the trail");
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
        Test("A blocked walk keeps its goal and never enters a retry loop", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.Blocked = true; UpdateHook(k);
            Frames(170);
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "the blocked walk still owns its goal");
            Eq(false, k._damageable.invulnerable, "nothing to protect");
            Eq(0, SamuraiDashVisuals.BeginCount(k), "no burst was ever spawned");
            int writes = k._mover.GoalWrites; Frames(5);
            Check(k._mover.GoalWrites - writes <= 1, "no per-frame goal rewrite (cadence only)");
        });
        Test("A paused frame starts no walk and grants no protection", () => {
            var k = NewKnight(20); Follower(k, 0); Time.timeScale = 0; Time.deltaTime = 0;
            Frames(30, 0); Eq(0, Scheduler.Started, "no paused start");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "no paused walk goal");
            Eq(false, k._damageable.invulnerable, "no paused protection");
            Time.timeScale = 1; Time.deltaTime = .02f; UpdateHook(k);
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "the walk opens on the first live tick");
            Eq(k._runSpeed, k._mover._goalSpeed, "at native run speed");
        });
        Test("External mover pause is not cleared", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover._pauseTimeout = 2; UpdateHook(k);
            Check(k._mover._pauseTimeout >= 2, "external pause unchanged by start"); Frames(5, .02f, false);
            Check(k._mover._pauseTimeout >= 2, "return never unpauses external owner");
        });
        Test("Walk disable and optional late coroutine cleanup cannot erase a reused owner", () => {
            var k = NewKnight(20); Follower(k, 0); UpdateHook(k);
            var old = Scheduler.All.LastOrDefault(c => ReferenceEquals(c.Owner, k) && c.Active); DisableHook(k);
            if (old != null) Scheduler.StopSilently(old);
            k.transform.position = new(20); Time.time += .25f; Time.frameCount++; UpdateHook(k);
            int stops = k._mover.StopCalls; float goal = k._mover._goalPosition;
            if (old?.Iterator is IDisposable disposable) disposable.Dispose();
            Eq(stops, k._mover.StopCalls, "old finally cannot stop the new walk goal"); Eq(goal, k._mover._goalPosition, "new walk goal retained");
            Eq(false, k._damageable.invulnerable, "the new walk owns no protection to erase");
            Check(ShouldSlash(k), "the new walk suppresses nothing");
        });
        Test("Whole follower cache is not searched every active return frame", () => {
            var k = NewKnight(30); Follower(k, 0); k._mover.Blocked = true; AssertStartedReturn(k, 0);
            int calls = UnitScanCache.Calls; Frames(30, .01f); Check(UnitScanCache.Calls - calls <= 3, "at most interval-based cache lookups over 0.3 seconds");
        });
        Test("Whole follower cache is not searched every attack dash frame", () => {
            var k = NewKnight(); Follower(k, 0); Enemy(k, 3); UpdateHook(k); int calls = UnitScanCache.Calls;
            Frames(20, .01f); Check(UnitScanCache.Calls - calls <= 3, "the trip never searches the follower cache");
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
        Test("A mid-flight external mover destination is reclaimed by the re-assert", () => {
            var k = NewKnight(); Follower(k, 0); Enemy(k, 3); UpdateHook(k);
            k._mover.SetGoalNoHaglet(100, 4); int stops = k._mover.StopCalls;
            Frame(.02f, false);                            // one routine frame is enough
            Eq(7f, k._mover._goalPosition, "the trip reclaimed its own goal");
            Eq(18f, k._mover._goalSpeed, "with the trip's own speed");
            Eq(stops, k._mover.StopCalls, "the foreign grab was never stopped");
            Check(TripActive(k), "the trip kept running");
        });
        Test("Optional late attack coroutine finally cannot clear a new walk owner after reuse", () => {
            var k = NewKnight(); var follower = Follower(k, 0); Enemy(k, 3); UpdateHook(k);
            var old = Scheduler.All.LastOrDefault(c => ReferenceEquals(c.Owner, k) && c.Active);
            DisableHook(k); if (old != null) Scheduler.StopSilently(old); k.transform.position = new(20); Scanner.ScanTargets.Clear();
            Time.time = .25f; Time.frameCount++; UpdateHook(k); int stops = k._mover.StopCalls;
            if (old?.Iterator is IDisposable disposable) disposable.Dispose();
            Eq(stops, k._mover.StopCalls, "old attack cannot stop the walk goal");
            Eq(false, k._damageable.invulnerable, "the walk owns no protection for the old attack to clear");
            Check(ShouldSlash(k), "the new walk suppresses nothing");
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "the walk still owns its goal");
        });
        foreach (bool returning in new[] { false })
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
            Test(direction + " burst damage is bounded per leg (the turn re-arms, the timeout does not linger)", () => {
                var k = PrepareBurst(returning); UpdateHook(k); var late = HitTarget(); Supply(late);
                Time.time = returning ? .61f : 1.3f; Time.frameCount++; UpdateHook(k); Scheduler.Advance();
                if (returning) Eq(0, late.HitCount, "no late timeout damage");
                else
                {
                    Eq(2, late.HitCount, "one strike per leg on a target supplied after the trigger (the turn re-arms)");
                    int hits = late.HitCount;
                    Frames(6, .02f, false);
                    Eq(hits, late.HitCount, "the turn round is deduplicated");
                }
            });
            if (returning)
                Test("Return burst stops damage at ten-and-a-half-unit travel boundary", () => {
                    var k = PrepareBurst(true); UpdateHook(k); var late = HitTarget(); Supply(late);
                    float x = k.transform.position.x; k.transform.position = new(x - 10.5f);
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
                UpdateHook(k); Eq(1, first.HitCount, "first callback actually invoked");
                if (!returning && interrupt.Name == "manual control")
                {
                    Eq(1, second.HitCount, "a native behaviour flag no longer interrupts the hit frame");
                    Check(TripActive(k), "the trip survives the behaviour flag");
                    Frame(.02f, false);
                    Eq(1, second.HitCount, "the dedup keeps the second target at one hit");
                    return;
                }
                if (!returning && interrupt.Name == "external goal")
                {
                    Eq(1, second.HitCount, "a foreign goal no longer interrupts the hit frame");
                    Eq(7f, k._mover._goalPosition, "the every-frame re-assert reclaims the destination in-frame");
                    Eq(18f, k._mover._goalSpeed, "with the trip's own speed");
                    Frame(.02f, false);
                    Eq(1, second.HitCount, "the dedup keeps the second target at one hit");
                    return;
                }
                Eq(0, second.HitCount, "old frame stops before second target");
                Frame(.02f, false); Eq(0, second.HitCount, "no resumed old-frame damage after interruption");
            });
            Test(direction + " ReceiveDamage callback replacing the motion leaves new return goal and effects intact", () => {
                var k = PrepareBurst(returning); var first = HitTarget(); var second = HitTarget(); Supply(first, second);
                float replacementGoal = float.NaN; int replacementStops = -1;
                first.OnReceiveDamage = _ => {
                    first.OnReceiveDamage = null; DisableHook(k); Scheduler.StopOwnerSilently(k);
                    k.transform.position = new(20); Scanner.ScanTargets.Clear(); Physics2D.Hits = Array.Empty<Collider2D>();
                    UpdateHook(k); replacementGoal = k._mover._goalPosition; replacementStops = k._mover.StopCalls;
                };
                UpdateHook(k); Eq(1, first.HitCount, "reentrant callback executed"); Eq(0, second.HitCount, "old frame cannot damage second target");
                Eq(replacementGoal, k._mover._goalPosition, "replacement goal unchanged after old caller returns"); Eq(replacementStops, k._mover.StopCalls, "old caller does not stop new motion");
                Eq(false, k._damageable.invulnerable, "the replacement walk owns no protection"); Check(ShouldSlash(k), "the replacement walk suppresses nothing");
                Eq(Mover.GoalMode.Position, k._mover.goalMode, "the replacement walk still owns its goal");
            });
        }
        Test("Forward and subsequent reverse cut may each hit the same Damageable once", () => {
            var k = PrepareBurst(false); var target = HitTarget(); Supply(target); UpdateHook(k); Eq(1, target.HitCount, "forward hit");
            Frames(20);                                     // the outbound leg arrives; the turn opens a fresh round
            Eq(2, target.HitCount, "the reverse cut gets its own dedup round"); Eq(2 * k._attackDamage, target.TotalDamage, "one ordinary hit per cut round");
        });
        Test("The walk home runs no hit scans and no damage", () => {
            var k = NewKnight(25); Follower(k, 0); UpdateHook(k);
            Eq(k._runSpeed, k._mover._goalSpeed, "walk runs at native run speed");
            var late = HitTarget(); Supply(late); int scans = Physics2D.Scans; Frames(20, .01f);
            Eq(0, late.HitCount, "the walk does not damage"); Eq(scans, Physics2D.Scans, "the walk does not scan hits");
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
            Eq(replacementGoal, k._mover._goalPosition, "Stop callback new walk goal retained");
            Eq(false, k._damageable.invulnerable, "the replacement walk owns no protection");
            Check(ShouldSlash(k), "the replacement walk suppresses nothing");
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "the new walk still owns its goal");
        });
        Test("Motion begins one visual token without old GlowOverlay call", () => {
            var k = PrepareBurst(false); UpdateHook(k); Eq(1, SamuraiDashVisuals.BeginCount(k), "one helper Begin");
            Eq(0, k._character.spriteFX.GlowCount, "old timed GlowOverlay removed");
            Check(SamuraiDashVisuals.Current.ContainsKey(k.gameObject.GetInstanceID()), "visual token held during burst");
        });
        Test("OnDisable prefix clears visuals before mover cleanup callbacks", () => {
            var k = PrepareBurst(false); UpdateHook(k); bool clearedBeforeStop = false;
            k._mover.OnStop = () => clearedBeforeStop = SamuraiDashVisuals.Clears.Contains(k);
            DisableHook(k); Check(clearedBeforeStop, "prefix Clear precedes native/motion cleanup");
            Check(!SamuraiDashVisuals.Current.ContainsKey(k.gameObject.GetInstanceID()), "no surviving active token");
        });
        RoundTripPhases();
        RoundTripInterruptions();
        RoundTripNight();

        NightFormationLeaseRegressions();
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed"); Environment.ExitCode = failed == 0 ? 0 : 1;
    }
}
