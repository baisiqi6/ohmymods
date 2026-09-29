// Minimal stand-ins so tests/wall-engineers can compile the real production files without the game
// assembly. Shapes mirror the actual 2.4 interop surface: outerWall values are GameObjects with a
// Wall component, worker.behaviour is an IHaglet with the Haglet cast, Damageable uses isDead.
using System;
using System.Reflection;
using System.Threading;

namespace Il2CppInterop.Runtime.InteropTypes
{
    public class Il2CppObjectBase
    {
        private static int _nextPointer = 0x1000;
        // Every wrapper gets a non-zero, unique identity unless a test aliases it explicitly.
        public IntPtr Pointer = (IntPtr)Interlocked.Increment(ref _nextPointer);

        public T Cast<T>() where T : class => this as T;
    }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppReferenceArray<T>
    {
        private readonly T[] _items;
        public Il2CppReferenceArray(int length) { _items = new T[length]; }
        public int Count => _items.Length;
        public T this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }
        public T[] Items => _items;
    }
}

namespace Il2CppSystem.Collections.Generic
{
    public class List<T>
    {
        private readonly System.Collections.Generic.List<T> _items = new();
        public int Count => _items.Count;
        public T this[int index] => _items[index];
        public void Clear() => _items.Clear();
        public void Add(T item) => _items.Add(item);
    }
}

namespace Coatsink.Common
{
    // Shape of the actual IHaglet/Haglet pair (interfaces are approximated as a base class so the
    // production `behaviour.Cast<Haglet>()` call compiles exactly as it does in the real interop).
    public class IHaglet : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase { }

    public class Haglet : IHaglet
    {
        public bool started = true;
        public int latestGoto;
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
        public static Vector3 zero => new Vector3(0, 0, 0);
    }

    public struct Bounds
    {
        public Vector3 min, max;
        public Bounds(Vector3 min, Vector3 max) { this.min = min; this.max = max; }
    }

    public struct Scene { public int handle; }

    public class Object : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        private static int _nextId = 1000;
        private readonly int _id = _nextId++;
        public int GetInstanceID() => _id;
    }

    public class GameObject : Object
    {
        public bool activeInHierarchy = true;
        public Scene scene;
        public int layer;
        public readonly System.Collections.Generic.List<object> Components = new();

        public T GetComponent<T>() where T : class
        {
            for (int i = 0; i < Components.Count; i++) if (Components[i] is T typed) return typed;
            return null;
        }
    }

    public class Transform : Object
    {
        public Vector3 position;
        public Vector3 localScale = new Vector3(1f, 1f, 1f);
        public Transform parent;
        public GameObject gameObject = new GameObject();

        public bool IsChildOf(Transform other)
        {
            Transform current = this;
            while (current != null)
            {
                if (ReferenceEquals(current, other)) return true;
                current = current.parent;
            }
            return false;
        }
    }

    public class Component : Object
    {
        public GameObject gameObject = new GameObject();
        public Transform transform = new Transform();
    }

    public class MonoBehaviour : Component { }

    public class Collider2D : Component
    {
        public Bounds bounds;
    }

    public struct LayerMask { public int value; }

    public struct ContactFilter2D
    {
        public bool useTriggers, useLayerMask, useDepth, useOutsideDepth, useNormalAngle, useOutsideNormalAngle;
        public LayerMask layerMask;
        public float minDepth, maxDepth, minNormalAngle, maxNormalAngle;
    }

    public static class Physics2D
    {
        public static Il2CppSystem.Collections.Generic.List<Collider2D> NextResults;
        public static int Calls;
        public static int? ForceCount;

        public static int OverlapArea(Vector2 a, Vector2 b, ContactFilter2D filter, Il2CppSystem.Collections.Generic.List<Collider2D> results)
        {
            Calls++;
            results.Clear();
            if (NextResults != null)
                for (int i = 0; i < NextResults.Count; i++) results.Add(NextResults[i]);
            return ForceCount ?? results.Count;
        }
    }

    public static class Time { public static float time; }
}

namespace KingdomEnhancedMod
{
    using Coatsink.Common;
    using Il2CppInterop.Runtime.InteropTypes;
    using UnityEngine;

    public sealed class ConfigEntry<T>
    {
        public T Value;
    }

    internal static class ModConfig
    {
        public static ConfigEntry<bool> Enabled = new ConfigEntry<bool>();
    }

    public class LogSource
    {
        public readonly System.Collections.Generic.List<string> Lines = new();
        public void LogWarning(string message) => Lines.Add(message);
        public void LogInfo(string message) => Lines.Add(message);
    }

    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance;
        public LogSource LogSource = new LogSource();
    }

    public class Damageable : MonoBehaviour
    {
        public int initialHitPoints;
        public int hitPoints;
        public bool isDead;
        public bool useHitPoints = true;
    }

    public class WorkableBuilding : MonoBehaviour
    {
        public bool UnderConstruction;
    }

    public class Workable : Il2CppObjectBase { }

    public class Wall : Workable
    {
        public Damageable _damageable;
        public WorkableBuilding _workableBuilding = new WorkableBuilding();
        public float MinimumIntactRatio = -1f;   // negative = always intact (tests opt in per case)
        public int UpdateGraphicsCalls;
        public float JobOffset = -0.5f;
        public GameObject gameObject = new GameObject();
        public Transform transform = new Transform();

        public bool isIntact
        {
            get
            {
                if (_damageable == null) return false;
                if (MinimumIntactRatio < 0f) return true;
                return _damageable.hitPoints >= MinimumIntactRatio * _damageable.initialHitPoints;
            }
        }

        public int GetTotalMaxHitPoints()
            => _damageable == null ? 0 : _damageable.initialHitPoints;

        public bool JobAvailable() => _damageable != null && _damageable.hitPoints < GetTotalMaxHitPoints();

        public void UpdateGraphics() => UpdateGraphicsCalls++;

        public float GetJobOffset(Worker worker) => JobOffset;
    }

    public class Scanner : Il2CppObjectBase
    {
        public Transform observer = new Transform();
        public float range = 10f;
        public float rangeBehind = 2f;
        public float _height = 1f;
        public float _lastRefresh;
        public ContactFilter2D _contactFilter;
        public static Vector2 _pointA, _pointB;
        public static int ComputeCornersCalls;
        public static bool ComputeCornersThrow;
        public static bool UseMovingCorners;

        public static void ComputeCorners(Transform observer, float range, float rangeBehind, float height, bool flag)
        {
            ComputeCornersCalls++;
            if (ComputeCornersThrow) throw new InvalidOperationException("corners");
            if (UseMovingCorners)
            {
                _pointA = new Vector2(observer.position.x + range * observer.localScale.x, observer.position.y + height);
                _pointB = new Vector2(observer.position.x - rangeBehind * observer.localScale.x, 0.5f);
                return;
            }
            _pointA = new Vector2(observer.position.x - 1f, -2f);
            _pointB = new Vector2(observer.position.x + 1f, 2f);
        }
    }

    public class Worker : Il2CppObjectBase
    {
        public Scanner _enemyScanner = new Scanner();
        public Workable _currentWork;
        public Workable _queuedWork;
        public IHaglet behaviour = new Haglet();
        public int ResetWorkCalls;
        public bool KeepWorkSeen;
        public GameObject gameObject = new GameObject();
        public Transform transform = new Transform();

        public void ResetWorkState(bool keepWork)
        {
            ResetWorkCalls++;
            KeepWorkSeen |= keepWork;
            if (!keepWork) { _currentWork = null; _queuedWork = null; }
        }
    }

    public class WorkersCollection
    {
        public readonly System.Collections.Generic.List<Worker> Items = new();
        public int Count => Items.Count;
        public void CopyTo(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Worker> array, int index)
        {
            for (int i = 0; i < Items.Count && index + i < array.Count; i++) array[index + i] = Items[i];
        }
    }

    public enum Side { Left = 0, Right = 1 }

    // Actual shape: the map stores GameObjects; the runtime resolves the Wall component itself.
    public sealed class OuterWallMap
    {
        private readonly GameObject[] _walls = new GameObject[2];
        public GameObject this[Side side]
        {
            get => _walls[(int)side];
            set => _walls[(int)side] = value;
        }
    }

    public class Kingdom : Il2CppObjectBase
    {
        public WorkersCollection Workers = new WorkersCollection();
        public OuterWallMap outerWall = new OuterWallMap();
        public readonly System.Collections.Generic.HashSet<Workable> Jobs = new();
        public int AddWorkCalls, RemoveWorkCalls, ReassignCalls;
        public bool AddWorkThrow, PostRegisterThrow, ReassignThrow;
        public bool NativeRegisterOnRemove;
        public bool InsideWalls = true;
        public bool isSafe;

        public bool IsWithinWalls(float x) => InsideWalls;

        // Actual shape: one HashSet membership plus an ActorTracker scheduling request.
        public void AddWork(Workable workable)
        {
            AddWorkCalls++;
            if (AddWorkThrow) throw new InvalidOperationException("AddWork");
            Jobs.Add(workable);
            if (PostRegisterThrow) throw new InvalidOperationException("AddWork-post");
        }

        public void RemoveWork(Workable workable)
        {
            RemoveWorkCalls++;
            Jobs.Remove(workable);
            if (NativeRegisterOnRemove) Jobs.Add(workable); // native event wins before handoff membership check
        }

        public bool CheckWork(Workable workable) => Jobs.Contains(workable);

        public void ReassignWork()
        {
            ReassignCalls++;
            if (ReassignThrow) throw new InvalidOperationException("Reassign");
        }

        public void AddWorker(Worker worker) { }
        public void RemoveWorker(Worker worker) { }
        public void AddWall(Wall wall) { }
        public void DestroyWall(Wall wall) { }
        public void ReplaceWall(Wall wall) { }

        public int BorderCalls;
        public bool BorderThrow;
        public void CalculateBordersNow()
        {
            BorderCalls++;
            if (BorderThrow) throw new InvalidOperationException("borders");
        }
    }

    public class World : Il2CppObjectBase
    {
        public Transform gameLayer = new Transform();
    }

    public class Game : Il2CppObjectBase
    {
        public enum State { Playing, Menu, NetworkClientPlaying }
        public State state = State.Menu;
    }

    public class Managers : Il2CppObjectBase
    {
        public static Managers Inst;
        public Kingdom kingdom;
        public World world;
        public Game game;
    }

    public static class NetworkBigBoss
    {
        public static bool IsOnline;
        public static bool HasWorldAuth = true;
    }

    public class IslandSaveData : Il2CppObjectBase
    {
        public static bool poppingObjectsToScene;
        public bool TryPopObjectsToScene() => true;
    }

    public class CampaignSaveData : Il2CppObjectBase
    {
        public static CampaignSaveData current;
        public IslandSaveData CurrentIsland;
        public void ApplyToScene() { }
    }
}

namespace HarmonyLib
{
    using System;

    [AttributeUsage(AttributeTargets.Class)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type declaringType, string methodName) { }
    }

    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyFinalizer : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPriority : Attribute { public HarmonyPriority(Priority priority) { } }
    public enum Priority { First, Last }
}
