// ModDoubles.cs — stand-ins for the mod-side surface the compiled production files touch:
// BepInEx configuration, the plugin log, and the sibling modules PatchRoles_Castle calls into.
// Only observable counters/logs are modelled; no sibling behaviour is simulated.
using System;
using System.Collections.Generic;

namespace BepInEx.Configuration
{
    public class ConfigEntry<T>
    {
        public T Value;
        public ConfigEntry() { }
        public ConfigEntry(T value) { Value = value; }
    }
}

public class ManualLogSource
{
    public readonly List<string> Infos = new List<string>();
    public readonly List<string> Warnings = new List<string>();
    public readonly List<string> Errors = new List<string>();

    public void LogInfo(string message) => Infos.Add(message);
    public void LogWarning(string message) => Warnings.Add(message);
    public void LogDebug(string message) => Infos.Add("DEBUG " + message);
    public void LogError(object message) => Errors.Add(Convert.ToString(message));

    public void Clear() { Infos.Clear(); Warnings.Clear(); Errors.Clear(); }

    public bool ErrorsContain(string fragment)
    {
        for (int i = 0; i < Errors.Count; i++)
            if (Errors[i] != null && Errors[i].Contains(fragment)) return true;
        return false;
    }

    public bool InfosContain(string fragment)
    {
        for (int i = 0; i < Infos.Count; i++)
            if (Infos[i] != null && Infos[i].Contains(fragment)) return true;
        return false;
    }
}

public class KingdomEnhancedPlugin
{
    public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
    public ManualLogSource LogSource = new ManualLogSource();

    public static void ResetLog() { Instance = new KingdomEnhancedPlugin(); }
}

namespace KingdomEnhancedMod
{
    using BepInEx.Configuration;

    public static class ModConfig
    {
        public static ConfigEntry<bool> Enabled = new ConfigEntry<bool>(true);

        public static void ResetConfig() { Enabled = new ConfigEntry<bool>(true); }
    }

    /// <summary>Ninja pool module stand-in (its own file is compiled by the real 2.4 build).</summary>
    public static class PatchRoles_Ninja
    {
        public static int EnsureRuntimePoolsCalls;
        public static void EnsureRuntimePoolsInGreece(Holder holder) { EnsureRuntimePoolsCalls++; }
        public static void ResetCounters() { EnsureRuntimePoolsCalls = 0; }
    }

    /// <summary>Ghost squad pool module stand-in; SyncIdMin/Max only bound the pool sync-id allocator.</summary>
    public static class PatchDivine_GhostSquads
    {
        public const int SyncIdMin = 30130;
        public const int SyncIdMax = 30149;

        public static int EnsurePoolsCalls;
        public static void EnsurePools() { EnsurePoolsCalls++; }
        public static void ResetCounters() { EnsurePoolsCalls = 0; }
    }

    /// <summary>Norse squad pool module stand-in.</summary>
    public static class PatchRoles_NorseSquad
    {
        public static int EnsurePoolCalls;
        public static void EnsureNorseArcherPool() { EnsurePoolCalls++; }
        public static void ResetCounters() { EnsurePoolCalls = 0; }
    }
}
