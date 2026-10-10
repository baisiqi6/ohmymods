using System;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;

namespace KingdomEnhancedMod;

// Issue206 · HeavyShieldNativeHooks.Installed 的查询路径。
//
// 原实现每次 Installed 走 7 次 public 快照查询（six Has 加一次 Demote-postfix），每次
// HarmonyLib.Harmony.GetPatchInfo -> PatchProcessor.GetPatchInfo -> new Patches(...)：五个数组各
// ToList().AsReadOnly() 加 LINQ 闭包（一次 Installed 约 35 组列表/wrapper），CarrierPreflightReady
// 每帧约到七次。接线后 caller 是 6 次 fresh 读取（Demote 的那一次在同一读取上核 prefix/finalizer/
// postfix 三种成员）；合同是“同样守卫语义”，不是查询次数字面。
//
// 本 helper 从 live 登记视图回答同一个成员问题：
//   * 能力：当前已加载 Harmony 程序集里精确的公开静态方法 PatchManager.GetPatchInfo(MethodBase)
//     -> PatchInfo，按精确限定名反射一次并绑定为缓存 delegate。不 static 引用可能缺失的平台类型，
//     因此类型缺失不会让本 helper 的 JIT 失败。绑定前同时核对 typed 前提：PatchInfo 必须有
//     public instance 的 Patch[] prefixes / finalizers / postfixes 字段；缺失、类型不符或不可见
//     一律按能力 unsupported 处理走兼容路径。
//   * 每次 Has 都 fresh 调用该 delegate，读取当前 PatchInfo 对象的当前 prefix/finalizer/(可选)
//     postfix 数组；数组只捕获进本次调用的局部变量。任何东西都不跨调用缓存：PatchInfo 对象、
//     数组、匹配结果、布尔值都不缓存。typed 字段读取集中在只于能力就绪时调用的独立方法里，
//     兼容分支与 Has 入口不会在 JIT 时加载这些字段。
//   * 成员判定按原 Any 的遍历顺序：null 条目或 null PatchMethod 视为“读不动”的坏项，立即 false
//     （旧 Any 在该位置会抛异常，付款预检 fail-closed）；此前已匹配的条目仍可短路 true。
//   * 能力不可用（精确类型/方法/签名/typed 字段缺失，或 delegate 绑定失败）在类型初始化时一次
//     锁存，改用原始 public 快照路径：Harmony.GetPatchInfo 加 ReadOnlyCollection 索引循环。仍是
//     fresh 读取、同样语义，只是保留原分配。运行时读取失败（invoke/字段/解析异常）一律当次 false，
//     不 fallback、不缓存；此处 false 只关闭新付款门（Installed 消费方），不会重装或摘除任何 hook。
//
// 并发契约与原 public 快照读取一致：GetPatchInfo 只在 PatchManager 字典锁内取出当前 PatchInfo 对象，
// Patch/Unpatch 随后在另一把锁下替换数组。因此这里同样只承诺“读取时点的当前登记视图”，不是跨方法
// 原子快照；每次调用把每个数组只捕获一次到局部变量，不会在同一数组上混合长度重读。两次守卫之间发生
// 并发 unpatch 时，由下一次守卫的 fresh 读取回答，与原行为一致。
//
// 诊断仅 opt-in：KEM_PERF_DIAG=1 时，最多三次成功 live 读取附带旧 public 快照 shadow 对照；槽位用
// CAS 原子领取（并发下也不会超过三次），判定不一致或 shadow 出错时当次返回 false 并计数，不缓存；
// 预算用尽后不再调用旧 getter。环境变量读取在独立 no-throw helper 内完成，读取失败只关诊断，不会
// 毒化类型初始化、不影响已绑定的能力与业务；未设变量时只是一次布尔早退。
internal static class HeavyShieldHookQuery
{
    private const string PatchManagerTypeName = "HarmonyLib.Public.Patching.PatchManager";
    private const int ShadowBudget = 3;

    // 绑定一次；null 表示能力 unsupported，之后每次调用走 compat 快照路径。
    private static readonly Func<MethodBase, PatchInfo> LiveGetPatchInfo = BindLiveGetPatchInfo();

    // 诊断开关用 no-throw 读取；异常只禁用诊断，不毒化类型初始化。
    private static readonly bool DiagEnabled = TryReadDiagFlag(ReadDiagEnvironment);

    private static int _diagShadows;
    private static int _diagFaults;

#if KEM_HOOKQUERY_TEST_SEAM
    // 仅测试编译符号（KEM_HOOKQUERY_TEST_SEAM，只由 source-linked tests 定义）注入的 seam；
    // 产品构建不含以下成员。
    internal static bool TestForceCompatPath;

    internal static bool TestTryReadDiagFlag(Func<string> read) => TryReadDiagFlag(read);

    internal static bool TestHasExactPatchArrayFields(Type infoType) => HasExactPatchArrayFields(infoType);
#endif

    // 供 root 在 KEM_PERF_DIAG=1 时打印一次；无 payload（不含方法/补丁类名）。
    internal static string PerfDiagLine
    {
        get
        {
            bool live = LiveGetPatchInfo != null;
            return "KEM_HOOKQUERY mode=" + (live ? "live" : "compat-unsupported")
                + " livecap=" + (live ? "1" : "0")
                + " shadow=" + Volatile.Read(ref _diagShadows)
                + " fault=" + Volatile.Read(ref _diagFaults);
        }
    }

    internal static bool Has(MethodBase method, Type patch, bool requirePostfix = false)
    {
        if (method == null || patch == null) return false;
        var live = LiveGetPatchInfo;
#if KEM_HOOKQUERY_TEST_SEAM
        if (TestForceCompatPath) live = null;
#endif
        if (live == null) return HasFromPublicSnapshot(method, patch, requirePostfix);
        try
        {
            bool result = HasLiveResult(live(method), patch, requirePostfix);
            if (DiagEnabled && TryClaimShadowSlot())
                return ShadowCheck(method, patch, requirePostfix, result);
            return result;
        }
        catch
        {
            return false; // 运行时读取失败：当次关闭，不 fallback，不缓存
        }
    }

    // typed live-field 读取只在本方法内；只有能力就绪（capReady）才会调用，因此即使某个 Harmony
    // 版本缺字段，Has 入口与 compat 分支也不会在 JIT 时解析这些字段。字段引用每次只落本次局部。
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool HasLiveResult(PatchInfo info, Type patch, bool requirePostfix)
    {
        if (info == null) return false;
        var prefixes = info.prefixes;
        var finalizers = info.finalizers;
        if (!HasEntry(prefixes, patch) || !HasEntry(finalizers, patch)) return false;
        return !requirePostfix || HasEntry(info.postfixes, patch);
    }

    private static bool ShadowCheck(MethodBase method, Type patch, bool requirePostfix, bool liveResult)
    {
        bool legacy;
        try
        {
            legacy = HasFromSnapshotCore(method, patch, requirePostfix);
        }
        catch
        {
            Interlocked.Increment(ref _diagFaults);
            return false; // shadow 出错：当次 false，只计数，不缓存
        }
        if (legacy != liveResult)
        {
            Interlocked.Increment(ref _diagFaults);
            return false; // live 与旧 getter 判定不一致：当次 false，保留 fault 可见
        }
        return liveResult;
    }

    // CAS 领取 shadow 槽位：并发下领取总数不超过 ShadowBudget；预算耗尽即不再调用旧 getter。
    private static bool TryClaimShadowSlot()
    {
        int current = Volatile.Read(ref _diagShadows);
        while (current < ShadowBudget)
        {
            int seen = Interlocked.CompareExchange(ref _diagShadows, current + 1, current);
            if (seen == current) return true;
            current = seen;
        }
        return false;
    }

    private static string ReadDiagEnvironment() => Environment.GetEnvironmentVariable("KEM_PERF_DIAG");

    private static bool TryReadDiagFlag(Func<string> read)
    {
        try
        {
            return string.Equals(read(), "1", StringComparison.Ordinal);
        }
        catch
        {
            return false; // 诊断读取失败只关诊断，不影响能力绑定与业务
        }
    }

    private static Func<MethodBase, PatchInfo> BindLiveGetPatchInfo()
    {
        try
        {
            var manager = typeof(HarmonyLib.Harmony).Assembly.GetType(PatchManagerTypeName, false);
            if (manager == null || manager.IsGenericType) return null;
            var method = manager.GetMethod("GetPatchInfo", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(MethodBase) }, null);
            if (method == null || !method.IsStatic || method.IsGenericMethodDefinition) return null;
            if (method.ReturnType != typeof(PatchInfo)) return null;
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(MethodBase)) return null;
            if (!HasExactPatchArrayFields(typeof(PatchInfo))) return null; // typed 前提缺失 → unsupported
            return method.CreateDelegate<Func<MethodBase, PatchInfo>>();
        }
        catch
        {
            return null; // 能力 unsupported：锁存一次，走 compat 快照路径，绝不伪造结果
        }
    }

    // 事前合同：PatchInfo 必须是 public instance 的 Patch[] prefixes / finalizers / postfixes。
    private static bool HasExactPatchArrayFields(Type infoType)
        => IsExactPatchArrayField(infoType, "prefixes")
        && IsExactPatchArrayField(infoType, "finalizers")
        && IsExactPatchArrayField(infoType, "postfixes");

    private static bool IsExactPatchArrayField(Type infoType, string name)
    {
        var field = infoType.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        return field != null && field.FieldType == typeof(Patch[]);
    }

    private static bool HasFromPublicSnapshot(MethodBase method, Type patch, bool requirePostfix)
    {
        try
        {
            return HasFromSnapshotCore(method, patch, requirePostfix);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasFromSnapshotCore(MethodBase method, Type patch, bool requirePostfix)
    {
        var snapshot = HarmonyLib.Harmony.GetPatchInfo(method);
        if (snapshot == null) return false;
        return HasEntry(snapshot.Prefixes, patch)
            && HasEntry(snapshot.Finalizers, patch)
            && (!requirePostfix || HasEntry(snapshot.Postfixes, patch));
    }

    private static bool HasEntry(Patch[] patches, Type patch)
    {
        if (patches == null) return false; // 原始 null 数组：按原 normalizing 口径视为空集
        for (int i = 0; i < patches.Length; i++)
        {
            var entry = patches[i];
            if (entry == null) return false;            // 坏项：立即 false（旧 Any 在此抛）
            var patchMethod = entry.PatchMethod;
            if (patchMethod == null) return false;      // 未解析：立即 false
            if (patchMethod.DeclaringType == patch) return true;
        }
        return false;
    }

    private static bool HasEntry(ReadOnlyCollection<Patch> patches, Type patch)
    {
        if (patches == null) return false;
        for (int i = 0; i < patches.Count; i++)
        {
            var entry = patches[i];
            if (entry == null) return false;
            var patchMethod = entry.PatchMethod;
            if (patchMethod == null) return false;
            if (patchMethod.DeclaringType == patch) return true;
        }
        return false;
    }
}
