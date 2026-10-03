using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Farm activity points only: no pathfinding, physics/animation writes, or saved state.
/// Drives only after a successful native Cat.Update in this enable generation.
/// </summary>
internal static class FarmCatMovement
{
    internal readonly record struct Interval(float Min, float Max);
    private readonly record struct Identity(IntPtr Pointer, int Id, IntPtr ObjectPointer, int ObjectId);
    private readonly record struct ScopeKey(IntPtr World, IntPtr Layer, int Scene, IntPtr Kingdom);
    private readonly record struct Goal(Mover.GoalMode Mode, Identity Object, float Position, float Speed,
        float Offset, Mover.OffsetMode OffsetMode);
    private sealed class Life
    {
        internal long Epoch;
        internal ScopeKey Scope;
        internal Identity Farm, Mover, Body;
        internal bool Bound, Fault, Pending;
        internal IntPtr PendingFsm, PendingIterator;
        internal int PendingPhase;
        internal Goal PendingGoal;
        internal int SamplePhase;
        internal IntPtr SampleIterator;
        internal float NextRecovery;
        internal readonly Progress Progress = new();
    }
    private sealed class FarmCache
    {
        internal float Until;
        internal ScopeKey Scope;
        internal readonly List<Interval> Regions = new();
    }
    private struct Snapshot
    {
        internal Cat Cat;
        internal Identity Actor, FarmKey, MoverKey, BodyKey;
        internal ScopeKey Scope;
        internal Farmhouse Farm;
        internal Mover Mover;
        internal Rigidbody2D Body;
        internal StateMachine Fsm;
        internal Cat._FarmCatRoutine_d__74 Iterator;
        internal int Phase;
        internal Goal Goal;
        internal float X, Destination, Epsilon, Margin, BodyVx;
        internal Identity Coin;
        internal bool InvalidObject;
        internal Kingdom Kingdom;
        internal Transform Layer;
        internal bool Unknown;
    }

    /// <summary>Counts actual displacement, including back-and-forth motion. Velocity is not a gate.</summary>
    internal sealed class Progress
    {
        internal bool Active;
        internal float Started, LastTime, LastFixed, LastX, Travel;
        internal int FixedSteps;
        internal void Reset() { Active = false; }
        internal bool Observe(float time, float fixedTime, float x)
        {
            if (!Active || time < LastTime || fixedTime < LastFixed)
            {
                Active = true; Started = LastTime = time; LastFixed = fixedTime; LastX = x;
                Travel = 0; FixedSteps = 0; return false;
            }
            Travel += Math.Abs(x - LastX);
            LastX = x; LastTime = time;
            if (fixedTime > LastFixed) { FixedSteps++; LastFixed = fixedTime; }
            if (Travel >= 0.05f)
            {
                Started = time; Travel = 0; FixedSteps = 0; return false;
            }
            return time - Started >= 4f && FixedSteps >= 3;
        }
    }

    private static readonly Dictionary<Identity, Life> Lives = new();
    private static readonly Dictionary<Identity, FarmCache> Farms = new();
    private static readonly List<Interval> Allowed = new();
    private static readonly Dictionary<Identity, int> Logged = new();
    private static ScopeKey _logScope;
    private static long _epoch;
    private const int MaxLives = 512, MaxFarmPlots = 128;
    private const int FarmState = (int)Cat.State.FarmCating;

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static Identity Key(Component component)
    {
        GameObject go = component.gameObject;
        return new(component.Pointer, component.GetInstanceID(), go.Pointer, go.GetInstanceID());
    }
    private static Identity ObjectKey(GameObject go) => new(go.Pointer, go.GetInstanceID(), go.Pointer, go.GetInstanceID());
    private static bool UnitX(Transform t) => Mathf.Approximately(Math.Abs(t.lossyScale.x), 1f);
    private static bool InWorld(Component c, Transform layer, int scene)
        => c != null && c.gameObject != null && c.gameObject.activeInHierarchy
        && c.gameObject.scene.handle == scene && c.transform.IsChildOf(layer);

    internal static void Enabled(Cat cat, bool originalRan)
    {
        if (!originalRan) { Disabled(cat); return; }
        try
        {
            if (cat == null || cat.gameObject == null || cat.Pointer == IntPtr.Zero) return;
            Identity key = Key(cat);
            if (Lives.Count >= MaxLives && !Lives.ContainsKey(key)) return;
            Lives[key] = new Life { Epoch = ++_epoch };
        }
        catch (Exception) { /* Unknown enable is never promoted to a driveable life. */ }
    }

    internal static void Disabled(Cat cat)
    {
        try { if (cat != null && cat.gameObject != null) Lives.Remove(Key(cat)); }
        catch (Exception) { /* Do not erase another generation on an unreadable callback. */ }
    }

    private static bool Environment(out World world, out Kingdom kingdom, out Transform layer, out ScopeKey scope)
    {
        world = null; kingdom = null; layer = null; scope = default;
        float scale = Time.timeScale;
        if (ModConfig.Enabled == null || !ModConfig.Enabled.Value || BiomeHolder.Inst == null
            || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex || NetworkBigBoss.IsOnline
            || !NetworkBigBoss.HasWorldAuth || !Finite(scale) || scale <= 0f || IslandSaveData.isSavingGame
            || IslandSaveData.CurrentlySavingIsland != null || !Finite(Time.time) || !Finite(Time.fixedTime)) return false;
        Managers managers = Managers.Inst;
        if (managers == null || managers.game == null || managers.game.state != Game.State.Playing) return false;
        world = managers.world; kingdom = managers.kingdom; layer = world != null ? world.gameLayer : null;
        if (world == null || kingdom == null || layer == null || !layer.gameObject.scene.IsValid()) return false;
        scope = new(world.Pointer, layer.Pointer, layer.gameObject.scene.handle, kingdom.Pointer);
        return true;
    }

    private static bool Source(ref Snapshot s, bool requireMovement)
    {
        StateMachine fsm = s.Fsm;
        if (fsm == null || fsm.Current != FarmState) return false;
        s.Unknown = true;
        if (!fsm._shouldRunCallbacks
            || fsm._owner == null || fsm._owner.Pointer != s.Cat.Pointer
            || fsm._states == null || !fsm._states.ContainsKey(FarmState)) return false;
        var state = fsm._states[FarmState];
        if (state == null || (requireMovement && !state.IsRunning) || !state.CoroutineExists || state.CoroutineStack == null
            || state.CoroutineStack.Count != 1 || state._sourcePC == null
            || state._sourcePC.Name != "<>1__state" || state._sourcePC.DeclaringType == null
            || state._sourcePC.DeclaringType.FullName != "Cat+<FarmCatRoutine>d__74") return false;
        s.Iterator = state.CoroutineStack.Peek()?.TryCast<Cat._FarmCatRoutine_d__74>();
        if (s.Iterator == null || s.Iterator.__4__this == null || s.Iterator.__4__this.Pointer != s.Cat.Pointer) return false;
        s.Phase = s.Iterator.__1__state;
        s.Unknown = false;
        return !requireMovement || s.Phase == 1 || s.Phase == 2 || s.Phase == 4 || s.Phase == 8;
    }

    private static bool Margin(GameObject go, float epsilon, out float margin)
    {
        margin = epsilon;
        // Resource prefabs have no current world-space collider bounds. Do not manufacture them.
        if (!go.scene.IsValid()) return true;
        Collider2D collider = go.GetComponent<Collider2D>();
        if (collider == null) return true; // Legal center point, not a claim about physical reachability.
        float extent = collider.bounds.extents.x;
        if (!Finite(extent) || extent < 0f) return false;
        margin += extent;
        return Finite(margin);
    }

    private static bool Read(Cat cat, out Snapshot s, bool allowQueued = false, bool afterCleanup = false, bool requireMovement = true)
    {
        s = new Snapshot { Cat = cat };
        if (!Environment(out _, out Kingdom kingdom, out Transform layer, out ScopeKey scope)
            || cat == null || !cat.enabled || !InWorld(cat, layer, scope.Scene)) return false;
        s.Actor = Key(cat); s.Scope = scope;
        if (!cat.domesticated
            || cat.followingPlayer != null || !UnitX(cat.transform)) return false;
        Farmhouse farm = cat.farmHouse;
        if (!InWorld(farm, layer, scope.Scene) || !UnitX(farm.transform)) return false;
        Mover mover = cat.mover;
        Rigidbody2D body = mover != null ? mover.rigidbody : null;
        Droppable droppable = cat.GetComponent<Droppable>();
        if (mover == null || body == null || mover != cat.GetComponent<Mover>()
            || body != cat.GetComponent<Rigidbody2D>() || mover.gameObject != cat.gameObject
            || body.gameObject != cat.gameObject || !mover.enabled || mover.IsPaused()
            || droppable == null || droppable.gameObject != cat.gameObject || droppable.pickedUp) return false;
        Embarkee embarkee = cat.GetComponent<Embarkee>();
        if (embarkee != null && (embarkee.IsEmbarked || embarkee.IsTargetingEmbarkable || embarkee.EmbarkableTarget != null)) return false;
        s.Actor = Key(cat); s.FarmKey = Key(farm); s.MoverKey = Key(mover); s.BodyKey = Key(body);
        s.Farm = farm; s.Scope = scope; s.Layer = layer; s.Kingdom = kingdom; s.Mover = mover; s.Body = body; s.Fsm = cat.fsm;
        s.BodyVx = body.linearVelocity.x; // Evidence only: a nonzero command never excludes a stall.
        s.X = cat.transform.position.x; s.Epsilon = Mover.PositionEpsilon;
        if (!Finite(s.X) || !Finite(s.BodyVx) || !Finite(s.Epsilon) || s.Epsilon <= 0f || !Margin(cat.gameObject, s.Epsilon, out s.Margin)
            || !Source(ref s, requireMovement) || (!allowQueued && s.Fsm._executeQueuedState)
            || (requireMovement && !mover.movingToGoal)) return false;
        if (!requireMovement) return true;
        s.Unknown = true;
        float speed = mover._goalSpeed;
        if (!Finite(speed) || speed <= 0f) return false;
        Mover.GoalMode mode = mover.goalMode;
        if (mode == Mover.GoalMode.Position)
        {
            s.Destination = mover._goalPosition;
            if (!Finite(s.Destination)) return false;
            s.Goal = new(mode, default, s.Destination, speed, 0f, default);
        }
        else if (mode == Mover.GoalMode.Object)
        {
            GameObject goal = mover._goalObject;
            float offset = mover._goalOffset;
            if (s.Phase != 4 || goal == null || !Finite(offset)) return false;
            Droppable coin = goal.GetComponent<Droppable>();
            if (coin == null || coin.gameObject != goal || (!afterCleanup && cat.playCoin != coin)) return false;
            s.Coin = Key(coin);
            s.InvalidObject = !goal.activeInHierarchy || goal.scene.handle != scope.Scene || !goal.transform.IsChildOf(layer)
                || coin.pickedUp || !coin.isActiveAndEnabled;
            float x = goal.transform.position.x;
            if (!Finite(x)) return false;
            var offsetMode = mover._goalOffsetMode;
            if (offsetMode == Mover.OffsetMode.Distance) s.Destination = Mathf.Clamp(s.X, x - offset, x + offset);
            else if (offsetMode == Mover.OffsetMode.Formation) s.Destination = x + offset * goal.transform.localScale.x;
            else if (offsetMode == Mover.OffsetMode.Strict) s.Destination = x + offset;
            else return false;
            if (!Finite(s.Destination)) return false;
            s.Goal = new(mode, ObjectKey(goal), 0f, speed, offset, offsetMode);
        }
        else return false;
        s.Unknown = false;
        return true;
    }

    private static bool Geometry(Snapshot s, bool fresh)
    {
        Allowed.Clear();
        if (!Farms.TryGetValue(s.FarmKey, out FarmCache cache))
        {
            if (Farms.Count >= 256) Farms.Clear();
            Farms[s.FarmKey] = cache = new FarmCache();
        }
        if (fresh || cache.Scope != s.Scope || Time.time >= cache.Until)
        {
            cache.Regions.Clear(); cache.Until = 0f;
            float home = s.Farm.transform.position.x;
            if (!Finite(home)) return false;
            cache.Regions.Add(new(home - 4f, home + 4f));
            var plots = s.Farm.farmlands;
            if (plots == null || plots.Count > MaxFarmPlots) return false;
            for (int i = 0; i < plots.Count; i++)
            {
                Farmland plot = plots[i];
                if (plot == null) return false;
                if (!InWorld(plot, s.Layer, s.Scope.Scene) || plot.farmhouse != s.Farm) return false;
                GAPS gaps = plot.GetComponent<GAPS>();
                if (gaps == null || gaps.gameObject != plot.gameObject || !UnitX(plot.transform)) return false;
                Rect region = gaps.GetRegion();
                float min = region.xMin, max = region.xMax;
                if (!Finite(min) || !Finite(max) || min >= max) return false;
                if (plot.farmhouse != s.Farm || !InWorld(plot, s.Layer, s.Scope.Scene)) return false;
                cache.Regions.Add(new(min, max));
            }
            cache.Scope = s.Scope; cache.Until = Time.time + 0.5f;
        }
        float left = s.Kingdom.GetBorderSideIntact(Side.Left), right = s.Kingdom.GetBorderSideIntact(Side.Right);
        if (!Finite(left) || !Finite(right) || left >= right) return false;
        return Intersect(cache.Regions, left + s.Margin, right - s.Margin, Allowed);
    }

    internal static bool Intersect(List<Interval> regions, float left, float right, List<Interval> result)
    {
        result.Clear();
        if (!Finite(left) || !Finite(right) || left > right) return false;
        for (int i = 0; i < regions.Count; i++)
        {
            float min = Math.Max(left, regions[i].Min), max = Math.Min(right, regions[i].Max);
            if (min <= max) result.Add(new(min, max));
        }
        return result.Count > 0;
    }

    internal static float Project(List<Interval> intervals, float value)
    {
        float closest = value, distance = float.PositiveInfinity;
        for (int i = 0; i < intervals.Count; i++)
        {
            float point = Mathf.Clamp(value, intervals[i].Min, intervals[i].Max);
            float d = Math.Abs(point - value);
            if (d < distance) { closest = point; distance = d; }
        }
        return closest;
    }

    internal static bool TryBirth(Farmhouse farm, Cat prefab, Transform layer, float candidate, out float x)
    {
        x = candidate;
        try
        {
            if (!Environment(out _, out Kingdom kingdom, out Transform currentLayer, out ScopeKey scope)
                || currentLayer != layer || !InWorld(farm, layer, scope.Scene) || !UnitX(farm.transform)
                || prefab == null || prefab.gameObject == null || !UnitX(prefab.transform) || !Finite(candidate)) return false;
            float epsilon = Mover.PositionEpsilon;
            if (!Finite(epsilon) || epsilon <= 0f || !Margin(prefab.gameObject, epsilon, out float margin)) return false;
            float home = farm.transform.position.x;
            float left = kingdom.GetBorderSideIntact(Side.Left), right = kingdom.GetBorderSideIntact(Side.Right);
            if (!Finite(home) || !Finite(left) || !Finite(right) || left >= right) return false;
            float min = Math.Max(home - 4f, left + margin), max = Math.Min(home + 4f, right - margin);
            if (min > max) return false;
            x = Mathf.Clamp(candidate, min, max);
            // Only the home intersection is required for birth; not a claim about a physical path.
            return farm != null && farm.gameObject.activeInHierarchy && farm.farmlands != null
                && farm.transform.IsChildOf(layer) && UnitX(farm.transform) && farm.transform.position.x == home
                && Environment(out _, out Kingdom currentKingdom, out Transform finalLayer, out ScopeKey finalScope)
                && finalScope == scope && finalLayer == layer && currentKingdom == kingdom;
        }
        catch (Exception) { return false; }
    }

    private static bool Same(Snapshot a, Snapshot b)
        => a.Actor == b.Actor && a.Scope == b.Scope && a.FarmKey == b.FarmKey && a.MoverKey == b.MoverKey
        && a.BodyKey == b.BodyKey && a.Fsm.Pointer == b.Fsm.Pointer && a.Iterator.Pointer == b.Iterator.Pointer
        && a.Phase == b.Phase && a.Goal == b.Goal && a.Destination == b.Destination && a.Coin == b.Coin
        && a.X == b.X && a.Epsilon == b.Epsilon && a.Margin == b.Margin;
    private static bool Owns(Life life, Snapshot s)
        => Lives.TryGetValue(s.Actor, out Life current) && ReferenceEquals(life, current);

    // Native FarmCatRoutine checks Time.time > each stamp. Max preserves any later
    // native/external cooldown; no rollback or retry after a partial setter failure.
    private static bool DelayActivities(Cat cat, Life life, Snapshot before, out Snapshot delayed)
    {
        delayed = default;
        if (!Read(cat, out Snapshot current, false, true) || !Owns(life, current) || !Same(before, current)) return false;
        float floor = Time.time + 5f;
        float play = cat.nextPlayWithCoinTime, mouse = cat.nextCatchMouseTime;
        if (!Finite(floor) || !Finite(play) || !Finite(mouse)) return false;
        float plannedPlay = Math.Max(play, floor), plannedMouse = Math.Max(mouse, floor);
        // Reads can observe a changed world/life or a newer native/external stamp.
        play = cat.nextPlayWithCoinTime;
        if (!Finite(play) || !Read(cat, out current, false, true) || !Owns(life, current) || !Same(before, current)) return false;
        plannedPlay = Math.Max(play, plannedPlay);
        if (play < plannedPlay) cat.nextPlayWithCoinTime = plannedPlay;
        float observedPlay = cat.nextPlayWithCoinTime;
        if (!Finite(observedPlay) || observedPlay < plannedPlay
            || !Read(cat, out current, false, true) || !Owns(life, current) || !Same(before, current)) return false;
        // Re-read the second field at its own commit boundary, retaining any increase.
        mouse = cat.nextCatchMouseTime;
        if (!Finite(mouse) || !Read(cat, out current, false, true) || !Owns(life, current) || !Same(before, current)) return false;
        plannedMouse = Math.Max(mouse, plannedMouse);
        if (mouse < plannedMouse) cat.nextCatchMouseTime = plannedMouse;
        float observedMouse = cat.nextCatchMouseTime;
        if (!Finite(observedMouse) || observedMouse < plannedMouse) return false;
        // Joint acknowledgement before queueing: a later setter may have changed the first field.
        observedPlay = cat.nextPlayWithCoinTime;
        observedMouse = cat.nextCatchMouseTime;
        if (!Finite(observedPlay) || !Finite(observedMouse) || observedPlay < plannedPlay || observedMouse < plannedMouse) return false;
        return Read(cat, out delayed, false, true) && Owns(life, delayed) && Same(before, delayed);
    }

    private static void Note(Snapshot s, string reason)
    {
        if (_logScope != s.Scope) { _logScope = s.Scope; Logged.Clear(); }
        if (!Logged.TryGetValue(s.Actor, out int count))
        {
            if (Logged.Count >= 8) return;
            count = 0;
        }
        if (count >= 6) return;
        Logged[s.Actor] = count + 1;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[FarmCatMovement] " + reason
                + " cat=" + s.Actor.Id + " pc=" + s.Phase + " x=" + s.X.ToString("F2")
                + " bodyVx=" + s.BodyVx.ToString("F2"));
        }
        catch (Exception) { /* Diagnostics cannot poison a completed native transaction. */ }
    }

    internal static void Updated(Cat cat, bool originalRan)
    {
        if (!originalRan)
        {
            try { if (cat != null && cat.gameObject != null && Lives.TryGetValue(Key(cat), out Life skipped)) skipped.Progress.Reset(); }
            catch (Exception) { }
            return;
        }
        Life life = null;
        Snapshot s = default;
        try
        {
            if (cat == null || cat.gameObject == null || !Lives.TryGetValue(Key(cat), out life)) return;
            if (life.Pending)
            {
                life.Progress.Reset();
                if (!Read(cat, out s, true, true, false) || !Owns(life, s)) return;
                if (life.Scope != s.Scope || life.Farm != s.FarmKey || life.Mover != s.MoverKey
                    || life.Body != s.BodyKey || life.PendingFsm != s.Fsm.Pointer
                    || life.PendingIterator != s.Iterator.Pointer) { life.Fault = true; return; }
                if (s.Fsm._executeQueuedState)
                {
                    if (s.Fsm._queuedState != FarmState) life.Fault = true;
                    return;
                }
                if (s.Fsm.Previous != FarmState) { life.Fault = true; return; }
                life.Pending = false; life.Progress.Reset(); Note(s, "native restart consumed"); return;
            }
            if (!Read(cat, out s))
            {
                life.Progress.Reset();
                if (s.Unknown) Note(s, "native activity state unproven");
                return;
            }
            if (!life.Bound || life.Scope != s.Scope || life.Farm != s.FarmKey
                || life.Mover != s.MoverKey || life.Body != s.BodyKey)
            {
                if (life.Pending || life.Fault) { life.Fault = true; return; }
                life.Scope = s.Scope; life.Farm = s.FarmKey; life.Mover = s.MoverKey; life.Body = s.BodyKey;
                life.Bound = true; life.Progress.Reset();
            }
            if (life.Fault) return;
            if (!Geometry(s, false)) { life.Progress.Reset(); Note(s, "activity geometry unknown/empty"); return; }
            float projected = Project(Allowed, s.Destination);
            if (projected != s.Destination && s.Goal.Mode == Mover.GoalMode.Position)
            {
                life.Progress.Reset();
                if (!Read(cat, out Snapshot current) || !Owns(life, current) || !Same(s, current) || !Geometry(current, true)) return;
                float point = Project(Allowed, current.Destination);
                if (point != current.Destination)
                {
                    if (!Read(cat, out Snapshot final) || !Owns(life, final) || !Same(current, final)) return;
                    life.Fault = true;
                    final.Mover.SetGoal(point, final.Goal.Speed);
                    if (!Read(cat, out Snapshot written) || !Owns(life, written) || written.Actor != final.Actor
                        || written.Scope != final.Scope || written.FarmKey != final.FarmKey || written.MoverKey != final.MoverKey
                        || written.Goal.Mode != Mover.GoalMode.Position || written.Goal.Position != point
                        || written.Goal.Speed != final.Goal.Speed || written.Phase != final.Phase) return;
                    life.Fault = false; Note(final, "position target projected");
                }
                return;
            }
            bool illegalObject = s.Goal.Mode == Mover.GoalMode.Object && (projected != s.Destination || s.InvalidObject);
            if (!illegalObject && Math.Abs(s.Destination - s.X) <= s.Epsilon) { life.Progress.Reset(); return; }
            if (life.Progress.Active && (life.PendingGoal != s.Goal || life.SamplePhase != s.Phase
                || life.SampleIterator != s.Iterator.Pointer)) life.Progress.Reset();
            life.PendingGoal = s.Goal;
            life.SamplePhase = s.Phase; life.SampleIterator = s.Iterator.Pointer;
            bool stalled = life.Progress.Observe(Time.time, Time.fixedTime, s.X);
            if ((!illegalObject && !stalled) || Time.time < life.NextRecovery) return;
            if (!Read(cat, out Snapshot before) || !Owns(life, before) || !Same(s, before) || !Geometry(before, true)) { life.Progress.Reset(); return; }
            // Geometry may have become legal since the cached observation.
            if (illegalObject && !before.InvalidObject && Project(Allowed, before.Destination) == before.Destination) return;
            if (!Read(cat, out Snapshot commit) || !Owns(life, commit) || !Same(before, commit)) { life.Progress.Reset(); return; }
            life.NextRecovery = Time.time + 5f;
            life.Fault = true; // Claim cleanup once. Any partial failure retains an unknown fault.
            cat.ResetPlayCoin();
            if (cat.playCoin != null || !Read(cat, out Snapshot reset, false, true) || !Owns(life, reset) || !Same(commit, reset))
            {
                Note(before, "coin cleanup ownership unconfirmed"); return;
            }
            cat.OnAnimPlayWithCoin();
            if (cat.playCoin != null || cat.playingWithCoin) { Note(before, "cleanup readback unconfirmed"); return; }
            if (!Read(cat, out Snapshot cleaned, false, true) || !Owns(life, cleaned) || !Same(before, cleaned)) { Note(before, "cleanup ownership changed"); return; }
            if (!DelayActivities(cat, life, cleaned, out Snapshot delayed)) { Note(before, "native cooldown commit unconfirmed"); return; }
            cat.fsm.GoToState(FarmState);
            if (!Read(cat, out Snapshot queued, true, true) || !Owns(life, queued) || !Same(delayed, queued)
                || !queued.Fsm._executeQueuedState || queued.Fsm._queuedState != FarmState) { Note(before, "restart queue ownership unconfirmed"); return; }
            life.Pending = true; life.PendingFsm = queued.Fsm.Pointer; life.PendingIterator = queued.Iterator.Pointer;
            life.PendingPhase = queued.Phase; life.PendingGoal = queued.Goal;
            queued.Mover.Stop(); // Already queued: next native Update resets before it steps the old wait.
            life.Fault = false; life.Progress.Reset(); Note(queued, illegalObject ? "illegal object restart queued" : "no displacement restart queued");
        }
        catch (Exception)
        {
            if (life != null) { life.Progress.Reset(); if (life.Pending) life.Fault = true; }
            if (s.Scope.World != IntPtr.Zero) { try { Note(s, "native access/cleanup deferred"); } catch (Exception) { } }
        }
    }
}

[HarmonyPatch(typeof(Cat), nameof(Cat.OnEnable))]
internal static class Cat_OnEnable_FarmMovement_Patch
{
    [HarmonyPostfix] internal static void Postfix(Cat __instance, bool __runOriginal)
        => FarmCatMovement.Enabled(__instance, __runOriginal);
}
[HarmonyPatch(typeof(Cat), nameof(Cat.OnDisable))]
internal static class Cat_OnDisable_FarmMovement_Patch
{
    [HarmonyPrefix] internal static void Prefix(Cat __instance) => FarmCatMovement.Disabled(__instance);
}
[HarmonyPatch(typeof(Cat), nameof(Cat.Update))]
internal static class Cat_Update_FarmMovement_Patch
{
    [HarmonyPostfix] internal static void Postfix(Cat __instance, bool __runOriginal)
        => FarmCatMovement.Updated(__instance, __runOriginal);
}
