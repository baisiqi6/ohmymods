// Mock host for the musketeer native-save adapter tests. Shapes mirror the actual 2.4 interop
// wrappers (Filer.DotNetAgent, GlobalSaveData/PrefsSaveData, Il2Cpp Action/Task/StructArray,
// Coatsink.Common.SaveLoadResult/Routine.Return). The production file is linked unchanged; only
// these types are doubled, so the gate/bridge logic under test is exactly the shipped one.
using System;
using System.Collections.Generic;

namespace BepInEx { public static class Paths { public static string ConfigPath; } }

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type) { }
        public HarmonyPatch(Type type, string method) { }
        public HarmonyPatch(Type type, string method, Type[] args) { }
    }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyFinalizer : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPriority : Attribute { public HarmonyPriority(int i) { } }
    public static class Priority { public const int First = 800, Last = 0; }
}

namespace Coatsink.Common
{
    [Flags]
    public enum SaveLoadResult
    {
        None = 0,
        Save = 8,
        Success = 64,
        Failure = 128,
        Busy = 256,
        Subsystem = 512,
        MissingData = 1024,
        CorruptedData = 2048,
        InsufficientSpace = 4096,
        Cancelled = 8192,
        InvalidVersion = 16384
    }

    public static class Routine
    {
        public struct Yield { }
        public sealed class Return<T> { public T value; }
    }
}

namespace Il2CppSystem
{
    public class Action<T>
    {
        private readonly System.Action<T> _managed;
        public Action(System.Action<T> managed) { _managed = managed; }
        public static implicit operator Action<T>(System.Action<T> value) => new Action<T>(value);
        public void Invoke(T value) => _managed(value);
    }
}

namespace Il2CppSystem.Threading.Tasks
{
    public class Task<TResult>
    {
        public bool IsCompleted { get; set; }
        public TResult Result { get; set; }
    }

    public static class Task
    {
        // Test switch proving the unsupported endpoint path (the real AOT generic can fail).
        public static bool ForceFailure;

        public static Task<TResult> FromResult<TResult>(TResult result)
        {
            if (ForceFailure) throw new InvalidOperationException("FromResult unavailable");
            return new Task<TResult> { IsCompleted = true, Result = result };
        }
    }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppStructArray<T>
    {
        private readonly T[] _items;
        public Il2CppStructArray(T[] items) { _items = items ?? Array.Empty<T>(); }
        public int Length => _items.Length;
        public T this[int index] { get => _items[index]; set => _items[index] = value; }
        public static implicit operator Il2CppStructArray<T>(T[] items) => new Il2CppStructArray<T>(items);
    }
}

namespace Il2CppSystem.Collections.Generic
{
    public class List<T> : System.Collections.Generic.List<T> { }
    public class Dictionary<K, V> : System.Collections.Generic.Dictionary<K, V> { }
}

public class CampaignSaveData
{
    private static long _counter;
    public IntPtr Pointer = (IntPtr)(++_counter);
    public int challengeId;
}

public class PrefsSaveData
{
    private static long _counter;
    public IntPtr Pointer = (IntPtr)(++_counter);
    public Il2CppSystem.Collections.Generic.Dictionary<string, string> contents = new();
}

public class GlobalSaveData
{
    private static long _counter;
    public static GlobalSaveData loaded;
    public static string filename = "global-v35";
    public int currentCampaign, currentChallenge;
    public IntPtr Pointer = (IntPtr)(++_counter);
    public PrefsSaveData prefs = new();
    public Il2CppSystem.Collections.Generic.List<CampaignSaveData> campaigns = new();
    public Il2CppSystem.Collections.Generic.List<CampaignSaveData> challenges = new();

    // Exists so the production [HarmonyPatch(typeof(GlobalSaveData), nameof(GlobalSaveData.SaveAsync))]
    // attribute resolves; the tests drive the prefix method directly.
    public void SaveAsync(Il2CppSystem.Action<Coatsink.Common.SaveLoadResult> callback) { }
}

public static class Filer
{
    public static string folder = "/tmp/ktc/Release";

    public class DotNetAgent
    {
        public string folder;

        public virtual Il2CppSystem.Threading.Tasks.Task<Coatsink.Common.SaveLoadResult> SaveFileToDiskAsync(
            string filename, string title, string details, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> data)
            => new Il2CppSystem.Threading.Tasks.Task<Coatsink.Common.SaveLoadResult>();

        public sealed class _SaveFileToDisk_d__10
        {
            public int __1__state;
            public Coatsink.Common.Routine.Yield __2__current;
            public DotNetAgent __4__this;
            public string filename;
            public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> data;
            public Coatsink.Common.Routine.Return<Coatsink.Common.SaveLoadResult> @return = new();

            public bool MoveNext() => false;
        }
    }
}

namespace KingdomEnhancedMod
{
    // Test host for the plugin log source used by the linked production files.
    internal sealed class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance = new();
        internal PluginLog LogSource = new();

        internal sealed class PluginLog
        {
            internal readonly List<string> Warnings = new();
            internal readonly List<string> Infos = new();
            internal void LogWarning(string text) => Warnings.Add(text);
            internal void LogInfo(string text) => Infos.Add(text);
        }
    }

    // Shape of the one member the runtime slice consumes from MusketeerPersistence (the real file
    // is part of the mod build; linking it here would drag the whole plugin graph).
    internal static class MusketeerPersistence
    {
        internal static string ArchivePath;
    }
}
