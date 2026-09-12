using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>One motion lease per knight. A retired lease can never write again.</summary>
internal static class PatchRoles_SamuraiPowerDash
{
    private const float ScanInterval = .2f, Cooldown = 3f, MaxRange = 7f;
    private const float DashSpeed = 18f, DashTimeout = .6f, FollowLeash = 10f, ReturnStop = 4f;
    private static readonly Dictionary<int, ActorState> Actors = new();
    private static readonly int PowerSlash = Animator.StringToHash("PowerSlash");
    private static readonly HashSet<string> Logged = new();
    private static int HitLayerMask;

    private sealed class ActorState
    {
        internal Knight Owner;
        internal Archer Follower;
        internal Mover ObservedMover;
        internal float NextFollowerScan, NextAttack, RetryAt;
        internal int Failures;
        internal MotionLease Motion;
    }

    private sealed class MotionLease
    {
        internal ActorState Actor;
        internal Mover Mover;
        internal Damageable Damageable;
        internal TrailRenderer Trail;
        internal SamuraiDashVisuals.Token Visual;
        internal bool Returning, Retired, Effects, OldInvulnerable, OldTrail, HasGoal, Running;
        internal float GoalX, GoalSpeed, StartedAt, StartX, LastProgressAt, BestDistance, NextGoal;
        // Per-lease hit bookkeeping: one hit per Damageable per dash, no per-frame allocations.
        internal readonly HashSet<IntPtr> HitObjects = new();
        internal readonly Collider2D[] Colliders = new Collider2D[16];
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
    }

    private static void Finish(MotionLease m, bool failure = false)
    {
        if (!Current(m)) return;
        try
        {
            RestoreEffects(m);
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
                if (m.Returning)
                {
                    if (failure) { m.Actor.Failures++; m.Actor.RetryAt = Time.time + 2f; }
                    else { m.Actor.Failures = 0; m.Actor.RetryAt = 0; }
                }
                else m.Actor.NextAttack = Time.time + Cooldown;
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

    private static MotionLease Begin(ActorState a, bool returning, float goal)
    {
        Knight k = a.Owner;
        var m = new MotionLease { Actor = a, Mover = k._mover, Damageable = k._damageable,
            Trail = k._trail, Returning = returning, StartedAt = Time.time,
            StartX = k.transform.position.x, LastProgressAt = Time.time,
            BestDistance = returning ? Distance(a) : 0 };
        a.Motion = m;
        m.OldInvulnerable = m.Damageable.invulnerable;
        m.OldTrail = m.Trail != null && m.Trail.enabled;
        m.Effects = true;
        m.Damageable.invulnerable = true;
        if (m.Trail != null) m.Trail.enabled = true;
        if (k._animator != null) k._animator.SetTrigger(PowerSlash);
        m.Visual = SamuraiDashVisuals.Begin(k);
        Goal(m, goal, DashSpeed);
        return m;
    }

    private static float StationX(ActorState a)
    {
        float x = a.Owner.transform.position.x, target = a.Follower.transform.position.x;
        return target - Mathf.Sign(target - x) * 2.5f;
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
                if (!ValidMotion(m)) { Finish(m, m.Returning); return; }
                if (m.Returning)
                {
                    if (NightGuard(knight)) { Finish(m); return; }
                    // Verify this reference each frame before periodic reselection, never mask its loss.
                    if (!ValidFollower(knight, a.Follower)) { Finish(m); a.Follower = null; return; }
                    RefreshFollower(a);
                    AdvanceReturn(m);
                }
                else if (ValidFollower(knight, a.Follower) && Distance(a) > FollowLeash) Finish(m);
                return; // Attack and Return are mutually exclusive, including the 4..10 band.
            }
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
            if (distance > FollowLeash || (a.Failures > 0 && distance > ReturnStop))
            {
                if (Time.timeScale <= 0 || knight._mover._pauseTimeout > 0 ||
                    Time.time < a.RetryAt || a.Failures >= 3) return;
                float x = knight.transform.position.x;
                var burst = Begin(a, true, x + Mathf.Clamp(StationX(a) - x, -MaxRange, MaxRange));
                HitScan(burst); // entry frame hits, same as the attack dash's first coroutine step
                if (Current(burst) && (!ValidMotion(burst) || !ValidFollower(knight, a.Follower)))
                    Finish(burst, true);
                return;
            }
            if (Time.timeScale <= 0 || knight._mover._pauseTimeout > 0 || Time.time < a.NextAttack) return;
            a.NextAttack = Time.time + ScanInterval;
            GameObject target = knight._enemyScanner != null ? knight._enemyScanner.GetClosest() : null;
            if (target == null) return;
            Damageable enemy = target.GetComponent<Damageable>();
            if (enemy == null || !enemy.IsDamagedBy(DamageSource.Knight)) return;
            float dx = target.transform.position.x - knight.transform.position.x;
            if (Mathf.Abs(dx) < 1.5f || Mathf.Abs(dx) > MaxRange) return;
            var attack = Begin(a, false, target.transform.position.x - Mathf.Sign(dx) * .5f);
            knight.StartCoroutine(AttackRoutine(attack).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            if (a?.Motion != null) Finish(a.Motion, a.Motion.Returning);
            Log("tick", e);
        }
    }

    private static void AdvanceReturn(MotionLease m)
    {
        if (!ValidMotion(m)) { Finish(m, true); return; }
        var a = m.Actor;
        if (!ValidFollower(a.Owner, a.Follower)) { Finish(m); return; }
        float distance = Distance(a), now = Time.time;
        if (!m.Running && Time.timeScale > 0 && now - m.StartedAt < DashTimeout &&
            Mathf.Abs(a.Owner.transform.position.x - m.StartX) < MaxRange)
            HitScan(m);
        // A hit callback may synchronously disable the knight or replace this lease; never touch the new one.
        if (!Current(m)) return;
        if (!ValidMotion(m)) { Finish(m, true); return; }
        if (!ValidFollower(a.Owner, a.Follower)) { Finish(m); return; }
        distance = Distance(a); now = Time.time;
        if (distance <= ReturnStop) { Finish(m); return; }
        if (distance < m.BestDistance - .1f) { m.BestDistance = distance; m.LastProgressAt = now; }
        if (now - m.StartedAt >= 3f || now - m.LastProgressAt >= .5f) { Finish(m, true); return; }
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

    private static bool CanHit(MotionLease m) => !m.Running && Time.timeScale > 0 &&
        ValidMotion(m) && Time.time - m.StartedAt < DashTimeout &&
        Mathf.Abs(m.Actor.Owner.transform.position.x - m.StartX) < MaxRange &&
        (m.Returning ? ValidFollower(m.Actor.Owner, m.Actor.Follower) :
            !ValidFollower(m.Actor.Owner, m.Actor.Follower) || Distance(m.Actor) <= FollowLeash);

    /// <summary>Shared burst hit scan used by both the attack dash and the return dash.</summary>
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
            while (ValidMotion(m) && Time.time - m.StartedAt < DashTimeout &&
                Mathf.Abs(m.Actor.Owner.transform.position.x - m.StartX) < MaxRange)
            {
                var a = m.Actor;
                if (ValidFollower(a.Owner, a.Follower) && Distance(a) > FollowLeash) break;
                if (Time.timeScale > 0)
                {
                    RefreshFollower(a);
                    if (ValidFollower(a.Owner, a.Follower) && Distance(a) > FollowLeash) break;
                    HitScan(m);
                }
                yield return null;
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
            return m != null && m.Returning && ValidMotion(m) && ValidFollower(knight, a.Follower) && Distance(a) > ReturnStop;
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
