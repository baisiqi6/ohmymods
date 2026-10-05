using System;
using System.Collections.Generic;
using System.Linq;
using KingdomEnhancedMod;

// Recovery counterexamples for the four Astra findings. Same production link chain and stubs as
// Astra.cs; these add the required recovery directions:
//   omp-partial-recover    a complete retry for the same source restores the pairing
//   omp-corrupt-retry      a transient read recovers; a truly corrupt document stays refused
//   omp-crud-remap         paid campaigns deleted in both directions keep the survivor's rights
//   omp-pending-survivor   deleting one pending owner must not release another one's block
//   omp-challenge-delete   challenge deletion through both hook entry points
internal static partial class Program
{
    private static void RecoveryProbe()
    {
        if (_scenario == "omp-partial-recover") PartialRecover();
        else if (_scenario == "omp-corrupt-retry") CorruptRetry();
        else if (_scenario == "omp-crud-remap") CrudRemap();
        else if (_scenario == "omp-pending-survivor") PendingSurvivor();
        else if (_scenario == "omp-challenge-delete") ChallengeDelete();
        else throw new Exception("unknown omp case");
    }

    private static void InvokeRightsPrefix(string nested, string method, params object[] args)
    {
        var type = typeof(HeroNativeRights).GetNestedType(nested,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var prefix = type.GetMethod(method,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        prefix.Invoke(null, args);
    }

    /// <summary>After a failed partial capture, the same source's next complete capture must
    /// re-pair the paid row and unblock the native save; a cold reload then restores the owner.</summary>
    private static void PartialRecover()
    {
        ResetArchive();
        var host = Host.Create(1, VirginJsonA, isNew: true, days: 0);
        BootVirginIsland(host);
        var payable = BootShop(2000f, 4000);
        var actor = host.CreateArcher("Paid", Side.Right);
        HeroArcherRuntime.Observe(actor.Archer);
        Check(NativePayment(host, payable, true).Gate, "recover-purchase");
        SavePaidHero(host, actor, SavedJsonA, "paid-id");
        Check(NativeGlobalSaveAllowed(), "recover-first-complete-save");

        // A later native island save fails internally: changed partial snapshot, no normal marker.
        host.Island.Json = "{\"land\":1,\"partial\":true}";
        host.Island.objects.Clear();
        IslandSaveData.CurrentlySavingIsland = host.Island; IslandSaveData.isSavingGame = true;
        var failed = new HeroRecruitment.SaveCapture { Campaign = 1, Land = 1, Challenge = 0 };
        failed.Capture(actor.Persistent, "paid-id"); failed.Apply();
        Check(!NativeGlobalSaveAllowed(), "recover-partial-blocks-global-save");
        Check(!NativeCoroutineSaveAllowed(), "recover-partial-blocks-sync-save");

        // The retried save completes for the same island state and re-pairs the paid row.
        var record = new IslandSaveData.ObjectData { uniqueID = "paid-id", Root = actor.Persistent };
        record.componentData2.Add(new IslandSaveData.ObjectData.ComponentData { name = "Character", type = "CharacterData" });
        host.Island.objects.Add(record);
        var retry = new HeroRecruitment.SaveCapture { Campaign = 1, Land = 1, Challenge = 0 };
        retry.Capture(actor.Persistent, "paid-id"); retry.MarkerSeen = true; retry.Apply();
        IslandSaveData.isSavingGame = false; IslandSaveData.CurrentlySavingIsland = null;
        Check(NativeGlobalSaveAllowed() && NativeCoroutineSaveAllowed(), "recover-complete-capture-releases");

        var saved = new Dictionary<string, string>(GlobalSaveData._loaded.prefs.contents);
        var cold = Host.Create(1, host.Island.Json, isNew: false, days: 1, sharePlayer: host);
        foreach (var pair in saved) GlobalSaveData._loaded.prefs.contents[pair.Key] = pair.Value;
        var reloaded = cold.CreateArcher("PaidReload", Side.Right);
        var coldRecord = new IslandSaveData.ObjectData { uniqueID = "paid-id", Root = reloaded.Persistent };
        coldRecord.componentData2.Add(new IslandSaveData.ObjectData.ComponentData { name = "Character", type = "CharacterData" });
        cold.Island.objects.Add(coldRecord);
        var load = new HeroRecruitment.LoadCapture(); load.Begin(cold.Island);
        load.Capture(coldRecord, reloaded.Persistent); load.End(true);
        Check(HeroRecruitment.IsPurchased(reloaded.Archer) && HeroRecruitment.DescribeForTests().Contains(":native:bound"),
            "recover-cold-load-restores-paid-owner", HeroRecruitment.DescribeForTests());
    }

    /// <summary>A one-shot read failure must recover on a healthy retry, while a genuinely
    /// corrupt document must stay refused across retries instead of being swallowed.</summary>
    private static void CorruptRetry()
    {
        ResetArchive();
        var host = Host.Create(1, VirginJsonA, isNew: true, days: 0);
        Check(HeroNativeRights.Available && NativeGlobalSaveAllowed(), "corrupt-healthy-global");
        GlobalSaveData._loaded.prefs.contents.ThrowOnce = true;
        Check(!HeroNativeRights.Available, "corrupt-transient-read-fails-closed");
        Check(HeroNativeRights.Available, "corrupt-healthy-retry-recovers");
        Check(NativeGlobalSaveAllowed(), "corrupt-healthy-save-recovers");

        const string key = "KingdomEnhancedMod_HeroRights_v1";
        GlobalSaveData._loaded.prefs.contents[key] = "not json";
        Check(!HeroNativeRights.Available, "corrupt-document-rejected");
        Check(!HeroNativeRights.Available, "corrupt-document-stays-rejected");
        Check(!NativeGlobalSaveAllowed(), "corrupt-document-blocks-save");
        GlobalSaveData._loaded.prefs.contents.Remove(key);   // rollback to the pre-write state
        Check(HeroNativeRights.Available, "corrupt-rollback-recovers");
        Check(NativeGlobalSaveAllowed(), "corrupt-rollback-save-recovers");
    }

    /// <summary>Two paid campaigns: deleting either slot must leave the survivor's private GUID
    /// and rights resolvable, re-stageable and restorable across a cold load.</summary>
    private static void CrudRemap()
    {
        ResetArchive();
        var host = Host.Create(1, VirginJsonA, isNew: true, days: 0);
        var global = GlobalSaveData._loaded;
        var c0 = global.campaigns[0]; var c1 = global.campaigns[1];
        var i0 = new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = VirginJsonA };
        var i1 = new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = SavedJsonA };
        c0.CurrentIsland = i0; c1.CurrentIsland = i1;
        Host.PlaceIsland(c0, i0, 1); Host.PlaceIsland(c1, i1, 1);
        string k0 = HeroRecruitmentArchive.ContextKey(GlobalSaveData.filename, 0, 0, 1);
        string k1 = HeroRecruitmentArchive.ContextKey(GlobalSaveData.filename, 1, 0, 1);
        var r0 = new HeroPurchaseReceipt { Id = Guid.NewGuid(), Side = -1, NativeId = "owner0" };
        var r1 = new HeroPurchaseReceipt { Id = Guid.NewGuid(), Side = 1, NativeId = "owner1" };
        var e0 = HeroRecruitmentArchive.NewScope();
        var e1 = HeroRecruitmentArchive.NewScope();
        global.currentCampaign = 0; CampaignSaveData.current = c0;
        Check(HeroNativeRights.Stage(k0, 0, 0, i0, HeroRecruitmentFingerprint.Hash(i0.Json, e0), e0, new[] { r0 }, true),
            "crud-stage-paid-slot0");
        global.currentCampaign = 1; CampaignSaveData.current = c1;
        Check(HeroNativeRights.Stage(k1, 1, 0, i1, HeroRecruitmentFingerprint.Hash(i1.Json, e1), e1, new[] { r1 }, true),
            "crud-stage-paid-slot1");
        Check(NativeGlobalSaveAllowed(), "crud-both-paid-catalog-saves");

        // Delete the earlier paid campaign: the survivor keeps its private GUID and rights.
        InvokeRightsPrefix("DeleteCampaignPatch", "Prefix", global);
        global.campaigns.RemoveAt(0);
        global.currentCampaign = 0; CampaignSaveData.current = global.campaigns[0];
        Check(NativeGlobalSaveAllowed(), "crud-remapped-catalog-saves");
        bool resolvedOk = HeroNativeRights.Resolve(k0, 0, 0, 1, i1, i1.Json, false,
            out var resolved, out bool known, out _);
        Check(resolvedOk && known && !resolved.Unresolved && resolved.Seats.Count == 1 && resolved.Seats[0].Id == r1.Id,
            "crud-survivor-resolves-after-remap", resolved?.Kind ?? "null");
        // The survivor's own next complete capture must still stage despite the stale slot alias.
        Check(HeroNativeRights.Stage(k0, 0, 0, i1, HeroRecruitmentFingerprint.Hash(i1.Json, e1), e1, new[] { r1 }, false),
            "crud-survivor-restages-after-remap");
        Check(NativeGlobalSaveAllowed(), "crud-restaged-catalog-saves");

        var saved = new Dictionary<string, string>(global.prefs.contents);
        var cold = Host.Create(1, SavedJsonA, isNew: false, days: 1);
        GlobalSaveData._loaded.campaigns.RemoveAt(0);
        GlobalSaveData._loaded.currentCampaign = 0;
        foreach (var pair in saved) GlobalSaveData._loaded.prefs.contents[pair.Key] = pair.Value;
        resolvedOk = HeroNativeRights.Resolve(k0, 0, 0, 1, cold.Island, cold.Island.Json, false,
            out resolved, out known, out _);
        Check(resolvedOk && known && !resolved.Unresolved && resolved.Seats.Single().Id == r1.Id,
            "crud-cold-survivor-resolves", resolved?.Kind ?? "null");

        // The other direction: deleting the later paid campaign keeps the earlier one's rights.
        ResetArchive();
        var host2 = Host.Create(1, VirginJsonA, isNew: true, days: 0);
        var global2 = GlobalSaveData._loaded;
        var b0 = global2.campaigns[0]; var b1 = global2.campaigns[1];
        var j0 = new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = VirginJsonA };
        var j1 = new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = SavedJsonB };
        b0.CurrentIsland = j0; b1.CurrentIsland = j1;
        Host.PlaceIsland(b0, j0, 1); Host.PlaceIsland(b1, j1, 1);
        var q0 = new HeroPurchaseReceipt { Id = Guid.NewGuid(), Side = -1, NativeId = "other0" };
        var q1 = new HeroPurchaseReceipt { Id = Guid.NewGuid(), Side = 1, NativeId = "other1" };
        var f0 = HeroRecruitmentArchive.NewScope();
        var f1 = HeroRecruitmentArchive.NewScope();
        global2.currentCampaign = 0; CampaignSaveData.current = b0;
        Check(HeroNativeRights.Stage(k0, 0, 0, j0, HeroRecruitmentFingerprint.Hash(j0.Json, f0), f0, new[] { q0 }, true),
            "crud-second-stage-slot0");
        global2.currentCampaign = 1; CampaignSaveData.current = b1;
        Check(HeroNativeRights.Stage(k1, 1, 0, j1, HeroRecruitmentFingerprint.Hash(j1.Json, f1), f1, new[] { q1 }, true),
            "crud-second-stage-slot1");
        InvokeRightsPrefix("DeleteCampaignPatch", "Prefix", global2);
        global2.campaigns.RemoveAt(1);
        global2.currentCampaign = 0; CampaignSaveData.current = global2.campaigns[0];
        Check(NativeGlobalSaveAllowed(), "crud-later-delete-catalog-saves");
        resolvedOk = HeroNativeRights.Resolve(k0, 0, 0, 1, j0, j0.Json, false, out resolved, out known, out _);
        Check(resolvedOk && known && !resolved.Unresolved && resolved.Seats.Single().Id == q0.Id,
            "crud-earlier-survives-later-delete", resolved?.Kind ?? "null");
    }

    /// <summary>Pending owners are attributed: a transient catalog read failure is not a deletion,
    /// deleting one owner releases only that receipt, and a cold Global starts from its own wallet.</summary>
    private static void PendingSurvivor()
    {
        ResetArchive();
        var host = Host.Create(1, VirginJsonA, isNew: true, days: 0);
        var global = GlobalSaveData._loaded;
        global.campaigns[0].CurrentIsland = new IslandSaveData { land = 1, isNew = false, Json = VirginJsonA };
        Host.PlaceIsland(global.campaigns[0], global.campaigns[0].CurrentIsland, 1);
        global.currentCampaign = 1; CampaignSaveData.current = global.campaigns[1];
        Check(HeroNativeRights.Track(Guid.NewGuid()), "pending-survivor-track-slot1");
        global.currentCampaign = 0; CampaignSaveData.current = global.campaigns[0];
        Check(HeroNativeRights.Track(Guid.NewGuid()), "pending-survivor-track-slot0");
        Check(!NativeGlobalSaveAllowed(), "pending-survivor-both-block");

        // A transient catalog read failure is never a deletion: the responsibilities stay.
        Il2CppSystem.Collections.Generic.List<CampaignSaveData>.FailEnumerations = 1;
        Check(!NativeGlobalSaveAllowed(), "pending-survivor-catalog-read-failure-blocks");
        Check(!NativeGlobalSaveAllowed(), "pending-survivor-catalog-retry-keeps-pending");

        // Deleting the first owner ends only that receipt's responsibility.
        InvokeRightsPrefix("DeleteCampaignPatch", "Prefix", global);
        global.campaigns.RemoveAt(0);
        global.currentCampaign = 0; CampaignSaveData.current = global.campaigns[0];
        Check(!NativeGlobalSaveAllowed(), "pending-survivor-partial-delete-still-blocked");
        // Deleting the survivor ends the last responsibility.
        InvokeRightsPrefix("DeleteCampaignPatch", "Prefix", global);
        global.campaigns.RemoveAt(0);
        Check(NativeGlobalSaveAllowed() && NativeCoroutineSaveAllowed(), "pending-survivor-all-deleted-releases");

        // A cold load installs a different Global: it starts from its own catalog and wallet.
        ResetArchive();
        var host2 = Host.Create(1, VirginJsonA, isNew: true, days: 0);
        Check(HeroNativeRights.Track(Guid.NewGuid()), "pending-survivor-track-before-cold");
        Check(!NativeGlobalSaveAllowed(), "pending-survivor-blocks-before-cold");
        Host.Create(1, VirginJsonA, isNew: false, days: 1);
        Check(NativeGlobalSaveAllowed(), "pending-survivor-cold-load-starts-clean");
    }

    /// <summary>Pending on a challenge is released by both deletion entry points: the synchronous
    /// DeleteChallenge prefix and the state-0 coroutine prefix.</summary>
    private static void ChallengeDelete()
    {
        ResetArchive();
        var host = Host.Create(1, VirginJsonA, isNew: true, days: 0);
        var global = GlobalSaveData._loaded;

        var ch1 = new CampaignSaveData { CurrentIsland = new IslandSaveData { land = 1, isNew = false, Json = VirginJsonA } };
        Host.PlaceIsland(ch1, ch1.CurrentIsland, 1);
        global.challenges.Add(ch1);                       // challenges: [placeholder, ch1]
        global.currentChallenge = 1; CampaignSaveData.current = ch1;
        Check(HeroNativeRights.Track(Guid.NewGuid()), "challenge-pending-sync-entry");
        Check(!NativeGlobalSaveAllowed(), "challenge-pending-blocks");
        InvokeRightsPrefix("DeleteChallengePatch", "Prefix", global);          // synchronous entry
        global.challenges.RemoveAt(1);
        global.currentChallenge = 0; CampaignSaveData.current = global.campaigns[1];
        Check(NativeGlobalSaveAllowed(), "challenge-sync-delete-releases");

        var ch2 = new CampaignSaveData { CurrentIsland = new IslandSaveData { land = 1, isNew = false, Json = VirginJsonA } };
        Host.PlaceIsland(ch2, ch2.CurrentIsland, 1);
        global.challenges.Add(ch2);
        global.currentChallenge = 1; CampaignSaveData.current = ch2;
        Check(HeroNativeRights.Track(Guid.NewGuid()), "challenge-pending-routine-entry");
        Check(!NativeGlobalSaveAllowed(), "challenge-pending-blocks-again");
        var routine = new GlobalSaveData.__TryDeleteChallenge_d__94 { __1__state = 0 };
        InvokeRightsPrefix("DeleteChallengeRoutinePatch", "Prefix", routine);  // coroutine state-0 entry
        global.challenges.RemoveAt(1);
        global.currentChallenge = 0; CampaignSaveData.current = global.campaigns[1];
        Check(NativeGlobalSaveAllowed(), "challenge-routine-delete-releases");

        var ch3 = new CampaignSaveData { CurrentIsland = new IslandSaveData { land = 1, isNew = false, Json = VirginJsonA } };
        Host.PlaceIsland(ch3, ch3.CurrentIsland, 1);
        global.challenges.Add(ch3);
        global.currentChallenge = 1; CampaignSaveData.current = ch3;
        Check(HeroNativeRights.Track(Guid.NewGuid()), "challenge-pending-unstarted-routine");
        var pendingRoutine = new GlobalSaveData.__TryDeleteChallenge_d__94 { __1__state = -1 };
        InvokeRightsPrefix("DeleteChallengeRoutinePatch", "Prefix", pendingRoutine);
        Check(!NativeGlobalSaveAllowed(), "challenge-unstarted-routine-does-not-release");
        global.challenges.RemoveAt(1);
        global.currentChallenge = 0; CampaignSaveData.current = global.campaigns[1];
        Check(NativeGlobalSaveAllowed(), "challenge-deletion-confirmed-by-next-observation");
    }
}
