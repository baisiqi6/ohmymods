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
        CrossbowRow();
        TransactionFailures();
        Lifecycle();
        FeatureSwitch();
        Ghosts();
        AuthorityAndScene();
    }

    // ---- helpers ------------------------------------------------------------

    private static void Activate(int boats, int musketeers, Side side = Side.Right, bool featureOn = true,
        int crossbows = 0, bool crossIdentity = false)
    {
        Fixture.Reset(side == Side.Right ? 10f : -10f);
        ModConfig.MusketeerEnabled.Value = featureOn;
        CrossbowmanLifecycle.IdentityEnabled = crossIdentity;
        for (int i = 1; i <= boats; i++) Fixture.AddBoat(i, side);
        for (int i = 0; i < musketeers; i++) Fixture.AddMusketeer(20f + i * 2f);
        for (int i = 0; i < crossbows; i++) Fixture.AddCrossbowman(30f + i * 2f);
        Fixture.Player.ActivateFormation();
    }

    /// <summary>
    /// Row geometry with the cross row always reserved (R1): the fleet footprint plus the two
    /// native gaps lead the crossbow row, the musketeer row follows it.
    /// </summary>
    private static int CrossStart(int boats) => (boats > 0 ? boats : 1) + 2;

    private static int CrossSeat(int boats, int index) => CrossStart(boats) + index;

    /// <summary>First composite slot of the musketeer row (after the crossbow row).</summary>
    private static int RowStart(int boats) => CrossStart(boats) + RowLength;

    private static int RowSeat(int boats, int index) => RowStart(boats) + index;

    private static int MuskSeat2(int boats, int index) => RowStart(boats) + index;

    /// <summary>
    /// Expected composite seat map with any combination of the two rear rows: fleet block, native
    /// gaps, crossbow row (if present), musketeer row (if present), then the baseline bow line.
    /// </summary>
    private static void AssertSeatMap2(int boats, bool muskRow, bool crossRow)
    {
        Formation formation = Fixture.Formation;
        int rows = (muskRow ? RowLength : 0) + (crossRow ? RowLength : 0);
        int expected = Fixture.Baseline24.Length + (boats > 0 ? boats - 1 : 0) + rows;
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
        for (int i = 0; i < rows; i++)
            Check.Equal(Formation.UnitTypes.Squire, formation.unitTypes[write++], "row seat type");
        for (int read = FirstArcherRead; read < Fixture.Baseline24.Length; read++)
            Check.Equal(Fixture.Baseline24[read], formation.unitTypes[write++], "baseline order");
        Check.Equal(write, formation.unitTypes.Length, "no extra seats");
    }

    /// <summary>Composite index map of every original baseline seat for any row combination.</summary>
    private static int[] BaselineSeatMap2(int boats, bool muskRow, bool crossRow)
    {
        int fleetFootprint = boats > 0 ? boats : 1;
        int rows = (muskRow ? RowLength : 0) + (crossRow ? RowLength : 0);
        var map = new int[Fixture.Baseline24.Length];
        for (int read = 0; read < Fixture.Baseline24.Length; read++)
        {
            int before = read == 0 ? 0 : fleetFootprint + (read - 1);
            map[read] = before + (read >= FirstArcherRead ? rows : 0);
        }
        return map;
    }

    /// <summary>
    /// Expected composite seat map: the fleet block, the native gaps, the Squire row, then the
    /// baseline bow line onward (see MusketeerFormationLayout.TryCompose).
    /// </summary>
    private static void AssertSeatMap(int boats, bool row)
    {
        Formation formation = Fixture.Formation;
        int expected = Fixture.Baseline24.Length + (boats > 0 ? boats - 1 : 0) + RowLength + (row ? RowLength : 0);
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
        for (int i = 0; i < RowLength; i++)
            Check.Equal(Formation.UnitTypes.Squire, formation.unitTypes[write++], "cross seat type");
        if (row)
        {
            for (int i = 0; i < RowLength; i++)
                Check.Equal(Formation.UnitTypes.Squire, formation.unitTypes[write++], "musk seat type");
        }
        for (int read = FirstArcherRead; read < Fixture.Baseline24.Length; read++)
            Check.Equal(Fixture.Baseline24[read], formation.unitTypes[write++], "baseline order");
        Check.Equal(write, formation.unitTypes.Length, "no extra seats");
    }

    /// <summary>Composite index of every original baseline seat (rows are inserted before the bow line).</summary>
    private static int[] BaselineSeatMap(int boats, bool row)
    {
        int fleetFootprint = boats > 0 ? boats : 1;
        int rows = RowLength + (row ? RowLength : 0);
        var map = new int[Fixture.Baseline24.Length];
        for (int read = 0; read < Fixture.Baseline24.Length; read++)
        {
            int before = read == 0 ? 0 : fleetFootprint + (read - 1);
            map[read] = before + (read >= FirstArcherRead ? rows : 0);
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

    // ---- cases --------------------------------------------------------------

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

    private static void Recruitment()
    {
        Case.Run("directed recruits land in the row and native bow/pike seats stay free", () =>
        {
            Activate(0, 4);
            int[] map = BaselineSeatMap(0, true);
            for (int seat = 0; seat < RowLength; seat++)
            {
                Check.True(Fixture.Formation.units[RowSeat(0, seat)] is Archer, "row seat holds a musketeer");
                Check.Equal(Formation.UnitTypes.Squire, Fixture.Formation.unitTypes[RowSeat(0, seat)],
                    "row seat type is restored after the directed recruit");
            }
            Check.Equal(0, CountOccupied(map[0], 1), "fleet seat free");
            Check.Equal(0, CountOccupied(map[3], 4), "bow seats free");
            Check.Equal(0, CountOccupied(map[8], 4), "pike seats free");
        });

        Case.Run("an extra musketeer cannot take a native bow seat while the row is full", () =>
        {
            Activate(0, 4);
            int[] map = BaselineSeatMap(0, true);
            Archer extra = Fixture.AddMusketeer(1f);
            Check.False(extra.TryRecruit(Fixture.Formation), "guard must refuse the native recruit");
            Check.True(extra.GetFormation() == null, "must stay outside the formation");
            Check.Equal(0, CountOccupied(map[3], 4), "bow seats stay free");
        });

        Case.Run("ordinary archers keep filling the native bow seats", () =>
        {
            Activate(0, 4);
            int[] map = BaselineSeatMap(0, true);
            var archers = new List<Archer>();
            for (int i = 0; i < 4; i++) archers.Add(Fixture.AddArcher(5f + i));
            for (int i = 0; i < 4; i++)
                Check.True(archers[i].TryRecruit(Fixture.Formation), "native bow recruit");
            Check.Equal(4, CountOccupied(map[3], 4), "bow seats filled");
            for (int seat = 0; seat < RowLength; seat++)
            {
                Check.True(Fixture.Formation.units[RowSeat(0, seat)] is Archer,
                    "row untouched by ordinary archers");
            }
        });

        Case.Run("a crossbowman is refused the bow line even with the musketeer feature off", () =>
        {
            Activate(0, 0, featureOn: false);                       // no row, feature off
            Archer crossbowman = Fixture.AddArcher(6f);
            CrossbowmanLifecycle.IdentityEnabled = true;
            CrossbowmanLifecycle.Crossbowmen.Add(crossbowman);
            Check.False(crossbowman.TryRecruit(Fixture.Formation), "crossbowman must be refused");
            Check.True(crossbowman.GetFormation() == null, "crossbowman must stay outside the formation");

            Archer ordinary = Fixture.AddArcher(7f);
            Check.True(ordinary.TryRecruit(Fixture.Formation), "ordinary archer keeps its native seat");
        });
    }

    private static void CrossbowRow()
    {
        Case.Run("both rear rows compose and seat separately for 0..4 boats", () =>
        {
            foreach (Side side in new[] { Side.Right, Side.Left })
            {
                for (int boats = 0; boats <= 4; boats++)
                {
                    Activate(boats, 4, side, crossbows: 4, crossIdentity: true);
                    AssertSeatMap2(boats, true, true);
                    for (int seat = 0; seat < RowLength; seat++)
                    {
                        Check.True(Fixture.Formation.units[CrossSeat(boats, seat)] is Archer,
                            "cross seat holds a crossbowman");
                        Check.True(Fixture.Formation.units[MuskSeat2(boats, seat)] is Archer,
                            "musk seat holds a musketeer");
                        Check.Equal(Formation.UnitTypes.Squire, Fixture.Formation.unitTypes[CrossSeat(boats, seat)],
                            "cross seat type restored after the directed recruit");
                        Check.Equal(Formation.UnitTypes.Squire, Fixture.Formation.unitTypes[MuskSeat2(boats, seat)],
                            "musk seat type restored after the directed recruit");
                    }
                    int[] map = BaselineSeatMap2(boats, true, true);
                    for (int i = 1; i < Fixture.Baseline24.Length; i++)
                        Check.True(Fixture.Formation.units[map[i]] == null,
                            "original seat must stay free (slot " + i + ")");
                    if (boats > 0) Check.Equal(boats, CountOccupied(map[0], boats), "boats seated");
                    float step = Fixture.Formation.UnitSpacing[(int)Formation.UnitTypes.Squire];
                    Check.Near(Fixture.Spacing24[(int)Formation.UnitTypes.Archer], step, 1e-6,
                        "both rows share the archer step");
                    Check.Equal(4, PatchRoles_Crossbowman.SeatedReconciles,
                        "career reconcile ran once per confirmed crossbow seat");
                }
            }
        });

        Case.Run("a full cross row and a full musk row move the fleet back one row each", () =>
        {
            foreach (Side side in new[] { Side.Right, Side.Left })
            {
                foreach (int boats in new[] { 0, 1, 4 })
                {
                    // Reference: the same activation with the cross row reserved but empty and no
                    // musk row, so the fleet spacing/closure semantics match exactly. Each filled
                    // row then shifts the bow line one row toward the fleet and vice versa.
                    Activate(boats, 0, side, featureOn: false, crossbows: 0, crossIdentity: false);
                    float[] crossOnly = SnapshotPositions();
                    int[] refMap = BaselineSeatMap2(boats, false, true);
                    float step = Fixture.Spacing24[(int)Formation.UnitTypes.Archer];

                    Activate(boats, 4, side, crossbows: 4, crossIdentity: true);
                    float[] withRows = SnapshotPositions();
                    int[] withMap = BaselineSeatMap2(boats, true, true);

                    Check.Equal(withRows.Length, crossOnly.Length + RowLength, "the musk row adds four seats");
                    for (int i = FirstArcherRead; i < Fixture.Baseline24.Length; i++)
                    {
                        Check.Near(Frontward(crossOnly[refMap[i]], side) + RowLength * step,
                            Frontward(withRows[withMap[i]], side), 1e-4,
                            "filled rows keep the bow line relative distances (side=" + side
                            + ", boats=" + boats + ", slot=" + i + ")");
                    }
                    for (int i = 0; i < FirstArcherRead; i++)
                    {
                        Check.Near(Frontward(crossOnly[refMap[i]], side) - RowLength * step,
                            Frontward(withRows[withMap[i]], side), 1e-4,
                            "fleet block moves back one row (side=" + side + ", boats=" + boats + ")");
                    }

                    float archer1 = Frontward(withRows[withMap[FirstArcherRead]], side);
                    float nearestMusk = Frontward(withRows[MuskSeat2(boats, RowLength - 1)], side);
                    float nearestCross = Frontward(withRows[CrossSeat(boats, RowLength - 1)], side);
                    Check.Near(step, archer1 - nearestMusk, 1e-4, "musketeer row stays one step off the bow line");
                    Check.Near(RowLength * step, nearestMusk - nearestCross, 1e-4,
                        "crossbow row sits one row behind the musketeers");
                }
            }
        });

        Case.Run("cross row degrades to the bow line and survives musk0/arch0", () =>
        {
            // Musketeer feature off: only the crossbow row is reserved (still reserved with R1).
            Activate(0, 0, featureOn: false, crossbows: 4, crossIdentity: true);
            AssertSeatMap2(0, false, true);
            for (int seat = 0; seat < RowLength; seat++)
            {
                Check.True(Fixture.Formation.units[CrossSeat(0, seat)] is Archer, "cross seat full");
            }
            int[] map = BaselineSeatMap2(0, false, true);
            float step = Fixture.Formation.UnitSpacing[(int)Formation.UnitTypes.Squire];
            Check.Near(Fixture.Spacing24[(int)Formation.UnitTypes.Archer], step, 1e-6, "cross row step");
            Check.Equal(0, CountOccupied(map[3], 4), "native bow seats stay empty (Arch0)");
            float archer1 = Fixture.Formation.GetXPosForIndex(map[3]);
            float nearestCross = Fixture.Formation.GetXPosForIndex(CrossSeat(0, RowLength - 1));
            Check.Near(step, archer1 - nearestCross, 1e-4, "crossbow row degrades to one step off the bow line");

            // Musketeer feature on but zero musketeers: the reserved musk row compacts away and the
            // crossbow row still fills.
            Activate(0, 0, crossbows: 4, crossIdentity: true);
            AssertSeatMap2(0, true, true);
            for (int seat = 0; seat < RowLength; seat++)
            {
                Check.True(Fixture.Formation.units[CrossSeat(0, seat)] is Archer, "cross seat full with musk0");
                Check.True(Fixture.Formation.units[MuskSeat2(0, seat)] == null, "empty musk seats stay empty");
            }
            float archer1b = Fixture.Formation.GetXPosForIndex(BaselineSeatMap2(0, true, true)[3]);
            float nearestCrossB = Fixture.Formation.GetXPosForIndex(CrossSeat(0, RowLength - 1));
            float step2 = Fixture.Spacing24[(int)Formation.UnitTypes.Archer];
            Check.Near(RowLength * step2, archer1 - archer1b, 1e-4,
                "an empty reserved musk row pulls the bow line back by its four steps");
            Check.Near(step2, archer1b - nearestCrossB, 1e-4,
                "the crossbow row still bows up one step behind the compacted bow line");
        });

        Case.Run("the cross row is reserved with zero crossbowmen and later careers join on maintenance", () =>
        {
            Activate(2, 4, crossbows: 0);
            AssertSeatMap2(2, true, true);
            for (int seat = 0; seat < RowLength; seat++)
            {
                Check.Equal(Formation.UnitTypes.Squire, Fixture.Formation.unitTypes[CrossSeat(2, seat)],
                    "empty cross seat stays Squire (no phantom Gap)");
                Check.True(Fixture.Formation.units[CrossSeat(2, seat)] == null, "no phantom unit");
            }
            Check.Equal(0, PatchRoles_Crossbowman.SeatedReconciles, "no cross seat confirmed yet");

            Archer late = Fixture.AddCrossbowman(4f);
            CrossbowmanLifecycle.IdentityEnabled = true;
            Fixture.Tick();
            Check.Equal(1, CountOccupied(CrossSeat(2, 0), RowLength), "maintenance added the late crossbowman");
            Check.True(ReferenceEquals(Fixture.Formation.units[CrossSeat(2, RowLength - 1)], late),
                "bow-side cross seat first");
            Check.True(ReferenceEquals(late.GetFormation(), Fixture.Formation), "bound by the native recruit");
        });

        Case.Run("crossbowmen never take native bow or musketeer seats", () =>
        {
            Activate(0, 4, crossbows: 4, crossIdentity: true);
            int[] map = BaselineSeatMap2(0, true, true);

            var archers = new List<Archer>();
            for (int i = 0; i < 4; i++) archers.Add(Fixture.AddArcher(5f + i));
            for (int i = 0; i < 4; i++)
                Check.True(archers[i].TryRecruit(Fixture.Formation), "ordinary archer keeps a native bow seat");
            Check.Equal(4, CountOccupied(map[3], 4), "bow seats filled by ordinary archers");
            for (int seat = 0; seat < RowLength; seat++)
            {
                Check.True(Fixture.Formation.units[CrossSeat(0, seat)] is Archer, "cross row untouched");
                Check.True(Fixture.Formation.units[MuskSeat2(0, seat)] is Archer, "musk row untouched");
            }

            Archer extra = Fixture.AddCrossbowman(1f);
            Check.False(extra.TryRecruit(Fixture.Formation), "a non-directed crossbowman is refused");
            Check.True(extra.GetFormation() == null, "extra crossbowman stays outside");
            Check.Equal(4, CountOccupied(map[3], 4), "no crossbowman displaced an archer");

            Archer robber = Fixture.AddCrossbowman(2f);
            Check.False(robber.TryRecruit(Fixture.Formation), "crossbowman cannot take any native seat");
            for (int seat = 0; seat < RowLength; seat++)
            {
                Check.Equal(0, robber.OnLeaveCalls, "no leave was involved");
            }
        });

        Case.Run("cross row candidates come from the registry with the native-free gates", () =>
        {
            // One owned crossbowman is parked on a guard slot: the row is still reserved with the
            // selected identity but nothing may be recruited, and an empty row compacts.
            Fixture.Reset(10f);
            Archer guard = Fixture.AddCrossbowman(4f);
            guard._guardSlot = new GuardSlot();
            Archer live = Fixture.AddCrossbowman(6f);
            Archer hero = Fixture.AddCrossbowman(8f);
            HeroArcherRuntime.Heroes.Add(hero);
            Archer grabbed = Fixture.AddCrossbowman(3f);
            grabbed._character.grabbed = true;
            Archer dead = Fixture.AddCrossbowman(2f);
            dead._damageable.isDead = true;
            CrossbowmanLifecycle.IdentityEnabled = true;
            Fixture.Player.ActivateFormation();

            int crossStart = CrossStart(0);
            Check.Equal(1, CountOccupied(crossStart, RowLength), "only the free crossbowman joined");
            Check.True(Fixture.Formation.units[crossStart + RowLength - 1] is Archer, "bow-side cross seat first");
            Check.True(ReferenceEquals(Fixture.Formation.units[crossStart + RowLength - 1], live),
                "the nearest eligible crossbowman took the seat");
            Check.True(guard.GetFormation() == null, "guard-post crossbowman excluded");
            Check.True(hero.GetFormation() == null, "hero excluded");
            Check.True(grabbed.GetFormation() == null, "grabbed crossbowman excluded");
            Check.True(dead.GetFormation() == null, "dead crossbowman excluded");
            Check.Equal(1, PatchRoles_Crossbowman.SeatedReconciles, "one confirmed seat, one reconcile");
        });

        Case.Run("nearest-first order and the four-seat cap hold for the cross row", () =>
        {
            Fixture.Reset(0f);
            var far = Fixture.AddCrossbowman(40f);
            var tooFar = Fixture.AddCrossbowman(60f);
            var near2 = Fixture.AddCrossbowman(-2f);
            var near1 = Fixture.AddCrossbowman(1f);
            var near3 = Fixture.AddCrossbowman(-1f);
            var near4 = Fixture.AddCrossbowman(2f);
            CrossbowmanLifecycle.IdentityEnabled = true;
            Fixture.Player.ActivateFormation();
            int crossStart = CrossStart(0);
            int rowSlot = crossStart + RowLength - 1;   // bow side first, then away
            Check.True(ReferenceEquals(Fixture.Formation.units[rowSlot], near1), "nearest first");
            Check.True(ReferenceEquals(Fixture.Formation.units[rowSlot - 1], near2)
                || ReferenceEquals(Fixture.Formation.units[rowSlot - 1], near3), "second nearest next");
            Check.True(ReferenceEquals(Fixture.Formation.units[rowSlot - 2], near4)
                || ReferenceEquals(Fixture.Formation.units[rowSlot - 2], near3)
                || ReferenceEquals(Fixture.Formation.units[rowSlot - 2], near2), "third nearest next");
            Check.Equal(4, CountOccupied(crossStart, RowLength), "four seats filled");
            Check.True(far.GetFormation() == null, "farther crossbowman stays outside (cap 4)");
            Check.True(tooFar.GetFormation() == null, "farthest crossbowman stays outside");
        });

        Case.Run("a re-armed same-GO life is never sent through OnLeave by a cross receipt", () =>
        {
            Fixture.Reset(10f);
            Archer crossbowman = Fixture.AddCrossbowman(2f);
            CrossbowmanLifecycle.IdentityEnabled = true;
            Fixture.Player.ActivateFormation();          // arm the cross row with the full roster
            int crossStart = CrossStart(0);
            Check.Equal(1, CountOccupied(crossStart, RowLength), "row armed");

            // Half-registration into a second, free cross seat, with the immediate rollback failing.
            Archer second = Fixture.AddCrossbowman(1f);
            second.ThrowOnConvertToSoldier = true;
            Fixture.Formation.ThrowOnUnregister = 1;
            Fixture.Tick();
            Check.Equal(2, CountOccupied(crossStart, RowLength), "half-registered seat still occupied");
            long capturedLife = CrossbowmanLifecycle.FormationLife(second);
            Check.True(capturedLife > 0L, "the half-registration captured a live career life");

            second.ThrowOnConvertToSoldier = false;
            Fixture.Formation.ThrowOnUnregister = 0;
            second._character.inert = true;              // keep the top-up out of the seat
            CrossbowmanLifecycle.ArmNewLife(second);     // same GameObject re-armed: new life
            Check.False(CrossbowmanLifecycle.MatchesFormationLife(second, capturedLife),
                "the career life changed");

            Fixture.Tick();
            Check.Equal(1, CountOccupied(crossStart, RowLength), "stale seat reference cleared");
            Check.Equal(0, second.OnLeaveCalls, "never OnLeave a different life");
            Check.True(second.GetFormation() == null, "the new life stays untouched");
        });

        Case.Run("identity off releases own cross members without shrinking the formation", () =>
        {
            Activate(1, 4, crossbows: 4, crossIdentity: true);
            int[] map = BaselineSeatMap2(1, true, true);
            int length = Fixture.Formation.unitTypes.Length;
            var crossMembers = new List<Archer>();
            for (int seat = 0; seat < RowLength; seat++)
            {
                if (Fixture.Formation.units[CrossSeat(1, seat)] is Archer archer) crossMembers.Add(archer);
            }
            Check.Equal(4, crossMembers.Count, "cross row full");
            int muskBefore = CountOccupied(MuskSeat2(1, 0), RowLength);
            Check.Equal(4, muskBefore, "musk row full");

            CrossbowmanLifecycle.IdentityEnabled = false;
            Fixture.Tick();

            Check.Equal(0, CountOccupied(CrossSeat(1, 0), RowLength), "cross members released");
            for (int i = 0; i < crossMembers.Count; i++)
            {
                Check.True(crossMembers[i].GetFormation() == null, "released from the formation");
                Check.Equal(1, crossMembers[i].OnLeaveCalls, "native leave exactly once");
            }
            Check.Equal(length, Fixture.Formation.unitTypes.Length, "arrays are not hot-shrunk");
            Check.Equal(muskBefore, CountOccupied(MuskSeat2(1, 0), RowLength), "musk row untouched");
            Check.Equal(1, CountOccupied(map[0], 1), "boat untouched");
        });

        Case.Run("cross recruit failure rolls back and the receipt retry completes", () =>
        {
            Fixture.Reset(10f);
            Archer crossbowman = Fixture.AddCrossbowman(2f);
            CrossbowmanLifecycle.IdentityEnabled = true;
            crossbowman.ThrowOnConvertToSoldier = true;
            Fixture.Player.ActivateFormation();
            int crossStart = CrossStart(0);

            Check.Equal(0, CountOccupied(crossStart, RowLength), "seat must be rolled back");
            Check.True(crossbowman.GetFormation() == null, "crossbowman must not stay bound");
            Check.Equal(1, crossbowman.OnLeaveCalls, "native leave ran exactly once");

            crossbowman.ThrowOnConvertToSoldier = false;
            Fixture.Tick();
            Check.Equal(1, CountOccupied(crossStart, RowLength), "seat is usable again on the next pass");
        });

        Case.Run("a callback disabling the feature fails the success check and releases same-life", () =>
        {
            Fixture.Reset(10f);
            Archer cross = Fixture.AddCrossbowman(2f);
            CrossbowmanLifecycle.IdentityEnabled = true;
            cross.OnConvertedToSoldierCallback = a => ModConfig.Enabled.Value = false;
            Fixture.Player.ActivateFormation();
            ModConfig.Enabled.Value = true;                    // restore for the rest of the suite

            Check.Equal(0, PatchRoles_Crossbowman.SeatedReconciles,
                "a disabled feature never claims the seat as an accepted success");
            Check.Equal(0, CountOccupied(CrossStart(0), RowLength), "same-life seat released");
            Check.Equal(1, cross.OnLeaveCalls, "native leave ran once for the same life");
            Check.True(cross.GetFormation() == null, "actor unbound");
        });

        Case.Run("a callback replacing the life inside OnSeated is not accepted and only drops the stale ref", () =>
        {
            Fixture.Reset(10f);
            Archer cross = Fixture.AddCrossbowman(2f);
            CrossbowmanLifecycle.IdentityEnabled = true;
            long firstLife = CrossbowmanLifecycle.FormationLife(cross);
            PatchRoles_Crossbowman.AfterSeatedReconcile = a => CrossbowmanLifecycle.ArmNewLife(a);
            Fixture.Player.ActivateFormation();

            Check.Equal(1, PatchRoles_Crossbowman.SeatedReconciles, "the reconcile boundary ran");
            Check.True(CrossbowmanLifecycle.FormationLife(cross) != firstLife, "life rotated in the callback");
            Check.Equal(0, CountOccupied(CrossStart(0), RowLength), "stale array reference dropped");
            Check.Equal(0, cross.OnLeaveCalls, "never OnLeave the new life");
            Check.True(ReferenceEquals(cross.GetFormation(), Fixture.Formation),
                "the new life's own binding is untouched");
        });

        Case.Run("authority loss inside the callback fails closed and resolves after restore", () =>
        {
            Fixture.Reset(10f);
            Archer cross = Fixture.AddCrossbowman(2f);
            CrossbowmanLifecycle.IdentityEnabled = true;
            cross.OnConvertedToSoldierCallback = a => NetworkBigBoss.HasWorldAuth = false;
            Fixture.Player.ActivateFormation();

            Check.Equal(0, PatchRoles_Crossbowman.SeatedReconciles, "no claim without authority");
            Check.Equal(1, CountOccupied(CrossStart(0), RowLength), "seat untouched while authority is lost");
            Check.Equal(0, cross.OnLeaveCalls, "never force a native unregister without authority");

            NetworkBigBoss.HasWorldAuth = true;
            Fixture.Tick();                                    // maintained gate settles the same life
            Check.True(ReferenceEquals(cross.GetFormation(), Fixture.Formation),
                "the legitimate same-life member stays after the world recovers");
            Check.Equal(0, cross.OnLeaveCalls, "recovery does not release a valid member");
        });

        Case.Run("a cross receipt survives an array replaced mid-transaction", () =>
        {
            Fixture.Reset(10f);
            Archer crossbowman = Fixture.AddCrossbowman(2f);
            CrossbowmanLifecycle.IdentityEnabled = true;
            crossbowman.ReplaceArraysOnRecruit = formation =>
            {
                var replacement = new Il2CppStructArray<Formation.UnitTypes>(18);
                for (int i = 0; i < 18; i++) replacement[i] = Formation.UnitTypes.Player;
                formation.unitTypes = replacement;
                formation.units = new Il2CppReferenceArray<Formation.IFormationUnit>(18);
            };
            Fixture.Player.ActivateFormation();

            Check.Equal(Formation.UnitTypes.Player, Fixture.Formation.unitTypes[3], "replacement array untouched");
            Check.True(crossbowman.GetFormation() == null, "bound-but-seatless actor was released directly");
            Check.Equal(1, crossbowman.OnLeaveCalls, "direct leave ran exactly once");
        });
    }

    private static void TransactionFailures()
    {
        Case.Run("native throw after RegisterUnit rolls the seat back and releases the archer", () =>
        {
            Fixture.Reset(10f);
            Archer musketeer = Fixture.AddMusketeer(20f);
            musketeer.ThrowOnConvertToSoldier = true;
            Fixture.Player.ActivateFormation();

            Check.Equal(0, CountOccupied(RowStart(0), RowLength), "seat must be rolled back");
            Check.True(musketeer.GetFormation() == null, "archer must not stay bound");
            Check.Equal(1, musketeer.OnLeaveCalls, "native leave ran exactly once");
            Check.False(Fixture.HasInfo("directed recruit rollback pending"), "no receipt when the rollback completes");

            musketeer.ThrowOnConvertToSoldier = false;
            Fixture.Tick();
            Check.Equal(1, CountOccupied(RowStart(0), RowLength), "seat is usable again on the next pass");
        });

        Case.Run("false with a partial registration rolls back through native leave", () =>
        {
            Fixture.Reset(10f);
            Archer musketeer = Fixture.AddMusketeer(20f);
            musketeer.ReturnFalseAfterRegister = true;
            Fixture.Player.ActivateFormation();

            Check.Equal(0, CountOccupied(RowStart(0), RowLength), "seat must be rolled back");
            Check.True(musketeer.GetFormation() == null, "archer must not stay bound");
            Check.Equal(1, musketeer.OnLeaveCalls, "native leave ran exactly once");
            Check.False(Fixture.HasInfo("directed recruit rollback pending"), "no receipt when the rollback completes");
        });

        Case.Run("false with a full binding is verified by state and kept", () =>
        {
            Fixture.Reset(10f);
            Archer musketeer = Fixture.AddMusketeer(20f);
            musketeer.ReturnFalseAfterBind = true;
            Fixture.Player.ActivateFormation();

            Check.Equal(1, CountOccupied(RowStart(0), RowLength),
                "state-complete join must be kept despite the false return");
            Check.True(ReferenceEquals(musketeer.GetFormation(), Fixture.Formation), "bound to the formation");
            Check.Equal(0, musketeer.OnLeaveCalls, "no leave on a completed join");
        });

        Case.Run("rollback failure leaves a receipt and the next pass completes it", () =>
        {
            Fixture.Reset(10f);
            Archer musketeer = Fixture.AddMusketeer(20f);
            musketeer.ThrowOnConvertToSoldier = true;
            Fixture.Formation.ThrowOnUnregister = 1;
            Fixture.Player.ActivateFormation();

            Check.Equal(1, CountOccupied(RowStart(0), RowLength), "seat still occupied after the failed rollback");
            Check.True(musketeer.GetFormation() == null, "archer must not stay bound");

            musketeer._character.inert = true;    // keep the top-up from refilling the seat
            Fixture.Formation.ThrowOnUnregister = 0;
            Fixture.Tick();
            Check.Equal(0, CountOccupied(RowStart(0), RowLength), "receipt retry released the seat");
            Check.Equal(1, musketeer.OnLeaveCalls, "native leave ran exactly once");
        });

        Case.Run("a throw in the type write is rolled back; a failed restore is finished by the next pass", () =>
        {
            Activate(0, 1);
            Archer second = Fixture.AddMusketeer(2f);

            int bowSeat = BaselineSeatMap2(0, true, true)[FirstArcherRead];
            Il2CppStructArray<Formation.UnitTypes>.ResetSetAttempts();
            Il2CppStructArray<Formation.UnitTypes>.ApplyThenThrowOnAttempt = bowSeat + 1;      // applies the closed bow seat then throws
            // The very next set attempt is that cell's restore (all earlier cells are unchanged),
            // and it must fail for the residual to stay visible until the receipt retry.
            Il2CppStructArray<Formation.UnitTypes>.ThrowBeforeApplyOnAttempt = bowSeat + 2;

            Fixture.Tick();
            Check.Equal(Formation.UnitTypes.Gap, Fixture.Formation.unitTypes[bowSeat],
                "restore residual is visible");
            Check.True(second.GetFormation() == null, "failed transaction must not bind the archer");
            Check.Equal(1, CountOccupied(RowStart(0), RowLength), "no second member");

            Il2CppStructArray<Formation.UnitTypes>.ApplyThenThrowOnAttempt = 0;
            Il2CppStructArray<Formation.UnitTypes>.ThrowBeforeApplyOnAttempt = 0;
            Fixture.Tick();
            Check.Equal(Formation.UnitTypes.Archer, Fixture.Formation.unitTypes[bowSeat],
                "receipt retry restored the cell");
            Check.Equal(2, CountOccupied(RowStart(0), RowLength), "seat refilled after the restore");
        });

        Case.Run("an array replaced mid-transaction is never overwritten", () =>
        {
            Fixture.Reset(10f);
            Archer musketeer = Fixture.AddMusketeer(20f);
            musketeer.ReplaceArraysOnRecruit = formation =>
            {
                var replacement = new Il2CppStructArray<Formation.UnitTypes>(16);
                for (int i = 0; i < 16; i++) replacement[i] = Formation.UnitTypes.Player;
                formation.unitTypes = replacement;
                formation.units = new Il2CppReferenceArray<Formation.IFormationUnit>(16);
            };
            Fixture.Player.ActivateFormation();

            Check.Equal(Formation.UnitTypes.Player, Fixture.Formation.unitTypes[3], "replacement array untouched");
            Check.True(musketeer.GetFormation() == null, "bound-but-seatless actor was released directly");
            Check.Equal(1, musketeer.OnLeaveCalls, "direct leave ran exactly once");
        });

        Case.Run("dirty temporary types refuse every other recruit until the restore lands", () =>
        {
            Fixture.Reset(10f);
            Archer first = Fixture.AddMusketeer(20f);
            Fixture.Player.ActivateFormation();               // bow-side row seat holds `first`
            Archer second = Fixture.AddMusketeer(2f);

            Il2CppStructArray<Formation.UnitTypes>.ResetSetAttempts();
            // Target cell (musk row index 9) is written at attempt 10; the next write fails and every
            // restore attempt keeps throwing, so the target stays open while the receipt owns it.
            Il2CppStructArray<Formation.UnitTypes>.FailRestoreFromAttempt = 11;
            Fixture.Tick();

            Check.Equal(1, CountOccupied(RowStart(0), RowLength), "no second member");
            Check.Equal(Formation.UnitTypes.Archer, Fixture.Formation.unitTypes[RowSeat(0, 2)],
                "dirty target seat left open");

            Archer ordinary = Fixture.AddArcher(1f);
            Check.False(ordinary.TryRecruit(Fixture.Formation), "ordinary archer refused while dirty");
            Check.True(ordinary.GetFormation() == null, "ordinary archer stays outside");
            Check.Equal(1, CountOccupied(RowStart(0), RowLength), "the open seat stays empty");

            Fixture.Tick();                                    // the receipt retry still keeps failing
            Check.False(ordinary.TryRecruit(Fixture.Formation), "still refused while the restore keeps failing");

            Il2CppStructArray<Formation.UnitTypes>.FailRestoreFromAttempt = 0;
            Fixture.Tick();
            Check.Equal(Formation.UnitTypes.Squire, Fixture.Formation.unitTypes[RowSeat(0, 2)],
                "receipt restored the cell");
            Check.Equal(2, CountOccupied(RowStart(0), RowLength), "the waiting musketeer took the seat afterwards");

            Archer lateArcher = Fixture.AddArcher(1f);
            Check.True(lateArcher.TryRecruit(Fixture.Formation), "ordinary archers are native again");
            int[] map = BaselineSeatMap(0, true);
            Check.Equal(1, CountOccupied(map[3], 4), "ordinary archer fills a native bow seat");
            for (int seat = 0; seat < RowLength; seat++)
            {
                var occupant = Fixture.Formation.units[RowSeat(0, seat)];
                Check.True(occupant == null || (occupant is Archer rifleman && MusketeerIdentity.IsUnit(rifleman)),
                    "each occupied row seat still holds a marked musketeer; unfilled seats stay empty");
            }
        });

        Case.Run("a re-armed same-GO life is never sent through OnLeave by an old receipt", () =>
        {
            Fixture.Reset(10f);
            Fixture.Player.ActivateFormation();               // empty row
            Archer reused = Fixture.AddMusketeer(2f);

            reused.ThrowOnConvertToSoldier = true;            // half-registration
            Fixture.Formation.ThrowOnUnregister = 1;          // the immediate rollback fails -> receipt with the lease
            Fixture.Tick();
            Check.Equal(1, CountOccupied(RowStart(0), RowLength), "half-registered seat still occupied");
            long capturedLease = MusketeerRuntime.BindingLease(reused);
            Check.True(capturedLease > 0L, "the half-registration captured a live lease");

            reused.ThrowOnConvertToSoldier = false;
            Fixture.Formation.ThrowOnUnregister = 0;
            reused._character.inert = true;                   // keep the top-up out of the seat
            MusketeerRuntime.ArmNewLife(reused);              // same GameObject re-armed: new lease, still marked
            Check.True(MusketeerIdentity.IsUnit(reused), "counterexample needs a marked new life");
            Check.False(MusketeerRuntime.MatchesBindingLease(reused, capturedLease), "the life changed");

            Fixture.Tick();
            Check.Equal(0, CountOccupied(RowStart(0), RowLength), "stale seat reference cleared");
            Check.Equal(0, reused.OnLeaveCalls, "never OnLeave a different life");
            Check.True(reused.GetFormation() == null, "the new life stays untouched");
            Check.True(MusketeerRuntime.BindingLease(reused) == capturedLease + 1L, "the new life keeps its own lease");
        });

        Case.Run("a failed seatless leave is retried by the maintenance pass", () =>
        {
            Fixture.Reset(10f);
            Archer musketeer = Fixture.AddMusketeer(20f);
            musketeer.ThrowOnLeave = 1;                       // the direct leave fails once
            musketeer.ReplaceArraysOnRecruit = formation =>
            {
                var replacement = new Il2CppStructArray<Formation.UnitTypes>(16);
                for (int i = 0; i < 16; i++) replacement[i] = Formation.UnitTypes.Player;
                formation.unitTypes = replacement;
                formation.units = new Il2CppReferenceArray<Formation.IFormationUnit>(16);
            };
            Fixture.Player.ActivateFormation();

            Check.True(ReferenceEquals(musketeer.GetFormation(), Fixture.Formation),
                "actor stays bound while the seat is already gone");
            Check.Equal(0, musketeer.OnLeaveCalls, "no completed leave yet");
            Check.Equal(Formation.UnitTypes.Player, Fixture.Formation.unitTypes[3], "replacement array untouched");

            musketeer._character.inert = true;                // keep the top-up out of the new array
            Fixture.Tick();
            Check.True(musketeer.GetFormation() == null, "maintenance finished the seatless leave");
            Check.Equal(1, musketeer.OnLeaveCalls, "leave ran exactly once");
            Check.Equal(Formation.UnitTypes.Player, Fixture.Formation.unitTypes[3], "new array still untouched");
        });

        Case.Run("a seatless receipt never releases a re-armed same-GO life", () =>
        {
            Fixture.Reset(10f);
            Archer musketeer = Fixture.AddMusketeer(20f);
            musketeer.ThrowOnLeave = 1;
            musketeer.ReplaceArraysOnRecruit = formation =>
            {
                var replacement = new Il2CppStructArray<Formation.UnitTypes>(16);
                for (int i = 0; i < 16; i++) replacement[i] = Formation.UnitTypes.Player;
                formation.unitTypes = replacement;
                formation.units = new Il2CppReferenceArray<Formation.IFormationUnit>(16);
            };
            Fixture.Player.ActivateFormation();

            long capturedLease = MusketeerRuntime.BindingLease(musketeer);
            Check.True(capturedLease > 0L, "the seatless receipt captured a live lease");
            Check.True(ReferenceEquals(musketeer.GetFormation(), Fixture.Formation), "bound after the failed leave");

            musketeer._character.inert = true;                // keep the top-up out
            MusketeerRuntime.ArmNewLife(musketeer);           // same GO, new life; still marked
            Check.True(MusketeerIdentity.IsUnit(musketeer), "counterexample needs a marked new life");
            Check.False(MusketeerRuntime.MatchesBindingLease(musketeer, capturedLease), "the life changed");

            Fixture.Tick();
            Check.Equal(0, musketeer.OnLeaveCalls, "never OnLeave a different life");
            Check.True(ReferenceEquals(musketeer.GetFormation(), Fixture.Formation),
                "the different life was not touched");
            Check.Equal(Formation.UnitTypes.Player, Fixture.Formation.unitTypes[3], "new array untouched");
        });

        Case.Run("native exception in ActivateFormation restores the empty baseline", () =>
        {
            Fixture.Reset(10f);
            Fixture.AddMusketeer(20f);
            Player.ThrowInActivateBody = 1;
            bool threw = false;
            try { Fixture.Player.ActivateFormation(); }
            catch (InvalidOperationException) { threw = true; }

            Check.True(threw, "the scripted native exception must surface");
            Check.Equal(Fixture.Baseline24.Length, Fixture.Formation.unitTypes.Length, "baseline restored");
            Check.Equal(0, CountOccupied(0, Fixture.Baseline24.Length), "no units left behind");
            Check.Equal(0f, Fixture.Formation.startOffset, "startOffset restored");
            Check.Near(Fixture.Spacing24[(int)Formation.UnitTypes.Squire],
                Fixture.Formation.UnitSpacing[(int)Formation.UnitTypes.Squire], 1e-6,
                "Squire spacing restored");
        });
    }

    private static void Lifecycle()
    {
        Case.Run("consecutive activate is idempotent and furl restores the exact baseline", () =>
        {
            Activate(2, 3, Side.Right);
            int length = Fixture.Formation.unitTypes.Length;
            int[] map = BaselineSeatMap(2, true);
            int rowStart = RowStart(2);

            Fixture.Player.ActivateFormation();
            Check.Equal(length, Fixture.Formation.unitTypes.Length, "no double expansion");
            Check.Equal(3, CountOccupied(rowStart, RowLength), "no duplicate musketeers");
            Check.Equal(2, CountOccupied(map[0], 2), "no duplicate boats");

            Fixture.Furl();
            Check.Equal(Fixture.Baseline24.Length, Fixture.Formation.unitTypes.Length, "baseline length");
            for (int i = 0; i < Fixture.Baseline24.Length; i++)
                Check.Equal(Fixture.Baseline24[i], Fixture.Formation.unitTypes[i], "baseline type order");
            Check.Equal(0f, Fixture.Formation.startOffset, "baseline startOffset");
            Check.Equal(0, CountOccupied(0, Fixture.Baseline24.Length), "all units left");
            Check.Near(Fixture.Spacing24[(int)Formation.UnitTypes.Squire],
                Fixture.Formation.UnitSpacing[(int)Formation.UnitTypes.Squire], 1e-6,
                "Squire spacing restored by the furl");
            Check.Near(Fixture.Spacing24[(int)Formation.UnitTypes.FleetBoat],
                Fixture.Formation.UnitSpacing[(int)Formation.UnitTypes.FleetBoat], 1e-6,
                "fleet spacing restored by the furl");
            Fixture.Tick();
            Check.Equal(Fixture.Baseline24.Length, Fixture.Formation.unitTypes.Length, "furl stays restored");

            Fixture.Player.ActivateFormation();
            AssertSeatMap(2, true);
            Check.Equal(3, CountOccupied(rowStart, RowLength), "re-raise refills the row");
            Check.Near(Fixture.Spacing24[(int)Formation.UnitTypes.Archer],
                Fixture.Formation.UnitSpacing[(int)Formation.UnitTypes.Squire], 1e-6,
                "row step re-applied on re-raise");
            Check.Near(1f, Fixture.Formation.UnitSpacing[(int)Formation.UnitTypes.FleetBoat], 1e-6,
                "multi-boat spacing re-applied on re-raise");
        });

        Case.Run("death frees the row seat and the maintenance pass refills it", () =>
        {
            Fixture.Reset(10f);
            var musketeers = new List<Archer>();
            for (int i = 0; i < 4; i++) musketeers.Add(Fixture.AddMusketeer(20f + i * 2f));
            Fixture.Player.ActivateFormation();
            int rowStart = RowStart(0);
            Check.Equal(4, CountOccupied(rowStart, RowLength), "row full");

            Archer spare = Fixture.AddMusketeer(2f);
            Archer dying = musketeers[0];              // first recruit took the bow-side row seat
            dying._damageable.isDead = true;
            Fixture.Formation.UnregisterUnit(dying);   // native death path
            Check.Equal(3, CountOccupied(rowStart, RowLength), "seat freed");

            Fixture.Tick();
            Check.Equal(4, CountOccupied(rowStart, RowLength), "seat refilled");
            Check.True(ReferenceEquals(spare.GetFormation(), Fixture.Formation), "the spare took the seat");
        });
    }

    private static void FeatureSwitch()
    {
        Case.Run("feature off releases own members without shrinking the formation", () =>
        {
            Activate(1, 4);
            int[] map = BaselineSeatMap(1, true);
            int rowStart = RowStart(1);
            int length = Fixture.Formation.unitTypes.Length;
            List<Archer> members = RowMembers(1);
            Check.Equal(4, members.Count, "row full");

            ModConfig.MusketeerEnabled.Value = false;
            Fixture.Tick();

            Check.Equal(0, CountOccupied(rowStart, RowLength), "own members released");
            for (int i = 0; i < members.Count; i++)
            {
                Check.True(members[i].GetFormation() == null, "released from the formation");
                Check.Equal(1, members[i].OnLeaveCalls, "native leave exactly once");
            }
            Check.Equal(length, Fixture.Formation.unitTypes.Length, "arrays are not hot-shrunk");
            Check.Equal(1, CountOccupied(map[0], 1), "boat untouched");
            for (int i = 1; i < Fixture.Baseline24.Length; i++)
                Check.True(Fixture.Formation.units[map[i]] == null, "original seat untouched");

            Archer late = Fixture.AddMusketeer(1f);
            Fixture.Tick();
            Check.Equal(0, CountOccupied(rowStart, RowLength), "no recruit while the feature is off");
            Check.True(late.GetFormation() == null, "candidate stays outside");
        });

        Case.Run("a paused world defers the feature-off release", () =>
        {
            Activate(0, 2);
            int rowStart = RowStart(0);
            ModConfig.MusketeerEnabled.Value = false;
            Time.timeScale = 0f;
            Fixture.Tick();
            Check.Equal(2, CountOccupied(rowStart, RowLength), "nothing released while paused");

            Time.timeScale = 1f;
            Fixture.Tick();
            Check.Equal(0, CountOccupied(rowStart, RowLength), "released after unpause");
        });
    }

    private static void Ghosts()
    {
        Case.Run("ghost seats are cleaned per owner rules", () =>
        {
            Activate(0, 0);
            Formation formation = Fixture.Formation;
            Formation other = Fixture.Create<Formation>(Fixture.Root, 0f);
            int foreignSeat = RowSeat(0, 3);
            int partialSeat = RowSeat(0, 2);
            int pooledSeat = RowSeat(0, 1);
            int memberSeat = RowSeat(0, 0);

            Archer foreign = Fixture.AddMusketeer(1f);      // bow-side seat: different owner
            foreign.BindForTests(other);
            formation.units[foreignSeat] = foreign;

            Archer partial = Fixture.AddMusketeer(1f);      // registered without a binding
            partial._character.inert = true;                // keep the top-up from refilling it
            formation.units[partialSeat] = partial;

            Archer pooled = Fixture.AddArcher(1f);          // pooled new life (unmarked, unbound)
            formation.units[pooledSeat] = pooled;

            Archer member = Fixture.AddMusketeer(1f);       // legitimate member
            member.BindForTests(formation);
            formation.units[memberSeat] = member;

            Fixture.Tick();

            Check.True(formation.units[foreignSeat] == null, "foreign reference dropped");
            Check.Equal(0, foreign.OnLeaveCalls, "never OnLeave a foreign member");
            Check.True(formation.units[partialSeat] == null, "partial ghost reference dropped");
            Check.Equal(0, partial.OnLeaveCalls, "no captured lease: reference drop only, never OnLeave");
            Check.True(formation.units[pooledSeat] == null, "pooled new-life reference dropped");
            Check.Equal(0, pooled.OnLeaveCalls, "never OnLeave a pooled new life");
            Check.True(ReferenceEquals(formation.units[memberSeat], member), "legitimate member kept");
            Check.Equal(0, member.OnLeaveCalls, "member untouched");
        });
    }

    private static void AuthorityAndScene()
    {
        Case.Run("authority loss and scene change never write", () =>
        {
            Activate(1, 3);
            int rowStart = RowStart(1);
            List<Archer> members = RowMembers(1);
            Check.Equal(3, members.Count, "row full");

            NetworkBigBoss.HasWorldAuth = false;
            Fixture.Tick();
            Check.Equal(3, CountOccupied(rowStart, RowLength), "no release without authority");
            for (int i = 0; i < members.Count; i++)
                Check.Equal(0, members[i].OnLeaveCalls, "no leave without authority");

            NetworkBigBoss.HasWorldAuth = true;
            Managers.Inst.world = new World { gameLayer = new GameObject().transform };
            Fixture.Tick();
            Check.Equal(3, CountOccupied(rowStart, RowLength), "no write into a new scene");
            for (int i = 0; i < members.Count; i++)
                Check.Equal(0, members[i].OnLeaveCalls, "no leave across scenes");
        });
    }
}
