using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Crossbow half of the banner rear rows (2026-10-03 user ruling): the native 2-coin banner
/// activation also carries at most four crossbowmen as their own rear row
/// (foot → native gap → archers → musketeers → crossbowmen). The composite layout, the live
/// unitTypes/units/UnitSpacing/startOffset arrays, the per-formation profile and the directed
/// recruit transaction stay owned by <see cref="PatchWorld_FleetBoatFormation"/> — this file owns
/// only what is crossbow-specific:
///
///  * the row-request gate (an offline/authoritative world that still has at least one owned
///    crossbowman with a live career selection; independent of the musketeer switch and of the
///    musketeer count), and
///  * the candidate policy for the crossbow row: live crossbow identity (never a component guess,
///    never a paid musketeer career) on top of the same native-free gates the musketeer row uses,
///    nearest first, at most four.
///
/// Candidates come from the bounded owned registry in <see cref="CrossbowmanLifecycle"/> (the
/// instances that ever received the crossbow package), never from a scene scan, never from
/// Resources/FindObjectsOfType, and no unit is ever created, promoted or teleported here. Every
/// join still goes through the native Archer.TryRecruit on the owner's directed seat.
/// </summary>
internal static class PatchCrossbowFormation
{
    /// <summary>Rear-row capacity. Fewer owned crossbowmen just leave slots empty.</summary>
    internal const int MaxCrossbows = MusketeerFormationLayout.RowSeats;

    private static readonly List<Archer> Roster = new(MaxCrossbows * 8);
    private static readonly List<Archer> Eligible = new(MaxCrossbows);
    private static readonly RearRowNearestFirst Ranking = new();
    private static readonly HashSet<string> Logged = new();

    /// <summary>
    /// True while this world should reserve the crossbow rear row on an activated banner: mod
    /// enabled, offline authority, current known world. The row is reserved from activation even
    /// with zero crossbowmen (R1): candidates that appear later — a new bow promotion or the load
    /// recompute finishing — join through the existing half-second top-up pass. Never depends on
    /// the musketeer switch or on the current crossbow count; an unfilled row compacts under the
    /// native empty non-Gap rule without spawning anything.
    /// </summary>
    internal static bool RowRequested
    {
        get
        {
            try
            {
                return ModConfig.Enabled != null && ModConfig.Enabled.Value
                    && MusketeerAccess.TrackAllowed;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Live maintenance gate for the crossbow row (top-up / feature-off release), mirroring the
    /// musketeer gate minus the musketeer switches: mod enabled, offline authority, unpaused,
    /// playing.
    /// </summary>
    internal static bool Playing
    {
        get
        {
            try
            {
                if (ModConfig.Enabled == null || !ModConfig.Enabled.Value) return false;
                if (!MusketeerAccess.TrackAllowed) return false;
                if (Time.timeScale <= 0f) return false;
                Managers managers = Managers.Inst;
                return managers != null && managers.game != null
                    && managers.game.state == Game.State.Playing;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Crossbow row gate: live crossbow identity (fail-closed reader) on top of the shared
    /// native-free gate. A paid musketeer career is excluded so a co-marked instance can never
    /// play both rows.
    /// </summary>
    internal static bool IsEligible(Archer archer)
    {
        try
        {
            if (archer == null) return false;
            if (!CrossbowmanLifecycle.IsCrossbowman(archer)) return false;
            if (MusketeerIdentity.IsUnit(archer)) return false;
            return PatchMusketeerFormation.IsFreeForRearRow(archer);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Nearest-first snapshot of the crossbowmen that may take one of the free crossbow seats
    /// right now. Reads only the bounded owned registry and returns at most
    /// <paramref name="freeSlots"/> entries. Invalid candidates are skipped, not waited on.
    /// </summary>
    internal static int Collect(Formation formation, int freeSlots, List<Archer> output)
    {
        if (output == null) return 0;
        output.Clear();
        try
        {
            if (formation == null || formation.transform == null || freeSlots <= 0) return 0;
            if (ModConfig.Enabled == null || !ModConfig.Enabled.Value) return 0;
            float formationX = formation.transform.position.x;
            if (!float.IsFinite(formationX)) return 0;
            int limit = Math.Min(freeSlots, MaxCrossbows);
            Roster.Clear();
            if (CrossbowmanLifecycle.CopyOwnedArchers(Roster) <= 0) return 0;
            Eligible.Clear();
            for (int i = 0; i < Roster.Count; i++)
            {
                Archer archer = Roster[i];
                if (IsEligible(archer)) Eligible.Add(archer);
            }
            if (Eligible.Count == 0) return 0;
            Ranking.X = formationX;
            Eligible.Sort(Ranking);
            for (int i = 0; i < Eligible.Count && i < limit; i++) output.Add(Eligible[i]);
            return output.Count;
        }
        catch (Exception e)
        {
            Log("collect", e);
            output.Clear();
            return 0;
        }
        finally
        {
            Roster.Clear();
            Eligible.Clear();
        }
    }

    /// <summary>
    /// Called by the fleet owner exactly once after a directed crossbow seat was confirmed by
    /// final state. Native TryRecruit runs ConvertToSoldier inside the same call, so the host's
    /// existing reconcile boundary is invoked once to re-assert the crossbow attack package,
    /// rooted skin and banner color against whatever that native step wrote; it never changes
    /// numbers and never adds a clock. Failures stay inside the host reconciler.
    /// </summary>
    internal static void OnSeated(Archer archer)
    {
        try
        {
            if (archer == null || !CrossbowmanLifecycle.IsCrossbowman(archer)) return;
            PatchRoles_Crossbowman.OnArcherEnablePostfix(archer);
        }
        catch (Exception e)
        {
            Log("seated", e);
        }
    }

    private static void Log(string key, Exception e)
    {
        if (!Logged.Add(key)) return;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[CrossbowFormation] " + key + ": " + e.GetType().Name);
        }
        catch { }
    }
}
