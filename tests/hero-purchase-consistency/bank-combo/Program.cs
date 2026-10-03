// Combined H hero-rights gate + H-side bank gate on GlobalSaveData.SaveAsync / _Save_d__89.
//
// The scheduling model is the installed HarmonyX 2.10.2 WritePrefixes IL (see
// logs/r2/harmony-emit-call-parameter.il): every prefix is invoked with the shared runOriginal
// local passed by reference, runOriginal is ANDed with each prefix's bool result, and the
// original method runs only when the accumulated flag survives. This is an offline model with
// boundary stubs; it does not execute the game or Harmony runtime.
using System;
using System.Reflection;
using Coatsink.Common;
using KingdomEnhancedMod;

internal static class Program
{
    private const string HeroKey = "KingdomEnhancedMod_HeroRights_v1";
    private const string BankKey = "MyMod_SharedBankNative_v1";

    private static int Main(string[] args)
    {
        Async("combo-async-hero-first", heroFirst: true);
        Async("combo-async-bank-first", heroFirst: false);
        NaiveControl(heroFirst: true);
        NaiveControl(heroFirst: false);
        Sync("combo-sync-hero-first", heroFirst: true);
        Sync("combo-sync-bank-first", heroFirst: false);
        return Harness.Finish();
    }

    // ---------------------------------------------------------------- async gate combination

    private static void Async(string scenario, bool heroFirst)
    {
        Harness.Test(scenario + "-both-allow", () =>
        {
            Fixture.NewWorld();
            var run = RunAsync(heroFirst);
            Harness.True(run.Ran, "original runs when both gates allow");
            Harness.Eq(0, run.Callbacks, "no failure callback");
        });
        Harness.Test(scenario + "-hero-only-fails", () =>
        {
            Fixture.NewWorld();
            GlobalSaveData._loaded.prefs.contents[HeroKey] = "not json";
            var run = RunAsync(heroFirst);
            Harness.False(run.Ran, "original refused");
            Harness.Eq(1, run.Callbacks, "failure callback exactly once");
        });
        Harness.Test(scenario + "-bank-only-fails", () =>
        {
            Fixture.NewWorld();
            GlobalSaveData._loaded.prefs.contents[BankKey] = "{broken";
            var run = RunAsync(heroFirst);
            Harness.False(run.Ran, "original refused");
            Harness.Eq(1, run.Callbacks, "failure callback exactly once");
        });
        Harness.Test(scenario + "-both-fail", () =>
        {
            Fixture.NewWorld();
            GlobalSaveData._loaded.prefs.contents[HeroKey] = "not json";
            GlobalSaveData._loaded.prefs.contents[BankKey] = "{broken";
            var run = RunAsync(heroFirst);
            Harness.False(run.Ran, "original refused");
            Harness.Eq(1, run.Callbacks, "failure callback exactly once for two refusals");
        });
        Harness.Test(scenario + "-both-fail-null-callback", () =>
        {
            Fixture.NewWorld();
            GlobalSaveData._loaded.prefs.contents[HeroKey] = "not json";
            GlobalSaveData._loaded.prefs.contents[BankKey] = "{broken";
            var run = RunAsync(heroFirst, nullCallback: true);
            Harness.False(run.Ran, "original refused with null callback");
        });
        Harness.Test(scenario + "-both-fail-throwing-callback", () =>
        {
            Fixture.NewWorld();
            GlobalSaveData._loaded.prefs.contents[HeroKey] = "not json";
            GlobalSaveData._loaded.prefs.contents[BankKey] = "{broken";
            var run = RunAsync(heroFirst, throwingCallback: true);
            Harness.False(run.Ran, "a throwing consumer cannot reopen the original");
            Harness.Eq(1, run.Callbacks, "throwing callback called exactly once");
        });
    }

    /// <summary>Negative control: with both real prefixes refusing, replaying the second one
    /// with a fresh (naive) runOriginal local double-fires the native callback. That is the
    /// pre-convention shape the shared flag removes, so the exactly-once cases above are not
    /// vacuous.</summary>
    private static void NaiveControl(bool heroFirst)
    {
        Harness.Test("combo-async-naive-local-double-fire-" + (heroFirst ? "hero-first" : "bank-first"), () =>
        {
            Fixture.NewWorld();
            GlobalSaveData._loaded.prefs.contents[HeroKey] = "not json";
            GlobalSaveData._loaded.prefs.contents[BankKey] = "{broken";
            int callbacks = 0;
            var callback = new Il2CppSystem.Action<SaveLoadResult>(_ => callbacks++);
            var first = heroFirst ? Prefix(typeof(HeroNativeRights), "AsyncGate") : Prefix(typeof(SharedBankNative), "AsyncGate");
            var second = heroFirst ? Prefix(typeof(SharedBankNative), "AsyncGate") : Prefix(typeof(HeroNativeRights), "AsyncGate");
            object[] firstArgs = { GlobalSaveData._loaded, callback, true };
            first.Invoke(null, firstArgs);
            // A second gate that ignored the shared flag (fresh local) still reports its refusal:
            object[] naiveArgs = { GlobalSaveData._loaded, callback, true };
            second.Invoke(null, naiveArgs);
            Harness.Eq(2, callbacks, "naive independent refusals double-fire");
        });
    }

    private sealed class AsyncRun
    {
        public bool Ran;
        public int Callbacks;
    }

    private static AsyncRun RunAsync(bool heroFirst, bool nullCallback = false, bool throwingCallback = false)
    {
        var global = GlobalSaveData._loaded;
        int callbacks = 0;
        Il2CppSystem.Action<SaveLoadResult> callback = null;
        if (!nullCallback)
            callback = new Il2CppSystem.Action<SaveLoadResult>(_ =>
            {
                callbacks++;
                if (throwingCallback) throw new InvalidOperationException("consumer fault");
            });
        bool runOriginal = true;
        var order = heroFirst
            ? new[] { Prefix(typeof(HeroNativeRights), "AsyncGate"), Prefix(typeof(SharedBankNative), "AsyncGate") }
            : new[] { Prefix(typeof(SharedBankNative), "AsyncGate"), Prefix(typeof(HeroNativeRights), "AsyncGate") };
        foreach (var prefix in order)
        {
            object[] args = { global, callback, runOriginal };
            bool result = (bool)prefix.Invoke(null, args);
            runOriginal = ((bool)args[2]) && result;
        }
        return new AsyncRun { Ran = runOriginal, Callbacks = callbacks };
    }

    // ----------------------------------------------------------------- sync gate combination

    private static void Sync(string scenario, bool heroFirst)
    {
        Harness.Test(scenario + "-both-allow", () =>
        {
            Fixture.NewWorld();
            var run = RunSync(heroFirst);
            Harness.True(run.Ran, "original runs when both gates allow");
            Harness.Eq(0, run.Routine.ReturnWrites, "no gate wrote the boxed return");
            Harness.Eq(0, run.Routine.__1__state, "state untouched");
        });
        Harness.Test(scenario + "-hero-only-fails", () =>
        {
            Fixture.NewWorld();
            GlobalSaveData._loaded.prefs.contents[HeroKey] = "not json";
            var run = RunSync(heroFirst);
            AssertSyncRefusal(run, "hero-only");
        });
        Harness.Test(scenario + "-bank-only-fails", () =>
        {
            Fixture.NewWorld();
            GlobalSaveData._loaded.prefs.contents[BankKey] = "{broken";
            var run = RunSync(heroFirst);
            AssertSyncRefusal(run, "bank-only");
        });
        Harness.Test(scenario + "-both-fail", () =>
        {
            Fixture.NewWorld();
            GlobalSaveData._loaded.prefs.contents[HeroKey] = "not json";
            GlobalSaveData._loaded.prefs.contents[BankKey] = "{broken";
            var run = RunSync(heroFirst);
            AssertSyncRefusal(run, "both");
        });
    }

    private static void AssertSyncRefusal(SyncRun run, string label)
    {
        Harness.False(run.Ran, label + ": original refused");
        Harness.False(run.Result, label + ": __result false");
        Harness.Eq(-1, run.Routine.__1__state, label + ": terminal state");
        Harness.Eq(0x88, (int)run.Routine.@return.value, label + ": boxed failure result");
        Harness.Eq(1, run.Routine.ReturnWrites, label + ": boxed return written exactly once");
    }

    private sealed class SyncRun
    {
        public bool Ran;
        public bool Result;
        public GlobalSaveData._Save_d__89 Routine;
    }

    private static SyncRun RunSync(bool heroFirst)
    {
        var global = GlobalSaveData._loaded;
        var routine = new GlobalSaveData._Save_d__89 { __1__state = 0, __4__this = global };
        bool runOriginal = true;
        bool result = true;
        var order = heroFirst
            ? new[] { Prefix(typeof(HeroNativeRights), "SyncGate"), Prefix(typeof(SharedBankNative), "SyncGate") }
            : new[] { Prefix(typeof(SharedBankNative), "SyncGate"), Prefix(typeof(HeroNativeRights), "SyncGate") };
        foreach (var prefix in order)
        {
            object[] args = { routine, result };
            bool ok = (bool)prefix.Invoke(null, args);
            result = (bool)args[1];
            runOriginal = runOriginal && ok;
        }
        return new SyncRun { Ran = runOriginal, Result = result, Routine = routine };
    }

    private static MethodInfo Prefix(Type owner, string nested)
    {
        var type = owner.GetNestedType(nested, Harness.AnyStatic);
        if (type == null) throw new Exception("missing nested " + owner.Name + "." + nested);
        return type.GetMethod("Prefix", Harness.AnyStatic) ?? throw new Exception("missing Prefix in " + type.FullName);
    }
}
