// 弩手举旗后排直源测试：编译生产 il2cpp/Patch_MusketeerFormation.cs（共享纯布局/空闲门）
// 与 il2cpp/Patch_CrossbowFormation.cs（弩手候选策略），配替身（Stubs.cs）。
// 覆盖：两排合成长度/槽位/顺序、原生坐标（满员与缺员、镜像、0..4 船）、
// 弩手预留门与候选资格（身份/空闲门/塔/英雄/其他编队/死亡/被抓/上船/相互不偷槽）、
// nearest-first 与 4 席上限。真实 interop 签名由 actual-arm/interop 构建核对。
using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

namespace CrossbowBannerFormationTests
{
    internal static class Program
    {
        private static int Main()
        {
            LayoutComposition();
            LayoutCoordinates();
            PolicyGates();
            CandidateCollection();

            Console.WriteLine();
            Console.WriteLine("passed=" + Case.Passed + " failed=" + Case.Failed);
            for (int i = 0; i < Case.Failures.Count; i++)
                Console.WriteLine("  FAIL " + Case.Failures[i]);
            return Case.Failed == 0 ? 0 : 1;
        }

        // ---- layout ----------------------------------------------------------

        private const int RowSeats = 4;
        private const int FirstArcherRead = 3;   // FleetBoat, Gap, Gap, then the bow line

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

        private static void LayoutComposition()
        {
            Case.Run("both rows compose in front of the bow line with the crossbow row farther back", () =>
            {
                Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, 1, true, true,
                    out Formation.UnitTypes[] types, out int[] boats, out int[] musketeers,
                    out int[] crossbows, out int rowLength), "compose");
                Check.Equal(8, rowLength, "two rows");
                Check.Sequence(new[] { 0 }, boats, "boat slots");
                Check.Sequence(new[] { 3, 4, 5, 6 }, crossbows, "crossbow slots lead the row block");
                Check.Sequence(new[] { 7, 8, 9, 10 }, musketeers, "musketeer slots sit behind them");
                Check.Equal(20, types.Length, "composite length");
                Check.Sequence(new[]
                {
                    Formation.UnitTypes.FleetBoat, Formation.UnitTypes.Gap, Formation.UnitTypes.Gap,
                    Formation.UnitTypes.Squire, Formation.UnitTypes.Squire,
                    Formation.UnitTypes.Squire, Formation.UnitTypes.Squire,
                    Formation.UnitTypes.Squire, Formation.UnitTypes.Squire,
                    Formation.UnitTypes.Squire, Formation.UnitTypes.Squire,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Gap,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen
                }, types, "composite order");
            });

            Case.Run("cross-only and musk-only keep the single-row shapes", () =>
            {
                Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, 2, false, true,
                    out Formation.UnitTypes[] crossTypes, out _, out int[] musketeers,
                    out int[] crossbows, out int crossRow), "cross only");
                Check.Equal(4, crossRow, "cross row length");
                Check.Sequence(new[] { 4, 5, 6, 7 }, crossbows, "cross row after the two boats and gaps");
                Check.Equal(0, musketeers.Length, "no musk row");
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
                }, crossTypes, "cross-only order");

                Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, 2, true, false,
                    out Formation.UnitTypes[] muskTypes, out _, out int[] muskSlots,
                    out int[] noCross, out int muskRow), "musk only");
                Check.Equal(4, muskRow, "musk row length");
                Check.Sequence(new[] { 4, 5, 6, 7 }, muskSlots, "legacy musk slot layout is unchanged");
                Check.Equal(0, noCross.Length, "no cross row");
                Check.Equal(crossTypes.Length, muskTypes.Length, "same composite length");
            });

            Case.Run("zero boats closes the fleet seat ahead of both rows", () =>
            {
                Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, 0, true, true,
                    out Formation.UnitTypes[] types, out int[] boats, out _, out int[] crossbows, out int rowLength),
                    "compose");
                Check.Equal(8, rowLength, "two rows");
                Check.Equal(0, boats.Length, "no boat slots");
                Check.Sequence(new[] { 3, 4, 5, 6 }, crossbows, "cross row starts after the closed fleet seat");
                Check.Sequence(new[]
                {
                    Formation.UnitTypes.Gap,
                    Formation.UnitTypes.Gap, Formation.UnitTypes.Gap,
                    Formation.UnitTypes.Squire, Formation.UnitTypes.Squire,
                    Formation.UnitTypes.Squire, Formation.UnitTypes.Squire,
                    Formation.UnitTypes.Squire, Formation.UnitTypes.Squire,
                    Formation.UnitTypes.Squire, Formation.UnitTypes.Squire,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer,
                    Formation.UnitTypes.Gap,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen
                }, types, "closed-fleet order");
            });

            Case.Run("a baseline without an archer slot skips both rows but keeps the fleet plan", () =>
            {
                var baseline = new[]
                {
                    Formation.UnitTypes.FleetBoat,
                    Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen
                };
                Check.True(MusketeerFormationLayout.TryCompose(baseline, 1, true, true,
                    out Formation.UnitTypes[] types, out _, out int[] musketeers, out int[] crossbows,
                    out int rowLength), "compose");
                Check.Equal(0, rowLength, "rows skipped");
                Check.Equal(0, musketeers.Length, "no musk row");
                Check.Equal(0, crossbows.Length, "no cross row");
                Check.Sequence(new[]
                {
                    Formation.UnitTypes.FleetBoat, Formation.UnitTypes.Pikemen, Formation.UnitTypes.Pikemen
                }, types, "baseline order");
            });

            Case.Run("malformed baselines and inputs fail closed without mutating the baseline", () =>
            {
                var copy = (Formation.UnitTypes[])RealBaseline.Clone();
                Check.False(MusketeerFormationLayout.TryCompose(null, 0, true, true,
                    out _, out _, out _, out _, out _), "null baseline");
                Check.False(MusketeerFormationLayout.TryCompose(new Formation.UnitTypes[0], 0, true, true,
                    out _, out _, out _, out _, out _), "empty baseline");
                Check.False(MusketeerFormationLayout.TryCompose(new[]
                {
                    Formation.UnitTypes.Archer, Formation.UnitTypes.Archer
                }, 0, true, true, out _, out _, out _, out _, out _), "missing fleet seat");
                Check.False(MusketeerFormationLayout.TryCompose(RealBaseline, -1, true, true,
                    out _, out _, out _, out _, out _), "negative boats");
                Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, 2, true, true,
                    out _, out _, out _, out _, out _), "compose for mutation check");
                Check.Sequence(copy, RealBaseline, "baseline untouched");
            });
        }

        private static void LayoutCoordinates()
        {
            Case.Run("full rows keep the bow line and shift the fleet by both rows on both sides", () =>
            {
                float step = RealSpacing[(int)Formation.UnitTypes.Archer];
                foreach (int boats in new[] { 0, 1, 2, 4 })
                {
                    foreach (bool left in new[] { false, true })
                    {
                        Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, boats, true, true,
                            out Formation.UnitTypes[] types, out _, out int[] muskSlots,
                            out int[] crossSlots, out int rowLength), "compose");
                        float[] spacing = ExpandedSpacing(RealSpacing, boats, rowLength);
                        var occupied = new bool[types.Length];
                        for (int i = 0; i < occupied.Length; i++) occupied[i] = true;
                        float shift = rowLength * step;

                        Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, boats, false, false,
                            out Formation.UnitTypes[] reference, out _, out _, out _, out _), "reference plan");
                        float baselineArcher1 = Frontward(NativeX(reference, AllOccupied(reference),
                            ExpandedSpacing(RealSpacing, boats, 0), 0f, left, Mapped(FirstArcherRead, boats, 0)), left);
                        float archer1 = Frontward(NativeX(types, occupied, spacing, -shift, left,
                            Mapped(FirstArcherRead, boats, rowLength)), left);
                        Check.Near(baselineArcher1, archer1, 1e-4,
                            "bow line at baseline (left=" + left + ", boats=" + boats + ")");

                        float fleet = Frontward(NativeX(types, occupied, spacing, -shift, left, 0), left);
                        float baselineFleet = Frontward(NativeX(reference, AllOccupied(reference),
                            ExpandedSpacing(RealSpacing, boats, 0), 0f, left, 0), left);
                        Check.Near(baselineFleet - shift, fleet, 1e-4,
                            "fleet shifted by both rows (left=" + left + ", boats=" + boats + ")");

                        float nearestMusk = Frontward(NativeX(types, occupied, spacing, -shift, left,
                            muskSlots[RowSeats - 1]), left);
                        float nearestCross = Frontward(NativeX(types, occupied, spacing, -shift, left,
                            crossSlots[RowSeats - 1]), left);
                        Check.Near(step, archer1 - nearestMusk, 1e-4, "musk row one step off the bow line");
                        Check.Near(4 * step, nearestMusk - nearestCross, 1e-4,
                            "cross row one row behind the musk row");
                    }
                }
            });

            Case.Run("cross understrength compacts toward the bow line per native empty-slot rule", () =>
            {
                float step = RealSpacing[(int)Formation.UnitTypes.Archer];
                foreach (int boats in new[] { 0, 2 })
                {
                    Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, boats, true, true,
                        out Formation.UnitTypes[] types, out _, out int[] muskSlots,
                        out int[] crossSlots, out int rowLength), "compose");
                    float[] spacing = ExpandedSpacing(RealSpacing, boats, rowLength);
                    Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, boats, false, false,
                        out Formation.UnitTypes[] reference, out _, out _, out _, out _), "reference plan");
                    float baselineArcher1 = NativeX(reference, AllOccupied(reference),
                        ExpandedSpacing(RealSpacing, boats, 0), 0f, false, Mapped(FirstArcherRead, boats, 0));
                    for (int count = 0; count <= RowSeats; count++)
                    {
                        var occupied = new bool[types.Length];
                        for (int i = 0; i < occupied.Length; i++) occupied[i] = true;
                        for (int seat = 0; seat < crossSlots.Length; seat++) occupied[crossSlots[seat]] = false;
                        for (int filled = 0; filled < count; filled++)
                            occupied[crossSlots[RowSeats - 1 - filled]] = true;   // bow side first

                        float archer1 = NativeX(types, occupied, spacing, -rowLength * step, false,
                            Mapped(FirstArcherRead, boats, rowLength));
                        Check.Near(baselineArcher1 - (RowSeats - count) * step, archer1, 1e-4,
                            "missing cross seats pull the bow line back (boats=" + boats + ", count=" + count + ")");
                        if (count == 0) continue;
                        float nearest = NativeX(types, occupied, spacing, -rowLength * step, false,
                            crossSlots[RowSeats - 1]);
                        // The musketeer row is fully occupied ahead of the crossbow row, so the
                        // bow-side crossbow seat sits one musk row plus one step off the bow line.
                        Check.Near((RowSeats + 1) * step, archer1 - nearest, 1e-4,
                            "nearest crossbowman one row plus a step off the bow line (count=" + count + ")");
                        for (int filled = 1; filled < count; filled++)
                        {
                            int seat = RowSeats - 1 - filled;
                            float rowStep = NativeX(types, occupied, spacing, -rowLength * step, false,
                                crossSlots[seat + 1])
                                - NativeX(types, occupied, spacing, -rowLength * step, false,
                                    crossSlots[seat]);
                            Check.Near(step, rowStep, 1e-4,
                                "filled cross seats keep the row step (count=" + count + ")");
                        }
                    }
                }
            });

            Case.Run("cross-only degrades to the bow line and keeps native foot/arch slots", () =>
            {
                float step = RealSpacing[(int)Formation.UnitTypes.Archer];
                Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, 0, false, true,
                    out Formation.UnitTypes[] types, out _, out _, out int[] crossSlots, out int rowLength),
                    "compose");
                Check.Equal(4, rowLength, "single row");
                var occupied = new bool[types.Length];
                for (int i = 0; i < occupied.Length; i++) occupied[i] = true;
                float[] spacing = ExpandedSpacing(RealSpacing, 0, rowLength);
                Check.True(MusketeerFormationLayout.TryCompose(RealBaseline, 0, false, false,
                    out Formation.UnitTypes[] reference, out _, out _, out _, out _), "reference plan");
                float baselineArcher1 = NativeX(reference, AllOccupied(reference),
                    ExpandedSpacing(RealSpacing, 0, 0), 0f, false, Mapped(FirstArcherRead, 0, 0));
                float archer1 = NativeX(types, occupied, spacing, -rowLength * step, false,
                    Mapped(FirstArcherRead, 0, rowLength));
                Check.Near(baselineArcher1, archer1, 1e-4, "bow line at baseline");
                float nearest = NativeX(types, occupied, spacing, -rowLength * step, false,
                    crossSlots[RowSeats - 1]);
                Check.Near(step, archer1 - nearest, 1e-4, "cross row bows up to the bow line");
            });
        }

        // ---- policy ----------------------------------------------------------

        private static void PolicyGates()
        {
            Case.Run("row requested for every enabled, offline world regardless of crossbow careers", () =>
            {
                Fixture.Reset();
                Check.True(PatchCrossbowFormation.RowRequested,
                    "zero crossbowmen still reserve the row (later careers join by maintenance)");

                MusketeerAccess.TrackAllowedFlag = false;
                Check.False(PatchCrossbowFormation.RowRequested, "no authority/online");
                MusketeerAccess.TrackAllowedFlag = true;

                ModConfig.Enabled.Value = false;
                Check.False(PatchCrossbowFormation.RowRequested, "mod disabled");
                ModConfig.Enabled.Value = true;

                Archer crossbowman = Fixture.NewCrossbowman(1f);
                Check.True(PatchCrossbowFormation.RowRequested, "owned crossbowman keeps the row");

                CrossbowmanLifecycle.IdentityEnabled = false;
                Check.True(PatchCrossbowFormation.RowRequested,
                    "identity state never gates the reservation (R1 contract)");
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "identity off blocks candidacy");
            });

            Case.Run("eligibility requires live crossbow identity plus every native-free gate", () =>
            {
                Fixture.Reset();
                Archer crossbowman = Fixture.NewCrossbowman(1f);
                Check.True(PatchCrossbowFormation.IsEligible(crossbowman), "plain crossbowman eligible");

                Check.False(PatchCrossbowFormation.IsEligible(Fixture.NewArcher(2f, marked: false)),
                    "ordinary archer is not a crossbowman");
                Check.False(PatchCrossbowFormation.IsEligible(Fixture.NewArcher(2f)), "marked musketeer excluded");

                CrossbowmanLifecycle.IdentityEnabled = false;
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "identity off");
                CrossbowmanLifecycle.IdentityEnabled = true;

                MusketeerAccess.InWorldResult = false;
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "outside the current world");
                MusketeerAccess.InWorldResult = true;

                crossbowman.gameObject.activeInHierarchy = false;
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "inactive");
                crossbowman.gameObject.activeInHierarchy = true;

                crossbowman.enabled = false;
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "disabled");
                crossbowman.enabled = true;

                crossbowman.formation = Fixture.NewFormation(0f);
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "already in a formation");
                crossbowman.formation = null;

                crossbowman._knight = new Knight();
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "knight follower");
                crossbowman._knight = null;

                crossbowman._guardSlot = new GuardSlot();
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "guard post");
                crossbowman._guardSlot = null;
                crossbowman.inGuardSlot = true;
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "in guard slot");
                crossbowman.inGuardSlot = false;

                crossbowman._embarkee.IsEmbarked = true;
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "embarked");
                crossbowman._embarkee.IsEmbarked = false;
                crossbowman._embarkee.EmbarkableTarget = new Embarkable();
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "boarding target");
                crossbowman._embarkee.EmbarkableTarget = null;

                crossbowman.playerControlled = true;
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "player controlled");
                crossbowman.playerControlled = false;

                crossbowman._character.inert = true;
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "inert");
                crossbowman._character.inert = false;
                crossbowman._character.grabbed = true;
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "grabbed");
                crossbowman._character.grabbed = false;

                crossbowman._damageable.isDead = true;
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "dead");
                crossbowman._damageable.isDead = false;

                crossbowman.harmless = true;
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "harmless/hidden");
                crossbowman.harmless = false;

                HeroArcherRuntime.Heroes.Add(crossbowman);
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "hero");
                HeroArcherRuntime.Heroes.Clear();

                Check.True(PatchCrossbowFormation.IsEligible(crossbowman), "all gates restored");
            });

            Case.Run("a crossbowman marked as a paid musketeer career is excluded from both rows", () =>
            {
                Fixture.Reset();
                Archer crossbowman = Fixture.NewCrossbowman(1f);
                MusketeerIdentity.Units.Add(crossbowman);
                Check.False(PatchCrossbowFormation.IsEligible(crossbowman), "cross row refuses the dual career");
                Check.False(PatchMusketeerFormation.IsEligible(crossbowman), "musk row refuses the dual career");
            });
        }

        private static void CandidateCollection()
        {
            Case.Run("collect returns nearest-first, capped at four, registry-only", () =>
            {
                Fixture.Reset();
                CrossbowmanLifecycle.IdentityEnabled = true;
                Formation formation = Fixture.NewFormation(0f);
                Archer far = Fixture.NewCrossbowman(40f);
                Archer tooFar = Fixture.NewCrossbowman(60f);
                Archer near1 = Fixture.NewCrossbowman(1f);
                Archer near2 = Fixture.NewCrossbowman(-2f);
                Archer near3 = Fixture.NewCrossbowman(-1f);
                Archer near4 = Fixture.NewCrossbowman(2f);
                Archer unmarked = Fixture.NewArcher(3f, marked: false);

                var output = new List<Archer>();
                Check.Equal(4, PatchCrossbowFormation.Collect(formation, 4, output), "collect four");
                Check.True(output[0] == near1, "nearest first");
                Check.True(output.IndexOf(near2) >= 0 && output.IndexOf(near3) >= 0 && output.IndexOf(near4) >= 0,
                    "four nearest taken");
                Check.False(output.Contains(far), "farther candidate dropped");
                Check.False(output.Contains(tooFar), "farthest candidate dropped");
                Check.False(output.Contains(unmarked), "unregistered archer never returned");

                Check.Equal(2, PatchCrossbowFormation.Collect(formation, 2, output), "free slots cap");
                Check.True(output[0] == near1, "nearest still first");

                // Ineligible registry members are skipped, not waited on.
                Fixture.Reset();
                CrossbowmanLifecycle.IdentityEnabled = true;
                Formation formation2 = Fixture.NewFormation(0f);
                Archer tower = Fixture.NewCrossbowman(1f);
                tower._guardSlot = new GuardSlot();
                Archer live = Fixture.NewCrossbowman(2f);
                Check.Equal(1, PatchCrossbowFormation.Collect(formation2, 4, output), "one eligible");
                Check.True(output[0] == live, "only the free crossbowman");

                Check.Equal(0, PatchCrossbowFormation.Collect(formation2, 0, output), "no free slots");
                Check.Equal(0, PatchCrossbowFormation.Collect(null, 4, output), "no formation");
            });
        }

        // ---- native position replica (game-source Formation.GetXPosForIndex) ----

        private static float[] ExpandedSpacing(float[] baseline, int boatCount, int rowLength)
        {
            var spacing = (float[])baseline.Clone();
            if (boatCount >= 2) spacing[(int)Formation.UnitTypes.FleetBoat] = 1f;
            if (rowLength > 0)
                spacing[(int)Formation.UnitTypes.Squire] = baseline[(int)Formation.UnitTypes.Archer];
            return spacing;
        }

        private static bool[] AllOccupied(Formation.UnitTypes[] types)
        {
            var occupied = new bool[types.Length];
            for (int i = 0; i < occupied.Length; i++) occupied[i] = true;
            return occupied;
        }

        private static int Mapped(int read, int boatCount, int rowLength)
        {
            int fleetFootprint = boatCount > 0 ? boatCount : 1;
            int before = read == 0 ? 0 : fleetFootprint + (read - 1);
            return before + (read >= FirstArcherRead ? rowLength : 0);
        }

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
