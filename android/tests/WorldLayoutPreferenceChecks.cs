using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using MelonLoader;
using Config = KingdomEnhancedMod.ModConfig;

namespace OhMyMods.AndroidProbe.Tests;

internal static class WorldLayoutPreferenceChecks
{
    private static bool layoutSeed;
    private static float? towerSeed;
    private static float? wallSeed;
    internal static int NonFiniteSeeds => (towerSeed.HasValue && !float.IsFinite(towerSeed.Value) ? 1 : 0)
        + (wallSeed.HasValue && !float.IsFinite(wallSeed.Value) ? 1 : 0);

    internal static bool Seed(string arg)
    {
        if (arg == "--seed-world-layout")
        {
            layoutSeed = true;
            MelonPreferencesStub.Seed("OhMyMods.Android", "WorldLayoutEnabled", true);
            return true;
        }
        if (arg.StartsWith("--seed-tower-spots=", StringComparison.Ordinal))
        {
            towerSeed = float.Parse(arg.Substring("--seed-tower-spots=".Length), CultureInfo.InvariantCulture);
            MelonPreferencesStub.Seed("OhMyMods.Android", "TowerSpotMultiplier", towerSeed.Value);
            return true;
        }
        if (arg.StartsWith("--seed-wall-spots=", StringComparison.Ordinal))
        {
            wallSeed = float.Parse(arg.Substring("--seed-wall-spots=".Length), CultureInfo.InvariantCulture);
            MelonPreferencesStub.Seed("OhMyMods.Android", "WallSpotMultiplier", wallSeed.Value);
            return true;
        }
        return false;
    }

    internal static void Run()
    {
        Checks.Check(MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/WorldLayoutEnabled default=False"),
            "old cfg receives a real default-OFF layout hook choice");
        Checks.Check(MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/TowerSpotMultiplier default=2")
            && MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/WallSpotMultiplier default=1"),
            "layout multipliers use the PC defaults");
        Checks.Check(MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/TowerLadderCleanup default=False")
            && MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/WallSpotDiagnostics default=False"),
            "maintenance and bounded diagnostics are real preferences");
        Checks.Check(Config.WorldLayoutEnabled.Value == layoutSeed, "layout choice loads from cfg once");
        Checks.Check(Config.TowerSpotMultiplier.Value == Expected(towerSeed, 2f)
            && Config.WallSpotMultiplier.Value == Expected(wallSeed, 1f),
            "loaded layout multipliers preserve valid fractions and clamp invalid inputs");
        Checks.Check(!Config.TowerLadderCleanup.Value && !Config.WallSpotDiagnostics.Value,
            "missing maintenance and diagnostic keys remain OFF");

        var original = new Dictionary<FieldInfo, object>();
        foreach (var field in typeof(Config).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
        {
            if (!field.FieldType.Name.StartsWith("MelonPreferences_Entry`", StringComparison.Ordinal)
                || field.Name == "WorldLayoutEnabled" || field.Name == "TowerSpotMultiplier"
                || field.Name == "WallSpotMultiplier" || field.Name == "TowerLadderCleanup"
                || field.Name == "WallSpotDiagnostics") continue;
            original.Add(field, field.FieldType.GetField("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(field.GetValue(null)));
        }
        Checks.Check(original.Count == 25, "layout controls retain all twenty-five prior preferences");
        int saves = MelonPreferencesStub.SaveCalls;
        Config.ToggleWorldLayout();
        Checks.Check(Config.WorldLayoutEnabled.Value == !layoutSeed && MelonPreferencesStub.SaveCalls == ++saves,
            "layout selection changes and saves once");
        Config.ToggleWorldLayout();
        Checks.Check(Config.WorldLayoutEnabled.Value == layoutSeed && MelonPreferencesStub.SaveCalls == ++saves,
            "layout selection restores and saves once");

        float tower = Config.TowerSpotMultiplier.Value;
        float wall = Config.WallSpotMultiplier.Value;
        foreach (var sample in new[] { (1f, 2f), (2f, 3f), (3f, 4f), (4f, 1f), (2.5f, 3f), (3.9f, 4f) })
        {
            Config.TowerSpotMultiplier.Value = sample.Item1;
            Config.CycleTowerSpotMultiplier();
            Checks.Check(Config.TowerSpotMultiplier.Value == sample.Item2 && MelonPreferencesStub.SaveCalls == ++saves,
                "tower density advances within 1..4 and saves once from " + sample.Item1);
            Config.WallSpotMultiplier.Value = sample.Item1;
            Config.CycleWallSpotMultiplier();
            Checks.Check(Config.WallSpotMultiplier.Value == sample.Item2 && MelonPreferencesStub.SaveCalls == ++saves,
                "wall density advances within 1..4 and saves once from " + sample.Item1);
        }
        Config.TowerSpotMultiplier.Value = tower;
        Config.WallSpotMultiplier.Value = wall;
        foreach (var item in original)
            Checks.Check(Equals(item.Value, item.Key.FieldType.GetField("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(item.Key.GetValue(null))),
                "layout controls preserve old preference " + item.Key.Name);
    }

    private static float Expected(float? input, float fallback)
    {
        if (!input.HasValue) return fallback;
        if (!float.IsFinite(input.Value)) return 1f;
        if (input.Value < 1f) return 1f;
        if (input.Value > 4f) return 4f;
        return input.Value;
    }
}
