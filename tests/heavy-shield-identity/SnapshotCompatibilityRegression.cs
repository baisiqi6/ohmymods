using KingdomEnhancedMod;using UnityEngine;using System.Text.Json;
internal static class SnapshotCompatibilityRegression
{
 private static long next=7000000;private static int checks;
 private static IntPtr Ptr()=>new(++next);
 private static GameObject Root(string name="")=>new(++next,(int)next,name);
 private static void Check(bool b,string label){checks++;if(!b)throw new Exception("snapshot compatibility: "+label);}
 internal static void Run()
 {
  foreach(string mode in new[]{"paid-proof-roundtrip","wrong-proof-owner","nonclock-change","missing-paid-row"})Case(mode);
  HeavyShieldNativeLoadProof.Owner=null;HeavyShieldNativeLoadProof.Proof=null;
  Console.WriteLine($"PASS paid legacy source→production restore→Stage/Prepare→cold load: {checks} checks (transport/native objects are controlled test seams)");
 }
 private static void Case(string mode)
 {
  string guid="aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb";
  string source="{\"land\":0,\"playTimeDays\":10.856266923767635,\"lastPlayedTimeDays\":345.0830674982473,\"_islandTimePlayed\":2584,\"objects\":[{\"uniqueID\":\"paid-shield\"}]}";
  var document=new HeavyShieldSaveDocument{Version=1,Campaigns=new(){new HeavyShieldSavedCampaign{Slot=0,Guid=guid,MoldReceipt="cccccccc-1111-2222-3333-dddddddddddd",Islands=new(){new HeavyShieldSavedIsland{Land=0,Challenge=0,SnapshotHash=HeavyShieldSaveCodec.SnapshotHash(guid,0,0,source),Claims=new(){new HeavyShieldSavedClaim{Receipt="eeeeeeee-1111-2222-3333-ffffffffffff",Side=HeavyShieldQuota.Side.Left,Phase=HeavyShieldSavedClaimPhase.Soldier,NativeId="paid-shield",Durability=2}}}}}}};
  Check(HeavyShieldSaveCodec.TrySerialize(document,out string key,out _),mode+" legacy payload");
  string globalJson="{\"prefs\":{\"srzEntries\":[{\"key\":"+JsonSerializer.Serialize(HeavyShieldSaveSchema.Key)+",\"val\":"+JsonSerializer.Serialize(key)+"}]},\"campaigns\":[{\"_islands\":["+source+"]}]}";
  Check(HeavyShieldLegacyLoadProof.TryCreate(globalJson,out var proof,out _),mode+" true original source witness");
  string live=source.Replace("10.856266923767635","10.856266923767637");if(mode=="nonclock-change")live=live.Replace("2584,\"objects\"","2584,\"rev\":2,\"objects\"");
  var island=new IslandSaveData{Pointer=Ptr(),land=0,playTimeDays=1,Json=live};island.objects.Add(new(){Pointer=Ptr(),uniqueID=mode=="missing-paid-row"?"other-unit":"paid-shield"});
  var campaign=new CampaignSaveData{Pointer=Ptr(),CurrentLand=0,CurrentIsland=island};campaign._islands.Add(island);
  var prefs=new PrefsSaveData{Pointer=Ptr()};prefs.contents[HeavyShieldSaveSchema.Key]=key;
  var global=new GlobalSaveData{Pointer=Ptr(),prefs=prefs};global.campaigns.Add(campaign);GlobalSaveData._loaded=global;
  Managers.Inst=new(){game=new(){Pointer=Ptr(),currentLand=0},world=new(){Pointer=Ptr(),gameLayer=Root().Add<Transform>()}};
  NetworkBigBoss.IsOnline=false;NetworkBigBoss.HasWorldAuth=true;ModConfig.Enabled.Value=ModConfig.HeavyShieldEnabled.Value=true;Time.timeScale=1;Game.SavingEnabled=true;
  HeavyShieldRuntime.AttachSucceeds=HeavyShieldRuntime.PreflightSucceeds=true;HeavyShieldRuntime.DeferAttach=false;
  HeavyShieldNativeLoadProof.Owner=mode=="wrong-proof-owner"?new GlobalSaveData{Pointer=Ptr()}:global;HeavyShieldNativeLoadProof.Proof=proof;
  var actorRoot=Root();var actor=actorRoot.Add<Archer>();var persistent=actorRoot.Add<Persistent>();
  var load=HeavyShieldPersistence.BeginNativeIslandLoad(island);var row=HeavyShieldPersistence.BeginNativeLoadRow(load,island.objects[0]);HeavyShieldPersistence.ObserveNativeLoadRow(load,island.objects[0],persistent);HeavyShieldPersistence.EndNativeLoadRow(row);HeavyShieldPersistence.EndNativeIslandLoad(load,true);HeavyShieldPersistence.TickBinding();
  Check(prefs.contents[HeavyShieldSaveSchema.Key]==key,mode+" loading does not rewrite wallet key");
  if(mode!="paid-proof-roundtrip"){
   Check(HeavyShieldIdentity.Current.Unknown,mode+" stays blocked");Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldRight),mode+" no new charge");Check(HeavyShieldIdentity.GetQuotaView().LeftOccupied==1,mode+" old paid seat preserved");return;
  }
  Check(load.Exact&&!HeavyShieldIdentity.Current.Unknown,"legacy clock drift verified at producer input");Check(HeavyShieldIdentity.TryGetSoldier(actor,out var handle),"same paid native row restored");Check(HeavyShieldIdentity.TryGetCombatState(handle,out var combat)&&combat.Durability==2,"wear state not reset");Check(HeavyShieldIdentity.GetQuotaView().LeftOccupied==1&&HeavyShieldIdentity.GetQuotaView().MoldUnlocked,"mold and paid seat preserved");
  IslandSaveData.isSavingGame=true;IslandSaveData.CurrentlySavingIsland=island;var save=HeavyShieldPersistence.BeginNativeIslandSave(0,0,0);HeavyShieldPersistence.ObserveNativeId(save,persistent,"paid-shield");HeavyShieldPersistence.ObserveNativeIslandCaptured(save,island);HeavyShieldPersistence.EndNativeIslandSave(save,true);IslandSaveData.isSavingGame=false;IslandSaveData.CurrentlySavingIsland=null;
  Check(HeavyShieldPersistence.CampaignRestorePending(HeavyShieldIdentity.Current),"new snapshot stays unpublished before native Prepare");var prepare=HeavyShieldPersistence.BeginNativePrefsPrepare(prefs);prefs.CopyToSerializedEntries();HeavyShieldPersistence.EndNativePrefsPrepare(prepare,true);
  Check(HeavyShieldSaveCodec.TryParse(prefs.SerializedContents[HeavyShieldSaveSchema.Key],out var saved,out _)&&saved.Campaigns[0].Islands[0].HashKind==2&&saved.Campaigns[0].Islands[0].Claims[0].Durability==2,"published paid record canonical with exact wear");
  string durable=prefs.SerializedContents[HeavyShieldSaveSchema.Key];var cold=new IslandSaveData{Pointer=Ptr(),land=0,playTimeDays=1,Json=source.Replace("10.856266923767635","99.125")};cold.objects.Add(new(){Pointer=Ptr(),uniqueID="paid-shield"});var coldCampaign=new CampaignSaveData{Pointer=Ptr(),CurrentLand=0,CurrentIsland=cold};coldCampaign._islands.Add(cold);var coldPrefs=new PrefsSaveData{Pointer=Ptr()};coldPrefs.contents[HeavyShieldSaveSchema.Key]=durable;var coldGlobal=new GlobalSaveData{Pointer=Ptr(),prefs=coldPrefs};coldGlobal.campaigns.Add(coldCampaign);GlobalSaveData._loaded=coldGlobal;HeavyShieldNativeLoadProof.Owner=null;HeavyShieldNativeLoadProof.Proof=null;
  actorRoot=Root();actor=actorRoot.Add<Archer>();persistent=actorRoot.Add<Persistent>();load=HeavyShieldPersistence.BeginNativeIslandLoad(cold);row=HeavyShieldPersistence.BeginNativeLoadRow(load,cold.objects[0]);HeavyShieldPersistence.ObserveNativeLoadRow(load,cold.objects[0],persistent);HeavyShieldPersistence.EndNativeLoadRow(row);HeavyShieldPersistence.EndNativeIslandLoad(load,true);HeavyShieldPersistence.TickBinding();
  Check(load.Exact&&!HeavyShieldIdentity.Current.Unknown,"canonical cold load without legacy bridge");Check(HeavyShieldIdentity.TryGetSoldier(actor,out handle)&&HeavyShieldIdentity.TryGetCombatState(handle,out combat)&&combat.Durability==2,"cold role and wear intact");Check(HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldRight),"other free seat still purchasable");
 }
}
