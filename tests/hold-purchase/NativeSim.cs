// NativeSim.cs: faithful re-implementation of the 2.1.0 Player.UpdatePayState state
// machine (game-source/Assembly-CSharp-2.1.0/Player.cs) so the tests can run the
// production hook in front of / behind the real vanilla flow. Plus the frame driver
// (Prefix -> native -> Postfix, Finalizer on native throw) and the fixture.
using System;
using KingdomEnhancedMod;
using UnityEngine;

/// <summary>Vanilla pay state machine, copied from the 2.1.0 source ordering.</summary>
public static class Native
{
    public const int None = 0, Holding = 1, Transaction = 2, Completed = 3, Cancelling = 7;

    public static void UpdatePayState(Player p, bool payKey, bool payKeyDown, bool usingTouch)
    {
        Payable payable = null;
        if (p.actionState != (int)PlayerAction.Run)
        {
            Payable selected = p.selectedPayable;
            if (selected != null && selected.CanSelect(p)
                && Mathf.Abs(selected.PlayerPayPoint() - p.transform.position.x) <= 0.5f
                && Mathf.Abs(selected.PlayerPayPoint() - p.transform.position.x)
                    <= selected.playerPayDistance)
            {
                payable = selected;
            }
            else if (Managers.Inst != null && Managers.Inst.payables != null)
            {
                payable = Managers.Inst.payables.GetClosestPayable(p.transform.position.x, 0.5f, p);
            }
        }

        if (p._payState != Transaction && p._payState != Completed
            && (payable == null || payable != p.selectedPayable))
        {
            CancelTransaction(p);
            SelectPayable(p, payable);
        }

        if (p._payState == None && payKeyDown)
        {
            Payable selected = p.selectedPayable;
            if (selected != null && selected.CanPay(p)
                && (selected.interactingPlayer == null || selected.interactingPlayer == p
                    || !selected.interactingPlayer.gameObject.activeInHierarchy)
                && p.wallet.HasCurrency(selected.Currency))
            {
                selected.interactingPlayer = p;
                SetPayState(p, Holding);
                p.HoldingEntered++;
            }
            else if (p.actionState != (int)PlayerAction.Transformed && !p.IsPetrified
                && p.currencyDroppingEnabled && p.wallet.HasDroppableCurrency)
            {
                p.wallet.DropCurrency(p.wallet.FirstAvailableDroppableCurrency(),
                    new Vector2(0f, 2.5f), 0, null, false, true, true);
                p.GroundDrops++;
            }
        }

        if (p._payState == Holding)
        {
            if (!payKey)
            {
                p.wallet.DropCurrency(CurrencyType.Coins, 0, true);
                p.GroundDrops++;
                SetPayState(p, None);
            }
            else
            {
                p._payTimer += Time.deltaTime;
                float threshold = usingTouch ? p.touchPressedThreshold : p.keyDownThreshold;
                if (p._payTimer > threshold)
                {
                    SetPayState(p, Transaction);
                    p.selectedPayable.Select(p);
                    p.selectedPayable.TransactionStarted(p);
                }
            }
        }

        if (p._payState == Transaction)
        {
            p._payTimer += Time.deltaTime;
            if ((!payKey && p._payTimer > p.timeBeforeCancel) || p.selectedPayable == null
                || !p.selectedPayable.CanPay(p))
            {
                CancelTransaction(p);
                return;
            }
            if (payKey && (p._payTimer > p.ReadIntervalForNative()
                || p._floatingCurrency.Count == 0))
            {
                if (p.wallet.HasCurrency(p.selectedPayable.Currency))
                {
                    AddCurrencyToSelectedPayable(p);
                    return;
                }
                CancelTransaction(p);
                return;
            }
        }
        else
        {
            if (p._payState == Cancelling)
            {
                p._payTimer += Time.deltaTime;
                DropFloatingCurrency(p);
                SetPayState(p, None);
                return;
            }
            if (p._payState == Completed)
            {
                p._payTimer += Time.deltaTime;
                if (p._payTimer > p.timeBeforeTransaction && p._payTimer > p.timeToReachIndicator)
                {
                    if (p._completingPayable == null || !p._completingPayable.CanPay(p))
                    {
                        CancelTransaction(p);
                        return;
                    }
                    p.timeToReachIndicator = 0f;
                    p._completingPayable.TransactionComplete();
                    p._completingPayable = null;
                    p._floatingCurrency.Clear();
                    p.Purchases++;
                    SetPayState(p, None);
                }
            }
        }
    }

    private static void SetPayState(Player p, int state)
    {
        p._payTimer = 0f;
        p._payState = state;
    }

    private static void SelectPayable(Player p, Payable payable)
    {
        if (p.selectedPayable != null) p.selectedPayable.Deselect(p);
        if (payable != null) payable.Select(p);
        p.selectedPayable = payable;
    }

    private static void CancelTransaction(Player p)
    {
        p.CancelCalls++;
        if (p.selectedPayable != null && p.selectedPayable.interactingPlayer == p)
            p.selectedPayable.interactingPlayer = null;
        if (p._payState == Holding)
        {
            SetPayState(p, None);
            return;
        }
        if ((uint)(p._payState - Transaction) > 1) return;
        SetPayState(p, Cancelling);
    }

    private static void DropFloatingCurrency(Player p)
    {
        p._floatingCurrency.Clear();
        SetPayState(p, None);
    }

    private static void AddCurrencyToSelectedPayable(Player p)
    {
        if (p._payState != Transaction && p._payState != None) return;
        p._payState = Transaction;
        p._payTimer = 0f;
        CurrencyType currency = p.selectedPayable.Currency;
        if (!p.wallet.HasCurrency(currency)) return;
        FloatingCoin coin = p.wallet.MoveCurrencyObject(currency, p.selectedPayable.transform,
            default, false);
        coin.MoveTimeUntilNearTarget = p.reachTime;
        p._floatingCurrency.Add(coin);
        if (p._floatingCurrency.Count == p.selectedPayable.Price)
        {
            p._completingPayable = p.selectedPayable;
            p.timeToReachIndicator = coin.MoveTimeUntilNearTarget;
            SetPayState(p, Completed);
        }
    }
}

/// <summary>Frame driver: production Prefix -> native body -> Postfix (Finalizer on throw).</summary>
internal static class Harness
{
    /// <summary>Mirrors the Payable.PerformPay Harmony postfix for a shop.</summary>
    internal static void WirePerformPayHook(Shop shop)
    {
        if (shop == null) return;
        shop.AfterPerformPay = hooked => PatchPlayer_HoldPurchase.OnPerformPay(hooked);
    }

    /// <summary>Unpatched vanilla frame (reference run for the disabled-toggle comparison).</summary>
    internal static void NativeFrame(Player player, bool payKey, bool payKeyDown, float dt = 0.02f)
    {
        AdvanceTime(dt);
        Native.UpdatePayState(player, payKey, payKeyDown, false);
    }

    internal static bool Frame(Player player, bool payKey, bool payKeyDown, bool usingTouch = false,
        float dt = 0.02f)
    {
        if (player.selectedPayable is Shop selected) WirePerformPayHook(selected);
        AdvanceTime(dt);

        bool down = payKeyDown;
        PatchPlayer_HoldPurchase.FrameReceipt receipt;
        PatchPlayer_HoldPurchase.BeforePayUpdate(player, payKey, ref down, out receipt);
        bool injected = down && !payKeyDown;
        try
        {
            Native.UpdatePayState(player, payKey, down, usingTouch);
        }
        catch (Exception error)
        {
            PatchPlayer_HoldPurchase.FinishPayUpdate(player, error, receipt);
            throw;
        }
        PatchPlayer_HoldPurchase.AfterPayUpdate(player, payKey, receipt);
        PatchPlayer_HoldPurchase.FinishPayUpdate(player, null, receipt);
        return injected;
    }

    private static void AdvanceTime(float dt)
    {
        Time.deltaTime = Time.timeScale <= 0f ? 0f : dt;
        Time.time += Time.deltaTime;
        Time.unscaledTime += dt;
    }
}

/// <summary>Fresh world/player/shop fixture; resets every global the production file reads.</summary>
internal sealed class Env
{
    internal readonly Managers Managers;
    internal readonly World World;
    internal readonly Transform Layer;
    internal readonly Player P1;
    internal readonly Player P2;
    internal readonly Shop ShopA;
    internal readonly Shop ShopB;

    private int _nextId = 1;

    internal Env(int priceA = 4, int limitA = 8, int priceB = 4, int limitB = 8)
    {
        Time.time = 0f;
        Time.deltaTime = 0f;
        Time.unscaledTime = 0f;
        Time.timeScale = 1f;
        ModConfig.HoldPurchaseEnabled = new ConfigEntry<bool> { Value = true };
        OptionalQoLScope.IsActive = true;
        OptionalQoLScope.CurrentPredicate = _ => true;

        Managers = new Managers();
        Managers.Inst = Managers;

        Layer = NewTransform(0f, null, out _);
        World = new World { gameLayer = Layer, Pointer = Ptr(0x2000) };
        Managers.world = World;

        P1 = AddPlayer(0, 0f);
        P2 = AddPlayer(1, 0f);
        ShopA = AddShop("ShopHammer", 0f, priceA, limitA, false);
        ShopB = AddShop("ShopBow", 20f, priceB, limitB, false);
    }

    internal static IntPtr Ptr(int value) => new IntPtr(value);

    private Transform NewTransform(float x, Transform parent, out GameObject go)
    {
        go = new GameObject { Id = _nextId++, Pointer = Ptr(0x10000 + _nextId * 0x10) };
        var transform = new Transform { Pointer = Ptr(0x50000 + _nextId * 0x10), position = new Vector3(x, 0f, 0f), Parent = parent };
        go.Attach(transform);
        return transform;
    }

    private Player AddPlayer(int id, float x)
    {
        Transform transform = NewTransform(x, Layer, out GameObject go);
        go.Scene = Layer.gameObject.Scene;
        var player = new Player { playerId = id, Pointer = Ptr(0x30000 + id * 0x100) };
        go.Attach(player);
        player.wallet = new Wallet();
        player.timeBetweenCoins = 0.4f;
        player.Init();
        return player;
    }

    /// <summary>Adds a shop. <paramref name="baker"/> marks the bread shop (Baker on the GO).</summary>
    internal Shop AddShop(string tag, float x, int price, int limit, bool baker)
    {
        Transform transform = NewTransform(x, Layer, out GameObject go);
        go.Scene = Layer.gameObject.Scene;
        go.Tag = tag;
        if (baker) go.Attach(new Baker());
        var shop = new Shop
        {
            Price = price,
            Limit = limit,
            maxItems = limit,
            _limitedNumItems = limit,
            PayPointX = x,
            Pointer = Ptr(0x50000 + _nextId * 0x10)
        };
        go.Attach(shop);
        Managers.payables.All.Add(shop);
        return shop;
    }

    /// <summary>Moves a player onto a shop's pay point.</summary>
    internal static void StandAt(Player player, Payable shop)
    {
        Vector3 position = player.transform.position;
        position.x = shop.PlayerPayPoint();
        player.transform.position = position;
    }

    /// <summary>Drives one frame for a player, returning true when the press was synthesized.</summary>
    internal static bool Frame(Player player, bool payKey, bool payKeyDown, float dt = 0.02f)
        => Harness.Frame(player, payKey, payKeyDown, false, dt);

    /// <summary>Holds the pay key for N frames (fresh press on the first frame).</summary>
    internal static void Hold(Player player, int frames, float dt = 0.02f)
    {
        for (int i = 0; i < frames; i++) Harness.Frame(player, true, i == 0, false, dt);
    }
}
