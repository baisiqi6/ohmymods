using System.Collections;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

internal static class RuntimeLifecycleRegression
{
    private static IDictionary Entries => (IDictionary)typeof(HeavyShieldRuntime)
        .GetField("Entries", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    private static HeavyShieldMaintenancePolicy Policy => (HeavyShieldMaintenancePolicy)typeof(HeavyShieldRepair)
        .GetField("Policy", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    private static void Tick(Fixture f, float dt)
    {
        Time.time += dt; Time.deltaTime = dt; Time.frameCount++;
        HeavyShieldActorVisuals.Tick(f.Handle.GoId); HeavyShieldRuntime.Tick();
    }

    internal static void Run(Action<bool, string> check, string only = null)
    {
        if (only != "uncertain")
        using (var f = new Fixture())
        {
            check(f.Attach(new(40, false, false)), "fresh-context escort fixture attaches");
            // Native Activate has already produced a current-world order before the
            // Mod's first Tick. Verify the real movement consumer preserves that order.
            typeof(HeavyShieldRuntime).GetField("_activityWorld", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, 0L);
            using var nativeBanner = new NativeBannerFixture(9);
            Tick(f, .25f);
            check(f.Archer._mover._goalPosition == 9, "first context Tick preserves already-captured current banner destination");
            HeavyShieldBanner.Reset();
        }
        if (only == "capture") return;
        if (only != "uncertain")
        {
            foreach (bool replaceSession in new[] { false, true })
            {
                using var old = new Fixture();
                check(old.Attach(new(40, false, false)), "old pending context carrier attaches");
                using var oldBanner = new NativeBannerFixture(9);
                old.Dispose(); // A pending flag has not yet selected any career.
                if (replaceSession)
                    HeavyShieldIdentity.Current = new() { World = Managers.Inst.world.gameLayer.Pointer.ToInt64(), Land = 0 };
                using var fresh = new Fixture(land: replaceSession ? 0 : 1);
                check(fresh.Attach(new(40, false, false)), "new context carrier attaches");
                Tick(fresh, .25f);
                check(!HeavyShieldBanner.IsAssigned(fresh.Handle.Receipt, fresh.Handle.Life)
                    && fresh.Archer._mover._goalPosition < 6,
                    "old pending activation cannot recruit across " + (replaceSession ? "same-world installed Session replacement" : "land-only change"));
                fresh.Dispose();
                if (replaceSession)
                    HeavyShieldIdentity.Current = new() { World = Managers.Inst.world.gameLayer.Pointer.ToInt64(), Land = 0 };
                using var newest = new Fixture(land: replaceSession ? 0 : 2);
                check(newest.Attach(new(40, false, false)), "new-context capture carrier has its own current identity and visual stamp");
                using var newBanner = new NativeBannerFixture(12);
                Tick(newest, .25f);
                check(HeavyShieldBanner.IsAssigned(newest.Handle.Receipt, newest.Handle.Life)
                    && Math.Abs(newest.Archer._mover._goalPosition - 12) < .001,
                    "new-context actual capture survives Root's first observation");
                long activation = NativeBannerFixture.LastActivation;
                ModConfig.Enabled.Value = false; HeavyShieldRuntime.Tick();
                ModConfig.Enabled.Value = true;
                check(newest.Attach(new(40, false, false)), "ordinary nonunknown disable returns carrier normally");
                newBanner.Capture(); Tick(newest, .25f);
                check(NativeBannerFixture.LastActivation > activation,
                    "feature switch ends banner without reusing its process activation token");
            }
        }
        using (var f = new Fixture())
        {
            try
            {
                HeavyShieldRepair.Reset();
                check(f.Attach(new(20, false, false)), "unknown-result fixture attaches");
                HeavyShieldShopShell.SiteEnabled = true;
                HeavyShieldShopShell.Site = new(f.Handle.World, 1, (IntPtr)999, f.Go.transform.position.x, 0);
                Tick(f, 7);
                HeavyShieldIdentity.ThrowRepairReadback = true; Tick(f, 8);
                HeavyShieldIdentity.ThrowRepairReadback = false;
                var entry = (HeavyShieldRuntime.Entry)Entries[f.Handle.GoId];
                check(entry.RepairUncertain && Policy.Phase == HeavyShieldMaintenancePhase.HeldUnknown,
                    "controlled readback fault produces real uncertain entry and transaction barrier");
                ModConfig.Enabled.Value = false; HeavyShieldRuntime.Tick();
                check(Entries.Contains(f.Handle.GoId) && Policy.Phase == HeavyShieldMaintenancePhase.HeldUnknown,
                    "ordinary disable preserves uncertain same-owner receipt and held transaction");
                check(!HeavyShieldRuntime.DetachCarrier(f.Handle), "explicit detach cannot erase an uncertain career credential");
                ModConfig.Enabled.Value = true; HeavyShieldRuntime.Tick();
                check(entry.RepairUncertain && !HeavyShieldRuntime.IsMaintenanceEligible(entry)
                    && !HeavyShieldRuntime.IsCarrierActive(f.Handle), "reenable does not reactivate same-life uncertain repair");
                check(!f.Attach(new(40, false, false)), "same-life reattach cannot advertise uncertain carrier as active");
                // ReplaceIslandSession creates a new installed authority object on a
                // same-world successful reload while preserving native owner generation.
                HeavyShieldIdentity.Current = new() { World = f.Handle.World, Land = f.Handle.Land };
                Tick(f, .25f);
                check(Policy.Phase != HeavyShieldMaintenancePhase.HeldUnknown,
                    "installed replacement Session terminates old work even with unchanged native owner generation");
            }
            finally
            {
                HeavyShieldIdentity.ThrowRepairReadback = false; ModConfig.Enabled.Value = true;
                HeavyShieldShopShell.SiteEnabled = false;
                ++HeavyShieldIdentity.Generation; HeavyShieldRuntime.Tick();
                HeavyShieldRepair.Reset(); HeavyShieldBanner.Reset();
            }
        }
        foreach (bool observationGap in new[] { false, true })
        using (var f = new Fixture())
        {
            try
            {
                HeavyShieldRepair.Reset(); check(f.Attach(new(20, false, false)), "native-life-end fixture attaches");
                HeavyShieldShopShell.SiteEnabled = true;
                HeavyShieldShopShell.Site = new(f.Handle.World, 1, (IntPtr)999, f.Go.transform.position.x, 0);
                Tick(f, 7); HeavyShieldIdentity.ThrowRepairReadback = true; Tick(f, 8);
                HeavyShieldIdentity.ThrowRepairReadback = false;
                check(Policy.Phase == HeavyShieldMaintenancePhase.HeldUnknown, "life-end test starts with real held completion");
                HeavyShieldIntegration.BeforePoolDespawn(f.Go, 0);
                check(Entries.Contains(f.Handle.GoId) && Policy.Phase == HeavyShieldMaintenancePhase.HeldUnknown,
                    "pool BEFORE notification alone cannot erase unknown credential");
                check(!f.Attach(new(40, false, false)), "same active life cannot reacquire after pool notification");
                var marker = f.Go.GetComponent<CombatTargetLifeMarker>();
                long oldNativeLife = marker.Life;
                if (observationGap)
                {
                    marker.enabled = false; marker.enabled = true;
                    check(!marker.Observing, "component-only disable creates actual marker observation gap");
                }
                f.Go.SetActive(false); // Executes the actual production CombatTargetLifeMarker.OnDisable.
                HeavyShieldIntegration.AfterPoolDespawn(f.Go, 0);
                if (observationGap)
                {
                    check(marker.Observing && marker.Life == 0 && marker.LastObservedLife == 0,
                        "inactive after observation gap has no exact old-life receipt");
                    check(Entries.Contains(f.Handle.GoId) && Policy.Phase == HeavyShieldMaintenancePhase.HeldUnknown,
                        "ended=true without original-life proof cannot erase unknown repair");
                    f.Go.SetActive(true);
                    check(marker.Observing && marker.Life > 0 && marker.Life != oldNativeLife,
                        "complete subsequent enable establishes an actual positive replacement life");
                    var writes = (f.Archer.EnabledWrites, f.Archer.Embarkee.EnabledWrites,
                        f.Archer._spriteRenderer.EnabledWrites, f.Archer._spriteRenderer.VisibilityWrites,
                        f.Archer._mover.CommandWrites);
                    var mover = new HeavyShieldRuntime.MoverFields(f.Archer._mover);
                    var native = new HeavyShieldRuntime.NativeFields(f.Archer);
                    HeavyShieldRuntime.Tick();
                    check(writes == (f.Archer.EnabledWrites, f.Archer.Embarkee.EnabledWrites,
                        f.Archer._spriteRenderer.EnabledWrites, f.Archer._spriteRenderer.VisibilityWrites,
                        f.Archer._mover.CommandWrites) && mover.Matches(f.Archer._mover)
                        && native.Equals(new HeavyShieldRuntime.NativeFields(f.Archer)),
                        "replacement-life cleanup drops only the old managed credential without native writes");
                }
                check(!Entries.Contains(f.Handle.GoId) && Policy.Phase != HeavyShieldMaintenancePhase.HeldUnknown,
                    "complete inactive old-life proof ends only its transient unknown repair scope");
                check(HeavyShieldIdentity.Unresolved.Contains(f.Handle.Receipt),
                    "ending transient work never releases the unproven paid career's occupancy");
            }
            finally
            {
                HeavyShieldIdentity.ThrowRepairReadback = false; HeavyShieldShopShell.SiteEnabled = false;
                HeavyShieldRepair.Reset(); HeavyShieldBanner.Reset();
            }
        }
    }
}
