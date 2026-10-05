using System;
using System.Collections.Generic;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

// Behavior boundaries of the shared il2cpp/PatchWorld_OptionalVegetation.cs compiled with
// ANDROID against the real android/OptionalQoLScope.cs and the typed Il2Cpp interop shapes:
//   - default OFF with an empty debt ledger: the four hook entries and Tick perform zero
//     native reads even when every stub getter throws (managed gate first);
//   - the borrow/restore lease survives a setter that lands its value and then throws, a
//     double failure parks a Pending entry that a later OFF entry settles, nested windows
//     never halve twice and a stale finalizer never overwrites the newer lease;
//   - the Android FX seam is compiled out: a present SpriteRendererFX fake is never started,
//     no TryStartFxFade/FxFade member exists on the compiled type, and every fade goes
//     through the receipt-backed fallback;
//   - full RGBA receipts: per-layer capture, fallback alpha fade, restore before the native
//     hand-back, foreign writers keep their color, same-pointer/id new life revokes the old
//     receipt, the native removal prefix restores first;
//   - the three real config writers save exactly once per real change (same/reject-false
//     zero, change/correction/observe-force one).
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
        if (!EqualityComparer<T>.Default.Equals(wanted, got))
            throw new Exception(what + ": expected " + wanted + ", got " + got);
    }

    internal static void Near(float wanted, float got, string what)
    {
        if (MathF.Abs(wanted - got) > 0.0001f)
            throw new Exception(what + ": expected " + wanted + ", got " + got);
    }

    internal static void SameColor(Color wanted, Color got, string what)
    {
        if (wanted.r != got.r || wanted.g != got.g || wanted.b != got.b || wanted.a != got.a)
            throw new Exception(what + ": expected (" + wanted.r + "," + wanted.g + "," + wanted.b + "," + wanted.a
                + "), got (" + got.r + "," + got.g + "," + got.b + "," + got.a + ")");
    }

    internal static void CheckContains(string wanted, string text, string what)
    {
        if (text == null || !text.Contains(wanted, StringComparison.Ordinal))
            throw new Exception(what + ": [" + text + "] does not contain " + wanted);
    }
}

internal static class Program
{
    private const float Spacing = 4f;

    private static readonly Color[] LayerColors =
    {
        new Color(0.10f, 0.20f, 0.30f, 1.00f),
        new Color(0.40f, 0.50f, 0.60f, 0.80f),
        new Color(0.70f, 0.80f, 0.90f, 0.60f),
    };

    private static int Main()
    {
        Console.WriteLine("DenseThicketsAndroidHost: shared ANDROID source against the real android/OptionalQoLScope");
        Test("OFF empty: zero native access even when every getter throws", OffEmptyZeroAccess);
        Test("an unwired entry surfaces the wiring error instead of a silent OFF", UnwiredEntrySurfacesError);
        Test("scalar setter writes then throws: the lease restores its own value", ScalarWriteThenThrow);
        Test("failed restore parks Pending and a later OFF entry settles it", PendingSettledWhenOff);
        Test("nested spawn window inherits without a second halving", NestedNoDoubleHalve);
        Test("a stale finalizer never overwrites the newer lease", StaleTokenFinalizer);
        Test("native exception returns through the finalizer after the scalar restore", NativeExceptionFinalizer);
        Test("full RGBA receipt: fade, restore before removal, native reclaim", FullRgbaFadeAndReclaim);
        Test("unverified backing collection fails closed without a spacing write", NonHashSetFailsClosed);
        Test("FX present is never started on Android (branch compiled out)", FxNeverStarted);
        Test("a foreign writer owns its color and is never overwritten", ForeignWriterPreserved);
        Test("same pointer/id new life revokes the old color receipt", SamePointerNewLife);
        Test("native removal prefix restores owned colors first", NativePrefixRestores);
        // Color-responsibility cases run before TrySetSaveMatrix: that case intentionally ends
        // while a cleanup batch is still pending, which would poison the next registration.
        Test("after-write color error preserves full RGBA before reclaim", AfterWriteErrorPreservesRgba);
        Test("a before-write error after a successful fade still restores the old color", BeforeWriteAfterSuccessRestores);
        Test("after-write error with a transient color-read failure keeps the receipt and recovers", TransientReadDuringIntentRecovers);
        Test("a foreign writer during a pending intent is never overwritten", ForeignDuringIntentYields);
        Test("same pointer/id new life during a pending intent revokes the receipt", NewLifeDuringIntentRevokes);
        Test("multi-layer faults resolve independently", MultiLayerFaultsResolveIndependently);
        Test("serial after-write then before-write failure keeps first landed color", SerialAfterThenBeforeError);
        Test("base resolves pending intent before next fade", PendingBaseResolution);
        Test("TrySet save matrix: same0 / changed1 / reject0 / correction1 / observe1", TrySetSaveMatrix);
        Console.WriteLine("RESULT passed=" + HostChecks.Passed + " failed=" + HostChecks.Failed);
        return HostChecks.Failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- fixtures

    private static World NewWorld()
    {
        var layer = new GameObject("GameLayer");
        var world = new World { gameLayer = layer.transform };
        world.thicketSpacing = Spacing;
        Managers.Inst = new Managers { world = world };
        BiomeHolder.Inst = new BiomeHolder { BiomeIndex = 0 };
        ModConfig.Enabled.Value = true;
        ModConfig.DenseThicketsEnabled.Value = false;
        ModConfig.SaveCalls = 0;
        NetworkBigBoss.HasWorldAuth = true;
        NetworkBigBoss.AccessCount = 0;
        NetworkBigBoss.ThrowOnAuth = false;
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
        Time.time = 0f;
        return world;
    }

    private static Grass NewGrass(World world, float x)
    {
        var go = new GameObject("Grass");
        go.scene.handle = world.gameLayer.gameObject.scene.handle;
        go.transform.SetParent(world.gameLayer);
        go.transform.position = new Vector3 { x = x };
        return go.AddComponent<Grass>();
    }

    private static GameObject NewThicket(World world, Grass grass, params Color[] layers)
    {
        var thicket = new GameObject("Thicket");
        thicket.scene.handle = world.gameLayer.gameObject.scene.handle;
        foreach (Color layer in layers)
        {
            var child = new GameObject("Layer");
            child.scene.handle = thicket.scene.handle;
            child.transform.SetParent(thicket.transform);
            child.AddComponent<SpriteRenderer>().color = layer;
        }
        grass._thicket = thicket;
        return thicket;
    }

    private static void SetMembers(World world, params Grass[] members)
    {
        var set = new Il2CppSystem.Collections.Generic.HashSet<Grass>();
        foreach (Grass grass in members) set.Add(grass);
        world._grassWithThicket = set;
    }

    /// <summary>Native order: the new thicket already exists and AddThicket just ran.</summary>
    private static void RegisterExtra(World world, Grass extra, Grass natural)
    {
        SetMembers(world, natural, extra);
        World_AddThicket_OptionalVegetation_Patch.Postfix(world, extra);
    }

    private static void ThrowEverything(World world, Grass grass)
    {
        world.ThrowOnPointer = true;
        world.ThrowOnSpacingGet = true;
        world.ThrowOnGrassWithThicket = true;
        grass.ThrowOnPointer = true;
        grass.ThrowOnThicketGet = true;
        NetworkBigBoss.ThrowOnAuth = true;
    }

    private static void StopThrowing(World world, Grass grass)
    {
        world.ThrowOnPointer = false;
        world.ThrowOnSpacingGet = false;
        world.ThrowOnGrassWithThicket = false;
        grass.ThrowOnPointer = false;
        grass.ThrowOnThicketGet = false;
        NetworkBigBoss.ThrowOnAuth = false;
    }

    private static int NativeReads(World world, Grass grass)
        => world.PointerReads + world.SpacingReads + world.GrassWithThicketReads
           + grass.PointerReads + grass.ThicketReads + NetworkBigBoss.AccessCount;

    private static void ResetNativeCounters(World world, Grass grass)
    {
        world.ResetCounters();
        grass.PointerReads = 0;
        grass.ThicketReads = 0;
        NetworkBigBoss.AccessCount = 0;
    }

    private static void Test(string name, Action body)
    {
        try
        {
            body();
            SettleBetweenScenarios();
            HostChecks.Passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            // A failed body may leave a cleanup batch pending; drain it so later scenarios stay
            // independent and the failure stays attributable to this case.
            SettleBetweenScenarios();
            HostChecks.Failed++;
            Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message);
        }
    }

    // Production runs Tick every frame. Between scenarios, complete any leftover cleanup batch
    // (bounded frames) and let the one-frame latch reset happen, exactly as the following real
    // frames would; scenario bodies and the original 14 cases stay untouched.
    private static void SettleBetweenScenarios()
    {
        for (int frame = 0; frame < 16 && PatchWorld_OptionalVegetation.IsCleaning; frame++)
        {
            Time.time += 1f;
            PatchWorld_OptionalVegetation.Tick();
        }
        PatchWorld_OptionalVegetation.Tick();
    }

    // ---------------------------------------------------------------- scenarios

    private static void OffEmptyZeroAccess()
    {
        World world = NewWorld();
        Grass grass = NewGrass(world, 1f);
        NewThicket(world, grass, LayerColors[0]);
        Logger log = KingdomEnhancedPlugin.Instance.LogSource;
        ThrowEverything(world, grass);
        ResetNativeCounters(world, grass);

        PatchWorld_OptionalVegetation.BeginCanSpawn(world, out PatchWorld_OptionalVegetation.CanSpawnLease lease);
        HostChecks.Eq(0UL, lease.Token, "OFF empty returns the default lease");
        World_AddThicket_OptionalVegetation_Patch.Postfix(world, grass);
        Grass_RemoveThicket_OptionalVegetation_Patch.Prefix(grass);
        Grass_RemoveThicket_OptionalVegetation_Patch.Postfix(grass);
        PatchWorld_OptionalVegetation.Tick();

        HostChecks.Eq(0, NativeReads(world, grass), "OFF empty performs zero native reads");
        HostChecks.Eq(0, world.SpacingWrites, "OFF empty performs zero native writes");
        HostChecks.Eq(0, log.Warnings.Count, "OFF empty logs nothing (the gate fired before any access)");
        StopThrowing(world, grass);
    }

    private static void UnwiredEntrySurfacesError()
    {
        World world = NewWorld();
        Grass grass = NewGrass(world, 1f);
        NewThicket(world, grass, LayerColors[0]);
        Logger log = KingdomEnhancedPlugin.Instance.LogSource;
        int warningsBefore = log.Warnings.Count;
        ModConfig.DenseThicketsEnabled = null; // wiring error: Initialize was never called
        ThrowEverything(world, grass);
        ResetNativeCounters(world, grass);

        PatchWorld_OptionalVegetation.BeginCanSpawn(world, out PatchWorld_OptionalVegetation.CanSpawnLease lease);
        HostChecks.Eq(0UL, lease.Token, "an unwired entry never silently reads as OFF");
        HostChecks.Eq(0, NativeReads(world, grass), "the wiring error is surfaced before any native read");
        HostChecks.Check(log.Warnings.Count == warningsBefore + 1, "the wiring error is logged once through the existing catch");
        HostChecks.CheckContains("NullReferenceException", log.Warnings[log.Warnings.Count - 1], "the log names the real wiring failure");
        ModConfig.DenseThicketsEnabled = new MelonLoader.MelonPreferences_Entry<bool>(false);
        StopThrowing(world, grass);
    }

    private static void ScalarWriteThenThrow()
    {
        World world = NewWorld();
        Grass grass = NewGrass(world, 3f);
        ModConfig.DenseThicketsEnabled.Value = true;
        SetMembers(world, grass);
        world.ResetCounters();
        Logger log = KingdomEnhancedPlugin.Instance.LogSource;
        int warningsBefore = log.Warnings.Count;
        world.ThrowAfterSpacingSetOnce = true; // the interop setter lands the value, then throws

        PatchWorld_OptionalVegetation.BeginCanSpawn(world, out PatchWorld_OptionalVegetation.CanSpawnLease lease);
        HostChecks.Eq(1, lease.Written, "the lease registered its write before calling the setter");
        HostChecks.Near(Spacing, world.thicketSpacing, "the catch restored the original spacing");
        HostChecks.Eq(2, world.SpacingWrites, "one failing write + one restoring write");
        HostChecks.Check(log.Warnings.Count == warningsBefore + 1, "the failure warns exactly once");
        HostChecks.CheckContains("[OptionalVegetation]", log.Warnings[log.Warnings.Count - 1], "warning prefix");

        bool result = false;
        PatchWorld_OptionalVegetation.FinishCanSpawn(world, grass, ref result, lease);
        HostChecks.Near(Spacing, world.thicketSpacing, "the postfix restore is idempotent");

        PatchWorld_OptionalVegetation.BeginCanSpawn(world, out PatchWorld_OptionalVegetation.CanSpawnLease next);
        HostChecks.Near(Spacing / 2f, world.thicketSpacing, "the next window applies the half spacing once");
        PatchWorld_OptionalVegetation.FinishCanSpawn(world, grass, ref result, next);
        HostChecks.Near(Spacing, world.thicketSpacing, "the next window restores at its own finish");
    }

    private static void PendingSettledWhenOff()
    {
        World world = NewWorld();
        Grass grass = NewGrass(world, 3f);
        ModConfig.DenseThicketsEnabled.Value = true;
        SetMembers(world, grass);
        world.ThrowAfterSpacingSet = true; // every write lands then throws: the restore fails too

        PatchWorld_OptionalVegetation.BeginCanSpawn(world, out PatchWorld_OptionalVegetation.CanSpawnLease parked);
        HostChecks.Near(Spacing, world.thicketSpacing, "both failing writes still landed the original value");
        world.ThrowAfterSpacingSet = false;
        ModConfig.DenseThicketsEnabled.Value = false;
        int writesBefore = world.SpacingWrites;

        // OFF must still settle the parked scalar before anything else.
        PatchWorld_OptionalVegetation.BeginCanSpawn(world, out PatchWorld_OptionalVegetation.CanSpawnLease settled);
        HostChecks.Eq(0, settled.Written, "the settling call holds no new lease");
        HostChecks.Near(Spacing, world.thicketSpacing, "the spacing stays the original");
        HostChecks.Eq(writesBefore, world.SpacingWrites, "the settle needed no third write");

        // debt gone: the next OFF empty call is the managed gate again.
        ResetNativeCounters(world, grass);
        ThrowEverything(world, grass);
        PatchWorld_OptionalVegetation.BeginCanSpawn(world, out PatchWorld_OptionalVegetation.CanSpawnLease gated);
        HostChecks.Eq(0UL, gated.Token, "after settling, OFF empty is the managed gate again");
        HostChecks.Eq(0, NativeReads(world, grass), "the gate performs zero native reads");
        StopThrowing(world, grass);
    }

    private static void NestedNoDoubleHalve()
    {
        World world = NewWorld();
        Grass grass = NewGrass(world, 3f);
        ModConfig.DenseThicketsEnabled.Value = true;
        SetMembers(world, grass);
        bool result = false;

        PatchWorld_OptionalVegetation.BeginCanSpawn(world, out PatchWorld_OptionalVegetation.CanSpawnLease outer);
        HostChecks.Near(Spacing / 2f, world.thicketSpacing, "the outer window halves the spacing");
        PatchWorld_OptionalVegetation.BeginCanSpawn(world, out PatchWorld_OptionalVegetation.CanSpawnLease inner);
        HostChecks.Eq(0, inner.Written, "the nested call inherits instead of rewriting");
        HostChecks.Eq(0UL, inner.Token, "the nested call holds no token");
        HostChecks.Near(Spacing / 2f, world.thicketSpacing, "the nested call never halves a second time");
        PatchWorld_OptionalVegetation.FinishCanSpawn(world, grass, ref result, inner);
        HostChecks.Near(Spacing / 2f, world.thicketSpacing, "the nested finish does not restore early");
        PatchWorld_OptionalVegetation.FinishCanSpawn(world, grass, ref result, outer);
        HostChecks.Near(Spacing, world.thicketSpacing, "the outer finish restores the original");
    }

    private static void StaleTokenFinalizer()
    {
        World world = NewWorld();
        Grass grass = NewGrass(world, 3f);
        ModConfig.DenseThicketsEnabled.Value = true;
        SetMembers(world, grass);
        bool result = false;

        PatchWorld_OptionalVegetation.BeginCanSpawn(world, out PatchWorld_OptionalVegetation.CanSpawnLease first);
        HostChecks.Near(Spacing / 2f, world.thicketSpacing, "the first window halves");
        PatchWorld_OptionalVegetation.FinishCanSpawn(world, grass, ref result, first);
        HostChecks.Near(Spacing, world.thicketSpacing, "the first window closed cleanly");

        PatchWorld_OptionalVegetation.BeginCanSpawn(world, out PatchWorld_OptionalVegetation.CanSpawnLease second);
        HostChecks.Near(Spacing / 2f, world.thicketSpacing, "the second window halves");
        var native = new InvalidOperationException("native failure after the first window closed");
        Exception returned = PatchWorld_OptionalVegetation.AbortCanSpawn(native, first);
        HostChecks.Check(ReferenceEquals(returned, native), "the stale finalizer returns its exception untouched");
        HostChecks.Near(Spacing / 2f, world.thicketSpacing, "the stale finalizer does not touch the newer lease");
        PatchWorld_OptionalVegetation.FinishCanSpawn(world, grass, ref result, second);
        HostChecks.Near(Spacing, world.thicketSpacing, "the newer lease still restores at its own finish");
    }

    private static void NativeExceptionFinalizer()
    {
        World world = NewWorld();
        Grass grass = NewGrass(world, 3f);
        ModConfig.DenseThicketsEnabled.Value = true;
        SetMembers(world, grass);

        PatchWorld_OptionalVegetation.BeginCanSpawn(world, out PatchWorld_OptionalVegetation.CanSpawnLease lease);
        HostChecks.Near(Spacing / 2f, world.thicketSpacing, "the window halves");
        var exception = new InvalidOperationException("native body failed");
        Exception returned = PatchWorld_OptionalVegetation.AbortCanSpawn(exception, lease);
        HostChecks.Check(ReferenceEquals(returned, exception), "the finalizer returns the native exception unchanged");
        HostChecks.Near(Spacing, world.thicketSpacing, "the finalizer restored the scalar on the exception path");
    }

    private static void FullRgbaFadeAndReclaim()
    {
        World world = NewWorld();
        Grass natural = NewGrass(world, 0f);
        Grass extra = NewGrass(world, 1f);
        GameObject thicket = NewThicket(world, extra, LayerColors);
        SpriteRenderer[] sprites = thicket.GetComponentsInChildren<SpriteRenderer>(true);
        HostChecks.Eq(3, sprites.Length, "the thicket carries three sprite layers");
        ModConfig.DenseThicketsEnabled.Value = true;
        RegisterExtra(world, extra, natural);
        HostChecks.Eq(0, ModConfig.SaveCalls, "registration alone saves nothing");

        HostChecks.Check(PatchWorld_OptionalVegetation.TrySetDenseThickets(false), "the OFF request lands");
        HostChecks.Eq(1, ModConfig.SaveCalls, "the real change saves exactly once");

        PatchWorld_OptionalVegetation.Tick(); // StartFade: capture the receipt, no fade yet
        HostChecks.Check(PatchWorld_OptionalVegetation.IsCleaning, "the record is in cleanup");
        for (int i = 0; i < 3; i++) HostChecks.Near(LayerColors[i].a, sprites[i].color.a, "capture does not fade layer " + i);

        Time.time = 0.2f;
        PatchWorld_OptionalVegetation.Tick(); // half fade through the receipt
        for (int i = 0; i < 3; i++)
        {
            HostChecks.Near(LayerColors[i].a / 2f, sprites[i].color.a, "layer " + i + " fades to half alpha");
            HostChecks.Near(LayerColors[i].r, sprites[i].color.r, "layer " + i + " keeps its RGB");
        }

        Time.time = 0.5f;
        PatchWorld_OptionalVegetation.Tick(); // restore + native hand-back
        HostChecks.Eq(1, extra.RemoveCalls, "the native reclaim ran exactly once");
        HostChecks.Check(extra._thicket == null, "the native body really handed the thicket back");
        HostChecks.Check(!PatchWorld_OptionalVegetation.IsCleaning, "the record is fully settled");
        HostChecks.Eq(3, extra.RemovalColorSnapshot.Count, "the hand-back saw three layers");
        for (int i = 0; i < 3; i++)
        {
            HostChecks.SameColor(LayerColors[i], extra.RemovalColorSnapshot[i], "layer " + i + " was restored before removal");
            HostChecks.SameColor(LayerColors[i], sprites[i].color, "layer " + i + " keeps its full RGBA in the pool");
        }
    }

    private static void NonHashSetFailsClosed()
    {
        World world = NewWorld();
        Grass grass = NewGrass(world, 3f);
        ModConfig.DenseThicketsEnabled.Value = true;
        var plain = new Il2CppSystem.Collections.Generic.ICollection<Grass> { grass };
        world._grassWithThicket = plain; // declared ICollection but not the native HashSet
        world.ResetCounters();

        PatchWorld_OptionalVegetation.BeginCanSpawn(world, out PatchWorld_OptionalVegetation.CanSpawnLease lease);
        HostChecks.Eq(0, lease.Written, "an unverified backing collection fails closed");
        HostChecks.Eq(0, world.SpacingWrites, "no spacing write without the native HashSet");
        HostChecks.Near(Spacing, world.thicketSpacing, "the spacing stays untouched");
    }

    private static void FxNeverStarted()
    {
        World world = NewWorld();
        Grass natural = NewGrass(world, 0f);
        Grass extra = NewGrass(world, 1f);
        GameObject thicket = NewThicket(world, extra, LayerColors);
        SpriteRendererFX fx = thicket.AddComponent<SpriteRendererFX>(); // present, must never run
        ModConfig.DenseThicketsEnabled.Value = true;
        RegisterExtra(world, extra, natural);
        PatchWorld_OptionalVegetation.TrySetDenseThickets(false);

        PatchWorld_OptionalVegetation.Tick();
        Time.time = 0.2f;
        PatchWorld_OptionalVegetation.Tick();
        Time.time = 0.5f;
        PatchWorld_OptionalVegetation.Tick();

        HostChecks.Eq(0, fx.FadeOutCalls, "the Android build never starts an FX present on the thicket");
        HostChecks.Eq(1, extra.RemoveCalls, "the reclaimed record still completed through the fallback");
        SpriteRenderer sprite = thicket.GetComponentsInChildren<SpriteRenderer>(true)[0];
        HostChecks.SameColor(LayerColors[0], sprite.color, "the fallback restored the layer");
        HostChecks.Check(typeof(PatchWorld_OptionalVegetation).GetMethod("TryStartFxFade",
            BindingFlags.NonPublic | BindingFlags.Static) == null, "TryStartFxFade is compiled out");
        Type recordType = typeof(PatchWorld_OptionalVegetation).GetNestedType("ExtraThicket", BindingFlags.NonPublic);
        HostChecks.Check(recordType != null && recordType.GetField("FxFade", BindingFlags.NonPublic | BindingFlags.Instance) == null,
            "the FX fade flag is compiled out");
    }

    private static void ForeignWriterPreserved()
    {
        World world = NewWorld();
        Grass natural = NewGrass(world, 0f);
        Grass extra = NewGrass(world, 1f);
        GameObject thicket = NewThicket(world, extra, LayerColors);
        SpriteRenderer[] sprites = thicket.GetComponentsInChildren<SpriteRenderer>(true);
        ModConfig.DenseThicketsEnabled.Value = true;
        RegisterExtra(world, extra, natural);
        PatchWorld_OptionalVegetation.TrySetDenseThickets(false);

        PatchWorld_OptionalVegetation.Tick(); // capture
        var foreign = new Color(0.2f, 0.3f, 0.4f, 0.9f);
        sprites[1].color = foreign;           // an external writer takes layer 1 after the capture
        Time.time = 0.2f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.SameColor(foreign, sprites[1].color, "the fade never overwrites the foreign color");
        HostChecks.Near(LayerColors[0].a / 2f, sprites[0].color.a, "the untouched layer still fades");

        Time.time = 0.5f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.SameColor(foreign, sprites[1].color, "the restore never overwrites the foreign color");
        HostChecks.SameColor(LayerColors[0], sprites[0].color, "the owned layer is restored");
        HostChecks.Eq(1, extra.RemoveCalls, "the record still reclaimed");
        HostChecks.SameColor(foreign, extra.RemovalColorSnapshot[1], "the pool keeps the foreign color");
    }

    private static void SamePointerNewLife()
    {
        World world = NewWorld();
        Grass natural = NewGrass(world, 0f);
        Grass extra = NewGrass(world, 1f);
        GameObject thicket = NewThicket(world, extra, LayerColors);
        ModConfig.DenseThicketsEnabled.Value = true;
        RegisterExtra(world, extra, natural);
        PatchWorld_OptionalVegetation.TrySetDenseThickets(false);
        PatchWorld_OptionalVegetation.Tick(); // capture a live receipt on the old life

        // Pool reuse: the same native grass/thicket objects come back as a new life.
        var reusedGo = new GameObject("ReusedGrass");
        reusedGo.scene.handle = world.gameLayer.gameObject.scene.handle;
        reusedGo.transform.SetParent(world.gameLayer);
        reusedGo.transform.position = new Vector3 { x = 5f };
        Grass reused = reusedGo.AddComponent<Grass>();
        reused.Pointer = extra.Pointer;
        reused.OverrideInstanceID(extra.GetInstanceID());
        GameObject reusedThicket = NewThicket(world, reused, new Color(0.9f, 0.85f, 0.8f, 0.7f));
        reusedThicket.Pointer = thicket.Pointer;
        reusedThicket.OverrideInstanceID(thicket.GetInstanceID());
        SpriteRenderer reusedSprite = reusedThicket.GetComponentsInChildren<SpriteRenderer>(true)[0];
        int writesBefore = reusedSprite.ColorWrites;

        // The new life raises AddThicket while OFF: the old receipt must be revoked, not poured
        // over the re-used object.
        World_AddThicket_OptionalVegetation_Patch.Postfix(world, reused);

        Time.time = 0.5f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Eq(writesBefore, reusedSprite.ColorWrites, "the old receipt writes nothing to the new life");
        HostChecks.SameColor(new Color(0.9f, 0.85f, 0.8f, 0.7f), reusedSprite.color, "the new life keeps its own color");
        HostChecks.Eq(0, reused.RemoveCalls, "the old dead record never reclaims the new life");
        HostChecks.Eq(0, extra.RemoveCalls, "the old record never reclaims the old object either");
        HostChecks.Check(!PatchWorld_OptionalVegetation.IsCleaning, "the revoked record was dropped");
    }

    private static void NativePrefixRestores()
    {
        World world = NewWorld();
        Grass natural = NewGrass(world, 0f);
        Grass extra = NewGrass(world, 1f);
        GameObject thicket = NewThicket(world, extra, LayerColors);
        SpriteRenderer[] sprites = thicket.GetComponentsInChildren<SpriteRenderer>(true);
        ModConfig.DenseThicketsEnabled.Value = true;
        RegisterExtra(world, extra, natural);
        PatchWorld_OptionalVegetation.TrySetDenseThickets(false);

        PatchWorld_OptionalVegetation.Tick(); // capture
        Time.time = 0.2f;
        PatchWorld_OptionalVegetation.Tick(); // half fade
        HostChecks.Near(LayerColors[0].a / 2f, sprites[0].color.a, "the fade is mid-flight");

        Grass_RemoveThicket_OptionalVegetation_Patch.Prefix(extra); // native deletion (winter/stage) path
        for (int i = 0; i < 3; i++) HostChecks.SameColor(LayerColors[i], sprites[i].color, "the prefix restored layer " + i);

        extra.RemoveThicket(); // the native body then really hands the object back
        Grass_RemoveThicket_OptionalVegetation_Patch.Postfix(extra);
        HostChecks.Eq(1, extra.RemoveCalls, "the native body ran once");
        HostChecks.Eq(3, extra.RemovalColorSnapshot.Count, "the hand-back saw the restored layers");
        HostChecks.SameColor(LayerColors[0], extra.RemovalColorSnapshot[0], "the hand-back saw the restored color");

        Time.time = 0.5f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Check(!PatchWorld_OptionalVegetation.IsCleaning, "the native removal settled the record");
    }

    private static void TrySetSaveMatrix()
    {
        World world = NewWorld();
        Grass natural = NewGrass(world, 0f);
        Grass extra = NewGrass(world, 1f);
        NewThicket(world, extra, LayerColors);
        ModConfig.SaveCalls = 0;

        // load0 lives in AdapterTests (Initialize writes nothing); same value => no save.
        HostChecks.Check(PatchWorld_OptionalVegetation.TrySetDenseThickets(false), "the same-value OFF request succeeds");
        HostChecks.Eq(0, ModConfig.SaveCalls, "a same-value request does not save");

        HostChecks.Check(PatchWorld_OptionalVegetation.TrySetDenseThickets(true), "the ON request lands");
        HostChecks.Check(ModConfig.DenseThicketsEnabled.Value, "the entry holds the ON value");
        HostChecks.Eq(1, ModConfig.SaveCalls, "the real change saves exactly once");

        RegisterExtra(world, extra, natural);
        HostChecks.Check(PatchWorld_OptionalVegetation.TrySetDenseThickets(false), "the OFF request lands");
        HostChecks.Eq(2, ModConfig.SaveCalls, "the OFF change saves exactly once more");
        PatchWorld_OptionalVegetation.Tick(); // StartFade -> cleanup pending
        HostChecks.Check(PatchWorld_OptionalVegetation.IsCleaning, "the record is cleaning now");

        HostChecks.Check(!PatchWorld_OptionalVegetation.TrySetDenseThickets(true), "re-enable during cleanup is refused");
        HostChecks.Eq(2, ModConfig.SaveCalls, "the refused request never wrote (entry already false)");

        ModConfig.DenseThicketsEnabled.Value = true; // external cfg write while cleaning
        HostChecks.Check(!PatchWorld_OptionalVegetation.TrySetDenseThickets(true), "the forced-true re-enable is refused");
        HostChecks.Check(!ModConfig.DenseThicketsEnabled.Value, "the refusal corrected the entry back to false");
        HostChecks.Eq(3, ModConfig.SaveCalls, "the correction saved exactly once");

        PatchWorld_OptionalVegetation.Tick();         // observe false, seen := false
        ModConfig.DenseThicketsEnabled.Value = true;  // external write again
        PatchWorld_OptionalVegetation.Tick();         // observe true while cleaning -> forced off
        HostChecks.Check(!ModConfig.DenseThicketsEnabled.Value, "ObserveDenseConfig forced the external true back off");
        HostChecks.Eq(4, ModConfig.SaveCalls, "the forced-off saved exactly once");

        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Eq(4, ModConfig.SaveCalls, "no further saves once settled back off");
    }

    // ---------------------------------------------------------------- color write responsibility (fix round)

    private static void AfterWriteErrorPreservesRgba()
    {
        World world = NewWorld();
        Grass natural = NewGrass(world, 0f);
        Grass extra = NewGrass(world, 1f);
        GameObject thicket = NewThicket(world, extra, LayerColors);
        SpriteRenderer[] sprites = thicket.GetComponentsInChildren<SpriteRenderer>(true);
        ModConfig.DenseThicketsEnabled.Value = true;
        RegisterExtra(world, extra, natural);
        HostChecks.Check(PatchWorld_OptionalVegetation.TrySetDenseThickets(false), "the OFF request lands");

        PatchWorld_OptionalVegetation.Tick(); // capture
        HostChecks.Check(PatchWorld_OptionalVegetation.IsCleaning, "the record is in cleanup");
        sprites[0].ThrowAfterColorWrite = true; // every write lands its value and then throws
        Time.time = 0.2f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Near(LayerColors[0].a / 2f, sprites[0].PeekColor.a, "the failed write still landed its alpha");

        sprites[0].ThrowAfterColorWrite = false;
        Time.time = 0.5f;
        PatchWorld_OptionalVegetation.Tick(); // restore + native hand-back
        HostChecks.Eq(1, extra.RemoveCalls, "the native reclaim ran exactly once");
        HostChecks.Check(!PatchWorld_OptionalVegetation.IsCleaning, "the record is fully settled");
        HostChecks.Eq(3, extra.RemovalColorSnapshot.Count, "the hand-back saw three layers");
        for (int i = 0; i < 3; i++)
        {
            HostChecks.SameColor(LayerColors[i], extra.RemovalColorSnapshot[i], "layer " + i + " was restored before removal");
            HostChecks.SameColor(LayerColors[i], sprites[i].color, "layer " + i + " keeps its full RGBA in the pool");
        }
    }

    private static void BeforeWriteAfterSuccessRestores()
    {
        World world = NewWorld();
        Grass natural = NewGrass(world, 0f);
        Grass extra = NewGrass(world, 1f);
        GameObject thicket = NewThicket(world, extra, LayerColors);
        SpriteRenderer[] sprites = thicket.GetComponentsInChildren<SpriteRenderer>(true);
        ModConfig.DenseThicketsEnabled.Value = true;
        RegisterExtra(world, extra, natural);
        HostChecks.Check(PatchWorld_OptionalVegetation.TrySetDenseThickets(false), "the OFF request lands");

        PatchWorld_OptionalVegetation.Tick(); // capture
        Time.time = 0.2f;
        PatchWorld_OptionalVegetation.Tick(); // all three layers fade once successfully
        for (int i = 0; i < 3; i++) HostChecks.Near(LayerColors[i].a / 2f, sprites[i].color.a, "layer " + i + " faded once");

        sprites[1].ThrowOnColorWrite = true; // the next write fails before landing
        Time.time = 0.3f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Near(LayerColors[1].a / 2f, sprites[1].color.a, "the before-write failure keeps the last landed value");
        sprites[1].ThrowOnColorWrite = false;

        Time.time = 0.6f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Eq(1, extra.RemoveCalls, "the native reclaim ran exactly once");
        for (int i = 0; i < 3; i++)
            HostChecks.SameColor(LayerColors[i], extra.RemovalColorSnapshot[i], "layer " + i + " was restored");
    }

    private static void TransientReadDuringIntentRecovers()
    {
        World world = NewWorld();
        Grass natural = NewGrass(world, 0f);
        Grass extra = NewGrass(world, 1f);
        GameObject thicket = NewThicket(world, extra, LayerColors);
        SpriteRenderer[] sprites = thicket.GetComponentsInChildren<SpriteRenderer>(true);
        ModConfig.DenseThicketsEnabled.Value = true;
        RegisterExtra(world, extra, natural);
        HostChecks.Check(PatchWorld_OptionalVegetation.TrySetDenseThickets(false), "the OFF request lands");

        PatchWorld_OptionalVegetation.Tick(); // capture
        sprites[2].ThrowAfterColorWriteOnce = true;
        Time.time = 0.2f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Near(LayerColors[2].a / 2f, sprites[2].PeekColor.a, "the failed write still landed its alpha");

        sprites[2].ThrowOnColorRead = true;
        Time.time = 0.3f;
        PatchWorld_OptionalVegetation.Tick(); // the per-layer read fails: receipt and intent stay
        HostChecks.Near(LayerColors[2].a / 2f, sprites[2].PeekColor.a, "the deferred layer is neither written nor dropped");
        HostChecks.Eq(0, extra.RemoveCalls, "no reclaim while the receipt is unresolved");
        HostChecks.Check(PatchWorld_OptionalVegetation.IsCleaning, "the record is still owned");

        sprites[2].ThrowOnColorRead = false;
        Time.time = 0.35f;
        PatchWorld_OptionalVegetation.Tick(); // the resolved intent lets the fade continue
        HostChecks.Check(sprites[2].color.a < LayerColors[2].a / 2f, "the fade resumes after the read recovers");
        HostChecks.Check(sprites[2].color.a > 0f, "the fade did not jump to invisibility");

        Time.time = 0.6f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Eq(1, extra.RemoveCalls, "the native reclaim ran exactly once");
        for (int i = 0; i < 3; i++)
            HostChecks.SameColor(LayerColors[i], extra.RemovalColorSnapshot[i], "layer " + i + " was restored");
    }

    private static void ForeignDuringIntentYields()
    {
        World world = NewWorld();
        Grass natural = NewGrass(world, 0f);
        Grass extra = NewGrass(world, 1f);
        GameObject thicket = NewThicket(world, extra, LayerColors);
        SpriteRenderer[] sprites = thicket.GetComponentsInChildren<SpriteRenderer>(true);
        ModConfig.DenseThicketsEnabled.Value = true;
        RegisterExtra(world, extra, natural);
        HostChecks.Check(PatchWorld_OptionalVegetation.TrySetDenseThickets(false), "the OFF request lands");

        PatchWorld_OptionalVegetation.Tick(); // capture
        sprites[1].ThrowAfterColorWriteOnce = true;
        Time.time = 0.2f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Near(LayerColors[1].a / 2f, sprites[1].PeekColor.a, "the failed write still landed its alpha");

        var foreign = new Color(0.2f, 0.3f, 0.4f, 0.9f);
        sprites[1].color = foreign; // an external writer takes the layer while the intent is pending
        Time.time = 0.5f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.SameColor(foreign, sprites[1].color, "the restore never overwrites the foreign color");
        HostChecks.SameColor(foreign, extra.RemovalColorSnapshot[1], "the pool keeps the foreign color");
        HostChecks.SameColor(LayerColors[0], extra.RemovalColorSnapshot[0], "the owned layer is restored");
        HostChecks.Eq(1, extra.RemoveCalls, "the record still reclaimed");

        int writesAfter = sprites[1].ColorWrites;
        Time.time = 0.6f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Eq(writesAfter, sprites[1].ColorWrites, "the yielded layer is never written again");
    }

    private static void NewLifeDuringIntentRevokes()
    {
        World world = NewWorld();
        Grass natural = NewGrass(world, 0f);
        Grass extra = NewGrass(world, 1f);
        GameObject thicket = NewThicket(world, extra, LayerColors);
        SpriteRenderer[] sprites = thicket.GetComponentsInChildren<SpriteRenderer>(true);
        ModConfig.DenseThicketsEnabled.Value = true;
        RegisterExtra(world, extra, natural);
        HostChecks.Check(PatchWorld_OptionalVegetation.TrySetDenseThickets(false), "the OFF request lands");

        PatchWorld_OptionalVegetation.Tick(); // capture
        sprites[0].ThrowAfterColorWriteOnce = true;
        Time.time = 0.2f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Near(LayerColors[0].a / 2f, sprites[0].PeekColor.a, "the pending intent holds the landed value");

        // Pool reuse: the same native grass/thicket objects come back as a new life.
        var reusedGo = new GameObject("ReusedGrass");
        reusedGo.scene.handle = world.gameLayer.gameObject.scene.handle;
        reusedGo.transform.SetParent(world.gameLayer);
        reusedGo.transform.position = new Vector3 { x = 5f };
        Grass reused = reusedGo.AddComponent<Grass>();
        reused.Pointer = extra.Pointer;
        reused.OverrideInstanceID(extra.GetInstanceID());
        var sentinel = new Color(0.9f, 0.85f, 0.8f, 0.7f);
        GameObject reusedThicket = NewThicket(world, reused, sentinel);
        reusedThicket.Pointer = thicket.Pointer;
        reusedThicket.OverrideInstanceID(thicket.GetInstanceID());
        SpriteRenderer reusedSprite = reusedThicket.GetComponentsInChildren<SpriteRenderer>(true)[0];
        int writesBefore = reusedSprite.ColorWrites;

        World_AddThicket_OptionalVegetation_Patch.Postfix(world, reused); // new life revokes the pending receipt
        Time.time = 0.5f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Eq(writesBefore, reusedSprite.ColorWrites, "the revoked receipt writes nothing to the new life");
        HostChecks.SameColor(sentinel, reusedSprite.color, "the new life keeps its own color");
        HostChecks.Eq(0, reused.RemoveCalls, "the dead record never reclaims the new life");
        HostChecks.Eq(0, extra.RemoveCalls, "the dead record never reclaims the old object either");
        HostChecks.Check(!PatchWorld_OptionalVegetation.IsCleaning, "the revoked record was dropped");
    }

    private static void MultiLayerFaultsResolveIndependently()
    {
        World world = NewWorld();
        Grass natural = NewGrass(world, 0f);
        Grass extra = NewGrass(world, 1f);
        GameObject thicket = NewThicket(world, extra, LayerColors);
        SpriteRenderer[] sprites = thicket.GetComponentsInChildren<SpriteRenderer>(true);
        ModConfig.DenseThicketsEnabled.Value = true;
        RegisterExtra(world, extra, natural);
        HostChecks.Check(PatchWorld_OptionalVegetation.TrySetDenseThickets(false), "the OFF request lands");

        PatchWorld_OptionalVegetation.Tick(); // capture
        sprites[0].ThrowAfterColorWriteOnce = true; // lands then throws
        sprites[1].ThrowOnColorWrite = true;        // fails before landing
        Time.time = 0.2f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Near(LayerColors[0].a / 2f, sprites[0].PeekColor.a, "layer 0 landed its failed write");
        HostChecks.Near(LayerColors[1].a, sprites[1].color.a, "layer 1 kept its pre-fade value");
        HostChecks.Near(LayerColors[2].a / 2f, sprites[2].color.a, "layer 2 faded normally");

        sprites[1].ThrowOnColorWrite = false;
        Time.time = 0.3f;
        PatchWorld_OptionalVegetation.Tick();
        for (int i = 0; i < 3; i++)
            HostChecks.Near(LayerColors[i].a / 4f, sprites[i].color.a, "layer " + i + " converged to the next fade step");

        Time.time = 0.6f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Eq(1, extra.RemoveCalls, "the native reclaim ran exactly once");
        for (int i = 0; i < 3; i++)
            HostChecks.SameColor(LayerColors[i], extra.RemovalColorSnapshot[i], "layer " + i + " was restored");
    }

    private static void SerialAfterThenBeforeError()
    {
        World world = NewWorld();
        Grass natural = NewGrass(world, 0f);
        Grass extra = NewGrass(world, 1f);
        GameObject thicket = NewThicket(world, extra, LayerColors);
        SpriteRenderer[] sprites = thicket.GetComponentsInChildren<SpriteRenderer>(true);
        ModConfig.DenseThicketsEnabled.Value = true;
        RegisterExtra(world, extra, natural);
        HostChecks.Check(PatchWorld_OptionalVegetation.TrySetDenseThickets(false), "the OFF request lands");

        PatchWorld_OptionalVegetation.Tick(); // capture
        HostChecks.Check(PatchWorld_OptionalVegetation.IsCleaning, "the record is in cleanup");
        sprites[0].ThrowAfterColorWrite = true; // every write lands its value and then throws
        Time.time = 0.2f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Near(LayerColors[0].a / 2f, sprites[0].PeekColor.a, "the failed write still landed its alpha");

        sprites[0].ThrowAfterColorWrite = false;
        sprites[0].ThrowOnColorWrite = true; // the next write fails before landing
        Time.time = 0.3f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Near(LayerColors[0].a / 2f, sprites[0].PeekColor.a, "the second failure left the first attempted color in place");
        sprites[0].ThrowOnColorWrite = false;
        Time.time = 0.5f;
        PatchWorld_OptionalVegetation.Tick(); // restore + native hand-back
        HostChecks.Eq(1, extra.RemoveCalls, "the native reclaim ran exactly once");
        HostChecks.Check(!PatchWorld_OptionalVegetation.IsCleaning, "the record is fully settled");
        HostChecks.Eq(3, extra.RemovalColorSnapshot.Count, "the hand-back saw three layers");
        for (int i = 0; i < 3; i++)
        {
            HostChecks.SameColor(LayerColors[i], extra.RemovalColorSnapshot[i], "layer " + i + " was restored before removal");
            HostChecks.SameColor(LayerColors[i], sprites[i].color, "layer " + i + " keeps its full RGBA in the pool");
        }
    }

    private static void PendingBaseResolution()
    {
        World world = NewWorld();
        Grass natural = NewGrass(world, 0f);
        Grass extra = NewGrass(world, 1f);
        GameObject thicket = NewThicket(world, extra, LayerColors);
        SpriteRenderer[] sprites = thicket.GetComponentsInChildren<SpriteRenderer>(true);
        ModConfig.DenseThicketsEnabled.Value = true;
        RegisterExtra(world, extra, natural);
        HostChecks.Check(PatchWorld_OptionalVegetation.TrySetDenseThickets(false), "the OFF request lands");

        PatchWorld_OptionalVegetation.Tick(); // capture
        HostChecks.Check(PatchWorld_OptionalVegetation.IsCleaning, "the record is in cleanup");
        Time.time = 0.1f;
        PatchWorld_OptionalVegetation.Tick(); // previously successful alpha .75
        sprites[0].ThrowAfterColorWrite = true; // every write lands its value and then throws
        Time.time = 0.2f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Near(LayerColors[0].a / 2f, sprites[0].PeekColor.a, "the failed write still landed its alpha");

        sprites[0].ThrowAfterColorWrite = false;
        sprites[0].color = LayerColors[0]; // native/restore has already returned exact base
        Time.time = 0.3f;
        PatchWorld_OptionalVegetation.Tick();
        HostChecks.Near(LayerColors[0].a / 4f, sprites[0].PeekColor.a, "base resolves old intent and remains eligible for the next fade");
        Time.time = 0.5f;
        PatchWorld_OptionalVegetation.Tick(); // restore + native hand-back
        HostChecks.Eq(1, extra.RemoveCalls, "the native reclaim ran exactly once");
        HostChecks.Check(!PatchWorld_OptionalVegetation.IsCleaning, "the record is fully settled");
        HostChecks.Eq(3, extra.RemovalColorSnapshot.Count, "the hand-back saw three layers");
        for (int i = 0; i < 3; i++)
        {
            HostChecks.SameColor(LayerColors[i], extra.RemovalColorSnapshot[i], "layer " + i + " was restored before removal");
            HostChecks.SameColor(LayerColors[i], sprites[i].color, "layer " + i + " keeps its full RGBA in the pool");
        }
    }
}
