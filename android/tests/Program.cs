using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using OhMyMods.AndroidProbe;
using Config = KingdomEnhancedMod.ModConfig;
using EnemyPatch = KingdomEnhancedMod.PatchWorld_EnemyManager;

namespace OhMyMods.AndroidProbe.Tests;

internal static class Checks
{
    internal static int Passed;
    internal static int Failed;

    internal static void Check(bool condition, string what)
    {
        if (condition) { Passed++; return; }
        Failed++;
        Console.WriteLine("FAIL " + what);
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
        bool oldCfg = false;
        var artifactArgs = new List<string>();
        foreach (string arg in args)
        {
            if (TryFlag(arg, "--seed-speed=", out string speedText))
                seededSpeed = int.Parse(speedText, CultureInfo.InvariantCulture);
            else if (TryFlag(arg, "--seed-enemy-count=", out string countText))
                seededEnemyCount = ParseSeedFloat(countText);
            else if (TryFlag(arg, "--seed-enemy-timeline=", out string timelineText))
                seededEnemyTimeline = ParseSeedFloat(timelineText);
            else if (arg == "--oldcfg")
                oldCfg = true;
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
        if (oldCfg)
        {
            if (!seededSpeed.HasValue) MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "SpeedMultiplier", 3);
            MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "InfiniteSteedStamina", true);
            MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "HoldPurchaseEnabled", true);
            MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "CalendarEnabled", true);
            Console.WriteLine("mode: pre-seeded cfg with only the four original keys");
        }
        LayoutChecks();
        ConfigChecks(seededSpeed, seededEnemyCount, seededEnemyTimeline, oldCfg);
        EnemyMathChecks.Run();
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
        Checks.Check(Math.Abs(layout.PanelHeight - 484f) < 1e-4f, "home panel height is 484 (five rows)");
        layout.Expanded = true;
        Checks.Check(layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 478f), "touch filter covers the 484-high home panel bottom row");
        layout.PlayerPage = true;
        Checks.Check(Math.Abs(layout.PanelHeight - 406f) < 1e-4f, "player page panel height is 406");
        Checks.Check(layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 400f), "touch filter covers the 406-high player panel");
        layout.PlayerPage = false;
        layout.WorldPage = true;
        Checks.Check(Math.Abs(layout.PanelHeight - 484f) < 1e-4f, "world page panel height is 484 (five rows)");
        Checks.Check(layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 478f), "touch filter covers the 484-high world panel bottom row");
        layout.WorldPage = false;
        layout.PopulationPage = true;
        Checks.Check(Math.Abs(layout.PanelHeight - 376f) < 1e-4f, "population panel height stays 376");
        Checks.Check(!layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 400f), "population panel keeps its 376 touch boundary");
        layout.PopulationPage = false;
        Checks.Check(Math.Abs(layout.PanelHeight - 484f) < 1e-4f, "closing the subpages restores the 484-high home panel");
    }

    private static void ConfigChecks(int? seededSpeed, float? seededEnemyCount, float? seededEnemyTimeline, bool oldCfg)
    {
        var applied = new List<bool>();
        int expectedWarnings = (seededEnemyCount.HasValue && !IsFinite(seededEnemyCount.Value) ? 1 : 0)
            + (seededEnemyTimeline.HasValue && !IsFinite(seededEnemyTimeline.Value) ? 1 : 0);
        bool rejected = false;
        try { Config.Initialize(null); }
        catch (ArgumentNullException) { rejected = true; }
        Checks.Check(rejected, "Initialize(null) fails with ArgumentNullException");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Count == 0,
            "the rejected null seam neither creates entries nor poisons Initialize");

        Config.Initialize(enabled => applied.Add(enabled));

        Checks.Check(Config.Enabled.Value, "session master switch defaults ON and is not persisted");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Count == 8, "Initialize creates exactly eight entries (no persisted master switch)");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/SpeedMultiplier default=1"), "SpeedMultiplier is declared with default 1 in the OhMyMods.Android category");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/InfiniteSteedStamina default=False"), "InfiniteSteedStamina is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/HoldPurchaseEnabled default=False"), "HoldPurchaseEnabled is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/CalendarEnabled default=False"), "CalendarEnabled is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/EnemyCountMultiplier default=1"), "EnemyCountMultiplier is declared with default 1");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/EnemyTimelineSpeed default=1"), "EnemyTimelineSpeed is declared with default 1");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/InfiniteMoney default=False"), "InfiniteMoney is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/FarmCatsEnabled default=False"), "FarmCatsEnabled is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == 0, "Initialize never writes the cfg");
        Checks.Check(applied.Count == 1 && !applied[0], "cold start applies InfiniteMoney=false through the seam exactly once");
        Checks.Check(MelonLoader.MelonLogger.WarningCalls == expectedWarnings, "the load boundary warns exactly once per non-finite seeded multiplier");

        float expectedEnemyCount = seededEnemyCount.HasValue ? ExpectedMultiplier(seededEnemyCount.Value) : 1f;
        float expectedEnemyTimeline = seededEnemyTimeline.HasValue ? ExpectedMultiplier(seededEnemyTimeline.Value) : 1f;

        bool seeded = seededSpeed.HasValue || seededEnemyCount.HasValue || seededEnemyTimeline.HasValue || oldCfg;
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
            Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == 0, "the load-boundary clamp/fallback does not write the cfg");
            Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_SETTINGS_READY category=OhMyMods.Android speed=" + expectedSpeed
                + " stamina=" + oldCfg + " hold=" + oldCfg + " calendar=" + oldCfg
                + " enemyCount=" + expectedEnemyCount + " growth=" + expectedEnemyTimeline + " money=False cats=False",
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
            return;
        }

        Checks.Check(Config.SpeedMultiplier.Value == 1, "speed multiplier defaults to 1x");
        Checks.Check(Config.EnemyCountMultiplier.Value == 1f && Config.EnemyTimelineSpeed.Value == 1f, "enemy multipliers default to 1x");
        Checks.Check(!Config.InfiniteSteedStamina.Value
            && !Config.HoldPurchaseEnabled.Value
            && !Config.CalendarEnabled.Value
            && !Config.InfiniteMoney.Value
            && !Config.FarmCatsEnabled.Value, "qol switches, InfiniteMoney and FarmCats default OFF");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_SETTINGS_READY category=OhMyMods.Android speed=1 stamina=False hold=False calendar=False enemyCount=1 growth=1 money=False cats=False",
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

        Checks.Check(applied.Count == 3, "the InfiniteMoney seam fires only at Initialize and the two toggles (no subscription or per-frame write)");
        Checks.Check(saves == 22, "twenty-two switch actions produced twenty-two saves");
    }

    private static float ExpectedMultiplier(float seeded)
        => IsFinite(seeded) ? Math.Clamp(seeded, 1f, 5f) : 1f;

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
        "Il2Cpp.BiomeHolder", "Il2Cpp.BiomeData", "Il2Cpp.Character", "Il2Cpp.Mover",
        "Il2Cpp.Droppable", "Il2Cpp.Kingdom", "Il2Cpp.StateMachine", "Il2Cpp.Side",
        "Il2Cpp.Embarkee", "Il2Cpp.IslandSaveData", "Il2Cpp.Game", "Il2Cpp.Farmland", "Il2Cpp.GAPS",
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
        "SpawnOrInstantiate", "DespawnOrDestroy", "get_catPrefab", "GetBorderSideIntact"
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
    }

    private static bool HasConstructorReference(MetadataReader reader, string typeName)
    {
        foreach (var handle in reader.MemberReferences)
        {
            var reference = reader.GetMemberReference(handle);
            if (reader.GetString(reference.Name) == ".ctor" && TypeName(reader, reference.Parent) == typeName) return true;
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
