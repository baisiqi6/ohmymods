using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    private static readonly List<Knight> Knights = new();
    private static int passed, failed;
    private static void Eq<T>(T expected, T actual, string message)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{message}: expected {expected}, got {actual}"); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Within(float expected, float actual, float epsilon, string message)
    { if (MathF.Abs(expected - actual) > epsilon) throw new Exception($"{message}: expected {expected}±{epsilon}, got {actual}"); }

    private static void Test(string name, Action action)
    {
        foreach (var knight in Knights) DisableHook(knight);
        Knights.Clear();
        ResetProductionStatics();
        SamuraiDashVisuals.Reset();
        UnitScanCache.Archers = Array.Empty<Archer>(); UnitScanCache.Calls = 0;
        Physics2D.Reset();
        Scanner.ScanTargets.Clear(); Scanner.ScanCalls = 0; Scanner.LastLayers = 0;
        Scanner.LastRange = 0; Scanner.LastRangeBehind = 0; Scanner.LastHeight = 0; Scanner.LastExcludeDead = false;
        Time.time = 0; Time.deltaTime = .02f; Time.fixedDeltaTime = .02f; Time.timeScale = 1; Time.frameCount = 0;
        ModConfig.Enabled.Value = true; NetworkBigBoss.HasWorldAuth = true; Managers.Inst = new();
        KingdomEnhancedPlugin.Instance.LogSource.Errors.Clear();
        KingdomEnhancedPlugin.Instance.LogSource.Infos.Clear();
        try
        {
            action();
            Eq(0, KingdomEnhancedPlugin.Instance.LogSource.Errors.Count, "production error logs");
            passed++; Console.WriteLine("PASS " + name);
        }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message); }
    }

    // ---- 生产静态状态隔离（跨测试不携带行程/裁决缓存/预算） ----
    private static void ResetProductionStatics()
    {
        ClearStatic(typeof(SamuraiChoreoMotion), "TripsByOwner");
        ClearStatic(typeof(SamuraiChoreoMotion), "TripsByMover");
        ClearStatic(typeof(SamuraiChoreoMotion), "PowerSlashByController");
        ClearStatic(typeof(SamuraiChoreoMotion), "Logged");
        ZeroStatic(typeof(SamuraiChoreoMotion), "_closeLogs");
        ZeroStatic(typeof(SamuraiChoreoMotion), "_turnLogs");
        ClearStatic(typeof(PatchRoles_SamuraiPowerDash), "Logged");
        ZeroStatic(typeof(PatchRoles_SamuraiPowerDash), "WalkLogs");
    }
    private static void ClearStatic(Type type, string name)
    {
        var value = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        value?.GetType().GetMethod("Clear")?.Invoke(value, null);
    }
    private static void ZeroStatic(Type type, string name) =>
        type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, 0);

    // ---- 生产接线 ----
    private static void InvokeHook(Type type, string name, Knight knight) =>
        type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { knight });
    private static void UpdateHook(Knight knight) => InvokeHook(typeof(Knight_Update_SamuraiPowerDash_Patch), "Postfix", knight);
    private static void UpdatePrefix(Knight knight) => InvokeHook(typeof(Knight_Update_SamuraiPowerDash_Patch), "Prefix", knight);
    private static void DisableHook(Knight knight)
    {
        var type = typeof(Knight_OnDisable_SamuraiPowerDash_Patch);
        type.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, new object[] { knight });
        InvokeHook(type, "Postfix", knight);
    }
    private static bool ShouldSlash(Knight knight)
    {
        var method = typeof(Knight_ShouldSlash_SamuraiReturn_Patch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
        var parameters = method.GetParameters();
        object[] args = parameters.Select(p => p.Name == "__instance" ? (object)knight : true).ToArray();
        bool run = (bool)method.Invoke(null, args);
        if (run) return true;
        for (int i = 0; i < parameters.Length; i++) if (parameters[i].Name == "__result") return (bool)args[i];
        return false;
    }
    private static bool MoverPrefix(Mover mover) =>
        (bool)typeof(Mover_Update_SamuraiChoreo_Patch)
            .GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { mover });
    private static SamuraiChoreoDriver Driver(Knight knight) => knight.gameObject.GetComponent<SamuraiChoreoDriver>();
    private static void DriverFixed(SamuraiChoreoDriver driver) =>
        typeof(SamuraiChoreoDriver).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(driver, null);

    private static bool TripActive(Knight knight) => SamuraiChoreoMotion.ActiveTrip(knight) != null;
    private static SamuraiChoreoMotion.Trip Trip(Knight knight) => SamuraiChoreoMotion.ActiveTrip(knight);

    // ---- 帧循环：固定步（驱动器 → 物理）与渲染帧（原生 Mover.Update → Knight.Update）分离 ----
    private static void FixedStep()
    {
        foreach (var knight in Knights)
        {
            var driver = Driver(knight);
            if (driver != null && driver.enabled) DriverFixed(driver);
        }
        foreach (var knight in Knights) knight._mover?.rigidbody?.ApplyPhysics(Time.fixedDeltaTime);
    }
    private static void Frame(float dt = .02f, int fixedSteps = 1)
    {
        Time.frameCount++; Time.deltaTime = dt; Time.time += dt;
        for (int i = 0; i < fixedSteps; i++) FixedStep();
        foreach (var knight in Knights)
            if (knight._mover != null && MoverPrefix(knight._mover)) knight._mover.Step(dt);
        foreach (var knight in Knights) if (knight.gameObject.activeInHierarchy) UpdateHook(knight);
    }
    private static void Frames(int count, float dt = .02f, int fixedSteps = 1)
    { for (int i = 0; i < count; i++) Frame(dt, fixedSteps); }
    private static void RunTrip(Knight knight, int maxFrames = 300)
    {
        for (int i = 0; i < maxFrames && TripActive(knight); i++) Frame();
        Check(!TripActive(knight), "the trip closed within its frame budget");
    }
    private static void RunToHomeLeg(Knight knight)
    {
        for (int i = 0; i < 200 && TripActive(knight) && Trip(knight)?.Outbound == true; i++) Frame();
        Check(TripActive(knight) && Trip(knight)?.Outbound == false, "the home leg is running");
    }
    private static void NativeFrame(Knight k)
    {
        UpdatePrefix(k);
        k._fsm.Update();
        UpdateHook(k);
    }

    // ---- 夹具 ----
    private static Knight NewKnight(float x = 0)
    {
        var go = new GameObject(); var k = go.AddComponent<Knight>(); k.transform.position = new(x);
        k._mover = go.AddComponent<Mover>(); k._damageable = go.AddComponent<Damageable>();
        k._character = go.AddComponent<Character>(); k._trail = go.AddComponent<TrailRenderer>(); k._animator = go.AddComponent<Animator>();
        k._mover.rigidbody = go.AddComponent<Rigidbody2D>();
        go.transform.parent = Managers.Inst.world.gameLayer;   // 真实夹具：骑士实际挂在当前 gameLayer 下
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
    private static Damageable HitTarget(float x = 0, float y = 0)
    {
        var go = new GameObject(); go.transform.position = new(x, y);
        var target = go.AddComponent<Damageable>(); go.AddComponent<Collider2D>(); return target;
    }
    private static void Supply(params Damageable[] targets) => Physics2D.Hits = targets.Select(t => t.GetComponent<Collider2D>()).ToArray();
    // 触发一名武士的完整行程（敌人 x=3、随从在同 x），返回骑士。
    private static Knight SimpleTrip(float knightX = 0)
    {
        var k = NewKnight(knightX); Follower(k, knightX); Enemy(k, knightX + 3);
        Physics2D.Hits = Array.Empty<Collider2D>();
        UpdateHook(k);
        Check(TripActive(k), "the trip opened");
        return k;
    }
    private static int LandWrites(Knight k) => k._animator.SetTriggers.Count(t => t == Hash("Land"));
    private static int Hash(string name) => Animator.StringToHash(name);
    private static string LastCloseLine() =>
        KingdomEnhancedPlugin.Instance.LogSource.Infos.LastOrDefault(m => m.StartsWith("[SamuraiDash/choreo]"));
    private static int TurnLines() =>
        KingdomEnhancedPlugin.Instance.LogSource.Infos.Count(m => m.StartsWith("[SamuraiDash/roundtrip-turn]"));

    // =======================================================================================
    private static void Main()
    {
        TriggerRegressions();
        FixedStepRegressions();
        SweepRegressions();
        PoseContractRegressions();
        InterruptionRegressions();
        WalkHomeRegressions();
        ScanRegressions();
        NightRegressions();
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed"); Environment.ExitCode = failed == 0 ? 0 : 1;
    }

    // 触发门与冻结端点：只读一次世界；起点=实际刚体 x；固定 7 格；带 [1.5, 8.2]。
    private static void TriggerRegressions()
    {
        Test("The trigger freezes the actual body start and the fixed seven-unit endpoint", () => {
            var k = NewKnight(2.5f); Follower(k, 2.5f); Enemy(k, 5.5f);
            UpdateHook(k);
            var trip = Trip(k);
            Check(trip != null, "trip opened");
            Eq(2.5f, trip.HomeX, "home is the actual body x at the trigger");
            Eq(9.5f, trip.OutX, "outbound is start + fixed seven");
            Eq(1, trip.Side, "enemy on the +x side");
            Eq(trip.OutX, trip.TargetX, "the first leg targets the frozen outbound point");
            Eq(0, k._mover.GoalWrites, "the trip never writes a mover goal");
        });
        Test("Near, very near and behind enemies all get the fixed seven-unit endpoint", () => {
            foreach (float enemyX in new[] { 3f, 1.6f })
            {
                var k = NewKnight(0); Follower(k, 0); Enemy(k, enemyX);
                UpdateHook(k);
                Eq(7f, Trip(k).OutX, "enemy at " + enemyX + " no longer shortens the dash");
                Eq(0, k._mover.GoalWrites, "no goal write for the pinned dash");
            }
            var behind = NewKnight(0); Follower(behind, 0); Enemy(behind, -3);
            UpdateHook(behind);
            Eq(-7f, Trip(behind).OutX, "an enemy behind gets the mirrored fixed seven");
        });
        Test("The trigger band is [1.5, 7+1.2] and 9.0/10.5 enemies never fire", () => {
            float edge = 7f + 1.2f;
            foreach ((float enemyX, bool fires) in new[] { (1.4f, false), (1.5f, true), (edge, true), (edge + .05f, false), (9f, false), (10.5f, false) })
            {
                var k = NewKnight(0); Follower(k, 0); Enemy(k, enemyX);
                UpdateHook(k);
                Check(Scanner.ScanCalls > 0, "the band decision came from a real scan at x=" + enemyX);
                Eq(fires, TripActive(k), "trip at enemy x=" + enemyX);
                if (fires) Eq(7f, Trip(k).OutX, "fixed seven from the start point for x=" + enemyX);
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
                Check(!TripActive(k), "no trip opens for a retreating knight (day=" + day + ")");
                Eq(0, k._mover.GoalWrites, "no goal is issued for a retreating knight (day=" + day + ")");
                Check(!k._damageable.invulnerable, "no protection is applied for a retreating knight (day=" + day + ")");
            }
        });
        Test("Attack selection keeps its own .2 s cadence and never queries the native scanner", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 1);   // inside the 1.5 aim floor: scans, never attacks
            Time.time = .25f; UpdateHook(k);
            Eq(1, Scanner.ScanCalls, "one self-scan at the cadence boundary");
            Frames(21, .01f, 0);
            Eq(2, Scanner.ScanCalls, "one more scan once the interval elapses");
            Frames(18, .01f, 0);
            Eq(2, Scanner.ScanCalls, "no scan inside the interval");
            Eq(0, k._enemyScanner.Calls, "the native scanner is never queried");
            Check(Scanner.LastRangeBehind > 0, "the self-scan also covers the knight's back");
            Eq(1f, Scanner.LastHeight, "the self-scan keeps the replaced scanner's own column");
            Eq(true, Scanner.LastExcludeDead, "dead targets are excluded on purpose");
        });
        Test("A knight facing its home side still finds the enemy behind it", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, -3);
            UpdateHook(k);
            Check(TripActive(k), "the enemy behind the knight opened an attack");
            Check(k._damageable.invulnerable, "the trip is running");
            Check(Trip(k).OutX < 0, "the dash heads toward the enemy's own side");
            Eq(0, Scanner.LastLayers & 2, "the self-scan never asks for Wildlife");
            Eq(1, Scanner.LastLayers & 1, "the self-scan asks for the Enemies layer");
            Eq(10.5f, Scanner.LastRange, "the self-scan reaches the widened distance");
        });
        Test("A paused frame never opens a trip", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            Time.timeScale = 0; Time.deltaTime = 0; UpdateHook(k);
            Check(!TripActive(k), "no trip starts while paused");
            Check(!k._damageable.invulnerable, "no protection is applied while paused");
        });
        Test("The six-second attack cooldown anchors at the trigger, not at the trip's end", () => {
            var k = SimpleTrip();
            RunTrip(k);
            Time.time = 5.99f; Time.frameCount++; UpdateHook(k);
            Check(!TripActive(k), "inside the six seconds no new attack opens");
            Time.time = 6.01f; Time.frameCount++; UpdateHook(k);
            Check(TripActive(k), "the trigger-time anchor opens the next attack");
        });
        Test("Every trip leaves zero mover goal writes and zero Stop calls", () => {
            var k = SimpleTrip();
            RunTrip(k);
            Eq(0, k._mover.GoalWrites, "the whole trip never writes a goal");
            Eq(0, k._mover.StopCalls, "the whole trip never stops anything");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "an untouched native goal state stays Off");
        });
        Test("The outbound leg faces its travel direction and never stomps a foreign facing", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, -3);   // behind → side -1
            k.transform.localScale = new Vector3(1, 1.3f, 1);
            UpdateHook(k);
            Eq(Mover.FacingMode.Left, k._mover.facingMode, "the trip takes the facing from Ahead");
            Eq(-1f, k.transform.localScale.x, "the travel direction lands on the x scale");
            Eq(1.3f, k.transform.localScale.y, "the y scale is preserved by the facing write");
            RunTrip(k);
            Eq(Mover.FacingMode.Ahead, k._mover.facingMode, "the facing is returned at the close");

            var b = NewKnight(0); Follower(b, 0); Enemy(b, 3);
            b._mover.facingMode = Mover.FacingMode.Target;        // foreign fixed facing
            UpdateHook(b);
            Eq(Mover.FacingMode.Target, b._mover.facingMode, "a foreign facing is never taken over");
            RunTrip(b);
            Eq(Mover.FacingMode.Target, b._mover.facingMode, "the foreign facing survives the close");
        });
        Test("The Mover.Update yield prefix is narrow and explicitly Priority.Last", () => {
            var patch = typeof(Mover_Update_SamuraiChoreo_Patch).GetCustomAttributes<HarmonyPatch>().FirstOrDefault();
            Check(patch != null && patch.TargetType == typeof(Mover) && patch.MethodName == "Update", "targets Mover.Update");
            var prefix = typeof(Mover_Update_SamuraiChoreo_Patch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
            Check(prefix.GetCustomAttribute<HarmonyPrefix>() != null, "wired as a prefix");
            var priority = prefix.GetCustomAttribute<HarmonyPriority>();
            Check(priority != null && priority.Info == Priority.Last,
                "explicit lowest priority so existing prefixes run first");
        });
        Test("The Mover.Update prefix yields only the trip's own mover", () => {
            var k = SimpleTrip();
            var other = NewKnight(50);
            Check(!MoverPrefix(k._mover), "the trip mover yields");
            Check(MoverPrefix(other._mover), "an unrelated mover runs natively");
            RunTrip(k);
            Check(MoverPrefix(k._mover), "after the close the mover runs natively again");
        });
    }

    // 固定步推进：真实位置结算、两腿实际到达、帧率/物理步解耦、受阻/超时、暂停、看门狗。
    private static void FixedStepRegressions()
    {
        Test("Both legs advance by fixed steps only and close at the frozen start point", () => {
            var k = SimpleTrip();
            var trip = Trip(k);
            var body = k._mover.rigidbody;
            Frame();
            Eq(1, body.MoveCalls, "the first fixed step submits one MovePosition");
            Check(body.position.x > 0f, "the body actually moved by physics");
            RunToHomeLeg(k);
            Check(trip.TurnX >= 6.9f && trip.TurnX <= 7.001f, "the turn happened at the actual outbound endpoint");
            Check(ShouldSlash(k) == false, "slash stays suppressed through both legs");
            RunTrip(k);
            Check(MathF.Abs(body.position.x) <= .0625f + 1e-4f, "the home leg closed on the actual start point");
            Eq(0, k._mover.GoalWrites, "no goal was ever written");
            Check(!k._damageable.invulnerable, "effects retired at the close");
            Check(!Driver(k).enabled, "the driver idled after the close");
        });
        Test("Frame time and fixed-step count are decoupled (same trip, different frame pacing)", () => {
            var slow = SimpleTrip();
            for (int i = 0; i < 200 && TripActive(slow); i++) Frame(.05f, 1);   // 1 physics step per 50 ms frame
            Check(!TripActive(slow), "the 50 ms-frame trip closed");
            int slowMoves = slow._mover.rigidbody.MoveCalls;

            Time.time = 0; Time.frameCount = 0; ResetProductionStatics();
            var fast = SimpleTrip();
            for (int i = 0; i < 200 && TripActive(fast); i++) Frame(.05f, 3);   // 3 physics steps per 50 ms frame
            Check(!TripActive(fast), "the 3-steps-per-frame trip closed");
            int fastMoves = fast._mover.rigidbody.MoveCalls;
            Check(slowMoves >= 38 && slowMoves <= 42, "one route = one fixed-step count (slow pacing): " + slowMoves);
            Eq(slowMoves, fastMoves, "the fixed-step count does not depend on frame time");
        });
        Test("A frame with zero physics steps holds the trip without settlement or damage", () => {
            var k = SimpleTrip();
            var trip = Trip(k);
            Frames(5, .02f, 1);                                  // fly a little
            var late = HitTarget(k._mover.rigidbody.position.x + 1f); Supply(late);
            int scans = Physics2D.CapsuleScans;
            Frames(10, .02f, 0);                                 // no FixedUpdate: no progress, no sweep
            Eq(scans, Physics2D.CapsuleScans, "no sweep without a physics step");
            Eq(0, late.HitCount, "no damage without a physics step");
            Check(TripActive(k), "the trip is still live");
            Check(k._damageable.invulnerable, "effects stay while frozen between physics frames");
            Frame();
            Check(Physics2D.CapsuleScans > scans, "the next physics step resumes the sweep");
            Eq(1, late.HitCount, "the resumed sweep reaches the target exactly once");
        });
        Test("Multiple physics steps inside one frame advance and close exactly once", () => {
            var k = SimpleTrip();
            Frame(.02f, 3);
            Check(Trip(k).Pending, "the last of the three steps submitted the next move");
            RunTrip(k);
            Check(MathF.Abs(k._mover.rigidbody.position.x) <= .0625f + 1e-4f, "multi-step frames still close at home");
            Eq(1, SamuraiDashVisuals.BeginCount(k), "one begin per trip");
        });
        Test("A blocked outbound leg ends the whole trip as blocked, never as a fake arrival", () => {
            var k = SimpleTrip();
            k._mover.rigidbody.Blocked = true;
            Frames(30);                                          // 0.6 s > .5 s stall, well under the 1.2 s window
            Check(!TripActive(k), "the blocked trip closed");
            Eq(1, k._animator.PlayCalls, "no second leg was opened");
            Check(LastCloseLine()?.Contains("step=out-blocked") == true, "the close names the blocked reason: " + LastCloseLine());
            Eq(0f, k._mover.rigidbody.position.x, "the knight stayed at the start point (no teleport)");
            Check(!k._damageable.invulnerable, "effects restored on the abnormal close");
            Check(Driver(k) != null && !Driver(k).enabled, "the driver idled");
        });
        Test("A slow outbound leg hits the leg window and hands the whole trip back to native", () => {
            var k = SimpleTrip();
            k._mover.rigidbody.SpeedCap = 2.5f;                  // 0.05/step: progress, but nowhere near 7 in 1.2 s
            Frames(80);
            Check(!TripActive(k), "the window closed the trip");
            Eq(1, k._animator.PlayCalls, "the outbound timeout never starts a second leg");
            Check(LastCloseLine()?.Contains("step=out-window") == true, "the close names the window: " + LastCloseLine());
            Check(k._mover.rigidbody.position.x < 3f, "it never reached the outbound endpoint");
        });
        Test("A slow home leg closes as home-window, not as complete", () => {
            var k = SimpleTrip();
            RunToHomeLeg(k);
            Check(k._animator.PlayCalls == 2, "the turn replayed the pose");
            k._mover.rigidbody.SpeedCap = 2.5f;
            for (int i = 0; i < 80 && TripActive(k); i++) Frame();
            Check(!TripActive(k), "the home window closed the trip");
            Check(LastCloseLine()?.Contains("step=home-window") == true, "the close names the home window: " + LastCloseLine());
            Check(k._mover.rigidbody.position.x > .5f, "the knight did not arrive");
        });
        Test("A blocked home leg closes as home-blocked", () => {
            var k = SimpleTrip();
            RunToHomeLeg(k);
            k._mover.rigidbody.Blocked = true;
            Frames(30);
            Check(!TripActive(k), "the blocked home leg closed");
            Check(LastCloseLine()?.Contains("step=home-blocked") == true, "the close names the blocked home leg: " + LastCloseLine());
        });
        Test("Menu pause freezes the trip: no progress, no damage, no settlement; resume closes normally", () => {
            var k = SimpleTrip();
            var trip = Trip(k);
            Frames(5);
            var late = HitTarget(k._mover.rigidbody.position.x + 1f); Supply(late);
            float x = k._mover.rigidbody.position.x;
            int scans = Physics2D.CapsuleScans;
            Time.timeScale = 0;
            Frames(10, 0, 0);                                    // paused: no physics steps, Time frozen
            Eq(x, k._mover.rigidbody.position.x, "no paused movement");
            Eq(scans, Physics2D.CapsuleScans, "no paused sweep");
            Eq(0, late.HitCount, "no paused damage");
            Check(TripActive(k) && k._damageable.invulnerable, "the pause does not settle the trip");
            Time.timeScale = 1;
            RunTrip(k);
            Check(LastCloseLine() == null || !LastCloseLine().Contains("tick-cap"), "the pause never trips the watchdog");
            Check(MathF.Abs(k._mover.rigidbody.position.x) <= .0625f + 1e-4f, "the resumed trip closed at home");
        });
        Test("An external Mover.Pause ticks down while the trip keeps its motion authority", () => {
            var k = SimpleTrip();
            Frames(3);
            k._mover._pauseTimeout = 1f;
            k._mover._multiplier = 2f; k._mover._multiplierTimeout = .05f;
            float before = k._mover.rigidbody.position.x;
            Frame();
            Check(k._mover._pauseTimeout < 1f && k._mover._pauseTimeout > .9f,
                "the skipped Mover.Update's pause timer still decays: " + k._mover._pauseTimeout);
            Check(k._mover.rigidbody.position.x > before, "the pause does not take the trip's motion away");
            Eq(2f, k._mover._multiplier, "the multiplier is not reset before its timeout expires");
            Frames(3);
            Eq(1f, k._mover._multiplier, "the skipped Mover.Update's multiplier reset runs on schedule");
            Check(TripActive(k), "the pause does not close the trip");
            RunTrip(k);
        });
        Test("A native goal set mid-trip is inert during the trip and consumed by native after the close", () => {
            var k = SimpleTrip();
            Frame();
            k._mover.SetGoalNoHaglet(50, 5);
            Check(!MoverPrefix(k._mover), "the prefix yields the mover to the trip");
            Eq(50f, k._mover._goalPosition, "the foreign goal is never stopped or rewritten");
            Eq(1, k._mover.GoalWrites, "only the test wrote a goal");
            RunTrip(k);
            Check(!TripActive(k) && MoverPrefix(k._mover), "native consumption resumes at the close");
            float before = k._mover.rigidbody.position.x;
            Frame();
            Check(k._mover.rigidbody.position.x > before, "native now consumes its own goal");
            Eq(0, k._mover.StopCalls, "the trip never calls Stop");
        });
        Test("A complete trip clears its own horizontal velocity and keeps the vertical one", () => {
            var k = SimpleTrip();
            RunToHomeLeg(k);
            k._mover.rigidbody.linearVelocity = new Vector2(18f, -3f);
            RunTrip(k);
            Eq(0f, k._mover.rigidbody.linearVelocity.x, "the skill's horizontal speed is cleared at completion");
            Eq(-3f, k._mover.rigidbody.linearVelocity.y, "vertical velocity is preserved");
        });
        Test("An abnormal close does not touch velocity (native keeps its physics)", () => {
            var k = SimpleTrip();
            Frames(5);
            k._mover.rigidbody.linearVelocity = new Vector2(4f, -2f);
            ModConfig.Enabled.Value = false;                     // hard invalidation, closed by the frame tick
            Frames(2, .02f, 0);
            Check(!TripActive(k), "the hard-invalid trip closed");
            Eq(4f, k._mover.rigidbody.linearVelocity.x, "the abnormal close leaves velocity alone");
            Eq(-2f, k._mover.rigidbody.linearVelocity.y, "the abnormal close leaves vertical velocity alone");
        });
        Test("The driver watchdog retires a trip whose fixed steps stopped", () => {
            var k = SimpleTrip();
            Check(TripActive(k), "trip opened");
            Scanner.ScanTargets.Clear();                        // no retrigger: the self-scan target is gone
            for (int i = 0; i < 7; i++) { Time.time += .5f; Time.frameCount++; UpdateHook(k); }   // 3.5 s of frames, no FixedUpdate
            Check(!TripActive(k), "the watchdog closed the orphaned trip");
            Check(LastCloseLine()?.Contains("step=tick-cap") == true, "the close names the watchdog: " + LastCloseLine());
            Check(!k._damageable.invulnerable, "effects restored by the watchdog");
            Check(!Driver(k).enabled, "the driver idled");
        });
        Test("A retired trip can no longer move, damage or write anything", () => {
            var k = SimpleTrip();
            RunTrip(k);
            int moves = k._mover.rigidbody.MoveCalls;
            var late = HitTarget(0); Supply(late);
            DriverFixed(Driver(k));                              // force the disabled driver entry
            Eq(moves, k._mover.rigidbody.MoveCalls, "no further MovePosition after retirement");
            Eq(0, late.HitCount, "no damage after retirement");
            Eq(0, k._mover.GoalWrites, "no goal write after retirement");
            Check(MoverPrefix(k._mover), "the mover is no longer yielded");
        });
        Test("Two samurai run independent trips toward their own start points", () => {
            var a = NewKnight(0); Follower(a, 0); Enemy(a, 3);
            var b = NewKnight(5); Follower(b, 5); Enemy(b, 2);
            Physics2D.Hits = Array.Empty<Collider2D>();
            UpdateHook(a); UpdateHook(b);
            Eq(7f, Trip(a).OutX, "A heads seven from its own start");
            Eq(-2f, Trip(b).OutX, "B heads seven toward its own enemy side");
            Check(!MoverPrefix(a._mover) && !MoverPrefix(b._mover), "both movers yield");
            for (int i = 0; i < 300 && (TripActive(a) || TripActive(b)); i++) Frame();
            Check(!TripActive(a) && !TripActive(b), "both trips closed");
            Check(MathF.Abs(a._mover.rigidbody.position.x) <= .0625f + 1e-4f, "A is home");
            Check(MathF.Abs(b._mover.rigidbody.position.x - 5f) <= .0625f + 1e-4f, "B is home");
        });
        Test("A moving follower never moves the home target", () => {
            var k = SimpleTrip();
            var f = UnitScanCache.Archers[0];
            Frame();
            f.transform.position = new(19);                      // the squad walks on mid-trip
            RunTrip(k);
            Check(MathF.Abs(k._mover.rigidbody.position.x) <= .0625f + 1e-4f, "home stayed the trip's own start point");
        });
    }

    // 实际线段胶囊：几何契约、父级解析、每腿去重、回调重入、饱和记账、传送豁免。
    private static void SweepRegressions()
    {
        Test("The sweep capsule matches the actually travelled segment (center/size/angle)", () => {
            var k = SimpleTrip();
            Frame();                                             // first fixed step: entry circle + first submission
            var trip = Trip(k);
            Vector2 before = trip.LastActual;                    // the observation the next step reads
            Vector2 sweptEnd = k._mover.rigidbody.position;      // physics applied the submission
            Frame();                                             // second step: sweeps [before, sweptEnd]
            Within(before.x + (sweptEnd.x - before.x) * .5f, Physics2D.LastCapsuleCenter.x, 1e-4f, "capsule center x is the midpoint");
            Within(MathF.Abs(sweptEnd.x - before.x) + 2f * 1.2f, Physics2D.LastCapsuleSize.x, 1e-4f, "capsule length is the actual segment + 2r");
            Within(2.4f, Physics2D.LastCapsuleSize.y, 1e-4f, "capsule height is 2r");
            Eq(CapsuleDirection2D.Horizontal, Physics2D.LastCapsuleDirection, "horizontal capsule");
            Within(0f, Physics2D.LastCapsuleAngle, 1e-3f, "a level segment has angle 0");
            Eq(1 | 2, Physics2D.LastCapsuleMask, "the sweep keeps Enemies and Wildlife");
        });
        Test("A zero-length segment degenerates to a circle of the same radius", () => {
            var k = SimpleTrip();
            Frame();                                             // entry step: no pending submission yet
            Within(2.4f, Physics2D.LastCapsuleSize.x, 1e-4f, "degenerate length = 2r");
            Within(2.4f, Physics2D.LastCapsuleSize.y, 1e-4f, "degenerate height = 2r");
            Within(0f, Physics2D.LastCapsuleAngle, 1e-3f, "degenerate angle 0");
        });
        Test("Diagonal geometry: center, atan2 angle and true capsule coverage", () => {
            var buffer = new Il2CppReferenceArray<Collider2D>(8);
            int count = SamuraiChoreoMotion.SweepSegment(new Vector2(0, 0), new Vector2(3, 4), buffer, 1 | 2);
            Eq(0, count, "no hits supplied");
            var inside = HitTarget(1.5f, 2f);                    // exactly on the spine
            var outside = HitTarget(0.46f, 2.78f);               // 1.3 > r perpendicular from the spine midpoint
            Supply(inside, outside);
            count = SamuraiChoreoMotion.SweepSegment(new Vector2(0, 0), new Vector2(3, 4), buffer, 1 | 2);
            Eq(1, count, "only the target inside the swept capsule is returned");
            Eq(inside.GetComponent<Collider2D>(), buffer[0], "the inside target is the one returned");
            Within(1.5f, Physics2D.LastCapsuleCenter.x, 1e-4f, "diagonal center x");
            Within(2f, Physics2D.LastCapsuleCenter.y, 1e-4f, "diagonal center y");
            Within(5f + 2.4f, Physics2D.LastCapsuleSize.x, 1e-4f, "diagonal length = |b-a| + 2r");
            Within(MathF.Atan2(4, 3) * Mathf.Rad2Deg, Physics2D.LastCapsuleAngle, 1e-3f, "atan2(dy,dx) in degrees");
            Eq(inside.GetComponent<Collider2D>(), buffer[0], "the same buffer reports the hit");
            Eq(1, Physics2D.Buffers.Count, "the caller's buffer is reused, not allocated per query");
        });
        Test("The old managed-array pattern silently loses native writes (counterexample)", () => {
            var managed = new Collider2D[8];
            var wrapper = (Il2CppReferenceArray<Collider2D>)managed;   // the implicit copy the old code relied on
            var target = HitTarget(0f).GetComponent<Collider2D>();
            Physics2D.Hits = new[] { target };
            int count = Physics2D.OverlapCapsuleNonAlloc(new Vector2(0, 0), new Vector2(2.4f, 2.4f),
                CapsuleDirection2D.Horizontal, 0f, wrapper, 1 | 2);
            Check(count > 0, "the native query really wrote a result");
            Check(wrapper[0] == target, "the copy native wrote into holds the target");
            Check(managed[0] == null, "the source managed array never sees it: the old read path is blind");
            Eq(1, Il2CppReferenceArray<Collider2D>.Conversions, "the implicit conversion copied once");
        });
        Test("The resident native buffer is read back through the same wrapper and deals damage", () => {
            var k = SimpleTrip();
            var target = HitTarget(0.5f); Supply(target);
            Frame();
            var trip = Trip(k);
            Check(ReferenceEquals(trip.Colliders, Physics2D.LastBuffer), "the query wrote the trip's own buffer");
            Check(trip.Colliders[0] == target.GetComponent<Collider2D>(), "the wrapper indexer sees the native write");
            Eq(1, target.HitCount, "the same-wrapper read produced a normal hit");
            Eq(0, Il2CppReferenceArray<Collider2D>.Conversions, "production never uses the implicit managed-array copy");
        });
        Test("The sweep buffer is one resident native array reused across every step", () => {
            var k = SimpleTrip();
            var trip = Trip(k);
            Frame();
            Check(ReferenceEquals(trip.Colliders, Physics2D.LastBuffer), "the query wrote the resident buffer");
            int allocations = Il2CppReferenceArray<Collider2D>.Allocations;
            Frame(); Frame();
            Check(ReferenceEquals(trip.Colliders, Physics2D.LastBuffer), "still the same wrapper instance");
            Eq(allocations, Il2CppReferenceArray<Collider2D>.Allocations, "no per-step array construction");
            Eq(0, Il2CppReferenceArray<Collider2D>.Conversions, "no implicit conversion anywhere in the trip");
        });
        Test("A target on the path takes a flat 3 per leg through the native flow, once per leg", () => {
            var k = SimpleTrip();
            k._attackDamage = 6;                                 // 原版普通斩击字段：燕返不读也不写
            var target = HitTarget(3); Supply(target);
            Frames(10);                                          // the outbound capsule repeatedly covers x=3
            var collider = target.GetComponent<Collider2D>();
            Check(Physics2D.ReturnedCount(collider) > 1, "the target really sat inside several outbound sweeps");
            Eq(1, target.HitCount, "no second hit inside the same leg");
            Eq(3, target.TotalDamage, "one leg deals exactly 3");
            Eq(k.gameObject, target.LastAttacker, "same attacker attribution");
            Eq(DamageSource.Knight, target.LastSource, "Knight damage source");
            RunTrip(k);
            Eq(2, target.HitCount, "the return leg strikes the same target once more");
            Eq(6, target.TotalDamage, "two legs deal 3 each");
            Eq(6, k._attackDamage, "the sweep never writes the native attack field");
        });
        Test("A target that only enters on the home path is hit exactly once", () => {
            var k = SimpleTrip();
            RunToHomeLeg(k);
            var late = HitTarget(1.5f); Supply(late);
            RunTrip(k);
            Eq(1, late.HitCount, "the home capsule hit the late target once");
        });
        Test("A parent Damageable is resolved upward and the knight's own is never hit", () => {
            var k = SimpleTrip();
            var parentGo = new GameObject(); parentGo.transform.position = new(2f);
            var parent = parentGo.AddComponent<Damageable>();
            var childGo = new GameObject(); childGo.transform.position = new(2f); childGo.transform.parent = parentGo.transform;
            var child = childGo.AddComponent<Collider2D>();
            var own = k.gameObject.AddComponent<Collider2D>();   // rides the knight's own Damageable
            Physics2D.Hits = new[] { child, own };
            Frames(8);
            Eq(1, parent.HitCount, "the child collider resolved its parent Damageable");
            Eq(0, k._damageable.HitCount, "the knight's own Damageable is explicitly excluded");
        });
        Test("A damage callback that hard-invalidates stops the rest of the same sweep", () => {
            var k = SimpleTrip();
            var first = HitTarget(0.5f); var second = HitTarget(1f);
            Supply(first, second);
            first.OnReceiveDamage = _ => ModConfig.Enabled.Value = false;
            Frame();                                             // entry circle covers both; the callback kills the trip
            Eq(1, first.HitCount, "the first callback actually ran");
            Eq(0, second.HitCount, "the old token cannot damage the second collider after invalidation");
            Check(!TripActive(k), "the trip hard-closed from within the callback");
            Check(!k._damageable.invulnerable, "the close restored the effects");
        });
        Test("A saturated buffer is accounted for, not silently passed off as full coverage", () => {
            var k = SimpleTrip();
            var hits = new List<Damageable>();
            for (int i = 0; i < 70; i++) hits.Add(HitTarget(.1f + i * .001f));
            Supply(hits.ToArray());
            Frame();
            Eq(64, Physics2D.LastBufferLength, "the bounded buffer is 64 entries");
            Check(64 <= hits.Count(h => h.HitCount > 0), "all returned entries were processed");
            var trip = Trip(k);
            Check(trip.SaturationLogged, "the trip recorded the saturation");
            Check(trip.Diagnostics.Lines.Any(l => l.StartsWith("saturated")), "a saturated diagnostic line exists");
        });
        Test("A discontinuous external displacement retires the trip and stops claiming the corridor", () => {
            var k = SimpleTrip();                                 // home 0, out 7
            Frame();                                              // first fixed step: physics lands at .36
            var body = k._mover.rigidbody;
            int moves = body.MoveCalls;
            body.linearVelocity = new Vector2(4f, -2f);           // an external owner's velocity must survive
            k._mover.SetGoalNoHaglet(50, 5);                      // ... and its goal too
            var watcher = HitTarget(19.5f); Supply(watcher);      // only an old resumed corridor could reach it
            var follower = UnitScanCache.Archers[0];
            follower.transform.position = new(20f);               // the squad is with the knight: no walk takeover
            int scans = Physics2D.CapsuleScans;
            body.position = new Vector2(20f, 0f);                 // external displacement beyond the threshold
            FixedStep();                                          // the driver observes the jump
            Check(!TripActive(k), "the displacement retired the trip");
            Check(LastCloseLine()?.Contains("step=external-displacement") == true, "the close names the displacement: " + LastCloseLine());
            Eq(moves, body.MoveCalls, "no MovePosition on the displacement step");
            Eq(scans, Physics2D.CapsuleScans, "the jump segment is not settled");
            Eq(0, watcher.HitCount, "19.5 takes no old-skill damage");
            Check(body.position.x >= 20f, "the landing point is not pulled back");
            Eq(4f, body.linearVelocity.x, "the external velocity survives");
            Eq(-2f, body.linearVelocity.y, "the external vertical velocity survives");
            Eq(50f, k._mover._goalPosition, "the external goal survives");
            Check(!k._damageable.invulnerable, "effects restored");
            Check(!Driver(k).enabled, "the driver idled");
            Check(MoverPrefix(k._mover), "native consumption is released in the same call");
            Frames(3);                                            // native keeps moving toward its own goal
            Eq(moves, body.MoveCalls, "no later submission from the retired token");
            Check(body.position.x > 20f, "native consumption continues");
            Eq(0, watcher.HitCount, "the abandoned corridor stays harmless");
        });
        Test("An in-corridor discontinuous displacement also retires (landing 4)", () => {
            var k = SimpleTrip();
            Frame();
            var body = k._mover.rigidbody;
            int moves = body.MoveCalls;
            body.position = new Vector2(4f, 0f);                  // still inside 0..7, yet beyond the anomaly threshold
            FixedStep();
            Check(!TripActive(k), "the displacement retired the trip");
            Check(LastCloseLine()?.Contains("step=external-displacement") == true, "the close names the displacement");
            Eq(moves, body.MoveCalls, "no MovePosition on the displacement step");
            Check(MathF.Abs(body.position.x - 4f) < 1e-4f, "the landing point is not pulled back");
        });
        Test("A sub-threshold displacement continues the normal sweep", () => {
            var k = SimpleTrip();
            Frame();
            var body = k._mover.rigidbody;
            var trip = Trip(k);
            int moves = body.MoveCalls;
            float from = trip.LastActual.x;
            var target = HitTarget(from + .25f); Supply(target);
            body.position = new Vector2(from + .5f, 0f);          // <= 3x submitted step + .25: treated as travelled
            FixedStep();
            Check(TripActive(k), "a small displacement does not retire the trip");
            Eq(moves + 1, body.MoveCalls, "the normal flow still submits the next step");
            Eq(1, target.HitCount, "the covered sub-threshold segment is swept once");
        });
        Test("A knight that left the captured layer retires the trip and releases native immediately", () => {
            var k = SimpleTrip(); Frame();
            int moves = k._mover.rigidbody.MoveCalls;
            var stray = new GameObject();                        // a different root, outside the game layer
            k.gameObject.transform.parent = stray.transform;     // same scene, same Managers; only the layer relation changed
            Check(TripActive(k), "still live before the gate sees it");
            Check(MoverPrefix(k._mover), "the prefix releases the mover on the same call");
            Check(!TripActive(k), "the layer departure retired the trip");
            Check(LastCloseLine()?.Contains("hard-invalid:left-layer") == true, "the close names left-layer: " + LastCloseLine());
            Eq(moves, k._mover.rigidbody.MoveCalls, "no further submission after the departure");
            Check(!k._damageable.invulnerable, "effects restored");
            k._mover.SetGoalNoHaglet(60, 5);
            float before = k.gameObject.transform.position.x;
            Frame();
            Check(k.gameObject.transform.position.x > before, "native consumption continues");
        });
        Test("An actor/body scene change retires the trip", () => {
            var k = SimpleTrip(); Frame();
            int moves = k._mover.rigidbody.MoveCalls;
            k.gameObject.scene.handle = 7;                       // actor (and its body, same GO) moved to another scene
            Check(MoverPrefix(k._mover), "the prefix releases the mover");
            Check(!TripActive(k), "the scene change retired the trip");
            Check(LastCloseLine()?.Contains("hard-invalid:left-scene") == true, "the close names left-scene: " + LastCloseLine());
            Eq(moves, k._mover.rigidbody.MoveCalls, "no submission after the scene change");
        });
        Test("A same-scene reparent inside the layer is not an interruption", () => {
            var k = SimpleTrip(); Frame();
            var anchor = new GameObject();                       // a child node under the game layer
            anchor.transform.parent = Managers.Inst.world.gameLayer;
            k.gameObject.transform.parent = anchor.transform;    // deeper, same layer, same scene
            Check(!MoverPrefix(k._mover), "the trip keeps the mover inside its layer");
            Check(TripActive(k), "the trip survives a legal reparent");
            RunTrip(k);
            Check(MathF.Abs(k._mover.rigidbody.position.x) <= .0625f + 1e-4f, "the trip still closes at home");
        });
        Test("A knight that is outside the game layer never opens a trip", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            k.gameObject.transform.parent = new GameObject().transform;   // outside the layer before the trigger
            UpdateHook(k);
            Check(!TripActive(k), "the trigger refuses an out-of-layer knight");
            Check(Driver(k) == null, "no driver component was created");
            Check(!k._damageable.invulnerable, "no effects were applied");
            Eq(0, k._mover.GoalWrites, "no goal was written");
            KingdomEnhancedPlugin.Instance.LogSource.Errors.Clear();      // the refusal is logged once on purpose
        });
        Test("An unreadable game layer refuses the trip at birth", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            var layer = Managers.Inst.world.gameLayer;
            Managers.Inst.world.gameLayer = null;
            UpdateHook(k);
            Check(!TripActive(k), "no trip without a readable layer");
            Check(Driver(k) == null, "no driver component was created");
            Managers.Inst.world.gameLayer = layer;
            KingdomEnhancedPlugin.Instance.LogSource.Errors.Clear();
        });
    }

    // 2026-09-25b 真实控制器姿态契约（原样保留，token 换 owner）。
    private static void PoseContractRegressions()
    {
        Test("Both legs play the verified state from frame zero with Land cleared first", () => {
            var k = SimpleTrip();
            Eq(1, k._animator.PlayCalls, "the outbound leg plays the verified state");
            Eq(Hash("Base Layer.PowerSlash"), k._animator.FullPathHash, "the play targets the full path");
            Eq(0, k._animator.TriggerCount, "the verified path never fires the PowerSlash trigger");
            Eq(2, k._animator.ResetCount, "old PowerSlash and Land are cleared before the leg");
            Eq(Hash("PowerSlash"), k._animator.ResetTriggers[0], "the cut trigger is cleared first");
            Eq(Hash("Land"), k._animator.ResetTriggers[1], "the exit trigger is cleared before playback");
            RunToHomeLeg(k);
            Eq(2, k._animator.PlayCalls, "the reverse cut replays the state from frame zero");
            Eq(4, k._animator.ResetCount, "the turn cleared PowerSlash then Land");
            Eq(Hash("PowerSlash"), k._animator.ResetTriggers[^2], "the turn clears the finished cut first");
            Eq(Hash("Land"), k._animator.ResetTriggers[^1], "then the exit trigger before the replay");
            Eq(0, k._animator.TriggerCount, "still no trigger path on a verified controller");
        });
        Test("A complete close fires Land exactly once while sitting in PowerSlash", () => {
            var k = SimpleTrip();
            k._animator.StateHash = Hash("PowerSlash");          // settled in the slash state
            k._animator.FullPathHash = Hash("Base Layer.PowerSlash");
            RunTrip(k);
            Eq(1, LandWrites(k), "Land fired exactly once");
            Eq(0, k._animator.SetTriggers.Count(t => t == Hash("PowerSlash")), "no PowerSlash trigger on the verified path");
        });
        Test("Land is withheld when another state took over or the animator is unreadable", () => {
            var k = SimpleTrip();
            RunToHomeLeg(k);
            k._animator.StateHash = 999; k._animator.FullPathHash = 999;   // Block took over
            RunTrip(k);
            Eq(0, LandWrites(k), "no Land over another state");

            var b = SimpleTrip();
            b._animator.StateHash = Hash("PowerSlash"); b._animator.FullPathHash = Hash("Base Layer.PowerSlash");
            b._animator.enabled = false;                                   // unreadable animator
            RunTrip(b);
            Eq(0, LandWrites(b), "no Land from an unreadable animator");
        });
        Test("A hard-invalidated trip still lands its stuck pose (config or style off)", () => {
            foreach (var hard in new (string Name, Action<Knight> Apply)[] {
                ("config", k => ModConfig.Enabled.Value = false),
                ("style", k => k.Style = 1)
            })
            {
                ModConfig.Enabled.Value = true;
                var k = SimpleTrip();
                k._animator.StateHash = Hash("PowerSlash"); k._animator.FullPathHash = Hash("Base Layer.PowerSlash");
                hard.Apply(k);
                Frames(3);
                Check(!TripActive(k), "the hard invalidation closed the trip (" + hard.Name + ")");
                Eq(1, LandWrites(k), "Land still recovered the pose (" + hard.Name + ")");
            }
        });
        Test("Death never receives Land", () => {
            var k = SimpleTrip();
            k._animator.StateHash = Hash("PowerSlash"); k._animator.FullPathHash = Hash("Base Layer.PowerSlash");
            k._damageable.isDead = true;
            Frames(3);
            Eq(0, LandWrites(k), "death keeps the native Die resolution");
        });
        Test("A replaced controller gets no Land and no turn replay", () => {
            var k = SimpleTrip();
            k._animator.StateHash = Hash("PowerSlash"); k._animator.FullPathHash = Hash("Base Layer.PowerSlash");
            int resets = k._animator.ResetCount;
            k._animator.runtimeAnimatorController = new RuntimeAnimatorController();
            RunTrip(k);
            Eq(1, k._animator.PlayCalls, "only the original outbound leg played");
            Eq(0, k._animator.TriggerCount, "no trigger belongs to the replacement controller");
            Eq(resets, k._animator.ResetCount, "no reset belongs to the replacement controller");
            Eq(0, LandWrites(k), "no Land on a controller never verified");
        });
        Test("Replacing the animator receives no old-trip animation writes", () => {
            var k = SimpleTrip();
            var replacement = new GameObject().AddComponent<Animator>();
            k._animator = replacement;
            RunTrip(k);
            Eq(0, replacement.PlayCalls + replacement.TriggerCount + replacement.ResetCount,
                "the replacement animator receives no replay, trigger or reset");
        });
        Test("An unknown controller falls back to the trigger and closes without Land", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            Physics2D.Hits = Array.Empty<Collider2D>();
            k._animator.OnHasState = (layer, id) => false;      // a controller without the state
            UpdateHook(k);
            Check(TripActive(k) && !Trip(k).PoseKnown, "the verdict is negative");
            Eq(0, k._animator.PlayCalls, "no Play on an unverified controller");
            Eq(1, k._animator.TriggerCount, "the outbound leg used the trigger");
            RunTrip(k);
            Eq(2, k._animator.SetTriggers.Count(t => t == Hash("PowerSlash")), "the turn used the trigger too");
            Eq(0, LandWrites(k), "the fallback path never receives Land");
            Eq(0, k._animator.PlayCalls, "still no Play");
        });
        Test("The HasState verdict is remembered per controller pointer", () => {
            var a = SimpleTrip();
            int probes = 0;
            a._animator.OnHasState = (layer, id) => { probes++; return true; };
            RunTrip(a);
            Eq(0, probes, "the verdict was cached at the trigger probe");
            Time.time += 3.2f; Time.frameCount++;
            var b = NewKnight(0); Follower(b, 0); Enemy(b, 3);
            b._animator.runtimeAnimatorController = a._animator.runtimeAnimatorController;  // shared instance
            b._animator.OnHasState = (layer, id) => { probes++; return true; };
            UpdateHook(b);
            Check(TripActive(b), "the second trip opened");
            Eq(0, probes, "the shared controller answered from the cache");
        });
    }

    // 中断矩阵：松旗标不结束、硬失效一律收尾归还、旧 token 不能碰新 owner。
    private static void InterruptionRegressions()
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
            var k = SimpleTrip();
            Frame();
            soft.Apply(k);
            RunTrip(k);
            Check(MathF.Abs(k._mover.rigidbody.position.x) <= .0625f + 1e-4f, "the trip came home despite " + soft.Name);
            Check(!k._damageable.invulnerable, "the natural close retired the effects");
            Check(!SamuraiDashVisuals.Current.ContainsKey(k.gameObject.GetInstanceID()), "the visual token ended");
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
            var k = SimpleTrip();
            hard.Apply(k);
            Frames(3);
            Check(!TripActive(k), "the hard invalidation closed the trip");
            Check(!k._damageable.invulnerable, "the close released invulnerability");
            Check(!SamuraiDashVisuals.Current.ContainsKey(k.gameObject.GetInstanceID()), "the visual token ended");
            Check(MoverPrefix(k._mover), "native consumption resumes");
        });
        Test("In-flight hard invalidation closes the trip: reused instance id (owner lost)", () => {
            var k = SimpleTrip();
            var replacement = NewKnight(0);
            replacement.gameObject.Id = k.gameObject.Id;         // pooled reuse of the same instance id
            UpdateHook(replacement);
            Check(!TripActive(k), "the stale owner's trip closed on the identity mismatch");
            Check(!k._damageable.invulnerable, "the close released the stale owner's effects");
            Check(!TripActive(replacement), "the replacement knight inherited nothing");
            Eq(0, replacement._mover.GoalWrites, "the replacement mover received nothing");
        });
        Test("A hard invalidation names its clause in the bounded close line", () => {
            var k = SimpleTrip();
            k._damageable.isDead = true;
            Frames(2);
            Check(LastCloseLine()?.Contains("step=hard-invalid:dead") == true, "the close names the hard clause");
        });
        Test("In-flight interruption: mover replacement closes with zero writes to the new mover", () => {
            var k = SimpleTrip();
            var old = k._mover;
            k._mover = k.gameObject.AddComponent<Mover>();
            Frames(3);
            Eq(0, old.StopCalls, "the replaced mover is not stopped");
            Eq(0, k._mover.GoalWrites, "the replacement mover receives nothing");
            Check(!k._damageable.invulnerable, "the close released its effects");
            Check(!TripActive(k), "the trip closed");
        });
        Test("OnDisable mid-trip closes the trip and idles the driver", () => {
            var k = SimpleTrip();
            Check(Driver(k).enabled, "driver active during the trip");
            DisableHook(k);
            Check(!TripActive(k), "OnDisable closed the trip");
            Check(!k._damageable.invulnerable, "OnDisable restored the effects");
            Check(!Driver(k).enabled, "OnDisable idled the driver");
            Check(MoverPrefix(k._mover), "native consumption resumes after OnDisable");
        });
        Test("OnDisable preserves preexisting invulnerability and external trail", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 3);
            k._damageable.invulnerable = true; k._trail.enabled = true;
            UpdateHook(k);
            DisableHook(k);
            Eq(true, k._damageable.invulnerable, "preexisting invulnerability preserved");
            Eq(true, k._trail.enabled, "external trail untouched");
        });
        Test("A completed trip re-enables the same driver component for the next trip", () => {
            var k = SimpleTrip();
            int components = k.gameObject.ComponentCount;
            RunTrip(k);
            Time.time = 6.05f; Time.frameCount++; UpdateHook(k);   // past the six-second trigger cooldown
            Check(TripActive(k), "second trip opened");
            Check(Driver(k).enabled, "the driver is active again");
            Eq(components, k.gameObject.ComponentCount, "no duplicate driver component was added");
            RunTrip(k);
        });
        Test("An identity swap retires the trip: rigidbody replaced", () => {
            var k = SimpleTrip(); Frame();
            var old = k._mover.rigidbody;
            int moves = old.MoveCalls;
            var replacement = new GameObject().AddComponent<Rigidbody2D>();
            k._mover.rigidbody = replacement;
            Frame();
            Check(!TripActive(k), "the body swap retired the trip");
            Eq(moves, old.MoveCalls, "no further MovePosition reaches the old body");
            Eq(0, replacement.MoveCalls, "the replacement body receives nothing");
            Check(MoverPrefix(k._mover), "native consumption resumes");
        });
        Test("An identity swap retires the trip: world / layer / scene", () => {
            var a = SimpleTrip(); Frame();
            Managers.Inst.world = new World();
            Frame();
            Check(!TripActive(a), "the world swap retired the trip");
            Check(LastCloseLine()?.Contains("hard-invalid:world") == true, "the close names the world");

            Managers.Inst = new Managers();
            var b = SimpleTrip(); Frame();
            Managers.Inst.world.gameLayer = new GameObject().transform;
            Frame();
            Check(!TripActive(b), "the layer swap retired the trip");
            Check(LastCloseLine()?.Contains("hard-invalid:layer") == true, "the close names the layer");

            Managers.Inst = new Managers();
            var c = SimpleTrip(); Frame();
            Managers.Inst.world.gameLayer.gameObject.scene.handle = 7;
            Frame();
            Check(!TripActive(c), "the scene swap retired the trip");
            Check(LastCloseLine()?.Contains("hard-invalid:scene") == true, "the close names the scene");
        });
        Test("An identity swap retires the trip: embark and desimulated body", () => {
            var a = SimpleTrip(); Frame();
            a._embarkee.IsEmbarked = true;
            Frame();
            Check(!TripActive(a), "real embark retired the trip");
            Check(LastCloseLine()?.Contains("hard-invalid:embarked") == true, "the close names embarked");

            var b = SimpleTrip(); Frame();
            b._mover.rigidbody.simulated = false;
            Frame();
            Check(!TripActive(b), "a desimulated body retired the trip");
            Check(LastCloseLine()?.Contains("hard-invalid:body-disabled") == true, "the close names body-disabled");
        });
        Test("The prefix validates before yielding: a dead token is retired and native released", () => {
            var k = SimpleTrip();
            k._damageable.isDead = true;                         // no Tick between: only the prefix can see it
            Check(MoverPrefix(k._mover), "the prefix releases the mover instead of yielding to a dead token");
            Check(!TripActive(k), "the invalid token retired in the prefix");
            Check(!k._damageable.invulnerable, "effects restored");
        });
        Test("A persistently throwing sweep retires the trip instead of hiding behind the watchdog", () => {
            var k = SimpleTrip();
            Physics2D.ThrowOnQuery = true;
            Frame();
            Check(!TripActive(k), "the throwing step retired the trip");
            Check(LastCloseLine()?.Contains("step=fixed-step-exception") == true, "the close names the exception");
            Check(!k._damageable.invulnerable, "effects restored");
            Check(MoverPrefix(k._mover), "native consumption is allowed again");
            KingdomEnhancedPlugin.Instance.LogSource.Errors.Clear();   // the exception was logged on purpose
        });
        Test("A last-hit callback that invalidates stops the same step's MovePosition", () => {
            var k = SimpleTrip();
            var only = HitTarget(0.5f); Supply(only);
            only.OnReceiveDamage = _ => k._character.grabbed = true;
            Frame();
            Eq(1, only.HitCount, "the callback ran");
            Check(!TripActive(k), "the grabbed state retired the trip");
            Eq(0, k._mover.rigidbody.MoveCalls, "no MovePosition was submitted after the invalidating callback");
            Check(LastCloseLine()?.Contains("hard-invalid:grabbed") == true, "the close names grabbed");
        });
        Test("The night formation lease probe follows a live trip until it retires", () => {
            var k = SimpleTrip();
            Check(PatchRoles_SamuraiPowerDash.HasActiveMotion(k), "a live trip is reported");
            DisableHook(k);
            Check(!PatchRoles_SamuraiPowerDash.HasActiveMotion(k), "a retired trip is not reported");
        });
        Test("The night formation lease probe is false without an actor record", () => {
            var fresh = NewKnight(0);
            Check(!PatchRoles_SamuraiPowerDash.HasActiveMotion(fresh), "untracked knight has no lease");
            Check(!PatchRoles_SamuraiPowerDash.HasActiveMotion(null), "null knight is safe");
        });
    }

    // 普通返队 walk（与技能完全独立，2026-09-25 裁定后仅剩 run-speed 走位）。
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
            Check(!TripActive(k), "no trip for a walk");
        });
        Test("The walk keeps a live goal while blocked and finishes when the path clears", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.Blocked = true; UpdateHook(k);
            int live = 0;
            for (int i = 0; i < 40; i++) { if (k._mover.goalMode == Mover.GoalMode.Position) live++; Frame(); }
            Eq(40, live, "no tick leaves the stranded knight without a goal");
            Eq(0, SamuraiDashVisuals.BeginCount(k), "a stuck walk never spawns anything");
            k._mover.Blocked = false;
            for (int i = 0; i < 900 && k.transform.position.x > 4.01f; i++) Frame();
            Check(k.transform.position.x <= 4.01f, "the walk itself reached the return threshold");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "arrival released the walk goal");
        });
        Test("Repeated hit-stun pauses end and restart the walk without any burst", () => {
            var k = NewKnight(20); Follower(k, 0); UpdateHook(k);
            for (int i = 0; i < 4; i++)
            {
                k._mover._pauseTimeout = 1f;      // native HandleOnReceiveDamage -> Mover.Pause(1f)
                Frame();                          // the external pause ends the live walk
                Eq(Mover.GoalMode.Off, k._mover.goalMode, "the paused walk released its goal");
                k._mover._pauseTimeout = 0f;      // ... and expires in scaled time
                Time.time += 1f;
                Frame();                          // the tick answers instead of locking
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
            for (int i = 0; i < 900 && k.transform.position.x > 4.01f; i++) Frame();
            Check(k.transform.position.x <= 4.01f, "the walk reached the station");
            Eq(Mover.FacingMode.Ahead, k._mover.facingMode, "facing untouched across the whole walk");
            Eq(0, k._animator.TriggerCount, "no dash pose across the whole walk");
        });
        Test("A foreign fixed facing survives the whole walk", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.facingMode = Mover.FacingMode.Target;
            UpdateHook(k);
            Eq(Mover.FacingMode.Target, k._mover.facingMode, "native Target facing never taken over");
            Check(k._mover.facingTarget == null, "no invented facing target");
            ModConfig.Enabled.Value = false;
            Frame();
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
            Frames(30);
            Eq(writes, k._mover.GoalWrites, "no replacement goal after " + interrupt.Name);
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "walk goal released");
        });
        Test("Night hands the walk over: no mod goal, native guard-slot task only", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover.Blocked = true; UpdateHook(k);
            Frames(40);
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "day walk live before dusk");
            Managers.Inst.kingdom.isDaytime = false;
            int writes = k._mover.GoalWrites;
            Frames(90);
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "mod goal released at dusk");
            Eq(writes, k._mover.GoalWrites, "no walk goal after the night handoff");
            k._mover.Blocked = false;
            k._fsm.OnEnter = state => {
                if (state == Knight.State.GoToWall) { k.isRetreating = true; k._mover.SetGoal(1, 2); }
            };
            NativeFrame(k);
            Eq(1, k._fsm.Requests, "one wall request for the guard slot");
            Eq(1f, k._mover._goalPosition, "the native task owns the way home");
            for (int i = 0; i < 60; i++) Frame();
            Eq(writes + 1, k._mover.GoalWrites, "the mod never fights the native guard-slot goal");
        });
        Test("A long gap closes by plain walk at run speed without any protection", () => {
            var k = NewKnight(25); Follower(k, 0); UpdateHook(k);
            Check(!k._damageable.invulnerable && !k._trail.enabled, "no protection exists to hold");
            Eq(k._runSpeed, k._mover._goalSpeed, "the whole way home is native run speed");
            for (int i = 0; i < 400 && k.transform.position.x > 4.1f; i++) Frame(.01f);
            Check(k.transform.position.x <= 4.1f, "a longer-than-seventeen gap still closes");
            Eq(0, SamuraiDashVisuals.BeginCount(k), "no burst ever existed");
        });
        Test("The walk home runs no hit sweeps and takes no damage", () => {
            var k = NewKnight(25); Follower(k, 0); UpdateHook(k);
            var late = HitTarget(3); Supply(late); int scans = Physics2D.CapsuleScans;
            Frames(20);
            Eq(0, late.HitCount, "the walk does not damage");
            Eq(scans, Physics2D.CapsuleScans, "the walk does not sweep hits");
        });
        Test("A paused frame starts no walk and grants no protection", () => {
            var k = NewKnight(20); Follower(k, 0); Time.timeScale = 0; Time.deltaTime = 0;
            Frames(30, 0, 0);
            Check(!TripActive(k), "no paused start");
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "no paused walk goal");
            Time.timeScale = 1; Time.deltaTime = .02f; UpdateHook(k);
            Eq(Mover.GoalMode.Position, k._mover.goalMode, "the walk opens on the first live tick");
            Eq(k._runSpeed, k._mover._goalSpeed, "at native run speed");
        });
        Test("External mover pause is not cleared by the walk", () => {
            var k = NewKnight(20); Follower(k, 0); k._mover._pauseTimeout = 2; UpdateHook(k);
            Check(k._mover._pauseTimeout >= 2, "external pause unchanged by start"); Frames(5);
            Check(k._mover._pauseTimeout >= 1.8f, "the walk never unpauses an external owner");
        });
        Test("Whole follower cache is not searched every active walk frame", () => {
            var k = NewKnight(30); Follower(k, 0); k._mover.Blocked = true; UpdateHook(k);
            int calls = UnitScanCache.Calls; Frames(30, .01f);
            Check(UnitScanCache.Calls - calls <= 3, "at most interval-based cache lookups over 0.3 seconds");
        });
        Test("Whole follower cache is not searched every attack dash frame", () => {
            var k = SimpleTrip(); int calls = UnitScanCache.Calls;
            Frames(20);
            Check(UnitScanCache.Calls - calls <= 3, "the trip never searches the follower cache");
        });
    }

    private static void ScanRegressions()
    {
        Test("The trigger keeps its own .2 s cadence while a trip cannot open", () => {
            var k = NewKnight(0); Follower(k, 0); Enemy(k, 1);   // inside the 1.5 floor
            Time.time = .25f; UpdateHook(k);
            Eq(1, Scanner.ScanCalls, "one self-scan at the cadence boundary");
            Frames(21, .01f, 0);
            Eq(2, Scanner.ScanCalls, "one more scan once the interval elapses");
        });
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
            UpdatePrefix(k);
            Eq(1, k._fsm.Requests, "one native request"); Eq(Knight.State.Stand, k._fsm.Current, "request does not enter immediately");
            Eq(0, k._mover.GoalWrites, "mod does not supply return goal"); Eq(0, enemy.HitCount, "return is not an attack");
            UpdatePrefix(k); Eq(1, k._fsm.Requests, "same frame cannot requeue");
            k._fsm.Update(); UpdateHook(k); Eq(Knight.State.GoToWall, k._fsm.Current, "next native update enters wall task");
            Check(k.isRetreating, "native fixture supplies retreat posture"); Eq(1f, k._mover._goalPosition, "current native target used");
            Eq(0, SamuraiDashVisuals.BeginCount(k), "no mod return effects");
        });
        Test("Night trips are target-agnostic: no leash, home is the start point", () => {
            Managers.Inst.kingdom.isDaytime = false;
            var k = NewKnight(8); Follower(k, 0); Enemy(k, 13); UpdateHook(k);
            Check(k._damageable.invulnerable, "forward attack began");
            Eq(15f, Trip(k).OutX, "the outbound endpoint is start + fixed seven");
            var body = k._mover.rigidbody;
            body.position = new(13.6f);                          // the old attack leash distance: irrelevant now
            NativeFrame(k);
            Eq(0, k._fsm.Requests, "no native handoff while the trip runs");
            for (int i = 0; i < 200 && TripActive(k); i++) Frame();
            Check(!TripActive(k), "the trip turned and closed at homeX = 8");
            Check(!k._damageable.invulnerable && !k._trail.enabled, "effects retired at the close");
            Check(MathF.Abs(body.position.x - 8f) <= .3f, "home is the start point, not the follower");
        });
        Test("The night handoff resumes only after the trip closes", () => {
            var k = NewKnight(0); var f = Follower(k, 0); Enemy(k, 3);
            Managers.Inst.kingdom.isDaytime = false;
            UpdateHook(k);
            Check(TripActive(k), "night attacks still dash");
            NativeFrame(k);
            Eq(0, k._fsm.Requests, "no wall queue while the trip runs");
            RunTrip(k);
            Eq(Mover.GoalMode.Off, k._mover.goalMode, "the trip finished");
            f.transform.position = new(20);                      // the squad moved on
            Time.time += .25f; Time.frameCount++; NativeFrame(k);
            Eq(1, k._fsm.Requests, "the night handoff resumes once the trip is gone");
        });
        Test("Dusk mid-trip keeps the trip and its own home anchor", () => {
            var k = NewKnight(0); var f = Follower(k, 0); Enemy(k, 3);
            UpdateHook(k);
            Frames(3);
            Managers.Inst.kingdom.isDaytime = false;
            f.transform.position = new(12);
            RunTrip(k);
            Check(MathF.Abs(k._mover.rigidbody.position.x) <= .0625f + 1e-4f, "home is the trip's own start point");
        });
        Test("A retreat onset during an outbound hit callback preserves remaining hits", () => {
            Managers.Inst.kingdom.isDaytime = false;
            var k = SimpleTrip();
            var first = HitTarget(0.5f); var second = HitTarget(1f); Supply(first, second);
            first.OnReceiveDamage = _ => k.isRetreating = true;
            Frame();
            Eq(1, first.HitCount, "the first outbound hit sets retreating");
            Eq(1, second.HitCount, "the second outbound hit is not silenced by retreating");
            Check(TripActive(k), "the same trip survives the callback");
            RunTrip(k);
            Eq(2, first.HitCount, "first target is hit once per leg");
            Eq(2, second.HitCount, "second target is hit once per leg");
        });
    }
}
