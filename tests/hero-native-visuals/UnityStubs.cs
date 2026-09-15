// 测试替身：把 HeroArcherVisuals 的真实控制流（接管/挂起/归还 CAS/诊断上限/同帧去重）在无 Unity
// 环境下跑起来。合同（有意为之）：
//  * 只提供生产代码真正用到的成员；生产若引用其它成员即编译失败（契约面被钉住）。
//  * forceRenderingOff / enabled 的读写可被测试置为「下一次抛异常」，验证写失败不持有、下一帧重试。
//  * ImageConversion.LoadImage 不真解码 PNG：只解析 IHDR 取宽高并合成不透明像素 —— 覆盖生产侧
//    「资源存在 / 尺寸校验 / 像素长度 / 非全透明」断言，不声称 PNG 解码正确（真机由 Unity 负责）。
//  * AddComponent<T> 走 RuntimeHelpers.GetUninitializedObject（等价于引擎反射构造，允许仅有 IntPtr ctor 的
//    MonoBehaviour 注入类型），不要求无参构造函数。
// 本文件绝不进入游戏程序集（只在 tests/visuals-native-stub 参与编译）。

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Il2CppInterop.Runtime.Injection
{
    /// <summary>ClassInjector 替身：记录已注册类型（生产只要求「注册调用不抛」）。</summary>
    internal static class ClassInjector
    {
        internal static readonly HashSet<Type> Registered = new HashSet<Type>();

        internal static bool IsTypeRegisteredInIl2Cpp(Type type) => Registered.Contains(type);

        internal static void RegisterTypeInIl2Cpp(Type type) => Registered.Add(type);
    }
}

namespace UnityEngine
{
    public class Object
    {
        public string name = "";
        internal bool Destroyed;

        public static void Destroy(Object target)
        {
            if (target != null) target.Destroyed = true;
        }

        public override string ToString() => name;
    }

    public struct Vector2
    {
        public float x;
        public float y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public override string ToString() => "(" + x + ", " + y + ")";
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
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
        public Rect(float x, float y, float width, float height)
        {
            this.x = x; this.y = y; this.width = width; this.height = height;
        }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2 }
    public enum TextureFormat { RGBA32 = 4 }
    public enum SpriteMeshType { FullRect = 1, Tight = 0 }

    public static class Mathf
    {
        public static float Abs(float value) => Math.Abs(value);
    }

    /// <summary>Time 替身：由测试显式推进（帧号必须变化才允许下一次 Sync 执行）。</summary>
    public static class Time
    {
        public static float time;
        public static float deltaTime = 1f / 60f;
        public static int frameCount;
    }

    public class Component : Object
    {
        internal GameObject Owner;

        public GameObject gameObject => Owner;
        public Transform transform => Owner != null ? Owner.transform : null;

        public T GetComponent<T>() where T : Component => Owner != null ? Owner.GetComponent<T>() : null;

        public T GetComponentInChildren<T>() where T : Component
            => Owner != null ? Owner.GetComponentInChildren<T>() : null;

        internal void Attach(GameObject owner) => Owner = owner;
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
    }

    public class MonoBehaviour : Behaviour
    {
        protected MonoBehaviour(IntPtr pointer) { }
        public MonoBehaviour() { }
    }

    public sealed class GameObject : Object
    {
        internal static readonly List<GameObject> All = new List<GameObject>();
        private static int _nextId = 1;

        public int layer;
        public bool activeSelf = true;

        private readonly List<Component> _components = new List<Component>();
        private readonly int _id;

        public Transform transform { get; }

        public GameObject(string name)
        {
            this.name = name;
            _id = _nextId++;
            transform = new Transform();
            transform.Attach(this);
            _components.Add(transform);
            All.Add(this);
        }

        public int GetInstanceID() => _id;

        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);

        public T AddComponent<T>() where T : Component
        {
            T component;
            try
            {
                component = (T)Activator.CreateInstance(typeof(T), true);   // 字段初始化器必须执行（enabled 等默认值）
            }
            catch (MissingMethodException)
            {
                // 只有 (IntPtr) 构造的注入型 MonoBehaviour：按引擎做法跳过 ctor（该类型无字段初始化器）。
                component = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            }
            component.Attach(this);
            _components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : Component
        {
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T match) return match;
            }
            return null;
        }

        public T GetComponentInChildren<T>() where T : Component
        {
            T own = GetComponent<T>();
            if (own != null) return own;
            for (int i = 0; i < All.Count; i++)
            {
                GameObject other = All[i];
                if (other == this || other.Destroyed) continue;
                Transform parent = other.transform.parent;
                while (parent != null)
                {
                    if (parent.gameObject == this) break;
                    parent = parent.transform != null ? parent.transform.parent : null;
                }
                if (parent == null) continue;
                T child = other.GetComponent<T>();
                if (child != null) return child;
            }
            return null;
        }

        public static void ResetRegistry()
        {
            All.Clear();
            _nextId = 1;
        }
    }

    public sealed class Transform : Component
    {
        public Transform parent { get; private set; }
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale = Vector3.one;

        public Vector3 position
        {
            get
            {
                if (parent == null) return localPosition;
                Vector3 scale = parent.lossyScale;
                return new Vector3(
                    parent.position.x + localPosition.x * scale.x,
                    parent.position.y + localPosition.y * scale.y,
                    parent.position.z + localPosition.z * scale.z);
            }
        }

        public Vector3 lossyScale
        {
            get
            {
                Vector3 scale = parent != null ? parent.lossyScale : Vector3.one;
                return new Vector3(scale.x * localScale.x, scale.y * localScale.y, scale.z * localScale.z);
            }
        }

        public void SetParent(Transform newParent, bool worldPositionStays)
        {
            parent = newParent;
        }

        public void SetParent(Transform newParent) => parent = newParent;
    }

    public class Material : Object
    {
        public Shader shader;
    }

    public sealed class Shader : Object
    {
    }

    public sealed class MaterialPropertyBlock
    {
        public Color color = Color.white;
        public MaterialPropertyBlock Clone() => new MaterialPropertyBlock { color = color };
    }

    public class Renderer : Component
    {
        public bool enabled = true;
        public int sortingLayerID;
        public int sortingOrder;
        public Material sharedMaterial;
    }

    /// <summary>
    /// SpriteRenderer 替身：forceRenderingOff 可注入一次性写失败（验证「没写成功就不持有」与下一帧重试）。
    /// </summary>
    public sealed class SpriteRenderer : Renderer
    {
        private bool _forceRenderingOff;

        public Sprite sprite;
        public bool flipX;
        public bool flipY;
        public Color color = Color.white;

        /// <summary>下一次 forceRenderingOff 读取抛异常（模拟半销毁对象）。</summary>
        public bool FailNextForceRenderingOffRead;
        /// <summary>下一次 forceRenderingOff 写入抛异常。</summary>
        public bool FailNextForceRenderingOffWrite;
        public bool FailAfterForceRenderingOffWrite;
        /// <summary>下一次 enabled 写入抛异常。</summary>
        public bool FailNextEnabledWrite;

        public int ForceRenderingOffWrites;
        public int EnabledWrites;

        public bool forceRenderingOff
        {
            get
            {
                if (FailNextForceRenderingOffRead)
                {
                    FailNextForceRenderingOffRead = false;
                    throw new InvalidOperationException("forceRenderingOff read failed");
                }
                return _forceRenderingOff;
            }
            set
            {
                if (FailNextForceRenderingOffWrite)
                {
                    FailNextForceRenderingOffWrite = false;
                    throw new InvalidOperationException("forceRenderingOff write failed");
                }
                ForceRenderingOffWrites++;
                _forceRenderingOff = value;
                if (FailAfterForceRenderingOffWrite) { FailAfterForceRenderingOffWrite=false; throw new InvalidOperationException("partial write"); }
            }
        }

        public new bool enabled
        {
            get => base.enabled;
            set
            {
                if (FailNextEnabledWrite)
                {
                    FailNextEnabledWrite = false;
                    throw new InvalidOperationException("enabled write failed");
                }
                EnabledWrites++;
                base.enabled = value;
            }
        }

        private MaterialPropertyBlock _block;

        public void GetPropertyBlock(MaterialPropertyBlock block)
        {
            if (block == null || _block == null) return;
            block.color = _block.color;
        }

        public void SetPropertyBlock(MaterialPropertyBlock block)
        {
            _block = block != null ? block.Clone() : null;
        }
    }

    public sealed class MeshRenderer : Renderer
    {
        public new bool enabled { get => base.enabled; set => base.enabled = value; }
    }

    public sealed class Texture2D : Object
    {
        private Color32[] _pixels;

        public int width { get; private set; }
        public int height { get; private set; }
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public int anisoLevel;

        public Texture2D(int width, int height, TextureFormat format, bool mipChain)
        {
            this.width = width;
            this.height = height;
            _pixels = new Color32[width * height];
        }

        public Color32[] GetPixels32() => _pixels;

        internal void SetDecoded(int width, int height, Color32[] pixels)
        {
            this.width = width;
            this.height = height;
            _pixels = pixels;
        }
    }

    public sealed class Sprite : Object
    {
        public Texture2D texture { get; private set; }
        public Rect rect { get; private set; }
        public Vector2 pivot { get; private set; }
        public float pixelsPerUnit { get; private set; }
        public SpriteMeshType meshType { get; private set; }

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit,
            uint extrude, SpriteMeshType meshType)
            => new Sprite
            {
                texture = texture,
                rect = rect,
                pivot = pivot,
                pixelsPerUnit = pixelsPerUnit,
                meshType = meshType,
            };
    }

    public static class ImageConversion
    {
        /// <summary>只解析 PNG IHDR 宽高并合成不透明像素（见文件头合同）。</summary>
        public static bool LoadImage(Texture2D texture, byte[] data, bool markNonReadable)
        {
            if (texture == null || data == null || data.Length < 24) return false;
            if (!(data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)) return false;
            int width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
            int height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
            if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return false;
            Color32[] pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetDecoded(width, height, pixels);
            return true;
        }
    }

    public struct AnimatorStateInfo
    {
        public int shortNameHash;
        public float normalizedTime;

        public AnimatorStateInfo(int shortNameHash, float normalizedTime)
        {
            this.shortNameHash = shortNameHash;
            this.normalizedTime = normalizedTime;
        }
    }

    /// <summary>Animator 替身：脚本化 current/next/转场/异常；StringToHash 为任意稳定映射（见 native-pose 说明）。</summary>
    public class Animator : Behaviour
    {
        public bool isActiveAndEnabled = true;
        public AnimatorStateInfo Current;
        public AnimatorStateInfo Next;
        public bool InTransition;
        public bool ThrowOnRead;
        public int NextReads;
        public int LastLayer = -1;

        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer)
        {
            LastLayer = layer;
            if (ThrowOnRead) throw new InvalidOperationException("animator unreadable");
            return Current;
        }

        public AnimatorStateInfo GetNextAnimatorStateInfo(int layer)
        {
            LastLayer = layer;
            NextReads++;
            if (ThrowOnRead) throw new InvalidOperationException("animator unreadable");
            return Next;
        }

        public bool IsInTransition(int layer)
        {
            LastLayer = layer;
            if (ThrowOnRead) throw new InvalidOperationException("animator unreadable");
            return InTransition;
        }

        public static int StringToHash(string name)
        {
            switch (name)
            {
                case "Stand": return 1001;
                case "Walk": return 1002;
                case "Run": return 1003;
                case "Prepare": return 1004;
                case "Shoot": return 1005;
                case "Ghost Die": return 1006;
                case "Spawn": return 1007;
                default: return 9000;
            }
        }
    }
}
