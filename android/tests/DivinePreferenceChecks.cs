using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using Config = KingdomEnhancedMod.ModConfig;

namespace OhMyMods.AndroidProbe.Tests;

internal static class DivinePreferenceChecks
{
    private static bool seeded;
    internal static bool Seed(string argument)
    {
        if (argument != "--seed-divine-parameters") return false;
        seeded = true;
        MelonPreferencesStub.Seed("OhMyMods.Android", "DivineParametersEnabled", true);
        return true;
    }

    internal static void Run()
    {
        Checks.Check(MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/DivineParametersEnabled default=False"),
            "divine parameters use a real default-OFF loader preference");
        Checks.Check(Config.DivineParametersEnabled.Value == seeded, "divine choice loads the stored value");
        var old = new Dictionary<FieldInfo, object>();
        foreach (var field in typeof(Config).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
        {
            if (!field.FieldType.Name.StartsWith("MelonPreferences_Entry`", StringComparison.Ordinal)
                || field.Name == "DivineParametersEnabled") continue;
            old.Add(field, field.FieldType.GetField("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .GetValue(field.GetValue(null)));
        }
        Checks.Check(old.Count == 39, "divine toggle retains the other thirty-nine real preferences");
        int saves = MelonPreferencesStub.SaveCalls;
        Config.ToggleDivineParameters();
        Checks.Check(Config.DivineParametersEnabled.Value == !seeded && MelonPreferencesStub.SaveCalls == saves + 1,
            "divine first toggle saves exactly once");
        Config.ToggleDivineParameters();
        Checks.Check(Config.DivineParametersEnabled.Value == seeded && MelonPreferencesStub.SaveCalls == saves + 2,
            "divine second toggle restores the stored choice and saves once");
        foreach (var entry in old)
            Checks.Check(object.Equals(entry.Value, entry.Key.FieldType.GetField("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .GetValue(entry.Key.GetValue(null))), "divine toggles preserve " + entry.Key.Name);
    }
}
