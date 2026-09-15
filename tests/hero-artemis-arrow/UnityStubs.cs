// 原生边界替身（Unity 侧）：只提供 HeroArcherArrowVisuals.cs 真正引用的 API 面，
// 并把「可观察写入」变成测试证据：sprite/color 的每次读写都记账，其它成员（material/sorting/
// collider/trail/damage…）只有只读探针，模块若能编过就等于声明它不碰那些字段。
//
// 这不是 Unity：绝不用于任何游戏内验证；真实 API 面由 ../interop-compile 的真实 2.4 interop 编译门负责。

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

        internal static int DestroyCalls;

        public static void Destroy(Object target)
        {
            DestroyCalls++;
            if (target != null) target.Destroyed = true;
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
        /// <summary>测试可推进的 unscaled 时间（模块的退避只看它）。</summary>
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

        /// <summary>世界尺寸（rect / PPU）——用来验证「缩放图不改变世界尺寸」。</summary>
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
