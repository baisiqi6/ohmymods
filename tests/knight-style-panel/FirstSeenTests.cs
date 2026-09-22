using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

namespace KnightStylePanelTests
{
    /// <summary>
    /// 功能 A（首见均匀分配，KnightIdentityRuntime.AssignFirstSeenUniform）：
    /// 批量均匀、一次性、收据落档；unresolved/失配/客户端/Load 上下文/失败装载/新招募/在场景格绝不自动分配；
    /// 生产次序（批次先于 PrimeExisting）下不再有哈希铸币。
    /// </summary>
    internal static class FirstSeenTests
    {
        internal static void Run()
        {
            BatchSpreadsStylesAndMintsReceipts();
            BatchWritesNothingWithoutEligibleKnights();
            BatchIsStableAcrossRepeatedPasses();
            BatchRespectsEveryHardGate();
            BatchBeforePrimingNeverHashMints();
            BatchStaysEvenAtScale();
        }

        private static bool Logged(string fragment)
        {
            return Log.Saw(fragment);
        }

        private static void BatchSpreadsStylesAndMintsReceipts()
        {
            Case.Run("first_seen_batch_spreads_styles_evenly_and_mints_receipts", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = Knights.Loaded(21000, 7);
                    int assigned = KnightIdentityRuntime.AssignFirstSeenUniform(Knights.ArrayOf(units), Styles.All, k => false);
                    Check.Equal(7, assigned, "every zero-record knight is assigned");
                    int[] counts = Knights.CountStyles(units, out int missing);
                    Check.Equal(0, missing, "every knight holds a receipt");
                    Check.True(Knights.Spread(counts) <= 1, "batch stays balanced [counts=" + string.Join(",", counts) + "]");
                    Check.True(Knights.Distinct(counts) >= 4, "styles really spread (not one collapsed hash value)");
                    Check.True(Logged("first-seen auto-assigned 7 knights ("), "one-shot batch log | " + Log.Dump());
                    // 既有巡检的 ApplyKnightStyle 段随后跑（stub 同形：按收据写入在场景格）→ HUD 口径待识别归零
                    for (int i = 0; i < units.Count; i++) PatchRoles_KnightStyle.ApplyPanelRestyle(units[i].Knight);
                    Check.Equal(0, PatchRoles_KnightStyle.Unknown(units), "no knight is left showing as unidentified after the pass");
                }
            });
        }

        private static void BatchWritesNothingWithoutEligibleKnights()
        {
            Case.Run("first_seen_batch_writes_nothing_without_eligible_knights", () =>
            {
                using (Fixture f = new Fixture())
                {
                    Check.Equal(0, KnightIdentityRuntime.AssignFirstSeenUniform(Array.Empty<Knight>(), Styles.All, k => false), "empty scan");
                    Check.Equal(0, KnightIdentityRuntime.AssignFirstSeenUniform(null, Styles.All, null), "null scan");
                    List<KnightUnit> one = Knights.Loaded(21100, 1);
                    Check.Equal(0, KnightIdentityRuntime.AssignFirstSeenUniform(Knights.ArrayOf(one), Styles.Of(), k => false), "empty asset pool defers");
                    Check.Equal(0, KnightIdentityRuntime.AssignFirstSeenUniform(Knights.ArrayOf(one), Styles.Of(0, 0), k => false), "duplicate pool entries are rejected");
                    Check.False(Logged("first-seen auto-assigned"), "no batch log without a batch | " + Log.Dump());
                    Check.False(KnightIdentityRuntime.TryGetReceipt(one[0].Knight, out _), "no identity when the gate refuses");
                }
            });
        }

        private static void BatchIsStableAcrossRepeatedPasses()
        {
            Case.Run("first_seen_batch_is_one_shot_and_stable_across_integrity_passes", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = Knights.Loaded(21200, 11);
                    Check.Equal(11, KnightIdentityRuntime.AssignFirstSeenUniform(Knights.ArrayOf(units), Styles.All, k => false), "first pass assigns");
                    Guid[] before = ReceiptIds(units);
                    Check.Equal(0, KnightIdentityRuntime.AssignFirstSeenUniform(Knights.ArrayOf(units), Styles.All, k => false), "second pass assigns nothing");
                    Guid[] after = ReceiptIds(units);
                    Check.SequenceEqual(before, after, "guids frozen across passes");
                }
            });
        }

        private static void BatchRespectsEveryHardGate()
        {
            Case.Run("first_seen_batch_respects_every_hard_gate", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit pending = NativeSim.NewKnight(21300);
                    KnightIdentityRuntime.OnEnable(pending.Knight);

                    // unresolved 上下文（legacy-pending/known-mismatch/conflict）：绝不自动分配
                    KnightIdentityRuntime.ConfirmContext(true);
                    Check.Equal(0, KnightIdentityRuntime.AssignFirstSeenUniform(new[] { pending.Knight }, Styles.All, k => false), "unresolved context never auto-assigns");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(pending.Knight, out _), "no identity on an unresolved context");
                    KnightIdentityRuntime.ConfirmContext(false);

                    // 客户端（无世界权威）：绝不分配
                    NetworkBigBoss.HasWorldAuth = false;
                    Check.Equal(0, KnightIdentityRuntime.AssignFirstSeenUniform(new[] { pending.Knight }, Styles.All, k => false), "client never allocates");
                    NetworkBigBoss.HasWorldAuth = true;

                    // Load 作用域内：绝不分配
                    KnightIdentityRuntime.EnterLoadScope(0x1234);
                    Check.Equal(0, KnightIdentityRuntime.AssignFirstSeenUniform(new[] { pending.Knight }, Styles.All, k => false), "inside a load scope nothing is allocated");
                    KnightIdentityRuntime.ExitLoadScope(0x1234, 0);

                    // 失败装载（FailedLoad）：绝不自动分配
                    KnightUnit failed = NativeSim.NewKnight(21301);
                    IslandSaveData failedIsland = new IslandSaveData { land = 4 };
                    KnightIdentityLoadBridge.LoadScope failedScope = KnightIdentityLoadBridge.Begin(failedIsland);
                    KnightIdentityRuntime.OnEnable(failed.Knight);
                    KnightIdentityLoadBridge.End(new Exception("test-load-failure"), failedScope);
                    Check.Equal(0, KnightIdentityRuntime.AssignFirstSeenUniform(new[] { failed.Knight }, Styles.All, k => false), "failed-load owners are never auto-assigned");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(failed.Knight, out _), "failed-load owner stays unassigned");

                    // MarkedNew（新招募）：批次不碰，仍走既有 ChooseBalanced
                    KnightUnit recruit = NativeSim.NewKnight(21302);
                    KnightIdentityRuntime.OnEnable(recruit.Knight);
                    KnightIdentityRuntime.MarkPromoted(recruit.Knight);
                    Check.Equal(0, KnightIdentityRuntime.AssignFirstSeenUniform(new[] { recruit.Knight }, Styles.All, k => false), "marked recruits are skipped");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(recruit.Knight, out _), "skipped recruit stays unassigned until TryResolve");
                    Check.True(KnightIdentityRuntime.TryResolve(recruit.Knight, -1, 0u, Styles.All, out int recruitStyle), "recruit path still mints through TryResolve");
                    Check.True(KnightIdentityReceipt.IsValidStyle(recruitStyle), "valid recruit style");

                    // 已有在场景格（本会话已生效）：留给冻结路径，不重摇
                    KnightUnit styled = NativeSim.NewKnight(21303);
                    KnightIdentityRuntime.OnEnable(styled.Knight);
                    Check.Equal(0, KnightIdentityRuntime.AssignFirstSeenUniform(new[] { styled.Knight }, Styles.All, k => k == styled.Knight), "live-styled knights are left to the freeze path");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(styled.Knight, out _), "no overwrite of a live style");
                    Check.Equal(0, KnightIdentityRuntime.AssignFirstSeenUniform(new[] { recruit.Knight, styled.Knight }, Styles.All, k => k == styled.Knight), "no eligible knights at all");

                    // 非 tagKnight（侍从）与不在场的骑士：绝不分配
                    KnightUnit squire = NativeSim.NewKnight(21304, "Squire", "Squire(Clone)");
                    KnightIdentityRuntime.OnEnable(squire.Knight);
                    KnightUnit detached = NativeSim.NewDetachedKnight(21305);
                    KnightIdentityRuntime.OnEnable(detached.Knight);
                    Check.Equal(0, KnightIdentityRuntime.AssignFirstSeenUniform(new[] { squire.Knight, detached.Knight }, Styles.All, k => false), "squire/detached are not assignable");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(squire.Knight, out _), "squire never gets an identity");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(detached.Knight, out _), "out-of-world knight never gets an identity");
                }
            });
        }

        private static void BatchBeforePrimingNeverHashMints()
        {
            Case.Run("integrity_pass_order_batches_before_priming_without_hash_minting", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = Knights.Loaded(21400, 6);
                    KnightUnit recruit = NativeSim.NewKnight(21450);
                    KnightIdentityRuntime.OnEnable(recruit.Knight);
                    KnightIdentityRuntime.MarkPromoted(recruit.Knight);
                    List<KnightUnit> all = new List<KnightUnit>(units) { recruit };

                    Check.Equal(6, KnightIdentityRuntime.AssignFirstSeenUniform(Knights.ArrayOf(all), Styles.All, k => false),
                        "the batch covers the six loaded knights (marked recruit excluded)");

                    // PrimeExisting 排在批次之后（生产的 IntegrityPass 次序）：哈希式回调绝无机会铸币
                    int callbacks = 0;
                    KnightIdentityRuntime.PrimeExisting(Knights.ArrayOf(all), k => { callbacks++; return 1; });
                    Check.Equal(0, callbacks, "hash-style callback is never consulted");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(recruit.Knight, out _), "marked recruit untouched by batch and priming");

                    int[] counts = Knights.CountStyles(units, out int missing);
                    Check.Equal(0, missing, "all batched knights receipted");
                    Check.True(Knights.Spread(counts) <= 1, "batch distribution balanced [counts=" + string.Join(",", counts) + "]");
                    Check.True(Knights.Distinct(counts) >= 4, "a constant hash style could not have decided this batch");
                }
            });
        }

        private static void BatchStaysEvenAtScale()
        {
            Case.Run("first_seen_batch_stays_even_at_scale", () =>
            {
                int[] sizes = { 1, 4, 5, 512 };
                for (int s = 0; s < sizes.Length; s++)
                {
                    using (Fixture f = new Fixture())
                    {
                        int size = sizes[s];
                        List<KnightUnit> units = Knights.Loaded(22000 + s * 1000, size);
                        Check.Equal(size, KnightIdentityRuntime.AssignFirstSeenUniform(Knights.ArrayOf(units), Styles.All, k => false),
                            "size " + size + ": all assigned");
                        int[] counts = Knights.CountStyles(units, out int missing);
                        Check.Equal(0, missing, "size " + size + ": every knight receipted");
                        Check.True(Knights.Spread(counts) <= 1, "size " + size + ": stays even [counts=" + string.Join(",", counts) + "]");
                        Check.Equal(size, KnightIdentityRuntime.ReceiptCount, "size " + size + ": receipt count matches");
                    }
                }
            });
        }

        private static Guid[] ReceiptIds(IList<KnightUnit> units)
        {
            Guid[] ids = new Guid[units.Count];
            for (int i = 0; i < units.Count; i++)
            {
                Check.True(KnightIdentityRuntime.TryGetReceipt(units[i].Knight, out KnightIdentityReceipt receipt), "receipt " + i);
                ids[i] = receipt.Id;
            }
            return ids;
        }
    }
}
