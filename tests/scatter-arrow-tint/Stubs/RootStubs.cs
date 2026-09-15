using System;
using System.Collections.Generic;

namespace HarmonyLib
{
    /// <summary>生产 patch 包装类用到的 Harmony 属性面（只做元数据，测试用反射断言目标）。</summary>
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
}

namespace BepInEx.Configuration
{
    public class ConfigEntry<T>
    {
        public ConfigEntry(T value) => Value = value;

        public T Value;
    }
}

namespace KingdomScatterTint.Tests
{
    /// <summary>捕获插件日志，供日志相关断言。</summary>
    public static class TestLog
    {
        public static readonly List<string> Lines = new List<string>();

        public static void Reset() => Lines.Clear();

        public static int Count(string fragment)
        {
            int count = 0;
            for (int i = 0; i < Lines.Count; i++) if (Lines[i].Contains(fragment)) count++;
            return count;
        }
    }
}

namespace KingdomEnhancedMod
{
    /// <summary>root 拥有的配置面（既定契约），桩起来让模块独立编译。</summary>
    public static class ModConfig
    {
        public static BepInEx.Configuration.ConfigEntry<bool> Enabled =
            new BepInEx.Configuration.ConfigEntry<bool>(true);

        public static BepInEx.Configuration.ConfigEntry<bool> ArcherScatterEnabled =
            new BepInEx.Configuration.ConfigEntry<bool>(true);
    }

    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance;

        public TestLogSource LogSource = new TestLogSource();
    }

    public class TestLogSource
    {
        public void LogInfo(string message) => KingdomScatterTint.Tests.TestLog.Lines.Add("I " + message);

        public void LogWarning(string message) => KingdomScatterTint.Tests.TestLog.Lines.Add("W " + message);

        public void LogError(string message) => KingdomScatterTint.Tests.TestLog.Lines.Add("E " + message);
    }
}
