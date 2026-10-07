// RangeTests.cs: issue #173 regressions — the finite-hold space gate and the native
// retention/fallback entry contract (reviewer evidence: native-range-review.md,
// RetrievePayableIndices neighbor relation 0x7b4528-4538 / 0x7b4790-47a0, retention
// fast-path 0x7d9fbc-0x7da054, fallback 0x7da058-0x7da09c, GetClosestPayable 0x7b5788).
// All scenarios drive the production Prefix/Postfix around the vanilla sim: assertions
// stay on observable purchases/coins/sessions/log lines.
using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

internal static partial class Program
{
    internal static void RangeScenarios()
    {
        Run("neighbor_range_standing_hold_buys_multiple_rounds", NeighborRangeStandingHold);
        Run("strict_boundary_margin_equal_limit_rejects", StrictBoundaryRejects);
        Run("boundary_just_inside_continues_just_outside_stops", BoundaryUlp);
        Run("far_nearest_center_still_ends_hold", FarNearestCenterEndsHold);
        Run("fast_retention_ignores_closer_shop_and_never_queries", FastRetentionNoQuery);
        Run("fallback_other_shop_never_synthesizes_or_switches", FallbackOtherRejects);
        Run("fallback_query_fault_fails_closed_without_injection", FallbackFaultFailsClosed);
        Run("client_wait_at_neighbor_range_survives_until_reply", ClientWaitNeighborRange);
        Run("client_wait_rival_selection_ends_only_when_native_switches", ClientWaitIgnoresCloserShop);
        Run("selected_null_flicker_at_neighbor_range_reselects_and_continues",
            NeighborRangeReselectFlicker);
        Run("negative_pay_distance_fails_closed_as_reach_invalid", NegativeRangeFailsClosed);
        Run("nonfinite_pay_point_fails_closed_observable", NonfinitePayPointFailsClosed);
        Run("pay_point_read_fault_classified_as_reach_fault", PayPointReadFaultClassified);
        Run("baker_stock_wait_works_at_neighbor_range", BakerWaitNeighborRange);
    }

    /// <summary>Reviewer counterexample shape: ShopBow prefab r=1, payPoint=.25, still player
    /// at x=1 (distance .75, no movement input). The old .5 gate dropped after receipts=1.</summary>
    private static void NeighborRangeStandingHold()
    {
        Reset();
        Env env = new Env(priceA: 2, limitA: 5);
        Shop shop = env.ShopA;
        shop.gameObject.Tag = "ShopBow";
        shop.PayPointX = 0.25f;
        Player p = env.P1;
        p.transform.position = new Vector3(1f, 0f, 0f);
        p.coins = 100;
        for (int i = 0; i < 2000; i++)
        {
            Env.Frame(p, true, i == 0);
            if (PatchPlayer_HoldPurchase.ActiveSessionCount == 0) break;
        }
        Eq(5, p.Purchases, "standing hold at distance .75 keeps buying until the shelf fills");
        Eq(5, shop.Stock, "every purchase landed in the shelf");
        Eq(90, p.coins, "exactly 5 * price 2 coins were spent");
        Eq(0, p.GroundDrops, "no coin may fall to the ground");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "full shelf ends the hold");
        List<string> infos = KingdomEnhancedPlugin.Instance.LogSource.Infos;
        True(HasInfo(infos, "bind: branch=tag price=2"), "bind line names the tag branch");
        True(HasInfo(infos, "tag=ShopBow"), "bind line names the matched tag");
        True(HasInfo(infos, "geo-bind: x=1 point=0.25 d=0.75 r=1 margin=-0.25"),
            "bind geometry line records roundtrip values");
        True(HasInfo(infos, "geo-first-receipt: x=1 point=0.25 d=0.75 r=1 margin=-0.25"),
            "first receipt geometry line records roundtrip values");
        True(HasInfo(infos, "drop: reason=stock-no-wait receipts=5"),
            "aggregate drop reports the five confirmed purchases");
    }

    /// <summary>d−r == 0.5 exactly must reject (strict less-than, no rewrites to d &lt; r+.5).</summary>
    private static void StrictBoundaryRejects()
    {
        Reset();
        Env env = new Env(priceA: 2, limitA: 20);
        Shop shop = env.ShopA;
        Player p = env.P1;
        Env.StandAt(p, shop);
        p.coins = 100;
        for (int i = 0; i < 400 && p.Purchases < 1; i++) Env.Frame(p, true, i == 0);
        Eq(1, p.Purchases, "precondition: one native purchase at distance 0");

        Vector3 edge = p.transform.position;                 // payPoint 0, r 1 -> d = 1.5
        edge.x = 1.5f;
        p.transform.position = edge;
        for (int i = 0; i < 200; i++) Env.Frame(p, true, false);
        Eq(1, p.Purchases, "d - r == 0.5 rejects the continuation");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "the hold ends at the boundary");
        Eq(0, p.GroundDrops, "no coin may fall at the boundary");
        True(HasInfo(KingdomEnhancedPlugin.Instance.LogSource.Infos, "drop: reason=out-of-reach"),
            "the drop line names the real space reason");
        True(HasInfo(KingdomEnhancedPlugin.Instance.LogSource.Infos, "margin=0.5"),
            "the rejection snapshot records the exact boundary margin");
    }

    /// <summary>One ULP inside the boundary continues; one ULP outside stops (subtraction semantics).</summary>
    private static void BoundaryUlp()
    {
        float inside = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(1.5f) - 1);
        float outside = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(1.5f) + 1);

        Reset();
        Env env = new Env(priceA: 2, limitA: 20);
        Shop shop = env.ShopA;
        Player p = env.P1;
        Env.StandAt(p, shop);
        p.coins = 100;
        for (int i = 0; i < 400 && p.Purchases < 1; i++) Env.Frame(p, true, i == 0);
        Eq(1, p.Purchases, "precondition: first native purchase");

        Vector3 spot = p.transform.position;
        spot.x = inside;
        p.transform.position = spot;
        for (int i = 0; i < 600 && p.Purchases < 2; i++) Env.Frame(p, true, false);
        Eq(2, p.Purchases, "one ULP inside d-r keeps the native continuation");
        Eq(0, p.GroundDrops, "inside-ULP continuation drops no coin");

        spot.x = outside;
        p.transform.position = spot;
        int purchases = p.Purchases;
        for (int i = 0; i < 200; i++) Env.Frame(p, true, false);
        Eq(purchases, p.Purchases, "one ULP outside d-r stops the continuation");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "outside-ULP ends the hold");
    }

    /// <summary>Walk far while the shop stays the native nearest center (Recurse 0x7b49e0 does
    /// not range-reject the nearest): the Mod finite-hold space bound must still end the hold.</summary>
    private static void FarNearestCenterEndsHold()
    {
        Reset();
        Env env = new Env(priceA: 2, limitA: 20);
        Shop shop = env.ShopA;
        Player p = env.P1;
        Env.StandAt(p, shop);
        p.coins = 100;
        for (int i = 0; i < 400 && p.Purchases < 1; i++) Env.Frame(p, true, i == 0);
        Eq(1, p.Purchases, "precondition: first native purchase");

        env.Managers.payables.NearestCenterAlwaysIncluded = true;
        Eq(shop, env.Managers.payables.GetClosestPayable(3f, 0.5f, p),
            "native semantics check: the far shop is still the returned nearest center");

        Vector3 far = p.transform.position;
        far.x = 3f;
        p.transform.position = far;
        for (int i = 0; i < 200; i++) Env.Frame(p, true, false);
        Eq(1, p.Purchases, "no remote continuation even when native would return the same shop");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "the finite-hold bound ends the session");
        Eq(0, p.GroundDrops, "no coin may fall while far away");
        True(HasInfo(KingdomEnhancedPlugin.Instance.LogSource.Infos, "drop: reason=out-of-reach"),
            "the space gate is the documented stopping reason");
    }

    /// <summary>Retention fast-path (d&lt;=.5 and d&lt;=r): the closest query must never run, and a
    /// newly selectable closer shop must not steal or stop the hold.</summary>
    private static void FastRetentionNoQuery()
    {
        Reset();
        Env env = new Env(priceA: 2, limitA: 20);
        Shop shop = env.ShopA;
        Player p = env.P1;
        p.transform.position = new Vector3(0.3f, 0f, 0f);
        p.coins = 100;
        Shop rival = env.AddShop("ShopBow", 0.35f, 2, 20, false);
        rival.Blocked = true;                               // selectable only later
        for (int i = 0; i < 400 && p.Purchases < 1; i++) Env.Frame(p, true, i == 0);
        Eq(1, p.Purchases, "precondition: holding the first shop at distance .3");
        Eq(shop, p.selectedPayable, "the first shop stays selected");

        env.Managers.payables.GetClosestCalls = 0;
        rival.Blocked = false;                              // now closer AND selectable
        for (int i = 0; i < 800 && p.Purchases < 3; i++) Env.Frame(p, true, false);
        True(p.Purchases >= 3, "fast retention keeps buying the selected shop, got " + p.Purchases);
        Eq(0, rival.TransactionCompleteCalls, "the closer shop is never paid");
        Eq(0, env.Managers.payables.GetClosestCalls,
            "no closest query while the retention fast-path holds");
        Eq(0, p.GroundDrops, "no coin may fall");
    }

    /// <summary>Fast-path fails and the native fallback returns a different selectable shop:
    /// no synthesis, no forced switch by the Mod; the native switch ends the hold as switched-shop.</summary>
    private static void FallbackOtherRejects()
    {
        Reset();
        Env env = new Env(priceA: 2, limitA: 20);
        Shop shop = env.ShopA;
        shop.gameObject.Tag = "ShopBow";
        shop.PayPointX = 0.25f;
        Player p = env.P1;
        p.transform.position = new Vector3(1f, 0f, 0f);
        p.coins = 100;
        for (int i = 0; i < 400 && p.Purchases < 1; i++) Env.Frame(p, true, i == 0);
        Eq(1, p.Purchases, "precondition: one purchase at neighbor range");

        Shop rival = env.AddShop("ShopBow", 0.55f, 2, 20, false);   // d=.2, closer
        for (int i = 0; i < 200; i++) Env.Frame(p, true, false);
        Eq(1, p.Purchases, "a different fallback shop never synthesizes a press");
        Eq(0, rival.TransactionCompleteCalls, "the rival is not paid by the hold");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "the native switch ends the hold");
        Eq(0, p.GroundDrops, "no coin may fall on the fallback mismatch");
        True(HasInfo(KingdomEnhancedPlugin.Instance.LogSource.Infos, "drop: reason=switched-shop"),
            "the drop line names the native switch");
        True(HasInfo(KingdomEnhancedPlugin.Instance.LogSource.Infos, "fb=other"),
            "the rejection snapshot records the fallback identity result");
    }

    /// <summary>The fallback query itself faults: fail closed, no injection, no forged receipt.</summary>
    private static void FallbackFaultFailsClosed()
    {
        Reset();
        Env env = new Env(priceA: 2, limitA: 20);
        Shop shop = env.ShopA;
        shop.gameObject.Tag = "ShopBow";
        shop.PayPointX = 0.25f;
        Player p = env.P1;
        p.transform.position = new Vector3(1f, 0f, 0f);
        p.coins = 100;
        for (int i = 0; i < 400 && p.Purchases < 1; i++) Env.Frame(p, true, i == 0);
        Eq(1, p.Purchases, "precondition: one purchase at neighbor range");

        env.Managers.payables.ThrowOnQuery = true;
        PrefixMustNotInject(p); // observed before the native query can throw
        bool nativeThrew = false;
        for (int i = 0; i < 200 && PatchPlayer_HoldPurchase.ActiveSessionCount > 0 && !nativeThrew; i++)
        {
            // The native sim walks the same faulting query in its own fallback; the frame
            // driver rethrows after the Finalizer, like the real game would.
            try
            {
                Env.Frame(p, true, false);
            }
            catch (InvalidOperationException)
            {
                nativeThrew = true;
            }
        }
        True(nativeThrew, "the separate native query fault also surfaces through the frame driver");
        Eq(1, p.Purchases, "no purchase is forged after the query fault");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "the session fails closed");
        Eq(0, p.GroundDrops, "no coin may fall on the query fault");
        True(HasInfo(KingdomEnhancedPlugin.Instance.LogSource.Infos, "fb=fault"),
            "this scenario records the Mod fallback query fault, before the separate native throw");
    }

    /// <summary>Client at neighbor range: the RPC wait keeps the same-shop identity (closest is
    /// not consulted; forceBlock excludes the shop from normal queries) until the reply lands.</summary>
    private static void ClientWaitNeighborRange()
    {
        Reset();
        Env env = new Env(priceA: 2, limitA: 20);
        NetworkBigBoss.HasWorldAuth = false;
        Shop shop = env.ShopA;
        shop.gameObject.Tag = "ShopBow";
        shop.PayPointX = 0.25f;
        Player p = env.P2;
        p.transform.position = new Vector3(1f, 0f, 0f);
        p.coins = 100;
        Harness.WirePerformPayHook(shop);
        for (int i = 0; i < 400 && !shop.PendingReply; i++) Env.Frame(p, true, i == 0);
        True(shop.PendingReply, "client submitted the pay RPC at neighbor range");

        for (int i = 0; i < 120; i++)
        {
            Env.Frame(p, true, false);
            Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount,
                "the neighbor-range wait keeps the same shop identity");
        }
        Eq(0, p.GroundDrops, "waiting at neighbor range drops no coin");

        shop.RecvApproved(p);
        for (int i = 0; i < 600 && p.Purchases < 2; i++) Env.Frame(p, true, false);
        True(p.Purchases >= 2, "the approved reply resumes continuation at neighbor range");
        Eq(0, p.GroundDrops, "no ground drops across the client wait");
    }

    /// <summary>A closer selectable shop during the RPC wait must not reject the wait through the
    /// closest query; after the reply the native reselection decides, never the Mod.</summary>
    private static void ClientWaitIgnoresCloserShop()
    {
        Reset();
        Env env = new Env(priceA: 2, limitA: 20);
        NetworkBigBoss.HasWorldAuth = false;
        Shop shop = env.ShopA;
        shop.gameObject.Tag = "ShopBow";
        shop.PayPointX = 0.25f;
        Player p = env.P2;
        p.transform.position = new Vector3(1f, 0f, 0f);
        p.coins = 100;
        Harness.WirePerformPayHook(shop);
        for (int i = 0; i < 400 && !shop.PendingReply; i++) Env.Frame(p, true, i == 0);
        True(shop.PendingReply, "client submitted the pay RPC");

        Shop rival = env.AddShop("ShopBow", 0.55f, 2, 20, false);   // closer and selectable
        for (int i = 0; i < 120 && PatchPlayer_HoldPurchase.ActiveSessionCount > 0; i++)
            Env.Frame(p, true, false);
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount,
            "the native reselection of the rival ends the hold (the Mod never queries)");
        Eq(0, p.GroundDrops, "the wait kills no coin");
        List<string> waitInfos = KingdomEnhancedPlugin.Instance.LogSource.Infos;
        True(HasInfo(waitInfos, "drop: reason=switched-shop"),
            "the end is the native switch, not a query-based rejection");
        True(!HasInfo(waitInfos, "drop: reason=receipt-lost"),
            "the receipt wait itself was never rejected");

        shop.RecvApproved(p);
        for (int i = 0; i < 200; i++) Env.Frame(p, true, false);
        Eq(1, p.Purchases, "only the approved purchase counts");
        Eq(0, rival.TransactionCompleteCalls, "the rival is never paid without a real press");
        Eq(0, p.GroundDrops, "no coin may fall");
    }

    /// <summary>Selected-null flicker at neighbor range: wait for the real native reselection
    /// instead of dropping coins or synthesizing early.</summary>
    private static void NeighborRangeReselectFlicker()
    {
        Reset();
        Env env = new Env(priceA: 2, limitA: 20);
        Shop shop = env.ShopA;
        shop.gameObject.Tag = "ShopBow";
        shop.PayPointX = 0.25f;
        Player p = env.P1;
        p.transform.position = new Vector3(1f, 0f, 0f);
        p.coins = 100;
        for (int i = 0; i < 400 && p.Purchases < 1; i++) Env.Frame(p, true, i == 0);
        Eq(1, p.Purchases, "precondition: one purchase at neighbor range");

        p.selectedPayable = null;                            // one-frame selection flicker
        for (int i = 0; i < 600 && p.Purchases < 2; i++) Env.Frame(p, true, false);
        Eq(2, p.Purchases, "the native reselection continues the hold");
        Eq(0, p.GroundDrops, "the flicker frame drops no coin");
        Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount, "the session survives the flicker");
        Env.Frame(p, false, false);
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "release still ends the hold");
    }

    /// <summary>Negative playerPayDistance fails closed as reach-invalid, never as out-of-reach.</summary>
    private static void NegativeRangeFailsClosed()
    {
        Reset();
        Env env = new Env(priceA: 2, limitA: 20);
        Shop shop = env.ShopA;
        Player p = env.P1;
        Env.StandAt(p, shop);
        p.coins = 100;
        for (int i = 0; i < 400 && p.Purchases < 1; i++) Env.Frame(p, true, i == 0);
        Eq(1, p.Purchases, "precondition: one native purchase");

        shop.playerPayDistance = -1f;
        for (int i = 0; i < 200; i++) Env.Frame(p, true, false);
        Eq(1, p.Purchases, "a negative r never continues the hold");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "the session fails closed");
        Eq(0, p.GroundDrops, "no coin may fall on the invalid range");
        List<string> infos = KingdomEnhancedPlugin.Instance.LogSource.Infos;
        True(HasInfo(infos, "drop: reason=reach-invalid"),
            "the invalid number is not disguised as out-of-reach");
        True(HasInfo(infos, "fault=r=-1"), "the snapshot names the invalid value");
    }

    /// <summary>Non-finite pay point fails closed as reach-invalid with an observable fault field.</summary>
    private static void NonfinitePayPointFailsClosed()
    {
        Reset();
        Env env = new Env(priceA: 2, limitA: 20);
        Shop shop = env.ShopA;
        Player p = env.P1;
        Env.StandAt(p, shop);
        p.coins = 100;
        for (int i = 0; i < 400 && p.Purchases < 1; i++) Env.Frame(p, true, i == 0);
        Eq(1, p.Purchases, "precondition: one native purchase");

        shop.PayPointX = float.NaN;
        for (int i = 0; i < 200; i++) Env.Frame(p, true, false);
        Eq(1, p.Purchases, "a NaN pay point never continues the hold");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "the session fails closed");
        Eq(0, p.GroundDrops, "no coin may fall on the invalid point");
        List<string> infos = KingdomEnhancedPlugin.Instance.LogSource.Infos;
        True(HasInfo(infos, "drop: reason=reach-invalid"), "NaN is reach-invalid, not out-of-reach");
        True(HasInfo(infos, "fault=point=NaN"), "the snapshot names the NaN source");
    }

    /// <summary>A wrapper read exception is classified as reach-fault with the exception type.</summary>
    private static void PayPointReadFaultClassified()
    {
        Reset();
        Env env = new Env(priceA: 2, limitA: 20);
        Shop shop = env.ShopA;
        Player p = env.P1;
        Env.StandAt(p, shop);
        p.coins = 100;
        for (int i = 0; i < 400 && p.Purchases < 1; i++) Env.Frame(p, true, i == 0);
        Eq(1, p.Purchases, "precondition: one native purchase");

        shop.ThrowOnPayPoint = true;
        PrefixMustNotInject(p); // observed before the native pay-point read can throw
        True(HasInfo(KingdomEnhancedPlugin.Instance.LogSource.Infos, "x=NaN point=NaN d=NaN r=NaN"),
            "unread geometry is unavailable, never default zero");
        bool nativeThrew = false;
        for (int i = 0; i < 200 && !nativeThrew; i++)
        {
            // The vanilla retention read of the same wrapper faults too; the driver
            // rethrows after the Finalizer, like the real game would.
            try
            {
                Env.Frame(p, true, false);
            }
            catch (InvalidOperationException)
            {
                nativeThrew = true;
            }
        }
        True(nativeThrew, "the wrapper fault surfaces through the native call");
        Eq(1, p.Purchases, "a read fault never continues the hold");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "the session fails closed");
        Eq(0, p.GroundDrops, "no coin may fall on the read fault");
        List<string> infos = KingdomEnhancedPlugin.Instance.LogSource.Infos;
        True(HasInfo(infos, "drop: reason=reach-fault"), "the read fault is its own reason");
        True(HasInfo(infos, "fault=InvalidOperationException"), "the snapshot names the exception type");
    }

    /// <summary>Baker stock-wait semantics are unchanged at neighbor range.</summary>
    private static void BakerWaitNeighborRange()
    {
        Reset();
        Env env = new Env(priceA: 2, limitA: 2);
        // Keep the default shops out of the native candidate set (d-r >= .5) so the wait
        // measures the baker itself, not a rival reselection.
        env.ShopA.PayPointX = 30f;
        Shop shop = env.AddShop("BreadShop", 0.25f, 2, 2, true);
        Player p = env.P1;
        p.transform.position = new Vector3(1f, 0f, 0f);
        p.coins = 100;
        for (int i = 0; i < 600 && p.Purchases < 2; i++) Env.Frame(p, true, i == 0);
        Eq(2, p.Purchases, "the baker shelf fills to its limit at neighbor range");
        Eq(2, shop.Stock, "shelf full");

        int injections = 0;
        for (int i = 0; i < 150; i++)
        {
            if (Env.Frame(p, true, false)) injections++;
            Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount,
                "the baker wait survives the full shelf at neighbor range");
        }
        Eq(0, injections, "the wait never synthesizes a press");
        Eq(0, p.GroundDrops, "the wait drops no coin");

        shop.Stock = 0;                                     // a beggar freed a slot
        int resumed = 0;
        for (int i = 0; i < 600 && p.Purchases < 3; i++)
            if (Env.Frame(p, true, false)) resumed++;
        Eq(1, resumed, "the resumed purchase uses exactly one synthesized press");
        Eq(3, p.Purchases, "the freed slot is bought at neighbor range");
        Eq(0, p.GroundDrops, "no coin may fall across the wait");
        True(HasInfo(KingdomEnhancedPlugin.Instance.LogSource.Infos, "wait-stock: cause=stock"),
            "the wait line still names the stock cause");
    }
}
