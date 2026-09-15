using System;
using System.Collections.Generic;
using KingdomArcherOptions.Combat.Tests;

namespace UnityEngine
{
    public class Object : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        private static int _nextInstanceId = 1;

        internal readonly int InstanceId = _nextInstanceId++;

        public string name = string.Empty;

        /// <summary>Test-only: models a freed native object, which compares equal to null in Unity.</summary>
        internal bool DestroyedForTests;

        internal void DestroyForTests() => DestroyedForTests = true;

        public bool ThrowOnInstanceIdRead;
        public int GetInstanceID() => ThrowOnInstanceIdRead ? throw new InvalidOperationException("instance ID temporarily unavailable") : InstanceId;

        public static bool operator ==(Object a, Object b)
        {
            bool aNull = ReferenceEquals(a, null) || a.DestroyedForTests;
            bool bNull = ReferenceEquals(b, null) || b.DestroyedForTests;
            if (aNull || bNull) return aNull && bNull;
            return ReferenceEquals(a, b);
        }

        public static bool operator !=(Object a, Object b) => !(a == b);

        public override bool Equals(object other) => other is Object o ? this == o : ReferenceEquals(this, other);

        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }

    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public float magnitude => Mathf.Sqrt(x * x + y * y);

        public static Vector2 zero => new Vector2(0f, 0f);

        public override string ToString() => "(" + x + ", " + y + ")";
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 zero => new Vector3(0f, 0f, 0f);
    }

    public struct Quaternion
    {
        public static Quaternion identity => default;
    }

    public static class Mathf
    {
        public const float Deg2Rad = 0.0174532924f;

        public static float Abs(float value) => Math.Abs(value);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);
        public static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);
        public static float Cos(float radians) => (float)Math.Cos(radians);
        public static float Sin(float radians) => (float)Math.Sin(radians);
        public static float Sqrt(float value) => (float)Math.Sqrt(value);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
    }

    public static class Time
    {
        public static float deltaTime = 1f / 60f;
        public static float unscaledTime;
        public static float timeScale = 1f;
        public static int frameCount;
    }

    public enum ForceMode2D
    {
        Force = 0,
        Impulse = 1
    }

    public struct Scene
    {
        public int handle;
    }

    public class Component : Object
    {
        internal GameObject Owner;

        public GameObject gameObject => Owner;

        public Transform transform => Owner != null ? Owner.transform : null;

        public string tag => Owner?.tag;
        public T GetComponent<T>() where T : class => Owner != null ? Owner.GetComponent<T>() : null;
        public T GetComponentInParent<T>() where T : class => Owner?.GetComponentInParent<T>();

        public bool TryGetComponent<T>(out T component) where T : class
        {
            component = GetComponent<T>();
            return component != null;
        }
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
    }

    public class MonoBehaviour : Behaviour
    {
    }

    public class ScriptableObject : Object
    {
    }

    public class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();

        public bool activeSelf = true;
        public int layer;
        public string tag = "Untagged";
        public bool CompareTag(string value) => tag == value;
        public T GetComponentInParent<T>() where T : class
        {
            for (Transform cur = transform; cur != null; cur = cur.parent)
            { T found = cur.gameObject.GetComponent<T>(); if (found != null) return found; }
            return null;
        }

        public Transform transform;

        public Scene scene;

        public GameObject(string name = "go")
        {
            this.name = name;
            transform = new Transform(this);
            scene = new Scene { handle = 0 };
        }

        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);

        public T AddComponent<T>() where T : Component, new()
        {
            T component = new T { Owner = this };
            Components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : class
        {
            for (int i = 0; i < Components.Count; i++)
                if (Components[i] is T match) return match;
            return null;
        }

        public bool TryGetComponent<T>(out T component) where T : class
        {
            component = GetComponent<T>();
            return component != null;
        }

        public void SetActive(bool value) => activeSelf = value;
    }

    public class Transform : Component
    {
        private readonly List<Transform> _children = new List<Transform>();

        public Transform parent;

        public Vector3 position;

        internal Transform(GameObject owner)
        {
            Owner = owner;
            owner.transform = this;
        }

        public void SetParent(Transform newParent)
        {
            if (parent != null) parent._children.Remove(this);
            parent = newParent;
            if (newParent != null) newParent._children.Add(this);
        }

        public bool IsChildOf(Transform candidate)
        {
            for (Transform current = parent; current != null; current = current.parent)
                if (ReferenceEquals(current, candidate)) return true;
            return false;
        }
    }

    public class Rigidbody2D : Component
    {
        public Vector2 velocity;
        public float angularVelocity;
        public float gravityScale = 1f;

        /// <summary>Every impulse the test-side native body or the production module applied.</summary>
        public readonly List<Vector2> Impulses = new List<Vector2>();

        public void AddForce(Vector2 force, ForceMode2D mode)
        {
            Impulses.Add(force);
            FakeOps.Add("force");
        }
    }

    /// <summary>
    /// Fake pool with the native layout the module gates on: <c>capacity</c>, <c>_total</c>,
    /// <c>_cache</c> (inactive instances). Identity-stable reuse, bounded capacity.
    /// </summary>
    public class Pool : MonoBehaviour
    {
        private static readonly Dictionary<GameObject, Pool> Registry = new Dictionary<GameObject, Pool>();
        private static readonly List<Pool> All = new List<Pool>();

        internal Func<Component> Factory;
        internal readonly List<Component> Active = new List<Component>();

        /// <summary>Every instance handed out by Spawn, in order (reused instances repeat).</summary>
        internal readonly List<Component> HandedOut = new List<Component>();

        private readonly Dictionary<GameObject, Component> _componentByGo = new Dictionary<GameObject, Component>();

        public int capacity = int.MaxValue;
        public int _total;
        public Il2CppSystem.Collections.Generic.List<GameObject> _cache = new Il2CppSystem.Collections.Generic.List<GameObject>();

        public static void RegisterForTests(GameObject prefab, Func<Component> factory, int capacity = int.MaxValue)
        {
            Pool pool = new Pool { Factory = factory, capacity = capacity };
            Registry[prefab] = pool;
            All.Add(pool);
        }

        public static void ClearForTests()
        {
            Registry.Clear();
            All.Clear();
        }

        public static Pool GetPoolFromPrefabAsset(GameObject prefab)
        {
            Pool pool;
            return prefab != null && Registry.TryGetValue(prefab, out pool) ? pool : null;
        }

        /// <summary>Resolves the pool the same way native SpawnGO does (biome swap first).</summary>
        internal static Pool ForSpawn(GameObject prefabGo)
        {
            GameObject resolved = BiomeData.Current != null && prefabGo != null
                ? BiomeData.Current.GetAssetSwapForThis<GameObject>(prefabGo)
                : prefabGo;
            return GetPoolFromPrefabAsset(resolved);
        }

        public static T Spawn<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent, bool assertNonNullPrefab) where T : Component
        {
            Pool pool = ForSpawn(prefab != null ? prefab.gameObject : null);
            if (pool == null) return null;

            Component instance = pool.Take();
            if (instance == null) return null;

            instance.transform.position = position;
            instance.gameObject.activeSelf = true;
            PatchBridge.RaiseNativeOnEnable(instance);   // Unity activation -> OnEnable -> Harmony prefix
            return instance as T;
        }

        private Component Take()
        {
            GameObject go = null;
            if (_cache.Count > 0)
            {
                go = _cache[_cache.Count - 1];
                _cache.RemoveAt(_cache.Count - 1);
            }
            if (go == null)
            {
                if (_total >= capacity) return null;
                _total++;
                Component fresh = Factory();
                go = fresh.gameObject;
                _componentByGo[go] = fresh;
            }
            Component instance;
            if (!_componentByGo.TryGetValue(go, out instance)) instance = go.GetComponent<Arrow>();
            Active.Add(instance);
            HandedOut.Add(instance);
            FakeOps.Add("spawn");
            return instance;
        }

        /// <summary>Model Pool.Despawn: object returns to the pool cache inactive, identity unchanged.</summary>
        internal static void DespawnForTests(Component instance)
        {
            for (int i = 0; i < All.Count; i++)
            {
                Pool pool = All[i];
                if (pool.Active.Remove(instance))
                {
                    pool._cache.Add(instance.gameObject);
                    break;
                }
            }
            instance.gameObject.activeSelf = false;
            FakeOps.Add("despawn");
        }

        internal static Pool ForPrefab(GameObject prefab)
        {
            Pool pool;
            return Registry.TryGetValue(prefab, out pool) ? pool : null;
        }
    }
}
