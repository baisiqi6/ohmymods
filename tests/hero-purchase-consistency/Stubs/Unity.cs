// Minimal UnityEngine stand-in for the offline reproduction. Only members the compiled
// production files actually touch are provided. Geometry/sprite math mirrors the small
// subset production reads (pivot, rect, pixelsPerUnit, active hierarchy, child transforms).
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        private static int _next = 0x1000;
        public IntPtr Pointer { get; } = new IntPtr(++_next);
        public string name = "";
        public int GetInstanceID() => (int)Pointer.ToInt64();
        public static void Destroy(Object obj)
        {
            if (obj == null) return;
            if (obj is GameObject go) go.SetActive(false);
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject != null ? gameObject.transform : null;

        public T GetComponent<T>() where T : Component => gameObject != null ? gameObject.GetComponent<T>() : null;
        public T GetComponentInParent<T>() where T : Component => gameObject != null ? gameObject.GetComponentInParent<T>() : null;
        public T GetComponentInChildren<T>() where T : Component => gameObject != null ? gameObject.GetComponentInChildren<T>() : null;

        public T TryCast<T>() where T : class => this as T;

        public T Cast<T>() where T : class
        {
            if (this is T same) return same;
            var bridge = KingdomEnhancedMod.OwnerInterfaceBridges.Cast<T>(this);
            if (bridge != null) return bridge;
            throw new InvalidCastException("stub cast failed: " + GetType().Name + " -> " + typeof(T).Name);
        }
    }

    public class MonoBehaviour : Component
    {
        public MonoBehaviour() { }
        public MonoBehaviour(IntPtr pointer) { }
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject != null && gameObject.activeInHierarchy;
    }

    public class Transform : Component
    {
        public Transform parent;
        public readonly List<Transform> children = new List<Transform>();
        public Vector3 localPosition;
        public Vector3 localScale = new Vector3(1f, 1f, 1f);
        public Quaternion localRotation;

        public Vector3 position
        {
            get => parent == null ? localPosition : parent.position + localPosition;
            set => localPosition = parent == null ? value : value - parent.position;
        }

        public void SetParent(Transform next, bool keepWorldPosition)
        {
            var oldPosition = position;
            parent?.children.Remove(this);
            parent = next;
            parent?.children.Add(this);
            if (keepWorldPosition) position = oldPosition;
        }

        public bool IsChildOf(Transform other)
        {
            if (other == null) return false;
            for (var p = parent; p != null; p = p.parent)
                if (ReferenceEquals(p, other)) return true;
            return false;
        }
    }

    public struct Scene
    {
        public int handle;
    }

    public class GameObject : Object
    {
        private static int _nextId = 0x2000;
        public string tag = "Untagged";
        public int layer;
        public Scene scene;
        private bool _activeSelf = true;
        public bool activeSelf => _activeSelf;
        public bool activeInHierarchy => _activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public readonly Transform transform;
        private readonly List<Component> _components = new List<Component>();

        public GameObject() : this("GameObject") { }

        public GameObject(string objectName)
        {
            name = objectName;
            InstanceId = _nextId++;
            transform = new Transform { gameObject = this };
            _components.Add(transform);
        }

        public int InstanceId { get; }

        public T AddComponent<T>() where T : Component
        {
            var ctor = typeof(T).GetConstructor(new[] { typeof(IntPtr) });
            var item = ctor == null ? (T)Activator.CreateInstance(typeof(T)) : (T)ctor.Invoke(new object[] { IntPtr.Zero });
            item.gameObject = this;
            _components.Add(item);
            return item;
        }

        // Test-only query remap: detach the old component from lookup while keeping its
        // managed/native stand-in object alive and attached to this root for receipt checks.
        public bool ReplaceQueryableComponent<T>(T oldComponent, T replacement) where T : Component
        {
            int oldIndex = _components.IndexOf(oldComponent);
            int newIndex = _components.IndexOf(replacement);
            if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex
                || oldComponent.gameObject != this || replacement.gameObject != this) return false;
            _components.RemoveAt(newIndex);
            _components[oldIndex] = replacement;
            return true;
        }

        public T GetComponent<T>() where T : Component
        {
            for (int i = 0; i < _components.Count; i++)
                if (_components[i] is T typed) return typed;
            return null;
        }

        public T GetComponentInParent<T>() where T : Component
        {
            for (var t = transform; t != null; t = t.parent)
            {
                var found = t.gameObject.GetComponent<T>();
                if (found != null) return found;
            }
            return null;
        }

        public T GetComponentInChildren<T>() where T : Component => GetComponentInChildren<T>(false);

        public T GetComponentInChildren<T>(bool includeInactive) where T : Component
        {
            if (includeInactive || activeInHierarchy)
            {
                var own = GetComponent<T>();
                if (own != null) return own;
            }
            for (int i = 0; i < transform.children.Count; i++)
            {
                var found = transform.children[i].gameObject.GetComponentInChildren<T>(includeInactive);
                if (found != null) return found;
            }
            return null;
        }

        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var list = new List<T>();
            Collect(list, includeInactive);
            return list.ToArray();
        }

        private void Collect<T>(List<T> list, bool includeInactive) where T : Component
        {
            if (includeInactive || activeInHierarchy)
                for (int i = 0; i < _components.Count; i++)
                    if (_components[i] is T typed) list.Add(typed);
            for (int i = 0; i < transform.children.Count; i++)
                transform.children[i].gameObject.Collect(list, includeInactive);
        }

        public void SetActive(bool value)
        {
            bool was = activeInHierarchy;
            _activeSelf = value;
            if (was && !value) DisableTree(this);
            if (!was && value) EnableTree(this);
        }

        private static void DisableTree(GameObject go)
        {
            foreach (var c in go._components)
                c.GetType().GetMethod("OnDisable")?.Invoke(c, null);
            foreach (var child in go.transform.children) DisableTree(child.gameObject);
        }

        private static void EnableTree(GameObject go)
        {
            foreach (var c in go._components)
                c.GetType().GetMethod("OnEnable")?.Invoke(c, null);
            foreach (var child in go.transform.children) EnableTree(child.gameObject);
        }

        public bool CompareTag(string candidate) => tag == candidate;
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0f, 0f);
        public override string ToString() => "(" + x + ", " + y + ")";
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public override string ToString() => "(" + x + ", " + y + ", " + z + ")";
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity => new Quaternion { w = 1f };
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }

    public struct Bounds
    {
        public Vector3 center, size;
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public static class Time
    {
        public static float time = 100f;
        public static float unscaledTime = 100f;
        public static float deltaTime = 0.02f;
        public static int frameCount = 10;
    }

    public static class Mathf
    {
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float SmoothStep(float from, float to, float t) => Lerp(from, to, t * t * (3f - 2f * t));
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    public enum FilterMode { Point, Bilinear }
    public enum TextureWrapMode { Clamp, Repeat }
    public enum TextureFormat { RGBA32 }
    public enum SpriteMeshType { FullRect, Tight }

    public class Texture2D : Object
    {
        public int width, height, anisoLevel;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public Texture2D(int width, int height, TextureFormat format, bool mipChain) { this.width = width; this.height = height; }
    }

    public static class ImageConversion
    {
        // Real PNG bytes are embedded; only the IHDR dimensions are needed by production checks.
        public static bool LoadImage(Texture2D texture, byte[] bytes, bool markNonReadable)
        {
            if (texture == null || bytes == null || bytes.Length < 24) return false;
            texture.width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
            texture.height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
            return true;
        }
    }

    public class Sprite : Object
    {
        public Vector2 pivot;
        public Rect rect;
        public Texture2D texture;
        public float pixelsPerUnit = 32f;
        public Bounds bounds => new Bounds(Vector3.zero, new Vector3(rect.width / pixelsPerUnit, rect.height / pixelsPerUnit, 0f));

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit, uint extrude, SpriteMeshType meshType)
            => new Sprite { texture = texture, rect = rect, pivot = pivot, pixelsPerUnit = pixelsPerUnit };
    }

    public class Shader : Object { }

    public class Material : Object
    {
        public Shader shader;
        public bool HasProperty(string property) => false;
        public float GetFloat(string property) => 0f;
        public bool IsKeywordEnabled(string keyword) => false;
    }

    public class SpriteRenderer : Component
    {
        public Sprite sprite;
        public bool enabled = true;
        public bool flipX;
        public int sortingOrder, sortingLayerID;
        public Material sharedMaterial;
        public Color color = new Color(1f, 1f, 1f, 1f);
        public Bounds bounds => sprite != null ? sprite.bounds : new Bounds(Vector3.zero, Vector3.one);
    }

    public static class JsonUtility
    {
        public static string ToJson(IslandSaveData island, bool prettyPrint) => island != null ? island.Json : "";
    }

    public static class LayerMask
    {
        public static int NameToLayer(string layerName) => layerName == "Enemies" ? 9 : -1;
    }
}
