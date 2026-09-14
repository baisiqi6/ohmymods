// Tests.cs — black-box regression for SiegeAmmoCounts.
// The production file is linked UNMODIFIED (../../il2cpp/SiegeAmmoCounts.cs); every game
// API it touches is stubbed in Stubs.cs. Cases drive the public contract
// (Refresh/IsReady/ClientUnavailable/Version/Count/GetPayables/Classify/CountAt) and the
// single native hook pair, asserting observable outcomes: native read counts, published
// counts, cached target identity and fail-closed behaviour — never the internal algorithm.
using System;
using System.Collections.Generic;
using System.Reflection;
using KingdomEnhancedMod;

namespace SiegeAmmoTests
{
    internal static class Program
    {
        internal static int Main()
        {
            Run("shared_catapult_counted_once_but_local_targets_keep_coverage", () =>
            {
                var e = new Env(); var a = e.MakeSite("a"); var b = e.MakeSite("b");
                a.Catapult.queuedOilBarrels = 4; b.Counterpart.catapult = a.Catapult;
                Ok(e.Refresh(10f), "shared native vehicle ready");
                Eq(SiegeAmmoCounts.Count(6), 4, "global catapult inventory deduplicated");
                Eq(SiegeAmmoCounts.CountAt(a.Payable), 4, "first local target");
                Eq(SiegeAmmoCounts.CountAt(b.Payable), 4, "second local target");
            });
            Run("missing_counterpart_defers_without_publishing_low_inventory", () =>
            {
                var e = new Env(); var a = e.MakeSite("a"); e.MakeBarrel(a);
                Ok(e.Refresh(10f), "one transport barrel known");
                a.Payable.catapultCounterpart = null;
                Eq(e.Refresh(11f, true), false, "lost binding cannot publish zero");
                Eq(e.Refresh(12f, true), false, "rebuild must retain pending target");
                a.Payable.catapultCounterpart = a.Counterpart;
                Ok(e.Refresh(13f), "restored binding recovers without new register event");
                Eq(SiegeAmmoCounts.Count(6), 1, "retained transport stock");
            });
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

            Run("empty_island_ready_true_count_zero", Tests.EmptyIslandReady);
            Run("disabled_clears_counts_and_reads_nothing", Tests.DisabledClears);
            Run("steady_state_no_registry_scan_and_05s_ammo_window", Tests.SteadyStateThrottle);
            Run("force_rereads_ammo_immediately_without_rescan", Tests.ForceImmediate);
            Run("force_never_rescans_registry_while_clean", Tests.ForceNoRescan);
            Run("transport_barrel_counted_once", Tests.TransportCountedOnce);
            Run("transport_to_queue_handoff_no_double_count", Tests.TransportQueueHandoff);
            Run("loaded_barrel_counted_once", Tests.LoadedCountedOnce);
            Run("loaded_counts_before_release", Tests.LoadedCountsBeforeRelease);
            Run("spoon_identity_and_oil_component_required", Tests.SpoonIdentityAndOilComponent);
            Run("buff_toggle_does_not_change_loaded_count", Tests.BuffTogglesLoadedCount);
            Run("delivered_transport_window", Tests.DeliveredTransportWindow);
            Run("firetower_counts_jars_only", Tests.FireTowerJarsOnly);
            Run("firetower_release_decrements", Tests.FireTowerRelease);
            Run("firetower_full_still_counts", Tests.TowerFullStillCounts);
            Run("foreign_owner_and_shop_not_counted", Tests.ForeignOwnerIgnored);
            Run("tower_canonical_component_identity", Tests.TowerCanonicalIdentity);
            Run("uninitialized_owner_defers_then_admits", Tests.UninitializedOwnerDefers);
            Run("tower_wrong_owner_or_inactive_not_listed", Tests.TowerWrongOwnerOrInactive);
            Run("multi_site_transport_ptr_dedup", Tests.MultiSiteDedup);
            Run("cross_site_stale_reference_keeps_true_owner", Tests.CrossSiteStaleReference);
            Run("inactive_or_foreign_scene_barrel_excluded", Tests.InactiveOrForeignBarrel);
            Run("site_survives_catapult_destruction_for_transport", Tests.SiteWithoutCatapult);
            Run("awake_before_parent_candidate_retained", Tests.PendingAwakeBeforeParent);
            Run("barrel_in_native_spawn_container_counted", Tests.SpawnContainerFallback);
            Run("world_change_resets_and_rebootstraps", Tests.WorldChangeResets);
            Run("client_without_authority_fail_closed_then_recovers", Tests.ClientUnavailable);
            Run("registry_exception_backoff_latch_and_event_unlock", Tests.RegistryExceptionBackoff);
            Run("native_read_exception_fail_closed_then_recovers", Tests.ReadExceptionRecovers);
            Run("pooled_identity_break_drops_then_rebuilds", Tests.IdentityBreakRecovers);
            Run("role_and_getpayables_edges", Tests.RolesAndEdges);
            Run("count_at_unknown_and_disabled_zero", Tests.CountAtEdges);
            Run("enabled_parameter_only_gating", Tests.EnabledOnly);
            Run("hooks_bound_to_payable_registry", Tests.HooksWired);

            Console.WriteLine();
            Console.WriteLine($"==== {Passed} passed, {Failed} failed, {Passed + Failed} total ====");
            foreach (string f in Failures) Console.WriteLine("FAIL: " + f);
            return Failed == 0 ? 0 : 1;
        }

        internal static int Passed, Failed;
        internal static readonly List<string> Failures = new List<string>();

        internal static void Run(string name, Action body)
        {
            try { body(); Passed++; Console.WriteLine("PASS " + name); }
            catch (Exception e)
            {
                Failed++;
                Failures.Add(name + " -> " + e.Message);
                Console.WriteLine("FAIL " + name + " : " + e.Message);
            }
        }

        internal static void Ok(bool condition, string message)
        {
            if (!condition) throw new Exception("assert: " + message);
        }

        internal static void Eq<T>(T actual, T expected, string message)
        {
            if (!Equals(actual, expected))
                throw new Exception($"{message}: got '{actual}', want '{expected}'");
        }
    }

    internal sealed class Site
    {
        public UnityEngine.GameObject Go;
        public PayableWorkshopBarrel Payable;
        public PayableWorkshop Counterpart;
        public Catapult Catapult;
        public UnityEngine.GameObject CatapultGo;
    }

    internal sealed class TowerFixture
    {
        public UnityEngine.GameObject Go;
        public FireTower Tower;
        public PayableComponent Payable;
    }

    /// <summary>World fixture: current layer/scene/registry plus native-like object builders.</summary>
    internal sealed class Env
    {
        private static long _ptr = 0x500000;
        internal static IntPtr NewPtr() => (IntPtr)(_ptr += 0x80);

        public Managers M = new Managers();
        public Kingdom Kingdom;
        public World World;
        public UnityEngine.GameObject Root;      // 世界层之外的场景根
        public UnityEngine.GameObject LayerGo;
        public UnityEngine.Transform Layer;
        public PayableManager Registry = new PayableManager();
        public int Scene = 1;

        public Env()
        {
            SiegeAmmoCounts.Refresh(null, false, 0f);   // 每个用例从空缓存开始（禁用即清）
            NetworkBigBoss.HasWorldAuth = true;
            Build(1);
        }

        private void Build(int scene)
        {
            Scene = scene;
            Root = Fabric.NewGo("root" + scene, null, scene);
            LayerGo = Fabric.NewGo("layer" + scene, Root.transform, scene);
            Layer = LayerGo.transform;
            World = new World { gameLayer = Layer, Pointer = NewPtr() };
            Kingdom = new Kingdom { Pointer = NewPtr() };
            Registry = new PayableManager();
            M.world = World;
            M.kingdom = Kingdom;
            M.game = new Game();
            M.payables = Registry;
        }

        public bool Refresh(float now, bool force = false)
            => SiegeAmmoCounts.Refresh(M, true, now, force);

        public bool RefreshDisabled()
            => SiegeAmmoCounts.Refresh(M, false, 0f);

        /// <summary>换岛：全新层/场景/世界/王国/注册表（旧世界对象不再属于当前上下文）。</summary>
        public void ReplaceWorld(int scene) => Build(scene);

        public Site MakeSite(string name = "site", bool register = true, bool underLayer = true)
        {
            UnityEngine.Transform parent = underLayer ? Layer : Root.transform;
            UnityEngine.GameObject go = Fabric.NewGo(name, parent, Scene);
            PayableWorkshopBarrel payable = Fabric.Attach(go, new PayableWorkshopBarrel());

            UnityEngine.GameObject workshopGo = Fabric.NewGo(name + "-workshop", parent, Scene);
            PayableWorkshop counterpart = Fabric.Attach(workshopGo, new PayableWorkshop());
            payable.catapultCounterpart = counterpart;

            var site = new Site { Go = go, Payable = payable, Counterpart = counterpart };
            site.Catapult = BuildCatapult(site);
            if (register) Registry.AddPayable(payable);
            return site;
        }

        public Catapult BuildCatapult(Site site)
        {
            UnityEngine.GameObject go = Fabric.NewGo(site.Go.name + "-catapult", Layer, Scene);
            Catapult catapult = Fabric.Attach(go, new Catapult());
            catapult.spoon = Fabric.NewGo(site.Go.name + "-spoon", go.transform, Scene).transform;
            site.Counterpart.catapult = catapult;
            site.Catapult = catapult;
            site.CatapultGo = go;
            return catapult;
        }

        public RollableOilBarrel MakeBarrel(Site site, bool underLayer = true, bool active = true, int scene = -1)
        {
            UnityEngine.Transform parent = underLayer ? Layer : Root.transform;
            UnityEngine.GameObject go = Fabric.NewGo("barrel", parent, scene < 0 ? Scene : scene);
            go.Active = active;
            RollableOilBarrel barrel = Fabric.Attach(go, new RollableOilBarrel());
            barrel._parentWorkshop = site.Payable;
            barrel.targetCatapult = site.Catapult;
            site.Payable._activeBarrels.Add(barrel);
            return barrel;
        }

        /// <summary>原生 Pool.Spawn 的 parent：payable.transform.parent.parent。</summary>
        public RollableOilBarrel MakeBarrelInSpawnContainer(Site site)
            => MakeBarrelUnder(site, site.Go.transform.parent.parent);

        public RollableOilBarrel MakeBarrelUnder(Site site, UnityEngine.Transform parent)
        {
            UnityEngine.GameObject go = Fabric.NewGo("barrel-other", parent, Scene);
            RollableOilBarrel barrel = Fabric.Attach(go, new RollableOilBarrel());
            barrel._parentWorkshop = site.Payable;
            barrel.targetCatapult = site.Catapult;
            site.Payable._activeBarrels.Add(barrel);
            return barrel;
        }

        /// <summary>CreateProjectile：queued--、currentLaunchableIsOil、spoon 下带 OilBarrel 的弹体。</summary>
        public UnityEngine.GameObject LoadOil(Site site)
        {
            site.Catapult.queuedOilBarrels = Math.Max(0, site.Catapult.QueuedValue - 1);
            site.Catapult.currentLaunchableIsOil = true;
            UnityEngine.GameObject projectile = Fabric.NewGo("loaded-barrel", site.Catapult.spoon, Scene);
            Fabric.Attach(projectile, new OilBarrel());
            site.Catapult._projectile = projectile;
            return projectile;
        }

        /// <summary>spoon 上没有 OilBarrel 标记的弹体（巨石）。</summary>
        public UnityEngine.GameObject LoadBoulder(Site site)
        {
            UnityEngine.GameObject projectile = Fabric.NewGo("boulder", site.Catapult.spoon, Scene);
            site.Catapult._projectile = projectile;
            return projectile;
        }

        /// <summary>ReleaseProjectileFromAnimation：弹体离开 spoon 并发射。</summary>
        public void ReleaseOil(Site site)
        {
            if (site.Catapult._projectile != null)
                site.Catapult._projectile.transform.parent = Layer;
            site.Catapult._projectile = null;
            site.Catapult.currentLaunchableIsOil = false;
        }

        public TowerFixture MakeTower(string name = "tower", bool register = true, int jars = 0, bool initializeOwner = true)
        {
            UnityEngine.GameObject go = Fabric.NewGo(name, Layer, Scene);
            FireTower tower = Fabric.Attach(go, new FireTower());
            PayableComponent payable = Fabric.Attach(go, new PayableComponent());
            tower._payableComponent = payable;
            tower._fireJarsActiveNum = jars;
            if (initializeOwner) payable._owner = tower;
            if (register) Registry.AddPayable(payable);
            return new TowerFixture { Go = go, Tower = tower, Payable = payable };
        }
    }

    internal static class Tests
    {
        private static int Count(int role) => SiegeAmmoCounts.Count(role);
        private static bool Ready => SiegeAmmoCounts.IsReady;

        private static List<Payable> Payables(int role)
        {
            var list = new List<Payable>();
            SiegeAmmoCounts.GetPayables(role, list);
            return list;
        }

        private sealed class OtherOwner : IPayableComponentOwner
        {
            public IntPtr Pointer { get; } = Env.NewPtr();
        }

        // ------------------------------------------------------------ bootstrap

        internal static void EmptyIslandReady()
        {
            var e = new Env();
            Program.Ok(e.Refresh(10f), "empty registry still publishes a ready snapshot");
            Program.Ok(Ready, "ready with zero siege weapons (不伪报未就绪)");
            Program.Eq(Count(6), 0, "role6 zero");
            Program.Eq(Count(7), 0, "role7 zero");
            Program.Eq(Payables(6).Count, 0, "no barrel targets");
            Program.Eq(Payables(7).Count, 0, "no tower targets");
            Program.Eq(e.Registry.Reads, 1, "one bootstrap registry read");
        }

        internal static void DisabledClears()
        {
            var e = new Env();
            Site s = e.MakeSite();
            s.Catapult.queuedOilBarrels = 4;
            Program.Ok(e.Refresh(10f), "bootstrap");
            Program.Eq(Count(6), 4, "queued ammo counted");

            int reads = e.Registry.Reads;
            Program.Ok(!e.RefreshDisabled(), "disabled refresh returns false");
            Program.Ok(!Ready, "disabled clears readiness");
            Program.Eq(Count(6), 0, "disabled clears counts");
            Program.Eq(Payables(6).Count, 0, "disabled clears registrations");
            Program.Eq(e.Registry.Reads, reads, "disabled reads nothing");
        }

        internal static void SteadyStateThrottle()
        {
            var e = new Env();
            Site s = e.MakeSite();
            s.Catapult.queuedOilBarrels = 3;
            Program.Ok(e.Refresh(10f), "bootstrap");
            Program.Eq(Count(6), 3, "queued ammo");
            Program.Eq(e.Registry.Reads, 1, "one bootstrap read");

            int queuedReads = s.Catapult.QueuedReads;
            for (int i = 0; i < 50; i++) e.Refresh(10f + i * 0.002f);
            Program.Eq(e.Registry.Reads, 1, "steady state must not rescan AllPayables");
            Program.Eq(s.Catapult.QueuedReads, queuedReads, "ammo fields only in the 0.5s window");

            s.Catapult.queuedOilBarrels = 5;
            Program.Ok(e.Refresh(10.6f), "next window publishes");
            Program.Eq(Count(6), 5, "reread value");
            Program.Ok(s.Catapult.QueuedReads > queuedReads, "0.5s window rereads ammo fields");
        }

        internal static void ForceImmediate()
        {
            var e = new Env();
            Site s = e.MakeSite();
            s.Catapult.queuedOilBarrels = 3;
            Program.Ok(e.Refresh(10f), "bootstrap");
            Program.Ok(e.Refresh(10.1f), "throttled call stays ready");
            s.Catapult.queuedOilBarrels = 7;
            int queuedReads = s.Catapult.QueuedReads;

            Program.Ok(e.Refresh(10.2f, force: true), "force publishes immediately");
            Program.Eq(Count(6), 7, "final-commit forced reread sees new stock");
            Program.Ok(s.Catapult.QueuedReads > queuedReads, "force read the native field");

            int after = s.Catapult.QueuedReads;
            Program.Ok(e.Refresh(10.21f), "same frame without force");
            Program.Eq(s.Catapult.QueuedReads, after, "throttle still applies to plain calls");
        }

        internal static void ForceNoRescan()
        {
            var e = new Env();
            Site s = e.MakeSite();
            s.Catapult.queuedOilBarrels = 3;
            Program.Ok(e.Refresh(10f), "bootstrap");
            for (int i = 0; i < 100; i++) Program.Ok(e.Refresh(10f, force: true), "force refresh");
            Program.Eq(e.Registry.Reads, 1, "force must never rescan AllPayables while clean");
            Program.Eq(Count(6), 3, "counts stable");
        }

        // ------------------------------------------------------------ role6 ammo

        internal static void TransportCountedOnce()
        {
            var e = new Env();
            Site s = e.MakeSite("s1");
            RollableOilBarrel barrel = e.MakeBarrel(s);
            Program.Ok(e.Refresh(10f), "bootstrap");
            Program.Eq(Count(6), 1, "transport barrel counted once");
            Program.Eq(SiegeAmmoCounts.CountAt(s.Payable), 1, "site-local stock");
            List<Payable> cached = Payables(6);
            Program.Eq(cached.Count, 1, "barrel target cached");
            Program.Ok(ReferenceEquals(cached[0], s.Payable), "cached target identity");
            Program.Ok(barrel.gameObject.activeInHierarchy, "fixture barrel active");
        }

        internal static void TransportQueueHandoff()
        {
            var e = new Env();
            Site s = e.MakeSite();
            RollableOilBarrel barrel = e.MakeBarrel(s);
            Program.Ok(e.Refresh(10f), "transport visible");
            Program.Eq(Count(6), 1, "transport counted");

            // native DelayDespawn: QueueOilBarrel() then OnBarrelComplete()/despawn, same frame
            s.Payable._activeBarrels.Remove(barrel);
            s.Catapult.queuedOilBarrels = 1;
            Program.Ok(e.Refresh(10.6f), "queue visible");
            Program.Eq(Count(6), 1, "hand-off must not double count");
            Program.Eq(SiegeAmmoCounts.CountAt(s.Payable), 1, "local stock after hand-off");
        }

        internal static void LoadedCountedOnce()
        {
            var e = new Env();
            Site s = e.MakeSite();
            e.LoadOil(s);
            Program.Ok(e.Refresh(10f), "bootstrap");
            Program.Eq(Count(6), 1, "loaded barrel counted once");
            Program.Eq(SiegeAmmoCounts.CountAt(s.Payable), 1, "local loaded stock");
        }

        internal static void LoadedCountsBeforeRelease()
        {
            var e = new Env();
            Site s = e.MakeSite();
            e.LoadOil(s);
            s.Catapult.currentLaunchableIsOil = false;   // Catapult.Fire 在发射动画开始就清它
            Program.Ok(e.Refresh(10f), "firing animation started");
            Program.Eq(Count(6), 1, "spoon-mounted OilBarrel still counts before actual release");

            e.ReleaseOil(s);                             // ReleaseProjectileFromAnimation
            Program.Ok(e.Refresh(10.6f), "released");
            Program.Eq(Count(6), 0, "released projectile is no longer unused ammunition");
        }

        internal static void SpoonIdentityAndOilComponent()
        {
            var e = new Env();
            Site s = e.MakeSite();
            e.LoadBoulder(s);                            // spoon 上是巨石：没有 OilBarrel 组件
            Program.Ok(e.Refresh(10f), "boulder loaded");
            Program.Eq(Count(6), 0, "non-oil projectile is not oil ammo");

            UnityEngine.GameObject projectile = e.LoadOil(s);
            projectile.transform.parent = e.Layer;       // 已离开 spoon（发射途中 / 未清字段）
            Program.Ok(e.Refresh(10.6f), "left the spoon");
            Program.Eq(Count(6), 0, "projectile that left the spoon is not stock");
        }

        internal static void BuffTogglesLoadedCount()
        {
            var e = new Env();
            Site s = e.MakeSite();
            s.Catapult._fireAttacks = true;              // 永久 buff 期间的一发同样真实存在
            s.Catapult.queuedOilBarrels = 2;
            e.LoadOil(s);
            Program.Ok(e.Refresh(10f), "buff active");
            Program.Eq(Count(6), 2, "queue + spoon barrel are real regardless of the buff");
            Program.Eq(SiegeAmmoCounts.CountAt(s.Payable), 2, "local stock with buff");

            s.Catapult._fireAttacks = false;
            Program.Ok(e.Refresh(10.6f), "buff gone");
            Program.Eq(Count(6), 2, "buff toggle does not change the loaded amount");
        }

        internal static void DeliveredTransportWindow()
        {
            var e = new Env();
            Site s = e.MakeSite();
            RollableOilBarrel barrel = e.MakeBarrel(s);
            barrel._delivered = true;                    // 已到达投石车，仍在原生 DelayDespawn 1 秒窗口
            Program.Ok(e.Refresh(10f), "delivered window");
            Program.Eq(Count(6), 1, "delivered-but-not-yet-queued barrel is real stock");

            barrel._delivered = false;
            s.Payable._activeBarrels.Remove(barrel);     // 原生 OnBarrelComplete
            s.Catapult.queuedOilBarrels = 1;             // 原生 QueueOilBarrel（同帧）
            Program.Ok(e.Refresh(10.6f), "queued");
            Program.Eq(Count(6), 1, "no double count across the delivery window");
        }

        internal static void MultiSiteDedup()
        {
            var e = new Env();
            Site a = e.MakeSite("a");
            Site b = e.MakeSite("b");
            RollableOilBarrel barrel = e.MakeBarrel(a);
            b.Payable._activeBarrels.Add(barrel);   // 池化复用后两边集合都残留同一 ptr
            Program.Ok(e.Refresh(10f), "bootstrap");
            Program.Eq(Count(6), 1, "one physical barrel counts once island-wide");
            Program.Eq(SiegeAmmoCounts.CountAt(a.Payable), 1, "owned by site a");
            Program.Eq(SiegeAmmoCounts.CountAt(b.Payable), 0, "foreign site b must not claim it");
        }

        internal static void CrossSiteStaleReference()
        {
            var e = new Env();
            Site a = e.MakeSite("a");
            Site b = e.MakeSite("b");
            RollableOilBarrel owned = e.MakeBarrel(b);   // 真正归属 b
            a.Payable._activeBarrels.Add(owned);         // a 的集合里残留陈旧引用（a 先遍历）
            Program.Ok(e.Refresh(10f), "bootstrap");
            Program.Eq(Count(6), 1, "owned barrel counted once");
            Program.Eq(SiegeAmmoCounts.CountAt(a.Payable), 0, "stale reference in a must not claim it");
            Program.Eq(SiegeAmmoCounts.CountAt(b.Payable), 1, "true owner b keeps it");

            UnityEngine.GameObject orphanGo = Fabric.NewGo("orphan", e.Layer, e.Scene);
            RollableOilBarrel orphan = Fabric.Attach(orphanGo, new RollableOilBarrel());
            orphan._parentWorkshop = null;               // 无站点归属
            a.Payable._activeBarrels.Add(orphan);
            Program.Ok(e.Refresh(10.6f), "orphan barrel");
            Program.Eq(Count(6), 1, "barrel without a workshop owner is not stock");
        }

        internal static void InactiveOrForeignBarrel()
        {
            var e = new Env();
            Site s = e.MakeSite();
            RollableOilBarrel pooled = e.MakeBarrel(s, active: false);   // 池化/未激活
            Program.Ok(e.Refresh(10f), "pooled barrel");
            Program.Eq(Count(6), 0, "inactive barrel excluded");

            pooled.gameObject.Active = true;
            e.MakeBarrel(s, scene: e.Scene + 1);                          // 另一场景的同名站
            Program.Ok(e.Refresh(10.6f), "scene mix");
            Program.Eq(Count(6), 1, "only the current-scene barrel counts");
        }

        internal static void SiteWithoutCatapult()
        {
            var e = new Env();
            Site s = e.MakeSite();
            e.MakeBarrel(s);                             // 运输中的桶：原生保留在 _activeBarrels
            Catapult old = s.Catapult;
            old.queuedOilBarrels = 4;
            e.LoadOil(s);                                // spoon 上装填一发（随车销毁）
            s.CatapultGo.Active = false;                 // 投石车被毁
            s.Counterpart.catapult = null;
            Program.Ok(e.Refresh(10f), "destroyed catapult");
            Program.Eq(Count(6), 1, "owned transport barrel survives the destroyed catapult");
            Program.Eq(SiegeAmmoCounts.CountAt(s.Payable), 1, "local stock is the surviving barrel");

            Catapult rebuilt = e.BuildCatapult(s);       // 重建投石车：站点无需重新注册
            rebuilt.queuedOilBarrels = 2;
            Program.Ok(e.Refresh(10.6f), "rebuilt");
            Program.Eq(Count(6), 3, "rebuilt queue plus the retained transport barrel");
            Program.Eq(e.Registry.Reads, 1, "no registry rescan for a native link change");
        }

        internal static void PendingAwakeBeforeParent()
        {
            var e = new Env();
            Site s = e.MakeSite("late", underLayer: false);   // 原生 Awake 早于 parent 挂接
            s.Catapult.queuedOilBarrels = 4;
            Program.Ok(!e.Refresh(10f), "unresolved candidate must not publish");
            Program.Ok(!Ready, "fail-closed instead of a fake zero");
            Program.Eq(Count(6), 0, "no count published");
            Program.Eq(e.Registry.Reads, 1, "bootstrap read once");

            Program.Ok(!e.Refresh(11f), "candidate retained, still unresolved");
            Program.Ok(!e.Refresh(12f), "candidate retained across windows");
            Program.Eq(e.Registry.Reads, 1, "retaining a candidate is not a scene rescan");

            s.Go.transform.parent = e.Layer;                  // 迟到挂接完成
            Program.Ok(e.Refresh(13f), "candidate admitted once attribution resolves");
            Program.Eq(Count(6), 4, "stock counted after attribution");
            Program.Ok(Ready, "ready once nothing is pending");
        }

        internal static void SpawnContainerFallback()
        {
            var e = new Env();
            Site s = e.MakeSite("s");                          // 站点是 layer 的直接子对象
            e.MakeBarrelInSpawnContainer(s);                   // 原生 Pool.Spawn parent = layer.parent
            Program.Ok(e.Refresh(10f), "native container barrel");
            Program.Eq(Count(6), 1, "barrel in the native spawn container still counts");

            UnityEngine.GameObject strayContainer = Fabric.NewGo("other-container", e.Root.transform, e.Scene);
            e.MakeBarrelUnder(s, strayContainer.transform);    // 层外且非生成容器
            Program.Ok(e.Refresh(10.6f), "unrelated container barrel");
            Program.Eq(Count(6), 1, "unrelated layer-external container is not stock");
        }

        // ------------------------------------------------------------ role7 ammo

        internal static void FireTowerJarsOnly()
        {
            var e = new Env();
            TowerFixture t = e.MakeTower(jars: 3);
            t.Tower._projectile = Fabric.NewGo("jar-preview", e.Layer, e.Scene);  // CreateProjectile 预览
            Program.Ok(e.Refresh(10f), "bootstrap");
            Program.Eq(Count(7), 3, "jars only; spoon preview never double counted");
            Program.Eq(SiegeAmmoCounts.CountAt(t.Payable), 3, "local jars");
            List<Payable> cached = Payables(7);
            Program.Eq(cached.Count, 1, "tower target cached");
            Program.Ok(ReferenceEquals(cached[0], t.Payable), "cached tower identity");
        }

        internal static void FireTowerRelease()
        {
            var e = new Env();
            TowerFixture t = e.MakeTower(jars: 3);
            Program.Ok(e.Refresh(10f), "bootstrap");
            Program.Eq(Count(7), 3, "three jars");

            t.Tower._fireJarsActiveNum = 2;      // ReleaseProjectile 才 --
            t.Tower._projectile = null;
            Program.Ok(e.Refresh(10.6f), "after release");
            Program.Eq(Count(7), 2, "release decrements real stock");
        }

        internal static void TowerFullStillCounts()
        {
            var e = new Env();
            TowerFixture t = e.MakeTower(jars: 12);
            Program.Ok(e.Refresh(10f), "full tower");
            Program.Eq(Count(7), 12, "full tower still reports real stock (CanPay is the service gate)");
            Program.Eq(t.Tower._maxFireJars, 12, "native capacity untouched");
        }

        internal static void ForeignOwnerIgnored()
        {
            var e = new Env();
            UnityEngine.GameObject go = Fabric.NewGo("other-building", e.Layer, e.Scene);
            PayableComponent component = Fabric.Attach(go, new PayableComponent());
            component._owner = new OtherOwner();
            e.Registry.AddPayable(component);
            e.Registry.AddPayable(Fabric.Attach(Fabric.NewGo("shop", e.Layer, e.Scene), new PayableShop()));

            Program.Ok(e.Refresh(10f), "bootstrap");
            Program.Eq(SiegeAmmoCounts.Classify(component), -1, "no same-GO FireTower => not role7");
            Program.Eq(SiegeAmmoCounts.Classify(null), -1, "null classify");
            Program.Eq(Count(7), 0, "foreign owner contributes nothing");
            Program.Eq(Payables(7).Count, 0, "foreign owner is not a target");
        }

        internal static void TowerCanonicalIdentity()
        {
            var e = new Env();
            TowerFixture t = e.MakeTower(jars: 2);
            PayableComponent extra = Fabric.Attach(t.Go, new PayableComponent());
            extra._owner = t.Tower;                    // 同 owner，但不是火塔持有的那份
            e.Registry.AddPayable(extra);
            Program.Ok(e.Refresh(10f), "bootstrap");
            Program.Eq(Count(7), 2, "tower jars counted exactly once");
            Program.Eq(Payables(7).Count, 1, "no duplicate target for one tower GO");
            Program.Ok(ReferenceEquals(Payables(7)[0], t.Payable), "canonical component is the target");
            Program.Eq(SiegeAmmoCounts.Classify(extra), 7, "kind of any same-GO fire tower component");

            // 火塔自己持有的那份不在注册表里（例如被禁用）：非 canonical 组件不得冒名计数
            var e2 = new Env();
            UnityEngine.GameObject go = Fabric.NewGo("tower2", e2.Layer, e2.Scene);
            FireTower tower = Fabric.Attach(go, new FireTower());
            PayableComponent canonical = Fabric.Attach(go, new PayableComponent());
            tower._payableComponent = canonical;
            tower._fireJarsActiveNum = 4;
            PayableComponent lone = Fabric.Attach(go, new PayableComponent());
            lone._owner = tower;
            e2.Registry.AddPayable(lone);
            Program.Ok(e2.Refresh(10f), "bootstrap without the canonical component");
            Program.Eq(Count(7), 0, "non-canonical component must not publish tower stock");
            Program.Eq(Payables(7).Count, 0, "no target for an unowned component");
        }

        internal static void UninitializedOwnerDefers()
        {
            var e = new Env();
            TowerFixture t = e.MakeTower(jars: 5, initializeOwner: false);
            Program.Ok(!e.Refresh(10f), "owner identity not established yet");
            Program.Ok(!Ready, "fail-closed instead of a fake zero");
            Program.Eq(Count(7), 0, "nothing published while unresolved");
            Program.Eq(SiegeAmmoCounts.Classify(t.Payable), -1, "unresolved owner is not a role7 target yet");

            t.Payable._owner = t.Tower;                // Awake/Init 迟到完成
            Program.Ok(e.Refresh(10.6f), "candidate resolves in a later window");
            Program.Eq(Count(7), 5, "jars published after resolution");
            Program.Eq(SiegeAmmoCounts.Classify(t.Payable), 7, "resolved owner classifies as role7");
        }

        internal static void TowerWrongOwnerOrInactive()
        {
            var e = new Env();
            TowerFixture a = e.MakeTower("tower-a", jars: 2);
            TowerFixture b = e.MakeTower("tower-b", jars: 3);
            b.Payable._owner = a.Tower;                // 坏 owner：指向另一座塔
            Program.Ok(e.Refresh(10f), "bootstrap");
            Program.Eq(SiegeAmmoCounts.Classify(b.Payable), -1, "wrong owner is not a role7 target");
            Program.Eq(Count(7), 2, "only the correctly owned tower counts");
            List<Payable> listed = Payables(7);
            Program.Eq(listed.Count, 1, "bad owner must not be listed as purchasable");
            Program.Ok(ReferenceEquals(listed[0], a.Payable), "listed target is the good tower");

            a.Go.Active = false;                       // 非激活塔
            Program.Ok(!e.Refresh(10.6f), "identity loss must not publish stale stock");
            Program.Ok(e.Refresh(11.2f), "bounded rebuild recovers");
            Program.Eq(SiegeAmmoCounts.Classify(a.Payable), -1, "inactive tower is not a role7 target");
            Program.Eq(Count(7), 0, "no stock from an inactive tower");
            Program.Eq(Payables(7).Count, 0, "inactive tower not listed");
        }

        // ------------------------------------------------------------ world / client

        internal static void WorldChangeResets()
        {
            var e = new Env();
            Site s1 = e.MakeSite("s1");
            s1.Catapult.queuedOilBarrels = 2;
            Program.Ok(e.Refresh(10f), "old island");
            Program.Eq(Count(6), 2, "old island stock");

            e.ReplaceWorld(2);
            Site s2 = e.MakeSite("s2");
            s2.Catapult.queuedOilBarrels = 5;
            Program.Ok(e.Refresh(11f), "new island bootstraps");
            Program.Eq(Count(6), 5, "only current-world targets counted");
            Program.Eq(SiegeAmmoCounts.CountAt(s1.Payable), 0, "old-world target gone");
            Program.Eq(e.Registry.Reads, 1, "new registry is bootstrapped exactly once");
        }

        internal static void ClientUnavailable()
        {
            var e = new Env();
            Site s = e.MakeSite();
            s.Catapult.queuedOilBarrels = 3;
            Program.Ok(e.Refresh(10f), "host bootstrap");
            Program.Eq(Count(6), 3, "host stock");

            int reads = e.Registry.Reads, queuedReads = s.Catapult.QueuedReads;
            NetworkBigBoss.HasWorldAuth = false;
            Program.Ok(!e.Refresh(10.6f), "client must not publish counts");
            Program.Ok(SiegeAmmoCounts.ClientUnavailable, "client flagged");
            Program.Ok(!Ready, "not ready on a client");
            Program.Eq(Count(6), 0, "no client-side count");
            Program.Eq(e.Registry.Reads, reads, "no registry scan without authority");
            Program.Eq(s.Catapult.QueuedReads, queuedReads, "no ammo reads without authority");

            NetworkBigBoss.HasWorldAuth = true;
            Program.Ok(e.Refresh(11.2f), "authority regained");
            Program.Ok(!SiegeAmmoCounts.ClientUnavailable, "client flag cleared");
            Program.Eq(Count(6), 3, "counts resume");
        }

        // ------------------------------------------------------------ faults

        internal static void RegistryExceptionBackoff()
        {
            var e = new Env();
            e.Registry.ThrowOnRead = true;
            KingdomEnhancedPlugin.Instance.LogSource.Warnings.Clear();

            Program.Ok(!e.Refresh(10f), "registry fault is not ready");
            Program.Ok(!Ready, "fail-closed");
            Program.Eq(e.Registry.Reads, 1, "one attempt");
            int reads = e.Registry.Reads;

            Program.Ok(!e.Refresh(10.3f, force: true), "force must not break the error backoff");
            Program.Eq(e.Registry.Reads, reads, "force inside backoff reads nothing");
            Program.Ok(!e.Refresh(11.1f), "second attempt after 1s");
            Program.Eq(e.Registry.Reads, reads + 1, "retry after backoff");
            Program.Ok(!e.Refresh(12.2f), "third failure");
            reads = e.Registry.Reads;
            Program.Ok(!e.Refresh(14f, force: true), "latched after 3 consecutive failures");
            Program.Eq(e.Registry.Reads, reads, "latched means no more registry scans");
            Program.Ok(KingdomEnhancedPlugin.Instance.LogSource.Warnings.Count <= 1, "no log spam");

            e.Registry.ThrowOnRead = false;
            SiegeAmmoCounts.HookRegistryChanged();          // 玩家触发新注册事件：解锁恢复
            Site s = e.MakeSite("after-fault");             // 该站点已在注册表内
            s.Catapult.queuedOilBarrels = 6;
            Program.Ok(e.Refresh(14.1f), "recovery unlocked by a registry event");
            Program.Eq(Count(6), 6, "counts published after recovery");
        }

        internal static void ReadExceptionRecovers()
        {
            var e = new Env();
            Site s = e.MakeSite();
            s.Payable.ThrowOnCounterpartRead = true;
            Program.Ok(!e.Refresh(10f), "native read fault must not publish");
            Program.Ok(!Ready, "fail-closed on a read fault");

            s.Payable.ThrowOnCounterpartRead = false;
            s.Catapult.queuedOilBarrels = 4;
            Program.Ok(e.Refresh(11.1f), "recovers after backoff");
            Program.Eq(Count(6), 4, "sample resumes with the real value");
        }

        internal static void IdentityBreakRecovers()
        {
            var e = new Env();
            Site s = e.MakeSite();
            s.Catapult.queuedOilBarrels = 3;
            Program.Ok(e.Refresh(10f), "bootstrap");
            Program.Eq(Count(6), 3, "stock");

            int reads = e.Registry.Reads;
            s.Go.Pointer = UnityEngine.Object.NextPointer();   // 池化复用：旧身份失效
            s.Go.InstanceId = UnityEngine.Object.NextId();
            Program.Ok(!e.Refresh(10.6f), "broken identity must not publish stale stock");
            Program.Eq(Count(6), 0, "no stale value");

            Program.Ok(e.Refresh(11.2f), "bounded rebuild recovers");
            Program.Eq(Count(6), 3, "stock re-read after the rebuild");
            Program.Eq(e.Registry.Reads, reads + 1, "exactly one rebuild for the drop");
        }

        // ------------------------------------------------------------ API edges

        internal static void RolesAndEdges()
        {
            var e = new Env();
            e.MakeSite();
            e.MakeTower(jars: 1);
            Program.Ok(e.Refresh(10f), "bootstrap");
            Program.Eq(Count(5), 0, "other role zero");
            Program.Eq(Count(0), 0, "role0 zero");
            Program.Eq(Count(8), 0, "out-of-range role zero");

            var list = new List<Payable> { new PayableShop() };
            SiegeAmmoCounts.GetPayables(5, list);
            Program.Eq(list.Count, 0, "unknown role clears the buffer");
            SiegeAmmoCounts.GetPayables(6, list);
            Program.Eq(list.Count, 1, "role6 fills from cache");
            SiegeAmmoCounts.GetPayables(7, list);
            Program.Eq(list.Count, 1, "role7 fills from cache");
            SiegeAmmoCounts.GetPayables(6, null);                 // 不得抛
            int reads = e.Registry.Reads;
            SiegeAmmoCounts.GetPayables(6, list);
            SiegeAmmoCounts.GetPayables(7, list);
            Program.Eq(e.Registry.Reads, reads, "GetPayables never scans");
        }

        internal static void CountAtEdges()
        {
            var e = new Env();
            Site s = e.MakeSite();
            s.Catapult.queuedOilBarrels = 3;
            Program.Ok(e.Refresh(10f), "bootstrap");
            Program.Eq(SiegeAmmoCounts.CountAt(null), 0, "null payable");
            Program.Eq(SiegeAmmoCounts.CountAt(new PayableShop()), 0, "unknown payable");
            Program.Eq(SiegeAmmoCounts.CountAt(s.Payable), 3, "cached local stock");

            Program.Ok(!e.RefreshDisabled(), "disable");
            Program.Eq(SiegeAmmoCounts.CountAt(s.Payable), 0, "cleared cache reports zero");
        }

        internal static void EnabledOnly()
        {
            var e = new Env();
            Site s = e.MakeSite();
            s.Catapult.queuedOilBarrels = 3;
            Program.Ok(e.Refresh(10f), "auto/HUD enabled by the caller");
            Program.Eq(Count(6), 3, "stock");
            Program.Eq(e.Registry.Reads, 1, "bootstrap");

            Program.Ok(!e.RefreshDisabled(), "HUD and auto flags both off");
            Program.Ok(!Ready, "cache released");
            Program.Ok(e.Refresh(10f), "re-enabled later");
            Program.Eq(Count(6), 3, "same world state re-read");
            Program.Eq(e.Registry.Reads, 2, "fresh bootstrap after release");
        }

        internal static void HooksWired()
        {
            var e = new Env();
            var patchTypes = new List<Type>();
            foreach (Type type in typeof(SiegeAmmoCounts).Assembly.GetTypes())
                if (type.GetCustomAttribute<HarmonyLib.HarmonyPatchAttribute>() != null) patchTypes.Add(type);
            Program.Eq(patchTypes.Count, 2, "exactly two native hook wrappers in this file");

            var postfix = new Dictionary<string, MethodInfo>();
            foreach (Type type in patchTypes)
            {
                var attr = type.GetCustomAttribute<HarmonyLib.HarmonyPatchAttribute>();
                Program.Eq(attr.TargetType, typeof(PayableManager), "hook target type");
                MethodInfo method = type.GetMethod("Postfix",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                Program.Ok(method != null, "postfix method present");
                Program.Ok(method.GetCustomAttribute<HarmonyLib.HarmonyPostfixAttribute>() != null, "postfix attribute");
                postfix[attr.MethodName] = method;
            }
            Program.Ok(postfix.ContainsKey("AddPayable") && postfix.ContainsKey("RemovePayable"),
                "hooks bound to AddPayable/RemovePayable");

            // 直改注册表（无原生事件）：只有真实 Postfix 能让下一轮刷新看到成员变化
            Site s = e.MakeSite("hooked", register: false);
            s.Catapult.queuedOilBarrels = 6;
            Program.Ok(e.Refresh(10f), "bootstrap with an empty registry");
            Program.Eq(Count(6), 0, "not registered yet");

            e.Registry.SetRegistryDirectly(s.Payable);
            Program.Ok(e.Refresh(10.6f), "no event, no membership change");
            Program.Eq(Count(6), 0, "registry edits without the hook are invisible");

            postfix["AddPayable"].Invoke(null, null);
            Program.Ok(e.Refresh(11.2f), "postfix marks the registry dirty");
            Program.Eq(Count(6), 6, "new member counted after the real hook fires");

            e.Registry.SetRegistryDirectly();
            Program.Ok(e.Refresh(11.8f), "removal without the hook is invisible");
            Program.Eq(Count(6), 6, "still cached");
            postfix["RemovePayable"].Invoke(null, null);
            Program.Ok(e.Refresh(12.4f), "removal dirty");
            Program.Eq(Count(6), 0, "member dropped after the real hook fires");
            Program.Eq(Payables(6).Count, 0, "cached target released");
        }
    }
}
