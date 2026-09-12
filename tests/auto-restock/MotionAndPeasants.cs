using System;
using System.Linq;
using KingdomEnhancedMod;
using static AutoRestockTests.Program;

namespace AutoRestockTests;
internal static class MotionAndPeasants
{
    private static (Env e,PayableShop s,GameObject a) Start(int role=0,int target=1,int price=1,int balance=20)
    {
        var e=NewEnv(); Role(role,true,target); var s=e.MakeShop(role,price);
        e.Banker._stashedCoins=balance; var a=BankAssistantCoordinator.Actors.Peek(); e.Tick();
        Eq(Reserved,1,"order created"); return(e,s,a);
    }
    private static void Paid(Env e,PayableShop s)
    { Ok(PumpUntil(e,()=>s.TransactionCompleteCalls==1),"purchase occurs"); Eq(Reserved,1,"paid assistant stays leased"); }
    private static void Near(float got,float expected,string why)
    { Ok(Math.Abs(got-expected)<0.025f,$"{why}: got {got}, expected {expected}"); }

    internal static void Run()
    {
        Program.Run("motion_teleport_near_multiple_actual_frames_without_coin_or_debit",()=>{
            var(e,s,a)=Start(); Near(a.transform.position.x,5,"teleport near shop");
            Eq(Pool.SpawnCalls,0,"no arrival coin on assignment"); Eq(Spend,0,"no arrival debit");
            e.Frame(.1f); Near(a.transform.position.x,4.68f,"0.1 seconds at 3.2");
            e.Frame(.3f); Near(a.transform.position.x,3.72f,"additional 0.3 seconds at 3.2");
            for(int i=0;i<10;i++) e.Frame(.05f);
            Ok(a.transform.position.x>1.02f,"still approaching");
            Eq(Pool.SpawnCalls,0,"no coins before actual arrival"); Eq(Spend,0,"no early debit");
            Arrive(e); Eq(Pool.SpawnCalls,0,"arrival starts delay only");
            e.Frame(.1f); Eq(Pool.SpawnCalls,0,"start delay respected");
            e.Frame(.06f); Eq(Pool.SpawnCalls,1,"first coin after arrival delay");
        });
        Program.Run("motion_same_entry_side_preserves_actor_ground_y_z",()=>{
            var e=NewEnv();Role(0,true,1);var s=e.MakeShop(0,1);s.transform.position=new Vector3(10,30,40);
            var a=BankAssistantCoordinator.Actors.Peek();a.transform.position=new Vector3(-20,7,9);
            e.Banker._stashedCoins=10;e.Tick();Near(a.transform.position.x,5,"left entry five from shop");
            Near(a.transform.position.y,7,"entry ground Y");Near(a.transform.position.z,9,"entry Z");
            Ok(PumpUntil(e,()=>Pool.SpawnCalls==1),"coin after run-up");Near(a.transform.position.x,9,"left counter");
            Near(a.transform.position.y,7,"approach ground Y");Near(a.transform.position.z,9,"approach Z");
            Paid(e,s);e.Frame(.2f);Near(a.transform.position.x,8.68f,"depart further left");
            Near(a.transform.position.y,7,"departure ground Y");Near(a.transform.position.z,9,"departure Z");
        });
        Program.Run("motion_pause_freezes_approach_and_departure_scaled_timeout",()=>{
            var(e,s,a)=Start();e.Frame(.2f);float x=a.transform.position.x;float t=Time.time;
            Time.timeScale=0;for(int i=0;i<200;i++)e.Frame(.1f);
            Near(a.transform.position.x,x,"approach paused");Near(Time.time,t,"scaled time paused");Eq(Reserved,1,"no timeout during pause");Eq(Spend,0,"no paused debit");
            Time.timeScale=1;Paid(e,s);e.Frame(.1f);x=a.transform.position.x;
            Time.timeScale=0;for(int i=0;i<200;i++)e.Frame(.1f);
            Near(a.transform.position.x,x,"departure paused");Eq(Reserved,1,"departure lease held");
            Time.timeScale=1;Ok(PumpUntil(e,()=>Reserved==0),"resumed departure finishes");Eq(Spend,1,"one charge");
        });
        Program.Run("motion_paid_walks_out_before_home_without_target_or_price_cancel",()=>{
            var(e,s,a)=Start();Paid(e,s);Near(a.transform.position.x,1,"paid at counter");Eq(BankAssistantCoordinator.HomeTeleports,0,"no immediate teleport");
            s.Price=7;Role(0,true,0);e.Frame(.25f);Near(a.transform.position.x,1.4f,"walk 1.6 per second");Eq(Reserved,1,"price/target changes do not cancel departure");
            Ok(PumpUntil(e,()=>Reserved==0),"home after walk");
            Ok(BankAssistantCoordinator.Moves.Any(m=>m.Speed==1.6f&&Math.Abs(m.X-4)<.025f),"reached walk-out point before home");
            Near(a.transform.position.x,0,"home teleport");Eq(BankAssistantCoordinator.HomeTeleports,1,"one home teleport");Eq(Spend,1,"no second debit");Eq(s.TransactionCompleteCalls,1,"no second native call");
        });
        Program.Run("motion_departing_releases_budget_and_incoming_stock_but_holds_slot",()=>{
            var(e,s,a)=Start(target:2,price:3,balance:10);Paid(e,s);
            Eq(e.Banker._stashedCoins,4,"post-debit budget");Has(PatchEconomy_AutoRestock.GetSummary(0),"采购 0","paid order no longer incoming purchase");
            var second=e.MakeShop(0,2);AutoRestockCounts.SetStock(0,1);e.Frame();
            Eq(Reserved,2,"remaining four coins fund another shop while first departs");
            Eq(BankAssistantCoordinator.HomeTeleports,0,"first lease not released to create second");
            Ok(PumpUntil(e,()=>second.TransactionCompleteCalls==1&&Reserved==0,12),"both complete");
            Eq(Spend,2,"exactly two charges");Eq(e.Banker._stashedCoins,0,"full budget no overspend");
        });
        Program.Run("motion_same_shop_blocked_while_leaving",()=>{
            var(e,s,a)=Start(target:3);Paid(e,s);
            for(int i=0;i<10;i++)e.Frame(.1f);
            Eq(Reserved,1,"no duplicate same-shop lease");Eq(BankAssistantCoordinator.PlaceCalls,1,"no second placement while leaving");Eq(s.TransactionCompleteCalls,1,"no second purchase while leaving");
            Ok(PumpUntil(e,()=>s.TransactionCompleteCalls==2,10),"new order allowed after departure");
        });
        foreach(bool cleanup in new[]{false,true}) Program.Run(cleanup?"motion_cleanup_throw_paid_departure_no_retry":"motion_native_throw_paid_departure_no_retry",()=>{
            var e=NewEnv();Role(0,true,4);var s=e.MakeShop(0,2);e.Banker._stashedCoins=20;
            s.TransactionCompleteF=shop=>{if(cleanup){shop.PlayerSelecting=new Player();shop.DeselectThrows=true;}else throw new InvalidOperationException("native uncertain");};
            e.Tick();Paid(e,s);var a=BankAssistantCoordinator.Active[0].Actor;
            s.Price=9;Role(0,true,10);e.Frame(.2f);Near(a.transform.position.x,1.32f,"fault still walks away");Eq(Reserved,1,"fault retains departure lease");
            Ok(PumpUntil(e,()=>Reserved==0),"fault departure frees lease");
            for(int i=0;i<120;i++)e.Frame(.1f);
            Eq(Spend,1,"uncertain transaction not retried");Eq(s.TransactionCompleteCalls,1,"one native call");Eq(e.Banker._stashedCoins,16,"no blind refund");
        });
        Program.Run("motion_five_role_fairness_max_two_leases_and_exact_budget",()=>{
            var e=NewEnv();var shops=Enumerable.Range(0,5).Select(r=>{Role(r,true,1);return e.MakeShop(r,1);}).ToArray();
            e.AddAssistants(2);e.Banker._stashedCoins=10;e.Tick();
            Eq(Reserved,2,"global cap despite four idle actors");
            Ok(PumpUntil(e,()=>shops.All(s=>s.TransactionCompleteCalls==1)&&Reserved==0,30),"all five roles served");
            Eq(BankAssistantCoordinator.MaxActive,2,"max two concurrent leases");Eq(Spend,5,"each role exactly once");Eq(e.Banker._stashedCoins,0,"exact budget exhausted");
        });
        foreach(bool paid in new[]{false,true}) Program.Run(paid?"motion_paid_invalid_lease_releases_without_old_actor_move":"motion_approach_invalid_lease_releases_without_actor_move",()=>{
            var(e,s,a)=Start();if(paid)Paid(e,s);else e.Frame(.1f);float x=a.transform.position.x;
            BankAssistantCoordinator.InvalidLease=true;e.Frame();
            Eq(Reserved,0,"invalid lease relinquished");Near(a.transform.position.x,x,"invalid actor not teleported");Eq(BankAssistantCoordinator.HomeTeleports,0,"no invalid-context teleport");Eq(Spend,paid?1:0,"no additional charge");
        });
        Program.Run("motion_paid_world_change_releases_old_actor_without_moving",()=>{
            var(e,s,a)=Start();Paid(e,s);float x=a.transform.position.x;
            e.World.gameLayer=Fake.NewGO("replacement-layer").transform;e.Banker.transform.Parent=e.World.gameLayer;
            e.Frame();Eq(Reserved,0,"old world lease cleared");Near(a.transform.position.x,x,"old actor unchanged");Eq(BankAssistantCoordinator.HomeTeleports,0,"old actor not returned");Eq(Spend,1,"no further debit");
        });
        foreach(bool paid in new[]{false,true}) Program.Run(paid?"motion_departure_scaled_timeout_frees_lease":"motion_approach_scaled_timeout_frees_lease_without_payment",()=>{
            var(e,s,a)=Start();if(paid)Paid(e,s);BankAssistantCoordinator.FreezeMovement=true;
            float start=Time.time;for(int i=0;i<79;i++)e.Frame(.1f);
            Eq(Reserved,1,"held before eight scaled seconds");
            Ok(PumpUntil(e,()=>Reserved==0,.3f),"timeout releases near eight seconds");
            Ok(Time.time-start<8.25f,"bounded scaled timeout");Eq(Spend,paid?1:0,"timeout never retries paid transaction");
        });
        Program.Run("motion_move_exception_releases_without_debit",()=>{
            var(e,s,a)=Start();BankAssistantCoordinator.MoveThrows=true;e.Frame();Eq(Reserved,0,"movement exception releases");Eq(Spend,0,"no debit");
        });
        Program.Run("motion_displacement_after_arrival_prevents_coins_and_debit",()=>{
            var(e,s,a)=Start();Arrive(e);a.transform.position=new Vector3(3,0,0);e.Frame(.2f);
            Eq(Pool.SpawnCalls,0,"external displacement blocks coin");Eq(Spend,0,"external displacement blocks debit");Eq(Reserved,0,"displaced order cancelled");
        });
        foreach(bool coins in new[]{false,true}) Program.Run(coins?"motion_shop_shift_during_coins_cancels_without_debit":"motion_shop_shift_during_approach_cancels_without_debit",()=>{
            var(e,s,a)=Start(price:3);
            if(coins) { Arrive(e);e.Frame(.16f);Eq(Pool.SpawnCalls,1,"first coin emitted at original counter"); }
            else e.Frame(.1f);
            int emitted=Pool.SpawnCalls;s.transform.position=new Vector3(10,0,0);e.Frame(.21f);
            Eq(Spend,0,"moved shop cannot be paid remotely");Eq(s.TransactionCompleteCalls,0,"no native purchase");
            Eq(Pool.SpawnCalls,emitted,"no further coin to moved shop");Eq(Reserved,0,"moved shop order safely released");
        });
        Program.Run("motion_current_banker_identity_mismatch_blocks_payment",()=>{
            var(e,s,a)=Start();Arrive(e);var other=new Banker();Fake.Attach(other,Fake.NewGO("other-banker",e.Layer));
            BankAssistantCoordinator.MainBanker=other;e.Frame(.2f);Eq(Spend,0,"wrong supplied banker cannot spend");
            Eq(Reserved,0,"mismatched banker releases order");Eq(Pool.SpawnCalls,0,"no coins after identity mismatch");
        });
        Program.Run("peasant_live_bread_incoming_satisfy_target_and_truthful_status",()=>{
            var e=NewEnv();Role(4,true,10);e.MakeShop(4,1);e.Banker._stashedCoins=20;
            AutoRestockCounts.SetLive(4,6);AutoRestockCounts.SetStock(4,2);AutoRestockCounts.SetIncoming(4,2);e.Tick();
            Eq(Reserved,0,"6 live + 2 bread + 2 incoming covers 10");Has(PatchEconomy_AutoRestock.GetSummary(4),"招募中 2","incoming shown");Has(PatchEconomy_AutoRestock.GetSummary(4),"等待入籍","not falsely fulfilled");
            AutoRestockCounts.SetIncoming(4,0);AutoRestockCounts.SetStock(4,4);e.Tick();Has(PatchEconomy_AutoRestock.GetSummary(4),"面包已备足","bread ready status");
            AutoRestockCounts.SetLive(4,10);AutoRestockCounts.SetStock(4,0);e.Tick();Has(PatchEconomy_AutoRestock.GetSummary(4),"已达标","actual peasant target met");
            Eq(Spend,0,"no unnecessary debit");
        });
        Program.Run("peasant_actual_deficit_one_bread_purchase_counts_stock_and_incoming",()=>{
            var e=NewEnv();Role(4,true,10);var s=e.MakeShop(4,2);e.Banker._stashedCoins=4;
            AutoRestockCounts.SetLive(4,6);AutoRestockCounts.SetStock(4,2);AutoRestockCounts.SetIncoming(4,1);e.Tick();
            Eq(Reserved,1,"deficit exactly one");Ok(PumpUntil(e,()=>Reserved==0),"bread bought and helper home");
            Eq(s.TransactionCompleteCalls,1,"one bread purchase");Eq(AutoRestockCounts.StockCount(4),3,"bread count updated");Eq(Spend,1,"one debit");Eq(e.Banker._stashedCoins,0,"exact budget");
            Has(PatchEconomy_AutoRestock.GetSummary(4),"等待入籍","awaiting real recruitment");
        });
        Program.Run("peasant_incoming_added_before_payment_cancels_purchase",()=>{
            var(e,s,a)=Start(role:4);Arrive(e);AutoRestockCounts.SetIncoming(4,1);e.Frame(.2f);
            Eq(Spend,0,"incoming covers deficit before debit");Eq(Reserved,0,"redundant bread order cancelled");
        });
        Program.Run("peasant_requires_typed_baker_not_bread_tag_alone",()=>{
            var e=NewEnv();Role(4,true,1);e.Banker._stashedCoins=10;
            var fake=e.MakeShop(0,1);fake.itemPrefab.tag="Bread";e.Tick();Eq(Reserved,0,"generic bread tag not a baker");
            var baker=e.MakeShop(4,1);baker.itemPrefab.tag="Untagged";AutoRestockCounts.SetLive(4,0);e.Tick();
            Eq(Reserved,1,"typed baker classified without tag");
        });
    }
}
