// SinglePurchaseTests.cs: issue #300 behavior regressions.
//
// Two contracts are covered here, both through the production Prefix/Postfix around the
// vanilla state machine:
//   * the 4-coin Musketeer PayableComponent is part of the existing non-Baker goods path
//     (accelerated cadence plus native re-buy), but only while the exact static
//     _payable/_owner/root identity is current;
//   * every other non-whitelisted Payable whose current integral price is >= 7 Coins gets
//     only the cadence acceleration for the current native transaction. It never gets a
//     synthesized press, a receipt, stock waiting or a second stage.
using System;
using KingdomEnhancedMod;
using UnityEngine;

internal static partial class Program
{
    private const float AcceleratedInterval = 0.1f;

    private static (Env Env, PayableComponent Shop, Player Player) GenericComponent(
        int price, CurrencyType currency = CurrencyType.Coins, string tag = "GenericShop")
    {
        Reset();
        var env = new Env();
        PayableComponent shop = env.AddComponentShop(tag, 40f, price, 20);
        shop.Currency = currency;
        Player p = env.P1;
        Env.StandAt(p, shop);
        p.coins = 100;
        p.RecordReads = true;
        return (env, shop, p);
    }

    private static (Env Env, Shop Shop, Player Player) GenericPayable(int price)
    {
        Reset();
        var env = new Env();
        Shop shop = env.AddShop("ShopChangeRuler", 40f, price, 20, false);
        Player p = env.P1;
        Env.StandAt(p, shop);
        p.coins = 100;
        p.RecordReads = true;
        return (env, shop, p);
    }

    private static bool HasAcceleratedRead(Player p)
    {
        foreach (float read in p.Reads)
            if (Math.Abs(read - AcceleratedInterval) < 0.0005f) return true;
        return false;
    }

    private static void HoldUntilAccelerated(Player p, int frames = 400)
    {
        for (int i = 0; i < frames; i++)
        {
            Env.Frame(p, true, i == 0);
            if (HasAcceleratedRead(p)) return;
        }
        throw new Exception("no accelerated interval read observed");
    }

    private static void NoAcceleratedReadsFrom(Player p, int start)
    {
        for (int i = start; i < p.Reads.Count; i++)
            if (Math.Abs(p.Reads[i] - AcceleratedInterval) < 0.0005f)
                throw new Exception("accelerated interval resumed at read " + i);
    }

    internal static void SinglePurchaseScenarios()
    {
        // --- Musketeer: exact identity, existing goods continuation -----------------
        Run("musketeer_4coin_exact_identity_rebuys_with_native_presses", () =>
        {
            Reset();
            var env = new Env();
            PayableComponent shop = env.AddMusketeer();
            Player p = env.P1;
            Env.StandAt(p, shop);
            p.coins = 100;
            p.RecordReads = true;
            int injections = 0;
            for (int i = 0; i < 500; i++)
                if (Env.Frame(p, true, i == 0)) injections++;
            True(p.Purchases >= 3, "one hold keeps buying at the musketeer, got " + p.Purchases);
            Eq(p.Purchases, shop.TransactionCompleteCalls, "every purchase is at the musketeer");
            Eq(p.HoldingEntered - 1, injections, "each later purchase uses one native press");
            Eq(0, p.GroundDrops, "no coin may fall to the ground");
            Eq(100 - p.Purchases * 4 - p._floatingCurrency.Count, p.wallet.Coins,
                "4-coin wallet conservation");
            True(HasAcceleratedRead(p), "musketeer cadence is accelerated");
            True(HasInfo(KingdomEnhancedPlugin.Instance.LogSource.Infos, "branch=musketeer"),
                "musketeer is classified as its own goods branch");
        });

        Run("musketeer_wrong_owner_from_first_press_is_vanilla_only", () =>
        {
            Reset();
            var env = new Env();
            PayableComponent shop = env.AddMusketeer();
            shop._owner = new Component { Pointer = Env.Ptr(0x991000) };
            Player p = env.P1;
            Env.StandAt(p, shop);
            p.coins = 100;
            p.RecordReads = true;
            Env.Hold(p, 220);
            Eq(1, p.Purchases, "wrong owner gets exactly the vanilla manual purchase");
            Eq(1, shop.Stock, "wrong owner cannot add stock");
            Eq(0, p.GroundDrops, "wrong owner drops nothing");
            NoRead(p, AcceleratedInterval, "wrong owner is never accelerated");
        });

        foreach (string change in new[] { "component-owner", "static-owner" })
        {
            Run("musketeer_owner_replaced_" + change + "_stops_without_extra_charge", () =>
            {
                Reset();
                var env = new Env();
                PayableComponent shop = env.AddMusketeer();
                Player p = env.P1;
                Env.StandAt(p, shop);
                p.coins = 100;
                p.RecordReads = true;
                FirstPurchase(p); // ensures the session had a real receipt
                int balance = p.coins;
                int reads = p.Reads.Count;
                if (change == "component-owner")
                    shop._owner = new Component { Pointer = Env.Ptr(0x992000) };
                else
                    MusketeerShop._owner = new MusketeerShopOwner { Pointer = Env.Ptr(0x993000) };
                Eq(0, Continue(p, 140), "replaced owner cannot resume the captured hold");
                Eq(1, shop.Stock, "only the original purchase remains");
                Eq(balance, p.coins, "replaced owner never debits again");
                NoAcceleratedReadsFrom(p, reads);
            });
        }

        Run("musketeer_client_approval_resumes_native_rebuy", () =>
        {
            Reset();
            var env = new Env();
            PayableComponent shop = env.AddMusketeer();
            NetworkBigBoss.HasWorldAuth = false;
            Player p = env.P2;
            Env.StandAt(p, shop);
            p.coins = 100;
            for (int i = 0; i < 300 && !shop.PendingReply; i++) Env.Frame(p, true, i == 0);
            True(shop.PendingReply, "native client payment is pending");
            shop.RecvApproved(p);
            Eq(1, shop.Stock, "approved RPC grants the gun once");
            True(Continue(p, 140) > 0, "approved receipt resumes the native hold");
            True(p.Purchases >= 2, "one approval is followed by real native purchases");
            Eq(0, p.GroundDrops, "approval path drops nothing");
        });

        Run("musketeer_client_denial_refunds_and_does_not_retry", () =>
        {
            Reset();
            var env = new Env();
            PayableComponent shop = env.AddMusketeer();
            NetworkBigBoss.HasWorldAuth = false;
            Player p = env.P2;
            Env.StandAt(p, shop);
            p.coins = 100;
            for (int i = 0; i < 300 && !shop.PendingReply; i++) Env.Frame(p, true, i == 0);
            True(shop.PendingReply, "native client payment is pending");
            int balance = p.coins;
            Eq(0, Continue(p, 80), "no synthesized press while the RPC is unanswered");
            Eq(balance, p.coins, "waiting costs nothing extra");
            shop.RecvDenied(p.playerId);
            Eq(100, p.coins, "native denial refunds the four coins");
            Eq(0, Continue(p, 140), "denied payment is never retried");
            Eq(0, shop.Stock, "denial never grants the gun");
            Eq(100, p.coins, "denial costs nothing extra");
        });

        // --- Musketeer rejected shipment: a void/refunded order must end the hold -----
        // NativeSim models the real OnPay rejection inside the PerformPay callback: the
        // production shop calls the rejection hop, then refunds, then the PerformPay
        // postfix runs. This is not native-game execution and not the complete OnPay
        // integration, but the notification point is statically ordered before both the
        // refund and the postfix in the real source.
        Run("musketeer_refunded_shipment_ends_hold_without_retry", () =>
        {
            Reset();
            var env = new Env();
            PayableComponent shop = env.AddMusketeer();
            Player p = env.P1;
            Env.StandAt(p, shop);
            p.coins = 100;
            p.RecordReads = true;
            shop.RefundRejectedShipment = true;
            int injections = 0;
            for (int i = 0; i < 500; i++)
                if (Env.Frame(p, true, i == 0)) injections++;
            Eq(1, shop.PerformPayCalls, "one rejected/refunded native order per real press");
            Eq(0, injections, "a refunded shipment never re-arms a synthetic press");
            Eq(0, shop.Stock, "the failed shipment grants no stock");
            Eq(100, p.coins, "the native refund restores exactly the four coins");
            Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount,
                "the rejected order leaves no armed session");
            Eq(0, p.GroundDrops, "the rejected order drops no coin");
        });

        Run("musketeer_rejected_order_does_not_block_a_new_real_press", () =>
        {
            Reset();
            var env = new Env();
            PayableComponent shop = env.AddMusketeer();
            Player p = env.P1;
            Env.StandAt(p, shop);
            p.coins = 100;
            shop.RefundRejectedShipment = true;
            Env.Hold(p, 500);
            Eq(1, shop.PerformPayCalls, "the first press is rejected and refunded once");
            shop.RefundRejectedShipment = false;
            Env.Frame(p, false, false);
            Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount,
                "the failure left no continuation to rearm");
            for (int i = 0; i < 300 && shop.PerformPayCalls < 2; i++)
                Env.Frame(p, true, i == 0);
            Eq(2, shop.PerformPayCalls, "a fresh real press can try the same shop again");
            Eq(1, shop.Stock, "the retried shipment succeeds exactly once");
        });

        // Boundary 1 (unrecorded/incomplete callback) is notified from OnPay before the
        // native CancelTransaction+DropFloatingCurrency. This drives the exact hold-side
        // contract; the native cancel itself belongs to the real OnPay and is not re-modeled.
        Run("musketeer_rejection_boundary_ends_bound_hold_without_mutation", () =>
        {
            Reset();
            var env = new Env();
            PayableComponent shop = env.AddMusketeer();
            Player p = env.P1;
            Env.StandAt(p, shop);
            p.coins = 100;
            Env.Frame(p, true, true);
            Env.Frame(p, true, false);
            Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount, "hold session is active and bound");
            int balance = p.coins;
            int stock = shop.Stock;
            int completions = shop.TransactionCompleteCalls;
            MusketeerShop.NotifyPaymentRejected(p);
            Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount,
                "incomplete/rejected callback ends the hold");
            Eq(balance, p.coins, "rejection notification changes no wallet amount");
            Eq(stock, shop.Stock, "rejection notification changes no stock");
            Eq(completions, shop.TransactionCompleteCalls,
                "rejection notification starts no transaction");
            Eq(0, p._floatingCurrency.Count, "no floating coin is manufactured");
        });

        Run("musketeer_rejection_ignores_other_player_and_target", () =>
        {
            Reset();
            var env = new Env();
            PayableComponent shop = env.AddMusketeer();
            Player p = env.P1;
            Env.StandAt(p, shop);
            p.coins = 100;
            Env.Frame(p, true, true);
            Env.Frame(p, true, false);
            Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount, "hold session is active and bound");

            MusketeerShop.NotifyPaymentRejected(env.P2);
            Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount,
                "another player's rejection cannot end this hold");

            PatchPlayer_HoldPurchase.OnMusketeerPaymentRejected(p, env.ShopB);
            Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount,
                "another payable cannot end this hold");

            int instance = shop.gameObject.Id;
            shop.gameObject.Id = instance + 1;
            PatchPlayer_HoldPurchase.OnMusketeerPaymentRejected(p, shop);
            Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount,
                "a reused pointer with another GameObject instance cannot end this hold");
            shop.gameObject.Id = instance;

            MusketeerShop.NotifyPaymentRejected(p);
            Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount,
                "the exact current player and payable do end the hold");
        });

        Run("musketeer_rejection_without_session_or_disabled_is_inert", () =>
        {
            Reset();
            var env = new Env();
            PayableComponent shop = env.AddMusketeer();
            Player p = env.P1;
            Env.StandAt(p, shop);
            p.coins = 100;

            MusketeerShop.NotifyPaymentRejected(p);
            Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount,
                "a rejection without a session creates nothing");
            Eq(100, p.coins, "a rejection without a session changes no wallet");
            PatchPlayer_HoldPurchase.OnMusketeerPaymentRejected(null, shop);
            PatchPlayer_HoldPurchase.OnMusketeerPaymentRejected(p, null);

            Env.Frame(p, true, true);
            Env.Frame(p, true, false);
            Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount, "hold session is active");
            ModConfig.HoldPurchaseEnabled.Value = false;
            MusketeerShop.NotifyPaymentRejected(p);
            Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount,
                "a disabled feature produces no rejection side effect");
            Eq(100, p.coins, "the disabled rejection changes no wallet");
        });

        // GLM r3: a very early long-frame payment may reach OnPay before the hold bound the
        // shop. The fallback is allowed only with the current player identity, the exact
        // completing payable and the exact current Musketeer shop identity.
        Run("musketeer_early_unbound_rejection_requires_exact_completing_payable", () =>
        {
            Reset();
            var env = new Env();
            PayableComponent shop = env.AddMusketeer();
            Player p = env.P1;
            Env.StandAt(p, shop);
            p.coins = 100;
            shop.Blocked = true; // native selects null, so the postfix cannot bind
            Env.Frame(p, true, true); // prefix creates the session before native selection
            Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount, "unbound hold session is active");

            p._completingPayable = null;
            MusketeerShop.NotifyPaymentRejected(p);
            Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount,
                "the fallback requires a completing payable");

            p._completingPayable = env.ShopB;
            MusketeerShop.NotifyPaymentRejected(p);
            Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount,
                "a different completing target cannot end the unbound hold");

            PayableComponent other = env.AddComponentShop("OtherShop", 60f, 4, 20);
            p._completingPayable = shop;
            MusketeerShop._payable = other;
            PatchPlayer_HoldPurchase.OnMusketeerPaymentRejected(p, shop);
            Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount,
                "a payable that is not the current Musketeer shop is refused");

            MusketeerShop._payable = shop;
            PatchPlayer_HoldPurchase.OnMusketeerPaymentRejected(p, shop);
            Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount,
                "the exact completing payable of the current shop ends the unbound hold");
        });

        // --- Other payables: one transaction only, no continuation ------------------
        foreach (int price in new[] { 7, 8 })
        {
            Run("generic_component_price" + price + "_accelerates_one_transaction_only", () =>
            {
                var (_, shop, p) = GenericComponent(price);
                int injections = 0;
                for (int i = 0; i < 400; i++)
                    if (Env.Frame(p, true, i == 0)) injections++;
                Eq(1, p.Purchases, "high-price generic gets exactly one native purchase");
                Eq(1, shop.Stock, "no synthetic second purchase");
                Eq(0, injections, "no payKeyDown is ever synthesized");
                Eq(0, p.GroundDrops, "no ground coin");
                True(HasAcceleratedRead(p), "the current transaction was accelerated");
                Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount,
                    "the completed single transaction leaves no hold session");
            });
        }

        Run("generic_plain_payable_price8_accelerates_one_transaction_only", () =>
        {
            var (_, shop, p) = GenericPayable(8);
            int injections = 0;
            for (int i = 0; i < 400; i++)
                if (Env.Frame(p, true, i == 0)) injections++;
            Eq(1, p.Purchases, "plain high-price payable gets exactly one purchase");
            Eq(1, shop.TransactionCompleteCalls, "no second native transaction");
            Eq(0, injections, "plain payable never gets a synthesized press");
            Eq(0, p.GroundDrops, "no ground coin");
            True(HasAcceleratedRead(p), "plain high-price payable is accelerated");
        });

        Run("generic_component_price6_is_not_accelerated", () =>
        {
            var (_, shop, p) = GenericComponent(6);
            Env.Hold(p, 320);
            Eq(1, p.Purchases, "price 6 buys natively once");
            Eq(1, shop.Stock, "price 6 has no synthetic purchase");
            NoRead(p, AcceleratedInterval, "price 6 is below the single-acceleration threshold");
        });

        Run("generic_component_non_coins_is_not_accelerated", () =>
        {
            var (_, _, p) = GenericComponent(8, CurrencyType.Crown);
            p.currencyDroppingEnabled = false;
            Env.Hold(p, 200);
            Eq(0, p.Purchases, "crown currency is not handled as coins");
            Eq(0, p.GroundDrops, "currency dropping disabled");
            NoRead(p, AcceleratedInterval, "non-coins is never accelerated");
        });

        // --- Wallet boundary: an exact full price and coins already in flight ------
        Run("generic_component_price8_exact_wallet_completes_with_acceleration", () =>
        {
            var (_, shop, p) = GenericComponent(8);
            p.coins = 8;
            Env.Hold(p, 320);
            Eq(1, p.Purchases, "exact wallet completes one purchase");
            Eq(1, shop.Stock, "native stock grows once per completed transaction");
            Eq(0, p.coins, "exact wallet spent exactly the price");
            Eq(0, p.GroundDrops, "exact wallet drops nothing");
            True(HasAcceleratedRead(p), "exact wallet still accelerates");
        });

        Run("generic_component_partial_coins_keep_accelerating", () =>
        {
            var (_, shop, p) = GenericComponent(8);
            p.coins = 8;
            bool partialSeen = false;
            for (int i = 0; i < 320; i++)
            {
                Env.Frame(p, true, i == 0);
                if (!partialSeen && p.wallet.Launched == 3)
                {
                    partialSeen = true;
                    True(p.coins < shop.Price, "remaining wallet is below the full price");
                }
            }
            True(partialSeen, "three coins were in flight");
            Eq(1, p.Purchases, "partial payment still completes the one legal transaction");
            Eq(0, p.GroundDrops, "partial payment drops nothing");
            True(HasAcceleratedRead(p), "partial payment still uses the accelerated cadence");
        });

        // --- Dynamic price/currency and object lifetime ----------------------------
        Run("generic_dynamic_currency_change_stops_acceleration_immediately", () =>
        {
            var (_, shop, p) = GenericComponent(8);
            HoldUntilAccelerated(p);
            int reads = p.Reads.Count;
            shop.Currency = CurrencyType.Crown;
            for (int i = 0; i < 120; i++) Env.Frame(p, true, false);
            NoAcceleratedReadsFrom(p, reads);
            Eq(0, p.Purchases, "changing to crown cancels the coin transaction");
            Eq(0, p.GroundDrops, "no ground coin after the currency change");
        });

        Run("generic_dynamic_price_drop_stops_acceleration_immediately", () =>
        {
            var (_, shop, p) = GenericComponent(8);
            HoldUntilAccelerated(p);
            int reads = p.Reads.Count;
            shop.Price = 6;
            for (int i = 0; i < 120; i++) Env.Frame(p, true, false);
            NoAcceleratedReadsFrom(p, reads);
            Eq(0, p.GroundDrops, "no ground coin after the price drop");
        });

        foreach (string change in new[] { "inactive", "scene", "foreign-layer", "instance" })
        {
            Run("generic_object_" + change + "_stops_acceleration", () =>
            {
                var (_, shop, p) = GenericComponent(8);
                HoldUntilAccelerated(p);
                int reads = p.Reads.Count;
                switch (change)
                {
                    case "inactive": shop.gameObject.SetActive(false); break;
                    case "scene": shop.gameObject.Scene.handle++; break;
                    case "foreign-layer": shop.transform.Parent = null; break;
                    case "instance": shop.Pointer = Env.Ptr(0x994000); break;
                }
                for (int i = 0; i < 120; i++) Env.Frame(p, true, false);
                NoAcceleratedReadsFrom(p, reads);
                True(p.Purchases <= 1, "at most the in-flight native transaction completes");
                Eq(0, p.GroundDrops, "no ground coin after the object change");
            });
        }

        Run("generic_pool_disable_reenable_cannot_inherit_single_acceleration", () =>
        {
            var (_, shop, p) = GenericComponent(8);
            HoldUntilAccelerated(p);
            int reads = p.Reads.Count;
            shop.gameObject.SetActive(false);
            PatchPlayer_HoldPurchase.Tick();
            shop.gameObject.SetActive(true);
            for (int i = 0; i < 120; i++) Env.Frame(p, true, false);
            NoAcceleratedReadsFrom(p, reads);
            True(p.Purchases <= 1, "reactivated object cannot start a second transaction");
            Eq(0, p.GroundDrops, "reactivation drops nothing");
        });

        // --- Release and menu teardown ---------------------------------------------
        Run("generic_release_stops_and_restores_interval", () =>
        {
            var (_, _, p) = GenericComponent(8);
            HoldUntilAccelerated(p);
            int reads = p.Reads.Count;
            Env.Frame(p, false, false);
            Eq(0.4f, p.timeBetweenCoins, "release restores the original interval");
            for (int i = 0; i < 120; i++) Env.Frame(p, false, false);
            NoAcceleratedReadsFrom(p, reads);
            Eq(0, p.GroundDrops, "release never drops a coin on the ground");
        });

        Run("generic_menu_open_stops_and_restores_interval", () =>
        {
            var (_, _, p) = GenericComponent(8);
            HoldUntilAccelerated(p);
            int reads = p.Reads.Count;
            ModPanel.IsShown = true;
            Env.Frame(p, true, false);
            Eq(0.4f, p.timeBetweenCoins, "menu restores the original interval");
            for (int i = 0; i < 80; i++) Env.Frame(p, true, false);
            NoAcceleratedReadsFrom(p, reads);
        });

        // --- Native throw still returns the owned interval -------------------------
        Run("generic_native_throw_restores_interval_and_receipt", () =>
        {
            var (_, _, p) = GenericComponent(8);
            int launches = 0;
            p.wallet.OnLaunch = type =>
            {
                if (++launches == 3) p.wallet.ThrowOnLaunch = true;
            };
            bool threw = false;
            for (int i = 0; i < 200 && !threw; i++)
            {
                try { Env.Frame(p, true, i == 0); }
                catch (InvalidOperationException) { threw = true; }
            }
            True(threw, "native throw was exercised");
            True(HasAcceleratedRead(p), "the throw happened after single acceleration started");
            Eq(0.4f, p.timeBetweenCoins, "native throw restores the original interval");
            Eq(0, PatchPlayer_HoldPurchase.PendingRestoreCount, "no owned receipt remains");
            Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "thrown session is dropped");
        });

        // --- Existing whitelist keeps its re-buy semantics at six coins ------------
        Run("whitelist_six_coin_forge_still_rebuys", () =>
        {
            Reset();
            var env = new Env(priceA: 6, limitA: 20);
            Shop forge = env.AddShop("ShopForge", 40f, 6, 20, false);
            Player p = env.P1;
            Env.StandAt(p, forge);
            p.coins = 100;
            int injections = 0;
            for (int i = 0; i < 500; i++)
                if (Env.Frame(p, true, i == 0)) injections++;
            True(p.Purchases >= 3, "whitelisted six-coin shop still re-buys, got " + p.Purchases);
            Eq(p.HoldingEntered - 1, injections, "whitelist keeps synthesized native presses");
            Eq(0, p.GroundDrops, "whitelist drops nothing");
        });
    }
}
