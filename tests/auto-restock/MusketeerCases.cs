using System;
using KingdomEnhancedMod;
using static AutoRestockTests.Program;
namespace AutoRestockTests;
internal static class MusketeerCases
{
    private static (Env, Payable) Setup(int goal = 1)
    {
        var e = NewEnv(); e.Banker._stashedCoins = 100;
        ModConfig.MusketeerEnabled.Value = ModConfig.AutoRestockMusketeersEnabled.Value = true;
        ModConfig.AutoRestockMusketeersTarget.Value = goal;
        var p = Fake.Attach(new Payable { Price = 4, CanPayF = _ => false }, Fake.NewGO("gunshop", e.Layer));
        MusketeerShop.Setup(p, e.Kingdom);
        return (e, p);
    }
    private static void Advance(Env e, int frames = 400) { for (int i = 0; i < frames; i++) e.Frame(); }
    internal static void RunAll()
    {
        Run("musketeer_real_service_adapter_single_8_one_gun_no_player_completion", () =>
        {
            var (e,p) = Setup(); Advance(e);
            Eq(PatchEconomy_Banker.TrySpendCalls,1,"one debit"); Eq(e.Banker._stashedCoins,92,"8 bank coins");
            Eq(MusketeerShop.SpawnCalls,1,"one real shipment boundary"); Eq(MusketeerShop.BalanceAtSpawn,92,"debit precedes shipment");
            Eq(p.TransactionCompleteCalls,0,"custom owner never native-completed");
            Eq(MusketeerIdentity.Guns,1,"coverage gained");
            Eq(e.M.stats.Spent,4f,"native base-price spending statistic"); Eq(e.M.stats.Calls,1,"one statistics update");
            Advance(e); Eq(PatchEconomy_Banker.TrySpendCalls,1,"completed order cannot debit again");
        });
        Run("musketeer_target_live_plus_ground_gun_and_reservation_prevents_overbuy", () =>
        {
            var (e,p) = Setup(3); MusketeerIdentity.Live=1; MusketeerIdentity.Guns=1;
            Advance(e); Eq(PatchEconomy_Banker.TrySpendCalls,1,"only missing one ordered");
            Eq(MusketeerIdentity.Guns,2,"dropped usable gun retained in coverage");
            Has(PatchEconomy_AutoRestock.GetSummary(8),"现有 1","separate role summary");
        });
        foreach (string gate in new[]{"closed","rack","bow","unknown","saving","funds","selected","engaged","foreign","online","paused","menu"})
            Run("musketeer_preflight_no_debit_"+gate, () =>
            {
                var (e,p)=Setup();
                switch(gate) {
                    case "closed": ModConfig.MusketeerEnabled.Value=false; break;
                    case "rack": MusketeerShop.Rack=3; break;
                    case "bow": MusketeerShop.BowReady=false; break;
                    case "unknown": MusketeerIdentity.Ready=false; break;
                    case "saving": MusketeerShop.Saving=true; break;
                    case "funds": e.Banker._stashedCoins=7; break;
                    case "selected": p.selectedByP1=true; break;
                    case "engaged": e.Kingdom.playerOne=new Player {_completingPayable=p}; break;
                    case "foreign": GreekBankScope.IsActive=false; break;
                    case "online": NetworkBigBoss.IsOnline=true; break;
                    case "paused": UnityEngine.Time.timeScale=0; break;
                    case "menu": e.Game.state=Game.State.Menu; break;
                }
                Advance(e); Eq(PatchEconomy_Banker.TrySpendCalls,0,"preflight rejects before debit");
                Eq(MusketeerShop.SpawnCalls,0,"no free shipment");
                if(gate=="closed") Has(PatchEconomy_AutoRestock.GetSummary(8),"请先开启火铳铺","actionable disabled status");
            });
        foreach (string change in new[]{"closed","full","player","met","unknown","world"})
            Run("musketeer_mid_order_cancel_"+change, () =>
            {
                var(e,p)=Setup(); e.Frame(); Advance(e,15);
                switch(change) {
                    case "closed": ModConfig.MusketeerEnabled.Value=false; break;
                    case "full": MusketeerShop.Rack=3; break;
                    case "player": p.interactingPlayer=new Player(); break;
                    case "met": MusketeerIdentity.Guns=1; break;
                    case "unknown": MusketeerIdentity.Ready=false; break;
                    case "world": e.World.Pointer=(IntPtr)999999; MusketeerShop.Exists=false; break;
                }
                Advance(e); Eq(PatchEconomy_Banker.TrySpendCalls,0,"cancel has no debit");
            });
        foreach(bool throws in new[]{false,true}) Run("musketeer_paid_failure_faults_target_"+throws,()=>
        {
            var(e,p)=Setup(); MusketeerShop.SpawnFails=!throws; MusketeerShop.SpawnThrows=throws;
            Advance(e); Advance(e);
            Eq(PatchEconomy_Banker.TrySpendCalls,1,"uncertain shipment never blindly retried");
            Eq(MusketeerShop.SpawnCalls,1,"single shipment attempt"); Eq(e.Banker._stashedCoins,92,"paid outcome retained");
        });
        Run("musketeer_adapter_debit_callback_throw_reports_paid_uncertain",()=>
        {
            var(e,p)=Setup();
            var r=MusketeerShop.PurchaseForAutoRestock(p,e.Banker,()=>throw new Exception("callback"),out _);
            Eq(r,MusketeerShop.AutoPurchaseResult.PaidUncertain,"callback failure still paid");
            Eq(PatchEconomy_Banker.TrySpendCalls,1,"only one debit"); Eq(MusketeerShop.SpawnCalls,0,"no unintended shipment");
        });
        Run("musketeer_stats_failure_cannot_retry_paid_shipment",()=>
        { var(e,p)=Setup(); e.M.stats.Throws=true; Advance(e); Advance(e); Eq(PatchEconomy_Banker.TrySpendCalls,1,"one paid sale"); Eq(MusketeerShop.SpawnCalls,1,"one shipment"); Eq(e.M.stats.Calls,1,"no statistics retry"); });
        Run("musketeer_unready_does_not_block_native_worker",()=>
        {
            var(e,p)=Setup(); MusketeerIdentity.Ready=false; Role(0,true,1); e.MakeShop(0,1);
            Advance(e); Eq(PatchEconomy_Banker.TrySpendCalls,1,"worker family still runs");
            Eq(e.Banker._stashedCoins,98,"worker price unchanged");
        });
        Run("musketeer_real_manual_receipt_native_cancel_allows_auto_resume",()=>
        {
            var(e,p)=Setup(); var player=new Player {selectedPayable=p,Pointer=(IntPtr)123}; e.Kingdom.playerOne=player;
            player._floatingCurrency.Add(new DroppableCurrency());
            p.selectedByP1=true;p.PlayerSelecting=player;MusketeerShop.ArmReceiptForTest(123);
            Advance(e);Eq(PatchEconomy_Banker.TrySpendCalls,0,"native player occupation blocks");
            player.selectedPayable=null;player._completingPayable=null;p.selectedByP1=false;p.PlayerSelecting=null;
            CheckReceipt(); Advance(e);Eq(PatchEconomy_Banker.TrySpendCalls,0,"unreturned real floating currency still blocks");
            player._floatingCurrency.Clear(); Advance(e);Eq(PatchEconomy_Banker.TrySpendCalls,1,"native refunded cancellation permits automatic recovery");
            Eq(MusketeerShop.SpawnCalls,1,"one shipment without modifying manual receipt");
            void CheckReceipt() => Ok(MusketeerShop.ReceiptArmedForTest(123),"real receipt intentionally remains armed after cancellation");
            CheckReceipt();
        });
        Run("musketeer_zero_count_readiness_transition_wakes_idle_planner",()=>
        {
            var(e,p)=Setup(); Role(0,true,1); AutoRestockCounts.Live[0]=1;
            MusketeerIdentity.Ready=false; Advance(e);
            Eq(PatchEconomy_Banker.TrySpendCalls,0,"satisfied worker and unknown gunner stay idle");
            MusketeerIdentity.Ready=true; Advance(e);
            Eq(PatchEconomy_Banker.TrySpendCalls,1,"ready zero wakes planner with unchanged balance");
            Eq(MusketeerShop.SpawnCalls,1,"one gun after readiness");
        });
    }
}
