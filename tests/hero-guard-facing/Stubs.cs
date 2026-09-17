// 边界桩：只实现 production files（HeroArcherGuardFacing / HeroArcherLiveDiagnostics）
// 实际触碰到的 Unity / 游戏 / MOD 成员。原生语义按 2.4 已知行为照抄：
//   * Mover.FacingMode { Ahead=0, Left=-1, Right=1, Target=2 }（2.1 参考源 + SetFacingMode 语义）
//   * Mover 到位后 _movingToGoal.value=false、goalMode 保持 Position
//   * Director.IsNight 是实际原生夜间；Kingdom.Archers 运行时是 HashSet（需 Cast）
//   * Archer.behaviour / Archer.shoot 是 IHaglet，须 Cast 到 Haglet 读 started/latestGoto
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        private static long next;
        public IntPtr Pointer = (IntPtr)(++next);
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T GetComponentInChildren<T>() where T : Component => gameObject.GetComponentInChildren<T>();
    }

    public class Behaviour : Component { public bool enabled = true; }

    public class MonoBehaviour : Behaviour { }

    public class Transform : Component
    {
        public Vector3 position;
        public Vector3 localScale = Vector3.one;
        public Vector3 lossyScale = Vector3.one;

        public bool IsChildOf(Transform other)
        {
            if (other == null || gameObject == null || other.gameObject == null) return false;
            for (GameObject p = gameObject; p != null; p = p.Parent)
                if (ReferenceEquals(p, other.gameObject)) return true;
            return false;
        }
    }

    public class GameObject : Object
    {
        private static int nextId;
        public int Id = ++nextId;
        public bool activeInHierarchy = true;
        public GameObject Parent;
        public readonly List<GameObject> Children = new List<GameObject>();
        private readonly Dictionary<Type, Component> components = new Dictionary<Type, Component>();

        public static int FindObjectsCalls;   // 只读断言用：生产代码绝不该走 FindObjects

        public int GetInstanceID() => Id;

        public T Add<T>(T value) where T : Component
        {
            value.gameObject = this;
            if (value is Transform self) self.transform = self;
            else if (value.transform == null)
            {
                var transform = new Transform();
                transform.gameObject = this;
                transform.transform = transform;
                value.transform = transform;
            }
            components[typeof(T)] = value;
            return value;
        }

        public T GetComponent<T>() where T : Component
            => components.TryGetValue(typeof(T), out Component component) ? (T)component : null;

        public T GetComponentInChildren<T>() where T : Component
        {
            T own = GetComponent<T>();
            if (own != null) return own;
            for (int i = 0; i < Children.Count; i++)
            {
                T found = Children[i].GetComponentInChildren<T>();
                if (found != null) return found;
            }
            return null;
        }

        public void SetParent(GameObject parent)
        {
            Parent = parent;
            if (parent != null) parent.Children.Add(this);
        }

        public static T[] FindObjectsOfType<T>() where T : Component
        {
            FindObjectsCalls++;
            return Array.Empty<T>();
        }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new Vector3(1f, 1f, 1f);
    }

    public struct Bounds
    {
        public Vector3 min, max;
        public Bounds(Vector3 center, Vector3 size)
        {
            min = new Vector3(center.x - size.x * 0.5f, center.y - size.y * 0.5f, center.z);
            max = new Vector3(center.x + size.x * 0.5f, center.y + size.y * 0.5f, center.z);
        }
    }

    public class Sprite : Object { public string name = "sprite"; }

    public class Renderer : Component
    {
        public bool enabled = true;
        public Bounds bounds;
    }

    public class SpriteRenderer : Renderer
    {
        public Sprite sprite;
        public bool flipX;
    }

    public class Camera : Behaviour
    {
        public static Camera main;
    }

    public static class Time
    {
        public static float time = 100f;
    }

    public static class Mathf
    {
        public static float Abs(float value) => Math.Abs(value);
        public static bool IsFinite(float value) => float.IsFinite(value);
    }
}

public enum Side { Left = -1, Right = 1, Neutral = 0 }

public class Damageable : UnityEngine.Component
{
    public bool isDead;
    public bool enabled = true;
}

public class Knight : UnityEngine.Component { }

public class GuardSlot : UnityEngine.Component { }

public class Formation : UnityEngine.Component { }

public class Embarkee : UnityEngine.Component
{
    public bool IsEmbarked;
    public object EmbarkableTarget;
}

public class Mover : UnityEngine.Component
{
    public enum FacingMode { Ahead = 0, Left = -1, Right = 1, Target = 2 }

    public enum GoalMode { Off = 0, Position = 1, Object = 2 }

    private FacingMode _facingMode = FacingMode.Ahead;

    public FacingMode facingMode
    {
        get => ThrowOnReadFacing ? throw new Exception("facing read failed") : _facingMode;
        set => _facingMode = value;
    }

    public UnityEngine.GameObject facingTarget;
    public GoalMode goalMode = GoalMode.Off;
    public float _goalPosition;
    public Coatsink.Common.HagletValue<bool> _movingToGoal = new Coatsink.Common.HagletValue<bool>();
    public float _pauseTimeout;

    /// <summary>写入计数（断言“不重复写/必须归还”用）。</summary>
    public int SetFacingCalls;

    /// <summary>读 facingMode 抛错（模拟 interop 读取失败）。</summary>
    public bool ThrowOnReadFacing;

    /// <summary>写入抛错且不生效（模拟 setter 尚未写入）。</summary>
    public bool ThrowOnSetFacing;

    /// <summary>写入已生效后抛错（模拟“可能已写入”的半途失败）。</summary>
    public bool ThrowAfterSetFacing;

    /// <summary>完全接管写入行为（可抛错、可写别的值）。非 null 时由它决定字段。</summary>
    public Action<FacingMode, UnityEngine.GameObject> OnSetFacing;

    public void SetFacingMode(FacingMode mode, UnityEngine.GameObject target = null)
    {
        SetFacingCalls++;
        if (OnSetFacing != null) { OnSetFacing(mode, target); return; }
        if (ThrowOnSetFacing) throw new Exception("facing set failed");
        _facingMode = mode;
        facingTarget = mode == FacingMode.Target ? target : null;
        if (ThrowAfterSetFacing) throw new Exception("facing set threw after applying");
    }
}

public class Director
{
    public float currentTime = 22f;
    public bool IsNight = true;
}

public class World : UnityEngine.Component
{
    public UnityEngine.Transform gameLayer;
}

public class Kingdom
{
    public float campfirePosition;
    public float Left = -50f, Right = 50f;
    public object Archers;

    public float GetBorderSideIntact(Side side)
        => side == Side.Left ? Left : side == Side.Right ? Right : 0f;
}

public class Managers
{
    public static Managers Inst = new Managers();
    public Kingdom kingdom = new Kingdom();
    public World world = new World();
    public Director director = new Director();
}

namespace Coatsink.Common
{
    public class HagletValue<T> { public T value; }

    public class Haglet
    {
        public bool started = true;
        public int latestGoto = 8;
    }
}

public class Archer : UnityEngine.Behaviour
{
    public Mover _mover;
    public Knight _knight;
    public Formation _currentFormation;
    public Side _guardSide;
    public bool inGuardSlot;
    public GuardSlot _guardSlot;
    public Embarkee _embarkee;
    public Damageable _damageable;
    public object _huntingTarget;
    public object behaviour;
    public object shoot;
    public bool PlayerControlled;

    public Formation GetFormation() => _currentFormation;
    public bool ShouldPlayerControl() => PlayerControlled;
}

namespace KingdomEnhancedMod
{
    /// <summary>HeroArcherRuntime 的边界桩：Enabled/IsHero/CurrentActorLife 是 production 唯一依赖。</summary>
    internal static class HeroArcherRuntime
    {
        internal static bool Enabled = true;
        internal static readonly Dictionary<int, int> Lives = new Dictionary<int, int>();
        internal static readonly HashSet<Archer> Denied = new HashSet<Archer>();

        internal static bool IsHero(Archer archer)
        {
            if (!Enabled || archer == null || archer.gameObject == null || !archer.gameObject.activeInHierarchy) return false;
            if (Denied.Contains(archer)) return false;
            return CurrentActorLife(archer) > 0;
        }

        internal static int CurrentActorLife(Archer archer)
        {
            if (archer == null || archer.gameObject == null) return 0;
            return Lives.TryGetValue(archer.gameObject.GetInstanceID(), out int life) ? life : 0;
        }
    }

    internal class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
        internal Log LogSource = new Log();
    }

    internal class Log
    {
        internal readonly List<string> Lines = new List<string>();
        internal void LogInfo(string message) => Lines.Add(message);
        internal void LogWarning(string message) { }
        internal void LogError(string message) { }
    }
}
