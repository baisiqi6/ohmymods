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
        // Same for the heal narrative's session budget and the shared-controller slash memory:
        // adoption and heal lines must be opted into per test, not inherited from earlier ones.
        typeof(PatchRoles_SamuraiPowerDash).GetField("HealLogs", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, 0);
        // The choreography's early-exit narrative is budgeted the same way.
        typeof(PatchRoles_SamuraiPowerDash).GetField("ChoreoLogs", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, 0);
        (typeof(PatchRoles_SamuraiPowerDash).GetField("SlashByController", BindingFlags.Static | BindingFlags.NonPublic)
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
        Test("A night goal steal mid-dash is reclaimed by the 0.3 s re-assert", () => {
            Managers.Inst.kingdom.isDaytime = false; var k = NewKnight(0); Follower(k, 0); Enemy(k, 3); UpdateHook(k);
            k._mover.SetGoal(1, 2);                        // native GoToWall supplies its own goal
            Frames(4, .02f, false);                        // inside the cadence the foreign goal stands
            Eq(1f, k._mover._goalPosition, "the foreign goal is not stomped before the cadence");
            Eq(0, k._mover.StopCalls, "the foreign goal is never stopped");
            Frames(14, .02f, false);                       // past the 0.3 s re-assert
            Eq(7f, k._mover._goalPosition, "the trip reclaimed its own goal");
            Eq(18f, k._mover._goalSpeed, "with the trip's own speed");
            NativeFrame(k);
            Eq(0, k._fsm.Requests, "the mod never queues over the trip");
            Check(k._damageable.invulnerable, "the trip kept its protection through the steal");
        });
    }
    private static void ReturnLadder()
    {
        Test("A failed burst hands over to the walk home in the very next tick", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.Blocked = true;
            AssertStartedReturn(k, 0);
            Frame(.5f, false);   // the .5 s no-progress deadline fails the burst on this tick
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "failed burst released its own goal");
            Eq(false, k._damageable.invulnerable, "failed burst released its protection");
            Frame(.02f, false);
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "the next tick already walks home");
            Eq(k._runSpeed, k._mover._goalSpeed, "walk uses the native run speed");
            Eq(2.5f, k._mover._goalPosition, "walk heads for the follower station");
            Check(ShouldSlash(k), "a walking withdrawal no longer suppresses the native slash");
        });
        Test("The walk keeps a live goal for as long as the burst backoff runs", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.Blocked = true; AssertStartedReturn(k, 0);
            Frames(30, .02f, false);   // burst failed at .5 s, the walk began on the following tick
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "walk owns the return");
            int live = 0;
            for (int i = 0; i < 40; i++) { if (k._mover.goalMode == Mover.GoalMode.Position) live++; Frame(.02f, false); }
            Eq(40, live, "no tick leaves the stranded knight without a goal");
            Eq(1, SamuraiDashVisuals.BeginCount(k), "walking never spawns another burst");
        });
        Test("Repeated hit-stun pauses never lock the return", () => {
            var k = NewKnight(20); Follower(k, 0); AssertStartedReturn(k, 0);
            for (int i = 0; i < 4; i++)
            {
                k._mover._pauseTimeout = 1f;      // native HandleOnReceiveDamage -> Mover.Pause(1f)
                Frame(.02f, false);               // the external pause invalidates the live lease
                k._mover._pauseTimeout = 0f;      // ... and expires in scaled time
                Time.time += 1f;
                Frame(.02f, false);               // the ladder answers instead of locking
            }
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "still returning after four interruptions");
            Check(SamuraiDashVisuals.BeginCount(k) <= 3, "bounded bursts while being hit");
            Eq(false, k._damageable.invulnerable, "no permanently held protection");
        });
        Test("Burst retries escalate and then retire in favour of the walk", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.Blocked = true; AssertStartedReturn(k, 0);
            Frames(175, .02f, false);   // 3.5 s: burst 1 failed at .5 s, burst 2 after the 2 s backoff
            Eq(2, SamuraiDashVisuals.BeginCount(k), "exactly two bursts inside 3.5 s");
            Frames(250, .02f, false);   // 8.5 s: burst 3 after the 4 s backoff
            Eq(3, SamuraiDashVisuals.BeginCount(k), "third burst after the escalated backoff");
            Frames(250, .02f, false);   // 13.5 s: the walk owns the way home from here on
            Eq(3, SamuraiDashVisuals.BeginCount(k), "no burst after the third failure");
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "the knight is still walking, never standing");
            Eq(false, k._damageable.invulnerable, "no stray protection in the walk phase");
        });
        Test("A spent ladder walks home by itself and the next episode bursts again", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.Blocked = true; AssertStartedReturn(k, 0);
            Frames(450, .02f, false);   // 9 s: three blocked burst failures spend the ladder
            Eq(3, SamuraiDashVisuals.BeginCount(k), "three bursts spent while blocked");
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "the degraded walk owns the return");
            k._mover.Blocked = false;
            // Arrival arithmetic: 16 u at the native run speed (6 u/s stub) = 2.67 s = 133 frames.
            for (int i = 0; i < 900 && k.transform.position.x > 4.01f; i++) Frame(.02f);
            Check(k.transform.position.x <= 4.01f, "the walk itself reached the return threshold");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "arrival released the walk goal");
            Eq(false, k._damageable.invulnerable, "a plain walk never gains burst protection");
            k.transform.position = new(20);
            Frames(12, .02f, false);
            Eq(4, SamuraiDashVisuals.BeginCount(k), "the cleared ladder allows a fresh burst");
        });
        Test("A walk upgraded by its backoff still ends at the follower station", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.Blocked = true; AssertStartedReturn(k, 0);
            Frames(40, .02f, false);   // burst failed at .5 s; the walk is live but blocked
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "walking while the path is blocked");
            k._mover.Blocked = false;
            // The walk upgrades to a burst at RetryAt (2.5 s) and that burst closes the rest.
            for (int i = 0; i < 900 && k.transform.position.x > 4.01f; i++) Frame(.02f);
            Check(k.transform.position.x <= 4.01f, "the upgraded return still reached the station");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "arrival released the goal");
            Eq(2, SamuraiDashVisuals.BeginCount(k), "exactly one burst finished the walk's leg");
            Eq(false, k._damageable.invulnerable, "burst effects retired at arrival");
        });
        foreach (var side in new[] { Side.Left, Side.Right })
            Test("A " + side + " return withdraws facing the enemy side without the dash pose", () => {
                var k = NewKnight(20); Follower(k, 0); k.side = side;
                AssertStartedReturn(k, 0);
                Eq(side == Side.Left ? Mover.FacingMode.Left : Mover.FacingMode.Right,
                    k._mover.facingMode, "defensive withdrawal facing");
                Eq(0, k._animator.TriggerCount, "returns never replay PowerSlash");
            });
        Test("Attack dash keeps the dash pose and never takes a facing lease", () => {
            var k = PrepareBurst(false);
            Eq(Mover.FacingMode.Ahead, k._mover.facingMode, "attack dash leaves the facing native");
            UpdateHook(k);
            Eq(1, k._animator.TriggerCount, "attack dash plays PowerSlash");
            Check(k._damageable.invulnerable, "attack burst ran");
            Eq(Mover.FacingMode.Ahead, k._mover.facingMode, "attack dash still owns no facing");
        });
        Test("A native reset to Ahead is re-asserted while the return runs", () => {
            var k = NewKnight(20); Follower(k, 0); k.side = Side.Right;
            AssertStartedReturn(k, 0);
            k._mover.facingMode = Mover.FacingMode.Ahead;   // native wrote ahead mid-return
            Frame(.02f, false);
            Eq(Mover.FacingMode.Right, k._mover.facingMode, "defensive facing restored");
        });
        Test("A foreign fixed facing survives the whole return", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.facingMode = Mover.FacingMode.Target;
            AssertStartedReturn(k, 0);
            Eq(Mover.FacingMode.Target, k._mover.facingMode, "native Target facing never taken over");
            Check(k._mover.facingTarget == null, "no invented facing target");
            ModConfig.Enabled.Value = false;
            Frame(.02f, false);
            Eq(Mover.FacingMode.Target, k._mover.facingMode, "cleanup never stomps a foreign facing");
        });
        Test("Walking withdrawal keeps the defensive facing and releases it on arrival", () => {
            var k = NewKnight(20); Follower(k, 0); k.side = Side.Left; k._mover.Blocked = true;
            AssertStartedReturn(k, 0);
            Eq(Mover.FacingMode.Left, k._mover.facingMode, "burst withdrew facing the enemy side");
            Eq(0, k._animator.TriggerCount, "no dash pose on a return");
            Frames(30, .02f, false);
            Eq(Mover.FacingMode.Left, k._mover.facingMode, "walk keeps the defensive facing");
            Eq(0, k._animator.TriggerCount, "walk never replays the dash pose");
            k._mover.Blocked = false;
            for (int i = 0; i < 900 && k.transform.position.x > 4.01f; i++) Frame(.02f);
            Check(k.transform.position.x <= 4.01f, "the return reached the follower station");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "arrival released the goal");
            Eq(Mover.FacingMode.Ahead, k._mover.facingMode, "facing released once the return ended");
            Eq(0, k._animator.TriggerCount, "no dash pose across the whole withdrawal");
        });
        foreach (var interrupt in new (string Name, Action<Knight> Apply)[] {
            ("formation", k => k.Formation = new()), ("retreating", k => k.isRetreating = true),
            ("charging", k => k.isCharging = true), ("charge pending", k => k._shouldCharge = true),
            ("dead", k => k._damageable.isDead = true), ("manual control", k => k.ControlRequested = true),
            ("config", k => ModConfig.Enabled.Value = false), ("authority", k => NetworkBigBoss.HasWorldAuth = false)
        }) Test("Walking withdrawal honours " + interrupt.Name + " exactly like the burst", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.Blocked = true; AssertStartedReturn(k, 0);
            Frames(40, .02f, false);
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "walk live before the interruption");
            interrupt.Apply(k); int writes = k._mover.GoalWrites;
            Frames(30, .02f, false);
            Eq(writes, k._mover.GoalWrites, "no replacement goal after " + interrupt.Name);
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "walk goal released");
        });
        Test("Night hands the return over: no mod goal, native guard-slot task only", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.Blocked = true; AssertStartedReturn(k, 0);
            Frames(40, .02f, false);   // day burst failed, the degraded walk is live
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "day walk live before dusk");
            Managers.Inst.kingdom.isDaytime = false;
            int writes = k._mover.GoalWrites;
            Frames(90, .02f, false);
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "mod goal released at dusk");
            Eq(writes, k._mover.GoalWrites, "no burst and no walk after the night handoff");
            Eq(1, SamuraiDashVisuals.BeginCount(k), "exactly the one day burst, none at night");
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
        Test("The trail pin holds across the whole trip and returns at the close", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            k._trail.time = .4f;                            // the native default this mod never wrote before
            UpdateHook(k);
            Eq(1f, k._trail.time, "opening pins the trail lifetime at one second");
            Check(k._trail.enabled, "the trail is emitting during the outbound dash");
            Frames(30);                                     // the outbound leg arrives; the turn fires in-lease
            Eq(1f, k._trail.time, "the reverse cut keeps the same pin (continuous ownership)");
            Check(k._trail.enabled, "the reverse cut still emits");
            Frames(40);                                     // the home leg closes the trip
            Check(!k._trail.enabled, "the finale retired the trail enable");
            Eq(.4f, k._trail.time, "the finale returned the trail's own lifetime");
        });
        Test("An externally rewritten trail lifetime survives the close", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            k._trail.time = .7f;                            // a third party rewrote the lifetime mid-trip
            Frames(70);                                     // the trip closes at the start point
            Eq(.7f, k._trail.time, "the foreign value is not ours: the finale leaves it alone");
            Check(!k._trail.enabled, "the trail enable is still retired by value discipline");
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

    // 2026-09-24 卡姿复发：短于自身转移的冲刺与被毒化的基线都捕不到姿势 → 探测失明。
    // 终捕（转移目标/见过转移的 Current）与同控制器采纳（同伴实证 hash）双网兜底。
    // 所有行程都用 1.2 s 腿窗跳时推进确定性收尾（对齐 RunAttackLease 的手法）。
    private static void StuckRecaptureRegressions()
    {
        Test("A cut that ends mid-transition captures where it was heading", () => {
            var k = NewKnight(0); Follower(k, 0);
            k._animator.StateHash = 111;
            Frames(2, .02f, false);                         // the calm pose is captured as the replay target
            Time.time += .25f;
            Enemy(k, 3);
            k._animator.InTransition = true;                // the transition outlives the whole cut
            k._animator.NextStateHash = 777;
            UpdateHook(k);
            Frames(20, .02f, false);                        // the cut misses its in-flight capture
            Time.time += 1.3f; Time.frameCount++; Scheduler.Advance();   // the timeout turns the lease in-lease
            Scanner.ScanTargets.Clear(); Physics2D.Hits = Array.Empty<Collider2D>();
            Frames(3, .02f, false);                        // the walk home closes the trip; the finish capture runs
            k._animator.InTransition = false;
            k._animator.StateHash = 777;                    // the transition lands on the stuck pose
            Check(KingdomEnhancedPlugin.Instance.LogSource.Infos.Any(m =>
                m.StartsWith("[SamuraiDash/slash-finish-capture] knight=")), "the finish capture names the knight");
            int resets = k._animator.ResetCount;
            Frames(100, .02f, false);                       // the finish capture armed the probe -> heal
            Eq(resets + 1, k._animator.ResetCount, "the finish-captured pose is repaired");
            Eq(1, k._animator.PlayCalls, "the replay lands on the calm pose");
        });
        Test("A settled walk pose after an untransitioned cut is not trusted", () => {
            var k = NewKnight(0); Follower(k, 0);
            k._animator.StateHash = 111;
            Time.time += .25f;
            Enemy(k, 3);
            UpdateHook(k);                                  // the cut starts; the animator never transitions
            k._animator.StateHash = 555;                    // a walk-like pose with no observed transition
            Frames(20, .02f, false);
            Time.time += 1.3f; Time.frameCount++; Scheduler.Advance();
            Scanner.ScanTargets.Clear(); Physics2D.Hits = Array.Empty<Collider2D>();
            Frames(3, .02f, false);                        // the trip closes; the untrusted settled read is rejected
            int resets = k._animator.ResetCount;
            Frames(90, .02f, false);
            Eq(resets, k._animator.ResetCount, "no transition history: the current read is rejected");
            Eq(0, k._animator.PlayCalls, "no replay for a guessed pose");
        });
        Test("A stuck knight's poisoned baseline cannot self-capture but a peer adopts", () => {
            var a = NewKnight(0); Follower(a, 0);
            a._animator.StateHash = 111;
            RunAttackLease(a, 777);                         // a clean lease proves 777 on the controller
            var shared = a._animator.runtimeAnimatorController;
            var b = NewKnight(0); Follower(b, 0);
            b._animator.runtimeAnimatorController = shared; // the same shared controller instance
            b._animator.StateHash = 777;                    // stuck from birth: every baseline reads the pose
            Time.time += .25f;
            Enemy(b, 3);
            UpdateHook(b);
            b._animator.InTransition = true; b._animator.NextStateHash = 777;
            Frames(20, .02f, false);                        // the cut ends heading nowhere new
            Time.time += 1.3f; Time.frameCount++; Scheduler.Advance();
            Scanner.ScanTargets.Clear(); Physics2D.Hits = Array.Empty<Collider2D>();
            Frames(3, .02f, false);                        // the trip closes: the poisoned capture is rejected and the peer adopts
            b._animator.InTransition = false;
            Check(KingdomEnhancedPlugin.Instance.LogSource.Infos.Any(m => m.StartsWith("[SamuraiDash/slash-adopt]")),
                "the peer's proven hash adopts through the poisoned baseline");
            int resets = b._animator.ResetCount;
            Frames(120, .02f, false);                       // the adoption armed the probe -> ladder runs
            Eq(resets + 1, b._animator.ResetCount, "the adopted pose is repaired");
        });
        Test("Adoption never crosses controller families", () => {
            var a = NewKnight(0); Follower(a, 0);
            a._animator.StateHash = 111;
            RunAttackLease(a, 777);
            var b = NewKnight(0); Follower(b, 0);           // its own controller instance
            b._animator.StateHash = 777;
            Time.time += .25f;
            Enemy(b, 3);
            UpdateHook(b);
            b._animator.InTransition = true; b._animator.NextStateHash = 777;
            Frames(20, .02f, false);
            Time.time += 1.3f; Time.frameCount++; Scheduler.Advance();
            Scanner.ScanTargets.Clear(); Physics2D.Hits = Array.Empty<Collider2D>();
            Frames(3, .02f, false);                        // the trip closes on the alien controller
            b._animator.InTransition = false;
            Check(!KingdomEnhancedPlugin.Instance.LogSource.Infos.Any(m => m.StartsWith("[SamuraiDash/slash-adopt]")),
                "a different controller never adopts");
            int resets = b._animator.ResetCount;
            Frames(90, .02f, false);
            Eq(resets, b._animator.ResetCount, "still blind: no repair without adoption");
        });
        Test("A wrong finish guess is overridden by the peer's proven hash", () => {
            var a = NewKnight(0); Follower(a, 0);
            a._animator.StateHash = 111;
            RunAttackLease(a, 777);
            var d = NewKnight(0); Follower(d, 0);
            d._animator.runtimeAnimatorController = a._animator.runtimeAnimatorController;
            d._animator.StateHash = 111;
            Frames(2, .02f, false);                         // calm pose captured for the replay
            Time.time += .25f;
            Enemy(d, 3);
            d._animator.InTransition = true;
            d._animator.NextStateHash = 555;                // a misleading transition target at the finish
            UpdateHook(d);
            Frames(20, .02f, false);
            Time.time += 1.3f; Time.frameCount++; Scheduler.Advance();
            Scanner.ScanTargets.Clear(); Physics2D.Hits = Array.Empty<Collider2D>();
            Frames(3, .02f, false);                        // the trip closes: the guess is captured, then the peer overrides
            d._animator.InTransition = false;
            Check(KingdomEnhancedPlugin.Instance.LogSource.Infos.Any(m => m.StartsWith("[SamuraiDash/slash-adopt]")),
                "the proven peer hash overrides the finish guess");
            d._animator.StateHash = 777;                    // the real stuck pose
            int resets = d._animator.ResetCount;
            Frames(120, .02f, false);
            Eq(resets + 1, d._animator.ResetCount, "the adopted hash repairs the real stuck pose");
        });
    }

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
            Check(k._damageable.invulnerable && k._trail.enabled && k._trail.time == 1f, "one protected motion");
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
        Test("The turn replays the dash pose, faces home and repairs the y scale", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3); k._animator.StateHash = 111;
            k.transform.localScale = new Vector3(-1, 1.3f, 1);
            UpdateHook(k);
            Frames(21);                                     // the outbound leg arrives; the turn fires in-lease
            Check(TripActive(k), "the return leg is still running");
            Eq(2, k._animator.TriggerCount, "the reverse cut re-fired the dash pose");
            Eq(1, k._animator.ResetCount, "trigger hygiene cleared the finished cut first");
            Eq("reset", k._animator.Ops[^2], "the finished cut clears the trigger first");
            Eq("set", k._animator.Ops[^1], "the reverse cut then fires its own trigger");
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
            Eq(true, k._trail.enabled, "preexisting trail returned");
            Eq(.4f, k._trail.time, "preexisting trail lifetime returned");
            Time.time += 3.2f; Time.frameCount++; UpdateHook(k);   // past the trigger-anchored cooldown
            Check(TripActive(k), "the second trip opened");
            CloseTrip(k);
            Eq(true, k._damageable.invulnerable, "second trip returned the flag again");
            Eq(true, k._trail.enabled, "second trip returned the trail again");
            Eq(.4f, k._trail.time, "second trip returned the lifetime again");
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

    // 中断矩阵（单相位：协程自行推进，无 Tick 相位）：资格失效（控制/编队/死亡等）与移动器替换
    // 一律以终幕结束并归还效果；纯外目标不终幕（.3 s 重申夺回）；外部暂停迟滞不终幕。
    private static void RoundTripInterruptions()
    {
        foreach (var interrupt in new (string Name, Action<Knight> Apply)[] {
            ("config", k => ModConfig.Enabled.Value = false),
            ("authority", k => NetworkBigBoss.HasWorldAuth = false),
            ("manual control", k => k.ControlRequested = true),
            ("formation", k => k.Formation = new()),
            ("dead", k => k._damageable.isDead = true),
            ("charging", k => k.isCharging = true),
            ("grabbed", k => k._character.grabbed = true),
            ("other FSM task", k => k._fsm.Current = (int)Knight.State.GrabCoin)
        }) Test("In-flight interruption: " + interrupt.Name, () => {
            var k = NewKnight(0); Follower(k, 6); Enemy(k, 3);
            UpdateHook(k);
            Check(TripActive(k), "the trip opened");
            interrupt.Apply(k);
            Frames(60, .02f, false);
            Check(!TripActive(k), "the interruption closed the trip");
            Check(!k._damageable.invulnerable, "the close released invulnerability");
            Check(!k._trail.enabled, "the close released the trail");
            Check(!SamuraiDashVisuals.Current.ContainsKey(k.gameObject.GetInstanceID()), "the visual token ended");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "the owned goal was released");
        });
        Test("In-flight interruption: a foreign goal is reclaimed by the re-assert, not handed off", () => {
            var k = NewKnight(0); Follower(k, 6); Enemy(k, 3);
            UpdateHook(k);
            k._mover.SetGoalNoHaglet(100, 3);               // native GoToWall takes the mover
            Frames(4, .02f, false);
            Eq(100f, k._mover._goalPosition, "inside the cadence the foreign goal stands");
            Eq(0, k._mover.StopCalls, "the foreign goal is never stopped");
            Frames(14, .02f, false);
            Eq(7f, k._mover._goalPosition, "the trip reclaimed its own goal");
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
            Frames(18, .02f, false);                        // past the 0.3 s re-assert, still inside the leg window
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
    }

    // One complete choreographed trip in the stub world: two calm frames let the calm pose settle,
    // the outbound dash fires from the animator's current state and settles on `pose` so the
    // capture can take it, the outbound window is then spent with a time jump (the turn fires
    // in-lease and re-plays the captured pose) and the home leg closes at the start point --
    // finale included (trigger hygiene, finish capture, probe arm). The scanner and the physics
    // window are emptied afterwards.
    private static void RunAttackLease(Knight k, int pose)
    {
        Frames(2, .02f, false);
        Time.time += .25f;                          // past the attack scan interval
        Enemy(k, 3);
        k._animator.InTransition = true;            // entering the cut pose, like a real transition
        UpdateHook(k);
        k._animator.InTransition = false;
        k._animator.StateHash = pose;
        Frames(3, .02f, false);                     // settled: the trip captures the pose
        Time.time += 1.3f;                          // past the outbound window
        Time.frameCount++;
        Scheduler.Advance();                        // the outbound window ends; the turn fires in-lease
        Frames(3, .02f, false);                     // the home leg closes at the start point
        Scanner.ScanTargets.Clear();
        Physics2D.Hits = Array.Empty<Collider2D>();
    }

    private static void StuckPoseRepair()
    {
        Test("A pose that outlives the native slash is reset, replayed to calm and left alone", () => {
            var k = NewKnight(0); Follower(k, 0);
            k._animator.StateHash = 111;                    // the calm stand pose
            RunAttackLease(k, 777);                         // the capture takes 777 as the slash pose
            int resets = k._animator.ResetCount;            // the trip's own turn and Finish resets
            k._animator.StateHash = 777;                    // the trigger's pose survived the dash
            Frames(70, .02f, false);                        // 1.4 s: still inside the native budget
            Eq(resets, k._animator.ResetCount, "no repair before the pose outlives the native slash");
            Frames(15, .02f, false);                        // past 1.5 s
            Eq(resets + 1, k._animator.ResetCount, "the leftover trigger is reset once");
            Eq(2, k._animator.PlayCalls, "the calm pose is replayed (turn reverse-cut + heal replay)");
            Eq(111, k._animator.StateHash, "the replay landed on the captured calm state");
            Eq(0, k._animator.EnabledWrites, "no enable toggle once the replay worked");
            Frames(120, .02f, false);
            Eq(resets + 1, k._animator.ResetCount, "a healed pose is not repaired again");
            Eq(2, k._animator.PlayCalls, "no repeated heal replay beyond the turn cut");
            var line = KingdomEnhancedPlugin.Instance.LogSource.Infos.LastOrDefault(m => m.StartsWith("[SamuraiDash/heal]"));
            Check(line != null && line.Contains("normalizedTime=") && line.Contains("fullPathHash=") && line.Contains("order=1"),
                "the repair line carries normalizedTime, fullPathHash and the retry order");
        });
        Test("A transition-only lease leaves the session's captured pose untouched", () => {
            var k = NewKnight(0); Follower(k, 0);
            k._animator.StateHash = 111;
            RunAttackLease(k, 777);                         // lease 1 captures 777
            // Lease 2: every frame is a transition whose current state is the source pose 111.
            Time.time += 3.5f;
            Enemy(k, 3);
            k._animator.InTransition = true;
            k._animator.StateHash = 111;
            UpdateHook(k);
            Frames(10, .02f, false);                        // the capture gates never open for this lease
            k._animator.InTransition = false;
            Time.time += 1.3f; Time.frameCount++; Scheduler.Advance();
            Scanner.ScanTargets.Clear(); Physics2D.Hits = Array.Empty<Collider2D>();
            Frames(3, .02f, false);                        // the trip closes; the session capture was untouched
            int resets = k._animator.ResetCount;
            k._animator.StateHash = 777;                    // the pose lease 1 captured is still known
            Frames(120, .02f, false);
            Eq(resets + 1, k._animator.ResetCount, "the session capture survived the later lease");
            Eq(3, k._animator.PlayCalls, "the captured calm pose is replayed (turn + heal, second lease adds its turn cut)");
        });
        Test("A lease that never opens its gates stands the capture probe down for the session", () => {
            var k = NewKnight(0); Follower(k, 0);
            k._animator.StateHash = 111;
            Enemy(k, 3);
            k._animator.InTransition = true;                // every frame reports the transition source
            UpdateHook(k);
            Frames(64, .01f, false);                        // one lease, 30+ misses: the probe is retired
            k._animator.InTransition = false;
            Scanner.ScanTargets.Clear(); Physics2D.Hits = Array.Empty<Collider2D>();
            Frames(45, .02f, false);                        // the outbound window is spent; the trip closes; the dead probe stays down
            int resets = k._animator.ResetCount;
            k._animator.StateHash = 777;
            Frames(80, .02f, false);                        // far past the stuck budget
            Eq(resets, k._animator.ResetCount, "a never-captured knight is never repaired");
            Eq(0, k._animator.PlayCalls, "no replay without a captured pose");
            // A later, perfectly clean lease cannot revive the probe inside the same session.
            Time.time += 3.5f;
            k._animator.StateHash = 111;
            RunAttackLease(k, 777);
            k._animator.StateHash = 777;
            resets = k._animator.ResetCount;
            Frames(80, .02f, false);
            Eq(resets, k._animator.ResetCount, "the stand-down survives a clean lease");
        });
        Test("Misses from one lease do not count against the next", () => {
            var k = NewKnight(0); Follower(k, 0);
            k._animator.StateHash = 111;
            Frames(2, .02f, false);                         // the calm pose is captured
            Time.time += .25f;
            Enemy(k, 3);
            k._animator.InTransition = true;                // lease A misses on 29 of its 30 frames
            UpdateHook(k);
            Frames(28, .02f, false);
            k._animator.InTransition = false;
            Time.time += 1.3f; Time.frameCount++; Scheduler.Advance();
            Scanner.ScanTargets.Clear(); Physics2D.Hits = Array.Empty<Collider2D>();
            Frames(3, .02f, false);                        // the trip closes; the fresh budget was never spent
            Time.time += 3.5f;
            k._animator.StateHash = 111;
            RunAttackLease(k, 777);                         // lease B's gates open and still capture
            k._animator.StateHash = 777;
            int resets = k._animator.ResetCount;
            Frames(120, .02f, false);
            Eq(resets + 1, k._animator.ResetCount, "a short lease's misses never retire the probe");
        });
        Test("A lease that never leaves its begin pose captures nothing", () => {
            var k = NewKnight(0); Follower(k, 0);
            k._animator.StateHash = 111;
            RunAttackLease(k, 111);                         // the pose never moves off the baseline
            int resets = k._animator.ResetCount;
            Frames(90, .02f, false);                        // the same hash, but it was never captured
            Eq(resets, k._animator.ResetCount, "the begin-frame pose is not mistaken for the slash pose");
            Eq(0, k._animator.PlayCalls, "nothing is replayed");
        });
        Test("A state that is not the captured pose is never repaired", () => {
            var k = NewKnight(0); Follower(k, 0);
            k._animator.StateHash = 111;
            RunAttackLease(k, 777);
            k._animator.StateHash = 888;                    // a native state with its own hash
            int resets = k._animator.ResetCount;
            Frames(90, .02f, false);                        // 1.8 s in that other state
            Eq(resets, k._animator.ResetCount, "only the captured pose is repaired");
            Eq(1, k._animator.PlayCalls, "only the turn reverse-cut replays; no heal replay for a foreign state");
        });
        Test("A knight that never drove a cut lease is never repaired", () => {
            var k = NewKnight(0); Follower(k, 0);
            k._animator.StateHash = 777;                    // the same pose, but no cut ever armed it
            Frames(200, .02f, false);                       // 4 s: far past the stuck budget
            Eq(0, k._animator.ResetCount, "no cut lease ever armed the probe");
            Eq(0, k._animator.PlayCalls, "nothing is replayed");
        });
        Test("A pose first shown after the eight-second window is left alone", () => {
            var k = NewKnight(0); Follower(k, 0);
            k._animator.StateHash = 111;
            RunAttackLease(k, 777);
            k._animator.StateHash = 111;                    // calm again: no episode ever starts
            int resets = k._animator.ResetCount;
            Frames(450, .02f, false);                       // 9 s past the last cut, calm throughout
            k._animator.StateHash = 777;
            Frames(120, .02f, false);                       // 2.4 s in the pose, but the window is shut
            Eq(resets, k._animator.ResetCount, "the window closed before the pose appeared");
            Eq(1, k._animator.PlayCalls, "only the turn reverse-cut replays; no heal replay outside the window");
        });
        Test("The outbound cut clears its trigger before the reverse cut fires its own", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            k._animator.StateHash = 111;
            UpdateHook(k);                                  // the attack dash begins from 111
            k._animator.StateHash = 777;
            Frames(3, .02f, false);                         // the capture settles on 777
            Frames(9);                                      // the dash runs toward the end of its window
            k._animator.Ops.Clear();
            Time.time = 1.3f; Time.frameCount++; Scheduler.Advance(); // the outbound window ends; the turn fires
            Check(k._animator.Ops.Count >= 2, "both trigger writes were recorded");
            Eq("reset", k._animator.Ops[^2], "the finished cut clears the trigger first");
            Eq("set", k._animator.Ops[^1], "the reverse cut then fires its own trigger");
        });
        Test("Withdrawal finishes never touch the slash trigger", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.Blocked = true;
            AssertStartedReturn(k, 0);
            Frames(40, .02f, false);                        // the burst fails and the walk takes over
            k._mover.Blocked = false;
            for (int i = 0; i < 900 && k.transform.position.x > 4.01f; i++) Frame(.02f);
            Check(k.transform.position.x <= 4.01f, "the withdrawal actually finished its leg");
            Eq(0, k._animator.ResetCount, "no trigger hygiene on a return or a walk");
            Eq(0, k._animator.TriggerCount, "no dash pose either");
        });
        Test("A pose that survives both ladders stands the probe down until the next cut lease", () => {
            var k = NewKnight(0); Follower(k, 0);
            k._animator.StateHash = 111;
            RunAttackLease(k, 777);
            k._animator.OnPlay = (hash, layer, time) => false;  // the replay never takes
            int resets = k._animator.ResetCount;
            k._animator.StateHash = 777;
            Frames(200, .02f, false);                       // 4 s: both ladders spend themselves
            Eq(resets + 2, k._animator.ResetCount, "exactly two repair ladders");
            Eq(3, k._animator.PlayCalls, "turn reverse-cut + one replay per ladder");
            Eq(4, k._animator.EnabledWrites, "one enable toggle per ladder");
            Frames(200, .02f, false);                       // the stand-down holds
            Eq(resets + 2, k._animator.ResetCount, "no third ladder inside the same episode");
            Eq(4, k._animator.EnabledWrites, "no further toggles");
            Time.time += 3.5f;
            k._animator.StateHash = 111;
            RunAttackLease(k, 777);                         // the next cut lease arms the probe again
            k._animator.StateHash = 777;
            resets = k._animator.ResetCount;
            Frames(90, .02f, false);
            Eq(resets + 1, k._animator.ResetCount, "the next cut lease releases the stand-down");
        });
        Test("Without a captured calm pose the repair goes straight to the enable toggle", () => {
            var k = NewKnight(0); Follower(k, 0);
            k._animator.StateHash = 111;
            // 2026-09-24: Speed no longer gates the capture — block only the pre-lease frames
            // with a held transition (inlined RunAttackLease so the pose frames still capture
            // the slash hash; pauseTimeout would kill the lease itself).
            k._animator.InTransition = true;
            Frames(2, .02f, false);
            k._animator.InTransition = false;
            Time.time += .25f; Enemy(k, 3); UpdateHook(k);
            k._animator.InTransition = true;             // entering the cut pose, like a real transition
            Frames(1, .02f, false);
            k._animator.InTransition = false;
            k._animator.StateHash = 777;
            Frames(3, .02f, false);
            Time.time += 1.3f; Time.frameCount++; Scheduler.Advance();
            Scanner.ScanTargets.Clear(); Physics2D.Hits = Array.Empty<Collider2D>();
            Frames(3, .02f, false);                        // the walk home closes the trip
            k._animator.StateHash = 777;                     // the pose is left behind
            k._animator.OnEnabledWrite = on => { if (on) k._animator.StateHash = 111; };
            int resets = k._animator.ResetCount;
            Frames(100, .02f, false);                       // 2 s stuck: one ladder runs
            Eq(resets + 1, k._animator.ResetCount, "the ladder still resets the trigger");
            Eq(1, k._animator.PlayCalls, "only the turn reverse-cut replays without a captured calm pose");
            Eq(2, k._animator.EnabledWrites, "the enable toggle is the fallback");
            Frames(60, .02f, false);
            Eq(resets + 1, k._animator.ResetCount, "the toggle healed the episode");
        });
        Test("A walking frame supplies the replay pose (2026-09-24 field fix)", () => {
            var k = NewKnight(0); Follower(k, 0);
            k._animator.StateHash = 111;
            k._animator.Speed = 1;                          // walking: the old Speed gate starved this
            Frames(6, .02f, false);                         // two stable frames -> captured
            RunAttackLease(k, 777);
            k._animator.StateHash = 777;                    // the pose is left behind
            k._animator.OnPlay = (h, l, t) => { k._animator.StateHash = 111; return true; };
            int resets = k._animator.ResetCount;
            Frames(90, .02f, false);
            Eq(resets + 1, k._animator.ResetCount, "the ladder resets the trigger");
            Eq(true, k._animator.PlayCalls >= 1, "the walking pose is replayed, not the toggle");
            Eq(0, k._animator.EnabledWrites, "no enable toggle when a replay pose exists");
        });
        Test("A native slash pause never yields a calm pose to replay", () => {
            var k = NewKnight(0); Follower(k, 0);
            k._mover._pauseTimeout = 5;                     // Mover.Pause: Speed 0 while the slash holds
            k._animator.StateHash = 111;
            Frames(10, .02f, false);                        // the only calm window is this paused one
            k._mover._pauseTimeout = 0;
            Enemy(k, 3);
            UpdateHook(k);                                  // the attack dash begins from 111
            k._animator.InTransition = true;                // entering the cut pose, like a real transition
            Frames(1, .02f, false);
            k._animator.InTransition = false;
            k._animator.StateHash = 777;
            Frames(3, .02f, false);
            Time.time += 1.3f; Time.frameCount++; Scheduler.Advance();
            Scanner.ScanTargets.Clear(); Physics2D.Hits = Array.Empty<Collider2D>();
            Frames(3, .02f, false);                        // the walk home closes the trip
            k._animator.StateHash = 777;                    // the pose is left behind
            k._animator.OnEnabledWrite = on => { if (on) k._animator.StateHash = 111; };
            int resets = k._animator.ResetCount;
            Frames(100, .02f, false);
            Eq(resets + 1, k._animator.ResetCount, "the ladder still resets the trigger");
            Eq(1, k._animator.PlayCalls, "only the turn reverse-cut replays; the paused frame never supplied a calm pose");
            Eq(2, k._animator.EnabledWrites, "the toggle fallback carried the repair");
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
        ReturnLadder();
        ScanRegressions();
        FixedDashRegressions();
        StuckRecaptureRegressions();
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
            Check(k._mover.InvulnerableDistance <= 10.7f, "invulnerable distance bounded by ten and a half plus one physics step");
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
            Frames(18, .02f, false);                        // past the 0.3 s re-assert
            Eq(7f, k._mover._goalPosition, "the trip reclaimed its own goal");
            Eq(18f, k._mover._goalSpeed, "with the trip's own speed");
            Eq(stops, k._mover.StopCalls, "the foreign grab was never stopped");
            Check(TripActive(k), "the trip kept running");
        });
        Test("Optional late attack coroutine finally cannot clear new return owner after reuse", () => {
            var k = NewKnight(); var follower = Follower(k, 0); Enemy(k, 3); UpdateHook(k);
            var old = Scheduler.All.LastOrDefault(c => ReferenceEquals(c.Owner, k) && c.Active);
            DisableHook(k); if (old != null) Scheduler.StopSilently(old); k.transform.position = new(20); Scanner.ScanTargets.Clear();
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
                if (!returning && interrupt.Name == "external goal")
                {
                    Eq(1, second.HitCount, "a foreign goal no longer interrupts the hit frame");
                    Eq(100f, k._mover._goalPosition, "new external destination retained");
                    Eq(3f, k._mover._goalSpeed, "new external speed retained");
                    Frame(.02f, false);
                    Eq(1, second.HitCount, "the dedup keeps the second target at one hit");
                    return;
                }
                Eq(0, second.HitCount, "old frame stops before second target");
                if (interrupt.Name == "external goal") { Eq(100f, k._mover._goalPosition, "new external destination retained"); Eq(3f, k._mover._goalSpeed, "new external speed retained"); }
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
                Eq(true, k._damageable.invulnerable, "replacement effects still owned"); Eq(true, k._trail.enabled, "replacement trail retained"); Check(!ShouldSlash(k), "new return remains active");
            });
        }
        Test("Forward and subsequent reverse cut may each hit the same Damageable once", () => {
            var k = PrepareBurst(false); var target = HitTarget(); Supply(target); UpdateHook(k); Eq(1, target.HitCount, "forward hit");
            Frames(20);                                     // the outbound leg arrives; the turn opens a fresh round
            Eq(2, target.HitCount, "the reverse cut gets its own dedup round"); Eq(2 * k._attackDamage, target.TotalDamage, "one ordinary hit per cut round");
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
        RoundTripPhases();
        RoundTripInterruptions();
        RoundTripNight();
        StuckPoseRepair();
        NightFormationLeaseRegressions();
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed"); Environment.ExitCode = failed == 0 ? 0 : 1;
    }
}
