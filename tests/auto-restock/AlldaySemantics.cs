using System;
using KingdomEnhancedMod;
namespace AutoRestockTests;

/// <summary>全天补货语义（2026-09-24 用户回退，恢复 2026-09-17 前长期在产行为）：
/// 夜间不再是自动补货的门。原“夜停摆”专项场景按全天语义翻转；不依赖昼夜的纯门型
/// 断言（原生付款门、最深层阻塞原因）原样保留。原“native 回调中途翻夜即停摆”类
/// 场景已删除——翻夜不再产生任何可观测行为差异。</summary>
internal static class AlldaySemantics
{
 internal static void Run()
 {
  // 夜间冷启动对全部 9 类照常服务：借人、下单、发动画币、扣款、发货。
  Program.Run("night_cold_start_serves_all_nine_roles", () =>
  {
   for (int role = 0; role < 9; role++)
   {
    var e = Program.NewEnv();
    e.Banker._stashedCoins = 100;
    e.Kingdom.isDaytime = false;
    Payable target = SetupDeficit(e, role);
    Func<int> shipped = role == 8
        ? () => MusketeerShop.SpawnCalls
        : () => target.TransactionCompleteCalls;
    e.Tick();
    Program.Ok(Program.PumpUntil(e, () => shipped() == 1, 20), "role " + role + ": night purchase completes");
    Program.Ok(BankAssistantCoordinator.PlaceCalls > 0, "role " + role + ": night borrows and places the tax assistant");
    Program.Eq(Program.Spend, 1, "role " + role + ": exactly one night debit");
    Program.Eq(PatchEconomy_Banker.TrySpendCalls, 1, "role " + role + ": exactly one debit attempt");
    Program.Ok(!PatchEconomy_AutoRestock.GetSummary(role).Contains("夜间暂停"),
        "role " + role + ": no paused text at night");
   }
  });
  // 夜幕在动画前/动画完成后落下：Approach 与 FinalWait 两相都继续完成采购。
  Program.Run("night_mid_phases_approach_and_finalwait_complete_purchase", () =>
  {
   // (a) Approach：订单已派出、尚未进店——夜幕不撤单。
   var e=Program.NewEnv();Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);
   PayableShop s=e.MakeShop(0,2);e.Banker._stashedCoins=10;e.Tick();
   Program.Eq(Program.Reserved,1,"order exists before nightfall");
   e.Kingdom.isDaytime=false;e.Tick();
   Program.Eq(Program.Reserved,1,"approach order survives nightfall");
   Program.Ok(Program.PumpUntil(e,()=>s.TransactionCompleteCalls==1&&Program.Reserved==0,20),"approach order completes at night");
   Program.Eq(Program.Spend,1,"single night debit");

   // (b) FinalWait：动画币已发完、等待最终扣款——夜幕同样不撤单。
   e=Program.NewEnv();Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);
   s=e.MakeShop(0,2);e.Banker._stashedCoins=10;e.Tick();
   float t0=Program.Arrive(e);
   e.SetTime(t0+.151f);e.SetTime(t0+.352f);e.SetTime(t0+.553f);e.SetTime(t0+.754f);
   Program.Eq(Program.Reserved,1,"order still pending at final wait");
   Program.Eq(Pool.SpawnCalls,4,"all animation coins already sent");
   e.Kingdom.isDaytime=false;
   Program.Ok(Program.PumpUntil(e,()=>s.TransactionCompleteCalls==1&&Program.Reserved==0,20),"final-wait order completes at night");
   Program.Eq(Program.Spend,1,"final-wait debit lands at night");
  });
  // 夜间摘要显示真实计数文案；关闭角色仍是已关闭；不再出现夜间暂停文案。
  Program.Run("night_summary_uses_real_counts", () =>
  {
   var e=Program.NewEnv();e.Kingdom.isDaytime=false;Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);
   e.MakeShop(0,2);e.Banker._stashedCoins=10;e.Tick();
   Program.Has(PatchEconomy_AutoRestock.GetSummary(0),"现有 2","enabled role shows real counts at night");
   Program.Eq(PatchEconomy_AutoRestock.GetSummary(1),"已关闭","disabled role stays closed at night");
   Program.Ok(!PatchEconomy_AutoRestock.GetSummary(0).Contains("夜间暂停"),"no paused text at night");
  });
  Program.Run("night_does_not_bypass_player_payment_guard", () =>
  {
   var e=Program.NewEnv();e.Kingdom.isDaytime=false;Program.Role(3,true,10);AutoRestockCounts.SetLive(3,7);var s=e.MakeShop(3,2);e.Banker._stashedCoins=100;s.selectedByP1=true;e.Tick();
   Program.Eq(Program.Reserved,0,"player selection blocks at night");
   Program.Has(PatchEconomy_AutoRestock.GetSummary(3),"玩家支付占用","truthful reason at night");
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
  // 摘要与昼夜彻底解耦：夜间/暂停/kingdom 缺失都不改变摘要口径。
  Program.Run("get_summary_independent_of_day", () =>
  {
   var e=Program.NewEnv();Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);
   e.Kingdom.isDaytime=false;
   Program.Eq(PatchEconomy_AutoRestock.GetSummary(0),"初始化中","night before any tick keeps init state");
   Program.Eq(PatchEconomy_AutoRestock.GetSummary(1),"已关闭","disabled role summary");
   Time.timeScale=0f; // 暂停中也不写入任何昼夜文案
   e.Tick();
   Program.Eq(PatchEconomy_AutoRestock.GetSummary(0),"初始化中","paused tick leaves summary untouched");
   Time.timeScale=1f;
   e.M.kingdom=null; // kingdom 缺失：摘要照常回退初始化口径
   Program.Eq(PatchEconomy_AutoRestock.GetSummary(0),"初始化中","missing kingdom keeps conservative state");
  });
 }

 /// <summary>每个角色一个可采购的缺口目标（0..5 店、6..7 弹药、8 火枪铺）。</summary>
 private static Payable SetupDeficit(Program.Env e, int role)
 {
  if (role == 8)
  {
   Program.Role(8, true, 1);
   ModConfig.MusketeerEnabled.Value = true;
   var gun = Program.Fake.Attach(new Payable { Price = 4, CanPayF = _ => false },
       Program.Fake.NewGO("gunshop", e.Layer));
   MusketeerShop.Setup(gun, e.Kingdom);
   return gun;
  }
  if (role == 6)
  {
   Program.Role(6, true, 2);
   SiegeAmmoCounts.SetCount(6, 0);
   return e.MakeAmmo(6, 5, 0);
  }
  if (role == 7)
  {
   Program.Role(7, true, 2);
   SiegeAmmoCounts.SetCount(7, 0);
   return e.MakeAmmo(7, 2, 0);
  }
  Program.Role(role, true, 3);
  AutoRestockCounts.SetLive(role, 2);
  return e.MakeShop(role, 2);
 }
}
