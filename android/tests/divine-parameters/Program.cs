using System;
using DivineParameterTests;
using KingdomEnhancedMod;
using Il2Cpp;
internal static class Program
{
    private static int failures, assertions;
    private static void Check(bool condition, string label)
    {
        assertions++; Console.WriteLine((condition ? "PASS " : "FAIL ") + label);
        if (!condition) failures++;
    }
#if !LEGACY_RED
    private static void Reset()
    {
        NativeMeter.Reads = 0; NativeMeter.Throw = false;
        ModConfig.Enabled = new Entry<bool> { Raw = true };
        ModConfig.DivineParametersEnabled = new Entry<bool> { Raw = true };
        NetworkBigBoss.Online = false; NetworkBigBoss.Auth = true;
        BiomeHolder.Current = new BiomeHolder { Index = 0 };
        Managers.Current = new Managers { world = new World() };
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
    }
    private static T Current<T>(T component) where T : UnityEngine.Component
    { component.Transform.Parent = Managers.Current.world.gameLayer; return component; }
    private static HermesStaff._StartAbilityRoutine_d__17 Routine(HermesStaff staff, int state = 0)
    { return new HermesStaff._StartAbilityRoutine_d__17 { Owner = staff, State = state }; }
    private static void Gates()
    {
        for (int mode = 0; mode < 6; mode++)
        {
            Reset(); var staff = Current(new HermesStaff()); var arrow = Current(new ArtemisArrow());
            var troll = Current(new FriendlyTroll()); var routine = Routine(staff);
            if (mode == 0) ModConfig.Enabled.Raw = false;
            if (mode == 1) ModConfig.DivineParametersEnabled.Raw = false;
            if (mode == 2) ModConfig.Enabled = null;
            if (mode == 3) ModConfig.DivineParametersEnabled = null;
            if (mode == 4) ModConfig.Enabled.Throw = true;
            if (mode == 5) ModConfig.DivineParametersEnabled.Throw = true;
            NativeMeter.Throw = true; bool result = true;
            PatchDivine_HermesStaff.RoutinePrefix(routine, out var hs);
            PatchDivine_Artemis.DamageAffectedEnemies_Prefix(arrow, out var ar);
            bool run = PatchDivine_HermesStaff.FriendlyTroll_ShouldRevertToTroll_Prefix(troll, ref result);
            Check(NativeMeter.Reads == 0, "gate " + mode + " all handlers zero native");
            Check(hs == null && ar == null && run && result, "gate " + mode + " native path and no loan");
            Check(staff.Max.Raw == 8 && arrow.Max.Raw == 6, "gate " + mode + " fields unchanged");
        }
    }
    private static void Authority()
    {
        for (int mode = 0; mode < 7; mode++)
        {
            Reset(); var staff = Current(new HermesStaff()); var arrow = Current(new ArtemisArrow());
            var troll = Current(new FriendlyTroll());
            if (mode == 0) NetworkBigBoss.Online = true;
            if (mode == 1) NetworkBigBoss.Auth = false;
            if (mode == 2) BiomeHolder.Current.Index = -1;
            if (mode == 3) Managers.Current.world.gameLayer.GameObject.Active = false;
            if (mode == 4) { staff.GameObject.SceneHandle = 10; arrow.GameObject.SceneHandle = 10; troll.GameObject.SceneHandle = 10; }
            if (mode == 5) { staff.Transform.Parent = null; arrow.Transform.Parent = null; troll.Transform.Parent = null; }
            if (mode == 6) { staff.GameObject.Active = false; arrow.GameObject.Active = false; troll.GameObject.Active = false; }
            PatchDivine_HermesStaff.RoutinePrefix(Routine(staff), out var hs);
            PatchDivine_Artemis.DamageAffectedEnemies_Prefix(arrow, out var ar); bool result = true;
            bool run = PatchDivine_HermesStaff.FriendlyTroll_ShouldRevertToTroll_Prefix(troll, ref result);
            Check(hs == null && ar == null && run && result, "authority " + mode + " excluded consistently");
            Check(staff.Max.Writes == 0 && arrow.Max.Writes == 0, "authority " + mode + " no parameter write");
        }
        Reset(); BiomeHolder.Current.Index = 4; var friendly = Current(new FriendlyTroll()); bool value = true;
        Check(!PatchDivine_HermesStaff.FriendlyTroll_ShouldRevertToTroll_Prefix(friendly, ref value) && !value && friendly.RevertTime == 99,
            "friendly all-world nonnegative biome suppresses revert without field writes");
    }
    private static void Hermes()
    {
        Reset(); var staff = Current(new HermesStaff()); var scanner = staff._trollScanner;
        PatchDivine_HermesStaff.RoutinePrefix(Routine(staff), out var state);
        Check(staff.Max.Raw == 32 && state != null && state.Written, "Hermes state-zero loan exposes 32");
        var nativeException = new InvalidOperationException("native");
        Check(ReferenceEquals(PatchDivine_HermesStaff.Finalizer(nativeException, state), nativeException) && staff.Max.Raw == 8,
            "Hermes exception same reference and original restored");
        Check(staff._itemCooldown == 7 && staff._abilityRange == 5 && staff._nextActivationTime == 123
            && staff._cooldownAddPerTroll == 2 && ReferenceEquals(staff._trollScanner, scanner) && scanner.range == 3 && scanner.rangeBehind == 4,
            "Hermes cooldown range scanner additive activation unchanged");
        int writes = staff.Max.Writes; PatchDivine_HermesStaff.Finalizer(null, state);
        Check(staff.Max.Writes == writes, "Hermes double end does not write");
        var resumed = Routine(staff, 1); resumed.ThrowOwner = true;
        PatchDivine_HermesStaff.RoutinePrefix(resumed, out var skipped);
        Check(skipped == null && resumed.OwnerReads == 0, "Hermes nonzero state does not read owner");
        PatchDivine_HermesStaff.RoutinePrefix(null, out skipped);
        Check(skipped == null, "Hermes null routine no loan");
        PatchDivine_HermesStaff.RoutinePrefix(Routine(staff), out var outer);
        PatchDivine_HermesStaff.RoutinePrefix(Routine(staff), out var inner);
        PatchDivine_HermesStaff.Finalizer(null, inner); PatchDivine_HermesStaff.Finalizer(null, outer);
        Check(inner == null && staff.Max.Raw == 8, "Hermes nested fixed assignment returns outer original");
        staff.Max.Raw = 32; writes = staff.Max.Writes;
        PatchDivine_HermesStaff.RoutinePrefix(Routine(staff), out skipped);
        Check(skipped == null && staff.Max.Writes == writes, "Hermes entering 32 has no write or loan");
        staff.Max.Raw = 8; PatchDivine_HermesStaff.RoutinePrefix(Routine(staff), out state); staff.Max.Raw = 11;
        PatchDivine_HermesStaff.Finalizer(null, state);
        Check(staff.Max.Raw == 11, "Hermes external writer owns changed value");
    }
    private static void Artemis()
    {
        Reset(); var arrow = Current(new ArtemisArrow()); var scanner = arrow._scanner;
        PatchDivine_Artemis.DamageAffectedEnemies_Prefix(arrow, out var state);
        Check(arrow.Max.Raw == 20 && state != null, "Artemis exposes candidate count 20");
        var ex = new Exception("native arrow");
        Check(ReferenceEquals(PatchDivine_Artemis.Finalizer(ex, state), ex) && arrow.Max.Raw == 6
            && arrow._range == 8 && arrow._nextActivationTime == 87 && ReferenceEquals(arrow._scanner, scanner),
            "Artemis restores original preserves unrelated fields and exception identity");
        int writes = arrow.Max.Writes; PatchDivine_Artemis.Finalizer(null, state);
        Check(arrow.Max.Writes == writes, "Artemis double end does not write");
        PatchDivine_Artemis.DamageAffectedEnemies_Prefix(arrow, out var outer);
        PatchDivine_Artemis.DamageAffectedEnemies_Prefix(arrow, out var inner);
        PatchDivine_Artemis.Finalizer(null, inner); PatchDivine_Artemis.Finalizer(null, outer);
        Check(inner == null && arrow.Max.Raw == 6, "Artemis nested fixed assignment restores outer original");
        arrow.Max.Raw = 20; writes = arrow.Max.Writes;
        PatchDivine_Artemis.DamageAffectedEnemies_Prefix(arrow, out state);
        Check(state == null && arrow.Max.Writes == writes, "Artemis entering 20 no write or loan");
        arrow.Max.Raw = 6; PatchDivine_Artemis.DamageAffectedEnemies_Prefix(arrow, out state); arrow.Max.Raw = 17;
        PatchDivine_Artemis.Finalizer(null, state);
        Check(arrow.Max.Raw == 17, "Artemis external writer is not overwritten");
    }
    private static void Faults()
    {
        Reset(); var staff = Current(new HermesStaff()); staff.Max.FailAfter = 1;
        PatchDivine_HermesStaff.RoutinePrefix(Routine(staff), out var hs);
        Check(hs != null && hs.Written && staff.Max.Raw == 32 && KingdomEnhancedPlugin.Instance.LogSource.Errors == 1,
            "Hermes write-after-throw retains complete observable loan");
        PatchDivine_HermesStaff.Finalizer(null, hs);
        Check(staff.Max.Raw == 8 && !hs.Written, "Hermes write-after-throw restored at finalizer");
        Reset(); var arrow = Current(new ArtemisArrow()); arrow.Max.FailAfter = 1;
        PatchDivine_Artemis.DamageAffectedEnemies_Prefix(arrow, out var ar);
        Check(ar != null && ar.Written && arrow.Max.Raw == 20 && KingdomEnhancedPlugin.Instance.LogSource.Errors == 1,
            "Artemis write-after-throw retains complete observable loan");
        PatchDivine_Artemis.Finalizer(null, ar);
        Check(arrow.Max.Raw == 6 && !ar.Written, "Artemis write-after-throw restored at finalizer");
        Reset(); arrow = Current(new ArtemisArrow()); arrow.Max.ThrowRead = true;
        PatchDivine_Artemis.DamageAffectedEnemies_Prefix(arrow, out ar);
        Check(ar == null && arrow.Max.Writes == 0 && KingdomEnhancedPlugin.Instance.LogSource.Errors == 1,
            "prefix field-read failure observable without write");
        Reset(); arrow = Current(new ArtemisArrow());
        PatchDivine_Artemis.DamageAffectedEnemies_Prefix(arrow, out ar); arrow.Max.ThrowRead = true;
        var ex = new Exception("original"); var returned = PatchDivine_Artemis.Finalizer(ex, ar); int reads = arrow.Max.Reads;
        PatchDivine_Artemis.Finalizer(null, ar);
        Check(!ar.Written && ReferenceEquals(returned, ex) && arrow.Max.Reads == reads && KingdomEnhancedPlugin.Instance.LogSource.Errors == 1,
            "end read failure consumed observable and never retried");
        Reset(); arrow = Current(new ArtemisArrow()); arrow.Max.FailBefore = 1;
        PatchDivine_Artemis.DamageAffectedEnemies_Prefix(arrow, out ar); PatchDivine_Artemis.Finalizer(null, ar);
        Check(arrow.Max.Raw == 6 && arrow.Max.Writes == 1 && !ar.Written, "write-before-throw leaves original and no restoration write");
        Reset(); arrow = Current(new ArtemisArrow()); arrow.Max.FailAfter = 2;
        PatchDivine_Artemis.DamageAffectedEnemies_Prefix(arrow, out ar); PatchDivine_Artemis.Finalizer(null, ar);
        PatchDivine_Artemis.Finalizer(null, ar);
        Check(arrow.Max.Raw == 6 && arrow.Max.Writes == 2 && KingdomEnhancedPlugin.Instance.LogSource.Errors == 1,
            "end setter throw observable with no second restore attempt");
        Reset(); arrow = Current(new ArtemisArrow()); arrow.Max.FailAfter = 1; KingdomEnhancedPlugin.Instance.LogSource.Throw = true;
        PatchDivine_Artemis.DamageAffectedEnemies_Prefix(arrow, out ar); PatchDivine_Artemis.Finalizer(null, ar);
        Check(arrow.Max.Raw == 6, "logger failure isolated from prefix and finalizer");
    }
#endif
    private static int Main()
    {
#if LEGACY_RED
        var arrow = new ArtemisArrow();
        float original = arrow.Max.Raw;
        PatchDivine_Artemis.DamageAffectedEnemies_Prefix(arrow);
        Check(arrow.Max.Raw == 20f, "actual old production Prefix must expose candidate quota 20; observed=" + arrow.Max.Raw);
        Check(arrow.Max.Raw == original, "old call lacks end-boundary restore; original=" + original + ", observed=" + arrow.Max.Raw);
#else
        Gates(); Authority(); Hermes(); Artemis(); Faults();
#endif
        Console.WriteLine("Assertions=" + assertions + " Failures=" + failures);
        return failures == 0 ? 0 : 1;
    }
}
