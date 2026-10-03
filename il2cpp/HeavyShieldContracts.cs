using System;

namespace KingdomEnhancedMod;

// Shared HeavyShield contract. Native-facing entry points live in their named owners;
// this is the only definition site for values exchanged among shop, identity and combat.
internal enum HeavyShieldPurchaseKind : byte
{
    Mold, ExtraLeft, ExtraRight, ShieldLeft, ShieldRight
}

internal enum HeavyShieldCommitResult : byte
{
    Applied, AlreadyApplied, Rejected, Unknown
}

internal enum HeavyShieldIdentityPhase : byte
{
    Unavailable, Allocated, Staged, Loaded
}

internal enum HeavyShieldExitKind : byte
{
    Dead, DemotedToPeasant
}

internal readonly record struct HeavyShieldCampaignToken(
    string Guid, IntPtr Global, IntPtr Campaign, long OwnerGeneration,
    HeavyShieldIdentityPhase Phase);

internal readonly record struct HeavyShieldPurchaseLease(
    Guid Receipt, HeavyShieldCampaignToken Campaign, HeavyShieldPurchaseKind Kind,
    HeavyShieldQuota.Side Side, int Land, long World);

internal readonly record struct HeavyShieldCareerHandle(
    Guid Receipt, string CampaignGuid, HeavyShieldQuota.Side Side,
    int Land, IntPtr Root, int GoId, long World, long Life);

internal readonly record struct HeavyShieldSavedCombatState(
    int Durability, bool PendingBreak, bool RetirementUnknown);

internal readonly record struct HeavyShieldQuotaView(
    bool Ready, bool Unknown, bool MoldUnlocked, bool LeftExtra, bool RightExtra,
    int LeftLimit, int RightLimit, int LeftOccupied, int RightOccupied,
    bool ShopOccupied);

// Additional exact entry points (their implementations remain in the named owners):
// HeavyShieldIdentity.ValidatePurchase(in HeavyShieldPurchaseLease lease) : bool
// HeavyShieldIdentity.ObserveNativeExitProof(in HeavyShieldCareerHandle handle,
//     HeavyShieldExitKind kind) : bool
// HeavyShieldIdentity.ObservePoolFreshLife(GameObject root, bool freshLife) : void
// HeavyShieldIdentity.ObservePoolDespawn(GameObject root, float delay) : void
// HeavyShieldIdentity.CanNativePickup(Peasant source, DroppableTool tool) : bool
// HeavyShieldPromotionBridge.Before(Character source, DroppableTool tool,
//     out HeavyShieldPromotionBridge.PromotionState state) : void
// HeavyShieldPromotionBridge.After(Character result,
//     HeavyShieldPromotionBridge.PromotionState state) : void
// HeavyShieldPromotionBridge.Finally(
//     HeavyShieldPromotionBridge.PromotionState state) : void
// HeavyShieldPersistence.BeginNativeLoadRow(
//     HeavyShieldPersistence.LoadCapture scope,
//     IslandSaveData.ObjectData exactRow) : HeavyShieldPersistence.CreateCapture
// HeavyShieldPersistence.EndNativeLoadRow(
//     HeavyShieldPersistence.CreateCapture scope) : void
// HeavyShieldPersistence.ShieldLoadInProgress : bool
// HeavyShieldPersistence.BeginNativePrefsPrepare(PrefsSaveData exactPrefs)
//     : HeavyShieldPersistence.PrepareCapture
// HeavyShieldPersistence.EndNativePrefsPrepare(
//     HeavyShieldPersistence.PrepareCapture scope, bool normalReturn) : void
// HeavyShieldRuntime.IsCarrierActive(in HeavyShieldCareerHandle handle) : bool
// HeavyShieldRuntime.TryAttachCarrier(Archer carrier,
//     in HeavyShieldCareerHandle handle, in HeavyShieldSavedCombatState restored)
//     : HeavyShieldRuntime.AttachResult { Deferred, Attached, Failed }
