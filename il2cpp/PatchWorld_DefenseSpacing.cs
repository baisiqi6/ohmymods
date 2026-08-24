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

        // Wall-lineup report: fires once per world when at least three knights
        // stand in a depth band behind the border wall (the night formation),
        // listing the rank -> measured depth mapping.  The remap itself runs
        // every pass below: native RankKnights() rewrites 1..N on every hire
        // or loss, so a one-shot remap would silently stop working as the
        // roster grows or changes.
        if (count < 3) return;
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
        if (lined.Count >= 3 && !_loggedKnightLineup)
        {
            _loggedKnightLineup = true;
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
        }

        // Measured live: depth = rank * _distanceFromWall (1.0 per rank, r15 at
        // 15 units behind the wall).  Follower archers trail their knight by
        // knightFollowDistance, so rank N puts its squad's bows at roughly
        // N*spacing + 1 — far past the 8-unit bow range for N > 7.  Remap ranks
        // per side into 1..KnightRankCap each pass; with more knights than cap
        // values some squads share a depth slot (native code has no invariant
        // on rank uniqueness).  The remap is idempotent: once compressed it
        // leaves ranks untouched until native RankKnights() exceeds the cap
        // again after a hire or a loss.
        if (NetworkBigBoss.HasWorldAuth) RemapKnightRanks(knights, kingdom);
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

        // Idempotent guard: once compressed (max rank <= cap) do nothing until
        // a native RankKnights() rewrite pushes max rank past the cap again.
        // This also avoids re-sorting an already-compressed list where equal
        // ranks would let the unstable sort swap knights between passes.
        int maxRank = 0;
        for (int i = 0; i < side.Count; i++)
            if (side[i].rank > maxRank) maxRank = side[i].rank;
        if (maxRank <= KnightRankCap) return 0;

        side.Sort((a, b) => a.rank.CompareTo(b.rank));
        int changed = 0;
        for (int i = 0; i < side.Count; i++)
        {
            // Spread count knights over ranks 1..cap: the i-th of n knights
            // gets 1 + i*cap/n (defensive clamp for degenerate inputs).
            int newRank = 1 + (int)Math.Floor((double)i * KnightRankCap / side.Count);
            if (newRank > KnightRankCap) newRank = KnightRankCap;
            if (side[i].rank == newRank) continue;
            side[i].rank = newRank;
            changed++;
        }
        return changed;
    }

    // ---- night archer lineup report ----------------------------------------
    // 夜间弓箭手列队诊断（只记录不改行为，每侧每世界只输出一次）：
    // 用户报告"守家时一部分弓箭手站在城墙外面"+"站得拥挤"。挂在现有
    // DepthClampPass 扫描里：夜间（director.currentTime >= 17.5 || <= 5.5）
    // 且某侧 depth 在 [-6,8] 区间的弓箭手 >=5 时，输出该侧队列构成——
    // 站到墙外（depth < -0.5）的数量与前三个样本 x、弩手/骑士随从/普通弓
    // 的占比。depth 公式与上面骑士 lineup 相同：(墙x - 单位x) * side。
    // 弩手判定用 PatchRoles_Crossbowman.IsCrossbowman（内部有注册防御，安全）。
    private static bool _loggedArcherLineupLeft;
    private static bool _loggedArcherLineupRight;

    private static void ScanNightArcherLineup(Kingdom kingdom, Archer[] archers)
    {
        try
        {
            // 两侧都已输出过 → 零开销早退
            if (_loggedArcherLineupLeft && _loggedArcherLineupRight) return;
            Director director = Managers.Inst != null ? Managers.Inst.director : null;
            if (director == null) return;
            float t = director.currentTime;
            if (!(t >= 17.5f || t <= 5.5f)) return; // 非夜间

            ReportArcherLineupSide(kingdom, archers, Side.Left,
                ref _loggedArcherLineupLeft, "L");
            ReportArcherLineupSide(kingdom, archers, Side.Right,
                ref _loggedArcherLineupRight, "R");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[DefenseSpacing/archer-lineup] " + e);
        }
    }

    /// <summary>
    /// 单侧夜间弓箭手列队报告（纯读）。total=depth 在 [-6,8] 区间的该侧弓箭手
    /// 总数；outside=其中 depth&lt;-0.5（站到墙外）的数量，outsideSample 为前
    /// 三个墙外弓箭手的 x 坐标；xbow/followers/plain 为同一集合内弩手、
    /// 有 _knight 的骑士随从、其余普通弓的数量（三分类互斥，plain=total-其余）。
    /// </summary>
    private static void ReportArcherLineupSide(Kingdom kingdom, Archer[] archers,
        Side side, ref bool logged, string sideLabel)
    {
        if (logged) return;
        float sign = (float)side;
        float wall = kingdom.GetBorderSideIntact(side);

        int inBand = 0, outside = 0, xbow = 0, followers = 0;
        var outsideSample = new System.Collections.Generic.List<float>();
        for (int i = 0; i < archers.Length; i++)
        {
            Archer archer = archers[i];
            if (archer == null || archer.gameObject == null
                || !archer.gameObject.activeInHierarchy) continue;
            if (archer._guardSide != side) continue;

            // depth 同骑士 lineup 公式；区间外（未列队/游荡）的不进报告
            float depth = (wall - archer.transform.position.x) * sign;
            if (depth < -6f || depth > 8f) continue;
            inBand++;

            if (depth < -0.5f)
            {
                outside++;
                if (outsideSample.Count < 3)
                    outsideSample.Add(archer.transform.position.x);
            }
            if (PatchRoles_Crossbowman.IsCrossbowman(archer)) xbow++;
            else if (archer._knight != null) followers++; // HasKnight 等价判 _knight
        }
        if (inBand < 5) return;

        logged = true;
        System.Text.StringBuilder sample = new System.Text.StringBuilder();
        for (int i = 0; i < outsideSample.Count; i++)
        {
            if (i > 0) sample.Append(',');
            sample.Append(outsideSample[i].ToString("F1"));
        }
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[DefenseSpacing] archer lineup: side=" + sideLabel
            + " total=" + inBand
            + " outside=" + outside
            + " xbow=" + xbow
            + " followers=" + followers
            + " plain=" + (inBand - xbow - followers)
            + " outsideSample=[" + sample + "]");
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
        // New world (island hop / new campaign): re-arm every one-shot
        // report so coverage and logging restart.
        _loggedHeartbeat = false;
        _loggedDepthClamp = false;
        _loggedKnightSample = false;
        _loggedKnightLineup = false;
        _loggedKnightRemap = false;
        // 夜间弓箭手列队诊断：每世界每侧重新武装
        _loggedArcherLineupLeft = false;
        _loggedArcherLineupRight = false;
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

            // 夜间弓箭手列队诊断（纯读，每侧每世界一次）
            ScanNightArcherLineup(kingdom, archers);

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
