using System;
using System.Diagnostics;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// Stage-one runtime probe for the sparse tool-assignment optimization.
///
/// The original ReassignRoutine calls a private native method. Although the
/// IL2CPP interop assembly exposes a callable wrapper, native-to-native calls
/// can bypass that wrapper. This probe deliberately leaves the original
/// assignment untouched and logs only the first four observed calls so the
/// real hook path and its baseline cost can be proven in game before any
/// replacement algorithm is enabled.
/// </summary>
[HarmonyPatch(typeof(DroppableRegistrar), nameof(DroppableRegistrar.ReassignClaimers))]
internal static class PatchPerformance_ToolAssignmentProbe
{
    private const int MaxLoggedSamplesPerRegistrar = 4;

    private static IntPtr _registrarPointer;
    private static long _previousStartTimestamp;
    private static int _observedCalls;
    private static bool _probeErrorLogged;

    private struct ProbeState
    {
        public bool Active;
        public int Sample;
        public int Carriers;
        public int Droppables;
        public long StartTimestamp;
        public double IntervalMilliseconds;
    }

    [HarmonyPrefix]
    private static void Prefix(DroppableRegistrar __instance, out ProbeState __state)
    {
        __state = default;

        if (ModConfig.Enabled?.Value != true || __instance == null)
            return;

        try
        {
            IntPtr pointer = __instance.Pointer;
            if (pointer == IntPtr.Zero)
                return;

            if (_registrarPointer != pointer)
            {
                _registrarPointer = pointer;
                _previousStartTimestamp = 0;
                _observedCalls = 0;
                _probeErrorLogged = false;
            }

            long now = Stopwatch.GetTimestamp();
            int sample = ++_observedCalls;
            double intervalMilliseconds = _previousStartTimestamp == 0
                ? 0d
                : TicksToMilliseconds(now - _previousStartTimestamp);
            _previousStartTimestamp = now;

            if (sample > MaxLoggedSamplesPerRegistrar)
                return;

            __state = new ProbeState
            {
                Active = true,
                Sample = sample,
                Carriers = __instance._registeredCarriers?.Count ?? -1,
                Droppables = __instance._droppedItemList?.Count ?? -1,
                StartTimestamp = now,
                IntervalMilliseconds = intervalMilliseconds
            };
        }
        catch (Exception exception)
        {
            LogProbeErrorOnce(exception);
        }
    }

    [HarmonyPostfix]
    private static void Postfix(ProbeState __state)
    {
        if (!__state.Active)
            return;

        try
        {
            double elapsedMilliseconds = TicksToMilliseconds(
                Stopwatch.GetTimestamp() - __state.StartTimestamp);
            string interval = __state.Sample == 1
                ? "first"
                : __state.IntervalMilliseconds.ToString("F1") + "ms";

            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[ToolAssignmentProbe] hit=" + __state.Sample
                + " carriers=" + __state.Carriers
                + " droppables=" + __state.Droppables
                + " interval=" + interval
                + " original=" + elapsedMilliseconds.ToString("F3") + "ms");
        }
        catch (Exception exception)
        {
            LogProbeErrorOnce(exception);
        }
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000d / Stopwatch.Frequency;
    }

    private static void LogProbeErrorOnce(Exception exception)
    {
        if (_probeErrorLogged)
            return;

        _probeErrorLogged = true;
        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
            "[ToolAssignmentProbe] probe failed without changing original assignment: "
            + exception.GetType().Name + ": " + exception.Message);
    }
}
