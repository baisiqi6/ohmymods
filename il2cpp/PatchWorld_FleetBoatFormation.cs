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
        internal float BaselineStartOffset;
        internal int[] ReservedSlots = Array.Empty<int>();
        internal int[] MusketeerSlots = Array.Empty<int>();
        internal bool MusketeerRow;
        internal readonly List<MusketeerRollback> PendingRollbacks = new();
        internal bool Expanded;
        internal float NextMaintenanceAt;
    }

    /// <summary>
    /// Coordinator receipt for one directed-recruit step that could not be fully unwound. It is
    /// retried by the maintenance pass instead of being forgotten: the seat half releases a
    /// half-registered archer, the type half returns cells this transaction still owns.
    /// </summary>
    private sealed class MusketeerRollback
    {
        internal int Slot = -1;
        internal int GameObjectInstanceId;
        internal long Lease;
        internal Archer SeatlessActor;
        internal IntPtr TypesPointer;
        internal Formation.UnitTypes[] TypesBefore;
        internal Formation.UnitTypes[] TypesWritten;
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
    private static readonly HashSet<string> LoggedInfo = new();
    private static readonly List<Archer> MusketeerCandidates =
        new(PatchMusketeerFormation.MaxMusketeers);
    private static bool _coordinatorRegistered;
    private static bool _unregisterCanaryLogged;
    private static bool _disableCanaryLogged;

    private static void LogFailureOnce(string key, Exception exception)
    {
        if (!LoggedFailures.Add(key)) return;
        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
            "[FleetBoatFormation] " + key + ": " + exception.GetType().Name);
    }

    private static void LogInfoOnce(string key, string message)
    {
        if (!LoggedInfo.Add(key)) return;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[MusketeerFormation] " + message);
        }
        catch { }
    }

    private static void RemoveProfile(FormationProfile profile)
    {
        if (profile == null) return;
        FleetGreekSquads.Release(profile.Formation);
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
                BaselineStartOffset = formation.startOffset,
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
            // All four assignments are synchronous. Assign the empty units first so native
            // code can never observe a shorter type array paired with longer live unit data.
            formation.units = units;
            formation.unitTypes = types;
            formation.UnitSpacing = spacing;
            formation.startOffset = profile.BaselineStartOffset;
            profile.ReservedSlots = Array.Empty<int>();
            profile.MusketeerSlots = Array.Empty<int>();
            profile.MusketeerRow = false;
            profile.PendingRollbacks.Clear();
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
                    || !FleetGreekSquads.NativeCandidateCanJoin(boat, requestedSide))
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

    private static bool TryExpand(FormationProfile profile, int count, bool musketeerRow)
    {
        try
        {
            // A row slot's per-step distance is the Squire entry written below (one archer step),
            // so the nearest musketeer is exactly one normal queue step from the bow line whatever
            // the fleet block looks like. A non-positive/non-finite value would collapse the row
            // onto the bow line: fail closed and keep the fleet-only layout instead.
            float rowSpacing = profile.BaselineSpacing[(int)Formation.UnitTypes.Archer];
            bool rowUsable = musketeerRow && float.IsFinite(rowSpacing) && rowSpacing > 0f;
            if (musketeerRow && !rowUsable)
                LogInfoOnce("row-spacing-unusable", "rear row skipped: Archer spacing is not usable");

            if (!MusketeerFormationLayout.TryCompose(profile.BaselineTypes, count, rowUsable,
                    out Formation.UnitTypes[] plannedTypes, out int[] boatSlots,
                    out int[] musketeerSlots, out int rowLength))
            {
                return false;
            }

            var types = new Il2CppStructArray<Formation.UnitTypes>(plannedTypes.Length);
            for (int i = 0; i < plannedTypes.Length; i++) types[i] = plannedTypes[i];
            var units = new Il2CppReferenceArray<Formation.IFormationUnit>(plannedTypes.Length);

            var spacing = new Il2CppStructArray<float>(profile.BaselineSpacing.Length);
            for (int i = 0; i < profile.BaselineSpacing.Length; i++)
                spacing[i] = profile.BaselineSpacing[i];
            if (count >= 2)
                spacing[(int)Formation.UnitTypes.FleetBoat] = MultiBoatSpacing;
            // Row seats advertise Squire; that entry is the row step, so an occupied seat counts
            // exactly one archer step (empty row seats count nothing — native compaction rule).
            if (rowLength > 0)
                spacing[(int)Formation.UnitTypes.Squire] = rowSpacing;

            profile.Formation.units = units;
            profile.Formation.unitTypes = types;
            profile.Formation.UnitSpacing = spacing;
            // Shift the origin by exactly the new row's steps: with a full row every archer-down
            // slot keeps its old coordinate and the fleet block moves back by the row; an unfilled
            // row compacts toward the fleet block (see layout planner).
            profile.Formation.startOffset = profile.BaselineStartOffset - rowLength * rowSpacing;
            profile.ReservedSlots = boatSlots;
            profile.MusketeerSlots = musketeerSlots;
            profile.MusketeerRow = rowLength > 0;
            profile.Expanded = true;
            profile.NextMaintenanceAt = Time.unscaledTime + MaintenanceInterval;
            if (profile.MusketeerRow)
                LogInfoOnce("row-reserved", "reserved " + rowLength + " rear slots for musketeers");
            else if (rowUsable)
                LogInfoOnce("row-no-archer-slot", "rear row skipped: baseline has no archer slot");
            return true;
        }
        catch (Exception e)
        {
            LogFailureOnce("layout-expand", e);
            if (AllUnitsEmpty(profile.Formation)) TryRestoreBaseline(profile, false);
            return false;
        }
    }

    /// <summary>True while this exact formation has a live reserved musketeer row.</summary>
    internal static bool HasMusketeerRow(Formation formation)
    {
        try
        {
            return TryGetMatchingProfile(formation, out FormationProfile profile)
                && profile.Expanded && profile.MusketeerRow;
        }
        catch
        {
            return false;
        }
    }

    private static int CountFreeMusketeerSlots(FormationProfile profile)
    {
        try
        {
            if (profile == null || !profile.MusketeerRow || profile.Formation == null) return 0;
            Il2CppReferenceArray<Formation.IFormationUnit> units = profile.Formation.units;
            if (units == null) return 0;
            int free = 0;
            for (int i = 0; i < profile.MusketeerSlots.Length; i++)
            {
                int slot = profile.MusketeerSlots[i];
                if (slot >= 0 && slot < units.Length && units[slot] == null) free++;
            }
            return free;
        }
        catch
        {
            return 0;
        }
    }

    // Fill from the bow side of the row (highest slot index) so occupied musketeers always line up
    // adjacent to the archers and empty seats stay on the fleet side; an empty Squire row slot
    // does not count in the native position accumulation, so an unfilled row compacts the bow line
    // toward the fleet block by the missing steps instead of leaving a hole (see the planner).
    private static int NextFreeMusketeerSlot(FormationProfile profile)
    {
        try
        {
            if (profile == null || profile.Formation == null) return -1;
            Il2CppReferenceArray<Formation.IFormationUnit> units = profile.Formation.units;
            if (units == null) return -1;
            for (int i = profile.MusketeerSlots.Length - 1; i >= 0; i--)
            {
                int slot = profile.MusketeerSlots[i];
                if (slot >= 0 && slot < units.Length && units[slot] == null) return slot;
            }
        }
        catch { }
        return -1;
    }

    /// <summary>
    /// Maintenance half of the rear row. Runs on the activation postfix and on the existing
    /// half-second coordinator pass, and does exactly three bounded things:
    ///  * retries receipts left by a directed recruit that could not be fully unwound,
    ///  * cleans only the reserved seats (a member whose feature was switched off, died or lost
    ///    its identity is released through native UnregisterUnit; a seat holding an archer that
    ///    already belongs to another formation, an unmarked non-member or a stale/destroyed
    ///    source only loses this array's reference and is never sent through OnLeave),
    ///  * tops the row back up while the feature is on.
    /// Authority loss and scene changes never write here; the existing inactive+empty baseline
    /// restore stays the only path for those states.
    /// </summary>
    private static void ReconcileMusketeerRow(FormationProfile profile)
    {
        try
        {
            if (profile == null || !profile.Expanded || !profile.MusketeerRow) return;
            Formation formation = profile.Formation;
            if (formation == null || !formation.enabled || !IsCurrentScene(profile)) return;
            if (!LiveMusketeerWorld) return;

            RetryPendingMusketeerRollbacks(profile);

            Il2CppReferenceArray<Formation.IFormationUnit> units = formation.units;
            if (units != null)
            {
                for (int i = 0; i < profile.MusketeerSlots.Length; i++)
                {
                    int slot = profile.MusketeerSlots[i];
                    if (slot < 0 || slot >= units.Length || units[slot] == null) continue;
                    if (HasPendingMusketeerRollback(profile, slot)) continue;   // the receipt owns that seat
                    TryCleanupMusketeerSeat(profile, slot, 0, 0L);
                }
            }

            TopUpMusketeers(profile);
        }
        catch (Exception e)
        {
            LogFailureOnce("musketeer-reconcile", e);
        }
    }

    /// <summary>
    /// Live, offline, authoritative, unpaused world with the game running: the only state in which
    /// this owner mutates unit state. Co-op and menu pause leave the row untouched until the world
    /// is live again (or until the native furl path restores the baseline).
    /// </summary>
    private static bool LiveMusketeerWorld
    {
        get
        {
            try
            {
                Managers managers = Managers.Inst;
                return NetworkBigBoss.HasWorldAuth && !NetworkBigBoss.IsOnline
                    && Time.timeScale > 0f
                    && managers != null && managers.game != null
                    && managers.game.state == Game.State.Playing;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>The musketeer switch (or the whole mod) is definitively off in the config.</summary>
    private static bool MusketeerFeatureOff
    {
        get
        {
            try
            {
                if (ModConfig.Enabled == null || ModConfig.MusketeerEnabled == null) return false;
                return !ModConfig.Enabled.Value || !ModConfig.MusketeerEnabled.Value;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// A live member of this row is released when the feature is off, when it died, or when the
    /// identity registry no longer marks it (the seat belongs to musketeers, not to a ghost).
    /// </summary>
    private static bool ShouldReleaseMusketeerSeat(Archer archer)
    {
        try
        {
            if (MusketeerFeatureOff) return true;
            Damageable damageable = archer._damageable;
            if (damageable == null || damageable.isDead) return true;
            return !MusketeerIdentity.IsUnit(archer);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ownership-safe cleanup for one reserved seat. Returns true when the seat needs no further
    /// work (already empty, released now, legitimately occupied, or held by an occupant this owner
    /// must not touch); false keeps a pending receipt for a later maintenance retry.
    /// expectedInstanceId 0 classifies whatever this owned seat currently holds. A seat without a
    /// formation binding is only sent through the native leave path when the lease captured at
    /// recruit time still matches (same life); otherwise the actor may be a pooled re-arm of the
    /// same GameObject, so only this array's stale reference is dropped.
    /// </summary>
    private static bool TryCleanupMusketeerSeat(FormationProfile profile, int slot, int expectedInstanceId,
        long capturedLease)
    {
        try
        {
            Formation formation = profile != null ? profile.Formation : null;
            if (formation == null || formation.Pointer != profile.FormationPointer) return true;
            if (!NetworkBigBoss.HasWorldAuth || NetworkBigBoss.IsOnline) return false;
            if (!IsCurrentScene(profile)) return false;

            Il2CppReferenceArray<Formation.IFormationUnit> units = formation.units;
            if (units == null || slot < 0 || slot >= units.Length) return true;
            Formation.IFormationUnit occupant = units[slot];
            if (occupant == null) return true;

            GameObject gameObject = occupant.GetGO;
            if (gameObject == null)
                return TryDropMusketeerSeatReference(profile, units, slot, 0);
            int instanceId = gameObject.GetInstanceID();
            if (expectedInstanceId > 0 && instanceId != expectedInstanceId) return true;   // seat reused: not ours
            if (!gameObject.activeInHierarchy)
                return TryDropMusketeerSeatReference(profile, units, slot, instanceId);
            if (!gameObject.TryGetComponent<Archer>(out Archer archer) || archer == null)
                return TryDropMusketeerSeatReference(profile, units, slot, instanceId);

            Formation current = archer.GetFormation();
            if (current != null)
            {
                if (current.Pointer != formation.Pointer)
                    return TryDropMusketeerSeatReference(profile, units, slot, instanceId);   // foreign owner: never OnLeave
                if (!ShouldReleaseMusketeerSeat(archer)) return true;                          // legitimate member
            }
            else if (!SameMusketeerLife(archer, capturedLease))
            {
                // No formation binding and no same-life proof: the same GameObject may have been
                // re-armed as a new life, so only this array's stale reference goes away and the
                // actor itself is never OnLeave-ed.
                return TryDropMusketeerSeatReference(profile, units, slot, instanceId);
            }

            formation.UnregisterUnit(occupant);     // native leave: clears the seat + OnLeaveFormation
            Il2CppReferenceArray<Formation.IFormationUnit> after = formation.units;
            return after == null || slot >= after.Length || after[slot] == null;
        }
        catch (Exception e)
        {
            LogFailureOnce("musketeer-seat", e);
            return false;
        }
    }

    /// <summary>
    /// Drops this array's reference to a stale occupant without touching the occupant itself.
    /// Never writes when another owner already replaced the array or the seat content changed.
    /// </summary>
    private static bool TryDropMusketeerSeatReference(FormationProfile profile,
        Il2CppReferenceArray<Formation.IFormationUnit> observed, int slot, int instanceId)
    {
        try
        {
            Formation formation = profile != null ? profile.Formation : null;
            if (formation == null) return true;
            Il2CppReferenceArray<Formation.IFormationUnit> current = formation.units;
            if (current == null || observed == null || current.Pointer != observed.Pointer) return true;
            if (slot < 0 || slot >= current.Length) return true;
            Formation.IFormationUnit occupant = current[slot];
            if (occupant == null) return true;
            if (instanceId > 0)
            {
                GameObject gameObject = occupant.GetGO;
                if (gameObject == null || gameObject.GetInstanceID() != instanceId) return true;
            }
            current[slot] = null;
            return true;
        }
        catch (Exception e)
        {
            LogFailureOnce("musketeer-seat", e);
            return false;
        }
    }

    private static bool HasPendingMusketeerRollback(FormationProfile profile, int slot)
    {
        for (int i = 0; i < profile.PendingRollbacks.Count; i++)
        {
            MusketeerRollback pending = profile.PendingRollbacks[i];
            if (pending != null && pending.Slot == slot) return true;
        }
        return false;
    }

    private static void QueueMusketeerRollback(FormationProfile profile, int slot, int instanceId,
        long lease, Archer seatlessActor, IntPtr typesPointer,
        Formation.UnitTypes[] typesBefore, Formation.UnitTypes[] typesWritten)
    {
        if (profile == null) return;
        try
        {
            for (int i = 0; i < profile.PendingRollbacks.Count; i++)
            {
                MusketeerRollback existing = profile.PendingRollbacks[i];
                if (existing.Slot == slot && existing.GameObjectInstanceId == instanceId
                    && existing.TypesPointer == typesPointer && existing.Lease == lease)
                {
                    if (existing.SeatlessActor == null) existing.SeatlessActor = seatlessActor;
                    return;
                }
            }
            profile.PendingRollbacks.Add(new MusketeerRollback
            {
                Slot = slot,
                GameObjectInstanceId = instanceId,
                Lease = lease,
                SeatlessActor = seatlessActor,
                TypesPointer = typesPointer,
                TypesBefore = typesBefore,
                TypesWritten = typesWritten
            });
            LogInfoOnce("rollback-pending", "directed recruit rollback pending; maintenance will retry");
        }
        catch { }
    }

    /// <summary>
    /// Retries every outstanding receipt (types CAS restore, then the seat, then a bound-but-seatless
    /// actor whose direct leave failed); a resolved receipt is dropped, the rest stay queued.
    /// </summary>
    private static void RetryPendingMusketeerRollbacks(FormationProfile profile)
    {
        for (int i = profile.PendingRollbacks.Count - 1; i >= 0; i--)
        {
            MusketeerRollback pending = profile.PendingRollbacks[i];
            if (pending == null)
            {
                profile.PendingRollbacks.RemoveAt(i);
                continue;
            }
            bool resolved = true;
            if (pending.TypesBefore != null && pending.TypesWritten != null)
            {
                resolved = TryRestoreDirectedTypes(profile, pending.TypesPointer,
                    pending.TypesBefore, pending.TypesWritten, pending.TypesBefore.Length);
            }
            if (resolved && pending.Slot >= 0)
                resolved = TryCleanupMusketeerSeat(profile, pending.Slot, pending.GameObjectInstanceId, pending.Lease);
            if (resolved && pending.SeatlessActor != null)
                resolved = TryFinishMusketeerSeatlessLeave(profile, pending);
            if (resolved) profile.PendingRollbacks.RemoveAt(i);
        }
    }

    /// <summary>
    /// True while this formation still owes a temporary-type restore (a receipt with a type
    /// snapshot is pending). While dirty, a leftover Archer-typed row seat is open, so every other
    /// Archer recruit for this formation is refused until the CAS restore completes or the array is
    /// confirmed to belong to another owner. Scoped to this formation only.
    /// </summary>
    internal static bool HasDirtyMusketeerTypes(Formation formation)
    {
        try
        {
            if (!TryGetMatchingProfile(formation, out FormationProfile profile)) return false;
            for (int i = 0; i < profile.PendingRollbacks.Count; i++)
            {
                MusketeerRollback pending = profile.PendingRollbacks[i];
                if (pending != null && pending.TypesBefore != null && pending.TypesWritten != null)
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static long BindingLeaseOf(Archer archer)
    {
        try { return MusketeerRuntime.BindingLease(archer); }
        catch { return 0L; }
    }

    /// <summary>
    /// Same-life proof for a half-registered seat: only the exact lease captured at recruit time
    /// may be sent through the native leave path. A different (or unknown) lease may be a pooled
    /// re-arm of the same GameObject and only loses this array's stale reference.
    /// </summary>
    private static bool SameMusketeerLife(Archer archer, long capturedLease)
    {
        try
        {
            return capturedLease > 0L && MusketeerRuntime.MatchesBindingLease(archer, capturedLease);
        }
        catch
        {
            return false;
        }
    }

    private static int SafeInstanceId(Archer archer)
    {
        try { return archer != null && archer.gameObject != null ? archer.gameObject.GetInstanceID() : 0; }
        catch { return 0; }
    }

    /// <summary>True while the reserved seat still holds the given GameObject instance.</summary>
    private static bool MusketeerSeatHolds(FormationProfile profile, int slot, int instanceId)
    {
        try
        {
            Formation formation = profile != null ? profile.Formation : null;
            Il2CppReferenceArray<Formation.IFormationUnit> units = formation != null ? formation.units : null;
            if (units == null || instanceId <= 0 || slot < 0 || slot >= units.Length) return false;
            Formation.IFormationUnit occupant = units[slot];
            if (occupant == null) return false;
            GameObject gameObject = occupant.GetGO;
            return gameObject != null && gameObject.GetInstanceID() == instanceId;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True while the archer's current formation is exactly the managed one.</summary>
    private static bool MusketeerOwnsFormation(Archer archer, FormationProfile profile)
    {
        try
        {
            Formation formation = profile != null ? profile.Formation : null;
            if (formation == null || formation.Pointer != profile.FormationPointer) return false;
            Formation current = archer.GetFormation();
            return current != null && current.Pointer == formation.Pointer;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Retries the bound-but-seatless half of a failed transaction: another owner replaced the
    /// arrays, so native UnregisterUnit has no seat left to clear, and the direct leave failed.
    /// The captured actor is released only while its captured lease still matches (same life) and
    /// its current formation is exactly this one; an actor that is seated again, has a different
    /// life, is foreign or is already free is left alone. Returns true when the receipt no longer
    /// needs to be kept (released now, already resolved, or never ours to touch).
    /// </summary>
    private static bool TryFinishMusketeerSeatlessLeave(FormationProfile profile, MusketeerRollback pending)
    {
        try
        {
            Archer archer = pending.SeatlessActor;
            if (archer == null) return true;                                 // destroyed: nothing left to release
            Formation formation = profile != null ? profile.Formation : null;
            if (formation == null || formation.Pointer != profile.FormationPointer) return true;
            if (!NetworkBigBoss.HasWorldAuth || NetworkBigBoss.IsOnline) return false;
            if (!IsCurrentScene(profile)) return false;
            if (!SameMusketeerLife(archer, pending.Lease)) return true;      // different/unknown life: never OnLeave
            if (MusketeerSeated(profile, pending.GameObjectInstanceId)) return true;   // seated again: keep the member
            Formation current = archer.GetFormation();
            if (current == null || current.Pointer != formation.Pointer) return true;  // already free / foreign
            archer.OnLeaveFormation();
            Formation after = archer.GetFormation();
            return after == null || after.Pointer != formation.Pointer;
        }
        catch (Exception e)
        {
            LogFailureOnce("musketeer-seatless", e);
            return false;
        }
    }

    /// <summary>True while any seat of the managed formation holds the given GameObject instance.</summary>
    private static bool MusketeerSeated(FormationProfile profile, int instanceId)
    {
        try
        {
            Formation formation = profile != null ? profile.Formation : null;
            Il2CppReferenceArray<Formation.IFormationUnit> units = formation != null ? formation.units : null;
            if (units == null || instanceId <= 0) return false;
            for (int i = 0; i < units.Length; i++)
            {
                Formation.IFormationUnit occupant = units[i];
                if (occupant == null) continue;
                GameObject gameObject = occupant.GetGO;
                if (gameObject != null && gameObject.GetInstanceID() == instanceId) return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Bounded top-up of the reserved rear row: the activation-time fill plus the same pass on the
    /// existing half-second maintenance tick, so a musketeer that was grabbed/inert at banner
    /// time can still walk in later and a dead musketeer's seat is refilled by an eligible one.
    /// Never scans the scene: candidates come from the identity career registry only.
    /// </summary>
    private static void TopUpMusketeers(FormationProfile profile)
    {
        try
        {
            if (profile == null || !profile.Expanded || !profile.MusketeerRow) return;
            if (!MusketeerAccess.Playing || !NetworkBigBoss.HasWorldAuth || !IsCurrentScene(profile))
                return;
            Formation formation = profile.Formation;
            if (formation == null || !formation.enabled) return;

            int free = CountFreeMusketeerSlots(profile);
            if (free <= 0) return;
            if (PatchMusketeerFormation.Collect(formation, free, MusketeerCandidates) <= 0) return;

            for (int i = 0; i < MusketeerCandidates.Count; i++)
            {
                Archer archer = MusketeerCandidates[i];
                // Re-check right before the native recruit below mutates unit state; the earlier
                // pass was only the selection snapshot.
                if (!PatchMusketeerFormation.IsEligible(archer)) continue;
                if (!TryDirectedMusketeerRecruit(profile, archer)) continue;
                if (--free <= 0) break;
            }
        }
        catch (Exception e)
        {
            LogFailureOnce("musketeer-topup", e);
        }
    }

    /// <summary>
    /// One directed native recruit into a reserved row seat. The target seat is advertised as
    /// Archer and every other empty seat an Archer could take is closed to Gap, so the marked
    /// musketeer can only land in its own seat. Native TryRecruit writes in order
    /// RegisterUnit -> ConvertToSoldier -> _npcShieldUser -> _currentFormation, so a throw in the
    /// middle can leave the seat registered while the archer is not a member yet: success is
    /// therefore decided by that final state (seat holds this archer AND its GetFormation() is
    /// this formation), never by the return value alone. The temporary type write and the directed
    /// bypass are one transaction - a throw in either half still restores the cells this
    /// transaction actually wrote (only cells that still hold the written value are returned) and
    /// drops the bypass; a step that cannot be unwound leaves a coordinator receipt for the next
    /// maintenance pass instead of being forgotten.
    /// </summary>
    private static bool TryDirectedMusketeerRecruit(FormationProfile profile, Archer archer)
    {
        try
        {
            Formation formation = profile != null ? profile.Formation : null;
            if (formation == null || archer == null) return false;
            if (formation.Pointer != profile.FormationPointer || !IsCurrentScene(profile)) return false;
            int slot = NextFreeMusketeerSlot(profile);
            if (slot < 0) return false;

            // Same-life proof for every rollback path: a lease of 0 means the runtime has no
            // applied package for this actor right now, so it is never recruited (and never
            // OnLeave-ed) on the strength of an unproven life.
            long lease = BindingLeaseOf(archer);
            if (lease <= 0L) return false;

            Il2CppStructArray<Formation.UnitTypes> types = formation.unitTypes;
            Il2CppReferenceArray<Formation.IFormationUnit> units = formation.units;
            if (types == null || units == null || types.Length == 0 || types.Length != units.Length
                || slot >= types.Length || units[slot] != null)
            {
                return false;
            }

            var snapshot = new Formation.UnitTypes[types.Length];
            var occupied = new bool[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                snapshot[i] = types[i];
                occupied[i] = units[i] != null;
            }
            if (!MusketeerFormationLayout.TryPlanDirected(snapshot, occupied, slot,
                    out Formation.UnitTypes[] temporary))
            {
                return false;
            }

            IntPtr arrayPointer = types.Pointer;
            int applied = 0;
            Exception failure = null;
            try
            {
                // The bypass, the temporary types and the real native recruit are ONE step: the
                // guard only lets this exact archer through while the bypass is armed, and the
                // snapshot may only be returned after the native call has completed or thrown.
                PatchMusketeerFormation.BeginDirected(archer, formation);
                for (; applied < types.Length; applied++) types[applied] = temporary[applied];
                try { archer.TryRecruit(formation); }
                catch (Exception e) { failure = e; }
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                if (!TryRestoreDirectedTypes(profile, arrayPointer, snapshot, temporary,
                        Math.Min(applied + 1, snapshot.Length)))
                {
                    QueueMusketeerRollback(profile, -1, 0, 0L, archer, arrayPointer, snapshot, temporary);
                }
                PatchMusketeerFormation.EndDirected();
            }

            if (DirectedSeatJoined(profile, slot, archer))
            {
                LogInfoOnce("row-recruit", "musketeer joined the rear row");
                if (failure != null) LogFailureOnce("musketeer-recruit", failure);
                return true;
            }

            int instanceId = SafeInstanceId(archer);
            bool cleaned = TryCleanupMusketeerSeat(profile, slot, instanceId, lease);
            if (cleaned && !MusketeerSeatHolds(profile, slot, instanceId)
                && SameMusketeerLife(archer, lease) && MusketeerOwnsFormation(archer, profile))
            {
                // Another owner replaced the arrays mid-call: the seat (and therefore native
                // UnregisterUnit) is gone while this transaction's captured life is still bound.
                // The leave call is made directly and verified; when it does not unbind, the
                // seatless half is queued as a receipt (a seat-only receipt would resolve on the
                // empty seat without ever leaving, so that would otherwise be lost).
                bool left = false;
                try
                {
                    archer.OnLeaveFormation();
                    left = !MusketeerOwnsFormation(archer, profile);
                }
                catch (Exception e) { LogFailureOnce("musketeer-seatless", e); }
                if (!left)
                    QueueMusketeerRollback(profile, slot, instanceId, lease, archer, IntPtr.Zero, null, null);
            }
            else if (!cleaned)
            {
                QueueMusketeerRollback(profile, slot, instanceId, lease, archer, IntPtr.Zero, null, null);
            }
            if (failure != null) LogFailureOnce("musketeer-recruit", failure);
            return false;
        }
        catch (Exception e)
        {
            LogFailureOnce("musketeer-recruit", e);
            return false;
        }
    }

    /// <summary>
    /// Returns cells this transaction wrote to their pre-transaction values. Only a cell that
    /// still holds the value this transaction wrote is restored (a cell someone else changed is
    /// theirs); the read-back decides completion so a failure can be retried from the coordinator
    /// receipt, and an array another owner replaced is never written.
    /// </summary>
    private static bool TryRestoreDirectedTypes(FormationProfile profile, IntPtr arrayPointer,
        Formation.UnitTypes[] before, Formation.UnitTypes[] written, int writtenCount)
    {
        try
        {
            Formation formation = profile != null ? profile.Formation : null;
            if (formation == null || formation.Pointer != profile.FormationPointer) return true;
            if (before == null || written == null) return true;
            if (!IsCurrentScene(profile)) return false;

            Il2CppStructArray<Formation.UnitTypes> current = formation.unitTypes;
            if (current == null || current.Pointer != arrayPointer || current.Length != before.Length)
                return true;   // replaced by another owner: nothing of ours is left in it

            int limit = Math.Min(Math.Max(writtenCount, 0), before.Length);
            for (int i = 0; i < limit; i++)
            {
                if (written[i] == before[i] || current[i] != written[i]) continue;
                current[i] = before[i];
            }
            for (int i = 0; i < before.Length; i++)
            {
                if (written[i] == before[i]) continue;
                if (current[i] == written[i]) return false;   // still ours: keep the receipt
            }
            return true;
        }
        catch (Exception e)
        {
            LogFailureOnce("directed-restore", e);
            return false;
        }
    }

    /// <summary>State-based success check: the seat holds this archer and the archer is bound to this formation.</summary>
    private static bool DirectedSeatJoined(FormationProfile profile, int slot, Archer archer)
    {
        try
        {
            Formation formation = profile != null ? profile.Formation : null;
            if (formation == null || formation.Pointer != profile.FormationPointer) return false;
            Il2CppReferenceArray<Formation.IFormationUnit> units = formation.units;
            if (units == null || slot < 0 || slot >= units.Length || units[slot] == null) return false;
            GameObject seated = units[slot].GetGO;
            if (seated == null || seated.GetInstanceID() != SafeInstanceId(archer)) return false;
            Formation current = archer.GetFormation();
            return current != null && current.Pointer == formation.Pointer;
        }
        catch
        {
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
        FleetGreekSquads.Maintain();

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
        ReconcileMusketeerRow(profile);
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

                FleetGreekSquads.Select(formation, sceneRoot, requestedSide, __state.Candidates);
                if (!TryExpand(profile, __state.Candidates.Count,
                        PatchMusketeerFormation.RowRequested))
                {
                    FleetGreekSquads.Release(formation);
                    return;
                }
                __state.Profile = profile;
                __state.RequestedSide = requestedSide;
                __state.Expanded = true;
            }
            catch (Exception e)
            {
                FleetGreekSquads.Release(formation);
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

                ReconcileMusketeerRow(profile);
            }
            catch (Exception e)
            {
                LogFailureOnce("activate-postfix", e);
            }
            finally
            {
                FleetGreekSquads.Complete(profile.Formation);
                ConvertEmptyReservedSlotsToGaps(profile);
            }
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, ActivationState __state)
        {
            if (__exception != null && __state != null && __state.Expanded && __state.Profile != null)
            {
                // Native exceptions skip Postfix. End pending reservations even when the
                // candidate was already embarked (which intentionally has no boarding timeout).
                try { FleetGreekSquads.Complete(__state.Profile.Formation); }
                catch (Exception e) { LogFailureOnce("activation-plan-finalizer", e); }
                if (AllUnitsEmpty(__state.Profile.Formation)) TryRestoreBaseline(__state.Profile, false);
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
