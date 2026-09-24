using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using static AutoRestockTests.Program;

namespace AutoRestockTests;

/// <summary>
/// Role 5 (farmer / scythe shop) and roles 6-7 (catapult barrel, fire tower jar)
/// driven through the real service against contract stubs. Ammo targets are plain
/// Payables from SiegeAmmoCounts.GetPayables and are paid through the native
/// TransactionComplete; a native pay that changes ammo is modelled as a native write
/// that only the next successful ammo refresh publishes.
/// </summary>
internal static class AmmoAndFarmer
{
    internal static void Run()
    {
        Program.Run("eight_role_settings_detect_same_parity_target_and_even_enable", () =>
        {
            var e = NewEnv(); Role(0, true, 15); AutoRestockCounts.SetLive(0, 15); e.MakeShop(0, 1); e.Banker._stashedCoins = 100;
            e.Tick(); Eq(Reserved, 0, "worker target initially met");
            Role(0, true, 17); e.Tick(); Eq(Reserved, 1, "15 to 17 must invalidate idle planner");
            e = NewEnv(); Role(0, false, 16); Role(1, true, 1); AutoRestockCounts.SetLive(1, 1); e.MakeShop(0, 1); e.Banker._stashedCoins = 100;
            e.Tick(); Eq(Reserved, 0, "enabled other role already met");
            Role(0, true, 16); e.Tick(); Eq(Reserved, 1, "even worker target off to on must invalidate planner");
        });
        Program.Run("disabled_ammo_payable_never_spends_before_or_during_order", () =>
        {
            var e = NewEnv(); Role(6, true, 1); var target = e.MakeAmmo(6, 5, 0); e.Banker._stashedCoins = 100;
            target.enabled = false; e.Tick(); Eq(Reserved, 0, "disabled before planning");
            target.enabled = true; Time.time += 3; e.Tick(); Eq(Reserved, 1, "enabled can reserve");
            target.enabled = false; e.Tick(); Eq(Reserved, 0, "disabled mid-order cancels"); Eq(Spend, 0, "never spent");
        });
        Program.Run("firetower_requires_its_own_ammo_rpc_before_and_during_order", () =>
        {
            var e = NewEnv(); Role(7, true, 1); var target = e.MakeAmmo(7, 2, 0); e.Banker._stashedCoins = 100;
            var tower = target.gameObject.GetComponent<FireTower>();
            NetworkBigBoss.IsOnline = NetworkBigBoss.IsClientPresent = true;
            tower._parentHeaderRef = null; e.Tick(); Eq(Reserved, 0, "payable RPC alone is insufficient");
            tower._parentHeaderRef = new object(); tower._fireJarsActiveIndex = -1; Time.time += 3; e.Tick(); Eq(Reserved, 0, "missing jar RPC index");
            tower._fireJarsActiveIndex = 1; Time.time += 3; e.Tick(); Eq(Reserved, 1, "both RPC chains ready");
            tower._parentHeaderRef = null; e.Tick(); Eq(Reserved, 0, "late RPC loss cancels"); Eq(Spend, 0, "no debit before actual jar sync ready");
        });
        Program.Run("full_fire_tower_waits_then_buys_only_one_after_consumption", () =>
        {
            var e = NewEnv(); Role(7, true, 12); var tower = e.MakeAmmo(7, 2, 9);
            SiegeAmmoCounts.SetCount(7, 9); e.Banker._stashedCoins = 100;
            e.Tick(); Eq(Reserved, 0, "full tower cannot reserve an assistant despite unmet target");
            Has(PatchEconomy_AutoRestock.GetSummary(7), "等待消耗", "explicit capacity wait reason");
            SiegeAmmoCounts.SetLocal(tower, 8); SiegeAmmoCounts.SetCount(7, 8); Time.time += 3; e.Tick();
            Eq(Reserved, 1, "consumption reopens exactly one purchase");
            Ok(PumpUntil(e, () => tower.TransactionCompleteCalls == 1 && Reserved == 0), "refill completed and assistant released");
            for (int i = 0; i < 10; i++) { Time.time += 3; e.Tick(); }
            Eq(tower.TransactionCompleteCalls, 1, "no purchases above capacity");
            Eq(Spend, 1, "one debit only");
        });
        FarmerSuite();
        AmmoSuite();
    }

    // ------------------------------------------------------------------ farmer
    private static void FarmerSuite()
    {
        Program.Run("farmer_live_plus_scythe_stock_meets_target", () =>
        {
            var e = NewEnv();
            Role(5, true, 2);
            AutoRestockCounts.SetLive(5, 1);
            var scythe = e.MakeShop(5, 3);
            e.Banker._stashedCoins = 20;
            e.Tick();
            Eq(Reserved, 1, "farmer deficit creates one order");
            Has(PatchEconomy_AutoRestock.GetSummary(5), "现有 1", "live farmer shown");
            Has(PatchEconomy_AutoRestock.GetSummary(5), "采购 1", "reserved purchase shown");
            Ok(PumpUntil(e, () => scythe.TransactionCompleteCalls == 1 && Reserved == 0), "scythe purchase completes");
            Eq(PatchEconomy_Banker.SpendAmounts.Single(), 6, "doubled native scythe price");
            Eq(AutoRestockCounts.StockCount(5), 1, "bought scythe counted as farmer stock");
            Eq(AutoRestockCounts.HookCalls, 1, "shop add-item hook fires for the scythe shop");
            Eq(e.Banker._stashedCoins, 14, "balance after the purchase");
            Time.time += 3f; e.Tick();
            Eq(Spend, 1, "live farmer + scythe stock now meet the target");
            Has(PatchEconomy_AutoRestock.GetSummary(5), "已达标", "farmer status reached target");
        });

        Program.Run("farmer_stock_arriving_mid_animation_cancels_without_debit", () =>
        {
            var e = NewEnv();
            Role(5, true, 3);
            AutoRestockCounts.SetLive(5, 2);
            var scythe = e.MakeShop(5, 2);
            e.Banker._stashedCoins = 10;
            e.Tick();
            Eq(Reserved, 1, "order created");
            Arrive(e);
            AutoRestockCounts.SetStock(5, 1); // 2 live + 1 unclaimed scythe == target
            e.Tick();
            Eq(Spend, 0, "scythe stock must not be bought twice");
            Eq(scythe.TransactionCompleteCalls, 0, "no native purchase");
            Eq(Reserved, 0, "order cancelled");
            Ok(BankAssistantCoordinator.Releases.Count > 0
                && BankAssistantCoordinator.Releases[BankAssistantCoordinator.Releases.Count - 1].ReturnHome,
                "cancelled farmer order returns the assistant");
        });

        Program.Run("farmer_buys_cheapest_scythe_shop_only", () =>
        {
            var e = NewEnv();
            Role(5, true, 2);
            AutoRestockCounts.SetLive(5, 1);
            var bakery = e.MakeShop(4, 1);   // peasant shop: role disabled here
            var pricey = e.MakeShop(5, 5);
            var cheap = e.MakeShop(5, 2);
            e.Banker._stashedCoins = 20;
            e.Tick();
            Eq(Reserved, 1, "single farmer order");
            Ok(PumpUntil(e, () => cheap.TransactionCompleteCalls == 1 && Reserved == 0), "cheapest scythe shop buys");
            Eq(pricey.TransactionCompleteCalls, 0, "expensive scythe shop untouched");
            Eq(bakery.TransactionCompleteCalls, 0, "bakery is not a farmer target");
            Eq(PatchEconomy_Banker.SpendAmounts.Single(), 4, "doubled price of the cheap scythe shop");
        });

        Program.Run("farmer_and_peasant_keep_separate_stock", () =>
        {
            var e = NewEnv();
            Role(4, true, 1); Role(5, true, 1);
            var bakery = e.MakeShop(4, 2);
            var scythe = e.MakeShop(5, 2);
            e.Banker._stashedCoins = 20;
            e.Tick();
            Eq(Reserved, 2, "both roles order their own shop");
            Ok(PumpUntil(e, () => bakery.TransactionCompleteCalls == 1 && scythe.TransactionCompleteCalls == 1), "both purchase");
            Eq(PatchEconomy_Banker.SpendAmounts.Count, 2, "each role paid once");
            Eq(AutoRestockCounts.StockCount(4), 1, "bread counted for the peasant role");
            Eq(AutoRestockCounts.StockCount(5), 1, "scythe counted for the farmer role");
            Has(PatchEconomy_AutoRestock.GetSummary(4), "面包", "peasant summary keeps bread wording");
            Has(PatchEconomy_AutoRestock.GetSummary(5), "待领", "farmer summary reports unclaimed scythes");
        });
    }

    // -------------------------------------------------------------------- ammo
    private static void AmmoSuite()
    {
        Program.Run("ammo_barrel_pays_doubled_native_price_and_publishes_new_ammo", () =>
        {
            var e = NewEnv();
            Role(6, true, 2);
            SiegeAmmoCounts.SetCount(6, 1);
            Payable barrel = e.MakeAmmo(6, 5, 3);
            int forcesBeforePay = 0;
            barrel.TransactionCompleteF = p =>
            {
                forcesBeforePay = SiegeAmmoCounts.ForceCalls;
                SiegeAmmoCounts.SetNative(6, 2);   // native pay adds one barrel of ammo
                SiegeAmmoCounts.SetLocal(p, 4);
            };
            e.Banker._stashedCoins = 20;
            e.Tick();
            Eq(Reserved, 1, "barrel deficit creates one order");
            Ok(SiegeAmmoCounts.GetPayablesCalls > 0, "ammo targets come from the cached payable list");
            Has(PatchEconomy_AutoRestock.GetSummary(6), "弹药 1", "island-total ammo shown");
            Ok(PumpUntil(e, () => barrel.TransactionCompleteCalls == 1 && Reserved == 0), "native purchase completes");
            Eq(PatchEconomy_Banker.SpendAmounts.Single(), 10, "doubled native barrel price");
            Eq(e.Banker._stashedCoins, 10, "balance after the purchase");
            Eq(AutoRestockCounts.HookCalls, 0, "no shop add-item hook for ammo");
            Ok(SiegeAmmoCounts.ForceCalls > forcesBeforePay, "final commit force-refreshed the ammo snapshot");
            Eq(SiegeAmmoCounts.Count(6), 2, "refreshed island total is visible after the commit");
            Time.time += 3f; e.Tick();
            Eq(Spend, 1, "no second purchase once the refreshed total reaches the target");
        });

        Program.Run("ammo_full_tower_blocks_on_the_native_gate", () =>
        {
            var e = NewEnv();
            Role(7, true, 3);
            SiegeAmmoCounts.SetCount(7, 1);
            Payable tower = e.MakeAmmo(7, 2, 0);
            tower.CanPayF = _ => false;          // full tower: native CanPay refuses
            e.Banker._stashedCoins = 10;
            e.Tick();
            Eq(Reserved, 0, "full tower yields no order");
            Eq(Spend, 0, "no debit while the native gate is closed");
            Eq(tower.TransactionCompleteCalls, 0, "no native purchase");
            Has(PatchEconomy_AutoRestock.GetSummary(7), "原生付款暂不可用", "truthful native-gate reason");
        });

        Program.Run("ammo_barrel_without_catapult_serves_the_working_site_only", () =>
        {
            var e = NewEnv();
            Role(6, true, 3);
            SiegeAmmoCounts.SetCount(6, 1);
            Payable noCatapult = e.MakeAmmo(6, 5, 0); // emptiest, but native CanPay refuses (no catapult)
            noCatapult.CanPayF = _ => false;
            Payable working = e.MakeAmmo(6, 5, 4);
            e.Banker._stashedCoins = 20;
            e.Tick();
            Eq(Reserved, 1, "the working site orders");
            Ok(PumpUntil(e, () => working.TransactionCompleteCalls == 1), "working site purchase completes");
            Eq(noCatapult.TransactionCompleteCalls, 0, "site without a catapult untouched");
            Eq(Spend, 1, "one debit");
        });

        Program.Run("ammo_sites_prefer_the_emptier_one_and_then_alternate", () =>
        {
            var e = NewEnv();
            Role(6, true, 3);
            SiegeAmmoCounts.SetCount(6, 2);
            Payable loaded = e.MakeAmmo(6, 5, 5);
            Payable empty = e.MakeAmmo(6, 5, 1);
            e.Banker._stashedCoins = 20;
            e.Tick();
            Eq(Reserved, 1, "one order per role");
            Ok(PumpUntil(e, () => Reserved == 0), "first purchase done");
            Eq(empty.TransactionCompleteCalls, 1, "emptier site served first");
            Eq(loaded.TransactionCompleteCalls, 0, "loaded site not served first");
            // The served site is now the fuller one: the next order must go the other way.
            SiegeAmmoCounts.SetLocal(empty, 6);
            SiegeAmmoCounts.SetNative(6, 2);
            Time.time += 3f;
            e.Tick();
            Ok(PumpUntil(e, () => loaded.TransactionCompleteCalls == 1 && Reserved == 0, 20), "the other side is served next and finishes departing (no starvation)");
            Eq(Reserved, 0, "second order resolved");
            Eq(Spend, 2, "two separate commits");
        });

        Program.Run("ammo_threshold_is_island_total_with_one_commit_at_a_time", () =>
        {
            var e = NewEnv();
            Role(6, true, 5);
            SiegeAmmoCounts.SetCount(6, 3);          // two missing, but one order at a time
            Payable site = e.MakeAmmo(6, 5, 0);
            site.TransactionCompleteF = p =>
            {
                SiegeAmmoCounts.SetNative(6, SiegeAmmoCounts.Native[6] + 1);
                SiegeAmmoCounts.SetLocal(site, SiegeAmmoCounts.CountAt(site) + 1);
            };
            e.Banker._stashedCoins = 100;
            e.Tick();
            Eq(Reserved, 1, "a single order even though two units are missing");
            Has(PatchEconomy_AutoRestock.GetSummary(6), "采购 1", "reserved counts toward the threshold");
            Ok(PumpUntil(e, () => site.TransactionCompleteCalls == 1, 20), "first purchase");
            Eq(SiegeAmmoCounts.Count(6), 4, "island total published by the forced refresh");
            Ok(PumpUntil(e, () => site.TransactionCompleteCalls == 2, 20), "second order covers the remaining deficit");
            Eq(PatchEconomy_Banker.SpendAmounts.Count, 2, "two commits for the two missing units");
            Eq(PatchEconomy_Banker.SpendAmounts.Distinct().Single(), 10, "both commits at the doubled native price");
        });

        Program.Run("ammo_only_operates_without_any_shop_role_or_shop_counts", () =>
        {
            var e = NewEnv();
            AutoRestockCounts.RefreshResult = false;   // shop counts never available
            Role(6, true, 1);
            SiegeAmmoCounts.SetCount(6, 0);
            Payable barrel = e.MakeAmmo(6, 5, 0);
            e.Banker._stashedCoins = 10;
            int shopRefreshes = AutoRestockCounts.RefreshCalls;
            e.Tick();
            Eq(Reserved, 1, "ammo order without any shop role enabled");
            Eq(AutoRestockCounts.RefreshCalls, shopRefreshes, "the shop count cache is not consulted at all");
            Eq(PatchEconomy_AutoRestock.GetSummary(0), "已关闭", "shop roles stay reported closed");
            Ok(PumpUntil(e, () => barrel.TransactionCompleteCalls == 1), "ammo purchase completes");
            Eq(PatchEconomy_Banker.SpendAmounts.Single(), 10, "doubled barrel price in ammo-only mode");
        });

        Program.Run("ammo_unready_or_client_locked_blocks_without_low_count_fallback", () =>
        {
            var e = NewEnv();
            Role(6, true, 2);
            SiegeAmmoCounts.SetCount(6, 1);
            e.MakeAmmo(6, 5, 0);
            e.Banker._stashedCoins = 10;
            SiegeAmmoCounts.Ready = false;
            e.Tick();
            Eq(Reserved, 0, "unready ammo counts block new orders");
            Has(PatchEconomy_AutoRestock.GetSummary(6), "计数初始化中", "truthful initialising summary");
            Eq(Spend, 0, "no purchase on unready counts");
            SiegeAmmoCounts.Ready = true;
            e.Tick();
            Eq(Reserved, 1, "order once counts are ready");
            Arrive(e);
            SiegeAmmoCounts.Unavailable = true;         // client/locked between decision and commit
            e.Tick();
            Eq(Reserved, 0, "client-unavailable cancels the order");
            Eq(Spend, 0, "no debit without available counts");
        });

        Program.Run("ammo_price_change_and_disable_cancel_without_debit", () =>
        {
            var e = NewEnv();
            Role(7, true, 2);
            SiegeAmmoCounts.SetCount(7, 0);
            Payable tower = e.MakeAmmo(7, 2, 0);
            e.Banker._stashedCoins = 10;
            e.Tick();
            Eq(Reserved, 1, "order created");
            Arrive(e);
            int coins = Pool.SpawnCalls;
            tower.Price = 3;                            // native price changed
            e.Frame(.21f);
            Eq(Reserved, 0, "native price change cancels the order");
            Eq(Spend, 0, "cancelled order never debits");
            Eq(tower.TransactionCompleteCalls, 0, "no native purchase");
            Eq(Pool.SpawnCalls, coins, "no further coin after the price changed");

            e = NewEnv();
            Role(6, true, 2);
            SiegeAmmoCounts.SetCount(6, 0);
            Payable barrel = e.MakeAmmo(6, 5, 0);
            e.Banker._stashedCoins = 10;
            e.Tick();
            Arrive(e);
            Role(6, false, 2);
            e.Tick();
            Eq(Reserved, 0, "disabled role cancels the in-flight order");
            Eq(Spend, 0, "no debit after the role was disabled");
            Eq(barrel.TransactionCompleteCalls, 0, "no purchase after the role was disabled");
        });

        Program.Run("ammo_world_change_and_auth_loss_release_in_flight_orders", () =>
        {
            var e = NewEnv();
            Role(7, true, 2);
            SiegeAmmoCounts.SetCount(7, 0);
            Payable tower = e.MakeAmmo(7, 2, 0);
            e.Banker._stashedCoins = 10;
            e.Tick();
            Arrive(e);
            int releases = BankAssistantCoordinator.Releases.Count;
            Transform newLayer = Fake.NewGO("layer2").Transform; // same scene, new layer ptr
            e.World.gameLayer = newLayer;
            e.Banker.transform.Parent = newLayer;
            tower.gameObject.transform.Parent = newLayer;
            e.Tick();
            Eq(Reserved, 0, "world change drops the ammo order");
            Ok(BankAssistantCoordinator.Releases.Count == releases + 1
                && !BankAssistantCoordinator.Releases[BankAssistantCoordinator.Releases.Count - 1].ReturnHome,
                "world-change release must not teleport the assistant home");
            Eq(Spend, 0, "no debit across worlds");

            e = NewEnv();
            Role(6, true, 2);
            SiegeAmmoCounts.SetCount(6, 0);
            e.MakeAmmo(6, 5, 0);
            e.Banker._stashedCoins = 10;
            e.Tick();
            Eq(Reserved, 1, "order with world authority");
            NetworkBigBoss.HasWorldAuth = false;
            e.Tick();
            Eq(Reserved, 0, "auth loss releases the ammo order");
            Ok(BankAssistantCoordinator.Releases.Count > 0
                && !BankAssistantCoordinator.Releases[BankAssistantCoordinator.Releases.Count - 1].ReturnHome,
                "auth-loss release must not return the assistant home");
            Eq(Spend, 0, "no debit after losing authority");
        });

        Program.Run("ammo_native_fault_is_sticky_without_refund_or_blind_retry", () =>
        {
            var e = NewEnv();
            Role(6, true, 2);
            SiegeAmmoCounts.SetCount(6, 0);
            Payable barrel = e.MakeAmmo(6, 5, 0);
            barrel.TransactionCompleteF = _ => throw new InvalidOperationException("native boom");
            e.Banker._stashedCoins = 20;
            e.Tick();
            Ok(PumpUntil(e, () => Reserved == 0), "order resolves (faulted)");
            Eq(PatchEconomy_Banker.TrySpendCalls, 1, "debited exactly once");
            Eq(e.Banker._stashedCoins, 10, "no refund after a native fault");
            Ok(KingdomEnhancedPlugin.Instance.LogSource.Warnings.Count(w => w.Contains("shop fault")) == 1, "fault logged");
            for (int i = 0; i < 5; i++) { Time.time += 3f; e.Tick(); }
            Eq(barrel.TransactionCompleteCalls, 1, "no blind retry on the same world");
            Eq(Spend, 1, "no second debit for the faulted target");
        });

        Program.Run("ammo_gates_currency_selection_network_and_price", () =>
        {
            var cases = new List<(string Name, Action<Env, Payable>)>
            {
                ("wrongCurrency",       (en, p) => p.Currency = CurrencyType.Jade),
                ("priceZero",           (en, p) => p.Price = 0),
                ("priceAbove100",       (en, p) => p.Price = 101),
                ("forceBlockPayment",   (en, p) => p.forceBlockPayment = true),
                ("selectedByP1",        (en, p) => p.selectedByP1 = true),
                ("selectedByP2",        (en, p) => p.selectedByP2 = true),
                ("playerSelecting",     (en, p) => p.PlayerSelecting = new Player()),
                ("interactingPlayer",   (en, p) => p.interactingPlayer = new Player()),
                ("p1SelectedPayable",   (en, p) => en.Kingdom.playerOne = new Player { selectedPayable = p }),
                ("p2CompletingPayable", (en, p) => en.Kingdom.playerTwo = new Player { _completingPayable = p }),
                ("onlineNoHeader",      (en, p) => { NetworkBigBoss.IsOnline = true; p.parentHeaderRef = null; }),
                ("onlineNoRpcIndex",    (en, p) => { NetworkBigBoss.IsOnline = true; p._payRPCIndex = -1; }),
                ("onlineNoCrownPlayer", (en, p) => { NetworkBigBoss.IsOnline = true; en.Kingdom.CrownFinder = _ => null; }),
                ("onlineNotCaughtUp",   (en, p) => { NetworkBigBoss.IsOnline = true; NetworkBigBoss.HasClientCaughtUp = false; }),
                ("statsNull",           (en, p) => en.M.stats = null),
                ("currencyNull",        (en, p) => en.M.currency = null),
                ("inactiveGameObject",  (en, p) => p.gameObject.SetActive(false)),
            };
            foreach (var c in cases)
            {
                var e = NewEnv();
                Role(7, true, 2);
                SiegeAmmoCounts.SetCount(7, 0);
                Payable tower = e.MakeAmmo(7, 2, 0);
                e.Banker._stashedCoins = 10;
                c.Item2(e, tower);
                e.Tick();
                Ok(Reserved == 0 && Spend == 0 && tower.TransactionCompleteCalls == 0,
                    "blocked ammo target must yield no order/spend/purchase: " + c.Name);
            }
        });

        Program.Run("ammo_barrel_requires_its_actual_spawn_pool", () =>
        {
            var e = NewEnv();
            Role(6, true, 1);
            SiegeAmmoCounts.SetCount(6, 0);
            Payable barrel = e.MakeAmmo(6, 5, 0);
            Pool.PoolAssetAvailable = false;   // no shop tool pool for the barrel target
            e.Banker._stashedCoins = 10;
            e.Tick();
            Eq(Reserved, 0, "barrel actual pool must be ready before reserving");
            Eq(Spend, 0, "missing pool cannot consume bank money");
            Pool.PoolAssetAvailable = true;
            BiomeData.Swap = new UnityEngine.GameObject();
            Pool.RequiredPoolPrefab = BiomeData.Swap;
            Time.time += 3f; e.Tick();
            Ok(PumpUntil(e, () => barrel.TransactionCompleteCalls == 1), "uses biome-resolved barrel pool");
            Eq(PatchEconomy_Banker.SpendAmounts.Single(), 10, "ammo paid once when native dependency is ready");
        });

        Program.Run("new_roles_report_closed_without_touching_legacy_flow", () =>
        {
            var e = NewEnv();
            Role(0, true, 2);
            AutoRestockCounts.SetLive(0, 1);
            PayableShop shop = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Tick();
            for (int r = 5; r < 8; r++)
                Eq(PatchEconomy_AutoRestock.GetSummary(r), "已关闭", "new role reported closed: " + r);
            Eq(Reserved, 1, "legacy role still orders");
            Ok(PumpUntil(e, () => shop.TransactionCompleteCalls == 1), "legacy purchase still works");
            Eq(PatchEconomy_Banker.SpendAmounts.Single(), 4, "legacy doubled price unchanged");
            Eq(AutoRestockCounts.HookCalls, 1, "legacy shop add-item hook still fires");
            Eq(SiegeAmmoCounts.RefreshCalls, 0, "no ammo refresh while both ammo roles are off");
        });

        Program.Run("all_eight_roles_share_two_order_cap_and_exact_budget", () =>
        {
            var e = NewEnv();
            var shops = new List<PayableShop>();
            for (int r = 0; r < 6; r++) { Role(r, true, 1); shops.Add(e.MakeShop(r, 1)); }
            Role(6, true, 1); Role(7, true, 1);
            Payable barrel = e.MakeAmmo(6, 1, 0);
            Payable tower = e.MakeAmmo(7, 1, 0);
            barrel.TransactionCompleteF = p => { SiegeAmmoCounts.SetCount(6, SiegeAmmoCounts.Count(6) + 1); SiegeAmmoCounts.SetLocal(p, 1); };
            tower.TransactionCompleteF = p => SiegeAmmoCounts.SetCount(7, SiegeAmmoCounts.Count(7) + 1);
            e.Banker._stashedCoins = 16;   // exactly eight doubled purchases at native price 1
            e.Tick();
            Eq(Reserved, 2, "global cap of two concurrent orders");
            Ok(PumpUntil(e, () => shops.All(s => s.TransactionCompleteCalls == 1)
                && barrel.TransactionCompleteCalls == 1 && tower.TransactionCompleteCalls == 1
                && Reserved == 0, 60), "all eight roles served");
            Eq(BankAssistantCoordinator.MaxActive, 2, "never more than two leases");
            Eq(PatchEconomy_Banker.SpendAmounts.Count, 8, "each role paid exactly once");
            Eq(e.Banker._stashedCoins, 0, "exact shared budget, no overspend");
        });

        Program.Run("ammo_purchases_complete_at_night_without_withdrawal", () =>
        {
            // 全天语义：角色 7（火塔罐）夜间冷启动直接下单，扣款/发货照常完成。
            // 余额恰好只够一发双倍价（2*2=4），避免第二缺口立刻再下单干扰完成断言。
            var e = NewEnv();
            Role(7, true, 2);
            SiegeAmmoCounts.SetCount(7, 0);
            Payable tower = e.MakeAmmo(7, 2, 0);
            e.Banker._stashedCoins = 4;
            e.Kingdom.isDaytime = false;
            e.Tick();
            Eq(Reserved, 1, "night creates the ammo order");
            Ok(PumpUntil(e, () => tower.TransactionCompleteCalls == 1 && Reserved == 0, 20), "ammo shipped at night");
            Eq(Spend, 1, "single night debit");

            // 角色 6（投石车油桶）：SendingCoins 中入夜——继续完成，不撤单、不等天亮。
            e = NewEnv();
            Role(6, true, 2);
            SiegeAmmoCounts.SetCount(6, 0);
            Payable barrel = e.MakeAmmo(6, 5, 0);
            e.Banker._stashedCoins = 10;
            e.Tick();
            Arrive(e);
            e.SetTime(Time.time + 0.30f); // 已发出 1 枚动画币
            Eq(Reserved, 1, "barrel order waiting in the coin phase");
            e.Kingdom.isDaytime = false;
            Ok(PumpUntil(e, () => barrel.TransactionCompleteCalls == 1 && Reserved == 0, 20), "barrel purchase completes at night");
            Eq(PatchEconomy_Banker.SpendAmounts.Single(), 10, "single doubled barrel price");
        });
    }
}
