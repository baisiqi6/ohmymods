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
        Checks.Check(Math.Abs(layout.PanelHeight - 562f) < 1e-4f, "home panel height is 562 (generation row at 410 + Close at 488)");
        layout.Expanded = true;
        Checks.Check(layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 552f), "touch filter covers the 562-high home panel Close row bottom (ends at 552)");
        Checks.Check(!layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 630f), "the home panel keeps its 562 touch boundary (seven-row 630 boundary free)");
        layout.GenerationPage = true;
        Checks.Check(Math.Abs(layout.PanelHeight - 250f) < 1e-4f, "generation page panel height is 250 (Length 98 + Back 176)");
        Checks.Check(layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 130f), "touch filter covers the generation Length row (98..162)");
        Checks.Check(layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 208f), "touch filter covers the generation Back row (176..240)");
        Checks.Check(!layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 260f), "generation panel keeps its 250 touch boundary");
        layout.GenerationPage = false;
        Checks.Check(Math.Abs(layout.PanelHeight - 562f) < 1e-4f, "Back out of the generation page restores the 562-high home panel");
        layout.PlayerPage = true;
        Checks.Check(Math.Abs(layout.PanelHeight - 562f) < 1e-4f, "player page panel height is 562 (six rows: staff row at 410, Back at 488)");
        Checks.Check(layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 552f), "touch filter covers the 562-high player panel Back row bottom (ends at 552)");
        Checks.Check(!layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 630f), "the player panel keeps its 562 touch boundary (seven-row 630 boundary free)");
        layout.PlayerPage = false;
        layout.WorldPage = true;
        Checks.Check(Math.Abs(layout.PanelHeight - 640f) < 1e-4f, "world page panel height is 640 (seven rows)");
        Checks.Check(layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 630f), "touch filter covers the 640-high world panel bottom row (Back ends at 630)");
        layout.VegetationPage = true;
        Checks.Check(Math.Abs(layout.PanelHeight - 328f) < 1e-4f, "vegetation page height is 328 while the world page stays open (cats 98 / forest 176 / Back 254)");
        Checks.Check(layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 130f), "touch filter covers the vegetation cats row (98..162)");
        Checks.Check(layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 208f), "touch filter covers the vegetation fast-forest row (176..240)");
        Checks.Check(layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 286f), "touch filter covers the vegetation Back row (254..318)");
        Checks.Check(!layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 340f), "vegetation panel keeps its 328 touch boundary");
        layout.VegetationPage = false;
        Checks.Check(Math.Abs(layout.PanelHeight - 640f) < 1e-4f, "Back out of the vegetation page restores the still-open world page (640)");
        layout.WorldPage = false;
        Checks.Check(Math.Abs(layout.PanelHeight - 562f) < 1e-4f, "Back out of the world page restores the 562-high home panel");
        layout.PopulationPage = true;
        Checks.Check(Math.Abs(layout.PanelHeight - 376f) < 1e-4f, "population panel height stays 376");
        Checks.Check(!layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 400f), "population panel keeps its 376 touch boundary");
        layout.PopulationPage = false;
        Checks.Check(Math.Abs(layout.PanelHeight - 562f) < 1e-4f, "closing the subpages restores the 562-high home panel");
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
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Count == 14, "Initialize creates exactly fourteen entries (no persisted master switch)");
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
            Checks.Check(Config.MapSizeMultiplier.Value == expectedMap,
                "MapSizeMultiplier is " + Describe(expectedMap) + "x after the load boundary (1..5 clamp, non-finite fallback 1)");
            Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == 0, "the load-boundary clamp/fallback does not write the cfg");
            Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_SETTINGS_READY category=OhMyMods.Android speed=" + expectedSpeed
                + " stamina=" + oldCfg + " hold=" + oldCfg + " calendar=" + oldCfg
                + " enemyCount=" + expectedEnemyCount + " growth=" + expectedEnemyTimeline + " money=False cats=False fastBuild=" + expectedFastBuild
                + " cooldown=" + expectedCooldown + " boat=" + expectedBoat + " map=" + expectedMap
                + " staff=" + expectedStaffCooldown + " forestRecede=" + expectedForest,
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
            && !Config.FastForestRecedeEnabled.Value, "qol switches, InfiniteMoney, FarmCats, FastBuild, BoatCapacity and FastForestRecede default OFF");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_SETTINGS_READY category=OhMyMods.Android speed=1 stamina=False hold=False calendar=False enemyCount=1 growth=1 money=False cats=False fastBuild=False cooldown=1 boat=False map=1 staff=1 forestRecede=False",
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

        Checks.Check(applied.Count == 3, "the InfiniteMoney seam fires only at Initialize and the two toggles (no subscription or per-frame write)");
        Checks.Check(saves == 43, "forty-three switch actions produced forty-three saves");
    }

    private static float ExpectedMultiplier(float seeded)
        => IsFinite(seeded) ? Math.Clamp(seeded, 1f, 5f) : 1f;

    private static float ExpectedCooldown(float seeded)
        => IsFinite(seeded) ? Math.Clamp(seeded, 0.2f, 1f) : 1f;

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
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
        Checks.Check(source.Contains("\"0.0.18\""), "Probe reports version 0.0.18");
        Checks.Check(source.Contains("staff_base_cooldown"), "Probe feature marker advertises staff_base_cooldown");
        Checks.Check(source.Contains("fast_forest_recede"), "Probe feature marker advertises fast_forest_recede");
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
        Checks.Check(source.Contains("Layout.VegetationPage") && source.Contains("MobileVegetationMenu.Draw"),
            "Probe routes the Vegetation page to MobileVegetationMenu.Draw");
    }
}

// Source-level route contract for the vegetation page: the World row 332 becomes the Vegetation
// entry without closing the World page, the vegetation page reuses the unchanged cats toggle
// copy and arms the shared forest switch, and Back clears only VegetationPage. These are
// worker-owned menu sources, frozen by hash in VerifyFrozenSources.
internal static class MenuChecks
{
    internal static void Run()
    {
        string worldPath = Path.Combine(Checks.RepositoryRoot(), "android", "MobileWorldMenu.cs");
        string vegetationPath = Path.Combine(Checks.RepositoryRoot(), "android", "MobileVegetationMenu.cs");
        Checks.Check(File.Exists(worldPath) && File.Exists(vegetationPath), "vegetation menu sources present");
        if (!File.Exists(worldPath) || !File.Exists(vegetationPath)) return;
        string world = File.ReadAllText(worldPath);
        string vegetation = File.ReadAllText(vegetationPath);
        Checks.Check(world.Contains("py + 332 * scale") && world.Contains("\"Vegetation\"")
            && world.Contains("Layout.VegetationPage = true"), "the World cats row (332) became the Vegetation entry");
        Checks.Check(!world.Contains("ToggleFarmCats") && !world.Contains("Stock cats"),
            "the World page no longer toggles or labels cats directly");
        Checks.Check(vegetation.Contains("py + 98 * scale") && vegetation.Contains("ModConfig.ToggleFarmCats()")
            && vegetation.Contains("Stock cats: ON\\nnext level; cats stay") && vegetation.Contains("Stock cats: OFF\\nnext level; cats stay"),
            "the vegetation cats row (98) reuses the original toggle copy unchanged");
        Checks.Check(vegetation.Contains("py + 176 * scale") && vegetation.Contains("ModConfig.ToggleFastForestRecede()")
            && vegetation.Contains("Fast forest recede: "), "the vegetation fast-forest row (176) arms the shared switch");
        Checks.Check(vegetation.Contains("py + 254 * scale") && vegetation.Contains("\"Back\"")
            && vegetation.Contains("Layout.VegetationPage = false"), "Back (254) clears only VegetationPage");
        Checks.Check(!vegetation.Contains("WorldPage = false") && !vegetation.Contains("WorldPage=false"),
            "the vegetation Back never closes the World page");
        Checks.Check(!vegetation.Contains("FarmCatsEnabled.Value =") && !vegetation.Contains("RemoveCat") && !vegetation.Contains("ClearCat"),
            "the vegetation page never enables, disables or clears cats outside the shared toggle");
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
        "Il2Cpp.ForestItem", "Il2Cpp.Forest",
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
        "get_controlsForestSize", "get_removedByForest", "get_removeDelay", "get__forest"
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

        VerifyFrozenSources();
        Console.WriteLine("artifact sha256 " + Checks.ComputeSha256(dllPath));
    }

    // SHA-256 freeze for the Android adapter sources and the linked production sources. Files
    // listed in intentionallyChanged are the only ones the current issue may edit; every other
    // frozen source must stay byte-identical (the current issue only moves the shared
    // fast-forest source into its own file, wires the vegetation page and the fourteenth entry).
    private static readonly string SourceRoot = Checks.RepositoryRoot();

    private static readonly string[] FrozenSources =
    {
        "android/AssemblyInfo.cs",
        "android/AndroidCoroutine.cs",
        "android/CalendarSnapshot.cs",
        "android/FloatInput.cs",
        "android/FloatLayout.cs",
        "android/GlobalAliases.cs",
        "android/HoldBridges.cs",
        "android/MobileCalendar.cs",
        "android/MobileGenerationMenu.cs",
        "android/MobilePlayerConfig.cs",
        "android/MobilePlayerMenu.cs",
        "android/MobilePopulation.cs",
        "android/MobileUiInputSurface.cs",
        "android/MobileVegetationMenu.cs",
        "android/MobileWorldMenu.cs",
        "android/OhMyMods.AndroidProbe.csproj",
        "android/OptionalQoLScope.cs",
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
        "il2cpp/MapWidthPlanner.cs",
        "il2cpp/MapWidthTerrain.cs",
        "il2cpp/PatchWorld_FastForestRecede.cs",
        "il2cpp/PatchWorld_FarmCats.cs",
        "il2cpp/PatchWorld_Construction.cs",
        "il2cpp/PatchWorld_Level.cs"
    };

    private static void VerifyFrozenSources()
    {
        var known = new Dictionary<string, string>
        {
            { "android/AndroidCoroutine.cs", "c3f16218e82cc26f9f8e5b9ed9e74aa1f81b666da821f0acccf35133fd9ae265" },
            { "android/AssemblyInfo.cs", "b5a7ade914d9e157d175ae9f7d42f40bb92cb608722875de4cd025ad50e86a14" },
            { "android/CalendarSnapshot.cs", "0ece4765f9a5311707f1a9bf0d61c491846cd8e17327ac4d09773c37eb0fdb8e" },
            { "android/FloatInput.cs", "17bccf9800a36c2cb1ffd0ee6e9f112f343951b0eae64acd7475afe93f6344a8" },
            { "android/GlobalAliases.cs", "17c0300c95a2abbfb6ca621c5f36ee6e2449593898e44aab9322b460cb47d230" },
            { "android/HoldBridges.cs", "a9aafb9b4c55b6cf1600f21b99506b3ef1a1304aa722a3d87fd7cff8b8ee5b0e" },
            { "android/MobileCalendar.cs", "f0d174125f0e3771a906712f52398e422f7812862b8614b3ecc709a385271633" },
            { "android/MobileGenerationMenu.cs", "f226e36b04aa2feaa239c2758f3c47858d526017edee062560035d178632c00b" },
            { "android/MobilePlayerMenu.cs", "28524aaa69d061e2e618643e975ef8a8e19ab15666e1fe2be70bcdd92300fb33" },
            { "android/MobilePopulation.cs", "6dd431df541a4d2fd268eccfc3e9c757e5837cb8535acbd102bd05b6b1ed3f33" },
            { "android/MobileUiInputSurface.cs", "62d8e15aaab385853bfab63e36d382906c9ebdc17179520e52191526bb25fad7" },
            { "android/MobileVegetationMenu.cs", "43af167093c13ae61167a6c474ebfa38892ea3eea5102aac0fe0115ba79298ba" },
            { "android/OptionalQoLScope.cs", "6c1d02d0f92ba9a202ea26a5a0d05c0ea07c5d3cb7a64c05af8fa2f19b4e27ac" },
            { "android/PatchDivine_StaffCooldown.cs", "fed3aa92423d4cada13071dde8e04f8a4a2b49c6379220a0bf216b908aa74cda" },
            { "android/PatchRide_InfiniteStamina.cs", "02a27e7c21d865596e23db3cdfe405dafb8c9e95f303e45481b24a5f72b280fc" },
            { "android/PatchRide_SteedCooldown.cs", "b29cfe6d93af38e0d49e33dc4254c501a7b5ee8c664ecf473ec499cadc01eb52" },
            { "android/PatchWorld_BoatCapacity.cs", "3addaeaf8a4e1672bf1727e56e5b38efd242894c2bb1310937e0d923a435cc57" },
            { "android/PatchWorld_Mover.cs", "278414cb21ba169e1ca3e32aaca610e7dfd1a5d1ba5695ad7ab3b8d53f62788e" },
            { "android/PopulationCounts.cs", "bc7342133e52ef79ae79e15969bc358a61b9fccc0e84386c8cff3cf5563ec1c4" },
            { "android/Probe.cs", "7c4bbc441e2d7f15db1aa83fcccd11d90e14b58b7bc44e48fc84481bc5469b5d" },
            { "android/TouchClaims.cs", "ff41eb50d02792ff8da8d62ed4f4a7c18a3d7ce3eb07d4ce79859c93f5fb140a" },
            { "android/tests/AdapterTests.csproj", "aad4aba93718df35016419550c9dbee3e93407afae0b61c9ee3b77a8f67d841f" },
            { "android/tests/MelonLoggerStub.cs", "816742fe131ed5a8d4ac906788c7ced7e8ef47e59ca340cab2de983dcff65a49" },
            { "android/tests/MelonPreferencesStub.cs", "43cb64d622d4b1827aff0229902a732d209b188400f628d088fde099337e38d2" },
            { "il2cpp/BoatCapacityProfile.cs", "02d43c64922667e6e1f2d834bf29f28f8fbc683878dc27db1430abed4c11c2f2" },
            { "il2cpp/FarmCatMovement.cs", "02e37276ae1fd5ac697ef6b71c4cf5bd79f642981a4f3958667dde525be2a2a4" },
            { "il2cpp/GreekScaleScope.cs", "13d913b12e89338645847bb0f4d04fd4370bd808035e377766e2c5f9da530221" },
            { "il2cpp/MapWidthPlanner.cs", "3790b85fef8f36c822cec3d845a1ff90fb0a2dbcfaa295169966cc2ad036aa20" },
            { "il2cpp/MapWidthTerrain.cs", "5fe7ccde3fcdf5ff99e169e17b13b8558b37e3f03d7d7294ada8649d6afa194b" },
            { "il2cpp/PatchWorld_FastForestRecede.cs", "1b51c0aa24daec749d23647f82aecb789e1b36b7439497eff2bd93dbb40d3193" },
            { "il2cpp/PatchWorld_FarmCats.cs", "d73a8b40ad60901bf1505731fae2baae867fc9c0b0f9f2fafffdd06868e30d2f" },
            { "il2cpp/PatchWorld_Construction.cs", "5ac44db39daf30e1e8ae4a5c73005ab212571e49424cee72215ce665c576ad79" },
            { "il2cpp/PatchWorld_Level.cs", "90f9f724db0d0f9353775f4284ed09d6d1782c125c5ad028e83b843b55e1315e" }
        };
        // Issue #138 touches exactly these frozen sources: the FloatLayout vegetation height and
        // touch geometry, the fourteenth config entry + forest toggle, the World cats row that
        // became the Vegetation entry, and the new Compile Include. The Operator-owned Probe
        // entry (version 0.0.18, the forest registration, the Vegetation router) and the new
        // shared PatchWorld_FastForestRecede.cs / MobileVegetationMenu.cs are frozen with their
        // final bytes; the rest of the #122/#127/#130/#134/#136 surface stays ordinary frozen
        // entries.
        var intentionallyChanged = new HashSet<string>
        {
            "android/FloatLayout.cs", "android/MobilePlayerConfig.cs",
            "android/MobileWorldMenu.cs", "android/OhMyMods.AndroidProbe.csproj"
        };
        foreach (string relative in FrozenSources)
        {
            string path = Path.Combine(SourceRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) { Checks.Check(false, "frozen source present: " + relative); continue; }
            string hash = Checks.ComputeSha256(path);
            if (intentionallyChanged.Contains(relative)) continue;
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
