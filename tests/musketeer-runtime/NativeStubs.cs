// 原生游戏类型 + 协作 API 的最小替身（只覆盖 4 个生产文件实际用到的成员）。
// 真实签名/可读写性核对在 interop-check/（对 E 盘 2.4 interop 编译），本文件只保证测试可完全控制。
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type declaringType) { }
        public HarmonyPatch(Type declaringType, string methodName) { }
        public HarmonyPatch(Type declaringType, string methodName, Type[] argumentTypes) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyFinalizer : Attribute { }
}

namespace KingdomEnhancedMod
{
    public enum DamageSource
    {
        Troll = 1,
        Arrow = 2,
        Knight = 16,
        Fire = 512,
    }

    public enum EnemyType
    {
        TrollWeak = 0,
        TrollMedium = 1,
        ToughTroll = 2,
        Ogre = 3,
        Squid = 4,
        Boss = 5,
        KillerBoss = 6,
        Stealer = 7,
        BossWithStealer = 8,
        GauntletBoss = 9,
        Crusher = 10,
        Knight = 11,
        Archer = 12,
    }

    public class Enemy : MonoBehaviour
    {
        public EnemyType Type;
    }

    public class Squid : Enemy
    {
    }

    public class Crusher : Enemy
    {
        public bool IsStunned;
    }

    public class FriendlyTroll : MonoBehaviour
    {
    }

    /// <summary>原生普通鹿（Deer）：身份只看这个根组件，绝不认名字/tag。</summary>
    public class Deer : MonoBehaviour
    {
    }

    /// <summary>兔子等小动物（Critter）：在 Wildlife 层但没有 Deer 根组件 → 永远不是猎杀目标。</summary>
    public class Critter : MonoBehaviour
    {
    }

    /// <summary>坐骑（Hind）：与 Deer 无继承关系 → 永远不是猎杀目标。</summary>
    public class Hind : MonoBehaviour
    {
    }

    /// <summary>骑乘坐骑（Steed）：普通鹿判据排除它（PatchWorld_DeerPopulation 同款）。</summary>
    public class Steed : MonoBehaviour
    {
    }

    /// <summary>石化组件（鹿可被石化）：IsPetrified 是原生公开只读属性。</summary>
    public class Petrifiable : MonoBehaviour
    {
        public bool IsPetrified;
    }

    public class Embarkee : MonoBehaviour
    {
        public bool IsEmbarked;
        public GameObject EmbarkableTarget;
    }

    /// <summary>编队（仅用于"编队中不狩猎"的最终门断言）。</summary>
    public class Formation : MonoBehaviour
    {
    }

    /// <summary>原生 Kingdom 的最小替身（鹿猎只读 isDaytime）。</summary>
    public class Kingdom : MonoBehaviour
    {
        public bool isDaytime;
    }

    public class Damageable : MonoBehaviour
    {
        public bool isDead;
        public bool invulnerable;
        public bool ignoredWhenInvulnerable;
        public bool acceptsArrow = true;
        public readonly List<string> DamageLog = new();
        public Action<int, GameObject, DamageSource> OnReceiveDamage;

        public bool IsDamagedBy(DamageSource source) => acceptsArrow && source == DamageSource.Arrow;

        public void ReceiveDamage(int damageMultiplier, GameObject damager, DamageSource source)
        {
            DamageLog.Add(damageMultiplier + ":" + source);
            OnReceiveDamage?.Invoke(damageMultiplier, damager, source);
        }
    }

    public class Character : MonoBehaviour
    {
        public bool inert;
        public bool grabbed;
    }

    public class ArrowAttack : UnityEngine.Object
    {
        public float _shotMagnitude = 8f;
        public float _boostedShotMagnitude = 12f;
        public Arrow _arrowPrefab;

        /// <summary>原生 Range = v²/(-gravity)；替身按 gravity=-8 近似（baseline 8 → 8、克隆 → 12）。</summary>
        public float Range => _shotMagnitude > 0f ? _shotMagnitude * _shotMagnitude / 8f : 0f;
    }

    public class Arrow : MonoBehaviour
    {
    }

    public class GuardSlot : MonoBehaviour
    {
    }

    public class Knight : MonoBehaviour
    {
    }

    /// <summary>扫描器替身：additionalRequirements + SetExtraCondition 与 2.1/2.4 同形（组合谓词用）。</summary>
    public class Scanner : UnityEngine.Object
    {
        private static long _nextCondition;

        public sealed class ObjectCondition
        {
            private readonly Func<GameObject, bool> _invoke;

            public ObjectCondition(Func<GameObject, bool> invoke)
            {
                _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
                Pointer = (IntPtr)Interlocked.Increment(ref _nextCondition);
            }

            public IntPtr Pointer { get; }

            public bool Invoke(GameObject obj) => _invoke(obj);

            public static implicit operator ObjectCondition(Func<GameObject, bool> managed) => new ObjectCondition(managed);
        }

        public float range = 8f;
        public float rangeBehind = 8f;
        public ObjectCondition additionalRequirements;
        /// <summary>原生扫描器缓存列表（GetAll 返回它；地面重选读它、不写它）。</summary>
        public readonly System.Collections.Generic.List<GameObject> Cached = new();

        public int GetAll(out Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject> results)
        {
            results = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject>(Cached.Count);
            for (int i = 0; i < Cached.Count; i++) results[i] = Cached[i];
            return Cached.Count;
        }

        /// <summary>测试用：写入成功后再抛异常（模拟 2.4 setter 可能"写一半才抛"）。</summary>
        public bool ThrowAfterWriteOnSet;
        /// <summary>测试用：写之前就抛异常（字段保持原样，用于验证归还回执被保留）。</summary>
        public bool ThrowBeforeWriteOnSet;

        public void SetExtraCondition(ObjectCondition condition)
        {
            if (ThrowBeforeWriteOnSet)
            {
                ThrowBeforeWriteOnSet = false;
                throw new InvalidOperationException("scanner setter failed before write");
            }
            additionalRequirements = condition;
            if (ThrowAfterWriteOnSet)
            {
                ThrowAfterWriteOnSet = false;
                throw new InvalidOperationException("scanner setter failed after write");
            }
        }
    }

    public class Holder : MonoBehaviour
    {
        public static Holder Inst;
        public readonly Dictionary<string, Character> tagCharacterPairs = new();
    }

    public class Managers : MonoBehaviour
    {
        public static Managers Inst;
        public Holder holder;
        public Kingdom kingdom;
    }

    public class World : MonoBehaviour
    {
        public static BoxCollider2D GroundCollider;
    }

    public static class Layers
    {
        public const string Enemies = "Enemies";
        public const string Citizens = "Citizens";
    }

    /// <summary>
    /// 共享 helper（真实 `il2cpp/CombatDamage.cs`）引用的网络权威门替身：测试可控。
    /// root 把真实 helper 编入测试工程后即被使用；若 root 侧另有同名替身，保留其一。
    /// </summary>
    public static class NetworkBigBoss
    {
        public static bool HasWorldAuth { get; set; } = true;
    }

    public class Archer : MonoBehaviour
    {
        public ArrowAttack _arrowAttack;
        public ArrowAttack _fireArrowAttack;
        public ArrowAttack ActiveArrowAttack;
        public Scanner _enemyScanner = new Scanner();
        public Scanner _wildlifeScanner = new Scanner();
        public SpriteRenderer _spriteRenderer;
        public Animator _animator;
        public Character _character;
        public Damageable _damageable;
        public GuardSlot _guardSlot;
        public bool inGuardSlot;
        public Formation _currentFormation;
        public Knight _knight;
        public Embarkee _embarkee;
        public GameObject _shootingTarget;
        public float shootRange = 8f;
        public float shootPrepTime = 0.5f;
        public float shootCooldownTime = 1f;
        public float _cooldownReduction;
        public Vector2 _shootIntervalRange = new Vector2(0.5f, 1.5f);
        public Vector2 _shootIntervalRangeFormation = new Vector2(0.25f, 0.5f);
        public int minAttempts = 1;
        public int maxAttempts = 3;
        public float perfectArrowProbability = 0.2f;
        public bool harmless;

        public readonly List<string> Calls = new();
        public Func<GameObject, bool> JobAvailability = _ => true;

        public bool IsAvailableForJob(GameObject jobObject)
        {
            Calls.Add("IsAvailableForJob");
            return JobAvailability(jobObject);
        }

        public void AssignJob(GameObject jobObject) => Calls.Add("AssignJob");

        public void SetGuardSlot(GuardSlot slot) => Calls.Add("SetGuardSlot");

        public void EnterGuardSlot(GuardSlot slot) => Calls.Add("EnterGuardSlot");

        public void ExitGuardSlot() => Calls.Add("ExitGuardSlot");

        /// <summary>原生公开访问器（IsInFormation 为私有；生产读 _currentFormation 的等价面）。</summary>
        public Formation GetFormation() => _currentFormation;
    }

    public static class PatchRoles_Crossbowman
    {
        public static bool CrossbowmanResult;

        public static bool IsCrossbowman(Archer archer) => CrossbowmanResult;
    }

    /// <summary>Operator 侧 MusketeerAccess 的同签名替身（测试可控）。</summary>
    internal static class MusketeerAccess
    {
        internal static bool EnabledValue = true;
        internal static bool PlayingValue = true;
        internal static Transform WorldValue;

        internal static Transform World => WorldValue;
        internal static bool Enabled => EnabledValue;
        internal static bool Playing => PlayingValue;
        internal static bool InWorld(GameObject root) => true;
        internal static bool InWorld(Component component) => component != null && component.gameObject != null;
        internal static void Reset() { EnabledValue = true; PlayingValue = true; WorldValue = null; }
    }

    /// <summary>Identity worker 契约的同签名替身：测试用 Units 列表控制"身份名单"。</summary>
    internal static class MusketeerIdentity
    {
        internal static readonly List<Archer> Units = new();
        internal static bool UnitResult;

        internal static void Reset() { Units.Clear(); UnitResult = false; }

        internal static bool IsUnit(Archer actor) => Units.Contains(actor) || UnitResult;
        internal static bool IsMarked(GameObject root) => false;
        internal static bool CanPurchase => false;
        internal static bool HasUnresolved => false;
        internal static string StatusText => "stub";
        internal static bool GunPromotionInProgress => false;

        internal static void CopyUnits(List<Archer> destination)
        {
            destination.Clear();
            for (int i = 0; i < Units.Count; i++) destination.Add(Units[i]);
        }
    }

    internal sealed class LogStub
    {
        public readonly List<string> Lines = new();
        public void LogInfo(string line) => Lines.Add("I:" + line);
        public void LogWarning(string line) => Lines.Add("W:" + line);
        public void LogError(string line) => Lines.Add("E:" + line);
    }

    internal sealed class PluginStub
    {
        public readonly LogStub LogSource = new();
    }

    internal static class KingdomEnhancedPlugin
    {
        internal static PluginStub Instance = new PluginStub();
    }
}
