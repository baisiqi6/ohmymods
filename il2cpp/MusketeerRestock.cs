using System;
using UnityEngine;

namespace KingdomEnhancedMod;

// Dedicated automatic procurement adapter. Manual player payment remains in MusketeerShop.
internal static partial class MusketeerShop
{
    internal enum AutoPurchaseResult { Rejected, Purchased, PaidUncertain }

    internal static bool TryGetAutoRestockTarget(out Payable target)
    {
        target = _payable;
        return target != null && CanAutoRestock(target, out _);
    }

    internal static bool CanAutoRestock(Payable target, out string reason)
    {
        reason = "火铳铺未就绪";
        try
        {
            if (!GreekBankScope.IsActive || NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth
                || Time.timeScale <= 0f || !CanPurchase()) return false;
            if (target == null || _payable == null || target.Pointer != _payable.Pointer
                || !target.enabled || target.gameObject == null || !target.gameObject.activeInHierarchy
                || !MusketeerAccess.InWorld(target) || target.Price != Price
                || target.Currency != CurrencyType.Coins || target.forceBlockPayment) return false;
            if (!MusketeerIdentity.TryGetRestockCounts(out _, out _)) return false;
            reason = "玩家支付占用";
            // An armed manual receipt can outlive native cancellation. Occupancy belongs
            // to the native selecting/completing players, never the stale receipt latch.
            if (target.selectedByP1 || target.selectedByP2
                || target.PlayerSelecting != null || target.interactingPlayer != null
                || Pending(_kingdom.playerOne) || Pending(_kingdom.playerTwo)
                || ManualCoinsPending(_kingdom.playerOne) || ManualCoinsPending(_kingdom.playerTwo)) return false;
            reason = "";
            return true;
        }
        catch { return false; }
    }

    private static bool ManualCoinsPending(Player player)
        => player != null && Payment.IsArmed(player.Pointer.ToInt64())
            && player._floatingCurrency != null && player._floatingCurrency.Count > 0;

    // Dedicated bank transaction: native player receipt/refund logic above remains untouched.
    // There is no yield between preflight, one debit, and the very same real gun creation.
    internal static AutoPurchaseResult PurchaseForAutoRestock(Payable target, Banker banker,
        Action onDebited, out string reason)
    {
        reason = "火铳铺未就绪";
        if (!CanAutoRestock(target, out reason)) return AutoPurchaseResult.Rejected;
        if (!PatchEconomy_Banker.TrySpendForAutoRestock(banker, Price * 2))
        { reason = "金币不足"; return AutoPurchaseResult.Rejected; }
        try
        {
            onDebited(); // relinquish the incoming/budget reservation before any native spawn
            if (TryCreateGun(out reason))
            {
                // Native PerformPay records its base price, not the bank procurement surcharge.
                // A diagnostic/statistics fault after a proven shipment can never retry the sale.
                try { Managers.Inst.stats.Increment(Stat.CoinsSpent, Price); }
                catch (Exception error) { try { Log("auto shipment completed; spending stat failed: " + error.GetType().Name); } catch { } }
                _status = "火枪已上架，等待居民领取";
                return AutoPurchaseResult.Purchased;
            }
        }
        catch (Exception error) { reason = error.GetType().Name; }
        // Never use the manual four-coin refund for a bank purchase. Caller records a
        // world-scoped target fault and paid departure, never another debit of this order.
        _status = "自动采购已扣款，出货未确认；本世界停止该店自动采购";
        return AutoPurchaseResult.PaidUncertain;
    }

}
