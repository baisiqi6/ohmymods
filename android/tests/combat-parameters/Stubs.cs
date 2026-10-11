// Issue196 worker host 桩（Stubs.cs）：只为编译两份候选与观察控制流提供最小类型面。
// 不镜像业务算法；不模拟 native/interop 生命周期（Awake 时机、Harmony 绑定、对象池由 Root 在真实环境验证）。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    internal sealed class HarmonyPatchAttribute : Attribute
    {
        public HarmonyPatchAttribute() { }

        public HarmonyPatchAttribute(Type type, string methodName) { }
    }

    [AttributeUsage(AttributeTargets.All)]
    internal sealed class HarmonyPrefixAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.All)]
    internal sealed class HarmonyPriorityAttribute : Attribute
    {
        public HarmonyPriorityAttribute(int priority) { }
    }

    internal static class Priority
    {
        internal const int High = 600; // 实际加载器常量；此桩不验证原生调度优先级。
    }
}

namespace UnityEngine
{
    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }
    }
}

// —— 以下为游戏侧类型的最小声明（真实程序集为 interop wrapper，此处仅保控制流形状）——

public sealed class GameObject
{
    private readonly object _component;

    public GameObject(object component) { _component = component; }

    // 同对象语义：返回构造时注入的同一实例；非 Knight 组件返回 null。
    public T GetComponent<T>() where T : class { return _component as T; }
}

public class Knight { }

public static class Kingdom
{
    // 声明仅供 HarmonyPatch 的 nameof 解析；host 不调用（原生排序/过滤逻辑不在 host 镜像）。
    public static void FetchArchersForJob(GameObject jobObject, float maxRange) { }
}

public class CrownStealer
{
    private float _walkSpeed;
    private float _runSpeed;
    private float _jumpSpeed;
    private float _chargeSpeed;
    private Vector2 _wallJumpForce;
    private Vector2 _chargeJumpForce;

    // 异常注入点：仅用于验证既有 try/catch 的局限语义（walk 已乘保持、后续未改、仅一次日志）。
    public bool ThrowOnRunSpeedGet;
    public bool ThrowOnRunSpeedSet;

    public virtual void Awake() { } // 仅供 nameof 解析；host 不调用、不模拟生命周期

    public float walkSpeed { get => _walkSpeed; set => _walkSpeed = value; }

    public float runSpeed
    {
        get
        {
            if (ThrowOnRunSpeedGet) throw new InvalidOperationException("stub: runSpeed getter");
            return _runSpeed;
        }
        set
        {
            if (ThrowOnRunSpeedSet) throw new InvalidOperationException("stub: runSpeed setter");
            _runSpeed = value;
        }
    }

    public float jumpSpeed { get => _jumpSpeed; set => _jumpSpeed = value; }

    public float chargeSpeed { get => _chargeSpeed; set => _chargeSpeed = value; }

    public Vector2 wallJumpForce { get => _wallJumpForce; set => _wallJumpForce = value; }

    public Vector2 chargeJumpForce { get => _chargeJumpForce; set => _chargeJumpForce = value; }
}

public sealed class Logger
{
    public readonly List<object> Entries = new List<object>();

    public void LogError(object message) { Entries.Add(message); }
}

public sealed class KingdomEnhancedPlugin
{
    public static KingdomEnhancedPlugin Instance;

    public Logger LogSource { get; } = new Logger();
}

// BepInEx ConfigEntry<T> 的最小读值形状：.Value（bool）。host 直接改写 .Value 模拟开关状态。
public sealed class ConfigEntry<T>
{
    public T Value { get; set; }
}

public static class ModConfig
{
    public static ConfigEntry<bool> Enabled;
    public static ConfigEntry<bool> CrownStealerSlowdownEnabled;
    public static ConfigEntry<bool> KnightRecruitRangeEnabled;
}
