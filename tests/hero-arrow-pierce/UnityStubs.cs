// 原生边界替身（Unity 侧）：只提供 HeroArcherArrowVisuals.cs + HeroArcherWallPierce.cs 真正引用的 API 面，
// 并把「可观察写入」变成测试证据：sprite/color 的每次读写都记账，Physics2D.IgnoreCollision 的每次调用
// （含 true/false 顺序）都记账，其它成员（material/sorting/enabled/trail…）只有只读探针，
// 模块若能编过就等于声明它不碰那些字段。
//
// 这不是 Unity：绝不用于任何游戏内验证；真实 API 面由 actual-interop 编译门负责。

using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        private IntPtr _pointer;

        /// <summary>native 指针（真实 IL2CPP 里是只读属性；替身允许测试改写/模拟读取异常）。</summary>
        public virtual IntPtr Pointer
        {
            get { return _pointer; }
            set { _pointer = value; }
        }

        public bool Destroyed;

        /// <summary>true 时对它的 <c>== null</c> / <c>!= null</c> 抛异常（模拟 Unity 原生 null 检查失败的外层异常面）。</summary>
        public bool NullCheckThrows;

        public static bool operator ==(Object a, Object b)
        {
            ThrowIfNullCheckThrows(a);
            ThrowIfNullCheckThrows(b);
            return ReferenceEquals(a, b);
        }

        public static bool operator !=(Object a, Object b)
        {
            ThrowIfNullCheckThrows(a);
            ThrowIfNullCheckThrows(b);
            return !ReferenceEquals(a, b);
        }

        public override bool Equals(object other) => ReferenceEquals(this, other);

        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

        private static void ThrowIfNullCheckThrows(Object candidate)
        {
            if (!ReferenceEquals(candidate, null) && candidate.NullCheckThrows)
                throw new InvalidOperationException("stub: unity null check threw");
        }

        internal static int DestroyCalls;

        public static void Destroy(Object target)
        {
            DestroyCalls++;
            if (target != null) target.Destroyed = true;
        }

        // ---------- 场景替身：FindObjectsOfType<T> 只认登记过、未销毁且所在 GO 活动的对象 ----------

        internal static readonly List<Object> AllObjects = new List<Object>();
        internal static bool FindObjectsOfTypeThrows;
        internal static int FindObjectsOfTypeCalls;

        /// <summary>镜像 Unity 语义：只回活动对象（组件按其 GameObject.activeSelf 判定），销毁对象不可见。</summary>
        public static T[] FindObjectsOfType<T>() where T : Object
        {
            FindObjectsOfTypeCalls++;
            if (FindObjectsOfTypeThrows) throw new InvalidOperationException("stub: FindObjectsOfType threw");
            List<T> found = new List<T>();
            for (int i = 0; i < AllObjects.Count; i++)
            {
                Object candidate = AllObjects[i];
                if (candidate == null || candidate.Destroyed) continue;
                Component component = candidate as Component;
                if (component != null)
                {
                    GameObject go = component.gameObject;
                    if (go == null || !go.activeSelf) continue;
                }
                if (candidate is T match) found.Add(match);
            }
            return found.ToArray();
        }

        internal static void ResetScene()
        {
            AllObjects.Clear();
            FindObjectsOfTypeThrows = false;
            FindObjectsOfTypeCalls = 0;
            DestroyCalls = 0;
        }
    }

    public class Component : Object
    {
        public GameObject gameObject { get; set; }
    }

    public class GameObject : Object
    {
        public int InstanceId;

        private bool _activeSelf = true;
        /// <summary>true 时读 activeSelf 抛异常（测试「未知」分支）。</summary>
        public bool ActiveSelfReadThrows;

        public bool activeSelf
        {
            get
            {
                if (ActiveSelfReadThrows) throw new InvalidOperationException("stub: activeSelf read threw");
                return _activeSelf;
            }
            set { _activeSelf = value; }
        }

        public readonly List<Component> Components = new List<Component>();
        /// <summary>true 时 GetComponent/TryGetComponent 抛异常（测试异常隔离）。</summary>
        public bool ComponentLookupThrows;

        public int GetInstanceID() => InstanceId;

        public T GetComponent<T>() where T : Component
        {
            if (ComponentLookupThrows) throw new InvalidOperationException("stub: component lookup threw");
            for (int i = 0; i < Components.Count; i++) if (Components[i] is T match) return match;
            return null;
        }

        public bool TryGetComponent<T>(out T component) where T : Component
        {
            component = GetComponent<T>();
            return component != null;
        }
    }

    public sealed class Material : Object
    {
    }

    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum TextureWrapMode { Repeat, Clamp, Mirror }
    public enum TextureFormat { RGBA32, ARGB32 }
    public enum SpriteMeshType { FullRect, Tight }

    public struct Vector2
    {
        public float x;
        public float y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Rect
    {
        public float x;
        public float y;
        public float width;
        public float height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public override string ToString() => "(" + r + "," + g + "," + b + "," + a + ")";
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public static class Mathf
    {
        public static float Abs(float value) => Math.Abs(value);
    }

    public static class Time
    {
        /// <summary>测试可推进的 unscaled 时间（模块的快照 TTL 与退避只看它）。</summary>
        public static float unscaledTime;
    }

    public class Texture2D : Object
    {
        public Texture2D(int width, int height, TextureFormat format, bool mipChain)
        {
            this.width = width;
            this.height = height;
            this.format = format;
            this.mipChain = mipChain;
        }

        public int width;
        public int height;
        public TextureFormat format;
        public bool mipChain;
        public FilterMode filterMode = FilterMode.Bilinear;
        public TextureWrapMode wrapMode = TextureWrapMode.Repeat;
        public int anisoLevel = 1;
        public Color32[] decodedPixels;
        public bool PixelReadThrows;

        public Color32[] GetPixels32()
        {
            if (PixelReadThrows) throw new InvalidOperationException("stub: GetPixels32 threw");
            return decodedPixels;
        }
    }

    public class Sprite : Object
    {
        public Texture2D texture;
        public Rect rect;
        public Vector2 pivot;
        public float pixelsPerUnit;
        public uint extrude;
        public SpriteMeshType meshType;

        /// <summary>true 时读 Pointer 抛异常（模拟已销毁/不可读的 sprite，测试「未知」分支）。</summary>
        public bool PointerReadThrows;

        public override IntPtr Pointer
        {
            get
            {
                if (PointerReadThrows) throw new InvalidOperationException("stub: sprite Pointer read threw");
                return base.Pointer;
            }
            set { base.Pointer = value; }
        }

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit,
            uint extrude, SpriteMeshType meshType)
        {
            return new Sprite
            {
                texture = texture,
                rect = rect,
                pivot = pivot,
                pixelsPerUnit = pixelsPerUnit,
                extrude = extrude,
                meshType = meshType,
                Pointer = new IntPtr(SpriteCounter++ + 100000),
            };
        }

        internal static int SpriteCounter = 1;

        /// <summary>世界尺寸（rect / PPU）——用来验证显示缩放只改这一项。</summary>
        public float WorldWidth => rect.width / pixelsPerUnit;
        public float WorldHeight => rect.height / pixelsPerUnit;
    }

    public class SpriteRenderer : Component
    {
        private Sprite _sprite;
        private Color _color;

        /// <summary>按顺序记录本 renderer 上的成员写入（只应为 "sprite"/"color"）。</summary>
        public readonly List<string> Writes = new List<string>();

        public bool ReadThrows;
        public bool WriteSpriteThrows;
        public bool WriteColorThrows;

        public Sprite sprite
        {
            get { if (ReadThrows) throw new InvalidOperationException("stub: sprite read threw"); return _sprite; }
            set
            {
                if (WriteSpriteThrows) throw new InvalidOperationException("stub: sprite write threw");
                Writes.Add("sprite");
                _sprite = value;
            }
        }

        public Color color
        {
            get { if (ReadThrows) throw new InvalidOperationException("stub: color read threw"); return _color; }
            set
            {
                if (WriteColorThrows) throw new InvalidOperationException("stub: color write threw");
                Writes.Add("color");
                _color = value;
            }
        }

        // 只读探针：模块绝不应触碰这些成员（碰了替身会直接编不过）。
        public Material sharedMaterial;
        public bool enabled = true;
        public int sortingLayerID;
        public int sortingOrder;

        public int SpriteWrites { get { int n = 0; foreach (string w in Writes) if (w == "sprite") n++; return n; } }
        public int ColorWrites { get { int n = 0; foreach (string w in Writes) if (w == "color") n++; return n; } }
    }

    /// <summary>原生 Collider2D 替身：生产只允许把它作为 IgnoreCollision 的参数/null 判断/缓存条目；
    /// Label 仅供测试断言，生产代码读它就会在真实 interop 里编不过。</summary>
    public class Collider2D : Component
    {
        /// <summary>测试标签。</summary>
        public string Label;

        /// <summary>true 时读 Pointer 抛异常（模拟「实际 collider 原生身份读不到」= 未知）。</summary>
        public bool PointerReadThrows;

        public override IntPtr Pointer
        {
            get
            {
                if (PointerReadThrows) throw new InvalidOperationException("stub: collider Pointer read threw");
                return base.Pointer;
            }
            set { base.Pointer = value; }
        }
    }

    /// <summary>Physics2D 替身：完整记录每次 IgnoreCollision 的 (箭碰撞体, 墙碰撞体, ignore) 序列，
    /// 并维护**真实 pair 状态表**（GetIgnoreCollision 读取 / Reset 清空，对称写入，与引擎同语义）；
    /// 还允许测试按对注入读/写异常。计数助手按侧/方向显式命名，避免把墙碰撞体传进箭侧计数而恒 0。
    /// 夹具全程保持碰撞体/GO 活动：不模拟「停用即清 ignore」的引擎边界。</summary>
    public static class Physics2D
    {
        internal struct IgnoreCall
        {
            internal Collider2D Arrow;
            internal Collider2D Wall;
            internal bool Ignore;
        }

        internal static readonly List<IgnoreCall> Calls = new List<IgnoreCall>();

        /// <summary>真实 pair 状态（对称）：生产用 GetIgnoreCollision 决定「是否本模块接管」。</summary>
        internal static readonly Dictionary<(Collider2D, Collider2D), bool> PairState =
            new Dictionary<(Collider2D, Collider2D), bool>();

        internal delegate void IgnoreHandler(Collider2D collider1, Collider2D collider2, bool ignore);
        /// <summary>测试注入：可按 (collider1, collider2, ignore) 抛异常；抛出的对不记入 Calls/PairState。</summary>
        internal static IgnoreHandler OnIgnore;
        internal delegate void GetIgnoreHandler(Collider2D collider1, Collider2D collider2);
        /// <summary>测试注入：读取按对抛异常（「读取未知」分支）。</summary>
        internal static GetIgnoreHandler OnGetIgnore;
        /// <summary>true 时所有写入调用都抛（测试全局失败面）。</summary>
        internal static bool Throws;

        public static bool GetIgnoreCollision(Collider2D collider1, Collider2D collider2)
        {
            if (collider1 == null || collider2 == null)
                throw new ArgumentNullException("stub: GetIgnoreCollision called with a null collider");
            if (OnGetIgnore != null) OnGetIgnore(collider1, collider2);
            return PairState.TryGetValue((collider1, collider2), out bool ignored) && ignored;
        }

        public static void IgnoreCollision(Collider2D collider1, Collider2D collider2, bool ignore)
        {
            if (collider1 == null || collider2 == null)
                throw new ArgumentNullException("stub: IgnoreCollision called with a null collider");
            if (Throws) throw new InvalidOperationException("stub: IgnoreCollision threw");
            if (OnIgnore != null) OnIgnore(collider1, collider2, ignore);
            Calls.Add(new IgnoreCall { Arrow = collider1, Wall = collider2, Ignore = ignore });
            PairState[(collider1, collider2)] = ignore;
            PairState[(collider2, collider1)] = ignore;
        }

        /// <summary>按箭一侧计数（calls[i].Arrow == arrow）：误把墙碰撞体传进来会恒 0——请改用 CountWallPair。</summary>
        internal static int CountArrowPair(Collider2D arrow, bool ignore)
        {
            int n = 0;
            for (int i = 0; i < Calls.Count; i++)
                if (ReferenceEquals(Calls[i].Arrow, arrow) && Calls[i].Ignore == ignore) n++;
            return n;
        }

        /// <summary>按墙一侧计数（calls[i].Wall == wall）：必须同时过滤 ignore 方向（true=穿透 / false=归还）。</summary>
        internal static int CountWallPair(Collider2D wall, bool ignore)
        {
            int n = 0;
            for (int i = 0; i < Calls.Count; i++)
                if (ReferenceEquals(Calls[i].Wall, wall) && Calls[i].Ignore == ignore) n++;
            return n;
        }

        internal static void Reset()
        {
            Calls.Clear();
            PairState.Clear();
            OnIgnore = null;
            OnGetIgnore = null;
            Throws = false;
        }
    }

    public static class ImageConversion
    {
        /// <summary>真实解码本目录 PngCodec（只支持 8bit RGB/RGBA 非隔行）；非法字节返回 false。</summary>
        public static bool LoadImage(Texture2D texture, byte[] data, bool markNonReadable)
        {
            if (texture == null || data == null) return false;
            if (markNonReadable) throw new InvalidOperationException("stub: module must decode readable (markNonReadable=false)");
            int width, height;
            Color32[] pixels;
            if (!PngCodec.TryDecode(data, out width, out height, out pixels)) return false;
            texture.width = width;
            texture.height = height;
            texture.decodedPixels = pixels;
            return true;
        }
    }
}

// Harmony 属性替身：只保证 [HarmonyPatch]/[Harmony*] 用法能编过（真实属性面由 interop 编译门验证）。
namespace HarmonyLib
{
    public enum Priority
    {
        Last = 0,
        LowerThanNormal = 300,
        Normal = 400,
        HigherThanNormal = 500,
        First = 800,
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public readonly Type DeclaringType;
        public readonly string MethodName;

        public HarmonyPatch(Type declaringType, string methodName)
        {
            DeclaringType = declaringType;
            MethodName = methodName;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyFinalizer : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPriority : Attribute
    {
        public readonly Priority Value;

        public HarmonyPriority(Priority priority) { Value = priority; }
    }
}
