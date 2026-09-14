// Tests.cs: behavior regression for il2cpp/PatchPlayer_HoldPurchase.cs (linked unmodified).
// Run with: dotnet run --project tests/hold-purchase (see WORKER.md for the full command).
// The suite drives the production Prefix/Postfix/Finalizer around a faithful copy of the
// vanilla 2.1.0 UpdatePayState state machine, so every assertion is about observable
// pay/coin behavior, never about production internals.
using System;
using System.Collections.Generic;
using System.Text;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    internal static int Main()
    {
        Run("off_matches_unpatched_vanilla_trace", OffMatchesUnpatchedTrace);
        Run("hold_continues_same_shop_without_release", ContinuesSameShop);
        Run("first_coin_and_holding_threshold_stay_vanilla", FirstCoinVanilla);
        Run("acceleration_is_quarter_interval_with_floor", AccelerationQuarterAndFloor);
        Run("release_ends_hold_and_needs_new_press", ReleaseEndsHold);
        Run("new_shop_needs_a_new_press", NewShopNeedsNewPress);
        Run("empty_wallet_stops_continuation_without_drop", EmptyWalletStops);
        Run("full_shelf_stops_continuation_without_drop", FullShelfStops);
        Run("pause_and_menu_stall_drop_the_session", PauseAndStallDrop);
        Run("scope_off_and_unknown_layer_are_inert", ScopeInert);
        Run("disabled_toggle_is_inert_and_keeps_vanilla_interval", DisabledToggleInert);
        Run("remote_or_tunneled_player_is_never_touched", RemotePlayerUntouched);
        Run("two_players_have_independent_sessions", TwoPlayersIsolated);
        Run("restore_failure_keeps_receipt_then_tick_repairs", RestoreFailureReceipt);
        Run("whitelist_excludes_change_shops_includes_bread", Whitelist);
        Run("native_throw_restores_interval_and_drops_session", NativeThrowSafe);
        Run("walking_out_of_range_ends_the_session", WalkAwayEnds);
        Run("starting_to_run_never_drops_a_coin", RunningPlayerNeverDrops);
        Run("client_purchase_waits_for_reply_then_continues", ClientWaitsForReply);
        Run("client_denial_stops_the_hold_and_refunds", ClientDenialStops);
        Run("foreign_perform_pay_is_not_our_receipt", ForeignReceiptIgnored);
        Run("banker_purchase_never_arms_our_hold", BankerPurchaseIsInert);
        Run("restore_never_gives_up_while_alive", RestoreNeverGivesUp);
        Run("panel_open_cancels_the_hold", PanelOpenCancels);
        Run("pending_restore_survives_release_and_repress_without_compounding", PendingAcrossRepress);

        Console.WriteLine();
        Console.WriteLine(_failed == 0
            ? "ALL PASS (" + _passed + " scenarios)"
            : _failed + " FAILED, " + _passed + " passed");
        return _failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------- scenarios

    private static void OffMatchesUnpatchedTrace()
    {
        Reset();
        Env offEnv = new Env();
        offEnv.P1.timeBetweenCoins = 0.4f;
        ModConfig.HoldPurchaseEnabled.Value = false;
        string patchedTrace = Script(offEnv, true, out int patchedPurchases);

        Reset();
        Env referenceEnv = new Env();
        string referenceTrace = Script(referenceEnv, false, out int referencePurchases);

        Eq(referenceTrace, patchedTrace, "disabled toggle must reproduce the vanilla trace");
        Eq(referencePurchases, patchedPurchases, "disabled toggle purchase count");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "disabled toggle keeps no session");
        Eq(0, offEnv.P1.wallet.Dropped, "disabled toggle drops nothing");
        Eq(0.4f, offEnv.P1.timeBetweenCoins, "disabled toggle leaves the interval alone");
    }

    private static void ContinuesSameShop()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;

        int injections = 0;
        for (int i = 0; i < 200; i++)          // 4 seconds of one continuous hold
            if (Env.Frame(p, true, i == 0)) injections++;

        True(p.Purchases >= 3, "one hold should keep buying, got " + p.Purchases);
        Eq(p.Purchases, env.ShopA.TransactionCompleteCalls, "all purchases at the same shop");
        Eq(0, env.ShopB.TransactionCompleteCalls, "the other shop is never paid");
        Eq(p.HoldingEntered - 1, injections, "every purchase after the first uses one synthesized press");
        Eq(0, p.GroundDrops, "no coin may fall to the ground");
        Eq(100 - p.Purchases * env.ShopA.Price - p._floatingCurrency.Count, p.wallet.Coins, "coin conservation");
        Eq(p.Purchases, env.ShopA.Stock, "shop stock grows by exactly one per purchase");
        Eq(p.Purchases + (p._payState == Native.Transaction || p._payState == Native.Completed ? 1 : 0), env.ShopA.TransactionStartedCalls, "one transaction per purchase");
        Eq(4, env.ShopA.Price, "price is never modified");
        Eq(0.4f, p.timeBetweenCoins, "interval restored at every frame boundary");
    }

    private static void FirstCoinVanilla()
    {
        Reset();
        Env env = new Env(priceA: 6, limitA: 20);
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;
        p.RecordReads = true;

        for (int i = 0; i < 12; i++) Env.Frame(p, true, i == 0);
        Eq(Native.Holding, p._payState, "still Holding before the vanilla keyDownThreshold");
        for (int i = 12; i < 15; i++) Env.Frame(p, true, false);
        Eq(Native.Transaction, p._payState, "Transaction starts on the vanilla threshold");

        float firstLaunch = p.CoinLaunchTimes[0];
        Between(0.26f, 0.30f, firstLaunch, "first coin launches only after the vanilla threshold");
        Eq(0.4f, p.timeBetweenCoins, "interval untouched before the 0.6s hold");
    }

    private static void AccelerationQuarterAndFloor()
    {
        Reset();
        Env env = new Env(priceA: 6, limitA: 20);
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;
        p.timeBetweenCoins = 0.2f;
        p.RecordReads = true;
        for (int i = 0; i < 90; i++) Env.Frame(p, true, i == 0);
        HasRead(p, 0.05f, "0.2s accelerates to 0.05s (one quarter)");
        Eq(0.2f, p.timeBetweenCoins, "interval restored after each frame");

        Reset();
        Env floorEnv = new Env(priceA: 6, limitA: 20);
        Player floorPlayer = floorEnv.P1;
        Env.StandAt(floorPlayer, floorEnv.ShopA);
        floorPlayer.coins = 100;
        floorPlayer.timeBetweenCoins = 0.05f;
        for (int i = 0; i < 40; i++) Env.Frame(floorPlayer, true, i == 0);   // past the 0.6s hold
        floorPlayer.Reads.Clear();
        floorPlayer.RecordReads = true;
        for (int i = 40; i < 120; i++) Env.Frame(floorPlayer, true, false);
        HasRead(floorPlayer, 0.03f, "quarter of 0.05s clamps up to the 0.03s floor");
        NoRead(floorPlayer, 0.05f, "the floor keeps the accelerated read at 0.03s");

        Reset();
        Env tinyEnv = new Env(priceA: 6, limitA: 20);
        Player tiny = tinyEnv.P1;
        Env.StandAt(tiny, tinyEnv.ShopA);
        tiny.coins = 100;
        tiny.timeBetweenCoins = 0.02f;
        for (int i = 0; i < 40; i++) Env.Frame(tiny, true, i == 0);
        tiny.Reads.Clear();
        tiny.RecordReads = true;
        for (int i = 40; i < 120; i++) Env.Frame(tiny, true, false);
        Eq(0.02f, tiny.timeBetweenCoins, "already faster than the floor is never slowed down");
        NoRead(tiny, 0.03f, "no write happened for a 0.02s original");
    }

    private static void ReleaseEndsHold()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;

        int purchasesWhileHeld = 0;
        for (int i = 0; i < 300; i++)
        {
            Env.Frame(p, true, i == 0);
            if (p.Purchases >= 2 && p._payState == Native.None)
            {
                purchasesWhileHeld = p.Purchases;
                break;
            }
        }
        True(purchasesWhileHeld >= 2, "precondition: hold bought repeatedly");
        Eq(Native.None, p._payState, "release happens between native transactions");

        Env.Frame(p, false, false);                    // release
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "release drops the session");
        for (int i = 0; i < 300; i++) Env.Frame(p, false, false);
        Eq(purchasesWhileHeld, p.Purchases, "released key buys nothing more");
        Eq(0, p.GroundDrops, "release between transactions drops nothing");
        Eq(0.4f, p.timeBetweenCoins, "interval restored on release");

        // Re-holding without a fresh press must stay vanilla (no payKeyDown).
        p.RecordReads = true;
        for (int i = 0; i < 100; i++) Env.Frame(p, true, false);
        Eq(purchasesWhileHeld, p.Purchases, "holding without a new press buys nothing");
        NoRead(p, 0.1f, "no acceleration without a fresh press");
        for (int i = 0; i < 100; i++) Env.Frame(p, true, i == 0);
        True(p.Purchases > purchasesWhileHeld, "a fresh press resumes buying");
    }

    private static void NewShopNeedsNewPress()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20, priceB: 4, limitB: 20);
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;
        for (int i = 0; i < 400; i++) { Env.Frame(p, true, i == 0); if (p.Purchases >= 2 && p._payState == Native.None) break; }
        True(p.Purchases >= 2, "precondition: shopping at A continues");
        int purchasesAtA = env.ShopA.TransactionCompleteCalls;

        // Walk to shop B while still holding: the hold ends, B must not auto-buy.
        Env.StandAt(p, env.ShopB);
        p.RecordReads = true;
        int injections = 0;
        for (int i = 0; i < 150; i++)
            if (Env.Frame(p, true, false)) injections++;
        Eq(0, injections, "a new shop never inherits the previous hold");
        Eq(0, env.ShopB.TransactionCompleteCalls, "no purchase at the new shop without a press");
        Eq(purchasesAtA, env.ShopA.TransactionCompleteCalls, "shop A untouched after leaving");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "changing the selected shop drops the session");
        NoRead(p, 0.1f, "no acceleration at the new shop before a press");

        Env.Frame(p, true, true);                      // fresh press at B
        for (int i = 0; i < 120; i++) Env.Frame(p, true, false);
        True(env.ShopB.TransactionCompleteCalls >= 1, "fresh press buys at the new shop");
        Eq(env.ShopB.TransactionCompleteCalls, p.Purchases - purchasesAtA,
            "purchases after the move all belong to shop B");
        Eq(0.4f, p.timeBetweenCoins, "interval back to vanilla after the frame");
    }

    private static void EmptyWalletStops()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 4;                                  // exactly one purchase
        int injections = 0;
        for (int i = 0; i < 200; i++)
            if (Env.Frame(p, true, i == 0)) injections++;

        Eq(1, p.Purchases, "wallet cannot fund a second purchase");
        Eq(0, injections, "no press is synthesized without money");
        Eq(0, p.GroundDrops, "no coin drops from the aborted continuation");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "session ends when it cannot continue");
        Eq(0, p.wallet.Coins, "wallet drained exactly once");
    }

    private static void FullShelfStops()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 2);      // shelf holds two items
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;
        int injections = 0;
        for (int i = 0; i < 200; i++)
            if (Env.Frame(p, true, i == 0)) injections++;

        Eq(2, p.Purchases, "only the two affordable shelf slots are bought");
        Eq(1, injections, "continuation stops once the shelf is full");
        Eq(0, p.GroundDrops, "full shelf never turns into a ground drop");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "session ends on a full shelf");
        Eq(2, env.ShopA.Stock, "shelf is full");
    }

    private static void PauseAndStallDrop()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;
        for (int i = 0; i < 5; i++) Env.Frame(p, true, i == 0);
        Eq(Native.Holding, p._payState, "precondition: holding at the shop");
        Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount, "precondition: one session");

        Time.timeScale = 0f;
        Env.Frame(p, true, false);
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "pause drops the session");
        Time.timeScale = 1f;
        p.RecordReads = true;
        for (int i = 0; i < 200; i++) Env.Frame(p, true, false);
        Eq(1, p.Purchases, "after unpausing the native hold finishes one purchase");
        NoRead(p, 0.1f, "pause cancels acceleration for the rest of the hold");

        // Menu / load stall: frames stop, unscaled time keeps moving.
        Reset();
        Env stallEnv = new Env(priceA: 4, limitA: 20);
        Player stall = stallEnv.P1;
        Env.StandAt(stall, stallEnv.ShopA);
        stall.coins = 100;
        for (int i = 0; i < 5; i++) Env.Frame(stall, true, i == 0);
        Time.unscaledTime += 2f;                       // menu gap, no frames
        Env.Frame(stall, true, false);
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "menu stall drops the session");
    }

    private static void ScopeInert()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;
        for (int i = 0; i < 400; i++) { Env.Frame(p, true, i == 0); if (p.Purchases >= 2 && p._payState == Native.None) break; }
        True(p.Purchases >= 2, "precondition: continuation active");

        OptionalQoLScope.IsActive = false;
        Env.Frame(p, true, false);
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "leaving the world drops the session");
        p.RecordReads = true;
        int injections = 0;
        for (int i = 0; i < 150; i++)
            if (Env.Frame(p, true, false)) injections++;
        Eq(0, injections, "no synthetic press outside the world scope");
        NoRead(p, 0.1f, "no acceleration outside the world scope");
        Eq(0.4f, p.timeBetweenCoins, "interval stays vanilla outside the scope");
        OptionalQoLScope.IsActive = true;

        // Unknown layer / scene (IsCurrent false) behaves the same.
        Reset();
        Env layerEnv = new Env(priceA: 4, limitA: 20);
        Player layerPlayer = layerEnv.P1;
        Env.StandAt(layerPlayer, layerEnv.ShopA);
        layerPlayer.coins = 100;
        OptionalQoLScope.CurrentPredicate = _ => false;
        layerPlayer.RecordReads = true;
        int layerInjections = 0;
        for (int i = 0; i < 150; i++)
            if (Env.Frame(layerPlayer, true, i == 0)) layerInjections++;
        Eq(0, layerInjections, "an out of layer/scene player is never driven");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "no session for an out of layer player");
        NoRead(layerPlayer, 0.1f, "no acceleration for an out of layer player");
    }

    private static void DisabledToggleInert()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;
        for (int i = 0; i < 400; i++) { Env.Frame(p, true, i == 0); if (p.Purchases >= 2 && p._payState == Native.None) break; }
        int purchases = p.Purchases;
        True(purchases >= 2, "precondition: continuation active");

        ModConfig.HoldPurchaseEnabled.Value = false;
        PatchPlayer_HoldPurchase.Tick();
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "Tick clears sessions when disabled");
        Eq(0.4f, p.timeBetweenCoins, "interval restored when the toggle goes off");
        p.RecordReads = true;
        int injections = 0;
        for (int i = 0; i < 150; i++)
            if (Env.Frame(p, true, false)) injections++;
        Eq(0, injections, "disabled never synthesizes input");
        NoRead(p, 0.1f, "disabled never accelerates");

        ModConfig.HoldPurchaseEnabled = null;          // config not initialised yet
        Env.Frame(p, true, false);
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "missing config entry is inert");
        NoRead(p, 0.1f, "missing config entry never accelerates");
    }

    private static void RemotePlayerUntouched()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;
        p.hasLocalAuthority = false;
        p.RecordReads = true;
        int injections = 0;
        for (int i = 0; i < 150; i++)
            if (Env.Frame(p, true, i == 0)) injections++;
        Eq(0, injections, "remote player input is never synthesized");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "no session for a remote player");
        NoRead(p, 0.1f, "no acceleration for a remote player");
        Eq(1, p.Purchases, "remote player still runs its own vanilla purchase");

        Reset();
        Env tunnelEnv = new Env(priceA: 4, limitA: 20);
        Player tunnel = tunnelEnv.P1;
        Env.StandAt(tunnel, tunnelEnv.ShopA);
        tunnel.coins = 100;
        tunnel.hasLocalAuthority = true;
        tunnel.TunnelInput = true;
        tunnel.RecordReads = true;
        int tunnelInjections = 0;
        for (int i = 0; i < 150; i++)
            if (Env.Frame(tunnel, true, i == 0)) tunnelInjections++;
        Eq(0, tunnelInjections, "tunneled input is never taken over");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "no session while tunneled");
        NoRead(tunnel, 0.1f, "no acceleration while tunneled");
    }

    private static void TwoPlayersIsolated()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20, priceB: 4, limitB: 20);
        Player p1 = env.P1;
        Player p2 = env.P2;
        Env.StandAt(p1, env.ShopA);
        Env.StandAt(p2, env.ShopB);
        p1.coins = 100;
        p2.coins = 100;

        int p1Injections = 0;
        int p2Injections = 0;
        for (int i = 0; i < 120; i++)
        {
            if (Env.Frame(p1, true, i == 0)) p1Injections++;
            if (Env.Frame(p2, true, i == 0)) p2Injections++;
            Eq(2, PatchPlayer_HoldPurchase.ActiveSessionCount, "both players hold their own session");
        }

        True(p1.Purchases >= 2, "player 1 keeps buying, got " + p1.Purchases);
        True(p2.Purchases >= 2, "player 2 keeps buying, got " + p2.Purchases);
        Eq(p1.Purchases, env.ShopA.TransactionCompleteCalls, "player 1 only pays shop A");
        Eq(p2.Purchases, env.ShopB.TransactionCompleteCalls, "player 2 only pays shop B");
        Eq(0, env.ShopA.TransactionCompleteCalls - p1.Purchases, "no cross player purchases");
        Eq(p1.HoldingEntered - 1, p1Injections, "player 1 continuation count");
        Eq(p2.HoldingEntered - 1, p2Injections, "player 2 continuation count");
        Eq(0, p1.GroundDrops + p2.GroundDrops, "no ground drops with two players");

        // Releasing player 1 must not disturb player 2's hold.
        int p2Purchases = p2.Purchases;
        Env.Frame(p1, false, false);
        Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount, "player 2 keeps its session");
        for (int i = 0; i < 60; i++) Env.Frame(p2, true, false);
        True(p2.Purchases > p2Purchases, "player 2 keeps buying after player 1 released");
        Eq(p1.Purchases, env.ShopA.TransactionCompleteCalls, "player 1 stopped at release");
    }

    private static void RestoreFailureReceipt()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;

        int launches = 0;
        Action<CurrencyType> record = p.wallet.OnLaunch;
        p.wallet.OnLaunch = type =>
        {
            record(type);
            launches++;
            if (launches == 3) p.FailAllIntervalWrites = true; // both Postfix and Finalizer fail
        };

        bool sawFailure = false;
        for (int i = 0; i < 60 && !sawFailure; i++)
        {
            Env.Frame(p, true, i == 0);
            if (PatchPlayer_HoldPurchase.PendingRestoreCount > 0) sawFailure = true;
        }
        True(sawFailure, "the failing restore must keep a receipt");
        Eq(1, PatchPlayer_HoldPurchase.PendingRestoreCount, "one receipt queued");
        Eq(0.1f, p.timeBetweenCoins, "temporary value still in place until the retry");
        True(KingdomEnhancedPlugin.Instance.LogSource.Warnings.Count > 0, "failure is logged");

        p.FailAllIntervalWrites = false;
        PatchPlayer_HoldPurchase.Tick();
        Eq(0, PatchPlayer_HoldPurchase.PendingRestoreCount, "Tick retries and clears the receipt");
        Eq(0.4f, p.timeBetweenCoins, "original interval returned by the retry");

        for (int i = 0; i < 60; i++) Env.Frame(p, true, false);
        Eq(0.4f, p.timeBetweenCoins, "later frames keep restoring normally");
        Eq(0, p.GroundDrops, "restore failure never disturbed the pay flow");
    }

    private static void Whitelist()
    {
        Reset();
        Env changeEnv = new Env(priceA: 4, limitA: 20);
        Shop changeShop = changeEnv.AddShop("ShopChangeRuler", 30f, 4, 20, false);
        Player changePlayer = changeEnv.P1;
        Env.StandAt(changePlayer, changeShop);
        changePlayer.coins = 100;
        changePlayer.RecordReads = true;
        int changeInjections = 0;
        for (int i = 0; i < 200; i++)
            if (Env.Frame(changePlayer, true, i == 0)) changeInjections++;
        Eq(0, changeInjections, "change-ruler shop never continues");
        NoRead(changePlayer, 0.1f, "change-ruler shop is never accelerated");
        True(changeShop.TransactionCompleteCalls >= 1, "change-ruler shop still works natively");
        Eq(0, changeShop.TransactionCompleteCalls - changePlayer.Purchases, "vanilla count");

        Reset();
        Env itemEnv = new Env(priceA: 4, limitA: 20);
        Shop itemShop = itemEnv.AddShop("ChangeItemShop", 30f, 4, 20, false);
        Player itemPlayer = itemEnv.P1;
        Env.StandAt(itemPlayer, itemShop);
        itemPlayer.coins = 100;
        itemPlayer.RecordReads = true;
        int itemInjections = 0;
        for (int i = 0; i < 120; i++)
            if (Env.Frame(itemPlayer, true, i == 0)) itemInjections++;
        Eq(0, itemInjections, "change-item shop never continues");
        NoRead(itemPlayer, 0.1f, "change-item shop is never accelerated");

        Reset();
        Env breadEnv = new Env(priceA: 4, limitA: 20);
        Shop bread = breadEnv.AddShop("Bakery", 30f, 4, 20, true);
        Player breadPlayer = breadEnv.P1;
        Env.StandAt(breadPlayer, bread);
        breadPlayer.coins = 100;
        int breadInjections = 0;
        for (int i = 0; i < 200; i++)
            if (Env.Frame(breadPlayer, true, i == 0)) breadInjections++;
        True(breadPlayer.Purchases >= 2, "bread shop restocks repeatedly");
        Eq(breadPlayer.HoldingEntered - 1, breadInjections, "bread shop uses the synthesized press");
        Eq(0, breadPlayer.GroundDrops, "bread shop never drops coins");
    }

    private static void NativeThrowSafe()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;

        int launches = 0;
        Action<CurrencyType> record = p.wallet.OnLaunch;
        p.wallet.OnLaunch = type =>
        {
            record(type);
            // Strike on the third coin: that frame already had the accelerated interval written.
            if (++launches == 3) p.wallet.ThrowOnLaunch = true;
        };

        bool threw = false;
        for (int i = 0; i < 120 && !threw; i++)
        {
            try
            {
                Env.Frame(p, true, i == 0);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
        }
        True(threw, "native throw must propagate to the caller");
        True(launches >= 3, "the throw happened on an accelerated frame");
        Eq(0.4f, p.timeBetweenCoins, "Finalizer restored the interval after a native throw");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "Finalizer dropped the session");
        Eq(0, PatchPlayer_HoldPurchase.PendingRestoreCount, "no receipt left behind");
    }

    private static void WalkAwayEnds()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;
        for (int i = 0; i < 10; i++) Env.Frame(p, true, i == 0);
        Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount, "precondition: session active");

        Vector3 away = p.transform.position;
        away.x = 10f;                                  // no payable within 0.5 of the player
        p.transform.position = away;
        p.RecordReads = true;
        int purchases = p.Purchases;
        int injections = 0;
        for (int i = 0; i < 150; i++)
            if (Env.Frame(p, true, false)) injections++;
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "walking away drops the session");
        Eq(0, injections, "no press away from the shop");
        Eq(purchases, p.Purchases, "no purchase away from the shop");
        NoRead(p, 0.1f, "no acceleration after losing the shop");
    }

    private static void RunningPlayerNeverDrops()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;

        // Stop on the frame right after a completed purchase: that is exactly the frame
        // where the continuation press would be synthesized.
        for (int i = 0; i < 300; i++)
        {
            Env.Frame(p, true, i == 0);
            if (p.Purchases >= 1 && p._payState == Native.None) break;
        }
        True(p.Purchases >= 1 && p._payState == Native.None, "precondition: idle between purchases");
        int purchases = p.Purchases;

        // The player starts running while still holding the pay key: the native selection
        // block refuses the shop on this very frame, so the press must not be synthesized
        // (otherwise the native else branch would throw a coin on the ground).
        p.actionState = (int)PlayerAction.Run;
        bool injected = Env.Frame(p, true, false);
        Eq(false, injected, "no press while the player is running");
        Eq(0, p.GroundDrops, "running never turns into a ground drop");
        Eq(purchases, p.Purchases, "no purchase while running");

        for (int i = 0; i < 100; i++)
        {
            if (Env.Frame(p, true, false)) injected = true;
        }
        Eq(false, injected, "still no press while running");
        Eq(0, p.GroundDrops, "no coin dropped after running away");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "running ends the session");
    }

    /// <summary>
    /// Network client (no world auth): TransactionComplete only sets forceBlockPayment and the
    /// local pay state ends. The hold must survive the wait (native clears selectedPayable),
    /// stay silent until PerformPay arrives through the reply, then continue at the same shop.
    /// </summary>
    private static void ClientWaitsForReply()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        NetworkBigBoss.HasWorldAuth = false;
        Player p = env.P2;                                   // guest local player (playerId 1)
        Shop shop = env.ShopA;
        Env.StandAt(p, shop);
        p.coins = 100;
        p.RecordReads = true;
        Harness.WirePerformPayHook(shop);

        int injections = 0;
        for (int i = 0; i < 200 && !shop.PendingReply; i++)
            if (Env.Frame(p, true, i == 0)) injections++;
        True(shop.PendingReply, "client submitted the pay RPC");
        Eq(1, shop.TransactionCompleteCalls, "local insertion finished once");
        Eq(0, shop.PerformPayCalls, "no PerformPay before the reply");
        Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount, "hold survives the client wait");
        HasRead(p, 0.1f, "the client purchase is accelerated like the host");

        p.Reads.Clear();
        injections = 0;
        for (int i = 0; i < 40; i++)
        {
            if (Env.Frame(p, true, false)) injections++;
            Eq(null, p.selectedPayable, "native clears the selection while blocked");
            Eq(1, PatchPlayer_HoldPurchase.ActiveSessionCount, "same shop identity is kept");
        }
        Eq(0, injections, "no synthesized press while waiting");
        Eq(0, p.GroundDrops, "waiting never drops coins");
        NoRead(p, 0.1f, "no acceleration while waiting");

        // Reply code 1: forceBlockPayment cleared, PerformPay called, native re-selects the shop.
        shop.RecvApproved(p);
        Eq(1, shop.PerformPayCalls, "reply performs the payment");
        for (int i = 0; i < 80; i++)
            if (Env.Frame(p, true, false)) injections++;
        True(p.Purchases >= 2, "continuation resumes after the reply, got " + p.Purchases);
        Eq(p.HoldingEntered - 1, injections, "one synthesized press per continuation");
        Eq(0, p.GroundDrops, "no ground drops on the client");
        NetworkBigBoss.HasWorldAuth = true;
    }

    /// <summary>Reply code 2: forceBlockPayment cleared with a refund and never PerformPay.</summary>
    private static void ClientDenialStops()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        NetworkBigBoss.HasWorldAuth = false;
        Player p = env.P2;
        Shop shop = env.ShopA;
        Env.StandAt(p, shop);
        p.coins = 100;
        Harness.WirePerformPayHook(shop);

        for (int i = 0; i < 200 && !shop.PendingReply; i++) Env.Frame(p, true, i == 0);
        True(shop.PendingReply, "client submitted the pay RPC");
        int coinsAfterSpend = p.wallet.Coins;

        shop.RecvDenied(p.playerId);
        Eq(0, shop.PerformPayCalls, "denial never performs the payment");
        Eq(coinsAfterSpend + shop.Price, p.wallet.Coins, "denied purchase is refunded");

        int purchases = p.Purchases;
        int injections = 0;
        for (int i = 0; i < 150; i++)
            if (Env.Frame(p, true, false)) injections++;
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "denial ends the hold");
        Eq(0, injections, "a denial is never retried");
        Eq(purchases, p.Purchases, "no purchase after a denial");
        Eq(0, p.GroundDrops, "denial drops nothing");

        // A fresh press is allowed again afterwards.
        for (int i = 0; i < 60; i++) Env.Frame(p, true, i == 0);
        True(p.Purchases > purchases, "a fresh press may buy again after a denial");
        NetworkBigBoss.HasWorldAuth = true;
    }

    /// <summary>
    /// A PerformPay that belongs to another player at the same shop must never be taken as this
    /// hold's success receipt; the pending client wait ends instead of continuing on a false credit.
    /// </summary>
    private static void ForeignReceiptIgnored()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        NetworkBigBoss.HasWorldAuth = false;
        Player p = env.P2;
        Shop shop = env.ShopA;
        Env.StandAt(p, shop);
        p.coins = 100;
        Harness.WirePerformPayHook(shop);
        for (int i = 0; i < 200 && !shop.PendingReply; i++) Env.Frame(p, true, i == 0);
        True(shop.PendingReply, "client submitted the pay RPC");
        int purchases = p.Purchases;

        shop.RecvApproved(env.P1);                            // somebody else's transaction
        Eq(1, shop.PerformPayCalls, "foreign PerformPay happened on the same shop");
        int injections = 0;
        for (int i = 0; i < 150; i++)
            if (Env.Frame(p, true, false)) injections++;
        Eq(0, injections, "a foreign receipt never continues our hold");
        Eq(purchases, p.Purchases, "no extra purchase from a foreign receipt");
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "hold ends instead of waiting forever");
        Eq(0, p.GroundDrops, "no ground drops");
        NetworkBigBoss.HasWorldAuth = true;
    }

    /// <summary>
    /// Banker/restock style purchase at the same shop (outside the player input flow) must not
    /// start our hold's own buying; the player's purchase still follows the vanilla threshold.
    /// </summary>
    private static void BankerPurchaseIsInert()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        Player p = env.P1;
        Shop shop = env.ShopA;
        Env.StandAt(p, shop);
        p.coins = 100;
        Harness.WirePerformPayHook(shop);

        for (int i = 0; i < 5; i++) Env.Frame(p, true, i == 0);
        Eq(Native.Holding, p._payState, "precondition: holding, no purchase yet");

        shop.ExternalPurchase(p);                             // banker style, same shop
        Eq(1, shop.PerformPayCalls, "banker purchase performed pay");
        Eq(false, shop.PendingReply, "host path never waits for a reply");
        int injections = 0;
        for (int i = 5; i < 15; i++)
            if (Env.Frame(p, true, false)) injections++;
        Eq(0, injections, "no press before our own purchase completes");
        Eq(0, p.Purchases, "the banker purchase is not credited to the player");

        for (int i = 15; i < 200; i++)
            if (Env.Frame(p, true, false)) injections++;
        True(p.Purchases >= 1, "our own purchase still happens");
        Eq(p.HoldingEntered - 1, injections, "continuation count stays consistent");
        Eq(0, p.GroundDrops, "no ground drops");
    }

    /// <summary>
    /// A failing restore must never drop the receipt for a live object: it keeps retrying with
    /// backoff past the old give up count and only clears once the field is really returned.
    /// </summary>
    private static void RestoreNeverGivesUp()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;
        for (int i = 0; i < 40; i++) Env.Frame(p, true, i == 0);   // past the 0.6s hold

        // Every frame: write succeeds in the prefix, the restore in the postfix throws.
        int launches = 0;
        Action<CurrencyType> record = p.wallet.OnLaunch;
        p.wallet.OnLaunch = type =>
        {
            record(type);
            launches++;
            p.FailAllIntervalWrites = true;
        };
        for (int i = 0; i < 100 && PatchPlayer_HoldPurchase.PendingRestoreCount == 0; i++)
            Env.Frame(p, true, false);
        True(PatchPlayer_HoldPurchase.PendingRestoreCount >= 1, "failed restores keep a receipt");

        int retries = 0;
        while (retries < 14 && PatchPlayer_HoldPurchase.PendingRestoreCount > 0)
        {
            Time.unscaledTime += 10f;                          // defeat the backoff
            PatchPlayer_HoldPurchase.Tick();
            retries++;
        }
        True(retries > 8, "keeps retrying past the old 8 attempt give up, got " + retries);
        True(PatchPlayer_HoldPurchase.PendingRestoreCount >= 1, "receipt owned until it succeeds");

        p.FailAllIntervalWrites = false;
        Time.unscaledTime += 10f;
        PatchPlayer_HoldPurchase.Tick();
        Eq(0, PatchPlayer_HoldPurchase.PendingRestoreCount, "receipt cleared only after write back");
        Eq(0.4f, p.timeBetweenCoins, "the true original interval is returned");
    }

    /// <summary>Opening the settings panel must cancel the hold and keep it cancelled.</summary>
    private static void PanelOpenCancels()
    {
        Reset();
        Env env = new Env(priceA: 4, limitA: 20);
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;
        for (int i = 0; i < 400; i++)
        {
            Env.Frame(p, true, i == 0);
            if (p.Purchases >= 2 && p._payState == Native.None) break;
        }
        True(p.Purchases >= 2 && p._payState == Native.None, "precondition: idle between purchases");

        ModPanel.IsShown = true;
        Env.Frame(p, true, false);
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "the open panel drops the session");
        PatchPlayer_HoldPurchase.Tick();
        Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "Tick keeps it dropped");
        Eq(0.4f, p.timeBetweenCoins, "interval returned while the panel is open");

        p.RecordReads = true;
        int purchases = p.Purchases;
        int injections = 0;
        for (int i = 0; i < 150; i++)
            if (Env.Frame(p, true, false)) injections++;
        Eq(0, injections, "no press while the panel is open");
        Eq(purchases, p.Purchases, "no purchase while the panel is open");
        NoRead(p, 0.1f, "no acceleration while the panel is open");

        ModPanel.IsShown = false;
        for (int i = 0; i < 60; i++) Env.Frame(p, true, false);
        Eq(purchases, p.Purchases, "reopening does not resume without a fresh press");
        Env.Frame(p, true, true);
        for (int i = 0; i < 60; i++) Env.Frame(p, true, false);
        True(p.Purchases > purchases, "a fresh press works again after the panel closes");
    }

    private static void PendingAcrossRepress()
    {
        Reset();
        Env env = new Env(priceA: 20, limitA: 20);
        Player p = env.P1;
        p.coins = 200;
        int launches = 0;
        Action<CurrencyType> record = p.wallet.OnLaunch;
        p.wallet.OnLaunch = type => { record(type); if (++launches == 3) p.FailAllIntervalWrites = true; };
        for (int i = 0; i < 100 && PatchPlayer_HoldPurchase.PendingRestoreCount == 0; i++) Env.Frame(p, true, i == 0);
        True(PatchPlayer_HoldPurchase.PendingRestoreCount > 0, "old receipt exists");
        Env.Frame(p, false, false);
        // Reads still show our old applied interval. Permit new borrowing writes,
        // but fail returning the original until the explicit recovery below.
        p.FailAllIntervalWrites = false;
        p.FailOriginalRestore = true;
        p.RecordReads = true;
        for (int i = 0; i < 120; i++) Env.Frame(p, true, i == 0);
        NoRead(p, 0.03f, "new session cannot compound the old applied value");
        Eq(0.1f, p.timeBetweenCoins, "old owner retains its applied value");
        p.FailOriginalRestore = false;
        Time.unscaledTime += 10f;
        PatchPlayer_HoldPurchase.Tick();
        Eq(0.4f, p.timeBetweenCoins, "true original recovered after repress");
    }

    // ------------------------------------------------------------- helpers
    /// <summary>Scripted patched/vanilla comparison run: press, hold, release.</summary>
    private static string Script(Env env, bool patched, out int purchases)
    {
        Player p = env.P1;
        Env.StandAt(p, env.ShopA);
        p.coins = 100;
        var trace = new StringBuilder();
        for (int i = 0; i < 80; i++)
        {
            bool payKey = i < 60;
            bool payKeyDown = i == 0;
            bool injected = false;
            if (patched)
            {
                injected = Harness.Frame(p, payKey, payKeyDown, false, 0.02f);
                Eq(0, PatchPlayer_HoldPurchase.ActiveSessionCount, "disabled run keeps no session");
            }
            else
            {
                Harness.NativeFrame(p, payKey, payKeyDown, 0.02f);
            }
            trace.Append(i).Append(':').Append(p._payState).Append(':')
                .Append(p.wallet.Coins).Append(':').Append(p.Purchases).Append(':')
                .Append(p.GroundDrops).Append(':').Append(injected ? 'I' : '-').Append(';');
        }
        purchases = p.Purchases;
        trace.Append("launches=");
        foreach (float t in p.CoinLaunchTimes) trace.Append(t.ToString("0.00")).Append(',');
        return trace.ToString();
    }

    private static void HasRead(Player player, float expected, string what)
    {
        foreach (float read in player.Reads)
            if (Math.Abs(read - expected) < 0.0005f) return;
        throw new Exception(what + ": no read of " + expected + " observed");
    }

    private static void NoRead(Player player, float forbidden, string what)
    {
        foreach (float read in player.Reads)
            if (Math.Abs(read - forbidden) < 0.0005f)
                throw new Exception(what + ": forbidden read of " + forbidden + " observed");
    }

    private static void Reset()
    {
        Time.time = 0f;
        Time.deltaTime = 0f;
        Time.unscaledTime = 0f;
        Time.timeScale = 1f;
        OptionalQoLScope.IsActive = true;
        OptionalQoLScope.CurrentPredicate = _ => true;
        ModConfig.HoldPurchaseEnabled = new ConfigEntry<bool> { Value = true };
        ModPanel.IsShown = false;
        NetworkBigBoss.HasWorldAuth = true;
        PatchPlayer_HoldPurchase.ResetAll();
        PatchPlayer_HoldPurchase.Tick();
        PatchPlayer_HoldPurchase.ResetAll();
        PatchPlayer_HoldPurchase.Tick();
    }

    private static void Run(string name, Action body)
    {
        try
        {
            body();
            _passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            _failed++;
            Console.WriteLine("FAIL " + name + " :: " + e.Message);
        }
    }

    private static void Eq<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(what + " (expected " + expected + ", got " + actual + ")");
    }

    private static void True(bool condition, string what)
    {
        if (!condition) throw new Exception(what);
    }

    private static void Between(float min, float max, float actual, string what)
    {
        if (actual < min || actual > max)
            throw new Exception(what + " (expected [" + min + "," + max + "], got " + actual + ")");
    }
}
