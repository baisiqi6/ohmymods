using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

// 自由弓手夜间射击带（α′，archer-night-band）回归套件。两层覆盖：
//  A. 模块级（PatchRoles_ArcherNightBand.TryTakeRedirect）：门序矩阵/深度预检/
//     带闸/两侧与中性侧/带内确定性/白天零变化/遥测预算/零字段写/权限。
//  B. 真实前缀集成（archerband 抽取：DayAssembleSpreadPrefix + MirrorNightArcherGoal +
//     NightParkedFollowerSweep）：深位改写落点与递归守卫、浅位放行、墙外镜像不回归、
//     排除项逐帧 goal 零改写、弩手通道优先、sweep 目标 ≤Cap 带与三态通道。
internal static class Program
{
    private static int passed, failed;
    private const float Eps = 1e-4f;
    private const float Cap = PatchRoles_ArcherNightBand.Cap;
    private const float Spread = PatchRoles_ArcherNightBand.BandSpread;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Eq(float expected, float actual, string message)
    {
        if (MathF.Abs(expected - actual) > Eps) throw new Exception($"{message}: expected {expected}, got {actual}");
    }

    private static void Eq<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{message}: expected {expected}, got {actual}");
    }

    private static void InBand(float depth, string message) =>
        Check(depth >= Cap - Spread - Eps && depth <= Cap + Eps, $"{message}: depth {depth} outside [Cap-{Spread}, Cap]");

    private static void Test(string name, Action body)
    {
        Managers.Inst = new Managers();
        ModConfig.Enabled.Value = true;
        NetworkBigBoss.HasWorldAuth = true;
        PatchRoles_CrossbowDefense.NightGoal = false;
        PatchRoles_CrossbowDefense.Pullbacks = 0;
        SquadFollowGuard.WallArcher = true;
        SquadFollowGuard.WallFollower = true;
        UnityEngine.Random.NextFraction = 0f;
        Mover.FloatIntercept = null;
        ResetBandState();
        ResetPrefixState();
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

    // ---- 状态重置（生产私有静态；与 expedition-follow 的私有字段重置同款） ----
    private static void ResetBandState()
    {
        var t = typeof(PatchRoles_ArcherNightBand);
        t.GetField("_nightLogs", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, 0);
        t.GetField("_firstMismatchLogged", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, false);
    }

    private static void ResetPrefixState()
    {
        var t = typeof(PatchWorld_DefenseSpacing);
        t.GetField("_inSetGoalRedirect", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, false);
        t.GetField("_loggedNightMirror", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, false);
        t.GetField("_loggedNightRegoal", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, false);
        t.GetField("_loggedNightRelocate", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, false);
    }

    // ---- 抽取产物入口 ----
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

    private static bool Intercept(Mover mover, float goal, ref float speed) => Prefix(mover, goal, ref speed);

    private static void Sweep(Kingdom kingdom, params Archer[] archers)
    {
        var method = typeof(PatchWorld_DefenseSpacing)
            .GetMethod("NightParkedFollowerSweep", BindingFlags.Static | BindingFlags.NonPublic);
        if (method == null) throw new Exception("extracted night parked sweep missing");
        method.Invoke(null, new object[] { kingdom, archers });
    }

    // ---- 夹具 ----
    private static void Night() => Managers.Inst.director.currentTime = 20f;

    private static (Archer a, Mover m) NewArcher(Side side = Side.Right, float x = 96f)
    {
        var go = new GameObject();
        var a = go.AddComponent<Archer>();
        var m = go.AddComponent<Mover>();
        a._mover = m;
        a._guardSide = side;
        a._character = go.AddComponent<Character>();
        a._damageable = go.AddComponent<Damageable>();
        go.transform.position = new Vector3(x);
        return (a, m);
    }

    private static bool Redirect(Archer a, float goal, out float target) =>
        PatchRoles_ArcherNightBand.TryTakeRedirect(a, goal, out target);

    private static void Main()
    {
        // ================= A. 模块级 =================

        Test("deep right-side guard goal lands deterministically in the shooting band", () =>
        {
            Night();
            var (a, _) = NewArcher(Side.Right);
            Check(Redirect(a, 90f, out float target), "deep goal redirected");
            InBand(100f - target, "right target");
            for (int i = 0; i < 4; i++)
            {
                Check(Redirect(a, 90f, out float again), "repeat redirect");
                Eq(target, again, "same instanceID → stable target");
            }
            Check(Infos.Count >= 1 && Infos.All(s => s.Contains("[ArcherNightBand/redirect]")),
                "redirect telemetry only");
        });

        Test("deep left-side guard goal mirrors the same band", () =>
        {
            Night();
            var (a, _) = NewArcher(Side.Left);
            Check(Redirect(a, -90f, out float target), "deep left goal redirected");
            InBand((-100f - target) * -1f, "left target");
            Check(target < 0f && target > -100f, "target stays inside the left half");
        });

        Test("shallow, outside and non-finite goals pass through untouched", () =>
        {
            Night();
            var (a, _) = NewArcher(Side.Right);
            Check(!Redirect(a, 93f, out _), "depth 7 (== Cap) passes");
            Check(!Redirect(a, 93.5f, out _), "depth 6.5 passes");
            Check(!Redirect(a, 101.5f, out _), "outside-wall goal passes");
            Check(!Redirect(a, 105f, out _), "far-outside goal passes");
            Check(!Redirect(a, float.NaN, out _), "NaN goal passes");
            Check(!Redirect(a, float.PositiveInfinity, out _), "infinite goal passes");
            Eq(0, Infos.Count, "precheck never logs");
        });

        Test("band gate caps only native-formula-scale targets", () =>
        {
            Night();
            var (a, _) = NewArcher(Side.Right);
            Check(Redirect(a, 88f, out _), "depth 12 (== allowed with nativeDepth 1) redirects");
            var (b, _) = NewArcher(Side.Right);
            Check(!Redirect(b, 87.5f, out _), "depth 12.5 beyond allowed stays native");
            var (c, _) = NewArcher(Side.Right);
            c._minDistanceFromWall = 20f;
            Check(Redirect(c, 80f, out _), "extreme native depth 20 passes max(12, native+1) = 21");
            var (d, _) = NewArcher(Side.Right);
            Check(!Redirect(d, 50f, out _), "unrelated deep goal (depth 50) stays native");
            Check(Infos.Any(s => s.Contains("[ArcherNightBand/mismatch]") && s.Contains("reason=band")),
                "first mismatch names the band gate");
        });

        Test("exclusion matrix leaves every qualified-but-excluded archer native", () =>
        {
            var entries = new (string, Action<Archer>)[]
            {
                ("knight follower", a => a._knight = new Knight()),
                ("guard slot", a => a._guardSlot = new GuardSlot()),
                ("in guard slot", a => a.inGuardSlot = true),
                ("tower height", a => a.transform.position = new Vector3(a.transform.position.x, 3f, 0f)),
                ("formation", a => a.Formation = new Formation()),
                ("player control", a => a.ControlRequested = true),
                ("inert", a => a._character.inert = true),
                ("grabbed", a => a._character.grabbed = true),
                ("stationary", a => a._character.isStationary = true),
                ("no character", a => a._character = null),
                ("embarked", a => a._embarkee.IsEmbarked = true),
                ("targeting embarkable", a => a._embarkee.IsTargetingEmbarkable = true),
                ("embarkable target", a => a._embarkee.EmbarkableTarget = new object()),
                ("crossbowman", a => a.Crossbow = true),
                ("musketeer", a => a.Musketeer = true),
                ("hero", a => a.Hero = true),
                ("dead", a => a._damageable.isDead = true),
                ("not wall duty", a => a.WallDuty = false),
                ("off wall state", a => a.behaviour.latestGoto = 10),
                ("no behaviour", a => a.behaviour = null),
            };
            foreach (var entry in entries)
            {
                Night();
                var (a, _) = NewArcher(Side.Right);
                entry.Item2(a);
                Check(!Redirect(a, 90f, out _), "excluded: " + entry.Item1);
            }
            Check(Infos.Any(s => s.Contains("[ArcherNightBand/mismatch]")), "first mismatch recorded");
        });

        Test("first mismatch names the failing gate", () =>
        {
            Night();
            var (a, _) = NewArcher(Side.Right);
            a.Crossbow = true;
            Check(!Redirect(a, 90f, out _), "crossbowman excluded");
            Check(Infos.Count == 1 && Infos[0].Contains("reason=crossbowman"), "mismatch carries the gate name");
        });

        Test("neutral guard side resolves to the wall nearest the goal", () =>
        {
            Night();
            var (a, _) = NewArcher(Side.Neutral);
            Check(Redirect(a, 90f, out float right), "deep goal on the right redirects");
            InBand(100f - right, "right band");
            var (b, _) = NewArcher(Side.Neutral);
            Check(Redirect(b, -90f, out float left), "deep goal on the left redirects");
            InBand((-100f - left) * -1f, "left band");
        });

        Test("target depth is stable per instance and spread across ids", () =>
        {
            Night();
            var depths = new List<float>();
            var archers = new List<Archer>();
            for (int i = 0; i < 64; i++)
            {
                var (a, _) = NewArcher(Side.Right);
                archers.Add(a);
                Check(Redirect(a, 90f, out float t), "batch redirect");
                depths.Add(100f - t);
            }
            Check(depths.All(d => d >= Cap - Spread - Eps && d <= Cap + Eps), "all inside the band");
            Check(depths.Distinct().Count() > 1, "hash actually spreads ids");
            for (int i = 0; i < archers.Count; i++)
            {
                Check(Redirect(archers[i], 90f, out float t), "repeat redirect");
                Eq(depths[i], 100f - t, "stable per instance");
            }
        });

        Test("night window edges are inclusive", () =>
        {
            var (a, _) = NewArcher(Side.Right);
            Managers.Inst.director.currentTime = 17.5f;
            Check(Redirect(a, 90f, out _), "17.5 is night");
            var (b, _) = NewArcher(Side.Right);
            Managers.Inst.director.currentTime = 5.5f;
            Check(Redirect(b, 90f, out _), "5.5 is night");
            var (c, _) = NewArcher(Side.Right);
            Managers.Inst.director.currentTime = 17.49f;
            Check(!Redirect(c, 90f, out _), "17.49 is day");
        });

        Test("daytime is untouched and refills the nightly telemetry budget", () =>
        {
            Night();
            var (a, _) = NewArcher(Side.Right);
            Check(Redirect(a, 90f, out _), "night redirect");
            Managers.Inst.director.currentTime = 12f;
            var (b, _) = NewArcher(Side.Right);
            Check(!Redirect(b, 90f, out _), "daytime untouched");
            Eq(1, Infos.Count, "daytime adds no log");
            Night();
            var (c, _) = NewArcher(Side.Right);
            Check(Redirect(c, 90f, out _), "next night redirect");
            Eq(2, Infos.Count, "budget refilled after daytime");
        });

        Test("nightly telemetry budget is a hard four lines", () =>
        {
            Night();
            for (int i = 0; i < 6; i++)
            {
                var (a, _) = NewArcher(Side.Right);
                Check(Redirect(a, 90f, out _), "batch redirect");
            }
            Eq(4, Infos.Count, "four lines max");
            var (x, _) = NewArcher(Side.Right);
            x.Crossbow = true;
            Check(!Redirect(x, 90f, out _), "excluded after budget");
            Eq(4, Infos.Count, "no fifth line");
            Managers.Inst.director.currentTime = 12f;
            var (y, _) = NewArcher(Side.Right);
            Check(!Redirect(y, 90f, out _), "day resets");
            Eq(4, Infos.Count, "day itself adds nothing");
            Night();
            var (z, _) = NewArcher(Side.Right);
            Check(Redirect(z, 90f, out _), "new night redirect");
            Eq(5, Infos.Count, "one fresh line after reset");
        });

        Test("mod switch and authority gate all rewrites", () =>
        {
            Night();
            var (a, _) = NewArcher(Side.Right);
            ModConfig.Enabled.Value = false;
            Check(!Redirect(a, 90f, out _), "mod off");
            ModConfig.Enabled.Value = true;
            NetworkBigBoss.HasWorldAuth = false;
            Check(!Redirect(a, 90f, out _), "client");
            Eq(0, Infos.Count, "gated calls log nothing");
        });

        Test("redirect writes no actor fields (zero persistent writes)", () =>
        {
            Night();
            var (a, _) = NewArcher(Side.Right);
            int depth = a._guardDepth;
            float min = a._minDistanceFromWall, spacing = a._unitSpacingAtWall, random = a._guardRandomOffset;
            Vector3 pos = a.transform.position;
            Check(Redirect(a, 90f, out _), "redirect");
            Eq(depth, a._guardDepth, "guardDepth untouched");
            Eq(min, a._minDistanceFromWall, "min untouched");
            Eq(spacing, a._unitSpacingAtWall, "spacing untouched");
            Eq(random, a._guardRandomOffset, "random untouched");
            Check(pos.x == a.transform.position.x && pos.y == a.transform.position.y, "position untouched");
        });

        // ================= B. 真实前缀集成 =================

        Test("extracted prefix rewrites a deep archer goal under the recursion guard", () =>
        {
            Night();
            Mover.FloatIntercept = Intercept;
            var (a, m) = NewArcher(Side.Right);
            m.SetGoal(90f, 2f);
            Eq(1, m.NativeFloatGoals, "one native goal execution (no recursion)");
            InBand(100f - m.Goal, "native goal replaced by a band target");
            Eq(2f, m.Speed, "existing speed chain preserved");
            m.SetGoal(95f, 2f);
            Eq(2, m.NativeFloatGoals, "guard cleared: next goal lands normally");
            Eq(95f, m.Goal, "shallow goal untouched");
        });

        Test("deep left-side goal is rewritten through the prefix with the left sign", () =>
        {
            Night();
            Mover.FloatIntercept = Intercept;
            var (a, m) = NewArcher(Side.Left, -96f);
            m.SetGoal(-90f, 2f);
            Eq(1, m.NativeFloatGoals, "one execution");
            InBand((-100f - m.Goal) * -1f, "left band");
            Check(m.Goal < -90f, "moved toward the left wall");
        });

        Test("outside-narrow-band mirror still mirrors to 0.5 inside", () =>
        {
            Night();
            Mover.FloatIntercept = Intercept;
            var (a, m) = NewArcher(Side.Right);
            m.SetGoal(101.5f, 2f); // 墙外 1.5 → 镜像到墙内 2.0
            Eq(1, m.NativeFloatGoals, "mirror redirect recorded once");
            Eq(98f, m.Goal, "mirrored to depth 2.0 inside");
        });

        Test("excluded formation goals stay native even when reissued every frame", () =>
        {
            Night();
            Mover.FloatIntercept = Intercept;
            var (a, m) = NewArcher(Side.Right);
            a.Formation = new Formation();
            for (int i = 0; i < 10; i++) m.SetGoal(90f, 2f);
            Eq(10, m.NativeFloatGoals, "every issue lands native");
            Eq(90f, m.Goal, "goal never rewritten");
        });

        Test("crossbowman deep goals never enter the archer band", () =>
        {
            Night();
            Mover.FloatIntercept = Intercept;
            var (a, m) = NewArcher(Side.Right);
            a.Crossbow = true;
            m.SetGoal(90f, 2f);
            Eq(1, m.NativeFloatGoals, "crossbow goal native");
            Eq(90f, m.Goal, "unchanged by the archer band");
        });

        Test("crossbowman wall goal still takes the crossbow deepening path", () =>
        {
            Night();
            Mover.FloatIntercept = Intercept;
            PatchRoles_CrossbowDefense.NightGoal = true;
            PatchRoles_CrossbowDefense.NightGoalX = 94f;
            var (a, m) = NewArcher(Side.Right);
            a.Crossbow = true;
            m.SetGoal(90f, 2f);
            Eq(1, m.NativeFloatGoals, "single redirect");
            Eq(94f, m.Goal, "crossbow target wins");
        });

        Test("knight movers never enter the archer band", () =>
        {
            Night();
            Mover.FloatIntercept = Intercept;
            var kg = new GameObject();
            kg.AddComponent<Knight>();
            var km = kg.AddComponent<Mover>();
            km.SetGoal(50f, 4f);
            Eq(1, km.NativeFloatGoals, "knight goal reaches native");
            Eq(50f, km.Goal, "unchanged");
        });

        Test("parked sweep relocates outside archers into the ≤Cap band", () =>
        {
            Night();
            var (a, m) = NewArcher(Side.Right, 102f); // 墙外 depth −2
            UnityEngine.Random.NextFraction = 0f;    // 下界 Cap−2
            Sweep(Managers.Inst.kingdom, a);
            Eq(1, m.NativeFloatGoals, "one relocation goal");
            Eq(95f, m.Goal, "lower bound wall-5");
            float depth = (100f - m.Goal) * 1f;
            Check(depth <= Cap + Eps && depth >= Cap - 2f - Eps, "inside the ≤Cap band");

            var (b, n) = NewArcher(Side.Right, 102f);
            UnityEngine.Random.NextFraction = 1f;    // 上界 Cap
            Sweep(Managers.Inst.kingdom, b);
            Eq(1, n.NativeFloatGoals, "upper bound goal");
            Eq(93f, n.Goal, "upper bound wall-7");
        });

        Test("parked sweep skips towers, in-band archers and keeps both alternate channels", () =>
        {
            Night();
            var (tower, tm) = NewArcher(Side.Right, 102f);
            tower.inGuardSlot = true;
            var (high, hm) = NewArcher(Side.Right, 102f);
            high.transform.position = new Vector3(102f, 3f, 0f);
            var (inside, im) = NewArcher(Side.Right, 95f); // 墙内 depth 5
            Sweep(Managers.Inst.kingdom, tower, high, inside);
            Eq(0, tm.NativeFloatGoals, "tower slot skipped");
            Eq(0, hm.NativeFloatGoals, "tower height skipped");
            Eq(0, im.NativeFloatGoals, "in-band archer untouched");

            var kg = new GameObject();
            var k = kg.AddComponent<Knight>();
            var (f, fm) = NewArcher(Side.Right, 102f);
            f._knight = k;
            Sweep(Managers.Inst.kingdom, f);
            Eq(0, fm.NativeFloatGoals, "follower takes the re-goal path only");
            Eq(1, fm.ObjectGoals, "native follow reissued");
            Check(ReferenceEquals(kg, fm.ObjectGoal), "goal is the knight object");
            Eq(-1f, fm.ObjectOffset, "native knightFollowDistance offset");
            Eq(Mover.OffsetMode.Formation, fm.ObjectMode, "native formation mode");

            var (x, xm) = NewArcher(Side.Right, 102f);
            x.Crossbow = true;
            Sweep(Managers.Inst.kingdom, x);
            Eq(0, xm.NativeFloatGoals, "crossbow relocation left to its pullback");
            Eq(1, PatchRoles_CrossbowDefense.Pullbacks, "crossbow pullback invoked");
        });

        Test("parked sweep is a no-op by day", () =>
        {
            Managers.Inst.director.currentTime = 12f;
            var (a, m) = NewArcher(Side.Right, 102f);
            Sweep(Managers.Inst.kingdom, a);
            Eq(0, m.NativeFloatGoals, "daytime sweep writes nothing");
        });

        Console.WriteLine($"RESULT {passed} passed, {failed} failed");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }

    private static List<string> Infos => KingdomEnhancedPlugin.Instance.LogSource.Infos;
}
