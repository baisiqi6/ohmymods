// parse probe: PipelineTests.cs Composition method (lines 115-230 verbatim)
internal static class PipelineTests
{
    private static void Composition()
    {
        Case.Run("activation composes the 2.4 seat map for 0..4 boats and 0..4 musketeers", () =>
        {
            for (int boats = 0; boats <= 4; boats++)
            {
                for (int muskets = 0; muskets <= 4; muskets++)
                {
                    Activate(boats, muskets);
                    AssertSeatMap(boats, true);
                    int[] map = BaselineSeatMap(boats, true);
                    for (int seat = 0; seat < RowLength; seat++)
                    {
                        Check.Equal(seat >= RowLength - muskets,
                            Fixture.Formation.units[RowSeat(boats, seat)] != null,
                            "row occupancy seat=" + seat + " boats=" + boats + " muskets=" + muskets);
                    }
                    for (int i = 1; i < Fixture.Baseline24.Length; i++)
                    {
                        Check.True(Fixture.Formation.units[map[i]] == null,
                            "original seat must stay free (slot " + i + ", boats=" + boats + ")");
                    }
                    if (boats > 0)
                    {
                        Check.Equal(boats, CountOccupied(map[0], boats), "boats seated");
                    }
                    else
                    {
                        Check.True(Fixture.Formation.units[map[0]] == null, "closed fleet seat stays empty");
                    }
                }
            }
        });

        Case.Run("a full row keeps the bow line exact and moves the fleet block back one row", () =>
        {
            foreach (Side side in new[] { Side.Right, Side.Left })
            {
                foreach (int boats in new[] { 0, 1, 4 })
                {
                    Activate(boats, 4, side);
                    float[] withRow = SnapshotPositions();
                    int[] withMap = BaselineSeatMap(boats, true);
                    float rowStep = Fixture.Formation.UnitSpacing[(int)Formation.UnitTypes.Squire];
                    Check.Near(Fixture.Spacing24[(int)Formation.UnitTypes.Archer], rowStep, 1e-6,
                        "row step is written to the Squire entry");
                    float fleetShift = RowLength * rowStep;

                    Activate(boats, 4, side, featureOn: false);
                    float[] noRow = SnapshotPositions();
                    int[] noMap = BaselineSeatMap(boats, false);

                    Check.Equal(withRow.Length, noRow.Length + RowLength, "row adds exactly four seats");
                    for (int i = FirstArcherRead; i < Fixture.Baseline24.Length; i++)
                    {
                        Check.Near(Frontward(noRow[noMap[i]], side), Frontward(withRow[withMap[i]], side), 1e-4,
                            "full row must keep the bow line at the baseline (side=" + side
                            + ", boats=" + boats + ", slot=" + i + ")");
                    }
                    for (int i = 0; i < FirstArcherRead; i++)
                    {
                        Check.Near(Frontward(noRow[noMap[i]], side) - fleetShift,
                            Frontward(withRow[withMap[i]], side), 1e-4,
                            "fleet block must move back one row (side=" + side + ", boats=" + boats
                            + ", slot=" + i + ")");
                    }

                    float archer1 = Frontward(withRow[withMap[FirstArcherRead]], side);
                    float nearest = Frontward(withRow[RowSeat(boats, RowLength - 1)], side);
                    Check.Near(rowStep, archer1 - nearest, 1e-4,
                        "the nearest musketeer is one row step from the bow line (side=" + side
                        + ", boats=" + boats + ")");
                    for (int seat = 0; seat < RowLength; seat++)
                    {
                        Check.True(Frontward(withRow[RowSeat(boats, seat)], side) < archer1 - 1e-4,
                            "row seats stay behind the bow line (side=" + side + ", seat=" + seat + ")");
                    }
                }
            }
        });

        Case.Run("an unfilled row pulls the bow line toward the fleet and stays one step away", () =>
        {
            foreach (Side side in new[] { Side.Right, Side.Left })
            {
                foreach (int boats in new[] { 0, 1, 2, 4 })
                {
                    Activate(boats, 4, side, featureOn: false);
                    float[] noRow = SnapshotPositions();
                    int[] noMap = BaselineSeatMap(boats, false);
                    float baselineArcher1 = Frontward(noRow[noMap[FirstArcherRead]], side);

                    for (int muskets = 0; muskets <= RowLength; muskets++)
                    {
                        Activate(boats, muskets, side);
                        float[] withRow = SnapshotPositions();
                        int[] withMap = BaselineSeatMap(boats, true);
                        float rowStep = Fixture.Formation.UnitSpacing[(int)Formation.UnitTypes.Squire];
                        Check.Near(Fixture.Spacing24[(int)Formation.UnitTypes.Archer], rowStep, 1e-6,
                            "row step is written to the Squire entry");

                        float archer1 = Frontward(withRow[withMap[FirstArcherRead]], side);
                        Check.Near(baselineArcher1 - (RowLength - muskets) * rowStep, archer1, 1e-4,
                            "missing seats pull the bow line toward the fleet (side=" + side
                            + ", boats=" + boats + ", muskets=" + muskets + ")");
                        if (muskets == 0) continue;
                        float nearest = Frontward(withRow[RowSeat(boats, RowLength - 1)], side);
                        Check.Near(rowStep, archer1 - nearest, 1e-4,
                            "the nearest musketeer is one row step from the bow line (side=" + side
                            + ", boats=" + boats + ", muskets=" + muskets + ")");
                    }
                }
            }
        });
    }
}
