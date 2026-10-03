using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

// Runtime authority is always scoped to one exact Global/Campaign/native island.
// HeavyShieldPersistence owns installation and the native snapshot proof.
internal static class HeavyShieldIdentity
{
    internal sealed class Claim
    {
        internal Guid Receipt;
        internal HeavyShieldQuota.Side Side;
        internal HeavyShieldSavedClaimPhase Phase;
        internal string NativeId = "";
        internal HeavyShieldCareerHandle Handle;
        internal GameObject RootObject;
        internal HeavyShieldSavedCombatState Combat = new(HeavyShieldSaveSchema.MaxDurability, false, false);
        internal bool NativeDeathProven;
        internal bool NativeDemotionProven;
        internal bool InitialActivationPending;
        internal float NextActivationAt;
    }

    internal sealed class Session
    {
        internal IntPtr Global;
        internal IntPtr Campaign;
        internal long OwnerGeneration;
        internal string Guid;
        internal int Slot;
        internal int Land = -1;
        internal int Challenge;
        internal long World;
        internal HeavyShieldIdentityPhase Phase;
        internal bool Unknown = true;
        internal bool KeyReady;
        internal bool StageFault;
        internal bool CaptureFaultRecoverable;
        internal string MoldReceipt;
        internal string LeftExtraReceipt;
        internal string RightExtraReceipt;
        internal readonly HeavyShieldQuota Quota = new();
        internal readonly Dictionary<Guid, Claim> Claims = new();
        internal readonly Dictionary<Guid, HeavyShieldPurchaseLease> Pending = new();
        internal readonly HashSet<Guid> Completed = new();
    }

    private static Session _current;
    private static long _nextLife;
    private static readonly Dictionary<IntPtr, long> RootLives = new();
    private static readonly Dictionary<IntPtr, Guid> RootClaims = new();
    internal static Session Current => _current;

    internal static void Install(Session session)
    {
        if (ReferenceEquals(_current, session)) return;
        _current = session;
        RootLives.Clear();
        RootClaims.Clear();
    }

    internal static bool TryGetCampaign(out HeavyShieldCampaignToken token)
    {
        token = default;
        Session s = _current;
        if (!SessionValid(s)) return false;
        token = Token(s);
        return true;
    }

    internal static bool ValidateCampaign(in HeavyShieldCampaignToken token)
    {
        Session s = _current;
        return SessionValid(s) && (token.Phase is HeavyShieldIdentityPhase.Allocated
                or HeavyShieldIdentityPhase.Staged or HeavyShieldIdentityPhase.Loaded)
            && s.Guid == token.Guid && s.Global == token.Global
            && s.Campaign == token.Campaign && s.OwnerGeneration == token.OwnerGeneration;
    }

    internal static bool CanReservePurchase(HeavyShieldPurchaseKind kind)
    {
        Session s = _current;
        if (!HeavyShieldKeyGate.CanAdvertise(ReadyForMoney(s), s?.KeyReady ?? false)
            || !HeavyShieldPersistence.IsNativeKeyCurrent(s)
            || HeavyShieldPersistence.CampaignRestorePending(s)) return false;
        return kind switch
        {
            HeavyShieldPurchaseKind.Mold => s.Quota.CanBuyMold,
            HeavyShieldPurchaseKind.ExtraLeft => s.Quota.CanBuyUpgrade(HeavyShieldQuota.Side.Left),
            HeavyShieldPurchaseKind.ExtraRight => s.Quota.CanBuyUpgrade(HeavyShieldQuota.Side.Right),
            HeavyShieldPurchaseKind.ShieldLeft => s.Quota.CanReservePayment(HeavyShieldQuota.Side.Left),
            HeavyShieldPurchaseKind.ShieldRight => s.Quota.CanReservePayment(HeavyShieldQuota.Side.Right),
            _ => false,
        };
    }

    internal static HeavyShieldQuotaView GetQuotaView()
    {
        Session s = _current;
        if (s == null) return new(false, true, false, false, false, 0, 0, 0, 0, false);
        bool keyFault = s.KeyReady && !HeavyShieldPersistence.IsNativeKeyCurrent(s);
        bool preparedPending = HeavyShieldPersistence.CampaignRestorePending(s);
        return new(HeavyShieldKeyGate.CanAdvertise(ReadyForMoney(s), s.KeyReady) && !keyFault && !preparedPending,
            s.Unknown || s.Quota.Unknown || keyFault || preparedPending,
            s.Quota.MoldUnlocked, s.Quota.ExtraSeatPurchased(HeavyShieldQuota.Side.Left),
            s.Quota.ExtraSeatPurchased(HeavyShieldQuota.Side.Right),
            s.Quota.SeatLimit(HeavyShieldQuota.Side.Left), s.Quota.SeatLimit(HeavyShieldQuota.Side.Right),
            s.Quota.OccupiedLeft, s.Quota.OccupiedRight, s.Quota.ShopOccupied);
    }

    internal static string StatusText
    {
        get
        {
            var view = GetQuotaView();
            if (HeavyShieldPersistence.CampaignRestorePending(_current))
                return "宝石盾卫：存档权益等待保存完成，暂停购买";
            if (view.Unknown) return "宝石盾卫：存档身份待核验，暂停购买";
            if (ModConfig.Enabled == null || !ModConfig.Enabled.Value
                || ModConfig.HeavyShieldEnabled == null || !ModConfig.HeavyShieldEnabled.Value)
                return "宝石盾卫：功能未开启";
            if (_current != null && !_current.Quota.ReceiptCapacityAvailable)
                return "宝石盾卫：付款收据已达容量，暂停购买";
            if (_current != null && !SoldiersActive(_current))
            {
                foreach (var claim in _current.Claims.Values)
                    if (claim.Combat.RetirementUnknown) return "宝石盾卫：退职结果待核验，暂停购买";
                foreach (var claim in _current.Claims.Values)
                    if (claim.Combat.PendingBreak) return "宝石盾卫：等待破盾处理，暂停购买";
                return "宝石盾卫：等待已购盾卫激活，暂停购买";
            }
            if (!view.Ready) return "宝石盾卫：当前不可购买";
            if (!view.MoldUnlocked) return "宝石盾卫：盾模未解锁（4宝石）";
            return $"宝石盾卫：左 {view.LeftOccupied}/{view.LeftLimit}，右 {view.RightOccupied}/{view.RightLimit}";
        }
    }

    internal static bool TryReservePurchase(HeavyShieldPurchaseKind kind, out HeavyShieldPurchaseLease lease)
    {
        lease = default;
        if (!CanReservePurchase(kind)) return false;
        Session s = _current;
        if (!HeavyShieldPersistence.PreflightKeyWrite(s) || !s.KeyReady) return false;
        Guid receipt = Guid.NewGuid();
        HeavyShieldQuota.Side side = kind == HeavyShieldPurchaseKind.ExtraRight
            || kind == HeavyShieldPurchaseKind.ShieldRight
            ? HeavyShieldQuota.Side.Right : HeavyShieldQuota.Side.Left;
        bool reserved = kind switch
        {
            HeavyShieldPurchaseKind.Mold => s.Quota.TryBeginMoldPayment(receipt),
            HeavyShieldPurchaseKind.ExtraLeft or HeavyShieldPurchaseKind.ExtraRight
                => s.Quota.TryBeginUpgradePayment(receipt, side),
            HeavyShieldPurchaseKind.ShieldLeft or HeavyShieldPurchaseKind.ShieldRight
                => s.Quota.TryReservePayment(receipt, side, out _),
            _ => false,
        };
        if (!reserved) return false;
        lease = new(receipt, Token(s), kind, side, s.Land, s.World);
        s.Pending.Add(receipt, lease);
        return true;
    }

    // A Completed transaction asks CanPay again after its own reservation has
    // occupied the shop. This validates only that same armed reservation.
    internal static bool ValidatePurchase(in HeavyShieldPurchaseLease lease)
    {
        Session s = _current;
        if (!ValidateLease(s, lease) || s.Unknown || s.StageFault || !s.KeyReady
            || !HeavyShieldPersistence.IsNativeKeyCurrent(s) || !ActiveArmedContext(s)) return false;
        return lease.Kind switch
        {
            HeavyShieldPurchaseKind.Mold => s.Quota.MoldPending,
            HeavyShieldPurchaseKind.ExtraLeft or HeavyShieldPurchaseKind.ExtraRight
                => s.Quota.UpgradePending,
            HeavyShieldPurchaseKind.ShieldLeft or HeavyShieldPurchaseKind.ShieldRight
                => s.Quota.ShopOccupied,
            _ => false,
        };
    }

    internal static HeavyShieldCommitResult CompleteGemPayment(in HeavyShieldPurchaseLease lease)
    {
        Session s = _current;
        if (s != null && MatchesLeaseContext(s, lease) && ActiveArmedContext(s)
            && s.Completed.Contains(lease.Receipt)) return HeavyShieldCommitResult.AlreadyApplied;
        if (!ValidateLease(s, lease)) return RejectPending(s, lease);
        if (s.Unknown || s.StageFault) return HeavyShieldCommitResult.Unknown;
        if (!ValidatePurchase(lease)) return RejectPending(s, lease);
        string receipt = lease.Receipt.ToString("D");
        bool ok;
        switch (lease.Kind)
        {
            case HeavyShieldPurchaseKind.Mold:
                ok = s.Quota.ConfirmMoldUnlocked(lease.Receipt);
                if (ok) s.MoldReceipt = receipt;
                break;
            case HeavyShieldPurchaseKind.ExtraLeft:
            case HeavyShieldPurchaseKind.ExtraRight:
                ok = s.Quota.ConfirmUpgradePurchased(lease.Receipt);
                if (ok)
                {
                    if (lease.Kind == HeavyShieldPurchaseKind.ExtraLeft) s.LeftExtraReceipt = receipt;
                    else s.RightExtraReceipt = receipt;
                }
                break;
            default: return HeavyShieldCommitResult.Rejected;
        }
        if (!ok) return HeavyShieldCommitResult.Unknown;
        s.Pending.Remove(lease.Receipt);
        s.Completed.Add(lease.Receipt);
        return HeavyShieldCommitResult.Applied; // Live only; EndNativeIslandSave alone stages.
    }

    internal static HeavyShieldCommitResult BindIssuedPaidBow(in HeavyShieldPurchaseLease lease, DroppableTool bow)
    {
        Session s = _current;
        if (s != null && MatchesLeaseContext(s, lease) && ActiveArmedContext(s)
            && s.Completed.Contains(lease.Receipt)) return HeavyShieldCommitResult.AlreadyApplied;
        if (!ValidateLease(s, lease) || lease.Kind is not (HeavyShieldPurchaseKind.ShieldLeft
            or HeavyShieldPurchaseKind.ShieldRight)) return RejectPending(s, lease);
        if (s.Unknown || s.StageFault || bow == null || bow.gameObject == null)
            return HeavyShieldCommitResult.Unknown;
        if (!ValidatePurchase(lease)) return RejectPending(s, lease);
        GameObject root = bow.gameObject;
        if (!HeavyShieldPromotionBridge.RegisterIssuedTool(bow, lease.Receipt)) return HeavyShieldCommitResult.Unknown;
        if (!s.Quota.ConfirmPaidTool(lease.Receipt))
        {
            HeavyShieldPromotionBridge.RevokeIssuedTool(bow);
            return HeavyShieldCommitResult.Unknown;
        }
        var claim = new Claim { Receipt = lease.Receipt, Side = lease.Side,
            Phase = HeavyShieldSavedClaimPhase.PaidTool };
        claim.Handle = CreateHandle(s, claim, root);
        s.Claims.Add(claim.Receipt, claim);
        RootClaims[root.Pointer] = claim.Receipt;
        s.Pending.Remove(lease.Receipt);
        s.Completed.Add(lease.Receipt);
        return HeavyShieldCommitResult.Applied;
    }

    internal static bool ConfirmUnpaidCancellation(in HeavyShieldPurchaseLease lease)
    {
        Session s = _current;
        if (!ValidateLease(s, lease)) return false;
        bool ok = lease.Kind switch
        {
            HeavyShieldPurchaseKind.Mold => s.Quota.ConfirmMoldCancelledAfterRefund(lease.Receipt),
            HeavyShieldPurchaseKind.ExtraLeft or HeavyShieldPurchaseKind.ExtraRight
                => s.Quota.ConfirmUpgradeCancelledAfterRefund(lease.Receipt),
            _ => s.Quota.ConfirmUnpaidCancellation(lease.Receipt),
        };
        if (ok) s.Pending.Remove(lease.Receipt);
        return ok;
    }

    internal static bool ConfirmRefund(in HeavyShieldPurchaseLease lease)
    {
        Session s = _current;
        if (!ValidateCampaign(lease.Campaign) || s == null || lease.Land != s.Land
            || lease.World != s.World) return false;
        if (s.Pending.ContainsKey(lease.Receipt)) return ConfirmUnpaidCancellation(lease);
        if (!s.Claims.TryGetValue(lease.Receipt, out var claim)
            || claim.Phase is not (HeavyShieldSavedClaimPhase.PaidTool
                or HeavyShieldSavedClaimPhase.PaidToolUnresolved)
            || !s.Quota.ConfirmRefundedTool(lease.Receipt)) return false;
        s.Claims.Remove(lease.Receipt);
        RootClaims.Remove(claim.Handle.Root);
        return true;
    }

    internal static void HoldUnknown(in HeavyShieldPurchaseLease lease, string reason)
    {
        Session s = _current;
        if (!ValidateCampaign(lease.Campaign) || s == null) return;
        s.Unknown = true;
        s.StageFault = true;
        s.CaptureFaultRecoverable = false;
        if (lease.Kind == HeavyShieldPurchaseKind.Mold) s.Quota.HoldUnknownMold(lease.Receipt);
        else if (lease.Kind is HeavyShieldPurchaseKind.ExtraLeft or HeavyShieldPurchaseKind.ExtraRight)
            s.Quota.HoldUnknownUpgrade(lease.Receipt);
        else if (s.Claims.ContainsKey(lease.Receipt)) s.Quota.HoldUnresolvedPaidTool(lease.Receipt);
    }

    internal static bool TryGetPaidBow(DroppableTool bow, out HeavyShieldCareerHandle handle)
    {
        handle = default;
        if (bow == null || bow.gameObject == null || !TryGetRootClaim(bow.gameObject, out var claim)
            || claim.Phase != HeavyShieldSavedClaimPhase.PaidTool) return false;
        handle = claim.Handle;
        return ValidateCareer(handle);
    }

    internal static bool TryGetSoldier(Archer carrier, out HeavyShieldCareerHandle handle)
    {
        handle = default;
        if (carrier == null || carrier.gameObject == null || !TryGetRootClaim(carrier.gameObject, out var claim)
            || claim.Phase != HeavyShieldSavedClaimPhase.Soldier) return false;
        handle = claim.Handle;
        return ValidateCareer(handle);
    }

    internal static bool IsKnownCareerRoot(GameObject root)
        => TryGetRootClaim(root, out var claim) && ValidateCareer(claim.Handle);

    internal static bool CanNativePickup(Peasant peasant, DroppableTool tool)
    {
        if (tool == null || tool.gameObject == null) return true;
        var pointer = tool.gameObject.Pointer;
        if (!RootClaims.ContainsKey(pointer)) return true;
        if (_current == null || _current.Unknown || _current.StageFault
            || peasant == null || peasant.gameObject == null
            || MusketeerIdentity.IsMarked(peasant.gameObject)
            || HeroRecruitment.HasPurchasedCareer(peasant.GetComponent<Character>())
            || !TryGetPaidBow(tool, out _)) return false;
        return true;
    }

    internal static bool ValidateCareer(in HeavyShieldCareerHandle handle)
    {
        Session s = _current;
        if (!SessionValid(s) || handle.CampaignGuid != s.Guid || handle.Land != s.Land
            || handle.World != s.World || !s.Claims.TryGetValue(handle.Receipt, out var claim)
            || claim.Handle != handle || !RootClaims.TryGetValue(handle.Root, out var receipt)
            || receipt != handle.Receipt || !RootLives.TryGetValue(handle.Root, out long life)
            || life != handle.Life) return false;
        try
        {
            var root = claim.RootObject;
            return root != null && root.GetInstanceID() == handle.GoId;
        }
        catch { return false; }
    }

    internal static bool TryGetCombatState(in HeavyShieldCareerHandle handle,
        out HeavyShieldSavedCombatState state)
    {
        state = default;
        if (!ValidateCareer(handle) || !_current.Claims.TryGetValue(handle.Receipt, out var claim)
            || claim.Phase != HeavyShieldSavedClaimPhase.Soldier) return false;
        state = claim.Combat;
        return true;
    }

    internal static bool UpdateCombatState(in HeavyShieldCareerHandle handle,
        in HeavyShieldSavedCombatState state)
    {
        if (!ValidateCareer(handle) || state.Durability < 0
            || state.Durability > HeavyShieldSaveSchema.MaxDurability) return false;
        var claim = _current.Claims[handle.Receipt];
        if (claim.Phase != HeavyShieldSavedClaimPhase.Soldier) return false;
        // Once uncertain or broken, save state cannot silently repair it.
        if (state.Durability > claim.Combat.Durability
            || (claim.Combat.PendingBreak && !state.PendingBreak)
            || (claim.Combat.RetirementUnknown && !state.RetirementUnknown)) return false;
        claim.Combat = state;
        return true;
    }

    internal static bool ConfirmExit(in HeavyShieldCareerHandle handle,
        HeavyShieldExitKind kind, Character successor)
    {
        if (!ValidateCareer(handle) || !_current.Claims.TryGetValue(handle.Receipt, out var claim)
            || claim.Phase != HeavyShieldSavedClaimPhase.Soldier) return false;
        if (kind == HeavyShieldExitKind.Dead)
        {
            if (successor != null || !claim.NativeDeathProven) return false;
        }
        else if (kind == HeavyShieldExitKind.DemotedToPeasant)
        {
            if (!claim.NativeDemotionProven || successor == null
                || successor.GetComponent<Peasant>() == null) return false;
        }
        else return false;
        if (!_current.Quota.ConfirmSoldierExit(handle.Receipt)) return false;
        _current.Claims.Remove(handle.Receipt);
        RootClaims.Remove(handle.Root);
        return true;
    }

    // C supplies native terminal proof only after the same career/life has ended.
    internal static bool ObserveNativeExitProof(in HeavyShieldCareerHandle handle,
        HeavyShieldExitKind kind)
    {
        if (!ValidateCareer(handle) || !_current.Claims.TryGetValue(handle.Receipt, out var claim)
            || claim.Phase != HeavyShieldSavedClaimPhase.Soldier) return false;
        if (kind == HeavyShieldExitKind.Dead) claim.NativeDeathProven = true;
        else if (kind == HeavyShieldExitKind.DemotedToPeasant) claim.NativeDemotionProven = true;
        else return false;
        return true;
    }

    internal static bool AcceptNativePromotion(in HeavyShieldCareerHandle paidBow,
        Character source, Character successor)
    {
        Session s = _current;
        if (s == null || s.Unknown || s.StageFault
            || !s.Claims.TryGetValue(paidBow.Receipt, out var claim)
            || claim.Handle != paidBow || claim.Phase != HeavyShieldSavedClaimPhase.PaidTool
            || source == null || source.GetComponent<Peasant>() == null
            || HeroRecruitment.HasPurchasedCareer(source)
            || MusketeerIdentity.IsMarked(source.gameObject)
            || successor == null || successor.gameObject == null
            || successor.GetComponent<Archer>() == null || !ShieldPromotionInProgress
            || HeroRecruitment.HasPurchasedCareer(successor)
            || MusketeerIdentity.IsMarked(successor.gameObject)
            || !HeavyShieldPromotionBridge.MatchesActiveProof(paidBow, source)
            || !s.Quota.ConfirmSoldier(paidBow.Receipt)) return false;
        RootClaims.Remove(paidBow.Root);
        claim.Phase = HeavyShieldSavedClaimPhase.Soldier;
        claim.NativeId = "";
        claim.Handle = CreateHandle(s, claim, successor.gameObject);
        claim.InitialActivationPending = true;
        RootClaims[claim.Handle.Root] = claim.Receipt;
        try
        {
            var carrier = successor.GetComponent<Archer>();
            var result = HeavyShieldRuntime.TryAttachCarrier(carrier, claim.Handle, claim.Combat);
            if (result == HeavyShieldRuntime.AttachResult.Attached)
            { claim.InitialActivationPending = false; return true; }
            if (result == HeavyShieldRuntime.AttachResult.Deferred)
            { claim.NextActivationAt = Time.time + 0.2f; return true; }
        }
        catch { }
        claim.Phase = HeavyShieldSavedClaimPhase.SoldierUnresolved;
        claim.InitialActivationPending = false;
        s.Unknown = true;
        s.StageFault = true;
        return false; // paid seat stays occupied; no new shield or refund
    }

    internal static bool ShieldPromotionInProgress => HeavyShieldPromotionBridge.ShieldPromotionInProgress;

    internal static void ObservePoolFreshLife(GameObject root, bool freshLife)
    {
        if (!freshLife || root == null) return;
        IntPtr pointer = root.Pointer;
        RootLives[pointer] = ++_nextLife;
        RootClaims.Remove(pointer); // old claim remains occupied but unproven
        HeavyShieldPromotionBridge.OnPoolSpawn(root, true);
    }

    internal static void ObservePoolDespawn(GameObject root, float delay)
    {
        if (root == null || delay > 0f) return;
        bool promoting = HeavyShieldPromotionBridge.IsConsumingRoot(root);
        HeavyShieldPromotionBridge.OnPoolDespawn(root, delay);
        if (promoting || !TryGetRootClaim(root, out var claim)) return;
        if (claim.Phase == HeavyShieldSavedClaimPhase.PaidTool)
        {
            _current.Quota.HoldUnresolvedPaidTool(claim.Receipt);
            claim.Phase = HeavyShieldSavedClaimPhase.PaidToolUnresolved;
        }
        else if (claim.Phase == HeavyShieldSavedClaimPhase.Soldier)
            claim.Phase = HeavyShieldSavedClaimPhase.SoldierUnresolved;
        else return;
        _current.Unknown = true;
        _current.StageFault = true;
        // Neither shop nor side seat is released by pool/disable notification.
    }

    internal static bool RestoreClaim(HeavyShieldSavedClaim saved, GameObject root)
    {
        Session s = _current;
        if (s == null || saved == null || !Guid.TryParseExact(saved.Receipt, "D", out var receipt)
            || root == null || s.Claims.ContainsKey(receipt)) return false;
        bool paid = saved.Phase is HeavyShieldSavedClaimPhase.PaidTool
            or HeavyShieldSavedClaimPhase.PaidToolUnresolved;
        if (!(paid ? s.Quota.TryRestorePaidTool(receipt, saved.Side)
            : s.Quota.TryRestoreSoldier(receipt, saved.Side))) return false;
        var claim = new Claim { Receipt = receipt, Side = saved.Side,
            Phase = saved.Phase, NativeId = saved.NativeId,
            InitialActivationPending = !paid,
            Combat = new(saved.Durability, saved.PendingBreak, saved.RetirementUnknown) };
        claim.Handle = CreateHandle(s, claim, root);
        s.Claims.Add(receipt, claim);
        RootClaims[root.Pointer] = receipt;
        if (paid)
        {
            var bow = root.GetComponent<DroppableTool>();
            if (bow == null || !HeavyShieldPromotionBridge.RegisterIssuedTool(bow, receipt, restored: true))
            {
                claim.Phase = HeavyShieldSavedClaimPhase.PaidToolUnresolved;
                s.Unknown = true;
                s.StageFault = true;
                return false;
            }
            // Existing B contract: observe only the exact successfully rebound
            // paid tool. This cache/visual callback never owns identity success.
            if (claim.Phase == HeavyShieldSavedClaimPhase.PaidTool)
                try { HeavyShieldShopShell.ObservePaidBow(bow); } catch { }
        }
        return true;
    }

    internal static bool RestoreUnboundClaim(HeavyShieldSavedClaim saved)
    {
        Session s = _current;
        if (s == null || saved == null || !Guid.TryParseExact(saved.Receipt, "D", out var receipt)
            || s.Claims.ContainsKey(receipt)) return false;
        bool paid = saved.Phase is HeavyShieldSavedClaimPhase.PaidTool
            or HeavyShieldSavedClaimPhase.PaidToolUnresolved;
        if (!(paid ? s.Quota.TryRestorePaidTool(receipt, saved.Side)
            : s.Quota.TryRestoreSoldier(receipt, saved.Side))) return false;
        s.Claims.Add(receipt, new Claim
        {
            Receipt = receipt, Side = saved.Side,
            Phase = paid ? HeavyShieldSavedClaimPhase.PaidToolUnresolved
                : HeavyShieldSavedClaimPhase.SoldierUnresolved,
            NativeId = saved.NativeId,
            Combat = new(saved.Durability, saved.PendingBreak, saved.RetirementUnknown),
        });
        return true;
    }

    private static bool TryGetRootClaim(GameObject root, out Claim claim)
    {
        claim = null;
        if (root == null || _current == null
            || !RootClaims.TryGetValue(root.Pointer, out var receipt)
            || !_current.Claims.TryGetValue(receipt, out claim)) return false;
        return claim.Handle.Root == root.Pointer && claim.Handle.GoId == root.GetInstanceID()
            && claim.Handle.World == _current.World && claim.Handle.Land == _current.Land;
    }

    private static HeavyShieldCareerHandle CreateHandle(Session s, Claim claim, GameObject root)
    {
        claim.RootObject = root;
        IntPtr pointer = root.Pointer;
        if (!RootLives.TryGetValue(pointer, out long life))
            RootLives[pointer] = life = ++_nextLife;
        return new(claim.Receipt, s.Guid, claim.Side, s.Land,
            pointer, root.GetInstanceID(), s.World, life);
    }

    private static bool ValidateLease(Session s, in HeavyShieldPurchaseLease lease)
        => MatchesLeaseContext(s, lease) && lease.Receipt != Guid.Empty
            && s.Pending.TryGetValue(lease.Receipt, out var stored) && stored == lease;

    private static bool MatchesLeaseContext(Session s, in HeavyShieldPurchaseLease lease)
        => s != null && ValidateCampaign(lease.Campaign)
            && lease.Land == s.Land && lease.World == s.World;

    // Read-only CanPay never changes a reservation. Once a real native
    // Completed reaches commit and its exact stored ticket has lost authority,
    // retain an explicit fault instead of leaving an unexplained Pending.
    private static HeavyShieldCommitResult RejectPending(Session s, in HeavyShieldPurchaseLease lease)
    {
        if (s == null || !s.Pending.TryGetValue(lease.Receipt, out var stored)
            || stored != lease) return HeavyShieldCommitResult.Rejected;
        s.Unknown = s.StageFault = true;
        s.CaptureFaultRecoverable = false;
        if (lease.Kind == HeavyShieldPurchaseKind.Mold) s.Quota.HoldUnknownMold(lease.Receipt);
        else if (lease.Kind is HeavyShieldPurchaseKind.ExtraLeft or HeavyShieldPurchaseKind.ExtraRight)
            s.Quota.HoldUnknownUpgrade(lease.Receipt);
        return HeavyShieldCommitResult.Unknown;
    }

    private static HeavyShieldCampaignToken Token(Session s)
        => new(s.Guid, s.Global, s.Campaign, s.OwnerGeneration, s.Phase);

    private static bool SessionValid(Session s)
        => s != null && s.Global != IntPtr.Zero && s.Campaign != IntPtr.Zero
            && s.OwnerGeneration > 0 && System.Guid.TryParseExact(s.Guid, "D", out _)
            && s.World != 0 && s.Land >= 0 && (s.Phase is HeavyShieldIdentityPhase.Allocated
                or HeavyShieldIdentityPhase.Staged or HeavyShieldIdentityPhase.Loaded);

    private static bool ReadyForMoney(Session s)
    {
        if (!SessionValid(s) || s.Unknown || s.StageFault || s.Quota.Unknown
            || HeavyShieldPersistence.CampaignRestorePending(s) || !SoldiersActive(s))
            return false;
        try
        {
            if (ModConfig.Enabled == null || !ModConfig.Enabled.Value
                || ModConfig.HeavyShieldEnabled == null || !ModConfig.HeavyShieldEnabled.Value
                || NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth
                || IslandSaveData.isSavingGame || !Game.SavingEnabled || Time.timeScale <= 0f)
                return false;
            var loaded = GlobalSaveData._loaded;
            var current = CampaignSaveData.current;
            var managers = Managers.Inst;
            return loaded != null && loaded.Pointer == s.Global
                && current != null && current.Pointer == s.Campaign
                && loaded.currentCampaign == s.Slot && loaded.currentChallenge == s.Challenge
                && managers?.game != null && managers.game.state == Game.State.Playing
                && managers.game.currentLand == s.Land
                && managers.world?.gameLayer != null
                && managers.world.gameLayer.Pointer.ToInt64() == s.World;
        }
        catch { return false; }
    }

    private static bool SoldiersActive(Session s)
    {
        foreach (var claim in s.Claims.Values)
            if (claim.Phase is HeavyShieldSavedClaimPhase.Soldier or HeavyShieldSavedClaimPhase.SoldierUnresolved)
                if (!ValidateCareer(claim.Handle) || !HeavyShieldRuntime.IsCarrierActive(claim.Handle)) return false;
        return true;
    }

    // An already armed native transaction must be allowed to finish while a
    // save, pause, or feature toggle occurs. Those gates bar new reservations,
    // but rejecting native Completed here could consume its frozen floating
    // currency without an A receipt. Exact owner/world/land/authority remains
    // mandatory for this continuation.
    private static bool ActiveArmedContext(Session s)
    {
        if (!SessionValid(s)) return false;
        try
        {
            if (NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth) return false;
            var loaded = GlobalSaveData._loaded;
            var current = CampaignSaveData.current;
            var managers = Managers.Inst;
            return loaded != null && loaded.Pointer == s.Global
                && current != null && current.Pointer == s.Campaign
                && loaded.currentCampaign == s.Slot && loaded.currentChallenge == s.Challenge
                && managers?.game != null && managers.game.currentLand == s.Land
                && managers.world?.gameLayer != null
                && managers.world.gameLayer.Pointer.ToInt64() == s.World;
        }
        catch { return false; }
    }

    // Called by the existing Tick owner before shop/payable activation.
    // CanReservePurchase and GetQuotaView remain strictly read-only.
    internal static void PrimeNativeKeyBeforePayment()
    {
        Session s = _current;
        if (s == null || s.KeyReady || s.Pending.Count != 0 || !ReadyForMoney(s)) return;
        HeavyShieldPersistence.PreflightKeyWrite(s);
    }
}
