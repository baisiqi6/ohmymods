using System;
using System.Collections.Generic;
using MedievalScatterPolicyTests;
using UnityEngine;

// 测试用边界 stub：只提供 MedievalScatterPolicy.cs 触及的 Unity/interop 表面（2.4 interop 形状），
// 以及生产协作类（ArcherOptionsScope / PatchRoles_KnightStyle / PatchRoles_Crossbowman /
// PatchRoles_NorseSquad）的测试替身。所有 stub 可变状态的写入都计数（Evidence.Writes）。
// 生产源码不改写；本文件只在测试程序集里编译。

namespace UnityEngine
{
    internal class Object
    {
        internal string name;

        internal Object(string name) => this.name = name;
    }

    internal class Component : Object
    {
        internal GameObject gameObject;

        internal Component(string name) : base(name) { }
    }

    internal class Transform : Component
    {
        internal Transform parent;

        internal Transform(string name) : base(name) { }

        internal bool IsChildOf(Transform other)
        {
            for (Transform current = this; current != null; current = current.parent)
            {
                if (ReferenceEquals(current, other)) return true;
            }
            return false;
        }
    }

    internal class MonoBehaviour : Component
    {
        private bool _enabled = true;

        internal MonoBehaviour(string name) : base(name) { }

        internal bool enabled
        {
            get => _enabled;
            set { _enabled = value; Evidence.Wrote(); }
        }
    }

    internal class GameObject : Object
    {
        private readonly Dictionary<Type, Component> _components = new Dictionary<Type, Component>();
        private int _layer;
        private bool _activeInHierarchy = true;
        private string _tag;

        internal GameObject(string name) : base(name)
        {
            transform = new Transform(name + "/transform") { gameObject = this };
        }

        internal Transform transform { get; }

        internal bool activeInHierarchy
        {
            get => _activeInHierarchy;
            set { _activeInHierarchy = value; Evidence.Wrote(); }
        }

        internal string tag
        {
            get => _tag;
            set { _tag = value; Evidence.Wrote(); }
        }

        internal int layer
        {
            get => _layer;
            set { _layer = value; Evidence.Wrote(); }
        }

        internal T AddComponentForTests<T>(T component) where T : Component
        {
            component.gameObject = this;
            _components[typeof(T)] = component;
            return component;
        }

        internal T GetComponent<T>() where T : Component
        {
            return _components.TryGetValue(typeof(T), out Component component) ? (T)component : null;
        }

        internal T GetComponentInParent<T>() where T : Component
        {
            for (GameObject go = this; go != null; go = go.transform.parent != null ? go.transform.parent.gameObject : null)
            {
                T component = go.GetComponent<T>();
                if (component != null) return component;
            }
            return null;
        }

        internal bool CompareTag(string value) => string.Equals(_tag, value, StringComparison.Ordinal);
    }

    internal static class LayerMask
    {
        internal const int EnemiesLayer = 9;
        internal const int WildlifeLayer = 11;

        internal static int NameToLayer(string name) =>
            name == "Enemies" ? EnemiesLayer : name == "Wildlife" ? WildlifeLayer : -1;
    }
}

namespace KingdomEnhancedMod
{
    internal enum DamageSource
    {
        Arrow = 2,
    }

    internal class Damageable : UnityEngine.MonoBehaviour
    {
        private bool _isDead;

        internal Damageable() : base("Damageable") { }

        internal bool isDead
        {
            get => _isDead;
            set { _isDead = value; Evidence.Wrote(); }
        }

        internal Func<DamageSource, bool> DamagedBy = _ => true;

        internal bool IsDamagedBy(DamageSource source) => DamagedBy(source);
    }

    internal class Character : UnityEngine.MonoBehaviour
    {
        private bool _inert;
        private bool _grabbed;

        internal Character() : base("Character") { }

        internal bool inert
        {
            get => _inert;
            set { _inert = value; Evidence.Wrote(); }
        }

        internal bool grabbed
        {
            get => _grabbed;
            set { _grabbed = value; Evidence.Wrote(); }
        }
    }

    internal class FriendlyTroll : UnityEngine.MonoBehaviour
    {
        internal FriendlyTroll() : base("FriendlyTroll") { }
    }

    internal class Knight : UnityEngine.MonoBehaviour
    {
        private Damageable _damageableValue;

        internal Knight() : base("Knight") { }

        internal Damageable _damageable
        {
            get => _damageableValue;
            set { _damageableValue = value; Evidence.Wrote(); }
        }
    }

    internal class Archer : UnityEngine.MonoBehaviour
    {
        internal enum AttackMode
        {
            Ranged = 0,
            Melee = 1,
            Shield = 2,
        }

        private GameObject _shootingTargetValue;
        private Knight _knightValue;
        private Damageable _damageableValue;
        private Character _characterValue;
        private bool _harmlessValue;
        private AttackMode _attackModeValue = AttackMode.Ranged;
        private AttackMode _desiredAttackModeValue = AttackMode.Ranged;

        internal Archer() : base("Archer") { }

        internal GameObject _shootingTarget
        {
            get => _shootingTargetValue;
            set { _shootingTargetValue = value; Evidence.Wrote(); }
        }

        internal Knight _knight
        {
            get => _knightValue;
            set { _knightValue = value; Evidence.Wrote(); }
        }

        internal Damageable _damageable
        {
            get => _damageableValue;
            set { _damageableValue = value; Evidence.Wrote(); }
        }

        internal Character _character
        {
            get => _characterValue;
            set { _characterValue = value; Evidence.Wrote(); }
        }

        internal bool harmless
        {
            get => _harmlessValue;
            set { _harmlessValue = value; Evidence.Wrote(); }
        }

        internal AttackMode _attackMode
        {
            get => _attackModeValue;
            set { _attackModeValue = value; Evidence.Wrote(); }
        }

        internal AttackMode _desiredAttackMode
        {
            get => _desiredAttackModeValue;
            set { _desiredAttackModeValue = value; Evidence.Wrote(); }
        }
    }

    internal static class Layers
    {
        internal static string Enemies => "Enemies";
    }

    // ---- 生产协作类的测试替身（boundary double）：只模拟判定入口，不模拟其内部实现 ----

    /// <summary>当前 world 判定替身：默认全部“当前世界”，Foreign 集合内的对象视为异 world。</summary>
    internal static class ArcherOptionsScope
    {
        internal static readonly HashSet<UnityEngine.Object> Foreign = new HashSet<UnityEngine.Object>();
        internal static readonly HashSet<UnityEngine.Object> Throwing = new HashSet<UnityEngine.Object>();

        internal static bool IsCurrent(UnityEngine.Component component)
        {
            if (component != null && Throwing.Contains(component))
                throw new InvalidOperationException("stub: IsCurrent read failed");
            return component != null && !Foreign.Contains(component);
        }
    }

    /// <summary>固定身份风格解析替身：Styles 里登记了才是已解析（未登记 = 未决）。</summary>
    internal static class PatchRoles_KnightStyle
    {
        internal static readonly Dictionary<Knight, int> Styles = new Dictionary<Knight, int>();

        internal static bool TryGetResolvedStyleIndex(Knight knight, out int styleIndex)
        {
            styleIndex = -1;
            if (knight == null) return false;
            if (Styles.TryGetValue(knight, out int resolved))
            {
                styleIndex = resolved;
                return true;
            }
            return false;
        }
    }

    internal static class PatchRoles_Crossbowman
    {
        internal static readonly HashSet<Archer> Crossbows = new HashSet<Archer>();

        internal static bool IsCrossbowman(Archer archer) => archer != null && Crossbows.Contains(archer);
    }

    internal static class PatchRoles_NorseSquad
    {
        internal static readonly HashSet<Archer> NorseFollowers = new HashSet<Archer>();

        internal static bool IsNorseArcherInstance(Archer archer) => archer != null && NorseFollowers.Contains(archer);
    }
}
