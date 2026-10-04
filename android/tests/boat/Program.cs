// Scenario checks for the actual android/PatchWorld_BoatCapacity.cs (linked into this project)
// against the minimal typed doubles in Stubs.cs. The host body supplies only the recorded
// native consumption shape — Boat.OnEnable reads the four max fields once before feeding them
// to Embarkable::RegisterUnitSlots — and never reimplements the Prefix/End algorithm or
// models slot registration. Assertions count actual getter/setter/cleanup attempts and pin:
// the four enters must succeed before any state/write, each write records its attempt bit
// first, the single Finalizer End restores only attempted fields under exact int equality,
// cleanup failures stay isolated per field, and the native exception keeps its identity.
// None of this is native/Unity/Harmony gameplay proof.
using System;
using Boat = Il2Cpp.Boat;
using Config = KingdomEnhancedMod.ModConfig;
using MelonLoader;
using Patch = KingdomEnhancedMod.PatchWorld_BoatCapacity;

namespace OhMyMods.AndroidProbe.Tests;

internal static class BoatChecks
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
        Console.WriteLine("BoatTests: boat capacity per-window borrow scenarios (host doubles)");
        DefaultOffAndSessionOff();
        WindowAndRestore();
        GetterFailures();
        WriteFailureBefore();
        WriteFailureAfter();
        NativeExceptionIdentity();
        CleanupIsolation();
        ExternalValueDuringWindow();
        WindowConfigChange();

        Console.WriteLine("boat tests: " + BoatChecks.Passed + " passed / " + BoatChecks.Failed + " failed");
        return BoatChecks.Failed == 0 ? 0 : 1;
    }

    private static Boat SeededBoat()
    {
        var boat = new Boat();
        boat.Workers.Seed(5);
        boat.Knights.Seed(2);
        boat.Pikemen.Seed(7);
        boat.Farmers.Seed(1);
        return boat;
    }

    private static bool NoTouches(Boat boat)
        => boat.Workers.GetterCalls == 0 && boat.Workers.SetterCalls == 0
        && boat.Knights.GetterCalls == 0 && boat.Knights.SetterCalls == 0
        && boat.Pikemen.GetterCalls == 0 && boat.Pikemen.SetterCalls == 0
        && boat.Farmers.GetterCalls == 0 && boat.Farmers.SetterCalls == 0;

    private static bool EnteringValues(Boat boat)
        => boat.Workers.Raw == 5 && boat.Knights.Raw == 2 && boat.Pikemen.Raw == 7 && boat.Farmers.Raw == 1;

    private static void DefaultOffAndSessionOff()
    {
        Config.Enabled.Value = true;
        Config.BoatCapacityEnabled.Value = false;
        var boat = SeededBoat();
        Patch.Prefix(boat, out var state);
        BoatChecks.Check(state == null, "boat capacity OFF (the default) allocates no state");
        BoatChecks.Check(NoTouches(boat), "the OFF path neither reads nor writes any max field");
        BoatChecks.Check(EnteringValues(boat), "the OFF path leaves every field at its entering value");

        Config.BoatCapacityEnabled.Value = true;
        Config.Enabled.Value = false;
        var sessionOff = SeededBoat();
        Patch.Prefix(sessionOff, out var sessionState);
        BoatChecks.Check(sessionState == null && NoTouches(sessionOff), "the session switch OFF keeps the same zero-touch default path");
        BoatChecks.Check(Patch.Finalizer(null, sessionState) == null, "finalizer with no state is a no-op");
        Config.Enabled.Value = true;
    }

    private static void WindowAndRestore()
    {
        Config.Enabled.Value = true;
        Config.BoatCapacityEnabled.Value = true;
        var boat = SeededBoat();
        MelonLogger.LastWarning = null;
        MelonLogger.LastError = null;

        Patch.Prefix(boat, out var state);
        BoatChecks.Check(state != null, "the enabled prefix publishes a borrow");
        BoatChecks.Check(ReferenceEquals(state.Target, boat), "the borrow holds the captured boat reference");
        BoatChecks.Check(state.AttemptWorkers && state.AttemptKnights && state.AttemptPikemen && state.AttemptFarmers,
            "every field's attempt bit is recorded before its write");
        BoatChecks.Check(state.OriginalWorkers == 5 && state.OriginalKnights == 2 && state.OriginalPikemen == 7 && state.OriginalFarmers == 1,
            "the borrow captured the entering values, not the native ctor default 3");
        BoatChecks.Check(boat.Workers.SetterCalls == 1 && boat.Knights.SetterCalls == 1
            && boat.Pikemen.SetterCalls == 1 && boat.Farmers.SetterCalls == 1,
            "each field is written exactly once by the prefix");

        // Recorded native consumption shape: OnEnable reads the four max fields before
        // Embarkable::RegisterUnitSlots; the host body only reads the same four properties.
        int nativeWorkers = boat.maxWorkers;
        int nativeKnights = boat.maxKnights;
        int nativePikemen = boat.maxPikemen;
        int nativeFarmers = boat.maxFarmers;
        BoatChecks.Check(nativeWorkers == 8 && nativeKnights == 6 && nativePikemen == 8 && nativeFarmers == 3,
            "the native window reads the profile targets 8/6/8/3");

        BoatChecks.Check(Patch.Finalizer(null, state) == null, "the success finalizer returns null (no exception)");
        BoatChecks.Check(EnteringValues(boat), "each field is restored to its own entering value");
        BoatChecks.Check(boat.Workers.GetterCalls == 3 && boat.Workers.SetterCalls == 2
            && boat.Knights.GetterCalls == 3 && boat.Knights.SetterCalls == 2
            && boat.Pikemen.GetterCalls == 3 && boat.Pikemen.SetterCalls == 2
            && boat.Farmers.GetterCalls == 3 && boat.Farmers.SetterCalls == 2,
            "each field: one capture read, one native read, one cleanup read; one applied and one restore write");
        BoatChecks.Check(MelonLogger.LastWarning == null && MelonLogger.LastError == null, "the success path logs nothing");
    }

    private static void GetterFailures()
    {
        Config.Enabled.Value = true;
        Config.BoatCapacityEnabled.Value = true;

        var second = SeededBoat();
        second.Knights.ThrowOnGetter = true;
        MelonLogger.LastError = null;
        Patch.Prefix(second, out var secondState);
        BoatChecks.Check(secondState == null, "a failing 2nd getter leaves no borrow state");
        BoatChecks.Check(second.Workers.GetterCalls == 1 && second.Knights.GetterCalls == 1
            && second.Pikemen.GetterCalls == 0 && second.Farmers.GetterCalls == 0,
            "the read phase stops at the failing getter (no later read, no cleanup read)");
        BoatChecks.Check(second.Workers.SetterCalls == 0 && second.Knights.SetterCalls == 0
            && second.Pikemen.SetterCalls == 0 && second.Farmers.SetterCalls == 0,
            "a getter failure performs zero writes on every field");
        BoatChecks.Check(EnteringValues(second), "a getter failure leaves every field untouched");
        BoatChecks.Check(MelonLogger.LastError != null && MelonLogger.LastError.Contains("prefix failed getters"),
            "the getter failure is visible as prefix failed getters");
        BoatChecks.Check(Patch.Finalizer(null, secondState) == null, "the finalizer with the consumed state is a no-op");

        var fourth = SeededBoat();
        fourth.Farmers.ThrowOnGetter = true;
        Patch.Prefix(fourth, out var fourthState);
        BoatChecks.Check(fourthState == null && fourth.Workers.GetterCalls == 1 && fourth.Knights.GetterCalls == 1
            && fourth.Pikemen.GetterCalls == 1 && fourth.Farmers.GetterCalls == 1,
            "a failing 4th getter also stops the read phase with no state");
        BoatChecks.Check(fourth.Workers.SetterCalls == 0 && fourth.Knights.SetterCalls == 0
            && fourth.Pikemen.SetterCalls == 0 && fourth.Farmers.SetterCalls == 0 && EnteringValues(fourth),
            "the 4th getter failure performs zero writes and leaves the fields untouched");
    }

    private static void WriteFailureBefore()
    {
        Config.Enabled.Value = true;
        Config.BoatCapacityEnabled.Value = true;
        var boat = SeededBoat();
        boat.Knights.FailBeforeWrite.Add(1); // the 2nd writer of the sequence throws before its store
        MelonLogger.LastError = null;
        MelonLogger.LastWarning = null;

        Patch.Prefix(boat, out var state);
        BoatChecks.Check(state == null, "the inline cleanup consumes the state after the 2nd writer fails before its write");
        BoatChecks.Check(boat.Workers.SetterCalls == 2 && boat.Workers.Raw == 5,
            "the 1st (attempted) field is written and then restored to its entering value");
        BoatChecks.Check(boat.Knights.SetterCalls == 1 && boat.Knights.Raw == 2,
            "the failed write is attempted exactly once and never lands (no retry, no compensation)");
        BoatChecks.Check(boat.Pikemen.SetterCalls == 0 && boat.Pikemen.GetterCalls == 1 && boat.Pikemen.Raw == 7
            && boat.Farmers.SetterCalls == 0 && boat.Farmers.GetterCalls == 1 && boat.Farmers.Raw == 1,
            "unattempted fields get no applied write and no cleanup read (capture read only)");
        BoatChecks.Check(boat.Knights.GetterCalls == 2, "the failed field is read once for capture and once for cleanup");
        BoatChecks.Check(MelonLogger.LastError != null && MelonLogger.LastError.Contains("prefix failed writes"),
            "the write failure is visible as prefix failed writes");
        BoatChecks.Check(MelonLogger.LastWarning != null && MelonLogger.LastWarning.Contains("maxKnights")
            && MelonLogger.LastWarning.Contains("differs from applied"),
            "the never-applied value is kept with a visible skip warning (no writer identity claimed)");
        BoatChecks.Check(Patch.Finalizer(null, state) == null, "no debt remains for the finalizer (no retry)");
    }

    private static void WriteFailureAfter()
    {
        Config.Enabled.Value = true;
        Config.BoatCapacityEnabled.Value = true;
        var boat = SeededBoat();
        boat.Knights.FailAfterWrite.Add(1); // the 2nd writer stores, then throws
        MelonLogger.LastWarning = null;

        Patch.Prefix(boat, out var state);
        BoatChecks.Check(state == null, "the inline cleanup consumes the state after the 2nd writer fails after its write");
        BoatChecks.Check(boat.Workers.SetterCalls == 2 && boat.Workers.Raw == 5, "the 1st attempted field is written and restored");
        BoatChecks.Check(boat.Knights.SetterCalls == 2 && boat.Knights.Raw == 2,
            "the landed applied write is restored by the single cleanup write (one attempt)");
        BoatChecks.Check(boat.Pikemen.SetterCalls == 0 && boat.Pikemen.Raw == 7 && boat.Farmers.SetterCalls == 0 && boat.Farmers.Raw == 1,
            "unattempted fields receive no write at all");
        BoatChecks.Check(boat.Knights.GetterCalls == 2 && boat.Workers.GetterCalls == 2,
            "cleanup reads each attempted field exactly once");
        BoatChecks.Check(MelonLogger.LastWarning == null, "the current==applied restore branch needs no skip warning");
        BoatChecks.Check(Patch.Finalizer(null, state) == null, "the finalizer has no remaining debt");
    }

    private static void NativeExceptionIdentity()
    {
        Config.Enabled.Value = true;
        Config.BoatCapacityEnabled.Value = true;
        var boat = SeededBoat();
        Patch.Prefix(boat, out var state);

        var native = new InvalidOperationException("native body failure");
        var returned = Patch.Finalizer(native, state);
        BoatChecks.Check(ReferenceEquals(returned, native), "the finalizer returns the original native exception object");
        BoatChecks.Check(boat.Workers.GetterCalls == 2 && boat.Workers.SetterCalls == 2
            && boat.Knights.GetterCalls == 2 && boat.Knights.SetterCalls == 2
            && boat.Pikemen.GetterCalls == 2 && boat.Pikemen.SetterCalls == 2
            && boat.Farmers.GetterCalls == 2 && boat.Farmers.SetterCalls == 2,
            "the native-exception path performs exactly one cleanup read/write per field");
        BoatChecks.Check(EnteringValues(boat), "the native-exception path restores every field");
    }

    private static void CleanupIsolation()
    {
        Config.Enabled.Value = true;
        Config.BoatCapacityEnabled.Value = true;
        var boat = SeededBoat();
        Patch.Prefix(boat, out var state);
        boat.Workers.ThrowOnGetter = true;      // 1st cleanup read fails
        boat.Pikemen.FailAfterWrite.Add(2);     // 3rd cleanup restore stores, then throws
        MelonLogger.LastError = null;

        var native = new InvalidOperationException("cleanup failures");
        var returned = Patch.Finalizer(native, state);
        BoatChecks.Check(ReferenceEquals(returned, native), "cleanup failures never replace the native exception object");
        BoatChecks.Check(boat.Workers.GetterCalls == 2 && boat.Workers.SetterCalls == 1 && boat.Workers.Raw == 8,
            "the failing cleanup read is attempted once, writes nothing, keeps the applied value");
        BoatChecks.Check(boat.Knights.Raw == 2 && boat.Knights.SetterCalls == 2,
            "an independent field is still restored after another field's cleanup failure");
        BoatChecks.Check(boat.Pikemen.Raw == 7 && boat.Pikemen.SetterCalls == 2,
            "the failing cleanup setter stored the original on its single attempt (no retry)");
        BoatChecks.Check(boat.Farmers.Raw == 1 && boat.Farmers.SetterCalls == 2,
            "the trailing field is still processed after the setter failure");
        BoatChecks.Check(MelonLogger.LastError != null && MelonLogger.LastError.Contains("cleanup failed"),
            "the cleanup failures are visible as cleanup failed");
    }

    private static void ExternalValueDuringWindow()
    {
        Config.Enabled.Value = true;
        Config.BoatCapacityEnabled.Value = true;

        var boat = SeededBoat();
        Patch.Prefix(boat, out var state);
        boat.Knights.Seed(99); // external/native write during the window, different from applied
        MelonLogger.LastWarning = null;
        Patch.Finalizer(null, state);
        BoatChecks.Check(boat.Knights.Raw == 99 && boat.Knights.SetterCalls == 1,
            "an external in-window different value is kept: no restore write");
        BoatChecks.Check(MelonLogger.LastWarning != null && MelonLogger.LastWarning.Contains("maxKnights")
            && MelonLogger.LastWarning.Contains("differs from applied"),
            "the kept-different-value outcome is visible; the log never names the writer");
        BoatChecks.Check(boat.Workers.Raw == 5 && boat.Workers.SetterCalls == 2,
            "independent fields still settle normally around the external write");

        var same = SeededBoat();
        Patch.Prefix(same, out var sameState);
        same.Knights.Seed(6); // external write with the same value as applied: indistinguishable
        MelonLogger.LastWarning = null;
        Patch.Finalizer(null, sameState);
        BoatChecks.Check(same.Knights.Raw == 2 && same.Knights.SetterCalls == 2,
            "a same-value external write is indistinguishable; the value-gated restore returns the original");
        BoatChecks.Check(MelonLogger.LastWarning == null, "the same-value case takes the restore branch (no skip warning)");
    }

    private static void WindowConfigChange()
    {
        Config.Enabled.Value = true;
        Config.BoatCapacityEnabled.Value = true;
        var boat = SeededBoat();
        Patch.Prefix(boat, out var state);
        Config.BoatCapacityEnabled.Value = false; // switch off mid-window
        Patch.Finalizer(null, state);
        BoatChecks.Check(boat.Workers.SetterCalls == 2 && EnteringValues(boat),
            "a mid-window config switch does not cancel the captured cleanup");

        var disabled = SeededBoat();
        Patch.Prefix(disabled, out var disabledState);
        BoatChecks.Check(disabledState == null && NoTouches(disabled), "after the switch, new calls touch nothing");

        Config.BoatCapacityEnabled.Value = true;
        var session = SeededBoat();
        Patch.Prefix(session, out var sessionState);
        Config.Enabled.Value = false; // session switch off mid-window
        Patch.Finalizer(null, sessionState);
        BoatChecks.Check(session.Workers.SetterCalls == 2 && EnteringValues(session),
            "a mid-window session switch also keeps the captured cleanup responsibility");
        Config.Enabled.Value = true;
    }
}
