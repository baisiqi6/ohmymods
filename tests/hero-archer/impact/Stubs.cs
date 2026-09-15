// Native-boundary and Unity-surface stubs for the impact-visual regression suite.
// This file models only what the production module and the mirrored native
// Arrow.HitObject path touch; it is not a Unity emulator.

using System.Collections.Generic;

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    /// <summary>Mirror of the real Il2CppInterop array: ctor(long), indexer, Length, implicit from T[].
    /// The implicit is instrumented so a regression that copies managed arrays into native ones
    /// per frame shows up as allocation growth.</summary>
    public class Il2CppStructArray<T>
    {
        public static int Allocations, ManagedCopies;
        private readonly T[] items;
        public Il2CppStructArray(long size) { items = new T[size]; Allocations++; }
        public Il2CppStructArray(T[] source) { items = (T[])source.Clone(); ManagedCopies++; Allocations++; }
        public int Length => items.Length;
        public T this[int index] { get => items[index]; set => items[index] = value; }
        public static implicit operator Il2CppStructArray<T>(T[] array) => new(array);
        public static implicit operator T[](Il2CppStructArray<T> array) => array.items;
    }
}

namespace HarmonyLib
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : System.Attribute
    {
        public readonly System.Type TargetType;
        public readonly string MethodName;
        public HarmonyPatch(System.Type type, string name) { TargetType = type; MethodName = name; }
    }
    public class HarmonyPrefix : System.Attribute { }
    public class HarmonyPostfix : System.Attribute { }
    public class HarmonyFinalizer : System.Attribute { }
}

namespace UnityEngine
{
    public class Object
    {
        private static long pointers;
        public static int DestroyRequests, DoubleDestroys;
        public static readonly List<Object> Created = new();
        public static readonly List<Object> DestroyedObjects = new();
        public string name = "Object";
        public bool Destroyed;
        public System.IntPtr Pointer { get; set; }

        protected Object()
        {
            Pointer = (System.IntPtr)System.Threading.Interlocked.Increment(ref pointers);
            Created.Add(this);
        }

        public static void Destroy(Object obj)
        {
            if (obj == null) return;
            if (obj.Destroyed) { DoubleDestroys++; return; }
            DestroyRequests++;
            DestroyedObjects.Add(obj);
            obj.Destroyed = true;
            if (obj is GameObject go) go.DestroyDeep();
        }

        public static implicit operator bool(Object obj) => obj is not null && !obj.Destroyed;
        public static bool operator ==(Object a, Object b) =>
            ReferenceEquals(a, b) || ((a is null || a.Destroyed) && (b is null || b.Destroyed));
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }

    public class GameObject : Object
    {
        private static int sequence;
        private readonly List<Component> components = new();
        public readonly int Id;
        public Transform transform;
        public bool activeSelf = true, activeInHierarchy = true;
        public string tag = "Citizen";
        public int layer;

        // Test instrumentation: one record per explicit SetActive(true).
        public static int SetActiveTrueCalls;
        public static readonly List<(int Frame, float Time, string Name)> Activations = new();

        public GameObject(string name = "GameObject")
        {
            this.name = name;
            Id = ++sequence;
            transform = new Transform { gameObject = this };
        }

        public void SetActive(bool value)
        {
            activeSelf = value;
            activeInHierarchy = value;
            if (!value) return;
            SetActiveTrueCalls++;
            Activations.Add((Time.frameCount, Time.time, name));
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : class => components.Find(c => c is T) as T;

        /// <summary>Test-only: drop a component the way a pooled/destroyed target loses it mid-hit.</summary>
        public void RemoveComponent<T>() where T : class => components.RemoveAll(c => c is T);

        public bool TryGetComponent<T>(out T component) where T : class
        {
            component = GetComponent<T>();
            return component != null;
        }

        public T GetComponentInParent<T>() where T : class
        {
            for (Transform t = transform; t != null; t = t.parent)
            {
                T found = t.gameObject.GetComponent<T>();
                if (found != null) return found;
            }
            return null;
        }

        public bool CompareTag(string other) => tag == other;

        public int GetInstanceID() => Id;

        public void DestroyDeep()
        {
            foreach (var component in components) component.Destroyed = true;
            transform.Destroyed = true;
            foreach (var child in transform.children.ToArray()) child.gameObject.DestroyDeep();
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public string tag => gameObject.tag;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T GetComponentInParent<T>() where T : class => gameObject.GetComponentInParent<T>();
    }

    public class MonoBehaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }

    public class Behaviour : Component { public bool enabled = true; }

    public class Transform : Component
    {
        public static bool ThrowOnSetParent;
        public Vector3 position, localPosition, localScale = Vector3.one;
        public Quaternion rotation = Quaternion.identity, localRotation = Quaternion.identity;
        public Transform parent;
        public readonly List<Transform> children = new();

        public void SetParent(Transform newParent, bool worldPositionStays = true)
        {
            if (ThrowOnSetParent) throw new System.InvalidOperationException("injected SetParent failure");
            parent?.children.Remove(this);
            parent = newParent;
            newParent?.children.Add(this);
            if (worldPositionStays) return;
            localPosition = Vector3.zero;
            localRotation = Quaternion.identity;
            localScale = Vector3.one;
        }

        public bool IsChildOf(Transform ancestor)
        {
            for (Transform t = this; t != null; t = t.parent)
                if (ReferenceEquals(t, ancestor)) return true;
            return false;
        }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y = 0f, float z = 0f) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new(1f, 1f, 1f);
        public static Vector3 zero => new(0f, 0f, 0f);
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y = 0f) { this.x = x; this.y = y; }
        public static Vector2 zero => new(0f, 0f);
    }

    public struct Quaternion
    {
        public static int EulerCalls;
        public static Quaternion identity => new();
        public static Quaternion Euler(float x, float y, float z) { EulerCalls++; return new(); }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new(1f, 1f, 1f, 1f);
        public float Luminance => (r + g + b) / 3f;

        /// <summary>Unity's Color.HSVToRGB: v may exceed 1 and is not clamped.</summary>
        public static Color HSVToRGB(float h, float s, float v)
        {
            float r = v, g = v, b = v;
            if (s > 0f)
            {
                h = (h - System.MathF.Floor(h)) * 6f;
                int sector = (int)System.MathF.Floor(h);
                float f = h - sector;
                float p = v * (1f - s);
                float q = v * (1f - s * f);
                float t = v * (1f - s * (1f - f));
                switch (sector)
                {
                    case 0: r = v; g = t; b = p; break;
                    case 1: r = q; g = v; b = p; break;
                    case 2: r = p; g = v; b = t; break;
                    case 3: r = p; g = q; b = v; break;
                    case 4: r = t; g = p; b = v; break;
                    default: r = v; g = p; b = q; break;
                }
            }
            return new Color(r, g, b, 1f);
        }
    }

    public static class Mathf
    {
        public const float PI = 3.1415927f;
        public static float Sin(float value) => System.MathF.Sin(value);
        public static float Cos(float value) => System.MathF.Cos(value);
        public static float Abs(float value) => System.MathF.Abs(value);
        public static float Min(float a, float b) => System.MathF.Min(a, b);
        public static float Max(float a, float b) => System.MathF.Max(a, b);
        public static float Pow(float f, float p) => System.MathF.Pow(f, p);
        public static float Clamp(float value, float min, float max) => System.Math.Clamp(value, min, max);
        public static float Clamp01(float value) => System.Math.Clamp(value, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * System.Math.Clamp(t, 0f, 1f);
        public static float Round(float value) => System.MathF.Round(value, System.MidpointRounding.ToEven);

        /// <summary>Deterministic stand-in for Unity's Perlin noise, returning [0,1].</summary>
        public static float PerlinNoise(float x, float y)
        {
            float value = System.MathF.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f;
            return value - System.MathF.Floor(value);
        }
    }

    public static class Time
    {
        public static float time = 1f, deltaTime = .02f, timeScale = 1f;
        public static int frameCount = 1;
    }

    public static class Random
    {
        public static int Calls;
        public static float value { get { Calls++; return .5f; } }
        public static float Range(float min, float max) { Calls++; return min; }
        public static int Range(int min, int max) { Calls++; return min; }
    }

    public class Shader : Object
    {
        public static bool Available = true, BlockPrimary;
        public static int Finds;
        public static readonly List<string> FindLog = new();
        private static readonly Dictionary<string, Shader> instances = new();

        public static Shader Find(string name)
        {
            Finds++;
            FindLog.Add(name);
            if (!Available) return null;
            if (BlockPrimary && name == "Sprites/Default") return null;
            if (name != "Sprites/Default" && name != "Unlit/Texture") return null;
            if (!instances.TryGetValue(name, out Shader instance)) instances[name] = instance = new Shader { name = name };
            return instance;
        }

        public override string ToString() => name;
    }

    public enum FilterMode { Point, Bilinear, Trilinear }

    public class Texture : Object
    {
        public FilterMode filterMode = FilterMode.Bilinear;
    }

    public class Texture2D : Texture
    {
        public static int CreatedCount, SetPixelCalls, ApplyCalls;
        public static bool ThrowOnSetPixel, ThrowOnApply;
        public readonly int width, height;
        public Color LastPixel = Color.white;
        public Vector2 LastPixelCoord = new(-1f, -1f);
        public bool Applied;

        public Texture2D(int width, int height)
        {
            this.width = width;
            this.height = height;
            CreatedCount++;
        }

        public void SetPixel(int x, int y, Color color)
        {
            SetPixelCalls++;
            if (ThrowOnSetPixel) throw new System.InvalidOperationException("injected SetPixel failure");
            LastPixel = color;
            LastPixelCoord = new Vector2(x, y);
        }

        public void Apply()
        {
            ApplyCalls++;
            if (ThrowOnApply) throw new System.InvalidOperationException("injected Apply failure");
            Applied = true;
        }
    }

    public class Material : Object
    {
        public static int CreatedCount;
        public int InstanceWrites;
        public Shader shader;
        public Texture mainTexture;
        public int renderQueue;
        public readonly List<(string Name, int Value)> IntSets = new();
        private Color colorValue = Color.white;

        public Material(Shader shader) { this.shader = shader; CreatedCount++; }

        public Color color
        {
            get => colorValue;
            set { colorValue = value; InstanceWrites++; }
        }

        public void SetInt(string name, int value)
        {
            IntSets.Add((name, value));
            InstanceWrites++;
        }

        public Texture GetMainTexture() => mainTexture;
    }

    public class Renderer : Component
    {
        public static bool ThrowOnSortingWrite;
        public static int SortingWrites;
        private int sortingLayerValue, sortingOrderValue;
        public Material sharedMaterial;
        public bool enabled;
        public int sortingLayerID
        {
            get => sortingLayerValue;
            set { sortingLayerValue = value; SortingWrites++; if (ThrowOnSortingWrite) throw new System.InvalidOperationException("injected sorting write failure"); }
        }
        public int sortingOrder
        {
            get => sortingOrderValue;
            set { sortingOrderValue = value; SortingWrites++; if (ThrowOnSortingWrite) throw new System.InvalidOperationException("injected sorting write failure"); }
        }
        // Production must never clone materials through the instance accessor.
        public Material material => throw new System.NotSupportedException("material cloning is forbidden; use sharedMaterial");
    }

    public class MeshRenderer : Renderer { }

    public class Mesh : Object
    {
        public static int CreatedCount, VertexUploads, BoundsRecalculations, NormalRecalculations, Clears;
        public static int RejectedGeometryWrites;
        public static bool ThrowOnVertexWrite, ThrowOnUvWrite, ThrowOnTriangleWrite;
        public Bounds bounds;
        private Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> vertexBuffer;
        private Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector2> uvBuffer;
        private Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> triangleBuffer;

        public Mesh() { CreatedCount++; }

        public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> vertices
        {
            get => vertexBuffer;
            set
            {
                if (ThrowOnVertexWrite) throw new System.InvalidOperationException("injected mesh vertices failure");
                vertexBuffer = value;
                VertexUploads++;
            }
        }

        public static int UvWrites, TriangleWrites;

        /// <summary>Unity rejects uv/indices on a mesh with no vertices; model that.</summary>
        public int vertexCount => vertexBuffer == null ? 0 : vertexBuffer.Length;

        public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector2> uv
        {
            get => uvBuffer;
            set
            {
                if (ThrowOnUvWrite) throw new System.InvalidOperationException("injected mesh uv failure");
                if (vertexCount == 0) { RejectedGeometryWrites++; throw new System.InvalidOperationException("mesh has no vertices: uv rejected"); }
                uvBuffer = value;
                UvWrites++;
            }
        }

        public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> triangles
        {
            get => triangleBuffer;
            set
            {
                if (ThrowOnTriangleWrite) throw new System.InvalidOperationException("injected mesh triangles failure");
                if (vertexCount == 0) { RejectedGeometryWrites++; throw new System.InvalidOperationException("mesh has no vertices: indices rejected"); }
                triangleBuffer = value;
                TriangleWrites++;
            }
        }

        public void RecalculateBounds() { BoundsRecalculations++; }
        public void RecalculateNormals() { NormalRecalculations++; }
        public void Clear() { Clears++; }

        public void ResetCounter() { }
    }

    public struct Bounds
    {
        public Vector3 center, size;
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
    }

    public class MeshFilter : Component
    {
        public Mesh sharedMesh;
        public Mesh mesh { get => sharedMesh; set { sharedMesh = value; MeshInstantiations++; } }
        public static int MeshInstantiations;
        public static bool ThrowOnMeshWrite;
    }

    public class SpriteRenderer : Renderer { }

    public class TrailRenderer : Renderer { public float time = .2f; }

    public class ParticleSystem : Component
    {
        public static int Plays, MainAccesses;
        public static bool ThrowOnPlay;
        public struct MinMaxCurve { public float constantMax; public MinMaxCurve(float value) { constantMax = value; } }
        public struct MainModule { public MinMaxCurve startLifetime; }
        private MainModule module = new MainModule { startLifetime = new MinMaxCurve(.4f) };
        public MainModule main { get { MainAccesses++; return module; } }
        public void Play()
        {
            Plays++;
            if (ThrowOnPlay) throw new System.InvalidOperationException("injected native particle failure");
        }
    }

    public enum ForceMode2D { Force, Impulse }

    public class Rigidbody2D : Component
    {
        public static int TorqueCalls;
        public bool isKinematic;
        public Vector2 velocity, angularVelocity;
        public float mass = 1f;
        public void AddTorque(float torque, ForceMode2D mode) => TorqueCalls++;
    }

    public class Collider2D : Component { }
}

public enum DamageSource { Arrow, Fire }
public enum DamageSurface { Flesh, Structure }
public enum Side { Player, Enemy }

public class AudioEmitter : UnityEngine.Component
{
    public int Plays;
    public void Play(UnityEngine.Vector3 position, bool a, bool b, bool c) => Plays++;
}

public class Wall : UnityEngine.Component { public bool isIntact = true; }
public class Crusher : UnityEngine.Component { public bool IsStunned; }

public class Damageable : UnityEngine.Component
{
    public bool isDead, invulnerable, IgnoreDamage;
    public DamageSurface surface = DamageSurface.Flesh;
    public int HitCount, TotalDamage;
    public DamageSource LastSource;
    public UnityEngine.GameObject LastAttacker;
    public bool IsDamagedBy(DamageSource source) => !isDead && !IgnoreDamage;
    public void ReceiveDamage(int amount, UnityEngine.GameObject attacker, DamageSource source)
    {
        HitCount++;
        TotalDamage += amount;
        LastAttacker = attacker;
        LastSource = source;
    }
}

public class Archer : UnityEngine.MonoBehaviour { }

public class Arrow : UnityEngine.MonoBehaviour
{
    public UnityEngine.GameObject archer;
    public bool isFireArrow, canBounce = true, authorityActive = true, shouldOrientate = true;
    public bool _hasHit, _orientToVelocity = true, _perfect;
    public int hitDamage = 1, perfectDamageMultiplier = 2;
    public DamageSource _damageSource = DamageSource.Arrow;
    public UnityEngine.ParticleSystem _impactSpawner;
    public UnityEngine.SpriteRenderer _spriteRenderer;
    public UnityEngine.TrailRenderer _trail;
    public UnityEngine.Rigidbody2D _rigidbody;
    public AudioEmitter wallHitSound = new(), groundHitSound = new();
}

public static class NetworkBigBoss
{
    public static bool HasWorldAuth = true, IsClientPresent, IsOnline, HasClientCaughtUp = true;
}

public static class Pool
{
    public static int DespawnCalls;
    public static float LastDespawnDelay;
    public static void Despawn(UnityEngine.GameObject go, float delay, bool instant) { DespawnCalls++; LastDespawnDelay = delay; }
}

namespace KingdomEnhancedMod
{
    // Renderer contract only. Full projectile qualification and combat use the greek-impact suite.
    internal static class PatchArcher_GreekImpact
    {
        // 边界替身（真实判定在 greek-impact 套件用真实 PatchArcher_GreekImpact 验证）：
        // 本套件只验证 FX 侧如何消费「合资格箭 + 来源」这一接口。
        internal static bool Eligible = true;
        internal static bool HeroOrigin;                       // 本次命中的来源（false=Greek、true=Hero）
        internal static bool HeroEnabled;                      // 英雄来源开关（独立于 ArcherImpactEnabled）
        internal static bool GreekEnabled => ModConfig.ArcherImpactEnabled.Value;
        internal static bool AnyOriginEnabled => GreekEnabled || HeroEnabled;
        internal static bool IsOriginEnabledFor(bool heroOrigin) => heroOrigin ? HeroEnabled : GreekEnabled;
        internal static bool TryGetEligibleOrigin(Arrow arrow, out bool heroOrigin)
        {
            heroOrigin = HeroOrigin;
            return Eligible;
        }
        internal static bool IsEligibleArrow(Arrow arrow) => Eligible;
        internal struct HitTicket { internal bool Valid; }
        internal static HitTicket BeginHit(Arrow arrow, UnityEngine.GameObject target) => new() { Valid = Eligible };
        internal static bool EndHit(Arrow arrow, HitTicket ticket) => ticket.Valid && arrow != null && arrow._hasHit;
        internal static void AbortHit(Arrow arrow, HitTicket ticket) { }
    }
    public static class ModConfig
    {
        public class Entry { public bool Value; }
        public static Entry ArcherImpactEnabled = new();
    }

    /// <summary>
    /// Stand-in for the root-owned scope helper: <c>IsActive</c> mirrors
    /// OptionalQoLScope.IsActive + "game exists", and <c>TryGetContext</c> hands out the
    /// trusted world/layer/scene identity of the live world. The two are modelled as
    /// independent on purpose (IsActive does not imply a resolvable context), so the
    /// module's fault path can be exercised; replacing <c>World</c>/<c>Layer</c>/<c>Scene</c>
    /// models a real world change while the scope stays active.
    /// </summary>
    public static class ArcherOptionsScope
    {
        public static bool Active = true;
        public static UnityEngine.GameObject World;
        public static UnityEngine.GameObject Layer;
        public static int Scene;
        public static bool IsActive => Active && Layer != null;
        public static bool TryGetContext(out System.IntPtr world, out System.IntPtr layer, out int scene)
        {
            world = default;
            layer = default;
            scene = 0;
            if (!IsActive || World == null) return false;
            world = World.Pointer;
            layer = Layer.Pointer;
            scene = Scene;
            return true;
        }
        public static bool IsCurrent(UnityEngine.Component component) =>
            Active && component != null && component.gameObject != null
            && component.gameObject.activeInHierarchy && Layer != null && Layer.activeInHierarchy
            && component.transform.IsChildOf(Layer.transform);
    }

    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new();
        public LogSink LogSource = new();
        public class LogSink
        {
            public readonly List<string> Infos = new(), Warnings = new(), Errors = new();
            public void LogInfo(string message) => Infos.Add(message);
            public void LogWarning(string message) => Warnings.Add(message);
            public void LogError(string message) => Errors.Add(message);
        }
    }
}
