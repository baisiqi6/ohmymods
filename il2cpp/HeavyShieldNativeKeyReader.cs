using System;
using System.Reflection;
using Il2CppInterop.Runtime;

namespace KingdomEnhancedMod;

// Issue #206 盾卫每帧分配（phase4 批准 + native-key review 修复项）：
// 固定 HeavyShieldSaveSchema.Key 的 fresh-native 字符串比较，替换原 managed indexer 终句
// `prefs.contents[HeavyShieldSaveSchema.Key] == owner.ExpectedRaw`（每帧 new managed string）。
//
// 语义与不变式：
//  - 只支持固定 key（HeavyShieldSaveSchema.Key）：从闭合 Dictionary<System.String,System.String> 类型
//    精确私有字段读取当前 cctor 已产生的 get_Item native MethodInfo（token 100676033），再无 GetVirtualMethod、
//    无裸函数地址、无 offset/布局猜测。
//  - 恒定 key 转一次 native string 并持 pinned 强 handle（ownership 见 ReleaseNativeResources）；每次调用
//    fresh 取 handle target、fresh direct IL2CPP.il2cpp_runtime_invoke（同 generated getter body ABI）。
//  - 结果对象当次临时 pinned handle，finally 释放；真实 dict wrapper 由调用方 KeepAlive 贯穿。
//  - String 身份以 il2cpp_object_get_class 与恒定 key 自身 class 比对证明（不读任意 ptr 字符），
//    再 string_length 核 expected.Length，最后 UTF16 ordinal span 比较；不 new string/char[]/Marshal。
//  - 业务失败（null/exc/类型/坏长/handle 失效）一律 false：fail closed，不重试、不回退 managed。
//  - 失败分型（review 项 1）：仅能力缺失（MIP 字段/token/export 不可用）一次锁存 → 原 managed getter 语义；
//    native string/handle/读取/资源等执行失败**不**锁存回退，当次 false，下一次 fresh 调用可重试且不获新权限。
//  - 线程（review 项 4）：root 在插件 Load 前调用 BindMainThread()；比较/释放只在该绑定线程执行，其他线程
//    false/no-op，不触碰 state、不读 native、不回退。
//  - 释放（review 项 3）：只有 free 确认成功才清 owned 状态并允许重新 initialize；free 失败保留 ownership、
//    置 Faulted、禁止再创建/再 free 未知句柄，不宣称已释放。
//  - 不缓存比较结果（含 true）、不写存档/字典/权益/钱包、不改变调用频率、不新 Native/Harmony/Dispose hook。
//  - 单次比较正常路径 0 managed allocation（KEM_PERF_DIAG 有界影子对照除外）。
internal enum NativeKeyDecision
{
    Match = 0,
    NoMatch = 1,
    NeedsManagedFallback = 2,
}

// 能力获取结论：Ready=可用；Unsupported=能力缺失（字段/token/export，调用方可锁存回退）；Failed=执行/资源失败（不锁存）。
internal enum NativeKeyAcquire
{
    Ready = 0,
    Unsupported = 1,
    Failed = 2,
}

// native 边界：只声明读取 native Dictionary<string, string> get_Item 结果所需的最小原语。
// 纯测试（KEM_NATIVE_KEY_PURE_TEST）只替换本接口实现；上层策略/所有权/比较全部走真实源码。
// 实现契约：
//  - AcquireFixedKey 原子操作：非 Ready 时不得留下 owned handle；只有确切能力缺失才返回 Unsupported。
//  - TryReleaseHandle：true=已确认释放；false=释放未确认（调用方须保留 ownership 责任，不得再创建、不得重复 free）。
internal interface INativeKeyAdapter
{
    bool Faulted { get; }
    int PendingCount { get; }
    NativeKeyAcquire AcquireFixedKey(string key, out IntPtr keyHandle);
    IntPtr ResolveHandle(IntPtr handle);
    bool TryReleaseHandle(IntPtr handle);
    IntPtr RetainObject(IntPtr objectPtr);
    bool TryInvokeGetter(IntPtr dictionaryPtr, IntPtr keyPtr, out IntPtr resultPtr, out IntPtr excPtr);
    bool TryReadStringObject(IntPtr objectPtr, out int length, out IntPtr chars);
}

internal static class HeavyShieldNativeKeyReader
{
    // 闭合 Dictionary<string, string>.get_Item 的实际 metadata token（当前 runtime 反编译证据）。
    internal const int GetItemMethodInfoToken = 100676033;
    // KEM_PERF_DIAG 影子对照次数上限：有界、一次性，之后不再读 managed getter。
    internal const int ShadowBudget = 3;

    private const int Uninitialized = 0;
    private const int Ready = 1;
    private const int Unsupported = 2;
    private const int Faulted = 3;

#if KEM_NATIVE_KEY_PURE_TEST
    // 纯测试注入点：真实 adapter 由同一 define 排除，不执行任何 native。
    internal static INativeKeyAdapter Adapter;
    // 纯测试影子/回退读 seam（生产路径走真实 managed indexer）。
    internal static Func<Il2CppSystem.Collections.Generic.Dictionary<string, string>, string> ManagedReadForTest;
    // 纯测试诊断 env seam（验证 env 读取故障不影响能力/owner 状态）。
    internal static Func<string, string> EnvForTest;
#else
    private static readonly INativeKeyAdapter Adapter = new Il2CppNativeKeyAdapter();
#endif

    private static int _mainThreadId;
    private static int _state;
    private static IntPtr _fixedKeyHandle;
    private static int _shadowRemaining;
    private static int _shadowRuns;
    private static int _shadowMismatches;
    private static int _shadowFaults;

    // 供 root/Native gate 有界观测（无 payload）。
    internal static bool UnsupportedLatched => _state == Unsupported;
    internal static bool FixedKeyHandleOwned => _fixedKeyHandle != IntPtr.Zero;
    internal static bool ShadowOpen => _shadowRemaining > 0;
    internal static bool NativeFaulted => _state == Faulted || (Adapter != null && Adapter.Faulted);
    internal static int ShadowFailures => _shadowMismatches + _shadowFaults;
    internal static int NativePendingCount => Adapter == null ? 0 : Adapter.PendingCount;
    internal static bool MainThreadBound => _mainThreadId != 0;

    /// root 在 KingdomEnhancedPlugin.Load（PatchAll 之前，与 Unity GameObject 创建同线程）调用一次；
    /// 只首次记录 CurrentManagedThreadId。此后比较/释放必须在该线程，其他线程一律 false/no-op。
    internal static void BindMainThread()
    {
        if (_mainThreadId == 0) _mainThreadId = Environment.CurrentManagedThreadId;
    }

    /// 原 IsNativeKeyCurrent 终句替换点：owner/global/prefs/generation/ContainsKey 守卫之后调用。
    /// 语义 == `contents[HeavyShieldSaveSchema.Key] == expectedRaw`，但正常路径不物化 managed string。
    internal static bool EqualsCurrent(
        Il2CppSystem.Collections.Generic.Dictionary<string, string> contents, string expectedRaw)
    {
        if (!OnBoundThread()) return false; // 其他线程 false：不触碰 state、不读 native、不回退
        if (contents == null || expectedRaw == null) return false;
        try
        {
            var decision = Evaluate(contents.Pointer, expectedRaw);
            bool result = decision == NativeKeyDecision.Match
                || (decision == NativeKeyDecision.NeedsManagedFallback && ManagedFallback(contents, expectedRaw));
            // review 项 5：影子 mismatch/fault 当次必须 false，不得仍返回 native true。
            return ShadowConfirmedResult(result, ObserveShadow(contents, expectedRaw, decision, result));
        }
        catch { return false; }
        // 字典 wrapper 存活贯穿 native invoke 与当次结果读取。
        finally { GC.KeepAlive(contents); }
    }

    /// fresh 比较核心：不可证明的读取一律 NoMatch；只有能力缺失才 NeedsManagedFallback。
    internal static unsafe NativeKeyDecision Evaluate(IntPtr contentsPtr, string expectedRaw)
    {
        if (!OnBoundThread()) return NativeKeyDecision.NoMatch;
        if (contentsPtr == IntPtr.Zero || expectedRaw == null) return NativeKeyDecision.NoMatch;
        if (_state == Uninitialized) TryInitialize();
        if (_state != Ready) return _state == Unsupported
            ? NativeKeyDecision.NeedsManagedFallback
            : NativeKeyDecision.NoMatch; // Faulted/未就绪：fail closed，不回退
        if (Adapter.Faulted) return NativeKeyDecision.NoMatch; // 释放未确认：禁止再创建

        IntPtr keyPtr = Adapter.ResolveHandle(_fixedKeyHandle);
        if (keyPtr == IntPtr.Zero) return NativeKeyDecision.NoMatch;

        if (!Adapter.TryInvokeGetter(contentsPtr, keyPtr, out IntPtr resultPtr, out IntPtr excPtr)
            || excPtr != IntPtr.Zero || resultPtr == IntPtr.Zero)
            return NativeKeyDecision.NoMatch; // invoke 异常 / 真实存 null：false，不重试、不回退

        IntPtr resultHandle = Adapter.RetainObject(resultPtr);
        if (resultHandle == IntPtr.Zero) return NativeKeyDecision.NoMatch;
        NativeKeyDecision decision;
        bool released;
        try
        {
            decision = ReadCurrent(resultHandle, expectedRaw);
        }
        finally
        {
            released = Adapter.TryReleaseHandle(resultHandle); // 当次临时 handle；失败=未确认释放
        }
        // 释放未确认：当次 false，adapter 已 Faulted（后续不再创建/不再宣称释放）。
        return released ? decision : NativeKeyDecision.NoMatch;
    }

    /// 纯 UTF16 ordinal 比较：先按 expected 长度核 native length，再 span SequenceEqual。
    /// 允许内嵌 NUL、代理对与孤立代理（按 code unit 比较，不转换）；chars 不可用/坏长一律 false。
    internal static unsafe bool Utf16Equals(int nativeLength, char* nativeChars, ReadOnlySpan<char> expected)
    {
        if (nativeChars == null || nativeLength < 0 || nativeLength != expected.Length) return false;
        if (nativeLength == 0) return true;
        return expected.SequenceEqual(new ReadOnlySpan<char>(nativeChars, nativeLength));
    }

    /// 影子一致性（review 项 5 的判定核心）：native 结果必须与现 managed getter 结果一致（ordinal 相等即长度一致）。
    internal static bool ShadowConsistent(bool nativeResult, string expectedRaw, string managedValue)
    {
        bool managedMatch = managedValue != null && string.Equals(managedValue, expectedRaw, StringComparison.Ordinal);
        return managedMatch == nativeResult;
    }

    /// 影子未通过（mismatch/fault）时当次结果必须为 false。
    internal static bool ShadowConfirmedResult(bool result, bool shadowFailed) => result && !shadowFailed;

    /// root 在现 ModPanel.OnDestroy 调用一次（禁止新增 Native/Dispose hook）；仅绑定线程有效。
    /// 只有 free 确认成功才清 owned 状态并允许重新 initialize；free 未确认则保留 ownership 并 Faulted，
    /// 不重复 free 未知句柄、不宣称已释放。正常/重复 cleanup 幂等。
    internal static void ReleaseNativeResources()
    {
        if (!OnBoundThread() || _state != Ready) return;
        IntPtr handle = _fixedKeyHandle;
        if (handle == IntPtr.Zero) { _state = Uninitialized; return; }
        if (Adapter.TryReleaseHandle(handle))
        {
            _fixedKeyHandle = IntPtr.Zero;
            _state = Uninitialized;
            return;
        }
        _state = Faulted; // 保留 _fixedKeyHandle ownership；不再尝试
    }

#if KEM_NATIVE_KEY_PURE_TEST
    // 纯测试专用重置（生产构建不存在此 API）：注入 adapter、绑定指定线程并清空锁存/handle/诊断计数。
    internal static void ResetForTest(INativeKeyAdapter adapter, bool bindCurrentThread = true)
    {
        Adapter = adapter;
        ManagedReadForTest = null;
        EnvForTest = null;
        _mainThreadId = bindCurrentThread ? Environment.CurrentManagedThreadId : 0;
        _state = Uninitialized;
        _fixedKeyHandle = IntPtr.Zero;
        _shadowRemaining = 0;
        _shadowRuns = 0;
        _shadowMismatches = 0;
        _shadowFaults = 0;
    }

    // 纯测试：以给定 managed 读值走一次影子记账/判罚（生产路径由 EqualsCurrent 内部调用）。
    internal static bool ShadowObserveForTest(
        Il2CppSystem.Collections.Generic.Dictionary<string, string> contents, string expectedRaw,
        NativeKeyDecision decision, bool result)
        => ObserveShadow(contents, expectedRaw, decision, result);
#endif

    /// 有界观测行（无 payload，root 只打印一次）；不含 key/存档内容。
    internal static string PerfDiagLine()
    {
        string mode = _state == Ready ? "native"
            : _state == Unsupported ? "unsupported"
            : _state == Faulted ? "faulted" : "uninit";
        return "native-key " + mode
            + " bound=" + (_mainThreadId != 0 ? 1 : 0)
            + " shadow=" + _shadowRuns + "/" + ShadowBudget
            + " open=" + (_shadowRemaining > 0 ? 1 : 0)
            + " mismatch=" + _shadowMismatches + " fault=" + _shadowFaults
            + " failure=" + ShadowFailures
            + " adapterFault=" + (Adapter != null && Adapter.Faulted ? 1 : 0)
            + " pending=" + NativePendingCount
            + " keyOwned=" + (FixedKeyHandleOwned ? 1 : 0);
    }

    private static bool OnBoundThread() => _mainThreadId != 0 && _mainThreadId == Environment.CurrentManagedThreadId;

    private static unsafe NativeKeyDecision ReadCurrent(IntPtr resultHandle, string expectedRaw)
    {
        IntPtr fresh = Adapter.ResolveHandle(resultHandle);
        if (fresh == IntPtr.Zero) return NativeKeyDecision.NoMatch;
        if (!Adapter.TryReadStringObject(fresh, out int length, out IntPtr chars))
            return NativeKeyDecision.NoMatch;
        return Utf16Equals(length, (char*)chars, expectedRaw.AsSpan())
            ? NativeKeyDecision.Match
            : NativeKeyDecision.NoMatch;
    }

    // 初始化结论：能力缺失→Unsupported 锁存；执行/资源失败→保持 Uninitialized（下一次 fresh 调用可重试）。
    // 诊断 env 读取不参与能力/owner 状态判定。
    private static void TryInitialize()
    {
        NativeKeyAcquire acquire = NativeKeyAcquire.Failed;
        try
        {
            acquire = Adapter.AcquireFixedKey(HeavyShieldSaveSchema.Key, out IntPtr handle);
            if (acquire == NativeKeyAcquire.Ready && handle != IntPtr.Zero)
            {
                _fixedKeyHandle = handle; // ownership：本类持有，ReleaseNativeResources 释放
                _state = Ready;
                if (RequestShadowDiag()) _shadowRemaining = ShadowBudget; // 诊断仅影响影子预算
                return;
            }
        }
        catch { /* 执行异常不锁存回退 */ }

        // ownership 已提交则任何异常都不得回滚 Ready（env/诊断读取绝不改变已 Ready/owned 状态）。
        if (_fixedKeyHandle != IntPtr.Zero) { _state = Ready; return; }

        // 半成功/失败资源由 adapter 原子清理；本类不盲释放非 own handle。
        // 释放未确认（Faulted）优先；否则仅能力缺失锁存 Unsupported，执行失败保持 Uninitialized 可重试。
        if (Adapter.Faulted) _state = Faulted;
        else _state = acquire == NativeKeyAcquire.Unsupported ? Unsupported : Uninitialized;
    }

    // 诊断开关读取与能力/owner 状态无关：自身异常只关闭/跳过影子对照，不回滚 already-Ready/owned。
    private static bool RequestShadowDiag()
    {
        try
        {
#if KEM_NATIVE_KEY_PURE_TEST
            if (EnvForTest != null) return EnvForTest("KEM_PERF_DIAG") == "1";
#endif
            return string.Equals(Environment.GetEnvironmentVariable("KEM_PERF_DIAG"), "1", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    // 仅能力 unsupported 时进入：保持原 managed getter 读取/ordinal equality 语义（含原异常行为）。
    private static bool ManagedFallback(
        Il2CppSystem.Collections.Generic.Dictionary<string, string> contents, string expectedRaw)
        => ReadManaged(contents) == expectedRaw;

    private static string ReadManaged(Il2CppSystem.Collections.Generic.Dictionary<string, string> contents)
    {
#if KEM_NATIVE_KEY_PURE_TEST
        if (ManagedReadForTest != null) return ManagedReadForTest(contents);
#endif
        return contents[HeavyShieldSaveSchema.Key];
    }

    // KEM_PERF_DIAG=1：最初 <= ShadowBudget 次与现 managed getter 做影子对照（结果+长度一致性）。
    // 返回 true 表示当次影子未通过（mismatch/fault），调用方必须让该次比较 false；
    // 有界会计：到预算即关闭，之后不再读 managed getter。
    // 该分支会物化 managed string：稳态分配窗口应在 ShadowOpen=false（前 <=3 次影子帧之后）取样，
    // 不必关闭整个 PerfDiag（timer 继续有效）。
    private static bool ObserveShadow(
        Il2CppSystem.Collections.Generic.Dictionary<string, string> contents, string expectedRaw,
        NativeKeyDecision decision, bool result)
    {
        if (_shadowRemaining <= 0 || decision == NativeKeyDecision.NeedsManagedFallback) return false;
        _shadowRemaining--;
        _shadowRuns++;
        try
        {
            if (ShadowConsistent(result, expectedRaw, ReadManaged(contents))) return false;
            _shadowMismatches++;
            return true;
        }
        catch
        {
            _shadowFaults++;
            return true;
        }
    }
}

#if !KEM_NATIVE_KEY_PURE_TEST
// 真实 IL2CPP 边界：仅使用当前 Runtime wrapper（gchandle nint / string_length int / string_chars char*），
// 不猜旧 uint、不硬编码 offset/函数地址、不额外 GetVirtualMethod。
internal sealed class Il2CppNativeKeyAdapter : INativeKeyAdapter
{
    // 闭合 Dictionary<string, string> 的 get_Item MIP 私有字段名（当前 runtime 反编译 exact 名）。
    private const string GetItemMethodInfoField =
        "NativeMethodInfoPtr_get_Item_Public_Virtual_Final_New_get_TValue_TKey_0";

    private IntPtr _getItemMethodInfo;
    private IntPtr _stringClass;
    private IntPtr _pending0; // bounded（≤2）未确认释放句柄；Faulted 后不再创建新句柄
    private IntPtr _pending1;
    private int _pendingCount;

    public bool Faulted { get; private set; }
    public int PendingCount => _pendingCount;

    public unsafe NativeKeyAcquire AcquireFixedKey(string key, out IntPtr keyHandle)
    {
        keyHandle = IntPtr.Zero;
        if (Faulted || key == null) return NativeKeyAcquire.Failed;
        if (_getItemMethodInfo == IntPtr.Zero && !TryResolveCapability()) return NativeKeyAcquire.Unsupported;

        IntPtr handle = IntPtr.Zero;
        try
        {
            IntPtr native = IL2CPP.ManagedStringToIl2Cpp(key);
            if (native == IntPtr.Zero) return NativeKeyAcquire.Failed;
            IntPtr keyClass = IL2CPP.il2cpp_object_get_class(native);
            if (keyClass == IntPtr.Zero) return NativeKeyAcquire.Failed;
            handle = IL2CPP.il2cpp_gchandle_new(native, true); // pinned 强 handle：恒定 key 生命周期由它保证
            if (handle == IntPtr.Zero) return NativeKeyAcquire.Failed;

            // 预检 string 读取 API：constant key 自身 target 的 class/length/chars 必须可读且长度正确。
            NativeKeyAcquire preflight = Preflight(handle, key, keyClass);
            if (preflight != NativeKeyAcquire.Ready)
            {
                TryReleaseHandle(handle);
                handle = IntPtr.Zero;
                return preflight;
            }
            _stringClass = keyClass; // key 自身 class 即本 runtime 的 System.String class（结果对象按此证明）
            keyHandle = handle;      // ownership 移交调用方
            handle = IntPtr.Zero;
            return NativeKeyAcquire.Ready;
        }
        catch (Exception ex)
        {
            return IsCapabilityException(ex) ? NativeKeyAcquire.Unsupported : NativeKeyAcquire.Failed;
        }
        finally
        {
            if (handle != IntPtr.Zero) TryReleaseHandle(handle); // 半成功不留 silently 丢失的句柄
        }
    }

    public IntPtr ResolveHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero || Faulted) return IntPtr.Zero;
        try { return IL2CPP.il2cpp_gchandle_get_target(handle); }
        catch { return IntPtr.Zero; }
    }

    public bool TryReleaseHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return true;
        try
        {
            IL2CPP.il2cpp_gchandle_free(handle);
            return true;
        }
        catch
        {
            RecordPending(handle); // 未确认释放：bounded 记录，供 root 退出判定；不重复 free
            Faulted = true;
            return false;
        }
    }

    // 结果临时句柄：pinned（review 项 2；不依赖未证明的非移动 GC 假设）。
    public IntPtr RetainObject(IntPtr objectPtr)
    {
        if (Faulted || objectPtr == IntPtr.Zero) return IntPtr.Zero;
        try { return IL2CPP.il2cpp_gchandle_new(objectPtr, true); }
        catch { return IntPtr.Zero; }
    }

    public unsafe bool TryInvokeGetter(IntPtr dictionaryPtr, IntPtr keyPtr, out IntPtr resultPtr, out IntPtr excPtr)
    {
        resultPtr = IntPtr.Zero;
        excPtr = IntPtr.Zero;
        if (Faulted || _getItemMethodInfo == IntPtr.Zero || dictionaryPtr == IntPtr.Zero || keyPtr == IntPtr.Zero)
            return false;
        try
        {
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = keyPtr; // 引用类型 TKey 参数槽即对象指针本身（同 generated getter body ABI）
            resultPtr = IL2CPP.il2cpp_runtime_invoke(_getItemMethodInfo, dictionaryPtr, (void**)args, ref excPtr);
            return true; // exc != 0 由上层 fail closed；不制造 managed exception
        }
        catch
        {
            resultPtr = IntPtr.Zero;
            excPtr = IntPtr.Zero;
            return false;
        }
    }

    public unsafe bool TryReadStringObject(IntPtr objectPtr, out int length, out IntPtr chars)
    {
        length = 0;
        chars = IntPtr.Zero;
        if (objectPtr == IntPtr.Zero || _stringClass == IntPtr.Zero) return false;
        try
        {
            if (IL2CPP.il2cpp_object_get_class(objectPtr) != _stringClass) return false; // 防读错对象
            int len = IL2CPP.il2cpp_string_length(objectPtr);
            if (len < 0) return false;
            char* charsPtr = IL2CPP.il2cpp_string_chars(objectPtr);
            if (charsPtr == null) return false;
            length = len;
            chars = (IntPtr)charsPtr;
            return true;
        }
        catch { return false; }
    }

    private unsafe NativeKeyAcquire Preflight(IntPtr handle, string key, IntPtr keyClass)
    {
        try
        {
            IntPtr target = IL2CPP.il2cpp_gchandle_get_target(handle);
            if (target == IntPtr.Zero) return NativeKeyAcquire.Failed;
            if (IL2CPP.il2cpp_object_get_class(target) != keyClass) return NativeKeyAcquire.Failed;
            int len = IL2CPP.il2cpp_string_length(target);
            char* chars = IL2CPP.il2cpp_string_chars(target);
            if (len != key.Length || chars == null) return NativeKeyAcquire.Failed;
            return NativeKeyAcquire.Ready;
        }
        catch (Exception ex)
        {
            return IsCapabilityException(ex) ? NativeKeyAcquire.Unsupported : NativeKeyAcquire.Failed;
        }
    }

    private static bool IsCapabilityException(Exception ex)
        => ex is EntryPointNotFoundException || ex is DllNotFoundException || ex is MissingMethodException
           || ex is MissingFieldException || ex is TypeLoadException || ex is TypeInitializationException;

    private void RecordPending(IntPtr handle)
    {
        if (_pending0 == IntPtr.Zero) _pending0 = handle;
        else if (_pending1 == IntPtr.Zero) _pending1 = handle;
        _pendingCount++;
    }

    private bool TryResolveCapability()
    {
        try
        {
            FieldInfo field = typeof(Il2CppSystem.Collections.Generic.Dictionary<string, string>)
                .GetField(GetItemMethodInfoField, BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(IntPtr)) return false;
            IntPtr methodInfo = (IntPtr)field.GetValue(null); // 当前闭合类型 cctor 已产生的 native MethodInfo
            if (methodInfo == IntPtr.Zero) return false;
            if (IL2CPP.il2cpp_method_get_token(methodInfo) != (uint)HeavyShieldNativeKeyReader.GetItemMethodInfoToken)
                return false;
            _getItemMethodInfo = methodInfo;
            return true;
        }
        catch
        {
            return false; // 字段/token/类型不可用 = 能力缺失
        }
    }
}
#endif
