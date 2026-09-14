// UnityDoubles.cs — Unity / Il2CppInterop / Harmony boundary doubles for the shop-cleanup
// regression. They exist so the UNMODIFIED production files
// (il2cpp/PatchRoles_Castle.cs, il2cpp/ShopCleanupQueue.cs) can be compiled and driven
// deterministically; they model only the boundary behavior those files rely on.
//
// Models that matter for this regression:
// - UnityEngine.Object implements Unity's fake-null: a destroyed wrapper == null. The
//   production queue uses that to detect "the object is really gone".
// - Destroy is DEFERRED (Unity's frame-end destruction): the object stays alive for the rest
//   of the frame, and every component's OnDestroy runs when the destruction is flushed.
//   Object.DestroyImmediate is deliberately NOT defined anywhere in this project, so the
//   production code cannot call the illegal immediate API without failing to compile.
// - GameObject.Find is a counted global name lookup (no production code may add new ones).
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
    /// <summary>Stand-in for the interop array base (Length + indexer, as production uses it).</summary>
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

    /// <summary>2.4 exposes ShopPlanner._placedShops as Il2CppReferenceArray&lt;GameObject&gt;.</summary>
    public class Il2CppReferenceArray<T> : Il2CppArrayBase<T> where T : class
    {
        public Il2CppReferenceArray(int length) : base(length) { }
        public Il2CppReferenceArray(T[] items) : base(items) { }
    }
}

namespace Il2CppSystem
{
    /// <summary>Interop wrapper for Side?: production only constructs it and forwards it.</summary>
    public struct Nullable<T> where T : struct
    {
        private readonly T _value;

        public Nullable(T value) { _value = value; HasValue = true; }
        public bool HasValue { get; }
        public T Value => _value;
        public override string ToString() => HasValue ? _value.ToString() : "";
    }
}

namespace Il2CppSystem.Collections.Generic
{
    /// <summary>Il2Cpp dictionary surface production touches (ContainsKey / indexer / Add).</summary>
    public class Dictionary<K, V>
    {
        private readonly System.Collections.Generic.Dictionary<K, V> _inner =
            new System.Collections.Generic.Dictionary<K, V>();

        public int Count => _inner.Count;
        public V this[K key] { get => _inner[key]; set => _inner[key] = value; }
        public bool ContainsKey(K key) => _inner.ContainsKey(key);
        public bool TryGetValue(K key, out V value) => _inner.TryGetValue(key, out value);
        public void Add(K key, V value) => _inner[key] = value;
        public bool Remove(K key) => _inner.Remove(key);
    }

    /// <summary>Il2Cpp list surface production touches (Add/Remove/Contains/foreach/Count).</summary>
    public class List<T>
    {
        private readonly System.Collections.Generic.List<T> _inner = new System.Collections.Generic.List<T>();

        public int Count => _inner.Count;
        public T this[int index] { get => _inner[index]; set => _inner[index] = value; }
        public void Add(T item) => _inner.Add(item);
        public bool Remove(T item) => _inner.Remove(item);
        public bool Contains(T item) => _inner.Contains(item);
        public IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();
        public void Clear() => _inner.Clear();
    }
}

namespace HarmonyLib
{
    // Attribute stand-ins: the tests drive the patched production methods directly, so these
    // only need to accept the shapes production uses. Production uses typeof(...) and
    // nameof(...) forms, so the single-string ctor is required.
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type) { }
        public HarmonyPatch(string methodName) { }
        public HarmonyPatch(Type type, string method) { }
        public HarmonyPatch(Type type, string method, Type[] argumentTypes) { }
    }

    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPostfix : Attribute { }
}

namespace UnityEngine
{
    public struct Scene
    {
        public int handle;
        public bool valid;
        public bool IsValid() => valid;
    }

    /// <summary>Only the clock the cleanup backoff reads.</summary>
    public static class Time
    {
        public static float time;
        public static float unscaledTime;
        public static float deltaTime;
    }

    public class Transform : Component
    {
        public Transform Parent;

        /// <summary>Only parentage is needed here (production parents level objects under gameLayer).</summary>
        public bool IsChildOf(Transform parent)
        {
            for (Transform current = this; current != null; current = current.Parent)
                if (ReferenceEquals(current, parent)) return true;
            return false;
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
        public override string ToString() => name;

        /// <summary>Unity's deferred destroy: the wrapper stays non-null until the flush.</summary>
        public static void Destroy(Object target)
        {
            if (target == null) return;
            Sim.EnqueueDestroy(target);
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;

        public Transform transform => gameObject != null ? gameObject.transform : null;

        public T GetComponent<T>() where T : Component
            => gameObject != null ? gameObject.GetComponent<T>() : null;

        public T[] GetComponentsInChildren<T>() where T : Component
        {
            var found = new List<T>();
            if (gameObject == null) return found.ToArray();
            for (int i = 0; i < gameObject.Components.Count; i++)
            {
                Component component = gameObject.Components[i];
                if (component.Alive && component is T match) found.Add(match);
            }
            return found.ToArray();
        }

        public bool CompareTag(string tag) => gameObject != null && gameObject.Tag == tag;
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject != null && gameObject.activeInHierarchy;
    }

    public class MonoBehaviour : Behaviour { }

    public class GameObject : Object
    {
        public readonly List<Component> Components = new List<Component>();
        private readonly Transform _transform;
        public bool ActiveSelf = true;
        public Scene scene;
        public string Tag = "";

        public GameObject(string name = "object")
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

        public bool activeSelf => ActiveSelf;

        public void SetActive(bool value)
        {
            SetActiveCalls++;
            Func<Exception> fault = SetActiveFault;
            if (fault != null) throw fault(); // 失败不生效：production 必须观察到仍 active 而不敢销毁
            ActiveSelf = value;
            // 原生 OnDisable 之类的外部回调：可能改 item/tag/scene，甚至把它重新登记回 planner。
            Action<GameObject> callback = SetActiveCallback;
            if (callback != null) callback(this);
        }

        /// <summary>Test injection: the next SetActive call throws, mirroring a native fault.</summary>
        public static Func<Exception> SetActiveFault;
        /// <summary>Test injection: an external callback after a successful SetActive (OnDisable-like).</summary>
        public static Action<GameObject> SetActiveCallback;
        public static int SetActiveCalls;

        public T AddComponent<T>() where T : Component
        {
            T component = (T)Activator.CreateInstance(typeof(T));
            component.gameObject = this;
            Components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : Component
        {
            if (!Alive) return null;
            for (int i = 0; i < Components.Count; i++)
            {
                if (Components[i].Alive && Components[i] is T match) return match;
            }
            return null;
        }

        /// <summary>Global name lookup (counted): production must not add new call sites.</summary>
        public static int FindCalls;

        public static GameObject Find(string name)
        {
            FindCalls++;
            for (int i = 0; i < Object.All.Count; i++)
            {
                if (Object.All[i] is GameObject go && go.Alive && go.name == name) return go;
            }
            return null;
        }
    }
}
