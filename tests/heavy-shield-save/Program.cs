using KingdomEnhancedMod;

int checks = 0;
void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }

string campaignGuid = HeavyShieldSaveCodec.NewGuid();
string oldHash = HeavyShieldSaveCodec.SnapshotHash(campaignGuid, 0, 1, "{\"objects\":[]}")!;
string newHash = HeavyShieldSaveCodec.SnapshotHash(campaignGuid, 0, 1, "{\"objects\":[1]}")!;
var document = new HeavyShieldSaveDocument();
var campaign = new HeavyShieldSavedCampaign
{
    Slot = 0, Guid = campaignGuid,
    MoldReceipt = HeavyShieldSaveCodec.NewGuid(),
    LeftExtraReceipt = HeavyShieldSaveCodec.NewGuid(),
};
var island = new HeavyShieldSavedIsland { Challenge = 0, Land = 1, SnapshotHash = oldHash };
campaign.Islands.Add(island);
document.Campaigns.Add(campaign);
Check(HeavyShieldSaveCodec.TrySerialize(document, out var json, out _), "empty island serializes");
Check(HeavyShieldSaveCodec.TryParse(json, out var parsed, out _), "empty island parses");
Check(HeavyShieldSaveCodec.Resolve(HeavyShieldNativeKeyRead.Present, parsed, 0, 0, 1,
    oldHash, out _, out _) == HeavyShieldSnapshotResolution.Exact, "known empty exact");
Check(HeavyShieldSaveCodec.Resolve(HeavyShieldNativeKeyRead.Present, parsed, 0, 0, 1,
    newHash, out _, out _) == HeavyShieldSnapshotResolution.Unknown, "known empty mismatch unknown");
Check(HeavyShieldSaveCodec.Resolve(HeavyShieldNativeKeyRead.Failed, null, 0, 0, 1,
    oldHash, out _, out _) == HeavyShieldSnapshotResolution.Unknown, "read failure cannot mint fresh");
Check(HeavyShieldSaveCodec.Resolve(HeavyShieldNativeKeyRead.ConfirmedMissing, null, 0, 0, 1,
    oldHash, out _, out _) == HeavyShieldSnapshotResolution.ConfirmedFresh, "confirmed missing fresh");
Check(HeavyShieldSaveCodec.Resolve(HeavyShieldNativeKeyRead.Present, parsed, 1, 0, 1,
    oldHash, out _, out _) == HeavyShieldSnapshotResolution.Unknown, "new slot cannot inherit old");
Check(HeavyShieldSaveCodec.Resolve(HeavyShieldNativeKeyRead.Present, parsed, 0, 0, 2,
    oldHash, out _, out _) == HeavyShieldSnapshotResolution.Unknown, "different island cannot inherit");
Check(!HeavyShieldSaveCodec.TryParse("{", out _, out _), "bad JSON remains unknown");
Check(!HeavyShieldSaveCodec.TryParse("", out _, out _), "empty present key remains unknown");
Check(!HeavyShieldKeyGate.CanAdvertise(true, false),
    "first native key write failure keeps CanPay false before first coin");
Check(!HeavyShieldKeyGate.CanAdvertise(false, true),
    "key alone cannot authorize an unproven native island");
Check(HeavyShieldKeyGate.CanAdvertise(true, true),
    "only proven island and prior key preflight advertise payment");
Check(HeavyShieldSaveCaptureGate.CanStage(true, true, true, false, true, true),
    "complete exact native save stages");
Check(!HeavyShieldSaveCaptureGate.CanStage(false, true, true, false, true, true),
    "native save exception never stages despite marker");
Check(!HeavyShieldSaveCaptureGate.CanStage(true, true, false, false, true, true),
    "missing UpdateSavedWithRevisions marker never stages");
Check(!HeavyShieldSaveCaptureGate.CanStage(true, true, true, false, false, true),
    "changed scene never stages");
Check(!HeavyShieldSaveCaptureGate.CanStage(true, true, true, false, true, false),
    "wrong exact island never stages");
var mask = new HeavyShieldLoadRowMask();
var paidRow = mask.Begin();
Check(mask.MarkOwned(paidRow) && mask.Active, "exact paid row opens local exclusion");
var ordinaryRow = mask.Begin();
Check(!mask.Active, "nested ordinary row masks outer paid row");
mask.End(ordinaryRow);
Check(mask.Active, "nested ordinary row exit restores outer proof");
var failedRow = mask.Begin();
Check(!mask.Active && !mask.MarkOwned(paidRow), "inner failing row cannot reuse outer proof");
mask.End(failedRow);
mask.End(paidRow);
Check(!mask.Active, "finalizer closes paid row proof");

var leftReceipt = HeavyShieldSaveCodec.NewGuid();
island.Claims.Add(new HeavyShieldSavedClaim
{
    Receipt = leftReceipt, Side = HeavyShieldQuota.Side.Left,
    Phase = HeavyShieldSavedClaimPhase.Soldier, NativeId = "exact-native-id",
    Durability = 1, PendingBreak = true, RetirementUnknown = true,
});
Check(HeavyShieldSaveCodec.TrySerialize(document, out json, out _)
    && HeavyShieldSaveCodec.TryParse(json, out parsed, out _), "combat state roundtrips");
var restored = parsed!.Campaigns[0].Islands[0].Claims[0];
Check(restored.Durability == 1 && restored.PendingBreak && restored.RetirementUnknown,
    "save never repairs damaged/pending/unknown shield");
island.Claims.Add(new HeavyShieldSavedClaim
{
    Receipt = leftReceipt, Side = HeavyShieldQuota.Side.Right,
    Phase = HeavyShieldSavedClaimPhase.PaidTool, NativeId = "other-id",
});
Check(!HeavyShieldSaveCodec.TrySerialize(document, out _, out _), "receipt cannot buy two careers");
island.Claims.RemoveAt(1);
island.Claims[0].Durability = 5;
Check(!HeavyShieldSaveCodec.TrySerialize(document, out _, out _), "invalid durability rejected");
island.Claims[0].Durability = 1;
campaign.RightExtraReceipt = campaign.LeftExtraReceipt;
Check(!HeavyShieldSaveCodec.TrySerialize(document, out _, out _), "gem receipt cannot buy both sides");
campaign.RightExtraReceipt = null;

var otherGuid = HeavyShieldSaveCodec.NewGuid();
var other = new HeavyShieldSavedCampaign { Slot = 1, Guid = otherGuid };
document.Campaigns.Add(other);
Check(HeavyShieldSaveCodec.TryReconcileStaged(document,
        new Dictionary<string, int> { [campaignGuid] = 1, [otherGuid] = 0 }, out var reordered)
    && reordered.Campaigns[0].Slot == 1 && reordered.Campaigns[1].Slot == 0
    && reordered.Campaigns[0].MoldReceipt == campaign.MoldReceipt,
    "native object survival reorders slots without transferring entitlements");
Check(HeavyShieldSaveCodec.TryReconcileStaged(document,
        new Dictionary<string, int> { [otherGuid] = 0 }, out var afterDelete)
    && afterDelete.Campaigns.Count == 1 && afterDelete.Campaigns[0].Guid == otherGuid
    && afterDelete.Campaigns[0].MoldReceipt == null,
    "deleted campaign vanishes and new occupant of slot cannot inherit gem rights");

Console.WriteLine($"PASS heavy-shield native payload: {checks} checks");
DurabilityCompatibility.Run();
