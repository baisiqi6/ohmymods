using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        private static long _next;
        public IntPtr Pointer = (IntPtr)(++_next);
    }

    public class GameObject : Object
    {
        public bool activeSelf = true;
        public bool activeInHierarchy = true;
        public Transform transform;
        private readonly Dictionary<Type, Component> _components = new();

        public T Add<T>(T value) where T : Component
        {
            value.gameObject = this;
            if (value.transform == null) value.transform = new Transform { gameObject = this };
            _components[typeof(T)] = value;
            return value;
        }
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
}

// ---- game boundary stubs (only the members GuardRankDistribution touches) ----

// Detector self-check fixture for the per-call snapshot regression: a static list of the same
// shape the production class must not have.
public static class StaticSnapshotProbe
{
    internal static List<Archer> Units = new();
}

public enum Side { Left = -1, Right = 1 }

public static class NetworkBigBoss
{
    public static bool HasWorldAuth = true;
    public static bool IsOnline;
}

public class World
{
    public UnityEngine.Transform gameLayer;
}

public class Managers
{
    public static Managers Inst;
    public World world;
    public Kingdom kingdom;
}

public class Kingdom : UnityEngine.Component
{
    public List<Archer> _availableArchersCache = new();
}

public class Archer : UnityEngine.Component
{
    private bool _available = true;
    public bool ThrowOnAvailable;
    public bool ThrowOnWrite;
    public bool ThrowAfterWrite;

    public bool isAvailable
    {
        get
        {
            if (ThrowOnAvailable) throw new InvalidOperationException("isAvailable fault fixture");
            return _available;
        }
        set => _available = value;
    }

    public Side _guardSide;
    public int _guardDepth;
    public readonly List<(Side Side, int Depth)> Writes = new();

    public void SetGuardSide(Side side, int depth)
    {
        if (ThrowOnWrite) throw new InvalidOperationException("setter fault fixture");
        _guardSide = side;
        _guardDepth = depth;
        Writes.Add((side, depth));
        if (ThrowAfterWrite) throw new InvalidOperationException("post-assign fault fixture");
    }
}

namespace KingdomEnhancedMod
{
    internal static class ModConfig
    {
        internal sealed class BoolEntry { internal bool Value = true; }
        internal static BoolEntry Enabled = new();
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
