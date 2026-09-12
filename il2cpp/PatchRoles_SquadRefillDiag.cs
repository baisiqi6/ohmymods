using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Read-only native recruitment evidence. At most 30 event lines per process, with no scene scan.</summary>
internal static class PatchRoles_SquadRefillDiag
{
    internal sealed class Sample
    {
        internal Knight Knight;
        internal IntPtr JobPtr;
        internal int Style, Id, Before, Capacity, Requested, MinFree;
        internal float Range, X, Nearest = float.PositiveInfinity;
        internal bool HasValidator, Truncated;
        internal int Inspected;
        internal int Accepted, Within, Rejected, ReserveDecrements, Occupied, Guard, Formation, Embark, Grab, Shield, Inactive, Crossbow;
    }
    internal struct Frame
    {
        internal Sample Previous, Selected;
        internal bool Entered;
    }
    private static readonly float[] NextStyle = new float[5];
    private static float NextEvent;
    private static int Used;
    private static bool Failed;
    internal static Sample Current;
    private static bool Ready() => !Failed && Used < 30 && ModConfig.Enabled.Value &&
        NetworkBigBoss.HasWorldAuth && Time.timeScale > 0 && Time.time >= NextEvent;
    private static void Log(string text)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SquadRefill] " + text); }
        catch { Failed = true; }
    }
    private static void Error(Exception e)
    {
        if (Failed) return;
        Failed = true;
        if (Used >= 30) return;
        Used++;
        Log("diagnostic stopped after " + e.GetType().Name);
    }

    internal static void Begin(Kingdom kingdom, GameObject job, int requested, float range, bool hasValidator, ref Frame frame)
    {
        // Every nested call masks its parent, including calls that are not sampled. No heap allocation for those calls.
        frame = new Frame { Previous = Current, Entered = true };
        Current = null;
        try
        {
            if (!Ready() || kingdom == null || job == null || requested <= 0) return;
            var k = job.GetComponent<Knight>();
            if (k == null || !job.activeInHierarchy || !k.enabled ||
                !PatchRoles_KnightStyle.TryGetResolvedStyleIndex(k, out int style) || style < 0 || style >= 5 ||
                Time.time < NextStyle[style]) return;
            var sample = new Sample { Knight = k, JobPtr = job.Pointer, Style = style,
                Id = k.GetInstanceID(), Before = k.numArchers, Capacity = k._maxArchers,
                Requested = requested, Range = range, X = job.transform.position.x,
                MinFree = kingdom.minFreeArchers, HasValidator = hasValidator };
            NextStyle[style] = Time.time + 30f;
            NextEvent = Time.time + 3f;
            Used++; // Reserve the eventual completion line, even if the native call throws.
            frame.Selected = Current = sample;
        }
        catch (Exception e) { Error(e); }
    }

    internal static void Candidate(Archer archer, GameObject job, bool accepted)
    {
        var s = Current;
        if (Failed || s == null) return;
        // Bound extra native reads per event, even in a huge population; report incomplete counts explicitly.
        if (s.Inspected >= 256) { s.Truncated = true; return; }
        try
        {
            if (archer == null || job == null || job.Pointer != s.JobPtr) return;
            s.Inspected++;
            if (accepted)
            {
                s.Accepted++;
                float distance = Mathf.Abs(archer.transform.position.x - s.X);
                s.Nearest = Mathf.Min(s.Nearest, distance);
                if (distance < s.Range) s.Within++;
                return;
            }
            s.Rejected++;
            if (archer._knight != null) s.Occupied++;
            if (archer.inGuardSlot) s.Guard++;
            if (archer.IsInFormation()) s.Formation++;
            if (archer.HasEmbarkableTarget()) s.Embark++;
            if (archer.IsGrabbed()) s.Grab++;
            if (archer._npcShieldUser != null && !archer._npcShieldUser.HasShield()) s.Shield++;
            if (!archer.gameObject.activeSelf) s.Inactive++;
            if (PatchRoles_Crossbowman.IsCrossbowman(archer)) s.Crossbow++;
            if (archer.isAvailable) s.ReserveDecrements++;
        }
        catch (Exception e) { Error(e); }
    }

    internal static void End(ref Frame frame, Exception nativeException)
    {
        if (!frame.Entered) return;
        try
        {
            var s = frame.Selected;
            if (s == null) return;
            int after = s.Knight != null && s.Knight.gameObject != null ? s.Knight.numArchers : -1;
            Log("style=" + s.Style + " owner#" + s.Id + " roster=" + s.Before + "->" + after + "/" + s.Capacity +
                " requested=" + s.Requested + " range=" + s.Range.ToString("F1") + " accepted=" + s.Accepted +
                " within=" + s.Within + " nearest=" + (float.IsPositiveInfinity(s.Nearest) ? "none" : s.Nearest.ToString("F1")) +
                " minFree=" + s.MinFree + " reserveDecrements=" + s.ReserveDecrements +
                " effectiveReserve=" + ((s.HasValidator || s.Truncated) ? "unknown-partial" : Math.Max(0, s.MinFree - s.ReserveDecrements).ToString()) +
                " rejected=" + s.Rejected + " occupied=" + s.Occupied + " guard=" + s.Guard + " formation=" + s.Formation +
                " embark=" + s.Embark + " grab=" + s.Grab + " shield=" + s.Shield + " inactive=" + s.Inactive +
                " crossbow=" + s.Crossbow + " overlap=true validator=" + s.HasValidator + " truncated=" + s.Truncated +
                (nativeException != null ? " nativeError=" + nativeException.GetType().Name : ""));
        }
        catch (Exception e) { Error(e); }
        finally
        {
            Current = frame.Previous;
            frame = default;
        }
    }

    internal static void Disabled(Archer archer)
    {
        try
        {
            if (!Ready() || archer == null) return;
            var k = archer._knight;
            if (k == null || k.gameObject == null || !k.gameObject.activeInHierarchy ||
                !PatchRoles_KnightStyle.TryGetResolvedStyleIndex(k, out int style) || style < 0 || style >= 5) return;
            NextEvent = Time.time + 3f;
            Used++;
            Log("disabled style=" + style + " owner#" + k.GetInstanceID() + " rosterBefore=" + k.numArchers + "/" + k._maxArchers +
                " dead=" + (archer._damageable != null && archer._damageable.isDead) + " disableIsNotAlwaysDeath=true");
        }
        catch (Exception e) { Error(e); }
    }
}

[HarmonyPatch(typeof(Kingdom), nameof(Kingdom.FetchArchersForJob))]
internal static class Kingdom_FetchArchersForJob_SquadRefillDiag_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Kingdom __instance, GameObject __0, int __1, float __2, Il2CppSystem.Func<float, bool> __3,
        ref PatchRoles_SquadRefillDiag.Frame __state) => PatchRoles_SquadRefillDiag.Begin(__instance, __0, __1, __2, __3 != null, ref __state);
    [HarmonyFinalizer]
    private static void Finalizer(Exception __exception, ref PatchRoles_SquadRefillDiag.Frame __state) =>
        PatchRoles_SquadRefillDiag.End(ref __state, __exception); // void: preserve the original native exception.
}

[HarmonyPatch(typeof(Archer), nameof(Archer.IsAvailableForJob))]
internal static class Archer_IsAvailableForJob_SquadRefillDiag_Patch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(Archer __instance, GameObject jobObject, bool __result) =>
        PatchRoles_SquadRefillDiag.Candidate(__instance, jobObject, __result);
}

[HarmonyPatch(typeof(Archer), "OnDisable")]
internal static class Archer_OnDisable_SquadRefillDiag_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Archer __instance) => PatchRoles_SquadRefillDiag.Disabled(__instance);
}
