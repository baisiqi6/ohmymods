using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

namespace KnightStylePanelTests
{
    /// <summary>
    /// 功能 B（面板）：步进器守恒与剩余池、应用时剩余归希腊、重派保留式/全重洗、GUID 保留、
    /// 全或无、联机/客机/资源池门槛、绘制冒烟。
    /// </summary>
    internal static class PanelTests
    {
        internal static void Run()
        {
            StepperConservationAndGreeceFolding();
            ReassignRespectsQuotasAndRerollsAll();
            PanelRefreshCountsAndGates();
            PanelApplyFoldsPoolIntoGreeceAndStylesEveryone();
            PanelAssignmentIsAllOrNothing();
            PanelDrawSectionSmoke();
        }

        private static bool Logged(string fragment)
        {
            return Log.Saw(fragment);
        }

        private static void StepperConservationAndGreeceFolding()
        {
            Case.Run("panel_steppers_conserve_the_total_and_apply_folds_the_pool_into_greece", () =>
            {
                KnightStylePanelCore core = new KnightStylePanelCore();
                core.Reset(10, new[] { 2, 1, 1, 0, 0 });
                Check.Equal(10, core.Knights, "knights total");
                Check.Equal(6, core.Pool, "pool = knights - rows");
                Check.True(core.CanAdd(0), "add allowed while the pool is filled");
                Check.False(core.CanRemove(3), "empty row cannot be reduced");
                Check.True(core.CanRemove(0), "filled row can be reduced");
                Check.True(core.Add(4), "take one from the pool");
                Check.Equal(5, core.Pool, "pool shrinks by one");
                Check.Equal(1, core.Target(4), "row 4 grew by one");
                Check.True(core.Remove(4), "give one back");
                Check.Equal(6, core.Pool, "pool grows by one");
                Check.Equal(0, core.Target(4), "row 4 back to zero");
                Check.False(core.CanRemove(4), "empty row disabled again");

                while (core.CanAdd(2)) core.Add(2);
                Check.Equal(0, core.Pool, "pool exhausted");
                Check.False(core.CanAdd(2), "no + when the pool is empty");
                Check.Equal(10, Total(core), "total conserved while taking");

                core.Reset(10, new[] { 2, 2, 2, 2, 2 });
                Check.Equal(0, core.Pool, "balanced start");
                Check.True(core.Remove(0), "release one from row 0");
                Check.True(core.Remove(0), "release another from row 0");
                Check.False(core.CanRemove(0), "row 0 cannot go below zero");
                Check.Equal(2, core.Pool, "two released");
                core.SpreadEvenly();
                Check.Equal(0, core.Pool, "spread distributes everything");
                Check.True(MaxMin(core) <= 1, "spread levels the rows within one");
                Check.Equal(10, Total(core), "total conserved while spreading");

                core.Reset(7, new[] { 1, 1, 0, 0, 0 });
                Check.Equal(5, core.Pool, "fresh pool");
                int[] targets = core.ApplyTargets(out int folded);
                Check.Equal(5, folded, "all pool folds into greece");
                Check.SequenceEqual(new[] { 1, 1, 0, 5, 0 }, targets, "folded targets");
                Check.Equal(7, Sum(targets), "targets sum equals the knight count");
                Check.Equal(5, core.Pool, "folding does not mutate the pending state");
                core.SetTargets(targets);
                Check.Equal(0, core.Pool, "applied state shows an empty pool");
                Check.SequenceEqual(new[] { 1, 1, 0, 5, 0 }, core.CopyTargets(), "applied targets visible");

                // 非法输入：重派必须拒绝（面板会捕获并提示，不产生半应用）
                bool threw = false;
                try { KnightStylePanelCore.Reassign(new[] { 0, 0 }, new[] { 1, 1, 1, 1, 1 }, false, Knights.Entropy(1u)); }
                catch (ArgumentException) { threw = true; }
                Check.True(threw, "targets that do not sum to the roster are rejected");
            });
        }

        private static void ReassignRespectsQuotasAndRerollsAll()
        {
            Case.Run("reassign_keeps_confirmed_within_quota_and_rerolls_everything_when_asked", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = Knights.Loaded(23000, 4);
                    Check.True(KnightIdentityRuntime.TryResolve(units[0].Knight, 0, 0u, Styles.All, out _), "knight 0 medieval");
                    Check.True(KnightIdentityRuntime.TryResolve(units[1].Knight, 0, 0u, Styles.All, out _), "knight 1 medieval");
                    Check.True(KnightIdentityRuntime.TryResolve(units[2].Knight, 1, 0u, Styles.All, out _), "knight 2 deadlands");
                    Check.True(KnightIdentityRuntime.TryResolve(units[3].Knight, 2, 0u, Styles.All, out _), "knight 3 shogun");
                    Guid[] before = ReceiptIds(units);

                    int[] current = { 0, 0, 1, 2 };
                    int[] targets = { 0, 1, 1, 1, 1 }; // 中世纪配额 0：该行已被清空，两名旧中世纪必须改派
                    int[] styles = KnightStylePanelCore.Reassign(current, targets, false, Knights.Entropy(7u));
                    Check.SequenceEqual(targets, Knights.CountStyles(styles), "assignments match the targets");
                    Check.Equal(1, styles[2], "confirmed deadlands knight keeps its style (quota allows)");
                    Check.Equal(2, styles[3], "confirmed shogun knight keeps its style (quota allows)");
                    Check.NotEqual(0, styles[0], "excess medieval knight is reassigned");
                    Check.NotEqual(0, styles[1], "second excess medieval knight is reassigned");

                    Check.True(KnightIdentityRuntime.TryPanelAssignAll(Knights.ArrayOf(units), styles, out int assigned), "panel assignment applied");
                    Check.Equal(4, assigned, "all four assigned");
                    for (int i = 0; i < units.Count; i++)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(units[i].Knight, out KnightIdentityReceipt receipt), "receipt " + i);
                        Check.Equal(styles[i], receipt.Style, "style applied " + i);
                        Check.Same(before[i], receipt.Id, "guid preserved through reassignment " + i);
                    }

                    // 全部重洗：忽略现有分配，所有骑士参与随机；GUID 依旧保留
                    int[] current2 = { 3, 3, 1, 4 };
                    int[] targets2 = { 1, 0, 1, 1, 1 };
                    int[] rerolled = KnightStylePanelCore.Reassign(current2, targets2, true, Knights.Entropy(11u));
                    Check.SequenceEqual(targets2, Knights.CountStyles(rerolled), "reroll matches the targets");
                    Check.True(KnightIdentityRuntime.TryPanelAssignAll(Knights.ArrayOf(units), rerolled, out _), "reroll applied");
                    for (int i = 0; i < units.Count; i++)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(units[i].Knight, out KnightIdentityReceipt receipt), "receipt after reroll " + i);
                        Check.Same(before[i], receipt.Id, "guid preserved through a full reroll " + i);
                        Check.Equal(rerolled[i], receipt.Style, "reroll style " + i);
                    }

                    // 零记录骑士：重派铸新 GUID（历史不断链只适用于已有身份）
                    KnightUnit fresh = NativeSim.NewKnight(23010);
                    KnightIdentityRuntime.OnEnable(fresh.Knight);
                    Check.True(KnightIdentityRuntime.TryPanelAssignAll(new[] { fresh.Knight }, new[] { 4 }, out _), "zero-record knight assigned");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(fresh.Knight, out KnightIdentityReceipt freshReceipt), "fresh receipt");
                    Check.Equal(4, freshReceipt.Style, "fresh style");
                }
            });
        }

        private static void PanelRefreshCountsAndGates()
        {
            Case.Run("panel_refresh_counts_and_online_client_pool_gates", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = Knights.Loaded(23100, 3);
                    Check.True(KnightIdentityRuntime.TryResolve(units[0].Knight, 2, 0u, Styles.All, out _), "one confirmed shogun");
                    UnitScanCache.TestKnights = Knights.ArrayOf(units);
                    PopulationCounts.Knights = 5; // HUD 口径含 2 名侍从：仅作脚注对照

                    KnightStylePanel.ResetForTests();
                    KnightStylePanel.Refresh();
                    Check.True(KnightStylePanel.Refreshed, "offline refresh succeeds");
                    Check.Equal(3, KnightStylePanel.ScanKnights, "assignable knights counted");
                    Check.Equal(3, KnightStylePanel.Session.Knights, "total = assignable knights");
                    Check.Equal(2, KnightStylePanel.Session.Pool, "pool = unconfirmed knights");
                    Check.Equal(1, KnightStylePanel.Session.Target(2), "confirmed style pre-filled");
                    Check.Equal(1, KnightStylePanel.Session.Confirmed(2), "confirmed counted by receipt");
                    Check.Equal(5, KnightStylePanel.HudKnights, "HUD count exposed for the parity footnote");
                    Check.Equal<string>(null, KnightStylePanel.Blocked, "no gate blocks a ready island");

                    NetworkBigBoss.IsOnline = true;
                    KnightStylePanel.Refresh();
                    Check.False(KnightStylePanel.Refreshed, "online disables the section");
                    Check.True(KnightStylePanel.Blocked != null && KnightStylePanel.Blocked.IndexOf("联机", StringComparison.Ordinal) >= 0,
                        "online reason shown | " + KnightStylePanel.Blocked);
                    NetworkBigBoss.IsOnline = false;

                    NetworkBigBoss.HasWorldAuth = false;
                    KnightStylePanel.Refresh();
                    Check.False(KnightStylePanel.Refreshed, "client disabled");
                    Check.True(KnightStylePanel.Blocked != null && KnightStylePanel.Blocked.IndexOf("客机", StringComparison.Ordinal) >= 0,
                        "client reason shown | " + KnightStylePanel.Blocked);
                    NetworkBigBoss.HasWorldAuth = true;

                    PatchRoles_KnightStyle.PoolReady = false;
                    KnightStylePanel.Refresh();
                    Check.False(KnightStylePanel.Refreshed, "missing asset pool blocks apply");
                    PatchRoles_KnightStyle.PoolReady = true;

                    KnightUnit detached = NativeSim.NewDetachedKnight(23150);
                    KnightIdentityRuntime.OnEnable(detached.Knight);
                    UnitScanCache.TestKnights = new[] { units[0].Knight, units[1].Knight, units[2].Knight, detached.Knight };
                    KnightStylePanel.Refresh();
                    Check.False(KnightStylePanel.Refreshed, "unverified knight blocks apply");
                    Check.Equal(1, KnightStylePanel.Unverified, "unverified knight counted");
                }
            });
        }

        private static void PanelApplyFoldsPoolIntoGreeceAndStylesEveryone()
        {
            Case.Run("panel_apply_folds_the_pool_into_greece_and_styles_every_knight", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = Knights.Loaded(23200, 4);
                    Check.True(KnightIdentityRuntime.TryResolve(units[0].Knight, 0, 0u, Styles.All, out _), "knight 0 medieval");
                    Check.True(KnightIdentityRuntime.TryResolve(units[1].Knight, 0, 0u, Styles.All, out _), "knight 1 medieval");
                    Check.True(KnightIdentityRuntime.TryResolve(units[2].Knight, 1, 0u, Styles.All, out _), "knight 2 deadlands");
                    Guid[] before = ReceiptIds(new List<KnightUnit> { units[0], units[1], units[2] });

                    UnitScanCache.TestKnights = Knights.ArrayOf(units);
                    KnightStylePanel.ResetForTests();
                    KnightStylePanel.Refresh();
                    Check.True(KnightStylePanel.Refreshed, "refresh ready");
                    Check.Equal(1, KnightStylePanel.Session.Pool, "one zero-record knight in the pool");
                    Check.True(KnightStylePanel.Session.Remove(0), "drop row 0");
                    Check.True(KnightStylePanel.Session.Remove(0), "drop row 0 again");
                    Check.Equal(3, KnightStylePanel.Session.Pool, "pool now three");

                    KnightStylePanel.Apply();
                    Check.True(KnightStylePanel.Status != null && KnightStylePanel.Status.IndexOf("已应用 4", StringComparison.Ordinal) >= 0,
                        "status reports the applied roster | " + KnightStylePanel.Status);
                    Check.Equal(0, KnightStylePanel.Session.Target(0), "row 0 emptied");
                    Check.Equal(1, KnightStylePanel.Session.Target(1), "deadlands kept");
                    Check.Equal(3, KnightStylePanel.Session.Target(3), "greece absorbed the pool");
                    Check.Equal(0, KnightStylePanel.Session.Pool, "pool folded and applied");

                    int[] counts = Knights.CountStyles(units, out int missing);
                    Check.Equal(0, missing, "every knight has a receipt after apply");
                    Check.SequenceEqual(new[] { 0, 1, 0, 3, 0 }, counts, "applied counts match the folded targets");
                    Check.Same(before[0], ReceiptId(units[0]), "reassigned medieval guid preserved");
                    Check.Same(before[1], ReceiptId(units[1]), "second reassigned medieval guid preserved");
                    Check.Same(before[2], ReceiptId(units[2]), "confirmed deadlands guid preserved");

                    Check.Equal(4, PatchRoles_KnightStyle.Restyled.Count, "restyle path used for every knight");
                    Check.Equal(0, PatchRoles_KnightStyle.Unknown(units), "no knight is left without a resolved style");
                    Check.True(Logged("apply: knights=4 reroll=off before=2/1/0/0/0 after=0/1/0/3/0 pool->greece=3 rebaseline=none"),
                        "apply log | " + Log.Dump());
                    Check.False(KnightIdentityRuntime.PanelRebaselineArmed, "a resolved context needs no rebaseline");
                }
            });
        }

        private static void PanelAssignmentIsAllOrNothing()
        {
            Case.Run("panel_assignment_is_all_or_nothing", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit confirmed = NativeSim.NewKnight(23300);
                    KnightIdentityRuntime.OnEnable(confirmed.Knight);
                    Check.True(KnightIdentityRuntime.TryResolve(confirmed.Knight, 0, 0u, Styles.All, out _), "confirmed medieval");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(confirmed.Knight, out KnightIdentityReceipt before), "receipt");

                    KnightUnit detached = NativeSim.NewDetachedKnight(23301);
                    KnightIdentityRuntime.OnEnable(detached.Knight);

                    Check.False(KnightIdentityRuntime.TryPanelAssignAll(new[] { confirmed.Knight, detached.Knight }, new[] { 1, 2 }, out int assigned),
                        "out-of-world knight rejects the batch");
                    Check.Equal(0, assigned, "nothing assigned");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(confirmed.Knight, out KnightIdentityReceipt after), "receipt still there");
                    Check.Same(before.Id, after.Id, "guid untouched");
                    Check.Equal(0, after.Style, "style untouched (no half-apply)");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(detached.Knight, out _), "no identity for the rejected batch");

                    Check.False(KnightIdentityRuntime.TryPanelAssignAll(new[] { confirmed.Knight, confirmed.Knight }, new[] { 1, 2 }, out _),
                        "duplicate object rejects the batch");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(confirmed.Knight, out after), "receipt after duplicate rejection");
                    Check.Equal(0, after.Style, "duplicate rejection left the receipt alone");

                    NetworkBigBoss.HasWorldAuth = false;
                    Check.False(KnightIdentityRuntime.TryPanelAssignAll(new[] { confirmed.Knight }, new[] { 1 }, out _), "client cannot reassign");
                    NetworkBigBoss.HasWorldAuth = true;

                    Check.True(KnightIdentityRuntime.TryPanelAssignAll(new[] { confirmed.Knight }, new[] { 4 }, out _), "valid batch applies");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(confirmed.Knight, out after), "receipt after apply");
                    Check.Equal(4, after.Style, "style changed on the explicit user action");
                    Check.Same(before.Id, after.Id, "guid preserved on the explicit user action");
                }
            });
        }

        private static void PanelDrawSectionSmoke()
        {
            Case.Run("panel_draw_section_smoke_keeps_the_layout_stable", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit unit = NativeSim.NewKnight(23400);
                    KnightIdentityRuntime.OnEnable(unit.Knight);
                    UnitScanCache.TestKnights = new[] { unit.Knight };
                    KnightStylePanel.ResetForTests();
                    KnightStylePanel.Refresh();
                    UnityEngine.GUIStyle style = new UnityEngine.GUIStyle();
                    float y = 0f;
                    KnightStylePanel.DrawSection(ref y, 900f, style, style, style, style, style, style);
                    Check.True(y > 300f, "section advanced the cursor");

                    KnightStylePanel.SetPreview(0, new UnityEngine.Texture2D(24, 24));
                    float y2 = 0f;
                    KnightStylePanel.DrawSection(ref y2, 900f, style, style, style, style, style, style);
                    Check.Equal(y, y2, "preview swap does not change the layout");
                }
            });
        }

        private static int Total(KnightStylePanelCore core)
        {
            int total = 0;
            for (int i = 0; i < KnightStylePanelCore.StyleCount; i++) total += core.Target(i);
            return total;
        }

        private static int MaxMin(KnightStylePanelCore core)
        {
            int min = int.MaxValue, max = 0;
            for (int i = 0; i < KnightStylePanelCore.StyleCount; i++)
            {
                int value = core.Target(i);
                if (value < min) min = value;
                if (value > max) max = value;
            }
            return max - min;
        }

        private static int Sum(int[] values)
        {
            int total = 0;
            for (int i = 0; i < values.Length; i++) total += values[i];
            return total;
        }

        private static Guid[] ReceiptIds(IList<KnightUnit> units)
        {
            Guid[] ids = new Guid[units.Count];
            for (int i = 0; i < units.Count; i++) ids[i] = ReceiptId(units[i]);
            return ids;
        }

        private static Guid ReceiptId(KnightUnit unit)
        {
            Check.True(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt receipt), "receipt for " + unit.Go.name);
            return receipt.Id;
        }
    }
}
