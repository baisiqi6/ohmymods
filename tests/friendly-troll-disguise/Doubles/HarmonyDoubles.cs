using System;

// HarmonyLib 特性替身：生产文件需要 [HarmonyPatch] / [HarmonyPostfix] /
// [HarmonyPriority(Priority.Last)] 才能编译；测试不跑 Harmony 管线，只核对
// “后缀确实标了 Priority.Last”（读特性元数据断言）。
// 签名按真实 E build 22992091 的 BepInEx core/0Harmony.dll（HarmonyX）核对：
//   HarmonyLib.Priority          = 静态类，int 字段（不是 enum！）
//   HarmonyLib.HarmonyPriority   : Attribute { .ctor(int) }
//   HarmonyLib.HarmonyPostfix    : Attribute
//   HarmonyLib.HarmonyPatch      : HarmonyAttribute { .ctor(Type, string) ... }

namespace HarmonyLib
{
    /// <summary>HarmonyX 的优先级表：静态 int 字段（数值越小，后缀越晚跑）。</summary>
    public static class Priority
    {
        public const int Last = 0;
        public const int VeryLow = 100;
        public const int Low = 200;
        public const int LowerThanNormal = 300;
        public const int Normal = 400;
        public const int HigherThanNormal = 500;
        public const int High = 600;
        public const int VeryHigh = 700;
        public const int First = 800;
    }

    public class HarmonyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : HarmonyAttribute
    {
        public HarmonyPatch()
        {
        }

        public HarmonyPatch(Type declaringType)
        {
            DeclaringType = declaringType;
        }

        public HarmonyPatch(Type declaringType, string methodName)
        {
            DeclaringType = declaringType;
            MethodName = methodName;
        }

        public HarmonyPatch(Type declaringType, string methodName, Type[] argumentTypes)
        {
            DeclaringType = declaringType;
            MethodName = methodName;
            ArgumentTypes = argumentTypes;
        }

        public Type DeclaringType { get; }

        public string MethodName { get; }

        public Type[] ArgumentTypes { get; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPostfix : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPriority : Attribute
    {
        public HarmonyPriority(int priority)
        {
            Priority = priority;
        }

        public int Priority { get; }
    }
}
