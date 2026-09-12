using System;
using KingdomEnhancedMod;

// Invoke the linked production hooks and Refresh; fixtures supply only native
// inputs (roster, registry, stock and promotion state), never derived counters.
internal static class BreadTests
{
    private const int P = 4;
    private static int Incoming => AutoRestockCounts.IncomingCount(P);
    private static int Live => AutoRestockCounts.LiveCount(P);
    private static int Stock => AutoRestockCounts.StockCount(P);
    private sealed class Setup
    {
        internal readonly Env E = new Env();
        internal readonly Character C;
        internal readonly Beggar Beggar;
        internal readonly PayableShop Shop;
        internal readonly Baker Baker;
        internal Setup()
        {
            E.Mask(false, false, false, false, true);
            C = E.MakeCharacter("Beggar"); Beggar = C.GetComponent<Beggar>();
            Shop = Bakery(E, 3); Baker = Shop.GetComponent<Baker>();
            E.Planner.AddShop(Shop.gameObject);
            Test.Eq(E.R(), true, "fixture seed");
        }
        internal void Consume()
        {
            Shop.itemCountOverride--;
            Shop._items[0].FirePickedUp();
            AutoRestockCountsHooks.Baker_TryEatBread.Postfix(Baker, Beggar, true);
        }
        internal void Eating()
        {
            Consume(); Beggar._isEating = true; Time.frameCount++;
            Test.Eq(E.R(), true, "native promotion underway");
            Test.Eq(Incoming, 1, "pending recruit");
        }
    }
    private static PayableShop Bakery(Env e, int stock, string tag = "Untagged")
    {
        PayableShop shop = e.MakeShop(tag, stock);
        shop.gameObject.Add(new Baker { Pointer = Env.Next() });
        return shop;
    }
    private static void Retires(string name, Action<Setup> transition)
    {
        Test.Run(name, () => {
            var s = new Setup(); s.Eating(); long version = AutoRestockCounts.Version;
            transition(s); Test.Eq(s.E.R(), true, "refresh after native transition");
            Test.Eq(Incoming, 0, "incoming retired");
            Test.Assert(AutoRestockCounts.Version > version, "retirement changes version");
        });
    }
    internal static void Run()
    {
        Test.Run("bread01_actual_peasant_and_warrior_skin_only", () => {
            var e = new Env(); e.Mask(true, true, true, true, true);
            e.MakeCharacter("Peasant"); e.MakeCharacter("Peasant"); // second models skin-only variant
            foreach (string role in new[] { "Worker", "Archer", "Ninja", "Berserker" })
                e.MakeCharacter(role).gameObject.Add(new Peasant { Pointer = Env.Next() });
            e.MakeCharacter("Beggar");
            e.MakeCharacter("Beggar").gameObject.Add(new Peasant { Pointer = Env.Next() });
            Test.Eq(e.R(), true, "seed all five roles"); Test.Eq(Live, 2, "only actual unassigned Peasants");
            for (int i = 0; i < 4; i++) Test.Eq(AutoRestockCounts.LiveCount(i), 1, "employed precedence retained");
        });
        Test.Run("bread02_registered_bakeries_outside_placed_dedup_and_sum", () => {
            var e = new Env(); e.Mask(false, false, false, false, true);
            var first = Bakery(e, 2); var second = Bakery(e, 5);
            e.Planner.AddShop(first.gameObject); e.Planner.AddShop(first.gameObject); e.Planner.AddShop(second.gameObject);
            Test.Eq(e.Planner._placedShops.Length, 0, "bakeries absent from legacy placed array");
            Test.Eq(e.R(), true, "registered bakeries found"); Test.Eq(Stock, 7, "sum each bakery once");
            Test.Eq(AutoRestockCounts.Shops.Count, 2, "dedup membership");
            Test.Eq(first.GetItemCountCalls, 1, "duplicate reference queried once");
        });
        Test.Run("bread03_tag_alone_is_not_bakery_and_prefab_required", () => {
            var e = new Env(); e.Mask(false, false, false, false, true);
            var impostor = e.MakeShop("Bread", 7); var typed = Bakery(e, 2, "Bread");
            var missing = Bakery(e, 9); missing.itemPrefab = null;
            e.Planner.AddShop(impostor.gameObject); e.Planner.AddShop(typed.gameObject); e.Planner.AddShop(missing.gameObject);
            Test.Eq(e.R(), true, "seed"); Test.Eq(Stock, 2, "only typed bakery with prefab");
            Test.Eq(AutoRestockCounts.ClassifyShop(impostor), -1, "service classifier rejects impostor");
            Test.Eq(AutoRestockCounts.ClassifyShop(typed), P, "service classifier accepts bakery");
        });
        Test.Run("bread04_add_remove_hooks_rebuild_without_roster_scan", () => {
            var s = new Setup(); int enumerations = s.E.Enums;
            var added = Bakery(s.E, 5); s.E.Planner.AddShop(added.gameObject);
            AutoRestockCountsHooks.ShopPlanner_AddShop.Postfix(s.E.Planner);
            Test.Eq(s.E.R(), true, "addition"); Test.Eq(Stock, 8, "new bakery stock");
            s.E.Planner.RemoveShop(s.Shop.gameObject);
            AutoRestockCountsHooks.ShopPlanner_RemoveShop.Postfix(s.E.Planner);
            Test.Eq(s.E.R(), true, "removal"); Test.Eq(Stock, 5, "removed bakery excluded");
            Test.Eq(s.E.Enums, enumerations, "shop lifecycle never rescans roster");
            Test.Eq(s.Shop._items[0].PickedUpSubscriberCount, 0, "removed stock subscriptions detached");
        });
        Test.Run("bread05_foreign_planner_hook_is_scoped", () => {
            var s = new Setup(); int queries = Native.GetItemCountCalls;
            var other = new ShopPlanner { Pointer = Env.Next() };
            AutoRestockCountsHooks.ShopPlanner_AddShop.Postfix(other);
            AutoRestockCountsHooks.ShopPlanner_RemoveShop.Postfix(other);
            Test.Eq(s.E.R(), true, "steady"); Test.Eq(Native.GetItemCountCalls, queries, "foreign lifecycle does not rebuild");
        });
        Test.Run("bread06_success_is_incoming_not_live_or_stock", () => {
            var s = new Setup(); long version = AutoRestockCounts.Version; s.Consume();
            Test.Eq(s.E.R(), true, "consumption frame");
            Test.Eq(Incoming, 1, "consumption retained"); Test.Eq(Live, 0, "Beggar not live Peasant");
            Test.Eq(Stock, 2, "native bread stock decreased");
            Test.Assert(AutoRestockCounts.Version > version, "consumption changes version");
            for (int role = -1; role < 7; role++) if (role != P)
                Test.Eq(AutoRestockCounts.IncomingCount(role), 0, "only Peasant role has incoming");
        });
        Test.Run("bread07_false_consumption_does_not_credit", () => {
            var s = new Setup(); AutoRestockCountsHooks.Baker_TryEatBread.Postfix(s.Baker, s.Beggar, false);
            Test.Eq(s.E.R(), true, "failed attempt"); Test.Eq(Incoming, 0, "no credit"); Test.Eq(Stock, 3, "stock retained");
        });
        Test.Run("bread08_duplicate_success_deduplicates_identity", () => {
            var s = new Setup(); s.Consume(); long version = AutoRestockCounts.Version;
            AutoRestockCountsHooks.Baker_TryEatBread.Postfix(s.Baker, s.Beggar, true);
            Test.Eq(AutoRestockCounts.Version, version, "duplicate event has no mutation");
            Test.Eq(s.E.R(), true, "refresh"); Test.Eq(Incoming, 1, "same actor once");
        });
        Test.Run("bread09_foreign_world_success_ignored", () => {
            var s = new Setup(); s.E.Managers.world = new World { Pointer = Env.Next(), gameLayer = s.E.Layer };
            AutoRestockCountsHooks.Baker_TryEatBread.Postfix(s.Baker, s.Beggar, true);
            s.E.Managers.world = s.E.World;
            Test.Eq(s.E.R(), true, "original context"); Test.Eq(Incoming, 0, "world mismatch ignored");
        });
        Test.Run("bread10_foreign_layer_bakery_and_actor_ignored", () => {
            var s = new Setup(); s.Shop.transform.parent = null;
            AutoRestockCountsHooks.Baker_TryEatBread.Postfix(s.Baker, s.Beggar, true);
            s.Shop.transform.parent = s.E.Layer; s.C.transform.parent = null;
            AutoRestockCountsHooks.Baker_TryEatBread.Postfix(s.Baker, s.Beggar, true);
            s.C.transform.parent = s.E.Layer;
            Test.Eq(s.E.R(), true, "restored context"); Test.Eq(Incoming, 0, "foreign actors or bakeries not credited");
        });
        Test.Run("bread11_same_frame_grace_then_native_state_required", () => {
            var s = new Setup(); s.Consume();
            for (int i = 0; i < 3; i++) { Test.Eq(s.E.R(), true, "same-frame refresh"); Test.Eq(Incoming, 1, "caller has not set eating yet"); }
            Time.frameCount++; Test.Eq(s.E.R(), true, "next frame"); Test.Eq(Incoming, 0, "no native eating state means no continuing credit");
        });
        Test.Run("bread12_native_eating_survives_arbitrary_elapsed_time_without_scans", () => {
            var s = new Setup(); s.Eating(); int scans = s.E.Enums, queries = Native.GetItemCountCalls;
            for (int i = 0; i < 100; i++) {
                Time.time += 10000; Time.frameCount += 600000;
                Test.Eq(s.E.R(), true, "native eating still true"); Test.Eq(Incoming, 1, "no fixed duration expiry");
            }
            Test.Eq(s.E.Enums, scans, "bounded incoming check, no roster scan");
            Test.Eq(Native.GetItemCountCalls, queries, "no periodic shop queries");
        });
        Retires("bread13_eating_false_retires", s => s.Beggar._isEating = false);
        Retires("bread14_death_retires", s => s.C._damageable.isDead = true);
        Retires("bread15_remove_character_retires", s => {
            AutoRestockCountsHooks.Kingdom_RemoveCharacter.Prefix(s.C); s.E.Kingdom.RemoveCharacter(s.C);
        });
        Retires("bread16_leaving_layer_retires", s => s.C.transform.parent = null);
        Retires("bread17_inactive_character_retires", s => s.C.gameObject.activeInHierarchy = false);
        Retires("bread18_disabled_beggar_retires", s => s.Beggar.enabled = false);
        Retires("bread19_instance_identity_replacement_retires", s => s.Beggar.InstanceId++);
        Retires("bread20_owner_identity_replacement_retires", s => s.Beggar._character = s.E.MakeCharacter("Beggar"));
        Test.Run("bread21_mask_reseed_preserves_native_pending", () => {
            var s = new Setup(); s.Eating(); int scans = s.E.Enums;
            ModConfig.AutoRestockWorkersEnabled.Value = true;
            Test.Eq(s.E.R(), true, "mask reseed"); Test.Eq(Incoming, 1, "actual native pending preserved");
            Test.Eq(s.E.Enums, scans + 1, "only requested settings reseed");
        });
        Test.Run("bread22_disable_reenable_reconstructs_native_pending", () => {
            var s = new Setup(); s.Eating(); ModConfig.AutoRestockPeasantsEnabled.Value = false;
            Test.Eq(s.E.R(), false, "fully disabled"); Test.Eq(Incoming, 0, "disabled getter gated");
            ModConfig.AutoRestockPeasantsEnabled.Value = true;
            Test.Eq(s.E.R(), true, "reenabled"); Test.Eq(Incoming, 1, "reconstructed from native eating state");
        });
        Test.Run("bread23_same_frame_reseed_preserves_grace", () => {
            var s = new Setup(); s.Consume(); ModConfig.AutoRestockWorkersEnabled.Value = true;
            Test.Eq(s.E.R(), true, "same-frame settings reseed"); Test.Eq(Incoming, 1, "genuine consumption not lost before caller state assignment");
        });
        Test.Run("bread24_failure_gates_incoming_then_reconstructs", () => {
            var s = new Setup(); s.Eating(); s.Shop.throwOnGetItemCount = true;
            AutoRestockCounts.HookShopAddItem(s.Shop);
            Test.Eq(s.E.R(), false, "native stock uncertainty"); Test.Eq(Incoming, 0, "unready cache publishes no incoming");
            s.Shop.throwOnGetItemCount = false;
            Test.Eq(s.E.R(), true, "bounded recovery"); Test.Eq(Incoming, 1, "native pending survives recovery");
        });
        Test.Run("bread25_world_change_clears_old_incoming", () => {
            var s = new Setup(); s.Eating();
            var go = Fabric.NewGo(Env.Next(), 2, null);
            s.E.Managers.world = new World { Pointer = Env.Next(), gameLayer = go.transform };
            Test.Eq(s.E.R(), true, "new world seed"); Test.Eq(Incoming, 0, "old world eating actor excluded");
        });
        Test.Run("bread26_native_promotion_replaces_incoming_with_live", () => {
            var s = new Setup(); s.Eating();
            AutoRestockCountsHooks.Kingdom_RemoveCharacter.Prefix(s.C); s.E.Kingdom.RemoveCharacter(s.C);
            var promoted = s.E.MakeCharacter("Peasant");
            AutoRestockCountsHooks.Kingdom_AddCharacter.Postfix(s.E.Kingdom, promoted);
            Test.Eq(s.E.R(), true, "synchronous native replacement"); Test.Eq(Incoming, 0, "old beggar removed");
            Test.Eq(Live, 1, "new live Peasant"); Test.Eq(Stock, 2, "bread was consumed once");
        });
        Test.Run("bread27_config_off_success_ignored_before_refresh", () => {
            var s = new Setup(); ModConfig.AutoRestockPeasantsEnabled.Value = false;
            AutoRestockCountsHooks.Baker_TryEatBread.Postfix(s.Baker, s.Beggar, true);
            ModConfig.AutoRestockPeasantsEnabled.Value = true;
            Test.Eq(s.E.R(), true, "same cached mask"); Test.Eq(Incoming, 0, "hook obeys current setting");
        });
        Test.Run("bread28_missing_actor_identity_fails_closed", () => {
            var s = new Setup(); s.Beggar._character = null;
            AutoRestockCountsHooks.Baker_TryEatBread.Postfix(s.Baker, s.Beggar, true);
            Test.Eq(Incoming, 0, "undecidable successful consumption invalidates cache");
            s.Beggar._isEating = true;
            Test.Eq(s.E.R(), false, "unresolved identity prevents publishing");
        });
        Test.Run("bread29_registry_excludes_inactive_foreign_scene_and_layer_bakeries", () => {
            var e = new Env(); e.Mask(false, false, false, false, true);
            var active = Bakery(e, 1); var inactive = Bakery(e, 20); inactive.gameObject.activeInHierarchy = false;
            var otherScene = Bakery(e, 30); otherScene.gameObject.scene = new Scene { handle = 9 };
            var otherLayer = Bakery(e, 40); otherLayer.transform.parent = null;
            foreach (var shop in new[] { active, inactive, otherScene, otherLayer }) e.Planner.AddShop(shop.gameObject);
            Test.Eq(e.R(), true, "registry seed"); Test.Eq(Stock, 1, "only live current context stock");
            Test.Eq(AutoRestockCounts.Shops.Count, 1, "only current bakery registered");
        });
        Test.Run("bread30_reset_clears_pending_and_version", () => {
            var s = new Setup(); s.Eating(); long version = AutoRestockCounts.Version;
            AutoRestockCounts.Reset(); Test.Eq(Incoming, 0, "reset getter unavailable");
            Test.Assert(AutoRestockCounts.Version > version, "reset records change");
            s.Beggar._isEating = false;
            Test.Eq(s.E.R(), true, "new seed"); Test.Eq(Incoming, 0, "no residual synthetic credits");
        });
    }
}
