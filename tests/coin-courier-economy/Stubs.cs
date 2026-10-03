// Stubs.cs — fake game / interop / config surface for the coin-courier economy suite.
//
// These types exist only so the UNMODIFIED production files (CoinCourierPurse,
// CoinCourierEconomy, CoinCourierRules, CoinCourierTargeting, CoinCourierBankScope,
// GreekBankScope, PatchEconomy_Banker) can be compiled and driven deterministically.
// They model observable native behavior at the boundary the courier code uses —
// wallet reads/writes, treasury reads/writes, identity, liveness, layer/scene
// membership, PlayerPrefs staging — never the algorithm under test.
//
// Deliberate models:
// - UnityEngine.Object implements Unity's fake-null (a destroyed wrapper == null).
// - GameObject captures the current Sim scene handle at construction, like Unity's
//   scene binding; another world means another handle and therefore another fixture.
// - Wallet.SetCurrency mirrors 2.1.0/2.4 semantics: silently no-ops without world
//   authority and clamps the written value to [0, TotalCapacity].
// - Banker._stashedCoins is a property so native setter/read faults are injectable;
//   WriteFaultWhen throws before the field changes, WriteAfterFaultWhen throws after it
//   (the production code must read back and decide, never assume).
using Object = UnityEngine.Object;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Il2CppInterop.Runtime
{
    // Marker namespace so production `using Il2CppInterop.Runtime;` resolves.
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppArrayBase<T> : IEnumerable<T>
    {
        private readonly T[] _items;

        public Il2CppArrayBase(int length) { _items = new T[length]; }
        public Il2CppArrayBase(T[] items) { _items = items ?? new T[0]; }

        public int Length => _items.Length;
        public int Count => _items.Length;
        public T this[int index] { get => _items[index]; set => _items[index] = value; }
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }

    public class Il2CppReferenceArray<T> : Il2CppArrayBase<T> where T : class
    {
        public Il2CppReferenceArray(long length) : base((int)length) { }
        public Il2CppReferenceArray(T[] items) : base(items) { }
    }
}

namespace Il2CppSystem.Collections.Generic
{
    /// <summary>
    /// 最小 interop 形状字典替身：只实现生产解析器真正使用的 TryGetValue/索引器/Count。
    /// </summary>
    public class Dictionary<TKey, TValue>
    {
        private readonly System.Collections.Generic.Dictionary<TKey, TValue> _items =
            new System.Collections.Generic.Dictionary<TKey, TValue>();
        public bool ThrowOnSet, ThrowOnRead;

        public int Count => _items.Count;

        public TValue this[TKey key]
        {
            get => ThrowOnRead ? throw new InvalidOperationException("native dictionary read") : _items[key];
            set
            {
                if (ThrowOnSet) throw new InvalidOperationException("native dictionary set");
                _items[key] = value;
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (ThrowOnRead) throw new InvalidOperationException("native dictionary read");
            return _items.TryGetValue(key, out value);
        }

        public bool ContainsKey(TKey key) => _items.ContainsKey(key);

        public void Remove(TKey key) => _items.Remove(key);

        public void Clear() => _items.Clear();
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type) { }
        public HarmonyPatch(Type type, string method) { }
        public HarmonyPatch(Type type, string method, Type[] argumentTypes) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfix : Attribute { }
}

namespace Coatsink.Common
{
    // Marker namespace so production `using Coatsink.Common;` resolves.
}

namespace BepInEx.Configuration
{
    public class ConfigEntry<T>
    {
        public T Value;
        public ConfigEntry() { }
        public ConfigEntry(T value) { Value = value; }
    }
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
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float scale) => new Vector3(a.x * scale, a.y * scale, a.z * scale);
    }

    public struct Scene
    {
        public int handle;
        public bool valid;
        public bool IsValid() => valid;
    }

    public static class Mathf
    {
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Abs(float value) => Math.Abs(value);
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 1e-6f;
    }

    public static class Time
    {
        public static float time;
        public static float deltaTime;
        public static float unscaledTime;
        public static float timeScale = 1f;
        public static int frameCount;
    }

    public static class PlayerPrefs
    {
        public static readonly Dictionary<string, int> Ints = new Dictionary<string, int>();
        public static readonly List<string> SetKeys = new List<string>();
        public static int HasKeyCalls, GetIntCalls, SetIntCalls, SaveCalls;
        /// <summary>Test hook: staging fault after the economic commit.</summary>
        public static bool ThrowOnSet;
        public static Action<string> OnGet;

        public static bool HasKey(string key) { HasKeyCalls++; return Ints.ContainsKey(key); }
        public static int GetInt(string key)
        {
            GetIntCalls++;
            OnGet?.Invoke(key);
            return Ints.TryGetValue(key, out int value) ? value : 0;
        }
        public static void SetInt(string key, int value)
        {
            SetIntCalls++;
            if (ThrowOnSet) throw new InvalidOperationException("prefs staging fault");
            SetKeys.Add(key);
            Ints[key] = value;
        }
        public static void Save() { SaveCalls++; }
        public static void ResetAll()
        {
            Ints.Clear();
            SetKeys.Clear();
            HasKeyCalls = GetIntCalls = SetIntCalls = SaveCalls = 0;
            ThrowOnSet = false;
            OnGet = null;
        }
    }

    public class Object
    {
        private static int _nextId;
        private static long _nextPointer;
        internal static readonly List<Object> All = new List<Object>();

        public int Id;
        public IntPtr Pointer;
        public bool Alive = true;
        public string name = "";

        public Object()
        {
            Id = ++_nextId;
            Pointer = (IntPtr)(0x10000000L + _nextPointer++ * 0x40L);
            All.Add(this);
        }

        public int GetInstanceID() => Id;
        public T TryCast<T>() where T : class => this as T;

        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = (object)left == null;
            bool rightNull = (object)right == null;
            if (leftNull && rightNull) return true;
            if (rightNull) return !left.Alive;
            if (leftNull) return !right.Alive;
            return left.Id == right.Id;
        }

        public static bool operator !=(Object left, Object right) => !(left == right);

        public override bool Equals(object other) => other is Object o && this == o;

        public override int GetHashCode() => Id;

        /// <summary>Test hook standing in for Unity calling OnDestroy before the wrapper dies.</summary>
        public static Action<GameObject> DestroyListener;

        public static readonly List<GameObject> Destroyed = new List<GameObject>();

        public static void Destroy(Object target)
        {
            if (target == null) return;
            GameObject owner = target as GameObject;
            if (owner == null)
            {
                Component component = target as Component;
                owner = component != null ? component.gameObject : null;
            }
            if (owner != null) DestroyListener?.Invoke(owner);
            target.Alive = false;
            if (owner != null) Destroyed.Add(owner);
        }

        public static T[] FindObjectsOfType<T>() where T : Object
            => All.OfType<T>().Where(item => item.Alive).ToArray();
    }

    public class Component : Object
    {
        public GameObject gameObject;

        public Transform transform => gameObject != null ? gameObject.transform : null;
        public T GetComponent<T>() where T : Component => gameObject != null ? gameObject.GetComponent<T>() : null;
        public T GetComponentInParent<T>() where T : Component => gameObject != null ? gameObject.GetComponentInParent<T>() : null;
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject != null && gameObject.activeInHierarchy;
    }

    public class Transform : Component
    {
        private Transform _parent;
        public readonly List<Transform> Children = new List<Transform>();
        public Vector3 position;
        private Vector3 _localScale = new Vector3(1f, 1f, 1f);
        public int Writes;

        /// <summary>Parent tracking feeds GetComponentsInChildren so structure scans see the hierarchy.</summary>
        public Transform Parent
        {
            get => _parent;
            set
            {
                _parent = value;
                if (value != null && !value.Children.Contains(this)) value.Children.Add(this);
            }
        }

        public Vector3 localScale
        {
            get => _localScale;
            set { Writes++; _localScale = value; }
        }

        public bool IsChildOf(Transform parent)
        {
            for (Transform current = this; current != null; current = current.Parent)
                if (ReferenceEquals(current, parent)) return true;
            return false;
        }

        /// <summary>Issue 100：models Unity subtree scans (includeInactive).</summary>
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var found = new List<T>();
            CollectComponents(this, includeInactive, found);
            return found.ToArray();
        }

        private static void CollectComponents<T>(Transform node, bool includeInactive, List<T> found)
            where T : Component
        {
            if (node == null) return;
            GameObject owner = node.gameObject;
            if (owner == null) return;
            if (!includeInactive && !owner.activeInHierarchy) return;
            T component = owner.GetComponent<T>();
            if (component != null) found.Add(component);
            for (int i = 0; i < node.Children.Count; i++)
                CollectComponents(node.Children[i], includeInactive, found);
        }
    }

    public class GameObject : Object
    {
        public readonly List<Component> Components = new List<Component>();
        private readonly Transform _transform;
        public bool ActiveSelf = true;
        public Scene scene;

        public GameObject(string name = "actor")
        {
            this.name = name;
            scene = Sim.CurrentScene;
            _transform = new Transform { gameObject = this };
            Components.Add(_transform);
        }

        public Transform transform => _transform;

        public bool activeInHierarchy
        {
            get
            {
                if (!Alive) return false;
                for (Transform current = _transform; current != null; current = current.Parent)
                {
                    if (current.gameObject == null || !current.gameObject.Alive) return false;
                    if (!current.gameObject.ActiveSelf) return false;
                }
                return true;
            }
        }

        public void SetActive(bool value) => ActiveSelf = value;

        public T AddComponent<T>() where T : Component
        {
            T component = Activator.CreateInstance<T>();
            component.gameObject = this;
            Components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault();

        public T GetComponentInParent<T>() where T : Component
        {
            for (Transform current = _transform; current != null; current = current.Parent)
            {
                T found = current.gameObject.GetComponent<T>();
                if (found != null) return found;
            }
            return null;
        }
    }
}

// ---------------------------------------------------------------------------
// Game surface (global namespace, as the game assembly exposes it).
// ---------------------------------------------------------------------------
public enum CurrencyType { Coins, Gems }
public enum DropType { Player, Wildlife, Citizen }
// Side mirrors the native enum: Left = -1, Right = 1 (the rules layer depends on it).
public enum Side { Left = -1, Right = 1 }
public enum Stat { BiggestStash, BiggestWinterStash, CoinsInBank }
public enum Season { Spring, Summer, Autumn, Winter }

public class Game : UnityEngine.Object
{
    public int currentLand;
    public enum State { Menu, Playing, Paused, Loading }

    private State _state = State.Playing;
    /// <summary>Test hook: world-gate read fault.</summary>
    public bool FailStateRead;

    public State state
    {
        get => FailStateRead ? throw new InvalidOperationException("game state read fault") : _state;
        set => _state = value;
    }
}

public class World : Object
{
    public Transform gameLayer;
}

public class OrderedWalls
{
    private readonly Dictionary<Side, List<Wall>> _walls = new Dictionary<Side, List<Wall>>();

    public List<Wall> this[Side side]
    {
        get => _walls.TryGetValue(side, out List<Wall> value) ? value : null;
        set => _walls[side] = value;
    }
}

public class Wall : Behaviour { }

public class Kingdom : Object
{
    public Banker banker;
    public Castle castle;
    public float campfirePosition;
    public bool HasBorderLoaded = true;
    public bool isSafe = true;
    public bool isDaytime = true;
    public OrderedWalls _orderedWalls = new OrderedWalls();
    private readonly Dictionary<Side, float> _borders = new Dictionary<Side, float>();
    private readonly Dictionary<Side, Dictionary<int, Wall>> _walls = new Dictionary<Side, Dictionary<int, Wall>>();

    public float GetBorderSide(Side side) => _borders.TryGetValue(side, out float value) ? value : 0f;

    public Wall GetWall(Side side, int index)
        => _walls.TryGetValue(side, out Dictionary<int, Wall> byIndex)
            && byIndex.TryGetValue(index, out Wall wall) ? wall : null;
}

public class Castle : Behaviour
{
    public readonly List<int> StashCalls = new List<int>();
    /// <summary>Test hook: display refresh fault after the economic commit.</summary>
    public bool ThrowOnSetStash;

    public void SetStash(int value)
    {
        if (ThrowOnSetStash) throw new InvalidOperationException("castle stash fault");
        StashCalls.Add(value);
    }
}

public class Stats : Object
{
    public readonly Dictionary<Stat, int> MaxCalls = new Dictionary<Stat, int>();
    public readonly Dictionary<Stat, int> StatCalls = new Dictionary<Stat, int>();

    public void SetMax(Stat stat, int value) => MaxCalls[stat] = value;
    public void SetStat(Stat stat, int value, bool animate) => StatCalls[stat] = value;
}

public class Director : Object
{
    public Season CurrentSeason = Season.Spring;
}

/// <summary>Real 2.4 Scanner is an interop class, NOT a Unity Component (no gameObject).</summary>
public class Scanner
{
    private static long _next;
    public IntPtr Pointer;
    public float range, rangeBehind, _interval;

    public Scanner() { Pointer = (IntPtr)(0x30000000L + _next++ * 0x40L); }
}

/// <summary>原生登记类型（实测 2.4：Dynamic = 1，SemiStatic = 2）。</summary>
public enum CRPCType
{
    Static = 0,
    Dynamic = 1,
    SemiStatic = 2,
}

/// <summary>原生 RPC 登记头：903 银行家身份的双向登记一侧。</summary>
public class CRPCHeader : Object
{
    public short NetID;
    public int netID;
    public CRPCType HeaderType;
    public GameObject referencedGO;
}

/// <summary>
/// 原生 NetworkPostbox 的最小替身：DynamicObjects 固定 903 登记是装袋身份的唯一来源。
/// </summary>
public class NetworkPostbox : Object
{
    public static NetworkPostbox Instance;

    public readonly Il2CppSystem.Collections.Generic.Dictionary<short, CRPCHeader> DynamicObjects =
        new Il2CppSystem.Collections.Generic.Dictionary<short, CRPCHeader>();
}

public class Banker : Behaviour
{
    public void Persistent_IBehaviour_ApplyData(Il2CppSystem.Object data)
        => _stashedCoins = data.TryCast<BankerData>().stashedCoins;
    private int _stashedCoinsValue;
    public float coinScanRange, coinGatherTargetPercentage, runSpeed, wanderRange, walkSpeed;
    public int playerMaxCoins;
    public Scanner _coinScanner;
    public Wallet _wallet;
    public DroppableCurrency _targetCoin;
    public StateMachine _fsm;
    public Mover _mover;
    public int InterestPerDay;
    /// <summary>原生 Banker.BeginRegisteringRPCs 写入的登记回指（双向登记的另一侧）。</summary>
    public CRPCHeader parentHeaderRef;

    /// <summary>
    /// Test hooks: native setter/read faults. Write faults are value-conditioned so the
    /// Greek priming write (the shared balance) can differ from the one-coin debit.
    /// </summary>
    public Func<int, bool> WriteFaultWhen;
    public Func<int, bool> WriteAfterFaultWhen;
    public bool ThrowOnStashRead;
    /// <summary>Runs inside the setter before the write decision (fault cascades).</summary>
    public Action<int> OnStashWriteAttempt;
    public int StashWrites;

    public int _stashedCoins
    {
        get
        {
            if (ThrowOnStashRead) throw new InvalidOperationException("treasury read fault");
            return _stashedCoinsValue;
        }
        set
        {
            StashWrites++;
            OnStashWriteAttempt?.Invoke(value);
            if (WriteFaultWhen != null && WriteFaultWhen(value))
                throw new InvalidOperationException("treasury write fault");
            _stashedCoinsValue = value;
            if (WriteAfterFaultWhen != null && WriteAfterFaultWhen(value))
                throw new InvalidOperationException("treasury write fault after commit");
        }
    }

    public void Awake() { }
    public void Update() { }
    public void OnDestroy() { }
    public void OpenCastleDoor() { }
    public void FinaliseEmerge() { }
    public void HandleOnDayStart() { }
    public void ClaimCoins() { }
    public bool ShouldHide() => false;
    public bool ShouldEmerge() => true;
}

public class Managers : Object
{
    public static Managers Inst;
    public World world;
    public Kingdom kingdom;
    public Game game;
    public Stats stats;
    public Director director;
    public DroppableRegistrar dropManager;
    public int OnLevelLoadedCalls;

    /// <summary>Native successful-load notification (postfix host for the fixed domain).</summary>
    public void OnLevelLoaded(bool fromSave) { OnLevelLoadedCalls++; }
}

public class DroppableRegistrar : Behaviour
{
    public readonly List<Droppable> Droppables = new List<Droppable>();
    public int QueryCalls;

    public void GetDroppablesInRange<T>(float position, float range,
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<T> buffer,
        out int count, object filter) where T : Droppable
    {
        QueryCalls++;
        int written = 0;
        for (int i = 0; i < Droppables.Count && written < buffer.Length; i++)
            if (Droppables[i] is T typed) buffer[written++] = typed;
        count = written;
    }
}

public class Droppable : Behaviour
{
    public virtual bool TryFriendlyClaim(GameObject claimer, float range) => true;
}

public class DroppableCurrency : Droppable
{
    public DropType droppedBy = DropType.Player;
    public CurrencyType CurrencyType;
    public bool Fake;
    public GameObject friendlyClaimer;

    public bool IsFake() => Fake;
    public void ClearFriendlyClaimIfClaimer(GameObject claimer)
    {
        if (friendlyClaimer == claimer) friendlyClaimer = null;
    }
}

public class BiomeHolder
{
    public static BiomeHolder Inst = new BiomeHolder();
    public const int GreeceBiomeIndex = 3;

    private int _biomeIndex = GreeceBiomeIndex;
    public bool FailRead;

    public int BiomeIndex
    {
        get => FailRead ? throw new InvalidOperationException("biome read fault") : _biomeIndex;
        set => _biomeIndex = value;
    }
}

public static class NetworkBigBoss
{
    public static bool HasWorldAuth = true;
    public static bool IsOnline = false;
    public static bool IsClientPresent = false;
    public static bool HasClientCaughtUp = true;
}

// ---------------------------------------------------------------------------
// Courier-wallet / knight surface used by the production courier files.
// ---------------------------------------------------------------------------
public class Wallet : Behaviour
{
    private readonly Dictionary<CurrencyType, int> _amount = new Dictionary<CurrencyType, int>();
    public int TotalCapacity = 1000;
    /// <summary>Native final pickup entry; the fixed-domain wallet gate prefixes it.</summary>
    public int SuckCurrencyCalls;
    public bool SuckCurrency(DroppableCurrency currency, bool playSound)
    {
        SuckCurrencyCalls++;
        return true;
    }

    /// <summary>Test hooks: fault inside SetCurrency before/after the map write, or on read.</summary>
    public Action<CurrencyType, int> BeforeWrite;
    public Action<CurrencyType, int> AfterWrite;
    public bool ThrowOnRead;
    public int Reads, Writes;

    public int Coins
    {
        get => GetCurrency(CurrencyType.Coins);
        set => SetCurrency(CurrencyType.Coins, value);
    }

    public int GetCurrency(CurrencyType type)
    {
        Reads++;
        if (ThrowOnRead) throw new InvalidOperationException("wallet read fault");
        return _amount.TryGetValue(type, out int value) ? value : 0;
    }

    public void SetCurrency(CurrencyType type, int value)
    {
        // Native gate: clients that are not caught up cannot write the local map.
        if (!NetworkBigBoss.HasWorldAuth && !NetworkBigBoss.HasClientCaughtUp) return;
        Writes++;
        BeforeWrite?.Invoke(type, value);
        int clamped = value < 0 ? 0 : value > TotalCapacity ? TotalCapacity : value;
        _amount[type] = clamped;
        AfterWrite?.Invoke(type, value);
    }
}

public class Character : Behaviour
{
    public bool inert;
    public bool grabbed;
}

public class Damageable : Behaviour
{
    public bool isDead;
    public bool invulnerable;
}

public class Embarkee : Behaviour
{
    public bool IsEmbarked;
    public bool IsTargetingEmbarkable;
    public GameObject EmbarkableTarget;
}

public class StateMachine
{
    public Knight.State Current;
    public int _queuedState;
    public bool _executeQueuedState;
    public int GoToStateCalls;

    public void GoToState(int state)
    {
        GoToStateCalls++;
        _queuedState = state;
        _executeQueuedState = true;
    }

    public void GoToState(int state, bool force) => GoToState(state);
}

public class Mover : Component
{
    public enum GoalMode { Off = 0, Position = 1, Object = 2 }
    public enum OffsetMode { Distance = 0, Formation = 1, Strict = 2 }

    public bool movingToGoal;
    public GoalMode goalMode;
    public float _goalPosition;
    public GameObject _goalObject;
    public int StopCalls;

    public void Stop() { StopCalls++; movingToGoal = false; }
}

public class PayableUpgrade : Behaviour
{
    /// <summary>原生成品证据：未建 Wall0 只有 PayableUpgrade.nextPrefab 直接指向带 Wall 的预制。</summary>
    public GameObject nextPrefab;
}

public class Knight : Behaviour
{
    public enum State
    {
        Stand = 0,
        GoToWall = 1,
        Assemble = 2,
        Charge = 3,
        GrabCoin = 4,
        GrabArmor = 5,
        InFormation = 6,
        Embarking = 7,
        MoveToPillar = 8,
        Stationary = 9,
    }

    public Side side = Side.Left;
    public Wallet Wallet;
    public Character _character;
    public Damageable _damageable;
    public StateMachine _fsm;
    public Embarkee _embarkee;
}

// ---------------------------------------------------------------------------
// Fixture helpers shared by the harness and the stubs.
// ---------------------------------------------------------------------------
public static class Sim
{
    public static int SceneHandle = 1;
    public static bool SceneValid = true;
    public static Scene CurrentScene => new Scene { handle = SceneHandle, valid = SceneValid };

    /// <summary>Creates a layer GameObject in the current scene era, as a world switch would.</summary>
    public static GameObject NewLayer(string name = "GameLayer") => new GameObject(name);

    public static GameObject NewActor(string name = "actor", Transform parent = null)
    {
        GameObject actor = new GameObject(name);
        if (parent != null) actor.transform.Parent = parent;
        return actor;
    }
}

// ---------------------------------------------------------------------------
// Mod-side surface used by the production courier files.
// ---------------------------------------------------------------------------
public class ManualLogSource
{
    public readonly List<string> Infos = new List<string>();
    public readonly List<string> Warnings = new List<string>();
    public readonly List<string> Errors = new List<string>();

    public void LogInfo(string message) => Infos.Add(message);
    public void LogWarning(string message) => Warnings.Add(message);
    public void LogError(object message) => Errors.Add(Convert.ToString(message));
}

public class KingdomEnhancedPlugin
{
    public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
    public ManualLogSource LogSource = new ManualLogSource();
}

namespace KingdomEnhancedMod
{
    using BepInEx.Configuration;

    public static class ModConfig
    {
        public static ConfigEntry<bool> Enabled = new ConfigEntry<bool>(true);

        public static void ResetConfig()
        {
            Enabled = new ConfigEntry<bool>(true);
        }
    }

    /// <summary>
    /// Coordinator stand-in: only the gate the bank owner calls is modelled. The courier
    /// entry must not use this restock-only gate for its own eligibility.
    /// </summary>
    internal static class BankAssistantCoordinator
    {
        internal static Banker MainBanker;
        internal static int GateCalls;

        internal static bool IsCurrentRestockBanker(Banker banker)
        {
            GateCalls++;
            return banker != null && ReferenceEquals(banker, MainBanker)
                && ModConfig.Enabled.Value && NetworkBigBoss.HasWorldAuth
                && Time.timeScale > 0f
                && Managers.Inst != null && Managers.Inst.game != null
                && Managers.Inst.game.state == Game.State.Playing;
        }

        internal static void ResetCoord()
        {
            MainBanker = null;
            GateCalls = 0;
        }
    }

    /// <summary>Assistant bridge surface the bank file calls (no-op stand-in).</summary>
    internal static class PatchEconomy_BankAssistants
    {
        internal static int EnsureCalls;
        internal static bool Bound;

        public static void EnsureForMainBanker(Banker banker)
        {
            EnsureCalls++;
            Bound = true;
        }

        internal static bool IsBound(Banker banker) => Bound && banker != null;

        internal static void Reset()
        {
            EnsureCalls = 0;
            Bound = false;
        }
    }

    /// <summary>
    /// Deterministic stand-in for the knight identity runtime: the economy only needs a
    /// stable lifetime per Knight and the ability to change it as a pool reuse would.
    /// </summary>
    internal static class KnightIdentityRuntime
    {
        private static readonly Dictionary<Knight, long> Lifetimes = new Dictionary<Knight, long>();

        internal static long GetLifetime(Knight knight)
            => knight != null && Lifetimes.TryGetValue(knight, out long life) ? life : 0L;

        internal static void SetLifetime(Knight knight, long life) => Lifetimes[knight] = life;

        internal static void Reset() => Lifetimes.Clear();
    }

    /// <summary>Roster stand-in: the tests publish the roster the shared scan would return.</summary>
    internal static class UnitScanCache
    {
        internal static Knight[] Knights = Array.Empty<Knight>();
        internal static int Calls;

        internal static Knight[] GetKnights(float maxAgeSec = 3f)
        {
            Calls++;
            return Knights;
        }

        internal static void Reset()
        {
            Knights = Array.Empty<Knight>();
            Calls = 0;
        }
    }
}
