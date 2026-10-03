using System;

namespace KingdomEnhancedMod;

/// <summary>One immutable receipt for one owned native payable. This code also runs in the offline suite.</summary>
internal sealed class HeavyShieldShopPayment
{
    internal enum Outcome : byte { Ignored, Applied, RejectedUnrefunded, Unknown }
    internal delegate bool Reserve(HeavyShieldPurchaseKind kind, out HeavyShieldPurchaseLease lease);

    internal readonly record struct NativeReceipt(long Payer, long Payable, long CompletingPayable,
        bool Completed, int Price, CurrencyType Currency, int FloatingCount,
        Func<int, CurrencyType> FloatingKind);

    // A refused/throwing reservation has no trustworthy purchase lease. This local observation
    // is deliberately separate from A's money/claim authority and never releases an A receipt.
    internal sealed class FailedStartup
    {
        internal long Payer { get; }
        internal long Payable { get; }
        internal long ShopLife { get; }
        internal long World { get; }
        internal long Kingdom { get; }
        internal HeavyShieldPurchaseKind Kind { get; }
        internal int Price { get; }
        internal CurrencyType Currency { get; }
        internal int StartedFrame { get; }
        internal string Reason { get; private set; }
        internal bool CancelRequested { get; private set; }
        internal bool DropReturned { get; private set; }
        internal bool Unknown { get; private set; }
        internal bool Finished { get; private set; }
        internal bool Pending => !Finished;

        internal FailedStartup(long payer, long payable, long life, long world, long kingdom,
            HeavyShieldPurchaseKind kind, int price, CurrencyType currency, int frame, string reason)
        {
            Payer = payer; Payable = payable; ShopLife = life; World = world; Kingdom = kingdom;
            Kind = kind; Price = price; Currency = currency; StartedFrame = frame; Reason = reason;
        }
        internal bool TryRequestCancel(int frame)
        {
            if (frame <= StartedFrame || CancelRequested || Unknown || Finished) return false;
            CancelRequested = true; // before calling native code; even a throw cannot be replayed
            return true;
        }
        internal void ObserveDropReturn() { if (CancelRequested && !Unknown) DropReturned = true; }
        internal void HoldUnknown(string reason) { if (!Finished) { Unknown = true; Reason = reason; } }
        internal bool ObserveCleanup(int frame, bool empty, bool completingCleared, bool nativeSettled)
        {
            if (frame <= StartedFrame || Unknown || Finished || !CancelRequested || !DropReturned
                || !empty || !completingCleared || !nativeSettled) return false;
            Finished = true; return true;
        }
    }

    internal HeavyShieldPurchaseKind Kind { get; }
    internal HeavyShieldQuota.Side Side { get; }
    internal CurrencyType Currency { get; }
    internal int Price { get; }
    internal HeavyShieldPurchaseLease Lease { get; private set; }
    internal long Payer { get; private set; }
    internal long Payable { get; private set; }
    internal long ShopLife { get; private set; }
    internal bool Armed { get; private set; }
    internal bool Latched { get; private set; }
    internal bool Unknown { get; private set; }
    internal bool CancellationRequested { get; private set; }
    internal bool CancellationConfirmed { get; private set; }
    internal FailedStartup FailedStart { get; private set; }
    internal bool Busy => Armed || Latched || CancellationRequested || FailedStart?.Pending == true;
    private int _completedFrame;

    internal HeavyShieldShopPayment(HeavyShieldPurchaseKind kind)
    {
        Kind = kind;
        Price = PriceFor(kind);
        Currency = IsGem(kind) ? CurrencyType.Gems : CurrencyType.Coins;
        Side = kind == HeavyShieldPurchaseKind.ExtraRight || kind == HeavyShieldPurchaseKind.ShieldRight
            ? HeavyShieldQuota.Side.Right : HeavyShieldQuota.Side.Left;
    }

    internal static bool IsGem(HeavyShieldPurchaseKind kind)
        => kind == HeavyShieldPurchaseKind.Mold || kind == HeavyShieldPurchaseKind.ExtraLeft
            || kind == HeavyShieldPurchaseKind.ExtraRight;

    internal static int PriceFor(HeavyShieldPurchaseKind kind) => kind switch
    {
        HeavyShieldPurchaseKind.Mold => 4,
        HeavyShieldPurchaseKind.ExtraLeft or HeavyShieldPurchaseKind.ExtraRight => 2,
        HeavyShieldPurchaseKind.ShieldLeft or HeavyShieldPurchaseKind.ShieldRight => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal bool TryArm(long payer, long payable, long shopLife, bool ready, Reserve reserve,
        Action<HeavyShieldPurchaseLease, string> holdUnknown)
    {
        if (Busy || !ready || payer == 0 || payable == 0 || shopLife <= 0 || reserve == null) return false;
        if (!reserve(Kind, out var lease)) return false;
        Lease = lease; Payer = payer; Payable = payable; ShopLife = shopLife; Armed = true;
        if (lease.Receipt == Guid.Empty || lease.Kind != Kind || lease.Side != Side || lease.World <= 0)
        {
            LatchUnknown("reservation-contract-mismatch", holdUnknown);
            return false;
        }
        return true;
    }

    internal void ObserveFailedStart(long payer, long payable, long shopLife, long world, long kingdom,
        int price, CurrencyType currency, int frame, string reason)
    {
        // Never replace an older frozen observation with a later/foreign Started callback.
        FailedStart ??= new FailedStartup(payer, payable, shopLife, world, kingdom, Kind, price, currency, frame, reason);
    }

    internal bool Owns(long payer, long payable, long shopLife)
        => Busy && payer != 0 && payer == Payer && payable == Payable && shopLife == ShopLife;

    // Native Player.UpdatePayState asks CanPay again after Started has reserved the slot.
    // This is an existing-receipt read, never a second reservation probe.
    internal bool MayContinue(long payer, long payable, long shopLife, int price, CurrencyType currency,
        bool campaignValid) => Armed && !Latched && !CancellationRequested && campaignValid
            && Owns(payer, payable, shopLife) && price == Price && currency == Currency;

    internal bool HasNativePaymentReceipt(in NativeReceipt native)
    {
        if (!Armed || Latched || CancellationRequested || native.Payer != Payer
            || native.Payable != Payable || native.CompletingPayable != Payable || !native.Completed
            || native.Price != Price || native.Currency != Currency || native.FloatingCount != Price
            || native.FloatingKind == null) return false;
        try
        {
            for (int i = 0; i < Price; i++) if (native.FloatingKind(i) != Currency) return false;
            return true;
        }
        catch { return false; }
    }

    internal Outcome Complete(in NativeReceipt native, long shopLife, int frame,
        bool campaignValid, Func<HeavyShieldPurchaseLease, HeavyShieldCommitResult> commit,
        Action<HeavyShieldPurchaseLease, string> holdUnknown)
    {
        if (!Armed || Latched || CancellationRequested || !Owns(native.Payer, native.Payable, shopLife)
            || native.CompletingPayable != Payable || !native.Completed) return Outcome.Ignored;
        if (!campaignValid || !HasNativePaymentReceipt(in native))
        {
            LatchUnknown("native-completed-receipt-mismatch", holdUnknown);
            return Outcome.Unknown;
        }
        // Latch before any identity/pool callback. Reentrant and repeated native callbacks cannot replay it.
        Armed = false; Latched = true; _completedFrame = frame;
        HeavyShieldCommitResult result;
        try { result = commit != null ? commit(Lease) : HeavyShieldCommitResult.Unknown; }
        catch { result = HeavyShieldCommitResult.Unknown; }
        if (result == HeavyShieldCommitResult.Applied || result == HeavyShieldCommitResult.AlreadyApplied)
            return Outcome.Applied;
        // Actual 2.4 has no verified explicit Gem/Coin refund contract here. Do not add wallet currency.
        // A definite rejection and an uncertain commit remain distinct diagnostics, both retaining the receipt.
        LatchUnknown(result == HeavyShieldCommitResult.Rejected
            ? "commit-rejected-refund-unproven" : "commit-outcome-unknown", holdUnknown);
        return result == HeavyShieldCommitResult.Rejected ? Outcome.RejectedUnrefunded : Outcome.Unknown;
    }

    internal bool RequestCancellation(long payer, long payable, long shopLife, bool nativeCompleted)
    {
        if (!Armed || Latched || Unknown || nativeCompleted || !Owns(payer, payable, shopLife)) return false;
        CancellationRequested = true;
        return true;
    }

    internal bool ObserveUnpaidCancellation(bool nativeDropReturned, bool floatingEmpty,
        Func<HeavyShieldPurchaseLease, bool> confirm,
        Action<HeavyShieldPurchaseLease, string> holdUnknown)
    {
        if (!CancellationRequested || CancellationConfirmed || Latched || !nativeDropReturned || !floatingEmpty)
            return false;
        // The native DropFromIndicator loop and list Clear have both returned before releasing the lease.
        CancellationConfirmed = true;
        bool accepted;
        try { accepted = confirm != null && confirm(Lease); }
        catch { accepted = false; }
        if (!accepted) { LatchUnknown("unpaid-cancellation-confirmation-unknown", holdUnknown); return false; }
        Armed = false;
        return true;
    }

    internal void LatchUnknown(string reason, Action<HeavyShieldPurchaseLease, string> holdUnknown)
    {
        if (Unknown) return;
        Armed = false; Latched = true; Unknown = true;
        try { holdUnknown?.Invoke(Lease, reason); } catch { /* local latch still forbids all replay */ }
    }

    // Phase/UI changes only after a later driver tick observes the native floating-currency cleanup.
    internal bool ObserveSettled(int frame, bool floatingEmpty, bool completingCleared, bool nativeStateSettled)
    {
        if (!Busy || Unknown || frame <= _completedFrame || !floatingEmpty || !completingCleared
            || !nativeStateSettled || (CancellationRequested && !CancellationConfirmed)
            || (Armed && !CancellationConfirmed)) return false;
        Lease = default; Payer = 0; Payable = 0; ShopLife = 0;
        Armed = false; Latched = false; CancellationRequested = false; CancellationConfirmed = false;
        return true;
    }
}
