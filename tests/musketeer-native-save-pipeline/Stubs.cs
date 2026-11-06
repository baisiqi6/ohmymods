using System;
using System.Collections.Generic;

namespace BepInEx { public static class Paths { public static string ConfigPath; } }

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type t) { TargetType = t; }
        public HarmonyPatch(Type t, string n) { TargetType = t; TargetMethod = n; }
        public HarmonyPatch(Type t, string n, Type[] p) { TargetType = t; TargetMethod = n; }
        // Stored so the identity regression can pin which native boundaries are detoured.
        public Type TargetType; public string TargetMethod;
    }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyFinalizer : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPriority : Attribute { public HarmonyPriority(int i) { } }
    public static class Priority { public const int First = 800, Last = 0; }
}

namespace UnityEngine
{
    public class Object { static long counter; public IntPtr Pointer = (IntPtr)(++counter); }
    public class GameObject : Object
    {
        static int nextId;
        public int InstanceId = ++nextId;
        public int GetInstanceID() => InstanceId;
        private bool active = true; public bool ThrowOnActiveRead;
        public bool activeInHierarchy { get { if (ThrowOnActiveRead) throw new InvalidOperationException("active read"); return active; } set => active = value; }
        public string tag = "";
        public Scene scene; public Transform transform = new();
        readonly Dictionary<Type, Component> components = new();
        public T Add<T>(T c) where T : Component { c.gameObject = this; components[typeof(T)] = c; return c; }
        public T GetComponent<T>() where T : Component => components.TryGetValue(typeof(T), out var c) ? (T)c : null;
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : Component => gameObject?.GetComponent<T>();
    }
    public struct Scene { public int handle; }
    public class Transform : Component { public float x, y; public bool IsChildOf(Transform layer) => KingdomEnhancedMod.MusketeerAccess.InWorldResult; }
    public struct Vector2
    {
        public float x, y;
        public static Vector2 zero => new Vector2();
    }
    public struct Vector3 { }
    public struct Quaternion { }
    public class Rigidbody2D : Component { public bool isKinematic; public Vector2 velocity; }
    public static class Time { public static float time = 100, unscaledTime = 100; public static int frameCount = 1; }
    public static class JsonUtility { public static string ToJson(IslandSaveData island, bool pretty) => island.Json; }
}

public interface IUnitController { }
public enum PickUpPolicy { Anybody = 0, Nobody = 1, OnlyClaimer = 2, AnybodyExceptDropper = 3, AnyPlayer = 4, Blocked = 5 }
public class Damageable { public bool isDead; }
public class Character : UnityEngine.Component
{
    public Damageable _damageable = new();
    public Character Promote(DroppableTool tool, IUnitController unitController) => this;
}
public class Embarkee : UnityEngine.Component
{
    private bool _isEmbarked;
    public bool IsEmbarked { get => _isEmbarked; set => _isEmbarked = value; }
    // Native owner reference double: the interop surface exposes TryCast<T> (HeroRecruitment form).
    public UnitOwnerRef _owner;
    // Native boundary shapes the Harmony attributes name (nameof); doubles stay no-op.
    public void SetEmbarkableTarget(Embarkable target, int slot) { }
    public void Embark() { }
}
public class Embarkable : UnityEngine.Component { }
public sealed class UnitOwnerRef
{
    private readonly UnityEngine.Component _value;
    public UnitOwnerRef(UnityEngine.Component value) { _value = value; }
    public T TryCast<T>() where T : UnityEngine.Component => _value as T;
}
public class Archer : UnityEngine.Component
{
    public Embarkee _embarkee;
    // Native CanEmbark getter double: the policy postfix only ever tightens an actor's own value.
    public bool NativeCanEmbark = true;
    public bool CanEmbark => NativeCanEmbark;
}
public class Droppable : UnityEngine.Component { public bool pickedUp; public UnityEngine.GameObject enemyClaimer; public UnityEngine.GameObject dropper; }
public class DroppableTool : Droppable { }
public class Persistent : UnityEngine.Component { public string path; public bool ShouldPersist() => false; }
public class Pool
{
    public UnityEngine.GameObject FastSpawn(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation, UnityEngine.Transform parent, short netID, bool syncReceipt) => null;
    public void FastDespawn(UnityEngine.GameObject clone, float delay, bool ignoreWarnings) { }
}
public class World { public UnityEngine.Transform gameLayer; }
public class Managers { public static Managers Inst; public World world; }
public class GlobalSaveData : UnityEngine.Object
{
    public static GlobalSaveData loaded;
    public static string filename = "global-v35";
    public int currentCampaign, currentChallenge;
    public PrefsSaveData prefs = new();
    public Il2CppSystem.Collections.Generic.List<CampaignSaveData> campaigns = new();
    public Il2CppSystem.Collections.Generic.List<CampaignSaveData> challenges = new();
    public CampaignSaveData CurrentCampaign;
    public CampaignSaveData GetCurrentCampaign() => CurrentCampaign;
    public void SaveAsync(Il2CppSystem.Action<Coatsink.Common.SaveLoadResult> cb) { }
}
public class CampaignSaveData : UnityEngine.Object
{
    public static CampaignSaveData current;
    public int challengeId;
    public int CurrentLand;
    public IslandSaveData CurrentIsland;
    public Il2CppSystem.Collections.Generic.List<IslandSaveData> _islands = new();
    public void ApplyToScene() { }
}
public class IslandSaveData : UnityEngine.Object
{
    public static IslandSaveData CurrentlySavingIsland; public static bool isSavingGame;
    public int land; public string Json = "{}"; public bool isNew = true; public double playTimeDays;
    public List<ObjectData> objects = new();
    public static void Save(int campaign, int land, int challenge) { }
    public static string GetID(Persistent persistent) => "";
    public bool TryPopObjectsToScene() => true;
    public static Persistent TryCreateOrFind(ObjectData data) => null;
    public class ObjectData : UnityEngine.Object { public string uniqueID; }
}

namespace KingdomEnhancedMod
{
    internal static class MusketeerDefense
    {
        internal static bool RedistributeAfterBindings() => true;
    }

    internal static class MusketeerAccess
    {
        internal static bool TrackAllowed = true;
        private static bool enabled = true, playing = true;
        internal static bool Enabled { get => TrackAllowed && enabled; set => enabled = value; }
        internal static bool Playing { get => Enabled && playing; set => playing = value; }
        internal static bool InWorldResult = true;
        internal static UnityEngine.Transform World;
        internal static bool InWorld(UnityEngine.GameObject root) => InWorldResult && root != null;
        internal static bool InWorld(UnityEngine.Component component) => InWorldResult && component != null && component.gameObject != null;
    }

    /// <summary>
    /// 边界 double：真实 TryPrepareBoardingDemotion 由 B worker 的 il2cpp/MusketeerRuntime.cs 提供
    /// （本套件不链接该文件）。这里只证明 BoardingPolicy 的调用边界与按返回值行动（计数/故障注入），
    /// 绝不能宣称真实字段/视觉归还已在本套件通过。
    /// </summary>
    internal static class MusketeerRuntime
    {
        internal static int PrepareCalls;
        internal static Archer LastActor;
        internal static bool NextResult = true;
        internal static bool ThrowOnPrepare;

        internal static bool TryPrepareBoardingDemotion(Archer actor)
        {
            PrepareCalls++;
            LastActor = actor;
            if (ThrowOnPrepare) throw new InvalidOperationException("prepare fault");
            return NextResult;
        }

        internal static void ResetBoardingDouble()
        {
            PrepareCalls = 0; LastActor = null; NextResult = true; ThrowOnPrepare = false;
        }
    }

    internal class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance = new();
        internal Logger LogSource = new();
    }

    /// <summary>捕获日志行供断言（生产侧只调用 LogInfo / LogWarning / LogError）。</summary>
    internal class Logger
    {
        internal static readonly List<string> Lines = new();

        internal void LogInfo(string message) { Lines.Add("I: " + message); }
        internal void LogWarning(string message) { Lines.Add("W: " + message); }
        internal void LogError(string message) { Lines.Add("E: " + message); }
    }

    /// <summary>
    /// 仅测试替身：MusketeerPersistence 现役 ApplyToScene postfix 末尾调用本模块确认入口；
    /// 与生产 `CoinCourierPersistence.EnsureBoundFromApplyToScene(CampaignSaveData)` 同签名 no-op。
    /// 待本工程改为链接真实持久化生产文件时同步删除（防双源漂移）；旧断言不受影响。
    /// </summary>
    internal static class CoinCourierPersistence
    {
        internal static void EnsureBoundFromApplyToScene(CampaignSaveData applied) { }
    }

    /// <summary>
    /// 中性 disabled 边界替身：本套件覆盖火枪手身份/存档接线，不覆盖共享银行账本算法
    /// （真实 SharedBankNative/R3 的 capture/save-gate 语义由 tests/coin-courier-economy 与
    /// tests/shared-bank-regressions 直接链接生产源验证）。这里只让 MusketeerPersistence 新增的
    /// 银行观察/装载接线保持同签名 no-op；绝不能被当作银行行为已在本套件被验证。
    /// Pop 令牌只把 BeginPop/EndPop 配对，不承载任何账本状态。
    /// </summary>
    internal static class SharedBankNative
    {
        internal sealed class Pop { }

        private static Pop _current;

        internal static Pop CurrentPop => _current;

        internal static void ObserveId(Persistent root, string id) { }

        internal static Pop BeginPop(IslandSaveData island) => _current = new Pop();

        internal static void EndPop(Pop pop, bool normal)
        {
            if (ReferenceEquals(_current, pop)) _current = null;
        }

        internal static void Created(IslandSaveData.ObjectData row, Persistent root) { }

        internal static void SceneApplied() { }
    }
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


public class PrefsSaveData : UnityEngine.Object { public Il2CppSystem.Collections.Generic.Dictionary<string, string> contents = new(); }

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
