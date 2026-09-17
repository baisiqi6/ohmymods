// Stubs.cs: minimal Unity / game / sibling-mod doubles for the hold-purchase
// behavior tests. The production file is linked UNMODIFIED (see the csproj);
// only surfaces the production file and the native simulation actually touch
// are modeled here. Nothing in this file reimplements the production algorithm.
using System;
using UnityEngine;
using System.Collections.Generic;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(params object[] args) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyFinalizer : Attribute { }
}

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public static class Mathf
    {
        public static float Abs(float v) => Math.Abs(v);
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
    }

    public static class Time
    {
        public static float time;
        public static float deltaTime;
        public static float unscaledTime;
        public static float timeScale = 1f;
    }

    public struct Scene { public int handle; }

    public class Component
    {
        public GameObject gameObject;
        public IntPtr Pointer;
        private Transform _transform;

        // Unity would throw on destroyed components; null is enough for these tests
        // because the production code only null checks and catches.
        public Transform transform
        {
            get => _transform != null ? _transform : gameObject?.Transform;
            set => _transform = value;
        }
    }

    public class Transform : Component
    {
        public Vector3 position;
        public Transform Parent;

        public bool IsChildOf(Transform other)
        {
            for (Transform current = this; current != null; current = current.Parent)
                if (ReferenceEquals(current, other)) return true;
            return false;
        }
    }

    public class GameObject
    {
        public string Tag = "";
        public bool Active = true;
        public IntPtr Pointer;
        public int Id;
        public Scene Scene;
        public Transform Transform;
        private readonly List<Component> _components = new List<Component>();

        public bool activeInHierarchy
        {
            get
            {
                for (Transform t = Transform; t != null; t = t.Parent)
                    if (!t.gameObject.Active) return false;
                return true;
            }
        }

        public Transform transform => Transform;
        public Scene scene => Scene;
        public bool CompareTag(string tag) => Tag == tag;
        public int GetInstanceID() => Id;
        public void SetActive(bool value) => Active = value;

        public T GetComponent<T>() where T : class
        {
            foreach (Component component in _components)
                if (component is T typed) return typed;
            return null;
        }

        public void Attach(Component component)
        {
            component.gameObject = this;
            if (component is Transform transform)
            {
                Transform = transform;
                return;
            }
            _components.Add(component);
        }
    }
}

// ---------------------------------------------------------------------------
// Game assembly doubles (global namespace, like the interop assembly).
// ---------------------------------------------------------------------------

public enum CurrencyType { Coins, Crown }

public enum PlayerAction { Walk, Run, Stand, Transformed }

public class Payable : UnityEngine.Component
{
    public bool enabled = true;
    public int CastCalls, ThrowOnCastCall;
    public T TryCast<T>() where T : class
    { if (++CastCalls == ThrowOnCastCall) throw new InvalidOperationException("native cast"); return this as T; }
    public bool forceBlockPayment;
    public int Price = 4;
    public CurrencyType Currency = CurrencyType.Coins;
    public float playerPayDistance = 1f;
    public Player interactingPlayer;
    public float PayPointX;
    public bool CanSelectResult = true;
    public bool CanPayResult = true;
    public int CanPayCalls, CanSelectCalls, SelectCalls, DeselectCalls, TransactionStartedCalls;

    public virtual bool CanPay(Player player)
    {
        CanPayCalls++;
        return CanPayResult;
    }

    public virtual bool CanSelect(Player player)
    {
        CanSelectCalls++;
        return CanSelectResult;
    }

    public float PlayerPayPoint() => PayPointX;
    public void Select(Player player) { SelectCalls++; }
    public virtual void Deselect(Player player) { DeselectCalls++; }
    public virtual void TransactionComplete() { }
    public void TransactionStarted(Player player) { TransactionStartedCalls++; }
    public void Highlight() { }
}

public class Baker : UnityEngine.Component { }

/// <summary>PayableShop double: stock/limit drive CanPay the way the real shop does,
/// plus the two 2.4 payment completion paths (host PerformPay, client forceBlock wait).</summary>
public class Shop : Payable
{
    public int Stock;
    public int Limit = 4;
    public int maxItems = 4;
    public int _limitedNumItems = 4;
    public bool Blocked;
    public int PriceIncrease;
    public int TransactionCompleteCalls;
    public Func<Player, bool> CanPayHook;

    /// <summary>Payable+0x100: client sets it while the pay RPC is unanswered.</summary>
    public int PerformPayCalls;
    public Player LastPayer;
    /// <summary>Harness hook mirroring the Payable.PerformPay Harmony postfix.</summary>
    public Action<Shop> AfterPerformPay;

    // client submission bookkeeping (what RecvPay replies to)
    public bool PendingReply;
    public Player SubmittedPayer;
    public int SubmittedPrice;

    public override bool CanPay(Player player)
    {
        CanPayCalls++;
        if (Blocked || !enabled || forceBlockPayment) return false;
        if (CanPayHook != null && !CanPayHook(player)) return false;
        return Stock < Limit;
    }

    public override bool CanSelect(Player player) => !forceBlockPayment && base.CanSelect(player);

    public override void TransactionComplete()
    {
        TransactionCompleteCalls++;
        // Native order: Pay() first, then either the host PerformPay or the client RPC wait.
        if (NetworkBigBoss.HasWorldAuth)
        {
            PerformPay();
        }
        else
        {
            forceBlockPayment = true;
            PendingReply = true;
            SubmittedPayer = interactingPlayer;
            SubmittedPrice = Price;
        }
    }

    /// <summary>Payable.PerformPay 0x667d30: the single success entry (host and client reply).</summary>
    public void PerformPay()
    {
        Stock++;
        if (PriceIncrease != 0) Price += PriceIncrease;
        PerformPayCalls++;
        LastPayer = interactingPlayer;
        AfterPerformPay?.Invoke(this);
    }

    /// <summary>Client side of RecvPay with reply code 1: clears forceBlock, then PerformPay.</summary>
    public void RecvApproved(Player payer)
    {
        forceBlockPayment = false;
        PendingReply = false;
        interactingPlayer = payer;
        PerformPay();
    }

    /// <summary>Client side of RecvPay with reply code 2: clears forceBlock, refunds, never PerformPay.</summary>
    public void RecvDenied(int payerIndex)
    {
        forceBlockPayment = false;
        PendingReply = false;
        if (payerIndex == 1 && SubmittedPayer != null)
            SubmittedPayer.wallet.Coins += SubmittedPrice;   // native refunds player 2 only
    }

    /// <summary>Banker style purchase from outside the player input flow (AutoRestock path).</summary>
    public void ExternalPurchase(Player nearestCrowned)
    {
        interactingPlayer = nearestCrowned;
        TransactionComplete();
        interactingPlayer = null;
    }

    public int GetItemCount() => Stock;
}

public sealed class FloatingCoin
{
    public CurrencyType Type;
    public float MoveTimeUntilNearTarget = 0.25f;
}

public class Wallet
{
    public int Coins;
    public int Dropped;
    public int Launched;
    public bool ThrowOnLaunch;
    public Action<CurrencyType> OnLaunch;

    public bool HasCurrency(CurrencyType type) => type == CurrencyType.Coins && Coins > 0;
    public bool HasDroppableCurrency => Coins > 0;
    public CurrencyType FirstAvailableDroppableCurrency() => CurrencyType.Coins;

    /// <summary>None 加 payKeyDown 的掉地币分支。</summary>
    public void DropCurrency(CurrencyType type, Vector2 velocity, int policy, object a, bool b,
        bool c, bool d)
    {
        Coins--;
        Dropped++;
    }

    /// <summary>Holding 松开时的掉落分支。</summary>
    public void DropCurrency(CurrencyType type, int policy, bool b)
    {
        if (Coins > 0) Coins--;
        Dropped++;
    }

    public FloatingCoin MoveCurrencyObject(CurrencyType type, Transform target, Vector3 offset,
        bool fake)
    {
        Coins--;
        Launched++;
        OnLaunch?.Invoke(type);
        if (ThrowOnLaunch) throw new InvalidOperationException("native coin move failed");
        return new FloatingCoin { Type = type, MoveTimeUntilNearTarget = 0.25f };
    }
}

public class Player : UnityEngine.Component
{
    public int playerId;
    public bool hasLocalAuthority = true;
    public bool TunnelInput;
    public int actionState = (int)PlayerAction.Stand;
    public bool currencyDroppingEnabled = true;
    public bool IsPetrified => false;

    // native pay state (names and values from game-source Player.cs)
    public int _payState;
    public float _payTimer;
    public float timeToReachIndicator;
    public Payable selectedPayable;
    public Payable _completingPayable;
    public readonly List<FloatingCoin> _floatingCurrency = new List<FloatingCoin>();

    // coin/pay settings
    public float keyDownThreshold = 0.26f;
    public float touchPressedThreshold = 0.015f;
    public float timeBeforeCancel = 0.5f;
    public float timeBeforeTransaction = 0.1f;
    public float reachTime = 0.25f;
    private float _timeBetweenCoins;

    public bool ThrowOnIntervalSet;
    public bool FailAllIntervalWrites;
    public bool FailOriginalRestore;
    public int IntervalWrites;

    /// <summary>Reads observed by the native flow only (production reads are not recorded).</summary>
    public bool RecordReads;
    public readonly List<float> Reads = new List<float>();

    public float ReadIntervalForNative()
    {
        if (RecordReads) Reads.Add(_timeBetweenCoins);
        return _timeBetweenCoins;
    }

    public float timeBetweenCoins
    {
        get => _timeBetweenCoins;
        set
        {
            if (FailAllIntervalWrites || (FailOriginalRestore && value == 0.4f))
                throw new InvalidOperationException("native interval write failed");
            if (ThrowOnIntervalSet)
            {
                ThrowOnIntervalSet = false;
                throw new InvalidOperationException("native interval write failed");
            }
            _timeBetweenCoins = value;
            IntervalWrites++;
        }
    }

    public Wallet wallet = new Wallet();
    public int coins { get => wallet.Coins; set => wallet.Coins = value; }

    // observable counters
    public int HoldingEntered;
    public int Purchases;
    public int GroundDrops;
    public int CancelCalls;
    public readonly List<float> CoinLaunchTimes = new List<float>();
    public readonly List<int> CoinLaunchIntervalsMs = new List<int>();

    public void Init()
    {
        wallet.OnLaunch = _ =>
        {
            CoinLaunchTimes.Add(UnityEngine.Time.time);
            int count = CoinLaunchTimes.Count;
            if (count >= 2)
                CoinLaunchIntervalsMs.Add((int)Math.Round(
                    (CoinLaunchTimes[count - 1] - CoinLaunchTimes[count - 2]) * 1000f));
        };
    }
}

public class PayableRegistry
{
    public readonly List<Payable> All = new List<Payable>();

    public Payable GetClosestPayable(float x, float range, Player player)
    {
        Payable best = null;
        float bestDistance = range;
        foreach (Payable payable in All)
        {
            if (payable == null || payable.gameObject == null) continue;
            float distance = Math.Abs(payable.PlayerPayPoint() - x);
            if (distance > bestDistance) continue;
            // The registrar only offers payables the player may currently select, which is
            // exactly why a client drops its selection while forceBlockPayment is set.
            if (!payable.CanSelect(player)) continue;
            bestDistance = distance;
            best = payable;
        }
        return best;
    }
}

public class World
{
    public Transform gameLayer;
    public IntPtr Pointer;
}

/// <summary>Host flag double: false models a network client machine.</summary>
public static class NetworkBigBoss
{
    public static bool HasWorldAuth = true;
}

public class Managers
{
    public static Managers Inst;
    public World world;
    public PayableRegistry payables = new PayableRegistry();
}

// ---------------------------------------------------------------------------
// Sibling mod API doubles (namespace KingdomEnhancedMod).
// ---------------------------------------------------------------------------
namespace KingdomEnhancedMod
{
    public class ConfigEntry<T>
    {
        public T Value;
    }

    public static class ModConfig
    {
        public static ConfigEntry<bool> Enabled = new ConfigEntry<bool> { Value = true };
        public static ConfigEntry<bool> HoldPurchaseEnabled = new ConfigEntry<bool> { Value = true };
    }

    /// <summary>Root scope gate double: world agnostic (all explicit worlds), tests flip it directly.</summary>
#if !SCOPE_TEST
    internal static class OptionalQoLScope
    {
        internal static bool IsActive = true;
        internal static Func<UnityEngine.Component, bool> CurrentPredicate = _ => true;
        internal static bool IsCurrent(UnityEngine.Component component) => CurrentPredicate(component);
    }
#endif

    /// <summary>Root panel flag double: opening the panel must cancel the hold.</summary>
    public static class ModPanel
    {
        public static bool IsShown;
    }

    public class ManualLogSource
    {
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Infos = new List<string>();
        public void LogInfo(string message) => Infos.Add(message);
        public void LogWarning(string message) => Warnings.Add(message);
        public void LogError(string message) => Warnings.Add(message);
    }

    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
        public ManualLogSource LogSource = new ManualLogSource();
    }
}

// These types reuse only the native payment simulator, not production classification logic.
public class PayableWorkshopBarrel : Shop { }
public class PayableComponent : Shop
{
    private UnityEngine.Component owner;
    public bool ThrowOwnerRead;
    public UnityEngine.Component _owner { get { if (ThrowOwnerRead) throw new InvalidOperationException("native owner read"); return owner; } set => owner = value; }
}
public class FireTower : UnityEngine.Component { public bool enabled = true; }
public class PayableWorkshop : Shop { }
