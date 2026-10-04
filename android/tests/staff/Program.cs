// Scenario checks for the actual android/PatchDivine_StaffCooldown.cs (linked into this
// project) against the minimal typed doubles in Stubs.cs, plus the real MobilePlayerMenu rows
// and the real MobilePlayerConfig cycle driven through captured UnityEngine GUI calls. The host
// models only the recorded native consumption shapes — MoveNext state 0 schedules
// Time + borrowed _itemCooldown + additive term; CanCancel compares remaining > _itemCooldown —
// and never reimplements the Prefix/End algorithm. Assertions count actual getter/setter/cleanup
// attempts and pin: the borrow factor on two distinct currents, the additive window equality
// with the m=1 native formula, the default/disabled/non-Hermes/state-1 zero-touch paths, the
// partial-setter and cleanup failure boundaries with native-exception identity, the single
// restore write under exact float equality, and the log-failure boundary swallowing. None of
// this is Unity/Harmony/IL2CPP runtime or native proof.
using System;
using System.Collections.Generic;
using Cooldown = KingdomEnhancedMod.PatchDivine_StaffCooldown;
using Config = KingdomEnhancedMod.ModConfig;
using HermesStaff = Il2Cpp.HermesStaff;
using ItemOfPower = Il2Cpp.ItemOfPower;
using MelonLoader;
using Routine = Il2Cpp.HermesStaff._StartAbilityRoutine_d__17;

namespace OhMyMods.AndroidProbe.Tests;

internal static class StaffChecks
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
        Console.WriteLine("StaffTests: staff base cooldown per-call borrow + Player menu scenarios (host doubles)");
        Config.Initialize(static _ => { });
        KingdomEnhancedMod.KingdomEnhancedPlugin.Initialize();
        Config.Enabled.Value = true;

        RoutineBorrow();
        RoutineSecondCurrent();
        CancelWindow();
        CancelNonHermes();
        DefaultZeroTouch();
        StateGateAndOwner();
        PrefixGetterFailure();
        PrefixLogCallFailureBounded();
        PrefixSetterFailureAfterWrite();
        PrefixSetterFailureBeforeWrite();
        CleanupGetterFailureIdentity();
        CleanupLogCallFailureBounded();
        CleanupSetterFailureIdentity();
        NativeExceptionSingleEnd();
        ExternalDifferentWriteKept();
        ExternalSameValueWrite();
        NanAppliedValue();
        MenuRowsAndBack();

        Console.WriteLine("staff tests: " + StaffChecks.Passed + " passed / " + StaffChecks.Failed + " failed");
        return StaffChecks.Failed == 0 ? 0 : 1;
    }

    private static HermesStaff NewStaff(float seed, float multiplier)
    {
        var staff = new HermesStaff();
        staff.Seed(seed);
        Config.Enabled.Value = true;
        Config.StaffCooldownMultiplier.Value = multiplier;
        return staff;
    }

    private static Routine StateZero(HermesStaff owner)
        => new Routine { __1__state = 0, __4__this = owner };

    // The once-per-key dedup is private production state; the suite resets it between
    // log-boundary scenarios so both the visible-log and the log-call-failure paths can be
    // exercised. Test-only seam; failure to reset fails the suite loudly.
    private static void ResetLoggedErrors()
    {
        var field = typeof(KingdomEnhancedMod.PatchDivine_StaffCooldown).GetField("LoggedErrors",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        ((HashSet<string>)field.GetValue(null)).Clear();
    }

    private static void RoutineBorrow()
    {
        var staff = NewStaff(30f, 0.2f);
        MelonLogger.LastWarning = null;
        MelonLogger.LastError = null;

        Cooldown.RoutinePrefix(StateZero(staff), out var state);
        StaffChecks.Check(state != null, "state-0 MoveNext borrow creates a state");
        StaffChecks.Check(staff.Raw == 6f, "the native schedule reads the scaled current base (30*0.2=6, no hard 30)");
        StaffChecks.Check(staff.GetterCalls == 1 && staff.SetterCalls == 1, "the borrow reads once and writes the applied value once");

        // Recorded native shape: nextActivationTime = Time.time + _itemCooldown + addPerTroll*min(...);
        // the additive term must stay native (30*0.2 + 40 => 46, never 0.2*(30+40) = 14).
        const float t0 = 1000f;
        float scheduled = t0 + staff.Raw + 40f;
        StaffChecks.Check(scheduled - t0 == 46f, "additive per-target time is not scaled (30*0.2+40 yields 46, not 14)");

        StaffChecks.Check(Cooldown.Finalizer(null, state) == null, "success finalizer returns null (no exception)");
        StaffChecks.Check(staff.Raw == 30f, "the steady field is restored to the entering base (30)");
        StaffChecks.Check(staff.GetterCalls == 2 && staff.SetterCalls == 2,
            "success path performs exactly one cleanup read and one restore write (no duplicate cleanup)");
        StaffChecks.Check(MelonLogger.LastWarning == null && MelonLogger.LastError == null, "success path logs nothing");
    }

    private static void RoutineSecondCurrent()
    {
        var staff = NewStaff(12f, 0.6f);
        Cooldown.RoutinePrefix(StateZero(staff), out var state);
        StaffChecks.Check(staff.Raw == 12f * 0.6f && Math.Abs(staff.Raw - 7.2f) < 1e-4f,
            "a second current scales on its own entry value (12*0.6 float32 = 7.2)");
        Cooldown.Finalizer(null, state);
        StaffChecks.Check(staff.Raw == 12f && staff.SetterCalls == 2, "the second staff settles back to 12");
    }

    private static void CancelWindow()
    {
        var staff = NewStaff(30f, 0.2f);
        staff.NativeType = typeof(HermesStaff); // base-typed proxy whose native identity is Hermes
        const float t0 = 200f;
        float next = t0 + 6f + 40f; // the state-0 schedule with the borrowed base
        float now = t0 + 35f;

        Cooldown.CanCancelPrefix(staff, out var state);
        StaffChecks.Check(state != null && staff.Raw == 6f, "CanCancel borrows the same scaled base for the compare");
        float remaining = next - now;
        bool hooked = remaining > staff.Raw;
        float originalRemaining = (t0 + 30f + 40f) - now;
        bool original = originalRemaining > 30f;
        StaffChecks.Check(hooked == original, "the borrow keeps the m=1 additive window (a > elapsed): both sides cancel");
        StaffChecks.Check(!(remaining > 30f), "without the same-window borrow the additive window is squeezed (11 > 30 false)");
        Cooldown.Finalizer(null, state);
        StaffChecks.Check(staff.Raw == 30f && staff.GetterCalls == 2 && staff.SetterCalls == 2, "the cancel query restores the base and counts one borrow+one restore");

        Cooldown.CanCancelPrefix(staff, out var lateState);
        float lateRemaining = next - (t0 + 45f);
        float lateOriginalRemaining = (t0 + 30f + 40f) - (t0 + 45f);
        StaffChecks.Check((lateRemaining > staff.Raw) == (lateOriginalRemaining > 30f),
            "past the additive window both the hooked and the m=1 formula refuse to cancel");
        Cooldown.Finalizer(null, lateState);
        StaffChecks.Check(staff.Raw == 30f, "the late query settles back");
    }

    private static void CancelNonHermes()
    {
        var other = new ItemOfPower();
        other.Seed(30f);
        Config.StaffCooldownMultiplier.Value = 0.2f;
        Cooldown.CanCancelPrefix(other, out var state);
        StaffChecks.Check(state == null && other.GetterCalls == 0 && other.SetterCalls == 0 && other.Raw == 30f,
            "a non-Hermes item never borrows (TryCast identity, not managed is)");
        StaffChecks.Check(other.TryCastCalls == 1, "the non-Hermes query still performs exactly one native identity check");
    }

    private static void DefaultZeroTouch()
    {
        Config.Enabled.Value = true;
        Config.StaffCooldownMultiplier.Value = 1f;
        var staff = new HermesStaff();
        staff.Seed(30f);
        staff.NativeType = typeof(HermesStaff);

        // The config short-circuit must run before the native state/owner/proxy reads: the
        // state, owner and TryCast getters below throw if touched at all.
        var routine = new Routine { __1__state = 0, __4__this = staff, ThrowOnStateGet = true, ThrowOnOwnerGet = true };
        staff.ThrowOnTryCast = true;
        Cooldown.RoutinePrefix(routine, out var routineState);
        Cooldown.CanCancelPrefix(staff, out var cancelState);
        StaffChecks.Check(routineState == null && cancelState == null && staff.GetterCalls == 0 && staff.SetterCalls == 0,
            "multiplier 1 allocates no state and neither reads nor writes the field on either hook");
        StaffChecks.Check(routine.StateReads == 0 && routine.OwnerReads == 0 && staff.TryCastCalls == 0,
            "multiplier 1 returns before the state, owner and TryCast reads (throwing getters not touched)");

        Config.Enabled.Value = false;
        Config.StaffCooldownMultiplier.Value = 0.2f;
        Cooldown.RoutinePrefix(routine, out var disabledRoutine);
        Cooldown.CanCancelPrefix(staff, out var disabledCancel);
        StaffChecks.Check(disabledRoutine == null && disabledCancel == null && staff.GetterCalls == 0 && staff.SetterCalls == 0,
            "session switch OFF allocates no state and touches no field");
        StaffChecks.Check(routine.StateReads == 0 && routine.OwnerReads == 0 && staff.TryCastCalls == 0,
            "session switch OFF returns before the state, owner and TryCast reads");
        Config.Enabled.Value = true;
    }

    private static void StateGateAndOwner()
    {
        Config.StaffCooldownMultiplier.Value = 0.2f;
        var staff = new HermesStaff();
        staff.Seed(30f);

        var running = new Routine { __1__state = 1, __4__this = staff, ThrowOnOwnerGet = true };
        Cooldown.RoutinePrefix(running, out var runningState);
        var finished = new Routine { __1__state = -1, __4__this = staff, ThrowOnOwnerGet = true };
        Cooldown.RoutinePrefix(finished, out var finishedState);
        StaffChecks.Check(runningState == null && finishedState == null && staff.GetterCalls == 0 && staff.SetterCalls == 0,
            "non-zero states return before the owner read and touch nothing");
        StaffChecks.Check(running.StateReads == 1 && finished.StateReads == 1 && running.OwnerReads == 0 && finished.OwnerReads == 0,
            "non-zero states are read exactly once and the throwing owner getter is never reached");

        var ownerless = new Routine { __1__state = 0, __4__this = null };
        Cooldown.RoutinePrefix(ownerless, out var ownerlessState);
        Cooldown.RoutinePrefix(null, out var nullState);
        StaffChecks.Check(ownerlessState == null && nullState == null && staff.SetterCalls == 0,
            "a missing owner or instance leaves no state and writes nothing");
    }

    private static void PrefixGetterFailure()
    {
        ResetLoggedErrors();
        var staff = NewStaff(30f, 0.2f);
        staff.ThrowOnGetter = true;
        MelonLogger.LastError = null;

        Cooldown.RoutinePrefix(StateZero(staff), out var state);
        StaffChecks.Check(state == null, "a failing borrow read leaves no state");
        StaffChecks.Check(staff.GetterCalls == 1 && staff.SetterCalls == 0, "the failed read performs no write and no cleanup read");
        StaffChecks.Check(MelonLogger.LastError != null && MelonLogger.LastError.Contains("prefix failed"), "the failed read is logged as prefix failed");
        StaffChecks.Check(Cooldown.Finalizer(null, state) == null, "finalizer with the consumed state is a no-op");
        staff.ThrowOnGetter = false;
    }

    private static void PrefixLogCallFailureBounded()
    {
        // The logger call itself fails (the exception's ToString throws inside the message
        // build): the single error boundary must swallow it without an escaping exception or a
        // replaced state.
        ResetLoggedErrors();
        var staff = NewStaff(30f, 0.2f);
        staff.ThrowOnGetter = true;
        staff.GetterException = new ThrowingToStringException();
        MelonLogger.LastError = null;

        Cooldown.RoutinePrefix(StateZero(staff), out var state);
        StaffChecks.Check(state == null, "a log-call failure inside the prefix boundary still consumes the state");
        StaffChecks.Check(MelonLogger.LastError == null, "the log-call failure produced no log line (and did not escape)");
        staff.GetterException = null;
        staff.ThrowOnGetter = false;
    }

    private static void PrefixSetterFailureAfterWrite()
    {
        var staff = NewStaff(30f, 0.2f);
        staff.FailAfterWrite.Add(1); // the applied write stores, then throws
        Cooldown.RoutinePrefix(StateZero(staff), out var state);
        StaffChecks.Check(state == null, "an after-write setter failure consumes the state (finalizer no-ops)");
        StaffChecks.Check(staff.SetterCalls == 2, "one failed applied write plus one restore attempt, no retry");
        StaffChecks.Check(staff.GetterCalls == 2, "inline cleanup reads the field exactly once");
        StaffChecks.Check(staff.Raw == 30f, "the inline cleanup recovers the original when the failed write had landed");
        StaffChecks.Check(Cooldown.Finalizer(null, state) == null && staff.SetterCalls == 2, "no debt remains for the finalizer");
    }

    private static void PrefixSetterFailureBeforeWrite()
    {
        var staff = NewStaff(30f, 0.2f);
        staff.FailBeforeWrite.Add(1); // the applied write throws before the store
        MelonLogger.LastWarning = null;
        Cooldown.RoutinePrefix(StateZero(staff), out var state);
        StaffChecks.Check(state == null && staff.SetterCalls == 1, "a before-write setter failure is attempted once and consumes the state");
        StaffChecks.Check(staff.Raw == 30f && staff.GetterCalls == 2, "the field stays original and cleanup reads it exactly once");
        StaffChecks.Check(MelonLogger.LastWarning != null && MelonLogger.LastWarning.Contains("differs from applied"),
            "the kept-different-value cleanup is visible in the log");
    }

    private static void CleanupGetterFailureIdentity()
    {
        ResetLoggedErrors();
        var staff = NewStaff(30f, 0.2f);
        Cooldown.RoutinePrefix(StateZero(staff), out var state);
        staff.ThrowOnGetter = true; // the cleanup read fails; the borrow read already succeeded
        var native = new InvalidOperationException("cleanup read failure");
        MelonLogger.LastError = null;
        var returned = Cooldown.Finalizer(native, state);
        StaffChecks.Check(ReferenceEquals(returned, native), "a cleanup read failure keeps the native exception identity");
        StaffChecks.Check(staff.SetterCalls == 1 && staff.GetterCalls == 2,
            "the failed cleanup read performs no restore write and exactly one attempted read");
        StaffChecks.Check(MelonLogger.LastError != null && MelonLogger.LastError.Contains("cleanup failed"), "the cleanup read failure is logged once");
        staff.ThrowOnGetter = false;
    }

    private static void CleanupLogCallFailureBounded()
    {
        ResetLoggedErrors();
        var staff = NewStaff(30f, 0.2f);
        Cooldown.RoutinePrefix(StateZero(staff), out var state);
        staff.ThrowOnGetter = true;
        staff.GetterException = new ThrowingToStringException();
        var native = new InvalidOperationException("cleanup read failure with a failing log call");
        MelonLogger.LastError = null;
        var returned = Cooldown.Finalizer(native, state);
        StaffChecks.Check(ReferenceEquals(returned, native), "a failing log call inside cleanup still returns the native exception object");
        StaffChecks.Check(MelonLogger.LastError == null && staff.SetterCalls == 1, "the failing log call produced no line and no write");
        staff.GetterException = null;
        staff.ThrowOnGetter = false;
    }

    private static void CleanupSetterFailureIdentity()
    {
        var staff = NewStaff(30f, 0.2f);
        Cooldown.RoutinePrefix(StateZero(staff), out var state);
        staff.FailAfterWrite.Add(2); // the restore write stores the original, then throws
        var native = new InvalidOperationException("cleanup setter failure");
        var returned = Cooldown.Finalizer(native, state);
        StaffChecks.Check(ReferenceEquals(returned, native), "a cleanup setter failure keeps the native exception identity");
        StaffChecks.Check(staff.SetterCalls == 2 && staff.Raw == 30f,
            "the restore setter is attempted exactly once and the stored original remains");
    }

    private static void NativeExceptionSingleEnd()
    {
        var staff = NewStaff(30f, 0.2f);
        Cooldown.RoutinePrefix(StateZero(staff), out var state);
        var native = new InvalidOperationException("native body failure");
        MelonLogger.LastWarning = null;
        StaffChecks.Check(ReferenceEquals(Cooldown.Finalizer(native, state), native), "finalizer returns the original exception object");
        StaffChecks.Check(staff.Raw == 30f && staff.SetterCalls == 2 && staff.GetterCalls == 2, "the native exception path performs exactly one cleanup");
        // A hypothetical duplicate finalizer for the same state must not add a write (value gate);
        // the extra attempt stays visible in the read count.
        StaffChecks.Check(ReferenceEquals(Cooldown.Finalizer(native, state), native) && staff.SetterCalls == 2 && staff.GetterCalls == 3,
            "a duplicate cleanup attempt adds no write and is visible in the read count");
        StaffChecks.Check(MelonLogger.LastWarning != null && MelonLogger.LastWarning.Contains("differs from applied"),
            "the duplicate attempt sees current != applied and only warns");
    }

    private static void ExternalDifferentWriteKept()
    {
        var staff = NewStaff(30f, 0.2f);
        Cooldown.RoutinePrefix(StateZero(staff), out var state);
        staff.Seed(7f); // external write during the window
        MelonLogger.LastWarning = null;
        Cooldown.Finalizer(null, state);
        StaffChecks.Check(staff.Raw == 7f, "an in-window different value is kept, not overwritten");
        StaffChecks.Check(staff.SetterCalls == 1, "no restore write happens when current differs from applied");
        StaffChecks.Check(MelonLogger.LastWarning != null && MelonLogger.LastWarning.Contains("differs from applied"),
            "the kept-different-value outcome is visible in the log");
    }

    private static void ExternalSameValueWrite()
    {
        var staff = NewStaff(30f, 0.2f);
        Cooldown.RoutinePrefix(StateZero(staff), out var state);
        staff.Seed(6f); // external same-value write: value-indistinguishable from the borrow
        MelonLogger.LastWarning = null;
        Cooldown.Finalizer(null, state);
        // Observable only: the value-gated restore behaves exactly as if no external write
        // happened. No claim is made that the writer or a same-value write was recognized.
        StaffChecks.Check(staff.Raw == 30f && staff.SetterCalls == 2,
            "a same-value write is indistinguishable: value-gated restore yields the original (30)");
        StaffChecks.Check(MelonLogger.LastWarning == null, "the same-value case takes the restore branch (no skip log)");
    }

    private static void NanAppliedValue()
    {
        var staff = NewStaff(float.NaN, 0.2f);
        Cooldown.RoutinePrefix(StateZero(staff), out var state);
        MelonLogger.LastWarning = null;
        Cooldown.Finalizer(null, state);
        StaffChecks.Check(float.IsNaN(staff.Raw) && staff.SetterCalls == 1,
            "NaN != NaN keeps the pathological value with no restore write (no epsilon)");
        StaffChecks.Check(MelonLogger.LastWarning != null && MelonLogger.LastWarning.Contains("differs from applied"),
            "the NaN cleanup is visible in the log");
    }

    private static void MenuRowsAndBack()
    {
        Config.Enabled.Value = true;
        Config.StaffCooldownMultiplier.Value = 1f; // earlier scenarios left a non-default multiplier
        var layout = ProbeTicker.Layout;
        layout.Resize(1280f, 720f);
        layout.PlayerPage = true;
        layout.Expanded = true;
        StaffChecks.Check(Math.Abs(layout.PanelHeight - 562f) < 1e-4f, "player page panel height is 562 (six rows, Back ends at 552)");

        var labelStyle = new UnityEngine.GUIStyle();
        var titleStyle = new UnityEngine.GUIStyle();
        var buttonStyle = new UnityEngine.GUIStyle();
        UnityEngine.GUI.Reset();
        OhMyMods.AndroidProbe.MobilePlayerMenu.Draw(100f, 50f, 1f, labelStyle, titleStyle, buttonStyle);

        UnityEngine.GUI.Record Row(string prefix)
            => UnityEngine.GUI.Records.Find(record => record.IsButton && record.Text.StartsWith(prefix, StringComparison.Ordinal));
        UnityEngine.GUI.Record speed = Row("Speed: ");
        UnityEngine.GUI.Record stamina = Row("Stamina: ");
        UnityEngine.GUI.Record hold = Row("Hold purchase: ");
        UnityEngine.GUI.Record steed = Row("Cooldown: ");
        UnityEngine.GUI.Record staff = Row("Staff base cooldown: ");
        UnityEngine.GUI.Record back = Row("Back");
        StaffChecks.Check(speed != null && speed.Rect.Y == 148f, "speed row stays at y=98");
        StaffChecks.Check(stamina != null && stamina.Rect.Y == 226f, "stamina row stays at y=176");
        StaffChecks.Check(hold != null && hold.Rect.Y == 304f, "hold row stays at y=254");
        StaffChecks.Check(steed != null && steed.Rect.Y == 382f, "steed cooldown row stays at y=332");
        StaffChecks.Check(staff != null && staff.Rect.Y == 460f && staff.Rect.X == 116f && staff.Rect.Width == 248f && staff.Rect.Height == 64f,
            "the staff base cooldown row sits at y=410 with the shipped 248x64 geometry");
        StaffChecks.Check(staff.Text == "Staff base cooldown: 1x", "the staff row states the product wording with the current multiplier");
        StaffChecks.Check(back != null && back.Rect.Y == 538f, "Back moved to y=488");

        // Scripted click on the staff row runs the real config cycle once (one save, one log).
        int saves = MelonPreferencesStub.SaveCalls;
        float before = Config.StaffCooldownMultiplier.Value;
        UnityEngine.GUI.Reset();
        UnityEngine.GUI.ClickText = "Staff base cooldown: 1x";
        OhMyMods.AndroidProbe.MobilePlayerMenu.Draw(100f, 50f, 1f, labelStyle, titleStyle, buttonStyle);
        StaffChecks.Check(before == 1f && Config.StaffCooldownMultiplier.Value == 0.8f,
            "clicking the staff row advances the real cycle 1->0.8");
        StaffChecks.Check(MelonPreferencesStub.SaveCalls == saves + 1, "the staff row click saves exactly once");
        StaffChecks.Check(MelonLogger.LastMessage == "ANDROID_PLAYER_STAFF_COOLDOWN multiplier=0.8", "the click logs the staff multiplier");

        UnityEngine.GUI.Reset();
        UnityEngine.GUI.ClickText = "Back";
        OhMyMods.AndroidProbe.MobilePlayerMenu.Draw(100f, 50f, 1f, labelStyle, titleStyle, buttonStyle);
        StaffChecks.Check(layout.PlayerPage == false, "Back leaves the Player page");
        StaffChecks.Check(Config.StaffCooldownMultiplier.Value == 0.8f && MelonPreferencesStub.SaveCalls == saves + 1,
            "Back does not cycle or save anything");
        layout.PlayerPage = false;
        UnityEngine.GUI.Reset();
    }

    private sealed class ThrowingToStringException : Exception
    {
        public override string ToString() => throw new InvalidOperationException("stub ToString failure");
    }
}
