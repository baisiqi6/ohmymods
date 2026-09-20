using KingdomEnhancedMod;

namespace MusketeerFormationTests
{
    internal static class LayoutTests
    {
        private const int RowLength = PatchMusketeerFormation.MaxMusketeers;
        private const int FirstArcherRead = 3;   // RealBaseline: FleetBoat, Gap, Gap, then the bow line

        // Operator-provided real 2.4 Player formation resource (tasks/.../geometry-notes.md):
        // unitTypes [FleetBoat, Gap, Gap, Archer x4, Gap, Pikemen x4] and its authored spacing.
        private static readonly Formation.UnitTypes[] RealBaseline =
        {
            Formation.UnitTypes.FleetBoat, Formation.UnitTypes.Gap, Formation.UnitTypes.Gap,
            Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
            Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
            Formation.UnitTypes.Gap,
            Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen,
            Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen
        };

        private static readonly float[] RealSpacing =
        {
            0.21875f, 0.25f, 0.25f, 0.21875f, 1f, 0.34375f, 1f,
            1f, 0.21875f, 0.21875f, 0.21875f, 0f, 0f
        };

        internal static void Run()
        {
            Composition();
            Placement();
            DirectedPlan();
        }

        private static void Composition()
        {
            Case.Run("the row is inserted after the lead block and before the first archer", () =>
            {
                var baseline = new[]
                {
                    Formation.UnitTypes.FleetBoat,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen
                };
                Check.True(MusketeerFormationLayout.TryCompose(baseline, 3, true,
                    out Formation.UnitTypes[] types, out int[] boats, out int[] musketeers, out int row),
                    "compose");
                Check.Equal(4, row, "row length");
                Check.Sequence(new[] { 3, 4, 5, 6 }, musketeers, "row slots follow the boat block");
                Check.Sequence(new[] { 0, 1, 2 }, boats, "boat slots");
                Check.Equal(15, types.Length, "composite length");
                Check.Sequence(new[]
                {
                    Formation.UnitTypes.FleetBoat, Formation.UnitTypes.FleetBoat,
                    Formation.UnitTypes.FleetBoat,
                    Formation.UnitTypes.Squire, Formation.UnitTypes.Squire,
                    Formation.UnitTypes.Squire, Formation.UnitTypes.Squire,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen
                }, types, "composite order");
            });

            Case.Run("the real 2.4 baseline keeps its gaps and puts the row before the bow line", () =>
            {
                Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, 2, true,
                    out Formation.UnitTypes[] types, out int[] boats, out int[] musketeers, out int row),
                    "compose");
                Check.Equal(4, row, "row length");
                Check.Sequence(new[] { 4, 5, 6, 7 }, musketeers, "row slots between the gaps and the bow line");
                Check.Sequence(new[] { 0, 1 }, boats, "boat slots");
                Check.Equal(17, types.Length, "composite length");
                Check.Sequence(new[]
                {
                    Formation.UnitTypes.FleetBoat, Formation.UnitTypes.FleetBoat,
                    Formation.UnitTypes.Gap, Formation.UnitTypes.Gap,
                    Formation.UnitTypes.Squire, Formation.UnitTypes.Squire,
                    Formation.UnitTypes.Squire, Formation.UnitTypes.Squire,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Gap,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen
                }, types, "real 2.4 composite order");
            });

            Case.Run("zero boats leaves one closed gap at the native fleet seat", () =>
            {
                var baseline = new[]
                {
                    Formation.UnitTypes.FleetBoat,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Pikemen
                };
                Check.True(MusketeerFormationLayout.TryCompose(baseline, 0, true,
                    out Formation.UnitTypes[] types, out int[] boats, out int[] musketeers, out int row),
                    "compose");
                Check.Equal(4, row, "row length");
                Check.Equal(0, boats.Length, "no boat slots");
                Check.Sequence(new[] { 1, 2, 3, 4 }, musketeers, "row slots after the closed fleet seat");
                Check.Equal(7, types.Length, "composite length");
                Check.Sequence(new[]
                {
                    Formation.UnitTypes.Gap,
                    Formation.UnitTypes.Squire, Formation.UnitTypes.Squire,
                    Formation.UnitTypes.Squire, Formation.UnitTypes.Squire,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Pikemen
                }, types, "closed fleet seat order");
            });

            Case.Run("feature off keeps the fleet-only layout", () =>
            {
                var baseline = new[]
                {
                    Formation.UnitTypes.FleetBoat,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Pikemen
                };
                Check.True(MusketeerFormationLayout.TryCompose(baseline, 2, false,
                    out Formation.UnitTypes[] types, out int[] boats, out int[] musketeers, out int row),
                    "compose");
                Check.Equal(0, row, "no row");
                Check.Equal(0, musketeers.Length, "no row slots");
                Check.Sequence(new[] { 0, 1 }, boats, "boat slots");
                Check.Sequence(new[]
                {
                    Formation.UnitTypes.FleetBoat, Formation.UnitTypes.FleetBoat,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Pikemen
                }, types, "fleet only order");
            });

            Case.Run("baseline without an archer slot skips the row but keeps the fleet plan", () =>
            {
                var baseline = new[]
                {
                    Formation.UnitTypes.FleetBoat,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen
                };
                Check.True(MusketeerFormationLayout.TryCompose(baseline, 1, true,
                    out Formation.UnitTypes[] types, out int[] boats, out int[] musketeers, out int row),
                    "compose");
                Check.Equal(0, row, "row skipped");
                Check.Equal(0, musketeers.Length, "no row slots");
                Check.Sequence(new[] { 0 }, boats, "boat slots");
                Check.Sequence(new[]
                {
                    Formation.UnitTypes.FleetBoat, Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen
                }, types, "baseline order");
            });

            Case.Run("malformed baselines fail closed", () =>
            {
                Check.False(MusketeerFormationLayout.TryCompose(null, 0, true,
                    out _, out _, out _, out _), "null baseline");
                Check.False(MusketeerFormationLayout.TryCompose(new Formation.UnitTypes[0], 0, true,
                    out _, out _, out _, out _), "empty baseline");
                Check.False(MusketeerFormationLayout.TryCompose(new[]
                {
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer
                }, 0, true, out _, out _, out _, out _), "missing fleet seat");
                Check.False(MusketeerFormationLayout.TryCompose(new[]
                {
                    Formation.UnitTypes.FleetBoat, Formation.UnitTypes.FleetBoat, Formation.UnitTypes.Archer
                }, 0, true, out _, out _, out _, out _), "duplicate fleet seat");
                Check.False(MusketeerFormationLayout.TryCompose(new[]
                {
                    Formation.UnitTypes.FleetBoat, Formation.UnitTypes.Archer
                }, -1, true, out _, out _, out _, out _), "negative boats");
            });

            Case.Run("compose never mutates the baseline array", () =>
            {
                var baseline = new[]
                {
                    Formation.UnitTypes.FleetBoat, Formation.UnitTypes.Archer, Formation.UnitTypes.Pikemen
                };
                var copy = (Formation.UnitTypes[])baseline.Clone();
                Check.True(MusketeerFormationLayout.TryCompose(baseline, 2, true,
                    out _, out _, out _, out _), "compose");
                Check.Sequence(copy, baseline, "baseline untouched");
            });
        }

        private static void Placement()
        {
            Case.Run("a full row leaves the bow line on the baseline and moves the fleet block back", () =>
            {
                float rowStep = RealSpacing[(int)Formation.UnitTypes.Archer];   // 0.21875
                float fleetShift = RowLength * rowStep;                        // 0.875

                foreach (int boats in new[] { 0, 1, 2, 4 })
                {
                    Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, boats, true,
                        out Formation.UnitTypes[] typesWith, out _, out int[] seats, out int row),
                        "compose with row");
                    Check.Equal(RowLength, row, "row length");
                    Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, boats, false,
                        out Formation.UnitTypes[] typesWithout, out _, out _, out int noRow),
                        "compose without row");
                    Check.Equal(0, noRow, "fleet-only layout");

                    float[] spacingWith = ExpandedSpacing(RealSpacing, boats, true);
                    float[] spacingWithout = ExpandedSpacing(RealSpacing, boats, false);
                    Check.Near(RealSpacing[(int)Formation.UnitTypes.Archer],
                        spacingWith[(int)Formation.UnitTypes.Squire], 1e-6,
                        "row step is written to the Squire entry");
                    Check.Near(RealSpacing[(int)Formation.UnitTypes.Squire],
                        spacingWithout[(int)Formation.UnitTypes.Squire], 1e-6,
                        "no row leaves the Squire entry authored");

                    var occupiedWith = new bool[typesWith.Length];
                    var occupiedWithout = new bool[typesWithout.Length];
                    for (int i = 0; i < typesWith.Length; i++) occupiedWith[i] = true;
                    for (int i = 0; i < typesWithout.Length; i++) occupiedWithout[i] = true;

                    foreach (bool left in new[] { false, true })
                    {
                        float reference(int index, Formation.UnitTypes[] source, bool[] occupied,
                                          float[] spacing, int boatsCount, bool withRow)
                            => Frontward(NativeX(source, occupied, spacing,
                                withRow ? -fleetShift : 0f, left, Mapped(index, boatsCount, withRow)), left);

                        for (int read = FirstArcherRead; read < RealBaseline.Length; read++)
                        {
                            Check.Near(
                                reference(read, typesWithout, occupiedWithout, spacingWithout, boats, false),
                                reference(read, typesWith, occupiedWith, spacingWith, boats, true), 1e-4,
                                "full row must keep the bow line on the baseline (left=" + left
                                + ", boats=" + boats + ", slot=" + read + ")");
                        }
                        for (int read = 0; read < FirstArcherRead; read++)
                        {
                            Check.Near(
                                reference(read, typesWithout, occupiedWithout, spacingWithout, boats, false) - fleetShift,
                                reference(read, typesWith, occupiedWith, spacingWith, boats, true), 1e-4,
                                "fleet block must move back one row (left=" + left
                                + ", boats=" + boats + ", slot=" + read + ")");
                        }

                        float archer1 = Frontward(NativeX(typesWith, occupiedWith, spacingWith,
                            -fleetShift, left, Mapped(FirstArcherRead, boats, true)), left);
                        float nearest = Frontward(NativeX(typesWith, occupiedWith, spacingWith,
                            -fleetShift, left, seats[RowLength - 1]), left);
                        Check.Near(rowStep, archer1 - nearest, 1e-4,
                            "full row boundary is one row step (left=" + left + ", boats=" + boats + ")");
                        for (int i = 0; i < RowLength - 1; i++)
                        {
                            float step = Frontward(NativeX(typesWith, occupiedWith, spacingWith,
                                -fleetShift, left, seats[i + 1]), left)
                                - Frontward(NativeX(typesWith, occupiedWith, spacingWith,
                                    -fleetShift, left, seats[i]), left);
                            Check.Near(rowStep, step, 1e-4,
                                "musketeers are spaced like archers (left=" + left + ", boats=" + boats + ")");
                        }
                    }
                }
            });

            Case.Run("an unfilled row compacts toward the fleet and stays one step off the bow line", () =>
            {
                float rowStep = RealSpacing[(int)Formation.UnitTypes.Archer];
                float fleetShift = RowLength * rowStep;

                foreach (int boats in new[] { 0, 1, 2, 4 })
                {
                    Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, boats, true,
                        out Formation.UnitTypes[] typesWith, out _, out int[] seats, out _),
                        "compose with row");
                    Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, boats, false,
                        out Formation.UnitTypes[] typesWithout, out _, out _, out _),
                        "compose without row");

                    float[] spacingWith = ExpandedSpacing(RealSpacing, boats, true);
                    float[] spacingWithout = ExpandedSpacing(RealSpacing, boats, false);
                    var originalsWith = new bool[typesWith.Length];
                    var originalsWithout = new bool[typesWithout.Length];
                    for (int i = 0; i < typesWith.Length; i++) originalsWith[i] = true;
                    for (int i = 0; i < typesWithout.Length; i++) originalsWithout[i] = true;
                    for (int i = 0; i < seats.Length; i++) originalsWith[seats[i]] = false;

                    foreach (bool left in new[] { false, true })
                    {
                        float baselineArcher1 = Frontward(NativeX(typesWithout, originalsWithout,
                            spacingWithout, 0f, left, Mapped(FirstArcherRead, boats, false)), left);
                        for (int count = 0; count <= RowLength; count++)
                        {
                            var occupied = (bool[])originalsWith.Clone();
                            for (int filled = 0; filled < count; filled++)
                                occupied[seats[RowLength - 1 - filled]] = true;   // production fill: bow side first

                            float archer1 = Frontward(NativeX(typesWith, occupied, spacingWith,
                                -fleetShift, left, Mapped(FirstArcherRead, boats, true)), left);
                            Check.Near(baselineArcher1 - (RowLength - count) * rowStep, archer1, 1e-4,
                                "missing seats pull the bow line toward the fleet (left=" + left
                                + ", boats=" + boats + ", count=" + count + ")");
                            if (count == 0) continue;

                            float nearest = Frontward(NativeX(typesWith, occupied, spacingWith,
                                -fleetShift, left, seats[RowLength - 1]), left);
                            Check.Near(rowStep, archer1 - nearest, 1e-4,
                                "nearest musketeer stays one step from the bow line (left=" + left
                                + ", boats=" + boats + ", count=" + count + ")");
                            for (int filled = 1; filled < count; filled++)
                            {
                                int seat = RowLength - 1 - filled;
                                float gap = Frontward(NativeX(typesWith, occupied, spacingWith,
                                    -fleetShift, left, seats[seat + 1]), left)
                                    - Frontward(NativeX(typesWith, occupied, spacingWith,
                                        -fleetShift, left, seats[seat]), left);
                                Check.Near(rowStep, gap, 1e-4,
                                    "filled musketeers keep the row step (left=" + left
                                    + ", boats=" + boats + ", count=" + count + ")");
                            }
                        }
                    }
                }
            });

            Case.Run("any row occupancy keeps the nearest musketeer one row step from the bow line", () =>
            {
                float rowStep = RealSpacing[(int)Formation.UnitTypes.Archer];
                float fleetShift = RowLength * rowStep;

                foreach (int boats in new[] { 0, 1, 2, 4 })
                {
                    Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, boats, true,
                        out Formation.UnitTypes[] types, out _, out int[] seats, out _),
                        "compose with row");
                    Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, boats, false,
                        out Formation.UnitTypes[] typesWithout, out _, out _, out _),
                        "compose without row");

                    float[] spacing = ExpandedSpacing(RealSpacing, boats, true);
                    float[] spacingWithout = ExpandedSpacing(RealSpacing, boats, false);
                    var originals = new bool[types.Length];
                    var originalsWithout = new bool[typesWithout.Length];
                    for (int i = 0; i < types.Length; i++) originals[i] = true;
                    for (int i = 0; i < typesWithout.Length; i++) originalsWithout[i] = true;
                    for (int i = 0; i < seats.Length; i++) originals[seats[i]] = false;

                    foreach (bool left in new[] { false, true })
                    {
                        float baselineArcher1 = Frontward(NativeX(typesWithout, originalsWithout,
                            spacingWithout, 0f, left, Mapped(FirstArcherRead, boats, false)), left);
                        for (int subset = 1; subset < (1 << RowLength); subset++)
                        {
                            var occupied = (bool[])originals.Clone();
                            int count = 0;
                            int nearest = -1;
                            for (int seat = 0; seat < RowLength; seat++)
                            {
                                if ((subset & (1 << seat)) == 0) continue;
                                occupied[seats[seat]] = true;
                                count++;
                                nearest = seat;
                            }

                            float archer1 = Frontward(NativeX(types, occupied, spacing,
                                -fleetShift, left, Mapped(FirstArcherRead, boats, true)), left);
                            float nearestX = Frontward(NativeX(types, occupied, spacing,
                                -fleetShift, left, seats[nearest]), left);
                            Check.Near(rowStep, archer1 - nearestX, 1e-4,
                                "one row step for any occupancy (left=" + left + ", boats=" + boats
                                + ", subset=" + subset + ")");
                            Check.Near(baselineArcher1 - (RowLength - count) * rowStep, archer1, 1e-4,
                                "bow line shift counts occupied seats only (left=" + left
                                + ", boats=" + boats + ", subset=" + subset + ")");
                        }
                    }
                }
            });
        }

        private static void DirectedPlan()
        {
            Case.Run("directed plan closes empty native bow seats only", () =>
            {
                var types = new[]
                {
                    Formation.UnitTypes.Gap, Formation.UnitTypes.Gap, Formation.UnitTypes.Gap,
                    Formation.UnitTypes.Squire, Formation.UnitTypes.Squire,
                    Formation.UnitTypes.Squire, Formation.UnitTypes.Squire,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.AnyShieldedUnit,
                    Formation.UnitTypes.Gap,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen
                };
                var occupied = new bool[types.Length];
                for (int i = 7; i < 9; i++) occupied[i] = true;     // native bow seats already held
                for (int i = 11; i < 15; i++) occupied[i] = true;   // pikemen seated
                var copy = (Formation.UnitTypes[])types.Clone();

                Check.True(MusketeerFormationLayout.TryPlanDirected(types, occupied, 5,
                    out Formation.UnitTypes[] temporary), "plan");
                Check.Equal(Formation.UnitTypes.Archer, temporary[5], "target advertises Archer");
                for (int i = 3; i <= 6; i++)
                {
                    Check.Equal(i == 5 ? Formation.UnitTypes.Archer : Formation.UnitTypes.Squire,
                        temporary[i], "empty row seat keeps Squire");
                }
                for (int i = 7; i < 9; i++)
                    Check.Equal(Formation.UnitTypes.Archer, temporary[i], "occupied bow seat keeps its type");
                Check.Equal(Formation.UnitTypes.Gap, temporary[9], "empty AnyShieldedUnit seat closed");
                Check.Equal(Formation.UnitTypes.Gap, temporary[10], "native gap untouched");
                for (int i = 11; i < 15; i++)
                    Check.Equal(Formation.UnitTypes.Pikemen, temporary[i], "pikemen seat untouched");
                Check.Sequence(copy, types, "input array not mutated");
            });

            Case.Run("directed plan refuses occupied or malformed targets", () =>
            {
                var types = new[] { Formation.UnitTypes.Gap, Formation.UnitTypes.Archer };
                var occupied = new bool[2];
                Check.False(MusketeerFormationLayout.TryPlanDirected(types, occupied, -1, out _), "negative target");
                Check.False(MusketeerFormationLayout.TryPlanDirected(types, occupied, 2, out _), "out of range");
                occupied[0] = true;
                Check.False(MusketeerFormationLayout.TryPlanDirected(types, occupied, 0, out _), "occupied target");
                Check.False(MusketeerFormationLayout.TryPlanDirected(types, new bool[1], 0, out _), "length mismatch");
                Check.False(MusketeerFormationLayout.TryPlanDirected(null, occupied, 0, out _), "null types");
            });
        }

        /// <summary>
        /// Effective live spacing the fleet owner writes for one expansion (mirrors TryExpand):
        /// the baseline copy, MultiBoatSpacing on the fleet entry for 2+ boats, and the row step
        /// (the Archer entry) on Squire whenever the composite carries row slots.
        /// </summary>
        private static float[] ExpandedSpacing(float[] baseline, int boatCount, bool rowPresent)
        {
            var spacing = (float[])baseline.Clone();
            if (boatCount >= 2) spacing[(int)Formation.UnitTypes.FleetBoat] = 1f;
            if (rowPresent)
                spacing[(int)Formation.UnitTypes.Squire] = baseline[(int)Formation.UnitTypes.Archer];
            return spacing;
        }

        /// <summary>Composite index of one RealBaseline slot (the row is inserted before the first Archer).</summary>
        private static int Mapped(int read, int boatCount, bool row)
        {
            int fleetFootprint = boatCount > 0 ? boatCount : 1;
            int before = read == 0 ? 0 : fleetFootprint + (read - 1);
            return before + (row && read >= FirstArcherRead ? RowLength : 0);
        }

        /// <summary>
        /// Native Formation.GetXPosForIndex replica (game-source Formation.cs:319-332): the offset
        /// accumulates startOffset + UnitSpacing[type] for every earlier slot that holds a unit or
        /// is a Gap, and the Left side mirrors the sign.
        /// </summary>
        private static float NativeX(Formation.UnitTypes[] types, bool[] occupied, float[] spacing,
            float startOffset, bool left, int index)
        {
            float num = startOffset;
            for (int i = 0; i < index; i++)
            {
                if (occupied[i] || types[i] == Formation.UnitTypes.Gap) num += spacing[(int)types[i]];
            }
            return left ? -num : num;
        }

        private static float Frontward(float x, bool left) => left ? -x : x;
    }
}
