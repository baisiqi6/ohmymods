// Source-linked ANDROID behaviour suite for the HeavyShieldBanner platform boundary.
//
// This project compiles the REAL il2cpp/Patch_HeavyShieldBanner.cs (plus the real pure
// HeavyShieldContracts / Patch_HeavyShieldBannerPolicy sources) with ANDROID defined and
// without any of the PC-only formation owners (PatchMusketeerFormation /
// PatchWorld_FleetBoatFormation / PatchCrossbowFormation). Its compilation therefore also
// proves the Android preprocessed source never references those owners.
using System;
using System.Collections.Generic;
using System.Reflection;
using KingdomEnhancedMod;

internal static class Program
{
    private static int _passed, _failed;
    private static readonly Type Banner = typeof(HeavyShieldBanner);

    private static void Check(bool ok, string label)
    {
        if (ok) { _passed++; return; }
        _failed++;
        Console.WriteLine("FAIL " + label);
    }

    private static void ResetCanaries()
    {
        Player.Reads = 0; Formation.Reads = 0; Managers.Reads = 0; World.Reads = 0; Kingdom.Reads = 0;
        UnityEngine.GameObject.Reads = 0; UnityEngine.Transform.Reads = 0;
        Damageable.Reads = 0; FixtureArrayReads.Total = 0; FormationUnit.Reads = 0; Pikeman.Reads = 0;
        HeavyShieldIdentity.Reads = 0; HeavyShieldRuntime.Reads = 0; KingdomEnhancedPlugin.LogCalls = 0;
    }

    private static long NativeReads() => Player.Reads + Formation.Reads + Managers.Reads + World.Reads
        + Kingdom.Reads + UnityEngine.GameObject.Reads + UnityEngine.Transform.Reads + Damageable.Reads
        + FormationUnit.Reads + Pikeman.Reads + FixtureArrayReads.Total
        + HeavyShieldIdentity.Reads + HeavyShieldRuntime.Reads;

    private static FieldInfo StaticField(string name) =>
        Banner.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("missing field " + name);

    private static MethodInfo StaticMethod(string name) =>
        Banner.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("missing method " + name);

    private static long ActivationWatermark() => (long)StaticField("_nextActivation").GetValue(null);

    private static bool AllRequestsIdle()
    {
        var captures = (Array)StaticField("Captures").GetValue(null);
        foreach (var capture in captures)
        {
            var request = capture.GetType()
                .GetField("Request", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
                .GetValue(capture);
            var type = request.GetType();
            Guid receipt = (Guid)type.GetProperty("Receipt", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(request);
            long activation = (long)type.GetProperty("Activation", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(request);
            bool pending = (bool)type.GetProperty("Pending", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(request);
            if (receipt != Guid.Empty || activation != 0 || pending) return false;
        }
        return true;
    }

    private static int Main()
    {
        // ---- Android shape: no observers, no PC formation owners in this assembly ----
        Check(Banner.GetNestedType("ActivateObserver", BindingFlags.NonPublic) == null,
            "ANDROID Banner has no ActivateObserver nested type");
        Check(Banner.GetNestedType("DisableObserver", BindingFlags.NonPublic) == null,
            "ANDROID Banner has no DisableObserver nested type");
        Check(Type.GetType("KingdomEnhancedMod.PatchMusketeerFormation") == null,
            "ANDROID assembly carries no musketeer formation owner");
        Check(Type.GetType("KingdomEnhancedMod.PatchWorld_FleetBoatFormation") == null,
            "ANDROID assembly carries no fleet-boat formation owner");
        Check(Type.GetType("KingdomEnhancedMod.PatchCrossbowFormation") == null,
            "ANDROID assembly carries no crossbow formation owner");

        // Fixed receipts keep the pure tie-break deterministic (Guid.CompareTo order).
        var receiptA = new Guid("00000000-0000-0000-0000-0000000000a1");
        var receiptB = new Guid("00000000-0000-0000-0000-0000000000b2");
        var candidates = new List<HeavyShieldBannerCandidate>
        {
            new(receiptA, 5, -1, 1.5f, true),
            new(receiptB, 6, -1, 2.5f, true),
        };
        var campaign = new HeavyShieldCampaignToken("campaign", new IntPtr(7), new IntPtr(8), 9,
            HeavyShieldIdentityPhase.Loaded);

        // ---- Tick: selection entry performs nothing on Android ----
        ResetCanaries();
        HeavyShieldBanner.Tick(campaign, 123456, candidates, true);
        HeavyShieldBanner.Tick(default, 0, candidates, true);
        HeavyShieldBanner.Tick(campaign, 123456, Array.Empty<HeavyShieldBannerCandidate>(), false);
        Check(NativeReads() == 0, "Android Tick never reads the native boundary");
        Check(KingdomEnhancedPlugin.LogCalls == 0, "Android Tick logs nothing");
        Check(AllRequestsIdle(), "Android Tick establishes no binding");
        Check(ActivationWatermark() == 0, "Android Tick consumes no activation token");

        // ---- IsAssigned: normal and malicious arguments ----
        ResetCanaries();
        Check(!HeavyShieldBanner.IsAssigned(candidates[0].Receipt, candidates[0].Life),
            "nonempty receipt with positive life is never assigned on Android");
        Check(!HeavyShieldBanner.IsAssigned(Guid.Empty, 0) && !HeavyShieldBanner.IsAssigned(Guid.Empty, -7),
            "empty receipt arguments refused");
        Check(NativeReads() == 0, "IsAssigned never reads the native boundary");

        // ---- TryGetOrder: normal and malicious arguments, order stays default ----
        ResetCanaries();
        Check(!HeavyShieldBanner.TryGetOrder(candidates[0].Receipt, candidates[0].Life, out var order),
            "normal-shaped TryGetOrder returns no order on Android");
        Check(order.X == 0f && order.Facing == 0 && order.Speed == 0f && !order.Hold,
            "returned order is the default value");
        var malicious = new (Guid Receipt, long Life)[]
        {
            (Guid.Empty, 0), (Guid.Empty, long.MaxValue), (Guid.Empty, -9),
            (Guid.NewGuid(), 0), (Guid.NewGuid(), -9), (Guid.NewGuid(), long.MaxValue),
        };
        foreach (var (receipt, life) in malicious)
            Check(!HeavyShieldBanner.TryGetOrder(receipt, life, out var probe) && probe.X == 0f && !probe.Hold,
                $"malicious TryGetOrder refused (life={life})");
        Check(NativeReads() == 0, "TryGetOrder never reads the native boundary");
        Check(ActivationWatermark() == 0 && AllRequestsIdle(), "TryGetOrder binds nothing and consumes no token");

        // ---- Available: refused in every phase ----
        ResetCanaries();
        var available = StaticMethod("Available");
        foreach (HeavyShieldIdentityPhase phase in Enum.GetValues(typeof(HeavyShieldIdentityPhase)))
            Check(!(bool)available.Invoke(null, new object[] { phase }),
                "Available(" + phase + ") is false on Android");
        Check(NativeReads() == 0, "Available reads no native state");

        // ---- Activated: reflection entry stays a no-op ----
        ResetCanaries();
        var activated = StaticMethod("Activated");
        activated.Invoke(null, new object[] { new Player() });
        activated.Invoke(null, new object[] { null });
        Check(NativeReads() == 0, "Android Activated reads no player/native state");
        Check(KingdomEnhancedPlugin.LogCalls == 0, "Android Activated logs nothing");
        Check(ActivationWatermark() == 0, "Android Activated consumes no activation token");
        Check(AllRequestsIdle(), "Android Activated establishes no binding");

        // ---- Reset: managed cleanup only, repeatable ----
        ResetCanaries();
        HeavyShieldBanner.Reset();
        HeavyShieldBanner.Reset();
        Check(NativeReads() == 0, "Android Reset performs no native read");
        Check(AllRequestsIdle(), "Reset leaves every request idle");
        Check(ActivationWatermark() == 0, "Reset never advances the activation watermark");

        // ---- Real pure decisions still linked and unchanged ----
        Check(HeavyShieldBannerRules.Speed(2f) == 2f && HeavyShieldBannerRules.Speed(0f) == 0f
            && HeavyShieldBannerRules.Speed(-1f) == 0f && HeavyShieldBannerRules.Speed(float.NaN) == 0f
            && HeavyShieldBannerRules.Speed(float.PositiveInfinity) == 0f, "pure speed rule intact");
        long counter = 0;
        Check(HeavyShieldBannerRules.TryNextActivation(ref counter, out var first) && first == 1
            && HeavyShieldBannerRules.TryNextActivation(ref counter, out var second) && second == 2,
            "activation watermark is monotonic from one");
        counter = long.MaxValue;
        Check(!HeavyShieldBannerRules.TryNextActivation(ref counter, out _), "activation watermark stops at the ceiling");
        Check(HeavyShieldBannerRules.FrontSlot(4, -1, -1) == 3 && HeavyShieldBannerRules.FrontSlot(4, 2, 1) == 2
            && HeavyShieldBannerRules.FrontSlot(4, -1, 1) == 1 && HeavyShieldBannerRules.FrontSlot(0, -1, -1) == -1
            && HeavyShieldBannerRules.FrontSlot(4, 5, -1) == -1, "pure front-slot selection intact");
        Check(HeavyShieldBannerSelection.TrySelect(candidates, -1, 1.6f, out var nearest) && nearest.Life == 5
            && nearest.Receipt == receiptA, "pure selection still picks the nearest eligible candidate");
        Check(HeavyShieldBannerSelection.TrySelect(candidates, -1, 2f, out var tied) && tied.Receipt == receiptA
            && tied.Life == 5, "equidistant tie-break still prefers the smaller receipt");
        var request = new HeavyShieldBannerRequest();
        request.Begin(1, 11, 12, -1, 1.6f);
        request.Select(candidates, Guid.Empty, 0);
        Check(request.Receipt == receiptA && request.Life == 5 && request.Pending == false,
            "real request binding still assigns the selected life");
        request.End();
        Check(request.Receipt == Guid.Empty && request.Activation == 0, "real request End stays idle");

        // ---- Positive controls: the canaries themselves work ----
        ResetCanaries();
        bool threw = false;
        try { _ = new Player().Pointer; } catch (InvalidOperationException) { threw = true; }
        Check(threw && Player.Reads == 1, "player canary throws and counts when actually read");
        threw = false;
        try { _ = Managers.Inst; } catch (InvalidOperationException) { threw = true; }
        Check(threw && Managers.Reads == 1, "managers canary throws and counts");
        threw = false;
        try { _ = new Formation().GetFormationPosition(0, null); } catch (InvalidOperationException) { threw = true; }
        Check(threw && Formation.Reads == 1, "formation anchor canary throws and counts");
        threw = false;
        try { _ = new FixtureArray<float>().Length; } catch (InvalidOperationException) { threw = true; }
        Check(threw && FixtureArrayReads.Total == 1,
            "shared array canary counts across generic instantiations");
        threw = false;
        try { _ = HeavyShieldIdentity.Current; } catch (InvalidOperationException) { threw = true; }
        Check(threw && HeavyShieldIdentity.Reads == 1, "identity canary throws and counts");
        threw = false;
        try { _ = HeavyShieldRuntime.Enabled; } catch (InvalidOperationException) { threw = true; }
        Check(threw && HeavyShieldRuntime.Reads == 1, "runtime canary throws and counts");
        KingdomEnhancedPlugin.Instance.LogSource.LogWarning("canary");
        Check(KingdomEnhancedPlugin.LogCalls == 1, "log canary counts a real warning");

        Console.WriteLine($"PASS {_passed} assertions / {_failed} failed "
            + "(ANDROID source-linked banner platform boundary; native fixtures canary-counted)");
        return _failed == 0 ? 0 : 1;
    }
}
