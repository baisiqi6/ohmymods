using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// One motion lease per knight. A retired lease can never write again.
///
/// Atomic round trip (2026-09-24): the outbound attack dash, the reverse cut and the walk home
/// are one lease with three phases, so no hand-off seam can strand the samurai outside the wall.
/// A broken leash turns the dash early instead of finishing it; the reverse cut keeps the
/// swallow's mechanics (dash speed, hit scan, invulnerability for one dash window) and degrades
/// into a vulnerable enemy-facing walk home on arrival, no progress or the window's end; the
/// whole lease is capped so a blocked trip still hands back to the ladder and the night systems.
/// The withdrawal ladder (Return/Walk) is untouched and still serves followers that walked off
/// with no cut running; only the debt the old cut-end hand-off dropped is retired with it.
///
/// Stuck slash pose: a cut lease drives the native PowerSlash trigger, and a dash that ends while
/// that state is still current can leave the trigger unconsumed, so the knight keeps the pose.
/// Finish clears the trigger, and a bounded probe watches only the knights that actually drove a
/// cut lease this session, only inside CutWindow after the last one. It compares against the pose
/// hash captured from the live animator during a cut, and a pose that outlives the native slash
/// is repaired by a trigger reset, a replay of the captured calm state and -- as a last resort --
/// an Animator enable toggle, at most two ladders per episode.
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
    // The static self-scan's own reach, kept independent of the dash cap on purpose: the two
    // numbers answer different questions (what the samurai may attack vs. how far a dash runs).
    private const float SamuraiScanRange = 10.5f;
    // The attack family's leash -- and only its: MaxRange + 2.5 station offset + .5 aim inset
    // is exactly the furthest the follower can be while a round trip that started legally
    // is still running, so a dash never cuts itself off on its own start frame. The return
    // family keeps FollowLeash: that is the walk-home trigger and must not move with the cap.
    private const float AttackLeash = MaxRange + 2.5f + .5f;
    // 2026-09-24 用户裁定：攻击冲刺改混合固定距离——goal = startX + Sign(dx) × max(|dx| - .5, 7)。
    // 近敌(|dx|≤7.5)固定 7 格穿过目标（消灭贴墙 0.04-0.09 s 小戳、燕返行程门永远不过、8.2-10.5
    // 零伤害死区），远敌照常停在敌前 0.5。timeout 最长 10/18≈.556<.6、燕返行程窗 [7,10]⊆[1.5,10.5]，
    // AttackLeash 不变量不动；用户要更长冲刺只改这一个常量即可。
    private const float SamuraiFixedDashDistance = 7f;
    // 残影持续 1 秒（2026-09-24）：冲刺期间把拖尾 lifetime 钉在 1 秒。原生对这个组件只开关
    // enabled、全 mod 无既有 time 写点，故 Begin 快照 OldTrailTime、RestoreEffects 仅在
    // trail.time 仍等于写入值时归还（invulnerable/OldTrail 同款纪律）。
    private const float SamuraiTrailLifetime = 1f;
    private const float DashSpeed = 18f, DashTimeout = .6f, FollowLeash = 10f, ReturnStop = 4f;
    // Atomic round trip (2026-09-24 用户裁定): 冲刺出去+返回合并为单一租约——出程冲刺、反向斩
    // （原燕返机制，无概率无冷却：怪堆里防御姿态退回=送死，反向冲刺才是保命手段）与走路回家
    // 在同一 lease 内完成，消灭两段拼接缝。turn 到达容差、停滞看门狗（.5 s 无进展即降级为
    // 走路）、整租约 ~3.5 s 上限；超时 Finish 后夜列队/返程阶梯接管。
    private const float TurnArrive = .25f, RoundTripStall = .5f, RoundTripLease = 3.5f;

    // A spent dash ladder degrades into a plain walk home. While the leash is broken the
    // knight always has a goal: after any failed burst it walks, the burst is retried only
    // once its escalating backoff (2 s, 4 s, ...) has elapsed, and from the third failed
    // burst on the walk owns the way back until the samurai actually arrives.
    private const int WalkAfterFailures = 3;
    private const float RetryStep = 2f;
    // Stuck-pose probe: armed only by a cut lease, considered only CutWindow after the last one,
    // repaired only once the pose outlives the native slash by StuckAfter; the capture probe is
    // retired after CaptureFailFrames misses inside one lease, the repair after HealRetryCap
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

    /// <summary>仅诊断用（FrameWatch 记行时才遍历）：当前在跑的 RoundTrip 租约数。</summary>
    internal static int ActiveCutLeases
    {
        get
        {
            int n = 0;
            foreach (KeyValuePair<int, ActorState> pair in Actors)
            {
                MotionLease m = pair.Value?.Motion;
                if (m != null && !m.Retired && m.Kind == MotionKind.RoundTrip) n++;
            }
            return n;
        }
    }
    private static readonly int PowerSlash = Animator.StringToHash("PowerSlash");
    private static readonly HashSet<string> Logged = new();
    // Stuck-pose net (2026-09-24 recurrence): same-family knights share one AnimatorOverrideController
    // instance (BiomeSwapData.GetAnimSwap cache), so a slash hash proven on one samurai is valid for
    // every samurai on that controller -- and nothing else (cross-family pointers differ).
    private static readonly Dictionary<long, int> SlashByController = new();
    private static int HitLayerMask, EnemyScanLayer, WalkLogs, HealLogs, TurnLogs;

    private sealed class ActorState
    {
        internal Knight Owner;
        internal Archer Follower;
        internal Mover ObservedMover;
        internal float NextFollowerScan, NextAttack, RetryAt;
        internal int Failures;
        internal MotionLease Motion;
        // Stuck-pose probe (see the class comment): a cut lease arms it for good (HasCutHistory),
        // the captured hashes are what the pose check and the repair replay compare against, and
        // the ladder fields below track one stuck episode at a time.
        internal bool HasCutHistory, CaptureDead, HealGivenUp, LoggingHeal;
        internal float LastCutEndAt, StuckAt, NextHealLogAt, NextTurnLogAt;
        internal int SlashHash, DefaultHash;
        // SlashSource: 0=none, 1=Lease (settled during a cut -- proven), 2=Finish (inferred at the
        // cut's end -- a guess a peer's proven hash may override), 3=Adopt (peer's proven hash).
        internal int SlashSource;
        internal bool LeaseSawTransition;   // finish-capture trust gate: a Current read is only
                                            // meaningful after this lease observed a transition
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
        // Round trip phase state: where the lease is, when its current phase began (the turn
        // window is measured from here, not from the lease start), and whether Tick saw the
        // leash break (the coroutine turns on its own resume; if it is gone, Tick turns too).
        internal TripPhase Phase;
        internal float PhaseAt;
        internal bool PullPending;
        internal bool Retired, Effects, OldInvulnerable, OldTrail, HasGoal, Running;

        internal float GoalX, GoalSpeed, StartedAt, StartX, LastProgressAt, BestDistance, NextGoal;
        // The trail lifetime this lease pinned (see SamuraiTrailLifetime): restored by value.
        internal float OldTrailTime;
        // Defensive withdrawal facing: the mode we hold while returning, and whether the
        // mover field currently holds our value (so a foreign mode is never stomped).
        internal Mover.FacingMode FacingWritten;
        internal bool FacingOwned;
        // Per-lease hit bookkeeping: one hit per Damageable per dash, no per-frame allocations.
        internal readonly HashSet<IntPtr> HitObjects = new();
        internal readonly Collider2D[] Colliders = new Collider2D[16];
    }

    // Why a return lease ended: Success clears the failure ladder, Failure feeds the
    // escalating backoff, Handoff (night / actor lost / walk upgraded to a burst) keeps it.
    private enum EndReason { Handoff, Success, Failure }
    // The motions a lease can drive. RoundTrip is the atomic attack-family cut motion: outbound
    // dash, reverse cut and walk home in one lease. Return and Walk form the defensive withdrawal
    // family that owns the failure ladder.
    private enum MotionKind : byte { RoundTrip, Return, Walk }
    // The phases of MotionKind.RoundTrip: the outbound dash, the reverse cut, the walk home.
    private enum TripPhase : byte { Out, Turn, Home }
    private static bool IsReturnFamily(MotionLease m) =>
        m.Kind == MotionKind.Return || m.Kind == MotionKind.Walk;

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

    // Full restore: the visual token always ends here -- a RoundTrip keeps it across its own
    // phase boundaries but never past Finish -- and the combat flags follow the discipline above.
    private static void RestoreEffects(MotionLease m)
    {
        if (!Current(m)) return;
        SamuraiDashVisuals.End(m.Visual);
        RestoreCombatEffects(m);
    }

    // Round trip phase boundary: return the effects we applied to their owners, then re-snapshot the
    // now-unowned state and re-apply for the next phase. Restoring before snapshotting is what keeps
    // a fresh phase from capturing our own applied values as its restore target -- the chained
    // two-lease hand-off this refactor exists to remove.
    private static void RebaseCombatEffects(MotionLease m)
    {
        if (!Current(m) || !m.Effects) return;
        // 2026-09-25 用户裁定（全程无敌）后，本方法只剩"第三方中途改写 trail.time 的
        // 复位"职责：invulnerable 从 Begin 起连续持有到 Finish，无相位边界归还。
        if (m.Trail != null) { m.Trail.enabled = true; m.Trail.time = SamuraiTrailLifetime; }
        if (m.Damageable != null) m.Damageable.invulnerable = true;
        LogTrailState(m, "turn-effects-rebased");
    }

    private static void Finish(MotionLease m, EndReason reason = EndReason.Handoff)
    {
        if (!Current(m)) return;
        try
        {
            RestoreEffects(m);
            RestoreFacing(m);
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
                // Trigger hygiene the native path does not have: a cut that ends with its
                // PowerSlash trigger still unconsumed leaves the pose armed for the next state
                // entry. Clearing the flag never interrupts a state that is already playing --
                // that animation leaves on its own. A round trip keeps the same cut bookkeeping
                // the two-lease design ran at the attack lease's end.
                if (m.Kind == MotionKind.RoundTrip)
                {
                    // The turn transition anchors the attack cadence for every trip that gets
                    // there (a lease that lasts seconds must not push the next attack past its
                    // own cooldown). A trip that ends inside its outbound dash never reached
                    // that anchor -- and an attack that ends without one could dash again on the
                    // very next scan. Anchor it here, exactly like the retired attack lease did.
                    if (m.Phase == TripPhase.Out) m.Actor.NextAttack = Time.time + Cooldown;
                    m.Actor.LastCutEndAt = Time.time;   // arms the stuck-pose probe window
                    try
                    {
                        Knight k = m.Actor.Owner;
                        if (k._animator != null) k._animator.ResetTrigger(PowerSlash);
                    }
                    catch (Exception e) { Log("reset-trigger", e); }
                    // Stuck-pose capture net, finish leg: short-of-transition cuts and
                    // baseline-poisoned knights can never capture in-flight (2026-09-24
                    // recurrence); peers on the same shared controller may still know the pose.
                    try { CaptureSlashAtFinish(m.Actor, m.Actor.Owner); }
                    catch (Exception e) { Log("finish-capture", e); }
                }
                if (IsReturnFamily(m))
                {
                    if (reason == EndReason.Failure)
                    {
                        m.Actor.Failures++;
                        m.Actor.RetryAt = Time.time + BackoffSeconds(m.Actor.Failures);
                    }
                    else if (reason == EndReason.Success) { m.Actor.Failures = 0; m.Actor.RetryAt = 0; }
                }
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

    // Defensive withdrawal posture: keep facing the enemy side while withdrawing. Only ever
    // takes over from Ahead; a foreign fixed facing (Target / the other side) is left alone.
    private static void HoldFacing(MotionLease m)
    {
        var mover = m.Mover;
        if (mover == null) return;
        try
        {
            if (mover.facingMode == m.FacingWritten) { m.FacingOwned = true; return; }
            if (mover.facingMode != Mover.FacingMode.Ahead) { m.FacingOwned = false; return; }
            mover.SetFacingMode(m.FacingWritten, null);
            m.FacingOwned = true;
        }
        catch (Exception e) { Log("return-facing", e); }
    }

    // Release only while the mover still holds our value: a mode written by the native code
    // or a third party after us is never overwritten.
    private static void RestoreFacing(MotionLease m)
    {
        if (m == null || !m.FacingOwned) return;
        m.FacingOwned = false;
        var mover = m.Mover;
        if (mover == null) return;
        try { if (mover.facingMode == m.FacingWritten) mover.SetFacingMode(Mover.FacingMode.Ahead, null); }
        catch (Exception e) { Log("restore-facing", e); }
    }

    // Re-target an owned facing lease to the mode of the next phase: a mode still ours is swapped
    // directly; otherwise the new mode is only taken over from Ahead, never from a foreign one.
    // The turn's own SetDirection call (travel direction, after this) repairs the Y scale native
    // SetDirection wipes.
    private static void RetargetFacing(MotionLease m, Mover.FacingMode mode)
    {
        var mover = m.Mover;
        if (mover == null) return;
        try
        {
            if (m.FacingOwned)
            {
                if (mover.facingMode != m.FacingWritten) { m.FacingOwned = false; return; }
                mover.SetFacingMode(mode, null);
                m.FacingWritten = mode;
                return;
            }
            if (mover.facingMode != Mover.FacingMode.Ahead) return;
            mover.SetFacingMode(mode, null);
            m.FacingWritten = mode;
            m.FacingOwned = true;
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

    private static MotionLease Begin(ActorState a, MotionKind kind, float goal)
    {
        Knight k = a.Owner;
        var m = new MotionLease { Actor = a, Mover = k._mover, Damageable = k._damageable,
            Trail = k._trail, Kind = kind, StartedAt = Time.time,
            StartX = k.transform.position.x, LastProgressAt = Time.time,
            BestDistance = kind == MotionKind.Return ? Distance(a) : 0 };
        a.Motion = m;
        m.Diagnostics = SamuraiDashDiagnostics.Begin(k, IsReturnFamily(m), m.StartedAt);
        m.OldInvulnerable = m.Damageable.invulnerable;
        m.OldTrail = m.Trail != null && m.Trail.enabled;
        m.OldTrailTime = m.Trail != null ? m.Trail.time : 0f;
        m.Effects = true;
        m.Damageable.invulnerable = true;
        if (m.Trail != null) { m.Trail.enabled = true; m.Trail.time = SamuraiTrailLifetime; }
        LogTrailState(m, "trail-state");
        // A round trip opens with the attack dash: the slash pose fires, the native Ahead facing
        // stays untouched until the turn. The turn transition writes the reversed direction and
        // replays the trigger on this same lease. The return family is a defensive withdrawal:
        // it faces the enemy side instead of replaying the dash animation.
        // A new motion restarts the stuck-pose measurement; the round trip additionally arms the
        // probe, records the pose the trigger fires from and may release its give-up latch.
        ClearEpisode(a);
        if (kind == MotionKind.RoundTrip)
        {
            a.HasCutHistory = true;
            a.HealGivenUp = false;
            a.CaptureBaseline = PoseHash(k);        // read before the trigger moves the animator
            a.CaptureSeen = a.CaptureBaseline;
            a.CaptureStable = 0;
            a.CaptureFrames = 0;                    // the miss budget is counted per lease
            a.LeaseSawTransition = false;           // finish-capture trust gate, per lease
            if (k._animator != null) k._animator.SetTrigger(PowerSlash);
        }
        if (kind == MotionKind.Return) { m.FacingWritten = EnemyFacing(k); HoldFacing(m); }
        m.Visual = SamuraiDashVisuals.Begin(k, m.Diagnostics);
        Goal(m, goal, DashSpeed);
        return m;
    }

    // Diagnostic reads are admitted per motion; failures never interrupt gameplay or cleanup.
    private static void LogTrailState(MotionLease m, string eventName)
    {
        if (m.Diagnostics == null) return;
        try
        {
            var trail = m.Trail;
            string details = "elapsed=" + (Time.time - m.StartedAt).ToString("0.###") + " present=" + (trail != null);
            if (trail != null)
                details += " active=" + trail.gameObject.activeInHierarchy + " enabled=" + trail.enabled
                    + " emitting=" + trail.emitting + " points=" + trail.positionCount
                    + " lifetime=" + trail.time + " width=" + trail.widthMultiplier
                    + " layer=" + trail.sortingLayerID + " order=" + trail.sortingOrder;
            SamuraiDashDiagnostics.Write(m.Diagnostics, eventName, details);
        }
        catch (Exception e) { SamuraiDashDiagnostics.Write(m.Diagnostics, eventName, "state-read-failed=" + e.GetType().Name); }
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
        // 2026-09-25 用户裁定（目标死也要回家）：随从仍在=随从站位（原语义）；随从已死/
        // 失效=夜间列队槽位或原生守位锚——绝不因无随从而失去回家目标。
        float target = a.Follower != null && a.Follower.gameObject != null
            ? a.Follower.transform.position.x
            : PatchRoles_SamuraiNightFormation.HomeXOf(a.Owner);
        return target - Mathf.Sign(target - x) * 2.5f;
    }

    // The round trip's live home goal: the follower's station as it is right now, clamped to the
    // dash cap so a runaway squad never turns the walk home into an unbounded trip. Both moving
    // phases refresh it periodically -- a frozen origin breaks the moment the squad moves.
    private static float HomeTarget(MotionLease m)
    {
        float x = m.Actor.Owner.transform.position.x;
        return x + Mathf.Clamp(StationX(m.Actor) - x, -MaxRange, MaxRange);
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
    /// Captures the slash pose from the live animator during a cut lease: only a state the
    /// animator settled on counts -- never a transition source, never the begin-frame baseline
    /// and never a single-frame flicker. One captured hash lasts for the whole actor state; a
    /// later lease that cannot re-observe it leaves it alone, and only a knight that never
    /// captured a pose can be stood down by a whole lease window of misses.
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
            // Trust gate (2026-09-24 recurrence): a settled pose this lease never entered through
            // a transition is untrustworthy -- it kills the whole teleport/no-transition-entry
            // false-capture class (an aborted cut drifting into a walk pose mid-lease).
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
    /// only when this lease actually observed a transition (an untransitioned Current is
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
                if (a.Motion != null) Finish(a.Motion);
                a.ObservedMover = knight._mover;
                a.Failures = 0; a.RetryAt = 0; // a replacement mover starts the ladder clean
                return; // A replacement mover's first observed goal belongs to its new owner.
            }
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
                if (!ValidMotion(m)) { Finish(m, IsReturnFamily(m) ? EndReason.Failure : EndReason.Handoff); return; }
                if (m.Kind == MotionKind.RoundTrip)
                {
                    if (m.Phase == TripPhase.Out)
                    {
                        // The coroutine owns the outbound dash; this tick feeds it the pose
                        // measurement and the leash pull. A pull still pending on a later frame
                        // means the coroutine never resumed, so the turn happens here instead.
                        TryCaptureSlashPose(knight, a);
                        if (ValidFollower(knight, a.Follower) && Distance(a) > AttackLeash)
                        {
                            if (m.PullPending) TurnTransition(m);
                            else m.PullPending = true;
                        }
                        else if (m.PullPending) m.PullPending = false;
                    }
                    else if (m.Phase == TripPhase.Turn) { TryCaptureSlashPose(knight, a); AdvanceRoundTrip(m); }
                    else AdvanceRoundTrip(m);
                    return;
                }
                // The withdrawal family steps aside for the native night goal when dusk arrives.
                if (NightGuard(knight)) { Finish(m); return; }
                // Verify this reference each frame before periodic reselection, never mask its loss.
                if (!ValidFollower(knight, a.Follower)) { Finish(m); a.Follower = null; return; }
                RefreshFollower(a);
                AdvanceReturn(m);
                return; // RoundTrip, Return and Walk are mutually exclusive, including the 4..10 band.
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
            // An attack-initiated withdrawal is owned by its RoundTrip lease from the first
            // frame -- this ladder only serves followers that walked away with no cut running.
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
                var dash = Begin(a, MotionKind.Return, x + Mathf.Clamp(StationX(a) - x, -MaxRange, MaxRange));
                HitScan(dash); // entry frame hits, same as the attack dash's first coroutine step
                if (Current(dash) && (!ValidMotion(dash) || !ValidFollower(knight, a.Follower)))
                    Finish(dash, EndReason.Failure);
                return;
            }
            if (Time.timeScale <= 0 || knight._mover._pauseTimeout > 0 || Time.time < a.NextAttack) return;
            a.NextAttack = Time.time + ScanInterval;
            GameObject target = ScanClosestEnemy(knight);
            if (target == null) return;
            Damageable enemy = target.GetComponent<Damageable>();
            if (enemy == null || !enemy.IsDamagedBy(DamageSource.Knight)) return;
            float dx = target.transform.position.x - knight.transform.position.x;
            if (Mathf.Abs(dx) < 1.5f || Mathf.Abs(dx) > MaxRange) return;
            // 混合固定距离（2026-09-24）：近敌固定 7 格穿过（HitScan 沿路径扫+逐目标去重本就
            // 支持穿透命中），远敌(|dx|>7.5)仍停敌前 0.5——8.2-10.5 零伤害死区由此消灭。
            // 同日用户裁定：冲刺不考虑墙（墙防时武士照旧冲出墙外再回撤，batch2 的既有行为）。
            var attack = Begin(a, MotionKind.RoundTrip,
                knight.transform.position.x + Mathf.Sign(dx) * Mathf.Max(Mathf.Abs(dx) - .5f, SamuraiFixedDashDistance));
            knight.StartCoroutine(RoundTripRoutine(attack).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            if (a?.Motion != null) Finish(a.Motion, IsReturnFamily(a.Motion) ? EndReason.Failure : EndReason.Handoff);
            Log("tick", e);
        }
    }

    // Advance of the moving phases of a round trip (the outbound dash is driven by its coroutine).
    // Turn: the reverse cut, bounded by its own dash window, its arrival tolerance and the
    // no-progress watchdog. Home: a plain run-speed walk, bounded only by arrival and the whole
    // lease cap. Only real ownership loss (goal stolen, follower gone, mover replaced) finishes
    // the lease from here -- the ladder stays untouched.
    private static void AdvanceRoundTrip(MotionLease m)
    {
        if (!ValidMotion(m)) { Finish(m); return; }
        var a = m.Actor;
        Knight k = a.Owner;
        // 2026-09-25 用户裁定：目标死/失效不终止回程——回家目标由 StationX 的无随从
        // 分支兜底；仅清引用让后续 RefreshFollower 有机会换新目标。
        if (!ValidFollower(k, a.Follower)) a.Follower = null;
        float now = Time.time;
        if (now - m.StartedAt >= RoundTripLease) { Finish(m); return; }
        if (m.Phase == TripPhase.Turn)
        {
            if (Time.timeScale > 0 && now - m.PhaseAt < DashTimeout &&
                Mathf.Abs(k.transform.position.x - m.StartX) < MaxRange)
                HitScan(m);
            // A hit callback may synchronously disable the knight or replace this lease; never touch the new one.
            if (!Current(m)) return;
            if (!ValidMotion(m)) { Finish(m); return; }
            if (!ValidFollower(k, a.Follower)) a.Follower = null;
            float x = k.transform.position.x, distance = Distance(a);
            if (distance < m.BestDistance - .1f) { m.BestDistance = distance; m.LastProgressAt = now; }
            // Arrival, the dash window, the watchdog or the travel cap: the cut is spent and the
            // same lease degrades into the walk home. Nothing here finishes the lease.
            if (Mathf.Abs(x - m.GoalX) <= TurnArrive || now - m.PhaseAt >= DashTimeout ||
                now - m.LastProgressAt >= RoundTripStall || Mathf.Abs(x - m.StartX) >= MaxRange - .05f)
            { DegradeHome(m); return; }
            if (Time.timeScale <= 0) return;
            if (now >= m.NextGoal)
            {
                m.NextGoal = now + ScanInterval;
                float target = HomeTarget(m);
                if (Mathf.Abs(target - m.GoalX) > .25f) Goal(m, target, DashSpeed);
            }
            return;
        }
        // Home: no effects, no hit scan -- the station zone or the lease cap ends the trip.
        // The defensive facing is re-asserted every frame, exactly like the ladder's running leg.
        HoldFacing(m);
        if (Distance(a) <= ReturnStop) { Finish(m, EndReason.Success); return; }
        if (Time.timeScale <= 0) return;
        if (now >= m.NextGoal)
        {
            m.NextGoal = now + ScanInterval;
            float target = HomeTarget(m);
            if (Mathf.Abs(target - m.GoalX) > .25f) Goal(m, target, a.Owner._runSpeed);
        }
    }

    // The turn transition, run in the coroutine's own exit path (same frame, no Finish): the one
    // phase boundary the spec binds. HitObjects is cleared so out and turn may each strike the
    // same enemy once; the attack cadence anchor moves here so a lease that lasts seconds never
    // pushes the next attack past its own cooldown; the trigger is reset and re-fired for the
    // reverse cut; the capture probe gets a fresh cut budget; the effects are re-based on the
    // state the out phase restored; and the knight turns toward the way home at dash speed.
    private static bool TurnTransition(MotionLease m)
    {
        if (!Current(m) || m.Phase != TripPhase.Out) return Current(m);
        var a = m.Actor;
        Knight k = a.Owner;
        m.Phase = TripPhase.Turn;
        m.PhaseAt = Time.time;
        m.LastProgressAt = Time.time;
        m.BestDistance = Distance(a);
        m.HitObjects.Clear();
        a.NextAttack = Time.time + Cooldown;
        a.HealGivenUp = false;
        a.CaptureBaseline = PoseHash(k);            // read before the trigger moves the animator
        a.CaptureSeen = a.CaptureBaseline;
        a.CaptureStable = 0;
        a.CaptureFrames = 0;                        // the reverse cut gets its own miss budget
        a.LeaseSawTransition = false;               // and its own transition trust window
        try
        {
            if (k._animator != null)
            {
                k._animator.ResetTrigger(PowerSlash);   // the finished cut's flag may still be armed
                k._animator.SetTrigger(PowerSlash);     // and the reverse cut replays the pose
            }
        }
        catch (Exception e) { Log("turn-trigger", e); }
        RebaseCombatEffects(m);
        float target = HomeTarget(m);
        int direction = target < k.transform.position.x ? -1 : 1;
        RetargetFacing(m, direction < 0 ? Mover.FacingMode.Left : Mover.FacingMode.Right);
        try
        {
            var scale = k.transform.localScale;
            m.Mover.SetDirection(direction);
            k.transform.localScale = new Vector3(direction * Mathf.Abs(scale.x), scale.y, scale.z);
        }
        catch (Exception e) { Log("turn-facing", e); }
        Goal(m, target, DashSpeed);
        m.NextGoal = Time.time + ScanInterval;
        SamuraiDashDiagnostics.Write(m.Diagnostics, "turn",
            "goal=" + target.ToString("0.##") + " facing=" + m.FacingWritten);
        TurnLog(a, k, target);
        HitScan(m);                                 // entry frame hits, same as every other dash
        return Current(m);
    }

    // The reverse cut is spent: vulnerable enemy-facing walk home. The visual token deliberately
    // stays for the whole lease; only the combat effects and the trail pin retire here.
    private static void DegradeHome(MotionLease m)
    {
        if (!Current(m)) return;
        m.Phase = TripPhase.Home;
        m.Running = true;               // CanHit false: the walk home never damages
        // 2026-09-25 用户裁定：整段往返=一次受保护的完整动作——冲出/反斩/回家全程
        // 无敌不中断（此前此处的 RestoreCombatEffects 会让回家半程可被击杀，即"武士
        // 死在回家路上"的实机根因）。效果只在本租约 Finish 时统一归还。
        RetargetFacing(m, EnemyFacing(m.Actor.Owner));
        Goal(m, HomeTarget(m), m.Actor.Owner._runSpeed);
        m.NextGoal = Time.time + ScanInterval;
        LogTrailState(m, "roundtrip-home");
    }

    private static void AdvanceReturn(MotionLease m)
    {
        if (!ValidMotion(m)) { Finish(m, EndReason.Failure); return; }
        var a = m.Actor;
        if (!ValidFollower(a.Owner, a.Follower)) { Finish(m, EndReason.Handoff); return; }
        if (m.Kind == MotionKind.Walk) { HoldFacing(m); AdvanceWalk(m); return; }

        HoldFacing(m);
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
        HoldFacing(m);
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
        // The outbound dash may also strike without a follower (the old attack rule); the reverse
        // cut is follower-guarded like the swallow it replaces; the walk home (Running) never damages.
        if (m.Kind == MotionKind.RoundTrip)
        {
            if (m.Phase == TripPhase.Out)
                return Time.time - m.StartedAt < DashTimeout &&
                    (!ValidFollower(a.Owner, a.Follower) || Distance(a) <= AttackLeash);
            if (m.Phase != TripPhase.Turn) return false;
            return Time.time - m.PhaseAt < DashTimeout &&
                ValidFollower(a.Owner, a.Follower) && Distance(a) <= AttackLeash;
        }
        return Time.time - m.StartedAt < DashTimeout && ValidFollower(a.Owner, a.Follower);
    }

    /// <summary>Shared burst hit scan used by the outbound dash, the reverse cut and the return dash.</summary>
    private static void HitScan(MotionLease m)
    {
        if (!CanHit(m)) return;
        if (HitLayerMask == 0) HitLayerMask = LayerMask.GetMask("Enemies", "Wildlife");
        var a = m.Actor;
        int count = Physics2D.OverlapCircleNonAlloc(a.Owner.transform.position, 1.2f, m.Colliders, HitLayerMask);
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

    // The outbound dash of a round trip. Every exit that still owns the mover -- goal reached and
    // stood at, the dash window, the travel cap, a broken leash -- turns the same lease around on
    // this very frame; only real ownership loss finishes here. The turn deliberately happens in
    // this exit path (not in Tick) so no natural completion ever passes through a hand-off seam.
    private static IEnumerator RoundTripRoutine(MotionLease m)
    {
        bool turning = false;
        try
        {
            while (ValidMotion(m) && Time.time - m.StartedAt < DashTimeout &&
                Mathf.Abs(m.Actor.Owner.transform.position.x - m.StartX) < MaxRange)
            {
                var a = m.Actor;
                TryCaptureSlashPose(a.Owner, a);        // the coroutine's own resume point
                if (m.PullPending || (ValidFollower(a.Owner, a.Follower) && Distance(a) > AttackLeash)) break;
                if (Time.timeScale > 0)
                {
                    RefreshFollower(a);
                    if (ValidFollower(a.Owner, a.Follower) && Distance(a) > AttackLeash) break;
                    HitScan(m);
                }
                yield return null;
            }
            // Every exit that still owns the mover and a follower -- goal reached and stood at,
            // the dash window, the travel cap, a broken leash -- turns the same lease around on
            // this very frame. Only real ownership loss finishes here; once the phase has moved,
            // the moving phases own the lease (a stopped coroutine never gets to finish it).
            if (Current(m) && ValidMotion(m) && m.Phase == TripPhase.Out)
                turning = TurnTransition(m);   // 2026-09-25: the trip always turns -- home is the station, not the follower
        }
        finally { if (!turning && m.Phase == TripPhase.Out) Finish(m); }
    }

    internal static bool IsReturning(Knight knight)
    {
        try
        {
            if (knight == null || knight.gameObject == null) return false;
            if (!Actors.TryGetValue(knight.gameObject.GetInstanceID(), out var a) || !Same(a.Owner, knight)) return false;
            var m = a.Motion;
            // Only the invulnerable ordinary return burst suppresses the native slash; a
            // walking withdrawal is a plain retreat and the round trip is an attack-family
            // motion, so both leave the samurai able to defend itself.
            return m != null && m.Kind == MotionKind.Return && ValidMotion(m) &&
                ValidFollower(knight, a.Follower) && Distance(a) > ReturnStop;
        }
        catch (Exception e) { Log("should-slash", e); return false; }
    }

    /// <summary>
    /// True while this knight's actor entry owns any live motion lease (round trip / return /
    /// walk). The night wall formation redirect (PatchRoles_SamuraiNightFormation) reads it
    /// to leave lease-owned goals untouched: the lease identifies its goal by value (OwnGoal),
    /// so a foreign rewrite would retire the motion mid-flight. Exception -> true: without
    /// proof that no lease exists we never interleave.
    /// </summary>
    internal static bool HasActiveMotion(Knight knight)
    {
        try
        {
            if (knight == null || knight.gameObject == null) return false;
            return Actors.TryGetValue(knight.gameObject.GetInstanceID(), out ActorState a) &&
                Same(a.Owner, knight) && a.Motion != null;
        }
        catch (Exception e) { Log("has-motion", e); return true; }
    }

    internal static void OnKnightDisabled(Knight knight)
    {
        if (knight == null || knight.gameObject == null) return;
        int id = knight.gameObject.GetInstanceID();
        if (!Actors.TryGetValue(id, out var a) || !Same(a.Owner, knight)) return;
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
        if (!PatchRoles_SamuraiPowerDash.IsReturning(__instance)) return true;
        __result = false;
        return false;
    }
}
