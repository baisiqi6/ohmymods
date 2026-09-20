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
        public GameObject gameObject;
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
public class Persistent : UnityEngine.Component { }
public class Pool
{
    public UnityEngine.GameObject FastSpawn(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation, UnityEngine.Transform parent, short netID, bool syncReceipt) => null;
    public void FastDespawn(UnityEngine.GameObject clone, float delay, bool ignoreWarnings) { }
}
public class World { public UnityEngine.Transform gameLayer; }
public class Managers { public static Managers Inst; public World world; }
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

        internal void LogInfo(string message) { Lines.Add("I: " + message); }
        internal void LogWarning(string message) { Lines.Add("W: " + message); }
        internal void LogError(string message) { Lines.Add("E: " + message); }
    }
}
