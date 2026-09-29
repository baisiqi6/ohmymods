using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type declaringType, string methodName) { }
    }

    public class HarmonyPrefix : Attribute { }
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

/// <summary>占位：非目标成员哨兵（Prefix 不得触碰该引用）。</summary>
public class Mover { }

/// <summary>
/// CrownStealer 的最小 interop 形状：六个目标成员按 interop 惯例暴露为同名属性
/// （结构体经属性访问无法原地改写子成员，与真实 interop 一致），另带若干无关哨兵成员。
/// </summary>
public class CrownStealer
{
    public float walkSpeed { get; set; }
    public float runSpeed { get; set; }
    public float jumpSpeed { get; set; }
    public float chargeSpeed { get; set; }
    public Vector2 wallJumpForce { get; set; }
    public Vector2 chargeJumpForce { get; set; }

    // 非目标成员：用于确认 Prefix 只改六个水平量。
    public float attackRange { get; set; }
    public float damage { get; set; }
    public float jumpCooldown { get; set; }
    public Mover _mover { get; set; } = new Mover();

    public virtual void Awake() { }
}

namespace KingdomEnhancedMod
{
    public static class ModConfig
    {
        public static BoolConfig Enabled = new BoolConfig();

        public class BoolConfig
        {
            public bool Value;
        }
    }

    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance;
        public TestLogSource LogSource = new TestLogSource();

        public class TestLogSource
        {
            public List<Exception> Errors = new List<Exception>();

            public void LogError(Exception e) => Errors.Add(e);
        }
    }
}
