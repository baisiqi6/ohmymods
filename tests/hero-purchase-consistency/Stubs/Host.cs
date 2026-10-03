// Host-boundary stand-ins: BepInEx paths/logging, Harmony attributes (compile-only) and the
// ModConfig entries the compiled production files read. No Harmony patch is ever applied here.
using System;
using System.Collections.Generic;

namespace BepInEx
{
    public static class Paths
    {
        public static string ConfigPath = "";
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type declaringType, string methodName) { }
        public HarmonyPatch(Type declaringType, string methodName, Type[] argumentTypes) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPrefix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPostfix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyFinalizer : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPriority : Attribute
    {
        public HarmonyPriority(int priority) { }
    }

    public static class Priority
    {
        public const int First = 800;
        public const int Last = 0;
    }
}

namespace KingdomEnhancedMod
{
    internal sealed class ConfigEntryBool
    {
        internal bool Value;
    }

    internal static class ModConfig
    {
        internal static ConfigEntryBool Enabled = new ConfigEntryBool { Value = true };
        internal static ConfigEntryBool HeroArcherEnabled = new ConfigEntryBool { Value = true };
    }

    internal sealed class ManualLogSource
    {
        internal readonly List<string> Lines = new List<string>();
        internal void LogInfo(string message) => Lines.Add("INFO " + message);
        internal void LogWarning(string message) => Lines.Add("WARN " + message);
        internal void Clear() => Lines.Clear();
    }

    internal sealed class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
        internal ManualLogSource LogSource = new ManualLogSource();
    }
}
