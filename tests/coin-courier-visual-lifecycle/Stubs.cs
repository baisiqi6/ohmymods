// Stubs.cs — 金币哥布林 visual-lifecycle 套件的边界替身（自 runtime-bridge 套件复制，按本套件升级）。
//
// 这些类型让"未修改的生产文件"（CoinCourierVisuals / CoinCourierRuntime / CoinCourierCoinFlight /
// CoinCourierRuntimeRules / CoinCourierCampaignState / CoinCourierShop 纯核 /
// CoinCourierPurse / CoinCourierRules / HeroShop 纯核）能编译并被确定性地驱动。
// UnityEngine.Object 按真实 Unity 语义建模：Destroy 后包装 == null（fake-null）；
// 销毁 GameObject 会级联销毁其组件与子物体；ImageConversion/图集纹理表面支持成功/失败注入，
// 以便复现"原生对象被卸载、托管缓存仍存活"的生命周期场景。
// 它们只模拟运行时真正触碰的边界：Unity 对象/物理查询、Managers/Kingdom/Knight/Banker、
// 网络/保存旗标、经济/目标/视觉/传送 FX/商店 Tick 的调用记录。绝不复制被测逻辑。
//
// 关键建模约定：
// - UnityEngine.Object 实现 Unity 的 fake-null（Destroy 后的包装 == null）。
// - Physics2D 用"地面 + 障碍区间"的简单世界：LinecastNonAlloc 返回真实 distance，
//   OverlapPoint 在障碍区间内返回非空 collider（掩码为 0 时恒空，供 fail-closed 反例）。
// - 经济/目标/视觉/FX 边界都是可编程记录器：运行时何时调用、用什么参数，由测试断言。
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Il2CppSystem
{
    /// <summary>interop 形状的单参委托替身（生产商店的 Player 回调）。</summary>
    public delegate void Action<in T>(T obj);
}

namespace Il2CppSystem.Collections.Generic
{
    /// <summary>interop 形状字典替身：只实现生产代码真正使用的成员。</summary>
    public class Dictionary<TKey, TValue>
    {
        private readonly System.Collections.Generic.Dictionary<TKey, TValue> _items =
            new System.Collections.Generic.Dictionary<TKey, TValue>();

        public int Count => _items.Count;

        public TValue this[TKey key]
        {
            get => _items[key];
            set => _items[key] = value;
        }

        public void Add(TKey key, TValue value) => _items.Add(key, value);
        public bool TryGetValue(TKey key, out TValue value) => _items.TryGetValue(key, out value);
        public bool ContainsKey(TKey key) => _items.ContainsKey(key);
        public bool Remove(TKey key) => _items.Remove(key);
        public void Clear() => _items.Clear();
    }

    /// <summary>interop 形状列表替身：只实现生产代码真正使用的成员。</summary>
    public class List<T>
    {
        private readonly System.Collections.Generic.List<T> _items = new System.Collections.Generic.List<T>();

        public int Count => _items.Count;

        public T this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        public void Add(T value) => _items.Add(value);
        public void Clear() => _items.Clear();
        public bool Remove(T value) => _items.Remove(value);
        public void RemoveAt(int index) => _items.RemoveAt(index);
    }
}

namespace Il2CppInterop.Runtime.InteropTypes
{
    /// <summary>interop 基类替身：Pointer 与 TryCast（本套件按 C# 类型归属直投）。</summary>
    public abstract class Il2CppObjectBase
    {
        private static long _nextPointer = 1;

        public IntPtr Pointer = new IntPtr(0x500000 + _nextPointer++);

        public T TryCast<T>() where T : class => this as T;
    }
}

namespace Coatsink.Common
{
    /// <summary>原生保存回执位（与真实 2.4 位值一致）；挑战删除原生体启动保存时使用。</summary>
    public enum SaveLoadResult
    {
        Delete = 16,
        Cancelled = 32,
        Success = 64,
        Failure = 128,
    }

    public static class Haglet
    {
        public enum State
        {
            Stopped = 0,
            Started = 1,
            Paused = 2,
            Completed = 3,
        }
    }

    public interface IHagletCallable
    {
        Haglet.State state { get; }
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type) { }
        public HarmonyPatch(Type type, string method) { }
        public HarmonyPatch(Type type, string method, Type[] argumentTypes) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyFinalizer : Attribute { }
}

namespace Il2CppInterop.Runtime.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor,
        AllowMultiple = false)]
    public class HideFromIl2CppAttribute : Attribute { }
}

namespace Il2CppInterop.Runtime.Injection
{
    /// <summary>IL2CPP ClassInjector 的最小替身：只记录注册状态，不做真实注入。</summary>
    public static class ClassInjector
    {
        private static readonly HashSet<Type> Registered = new HashSet<Type>();

        public static bool IsTypeRegisteredInIl2Cpp(Type type) => Registered.Contains(type);

        public static void RegisterTypeInIl2Cpp(Type type) { Registered.Add(type); }

        public static void RegisterTypeInIl2Cpp(Type type, RegisterTypeOptions options) { Registered.Add(type); }
    }

    public sealed class RegisterTypeOptions
    {
        public Type[] Interfaces;
    }
}

namespace UnityEngine
{
    public class Object
    {
        private static long _nextPointer = 1;
        internal bool Destroyed;
        public string name = "";
        public IntPtr Pointer = new IntPtr(_nextPointer++);

        public static int DestroyCalls;

        public static void Destroy(Object target)
        {
            if (target == null || target.Destroyed) return;   // fake-null：已销毁对象 == null
            DestroyCalls++;
            target.Destroyed = true;
            target.OnDestroyed();
            if (target is GameObject gameObject) gameObject.CascadeDestroy();
        }

        /// <summary>销毁回调（子类只做统计/清理，不改变 fake-null 语义）。</summary>
        protected virtual void OnDestroyed() { }

        public static void DontDestroyOnLoad(Object target) { }

        public static bool operator ==(Object a, Object b)
        {
            bool aNull = ReferenceEquals(a, null) || a.Destroyed;
            bool bNull = ReferenceEquals(b, null) || b.Destroyed;
            if (aNull || bNull) return aNull && bNull;
            return ReferenceEquals(a, b);
        }

        public static bool operator !=(Object a, Object b) => !(a == b);

        public override bool Equals(object other) => other is Object o && this == o;

        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 down => new Vector2(0f, -1f);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);
    }

    public struct Bounds
    {
        public Vector3 min, max;
        public Bounds(Vector3 min, Vector3 max) { this.min = min; this.max = max; }
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

    public enum FilterMode { Point, Bilinear }
    public enum TextureWrapMode { Repeat, Clamp }
    public enum SpriteMeshType { FullRect, Tight }
    public enum TextureFormat { RGBA32 }

    public class Transform : Object
    {
        public Vector3 position;
        public Vector3 localPosition;
        public Vector3 localScale = Vector3.one;
        public Transform parent;
        internal readonly List<Transform> Children = new List<Transform>();
        public GameObject gameObject { get; internal set; }

        public Transform(GameObject owner) { gameObject = owner; }

        public void SetParent(Transform newParent, bool worldPositionStays)
        {
            if (parent != null) parent.Children.Remove(this);
            parent = newParent;
            if (newParent != null) newParent.Children.Add(this);
        }

        public bool IsChildOf(Transform other)
        {
            for (Transform t = this; t != null; t = t.parent)
            {
                if (ReferenceEquals(t, other)) return true;
            }
            return false;
        }
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject != null ? gameObject.transform : null;

        public T GetComponent<T>() where T : class => gameObject != null ? gameObject.GetComponent<T>() : null;
        public T GetComponentInChildren<T>() where T : class
            => gameObject != null ? gameObject.GetComponentInChildren<T>() : null;
        public T Cast<T>() where T : class => KingdomEnhancedMod.InjectedOwnerInterfaces.Cast<T>(this);
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
    }

    public class MonoBehaviour : Behaviour
    {
        public MonoBehaviour() { }

        /// <summary>interop 注入类型（如 CoinCourierShopOwner）以 IntPtr 构造。</summary>
        public MonoBehaviour(IntPtr pointer)
        {
            Pointer = pointer;
        }
    }

    public class GameObject : Object
    {
        private static int _nextInstanceId = 1000;
        public static bool FailCourierProbeCreation;
        /// <summary>&gt;0：接下来的 KEM_CoinCourierView 根创建按次抛错（用于重建失败回归）。</summary>
        public static int FailViewRootCreation;
        /// <summary>view 根创建尝试次数（含被注入失败、未生成对象的尝试）。</summary>
        public static int ViewRootConstructorCalls;
        internal readonly List<Component> Components = new List<Component>();
        /// <summary>全部创建过的对象（含已销毁），供测试做生命周期/泄漏断言。</summary>
        public static readonly List<GameObject> All = new List<GameObject>();

        public Transform transform { get; }
        public int layer;
        public bool activeSelf = true;
        public bool activeInHierarchy => !Destroyed && activeSelf
            && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public readonly int InstanceId = _nextInstanceId++;

        public GameObject() : this("GameObject") { }

        public GameObject(string objectName)
        {
            if (FailCourierProbeCreation && objectName == "KEM_CoinCourierProbe")
                throw new InvalidOperationException("injected probe creation fault");
            if (objectName == "KEM_CoinCourierView")
            {
                ViewRootConstructorCalls++;
                if (FailViewRootCreation > 0)
                {
                    FailViewRootCreation--;
                    throw new InvalidOperationException("injected view root creation fault");
                }
            }
            name = objectName;
            transform = new Transform(this);
            All.Add(this);
        }

        /// <summary>原生销毁 GameObject 的真实级联：组件与子物体一并失效。</summary>
        internal void CascadeDestroy()
        {
            for (int i = 0; i < Components.Count; i++) Components[i].Destroyed = true;
            var children = new List<Transform>(transform.Children);
            for (int i = 0; i < children.Count; i++) Object.Destroy(children[i].gameObject);
        }

        public int GetInstanceID() => InstanceId;

        public void SetActive(bool value) { activeSelf = value; }

        public T AddComponent<T>() where T : Component
        {
            var component = (T)CreateComponent(typeof(T));
            component.gameObject = this;
            Components.Add(component);
            return component;
        }

        /// <summary>优先无参构造；interop 注入类型只有 IntPtr 构造时退回它。</summary>
        private static object CreateComponent(Type type)
        {
            try
            {
                return Activator.CreateInstance(type, nonPublic: true);
            }
            catch (MissingMethodException)
            {
                return Activator.CreateInstance(type,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic, null, new object[] { IntPtr.Zero }, null);
            }
        }

        public T GetComponent<T>() where T : class
        {
            for (int i = 0; i < Components.Count; i++)
            {
                if (Components[i] is T typed) return typed;
            }
            return null;
        }

        public T GetComponentInChildren<T>() where T : class => GetComponent<T>();
    }

    public class Collider2D : Component
    {
        public bool isTrigger;
        public Bounds bounds;   // Unity: Collider2D.bounds（世界 AABB）
    }

    public class BoxCollider2D : Collider2D
    {
        public Vector2 size;
    }

    public class Sprite : Object
    {
        public static int CreatedCount, DestroyedCount, CreateCalls;
        public static int ThrowAtCreateIndex = -1;
        public static int ReturnNullAtCreateIndex = -1;

        public Texture2D texture;

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit,
            uint extrude, SpriteMeshType meshType)
        {
            CreateCalls++;
            if (CreateCalls == ThrowAtCreateIndex)
                throw new InvalidOperationException("injected Sprite.Create failure");
            if (CreateCalls == ReturnNullAtCreateIndex) return null;
            CreatedCount++;
            return new Sprite { texture = texture };
        }

        protected override void OnDestroyed() => DestroyedCount++;

        public static void ResetCounters()
        {
            CreatedCount = DestroyedCount = CreateCalls = 0;
            ThrowAtCreateIndex = -1;
            ReturnNullAtCreateIndex = -1;
        }
    }

    public class Texture2D : Object
    {
        public static int CreatedCount, DestroyedCount;
        /// <summary>GetPixels32 的注入值（图集解码的"全透明即拒绝"门按它判定）。</summary>
        public static byte FillAlpha = 255;

        public int width, height;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public int anisoLevel;

        public Texture2D(int width, int height)
        {
            this.width = width; this.height = height;
            CreatedCount++;
        }

        public Texture2D(int width, int height, TextureFormat format, bool mipChain)
        {
            this.width = width; this.height = height;
            CreatedCount++;
        }

        public void SetPixels32(Color32[] pixels) { }

        public Color32[] GetPixels32()
        {
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, FillAlpha);
            return pixels;
        }

        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }

        protected override void OnDestroyed() => DestroyedCount++;

        public static void ResetCounters() { CreatedCount = DestroyedCount = 0; FillAlpha = 255; }
    }

    public class Shader : Object
    {
        public static Shader Find(string shaderName) => new Shader();
    }

    /// <summary>内嵌 PNG 解码替身：只注入结果与尺寸；真实字节仍走生产读流/长度校验路径。</summary>
    public static class ImageConversion
    {
        public static bool Result = true;
        public static int SimulatedWidth = 448, SimulatedHeight = 224;
        public static int LoadImageCalls;

        public static bool LoadImage(Texture2D texture, byte[] data, bool markNonReadable)
        {
            LoadImageCalls++;
            if (!Result) return false;
            texture.width = SimulatedWidth;
            texture.height = SimulatedHeight;
            return true;
        }

        public static void Reset()
        {
            Result = true;
            SimulatedWidth = 448;
            SimulatedHeight = 224;
            LoadImageCalls = 0;
        }
    }

    public class Material : Object
    {
        public Material(Shader shader) { }
    }

    public class Renderer : Component
    {
        public Material sharedMaterial;
        public bool enabled = true;
        public int sortingLayerID;
        public int sortingOrder;
    }

    public class SpriteRenderer : Renderer
    {
        public static int SpriteWrites;
        private Sprite _sprite;

        public Sprite sprite
        {
            get => _sprite;
            set { _sprite = value; SpriteWrites++; }
        }

        public Color color = Color.white;
    }

    public static class Time
    {
        public static float time;
        public static float unscaledTime;
        public static float deltaTime;
        public static float timeScale = 1f;
        public static int frameCount;
    }

    public static class Mathf
    {
        public static float Abs(float v) => Math.Abs(v);
        public static int Abs(int v) => Math.Abs(v);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
        public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
        public static int RoundToInt(float v) => (int)Math.Round(v);
        public static float Round(float v) => (float)Math.Round(v);
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 1e-5f;
    }

    public static class LayerMask
    {
        public static int GetMask(params string[] names)
        {
            int mask = 0;
            foreach (string name in names)
            {
                if (name == "Obstacles") mask |= 1 << 20;   // 与 2.4 原生 TagManager 一致
                else if (name == "Enemies") mask |= 1 << 9;
            }
            return mask;
        }
    }

    public struct RaycastHit2D
    {
        public Collider2D collider;
        public float distance;
    }

    public static class Physics2D
    {
        public static int LinecastNonAlloc(Vector2 from, Vector2 to, object buffer, int layerMask)
        {
            if (buffer == null) return 0;
            var hits = (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<RaycastHit2D>)buffer;
            if (!SimPhysics.Enabled || layerMask == 0) return 0;
            if (!SimPhysics.HasGround(from.x)) return 0;
            if (hits.Length < 1) return 0;
            var ground = SimPhysics.Ground;
            hits[0] = new RaycastHit2D { collider = ground, distance = from.y - SimPhysics.GroundTop };
            return 1;
        }

        /// <summary>完整列表查询：返回与矩形相交的建模 collider（含触发体，由生产侧过滤）。</summary>
        public static Collider2D[] OverlapAreaAll(Vector2 pointA, Vector2 pointB, int layerMask)
        {
            if (!SimPhysics.Enabled || layerMask == 0) return Array.Empty<Collider2D>();
            float minX = Math.Min(pointA.x, pointB.x), maxX = Math.Max(pointA.x, pointB.x);
            float minY = Math.Min(pointA.y, pointB.y), maxY = Math.Max(pointA.y, pointB.y);
            var result = new System.Collections.Generic.List<Collider2D>();
            for (int i = 0; i < SimPhysics.Shapes.Count; i++)
            {
                SimPhysics.Shape shape = SimPhysics.Shapes[i];
                if (shape.MaxX < minX || shape.MinX > maxX || shape.MaxY < minY || shape.MinY > maxY) continue;
                result.Add(shape.Collider);
            }
            return result.ToArray();
        }
    }

    /// <summary>测试世界模型：地面存在性/高度 + 真实 bounds 的障碍 collider（生产只通过 Physics2D 看到它）。</summary>
    public static class SimPhysics
    {
        public sealed class Shape
        {
            public float MinX, MaxX, MinY, MaxY;
            public bool IsTrigger;
            public BoxCollider2D Collider;

            public Shape(float minX, float maxX, float minY, float maxY, bool isTrigger)
            {
                MinX = minX; MaxX = maxX; MinY = minY; MaxY = maxY; IsTrigger = isTrigger;
                Collider = new BoxCollider2D
                {
                    gameObject = new GameObject("Obstacle"),
                    isTrigger = isTrigger,
                    bounds = new Bounds(new Vector3(minX, minY, 0f), new Vector3(maxX, maxY, 0f)),
                };
            }
        }

        public static bool Enabled = true;
        public static float GroundTop = 0.5f;
        public static Func<float, bool> HasGround = _ => true;
        public static readonly List<Shape> Shapes = new List<Shape>();
        public static BoxCollider2D Ground;

        public static Shape AddObstacle(float minX, float maxX, float minY = 0f, float maxY = 6f,
            bool isTrigger = false)
        {
            var shape = new Shape(minX, maxX, minY, maxY, isTrigger);
            Shapes.Add(shape);
            return shape;
        }

        public static void Reset()
        {
            Enabled = true;
            GroundTop = 0.5f;
            HasGround = _ => true;
            Shapes.Clear();
            Ground = null;
        }
    }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppStructArray<T> where T : struct
    {
        private readonly T[] _items;
        public Il2CppStructArray(long size) { _items = new T[size]; }
        public int Length => _items.Length;
        public T this[int index] { get => _items[index]; set => _items[index] = value; }
    }
}

namespace BepInEx.Configuration
{
    public class ConfigEntry<T>
    {
        public T Value;
        public ConfigEntry() { }
        public ConfigEntry(T value) { Value = value; }
    }
}

namespace KingdomEnhancedMod
{
    // ---- 配置边界（键默认值与生产 ModConfig 一致；测试可改写） ----
    internal static class ModConfig
    {
        public static ConfigEntry<bool> Enabled = new ConfigEntry<bool>(true);
        public static ConfigEntry<bool> CoinCourierEnabled = new ConfigEntry<bool>(true);
        public static ConfigEntry<int> CoinCourierRecruitPrice = new ConfigEntry<int>(8);
        public static ConfigEntry<int> CoinCourierPurseCapacity = new ConfigEntry<int>(12);
        public static ConfigEntry<int> CoinCourierMaxCoinsPerVisit = new ConfigEntry<int>(4);
        public static ConfigEntry<float> CoinCourierKnightCooldown = new ConfigEntry<float>(15f);
    }

    public sealed class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance;
        public LogSink LogSource = new LogSink();

        public sealed class LogSink
        {
            public readonly List<string> Messages = new List<string>();
            public void LogInfo(string message) { Messages.Add("INFO " + message); }
            public void LogWarning(string message) { Messages.Add("WARN " + message); }
            public void LogError(string message) { Messages.Add("ERR " + message); }
        }
    }

    // ---- 游戏边界 ----
    public enum CurrencyType { Coins = 0, Gems = 1 }

    public class DroppableCurrency : UnityEngine.Object
    {
        public CurrencyType CurrencyType;
    }

    public enum Side { Left = -1, Right = 1 }

    public class Game : UnityEngine.Object
    {
        public enum State { Intro, Playing, Menu, Loss, Quitting }
        public State state = State.Playing;
        public static bool SavingEnabled = true;
        public int currentLand;
        /// <summary>两个原生保存提示 Haglet（持久化评估读取它们是否空闲）。</summary>
        public Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase _saveGameWithFailurePromptRoutine;
        public Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase saveGameWithFailurePrompt;
    }

    public class World
    {
        private static long _nextPointer = 1;
        public static BoxCollider2D GroundCollider;
        public Transform gameLayer;
        public IntPtr Pointer = new IntPtr(0x7000 + _nextPointer++);
    }

    public class Kingdom : UnityEngine.Object
    {
        public float campfirePosition;
        public bool isDaytime = true;
        public Banker banker;
        public Player playerOne;
        public Player playerTwo;

        /// <summary>注入上下文读取异常（异常路径回归：Tick 进入 catch 且重标来源同样失败）。</summary>
        public bool ThrowOnBorderRead;

        private bool _hasBorderLoaded = true;

        public bool HasBorderLoaded
        {
            get => ThrowOnBorderRead
                ? throw new InvalidOperationException("injected border read fault")
                : _hasBorderLoaded;
            set => _hasBorderLoaded = value;
        }
    }

    public class Managers
    {
        public static Managers Inst;
        public Game game;
        public World world;
        public Kingdom kingdom;
        public PayableManager payables;
    }

    public class Wallet : UnityEngine.Component
    {
        public int Coins;
        public int TotalCapacity = 8;

        public void AddCurrency(CurrencyType type, int amount)
        {
            if (type == CurrencyType.Coins) Coins = Coins + amount > TotalCapacity ? TotalCapacity : Coins + amount;
        }
    }

    public class Knight : UnityEngine.Component
    {
        public Side side = Side.Right;
        public Wallet Wallet;
    }

    public class Banker : UnityEngine.Component
    {
        public int _stashedCoins;
    }

    // ---- 商店（PayableComponent/NetworkPostbox）边界：只让完整 Shop 编译并可控装配 ----

    public enum CRPCType
    {
        Static = 0,
        Dynamic = 1,
        SemiStatic = 2,
    }

    public class CRPCHeader : UnityEngine.Object
    {
        public short NetID;
        public int netID;
        public CRPCType HeaderType;
        public GameObject referencedGO;
        public List<NetworkPostbox.DynAction> RemoteMethodList = new List<NetworkPostbox.DynAction>();
    }

    /// <summary>
    /// 原生 NetworkPostbox 的最小替身：登记/注销与两张查表按生产 HeaderMatches 的读取形状建模。
    /// </summary>
    public class NetworkPostbox : UnityEngine.Object
    {
        /// <summary>原生 interop 的无参动态委托替身（RPC 槽位表元素）。</summary>
        public delegate void DynAction();

        public static NetworkPostbox Instance;

        public readonly Dictionary<GameObject, CRPCHeader> MasterSemiCRPCHLookup =
            new Dictionary<GameObject, CRPCHeader>();
        public readonly Dictionary<short, CRPCHeader> SemiStaticObjects =
            new Dictionary<short, CRPCHeader>();
        public readonly Dictionary<short, CRPCHeader> DynamicObjects =
            new Dictionary<short, CRPCHeader>();

        private short _nextSemi = 100;

        public CRPCHeader RegisterObject(GameObject owner, CRPCType type)
        {
            var header = new CRPCHeader
            {
                NetID = type == CRPCType.SemiStatic ? _nextSemi++ : (short)903,
                HeaderType = type,
                referencedGO = owner,
            };
            header.netID = header.NetID;
            header.RemoteMethodList.Add(null);
            header.RemoteMethodList.Add(null);
            header.RemoteMethodList.Add(null);
            var payable = owner.GetComponent<PayableComponent>();
            if (payable != null) payable.parentHeaderRef = header;
            if (type == CRPCType.SemiStatic)
            {
                MasterSemiCRPCHLookup[owner] = header;
                SemiStaticObjects[header.NetID] = header;
            }
            else
            {
                DynamicObjects[header.NetID] = header;
            }
            return header;
        }

        public void DeregisterObject(GameObject owner, short netId, CRPCType type)
        {
            MasterSemiCRPCHLookup.Remove(owner);
            SemiStaticObjects.Remove(netId);
        }
    }

    public class CRPCStamp : UnityEngine.Component { }

    public static class LockIndicator
    {
        public enum LockReason
        {
            Invalid = 0,
            NotLocked = 21,
        }
    }

    public interface IPayableComponentOwner
    {
        Il2CppSystem.Action<Player> OnPay { get; }
        bool CanPay(Player player);
        bool IsLocked(Player player, out LockIndicator.LockReason reason);
    }

    /// <summary>
    /// 注入 owner 的接口桥：真实 IL2CPP 由 ClassInjector 按 Interfaces 注册，Cast 返回接口包装；
    /// 测试替身没有这套运行时注入，这里把生产 <c>CoinCourierShopOwner</c> 的三个回调显式桥到
    /// 同名成员（IsLocked 语义与 HeroShopOwnerInterop 一致：未锁定并已写回 NotLocked）。
    /// 未引用 owner 的构型下只是普通的 <c>as T</c>。
    /// </summary>
    internal static class InjectedOwnerInterfaces
    {
        internal static T Cast<T>(object target) where T : class
        {
            if (target is T typed) return typed;
#if COURIER_FULL_SHOP
            if (target is CoinCourierShopOwner owner && typeof(T) == typeof(IPayableComponentOwner))
                return (T)(object)new CoinCourierShopOwnerInterface(owner);
#endif
            return null;
        }

#if COURIER_FULL_SHOP
        private sealed class CoinCourierShopOwnerInterface : IPayableComponentOwner
        {
            private readonly CoinCourierShopOwner _owner;
            internal CoinCourierShopOwnerInterface(CoinCourierShopOwner owner) { _owner = owner; }
            public Il2CppSystem.Action<Player> OnPay => _owner.OnPay;
            public bool CanPay(Player player) => _owner.CanPay(player);
            public bool IsLocked(Player player, out LockIndicator.LockReason reason)
            {
                reason = LockIndicator.LockReason.NotLocked;
                return false;
            }
        }
#endif
    }

    public class PayableComponent : UnityEngine.Component
    {
        public int Price;
        public bool forceBlockPayment;
        public CurrencyType Currency;
        public int priceIncrease;
        public float indicatorSpacing;
        public Vector2 indicatorOffset;
        public bool repositionInSplitScreen;
        public Vector2 playerPayPointOffset;
        public float playerPayDistance;
        public Vector2 payablePlacementExclusionOffset;
        public float payablePlacementExclusionDistance;
        public bool glowOnSelect;
        public bool needsNewNetId;
        public CRPCHeader parentHeaderRef;

        private Il2CppSystem.Action<Player> _started;

        public void Init(IPayableComponentOwner owner) { }

        public void add_OnTransactionStartedCallback(Il2CppSystem.Action<Player> callback) => _started = callback;

        public void remove_OnTransactionStartedCallback(Il2CppSystem.Action<Player> callback)
        {
            if (ReferenceEquals(_started, callback)) _started = null;
        }
    }

    public class Player : UnityEngine.Component
    {
        public enum PayState { None = 0, Started = 1, Completed = 2 }

        public bool hasLocalAuthority = true;
        public Wallet wallet;
        public PayState _payState;
        public PayableComponent _completingPayable;
        public PayableComponent selectedPayable;
        public List<DroppableCurrency> _floatingCurrency = new List<DroppableCurrency>();

        public void CancelTransaction() { }
        public void DropFloatingCurrency() { _floatingCurrency.Clear(); }
        public void DeselectPayable() { selectedPayable = null; }
    }

    /// <summary>
    /// 原生 Payable 的最小替身：当前固定锚策略不读取付款对象/间距；夹具用它证明
    /// "建筑预留与付款点覆盖固定点也不再拒绝创建"（若未来重新引入这类门，断言会失败）。
    /// </summary>
    public class Payable : UnityEngine.MonoBehaviour
    {
        public float playerPayDistance;
        public float PayPointX;

        public float PlayerPayPoint() => PayPointX;
    }

    public class PayableManager : UnityEngine.Object
    {
        public Payable[] AllPayables;
    }

    public static class NetworkBigBoss
    {
        public static bool HasWorldAuth = true;
        public static bool IsOnline;
    }

    // ---- 持久化边界：GlobalSaveData/prefs/campaign/island 的最小可驱动替身 ----

    public class GlobalSaveData : UnityEngine.Object
    {
        public static GlobalSaveData _loaded;

        public int currentCampaign;
        public int currentChallenge;
        public PrefsSaveData prefs = new PrefsSaveData();
        public Il2CppSystem.Collections.Generic.List<CampaignSaveData> campaigns =
            new Il2CppSystem.Collections.Generic.List<CampaignSaveData>();

        public CampaignSaveData CreateNewCampaign() => new CampaignSaveData();

        public void TryDeleteCampaignAsync() { }

        /// <summary>删除协程状态机（生产 Harmony 只挂 MoveNext 记账，本套件不驱动）。</summary>
        public sealed class __TryDeleteCampaign_d__91
        {
            public int __1__state;
            public void MoveNext() { }
        }

        public Il2CppSystem.Collections.Generic.List<CampaignSaveData> challenges =
            new Il2CppSystem.Collections.Generic.List<CampaignSaveData>();

        /// <summary>
        /// 实际 2.4 挑战删除的同步入口（static DeleteChallenge(int)）：按 challenge ID 找到
        /// 目录项 RemoveAt 后启动保存；生产侧只挂 prefix 记账（SharedBankNative.BeforeMutation），
        /// 本套件不驱动原生体。
        /// </summary>
        public static void DeleteChallenge(int challengeId)
        {
            GlobalSaveData global = _loaded;
            if (global == null) return;
            global.NativeDeleteChallenge(challengeId);
            global.SaveAsync(null);
        }

        /// <summary>
        /// 实际 2.4 协程状态机（GlobalSaveData/__TryDeleteChallenge_d__94）：state0 首次执行时
        /// 按 ID RemoveAt → SaveAsync；生产只挂 MoveNext state0 的 prefix。
        /// </summary>
        public sealed class __TryDeleteChallenge_d__94
        {
            public int __1__state;
            public int challengeId;
            public GlobalSaveData Owner;

            public bool MoveNext()
            {
                if (__1__state != 0) return false;
                Owner?.NativeDeleteChallenge(challengeId);
                Owner?.SaveAsync(null);
                __1__state = 1;
                return true;
            }
        }

        private void NativeDeleteChallenge(int challengeId)
        {
            for (int i = 0; i < challenges.Count; i++)
            {
                CampaignSaveData item = challenges[i];
                if (item != null && item.challengeId == challengeId)
                {
                    challenges.RemoveAt(i);
                    break;
                }
            }
        }

        public void SaveAsync(Il2CppSystem.Action<Coatsink.Common.SaveLoadResult> callback)
        {
            prefs.PrepareBeforeSave();
            callback?.Invoke(Coatsink.Common.SaveLoadResult.Success);
        }
    }

    public class CampaignSaveData : UnityEngine.Object
    {
        public static CampaignSaveData current;
        public int CurrentLand = 1;
        public int challengeId;
    }

    public class PrefsSaveData : UnityEngine.Object
    {
        public Il2CppSystem.Collections.Generic.Dictionary<string, string> contents =
            new Il2CppSystem.Collections.Generic.Dictionary<string, string>();

        /// <summary>写入失败注入（Prepare/捕获链的 fail-closed 用例）。</summary>
        public bool ThrowOnSet;

        public void SetString(string key, string value)
        {
            if (ThrowOnSet) throw new InvalidOperationException("prefs set fault");
            contents[key] = value;
        }

        public void PrepareBeforeSave()
        {
#if COURIER_REAL_PERSISTENCE
            CoinCourierPersistence.ObservePrepare(this);
#endif
        }
    }

    /// <summary>IslandSaveData 替身：Save 按真实顺序驱动持久化边界入口（scope → 标记 → 收尾）。</summary>
    public class IslandSaveData : UnityEngine.Object
    {
        public static readonly IslandSaveData Instance = new IslandSaveData();

        public static bool isSavingGame;
        public static IslandSaveData CurrentlySavingIsland;

        public static void Save(int campaign, int land, int challenge)
        {
            isSavingGame = true;
            CurrentlySavingIsland = Instance;
            try
            {
#if COURIER_REAL_PERSISTENCE
                CoinCourierPersistence.SaveScope scope = CoinCourierPersistence.BeginIslandSave(campaign, land, challenge);
#endif
                Instance.UpdateSavedWithRevisions();
#if COURIER_REAL_PERSISTENCE
                CoinCourierPersistence.EndIslandSave(scope, true);
#endif
            }
            finally
            {
                isSavingGame = false;
                CurrentlySavingIsland = null;
            }
        }

        /// <summary>真实 UpdateSavedWithRevisions 的 mark 观察点。</summary>
        public void UpdateSavedWithRevisions()
        {
#if COURIER_REAL_PERSISTENCE
            CoinCourierPersistence.ObserveMarker(this);
#endif
        }
    }

    /// <summary>可编程 Haglet 替身：持久化评估的"保存提示协程是否忙"读取。</summary>
    public sealed class HagletStub : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase,
        Coatsink.Common.IHagletCallable
    {
        public Coatsink.Common.Haglet.State state { get; set; } = Coatsink.Common.Haglet.State.Stopped;
    }

    public static class UnitScanCache
    {
        public static Knight[] Knights = Array.Empty<Knight>();
        public static Knight[] GetKnights(float maxAgeSec = 3f) => Knights;
    }

    public static class Scanner
    {
        public static GameObject Enemy;
        public static int ScanCalls;
        public static bool ThrowOnScan;

        public static GameObject ScanClosest(Transform observer, float range, int layers, string[] tags,
            float rangeBehind, float height, bool excludeDead)
        {
            ScanCalls++;
            if (ThrowOnScan) throw new InvalidOperationException("injected scanner fault");
            return Enemy;
        }
    }

    // ---- 边界记录器：经济/目标/银行资格/视觉/传送 FX/商店 Tick ----
    internal static class CoinCourierEconomy
    {
        internal static bool BagApplied;
        internal static CoinCourierReason BagRefusal = CoinCourierReason.BankEmpty;
        internal static bool DeliverApplied;
        internal static CoinCourierStatus DeliverStatus = CoinCourierStatus.Applied;
        internal static CoinCourierReason DeliverReason = CoinCourierReason.None;
        internal static int BagCalls;
        internal static int BagAppliedCount;
        internal static readonly List<float> BagPositions = new List<float>();
        internal static int DeliverCalls;
        internal static int DeliverAppliedCount;
        internal static readonly List<long> DeliverLives = new List<long>();
        internal static readonly List<float> DeliverPositions = new List<float>();
        internal static readonly List<float> DeliverTargetXs = new List<float>();

        internal static CoinCourierResult TryBagOneCoin(CoinCourierPurse purse, Banker banker, int purseCapacity)
        {
            BagCalls++;
            BagPositions.Add(CourierVisualProbe.LastPosition.x);
            if (banker == null)   // 与生产一致：无权威本体时明确拒绝，绝不记账
                return new CoinCourierResult(CoinCourierStatus.NotApplied, CoinCourierReason.InvalidArgument);
            if (!BagApplied)
                return new CoinCourierResult(CoinCourierStatus.NotApplied, BagRefusal);
            CoinCourierResult credit = purse.TryCreditTakenCoin(purseCapacity);
            if (credit.Applied) BagAppliedCount++;
            return credit.Applied ? credit
                : new CoinCourierResult(CoinCourierStatus.NotApplied, credit.Reason);
        }

        internal static CoinCourierResult TryDeliverOne(CoinCourierPurse purse, Knight knight, long expectedLife)
        {
            DeliverCalls++;
            DeliverLives.Add(expectedLife);
            DeliverPositions.Add(CourierVisualProbe.LastPosition.x);
            DeliverTargetXs.Add(knight != null ? knight.transform.position.x : float.NaN);
            if (DeliverStatus == CoinCourierStatus.Applied && DeliverApplied)
            {
                CoinCourierResult reserve = purse.TryReserveOneForDelivery(expectedLife);
                if (!reserve.Applied)
                    return new CoinCourierResult(CoinCourierStatus.NotApplied, reserve.Reason);
                CoinCourierResult complete = purse.TryCompleteDelivery(expectedLife);
                if (complete.Applied) DeliverAppliedCount++;
                return complete.Applied ? complete
                    : new CoinCourierResult(CoinCourierStatus.NotApplied, complete.Reason);
            }
            if (DeliverStatus == CoinCourierStatus.Indeterminate)
            {
                purse.MarkUnknown(new CoinCourierFault(CoinCourierFaultKind.DeliveryUnknown,
                    CoinCourierReason.WalletWriteUnknown, -1, -1, -1, -1, expectedLife));
                return new CoinCourierResult(CoinCourierStatus.Indeterminate, CoinCourierReason.WalletWriteUnknown);
            }
            return new CoinCourierResult(CoinCourierStatus.NotApplied, DeliverReason);
        }

        internal static void Reset()
        {
            BagApplied = false;
            BagPositions.Clear();
            BagRefusal = CoinCourierReason.BankEmpty;
            DeliverApplied = false;
            DeliverStatus = CoinCourierStatus.Applied;
            DeliverReason = CoinCourierReason.None;
            BagCalls = 0;
            BagAppliedCount = 0;
            DeliverCalls = 0;
            DeliverAppliedCount = 0;
            DeliverLives.Clear();
            DeliverPositions.Clear();
            DeliverTargetXs.Clear();
        }
    }

    internal static class CoinCourierTargeting
    {
        internal static bool SelectResult;
        internal static Knight Target;
        internal static CoinCourierRules.VisitPlan Plan;
        internal static bool CandidateValid = true;
        internal static int SelectCalls;

        internal static bool TrySelect(int purseCoins, float now, int lastServedSide, int maxCoinsPerVisit,
            IReadOnlyDictionary<long, float> nextEligibleAt, out Knight target,
            out CoinCourierRules.VisitPlan plan)
        {
            SelectCalls++;
            target = null;
            plan = default;
            if (!SelectResult) return false;
            if (nextEligibleAt != null && nextEligibleAt.TryGetValue(Plan.LifeId, out float readyAt)
                && now < readyAt) return false;   // 冷却中的骑士不可再选（与真实名册同语义）
            target = Target;
            plan = Plan;
            return true;
        }

        internal static bool IsCurrentDeliveryCandidate(Knight knight, long expectedLife, out Wallet wallet)
        {
            wallet = knight != null ? knight.Wallet : null;
            return CandidateValid && knight != null && wallet != null;
        }

        internal static void Reset()
        {
            SelectResult = false;
            Target = null;
            Plan = default;
            CandidateValid = true;
            SelectCalls = 0;
        }
    }

    /// <summary>
    /// 解析器的桥接替身：真实 903 登记矩阵由 economy 套件链接生产 resolver 覆盖；
    /// 本套件只控制"当前是否存在权威银行"与拒因，驱动运行时的等待/配送分支。
    /// </summary>
    internal static class CoinCourierBankScope
    {
        internal static Banker Current;
        internal static CoinCourierBankReason Reason = CoinCourierBankReason.EntryMissing;

        internal static bool TryGetCurrentAuthorityBanker(Kingdom kingdom, out Banker banker,
            out CoinCourierBankReason reason)
        {
            banker = Current;
            reason = Current != null ? CoinCourierBankReason.Ready : Reason;
            return Current != null;
        }

        internal static bool IsCurrentAuthorityBanker(Banker banker)
            => Current != null && banker != null && banker.Pointer == Current.Pointer;

        internal static void Reset()
        {
            Current = null;
            Reason = CoinCourierBankReason.EntryMissing;
        }
    }

    /// <summary>Scheduling hint boundary; unknown preserves the transaction's own read/prime path.</summary>
    internal static class PatchEconomy_Banker
    {
        internal static bool BalanceKnown;

        internal static bool TryReadCourierStash(Banker banker, out int coins)
        {
            coins = banker != null ? banker._stashedCoins : 0;
            return BalanceKnown && banker != null;
        }

        internal static void Reset() => BalanceKnown = false;
    }

    /// <summary>
    /// 中性 disabled 边界替身：本套件不覆盖共享银行账本算法（真实 SharedBankNative/R3 由
    /// tests/coin-courier-economy 与 tests/shared-bank-regressions 直接链接生产源验证）。
    /// 这里只让 CoinCourierPersistence 新增的银行接线保持同签名 no-op，绝不能被当作银行行为
    /// 已在本套件被验证。
    /// </summary>
    internal static class SharedBankNative
    {
        internal sealed class Scope { }

        internal static Scope BeginSave(int campaign, int land, int challenge) => new Scope();
        internal static void EndSave(Scope scope, bool normal) { }
        internal static void Marker(IslandSaveData island) { }
        internal static void BeforeMutation(GlobalSaveData value) { }
    }

    /// <summary>拒因枚举替身：本套件不链接生产 BankScope，只在边界起名字作用。</summary>
    internal enum CoinCourierBankReason
    {
        Ready = 0,
        ModDisabled,
        NoWorldAuthority,
        NotPlaying,
        UnknownBiome,
        PostboxMissing,
        EntryMissing,
        EntryMismatch,
        BankerUnavailable,
        NativeConflict,
        ReadFault,
    }

#if !COURIER_REAL_PERSISTENCE
    /// <summary>
    /// 商店/桥接套件不链接真实存档 owner 时的契约入口替身：没有当前 global 诊断，
    /// 也不携带任何缓存状态（真实实现只在链接 CoinCourierPersistence 的套件中验证）。
    /// </summary>
    internal static class CoinCourierPersistence
    {
        internal static bool TryGetCurrentClosedDiagnostic(out CoinCourierAvailabilityInfo info)
        {
            info = default;
            return false;
        }

        internal static void Reset() { }
    }
#endif

    internal readonly struct CoinCourierFxHandle
    {
        internal readonly int Slot;
        internal readonly long Generation;
        internal CoinCourierFxHandle(int slot, long generation) { Slot = slot; Generation = generation; }
        internal bool IsValid => Slot >= 0;
    }

    /// <summary>生产 CoinCourierTeleportStyle 的桥接替身（本套件不编译生产 FX 文件）。</summary>
    internal enum CoinCourierTeleportStyle
    {
        Horizontal,
        Vertical
    }

    internal static class CoinCourierTeleportFx
    {
        internal static int BeginCalls;
        internal static int CancelCalls;
        internal static int TickCalls;
        internal static int ClearCalls;
        internal static CoinCourierFxHandle LastHandle;
        internal static CoinCourierTeleportStyle LastStyle;

        internal static CoinCourierFxHandle Begin(Vector3 worldPosition, Color color, float scale)
            => Begin(worldPosition, color, scale, 0, 0);

        internal static CoinCourierFxHandle Begin(Vector3 worldPosition, Color color, float scale,
            int sortingLayerID, int sortingOrder)
            => Begin(worldPosition, color, scale, sortingLayerID, sortingOrder,
                CoinCourierTeleportStyle.Horizontal);

        internal static CoinCourierFxHandle Begin(Vector3 worldPosition, Color color, float scale,
            int sortingLayerID, int sortingOrder, CoinCourierTeleportStyle style)
        {
            BeginCalls++;
            LastStyle = style;
            LastHandle = new CoinCourierFxHandle(BeginCalls, BeginCalls);
            return LastHandle;
        }

        internal static void Cancel(CoinCourierFxHandle handle) { CancelCalls++; }
        internal static void Tick(float gameDelta) { TickCalls++; }
        internal static void Clear() { ClearCalls++; }
        internal static int ActiveCount => 0;

        internal static void Reset()
        {
            BeginCalls = 0;
            CancelCalls = 0;
            TickCalls = 0;
            ClearCalls = 0;
            LastHandle = default;
            LastStyle = CoinCourierTeleportStyle.Horizontal;
        }
    }

#if !COURIER_FULL_SHOP
    /// <summary>商店 Unity 部分在 core-only 模式下不编译；运行时只需要 Tick 与保存前取消边界。</summary>
    internal static class CoinCourierShop
    {
        internal static int TickCalls;
        internal static int CancelCalls;
        internal static string StatusText => "bridge-stub";
        internal static void Tick() { TickCalls++; }
        internal static void CancelPendingTransactions() { CancelCalls++; }
        internal static void Reset() { TickCalls = 0; CancelCalls = 0; }
    }
#endif
}
