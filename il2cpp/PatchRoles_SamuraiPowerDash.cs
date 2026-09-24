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
/// Choreographed round trip (2026-09-25 user ruling): the trigger reads the world exactly once --
/// an eligible enemy inside [1.5, 8.2] gives nothing but a direction, the start point and a fixed
/// seven-unit dash distance; no target reference survives the trigger frame. One coroutine then
/// owns the whole motion -- outbound dash, reverse cut home and the value-discipline finale -- so
/// no hand-off seam can strand the samurai outside the wall, and no branch depends on which enemy
/// was chosen, whether it died, or where the squad moved. The trip's home point is the position
/// the knight started from. While it runs, the actor's flag suppresses the native slash (whose
/// first line would pause the mover), suppresses the night formation redirect, blocks the night
/// wall queue and keeps Tick out; a native GoToWall goal steal is answered by a plain 0.3 s goal
/// re-assert, not by ownership. The whole trip is one protected motion (invulnerable, pinned
/// trail, visual token); the finale returns every value under the value discipline, stops only a
/// goal it still owns, and arms the stuck-pose net. The Tick hard cap (3 s) is the failsafe for a
/// coroutine that died without running its finally -- the permanent-invulnerability class.
///
/// The withdrawal ladder (Return/Walk) is untouched and still serves followers that walked off
/// with no cut running; it keeps the dash-failure backoff and the walk home.
///
/// Stuck slash pose: a round trip drives the native PowerSlash trigger, and a dash that ends while
/// that state is still current can leave the trigger unconsumed, so the knight keeps the pose.
/// The finale clears the trigger, and a bounded probe watches only the knights that actually drove
/// a round trip this session, only inside CutWindow after the last one. It compares against the
/// pose hash captured from the live animator during a trip, and a pose that outlives the native
/// slash is repaired by a trigger reset, a replay of the captured calm state and -- as a last
/// resort -- an Animator enable toggle, at most two ladders per episode.
///
/// Boundaries, deliberately not widened: knights outside the eligible pool (grabbed, petrified,
/// retreating, a foreign FSM task) are not repaired; the captured hashes live and die with the
/// actor state, exactly like the return ladder; and a repair is local presentation only -- it
/// never sends an animation-sync message, so the pre-existing gap that a knight's pose is not
/// networked stays exactly as wide as it was.
/// </summary>
internal static class PatchRoles_SamuraiPowerDash
{
    private const float ScanInterval = .2f, Cooldown = 3f, MaxRange = 10.5f;
    // The static self-scan's own reach: it must cover the whole trigger band ([1.5, 8.2]) with
    // margin, so an enemy may sit inside the scan while still being too far to open a trip.
    private const float SamuraiScanRange = 10.5f;
    // 2026-09-25 用户裁定（目标无关的编舞式往返）：冲刺距离恒为固定 7 格；触发带 = 固定 7 +
    // 命中扫描半径 1.2 = [1.5, 8.2]，带外敌人（9.0/10.5）一律不开程，出程不再按目标距离伸缩。
    private const float SamuraiFixedDashDistance = 7f;
    // 命中走廊半径：共享伤害扫描与触发带上限都引用它（旧实现是字面量 1.2）。
    private const float HitRadius = 1.2f;
    private const float TriggerRange = SamuraiFixedDashDistance + HitRadius;
    // 编舞节奏：单腿窗口 1.2 s、到点容差 .25、命中扫描 .1 s、目标重申 .3 s（对抗原生
    // GoToWall 3 s 改写的简单反制，无所有权语义）。Tick 硬上限 3 s 是协程未跑 finally 时的
    // 兜底（永久无敌类缺陷）；异常/提前退出按预算记一行（原因+相位+elapsed）。
    private const float ChoreoArrive = .25f, DashWindow = 1.2f, GoalReassert = .3f, HitInterval = .1f;
    private const float ChoreoBudget = 3f, ChoreoLogEvery = 6f;
    private const int ChoreoLogBudget = 12;
    // 残影持续 1 秒（2026-09-24）：冲刺期间把拖尾 lifetime 钉在 1 秒。原生对这个组件只开关
    // enabled、全 mod 无既有 time 写点，故快照旧值、归还仅在 trail.time 仍等于写入值时放行
    // （invulnerable/OldTrail 同款纪律）。幽灵残影的淡出窗是 SamuraiDashVisuals.Lifetime（2 s）。
    private const float SamuraiTrailLifetime = 1f;
    private const float DashSpeed = 18f, DashTimeout = .6f, FollowLeash = 10f, ReturnStop = 4f;

    // A spent dash ladder degrades into a plain walk home. While the leash is broken the
    // knight always has a goal: after any failed burst it walks, the burst is retried only
    // once its escalating backoff (2 s, 4 s, ...) has elapsed, and from the third failed
    // burst on the walk owns the way back until the samurai actually arrives.
    private const int WalkAfterFailures = 3;
    private const float RetryStep = 2f;
    // Stuck-pose probe: armed only by a round trip, considered only CutWindow after the last one,
    // repaired only once the pose outlives the native slash by StuckAfter; the capture probe is
    // retired after CaptureFailFrames misses inside one trip, the repair after HealRetryCap
    // ladders inside one episode, and the heal narrative is throttled to one incident per
    // HealLogEvery seconds and capped at HealLogBudget lines per session.
    private const float CutWindow = 8f, StuckAfter = 1.5f, HealLogEvery = 6f;
    private const int CaptureFailFrames = 30, HealRetryCap = 2, HealLogBudget = 12;
    // Turn-start narrative (2026-09-24): mirrors the heal log -- a session budget plus a
    // per-knight throttle; the old once-per-session key starved exactly this diagnosis.
    private const float TurnLogEvery = 6f;
    private const int TurnLogBudget = 12;
    private static readonly int SpeedParam = Animator.StringToHash("Speed");
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
    private static readonly int PowerSlash = Animator.StringToHash("PowerSlash");
    private static readonly HashSet<string> Logged = new();
    // Stuck-pose net (2026-09-24 recurrence): same-family knights share one AnimatorOverrideController
    // instance (BiomeSwapData.GetAnimSwap cache), so a slash hash proven on one samurai is valid for
    // every samurai on that controller -- and nothing else (cross-family pointers differ).
    private static readonly Dictionary<long, int> SlashByController = new();
    private static int HitLayerMask, EnemyScanLayer, WalkLogs, HealLogs, TurnLogs, ChoreoLogs;

    private sealed class ActorState
    {
        internal Knight Owner;
        internal Archer Follower;
        internal Mover ObservedMover;
        internal float NextFollowerScan, NextAttack, RetryAt;
        internal int Failures;
        internal MotionLease Motion;
        // Choreographed round trip (2026-09-25): the live flag (non-null = this actor owns a
        // running coroutine; the token is also the identity check that makes the finale
        // idempotent) and the value-discipline snapshots the finale restores.
        internal ChoreoToken Choreo;
        internal bool ChoreoOldInvulnerable, ChoreoOldTrail, ChoreoEffects;
        internal float ChoreoOldTrailTime, ChoreoHomeX, ChoreoStartX, NextChoreoLogAt;
        internal int ChoreoSide;
        internal Mover.FacingMode ChoreoFacingWritten;
        internal bool ChoreoFacingOwned;
        // Stuck-pose probe (see the class comment): a round trip arms it for good (HasCutHistory),
        // the captured hashes are what the pose check and the repair replay compare against, and
        // the ladder fields below track one stuck episode at a time.
        internal bool HasCutHistory, CaptureDead, HealGivenUp, LoggingHeal;
        internal float LastCutEndAt, StuckAt, NextHealLogAt, NextTurnLogAt;
        internal int SlashHash, DefaultHash;
        // SlashSource: 0=none, 1=Lease (settled during a cut -- proven), 2=Finish (inferred at the
        // cut's end -- a guess a peer's proven hash may override), 3=Adopt (peer's proven hash).
        internal int SlashSource;
        internal bool LeaseSawTransition;   // finish-capture trust gate: a Current read is only
                                            // meaningful after this trip observed a transition
        internal int CaptureBaseline, CaptureSeen, CaptureStable, CaptureFrames, CaptureFrame = -1;
        internal int DefaultSeen, DefaultStable, DefaultFrame = -1;
        internal int HealStep, HealFrame = -1, HealRetries;
    }

    private sealed class MotionLease
    {
        internal ActorState Actor;
        internal Mover Mover;
        internal Damageable Damageable;
        internal TrailRenderer Trail;
        internal SamuraiDashVisuals.Token Visual;
        internal SamuraiDashDiagnostics.Trace Diagnostics;
        internal MotionKind Kind;
        internal bool Retired, Effects, OldInvulnerable, OldTrail, HasGoal, Running;

        internal float GoalX, GoalSpeed, StartedAt, StartX, LastProgressAt, BestDistance, NextGoal;
        // The trail lifetime this motion pinned (see SamuraiTrailLifetime): restored by value.
        internal float OldTrailTime;
        // Defensive withdrawal facing: the mode we hold while returning, and whether the
        // mover field currently holds our value (so a foreign mode is never stomped).
        internal Mover.FacingMode FacingWritten;
        internal bool FacingOwned;
        // Per-motion hit bookkeeping: one hit per Damageable per dash, no per-frame allocations.
        internal readonly HashSet<IntPtr> HitObjects = new();
        internal readonly Collider2D[] Colliders = new Collider2D[16];
    }

    // Why a return lease ended: Success clears the failure ladder, Failure feeds the
    // escalating backoff, Handoff (night / actor lost / walk upgraded to a burst) keeps it.
    private enum EndReason { Handoff, Success, Failure }
    // The two motions the withdrawal ladder drives: an invulnerable defensive dash (Return) and
    // its spent run-speed walk (Walk). The attack-family round trip is the choreographed
    // coroutine below and never appears here.
    private enum MotionKind : byte { Return, Walk }

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
        if (!Same(a.Follower, nearest)) { a.Failures = 0; a.RetryAt = 0; }
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
        Same(m.Actor.Owner._mover, m.Mover) && Same(m.Actor.Owner._damageable, m.Damageable) &&
        ((m.Trail == null && m.Actor.Owner._trail == null) || Same(m.Actor.Owner._trail, m.Trail)) &&
        OwnGoal(m) && m.Mover._pauseTimeout <= 0;

    // Combat-effects half of the restore discipline: a bool cannot identify an external
    // true -> true rewrite, so restore only values that still carry ours. The trail lifetime is a
    // number, so ownership is identified by value: the owner's own lifetime comes back only while
    // the trail still carries exactly ours (same discipline as the bools).
    private static void RestoreCombatEffects(MotionLease m)
    {
        if (!Current(m) || !m.Effects) return;
        m.Effects = false;
        if (m.Damageable != null && m.Damageable.invulnerable) m.Damageable.invulnerable = m.OldInvulnerable;
        if (m.Trail != null && m.Trail.enabled) m.Trail.enabled = m.OldTrail;
        if (m.Trail != null && Mathf.Approximately(m.Trail.time, SamuraiTrailLifetime)) m.Trail.time = m.OldTrailTime;
        LogTrailState(m, "effects-restored");
    }

    // Full restore for a withdrawal lease: the visual token always ends here and the combat flags
    // follow the discipline above.
    private static void RestoreEffects(MotionLease m)
    {
        if (!Current(m)) return;
        SamuraiDashVisuals.End(m.Visual);
        RestoreCombatEffects(m);
    }

    private static void Finish(MotionLease m, EndReason reason = EndReason.Handoff)
    {
        if (!Current(m)) return;
        try
        {
            RestoreEffects(m);
            RestoreFacing(m.Mover, ref m.FacingWritten, ref m.FacingOwned);
            // Never restore a previous goal, clear an external pause, or stop a replacement mover.
            if (OwnGoal(m)) m.Mover.Stop();
        }
        catch (Exception e) { Log("finish", e); }
        finally
        {
            // Stop/RestoreEffects may synchronously re-enter and replace the lease; only clear ours.
            bool owned = Current(m);
            m.Retired = true;
            if (owned)
            {
                LogMotionEnd(m, reason == EndReason.Failure);
                if (reason == EndReason.Failure)
                {
                    m.Actor.Failures++;
                    m.Actor.RetryAt = Time.time + BackoffSeconds(m.Actor.Failures);
                }
                else if (reason == EndReason.Success) { m.Actor.Failures = 0; m.Actor.RetryAt = 0; }
                m.Actor.Motion = null;
            }
        }
    }

    private static void Goal(MotionLease m, float x, float speed)
    {
        // Caller validates lifecycle/ownership before every update, including burst -> run.
        if (!Current(m)) return;
        m.Mover.SetGoalNoHaglet(x, speed);
        m.GoalX = x; m.GoalSpeed = speed; m.HasGoal = true;
    }

    private static float BackoffSeconds(int failures) => RetryStep * Math.Min(failures, WalkAfterFailures);

    // Enemy side = the unit's own half: walls, portals and enemy waves sit at that outer
    // edge, and native Knight.SetRetreating(true) faces its defensive retreat by the very
    // same rule. Reading it needs no scan, so a return never touches the scanners.
    private static Mover.FacingMode EnemyFacing(Knight k) =>
        k.side == Side.Left ? Mover.FacingMode.Left : Mover.FacingMode.Right;

    // Facing ownership is shared by the withdrawal leases and the choreographed round trip, so
    // the three helpers take the owner's fields by reference; the discipline is identical for
    // both: only ever take over from Ahead, and never stomp a foreign fixed facing.

    // Defensive withdrawal posture: keep facing the enemy side while withdrawing.
    private static void HoldFacing(Mover mover, ref Mover.FacingMode written, ref bool owned)
    {
        if (mover == null) return;
        try
        {
            if (mover.facingMode == written) { owned = true; return; }
            if (mover.facingMode != Mover.FacingMode.Ahead) { owned = false; return; }
            mover.SetFacingMode(written, null);
            owned = true;
        }
        catch (Exception e) { Log("return-facing", e); }
    }

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

    // Bounded field evidence for the walk fallback (three lines per session, never per frame).
    private static void LogWalk(string eventName, MotionLease m)
    {
        if (WalkLogs >= 3) return;
        try
        {
            WalkLogs++;
            var a = m.Actor;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/" + eventName + "] failures=" + a.Failures
                + " x=" + a.Owner.transform.position.x.ToString("0.##")
                + " follower=" + a.Follower.transform.position.x.ToString("0.##")
                + " goal=" + m.GoalX.ToString("0.##") + " speed=" + m.GoalSpeed.ToString("0.##"));
        }
        catch { }
    }

    private static MotionLease BeginReturn(ActorState a, float goal)
    {
        Knight k = a.Owner;
        var m = new MotionLease { Actor = a, Mover = k._mover, Damageable = k._damageable,
            Trail = k._trail, Kind = MotionKind.Return, StartedAt = Time.time,
            StartX = k.transform.position.x, LastProgressAt = Time.time,
            BestDistance = Distance(a) };
        a.Motion = m;
        m.Diagnostics = SamuraiDashDiagnostics.Begin(k, true, m.StartedAt);
        m.OldInvulnerable = m.Damageable.invulnerable;
        m.OldTrail = m.Trail != null && m.Trail.enabled;
        m.OldTrailTime = m.Trail != null ? m.Trail.time : 0f;
        m.Effects = true;
        m.Damageable.invulnerable = true;
        if (m.Trail != null) { m.Trail.enabled = true; m.Trail.time = SamuraiTrailLifetime; }
        LogTrailState(m, "trail-state");
        // The withdrawal burst is a defensive dash: it faces the enemy side while withdrawing.
        // A new motion restarts the stuck-pose measurement (the choreographed round trip arms
        // the probe on its own Begin path).
        ClearEpisode(a);
        m.FacingWritten = EnemyFacing(k);
        HoldFacing(m.Mover, ref m.FacingWritten, ref m.FacingOwned);
        m.Visual = SamuraiDashVisuals.Begin(k, m.Diagnostics);
        Goal(m, goal, DashSpeed);
        return m;
    }

    private static void LogTrailState(MotionLease m, string eventName) =>
        LogTrailState(m.Trail, m.Diagnostics, m.StartedAt, eventName);

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

    private static void LogMotionEnd(MotionLease m, bool failure)
    {
        if (m.Diagnostics == null) return;
        try
        {
            SamuraiDashDiagnostics.Write(m.Diagnostics, "end", "failure=" + failure
                + " elapsed=" + (Time.time - m.StartedAt).ToString("0.###") + " kind=" + m.Kind
                + " effectsActive=" + m.Effects + " returnRunning=" + m.Running);
        }
        catch { }
    }

    private static float StationX(ActorState a)
    {
        float x = a.Owner.transform.position.x;
        // The withdrawal ladder's station: the follower's station while one is live; otherwise
        // the night formation slot or the native guard anchor -- the ladder never loses its way
        // home just because the follower died.
        float target = a.Follower != null && a.Follower.gameObject != null
            ? a.Follower.transform.position.x
            : PatchRoles_SamuraiNightFormation.HomeXOf(a.Owner);
        return target - Mathf.Sign(target - x) * 2.5f;
    }

    // ---------------------------------------------------------------------------------------
    // Stuck slash pose (see the class comment for the contract). Everything below is local
    // presentation repair: it reads the animator, writes at most trigger/state/enabled, and
    // never touches a lease, the network or gameplay.
    // ---------------------------------------------------------------------------------------

    /// <summary>Live animator reference; null for missing, destroyed or disabled animators.</summary>
    private static Animator ReadAnimator(Knight k)
    {
        try
        {
            Animator animator = k != null ? k._animator : null;
            return animator != null && animator.isActiveAndEnabled ? animator : null;
        }
        catch (Exception e) { Log("animator", e); return null; }
    }

    /// <summary>The animator's current state hash; false when it cannot be read.</summary>
    private static bool TryPoseHash(Knight k, out int hash)
    {
        hash = 0;
        Animator animator = ReadAnimator(k);
        if (animator == null) return false;
        hash = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        return true;
    }

    private static int PoseHash(Knight k)
    {
        try { return TryPoseHash(k, out int hash) ? hash : 0; }
        catch (Exception e) { Log("state-read", e); return 0; }
    }

    /// <summary>
    /// Captures the slash pose from the live animator during a round trip: only a state the
    /// animator settled on counts -- never a transition source, never the begin-frame baseline
    /// and never a single-frame flicker. One captured hash lasts for the whole actor state; a
    /// later trip that cannot re-observe it leaves it alone, and only a knight that never
    /// captured a pose can be stood down by a whole trip window of misses.
    /// </summary>
    private static void TryCaptureSlashPose(Knight k, ActorState a)
    {
        if (a.SlashHash != 0 || a.CaptureDead) return;
        if (Time.frameCount == a.CaptureFrame) return;      // at most one attempt per frame
        a.CaptureFrame = Time.frameCount;
        try
        {
            Animator animator = ReadAnimator(k);
            if (animator == null)
            {
                a.CaptureStable = 0;
                CountCaptureMiss(a);
                return;
            }
            if (animator.IsInTransition(0))
            {
                a.LeaseSawTransition = true;        // trust gate for the finish-capture's Current read
                a.CaptureStable = 0;
                CountCaptureMiss(a);
                return;
            }
            int hash = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            // Trust gate (2026-09-24 recurrence): a settled pose this trip never entered through
            // a transition is untrustworthy -- it kills the whole teleport/no-transition-entry
            // false-capture class (an aborted cut drifting into a walk pose mid-trip).
            if (hash == 0 || hash == a.CaptureBaseline || !a.LeaseSawTransition)
            {
                a.CaptureSeen = hash;
                a.CaptureStable = 0;
                CountCaptureMiss(a);
                return;
            }
            if (hash != a.CaptureSeen)
            {
                a.CaptureSeen = hash;
                a.CaptureStable = 1;
                CountCaptureMiss(a);
                return;
            }
            a.CaptureStable++;
            if (a.CaptureStable < 2) { CountCaptureMiss(a); return; }
            a.SlashHash = hash;
            a.SlashSource = 1;                      // proven: settled inside a live cut
            if (a.DefaultHash == hash) a.DefaultHash = 0;   // the slash pose is never a calm state
            RememberControllerSlash(animator, hash);
            if (Logged.Add("slash-capture-" + k.gameObject.GetInstanceID()))
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/slash-capture] knight=" +
                    k.gameObject.GetInstanceID() + " hash=" + hash
                    + " baseline=" + a.CaptureBaseline + " frames=" + a.CaptureFrames);
        }
        catch (Exception e) { Log("slash-capture", e); }
    }

    /// <summary>
    /// Stuck-pose net, finish leg (2026-09-24 recurrence): a cut shorter than its own transition can
    /// never capture in-flight, and a knight already stuck poisons every later cut's baseline
    /// (Begin reads the stuck pose), blocking self-capture for good. At the cut's end: capture what
    /// the cut is leaving behind -- the transition target when mid-flight, else the settled state
    /// only when this trip actually observed a transition (an untransitioned Current is
    /// untrustworthy) -- then let a peer's proven hash (same shared controller) adopt a blind or
    /// mis-captured one.
    /// </summary>
    private static void CaptureSlashAtFinish(ActorState a, Knight k)
    {
        Animator animator = ReadAnimator(k);
        if (animator == null) return;
        if (a.SlashHash == 0)
        {
            int candidate;
            if (animator.IsInTransition(0))
                candidate = animator.GetNextAnimatorStateInfo(0).shortNameHash;      // where the stuck pose is heading
            else if (a.LeaseSawTransition)
                candidate = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;   // settled post-transition
            else
                candidate = 0;                                                       // no transition history: reject
            if (candidate != 0 && candidate != a.CaptureBaseline)
            {
                a.SlashHash = candidate;
                a.SlashSource = 2;                  // inferred: a peer's proven hash may override
                if (a.DefaultHash == candidate) a.DefaultHash = 0;
                // guesses never enter the shared map -- only lease-proven hashes do
                if (Logged.Add("slash-finish-capture-" + k.gameObject.GetInstanceID()))
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/slash-finish-capture] knight=" +
                        k.gameObject.GetInstanceID() + " hash=" + candidate);
            }
        }
        AdoptPeerSlash(a, k, animator);
    }

    /// <summary>
    /// A lease-proven or already-adopted hash is never overridden; a finish-inferred one (or a blind
    /// knight) takes any peer hash remembered for the same shared controller instance.
    /// </summary>
    private static void AdoptPeerSlash(ActorState a, Knight k, Animator animator)
    {
        if (a.SlashSource == 1 || a.SlashSource == 3) return;
        RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        if (controller == null) return;
        long key = controller.Pointer.ToInt64();
        if (!SlashByController.TryGetValue(key, out int hash) || hash == 0) return;
        if (a.SlashHash == hash) { if (a.SlashSource == 0) a.SlashSource = 3; return; }
        a.SlashHash = hash;
        a.SlashSource = 3;
        if (a.DefaultHash == hash) a.DefaultHash = 0;
        if (Logged.Add("slash-adopt-" + k.gameObject.GetInstanceID()))
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/slash-adopt] knight=" +
                k.gameObject.GetInstanceID() + " controller=" + key + " hash=" + hash);
    }

    private static void RememberControllerSlash(Animator animator, int hash)
    {
        try
        {
            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            if (controller != null) SlashByController[controller.Pointer.ToInt64()] = hash;
        }
        catch { /* diagnostics only: a missed memory never blocks the capture itself */ }
    }

    /// <summary>
    /// A whole lease window of gate misses retires the capture probe for good -- but the counter
    /// is per lease, so a short lease can never accumulate a later lease's misses.
    /// </summary>
    private static void CountCaptureMiss(ActorState a)
    {
        a.CaptureFrames++;
        if (a.CaptureFrames > CaptureFailFrames) a.CaptureDead = true;
    }

    /// <summary>
    /// The replay pose for the repair: captured once per actor state on any settled frame
    /// (no lease, no transition, no native slash pause) whose hash holds for two frames and
    /// differs from the captured slash pose — walking/running included (field evidence
    /// 2026-09-24: Speed-at-rest starved the capture through whole combat sessions).
    /// Failure is silent; the next settled frame retries.
    /// </summary>
    private static void TryCaptureDefaultPose(Knight k, ActorState a)
    {
        if (a.DefaultHash != 0) return;
        if (Time.frameCount == a.DefaultFrame) return;
        a.DefaultFrame = Time.frameCount;
        try
        {
            // 2026-09-24 实机修订：不再要求 Speed 归零——战斗中速度参数几乎从不为 0，
            // 旧条件让捕获长期饥饿（实机 heal 走到 play-skipped/toggle 兜底的直接原因）。
            // 走路/跑步姿态同样是合格的回放目标；非转移、非本砍击态、非原生斩击暂停、
            // 两帧稳定四道守卫保留（原生 Slash 态仍被 pauseTimeout 门排除在外）。
            Animator animator = ReadAnimator(k);
            if (animator == null || animator.IsInTransition(0) ||
                k._mover == null || k._mover._pauseTimeout > 0)
            {
                a.DefaultStable = 0;
                return;
            }
            int hash = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            if (hash == 0 || hash == a.SlashHash) { a.DefaultStable = 0; return; }
            if (hash != a.DefaultSeen) { a.DefaultSeen = hash; a.DefaultStable = 1; return; }
            a.DefaultStable++;
            if (a.DefaultStable < 2) return;
            a.DefaultHash = hash;
            if (Logged.Add("default-capture-" + k.gameObject.GetInstanceID()))
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/default-capture] knight=" +
                    k.gameObject.GetInstanceID() + " hash=" + hash);
        }
        catch (Exception e) { Log("default-capture", e); }
    }

    /// <summary>
    /// Watches for the one pose this module can leave behind: the captured slash hash still
    /// current once the native slash would long be over. Only a knight that drove a cut lease,
    /// inside CutWindow after its end, is ever considered.
    /// </summary>
    private static void ProbeStuckPose(Knight k, ActorState a)
    {
        if (!a.HasCutHistory || a.SlashHash == 0 || a.HealGivenUp) return;
        try
        {
            if (Time.time - a.LastCutEndAt > CutWindow) { ClearEpisode(a); return; }
            if (!TryPoseHash(k, out int hash)) return;      // unreadable: keep the episode as it is
            if (hash != a.SlashHash) { RecoverStuckPose(a, k); return; }
            if (Time.timeScale <= 0) return;
            if (a.HealStep == 0)
            {
                if (a.StuckAt == 0) { a.StuckAt = Time.time; return; }
                if (Time.time - a.StuckAt < StuckAfter) return;
            }
            Heal(k, a);
        }
        catch (Exception e) { Log("stuck-probe", e); }
    }

    private static void ClearEpisode(ActorState a)
    {
        a.StuckAt = 0;
        a.HealStep = 0;
        a.HealFrame = -1;
        a.HealRetries = 0;
        a.LoggingHeal = false;
    }

    private static void RecoverStuckPose(ActorState a, Knight k)
    {
        if (a.StuckAt != 0 || a.HealStep != 0)
            HealLog(a, k, "recovered", a.HealRetries + 1, closes: true);
        ClearEpisode(a);
    }

    /// <summary>
    /// One repair ladder, one step per frame so every write can be re-checked before the next:
    /// clear the leftover trigger, replay the captured calm state, and only then fall back to
    /// toggling the Animator (the fallback for a session that never captured a calm pose). A
    /// ladder the pose survives counts as one failed retry; after HealRetryCap the probe stands
    /// down until the next cut lease arms it again.
    /// </summary>
    private static void Heal(Knight k, ActorState a)
    {
        if (a.HealGivenUp) return;
        try
        {
            Animator animator = ReadAnimator(k);
            if (animator == null) return;
            if (a.HealStep == 0)
            {
                HealLog(a, k, "reset", a.HealRetries + 1, opens: true);
                animator.ResetTrigger(PowerSlash);
                a.HealStep = 1;
                a.HealFrame = Time.frameCount;
                return;
            }
            if (Time.frameCount <= a.HealFrame) return;     // every step gets its own frame
            if (a.HealStep == 1)
            {
                if (a.DefaultHash != 0)
                {
                    HealLog(a, k, "play", a.HealRetries + 1);
                    animator.Play(a.DefaultHash, 0, 0f);
                }
                else HealLog(a, k, "play-skipped", a.HealRetries + 1);
                a.HealStep = 2;
                a.HealFrame = Time.frameCount;
                return;
            }
            if (a.HealStep == 2)
            {
                HealLog(a, k, "toggle", a.HealRetries + 1);
                animator.enabled = false;
                animator.enabled = true;
                a.HealStep = 3;
                a.HealFrame = Time.frameCount;
                return;
            }
            // Step 3: the ladder is spent and the pose is still current. That is one retry.
            a.HealRetries++;
            a.StuckAt = Time.time;
            a.HealStep = 0;
            a.HealFrame = -1;
            if (a.HealRetries >= HealRetryCap)
            {
                a.HealGivenUp = true;
                HealLog(a, k, "gave-up", a.HealRetries, closes: true);
            }
            else HealLog(a, k, "retry", a.HealRetries);
        }
        catch (Exception e) { Log("heal", e); }
    }

    /// <summary>
    /// Bounded heal narrative: one reported incident per HealLogEvery seconds per knight, at
    /// most HealLogBudget lines per session, and every line carries the state the diagnosis
    /// needs (normalizedTime tells a clip-end block from a looping re-entry).
    /// </summary>
    private static void HealLog(ActorState a, Knight k, string step, int order, bool opens = false, bool closes = false)
    {
        if (HealLogs >= HealLogBudget) return;
        if (opens)
        {
            if (Time.time < a.NextHealLogAt) return;
            a.NextHealLogAt = Time.time + HealLogEvery;
            a.LoggingHeal = true;
        }
        else if (!a.LoggingHeal) return;
        if (closes) a.LoggingHeal = false;
        HealLogs++;
        try
        {
            string details = "";
            Animator animator = k != null ? k._animator : null;
            if (animator != null)
            {
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                details = " normalizedTime=" + info.normalizedTime.ToString("0.###") +
                    " fullPathHash=" + info.fullPathHash;
            }
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/heal] knight=" +
                (k != null ? k.gameObject.GetInstanceID() : 0) +
                " step=" + step +
                " order=" + order + " retries=" + a.HealRetries + " slashHash=" + a.SlashHash +
                " defaultHash=" + a.DefaultHash + details);
        }
        catch { }
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
                a.Failures = 0; a.RetryAt = 0; // a replacement mover starts the ladder clean
                return; // A replacement mover's first observed goal belongs to its new owner.
            }
            if (a != null && (!Same(a.Owner, knight) || !Eligible(knight)))
            {
                if (a.Choreo != null) Finale(a, a.Choreo, "ineligible");
                if (a.Motion != null) Finish(a.Motion);
                Actors.Remove(id); a = null;
            }
            if (!Eligible(knight)) return;
            if (a == null) { a = new ActorState { Owner = knight, ObservedMover = knight._mover }; Actors[id] = a; }
            // The choreographed round trip owns the knight from its first frame to its finale; the
            // coroutine advances it, so Tick only enforces the hard cap (the failsafe for a
            // coroutine that died without running its finally).
            if (a.Choreo != null)
            {
                if (Time.time - a.Choreo.StartedAt >= ChoreoBudget) Finale(a, a.Choreo, "tick-cap");
                return;
            }
            if (a.Motion != null)
            {
                MotionLease m = a.Motion;
                if (!ValidMotion(m)) { Finish(m, EndReason.Failure); return; }
                // The withdrawal family steps aside for the native night goal when dusk arrives.
                if (NightGuard(knight)) { Finish(m); return; }
                // Verify this reference each frame before periodic reselection, never mask its loss.
                if (!ValidFollower(knight, a.Follower)) { Finish(m); a.Follower = null; return; }
                RefreshFollower(a);
                AdvanceReturn(m);
                return; // Return and Walk are mutually exclusive, including the 4..10 band.
            }
            TryCaptureDefaultPose(knight, a);
            ProbeStuckPose(knight, a);
            RefreshFollower(a);
            bool follower = ValidFollower(knight, a.Follower);
            if (!follower) { a.Follower = null; a.Failures = 0; a.RetryAt = 0; }
            float distance = follower ? Distance(a) : 0;
            if (distance <= ReturnStop) { a.Failures = 0; a.RetryAt = 0; }
            // At night the wall coroutine supplies its current destination and defensive
            // facing. Keep the follower leash, but never turn this homeward leg into an attack.
            if (NightGuard(knight))
            {
                a.Failures = 0; a.RetryAt = 0;
                if (distance > FollowLeash) return;
            }
            // An attack-initiated withdrawal is owned by its ladder lease from the first frame --
            // the defensive burst only serves followers that walked away with no cut running.
            if (distance > FollowLeash || (a.Failures > 0 && distance > ReturnStop))
            {
                if (Time.timeScale <= 0 || knight._mover._pauseTimeout > 0) return;
                // A burst is a privilege, walking home is the baseline: while the leash is
                // broken the samurai never stands still -- a failed burst is answered by a
                // plain run-speed walk, and the burst itself comes back once its backoff has
                // elapsed (never again once WalkAfterFailures bursts have failed).
                bool burst = a.Failures == 0 ||
                    (a.Failures < WalkAfterFailures && Time.time >= a.RetryAt);
                if (!burst) { BeginWalk(a); return; }
                float x = knight.transform.position.x;
                var dash = BeginReturn(a, x + Mathf.Clamp(StationX(a) - x, -MaxRange, MaxRange));
                HitScan(dash); // entry frame hits, same as every other burst
                if (Current(dash) && (!ValidMotion(dash) || !ValidFollower(knight, a.Follower)))
                    Finish(dash, EndReason.Failure);
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
            else if (a?.Motion != null) Finish(a.Motion, EndReason.Failure);
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
        internal TrailRenderer Trail;
        internal SamuraiDashVisuals.Token Visual;
        internal SamuraiDashDiagnostics.Trace Diagnostics;
        internal float StartedAt, GoalX, GoalSpeed;
        internal bool HasGoal;
        internal string Phase = "out";
        // Per-leg hit bookkeeping: outbound and home may each strike a given enemy once.
        internal readonly HashSet<IntPtr> HitObjects = new();
        internal readonly Collider2D[] Colliders = new Collider2D[16];
    }

    private static bool ChoreoAlive(ActorState a, ChoreoToken token) =>
        a != null && token != null && ReferenceEquals(a.Choreo, token);

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

    internal static bool SuppressesSlash(Knight knight) => IsReturning(knight) || IsChoreoActive(knight);

    /// <summary>
    /// Opens one round trip: snapshot the values the finale will restore, apply the protected
    /// motion (invulnerable, pinned trail, visual token), arm the stuck-pose capture net and
    /// start the coroutine. A failed start closes immediately instead of leaving the flag live.
    /// </summary>
    private static void StartChoreo(ActorState a, int side)
    {
        Knight k = a.Owner;
        a.ChoreoSide = side;
        a.ChoreoHomeX = k.transform.position.x;
        a.ChoreoStartX = a.ChoreoHomeX;
        var token = new ChoreoToken { Mover = k._mover, Damageable = k._damageable, Trail = k._trail, StartedAt = Time.time };
        a.Choreo = token;
        a.ChoreoEffects = true;
        a.ChoreoOldInvulnerable = token.Damageable.invulnerable;
        a.ChoreoOldTrail = token.Trail != null && token.Trail.enabled;
        a.ChoreoOldTrailTime = token.Trail != null ? token.Trail.time : 0f;
        token.Damageable.invulnerable = true;
        if (token.Trail != null) { token.Trail.enabled = true; token.Trail.time = SamuraiTrailLifetime; }
        // The stuck-pose capture net arms with the cut, exactly like the retired lease's Begin.
        ClearEpisode(a);
        a.HasCutHistory = true;
        a.HealGivenUp = false;
        a.CaptureBaseline = PoseHash(k);
        a.CaptureSeen = a.CaptureBaseline;
        a.CaptureStable = 0;
        a.CaptureFrames = 0;
        a.LeaseSawTransition = false;
        token.Diagnostics = SamuraiDashDiagnostics.Begin(k, false, token.StartedAt);
        LogChoreoTrail(token, "trail-state");
        token.Visual = SamuraiDashVisuals.Begin(k, token.Diagnostics);
        // 出程：触发拔刀姿态并立刻取固定 7 格目标（循环内的 .3 s 重申是防 GoToWall 改写的）。
        try
        {
            if (k._animator != null) k._animator.SetTrigger(PowerSlash);
        }
        catch (Exception e) { Log("choreo-trigger", e); }
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
    /// The whole trip in one coroutine: the outbound leg and the home leg share one loop shape
    /// (fixed goal, 1.2 s window, 0.1 s hit cadence, 0.3 s goal re-assert, eligibility checked
    /// every iteration) and the turn runs between them. Every exit -- complete, interrupted or
    /// throwing -- closes through the same Finale; the flag suppresses the native slash.
    /// </summary>
    private static IEnumerator ChoreoRoutine(ActorState a, ChoreoToken token)
    {
        Knight k = a.Owner;
        string step = "complete";
        int leg = 0;                                        // 0 = outbound, 1 = home
        float goal = a.ChoreoHomeX + a.ChoreoSide * SamuraiFixedDashDistance;
        float deadline = Time.time + DashWindow;
        float nextHit = 0f, nextGoal = Time.time + GoalReassert;
        while (true)
        {
            // (A yield may not sit inside a try that has a catch, so the iteration is synchronous
            // and the yield lives at the loop tail.)
            try
            {
                if (!ChoreoAlive(a, token) || !Same(k._mover, token.Mover) || !Eligible(k))
                {
                    step = leg == 0 ? "out-interrupt" : "home-interrupt";
                    break;
                }
                if (Time.timeScale > 0)
                {
                    if (Time.time >= nextHit) { nextHit = Time.time + HitInterval; ChoreoHitScan(a, token); }
                    if (!ChoreoAlive(a, token) || !Same(k._mover, token.Mover) || !Eligible(k))
                    {
                        step = leg == 0 ? "out-interrupt" : "home-interrupt";
                        break;
                    }
                    if (Time.time >= nextGoal) { nextGoal = Time.time + GoalReassert; ChoreoGoal(a, token, goal, DashSpeed); }
                    if (leg == 0) TryCaptureSlashPose(k, a);
                }
                if (Mathf.Abs(k.transform.position.x - goal) <= ChoreoArrive || Time.time >= deadline)
                {
                    if (leg == 1) break;                        // home: the trip is complete
                    TurnChoreo(a, token);                       // outbound: the reverse cut home
                    if (!ChoreoAlive(a, token) || !Same(k._mover, token.Mover) || !Eligible(k))
                    {
                        step = "turn-interrupt";
                        break;
                    }
                    leg = 1;
                    token.Phase = "home";
                    goal = a.ChoreoHomeX;
                    deadline = Time.time + DashWindow;
                    nextHit = Time.time + HitInterval;          // the turn already struck on its entry frame
                    nextGoal = Time.time + GoalReassert;
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
    /// The reverse cut: replay the captured slash pose (or re-fire the trigger when no pose was
    /// ever captured), turn toward home, put the goal on the start point and strike the entry
    /// frame. Runs in the coroutine's own path, so no hand-off seam exists.
    /// </summary>
    private static void TurnChoreo(ActorState a, ChoreoToken token)
    {
        Knight k = a.Owner;
        token.Phase = "turn";
        try
        {
            if (k._animator != null)
            {
                // 2026-09-25 用户裁定（反方向再出刀冲刺回来=燕返）：出程结束时动画机多半
                // 还在 PowerSlash 状态里，重触发不可见（实机"保持出刀姿势滑回来"）。把
                // 捕获的出刀状态从第 0 帧重放=可见的重新拔刀；无捕获时退回触发器。
                k._animator.ResetTrigger(PowerSlash);
                if (a.SlashHash != 0) k._animator.Play(a.SlashHash, 0, 0f);
                else k._animator.SetTrigger(PowerSlash);
            }
        }
        catch (Exception e) { Log("choreo-turn-trigger", e); }
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
    /// leg, stopped mid-frame whenever a synchronous damage callback retires the run.
    /// </summary>
    private static void ChoreoHitScan(ActorState a, ChoreoToken token)
    {
        if (!ChoreoAlive(a, token) || Time.timeScale <= 0 || !Eligible(a.Owner)) return;
        if (HitLayerMask == 0) HitLayerMask = LayerMask.GetMask("Enemies", "Wildlife");
        Knight k = a.Owner;
        int count = Physics2D.OverlapCircleNonAlloc(k.transform.position, HitRadius, token.Colliders, HitLayerMask);
        for (int i = 0; i < count; i++)
        {
            if (!ChoreoAlive(a, token) || !Eligible(k)) return;
            Collider2D hit = token.Colliders[i];
            if (hit == null) continue;
            Damageable enemy = hit.GetComponent<Damageable>();
            if (enemy == null || !enemy.IsDamagedBy(DamageSource.Knight)) continue;
            if (!ChoreoAlive(a, token) || !Eligible(k)) return;
            if (!token.HitObjects.Add(enemy.Pointer)) continue;
            enemy.ReceiveDamage(k._attackDamage, k.gameObject, DamageSource.Knight);
        }
    }

    /// <summary>
    /// The single close for one round trip: idempotent by token identity, so OnDisable, the Tick
    /// hard cap and the coroutine's own finally can all call it and only the first one writes.
    /// The flag is cleared first -- a Stop callback that starts a new motion must never be erased
    /// by this retired one -- then effects return by value, facing is released, the stuck-pose
    /// probe is armed (trigger reset + finish capture) and a goal still ours is stopped.
    /// </summary>
    private static void Finale(ActorState a, ChoreoToken token, string step)
    {
        if (!ChoreoAlive(a, token)) return;
        a.Choreo = null;
        Knight k = a.Owner;
        try
        {
            LogChoreoClose(a, token, step);
            SamuraiDashVisuals.End(token.Visual);
            RestoreChoreoEffects(a, token);
            RestoreFacing(token.Mover, ref a.ChoreoFacingWritten, ref a.ChoreoFacingOwned);
            a.LastCutEndAt = Time.time;             // arms the stuck-pose probe window
            try { if (k != null && k._animator != null) k._animator.ResetTrigger(PowerSlash); }
            catch (Exception e) { Log("reset-trigger", e); }
            // Stuck-pose capture net, finish leg: short-of-transition cuts and baseline-poisoned
            // knights can never capture in-flight; peers on the same controller may still know it.
            try { if (k != null) CaptureSlashAtFinish(a, k); }
            catch (Exception e) { Log("finish-capture", e); }
            // Never restore a previous goal, clear an external pause, or stop a replacement mover.
            if (ChoreoOwnGoal(a, token)) token.Mover.Stop();
        }
        catch (Exception e) { Log("choreo-finale", e); }
    }

    // Combat-effects half of the choreography's restore discipline: the same value discipline the
    // retired lease used -- a bool cannot identify an external true -> true rewrite, so restore
    // only values that still carry ours; the trail lifetime restores only while it is still ours.
    private static void RestoreChoreoEffects(ActorState a, ChoreoToken token)
    {
        if (!a.ChoreoEffects) return;
        a.ChoreoEffects = false;
        if (token.Damageable != null && token.Damageable.invulnerable) token.Damageable.invulnerable = a.ChoreoOldInvulnerable;
        if (token.Trail != null && token.Trail.enabled) token.Trail.enabled = a.ChoreoOldTrail;
        if (token.Trail != null && Mathf.Approximately(token.Trail.time, SamuraiTrailLifetime)) token.Trail.time = a.ChoreoOldTrailTime;
        LogChoreoTrail(token, "effects-restored");
    }

    // Early-exit narrative (budgeted): one line per abnormal close, a session budget plus a
    // per-knight throttle, carrying the reason, the phase and the elapsed time for the log.
    private static void LogChoreoClose(ActorState a, ChoreoToken token, string step)
    {
        if (step == "complete" || ChoreoLogs >= ChoreoLogBudget) return;
        if (Time.time < a.NextChoreoLogAt) return;
        a.NextChoreoLogAt = Time.time + ChoreoLogEvery;
        ChoreoLogs++;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/choreo] knight=" +
                (a.Owner != null && a.Owner.gameObject != null ? a.Owner.gameObject.GetInstanceID() : 0) +
                " step=" + step + " phase=" + token.Phase + " elapsed=" +
                (Time.time - token.StartedAt).ToString("0.###") + " startX=" + a.ChoreoStartX.ToString("0.##"));
        }
        catch { }
    }

    private static void LogChoreoTrail(ChoreoToken token, string eventName) =>
        LogTrailState(token.Trail, token.Diagnostics, token.StartedAt, eventName);

    private static void AdvanceReturn(MotionLease m)
    {
        if (!ValidMotion(m)) { Finish(m, EndReason.Failure); return; }
        var a = m.Actor;
        if (!ValidFollower(a.Owner, a.Follower)) { Finish(m, EndReason.Handoff); return; }
        if (m.Kind == MotionKind.Walk) { HoldFacing(m.Mover, ref m.FacingWritten, ref m.FacingOwned); AdvanceWalk(m); return; }

        HoldFacing(m.Mover, ref m.FacingWritten, ref m.FacingOwned);
        float distance = Distance(a), now = Time.time;
        if (!m.Running && Time.timeScale > 0 && now - m.StartedAt < DashTimeout &&
            Mathf.Abs(a.Owner.transform.position.x - m.StartX) < MaxRange)
            HitScan(m);
        // A hit callback may synchronously disable the knight or replace this lease; never touch the new one.
        if (!Current(m)) return;
        if (!ValidMotion(m)) { Finish(m, EndReason.Failure); return; }
        if (!ValidFollower(a.Owner, a.Follower)) { Finish(m, EndReason.Handoff); return; }
        distance = Distance(a); now = Time.time;
        if (distance <= ReturnStop) { Finish(m, EndReason.Success); return; }
        if (distance < m.BestDistance - .1f) { m.BestDistance = distance; m.LastProgressAt = now; }
        if (now - m.StartedAt >= 3f || now - m.LastProgressAt >= .5f) { Finish(m, EndReason.Failure); return; }
        if (Time.timeScale <= 0) return;
        if (!m.Running && (now - m.StartedAt >= DashTimeout ||
            Mathf.Abs(a.Owner.transform.position.x - m.StartX) >= MaxRange - .05f))
        {
            RestoreEffects(m);
            m.Running = true;
            Goal(m, StationX(a), a.Owner._runSpeed);
            m.NextGoal = now + ScanInterval;
        }
        else if (m.Running && now >= m.NextGoal)
        {
            m.NextGoal = now + ScanInterval;
            float target = StationX(a);
            if (Mathf.Abs(target - m.GoalX) > .25f) Goal(m, target, a.Owner._runSpeed);
        }
    }

    // Plain run-speed walk home: the fallback that keeps a stranded samurai moving once the
    // dash ladder is spent. No effects, no invulnerability, no hit scan, no visuals and no
    // deadline -- only arrival, a lost follower, a stolen goal or dusk ends it.
    private static void BeginWalk(ActorState a)
    {
        Knight k = a.Owner;
        var m = new MotionLease
        {
            Actor = a, Mover = k._mover, Damageable = k._damageable, Trail = k._trail,
            Kind = MotionKind.Walk, Running = true, // Running keeps CanHit false
            StartedAt = Time.time, StartX = k.transform.position.x,
            FacingWritten = EnemyFacing(k)
        };
        a.Motion = m;
        HoldFacing(m.Mover, ref m.FacingWritten, ref m.FacingOwned);
        Goal(m, StationX(a), k._runSpeed);
        m.NextGoal = Time.time + ScanInterval;
        LogWalk("walk", m);
    }

    // The walk phase carries no burst deadlines and suppresses no slash: the samurai may keep
    // defending itself on the way back, and only real ownership loss ends the walk.
    private static void AdvanceWalk(MotionLease m)
    {
        if (!ValidMotion(m)) { Finish(m, EndReason.Failure); return; }
        var a = m.Actor;
        float now = Time.time;
        if (Distance(a) <= ReturnStop) { LogWalk("walk-done", m); Finish(m, EndReason.Success); return; }
        if (now >= m.NextGoal)
        {
            m.NextGoal = now + ScanInterval;
            float target = StationX(a);
            if (Mathf.Abs(target - m.GoalX) > .25f) Goal(m, target, a.Owner._runSpeed);
        }
        // The backoff elapsed while walking: hand back to a fresh burst attempt (next frame).
        if (a.Failures < WalkAfterFailures && now >= a.RetryAt) Finish(m, EndReason.Handoff);
    }

    private static bool CanHit(MotionLease m)
    {
        if (m.Running || Time.timeScale <= 0 || !ValidMotion(m)) return false;
        var a = m.Actor;
        if (Mathf.Abs(a.Owner.transform.position.x - m.StartX) >= MaxRange) return false;
        // The return burst strikes only while its dash window runs and the follower leash holds;
        // the walk home (Running) never damages.
        return Time.time - m.StartedAt < DashTimeout && ValidFollower(a.Owner, a.Follower);
    }

    /// <summary>Shared burst hit scan used by the withdrawal ladder's return dash.</summary>
    private static void HitScan(MotionLease m)
    {
        if (!CanHit(m)) return;
        if (HitLayerMask == 0) HitLayerMask = LayerMask.GetMask("Enemies", "Wildlife");
        var a = m.Actor;
        int count = Physics2D.OverlapCircleNonAlloc(a.Owner.transform.position, HitRadius, m.Colliders, HitLayerMask);
        for (int i = 0; i < count && CanHit(m); i++)
        {
            Collider2D hit = m.Colliders[i];
            if (hit == null) continue;
            Damageable enemy = hit.GetComponent<Damageable>();
            if (enemy == null || !enemy.IsDamagedBy(DamageSource.Knight)) continue;
            if (!CanHit(m)) return;
            if (!m.HitObjects.Add(enemy.Pointer)) continue;
            // Continue through distinct enemies only while synchronous callbacks leave this burst valid.
            enemy.ReceiveDamage(a.Owner._attackDamage, a.Owner.gameObject, DamageSource.Knight);
            if (!CanHit(m)) return;
        }
    }

    internal static bool IsReturning(Knight knight)
    {
        try
        {
            if (knight == null || knight.gameObject == null) return false;
            if (!Actors.TryGetValue(knight.gameObject.GetInstanceID(), out var a) || !Same(a.Owner, knight)) return false;
            var m = a.Motion;
            // Only the invulnerable ordinary return burst suppresses the native slash; a
            // walking withdrawal is a plain retreat, and the choreographed round trip keeps
            // its own flag-based suppression (IsChoreoActive) for the whole trip.
            return m != null && m.Kind == MotionKind.Return && ValidMotion(m) &&
                ValidFollower(knight, a.Follower) && Distance(a) > ReturnStop;
        }
        catch (Exception e) { Log("should-slash", e); return false; }
    }

    /// <summary>
    /// True while this knight's actor entry owns any live motion (the choreographed round trip
    /// flag or a Return/Walk lease). The night wall formation redirect
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
