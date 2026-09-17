// Stubs.cs — fake game / interop / config surface for the Greek bank-scope regression.
//
// These types exist only so the UNMODIFIED production files can be compiled and driven
// deterministically. They model observable native behavior at the boundary the bank code
// uses (identity, liveness, layer/scene membership, PlayerPrefs, pool registration,
// claim/policy mutation counters) and never the algorithm under test.
//
// Deliberate models:
// - UnityEngine.Object implements Unity's fake-null: a destroyed wrapper compares equal
//   to null, and a "null" comparison against a live object is false.
// - GameObject captures the current Sim scene handle at construction, like Unity's
//   scene binding; moving to another world means creating objects under a new handle.
// - Object.Destroy invokes DestroyListener (a test hook standing in for Unity calling
//   OnDestroy) before the wrapper dies, so Harmony prefix behavior can be observed.
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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
        public Il2CppReferenceArray(int length) : base(length) { }
        public Il2CppReferenceArray(T[] items) : base(items) { }
    }
}

namespace Il2CppInterop.Runtime.Injection
{
    public static class ClassInjector
    {
        public static readonly HashSet<Type> Registered = new HashSet<Type>();
        public static bool IsTypeRegisteredInIl2Cpp(Type type) => Registered.Contains(type);
        public static void RegisterTypeInIl2Cpp(Type type) => Registered.Add(type);
    }
}

namespace HarmonyLib
{
    public static class Priority { public const int Last = 800; }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type) { }
        public HarmonyPatch(Type type, string method) { }
        public HarmonyPatch(Type type, string method, Type[] argumentTypes) { }
    }

    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPriority : Attribute
    { public HarmonyPriority(int priority) { } }
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
    public enum HideFlags { None = 0, HideAndDontSave = 61 }
    public enum RigidbodyType2D { Dynamic, Kinematic, Static }
    [Flags] public enum RigidbodyConstraints2D { None = 0, FreezeRotation = 4 }
    public enum AnimatorUpdateMode { Normal, AnimatePhysics, UnscaledTime }
    public enum AnimatorCullingMode { AlwaysAnimate, CullUpdateTransforms, CullCompletely }

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
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float scale) => new Vector3(a.x * scale, a.y * scale, a.z * scale);
        public float this[int index]
        {
            get => index == 0 ? x : index == 1 ? y : z;
            set { if (index == 0) x = value; else if (index == 1) y = value; else z = value; }
        }
        public static float Distance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDelta)
        {
            Vector3 delta = target - current;
            float distance = Distance(current, target);
            if (distance <= maxDelta || distance == 0f) return target;
            return current + delta * (maxDelta / distance);
        }
    }

    public struct Quaternion { public static Quaternion identity => default; }

    public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; } }

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
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta) return target;
            return current + Math.Sign(target - current) * maxDelta;
        }
        public static float Sign(float value) => value >= 0f ? 1f : -1f;
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

    public static class Random
    {
        public static float Range(float min, float max) => (min + max) * 0.5f;
    }

    public static class PlayerPrefs
    {
        public static readonly Dictionary<string, int> Ints = new Dictionary<string, int>();
        public static readonly List<string> SetKeys = new List<string>();
        public static int HasKeyCalls, GetIntCalls, SetIntCalls, SaveCalls;

        public static bool HasKey(string key) { HasKeyCalls++; return Ints.ContainsKey(key); }
        public static int GetInt(string key) { GetIntCalls++; return Ints.TryGetValue(key, out int value) ? value : 0; }
        public static void SetInt(string key, int value) { SetIntCalls++; SetKeys.Add(key); Ints[key] = value; }
        public static void Save() { SaveCalls++; }
        public static void ResetAll()
        {
            Ints.Clear(); SetKeys.Clear();
            HasKeyCalls = GetIntCalls = SetIntCalls = SaveCalls = 0;
        }
    }

    public class Object
    {
        private static int _nextId;
        private static long _nextPointer;
        internal static readonly List<Object> All = new List<Object>();

        public readonly int Id;
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

        public static bool IsNull(Object value) => (object)value == null || !value.Alive;

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

        /// <summary>Stands in for Unity calling OnDestroy before the wrapper dies.</summary>
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

        public static Il2CppArrayBase<T> FindObjectsOfType<T>() where T : Object
        {
            return new Il2CppArrayBase<T>(All.OfType<T>().Where(item => !IsNull(item)).ToArray());
        }
    }

    public static class Resources
    {
        public static readonly List<Object> Assets = new List<Object>();

        public static Il2CppArrayBase<T> LoadAll<T>(string path) where T : Object
            => new Il2CppArrayBase<T>(Assets.OfType<T>().Where(item => !Object.IsNull(item)).ToArray());

        public static Il2CppArrayBase<T> FindObjectsOfTypeAll<T>() where T : Object
            => new Il2CppArrayBase<T>(Object.All.OfType<T>().Where(item => !Object.IsNull(item)).ToArray());
    }

    public class Component : Object
    {
        public GameObject gameObject;

        public Transform transform => gameObject != null ? gameObject.transform : null;
        public T GetComponent<T>() where T : Component => gameObject != null ? gameObject.GetComponent<T>() : null;
        public T GetComponentInParent<T>() where T : Component => gameObject != null ? gameObject.GetComponentInParent<T>() : null;
        public T[] GetComponentsInChildren<T>() where T : Component
            => gameObject != null ? gameObject.GetComponentsInChildren<T>() : new T[0];
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject != null && gameObject.activeInHierarchy;
    }

    public class MonoBehaviour : Behaviour
    {
        public MonoBehaviour() { }
        public MonoBehaviour(IntPtr pointer) { }
    }

    public class Transform : Component
    {
        public Transform Parent;
        public int FailChildReads;
        public Vector3 position;
        private Vector3 _localScale = new Vector3(1f, 1f, 1f);
        public int Writes;

        public Vector3 localScale
        {
            get => _localScale;
            set { Writes++; _localScale = value; }
        }

        public bool IsChildOf(Transform parent)
        {
            if (FailChildReads-- > 0) throw new InvalidOperationException("transient native read");
            for (Transform current = this; current != null; current = current.Parent)
                if (ReferenceEquals(current, parent)) return true;
            return false;
        }
    }

    public class GameObject : Object
    {
        public readonly List<Component> Components = new List<Component>();
        private readonly Transform _transform;
        public bool ActiveSelf = true;
        public int layer;
        public HideFlags hideFlags;
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
            var constructor = typeof(T).GetConstructor(new[] { typeof(IntPtr) });
            T component = constructor != null
                ? (T)constructor.Invoke(new object[] { IntPtr.Zero })
                : (T)Activator.CreateInstance(typeof(T));
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

        public T[] GetComponentsInChildren<T>() where T : Component
            => Components.OfType<T>().ToArray();
    }

    public class Avatar : Object { }
    public class Sprite : Object { }
    public class Material : Object { }

    public class RuntimeAnimatorController : Object
    {
        public RuntimeAnimatorController(string name) { this.name = name; }
    }

    public class AnimatorOverrideController : RuntimeAnimatorController
    {
        public RuntimeAnimatorController runtimeAnimatorController;
        public AnimatorOverrideController(string name) : base(name) { }
    }

    public class Animator : Behaviour
    {
        public RuntimeAnimatorController runtimeAnimatorController;
        public Avatar avatar;
        public bool applyRootMotion;
        public AnimatorUpdateMode updateMode;
        public AnimatorCullingMode cullingMode;
        public readonly Dictionary<int, float> Floats = new Dictionary<int, float>();

        public void SetFloat(int hash, float value) => Floats[hash] = value;
        public static int StringToHash(string name) => name.GetHashCode();
    }

    public class SpriteRenderer : Behaviour
    {
        public Sprite sprite;
        public Material sharedMaterial;
        public Color color;
        public bool flipX, flipY;
        public int sortingLayerID, sortingOrder;
    }

    public class Rigidbody2D : Behaviour
    {
        public RigidbodyType2D bodyType;
        public float gravityScale;
        public RigidbodyConstraints2D constraints;
    }
}

// ---------------------------------------------------------------------------
// Game surface (global namespace, as the game assembly exposes it).
// ---------------------------------------------------------------------------

public enum CurrencyType { Coins, Jade, Eggs }
public enum DropType { Player, Wildlife, Citizen }
public enum PickUpPolicy { Everyone, OnlyClaimer, Nobody }
public enum Side { Left, Right }
public enum Stat { BiggestStash, BiggestWinterStash, CoinsInBank }
public enum Season { Spring, Summer, Autumn, Winter }

public class Game
{
    public enum State { Menu, Playing, Paused, NetworkClientPlaying, Loading }
    public State state = State.Playing;
}

public class World : Object
{
    public Transform gameLayer;
}

public class Player : Object
{
    public Payable selectedPayable;
    public Payable _completingPayable;
}

public class Payable : Behaviour
{
    public int Price = 10, priceIncrease;
    public CurrencyType Currency;
    public bool forceBlockPayment, selectedByP1, selectedByP2;
    public Player PlayerSelecting, interactingPlayer;
    public object parentHeaderRef = new object();
    public int _payRPCIndex = 1;
    public int DeselectCalls, TransactionCompleteCalls;
    public Vector3 GetApproximateGameLayerPosition() => new Vector3(0f, 0f, 0f);
    public bool CanPay(Player player) => true;
    public void Deselect(Player player) { DeselectCalls++; selectedByP1 = selectedByP2 = false; PlayerSelecting = null; }
    public virtual void TransactionComplete() { TransactionCompleteCalls++; }
}

public class WorkableBuilding : Object
{
    public bool UnderConstruction;
}

public class Droppable : Behaviour
{
    public virtual bool TryFriendlyClaim(GameObject claimer, float range) => false;
    public object parentHeaderRef = new object();
    public int DropCalls;

    public void Drop(GameObject dropper, Vector2 position, Vector2 velocity, PickUpPolicy policy,
        bool force, bool fake, bool animate) { DropCalls++; }
    public void Drop(Vector2 position, PickUpPolicy policy) { DropCalls++; }
}

public class DroppableCurrency : Droppable
{
    public DropType droppedBy = DropType.Player;
    public CurrencyType CurrencyType;
    public PickUpPolicy pickUpPolicy = PickUpPolicy.Everyone;
    public GameObject friendlyClaimer;
    public bool pickedUp;
    public bool Fake;
    public bool ClaimResult = true;
    public int ClaimCalls, PolicyRpcCalls, ClearClaimCalls, SyncPickedUpCalls, MoveToCalls, SetFakeCalls;

    public bool IsFake() => Fake;
    public void SetFake(bool value) { SetFakeCalls++; Fake = value; }

    public override bool TryFriendlyClaim(GameObject claimer, float range)
    {
        ClaimCalls++;
        if (!ClaimResult) return false;
        friendlyClaimer = claimer;
        return true;
    }

    public int FailPolicyRpc;
    public void SendPolicyRPC() { PolicyRpcCalls++; if (FailPolicyRpc-- > 0) throw new InvalidOperationException("RPC not ready"); }
    public void SyncPickedUpAndFake() => SyncPickedUpCalls++;
    public void ClearFriendlyClaimIfClaimer(GameObject claimer)
    {
        ClearClaimCalls++;
        if (friendlyClaimer == claimer) friendlyClaimer = null;
    }
    public void MoveTo(Transform target, Vector3 offset, bool destroyAfter) => MoveToCalls++;
}

public class CurrencyManager : Behaviour
{
    public DroppableCurrency CoinPrefab;
    public T GetCurrencyTypePrefab<T>(CurrencyType type) where T : Droppable => CoinPrefab as T;
}

public class Scanner
{
    private static long next = 100000;
    public IntPtr Pointer = (IntPtr)(++next);
    public float range, rangeBehind, _interval;
}

public class Wallet : Behaviour
{
    public int TotalCapacity = 1000;
}

public class Castle : Behaviour
{
    public readonly List<int> StashCalls = new List<int>();
    public void SetStash(int value) => StashCalls.Add(value);
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

public class Banker : Behaviour
{
    public int _stashedCoins;
    public float coinScanRange, coinGatherTargetPercentage, walkSpeed, runSpeed, wanderRange;
    public int playerMaxCoins;
    public Scanner _coinScanner;
    public Wallet _wallet;
    public DroppableCurrency _targetCoin;
    public int InterestPerDay;
    public int AwakeCalls, UpdateCalls, OnDestroyCalls, DayStartCalls, OpenDoorCalls,
        FinaliseCalls, ClaimCoinsCalls, ShouldHideCalls, ShouldEmergeCalls;

    public void Awake() { AwakeCalls++; }
    public void Update() { UpdateCalls++; }
    public void OnDestroy() { OnDestroyCalls++; }
    public void OpenCastleDoor() { OpenDoorCalls++; }
    public void FinaliseEmerge() { FinaliseCalls++; }
    public void ClaimCoins() { ClaimCoinsCalls++; }
    public bool ShouldHide() { ShouldHideCalls++; return true; }
    public bool ShouldEmerge() { ShouldEmergeCalls++; return true; }

    /// <summary>Native day-start: 原版利息只在这里加一次。</summary>
    public void HandleOnDayStart()
    {
        DayStartCalls++;
        if (InterestPerDay != 0) _stashedCoins += InterestPerDay;
    }
}

public class Wall : Behaviour { }

public class OrderedWalls
{
    private readonly Dictionary<Side, List<Wall>> _walls = new Dictionary<Side, List<Wall>>();
    public List<Wall> this[Side side]
    {
        get => _walls.TryGetValue(side, out List<Wall> value) ? value : null;
        set => _walls[side] = value;
    }
}

public class Kingdom : Object
{
    public Banker banker;
    public Castle castle;
    public float campfirePosition;
    public bool HasBorderLoaded = true;
    public bool isSafe = true;
    public Player playerOne, playerTwo;
    public Func<Vector3, Player> CrownFinder;
    public OrderedWalls _orderedWalls = new OrderedWalls();
    public readonly Dictionary<Side, float> Borders = new Dictionary<Side, float>();
    public readonly Dictionary<Side, List<Wall>> WallsBySide = new Dictionary<Side, List<Wall>>();
    public readonly Dictionary<Side, Dictionary<int, Wall>> WallByIndex = new Dictionary<Side, Dictionary<int, Wall>>();

    public float GetBorderSide(Side side) => Borders.TryGetValue(side, out float value) ? value : 0f;

    public Wall GetWall(Side side, int index)
    {
        return WallByIndex.TryGetValue(side, out Dictionary<int, Wall> byIndex)
            && byIndex.TryGetValue(index, out Wall wall) ? wall : null;
    }

    public Player GetNearestPlayerWithCrown(Vector3 position)
        => CrownFinder != null ? CrownFinder(position) : new Player();
}

public class Managers : Object
{
    public static Managers Inst;
    public World world;
    public Kingdom kingdom;
    public PoolManager pools;
    public DroppableRegistrar dropManager;
    public Game game;
    public Stats stats;
    public CurrencyManager currency;
    public Director director;
}

public class DroppableRegistrar : Behaviour
{
    public readonly List<Droppable> Droppables = new List<Droppable>();
    public int QueryCalls;

    public void GetDroppablesInRange<T>(float position, float range, Il2CppArrayBase<T> buffer,
        out int count, object filter) where T : Droppable
    {
        QueryCalls++;
        int written = 0;
        for (int i = 0; i < Droppables.Count && written < buffer.Length; i++)
            if (Droppables[i] is T typed) buffer[written++] = typed;
        count = written;
    }
}

public class Pool : Behaviour
{
    public GameObject prefab;
    public int preload, capacity;
    public bool sync, expendable;
    public short syncID;

    public static readonly Dictionary<GameObject, Pool> ByPrefab = new Dictionary<GameObject, Pool>();
    public static readonly Dictionary<GameObject, Pool> ByInstance = new Dictionary<GameObject, Pool>();
    public static int SpawnGoCalls, DespawnCalls;

    public static Pool GetPoolFromPrefabAsset(GameObject prefab)
        => prefab != null && ByPrefab.TryGetValue(prefab, out Pool pool) ? pool : null;

    public static Pool GetPoolByInstance(GameObject instance)
        => instance != null && ByInstance.TryGetValue(instance, out Pool pool) ? pool : null;

    public static GameObject SpawnGO(GameObject prefab, Vector3 position, Quaternion rotation,
        Transform parent, bool allowInstantiate, bool allowCreatePool, bool assertNonNullPrefab)
    {
        SpawnGoCalls++;
        Pool pool = GetPoolFromPrefabAsset(prefab);
        if (pool == null) return null; // 未注册的 prefab 在原生池里出不了实例
        GameObject actor = new GameObject(prefab.name + "(Clone)");
        actor.transform.Parent = parent;
        actor.transform.position = position;
        actor.AddComponent<Animator>();
        actor.AddComponent<SpriteRenderer>();
        actor.AddComponent<Rigidbody2D>();
        actor.AddComponent<PositionSync>();
        ByInstance[actor] = pool;
        return actor;
    }

    public static void Despawn(GameObject instance, bool destroy)
    {
        DespawnCalls++;
        if (instance != null) UnityEngine.Object.Destroy(instance);
    }

    public static T Spawn<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent,
        bool active) where T : Droppable
    {
        if (prefab == null) return null;
        T clone = (T)Activator.CreateInstance(typeof(T));
        clone.gameObject = new GameObject(prefab.name + "(Clone)");
        clone.gameObject.transform.Parent = parent;
        clone.gameObject.transform.position = position;
        return clone;
    }
}

public class PoolManager : Behaviour
{
    public readonly Dictionary<short, Pool> cachedSyncIdPoolPairs = new Dictionary<short, Pool>();
    public readonly List<Pool> cachedPools = new List<Pool>();
    public readonly Dictionary<string, Pool> cachedNamePoolPairs = new Dictionary<string, Pool>();
    public readonly List<Pool> Created = new List<Pool>();
    public int CreateCalls;

    public Pool CreatePoolFor(GameObject prefab)
    {
        CreateCalls++;
        Pool pool = new Pool { gameObject = gameObject, prefab = prefab };
        Pool.ByPrefab[prefab] = pool;
        return pool;
    }

    public void Init() { }
}

public class PositionSync : Behaviour
{
    public bool onConnectPosSync, enforceHeadingSync, fullAccuracyYSync, disableAnimPassthrough;
    public float syncDeltaThreshold, syncTimeMinInterval;
    public object parentHeaderRef = new object();
    public int SyncCalls, SendCalls;

    public void SetSyncAndRemote(bool sync, bool remote) => SyncCalls++;
    public void SendFullPos(bool force) => SendCalls++;
    public void Update() { }
}

public class Farmland : Behaviour { }
public class Persistent : Behaviour { }

public static class NetworkBigBoss
{
    public static bool HasWorldAuth = true;
    public static bool IsOnline = false;
    public static bool IsClientPresent = false;
    public static bool HasClientCaughtUp = true;
}

public class BiomeSwapData : Object
{
    public class AnimatorSwapData
    {
        public RuntimeAnimatorController original;
        public RuntimeAnimatorController swap;
    }

    public readonly List<AnimatorSwapData> animatorSwapPool = new List<AnimatorSwapData>();
}

public class BiomeData : Object
{
    public static BiomeData Current = new BiomeData();
    public T GetAssetSwapForThis<T>(T original) where T : UnityEngine.Object => original;
    public BiomeSwapData swapData;
}

public class BiomeHolder
{
    public static BiomeHolder Inst = new BiomeHolder();
    public const int GreeceBiomeIndex = 3;

    private int _biomeIndex = GreeceBiomeIndex;
    public bool FailRead;
    public Il2CppArrayBase<BiomeSwapData> biomePreloadData = new Il2CppArrayBase<BiomeSwapData>(0);
    public Il2CppArrayBase<BiomeData> biomeData = new Il2CppArrayBase<BiomeData>(0);

    public int BiomeIndex
    {
        get => FailRead ? throw new InvalidOperationException("biome read fault") : _biomeIndex;
        set => _biomeIndex = value;
    }
}

// ---------------------------------------------------------------------------
// Unity-free fixture helpers shared by the harness and the stubs.
// ---------------------------------------------------------------------------
public static class Sim
{
    public static int SceneHandle = 1;
    public static bool SceneValid = true;
    public static Scene CurrentScene => new Scene { handle = SceneHandle, valid = SceneValid };

    /// <summary>Creates a layer GameObject in the current scene era, as a world switch would.</summary>
    public static GameObject NewLayer(string name = "GameLayer")
    {
        GameObject layer = new GameObject(name);
        return layer;
    }

    public static GameObject NewActor(string name = "actor", Transform parent = null)
    {
        GameObject actor = new GameObject(name);
        if (parent != null) actor.transform.Parent = parent;
        return actor;
    }
}

// ---------------------------------------------------------------------------
// Mod-side surface used by the production bank files.
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
        public static ConfigEntry<bool> MusketeerEnabled = new();
        public static ConfigEntry<bool> AutoRestockMusketeersEnabled = new();
        public static ConfigEntry<int> AutoRestockMusketeersTarget = new() { Value = 15 };

        public static ConfigEntry<bool> AutoRestockFarmersEnabled = new ConfigEntry<bool>(false), AutoRestockCatapultBarrelsEnabled = new ConfigEntry<bool>(false), AutoRestockFireTowerAmmoEnabled = new ConfigEntry<bool>(false);
        public static ConfigEntry<int> AutoRestockFarmersTarget = new ConfigEntry<int>(15), AutoRestockCatapultBarrelsTarget = new ConfigEntry<int>(15), AutoRestockFireTowerAmmoTarget = new ConfigEntry<int>(15);
        public static ConfigEntry<bool> Enabled = new ConfigEntry<bool>(true);
        public static ConfigEntry<bool> AutoRestockWorkersEnabled = new ConfigEntry<bool>(false);
        public static ConfigEntry<bool> AutoRestockArchersEnabled = new ConfigEntry<bool>(false);
        public static ConfigEntry<bool> AutoRestockNinjasEnabled = new ConfigEntry<bool>(false);
        public static ConfigEntry<bool> AutoRestockBerserkersEnabled = new ConfigEntry<bool>(false);
        public static ConfigEntry<bool> AutoRestockPeasantsEnabled = new ConfigEntry<bool>(false);
        public static ConfigEntry<int> AutoRestockWorkersTarget = new ConfigEntry<int>(5);
        public static ConfigEntry<int> AutoRestockArchersTarget = new ConfigEntry<int>(5);
        public static ConfigEntry<int> AutoRestockNinjasTarget = new ConfigEntry<int>(5);
        public static ConfigEntry<int> AutoRestockBerserkersTarget = new ConfigEntry<int>(5);
        public static ConfigEntry<int> AutoRestockPeasantsTarget = new ConfigEntry<int>(5);
    }

    /// <summary>Fake counts cache: only the surface AutoRestock reads.</summary>
    internal static class AutoRestockCounts
    {
        private static readonly int[] Live = new int[5];
        private static readonly int[] Stock = new int[5];
        private static readonly List<PayableShop> ShopsCache = new List<PayableShop>();

        public static long Version { get; private set; }
        public static List<PayableShop> Shops => ShopsCache;
        public static int ResetCalls, RefreshCalls, HookCalls;
        public static bool RefreshResult = true;

        public static int LiveCount(int role) => role >= 0 && role < Live.Length ? Live[role] : 0;
        public static int StockCount(int role) => role >= 0 && role < Stock.Length ? Stock[role] : 0;
        public static int IncomingCount(int role) => 0;

        public static int ClassifyShop(PayableShop shop)
        {
            if (shop == null || shop.itemPrefab == null) return -1;
            if (shop is PayableShopBaker) return 4;
            string tag = shop.itemPrefab.name;
            if (tag == "Hammer") return 0;
            if (tag == "Bow") return 1;
            if (tag == "Katana") return 2;
            if (tag == "Axe") return 3;
            return -1;
        }

        public static bool Refresh(Managers managers) { RefreshCalls++; return RefreshResult; }
        public static void Reset() { ResetCalls++; }
        internal static void HookShopAddItem(PayableShop shop) { HookCalls++; }

        public static void SetLive(int role, int value) { Live[role] = value; }
        public static void SetStock(int role, int value) { Stock[role] = value; }
        public static void Clear()
        {
            Array.Clear(Live, 0, Live.Length);
            Array.Clear(Stock, 0, Stock.Length);
            ShopsCache.Clear();
            Version = 0; ResetCalls = RefreshCalls = HookCalls = 0; RefreshResult = true;
        }
    }
}

public class PayableShop : Payable
{
    public Droppable itemPrefab;
    public int maxItems = 99, _limitedNumItems = 99;
    public WorkableBuilding _workableBuilding = new WorkableBuilding();
    public int Items;
    public int GetItemCount() => Items;
    public override void TransactionComplete() { base.TransactionComplete(); Items++; }
}

public class PayableShopBaker : PayableShop { }

namespace KingdomEnhancedMod { internal static class GreekScaleScope { internal static Vector3 NativeScale(Transform t) => t.localScale; internal static void ApplyY(Transform t, float value) { } internal static void Restore(Transform t) { } } }

namespace KingdomEnhancedMod {
internal static class MusketeerIdentity { internal static bool TryGetRestockCounts(out int live,out int guns) { live=guns=0; return false; } }
internal static class MusketeerShop {
 internal const int Price=4; internal enum AutoPurchaseResult { Rejected,Purchased,PaidUncertain }
 internal static bool TryGetAutoRestockTarget(out Payable target) { target=null;return false; }
 internal static bool CanAutoRestock(Payable target,out string reason) { reason="disabled in bank scope fixture"; return false; }
 internal static AutoPurchaseResult PurchaseForAutoRestock(Payable target,Banker banker,System.Action onDebited,out string reason) { throw new System.InvalidOperationException("unexpected musketeer call in bank scope fixture"); }
} }
