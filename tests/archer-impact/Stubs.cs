// Native-boundary and Unity-surface stubs for the impact-visual regression suite.
// This file models only what the production module and the mirrored native
// Arrow.HitObject path touch; it is not a Unity emulator.

using System.Collections.Generic;

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
        public static Quaternion identity => new();
        public static Quaternion Euler(float x, float y, float z) => new();
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new(1f, 1f, 1f, 1f);
        public float Luminance => (r + g + b) / 3f;
    }

    public static class Mathf
    {
        public const float PI = 3.1415927f;
        public static float Sin(float value) => System.MathF.Sin(value);
        public static float Cos(float value) => System.MathF.Cos(value);
        public static float Abs(float value) => System.MathF.Abs(value);
        public static float Min(float a, float b) => System.MathF.Min(a, b);
        public static float Max(float a, float b) => System.MathF.Max(a, b);
        public static float Clamp(float value, float min, float max) => System.Math.Clamp(value, min, max);
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
        public static bool Available = true;
        public static int Finds;
        private static Shader instance;
        public static Shader Find(string name)
        {
            Finds++;
            if (!Available || name != "Sprites/Default") return null;
            return instance ??= new Shader { name = name };
        }
    }

    public class Material : Object
    {
        public static int CreatedCount;
        public Shader shader;
        public Material(Shader shader) { this.shader = shader; CreatedCount++; }
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

    public class LineRenderer : Renderer
    {
        public static bool ThrowOnPositionCountWrite, ThrowOnPositionWrite;
        public static int SetPositionCalls, PositionCountWrites;
        public readonly List<Vector3> Positions = new();
        public readonly List<float> WidthHistory = new();
        public readonly List<Color> ColorHistory = new();
        private int points;
        private float widthScale;
        public bool useWorldSpace, loop;
        public int numCapVertices, numCornerVertices;

        public int positionCount
        {
            get => points;
            set { points = value; PositionCountWrites++; if (ThrowOnPositionCountWrite) throw new System.InvalidOperationException("injected positionCount failure"); }
        }

        public float widthMultiplier
        {
            get => widthScale;
            set { widthScale = value; WidthHistory.Add(value); }
        }

        public Color startColor
        {
            get => ColorHistory.Count > 0 ? ColorHistory[^1] : default;
            set { ColorHistory.Add(value); }
        }

        public Color endColor { get; set; }

        public void SetPosition(int index, Vector3 position)
        {
            SetPositionCalls++;
            if (ThrowOnPositionWrite) throw new System.InvalidOperationException("injected SetPosition failure");
            while (Positions.Count <= index) Positions.Add(default);
            Positions[index] = position;
        }
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
