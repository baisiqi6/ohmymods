// hero-movement 测试替身面：只补 production HeroArcherMovement.cs 真正引用的成员。
// 认领模块没有逐帧 Unity 数学，替身只承载字段访问与身份（Pointer/GO InstanceID）；
// 抛错开关用于模拟 interop 瞬时异常（读/写字段、Deadlands 探针）。
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    internal class Object
    {
        private static int _nextInstanceId = 1;
        internal readonly int InstanceId = _nextInstanceId++;

        public string name = string.Empty;

        /// <summary>interop 身份：生产的指针对照只比较它。</summary>
        public IntPtr Pointer;

        /// <summary>测试钩子：GetInstanceID 返回 0（身份不可读）。</summary>
        internal bool ZeroInstanceId;
        internal bool ThrowInstanceId;

        public int GetInstanceID() => ThrowInstanceId ? throw new InvalidOperationException("identity read") : ZeroInstanceId ? 0 : InstanceId;
    }

    internal class Component : Object
    {
        internal GameObject Owner;
        public GameObject gameObject => Owner;
    }

    internal class Behaviour : Component
    {
        public bool enabled = true;
    }

    internal class MonoBehaviour : Behaviour
    {
    }

    internal class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();

        public GameObject(string objectName = "go")
        {
            name = objectName;
        }

        public T AddComponent<T>() where T : Component, new()
        {
            T component = new T { Owner = this };
            Components.Add(component);
            return component;
        }
    }
}

// ---- 游戏类型替身（最小面） ----

internal class Archer : UnityEngine.MonoBehaviour
{
    private float _walkSpeed = 4f;
    private float _runSpeed = 6f;

    /// <summary>测试钩子：读写速度字段抛错（模拟 interop 瞬时异常）。</summary>
    internal bool ThrowOnWalkSpeedRead, ThrowOnWalkSpeedWrite;
    internal bool ThrowOnRunSpeedRead, ThrowOnRunSpeedWrite;

    /// <summary>原生字段在替身里用属性承载，以便测试注入读/写异常（与 runtime 替身的 shootRange 同法）。</summary>
    public float walkSpeed
    {
        get
        {
            if (ThrowOnWalkSpeedRead) throw new InvalidOperationException("stub walk read failure");
            return _walkSpeed;
        }
        set
        {
            if (ThrowOnWalkSpeedWrite) throw new InvalidOperationException("stub walk write failure");
            _walkSpeed = value;
        }
    }

    public float runSpeed
    {
        get
        {
            if (ThrowOnRunSpeedRead) throw new InvalidOperationException("stub run read failure");
            return _runSpeed;
        }
        set
        {
            if (ThrowOnRunSpeedWrite) throw new InvalidOperationException("stub run write failure");
            _runSpeed = value;
        }
    }

    internal Mover _mover;
}

internal class Mover : UnityEngine.MonoBehaviour
{
    public IntPtr Pointer;
}

// ---- 跨模块替身（生产侧同签名） ----

internal sealed class ManualLogSource
{
    internal readonly List<string> Infos = new List<string>();
    internal readonly List<string> Warnings = new List<string>();
    internal readonly List<string> Errors = new List<string>();

    internal void LogInfo(string message) => Infos.Add(message);
    internal void LogWarning(string message) => Warnings.Add(message);
    internal void LogError(object message) => Errors.Add(message?.ToString() ?? string.Empty);
}

internal class KingdomEnhancedPlugin
{
    internal static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
    internal ManualLogSource LogSource = new ManualLogSource();
}

/// <summary>PatchRoles_DeadlandsPowers 的边界替身：镜像 Mover_Update_DeadlandsSpeed_Patch 的判定面。</summary>
internal static class PatchRoles_DeadlandsPowers
{
    internal sealed class UnitRef
    {
        internal object Knight;
        internal Archer Archer;
    }

    internal static bool Enabled;
    internal static bool ThrowFromProbe;
    internal static readonly Dictionary<IntPtr, UnitRef> ByMover = new Dictionary<IntPtr, UnitRef>();
    internal static readonly List<Archer> Followers = new List<Archer>();

    internal static bool GameplayActive()
    {
        if (ThrowFromProbe) throw new InvalidOperationException("stub deadlands probe failure");
        return Enabled;
    }

    internal static bool IsDeadlandsFollower(Archer archer) => archer != null && Followers.Contains(archer);

    internal static void MarkFollower(Archer archer)
    {
        if (archer != null && !Followers.Contains(archer)) Followers.Add(archer);
    }

    internal static void Reset()
    {
        Enabled = false;
        ThrowFromProbe = false;
        ByMover.Clear();
        Followers.Clear();
    }
}
