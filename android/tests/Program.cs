using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using OhMyMods.AndroidProbe;

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
        Console.WriteLine("AdapterTests: FloatLayout / ModConfig-MelonPreferences / artifact metadata");
        int? seededSpeed = null;
        var artifactArgs = new List<string>();
        foreach (string arg in args)
        {
            const string seedFlag = "--seed-speed=";
            if (arg.StartsWith(seedFlag, StringComparison.Ordinal))
                seededSpeed = int.Parse(arg.Substring(seedFlag.Length));
            else
                artifactArgs.Add(arg);
        }
        if (seededSpeed.HasValue)
        {
            MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "SpeedMultiplier", seededSpeed.Value);
            Console.WriteLine("mode: pre-seeded cfg speed multiplier " + seededSpeed.Value);
        }
        LayoutChecks();
        ConfigChecks(seededSpeed);
        if (artifactArgs.Count < 1)
            Checks.Check(false, "artifact path argument missing (pass the built OhMyMods.AndroidProbe.dll path)");
        else if (!File.Exists(artifactArgs[0]))
            Checks.Check(false, "artifact not found: " + artifactArgs[0]);
        else
            ArtifactChecks.Run(Path.GetFullPath(artifactArgs[0]));
        Console.WriteLine("adapter tests: " + Checks.Passed + " passed / " + Checks.Failed + " failed");
        return Checks.Failed == 0 ? 0 : 1;
    }

    private static void LayoutChecks()
    {
        var layout = new FloatLayout();
        layout.Resize(1280f, 720f);
        Checks.Check(Math.Abs(layout.Scale - 1f) < 1e-4f, "scale is 1.0 at 1280x720");
        Checks.Check(Math.Abs(layout.PanelHeight - 406f) < 1e-4f, "home panel height is 406");
        layout.PlayerPage = true;
        Checks.Check(Math.Abs(layout.PanelHeight - 406f) < 1e-4f, "player page panel height is 406");
        layout.Expanded = true;
        Checks.Check(layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 400f), "touch filter covers the 406-high player panel");
        layout.PlayerPage = false;
        Checks.Check(layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 400f), "touch filter covers the 406-high home panel");
        layout.PopulationPage = true;
        Checks.Check(Math.Abs(layout.PanelHeight - 376f) < 1e-4f, "population panel height stays 376");
        Checks.Check(!layout.HitPanel(layout.PanelX + 1f, layout.PanelY + 400f), "population panel keeps its 376 touch boundary");
    }

    private static void ConfigChecks(int? seededSpeed)
    {
        KingdomEnhancedMod.ModConfig.Initialize();

        Checks.Check(KingdomEnhancedMod.ModConfig.Enabled.Value, "session master switch defaults ON and is not persisted");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Count == 4, "Initialize creates exactly four entries (no persisted master switch)");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/SpeedMultiplier default=1"), "SpeedMultiplier is declared with default 1 in the OhMyMods.Android category");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/InfiniteSteedStamina default=False"), "InfiniteSteedStamina is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/HoldPurchaseEnabled default=False"), "HoldPurchaseEnabled is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/CalendarEnabled default=False"), "CalendarEnabled is declared OFF");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == 0, "Initialize never writes the cfg");

        if (seededSpeed.HasValue)
        {
            int expected = Math.Clamp(seededSpeed.Value, 1, 5);
            Checks.Check(KingdomEnhancedMod.ModConfig.SpeedMultiplier.Value == expected,
                "a stored speed of " + seededSpeed.Value + " is clamped to " + expected + " at the load boundary");
            Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == 0, "the load-boundary clamp does not write the cfg");
            Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_SETTINGS_READY category=OhMyMods.Android speed=" + expected + " stamina=False hold=False calendar=False",
                "cold-start ready line reports the effective values");
            return;
        }

        Checks.Check(KingdomEnhancedMod.ModConfig.SpeedMultiplier.Value == 1, "speed multiplier defaults to 1x");
        Checks.Check(!KingdomEnhancedMod.ModConfig.InfiniteSteedStamina.Value
            && !KingdomEnhancedMod.ModConfig.HoldPurchaseEnabled.Value
            && !KingdomEnhancedMod.ModConfig.CalendarEnabled.Value, "qol switches default OFF");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_SETTINGS_READY category=OhMyMods.Android speed=1 stamina=False hold=False calendar=False",
            "cold-start ready line reports the effective values");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == 0, "reading defaults does not write the cfg");

        KingdomEnhancedMod.ModConfig.ToggleHold();
        Checks.Check(KingdomEnhancedMod.ModConfig.HoldPurchaseEnabled.Value, "ToggleHold turns it ON");
        Checks.Check(MelonLoader.MelonLogger.LastMessage == "ANDROID_PLAYER_HOLD_PURCHASE enabled=True", "ToggleHold logs the switch state");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == 1, "ToggleHold saves exactly once");
        KingdomEnhancedMod.ModConfig.ToggleHold();
        Checks.Check(KingdomEnhancedMod.ModConfig.HoldPurchaseEnabled.Value == false, "ToggleHold turns it OFF again");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == 2, "every toggle saves exactly once");

        KingdomEnhancedMod.ModConfig.ToggleStamina();
        Checks.Check(KingdomEnhancedMod.ModConfig.InfiniteSteedStamina.Value, "ToggleStamina turns it ON");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == 3, "ToggleStamina saves exactly once");

        int saves = 3;
        for (int next = 2; next <= 5; next++)
        {
            KingdomEnhancedMod.ModConfig.CycleSpeed();
            saves++;
            Checks.Check(KingdomEnhancedMod.ModConfig.SpeedMultiplier.Value == next, "CycleSpeed advances to " + next + "x");
            Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == saves, "CycleSpeed to " + next + "x saves exactly once");
        }
        KingdomEnhancedMod.ModConfig.CycleSpeed();
        saves++;
        Checks.Check(KingdomEnhancedMod.ModConfig.SpeedMultiplier.Value == 1, "CycleSpeed wraps back to 1x");
        Checks.Check(MelonLoader.MelonPreferencesStub.SaveCalls == saves, "CycleSpeed wrap saves exactly once");
        Checks.Check(saves == 8, "eight switch actions produced eight saves");
    }
}

internal static class ArtifactChecks
{
    // Forge (ShopForge tag), Ammo (PayableWorkshopBarrel / PayableComponent._owner / FireTower)
    // and Mead (Baker) bindings must actually land in the built artifact; mere compilation of
    // dead code would not prove the linked Hold source kept them.
    private static readonly string[] RequiredTypes =
    {
        "Il2Cpp.Player", "Il2Cpp.Payable", "Il2Cpp.PayableComponent", "Il2Cpp.PayableWorkshopBarrel",
        "Il2Cpp.FireTower", "Il2Cpp.Baker", "Il2Cpp.CurrencyType", "Il2Cpp.Managers",
        "Il2Cpp.World", "Il2Cpp.NetworkBigBoss"
    };

    private static readonly string[] RequiredMembers =
    {
        "get__payState", "get_actionState", "get_TunnelInput", "get_hasLocalAuthority",
        "get_timeBetweenCoins", "set_timeBetweenCoins", "get_selectedPayable", "get_coins",
        "get_Price", "get_priceIncrease", "get_Currency", "get_interactingPlayer",
        "get_playerPayDistance", "get_forceBlockPayment", "get_HasWorldAuth", "get_world",
        "get_gameLayer", "get__owner", "CanPay", "CanSelect", "PlayerPayPoint", "CompareTag",
        "TryCast", "get_Pointer"
    };

    private static readonly string[] RequiredAssemblies =
    {
        "Assembly-CSharp", "MelonLoader", "0Harmony", "Il2CppInterop.Runtime"
    };

    // Explicit registration contract: name -> declared parameter count.
    private static readonly Dictionary<string, int> RequiredHandlers = new Dictionary<string, int>
    {
        { "UpdatePayState_Prefix", 4 },
        { "UpdatePayState_Postfix", 3 },
        { "UpdatePayState_Finalizer", 3 },
        { "PerformPay_Postfix", 1 }
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
        if (!hold.IsNil)
        {
            var found = new Dictionary<string, bool>();
            foreach (var handle in reader.GetTypeDefinition(hold).GetMethods())
            {
                var method = reader.GetMethodDefinition(handle);
                string name = reader.GetString(method.Name);
                if (!RequiredHandlers.ContainsKey(name)) continue;
                bool isStatic = (method.Attributes & MethodAttributes.Static) != 0;
                bool isPrivate = (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private;
                int parameters = ParameterCount(reader, method);
                bool ok = isStatic && isPrivate && parameters == RequiredHandlers[name];
                found[name] = ok;
                Checks.Check(ok, "handler " + name + " is private static with " + parameters + " parameters");
            }
            foreach (string name in RequiredHandlers.Keys)
                if (!found.ContainsKey(name)) Checks.Check(false, "handler " + name + " is present");
        }

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
            default:
                return "";
        }
    }
}
