using KingdomEnhancedMod;
using UnityEngine;

if (args.Contains("--durability")) { DurabilityCompatibility.Run(); return; }
if (args.Contains("--r2")) { R2Regression.Run(); R2Regression.RunR3(); return; }
if (args.Contains("--load-jobs")) { R2Regression.ProbeNativeJobs(); return; }
if (args.Contains("--r3")) { R2Regression.RunR3(); return; }
if (args.Contains("--enrollment")) { EnrollmentRegression.Run(); return; }

int checks = 0;
void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
long nextPointer = 10000;
IntPtr Ptr() => new(++nextPointer);

(GlobalSaveData global, CampaignSaveData campaign, IslandSaveData island) Setup(
    string key = null, string json = "{\"land\":0,\"rev\":1}", bool failSet = false)
{
    var island = new IslandSaveData { Pointer = Ptr(), land = 0, Json = json };
    var campaign = new CampaignSaveData { Pointer = Ptr(), CurrentLand = 0, CurrentIsland = island };
    var prefs = new PrefsSaveData { Pointer = Ptr(), FailSet = failSet };
    if (key != null) prefs.contents[HeavyShieldSaveSchema.Key] = key;
    var global = new GlobalSaveData { Pointer = Ptr(), prefs = prefs };
    global.campaigns.Add(campaign);
    GlobalSaveData._loaded = global;
    var layer = new GameObject(++nextPointer, (int)nextPointer).Add<Transform>();
    Managers.Inst = new Managers
    {
        game = new Game { Pointer = Ptr(), currentLand = 0, state = Game.State.Playing },
        world = new World { Pointer = Ptr(), gameLayer = layer },
    };
    IslandSaveData.CurrentlySavingIsland = null;
    IslandSaveData.isSavingGame = false;
    HeavyShieldRuntime.AttachSucceeds = true;
    HeavyShieldRuntime.PreflightSucceeds = true;
    HeavyShieldRuntime.DeferAttach = false; HeavyShieldRuntime.AttachAttempts = 0; Time.time = 0;
    HeavyShieldRuntime.AttachCalls = 0;
    HeavyShieldRuntime.DetachCalls = 0;
    HeavyShieldShopShell.ObservedBowCalls = 0; HeavyShieldShopShell.ThrowObserve = false;
    HeavyShieldShopShell.ExactAtObservation = false; HeavyShieldShopShell.LastObservedBow = null;
    return (global, campaign, island);
}

void Load(IslandSaveData island, bool success = true)
{
    var load = HeavyShieldPersistence.BeginNativeIslandLoad(island);
    HeavyShieldPersistence.EndNativeIslandLoad(load, success);
    HeavyShieldPersistence.TickBinding();
}

void NativePrepare(PrefsSaveData prefs)
{
    var scope = HeavyShieldPersistence.BeginNativePrefsPrepare(prefs);
    bool success = false;
    try { prefs.CopyToSerializedEntries(); success = true; }
    finally { HeavyShieldPersistence.EndNativePrefsPrepare(scope, success); }
}

(GlobalSaveData global, CampaignSaveData campaign, IslandSaveData island) FreshReady()
{
    var env = Setup();
    Load(env.island);
    Check(HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
        "prior Tick preflights first native key before CanPay");
    return env;
}

void UnlockMold()
{
    Check(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.Mold, out var mold),
        "four-Gem mold reserve");
    Check(HeavyShieldIdentity.ValidatePurchase(mold), "same armed reservation validates at Completed");
    ModConfig.Enabled.Value = false;
    ModConfig.HeavyShieldEnabled.Value = false;
    Time.timeScale = 0f;
    IslandSaveData.isSavingGame = true;
    Check(HeavyShieldIdentity.ValidatePurchase(mold),
        "armed receipt continues across disable, pause and native saving");
    Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
        "disabled/paused/saving gates still bar a fresh charge");
    NetworkBigBoss.HasWorldAuth = false;
    Check(!HeavyShieldIdentity.ValidatePurchase(mold),
        "lost native world authority invalidates armed receipt");
    NetworkBigBoss.HasWorldAuth = true;
    Managers.Inst.game.currentLand = 1;
    Check(!HeavyShieldIdentity.ValidatePurchase(mold),
        "changed native land invalidates armed receipt");
    Managers.Inst.game.currentLand = 0;
    IslandSaveData.isSavingGame = false;
    Time.timeScale = 1f;
    ModConfig.Enabled.Value = true;
    ModConfig.HeavyShieldEnabled.Value = true;
    Check(HeavyShieldIdentity.CompleteGemPayment(mold) == HeavyShieldCommitResult.Applied,
        "verified Gem payment applies only live");
    Check(HeavyShieldIdentity.CompleteGemPayment(mold) == HeavyShieldCommitResult.AlreadyApplied,
        "same completed receipt is idempotent and never grants another mold");
    Check(!HeavyShieldIdentity.ValidatePurchase(mold), "settled receipt cannot validate again");
}

void Save(IslandSaveData island, bool marker, bool normalReturn,
    Persistent paidRoot = null, string nativeId = null)
{
    IslandSaveData.CurrentlySavingIsland = island;
    IslandSaveData.isSavingGame = true;
    var save = HeavyShieldPersistence.BeginNativeIslandSave(0, 0, 0);
    if (paidRoot != null) HeavyShieldPersistence.ObserveNativeId(save, paidRoot, nativeId);
    if (marker) HeavyShieldPersistence.ObserveNativeIslandCaptured(save, island);
    HeavyShieldPersistence.EndNativeIslandSave(save, normalReturn);
    IslandSaveData.isSavingGame = false;
    IslandSaveData.CurrentlySavingIsland = null;
}

(DroppableTool tool, Persistent persistent, GameObject root) BuyPaidBow()
{
    var root = new GameObject(++nextPointer, (int)nextPointer) { Tag = "Bow" };
    var tool = root.Add<DroppableTool>();
    var persistent = root.Add<Persistent>();
    Check(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.ShieldLeft, out var lease),
        "strict left coin seat reserved");
    Check(HeavyShieldIdentity.ValidatePurchase(lease), "own Paying seat validates at Completed");
    Check(HeavyShieldIdentity.BindIssuedPaidBow(lease, tool) == HeavyShieldCommitResult.Applied,
        "one paid Bow bound to receipt");
    Check(HeavyShieldIdentity.TryGetPaidBow(tool, out _), "paid Bow exact root/life recognized");
    return (tool, persistent, root);
}

(Peasant source, Archer successor) PromotionActors()
{
    var source = new GameObject(++nextPointer, (int)nextPointer).Add<Peasant>();
    var successor = new GameObject(++nextPointer, (int)nextPointer).Add<Archer>();
    return (source, successor);
}

// First-key failure precedes any payable admission or reservation.
var failed = Setup(failSet: true);
Load(failed.island);
Check(!failed.global.prefs.contents.ContainsKey(HeavyShieldSaveSchema.Key),
    "first SetString failure preserves missing native key");
Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold)
    && !HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.Mold, out _),
    "first key failure advertises no CanPay and reserves no Gem charge");

// No marker / failed native save cannot stage a paid mold. Later exact capture
// can recover its own capture fault without minting a new receipt.
var e = FreshReady();
string preflight = e.global.prefs.contents[HeavyShieldSaveSchema.Key];
UnlockMold();
Save(e.island, marker: false, normalReturn: true);
Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldLeft),
    "missing revision marker freezes paid shield purchase");
Check(e.global.prefs.contents[HeavyShieldSaveSchema.Key] == preflight,
    "failed capture keeps exact old native string");
Save(e.island, marker: true, normalReturn: true);
NativePrepare(e.global.prefs);
string staged = e.global.prefs.contents[HeavyShieldSaveSchema.Key];
Check(staged != preflight && HeavyShieldSaveCodec.TryParse(staged, out var doc, out _)
    && doc.Campaigns[0].MoldReceipt != null,
    "only exact marker+normal save stages Live mold in Prefs");

var exact = Setup(staged);
Load(exact.island);
Check(HeavyShieldIdentity.GetQuotaView().MoldUnlocked
    && HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldLeft),
    "same exact native island restores staged mold");
var mismatch = Setup(staged, json: "{\"land\":0,\"rev\":2}");
Load(mismatch.island);
Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldLeft)
    && HeavyShieldIdentity.GetQuotaView().Unknown
    && mismatch.global.prefs.contents[HeavyShieldSaveSchema.Key] == staged,
    "known empty island hash mismatch is Unknown and preserves old string");

var thrown = FreshReady();
UnlockMold();
Save(thrown.island, marker: true, normalReturn: false);
Check(HeavyShieldIdentity.GetQuotaView().Unknown
    && !HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldLeft),
    "native save exception despite marker never stages or admits new payment");

// Native campaign object identity controls reorder and deletion, not old slot.
var order = FreshReady();
UnlockMold();
Save(order.island, marker: true, normalReturn: true);
NativePrepare(order.global.prefs);
var newcomer = new CampaignSaveData { Pointer = Ptr(), CurrentIsland = order.island };
HeavyShieldPersistence.BeforeCampaignMutation(order.global);
order.global.campaigns.Insert(0, newcomer);
order.global.currentCampaign = 1;
HeavyShieldPersistence.TickBinding();
NativePrepare(order.global.prefs);
Check(HeavyShieldSaveCodec.TryParse(order.global.prefs.contents[HeavyShieldSaveSchema.Key],
        out var reordered, out _)
    && reordered.Campaigns.Count == 1 && reordered.Campaigns[0].Slot == 1,
    "same native campaign object carries entitlement through slot reorder");
HeavyShieldPersistence.BeforeCampaignMutation(order.global);
order.global.campaigns.RemoveAt(1);
order.global.currentCampaign = 0;
HeavyShieldPersistence.TickBinding();
NativePrepare(order.global.prefs);
Check(HeavyShieldSaveCodec.TryParse(order.global.prefs.contents[HeavyShieldSaveSchema.Key],
        out var deleted, out _)
    && deleted.Campaigns.Count == 0,
    "deleted campaign receipt disappears; same-slot new object inherits nothing");

// The new campaign's native island can save before PrefsPrepare gets a turn.
// EndSave must reconcile old staged GUIDs first, then stage only the new GUID.
var sameSlot = FreshReady();
UnlockMold();
Save(sameSlot.island, marker: true, normalReturn: true);
NativePrepare(sameSlot.global.prefs);
string oldNative = sameSlot.global.prefs.contents[HeavyShieldSaveSchema.Key];
Check(HeavyShieldSaveCodec.TryParse(oldNative, out var oldDocument, out _),
    "old campaign staged native key valid before deletion");
string oldGuid = oldDocument.Campaigns[0].Guid;
HeavyShieldPersistence.BeforeCampaignMutation(sameSlot.global);
sameSlot.global.campaigns.Clear();
var virginIsland = new IslandSaveData
{ Pointer = Ptr(), land = 0, Json = "{\"land\":0,\"newCampaign\":true}",
  isNew = true, playTimeDays = 0 };
var sameSlotNew = new CampaignSaveData
{ Pointer = Ptr(), CurrentLand = 0, CurrentIsland = virginIsland };
sameSlot.global.campaigns.Add(sameSlotNew);
HeavyShieldPersistence.AfterCampaignCreated(sameSlot.global, sameSlotNew);
var generation = HeavyShieldPersistence.BeginNativeGeneration(sameSlotNew);
HeavyShieldPersistence.EndNativeGeneration(generation, sameSlotNew, normalReturn: true);
HeavyShieldPersistence.TickBinding();
Check(HeavyShieldIdentity.TryGetCampaign(out var newCampaignToken)
    && newCampaignToken.Guid != oldGuid,
    "same native slot gets fresh GUID after old campaign object deletion");
UnlockMold();
Save(virginIsland, marker: false, normalReturn: true);
Check(sameSlot.global.prefs.contents[HeavyShieldSaveSchema.Key] == oldNative,
    "failed new-campaign capture preserves exact old native string");
Save(virginIsland, marker: true, normalReturn: true);
Check(sameSlot.global.prefs.contents[HeavyShieldSaveSchema.Key] == oldNative,
    "new-campaign EndSave stages in memory before PrefsPrepare");
NativePrepare(sameSlot.global.prefs);
Check(HeavyShieldSaveCodec.TryParse(sameSlot.global.prefs.contents[HeavyShieldSaveSchema.Key],
        out var newDocument, out _)
    && newDocument.Campaigns.Count == 1
    && newDocument.Campaigns[0].Guid == newCampaignToken.Guid
    && newDocument.Campaigns[0].Guid != oldGuid
    && newDocument.Campaigns[0].MoldReceipt != oldDocument.Campaigns[0].MoldReceipt,
    "EndSave reconciles old staged row before same-slot new GUID and Gem entitlement");

// Full native paid Bow save/load includes row pointer, native ID, and local
// TryCreateOrFind mask. An inner ordinary row cannot borrow its exclusion.
var paidSave = FreshReady();
UnlockMold();
var bow = BuyPaidBow();
paidSave.island.Json = "{\"land\":0,\"rows\":[\"paid\"]}";
var paidRow = new IslandSaveData.ObjectData { Pointer = Ptr(), uniqueID = "paid-native-id" };
paidSave.island.objects.Add(paidRow);
Save(paidSave.island, marker: true, normalReturn: true, bow.persistent, paidRow.uniqueID);
NativePrepare(paidSave.global.prefs);
string paidKey = paidSave.global.prefs.contents[HeavyShieldSaveSchema.Key];
Check(HeavyShieldSaveCodec.TryParse(paidKey, out var paidDoc, out _)
    && paidDoc.Campaigns[0].Islands[0].Claims.Count == 1,
    "native row stage carries exactly one paid Bow");
var paidLoad = Setup(paidKey, json: paidSave.island.Json);
var restoredRoot = new GameObject(++nextPointer, (int)nextPointer) { Tag = "Bow" };
var restoredTool = restoredRoot.Add<DroppableTool>();
var restoredPersistent = restoredRoot.Add<Persistent>();
var restoredRow = new IslandSaveData.ObjectData { Pointer = Ptr(), uniqueID = paidRow.uniqueID };
paidLoad.island.objects.Add(restoredRow);
var loadScope = HeavyShieldPersistence.BeginNativeIslandLoad(paidLoad.island);
var paidLocal = HeavyShieldPersistence.BeginNativeLoadRow(loadScope, restoredRow);
Check(HeavyShieldPersistence.ShieldLoadInProgress, "exact paid row opens only local load exclusion");
var otherRow = new IslandSaveData.ObjectData { Pointer = Ptr(), uniqueID = "ordinary" };
var ordinaryLocal = HeavyShieldPersistence.BeginNativeLoadRow(loadScope, otherRow);
Check(!HeavyShieldPersistence.ShieldLoadInProgress,
    "nested ordinary row masks paid parent exclusion");
HeavyShieldPersistence.EndNativeLoadRow(ordinaryLocal);
Check(HeavyShieldPersistence.ShieldLoadInProgress,
    "nested row finalizer restores exact parent exclusion");
HeavyShieldPersistence.ObserveNativeLoadRow(loadScope, restoredRow, restoredPersistent);
HeavyShieldPersistence.EndNativeLoadRow(paidLocal);
HeavyShieldPersistence.EndNativeIslandLoad(loadScope, success: true);
Check(!HeavyShieldPersistence.ShieldLoadInProgress
    && HeavyShieldIdentity.TryGetPaidBow(restoredTool, out _)
    && HeavyShieldIdentity.GetQuotaView().ShopOccupied,
    "exact native row restores one paid Bow and keeps rack occupied");
var abortedLoad = Setup(paidKey, json: paidSave.island.Json);
var abortedRoot = new GameObject(++nextPointer, (int)nextPointer) { Tag = "Bow" };
var abortedTool = abortedRoot.Add<DroppableTool>();
var abortedPersistent = abortedRoot.Add<Persistent>();
var abortedRow = new IslandSaveData.ObjectData { Pointer = Ptr(), uniqueID = paidRow.uniqueID };
abortedLoad.island.objects.Add(abortedRow);
var abortedScope = HeavyShieldPersistence.BeginNativeIslandLoad(abortedLoad.island);
HeavyShieldPersistence.ObserveNativeLoadRow(abortedScope, abortedRow, abortedPersistent);
HeavyShieldPersistence.EndNativeIslandLoad(abortedScope, success: false);
var ordinaryPeasant = new GameObject(++nextPointer, (int)nextPointer).Add<Peasant>();
Check(HeavyShieldIdentity.GetQuotaView().Unknown
    && !HeavyShieldIdentity.CanNativePickup(ordinaryPeasant, abortedTool),
    "aborted native load quarantines partially bound paid Bow pickup");
paidDoc.Campaigns[0].Islands[0].Claims[0].Phase = HeavyShieldSavedClaimPhase.PaidToolUnresolved;
Check(HeavyShieldSaveCodec.TrySerialize(paidDoc, out var unresolvedKey, out _),
    "unresolved paid item remains a valid reserved native record");
var unresolved = Setup(unresolvedKey, json: paidSave.island.Json);
unresolved.island.objects.Add(new IslandSaveData.ObjectData
{ Pointer = Ptr(), uniqueID = paidRow.uniqueID });
Load(unresolved.island);
Check(HeavyShieldIdentity.GetQuotaView().Unknown
    && HeavyShieldIdentity.GetQuotaView().ShopOccupied,
    "saved unresolved paid Bow keeps quota and freezes all charging");

// Native Promote runs whole overload. Consumption inside the call is legal;
// a new pool life without consumption cannot inherit the spent ticket.
var promo = FreshReady();
UnlockMold();
var promoBow = BuyPaidBow();
var actors = PromotionActors();
promoBow.tool.pickedUp = true;
HeavyShieldPromotionBridge.Before(actors.source, promoBow.tool, out var promotion);
Check(HeavyShieldPromotionBridge.ShieldPromotionInProgress, "promotion scope stays active");
promoBow.root.activeInHierarchy = false;
HeavyShieldIdentity.ObservePoolDespawn(promoBow.root, 0);
HeavyShieldPromotionBridge.After(actors.successor, promotion);
HeavyShieldPromotionBridge.Finally(promotion);
Check(!HeavyShieldPromotionBridge.ShieldPromotionInProgress
    && HeavyShieldRuntime.AttachCalls == 1
    && HeavyShieldIdentity.TryGetSoldier(actors.successor, out _),
    "consumed tool promotes exactly one Archer and attaches carrier");
Check(HeavyShieldIdentity.TryGetSoldier(actors.successor, out var soldierHandle)
    && HeavyShieldIdentity.UpdateCombatState(soldierHandle, new(1, true, true)),
    "live damaged/pending/retirement state is recorded for exact soldier");
promo.island.Json = "{\"land\":0,\"rows\":[\"soldier\"]}";
var soldierNative = new IslandSaveData.ObjectData { Pointer = Ptr(), uniqueID = "soldier-native-id" };
promo.island.objects.Add(soldierNative);
var soldierPersistent = actors.successor.gameObject.Add<Persistent>();
Save(promo.island, marker: true, normalReturn: true, soldierPersistent, soldierNative.uniqueID);
NativePrepare(promo.global.prefs);
string soldierKey = promo.global.prefs.contents[HeavyShieldSaveSchema.Key];
var soldierLoad = Setup(soldierKey, json: promo.island.Json);
var restoredSoldierRoot = new GameObject(++nextPointer, (int)nextPointer);
var restoredArcher = restoredSoldierRoot.Add<Archer>();
var restoredSoldierPersistent = restoredSoldierRoot.Add<Persistent>();
var soldierLoadRow = new IslandSaveData.ObjectData
{ Pointer = Ptr(), uniqueID = soldierNative.uniqueID };
soldierLoad.island.objects.Add(soldierLoadRow);
var soldierScope = HeavyShieldPersistence.BeginNativeIslandLoad(soldierLoad.island);
var soldierLocal = HeavyShieldPersistence.BeginNativeLoadRow(soldierScope, soldierLoadRow);
HeavyShieldPersistence.ObserveNativeLoadRow(soldierScope, soldierLoadRow, restoredSoldierPersistent);
HeavyShieldPersistence.EndNativeLoadRow(soldierLocal);
HeavyShieldPersistence.EndNativeIslandLoad(soldierScope, success: true);
HeavyShieldPersistence.TickBinding();
Check(HeavyShieldRuntime.AttachCalls == 1
    && HeavyShieldRuntime.LastRestored == new HeavyShieldSavedCombatState(1, true, true)
    && HeavyShieldIdentity.TryGetSoldier(restoredArcher, out var restoredHandle),
    "native row reload attaches exact soldier with damaged/pending/retirement state");
Check(HeavyShieldIdentity.TryGetSoldier(restoredArcher, out restoredHandle)
    && !HeavyShieldIdentity.UpdateCombatState(restoredHandle, new(3, false, false)),
    "reload cannot refill or clear pending/retirement uncertainty");
var abortedSoldier = Setup(soldierKey, json: promo.island.Json);
var abortedSoldierRoot = new GameObject(++nextPointer, (int)nextPointer);
abortedSoldierRoot.Add<Archer>();
var abortedSoldierPersistent = abortedSoldierRoot.Add<Persistent>();
var abortedSoldierRow = new IslandSaveData.ObjectData
{ Pointer = Ptr(), uniqueID = soldierNative.uniqueID };
abortedSoldier.island.objects.Add(abortedSoldierRow);
var abortedSoldierScope = HeavyShieldPersistence.BeginNativeIslandLoad(abortedSoldier.island);
HeavyShieldPersistence.ObserveNativeLoadRow(abortedSoldierScope,
    abortedSoldierRow, abortedSoldierPersistent);
HeavyShieldPersistence.EndNativeIslandLoad(abortedSoldierScope, success: false);
Check(HeavyShieldRuntime.AttachCalls == 0 && HeavyShieldRuntime.DetachCalls == 0
    && HeavyShieldIdentity.GetQuotaView().Unknown,
    "aborted native load never activates provisional identity and freezes it");

var reuse = FreshReady();
UnlockMold();
var reuseBow = BuyPaidBow();
var reuseActors = PromotionActors();
reuseBow.tool.pickedUp = true;
HeavyShieldPromotionBridge.Before(reuseActors.source, reuseBow.tool, out var reuseScope);
HeavyShieldIdentity.ObservePoolFreshLife(reuseBow.root, true);
HeavyShieldPromotionBridge.After(reuseActors.successor, reuseScope);
HeavyShieldPromotionBridge.Finally(reuseScope);
Check(HeavyShieldRuntime.AttachCalls == 0
    && !HeavyShieldIdentity.TryGetSoldier(reuseActors.successor, out _)
    && HeavyShieldIdentity.GetQuotaView().ShopOccupied,
    "fresh pool life cannot borrow consumed Bow ticket or free paid rack");

var attachFail = FreshReady();
UnlockMold();
var failedBow = BuyPaidBow();
var failedActors = PromotionActors();
failedBow.tool.pickedUp = true;
HeavyShieldRuntime.AttachSucceeds = false;
HeavyShieldPromotionBridge.Before(failedActors.source, failedBow.tool, out var failedScope);
failedBow.root.activeInHierarchy = false;
HeavyShieldIdentity.ObservePoolDespawn(failedBow.root, 0);
HeavyShieldPromotionBridge.After(failedActors.successor, failedScope);
HeavyShieldPromotionBridge.Finally(failedScope);
Check(HeavyShieldRuntime.AttachCalls == 1
    && HeavyShieldIdentity.GetQuotaView().Unknown
    && !HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldLeft),
    "Attach failure retains paid seat and freezes all new payment");

Console.WriteLine($"PASS heavy-shield production native-boundary synthetic: {checks} checks");
DurabilityCompatibility.Run();
