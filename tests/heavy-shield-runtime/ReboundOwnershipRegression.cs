using System;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

// Host driver for native pause ownership; it does not qualify actual prefab routing.
// The two native body branches follow ARM64 0x90bac4..0x90bb60:
//   IsInBlockingFormation || surface in {1,2}: tail-call ReceiveDamage, no pause.
//   otherwise: ReceiveDamage -> allocate/replace/start one pause -> return.
// AfterReceive is a bounded synchronous ReceiveDamage postfix callback, before its
// Finalizer. It invokes a nested same-Troll collision; it never writes a pause.
internal static class ReboundOwnershipRegression
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;
    static readonly MethodInfo Prefix = typeof(HeavyShieldTrollImpact).GetMethod("Prefix", Hidden);
    static readonly MethodInfo Postfix = typeof(HeavyShieldTrollImpact).GetMethod("Postfix", Hidden);
    static readonly MethodInfo Finalizer = typeof(HeavyShieldTrollImpact).GetMethod("Finalizer", Hidden);
    static int checks, failures;
    static Action<bool, string> assert;
    sealed class ControlledTailFault : Exception { }
    sealed class Invocation
    {
        internal Troll._PauseMotion_d__114 Created;
        internal bool PostfixRan;
        internal Exception Fault;
    }

    static void Check(bool condition, string label)
    {
        checks++;
        if (!condition) failures++;
        assert(condition, label);
        Console.WriteLine((condition ? "PASS " : "FAIL ") + label);
    }

    internal static void Run(Action<bool, string> check)
    {
        assert = check;
        Case("control-ordinary-outer", 0, false, true, false);
        Case("surface1-outer-inner-normal", 1, false, true, false);
        Case("surface2-outer-inner-inactive-ticket", 2, false, false, false);
        Case("blocking-outer-inner-exception", 0, true, true, true);
        Case("control-ordinary-outer-inner-exception", 0, false, true, true);
        OuterException();
        SameTargetNested(false);
        SameTargetNested(true);
        UnavailableDamageReturn(false, false);
        UnavailableDamageReturn(false, true);
        UnavailableDamageReturn(true, false);
        UnavailableDamageReturn(true, true);
        Console.WriteLine($"RESULT counterexamples: {checks} assertions, {failures} failures");
        Check(failures == 0, "native pause ownership regressions all passed");
    }

    static void Case(string label, int outerSurface, bool outerBlocking, bool innerIntent, bool innerFault)
    {
        using var outerTarget = new Fixture();
        using var innerTarget = new Fixture();
        Prepare(outerTarget);
        // Same-side seat 1 is at 4.25; keep the second real runtime career at its
        // assigned home so it equips instead of waiting for movement not simulated here.
        innerTarget.Go.transform.position = new Vector3(4.25f, 0f);
        Prepare(innerTarget);
        var troll = TrollAt(5.5f);
        outerTarget.Damage.surface = outerSurface;
        outerTarget.Damage.IsInBlockingFormation = outerBlocking;
        bool outerMakesPause = !outerBlocking && outerSurface != 1 && outerSurface != 2;
        Invocation inner = null;
        outerTarget.Damage.AfterReceive = () =>
        {
            inner = Impact(troll, innerTarget.Damage, innerIntent, innerFault);
            Console.WriteLine($"TRACE {label}: inner created={inner.Created?.Pointer}, postfix={inner.PostfixRan}, fault={inner.Fault?.GetType().Name ?? "none"}, writes={inner.Created?.VectorWrites}");
            // A ReceiveDamage extension may catch its nested helper's error and return.
            // The normal outer invocation then resumes its native selected branch.
        };
        var outer = Impact(troll, outerTarget.Damage, true, false);
        Check(outerTarget.Damage.NativeHits == 0, label + " outer real TryBlock succeeded");
        Check(inner != null && inner.Created != null, label + " nested native path allocated pause");
        Check(innerTarget.Damage.NativeHits == (innerIntent ? 0 : 1), label + " inner block/native outcome");
        Check(inner.PostfixRan == !innerFault && outer.PostfixRan, label + " actual normal/exception postfix schedule");
        int expectedInnerWrites = innerIntent && !innerFault ? 1 : 0;
        int actualInnerWrites = inner.Created.VectorWrites;
        Check(actualInnerWrites == expectedInnerWrites,
            label + $" outer must not rewrite child pause (expected={expectedInnerWrites}, actual={actualInnerWrites})");
        if (outerMakesPause)
        {
            Check(outer.Created != null && outer.Created.Pointer != inner.Created.Pointer,
                label + " ordinary outer native tail replaced child pause");
            Check(outer.Created.VectorWrites == 1, label + " new outer pause gets exactly own witness");
        }
        else
        {
            Check(outer.Created == null && ReferenceEquals(troll._pauseMotion, inner.Created),
                label + " native tail-free outer leaves child pause current");
        }
        Console.WriteLine($"TRACE {label}: current={troll._pauseMotion?.Pointer}, outerCreated={outer.Created?.Pointer}, childFinalWrites={actualInnerWrites}, childAfter=({inner.Created.velocityAfter.x},{inner.Created.velocityAfter.y})");
    }

    static void OuterException()
    {
        using var target = new Fixture();
        Prepare(target);
        var troll = TrollAt(5.5f);
        var result = Impact(troll, target.Damage, true, true);
        Check(result.Fault is ControlledTailFault && !result.PostfixRan,
            "control outer exception does not execute Postfix");
        Check(result.Created != null && result.Created.VectorWrites == 0,
            "control outer exception leaves native pause vector untouched");
    }

    static void SameTargetNested(bool innerFault)
    {
        using var target = new Fixture();
        Prepare(target);
        var troll = TrollAt(5.5f);
        target.Damage.surface = 1;
        Invocation inner = null;
        target.Damage.AfterReceive = () =>
        {
            target.Damage.AfterReceive = null;
            // The outer native branch was selected before ReceiveDamage; the nested
            // call can independently take the ordinary branch with the same target.
            target.Damage.surface = 0;
            try { inner = Impact(troll, target.Damage, true, innerFault); }
            finally { target.Damage.surface = 1; }
        };
        var outer = Impact(troll, target.Damage, true, false);
        Check(outer.PostfixRan && inner?.Created != null, "same-target nested invocation completes outer");
        Check(target.Damage.NativeHits == 0, "both same-target contacts use real successful blocks");
        Check(inner.Created.VectorWrites == (innerFault ? 0 : 1),
            "same-target outer never borrows inner normal or exceptional pause");
    }

    static void UnavailableDamageReturn(bool throwsInChain, bool repeatSuccessful)
    {
        using var target = new Fixture();
        Prepare(target);
        var troll = TrollAt(5.5f);
        HeavyShieldCombat.ObserveTrollIntent(troll, target.Damage);
        object[] prefixArguments = { troll, target.Damage, null };
        Prefix.Invoke(null, prefixArguments);
        using var scope = (IDisposable)prefixArguments[2];
        target.Damage.AfterReceive = () =>
        {
            if (throwsInChain) throw new ControlledTailFault();
            troll.PauseReadFault = true;
        };
        Exception error = null;
        try { target.Damage.ReceiveDamage(1, troll.gameObject, DamageSource.Troll); }
        catch (Exception ex) { error = ex; }
        target.Damage.AfterReceive = null;
        troll.PauseReadFault = false;
        Check(throwsInChain ? error is ControlledTailFault : error == null,
            "damage finalizer preserves errors and does not throw on unknown pause");
        if (repeatSuccessful) target.Damage.ReceiveDamage(1, troll.gameObject, DamageSource.Troll);
        var pause = NativePause(troll);
        Postfix.Invoke(null, new object[] { troll, target.Damage, scope });
        Check(target.Damage.NativeHits == 0 && pause.VectorWrites == 0,
            "unknown or exceptional damage return stays rejected after a later successful observation");
    }

    // Calls the production Harmony hook methods and its real EnterTroll/EnterDamage,
    // TryBlock, policy, life, career and rebound implementation. Only the unavailable
    // native method body is modeled, like the project's existing boundary regression.
    static Invocation Impact(Troll troll, Damageable target, bool issueIntent, bool failAfterPause)
    {
        if (issueIntent) HeavyShieldCombat.ObserveTrollIntent(troll, target);
        object[] prefixArguments = { troll, target, null };
        Prefix.Invoke(null, prefixArguments);
        var state = (IDisposable)prefixArguments[2];
        var result = new Invocation();
        try
        {
            bool createsPause = !target.IsInBlockingFormation && target.surface != 1 && target.surface != 2;
            target.ReceiveDamage(1, troll.gameObject, DamageSource.Troll);
            if (createsPause) result.Created = NativePause(troll);
            if (failAfterPause) throw new ControlledTailFault();
            Postfix.Invoke(null, new object[] { troll, target, state });
            result.PostfixRan = true;
        }
        catch (Exception exception)
        {
            result.Fault = exception is TargetInvocationException tie ? tie.InnerException : exception;
        }
        finally
        {
            Finalizer.Invoke(null, new object[] { result.Fault, state });
        }
        return result;
    }

    static void Prepare(Fixture f)
    {
        Check(f.Attach(new HeavyShieldSavedCombatState(HeavyShieldBlockPolicy.InitialDurability, false, false)), "fixture attach");
        HeavyShieldRuntime.Tick();
        for (int i = 0; i < 8; i++)
        {
            Time.deltaTime = .25f; Time.time += .25f; Time.unscaledTime += .25f;
            Time.unscaledTimeAsDouble += .25; Time.frameCount++;
            HeavyShieldActorVisuals.Tick(f.Handle.GoId); HeavyShieldRuntime.Tick();
        }
        Check(HeavyShieldActorVisuals.GuardReady(f.Archer, f.Handle.Life), "fixture guard");
    }

    static Troll TrollAt(float x)
    {
        var go = new GameObject("review troll");
        go.transform.SetParent(Managers.Inst.world.gameLayer, false);
        go.transform.position = new Vector3(x, 0);
        var troll = go.AddComponent<Troll>(); troll.Type = EnemyType.TrollWeak;
        troll.knightBounceForce = new Vector2(2, 3);
        go.AddComponent<Damageable>();
        return troll;
    }

    static Troll._PauseMotion_d__114 NativePause(Troll troll)
    {
        var pause = new Troll._PauseMotion_d__114 { __4__this = troll, __1__state = 1,
            duration = 1f / 15f, velocityAfter = new Vector2(2.5f, 3.5f) };
        pause.VectorWrites = 0;
        troll._pauseMotion = pause;
        return pause;
    }
}
