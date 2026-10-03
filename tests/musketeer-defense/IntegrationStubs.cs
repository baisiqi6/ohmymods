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
        public bool activeInHierarchy = true;
        public bool activeSelf = true;
        public Transform transform = new(); public Scene scene;
        public string tag = "";
        readonly Dictionary<Type, Component> components = new();
        public T Add<T>(T c) where T : Component { c.gameObject = this; components[typeof(T)] = c; return c; }
        public T GetComponent<T>() where T : Component => components.TryGetValue(typeof(T), out var c) ? (T)c : null;
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject?.transform;
        public T GetComponent<T>() where T : Component => gameObject?.GetComponent<T>();
    }
    public struct Scene { public int handle; }
    public class Transform : Component { public float x, y; public Vector3 position; public bool IsChildOf(Transform layer) => KingdomEnhancedMod.MusketeerAccess.InWorldResult; }
    public struct Vector2
    {
        public float x, y;
        public static Vector2 zero => new Vector2();
    }
    public struct Vector3 { public float x, y, z; }
    public struct Quaternion { }
    public class Rigidbody2D : Component { public bool isKinematic; public Vector2 velocity; }
    public static class Time { public static float time = 100, unscaledTime = 100; public static int frameCount = 1; }
    public static class JsonUtility { public static string ToJson(IslandSaveData island, bool pretty) => island.Json; }
}

public interface IUnitController { }
public enum PickUpPolicy { Anybody = 0, Nobody = 1, OnlyClaimer = 2, AnybodyExceptDropper = 3, AnyPlayer = 4, Blocked = 5 }
public class Character : UnityEngine.Component
{
    public Damageable _damageable = new();
    public bool inert, grabbed, isStationary;
    public Character Promote(DroppableTool tool, IUnitController unitController) => this;
}
public enum Side { Left = -1, Right = 1 }
public class Damageable : UnityEngine.Component { public bool isDead; }
public class Embarkee : UnityEngine.Component { public bool IsEmbarked; public UnityEngine.GameObject EmbarkableTarget; }
public class Archer : UnityEngine.Component
{
    public bool isAvailable = true, harmless, inGuardSlot;
    public object _knight, _guardSlot;
    public Embarkee _embarkee;
    public Character _character;
    public Damageable _damageable = new();
    public Side _guardSide;
    public int _guardDepth;
    public readonly List<(Side Side, int Depth)> Writes = new();
    public object GetFormation() => null;
    public bool ShouldPlayerControl() => false;
    public void SetGuardSide(Side side, int depth) { _guardSide = side; _guardDepth = depth; Writes.Add((side, depth)); }
}
public static class NetworkBigBoss
{
    public static bool HasWorldAuth = true;
    public static bool IsOnline;
}

public class Kingdom : UnityEngine.Component
{
    public List<Archer> _availableArchersCache = new();
    public List<Archer> Archers = new();
    public int DistributeCalls;
    public bool ThrowNative, Override;
    // Modeled knight followers: the native quota mixes their counts into the right counter
    // (issue-78 evidence). FollowerLeft/FollowerRight = 4/4 and 40/40 reproduce the measured
    // free20 vectors; zero keeps legacy scenarios on a healthy counter.
    public int FollowerLeft, FollowerRight;
    public int LastNativeMinDepth = int.MaxValue;
    public bool HasOverrideGuardPosition() => Override;
    public KeyValuePair<Side, float> GetGuardPosition(Side side) => new(side, 20f * (float)side);
    public void DistributeFreeArchers()
    {
        bool owns = KingdomEnhancedMod.MusketeerDefense.Begin(this);
        DistributeCalls++;
        _availableArchersCache.Clear();
        foreach (var a in Archers)
        {
            if (!a.isAvailable) continue;
            _availableArchersCache.Add(a);
        }
        // Side placement stands in for the native position/safety decisions (same-shop cluster
        // left, ordinary archer right); the depth counters mirror the measured 2.4 arithmetic
        // (RVA 0x59D0B1-0x59D1D9): left starts at 0, right starts at freeCount - quota with
        // quota = max((total - leftFollowers + rightFollowers) / 2, 0), and the right counter
        // decrements. Native writes fields directly here so policy Writes counts only the hooks.
        int free = _availableArchersCache.Count;
        int total = free + FollowerLeft + FollowerRight;
        int quota = Math.Max((total - FollowerLeft + FollowerRight) / 2, 0);
        int rightCounter = free - quota;
        int leftCounter = 0;
        LastNativeMinDepth = int.MaxValue;
        for (int i = 0; i < free; i++)
        {
            var a = _availableArchersCache[i];
            a._guardSide = a.transform.position.x < 100 ? Side.Left : Side.Right;
            a._guardDepth = a._guardSide == Side.Left ? leftCounter++ : --rightCounter;
            if (a._guardDepth < LastNativeMinDepth) LastNativeMinDepth = a._guardDepth;
        }
        // A native throw skips the Harmony postfix entirely: neither A nor End runs here.
        if (ThrowNative) throw new InvalidOperationException("native failure fixture");
        KingdomEnhancedMod.GuardRankDistribution.ReindexAfterNative(this);   // A boundary
        if (owns) KingdomEnhancedMod.MusketeerDefense.End(this);
    }
}
public class Droppable : UnityEngine.Component { public bool pickedUp; public UnityEngine.GameObject enemyClaimer; public UnityEngine.GameObject dropper; }
public class DroppableTool : Droppable { }
public class Persistent : UnityEngine.Component { }
public class Pool
{
    public UnityEngine.GameObject FastSpawn(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation, UnityEngine.Transform parent, short netID, bool syncReceipt) => null;
    public void FastDespawn(UnityEngine.GameObject clone, float delay, bool ignoreWarnings) { }
}
public class World { public UnityEngine.Transform gameLayer; }
public class Managers { public static Managers Inst; public World world; public Kingdom kingdom; }
public class GlobalSaveData { public static GlobalSaveData loaded; public static string filename = "global-v35"; public int currentCampaign, currentChallenge; }
public class CampaignSaveData : UnityEngine.Object { public static CampaignSaveData current; public IslandSaveData CurrentIsland; public void ApplyToScene() { } }
public class IslandSaveData : UnityEngine.Object
{
    public static IslandSaveData CurrentlySavingIsland; public static bool isSavingGame;
    public int land; public string Json = "{}"; public bool isNew = true; public double playTimeDays;
    public List<ObjectData> objects = new();
    public static void Save(int campaign, int land, int challenge) { }
    public static string GetID(Persistent persistent) => "";
    public bool TryPopObjectsToScene() => true;
    public static Persistent TryCreateOrFind(ObjectData data) => null;
    public class ObjectData : UnityEngine.Object { public string uniqueID; }
}

namespace KingdomEnhancedMod
{
    internal static class ModConfig
    {
        internal sealed class BoolEntry { internal bool Value = true; }
        internal static BoolEntry Enabled = new();
    }

    internal static class MusketeerAccess
    {
        internal static bool TrackAllowed = true;
        internal static UnityEngine.Transform World => Managers.Inst?.world?.gameLayer;
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

    internal class Logger { internal void LogInfo(string message) { } internal void LogWarning(string message) { } }

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
    /// 中性 disabled 边界替身：本套件覆盖火枪手防御/身份接线，不覆盖共享银行账本算法
    /// （真实 SharedBankNative/R3 由 tests/coin-courier-economy 与 tests/shared-bank-regressions
    /// 直接链接生产源验证）。这里只让 MusketeerPersistence 新增的银行观察/装载接线保持同签名
    /// no-op；绝不能被当作银行行为已在本套件被验证。Pop 令牌只把 BeginPop/EndPop 配对。
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
