using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using OhMyMods.AndroidProbe;
using Config = KingdomEnhancedMod.ModConfig;
using EnemyPatch = KingdomEnhancedMod.PatchWorld_EnemyManager;

namespace OhMyMods.AndroidProbe.Tests;

internal static class Checks
{
    internal static int Passed;
    internal static int Failed;

    // Locates the repository checkout the tests were built from, independent of the
    // working directory, by walking up from this source file's compiled-in path.
    internal static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string path = null)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path)));
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
            directory = directory.Parent;
        }
        return Path.GetDirectoryName(Path.GetFullPath(path));
    }

    internal static void Check(bool condition, string what)
    {
        if (condition) { Passed++; return; }
        Failed++;
        Console.WriteLine("FAIL " + what);
    }

    internal static string ComputeSha256(string absolutePath)
    {
        using var stream = File.OpenRead(absolutePath);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(stream);
        var text = new System.Text.StringBuilder(hash.Length * 2);
        foreach (byte value in hash) text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        return text.ToString();
    }


}

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.WriteLine("AdapterTests: FloatLayout / ModConfig-MelonPreferences / enemy prefix math / artifact metadata");
        int? seededSpeed = null;
        float? seededEnemyCount = null;
        float? seededEnemyTimeline = null;
        float? seededCooldown = null;
        float? seededStaffCooldown = null;
        float? seededMap = null;
        bool oldCfg = false;
        bool seedFastBuild = false;
        bool seedBoat = false;
        bool seedForest = false;
        var artifactArgs = new List<string>();
        foreach (string arg in args)
        {
            if (TryFlag(arg, "--seed-speed=", out string speedText))
                seededSpeed = int.Parse(speedText, CultureInfo.InvariantCulture);
            else if (TryFlag(arg, "--seed-enemy-count=", out string countText))
                seededEnemyCount = ParseSeedFloat(countText);
            else if (TryFlag(arg, "--seed-enemy-timeline=", out string timelineText))
                seededEnemyTimeline = ParseSeedFloat(timelineText);
            else if (TryFlag(arg, "--seed-cooldown=", out string cooldownText))
                seededCooldown = ParseSeedFloat(cooldownText);
            else if (TryFlag(arg, "--seed-staff-cooldown=", out string staffCooldownText))
                seededStaffCooldown = ParseSeedFloat(staffCooldownText);
            else if (TryFlag(arg, "--seed-map=", out string mapText))
                seededMap = ParseSeedFloat(mapText);
            else if (arg == "--oldcfg")
                oldCfg = true;
            else if (arg == "--seed-fast-build")
                seedFastBuild = true;
            else if (arg == "--seed-boat")
                seedBoat = true;
            else if (arg == "--seed-forest")
                seedForest = true;
            else
                artifactArgs.Add(arg);
        }
        if (seededSpeed.HasValue)
        {
            MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "SpeedMultiplier", seededSpeed.Value);
            Console.WriteLine("mode: pre-seeded cfg speed multiplier " + seededSpeed.Value);
        }
        if (seededEnemyCount.HasValue)
        {
            MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "EnemyCountMultiplier", seededEnemyCount.Value);
            Console.WriteLine("mode: pre-seeded cfg EnemyCountMultiplier " + Describe(seededEnemyCount.Value));
        }
        if (seededEnemyTimeline.HasValue)
        {
            MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "EnemyTimelineSpeed", seededEnemyTimeline.Value);
            Console.WriteLine("mode: pre-seeded cfg EnemyTimelineSpeed " + Describe(seededEnemyTimeline.Value));
        }
        if (seededCooldown.HasValue)
        {
            MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "SteedCooldownMultiplier", seededCooldown.Value);
            Console.WriteLine("mode: pre-seeded cfg SteedCooldownMultiplier " + Describe(seededCooldown.Value));
        }
        if (seededStaffCooldown.HasValue)
        {
            MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "StaffCooldownMultiplier", seededStaffCooldown.Value);
            Console.WriteLine("mode: pre-seeded cfg StaffCooldownMultiplier " + Describe(seededStaffCooldown.Value));
        }
        if (seededMap.HasValue)
        {
            MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "MapSizeMultiplier", seededMap.Value);
            Console.WriteLine("mode: pre-seeded cfg MapSizeMultiplier " + Describe(seededMap.Value));
        }
        if (oldCfg)
        {
            if (!seededSpeed.HasValue) MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "SpeedMultiplier", 3);
            MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "InfiniteSteedStamina", true);
            MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "HoldPurchaseEnabled", true);
            MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "CalendarEnabled", true);
            Console.WriteLine("mode: pre-seeded cfg with only the four original keys");
        }
        if (seedFastBuild)
        {
            MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "FastBuild", true);
            Console.WriteLine("mode: pre-seeded cfg FastBuild true");
        }
        if (seedBoat)
        {
            MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "BoatCapacityEnabled", true);
            Console.WriteLine("mode: pre-seeded cfg BoatCapacityEnabled true");
        }
        if (seedForest)
        {
            MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "FastForestRecedeEnabled", true);
            Console.WriteLine("mode: pre-seeded cfg FastForestRecedeEnabled true");
        }
        LayoutChecks();
        GestureChecks.Run();
        ConfigChecks(seededSpeed, seededEnemyCount, seededEnemyTimeline, oldCfg, seedFastBuild, seededCooldown, seedBoat, seededMap, seededStaffCooldown, seedForest);
        EnemyMathChecks.Run();
        ProbeChecks.Run();
        MenuChecks.Run();
        if (artifactArgs.Count < 1)
            Checks.Check(false, "artifact path argument missing (pass the built OhMyMods.AndroidProbe.dll path)");
        else if (!File.Exists(artifactArgs[0]))
            Checks.Check(false, "artifact not found: " + artifactArgs[0]);
        else
            ArtifactChecks.Run(Path.GetFullPath(artifactArgs[0]));
        Console.WriteLine("adapter tests: " + Checks.Passed + " passed / " + Checks.Failed + " failed");
        return Checks.Failed == 0 ? 0 : 1;
    }

    private static bool TryFlag(string arg, string flag, out string value)
    {
        if (arg.StartsWith(flag, StringComparison.Ordinal)) { value = arg.Substring(flag.Length); return true; }
        value = null;
        return false;
    }

    private static float ParseSeedFloat(string text)
    {
        if (string.Equals(text, "NaN", StringComparison.OrdinalIgnoreCase)) return float.NaN;
        if (string.Equals(text, "Infinity", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "Inf", StringComparison.OrdinalIgnoreCase))
            return float.PositiveInfinity;
        if (string.Equals(text, "-Infinity", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "-Inf", StringComparison.OrdinalIgnoreCase))
            return float.NegativeInfinity;
        return float.Parse(text, CultureInfo.InvariantCulture);
    }

    private static string Describe(float value)
        => float.IsNaN(value) ? "NaN" : float.IsPositiveInfinity(value) ? "Infinity" : float.IsNegativeInfinity(value) ? "-Infinity" : value.ToString(CultureInfo.InvariantCulture);

    private static void LayoutChecks()
    {
        var layout = new FloatLayout();
        layout.Resize(1280f, 720f);
        Checks.Check(Math.Abs(layout.Scale - 1f) < 1e-4f, "scale is 1.0 at 1280x720");
        Checks.Check(Math.Abs(layout.Diameter - 48f) < 1e-4f, "orb visual diameter stays 48");
        Checks.Check(Math.Abs(layout.TouchSize - 72f) < 1e-4f, "orb square touch target stays 72");
        Checks.Check(!layout.Expanded, "panel starts collapsed");
        layout.Expanded = true;
        Checks.Check(Math.Abs(layout.PanelWidth - 320f) < 1e-4f, "panel outer width is the fixed 320*scale");
        Checks.Check(Math.Abs(layout.PanelHeight - 480f) < 1e-4f, "panel outer height is the fixed 480*scale");
        Checks.Check(layout.PanelWidth * layout.PanelHeight <= 179200f, "fixed panel area stays within the old 280x640 world-page cap");
        // 旧固定页高表已删除：页面 bool 只路由内容，不再改变外框。
        layout.PlayerPage = true; layout.WorldPage = true; layout.GenerationPage = true;
        layout.PopulationPage = true; layout.VegetationPage = true;
        Checks.Check(Math.Abs(layout.PanelWidth - 320f) < 1e-4f && Math.Abs(layout.PanelHeight - 480f) < 1e-4f,
            "page flags never resize the fixed outer panel");
        Checks.Check(layout.HitPanel(layout.PanelX + 1f, layout.PanelY + layout.PanelHeight - 1f),
            "touch filter covers the fixed panel bottom edge");
        Checks.Check(!layout.HitPanel(layout.PanelX + 1f, layout.PanelY + layout.PanelHeight + 1f),
            "touch filter ends at the fixed panel rect");
        Checks.Check(layout.ContentY > layout.PanelY && layout.ContentY + layout.ContentHeight <= layout.PanelY + layout.PanelHeight + 1e-3f,
            "content viewport stays inside the fixed panel");
        float content = 900f;
        Checks.Check(layout.ScrollMax(10f) == 0f, "short content needs no scroll");
        Checks.Check(Math.Abs(layout.ScrollMax(content) - (content - layout.ContentHeight)) < 1e-3f,
            "scroll max is the measured content height minus the viewport");
        Checks.Check(content - layout.ScrollMax(content) <= layout.ContentHeight + 1e-3f,
            "the last card bottom is reachable at max scroll");
        layout.PlayerPage = false; layout.WorldPage = false; layout.GenerationPage = false;
        layout.PopulationPage = false; layout.VegetationPage = false;
        // 安全区（top-left 绝对边界，与 RootFloatInput 的 nativeScreen.safeArea 转换一致）：
        // 面板被夹进安全区，浮球拖动也夹进安全区。
        layout.Resize(1280f, 720f, 60f, 20f, 1200f, 680f);
        Checks.Check(layout.PanelX >= 60f - 1e-3f && layout.PanelX + layout.PanelWidth <= 1200f + 1e-3f,
            "panel is clipped into the safe-area x bounds");
        Checks.Check(layout.PanelY >= 20f - 1e-3f && layout.PanelY + layout.PanelHeight <= 680f + 1e-3f,
            "panel is clipped into the safe-area y bounds");
        Checks.Check(layout.Begin(layout.X, layout.Y), "ball press starts inside the ball");
        layout.Move(-1000f, -1000f);
        layout.End(-1000f, -1000f);
        Checks.Check(layout.X >= 60f + layout.TouchSize / 2f - 1e-3f, "ball snap keeps the left safe inset");
        Checks.Check(layout.Y >= 20f + layout.TouchSize / 2f - 1e-3f, "ball snap keeps the top safe inset");
        // 球优先：球与面板重叠时球仍独占重叠点（PanelX 随拖动后的 X 重新计算，探测点在真实重叠区）。
        layout.Resize(1280f, 720f);
        layout.Expanded = true;
        Checks.Check(layout.Begin(layout.X, layout.Y), "ball press for the overlap probe");
        layout.Move(layout.X + 100f, layout.Y);
        float overlapX = layout.X + layout.Diameter / 2f + 5f * layout.Scale + 1f;
        Checks.Check(layout.HitBall(overlapX, layout.Y), "a ball beside its panel still owns the overlap point");
        Checks.Check(layout.HitPanel(overlapX, layout.Y), "the touch filter still covers the panel under the orb");
        Checks.Check(!layout.HitPanelBody(overlapX, layout.Y), "the panel gesture refuses orb-owned points (orb priority)");
        layout.Cancel();
        // 极小屏/小安全区：外框与视口都留在边界内，内容几何不为负（Draw 与 Hit 同界）。
        var tiny = new FloatLayout();
        tiny.Resize(100f, 40f);
        Checks.Check(tiny.PanelX >= -1e-3f && tiny.PanelX + tiny.PanelWidth <= 100f + 1e-3f
            && tiny.PanelY >= -1e-3f && tiny.PanelY + tiny.PanelHeight <= 40f + 1e-3f,
            "a tiny screen keeps the outer panel inside the screen bounds");
        Checks.Check(tiny.ContentWidth >= 0f && tiny.ContentHeight >= 0f, "degenerate screens yield non-negative content metrics");
        Checks.Check(tiny.ContentHeight == 0f, "a screen too short for the chrome reports no content viewport");
        tiny.Resize(100f, 40f, 2f, 2f, 98f, 38f);
        Checks.Check(tiny.PanelX >= 2f - 1e-3f && tiny.PanelX + tiny.PanelWidth <= 98f + 1e-3f
            && tiny.PanelY >= 2f - 1e-3f && tiny.PanelY + tiny.PanelHeight <= 38f + 1e-3f,
            "small safe-area bounds keep the outer panel inside the safe rect");
        Checks.Check(tiny.ContentX >= tiny.PanelX - 1e-3f && tiny.ContentX <= tiny.PanelX + tiny.PanelWidth + 1e-3f
            && tiny.ContentY >= tiny.PanelY - 1e-3f && tiny.ContentY <= tiny.PanelY + tiny.PanelHeight + 1e-3f
            && tiny.ContentX + tiny.ContentWidth <= tiny.PanelX + tiny.PanelWidth + 1e-3f
            && tiny.ContentY + tiny.ContentHeight <= tiny.PanelY + tiny.PanelHeight + 1e-3f,
            "degenerate screens keep the content viewport a subset of the outer panel");
        var cramped = new FloatLayout();
        cramped.Resize(100f, 40f);
        Checks.Check(cramped.ContentX >= cramped.PanelX - 1e-3f && cramped.ContentX <= cramped.PanelX + cramped.PanelWidth + 1e-3f
            && cramped.ContentY <= cramped.PanelY + cramped.PanelHeight + 1e-3f,
            "a tiny screen keeps the viewport origin inside the outer panel");
    }

    private static void ConfigChecks(int? seededSpeed, float? seededEnemyCount, float? seededEnemyTimeline, bool oldCfg, bool seededFastBuild, float? seededCooldown, bool seededBoat, float? seededMap, float? seededStaffCooldown, bool seedForest)
    {
        var applied = new List<bool>();
        int expectedWarnings = (seededEnemyCount.HasValue && !IsFinite(seededEnemyCount.Value) ? 1 : 0)
            + (seededEnemyTimeline.HasValue && !IsFinite(seededEnemyTimeline.Value) ? 1 : 0)
            + (seededCooldown.HasValue && !IsFinite(seededCooldown.Value) ? 1 : 0)
            + (seededStaffCooldown.HasValue && !IsFinite(seededStaffCooldown.Value) ? 1 : 0)
            + (seededMap.HasValue && !IsFinite(seededMap.Value) ? 1 : 0);
        bool rejected = false;
        try { Config.Initialize(null); }
        catch (ArgumentNullException) { rejected = true; }
        Checks.Check(rejected, "Initialize(null) fails with ArgumentNullException");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Count == 0,
            "the rejected null seam neither creates entries nor poisons Initialize");

        Config.Initialize(enabled => applied.Add(enabled));

        Checks.Check(Config.Enabled.Value, "session master switch defaults ON and is not persisted");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Count == 20, "Initialize creates exactly twenty entries (no persisted master switch)");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/SpeedMultiplier default=1"), "SpeedMultiplier is declared with default 1 in the OhMyMods.Android category");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/InfiniteSteedStamina default=False"), "InfiniteSteedStamina is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/HoldPurchaseEnabled default=False"), "HoldPurchaseEnabled is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/CalendarEnabled default=False"), "CalendarEnabled is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/EnemyCountMultiplier default=1"), "EnemyCountMultiplier is declared with default 1");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/EnemyTimelineSpeed default=1"), "EnemyTimelineSpeed is declared with default 1");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/InfiniteMoney default=False"), "InfiniteMoney is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/FarmCatsEnabled default=False"), "FarmCatsEnabled is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/FastBuild default=False"), "FastBuild is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/SteedCooldownMultiplier default=1"), "SteedCooldownMultiplier is declared with default 1");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/StaffCooldownMultiplier default=1"), "StaffCooldownMultiplier is declared with default 1");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/BoatCapacityEnabled default=False"), "BoatCapacityEnabled is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/MapSizeMultiplier default=1"), "MapSizeMultiplier is declared with default 1");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/FastForestRecedeEnabled default=False"), "FastForestRecedeEnabled is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/DeerPopulationEnabled default=False"), "DeerPopulationEnabled is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/DenseThicketsEnabled default=False"), "DenseThicketsEnabled is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/NightDepartureEnabled default=False"), "NightDepartureEnabled is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/PopulationEnabled default=False"), "PopulationEnabled is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/BeggarCampCapacity default=4"), "BeggarCampCapacity is declared with the PC default 4");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/BeggarSpawnIntervalSeconds default=120"), "BeggarSpawnIntervalSeconds is declared with the PC default 120");
        Checks.Check(!Config.NightDepartureEnabled.Value, "NightDepartureEnabled missing key defaults OFF without save");
        Checks.Check(!Config.PopulationEnabled.Value && Config.BeggarCampCapacity.Value == 4
            && Config.BeggarSpawnIntervalSeconds.Value == 120,
            "missing population keys default to the PC gate OFF with capacity 4 and interval 120 without save");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == 0, "Initialize never writes the cfg");
        Checks.Check(applied.Count == 1 && !applied[0], "cold start applies InfiniteMoney=false through the seam exactly once");
        Checks.Check(MelonLoader.MelonLogger.WarningCalls == expectedWarnings, "the load boundary warns exactly once per non-finite seeded multiplier");

        float expectedEnemyCount = seededEnemyCount.HasValue ? ExpectedMultiplier(seededEnemyCount.Value) : 1f;
        float expectedEnemyTimeline = seededEnemyTimeline.HasValue ? ExpectedMultiplier(seededEnemyTimeline.Value) : 1f;
        float expectedCooldown = seededCooldown.HasValue ? ExpectedCooldown(seededCooldown.Value) : 1f;
        float expectedStaffCooldown = seededStaffCooldown.HasValue ? ExpectedCooldown(seededStaffCooldown.Value) : 1f;
        float expectedMap = seededMap.HasValue ? ExpectedMultiplier(seededMap.Value) : 1f;
        string expectedFastBuild = seededFastBuild ? "True" : "False";
        string expectedBoat = seededBoat ? "True" : "False";
        string expectedForest = seedForest ? "True" : "False";

        bool seeded = seededSpeed.HasValue || seededEnemyCount.HasValue || seededEnemyTimeline.HasValue || oldCfg || seededFastBuild || seededCooldown.HasValue || seededBoat || seededMap.HasValue || seededStaffCooldown.HasValue || seedForest;
        if (seeded)
        {
            int expectedSpeed = seededSpeed.HasValue ? Math.Clamp(seededSpeed.Value, 1, 5) : (oldCfg ? 3 : 1);
            Checks.Check(Config.SpeedMultiplier.Value == expectedSpeed, "load boundary yields speed " + expectedSpeed + "x");
            Checks.Check(Config.EnemyCountMultiplier.Value == expectedEnemyCount,
                "EnemyCountMultiplier is " + Describe(expectedEnemyCount) + "x after the load boundary");
            Checks.Check(Config.EnemyTimelineSpeed.Value == expectedEnemyTimeline,
                "EnemyTimelineSpeed is " + Describe(expectedEnemyTimeline) + "x after the load boundary");
            Checks.Check(Config.InfiniteSteedStamina.Value == oldCfg && Config.HoldPurchaseEnabled.Value == oldCfg && Config.CalendarEnabled.Value == oldCfg,
                "the four original entries keep absorbing a pre-existing cfg");
            Checks.Check(!Config.InfiniteMoney.Value, "a cfg without the new keys keeps InfiniteMoney OFF");
            Checks.Check(!Config.FarmCatsEnabled.Value, "a cfg without the new keys keeps FarmCats OFF");
            Checks.Check(Config.FastBuild.Value == seededFastBuild,
                seededFastBuild ? "a seeded FastBuild key loads ON at the load boundary" : "a cfg without the FastBuild key keeps FastBuild OFF");
            Checks.Check(Config.SteedCooldownMultiplier.Value == expectedCooldown,
                "SteedCooldownMultiplier is " + Describe(expectedCooldown) + "x after the load boundary (0.2..1 clamp, non-finite fallback 1)");
            Checks.Check(Config.StaffCooldownMultiplier.Value == expectedStaffCooldown,
                "StaffCooldownMultiplier is " + Describe(expectedStaffCooldown) + "x after the load boundary (0.2..1 clamp, non-finite fallback 1)");
            Checks.Check(Config.BoatCapacityEnabled.Value == seededBoat,
                seededBoat ? "a seeded BoatCapacityEnabled key loads ON at the load boundary" : "a cfg without the BoatCapacityEnabled key keeps it OFF");
            Checks.Check(Config.FastForestRecedeEnabled.Value == seedForest,
                seedForest ? "a seeded FastForestRecede key loads ON at the load boundary" : "a cfg without the FastForestRecede key keeps it OFF");
            Checks.Check(!Config.DeerPopulationEnabled.Value,
                "a cfg without the DeerPopulation key keeps it OFF at the load boundary (no apply callback, load does not save)");
            Checks.Check(!Config.DenseThicketsEnabled.Value,
                "a cfg without the DenseThickets key keeps it OFF at the load boundary (no apply callback, load does not save)");
            Checks.Check(Config.MapSizeMultiplier.Value == expectedMap,
                "MapSizeMultiplier is " + Describe(expectedMap) + "x after the load boundary (1..5 clamp, non-finite fallback 1)");
            Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == 0, "the load-boundary clamp/fallback does not write the cfg");
            Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_SETTINGS_READY category=OhMyMods.Android speed=" + expectedSpeed
                + " stamina=" + oldCfg + " hold=" + oldCfg + " calendar=" + oldCfg
                + " enemyCount=" + expectedEnemyCount + " growth=" + expectedEnemyTimeline + " money=False cats=False fastBuild=" + expectedFastBuild
                + " cooldown=" + expectedCooldown + " boat=" + expectedBoat + " map=" + expectedMap
                + " staff=" + expectedStaffCooldown + " forestRecede=" + expectedForest
                + " deerPopulation=False denseThickets=False nightDeparture=False"
                + " population=False capacity=4 interval=120",
                "cold-start ready line reports the effective values");
            if (seededEnemyCount == 4.5f && seededEnemyTimeline == 4.5f)
            {
                int fractionalSaves = 0;
                Config.CycleEnemyCount();
                Checks.Check(Config.EnemyCountMultiplier.Value == 5f, "cycling 4.5 steps to 5x instead of 5.5x");
                Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++fractionalSaves, "4.5->5 saves exactly once");
                Config.CycleEnemyCount();
                Checks.Check(Config.EnemyCountMultiplier.Value == 1f, "cycling 5 wraps to 1x");
                Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++fractionalSaves, "5->1 saves exactly once");
                Config.CycleEnemyTimeline();
                Checks.Check(Config.EnemyTimelineSpeed.Value == 5f, "cycling a 4.5 timeline steps to 5x instead of 5.5x");
                Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++fractionalSaves, "4.5->5 timeline saves exactly once");
                Config.CycleEnemyTimeline();
                Checks.Check(Config.EnemyTimelineSpeed.Value == 1f, "cycling the timeline 5 wraps to 1x");
                Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++fractionalSaves, "timeline 5->1 saves exactly once");
            }
            if (seededCooldown.HasValue && seededCooldown.Value == 0.5f)
            {
                int cooldownSaves = MelonLoader.MelonPreferencesStub.SaveCalls;
                Config.CycleSteedCooldown();
                Checks.Check(Config.SteedCooldownMultiplier.Value == 0.4f, "cycling a loaded 0.5 cooldown multiplier selects the next lower 20% step (0.4x)");
                Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == cooldownSaves + 1, "0.5->0.4 saves exactly once");
            }
            if (seededStaffCooldown.HasValue && seededStaffCooldown.Value == 0.5f)
            {
                int staffCooldownSaves = MelonLoader.MelonPreferencesStub.SaveCalls;
                Config.CycleStaffCooldown();
                Checks.Check(Config.StaffCooldownMultiplier.Value == 0.4f, "cycling a loaded 0.5 staff multiplier selects the next lower 20% step (0.4x)");
                Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == staffCooldownSaves + 1, "staff 0.5->0.4 saves exactly once");
            }
            if (seededBoat)
            {
                int boatSaves = MelonLoader.MelonPreferencesStub.SaveCalls;
                Config.ToggleBoatCapacity();
                Checks.Check(!Config.BoatCapacityEnabled.Value, "toggling the loaded ON boat-capacity switch turns it OFF");
                Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_WORLD_BOAT_CAPACITY enabled=False", "the boat-capacity toggle logs the switch state");
                Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == boatSaves + 1, "one boat-capacity toggle saves exactly once");
            }
            if (seededMap == 4.5f)
            {
                int mapSaves = MelonLoader.MelonPreferencesStub.SaveCalls;
                Config.CycleMapSize();
                Checks.Check(Config.MapSizeMultiplier.Value == 5f, "cycling a 4.5 map length steps to 5x instead of 5.5x");
                Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == mapSaves + 1, "map length 4.5->5 saves exactly once");
                Config.CycleMapSize();
                Checks.Check(Config.MapSizeMultiplier.Value == 1f, "cycling the map length 5 wraps to 1x");
                Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == mapSaves + 2, "map length 5->1 saves exactly once");
            }
            if (seedForest)
            {
                int forestSaves = MelonLoader.MelonPreferencesStub.SaveCalls;
                Config.ToggleFastForestRecede();
                Checks.Check(!Config.FastForestRecedeEnabled.Value, "toggling the loaded ON fast-forest switch turns it OFF");
                Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_WORLD_FAST_FOREST_RECEDE enabled=False", "the fast-forest toggle logs the switch state");
                Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == forestSaves + 1, "one fast-forest toggle saves exactly once");
            }
            return;
        }

        Checks.Check(Config.SpeedMultiplier.Value == 1, "speed multiplier defaults to 1x");
        Checks.Check(Config.EnemyCountMultiplier.Value == 1f && Config.EnemyTimelineSpeed.Value == 1f, "enemy multipliers default to 1x");
        Checks.Check(Config.SteedCooldownMultiplier.Value == 1f, "steed cooldown multiplier defaults to 1x");
        Checks.Check(Config.StaffCooldownMultiplier.Value == 1f, "staff base cooldown multiplier defaults to 1x");
        Checks.Check(Config.MapSizeMultiplier.Value == 1f, "map length multiplier defaults to 1x (native layout)");
        Checks.Check(!Config.InfiniteSteedStamina.Value
            && !Config.HoldPurchaseEnabled.Value
            && !Config.CalendarEnabled.Value
            && !Config.InfiniteMoney.Value
            && !Config.FarmCatsEnabled.Value
            && !Config.FastBuild.Value
            && !Config.BoatCapacityEnabled.Value
            && !Config.FastForestRecedeEnabled.Value
            && !Config.DeerPopulationEnabled.Value
            && !Config.DenseThicketsEnabled.Value && !Config.NightDepartureEnabled.Value, "qol switches, InfiniteMoney, FarmCats, FastBuild, BoatCapacity, FastForestRecede, DeerPopulation and DenseThickets default OFF");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_SETTINGS_READY category=OhMyMods.Android speed=1 stamina=False hold=False calendar=False enemyCount=1 growth=1 money=False cats=False fastBuild=False cooldown=1 boat=False map=1 staff=1 forestRecede=False deerPopulation=False denseThickets=False nightDeparture=False"
            + " population=False capacity=4 interval=120",
            "cold-start ready line reports the effective values");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == 0, "reading defaults does not write the cfg");

        int saves = 0;
        Config.ToggleHold();
        Checks.Check(Config.HoldPurchaseEnabled.Value, "ToggleHold turns it ON");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_PLAYER_HOLD_PURCHASE enabled=True", "ToggleHold logs the switch state");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "ToggleHold saves exactly once");
        Config.ToggleHold();
        Checks.Check(Config.HoldPurchaseEnabled.Value == false, "ToggleHold turns it OFF again");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "every toggle saves exactly once");

        Config.ToggleStamina();
        Checks.Check(Config.InfiniteSteedStamina.Value, "ToggleStamina turns it ON");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "ToggleStamina saves exactly once");

        for (int next = 2; next <= 5; next++)
        {
            Config.CycleSpeed();
            Checks.Check(Config.SpeedMultiplier.Value == next, "CycleSpeed advances to " + next + "x");
            Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "CycleSpeed to " + next + "x saves exactly once");
        }
        Config.CycleSpeed();
        Checks.Check(Config.SpeedMultiplier.Value == 1, "CycleSpeed wraps back to 1x");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "CycleSpeed wrap saves exactly once");

        Config.ToggleMoney();
        Checks.Check(Config.InfiniteMoney.Value, "ToggleMoney turns it ON");
        Checks.Check(applied.Count == 2 && applied[1], "ToggleMoney applies InfiniteMoney=true exactly once");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_WORLD_MONEY enabled=True", "ToggleMoney logs the switch state");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "ToggleMoney ON saves exactly once");
        Config.ToggleMoney();
        Checks.Check(Config.InfiniteMoney.Value == false, "ToggleMoney turns it OFF again");
        Checks.Check(applied.Count == 3 && applied[2] == false, "ToggleMoney OFF applies false exactly once");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "ToggleMoney OFF saves exactly once");

        Config.ToggleFarmCats();
        Checks.Check(Config.FarmCatsEnabled.Value, "ToggleFarmCats turns it ON");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_FARM_CATS enabled=True", "ToggleFarmCats logs the switch state");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "ToggleFarmCats ON saves exactly once");
        Config.ToggleFarmCats();
        Checks.Check(Config.FarmCatsEnabled.Value == false, "ToggleFarmCats turns it OFF again");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_FARM_CATS enabled=False", "ToggleFarmCats logs the OFF state");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "ToggleFarmCats OFF saves exactly once");

        Config.ToggleFastBuild();
        Checks.Check(Config.FastBuild.Value, "ToggleFastBuild turns it ON");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_WORLD_FAST_BUILD enabled=True", "ToggleFastBuild logs the switch state");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "ToggleFastBuild ON saves exactly once");
        Config.ToggleFastBuild();
        Checks.Check(Config.FastBuild.Value == false, "ToggleFastBuild turns it OFF again");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_WORLD_FAST_BUILD enabled=False", "ToggleFastBuild logs the OFF state");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "ToggleFastBuild OFF saves exactly once");

        Config.ToggleBoatCapacity();
        Checks.Check(Config.BoatCapacityEnabled.Value, "ToggleBoatCapacity turns it ON");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_WORLD_BOAT_CAPACITY enabled=True", "ToggleBoatCapacity logs the switch state");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "ToggleBoatCapacity ON saves exactly once");
        Config.ToggleBoatCapacity();
        Checks.Check(Config.BoatCapacityEnabled.Value == false, "ToggleBoatCapacity turns it OFF again");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_WORLD_BOAT_CAPACITY enabled=False", "ToggleBoatCapacity logs the OFF state");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "ToggleBoatCapacity OFF saves exactly once");

        Config.ToggleFastForestRecede();
        Checks.Check(Config.FastForestRecedeEnabled.Value, "ToggleFastForestRecede turns it ON");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_WORLD_FAST_FOREST_RECEDE enabled=True", "ToggleFastForestRecede logs the switch state");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "ToggleFastForestRecede ON saves exactly once");
        Config.ToggleFastForestRecede();
        Checks.Check(Config.FastForestRecedeEnabled.Value == false, "ToggleFastForestRecede turns it OFF again");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_WORLD_FAST_FOREST_RECEDE enabled=False", "ToggleFastForestRecede logs the OFF state");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "ToggleFastForestRecede OFF saves exactly once");

        Config.ToggleDeerPopulation();
        Checks.Check(Config.DeerPopulationEnabled.Value, "ToggleDeerPopulation turns it ON");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_WORLD_DEER_POPULATION enabled=True", "ToggleDeerPopulation logs the switch state");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "ToggleDeerPopulation ON saves exactly once");
        Config.ToggleDeerPopulation();
        Checks.Check(Config.DeerPopulationEnabled.Value == false, "ToggleDeerPopulation turns it OFF again");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_WORLD_DEER_POPULATION enabled=False", "ToggleDeerPopulation logs the OFF state");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "ToggleDeerPopulation OFF saves exactly once");

        for (int next = 2; next <= 5; next++)
        {
            Config.CycleEnemyCount();
            Checks.Check(Config.EnemyCountMultiplier.Value == next, "CycleEnemyCount advances to " + next + "x");
            Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "CycleEnemyCount to " + next + "x saves exactly once");
        }
        Config.CycleEnemyCount();
        Checks.Check(Config.EnemyCountMultiplier.Value == 1, "CycleEnemyCount wraps back to 1x");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "CycleEnemyCount wrap saves exactly once");

        for (int next = 2; next <= 5; next++)
        {
            Config.CycleEnemyTimeline();
            Checks.Check(Config.EnemyTimelineSpeed.Value == next, "CycleEnemyTimeline advances to " + next + "x");
            Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "CycleEnemyTimeline to " + next + "x saves exactly once");
        }
        Config.CycleEnemyTimeline();
        Checks.Check(Config.EnemyTimelineSpeed.Value == 1, "CycleEnemyTimeline wraps back to 1x");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "CycleEnemyTimeline wrap saves exactly once");

        foreach (float next in new[] { 0.8f, 0.6f, 0.4f, 0.2f, 1f })
        {
            Config.CycleSteedCooldown();
            Checks.Check(Config.SteedCooldownMultiplier.Value == next, "CycleSteedCooldown advances to " + next + "x");
            Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_PLAYER_STEED_COOLDOWN multiplier=" + next,
                "CycleSteedCooldown to " + next + "x logs the multiplier");
            Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "CycleSteedCooldown to " + next + "x saves exactly once");
        }

        foreach (float next in new[] { 0.8f, 0.6f, 0.4f, 0.2f, 1f })
        {
            Config.CycleStaffCooldown();
            Checks.Check(Config.StaffCooldownMultiplier.Value == next, "CycleStaffCooldown advances to " + next + "x");
            Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_PLAYER_STAFF_COOLDOWN multiplier=" + next,
                "CycleStaffCooldown to " + next + "x logs the multiplier");
            Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "CycleStaffCooldown to " + next + "x saves exactly once");
        }

        for (int next = 2; next <= 5; next++)
        {
            Config.CycleMapSize();
            Checks.Check(Config.MapSizeMultiplier.Value == next, "CycleMapSize advances to " + next + "x");
            Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "CycleMapSize to " + next + "x saves exactly once");
        }
        Config.CycleMapSize();
        Checks.Check(Config.MapSizeMultiplier.Value == 1f, "CycleMapSize wraps back to 1x");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "CycleMapSize wrap saves exactly once");

        Config.TogglePopulation();
        Checks.Check(Config.PopulationEnabled.Value, "TogglePopulation turns the batch gate ON");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_POPULATION_ENABLED enabled=True", "TogglePopulation logs the switch state");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "TogglePopulation ON saves exactly once");
        Config.TogglePopulation();
        Checks.Check(!Config.PopulationEnabled.Value, "TogglePopulation turns the batch gate OFF again");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_POPULATION_ENABLED enabled=False", "TogglePopulation logs the OFF state");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "TogglePopulation OFF saves exactly once");

        for (int next = 5; next <= 20; next++)
        {
            Config.CycleBeggarCampCapacity();
            Checks.Check(Config.BeggarCampCapacity.Value == next, "CycleBeggarCampCapacity advances to " + next);
            Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "camp capacity " + next + " saves exactly once");
        }
        Config.CycleBeggarCampCapacity();
        Checks.Check(Config.BeggarCampCapacity.Value == 1, "camp capacity wraps 20 -> 1");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "camp capacity wrap 20->1 saves exactly once");

        foreach (int next in new[] { 60, 30, 10, 5, 1, 120 })
        {
            Config.CycleBeggarSpawnInterval();
            Checks.Check(Config.BeggarSpawnIntervalSeconds.Value == next, "CycleBeggarSpawnInterval steps to " + next + "s");
            Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == ++saves, "spawn interval " + next + "s saves exactly once");
        }

        Checks.Check(applied.Count == 3, "the InfiniteMoney seam fires only at Initialize and the two toggles (no subscription or per-frame write)");
        Checks.Check(saves == 70, "seventy switch actions produced seventy saves");
    }

    private static float ExpectedMultiplier(float seeded)
        => IsFinite(seeded) ? Math.Clamp(seeded, 1f, 5f) : 1f;

    private static float ExpectedCooldown(float seeded)
        => IsFinite(seeded) ? Math.Clamp(seeded, 0.2f, 1f) : 1f;

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

// 公共面板手势（纯逻辑）：点按恰好一个动作、任意方向位移 sticky 取消点击、cancel/release-outside
// 零动作、滚轮与拖拽共用同一 ScrollY、范围夹取、固定 chrome 按压取消点击但不滚动。
internal static class GestureChecks
{
    internal static void Run()
    {
        var g = new PanelGesture();
        int fired = 0;
        g.Begin(100f, 100f, 7, true, 10f);
        if (g.End(100f, 100f, 7, 1000f) == 7) fired++;
        Checks.Check(fired == 1, "a tap on the same tap rect fires exactly one action");
        Checks.Check(!g.Captured && !g.Moved, "the gesture is fully released after a tap");

        g.Begin(100f, 100f, 7, true, 10f);
        g.Move(100f, 70f, 1000f);
        Checks.Check(g.Moved && g.Scrolled, "a vertical drag past 10*scale becomes a scroll gesture");
        Checks.Check(Math.Abs(g.ScrollY - 30f) < 1e-3f, "the scroll follows the drag delta");
        g.Move(100f, 100f, 1000f);
        Checks.Check(g.End(100f, 100f, 7, 1000f) == -1, "dragging back to the origin still fires zero actions");
        Checks.Check(Math.Abs(g.ScrollY) < 1e-3f, "returning to the origin restores the clamped scroll position");

        g.Begin(100f, 100f, 7, true, 10f);
        g.Move(130f, 100f, 1000f);
        Checks.Check(g.Moved, "horizontal displacement past the threshold also cancels the tap");
        Checks.Check(Math.Abs(g.ScrollY) < 1e-3f, "horizontal movement never scrolls the content");
        Checks.Check(g.End(130f, 100f, 7, 1000f) == -1, "a horizontal drag fires nothing");

        g.Begin(100f, 100f, 7, true, 10f);
        Checks.Check(g.End(130f, 100f, 7, 1000f) == -1,
            "a release displaced past the threshold without any drag event fires nothing");
        g.Begin(100f, 100f, 7, true, 10f);
        Checks.Check(g.End(105f, 100f, 7, 1000f) == 7, "a release under the threshold stays a tap");

        g.Begin(100f, 100f, 7, true, 10f);
        Checks.Check(g.End(300f, 300f, -1, 1000f) == -1, "release outside every tap rect fires nothing");

        g.Begin(100f, 100f, 7, true, 10f);
        g.Cancel();
        Checks.Check(g.End(100f, 100f, 7, 1000f) == -1, "a cancelled press fires nothing");

        var w = new PanelGesture();
        w.Wheel(40f, 1000f);
        w.Begin(10f, 10f, -1, true, 10f);
        w.Move(10f, -10f, 1000f);
        Checks.Check(Math.Abs(w.ScrollY - 60f) < 1e-3f, "wheel and drag share one monotonic ScrollY");
        w.Move(10f, 0f, 1000f);
        w.Move(10f, -190f, 40f);
        Checks.Check(Math.Abs(w.ScrollY - 40f) < 1e-3f, "the shared scroll clamps at the measured content max");
        w.Wheel(-1000f, 40f);
        Checks.Check(Math.Abs(w.ScrollY) < 1e-3f, "the shared scroll clamps at the top");
        Checks.Check(w.End(10f, -190f, -1, 40f) == -1, "a scrolled content press fires nothing");

        g.Begin(100f, 100f, 9, false, 10f);
        g.Move(100f, 300f, 1000f);
        Checks.Check(g.Moved && !g.Scrolled && Math.Abs(g.ScrollY) < 1e-3f,
            "fixed chrome presses move-cancel but never scroll");
        Checks.Check(g.End(100f, 300f, 9, 1000f) == -1, "a moved chrome press fires nothing");
        g.Begin(100f, 100f, 9, false, 10f);
        Checks.Check(g.End(100f, 100f, 9, 1000f) == 9, "an unmoved chrome press releases as one action");

        // 帧末收束：内容收缩（无输入）也把唯一 ScrollY 收进新范围。
        var c = new PanelGesture();
        c.Wheel(40f, 1000f);
        c.ClampToMax(10f);
        Checks.Check(Math.Abs(c.ScrollY - 10f) < 1e-3f, "ClampToMax shrinks the shared ScrollY to the new content max");
        c.ClampToMax(0f);
        Checks.Check(Math.Abs(c.ScrollY) < 1e-3f, "ClampToMax(0) collapses the shared ScrollY with no input");
        c.Wheel(25f, 40f);
        c.ClampToMax(40f);
        Checks.Check(Math.Abs(c.ScrollY - 25f) < 1e-3f, "ClampToMax keeps a scroll already inside the range");

        // 按住卡片时滚轮：有效改变 ScrollY 必须先失效旧 pressedAction（sticky Moved）。
        var w2 = new PanelGesture();
        w2.Begin(50f, 50f, 6, true, 10f);
        w2.Wheel(30f, 1000f);
        Checks.Check(Math.Abs(w2.ScrollY - 30f) < 1e-3f, "an effective wheel updates the shared ScrollY while a press is held");
        Checks.Check(w2.Moved, "an effective wheel moves-cancels the held press");
        Checks.Check(w2.End(50f, 50f, 6, 1000f) == -1, "a held press cancelled by an effective wheel fires nothing on release");

        // 夹取后无有效改变：原点击保持。
        var w3 = new PanelGesture();
        w3.Begin(50f, 50f, 6, true, 10f);
        w3.Wheel(-30f, 1000f);
        Checks.Check(Math.Abs(w3.ScrollY) < 1e-3f && !w3.Moved, "a zero-effective wheel neither scrolls nor cancels");
        Checks.Check(w3.End(50f, 50f, 6, 1000f) == 6, "a zero-effective wheel keeps the held tap");

        // 无按住手势的滚轮：原滚动数学不变。
        var w4 = new PanelGesture();
        w4.Wheel(40f, 100f);
        Checks.Check(Math.Abs(w4.ScrollY - 40f) < 1e-3f && !w4.Moved, "an uncaptured wheel keeps the original scroll math");
    }
}

internal static class EnemyMathChecks
{
    internal static void Run()
    {
        // Session switch OFF: both prefixes must pass through untouched even with 2x configured.
        Config.EnemyCountMultiplier.Value = 2f;
        Config.EnemyTimelineSpeed.Value = 2f;
        Config.Enabled.Value = false;
        float disabledMultiplier = 1.5f;
        bool pass = EnemyPatch.AddEnemies_Prefix(ref disabledMultiplier);
        Checks.Check(pass && disabledMultiplier == 1.5f, "AddEnemies_Prefix passes the multiplier through while the session switch is OFF");
        int day = 3, multiplierDay = 4, daysOnIsland = 5;
        pass = EnemyPatch.GetEnemies_Prefix(ref day, ref multiplierDay, ref daysOnIsland);
        Checks.Check(pass && day == 3 && multiplierDay == 4 && daysOnIsland == 5, "GetEnemies_Prefix leaves the three day counts untouched while the session switch is OFF");
        Config.Enabled.Value = true;

        // 1x is the non-multiplier path.
        Config.EnemyCountMultiplier.Value = 1f;
        Config.EnemyTimelineSpeed.Value = 1f;
        float oneX = 1.5f;
        pass = EnemyPatch.AddEnemies_Prefix(ref oneX);
        Checks.Check(pass && oneX == 1.5f, "AddEnemies_Prefix leaves the multiplier untouched at 1x");
        day = 3; multiplierDay = 4; daysOnIsland = 5;
        pass = EnemyPatch.GetEnemies_Prefix(ref day, ref multiplierDay, ref daysOnIsland);
        Checks.Check(pass && day == 3 && multiplierDay == 4 && daysOnIsland == 5, "GetEnemies_Prefix leaves the day counts untouched at 1x");

        // 2x.
        Config.EnemyCountMultiplier.Value = 2f;
        float twoX = 1.5f;
        pass = EnemyPatch.AddEnemies_Prefix(ref twoX);
        Checks.Check(pass && twoX == 3f, "AddEnemies_Prefix doubles the multiplier at 2x and still runs the original");
        Config.EnemyTimelineSpeed.Value = 2f;
        day = 3; multiplierDay = 4; daysOnIsland = 5;
        pass = EnemyPatch.GetEnemies_Prefix(ref day, ref multiplierDay, ref daysOnIsland);
        Checks.Check(pass && day == 6 && multiplierDay == 8 && daysOnIsland == 10, "GetEnemies_Prefix scales all three day counts at 2x");

        // 5x is the load-boundary maximum.
        Config.EnemyCountMultiplier.Value = 5f;
        float fiveX = 1f;
        pass = EnemyPatch.AddEnemies_Prefix(ref fiveX);
        Checks.Check(pass && fiveX == 5f, "AddEnemies_Prefix scales by the 5x load-boundary maximum");
        Config.EnemyTimelineSpeed.Value = 5f;
        day = 1; multiplierDay = 2; daysOnIsland = 3;
        pass = EnemyPatch.GetEnemies_Prefix(ref day, ref multiplierDay, ref daysOnIsland);
        Checks.Check(pass && day == 5 && multiplierDay == 10 && daysOnIsland == 15, "GetEnemies_Prefix scales by the 5x load-boundary maximum");

        // Fractional 2.5x: Mathf.RoundToInt uses ties-to-even.
        Config.EnemyTimelineSpeed.Value = 2.5f;
        day = 1; multiplierDay = 2; daysOnIsland = 3;
        pass = EnemyPatch.GetEnemies_Prefix(ref day, ref multiplierDay, ref daysOnIsland);
        Checks.Check(pass && day == 2 && multiplierDay == 5 && daysOnIsland == 8, "GetEnemies_Prefix rounds 2.5 to 2 and 7.5 to 8 with ties-to-even");
        day = 5; multiplierDay = 5; daysOnIsland = 5;
        pass = EnemyPatch.GetEnemies_Prefix(ref day, ref multiplierDay, ref daysOnIsland);
        Checks.Check(pass && day == 12 && multiplierDay == 12 && daysOnIsland == 12, "12.5 rounds down to the even 12");

        Config.EnemyCountMultiplier.Value = 1f;
        Config.EnemyTimelineSpeed.Value = 1f;
    }
}

// Source-level registration contract for the Operator-owned Probe entry: the artifact metadata
// cannot show which native targets Probe patches, so the explicit staff registration is checked
// against the actual Probe.cs bytes (the 21 existing reported hook-count lines stay in place;
// the two new staff targets make 23 unique lines, with the nested type reported by its
// declared parent). This pins the Root entry, not worker source; the entry is frozen by hash in
// VerifyFrozenSources.
internal static class ProbeChecks
{
    internal static void Run()
    {
        string probePath = Path.Combine(Checks.RepositoryRoot(), "android", "Probe.cs");
        Checks.Check(File.Exists(probePath), "probe source present");
        if (!File.Exists(probePath)) return;
        string source = File.ReadAllText(probePath);
        Checks.Check(source.Contains("\"0.0.24\""), "Probe reports version 0.0.24");
        Checks.Check(source.Split("0.0.24").Length - 1 == 2, "Probe carries the two version markers (MelonInfo + load banner)");
        Checks.Check(source.Contains("camp_population") && source.Contains("ANDROID_CAMP_POPULATION_HOOKS_INSTALLED"), "Probe advertises and announces camp population");
        Checks.Check(source.Contains("campAwake,prefix:new HarmonyMethod(campPatch,\"Awake_Prefix\"),postfix:new HarmonyMethod(campPatch,\"Awake_Postfix\")"), "Camp Awake registers its whole prefix/postfix method set");
        Checks.Check(source.Contains("campDestroy,prefix:new HarmonyMethod(campPatch,\"OnDestroy_Prefix\")") && source.Contains("populationApply,postfix:new HarmonyMethod(typeof(KingdomEnhancedMod.PopulationPerformanceApplyPatch),\"Postfix\")"), "Camp destruction and campaign application register exact methods");
        Checks.Check(source.Contains("beggarEnable,prefix:new HarmonyMethod(beggarLifecycle,\"OnEnable_Prefix\")") && source.Contains("beggarDisable,prefix:new HarmonyMethod(beggarLifecycle,\"OnDisable_Prefix\")"), "Beggar enable/disable register exact life prefixes");
        Checks.Check(source.Contains("staff_base_cooldown"), "Probe feature marker advertises staff_base_cooldown");
        Checks.Check(source.Contains("fast_forest_recede"), "Probe feature marker advertises fast_forest_recede");
        Checks.Check(source.Contains("deer_population"), "Probe feature marker advertises deer_population");
        Checks.Check(source.Contains("dense_thickets"), "Probe feature marker advertises dense_thickets");
        Checks.Check(source.Contains("typeof(KingdomEnhancedMod.PatchDivine_StaffCooldown)"), "Probe wires the staff patch type");
        Checks.Check(source.Contains("typeof(Il2Cpp.HermesStaff._StartAbilityRoutine_d__17)"), "Probe resolves the nested HermesStaff state machine target");
        Checks.Check(source.Contains("typeof(Il2Cpp.ItemOfPower)"), "Probe resolves the ItemOfPower.CanCancel target");
        Checks.Check(source.Contains("\"RoutinePrefix\"") && source.Contains("\"CanCancelPrefix\"") && source.Contains("\"Finalizer\""),
            "Probe registers both staff prefixes and the shared Finalizer by the prepared handler names");
        Checks.Check(source.Contains("LogHookCounts(staffRoutine)") && source.Contains("LogHookCounts(staffCanCancel)"),
            "Probe reports hook counts for both staff targets");
        Checks.Check(source.Contains("owner.IsNested?owner.DeclaringType.Name+\".\"+owner.Name"),
            "the nested target is reported under its declared parent name");
        Checks.Check(source.Contains("ANDROID_STAFF_BASE_COOLDOWN_HOOKS_INSTALLED consumers=2"), "Probe announces the staff hook installation");
        Checks.Check(source.Contains("typeof(Il2Cpp.ForestItem)") && source.Contains("\"FadeAndRemove\"") && source.Contains("typeof(float)"),
            "Probe resolves the exact ForestItem.FadeAndRemove(float) target");
        Checks.Check(source.Contains("typeof(KingdomEnhancedMod.ForestItem_FadeAndRemove_OptionalVegetation_Patch)") && source.Contains("\"Prefix\""),
            "Probe registers the kept wrapper Prefix by name");
        Checks.Check(source.Contains("LogHookCounts(forestFadeAndRemove)"),
            "Probe reports the forest hook count (21 existing + 2 staff + 1 forest = 24 unique lines)");
        Checks.Check(source.Contains("ANDROID_FAST_FOREST_RECEDE_HOOK_INSTALLED sharedSource=true nativeEffects=true"),
            "Probe announces the fast-forest hook installation");
        Checks.Check(source.Contains("typeof(Il2Cpp.PopulationController),\"Update\"") && source.Contains("Type.EmptyTypes"),
            "Probe resolves the exact PopulationController.Update() target");
        Checks.Check(source.Contains("typeof(KingdomEnhancedMod.PopulationController_Update_DeerPopulation_Patch)")
            && source.Contains("deerPatchType,\"Prefix\"") && source.Contains("deerPatchType,\"Postfix\"") && source.Contains("deerPatchType,\"Finalizer\""),
            "Probe registers the shared deer prefix/postfix/finalizer by the prepared handler names");
        Checks.Check(source.Contains("LogHookCounts(deerUpdate)"),
            "Probe reports the deer hook count (deer makes 25 unique lines; the three dense targets make 28; 25 existing shapes stay)");
        Checks.Check(source.Contains("ANDROID_DEER_POPULATION_HOOK_INSTALLED sharedSource=true nativeSpawn=true"),
            "Probe announces the deer hook installation");
        Checks.Check(source.Contains("typeof(Il2Cpp.World),\"CanSpawnThicket\"") && source.Contains("typeof(Il2Cpp.Grass)")
            && source.Contains("typeof(Il2Cpp.Grass),\"RemoveThicket\""),
            "Probe resolves the exact CanSpawnThicket(Grass) and Grass.RemoveThicket() targets");
        Checks.Check(source.Contains("typeof(KingdomEnhancedMod.World_CanSpawnThicket_OptionalVegetation_Patch)")
            && source.Contains("typeof(KingdomEnhancedMod.World_AddThicket_OptionalVegetation_Patch)")
            && source.Contains("typeof(KingdomEnhancedMod.Grass_RemoveThicket_OptionalVegetation_Patch)"),
            "Probe wires the three dense patch types");
        Checks.Check(source.Contains("thicketCanSpawnPatch,\"Prefix\"") && source.Contains("thicketCanSpawnPatch,\"Postfix\"") && source.Contains("thicketCanSpawnPatch,\"Finalizer\""),
            "Probe registers the dense CanSpawn prefix/postfix/finalizer by the prepared handler names");
        Checks.Check(source.Contains("typeof(KingdomEnhancedMod.World_AddThicket_OptionalVegetation_Patch),\"Postfix\""),
            "Probe registers the dense AddThicket postfix");
        Checks.Check(source.Contains("thicketRemovePatch,\"Prefix\"") && source.Contains("thicketRemovePatch,\"Postfix\""),
            "Probe registers the dense RemoveThicket prefix/postfix");
        Checks.Check(source.Contains("LogHookCounts(thicketCanSpawn)") && source.Contains("LogHookCounts(thicketAdded)") && source.Contains("LogHookCounts(thicketRemove)"),
            "Probe reports all three dense hook counts (28 unique lines total; 25 existing shapes stay)");
        Checks.Check(source.Contains("ANDROID_DENSE_THICKETS_HOOKS_INSTALLED sharedSource=true nativeSpawn=true nativeRemove=true"),
            "Probe announces the dense-thickets hook installation");
        Checks.Check(source.Contains("KingdomEnhancedMod.PatchWorld_OptionalVegetation.Tick();"),
            "Probe drives the shared dense Tick once from the existing OnUpdate");
        Checks.Check(source.Contains("typeof(Il2Cpp.Director),\"ScheduleWaveToday\"") && source.Contains("typeof(Il2Cpp.EnemyManager),\"GetWaveTravelTime\""), "Probe resolves both real night long entries");
        Checks.Check(source.Contains("nightSchedulePatch,\"ScheduleWaveToday_Prefix\"") && source.Contains("nightSchedulePatch,\"ScheduleWaveToday_Finalizer\"") && source.Contains("PatchWorld_NightDepartureTravel),\"GetWaveTravelTime_Postfix\""), "Probe registers complete night prefix/finalizer and travel postfix method sets");
        Checks.Check(source.Contains("LogHookCounts(nightSchedule)") && source.Contains("LogHookCounts(nightTravel)"), "Probe reports two night entries (old 28 plus 2 = 30)");
        Checks.Check(source.Contains("private readonly MobileModPanel panel = new()") && source.Contains("panel.Draw(Layout)"),
            "Probe creates the common panel and hands it the shared layout");
        Checks.Check(source.Contains("panel.CancelGesture()"), "Probe cancels the common panel with the ticker lifecycle");
        Checks.Check(source.Contains("panel.Dispose()"), "Probe disposes the common panel on destroy");
        Checks.Check(source.Contains("Screen.safeArea") && source.Contains("Layout.Resize(Screen.width,Screen.height,safe.xMin,Screen.height-safe.yMax,safe.xMax,Screen.height-safe.yMin)"),
            "Probe converts the native safe area to the top-left layout bounds");
    }
}

// 公共 UI 契约（Issue #144/#146）：四个 MobileMenu 与 MobilePopulation 只做 presentation，
// 全部经 MobileModPanel.Toggle/Step/Info 公共路径（无 GUI.Button、无自持坐标与样式）；
// 16 个设置各只在一条页面上出现一次；导航/页面路由在 MobileModPanel；日历/人口只中文化
// 展示（Tick/时钟驱动不动）；csproj 的 Compile 列表是实际 source 列表。
internal static class MenuChecks
{
    internal static void Run()
    {
        string androidRoot = Path.Combine(Checks.RepositoryRoot(), "android");
        string panelPath = Path.Combine(androidRoot, "MobileModPanel.cs");
        Checks.Check(File.Exists(panelPath), "common panel source present");
        if (!File.Exists(panelPath)) return;
        string panel = File.ReadAllText(panelPath);
        string[] menuFiles = { "MobilePlayerMenu.cs", "MobileWorldMenu.cs", "MobileGenerationMenu.cs", "MobileVegetationMenu.cs" };
        var menus = new Dictionary<string, string>();
        foreach (string file in menuFiles)
        {
            string path = Path.Combine(androidRoot, file);
            Checks.Check(File.Exists(path), "menu source present: " + file);
            if (File.Exists(path)) menus[file] = File.ReadAllText(path);
        }
        if (menus.Count != menuFiles.Length) return;

        foreach (var pair in menus)
        {
            string source = pair.Value;
            Checks.Check(source.Contains("internal static void Draw(MobileModPanel panel, float width, float scale)"),
                pair.Key + " draws through the common panel");
            Checks.Check(!source.Contains("GUI.Button(") && !source.Contains("GUI.Label(") && !source.Contains("GUIStyle"),
                pair.Key + " never uses GUI controls or styles of its own");
            Checks.Check(!source.Contains("py + ") && !source.Contains("px + "),
                pair.Key + " drops the old fixed row coordinates");
        }
        Checks.Check(menus["MobilePlayerMenu.cs"].Contains("panel.Step(\"君主移动速度\"")
            && menus["MobilePlayerMenu.cs"].Contains("ModConfig.CycleSpeed")
            && menus["MobilePlayerMenu.cs"].Contains("panel.Toggle(\"坐骑无限体力\"")
            && menus["MobilePlayerMenu.cs"].Contains("panel.Toggle(\"长按连续购买\"")
            && menus["MobilePlayerMenu.cs"].Contains("panel.Step(\"坐骑技能冷却\"")
            && menus["MobilePlayerMenu.cs"].Contains("panel.Step(\"法杖神器冷却\""),
            "the player page presents the five PC-named settings through Step/Toggle");
        Checks.Check(menus["MobileWorldMenu.cs"].Contains("panel.Step(\"每波怪物数量\"")
            && menus["MobileWorldMenu.cs"].Contains("panel.Step(\"怪物时间线推进\"")
            && menus["MobileWorldMenu.cs"].Contains("panel.Toggle(\"无限金币\"")
            && menus["MobileWorldMenu.cs"].Contains("panel.Toggle(\"快速建造\"")
            && menus["MobileWorldMenu.cs"].Contains("panel.Toggle(\"船只额外乘员\"")
            && menus["MobileWorldMenu.cs"].Contains("panel.OpenVegetation"),
            "the world page presents its five settings plus the vegetation entry through the panel");
        Checks.Check(menus["MobileGenerationMenu.cs"].Contains("panel.Step(\"地图大小\"")
            && menus["MobileGenerationMenu.cs"].Contains("ModConfig.CycleMapSize"),
            "the generation page presents the map size step");
        Checks.Check(menus["MobileVegetationMenu.cs"].Contains("panel.Toggle(\"农舍猫补给\"")
            && menus["MobileVegetationMenu.cs"].Contains("panel.Toggle(\"森林快速消退\"")
            && menus["MobileVegetationMenu.cs"].Contains("panel.Toggle(\"普通鹿数量\"")
            && menus["MobileVegetationMenu.cs"].Contains("仅希腊普通鹿"),
            "the vegetation page presents the three switches with the Greek-only deer scope");
        Checks.Check(menus["MobileVegetationMenu.cs"].Contains("panel.Toggle(\"密灌木\"")
            && menus["MobileVegetationMenu.cs"].Contains("间距减半")
            && menus["MobileVegetationMenu.cs"].Contains("关闭只回收额外实例")
            && menus["MobileVegetationMenu.cs"].Contains("回收中暂不能重开"),
            "the vegetation page presents the dense-thickets switch with the accurate cleanup help");
        Checks.Check(menus["MobileVegetationMenu.cs"].Contains("ModConfig.DenseThicketsEnabled.Value || PatchWorld_OptionalVegetation.IsCleaning")
            && menus["MobileVegetationMenu.cs"].Contains("panel.Info(\"密灌木状态\", PatchWorld_OptionalVegetation.DenseStatus"),
            "the dense status row is shown only when ON or cleaning (the OFF empty display short-circuits without CurrentWorld)");
        Checks.Check(CountOf(menus["MobileVegetationMenu.cs"], "PatchWorld_OptionalVegetation.TrySetDenseThickets(") == 1
            && !menus["MobileVegetationMenu.cs"].Contains("Save("),
            "the dense row requests the shared TrySet exactly once and never saves a second time");
        Checks.Check(menus["MobileVegetationMenu.cs"].IndexOf("panel.Toggle(\"密灌木\"", StringComparison.Ordinal)
            < menus["MobileVegetationMenu.cs"].IndexOf("panel.Step(\"返回世界\"", StringComparison.Ordinal),
            "the dense rows precede the page's final return row");
        Checks.Check(menus["MobileVegetationMenu.cs"].Contains("panel.CloseVegetation"),
            "the vegetation back goes through the panel's page action");

        Checks.Check(menus["MobileWorldMenu.cs"].Contains("panel.Toggle(\"远距夜袭出发补偿\"") && menus["MobileWorldMenu.cs"].Contains("ModConfig.ToggleNightDeparture") && !menus["MobileWorldMenu.cs"].Contains("Save("), "night uses the shared Toggle and config's single-save callback");
        // 16 个可用设置各只在一条页面出现一次（植被入口是导航，不计入）。
        string pages = string.Join("\n", menus.Values);
        string[] settings = { "君主移动速度", "坐骑无限体力", "长按连续购买", "坐骑技能冷却", "法杖神器冷却",
            "每波怪物数量", "怪物时间线推进", "无限金币", "快速建造", "船只额外乘员",
            "地图大小", "农舍猫补给", "森林快速消退", "普通鹿数量", "密灌木", "远距夜袭出发补偿" };
        foreach (string title in settings)
            Checks.Check(CountOf(pages, "\"" + title + "\"") == 1,
                "the setting lives on exactly one page: " + title);
        Checks.Check(CountOf(panel, "\"常驻时间与季节\"") == 1 && CountOf(pages, "\"常驻时间与季节\"") == 0,
            "the calendar setting lives on exactly one page (the root)");

        // 导航与页面路由在面板；卡片无 GUI.Button；手势/滚动走 PanelGesture 单点生产者。
        Checks.Check(panel.Contains("\"主页\"") && panel.Contains("\"玩家\"") && panel.Contains("\"世界\"")
            && panel.Contains("\"生成\"") && panel.Contains("\"人口\""), "the panel carries the Chinese navigation chips");
        Checks.Check(panel.Contains("HitPanelBody") && panel.Contains("viewport.Contains(point)")
            && panel.Contains("gesture.Wheel(") && panel.Contains("gesture.Move(") && panel.Contains("gesture.End("),
            "the panel owns one hit-tested gesture with a shared wheel/drag scroll producer");
        Checks.Check(panel.Contains("GUI.BeginGroup(panelRect)") && panel.Contains("GUI.BeginGroup(contentGroup)")
            && panel.Contains("ImGuiCompat.IntersectRect("),
            "the whole panel is clipped and the content viewport nests the physical geometry");
        Checks.Check(!panel.Contains("GUI.Button(") && !panel.Contains("catch ("),
            "the panel never grabs capture with GUI.Button and never swallows draw exceptions");
        Checks.Check(panel.Contains("VegetationPage") && panel.Contains("SetPage("),
            "the panel routes the pages itself (vegetation included)");

        string populationPath = Path.Combine(androidRoot, "MobilePopulation.cs");
        string calendarPath = Path.Combine(androidRoot, "MobileCalendar.cs");
        Checks.Check(File.Exists(populationPath) && File.Exists(calendarPath), "population/calendar sources present");
        if (!File.Exists(populationPath) || !File.Exists(calendarPath)) return;
        string population = File.ReadAllText(populationPath);
        string calendar = File.ReadAllText(calendarPath);
        Checks.Check(population.Contains("\"Workers\"") && population.Contains("\"Archers\"") && population.Contains("\"Farmers\"")
            && population.Contains("\"Pikemen\"") && population.Contains("\"Ninjas\"") && population.Contains("\"Berserkers\"")
            && population.Contains("\"Villagers\"") && population.Contains("\"Beggars\"") && population.Contains("Knights: "),
            "the population log payload keeps the original English roster and Knights line");
        Checks.Check(population.Contains("\"工匠\"") && population.Contains("\"弓箭手\"") && population.Contains("\"农民\"")
            && population.Contains("\"长枪兵\"") && population.Contains("\"忍者\"") && population.Contains("\"狂战士\"")
            && population.Contains("\"无业村民\"") && population.Contains("\"乞丐\"") && population.Contains("\"骑士\"")
            && population.Contains("当前岛屿"),
            "the population panel shows the eight PC Chinese role names and knights");
        Checks.Check(population.Contains("ProbeTicker.Layout.Expanded && ProbeTicker.Layout.PopulationPage && ProbeTicker.UiReady"),
            "the population Tick keeps its page gate unchanged");
        Checks.Check(calendar.Contains("第 {data.TotalDay} 天") && calendar.Contains("{data.Hour} 点")
            && calendar.Contains("春") && calendar.Contains("夏") && calendar.Contains("秋") && calendar.Contains("冬"),
            "the calendar display uses the Chinese date/season strings");

        string csproj = File.ReadAllText(Path.Combine(androidRoot, "OhMyMods.AndroidProbe.csproj"));
        Checks.Check(csproj.Contains("Compile Include=\"MobileModPanel.cs\"") && csproj.Contains("Compile Include=\"PanelGesture.cs\"")
            && csproj.Contains("Compile Include=\"../il2cpp/ModPanelStyles.cs\"") && csproj.Contains("Compile Include=\"../il2cpp/ModPanelControls.cs\"")
            && csproj.Contains("Compile Include=\"../il2cpp/ImGuiCompat.cs\""),
            "the Android csproj compiles the common panel, gesture and shared UI sources");
        string testCsproj = File.ReadAllText(Path.Combine(androidRoot, "tests", "AdapterTests.csproj"));
        Checks.Check(testCsproj.Contains("Compile Include=\"../PanelGesture.cs\""),
            "the adapter tests compile the pure gesture helper");
    }

    private static int CountOf(string text, string needle)
    {
        int count = 0;
        for (int index = 0; (index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0; index += needle.Length) count++;
        return count;
    }
}

internal static class ArtifactChecks
{
    // Forge (ShopForge tag), Ammo (PayableWorkshopBarrel / PayableComponent._owner / FireTower),
    // Mead (Baker), the world bindings (EnemyManager / Wave / Wallet.InfiniteMoney) and the
    // farm-cat block (Cat / Farmhouse / Pool / Holder / Mover / coroutine bridge) must
    // actually land in the built artifact; mere compilation of dead code would not prove the
    // linked sources kept them.
    private static readonly string[] RequiredTypes =
    {
        "Il2Cpp.Player", "Il2Cpp.Payable", "Il2Cpp.PayableComponent", "Il2Cpp.PayableWorkshopBarrel",
        "Il2Cpp.FireTower", "Il2Cpp.Baker", "Il2Cpp.CurrencyType", "Il2Cpp.Managers",
        "Il2Cpp.World", "Il2Cpp.NetworkBigBoss",
        "Il2Cpp.EnemyManager", "Il2Cpp.Wave", "Il2Cpp.Wallet",
        "Il2Cpp.Cat", "Il2Cpp.Farmhouse", "Il2Cpp.Pool", "Il2Cpp.Holder",
        "Il2Cpp.ConstructionBuildingComponent", "Il2Cpp.Boat",
        "Il2Cpp.SteedAbility", "Il2Cpp.BuffUnitsSteedAbility", "Il2Cpp.GlideMovementSteedAbility",
        "Il2Cpp.SpeedBoostSteedAbility", "Il2Cpp.SummonGhostSteedAbility",
        "Il2Cpp.HermesStaff", "Il2Cpp.ItemOfPower", "_StartAbilityRoutine_d__17",
        "Il2Cpp.ForestItem", "Il2Cpp.Forest", "Il2Cpp.Grass",
        "Il2Cpp.PopulationController", "Il2Cpp.Deer", "Il2Cpp.Steed", "Il2Cpp.Hind", "Il2Cpp.Game",
        "Il2Cpp.BiomeHolder", "Il2Cpp.BiomeData", "Il2Cpp.Character", "Il2Cpp.Mover",
        "Il2Cpp.Droppable", "Il2Cpp.Kingdom", "Il2Cpp.StateMachine", "Il2Cpp.Side",
        "Il2Cpp.Embarkee", "Il2Cpp.IslandSaveData", "Il2Cpp.Game", "Il2Cpp.Farmland", "Il2Cpp.GAPS",
        "Il2Cpp.Level", "Il2Cpp.LevelConfig", "Il2Cpp.LevelLayout", "Il2Cpp.LevelBlock",
        "Il2Cpp.LevelBlockGroup", "Il2Cpp.IntRange", "Il2Cpp.ContentLayers", "Il2Cpp.Persistent",
        "Il2Cpp.PoolStamper", "Il2Cpp.Tile",
        "MelonLoader.Support.MonoEnumeratorWrapper", "Il2CppSystem.Collections.IEnumerator"
    };

    private static readonly string[] RequiredMembers =
    {
        "get__payState", "get_actionState", "get_TunnelInput", "get_hasLocalAuthority",
        "get_timeBetweenCoins", "set_timeBetweenCoins", "get_selectedPayable", "get_coins",
        "get_Price", "get_priceIncrease", "get_Currency", "get_interactingPlayer",
        "get_playerPayDistance", "get_forceBlockPayment", "get_HasWorldAuth", "get_world",
        "get_gameLayer", "get__owner", "CanPay", "CanSelect", "PlayerPayPoint", "CompareTag",
        "TryCast", "get_Pointer",
        "get_InfiniteMoney", "set_InfiniteMoney", "RoundToInt",
        "get__cooldown", "set__cooldown",
        "get__itemCooldown", "set__itemCooldown", "get___1__state", "get___4__this",
        "get_maxWorkers", "set_maxWorkers", "get_maxKnights", "set_maxKnights",
        "get_maxPikemen", "set_maxPikemen", "get_maxFarmers", "set_maxFarmers",
        "get_blocks", "get__levelEdges", "get_groupOne", "get_groupTwo", "get_groupThree",
        "get_absoluteCenter", "get_cameraBlockMarker", "get_isDangerSource", "get_persistObject",
        "get_path", "get_targetPool", "get_noGround", "get_childCount", "GetChild", "GetComponents",
        "il2cpp_class_get_name_", "TotalWidth",
        "SpawnOrInstantiate", "DespawnOrDestroy", "get_catPrefab", "GetBorderSideIntact",
        "get_controlsForestSize", "get_removedByForest", "get_removeDelay", "get__forest",
        "get_density", "set_density", "get_winterDensityDefault", "set_winterDensityDefault",
        "get_winterDensitySpecial", "set_winterDensitySpecial", "get__actualUpdateInterval", "set__actualUpdateInterval",
        "get_prefab", "get_useBiomeCritters", "get_enabled", "get_gameObject", "get_scene", "get_handle",
        "get_transform", "get_activeInHierarchy", "IsChildOf", "GetComponent", "GetInstanceID",
        "get_Inst", "get_GreeceBiomeIndex", "get_BiomeIndex", "get_playingOrInMenuWithClient", "get_game",
        "get_thicketSpacing", "set_thicketSpacing", "get__grassWithThicket",
        "get__thicket", "RemoveThicket", "GetComponentsInChildren", "get_color", "set_color"
    };

    private static readonly string[] RequiredAssemblies =
    {
        "Assembly-CSharp", "MelonLoader", "0Harmony", "Il2CppInterop.Runtime"
    };

    // Explicit registration contract: name -> declared parameter count. The Hold handlers are
    // private static; the enemy prefixes are public static (Probe passes them to HarmonyMethod
    // by name), so the access requirement is checked per type and not copied across.
    private static readonly Dictionary<string, int> RequiredHoldHandlers = new Dictionary<string, int>
    {
        { "UpdatePayState_Prefix", 4 },
        { "UpdatePayState_Postfix", 3 },
        { "UpdatePayState_Finalizer", 3 },
        { "PerformPay_Postfix", 1 }
    };

    private static readonly Dictionary<string, int> RequiredWorldHandlers = new Dictionary<string, int>
    {
        { "AddEnemies_Prefix", 1 },
        { "GetEnemies_Prefix", 3 }
    };

    internal static void Run(string dllPath)
    {
        using var stream = File.OpenRead(dllPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        bool dontPatchAll = false;
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (AttributeTypeName(reader, attribute.Constructor) == "MelonLoader.HarmonyDontPatchAllAttribute")
                dontPatchAll = true;
        }
        Checks.Check(dontPatchAll, "artifact carries MelonLoader.HarmonyDontPatchAll");

        var hold = FindType(reader, "KingdomEnhancedMod", "PatchPlayer_HoldPurchase");
        Checks.Check(!hold.IsNil, "artifact contains linked PatchPlayer_HoldPurchase");
        CheckHandlers(reader, hold, RequiredHoldHandlers, MethodAttributes.Private, "private static");

        var world = FindType(reader, "KingdomEnhancedMod", "PatchWorld_EnemyManager");
        Checks.Check(!world.IsNil, "artifact contains linked PatchWorld_EnemyManager");
        CheckHandlers(reader, world, RequiredWorldHandlers, MethodAttributes.Public, "public static");

        // Farm-cat block (linked PatchWorld_FarmCats / FarmCatMovement / GreekScaleScope) and the
        // Operator's explicit registration targets. The Android build must contain no
        // ScaleRegistryHolder stand-in; ScaleRegistryHolder.Register is replaced at the source
        // boundary by GreekScaleScope.Register.
        Checks.Check(!FindType(reader, "KingdomEnhancedMod", "PatchWorld_FarmCats").IsNil, "artifact contains linked PatchWorld_FarmCats");
        Checks.Check(!FindType(reader, "KingdomEnhancedMod", "FarmCatMovement").IsNil, "artifact contains linked FarmCatMovement");
        Checks.Check(!FindType(reader, "KingdomEnhancedMod", "GreekScaleScope").IsNil, "artifact contains linked GreekScaleScope");
        Checks.Check(FindType(reader, "KingdomEnhancedMod", "ScaleRegistryHolder").IsNil, "artifact contains no ScaleRegistryHolder stand-in");
        CheckHandlers(reader, FindType(reader, "KingdomEnhancedMod", "PatchWorld_FarmCats"),
            new Dictionary<string, int> { { "Schedule", 1 } }, MethodAttributes.Public, "public static");
        CheckHandlers(reader, FindType(reader, "KingdomEnhancedMod", "Cat_OnEnable_FarmMovement_Patch"),
            new Dictionary<string, int> { { "Postfix", 2 } }, MethodAttributes.Assembly, "internal static");
        CheckHandlers(reader, FindType(reader, "KingdomEnhancedMod", "Cat_OnDisable_FarmMovement_Patch"),
            new Dictionary<string, int> { { "Prefix", 1 } }, MethodAttributes.Assembly, "internal static");
        CheckHandlers(reader, FindType(reader, "KingdomEnhancedMod", "Cat_Update_FarmMovement_Patch"),
            new Dictionary<string, int> { { "Postfix", 2 } }, MethodAttributes.Assembly, "internal static");
        CheckHandlers(reader, FindType(reader, "OhMyMods.AndroidProbe", "Probe"),
            new Dictionary<string, int> { { "FarmCatsWorldLoaded", 1 } }, MethodAttributes.Private, "private static");
        CheckHandlers(reader, FindType(reader, "BepInEx.Unity.IL2CPP.Utils.Collections", "AndroidCoroutine"),
            new Dictionary<string, int> { { "WrapToIl2Cpp", 1 } }, MethodAttributes.Assembly, "internal static");
        Checks.Check(HasConstructorReference(reader, "MelonLoader.Support.MonoEnumeratorWrapper"),
            "MonoEnumeratorWrapper(IEnumerator) ctor is referenced by the coroutine bridge");
        Checks.Check(HasConstructorReference(reader, "Il2CppSystem.Collections.IEnumerator"),
            "Il2CppSystem.Collections.IEnumerator(IntPtr) ctor is referenced by the coroutine bridge");

        // Fast-build block (linked PatchWorld_Construction): the shared prefix must land in the
        // artifact, take ConstructionBuildingComponent as its target and really call the interop
        // rate setter. Which native method the Operator registers it on is Probe's registration
        // evidence, not something this metadata snapshot can re-derive.
        var construction = FindType(reader, "KingdomEnhancedMod", "PatchWorld_Construction");
        Checks.Check(!construction.IsNil, "artifact contains linked PatchWorld_Construction");
        CheckHandlers(reader, construction, new Dictionary<string, int> { { "Prefix", 1 } }, MethodAttributes.Public, "public static");
        if (!construction.IsNil)
            Checks.Check(SignatureTypes(reader, reader.GetTypeDefinition(construction), "Prefix") == "Il2Cpp.ConstructionBuildingComponent",
                "the fast-build Prefix patches ConstructionBuildingComponent (its single parameter)");
        Checks.Check(HasMemberReference(reader, "Il2Cpp.ConstructionBuildingComponent", "set__autoBuildRate"),
            "the fast-build prefix really calls the Il2Cpp _autoBuildRate setter");

        // Steed-cooldown block (android/PatchRide_SteedCooldown.cs, adapted source): the four
        // prefixes plus the shared Finalizer must land in the artifact and carry no Harmony
        // attributes — the Operator registers them explicitly, like the Hold handlers.
        var cooldown = FindType(reader, "KingdomEnhancedMod", "PatchRide_SteedCooldown");
        Checks.Check(!cooldown.IsNil, "artifact contains PatchRide_SteedCooldown");
        CheckHandlers(reader, cooldown, new Dictionary<string, int>
        {
            { "BasePrefix", 2 }, { "BuffPrefix", 2 }, { "GlidePrefix", 2 }, { "SpeedPrefix", 2 }, { "Finalizer", 2 }
        }, MethodAttributes.Assembly, "internal static");
        if (!cooldown.IsNil)
        {
            bool cooldownHarmonyAttribute = false;
            foreach (var handle in reader.GetTypeDefinition(cooldown).GetMethods())
            {
                var method = reader.GetMethodDefinition(handle);
                foreach (var attributeHandle in method.GetCustomAttributes())
                    if (AttributeTypeName(reader, reader.GetCustomAttribute(attributeHandle).Constructor).StartsWith("HarmonyLib."))
                        cooldownHarmonyAttribute = true;
            }
            Checks.Check(!cooldownHarmonyAttribute, "PatchRide_SteedCooldown carries no Harmony attributes (explicit registration only)");
            var cooldownDefinition = reader.GetTypeDefinition(cooldown);
            foreach (string prefix in new[] { "BasePrefix", "BuffPrefix", "GlidePrefix", "SpeedPrefix" })
                Checks.Check(SignatureTypes(reader, cooldownDefinition, prefix) == "Il2Cpp.SteedAbility,Borrow&",
                    prefix + "(SteedAbility, out Borrow) signature (Harmony __instance/__state shape)");
            Checks.Check(SignatureTypes(reader, cooldownDefinition, "Finalizer") == "System.Exception,Borrow",
                "Finalizer(Exception, Borrow) signature returns the original exception");
        }

        // Staff base-cooldown block (android/PatchDivine_StaffCooldown.cs, adapted source): the
        // two prefixes plus the shared Finalizer must land in the artifact with the nested
        // state-machine and ItemOfPower interop shapes and carry no Harmony attributes — the
        // Operator registers them explicitly on the Probe handler names, like the Hold handlers.
        var staffPatch = FindType(reader, "KingdomEnhancedMod", "PatchDivine_StaffCooldown");
        Checks.Check(!staffPatch.IsNil, "artifact contains PatchDivine_StaffCooldown");
        CheckHandlers(reader, staffPatch, new Dictionary<string, int>
        {
            { "RoutinePrefix", 2 }, { "CanCancelPrefix", 2 }, { "Finalizer", 2 }
        }, MethodAttributes.Assembly, "internal static");
        if (!staffPatch.IsNil)
        {
            bool staffHarmonyAttribute = false;
            foreach (var handle in reader.GetTypeDefinition(staffPatch).GetMethods())
            {
                var method = reader.GetMethodDefinition(handle);
                foreach (var attributeHandle in method.GetCustomAttributes())
                    if (AttributeTypeName(reader, reader.GetCustomAttribute(attributeHandle).Constructor).StartsWith("HarmonyLib."))
                        staffHarmonyAttribute = true;
            }
            Checks.Check(!staffHarmonyAttribute, "PatchDivine_StaffCooldown carries no Harmony attributes (explicit registration only)");
            var staffDefinition = reader.GetTypeDefinition(staffPatch);
            Checks.Check(SignatureTypes(reader, staffDefinition, "RoutinePrefix") == "_StartAbilityRoutine_d__17,Borrow&",
                "RoutinePrefix(_StartAbilityRoutine_d__17, out Borrow) binds the nested state machine (Harmony __instance/__state shape)");
            Checks.Check(SignatureTypes(reader, staffDefinition, "CanCancelPrefix") == "Il2Cpp.ItemOfPower,Borrow&",
                "CanCancelPrefix(ItemOfPower, out Borrow) binds the shared item base (Harmony __instance/__state shape)");
            Checks.Check(SignatureTypes(reader, staffDefinition, "Finalizer") == "System.Exception,Borrow",
                "Finalizer(Exception, Borrow) signature returns the original exception");
        }

        // Fast-forest-recede block (shared il2cpp/PatchWorld_FastForestRecede.cs linked with an
        // #if ANDROID alias header, plus the kept wrapper name so the PC auto-patch surface
        // survives): the shared class, the typed ForestItem/ref-float Prefix shape, the class
        // attribute target and the interop members the shared source consumes must land in the
        // artifact. Dead compiled-away code would not prove any of this.
        var fastForest = FindType(reader, "KingdomEnhancedMod", "PatchWorld_FastForestRecede");
        Checks.Check(!fastForest.IsNil, "artifact contains the shared PatchWorld_FastForestRecede");
        CheckHandlers(reader, fastForest, new Dictionary<string, int> { { "ScaleForestRecedeDelay", 2 } },
            MethodAttributes.Assembly, "internal static");
        if (!fastForest.IsNil)
            Checks.Check(SignatureTypes(reader, reader.GetTypeDefinition(fastForest), "ScaleForestRecedeDelay")
                == "Il2Cpp.ForestItem,System.Single&",
                "ScaleForestRecedeDelay(Il2Cpp.ForestItem, ref float) binds the typed ANDROID alias");
        var forestPatch = FindType(reader, "KingdomEnhancedMod", "ForestItem_FadeAndRemove_OptionalVegetation_Patch");
        Checks.Check(!forestPatch.IsNil, "artifact contains the kept forest wrapper");
        CheckHandlers(reader, forestPatch, new Dictionary<string, int> { { "Prefix", 2 } }, MethodAttributes.Assembly, "internal static");
        if (!forestPatch.IsNil)
        {
            var forestPatchDefinition = reader.GetTypeDefinition(forestPatch);
            var wrapperMethods = new HashSet<string>();
            foreach (var handle in forestPatchDefinition.GetMethods())
                wrapperMethods.Add(reader.GetString(reader.GetMethodDefinition(handle).Name));
            Checks.Check(wrapperMethods.SetEquals(new[] { "Prefix" }),
                "the kept wrapper is prefix-only (no postfix/finalizer state debt)");
            Checks.Check(SignatureTypes(reader, forestPatchDefinition, "Prefix") == "Il2Cpp.ForestItem,System.Single&",
                "Prefix(Il2Cpp.ForestItem, ref float delay) binds the typed ANDROID alias and the by-ref delay");
            Checks.Check(HasClassHarmonyPatchTarget(reader, forestPatchDefinition, "ForestItem", "FadeAndRemove"),
                "the wrapper keeps the [HarmonyPatch(typeof(ForestItem), FadeAndRemove)] class attribute for PC auto-patch");
            Checks.Check(BodyCallsMethod(pe, reader, forestPatchDefinition, "Prefix",
                    "ScaleForestRecedeDelay", "KingdomEnhancedMod.PatchWorld_FastForestRecede"),
                "the wrapper Prefix really calls the shared PatchWorld_FastForestRecede helper");
        }

        // Deer-population block (shared il2cpp/PatchWorld_DeerPopulation.cs linked with the
        // #if ANDROID alias header, the Android opt-in gate and the captured scope evidence):
        // the shared class, the typed class-attribute target, the internal
        // prefix/postfix/finalizer handler shapes and the real calls into the shared helper
        // must land in the artifact. Android registration is explicit through Probe, which the
        // source-level ProbeChecks pin separately.
        var deer = FindType(reader, "KingdomEnhancedMod", "PatchWorld_DeerPopulation");
        Checks.Check(!deer.IsNil, "artifact contains the shared PatchWorld_DeerPopulation");
        var deerPatch = FindType(reader, "KingdomEnhancedMod", "PopulationController_Update_DeerPopulation_Patch");
        Checks.Check(!deerPatch.IsNil, "artifact contains the deer patch wrapper");
        CheckHandlers(reader, deerPatch, new Dictionary<string, int>
        {
            { "Prefix", 2 }, { "Postfix", 1 }, { "Finalizer", 2 }
        }, MethodAttributes.Assembly, "internal static");
        if (!deerPatch.IsNil)
        {
            var deerPatchDefinition = reader.GetTypeDefinition(deerPatch);
            Checks.Check(SignatureTypes(reader, deerPatchDefinition, "Prefix") == "Il2Cpp.PopulationController,Lease&",
                "Prefix(Il2Cpp.PopulationController, out Lease) binds the typed ANDROID alias and the Harmony __state");
            Checks.Check(SignatureTypes(reader, deerPatchDefinition, "Postfix") == "Lease",
                "Postfix(Lease) binds the shared lease state");
            Checks.Check(SignatureTypes(reader, deerPatchDefinition, "Finalizer") == "System.Exception,Lease",
                "Finalizer(Exception, Lease) returns the original exception");
            Checks.Check(HasClassHarmonyPatchTarget(reader, deerPatchDefinition, "PopulationController", "Update"),
                "the wrapper keeps the [HarmonyPatch(typeof(PopulationController), Update)] class attribute for PC auto-patch");
            Checks.Check(BodyCallsMethod(pe, reader, deerPatchDefinition, "Prefix",
                    "Begin", "KingdomEnhancedMod.PatchWorld_DeerPopulation"),
                "the wrapper Prefix really calls the shared Begin helper");
            Checks.Check(BodyCallsMethod(pe, reader, deerPatchDefinition, "Postfix",
                    "Restore", "KingdomEnhancedMod.PatchWorld_DeerPopulation"),
                "the wrapper Postfix really calls the shared Restore helper");
        }
        var modConfig = FindType(reader, "KingdomEnhancedMod", "ModConfig");
        bool deerEntry = false;
        bool denseEntry = false;
        if (!modConfig.IsNil)
            foreach (var handle in reader.GetTypeDefinition(modConfig).GetFields())
            {
                string fieldName = reader.GetString(reader.GetFieldDefinition(handle).Name);
                if (fieldName == "DeerPopulationEnabled") deerEntry = true;
                if (fieldName == "DenseThicketsEnabled") denseEntry = true;
            }
        Checks.Check(deerEntry, "the fifteenth ModConfig entry field DeerPopulationEnabled lands in the artifact");
        Checks.Check(denseEntry, "the sixteenth ModConfig entry field DenseThicketsEnabled lands in the artifact");

        // Dense-thickets block (shared il2cpp/PatchWorld_OptionalVegetation.cs linked with the
        // #if ANDROID alias header, the FX-seam cut, the default-off debt-free gate and the
        // three single-save config writers): the shared class surface, the three wrapper types,
        // the typed handler shapes and the real calls into the shared helpers must land in the
        // artifact, and the Android build must carry no SpriteRendererFX/FadeOut surface.
        var dense = FindType(reader, "KingdomEnhancedMod", "PatchWorld_OptionalVegetation");
        Checks.Check(!dense.IsNil, "artifact contains the shared PatchWorld_OptionalVegetation");
        if (!dense.IsNil)
        {
            var denseMethods = new HashSet<string>();
            foreach (var handle in reader.GetTypeDefinition(dense).GetMethods())
                denseMethods.Add(reader.GetString(reader.GetMethodDefinition(handle).Name));
            Checks.Check(denseMethods.Contains("BeginCanSpawn") && denseMethods.Contains("FinishCanSpawn")
                && denseMethods.Contains("AbortCanSpawn") && denseMethods.Contains("OnThicketAdded")
                && denseMethods.Contains("OnNativeRemoveThicket") && denseMethods.Contains("OnThicketRemoved")
                && denseMethods.Contains("Tick") && denseMethods.Contains("TrySetDenseThickets")
                && denseMethods.Contains("get_DenseStatus") && denseMethods.Contains("get_IsCleaning"),
                "the shared dense surface (hooks, Tick, TrySet and the panel status accessors) lands in the artifact");
            // Issue #146 color-responsibility fix: the Android receipt carries the per-layer
            // attempted/pending-intent arrays next to BaseColors/Applied and clears them in the
            // same paths (the typed ANDROID host checks the lifecycle; this pins the shipped shape).
            TypeDefinitionHandle denseRecord = default;
            foreach (var nestedHandle in reader.GetTypeDefinition(dense).GetNestedTypes())
            {
                if (reader.GetString(reader.GetTypeDefinition(nestedHandle).Name) == "ExtraThicket")
                {
                    denseRecord = nestedHandle;
                    break;
                }
            }
            Checks.Check(!denseRecord.IsNil, "the dense receipt nested type ExtraThicket lands in the artifact");
            if (!denseRecord.IsNil)
            {
                bool attemptedField = false, intentField = false;
                foreach (var fieldHandle in reader.GetTypeDefinition(denseRecord).GetFields())
                {
                    string fieldName = reader.GetString(reader.GetFieldDefinition(fieldHandle).Name);
                    if (fieldName == "Attempted") attemptedField = true;
                    if (fieldName == "PendingIntent") intentField = true;
                }
                Checks.Check(attemptedField && intentField,
                    "the Android receipt carries the attempted/pending-intent fields next to Applied");
            }
        }
        var denseCanSpawn = FindType(reader, "KingdomEnhancedMod", "World_CanSpawnThicket_OptionalVegetation_Patch");
        Checks.Check(!denseCanSpawn.IsNil, "artifact contains the dense CanSpawn wrapper");
        CheckHandlers(reader, denseCanSpawn, new Dictionary<string, int>
        {
            { "Prefix", 2 }, { "Postfix", 4 }, { "Finalizer", 2 }
        }, MethodAttributes.Assembly, "internal static");
        if (!denseCanSpawn.IsNil)
        {
            var denseCanSpawnDefinition = reader.GetTypeDefinition(denseCanSpawn);
            Checks.Check(SignatureTypes(reader, denseCanSpawnDefinition, "Prefix") == "Il2Cpp.World,CanSpawnLease&",
                "dense Prefix(World, out CanSpawnLease) binds the Harmony __state");
            Checks.Check(SignatureTypes(reader, denseCanSpawnDefinition, "Postfix") == "Il2Cpp.World,Il2Cpp.Grass,System.Boolean&,CanSpawnLease",
                "dense Postfix(World, Grass, ref bool, CanSpawnLease) binds the native window");
            Checks.Check(SignatureTypes(reader, denseCanSpawnDefinition, "Finalizer") == "System.Exception,CanSpawnLease",
                "dense Finalizer(Exception, CanSpawnLease) returns the original exception");
            Checks.Check(HasClassHarmonyPatchTarget(reader, denseCanSpawnDefinition, "World", "CanSpawnThicket"),
                "the dense CanSpawn wrapper keeps the [HarmonyPatch(typeof(World), CanSpawnThicket)] class attribute for PC auto-patch");
            Checks.Check(BodyCallsMethod(pe, reader, denseCanSpawnDefinition, "Prefix",
                    "BeginCanSpawn", "KingdomEnhancedMod.PatchWorld_OptionalVegetation"),
                "the dense CanSpawn Prefix really calls the shared BeginCanSpawn helper");
            Checks.Check(BodyCallsMethod(pe, reader, denseCanSpawnDefinition, "Postfix",
                    "FinishCanSpawn", "KingdomEnhancedMod.PatchWorld_OptionalVegetation"),
                "the dense CanSpawn Postfix really calls the shared FinishCanSpawn helper");
            Checks.Check(BodyCallsMethod(pe, reader, denseCanSpawnDefinition, "Finalizer",
                    "AbortCanSpawn", "KingdomEnhancedMod.PatchWorld_OptionalVegetation"),
                "the dense CanSpawn Finalizer really calls the shared AbortCanSpawn helper");
        }
        var denseAdded = FindType(reader, "KingdomEnhancedMod", "World_AddThicket_OptionalVegetation_Patch");
        Checks.Check(!denseAdded.IsNil, "artifact contains the dense AddThicket wrapper");
        CheckHandlers(reader, denseAdded, new Dictionary<string, int> { { "Postfix", 2 } }, MethodAttributes.Assembly, "internal static");
        if (!denseAdded.IsNil)
            Checks.Check(BodyCallsMethod(pe, reader, reader.GetTypeDefinition(denseAdded), "Postfix",
                    "OnThicketAdded", "KingdomEnhancedMod.PatchWorld_OptionalVegetation"),
                "the dense AddThicket Postfix really calls the shared OnThicketAdded helper");
        var denseRemove = FindType(reader, "KingdomEnhancedMod", "Grass_RemoveThicket_OptionalVegetation_Patch");
        Checks.Check(!denseRemove.IsNil, "artifact contains the dense RemoveThicket wrapper");
        CheckHandlers(reader, denseRemove, new Dictionary<string, int> { { "Prefix", 1 }, { "Postfix", 1 } },
            MethodAttributes.Assembly, "internal static");
        if (!denseRemove.IsNil)
        {
            var denseRemoveDefinition = reader.GetTypeDefinition(denseRemove);
            Checks.Check(HasClassHarmonyPatchTarget(reader, denseRemoveDefinition, "Grass", "RemoveThicket"),
                "the dense RemoveThicket wrapper keeps the [HarmonyPatch(typeof(Grass), RemoveThicket)] class attribute for PC auto-patch");
            Checks.Check(BodyCallsMethod(pe, reader, denseRemoveDefinition, "Prefix",
                    "OnNativeRemoveThicket", "KingdomEnhancedMod.PatchWorld_OptionalVegetation"),
                "the dense RemoveThicket Prefix really calls the shared OnNativeRemoveThicket helper");
            Checks.Check(BodyCallsMethod(pe, reader, denseRemoveDefinition, "Postfix",
                    "OnThicketRemoved", "KingdomEnhancedMod.PatchWorld_OptionalVegetation"),
                "the dense RemoveThicket Postfix really calls the shared OnThicketRemoved helper");
        }

        bool nightEntry = false;
        if (!modConfig.IsNil) foreach (var h in reader.GetTypeDefinition(modConfig).GetFields())
            if (reader.GetString(reader.GetFieldDefinition(h).Name) == "NightDepartureEnabled") nightEntry = true;
        Checks.Check(nightEntry, "the seventeenth real ModConfig entry NightDepartureEnabled lands in the artifact");

        // Issue #184 营地补员批：三个新 entry 字段 + 三份共享源的补丁/协调器/诊断类型
        // 与原生 static-storage 静态类（无 managed 泛型实例字段）都必须在产物里。
        bool populationEntry = false, capacityEntry = false, intervalEntry = false;
        if (!modConfig.IsNil) foreach (var h in reader.GetTypeDefinition(modConfig).GetFields())
        {
            string fieldName = reader.GetString(reader.GetFieldDefinition(h).Name);
            if (fieldName == "PopulationEnabled") populationEntry = true;
            if (fieldName == "BeggarCampCapacity") capacityEntry = true;
            if (fieldName == "BeggarSpawnIntervalSeconds") intervalEntry = true;
        }
        Checks.Check(populationEntry, "the eighteenth real ModConfig entry PopulationEnabled lands in the artifact");
        Checks.Check(capacityEntry, "the nineteenth real ModConfig entry BeggarCampCapacity lands in the artifact");
        Checks.Check(intervalEntry, "the twentieth real ModConfig entry BeggarSpawnIntervalSeconds lands in the artifact");
        var populationApply = FindType(reader, "KingdomEnhancedMod", "PopulationPerformanceApplyPatch");
        var populationCoordinator = FindType(reader, "KingdomEnhancedMod", "PopulationPerformanceCoordinator");
        var campAwakeEntry = FindType(reader, "KingdomEnhancedMod", "BeggarCamp_Awake_Patch");
        var beggarLifecycleEntry = FindType(reader, "KingdomEnhancedMod", "Beggar_PopulationLifecycle_Patch");
        var populationGrounding = FindType(reader, "KingdomEnhancedMod", "PopulationGrounding");
        Checks.Check(!populationApply.IsNil && !populationCoordinator.IsNil && !campAwakeEntry.IsNil
            && !beggarLifecycleEntry.IsNil && !populationGrounding.IsNil,
            "artifact contains the shared population entry shells, coordinator and grounding diagnostic");
        CheckHandlers(reader, populationApply, new Dictionary<string, int> { { "Postfix", 1 } },
            MethodAttributes.Public, "public static");
        CheckHandlers(reader, campAwakeEntry, new Dictionary<string, int>
        {
            { "Awake_Prefix", 1 }, { "Awake_Postfix", 1 }, { "OnDestroy_Prefix", 1 }
        }, MethodAttributes.Public, "public static");
        CheckHandlers(reader, beggarLifecycleEntry, new Dictionary<string, int>
        {
            { "OnEnable_Prefix", 1 }, { "OnDisable_Prefix", 1 }
        }, MethodAttributes.Public, "public static");
        foreach (var entry in new[]
        {
            (populationApply, "Postfix", "Il2Cpp.CampaignSaveData", "BeginScene"),
            (campAwakeEntry, "Awake_Prefix", "Il2Cpp.BeggarCamp", "CaptureProfile"),
            (campAwakeEntry, "Awake_Postfix", "Il2Cpp.BeggarCamp", "ConfigureCamp"),
            (campAwakeEntry, "OnDestroy_Prefix", "Il2Cpp.BeggarCamp", "ForgetCamp"),
            (beggarLifecycleEntry, "OnEnable_Prefix", "Il2Cpp.Beggar", "BeginBeggarIncarnation"),
            (beggarLifecycleEntry, "OnDisable_Prefix", "Il2Cpp.Beggar", "ForgetBeggar")
        })
        {
            if (entry.Item1.IsNil) continue;
            var definition = reader.GetTypeDefinition(entry.Item1);
            Checks.Check(SignatureTypes(reader, definition, entry.Item2) == entry.Item3,
                "population handler " + entry.Item2 + " binds the actual Android native instance type");
            Checks.Check(BodyCallsMethod(pe, reader, definition, entry.Item2, entry.Item4,
                    "KingdomEnhancedMod.PopulationPerformanceCoordinator"),
                "population handler " + entry.Item2 + " calls the single shared coordinator entry " + entry.Item4);
        }
        if (!populationCoordinator.IsNil)
        {
            var coordinatorDefinition = reader.GetTypeDefinition(populationCoordinator);
            Checks.Check(BodyCallsMethod(pe, reader, coordinatorDefinition, "EnableCurrentScene", "BeginScene",
                    "KingdomEnhancedMod.PopulationPerformanceCoordinator"),
                "the Android enable entry really reuses BeginScene instead of a second scene scan");
            Checks.Check(BodyCallsMethod(pe, reader, coordinatorDefinition, "StopAndRelease", "ReleaseToNative",
                    "KingdomEnhancedMod.PopulationPerformanceCoordinator"),
                "the single stop path really releases the owned fields exactly once before clearing state");
        }
        if (!populationGrounding.IsNil)
        {
            var members = BodyReferencedMembers(pe, reader, reader.GetTypeDefinition(populationGrounding));
            Checks.Check(!members.Contains("UnityEngine.Rigidbody2D::get_simulated")
                && !members.Contains("UnityEngine.Rigidbody2D::get_constraints")
                && !members.Contains("UnityEngine.Rigidbody2D::get_collisionDetectionMode")
                && !members.Contains("UnityEngine.Physics2D::GetIgnoreLayerCollision"),
                "the actual Android Grounding artifact does not call the four unsupported physics reads");
            Checks.Check(members.Contains("UnityEngine.Rigidbody2D::get_bodyType")
                && members.Contains("UnityEngine.Rigidbody2D::get_gravityScale")
                && members.Contains("UnityEngine.Rigidbody2D::get_velocity")
                && members.Contains("UnityEngine.Collider2D::get_bounds"),
                "the Android Grounding artifact retains the supported native body and collider reads");
        }
        var nightSchedule = FindType(reader, "KingdomEnhancedMod", "PatchWorld_NightDeparture");
        var nightTravel = FindType(reader, "KingdomEnhancedMod", "PatchWorld_NightDepartureTravel");
        Checks.Check(!nightSchedule.IsNil && !nightTravel.IsNil, "artifact contains both shared night patch classes");
        CheckHandlers(reader, nightSchedule, new Dictionary<string,int>{{"ScheduleWaveToday_Prefix",5},{"ScheduleWaveToday_Finalizer",2}}, MethodAttributes.Assembly,"internal static");
        CheckHandlers(reader, nightTravel, new Dictionary<string,int>{{"GetWaveTravelTime_Postfix",5}}, MethodAttributes.Assembly,"internal static");
        if (!nightSchedule.IsNil) {
            var n = reader.GetTypeDefinition(nightSchedule);
            Checks.Check(SignatureTypes(reader,n,"ScheduleWaveToday_Prefix")=="Il2Cpp.Director,Il2Cpp.Wave,Il2Cpp.Side,System.Single,State&", "night Schedule Prefix binds actual Android native types and state");
            Checks.Check(SignatureTypes(reader,n,"ScheduleWaveToday_Finalizer")=="System.Exception,State&", "night Finalizer preserves exception/ref state shape");
            Checks.Check(BodyCallsMethod(pe,reader,n,"ScheduleWaveToday_Prefix","Arm","KingdomEnhancedMod.NightDepartureScope"),"night Prefix really calls shared Arm");
        }
        if (!nightTravel.IsNil) {
            var n=reader.GetTypeDefinition(nightTravel);
            Checks.Check(SignatureTypes(reader,n,"GetWaveTravelTime_Postfix")=="Il2Cpp.EnemyManager,Il2Cpp.Wave,System.Single,System.Int32,System.Single&","night Travel Postfix actual typed native signature");
            Checks.Check(BodyCallsMethod(pe,reader,n,"GetWaveTravelTime_Postfix","TryAdjustTravel","KingdomEnhancedMod.NightDepartureScope"),"night Postfix really calls shared TryAdjustTravel");
        }
        Checks.Check(!FindType(reader,"KingdomEnhancedMod","NightDepartureTiming").IsNil && !FindType(reader,"KingdomEnhancedMod","NightDepartureScope").IsNil, "shared night scope and pure planner land in actual artifact");
        Checks.Check(HasMemberReference(reader,"Il2Cpp.Director","GetTimesOfDayForDay") && HasMemberReference(reader,"Il2Cpp.TimesOfDay","get_eveningStart") && HasMemberReference(reader,"Il2Cpp.TimesOfDay","get_dawnStart"),"night uses actual opaque native profile API and scalar getters");

        // Boat-capacity block (android/PatchWorld_BoatCapacity.cs + the shared policy
        // il2cpp/BoatCapacityProfile.cs): the one Prefix + one Finalizer must land in the
        // artifact with the Harmony __instance/__state shape and no auto-scan attributes, and
        // the shared profile must be compiled in with the four contract constants (the consts
        // are inlined at the use sites, so their literal values are checked directly).
        var boat = FindType(reader, "KingdomEnhancedMod", "PatchWorld_BoatCapacity");
        Checks.Check(!boat.IsNil, "artifact contains PatchWorld_BoatCapacity");
        CheckHandlers(reader, boat, new Dictionary<string, int> { { "Prefix", 2 }, { "Finalizer", 2 } },
            MethodAttributes.Assembly, "internal static");
        if (!boat.IsNil)
        {
            bool boatHarmonyAttribute = false;
            foreach (var handle in reader.GetTypeDefinition(boat).GetMethods())
            {
                var method = reader.GetMethodDefinition(handle);
                foreach (var attributeHandle in method.GetCustomAttributes())
                    if (AttributeTypeName(reader, reader.GetCustomAttribute(attributeHandle).Constructor).StartsWith("HarmonyLib."))
                        boatHarmonyAttribute = true;
            }
            Checks.Check(!boatHarmonyAttribute, "PatchWorld_BoatCapacity carries no Harmony attributes (explicit registration only)");
            var boatDefinition = reader.GetTypeDefinition(boat);
            Checks.Check(SignatureTypes(reader, boatDefinition, "Prefix") == "Il2Cpp.Boat,Borrow&",
                "Prefix(Boat, out Borrow) signature (Harmony __instance/__state shape)");
            Checks.Check(SignatureTypes(reader, boatDefinition, "Finalizer") == "System.Exception,Borrow",
                "Finalizer(Exception, Borrow) signature returns the original exception");
        }

        var profile = FindType(reader, "KingdomEnhancedMod", "BoatCapacityProfile");
        Checks.Check(!profile.IsNil, "artifact contains the shared BoatCapacityProfile");
        if (!profile.IsNil)
        {
            Checks.Check(ConstantValue(reader, profile, "Workers") == 8, "BoatCapacityProfile.Workers is the literal 8");
            Checks.Check(ConstantValue(reader, profile, "Knights") == 6, "BoatCapacityProfile.Knights is the literal 6");
            Checks.Check(ConstantValue(reader, profile, "Pikemen") == 8, "BoatCapacityProfile.Pikemen is the literal 8");
            Checks.Check(ConstantValue(reader, profile, "Farmers") == 3, "BoatCapacityProfile.Farmers is the literal 3");
        }

        // Map-width block (shared il2cpp/MapWidthPlanner.cs + il2cpp/MapWidthTerrain.cs +
        // il2cpp/PatchWorld_Level.cs, linked read-only; the two adapter files only carry the
        // #if ANDROID alias header). The planner/scope must land in the artifact, both hook
        // types must carry the exact explicit-registration handler shapes, the GetBlocks
        // postfix must bind the real Il2Cpp type shapes, and the native members the scope
        // consumes must really be referenced (dead compiled-away code would not prove it).
        var levelPatch = FindType(reader, "KingdomEnhancedMod", "PatchWorld_Level");
        Checks.Check(!levelPatch.IsNil, "artifact contains PatchWorld_Level");
        CheckHandlers(reader, levelPatch, new Dictionary<string, int>
        {
            { "Prefix", 1 }, { "Postfix", 2 }, { "Finalizer", 2 }
        }, MethodAttributes.Private, "private static");
        if (!levelPatch.IsNil)
        {
            var levelPatchDefinition = reader.GetTypeDefinition(levelPatch);
            Checks.Check(SignatureTypes(reader, levelPatchDefinition, "Prefix") == "Frame&",
                "PatchWorld_Level.Prefix(out MapWidthScope.Frame) signature (Harmony __state shape)");
            Checks.Check(SignatureTypes(reader, levelPatchDefinition, "Postfix") == "Il2Cpp.Level,Frame",
                "PatchWorld_Level.Postfix(Level, Frame) binds the actual Il2Cpp.Level alias");
            Checks.Check(SignatureTypes(reader, levelPatchDefinition, "Finalizer") == "System.Exception,Frame",
                "PatchWorld_Level.Finalizer(Exception, Frame) returns the original exception");
        }

        var blocksPatch = FindType(reader, "KingdomEnhancedMod", "PatchWorld_Level_GetBlocks");
        Checks.Check(!blocksPatch.IsNil, "artifact contains PatchWorld_Level_GetBlocks");
        CheckHandlers(reader, blocksPatch, new Dictionary<string, int> { { "Postfix", 2 } },
            MethodAttributes.Private, "private static");
        if (!blocksPatch.IsNil)
            Checks.Check(SignatureTypes(reader, reader.GetTypeDefinition(blocksPatch), "Postfix")
                == "Il2Cpp.LevelLayout,Il2CppSystem.Collections.Generic.List`1<Il2Cpp.LevelBlock>&",
                "PatchWorld_Level_GetBlocks.Postfix(LevelLayout, ref List<LevelBlock>) binds the actual Il2Cpp shapes");

        Checks.Check(!FindType(reader, "KingdomEnhancedMod", "MapWidthPlanner").IsNil,
            "artifact contains the shared MapWidthPlanner");
        var mapScope = FindType(reader, "KingdomEnhancedMod", "MapWidthScope");
        Checks.Check(!mapScope.IsNil, "artifact contains the shared MapWidthScope");
        CheckHandlers(reader, mapScope, new Dictionary<string, int>
        {
            { "Open", 3 }, { "Close", 2 }, { "Abort", 1 }, { "TryApply", 2 }
        }, MethodAttributes.Assembly, "internal static");

        var typeRefs = new HashSet<string>();
        foreach (var handle in reader.TypeReferences)
        {
            var reference = reader.GetTypeReference(handle);
            string ns = reader.GetString(reference.Namespace);
            string name = reader.GetString(reference.Name);
            typeRefs.Add(ns.Length == 0 ? name : ns + "." + name);
        }
        foreach (string required in RequiredTypes) Checks.Check(typeRefs.Contains(required), "references type " + required);

        var memberNames = new HashSet<string>();
        foreach (var handle in reader.MemberReferences) memberNames.Add(reader.GetString(reader.GetMemberReference(handle).Name));
        foreach (string required in RequiredMembers) Checks.Check(memberNames.Contains(required), "references member " + required);
        Checks.Check(!typeRefs.Contains("Il2Cpp.SpriteRendererFX") && !typeRefs.Contains("Il2Cpp.BaseSpriteFX"),
            "the Android artifact references no SpriteRendererFX/BaseSpriteFX types (FX seam cut)");
        Checks.Check(!memberNames.Contains("FadeOut"),
            "the Android artifact references no FX FadeOut member");

        var assemblyRefs = new HashSet<string>();
        foreach (var handle in reader.AssemblyReferences) assemblyRefs.Add(reader.GetString(reader.GetAssemblyReference(handle).Name));
        foreach (string required in RequiredAssemblies) Checks.Check(assemblyRefs.Contains(required), "references assembly " + required);

        // Issue #119 hit surface: the two native UI DLLs and the four UGUI construction
        // types must be real references of the artifact, not just compiled-away code.
        Checks.Check(assemblyRefs.Contains("UnityEngine.UI"), "references assembly UnityEngine.UI");
        Checks.Check(assemblyRefs.Contains("UnityEngine.UIModule"), "references assembly UnityEngine.UIModule");
        Checks.Check(typeRefs.Contains("UnityEngine.UI.Image"), "references type UnityEngine.UI.Image");
        Checks.Check(typeRefs.Contains("UnityEngine.UI.GraphicRaycaster"), "references type UnityEngine.UI.GraphicRaycaster");
        Checks.Check(typeRefs.Contains("UnityEngine.Canvas"), "references type UnityEngine.Canvas");
        Checks.Check(typeRefs.Contains("UnityEngine.RectTransform"), "references type UnityEngine.RectTransform");
        Checks.Check(memberNames.Contains("set_raycastTarget") && memberNames.Contains("set_renderMode") && memberNames.Contains("set_sortingOrder"),
            "references the Image/Canvas setters used to build the surface");

        var surface = FindType(reader, "OhMyMods.AndroidProbe", "MobileUiInputSurface");
        Checks.Check(!surface.IsNil, "artifact contains MobileUiInputSurface");
        if (!surface.IsNil)
        {
            var definition = reader.GetTypeDefinition(surface);
            Checks.Check((definition.Attributes & TypeAttributes.Sealed) != 0, "MobileUiInputSurface is sealed");
            Checks.Check(TypeName(reader, definition.BaseType) == "System.Object",
                "MobileUiInputSurface is a plain CLR class, not a MonoBehaviour");
            var methods = new HashSet<string>();
            foreach (var handle in definition.GetMethods()) methods.Add(reader.GetString(reader.GetMethodDefinition(handle).Name));
            Checks.Check(SignatureTypes(reader, definition, "Sync") == "UnityEngine.GameObject,OhMyMods.AndroidProbe.FloatLayout,System.Boolean",
                "MobileUiInputSurface.Sync(GameObject, FloatLayout, bool)");
            Checks.Check(methods.Contains("Hide"), "MobileUiInputSurface.Hide()");
            Checks.Check(methods.Contains("Dispose"), "MobileUiInputSurface.Dispose()");
            Checks.Check(methods.Contains(".ctor"), "MobileUiInputSurface has a parameterless constructor");
            // No new Harmony surface: the ticker drives this class directly.
            bool harmonyAttribute = false;
            foreach (var handle in definition.GetMethods())
            {
                var method = reader.GetMethodDefinition(handle);
                foreach (var attributeHandle in method.GetCustomAttributes())
                    if (AttributeTypeName(reader, reader.GetCustomAttribute(attributeHandle).Constructor).StartsWith("HarmonyLib."))
                        harmonyAttribute = true;
            }
            Checks.Check(!harmonyAttribute, "MobileUiInputSurface carries no Harmony attributes");
        }

        var panel = FindType(reader, "OhMyMods.AndroidProbe", "MobileModPanel");
        Checks.Check(!panel.IsNil, "artifact contains MobileModPanel");
        if (!panel.IsNil)
        {
            var definition = reader.GetTypeDefinition(panel);
            Checks.Check((definition.Attributes & TypeAttributes.Sealed) != 0, "MobileModPanel is sealed");
            Checks.Check(TypeName(reader, definition.BaseType) == "System.Object",
                "MobileModPanel is a plain CLR class, not a MonoBehaviour");
            var methods = new HashSet<string>();
            foreach (var handle in definition.GetMethods()) methods.Add(reader.GetString(reader.GetMethodDefinition(handle).Name));
            Checks.Check(methods.Contains("Draw") && methods.Contains("CancelGesture") && methods.Contains("Dispose")
                && methods.Contains("Toggle") && methods.Contains("Step") && methods.Contains("Info"),
                "MobileModPanel exposes the fixed Root/menu surface");
            Checks.Check(SignatureTypes(reader, definition, "Draw") == "OhMyMods.AndroidProbe.FloatLayout",
                "MobileModPanel.Draw(FloatLayout)");
        }
        var gesture = FindType(reader, "OhMyMods.AndroidProbe", "PanelGesture");
        Checks.Check(!gesture.IsNil, "artifact contains the pure PanelGesture state machine");

        // Issue #144 native-offset regression: the device loader build is missing the
        // GUIStyle::AssignRectOffset_Injected icall, so every whole-RectOffset assignment
        // (set_padding / set_margin / set_overflow) crashed the first panel open at the
        // ModPanelStyles factory. The shared style provider must instead read each style's
        // own RectOffset back through the verified getters and write the four Int32 fields.
        // These checks pin that contract on the built artifact's IL, not on a source mirror;
        // getter/field-setter runtime behavior stays a device-gate question.
        var styles = FindType(reader, "KingdomEnhancedMod", "ModPanelStyles");
        Checks.Check(!styles.IsNil, "artifact contains the shared ModPanelStyles");
        if (!styles.IsNil)
        {
            var styleMembers = BodyReferencedMembers(pe, reader, reader.GetTypeDefinition(styles));
            Checks.Check(!styleMembers.Contains("UnityEngine.GUIStyle::set_padding")
                && !styleMembers.Contains("UnityEngine.GUIStyle::set_margin")
                && !styleMembers.Contains("UnityEngine.GUIStyle::set_overflow")
                && !styleMembers.Contains("UnityEngine.GUIStyle::AssignRectOffset"),
                "ModPanelStyles assigns no whole RectOffset (set_padding/set_margin/set_overflow/AssignRectOffset all absent)");
            Checks.Check(styleMembers.Contains("UnityEngine.GUIStyle::get_padding"),
                "ModPanelStyles reads each style's own padding back through the getter");
            Checks.Check(styleMembers.Contains("UnityEngine.RectOffset::set_left")
                && styleMembers.Contains("UnityEngine.RectOffset::set_right")
                && styleMembers.Contains("UnityEngine.RectOffset::set_top")
                && styleMembers.Contains("UnityEngine.RectOffset::set_bottom"),
                "ModPanelStyles writes each style's own RectOffset through the four field setters");
            Checks.Check(!styleMembers.Contains("UnityEngine.RectOffset::.ctor"),
                "ModPanelStyles constructs no replacement RectOffset (writes the style's own one)");
        }

        // Amendment: every whole-RectOffset assignment anywhere in the artifact funnels into the
        // same missing icall (ImGuiCompat.BuildStyle hit set_border next), so the ban and the
        // getter/field-setter evidence are asserted assembly-wide through the artifact's
        // MemberReference table; no Android-linked source may regress to whole assigns.
        foreach (string banned in new[] { "set_padding", "set_margin", "set_overflow", "set_border", "AssignRectOffset" })
            Checks.Check(!HasMemberReference(reader, "UnityEngine.GUIStyle", banned),
                "artifact references no GUIStyle::" + banned + " (whole-RectOffset assignment path)");
        Checks.Check(HasMemberReference(reader, "UnityEngine.GUIStyle", "get_padding")
            && HasMemberReference(reader, "UnityEngine.GUIStyle", "get_margin")
            && HasMemberReference(reader, "UnityEngine.GUIStyle", "get_overflow")
            && HasMemberReference(reader, "UnityEngine.GUIStyle", "get_border"),
            "artifact reads GUIStyle offsets through the getters (padding/margin/overflow/border)");
        Checks.Check(HasMemberReference(reader, "UnityEngine.RectOffset", "set_left")
            && HasMemberReference(reader, "UnityEngine.RectOffset", "set_right")
            && HasMemberReference(reader, "UnityEngine.RectOffset", "set_top")
            && HasMemberReference(reader, "UnityEngine.RectOffset", "set_bottom"),
            "artifact writes RectOffset fields through the four Int32 setters");

        // Issue #144 native-drawing adaptation: the Android artifact must not touch the stripped
        // GUIStyleState background members or the old CalcSize icall, and must not use the stub
        // drawing / clip-escape paths (GUI|Graphics::DrawTexture, ScaleAroundPivot, Unclip).
        // Solid fills go through GUI.Label + GUI.matrix; measurement goes through the genuine
        // CalcSizeWithConstraints (shared MeasureSize). PC keeps its paths behind #if guards in
        // the shared sources, so these bans apply to the Android-linked artifact only.
        foreach (string bannedState in new[] { "set_background", "get_background" })
            Checks.Check(!HasMemberReference(reader, "UnityEngine.GUIStyleState", bannedState),
                "artifact references no GUIStyleState::" + bannedState + " (stripped background path)");
        Checks.Check(!HasMemberReference(reader, "UnityEngine.GUIStyle", "CalcSize"),
            "artifact references no GUIStyle::CalcSize (stripped old measurement icall)");
        Checks.Check(!HasMemberReference(reader, "UnityEngine.GUI", "DrawTexture")
            && !HasMemberReference(reader, "UnityEngine.Graphics", "DrawTexture"),
            "artifact references no GUI/Graphics::DrawTexture stub");
        Checks.Check(!HasMemberReference(reader, "UnityEngine.GUI", "ScaleAroundPivot")
            && !HasMemberReference(reader, "UnityEngine.GUIUtility", "Unclip"),
            "artifact references no pivot/Unclip clip-escape helpers");
        Checks.Check(HasMemberReference(reader, "UnityEngine.GUIStyle", "CalcSizeWithConstraints"),
            "the shared MeasureSize really calls CalcSizeWithConstraints");
        Checks.Check(HasMemberReference(reader, "UnityEngine.GUI", "Label")
            && HasMemberReference(reader, "UnityEngine.GUI", "set_matrix") && HasMemberReference(reader, "UnityEngine.GUI", "get_matrix"),
            "primitive solid fills go through GUI.Label + GUI.matrix");

        var imgui = FindType(reader, "KingdomEnhancedMod", "ImGuiCompat");
        Checks.Check(!imgui.IsNil, "artifact contains the shared ImGuiCompat");
        if (!imgui.IsNil)
        {
            var imguiMethods = new HashSet<string>();
            foreach (var handle in reader.GetTypeDefinition(imgui).GetMethods())
                imguiMethods.Add(reader.GetString(reader.GetMethodDefinition(handle).Name));
            Checks.Check(imguiMethods.Contains("DrawSolidTexture") && imguiMethods.Contains("IntersectRect"),
                "ImGuiCompat carries the single solid-fill backend and the shared rect intersect");
        }
        var drawingControls = FindType(reader, "KingdomEnhancedMod", "ModPanelControls");
        if (!drawingControls.IsNil)
            Checks.Check(SignatureTypes(reader, reader.GetTypeDefinition(drawingControls), "DrawRow")
                .EndsWith(",UnityEngine.Vector2,UnityEngine.Rect", StringComparison.Ordinal),
                "DrawRow receives the caller's group origin and local clip (producer-side clipping)");
        var mobilePanel = FindType(reader, "OhMyMods.AndroidProbe", "MobileModPanel");
        if (!mobilePanel.IsNil)
        {
            bool staleIntersect = false;
            foreach (var handle in reader.GetTypeDefinition(mobilePanel).GetMethods())
                if (reader.GetString(reader.GetMethodDefinition(handle).Name) == "Intersect") staleIntersect = true;
            Checks.Check(!staleIntersect,
                "MobileModPanel no longer carries a private Intersect copy (hit and visual share ImGuiCompat.IntersectRect)");
        }

        // Correction round (Reviewer's V4 Box observation): on Android the value/tab text styles
        // must derive from skin.label — clones of skin.box/skin.button keep the native black
        // decoration background even without background setters — and the PC-only track/thumb
        // palette textures must not be allocated on Android. Compiled proof: no GUISkin::get_button
        // reference and exactly three Texture() allocation call sites in ModPanelStyles; source
        // proof: the conditional base selection keeps the PC box/button branches byte-side by side.
        var stylesDef = reader.GetTypeDefinition(styles);
        Checks.Check(!HasMemberReference(reader, "UnityEngine.GUISkin", "get_button"),
            "Android value/tab no longer clone skin.button (native black decoration base is gone)");
        Checks.Check(CountBodyCalls(pe, reader, stylesDef, "Texture") == 3,
            "ModPanelStyles allocates only the three Android palette textures (track/thumb are PC-only)");
        string stylesSource = File.ReadAllText(Path.Combine(SourceRoot,
            "il2cpp", "ModPanelStyles.cs"));
        Checks.Check(stylesSource.Contains("Style(skin.label, ValueSize, Gold)") && stylesSource.Contains("Style(skin.box, ValueSize, Gold)")
            && stylesSource.Contains("Style(skin.label, TabSize, Muted)") && stylesSource.Contains("Style(skin.button, TabSize, Muted)"),
            "the value/tab base selection keeps ANDROID label and PC box/button branches side by side");

        VerifyFrozenSources();
        Console.WriteLine("artifact sha256 " + Checks.ComputeSha256(dllPath));
    }

    // SHA-256 freeze for the Android adapter sources and the linked production sources. Every
    // listed source must stay byte-identical; this issue mechanically re-pinned every hash it
    // changed, removed the legacy intentionallyChanged skip set, and added the new shared UI
    // inputs (ModPanelStyles / ModPanelControls / ImGuiCompat), the common panel container and
    // the pure gesture helper as strict pins. Issue #146 re-pinned the five files it changed
    // (Probe / MobilePlayerConfig / MobileVegetationMenu / AndroidProbe.csproj /
    // AdapterTests.csproj) and added the shared dense source PatchWorld_OptionalVegetation.cs.
    private static readonly string SourceRoot = Checks.RepositoryRoot();

    private static readonly string[] FrozenSources =
    {
        "android/AndroidCoroutine.cs",
        "android/AssemblyInfo.cs",
        "android/CalendarSnapshot.cs",
        "android/FloatInput.cs",
        "android/FloatLayout.cs",
        "android/GlobalAliases.cs",
        "android/HoldBridges.cs",
        "android/MobileCalendar.cs",
        "android/MobileGenerationMenu.cs",
        "android/MobileModPanel.cs",
        "android/MobilePlayerConfig.cs",
        "android/MobilePlayerMenu.cs",
        "android/MobilePopulation.cs",
        "android/MobileUiInputSurface.cs",
        "android/MobileVegetationMenu.cs",
        "android/MobileWorldMenu.cs",
        "android/OhMyMods.AndroidProbe.csproj",
        "android/OptionalQoLScope.cs",
        "android/PanelGesture.cs",
        "android/PatchDivine_StaffCooldown.cs",
        "android/PatchRide_InfiniteStamina.cs",
        "android/PatchRide_SteedCooldown.cs",
        "android/PatchWorld_BoatCapacity.cs",
        "android/PatchWorld_Mover.cs",
        "android/PopulationCounts.cs",
        "android/Probe.cs",
        "android/TouchClaims.cs",
        "android/tests/AdapterTests.csproj",
        "android/tests/MelonLoggerStub.cs",
        "android/tests/MelonPreferencesStub.cs",
        "il2cpp/BoatCapacityProfile.cs",
        "il2cpp/FarmCatMovement.cs",
        "il2cpp/GreekScaleScope.cs",
        "il2cpp/ImGuiCompat.cs",
        "il2cpp/MapWidthPlanner.cs",
        "il2cpp/NightDepartureTiming.cs",
        "il2cpp/PatchWorld_NightDeparture.cs",
        "il2cpp/MapWidthTerrain.cs",
        "il2cpp/ModPanelControls.cs",
        "il2cpp/ModPanelStyles.cs",
        "il2cpp/PatchWorld_Construction.cs",
        "il2cpp/PatchWorld_DeerPopulation.cs",
        "il2cpp/PatchWorld_FarmCats.cs",
        "il2cpp/PatchWorld_FastForestRecede.cs",
        "il2cpp/PatchWorld_Level.cs",
        "il2cpp/PatchWorld_OptionalVegetation.cs",
        "il2cpp/PatchRoles_BeggarCamp.cs",
        "il2cpp/PatchPerformance_Population.cs",
        "il2cpp/PopulationGrounding.cs",
        "android/tests/population-android/PopulationTests.csproj",
        "android/tests/population-android/Stubs.cs",
        "android/tests/population-android/Program.cs",
    };

    private static void VerifyFrozenSources()
    {
        var known = new Dictionary<string, string>
        {
            { "android/AndroidCoroutine.cs", "c3f16218e82cc26f9f8e5b9ed9e74aa1f81b666da821f0acccf35133fd9ae265" },
            { "android/AssemblyInfo.cs", "b5a7ade914d9e157d175ae9f7d42f40bb92cb608722875de4cd025ad50e86a14" },
            { "android/CalendarSnapshot.cs", "0ece4765f9a5311707f1a9bf0d61c491846cd8e17327ac4d09773c37eb0fdb8e" },
            { "android/FloatInput.cs", "de1dfaf27d5d32334d9dc4a69b2f28523789f963698b23e3396a328e9d08d8e0" },
            { "android/FloatLayout.cs", "9fd919964256f18e11aebfad414a7b1a10dab41591ac286564e1b3c05bfbd7c6" },
            { "android/GlobalAliases.cs", "92f7a6997223c2874aae8a596d69f0f52cfac1da8f01b0bdce47c29c32e417d1" },
            { "android/HoldBridges.cs", "982ad14e89afc8337a9b30b51dd0ccdddbeee9b7e3a1c21acf326fdec457c916" },
            { "android/MobileCalendar.cs", "49e8fcbdc29a19b5100c5c6525a1317cccb446252bcbe91a8306699afcee718a" },
            { "android/MobileGenerationMenu.cs", "e89f7d9224b31f5ea77ca5392a02d8c7ac1a723c2dd0ee702d4225eb8f53f5d0" },
            { "android/MobileModPanel.cs", "e9a0bd5347da0333b2110b4a93dcf61b0caff4d4375a600a99ad12a124683f12" },
            { "android/MobilePlayerConfig.cs", "c82d4656d57109bfe29539165b4690e730fb25bd2e5c212dc1de2cf30f86c7ac" },
            { "android/MobilePlayerMenu.cs", "849ef81c029258d0a0ddbff4aef1684d9998434f4f85a01525d60236a714e139" },
            { "android/MobilePopulation.cs", "fdf95ab309d2489f1d0d3e4c4893d3ee44388e9e5ebb37d991c65a6f8c260571" },
            { "android/MobileUiInputSurface.cs", "62d8e15aaab385853bfab63e36d382906c9ebdc17179520e52191526bb25fad7" },
            { "android/MobileVegetationMenu.cs", "40e6be0dda5313a61f01f98e1829422290612ede0b948b8d21c4549a58890e66" },
            { "android/MobileWorldMenu.cs", "35749b5a0aaa300cb03c439790252f04bacdef91fc38188a25e260af29884fee" },
            { "android/OhMyMods.AndroidProbe.csproj", "432481f6c02cc891da4627e88e67c17badeb3e0f6cad4eca9421b56dff0cde33" },
            { "android/OptionalQoLScope.cs", "6c1d02d0f92ba9a202ea26a5a0d05c0ea07c5d3cb7a64c05af8fa2f19b4e27ac" },
            { "android/PanelGesture.cs", "51071fefbc1a1ab4b8f23ee84bf10925c325d8414400b9ed186af238c136e97b" },
            { "android/PatchDivine_StaffCooldown.cs", "fed3aa92423d4cada13071dde8e04f8a4a2b49c6379220a0bf216b908aa74cda" },
            { "android/PatchRide_InfiniteStamina.cs", "02a27e7c21d865596e23db3cdfe405dafb8c9e95f303e45481b24a5f72b280fc" },
            { "android/PatchRide_SteedCooldown.cs", "b29cfe6d93af38e0d49e33dc4254c501a7b5ee8c664ecf473ec499cadc01eb52" },
            { "android/PatchWorld_BoatCapacity.cs", "3addaeaf8a4e1672bf1727e56e5b38efd242894c2bb1310937e0d923a435cc57" },
            { "android/PatchWorld_Mover.cs", "278414cb21ba169e1ca3e32aaca610e7dfd1a5d1ba5695ad7ab3b8d53f62788e" },
            { "android/PopulationCounts.cs", "bc7342133e52ef79ae79e15969bc358a61b9fccc0e84386c8cff3cf5563ec1c4" },
            { "android/Probe.cs", "e1660aaa94c93b24ee09ae24f0472fbc0b269e460cbea9c22e65e5a0b680eb66" },
            { "android/TouchClaims.cs", "ff41eb50d02792ff8da8d62ed4f4a7c18a3d7ce3eb07d4ce79859c93f5fb140a" },
            { "android/tests/AdapterTests.csproj", "788d688ab72d580711a9ec42ec6a540dfa41102af307a65d64965748e36859e1" },
            { "android/tests/MelonLoggerStub.cs", "816742fe131ed5a8d4ac906788c7ced7e8ef47e59ca340cab2de983dcff65a49" },
            { "android/tests/MelonPreferencesStub.cs", "43cb64d622d4b1827aff0229902a732d209b188400f628d088fde099337e38d2" },
            { "il2cpp/BoatCapacityProfile.cs", "02d43c64922667e6e1f2d834bf29f28f8fbc683878dc27db1430abed4c11c2f2" },
            { "il2cpp/FarmCatMovement.cs", "02e37276ae1fd5ac697ef6b71c4cf5bd79f642981a4f3958667dde525be2a2a4" },
            { "il2cpp/GreekScaleScope.cs", "13d913b12e89338645847bb0f4d04fd4370bd808035e377766e2c5f9da530221" },
            { "il2cpp/ImGuiCompat.cs", "a69b83eb5c5219b7be84dadc37c7f13f17eafe3f69fa973235d9bb044f53af87" },
            { "il2cpp/MapWidthPlanner.cs", "3790b85fef8f36c822cec3d845a1ff90fb0a2dbcfaa295169966cc2ad036aa20" },
            { "il2cpp/MapWidthTerrain.cs", "5fe7ccde3fcdf5ff99e169e17b13b8558b37e3f03d7d7294ada8649d6afa194b" },
            { "il2cpp/ModPanelControls.cs", "0f6ac598c2e61d19cef0797de17f7e73b4c080113a6185da9623c498b83fec5b" },
            { "il2cpp/ModPanelStyles.cs", "c4de8ec78a4c35a57a0afde3ba80db41e80a1cbff63b27afcd825fd0795a79c5" },
            { "il2cpp/PatchWorld_Construction.cs", "5ac44db39daf30e1e8ae4a5c73005ab212571e49424cee72215ce665c576ad79" },
            { "il2cpp/PatchWorld_DeerPopulation.cs", "60ddae20a9dc96cadb769d607eed313bd3cd6c1eb8a6239bbf4165e96452aed2" },
            { "il2cpp/PatchWorld_FarmCats.cs", "d73a8b40ad60901bf1505731fae2baae867fc9c0b0f9f2fafffdd06868e30d2f" },
            { "il2cpp/PatchWorld_FastForestRecede.cs", "1b51c0aa24daec749d23647f82aecb789e1b36b7439497eff2bd93dbb40d3193" },
            { "il2cpp/PatchWorld_Level.cs", "90f9f724db0d0f9353775f4284ed09d6d1782c125c5ad028e83b843b55e1315e" },
            { "il2cpp/PatchRoles_BeggarCamp.cs", "0f6ef6e7c15d66e02ff34696fed991ba0864688f0a084377fd459cbc3c8981a9" },
            { "il2cpp/PatchPerformance_Population.cs", "a88d780cdfecfdcd2038cf524b318e74b2d83e6fb48dbaafc1dfdc3b49c39951" },
            { "il2cpp/PopulationGrounding.cs", "f343ded6e3bb6bde78fe6c277d3e797e32333b2c2fa8e4af4791606a7e12ca20" },
            { "android/tests/population-android/PopulationTests.csproj", "d3589f48f6b1a790b796b80740164011fec38e9327a65698097d695852efbcf6" },
            { "android/tests/population-android/Stubs.cs", "4acd6fd0dd282c1c2bb51bad36dbda1d53e343cfedd203941efce39923c75cf7" },
            { "android/tests/population-android/Program.cs", "99b2f857fbdd6aed635def5db2a601d87b5ec7ed3377c68e67a710a02412a667" },
            { "il2cpp/PatchWorld_OptionalVegetation.cs", "0d8d9e892e91b4439f026d211e3a26edb86b14861999438b1148e35cfb8918c0" },
            { "il2cpp/NightDepartureTiming.cs", "da410c8de68f2933dc3ae62bdc7cad0199b51af73102db9e36e250313a83f50d" },
            { "il2cpp/PatchWorld_NightDeparture.cs", "fdde1a68bc5a255beefd9f4ac8eb3849a5b807ddece7e0b4ce120bbb738c3a62" },
        };
        foreach (string relative in FrozenSources)
        {
            string path = Path.Combine(SourceRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) { Checks.Check(false, "frozen source present: " + relative); continue; }
            string hash = Checks.ComputeSha256(path);
            if (!known.TryGetValue(relative, out string expected))
            {
                Checks.Check(false, "frozen source " + relative + " has no recorded hash");
                continue;
            }
            Checks.Check(hash == expected, "frozen source unchanged: " + relative);
        }
    }

    // Walks the raw method-signature blob by ECMA-335 element codes. BlobReader's
    // ReadSignatureTypeCode maps TypeHandle tokens inconsistently with the raw element
    // byte (observed 0x01 reported as Void on this runtime), so the element walk is done
    // explicitly; only the well-formed shapes this artifact uses are decoded.
    private static string SignatureTypes(MetadataReader reader, TypeDefinition definition, string methodName)
    {
        foreach (var handle in definition.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            if (reader.GetString(method.Name) != methodName) continue;
            var blob = reader.GetBlobReader(method.Signature);
            blob.ReadByte(); // calling convention
            int count = blob.ReadCompressedInteger();
            WalkElementType(reader, ref blob); // return type
            var names = new List<string>();
            for (int index = 0; index < count; index++) names.Add(WalkElementType(reader, ref blob));
            return string.Join(",", names);
        }
        return "";
    }

    private static string WalkElementType(MetadataReader reader, ref BlobReader blob)
    {
        byte element = blob.ReadByte();
        if (element == 0x01) return "System.Void";
        if (element == 0x02) return "System.Boolean";
        if (element == 0x08) return "System.Int32";
        if (element == 0x0C) return "System.Single";
        if (element == 0x10) return WalkElementType(reader, ref blob) + "&";
        if (element == 0x11 || element == 0x12) return BlobTypeName(reader, blob.ReadTypeHandle());
        if (element == 0x15)
        {
            byte genericKind = blob.ReadByte(); // 0x11 VALUETYPE | 0x12 CLASS of the generic type definition
            if (genericKind != 0x11 && genericKind != 0x12) return "genericinst-invalid";
            string genericType = BlobTypeName(reader, blob.ReadTypeHandle());
            int argumentCount = blob.ReadCompressedInteger();
            var arguments = new List<string>();
            for (int index = 0; index < argumentCount; index++) arguments.Add(WalkElementType(reader, ref blob));
            return genericType + "<" + string.Join(",", arguments) + ">";
        }
        return "element-0x" + element.ToString("x2", CultureInfo.InvariantCulture);
    }

    private static string BlobTypeName(MetadataReader reader, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
            {
                var reference = reader.GetTypeReference((TypeReferenceHandle)handle);
                string ns = reader.GetString(reference.Namespace);
                string name = reader.GetString(reference.Name);
                return ns.Length == 0 ? name : ns + "." + name;
            }
            case HandleKind.TypeDefinition:
            {
                var definition = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                string ns = reader.GetString(definition.Namespace);
                string name = reader.GetString(definition.Name);
                return ns.Length == 0 ? name : ns + "." + name;
            }
            default:
                return "";
        }
    }

    // Reads a literal int field's constant from the artifact metadata (the shared profile's
    // four policy constants), returning null when the field is missing or not an Int32 literal.
    private static int? ConstantValue(MetadataReader reader, TypeDefinitionHandle type, string fieldName)
    {
        foreach (var handle in reader.GetTypeDefinition(type).GetFields())
        {
            var field = reader.GetFieldDefinition(handle);
            if (reader.GetString(field.Name) != fieldName) continue;
            if ((field.Attributes & FieldAttributes.Literal) == 0 || (field.Attributes & FieldAttributes.HasDefault) == 0) return null;
            var constant = reader.GetConstant(field.GetDefaultValue());
            if (constant.TypeCode != ConstantTypeCode.Int32) return null;
            return reader.GetBlobReader(constant.Value).ReadInt32();
        }
        return null;
    }

    private static bool HasConstructorReference(MetadataReader reader, string typeName)
        => HasMemberReference(reader, typeName, ".ctor");

    private static bool HasMemberReference(MetadataReader reader, string typeName, string memberName)
    {
        foreach (var handle in reader.MemberReferences)
        {
            var reference = reader.GetMemberReference(handle);
            if (reader.GetString(reference.Name) == memberName && TypeName(reader, reference.Parent) == typeName) return true;
        }
        return false;
    }

    // Matches a class-level [HarmonyPatch(typeof(T), nameof(T.M))] by scanning the decoded
    // custom-attribute blob for the type and method names (the Type argument is serialized as
    // an assembly-qualified name, so both short names appear verbatim).
    private static bool HasClassHarmonyPatchTarget(MetadataReader reader, TypeDefinition definition, string typeName, string methodName)
    {
        foreach (var handle in definition.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (!AttributeTypeName(reader, attribute.Constructor).StartsWith("HarmonyLib.HarmonyPatch", StringComparison.Ordinal)) continue;
            var blob = reader.GetBlobReader(attribute.Value);
            string text = System.Text.Encoding.UTF8.GetString(blob.ReadBytes(blob.RemainingBytes));
            if (text.Contains(typeName, StringComparison.Ordinal) && text.Contains(methodName, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    // Same-assembly calls compile to MethodDefinition tokens (not MemberReferences), so this
    // scans the method body for a call/callvirt whose 4-byte operand is the callee's metadata
    // token. Positive-only evidence for one known-shaped call in this artifact.
    private static bool BodyCallsMethod(PEReader pe, MetadataReader reader, TypeDefinition definition,
        string methodName, string calleeName, string calleeType)
    {
        int token = 0;
        foreach (var handle in reader.MethodDefinitions)
        {
            var candidate = reader.GetMethodDefinition(handle);
            if (reader.GetString(candidate.Name) != calleeName) continue;
            if (TypeName(reader, candidate.GetDeclaringType()) != calleeType) continue;
            token = MetadataTokens.GetToken(handle);
            break;
        }
        if (token == 0) return false;
        foreach (var handle in definition.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            if (reader.GetString(method.Name) != methodName) continue;
            if (method.RelativeVirtualAddress == 0) continue;
            byte[] il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
            for (int index = 0; index + 4 < il.Length; index++)
            {
                if (il[index] != 0x28 && il[index] != 0x6F) continue; // call / callvirt
                int operand = il[index + 1] | (il[index + 2] << 8) | (il[index + 3] << 16) | (il[index + 4] << 24);
                if (operand == token) return true;
            }
        }
        return false;
    }

    // Collects every call/callvirt/newobj MemberReference target in one type's method bodies as
    // "DeclaringType::member". Same naive byte-walk scan model as BodyCallsMethod: exact
    // positive evidence, and any hit on a banned token is decisive for the negative checks.
    private static HashSet<string> BodyReferencedMembers(PEReader pe, MetadataReader reader, TypeDefinition definition)
    {
        var members = new HashSet<string>();
        foreach (var handle in definition.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0) continue;
            byte[] il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
            for (int index = 0; index + 4 < il.Length; index++)
            {
                if (il[index] != 0x28 && il[index] != 0x6F && il[index] != 0x73) continue; // call / callvirt / newobj
                int operand = il[index + 1] | (il[index + 2] << 8) | (il[index + 3] << 16) | (il[index + 4] << 24);
                if ((operand & 0xFF000000) != 0x0A000000) continue; // MemberReference tokens only
                var reference = reader.GetMemberReference((MemberReferenceHandle)MetadataTokens.EntityHandle(operand));
                members.Add(TypeName(reader, reference.Parent) + "::" + reader.GetString(reference.Name));
            }
        }
        return members;
    }

    // Counts call/callvirt sites of one same-assembly method (by name, within the given type) in a
    // type's method bodies, using the same naive byte-walk scan model as BodyCallsMethod. Used for
    // exact call-count evidence (the palette texture allocation guard), not general decoding.
    private static int CountBodyCalls(PEReader pe, MetadataReader reader, TypeDefinition definition, string calleeName)
    {
        int token = 0;
        foreach (var handle in reader.MethodDefinitions)
        {
            var candidate = reader.GetMethodDefinition(handle);
            if (reader.GetString(candidate.Name) != calleeName) continue;
            if (TypeName(reader, candidate.GetDeclaringType()) != "KingdomEnhancedMod.ModPanelStyles") continue;
            token = MetadataTokens.GetToken(handle);
            break;
        }
        if (token == 0) return -1;
        int count = 0;
        foreach (var handle in definition.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0) continue;
            byte[] il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
            for (int index = 0; index + 4 < il.Length; index++)
            {
                if (il[index] != 0x28 && il[index] != 0x6F) continue; // call / callvirt
                int operand = il[index + 1] | (il[index + 2] << 8) | (il[index + 3] << 16) | (il[index + 4] << 24);
                if (operand == token) count++;
            }
        }
        return count;
    }

    private static void CheckHandlers(MetadataReader reader, TypeDefinitionHandle type, Dictionary<string, int> required, MethodAttributes expectedAccess, string accessLabel)
    {
        if (type.IsNil) return;
        var found = new HashSet<string>();
        foreach (var handle in reader.GetTypeDefinition(type).GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            string name = reader.GetString(method.Name);
            if (!required.TryGetValue(name, out int expectedParameters)) continue;
            found.Add(name);
            bool isStatic = (method.Attributes & MethodAttributes.Static) != 0;
            bool accessOk = (method.Attributes & MethodAttributes.MemberAccessMask) == expectedAccess;
            int parameters = ParameterCount(reader, method);
            Checks.Check(isStatic && accessOk && parameters == expectedParameters,
                "handler " + name + " is " + accessLabel + " with " + parameters + " parameters");
        }
        foreach (string name in required.Keys)
            if (!found.Contains(name)) Checks.Check(false, "handler " + name + " is present");
    }

    private static int ParameterCount(MetadataReader reader, MethodDefinition method)
    {
        var blob = reader.GetBlobReader(method.Signature);
        var header = blob.ReadSignatureHeader();
        if (header.IsGeneric) blob.ReadCompressedInteger();
        return blob.ReadCompressedInteger();
    }

    private static TypeDefinitionHandle FindType(MetadataReader reader, string ns, string name)
    {
        foreach (var handle in reader.TypeDefinitions)
        {
            var definition = reader.GetTypeDefinition(handle);
            if (reader.GetString(definition.Namespace) == ns && reader.GetString(definition.Name) == name) return handle;
        }
        return default;
    }

    private static string AttributeTypeName(MetadataReader reader, EntityHandle constructor)
    {
        switch (constructor.Kind)
        {
            case HandleKind.MemberReference:
                return TypeName(reader, reader.GetMemberReference((MemberReferenceHandle)constructor).Parent);
            case HandleKind.MethodDefinition:
                return TypeName(reader, reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType());
            default:
                return "";
        }
    }

    private static string TypeName(MetadataReader reader, EntityHandle type)
    {
        switch (type.Kind)
        {
            case HandleKind.TypeReference:
            {
                var reference = reader.GetTypeReference((TypeReferenceHandle)type);
                string ns = reader.GetString(reference.Namespace);
                string name = reader.GetString(reference.Name);
                return ns.Length == 0 ? name : ns + "." + name;
            }
            case HandleKind.TypeDefinition:
            {
                var definition = reader.GetTypeDefinition((TypeDefinitionHandle)type);
                string ns = reader.GetString(definition.Namespace);
                string name = reader.GetString(definition.Name);
                return ns.Length == 0 ? name : ns + "." + name;
            }
            case HandleKind.TypeSpecification:
                return "TypeSpecification";
            default:
                return "";
        }
    }
}
