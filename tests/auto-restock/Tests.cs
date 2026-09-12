// Tests.cs — black-box behavior tests for PatchEconomy_AutoRestock.Tick/Reset.
// The production file is linked UNMODIFIED from ../build; everything it touches
// is stubbed in Stubs.cs. Tests drive a fake clock and assert observable
// outcomes (ledger calls, native purchase calls, reservations, coin spawns,
// summaries) — never the internal algorithm.
using System;
using System.Collections.Generic;
using System.Reflection;
using KingdomEnhancedMod;

namespace AutoRestockTests
{
    internal static class Program
    {
        internal static int Main()
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

            Run("gates_all_roles_disabled_summaries_and_counts_reset", Gates_AllRolesDisabled);
            Run("gates_no_world_auth_releases_without_returnhome", Gates_NoWorldAuth);
            Run("gates_wrong_world_banker_resets_no_orders", Gates_WrongWorldBanker);
            Run("counts_init_pending_summary_and_no_orders", CountsInitPending);
            Run("meets_target_no_order", MeetsTarget_NoOrder);
            Run("full_shop_slots_block_even_in_deficit_10w4h_goal15", FullShopBlocks);
            Run("death_reduction_triggers_purchase", DeathReduction);
            Run("happy_path_debit_exactly_once_and_returnhome", HappyPath);
            Run("coin_timing_015_start_02_each_025_final", CoinTiming);
            Run("stock_added_during_animation_cancels_no_debit", StockAddedDuringAnimation);
            Run("population_added_during_animation_cancels_no_debit", PopulationAddedDuringAnimation);
            Run("price_change_cancels_then_rebuys_at_new_price", PriceChange);
            Run("role_disabled_mid_animation_cancels_no_debit", RoleDisabledMidAnimation);
            Run("goal_lowered_mid_animation_cancels_no_debit", GoalLoweredMidAnimation);
            Run("two_roles_two_orders_distinct_shops_max2", ConcurrentRoles);
            Run("budget_affords_one_role_only_one_order", BudgetOneRole);
            Run("insufficient_balance_no_order", InsufficientBalance);
            Run("debit_fails_no_native_pay", DebitFails);
            Run("native_throw_fault_sticky_same_world_no_refund_no_blind_retry", NativeThrowSticky);
            Run("native_throw_retried_after_world_change", NativeThrowWorldChange);
            Run("pause_and_notplaying_freeze_authloss_releases", PauseNotPlayingAuth);
            Run("night_preserves_and_completes_order", NightCancels);
            Run("layer_change_drops_orders_without_returnhome_teleport", LayerChange);
            Run("reset_false_releases_reservation_without_returnhome", ResetFalseDirect);
            Run("shop_block_conditions_prevent_any_order", BlockConditions);
            Run("finalize_revalidation_cancels_before_debit", FinalizeRevalidation);
            Run("coin_failure_cleanup_no_spend", CoinFailure);
            Run("reserve_or_place_failure_no_leak", ReservePlaceFailure);
            Run("interop_unavailable_no_order_no_spend", InteropUnavailable);
            Run("summary_getter_cached_zero_native_reads", SummaryCached);
            Run("tax_tick_flushes_counts_refresh_each_tick", TaxRefreshFlush);
            Run("cheapest_shop_single_order_per_role", CheapestShop);
            Run("role_fairness_rotation_serves_other_roles", RoleFairness);

            Additional.Run();
            NightDiagnostics.Run();
            MotionAndPeasants.Run();
            DoubleCost.Run();
            Console.WriteLine();
            Console.WriteLine($"==== {Passed} passed, {Failed} failed, {Passed + Failed} total ====");
            foreach (string f in Failures) Console.WriteLine("FAIL: " + f);
            return Failed == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        internal static int Passed, Failed;
        internal static readonly List<string> Failures = new();

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

        internal static void Ok(bool c, string msg) { if (!c) throw new Exception("assert: " + msg); }
        internal static void Eq<T>(T a, T b, string msg)
        {
            bool same = a is System.Collections.IEnumerable ea && b is System.Collections.IEnumerable eb && a is not string
                ? ea.Cast<object>().SequenceEqual(eb.Cast<object>()) : Equals(a,b);
            if (!same) throw new Exception($"{msg}: got '{a}', want '{b}'");
        }
        internal static void Has(string s, string sub, string msg)
        { if (s == null || !s.Contains(sub)) throw new Exception($"{msg}: '{s}' lacks '{sub}'"); }

        // ------------------------------------------------------------- env
        internal static long WorldCounter = 0x200000;

        internal sealed class Env
        {
            public Managers M;
            public Banker Banker;
            public Kingdom Kingdom;
            public World World;
            public Transform Layer;
            public Game Game;
            public CurrencyManager Currency;
            public int ShopSeq;

            public Env()
            {
                Layer = Fake.NewGO("layer").Transform;
                World = new World { gameLayer = Layer, Pointer = (IntPtr)(WorldCounter += 0x1000) };
                Kingdom = new Kingdom();
                Game = new Game();
                Currency = new CurrencyManager
                { CoinPrefab = new DroppableCurrency { gameObject = Fake.NewGO("coinprefab") } };
                Banker = new Banker();
                Fake.Attach(Banker, Fake.NewGO("banker", Layer));
                M = new Managers
                { world = World, kingdom = Kingdom, game = Game, currency = Currency, stats = new object() };
                Managers.Inst = M;
                BankAssistantCoordinator.MainBanker=Banker;
            }

            private float lastTick;
            public void Tick(bool tax = true)
            {
                Time.deltaTime = Time.timeScale<=0 ? 0 : Math.Max(0,Time.time-lastTick);
                lastTick=Time.time;
                PatchEconomy_AutoRestock.Tick(Banker, M, tax);
            }
            public void SetTime(float t) { Time.time = t; Tick(); }
            public void Frame(float realSeconds=0.05f,bool tax=true) { Time.time+=realSeconds*Time.timeScale; Tick(tax); }
            public void AddAssistants(int n)
            { for (int i = 0; i < n; i++) BankAssistantCoordinator.Actors.Enqueue(Fake.NewGO("assist" + i, Layer)); }

            public PayableShop MakeShop(int role, int price)
            {
                Droppable prefab = new Droppable { tag = AutoRestockCounts.Tags[role], gameObject = Fake.NewGO("prefab" + role) };
                GameObject go = Fake.NewGO("shop" + role + "_" + (++ShopSeq), Layer);
                PayableShop shop = role==4 ? new PayableShopBaker() : new PayableShop();
                shop.itemPrefab=prefab; shop.Price=price;
                Fake.Attach(shop, go);
                AutoRestockCounts._shops.Add(shop);
                return shop;
            }
        }

        internal static class Fake
        {
            private static int _ids = 1;
            private static long _ptr = 0x1000;

            public static GameObject NewGO(string name, Transform parent = null)
            {
                var go = new GameObject
                {
                    Name = name,
                    Id = _ids++,
                    Pointer = (IntPtr)(_ptr += 8),
                    Scene = new Scene { handle = 1 }
                };
                var t = new Transform { Parent = parent };
                t.gameObject = go;
                go.Transform = t;
                return go;
            }

            public static T Attach<T>(T c, GameObject go) where T : Component
            { c.gameObject = go; c.transform = go.Transform; return c; }
        }

        // ---------------------------------------------------------- reset
        internal static void HardReset()
        {
            Time.time = 0f; Time.timeScale = 1f; Time.deltaTime=0f;
            NetworkBigBoss.HasWorldAuth = true;
            NetworkBigBoss.IsOnline = false;
            NetworkBigBoss.IsClientPresent = false;
            NetworkBigBoss.HasClientCaughtUp = true;
            Pool.ResetPool();
            ModConfig.ResetConfig();
            AutoRestockCounts.ResetCounts();
            BankAssistantCoordinator.ResetCoord();
            PatchEconomy_Banker.ResetLedger();
            KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
            ResetServiceStatics();
        }

        internal static void ResetServiceStatics()
        {
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
            Type t = typeof(PatchEconomy_AutoRestock);
            foreach (FieldInfo f in t.GetFields(F))
            {
                if (f.IsLiteral || f.Name == "RoleTags" || f.Name == "RoleNames" || f.Name == "ReasonOrder") continue;
                if (f.IsInitOnly)
                {
                    var v = f.GetValue(null);
                    if (v is Array arr) Array.Clear(arr,0,arr.Length);
                    else if (v is System.Collections.IDictionary dict) dict.Clear();
                    else if (v is System.Collections.IList list) list.Clear();
                }
                else f.SetValue(null, f.FieldType.IsValueType ? Activator.CreateInstance(f.FieldType) : null);
            }
            t.GetField("_needsPlan", F).SetValue(null, true);
            t.GetField("_lastVersion", F).SetValue(null, -1L);
            t.GetField("_lastSettings", F).SetValue(null, -1L);
            t.GetField("_lastBalance", F).SetValue(null, -1);
        }

        internal static Env NewEnv() { HardReset(); var e = new Env(); e.AddAssistants(2); return e; }

        internal static void Role(int role, bool on, int target)
        {
            switch (role)
            {
                case 0: ModConfig.AutoRestockWorkersEnabled.Value = on; ModConfig.AutoRestockWorkersTarget.Value = target; break;
                case 1: ModConfig.AutoRestockArchersEnabled.Value = on; ModConfig.AutoRestockArchersTarget.Value = target; break;
                case 2: ModConfig.AutoRestockNinjasEnabled.Value = on; ModConfig.AutoRestockNinjasTarget.Value = target; break;
                case 3: ModConfig.AutoRestockBerserkersEnabled.Value = on; ModConfig.AutoRestockBerserkersTarget.Value = target; break;
                case 4: ModConfig.AutoRestockPeasantsEnabled.Value = on; ModConfig.AutoRestockPeasantsTarget.Value = target; break;
            }
        }

        internal static bool PumpUntil(Env e, Func<bool> done, float seconds = 8f)
        {
            for(int frame=0;frame<(int)(seconds/0.05f);frame++)
            {
                e.Frame();
                if (done()) return true;
            }
            return done();
        }

        internal static float Arrive(Env e)
        {
            Ok(PumpUntil(e,()=>BankAssistantCoordinator.Active.Count>0
                && BankAssistantCoordinator.Active.All(r=>Math.Abs(r.Actor.transform.position.x-1f)<=0.02f)),"actual counter arrival");
            return Time.time;
        }

        // shorthand aliases
        internal static int Spend => PatchEconomy_Banker.SpendAmounts.Count;
        internal static int Reserved => BankAssistantCoordinator.ReservedCount;

        // ==============================================================
        //  Tests
        // ==============================================================

        internal static void Gates_AllRolesDisabled()
        {
            Env e = NewEnv(); // no roles enabled (defaults)
            AutoRestockCounts.SetLive(0, 1);
            e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            int resets = AutoRestockCounts.ResetCalls;
            e.Tick();
            for (int r = 0; r < 4; r++) Eq(PatchEconomy_AutoRestock.GetSummary(r), "已关闭", "disabled summary " + r);
            Eq(AutoRestockCounts.ResetCalls, resets, "initially disabled service stays idle without repeated resets");
            Eq(Reserved, 0, "no orders when disabled");
            Eq(Spend, 0, "no spend when disabled");
        }

        internal static void Gates_NoWorldAuth()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            NetworkBigBoss.HasWorldAuth = false;
            int resets = AutoRestockCounts.ResetCalls;
            e.Tick();
            Eq(Reserved, 0, "no orders without world auth");
            Ok(AutoRestockCounts.ResetCalls > resets, "counts reset without world auth");
            Eq(Spend, 0, "no spend without world auth");
            // mid-animation loss of authority: release WITHOUT return-home teleport
            NetworkBigBoss.HasWorldAuth = true;
            e.Tick();
            Eq(Reserved, 1, "order with auth");
            NetworkBigBoss.HasWorldAuth = false;
            e.Tick();
            Eq(Reserved, 0, "order released on auth loss");
            Ok(BankAssistantCoordinator.Releases.Count > 0
                && !BankAssistantCoordinator.Releases[BankAssistantCoordinator.Releases.Count - 1].ReturnHome,
                "auth-loss release must not returnHome");
        }

        internal static void Gates_WrongWorldBanker()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Tick();
            Eq(Reserved, 1, "order with in-world banker");
            e.Banker.transform.Parent = null; // banker left the game layer
            e.Tick();
            Eq(Reserved, 0, "wrong-world banker drops orders");
            Eq(Spend, 0, "no spend with wrong-world banker");
            Ok(BankAssistantCoordinator.Releases.Count > 0
                && !BankAssistantCoordinator.Releases[BankAssistantCoordinator.Releases.Count - 1].ReturnHome,
                "wrong-world release without returnHome");
        }

        internal static void CountsInitPending()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            Role(1, false, 3);
            AutoRestockCounts.SetLive(0, 2);
            e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            AutoRestockCounts.RefreshResult = false;
            e.Tick();
            Eq(PatchEconomy_AutoRestock.GetSummary(0), "计数初始化中", "init-pending summary");
            Eq(PatchEconomy_AutoRestock.GetSummary(1), "已关闭", "disabled role summary");
            Eq(Reserved, 0, "no orders while counts initializing");
            AutoRestockCounts.RefreshResult = true;
            e.Tick();
            Eq(Reserved, 1, "order once counts ready");
        }

        internal static void MeetsTarget_NoOrder()
        {
            Env e = NewEnv();
            Role(0, true, 14);
            AutoRestockCounts.SetLive(0, 10);
            AutoRestockCounts.SetStock(0, 4);
            e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Tick();
            Has(PatchEconomy_AutoRestock.GetSummary(0), "已达标", "meets target summary");
            Eq(Reserved, 0, "no order when target met");
            Eq(Spend, 0, "no spend when target met");
        }

        internal static void FullShopBlocks()
        {
            Env e = NewEnv();
            Role(0, true, 15);
            AutoRestockCounts.SetLive(0, 10);
            AutoRestockCounts.SetStock(0, 4); // all native shop stock unclaimed
            PayableShop s = e.MakeShop(0, 2);
            s.maxItems = 4; s._limitedNumItems = 99; s.Items = 4; // every native slot full
            e.Banker._stashedCoins = 100;
            e.Tick();
            Has(PatchEconomy_AutoRestock.GetSummary(0), "店铺已满", "full-shop summary");
            Eq(Reserved, 0, "full native shop slots must block even in deficit");
            Eq(Spend, 0, "no spend when shop full");
            Eq(s.TransactionCompleteCalls, 0, "no purchase when shop full");
        }

        internal static void DeathReduction()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 3); // met
            PayableShop s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Tick();
            Eq(Reserved, 0, "no order while met");
            AutoRestockCounts.SetLive(0, 2); // a death reduces roster
            e.Tick();
            Eq(Reserved, 1, "deficit after death creates order");
            Ok(PumpUntil(e, () => s.TransactionCompleteCalls == 1 && Reserved == 0), "purchase completes");
            Eq(Spend, 1, "exactly one debit");
            Eq(PatchEconomy_Banker.SpendAmounts[0], 4, "debited doubled shop price");
            Eq(e.Banker._stashedCoins, 6, "bank balance reduced");
            Eq(s.Items, 1, "native item created");
            Eq(AutoRestockCounts.HookCalls, 1, "shop-add-item hook fired");
            Time.time += 3f; e.Tick();
            Eq(Spend, 1, "no further purchase once met again");
        }

        internal static void HappyPath()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            PayableShop s = e.MakeShop(0, 3);
            s.TransactionCompleteF = shop =>
            {
                shop.interactingPlayer = new Player(); // native selects nearest player
                shop.BalanceAtPay = e.Banker._stashedCoins;
            };
            e.Banker._stashedCoins = 10;
            e.Tick();
            Eq(Reserved, 1, "order created");
            Ok(PumpUntil(e, () => s.TransactionCompleteCalls == 1 && Reserved == 0), "completes");
            Eq(PatchEconomy_Banker.SpendAmounts, new List<int> { 6 }, "single debit of doubled price");
            Eq(e.Banker._stashedCoins, 4, "balance after purchase");
            Eq(Pool.SpawnCalls, 6, "one fake coin per total cost unit");
            Eq(s.Items, 1, "item added once");
            Eq(s.BalanceAtPay, 4, "debit must land before native purchase (no yields)");
            Ok(s.interactingPlayer == null, "synthetic interactingPlayer released");
            Ok(BankAssistantCoordinator.Releases.Count > 0
                && BankAssistantCoordinator.Releases[BankAssistantCoordinator.Releases.Count - 1].ReturnHome,
                "assistant returns home after success");
        }

        internal static void CoinTiming()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            PayableShop s = e.MakeShop(0, 3);
            e.Banker._stashedCoins = 10;
            e.Tick(); // scan creates order at T
            float t0 = Arrive(e);
            e.SetTime(t0 + 0.149f); Eq(Pool.SpawnCalls, 0, "no coin before start delay");
            e.SetTime(t0 + 0.151f); Eq(Pool.SpawnCalls, 1, "first coin after 0.15s start delay");
            e.SetTime(t0 + 0.350f); Eq(Pool.SpawnCalls, 1, "second coin not before cadence");
            e.SetTime(t0 + 0.352f); Eq(Pool.SpawnCalls, 2, "second coin 0.2s later (float tolerance)");
            e.SetTime(t0 + 0.551f); Eq(Pool.SpawnCalls, 2, "third coin not before cadence");
            e.SetTime(t0 + 0.553f); Eq(Pool.SpawnCalls, 3, "third coin 0.2s later (float tolerance)");
            e.SetTime(t0 + 0.754f); Eq(Pool.SpawnCalls, 4, "fourth coin at cadence");
            e.SetTime(t0 + 0.955f); Eq(Pool.SpawnCalls, 5, "fifth coin at cadence");
            e.SetTime(t0 + 1.156f); Eq(Pool.SpawnCalls, 6, "sixth coin at cadence");
            e.SetTime(t0 + 1.405f);
            Ok(s.TransactionCompleteCalls == 0, "no purchase before 0.25s final delay");
            e.SetTime(t0 + 1.410f);
            Eq(s.TransactionCompleteCalls, 1, "purchase after final delay");
            Ok(s.TransactionCompleteTime > t0 + 1.39f && s.TransactionCompleteTime <= t0 + 1.41f,
                "finalize at start+0.15+0.2*(totalCost-1)+0.25");
            e.SetTime(t0 + 2.5f);
            Eq(Pool.SpawnCalls, 6, "no coins beyond doubled price");
        }

        internal static void StockAddedDuringAnimation()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            PayableShop s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Tick();
            float t0 = Arrive(e);
            e.SetTime(t0 + 0.151f);
            e.SetTime(t0 + 0.352f); // native frames advance through both coin due times
            Eq(Pool.SpawnCalls, 2, "coins in flight");
            AutoRestockCounts.SetStock(0, 1); // unclaimed stock appears: 2 live + 1 stock = target
            e.Tick();
            Eq(Spend, 0, "stock change during animation must not debit");
            Eq(s.TransactionCompleteCalls, 0, "no purchase");
            Eq(Reserved, 0, "order cancelled");
            Ok(BankAssistantCoordinator.Releases.Count > 0
                && BankAssistantCoordinator.Releases[BankAssistantCoordinator.Releases.Count - 1].ReturnHome,
                "cancelled order returns assistant");
        }

        internal static void PopulationAddedDuringAnimation()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            PayableShop s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Tick();
            float t0 = Arrive(e);
            e.SetTime(t0 + 0.40f);
            AutoRestockCounts.SetLive(0, 3); // roster reached target
            e.Tick();
            Eq(Spend, 0, "population change during animation must not debit");
            Eq(s.TransactionCompleteCalls, 0, "no purchase");
            Eq(Reserved, 0, "order cancelled");
        }

        internal static void PriceChange()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            PayableShop s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Tick();
            float t0 = Arrive(e);
            e.SetTime(t0 + 0.30f); // 1 coin sent
            s.Price = 3;
            e.Frame(.21f); // next coin deadline must cancel old order before replanning
            Eq(Reserved, 0, "native price change cancels old order");
            Eq(Spend, 0, "cancelled old price never debits");
            Eq(Pool.SpawnCalls, 1, "old order emits no further coins after price change");
            Ok(PumpUntil(e, () => s.TransactionCompleteCalls == 1 && Reserved == 0), "rebuys at new price");
            Eq(PatchEconomy_Banker.SpendAmounts, new List<int> { 6 }, "single debit at doubled NEW price");
            Eq(e.Banker._stashedCoins, 4, "balance after rebuy");
            Eq(Pool.SpawnCalls, 7, "one cancelled visual coin plus six for repriced purchase");
            Eq(s.Price, 3, "automatic surcharge never writes native price");
        }

        internal static void RoleDisabledMidAnimation()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            PayableShop s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Tick();
            float t0 = Arrive(e);
            e.SetTime(t0 + 0.30f);
            Role(0, false, 3);
            e.Tick();
            Eq(Spend, 0, "role off mid-animation must not debit");
            Eq(s.TransactionCompleteCalls, 0, "no purchase");
            Eq(Reserved, 0, "order cancelled");
        }

        internal static void GoalLoweredMidAnimation()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            PayableShop s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Tick();
            float t0 = Arrive(e);
            e.SetTime(t0 + 0.30f);
            Role(0, true, 2); // target lowered to current roster
            e.Tick();
            Eq(Spend, 0, "lowered goal must not debit");
            Eq(s.TransactionCompleteCalls, 0, "no purchase");
            Eq(Reserved, 0, "order cancelled");
        }

        internal static void ConcurrentRoles()
        {
            Env e = NewEnv();
            Role(0, true, 2); Role(1, true, 2); Role(2, true, 2);
            AutoRestockCounts.SetLive(0, 1);
            AutoRestockCounts.SetLive(1, 1);
            AutoRestockCounts.SetLive(2, 1);
            PayableShop a = e.MakeShop(0, 2);
            PayableShop b = e.MakeShop(1, 2);
            PayableShop c = e.MakeShop(2, 2);
            e.Banker._stashedCoins = 20;
            e.Tick();
            Eq(Reserved, 2, "max two concurrent orders");
            Eq(BankAssistantCoordinator.PlaceCalls, 2, "two placements");
            Ok(PumpUntil(e, () => a.TransactionCompleteCalls == 1 && b.TransactionCompleteCalls == 1), "first two complete");
            Eq(Spend, 2, "two debits");
            Eq(e.Banker._stashedCoins, 12, "balance after two");
            Ok(PumpUntil(e, () => c.TransactionCompleteCalls == 1), "third role served after slot frees");
            Eq(Spend, 3, "third debit");
            Eq(a.TransactionCompleteCalls, 1, "no double purchase a");
            Eq(b.TransactionCompleteCalls, 1, "no double purchase b");
        }

        internal static void BudgetOneRole()
        {
            Env e = NewEnv();
            Role(0, true, 3); Role(1, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            AutoRestockCounts.SetLive(1, 1);
            e.MakeShop(0, 2);
            e.MakeShop(1, 5);
            e.Banker._stashedCoins = 5;
            e.Tick();
            Eq(Reserved, 1, "only affordable role orders");
            Has(PatchEconomy_AutoRestock.GetSummary(1), "金币不足", "insufficient role summary");
            Ok(PumpUntil(e, () => Reserved == 0 && Spend == 1), "first completes");
            Time.time += 3f; e.Tick();
            Eq(Spend, 1, "role1 still unaffordable (1 < 10)");
            Has(PatchEconomy_AutoRestock.GetSummary(1), "金币不足", "still insufficient");
        }

        internal static void InsufficientBalance()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            e.MakeShop(0, 2);
            e.Banker._stashedCoins = 1;
            e.Tick();
            Eq(Reserved, 0, "no order without balance");
            Has(PatchEconomy_AutoRestock.GetSummary(0), "金币不足", "insufficient summary");
            Eq(Spend, 0, "no spend");
        }

        internal static void DebitFails()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            PayableShop s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            PatchEconomy_Banker.FailNext = true;
            e.Tick();
            Ok(PumpUntil(e, () => Reserved == 0), "order resolves");
            Eq(Spend, 0, "failed debit must not charge");
            Eq(PatchEconomy_Banker.TrySpendCalls, 1, "exactly one spend attempt");
            Eq(e.Banker._stashedCoins, 10, "balance untouched");
            Eq(s.TransactionCompleteCalls, 0, "no native pay when debit fails");
            Ok(BankAssistantCoordinator.Releases.Count > 0
                && BankAssistantCoordinator.Releases[BankAssistantCoordinator.Releases.Count - 1].ReturnHome,
                "assistant home after funds failure");
        }

        internal static void NativeThrowSticky()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            PayableShop s = e.MakeShop(0, 2);
            s.TransactionCompleteF = shop => { if (shop.TransactionCompleteCalls == 1) throw new InvalidOperationException("native boom"); };
            e.Banker._stashedCoins = 10;
            e.Tick();
            Ok(PumpUntil(e, () => Reserved == 0), "order resolves (faulted)");
            Eq(PatchEconomy_Banker.TrySpendCalls, 1, "debited once");
            Eq(e.Banker._stashedCoins, 6, "no refund after native fault");
            Ok(KingdomEnhancedPlugin.Instance.LogSource.Warnings.Count(w => w.Contains("shop fault")) == 1,
                "shop fault logged");
            // stays blocked across replans, settings changes and off/on, same world
            Time.time += 3f; e.Tick();
            Role(0, true, 5); Time.time += 3f; e.Tick();
            Role(0, false, 3); e.Tick();
            Role(0, true, 3); Time.time += 3f; e.Tick(); Time.time += 3f; e.Tick();
            Eq(PatchEconomy_Banker.TrySpendCalls, 1, "no blind retry same world");
            Eq(s.TransactionCompleteCalls, 1, "no second native attempt");
            Eq(Reserved, 0, "no order on faulted shop");
        }

        internal static void NativeThrowWorldChange()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            PayableShop s = e.MakeShop(0, 2);
            s.TransactionCompleteF = shop => { if (shop.TransactionCompleteCalls == 1) throw new InvalidOperationException("native boom"); };
            e.Banker._stashedCoins = 10;
            e.Tick();
            Ok(PumpUntil(e, () => Reserved == 0), "faulted");
            Eq(s.TransactionCompleteCalls, 1, "failed once");
            // new world: fault marks cleared, same shop becomes buyable again
            Env e2 = new Env(); // new world pointer + layer + banker; counts stub persists
            e2.AddAssistants(2);
            s.gameObject.transform.Parent = e2.Layer;
            e2.Banker._stashedCoins = 10;
            e2.Tick();
            Eq(Reserved, 1, "order recreated in new world");
            Ok(PumpUntil(e2, () => s.TransactionCompleteCalls == 2), "retry succeeds after world change");
            Eq(PatchEconomy_Banker.SpendAmounts, new List<int> { 4, 4 }, "debited in both worlds");
        }

        internal static void PauseNotPlayingAuth()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            e.MakeShop(0, 3);
            e.Banker._stashedCoins = 10;
            e.Tick();
            float t0 = Arrive(e);
            e.SetTime(t0 + 0.151f);
            Eq(Pool.SpawnCalls, 1, "first coin sent");
            Time.timeScale = 0f;
            e.SetTime(t0 + 0.40f);
            Eq(Pool.SpawnCalls, 1, "paused: no coin progress");
            Time.timeScale = 1f;
            e.Game.state = Game.State.Paused;
            e.SetTime(t0 + 0.60f);
            Eq(Pool.SpawnCalls, 1, "game not playing: frozen");
            Eq(Reserved, 1, "order kept while not playing");
            e.Game.state = Game.State.Playing;
            e.SetTime(t0 + 0.61f);
            Ok(Pool.SpawnCalls >= 2, "resumed");
        }

        internal static void NightCancels()
        {
            Env e=NewEnv();Role(0,true,3);AutoRestockCounts.SetLive(0,2);
            PayableShop shop=e.MakeShop(0,2);e.Banker._stashedCoins=10;e.Tick();
            e.SetTime(Time.time+.3f);e.Kingdom.isDaytime=false;e.Tick();
            Eq(Reserved,1,"night preserves in-flight order");
            Ok(PumpUntil(e,()=>shop.TransactionCompleteCalls==1),"purchase finishes after nightfall");
            Eq(e.Banker._stashedCoins,6,"night purchase charges exactly once");
        }

        internal static void LayerChange()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            PayableShop s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Tick();
            float t0 = Arrive(e);
            e.SetTime(t0 + 0.30f);
            int releasesBefore = BankAssistantCoordinator.Releases.Count;
            Transform newLayer = Fake.NewGO("layer2").Transform; // same scene, new layer ptr
            e.World.gameLayer = newLayer;
            e.Banker.transform.Parent = newLayer;      // banker still valid in-world
            s.gameObject.transform.Parent = newLayer;  // shop too
            int resets = AutoRestockCounts.ResetCalls;
            e.Tick();
            Eq(Reserved, 0, "world/layer change drops orders");
            Ok(AutoRestockCounts.ResetCalls > resets, "counts reset on world change");
            Ok(BankAssistantCoordinator.Releases.Count == releasesBefore + 1
                && !BankAssistantCoordinator.Releases[BankAssistantCoordinator.Releases.Count - 1].ReturnHome,
                "world-change release must NOT teleport assistant home");
        }

        internal static void ResetFalseDirect()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Tick();
            Eq(Reserved, 1, "order active");
            PatchEconomy_AutoRestock.Reset(false);
            Eq(Reserved, 0, "reservation released");
            Ok(BankAssistantCoordinator.Releases.Count > 0
                && !BankAssistantCoordinator.Releases[BankAssistantCoordinator.Releases.Count - 1].ReturnHome,
                "Reset(false) must release without returnHome teleport");
        }

        internal static void BlockConditions()
        {
            var cases = new List<(string Name, Action<Env, PayableShop>)>
            {
                ("underConstruction",      (en, s) => s._workableBuilding.UnderConstruction = true),
                ("noWorkableBuilding",     (en, s) => s._workableBuilding = null),
                ("wrongCurrency",          (en, s) => s.Currency = CurrencyType.Jade),
                ("priceZero",              (en, s) => s.Price = 0),
                ("priceAbove100",          (en, s) => s.Price = 101),
                ("forceBlockPayment",      (en, s) => s.forceBlockPayment = true),
                ("selectedByP1",           (en, s) => s.selectedByP1 = true),
                ("selectedByP2",           (en, s) => s.selectedByP2 = true),
                ("playerSelecting",        (en, s) => s.PlayerSelecting = new Player()),
                ("interactingPlayer",      (en, s) => s.interactingPlayer = new Player()),
                ("p1SelectedPayable",      (en, s) => en.Kingdom.playerOne = new Player { selectedPayable = new Payable { Pointer = s.gameObject.Pointer } }),
                ("p1CompletingPayable",    (en, s) => en.Kingdom.playerOne = new Player { _completingPayable = new Payable { Pointer = s.gameObject.Pointer } }),
                ("p2SelectedPayable",      (en, s) => en.Kingdom.playerTwo = new Player { selectedPayable = new Payable { Pointer = s.gameObject.Pointer } }),
                ("p2CompletingPayable",    (en, s) => en.Kingdom.playerTwo = new Player { _completingPayable = new Payable { Pointer = s.gameObject.Pointer } }),
                ("shopDisabled",           (en, s) => s.enabled = false),
                ("shopInactive",           (en, s) => s.gameObject.SetActive(false)),
                ("prefabNull",             (en, s) => s.itemPrefab = null),
            };
            foreach (var c in cases)
            {
                Env e = NewEnv();
                Role(0, true, 3);
                AutoRestockCounts.SetLive(0, 2);
                PayableShop s = e.MakeShop(0, 2);
                e.Banker._stashedCoins = 10;
                c.Item2(e, s);
                e.Tick();
                Ok(Reserved == 0 && Spend == 0 && s.TransactionCompleteCalls == 0,
                    "blocked shop must yield no order/spend/purchase: " + c.Name);
            }
        }

        internal static void FinalizeRevalidation()
        {
            // (a) CanPay false at finalize
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            PayableShop s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Tick();
            float t0 = Arrive(e);
            e.SetTime(t0 + 0.151f); e.SetTime(t0 + 0.352f); e.SetTime(t0 + 0.553f); e.SetTime(t0 + 0.754f); // coins done, final wait pending
            s.CanPayF = _ => false;
            e.SetTime(t0 + 1.1f);
            Eq(Spend, 0, "CanPay false at finalize must not debit");
            Eq(s.TransactionCompleteCalls, 0, "no purchase");
            Eq(Reserved, 0, "order cancelled");

            // (b) balance drained below doubled price (still covers native price) before finalize
            e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Tick();
            t0 = Arrive(e);
            e.SetTime(t0 + 0.151f); e.SetTime(t0 + 0.352f); e.SetTime(t0 + 0.553f); e.SetTime(t0 + 0.754f);
            e.Banker._stashedCoins = 3;
            e.SetTime(t0 + 1.1f);
            Eq(Spend, 0, "drained balance must not debit");
            Eq(s.TransactionCompleteCalls, 0, "no purchase");

            // (c) player selects shop before finalize
            e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Tick();
            t0 = Arrive(e);
            e.SetTime(t0 + 0.151f); e.SetTime(t0 + 0.352f); e.SetTime(t0 + 0.553f); e.SetTime(t0 + 0.754f);
            e.Kingdom.playerOne = new Player { selectedPayable = new Payable { Pointer = s.gameObject.Pointer } };
            e.SetTime(t0 + 1.1f);
            Eq(Spend, 0, "player-selected shop must not debit at finalize");
            Eq(s.TransactionCompleteCalls, 0, "no purchase");
        }

        internal static void CoinFailure()
        {
            // (a) MoveTo throws: coin must be faked + despawned, order cancelled
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            PayableShop s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            Pool.MoveToFails = true;
            e.Tick();
            Ok(PumpUntil(e, () => Reserved == 0), "order cancelled on coin failure");
            Ok(Pool.LastCoin != null && Pool.LastCoin.FakeSet, "failed coin marked fake");
            Ok(Pool.DespawnCalls >= 1, "failed coin despawned");
            Eq(Spend, 0, "coin failure must not debit");
            Eq(s.TransactionCompleteCalls, 0, "no purchase");

            // (b) Pool.Spawn throws: tick fault, orders reset, warning logged once
            e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Tick();
            Pool.SpawnThrows = true;
            Ok(PumpUntil(e, () => Reserved == 0), "orders reset on spawn failure");
            Eq(Spend, 0, "spawn failure must not debit");
            Time.time += 3f; e.Tick(); // new order -> throws again
            int warns = KingdomEnhancedPlugin.Instance.LogSource.Warnings.Count(w => w.Contains("tick fault"));
            Ok(warns == 1, "tick fault logged once per world, got " + warns);

            // (c) currency prefab unavailable: cancel without spend
            e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Currency.CoinPrefab = null;
            e.Tick();
            Ok(PumpUntil(e, () => Reserved == 0), "order cancelled without coin prefab");
            Eq(Spend, 0, "no coin prefab must not debit");
        }

        internal static void ReservePlaceFailure()
        {
            // (a) no assistant available
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            PayableShop s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            BankAssistantCoordinator.FailReserve = true;
            e.Tick();
            Eq(Reserved, 0, "reserve failure: no order");
            Eq(Spend, 0, "no spend");
            Has(PatchEconomy_AutoRestock.GetSummary(0), "税收官暂不可用", "precise reservation failure summary");

            // (b) placement (teleport) fails: reservation must be released, no leak
            e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            BankAssistantCoordinator.FailPlace = true;
            e.Tick();
            Eq(Reserved, 0, "place failure: no lingering reservation");
            Eq(Spend, 0, "no spend");
            Ok(BankAssistantCoordinator.Releases.Count >= 1
                && BankAssistantCoordinator.Releases[BankAssistantCoordinator.Releases.Count - 1].ReturnHome,
                "failed placement releases actor");
            Eq(BankAssistantCoordinator.Actors.Count, 2, "no actor leak");
            Has(PatchEconomy_AutoRestock.GetSummary(0), "税收官到店失败", "precise placement failure summary");
        }

        internal static void InteropUnavailable()
        {
            var cases = new List<(string Name, Action<Env, PayableShop>)>
            {
                ("poolAssetNull",        (en, s) => Pool.PoolAssetAvailable = false),
                ("statsNull",            (en, s) => en.M.stats = null),
                ("currencyNull",         (en, s) => en.M.currency = null),
                ("onlineNoHeader",       (en, s) => { NetworkBigBoss.IsOnline = true; s.parentHeaderRef = null; }),
                ("onlineNoRpcIndex",     (en, s) => { NetworkBigBoss.IsOnline = true; s._payRPCIndex = -1; }),
                ("onlineNoCrownPlayer",  (en, s) => { NetworkBigBoss.IsOnline = true; en.Kingdom.CrownFinder = _ => null; }),
                ("onlineNotCaughtUp",    (en, s) => { NetworkBigBoss.IsOnline = true; NetworkBigBoss.HasClientCaughtUp = false; }),
            };
            foreach (var c in cases)
            {
                Env e = NewEnv();
                Role(0, true, 3);
                AutoRestockCounts.SetLive(0, 2);
                PayableShop s = e.MakeShop(0, 2);
                e.Banker._stashedCoins = 10;
                c.Item2(e, s);
                e.Tick();
                Ok(Reserved == 0 && Spend == 0 && s.TransactionCompleteCalls == 0,
                    "interop unavailable must yield no order/spend/purchase: " + c.Name);
            }
        }

        internal static void SummaryCached()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            PayableShop s = e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            e.Tick(); // scan builds summary (native reads allowed here)
            int reads = s.GetItemCountCalls;
            for (int i = 0; i < 25; i++)
                PatchEconomy_AutoRestock.GetSummary(0);
            Eq(s.GetItemCountCalls, reads, "summary getter must not read native shop state");
            Eq(PatchEconomy_AutoRestock.GetSummary(-1), "", "out-of-range role");
            Eq(PatchEconomy_AutoRestock.GetSummary(5), "", "out-of-range role");
            string before = PatchEconomy_AutoRestock.GetSummary(0);
            Eq(PatchEconomy_AutoRestock.GetSummary(0), before, "cached summary stable");
        }

        internal static void TaxRefreshFlush()
        {
            Env e = NewEnv();
            Role(0, true, 3);
            AutoRestockCounts.SetLive(0, 2);
            e.MakeShop(0, 2);
            e.Banker._stashedCoins = 10;
            int before = AutoRestockCounts.RefreshCalls;
            e.Tick(); e.Tick(); e.Tick();
            Ok(AutoRestockCounts.RefreshCalls - before >= 3, "counts refreshed on every tax tick");
        }

        internal static void CheapestShop()
        {
            Env e = NewEnv();
            Role(0, true, 3); // deficit 1
            AutoRestockCounts.SetLive(0, 2);
            PayableShop cheap = e.MakeShop(0, 2);
            PayableShop pricey = e.MakeShop(0, 5);
            e.Banker._stashedCoins = 20;
            e.Tick();
            Eq(Reserved, 1, "exactly one order per role");
            Ok(PumpUntil(e, () => Reserved == 0), "done");
            Eq(PatchEconomy_Banker.SpendAmounts, new List<int> { 4 }, "cheapest shop chosen");
            Eq(pricey.TransactionCompleteCalls, 0, "expensive shop untouched");
            Time.time += 3f; e.Tick();
            Eq(Spend, 1, "no second order for satisfied role");
        }

        internal static void RoleFairness()
        {
            Env e = NewEnv();
            Role(1, true, 2); Role(2, true, 2); // role0 disabled; cursor must still reach 1 and 2
            AutoRestockCounts.SetLive(1, 1);
            AutoRestockCounts.SetLive(2, 1);
            PayableShop s1 = e.MakeShop(1, 2);
            PayableShop s2 = e.MakeShop(2, 2);
            e.Banker._stashedCoins = 4; // budget for exactly one doubled order
            e.Tick();
            Eq(Reserved, 1, "one order with single-order budget");
            Ok(PumpUntil(e, () => Reserved == 0), "first done");
            e.Banker._stashedCoins = 4; // refill (balance change triggers replan)
            e.Tick();
            Ok(PumpUntil(e, () => Reserved == 0), "second done");
            Eq(s1.TransactionCompleteCalls, 1, "role1 served");
            Eq(s2.TransactionCompleteCalls, 1, "role2 served (cursor rotation, not starvation)");
            Eq(PatchEconomy_Banker.SpendAmounts, new List<int> { 4, 4 }, "both debits");
        }
    }
}
