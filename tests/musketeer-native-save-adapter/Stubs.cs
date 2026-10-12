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

        // Boxed value wrapper mirroring the generated 2.4 interop shape. The `value` getter
        // deliberately reproduces the observed defect: the generated getter hands
        // PointerToValueGeneric(fieldAddress, true, false) to the runtime, which boxes twice and
        // returns the box object header as the enum (656417088 = 0x27202140). Production read
        // sites must resolve the field through live metadata instead; the setter is the correct
        // static chain and writes the native field storage the fake heap exposes.
        public sealed class Return<T> : INativeValueBox
        {
            internal const int DefectiveGetterSentinel = unchecked((int)0x27202140);
            private int _nativeValue;

            public Return() => Pointer = NativeFakeHeap.RegisterBox(this, NativeFakeHeap.ClassFor<Routine.Return<T>>());

            public IntPtr Pointer { get; }

            public T value
            {
                get => unchecked((T)(object)DefectiveGetterSentinel);   // the double-box result
                set => _nativeValue = Convert.ToInt32(value);
            }

            int INativeValueBox.NativeValue { get => _nativeValue; set => _nativeValue = value; }
        }
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

namespace Il2CppInterop.Runtime
{
    // The live-metadata surface the production sync-return reader consumes, doubled over
    // NativeFakeHeap. Logical double only: it proves production routing/guards, not ABI or layout.
    public static class IL2CPP
    {
        public static IntPtr il2cpp_object_get_class(IntPtr obj) => NativeFakeHeap.ClassOf(obj);
        public static IntPtr il2cpp_class_get_field_from_name(IntPtr klass, string name) => NativeFakeHeap.FieldOf(klass, name);
        public static int il2cpp_field_get_flags(IntPtr field) => NativeFakeHeap.FieldFlags(field);
        public static IntPtr il2cpp_field_get_type(IntPtr field) => NativeFakeHeap.FieldTypeOf(field);
        public static IntPtr il2cpp_class_from_type(IntPtr type) => NativeFakeHeap.ClassFromType(type);
        public static bool il2cpp_class_is_valuetype(IntPtr klass) => NativeFakeHeap.IsValueType(klass);
        public static int il2cpp_class_value_size(IntPtr klass, ref uint align) => NativeFakeHeap.ValueSize(klass);
        public static unsafe void il2cpp_field_get_value(IntPtr obj, IntPtr field, void* value) => NativeFakeHeap.ReadField(obj, field, value);
    }

    public static class Il2CppClassPointerStore<T>
    {
        public static IntPtr NativeClassPtr = NativeFakeHeap.ClassFor<T>().Pointer;
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

// ---------------------------------------------------------------------------- native fake host

// Simulated native heap behind the doubled IL2CPP metadata surface. Every class/field/type
// identity is a distinct fake handle, class tables own field tables and field types, and a box's
// `value` storage is a plain int the tests read directly. Guards execute against this heap exactly
// as they would against live il2cpp metadata; it is a logical double, never an ABI model.
internal interface INativeValueBox { int NativeValue { get; set; } }

internal sealed class FakeField
{
    internal IntPtr Handle;
    internal FakeClass Owner;
    internal string Name;
    internal bool Static;
    internal IntPtr TypeHandle;
}

internal sealed class FakeClass
{
    internal IntPtr Pointer;
    internal string Name;
    internal bool ValueType;
    internal int Size;
    internal readonly Dictionary<string, FakeField> Fields = new(StringComparer.Ordinal);
}

internal static class NativeFakeHeap
{
    internal const int FieldStaticFlag = 0x0010;

    private static long _next = 0x5A00_0000;
    private static readonly Dictionary<IntPtr, FakeClass> Classes = new();
    private static readonly Dictionary<IntPtr, FakeField> Fields = new();
    private static readonly Dictionary<IntPtr, IntPtr> TypeClasses = new();
    private static readonly Dictionary<IntPtr, object> Boxes = new();
    private static readonly Dictionary<IntPtr, FakeClass> BoxClasses = new();

    // Instrumentation: native copies served, and type conversions (with the zero-handle subset a
    // fail-closed guard must never reach). Guard failures must not increment these.
    internal static int FieldGetValueCalls;
    internal static int ClassFromTypeCalls;
    internal static int ClassFromTypeZeroCalls;

    // Canonical 2.4 shapes: the closed Return<SaveLoadResult> value class (native size 4) with its
    // `value` field (non-static, native size 4, typed as the SaveLoadResult enum class).
    internal static readonly FakeClass ReturnClass;
    internal static readonly FakeClass EnumClass;

    // Guard variants: legitimately registered shapes the tests point the stores/boxes at to prove
    // each metadata guard fires without a native copy.
    internal static readonly FakeClass OtherReturnClass;          // another closed Return<...>
    internal static readonly FakeClass MissingValueFieldClass;    // no `value` field
    internal static readonly FakeClass StaticValueFieldClass;     // `value` field is static
    internal static readonly FakeClass WrongFieldTypeClass;       // `value` field typed as WrongEnum
    internal static readonly FakeClass MissingFieldTypeClass;     // `value` field with no type handle
    internal static readonly FakeClass WideReturnClass;           // Return native size 8
    internal static readonly FakeClass WideEnumFieldClass;        // `value` typed as WideEnum
    internal static readonly FakeClass WideEnumClass;             // enum-like native size 8
    internal static readonly FakeClass RefReturnClass;            // not a value type

    static NativeFakeHeap()
    {
        EnumClass = Class("Coatsink.Common.SaveLoadResult", valueType: true, size: 4);
        ReturnClass = Class("Coatsink.Common.Routine/Return<Coatsink.Common.SaveLoadResult>", true, 4);
        Field(ReturnClass, "value", isStatic: false, type: EnumClass);

        OtherReturnClass = Class("Coatsink.Common.Routine/Return<System.Int32>", true, 4);
        Field(OtherReturnClass, "value", false, Class("System.Int32", true, 4));
        MissingValueFieldClass = Class("Coatsink.Common.Routine/Return<NoValue>", true, 4);
        StaticValueFieldClass = Class("Coatsink.Common.Routine/Return<StaticValue>", true, 4);
        Field(StaticValueFieldClass, "value", true, EnumClass);
        WrongFieldTypeClass = Class("Coatsink.Common.Routine/Return<WrongValue>", true, 4);
        Field(WrongFieldTypeClass, "value", false, Class("Coatsink.Common.WrongEnum", true, 4));
        MissingFieldTypeClass = Class("Coatsink.Common.Routine/Return<MissingFieldType>", true, 4);
        FieldWithoutType(MissingFieldTypeClass, "value", false);
        WideEnumClass = Class("Coatsink.Common.WideEnum", true, 8);
        WideEnumFieldClass = Class("Coatsink.Common.Routine/Return<WideValue>", true, 4);
        Field(WideEnumFieldClass, "value", false, WideEnumClass);
        WideReturnClass = Class("Coatsink.Common.Routine/Return<Wide>", true, 8);
        Field(WideReturnClass, "value", false, EnumClass);
        RefReturnClass = Class("Coatsink.Common.Routine/Return<Ref>", false, 4);
        Field(RefReturnClass, "value", false, EnumClass);
    }

    internal static FakeClass ClassFor<T>()
    {
        if (typeof(T) == typeof(Coatsink.Common.Routine.Return<Coatsink.Common.SaveLoadResult>)) return ReturnClass;
        if (typeof(T) == typeof(Coatsink.Common.SaveLoadResult)) return EnumClass;
        return Class("Coatsink.Common.Routine/Return<" + typeof(T).Name + ">", true, 4);
    }

    internal static IntPtr RegisterBox(object box, FakeClass cls)
    {
        IntPtr pointer = NextHandle();
        Boxes[pointer] = box;
        BoxClasses[pointer] = cls;
        return pointer;
    }

    internal static void SetBoxClass(object box, FakeClass cls)
    {
        foreach (var entry in Boxes)
            if (ReferenceEquals(entry.Value, box)) { BoxClasses[entry.Key] = cls; return; }
        throw new InvalidOperationException("fake: box not registered");
    }

    // Test oracle for the fake native field storage; production cannot influence this directly.
    internal static int StoredValue(object box)
        => box is INativeValueBox valueBox ? valueBox.NativeValue : throw new InvalidOperationException("fake: box has no storage");

    internal static void ResetCounters()
    {
        FieldGetValueCalls = 0;
        ClassFromTypeCalls = 0;
        ClassFromTypeZeroCalls = 0;
    }

    // Snapshot/restore both class stores so a variant case cannot leak into the next.
    internal static IDisposable PinStores() => new StorePin();

    private sealed class StorePin : IDisposable
    {
        private readonly IntPtr _return = Il2CppInterop.Runtime.Il2CppClassPointerStore<Coatsink.Common.Routine.Return<Coatsink.Common.SaveLoadResult>>.NativeClassPtr;
        private readonly IntPtr _enum = Il2CppInterop.Runtime.Il2CppClassPointerStore<Coatsink.Common.SaveLoadResult>.NativeClassPtr;

        public void Dispose()
        {
            Il2CppInterop.Runtime.Il2CppClassPointerStore<Coatsink.Common.Routine.Return<Coatsink.Common.SaveLoadResult>>.NativeClassPtr = _return;
            Il2CppInterop.Runtime.Il2CppClassPointerStore<Coatsink.Common.SaveLoadResult>.NativeClassPtr = _enum;
        }
    }

    // ---------------------------------------------------------------- doubled bridge

    internal static IntPtr ClassOf(IntPtr obj)
        => obj != IntPtr.Zero && BoxClasses.TryGetValue(obj, out FakeClass cls) ? cls.Pointer : IntPtr.Zero;

    internal static IntPtr FieldOf(IntPtr klass, string name)
        => Classes.TryGetValue(klass, out FakeClass cls) && cls.Fields.TryGetValue(name, out FakeField field) ? field.Handle : IntPtr.Zero;

    internal static int FieldFlags(IntPtr field)
        => Fields.TryGetValue(field, out FakeField fieldInfo) ? (fieldInfo.Static ? FieldStaticFlag : 0) : throw new InvalidOperationException("fake: field not registered");

    internal static IntPtr FieldTypeOf(IntPtr field)
        => Fields.TryGetValue(field, out FakeField fieldInfo) ? fieldInfo.TypeHandle : IntPtr.Zero;

    internal static IntPtr ClassFromType(IntPtr type)
    {
        ClassFromTypeCalls++;
        if (type == IntPtr.Zero) ClassFromTypeZeroCalls++;
        return TypeClasses.TryGetValue(type, out IntPtr cls) ? cls : IntPtr.Zero;
    }

    internal static bool IsValueType(IntPtr klass)
        => Classes.TryGetValue(klass, out FakeClass cls) ? cls.ValueType : throw new InvalidOperationException("fake: class not registered");

    internal static int ValueSize(IntPtr klass)
        => Classes.TryGetValue(klass, out FakeClass cls) ? cls.Size : throw new InvalidOperationException("fake: class not registered");

    internal static unsafe void ReadField(IntPtr obj, IntPtr field, void* value)
    {
        FieldGetValueCalls++;
        if (!Boxes.TryGetValue(obj, out object box)) throw new InvalidOperationException("fake: object not registered");
        if (!Fields.TryGetValue(field, out FakeField fieldInfo)) throw new InvalidOperationException("fake: field not registered");
        if (!BoxClasses.TryGetValue(obj, out FakeClass cls) || !ReferenceEquals(cls, fieldInfo.Owner))
            throw new InvalidOperationException("fake: field does not belong to the object class");
        *(int*)value = ((INativeValueBox)box).NativeValue;
    }

    private static FakeClass Class(string name, bool valueType, int size)
    {
        var cls = new FakeClass { Pointer = NextHandle(), Name = name, ValueType = valueType, Size = size };
        Classes[cls.Pointer] = cls;
        return cls;
    }

    // A field whose type handle stayed unresolved (il2cpp_field_get_type returns zero): the reader
    // must fail closed before any native conversion.
    private static void FieldWithoutType(FakeClass owner, string name, bool isStatic)
    {
        var field = new FakeField { Handle = NextHandle(), Owner = owner, Name = name, Static = isStatic, TypeHandle = IntPtr.Zero };
        Fields[field.Handle] = field;
        owner.Fields[name] = field;
    }

    private static void Field(FakeClass owner, string name, bool isStatic, FakeClass type)
    {
        var field = new FakeField { Handle = NextHandle(), Owner = owner, Name = name, Static = isStatic, TypeHandle = NextHandle() };
        Fields[field.Handle] = field;
        TypeClasses[field.TypeHandle] = type.Pointer;
        owner.Fields[name] = field;
    }

    private static IntPtr NextHandle() => (IntPtr)(++_next);
}
