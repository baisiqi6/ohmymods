using System;
using System.Collections.Generic;

namespace BepInEx { public static class Paths { public static string ConfigPath; } }

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type t, string n) { TargetType = t; TargetMethod = n; }
        public HarmonyPatch(Type t, string n, Type[] p) { TargetType = t; TargetMethod = n; }
        // Stored so the identity regression can pin which native boundaries are detoured.
        public Type TargetType; public string TargetMethod;
    }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyFinalizer : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPriority : Attribute { public HarmonyPriority(int i) { } }
    public static class Priority { public const int First = 800, Last = 0; }
}

namespace UnityEngine
{
    public class Object { static long counter; public IntPtr Pointer = (IntPtr)(++counter); }
    public class GameObject : Object
    {
        static int nextId;
        public int InstanceId = ++nextId;
        public int GetInstanceID() => InstanceId;
        private bool active = true; public bool ThrowOnActiveRead;
        public bool activeInHierarchy { get { if (ThrowOnActiveRead) throw new InvalidOperationException("active read"); return active; } set => active = value; }
        public string tag = "";
        public Scene scene; public Transform transform = new();
        readonly Dictionary<Type, Component> components = new();
        public T Add<T>(T c) where T : Component { c.gameObject = this; components[typeof(T)] = c; return c; }
        public T GetComponent<T>() where T : Component => components.TryGetValue(typeof(T), out var c) ? (T)c : null;
    }
    public class Component : Object
    {
        private GameObject _gameObject;
        public int GameObjectReads;
        public bool ThrowOnGameObjectRead;
        public int ThrowOnGameObjectReadAtCall;
        public GameObject gameObject
        {
            get
            {
                GameObjectReads++;
                if (ThrowOnGameObjectRead
                    || (ThrowOnGameObjectReadAtCall > 0 && GameObjectReads == ThrowOnGameObjectReadAtCall))
                    throw new InvalidOperationException("gameObject read");
                return _gameObject;
            }
            set => _gameObject = value;
        }
        public T GetComponent<T>() where T : Component => gameObject?.GetComponent<T>();
    }
    public struct Scene { public int handle; }
    public class Transform : Component { public float x, y; public bool IsChildOf(Transform layer) => KingdomEnhancedMod.MusketeerAccess.InWorldResult; }
    public struct Vector2
    {
        public float x, y;
        public static Vector2 zero => new Vector2();
    }
    public struct Vector3 { }
    public struct Quaternion { }
    public class Rigidbody2D : Component { public bool isKinematic; public Vector2 velocity; }
    public static class Time { public static float time = 100, unscaledTime = 100; public static int frameCount = 1; }
    public static class JsonUtility { public static string ToJson(IslandSaveData island, bool pretty) => island.Json; }
}

public interface IUnitController { }
public enum PickUpPolicy { Anybody = 0, Nobody = 1, OnlyClaimer = 2, AnybodyExceptDropper = 3, AnyPlayer = 4, Blocked = 5 }
public class Damageable { public bool isDead; }
public class Character : UnityEngine.Component
{
    public Damageable _damageable = new();
    public Character Promote(DroppableTool tool, IUnitController unitController) => this;
}
public class Archer : UnityEngine.Component { }
public class Droppable : UnityEngine.Component { public bool pickedUp; public UnityEngine.GameObject enemyClaimer; public UnityEngine.GameObject dropper; }
public class DroppableTool : Droppable { }
public class Persistent : UnityEngine.Component
{
    public string path;
    private bool _persistObject = true, _persistInactive, __persistInstance = true;
    public static int PersistObjectReads, PersistInactiveReads, PersistInstanceReads, ShouldPersistCalls;
    public bool ThrowOnRead;
    public bool persistObject
    {
        get { PersistObjectReads++; if (ThrowOnRead) throw new InvalidOperationException("persistObject read"); return _persistObject; }
        set => _persistObject = value;
    }
    public bool persistInactive
    {
        get { PersistInactiveReads++; if (ThrowOnRead) throw new InvalidOperationException("persistInactive read"); return _persistInactive; }
        set => _persistInactive = value;
    }
    // C# interop keeps the native field name verbatim; it is the field the native ShouldPersist
    // checks alongside persistObject/persistInactive.
    public bool _persistInstance
    {
        get { PersistInstanceReads++; if (ThrowOnRead) throw new InvalidOperationException("_persistInstance read"); return __persistInstance; }
        set => __persistInstance = value;
    }
    public bool ShouldPersist() { ShouldPersistCalls++; return false; }
}
public class Game
{
    public enum State { None = 0, SailingAway = 32 }
    public State state;
}
public class Boat : UnityEngine.Component { }
public class Kingdom { public Boat boat; }
public class Pool
{
    public UnityEngine.GameObject FastSpawn(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation, UnityEngine.Transform parent, short netID, bool syncReceipt) => null;
    public void FastDespawn(UnityEngine.GameObject clone, float delay, bool ignoreWarnings) { }
}
public class World { public UnityEngine.Transform gameLayer; }
public class Managers { public static Managers Inst; public World world; public Game game; public Kingdom kingdom; }
public class GlobalSaveData : UnityEngine.Object
{
    public static GlobalSaveData loaded;
    public static string filename = "global-v35";
    public int currentCampaign, currentChallenge;
    public readonly List<CampaignSaveData> campaigns = new();
    public readonly List<CampaignSaveData> challenges = new();
    public readonly PrefsSaveData prefs = new();
    // The fixture registers this explicitly; GetCurrentCampaign never derives an owner from slots.
    public CampaignSaveData CurrentCampaign;
    public CampaignSaveData GetCurrentCampaign() => CurrentCampaign;
}
public class PrefsSaveData : UnityEngine.Object { public readonly Dictionary<string, string> contents = new(); }
public class CampaignSaveData : UnityEngine.Object
{
    public static CampaignSaveData current;
    public int challengeId;
    public int CurrentLand;
    public IslandSaveData CurrentIsland;
    public readonly List<IslandSaveData> _islands = new();
    public void ApplyToScene() { }
}
public class IslandSaveData : UnityEngine.Object
{
    public static IslandSaveData CurrentlySavingIsland; public static bool isSavingGame;
    private static Il2CppSystem.Collections.Generic.HashSet<Persistent> _registeredPersistents = new();
    public static int RegisteredPersistentsReads, GetIdCalls;
    public static Il2CppSystem.Collections.Generic.HashSet<Persistent> registeredPersistents
    {
        get { RegisteredPersistentsReads++; return _registeredPersistents; }
        set => _registeredPersistents = value ?? new();
    }
    public int land; public string Json = "{}"; public bool isNew = true; public double playTimeDays;
    public List<ObjectData> objects = new();
    public static void Save(int campaign, int land, int challenge) { }
    public static string GetID(Persistent persistent) { GetIdCalls++; return ""; }
    public bool TryPopObjectsToScene() => true;
    public static Persistent TryCreateOrFind(ObjectData data) => null;
    public class ObjectData : UnityEngine.Object { public string uniqueID; }
}

namespace Il2CppSystem.Collections.Generic
{
    // Native-shaped HashSet subset used by the interop surface (Contains is a method, Count a
    // getter). Production diagnostics name this type directly so the fixture resolves it without
    // a project reference to Il2CppSystem.Core.
    public class HashSet<T>
    {
        private readonly System.Collections.Generic.HashSet<T> inner = new();
        public int Count => inner.Count;
        public bool Add(T item) => inner.Add(item);
        public bool Remove(T item) => inner.Remove(item);
        public void Clear() => inner.Clear();
        public bool Contains(T item) => inner.Contains(item);
    }
}

namespace KingdomEnhancedMod
{
    internal static class MusketeerDefense
    {
        internal static bool RedistributeAfterBindings() => true;
    }

    internal static class MusketeerAccess
    {
        internal static bool TrackAllowed = true;
        private static bool enabled = true, playing = true;
        internal static bool Enabled { get => TrackAllowed && enabled; set => enabled = value; }
        internal static bool Playing { get => Enabled && playing; set => playing = value; }
        internal static bool InWorldResult = true;
        internal static bool InWorld(UnityEngine.GameObject root) => InWorldResult && root != null;
        internal static bool InWorld(UnityEngine.Component component) => InWorldResult && component != null && component.gameObject != null;
    }

    internal class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance = new();
        internal Logger LogSource = new();
    }

    /// <summary>捕获日志行供断言（生产侧只调用 LogInfo / LogWarning / LogError）。</summary>
    internal class Logger
    {
        internal static readonly List<string> Lines = new();
        internal static string ThrowOnFragment;

        private static void Guard(string message)
        {
            if (ThrowOnFragment != null && message != null && message.Contains(ThrowOnFragment))
                throw new InvalidOperationException("logger fault");
        }

        internal void LogInfo(string message) { Guard(message); Lines.Add("I: " + message); }
        internal void LogWarning(string message) { Guard(message); Lines.Add("W: " + message); }
        internal void LogError(string message) { Guard(message); Lines.Add("E: " + message); }
    }

    /// <summary>
    /// 仅测试替身：MusketeerPersistence 现役 ApplyToScene postfix 末尾调用本模块确认入口；
    /// 与生产 `CoinCourierPersistence.EnsureBoundFromApplyToScene(CampaignSaveData)` 同签名 no-op。
    /// 待本工程改为链接真实持久化生产文件时同步删除（防双源漂移）；旧断言不受影响。
    /// </summary>
    internal static class CoinCourierPersistence
    {
        internal static void EnsureBoundFromApplyToScene(CampaignSaveData applied) { }
    }

    /// <summary>
    /// 中性 disabled 边界替身：本套件覆盖火枪手身份/存档接线，不覆盖共享银行账本算法
    /// （真实 SharedBankNative/R3 的 capture/save-gate 语义由 tests/coin-courier-economy 与
    /// tests/shared-bank-regressions 直接链接生产源验证）。这里只让 MusketeerPersistence 新增的
    /// 银行观察/装载接线保持同签名 no-op；绝不能被当作银行行为已在本套件被验证。
    /// Pop 令牌只把 BeginPop/EndPop 配对，不承载任何账本状态。
    /// </summary>
    internal static class SharedBankNative
    {
        internal sealed class Pop { }

        private static Pop _current;

        internal static Pop CurrentPop => _current;

        internal static void ObserveId(Persistent root, string id) { }

        internal static Pop BeginPop(IslandSaveData island) => _current = new Pop();

        internal static void EndPop(Pop pop, bool normal)
        {
            if (ReferenceEquals(_current, pop)) _current = null;
        }

        internal static void Created(IslandSaveData.ObjectData row, Persistent root) { }

        internal static void SceneApplied() { }
    }
}
