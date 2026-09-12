using System;
using KingdomEnhancedMod;
internal static class Additional
{
 internal static void Run()
 {
  Test.Run("first_death_subscription_failure_latches_without_reseeding_every_tick",()=>{
   var e=new Env();var c=e.MakeCharacter("Worker");c._damageable.throwOnSubscribe=true;
   for(int i=0;i<20;i++)Test.Eq(e.R(),false,"untrusted counts");
   Test.Eq(e.Enums,2,"initial+one recovery only");
   c._damageable.throwOnSubscribe=false;var n=e.MakeCharacter("Worker");e.Kingdom.AddCharacter(n);AutoRestockCounts.HookAddCharacter(e.Kingdom,n);
   Test.Eq(e.R(),true,"new event recovery");Test.Eq(AutoRestockCounts.LiveCount(0),2,"full roster restored");
  });
  Test.Run("missing_role_damageable_never_publishes_undercount",()=>{
   var e=new Env();var c=e.MakeCharacter("Worker");c._damageable=null;c.gameObject.components.RemoveAll(x=>x is Damageable);
   for(int i=0;i<20;i++)Test.Eq(e.R(),false,"missing component invalidates cache");
   Test.Eq(e.Enums,2,"bounded retry");Test.Eq(AutoRestockCounts.LiveCount(0),0,"cache unavailable");
  });
  Test.Run("missing_shop_registry_latches_then_placed_event_recovers",()=>{
   var e=new Env();e.MakeCharacter("Worker");e.Planner._placedShops=null;
   for(int i=0;i<20;i++)Test.Eq(e.R(),false,"no speculative empty stock");Test.Eq(e.Enums,2,"bounded registry retry");
   e.SetPlaced();AutoRestockCounts.HookSetPlacedShop(e.Planner);Test.Eq(e.R(),true,"placement evidence recovers");
  });
 }
}
