using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

// 仅边界桩：只实现 production file 触碰到的 Harmony/BepInEx/Unity/游戏成员。
// 原生语义（CanSpawnThicket 的 auth + 左右城墙 strict 排除 + 全部 Grass 间距；SpawnThicket
// 早退；RemoveThicket 在守卫通过后交还 _thicket 并回写当前 Managers.world）按 2.4 已知行为照抄，
// 便于测试在同一条原生链路上施加 hook。thicketSpacing 做成可抛异常的属性，用于模拟 interop
// setter/getter 失败；transform 子层用于验证多 renderer 淡出。

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute
    {
        public readonly Type TargetType;
        public readonly string MethodName;
        public HarmonyPatch() { }
        public HarmonyPatch(Type type) { TargetType = type; }
        public HarmonyPatch(Type type, string name) { TargetType = type; MethodName = name; }
    }

    public sealed class HarmonyPrefix : Attribute { }
    public sealed class HarmonyPostfix : Attribute { }
    public sealed class HarmonyFinalizer : Attribute { }
}

namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T>
    {
        public readonly string KeyName;
        public T Value { get; set; }
        public ConfigEntry(string keyName, T value) { KeyName = keyName; Value = value; }
    }
}

namespace UnityEngine
{
    public class Object
    {
        private static long _nextPointer;
        private static long _nextId;
        private readonly int _id;
        public IntPtr Pointer { get; set; }
        public bool Destroyed;
        public string name;

        public Object(string name = "TestObject")
        {
            Pointer = (IntPtr)Interlocked.Increment(ref _nextPointer);
            _id = (int)Interlocked.Increment(ref _nextId);
            this.name = name;
        }

        public int GetInstanceID() => _id;

        // Unity 语义：已销毁对象为假、且与 null 相等。
        public static implicit operator bool(Object value) => value is not null && !value.Destroyed;
        public static bool operator ==(Object left, Object right) => ReferenceEquals(left, right)
            || ((left is null || left.Destroyed) && (right is null || right.Destroyed));
        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => _id;
    }

    public class GameObject : Object
    {
        private static int _nextScene = 100;
        private readonly List<Component> _components = new();
        public Transform transform;
        public bool activeInHierarchy = true;
        public SceneManagement.Scene scene;

        public GameObject(string name = "TestObject") : base(name)
        {
            transform = new Transform { gameObject = this };
            scene.handle = ++_nextScene;
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            _components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : class => _components.OfType<T>().FirstOrDefault();

        public T[] GetComponentsInChildren<T>() where T : class => GetComponentsInChildren<T>(false);

        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class
        {
            var found = new List<T>();
            Collect(this, includeInactive, found);
            return found.ToArray();
        }

        private static void Collect<T>(GameObject root, bool includeInactive, List<T> found) where T : class
        {
            if (!includeInactive && !root.activeInHierarchy) return;
            found.AddRange(root._components.OfType<T>());
            foreach (Transform child in root.transform.Children)
            {
                if (child != null && child.gameObject != null) Collect(child.gameObject, includeInactive, found);
            }
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    }

    public class MonoBehaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }

    public class Transform : Component
    {
        public Vector3 position;
        public Transform parent;
        public readonly List<Transform> Children = new();

        public void SetParent(Transform newParent)
        {
            if (parent != null) parent.Children.Remove(this);
            parent = newParent;
            if (newParent != null && !newParent.Children.Contains(this)) newParent.Children.Add(this);
        }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y = 0f, float z = 0f) { this.x = x; this.y = y; this.z = z; }
    }

    public static class Mathf
    {
        public static float Abs(float value) => MathF.Abs(value);
        public static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float Min(float a, float b) => MathF.Min(a, b);
    }

    public static class Time
    {
        public static float time;
        public static float timeScale = 1f;
    }

    public static class Random
    {
        public static int Calls;
        public static float Unit = 0.5f; // Range(min,max) 取中间值，测试确定性
        public static float Range(float min, float max)
        {
            Calls++;
            return min + (max - min) * Unit;
        }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public class SpriteRenderer : Component
    {
        private Color _color = new Color(1f, 1f, 1f, 1f);
        public bool FailColorWrite;
        public Color color { get => _color; set { if (FailColorWrite) throw new InvalidOperationException("color write failed"); _color = value; } }
        public bool enabled = true;
    }

    public abstract class BaseSpriteFX : MonoBehaviour
    {
        public enum EndAction { None, Deactivate, Destroy }
        public int FadeCalls;
        public float LastFadeSeconds = -1f;
        public void FadeOut(float duration, EndAction endAction = EndAction.None)
        {
            FadeCalls++;
            LastFadeSeconds = duration;
        }
    }

    public class SpriteRendererFX : BaseSpriteFX { }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public int handle;
        public bool IsValid() => handle != 0;
    }
}

// ---- 游戏侧边界桩（全局命名空间，与 interop 游戏类型一致）----

public class Grass : UnityEngine.MonoBehaviour
{
    public UnityEngine.GameObject _thicket;
    public int Stage = 7;              // 测试驱动用：原生 Stage7 条件在调用者一侧
    public int SpawnCalls, RemoveCalls;
    public bool ThrowOnRemove;
    public bool RemoveNoOp;            // 模拟原生守卫未满足：_thicket 原样保留
    public bool ThicketHasFx;          // 真实 Thicket prefab 没有 SpriteRendererFX
    public World WorldRef;             // 测试驱动用：本株所属 world

    public void SpawnThicket()
    {
        SpawnCalls++;
        if (!NetworkBigBoss.HasWorldAuth) return;
        if (_thicket != null) return;
        World world = WorldRef;
        if (world == null) return;
        var thicket = new UnityEngine.GameObject("Thicket");
        thicket.scene.handle = world.gameLayer.gameObject.scene.handle;
        thicket.transform.SetParent(world.gameLayer);
        var root = thicket.AddComponent<UnityEngine.SpriteRenderer>();
        root.color = new UnityEngine.Color(0.8f, 0.9f, 0.7f, 1f);
        if (ThicketHasFx) thicket.AddComponent<UnityEngine.SpriteRendererFX>();
        _thicket = thicket;
        world.AddThicket(this);
    }

    public void RemoveThicket()
    {
        RemoveCalls++;
        if (ThrowOnRemove) throw new InvalidOperationException("native thicket removal failed");
        if (RemoveNoOp) return;
        if (_thicket == null || !NetworkBigBoss.HasWorldAuth) return;
        _thicket.activeInHierarchy = false;
        _thicket = null;
        Managers managers = Managers.Inst;
        if (managers != null && managers.world != null) managers.world.RemoveThicket(this); // 原生走当前 world
    }
}

public class World : UnityEngine.Object
{
    private float _thicketSpacing = 6f;
    public bool ThrowOnSpacingGet, ThrowOnSpacingSet;

    public float thicketSpacing
    {
        get
        {
            if (ThrowOnSpacingGet) throw new InvalidOperationException("native spacing read failed");
            return _thicketSpacing;
        }
        set
        {
            if (ThrowOnSpacingSet) throw new InvalidOperationException("native spacing write failed");
            _thicketSpacing = value;
        }
    }

    /// <summary>测试直读原生字段值，不经 interop 属性语义。</summary>
    public float RawThicketSpacing => _thicketSpacing;

    public Il2CppSystem.Collections.Generic.HashSet<Grass> _grassWithThicket = new();
    public UnityEngine.Transform gameLayer;
    public float LeftBorder = 0f;
    public float RightBorder = 0f;
    public int AddThicketCalls, RemoveThicketCalls;
    public bool ThrowOnCanSpawn;

    public World(UnityEngine.GameObject layerObject) : base("World")
    {
        gameLayer = layerObject.transform;
    }

    public void AddThicket(Grass grass)
    {
        AddThicketCalls++;
        _grassWithThicket.Add(grass);
    }

    public void RemoveThicket(Grass grass)
    {
        RemoveThicketCalls++;
        _grassWithThicket.Remove(grass);
    }

    public bool CanSpawnThicket(Grass grass)
    {
        if (ThrowOnCanSpawn) throw new InvalidOperationException("native can spawn failed");
        if (!NetworkBigBoss.HasWorldAuth) return false;
        float x = grass.transform.position.x;
        if (x > LeftBorder && x < RightBorder) return false;
        foreach (Grass other in _grassWithThicket)
        {
            if (other == grass) continue;
            if (UnityEngine.Mathf.Abs(other.transform.position.x - x) < thicketSpacing) return false;
        }
        return true;
    }
}

public class Forest : UnityEngine.MonoBehaviour { }

public class ForestItem : UnityEngine.MonoBehaviour
{
    public void FadeAndRemove(float delay = 0f) { FadeAndRemoveCalls++; }
    public bool controlsForestSize;
    public bool hostAuthorativeDespawn;
    public float removeDelay = 10f;
    public bool removedByForest { get; set; }
    public Forest _forest;
    public int FadeAndRemoveCalls;
}

public class Managers
{
    public static Managers Inst;
    public World world;
}

public static class NetworkBigBoss
{
    public static bool HasWorldAuth;
}

namespace KingdomEnhancedMod
{
    public static class ModConfig
    {
        public static readonly BepInEx.Configuration.ConfigEntry<bool> Enabled =
            new BepInEx.Configuration.ConfigEntry<bool>("Enabled", true);
        public static readonly BepInEx.Configuration.ConfigEntry<bool> DenseThicketsEnabled =
            new BepInEx.Configuration.ConfigEntry<bool>("DenseThicketsEnabled", false);
        public static readonly BepInEx.Configuration.ConfigEntry<bool> FastForestRecedeEnabled =
            new BepInEx.Configuration.ConfigEntry<bool>("FastForestRecedeEnabled", false);
    }

    /// <summary>root 契约桩：IsActive = ModEnabled + 当前 world 明确可用（不绑定任何单一 biome）。</summary>
    public static class OptionalQoLScope
    {
        public static bool CurrentWorldIsActive = true;
        public static bool IsActive => ModConfig.Enabled.Value && CurrentWorldIsActive;
        public static int CurrentSceneHandle;
        public static int IsCurrentCalls;

        public static bool IsCurrent(UnityEngine.Component component)
        {
            IsCurrentCalls++;
            return component != null && component.gameObject != null
                && component.gameObject.scene.handle == CurrentSceneHandle;
        }
    }

    public sealed class ManualLogSource
    {
        public readonly List<string> Infos = new();
        public readonly List<string> Warnings = new();
        public void LogInfo(string message) => Infos.Add(message);
        public void LogWarning(string message) => Warnings.Add(message);
        public void LogError(string message) => Warnings.Add(message);
    }

    public sealed class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance;
        public readonly ManualLogSource LogSource = new ManualLogSource();
    }
}
