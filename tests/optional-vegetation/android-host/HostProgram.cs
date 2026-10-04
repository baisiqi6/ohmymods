using System;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

// Behavior boundaries of the shared il2cpp/PatchWorld_FastForestRecede.cs compiled with ANDROID
// against the real android/OptionalQoLScope.cs and the typed Il2Cpp interop shapes:
//   - default OFF: zero item/native access (throwing accessors) and zero RNG before any gate;
//   - positive delay / 3; delay <= 0 uses removeDelay * Random(0.5,1.5) / 3 with exactly one
//     RNG draw; non-finite precomputes never commit the ref;
//   - controlsForestSize / removedByForest / world / scene / forest-activity gates, parallax
//     items (same scene, not a child) stay eligible;
//   - the native body keeps running: fields untouched and the native else-branch draw remains
//     possible after an invalid precompute (existing source behavior, not repaired here);
//   - a failing access warns once for the process lifetime, the logger failure is contained and
//     the ref keeps the native wait parameter.
internal static class HostChecks
{
    internal static int Passed;
    internal static int Failed;

    internal static void Check(bool condition, string what)
    {
        if (!condition) throw new Exception(what);
    }

    internal static void Eq<T>(T wanted, T got, string what)
    {
        if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(wanted, got))
            throw new Exception(what + ": expected " + wanted + ", got " + got);
    }

    internal static void Near(float wanted, float got, string what)
    {
        if (MathF.Abs(wanted - got) > 0.0001f)
            throw new Exception(what + ": expected " + wanted + ", got " + got);
    }

    internal static void CheckContains(string wanted, string text, string what)
    {
        if (text == null || !text.Contains(wanted, StringComparison.Ordinal))
            throw new Exception(what + ": [" + text + "] does not contain " + wanted);
    }
}

internal static class Program
{
    private static readonly MethodInfo FadePrefix = typeof(ForestItem_FadeAndRemove_OptionalVegetation_Patch)
        .GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

    private static int Main()
    {
        Console.WriteLine("FastRecedeAndroidHost: shared ANDROID-alias source against the real android/OptionalQoLScope");
        Test("disabled: zero item access, zero RNG, no warning", DisabledZeroAccess);
        Test("scope inactive: zero item access, zero RNG", ScopeInactiveZeroAccess);
        Test("positive delay scales to one third", PositiveThird);
        Test("fallback uses exactly one RNG draw on removeDelay", FallbackSingleDraw);
        Test("native fields untouched and native body continues", NativeFieldsAndBody);
        Test("controlsForestSize and removedByForest gate out", ControlsAndRemovedGate);
        Test("world, scene, forest activity and parallax eligibility", EligibilityBoundaries);
        Test("invalid native number keeps ref and lets native draw again", InvalidNumberKeepsRef);
        Test("failure warns once, logger failure contained, ref keeps native wait", FailureWarningOnce);
        Test("wrapper keeps the PC auto-patch surface under ANDROID", WrapperSurface);
        Console.WriteLine("RESULT passed=" + HostChecks.Passed + " failed=" + HostChecks.Failed);
        return HostChecks.Failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- scenarios

    private static World NewWorld()
    {
        var layerObject = new GameObject("GameLayer");
        var world = new World { gameLayer = layerObject.transform };
        Managers.Inst = new Managers { world = world };
        BiomeHolder.Inst = new BiomeHolder { BiomeIndex = 0 };
        ModConfig.Enabled.Value = true;
        ModConfig.FastForestRecedeEnabled.Value = false;
        UnityEngine.Random.Calls = 0;
        UnityEngine.Random.Unit = 0.5f;
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
        return world;
    }

    private static Forest NewForest(World world, bool childOfLayer = true, bool active = true, bool sameScene = true)
    {
        var go = new GameObject("Forest");
        if (sameScene) go.scene.handle = world.gameLayer.gameObject.scene.handle;
        if (childOfLayer) go.transform.SetParent(world.gameLayer);
        go.activeInHierarchy = active;
        return go.AddComponent<Forest>();
    }

    private static ForestItem NewItem(World world, Forest forest, bool childOfLayer = false, bool sameScene = true)
    {
        var go = new GameObject("ForestItem");
        if (sameScene) go.scene.handle = world.gameLayer.gameObject.scene.handle;
        if (childOfLayer) go.transform.SetParent(world.gameLayer);
        ForestItem item = go.AddComponent<ForestItem>();
        item._forest = forest;
        item.AccessCount = 0;
        return item;
    }

    private static ManualLogSource Log => KingdomEnhancedPlugin.Instance.LogSource;

    private static float RunPrefix(ForestItem item, float delay)
    {
        object[] arguments = { item, delay };
        FadePrefix.Invoke(null, arguments);
        return (float)arguments[1];
    }

    private static void Test(string name, Action body)
    {
        try
        {
            body();
            HostChecks.Passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            HostChecks.Failed++;
            Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message);
        }
    }

    // ---------------------------------------------------------------- tests

    private static void DisabledZeroAccess()
    {
        World world = NewWorld();
        Forest forest = NewForest(world);
        ForestItem item = NewItem(world, forest);
        item.ThrowOnControlsForestSize = true;
        item.ThrowOnRemovedByForest = true;
        item.ThrowOnRemoveDelay = true;
        item.ThrowOnForest = true;
        float delay = RunPrefix(item, 5f);
        HostChecks.Near(5f, delay, "OFF keeps the native wait parameter");
        HostChecks.Eq(0, item.AccessCount, "OFF touches no item accessor");
        HostChecks.Eq(0, UnityEngine.Random.Calls, "OFF consumes no RNG");
        HostChecks.Eq(0, Log.Warnings.Count, "OFF never warns");
    }

    private static void ScopeInactiveZeroAccess()
    {
        World world = NewWorld();
        Forest forest = NewForest(world);
        ForestItem item = NewItem(world, forest);
        item.ThrowOnControlsForestSize = true;
        item.ThrowOnRemovedByForest = true;
        item.ThrowOnRemoveDelay = true;
        item.ThrowOnForest = true;
        ModConfig.FastForestRecedeEnabled.Value = true;

        ModConfig.Enabled.Value = false;
        HostChecks.Near(2f, RunPrefix(item, 2f), "master switch off keeps the parameter");
        BiomeHolder.Inst = null;
        ModConfig.Enabled.Value = true;
        HostChecks.Near(2f, RunPrefix(item, 2f), "no biome keeps the parameter");
        BiomeHolder.Inst = new BiomeHolder { BiomeIndex = -1 };
        HostChecks.Near(2f, RunPrefix(item, 2f), "biome index -1 keeps the parameter");

        HostChecks.Eq(0, item.AccessCount, "an inactive scope touches no item accessor");
        HostChecks.Eq(0, UnityEngine.Random.Calls, "an inactive scope consumes no RNG");
        HostChecks.Eq(0, Log.Warnings.Count, "an inactive scope never warns");
    }

    private static void PositiveThird()
    {
        World world = NewWorld();
        Forest forest = NewForest(world);
        ForestItem item = NewItem(world, forest);
        ModConfig.FastForestRecedeEnabled.Value = true;
        HostChecks.Near(1f, RunPrefix(item, 3f), "explicit 3s becomes 1s");
        HostChecks.Eq(0, UnityEngine.Random.Calls, "an explicit positive delay consumes no RNG");
        HostChecks.Near(10f, item.removeDelay, "removeDelay stays native");
    }

    private static void FallbackSingleDraw()
    {
        World world = NewWorld();
        Forest forest = NewForest(world);
        ForestItem item = NewItem(world, forest);
        ModConfig.FastForestRecedeEnabled.Value = true;
        item.removeDelay = 10f;
        HostChecks.Near(10f / 3f, RunPrefix(item, 0f), "removeDelay(10) * Range(0.5,1.5)=1.0 / 3");
        HostChecks.Eq(1, UnityEngine.Random.Calls, "the fallback draws exactly once");
        item.removeDelay = 0.9f;
        UnityEngine.Random.Calls = 0;
        HostChecks.Near(0.9f / 3f * 1f, RunPrefix(item, -2f), "a negative delay takes the fallback too");
        HostChecks.Eq(1, UnityEngine.Random.Calls, "the negative-delay fallback draws exactly once");
    }

    private static void NativeFieldsAndBody()
    {
        World world = NewWorld();
        Forest forest = NewForest(world);
        ForestItem item = NewItem(world, forest);
        ModConfig.FastForestRecedeEnabled.Value = true;
        item.removeDelay = 10f;
        float scaled = RunPrefix(item, 0f);
        HostChecks.Near(10f / 3f, scaled, "prefix result");
        HostChecks.Near(10f, item.removeDelay, "the prefix never writes removeDelay");
        HostChecks.Check(!item.controlsForestSize, "the prefix never writes controlsForestSize");
        HostChecks.Check(!item.removedByForest, "the prefix never writes removedByForest");
        HostChecks.Check(!item.NativeRemoved, "the prefix never marks the native removal");

        item.FadeAndRemove(scaled); // the native body runs with the prefix-adjusted parameter
        HostChecks.Eq(1, item.FadeAndRemoveCalls, "the native body still runs");
        HostChecks.Near(scaled, item.NativeWait, "the native body consumes the adjusted wait");
        HostChecks.Check(item.NativeRemoved, "the native body keeps its own removedByForest write");

        UnityEngine.Random.Calls = 0;
        HostChecks.Near(scaled, RunPrefix(item, scaled), "a repeat call on a native-removed item keeps the parameter");
        HostChecks.Eq(0, UnityEngine.Random.Calls, "the repeat call draws no RNG");
    }

    private static void ControlsAndRemovedGate()
    {
        World world = NewWorld();
        Forest forest = NewForest(world);
        ForestItem item = NewItem(world, forest);
        ModConfig.FastForestRecedeEnabled.Value = true;
        item.controlsForestSize = true;
        HostChecks.Near(0f, RunPrefix(item, 0f), "controlsForestSize keeps the parameter");
        HostChecks.Eq(0, UnityEngine.Random.Calls, "controlsForestSize draws no RNG");
        HostChecks.Near(1.5f, RunPrefix(item, 1.5f), "controlsForestSize keeps an explicit delay");
        item.controlsForestSize = false;
        item.removedByForest = true;
        HostChecks.Near(0f, RunPrefix(item, 0f), "removedByForest keeps the parameter");
        HostChecks.Eq(0, UnityEngine.Random.Calls, "removedByForest draws no RNG");
    }

    private static void EligibilityBoundaries()
    {
        World world = NewWorld();
        Forest forest = NewForest(world);
        ForestItem item = NewItem(world, forest);
        ModConfig.FastForestRecedeEnabled.Value = true;

        item.gameObject.scene.handle = world.gameLayer.gameObject.scene.handle + 1;
        HostChecks.Near(3f, RunPrefix(item, 3f), "an item in another scene keeps the parameter");
        item.gameObject.scene.handle = world.gameLayer.gameObject.scene.handle;

        forest.gameObject.scene.handle = world.gameLayer.gameObject.scene.handle + 1;
        HostChecks.Near(3f, RunPrefix(item, 3f), "a forest in another scene keeps the parameter");
        forest.gameObject.scene.handle = world.gameLayer.gameObject.scene.handle;

        forest.transform.SetParent(null);
        HostChecks.Near(3f, RunPrefix(item, 3f), "a forest outside the game layer keeps the parameter");
        forest.transform.SetParent(world.gameLayer);

        forest.gameObject.activeInHierarchy = false;
        HostChecks.Near(3f, RunPrefix(item, 3f), "an inactive forest keeps the parameter");
        forest.gameObject.activeInHierarchy = true;

        world.gameLayer.gameObject.activeInHierarchy = false;
        HostChecks.Near(3f, RunPrefix(item, 3f), "an inactive game layer keeps the parameter");
        world.gameLayer.gameObject.activeInHierarchy = true;

        // Parallax item: not a child of the layer, but the same scene -> eligible.
        HostChecks.Near(1f, RunPrefix(item, 3f), "a parallax item (same scene, not a child) is eligible");

        item._forest = null;
        HostChecks.Near(3f, RunPrefix(item, 3f), "a null forest keeps the parameter");
        item._forest = forest;

        Transform layer = world.gameLayer;
        world.gameLayer = null;
        HostChecks.Near(3f, RunPrefix(item, 3f), "a null game layer keeps the parameter");
        world.gameLayer = layer;

        Managers.Inst = null;
        HostChecks.Near(3f, RunPrefix(item, 3f), "no current world keeps the parameter");
        HostChecks.Eq(0, Log.Warnings.Count, "gate rejects never warn");
    }

    private static void InvalidNumberKeepsRef()
    {
        World world = NewWorld();
        Forest forest = NewForest(world);
        ForestItem item = NewItem(world, forest);
        ModConfig.FastForestRecedeEnabled.Value = true;

        item.removeDelay = float.NaN;
        HostChecks.Near(0f, RunPrefix(item, 0f), "the NaN precompute never commits the ref");
        HostChecks.Eq(1, UnityEngine.Random.Calls, "the invalid precompute still drew once (existing source behavior)");
        item.FadeAndRemove(0f);
        HostChecks.Eq(2, UnityEngine.Random.Calls, "the native else-branch draws again after the invalid precompute");
        HostChecks.Check(item.NativeRemoved, "the native body still completes");

        item = NewItem(world, forest);
        item.removeDelay = float.PositiveInfinity;
        UnityEngine.Random.Calls = 0;
        HostChecks.Near(0f, RunPrefix(item, 0f), "an infinite precompute never commits the ref");
        HostChecks.Eq(1, UnityEngine.Random.Calls, "the infinite precompute drew once");

        item = NewItem(world, forest);
        item.removeDelay = 1.2f;
        UnityEngine.Random.Unit = 0f; // Range(0.5,1.5) = 0.5 -> base 0.6 -> scaled 0.2
        HostChecks.Near(0.2f, RunPrefix(item, 0f), "the fallback scales the controlled draw");
    }

    private static void FailureWarningOnce()
    {
        World world = NewWorld();
        Forest forest = NewForest(world);
        ForestItem item = NewItem(world, forest);
        ModConfig.FastForestRecedeEnabled.Value = true;

        Log.ThrowOnWarning = true; // the single warning attempt must not escape into the game loop
        item.ThrowOnForest = true;
        HostChecks.Near(3f, RunPrefix(item, 3f), "a failing access keeps the native wait parameter");
        HostChecks.Eq(1, Log.Warnings.Count, "the failure produces exactly one warning attempt");
        HostChecks.CheckContains("[FastForestRecede]", Log.Warnings[0], "warning prefix");
        HostChecks.CheckContains("保留原生等待参数", Log.Warnings[0], "warning states the native parameter is kept");
        Log.ThrowOnWarning = false;

        item.ThrowOnForest = false;
        item.ThrowOnRemovedByForest = true;
        HostChecks.Near(4f, RunPrefix(item, 4f), "a second failure still keeps the parameter");
        HostChecks.Eq(1, Log.Warnings.Count, "the warning is once per lifetime, not per failure");
        HostChecks.Eq(0, UnityEngine.Random.Calls, "failed accesses never reach the RNG");
    }

    private static void WrapperSurface()
    {
        Type wrapper = typeof(ForestItem_FadeAndRemove_OptionalVegetation_Patch);
        var patch = (HarmonyLib.HarmonyPatch)Attribute.GetCustomAttribute(wrapper, typeof(HarmonyLib.HarmonyPatch));
        HostChecks.Check(patch != null, "wrapper carries the HarmonyPatch attribute (PC auto-patch surface)");
        HostChecks.Check(patch.TargetType == typeof(ForestItem), "wrapper targets ForestItem");
        HostChecks.Eq("FadeAndRemove", patch.MethodName, "wrapper targets FadeAndRemove");
        ParameterInfo[] parameters = FadePrefix.GetParameters();
        HostChecks.Eq(2, parameters.Length, "Prefix parameter count");
        HostChecks.Check(parameters[0].ParameterType == typeof(ForestItem), "Prefix binds ForestItem __instance");
        HostChecks.Check(parameters[1].ParameterType == typeof(float).MakeByRefType(), "Prefix binds ref float delay");
        HostChecks.Check(typeof(PatchWorld_FastForestRecede).Assembly == wrapper.Assembly, "shared class and wrapper share the Android host assembly");
    }
}
