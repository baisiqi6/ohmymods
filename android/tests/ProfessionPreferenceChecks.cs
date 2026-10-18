using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MelonLoader;
using Config = KingdomEnhancedMod.ModConfig;

namespace OhMyMods.AndroidProbe.Tests;

// Loader-host contract tests: seeds enter the existing stub before the actual
// MobilePlayerConfig.Initialize runs once. No production initialization is copied.
internal static class ProfessionPreferenceChecks
{
    private const string Category = "OhMyMods.Android";
    private static bool switchesSeeded;
    private static bool legacy31Seeded;
    private static float? ratioSeed;
    private static int? volleySeed;
    private static float? rateSeed;
    internal static int NonFiniteSeeds => rateSeed.HasValue && !float.IsFinite(rateSeed.Value) ? 1 : 0;

    private static readonly string[] Switches =
    {
        "MusketeerEnabled", "HeavyShieldEnabled", "HeroArcherEnabled",
        "ArcherScatterEnabled", "ArcherRateEnabled", "ArcherImpactEnabled"
    };

    // Fixed compatibility contract of the old public cfg, not an Initialize clone.
    // Types/defaults are checked against the actual entries and generic field types.
    private static readonly (string Name, object Default)[] Legacy =
    {
        ("SpeedMultiplier", 1), ("InfiniteSteedStamina", false), ("HoldPurchaseEnabled", false),
        ("PetGuardEnabled", false), ("CrownStealerSlowdownEnabled", false), ("KnightRecruitRangeEnabled", false),
        ("RoleScaleEnabled", false), ("WallEngineerEnabled", false), ("WorldLayoutEnabled", false),
        ("TowerSpotMultiplier", 2f), ("WallSpotMultiplier", 1f), ("TowerLadderCleanup", false),
        ("WallSpotDiagnostics", false), ("DivineParametersEnabled", false), ("CalendarEnabled", false),
        ("EnemyCountMultiplier", 1f), ("EnemyTimelineSpeed", 1f), ("SteedCooldownMultiplier", 1f),
        ("StaffCooldownMultiplier", 1f), ("InfiniteMoney", false), ("FarmCatsEnabled", false),
        ("FastBuild", false), ("BoatCapacityEnabled", false), ("MapSizeMultiplier", 1f),
        ("FastForestRecedeEnabled", false), ("DeerPopulationEnabled", false), ("DenseThicketsEnabled", false),
        ("NightDepartureEnabled", false), ("PopulationEnabled", false),
        ("BeggarCampCapacity", 4), ("BeggarSpawnIntervalSeconds", 120)
    };

    internal static bool Seed(string argument)
    {
        if (argument == "--seed-profession-switches")
        {
            switchesSeeded = true;
            foreach (string key in Switches) MelonPreferencesStub.Seed(Category, key, true);
            return true;
        }
        if (argument == "--seed-legacy31")
        {
            legacy31Seeded = true;
            foreach (var entry in Legacy) MelonPreferencesStub.Seed(Category, entry.Name, entry.Default);
            return true;
        }
        if (argument.StartsWith("--seed-crossbow-ratio=", StringComparison.Ordinal))
        {
            ratioSeed = float.Parse(argument.Substring("--seed-crossbow-ratio=".Length), CultureInfo.InvariantCulture);
            MelonPreferencesStub.Seed(Category, "CrossbowRecruitmentRatio", ratioSeed.Value);
            return true;
        }
        if (argument.StartsWith("--seed-archer-volley=", StringComparison.Ordinal))
        {
            volleySeed = int.Parse(argument.Substring("--seed-archer-volley=".Length), CultureInfo.InvariantCulture);
            MelonPreferencesStub.Seed(Category, "ArcherVolleyCount", volleySeed.Value);
            return true;
        }
        if (argument.StartsWith("--seed-archer-rate=", StringComparison.Ordinal))
        {
            rateSeed = float.Parse(argument.Substring("--seed-archer-rate=".Length), CultureInfo.InvariantCulture);
            MelonPreferencesStub.Seed(Category, "ArcherRateMultiplier", rateSeed.Value);
            return true;
        }
        return false;
    }

    internal static void AfterInitialize(bool oldCfg)
    {
        var fields = typeof(Config).GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition() == typeof(MelonPreferences_Entry<>))
            .ToDictionary(field => field.Name);
        Checks.Check(fields.Count == 58, "fifty-eight real loader entries, no persisted session Enabled");
        Checks.Check(MelonPreferencesStub.CreatedEntries.Count == 58
            && MelonPreferencesStub.CreatedEntries.Select(entry => entry.Split(' ')[0]).Distinct().Count() == 58,
            "Initialize creates each real entry exactly once in the existing category");
        Checks.Check(!MelonPreferencesStub.CreatedEntries.Any(entry => entry.StartsWith(Category + "/Enabled ", StringComparison.Ordinal)),
            "session master has no persisted preference");
        Checks.Check(Legacy.Length == 31 && Legacy.Select(entry => entry.Name).Distinct().Count() == 31,
            "compatibility fixture identifies all thirty-one original keys");
        foreach (var entry in Legacy)
        {
            Checks.Check(fields.TryGetValue(entry.Name, out var field)
                && field.FieldType.GetGenericArguments()[0] == entry.Default.GetType(),
                "legacy key retains its actual loader entry type: " + entry.Name);
            Checks.Check(MelonPreferencesStub.CreatedEntries.Contains(Category + "/" + entry.Name + " default=" + entry.Default),
                "legacy key retains its declared default: " + entry.Name);
            if (legacy31Seeded)
            {
                object expected = entry.Default;
                if (oldCfg && entry.Name == "SpeedMultiplier") expected = 3;
                if (oldCfg && (entry.Name == "InfiniteSteedStamina" || entry.Name == "HoldPurchaseEnabled"
                    || entry.Name == "CalendarEnabled")) expected = true;
                Checks.Check(Equals(Value(fields[entry.Name]), expected),
                    "full old31 cfg migrates without changing loaded value: " + entry.Name);
            }
        }
        foreach (string key in Switches)
        {
            Checks.Check(MelonPreferencesStub.CreatedEntries.Contains(Category + "/" + key + " default=False")
                && fields[key].FieldType.GetGenericArguments()[0] == typeof(bool),
                "profession/archer switch is a real default-OFF bool entry: " + key);
            Checks.Check(Equals(Value(fields[key]), switchesSeeded),
                "profession/archer choice loads the stored value or missing-key OFF: " + key);
        }
        Checks.Check(MelonPreferencesStub.CreatedEntries.Contains(Category + "/CrossbowRecruitmentRatio default=0.25")
            && fields["CrossbowRecruitmentRatio"].FieldType.GetGenericArguments()[0] == typeof(float),
            "crossbow ratio is a real float entry with PC default one quarter");
        float ratioExpected = .25f;
        if (ratioSeed == .5f) ratioExpected = .5f;
        if (ratioSeed == .75f) ratioExpected = .75f;
        if (ratioSeed == 1f) ratioExpected = 1f;
        Checks.Check(Config.CrossbowRecruitmentRatio.Value == ratioExpected,
            "actual config load accepts the four ratio choices and normalizes unsupported/nonfinite input to .25");
        Checks.Check(MelonPreferencesStub.CreatedEntries.Contains(Category + "/ArcherVolleyCount default=3")
            && fields["ArcherVolleyCount"].FieldType.GetGenericArguments()[0] == typeof(int),
            "volley is a real int entry default three");
        Checks.Check(Config.ArcherVolleyCount.Value == Math.Clamp(volleySeed ?? 3, 1, 3),
            "actual config load bounds volley count to one through three");
        float rateExpected = rateSeed ?? 1.5f;
        if (!float.IsFinite(rateExpected)) rateExpected = 1f;
        else rateExpected = Math.Clamp(rateExpected, 1f, 2f);
        Checks.Check(MelonPreferencesStub.CreatedEntries.Contains(Category + "/ArcherRateMultiplier default=1.5")
            && fields["ArcherRateMultiplier"].FieldType.GetGenericArguments()[0] == typeof(float),
            "rate multiplier is a real float entry default one and a half");
        Checks.Check(Config.ArcherRateMultiplier.Value == rateExpected,
            "actual config load bounds finite rate 1..2 and uses existing fallback one for NaN/Infinity");
        if (NonFiniteSeeds != 0)
            Checks.Check(MelonLogger.LastWarning == "ANDROID_SETTINGS_WARNING entry=ArcherRateMultiplier non-finite value fallback=1",
                "nonfinite rate uses the existing named warning, not a new sanitizer");
        Checks.Check(MelonPreferencesStub.SaveCalls == 0, "new preferences and migration never save during Initialize");
        var before = fields.ToDictionary(pair => pair.Key, pair => Value(pair.Value));
        int extraCallbacks = 0;
        Config.Initialize(_ => extraCallbacks++);
        Checks.Check(extraCallbacks == 0 && MelonPreferencesStub.CreatedEntries.Count == 58
            && MelonPreferencesStub.SaveCalls == 0, "repeat Initialize neither recreates entries, saves, nor replaces/applies the original callback");
        foreach (var entry in fields)
            Checks.Check(Equals(before[entry.Key], Value(entry.Value)), "repeat Initialize preserves loaded entry " + entry.Key);
    }

    private static object Value(FieldInfo field)
    {
        object entry = field.GetValue(null);
        return field.FieldType.GetField("Value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(entry);
    }
}
