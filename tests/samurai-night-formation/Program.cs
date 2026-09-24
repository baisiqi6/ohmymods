using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

// 武士夜间贴墙紧凑列队回归套件。
// 两层覆盖：
//  A. 模块级（PatchRoles_SamuraiNightFormation）：槽位公式/符号 fixture、翻假五态、
//     租约与权限门、override/回退锚、等秩 tie-break、活性日志。
//  B. 真实前缀集成：ExtractSource 从当前 PatchWorld_DefenseSpacing.cs 抽取出的
//     DayAssembleSpreadPrefix + Mover.SetGoal(float,float) hook（与 tests/samurai-retreat
//     同一抽取模式），验证重定向落位与 _inSetGoalRedirect 守卫。
internal static class Program
{
    private static readonly List<Knight> Units = new();
    private static int passed, failed;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Eq<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{message}: expected {expected}, got {actual}");
    }

    private static void Near(float expected, float actual, string message)
    {
        if (MathF.Abs(expected - actual) > 1e-4f)
            throw new Exception($"{message}: expected {expected}, got {actual}");
    }

    private static void Test(string name, Action body)
    {
        Units.Clear();
        UnitScanCache.Knights = Array.Empty<Knight>();
        UnitScanCache.Calls = 0;
        ModConfig.Enabled.Value = true;
        NetworkBigBoss.HasWorldAuth = true;
        PatchRoles_SamuraiPowerDash.MotionActive = false;
        Managers.Inst = new Managers();
        Time.time = 100f;
        Time.timeScale = 1f;
        KingdomEnhancedPlugin.Instance.LogSource.Infos.Clear();
        KingdomEnhancedPlugin.Instance.LogSource.Errors.Clear();
        try
        {
            body();
            Eq(0, KingdomEnhancedPlugin.Instance.LogSource.Errors.Count, "production error logs");
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + e.GetBaseException().Message);
        }
    }

    private static void Night() => Managers.Inst.kingdom.isDaytime = false;

    private static Knight NewKnight(Side side, int rank, float x, int style = 2)
    {
        var go = new GameObject();
        var k = go.AddComponent<Knight>();
        k.side = side;
        k.rank = rank;
        k.Style = style;
        k.KnownStyle = true;
        k._distanceFromWall = 1f;
        k.transform.position = new Vector3(x);
        k._mover = go.AddComponent<Mover>();
        k._damageable = go.AddComponent<Damageable>();
        k._character = go.AddComponent<Character>();
        k._fsm.Current = Knight.State.GoToWall;
        Units.Add(k);
        UnitScanCache.Knights = Units.ToArray();
        return k;
    }

    private static float NativeGoal(Knight k)
    {
        var guard = Managers.Inst.kingdom.GetGuardPosition(k.side);
        return guard.Value - (float)guard.Key * (k._distanceFromWall * k.rank);
    }

    private static bool Redirect(Knight k, out float slot) =>
        PatchRoles_SamuraiNightFormation.TryTakeRedirect(k, k._mover, NativeGoal(k), out slot);

    private static bool Postfix(Knight k, bool native)
    {
        bool result = native;
        PatchRoles_SamuraiNightFormation.AfterShouldGoToWall(k, ref result);
        return result;
    }

    private static bool Prefix(Mover mover, float goal, ref float speed)
    {
        var method = typeof(Mover_DefenseSpacing_DayAssemble_Spread_Patch)
            .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
        if (method == null) throw new Exception("extracted SetGoal prefix hook missing");
        object[] args = { mover, goal, speed };
        bool run = (bool)method.Invoke(null, args);
        speed = (float)args[2];
        return run;
    }

    private static int FormationLogs() => KingdomEnhancedPlugin.Instance.LogSource.Infos
        .Count(line => line.StartsWith("[SamuraiNightFormation", StringComparison.Ordinal));

    private static void Main()
    {
        // ---- A1. 槽位公式与符号 fixture（P0） ----
        Test("Right-side slots step inside the wall and the first slot clears it", () =>
        {
            var a = NewKnight(Side.Right, 1, 40);
            var b = NewKnight(Side.Right, 2, 40);
            Night();
            Check(Redirect(a, out float s0), "rank 1 redirects to a compact slot");
            Check(Redirect(b, out float s1), "rank 2 redirects to a compact slot");
            Near(98.5f, s0, "slot(0) = anchor - 1.5");
            Near(98.0f, s1, "slot(1) = anchor - 2.0");
            Check(s1 < s0 && s0 < 100f, "right slots step inward from the wall");
        });
        Test("Left-side slots mirror the wall step", () =>
        {
            var a = NewKnight(Side.Left, 1, -40);
            var b = NewKnight(Side.Left, 2, -40);
            Night();
            Check(Redirect(a, out float s0), "left rank 1 redirects");
            Check(Redirect(b, out float s1), "left rank 2 redirects");
            Near(-98.5f, s0, "slot(0) mirrors on the left");
            Near(-98.0f, s1, "slot(1) mirrors on the left");
            Check(-100f < s0 && s0 < s1, "left slots step inward");
        });
        Test("Sparse ranks compact to consecutive indexes", () =>
        {
            var a = NewKnight(Side.Right, 1, 40);
            var b = NewKnight(Side.Right, 3, 40);
            var c = NewKnight(Side.Right, 7, 40);
            Night();
            Redirect(a, out float s0);
            Redirect(b, out float s1);
            Redirect(c, out float s2);
            Near(98.5f, s0, "rank 1 -> index 0");
            Near(98.0f, s1, "rank 3 -> index 1");
            Near(97.5f, s2, "rank 7 -> index 2");
        });
        Test("Equal ranks keep instanceID order across cache rebuilds", () =>
        {
            var a = NewKnight(Side.Right, 3, 40);
            var b = NewKnight(Side.Right, 3, 40);
            Night();
            Check(a.gameObject.GetInstanceID() < b.gameObject.GetInstanceID(), "fixture instance order");
            Redirect(a, out float a1);
            Redirect(b, out float b1);
            Near(98.5f, a1, "lower instanceID takes the shallow slot");
            Near(98.0f, b1, "tie-break takes the next slot");
            Time.time += 5f; // past the 3s TTL -> rebuild
            Redirect(b, out float b2);
            Redirect(a, out float a2);
            Near(a1, a2, "no swap for the first knight after rebuild");
            Near(b1, b2, "no swap for the second knight after rebuild");
        });
        Test("Rank churn re-sorts inside the cache window", () =>
        {
            var a = NewKnight(Side.Right, 1, 40);
            var b = NewKnight(Side.Right, 2, 40);
            Night();
            Redirect(a, out float a1);
            Redirect(b, out float b1);
            Near(98.5f, a1, "rank 1 starts at index 0");
            Near(98.0f, b1, "rank 2 starts at index 1");
            a.rank = 2;
            b.rank = 1;
            Time.time += 5f;
            Redirect(b, out float b2);
            Redirect(a, out float a2);
            Near(98.5f, b2, "new rank 1 takes the shallow slot");
            Near(98.0f, a2, "the other rank follows");
        });
        Test("Override guard anchor uses the returned pair", () =>
        {
            Managers.Inst.kingdom.GuardResolver = _ => new KeyValuePair<Side, float>(Side.Right, 60f);
            var l = NewKnight(Side.Left, 1, 40);
            var r = NewKnight(Side.Right, 2, 40);
            Night();
            Check(Redirect(l, out float ls), "left knight redirects through the override");
            Check(Redirect(r, out float rs), "right knight redirects through the override");
            Near(58.5f, ls, "Left knight uses the RETURNED Right key");
            Check(ls < 60f, "override slot stays inside the returned anchor");
            Near(58.0f, rs, "both sides share one compact row when the override merges sides");
        });
        Test("Fallback anchor without an intact wall still steps inside", () =>
        {
            Managers.Inst.kingdom.GuardResolver = side =>
                new KeyValuePair<Side, float>(side, side == Side.Right ? 40f : -40f);
            var k = NewKnight(Side.Right, 1, 40);
            Night();
            Check(Redirect(k, out float slot), "fallback anchor redirects");
            Near(38.5f, slot, "fallback slot still uses the compact formula");
        });

        // ---- A2. 翻假防御（P1-B / P1-E） ----
        Test("At-post flip faces the samurai outward", () =>
        {
            var r = NewKnight(Side.Right, 1, 40);
            var l = NewKnight(Side.Left, 1, -40);
            Night();
            Check(Redirect(r, out float rs), "right knight redirects");
            Check(Redirect(l, out float ls), "left knight redirects");
            r.transform.position = new Vector3(rs);
            Check(!Postfix(r, true), "right knight at its slot flips to false");
            Eq(1, r._mover.DirectionWrites, "outward facing written once");
            Near(1f, r._mover.transform.localScale.x, "right knight faces outward (+1)");
            l.transform.position = new Vector3(ls);
            Check(!Postfix(l, true), "left knight at its slot flips to false");
            Near(-1f, l._mover.transform.localScale.x, "left knight faces outward (-1)");
        });
        Test("Flip window is half the slot spacing", () =>
        {
            var k = NewKnight(Side.Right, 1, 40);
            Night();
            Check(Redirect(k, out float slot), "slot");
            k.transform.position = new Vector3(slot + 0.2f);
            Check(!Postfix(k, true), "inside 0.25 flips");
            k.transform.position = new Vector3(slot + 0.24f);
            Check(!Postfix(k, true), "near the boundary still flips");
            k.transform.position = new Vector3(slot + 0.26f);
            Check(Postfix(k, true), "just outside the window stays native");
            k.transform.position = new Vector3(slot + 0.8f);
            Check(Postfix(k, true), "0.8 displacement stays native");
            int writes = k._mover.DirectionWrites;
            bool alreadyFalse = false;
            PatchRoles_SamuraiNightFormation.AfterShouldGoToWall(k, ref alreadyFalse);
            Check(!alreadyFalse, "false stays false");
            Eq(writes, k._mover.DirectionWrites, "no facing write on an already-false result");
        });
        Test("Serpent near keeps the emergency wall return", () =>
        {
            var k = NewKnight(Side.Right, 1, 40);
            Night();
            Check(Redirect(k, out float slot), "slot");
            k.transform.position = new Vector3(slot);
            Managers.Inst.kingdom.Serpent = new WorldEatingSerpent { Position = slot + 2f, AttackDistance = 3f };
            Check(Postfix(k, true), "serpent within attack distance keeps native true");
            Managers.Inst.kingdom.Serpent = new WorldEatingSerpent { Position = slot + 10f, AttackDistance = 3f };
            Check(!Postfix(k, true), "serpent away flips at post");
        });
        Test("Non-samurai, daytime, disabled and unresolved stay native", () =>
        {
            var other = NewKnight(Side.Right, 1, 40, style: 3);
            Night();
            other.transform.position = new Vector3(98.5f);
            Check(Postfix(other, true), "non-samurai untouched");

            var sam = NewKnight(Side.Right, 1, 40);
            Check(Redirect(sam, out float slot), "samurai slot");
            sam.transform.position = new Vector3(slot + 0.8f);
            Check(Postfix(sam, true), "displaced samurai stays native");
            sam.transform.position = new Vector3(slot);
            ModConfig.Enabled.Value = false;
            Check(Postfix(sam, true), "master switch off stays native");
            ModConfig.Enabled.Value = true;
            NetworkBigBoss.HasWorldAuth = false;
            Check(Postfix(sam, true), "client without world auth stays native");
            NetworkBigBoss.HasWorldAuth = true;
            sam.KnownStyle = false;
            Check(Postfix(sam, true), "unresolved style stays native");
            sam.KnownStyle = true;
            Managers.Inst.kingdom.isDaytime = true;
            Check(Postfix(sam, true), "daytime stays native");
        });

        // ---- A3. 重定向门（P1-C / P1-D） ----
        Test("Redirect takes only the native guard goal in the GoToWall state", () =>
        {
            var k = NewKnight(Side.Right, 1, 40);
            Night();
            float native = NativeGoal(k);
            Check(PatchRoles_SamuraiNightFormation.TryTakeRedirect(k, k._mover, native + 5e-4f, out _),
                "goal inside epsilon redirects");
            Check(!PatchRoles_SamuraiNightFormation.TryTakeRedirect(k, k._mover, native + 2e-3f, out _),
                "goal outside epsilon stays native");
            Check(!PatchRoles_SamuraiNightFormation.TryTakeRedirect(k, k._mover, float.NaN, out _),
                "NaN goal stays native");
            k._fsm.Current = Knight.State.Stand;
            Check(!PatchRoles_SamuraiNightFormation.TryTakeRedirect(k, k._mover, native, out _),
                "non-wall state stays native");
            k._fsm.Current = Knight.State.GoToWall;
            var other = NewKnight(Side.Right, 2, 40);
            Check(!PatchRoles_SamuraiNightFormation.TryTakeRedirect(k, other._mover, native, out _),
                "another knight's mover stays native");
            var stray = new GameObject().AddComponent<Mover>();
            Check(!PatchRoles_SamuraiNightFormation.TryTakeRedirect(k, stray, native, out _),
                "foreign mover stays native");
        });
        Test("Redirect gate stays closed by day", () =>
        {
            var k = NewKnight(Side.Right, 1, 40);
            Managers.Inst.kingdom.isDaytime = true;
            Check(!PatchRoles_SamuraiNightFormation.TryTakeRedirect(k, k._mover, NativeGoal(k), out _),
                "daytime never redirects");
        });
        Test("Redirect obeys permission gates and motion leases", () =>
        {
            var k = NewKnight(Side.Right, 1, 40);
            Night();
            float native = NativeGoal(k);
            PatchRoles_SamuraiPowerDash.MotionActive = true;
            Check(!PatchRoles_SamuraiNightFormation.TryTakeRedirect(k, k._mover, native, out _),
                "active lease suppresses the redirect");
            PatchRoles_SamuraiPowerDash.MotionActive = false;
            Check(PatchRoles_SamuraiNightFormation.TryTakeRedirect(k, k._mover, native, out _),
                "no lease redirects");
            ModConfig.Enabled.Value = false;
            Check(!PatchRoles_SamuraiNightFormation.TryTakeRedirect(k, k._mover, native, out _),
                "master switch off stays native");
            ModConfig.Enabled.Value = true;
            NetworkBigBoss.HasWorldAuth = false;
            Check(!PatchRoles_SamuraiNightFormation.TryTakeRedirect(k, k._mover, native, out _),
                "client stays native");
            NetworkBigBoss.HasWorldAuth = true;
            k.Style = 3;
            Check(!PatchRoles_SamuraiNightFormation.TryTakeRedirect(k, k._mover, native, out _),
                "non-samurai stays native");
            k.Style = 2;
            k.KnownStyle = false;
            Check(!PatchRoles_SamuraiNightFormation.TryTakeRedirect(k, k._mover, native, out _),
                "unresolved style stays native");
        });
        Test("Samurai outside the scan and null inputs stay native", () =>
        {
            var k = NewKnight(Side.Right, 1, 40);
            Night();
            Check(Redirect(k, out _), "baseline redirect");
            UnitScanCache.Knights = Array.Empty<Knight>();
            Time.time += 5f; // past the shared cache TTL -> the roster is empty again
            Check(!Redirect(k, out _), "samurai absent from the scan falls back to native");
            Check(!PatchRoles_SamuraiNightFormation.TryTakeRedirect(null, k._mover, NativeGoal(k), out _),
                "null knight stays native");
            Check(!PatchRoles_SamuraiNightFormation.TryTakeRedirect(k, null, NativeGoal(k), out _),
                "null mover stays native");
        });

        // ---- A4. 活性日志 ----
        Test("Activity log is bounded per night and proves the postfix flip", () =>
        {
            var k = NewKnight(Side.Right, 1, 40);
            Night();
            Check(Redirect(k, out float slot), "redirect");
            k.transform.position = new Vector3(slot);
            Check(!Postfix(k, true), "at-post flip");
            int firstNight = FormationLogs();
            Check(firstNight >= 1, "flip activity is logged");
            for (int i = 0; i < 5; i++)
            {
                k.transform.position = new Vector3(slot + 0.1f);
                Check(!Postfix(k, true), "still flipping");
            }
            int bounded = FormationLogs();
            Check(bounded <= 4, "per-night log budget is bounded at four lines");
            Managers.Inst.kingdom.isDaytime = true;
            bool dayResult = true;
            PatchRoles_SamuraiNightFormation.AfterShouldGoToWall(k, ref dayResult);
            Check(dayResult, "day keeps the native value");
            Managers.Inst.kingdom.isDaytime = false;
            k.transform.position = new Vector3(slot);
            Check(!Postfix(k, true), "next night flips again");
            Check(FormationLogs() > bounded && FormationLogs() <= 8, "budget resets for the next night");
        });
        Test("ShouldGoToWall postfix hook is wired to the module", () =>
        {
            var hookType = typeof(PatchRoles_SamuraiNightFormation).Assembly.GetTypes().FirstOrDefault(t =>
                t.GetCustomAttributes<HarmonyLib.HarmonyPatch>()
                    .Any(a => a.Target == typeof(Knight) && a.Name == "ShouldGoToWall"));
            Check(hookType != null, "hook declared for Knight.ShouldGoToWall");
            var postfix = hookType.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
            Check(postfix != null, "postfix method present");
            var k = NewKnight(Side.Right, 1, 40);
            Night();
            Check(Redirect(k, out float slot), "slot");
            k.transform.position = new Vector3(slot);
            object[] args = { k, true };
            postfix.Invoke(null, args);
            Check(!(bool)args[1], "postfix delegates to the module and flips at post");
        });

        // ---- B. 真实前缀集成（抽取的 DayAssembleSpreadPrefix） ----
        Test("Real SetGoal prefix rewrites the native guard goal to the compact slot", () =>
        {
            var k = NewKnight(Side.Right, 1, 40);
            Night();
            float speed = 6f;
            Check(!Prefix(k._mover, NativeGoal(k), ref speed), "prefix replaces the native goal");
            Near(98.5f, k._mover._goalPosition, "compact slot written through the real prefix");
            Near(speed, k._mover._goalSpeed, "post-Adjust speed forwarded");
            Eq(1, k._mover.PositionCalls, "exactly one rewrite");
        });
        Test("Real SetGoal prefix keeps the knight branch native by day", () =>
        {
            var k = NewKnight(Side.Right, 1, 40);
            Managers.Inst.kingdom.isDaytime = true; // stub default is false (night)
            float speed = 6f;
            Check(Prefix(k._mover, NativeGoal(k), ref speed), "native call continues by day");
            Eq(0, k._mover.PositionCalls, "no goal written by day");
        });
        Test("Real SetGoal prefix guard passes our own rewritten goal straight through", () =>
        {
            var k = NewKnight(Side.Right, 1, 40);
            Night();
            int reentries = 0;
            bool reentryRun = false;
            float reentrySpeed = -1f;
            k._mover.OnFloatGoal = () =>
            {
                reentries++;
                float s = k._mover._goalSpeed;
                reentryRun = Prefix(k._mover, k._mover._goalPosition, ref s);
                reentrySpeed = s;
            };
            float speed = 6f;
            Check(!Prefix(k._mover, NativeGoal(k), ref speed), "outer redirect");
            Eq(1, reentries, "rewritten SetGoal re-entered the prefix");
            Check(reentryRun, "guard returns pass-through, no recursion");
            Near(speed, reentrySpeed, "speed survives the re-entry");
        });
        Test("Real SetGoal prefix leaves lease-owned goals native", () =>
        {
            var k = NewKnight(Side.Right, 1, 40);
            Night();
            PatchRoles_SamuraiPowerDash.MotionActive = true;
            float speed = 6f;
            Check(Prefix(k._mover, NativeGoal(k), ref speed), "native call continues while a lease is active");
            Eq(0, k._mover.PositionCalls, "no rewrite while a lease owns the goal");
        });

        Console.WriteLine($"RESULT {passed} passed, {failed} failed");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }
}
