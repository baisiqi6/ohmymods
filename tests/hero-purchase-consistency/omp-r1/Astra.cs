using System;
using System.Collections.Generic;
using System.Linq;
using KingdomEnhancedMod;

internal static partial class Program
{
    private static void AstraProbe()
    {
        ResetArchive();
        var host=Host.Create(1,VirginJsonA,true,0);
        if (_scenario=="astra-read-retry")
        {
            Check(HeroNativeRights.Available && NativeGlobalSaveAllowed(),"healthy-global");
            GlobalSaveData._loaded.prefs.contents.ThrowOnce=true;
            Check(!HeroNativeRights.Available,"one-read-exception-fails-closed");
            Check(HeroNativeRights.Available,"next-healthy-read-recovers");
            Check(NativeGlobalSaveAllowed(),"next-healthy-save-recovers");
            return;
        }
        if (_scenario=="astra-crud-two")
        {
            var global=GlobalSaveData._loaded;
            var c0=global.campaigns[0]; var c1=global.campaigns[1];
            var i0=new IslandSaveData {land=1,Json="{\"land\":1,\"owner\":0}"};
            var i1=new IslandSaveData {land=1,Json="{\"land\":1,\"owner\":1}"};
            c0.CurrentIsland=i0;c1.CurrentIsland=i1;Host.PlaceIsland(c0,i0,1);Host.PlaceIsland(c1,i1,1);
            string k0=HeroRecruitmentArchive.ContextKey(GlobalSaveData.filename,0,0,1);
            string k1=HeroRecruitmentArchive.ContextKey(GlobalSaveData.filename,1,0,1);
            var r0=new HeroPurchaseReceipt {Id=Guid.NewGuid(),Side=1,NativeId="owner0"};
            var r1=new HeroPurchaseReceipt {Id=Guid.NewGuid(),Side=1,NativeId="owner1"};
            global.currentCampaign=0;CampaignSaveData.current=c0;
            var e0=HeroRecruitmentArchive.NewScope();
            Check(HeroNativeRights.Stage(k0,0,0,i0,HeroRecruitmentFingerprint.Hash(i0.Json,e0),e0,new[]{r0},true),"stage-paid-slot0");
            global.currentCampaign=1;CampaignSaveData.current=c1;
            var e1=HeroRecruitmentArchive.NewScope();
            Check(HeroNativeRights.Stage(k1,1,0,i1,HeroRecruitmentFingerprint.Hash(i1.Json,e1),e1,new[]{r1},true),"stage-paid-slot1");
            HeroNativeRights.BeforeCatalogMutation(global);global.campaigns.RemoveAt(0);global.currentCampaign=0;
            Check(NativeGlobalSaveAllowed(),"save-remapped-catalog");
            bool ok=HeroNativeRights.Resolve(k0,0,0,1,i1,i1.Json,false,out var resolved,out bool known,out _);
            Check(ok&&known&&!resolved.Unresolved&&resolved.Seats.Single().Id==r1.Id,"survivor-keeps-rights-after-deleting-earlier-paid-slot",resolved?.Kind??"null");
            var saved=new Dictionary<string,string>(global.prefs.contents);
            var cold=Host.Create(1,i1.Json,false,1);
            GlobalSaveData._loaded.campaigns.RemoveAt(0);GlobalSaveData._loaded.currentCampaign=0;
            foreach(var pair in saved)GlobalSaveData._loaded.prefs.contents[pair.Key]=pair.Value;
            ok=HeroNativeRights.Resolve(k0,0,0,1,cold.Island,cold.Island.Json,false,out resolved,out known,out _);
            Check(ok&&known&&!resolved.Unresolved&&resolved.Seats.Single().Id==r1.Id,"cold-survivor-keeps-rights",resolved?.Kind??"null");
            return;
        }
        if (_scenario=="astra-delete-pending")
        {
            BootVirginIsland(host);
            var payable=BootShop(2000f,4000);var actor=host.CreateArcher("Paid",Side.Right);
            HeroArcherRuntime.Observe(actor.Archer);
            Check(NativePayment(host,payable,true).Gate,"purchase-not-yet-captured");
            Check(!NativeGlobalSaveAllowed(),"pending-purchase-correctly-blocks-wallet-only-save");
            // A real campaign deletion is a terminal authority for that campaign's own rights.
            // There is no reason to retain this deleted campaign's unsaved receipt as a global lock.
            var global=GlobalSaveData._loaded;
            HeroNativeRights.BeforeCatalogMutation(global);
            global.campaigns.RemoveAt(1);global.currentCampaign=0;
            CampaignSaveData.current=global.campaigns[0];
            CampaignSaveData.current.CurrentIsland=new IslandSaveData{land=1,Json=VirginJsonA,isNew=true};
            Check(NativeGlobalSaveAllowed(),"deleting-pending-campaign-releases-only-its-global-save-block");
            Check(NativeCoroutineSaveAllowed(),"deleted-campaign-does-not-block-sync-global-save");
            return;
        }
        if (_scenario=="astra-poststage-partial")
        {
            BootVirginIsland(host);
            var payable=BootShop(2000f,4000);var actor=host.CreateArcher("Paid",Side.Right);
            HeroArcherRuntime.Observe(actor.Archer);
            Check(NativePayment(host,payable,true).Gate,"purchase");
            SavePaidHero(host,actor,SavedJsonA,"paid-id");
            Check(NativeGlobalSaveAllowed(),"first-complete-capture-saves");
            var saved=new Dictionary<string,string>(GlobalSaveData._loaded.prefs.contents);
            string diskJson=host.Island.Json; // Freeze the last accepted disk pair, before failure.
            // Later native IslandSave catches an exception internally, leaves a changed partial
            // object snapshot, and never emits its completion marker. No new purchase is pending.
            host.Island.Json="{\"land\":1,\"partial\":true}";
            host.Island.objects.Clear();
            IslandSaveData.CurrentlySavingIsland=host.Island;IslandSaveData.isSavingGame=true;
            var cap=new HeroRecruitment.SaveCapture {Campaign=1,Land=1,Challenge=0};
            cap.Capture(actor.Persistent,"paid-id");cap.Apply();
            Check(!NativeGlobalSaveAllowed(),"partial-capture-must-not-serialize-old-rights-with-new-island");
            // A denied save cannot write the later in-memory partial pair to disk.
            var cold=Host.Create(1,diskJson,false,1,host);
            foreach(var pair in saved)GlobalSaveData._loaded.prefs.contents[pair.Key]=pair.Value;
            var reloaded=cold.CreateArcher("RestoredPaid",Side.Right);
            var row=new IslandSaveData.ObjectData{uniqueID="paid-id",Root=reloaded.Persistent};
            row.componentData2.Add(new(){name="Character",type="CharacterData"});cold.Island.objects.Add(row);
            var load=new HeroRecruitment.LoadCapture();load.Begin(cold.Island);load.Capture(row,reloaded.Persistent);load.End(true);
            Check(HeroRecruitment.IsPurchased(reloaded.Archer),"last-accepted-paid-pair-restores-after-denied-save",HeroRecruitment.DescribeForTests());
            return;
        }
        if (_scenario=="astra-partial-recovery")
        {
            BootVirginIsland(host);
            var payable=BootShop(2000f,4000);var actor=host.CreateArcher("Paid",Side.Right);
            HeroArcherRuntime.Observe(actor.Archer);
            Check(NativePayment(host,payable,true).Gate,"purchase");
            SavePaidHero(host,actor,SavedJsonA,"paid-id");
            // The native island save catches internally and leaves a changed partial snapshot.
            host.Island.Json="{\"land\":1,\"partial\":true}";
            host.Island.objects.Clear();
            IslandSaveData.CurrentlySavingIsland=host.Island;IslandSaveData.isSavingGame=true;
            var cap=new HeroRecruitment.SaveCapture {Campaign=1,Land=1,Challenge=0};
            cap.Capture(actor.Persistent,"paid-id");cap.Apply();
            Check(!NativeGlobalSaveAllowed(),"refused-while-partial");
            // The same source then completes a full capture of the changed island (the hero row is
            // back): the pairing is re-verified and the sanctioned save becomes allowed again.
            SavePaidHero(host,actor,SavedJsonB,"paid-id");
            Check(NativeGlobalSaveAllowed(),"complete-capture-of-same-source-recovers");
            var saved=new Dictionary<string,string>(GlobalSaveData._loaded.prefs.contents);
            var cold=Host.Create(1,host.Island.Json,false,1,host);
            foreach(var pair in saved)GlobalSaveData._loaded.prefs.contents[pair.Key]=pair.Value;
            var load=new HeroRecruitment.LoadCapture();load.Begin(cold.Island);load.End(true);
            var describe=HeroRecruitment.DescribeForTests();
            Check(!describe.Contains("unresolved=True")&&describe.Contains("seats=1"),
                "cold-load-restores-paid-seat-after-complete-capture",describe);
            return;
        }
        if (_scenario=="astra-two-pendings-one-deleted")
        {
            BootVirginIsland(host);
            var payable=BootShop(2000f,4000);var actor=host.CreateArcher("Paid",Side.Right);
            HeroArcherRuntime.Observe(actor.Archer);
            Check(NativePayment(host,payable,true).Gate,"purchase-in-campaign-1");
            var global=GlobalSaveData._loaded;
            // A second paid-but-uncaptured purchase in the other campaign.
            HeroNativeRights.BeforeCatalogMutation(global);
            var other=global.campaigns[0];
            var host0=SwitchTo(global,host,other,0,0,VirginJsonA);
            ReplaceWorldLayer(host0);
            BootVirginIsland(host0);
            var payable0=BootShop(2100f,4200);var actor0=host0.CreateArcher("Paid0",Side.Right);
            HeroArcherRuntime.Observe(actor0.Archer);
            Check(NativePayment(host0,payable0,true).Gate,"purchase-in-campaign-0");
            Check(!NativeGlobalSaveAllowed(),"two-pendings-block-the-global-save");
            // Deleting the other campaign ends only that object's own responsibility.
            HeroNativeRights.BeforeCatalogMutation(global);
            global.campaigns.RemoveAt(1);global.currentCampaign=0;CampaignSaveData.current=other;
            Check(!NativeGlobalSaveAllowed(),"deleting-other-campaign-keeps-survivor-pending-blocked");
            Check(!NativeCoroutineSaveAllowed(),"sync-gate-keeps-survivor-pending-blocked");
            SavePaidHeroIn(host0,0,actor0,SavedJsonB,"paid-0");
            Check(NativeGlobalSaveAllowed(),"survivor-capture-clears-only-its-own-pending");
            return;
        }
        if (_scenario=="astra-challenge-two-entries")
        {
            var global=GlobalSaveData._loaded;
            var challengeA=new CampaignSaveData();global.challenges.Add(challengeA);
            var hostA=SwitchTo(global,host,challengeA,0,1,VirginJsonA);
            ReplaceWorldLayer(hostA);
            BootVirginIsland(hostA);
            var payableA=BootShop(2200f,4400);var actorA=hostA.CreateArcher("ChA",Side.Right);
            HeroArcherRuntime.Observe(actorA.Archer);
            Check(NativePayment(hostA,payableA,true).Gate,"challenge-purchase-pending");
            Check(!NativeGlobalSaveAllowed(),"challenge-pending-blocks");
            InvokePatch("DeleteChallengePatch",global);
            global.challenges.RemoveAt(1);
            Check(NativeGlobalSaveAllowed(),"direct-delete-challenge-releases-its-pending");
            // The coroutine entry deletes through its state-0 MoveNext.
            var challengeB=new CampaignSaveData();global.challenges.Add(challengeB);
            var hostB=SwitchTo(global,host,challengeB,0,1,VirginJsonA);
            ReplaceWorldLayer(hostB);
            BootVirginIsland(hostB);
            var payableB=BootShop(2300f,4600);var actorB=hostB.CreateArcher("ChB",Side.Right);
            HeroArcherRuntime.Observe(actorB.Archer);
            Check(NativePayment(hostB,payableB,true).Gate,"challenge-b-purchase-pending");
            Check(!NativeGlobalSaveAllowed(),"challenge-b-pending-blocks");
            InvokePatch("DeleteChallengeRoutinePatch",new GlobalSaveData.__TryDeleteChallenge_d__94{__1__state=0});
            global.challenges.RemoveAt(1);
            Check(NativeGlobalSaveAllowed(),"coroutine-delete-challenge-releases-its-pending");
            Check(NativeCoroutineSaveAllowed(),"coroutine-sync-gate-recovers-too");
            return;
        }
        if (_scenario=="astra-corrupt-stays-failed")
        {
            Check(HeroNativeRights.Available,"healthy-before");
            Check(NativeGlobalSaveAllowed(),"healthy-save-before");
            GlobalSaveData._loaded.prefs.contents["KingdomEnhancedMod_HeroRights_v1"]=
                "{\"Version\":1,\"File\":\"global-v35\",\"Campaigns\":[\"zz\"]";
            Check(!HeroNativeRights.Available,"corrupt-document-fails-closed");
            Check(!HeroNativeRights.Available,"corrupt-document-stays-failed-on-reread");
            Check(!NativeGlobalSaveAllowed(),"corrupt-document-does-not-swallow-into-a-save");
            Check(!NativeCoroutineSaveAllowed(),"corrupt-document-blocks-the-sync-gate-too");
            return;
        }
        throw new Exception("unknown astra case");
    }

    // Switches the loaded current campaign/challenge and returns a Host bound to its island.
    private static Host SwitchTo(GlobalSaveData global, Host template, CampaignSaveData campaign,
        int campaignIndex, int challengeIndex, string json)
    {
        var island=new IslandSaveData{land=1,Json=json,isNew=true};
        campaign.CurrentIsland=island;Host.PlaceIsland(campaign,island,1);
        global.currentCampaign=campaignIndex;global.currentChallenge=challengeIndex;
        CampaignSaveData.current=campaign;
        return new Host{Campaign=campaign,Island=island,Player=template.Player,Kingdom=template.Kingdom,
            Layer=template.Layer,NativeShopGo=template.NativeShopGo};
    }

    private static void SavePaidHeroIn(Host host,int campaign,Actor actor,string savedJson,string nativeId)
    {
        host.Island.Json=savedJson;
        var record=new IslandSaveData.ObjectData {uniqueID=nativeId,Root=actor.Persistent};
        record.componentData2.Add(new IslandSaveData.ObjectData.ComponentData {name="Character",type="CharacterData"});
        host.Island.objects.Add(record);
        IslandSaveData.CurrentlySavingIsland=host.Island;IslandSaveData.isSavingGame=true;
        var capture=new HeroRecruitment.SaveCapture {Campaign=campaign,Land=host.Island.land,Challenge=0};
        capture.Capture(actor.Persistent,nativeId);
        capture.MarkerSeen=true;
        capture.Apply();
        IslandSaveData.isSavingGame=false;IslandSaveData.CurrentlySavingIsland=null;
    }

    // Mirrors HarmonyX invoking the catalog-mutation prefix for a real native delete entry.
    private static void InvokePatch(string nested,object target)
    {
        var type=typeof(HeroNativeRights).GetNestedType(nested,System.Reflection.BindingFlags.NonPublic);
        var method=type.GetMethod("Prefix",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        method.Invoke(null,new[]{target});
    }
}
