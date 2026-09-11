using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Temporarily adjust native Object-follow offsets; never replace follow with a wall Position goal.</summary>
internal static class SquadFollowGuard
{
    private sealed class Lease
    {
        internal IntPtr Key;
        internal Archer Archer;
        internal Knight Knight;
        internal Mover Mover;
        internal GameObject Goal;
        internal float Original, Written, Speed;
    }
    private static readonly Dictionary<IntPtr, Lease> Ledger = new();
    private static readonly List<Lease> Snapshot = new();
    private static readonly HashSet<string> Logged = new();
    private static bool Restoring, Reconciling;

    private static void Info(string key, string message)
    {
        if (!Logged.Add(key)) return;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SquadFollowGuard] " + message); }
        catch { }
    }
    private static void Error(Exception e) => Info("error", "guard failed safely: " + e.GetType().Name);
    private static bool Same(UnityEngine.Object a, UnityEngine.Object b) => a != null && b != null && a.Pointer == b.Pointer;
    private static bool Active(Component c) => c != null && c.gameObject != null && c.gameObject.activeInHierarchy;
    private static bool FreeCharacter(Character c) => c != null && !c.inert && !c.grabbed && !c.isStationary;
    private static bool BoatTask(Embarkee e) => e != null && (e.IsEmbarked || e.IsTargetingEmbarkable || e.EmbarkableTarget != null);
    private static bool LiveKnight(Knight k) => Active(k) && k._damageable != null && !k._damageable.isDead;
    private static bool FreeArcher(Archer a) => Active(a) && a._damageable != null && !a._damageable.isDead &&
        FreeCharacter(a._character) && a._mover != null && !a.ShouldPlayerControl() &&
        a._guardSlot == null && !a.inGuardSlot && a.transform.position.y <= 2.5f &&
        a.GetFormation() == null && !BoatTask(a._embarkee);
    private static bool FreeKnight(Knight k) => LiveKnight(k) && FreeCharacter(k._character) &&
        k._mover != null && !k.isCharging && !k._shouldCharge && !k.isRetreating &&
        !k._beingControlled && !k.ShouldPlayerControl() && k.GetFormation() == null &&
        k.helPuzzlePillar == null && !BoatTask(k._embarkee);
    private static int State(Archer a)
    {
        if (a.behaviour == null) return -1;
        var h = a.behaviour.Cast<Coatsink.Common.Haglet>();
        return h != null && h.started ? h.latestGoto : -1;
    }
    // A leader's expedition/formation does not cancel the archer's own native follow intention.
    private static bool Follows(Archer a) => FreeArcher(a) && LiveKnight(a._knight) && State(a) == 2;

    internal static bool IsWallFollower(Archer a, Kingdom kingdom)
    {
        try
        {
            if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth || kingdom == null || kingdom.isDaytime ||
                !Follows(a) || a._mover._pauseTimeout > 0 || !FreeKnight(a._knight)) return false;
            var k = a._knight;
            if (k._fsm == null || (k._fsm.Current != Knight.State.Stand && k._fsm.Current != Knight.State.GoToWall)) return false;
            if (k.side != Side.Left && k.side != Side.Right) return false;
            float depth = (kingdom.GetBorderSideIntact(k.side) - k.transform.position.x) * (float)k.side;
            return float.IsFinite(depth) && depth >= -.5f && depth <= 15f;
        }
        catch (Exception e) { Error(e); return false; }
    }
    internal static bool IsOrdinaryWallArcher(Archer a)
    {
        try
        {
            return ModConfig.Enabled.Value && NetworkBigBoss.HasWorldAuth && FreeArcher(a) &&
                a._knight == null && a._mover._pauseTimeout <= 0 && a.ShouldGoToWall() && State(a) == 8;
        }
        catch (Exception e) { Error(e); return false; }
    }
    internal static bool IsDayAssemblingKnight(Knight k)
    {
        try
        {
            return ModConfig.Enabled.Value && NetworkBigBoss.HasWorldAuth && FreeKnight(k) &&
                k._fsm != null && k._fsm.Current == Knight.State.Assemble && k._mover._pauseTimeout <= 0;
        }
        catch (Exception e) { Error(e); return false; }
    }

    internal static void AdjustAnchor(Mover mover, GameObject goal, float speed, ref float offset, Mover.OffsetMode mode)
    {
        try
        {
            if (Restoring || mover == null) return;
            Ledger.TryGetValue(mover.Pointer, out var prior);
            bool reassert = prior != null && Owns(prior) && Same(goal, prior.Goal) &&
                mode == Mover.OffsetMode.Formation && Mathf.Approximately(offset, prior.Written) && Mathf.Approximately(speed, prior.Speed);
            float original = reassert ? prior.Original : offset;
            // Retain the baseline only for an exact reassertion of the still-owned tuple.
            // All other incoming native goals are new writers.
            Ledger.Remove(mover.Pointer);
            if (reassert && NetworkBigBoss.HasWorldAuth && Follows(prior.Archer))
            {
                var priorKingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
                if (!IsWallFollower(prior.Archer, priorKingdom))
                {
                    if (mover._pauseTimeout > 0 || Time.timeScale <= 0) Ledger[prior.Key] = prior;
                    else offset = original; // This native reassertion itself releases our spacing on departure/config-off.
                    return;
                }
            }
            if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth || goal == null ||
                mode != Mover.OffsetMode.Formation || !float.IsFinite(offset) || !float.IsFinite(speed) || speed <= 0) return;
            var a = mover.GetComponent<Archer>();
            var kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
            if (!IsWallFollower(a, kingdom) || !Same(a._mover, mover) || !Same(a._knight.gameObject, goal)) return;
            float x = goal.transform.position.x, facing = goal.transform.localScale.x;
            if (!float.IsFinite(x) || !float.IsFinite(facing) || Mathf.Abs(facing) < .01f) return;
            float wall = kingdom.GetBorderSideIntact(a._knight.side), sign = (float)a._knight.side;
            float pullback = PatchRoles_KnightStyle.GetFollowerAnchorPullback(a._knight);
            if (!float.IsFinite(wall) || !float.IsFinite(pullback) || pullback <= 0) return;
            float anchor = x + offset * facing;
            if (!float.IsFinite(anchor)) return;
            if ((wall - anchor) * sign >= pullback)
            {
                if (reassert) Ledger[mover.Pointer] = prior;
                return;
            }
            float adjusted = (wall - sign * pullback - x) / facing;
            if (!float.IsFinite(adjusted)) return;
            var lease = new Lease { Key = mover.Pointer, Archer = a, Knight = a._knight, Mover = mover, Goal = goal,
                Original = original, Written = adjusted, Speed = speed };
            Ledger[mover.Pointer] = lease;
            offset = adjusted; // Original SetGoal and its Wait still execute, keeping GoalMode.Object.
            Info("apply", "wall spacing adjusted without replacing the native dynamic follow target");
        }
        catch (Exception e) { Error(e); }
    }
    private static bool Current(Lease l) => Ledger.TryGetValue(l.Key, out var current) && ReferenceEquals(current, l);
    private static void Forget(Lease l)
    {
        if (Current(l)) Ledger.Remove(l.Key);
    }
    private static bool Owns(Lease l) => Active(l.Archer) && Same(l.Archer._mover, l.Mover) &&
        Same(l.Archer._knight, l.Knight) && LiveKnight(l.Knight) && Same(l.Knight.gameObject, l.Goal) &&
        l.Mover.goalMode == Mover.GoalMode.Object && Same(l.Mover._goalObject, l.Goal) &&
        l.Mover._goalOffsetMode == Mover.OffsetMode.Formation &&
        float.IsFinite(l.Mover._goalOffset) && float.IsFinite(l.Mover._goalSpeed) &&
        Mathf.Approximately(l.Mover._goalOffset, l.Written) && Mathf.Approximately(l.Mover._goalSpeed, l.Speed);

    internal static void Reconcile()
    {
        if (Reconciling || Ledger.Count == 0) return;
        Reconciling = true;
        try
        {
            if (!NetworkBigBoss.HasWorldAuth) { Ledger.Clear(); return; }
            if (Time.timeScale <= 0) return;
            Snapshot.Clear();
            Snapshot.AddRange(Ledger.Values);
            for (int i = 0; i < Snapshot.Count; i++)
            {
                var lease = Snapshot[i];
                try
                {
                    if (!Current(lease)) continue; // A previous callback may have replaced or cleared this receipt.
                    if (!NetworkBigBoss.HasWorldAuth) { Ledger.Clear(); break; }
                    if (!Owns(lease) || !Follows(lease.Archer)) { Forget(lease); continue; }
                    var mover = lease.Mover;
                    if (mover._pauseTimeout > 0) continue; // Retain our receipt, never clear native/external pauses.
                    var kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
                    if (IsWallFollower(lease.Archer, kingdom)) continue;
                    Forget(lease); // Retire before callbacks; never erase a newer receipt afterwards.
                    bool previous = Restoring;
                    Restoring = true;
                    try { mover.SetGoal(lease.Goal, lease.Speed, lease.Original, Mover.OffsetMode.Formation); }
                    finally { Restoring = previous; }
                    Info("release", "original follow spacing restored as squad leaves wall defense");
                }
                catch (Exception e) { Forget(lease); Error(e); }
            }
        }
        catch (Exception e) { Error(e); }
        finally { Snapshot.Clear(); Reconciling = false; }
    }
    internal static void Clear()
    {
        // No writes into the departing world. Snapshot entries are validated by Current before use.
        Ledger.Clear();
    }
}
