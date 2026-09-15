// 测试专用最小 UnityEngine / Il2CppInterop 替身（只为在无 Unity 环境下把 HeroArcherCloth.cs
// 的 Create/Tick/Destroy 控制流与顶点写出跑起来）。
//
// 重要约束：SpriteRenderer 的全部属性在本替身里都是**只读**（无 setter）→
// 若生产代码写了 reference 的任何字段，本测试项目将编译失败（这正是契约要求）。
// 本文件绝不进入游戏程序集（只在 tests/view-stub 里参与编译）。

using System;

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    /// <summary>Il2CppStructArray 替身：一次性分配 + 就地读写（与真实 interop 用法一致）。</summary>
    public sealed class Il2CppStructArray<T>
    {
        private readonly T[] _items;

        public Il2CppStructArray(int length) => _items = new T[length];

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

        public static Vector3 one => new Vector3(1f, 1f, 1f);

        public override string ToString() => "(" + x + ", " + y + ", " + z + ")";
    }

    public struct Quaternion
    {
        public static Quaternion identity => new Quaternion();
    }

    public struct Color
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static Color white => new Color(1f, 1f, 1f, 1f);

        public static Color Lerp(Color from, Color to, float t) => new Color(
            from.r + (to.r - from.r) * t,
            from.g + (to.g - from.g) * t,
            from.b + (to.b - from.b) * t,
            from.a + (to.a - from.a) * t);
    }

    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size)
        {
            this.center = center;
            this.size = size;
        }

        public Vector3 center;
        public Vector3 size;
    }

    public enum FilterMode
    {
        Point = 0,
        Bilinear = 1,
    }

    public enum TextureWrapMode
    {
        Repeat = 0,
        Clamp = 1,
    }

    public class Object
    {
        public string name;
        public bool destroyed;

        public static void Destroy(Object target)
        {
            if (target != null) target.destroyed = true;
        }
    }

    public class Component : Object
    {
        private protected readonly GameObject Owner;

        internal Component(GameObject owner) => Owner = owner;

        public GameObject gameObject => Owner;

        public Transform transform => Owner.transform;
    }

    public sealed class Transform : Component
    {
        internal Transform(GameObject owner) : base(owner) { }

        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale = Vector3.one;
        public Transform parent;

        public void SetParent(Transform newParent, bool worldPositionStays)
        {
            parent = newParent;
            if (!worldPositionStays)
            {
                localPosition = Vector3.zero;
                localRotation = Quaternion.identity;
                localScale = Vector3.one;
            }
        }
    }

    public sealed class GameObject : Object
    {
        private readonly System.Collections.Generic.List<Component> _components = new System.Collections.Generic.List<Component>();

        public GameObject() => transform = new Transform(this);

        public GameObject(string name) : this() => this.name = name;

        public Transform transform { get; }

        public int layer;

        public T AddComponent<T>() where T : Component
        {
            T component = (T)Activator.CreateInstance(typeof(T), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public, null, new object[] { this }, null);
            _components.Add(component);
            return component;
        }

        public Component[] Components => _components.ToArray();
    }

    public sealed class Mesh : Object
    {
        public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> vertices;
        public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color> colors;
        public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> triangles;
        public Bounds bounds;
        public bool dynamic;

        public void MarkDynamic() => dynamic = true;
    }

    public sealed class MeshFilter : Component
    {
        internal MeshFilter(GameObject owner) : base(owner) { }

        public Mesh sharedMesh;
    }

    public sealed class MaterialPropertyBlock
    {
        private readonly System.Collections.Generic.Dictionary<int, Color> _colors = new System.Collections.Generic.Dictionary<int, Color>();

        public void SetColor(int property, Color color) => _colors[property] = color;

        public bool TryGetColor(int property, out Color color) => _colors.TryGetValue(property, out color);
    }

    public class Renderer : Component
    {
        internal Renderer(GameObject owner) : base(owner) { }

        public bool enabled;
        public int sortingLayerID;
        public int sortingOrder;
        public Material sharedMaterial;
        public MaterialPropertyBlock lastBlock;
        public int BlockWrites;

        public void SetPropertyBlock(MaterialPropertyBlock block)
        {
            lastBlock = block;
            BlockWrites++;
        }
    }

    public sealed class MeshRenderer : Renderer
    {
        internal MeshRenderer(GameObject owner) : base(owner) { }
    }

    /// <summary>只读替身：没有任何 setter → 生产代码若写 reference 会编译失败。</summary>
    public sealed class SpriteRenderer : Renderer
    {
        internal SpriteRenderer(GameObject owner) : base(owner) { }

        private Color _color = Color.white;
        private bool _flipX;
        private int _sortingLayerID;
        private int _sortingOrder;
        private Material _material;

        public Color color => _color;
        public bool flipX => _flipX;
        public new int sortingLayerID => _sortingLayerID;
        public new int sortingOrder => _sortingOrder;
        public new Material sharedMaterial => _material;

        internal void Initialize(Color color, bool flipX, int sortingLayerID, int sortingOrder, Material material)
        {
            _color = color;
            _flipX = flipX;
            _sortingLayerID = sortingLayerID;
            _sortingOrder = sortingOrder;
            _material = material;
        }
    }

    public sealed class Shader : Object
    {
        public static Shader Find(string shaderName) => new Shader { name = shaderName };

        public static int PropertyToID(string propertyName) => propertyName.GetHashCode();
    }

    public sealed class Texture2D : Object
    {
        private readonly int _width;
        private readonly int _height;

        public Texture2D(int width, int height)
        {
            _width = width;
            _height = height;
        }

        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public Color Pixel;
        public int ApplyCount;

        public void SetPixel(int x, int y, Color color) => Pixel = color;

        public void Apply() => ApplyCount++;
    }

    public sealed class Material : Object
    {
        public Material(Shader shader) => this.shader = shader;

        public Shader shader;
        public Texture2D mainTexture;
        public Color color;
        public int renderQueue;
    }

    public static class Debug
    {
        public static string LastWarning;

        public static void LogWarning(object message) => LastWarning = "[KEM] " + message;
    }
}
