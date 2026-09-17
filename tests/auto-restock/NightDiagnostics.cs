using System;
using KingdomEnhancedMod;
namespace AutoRestockTests;
internal static class NightDiagnostics
{
 internal static void Run()
 {
  // 产品规则：只有天亮后的白天税收官补货（原生 Kingdom.isDaytime）。夜间冷启动对
  // 全部 9 类都不借人、不规划、不下单、不发动画币、不扣款；天亮后同一缺口照常补货。
  Program.Run("night_cold_start_blocks_all_nine_roles_and_day_resumes", () =>
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
    Program.Eq(Program.Reserved, 0, "role " + role + ": night borrows no tax assistant");
    Program.Eq(BankAssistantCoordinator.PlaceCalls, 0, "role " + role + ": night places no assistant");
    Program.Eq(PatchEconomy_Banker.TrySpendCalls, 0, "role " + role + ": night never reaches the debit gate");
    Program.Eq(Pool.SpawnCalls, 0, "role " + role + ": night sends no animation coin");
    Program.Eq(shipped(), 0, "role " + role + ": night ships nothing");
    Program.Has(PatchEconomy_AutoRestock.GetSummary(role), "夜间暂停，等待天亮", "role " + role + ": night summary");
    e.Kingdom.isDaytime = true;
    e.Tick();
    Program.Ok(Program.PumpUntil(e, () => shipped() == 1), "role " + role + ": day purchase after dawn");
    Program.Eq(Program.Spend, 1, "role " + role + ": exactly one day debit");
    Program.Eq(PatchEconomy_Banker.TrySpendCalls, 1, "role " + role + ": exactly one debit attempt");
   }
  });
  // 夜幕在动画前/动画完成后落下：Approach 与 FinalWait 两相都必须在扣款前撤单。
  Program.Run("night_mid_phases_approach_and_finalwait_cancel_without_debit", () =>
  {
   // (a) Approach：订单已派出、尚未进店
   var e=Program.NewEnv();Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);
   PayableShop s=e.MakeShop(0,2);e.Banker._stashedCoins=10;e.Tick();
   Program.Eq(Program.Reserved,1,"day order exists");
   int teleports=BankAssistantCoordinator.HomeTeleports;
   e.Kingdom.isDaytime=false;e.Tick();
   Program.Eq(Program.Reserved,0,"approach order withdrawn at nightfall");
   Program.Eq(Pool.SpawnCalls,0,"approach withdrawal sends no coin");
   Program.Eq(Program.Spend,0,"approach withdrawal debits nothing");
   Program.Eq(s.TransactionCompleteCalls,0,"approach withdrawal never buys");
   Program.Ok(BankAssistantCoordinator.HomeTeleports==teleports+1,"approach withdrawal sends the assistant home");

   // (b) FinalWait：动画币已发完、等待最终扣款
   e=Program.NewEnv();Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);
   s=e.MakeShop(0,2);e.Banker._stashedCoins=10;e.Tick();
   float t0=Program.Arrive(e);
   e.SetTime(t0+.151f);e.SetTime(t0+.352f);e.SetTime(t0+.553f);e.SetTime(t0+.754f);
   Program.Eq(Program.Reserved,1,"order still pending at final wait");
   Program.Eq(Pool.SpawnCalls,4,"all animation coins already sent");
   e.Kingdom.isDaytime=false;e.Tick();
   Program.Eq(Program.Reserved,0,"final-wait order withdrawn at nightfall");
   Program.Eq(Program.Spend,0,"final-wait withdrawal never debits");
   Program.Eq(s.TransactionCompleteCalls,0,"final-wait withdrawal never buys");
   for(int i=0;i<30;i++) e.Frame();
   Program.Eq(Program.Spend,0,"no late debit across night frames");
   Program.Eq(s.TransactionCompleteCalls,0,"no late purchase across night frames");
  });
  // 夜间摘要：启用角色等待天亮；关闭角色仍是已关闭；天亮后回到真实计数文案。
  Program.Run("night_summary_paused_then_rebuilt_at_dawn", () =>
  {
   var e=Program.NewEnv();e.Kingdom.isDaytime=false;Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);
   e.MakeShop(0,2);e.Banker._stashedCoins=10;e.Tick();
   Program.Has(PatchEconomy_AutoRestock.GetSummary(0),"夜间暂停，等待天亮","enabled role waits for dawn");
   Program.Eq(PatchEconomy_AutoRestock.GetSummary(1),"已关闭","disabled role stays closed at night");
   e.Kingdom.isDaytime=true;e.Tick();
   Program.Has(PatchEconomy_AutoRestock.GetSummary(0),"现有 2","day summary rebuilt from counts");
   Program.Ok(!PatchEconomy_AutoRestock.GetSummary(0).Contains("夜间暂停"),"night text does not persist into day");
  });
  Program.Run("night_does_not_bypass_player_payment_guard", () =>
  {
   var e=Program.NewEnv();e.Kingdom.isDaytime=false;Program.Role(3,true,10);AutoRestockCounts.SetLive(3,7);var s=e.MakeShop(3,2);e.Banker._stashedCoins=100;s.selectedByP1=true;e.Tick();
   Program.Eq(Program.Reserved,0,"night starts no order regardless of occupancy");
   Program.Has(PatchEconomy_AutoRestock.GetSummary(3),"夜间暂停，等待天亮","night pause is the reported state");
   e.Kingdom.isDaytime=true;e.Tick();
   Program.Eq(Program.Reserved,0,"player selection still blocks in daylight");
   Program.Has(PatchEconomy_AutoRestock.GetSummary(3),"玩家支付占用","truthful reason in daylight");
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
  // 复核点：native/协作回调在 Tick 中途翻夜时，同一 tick 不得继续规划/借人/发币/扣款。
  Program.Run("refresh_callback_night_flip_stops_planning_same_tick", () =>
  {
   var e=Program.NewEnv();Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);
   var s=e.MakeShop(0,2);e.Banker._stashedCoins=10;
   AutoRestockCounts.OnRefresh = () => e.Kingdom.isDaytime = false; // 税收刷新回调中翻夜
   e.Tick();
   Program.Eq(Program.Reserved,0,"refresh flip never creates an order");
   Program.Eq(Program.Spend,0,"refresh flip never debits");
   Program.Eq(s.TransactionCompleteCalls,0,"refresh flip never buys");
   Program.Has(PatchEconomy_AutoRestock.GetSummary(0),"夜间暂停，等待天亮","night reported after refresh flip");
  });
  Program.Run("reserve_callback_night_flip_releases_borrowed_assistant", () =>
  {
   var e=Program.NewEnv();Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);
   var s=e.MakeShop(0,2);e.Banker._stashedCoins=10;
   BankAssistantCoordinator.OnReserve = () => e.Kingdom.isDaytime = false; // 借人（可能含存币传送）回调翻夜
   e.Tick();
   Program.Eq(Program.Reserved,0,"reserve flip leaves no order");
   Program.Eq(Program.Spend,0,"reserve flip never debits");
   Program.Eq(s.TransactionCompleteCalls,0,"reserve flip never buys");
   Program.Ok(BankAssistantCoordinator.Releases.Count>0
       && BankAssistantCoordinator.Releases[BankAssistantCoordinator.Releases.Count-1].ReturnHome,
       "borrowed assistant returned home");
   Program.Eq(BankAssistantCoordinator.Actors.Count,2,"no actor leak");
   Program.Has(PatchEconomy_AutoRestock.GetSummary(0),"夜间暂停，等待天亮","night reported after reserve flip");
  });
  Program.Run("place_callback_night_flip_releases_borrowed_assistant", () =>
  {
   var e=Program.NewEnv();Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);
   var s=e.MakeShop(0,2);e.Banker._stashedCoins=10;
   BankAssistantCoordinator.OnPlace = () => e.Kingdom.isDaytime = false; // 到店传送回调翻夜
   e.Tick();
   Program.Eq(Program.Reserved,0,"place flip leaves no order");
   Program.Eq(Program.Spend,0,"place flip never debits");
   Program.Eq(s.TransactionCompleteCalls,0,"place flip never buys");
   Program.Ok(BankAssistantCoordinator.Releases.Count>0
       && BankAssistantCoordinator.Releases[BankAssistantCoordinator.Releases.Count-1].ReturnHome,
       "placed assistant returned home");
   Program.Eq(BankAssistantCoordinator.Actors.Count,2,"no actor leak");
   Program.Has(PatchEconomy_AutoRestock.GetSummary(0),"夜间暂停，等待天亮","night reported after place flip");
  });
  Program.Run("move_callback_night_flip_stops_same_tick_orders_and_coins", () =>
  {
   var e=Program.NewEnv();Program.Role(0,true,2);Program.Role(1,true,2);
   AutoRestockCounts.SetLive(0,1);AutoRestockCounts.SetLive(1,1);
   PayableShop a=e.MakeShop(0,2);PayableShop b=e.MakeShop(1,2);
   e.Banker._stashedCoins=20;e.Tick();
   Program.Eq(Program.Reserved,2,"two orders in flight in daylight");
   BankAssistantCoordinator.OnMove = () => e.Kingdom.isDaytime = false; // 首次移动回调翻夜
   int coins=Pool.SpawnCalls;
   e.Frame();
   Program.Eq(Program.Reserved,0,"move flip releases every order in the same tick");
   Program.Eq(Program.Spend,0,"move flip never debits");
   Program.Eq(a.TransactionCompleteCalls,0,"role0 never buys after flip");
   Program.Eq(b.TransactionCompleteCalls,0,"role1 never buys after flip");
   Program.Eq(Pool.SpawnCalls,coins,"no animation coin after the flip");
   for(int i=0;i<30;i++) e.Frame();
   Program.Eq(Program.Spend,0,"night frames stay debit-free");
   Program.Eq(Pool.SpawnCalls,coins,"night frames stay coin-free");
  });
  Program.Run("coin_night_flip_discards_visual_coin_without_pay", () =>
  {
   // (a) prefab 查询回调翻夜：Spawn 前复核，绝不产生新币。
   var e=Program.NewEnv();Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);
   var s=e.MakeShop(0,2);e.Banker._stashedCoins=10;e.Tick();
   float t0=Program.Arrive(e);
   e.Currency.OnGetPrefab = () => e.Kingdom.isDaytime = false;
   int coins=Pool.SpawnCalls;
   e.SetTime(t0+.30f);
   Program.Eq(Pool.SpawnCalls,coins,"prefab flip spawns no coin");
   Program.Eq(Program.Reserved,0,"prefab flip cancels the order");
   Program.Eq(Program.Spend,0,"prefab flip never debits");
   Program.Eq(s.TransactionCompleteCalls,0,"prefab flip never buys");

   // (b) Spawn 内回调翻夜：刚生成的动画币按 fake+despawn 释放，不 MoveTo、不扣款。
   e=Program.NewEnv();Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);
   s=e.MakeShop(0,2);e.Banker._stashedCoins=10;e.Tick();
   t0=Program.Arrive(e);
   Pool.OnSpawn = () => e.Kingdom.isDaytime = false;
   e.SetTime(t0+.30f);
   Program.Eq(Program.Reserved,0,"spawn flip cancels the order");
   Program.Eq(Program.Spend,0,"spawn flip never debits");
   Program.Eq(s.TransactionCompleteCalls,0,"spawn flip never buys");
   Program.Ok(Pool.LastCoin!=null && Pool.LastCoin.FakeSet,"spawned coin marked fake");
   Program.Eq(Pool.LastCoin.MoveToCalls,0,"spawned coin never reaches MoveTo");
   Program.Ok(Pool.DespawnCalls>=1,"spawned coin despawned");
   for(int i=0;i<30;i++) e.Frame();
   Program.Eq(Program.Spend,0,"night frames after spawn flip stay debit-free");
   Program.Eq(s.TransactionCompleteCalls,0,"no late purchase");
  });
  Program.Run("get_summary_follows_current_day_without_tick", () =>
  {
   var e=Program.NewEnv();Program.Role(0,true,3);AutoRestockCounts.SetLive(0,2);
   e.Kingdom.isDaytime=false;
   Program.Has(PatchEconomy_AutoRestock.GetSummary(0),"夜间暂停，等待天亮","enabled role waits before any tick");
   Program.Eq(PatchEconomy_AutoRestock.GetSummary(1),"已关闭","disabled role never shown as night-waiting");
   Time.timeScale=0f; // 暂停中入夜：Tick 提前返回也不留旧文案
   e.Tick();
   Program.Has(PatchEconomy_AutoRestock.GetSummary(0),"夜间暂停，等待天亮","paused night reported without tick refresh");
   Time.timeScale=1f;
   e.Kingdom.isDaytime=true;
   Program.Eq(PatchEconomy_AutoRestock.GetSummary(0),"初始化中","daylight before any tick keeps init state");
   e.M.kingdom=null; // day 读取未知：保持保守状态，不写夜间文案
   Program.Eq(PatchEconomy_AutoRestock.GetSummary(0),"初始化中","unknown day read keeps conservative state");
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
