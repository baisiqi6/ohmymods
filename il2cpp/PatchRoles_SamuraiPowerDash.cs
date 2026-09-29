using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 武士燕返的触发与随从撤收（2026-09-27 修订）。整次往返的运动、伤害与收尾都在
/// <see cref="SamuraiChoreoMotion"/>：触发器只读一次世界（敌人在 [1.5, 8.2] 内 → 只取
/// 方向），随后 <c>TryBegin</c> 冻结起点（实际刚体 x）与固定 7 格终点。目标的引用不跨帧。
/// 旧协程的每帧 SetGoalNoHaglet 重申已删除：本文件不再有任何运动推进代码。
///
/// 本文件保留的职责：
/// - 触发门（Eligible）与冷却：触发门保持完整；行程存续只由 SamuraiChoreoMotion 的
///   硬失效子集裁决（原生行为旗标不算失败，见其注释）。
/// - 夜墙队列（BeforeNativeUpdate）：行程期间让位，行程结束后恢复。
/// - 脱离随从的普通 run-speed 走位（MotionLease）：与技能完全独立，不用效果/无敌/命中/
///   视觉/朝向租约，也不压制原生斩击。
/// - OnDisable/Tick 的收尾入口：把行程关闭委托给 SamuraiChoreoMotion（幂等）。
///
/// Trail 不再被本模块写入（2026-09-25 用户裁定：只保留白色残影）；姿态由行程负责
/// （PowerSlash → Land 真实控制器契约原样保留在 SamuraiChoreoMotion）。
/// </summary>
internal static class PatchRoles_SamuraiPowerDash
{
    // 2026-09-28 用户裁定（普通平衡调整）：触发冷却 3 → 6 秒；锚点仍是触发帧（行程时间计入 CD）。
    private const float ScanInterval = .2f, Cooldown = 6f;
    // The static self-scan's own reach: it must cover the whole trigger band ([1.5, 8.2]) with
    // margin, so an enemy may sit inside the scan while still being too far to open a trip.
    private const float SamuraiScanRange = 10.5f;
    // 2026-09-25 用户裁定（目标无关的编舞式往返）：冲刺距离恒为固定 7 格；触发带 = 固定 7 +
    // 命中扫描半径 1.2 = [1.5, 8.2]，带外敌人（9.0/10.5）一律不开程。距离/半径常量由运动
    // owner（SamuraiChoreoMotion）单一持有。
    private const float TriggerRange = SamuraiChoreoMotion.FixedDashDistance + SamuraiChoreoMotion.HitRadius;
    // 脱离随从的普通步速回队（2026-09-25 用户裁定删除追随冲刺）：>10 开程、进 4 到达。
    private const float FollowLeash = 10f, ReturnStop = 4f;
    private static readonly Dictionary<int, ActorState> Actors = new();
    private static readonly HashSet<string> Logged = new();

    /// <summary>仅诊断用（FrameWatch 记行时才遍历）：当前在跑的编舞式往返数。</summary>
    internal static int ActiveCutLeases => SamuraiChoreoMotion.ActiveCount;

    private sealed class ActorState
    {
        internal Knight Owner;
        internal Archer Follower;
        internal Mover ObservedMover;
        internal float NextFollowerScan, NextAttack;
        // The plain walk home (2026-09-25 user ruling): the withdrawal family's only remaining
        // motion. No effects, no invulnerability, no hits, no visuals, no facing lease and no
        // deadline -- only arrival, a lost follower, dusk or real ownership loss ends it.
        internal MotionLease Motion;
    }

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
                SamuraiChoreoMotion.ActiveTrip(knight) != null ||  // 行程存续时不排队：夜墙交接等它结束
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

    private static int EnemyScanLayer;

    // ---------------------------------------------------------------------------------------
    // Plain run-speed walk home (2026-09-25 user ruling): the withdrawal family's whole
    // remaining motion -- no effects, no invulnerability, no hit scan, no visuals, no facing
    // lease and no deadline. Only arrival, a lost follower, a stolen goal or dusk ends it.
    // ---------------------------------------------------------------------------------------

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

    // Bounded field evidence for the plain walk home (three lines per session, never per frame).
    private static int WalkLogs;
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

    internal static void Tick(Knight knight)
    {
        if (knight == null || knight.gameObject == null) return;
        int id = knight.gameObject.GetInstanceID();
        Actors.TryGetValue(id, out ActorState a);
        try
        {
            if (a != null && Same(a.Owner, knight) && !Same(a.ObservedMover, knight._mover))
            {
                SamuraiChoreoMotion.PerFrame(knight);   // mover 被替换：行程按硬失效收尾
                if (a.Motion != null) Finish(a.Motion);
                a.ObservedMover = knight._mover;
                // A replacement mover starts clean.
                return; // A replacement mover's first observed goal belongs to its new owner.
            }
            // 行程由注入的固定步驱动器推进；Knight.Update 这里只做硬失效/看门狗收尾，行程存续时
            // 不再进入触发/走位逻辑（复用实例号上的旧行程也在这一步被收掉）。
            if (SamuraiChoreoMotion.HasTrip(knight))
            {
                SamuraiChoreoMotion.PerFrame(knight);
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
            // side 后不再持有任何目标引用；起点/终点由 SamuraiChoreoMotion 在实际刚体上冻结。
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
            SamuraiChoreoMotion.TryBegin(knight, dx < 0 ? -1 : 1);
        }
        catch (Exception e)
        {
            if (SamuraiChoreoMotion.HasTrip(knight)) SamuraiChoreoMotion.CloseActive(knight, "tick-exception");
            else if (a?.Motion != null) Finish(a.Motion);
            Log("tick", e);
        }
    }

    internal static bool IsChoreoActive(Knight knight) => SamuraiChoreoMotion.IsChoreoActive(knight);

    internal static bool SuppressesSlash(Knight knight) => SamuraiChoreoMotion.IsChoreoActive(knight);

    /// <summary>
    /// True while this knight's actor entry owns any live motion (the choreographed round trip
    /// or a plain walk lease). The night wall formation redirect
    /// (PatchRoles_SamuraiNightFormation) reads it to leave motion-owned goals untouched.
    /// Exception -> true: without proof that no motion exists we never interleave.
    /// </summary>
    internal static bool HasActiveMotion(Knight knight)
    {
        try
        {
            if (knight == null || knight.gameObject == null) return false;
            if (SamuraiChoreoMotion.IsChoreoActive(knight)) return true;
            return Actors.TryGetValue(knight.gameObject.GetInstanceID(), out ActorState a) &&
                Same(a.Owner, knight) && a.Motion != null;
        }
        catch (Exception e) { Log("has-motion", e); return true; }
    }

    internal static void OnKnightDisabled(Knight knight)
    {
        if (knight == null || knight.gameObject == null) return;
        int id = knight.gameObject.GetInstanceID();
        // Unity stops a disabled owner's coroutines without running their finally, so the trip's
        // close happens here too: effects, visuals and the driver all belong to this knight.
        SamuraiChoreoMotion.CloseActive(knight, "on-disable");
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
        if (!PatchRoles_SamuraiPowerDash.SuppressesSlash(__instance)) return true;
        __result = false;
        return false;
    }
}
