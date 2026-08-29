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

    /// <summary>
    /// 骑士扫描/诊断/rank 压缩（原逻辑不变）。返回本拍 FindObjectsOfType 的
    /// Knight[]（供同 pass 复用的数组返回；碰撞恢复探测已随 always-off 移除）
    /// 不为此再扫一遍场景。
    /// </summary>
    private static Knight[] ScanKnights()
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
        if (count < 3) return knights;
        Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
        if (kingdom == null) return knights;

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
        return knights;
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

    // ---- SetGoal(float,float) host: day knight spread + night archer mirror --
    // Knight path ground truth (Knight.cs Assemble, 2.1.0 lines 662-680): the
    // daytime idle assembly walks EVERY knight to the SAME point — banner.x +
    // side*3 (or border + side*4 with no banner) plus Random.Range(-1,1) —
    // then re-walks every ~10s (WaitForSeconds(10) at the coroutine tail).
    // It never reads rank: rank only positions the NIGHT wall lineup
    // (GetTargetPos, Knight.cs:656), and RemapKnightRanks above is the v2.1.0
    // all-day compression (1..KnightRankCap) restored verbatim so the night
    // compact defense keeps its accepted semantics.  With 19 knights the
    // same-point walk is a single clump with follower skins buried (user
    // report: knights clump by day, no follower skin visible).  Fix at the
    // movement layer: intercept the Mover.SetGoal(float,float) call Assemble
    // issues and push x out to a per-knight EXCLUSIVE slot.  First attempt
    // used rank*0.75, but measured live (knightstyle session) the compressed
    // ranks are shared ~3-per-slot — still a small clump with ±1 jitter.  Now
    // each side hands out stable first-seen indexes 0..N-1 (instanceID-keyed,
    // per world) and the slot depth is dayIndex*1.2: ~10 knights per side =
    // a ~12-unit strolling band, visually distinguishable, and every 10s
    // native re-walk re-rolls the ±0.5 for an idle-strolling look.
    // Archer path (night goal mirror, measured follow-up): with friendly
    // collision already off, side=R still had outside=15 parked in a narrow
    // band at wall+1.5..2.0 (side=L clean) — that residue is NOT push-out:
    // the native guard-goal ASSIGNMENT itself places those archers just
    // outside the wall.  Mirror such goals to the same depth inside.
    // Mover.SetGoal(float,float) is a shared hot path (archer hunting etc.),
    // so a static mover-instanceID -> unit-type cache gates it (0=other,
    // 1=knight, 2=archer): one GetComponent probe pair per mover, everyone
    // else permanently skipped after the first verdict.
    private static readonly System.Collections.Generic.Dictionary<int, int> _moverUnitType =
        new System.Collections.Generic.Dictionary<int, int>();
    private static bool _loggedDaySpread;
    private static readonly System.Collections.Generic.Dictionary<int, int> _dayIndexLeft =
        new System.Collections.Generic.Dictionary<int, int>();
    private static readonly System.Collections.Generic.Dictionary<int, int> _dayIndexRight =
        new System.Collections.Generic.Dictionary<int, int>();
    // True while our own redirected SetGoal call is in flight, so the prefix
    // passes it through instead of re-intercepting (index-0 slots land at
    // dayZone ±0.5 — inside the 1.6 intercept band — and would otherwise
    // recurse forever; also guards the night mirror redirect).
    private static bool _inSetGoalRedirect;
    private static readonly Side[] MirrorSides = { Side.Left, Side.Right };
    private static bool _loggedNightMirror;

    /// <summary>
    /// Prefix body for Mover.SetGoal(float, float).  Dispatches by cached
    /// mover unit type: knights take the daytime Assemble spread, archers
    /// take the night guard-goal mirror.  Returns false (skip the native
    /// call, goal replaced) only when a branch rewrote the goal; true
    /// (native call untouched) for everything else.
    /// </summary>
    internal static bool DayAssembleSpreadPrefix(Mover mover, float goal, float speed)
    {
        try
        {
            if (!ModConfig.Enabled.Value || mover == null) return true;
            if (_inSetGoalRedirect) return true; // our own redirected call

            // Fast path: cached unit-type verdict per mover instance.  Pooled
            // movers keep their components, so the verdict never flips.
            int id = mover.GetInstanceID();
            if (!_moverUnitType.TryGetValue(id, out int unitType))
            {
                if (mover.GetComponent<Knight>() != null) unitType = 1;
                else if (mover.GetComponent<Archer>() != null) unitType = 2;
                _moverUnitType[id] = unitType;
            }
            if (unitType == 1) return KnightDayAssembleSpread(mover, goal, speed);
            if (unitType == 2) return MirrorNightArcherGoal(mover, goal, speed);
            return true;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[DefenseSpacing/day-spread] " + e);
            return true;
        }
    }

    /// <summary>
    /// Knight branch of the SetGoal(float,float) prefix: the daytime
    /// Assemble spread (see the section comment for grounds).
    /// </summary>
    private static bool KnightDayAssembleSpread(Mover mover, float goal, float speed)
    {
        Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
        if (kingdom == null || !kingdom.isDaytime) return true;

        Knight knight = mover.GetComponent<Knight>();
        if (knight == null || knight.gameObject == null) return true;
        float side = (float)knight.side;
        if (side == 0f) return true;

        // dayZone mirrors Assemble's own target (Knight.cs): banner.x +
        // side*3, falling back to border + side*4 when the banner slot is
        // missing or the access throws.
        float dayZone;
        try
        {
            PayableBorder banner = kingdom.borderBanner != null
                ? kingdom.borderBanner[knight.side] : null;
            dayZone = banner != null && banner.transform != null
                ? banner.transform.position.x + side * 3f
                : kingdom.GetBorderSide(knight.side) + side * 4f;
        }
        catch
        {
            dayZone = kingdom.GetBorderSide(knight.side) + side * 4f;
        }

        // Only intercept the Assemble walk: |goal - dayZone| <= 1.6 (the
        // native target is dayZone ±1).  Night wall targets sit at wall -
        // depth and fall outside this band.
        if (Math.Abs(goal - dayZone) > 1.6f) return true;

        // Per-side stable exclusive slot index (first-seen assignment
        // 0..N-1, keyed by knight instanceID, reset per world): with
        // ranks shared ~3-per-slot after the all-day compression, rank
        // cannot give each knight its own strolling depth — the index
        // does.  ~10 knights per side at 1.2 spacing = ~12-unit band.
        System.Collections.Generic.Dictionary<int, int> indexes =
            knight.side == Side.Left ? _dayIndexLeft : _dayIndexRight;
        int knightId = knight.gameObject.GetInstanceID();
        if (!indexes.TryGetValue(knightId, out int dayIndex))
        {
            dayIndex = indexes.Count;
            indexes[knightId] = dayIndex;
        }
        float newX = dayZone - side * (dayIndex * 1.2f)
            + UnityEngine.Random.Range(-0.5f, 0.5f);
        if (!_loggedDaySpread)
        {
            _loggedDaySpread = true;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[DefenseSpacing] day assemble spread active: dayIndex="
                + dayIndex + " newX=" + newX.ToString("F2"));
        }
        // Guard: index-0 slots land at dayZone ±0.5, INSIDE the 1.6 band,
        // so our own redirected SetGoal would re-enter this prefix and
        // re-roll forever (index 1 recursed ~90% of calls too).  The
        // guard passes our redirect straight through; the 10s native
        // re-walk still re-rolls ±0.5 every cycle.
        _inSetGoalRedirect = true;
        try { mover.SetGoal(newX, speed); }
        finally { _inSetGoalRedirect = false; }
        return false; // skip the native SetGoal
    }

    /// <summary>
    /// Archer branch of the SetGoal(float,float) prefix: night guard-goal
    /// mirror.  The native guard assignment itself places some archers in a
    /// narrow band JUST outside the intact wall (measured with collision
    /// already off: side=R outside=15 at wall+1.5..2.0, side=L clean).  A
    /// goal whose depth relative to EITHER wall sits in the outside narrow
    /// band (0.2..2.5 steps, i.e. depth in (−2.5, −0.2]) is mirrored to the
    /// same depth plus 0.5 INSIDE the wall (0.7..3.0 steps).  Only the
    /// narrow band matches: daytime hunting goals and night chase/coin goals
    /// farther outside are untouched.
    /// </summary>
    private static bool MirrorNightArcherGoal(Mover mover, float goal, float speed)
    {
        Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
        if (kingdom == null) return true;
        Director director = Managers.Inst.director;
        if (director == null) return true;
        float t = director.currentTime;
        if (!(t >= 17.5f || t <= 5.5f)) return true; // 白天狩猎目标不碰

        for (int i = 0; i < MirrorSides.Length; i++)
        {
            Side side = MirrorSides[i];
            float sign = (float)side;
            if (sign == 0f) continue;
            float wall = kingdom.GetBorderSideIntact(side);
            float depth = (wall - goal) * sign;
            if (depth <= -2.5f || depth > -0.2f) continue; // 不在墙外窄带

            // 塔位弓箭手跳过（实测严重误伤："箭塔弓箭手在天上走"——塔
            // 守位 x 本就落在墙外窄带（日志 goal mirrored: 131.05->126.63
            // 即塔位），镜像把塔上弓箭手的目标改写成墙内地面 x，mover 驱动
            // 其在塔高度横走）。两道防线：a) 守位状态；b) 塔上高度。
            Archer archer = mover.GetComponent<Archer>();
            if (archer != null && archer.inGuardSlot) return true;
            if (mover.transform.position.y > 2.5f) return true; // 塔上高度，地面单位 y 通常 <2

            // 镜像到墙内同深+0.5（0.7~3.0 步）。墙内 N 步 = wall − side×N
            //（与 Knight.GetTargetPos / 锚点拉回同一符号约定）。
            float newX = wall - sign * (0.5f + Math.Abs(depth));
            if (!_loggedNightMirror)
            {
                _loggedNightMirror = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[DefenseSpacing] night archer goal mirrored inside: "
                    + goal.ToString("F2") + " -> " + newX.ToString("F2"));
            }
            // 递归保护：镜像后目标在墙内（depth +0.7..+3.0），不再落入
            // (−2.5,−0.2] 窄带，重入天然出套；redirect 标志再兜一层。
            _inSetGoalRedirect = true;
            try { mover.SetGoal(newX, speed); }
            finally { _inSetGoalRedirect = false; }
            return false; // skip the native SetGoal
        }
        return true;
    }

    // ---- night follower anchor pullback ------------------------------------
    // Measured (knightstyle session, side=R): 42 in-band archers with
    // followers=40, outside=24 standing at wall+3.0..3.6 while their knights
    // held r3@0.6..r7@1.8 just inside the wall.  The follower formation
    // (SetGoal(knight.gameObject, speed, -knightFollowDistance, Formation),
    // Archer.cs:486) anchors on the knight: once the knight stands at the
    // wall the formation's front slots (4 followers x unit spacing) cross to
    // the OUTSIDE and the squad's bows stand beyond the wall.  Fix: at night,
    // when a knight-following Archer's formation anchor is closer to the
    // intact wall than the pullback, replace the formation goal with a plain
    // position goal pulled inside by a PER-STYLE depth (KnightStyle API):
    // deadlands squads are crossbowmen (range 12 — standing deeper costs
    // nothing and the user confirmed wall-hugging triggers lob shots) → 6.5;
    // every other squad (and unknown style → null-safe fallback) → 4.2, which
    // keeps front slots ~2 and rear slots ~6 inside, all within the 8-unit
    // range the v2.1.0 depth clamp protects.  Daytime following is untouched
    // (isDaytime gate) and non-Archer Formation callers
    // (Knight.OnEmbarkStart boat boarding etc.) never match the Archer gate.
    private static readonly System.Collections.Generic.Dictionary<int, int> _moverIsArcher =
        new System.Collections.Generic.Dictionary<int, int>();
    private static bool _loggedNightPull;

    /// <summary>
    /// Prefix body for Mover.SetGoal(GameObject, float, float, OffsetMode).
    /// Returns false (skip the native call, anchor pulled inside the wall)
    /// only for a nighttime knight-following Archer formation walk; true
    /// (native call untouched) for everything else.
    /// </summary>
    internal static bool NightFollowerAnchorPrefix(Mover mover, GameObject goal,
        float speed, float offset, Mover.OffsetMode offsetMode)
    {
        try
        {
            if (!ModConfig.Enabled.Value || mover == null || goal == null) return true;
            if (offsetMode != Mover.OffsetMode.Formation) return true;

            // Fast path: cached is-archer verdict per mover instance (same
            // pattern as the is-knight cache above).
            int id = mover.GetInstanceID();
            if (!_moverIsArcher.TryGetValue(id, out int isArcher))
            {
                isArcher = mover.GetComponent<Archer>() != null ? 1 : 0;
                _moverIsArcher[id] = isArcher;
            }
            if (isArcher == 0) return true;

            Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
            if (kingdom == null || kingdom.isDaytime) return true;

            Archer archer = mover.GetComponent<Archer>();
            if (archer == null || archer._knight == null) return true;
            Knight knight = archer._knight;
            if (knight.gameObject == null) return true;
            float side = (float)knight.side;
            if (side == 0f) return true;

            float wall = kingdom.GetBorderSideIntact(knight.side);
            // Formation target = goal object x + offset (native multiplies the
            // offset by the goal's localScale.x facing sign, Mover.cs:161; the
            // plain sum is within 0.3 of that — close enough for the band test).
            float anchorX = goal.transform.position.x + offset;
            // 拉回量按骑士风格取（KnightStyle API，判空/查不到回落 4.2）：
            // 死地随从=弩手（射程 12，站深不打折、避开贴墙高抛）→ 6.5；
            // 普通弓随从（射程 8）→ 4.2，再深会把后排推出射程。
            float pullback = PatchRoles_KnightStyle.GetFollowerAnchorPullback(knight);
            if ((wall - anchorX) * side < pullback)
            {
                float newAnchor = wall - side * pullback;
                if (!_loggedNightPull)
                {
                    _loggedNightPull = true;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        "[DefenseSpacing] night follower anchor pulled inside: knight@"
                        + goal.transform.position.x.ToString("F1")
                        + " anchor " + anchorX.ToString("F1")
                        + "->" + newAnchor.ToString("F1"));
                }
                // Float overload; this mover is an Archer, so the day-spread
                // prefix's is-knight cache passes it straight through (no
                // recursion), and it is night anyway.
                mover.SetGoal(newAnchor, speed);
                return false; // skip the native formation goal
            }
            return true; // anchor already deep enough inside — native follow
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[DefenseSpacing/night-anchor] " + e);
            return true;
        }
    }

    // ---- night archer lineup report ----------------------------------------
    // 夜间弓箭手列队诊断（只记录不改行为，每侧每世界只输出一次）：
    // 用户报告"守家时一部分弓箭手站在城墙外面"+"站得拥挤"。挂在现有
    // DepthClampPass 扫描里：夜间（director.currentTime >= 17.5 || <= 5.5）
    // 且某侧 depth 在 [-6,10] 区间的弓箭手 >=5 时，输出该侧队列构成——
    // 站到墙外（depth < -0.5）的数量与前三个样本 x、弩手/骑士随从/普通弓
    // 的占比。depth 公式与上面骑士 lineup 相同：(墙x - 单位x) * side。
    // 侧归属按位置而非 _guardSide：实测（knightstyle6 会话一整夜零输出）
    // 很多守家弓箭手 _guardSide 疑为中性 0，按侧过滤会全部漏掉。
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
    /// 单侧夜间弓箭手列队报告（纯读）。total=depth 在 [-6,10] 区间的该侧弓箭手
    /// 总数（侧归属纯按位置，不读 _guardSide）；outside=其中 depth&lt;-0.5
    /// （站到墙外）的数量，outsideSample 为前三个墙外弓箭手的 x 坐标；
    /// xbow/followers/plain 为同一集合内弩手、有 _knight 的骑士随从、其余
    /// 普通弓的数量（三分类互斥，plain=total-其余）。
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

            // depth 同骑士 lineup 公式；区间外（未列队/游荡）的不进报告。
            // 侧归属按位置（depth 相对该侧墙落在带内即算），不读 _guardSide。
            float depth = (wall - archer.transform.position.x) * sign;
            if (depth < -6f || depth > 10f) continue;
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

    // ---- night friendly-collision toggle -------------------------------------
    // Root cause chain (user-confirmed, scout-verified): the outside-the-wall
    // overflow is FRIENDLY units colliding with each other — the 2D physics
    // between same-layer units IS the density cap in essence (there is no
    // numeric knob for it anywhere in the code).  PushablePusher is the
    // combat push component and is unrelated to density — do not touch it.
    // Layer names are serialized on the prefab (never written in code), so
    // sample them at runtime: the collider-diagnostic measured
    // colliders=[10,17] on one archer — colliders live on children at a
    // DIFFERENT layer than the root, so the ignore must cover EVERY pair in
    // the sampled layer set, not just the root self-pair (a 10-10-only
    // ignore left 17-layer friendly collisions still pushing).
    // Fix: ignore the units-self layer pair during the night window — the
    // wall-front crowd stops being expelled, and the deep-relocation sweep
    // below degrades to a rare no-op fallback.  Accepted cost (user knows and
    // accepts): friendly units may visually overlap when crowded — day or
    // night.  ALWAYS-OFF (v3 final): the timed restore (dawn, then 8:00
    // grace) and even the conditional restore (re-enable only when the crowd
    // probe found no overlapping pair) each still measured units blasted
    // skyward by day ("白天仍有人被挤上天") — every re-enable instant risks
    // meeting an interpenetrating pair, and physics depenetration force is
    // proportional to overlap depth.  User ruling: the wall push-out and the
    // sky-launch share ONE root cause (friendly collision), so the collision
    // stays off permanently — no restore branch, no probe, no scan cost.
    // Trade-off (accepted): crowded units pass through each other visually.
    // Projectiles and enemies live on separate layers: IgnoreLayerCollision
    // edits only the units-self pairs, so arrows and enemy hits unaffected.
    // The flag deliberately does NOT reset per world: Physics2D's
    // IgnoreLayerCollision state is global and survives scene loads.
    // Mechanism layering (measured follow-up): collision-off cures PUSH-OUT,
    // but the native guard-goal ASSIGNMENT itself still places some archers
    // just outside the wall — that residue is cured by the night archer goal
    // MIRROR in the SetGoal(float,float) prefix host above; the deep
    // relocation sweep below stays as the final fallback.
    private static bool _friendlyCollisionIgnored;
    private static readonly System.Collections.Generic.List<int> _friendlyCollisionLayers =
        new System.Collections.Generic.List<int>();

    private static void ToggleFriendlyCollision(Archer[] archers, Knight[] knights)
    {
        try
        {
            if (_friendlyCollisionIgnored) return; // 一次性：全局矩阵跨场景存活

                // Sample any active archer this pass (layer names are
                // prefab-serialized, not code-visible).
                if (archers == null) return;
                Archer sample = null;
                for (int i = 0; i < archers.Length; i++)
                {
                    Archer archer = archers[i];
                    if (archer == null || archer.gameObject == null
                        || !archer.gameObject.activeInHierarchy) continue;
                    sample = archer;
                    break;
                }
                if (sample == null) return; // 本拍无可采样，下拍再试

                // Layer SET = every collider in the hierarchy (colliders may
                // live on children at a DIFFERENT layer — diagnostic measured
                // colliders=[10,17], so the earlier 10-10-only ignore left
                // 17-layer friendly collisions still pushing) plus the root
                // layer itself.
                _friendlyCollisionLayers.Clear();
                int rootLayer = sample.gameObject.layer;
                if (rootLayer >= 0 && !_friendlyCollisionLayers.Contains(rootLayer))
                    _friendlyCollisionLayers.Add(rootLayer);
                Collider2D[] colliders = sample.GetComponentsInChildren<Collider2D>();
                if (colliders != null)
                {
                    for (int j = 0; j < colliders.Length; j++)
                    {
                        Collider2D collider = colliders[j];
                        if (collider == null || collider.gameObject == null) continue;
                        int colliderLayer = collider.gameObject.layer;
                        if (colliderLayer < 0
                            || _friendlyCollisionLayers.Contains(colliderLayer)) continue;
                        _friendlyCollisionLayers.Add(colliderLayer);
                    }
                }
                if (_friendlyCollisionLayers.Count == 0) return;

                // Ignore EVERY pair in the set (self pairs and cross pairs):
                // with layers {10,17} that is 10-10, 10-17, 17-10, 17-17.
                System.Text.StringBuilder layersText =
                    new System.Text.StringBuilder();
                System.Text.StringBuilder pairsText =
                    new System.Text.StringBuilder();
                for (int a = 0; a < _friendlyCollisionLayers.Count; a++)
                {
                    if (layersText.Length > 0) layersText.Append(',');
                    layersText.Append(_friendlyCollisionLayers[a]);
                    for (int b = 0; b < _friendlyCollisionLayers.Count; b++)
                    {
                        Physics2D.IgnoreLayerCollision(_friendlyCollisionLayers[a],
                            _friendlyCollisionLayers[b], true);
                        if (pairsText.Length > 0) pairsText.Append(',');
                        pairsText.Append(_friendlyCollisionLayers[a])
                            .Append('-').Append(_friendlyCollisionLayers[b]);
                    }
                }
                _friendlyCollisionIgnored = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[DefenseSpacing] friendly collision permanently off: layers=["
                    + layersText + "] ignored=[" + pairsText + "]");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[DefenseSpacing/friendly-collision] " + e);
        }
    }

    // ---- day crowd spread: native hunt-position re-roll ----------------------
    // 用户方案：友军碰撞永久关闭（上一节）根治了墙外推挤+天弹，代价是拥挤
    // 时单位视觉互穿——碰撞关了就没有"物理散开"，本机制用**原生站位分配
    // 公式**补位散开：白天密度过大时对重叠的弓箭手重掷一次原生白天狩猎
    // 位，让它们按原生逻辑自行走开。公式出处 game-source/
    // Assembly-CSharp-2.1.0/Archer.cs:597（狩猎路径原生自用）：
    //   x = kingdom.GetBorderSide(_guardSide)
    //       + (float)_guardSide * Random.Range(borderHuntRange.min,
    //                                         borderHuntRange.max)
    // 同行 num4 = this.walkSpeed（Archer.cs:598）。照抄原生：狩猎在墙外
    // 方向随机，且原生狩猎公式用的就是 GetBorderSide（本文件夜间 lineup
    // 用的 GetBorderSideIntact 是另一语义，不混用）。borderHuntRange 是
    // Archer 公开 FloatRange 字段，interop 直接暴露。
    // 跳过项：
    //   - _knight 非空：骑士随从的编队位由原生跟队目标（Archer.cs:486
    //     SetGoal(knight.gameObject, ..., Formation)）统一管理，重掷会与
    //     编队目标打架（走一半被拉回队），不碰；
    //   - inGuardSlot / y>2.5：塔守位是原生指派的固定岗——实测塔守位 x
    //     本就落在墙外带、且单位在塔高度（见 MirrorNightArcherGoal 的
    //     误伤记录"箭塔弓箭手在天上走"），重掷会让塔上弓箭手在空中横走。
    // 冷却（防抖）：巡检 3s 一拍，而走动途中两两位置仍会重叠——不冷却会
    // 每 3s 对同一对单位连续重掷造成抖动（刚要走开又被重掷到别处）。
    // instanceID → 下次可重掷时刻（Time.time），冷却 20 秒，每世界 Clear。
    // 与夜间规则互斥：本节只在白天窗口跑（currentTime 非夜间）；夜间的
    // goal 镜像/深定位（MirrorNightArcherGoal / NightParkedFollowerSweep）
    // 各自判夜窗口，白天不动，反之亦然。
    private static bool _loggedDayCrowdSpread;
    private const float CrowdOverlapDy = 1.5f;   // 同层判定（|dy| 阈值）
    private const float CrowdOverlapDx = 0.55f;  // 半身位重叠（|dx| 阈值）
    private const float DaySpreadCooldown = 20f; // 同单位重掷冷却（秒）
    private static readonly System.Collections.Generic.Dictionary<int, float>
        _daySpreadNextRollAt = new System.Collections.Generic.Dictionary<int, float>();

    private static void DayCrowdSpread(Kingdom kingdom, Archer[] archers, int count)
    {
        try
        {
            // 白天窗口：director 非空且非夜间（夜间 = t>=17.5 || t<=5.5）
            Director director = Managers.Inst != null ? Managers.Inst.director : null;
            if (director == null) return;
            float t = director.currentTime;
            if (t >= 17.5f || t <= 5.5f) return; // 夜间：本节不跑

            // 预取 active 单位与位置到托管数组（interop 封送 O(n) 化：
            // transform.position 每次访问都过封送层，pairwise 探测必须在
            // 纯托管 Vector3[] 上做，不能每对重复封送）。
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                Archer probe = archers[i];
                if (probe == null || probe.gameObject == null
                    || !probe.gameObject.activeInHierarchy) continue;
                n++;
            }
            if (n < 2) return;
            Archer[] active = new Archer[n];
            Vector3[] positions = new Vector3[n];
            int fill = 0;
            for (int i = 0; i < count && fill < n; i++)
            {
                Archer archer = archers[i];
                if (archer == null || archer.gameObject == null
                    || !archer.gameObject.activeInHierarchy) continue;
                active[fill] = archer;
                positions[fill] = archer.transform.position;
                fill++;
            }

            // pairwise 重叠探测（散开触发器）：|dy|<1.5 同层 && |dx|<0.55
            // 半身位 → 该对重叠，双方 overlapCount 各 +1。
            int[] overlapCount = new int[n];
            int crowdedPairs = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = positions[i];
                for (int j = i + 1; j < n; j++)
                {
                    Vector3 b = positions[j];
                    float dy = a.y - b.y;
                    if (dy > CrowdOverlapDy || dy < -CrowdOverlapDy) continue;
                    float dx = a.x - b.x;
                    if (dx > CrowdOverlapDx || dx < -CrowdOverlapDx) continue;
                    overlapCount[i]++;
                    overlapCount[j]++;
                    crowdedPairs++;
                }
            }
            if (crowdedPairs == 0) return;

            // 对 overlapCount>0 的弓箭手重掷原生狩猎位（跳过项见节注释）。
            float now = Time.time;
            int rerolled = 0;
            for (int i = 0; i < n; i++)
            {
                if (overlapCount[i] <= 0) continue;
                Archer archer = active[i];
                if (archer._knight != null) continue;  // 随从：编队位原生管理
                if (archer.inGuardSlot) continue;      // 塔位：原生固定岗
                if (positions[i].y > 2.5f) continue;   // 塔上高度

                int id = archer.gameObject.GetInstanceID();
                if (_daySpreadNextRollAt.TryGetValue(id, out float nextAt)
                    && now < nextAt) continue;         // 冷却防抖

                Side side = archer._guardSide;
                if (side != Side.Left && side != Side.Right)
                {
                    // _guardSide 中性（实测常见）：按近墙侧归属（同
                    // NightParkedFollowerSweep 的位置判定）。
                    float x = positions[i].x;
                    side = Mathf.Abs(x - kingdom.GetBorderSideIntact(Side.Left))
                        <= Mathf.Abs(x - kingdom.GetBorderSideIntact(Side.Right))
                        ? Side.Left : Side.Right;
                }
                float sideSign = (float)side;
                if (sideSign == 0f) continue;

                // 原生公式照抄（Archer.cs:597-598）：墙外方向随机狩猎位，
                // walkSpeed 步行前往。SetGoal(float,float) 过自家 prefix：
                // 该 mover 缓存为 archer → MirrorNightArcherGoal → 白天
                // 窗口直接放行，无递归、目标不被夜间镜像改写。
                FloatRange huntRange = archer.borderHuntRange;
                float goal = kingdom.GetBorderSide(side)
                    + sideSign * UnityEngine.Random.Range(huntRange.min, huntRange.max);
                archer._mover.SetGoal(goal, archer.walkSpeed);
                _daySpreadNextRollAt[id] = now + DaySpreadCooldown;
                rerolled++;
            }

            if (!_loggedDayCrowdSpread)
            {
                _loggedDayCrowdSpread = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[DefenseSpacing] day crowd spread: crowded=" + crowdedPairs
                    + " re-rolled=" + rerolled);
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[DefenseSpacing/day-crowd] " + e);
        }
    }

    // ---- night parked follower sweep + deep relocation -----------------------
    // Measured: the anchor pullback above only acts when a follower ISSUES a
    // follow goal (a SetGoal call).  Followers restored from a save park at
    // their old saved position — outside the wall — because nothing ever
    // issues them a new goal; they only return inside once enemies arrive and
    // the native flee/re-follow cycle re-issues SetGoal ("逃跑后重跟队才
    // 归位").  Patrol fix part 1: during the night window, re-issue the
    // NATIVE formation follow goal (Archer.cs:486 form) for every active
    // knight-follower standing outside the intact wall; the call flows through
    // our own NightFollowerAnchorPrefix, which pulls the anchor 4.2 units
    // inside, so the follower walks back on its own.
    // Part 2 — deep relocation (rework of the hard floor): plain knight-less
    // archers ALSO end up outside at night: the native crowding/push system
    // squeezes the high-density wall crowd outward, and native mover goals
    // carry no outside-the-wall constraint (弩手同样是守家单位，一并覆盖).
    // The first fix TELEPORTED outside archers to 0.6 inside — measured
    // result: the density cap pushed them right back out, so the visible
    // behaviour was "snap inside, spread back out, repeat": the teleport
    // fights the push system and cannot cure it (131 archers simply overflow
    // the wall-front space; the overflow has no directional constraint).
    // Root-cause-compatible fix: WALK them deep instead — issue a position
    // goal 8..18 units inside the wall at walkSpeed; the deep rear space is
    // low-density, so the push system no longer expels them.  If a native
    // guard goal later drags one back into the crowded strip and it gets
    // squeezed out again, the next 3s sweep simply relocates it again — a
    // walking loop, not a teleport snap, which reads as natural movement.
    // Followers take ONLY the re-goal path (continue below), never both.
    // Both parts are naturally rate limited: only units standing outside
    // match, and once inside they stop matching (depth >= -0.5 gate).
    // Layering note: with the night friendly-collision toggle above active,
    // the overflow this sweep reacts to stops at the SOURCE — the deep
    // relocation below remains only as a rare no-op fallback (e.g. units
    // already outside when the collision was switched off).
    private static bool _loggedNightRegoal;
    private static bool _loggedNightRelocate;

    private static void NightParkedFollowerSweep(Kingdom kingdom, Archer[] archers)
    {
        try
        {
            Director director = Managers.Inst != null ? Managers.Inst.director : null;
            if (director == null) return;
            float t = director.currentTime;
            if (!(t >= 17.5f || t <= 5.5f)) return; // 非夜间

            for (int i = 0; i < archers.Length; i++)
            {
                Archer archer = archers[i];
                if (archer == null || archer.gameObject == null
                    || !archer.gameObject.activeInHierarchy) continue;

                Knight knight = archer._knight;
                if (knight != null && knight.gameObject != null)
                {
                    // Part 1: knight-follower — re-goal path ONLY.
                    float knightSide = (float)knight.side;
                    if (knightSide == 0f) continue;
                    float followerDepth = (kingdom.GetBorderSideIntact(knight.side)
                        - archer.transform.position.x) * knightSide;
                    if (followerDepth >= -0.5f) continue; // 墙内或墙线上，不动

                    if (!_loggedNightRegoal)
                    {
                        _loggedNightRegoal = true;
                        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                            "[DefenseSpacing] night parked follower re-goaled: x="
                            + archer.transform.position.x.ToString("F1"));
                    }
                    // 原生跟队目标重发（与 Archer.cs:486 完全同参）；随后的
                    // NightFollowerAnchorPrefix 会把锚点钳到墙内 4.2 步。
                    archer._mover.SetGoal(knight.gameObject, archer.runSpeed,
                        -archer.knightFollowDistance, Mover.OffsetMode.Formation);
                    continue; // 有骑士随从只走重发路径，绝不再叠硬地板
                }

                // Part 2: knight-less archers (plain bows AND crossbowmen —
                // both are defenders that belong inside at night).
                // 塔位弓箭手跳过（同 MirrorNightArcherGoal 的两道防线：实测
                // 日志 re-located deep: x=130.8 疑似同因——塔守位在墙外窄
                // 带/塔高度，不属于墙外滞留，重定位会让塔上弓箭手在天上走）。
                if (archer.inGuardSlot) continue;
                if (archer.transform.position.y > 2.5f) continue; // 塔上高度

                Side side = archer._guardSide;
                float x = archer.transform.position.x;
                if (side != Side.Left && side != Side.Right)
                {
                    // _guardSide 中性（实测常见）：按与两侧墙距离近的一侧。
                    float wallLeft = kingdom.GetBorderSideIntact(Side.Left);
                    float wallRight = kingdom.GetBorderSideIntact(Side.Right);
                    side = Mathf.Abs(x - wallLeft) <= Mathf.Abs(x - wallRight)
                        ? Side.Left : Side.Right;
                }
                float sideSign = (float)side;
                if (sideSign == 0f) continue;
                float wall = kingdom.GetBorderSideIntact(side);
                if ((wall - x) * sideSign >= -0.5f) continue; // 墙内或墙线上

                // 深处重定位：不下发原地钳位（瞬移），改发墙内 8~18 步深
                // 处的位置目标，以 walkSpeed 步行回位（Archer.cs:598 狩猎
                // 路径同款公开字段）。深处密度低，推挤不再把人挤出墙。
                float deep = UnityEngine.Random.Range(8f, 18f);
                if (!_loggedNightRelocate)
                {
                    _loggedNightRelocate = true;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        "[DefenseSpacing] night outside archer re-located deep: x="
                        + x.ToString("F1") + " -> wall-" + deep.ToString("F1"));
                }
                // 防抖：巡检周期即 3s，同一弓箭手两次下发间隔天然 >= 3s
                // （走到墙内即不再触发），无需额外时间戳字典。SetGoal 走
                // float 重载：该 mover 非 Knight，day-spread prefix 的
                // is-knight 缓存直接放行，且本就在夜间，无递归。
                archer._mover.SetGoal(wall - sideSign * deep, archer.walkSpeed);
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[DefenseSpacing/night-regoal] " + e);
        }
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
        // 白天踱步独占索引按世界重置（新世界骑士集合全新，0..N-1 重新分配）
        _dayIndexLeft.Clear();
        _dayIndexRight.Clear();
        // 夜间弓箭手列队诊断：每世界每侧重新武装
        _loggedArcherLineupLeft = false;
        _loggedArcherLineupRight = false;
        // 白天拥挤散开：一次性日志重新武装 + 重掷冷却字典清空
        //（新世界单位集合全新，旧 instanceID 的冷却时间无意义）
        _loggedDayCrowdSpread = false;
        _daySpreadNextRollAt.Clear();
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

            // knights 数组传参复用本拍扫描（骑士诊断/压缩也消费）
            Knight[] knights = ScanKnights();
            if (kingdom.Archers == null) return;
            Archer[] archers = UnityEngine.Object.FindObjectsOfType<Archer>();
            int count = archers != null ? archers.Length : 0;

            // 友军碰撞永久关闭（治本，一次性）：units-self 全层对 Ignore，
            // 不再恢复——墙外推挤与天弹同源，根治即不再开。
            ToggleFriendlyCollision(archers, knights);

            // 白天拥挤重分配：碰撞已关=无物理散开，密度过大时对重叠弓箭手
            // 重掷原生狩猎位补位散开（内部自判白天窗口，与夜间规则互斥）。
            DayCrowdSpread(kingdom, archers, count);

            if (count == 0) return;

            // 夜间弓箭手列队诊断（纯读，每侧每世界一次）
            ScanNightArcherLineup(kingdom, archers);

            // 夜间滞留墙外纠偏：骑士随从重发原生跟队目标（被
            // NightFollowerAnchorPrefix 拉回墙内 4.2 步锚点）；无骑士的
            // 弓箭手/弩手重定位到墙内 8~18 步深处步行回位。
            NightParkedFollowerSweep(kingdom, archers);

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

// Daytime knight assemble spread: redirect the shared Mover.SetGoal(float,
// float) only when the caller is a knight walking to its daytime Assemble
// point.  The overload is pinned with the explicit float,float type array
// (Mover also has a GameObject overload).  All logic, gating and the
// mover-is-knight cache live in PatchWorld_DefenseSpacing.
// DayAssembleSpreadPrefix — see the section comment there for the
// Knight.cs Assemble grounds (same-point ±1 target, 10s re-walk, rank only
// used by the night wall lineup).
[HarmonyPatch(typeof(Mover), nameof(Mover.SetGoal), new[] { typeof(float), typeof(float) })]
public static class Mover_DefenseSpacing_DayAssemble_Spread_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(Mover __instance, float goal, float speed)
    {
        return PatchWorld_DefenseSpacing.DayAssembleSpreadPrefix(__instance, goal, speed);
    }
}

// Night follower anchor pullback: catch the knight-following formation goal
// (SetGoal(GameObject, float, float, OffsetMode) — Archer.cs:486 precedent
// SetGoal(this._knight.gameObject, this.runSpeed, -this.knightFollowDistance,
// Mover.OffsetMode.Formation)) and pull the anchor inside the wall at night.
// Logic lives in PatchWorld_DefenseSpacing.NightFollowerAnchorPrefix — see
// the section comment there for the measured wall-crossing evidence.
[HarmonyPatch(typeof(Mover), nameof(Mover.SetGoal),
    new[] { typeof(GameObject), typeof(float), typeof(float), typeof(Mover.OffsetMode) })]
public static class Mover_DefenseSpacing_NightFollowerAnchor_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(Mover __instance, GameObject goal, float speed,
        float offset, Mover.OffsetMode offsetMode)
    {
        return PatchWorld_DefenseSpacing.NightFollowerAnchorPrefix(
            __instance, goal, speed, offset, offsetMode);
    }
}
