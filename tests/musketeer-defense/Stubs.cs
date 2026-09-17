using System;
using System.Collections.Generic;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type declaringType, string methodName = null, Type[] argumentTypes = null) { }
    }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix : Attribute { }
}

namespace UnityEngine
{
    public class Object
    {
        private static long _next;
        public IntPtr Pointer = (IntPtr)(++_next);
        public static int DestroyCount;
        public static int FindCount;
        public static void Destroy(Object target) { DestroyCount++; }
        public static void FindObjectsOfType(Type type) { FindCount++; }
    }

    public class GameObject : Object
    {
        private static int _nextId;
        public int Id = ++_nextId;
        public bool activeInHierarchy = true;
        public bool activeSelf = true;
        public Transform transform;
        private readonly Dictionary<Type, Component> _components = new();

        public int GetInstanceID() => Id;

        public T Add<T>(T value) where T : Component
        {
            value.gameObject = this;
            if (value.transform == null) value.transform = new Transform { gameObject = this };
            _components[typeof(T)] = value;
            return value;
        }

        public T GetComponent<T>() where T : Component
            => _components.TryGetValue(typeof(T), out var component) ? (T)component : null;
    }

    public class Transform : Object
    {
        public GameObject gameObject;
        public Vector3 position;
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform;
    }

    public struct Vector3 { public float x, y, z; }

    public static class Time
    {
        public static int frameCount = 1;
        public static float unscaledTime = 100f;
    }
}

// ---- game boundary stubs (only the members the production file touches) ----

public enum Side { Left = -1, Right = 1 }

public class Sided<T>
{
    public T left, right;
    public T this[Side side]
    {
        get => side == Side.Left ? left : right;
        set { if (side == Side.Left) left = value; else right = value; }
    }
}

public class Managers : UnityEngine.Component
{
    public static Managers Inst;
    public Kingdom kingdom;
    public EnemyManager enemies;
}

public class EnemyManager : UnityEngine.Component
{
    public bool safeLeft, safeRight;
    public bool IsSideCompletelySafe(Side side) => side == Side.Left ? safeLeft : safeRight;
}

public class Kingdom : UnityEngine.Component
{
    public Sided<UnityEngine.GameObject> intactWall = new();
    public List<Archer> _availableArchersCache = new();
    public bool campaignOverride;
    public float campfirePosition = 50f, minKingdomExtents = 20f;
    public float fallbackOverride = float.NaN;
    public bool overrideGuard;
    public int DistributeCalls;
    public Action NativeDistribution;
    public bool HasOverrideGuardPosition() => overrideGuard;
    public KeyValuePair<Side, float> GetGuardPosition(Side side)
        => new(campaignOverride ? Side.Left : side, float.IsNaN(fallbackOverride)
            ? campfirePosition + minKingdomExtents * (float)side : fallbackOverride);
    public void DistributeFreeArchers()
    {
        DistributeCalls++;
        bool owns = KingdomEnhancedMod.MusketeerDefense.Begin(this);
        NativeDistribution?.Invoke();
        if (owns) KingdomEnhancedMod.MusketeerDefense.End(this);
    }
}

public class Formation : UnityEngine.Component { }
public class Knight : UnityEngine.Component { }
public class GuardSlot : UnityEngine.Component { public Archer archer; }
public class Character : UnityEngine.Component { public bool inert, grabbed, isStationary; }
public class Damageable : UnityEngine.Component { public bool isDead; }
public class Embarkee : UnityEngine.Component
{
    public bool IsEmbarked;
    public UnityEngine.GameObject EmbarkableTarget;
}

public class Archer : UnityEngine.Component
{
    private bool _isAvailable = true;
    public bool isAvailable
    {
        get => _isAvailable;
        set => _isAvailable = value;
    }
    public bool harmless;
    public bool playerControlled;
    public bool inGuardSlot;
    public GuardSlot _guardSlot;
    public Knight _knight;
    public Embarkee _embarkee;
    public Character _character;
    public Damageable _damageable;
    public Formation formation;
    public Side _guardSide;
    public int _guardDepth;
    public readonly List<(Side Side, int Depth)> Writes = new();

    public Formation GetFormation() => formation;
    public bool ShouldPlayerControl() => playerControlled;

    public void SetGuardSide(Side side, int depth)
    {
        Writes.Add((side, depth));
        _guardSide = side;
        _guardDepth = depth;
        KingdomEnhancedMod.Probe.TotalWrites++;
    }
}

namespace KingdomEnhancedMod
{
    internal static class Probe
    {
        internal static int TotalWrites;
    }

    internal static class MusketeerAccess
    {
        internal static bool Enabled = true;
        internal static UnityEngine.Transform World;
        internal static readonly HashSet<UnityEngine.Component> Foreign = new();
        internal static bool InWorld(UnityEngine.Component component)
            => component != null && !Foreign.Contains(component);
    }

    internal static class UnitScanCache
    {
        internal static Archer[] Archers = Array.Empty<Archer>();
        internal static int Calls;
        internal static Archer[] GetArchers(float maxAgeSec = 3f)
        {
            Calls++;
            return Archers;
        }
    }

    internal static class MusketeerIdentity
    {
        internal static readonly List<Archer> Registered = new();
        internal static readonly HashSet<Archer> NotUnits = new();
        internal static int CopyCalls;

        internal static void CopyUnits(List<Archer> destination)
        {
            CopyCalls++;
            destination.Clear();
            for (int i = 0; i < Registered.Count; i++) destination.Add(Registered[i]);
        }

        internal static bool IsUnit(Archer actor)
            => actor != null && Registered.Contains(actor) && !NotUnits.Contains(actor);
    }

    internal class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance = new();
        internal LogSource LogSource = new();
    }

    internal class LogSource
    {
        internal readonly List<string> Info = new();
        internal readonly List<string> Warn = new();
        internal void LogInfo(string message) => Info.Add(message);
        internal void LogWarning(string message) => Warn.Add(message);
    }
}
