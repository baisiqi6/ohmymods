using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
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
            bool clamped = depth > cap;
            if (clamped) __result = wall - side * cap;

            if (!_loggedClamp)
            {
                _loggedClamp = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[DefenseSpacing] archer wall target first call: depth="
                    + depth.ToString("F2") + " cap=" + cap.ToString("F2")
                    + " clamped=" + clamped);
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
    /// <summary>
    /// 2.4.0 refactored night positioning: GetWallTargetPos/GetTargetPos never
    /// fire (verified live with first-call probes on 20260819-c through a
    /// full night).  These probes map which candidate path actually runs at
    /// dusk so the real spacing fix can target it.
    /// </summary>
    private static bool _probedShouldGoToWall;
    private static bool _probedGetGuardPosition;
    private static int _guardPositionCalls;
    private static bool _probedEnterGuardSlot;

    [HarmonyPatch(typeof(Archer), nameof(Archer.ShouldGoToWall))]
    [HarmonyPostfix]
    private static void ShouldGoToWall_Probe()
    {
        if (_probedShouldGoToWall) return;
        _probedShouldGoToWall = true;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[DefenseSpacing] probe: Archer.ShouldGoToWall called");
    }

    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.GetGuardPosition))]
    [HarmonyPostfix]
    private static void GetGuardPosition_Probe()
    {
        _guardPositionCalls++;
        if (_probedGetGuardPosition) return;
        _probedGetGuardPosition = true;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[DefenseSpacing] probe: Kingdom.GetGuardPosition first call");
    }

    [HarmonyPatch(typeof(Archer), nameof(Archer.EnterGuardSlot))]
    [HarmonyPrefix]
    private static void EnterGuardSlot_Probe()
    {
        if (_probedEnterGuardSlot) return;
        _probedEnterGuardSlot = true;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[DefenseSpacing] probe: Archer.EnterGuardSlot called");
    }

    // ---- 2.4.0 depth-clamp supervisor -------------------------------------
    // 2.4.0 inlined wall positioning into the archer behaviour coroutine:
    // pos = wall - side * (_minDistanceFromWall + _guardDepth * _unitSpacingAtWall
    //                        + _guardRandomOffset), with GetWallTargetPos left as
    // dead code (verified by first-call probes through a full night).
    // _guardDepth is a plain field, so a slow supervisor rewrites any archer
    // whose depth * spacing exceeds bow range; the behaviour re-goals
    // periodically and the archer walks into range.

    private const float DepthClampRange = 7f;
    private static float _nextDepthClampAt;
    private static bool _loggedDepthClamp;
    private static bool _loggedHeartbeat;

    // ---- knight squad queue probe -----------------------------------------
    // Knight kept rank/_distanceFromWall/GetTargetPos in 2.4.0, but GetTargetPos
    // never fired through a full night (position likely inlined into the
    // GoToWall coroutine).  rank is a writable field: measure the live
    // depth-per-rank distribution during an actual wall lineup; if depth grows
    // with rank, clamping rank in this pass compresses the squad queue.
    private static bool _loggedKnightSample;
    private static bool _loggedKnightLineup;

    private static void ScanKnights()
    {
        Knight[] knights = UnityEngine.Object.FindObjectsOfType<Knight>();
        int count = knights != null ? knights.Length : 0;

        if (count > 0 && !_loggedKnightSample)
        {
            _loggedKnightSample = true;
            System.Text.StringBuilder sample = new System.Text.StringBuilder();
            int sampled = 0;
            for (int i = 0; i < count && sampled < 3; i++)
            {
                Knight knight = knights[i];
                if (knight == null || knight.gameObject == null) continue;
                if (sample.Length > 0) sample.Append(" | ");
                sample.Append("rank=").Append(knight.rank)
                    .Append(" dfw=").Append(knight._distanceFromWall.ToString("F2"))
                    .Append(" side=").Append((int)knight.side);
                sampled++;
            }
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[DefenseSpacing] knight sample: count=" + count
                + " [" + sample + "]");
        }

        // Wall-lineup report: fires once when at least three knights stand in a
        // depth band behind the border wall (the night formation), listing the
        // rank -> measured depth mapping.  Daytime wanderers sit too far away
        // or in front of the wall and do not trigger it.
        if (count < 3 || _loggedKnightLineup) return;
        Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
        if (kingdom == null) return;

        var lined = new System.Collections.Generic.List<Knight>();
        for (int i = 0; i < count; i++)
        {
            Knight knight = knights[i];
            if (knight == null || knight.gameObject == null) continue;
            float side = (float)knight.side;
            if (side == 0f) continue;
            float wall = kingdom.GetBorderSideIntact(knight.side);
            float depth = (wall - knight.transform.position.x) * side;
            if (depth > 0.5f && depth <= 15f) lined.Add(knight);
        }
        if (lined.Count < 3) return;

        lined.Sort((a, b) => a.rank.CompareTo(b.rank));
        System.Text.StringBuilder report = new System.Text.StringBuilder();
        for (int i = 0; i < lined.Count; i++)
        {
            Knight knight = lined[i];
            float side = (float)knight.side;
            float wall = kingdom.GetBorderSideIntact(knight.side);
            float depth = (wall - knight.transform.position.x) * side;
            if (report.Length > 0) report.Append("; ");
            report.Append("r").Append(knight.rank)
                .Append("@").Append(depth.ToString("F1"));
        }
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[DefenseSpacing] knight lineup: " + report);
        if (_loggedKnightLineup) return;
        _loggedKnightLineup = true;

        // Measured live: depth = rank * _distanceFromWall (1.0 per rank, r15 at
        // 15 units behind the wall).  Follower archers trail their knight by
        // knightFollowDistance, so rank N puts its squad's bows at roughly
        // N*spacing + 1 — far past the 8-unit bow range for N > 7.  Remap ranks
        // per side into 1..KnightRankCap (distinct values keep squads from
        // stacking on one spot); daytime passes rewrite ranks well before the
        // dusk lineup reads them.
        RemapKnightRanks(knights, kingdom);
    }

    private const int KnightRankCap = 7;

    private static bool _loggedKnightRemap;

    private static void RemapKnightRanks(Knight[] knights, Kingdom kingdom)
    {
        try
        {
            var left = new System.Collections.Generic.List<Knight>();
            var right = new System.Collections.Generic.List<Knight>();
            for (int i = 0; i < knights.Length; i++)
            {
                Knight knight = knights[i];
                if (knight == null || knight.gameObject == null
                    || !knight.gameObject.activeInHierarchy) continue;
                if (knight.side == Side.Left) left.Add(knight);
                else if (knight.side == Side.Right) right.Add(knight);
            }
            int remapped = 0;
            remapped += RemapSide(left);
            remapped += RemapSide(right);
            if (!_loggedKnightRemap && remapped > 0)
            {
                _loggedKnightRemap = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[DefenseSpacing] knight ranks compressed to cap="
                    + KnightRankCap + " remapped=" + remapped);
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[DefenseSpacing/knight-remap] " + e);
        }
    }

    private static int RemapSide(System.Collections.Generic.List<Knight> side)
    {
        if (side.Count <= KnightRankCap) return 0;
        side.Sort((a, b) => a.rank.CompareTo(b.rank));
        int changed = 0;
        for (int i = 0; i < side.Count; i++)
        {
            // Spread count knights over ranks 1..cap while keeping them
            // distinct: the i-th of n knights gets 1 + i*cap/n.
            int newRank = 1 + (int)Math.Floor((double)i * KnightRankCap / side.Count);
            if (newRank >= side.Count) newRank = side.Count;
            if (side[i].rank == newRank) continue;
            side[i].rank = newRank;
            changed++;
        }
        return changed;
    }

    // Director.Update proved unhookable in 2.4.0 (inlined or replaced — both the
    // depth supervisor and the night-volley probe on it never fired).  Host the
    // pass in a World coroutine instead, the pattern the working GhostLeashHold
    // supervisor uses.
    private static IntPtr _supervisorWorld;

    internal static IEnumerator SupervisorRoutine(World world)
    {
        if (world == null || _supervisorWorld == world.Pointer) yield break;
        _supervisorWorld = world.Pointer;
        while (world != null && world.gameObject != null)
        {
            yield return new WaitForSeconds(3f);
            DepthClampPass();
        }
    }

    private static void DepthClampPass()
    {
        try
        {
            float now = Time.unscaledTime;
            if (now < _nextDepthClampAt) return;
            _nextDepthClampAt = now + 3f;

            Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
            if (kingdom == null) return;
            if (!_loggedHeartbeat)
            {
                _loggedHeartbeat = true;
                int propCount = -1;
                string propError = null;
                try
                {
                    var list = kingdom.Archers;
                    propCount = list == null ? -1 : list.Count;
                }
                catch (Exception ex) { propError = ex.GetType().Name; }
                Archer[] found = UnityEngine.Object.FindObjectsOfType<Archer>();
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[DefenseSpacing] heartbeat: archersProp=" + propCount
                    + (propError != null ? " propError=" + propError : "")
                    + " foundByType=" + (found != null ? found.Length : -1));
            }

            ScanKnights();
            if (kingdom.Archers == null) return;
            Archer[] archers = UnityEngine.Object.FindObjectsOfType<Archer>();
            int count = archers != null ? archers.Length : 0;
            if (count == 0) return;

            int clamped = 0;
            int maxDepth = 0;
            for (int i = 0; i < count; i++)
            {
                Archer archer = archers[i];
                if (archer == null || archer.gameObject == null
                    || !archer.gameObject.activeInHierarchy) continue;
                float side = (float)archer._guardSide;
                if (side == 0f) continue;

                float spacing = archer._unitSpacingAtWall;
                if (spacing <= 0.01f) continue;
                float min = archer._minDistanceFromWall;
                float random = archer._guardRandomOffset;
                int depth = archer._guardDepth;
                if (depth > maxDepth) maxDepth = depth;

                // Effective depth = min + depth*spacing + random; clamp the
                // INDEX so the effective depth stays inside bow range.
                float allowed = (DepthClampRange - min - random) / spacing;
                int cap = (int)Math.Floor(Math.Max(0f, allowed));
                if (depth <= cap) continue;

                archer._guardDepth = cap;
                clamped++;
            }

            if (!_loggedDepthClamp)
            {
                _loggedDepthClamp = true;
                // Unconditional first-scan heartbeat: proves Director.Update is
                // patched alive, FindObjectsOfType works and what the 2.4.0
                // guard fields actually hold on live archers.
                System.Text.StringBuilder sample = new System.Text.StringBuilder();
                int sampled = 0;
                for (int i = 0; i < count && sampled < 3; i++)
                {
                    Archer archer = archers[i];
                    if (archer == null || archer.gameObject == null) continue;
                    if (sample.Length > 0) sample.Append(" | ");
                    sample.Append("d=").Append(archer._guardDepth)
                        .Append(" s=").Append(archer._unitSpacingAtWall.ToString("F2"))
                        .Append(" min=").Append(archer._minDistanceFromWall.ToString("F2"))
                        .Append(" rnd=").Append(archer._guardRandomOffset.ToString("F2"))
                        .Append(" side=").Append((int)archer._guardSide);
                    sampled++;
                }
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[DefenseSpacing] first scan: archers=" + count
                    + " maxDepth=" + maxDepth + " clamped=" + clamped
                    + " sample=[" + sample + "]");
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[DefenseSpacing/depth] " + e);
        }
    }
}


[HarmonyPatch(typeof(World), nameof(World.OnLevelLoaded))]
public static class World_DefenseSpacing_Supervisor_Host_Patch
{
    [HarmonyPostfix]
    private static void Postfix(World __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        try
        {
            __instance.StartCoroutine(
                PatchWorld_DefenseSpacing.SupervisorRoutine(__instance).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[DefenseSpacing] supervisor start failed: " + e);
        }
    }
}
