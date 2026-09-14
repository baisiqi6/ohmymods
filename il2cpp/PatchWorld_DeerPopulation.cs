using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Greek ordinary-deer population inputs, borrowed only for one native Update invocation.
/// Native timing, seasonal selection, region limits, spawning, pooling and loot stay native-owned.
/// </summary>
internal static class PatchWorld_DeerPopulation
{
    private const float Multiplier = 3f;
    private static readonly Dictionary<IntPtr, ulong> Active = new();
    // Only failed cleanup fields are retained here; normal Update invocations use the stack's struct state.
    private static readonly Dictionary<IntPtr, Lease> Pending = new();
    private static ulong _nextToken;
    private static bool _loggedApplied, _loggedFailure;

    internal struct Lease
    {
        internal PopulationController Controller;
        internal GameObject Object;
        internal IntPtr ControllerPtr, ObjectPtr;
        internal int ControllerId, ObjectId;
        internal ulong Token;
        internal byte Written;
        internal float Density, WinterDefault, WinterSpecial, Interval;
        internal float AppliedDensity, AppliedWinterDefault, AppliedWinterSpecial, AppliedInterval;
    }

    internal static void Begin(PopulationController controller, out Lease lease)
    {
        lease = default;
        try
        {
            if (controller == null) return;
            IntPtr pointer = controller.Pointer;
            if (pointer == IntPtr.Zero || Active.ContainsKey(pointer)) return;
            // Reentrant native Update must never restore the outer invocation's borrowed inputs.
            // Cleanup of a previous failed invocation precedes config/authority/world eligibility.
            if (Pending.TryGetValue(pointer, out Lease pending))
            {
                if (!SameIdentity(pending, controller)) Pending.Remove(pointer);
                else
                {
                    Restore(pending);
                    if (Pending.ContainsKey(pointer)) return; // Do not treat an unrecovered 3x field as a new baseline.
                }
            }
            if (!Eligible(controller, out GameObject go, out GameObject prefab)) return;

            // Read and validate every input before acquiring ownership or writing any field.
            float density = controller.density, winterDefault = controller.winterDensityDefault;
            float winterSpecial = controller.winterDensitySpecial, interval = controller._actualUpdateInterval;
            float appliedDensity = density * Multiplier, appliedDefault = winterDefault * Multiplier;
            float appliedSpecial = winterSpecial * Multiplier, appliedInterval = interval / Multiplier;
            if (!ValidDensity(density, appliedDensity) || !ValidDensity(winterDefault, appliedDefault)
                || !ValidDensity(winterSpecial, appliedSpecial) || !float.IsFinite(interval) || interval <= 0f
                || !float.IsFinite(appliedInterval) || appliedInterval <= 0f) return;

            ulong token = ++_nextToken;
            if (token == 0) token = ++_nextToken;
            lease = new Lease
            {
                Controller = controller, Object = go, ControllerPtr = pointer, ObjectPtr = go.Pointer,
                ControllerId = controller.GetInstanceID(), ObjectId = go.GetInstanceID(), Token = token,
                Density = density, WinterDefault = winterDefault, WinterSpecial = winterSpecial, Interval = interval,
                AppliedDensity = appliedDensity, AppliedWinterDefault = appliedDefault,
                AppliedWinterSpecial = appliedSpecial, AppliedInterval = appliedInterval
            };
            Active.Add(pointer, token);
            // Mark before each interop setter so a partial setter failure can still relinquish its own value.
            lease.Written |= 1; controller.density = appliedDensity;
            lease.Written |= 2; controller.winterDensityDefault = appliedDefault;
            lease.Written |= 4; controller.winterDensitySpecial = appliedSpecial;
            lease.Written |= 8; controller._actualUpdateInterval = appliedInterval;
            LogApplied(prefab, lease);
        }
        catch (Exception ex)
        {
            Restore(lease);
            LogFailure(ex);
        }
    }

    private static bool Eligible(PopulationController controller, out GameObject go, out GameObject prefab)
    {
        go = null; prefab = null;
        if (ModConfig.Enabled == null || !ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth
            || controller == null || !controller.enabled || controller.useBiomeCritters) return false;
        BiomeHolder biome = BiomeHolder.Inst;
        if (biome == null || biome.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return false;
        Managers managers = Managers.Inst;
        World world = managers != null ? managers.world : null;
        Game game = managers != null ? managers.game : null;
        Transform layer = world != null ? world.gameLayer : null;
        if (world == null || game == null || layer == null || !game.playingOrInMenuWithClient) return false;
        go = controller.gameObject;
        // Native World.FindOrCreateForest parents the ordinary-deer controller under the current gameLayer.
        if (go == null || !go.activeInHierarchy || go.scene.handle != layer.gameObject.scene.handle
            || !go.transform.IsChildOf(layer)) return false;
        prefab = controller.prefab;
        return prefab != null && prefab.GetComponent<Deer>() != null
            && prefab.GetComponent<Steed>() == null && prefab.GetComponent<Hind>() == null;
    }

    private static bool ValidDensity(float original, float applied)
        => float.IsFinite(original) && original >= 0f && float.IsFinite(applied);

    internal static void Restore(Lease lease)
    {
        if (lease.Token == 0) return;
        bool hasActive = Active.TryGetValue(lease.ControllerPtr, out ulong active);
        if (hasActive && active != lease.Token) return; // A stale finalizer cannot touch a newer invocation.
        bool hasPending = Pending.TryGetValue(lease.ControllerPtr, out Lease pending);
        if (!hasActive && (!hasPending || pending.Token != lease.Token)) return;
        if (!hasActive) lease = pending; // Retry only bits still owned after a previous partial cleanup.
        byte unresolved = lease.Written;
        try
        {
            // Restoration belongs to this invocation, regardless of changed config, authority or world.
            // Destroyed/reused native objects are never written. A merely disabled live object still needs cleanup.
            PopulationController controller = lease.Controller;
            if (!SameIdentity(lease, controller)) { unresolved = 0; return; }

            // Separate field boundaries ensure one failing native accessor cannot skip cleanup of the others.
            for (byte bit = 1; bit <= 8; bit <<= 1)
            {
                if ((lease.Written & bit) == 0) continue;
                try
                {
                    switch (bit)
                    {
                        case 1:
                            if (controller.density == lease.AppliedDensity) controller.density = lease.Density;
                            break;
                        case 2:
                            if (controller.winterDensityDefault == lease.AppliedWinterDefault) controller.winterDensityDefault = lease.WinterDefault;
                            break;
                        case 4:
                            if (controller.winterDensitySpecial == lease.AppliedWinterSpecial) controller.winterDensitySpecial = lease.WinterSpecial;
                            break;
                        case 8:
                            if (controller._actualUpdateInterval == lease.AppliedInterval) controller._actualUpdateInterval = lease.Interval;
                            break;
                    }
                    unresolved = (byte)(unresolved & ~bit); // Successfully restored or externally replaced.
                }
                catch (Exception ex) { LogFailure(ex); }
            }
        }
        catch (Exception ex) { LogFailure(ex); }
        finally
        {
            if (Active.TryGetValue(lease.ControllerPtr, out active) && active == lease.Token) Active.Remove(lease.ControllerPtr);
            // A different token owns any newer state; never erase or replace it from an old finalizer.
            if (!Active.ContainsKey(lease.ControllerPtr)
                && (!Pending.TryGetValue(lease.ControllerPtr, out pending) || pending.Token == lease.Token))
            {
                if (unresolved == 0) Pending.Remove(lease.ControllerPtr);
                else { lease.Written = unresolved; Pending[lease.ControllerPtr] = lease; }
            }
        }
    }

    private static bool SameIdentity(Lease lease, PopulationController controller)
    {
        GameObject go = lease.Object;
        return controller != null && go != null && controller.Pointer == lease.ControllerPtr
            && go.Pointer == lease.ObjectPtr && controller.GetInstanceID() == lease.ControllerId
            && go.GetInstanceID() == lease.ObjectId && controller.gameObject != null
            && controller.gameObject.Pointer == lease.ObjectPtr;
    }

    private static void LogApplied(GameObject prefab, Lease lease)
    {
        if (_loggedApplied) return;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[DeerPopulation] prefab=" + prefab.name
                + " density=" + lease.Density + "->" + lease.AppliedDensity
                + " winterDefault=" + lease.WinterDefault + "->" + lease.AppliedWinterDefault
                + " winterSpecial=" + lease.WinterSpecial + "->" + lease.AppliedWinterSpecial
                + " interval=" + lease.Interval + "->" + lease.AppliedInterval);
            _loggedApplied = true;
        }
        catch (Exception ex) { LogFailure(ex); }
    }

    private static void LogFailure(Exception ex)
    {
        if (_loggedFailure) return;
        _loggedFailure = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[DeerPopulation] temporary input adjustment failed: " + ex.GetType().Name); }
        catch { }
    }
}

[HarmonyPatch(typeof(PopulationController), nameof(PopulationController.Update))]
internal static class PopulationController_Update_DeerPopulation_Patch
{
    [HarmonyPrefix]
    internal static void Prefix(PopulationController __instance, out PatchWorld_DeerPopulation.Lease __state)
        => PatchWorld_DeerPopulation.Begin(__instance, out __state);

    [HarmonyPostfix]
    internal static void Postfix(PatchWorld_DeerPopulation.Lease __state) => PatchWorld_DeerPopulation.Restore(__state);

    [HarmonyFinalizer]
    internal static Exception Finalizer(Exception __exception, PatchWorld_DeerPopulation.Lease __state)
    {
        PatchWorld_DeerPopulation.Restore(__state);
        return __exception;
    }
}
