using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using KingdomEnhancedMod;
internal static partial class Program
{
 private static void R2Probe()
 {
  ResetArchive();var host=Host.Create(1,VirginJsonA,true,0);BootVirginIsland(host);
  var payable=BootShop(2000f,4000);var first=host.CreateArcher("First",Side.Right);
  HeroArcherRuntime.Observe(first.Archer);Check(NativePayment(host,payable,true).Gate,"r2-first-paid");
  SavePaidHero(host,first,SavedJsonA,"first-id");Check(NativeGlobalSaveAllowed(),"r2-first-checkpoint");
  var diskPrefs=new Dictionary<string,string>(GlobalSaveData._loaded.prefs.contents);
  string diskJson=host.Island.Json;int diskWallet=host.Player.wallet.Coins;
  var sameGlobal=GlobalSaveData._loaded;
  host.Island.isNew=false;host.Island.playTimeDays=1;
  if(_scenario=="r2-before-any-id")
  {
   // Native Save has a catch after its target object table is cleared/created. A throw
   // before the first successful GetID must still invalidate this already-known source.
   var flags=BindingFlags.Static|BindingFlags.NonPublic;
   var before=typeof(HeroRecruitment.SavePatch).GetMethod("Before",flags);
   var after=typeof(HeroRecruitment.SavePatch).GetMethod("After",flags);
   var final=typeof(HeroRecruitment.SavePatch).GetMethod("Finally",flags);
   object[] args={1,1,0,null};before.Invoke(null,args);
   host.Island.Json="{\"land\":1,\"partial\":true}";host.Island.objects.Clear();
   IslandSaveData.CurrentlySavingIsland=host.Island;IslandSaveData.isSavingGame=true;
   after.Invoke(null,new[]{args[3]});final.Invoke(null,new[]{null,args[3]});
   Check(!NativeGlobalSaveAllowed(),"capture-failed-before-getid-must-block-async");
   Check(!NativeCoroutineSaveAllowed(),"capture-failed-before-getid-must-block-sync");
   return;
  }
  var second=host.CreateArcher("Second",Side.Left);Host.AdvanceFrame();HeroArcherRuntime.Observe(second.Archer);HeroRecruitment.Tick();HeroShop.Tick();
  Check(NativePayment(host,payable,true).Gate && HeroRecruitment.IsPurchased(second.Archer) && host.Player.wallet.Coins==4,"r2-second-paid-not-checkpointed",HeroRecruitment.DescribeForTests());
  host.Island.Json="{\"land\":1,\"first\":\"first-id\",\"second\":\"second-id\",\"wallet\":4}";
  var secondRow=new IslandSaveData.ObjectData{uniqueID="second-id",Root=second.Persistent};secondRow.componentData2.Add(new(){name="Character",type="CharacterData"});host.Island.objects.Add(secondRow);
  IslandSaveData.CurrentlySavingIsland=host.Island;IslandSaveData.isSavingGame=true;
  var failed=new HeroRecruitment.SaveCapture{Campaign=1,Land=1,Challenge=0};failed.Capture(first.Persistent,"first-id");failed.Capture(second.Persistent,"second-id");failed.Apply();
  Check(!NativeGlobalSaveAllowed(),"r2-late-failure-blocks");
  if(_scenario=="r2-cold-after-second-rejected")
  {
   // This is the separate real-cold-load model. Only the last accepted disk pair is restored;
   // the rejected in-memory second purchase and wallet=4 are not serialized or copied.
   var cold=Host.Create(1,diskJson,false,1);cold.Player.wallet.Coins=diskWallet;
   foreach(var pair in diskPrefs)GlobalSaveData._loaded.prefs.contents[pair.Key]=pair.Value;
   var restored=cold.CreateArcher("FirstFromDisk",Side.Right);
   var row=new IslandSaveData.ObjectData{uniqueID="first-id",Root=restored.Persistent};row.componentData2.Add(new(){name="Character",type="CharacterData"});cold.Island.objects.Add(row);
   var coldLoad=new HeroRecruitment.LoadCapture();coldLoad.Begin(cold.Island);coldLoad.Capture(row,restored.Persistent);coldLoad.End(true);
   Check(!ReferenceEquals(GlobalSaveData._loaded,sameGlobal) && cold.Player.wallet.Coins==12,"cold-uses-new-global-and-last-accepted-wallet12");
   Check(HeroRecruitment.IsPurchased(restored.Archer) && HeroRecruitment.DescribeForTests().Contains("seats=1"),"cold-restores-only-checkpointed-first-hero",HeroRecruitment.DescribeForTests());
   Check(!HeroRecruitment.IsPurchased(second.Archer) && NativeGlobalSaveAllowed(),"cold-does-not-retain-or-gift-unsaved-second-purchase");
   return;
  }
  Check(ReferenceEquals(GlobalSaveData._loaded,sameGlobal) && host.Player.wallet.Coins==4,"same-memory-global-and-two-real-payments-retained");
  var load=new HeroRecruitment.LoadCapture();load.Begin(host.Island);
  load.Capture(host.Island.objects[0],first.Persistent);load.Capture(secondRow,second.Persistent);load.End(true);
  Check(HeroRecruitment.IsPurchased(first.Archer) && HeroRecruitment.IsPurchased(second.Archer),"checkpoint-rebind-preserves-newer-paid-seat",HeroRecruitment.DescribeForTests());
  var retry=new HeroRecruitment.SaveCapture{Campaign=1,Land=1,Challenge=0};retry.Capture(first.Persistent,"first-id");retry.Capture(second.Persistent,"second-id");retry.MarkerSeen=true;retry.Apply();
  Check(NativeGlobalSaveAllowed(),"full-retry-settles-both-paid-receipts",HeroRecruitment.DescribeForTests());
  Check(host.Player.wallet.Coins==4,"newer-payment-not-refunded-or-recharged");
 }
}
