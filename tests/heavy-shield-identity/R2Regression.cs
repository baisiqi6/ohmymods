using KingdomEnhancedMod;
using UnityEngine;

internal static class R2Regression
{
    private static long next = 1000000;
    private static int checks, failures;
    private static IntPtr Ptr() => new(++next);
    private static void Check(bool value, string name)
    { checks++; Console.WriteLine((value ? "PASS " : "FAIL ") + name); if (!value) failures++; }
    private static void Require(bool value, string name)
    { if (!value) throw new Exception("fixture: " + name); }

    private static (GlobalSaveData Global, CampaignSaveData Campaign, IslandSaveData Island) Setup(string key = null, string json = "{\"land\":0,\"rev\":1}")
    {
        var island = new IslandSaveData { Pointer = Ptr(), land = 0, Json = json };
        var campaign = new CampaignSaveData { Pointer = Ptr(), CurrentLand = 0, CurrentIsland = island };
        var prefs = new PrefsSaveData { Pointer = Ptr() };
        if (key != null) prefs.contents[HeavyShieldSaveSchema.Key] = key;
        var global = new GlobalSaveData { Pointer = Ptr(), prefs = prefs };
        global.campaigns.Add(campaign); GlobalSaveData._loaded = global;
        Managers.Inst = new Managers { game = new Game { Pointer = Ptr(), currentLand = 0 },
            world = new World { Pointer = Ptr(), gameLayer = Root().Add<Transform>() } };
        Time.timeScale = 1; ModConfig.Enabled.Value = ModConfig.HeavyShieldEnabled.Value = true;
        Time.time = 0;
        Game.SavingEnabled = true; NetworkBigBoss.HasWorldAuth = true; NetworkBigBoss.IsOnline = false;
        IslandSaveData.isSavingGame = false; IslandSaveData.CurrentlySavingIsland = null;
        HeavyShieldRuntime.AttachSucceeds = true; HeavyShieldRuntime.AttachCalls = HeavyShieldRuntime.DetachCalls = 0;
        HeavyShieldRuntime.PreflightSucceeds = true;
        HeavyShieldRuntime.DeferAttach = false; HeavyShieldRuntime.AttachAttempts = 0;
        HeavyShieldShopShell.ObservedBowCalls = 0; HeavyShieldShopShell.ThrowObserve = false;
        HeavyShieldShopShell.ExactAtObservation = false; HeavyShieldShopShell.LastObservedBow = null;
        return (global, campaign, island);
    }
    private static GameObject Root(string tag = null) => new(++next, (int)next) { Tag = tag };
    private static void EmptyLoad(IslandSaveData island)
    { var s = HeavyShieldPersistence.BeginNativeIslandLoad(island); HeavyShieldPersistence.EndNativeIslandLoad(s, true); HeavyShieldPersistence.TickBinding(); }
    private static void Unlock()
    {
        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.Mold, out var lease), "mold reserve");
        Require(HeavyShieldIdentity.CompleteGemPayment(lease) == HeavyShieldCommitResult.Applied, "mold complete");
    }
    private static (DroppableTool Tool, Persistent Persistent, GameObject Root) Bow()
    {
        var root = Root("Bow"); var tool = root.Add<DroppableTool>(); var persistent = root.Add<Persistent>();
        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.ShieldLeft, out var lease), "shield reserve");
        Require(HeavyShieldIdentity.BindIssuedPaidBow(lease, tool) == HeavyShieldCommitResult.Applied, "shield bind");
        return (tool, persistent, root);
    }
    private static Archer Promote((DroppableTool Tool, Persistent Persistent, GameObject Root) bow)
    {
        var source = Root().Add<Peasant>(); var soldier = Root().Add<Archer>(); bow.Tool.pickedUp = true;
        HeavyShieldPromotionBridge.Before(source, bow.Tool, out var scope); bow.Root.activeInHierarchy = false;
        HeavyShieldIdentity.ObservePoolDespawn(bow.Root, 0); HeavyShieldPromotionBridge.After(soldier, scope);
        HeavyShieldPromotionBridge.Finally(scope); Require(HeavyShieldIdentity.TryGetSoldier(soldier, out _), "promote"); return soldier;
    }
    private static void Stage(IslandSaveData island, Persistent root = null, string id = null, bool prepare = true)
    {
        IslandSaveData.isSavingGame = true; IslandSaveData.CurrentlySavingIsland = island;
        var s = HeavyShieldPersistence.BeginNativeIslandSave(GlobalSaveData._loaded.currentCampaign, island.land, 0);
        if (root != null) HeavyShieldPersistence.ObserveNativeId(s, root, id);
        HeavyShieldPersistence.ObserveNativeIslandCaptured(s, island); HeavyShieldPersistence.EndNativeIslandSave(s, true);
        IslandSaveData.isSavingGame = false; IslandSaveData.CurrentlySavingIsland = null;
        if (prepare) Prepare(GlobalSaveData._loaded.prefs);
    }
    private static void Prepare(PrefsSaveData prefs, bool normalReturn = true)
    {
        var scope = HeavyShieldPersistence.BeginNativePrefsPrepare(prefs);
        bool success = false;
        try { prefs.CopyToSerializedEntries(); success = normalReturn; }
        finally { HeavyShieldPersistence.EndNativePrefsPrepare(scope, success); }
    }
    private static void Switch(CampaignSaveData campaign, IslandSaveData island)
    {
        campaign.CurrentIsland = island; campaign.CurrentLand = island.land;
        Managers.Inst.game.currentLand = island.land; Managers.Inst.world.gameLayer = Root().Add<Transform>();
    }
    private static void NewIsland(CampaignSaveData campaign)
    {
        var island = new IslandSaveData { Pointer = Ptr(), land = 1, Json = "{\"land\":1,\"rev\":1}", isNew = true };
        Switch(campaign, island); var generation = HeavyShieldPersistence.BeginNativeGeneration(campaign);
        HeavyShieldPersistence.EndNativeGeneration(generation, campaign, true); HeavyShieldPersistence.TickBinding();
        Stage(island);
    }
    private static (Persistent Persistent, Archer Archer, DroppableTool Tool) Reload(IslandSaveData island, string id, bool soldier, Game.State state = Game.State.Playing, string nativeJob = null)
    {
        Managers.Inst.game.state = state;
        var root = Root(soldier ? null : "Bow"); var persistent = root.Add<Persistent>();
        var archer = soldier ? root.Add<Archer>() : null; var tool = soldier ? null : root.Add<DroppableTool>();
        if (archer != null && nativeJob == "tower") archer._guardSlot = new object();
        if (archer != null && nativeJob == "knight") archer._knight = new object();
        if (archer != null && nativeJob == "formation") archer._currentFormation = new object();
        var scope = HeavyShieldPersistence.BeginNativeIslandLoad(island); var row = island.objects.Single(x => x.uniqueID == id);
        var local = HeavyShieldPersistence.BeginNativeLoadRow(scope, row);
        HeavyShieldPersistence.ObserveNativeLoadRow(scope, row, persistent); HeavyShieldPersistence.EndNativeLoadRow(local);
        HeavyShieldPersistence.EndNativeIslandLoad(scope, true); HeavyShieldPersistence.TickBinding(); return (persistent, archer, tool);
    }
    internal static void Run()
    {
        var phase = Setup(); EmptyLoad(phase.Island);
        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.Mold, out var frozen), "phase reserve");
        Stage(phase.Island); Check(HeavyShieldIdentity.Current.Phase == HeavyShieldIdentityPhase.Staged, "actual marker+End advances phase");
        Check(HeavyShieldIdentity.ValidatePurchase(frozen), "frozen native Completed lease survives real stage phase change");
        Check(HeavyShieldIdentity.CompleteGemPayment(frozen) == HeavyShieldCommitResult.Applied, "stage continuation applies once");
        Check(HeavyShieldIdentity.CompleteGemPayment(frozen) == HeavyShieldCommitResult.AlreadyApplied, "stage continuation duplicate is idempotent");
        var loadedPhase = Setup(phase.Global.prefs.contents[HeavyShieldSaveSchema.Key]); EmptyLoad(loadedPhase.Island);
        Require(HeavyShieldIdentity.Current.Phase == HeavyShieldIdentityPhase.Loaded, "Loaded phase");
        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.Mold, out var loadedLease), "Loaded pending lease");
        // Explicit declaration avoids deriving permission from the persistence phase.
        HeavyShieldIdentity.TryGetCampaign(out var loadedToken);
        Stage(loadedPhase.Island);
        Check(HeavyShieldIdentity.ValidateCampaign(loadedToken) && HeavyShieldIdentity.ValidatePurchase(loadedLease)
            && HeavyShieldIdentity.CompleteGemPayment(loadedLease) == HeavyShieldCommitResult.Applied,
            "Loaded to Staged keeps stable owner and exact pending Completed permission");
        Check(!HeavyShieldIdentity.ValidateCampaign(loadedToken with { Guid = Guid.NewGuid().ToString("D") })
            && !HeavyShieldIdentity.ValidateCampaign(loadedToken with { Global = Ptr() })
            && !HeavyShieldIdentity.ValidateCampaign(loadedToken with { Campaign = Ptr() })
            && !HeavyShieldIdentity.ValidateCampaign(loadedToken with { OwnerGeneration = loadedToken.OwnerGeneration + 1 })
            && !HeavyShieldIdentity.ValidateCampaign(loadedToken with { Phase = HeavyShieldIdentityPhase.Unavailable }),
            "phase tolerance never accepts fake owner lineage or unavailable token");

        var pending = Setup(); EmptyLoad(pending.Island); Unlock(); var bow = Bow();
        pending.Island.Json = "{\"land\":0,\"rev\":2,\"paid\":1}";
        pending.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "pending" }); Stage(pending.Island, bow.Persistent, "pending");
        NewIsland(pending.Campaign); Check(HeavyShieldIdentity.GetQuotaView().MoldUnlocked && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 0, "new island keeps campaign mold and independent quota");
        Switch(pending.Campaign, pending.Island); var re = Reload(pending.Island, "pending", false);
        Check(HeavyShieldIdentity.TryGetPaidBow(re.Tool, out _) && HeavyShieldIdentity.GetQuotaView().ShopOccupied && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1, "same Global latest Staged paid Bow returns with occupied seat");
        Check(HeavyShieldIdentity.Current.Phase == HeavyShieldIdentityPhase.Staged && !HeavyShieldIdentity.GetQuotaView().Unknown, "same process stage provenance is Staged and verified");

        var worn = Setup(); EmptyLoad(worn.Island); Unlock(); var soldier = Promote(Bow());
        Require(HeavyShieldIdentity.TryGetSoldier(soldier, out var handle) && HeavyShieldIdentity.UpdateCombatState(handle, new(1, true, true)), "wear state");
        worn.Island.Json = "{\"land\":0,\"rev\":3,\"soldier\":1}";
        worn.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "soldier" }); Stage(worn.Island, soldier.gameObject.Add<Persistent>(), "soldier");
        string soldierKey = worn.Global.prefs.contents[HeavyShieldSaveSchema.Key];
        NewIsland(worn.Campaign); Switch(worn.Campaign, worn.Island); HeavyShieldRuntime.AttachCalls = 0;
        var restored = Reload(worn.Island, "soldier", true);
        Check(HeavyShieldIdentity.TryGetSoldier(restored.Archer, out var restoredHandle) && HeavyShieldIdentity.TryGetCombatState(restoredHandle, out var state) && state == new HeavyShieldSavedCombatState(1, true, true), "same Global soldier retains saved wear/pending/unknown");

        var startup = Setup(soldierKey, worn.Island.Json); startup.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "soldier" });
        var startupCarrier = Reload(startup.Island, "soldier", true);
        Require(HeavyShieldIdentity.TryGetSoldier(startupCarrier.Archer, out var startupHandle)
            && HeavyShieldIdentity.UpdateCombatState(startupHandle, new(0, true, true)), "startup wear advance");
        startup.Island.Json = "{\"land\":0,\"rev\":4,\"soldier\":1}";
        Stage(startup.Island, startupCarrier.Persistent, "soldier"); NewIsland(startup.Campaign); Switch(startup.Campaign, startup.Island);
        var newerCarrier = Reload(startup.Island, "soldier", true);
        Check(HeavyShieldIdentity.TryGetSoldier(newerCarrier.Archer, out var newerHandle)
            && HeavyShieldIdentity.TryGetCombatState(newerHandle, out var newerState) && newerState.Durability == 0
            && HeavyShieldIdentity.Current.Phase == HeavyShieldIdentityPhase.Staged, "startup native record is superseded by prepared newer stage");
        NewIsland(startup.Campaign); Switch(startup.Campaign, startup.Island); startup.Island.Json = worn.Island.Json;
        Reload(startup.Island, "soldier", true);
        Check(HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1
            && !HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldRight), "known newer mismatch cannot fall back to matching startup and keeps paid seat");

        var reorder = Setup(); EmptyLoad(reorder.Island); Unlock(); var reorderBow = Bow();
        reorder.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "reordered" }); Stage(reorder.Island, reorderBow.Persistent, "reordered");
        NewIsland(reorder.Campaign);
        reorder.Global.campaigns.Insert(0, new CampaignSaveData { Pointer = Ptr(), CurrentIsland = reorder.Campaign.CurrentIsland });
        reorder.Global.currentCampaign = 1; HeavyShieldPersistence.TickBinding(); Prepare(reorder.Global.prefs);
        Switch(reorder.Campaign, reorder.Island); var reorderedBow = Reload(reorder.Island, "reordered", false);
        Check(HeavyShieldIdentity.TryGetPaidBow(reorderedBow.Tool, out _) && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1,
            "same GUID native campaign restores after slot reorder");

        var unprepared = Setup(); EmptyLoad(unprepared.Island); Unlock(); var unpreparedBow = Bow();
        unprepared.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "unprepared" });
        Stage(unprepared.Island, unpreparedBow.Persistent, "unprepared", prepare: false);
        // A fresh load in this same world is intentionally not authorized by an unprepared stage.
        Switch(unprepared.Campaign, unprepared.Island); Reload(unprepared.Island, "unprepared", false);
        Check(HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1,
            "End stage without successful Prepare is not a restore source and preserves occupancy");

        var missing = Setup(); EmptyLoad(missing.Island); Unlock(); var missingBow = Bow();
        missing.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "missing-key" }); Stage(missing.Island, missingBow.Persistent, "missing-key");
        NewIsland(missing.Campaign); Switch(missing.Campaign, missing.Island); missing.Global.prefs.contents.Remove(HeavyShieldSaveSchema.Key);
        Reload(missing.Island, "missing-key", false);
        Check(HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1
            && !missing.Global.prefs.contents.ContainsKey(HeavyShieldSaveSchema.Key), "deleted prepared native key cannot borrow staged authority or become fresh");

        var denied = Setup(); EmptyLoad(denied.Island);
        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.Mold, out var deniedLease), "denied reservation");
        NetworkBigBoss.HasWorldAuth = false;
        Check(HeavyShieldIdentity.CompleteGemPayment(deniedLease) == HeavyShieldCommitResult.Unknown
            && HeavyShieldIdentity.Current.Unknown && HeavyShieldIdentity.Current.StageFault,
            "actual Completed authority loss records explicit pending fault");
        NetworkBigBoss.HasWorldAuth = true;
        Check(!HeavyShieldIdentity.ValidatePurchase(deniedLease) && !HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
            "faulted Completed cannot silently recharge after authority returns");

        var changedArmedKey = Setup(); EmptyLoad(changedArmedKey.Island);
        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.Mold, out var changedArmedLease), "armed key-change lease");
        changedArmedKey.Global.prefs.contents.Remove(HeavyShieldSaveSchema.Key);
        Check(!HeavyShieldIdentity.ValidatePurchase(changedArmedLease)
            && !HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold)
            && HeavyShieldIdentity.CompleteGemPayment(changedArmedLease) == HeavyShieldCommitResult.Unknown
            && HeavyShieldIdentity.GetQuotaView().Unknown,
            "native key deletion after Started bars new fees and faults exact Completed continuation");

        var loading = Setup(soldierKey, worn.Island.Json); loading.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "soldier" });
        var waiting = Reload(loading.Island, "soldier", true, Game.State.Loading);
        Check(HeavyShieldRuntime.AttachCalls == 0 && !HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1, "normal Loading binds claim and waits without failed Attach or lost seat");
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldRight), "load activation wait cannot charge");
        Managers.Inst.game.state = Game.State.Playing; HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldRuntime.AttachCalls == 1 && HeavyShieldIdentity.TryGetSoldier(waiting.Archer, out _) && !HeavyShieldIdentity.GetQuotaView().Unknown, "Playing readiness activates saved carrier once after load");

        HeavyShieldPersistence.TickBinding(); HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldRuntime.AttachCalls == 1 && !HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldRight)
            && HeavyShieldIdentity.StatusText.Contains("退职结果待核验"), "retirementUnknown restores wear once and never repeats activation or advertises new charge");

        var ordinaryWear = Setup(); EmptyLoad(ordinaryWear.Island); Unlock(); var normalSoldier = Promote(Bow());
        Require(HeavyShieldIdentity.TryGetSoldier(normalSoldier, out var normalHandle)
            && HeavyShieldIdentity.UpdateCombatState(normalHandle, new(2, false, false)), "normal wear");
        ordinaryWear.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "normal" });
        Stage(ordinaryWear.Island, normalSoldier.gameObject.Add<Persistent>(), "normal");
        string normalKey = ordinaryWear.Global.prefs.contents[HeavyShieldSaveSchema.Key];
        var disabled = Setup(normalKey, ordinaryWear.Island.Json); disabled.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "normal" });
        ModConfig.HeavyShieldEnabled.Value = false;
        var disabledLoaded = Reload(disabled.Island, "normal", true);
        Check(HeavyShieldRuntime.AttachCalls == 0 && !HeavyShieldIdentity.GetQuotaView().Unknown
            && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1 && HeavyShieldIdentity.StatusText.Contains("功能未开启"),
            "feature-off exact load waits while preserving paid identity and reports reason");
        ModConfig.HeavyShieldEnabled.Value = true; HeavyShieldRuntime.PreflightSucceeds = false; HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldRuntime.AttachCalls == 0 && !HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldRight)
            && HeavyShieldIdentity.StatusText.Contains("等待已购盾卫激活"), "Playing without full carrier readiness remains a diagnosed activation wait");
        HeavyShieldRuntime.PreflightSucceeds = true; HeavyShieldPersistence.TickBinding();
        Require(HeavyShieldIdentity.TryGetSoldier(disabledLoaded.Archer, out var disabledHandle), "disabled restored handle");
        Check(HeavyShieldRuntime.AttachCalls == 1 && HeavyShieldRuntime.LastRestored.Durability == 2
            && HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldRight), "enable and full readiness activates worn carrier then opens new fee");
        ModConfig.HeavyShieldEnabled.Value = false; HeavyShieldRuntime.DetachCarrier(disabledHandle);
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldRight), "off detached soldier cannot advertise new fee");
        ModConfig.HeavyShieldEnabled.Value = true; HeavyShieldPersistence.TickBinding(); HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldRuntime.AttachCalls == 2 && HeavyShieldRuntime.LastRestored.Durability == 2
            && HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldRight), "off to on reattaches same normal soldier once without repairing wear");

        // An armed transaction keeps its continuation gate when another career's
        // runtime detaches; the all-active gate only applies to new charges.
        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.ExtraRight, out var armedUpgrade), "armed upgrade before detach");
        HeavyShieldRuntime.DetachCarrier(disabledHandle);
        Check(HeavyShieldIdentity.ValidatePurchase(armedUpgrade)
            && HeavyShieldIdentity.CompleteGemPayment(armedUpgrade) == HeavyShieldCommitResult.Applied,
            "armed Completed does not borrow the new-payment all-active gate");
        HeavyShieldPersistence.TickBinding();
        Stage(disabled.Island, disabledLoaded.Persistent, "normal");
        var sameIsland = Reload(disabled.Island, "normal", true);
        Check(HeavyShieldIdentity.TryGetSoldier(sameIsland.Archer, out _)
            && !HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1,
            "same-Global same-island reload replaces old binding and retains exactly one claim");

        var failedActivation = Setup(normalKey, ordinaryWear.Island.Json); failedActivation.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "normal" });
        HeavyShieldRuntime.AttachSucceeds = false; Reload(failedActivation.Island, "normal", true);
        HeavyShieldPersistence.TickBinding(); HeavyShieldRuntime.AttachSucceeds = true; HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldRuntime.AttachCalls == 1 && HeavyShieldIdentity.GetQuotaView().Unknown
            && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1, "true-ready failed Attach preserves paid seat as Unknown and never retries");

        var changedWorld = Setup(normalKey, ordinaryWear.Island.Json); changedWorld.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "normal" });
        Reload(changedWorld.Island, "normal", true, Game.State.Loading);
        Managers.Inst.world.gameLayer = Root().Add<Transform>(); Managers.Inst.game.state = Game.State.Playing;
        HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldRuntime.AttachCalls == 0 && HeavyShieldIdentity.GetQuotaView().Unknown
            && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1, "world changes during activation wait cannot bind a stale career");

        var wrapper = Setup(); EmptyLoad(wrapper.Island); Unlock(); var wrapperBow = Bow();
        wrapper.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "wrapper" }); Stage(wrapper.Island, wrapperBow.Persistent, "wrapper", false);
        HeavyShieldPersistence.PrepareNativePrefs(wrapper.Global.prefs); Switch(wrapper.Campaign, wrapper.Island); Reload(wrapper.Island, "wrapper", false);
        Check(HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1,
            "compatibility Prepare write cannot publish latest stage without whole native completion");

        var copyFailure = Setup(); EmptyLoad(copyFailure.Island); Unlock(); var copyBow = Bow();
        copyFailure.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "copy" }); Stage(copyFailure.Island, copyBow.Persistent, "copy", false);
        var copyScope = HeavyShieldPersistence.BeginNativePrefsPrepare(copyFailure.Global.prefs);
        copyFailure.Global.prefs.FailCopy = true; bool copied = false;
        try { copyFailure.Global.prefs.CopyToSerializedEntries(); copied = true; }
        catch (InvalidOperationException) { }
        finally { HeavyShieldPersistence.EndNativePrefsPrepare(copyScope, copied); }
        Switch(copyFailure.Campaign, copyFailure.Island); Reload(copyFailure.Island, "copy", false);
        Check(!copied && HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1,
            "srzEntries copy throw never publishes stage even after prefix write/readback");

        var keyChanged = Setup(); EmptyLoad(keyChanged.Island); Unlock(); var keyBow = Bow();
        keyChanged.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "key-change" }); Stage(keyChanged.Island, keyBow.Persistent, "key-change", false);
        var changedKeyScope = HeavyShieldPersistence.BeginNativePrefsPrepare(keyChanged.Global.prefs);
        keyChanged.Global.prefs.CopyToSerializedEntries(); keyChanged.Global.prefs.contents[HeavyShieldSaveSchema.Key] = "bad-json";
        HeavyShieldPersistence.EndNativePrefsPrepare(changedKeyScope, true); Switch(keyChanged.Campaign, keyChanged.Island); Reload(keyChanged.Island, "key-change", false);
        Check(HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1
            && keyChanged.Global.prefs.contents[HeavyShieldSaveSchema.Key] == "bad-json", "native key change before Prepare end cannot publish or be overwritten as fresh");

        foreach (int corruption in new[] { 0, 1, 2 })
        {
            var badCopy = Setup(); EmptyLoad(badCopy.Island); Unlock(); var badCopyBow = Bow();
            badCopy.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "copied-proof" });
            Stage(badCopy.Island, badCopyBow.Persistent, "copied-proof", false);
            var badCopyScope = HeavyShieldPersistence.BeginNativePrefsPrepare(badCopy.Global.prefs);
            badCopy.Global.prefs.CopyToSerializedEntries();
            if (corruption == 0) badCopy.Global.prefs.srzEntries.Clear();
            else if (corruption == 1) badCopy.Global.prefs.srzEntries[0].val = "wrong-copied-value";
            else badCopy.Global.prefs.srzEntries.Add(new PrefsSaveData.SrzEntry
            { key = HeavyShieldSaveSchema.Key, val = badCopy.Global.prefs.contents[HeavyShieldSaveSchema.Key] });
            HeavyShieldPersistence.EndNativePrefsPrepare(badCopyScope, true);
            Switch(badCopy.Campaign, badCopy.Island); Reload(badCopy.Island, "copied-proof", false);
            Check(HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1,
                $"normal Prepare with {(corruption == 0 ? "missing" : corruption == 1 ? "wrong" : "duplicate")} copied key never publishes Prepared");
        }

        var emptyMismatch = Setup(); EmptyLoad(emptyMismatch.Island); Unlock(); Stage(emptyMismatch.Island);
        NewIsland(emptyMismatch.Campaign); Switch(emptyMismatch.Campaign, emptyMismatch.Island);
        emptyMismatch.Island.Json = "{\"land\":0,\"rev\":2}"; EmptyLoad(emptyMismatch.Island);
        Check(HeavyShieldIdentity.GetQuotaView().Unknown && !HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldLeft),
            "known newer empty snapshot mismatch remains Unknown");

        Console.WriteLine($"R2 production regressions: {checks} checks, {failures} failures");
        if (failures != 0) throw new Exception($"R2 regressions failed: {failures}");
    }

    internal static void RunR3()
    {
        checks = failures = 0;
        var whole = Setup(); EmptyLoad(whole.Island); Unlock(); Stage(whole.Island); NewIsland(whole.Campaign);
        var island1 = whole.Campaign.CurrentIsland;
        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.ExtraRight, out var extra)
            && HeavyShieldIdentity.CompleteGemPayment(extra) == HeavyShieldCommitResult.Applied, "cross-island extra paid");
        Stage(island1, prepare: false);
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ExtraLeft)
            && !HeavyShieldIdentity.GetQuotaView().Ready && HeavyShieldIdentity.GetQuotaView().Unknown
            && HeavyShieldIdentity.StatusText.Contains("等待保存完成"), "unprepared campaign stage closes every A quote/status advertisement surface");
        Check(HeavyShieldSaveCodec.TryParse(whole.Global.prefs.SerializedContents[HeavyShieldSaveSchema.Key], out var oldCopy, out _)
            && oldCopy.Campaigns[0].RightExtraReceipt == null, "native copied campaign has not published island1 extra receipt");
        Switch(whole.Campaign, whole.Island); EmptyLoad(whole.Island);
        Check(HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.GetQuotaView().RightExtra
            && HeavyShieldIdentity.Current.RightExtraReceipt == extra.Receipt.ToString("D")
            && !HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ExtraRight)
            && !HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldRight),
            "old island version cannot publish newer campaign entitlement or advertise a second charge");
        // Whole native Prepare is allowed from island1 even while the installed
        // A restore session was quarantined for island0.
        Switch(whole.Campaign, island1); Prepare(whole.Global.prefs);
        Switch(whole.Campaign, whole.Island); EmptyLoad(whole.Island);
        Check(!HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.GetQuotaView().RightExtra
            && HeavyShieldIdentity.GetQuotaView().RightLimit == 2
            && !HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ExtraRight),
            "normal whole Prepare from island1 publishes immutable campaign rights then restores island0 without second fee");
        for (int epoch = 0; epoch < 3; epoch++)
        {
            whole.Island.Json = $"{{\"land\":0,\"epoch\":{epoch}}}"; Stage(whole.Island);
            Switch(whole.Campaign, island1); EmptyLoad(island1);
            island1.Json = $"{{\"land\":1,\"epoch\":{epoch}}}"; Stage(island1);
            Switch(whole.Campaign, whole.Island); EmptyLoad(whole.Island);
            Check(!HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.GetQuotaView().RightLimit == 2,
                $"different per-island stage revisions converge after normal whole Prepare epoch{epoch}");
        }

        var claims = Setup(); EmptyLoad(claims.Island); Unlock(); Stage(claims.Island); NewIsland(claims.Campaign);
        var claimIsland1 = claims.Campaign.CurrentIsland; var claimBow = Bow();
        claimIsland1.objects.Add(new() { Pointer = Ptr(), uniqueID = "cross-island-claim" });
        Stage(claimIsland1, claimBow.Persistent, "cross-island-claim", false);
        Switch(claims.Campaign, claims.Island); EmptyLoad(claims.Island);
        Check(HeavyShieldIdentity.GetQuotaView().Unknown
            && !HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldLeft),
            "another island's unprepared claim changes quarantine the whole same-GUID campaign");
        Switch(claims.Campaign, claimIsland1); Prepare(claims.Global.prefs);
        var claimRestored = Reload(claimIsland1, "cross-island-claim", false);
        Check(HeavyShieldIdentity.TryGetPaidBow(claimRestored.Tool, out _)
            && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1 && HeavyShieldIdentity.GetQuotaView().ShopOccupied
            && !HeavyShieldIdentity.GetQuotaView().Unknown, "whole Prepare preserves and later restores the unprepared island's paid claim and one rack");

        var separate = Setup(); EmptyLoad(separate.Island); Unlock(); Stage(separate.Island); NewIsland(separate.Campaign);
        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.ExtraRight, out var separateExtra)
            && HeavyShieldIdentity.CompleteGemPayment(separateExtra) == HeavyShieldCommitResult.Applied, "other GUID pending extra");
        Stage(separate.Campaign.CurrentIsland, prepare: false);
        var secondIsland = new IslandSaveData { Pointer = Ptr(), land = 0, Json = "{\"land\":0,\"second\":1}", isNew = true };
        var secondCampaign = new CampaignSaveData { Pointer = Ptr(), CurrentLand = 0, CurrentIsland = secondIsland };
        separate.Global.campaigns.Add(secondCampaign); separate.Global.currentCampaign = 1;
        Switch(secondCampaign, secondIsland); HeavyShieldPersistence.AfterCampaignCreated(separate.Global, secondCampaign);
        var secondGeneration = HeavyShieldPersistence.BeginNativeGeneration(secondCampaign);
        HeavyShieldPersistence.EndNativeGeneration(secondGeneration, secondCampaign, true); HeavyShieldPersistence.TickBinding();
        Check(!HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
            "a different native campaign GUID is not locked by unprepared stage in another GUID");

        var promotion = Setup(); EmptyLoad(promotion.Island); Unlock(); var deferredBow = Bow();
        HeavyShieldRuntime.DeferAttach = true; int attemptsBefore = HeavyShieldRuntime.AttachAttempts;
        var deferredSoldier = Promote(deferredBow);
        Check(HeavyShieldIdentity.TryGetSoldier(deferredSoldier, out var deferredHandle)
            && !HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1
            && !HeavyShieldIdentity.GetQuotaView().ShopOccupied && HeavyShieldRuntime.AttachCalls == 0
            && HeavyShieldRuntime.AttachAttempts == attemptsBefore + 1,
            "typed Deferred accepts exact paid promotion with bound soldier and no carrier writes");
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldRight)
            && HeavyShieldIdentity.StatusText.Contains("等待已购盾卫激活"), "deferred promotion does not advertise new fee");
        HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldRuntime.AttachAttempts == attemptsBefore + 1, "deferred re-evaluation waits the bounded cadence");
        Time.time += 0.25f; HeavyShieldPersistence.TickBinding();
        Check(!HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldRuntime.AttachCalls == 0,
            "repeated typed Deferred stays bound without turning into failed native mutation");
        HeavyShieldRuntime.DeferAttach = false; Time.time += 0.25f; HeavyShieldPersistence.TickBinding();
        Require(HeavyShieldIdentity.TryGetSoldier(deferredSoldier, out var attachedHandle), "deferred attached handle");
        Check(HeavyShieldRuntime.AttachCalls == 1
            && attachedHandle == deferredHandle && HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldRight),
            "later Attached activates the same deferred career once and then opens new fee");

        Require(HeavyShieldIdentity.UpdateCombatState(attachedHandle, new(2, false, false)), "deferred soldier wear");
        promotion.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "deferred-load" });
        Stage(promotion.Island, deferredSoldier.gameObject.Add<Persistent>(), "deferred-load");
        string deferredKey = promotion.Global.prefs.SerializedContents[HeavyShieldSaveSchema.Key];
        var loadDeferred = Setup(deferredKey, promotion.Island.Json);
        loadDeferred.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "deferred-load" }); HeavyShieldRuntime.DeferAttach = true;
        var deferredLoaded = Reload(loadDeferred.Island, "deferred-load", true);
        Check(!HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldRuntime.AttachCalls == 0
            && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1, "saved exact carrier Deferred keeps wear identity and paid seat");
        HeavyShieldRuntime.DeferAttach = false; Time.time += 0.25f; HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldRuntime.AttachCalls == 1 && HeavyShieldRuntime.LastRestored.Durability == 2
            && HeavyShieldIdentity.TryGetSoldier(deferredLoaded.Archer, out _), "saved Deferred later Attached does not repair wear");

        var failDeferred = Setup(); EmptyLoad(failDeferred.Island); Unlock(); var failureBow = Bow();
        HeavyShieldRuntime.DeferAttach = true; var failureSoldier = Promote(failureBow);
        HeavyShieldRuntime.DeferAttach = false; HeavyShieldRuntime.AttachSucceeds = false;
        Time.time += 0.25f; HeavyShieldPersistence.TickBinding(); int failedAttempts = HeavyShieldRuntime.AttachAttempts;
        Check(HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1
            && HeavyShieldRuntime.AttachCalls == 1, "Deferred followed by actual Failed retains exact paid occupancy as Unknown");
        HeavyShieldRuntime.AttachSucceeds = true; Time.time += 1f; HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldRuntime.AttachAttempts == failedAttempts
            && !HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldRight), "typed Failed never automatically retries a native attempt");

        var terminal = Setup(); EmptyLoad(terminal.Island); Unlock(); var terminalSoldier = Promote(Bow());
        Require(HeavyShieldIdentity.TryGetSoldier(terminalSoldier, out var terminalHandle)
            && HeavyShieldIdentity.UpdateCombatState(terminalHandle, new(1, true, true)), "terminal saved wear");
        terminal.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "terminal-deferred" });
        Stage(terminal.Island, terminalSoldier.gameObject.Add<Persistent>(), "terminal-deferred");
        var terminalLoad = Setup(terminal.Global.prefs.SerializedContents[HeavyShieldSaveSchema.Key], terminal.Island.Json);
        terminalLoad.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "terminal-deferred" }); HeavyShieldRuntime.DeferAttach = true;
        Reload(terminalLoad.Island, "terminal-deferred", true);
        Check(!HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldRuntime.AttachCalls == 0,
            "saved retirementUnknown presentation may wait on zero-write Deferred");
        HeavyShieldRuntime.DeferAttach = false; Time.time += 0.25f; HeavyShieldPersistence.TickBinding();
        int terminalAttempts = HeavyShieldRuntime.AttachAttempts;
        Check(HeavyShieldRuntime.AttachCalls == 1
            && HeavyShieldRuntime.LastRestored == new HeavyShieldSavedCombatState(1, true, true),
            "terminal Deferred resolves to only the first exact-load worn presentation");
        Time.time += 1f; HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldRuntime.AttachAttempts == terminalAttempts
            && !HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldRight)
            && HeavyShieldIdentity.StatusText.Contains("退职结果待核验"),
            "terminal first presentation cannot become repeated auto-activation or active fee permission");

        var newLife = Setup(deferredKey, promotion.Island.Json);
        newLife.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "deferred-load" }); HeavyShieldRuntime.DeferAttach = true;
        var newLifeActor = Reload(newLife.Island, "deferred-load", true);
        int beforeNewLife = HeavyShieldRuntime.AttachAttempts;
        HeavyShieldIdentity.ObservePoolFreshLife(newLifeActor.Archer.gameObject, true);
        HeavyShieldRuntime.DeferAttach = false; Time.time += 0.25f; HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1
            && HeavyShieldRuntime.AttachAttempts == beforeNewLife && HeavyShieldRuntime.AttachCalls == 0,
            "a fresh native life during Deferred cannot borrow old paid activation permission");

        var observation = Setup(); EmptyLoad(observation.Island); Unlock(); var observedBow = Bow();
        observation.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "observe-paid-load" });
        Stage(observation.Island, observedBow.Persistent, "observe-paid-load");
        string observationKey = observation.Global.prefs.SerializedContents[HeavyShieldSaveSchema.Key];
        var observeLoad = Setup(observationKey, observation.Island.Json);
        var exactPaidRow = new IslandSaveData.ObjectData { Pointer = Ptr(), uniqueID = "observe-paid-load" };
        var ordinaryRow = new IslandSaveData.ObjectData { Pointer = Ptr(), uniqueID = "ordinary-bow" };
        observeLoad.Island.objects.Add(exactPaidRow); observeLoad.Island.objects.Add(ordinaryRow);
        Managers.Inst.game.state = Game.State.Loading;
        var observeScope = HeavyShieldPersistence.BeginNativeIslandLoad(observeLoad.Island);
        var ordinaryRoot = Root("Bow"); ordinaryRoot.Add<DroppableTool>();
        HeavyShieldPersistence.ObserveNativeLoadRow(observeScope, ordinaryRow, ordinaryRoot.Add<Persistent>());
        Check(HeavyShieldShopShell.ObservedBowCalls == 0, "ordinary native Bow row is not a paid visual observer caller");
        var exactRoot = Root("Bow"); var exactBow = exactRoot.Add<DroppableTool>(); var exactPersistent = exactRoot.Add<Persistent>();
        HeavyShieldPersistence.ObserveNativeLoadRow(observeScope, exactPaidRow, exactPersistent);
        HeavyShieldPersistence.EndNativeIslandLoad(observeScope, true);
        Check(HeavyShieldShopShell.ObservedBowCalls == 1 && HeavyShieldShopShell.LastObservedBow == exactBow
            && HeavyShieldShopShell.ExactAtObservation && HeavyShieldIdentity.TryGetPaidBow(exactBow, out _)
            && !HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldIdentity.GetQuotaView().ShopOccupied,
            "exact paid native load observes its already bound root and life once while Loading");

        var observeThrow = Setup(observationKey, observation.Island.Json);
        observeThrow.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "observe-paid-load" });
        HeavyShieldShopShell.ThrowObserve = true;
        var throwingBow = Reload(observeThrow.Island, "observe-paid-load", false, Game.State.Loading);
        Check(HeavyShieldShopShell.ObservedBowCalls == 1 && HeavyShieldShopShell.ExactAtObservation
            && HeavyShieldIdentity.TryGetPaidBow(throwingBow.Tool, out _)
            && !HeavyShieldIdentity.GetQuotaView().Unknown && !HeavyShieldIdentity.Current.StageFault
            && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1 && HeavyShieldIdentity.GetQuotaView().ShopOccupied,
            "paid Bow observer throw is isolated from exact identity and reserved quota success");

        Require(HeavyShieldSaveCodec.TryParse(observationKey, out var unresolvedObservedDoc, out _), "unresolved observation fixture parse");
        unresolvedObservedDoc.Campaigns[0].Islands[0].Claims[0].Phase = HeavyShieldSavedClaimPhase.PaidToolUnresolved;
        Require(HeavyShieldSaveCodec.TrySerialize(unresolvedObservedDoc, out var unresolvedObservedKey, out _), "unresolved observation fixture serialize");
        var unresolvedObserve = Setup(unresolvedObservedKey, observation.Island.Json);
        unresolvedObserve.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "observe-paid-load" });
        Reload(unresolvedObserve.Island, "observe-paid-load", false, Game.State.Loading);
        Check(HeavyShieldShopShell.ObservedBowCalls == 0 && HeavyShieldIdentity.GetQuotaView().Unknown
            && HeavyShieldIdentity.GetQuotaView().ShopOccupied, "unresolved paid Bow retains quota without advertising a successful visual observation");

        Console.WriteLine($"R3 prepared-campaign and typed-activation production regressions: {checks} checks, {failures} failures");
        if (failures != 0) throw new Exception($"R3 regressions failed: {failures}");
    }

    // Diagnostic boundary probe, not an assertion that native Loading assigns
    // any of these jobs. Actual OnEnable distribution has its own Playing gate.
    internal static void ProbeNativeJobs()
    {
        var fixture = Setup(); EmptyLoad(fixture.Island); Unlock(); var soldier = Promote(Bow());
        fixture.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "job-probe" });
        Stage(fixture.Island, soldier.gameObject.Add<Persistent>(), "job-probe");
        string key = fixture.Global.prefs.contents[HeavyShieldSaveSchema.Key];
        foreach (string job in new[] { "tower", "knight", "formation" })
        {
            var load = Setup(key, fixture.Island.Json); load.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "job-probe" });
            var actor = Reload(load.Island, "job-probe", true, Game.State.Loading, job);
            Check(HeavyShieldIdentity.TryGetSoldier(actor.Archer, out _)
                && !HeavyShieldIdentity.GetQuotaView().Unknown && HeavyShieldRuntime.AttachCalls == 0,
                $"preassigned {job}: exact paid identity binds while activation waits");
            bool remains = job == "tower" ? actor.Archer._guardSlot != null
                : job == "knight" ? actor.Archer._knight != null : actor.Archer._currentFormation != null;
            Check(remains && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1,
                $"preassigned {job}: A does not mutate native job while holding paid occupancy");
        }
        Console.WriteLine($"Native job boundary diagnostic: {checks} checks, {failures} failures; normal native assignment not inferred");
        if (failures != 0) throw new Exception("native job diagnostic failed");
    }
}
