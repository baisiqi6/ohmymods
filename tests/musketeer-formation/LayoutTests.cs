using KingdomEnhancedMod;

namespace MusketeerFormationTests
{
    internal static class LayoutTests
    {
        private const float GapStep = 1.3f;
        private static readonly float[] StepSpacing = BuildStepSpacing();

        internal static void Run()
        {
            Composition();
            Placement();
            DirectedPlan();
        }

        private static void Composition()
        {
            Case.Run("row sits at the rear and the baseline keeps its order", () =>
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
                Check.Sequence(new[] { 0, 1, 2, 3 }, musketeers, "row slots");
                Check.Sequence(new[] { 4, 5, 6 }, boats, "boat slots");
                Check.Equal(15, types.Length, "composite length");
                for (int i = 0; i < 4; i++)
                    Check.Equal(Formation.UnitTypes.Gap, types[i], "row slot type");
                for (int i = 0; i < 3; i++)
                    Check.Equal(Formation.UnitTypes.FleetBoat, types[4 + i], "boat slot type");
                for (int i = 0; i < 4; i++)
                    Check.Equal(Formation.UnitTypes.Archer, types[7 + i], "archer order");
                for (int i = 0; i < 4; i++)
                    Check.Equal(Formation.UnitTypes.Pikemen, types[11 + i], "pikemen order");
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
                Check.Equal(4, musketeers.Length, "row slots");
                Check.Equal(7, types.Length, "composite length");
                Check.Sequence(new[]
                {
                    Formation.UnitTypes.Gap, Formation.UnitTypes.Gap, Formation.UnitTypes.Gap, Formation.UnitTypes.Gap,
                    Formation.UnitTypes.Gap, Formation.UnitTypes.Archer, Formation.UnitTypes.Pikemen
                }, types, "closed fleet seat");
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
            Case.Run("row compensation preserves every original coordinate", () =>
            {
                var baseline = new[]
                {
                    Formation.UnitTypes.FleetBoat,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen
                };
                Check.True(MusketeerFormationLayout.TryCompose(baseline, 1, true,
                    out Formation.UnitTypes[] types, out _, out _, out int row), "compose");

                float baselineOffset = 2.5f;
                float expandedOffset = baselineOffset - row * GapStep;
                var baselineOccupied = new bool[baseline.Length];
                var expandedOccupied = new bool[types.Length];
                for (int i = 0; i < baseline.Length; i++) baselineOccupied[i] = true;
                for (int i = 0; i < baseline.Length; i++) expandedOccupied[i + row] = true;

                foreach (bool left in new[] { false, true })
                {
                    for (int i = 0; i < baseline.Length; i++)
                    {
                        Check.Near(
                            NativeX(baseline, baselineOccupied, baselineOffset, left, i),
                            NativeX(types, expandedOccupied, expandedOffset, left, i + row), 1e-4,
                            "original coordinate must not move (left=" + left + ", slot=" + i + ")");
                    }
                }
            });

            Case.Run("real 2.4 player baseline keeps every original coordinate", () =>
            {
                // Operator-provided 2.4 Player formation resource:
                // unitTypes [FleetBoat, Gap, Gap, Archer x4, Gap, Pikemen x4] with the spacing below.
                var baseline = new[]
                {
                    Formation.UnitTypes.FleetBoat, Formation.UnitTypes.Gap, Formation.UnitTypes.Gap,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Gap,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen
                };
                var spacing = new[]
                {
                    0.21875f, 0.25f, 0.25f, 0.21875f, 1f, 0.34375f, 1f,
                    1f, 0.21875f, 0.21875f, 0.21875f, 0f, 0f
                };
                Check.True(MusketeerFormationLayout.TryCompose(baseline, 1, true,
                    out Formation.UnitTypes[] types, out _, out int[] musketeers, out int row), "compose");
                Check.Equal(4, row, "row length");
                Check.Sequence(new[] { 0, 1, 2, 3 }, musketeers, "row slots");

                float baselineOffset = 0f;                                 // real prefab startOffset
                float rowStep = spacing[(int)Formation.UnitTypes.Gap];     // 0.34375
                float expandedOffset = baselineOffset - row * rowStep;
                var baselineOccupied = new bool[baseline.Length];
                var expandedOccupied = new bool[types.Length];
                for (int i = 0; i < baseline.Length; i++) baselineOccupied[i] = true;
                for (int i = 0; i < baseline.Length; i++) expandedOccupied[i + row] = true;
                expandedOccupied[musketeers[0]] = true;                    // one musketeer in the rear seat

                foreach (bool left in new[] { false, true })
                {
                    for (int i = 0; i < baseline.Length; i++)
                    {
                        Check.Near(
                            NativeX(baseline, baselineOccupied, spacing, baselineOffset, left, i),
                            NativeX(types, expandedOccupied, spacing, expandedOffset, left, i + row), 1e-4,
                            "2.4 original coordinate must not move (left=" + left + ", slot=" + i + ")");
                    }
                    for (int r = 0; r < musketeers.Length; r++)
                    {
                        float seat = Frontward(NativeX(types, expandedOccupied, spacing,
                            expandedOffset, left, musketeers[r]), left);
                        float rear = Frontward(NativeX(types, expandedOccupied, spacing,
                            expandedOffset, left, row), left);
                        Check.True(seat < rear - 1e-4f,
                            "2.4 seat " + r + " must stay behind the native rear line (left=" + left + ")");
                    }
                }
            });

            Case.Run("row stays behind archers and infantry on both sides", () =>
            {
                var baseline = new[]
                {
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen,
                    Formation.UnitTypes.FleetBoat
                };
                Check.True(MusketeerFormationLayout.TryCompose(baseline, 1, true,
                    out Formation.UnitTypes[] types, out _, out int[] musketeers, out int row), "compose");

                float baselineOffset = 1.5f;
                float expandedOffset = baselineOffset - row * GapStep;
                var occupied = new bool[types.Length];
                for (int i = row; i < types.Length; i++) occupied[i] = true;   // every original unit present
                occupied[musketeers[2]] = true;                                 // two musketeers in the row
                occupied[musketeers[3]] = true;

                foreach (bool left in new[] { false, true })
                {
                    for (int r = 0; r < musketeers.Length; r++)
                    {
                        float seat = Frontward(NativeX(types, occupied, expandedOffset, left, musketeers[r]), left);
                        for (int i = row; i < types.Length; i++)
                        {
                            Check.True(
                                seat < Frontward(NativeX(types, occupied, expandedOffset, left, i), left) - 1e-4f,
                                "row seat " + r + " must stay behind slot " + i + " (left=" + left + ")");
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
                    Formation.UnitTypes.Gap, Formation.UnitTypes.Gap, Formation.UnitTypes.Gap, Formation.UnitTypes.Gap,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.AnyShieldedUnit,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.FleetBoat
                };
                var occupied = new bool[types.Length];
                for (int i = 4; i < 8; i++) occupied[i] = true;   // native bow seats already held by ordinary archers
                for (int i = 9; i < 11; i++) occupied[i] = true;
                var copy = (Formation.UnitTypes[])types.Clone();

                Check.True(MusketeerFormationLayout.TryPlanDirected(types, occupied, 3,
                    out Formation.UnitTypes[] temporary), "plan");
                Check.Equal(Formation.UnitTypes.Archer, temporary[3], "target advertises Archer");
                for (int i = 0; i < 3; i++)
                    Check.Equal(Formation.UnitTypes.Gap, temporary[i], "empty row seats stay closed");
                for (int i = 4; i < 8; i++)
                    Check.Equal(Formation.UnitTypes.Archer, temporary[i], "occupied bow seat keeps its type");
                Check.Equal(Formation.UnitTypes.Gap, temporary[8], "empty AnyShieldedUnit seat closed");
                Check.Equal(Formation.UnitTypes.Pikemen, temporary[9], "pikemen seat untouched");
                Check.Equal(Formation.UnitTypes.FleetBoat, temporary[10], "fleet seat untouched");
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
        /// Native Formation.GetXPosForIndex replica (game-source Formation.cs:319-332): the offset
        /// accumulates startOffset + UnitSpacing[type] for every earlier slot that holds a unit or
        /// is a Gap, and the Left side mirrors the sign. Used to pin that the rear row does not
        /// move the original units and always sits behind them.
        /// </summary>
        private static float NativeX(Formation.UnitTypes[] types, bool[] occupied,
            float startOffset, bool left, int index)
            => NativeX(types, occupied, StepSpacing, startOffset, left, index);

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

        private static float[] BuildStepSpacing()
        {
            var spacing = new float[(int)Formation.UnitTypes.Total];
            spacing[(int)Formation.UnitTypes.Archer] = 0.7f;
            spacing[(int)Formation.UnitTypes.Knight] = 0.7f;
            spacing[(int)Formation.UnitTypes.Squire] = 0.7f;
            spacing[(int)Formation.UnitTypes.Pikemen] = 0.7f;
            spacing[(int)Formation.UnitTypes.Bomb] = 0.5f;
            spacing[(int)Formation.UnitTypes.Gap] = GapStep;
            spacing[(int)Formation.UnitTypes.Player] = 0f;
            spacing[(int)Formation.UnitTypes.Catapult] = 1f;
            spacing[(int)Formation.UnitTypes.Ninja] = 0.7f;
            spacing[(int)Formation.UnitTypes.Worker] = 0.7f;
            spacing[(int)Formation.UnitTypes.AnyShieldedUnit] = 0.7f;
            spacing[(int)Formation.UnitTypes.FleetBoat] = 1f;
            return spacing;
        }
    }
}
