// 最小 stub 面：让生产文件 HeroArcherRuntime.cs（Unity 侧胶水）脱离游戏进程也能编译运行。
// 只补 HeroArcherRuntime 真正引用的成员；行为（谁抛错、谁可见）由测试按需设置。
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    internal class Object
    {
        public string name = "";
    }

    internal class Component : Object
    {
        public GameObject gameObject;
        public Transform transform;
    }

    internal class MonoBehaviour : Component
    {
    }

    internal class Transform : Component
    {
        public Transform parent;
        public IntPtr Pointer;

        public Transform(IntPtr pointer)
        {
            Pointer = pointer;
        }
    }

    internal class GameObject : Object
    {
        private static int _nextId = 100;
        private readonly List<object> _components = new List<object>();
        public int layer;
        public bool activeInHierarchy = true;
        public Transform transform;

        public GameObject(string objectName = "go")
        {
            name = objectName;
            InstanceId = _nextId++;
            transform = new Transform(new IntPtr(0x1000 + InstanceId));
            transform.gameObject = this;
        }

        public int InstanceId { get; }

        public int GetInstanceID() => InstanceId;

        public void AddComponent(object component) => _components.Add(component);

        public T GetComponent<T>() where T : class
        {
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T typed) return typed;
            }
            return null;
        }

        public T GetComponentInParent<T>() where T : class => null;

        public T GetComponentInChildren<T>() where T : class => GetComponent<T>();

        public bool CompareTag(string tag) => false;
    }

    internal static class Time
    {
        internal static float time;
        internal static int frameCount;
    }

    internal static class LayerMask
    {
        internal static int NameToLayer(string layerName) => layerName == "Enemies" ? 9 : -1;
    }
}

namespace KingdomEnhancedMod
{
    // ---- 配置 / 网络 / 范围 gate（stub）----

    internal static class ModConfig
    {
        internal static ConfigEntryBool HeroArcherEnabled = new ConfigEntryBool();
    }

    internal sealed class ConfigEntryBool
    {
        internal bool Value;
    }

    internal static class NetworkBigBoss
    {
        internal static bool HasWorldAuth = true;
        internal static bool IsOnline;
    }

    internal static class ArcherOptionsScope
    {
        internal static bool IsActive = true;
    }

    internal static class OptionalQoLScope
    {
        internal static bool IsCurrent(UnityEngine.Component component) => component != null
            && component.gameObject != null && component.gameObject.activeInHierarchy;
    }

    internal static class Layers
    {
        internal static string Enemies = "Enemies";
    }

    internal enum DamageSource
    {
        Arrow = 1,
        Knight = 2,
    }

    internal class Damageable : UnityEngine.Component
    {
        public bool isDead;
        public bool enabled = true;
        public bool IsDamagedBy(DamageSource source) => true;
    }

    internal class Character : UnityEngine.Component
    {
        public bool inert;
        public bool grabbed;
    }

    internal class Embarkee : UnityEngine.Component
    {
        public bool IsEmbarked;
    }

    internal class FriendlyTroll : UnityEngine.Component
    {
    }

    internal class Scanner : UnityEngine.Component
    {
        public float range;
        public float rangeBehind;
        public UnityEngine.GameObject GetClosest() => null;
    }

    internal class Mover : UnityEngine.Component
    {
        public IntPtr Pointer;
        public float ActualSpeed;
    }

    internal class World : UnityEngine.Object
    {
        public UnityEngine.Transform gameLayer;
    }

    internal class Managers
    {
        internal static Managers Inst = new Managers();
        internal World world;
    }

    internal class Archer : UnityEngine.Component
    {
        internal enum AttackMode
        {
            Melee = 0,
            Ranged = 1,
        }

        public enum Side
        {
            Left = -1,
            Right = 1,
        }

        public IntPtr Pointer;
        public Side side = Side.Left;
        public bool enabled = true;
        public bool harmless;
        public bool inGuardSlot;
        public Damageable _guardSlot;
        public float walkSpeed = 4f;
        public float runSpeed = 6f;
        public AttackMode _attackMode = AttackMode.Ranged;
        public AttackMode _desiredAttackMode = AttackMode.Ranged;
        public Character _character;
        public Damageable _damageable;
        public Embarkee _embarkee;
        public Scanner _enemyScanner;
        public Mover _mover;
        public UnityEngine.GameObject _shootingTarget;
        internal bool PlayerControlled;
        /// <summary>测试钩子：为 true 时读 shootRange 抛错（模拟 interop 瞬时异常）。</summary>
        internal bool ThrowOnShootRange;
        /// <summary>测试钩子：为 true 时写 shootRange 抛错。</summary>
        internal bool ThrowOnShootRangeWrite;

        private float _shootRange = 8f;

        /// <summary>原生字段在 stub 里用属性承载，以便测试注入 interop 瞬时异常。</summary>
        public float shootRange
        {
            get
            {
                if (ThrowOnShootRange) throw new InvalidOperationException("stub read failure");
                return _shootRange;
            }
            set
            {
                if (ThrowOnShootRangeWrite) throw new InvalidOperationException("stub write failure");
                _shootRange = value;
            }
        }

        public bool ShouldPlayerControl() => PlayerControlled;

        internal void Init(UnityEngine.GameObject owner, IntPtr pointer)
        {
            gameObject = owner;
            transform = owner.transform;
            Pointer = pointer;
            owner.AddComponent(this);
            _character = new Character { gameObject = owner };
            _damageable = new Damageable { gameObject = owner };
            _embarkee = new Embarkee { gameObject = owner };
            _enemyScanner = new Scanner { gameObject = owner };
            _mover = new Mover { gameObject = owner };
            _shootRange = 8f;
        }
    }

    internal static class PatchRoles_Crossbowman
    {
        internal static bool CrossbowFlag;
        internal static bool IsCrossbowman(Archer archer) => CrossbowFlag && ReferenceEquals(archer, CrossbowTarget);
        internal static Archer CrossbowTarget;
    }

    internal static class PatchRoles_NorseSquad
    {
        internal static bool NorseFlag;
        internal static bool IsNorseArcherInstance(Archer archer) => NorseFlag && ReferenceEquals(archer, NorseTarget);
        internal static Archer NorseTarget;
    }

    /// <summary>PatchRoles_DeadlandsPowers 的边界替身：只供 HeroArcherMovement 的让位探针（默认关闭）。</summary>
    internal static class PatchRoles_DeadlandsPowers
    {
        internal sealed class UnitRef
        {
            internal object Knight;
            internal Archer Archer;
        }

        internal static bool Enabled;
        internal static readonly System.Collections.Generic.Dictionary<IntPtr, UnitRef> ByMover =
            new System.Collections.Generic.Dictionary<IntPtr, UnitRef>();

        internal static bool GameplayActive() => Enabled;
        internal static bool IsDeadlandsFollower(Archer archer) => false;
    }

    internal sealed class ManualLogSource
    {
        internal void LogInfo(string message)
        {
        }

        internal void LogWarning(string message)
        {
        }
    }

    internal class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
        internal ManualLogSource LogSource = new ManualLogSource();
    }

    /// <summary>生产 HeroArcherVisuals 的替身：记录 Apply/Remove/Clear 调用与可见性，供接线断言。</summary>
    internal static class HeroArcherVisuals
    {
        internal static int ApplyCount;
        internal static int RemoveCount;
        internal static int ClearCount;
        internal static bool AtlasUnavailable;
        internal static readonly System.Collections.Generic.HashSet<int> Visible = new System.Collections.Generic.HashSet<int>();

        internal static bool HasVisual(Archer archer) => archer != null && Visible.Contains(archer.gameObject.GetInstanceID());

        internal static void Apply(Archer archer)
        {
            ApplyCount++;
            Visible.Add(archer.gameObject.GetInstanceID());
        }

        internal static void Remove(Archer archer)
        {
            RemoveCount++;
            if (archer != null) Visible.Remove(archer.gameObject.GetInstanceID());
        }

        internal static void Clear()
        {
            ClearCount++;
            Visible.Clear();
        }

        internal static void NotifyRelease(Archer archer)
        {
        }

        internal static void Reset()
        {
            ApplyCount = 0;
            RemoveCount = 0;
            ClearCount = 0;
            AtlasUnavailable = false;
            Visible.Clear();
        }
    }
}
