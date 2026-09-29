using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

namespace MapWidthTests
{
    internal static class PlannerTests
    {
        internal static void Run()
        {
            Console.WriteLine("planner:");

            Case.Run("multiplier 1 is a no-op plan", () =>
            {
                MapWidthPlan plan = MapWidthPlanner.Plan(100, 1f, Candidates.Standard());
                Check.True(plan.Success, "m=1 must succeed");
                Check.Equal(0, plan.AddedWidth, "m=1 adds nothing");
                Check.Equal(100, plan.PlannedWidth, "m=1 keeps baseline");
                Check.Equal(0, plan.Paddings.Length, "m=1 has no paddings");
            });

            Case.Run("invalid multiplier fails closed", () =>
            {
                foreach (float multiplier in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
                {
                    MapWidthPlan plan = MapWidthPlanner.Plan(100, multiplier, Candidates.Standard());
                    Check.False(plan.Success, "multiplier " + multiplier + " must fail");
                    Check.Equal("multiplier-invalid", plan.Failure, "multiplier failure reason");
                }
            });

            Case.Run("invalid baseline and target overflow fail closed", () =>
            {
                Check.Equal("baseline-out-of-range",
                    MapWidthPlanner.Plan(-1, 2f, Candidates.Standard()).Failure, "negative baseline");
                Check.Equal("baseline-out-of-range",
                    MapWidthPlanner.Plan(MapWidthPlanner.MaxTotalWidth + 1, 1f, Candidates.Standard()).Failure,
                    "baseline beyond cap");
                Check.Equal("target-out-of-range",
                    MapWidthPlanner.Plan(MapWidthPlanner.MaxTotalWidth, 2f, Candidates.Standard()).Failure,
                    "target beyond cap");
            });

            Case.Run("exact 2x uses an exact representable combination", () =>
            {
                // 84 = 12+12+20+20+20（也等于 20+16*4）：必须命中精确解。
                MapWidthPlan plan = MapWidthPlanner.Plan(84, 2f, Candidates.Standard());
                Check.True(plan.Success, "must succeed");
                Check.Equal(84, plan.AddedWidth, "added width equals need");
                Check.Equal(168, plan.PlannedWidth, "planned width is exactly 2x");
                Check.Equal(0.0, plan.TargetWidth - plan.PlannedWidth, "exact solution error is zero");
            });

            Case.Run("exact 5x on real widths", () =>
            {
                MapWidthPlan plan = MapWidthPlanner.Plan(40, 5f, Candidates.Standard());
                Check.True(plan.Success, "must succeed");
                Check.Equal(160, plan.AddedWidth, "5x of 40 needs +160");
                Check.Equal(200, plan.PlannedWidth, "planned width 5x");
            });

            Case.Run("fractional multiplier snaps to nearest representable width", () =>
            {
                // W0=30, m=1.5 → 目标 45，需 +15：16（误差1）胜过 12（误差3）。
                MapWidthPlan plan = MapWidthPlanner.Plan(30, 1.5f, Candidates.Standard());
                Check.True(plan.Success, "must succeed");
                Check.Equal(16, plan.AddedWidth, "nearest representable added width");
                Check.Equal(46, plan.PlannedWidth, "planned width");
                Check.Equal(1.0, Math.Abs(plan.PlannedWidth - 45.0), "error 1 unit");
            });

            Case.Run("equidistant widths round up", () =>
            {
                // 目标 28，需 +14：12 与 16 等距（各差 2）→ 并列向上取 16。
                MapWidthPlan plan = MapWidthPlanner.Plan(14, 2f, Candidates.Standard());
                Check.True(plan.Success, "must succeed");
                Check.Equal(16, plan.AddedWidth, "tie must round up");
                Check.Equal(30, plan.PlannedWidth, "planned width after tie-up");
            });

            Case.Run("sub-grid need rounds down to no padding", () =>
            {
                // W0=100, m=1.02 → 目标 102，需 +2：0（差2）优于 8（差6）。
                MapWidthPlan plan = MapWidthPlanner.Plan(100, 1.02f, Candidates.Standard());
                Check.True(plan.Success, "must succeed");
                Check.Equal(0, plan.AddedWidth, "below half-grid need keeps native width");
                Check.Equal(100, plan.PlannedWidth, "planned width stays");
            });

            Case.Run("clearing preferred over forest for usable land", () =>
            {
                // 需 +24：12+12（全 Clearing）优先于 8+16（含 Forest）。
                MapWidthPlan first = MapWidthPlanner.Plan(100, 1f + 24f / 100f, Candidates.Standard());
                Check.True(first.Success, "must succeed");
                Check.Equal(24, first.AddedWidth, "added width");
                Check.Equal(2, first.Paddings.Length, "two blocks");
                foreach (int index in first.Paddings)
                {
                    Check.True(Candidates.Standard()[index].IsClearing, "must use clearing blocks for +24");
                }

                // 需 +28：16+12（全 Clearing）优先于 20+8（含 Forest）。
                MapWidthPlan second = MapWidthPlanner.Plan(100, 1.28f, Candidates.Standard());
                Check.True(second.Success, "must succeed");
                Check.Equal(28, second.AddedWidth, "added width");
                foreach (int index in second.Paddings)
                {
                    Check.True(Candidates.Standard()[index].IsClearing, "must avoid forest at +28");
                }
            });

            Case.Run("forest fills only when needed", () =>
            {
                MapWidthPlan plan = MapWidthPlanner.Plan(100, 1.08f, Candidates.Standard());
                Check.True(plan.Success, "must succeed");
                Check.Equal(8, plan.AddedWidth, "8 must come from forest");
                Check.Equal(1, plan.Paddings.Length, "single block");
                Check.False(Candidates.Standard()[plan.Paddings[0]].IsClearing, "the only width-8 candidate");
            });

            Case.Run("norse widths solve exactly", () =>
            {
                // Norse 无 Small：44 = 20+16+8。
                MapWidthPlan plan = MapWidthPlanner.Plan(44, 2f, Candidates.Norse());
                Check.True(plan.Success, "must succeed");
                Check.Equal(44, plan.AddedWidth, "exact norse combination");
                Check.Equal(88, plan.PlannedWidth, "planned width 2x");
            });

            Case.Run("error stays within half of the smallest block", () =>
            {
                foreach (int baseline in new[] { 84, 132, 300, 977 })
                {
                    for (int step = 101; step <= 500; step++)
                    {
                        float multiplier = step / 100f;
                        MapWidthPlan plan = MapWidthPlanner.Plan(baseline, multiplier, Candidates.Standard());
                        if (!plan.Success) throw new Exception("baseline=" + baseline + " m=" + multiplier + " failed: " + plan.Failure);
                        double error = Math.Abs(plan.PlannedWidth - (double)baseline * multiplier);
                        if (error > 4.0 + 1e-9) throw new Exception("error " + error + " at baseline=" + baseline + " m=" + multiplier);
                        int sum = 0;
                        for (int i = 0; i < plan.Paddings.Length; i++) sum += Candidates.Standard()[plan.Paddings[i]].Width;
                        if (sum != plan.AddedWidth) throw new Exception("paddings sum mismatch");
                    }
                }
            });

            Case.Run("input ordering does not change the plan", () =>
            {
                MapWidthPlan basePlan = MapWidthPlanner.Plan(150, 1.4f, Candidates.Standard());
                Check.True(basePlan.Success, "baseline plan");
                var reversed = Candidates.Standard();
                reversed.Reverse();
                MapWidthPlan other = MapWidthPlanner.Plan(150, 1.4f, reversed);
                Check.True(other.Success, "permuted plan");
                Check.Equal(basePlan.AddedWidth, other.AddedWidth, "same added width");
                if (basePlan.Paddings.Length != other.Paddings.Length) throw new Exception("padding count differs");
                for (int i = 0; i < basePlan.Paddings.Length; i++)
                {
                    Check.Equal(Candidates.Standard()[basePlan.Paddings[i]].Width,
                        reversed[other.Paddings[i]].Width, "same padding width sequence");
                }
            });

            Case.Run("normal big island at the cap is not rejected by a small limit", () =>
            {
                // W0 = 2^19（远超任何正常岛），2x 目标恰在上限内：必须成功而不是被小上限挡掉。
                MapWidthPlan plan = MapWidthPlanner.Plan(1 << 19, 2f, Candidates.Standard());
                Check.True(plan.Success, "baseline at half cap must still plan: " + plan.Failure);
                Check.Equal(1 << 19, plan.AddedWidth, "exact at cap");
            });

            Case.Run("no candidates fails", () =>
            {
                Check.Equal("no-candidates",
                    MapWidthPlanner.Plan(100, 2f, new List<MapWidthCandidate>()).Failure, "empty candidate list");
                var invalid = new List<MapWidthCandidate>
                {
                    new MapWidthCandidate("zero|5|0", 0, true),
                    new MapWidthCandidate("neg|5|-8", -8, true),
                    new MapWidthCandidate("huge|5|" + (MapWidthPlanner.MaxTotalWidth + 1), MapWidthPlanner.MaxTotalWidth + 1, true),
                };
                Check.Equal("no-candidates", MapWidthPlanner.Plan(100, 2f, invalid).Failure, "all invalid candidates");
            });

            Case.Run("invalid candidates are ignored, valid ones still used", () =>
            {
                var mixed = new List<MapWidthCandidate>
                {
                    new MapWidthCandidate("zero|5|0", 0, true),
                    new MapWidthCandidate("Clearing Small_Blocks|5|12", 12, true),
                };
                MapWidthPlan plan = MapWidthPlanner.Plan(100, 1.24f, mixed);
                Check.True(plan.Success, "valid candidate remains");
                Check.Equal(24, plan.AddedWidth, "12+12");
                foreach (int index in plan.Paddings) Check.Equal(12, mixed[index].Width, "only the valid width");
            });

            Case.Run("seam assignment is deterministic round robin", () =>
            {
                int[] slots = MapWidthPlanner.AssignSeams(7, 3);
                Check.Equal(7, slots.Length, "slot count");
                for (int i = 0; i < slots.Length; i++) Check.Equal(i % 3, slots[i], "slot " + i);
                Check.Equal(0, MapWidthPlanner.AssignSeams(0, 5).Length, "no paddings");
                bool threw = false;
                try { MapWidthPlanner.AssignSeams(3, 0); }
                catch (ArgumentOutOfRangeException) { threw = true; }
                Check.True(threw, "zero seams must throw");
            });
        }
    }
}
