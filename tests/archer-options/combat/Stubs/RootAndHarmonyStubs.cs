using System;
using System.Collections.Generic;
using HarmonyLib;

namespace HarmonyLib
{
    /// <summary>Attribute surface used by the production patch wrapper classes.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class HarmonyPatchAttribute : Attribute
    {
        public HarmonyPatchAttribute(Type declaringType) => DeclaringType = declaringType;

        public HarmonyPatchAttribute(Type declaringType, string methodName)
        {
            DeclaringType = declaringType;
            MethodName = methodName;
        }

        public Type DeclaringType { get; }

        public string MethodName { get; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPrefixAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPostfixAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyFinalizerAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPriorityAttribute : Attribute
    {
        public HarmonyPriorityAttribute(int priority) => Priority = priority;

        public int Priority { get; }
    }

    /// <summary>Harmony's priority constants (prefixes run high→low, postfixes/finalizers low→high).</summary>
    public static class Priority
    {
        public const int First = 800;
        public const int VeryHigh = 700;
        public const int High = 600;
        public const int HigherThanNormal = 500;
        public const int Normal = 400;
        public const int LowerThanNormal = 300;
        public const int Low = 200;
        public const int VeryLow = 100;
        public const int Last = 0;
    }
}

namespace BepInEx.Configuration
{
    /// <summary>Minimal ConfigEntry surface (value holder) used by the root-owned ModConfig.</summary>
    public class ConfigEntry<T>
    {
        public ConfigEntry(T value) => Value = value;

        public T Value;
    }
}

namespace KingdomArcherOptions.Combat.Tests
{
    public static class FakeOps
    {
        private static readonly List<string> Entries = new List<string>();

        public static void Reset() => Entries.Clear();

        public static void Add(string entry) => Entries.Add(entry);

        public static IReadOnlyList<string> All => Entries;

        public static int Count(string entry)
        {
            int count = 0;
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i] == entry) count++;
            return count;
        }
    }
}

namespace KingdomEnhancedMod
{
    using UnityEngine;

    /// <summary>Root-owned config surface (documented contract), stubbed so the module compiles standalone.</summary>
    public static class ModConfig
    {
        public static BepInEx.Configuration.ConfigEntry<bool> ArcherScatterEnabled = new BepInEx.Configuration.ConfigEntry<bool>(false);
        public static BepInEx.Configuration.ConfigEntry<int> ArcherVolleyCount = new BepInEx.Configuration.ConfigEntry<int>(3);
        public static BepInEx.Configuration.ConfigEntry<bool> ArcherRateEnabled = new BepInEx.Configuration.ConfigEntry<bool>(false);
        public static BepInEx.Configuration.ConfigEntry<float> ArcherRateMultiplier = new BepInEx.Configuration.ConfigEntry<float>(1.5f);
        public static BepInEx.Configuration.ConfigEntry<bool> ArcherImpactEnabled = new BepInEx.Configuration.ConfigEntry<bool>(false);
    }

    /// <summary>
    /// Root-owned scope helper (documented contract): IsActive = mod enabled + known world;
    /// IsCurrent = same live world layer/scene. Stub mirrors that rule for the tests.
    /// </summary>
    internal static class ArcherOptionsScope
    {
        internal static bool Active = true;
        internal static Transform Layer;

        internal static bool IsActive => Active;

        internal static bool IsCurrent(Component component)
        {
            try
            {
                return component != null && component.gameObject != null
                    && component.gameObject.activeInHierarchy && Layer != null
                    && Layer.gameObject != null && Layer.gameObject.activeInHierarchy
                    && component.gameObject.scene.handle == Layer.gameObject.scene.handle
                    && component.transform.IsChildOf(Layer);
            }
            catch (Exception) { return false; }
        }
    }

    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance;

        public readonly TestLogSource LogSource = new TestLogSource();
    }

    public class TestLogSource
    {
        public readonly List<string> Lines = new List<string>();

        public void LogInfo(string message) => Lines.Add("I " + message);

        public void LogWarning(string message) => Lines.Add("W " + message);

        public void LogError(string message) => Lines.Add("E " + message);
    }
}
