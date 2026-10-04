// Minimal UnityEngine doubles for the coin-courier visuals suite.
// They model only the API surface CoinCourierVisuals.cs / CoinCourierTeleportFx.cs touch:
// no scene scans, no prefab clones, no rendering. Failure injection hooks exist solely for the
// resource-cleanup tests. Passing these tests does not establish IL2CPP compatibility or real
// rendering; the contact sheet / in-game check owns that.
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public bool Destroyed;
        public static int DestroyCalls;
        private static int _nextInstanceId;
        private readonly int _instanceId = ++_nextInstanceId;

        public int GetInstanceID() => _instanceId;

        public static void Destroy(Object target)
        {
            if (target == null || target.Destroyed) return;
            DestroyCalls++;
            target.Destroyed = true;
            target.OnDestroyed();
            if (target is GameObject gameObject)
            {
                gameObject.SetActive(false);
                foreach (var component in gameObject.Components) component.Destroyed = true;
                foreach (var child in gameObject.transform.Children.ToArray()) Destroy(child.gameObject);
            }
        }

        protected virtual void OnDestroyed() { }

        public static void DontDestroyOnLoad(Object target) { }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
    }

    public class GameObject : Object
    {
        public static readonly List<GameObject> All = new();
        public readonly List<Component> Components = new();
        public Transform transform;
        private bool active = true;

        public static int SpriteRendererAdds, LineRendererAdds;
        public static int FailSpriteRendererAdd = -1, FailLineRendererAdd = -1;

        public GameObject(string name = "object")
        {
            this.name = name;
            transform = new Transform { gameObject = this, name = "transform" };
            Components.Add(transform);
            All.Add(this);
        }

        public bool activeSelf => active;
        public bool activeInHierarchy => !Destroyed && active
            && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public void SetActive(bool value) => active = value;

        public T AddComponent<T>() where T : Component, new()
        {
            if (typeof(T) == typeof(SpriteRenderer))
            {
                SpriteRendererAdds++;
                if (SpriteRendererAdds == FailSpriteRendererAdd)
                    throw new InvalidOperationException("injected SpriteRenderer failure");
            }
            if (typeof(T) == typeof(LineRenderer))
            {
                LineRendererAdds++;
                if (LineRendererAdds == FailLineRendererAdd)
                    throw new InvalidOperationException("injected LineRenderer failure");
            }
            var component = new T { gameObject = this };
            Components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault();

        public static void ResetCounters()
        {
            SpriteRendererAdds = 0;
            LineRendererAdds = 0;
        }
    }

    public class Transform : Component
    {
        public Transform parent;
        public readonly List<Transform> Children = new();
        public Vector3 localPosition;
        public Vector3 localScale = Vector3.one;
        public Vector3 position
        {
            get => parent == null ? localPosition : parent.position + localPosition;
            set => localPosition = parent == null ? value : value - parent.position;
        }
        public void SetParent(Transform value, bool worldPositionStays)
        {
            if (parent != null) parent.Children.Remove(this);
            parent = value;
            if (parent != null) parent.Children.Add(this);
        }

        /// <summary>轴对齐的父级缩放/flip 组合（测试替身不做旋转）。</summary>
        public Vector3 TransformVector(Vector3 value)
        {
            Vector3 world = parent != null ? parent.TransformVector(value) : value;
            return new Vector3(world.x * localScale.x, world.y * localScale.y, world.z * localScale.z);
        }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y = 0f, float z = 0f) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float scale) => new Vector3(a.x * scale, a.y * scale, a.z * scale);
        public static bool operator ==(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public override bool Equals(object other) => other is Vector3 v && this == v;
        public override int GetHashCode() => HashCode.Combine(x, y, z);
        public override string ToString() => $"({x},{y},{z})";
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static bool operator ==(Color left, Color right)
            => left.r == right.r && left.g == right.g && left.b == right.b && left.a == right.a;
        public static bool operator !=(Color left, Color right) => !(left == right);
        public override bool Equals(object other) => other is Color c && this == c;
        public override int GetHashCode() => HashCode.Combine(r, g, b, a);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public enum TextureFormat { RGBA32 = 4 }
    public enum FilterMode { Point = 0, Bilinear = 1 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1 }
    public enum SpriteMeshType { FullRect = 0, Tight = 1 }

    public static class Time
    {
        public static int frameCount;
        public static float time, deltaTime = 0.02f, timeScale = 1f;
    }

    public class Texture2D : Object
    {
        public static int CreatedCount, DestroyedCount;
        public static byte FillAlpha;
        /// <summary>可选逐像素不透明判定（纹理坐标）；为 null 时用 FillAlpha 的均匀 alpha。</summary>
        public Func<int, int, bool> OpaqueAt;

        public int width, height;
        public bool isReadable = true;
        public static int GetPixelsCalls;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public int anisoLevel;

        public Texture2D(int width, int height, TextureFormat format, bool mipChain)
        {
            this.width = width;
            this.height = height;
            CreatedCount++;
        }

        public Color[] GetPixels(int x, int y, int blockWidth, int blockHeight)
        {
            GetPixelsCalls++;
            var pixels = new Color[blockWidth * blockHeight];
            for (int row = 0; row < blockHeight; row++)
            {
                for (int col = 0; col < blockWidth; col++)
                {
                    bool opaque = OpaqueAt != null
                        ? OpaqueAt(x + col, y + row)
                        : FillAlpha > 0;
                    pixels[row * blockWidth + col] = new Color(1f, 1f, 1f, opaque ? 1f : 0f);
                }
            }
            return pixels;
        }

        public Color32[] GetPixels32()
        {
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, FillAlpha);
            return pixels;
        }

        /// <summary>程序生成的纹理（休闲金币）：记录写入内容与 Apply 次数，供资源/像素断言。</summary>
        public Color32[] WrittenPixels;
        public int ApplyCalls;

        public void SetPixels32(Color32[] pixels)
        {
            WrittenPixels = pixels == null ? null : (Color32[])pixels.Clone();
        }

        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) => ApplyCalls++;

        protected override void OnDestroyed() => DestroyedCount++;

        public static void ResetCounters() => CreatedCount = DestroyedCount = 0;
    }

    public static class ImageConversion
    {
        public static bool Result;
        public static int SimulatedWidth = 448, SimulatedHeight = 224;

        public static bool LoadImage(Texture2D texture, byte[] data, bool markNonReadable)
        {
            if (!Result) return false;
            texture.width = SimulatedWidth;
            texture.height = SimulatedHeight;
            return true;
        }

        public static void Reset()
        {
            Result = false;
            SimulatedWidth = 448;
            SimulatedHeight = 224;
        }
    }

    public class Sprite : Object
    {
        public static int CreatedCount, DestroyedCount, CreateCalls;
        public static int ThrowAtCreateIndex = -1;

        public Texture2D texture;
        public Rect rect;
        public Vector2 pivot;
        public float pixelsPerUnit;
        public bool packed;
        public Vector2[] vertices;

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit, uint extrude, SpriteMeshType meshType)
        {
            CreateCalls++;
            if (CreateCalls == ThrowAtCreateIndex)
                throw new InvalidOperationException("injected Sprite.Create failure");
            if (texture == null) return null;
            CreatedCount++;
            return new Sprite { texture = texture, rect = rect, pivot = pivot, pixelsPerUnit = pixelsPerUnit };
        }

        protected override void OnDestroyed() => DestroyedCount++;

        public static void ResetCounters()
        {
            CreatedCount = DestroyedCount = CreateCalls = 0;
            ThrowAtCreateIndex = -1;
        }
    }

    public class Shader : Object
    {
        public static int FindCalls;
        public static Shader Find(string shaderName) { FindCalls++; return new Shader { name = shaderName }; }
    }

    public class Material : Object
    {
        public Shader shader;
        public Material() { }
        public Material(Shader shader) { this.shader = shader; }
    }

    public class Renderer : Component
    {
        public bool enabled = true;
        public int sortingLayerID;
        public int sortingOrder;
        public Material sharedMaterial;
    }

    public class SpriteRenderer : Renderer
    {
        public static int SpriteWrites;
        public bool flipX, flipY;
        private Sprite current;

        public Sprite sprite
        {
            get => current;
            set { current = value; SpriteWrites++; }
        }
    }

    public struct Keyframe
    {
        public float time, value, inTangent, outTangent;

        public Keyframe(float time, float value) : this(time, value, 0f, 0f) { }

        public Keyframe(float time, float value, float inTangent, float outTangent)
        {
            this.time = time;
            this.value = value;
            this.inTangent = inTangent;
            this.outTangent = outTangent;
        }
    }

    // Production only authors linear secant tangents; a piecewise-linear evaluation is exact for
    // those keys and models the authored shape without importing Unity's curve editor.
    public class AnimationCurve
    {
        private readonly Keyframe[] _keys;

        public AnimationCurve(params Keyframe[] keys) => _keys = keys;

        public Keyframe[] keys => _keys;

        public float Evaluate(float time)
        {
            if (_keys == null || _keys.Length == 0) return 0f;
            if (!(time >= _keys[0].time)) return _keys[0].value;
            for (int i = 1; i < _keys.Length; i++)
            {
                if (time > _keys[i].time) continue;
                Keyframe a = _keys[i - 1];
                Keyframe b = _keys[i];
                float span = b.time - a.time;
                if (!(span > 0f)) return b.value;
                float t = (time - a.time) / span;
                return a.value + (b.value - a.value) * t;
            }
            return _keys[_keys.Length - 1].value;
        }
    }

    public class LineRenderer : Renderer
    {
        public bool useWorldSpace, loop;
        public int positionCount;
        public int numCapVertices, numCornerVertices;
        public float startWidth, endWidth, widthMultiplier = 1f;
        public AnimationCurve widthCurve;
        public Color startColor, endColor;
        public readonly List<Vector3> Positions = new();

        public void SetPosition(int index, Vector3 value)
        {
            while (Positions.Count <= index) Positions.Add(default);
            Positions[index] = value;
        }
    }
}

namespace KingdomEnhancedMod
{
    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance;
        public ManualLogSource LogSource = new ManualLogSource();
    }

    public class ManualLogSource
    {
        public readonly List<string> Warnings = new();
        public void LogWarning(string message) => Warnings.Add(message);
    }
}
