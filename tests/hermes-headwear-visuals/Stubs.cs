// UnityEngine + native boundary doubles for HermesHeadwearVisuals.
// Only native boundary members are modeled. Root checked offline asset composition and runtime resource availability; in-game visual QA remains pending.
namespace UnityEngine
{
    public class Object
    {
        private static long nextPointer;
        public IntPtr Pointer { get; set; } = (IntPtr)Interlocked.Increment(ref nextPointer);
        public bool Destroyed;
        public static int GameObjectDestroyCalls;
        public static Func<Object, bool> DestroyFailure;
        public static implicit operator bool(Object obj) => obj is not null && !obj.Destroyed;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b) ||
            ((a is null || a.Destroyed) && (b is null || b.Destroyed));
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => Pointer.GetHashCode();
        public static void Destroy(Object target)
        {
            if (target is null || target.Destroyed) return;
            if (DestroyFailure is not null && DestroyFailure(target)) throw new InvalidOperationException("stub: Destroy failed");
            if (target is GameObject go)
            {
                GameObjectDestroyCalls++;
                // Unity destroys the whole hierarchy: children and components become null-valued too.
                if (go.transform.parent is not null) go.transform.SetParent(null, false);
                foreach (Transform child in go.transform.Children.ToArray()) Destroy(child.gameObject);
                foreach (Component component in go.Components.ToArray()) Destroy(component);
                go.Destroyed = true;
                return;
            }
            target.Destroyed = true;
        }
    }

    public class GameObject : Object
    {
        public string name;
        public readonly Transform transform;
        public bool activeInHierarchy = true, activeSelf = true;
        public int layer;
        public int InstanceId;
        public static Func<Type, bool> AddComponentFailure;
        public readonly List<Component> Components = new();

        public GameObject(string name = "")
        {
            this.name = name;
            InstanceId = (int)Pointer;
            transform = new Transform { gameObject = this };
        }

        public int GetInstanceID() => InstanceId;
        public void SetActive(bool value)
        {
            activeSelf = value;
            activeInHierarchy = value && (transform.parent is null || transform.parent.gameObject.activeInHierarchy);
        }
        public T AddComponent<T>() where T : Component, new()
        {
            if (AddComponentFailure is not null && AddComponentFailure(typeof(T)))
                throw new InvalidOperationException("stub: AddComponent<" + typeof(T).Name + "> failed");
            var component = new T { gameObject = this };
            Components.Add(component);
            return component;
        }
        public T GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault();
        public bool TryGetComponent<T>(out T component) where T : class { component = GetComponent<T>(); return component != null; }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public bool TryGetComponent<T>(out T component) where T : class => gameObject.TryGetComponent(out component);
    }

    public class MonoBehaviour : Component { public bool enabled = true; }

    public class Transform : Component
    {
        public static Func<Transform, bool> ParentReadFailure;
        public static Func<Transform, Transform, bool> SetParentFailure;
        // Write counters: the visual layer must only write its own child transform, never the troll/Head/mask chain.
        public int ParentWrites, LocalPositionWrites, LocalRotationWrites, LocalScaleWrites, FindCalls;
        public readonly List<Transform> Children = new();
        private Transform parentValue;
        private Vector3 localPositionValue;
        private Vector3 localScaleValue = Vector3.one;
        private Quaternion localRotationValue;

        public Transform parent
        {
            get
            {
                if (ParentReadFailure is not null && ParentReadFailure(this)) throw new InvalidOperationException("stub: parent read failed");
                return parentValue;
            }
            set => parentValue = value;
        }
        public Vector3 localPosition
        {
            get => localPositionValue;
            set { localPositionValue = value; LocalPositionWrites++; }
        }
        public Vector3 localScale
        {
            get => localScaleValue;
            set { localScaleValue = value; LocalScaleWrites++; }
        }
        public Quaternion localRotation
        {
            get => localRotationValue;
            set { localRotationValue = value; LocalRotationWrites++; }
        }
        public void SetParent(Transform newParent, bool worldPositionStays)
        {
            if (SetParentFailure is not null && SetParentFailure(this, newParent)) throw new InvalidOperationException("stub: SetParent failed");
            ParentWrites++;
            if (parentValue is not null) parentValue.Children.Remove(this);
            parentValue = newParent;
            if (parentValue is not null) parentValue.Children.Add(this);
        }
        public Transform Find(string name)
        {
            FindCalls++;
            foreach (Transform child in Children) if (child.gameObject.name == name) return child;
            return null;
        }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x = 0f, float y = 0f, float z = 0f) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new(0f, 0f, 0f);
        public static Vector3 one => new(1f, 1f, 1f);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
    }

    public struct Quaternion
    {
        public static Quaternion identity => new();
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r = 0f, float g = 0f, float b = 0f, float a = 0f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new(1f, 1f, 1f, 1f);
    }

    public class Sprite : Object { public string name; public Sprite(string name = "") => this.name = name; }
    public class Material : Object { public string name; public Material(string name = "") => this.name = name; }

    public class SpriteRenderer : Component
    {
        public Sprite sprite;
        public Material sharedMaterial;
        public Color color;
        public bool flipX, flipY;
        public int sortingLayerID, sortingOrder;
        // Counts only real writes: ownership tests must see exactly the writes the production file makes.
        public int EnabledWrites;
        public static Func<SpriteRenderer, bool> EnabledWriteFailure;
        public static Func<SpriteRenderer, bool> EnabledReadFailure;
        private bool enabledValue = true;
        public bool enabled
        {
            get
            {
                if (EnabledReadFailure is not null && EnabledReadFailure(this)) throw new InvalidOperationException("stub: enabled read failed");
                return enabledValue;
            }
            set
            {
                if (EnabledWriteFailure is not null && EnabledWriteFailure(this)) throw new InvalidOperationException("stub: enabled write failed");
                enabledValue = value;
                EnabledWrites++;
            }
        }
    }

    public static class Resources
    {
        public static readonly List<Sprite> All = new();
        public static int AssetScanCalls;
        public static T[] FindObjectsOfTypeAll<T>() { AssetScanCalls++; return All.OfType<T>().ToArray(); }
    }

    public static class Time { public static float time; }
}

// Native boundary double: only the members the visual layer is allowed to read.
public class FriendlyTroll : UnityEngine.MonoBehaviour
{
    public UnityEngine.SpriteRenderer _mask;
    public UnityEngine.SpriteRenderer _maskPrefab;
}

namespace KingdomEnhancedMod
{
    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new();
        public Logger LogSource = new();
        public class Logger
        {
            public readonly List<string> Warnings = new();
            public void LogWarning(string message) => Warnings.Add(message);
            public void LogInfo(string message) { }
            public void LogError(string message) { }
        }
    }
}
