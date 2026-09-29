using System;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// Native boundaries for the wall-engineer feature. Every target below is an actual long method in
/// the 2.4 build (token/RVA recorded in the issue-86 evidence); all patches are plain attributes so
/// the existing PatchAll registration in KingdomEnhancedPlugin picks them up. Hook bodies are
/// one-liners into WallEngineerRuntime and every body is exception-isolated so a fault degrades the
/// state machine instead of the plugin.
///
/// Deliberately absent: any hook on Kingdom.RemoveWork, GetJobOffset/GetMaxActors, coroutine
/// MoveNext, generic methods, or non-existent members. Wall job removal/return happens from the
/// unsafe/damage/disable seams and from WallEngineerRuntime's own release paths.
/// </summary>
internal static class WallEngineersPatches
{
    // ---------------------------------------------------------------- load boundary

    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.TryPopObjectsToScene))]
    internal static class LoadScopePatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Before()
        {
            try { WallEngineerRuntime.NotifyLoadScopeEntered(); }
            catch { WallEngineerRuntime.ReportHookFault("pop-prefix"); }
        }

        [HarmonyFinalizer]
        private static Exception Finally(Exception __exception, bool __result)
        {
            try { WallEngineerRuntime.NotifyLoadScopeExited(__exception == null && __result); }
            catch { WallEngineerRuntime.ReportHookFault("pop-finalizer"); }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CampaignSaveData), nameof(CampaignSaveData.ApplyToScene))]
    internal static class ApplyToScenePatch
    {
        [HarmonyPostfix]
        private static void After(CampaignSaveData __instance)
        {
            try { WallEngineerRuntime.NotifyApplyToSceneCompleted(__instance); }
            catch { WallEngineerRuntime.ReportHookFault("apply-postfix"); }
        }

        [HarmonyFinalizer]
        private static Exception Finally(Exception __exception)
        {
            if (__exception != null)
            {
                try { WallEngineerRuntime.NotifyApplyToSceneFailed(); }
                catch { WallEngineerRuntime.ReportHookFault("apply-finalizer"); }
            }
            return __exception;
        }
    }

    // ---------------------------------------------------------------- roster / wall bookkeeping

    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.AddWorker))]
    internal static class AddWorkerPatch
    {
        [HarmonyPostfix]
        private static void After() => RosterChanged("add-worker");
    }

    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.RemoveWorker))]
    internal static class RemoveWorkerPatch
    {
        [HarmonyPostfix]
        private static void After() => RosterChanged("remove-worker");
    }

    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.AddWall))]
    internal static class AddWallPatch
    {
        [HarmonyPostfix]
        private static void After() => Bookkeeping("add-wall");
    }

    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.DestroyWall))]
    internal static class DestroyWallPatch
    {
        [HarmonyPostfix]
        private static void After() => Bookkeeping("destroy-wall");
    }

    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.ReplaceWall))]
    internal static class ReplaceWallPatch
    {
        [HarmonyPrefix]
        private static void Before(Wall __0)
        {
            try { WallEngineerRuntime.NotifyWallReplacing(__0); }
            catch { WallEngineerRuntime.ReportHookFault("replace-pre"); }
        }

        [HarmonyPostfix]
        private static void After() => Bookkeeping("replace-post");
    }

    // ---------------------------------------------------------------- wall seams

    [HarmonyPatch(typeof(Wall), "OnDisable")]
    internal static class WallDisablePatch
    {
        [HarmonyPrefix]
        private static void Before(Wall __instance)
        {
            try { WallEngineerRuntime.NotifyWallDisabled(__instance); }
            catch { WallEngineerRuntime.ReportHookFault("wall-disable"); }
        }
    }

    [HarmonyPatch(typeof(Wall), "HandleOnBecameUnSafe")]
    internal static class WallUnsafePatch
    {
        [HarmonyPrefix]
        private static bool Before(Wall __instance)
        {
            try { return !WallEngineerRuntime.ShouldSkipUnsafeRemoval(__instance); }
            catch
            {
                WallEngineerRuntime.ReportHookFault("wall-unsafe");
                return true;   // run the native removal on any doubt
            }
        }
    }

    [HarmonyPatch(typeof(Wall), "HandleOnReceiveDamage")]
    internal static class WallDamagePatch
    {
        [HarmonyPostfix]
        private static void After(Wall __instance)
        {
            try { WallEngineerRuntime.NotifyWallDamaged(__instance); }
            catch { WallEngineerRuntime.ReportHookFault("wall-damage"); }
        }
    }

    [HarmonyPatch(typeof(Kingdom), "OnDisable")]
    internal static class KingdomDisablePatch
    {
        [HarmonyPrefix]
        private static void Before()
        {
            try { WallEngineerRuntime.NotifyKingdomDisabled(); }
            catch { WallEngineerRuntime.ReportHookFault("kingdom-disable"); }
        }
    }

    // ---------------------------------------------------------------- worker seams

    [HarmonyPatch(typeof(Worker), "ShouldFlee")]
    internal static class ShouldFleePatch
    {
        [HarmonyPostfix]
        private static void After(Worker __instance, ref bool __result)
        {
            if (!__result) return;
            try
            {
                if (WallEngineerRuntime.MaySuppressFlee(__instance)) __result = false;
            }
            catch { WallEngineerRuntime.ReportHookFault("should-flee"); }
        }
    }

    [HarmonyPatch(typeof(Worker), "OnDisable")]
    internal static class WorkerDisablePatch
    {
        [HarmonyPostfix]
        private static void After(Worker __instance)
        {
            try { WallEngineerRuntime.NotifyWorkerDisabled(__instance); }
            catch { WallEngineerRuntime.ReportHookFault("worker-disable"); }
        }
    }

    // ---------------------------------------------------------------- helpers

    private static void RosterChanged(string where)
    {
        try { WallEngineerRuntime.NotifyRosterChanged(); }
        catch { WallEngineerRuntime.ReportHookFault(where); }
    }

    private static void Bookkeeping(string where)
    {
        try { WallEngineerRuntime.NotifyWallBookkeepingChanged(); }
        catch { WallEngineerRuntime.ReportHookFault(where); }
    }
}
