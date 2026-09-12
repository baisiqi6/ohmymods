using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Bound autonomous berserker pursuit; leave motion, attacks and ninja return routes native.</summary>
internal static class PatchCombatChaseGuard
{
    internal const float BerserkerLeash = 12f;
    internal const float HomeTolerance = 2f;

    private sealed class ActorState
    {
        internal Component Actor;
        internal Berserker Berserker;
        internal Ninja Ninja;
        internal IntPtr ActorKey, WorldKey, RootKey, FollowKey;
        internal int InstanceId;
        internal readonly List<IntPtr> ScannerKeys = new(3);
        internal bool[] Keep = Array.Empty<bool>();
        internal bool HomeKnown, Returning, Filtering, SawDawnRetreat;
        internal float HomeX, FollowOffset;
    }

    private static readonly Dictionary<IntPtr, ActorState> Actors = new();
    private static readonly Dictionary<IntPtr, ActorState> Scanners = new();
    private static IntPtr CurrentWorld, CurrentRoot;
    private static bool LoggedError, LoggedReturn, LoggedDawn;

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Same(UnityEngine.Object a, UnityEngine.Object b) =>
        a != null && b != null && a.Pointer == b.Pointer;

    private static void Error(Exception error)
    {
        if (LoggedError) return;
        LoggedError = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[CombatChase] " + error.GetType().Name); } catch { }
    }

    internal static void Clear()
    {
        Actors.Clear(); Scanners.Clear();
        CurrentWorld = CurrentRoot = IntPtr.Zero;
    }

    private static bool Context(out World world, out Transform root, out Kingdom kingdom)
    {
        world = null; root = null; kingdom = null;
        if (Time.timeScale <= 0f) return false; // Pause preserves the current plan without moving or filtering.
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) { Clear(); return false; }
        var managers = Managers.Inst;
        if (managers == null || managers.game == null || managers.game.state != Game.State.Playing ||
            managers.world == null || managers.kingdom == null || managers.world.gameLayer == null)
        { Clear(); return false; }
        world = managers.world; root = world.gameLayer; kingdom = managers.kingdom;
        if (CurrentWorld != world.Pointer || CurrentRoot != root.Pointer)
        {
            Clear(); CurrentWorld = world.Pointer; CurrentRoot = root.Pointer;
        }
        return true;
    }

    private static bool Live(Component actor, Transform root) => actor != null && actor.Pointer != IntPtr.Zero &&
        actor.gameObject != null && actor.gameObject.activeInHierarchy && actor.transform != null &&
        actor.transform.IsChildOf(root);

    private static bool Autonomous(ActorState state)
    {
        var b = state.Berserker;
        if (b != null)
            return b.enabled && b.fsm != null && b.mover != null && b.character != null &&
                !b.character.inert && !b.character.grabbed && b.damageable != null && !b.damageable.isDead &&
                !b.ShouldPlayerControl();
        var n = state.Ninja;
        return n != null && n.enabled && !n._isFisher && n._mover != null && n._character != null &&
            !n._character.inert && !n._character.grabbed && n._damageable != null && !n._damageable.isDead;
    }

    private static void Forget(ActorState state)
    {
        if (Actors.TryGetValue(state.ActorKey, out var actor) && ReferenceEquals(actor, state))
            Actors.Remove(state.ActorKey);
        foreach (var key in state.ScannerKeys)
            if (Scanners.TryGetValue(key, out var owner) && ReferenceEquals(owner, state)) Scanners.Remove(key);
    }

    internal static void Release(Component actor)
    {
        if (actor != null && Actors.TryGetValue(actor.Pointer, out var state) &&
            state.InstanceId == actor.GetInstanceID()) Forget(state);
    }

    private static void Bind(ActorState state, Scanner scanner)
    {
        if (scanner == null || scanner.Pointer == IntPtr.Zero || !Same(scanner.observer, state.Actor.transform)) return;
        if (!state.ScannerKeys.Contains(scanner.Pointer)) state.ScannerKeys.Add(scanner.Pointer);
        Scanners[scanner.Pointer] = state;
    }

    private static ActorState Ensure(Component actor, Berserker b, Ninja n)
    {
        if (!Context(out var world, out var root, out _) || !Live(actor, root)) return null;
        if (Actors.TryGetValue(actor.Pointer, out var state) &&
            (state.InstanceId != actor.GetInstanceID() || state.WorldKey != world.Pointer || state.RootKey != root.Pointer))
        { Forget(state); state = null; }
        if (state == null)
        {
            state = new ActorState { Actor = actor, Berserker = b, Ninja = n, ActorKey = actor.Pointer,
                InstanceId = actor.GetInstanceID(), WorldKey = world.Pointer, RootKey = root.Pointer };
            Actors.Add(state.ActorKey, state);
        }
        // Scanner replacement must not accumulate old keys or discard the live
        // actor's return state when a stale scanner is refreshed later.
        for (int i = state.ScannerKeys.Count - 1; i >= 0; i--)
        {
            IntPtr key = state.ScannerKeys[i];
            if (CurrentScannerKey(state, key)) continue;
            if (Scanners.TryGetValue(key, out var old) && ReferenceEquals(old, state)) Scanners.Remove(key);
            state.ScannerKeys.RemoveAt(i);
        }
        if (b != null) Bind(state, b.enemyScanner);
        else if (n != null)
        { Bind(state, n._frontScanner); Bind(state, n._behindScanner); Bind(state, n._rangeAttackScanner); }
        return state;
    }

    private static bool CurrentScannerKey(ActorState state, IntPtr key)
    {
        if (state.Berserker != null)
            return state.Berserker.enemyScanner != null && state.Berserker.enemyScanner.Pointer == key;
        var n = state.Ninja;
        return n != null && ((n._frontScanner != null && n._frontScanner.Pointer == key)
            || (n._behindScanner != null && n._behindScanner.Pointer == key)
            || (n._rangeAttackScanner != null && n._rangeAttackScanner.Pointer == key));
    }

    private static bool Owns(ActorState state, Scanner scanner)
    {
        if (!Same(scanner.observer, state.Actor.transform)) return false;
        if (state.Berserker != null)
            return state.Berserker.enemyScanner != null && state.Berserker.enemyScanner.Pointer == scanner.Pointer;
        var n = state.Ninja;
        return n != null && ((n._frontScanner != null && n._frontScanner.Pointer == scanner.Pointer) ||
            (n._behindScanner != null && n._behindScanner.Pointer == scanner.Pointer) ||
            (n._rangeAttackScanner != null && n._rangeAttackScanner.Pointer == scanner.Pointer));
    }

    private static Knight Follow(ActorState state, Transform root)
    {
        var knight = state.Berserker.followTarget;
        return knight != null && Live(knight, root) && knight._damageable != null && !knight._damageable.isDead
            ? knight : null;
    }

    private static bool SaneHome(float x)
    {
        if (!Finite(x)) return false;
        var ground = World.GroundCollider;
        if (ground == null) return false;
        var bounds = ground.bounds;
        float min = bounds.min.x, max = bounds.max.x;
        return Finite(min) && Finite(max) && max > min && x >= min - HomeTolerance && x <= max + HomeTolerance;
    }

    private static bool ToolTask(Berserker b) =>
        b.droppableTarget != null || b.fsm.Current == (int)Berserker.State.GrabDroppable;

    private static void UpdateHome(ActorState state, Kingdom kingdom, Transform root)
    {
        var b = state.Berserker;
        var follow = Follow(state, root);
        IntPtr followKey = follow != null ? follow.Pointer : IntPtr.Zero;
        if (state.FollowKey != followKey)
        {
            // Native recruitment/disband wins over an older guard assignment.
            state.FollowKey = followKey; state.FollowOffset = 0f;
            state.HomeKnown = false; state.Returning = false;
        }
        if (follow != null)
        {
            float followX = follow.transform.position.x;
            if (!SaneHome(followX)) { state.HomeKnown = false; return; }
            if (b.fsm.Current == (int)Berserker.State.FollowTarget &&
                b.mover.goalMode == Mover.GoalMode.Object && Same(b.mover._goalObject, follow.gameObject))
            {
                float goalX = b.mover.GetGoal().x;
                if (SaneHome(goalX)) state.FollowOffset = goalX - followX;
            }
            state.HomeX = followX + state.FollowOffset;
            state.HomeKnown = SaneHome(state.HomeX);
            return;
        }
        if (state.Returning && b.fsm.Current == (int)Berserker.State.GotoWall &&
            b.mover.goalMode == Mover.GoalMode.Position && b.mover._goalObject == null)
        {
            // The native wall route can legitimately change after a border/side/override update.
            float nativeGoal = b.mover.GetGoal().x;
            if (SaneHome(nativeGoal)) { state.HomeX = nativeGoal; state.HomeKnown = true; return; }
        }
        if (!state.HomeKnown || (!state.Returning && b.fsm.Current != (int)Berserker.State.Attack))
        {
            var guard = kingdom.GetGuardPosition(b.side);
            var side = guard.Key;
            float home = guard.Value - (float)side * b.distanceFromWall;
            if ((side == Side.Left || side == Side.Right) && SaneHome(home)) { state.HomeX = home; state.HomeKnown = true; }
            else state.HomeKnown = false;
        }
    }

    private static void RequestReturn(ActorState state)
    {
        if (!state.HomeKnown || ToolTask(state.Berserker)) return;
        state.Returning = true;
        if (LoggedReturn) return;
        LoggedReturn = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[CombatChase] berserker native return requested"); } catch { }
    }

    private static void CheckDistance(ActorState state)
    {
        var b = state.Berserker;
        float x = b.transform.position.x;
        if (state.HomeKnown && !ToolTask(b) && Finite(x) && b.fsm.Current == (int)Berserker.State.Attack &&
            Math.Abs(x - state.HomeX) > BerserkerLeash) RequestReturn(state);
    }

    private static bool DawnRetreat(GameObject target, Kingdom kingdom, Transform root)
    {
        if (!kingdom.isDaytime || target == null) return false;
        var enemy = target.GetComponent<Enemy>();
        if (enemy == null) enemy = target.GetComponentInParent<Enemy>();
        return enemy != null && Live(enemy, root) && enemy.shouldRetreat;
    }

    internal static void Filter(Scanner scanner)
    {
        // Unrelated scanners pay only one dictionary lookup; no global actor scans.
        if (Time.timeScale <= 0f || scanner == null || !Scanners.TryGetValue(scanner.Pointer, out var state) || state.Filtering) return;
        if (!Context(out var world, out var root, out var kingdom) || !Live(state.Actor, root) ||
            state.WorldKey != world.Pointer || state.RootKey != root.Pointer ||
            state.Actor.GetInstanceID() != state.InstanceId)
        { Forget(state); return; }
        if (!Owns(state, scanner))
        {
            Scanners.Remove(scanner.Pointer); state.ScannerKeys.Remove(scanner.Pointer); return;
        }
        if (!Autonomous(state)) return;
        var values = scanner._filtered;
        if (values == null) return;
        int original = scanner._numFiltered;
        int count = Math.Max(0, Math.Min(original, values.Length));
        if (count == 0)
        {
            if (original != 0) scanner._numFiltered = 0;
            return; // A legitimate native empty cache stays untouched.
        }
        if (state.Keep.Length < count) state.Keep = new bool[count];
        state.Filtering = true;
        try
        {
            if (state.Berserker != null)
            {
                if (ToolTask(state.Berserker)) state.Returning = false;
                UpdateHome(state, kingdom, root); CheckDistance(state);
            }
            bool sawRetreat = false;
            int eligible = 0;
            for (int i = 0; i < count; i++)
            {
                var target = values[i];
                bool retreat = DawnRetreat(target, kingdom, root);
                sawRetreat |= retreat;
                bool keep = !retreat && !(state.Berserker != null && state.Returning);
                state.Keep[i] = keep;
                if (keep && target != null) eligible++;
            }
            if (sawRetreat)
            {
                state.SawDawnRetreat = true;
                if (!LoggedDawn)
                {
                    LoggedDawn = true;
                    try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[CombatChase] dawn retreat targets excluded"); } catch { }
                }
            }
            var b = state.Berserker;
            if (b != null && sawRetreat && eligible == 0 &&
                (b.fsm.Current == (int)Berserker.State.Attack || b.fsm.Current == (int)Berserker.State.Rage))
                RequestReturn(state);
            // Predicate reads complete before the first mutation, so exceptions cannot half-compact a cache.
            if (!Context(out var currentWorld, out var currentRoot, out _) ||
                currentWorld.Pointer != state.WorldKey || currentRoot.Pointer != state.RootKey ||
                !Live(state.Actor, currentRoot) || !Autonomous(state) || !Owns(state, scanner) ||
                state.Actor.GetInstanceID() != state.InstanceId || scanner._filtered == null ||
                scanner._filtered.Pointer != values.Pointer || scanner._numFiltered != original ||
                !Scanners.TryGetValue(scanner.Pointer, out var registered) || !ReferenceEquals(registered, state)) return;
            int write = 0;
            for (int read = 0; read < count; read++)
                if (state.Keep[read]) values[write++] = values[read];
            for (int i = write; i < count; i++) values[i] = null;
            scanner._numFiltered = write;
        }
        finally { state.Filtering = false; }
    }

    internal static void TickBerserker(Berserker b, bool afterNativeUpdate)
    {
        var state = Ensure(b, b, null);
        if (state == null || !Autonomous(state)) return;
        if (ToolTask(b)) { state.Returning = false; return; }
        var kingdom = Managers.Inst.kingdom;
        var root = Managers.Inst.world.gameLayer;
        UpdateHome(state, kingdom, root); CheckDistance(state);
        if (!afterNativeUpdate || !state.Returning || !state.HomeKnown) return;
        float x = b.transform.position.x;
        if (!Finite(x)) return;
        if (Math.Abs(x - state.HomeX) <= HomeTolerance) { state.Returning = false; return; }
        // Attack owns leap drag/bump cleanup until its coroutine naturally exits.
        if (b.fsm.Current == (int)Berserker.State.Attack) return;
        int desired = Follow(state, root) != null ? (int)Berserker.State.FollowTarget : (int)Berserker.State.GotoWall;
        if (b.fsm.Current != desired) b.fsm.GoToState(desired);
    }

    internal static void TickNinja(Ninja n)
    {
        var state = Ensure(n, null, n);
        if (state == null || !Autonomous(state) || n.behaviour == null) return;
        var kingdom = Managers.Inst.kingdom;
        var root = Managers.Inst.world.gameLayer;
        bool retreatTarget = DawnRetreat(n.targetEnemy, kingdom, root);
        bool shouldReturn = kingdom.isDaytime && (retreatTarget || (n.targetEnemy == null && state.SawDawnRetreat));
        state.SawDawnRetreat = false;
        if (!shouldReturn) return;
        // Ambush/slash finish their own short native action; only the pursuit loop is redirected.
        var behaviour = n.behaviour.Cast<Coatsink.Common.Haglet>();
        if (behaviour == null || behaviour.latestGoto != Ninja.ChaseEnemies) return;
        if (retreatTarget) n.targetEnemy = null;
        n._chaseEnemyTime = 0f;
        n.behaviour.Goto(n.IsAmbushTime() ? Ninja.PositioningForAmbush : Ninja.ReturningToDojo);
    }

    [HarmonyPatch(typeof(Scanner), nameof(Scanner.Refresh), new Type[] { typeof(bool) })]
    private static class ScannerRefreshPatch
    {
        [HarmonyPostfix] private static void Postfix(Scanner __instance)
        { try { Filter(__instance); } catch (Exception e) { Error(e); } }
    }
    [HarmonyPatch(typeof(Berserker), nameof(Berserker.Update))]
    private static class BerserkerUpdatePatch
    {
        [HarmonyPrefix] private static void Prefix(Berserker __instance)
        { try { TickBerserker(__instance, false); } catch (Exception e) { Error(e); } }
        [HarmonyPostfix] private static void Postfix(Berserker __instance)
        { try { TickBerserker(__instance, true); } catch (Exception e) { Error(e); } }
    }
    [HarmonyPatch(typeof(Ninja), nameof(Ninja.Update))]
    private static class NinjaUpdatePatch
    {
        [HarmonyPostfix] private static void Postfix(Ninja __instance)
        { try { TickNinja(__instance); } catch (Exception e) { Error(e); } }
    }
    [HarmonyPatch(typeof(Berserker), nameof(Berserker.OnEnable))]
    private static class BerserkerEnablePatch
    {
        [HarmonyPostfix] private static void Postfix(Berserker __instance)
        { try { Release(__instance); Ensure(__instance, __instance, null); } catch (Exception e) { Error(e); } }
    }
    [HarmonyPatch(typeof(Ninja), nameof(Ninja.OnEnable))]
    private static class NinjaEnablePatch
    {
        [HarmonyPostfix] private static void Postfix(Ninja __instance)
        { try { Release(__instance); Ensure(__instance, null, __instance); } catch (Exception e) { Error(e); } }
    }
    [HarmonyPatch(typeof(Berserker), nameof(Berserker.OnDisable))]
    private static class BerserkerDisablePatch
    {
        [HarmonyPrefix] private static void Prefix(Berserker __instance)
        { try { Release(__instance); } catch (Exception e) { Error(e); } }
    }
    [HarmonyPatch(typeof(Ninja), nameof(Ninja.OnDisable))]
    private static class NinjaDisablePatch
    {
        [HarmonyPrefix] private static void Prefix(Ninja __instance)
        { try { Release(__instance); } catch (Exception e) { Error(e); } }
    }
}
