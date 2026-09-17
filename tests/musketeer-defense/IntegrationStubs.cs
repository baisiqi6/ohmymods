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
    public int _guardDepth, Writes;
    public object GetFormation() => null;
    public bool ShouldPlayerControl() => false;
    public void SetGuardSide(Side side, int depth) { _guardSide = side; _guardDepth = depth; Writes++; }
}
public class Kingdom : UnityEngine.Component
{
    public List<Archer> _availableArchersCache = new();
    public List<Archer> Archers = new();
    public int DistributeCalls;
    public bool ThrowNative, Override;
    public bool HasOverrideGuardPosition() => Override;
    public KeyValuePair<Side, float> GetGuardPosition(Side side) => new(side, 20f * (float)side);
    public void DistributeFreeArchers()
    {
        bool owns = KingdomEnhancedMod.MusketeerDefense.Begin(this);
        DistributeCalls++;
        if (ThrowNative) throw new InvalidOperationException("native failure fixture");
        _availableArchersCache.Clear();
        // Models the native same-shop cluster being sent left, with ordinary archer right.
        // Native writes fields directly here so policy Writes measures marked-only changes.
        int left = 0, right = 0;
        foreach (var a in Archers)
        {
            if (!a.isAvailable) continue;
            _availableArchersCache.Add(a);
            a._guardSide = a.transform.position.x < 100 ? Side.Left : Side.Right;
            a._guardDepth = a._guardSide == Side.Left ? left++ : right++;
        }
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
}

