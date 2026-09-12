using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Lease native Object-follow offsets; preserve the original goal, speed and Wait.</summary>
internal static class SquadFollowGuard
{
    private sealed class Lease
    {
        internal IntPtr Key;
        internal Archer Archer;
        internal Knight Knight;
        internal Mover Mover;
        internal GameObject Goal;
        internal World World;
        internal Transform Layer;
        internal float Original, Written, Speed;
        internal bool ClampLogged, Confirmed, Station, StationLogged;
        internal Mover StationMover;
        internal float StationX, StationFacing;
    }
    private static readonly Dictionary<IntPtr, Lease> Ledger = new();
    private static readonly List<Lease> Snapshot = new();
    private static readonly HashSet<string> Logged = new();
    private static bool Reconciling;
    private static int ClampLogCount, StationLogCount;
    private static readonly HashSet<IntPtr> LoggedClampLeaders = new();
    private static readonly Dictionary<IntPtr, (World World, Transform Layer)> SuppressedObservers = new();

    private static void Info(string key, string message)
    {
        if (!Logged.Add(key)) return;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SquadFollowGuard] " + message); }
        catch { }
    }
    private static void Error(Exception e) => Info("error", "guard failed safely: " + e.GetType().Name);
    private static bool Same(UnityEngine.Object a, UnityEngine.Object b) => a != null && b != null && a.Pointer == b.Pointer;
    private static bool Active(Component c) => c != null && c.gameObject != null && c.gameObject.activeInHierarchy;
    private static bool TryCurrentWorld(out World world, out Transform layer)
    {
        var managers = Managers.Inst;
        world = managers != null ? managers.world : null;
        layer = world != null ? world.gameLayer : null;
        return world != null && world.gameObject != null && layer != null && layer.gameObject != null;
    }
    private static bool InLayer(Component actor, Transform layer) => actor != null && layer != null &&
        actor.transform != null && actor.transform.IsChildOf(layer);
    private static bool CurrentWorld(Lease l) => TryCurrentWorld(out var world, out var layer) &&
        Same(world, l.World) && Same(layer, l.Layer) && InLayer(l.Archer, layer) &&
        InLayer(l.Knight, layer) && InLayer(l.Mover, layer);
    private static bool Suppressed(IntPtr key, World world, Transform layer)
    {
        if (!SuppressedObservers.TryGetValue(key, out var scope)) return false;
        if (Same(scope.World, world) && Same(scope.Layer, layer)) return true;
        SuppressedObservers.Remove(key);
        return false;
    }
    private static void RefuseObservation(Lease l)
    {
        if (l.Mover == null) return;
        // If an old actor survives/reparents into the current layer without a new native goal,
        // refuse automatic rebasing there too. This stores no old Original or actor reference.
        if (TryCurrentWorld(out var world, out var layer) && InLayer(l.Archer, layer) && InLayer(l.Mover, layer))
            SuppressedObservers[l.Key] = (world, layer);
        else SuppressedObservers[l.Key] = (l.World, l.Layer);
    }
    private static bool FreeCharacter(Character c) => c != null && !c.inert && !c.grabbed && !c.isStationary;
    private static bool BoatTask(Embarkee e) => e != null && (e.IsEmbarked || e.IsTargetingEmbarkable || e.EmbarkableTarget != null);
    private static bool LiveKnight(Knight k) => Active(k) && k._damageable != null && !k._damageable.isDead;
    private static bool FreeArcher(Archer a) => Active(a) && a._damageable != null && !a._damageable.isDead &&
        FreeCharacter(a._character) && a._mover != null && !a.ShouldPlayerControl() &&
        a._guardSlot == null && !a.inGuardSlot && a.transform.position.y <= 2.5f &&
        a.GetFormation() == null && !BoatTask(a._embarkee);
    private static bool AvailableKnight(Knight k) => LiveKnight(k) && FreeCharacter(k._character) &&
        k._mover != null &&
        !k._beingControlled && !k.ShouldPlayerControl() && k.GetFormation() == null &&
        k.helPuzzlePillar == null && !BoatTask(k._embarkee);
    private static bool FreeKnight(Knight k) => AvailableKnight(k) && !k.isCharging && !k._shouldCharge;
    private static int State(Archer a)
    {
        if (a.behaviour == null) return -1;
        var h = a.behaviour.Cast<Coatsink.Common.Haglet>();
        return h != null && h.started ? h.latestGoto : -1;
    }
    private static bool Follows(Archer a) => FreeArcher(a) && LiveKnight(a._knight) && State(a) == 2;
    private static bool WallTask(Archer a, Kingdom kingdom)
    {
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth || kingdom == null || kingdom.isDaytime ||
            !Follows(a) || !FreeKnight(a._knight)) return false;
        var k = a._knight;
        return k._fsm != null && (k._fsm.Current == Knight.State.Stand || k._fsm.Current == Knight.State.GoToWall) &&
            (k.side == Side.Left || k.side == Side.Right);
    }
    private static bool WallFollower(Archer a, Kingdom kingdom, bool confirmed)
    {
        if (!WallTask(a, kingdom)) return false;
        // Once confirmed, a defensive dash can cross the wall without turning into an expedition.
        // GoToWall is explicit home defense, including native isRetreating and save/load outside the wall.
        if (confirmed || a._knight._fsm.Current == Knight.State.GoToWall) return true;
        float depth = (kingdom.GetBorderSideIntact(a._knight.side) - a._knight.transform.position.x) * (float)a._knight.side;
        return float.IsFinite(depth) && depth >= -.5f && depth <= 15f;
    }
    internal static bool IsWallFollower(Archer a, Kingdom kingdom)
    {
        try
        {
            bool confirmed = a != null && a._mover != null && Ledger.TryGetValue(a._mover.Pointer, out var l) && l.Confirmed && Owns(l);
            return WallFollower(a, kingdom, confirmed) && a._mover._pauseTimeout <= 0;
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
            return ModConfig.Enabled.Value && NetworkBigBoss.HasWorldAuth && FreeKnight(k) && !k.isRetreating &&
                k._fsm != null && k._fsm.Current == Knight.State.Assemble && k._mover._pauseTimeout <= 0;
        }
        catch (Exception e) { Error(e); return false; }
    }
    private static bool TryOffset(Knight k, GameObject goal, Kingdom kingdom, float original, out float adjusted)
    {
        adjusted = original;
        float x = goal.transform.position.x, facing = goal.transform.localScale.x;
        float wall = kingdom.GetBorderSideIntact(k.side), sign = (float)k.side;
        float pullback = PatchRoles_KnightStyle.GetFollowerAnchorPullback(k);
        if (!float.IsFinite(x) || !float.IsFinite(facing) || Mathf.Abs(facing) < .01f ||
            !float.IsFinite(wall) || !float.IsFinite(pullback) || pullback <= 0) return false;
        float anchor = x + original * facing;
        if (!float.IsFinite(anchor)) return false;
        if ((wall - anchor) * sign < pullback) adjusted = (wall - sign * pullback - x) / facing;
        return float.IsFinite(adjusted);
    }
    private static bool ChargeTask(Archer a) => ModConfig.Enabled.Value && NetworkBigBoss.HasWorldAuth &&
        Follows(a) && AvailableKnight(a._knight) && a._knight.isCharging && !a._knight._shouldCharge &&
        a._knight._fsm != null && a._knight._fsm.Current == Knight.State.Charge;

    private static bool TryStationOffset(Lease l, out float adjusted)
    {
        adjusted = l.Original;
        if (!ChargeTask(l.Archer)) { l.Station = false; return false; }
        var k = l.Knight;
        var leaderMover = k._mover;
        if (l.Station)
        {
            // A new native Position goal, including a same-position reissue with movingToGoal=true,
            // must finish its own travel before we capture again. Block.Stop keeps the old position.
            bool sameGoal = Same(leaderMover, l.StationMover) && leaderMover._goalPosition == l.StationX &&
                !leaderMover.movingToGoal && (leaderMover.goalMode == Mover.GoalMode.Position ||
                (leaderMover.goalMode == Mover.GoalMode.Off && leaderMover._moveSpeed == 0));
            if (!sameGoal) l.Station = false;
        }
        if (!l.Station)
        {
            float goalX = leaderMover._goalPosition, x = k.transform.position.x, facing = k.transform.localScale.x;
            // movingToGoal=false alone is also true for an en-route Block/Stop. Require the actual
            // native Position goal and its arrival tolerance, shared by portal and serpent Charge.
            if (leaderMover.goalMode != Mover.GoalMode.Position || leaderMover.movingToGoal ||
                leaderMover._pauseTimeout > 0 || !float.IsFinite(goalX) || !float.IsFinite(x) ||
                Mathf.Abs(x - goalX) > .0625f || !float.IsFinite(facing) || Mathf.Abs(facing) < .01f) return false;
            l.Station = true;
            l.StationLogged = false;
            l.StationMover = leaderMover;
            l.StationX = goalX;
            l.StationFacing = facing;
        }
        float currentX = k.transform.position.x, currentFacing = k.transform.localScale.x;
        float target = l.StationX + l.Original * l.StationFacing;
        if (!float.IsFinite(currentX) || !float.IsFinite(currentFacing) || Mathf.Abs(currentFacing) < .01f ||
            !float.IsFinite(target)) { l.Station = false; return false; }
        adjusted = (target - currentX) / currentFacing;
        if (!float.IsFinite(adjusted)) { l.Station = false; return false; }
        return true;
    }
    private static bool TryDefenseOffset(Lease l, Kingdom kingdom, out float adjusted)
    {
        if (TryStationOffset(l, out adjusted)) { l.Confirmed = false; return true; }
        l.Confirmed = WallFollower(l.Archer, kingdom, l.Confirmed);
        return l.Confirmed && TryOffset(l.Knight, l.Goal, kingdom, l.Original, out adjusted);
    }
    internal static void AdjustAnchor(Mover mover, GameObject goal, float speed, ref float offset, Mover.OffsetMode mode)
    {
        try
        {
            if (mover == null || !TryCurrentWorld(out var world, out var layer)) return;
            SuppressedObservers.Remove(mover.Pointer); // An explicit native SetGoal is a new authorized baseline.
            Ledger.TryGetValue(mover.Pointer, out var prior);
            bool owned = prior != null && Owns(prior);
            bool sameTask = owned && Same(goal, prior.Goal) && mode == Mover.OffsetMode.Formation;
            bool reassert = sameTask && offset == prior.Written && speed == prior.Speed;
            float original = reassert ? prior.Original : offset;
            Ledger.Remove(mover.Pointer); // Every non-exact native argument supplies a fresh baseline.
            if (reassert && NetworkBigBoss.HasWorldAuth)
            {
                if (mover._pauseTimeout > 0 || Time.timeScale <= 0) { Ledger[prior.Key] = prior; return; }
                offset = original;
            }
            if (!NetworkBigBoss.HasWorldAuth || goal == null || mode != Mover.OffsetMode.Formation ||
                !float.IsFinite(original) || !float.IsFinite(speed) || speed <= 0) return;
            var a = mover.GetComponent<Archer>();
            var kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
            if (!Follows(a) || !Same(a._mover, mover) || !Same(a._knight.gameObject, goal) ||
                !InLayer(a, layer) || !InLayer(a._knight, layer) || !InLayer(mover, layer)) return;
            var lease = new Lease { Key = mover.Pointer, Archer = a, Knight = a._knight, Mover = mover, Goal = goal,
                World = world, Layer = layer, Original = original, Written = original, Speed = speed, Confirmed = sameTask && prior.Confirmed,
                Station = sameTask && prior.Station, StationMover = sameTask ? prior.StationMover : null,
                StationX = sameTask ? prior.StationX : 0, StationFacing = sameTask ? prior.StationFacing : 0,
                StationLogged = sameTask && prior.StationLogged };
            float adjusted = original;
            if (mover._pauseTimeout <= 0 && Time.timeScale > 0 && !TryDefenseOffset(lease, kingdom, out adjusted))
                adjusted = original;
            lease.Written = adjusted;
            Ledger[mover.Pointer] = lease;
            offset = adjusted; // Observe unchanged daytime/deep targets too: Follow2 need not call SetGoal again at dusk.
            if (lease.Station) LogStation(lease);
            else if (lease.Confirmed) LogClamp(lease, kingdom);
        }
        catch (Exception e) { Error(e); }
    }
    // Called from the existing shared Archer roster pass after loading; never discovers actors itself.
    internal static void ObserveCurrentFollow(Archer archer)
    {
        Lease lease = null;
        try
        {
            if (!NetworkBigBoss.HasWorldAuth || archer == null || archer._mover == null) return;
            var mover = archer._mover;
            if (!TryCurrentWorld(out var world, out var layer)) return;
            // Even an invalid old receipt belongs to its original ownership reconciliation path.
            // Do not silently replace it or re-adopt a tuple that an external writer took over.
            if (Ledger.ContainsKey(mover.Pointer) || Suppressed(mover.Pointer, world, layer) ||
                !Follows(archer) || !Same(mover.GetComponent<Archer>(), archer) ||
                !InLayer(archer, layer) || !InLayer(archer._knight, layer) || !InLayer(mover, layer) ||
                mover.goalMode != Mover.GoalMode.Object || mover._goalOffsetMode != Mover.OffsetMode.Formation ||
                !Same(mover._goalObject, archer._knight.gameObject) ||
                !float.IsFinite(mover._goalOffset) || !float.IsFinite(mover._goalSpeed) || mover._goalSpeed <= 0) return;
            lease = new Lease { Key = mover.Pointer, Archer = archer, Knight = archer._knight, Mover = mover,
                Goal = mover._goalObject, World = world, Layer = layer,
                Original = mover._goalOffset, Written = mover._goalOffset, Speed = mover._goalSpeed };
            Ledger.Add(lease.Key, lease);
            UpdateLease(lease); // Field-only adjustment; keeps the current native Wait and pause.
        }
        catch (Exception e)
        {
            if (lease != null) { try { Release(lease); } catch { Forget(lease); } }
            Error(e);
        }
    }
    private static void LogClamp(Lease l, Kingdom kingdom)
    {
        if (l.ClampLogged || ClampLogCount >= 8 || l.Original == l.Written) return;
        var k = l.Knight;
        float x = l.Goal.transform.position.x, facing = l.Goal.transform.localScale.x;
        float wall = kingdom.GetBorderSideIntact(k.side);
        // Reserve diagnostics for actual leader excursions, once per leader, so four initial
        // follower spacing adjustments cannot consume the useful natural-night evidence budget.
        if ((wall - x) * (float)k.side >= 0 || !LoggedClampLeaders.Add(k.Pointer)) return;
        l.ClampLogged = true;
        Info("clamp-" + ClampLogCount++, $"defense clamp leader={k.Pointer} leaderX={x:F2} facing={facing:F2} archerX={l.Archer.transform.position.x:F2} nativeX={x + l.Original * facing:F2} clampedX={x + l.Written * facing:F2} wall={wall:F2} state={k._fsm.Current} retreat={k.isRetreating}");
    }
    private static void LogStation(Lease l)
    {
        if (l.StationLogged || StationLogCount >= 4) return;
        l.StationLogged = true;
        Info("station-" + StationLogCount++, $"charge station leader={l.Knight.Pointer} stationX={l.StationX:F2} stationFacing={l.StationFacing:F2} targetX={l.StationX + l.Original * l.StationFacing:F2} leaderX={l.Knight.transform.position.x:F2} state={l.Knight._fsm.Current}");
    }
    private static bool Current(Lease l) => Ledger.TryGetValue(l.Key, out var current) && ReferenceEquals(current, l);
    private static void Forget(Lease l) { if (Current(l)) Ledger.Remove(l.Key); }
    private static bool Owns(Lease l) => CurrentWorld(l) && l.Archer != null && Same(l.Archer._mover, l.Mover) &&
        Same(l.Archer._knight, l.Knight) && l.Knight != null && Same(l.Knight.gameObject, l.Goal) &&
        l.Mover.goalMode == Mover.GoalMode.Object && Same(l.Mover._goalObject, l.Goal) &&
        l.Mover._goalOffsetMode == Mover.OffsetMode.Formation &&
        float.IsFinite(l.Mover._goalOffset) && float.IsFinite(l.Mover._goalSpeed) &&
        l.Mover._goalOffset == l.Written && l.Mover._goalSpeed == l.Speed;
    private static void Release(Lease l)
    {
        if (!TryCurrentWorld(out _, out _)) return; // A temporarily unavailable scope cannot authorize restoration or retirement.
        Forget(l);
        // A field write preserves the native Wait, pause, and any callbacks associated with SetGoal.
        if (NetworkBigBoss.HasWorldAuth && Owns(l)) l.Mover._goalOffset = l.Original;
    }
    private static void UpdateLease(Lease l)
    {
        if (!TryCurrentWorld(out _, out _)) return; // Keep pending ownership while loading temporarily hides the scope.
        if (!NetworkBigBoss.HasWorldAuth || !Owns(l))
        {
            // An automatic roster seed must not undo the external-writer/authority protection
            // on a later pass. Only a new native SetGoal or world Clear permits adoption again.
            RefuseObservation(l);
            Forget(l);
            return;
        }
        if (Time.timeScale <= 0 || l.Mover._pauseTimeout > 0) return;
        var kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
        if (!TryDefenseOffset(l, kingdom, out float adjusted))
        {
            l.Mover._goalOffset = l.Original;
            l.Written = l.Original;
            l.Confirmed = false;
            // Retain a dormant exact tuple while native Follow2 continues through day/night or leader missions.
            if (!Follows(l.Archer)) Forget(l);
            return;
        }
        l.Mover._goalOffset = adjusted;
        l.Written = adjusted;
        if (l.Station) LogStation(l);
        else LogClamp(l, kingdom);
    }
    // Called first in the EXISTING Mover.Update prefix, before temporary speed multipliers.
    // Exactly one dictionary lookup for this mover; no scene scan or SetGoal on the frame path.
    internal static void BeforeMoverUpdate(Mover mover)
    {
        Lease lease = null;
        try
        {
            if (mover == null || !Ledger.TryGetValue(mover.Pointer, out lease)) return;
            UpdateLease(lease);
        }
        catch (Exception e)
        {
            if (lease != null) { try { Release(lease); } catch { Forget(lease); } }
            Error(e);
        }
    }
    internal static void Reconcile()
    {
        if (Reconciling || Ledger.Count == 0) return;
        Reconciling = true;
        try
        {
            Snapshot.Clear();
            Snapshot.AddRange(Ledger.Values);
            foreach (var lease in Snapshot)
            {
                try { if (Current(lease)) UpdateLease(lease); }
                catch (Exception e)
                {
                    try { Release(lease); } catch { Forget(lease); }
                    Error(e);
                }
            }
        }
        catch (Exception e) { Error(e); }
        finally { Snapshot.Clear(); Reconciling = false; }
    }
    // OnLevelLoaded can run after current-world Archer Follow2 already called SetGoal. Keep only
    // those exact current-world receipts so loading does not promote our written clamp to Original.
    internal static void BeginWorld(World incomingWorld)
    {
        try
        {
            if (!TryCurrentWorld(out var world, out var layer) || !Same(incomingWorld, world))
            {
                Info("world-unready", "world boundary deferred: current World/gameLayer identity is unavailable or mismatched");
                return;
            }
            foreach (var lease in new List<Lease>(Ledger.Values))
            {
                try
                {
                    bool sameScope = Same(lease.World, world) && Same(lease.Layer, layer);
                    bool live = Active(lease.Archer) && lease.Archer._damageable != null && !lease.Archer._damageable.isDead &&
                        LiveKnight(lease.Knight) && Active(lease.Mover);
                    if (sameScope && live && Owns(lease)) continue;
                    // This is a refusal marker only, never a saved baseline or a way to revive old receipts.
                    if (lease.Mover != null && (sameScope || InLayer(lease.Archer, layer)))
                        SuppressedObservers[lease.Key] = (world, layer);
                    Forget(lease); // No writes into current or departing-world actors during a boundary.
                }
                catch (Exception e) { Forget(lease); Error(e); }
            }
            foreach (var key in new List<IntPtr>(SuppressedObservers.Keys))
            {
                var scope = SuppressedObservers[key];
                if (!Same(scope.World, world) || !Same(scope.Layer, layer)) SuppressedObservers.Remove(key);
            }
        }
        catch (Exception e) { Error(e); } // Unavailable identity is deferred, never guessed or destructively cleared.
    }
    internal static void Clear()
    {
        // World teardown drops receipts only; never write into actors of the departing world.
        Ledger.Clear();
        SuppressedObservers.Clear();
    }
}