using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using HarmonyLib.Public.Patching;

namespace KingdomEnhancedMod;

// PatchPerf_HeavyShieldHookQuery.cs 的 source-linked 实际测试：
//   * 直接编译生产 helper 源文件（csproj 里 <Compile Include="../PatchPerf_HeavyShieldHookQuery.cs" />）；
//   * 直接引用 actual 运行时 0Harmony.dll
//     $(BepInExRoot)/core/0Harmony.dll
//     （SHA256 6c898933...124c0877；运行时会再就地核对一次哈希）。
//
// 全部登记状态只用实际 API 对象制造：本测试程序集自有 MethodBase 上的 PatchManager.ToPatchInfo，
// 加 public PatchInfo.Add*/Remove*（Add* 是 public 兼容重载，Remove* 按 owner）。不调用
// Harmony.Patch/Unpatch、不接触游戏/native 目标、不建 detour、不加载 BepInEx/Unity；
// 只在独立 dotnet CLI 进程内运行。
internal static class HookQueryTestTargets
{
    internal static void Patched() { }
    internal static void Second() { }
    internal static void PatchedCompat() { }
    internal static void SecondCompat() { }
    internal static void Cold() { }
}

internal static class HookQueryTestPatchA
{
    internal static void Prefix() { }
    internal static void Postfix() { }
    internal static Exception Finalizer(Exception __exception) => __exception;
}

internal static class HookQueryTestPatchB
{
    internal static void Prefix() { }
    internal static void Postfix() { }
    internal static Exception Finalizer(Exception __exception) => __exception;
}

// typed field contract seam 用的形状替身（不参与业务，只验证 helper 的字段前提判定）。
internal sealed class HookQueryWrongTypeInfo
{
    public object prefixes;
    public object finalizers;
    public object postfixes;

    internal HookQueryWrongTypeInfo()
    {
        prefixes = finalizers = postfixes = null;
    }
}

internal sealed class HookQueryPrivateFieldInfo
{
    private Patch[] prefixes;
    private Patch[] finalizers;
    private Patch[] postfixes;

    internal HookQueryPrivateFieldInfo()
    {
        prefixes = finalizers = postfixes = new Patch[0];
        _ = prefixes.Length + finalizers.Length + postfixes.Length;
    }
}

internal sealed class HookQueryIncompleteInfo
{
    public Patch[] prefixes;
    public Patch[] finalizers;

    internal HookQueryIncompleteInfo()
    {
        prefixes = new Patch[0];
        finalizers = new Patch[0];
    }
}

internal static class HookQueryActualTests
{
    private const string ActualHarmonySha256 = "6c898933b52149e8bcf6722305c7e4d94add47c9e53b364096f91721624c0877";
    private const string OwnerA = "kem-hookquery-a";
    private const string OwnerB = "kem-hookquery-b";

    private static readonly MethodBase TargetPatched = Resolve(nameof(HookQueryTestTargets.Patched));
    private static readonly MethodBase TargetSecond = Resolve(nameof(HookQueryTestTargets.Second));
    private static readonly MethodBase TargetPatchedCompat = Resolve(nameof(HookQueryTestTargets.PatchedCompat));
    private static readonly MethodBase TargetSecondCompat = Resolve(nameof(HookQueryTestTargets.SecondCompat));
    private static readonly MethodBase TargetCold = Resolve(nameof(HookQueryTestTargets.Cold));
    private static readonly Type PatchA = typeof(HookQueryTestPatchA);
    private static readonly Type PatchB = typeof(HookQueryTestPatchB);

    private static int _passed;
    private static int _failed;
    private static volatile int _sink;

    private static int Main(string[] args)
    {
        string harmonyPath = typeof(Harmony).Assembly.Location;
        bool diag = string.Equals(Environment.GetEnvironmentVariable("KEM_PERF_DIAG"), "1", StringComparison.Ordinal);
        Console.WriteLine("HeavyShieldHookQuery source-linked actual tests");
        Console.WriteLine("loaded 0Harmony: " + harmonyPath + " v" + typeof(Harmony).Assembly.GetName().Version);
        Check(File.Exists(harmonyPath) && Sha256(harmonyPath) == ActualHarmonySha256,
            "loaded 0Harmony is the actual D runtime DLL (" + ActualHarmonySha256 + ")");
        Console.WriteLine("mode: " + (diag ? "diag (KEM_PERF_DIAG=1)" : "default"));

        if (diag)
        {
            RunDiagMode();
        }
        else
        {
            RunSemanticsSuite("live", false, TargetPatched, TargetSecond);
            RunSemanticsSuite("compat", true, TargetPatchedCompat, TargetSecondCompat);
            RunAlloc("live", false, TargetPatched);
            RunAlloc("compat", true, TargetPatchedCompat);
            Check(HeavyShieldHookQuery.PerfDiagLine.Contains("shadow=0"),
                "default mode: no shadow comparison ran");
        }

        RunTestSeams();
        Console.WriteLine("PerfDiagLine " + HeavyShieldHookQuery.PerfDiagLine);
        Console.WriteLine("checks passed=" + _passed + " failed=" + _failed);
        return _failed == 0 ? 0 : 1;
    }

    // 与模式无关的 seam：诊断环境读取 no-throw、typed 字段前提判定；均调用实际 helper 代码路径。
    private static void RunTestSeams()
    {
        Check(HeavyShieldHookQuery.TestTryReadDiagFlag(() => "1"), "diag env seam: value 1 -> enabled");
        Check(!HeavyShieldHookQuery.TestTryReadDiagFlag(() => "0"), "diag env seam: value 0 -> disabled");
        Check(!HeavyShieldHookQuery.TestTryReadDiagFlag(() => throw new InvalidOperationException("simulated env read failure")),
            "diag env seam: read exception -> disabled, no throw");
        Check(Has(TargetPatched, PatchA, true) && !Has(TargetCold, PatchA),
            "capability/business unaffected after env-read failure seam");

        Check(HeavyShieldHookQuery.TestHasExactPatchArrayFields(typeof(PatchInfo)),
            "typed field contract: actual PatchInfo has public Patch[] prefixes/finalizers/postfixes");
        Check(!HeavyShieldHookQuery.TestHasExactPatchArrayFields(typeof(HookQueryTestPatchA)),
            "typed field contract: unrelated type -> false");
        Check(!HeavyShieldHookQuery.TestHasExactPatchArrayFields(typeof(HookQueryWrongTypeInfo)),
            "typed field contract: wrong field types -> false");
        Check(!HeavyShieldHookQuery.TestHasExactPatchArrayFields(typeof(HookQueryPrivateFieldInfo)),
            "typed field contract: non-public fields -> false");
        Check(!HeavyShieldHookQuery.TestHasExactPatchArrayFields(typeof(HookQueryIncompleteInfo)),
            "typed field contract: missing postfixes -> false");
    }

    private static void RunSemanticsSuite(string mode, bool forceCompat, MethodBase target, MethodBase second)
    {
        HeavyShieldHookQuery.TestForceCompatPath = forceCompat;
        try
        {
            var info = PatchManager.ToPatchInfo(target);
            var secondInfo = PatchManager.ToPatchInfo(second);

            Check(!Has(target, PatchA), mode + ": empty registration -> false");
            Check(!Has(target, PatchA, true), mode + ": empty registration requirePostfix -> false");
            Check(!Has(TargetCold, PatchA), mode + ": unregistered method (missingInfo) -> false");
            Check(!Has(null, PatchA), mode + ": null method -> false");
            Check(!Has(target, null), mode + ": null patch type -> false");
            Check(!Has(target, PatchB), mode + ": wrong class on empty registration -> false");

            AddPrefix(info, PatchA, OwnerA);
            Check(!Has(target, PatchA, true), mode + ": prefix only -> false (finalizer required)");
            AddFinalizer(info, PatchA, OwnerA);
            Check(Has(target, PatchA), mode + ": prefix+finalizer -> true");
            Check(!Has(target, PatchA, true), mode + ": requirePostfix without postfix -> false");
            Check(LegacyHas(target, PatchA, true) == false, mode + ": original expression agrees (no postfix)");
            AddPostfix(info, PatchA, OwnerA);
            Check(Has(target, PatchA, true), mode + ": prefix+finalizer+postfix -> true");
            Check(LegacyHas(target, PatchA, true), mode + ": original expression agrees (full trio)");
            Check(Has(target, PatchA) == LegacyHas(target, PatchA, false), mode + ": live/compat decision == original expression (plain)");

            info.RemoveFinalizer(OwnerA);
            Check(!Has(target, PatchA, true), mode + ": finalizer removed -> immediate false");
            Check(!LegacyHas(target, PatchA, true), mode + ": original expression agrees after finalizer removal");
            AddFinalizer(info, PatchA, OwnerA);
            Check(Has(target, PatchA, true), mode + ": finalizer re-added -> true again (nothing cached)");

            info.RemovePostfix(OwnerA);
            Check(Has(target, PatchA) && !Has(target, PatchA, true),
                mode + ": postfix removed: plain true, requirePostfix false");
            Check(LegacyHas(target, PatchA, false) && !LegacyHas(target, PatchA, true),
                mode + ": original expression agrees postfix removal");
            AddPostfix(info, PatchA, OwnerA);
            Check(Has(target, PatchA, true), mode + ": postfix re-added -> true");

            info.RemovePrefix(OwnerA);
            Check(!Has(target, PatchA, true), mode + ": prefix removed -> false");
            AddPrefix(info, PatchA, OwnerA);
            Check(Has(target, PatchA, true), mode + ": prefix re-added -> true");

            AddPrefix(secondInfo, PatchB, OwnerB);
            AddFinalizer(secondInfo, PatchB, OwnerB);
            AddPostfix(secondInfo, PatchB, OwnerB);
            Check(Has(second, PatchB, true), mode + ": second target via its own class -> true");
            Check(!Has(second, PatchA, true), mode + ": alien DeclaringType on registered target -> false");

            // 原始 null 数组 / null 条目：必需项按空集/坏项口径 false。
            secondInfo.postfixes = null;
            Check(Has(second, PatchB) && !Has(second, PatchB, true),
                mode + ": null postfixes: plain true, requirePostfix false");
            secondInfo.postfixes = new Patch[0];
            AddPostfix(secondInfo, PatchB, OwnerB);

            secondInfo.prefixes = null;
            Check(!Has(second, PatchB, true), mode + ": null prefixes -> false");
            secondInfo.prefixes = new Patch[0];
            AddPrefix(secondInfo, PatchB, OwnerB);
            Check(Has(second, PatchB, true), mode + ": restored after null-array probe -> true");

            // query failure：actual Patch 的反序列化形态（patchMethod 未解析、moduleGUID 无匹配 module）
            // 使 PatchMethod getter 抛异常；helper 必须当次 false，不退化为 fallback 成功。
            Patch broken = new Patch(MethodOf(PatchB, "Prefix"), 0, OwnerB, 0, null, null, false);
            SetPrivateField(broken, "patchMethod", null);
            SetPrivateField(broken, "methodToken", 0);
            SetPrivateField(broken, "moduleGUID", "00000000-0000-0000-0000-000000000000");
            Patch matching = new Patch(MethodOf(PatchB, "Prefix"), 0, OwnerB, 0, null, null, false);

            secondInfo.prefixes = new Patch[] { broken };
            Check(!Has(second, PatchB, true), mode + ": unresolvable PatchMethod only -> query failure false");
            Check(LegacyThrows(second, PatchB, true), mode + ": old expression throws on unresolvable PatchMethod");

            // 遍历顺序语义：坏项在匹配项之前 → 立即 false；匹配项在前 → 短路 true（保持原 Any 语义）。
            secondInfo.prefixes = new Patch[] { null, matching };
            Check(!Has(second, PatchB, true), mode + ": [null, matching] -> false (bad item first)");
            Check(LegacyThrows(second, PatchB, true), mode + ": old expression throws on [null, matching]");
            secondInfo.prefixes = new Patch[] { matching, null };
            Check(Has(second, PatchB, true), mode + ": [matching, null] -> true (short-circuit)");
            Check(LegacyHas(second, PatchB, true), mode + ": old expression true on [matching, null]");

            secondInfo.prefixes = new Patch[] { broken, matching };
            Check(!Has(second, PatchB, true), mode + ": [unresolvable, matching] -> false (bad item first)");
            Check(LegacyThrows(second, PatchB, true), mode + ": old expression throws on [unresolvable, matching]");
            secondInfo.prefixes = new Patch[] { matching, broken };
            Check(Has(second, PatchB, true), mode + ": [matching, unresolvable] -> true (short-circuit)");
            Check(LegacyHas(second, PatchB, true), mode + ": old expression true on [matching, unresolvable]");

            secondInfo.prefixes = new Patch[0];
            AddPrefix(secondInfo, PatchB, OwnerB);
            Check(Has(second, PatchB, true), mode + ": restored after ordering probes -> true");
        }
        finally
        {
            HeavyShieldHookQuery.TestForceCompatPath = false;
        }
    }

    private static void RunAlloc(string label, bool forceCompat, MethodBase target)
    {
        HeavyShieldHookQuery.TestForceCompatPath = forceCompat;
        try
        {
            var info = PatchManager.ToPatchInfo(target);
            info.RemovePrefix("*");
            info.RemovePostfix("*");
            info.RemoveFinalizer("*");
            AddPrefix(info, PatchA, OwnerA);
            AddFinalizer(info, PatchA, OwnerA);
            AddPostfix(info, PatchA, OwnerA);

            Warmup(target, info);
            long patched = Window(target, 1000, true);
            info.RemoveFinalizer(OwnerA);
            long removed = Window(target, 1000, false);
            AddFinalizer(info, PatchA, OwnerA);
            long readded = Window(target, 1000, true);
            long total = patched + removed + readded;
            Console.WriteLine(label + " 1000it helper alloc: patched=" + patched + "B finalizer-removed=" + removed
                + "B re-added=" + readded + "B (true->false->true observed in all three windows)");
            if (forceCompat)
                Console.WriteLine(label + " path = old public snapshot fallback (unsupported-capability shape); allocation reported, not asserted");
            else
                Check(total == 0, "live path: 1000-iteration helper allocation is 0 across true->false->true (measured " + total + "B)");
        }
        finally
        {
            HeavyShieldHookQuery.TestForceCompatPath = false;
        }
    }

    private static void RunDiagMode()
    {
        var info = PatchManager.ToPatchInfo(TargetPatched);
        info.RemovePrefix("*");
        info.RemovePostfix("*");
        info.RemoveFinalizer("*");
        AddPrefix(info, PatchA, OwnerA);
        AddFinalizer(info, PatchA, OwnerA);
        AddPostfix(info, PatchA, OwnerA);

        // 并发抢 shadow 槽位：8 线程 × 200 次真实 helper 调用（source-linked），验证上限原子性。
        RunConcurrentShadowBudget();

        string line = HeavyShieldHookQuery.PerfDiagLine;
        Console.WriteLine("PerfDiagLine " + line);
        Check(line.Contains("mode=live") && line.Contains("livecap=1"), "diag: mode/livecap reported");
        Check(line.Contains("shadow=3"), "diag: shadow claims capped at exactly 3 under concurrency");
        Check(line.Contains("fault=0"), "diag: no shadow fault on consistent state");
        Check(line.IndexOf("Patch", StringComparison.Ordinal) < 0, "diag: line carries no patch/method payload");

        // 预算耗尽：不再调用旧 getter（0B），且仍 fresh（不缓存）。
        for (int i = 0; i < 100; i++) _sink += Has(TargetPatched, PatchA, true) ? 1 : 0;
        Check(HeavyShieldHookQuery.PerfDiagLine.Contains("shadow=3"), "diag: shadow stayed capped after further reads");
        Check(Window(TargetPatched, 200, true) == 0,
            "diag: after budget the old getter is no longer called (0B over 200 iterations)");

        info.RemoveFinalizer(OwnerA);
        Check(!Has(TargetPatched, PatchA, true), "diag: removal after budget -> immediate false");
        AddFinalizer(info, PatchA, OwnerA);
        Check(Has(TargetPatched, PatchA, true), "diag: re-added after budget -> true (nothing cached)");
    }

    private static void RunConcurrentShadowBudget()
    {
        const int threadCount = 8;
        const int iterations = 200;
        var failures = new int[threadCount];
        var errors = new Exception[threadCount];
        var workers = new Thread[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int index = t;
            workers[t] = new Thread(() =>
            {
                try
                {
                    int bad = 0;
                    for (int i = 0; i < iterations; i++)
                    {
                        if (!HeavyShieldHookQuery.Has(TargetPatched, PatchA, true)) bad++;
                    }
                    failures[index] = bad;
                }
                catch (Exception ex)
                {
                    errors[index] = ex;
                }
            });
            workers[t].IsBackground = true;
            workers[t].Start();
        }
        for (int t = 0; t < threadCount; t++) workers[t].Join();

        int bad = 0;
        int errorCount = 0;
        for (int t = 0; t < threadCount; t++)
        {
            bad += failures[t];
            if (errors[t] != null) errorCount++;
        }
        Check(bad == 0 && errorCount == 0,
            "concurrent reads across 8 threads: all true, no exceptions (bad=" + bad + ", errors=" + errorCount + ")");
    }

    private static void Warmup(MethodBase target, PatchInfo info)
    {
        for (int i = 0; i < 250; i++) _sink += Has(target, PatchA, true) ? 1 : 0;
        info.RemoveFinalizer(OwnerA);
        for (int i = 0; i < 250; i++) _sink += Has(target, PatchA, true) ? 1 : 0;
        AddFinalizer(info, PatchA, OwnerA);
        for (int i = 0; i < 250; i++) _sink += Has(target, PatchA, true) ? 1 : 0;
    }

    private static long Window(MethodBase target, int iterations, bool expect)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        int unexpected = 0;
        for (int i = 0; i < iterations; i++)
        {
            if (HeavyShieldHookQuery.Has(target, PatchA, true) != expect) unexpected++;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(unexpected == 0, "window expectation (" + (expect ? "true" : "false") + ") held for " + iterations + " iterations");
        return allocated;
    }

    // 复刻原 Installed 的表达式语义（含 Demote 的额外 postfix 查询），用于对照。
    private static bool LegacyHas(MethodBase method, Type patch, bool requirePostfix)
    {
        var info = HarmonyLib.Harmony.GetPatchInfo(method);
        if (info == null) return false;
        return info.Prefixes.Any(p => p.PatchMethod.DeclaringType == patch)
            && info.Finalizers.Any(p => p.PatchMethod.DeclaringType == patch)
            && (!requirePostfix || info.Postfixes.Any(p => p.PatchMethod.DeclaringType == patch));
    }

    // 旧表达式的坏登记项行为：遍历到坏项即抛（上层付款预检 fail-closed）。
    private static bool LegacyThrows(MethodBase method, Type patch, bool requirePostfix)
    {
        try
        {
            LegacyHas(method, patch, requirePostfix);
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool Has(MethodBase method, Type patch, bool requirePostfix = false)
        => HeavyShieldHookQuery.Has(method, patch, requirePostfix);

#pragma warning disable 618
    private static void AddPrefix(PatchInfo info, Type patchClass, string owner)
        => info.AddPrefix(MethodOf(patchClass, "Prefix"), owner, 0, null, null, false);

    private static void AddPostfix(PatchInfo info, Type patchClass, string owner)
        => info.AddPostfix(MethodOf(patchClass, "Postfix"), owner, 0, null, null, false);

    private static void AddFinalizer(PatchInfo info, Type patchClass, string owner)
        => info.AddFinalizer(MethodOf(patchClass, "Finalizer"), owner, 0, null, null, false);
#pragma warning restore 618

    private static void SetPrivateField(object instance, string name, object value)
        => typeof(Patch).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(instance, value);

    private static MethodInfo MethodOf(Type patchClass, string name)
        => patchClass.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);

    private static MethodBase Resolve(string name)
        => typeof(HookQueryTestTargets).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);

    private static void Check(bool condition, string label)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine("ok   " + label);
        }
        else
        {
            _failed++;
            Console.WriteLine("FAIL " + label);
        }
    }

    private static string Sha256(string path)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(File.ReadAllBytes(path));
            var text = new System.Text.StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) text.Append(hash[i].ToString("x2"));
            return text.ToString();
        }
    }
}
