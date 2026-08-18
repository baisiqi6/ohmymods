using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Night-defense column depth.  2.4.0 refactored archer wall positioning into
/// a GuardSlot system plus a GetWallTargetPos fallback; with the populations
/// this mod enables the fallback column stretches deeper than the archer's
/// bow range (shootRange, ~8), so the rear ranks can never reach enemies at
/// the wall base.  Rather than replicate the internal spacing formula, clamp
/// the OUTPUT: any target position deeper than (shootRange - margin) behind
/// the intact border wall is pulled back to that boundary.  Populations that
/// natively stay within the boundary are untouched.
/// </summary>
public static class PatchWorld_DefenseSpacing
{
    private const float RangeMargin = 1f;
    private static bool _loggedClamp;

    [HarmonyPatch(typeof(Archer), "GetWallTargetPos")]
    [HarmonyPostfix]
    private static void Postfix(Archer __instance, ref float __result)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            float side = (float)__instance._guardSide;
            if (side == 0f) return;

            Kingdom kingdom = Managers.Inst.kingdom;
            // Anchor on the intact border wall of the archer's side; plain
            // float return keeps this independent from GetGuardPosition's
            // KeyValuePair marshaling.
            float wall = kingdom.GetBorderSideIntact(__instance._guardSide);
            float depth = (wall - __result) * side;
            float cap = __instance.shootRange - RangeMargin;
            if (depth <= cap) return;

            __result = wall - side * cap;

            if (!_loggedClamp)
            {
                _loggedClamp = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[DefenseSpacing] column clamped: depth=" + depth.ToString("F2")
                    + " cap=" + cap.ToString("F2"));
            }
        }
        catch (Exception e)
        {
            // Diagnostics must never break native positioning; fail open.
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[DefenseSpacing] " + e);
        }
    }

    /// <summary>
    /// Knights rank up linearly per side (GetTargetPos = wall - side *
    /// distanceFromWall * rank).  With several squads the rear ranks idle far
    /// behind the wall: the knight's melee never reaches outside enemies while
    /// the wall stands, and their follower archers (who trail the knight by
    /// knightFollowDistance and only open fire once the knight has settled)
    /// start the night out of bow range.  Clamp the ranked depth so every
    /// squad keeps its followers within shooting distance of the wall; knight
    /// counts that natively stay shallower are untouched.
    /// </summary>
    private const float KnightDepthCap = 6f;
    private static bool _loggedKnightClamp;

    [HarmonyPatch(typeof(Knight), "GetTargetPos")]
    [HarmonyPostfix]
    private static void KnightTargetPos_Postfix(Knight __instance, ref float __result)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            float side = (float)__instance.side;
            if (side == 0f) return;

            float wall = Managers.Inst.kingdom.GetBorderSideIntact(__instance.side);
            float depth = (wall - __result) * side;
            if (depth <= KnightDepthCap) return;

            __result = wall - side * KnightDepthCap;

            if (!_loggedKnightClamp)
            {
                _loggedKnightClamp = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[DefenseSpacing] knight rank clamped: depth=" + depth.ToString("F2")
                    + " rank=" + __instance.rank
                    + " cap=" + KnightDepthCap.ToString("F2"));
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[DefenseSpacing/knight] " + e);
        }
    }
}
