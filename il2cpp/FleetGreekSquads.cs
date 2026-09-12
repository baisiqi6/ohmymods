using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Reserve one native Greek squad per selected fleet boat. Never create units or rewrite embark data.</summary>
internal static class FleetGreekSquads
{
    private sealed class Pair
    {
        internal IntPtr BoatKey, KnightKey;
        internal FleetBoat Boat;
        internal Knight Knight;
        internal Formation Formation;
        internal Transform Root;
        internal Side Side;
        internal bool Pending = true;
        internal float Deadline;
    }
    private static readonly Dictionary<IntPtr, Pair> Boats = new();
    private static readonly Dictionary<IntPtr, Pair> Knights = new();
    private static readonly List<Pair> Retired = new();
    private static readonly List<Pair> CancelBoats = new();
    private static bool Pruning;
    private static bool LoggedPlan, LoggedReject, LoggedError;
    private static FleetBoat NativeProbe;
    private static bool Same(UnityEngine.Object a, UnityEngine.Object b) => a != null && b != null && a.Pointer == b.Pointer;
    private static bool Active(Component c) => c != null && c.gameObject != null && c.gameObject.activeInHierarchy;
    private static bool Greek(Knight k) => PatchRoles_KnightStyle.TryGetResolvedStyleIndex(k, out int style) && style == 3;
    private static void Error(Exception e)
    {
        if (LoggedError) return;
        LoggedError = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[FleetGreekSquads] " + e.GetType().Name); } catch { }
    }
    private static void Forget(Pair p)
    {
        if (Boats.TryGetValue(p.BoatKey, out var b) && ReferenceEquals(p, b)) Boats.Remove(p.BoatKey);
        if (Knights.TryGetValue(p.KnightKey, out var k) && ReferenceEquals(p, k)) Knights.Remove(p.KnightKey);
    }
    private static bool Current(Pair p)
    {
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth || !Active(p.Boat) || !Active(p.Knight) ||
            p.Formation == null || p.Root == null || !p.Knight.enabled || p.Knight._harmless || p.Knight._damageable == null || p.Knight._damageable.isDead ||
            !Greek(p.Knight) || p.Boat.Pointer != p.BoatKey || p.Knight.Pointer != p.KnightKey ||
            !Same(Managers.Inst?.world?.gameLayer, p.Root) || !p.Boat.transform.IsChildOf(p.Root) ||
            !p.Knight.transform.IsChildOf(p.Root) || p.Boat.Side != p.Side) return false;
        if (!p.Pending && (!p.Formation.enabled || !Same(p.Boat._currentFormation, p.Formation))) return false;
        var e = p.Knight._embarkee;
        if (e == null || !e.CanEmbark || (e.EmbarkableTarget != null && !Same(e.EmbarkableTarget, p.Boat._embarkable))) return false;
        if (p.Knight.GetFormation() != null) return false;
        if (!e.IsEmbarked)
        {
            var c = p.Knight._character;
            if (Time.time > p.Deadline || p.Knight.side != p.Side || c == null || c.inert || c.grabbed ||
                (c.isStationary && e.EmbarkableTarget == null) ||
                p.Knight._beingControlled || p.Knight.ShouldPlayerControl() || p.Knight.isCharging ||
                p.Knight._shouldCharge || p.Knight.helPuzzlePillar != null) return false;
            // Native OnEmbarkStart becomes stationary before the boarding tween completes.
            // A still-owned target must retain its reservation through that transition.
            if (e.EmbarkableTarget == null)
            {
                if (p.Knight._fsm == null) return false;
                int state = p.Knight._fsm.Current;
                if (state != Knight.State.Stand && state != Knight.State.GoToWall && state != Knight.State.Assemble) return false;
            }
        }
        return true;
    }
    internal static void Prune()
    {
        if (Pruning) return;
        Pruning = true;
        try
        {
            Retired.Clear();
            foreach (var p in Boats.Values)
            {
                try { if (!Current(p)) Retired.Add(p); }
                catch (Exception e) { Retired.Add(p); Error(e); }
            }
            foreach (var p in Retired) { Forget(p); CancelBoats.Add(p); }
        }
        finally { Retired.Clear(); Pruning = false; }
    }
    internal static void Maintain()
    {
        Prune();
        // Never mutate the registrar's native collections from its scoring callback.
        // Retire only this plan's still-registered boat on the existing coordinator cadence.
        var pending = CancelBoats.ToArray();
        CancelBoats.Clear();
        foreach (var p in pending)
        {
            try
            {
                if (!NetworkBigBoss.HasWorldAuth || !Same(Managers.Inst?.world?.gameLayer, p.Root) ||
                    !Active(p.Boat) || p.Formation == null || !p.Formation.enabled || Boats.ContainsKey(p.BoatKey)) continue;
                // A native TryRecruit exception can leave an array membership before writing
                // _currentFormation. An unrelated non-null formation still takes precedence.
                if (p.Boat._currentFormation != null && !Same(p.Boat._currentFormation, p.Formation)) continue;
                var unit = p.Boat.Cast<Formation.IFormationUnit>();
                if (p.Formation.IsInFormation(unit)) p.Formation.UnregisterUnit(unit);
            }
            catch (Exception e) { Error(e); }
        }
    }
    internal static void ReleaseKnight(Knight knight)
    {
        if (knight != null && Knights.TryGetValue(knight.Pointer, out var p))
        { Forget(p); CancelBoats.Add(p); }
    }
    internal static void Clear()
    {
        Boats.Clear(); Knights.Clear(); Retired.Clear(); CancelBoats.Clear(); NativeProbe = null;
    }
    internal static void Release(Formation formation)
    {
        Retired.Clear();
        foreach (var p in Boats.Values) if (Same(p.Formation, formation)) Retired.Add(p);
        foreach (var p in Retired) Forget(p);
        Retired.Clear();
    }
    private static bool Available(Knight k, FleetBoat boat, Side side, Transform root)
    {
        if (!Active(k) || !k.enabled || k._damageable == null || k._damageable.isDead || !Greek(k) ||
            !k.transform.IsChildOf(root) || k.side != side || k._embarkee == null || !k._embarkee.CanEmbark ||
            k._embarkee.IsStowaway || k.GetFormation() != null || k.isCharging || k._shouldCharge ||
            k._beingControlled || k.ShouldPlayerControl() || k.helPuzzlePillar != null || k._harmless) return false;
        var c = k._character;
        if (c == null || c.inert || c.grabbed) return false;
        var e = k._embarkee;
        if (e.EmbarkableTarget != null && !Same(e.EmbarkableTarget, boat._embarkable)) return false;
        if (e.IsEmbarked) return Same(e.EmbarkableTarget, boat._embarkable);
        if (c.isStationary || k._fsm == null) return false;
        int state = k._fsm.Current;
        // Already-targeting native embark is allowed; otherwise only ordinary local defenders.
        return Same(e.EmbarkableTarget, boat._embarkable) || state == Knight.State.Stand ||
            state == Knight.State.GoToWall || state == Knight.State.Assemble;
    }
    internal static void Select(Formation formation, Transform root, Side side, List<FleetBoat> candidates)
    {
        Prune();
        Release(formation);
        var roster = UnitScanCache.GetKnights();
        var selected = new List<FleetBoat>(candidates.Count);
        try
        {
            // First preserve each boat's own assigned squad, then fill idle boats from free squads.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var boat in candidates)
                {
                    if (boat == null || boat._embarkable == null || boat._numSquads != 1 || Boats.ContainsKey(boat.Pointer)) continue;
                    bool legacyPassenger = false;
                    foreach (var passenger in roster)
                    {
                        if (passenger != null && passenger._embarkee != null && passenger._embarkee.IsEmbarked &&
                            Same(passenger._embarkee.EmbarkableTarget, boat._embarkable) && !Greek(passenger))
                        { legacyPassenger = true; break; }
                    }
                    if (legacyPassenger) continue;
                    Knight chosen = null;
                    foreach (var k in roster)
                    {
                        if (k == null || Knights.ContainsKey(k.Pointer) || !Available(k, boat, side, root)) continue;
                        bool assigned = Same(k._embarkee.EmbarkableTarget, boat._embarkable);
                        if ((pass == 0) != assigned) continue;
                        if (chosen == null || k.GetInstanceID() < chosen.GetInstanceID()) chosen = k;
                    }
                    if (chosen == null) continue;
                    var p = new Pair { BoatKey = boat.Pointer, KnightKey = chosen.Pointer, Boat = boat,
                        Knight = chosen, Formation = formation, Root = root, Side = side, Deadline = Time.time + 60f };
                    Boats.Add(p.BoatKey, p); Knights.Add(p.KnightKey, p);
                    selected.Add(boat);
                }
            }
            candidates.RemoveAll(b => !selected.Contains(b));
            if (!LoggedPlan)
            {
                LoggedPlan = true;
                try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[FleetGreekSquads] selected " + candidates.Count + " Greek squad/boat pairs"); } catch { }
            }
        }
        catch { Release(formation); candidates.Clear(); throw; }
    }
    internal static void Complete(Formation formation)
    {
        foreach (var p in Boats.Values) if (Same(p.Formation, formation)) p.Pending = false;
        Prune();
    }
    internal static bool NativeCandidateCanJoin(FleetBoat boat, Side side)
    {
        var previous = NativeProbe;
        NativeProbe = boat;
        try { return boat.CanJoinFormation(Formation.FormationType.PlayerFormation, side); }
        finally { NativeProbe = previous; }
    }
    internal static bool CanRecruitBoat(FleetBoat boat, Formation.FormationType type, Side side)
    {
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth || type != Formation.FormationType.PlayerFormation || Same(NativeProbe, boat)) return true;
        Prune();
        return boat != null && Boats.TryGetValue(boat.Pointer, out var p) && p.Side == side;
    }
    internal static bool Reserved(Knight knight)
    {
        Prune();
        return knight != null && Knights.ContainsKey(knight.Pointer);
    }
    internal static bool Reject(Embarkee unit, Embarkable target)
    {
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth || unit == null || target == null) return false;
        var boat = target.GetComponent<FleetBoat>();
        var knight = unit.GetComponent<Knight>();
        if (boat == null || knight == null) return false; // Main boat and worker/stowaway paths remain native.
        // Preserve existing passengers until their native disembark; don't eject old saves mid-voyage.
        if (unit.IsEmbarked && Same(unit.EmbarkableTarget, target)) return false;
        if (!Greek(knight)) return true;
        Prune();
        if (Boats.TryGetValue(boat.Pointer, out var b) && !Same(b.Knight, knight)) return true;
        if (Knights.TryGetValue(knight.Pointer, out var k) && !Same(k.Boat, boat)) return true;
        return false;
    }

    [HarmonyPatch(typeof(EmbarkableRegistrar), nameof(EmbarkableRegistrar.CalculateEmbarkeeScore))]
    private static class ScorePatch
    {
        [HarmonyPostfix]
        private static void Postfix(EmbarkableRegistrar __instance, int embarkeeIndex, int slotIndex, ref int __result)
        {
            try
            {
                if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth || __instance == null ||
                    embarkeeIndex < 0 || slotIndex < 0 || __instance._tempUnitCache == null || __instance._tempSlotCache == null ||
                    embarkeeIndex >= __instance._tempUnitCache.Count || slotIndex >= __instance._tempSlotCache.Count) return;
                var slot = __instance._tempSlotCache[slotIndex];
                if (slot == null || !Reject(__instance._tempUnitCache[embarkeeIndex], slot.Embarkable)) return;
                __result = 100000; // Actual native ApplyAssignments rejects this sentinel.
                if (!LoggedReject)
                {
                    LoggedReject = true;
                    try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[FleetGreekSquads] excluded ineligible fleet-boat squad assignment"); } catch { }
                }
            }
            catch (Exception e) { Error(e); }
        }
    }
    [HarmonyPatch(typeof(FleetBoat), nameof(FleetBoat.CanJoinFormation))]
    private static class BoatRecruitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(FleetBoat __instance, Formation.FormationType formationType, Side formationSide, ref bool __result)
        {
            if (!__result) return;
            try { __result = CanRecruitBoat(__instance, formationType, formationSide); }
            catch (Exception e) { Error(e); __result = false; }
        }
    }
    [HarmonyPatch(typeof(Knight), nameof(Knight.CanJoinFormation))]
    private static class KnightRecruitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Knight __instance, Formation.FormationType formationType, ref bool __result)
        {
            if (!__result || !ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth || formationType != Formation.FormationType.PlayerFormation) return;
            try { if (Reserved(__instance)) __result = false; }
            catch (Exception e) { Error(e); }
        }
    }
}
