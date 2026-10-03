using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod;

/// <summary>
/// One shop slot, one base seat and at most one paid extra seat on each side. This is an in-memory policy,
/// not a save format or proof that a native object exists. The career bridge must
/// rebuild it only from verified saved identities and hold Unknown while unresolved.
/// </summary>
internal sealed class HeavyShieldQuota
{
    internal enum Side : byte { Left, Right }
    internal enum Phase : byte { Paying, PaidTool, Soldier, PaidToolUnresolved }

    private sealed class Claim
    {
        internal Side Side;
        internal Phase Phase;
    }

    internal const int BaseSeatsPerSide = 1;
    internal const int MaxSeatsPerSide = 2;
    internal const int MaxSoldiers = 4;
    internal const int ShopSlots = 1;
    internal const int MaxSeenReceipts = 4096;
    private readonly Dictionary<Guid, Claim> _claims = new(4);
    private readonly HashSet<Guid> _seenReceipts = new();
    private Guid _shopClaim;
    private Guid _moldClaim;
    private Guid _upgradeClaim;
    private Side _upgradeSide;
    private bool _moldUnlocked;
    private bool _leftExtra;
    private bool _rightExtra;
    private bool _unknown = true;
    private bool _restoring;
    private bool _restoreHealthy;

    internal bool Unknown => _unknown;
    internal bool MoldUnlocked => _moldUnlocked;
    internal int OccupiedLeft => Count(Side.Left);
    internal int OccupiedRight => Count(Side.Right);
    internal int OccupiedTotal => _claims.Count;
    internal bool ShopOccupied => _shopClaim != Guid.Empty;
    internal bool MoldPending => _moldClaim != Guid.Empty;
    internal bool UpgradePending => _upgradeClaim != Guid.Empty;
    internal int SeatLimit(Side side) => !Valid(side) ? 0
        : BaseSeatsPerSide + (ExtraSeatPurchased(side) ? 1 : 0);
    internal bool ExtraSeatPurchased(Side side) => side == Side.Left ? _leftExtra
        : side == Side.Right && _rightExtra;
    internal bool CanPay => !_unknown && !_restoring && _moldUnlocked && HasReceiptCapacity
        && !ShopOccupied && !MoldPending && !UpgradePending
        && (Count(Side.Left) < SeatLimit(Side.Left)
            || Count(Side.Right) < SeatLimit(Side.Right));

    // Rebuild only after the sidecar has identified the save. Every saved paid
    // tool and soldier must then be restored before FinishVerifiedRestore(true).
    internal bool BeginVerifiedRestore(bool moldUnlocked, bool leftExtra, bool rightExtra)
    {
        ResetUnverified();
        if ((leftExtra || rightExtra) && !moldUnlocked) return false;
        _moldUnlocked = moldUnlocked;
        _leftExtra = leftExtra;
        _rightExtra = rightExtra;
        _restoring = true;
        _restoreHealthy = true;
        return true;
    }

    internal bool FinishVerifiedRestore(bool allPaidClaimsProven)
    {
        if (!_restoring) return false;
        _restoring = false;
        _unknown = !_restoreHealthy || !allPaidClaimsProven;
        return !_unknown;
    }

    private bool HasReceiptCapacity => _seenReceipts.Count < MaxSeenReceipts;
    internal bool ReceiptCapacityAvailable => HasReceiptCapacity;

    internal bool CanBuyMold => !_unknown && !_restoring && !_moldUnlocked && HasReceiptCapacity
        && !MoldPending && !UpgradePending && !ShopOccupied;

    internal bool TryBeginMoldPayment(Guid id)
    {
        if (!CanBuyMold || !Remember(id)) return false;
        _moldClaim = id;
        return true;
    }

    // The confirmed native payment is a live entitlement. Durability is established
    // only by a later exact native island save followed by PrefsPrepare.
    internal bool ConfirmMoldUnlocked(Guid paymentReceiptId)
    {
        if (_unknown || _restoring || _moldUnlocked || paymentReceiptId == Guid.Empty
            || paymentReceiptId != _moldClaim) return false;
        _moldUnlocked = true;
        _moldClaim = Guid.Empty;
        return true;
    }

    internal bool ConfirmMoldCancelledAfterRefund(Guid id)
    {
        if (id == Guid.Empty || id != _moldClaim) return false;
        _moldClaim = Guid.Empty;
        return true;
    }

    internal bool HoldUnknownMold(Guid id)
    {
        if (id == Guid.Empty || id != _moldClaim) return false;
        _unknown = true;
        return true;
    }

    internal bool CanBuyUpgrade(Side side) => Valid(side) && !_unknown && !_restoring && HasReceiptCapacity
        && _moldUnlocked && !ExtraSeatPurchased(side)
        && !ShopOccupied && !MoldPending && !UpgradePending;

    // One two-gem upgrade transaction at a time. The paid seat becomes live
    // after confirmed native payment; native staging is a separate boundary.
    internal bool TryBeginUpgradePayment(Guid id, Side side)
    {
        if (id == Guid.Empty || !CanBuyUpgrade(side) || !Remember(id)) return false;
        _upgradeClaim = id;
        _upgradeSide = side;
        return true;
    }

    internal bool ConfirmUpgradePurchased(Guid id)
    {
        if (_unknown || _restoring || id == Guid.Empty || id != _upgradeClaim) return false;
        if (_upgradeSide == Side.Left) _leftExtra = true;
        else _rightExtra = true;
        _upgradeClaim = Guid.Empty;
        return true;
    }

    internal bool ConfirmUpgradeCancelledAfterRefund(Guid id)
    {
        if (id == Guid.Empty || id != _upgradeClaim) return false;
        _upgradeClaim = Guid.Empty;
        return true;
    }

    // An ambiguous refund/save result freezes all new payments until verified restore.
    internal bool HoldUnknownUpgrade(Guid id)
    {
        if (id == Guid.Empty || id != _upgradeClaim) return false;
        _unknown = true;
        return true;
    }

    // A native transaction receipt and its seat are reserved before accepting coins.
    // The selected side is binding. A full left side must never sell a right seat.
    internal bool CanReservePayment(Side side)
        => Valid(side) && CanPay && Count(side) < SeatLimit(side);

    internal bool TryReservePayment(Guid id, Side selected, out Side assigned)
    {
        assigned = default;
        if (id == Guid.Empty || !CanReservePayment(selected)
            || _seenReceipts.Contains(id) || _seenReceipts.Count >= MaxSeenReceipts) return false;
        assigned = selected;
        _seenReceipts.Add(id);
        _claims.Add(id, new Claim { Side = assigned, Phase = Phase.Paying });
        _shopClaim = id;
        return true;
    }

    // Cancellation releases a seat only after the native floating currency was
    // returned. Calling this for an already paid item is deliberately rejected.
    internal bool ConfirmUnpaidCancellation(Guid id)
    {
        if (!OwnsShop(id, Phase.Paying)) return false;
        _claims.Remove(id);
        _shopClaim = Guid.Empty;
        return true;
    }

    // Mark only after the single paid native tool has actually been issued.
    internal bool ConfirmPaidTool(Guid id)
    {
        if (!OwnsShop(id, Phase.Paying)) return false;
        _claims[id].Phase = Phase.PaidTool;
        return true;
    }

    // The exact issued tool life must be witnessed promoting to the exact soldier.
    // The seat transfers without a gap; the rack frees only after that proof.
    internal bool ConfirmSoldier(Guid id)
    {
        if (!OwnsShop(id, Phase.PaidTool)) return false;
        _claims[id].Phase = Phase.Soldier;
        _shopClaim = Guid.Empty;
        return true;
    }

    // A paid item that vanished without a witnessed soldier stays occupied and
    // blocks the one shop slot until it is recovered or explicitly refunded.
    internal bool HoldUnresolvedPaidTool(Guid id)
    {
        if (!OwnsShop(id, Phase.PaidTool)) return false;
        _claims[id].Phase = Phase.PaidToolUnresolved;
        return true;
    }

    internal bool ConfirmRefundedTool(Guid id)
    {
        if (id == Guid.Empty || id != _shopClaim || !_claims.TryGetValue(id, out var claim)
            || (claim.Phase != Phase.PaidTool && claim.Phase != Phase.PaidToolUnresolved)) return false;
        _claims.Remove(id);
        _shopClaim = Guid.Empty;
        return true;
    }

    // Death or shield-break demotion releases the role only after native outcome
    // verification. An unconfirmed OnDisable/pool return must not call this.
    internal bool ConfirmSoldierExit(Guid id)
    {
        if (id == Guid.Empty || !_claims.TryGetValue(id, out var claim)
            || claim.Phase != Phase.Soldier) return false;
        _claims.Remove(id);
        return true;
    }

    // Restore only exact, witnessed identities during the blocked restore phase.
    // A failed row poisons that restore so FinishVerifiedRestore cannot enable pay.
    internal bool TryRestoreSoldier(Guid id, Side side)
    {
        if (!CanRestore(id, side)) { _restoreHealthy = false; return false; }
        _seenReceipts.Add(id);
        _claims.Add(id, new Claim { Side = side, Phase = Phase.Soldier });
        return true;
    }

    // The shop remains occupied across a save/load until the exact paid native
    // tool is identified and re-bound. Otherwise FinishVerifiedRestore(false).
    internal bool TryRestorePaidTool(Guid id, Side side)
    {
        if (_shopClaim != Guid.Empty || !CanRestore(id, side))
        { _restoreHealthy = false; return false; }
        _seenReceipts.Add(id);
        _claims.Add(id, new Claim { Side = side, Phase = Phase.PaidTool });
        _shopClaim = id;
        return true;
    }

    internal void ResetUnverified()
    {
        _claims.Clear();
        _seenReceipts.Clear();
        _shopClaim = Guid.Empty;
        _moldClaim = Guid.Empty;
        _upgradeClaim = Guid.Empty;
        _moldUnlocked = _leftExtra = _rightExtra = false;
        _unknown = true;
        _restoring = false;
        _restoreHealthy = false;
    }

    private bool CanRestore(Guid id, Side side)
        => _restoring && _restoreHealthy && _moldUnlocked && id != Guid.Empty
           && Valid(side) && !_seenReceipts.Contains(id)
           && _seenReceipts.Count < MaxSeenReceipts && Count(side) < SeatLimit(side);

    // Seed durable upgrade receipts during verified load; duplicate proof
    // poisons the restore rather than allowing a second charge.
    internal bool TryRestoreSpentReceipt(Guid id)
    {
        if (!_restoring || !_restoreHealthy || !Remember(id))
        { _restoreHealthy = false; return false; }
        return true;
    }

    private bool Remember(Guid id)
    {
        if (id == Guid.Empty || _seenReceipts.Count >= MaxSeenReceipts) return false;
        return _seenReceipts.Add(id);
    }

    private bool OwnsShop(Guid id, Phase phase)
        => id != Guid.Empty && id == _shopClaim && _claims.TryGetValue(id, out var claim)
           && claim.Phase == phase;

    private int Count(Side side)
    {
        int count = 0;
        foreach (var claim in _claims.Values)
            if (claim.Side == side) count++;
        return count;
    }

    private static bool Valid(Side side) => side == Side.Left || side == Side.Right;
}
