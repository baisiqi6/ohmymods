using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Expands the player's one native FleetBoat formation slot to the usable boats on the
/// activated banner side. FleetBoat recruitment and all subsequent behavior stay native.
/// </summary>
public static class PatchWorld_FleetBoatFormation
{
    private const int MaxFleetBoats = 4;
    private const float MultiBoatSpacing = 1f;
    private const float MaintenanceInterval = 0.5f;

    private sealed class FormationProfile
    {
        internal Formation Formation;
        internal IntPtr FormationPointer;
        internal int FormationInstanceId;
        internal int GameObjectInstanceId;
        internal IntPtr WorldPointer;
        internal IntPtr SceneRootPointer;
        internal Formation.UnitTypes[] BaselineTypes;
        internal float[] BaselineSpacing;
        internal int BaselineFleetSlot;
        internal int[] ReservedSlots = Array.Empty<int>();
        internal bool Expanded;
        internal float NextMaintenanceAt;
    }

    private sealed class ActivationState
    {
        internal FormationProfile Profile;
        internal Side RequestedSide;
        internal readonly List<FleetBoat> Candidates = new(MaxFleetBoats);
        internal bool Expanded;
    }

    private sealed class UnregisterState
    {
        internal FormationProfile Profile;
        internal int Slot = -1;
    }

    private static readonly Dictionary<int, FormationProfile> Profiles = new();
    private static readonly Dictionary<int, FormationProfile> ProfilesByGameObject = new();
    private static readonly HashSet<string> LoggedFailures = new();
    private static bool _coordinatorRegistered;
    private static bool _unregisterCanaryLogged;
    private static bool _disableCanaryLogged;

    private static void LogFailureOnce(string key, Exception exception)
    {
        if (!LoggedFailures.Add(key)) return;
        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
            "[FleetBoatFormation] " + key + ": " + exception.GetType().Name);
    }

    private static void RemoveProfile(FormationProfile profile)
    {
        if (profile == null) return;
        Profiles.Remove(profile.FormationInstanceId);
        if (ProfilesByGameObject.TryGetValue(profile.GameObjectInstanceId,
                out FormationProfile mapped)
            && ReferenceEquals(mapped, profile))
        {
            ProfilesByGameObject.Remove(profile.GameObjectInstanceId);
        }
    }

    private static bool TryGetMatchingProfile(Formation formation,
        out FormationProfile profile)
    {
        profile = null;
        if (formation == null) return false;

        try
        {
            int id = formation.GetInstanceID();
            if (!Profiles.TryGetValue(id, out FormationProfile existing)) return false;
            if (existing.Formation == null || existing.FormationPointer != formation.Pointer
                || existing.FormationInstanceId != id)
            {
                RemoveProfile(existing);
                return false;
            }

            profile = existing;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCaptureProfile(Formation formation, World world,
        Transform sceneRoot, out FormationProfile profile)
    {
        profile = null;
        try
        {
            int id = formation.GetInstanceID();
            if (Profiles.TryGetValue(id, out FormationProfile existing))
            {
                if (existing.Formation != null
                    && existing.FormationPointer == formation.Pointer)
                {
                    profile = existing;
                    return true;
                }
                RemoveProfile(existing);
            }

            Il2CppStructArray<Formation.UnitTypes> types = formation.unitTypes;
            Il2CppStructArray<float> spacing = formation.UnitSpacing;
            Il2CppReferenceArray<Formation.IFormationUnit> units = formation.units;
            if (types == null || spacing == null || units == null
                || types.Length == 0 || types.Length != units.Length
                || spacing.Length <= (int)Formation.UnitTypes.FleetBoat)
            {
                return false;
            }

            var baselineTypes = new Formation.UnitTypes[types.Length];
            int fleetSlot = -1;
            int fleetSlots = 0;
            for (int i = 0; i < types.Length; i++)
            {
                baselineTypes[i] = types[i];
                if (types[i] != Formation.UnitTypes.FleetBoat) continue;
                fleetSlot = i;
                fleetSlots++;
            }
            if (fleetSlots != 1) return false;

            var baselineSpacing = new float[spacing.Length];
            for (int i = 0; i < spacing.Length; i++) baselineSpacing[i] = spacing[i];

            profile = new FormationProfile
            {
                Formation = formation,
                FormationPointer = formation.Pointer,
                FormationInstanceId = id,
                GameObjectInstanceId = formation.gameObject.GetInstanceID(),
                WorldPointer = world.Pointer,
                SceneRootPointer = sceneRoot.Pointer,
                BaselineTypes = baselineTypes,
                BaselineSpacing = baselineSpacing,
                BaselineFleetSlot = fleetSlot,
                NextMaintenanceAt = Time.unscaledTime + MaintenanceInterval
            };
            Profiles[id] = profile;
            ProfilesByGameObject[profile.GameObjectInstanceId] = profile;
            return true;
        }
        catch (Exception e)
        {
            LogFailureOnce("profile-capture", e);
            return false;
        }
    }

    private static bool AllUnitsEmpty(Formation formation)
    {
        try
        {
            Il2CppReferenceArray<Formation.IFormationUnit> units = formation?.units;
            if (units == null) return false;
            for (int i = 0; i < units.Length; i++)
            {
                if (units[i] != null) return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryRestoreBaseline(FormationProfile profile, bool requireInactive)
    {
        if (profile == null || profile.Formation == null) return false;
        try
        {
            Formation formation = profile.Formation;
            if (formation.Pointer != profile.FormationPointer
                || (requireInactive && formation.enabled)
                || !AllUnitsEmpty(formation))
            {
                return false;
            }

            var types = new Il2CppStructArray<Formation.UnitTypes>(profile.BaselineTypes.Length);
            for (int i = 0; i < profile.BaselineTypes.Length; i++)
                types[i] = profile.BaselineTypes[i];

            var spacing = new Il2CppStructArray<float>(profile.BaselineSpacing.Length);
            for (int i = 0; i < profile.BaselineSpacing.Length; i++)
                spacing[i] = profile.BaselineSpacing[i];

            var units = new Il2CppReferenceArray<Formation.IFormationUnit>(types.Length);
            // All three assignments are synchronous. Assign the empty units first so native
            // code can never observe a shorter type array paired with longer live unit data.
            formation.units = units;
            formation.unitTypes = types;
            formation.UnitSpacing = spacing;
            profile.ReservedSlots = Array.Empty<int>();
            profile.Expanded = false;
            return true;
        }
        catch (Exception e)
        {
            LogFailureOnce("baseline-restore", e);
            return false;
        }
    }

    private static void RestoreOldInactiveProfile(Formation formation)
    {
        if (!TryGetMatchingProfile(formation, out FormationProfile profile)) return;
        if (TryRestoreBaseline(profile, true)) RemoveProfile(profile);
    }

    private static void PruneStaleProfiles()
    {
        if (Profiles.Count == 0) return;
        var remove = new List<int>(Profiles.Count);
        foreach (KeyValuePair<int, FormationProfile> pair in Profiles)
        {
            FormationProfile profile = pair.Value;
            try
            {
                if (profile == null || profile.Formation == null
                    || profile.Formation.Pointer != profile.FormationPointer)
                {
                    remove.Add(pair.Key);
                    continue;
                }

                if (IsCurrentScene(profile)) continue;
                if (!profile.Formation.enabled && AllUnitsEmpty(profile.Formation))
                {
                    if (TryRestoreBaseline(profile, true)) remove.Add(pair.Key);
                }
                // A valid, still-active profile belongs to an older scene that is in the
                // process of unloading. Keep its baseline until native OnDisable has emptied
                // the formation; dropping it here would orphan the expanded arrays.
            }
            catch
            {
                remove.Add(pair.Key);
            }
        }

        for (int i = 0; i < remove.Count; i++)
        {
            if (Profiles.TryGetValue(remove[i], out FormationProfile profile))
                RemoveProfile(profile);
        }
    }

    private static bool IsCurrentScene(FormationProfile profile)
    {
        try
        {
            Managers managers = Managers.Inst;
            return profile != null && profile.Formation != null
                && profile.Formation.Pointer == profile.FormationPointer
                && managers?.world != null && managers.world.gameLayer != null
                && managers.world.Pointer == profile.WorldPointer
                && managers.world.gameLayer.Pointer == profile.SceneRootPointer
                && profile.Formation.gameObject != null
                && profile.Formation.gameObject.GetInstanceID() == profile.GameObjectInstanceId;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryBuildCandidates(Player player, Formation formation,
        Kingdom kingdom, Transform sceneRoot, Side requestedSide, List<FleetBoat> output)
    {
        output.Clear();
        if (kingdom?.FleetBoats == null || sceneRoot == null) return false;

        var pointers = new HashSet<IntPtr>();
        var boatNumbers = new HashSet<int>();
        try
        {
            for (int i = 0; i < kingdom.FleetBoats.Count; i++)
            {
                FleetBoat boat = kingdom.FleetBoats[i];
                if (boat == null || boat.gameObject == null || boat.transform == null
                    || !boat.gameObject.activeInHierarchy || !boat.enabled
                    || !boat.transform.IsChildOf(sceneRoot) || boat.Side != requestedSide)
                {
                    continue;
                }

                if (boat.Pointer == IntPtr.Zero || !pointers.Add(boat.Pointer)
                    || boat._boatNumber < 1 || boat._boatNumber > MaxFleetBoats
                    || !boatNumbers.Add(boat._boatNumber))
                {
                    return false;
                }
                if (boat.HasFormation || boat._currentFormation != null
                    || boat._fsm == null
                    || !FleetBoat.State.CanJoinFormation(boat._fsm.Current)
                    || !boat.IsAccessible
                    || !boat.CanJoinFormation(Formation.FormationType.PlayerFormation,
                        requestedSide))
                {
                    continue;
                }

                output.Add(boat);
                if (output.Count > MaxFleetBoats) return false;
            }

            // RegisterUnit searches from the array tail. Descending BoatNumber calls therefore
            // place Boat 1..N into increasing slots and give every peer a stable layout.
            output.Sort((left, right) => right._boatNumber.CompareTo(left._boatNumber));
            return true;
        }
        catch (Exception e)
        {
            LogFailureOnce("candidate-snapshot", e);
            output.Clear();
            return false;
        }
    }

    private static bool TryExpand(FormationProfile profile, int count)
    {
        try
        {
            int added = Math.Max(0, count - 1);
            int length = profile.BaselineTypes.Length + added;
            var types = new Il2CppStructArray<Formation.UnitTypes>(length);
            var units = new Il2CppReferenceArray<Formation.IFormationUnit>(length);
            var reserved = count > 0 ? new int[count] : Array.Empty<int>();

            int write = 0;
            for (int read = 0; read < profile.BaselineTypes.Length; read++)
            {
                if (read != profile.BaselineFleetSlot)
                {
                    types[write++] = profile.BaselineTypes[read];
                    continue;
                }

                if (count == 0)
                {
                    types[write++] = Formation.UnitTypes.Gap;
                    continue;
                }

                for (int boat = 0; boat < count; boat++)
                {
                    reserved[boat] = write;
                    types[write++] = Formation.UnitTypes.FleetBoat;
                }
            }
            if (write != length) return false;

            var spacing = new Il2CppStructArray<float>(profile.BaselineSpacing.Length);
            for (int i = 0; i < profile.BaselineSpacing.Length; i++)
                spacing[i] = profile.BaselineSpacing[i];
            if (count >= 2)
                spacing[(int)Formation.UnitTypes.FleetBoat] = MultiBoatSpacing;

            profile.Formation.units = units;
            profile.Formation.unitTypes = types;
            profile.Formation.UnitSpacing = spacing;
            profile.ReservedSlots = reserved;
            profile.Expanded = true;
            profile.NextMaintenanceAt = Time.unscaledTime + MaintenanceInterval;
            return true;
        }
        catch (Exception e)
        {
            LogFailureOnce("layout-expand", e);
            if (AllUnitsEmpty(profile.Formation)) TryRestoreBaseline(profile, false);
            return false;
        }
    }

    private static bool IsCandidateStillValid(FleetBoat boat, Formation formation,
        FormationProfile profile, Side requestedSide)
    {
        try
        {
            return NetworkBigBoss.HasWorldAuth && IsCurrentScene(profile)
                && boat != null && boat.Pointer != IntPtr.Zero
                && boat.gameObject != null && boat.gameObject.activeInHierarchy
                && boat.enabled && boat.transform != null
                && boat.transform.IsChildOf(Managers.Inst.world.gameLayer)
                && boat.Side == requestedSide
                && boat._boatNumber >= 1 && boat._boatNumber <= MaxFleetBoats
                && !boat.HasFormation && boat._currentFormation == null
                && boat._fsm != null
                && FleetBoat.State.CanJoinFormation(boat._fsm.Current)
                && boat.IsAccessible
                && boat.CanJoinFormation(Formation.FormationType.PlayerFormation,
                    requestedSide);
        }
        catch
        {
            return false;
        }
    }

    private static void ConvertEmptyReservedSlotsToGaps(FormationProfile profile)
    {
        if (profile == null || !profile.Expanded || profile.Formation == null) return;
        try
        {
            Il2CppStructArray<Formation.UnitTypes> types = profile.Formation.unitTypes;
            Il2CppReferenceArray<Formation.IFormationUnit> units = profile.Formation.units;
            if (types == null || units == null || types.Length != units.Length) return;

            for (int i = 0; i < profile.ReservedSlots.Length; i++)
            {
                int slot = profile.ReservedSlots[i];
                if (slot < 0 || slot >= types.Length) continue;
                if (units[slot] == null && types[slot] == Formation.UnitTypes.FleetBoat)
                    types[slot] = Formation.UnitTypes.Gap;
            }
        }
        catch (Exception e)
        {
            LogFailureOnce("empty-slot-gap", e);
        }
    }

    private static bool TryEnsureCoordinator(Formation formation)
    {
        try
        {
            if (!_coordinatorRegistered)
            {
                if (!ClassInjector.IsTypeRegisteredInIl2Cpp(
                        typeof(FleetBoatFormationCoordinator)))
                {
                    ClassInjector.RegisterTypeInIl2Cpp(
                        typeof(FleetBoatFormationCoordinator));
                }
                _coordinatorRegistered = true;
            }

            FleetBoatFormationCoordinator coordinator =
                formation.GetComponent<FleetBoatFormationCoordinator>();
            if (coordinator == null)
                coordinator = formation.gameObject.AddComponent<FleetBoatFormationCoordinator>();
            return coordinator != null;
        }
        catch (Exception e)
        {
            LogFailureOnce("coordinator-attach", e);
            return false;
        }
    }

    internal static void TickCoordinator(FleetBoatFormationCoordinator coordinator)
    {
        if (coordinator == null || coordinator.gameObject == null) return;
        FormationProfile profile;
        try
        {
            int gameObjectId = coordinator.gameObject.GetInstanceID();
            if (!ProfilesByGameObject.TryGetValue(gameObjectId, out profile)) return;
        }
        catch { return; }
        if (Time.unscaledTime < profile.NextMaintenanceAt) return;
        profile.NextMaintenanceAt = Time.unscaledTime + MaintenanceInterval;

        if (!IsCurrentScene(profile))
        {
            if (profile.Formation != null && !profile.Formation.enabled
                && AllUnitsEmpty(profile.Formation))
            {
                if (TryRestoreBaseline(profile, true)) RemoveProfile(profile);
            }
            return;
        }

        if (!profile.Formation.enabled && AllUnitsEmpty(profile.Formation))
        {
            if (TryRestoreBaseline(profile, true))
                RemoveProfile(profile);
            return;
        }

        // Disabled or authority loss never hot-shrinks an active formation. Empty slots can
        // still be sealed locally because that does not touch any recruited FleetBoat state.
        ConvertEmptyReservedSlotsToGaps(profile);
    }

    private static int FindReservedSlot(FormationProfile profile,
        Formation.IFormationUnit unit)
    {
        if (profile == null || unit == null || profile.Formation == null) return -1;
        try
        {
            GameObject unitObject = unit.GetGO;
            if (unitObject == null) return -1;
            int unitId = unitObject.GetInstanceID();
            Il2CppReferenceArray<Formation.IFormationUnit> units = profile.Formation.units;
            if (units == null) return -1;

            for (int i = 0; i < profile.ReservedSlots.Length; i++)
            {
                int slot = profile.ReservedSlots[i];
                if (slot < 0 || slot >= units.Length || units[slot] == null) continue;
                GameObject registeredObject = units[slot].GetGO;
                if (registeredObject != null && registeredObject.GetInstanceID() == unitId)
                    return slot;
            }
        }
        catch { }
        return -1;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ActivateFormation))]
    private static class PlayerActivateFormationPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Player __instance, out ActivationState __state)
        {
            __state = new ActivationState();
            if (__instance == null || __instance._formation == null) return;

            Formation formation = __instance._formation;
            // Cleanup must remain available even while the mod is disabled or this peer lacks
            // authority. It only runs after the old formation is inactive and completely empty.
            PruneStaleProfiles();
            RestoreOldInactiveProfile(formation);

            try
            {
                if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth
                    || formation.enabled
                    || formation.GetFormationType != Formation.FormationType.PlayerFormation
                    || !AllUnitsEmpty(formation))
                {
                    return;
                }

                Managers managers = Managers.Inst;
                Kingdom kingdom = managers?.kingdom;
                World world = managers?.world;
                Transform sceneRoot = world?.gameLayer;
                if (managers?.game == null || managers.game.state != Game.State.Playing
                    || kingdom == null || world == null || sceneRoot == null
                    || __instance.gameObject == null || __instance.transform == null
                    || !__instance.transform.IsChildOf(sceneRoot))
                {
                    return;
                }

                if (!TryCaptureProfile(formation, world, sceneRoot,
                        out FormationProfile profile)
                    || profile.Expanded || !TryEnsureCoordinator(formation))
                {
                    return;
                }

                Side requestedSide = Util.SideApproximately(__instance.transform.position.x);
                if (!TryBuildCandidates(__instance, formation, kingdom, sceneRoot,
                        requestedSide, __state.Candidates))
                {
                    return;
                }

                if (!TryExpand(profile, __state.Candidates.Count)) return;
                __state.Profile = profile;
                __state.RequestedSide = requestedSide;
                __state.Expanded = true;
            }
            catch (Exception e)
            {
                LogFailureOnce("activate-prefix", e);
            }
        }

        [HarmonyPostfix]
        private static void Postfix(Player __instance, ActivationState __state)
        {
            if (__state == null || !__state.Expanded || __state.Profile == null) return;
            FormationProfile profile = __state.Profile;
            try
            {
                Formation formation = profile.Formation;
                if (!NetworkBigBoss.HasWorldAuth || !IsCurrentScene(profile)
                    || formation == null || !formation.enabled
                    || formation.side != __state.RequestedSide)
                {
                    return;
                }

                for (int i = 0; i < __state.Candidates.Count; i++)
                {
                    FleetBoat boat = __state.Candidates[i];
                    if (!IsCandidateStillValid(boat, formation, profile,
                            __state.RequestedSide))
                    {
                        continue;
                    }
                    try { boat.TryRecruit(formation); }
                    catch (Exception e) { LogFailureOnce("try-recruit", e); }
                }
            }
            catch (Exception e)
            {
                LogFailureOnce("activate-postfix", e);
            }
            finally
            {
                ConvertEmptyReservedSlotsToGaps(profile);
            }
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, ActivationState __state)
        {
            if (__exception != null && __state != null && __state.Expanded
                && __state.Profile != null && AllUnitsEmpty(__state.Profile.Formation))
            {
                TryRestoreBaseline(__state.Profile, false);
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Formation), nameof(Formation.UnregisterUnit))]
    private static class FormationUnregisterUnitPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Formation __instance,
            Formation.IFormationUnit __0, out UnregisterState __state)
        {
            __state = null;
            if (!TryGetMatchingProfile(__instance, out FormationProfile profile)
                || !profile.Expanded) return;
            int slot = FindReservedSlot(profile, __0);
            if (slot >= 0) __state = new UnregisterState { Profile = profile, Slot = slot };
        }

        [HarmonyPostfix]
        private static void Postfix(UnregisterState __state)
        {
            if (__state == null || __state.Profile == null || __state.Slot < 0) return;
            try
            {
                Formation formation = __state.Profile.Formation;
                if (formation == null || formation.units == null || formation.unitTypes == null
                    || __state.Slot >= formation.units.Length
                    || __state.Slot >= formation.unitTypes.Length
                    || formation.units[__state.Slot] != null
                    || formation.unitTypes[__state.Slot] != Formation.UnitTypes.FleetBoat)
                {
                    return;
                }

                formation.unitTypes[__state.Slot] = Formation.UnitTypes.Gap;
                if (!_unregisterCanaryLogged)
                {
                    _unregisterCanaryLogged = true;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        "[FleetBoatFormation] UnregisterUnit canary hit; empty boat slot sealed.");
                }
            }
            catch (Exception e)
            {
                LogFailureOnce("unregister-postfix", e);
            }
        }
    }

    [HarmonyPatch(typeof(Formation), nameof(Formation.OnDisable))]
    private static class FormationOnDisablePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Formation __instance)
        {
            if (!TryGetMatchingProfile(__instance, out FormationProfile profile)
                || !AllUnitsEmpty(__instance)) return;
            if (!TryRestoreBaseline(profile, false)) return;

            RemoveProfile(profile);
            if (!_disableCanaryLogged)
            {
                _disableCanaryLogged = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[FleetBoatFormation] Formation.OnDisable canary hit; native layout restored.");
            }
        }
    }
}

/// <summary>
/// Local-only guard for at most four reserved slots on one player's Formation.
/// It owns no network or persistent state.
/// </summary>
public sealed class FleetBoatFormationCoordinator : MonoBehaviour
{
    public FleetBoatFormationCoordinator(IntPtr pointer) : base(pointer) { }

    private void Update()
    {
        PatchWorld_FleetBoatFormation.TickCoordinator(this);
    }

    private void OnDisable()
    {
        PatchWorld_FleetBoatFormation.TickCoordinator(this);
    }
}
