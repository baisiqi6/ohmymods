using System;
using System.Threading;
using KingdomEnhancedMod;

// source-linked 纯测试：直接编译 ../HeavyShieldNativeKeyReader.cs（production helper，KEM_NATIVE_KEY_PURE_TEST
// 只隔离 native adapter）。覆盖 UTF16 ordinal 比较、fail-closed、failure/unsupported 分型、cleanup 未确认语义、
// 线程绑定、影子对照 mismatch/fault 判罚与 0 managed allocation。
internal static class Program
{
    private static int _fail;

    private static void Check(bool ok, string name)
    {
        if (!ok) { _fail++; Console.WriteLine("FAIL " + name); }
        else Console.WriteLine("ok   " + name);
    }

    private static FakeAdapter Fresh(bool bindCurrentThread = true)
    {
        var adapter = new FakeAdapter();
        HeavyShieldNativeKeyReader.ResetForTest(adapter, bindCurrentThread);
        return adapter;
    }

    private static unsafe bool Utf16(string native, string expected)
    {
        fixed (char* chars = native)
            return HeavyShieldNativeKeyReader.Utf16Equals(native.Length, chars, expected.AsSpan());
    }

    private static unsafe void Utf16Cases()
    {
        Check(Utf16("abc", "abc"), "utf16 equal short");
        Check(Utf16("中文测试 value", "中文测试 value"), "utf16 equal unicode");
        Check(Utf16("", ""), "utf16 equal empty");
        var longText = new string('x', 4095) + "y";
        Check(Utf16(longText, longText), "utf16 equal long 4096");
        Check(!Utf16("Xbc", "abc"), "utf16 mismatch first");
        Check(!Utf16("aXc", "abc"), "utf16 mismatch middle");
        Check(!Utf16("abX", "abc"), "utf16 mismatch last");
        Check(!Utf16("ab", "abc"), "utf16 shorter length fails");
        Check(!Utf16("abcd", "abc"), "utf16 longer length fails");
        Check(!Utf16("abc", "abcd"), "utf16 expected longer fails");
        Check(!Utf16("abc ", "abc"), "utf16 trailing space differs");

        Check(Utf16("a\0b", "a\0b"), "utf16 embedded NUL equal");
        Check(!Utf16("a\0b", "aXb"), "utf16 embedded NUL differs");
        Check(Utf16("\0", "\0"), "utf16 single NUL equal");
        Check(!Utf16("\0", ""), "utf16 NUL vs empty fails");

        const string pair = "\uD83D\uDE00";
        Check(Utf16(pair, pair), "utf16 surrogate pair equal");
        Check(Utf16("\uD83D", "\uD83D"), "utf16 lone high surrogate equal");
        Check(Utf16("\uDE00", "\uDE00"), "utf16 lone low surrogate equal");
        Check(!Utf16("\uD83D\uDE00", "\uD83D"), "utf16 pair vs lone surrogate fails");
        Check(!Utf16("\uD83D", "\uDE00"), "utf16 lone surrogates differ");

        fixed (char* pinned = "")
        {
            Check(pinned != null, "utf16 empty pin non-null");
            Check(!HeavyShieldNativeKeyReader.Utf16Equals(0, null, "".AsSpan()), "utf16 null chars empty expected fails");
            Check(!HeavyShieldNativeKeyReader.Utf16Equals(3, null, "abc".AsSpan()), "utf16 null chars fails");
            Check(!HeavyShieldNativeKeyReader.Utf16Equals(-1, pinned, "".AsSpan()), "utf16 negative length fails");
            Check(!HeavyShieldNativeKeyReader.Utf16Equals(1, pinned, "a".AsSpan()), "utf16 bogus length over empty fails");
        }
    }

    private static unsafe NativeKeyDecision EvaluateWith(FakeAdapter adapter, string nativeText, string expected)
    {
        fixed (char* pinned = nativeText)
        {
            adapter.ReadLength = nativeText.Length;
            adapter.ReadChars = (IntPtr)pinned;
            return HeavyShieldNativeKeyReader.Evaluate((IntPtr)0x9000, expected);
        }
    }

    private static unsafe void NativePathCases()
    {
        var adapter = Fresh();
        const string expected = "KEM-doc-value";
        const string nativeText = "KEM-doc-value";
        Check(EvaluateWith(adapter, nativeText, expected) == NativeKeyDecision.Match, "native fresh invoke match");
        Check(adapter.InvokeCalls == 1 && adapter.RetainCalls == 1 && adapter.ReleaseCalls == 1
            && adapter.Released.Count == 1 && adapter.Released[0] == adapter.ResultHandle,
            "native per-call: invoke once, temp handle freed once");

        adapter = Fresh();
        const string otherNative = "KEM-doc-VALUE";
        Check(EvaluateWith(adapter, otherNative, expected) == NativeKeyDecision.NoMatch,
            "native content mismatch -> NoMatch");
        Check(adapter.ReleaseCalls == 1, "native mismatch still frees temp handle");

        adapter = Fresh();
        const string sameLength = "KEM-doc-XXXXX";
        Check(EvaluateWith(adapter, sameLength, expected) == NativeKeyDecision.NoMatch,
            "native same-length different content -> NoMatch");

        adapter = Fresh();
        const string first = "KEM-doc-value";
        Check(EvaluateWith(adapter, first, expected) == NativeKeyDecision.Match, "native match before re-read");
        const string second = "KEM-doc-aluue";
        Check(EvaluateWith(adapter, second, expected) == NativeKeyDecision.NoMatch, "no cached true: fresh re-read");
        Check(adapter.InvokeCalls == 2 && adapter.ReleaseCalls == 2, "no cached true: two fresh invokes");

        // 业务读失败（exc）保持 NoMatch，重复多次也不锁存/不回退。
        adapter = Fresh();
        adapter.InvokeExc = (IntPtr)0x77;
        adapter.InvokeResult = IntPtr.Zero;
        for (int i = 0; i < 3; i++)
            Check(HeavyShieldNativeKeyReader.Evaluate((IntPtr)0x9000, expected) == NativeKeyDecision.NoMatch,
                "business exception NoMatch #" + i);
        Check(!HeavyShieldNativeKeyReader.UnsupportedLatched && adapter.InvokeCalls == 3,
            "repeated business failure never latches fallback");

        adapter = Fresh();
        adapter.InvokeResult = IntPtr.Zero; // 真实存 null：无 exc
        Check(HeavyShieldNativeKeyReader.Evaluate((IntPtr)0x9000, expected) == NativeKeyDecision.NoMatch,
            "stored null -> NoMatch");
        Check(adapter.RetainCalls == 0, "stored null never retains");

        adapter = Fresh();
        adapter.InvokeOk = false;
        Check(HeavyShieldNativeKeyReader.Evaluate((IntPtr)0x9000, expected) == NativeKeyDecision.NoMatch,
            "invoke transport failure -> NoMatch");

        adapter = Fresh();
        adapter.ReadOk = false; // String class 证明失败
        Check(EvaluateWith(adapter, nativeText, expected) == NativeKeyDecision.NoMatch,
            "string class proof failure -> NoMatch");
        Check(adapter.ReleaseCalls == 1 && !HeavyShieldNativeKeyReader.NativeFaulted,
            "class proof failure frees temp handle without fault");

        adapter = Fresh();
        adapter.HandleTargetOk = false; // handle 失效：key target 为 0
        Check(EvaluateWith(adapter, nativeText, expected) == NativeKeyDecision.NoMatch,
            "stale handle target -> NoMatch");
        Check(adapter.InvokeCalls == 0, "stale handle target never invokes");

        adapter = Fresh();
        adapter.RetainOk = false;
        Check(EvaluateWith(adapter, nativeText, expected) == NativeKeyDecision.NoMatch,
            "result handle unavailable -> NoMatch");
        Check(adapter.ReadCalls == 0 && adapter.ReleaseCalls == 0, "no read/release without owned handle");

        adapter = Fresh();
        Check(HeavyShieldNativeKeyReader.Evaluate(IntPtr.Zero, expected) == NativeKeyDecision.NoMatch,
            "zero dictionary pointer -> NoMatch");
        Check(HeavyShieldNativeKeyReader.Evaluate((IntPtr)0x9000, null) == NativeKeyDecision.NoMatch,
            "null expected -> NoMatch");
        Check(adapter.AcquireCalls == 0 && adapter.InvokeCalls == 0, "guarded inputs never probe adapter");
    }

    // review 项 1：能力缺失才锁存回退；执行失败当次 false、下一次 fresh 可重试。
    private static void FailureVsUnsupportedCases()
    {
        var adapter = Fresh();
        adapter.AcquireResult = NativeKeyAcquire.Unsupported;
        Check(HeavyShieldNativeKeyReader.Evaluate((IntPtr)0x9000, "x") == NativeKeyDecision.NeedsManagedFallback,
            "capability unsupported -> managed fallback");
        Check(adapter.AcquireCalls == 1 && adapter.InvokeCalls == 0 && HeavyShieldNativeKeyReader.UnsupportedLatched,
            "capability unsupported probes once and latches");
        Check(HeavyShieldNativeKeyReader.Evaluate((IntPtr)0x9000, "x") == NativeKeyDecision.NeedsManagedFallback
            && adapter.AcquireCalls == 1, "unsupported latch not retried");

        adapter = Fresh();
        adapter.AcquireResult = NativeKeyAcquire.Failed;
        Check(HeavyShieldNativeKeyReader.Evaluate((IntPtr)0x9000, "x") == NativeKeyDecision.NoMatch,
            "execution failure -> NoMatch (no fallback)");
        Check(!HeavyShieldNativeKeyReader.UnsupportedLatched, "execution failure does not latch");
        Check(HeavyShieldNativeKeyReader.Evaluate((IntPtr)0x9000, "x") == NativeKeyDecision.NoMatch
            && adapter.AcquireCalls == 2, "execution failure retried next fresh call");
        adapter.AcquireResult = NativeKeyAcquire.Ready;
        const string expected = "retry-value";
        const string nativeText = "retry-value";
        Check(EvaluateWith(adapter, nativeText, expected) == NativeKeyDecision.Match,
            "retry after failure reaches native match (no new authority)");

        // 半成功（Ready 但无 handle）既非能力缺失也非正常：当次 false、不锁存、可重试。
        adapter = Fresh();
        adapter.KeyHandle = IntPtr.Zero;
        Check(HeavyShieldNativeKeyReader.Evaluate((IntPtr)0x9000, "x") == NativeKeyDecision.NoMatch,
            "half-success acquire -> NoMatch (not fallback)");
        Check(!HeavyShieldNativeKeyReader.UnsupportedLatched && !HeavyShieldNativeKeyReader.FixedKeyHandleOwned
            && adapter.AcquireCalls == 1, "half-success keeps ownership clean");
        Check(HeavyShieldNativeKeyReader.Evaluate((IntPtr)0x9000, "x") == NativeKeyDecision.NoMatch
            && adapter.AcquireCalls == 2, "half-success retried next fresh call");
    }

    // review 项 3：只有 free 确认成功才清 owned / 允许重新 initialize；失败保留 ownership + Faulted。
    private static unsafe void CleanupCases()
    {
        var adapter = Fresh();
        const string expected = "released-key";
        const string nativeText = "released-key";
        Check(EvaluateWith(adapter, nativeText, expected) == NativeKeyDecision.Match, "ready match before release");
        Check(HeavyShieldNativeKeyReader.FixedKeyHandleOwned, "ready owns fixed key handle");
        int releasesBefore = adapter.ReleaseCalls;
        HeavyShieldNativeKeyReader.ReleaseNativeResources();
        Check(adapter.ReleaseCalls == releasesBefore + 1 && adapter.Released[adapter.ReleaseCalls - 1] == adapter.KeyHandle,
            "release frees exactly the owned key handle");
        Check(!HeavyShieldNativeKeyReader.FixedKeyHandleOwned, "confirmed free drops ownership");
        HeavyShieldNativeKeyReader.ReleaseNativeResources();
        Check(adapter.ReleaseCalls == releasesBefore + 1, "repeated cleanup after success is no-op");
        Check(EvaluateWith(adapter, nativeText, expected) == NativeKeyDecision.Match
            && adapter.AcquireCalls == 2, "post-release call re-initializes");

        // key free 未确认：保留 ownership/Faulted，不宣称已释放、不重复 free、不再 init、不再创建句柄。
        adapter = Fresh();
        Check(EvaluateWith(adapter, nativeText, expected) == NativeKeyDecision.Match, "match before failed key cleanup");
        adapter.KeyReleaseFails = true;
        int beforeFail = adapter.ReleaseCalls;
        HeavyShieldNativeKeyReader.ReleaseNativeResources();
        Check(adapter.ReleaseCalls == beforeFail + 1, "failed key cleanup attempts free once");
        Check(HeavyShieldNativeKeyReader.FixedKeyHandleOwned && HeavyShieldNativeKeyReader.NativeFaulted
            && adapter.Faulted && adapter.PendingCount == 1, "failed key cleanup keeps ownership and faults");
        HeavyShieldNativeKeyReader.ReleaseNativeResources();
        Check(adapter.ReleaseCalls == beforeFail + 1, "no blind repeat free of unconfirmed handle");
        int acquires = adapter.AcquireCalls;
        Check(EvaluateWith(adapter, nativeText, expected) == NativeKeyDecision.NoMatch
            && adapter.AcquireCalls == acquires && !HeavyShieldNativeKeyReader.UnsupportedLatched,
            "faulted state fails closed, no fallback, no re-init");

        // 当次结果 free 未确认：当次必须 false，且后续不再创建新句柄。
        adapter = Fresh();
        adapter.TempReleaseFails = true;
        Check(EvaluateWith(adapter, nativeText, expected) == NativeKeyDecision.NoMatch,
            "unconfirmed temp free forces NoMatch");
        Check(adapter.Faulted && adapter.PendingCount == 1 && HeavyShieldNativeKeyReader.NativeFaulted,
            "unconfirmed temp free records pending fault");
        int invokes = adapter.InvokeCalls;
        int retains = adapter.RetainCalls;
        Check(EvaluateWith(adapter, nativeText, expected) == NativeKeyDecision.NoMatch
            && adapter.InvokeCalls == invokes && adapter.RetainCalls == retains,
            "after temp free fault no new retain/invoke");
    }

    // review 项 4：只有绑定线程执行；其他线程 false/no-op 且不触碰 adapter；未绑定一律 false。
    private static unsafe void ThreadBindingCases()
    {
        var adapter = Fresh();
        var result = NativeKeyDecision.Match;
        int releaseCalls = -1;
        var thread = new Thread(() =>
        {
            result = HeavyShieldNativeKeyReader.Evaluate((IntPtr)0x9000, "x");
            HeavyShieldNativeKeyReader.ReleaseNativeResources();
            releaseCalls = adapter.ReleaseCalls;
        });
        thread.Start();
        thread.Join();
        Check(result == NativeKeyDecision.NoMatch, "off-thread evaluate -> NoMatch");
        Check(adapter.AcquireCalls == 0 && adapter.InvokeCalls == 0, "off-thread evaluate never touches adapter");
        Check(releaseCalls == 0 && !HeavyShieldNativeKeyReader.FixedKeyHandleOwned,
            "off-thread cleanup is no-op");

        // 未绑定：一律 false，不触碰 state/adapter；绑定后恢复。
        adapter = Fresh(bindCurrentThread: false);
        Check(!HeavyShieldNativeKeyReader.MainThreadBound, "unbound state reported");
        const string expected = "bound-key";
        const string nativeText = "bound-key";
        Check(EvaluateWith(adapter, nativeText, expected) == NativeKeyDecision.NoMatch,
            "unbound evaluate -> NoMatch");
        Check(adapter.AcquireCalls == 0 && adapter.InvokeCalls == 0, "unbound never touches adapter");
        Check(HeavyShieldNativeKeyReader.EqualsCurrent(null, expected) == false, "unbound equals -> false");
        HeavyShieldNativeKeyReader.BindMainThread();
        Check(HeavyShieldNativeKeyReader.MainThreadBound, "bind main thread records once");
        Check(EvaluateWith(adapter, nativeText, expected) == NativeKeyDecision.Match, "bound evaluate proceeds");
    }

    // review 项 5：影子 mismatch/fault 当次必须 false；有界到最初 3 次。
    private static void ShadowCases()
    {
        Check(HeavyShieldNativeKeyReader.ShadowConsistent(true, "abc", "abc"), "shadow consistent match");
        Check(!HeavyShieldNativeKeyReader.ShadowConsistent(true, "abc", "abd"), "shadow mismatch detected");
        Check(!HeavyShieldNativeKeyReader.ShadowConsistent(true, "abc", null), "shadow null detected");
        Check(HeavyShieldNativeKeyReader.ShadowConsistent(false, "abc", null), "shadow consistent both false");
        Check(!HeavyShieldNativeKeyReader.ShadowConsistent(false, "abc", "abc"), "shadow native false vs managed true");
        Check(!HeavyShieldNativeKeyReader.ShadowConfirmedResult(true, true), "shadow failure forces false");
        Check(HeavyShieldNativeKeyReader.ShadowConfirmedResult(true, false), "shadow pass keeps true");
        Check(!HeavyShieldNativeKeyReader.ShadowConfirmedResult(false, false), "shadow pass keeps false");

        Environment.SetEnvironmentVariable("KEM_PERF_DIAG", "1");
        FakeAdapter adapter = Fresh();
        HeavyShieldNativeKeyReader.ManagedReadForTest = _ => "shadow-doc";
        Check(EvaluateWith(adapter, "shadow-doc", "shadow-doc") == NativeKeyDecision.Match, "shadow armed match");
        Check(HeavyShieldNativeKeyReader.ShadowOpen, "shadow open right after arm");
        Check(!HeavyShieldNativeKeyReader.ShadowObserveForTest(null, "shadow-doc", NativeKeyDecision.Match, true)
            && HeavyShieldNativeKeyReader.ShadowFailures == 0, "shadow consistent counted once");
        HeavyShieldNativeKeyReader.ManagedReadForTest = _ => "shadow-DOC";
        Check(HeavyShieldNativeKeyReader.ShadowObserveForTest(null, "shadow-doc", NativeKeyDecision.Match, true)
            && HeavyShieldNativeKeyReader.ShadowFailures == 1, "shadow mismatch forces failure");
        HeavyShieldNativeKeyReader.ManagedReadForTest = _ => throw new InvalidOperationException("managed read");
        Check(HeavyShieldNativeKeyReader.ShadowObserveForTest(null, "shadow-doc", NativeKeyDecision.Match, true)
            && HeavyShieldNativeKeyReader.ShadowFailures == 2, "shadow fault forces failure");
        Check(!HeavyShieldNativeKeyReader.ShadowOpen, "shadow budget exhausted after 3");
        Check(!HeavyShieldNativeKeyReader.ShadowObserveForTest(null, "shadow-doc", NativeKeyDecision.Match, true)
            && HeavyShieldNativeKeyReader.ShadowFailures == 2, "shadow bounded: later calls not observed");
        Environment.SetEnvironmentVariable("KEM_PERF_DIAG", null);
    }

    // env/诊断读取故障不得回滚已 Ready/owned 状态，也不得触发第二次 acquire（env-fix review）。
    private static unsafe void EnvFaultCases()
    {
        var adapter = Fresh();
        HeavyShieldNativeKeyReader.EnvForTest = _ => throw new InvalidOperationException("env");

        const string expected = "env-key";
        const string nativeText = "env-key";
        Check(EvaluateWith(adapter, nativeText, expected) == NativeKeyDecision.Match, "env fault keeps native match");
        Check(HeavyShieldNativeKeyReader.FixedKeyHandleOwned && !HeavyShieldNativeKeyReader.NativeFaulted
            && !HeavyShieldNativeKeyReader.UnsupportedLatched && adapter.AcquireCalls == 1,
            "env fault keeps Ready/ownership");
        Check(!HeavyShieldNativeKeyReader.ShadowOpen, "env fault leaves shadow disarmed");
        Check(EvaluateWith(adapter, nativeText, expected) == NativeKeyDecision.Match && adapter.AcquireCalls == 1,
            "env fault never acquires a second handle");

        adapter = Fresh();
        HeavyShieldNativeKeyReader.EnvForTest = name => name == "KEM_PERF_DIAG" ? "1" : null;
        Check(EvaluateWith(adapter, nativeText, expected) == NativeKeyDecision.Match, "env seam arms shadow");
        Check(HeavyShieldNativeKeyReader.ShadowOpen, "env seam shadow open");
    }

    private static unsafe void NoManagedAllocationCase()
    {
        var adapter = Fresh();
        const string expected = "KEM-doc-value";
        const string nativeText = "KEM-doc-value";
        fixed (char* pinned = nativeText)
        {
            adapter.ReadLength = nativeText.Length;
            adapter.ReadChars = (IntPtr)pinned;
            if (HeavyShieldNativeKeyReader.Evaluate((IntPtr)0x9000, expected) != NativeKeyDecision.Match)
            { Check(false, "no-alloc warmup match"); return; }
            adapter.Released.Capacity = 4096; // 替身记录用 List 预分配：测量只归因 production 比较路径
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool allMatch = true;
            for (int i = 0; i < 1000; i++)
                allMatch &= HeavyShieldNativeKeyReader.Evaluate((IntPtr)0x9000, expected) == NativeKeyDecision.Match;
            long delta = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allMatch && delta == 0, "native compare path 0 managed alloc over 1000 iters (delta=" + delta + ")");
        }
    }

    private static int Main()
    {
        Environment.SetEnvironmentVariable("KEM_PERF_DIAG", null);
        Check(!HeavyShieldNativeKeyReader.ShadowOpen, "shadow disarmed without KEM_PERF_DIAG");
        Check(HeavyShieldNativeKeyReader.GetItemMethodInfoToken == 100676033, "get_Item token constant pinned");
        Check(HeavyShieldNativeKeyReader.EqualsCurrent(null, "x") == false, "null contents -> false");
        Check(HeavyShieldNativeKeyReader.EqualsCurrent(null, null) == false, "null contents/expected -> false");
        Check(HeavyShieldNativeKeyReader.PerfDiagLine().Contains("failure=0"), "perf diag line bounded, no payload");

        Utf16Cases();
        NativePathCases();
        FailureVsUnsupportedCases();
        CleanupCases();
        ThreadBindingCases();
        ShadowCases();
        EnvFaultCases();
        NoManagedAllocationCase();

        Console.WriteLine(_fail == 0 ? "ALL PASS" : ("FAILURES=" + _fail));
        return _fail == 0 ? 0 : 1;
    }
}
