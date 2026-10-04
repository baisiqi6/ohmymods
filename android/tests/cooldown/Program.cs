// Scenario checks for the actual android/PatchRide_SteedCooldown.cs (linked into this project)
// against the minimal typed doubles in Stubs.cs. The host body supplies only the recorded
// native consumption shapes (base schedules from _cooldown; BuffUnits re-reads after base;
// Glide's insufficient-stamina branch keeps a fixed +3s; SpeedBoost.Deactivate reads the
// cooldown; Buff/Speed coroutines snapshot it synchronously before the first yield) and never
// reimplements the Prefix/End algorithm. Assertions count actual getter/setter/cleanup
// attempts, and the CLR failure-boundary scenarios pin: the native exception object keeps its
// identity through Finalizer, the prefix/cleanup envelopes bound proxy/field failures, and a
// failed owner write never clears a legitimate outer Buff owner. None of this is native proof.
using System;
using BuffUnitsSteedAbility = Il2Cpp.BuffUnitsSteedAbility;
using Cooldown = KingdomEnhancedMod.PatchRide_SteedCooldown;
using Config = KingdomEnhancedMod.ModConfig;
using GlideMovementSteedAbility = Il2Cpp.GlideMovementSteedAbility;
using Il2Cpp;
using MelonLoader;
using SpeedBoostSteedAbility = Il2Cpp.SpeedBoostSteedAbility;
using SteedAbility = Il2Cpp.SteedAbility;
using SummonGhostSteedAbility = Il2Cpp.SummonGhostSteedAbility;

namespace OhMyMods.AndroidProbe.Tests;

internal static class CooldownChecks
{
    internal static int Passed;
    internal static int Failed;

    internal static void Check(bool condition, string what)
    {
        if (condition) { Passed++; return; }
        Failed++;
        Console.WriteLine("FAIL " + what);
    }
}

internal static class Program
{
    private static int Main()
    {
        Console.WriteLine("CooldownTests: steed cooldown per-call borrow scenarios (host doubles)");
        Config.Enabled.Value = true;
        Config.SteedCooldownMultiplier.Value = 0.5f;

        BaseSchedule();
        BuffWindow();
        BuffEarlyReturn();
        NativeExceptionPaths();
        PrefixGetterFailure();
        PrefixSetterFailureBeforeWrite();
        PrefixSetterFailureAfterWrite();
        CleanupGetterFailureIdentity();
        CleanupSetterFailureIdentity();
        CleanupPointerGetFailureKeepsIdentity();
        DefaultPathSkipsProxyRead();
        BaseOwnerPointerFailureBounded();
        BuffOwnerWritePointerFailureKeepsOuterOwner();
        CrossObjectBase();
        FactorOneZeroTouch();
        WindowConfigChange();
        ExternalBeforeFieldChange();
        WindowDifferentWriteKept();
        WindowSameValueWrite();
        GhostExcluded();
        GlidePaths();
        SpeedPaths();
        CoroutineFirstYield();
        NanField();

        Console.WriteLine("cooldown tests: " + CooldownChecks.Passed + " passed / " + CooldownChecks.Failed + " failed");
        return CooldownChecks.Failed == 0 ? 0 : 1;
    }

    private static void BaseSchedule()
    {
        var ability = new SteedAbility();
        ability.Seed(10f);
        Config.SteedCooldownMultiplier.Value = 0.5f;
        MelonLogger.LastWarning = null;
        MelonLogger.LastError = null;

        Cooldown.BasePrefix(ability, out var state);
        CooldownChecks.Check(state != null, "base borrow creates a state");
        CooldownChecks.Check(ability.Raw == 5f, "base native body schedules from the scaled current cooldown (10*0.5=5)");
        CooldownChecks.Check(ability.GetterCalls == 1 && ability.SetterCalls == 1, "base borrow reads once and writes the applied value once");
        CooldownChecks.Check(Cooldown.Finalizer(null, state) == null, "success finalizer returns null (no exception)");
        CooldownChecks.Check(ability.Raw == 10f, "base steady field is restored to the entering value (10)");
        CooldownChecks.Check(ability.GetterCalls == 2 && ability.SetterCalls == 2,
            "success path performs exactly one cleanup read and one restore write (no duplicate cleanup)");
        CooldownChecks.Check(MelonLogger.LastWarning == null && MelonLogger.LastError == null, "success path logs nothing");
    }

    private static void BuffWindow()
    {
        var buff = new BuffUnitsSteedAbility();
        buff.Seed(10f);
        Config.SteedCooldownMultiplier.Value = 0.5f;

        Cooldown.BuffPrefix(buff, out var state);
        CooldownChecks.Check(state != null && state.IsBuff, "buff borrow creates a buff state");
        CooldownChecks.Check(buff.Raw == 5f && buff.SetterCalls == 1, "buff borrow scales the entering value once");

        // Recorded shape: BuffUnitsSteedAbility.Activate calls base SteedAbility.Activate, which
        // reads the same field; the pointer marker must suppress a second multiplication there.
        Cooldown.BasePrefix(buff, out var inner);
        CooldownChecks.Check(inner == null, "base inside the buff window does not borrow again");
        CooldownChecks.Check(buff.Raw == 5f && buff.GetterCalls == 1, "inner base consumes the already scaled 5, not 2.5");
        float rewrite = buff.Raw;
        CooldownChecks.Check(rewrite == 5f, "buff's own re-read/rewrite also consumes the scaled 5");

        CooldownChecks.Check(Cooldown.Finalizer(null, state) == null, "buff finalizer returns null");
        CooldownChecks.Check(buff.Raw == 10f && buff.SetterCalls == 2, "buff steady field restored with exactly one restore write");

        Cooldown.BasePrefix(buff, out var again);
        CooldownChecks.Check(again != null, "owner marker is cleared after cleanup (a later base borrow works again)");
        Cooldown.Finalizer(null, again);
        CooldownChecks.Check(buff.Raw == 10f && buff.SetterCalls == 4, "second borrow settles the same way");
    }

    private static void BuffEarlyReturn()
    {
        var buff = new BuffUnitsSteedAbility();
        buff.Seed(10f);
        Config.SteedCooldownMultiplier.Value = 0.5f;

        Cooldown.BuffPrefix(buff, out var state);
        Cooldown.BasePrefix(buff, out var inner);
        float scheduledByBase = buff.Raw;
        // Early-return branch skips Buff's rewrite; the base schedule must stay 5, never 2.5.
        CooldownChecks.Check(inner == null && scheduledByBase == 5f, "early-return buff keeps the base-scheduled 5 (not 2.5)");
        Cooldown.Finalizer(null, state);
        CooldownChecks.Check(buff.Raw == 10f && buff.SetterCalls == 2, "early-return cleanup restores 10");
    }

    private static void NativeExceptionPaths()
    {
        var ability = new SteedAbility();
        ability.Seed(10f);
        Config.SteedCooldownMultiplier.Value = 0.5f;
        Cooldown.BasePrefix(ability, out var state);
        var native = new InvalidOperationException("native body failure");
        CooldownChecks.Check(ReferenceEquals(Cooldown.Finalizer(native, state), native), "finalizer returns the original exception object");
        CooldownChecks.Check(ability.Raw == 10f && ability.SetterCalls == 2, "native exception path performs exactly one cleanup write");
        CooldownChecks.Check(ability.GetterCalls == 2, "native exception path performs exactly one cleanup read");
        // A hypothetical duplicate finalizer for the same state must not add a write (value gate);
        // the extra attempt stays visible in the read count.
        CooldownChecks.Check(ReferenceEquals(Cooldown.Finalizer(native, state), native) && ability.SetterCalls == 2 && ability.GetterCalls == 3,
            "duplicate cleanup attempt adds no write and is visible in the read count");

        var buff = new BuffUnitsSteedAbility();
        buff.Seed(10f);
        Cooldown.BuffPrefix(buff, out var buffState);
        Cooldown.Finalizer(native, buffState);
        CooldownChecks.Check(buff.Raw == 10f, "buff native exception path restores the field");
        Cooldown.BasePrefix(buff, out var again);
        CooldownChecks.Check(again != null, "buff owner is restored on the exception path");
        Cooldown.Finalizer(null, again);
    }

    private static void PrefixGetterFailure()
    {
        var ability = new SteedAbility();
        ability.Seed(10f);
        ability.ThrowOnGetter = true;
        MelonLogger.LastError = null;
        Cooldown.BasePrefix(ability, out var state);
        CooldownChecks.Check(state == null, "getter failure leaves no borrow state");
        CooldownChecks.Check(ability.GetterCalls == 1 && ability.SetterCalls == 0, "getter failure performs no write and no cleanup read");
        CooldownChecks.Check(ability.Raw == 10f, "getter failure leaves the field untouched");
        CooldownChecks.Check(MelonLogger.LastError != null && MelonLogger.LastError.Contains("prefix failed"), "getter failure is logged as prefix failed");
        CooldownChecks.Check(Cooldown.Finalizer(null, state) == null, "finalizer with the consumed state is a no-op");
        ability.ThrowOnGetter = false;
    }

    private static void PrefixSetterFailureBeforeWrite()
    {
        var ability = new SteedAbility();
        ability.Seed(10f);
        ability.FailBeforeWrite.Add(1);
        MelonLogger.LastWarning = null;
        Cooldown.BasePrefix(ability, out var state);
        CooldownChecks.Check(state == null, "before-write setter failure consumes the state (finalizer no-ops)");
        CooldownChecks.Check(ability.SetterCalls == 1, "the failed applied write is attempted exactly once (no retry)");
        CooldownChecks.Check(ability.Raw == 10f, "field stays original when the write never landed");
        CooldownChecks.Check(ability.GetterCalls == 2, "inline cleanup reads the field exactly once");
        CooldownChecks.Check(MelonLogger.LastWarning != null && MelonLogger.LastWarning.Contains("differs from applied"),
            "kept-different-value cleanup is visible in the log");
        CooldownChecks.Check(Cooldown.Finalizer(null, state) == null && ability.SetterCalls == 1, "no debt remains for the finalizer");
    }

    private static void PrefixSetterFailureAfterWrite()
    {
        var ability = new SteedAbility();
        ability.Seed(10f);
        ability.FailAfterWrite.Add(1);
        Cooldown.BasePrefix(ability, out var state);
        CooldownChecks.Check(state == null, "after-write setter failure also consumes the state");
        CooldownChecks.Check(ability.SetterCalls == 2, "one failed applied write plus one restore attempt, no retry");
        CooldownChecks.Check(ability.Raw == 10f, "restore recovers the original when the failed write had landed");

        // Both the applied and the restore write fail: one inline cleanup attempt only, visible log.
        var stubborn = new SteedAbility();
        stubborn.Seed(10f);
        stubborn.FailAfterWrite.Add(1);
        stubborn.FailAfterWrite.Add(2);
        MelonLogger.LastError = null;
        Cooldown.BasePrefix(stubborn, out var stubbornState);
        CooldownChecks.Check(stubbornState == null && stubborn.SetterCalls == 2,
            "restore-write failure is attempted exactly once (no retry/compensation)");
        CooldownChecks.Check(stubborn.Raw == 10f, "the second failed write had stored the original before throwing");
        CooldownChecks.Check(MelonLogger.LastError != null && MelonLogger.LastError.Contains("cleanup failed"),
            "cleanup failure is logged inside the single End envelope");
    }

    private static void CleanupGetterFailureIdentity()
    {
        var buff = new BuffUnitsSteedAbility();
        buff.Pointer = new IntPtr(0x500);
        buff.Seed(10f);
        Cooldown.BuffPrefix(buff, out var state);
        buff.ThrowOnGetter = true; // the cleanup read fails; the borrow read already succeeded
        var native = new InvalidOperationException("cleanup read failure");
        var returned = Cooldown.Finalizer(native, state);
        CooldownChecks.Check(ReferenceEquals(returned, native), "cleanup read failure keeps the native exception identity");
        CooldownChecks.Check(buff.SetterCalls == 1 && buff.GetterCalls == 2,
            "cleanup read failure performs no restore write and exactly the single attempted read");
        buff.ThrowOnGetter = false;
        Cooldown.BasePrefix(buff, out var again);
        CooldownChecks.Check(again != null, "buff owner is restored even when the cleanup read failed");
        Cooldown.Finalizer(null, again);
    }

    private static void CleanupSetterFailureIdentity()
    {
        var buff = new BuffUnitsSteedAbility();
        buff.Pointer = new IntPtr(0x600);
        buff.Seed(10f);
        Cooldown.BuffPrefix(buff, out var state);
        buff.FailAfterWrite.Add(2); // the restore write stores the original, then throws
        var native = new InvalidOperationException("cleanup setter failure");
        var returned = Cooldown.Finalizer(native, state);
        CooldownChecks.Check(ReferenceEquals(returned, native), "cleanup setter failure keeps the native exception identity");
        CooldownChecks.Check(buff.SetterCalls == 2 && buff.Raw == 10f,
            "restore setter is attempted exactly once and the stored original remains");
        Cooldown.BasePrefix(buff, out var again);
        CooldownChecks.Check(again != null, "buff owner is restored when the cleanup setter fails");
        Cooldown.Finalizer(null, again);
    }

    private static void CleanupPointerGetFailureKeepsIdentity()
    {
        // P1 regression anchor: cleanup must not read the proxy pointer. The old guard threw
        // inside Finalizer and replaced the native exception; with the single End envelope the
        // field is restored and the exception object keeps its identity.
        Config.SteedCooldownMultiplier.Value = 0.5f;
        var ability = new SteedAbility();
        ability.Seed(10f);
        Cooldown.BasePrefix(ability, out var state);
        ability.ThrowOnPointerGet = true; // a throwing proxy read during cleanup
        var native = new InvalidOperationException("cleanup proxy read failure");
        var returned = Cooldown.Finalizer(native, state);
        CooldownChecks.Check(ReferenceEquals(returned, native),
            "cleanup never reads the proxy pointer: the native exception identity survives a throwing pointer get");
        CooldownChecks.Check(ability.Raw == 10f && ability.SetterCalls == 2, "cleanup still restores the field with exactly one write");
        ability.ThrowOnPointerGet = false;
    }

    private static void DefaultPathSkipsProxyRead()
    {
        // No legitimate outer Buff owner: the owner check must short-circuit before touching the
        // proxy pointer, so a throwing pointer get cannot affect the default path.
        Config.SteedCooldownMultiplier.Value = 0.5f;
        var ability = new SteedAbility();
        ability.Seed(10f);
        ability.ThrowOnPointerGet = true;
        Cooldown.BasePrefix(ability, out var state);
        CooldownChecks.Check(state != null && ability.Raw == 5f && ability.SetterCalls == 1,
            "no outer owner: the owner check never reads the proxy pointer and the borrow proceeds");
        Cooldown.Finalizer(null, state);
        CooldownChecks.Check(ability.Raw == 10f, "the borrow settles normally");
    }

    private static void BaseOwnerPointerFailureBounded()
    {
        var buff = new BuffUnitsSteedAbility();
        buff.Pointer = new IntPtr(0x700);
        buff.Seed(10f);
        Cooldown.BuffPrefix(buff, out var buffState); // outer window open, owner = 0x700

        var probe = new SteedAbility();
        probe.Seed(10f);
        probe.ThrowOnPointerGet = true; // a legitimate outer owner exists, so the check reads it
        Cooldown.BasePrefix(probe, out var probeState);
        CooldownChecks.Check(probeState == null, "pointer read failure inside the owner check is bounded by the prefix try");
        CooldownChecks.Check(probe.GetterCalls == 0 && probe.SetterCalls == 0, "the failed owner check touches no field");
        var native = new InvalidOperationException("native body");
        CooldownChecks.Check(ReferenceEquals(Cooldown.Finalizer(native, null), native), "finalizer with no state still returns the native exception");

        Cooldown.BasePrefix(buff, out var skip);
        CooldownChecks.Check(skip == null && buff.Raw == 5f, "the outer buff window is still recognized after the bounded failure");
        Cooldown.Finalizer(null, buffState);
        CooldownChecks.Check(buff.Raw == 10f, "the outer window settles normally");
    }

    private static void BuffOwnerWritePointerFailureKeepsOuterOwner()
    {
        var outer = new BuffUnitsSteedAbility();
        outer.Pointer = new IntPtr(0x800);
        outer.Seed(10f);
        Cooldown.BuffPrefix(outer, out var outerState); // outer window open, owner = 0x800

        var inner = new BuffUnitsSteedAbility();
        inner.Seed(10f);
        inner.ThrowOnPointerGet = true; // the owner write throws before the applied field write
        MelonLogger.LastWarning = null;
        Cooldown.BuffPrefix(inner, out var innerState);
        CooldownChecks.Check(innerState == null, "owner-write pointer failure consumes the inner state");
        CooldownChecks.Check(inner.SetterCalls == 0 && inner.GetterCalls == 2,
            "the applied write never happened; cleanup read the field exactly once");
        CooldownChecks.Check(MelonLogger.LastWarning != null && MelonLogger.LastWarning.Contains("differs from applied"),
            "cleanup after the owner-write failure keeps the original and logs the difference");

        // prevOwner was captured in the state initializer, before the throwing pointer get:
        Cooldown.BasePrefix(outer, out var skip);
        CooldownChecks.Check(skip == null && outer.Raw == 5f, "the outer buff owner survives the inner owner-write failure");
        Cooldown.Finalizer(null, outerState);
        CooldownChecks.Check(outer.Raw == 10f, "the outer window settles normally");
    }

    private static void CrossObjectBase()
    {
        var buff = new BuffUnitsSteedAbility();
        buff.Pointer = new IntPtr(0x100);
        buff.Seed(10f);
        var other = new SteedAbility();
        other.Pointer = new IntPtr(0x200);
        other.Seed(10f);
        Config.SteedCooldownMultiplier.Value = 0.5f;

        Cooldown.BuffPrefix(buff, out var buffState);
        Cooldown.BasePrefix(other, out var otherState);
        CooldownChecks.Check(otherState != null, "another object's base callback still borrows inside the buff window");
        CooldownChecks.Check(other.Raw == 5f && other.SetterCalls == 1, "the other object scales its own current value");
        Cooldown.Finalizer(null, otherState);
        CooldownChecks.Check(other.Raw == 10f, "the other object is restored independently");
        Cooldown.Finalizer(null, buffState);
        CooldownChecks.Check(buff.Raw == 10f, "the buff object is restored independently");
    }

    private static void FactorOneZeroTouch()
    {
        Config.SteedCooldownMultiplier.Value = 1f;
        var ability = new SteedAbility();
        ability.Seed(10f);
        Cooldown.BasePrefix(ability, out var state);
        CooldownChecks.Check(state == null && ability.GetterCalls == 0 && ability.SetterCalls == 0,
            "multiplier 1 allocates no state and neither reads nor writes the field");

        var buff = new BuffUnitsSteedAbility();
        buff.Seed(10f);
        Cooldown.BuffPrefix(buff, out var buffState);
        CooldownChecks.Check(buffState == null && buff.SetterCalls == 0, "multiplier 1 buff path writes nothing");

        Config.SteedCooldownMultiplier.Value = 0.5f;
        Cooldown.BasePrefix(buff, out var afterOne);
        CooldownChecks.Check(afterOne != null, "the 1x buff path left no owner marker behind");
        Cooldown.Finalizer(null, afterOne);

        Config.Enabled.Value = false;
        var disabled = new SteedAbility();
        disabled.Seed(10f);
        Cooldown.BasePrefix(disabled, out var disabledState);
        CooldownChecks.Check(disabledState == null && disabled.GetterCalls == 0 && disabled.SetterCalls == 0,
            "session switch OFF allocates no state and touches no field");
        Config.Enabled.Value = true;
    }

    private static void WindowConfigChange()
    {
        Config.SteedCooldownMultiplier.Value = 0.5f;
        var buff = new BuffUnitsSteedAbility();
        buff.Pointer = new IntPtr(0x300);
        buff.Seed(10f);
        Cooldown.BuffPrefix(buff, out var state);
        Config.SteedCooldownMultiplier.Value = 1f;
        Cooldown.Finalizer(null, state);
        CooldownChecks.Check(buff.Raw == 10f && buff.SetterCalls == 2,
            "mid-window multiplier change does not cancel or duplicate the captured cleanup");
        Config.SteedCooldownMultiplier.Value = 0.5f;
        Cooldown.BasePrefix(buff, out var again);
        CooldownChecks.Check(again != null, "owner is restored although the window ended with the option changed");
        Cooldown.Finalizer(null, again);

        var ability = new SteedAbility();
        ability.Seed(10f);
        Cooldown.BasePrefix(ability, out var disableState);
        Config.Enabled.Value = false;
        Cooldown.Finalizer(null, disableState);
        CooldownChecks.Check(ability.Raw == 10f && ability.SetterCalls == 2, "mid-window session switch-off still settles the captured borrow");
        Config.Enabled.Value = true;
    }

    private static void ExternalBeforeFieldChange()
    {
        Config.SteedCooldownMultiplier.Value = 0.5f;
        var ability = new SteedAbility();
        ability.Seed(10f);
        Cooldown.BasePrefix(ability, out var first);
        Cooldown.Finalizer(null, first);
        ability.Seed(20f); // native/external writer between calls
        Cooldown.BasePrefix(ability, out var second);
        CooldownChecks.Check(second != null && second.Original == 20f && second.Applied == 10f,
            "the next call captures the new external baseline as its multiplicand (20*0.5=10)");
        Cooldown.Finalizer(null, second);
        CooldownChecks.Check(ability.Raw == 20f, "restore returns the new baseline, not the first call's value");
    }

    private static void WindowDifferentWriteKept()
    {
        Config.SteedCooldownMultiplier.Value = 0.5f;
        var ability = new SteedAbility();
        ability.Seed(10f);
        MelonLogger.LastWarning = null;
        Cooldown.BasePrefix(ability, out var state);
        ability.Seed(7f); // external write during the window
        Cooldown.Finalizer(null, state);
        CooldownChecks.Check(ability.Raw == 7f, "an in-window different value is kept, not overwritten");
        CooldownChecks.Check(ability.SetterCalls == 1, "no restore write happens when current differs from applied");
        CooldownChecks.Check(MelonLogger.LastWarning != null && MelonLogger.LastWarning.Contains("differs from applied"),
            "the kept-different-value outcome is visible in the log");
    }

    private static void WindowSameValueWrite()
    {
        Config.SteedCooldownMultiplier.Value = 0.5f;
        var ability = new SteedAbility();
        ability.Seed(10f);
        MelonLogger.LastWarning = null;
        Cooldown.BasePrefix(ability, out var state);
        ability.Seed(5f); // external same-value write: value-indistinguishable from the borrow
        Cooldown.Finalizer(null, state);
        // Observable only: the value-gated restore behaves exactly as if no external write
        // happened. No claim is made that the writer or a same-value write was recognized.
        CooldownChecks.Check(ability.Raw == 10f && ability.SetterCalls == 2,
            "same-value write is indistinguishable: value-gated restore yields the original (10)");
        CooldownChecks.Check(MelonLogger.LastWarning == null, "same-value case takes the restore branch (no skip log)");
    }

    private static void GhostExcluded()
    {
        Config.SteedCooldownMultiplier.Value = 0.5f;
        var ghost = new SteedAbility();
        ghost.Pointer = new IntPtr(0x400);
        ghost.NativeType = typeof(SummonGhostSteedAbility); // base-typed proxy, ghost native identity
        ghost.Seed(10f);
        CooldownChecks.Check(!(ghost is SummonGhostSteedAbility), "the ghost proxy is base-typed: managed is does not match");
        CooldownChecks.Check(ghost.TryCast<SummonGhostSteedAbility>() != null, "its native identity is ghost, so the real TryCast matches");
        Cooldown.BasePrefix(ghost, out var state);
        CooldownChecks.Check(state == null && ghost.GetterCalls == 0 && ghost.SetterCalls == 0 && ghost.Raw == 10f,
            "base ghost entry keeps the cooldown untouched (skipped via TryCast, not managed is)");
    }

    private static void GlidePaths()
    {
        Config.SteedCooldownMultiplier.Value = 0.5f;
        var glide = new GlideMovementSteedAbility();
        glide.Seed(10f);
        Cooldown.GlidePrefix(glide, out var state);
        CooldownChecks.Check(state != null && glide.Raw == 5f, "glide borrow scales the current cooldown");
        // Recorded shape: when the original activation cannot proceed (insufficient stamina),
        // the native body keeps a fixed time+3 schedule and reads no cooldown field; the hook
        // must not bypass that original gate.
        bool activated = false;
        float scheduled = !activated ? 3f : glide.Raw;
        CooldownChecks.Check(scheduled == 3f, "glide insufficient-stamina branch keeps the fixed 3 seconds");
        CooldownChecks.Check(glide.GetterCalls == 1, "the original failure branch reads no cooldown field (gate untouched by the hook)");
        Cooldown.Finalizer(null, state);
        CooldownChecks.Check(glide.Raw == 10f && glide.SetterCalls == 2, "glide steady field restored after the failure path");

        var ready = new GlideMovementSteedAbility();
        ready.Seed(10f);
        Cooldown.GlidePrefix(ready, out var readyState);
        activated = true;
        scheduled = !activated ? 3f : ready.Raw;
        CooldownChecks.Check(scheduled == 5f, "a proceeding glide consumes the scaled cooldown (5)");
        Cooldown.Finalizer(null, readyState);
        CooldownChecks.Check(ready.Raw == 10f, "glide steady field restored after the success path");
    }

    private static void SpeedPaths()
    {
        Config.SteedCooldownMultiplier.Value = 0.5f;
        var speed = new SpeedBoostSteedAbility();
        speed.Seed(10f);
        Cooldown.SpeedPrefix(speed, out var state);
        CooldownChecks.Check(state != null && speed.Raw == 5f, "speed borrow scales the current cooldown");
        bool active = true;
        float next = -1f;
        if (active) next = speed.Raw;
        CooldownChecks.Check(next == 5f, "speed Deactivate consumes the scaled value (5)");
        Cooldown.Finalizer(null, state);
        CooldownChecks.Check(speed.Raw == 10f && speed.SetterCalls == 2, "speed steady field restored");

        var idle = new SpeedBoostSteedAbility();
        idle.Seed(10f);
        Cooldown.SpeedPrefix(idle, out var idleState);
        bool idleActive = false;
        float idleNext = -1f;
        if (idleActive) idleNext = idle.Raw;
        CooldownChecks.Check(idleNext == -1f, "inactive Deactivate keeps the recorded zero next-write shape (hook adds none)");
        Cooldown.Finalizer(null, idleState);
        CooldownChecks.Check(idle.Raw == 10f, "inactive path still settles the borrow");
    }

    private static void CoroutineFirstYield()
    {
        // Recorded shape: the Buff/Speed cooldown coroutines read _cooldown once at state 0,
        // synchronously inside StartCoroutine before the first yield, and snapshot it into
        // WaitForSeconds. No frame loop is simulated; both consumers see the scaled value
        // because the borrow is still open during that synchronous first step.
        Config.SteedCooldownMultiplier.Value = 0.5f;
        var buff = new BuffUnitsSteedAbility();
        buff.Seed(10f);
        Cooldown.BuffPrefix(buff, out var buffState);
        float buffWait = buff.Raw;
        CooldownChecks.Check(buffWait == 5f, "buff coroutine first-yield wait captures the scaled cooldown (5)");
        Cooldown.Finalizer(null, buffState);
        CooldownChecks.Check(buff.Raw == 10f, "buff coroutine window settles to 10");

        var speed = new SpeedBoostSteedAbility();
        speed.Seed(10f);
        Cooldown.SpeedPrefix(speed, out var speedState);
        float speedWait = speed.Raw;
        CooldownChecks.Check(speedWait == 5f, "speed coroutine first-yield wait captures the scaled cooldown (5)");
        Cooldown.Finalizer(null, speedState);
        CooldownChecks.Check(speed.Raw == 10f, "speed coroutine window settles to 10");
    }

    private static void NanField()
    {
        // exact float equality: NaN != NaN, so the value gate refuses to write and keeps the
        // pathological value with a visible log (no epsilon, no guessing).
        Config.SteedCooldownMultiplier.Value = 0.5f;
        var ability = new SteedAbility();
        ability.Seed(float.NaN);
        MelonLogger.LastWarning = null;
        Cooldown.BasePrefix(ability, out var state);
        Cooldown.Finalizer(null, state);
        CooldownChecks.Check(float.IsNaN(ability.Raw), "NaN field keeps the pathological value (no restore write on NaN != NaN)");
        CooldownChecks.Check(ability.SetterCalls == 1, "NaN cleanup adds no write");
        CooldownChecks.Check(MelonLogger.LastWarning != null && MelonLogger.LastWarning.Contains("differs from applied"),
            "NaN cleanup is visible in the log");
    }
}
