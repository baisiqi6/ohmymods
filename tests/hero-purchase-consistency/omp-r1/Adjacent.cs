using System;
using System.Linq;
using System.Reflection;
using KingdomEnhancedMod;
internal static partial class Program
{
 private static void FailPrefix(int campaign,int land)
 {
  var flags=BindingFlags.Static|BindingFlags.NonPublic;object[] args={campaign,land,0,null};
  typeof(HeroRecruitment.SavePatch).GetMethod("Before",flags).Invoke(null,args);
  typeof(HeroRecruitment.SavePatch).GetMethod("After",flags).Invoke(null,new[]{args[3]});
  typeof(HeroRecruitment.SavePatch).GetMethod("Finally",flags).Invoke(null,new[]{null,args[3]});
 }
 private static void Adjacent()
 {
  ResetArchive();var host=Host.Create(1,VirginJsonA,true,0);BootVirginIsland(host);
  var payable=BootShop(2000f,4000);var paid=host.CreateArcher("Paid",Side.Right);HeroArcherRuntime.Observe(paid.Archer);
  Check(NativePayment(host,payable,true).Gate,"adj-paid");SavePaidHero(host,paid,SavedJsonA,"paid-id");
  Check(NativeGlobalSaveAllowed(),"adj-checkpoint");host.Island.isNew=false;host.Island.playTimeDays=1;
  if(_scenario=="adj-failed-load-retains-source")
  {
   host.Island.Json="{\"land\":1,\"later\":true}";FailPrefix(1,1);
   Check(!NativeGlobalSaveAllowed(),"same-source-failure-blocks-before-load");
   var attempt=new HeroRecruitment.LoadCapture();attempt.Begin(host.Island);attempt.End(false);
   Check(HeroRecruitment.IsPurchased(paid.Archer) && !paid.Embarkee.enabled,"failed-load-restores-exact-prior-seat-and-borrow",HeroRecruitment.DescribeForTests());
   Check(!NativeGlobalSaveAllowed(),"failed-load-does-not-clear-source-responsibility");
   IslandSaveData.CurrentlySavingIsland=host.Island;IslandSaveData.isSavingGame=true;
   var retry=new HeroRecruitment.SaveCapture{Campaign=1,Land=1,Challenge=0};retry.Capture(paid.Persistent,"paid-id");retry.MarkerSeen=true;retry.Apply();
   Check(NativeGlobalSaveAllowed(),"failed-load-followed-by-complete-retry-recovers");return;
  }
  if(_scenario=="adj-empty-session")
  {
   paid.Damage.Kill();
   Check(HeroRecruitment.DescribeForTests().Contains("seats=0"),"terminal-death-empties-current-session");
   host.Island.Json="{\"land\":1,\"afterDeath\":true}";host.Island.objects.Clear();
   var ordinary=host.CreateArcher("Ordinary",Side.Left);
   var row=new IslandSaveData.ObjectData{uniqueID="ordinary-id",Root=ordinary.Persistent};row.componentData2.Add(new(){name="Character",type="CharacterData"});host.Island.objects.Add(row);
   FailPrefix(1,1);Check(!NativeGlobalSaveAllowed(),"failed-save-keeps-source-responsibility");
   var load=new HeroRecruitment.LoadCapture();load.Begin(host.Island);load.Capture(row,ordinary.Persistent);load.End(true);
   Check(HeroRecruitment.DescribeForTests().Contains("seats=0"),"known-empty-session-must-not-revive-dead-receipt",HeroRecruitment.DescribeForTests());
   IslandSaveData.CurrentlySavingIsland=host.Island;IslandSaveData.isSavingGame=true;
   var complete=new HeroRecruitment.SaveCapture{Campaign=1,Land=1,Challenge=0};complete.Capture(ordinary.Persistent,"ordinary-id");complete.MarkerSeen=true;complete.Apply();
   Check(NativeGlobalSaveAllowed(),"complete-empty-capture-settles-terminal-death",HeroRecruitment.DescribeForTests());return;
  }
  if(_scenario=="adj-live-alias-source")
  {
   string foreignReceipt=HeroRecruitment.DescribeForTests().Split('[')[1].Split(':')[0];
   var global=GlobalSaveData._loaded;var c1=host.Campaign;
   var host2=Host.Create(1,VirginJsonB,true,0);
   GlobalSaveData._loaded=global;global.campaigns.Add(host2.Campaign);global.currentCampaign=2;CampaignSaveData.current=host2.Campaign;
   BootVirginIsland(host2);var pay2=BootShop(3000f,6000);var second=host2.CreateArcher("OtherCampaignPaid",Side.Right);HeroArcherRuntime.Observe(second.Archer);
   Check(NativePayment(host2,pay2,true).Gate,"second-campaign-paid");
   host2.Island.Json="{\"land\":1,\"campaign\":2,\"saved\":true}";host2.Island.isNew=false;host2.Island.playTimeDays=1;
   var row=new IslandSaveData.ObjectData{uniqueID="other-paid-id",Root=second.Persistent};row.componentData2.Add(new(){name="Character",type="CharacterData"});host2.Island.objects.Add(row);
   IslandSaveData.CurrentlySavingIsland=host2.Island;IslandSaveData.isSavingGame=true;
   var save=new HeroRecruitment.SaveCapture{Campaign=2,Land=1,Challenge=0};save.Capture(second.Persistent,"other-paid-id");save.MarkerSeen=true;save.Apply();
   Check(NativeGlobalSaveAllowed(),"second-campaign-checkpoint");
   string ownReceipt=HeroRecruitment.DescribeForTests().Split('[')[1].Split(':')[0];
   host2.Island.Json="{\"land\":1,\"campaign\":2,\"later\":true}";FailPrefix(2,1);
   HeroNativeRights.BeforeCatalogMutation(global);global.campaigns.RemoveAt(0);global.currentCampaign=1;
   Check(!NativeGlobalSaveAllowed(),"surviving-source-retains-invalid-capture");
   var load=new HeroRecruitment.LoadCapture();load.Begin(host2.Island);load.Capture(row,second.Persistent);load.End(true);
   Check(HeroRecruitment.DescribeForTests().Contains(ownReceipt) && HeroRecruitment.IsPurchased(second.Archer),"numeric-slot-reuse-must-not-import-other-live-source","expected="+ownReceipt+" foreign="+foreignReceipt+" actual="+HeroRecruitment.DescribeForTests());return;
  }
  if(_scenario=="adj-alias-follows-slot-move")
  {
   // The paid source's runtime state must carry the lookup alias of its current catalog slot:
   // after a real slot move the state is re-installed once under the new key (and keeps its own
   // receipts/GUID/epoch), instead of every Tick re-entering the migration path with a stale key.
   var global=GlobalSaveData._loaded;
   string oldKey=HeroRecruitmentArchive.ContextKey(GlobalSaveData.filename,1,0,1);
   Check(HeroRecruitment.DescribeForTests().Contains("ctx="+oldKey.Substring(0,8)),
     "runtime-state-under-current-alias",HeroRecruitment.DescribeForTests());
   HeroNativeRights.BeforeCatalogMutation(global);
   global.campaigns.RemoveAt(0);global.currentCampaign=0;CampaignSaveData.current=global.campaigns[0];
   string newKey=HeroRecruitmentArchive.ContextKey(GlobalSaveData.filename,0,0,1);
   Check(newKey!=oldKey,"slot-move-produces-new-alias");
   Host.AdvanceFrame();HeroRecruitment.Tick();
   Check(HeroRecruitment.DescribeForTests().Contains("ctx="+newKey.Substring(0,8))
     && !HeroRecruitment.DescribeForTests().Contains("ctx="+oldKey.Substring(0,8)),
     "runtime-alias-follows-slot-move",HeroRecruitment.DescribeForTests());
   Host.AdvanceFrame();HeroRecruitment.Tick();
   Check(HeroRecruitment.DescribeForTests().Contains("ctx="+newKey.Substring(0,8))
     && HeroRecruitment.DescribeForTests().Contains("seats=1") && HeroRecruitment.IsPurchased(paid.Archer),
     "stable-tick-keeps-moved-alias-and-paid-seat",HeroRecruitment.DescribeForTests());
   return;
  }
  throw new Exception("unknown adj case");
 }
}
