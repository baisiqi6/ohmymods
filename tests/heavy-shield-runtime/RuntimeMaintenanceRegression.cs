using System.Collections;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

// Executes production Runtime, Repair, MaintenancePolicy and Integration against the
// controlled native boundary. It does not execute Unity physics or prove native callbacks.
internal static class RuntimeMaintenanceRegression
{
    private static HeavyShieldMaintenancePolicy Policy => (HeavyShieldMaintenancePolicy)
        typeof(HeavyShieldRepair).GetField("Policy", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static HeavyShieldRuntime.Entry Entry(Fixture f) => (HeavyShieldRuntime.Entry)
        ((IDictionary)typeof(HeavyShieldRuntime).GetField("Entries", BindingFlags.Static | BindingFlags.NonPublic)
            .GetValue(null))[f.Handle.GoId];
    private static void Tick(Fixture f, float delta = .25f)
    {
        Time.time += delta; Time.deltaTime = delta; Time.frameCount++; Time.unscaledTimeAsDouble += delta;
        HeavyShieldActorVisuals.Tick(f.Handle.GoId); HeavyShieldRuntime.Tick();
    }
    private static GameObject EnemyAt(float x, bool dead = false)
    {
        var go = new GameObject("maintenance-enemy") { layer = 16 };
        go.transform.SetParent(Managers.Inst.world.gameLayer, false); go.transform.position = new(x, 0);
        go.AddComponent<Troll>().Type = EnemyType.TrollWeak; go.AddComponent<Damageable>().isDead = dead;
        return go;
    }
    private static void Shop(Fixture f, float x)
    {
        HeavyShieldShopShell.Site = new(f.Handle.World, 1, (IntPtr)999, x, 0);
        HeavyShieldShopShell.SiteEnabled = true;
    }

    internal static void Run(Action<bool, string> check)
    {
        try
        {
            // The source scanner retains trailing storage. Its returned count, not buffer
            // length, must control the real targeting path after an enemy disappears.
            using (var f = new Fixture())
            {
                check(f.Attach(new(40, false, false)), "tail test attaches");
                for (int i = 0; i < 8; i++) Tick(f);
                var far = EnemyAt(20); var stale = EnemyAt(5.2f);
                var scanner = f.Archer._enemyScanner;
                scanner.Rows = new[] { far, stale }; scanner.Refresh(true);
                scanner.Rows = new[] { far }; Tick(f);
                check(Entry(f).EnemyCount == 1 && scanner._filtered[1] == stale,
                    "effective prefix is one while stale storage is deliberately retained");
                HeavyShieldActorVisuals.TryPose(f.Archer, f.Handle.Life, out var action, out _);
                check(action != HeavyShieldAction.Bash, "stale nearby enemy cannot start a bash");
                for (int i = 0; i < 4; i++) Tick(f);
                check(stale.GetComponent<Damageable>().NativeHits == 0, "stale tail receives no bash damage");
                UnityEngine.Object.Destroy(far); UnityEngine.Object.Destroy(stale);
            }
            using (var f = new Fixture())
            {
                check(f.Attach(new(20, false, false)), "safety fixture attaches"); Shop(f, f.Go.transform.position.x);
                var scanner = f.Archer._enemyScanner;
                scanner._tags = Array.Empty<string>(); Tick(f, 7);
                check(!Entry(f).ThreatKnown && !HeavyShieldRepair.IsQueued(f.Handle.Receipt, f.Handle.Life),
                    "empty tag filter is not proof that the scene is empty");
                scanner._tags = null;
                var bodies = Enumerable.Range(0, 12).Select(_ => EnemyAt(20, true)).ToArray();
                scanner.Rows = bodies; Tick(f, 7);
                check(!Entry(f).ThreatKnown && Policy.Phase == HeavyShieldMaintenancePhase.Idle,
                    "twelve corpses saturating overlap cannot hide a living enemy and authorize work");
                scanner.Rows = Array.Empty<GameObject>(); foreach (var body in bodies) UnityEngine.Object.Destroy(body);
                scanner.rangeBehind = 1; Tick(f, 7);
                check(!Entry(f).ThreatKnown, "missing rear coverage blocks repair");
                scanner.rangeBehind = 6; Tick(f);
                check(Entry(f).ThreatKnown && !Entry(f).Threatened, "complete fresh empty local observation is usable");
                var behind = EnemyAt(-2); behind.GetComponent<Troll>().Type = EnemyType.Boss;
                scanner.Rows = new[] { behind }; Tick(f, 7);
                check(Entry(f).Threatened && Policy.Phase == HeavyShieldMaintenancePhase.Idle,
                    "rear nonordinary enemy prevents smith work");
                scanner.Rows = Array.Empty<GameObject>(); UnityEngine.Object.Destroy(behind); Tick(f);
                double last = Entry(f).LastThreatAt;
                HeavyShieldRuntime.ObserveIncomingDamage(f.Damage, 0);
                check(Entry(f).LastThreatAt == last, "zero damage ping does not restart quiet interval");
                HeavyShieldRuntime.ObserveIncomingDamage(f.Damage, 1);
                check(Entry(f).LastThreatAt == Time.time && Entry(f).Threatened,
                    "positive unblocked damage request interrupts repair safety");
                Tick(f, 6.1f); check(Policy.Phase == HeavyShieldMaintenancePhase.Repairing, "arrival after quiet interval starts work");
                Tick(f, 3); check(Math.Abs(Policy.WorkedSeconds - 3) < .001, "three actual seconds accumulate");
                Time.timeScale = 0; Tick(f, 0);
                check(Policy.Phase == HeavyShieldMaintenancePhase.Repairing && Policy.WorkedSeconds == 3,
                    "pause keeps the valid career and current work");
                Time.timeScale = 1; IslandSaveData.isSavingGame = true; Tick(f, 20);
                check(Policy.WorkedSeconds == 3 && Entry(f).Policy.Durability == 20, "save interval cannot grant repair");
                IslandSaveData.isSavingGame = false;
                HeavyShieldIdentity.CurrentPhase = HeavyShieldIdentityPhase.Staged; Tick(f, 0);
                check(Policy.WorkedSeconds == 3, "autosave phase change is not owner teardown");
                Tick(f, 4.9f); check(Entry(f).Policy.Durability == 20, "before eight work seconds shield remains worn");
                Tick(f, .2f); check(Entry(f).Policy.Durability == 40 && Policy.Phase == HeavyShieldMaintenancePhase.Idle,
                    "complete safe in-shop work reaches checked repair commit");
                check(((IDictionary)typeof(HeavyShieldMaintenancePolicy).GetField("_accepted", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(Policy)).Count == 0, "synchronous authoritative full-state confirmation clears accepted history");
                check(!HeavyShieldRuntime.TryCompleteRepair(new(1, f.Handle.Receipt, f.Handle.Life, f.Handle.World, 1, 20),
                    HeavyShieldShopShell.Site, out bool uncertain) && !uncertain, "outside current completing scope cannot replay a value receipt");
            }
            HeavyShieldRepair.Reset();
            using (var f = new Fixture())
            {
                check(f.Attach(new(20, false, false)), "return-home fixture attaches"); Shop(f, 1);
                Tick(f, 6.1f);
                check(f.Archer._mover._goalPosition == 1 && Entry(f).Facing == -1, "right-side soldier faces inward to travel to smith");
                HeavyShieldShopShell.SiteEnabled = false; Tick(f);
                check(Entry(f).Facing == 1 && f.Archer._mover._goalPosition > 0,
                    "withdrawn shop sends inward-facing soldier back to its original right defense");
                using var nativeBanner = new NativeBannerFixture(9); nativeBanner.Dirty = true; Tick(f);
                check(f.Archer._mover._goalPosition == f.Go.transform.position.x && f.Archer._mover._goalSpeed == 0,
                    "dirty formation Hold freezes in place instead of walking toward placeholder zero");
            }
            HeavyShieldPersistence.PrepareFinalizerObserver = () =>
            {
                check(HeavyShieldIntegration.SerializationInProgress, "prepare finalizer stays masked after persistence pops its scope");
                throw new InvalidOperationException("controlled native readback failure");
            };
            HeavyShieldIntegration.EndPrepare(new HeavyShieldPersistence.PrepareCapture(), true);
            check(!HeavyShieldIntegration.SerializationInProgress, "exceptional prepare finalizer releases outer mask exactly once");
        }
        finally
        {
            Time.timeScale = 1; IslandSaveData.isSavingGame = false;
            HeavyShieldPersistence.PrepareFinalizerObserver = null; HeavyShieldPersistence.SerializationInProgress = false;
            HeavyShieldIdentity.CurrentPhase = HeavyShieldIdentityPhase.Loaded;
            HeavyShieldShopShell.SiteEnabled = false; HeavyShieldRepair.Reset(); HeavyShieldBanner.Reset();
        }
    }
}
