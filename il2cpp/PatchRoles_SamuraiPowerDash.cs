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
    // is exactly the furthest the follower can be while a dash or swallow that started legally
    // is still running, so a dash never cuts itself off on its own start frame. The return
    // family keeps FollowLeash: that is the walk-home trigger and must not move with the cap.
    private const float AttackLeash = MaxRange + 2.5f + .5f;
    private const float DashSpeed = 18f, DashTimeout = .6f, FollowLeash = 10f, ReturnStop = 4f;
    // Swallow return (燕返): after an attack dash that ran its full course the samurai may
    // cut straight back to that dash's origin. One 30% roll per qualified completion, a
    // per-knight 6 s cooldown counted from the actual start, and the ordinary return's
    // failure ladder is never touched by this motion.
    private const float SwallowChance = .30f, SwallowCooldown = 6f, SwallowMinTravel = 1.5f, SwallowArrive = .25f;

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
    private static readonly int SpeedParam = Animator.StringToHash("Speed");
    private static readonly Dictionary<int, ActorState> Actors = new();
    private static readonly int PowerSlash = Animator.StringToHash("PowerSlash");
    private static readonly HashSet<string> Logged = new();
    private static int HitLayerMask, EnemyScanLayer, WalkLogs, HealLogs;

    private sealed class ActorState
    {
        internal Knight Owner;
        internal Archer Follower;
        internal Mover ObservedMover;
        internal float NextFollowerScan, NextAttack, RetryAt;
        internal int Failures;
        internal MotionLease Motion;
        // Immediate withdrawal: a cut end raises it, the very next legal tick consumes it
        // (lease or in-place face), and it is never deferred through a pause nor cleared by
        // the same Finish that raised it.
        internal bool ReturnDue;
        // Swallow-return bookkeeping: the roll is pending for exactly the frame after a
        // naturally completed attack dash; the cooldown starts only at a real start.
        internal bool PendingSwallow;
        internal float SwallowOriginX, NextSwallowAt;
        internal int SwallowFrame = -1;
        // Stuck-pose probe (see the class comment): a cut lease arms it for good (HasCutHistory),
        // the captured hashes are what the pose check and the repair replay compare against, and
        // the ladder fields below track one stuck episode at a time.
        internal bool HasCutHistory, CaptureDead, HealGivenUp, LoggingHeal;
        internal float LastCutEndAt, StuckAt, NextHealLogAt;
        internal int SlashHash, DefaultHash;
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
    // The motions a lease can drive. Attack and Swallow are cut motions (PowerSlash pose);
    // the reverse cut explicitly turns toward its origin. Return and Walk form the
    // defensive withdrawal family that owns the failure ladder.
    private enum MotionKind : byte { Attack, Return, Walk, Swallow }
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

    private static float Distance(ActorState a) => Mathf.Abs(a.Owner.transform.position.x - a.Follower.transform.position.x);

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
    // from this static scan's default (the native instance scanner was constructed with it, the
    // native static scans leave it off): no dashing at corpses. Enemies only, never Wildlife --
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

    private static void RestoreEffects(MotionLease m)
    {
        if (!Current(m) || !m.Effects) return;
        m.Effects = false;
        SamuraiDashVisuals.End(m.Visual);
        // A bool cannot identify an external true -> true rewrite. Restore only our still-present values.
        if (m.Damageable != null && m.Damageable.invulnerable) m.Damageable.invulnerable = m.OldInvulnerable;
        if (m.Trail != null && m.Trail.enabled) m.Trail.enabled = m.OldTrail;
        LogTrailState(m, "effects-restored");
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
                // that animation leaves on its own.
                if (m.Kind == MotionKind.Attack || m.Kind == MotionKind.Swallow)
                {
                    m.Actor.LastCutEndAt = Time.time;   // arms the stuck-pose probe window
                    // Every cut owes the follower a withdrawal the moment the next frame is
                    // free to move -- success, handoff and failure alike.
                    m.Actor.ReturnDue = true;
                    try
                    {
                        Knight k = m.Actor.Owner;
                        if (k._animator != null) k._animator.ResetTrigger(PowerSlash);
                    }
                    catch (Exception e) { Log("reset-trigger", e); }
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
                else if (m.Kind == MotionKind.Attack) m.Actor.NextAttack = Time.time + Cooldown;
                // Swallow: neither the return ladder nor the attack cooldown -- the
                // forward dash that earned it already set NextAttack.
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

    // The in-place defensive face the immediate withdrawal owes when its cut already ended next
    // to the follower: turn toward the enemy half by the same rule the return family uses. The
    // native SetDirection wipes the cosmetic Y scale, so put it back immediately (the swallow
    // start's own repair), and write at all only when the knight is not already facing that way.
    private static void FaceEnemySide(Knight k)
    {
        try
        {
            var mover = k._mover;
            if (mover == null) return;
            int direction = k.side == Side.Left ? -1 : 1;
            var scale = k.transform.localScale;
            if ((scale.x < 0 ? -1 : 1) == direction) return;
            mover.SetDirection(direction);
            k.transform.localScale = new Vector3(direction * Mathf.Abs(scale.x), scale.y, scale.z);
        }
        catch (Exception e) { Log("return-face", e); }
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
        m.Effects = true;
        m.Damageable.invulnerable = true;
        if (m.Trail != null) m.Trail.enabled = true;
        LogTrailState(m, "trail-state");
        // The attack dash and the swallow keep the slash pose. The reverse cut must face
        // its origin before the first slash frame; native SetDirection also resets Y scale,
        // so put the cosmetic scale back immediately. Only an Ahead-facing mover is leased.
        // The return family is a defensive withdrawal: it faces the enemy side instead of
        // replaying the dash animation.
        if (kind == MotionKind.Swallow)
        {
            int direction = goal < m.StartX ? -1 : 1;
            m.FacingWritten = direction < 0 ? Mover.FacingMode.Left : Mover.FacingMode.Right;
            var scale = k.transform.localScale;
            m.Mover.SetFacingMode(m.FacingWritten, null);
            m.FacingOwned = true;
            m.Mover.SetDirection(direction);
            k.transform.localScale = new Vector3(direction * Mathf.Abs(scale.x), scale.y, scale.z);
        }
        // A new motion restarts the stuck-pose measurement; the cut motions additionally arm the
        // probe, record the pose the trigger fires from and may release its give-up latch.
        ClearEpisode(a);
        if (kind == MotionKind.Attack || kind == MotionKind.Swallow)
        {
            a.HasCutHistory = true;
            a.HealGivenUp = false;
            a.CaptureBaseline = PoseHash(k);        // read before the trigger moves the animator
            a.CaptureSeen = a.CaptureBaseline;
            a.CaptureStable = 0;
            a.CaptureFrames = 0;                    // the miss budget is counted per lease
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
        float x = a.Owner.transform.position.x, target = a.Follower.transform.position.x;
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
            if (animator == null || animator.IsInTransition(0))
            {
                a.CaptureStable = 0;
                CountCaptureMiss(a);
                return;
            }
            int hash = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            if (hash == 0 || hash == a.CaptureBaseline)
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
            if (a.DefaultHash == hash) a.DefaultHash = 0;   // the slash pose is never a calm state
            if (Logged.Add("slash-capture"))
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/slash-capture] hash=" + hash
                    + " baseline=" + a.CaptureBaseline + " frames=" + a.CaptureFrames);
        }
        catch (Exception e) { Log("slash-capture", e); }
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
    /// The calm state the repair replays: captured once per actor state on a settled, idle frame
    /// (no lease, no transition, Speed at rest, no native slash pause) whose hash holds for two
    /// frames and differs from the captured slash pose. Failure is silent; the next calm frame
    /// retries.
    /// </summary>
    private static void TryCaptureDefaultPose(Knight k, ActorState a)
    {
        if (a.DefaultHash != 0) return;
        if (Time.frameCount == a.DefaultFrame) return;
        a.DefaultFrame = Time.frameCount;
        try
        {
            Animator animator = ReadAnimator(k);
            if (animator == null || animator.IsInTransition(0) ||
                Mathf.Abs(animator.GetFloat(SpeedParam)) >= .01f ||
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
            if (Logged.Add("default-capture"))
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/default-capture] hash=" + hash);
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
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/heal] step=" + step +
                " order=" + order + " retries=" + a.HealRetries + " slashHash=" + a.SlashHash +
                " defaultHash=" + a.DefaultHash + details);
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
                a.PendingSwallow = false; // and never inherits a swallow intent
                a.ReturnDue = false;      // nor a withdrawal debt the old mover's cut left behind
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
                // Another motion owns the roll frame: the swallow yields for good, no re-roll.
                if (a.PendingSwallow && Time.frameCount > a.SwallowFrame) a.PendingSwallow = false;
                MotionLease m = a.Motion;
                if (!ValidMotion(m)) { Finish(m, IsReturnFamily(m) ? EndReason.Failure : EndReason.Handoff); return; }
                if (m.Kind == MotionKind.Attack)
                {
                    TryCaptureSlashPose(knight, a);
                    if (ValidFollower(knight, a.Follower) && Distance(a) > AttackLeash) Finish(m);
                }
                else if (m.Kind == MotionKind.Swallow)
                {
                    TryCaptureSlashPose(knight, a);
                    // Night wall guard and the ordinary return always have priority.
                    if (NightGuard(knight)) { Finish(m); return; }
                    if (ValidFollower(knight, a.Follower) &&
                        (Distance(a) > AttackLeash || (a.Failures > 0 && Distance(a) > ReturnStop))) Finish(m);
                    else AdvanceSwallow(m);
                }
                else
                {
                    if (NightGuard(knight)) { Finish(m); return; }
                    // Verify this reference each frame before periodic reselection, never mask its loss.
                    if (!ValidFollower(knight, a.Follower)) { Finish(m); a.Follower = null; return; }
                    RefreshFollower(a);
                    AdvanceReturn(m);
                }
                return; // Attack, Return and Swallow are mutually exclusive, including the 4..10 band.
            }
            TryCaptureDefaultPose(knight, a);
            ProbeStuckPose(knight, a);
            RefreshFollower(a);
            bool follower = ValidFollower(knight, a.Follower);
            if (!follower) { a.Follower = null; a.Failures = 0; a.RetryAt = 0; }
            float distance = follower ? Distance(a) : 0;
            if (distance <= ReturnStop) { a.Failures = 0; a.RetryAt = 0; }
            // The swallow roll: consumed exactly once, on the frame after a naturally
            // completed attack dash, whatever the outcome.
            if (a.PendingSwallow && TryStartSwallow(a, follower, distance)) return;
            // At night the wall coroutine supplies its current destination and defensive
            // facing. Keep the follower leash, but never turn this homeward leg into an attack;
            // the immediate withdrawal is a daytime posture, so dusk drops the debt instead of
            // letting a daylight cut drag the knight off the wall later.
            if (NightGuard(knight))
            {
                a.Failures = 0; a.RetryAt = 0;
                a.ReturnDue = false;
                if (distance > FollowLeash) return;
            }
            // A cut just ended: consume its withdrawal on this very frame. The debt only ever
            // spends itself while nothing else owns the mover goal -- a goal we did not issue
            // always wins, and the debt is dropped rather than retried over it.
            bool due = a.ReturnDue && distance > ReturnStop;
            if (due && knight._mover.goalMode != Mover.GoalMode.Off) { a.ReturnDue = false; due = false; }
            if (distance > FollowLeash || ((a.Failures > 0 || due) && distance > ReturnStop))
            {
                if (Time.timeScale <= 0 || knight._mover._pauseTimeout > 0) return;
                if (due) a.ReturnDue = false;   // consumed: the ladder below owns the way back
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
            // The cut already ended next to the follower: all the withdrawal still owes is the
            // defensive face, written in place, once, and only while the mover is free.
            if (a.ReturnDue)
            {
                a.ReturnDue = false;
                if (knight._mover.goalMode == Mover.GoalMode.Off) FaceEnemySide(knight);
            }
            if (Time.timeScale <= 0 || knight._mover._pauseTimeout > 0 || Time.time < a.NextAttack) return;
            a.NextAttack = Time.time + ScanInterval;
            GameObject target = ScanClosestEnemy(knight);
            if (target == null) return;
            Damageable enemy = target.GetComponent<Damageable>();
            if (enemy == null || !enemy.IsDamagedBy(DamageSource.Knight)) return;
            float dx = target.transform.position.x - knight.transform.position.x;
            if (Mathf.Abs(dx) < 1.5f || Mathf.Abs(dx) > MaxRange) return;
            var attack = Begin(a, MotionKind.Attack, target.transform.position.x - Mathf.Sign(dx) * .5f);
            knight.StartCoroutine(AttackRoutine(attack).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            if (a?.Motion != null) Finish(a.Motion, IsReturnFamily(a.Motion) ? EndReason.Failure : EndReason.Handoff);
            Log("tick", e);
        }
    }

    // The swallow roll. Runs at most once per qualified attack completion: all deterministic
    // gates are checked first and only a fully startable completion draws the 30% chance.
    // Whatever the outcome the pending flag is consumed -- nothing is re-rolled later and
    // nothing is left pending. The cooldown is charged only when a swallow really starts.
    private static bool TryStartSwallow(ActorState a, bool follower, float distance)
    {
        if (Time.frameCount <= a.SwallowFrame) return false; // only ever from the next frame on
        bool fresh = Time.frameCount <= a.SwallowFrame + 1; // a stalled frame never starts late
        bool returnDue = distance > AttackLeash || (a.Failures > 0 && distance > ReturnStop);
        Knight k = a.Owner;
        float travel = Mathf.Abs(k.transform.position.x - a.SwallowOriginX);
        bool start = fresh && follower && !returnDue && !NightGuard(k) && Time.timeScale > 0 &&
            k._mover._pauseTimeout <= 0 && k._mover.goalMode == Mover.GoalMode.Off &&
            k._mover.facingMode == Mover.FacingMode.Ahead &&
            Time.time >= a.NextSwallowAt && travel >= SwallowMinTravel && travel <= MaxRange &&
            UnityEngine.Random.value < SwallowChance; // the host decides; Eligible gated authority
        a.PendingSwallow = false;
        if (!start) return false;
        float x = k.transform.position.x;
        var swallow = Begin(a, MotionKind.Swallow, x + Mathf.Clamp(a.SwallowOriginX - x, -MaxRange, MaxRange));
        a.NextSwallowAt = Time.time + SwallowCooldown; // counted from the actual start only
        SamuraiDashDiagnostics.Write(swallow.Diagnostics, "swallow",
            "origin=" + a.SwallowOriginX.ToString("0.##") + " facing=" + swallow.FacingWritten);
        if (Logged.Add("swallow-start"))
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/swallow-start] x=" +
                x.ToString("0.##") + " origin=" + a.SwallowOriginX.ToString("0.##") +
                " chance=" + SwallowChance + " cooldown=" + SwallowCooldown);
        HitScan(swallow); // entry frame hits, same as every other dash's first step
        if (Current(swallow) && (!ValidMotion(swallow) || !ValidFollower(k, a.Follower)))
            Finish(swallow); // Handoff: no ladder and no attack-cooldown impact
        return true;
    }

    // One bounded reverse cut back to the attack dash's origin. It ends on the arrival
    // tolerance, its own timeout or travel cap; it never extends into the ordinary running
    // return and never touches the failure ladder.
    private static void AdvanceSwallow(MotionLease m)
    {
        if (!ValidMotion(m)) { Finish(m); return; }
        var a = m.Actor;
        Knight k = a.Owner;
        if (!ValidFollower(k, a.Follower)) { Finish(m); a.Follower = null; return; }
        if (Time.timeScale > 0 && Time.time - m.StartedAt < DashTimeout &&
            Mathf.Abs(k.transform.position.x - m.StartX) < MaxRange)
            HitScan(m);
        // A hit callback may synchronously disable the knight or replace this lease; never touch the new one.
        if (!Current(m)) return;
        if (!ValidMotion(m)) { Finish(m); return; }
        if (!ValidFollower(k, a.Follower)) { Finish(m); a.Follower = null; return; }
        float x = k.transform.position.x, now = Time.time;
        if (Mathf.Abs(x - m.GoalX) <= SwallowArrive) { Finish(m); return; }
        if (now - m.StartedAt >= DashTimeout || Mathf.Abs(x - m.StartX) >= MaxRange - .05f) { Finish(m); return; }
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

    private static bool CanHit(MotionLease m) => !m.Running && Time.timeScale > 0 &&
        ValidMotion(m) && Time.time - m.StartedAt < DashTimeout &&
        Mathf.Abs(m.Actor.Owner.transform.position.x - m.StartX) < MaxRange &&
        (m.Kind == MotionKind.Attack
            ? !ValidFollower(m.Actor.Owner, m.Actor.Follower) || Distance(m.Actor) <= AttackLeash
            : ValidFollower(m.Actor.Owner, m.Actor.Follower) &&
              (m.Kind != MotionKind.Swallow || Distance(m.Actor) <= AttackLeash));

    /// <summary>Shared burst hit scan used by the attack dash, the return dash and the swallow.</summary>
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

    private static IEnumerator AttackRoutine(MotionLease m)
    {
        try
        {
            bool pulled = false;
            while (ValidMotion(m) && Time.time - m.StartedAt < DashTimeout &&
                Mathf.Abs(m.Actor.Owner.transform.position.x - m.StartX) < MaxRange)
            {
                var a = m.Actor;
                TryCaptureSlashPose(a.Owner, a);        // the coroutine's own resume point
                if (ValidFollower(a.Owner, a.Follower) && Distance(a) > AttackLeash) { pulled = true; break; }
                if (Time.timeScale > 0)
                {
                    RefreshFollower(a);
                    if (ValidFollower(a.Owner, a.Follower) && Distance(a) > AttackLeash) { pulled = true; break; }
                    HitScan(m);
                }
                yield return null;
            }
            // Only the dash's own exit paths (goal reached and stood at, timeout, max travel)
            // qualify for the swallow roll -- never the finally cleanup below, never a leash
            // pull, never an exception, and a stopped coroutine never gets here at all.
            if (!pulled && ValidMotion(m) && Current(m))
            {
                m.Actor.PendingSwallow = true;
                m.Actor.SwallowFrame = Time.frameCount;
                m.Actor.SwallowOriginX = m.StartX;
            }
        }
        finally { Finish(m); }
    }

    internal static bool IsReturning(Knight knight)
    {
        try
        {
            if (knight == null || knight.gameObject == null) return false;
            if (!Actors.TryGetValue(knight.gameObject.GetInstanceID(), out var a) || !Same(a.Owner, knight)) return false;
            var m = a.Motion;
            // Only the invulnerable ordinary return burst suppresses the native slash; a
            // walking withdrawal is a plain retreat and the swallow is an attack-family
            // cut, so both leave the samurai able to defend itself.
            return m != null && m.Kind == MotionKind.Return && ValidMotion(m) &&
                ValidFollower(knight, a.Follower) && Distance(a) > ReturnStop;
        }
        catch (Exception e) { Log("should-slash", e); return false; }
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
