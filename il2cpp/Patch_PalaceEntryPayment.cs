using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Node 2 travel boundary, implemented by Root. TryStart only reports whether one request was
/// accepted; it never means the payer arrived anywhere.
/// </summary>
internal interface IPalaceIslandTravel
{
    bool CanStart(Game game, Kingdom kingdom, Player payer, PayableComponent payable, out string reason);
    bool TryDescribeForCharge(Game game, Kingdom kingdom, Player payer, PayableComponent payable,
        out PalaceDestinationDecision decision, out string reason);
    bool TryStart(Game game, Kingdom kingdom, Player payer, PayableComponent payable, out string reason);
    bool CanStartPaid(Game game, Kingdom kingdom, Player payer, PayableComponent payable, PalacePaidHandle paid, out string reason);
    bool TryStartPaid(Game game, Kingdom kingdom, Player payer, PayableComponent payable, PalacePaidHandle paid, out string reason);
}

/// <summary>
/// Node 2 receipt controller for the 10-coin palace entrance: pure managed, no MonoBehaviour,
/// no Harmony. It follows the native PayableComponent sequence (borrowed from the existing
/// HeroShop/MusketeerShop controllers: subscribe once, arm on TransactionStarted, demand the
/// exact receipt at Completed) but never consumes/refunds coins, never calls PerformPay /
/// TransactionComplete, never saves or switches scenes. Native callback ordering still needs a
/// private runtime observation; this file claims no such verification.
/// </summary>
internal sealed class PalaceEntryPayment
{
    internal const int Price = 10;
    private const float SettlementWindowSeconds = 10f;
    private const int DetailLogLimit = 24;

    private enum Phase { Idle, Armed, WaitingNative, Started, Blocked, Retired }

    // Strong references for this life plus pointer snapshots for identity checks.
    private PalaceVisibleEntryOwner _owner;
    private PayableComponent _payable;
    private IPalaceIslandTravel _travel;
    private PalaceEntryPayment _life;
    private Player _payer;
    private PalacePaidHandle _paid;
    private Il2CppSystem.Action<Player> _started; // exact delegate instance, kept for Retire removal
    private bool _subscribed;
    private readonly IntPtr _gamePointer, _kingdomPointer, _ownerPointer, _payablePointer;
    private readonly int _sceneHandle;

    private Phase _phase = Phase.Idle;
    private string _status = "idle";
    private string _waiting = "";
    private string _travelReason = "";
    private bool _consumed;
    private bool _settledObserved;
    private bool _duplicateOnPayLogged;
    private long _payerPointer;
    private IntPtr _walletPointer;
    private int _walletObserved = -1;
    private int _startedFrame, _callbackFrame = -1;
    private float _deadline;
    private int _requests, _detailLogs;

    internal PalaceEntryPayment(PalaceVisibleEntryOwner owner, PayableComponent payable, IPalaceIslandTravel travel)
    {
        _owner = owner;
        _payable = payable;
        _travel = travel;
        _life = this; // strong self-pin while the native delegate references this life
        try
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (payable == null) throw new ArgumentNullException(nameof(payable));
            Managers managers = Managers.Inst;
            _gamePointer = managers != null && managers.game != null ? managers.game.Pointer : IntPtr.Zero;
            _kingdomPointer = managers != null && managers.kingdom != null ? managers.kingdom.Pointer : IntPtr.Zero;
            _ownerPointer = owner.Pointer;
            _payablePointer = payable.Pointer;
            _sceneHandle = owner.gameObject.scene.handle;
            _started = (Il2CppSystem.Action<Player>)OnTransactionStarted;
            payable.add_OnTransactionStartedCallback(_started);
            _subscribed = true;
            PalaceVisibleEntry.Log("payment controller subscribed; price=" + Price
                + " currency=" + payable.Currency + " game=" + _gamePointer.ToInt64()
                + " kingdom=" + _kingdomPointer.ToInt64() + " owner=" + _ownerPointer.ToInt64()
                + " payable=" + _payablePointer.ToInt64() + " scene=" + _sceneHandle
                + "; no travel before observed native settlement");
        }
        catch (Exception e)
        {
            _phase = Phase.Blocked;
            _status = "blocked: subscribe failed";
            PalaceVisibleEntry.Log("payment controller subscribe failed; payment disabled: " + e);
        }
    }

    /// <summary>Native/gateway read. Initial Select must pass before any binding exists.</summary>
    internal bool CanPay(Player player)
    {
        try
        {
            if (_phase == Phase.Blocked || _phase == Phase.Retired || player == null || !SameEntry()) return false;
            Managers m = Managers.Inst;
            if (m == null || m.game == null || m.kingdom == null || NetworkBigBoss.IsOnline
                || !NetworkBigBoss.HasWorldAuth || m.game.Pointer != _gamePointer
                || m.kingdom.Pointer != _kingdomPointer || m.game.state != Game.State.Playing
                || IslandSaveData.isSavingGame || _payable.Price != Price || _payable.Currency != CurrencyType.Coins) return false;
            if (_paid != null && (_phase == Phase.Armed || _phase == Phase.WaitingNative))
            {
                // Existing native settlement does not depend on a new-fee/daytime/store gate.
                return PalacePaidResponsibility.CanContinueCharge(_paid, player, _payable, out _travelReason);
            }
            if (_consumed) return false;
            return PalaceVisitClock.CanBeginPaidVisit(out _travelReason)
                && PalacePaidResponsibility.CanBeginCharge(m.game, m.kingdom, player, _payable, out _travelReason)
                && _travel != null && _travel.CanStart(m.game, m.kingdom, player, _payable, out _travelReason);
        }
        catch (Exception e) { WaitFor("CanPay read failed: " + e.GetType().Name); return false; }
    }

    /// <summary>
    /// Root's owner forwards the native OnPay here. Accepted only for the exact armed receipt at
    /// Completed; then this life is consumed and observes the native settlement. No travel here.
    /// </summary>
    internal void OnPay(Player player)
    {
        try
        {
            if (_phase == Phase.Retired || _phase == Phase.Blocked) return; // stale callback after teardown/block
            if (_consumed)
            {
                if (_paid != null && player != null && player.Pointer.ToInt64() == _payerPointer && SameEntry())
                    PalacePaidResponsibility.ObserveOnPay(_paid,player); // Duplicate exact receipt becomes sticky Hold.
                if (!_duplicateOnPayLogged && player != null && player.Pointer.ToInt64() == _payerPointer)
                {
                    _duplicateOnPayLogged = true;
                    PalaceVisibleEntry.Log("repeated OnPay observed as unknown; no second payment and no second request");
                }
                return;
            }
            if (_phase != Phase.Armed)
            {
                Detail("OnPay ignored outside armed state: phase=" + _phase);
                return;
            }
            if (player == null || player.Pointer.ToInt64() != _payerPointer) { Fail("OnPay payer mismatch"); return; }
            if (!SameEntry()) { Fail("OnPay after entry changed"); return; }
            if (player._payState != Player.PayState.Completed
                || player._completingPayable == null || player._completingPayable.Pointer != _payablePointer
                || player._floatingCurrency == null || player._floatingCurrency.Count != Price)
            { Fail("OnPay without the exact Completed receipt"); return; }

            _consumed = true;
            PalacePaidResponsibility.ObserveOnPay(_paid, player);
            _phase = Phase.WaitingNative;
            _status = "waiting-native";
            _callbackFrame = Time.frameCount;
            _deadline = Time.unscaledTime + SettlementWindowSeconds;
            if (!_payable.forceBlockPayment) _payable.forceBlockPayment = true; // once; no permanent override
            PalaceVisibleEntry.Log("OnPay accepted: exact armed receipt; payer=" + _payerPointer
                + " wallet=" + _walletPointer.ToInt64() + " floating=10; consumed=true callbackFrame=" + _callbackFrame
                + "; forceBlockPayment=true once; native settlement observation starts ("
                + SettlementWindowSeconds + "s); travel not called here");
        }
        catch (Exception e) { Fail("OnPay fault: " + e.GetType().Name); }
    }

    /// <summary>Called from Root's existing scene Update; owns only this entrance's lifecycle.</summary>
    internal void Tick()
    {
        try
        {
            if (_phase == Phase.Armed) { ObserveArmedCancellation(); return; }
            if (_phase != Phase.WaitingNative) return;
            if (Time.frameCount <= _callbackFrame) return; // never act in the OnPay frame
            if (WindowExpired("observing native settlement")) return;

            if (!SameEntry()) { Fail("entry changed while observing"); return; }
            Managers managers = Managers.Inst;
            if (managers == null || managers.game == null || managers.kingdom == null
                || managers.game.Pointer != _gamePointer || managers.kingdom.Pointer != _kingdomPointer)
            { Fail("game/kingdom changed while observing"); return; }
            Player payer = _payer;
            if (payer == null || payer.Pointer.ToInt64() != _payerPointer)
            { Fail("armed payer left while observing"); return; }
            Wallet wallet = payer.wallet;
            if (_walletPointer != IntPtr.Zero && (wallet == null || wallet.Pointer != _walletPointer))
            { Fail("payer wallet changed while observing"); return; }

            int floating = payer._floatingCurrency != null ? payer._floatingCurrency.Count : -1;
            if (payer._payState != Player.PayState.None || floating != 0)
            {
                WaitFor("native settlement pending: payState=" + payer._payState + " floating=" + floating);
                return;
            }

            if (!_settledObserved)
            {
                if (_paid == null || !PalacePaidResponsibility.TryObserveSettled(_paid, payer, out _travelReason))
                { WaitFor("native settlement proof pending: " + _travelReason); return; }
                _settledObserved = true;
                int walletNow = wallet != null ? wallet.Coins : -1;
                // Read-only diagnostic: asynchronous income can land between the two reads, so
                // the delta is never asserted as a spend and never auto-corrected.
                PalaceVisibleEntry.Log("postNative: payState=None floating=0; wallet.Coins " + _walletObserved
                    + " -> " + walletNow + " (delta " + DeltaText(_walletObserved, walletNow)
                    + ", diagnostic only); callbackFrame=" + _callbackFrame);
            }

            if (managers.game.state != Game.State.Playing) { WaitFor("game-state=" + managers.game.state); return; }
            bool can;
            try { can = _travel != null && _travel.CanStartPaid(managers.game, managers.kingdom, payer, _payable, _paid, out _travelReason); }
            catch (Exception e) { Fail("travel.CanStart fault: " + e.GetType().Name); return; }
            if (!can) { WaitFor("travel.CanStart=false: " + _travelReason); return; }

            _phase = Phase.Started; // marked before the single request; accepted != arrived
            _status = "started";
            int request = ++_requests;
            bool accepted;
            try { accepted = _travel.TryStartPaid(managers.game, managers.kingdom, payer, _payable, _paid, out _travelReason); }
            catch (Exception e) { Fail("travel.TryStart fault (request #" + request + "): " + e.GetType().Name); return; }
            if (!accepted) { Fail("travel.TryStart refused (request #" + request + "): " + _travelReason); return; }
            PalaceVisibleEntry.Log("travel request accepted (request #" + request + "): " + _travelReason
                + "; no retry, no refund, no guessed outcome");
        }
        catch (Exception e) { Fail("tick fault: " + e.GetType().Name); }
    }

    /// <summary>真实订阅证据：订阅成功、delegate 仍保留，且非 Blocked/Retired（付款开放/提示用）。</summary>
    internal bool SubscriptionReady => _subscribed && _started != null
        && _phase != Phase.Blocked && _phase != Phase.Retired;

    internal bool IsConsumed => _consumed;

    /// <summary>False once Retired released the self-pin; Root may use it to drop a spent controller.</summary>
    internal bool IsAlive => _life != null;

    internal string StatusText => string.IsNullOrEmpty(_waiting) ? _status : _status + " | " + _waiting;

    /// <summary>Exact unsubscribe of this life's delegate; clears own managed references.</summary>
    internal void Retire()
    {
        if (_phase == Phase.Retired) return;
        Phase previous = _phase;
        _phase = Phase.Retired;
        _status = "retired";
        PayableComponent payable = _payable;
        Il2CppSystem.Action<Player> started = _started;
        _started = null;
        bool wasSubscribed = _subscribed;
        bool removed = false;
        if (wasSubscribed && payable != null && started != null)
        {
            try { payable.remove_OnTransactionStartedCallback(started); removed = true; }
            catch (Exception e) { PalaceVisibleEntry.Log("retire unsubscribe failed (observable); delegate may still be attached: " + e); }
        }
        _subscribed = false;
        _travel = null; _owner = null; _payable = null; _payer = null; _life = null;
        PalaceVisibleEntry.Log("retired from " + previous + "; delegate "
            + (wasSubscribed ? (removed ? "removed" : "removal NOT confirmed; may still be attached") : "was not subscribed")
            + "; managed references cleared");
    }

    private void OnTransactionStarted(Player player)
    {
        try
        {
            if (_consumed || _phase == Phase.Blocked || _phase == Phase.Retired || _phase == Phase.Started) return;
            if (player == null) { Fail("started callback without payer"); return; }
            if (!SameEntry()) { Fail("started callback after entry changed"); return; }
            if (_phase == Phase.Armed)
            {
                if (player.Pointer.ToInt64() != _payerPointer)
                { Fail("another payer started while armed; no cross-takeover"); return; }
                // Idempotent: the first observed binding (wallet pointer/snapshot/frame) wins.
                Detail("repeated started while armed ignored; first binding kept");
                return;
            }

            _payer = player;
            _payerPointer = player.Pointer.ToInt64();
            Wallet wallet = player.wallet;
            _walletPointer = wallet != null ? wallet.Pointer : IntPtr.Zero;
            _walletObserved = wallet != null ? wallet.Coins : -1;
            int floating = player._floatingCurrency != null ? player._floatingCurrency.Count : -1;
            long selected = player.selectedPayable != null ? player.selectedPayable.Pointer.ToInt64() : 0L;
            long completing = player._completingPayable != null ? player._completingPayable.Pointer.ToInt64() : 0L;
            _startedFrame = Time.frameCount;
            Managers m = Managers.Inst;
            if (m == null || m.game == null || m.kingdom == null
                || !PalacePlayable.TryRequirePaidEntryInstallation(true, out _travelReason)
                || !PalaceVisitClock.CanBeginPaidVisit(out _travelReason)
                || _travel == null || !_travel.TryDescribeForCharge(m.game, m.kingdom, player, _payable, out var destination, out _travelReason)
                || !PalaceReturnAnchor.TryCapture(m.game, m.kingdom, player, _payable, out var anchor, out _travelReason)
                || !PalacePaidResponsibility.TryBeginCharge(m.game, m.kingdom, player, _payable, anchor, destination, out _paid, out _travelReason))
            {
                // Native rechecks this life's CanPay after this synchronous delegate and before first coin.
                Fail("durable ChargeIntent refused: " + _travelReason);
                if (_payable != null) _payable.forceBlockPayment = true;
                return;
            }
            _phase = Phase.Armed;
            _status = "armed";
            // Observed snapshot only. It is not assumed to be the state before the first coin and
            // is never inverted into a fabricated "before" total.
            Detail("started/armed: payer=" + _payerPointer + " wallet=" + _walletPointer.ToInt64()
                + " wallet.Coins=" + _walletObserved + " payState=" + player._payState
                + " floating=" + floating + " selected=" + selected + " completing=" + completing + " frame=" + _startedFrame);
        }
        catch (Exception e) { Fail("started callback fault: " + e.GetType().Name); }
    }

    /// <summary>Only the same payer returning to None/floating 0 clears this binding; native owns the refund.</summary>
    private void ObserveArmedCancellation()
    {
        if (Time.frameCount <= _startedFrame || _paid == null) return;
        if (!SameEntry() || _payer == null || _payer.Pointer.ToInt64() != _payerPointer)
        { Fail("entry/payer changed before native cancellation completed"); return; }
        if (!PalacePaidResponsibility.TryObserveCancelled(_paid, _payer, out _travelReason)) return;
        _paid = null; _payer = null; _payerPointer = 0; _walletPointer = IntPtr.Zero;
        _phase = Phase.Idle; _status = "idle"; _waiting = "";
        PalaceVisibleEntry.Log("native cancellation + durable Cancelled confirmed; no wallet compensation");
    }


    private bool SameEntry()
    {
        if (_owner == null || _payable == null) return false;
        if (!_owner.gameObject.activeInHierarchy) return false;
        return _owner.Pointer == _ownerPointer && _payable.Pointer == _payablePointer
            && _owner.gameObject.scene.handle == _sceneHandle
            && _payable.gameObject.scene.handle == _sceneHandle;
    }

    private bool WindowExpired(string stage)
    {
        if (Time.unscaledTime <= _deadline) return false;
        Fail(SettlementWindowSeconds + "s window expired while " + stage + "; outcome unknown");
        return true;
    }

    private void Fail(string reason)
    {
        if (_phase == Phase.Blocked || _phase == Phase.Retired) return;
        Phase previous = _phase;
        _phase = Phase.Blocked;
        _status = "blocked: " + reason;
        PalaceVisibleEntry.Log("blocked from " + previous + ": " + reason + "; no retry, no refund, no guessed result");
    }

    private void WaitFor(string reason)
    {
        if (reason == _waiting) return;
        _waiting = reason;
        Detail("waiting: " + reason);
    }

    private void Detail(string message)
    {
        if (_detailLogs++ < DetailLogLimit) PalaceVisibleEntry.Log(message);
    }

    private static string DeltaText(int before, int after)
        => before < 0 || after < 0 ? "unknown" : (after - before).ToString();
}
