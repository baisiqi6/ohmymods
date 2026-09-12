using System;
using KingdomEnhancedMod;
namespace AutoRestockTests;
internal static class NightDiagnostics
{
 internal static void Run()
 {
  Program.Run("night_can_create_new_ninja_order_and_pay",()=>{
   var e=Program.NewEnv();e.Kingdom.isDaytime=false;Program.Role(2,true,5);AutoRestockCounts.SetLive(2,4);var s=e.MakeShop(2,2);e.Banker._stashedCoins=10;e.Tick();
   Program.Eq(Program.Reserved,1,"night starts order");Program.Ok(Program.PumpUntil(e,()=>s.TransactionCompleteCalls==1),"night completes");Program.Eq(Program.Spend,1,"one atomic debit");
  });
  Program.Run("night_does_not_bypass_player_payment_guard",()=>{
   var e=Program.NewEnv();e.Kingdom.isDaytime=false;Program.Role(3,true,10);AutoRestockCounts.SetLive(3,7);var s=e.MakeShop(3,2);e.Banker._stashedCoins=100;s.selectedByP1=true;e.Tick();
   Program.Eq(Program.Reserved,0,"selection blocks");Program.Has(PatchEconomy_AutoRestock.GetSummary(3),"玩家支付占用","truthful reason");
  });
  Program.Run("native_pay_gate_not_reported_as_insufficient_gold",()=>{
   var e=Program.NewEnv();Program.Role(2,true,15);AutoRestockCounts.SetLive(2,4);var s=e.MakeShop(2,2);e.Banker._stashedCoins=100;s.CanPayF=_=>false;e.Tick();
   Program.Eq(Program.Reserved,0,"native gate blocks");Program.Has(PatchEconomy_AutoRestock.GetSummary(2),"原生付款暂不可用","native gate reason");
  });
  Program.Run("best_shop_reason_is_deepest_gate_reached",()=>{
   var e=Program.NewEnv();Program.Role(2,true,15);AutoRestockCounts.SetLive(2,4);e.Banker._stashedCoins=100;
   var shallow=e.MakeShop(2,2);shallow.forceBlockPayment=true;var deep=e.MakeShop(2,2);deep.CanPayF=_=>false;e.Tick();
   Program.Has(PatchEconomy_AutoRestock.GetSummary(2),"原生付款暂不可用","deepest gate wins");
  });
 }
}
