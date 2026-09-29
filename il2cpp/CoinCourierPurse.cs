using System;

namespace KingdomEnhancedMod;

/// <summary>
/// Economy outcome vocabulary shared by the courier purse, the delivery bridge and the
/// narrow treasury entry. Every entry point returns one of these three states plus a
/// reason, so callers never have to guess whether money moved:
/// - NotApplied: nothing changed; finishing or reselecting is safe.
/// - Applied: the economic effect is committed; later presentation faults never undo it.
/// - Indeterminate: the outcome cannot be proven. The purse latches a fault and refuses
///   all further economy until an explicit recovery decision outside this layer.
/// </summary>
internal enum CoinCourierStatus
{
    NotApplied = 0,
    Applied = 1,
    Indeterminate = 2,
}

/// <summary>
/// Single reason vocabulary for courier results and the latched fault. The purse emits
/// the purse-side subset; the economy emits target/wallet reasons and maps the treasury
/// entry's own reason set into this vocabulary.
/// </summary>
internal enum CoinCourierReason
{
    None = 0,

    // ---- purse-side ----
    Reentrant,
    Frozen,
    InvalidArgument,
    CapacityExceeded,
    NoAvailableCoins,
    PendingExists,
    NoPendingDelivery,
    PendingLifeMismatch,

    // ---- world / session gate ----
    ModDisabled,
    NoAuthority,
    Online,
    Paused,
    WorldNotReady,

    // ---- treasury entry ----
    BankGateClosed,
    BankPrimeFailed,
    BankEmpty,
    BankUnreadable,
    BankWriteUnknown,

    // ---- delivery ----
    TargetInvalid,
    WalletFull,
    WalletRejected,
    WalletWriteUnknown,

    // ---- post-commit faults (the economic effect stands) ----
    WriteFault,
    PresentationFailed,

    // ---- defensive: a committed treasury debit could not be credited to the purse ----
    PurseCreditRefused,
}

/// <summary>Kind of unresolved outcome latched on a purse.</summary>
internal enum CoinCourierFaultKind
{
    None = 0,
    /// <summary>A treasury debit attempt whose result is unknown.</summary>
    BagUnknown = 1,
    /// <summary>A delivery handoff whose result is unknown; one coin stays reserved.</summary>
    DeliveryUnknown = 2,
}

/// <summary>
/// Latched evidence for an unresolved outcome. Int fields use -1 for "not observed".
/// The fault is part of the serializable purse snapshot and is never cleared
/// automatically: an unresolved result must not be silently dropped or re-guessed.
/// </summary>
internal readonly struct CoinCourierFault
{
    internal readonly CoinCourierFaultKind Kind;
    internal readonly CoinCourierReason Reason;
    internal readonly int BankBefore;
    internal readonly int BankAfter;
    internal readonly int WalletBefore;
    internal readonly int WalletAfter;
    internal readonly long ExpectedLife;

    internal CoinCourierFault(CoinCourierFaultKind kind, CoinCourierReason reason,
        int bankBefore, int bankAfter, int walletBefore, int walletAfter, long expectedLife)
    {
        Kind = kind;
        Reason = reason;
        BankBefore = bankBefore;
        BankAfter = bankAfter;
        WalletBefore = walletBefore;
        WalletAfter = walletAfter;
        ExpectedLife = expectedLife;
    }
}

/// <summary>Result of one purse or economy entry point.</summary>
internal readonly struct CoinCourierResult
{
    internal readonly CoinCourierStatus Status;
    internal readonly CoinCourierReason Reason;

    internal CoinCourierResult(CoinCourierStatus status, CoinCourierReason reason)
    {
        Status = status;
        Reason = reason;
    }

    internal bool Applied => Status == CoinCourierStatus.Applied;
    internal bool Indeterminate => Status == CoinCourierStatus.Indeterminate;
}

/// <summary>
/// Explicit, plain-field serializable state of one purse. The runtime/persistence layer
/// owns whether a purse exists (ownership identity, save slots, campaign generation);
/// this layer never mints ids and never writes files.
/// </summary>
internal readonly struct CoinCourierPurseSnapshot
{
    internal readonly int Coins;
    internal readonly bool PendingDelivery;
    internal readonly long PendingLife;
    internal readonly CoinCourierFault Fault;

    internal CoinCourierPurseSnapshot(int coins, bool pendingDelivery, long pendingLife, CoinCourierFault fault)
    {
        Coins = coins;
        PendingDelivery = pendingDelivery;
        PendingLife = pendingLife;
        Fault = fault;
    }
}

/// <summary>
/// Pure C# economic state of the single coin courier character.
///
/// Holds only coins already taken from the treasury, at most one pending delivery
/// handoff and at most one latched fault. It performs no native calls, no file I/O and
/// keeps no static ledger; capacity is supplied by the caller on every credit, and a
/// smaller capacity never trims an existing balance. The available balance excludes the
/// coin reserved for an in-flight delivery.
///
/// Every mutating entry point is guarded against same-call re-entrancy: a nested call is
/// refused with NotApplied/Reentrant instead of observing or mutating a half-applied
/// step. Read-only accessors (Coins, AvailableCoins, Capture, ...) always stay available
/// so the runtime can log or persist the fault evidence.
/// </summary>
internal sealed class CoinCourierPurse
{
    private int _coins;
    private bool _pendingDelivery;
    private long _pendingLife;
    private bool _busy;
    private CoinCourierFault _fault;

    /// <summary>Coins held by the purse, including one reserved for the in-flight delivery.</summary>
    internal int Coins => _coins;

    /// <summary>True while one coin is reserved for a delivery handoff.</summary>
    internal bool HasPendingDelivery => _pendingDelivery;

    /// <summary>Lifetime of the Knight the reserved coin is bound to; 0 when none.</summary>
    internal long PendingDeliveryLife => _pendingLife;

    /// <summary>Spendable coins for a new delivery (excludes the reserved coin).</summary>
    internal int AvailableCoins => _pendingDelivery ? _coins - 1 : _coins;

    internal bool IsFaulted => _fault.Kind != CoinCourierFaultKind.None;

    internal CoinCourierFault Fault => _fault;

    /// <summary>Read-only snapshot; never blocked by the re-entrancy guard.</summary>
    internal CoinCourierPurseSnapshot Capture()
        => new CoinCourierPurseSnapshot(_coins, _pendingDelivery, _pendingLife, _fault);

    /// <summary>
    /// Rebuilds a purse from a snapshot. A pending handoff without a recorded fault can
    /// only mean the process stopped mid-delivery, so the restore keeps the pending coin
    /// but surfaces it as an explicit DeliveryUnknown fault instead of silently treating
    /// the coin as spendable again.
    /// </summary>
    internal static bool TryRestore(in CoinCourierPurseSnapshot snapshot, out CoinCourierPurse purse,
        out CoinCourierReason reason)
    {
        purse = null;
        reason = CoinCourierReason.None;
        if (snapshot.Coins < 0) { reason = CoinCourierReason.InvalidArgument; return false; }
        if (snapshot.PendingDelivery)
        {
            if (snapshot.Coins < 1 || snapshot.PendingLife == 0)
            {
                reason = CoinCourierReason.InvalidArgument;
                return false;
            }
        }
        else if (snapshot.PendingLife != 0)
        {
            reason = CoinCourierReason.InvalidArgument;
            return false;
        }
        CoinCourierFault fault = snapshot.Fault;
        if (fault.Kind != CoinCourierFaultKind.None)
        {
            if (fault.BankBefore < -1 || fault.BankAfter < -1
                || fault.WalletBefore < -1 || fault.WalletAfter < -1)
            {
                reason = CoinCourierReason.InvalidArgument;
                return false;
            }
        }
        else if (snapshot.PendingDelivery)
        {
            fault = new CoinCourierFault(CoinCourierFaultKind.DeliveryUnknown,
                CoinCourierReason.WalletWriteUnknown, -1, -1, -1, -1, snapshot.PendingLife);
        }

        purse = new CoinCourierPurse
        {
            _coins = snapshot.Coins,
            _pendingDelivery = snapshot.PendingDelivery,
            _pendingLife = snapshot.PendingLife,
            _fault = fault,
        };
        return true;
    }

    /// <summary>Bagging side: one coin just left the treasury and enters the purse.</summary>
    internal CoinCourierResult TryCreditTakenCoin(int capacity)
    {
        if (!TryEnter(out CoinCourierResult busy)) return busy;
        try
        {
            if (capacity <= 0) return Refuse(CoinCourierReason.InvalidArgument);
            if (IsFaulted) return Refuse(CoinCourierReason.Frozen);
            if (_pendingDelivery) return Refuse(CoinCourierReason.PendingExists);
            if (_coins >= capacity) return Refuse(CoinCourierReason.CapacityExceeded);
            _coins++;
            return new CoinCourierResult(CoinCourierStatus.Applied, CoinCourierReason.None);
        }
        finally { Exit(); }
    }

    /// <summary>
    /// Delivery side: reserves one coin from the spendable balance for a handoff to the
    /// given Knight lifetime. The coin stays in the purse until the handoff is confirmed.
    /// </summary>
    internal CoinCourierResult TryReserveOneForDelivery(long expectedLife)
    {
        if (!TryEnter(out CoinCourierResult busy)) return busy;
        try
        {
            if (expectedLife == 0) return Refuse(CoinCourierReason.InvalidArgument);
            if (IsFaulted) return Refuse(CoinCourierReason.Frozen);
            if (_pendingDelivery) return Refuse(CoinCourierReason.PendingExists);
            if (AvailableCoins < 1) return Refuse(CoinCourierReason.NoAvailableCoins);
            _pendingDelivery = true;
            _pendingLife = expectedLife;
            return new CoinCourierResult(CoinCourierStatus.Applied, CoinCourierReason.None);
        }
        finally { Exit(); }
    }

    /// <summary>Confirmed handoff: the reserved coin leaves the purse.</summary>
    internal CoinCourierResult TryCompleteDelivery(long expectedLife)
    {
        if (!TryEnter(out CoinCourierResult busy)) return busy;
        try
        {
            if (IsFaulted) return Refuse(CoinCourierReason.Frozen);
            if (!_pendingDelivery) return Refuse(CoinCourierReason.NoPendingDelivery);
            if (_pendingLife != expectedLife) return Refuse(CoinCourierReason.PendingLifeMismatch);
            _coins--;
            _pendingDelivery = false;
            _pendingLife = 0;
            return new CoinCourierResult(CoinCourierStatus.Applied, CoinCourierReason.None);
        }
        finally { Exit(); }
    }

    /// <summary>Confirmed non-handoff: the reserved coin becomes spendable again.</summary>
    internal CoinCourierResult TryReturnDeliveryReservation(long expectedLife)
    {
        if (!TryEnter(out CoinCourierResult busy)) return busy;
        try
        {
            if (IsFaulted) return Refuse(CoinCourierReason.Frozen);
            if (!_pendingDelivery) return Refuse(CoinCourierReason.NoPendingDelivery);
            if (_pendingLife != expectedLife) return Refuse(CoinCourierReason.PendingLifeMismatch);
            _pendingDelivery = false;
            _pendingLife = 0;
            return new CoinCourierResult(CoinCourierStatus.Applied, CoinCourierReason.None);
        }
        finally { Exit(); }
    }

    /// <summary>
    /// Latches the first unresolved outcome. Later faults never overwrite the first
    /// evidence, and the fault is never cleared automatically: a recovery decision must
    /// come from outside this layer.
    /// </summary>
    internal CoinCourierResult MarkUnknown(CoinCourierFault fault)
    {
        if (!TryEnter(out CoinCourierResult busy)) return busy;
        try
        {
            if (fault.Kind == CoinCourierFaultKind.None) return Refuse(CoinCourierReason.InvalidArgument);
            if (IsFaulted) return Refuse(CoinCourierReason.Frozen);
            _fault = fault;
            return new CoinCourierResult(CoinCourierStatus.Applied, CoinCourierReason.None);
        }
        finally { Exit(); }
    }

    private bool TryEnter(out CoinCourierResult busy)
    {
        if (_busy)
        {
            busy = new CoinCourierResult(CoinCourierStatus.NotApplied, CoinCourierReason.Reentrant);
            return false;
        }
        _busy = true;
        busy = default;
        return true;
    }

    private void Exit() => _busy = false;

    private static CoinCourierResult Refuse(CoinCourierReason reason)
        => new CoinCourierResult(CoinCourierStatus.NotApplied, reason);
}
