using System;

// Host-only metadata stub for the exact production Patch_MountIslandNativeResult.cs.
// No Unity / GameAssembly / interop DLL is referenced or loaded. The fake metadata preserves the
// independent IL2CPP type kind and byref flag, so kind=GENERICINST/VALUETYPE + byref=true is
// representable exactly as the real runtime does.
public sealed class IslandSaveData
{
    public sealed class __TryLoad_d__54
    {
        public IntPtr Pointer => new IntPtr(1);
    }
}

namespace Coatsink.Common
{
    public enum SaveLoadResult
    {
        None = 0,
        Pending = 1,
        Init = 2,
        Metadata = 4,
        Save = 8,
        Load = 16,
        Delete = 32,
        Success = 64,
        Failure = 128,
        Busy = 256,
        Subsystem = 512,
        MissingData = 1024,
        CorruptedData = 2048,
        InsufficientSpace = 4096,
        Cancelled = 8192,
        InvalidVersion = 16384,
    }

    public static class Routine
    {
        public struct Return<T>
        {
            public T value;
        }
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
        internal static bool ReturnByRef;
        internal static bool ValueByRef;
        internal static bool EnumBaseByRef;
        internal static int EnumBaseKind = 0x08;   // I4
        internal static int FieldReadCalls;
        internal static int ByRefApiCalls;

        public static IntPtr il2cpp_object_get_class(IntPtr obj) => new IntPtr(1);
        public static bool il2cpp_class_is_valuetype(IntPtr cls) => cls.ToInt64() != 1;
        public static IntPtr il2cpp_class_get_field_from_name(IntPtr cls, string name) => new IntPtr(10);
        public static IntPtr il2cpp_field_get_parent(IntPtr field) => new IntPtr(1);
        public static int il2cpp_field_get_flags(IntPtr field) => 0;

        public static IntPtr il2cpp_field_get_type(IntPtr field)
            => field.ToInt64() == 10 ? new IntPtr(100) : new IntPtr(200);

        public static int il2cpp_type_get_type(IntPtr type)
        {
            if (type.ToInt64() == 100) return 0x15;  // GENERICINST for Return<SaveLoadResult>
            if (type.ToInt64() == 200) return 0x11;  // VALUETYPE for SaveLoadResult
            if (type.ToInt64() == 300) return EnumBaseKind;
            return 0x08;
        }

        public static bool il2cpp_type_is_byref(IntPtr type)
        {
            ByRefApiCalls++;
            if (type.ToInt64() == 100) return ReturnByRef;
            if (type.ToInt64() == 200) return ValueByRef;
            if (type.ToInt64() == 300) return EnumBaseByRef;
            return false;
        }

        public static IntPtr il2cpp_class_from_type(IntPtr type)
            => type.ToInt64() == 100 ? new IntPtr(2) : new IntPtr(3);

        public static int il2cpp_class_value_size(IntPtr cls, ref uint align)
        {
            align = 4;
            return 4;
        }

        public static bool il2cpp_class_is_enum(IntPtr cls) => cls.ToInt64() == 3;
        public static IntPtr il2cpp_class_enum_basetype(IntPtr cls) => new IntPtr(300);

        public static IntPtr il2cpp_class_get_fields(IntPtr cls, ref IntPtr iterator)
        {
            if (iterator == IntPtr.Zero)
            {
                iterator = new IntPtr(1);
                return new IntPtr(20);   // unique non-static `value` field
            }
            return IntPtr.Zero;
        }

        public static string il2cpp_field_get_name_(IntPtr field) => "value";

        public static void il2cpp_field_get_value(IntPtr obj, IntPtr field, void* value)
        {
            FieldReadCalls++;
            *(int*)value = 0x410;
        }
    }
}
