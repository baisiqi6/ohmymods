// 直接源测试的最小 UnityEngine / Il2CppInterop 边界桩：只覆盖 4 个生产文件实际用到的成员。
// 语义按"测试可完全控制"设计：Transform 是轴对齐简单模型，Physics2D 命中由测试排队，
// Animator 状态由测试写入。真实签名核对在 interop-check/（对着 E 盘 2.4 interop 编译）。
using System;
using System.Collections.Generic;
using System.Threading;
using KingdomEnhancedMod;   // ArrowAttack（弹丸 prefab 排序复制用）

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppStructArray<T>
    {
        private readonly T[] _items;
        public Il2CppStructArray(int size) { _items = new T[size]; }
        public int Length => _items.Length;
        public T this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }
    }

    public class Il2CppReferenceArray<T>
    {
        private readonly T[] _items;
        public Il2CppReferenceArray(int size) { _items = new T[size]; }
        public int Length => _items.Length;
        public T this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }
    }
}

namespace UnityEngine
{
    public class Object
    {
        private static long _next = 100;
        public IntPtr Pointer { get; set; } = (IntPtr)Interlocked.Increment(ref _next);
        public string name = "";
        /// <summary>Unity 语义：销毁后对象与 null 比较相等（生产代码所有 `x == null`/`!= null` 门都据此生效）。</summary>
        public bool IsDestroyed;
        public static implicit operator bool(Object obj) => obj is not null && !obj.IsDestroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool aNull = a is null || a.IsDestroyed;
            bool bNull = b is null || b.IsDestroyed;
            if (aNull || bNull) return aNull && bNull;
            return ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => Pointer.GetHashCode();

        public static T Instantiate<T>(T original) where T : Object
        {
            if (original is ArrowAttack attack)
            {
                var clone = new ArrowAttack
                {
                    _shotMagnitude = attack._shotMagnitude,
                    _boostedShotMagnitude = attack._boostedShotMagnitude,
                    _arrowPrefab = attack._arrowPrefab,
                };
                return clone as T;
            }
            throw new NotSupportedException("Instantiate stub supports ArrowAttack only");
        }

        /// <summary>测试用：接下来 N 次 Destroy 抛异常（验证"销毁失败不丢所有权"的退休回执）。</summary>
        public static int ThrowOnDestroyCount;

        public static void Destroy(Object target)
        {
            if (target == null) return;   // 已销毁/空：不重复记录
            if (ThrowOnDestroyCount > 0)
            {
                ThrowOnDestroyCount--;
                throw new InvalidOperationException("destroy failed");
            }
            target.IsDestroyed = true;
            Destroyed.Add(target);
            // Unity 语义：销毁 GameObject 连带销毁其组件与 Transform。
            if (target is GameObject go && go.Components != null)
            {
                for (int i = 0; i < go.Components.Count; i++)
                {
                    Component component = go.Components[i];
                    if (component == null) continue;
                    component.IsDestroyed = true;
                }
                if (go.OwnTransform != null) go.OwnTransform.IsDestroyed = true;
            }
        }

        public static void DontDestroyOnLoad(Object target) { }

        internal static readonly List<Object> Destroyed = new();
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static implicit operator Vector3(Vector2 value) => new Vector3(value.x, value.y, 0f);
        public static implicit operator Vector2(Vector3 value) => new Vector2(value.x, value.y);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y = 0f, float z = 0f) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
    }

    public struct Quaternion
    {
        public static Quaternion identity => default;
    }

    public struct Color
    {
        public float r, g, b, a;
        public static Color white => new Color { r = 1f, g = 1f, b = 1f, a = 1f };
    }

    public struct Color32
    {
        public byte r, g, b, a;
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height)
        {
            this.x = x; this.y = y; this.width = width; this.height = height;
        }
    }

    public struct Bounds
    {
        public Vector3 max;
    }

    public enum TextureFormat { RGBA32 = 4 }
    public enum FilterMode { Point = 0 }
    public enum TextureWrapMode { Clamp = 0 }
    public enum SpriteMeshType { FullRect = 1 }

    public class Transform : Object
    {
        public GameObject gameObject;
        public Transform parent;
        public Vector3 position;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale = Vector3.one;
        public Vector3 lossyScale = Vector3.one;
        /// <summary>子层级（与 Unity 一致：SetParent 维护；测试用来找自有视觉子物体）。</summary>
        public readonly List<Transform> Children = new();

        public void SetParent(Transform newParent, bool worldPositionStays)
        {
            if (parent != null) parent.Children.Remove(this);
            parent = newParent;
            if (newParent != null && !newParent.Children.Contains(this)) newParent.Children.Add(this);
        }

        public bool IsChildOf(Transform possibleParent)
        {
            Transform current = this;
            while (current != null)
            {
                if (ReferenceEquals(current, possibleParent)) return true;
                current = current.parent;
            }
            return false;
        }

        public Vector3 TransformPoint(Vector3 local)
        {
            return new Vector3(
                position.x + local.x * lossyScale.x,
                position.y + local.y * lossyScale.y,
                position.z + local.z * lossyScale.z);
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public bool enabled = true;
        public Transform transform => gameObject != null ? gameObject.transform : null;

        public T GetComponent<T>() where T : Component => gameObject != null ? gameObject.GetComponent<T>() : null;
        public T GetComponentInChildren<T>() where T : Component => gameObject != null ? gameObject.GetComponentInChildren<T>() : null;
        public T GetComponentInParent<T>() where T : Component => gameObject != null ? gameObject.GetComponentInParent<T>() : null;
        public bool TryGetComponent<T>(out T component) where T : Component
        {
            component = GetComponent<T>();
            return component != null;
        }
    }

    public class MonoBehaviour : Component
    {
    }

    public class Behaviour : Component
    {
    }

    public class GameObject : Object
    {
        private readonly List<Component> _components = new();
        internal readonly Transform OwnTransform;
        public int layer;
        public string tag = "";
        private bool _activeSelf = true;

        internal List<Component> Components => _components;

        public GameObject(string name = "GameObject")
        {
            this.name = name;
            OwnTransform = new Transform { gameObject = this };
        }

        public Transform transform => OwnTransform;

        public int GetInstanceID() => (int)Pointer;

        /// <summary>销毁后不可见（Unity：访问已销毁对象会抛异常；替身按"不可用"处理）。</summary>
        public bool activeInHierarchy => !IsDestroyed && _activeSelf;
        public bool activeSelf => !IsDestroyed && _activeSelf;

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            _components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : Component
        {
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T typed) return typed;
            }
            return null;
        }

        public T GetComponentInChildren<T>() where T : Component
        {
            T own = GetComponent<T>();
            if (own != null) return own;
            return null;
        }

        public T GetComponentInParent<T>() where T : Component
        {
            GameObject current = this;
            while (current != null)
            {
                T found = current.GetComponent<T>();
                if (found != null) return found;
                current = current.OwnTransform.parent != null ? current.OwnTransform.parent.gameObject : null;
            }
            return null;
        }

        public bool TryGetComponent<T>(out T component) where T : Component
        {
            component = GetComponent<T>();
            return component != null;
        }

        public bool CompareTag(string other) => tag == other;
        public void SetActive(bool value) => _activeSelf = value;
    }

    public class Sprite : Object
    {
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit,
            uint extrude, SpriteMeshType meshType)
            => new Sprite { Texture = texture, Rect = rect, Pivot = pivot };
        public Texture2D Texture;
        public Rect Rect;
        public Vector2 Pivot;
    }

    public class Material : Object { }

    public class MaterialPropertyBlock
    {
        public Color Color;
    }

    public class Texture2D : Object
    {
        public Texture2D(int width, int height, TextureFormat format, bool mipChain)
        {
            this.width = width;
            this.height = height;
        }
        public int width { get; set; }
        public int height { get; set; }
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public int anisoLevel;
        public Color32[] Pixels;
        public Color32[] GetPixels32() => Pixels;
    }

    public static class ImageConversion
    {
        public static bool LoadImage(Texture2D texture, byte[] data, bool markNonReadable) => LoadImageResult;
        public static bool LoadImageResult = true;
    }

    public class Renderer : Component
    {
        public int sortingLayerID;
        public int sortingOrder;
        public Material sharedMaterial;
        private bool _forceRenderingOff;

        /// <summary>测试用：下一次写 forceRenderingOff 抛异常（验证归还失败保留凭据）。</summary>
        public bool ThrowOnForceRenderingOffSet;

        public bool forceRenderingOff
        {
            get => _forceRenderingOff;
            set
            {
                if (ThrowOnForceRenderingOffSet)
                {
                    ThrowOnForceRenderingOffSet = false;
                    throw new InvalidOperationException("forceRenderingOff failed");
                }
                _forceRenderingOff = value;
            }
        }

        public void GetPropertyBlock(MaterialPropertyBlock block) { block.Color = PropertyBlockColor; }
        public void SetPropertyBlock(MaterialPropertyBlock block) { PropertyBlockColor = block.Color; }
        public Color PropertyBlockColor;
    }

    public class SpriteRenderer : Renderer
    {
        public Sprite sprite;
        public Color color = Color.white;
        public bool flipX;
        public bool flipY;
    }

    public struct AnimatorStateInfo
    {
        public int shortNameHash;
        public float normalizedTime;
        public float length;
    }

    public class Animator : Component
    {
        public bool isActiveAndEnabled = true;
        public AnimatorStateInfo State;
        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layerIndex) => State;
        public static int StringToHash(string name) => name != null ? name.GetHashCode() : 0;
    }

    public class Collider2D : Component
    {
    }

    public class BoxCollider2D : Collider2D
    {
        public Bounds bounds;
    }

    public struct RaycastHit2D
    {
        public Collider2D collider;
        public float distance;
    }

    public static class Physics2D
    {
        /// <summary>测试排队的命中（按加入顺序；生产按 distance 取最近，与顺序无关）。</summary>
        public static readonly List<RaycastHit2D> QueuedHits = new();
        public static int LastLayerMask;
        /// <summary>测试用：无论容量多大都填满并返回容量（模拟"命中数量饱和"）。</summary>
        public static bool Saturate;
        public static bool ThrowOnCast;
        public static int CastCount;

        public static int LinecastNonAlloc(Vector2 start, Vector2 end,
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<RaycastHit2D> results, int layerMask)
        {
            LastLayerMask = layerMask;
            CastCount++;
            if (ThrowOnCast) throw new InvalidOperationException("injected physics query failure");
            if (Saturate)
            {
                RaycastHit2D fill = QueuedHits.Count > 0 ? QueuedHits[0] : default;
                for (int i = 0; i < results.Length; i++) results[i] = fill;
                return results.Length;
            }
            int count = Math.Min(QueuedHits.Count, results.Length);
            for (int i = 0; i < count; i++) results[i] = QueuedHits[i];
            return count;
        }
    }

    public static class LayerMask
    {
        public static readonly Dictionary<string, int> LayersByName = new()
        {
            { "Enemies", 10 },
            { "Citizens", 11 },
            { "Wildlife", 12 },
        };

        public static int NameToLayer(string name)
            => name != null && LayersByName.TryGetValue(name, out int layer) ? layer : -1;
    }

    public static class Time
    {
        public static float time = 0f;
        public static float deltaTime = 0.016f;
        public static float unscaledTime = 0f;
        public static float timeScale = 1f;
        public static int frameCount = 1;
    }

    public static class Mathf
    {
        public static float Abs(float value) => Math.Abs(value);
        public static float Sqrt(float value) => (float)Math.Sqrt(value);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Sign(float value) => value < 0f ? -1f : 1f;
    }
}
