using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Two-stage coin courier economy.
///
/// Bagging (treasury -> purse) runs through the narrow one-coin entry owned by
/// <see cref="PatchEconomy_Banker"/>: the treasury is debited and the purse credited in
/// the same call, with the Greek shared ledger kept in memory sync before the existing
/// staging/refresh work. A committed debit is never reported as NotApplied and never
/// re-debited; an unknown outcome latches a fault on the purse and preserves the
/// observed before/after evidence.
///
/// Delivery (purse -> Knight wallet) does not require the bank, a banker or any treasury
/// balance. One coin is reserved from the spendable purse balance first; the Knight is
/// re-validated (same lifetime, same wallet, current world layer, not dead/grabbed/
/// embarking) and the real wallet value is written with SetCurrency and read back in the
/// same call. A confirmed read-back consumes the reservation, a confirmed no-op returns
/// it, and anything unprovable freezes the purse with the pending coin retained. The
/// result never guesses from later wallet values, never resends or refunds, never
/// re-targets another Knight, creates no droppable coin and touches no Gems.
///
/// All entry points share one re-entrancy guard: a nested call is refused with
/// NotApplied/Reentrant before it can observe a half-applied step.
/// </summary>
internal static class CoinCourierEconomy
{
    private static int _inCall;
    private static bool _loggedFault;

    /// <summary>True while a courier entry point is running (diagnostics/tests).</summary>
    internal static bool InCall => _inCall != 0;

    /// <summary>
    /// One treasury coin into the purse. Requires the offline authoritative world and the
    /// current native banker; Greek primes the shared ledger first, other biomes use only
    /// the native treasury and never touch the shared key.
    /// </summary>
    internal static CoinCourierResult TryBagOneCoin(CoinCourierPurse purse, Banker banker, int purseCapacity)
    {
        if (!TryEnter(out CoinCourierResult busy)) return busy;
        try
        {
            if (purse == null) return Refuse(CoinCourierReason.InvalidArgument);
            if (purse.IsFaulted) return Refuse(CoinCourierReason.Frozen);
            if (banker == null || purseCapacity <= 0) return Refuse(CoinCourierReason.InvalidArgument);
            if (purse.HasPendingDelivery) return Refuse(CoinCourierReason.PendingExists);
            if (purse.Coins >= purseCapacity) return Refuse(CoinCourierReason.CapacityExceeded);

            CoinCourierReason gate = WorldGate();
            if (gate != CoinCourierReason.None) return Refuse(gate);

            CourierBankDebit debit = PatchEconomy_Banker.TryWithdrawOneCoinForCourier(banker);
            if (debit.Outcome == CourierBankOutcome.NotApplied)
                return Refuse(MapBankReason(debit.Reason));
            if (debit.Outcome == CourierBankOutcome.Indeterminate)
            {
                // The treasury write may or may not have landed: never credit the purse on
                // a guess, keep the observed evidence and stop this character's economy.
                LogOnce("bag-unknown-" + debit.Reason);
                purse.MarkUnknown(new CoinCourierFault(CoinCourierFaultKind.BagUnknown,
                    CoinCourierReason.BankWriteUnknown, debit.Before, debit.After, -1, -1, 0));
                return new CoinCourierResult(CoinCourierStatus.Indeterminate, CoinCourierReason.BankWriteUnknown);
            }

            CoinCourierResult credit = purse.TryCreditTakenCoin(purseCapacity);
            if (credit.Applied)
                return new CoinCourierResult(CoinCourierStatus.Applied, AppliedBankReason(debit.Reason));

            // Defensive: the treasury is already debited, so the shortfall must never be
            // reported as a clean refusal nor hidden by re-debiting later.
            LogOnce("bag-credit-" + credit.Reason);
            purse.MarkUnknown(new CoinCourierFault(CoinCourierFaultKind.BagUnknown,
                CoinCourierReason.PurseCreditRefused, debit.Before, debit.After, -1, -1, 0));
            return new CoinCourierResult(CoinCourierStatus.Indeterminate, CoinCourierReason.PurseCreditRefused);
        }
        finally { Exit(); }
    }

    /// <summary>
    /// One coin from the purse into the Knight's wallet. Reserves before dispatch,
    /// consumes on a confirmed credit, returns the reservation on a confirmed no-op and
    /// freezes on anything unprovable (read-back fault, identity change, foreign write).
    /// </summary>
    internal static CoinCourierResult TryDeliverOne(CoinCourierPurse purse, Knight knight, long expectedLife)
    {
        if (!TryEnter(out CoinCourierResult busy)) return busy;
        try
        {
            if (purse == null) return Refuse(CoinCourierReason.InvalidArgument);
            if (purse.IsFaulted) return Refuse(CoinCourierReason.Frozen);
            if (knight == null || expectedLife == 0) return Refuse(CoinCourierReason.InvalidArgument);
            if (purse.HasPendingDelivery) return Refuse(CoinCourierReason.PendingExists);
            if (purse.AvailableCoins < 1) return Refuse(CoinCourierReason.NoAvailableCoins);

            CoinCourierReason gate = WorldGate();
            if (gate != CoinCourierReason.None) return Refuse(gate);

            CoinCourierResult reserve = purse.TryReserveOneForDelivery(expectedLife);
            if (!reserve.Applied) return Refuse(reserve.Reason);

            if (!CoinCourierTargeting.IsCurrentDeliveryCandidate(knight, expectedLife, out Wallet wallet))
                return ReturnReservationOrFreeze(purse, expectedLife, CoinCourierReason.TargetInvalid, -1, -1);

            int before;
            int capacity;
            try
            {
                before = wallet.Coins;
                capacity = wallet.TotalCapacity;
            }
            catch (Exception e)
            {
                LogOnce("wallet-read-" + e.GetType().Name);
                return ReturnReservationOrFreeze(purse, expectedLife, CoinCourierReason.TargetInvalid, -1, -1);
            }
            if (before < 0 || capacity <= 0)
                return ReturnReservationOrFreeze(purse, expectedLife, CoinCourierReason.TargetInvalid, before, -1);
            if (before >= capacity)
                return ReturnReservationOrFreeze(purse, expectedLife, CoinCourierReason.WalletFull, before, before);

            // Real native write + same-call read-back. SetCurrency clamps to
            // [0, TotalCapacity] and the NPC path has no player RPC; only the observed
            // value of this exact wallet decides the outcome.
            bool writeFault = false;
            try { wallet.SetCurrency(CurrencyType.Coins, before + 1); }
            catch (Exception e)
            {
                writeFault = true;
                LogOnce("wallet-write-" + e.GetType().Name);
            }

            int after = -1;
            bool readOk = false;
            try
            {
                after = wallet.Coins;
                readOk = true;
            }
            catch (Exception e) { LogOnce("wallet-readback-" + e.GetType().Name); }

            bool sameWallet = false;
            try
            {
                sameWallet = CoinCourierTargeting.IsCurrentDeliveryCandidate(knight, expectedLife, out Wallet current)
                    && current != null && wallet.Pointer != IntPtr.Zero && current.Pointer == wallet.Pointer;
            }
            catch (Exception e) { LogOnce("wallet-identity-" + e.GetType().Name); }

            if (sameWallet && readOk && after == before + 1)
            {
                CoinCourierResult consume = purse.TryCompleteDelivery(expectedLife);
                if (!consume.Applied)
                {
                    LogOnce("delivery-consume-" + consume.Reason);
                    return FreezeDelivery(purse, expectedLife, before, after);
                }
                return new CoinCourierResult(CoinCourierStatus.Applied,
                    writeFault ? CoinCourierReason.WriteFault : CoinCourierReason.None);
            }

            if (sameWallet && readOk && after == before)
                return ReturnReservationOrFreeze(purse, expectedLife, CoinCourierReason.WalletRejected, before, after);

            return FreezeDelivery(purse, expectedLife, before, readOk ? after : -1);
        }
        finally { Exit(); }
    }

    /// <summary>
    /// Shared world gate for both stages: mod enabled, not paused, offline authority and
    /// a loaded playing world. Online sessions stay unsupported until a verified wallet
    /// sync exists; HasWorldAuth alone is not accepted as proof of online safety.
    /// </summary>
    private static CoinCourierReason WorldGate()
    {
        try
        {
            if (ModConfig.Enabled == null || !ModConfig.Enabled.Value) return CoinCourierReason.ModDisabled;
            if (Time.timeScale <= 0f) return CoinCourierReason.Paused;
            if (!NetworkBigBoss.HasWorldAuth) return CoinCourierReason.NoAuthority;
            if (NetworkBigBoss.IsOnline) return CoinCourierReason.Online;
            Managers managers = Managers.Inst;
            if (managers == null || managers.game == null || managers.game.state != Game.State.Playing
                || managers.world == null || managers.kingdom == null)
                return CoinCourierReason.WorldNotReady;
            return CoinCourierReason.None;
        }
        catch (Exception e)
        {
            LogOnce("gate-" + e.GetType().Name);
            return CoinCourierReason.WorldNotReady;
        }
    }

    private static CoinCourierResult ReturnReservationOrFreeze(CoinCourierPurse purse, long expectedLife,
        CoinCourierReason refuseReason, int walletBefore, int walletAfter)
    {
        CoinCourierResult returned = purse.TryReturnDeliveryReservation(expectedLife);
        if (returned.Applied)
            return new CoinCourierResult(CoinCourierStatus.NotApplied, refuseReason);
        LogOnce("delivery-return-" + returned.Reason);
        return FreezeDelivery(purse, expectedLife, walletBefore, walletAfter);
    }

    private static CoinCourierResult FreezeDelivery(CoinCourierPurse purse, long expectedLife,
        int walletBefore, int walletAfter)
    {
        purse.MarkUnknown(new CoinCourierFault(CoinCourierFaultKind.DeliveryUnknown,
            CoinCourierReason.WalletWriteUnknown, -1, -1, walletBefore, walletAfter, expectedLife));
        return new CoinCourierResult(CoinCourierStatus.Indeterminate, CoinCourierReason.WalletWriteUnknown);
    }

    private static CoinCourierReason MapBankReason(CourierBankReason reason)
    {
        switch (reason)
        {
            case CourierBankReason.ModDisabled: return CoinCourierReason.ModDisabled;
            case CourierBankReason.NoAuthority: return CoinCourierReason.NoAuthority;
            case CourierBankReason.Online: return CoinCourierReason.Online;
            case CourierBankReason.Paused: return CoinCourierReason.Paused;
            case CourierBankReason.PrimeFailed: return CoinCourierReason.BankPrimeFailed;
            case CourierBankReason.Empty: return CoinCourierReason.BankEmpty;
            case CourierBankReason.Unreadable: return CoinCourierReason.BankUnreadable;
            case CourierBankReason.WriteUnknown:
            case CourierBankReason.ReadbackUnknown: return CoinCourierReason.BankWriteUnknown;
            case CourierBankReason.WriteFault: return CoinCourierReason.WriteFault;
            case CourierBankReason.PresentationFailed: return CoinCourierReason.PresentationFailed;
            default: return CoinCourierReason.BankGateClosed;
        }
    }

    private static CoinCourierReason AppliedBankReason(CourierBankReason reason)
    {
        if (reason == CourierBankReason.WriteFault) return CoinCourierReason.WriteFault;
        if (reason == CourierBankReason.PresentationFailed) return CoinCourierReason.PresentationFailed;
        return CoinCourierReason.None;
    }

    private static bool TryEnter(out CoinCourierResult busy)
    {
        if (_inCall != 0)
        {
            busy = new CoinCourierResult(CoinCourierStatus.NotApplied, CoinCourierReason.Reentrant);
            return false;
        }
        _inCall = 1;
        busy = default;
        return true;
    }

    private static void Exit() => _inCall = 0;

    private static CoinCourierResult Refuse(CoinCourierReason reason)
        => new CoinCourierResult(CoinCourierStatus.NotApplied, reason);

    private static void LogOnce(string tag)
    {
        if (_loggedFault) return;
        _loggedFault = true;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[CoinCourier] economy fault: " + tag);
        }
        catch { }
    }
}
