using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// Replaces the original large carrier-first assignment matrix only when the
/// population is high and eligible tools are sparse. All eligibility and cost
/// semantics still come from DroppableRegistrar.CalculateCarrierScore.
/// </summary>
[HarmonyPatch(typeof(DroppableRegistrar), nameof(DroppableRegistrar.ReassignClaimers))]
internal static class PatchPerformance_ToolAssignment
{
    private const int MinimumCarrierCount = 128;
    private const int MaximumScore = 10000;

    private static readonly List<int> EligibleDroppableIndices = new();

    private static JobAssigner _solver;
    private static JobAssigner.ComputeCost _reverseCostDelegate;
    private static AssignmentContext _context;
    private static bool _insideReplacement;
    private static IntPtr _lastRegistrarPointer;
    private static bool _replacementLogged;
    private static bool _failureLogged;

    private sealed class AssignmentContext
    {
        public int[,] Scores;
        public int[] EligibleIndices;
    }

    [HarmonyPrefix]
    private static bool Prefix(DroppableRegistrar __instance)
    {
        if (ModConfig.Enabled?.Value != true)
            return true;

        // Native host logic owns assignment. A client that unexpectedly reaches
        // this wrapper must never mutate claims locally.
        if (!NetworkBigBoss.HasWorldAuth)
            return false;

        if (_insideReplacement || !TryValidateRegistrar(__instance, out var carriers, out var droppables))
            return true;

        int carrierCount = carriers.Length;
        int rawDroppableCount = droppables.Length;
        if (carrierCount < MinimumCarrierCount)
            return true;

        IntPtr registrarPointer = __instance.Pointer;
        if (_lastRegistrarPointer != registrarPointer)
        {
            _lastRegistrarPointer = registrarPointer;
            _replacementLogged = false;
            _failureLogged = false;
        }

        long startTimestamp = Stopwatch.GetTimestamp();
        bool applicationStarted = false;
        _insideReplacement = true;

        try
        {
            var desired = new Droppable[carrierCount];

            if (rawDroppableCount > 0)
            {
                int[,] scores = BuildScoreMatrix(__instance, carrierCount, rawDroppableCount);
                int eligibleCount = EligibleDroppableIndices.Count;

                // An empty eligible set is not a sparse assignment to apply: the
                // native registrar may still own valid claims that its scoring
                // pass temporarily cannot expose (notably farm tools during a
                // day/night transition).  Falling through preserves those
                // claims instead of clearing every carrier target and parking
                // the entire peasant/farmer population in one idle cluster.
                if (eligibleCount == 0)
                    return true;

                // Dense cases keep the native implementation.
                if (eligibleCount * 4 > carrierCount)
                    return true;

                if (eligibleCount > 0)
                {
                    EnsureSolver();
                    int[] eligibleIndices = EligibleDroppableIndices.ToArray();
                    _context = new AssignmentContext
                    {
                        Scores = scores,
                        EligibleIndices = eligibleIndices
                    };

                    var assignments = _solver.Compute(
                        eligibleCount,
                        carrierCount,
                        _reverseCostDelegate);
                    if (assignments == null || assignments.Length != eligibleCount)
                        return true;

                    for (int toolRow = 0; toolRow < eligibleCount; toolRow++)
                    {
                        int carrierIndex = assignments[toolRow];
                        if (carrierIndex < 0 || carrierIndex >= carrierCount)
                            continue;

                        int droppableIndex = eligibleIndices[toolRow];
                        if (scores[droppableIndex, carrierIndex] >= MaximumScore
                            || desired[carrierIndex] != null)
                            continue;

                        desired[carrierIndex] = droppables[droppableIndex];
                    }
                }
            }

            // Zero writes occurred above. Revalidate the native lists and world
            // identity immediately before the two-phase claim update.
            if (!StillMatchesSnapshot(__instance, carriers, droppables))
                return true;

            applicationStarted = true;
            ApplyDesiredTargets(carriers, desired);

            LogReplacementOnce(
                carrierCount,
                rawDroppableCount,
                EligibleDroppableIndices.Count,
                Stopwatch.GetTimestamp() - startTimestamp);
            return false;
        }
        catch (Exception exception)
        {
            LogFailureOnce(exception, applicationStarted);

            // If application had begun, the original method is deliberately
            // allowed to run in this same call and rebuild every target/claim.
            return true;
        }
        finally
        {
            _context = null;
            EligibleDroppableIndices.Clear();
            _insideReplacement = false;
        }
    }

    private static bool TryValidateRegistrar(
        DroppableRegistrar registrar,
        out IDroppableCarrier[] carriers,
        out Droppable[] droppables)
    {
        carriers = null;
        droppables = null;

        try
        {
            Managers managers = Managers.Inst;
            if (registrar == null || registrar.Pointer == IntPtr.Zero
                || registrar.gameObject == null || !registrar.gameObject.activeInHierarchy
                || managers == null || managers.dropManager == null
                || managers.dropManager.Pointer != registrar.Pointer
                || managers.kingdom == null
                || registrar._registeredCarriers == null
                || registrar._droppedItemList == null)
                return false;

            int carrierCount = registrar._registeredCarriers.Count;
            int droppableCount = registrar._droppedItemList.Count;
            carriers = new IDroppableCarrier[carrierCount];
            droppables = new Droppable[droppableCount];

            for (int i = 0; i < carrierCount; i++)
            {
                IDroppableCarrier carrier = registrar._registeredCarriers[i];
                if (carrier == null || carrier.Pointer == IntPtr.Zero)
                    return false;
                carriers[i] = carrier;
            }

            for (int i = 0; i < droppableCount; i++)
            {
                Droppable droppable = registrar._droppedItemList[i];
                if (droppable == null || droppable.Pointer == IntPtr.Zero)
                    return false;
                droppables[i] = droppable;
            }

            return true;
        }
        catch
        {
            carriers = null;
            droppables = null;
            return false;
        }
    }

    private static int[,] BuildScoreMatrix(
        DroppableRegistrar registrar,
        int carrierCount,
        int droppableCount)
    {
        EligibleDroppableIndices.Clear();
        var scores = new int[droppableCount, carrierCount];

        for (int droppableIndex = 0; droppableIndex < droppableCount; droppableIndex++)
        {
            bool eligible = false;
            for (int carrierIndex = 0; carrierIndex < carrierCount; carrierIndex++)
            {
                int score = registrar.CalculateCarrierScore(carrierIndex, droppableIndex);
                scores[droppableIndex, carrierIndex] = score;
                if (score < MaximumScore)
                    eligible = true;
            }

            if (eligible)
                EligibleDroppableIndices.Add(droppableIndex);
        }

        return scores;
    }

    private static void EnsureSolver()
    {
        _solver ??= new JobAssigner();
        _reverseCostDelegate ??= (JobAssigner.ComputeCost)(Func<int, int, int>)ReverseCost;
    }

    private static int ReverseCost(int toolRow, int carrierIndex)
    {
        AssignmentContext context = _context;
        if (context == null
            || toolRow < 0 || toolRow >= context.EligibleIndices.Length
            || carrierIndex < 0 || carrierIndex >= context.Scores.GetLength(1))
            return int.MaxValue;

        return context.Scores[context.EligibleIndices[toolRow], carrierIndex];
    }

    private static bool StillMatchesSnapshot(
        DroppableRegistrar registrar,
        IDroppableCarrier[] carriers,
        Droppable[] droppables)
    {
        if (!NetworkBigBoss.HasWorldAuth)
            return false;

        Managers managers = Managers.Inst;
        if (managers == null || managers.dropManager == null
            || managers.dropManager.Pointer != registrar.Pointer
            || managers.kingdom == null
            || registrar.gameObject == null || !registrar.gameObject.activeInHierarchy
            || registrar._registeredCarriers == null
            || registrar._registeredCarriers.Count != carriers.Length
            || registrar._droppedItemList == null
            || registrar._droppedItemList.Count != droppables.Length)
            return false;

        for (int i = 0; i < carriers.Length; i++)
        {
            IDroppableCarrier current = registrar._registeredCarriers[i];
            if (current == null || current.Pointer != carriers[i].Pointer)
                return false;
        }

        for (int i = 0; i < droppables.Length; i++)
        {
            Droppable current = registrar._droppedItemList[i];
            if (current == null || current.Pointer != droppables[i].Pointer)
                return false;
        }

        return true;
    }

    private static void ApplyDesiredTargets(
        IDroppableCarrier[] carriers,
        Droppable[] desired)
    {
        // Release all changed/stale targets first. For a null desired target the
        // interface has no getter, so explicitly clearing is the safe native path.
        for (int i = 0; i < carriers.Length; i++)
        {
            Droppable target = desired[i];
            if (target == null || !carriers[i].IsTargetingDroppable(target))
                carriers[i].SetDroppableTarget(null);
        }

        // Reassert every desired claim, including unchanged targets. This repairs
        // stale duplicate-claimer states that may have been cleared above.
        for (int i = 0; i < carriers.Length; i++)
        {
            if (desired[i] != null)
                carriers[i].SetDroppableTarget(desired[i]);
        }
    }

    private static void LogReplacementOnce(
        int carriers,
        int rawDroppables,
        int eligibleDroppables,
        long elapsedTicks)
    {
        if (_replacementLogged)
            return;

        _replacementLogged = true;
        double elapsedMilliseconds = elapsedTicks * 1000d / Stopwatch.Frequency;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[ToolAssignment] sparse replacement active: carriers=" + carriers
            + " rawDroppables=" + rawDroppables
            + " eligibleDroppables=" + eligibleDroppables
            + " elapsed=" + elapsedMilliseconds.ToString("F3") + "ms");
    }

    private static void LogFailureOnce(Exception exception, bool applicationStarted)
    {
        if (_failureLogged)
            return;

        _failureLogged = true;
        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
            "[ToolAssignment] sparse replacement failed "
            + (applicationStarted ? "during target application" : "before target application")
            + "; original assignment will run: "
            + exception.GetType().Name + ": " + exception.Message);
    }
}
