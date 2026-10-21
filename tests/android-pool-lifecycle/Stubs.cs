using System;
using System.Collections.Generic;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatchAttribute : Attribute
    {
        public HarmonyPatchAttribute(Type type) { }
        public HarmonyPatchAttribute(Type type, string method) { }
        public HarmonyPatchAttribute(string method) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPrefixAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPostfixAttribute : Attribute { }
}

namespace KingdomEnhancedMod
{
    public sealed class Pool { }
    internal sealed class World { public void OnLevelLoaded() { } }

    internal sealed class PoolManager
    {
        public IntPtr Pointer;
        public bool isActiveAndEnabled = true;
        public bool Destroyed;
        public int UnityId;
        private static int _nextUnityId;
        public List<Pool> cachedPools = new List<Pool>();
        public PoolManager(IntPtr pointer, int? unityId = null) { Pointer = pointer; UnityId = unityId ?? ++_nextUnityId; }
        private static bool IsNull(PoolManager value) => ReferenceEquals(value, null) || value.Destroyed;
        public static bool operator ==(PoolManager left, PoolManager right)
        {
            if (IsNull(left) || IsNull(right)) return IsNull(left) && IsNull(right);
            return left.UnityId == right.UnityId;
        }
        public static bool operator !=(PoolManager left, PoolManager right) => !(left == right);
        public override bool Equals(object other) => other is PoolManager pm && this == pm;
        public override int GetHashCode() => UnityId;
        public void InitPools() { }
        public void OnLevelLoaded() { }
    }

    internal sealed class Managers
    {
        public static Managers Inst;
        public PoolManager pools;
    }

    internal sealed class ConfigEntry
    {
        public bool Value = true;
    }

    internal static class ModConfig
    {
        public static readonly ConfigEntry Enabled = new ConfigEntry();
    }

    internal sealed class LogSource
    {
        public static bool ThrowOnInfo;
        public void LogInfo(string message) { if (ThrowOnInfo) throw new InvalidOperationException("test log failure"); }
        public void LogError(string message) { }
    }

    internal sealed class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
        public readonly LogSource LogSource = new LogSource();
    }

    internal static class PatchRoles_NorseSquad
    {
        public static int Calls;
        public static void EnsureNorseArcherPool() { Calls++; }
    }

    internal static class PatchRoles_Crossbowman
    {
        public static int Calls;
        public static void EnsureBoltPoolRegistered() { Calls++; }
    }

    internal static class PatchEconomy_BankAssistants
    {
        public static int Calls;
        public static void EnsurePreparedPools(PoolManager pm) { Calls++; }
    }

    internal static class TestTotals
    {
        public static int Writes =>
            PatchRoles_NorseSquad.Calls + PatchRoles_Crossbowman.Calls + PatchEconomy_BankAssistants.Calls;

        public static void Reset()
        {
            PatchRoles_NorseSquad.Calls = 0;
            PatchRoles_Crossbowman.Calls = 0;
            PatchEconomy_BankAssistants.Calls = 0;
        }
    }
}
