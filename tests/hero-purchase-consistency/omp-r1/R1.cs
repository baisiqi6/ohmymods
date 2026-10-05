using System;
using System.Linq;
using System.Collections.Generic;
using KingdomEnhancedMod;
internal static partial class Program
{
 private static void R1Probe()
 {
  ResetArchive();
  var host=Host.Create(1,VirginJsonA,true,0);
  if(_scenario=="r1-crud-three")
  {
   var g=GlobalSaveData._loaded;g.campaigns.Add(new CampaignSaveData());
   var epochs=new string[3];var seats=new HeroPurchaseReceipt[3];
   for(int i=0;i<3;i++)
   {
    g.currentCampaign=i;CampaignSaveData.current=g.campaigns[i];
    var island=new IslandSaveData{land=1,Json="{\"land\":1,\"account\":"+i+"}"};g.campaigns[i].CurrentIsland=island;Host.PlaceIsland(g.campaigns[i],island,1);
    epochs[i]=HeroRecruitmentArchive.NewScope();seats[i]=new(){Id=Guid.NewGuid(),Side=1,NativeId="paid"+i};
    Check(HeroNativeRights.Stage(HeroRecruitmentArchive.ContextKey(GlobalSaveData.filename,i,0,1),i,0,island,
      HeroRecruitmentFingerprint.Hash(island.Json,epochs[i]),epochs[i],new[]{seats[i]},true),"three-stage-"+i);
   }
   HeroNativeRights.BeforeCatalogMutation(g);g.campaigns.RemoveAt(0);
   g.currentCampaign=1;CampaignSaveData.current=g.campaigns[1];var surviving=CampaignSaveData.current.CurrentIsland;
   Check(NativeGlobalSaveAllowed(),"three-catalog-remap-saves");
   var key=HeroRecruitmentArchive.ContextKey(GlobalSaveData.filename,1,0,1);
   Check(HeroNativeRights.Resolve(key,1,0,1,surviving,surviving.Json,false,out var res,out var known,out _)
      &&known&&!res.Unresolved&&res.Seats.Single().Id==seats[2].Id,"three-last-survivor-resolves");
   Check(HeroNativeRights.Stage(key,1,0,surviving,HeroRecruitmentFingerprint.Hash(surviving.Json,epochs[2]),epochs[2],new[]{seats[2]},false),
      "three-last-survivor-can-save-despite-shifted-live-alias");
   return;
  }
  BootVirginIsland(host);var payable=BootShop(2000f,4000);var actor=host.CreateArcher("Paid",Side.Right);
  HeroArcherRuntime.Observe(actor.Archer);
  Check(NativePayment(host,payable,true).Gate,"r1-purchase");SavePaidHero(host,actor,SavedJsonA,"paid-id");
  Check(NativeGlobalSaveAllowed(),"r1-complete-checkpoint");
  var diskPrefs=new Dictionary<string,string>(GlobalSaveData._loaded.prefs.contents);string diskJson=host.Island.Json;
  host.Island.Json="{\"land\":1,\"hero\":\"paid-id\",\"later\":1}";
  IslandSaveData.CurrentlySavingIsland=host.Island;IslandSaveData.isSavingGame=true;
  var cap=new HeroRecruitment.SaveCapture{Campaign=1,Land=1,Challenge=0};cap.Capture(actor.Persistent,"paid-id");
  if(_scenario=="r1-invalid-read")GlobalSaveData._loaded.prefs.contents.ThrowOnce=true;
  cap.Apply(); // No native completion marker.
  if(_scenario=="r1-invalid-read")
  {
   Check(!NativeGlobalSaveAllowed(),"failed-capture-survives-transient-pref-read");
   Check(!NativeCoroutineSaveAllowed(),"failed-capture-keeps-sync-block-after-read-recovery");
   return;
  }
  Check(!NativeGlobalSaveAllowed(),"r1-invalid-capture-blocks");
  if(_scenario=="r1-coherent-disk")
  {
   // A denied save must NOT copy in-memory partial state to the simulated disk.
   // The next cold load receives the last successful pair, exactly as a rejected save implies.
   var cold=Host.Create(1,diskJson,false,1,host);
   foreach(var pair in diskPrefs)GlobalSaveData._loaded.prefs.contents[pair.Key]=pair.Value;
   var reloaded=cold.CreateArcher("Reload",Side.Right);var row=new IslandSaveData.ObjectData{uniqueID="paid-id",Root=reloaded.Persistent};
   row.componentData2.Add(new(){name="Character",type="CharacterData"});cold.Island.objects.Add(row);
   var load=new HeroRecruitment.LoadCapture();load.Begin(cold.Island);load.Capture(row,reloaded.Persistent);load.End(true);
   Check(HeroRecruitment.IsPurchased(reloaded.Archer),"denied-save-cold-load-restores-previous-coherent-paid-pair");
   Check(NativeGlobalSaveAllowed(),"denied-save-does-not-carry-fault-to-real-cold-load");
   return;
  }
  if(_scenario=="r1-reload-failed-source")
  {
   // A late capture failure can occur after complete rows exist but before the marker.
   // Re-enter this same source through the production load/identity hooks, on the same Global.
   host.Island.isNew=false;host.Island.playTimeDays=1;
   var row=host.Island.objects[0];var load=new HeroRecruitment.LoadCapture();
   load.Begin(host.Island);load.Capture(row,actor.Persistent);load.End(true);
   Check(HeroRecruitment.IsPurchased(actor.Archer),"return-to-source-can-rebind-paid-owner",HeroRecruitment.DescribeForTests());
   var retry=new HeroRecruitment.SaveCapture{Campaign=1,Land=1,Challenge=0};retry.Capture(actor.Persistent,"paid-id");retry.MarkerSeen=true;retry.Apply();
   Check(NativeGlobalSaveAllowed(),"return-to-source-complete-capture-recovers-save",HeroRecruitment.DescribeForTests());
   return;
  }
  throw new Exception("unknown r1 case");
 }
}
