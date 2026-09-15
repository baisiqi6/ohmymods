using System;

namespace HarmonyLib
{
    /// <summary>Test double for the HarmonyX attributes: enough for PatchAll-style discovery
    /// and for reflection assertions in the tests.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class HarmonyPatchAttribute : Attribute
    {
        public Type TargetType { get; }
        public string MethodName { get; }
        public Type[] ArgumentTypes { get; }

        public HarmonyPatchAttribute(Type targetType) { TargetType = targetType; }
        public HarmonyPatchAttribute(Type targetType, string methodName) { TargetType = targetType; MethodName = methodName; }
        public HarmonyPatchAttribute(Type targetType, string methodName, Type[] argumentTypes)
        {
            TargetType = targetType;
            MethodName = methodName;
            ArgumentTypes = argumentTypes;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPostfixAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPrefixAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyFinalizerAttribute : Attribute { }
}
