// 游戏类型 stub：字段名/签名与 2.4 interop 一致（私有字段在 interop 中暴露为属性，
// 本 stub 用公开字段等价替代）。行为只模拟本模块依赖的原生语义。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod
{
    public enum BuffType { AttackCooldownReduction = 0, FireAttacks = 1, MovementSpeed = 2, DamageInvulnerability = 3 }

    public enum DamageSource
    {
        Troll = 1, Arrow = 2, BoulderFriendly = 4, BoulderEnemy = 8, Knight = 16, Ogre = 32, Bolt = 64,
        PlayerSteed = 128, Pike = 256, Fire = 512, Stealer = 1024, Boar = 2048, Trap = 4096, Crusher = 8192,
        Fleet = 16384, GreedProjectile = 32768, SerpentAttack = 65536
    }

    public enum DamageSurface { Generic = 0, Armored = 1, Structure = 2, Huge = 3, Clone = 4 }

    public class Damageable : MonoBehaviour
    {
        public bool isDead;
        public DamageSurface surface;
        public bool invulnerable;
        public bool Immune;                 // 测试用：模拟 IsDamagedBy=false（火免疫等）
        public int ReceivedTotal;           // 累计受击点数
        public readonly List<int> UnknownSources = new List<int>();
        public readonly List<DamageSource> ReceivedSources = new List<DamageSource>();
        public int DotTicks;                // native TryDamage 的持续灼烧次数（被本模块取消的那部分）
        public Action<Damageable> OnReceive;

        public bool IsDamagedBy(DamageSource source) => !Immune && !isDead;

        public void ReceiveDamage(int amount, GameObject source, DamageSource damageSource)
        {
            ReceivedTotal += amount;
            ReceivedSources.Add(damageSource);
            OnReceive?.Invoke(this);
        }

        public void ApplyDelayedDamage(float delay, float offset, int perTick, int ticks, GameObject source, DamageSource damageSource, bool something)
        {
            DotTicks += ticks;
        }
    }

    public class Buffable : MonoBehaviour
    {
        public object _owner;
        public Il2CppSystem.Collections.Generic.List<BuffType> _applicableBuffs;
        public Il2CppSystem.Collections.Generic.Dictionary<BuffType, float> _activeBuffsExpirations;

        public static Buffable Create(GameObject go, object owner, IEnumerable<BuffType> applicable, IDictionary<BuffType, float> expirations)
        {
            Buffable b = go.AddComponent<Buffable>();
            b._owner = owner;
            b._applicableBuffs = new Il2CppSystem.Collections.Generic.List<BuffType>(applicable);
            b._activeBuffsExpirations = new Il2CppSystem.Collections.Generic.Dictionary<BuffType, float>(expirations);
            return b;
        }
    }

    public class Character : MonoBehaviour
    {
        public bool inert;
        public bool grabbed;
    }

    public class Embarkee : MonoBehaviour
    {
        public bool IsEmbarked;
    }

    public class Enemy : MonoBehaviour { }

    public class FriendlyTroll : MonoBehaviour { }

    public class ArrowAttack : UnityEngine.Object
    {
        public Arrow _arrowPrefab;
        public float Range = 6f;
    }

    /// <summary>Arrow stub：HitObject/TryDamage 按 2.4 native 语义实现（DOT 由 isFireArrow 控制）。</summary>
    public class Arrow : MonoBehaviour
    {
        public DamageSource _damageSource = DamageSource.Arrow;
        public int hitDamage = 1;
        public int perfectDamageMultiplier = 2;
        public bool isFireArrow;
        public int damagePerTick = 1;
        public int damageTicks = 3;
        public float damageDelayOffset = 0.5f;
        public float damageDelayTime = 1.5f;
        public GameObject archer;
        public bool _hasHit;
        public bool authorityActive = true;
        public bool _perfect;
        public bool IsBelowGround;
        public bool AlwaysDrawTrail;
        public int TrailEnabled;
        /// <summary>观测点：native TryDamage 当刻的 isFireArrow（本模块绝不改它）。</summary>
        public bool FireFlagAtTryDamage;
        /// <summary>测试用：native HitObject 调用 TryDamage 的分派点（默认直连 TryDamage）。</summary>
        public static Func<Arrow, Damageable, bool> TryDamageDispatch;
        public bool FireFlagAtHitObjectEntry;

        /// <summary>模拟 native Arrow.OnEnable 本体：2.4 不触碰 isFireArrow。</summary>
        public void OnEnableBody()
        {
            _hasHit = false;
            _perfect = false;
            if (isFireArrow || AlwaysDrawTrail) TrailEnabled++;
        }

        /// <summary>native HitObject 的 TryDamage 调用点：先走模块替代分派，再走原生。</summary>
        public bool NativeTryDamage(Damageable damageable)
        {
            FireFlagAtTryDamage = isFireArrow;         // native 调用点观测（两条路径都经过这里）
            Func<Arrow, Damageable, bool> dispatch = TryDamageDispatch;
            return dispatch != null ? dispatch(this, damageable) : TryDamage(damageable);
        }

        public bool TryDamage(Damageable damageable)
        {
            FireFlagAtTryDamage = isFireArrow;
            if (!authorityActive) return false;
            if (damageable == null) return false;
            if (isFireArrow && damageable.IsDamagedBy(DamageSource.Fire))
                damageable.ApplyDelayedDamage(damageDelayTime, damageDelayOffset, damagePerTick, damageTicks, gameObject, DamageSource.Fire, true);
            if (damageable.IsDamagedBy(_damageSource))
            {
                int damage = _perfect ? hitDamage * perfectDamageMultiplier : hitDamage;
                damageable.ReceiveDamage(damage, archer, _damageSource);
            }
            return true;
        }

        /// <summary>2.4 语义：无 Wall/Crusher/Damageable 且非 Ground 时早退，不置 _hasHit。</summary>
        public void HitObject(GameObject target, bool physicalHit)
        {
            FireFlagAtHitObjectEntry = isFireArrow;
            if (_hasHit || !NetworkBigBoss.HasWorldAuth) return;
            Damageable damageable = target != null ? target.GetComponentInParent<Damageable>() : null;
            if (damageable != null)
            {
                if (damageable.IsDamagedBy(_damageSource)) NativeTryDamage(damageable);
            }
            else if (target == null || !target.CompareTag("Ground"))
            {
                return;
            }
            _hasHit = true;
        }

        public int HitObjectCall;
        public void HitObjectTracked(GameObject target, bool physicalHit)
        {
            HitObjectCall++;
            HitObject(target, physicalHit);
        }
    }

    public class Archer : MonoBehaviour
    {
        public ArrowAttack _fireArrowAttack;
        public Knight _knight;
        public Damageable _damageable;
        public Character _character;
        public Embarkee _embarkee;
        public Buffable Buffable;
        public GameObject _shootingTarget;
    }

    public class Knight : MonoBehaviour
    {
        public bool _harmless;
        public Damageable _damageable;
        public Character _character;
        public Embarkee _embarkee;
        public Buffable Buffable;
    }

    public static class NetworkBigBoss
    {
        public static bool HasWorldAuth = true;
        public static bool HasClientCaughtUp = true;
    }

    /// <summary>2.4 原生层名常量（Layers.Enemies 为 string 属性）。</summary>
    public static class Layers
    {
        public static string Enemies => "Enemies";
        public static string Wildlife => "Wildlife";
    }

    public static class Tags
    {
        public static string Ground => "Ground";
    }
}

namespace Il2CppSystem.Collections.Generic
{
    /// <summary>只实现本模块使用的 Il2Cpp 集合成员。</summary>
    public class List<T>
    {
        private readonly System.Collections.Generic.List<T> inner;
        public List() { inner = new System.Collections.Generic.List<T>(); }
        public List(System.Collections.Generic.IEnumerable<T> items) { inner = new System.Collections.Generic.List<T>(items); }
        public bool Contains(T item) => inner.Contains(item);
        public int Count => inner.Count;
        public void Add(T item) => inner.Add(item);
    }

    public class Dictionary<TKey, TValue>
    {
        private readonly System.Collections.Generic.Dictionary<TKey, TValue> inner;
        public Dictionary() { inner = new System.Collections.Generic.Dictionary<TKey, TValue>(); }
        public Dictionary(IDictionary<TKey, TValue> items) { inner = new System.Collections.Generic.Dictionary<TKey, TValue>(items); }
        public bool TryGetValue(TKey key, out TValue value) => inner.TryGetValue(key, out value);
        public TValue this[TKey key] { get => inner[key]; set => inner[key] = value; }
        public bool ContainsKey(TKey key) => inner.ContainsKey(key);
        public int Count => inner.Count;
        public void Clear() => inner.Clear();
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HarmonyPatch : Attribute
    {
        public Type TargetType;
        public string MethodName;
        public HarmonyPatch(Type declaringType, string methodName) { TargetType = declaringType; MethodName = methodName; }
        public HarmonyPatch(Type declaringType) { TargetType = declaringType; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyFinalizer : Attribute { }
    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPriority : Attribute { public HarmonyPriority(int priority) { } }
}
