// parse probe: PipelineTests.cs header (lines 1-113 verbatim) + closing brace
using System;
using System.Collections.Generic;
using Harness;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using KingdomEnhancedMod;
using UnityEngine;

internal static class PipelineTests
{
    private const int RowLength = 4;
    private const int FirstArcherRead = 3;   // Baseline24: FleetBoat, Gap, Gap, then the bow line

    internal static void Run()
    {
        Composition();
        Recruitment();
        TransactionFailures();
        Lifecycle();
        FeatureSwitch();
        Ghosts();
        AuthorityAndScene();
    }

    // ---- helpers ------------------------------------------------------------

    private static void Activate(int boats, int musketeers, Side side = Side.Right, bool featureOn = true)
    {
        Fixture.Reset(side == Side.Right ? 10f : -10f);
        ModConfig.MusketeerEnabled.Value = featureOn;
        for (int i = 1; i <= boats; i++) Fixture.AddBoat(i, side);
        for (int i = 0; i < musketeers; i++) Fixture.AddMusketeer(20f + i * 2f);
        Fixture.Player.ActivateFormation();
    }

    /// <summary>First composite slot of the musketeer row: the fleet footprint plus the two native gaps.</summary>
    private static int RowStart(int boats) => (boats > 0 ? boats : 1) + 2;

    private static int RowSeat(int boats, int index) => RowStart(boats) + index;

    /// <summary>
    /// Expected composite seat map: the fleet block, the native gaps, the Squire row, then the
    /// baseline bow line onward (see MusketeerFormationLayout.TryCompose).
    /// </summary>
    private static void AssertSeatMap(int boats, bool row)
    {
        Formation formation = Fixture.Formation;
        int expected = Fixture.Baseline24.Length + (boats > 0 ? boats - 1 : 0) + (row ? RowLength : 0);
        Check.Equal(expected, formation.unitTypes.Length, "composite length");
        int write = 0;
        if (boats > 0)
        {
            for (int boat = 0; boat < boats; boat++)
                Check.Equal(Formation.UnitTypes.FleetBoat, formation.unitTypes[write++], "boat seat");
        }
        else
        {
            Check.Equal(Formation.UnitTypes.Gap, formation.unitTypes[write++], "closed fleet seat");
        }
        for (int i = 0; i < 2; i++)
            Check.Equal(Formation.UnitTypes.Gap, formation.unitTypes[write++], "native gap");
        if (row)
        {
            for (int i = 0; i < RowLength; i++)
                Check.Equal(Formation.UnitTypes.Squire, formation.unitTypes[write++], "row seat type");
        }
        for (int read = FirstArcherRead; read < Fixture.Baseline24.Length; read++)
            Check.Equal(Fixture.Baseline24[read], formation.unitTypes[write++], "baseline order");
        Check.Equal(write, formation.unitTypes.Length, "no extra seats");
    }

    /// <summary>Composite index of every original baseline seat (the row is inserted before the bow line).</summary>
    private static int[] BaselineSeatMap(int boats, bool row)
    {
        int fleetFootprint = boats > 0 ? boats : 1;
        var map = new int[Fixture.Baseline24.Length];
        for (int read = 0; read < Fixture.Baseline24.Length; read++)
        {
            int before = read == 0 ? 0 : fleetFootprint + (read - 1);
            map[read] = before + (row && read >= FirstArcherRead ? RowLength : 0);
        }
        return map;
    }

    private static int CountOccupied(int from, int count)
    {
        int total = 0;
        for (int i = from; i < from + count; i++)
        {
            if (Fixture.Formation.units[i] != null) total++;
        }
        return total;
    }

    private static float[] SnapshotPositions()
    {
        var result = new float[Fixture.Formation.unitTypes.Length];
        for (int i = 0; i < result.Length; i++) result[i] = Fixture.Formation.GetXPosForIndex(i);
        return result;
    }

    private static List<Archer> RowMembers(int boats)
    {
        var members = new List<Archer>();
        for (int seat = 0; seat < RowLength; seat++)
        {
            if (Fixture.Formation.units[RowSeat(boats, seat)] is Archer archer) members.Add(archer);
        }
        return members;
    }

    /// <summary>Index-direction coordinate: larger values sit closer to the bow line on both sides.</summary>
    private static float Frontward(float x, Side side) => side == Side.Left ? -x : x;
}
