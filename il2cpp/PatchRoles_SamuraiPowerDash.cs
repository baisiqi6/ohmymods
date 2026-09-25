using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// One motion per knight. A retired motion can never write again.
///
/// Choreographed round trip (2026-09-25 user ruling, endpoint + pose contract 2026-09-25b): the
/// trigger reads the world exactly once -- an eligible enemy inside [1.5, 8.2] gives nothing but
/// a direction, the start point and a fixed seven-unit dash distance; no target reference
/// survives the trigger frame. One coroutine owns the whole motion -- outbound dash, reverse
/// cut home and the value-discipline finale. Both endpoints are frozen in the token (out =
/// home + side * 7, home = the start point), the goal is re-asserted every routine frame so a
/// native steal survives at most one consumed frame, and arrival is judged by crossing the
/// goal in the direction frozen at the leg's start, so a large dt cannot skip the tolerance. A
/// leg that misses its 1.2 s window never masquerades as arrival: the home deadline closes
/// under its own reason and the outbound one turns anyway with the timeout recorded -- both on
/// the bounded close line carrying the frozen endpoints and the actual end position. The 3 s
/// Tick cap stays as the failsafe for a coroutine that died without running its finally.
///
/// Pose (2026-09-25b, real 2.4 controller contract): the knight controller's PowerSlash state
/// has exactly one exit -- the Land trigger (AnyState enters it with duration 0, Land exits at
/// normalized 1 back to Stand). So both legs play the verified state directly from frame zero
/// -- HasState("Base Layer.PowerSlash") resolved once per controller pointer; a controller
/// without the state falls back to the old PowerSlash trigger and never receives Land -- and
/// every Finale, completion and hard invalidation alike, fires Land once for the same readable
/// animator/controller it drove while that animator still sits in PowerSlash and the body is
/// alive. Death, another state or a replaced controller is never overridden, and no network
/// message is sent: the pre-existing gap that a client's pose is not networked stays exactly
/// as wide as it was. PowerSlash loops its charge clip, so replaying both legs replays the
/// animation events (the slash sound) twice per trip -- intended.
///
/// Withdrawal (2026-09-25 user ruling): a follower that walked off is answered by a plain
/// run-speed walk home only -- the invulnerable chase burst, its backoff ladder and its hit
/// rounds are gone by user ruling ("fixed two legs, no third dash"). The walk keeps arrival,
/// follower loss, dusk and ownership as its exits, and never suppresses the native slash.
///
/// Trail: this module no longer enables the TrailRenderer nor pins its lifetime (2026-09-25
/// user ruling: frozen white ghosts only, no continuous smear); the only trail code left is
/// the read-only diagnostic line, so an externally enabled trail survives untouched. The
/// ghosts themselves are SamuraiDashVisuals, untouched.
///
/// The trip lives and dies by a hard-invalidation subset (ChoreoInvalidClause), never by the
/// full Eligible gate: dashing outside the wall makes the native Knight.GoToWall call
/// SetRetreating(true), and judging that reaction -- or a formation task, a charge flag, a
/// foreign FSM task or manual control -- as failure is the 2026-09-25 field bug that cut
/// every trip short at the wall line. The trigger gate keeps the full gate untouched; the
/// hard subset keeps only the clauses that mean the motion can no longer exist.
/// </summary>
internal static class PatchRoles_SamuraiPowerDash
{
    private const float ScanInterval = .2f, Cooldown = 3f, DashSpeed = 18f;
    // The static self-scan's own reach: it must cover the whole trigger band ([1.5, 8.2]) with
    // margin, so an enemy may sit inside the scan while still being too far to open a trip.
    private const float SamuraiScanRange = 10.5f;
    // 2026-09-25 用户裁定（目标无关的编舞式往返）：冲刺距离恒为固定 7 格；触发带 = 固定 7 +
    // 命中扫描半径 1.2 = [1.5, 8.2]，带外敌人（9.0/10.5）一律不开程，出程不再按目标距离伸缩。
    private const float SamuraiFixedDashDistance = 7f;
    // 命中走廊半径：共享伤害扫描与触发带上限都引用它（旧实现是字面量 1.2）。
    private const float HitRadius = 1.2f;
    private const float TriggerRange = SamuraiFixedDashDistance + HitRadius;
    // 编舞节奏：单腿窗口 1.2 s、到点容差 .25、命中扫描 .1 s。目标每帧重申（2026-09-25b 审查：
    // 协程在全部 Update 之后恢复，原生 Knight.Update 的偷写最晚同帧被覆盖，不需要 Mover.Update
    // hook；这只缩小暴露窗口，不宣称完全阻止消费或这就是现场唯一根因）。Tick 硬上限 3 s 是协程
    // 未跑 finally 时的兜底（永久无敌类缺陷）。
    private const float ChoreoArrive = .25f, DashWindow = 1.2f, HitInterval = .1f;
    private const float ChoreoBudget = 3f;
    // 异常关闭行预算（2026-09-25b）：超时与硬失效必须可区分、可计数；预算 24 行，无节流。
    private const int ChoreoLogBudget = 24;
    // Turn-start narrative (2026-09-24): a session budget plus a per-knight throttle; the old
    // once-per-session key starved exactly this diagnosis.
    private const float TurnLogEvery = 6f;
    private const int TurnLogBudget = 12;
    // 脱离随从的普通步速回队（2026-09-25 用户裁定删除追随冲刺）：>10 开程、进 4 到达。
    private const float FollowLeash = 10f, ReturnStop = 4f;
    private static readonly Dictionary<int, ActorState> Actors = new();

    /// <summary>仅诊断用（FrameWatch 记行时才遍历）：当前在跑的编舞式往返数。</summary>
    internal static int ActiveCutLeases
    {
        get
        {
            int n = 0;
            foreach (KeyValuePair<int, ActorState> pair in Actors)
                if (pair.Value != null && pair.Value.Choreo != null) n++;
            return n;
        }
    }
    // 2026-09-25b 真实控制器姿态契约：PowerSlash 唯一出口是 Land 触发器（EventID 137525990）；
    // 两腿直接 Play 已验证的 fullPath 状态，终幕发 Land。短名哈希只用于触发器与状态比对，
    // Play 一律用 fullPath（跨层无歧义），不硬编码资源数值。
    private static readonly int PowerSlash = Animator.StringToHash("PowerSlash");
    private static readonly int PowerSlashFullPath = Animator.StringToHash("Base Layer.PowerSlash");
    private static readonly int Land = Animator.StringToHash("Land");
    private static readonly HashSet<string> Logged = new();
    // HasState per controller pointer: same-family knights share one AnimatorOverrideController
    // instance (BiomeSwapData.GetAnimSwap cache), so a verdict proven on one animator holds for
    // every knight on that controller -- and nothing else (cross-family pointers differ).
    private static readonly Dictionary<long, bool> PowerSlashByController = new();
    private static int HitLayerMask, EnemyScanLayer, WalkLogs, TurnLogs, ChoreoLogs;

    private sealed class ActorState
    {
        internal Knight Owner;
        internal Archer Follower;
        internal Mover ObservedMover;
        internal float NextFollowerScan, NextAttack;
        internal MotionLease Motion;
        // Choreographed round trip (2026-09-25): the live flag (non-null = this actor owns a
        // running coroutine; the token is also the identity check that makes the finale
        // idempotent) and the value-discipline snapshot the finale restores.
        internal ChoreoToken Choreo;
        internal bool ChoreoEffects, ChoreoOldInvulnerable;
        internal float ChoreoHomeX;
        internal int ChoreoSide;
        internal Mover.FacingMode ChoreoFacingWritten;
        internal bool ChoreoFacingOwned;
        // Turn narrative throttle (2026-09-24): one line per knight per six seconds.
        internal float NextTurnLogAt;
    }

    // The plain walk home (2026-09-25 user ruling): the withdrawal family's only remaining
    // motion. No effects, no invulnerability, no hits, no visuals, no facing lease and no
    // deadline -- only arrival, a lost follower, dusk or real ownership loss ends it.
    private sealed class MotionLease
    {
        internal ActorState Actor;
        internal Mover Mover;
        internal TrailRenderer Trail;
        internal float GoalX, GoalSpeed, NextGoal;
        internal bool Retired, HasGoal;
    }

    private static bool Same(UnityEngine.Object a, UnityEngine.Object b) =>
        a != null && b != null && a.Pointer == b.Pointer;

    private static bool Eligible(Knight k)
    {
        if (k == null || !k.enabled || k.gameObject == null || !k.gameObject.activeInHierarchy ||
            !ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth ||
            !PatchRoles_KnightStyle.TryGetResolvedStyleIndex(k, out int style) || style != 2)
            return false;
        if (k.ShouldPlayerControl() || k._beingControlled || k.isRetreating || k.isCharging ||
            k._shouldCharge || k.GetFormation() != null || k.helPuzzlePillar != null || k._harmless)
            return false;
        var embarkee = k._embarkee;
        if (embarkee != null && (embarkee.IsEmbarked || embarkee.IsTargetingEmbarkable || embarkee.EmbarkableTarget != null))
            return false;
        var c = k._character;
        if (c == null || c.inert || c.grabbed || c.isStationary || k._damageable == null || k._damageable.isDead || k._mover == null)
            return false;
        if (k._fsm == null || k._fsm._executeQueuedState) return false;
        int state = k._fsm.Current;
        return state == Knight.State.Stand || state == Knight.State.GoToWall || state == Knight.State.Assemble;
    }

    private static bool NightGuard(Knight k)
    {
        var managers = Managers.Inst;
        return managers != null && managers.kingdom != null && !managers.kingdom.isDaytime &&
            (k._fsm.Current == Knight.State.Stand || k._fsm.Current == Knight.State.GoToWall);
    }

    internal static void BeforeNativeUpdate(Knight knight)
    {
        try
        {
            // Queue immediately before the native Update consumes it. Never retain/cancel a
            // queued state across frames or overwrite a goal belonging to another system.
            if (!Eligible(knight) || !NightGuard(knight) || Time.timeScale <= 0 ||
                knight._mover._pauseTimeout > 0 || knight._mover.goalMode != Mover.GoalMode.Off ||
                !Actors.TryGetValue(knight.gameObject.GetInstanceID(), out var a) ||
                !Same(a.Owner, knight) || !Same(a.ObservedMover, knight._mover) || a.Motion != null ||
                a.Choreo != null ||  // choreo trip holds a Position goal; queueing GoToWall would fight it
                !ValidFollower(knight, a.Follower) || Distance(a) <= FollowLeash) return;
            knight._fsm.GoToState(Knight.State.GoToWall);
            if (Logged.Add("night-wall-queue"))
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/night-wall-queue] x=" +
                    knight.transform.position.x + " follower=" + a.Follower.transform.position.x +
                    " goal=" + knight._mover.goalMode + " state=" + knight._fsm.Current);
        }
        catch (Exception e) { Log("night-wall", e); }
    }

    private static bool ValidFollower(Knight k, Archer a) => a != null && a.gameObject != null &&
        a.gameObject.activeInHierarchy && Same(a._knight, k) && a._damageable != null && !a._damageable.isDead;

    private static float Distance(ActorState a)
    {
        float x = a.Owner.transform.position.x;
        float target = a.Follower != null && a.Follower.gameObject != null
            ? a.Follower.transform.position.x
            : PatchRoles_SamuraiNightFormation.HomeXOf(a.Owner);
        return Mathf.Abs(x - target);
    }

    private static void RefreshFollower(ActorState a)
    {
        if (Time.time < a.NextFollowerScan) return;
        a.NextFollowerScan = Time.time + ScanInterval;
        Archer nearest = null;
        float distance = float.MaxValue;
        // Keep the shared cache's default three-second scene scan; only this selection runs at .2 s.
        foreach (Archer archer in UnitScanCache.GetArchers())
        {
            if (!ValidFollower(a.Owner, archer)) continue;
            float d = Mathf.Abs(archer.transform.position.x - a.Owner.transform.position.x);
            if (d < distance) { nearest = archer; distance = d; }
        }
        a.Follower = nearest;
    }

    // The enemy selection this module owns. The native `_enemyScanner` instance is deliberately
    // left exactly as the game built it -- widening it would widen the native slash targeting,
    // the daytime idleness check and the border guard scan with it -- so the extra reach lives
    // here instead. rangeBehind = range makes the strip symmetric: the samurai must also see the
    // enemy that sits behind it while its homeward side is toward the enemy half, and height 1
    // matches the replaced scanner's own column. excludeDead = true is a deliberate deviation
    // from BOTH the replaced native instance scanner and the native static scans (they run with
    // it off, Knight.cs:177 / Scanner defaults): no dashing at corpses. Enemies only, never Wildlife --
    // the shared hit mask keeps Wildlife for damage, the target scan must not.
    private static GameObject ScanClosestEnemy(Knight k)
    {
        if (EnemyScanLayer == 0) EnemyScanLayer = LayerMask.GetMask("Enemies");
        return Scanner.ScanClosest(k.transform, SamuraiScanRange, EnemyScanLayer, null, SamuraiScanRange, 1f, true);
    }

    private static bool Current(MotionLease m) => !m.Retired && ReferenceEquals(m.Actor.Motion, m);
    private static bool OwnGoal(MotionLease m) => m.HasGoal && Same(m.Actor.Owner._mover, m.Mover) &&
        m.Mover.goalMode == Mover.GoalMode.Position && m.Mover._goalObject == null &&
        Mathf.Approximately(m.Mover._goalPosition, m.GoalX) && Mathf.Approximately(m.Mover._goalSpeed, m.GoalSpeed);

    private static bool ValidMotion(MotionLease m) => Current(m) && Eligible(m.Actor.Owner) &&
        Same(m.Actor.Owner._mover, m.Mover) &&
        ((m.Trail == null && m.Actor.Owner._trail == null) || Same(m.Actor.Owner._trail, m.Trail)) &&
        OwnGoal(m) && m.Mover._pauseTimeout <= 0;

    private static void Finish(MotionLease m)
    {
        if (!Current(m)) return;
        try
        {
            // Never restore a previous goal, clear an external pause, or stop a replacement mover.
            if (OwnGoal(m)) m.Mover.Stop();
        }
        catch (Exception e) { Log("finish", e); }
        finally
        {
            // Stop may synchronously re-enter and replace the lease; only clear ours.
            bool owned = Current(m);
            m.Retired = true;
            if (owned) m.Actor.Motion = null;
        }
    }

    private static void Goal(MotionLease m, float x, float speed)
    {
        if (!Current(m)) return;
        m.Mover.SetGoalNoHaglet(x, speed);
        m.GoalX = x; m.GoalSpeed = speed; m.HasGoal = true;
    }

    // Facing ownership belongs to the choreographed round trip alone now (2026-09-25: the walk
    // takes no facing lease), so the two helpers take the trip's fields by reference: only ever
    // take over from Ahead, and never stomp a foreign fixed facing.

    // Release only while the mover still holds our value: a mode written by the native code
    // or a third party after us is never overwritten.
    private static void RestoreFacing(Mover mover, ref Mover.FacingMode written, ref bool owned)
    {
        if (!owned) return;
        owned = false;
        if (mover == null) return;
        try { if (mover.facingMode == written) mover.SetFacingMode(Mover.FacingMode.Ahead, null); }
        catch (Exception e) { Log("restore-facing", e); }
    }

    // Re-target an owned facing to the mode of the next leg: a mode still ours is swapped
    // directly; otherwise the new mode is only taken over from Ahead, never from a foreign one.
    // The turn's own SetDirection call (travel direction, after this) repairs the Y scale native
    // SetDirection wipes.
    private static void RetargetFacing(Mover mover, ref Mover.FacingMode written, ref bool owned, Mover.FacingMode mode)
    {
        if (mover == null) return;
        try
        {
            if (owned)
            {
                if (mover.facingMode != written) { owned = false; return; }
                mover.SetFacingMode(mode, null);
                written = mode;
                return;
            }
            if (mover.facingMode != Mover.FacingMode.Ahead) return;
            mover.SetFacingMode(mode, null);
            written = mode;
            owned = true;
        }
        catch (Exception e) { Log("retarget-facing", e); }
    }

    // Bounded field evidence for the plain walk home (three lines per session, never per frame).
    private static void LogWalk(string eventName, MotionLease m)
    {
        if (WalkLogs >= 3) return;
        try
        {
            WalkLogs++;
            var a = m.Actor;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/" + eventName + "] x=" +
                a.Owner.transform.position.x.ToString("0.##")
                + " follower=" + a.Follower.transform.position.x.ToString("0.##")
                + " goal=" + m.GoalX.ToString("0.##") + " speed=" + m.GoalSpeed.ToString("0.##"));
        }
        catch { }
    }




    // Diagnostic reads are admitted per motion; failures never interrupt gameplay or cleanup.
    private static void LogTrailState(TrailRenderer trail, SamuraiDashDiagnostics.Trace diagnostics,
        float startedAt, string eventName)
    {
        if (diagnostics == null) return;
        try
        {
            string details = "elapsed=" + (Time.time - startedAt).ToString("0.###") + " present=" + (trail != null);
            if (trail != null)
                details += " active=" + trail.gameObject.activeInHierarchy + " enabled=" + trail.enabled
                    + " emitting=" + trail.emitting + " points=" + trail.positionCount
                    + " lifetime=" + trail.time + " width=" + trail.widthMultiplier
                    + " layer=" + trail.sortingLayerID + " order=" + trail.sortingOrder;
            SamuraiDashDiagnostics.Write(diagnostics, eventName, details);
        }
        catch (Exception e) { SamuraiDashDiagnostics.Write(diagnostics, eventName, "state-read-failed=" + e.GetType().Name); }
    }


    private static float StationX(ActorState a)
    {
        float x = a.Owner.transform.position.x;
        // The walk's station: the follower's station while one is live; otherwise the night
        // formation slot or the native guard anchor -- the walk never loses its way home just
        // because the follower died.
        float target = a.Follower != null && a.Follower.gameObject != null
            ? a.Follower.transform.position.x
            : PatchRoles_SamuraiNightFormation.HomeXOf(a.Owner);
        return target - Mathf.Sign(target - x) * 2.5f;
    }


    /// <summary>
    /// Bounded turn narrative (2026-09-24), mirroring the heal log: at most TurnLogBudget lines
    /// per session and one line per TurnLogEvery seconds per knight.
    /// </summary>
    private static void TurnLog(ActorState a, Knight k, float goal)
    {
        if (TurnLogs >= TurnLogBudget) return;
        if (Time.time < a.NextTurnLogAt) return;
        a.NextTurnLogAt = Time.time + TurnLogEvery;
        TurnLogs++;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/roundtrip-turn] x=" +
                k.transform.position.x.ToString("0.##") + " goal=" + goal.ToString("0.##") + " deterministic");
        }
        catch { }
    }

    internal static void Tick(Knight knight)
    {
        if (knight == null || knight.gameObject == null) return;
        int id = knight.gameObject.GetInstanceID();
        Actors.TryGetValue(id, out ActorState a);
        try
        {
            if (a != null && Same(a.Owner, knight) && !Same(a.ObservedMover, knight._mover))
            {
                if (a.Choreo != null) Finale(a, a.Choreo, "mover-replaced");
                if (a.Motion != null) Finish(a.Motion);
                a.ObservedMover = knight._mover;
                // A replacement mover starts clean.
                return; // A replacement mover's first observed goal belongs to its new owner.
            }
            // The live round trip is judged by the hard gate alone, and it is answered before any
            // soft-eligibility return: the 3 s cap must stay reachable for a coroutine that died
            // without a finally (a third party's StopAllCoroutines) even while a native behaviour
            // flag is set, and closing the state on a soft flag would leave that coroutine
            // writing goals while the next eligible frame rebuilds a second state over the same
            // mover (two writers). The trigger gate below still requires the full Eligible.
            if (a != null && a.Choreo != null)
            {
                string why = ChoreoInvalidClause(a, a.Choreo, knight);
                if (why.Length != 0) Finale(a, a.Choreo, "hard-invalid:" + why);
                else if (Time.time - a.Choreo.StartedAt >= ChoreoBudget) Finale(a, a.Choreo, "tick-cap");
                return;
            }
            // No trip can be live past the return above, so this block only ever closes the
            // plain walk (and drops a state whose instance id was reused by another actor).
            if (a != null && (!Same(a.Owner, knight) || !Eligible(knight)))
            {
                if (a.Motion != null) Finish(a.Motion);
                Actors.Remove(id); a = null;
            }
            if (!Eligible(knight)) return;
            if (a == null) { a = new ActorState { Owner = knight, ObservedMover = knight._mover }; Actors[id] = a; }
            if (a.Motion != null)
            {
                MotionLease m = a.Motion;
                if (!ValidMotion(m) || NightGuard(knight)) { Finish(m); return; }
                // Verify this reference each frame before periodic reselection, never mask its loss.
                if (!ValidFollower(knight, a.Follower)) { Finish(m); a.Follower = null; return; }
                RefreshFollower(a);
                AdvanceWalk(m);
                return;
            }
            RefreshFollower(a);
            bool follower = ValidFollower(knight, a.Follower);
            if (!follower) a.Follower = null;
            float distance = follower ? Distance(a) : 0;
            // At night the wall coroutine supplies its current destination and defensive
            // facing. Keep the follower leash, but never turn this homeward leg into an attack.
            if (NightGuard(knight))
            {
                if (distance > FollowLeash) return;
            }
            // 2026-09-25 用户裁定：脱离随从只回普通步速的 walk（追随冲刺/回退梯/无敌/命中
            // 整链删除）。
            if (distance > FollowLeash)
            {
                if (Time.timeScale <= 0 || knight._mover._pauseTimeout > 0) return;
                BeginWalk(a);
                return;
            }
            if (Time.timeScale <= 0 || knight._mover._pauseTimeout > 0 || Time.time < a.NextAttack) return;
            // 2026-09-25 用户裁定（目标无关）：触发只读一次世界——存在性/方向/范围。取到
            // side 与出发点后不再持有任何目标引用；出程恒 7 格、1.2 s 窗口、回家点=出发 x。
            // 冲刺不考虑墙（墙防时武士照旧冲出墙外再回撤，batch2 的既有行为）。
            GameObject target = ScanClosestEnemy(knight);
            Damageable enemy = target != null ? target.GetComponent<Damageable>() : null;
            bool valid = enemy != null && enemy.IsDamagedBy(DamageSource.Knight);
            float dx = valid ? target.transform.position.x - knight.transform.position.x : 0f;
            if (!valid || Mathf.Abs(dx) < 1.5f || Mathf.Abs(dx) > TriggerRange)
            {
                a.NextAttack = Time.time + ScanInterval;   // 落空：按扫描节奏重试
                return;
            }
            a.NextAttack = Time.time + Cooldown;           // 触发成功：CD 从触发帧起算
            StartChoreo(a, dx < 0 ? -1 : 1);
        }
        catch (Exception e)
        {
            if (a?.Choreo != null) Finale(a, a.Choreo, "tick-exception");
            else if (a?.Motion != null) Finish(a.Motion);
            Log("tick", e);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Choreographed round trip (2026-09-25 user ruling: target-agnostic). The trigger keeps
    // nothing but a side and the start point; everything below runs off the actor flag, and no
    // Tick path advances the motion -- one coroutine owns both legs, the turn and the finale.
    // ---------------------------------------------------------------------------------------

    private sealed class ChoreoToken
    {
        internal Mover Mover;
        internal Damageable Damageable;
        internal TrailRenderer Trail;              // read-only trail diagnostics only (2026-09-25: no writes)
        internal SamuraiDashVisuals.Token Visual;
        internal SamuraiDashDiagnostics.Trace Diagnostics;
        // Pose authority (2026-09-25b): the animator this trip drives and the controller
        // pointer the HasState verdict was taken on. Once either is replaced no later pose
        // write -- the turn's replay or the finale's Land -- ever lands on the new controller.
        internal Animator Animator;
        internal long ControllerKey;
        internal bool PoseKnown;
        internal int LastPlayFrame = -1;
        internal float StartedAt, GoalX, GoalSpeed, TurnX;
        internal bool HasGoal, TurnTimedOut;
        internal string Phase = "out";
        // Per-leg hit bookkeeping: outbound and home may each strike a given enemy once.
        internal readonly HashSet<IntPtr> HitObjects = new();
        internal readonly Collider2D[] Colliders = new Collider2D[16];
    }

    private static bool ChoreoAlive(ActorState a, ChoreoToken token) =>
        a != null && token != null && ReferenceEquals(a.Choreo, token);

    /// <summary>
    /// Why the live trip must die -- empty while it must live. This is deliberately the
    /// hard-invalidation subset of <see cref="Eligible"/>, not the full gate: the native reaction
    /// to a knight outside the wall (retreating, charging, a formation task, a foreign FSM task,
    /// manual control) is the very state this dash creates, so treating it as failure cut every
    /// trip short at the wall line (2026-09-25 field evidence). What remains are the clauses that
    /// mean the motion can no longer exist or be trusted: the token is stale, the owner identity
    /// is gone (reused instance id), the object is gone/disabled/inactive, the mod or the world
    /// authority is off, the style stopped resolving as the samurai, the mover is not the one the
    /// trip owns, or the body cannot move under its own power (inert/grabbed/dead). Tick, the
    /// routine and the hit scan gate on this one function; the trigger gate (StartChoreo's
    /// caller) and the plain walk keep the full Eligible.
    /// </summary>
    private static string ChoreoInvalidClause(ActorState a, ChoreoToken token, Knight k)
    {
        if (!ChoreoAlive(a, token)) return "token";
        if (!Same(a.Owner, k)) return "owner";
        if (k == null || k.gameObject == null) return "gone";
        if (!k.enabled) return "disabled";
        if (!k.gameObject.activeInHierarchy) return "inactive";
        if (!ModConfig.Enabled.Value) return "config";
        if (!NetworkBigBoss.HasWorldAuth) return "authority";
        if (!PatchRoles_KnightStyle.TryGetResolvedStyleIndex(k, out int style) || style != 2) return "style";
        if (!Same(k._mover, token.Mover)) return "mover";
        var c = k._character;
        if (c == null) return "body";
        if (c.inert) return "inert";
        if (c.grabbed) return "grabbed";
        var d = k._damageable;
        if (d == null || d.isDead) return "dead";
        return "";
    }

    private static bool ChoreoValid(ActorState a, ChoreoToken token, Knight k) =>
        ChoreoInvalidClause(a, token, k).Length == 0;

    private static bool ChoreoOwnGoal(ActorState a, ChoreoToken token) => token.HasGoal &&
        Same(a.Owner._mover, token.Mover) && token.Mover.goalMode == Mover.GoalMode.Position &&
        token.Mover._goalObject == null && Mathf.Approximately(token.Mover._goalPosition, token.GoalX) &&
        Mathf.Approximately(token.Mover._goalSpeed, token.GoalSpeed);

    private static void ChoreoGoal(ActorState a, ChoreoToken token, float x, float speed)
    {
        if (!ChoreoAlive(a, token) || !Same(a.Owner._mover, token.Mover)) return;
        token.Mover.SetGoalNoHaglet(x, speed);
        token.GoalX = x; token.GoalSpeed = speed; token.HasGoal = true;
    }

    /// <summary>Live round-trip flag: drives the slash suppression, the night gates and Tick.</summary>
    internal static bool IsChoreoActive(Knight knight)
    {
        try
        {
            if (knight == null || knight.gameObject == null) return false;
            return Actors.TryGetValue(knight.gameObject.GetInstanceID(), out ActorState a) &&
                Same(a.Owner, knight) && a.Choreo != null;
        }
        catch (Exception e) { Log("choreo-active", e); return true; }
    }

    internal static bool SuppressesSlash(Knight knight) => IsChoreoActive(knight);

    /// <summary>
    /// Opens one round trip: snapshot the protection the finale will restore, apply it and the
    /// visual token, open the pose authority against the live controller and start the
    /// coroutine. A failed start closes immediately instead of leaving the flag live.
    /// </summary>
    private static void StartChoreo(ActorState a, int side)
    {
        Knight k = a.Owner;
        a.ChoreoSide = side;
        a.ChoreoHomeX = k.transform.position.x;
        var token = new ChoreoToken { Mover = k._mover, Damageable = k._damageable, Trail = k._trail, StartedAt = Time.time };
        a.Choreo = token;
        a.ChoreoEffects = true;
        a.ChoreoOldInvulnerable = token.Damageable.invulnerable;
        token.Damageable.invulnerable = true;
        token.Diagnostics = SamuraiDashDiagnostics.Begin(k, false, token.StartedAt);
        LogChoreoTrail(token, "trail-state");        // read-only diagnostic: this module writes no trail
        token.Visual = SamuraiDashVisuals.Begin(k, token.Diagnostics);
        // 出程姿态（2026-09-25b 真实控制器契约）：在控制器指针上解析 HasState("Base
        // Layer.PowerSlash")；验证通过直接 Play 0 帧，未验证/无该状态的控制器保守退回触发器
        // （该行程永不发 Land、不再 Play）。
        TryOpenSlashPose(token, k);
        SlashLegStart(token, k);
        // 出程：固定 7 格目标（协程每帧重申，对抗原生 GoToWall 改写）。
        ChoreoGoal(a, token, a.ChoreoHomeX + side * SamuraiFixedDashDistance, DashSpeed);
        try
        {
            k.StartCoroutine(ChoreoRoutine(a, token).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            Finale(a, token, "start-failed");
            Log("choreo-start", e);
        }
    }

    /// <summary>
    /// HasState probe wrapped so a marshaling failure is never cached as "state absent": only a
    /// verdict the controller actually returned enters the per-pointer cache.
    /// </summary>
    private static bool TryHasPowerSlash(Animator animator, out bool has)
    {
        try { has = animator.HasState(0, PowerSlashFullPath); return true; }
        catch (Exception e) { Log("pose-probe", e); has = false; return false; }
    }

    /// <summary>
    /// Opens the trip's pose authority: reads the live animator's controller, resolves the
    /// PowerSlash state once per controller pointer (same-family knights share one
    /// AnimatorOverrideController instance, so one verdict serves them all) and arms the token
    /// when the state is verified.
    /// </summary>
    private static bool TryOpenSlashPose(ChoreoToken token, Knight k)
    {
        try
        {
            token.Animator = k._animator;
            if (token.Animator == null) return false;
            RuntimeAnimatorController controller = token.Animator.runtimeAnimatorController;
            if (controller == null) return false;
            long key = controller.Pointer.ToInt64();
            token.ControllerKey = key; // even an initial trigger fallback is scoped to this controller
            if (!PowerSlashByController.TryGetValue(key, out bool known))
            {
                if (!TryHasPowerSlash(token.Animator, out known)) return false;   // probe failed: retry next trip
                PowerSlashByController[key] = known;
            }
            if (!known) return false;
            token.PoseKnown = true;
            return true;
        }
        catch (Exception e) { Log("pose-open", e); return false; }
    }

    /// <summary>
    /// The animator/controller identity this trip verified at its start; false once either was
    /// replaced, so no later pose write ever lands on a controller it was not proven on.
    /// </summary>
    private static bool SamePoseTarget(ChoreoToken token, Knight k, out Animator animator)
    {
        animator = null;
        if (token == null || k == null) return false;
        try
        {
            animator = k._animator;
            if (animator == null || !Same(animator, token.Animator) || !animator.isActiveAndEnabled) return false;
            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            return controller != null && controller.Pointer.ToInt64() == token.ControllerKey;
        }
        catch (Exception e) { Log("pose-authority", e); return false; }
    }

    private static bool PoseAuthority(ChoreoToken token, Knight k, out Animator animator)
    {
        animator = null;
        return token != null && token.PoseKnown && SamePoseTarget(token, k, out animator);
    }

    /// <summary>
    /// One leg's pose start: replay the verified PowerSlash state from frame zero -- a visible
    /// re-draw on the reverse cut even though the animator never left the state (2026-09-25
    /// field fix for "keeps the drawn pose while sliding home"). A leftover Land trigger would
    /// eat the state's only exit on the next update, so it is cleared first. A controller
    /// without the state at the start falls back to the trigger. Replacement animators or
    /// controllers receive no writes from the old trip. The looping
    /// charge clip replays its animation events (the slash sound) each leg: intended.
    /// </summary>
    private static void SlashLegStart(ChoreoToken token, Knight k)
    {
        try
        {
            if (!SamePoseTarget(token, k, out Animator animator)) return;
            animator.ResetTrigger(PowerSlash);
            if (token.PoseKnown)
            {
                animator.ResetTrigger(Land);
                animator.Play(PowerSlashFullPath, 0, 0f);
                token.LastPlayFrame = Time.frameCount;
            }
            else animator.SetTrigger(PowerSlash);
        }
        catch (Exception e) { Log("pose-leg", e); }
    }

    /// <summary>
    /// The close-out the real controller demands: PowerSlash's only exit is the Land trigger
    /// (contract 2026-09-25: AnyState enters with duration 0, Land exits at normalized 1 back
    /// to Stand), so a trip that ends -- completes or is hard-invalidated alike -- while its
    /// own animator still sits in that state must fire it, or the pose outlives the motion (the
    /// reported bug). The gate keeps it honest: the same readable animator/controller the trip
    /// drove, still in PowerSlash (short or full hash), and a live body. Death, another state
    /// (Die/Block already took over) and a replaced controller are never overridden, and no
    /// network message is sent -- the pre-existing pose-not-networked gap stays as wide as it
    /// was.
    /// </summary>
    private static void LandSlashPose(ChoreoToken token, Knight k)
    {
        try
        {
            if (!PoseAuthority(token, k, out Animator animator)) return;
            var d = k._damageable;
            if (d == null || d.isDead) return;
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            bool inState = info.shortNameHash == PowerSlash || info.fullPathHash == PowerSlashFullPath;
            // Play can still be pending until animation evaluation. A same-frame failure must
            // queue the exit too, or the pending looping pose would start after motion cleanup.
            if (!inState && token.LastPlayFrame != Time.frameCount) return;
            animator.SetTrigger(Land);
        }
        catch (Exception e) { Log("pose-land", e); }
    }


    /// <summary>
    /// The whole trip in one coroutine: the outbound leg and the home leg share one loop shape
    /// (frozen goal re-asserted every frame, arrival by crossing in the direction frozen at
    /// the leg's start, 1.2 s window, 0.1 s hit cadence, hard invalidation checked every
    /// iteration) and the turn runs between them. A leg that misses its window never
    /// masquerades as arrival: the home deadline closes under its own reason and the outbound
    /// one turns anyway with the timeout recorded. Every exit -- complete, deadline, invalid or
    /// throwing -- closes through the same Finale; the flag suppresses the native slash.
    /// </summary>
    private static IEnumerator ChoreoRoutine(ActorState a, ChoreoToken token)
    {
        Knight k = a.Owner;
        string step = "complete";
        int leg = 0;                                        // 0 = outbound, 1 = home
        float goal = a.ChoreoHomeX + a.ChoreoSide * SamuraiFixedDashDistance;
        int dir = a.ChoreoSide;                             // arrival direction frozen at the leg's start
        float deadline = Time.time + DashWindow;
        float nextHit = 0f;
        while (true)
        {
            // (A yield may not sit inside a try that has a catch, so the iteration is synchronous
            // and the yield lives at the loop tail.)
            try
            {
                string why = ChoreoInvalidClause(a, token, k);
                if (why.Length != 0)
                {
                    step = (leg == 0 ? "out-interrupt:" : "home-interrupt:") + why;
                    break;
                }
                if (Time.timeScale > 0)
                {
                    if (Time.time >= nextHit) { nextHit = Time.time + HitInterval; ChoreoHitScan(a, token); }
                    why = ChoreoInvalidClause(a, token, k);
                    if (why.Length != 0)
                    {
                        step = (leg == 0 ? "out-interrupt:" : "home-interrupt:") + why;
                        break;
                    }
                    // Every-frame re-assert (2026-09-25b): the routine resumes after all
                    // Updates, so a native goal steal survives at most one consumed frame.
                    ChoreoGoal(a, token, goal, DashSpeed);
                }
                // Arrival by crossing in the leg's frozen direction: a large dt that jumps
                // past the goal plus tolerance still lands, and the home leg only completes
                // on a real crossing -- a missed window gets its own close reason.
                bool arrived = dir > 0
                    ? k.transform.position.x >= goal - ChoreoArrive
                    : k.transform.position.x <= goal + ChoreoArrive;
                bool timedOut = Time.time >= deadline;
                if (arrived || timedOut)
                {
                    if (leg == 1)
                    {
                        if (!arrived) step = "home-deadline";
                        break;
                    }
                    if (timedOut && !arrived)
                    {
                        token.TurnTimedOut = true;         // the fact rides along on the close line
                        LogChoreoClose(a, token, "out-deadline");
                    }
                    TurnChoreo(a, token);                   // outbound: the reverse cut home
                    why = ChoreoInvalidClause(a, token, k);
                    if (why.Length != 0)
                    {
                        step = "turn-interrupt:" + why;
                        break;
                    }
                    leg = 1;
                    token.Phase = "home";
                    goal = a.ChoreoHomeX;
                    dir = goal < k.transform.position.x ? -1 : 1;
                    deadline = Time.time + DashWindow;
                    nextHit = Time.time + HitInterval;      // the turn already struck on its entry frame
                }
            }
            catch (Exception e)
            {
                step = "exception:" + e.GetType().Name;
                Log("choreo-routine", e);
                break;
            }
            yield return null;
        }
        Finale(a, token, step);
    }


    /// <summary>
    /// The reverse cut: replay the verified slash state from frame zero (or re-fire the trigger
    /// when the controller never verified), turn toward home, put the goal on the start point
    /// and strike the entry frame. Runs in the coroutine's own path, so no hand-off seam
    /// exists. The legs never Stop between them -- the mover's own acceleration ramp and
    /// rubber-band deceleration handle the reversal; only the finale stops a goal it still owns.
    /// </summary>
    private static void TurnChoreo(ActorState a, ChoreoToken token)
    {
        Knight k = a.Owner;
        token.Phase = "turn";
        token.TurnX = k.transform.position.x;
        SlashLegStart(token, k);
        token.HitObjects.Clear();                   // the reverse cut opens its own hit round
        float home = a.ChoreoHomeX;
        int direction = home < k.transform.position.x ? -1 : 1;
        RetargetFacing(token.Mover, ref a.ChoreoFacingWritten, ref a.ChoreoFacingOwned,
            direction < 0 ? Mover.FacingMode.Left : Mover.FacingMode.Right);
        try
        {
            var scale = k.transform.localScale;
            token.Mover.SetDirection(direction);
            k.transform.localScale = new Vector3(direction * Mathf.Abs(scale.x), scale.y, scale.z);
        }
        catch (Exception e) { Log("choreo-turn-facing", e); }
        ChoreoGoal(a, token, home, DashSpeed);
        SamuraiDashDiagnostics.Write(token.Diagnostics, "turn",
            "goal=" + home.ToString("0.##") + " facing=" + a.ChoreoFacingWritten);
        TurnLog(a, k, home);
        ChoreoHitScan(a, token);                    // entry frame hits, same as every other dash
    }

    /// <summary>
    /// The choreography's damage scan -- the "hit what you fly into" half that replaced the old
    /// target-tracking burst: a circle of HitRadius around the knight, one hit per Damageable per
    /// leg, stopped mid-frame only when a synchronous damage callback hard-invalidates the run
    /// (a native behaviour flag never silences the hits; see ChoreoInvalidClause).
    /// </summary>
    private static void ChoreoHitScan(ActorState a, ChoreoToken token)
    {
        if (!ChoreoValid(a, token, a.Owner) || Time.timeScale <= 0) return;
        if (HitLayerMask == 0) HitLayerMask = LayerMask.GetMask("Enemies", "Wildlife");
        Knight k = a.Owner;
        int count = Physics2D.OverlapCircleNonAlloc(k.transform.position, HitRadius, token.Colliders, HitLayerMask);
        for (int i = 0; i < count; i++)
        {
            if (!ChoreoValid(a, token, k)) return;
            Collider2D hit = token.Colliders[i];
            if (hit == null) continue;
            Damageable enemy = hit.GetComponent<Damageable>();
            if (enemy == null || !enemy.IsDamagedBy(DamageSource.Knight)) continue;
            if (!ChoreoValid(a, token, k)) return;
            if (!token.HitObjects.Add(enemy.Pointer)) continue;
            enemy.ReceiveDamage(k._attackDamage, k.gameObject, DamageSource.Knight);
        }
    }

    /// <summary>
    /// The single close for one round trip: idempotent by token identity, so OnDisable, the Tick
    /// hard cap and the coroutine's own finally can all call it and only the first one writes.
    /// The flag is cleared first -- a Stop callback that starts a new motion must never be erased
    /// by this retired one -- then the close narrative is emitted in its own guarded step (a
    /// logging failure can never skip the restore), effects return by value, facing is released,
    /// the pose is landed through the controller's own exit and a goal still ours is stopped.
    /// </summary>
    private static void Finale(ActorState a, ChoreoToken token, string step)
    {
        if (!ChoreoAlive(a, token)) return;
        a.Choreo = null;
        Knight k = a.Owner;
        // The narrative is isolated from the close on purpose: a logging failure must never be
        // able to skip the value-discipline restore and leave the knight invulnerable forever.
        try { LogChoreoClose(a, token, step); }
        catch (Exception e) { Log("choreo-close-log", e); }
        try
        {
            SamuraiDashVisuals.End(token.Visual);
            RestoreChoreoEffects(a, token);
            RestoreFacing(token.Mover, ref a.ChoreoFacingWritten, ref a.ChoreoFacingOwned);
            try { if (SamePoseTarget(token, k, out Animator animator)) animator.ResetTrigger(PowerSlash); }
            catch (Exception e) { Log("reset-trigger", e); }
            // The pose close-out the real controller needs (2026-09-25b): a trip that ends
            // while its own animator still sits in PowerSlash fires Land -- including every
            // hard invalidation, which is exactly when the pose used to be left behind.
            try { if (k != null) LandSlashPose(token, k); }
            catch (Exception e) { Log("land-pose", e); }
            // Never restore a previous goal, clear an external pause, or stop a replacement mover.
            if (ChoreoOwnGoal(a, token)) token.Mover.Stop();
        }
        catch (Exception e) { Log("choreo-finale", e); }
    }

    // The choreography's restore discipline: a bool cannot identify an external true -> true
    // rewrite, so the protection returns only while the flag still carries ours. The trail is
    // no longer written at all (2026-09-25 user ruling) -- ghosts only, never a live smear, so
    // an externally enabled trail survives the whole trip untouched.
    private static void RestoreChoreoEffects(ActorState a, ChoreoToken token)
    {
        if (!a.ChoreoEffects) return;
        a.ChoreoEffects = false;
        if (token.Damageable != null && token.Damageable.invulnerable) token.Damageable.invulnerable = a.ChoreoOldInvulnerable;
        LogChoreoTrail(token, "effects-restored");
    }

    // Abnormal-close narrative (2026-09-25b): one line per non-complete close carrying the
    // frozen endpoints, the turn position and the actual end position -- a missed window must
    // never read as an arrival. No throttle: repeated hard failures stay countable, bounded
    // only by the session budget.
    private static void LogChoreoClose(ActorState a, ChoreoToken token, string step)
    {
        if (step == "complete" || ChoreoLogs >= ChoreoLogBudget) return;
        ChoreoLogs++;
        try
        {
            Knight k = a.Owner;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/choreo] knight=" +
                (k != null && k.gameObject != null ? k.gameObject.GetInstanceID() : 0) +
                " step=" + step + " phase=" + token.Phase + " elapsed=" +
                (Time.time - token.StartedAt).ToString("0.###") +
                " home=" + a.ChoreoHomeX.ToString("0.##") +
                " outGoal=" + (a.ChoreoHomeX + a.ChoreoSide * SamuraiFixedDashDistance).ToString("0.##") +
                " turnX=" + token.TurnX.ToString("0.##") +
                " endX=" + (k != null ? k.transform.position.x.ToString("0.##") : "0") +
                " turnTimedOut=" + token.TurnTimedOut);
        }
        catch { }
    }



    private static void LogChoreoTrail(ChoreoToken token, string eventName) =>
        LogTrailState(token.Trail, token.Diagnostics, token.StartedAt, eventName);


    // Plain run-speed walk home (2026-09-25 user ruling): the withdrawal family's whole
    // remaining motion -- no effects, no invulnerability, no hit scan, no visuals, no facing
    // lease and no deadline. Only arrival, a lost follower, a stolen goal or dusk ends it.
    private static void BeginWalk(ActorState a)
    {
        Knight k = a.Owner;
        var m = new MotionLease { Actor = a, Mover = k._mover, Trail = k._trail };
        a.Motion = m;
        Goal(m, StationX(a), k._runSpeed);
        m.NextGoal = Time.time + ScanInterval;
        LogWalk("walk", m);
    }

    // The walk suppresses no slash: the samurai may keep defending itself on the way back,
    // and only real ownership loss ends the walk.
    private static void AdvanceWalk(MotionLease m)
    {
        if (!ValidMotion(m)) { Finish(m); return; }
        var a = m.Actor;
        float now = Time.time;
        if (Distance(a) <= ReturnStop) { LogWalk("walk-done", m); Finish(m); return; }
        if (now >= m.NextGoal)
        {
            m.NextGoal = now + ScanInterval;
            float target = StationX(a);
            if (Mathf.Abs(target - m.GoalX) > .25f) Goal(m, target, a.Owner._runSpeed);
        }
    }



    /// <summary>
    /// True while this knight's actor entry owns any live motion (the choreographed round trip
    /// flag or a plain walk lease). The night wall formation redirect
    /// (PatchRoles_SamuraiNightFormation) reads it to leave motion-owned goals untouched.
    /// Exception -> true: without proof that no motion exists we never interleave.
    /// </summary>
    internal static bool HasActiveMotion(Knight knight)
    {
        try
        {
            if (knight == null || knight.gameObject == null) return false;
            return Actors.TryGetValue(knight.gameObject.GetInstanceID(), out ActorState a) &&
                Same(a.Owner, knight) && (a.Choreo != null || a.Motion != null);
        }
        catch (Exception e) { Log("has-motion", e); return true; }
    }

    internal static void OnKnightDisabled(Knight knight)
    {
        if (knight == null || knight.gameObject == null) return;
        int id = knight.gameObject.GetInstanceID();
        if (!Actors.TryGetValue(id, out var a) || !Same(a.Owner, knight)) return;
        // Unity stops a disabled owner's coroutine without running its finally, so the close
        // happens here too: effects, visuals and the flag all belong to this knight.
        if (a.Choreo != null) Finale(a, a.Choreo, "on-disable");
        if (a.Motion != null) Finish(a.Motion);
        Actors.Remove(id);
    }

    private static void Log(string where, Exception e)
    {
        if (Logged.Add(where)) KingdomEnhancedPlugin.Instance?.LogSource.LogError("[SamuraiDash/" + where + "] " + e);
    }
}

[HarmonyPatch(typeof(Knight), "Update")]
internal static class Knight_Update_SamuraiPowerDash_Patch
{
    private static void Prefix(Knight __instance) => PatchRoles_SamuraiPowerDash.BeforeNativeUpdate(__instance);
    private static void Postfix(Knight __instance) => PatchRoles_SamuraiPowerDash.Tick(__instance);
}

[HarmonyPatch(typeof(Knight), "OnDisable")]
internal static class Knight_OnDisable_SamuraiPowerDash_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Knight __instance) => SamuraiDashVisuals.Clear(__instance);

    [HarmonyPostfix]
    private static void Postfix(Knight __instance) => PatchRoles_SamuraiPowerDash.OnKnightDisabled(__instance);
}

[HarmonyPatch(typeof(Knight), "ShouldSlash")]
internal static class Knight_ShouldSlash_SamuraiReturn_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(Knight __instance, ref bool __result)
    {
        if (!PatchRoles_SamuraiPowerDash.SuppressesSlash(__instance)) return true;
        __result = false;
        return false;
    }
}
