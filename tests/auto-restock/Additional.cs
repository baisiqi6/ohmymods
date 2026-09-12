using System;
using KingdomEnhancedMod;
namespace AutoRestockTests;
internal static class Additional
{
 internal static void Run()
 {
  Program.Run("native_price_increase_synthetic_selection_cleared_allows_repeat_purchase",()=>{
   var e=Program.NewEnv();Program.Role(0,true,4);AutoRestockCounts.SetLive(0,2);var s=e.MakeShop(0,1);e.Banker._stashedCoins=20;s.priceIncrease=1;
   var selected=new Player();s.TransactionCompleteF=shop=>{shop.PlayerSelecting=selected;shop.selectedByP1=true;shop.interactingPlayer=selected;shop.Price++;};
   e.Tick();Program.Ok(Program.PumpUntil(e,()=>s.TransactionCompleteCalls==2),"same shop purchased twice");
   Program.Eq(s.DeselectCalls,2,"native synthetic select removed twice");Program.Ok(s.PlayerSelecting==null&&!s.selectedByP1&&s.interactingPlayer==null,"no leaked player interaction");
   Program.Eq(e.Banker._stashedCoins,14,"doubled rising shop prices 2+4 charged");
  });
  Program.Run("actual_new_player_selection_is_preserved_after_purchase",()=>{
   var e=Program.NewEnv();Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);var s=e.MakeShop(0,1);e.Banker._stashedCoins=20;
   var selected=new Player();s.TransactionCompleteF=shop=>{selected.selectedPayable=shop;shop.PlayerSelecting=selected;shop.selectedByP1=true;shop.interactingPlayer=selected;};
   e.Tick();Program.Ok(Program.PumpUntil(e,()=>s.TransactionCompleteCalls==1),"purchase completes");Program.Eq(s.DeselectCalls,0,"new actual selection protected");Program.Ok(s.selectedByP1&&s.PlayerSelecting==selected,"actual player state unchanged");
  });
  Program.Run("offline_price_increase_requires_crowned_player_before_debit",()=>{
   var e=Program.NewEnv();Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);var s=e.MakeShop(0,1);e.Banker._stashedCoins=20;s.priceIncrease=1;e.Kingdom.CrownFinder=_=>null;e.Tick();
   Program.Eq(Program.Reserved,0,"offline native Select(null) prevented");Program.Eq(Program.Spend,0,"no debit");
  });
  Program.Run("final_deficit_uses_other_shop_stock",()=>{
   var e=Program.NewEnv();Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);var s=e.MakeShop(0,1);var other=e.MakeShop(0,2);e.Banker._stashedCoins=20;e.Tick();
   other.Items=1;AutoRestockCounts.SetStock(0,1);Program.PumpUntil(e,()=>Program.Reserved==0);Program.Eq(s.TransactionCompleteCalls,0,"other shop fills deficit");Program.Eq(Program.Spend,0,"no redundant debit");
  });
  Program.Run("fault_records_replaced_gameobject_instance_on_reused_pointer",()=>{
   var e=Program.NewEnv();Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);var s=e.MakeShop(0,1);e.Banker._stashedCoins=20;s.TransactionCompleteF=_=>throw new InvalidOperationException("native fail");e.Tick();
   Program.Ok(Program.PumpUntil(e,()=>s.TransactionCompleteCalls==1),"first instance fault");
   var replacement=Program.Fake.NewGO("replacement",e.Layer);replacement.Pointer=s.gameObject.Pointer;Program.Fake.Attach(s,replacement);
   Time.time+=3;e.Tick();Program.Ok(Program.PumpUntil(e,()=>s.TransactionCompleteCalls==2),"new instance can be attempted");
   for(int i=0;i<5;i++){Time.time+=3;e.Tick();}Program.Eq(s.TransactionCompleteCalls,2,"new instance fault is sticky too");Program.Eq(Program.Spend,2,"no repeated debits of replacement");
  });
 }
}
