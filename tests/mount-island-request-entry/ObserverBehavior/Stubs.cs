using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

// Host-only stubs for compiling/running the exact production Observer file. No Unity / GameAssembly /
// interop DLL is referenced or loaded. The stubs model only the fields/methods the stale-terminal
// scenario needs; they do not reimplement the Observer decision logic.
internal static class StubState
{
    internal static int TypedGetterCalls;
    internal static int NativeFieldGetCalls;
    internal static int Flags = 0x410;
    internal static bool DriftOnRead;
    internal static bool DriftDone;
    internal static KingdomEnhancedMod.MountIslandRequestEnvelope DriftEnvelope;

    internal static void OnFlagsRead()
    {
        if (!DriftOnRead || DriftDone) return;
        DriftDone = true;
        KingdomEnhancedMod.MountIslandFreshObservation.Gate.TrySealEnvelope(DriftEnvelope);
    }
}

public sealed class IslandSaveData : Il2CppObjectBase
{
    public static bool isSavingGame;
    public int land;
    public double playTimeDays;
    public double lastPlayedTimeDays;
    public Il2CppSystem.Collections.Generic.List<ObjectData> objects;
    public Il2CppSystem.Collections.Generic.List<int> savedWithRevisions;
    public Coatsink.Common.Routine.Return<Coatsink.Common.SaveLoadResult> @return;

    public sealed class ObjectData { }

    public sealed class __TryLoad_d__54 : Il2CppObjectBase
    {
        public int __1__state = -1;
        public int campaignIndex;
        public int challengeId;
        public int land;
        public Coatsink.Common.Routine.Return<Coatsink.Common.SaveLoadResult> @return;
    }
}

public sealed class GlobalSaveData : Il2CppObjectBase
{
    public static GlobalSaveData loaded;
    public bool InChallenge;
}

public static class NetworkBigBoss
{
    public static bool IsOnline;
}

public sealed class CampaignSaveData : Il2CppObjectBase
{
    public static CampaignSaveData current;
    public int CurrentLand;
    public int reign;
    public Il2CppSystem.Collections.Generic.List<IslandSaveData> _islands;
    public Il2CppSystem.Collections.Generic.List<int> visitedIslands;
    public ReignInfo currentReign;

    public sealed class ReignInfo
    {
        public Il2CppSystem.Collections.Generic.List<LandMapData> landData;
    }

    public sealed class LandMapData
    {
        public double lastPlayedTimeDays;
        public Il2CppStructArray<SteedType> steedSpawns;
    }
}

public sealed class BiomeHolder : Il2CppObjectBase
{
    public const int GreeceBiomeIndex = 1;
    public static BiomeHolder Inst;
    public int BiomeIndex;
}

public enum SteedType { None = 0 }

namespace Il2CppSystem.Collections.Generic
{
    public sealed class List<T> : Il2CppObjectBase
    {
        private readonly System.Collections.Generic.List<T> _items = new System.Collections.Generic.List<T>();

        public int Count => _items.Count;

        public T this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        public bool Contains(T item) => _items.Contains(item);
        public void Add(T item) => _items.Add(item);
    }
}

namespace Il2CppInterop.Runtime.InteropTypes
{
    public class Il2CppObjectBase
    {
        public IntPtr Pointer;
    }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppStructArray<T>
    {
        public int Length;
    }
}

namespace Il2CppInterop.Runtime
{
    public static class Il2CppClassPointerStore<T>
    {
        public static IntPtr NativeClassPtr
        {
            get
            {
                if (typeof(T) == typeof(IslandSaveData.__TryLoad_d__54)) return new IntPtr(1);
                if (typeof(T) == typeof(Coatsink.Common.Routine.Return<Coatsink.Common.SaveLoadResult>))
                    return new IntPtr(2);
                if (typeof(T) == typeof(Coatsink.Common.SaveLoadResult)) return new IntPtr(3);
                return IntPtr.Zero;
            }
        }
    }

    public static unsafe class IL2CPP
    {
        public static IntPtr il2cpp_object_get_class(IntPtr obj) => new IntPtr(1);
        public static bool il2cpp_class_is_valuetype(IntPtr cls) => cls.ToInt64() != 1;
        public static IntPtr il2cpp_class_get_field_from_name(IntPtr cls, string name) => new IntPtr(10);
        public static IntPtr il2cpp_field_get_parent(IntPtr field) => new IntPtr(1);
        public static int il2cpp_field_get_flags(IntPtr field) => 0;
        public static IntPtr il2cpp_field_get_type(IntPtr field)
            => field.ToInt64() == 10 ? new IntPtr(100) : new IntPtr(200);
        public static int il2cpp_type_get_type(IntPtr type)
            => type.ToInt64() == 100 ? 0x15 : type.ToInt64() == 200 ? 0x11 : 0x08;
        public static bool il2cpp_type_is_byref(IntPtr type) => false;
        public static IntPtr il2cpp_class_from_type(IntPtr type)
            => type.ToInt64() == 100 ? new IntPtr(2) : new IntPtr(3);
        public static int il2cpp_class_value_size(IntPtr cls, ref uint align) { align = 4; return 4; }
        public static bool il2cpp_class_is_enum(IntPtr cls) => cls.ToInt64() == 3;
        public static IntPtr il2cpp_class_enum_basetype(IntPtr cls) => new IntPtr(300);
        public static IntPtr il2cpp_class_get_fields(IntPtr cls, ref IntPtr iterator)
        {
            if (iterator == IntPtr.Zero) { iterator = new IntPtr(1); return new IntPtr(20); }
            return IntPtr.Zero;
        }
        public static string il2cpp_field_get_name_(IntPtr field) => "value";
        public static void il2cpp_field_get_value(IntPtr obj, IntPtr field, void* value)
        {
            StubState.NativeFieldGetCalls++;
            StubState.OnFlagsRead();
            *(int*)value = 0x410;
        }
    }
}

namespace Coatsink.Common
{
    public enum SaveLoadResult
    {
        None = 0, Pending = 1, Init = 2, Metadata = 4, Save = 8, Load = 16,
        Delete = 32, Success = 64, Failure = 128, Busy = 256, Subsystem = 512,
        MissingData = 1024, CorruptedData = 2048, InsufficientSpace = 4096,
        Cancelled = 8192, InvalidVersion = 16384,
    }

    public static class Routine
    {
        public struct Return<T>
        {
            public T value
            {
                get
                {
                    StubState.TypedGetterCalls++;
                    StubState.OnFlagsRead();
                    return (T)(object)(SaveLoadResult)StubState.Flags;
                }
                set { }
            }
        }
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string methodName) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPrefix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPostfix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyFinalizer : Attribute { }
}

namespace KingdomEnhancedMod
{
    internal sealed class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance;
        internal StubLogSource LogSource = new StubLogSource();

        internal sealed class StubLogSource
        {
            internal void LogInfo(string message) { }
            internal void LogError(string message) { }
        }
    }

    internal sealed class LevelConfig { }

    internal sealed class Level : Il2CppObjectBase { }

    internal static class ExtensionIslandRuntime
    {
        internal static bool TryResolveRawOwner(out GlobalSaveData global, out int selected,
            out CampaignSaveData current)
        {
            global = GlobalSaveData.loaded;
            selected = 0;
            current = CampaignSaveData.current;
            return global != null && current != null;
        }

        internal static bool IsOwnedConfig(LevelConfig config, int land) => true;
    }

    internal static class CrossWorldMountDependencies
    {
        internal static ulong PointerOfList<T>(Il2CppSystem.Collections.Generic.List<T> list)
            => list != null ? (ulong)list.Pointer.ToInt64() : 0UL;
    }

    internal static class CrossWorldMountRuntime
    {
        internal sealed class Frame
        {
            public LevelConfig Config;
            public MountIslandFrameTicket FreshTicket;
            public ulong TargetLevelPointer;
            public ulong LevelPointer;
            public ulong ConfigPointer;
        }
    }
}
