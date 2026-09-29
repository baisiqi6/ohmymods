using System;
using System.Collections.Generic;
using System.IO;
using KingdomEnhancedMod;

namespace KnightIdentityRuntimeTests
{
    /// <summary>
    /// 历史职业配额恢复（knight-load-roundtrip-20260929）：纯分配策略、历史来源选择（revision/SavedAtUtc/
    /// 空最新/冲突）、known-mismatch 生产链（冻结 cohort → 整批重分配 → 首次 Save 写新 revision → 下一会话
    /// exact 恢复）、用户动作优先与部分收据取消。
    /// </summary>
    internal static class QuotaRecoveryTests
    {
        internal static void Run()
        {
            AllocationFollowsLargestRemainderAndStyleTies();
            ExactCountIsPreserved();
            AllocationMatchesIndependentReferenceForAllCohortSizes();
            SelectionUsesOnlyTheActiveBranch();
            KnownMismatchRecoversQuotaThenSaveWritesNewRevisionAndReloadsExact();
            PartialReceiptsAndPanelPendingCancelTheWholeCohort();
            QuotaBeforeFirstSaveIsDeterministicAndMintsFreshGuids();
            EmptyLatestHandsOverToTheUniformRuleWithinTheAvailablePool();
            FailedQuotaSaveRetryKeepsGuidsAndConsumesRevisionOnlyOnSuccess();
            QuotaNeverTakesACohortWhileASaveIsInProgress();
            ManualApplySupersedesTheQuotaPendingAcrossTwoSaves();
            TerminatedMembersAreDroppedAndTheRemainingCohortRecovers();
            WorldReplacementCancelsTheCohortWithoutMinting();
            IncompleteOrFailedLoadsCancelTheCohortWithAReason();
            ExactRollbackRebranchesActiveAndLaterMismatchUsesThatBranch();
            SameSceneWorldReplacementCancelsAndLandChangeCancels();
            CurrentContextUnreadableKeepsTheCohortWaiting();
            RealCohortSizesDriveTheRecovery();
            NativeOrderLoadQuotaSaveClearWorldReloadKeepsTheGuids();
        }

        // ------------------------------------------------------------------ 纯策略

        private static void AllocationFollowsLargestRemainderAndStyleTies()
        {
            Case.Run("quota_allocation_uses_largest_remainder_with_style_id_ties", () =>
            {
                int[] equal = KnightIdentityQuotaRecovery.Allocate(3, new List<int> { 1, 1, 1, 1, 1 });
                Check.Equal("1/1/1/0/0", Line(equal), "3 of 5 equal shares: remainder ties go to the lowest style id");
                Check.Equal(3, Total(equal), "allocation never emits more or fewer seats than the live cohort");

                int[] shrunk = KnightIdentityQuotaRecovery.Allocate(20, new List<int> { 4, 5, 4, 5, 4 });
                Check.Equal("4/4/4/4/4", Line(shrunk), "22 -> 20 keeps the total with floored shares");

                int[] grown = KnightIdentityQuotaRecovery.Allocate(25, new List<int> { 4, 5, 4, 5, 4 });
                Check.Equal(25, Total(grown), "25 seats filled exactly");
                Check.True(grown[1] >= grown[0] && grown[3] >= grown[2], "larger historical shares keep larger quotas");

                Check.True(KnightIdentityQuotaRecovery.Allocate(5, new List<int> { 0, 0, 0, 0, 0 }) == null, "empty history has no quota (no divide by zero)");
                Check.True(KnightIdentityQuotaRecovery.Allocate(0, new List<int> { 4, 5, 4, 5, 4 }) == null, "empty cohort has no quota");

                int[] plan = KnightIdentityQuotaRecovery.BuildPlan(new List<int> { 2, 0, 3, 0, 0 }, 5);
                Check.Equal("0,0,2,2,2", string.Join(",", plan), "plan fills styles in ascending id order");
            });
        }

        private static void ExactCountIsPreserved()
        {
            Case.Run("quota_exact_count_is_preserved_when_the_cohort_is_unchanged", () =>
            {
                int[] exact = KnightIdentityQuotaRecovery.Allocate(22, new List<int> { 4, 5, 4, 5, 4 });
                Check.Equal("4/5/4/5/4", Line(exact), "22 == 22 keeps every historical style count");
            });
        }

        // ------------------------------------------------------------------ 历史来源选择

        private static void SelectionUsesOnlyTheActiveBranch()
        {
            Case.Run("quota_source_is_only_the_active_branch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    string contextKey = Sidecar.ContextKey(4);

                    // Active = 新代 rev0 空；旧代 rev8 非空 → 有效零计数来源（fresh 均匀），绝不选旧 rev8
                    KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
                    string epochOld = KnightIdentityArchive.NewScope();
                    AddSnapshot(archive, contextKey, epochOld, 8, "2026-01-01T00:00:00.0000000+00:00", new Dictionary<string, KnightIdentityReceipt>
                    {
                        { "old-a", new KnightIdentityReceipt(Guid.NewGuid(), 1) },
                        { "old-b", new KnightIdentityReceipt(Guid.NewGuid(), 4) },
                    });
                    string epochActive = KnightIdentityArchive.NewScope();
                    AddSnapshot(archive, contextKey, epochActive, 0, "2027-01-01T00:00:00.0000000+00:00", new Dictionary<string, KnightIdentityReceipt>());
                    Check.True(KnightIdentityQuotaRecovery.TrySelectSource(archive, contextKey, out string epoch, out int[] counts, out string reason),
                        "the active branch is the only source: " + reason);
                    Check.Equal(epochActive, epoch, "selected the Active epoch (not the old rev8 epoch)");
                    Check.Equal("0/0/0/0/0", Line(counts), "the active empty snapshot yields zero counts");
                    Check.Equal("empty-latest", reason, "valid empty latest is the uniform-rule handover");

                    // Active = 新代 rev0 非空 → 只按新代计数（旧 rev8 无论如何更新都不参与）
                    KnightIdentityArchive newBranch = KnightIdentityArchive.CreateEmpty();
                    AddSnapshot(newBranch, contextKey, KnightIdentityArchive.NewScope(), 8, "2026-01-01T00:00:00.0000000+00:00", new Dictionary<string, KnightIdentityReceipt>
                    {
                        { "old-a", new KnightIdentityReceipt(Guid.NewGuid(), 1) },
                        { "old-b", new KnightIdentityReceipt(Guid.NewGuid(), 1) },
                        { "old-c", new KnightIdentityReceipt(Guid.NewGuid(), 1) },
                    });
                    string activeNonEmpty = KnightIdentityArchive.NewScope();
                    AddSnapshot(newBranch, contextKey, activeNonEmpty, 0, "2027-02-01T00:00:00.0000000+00:00", new Dictionary<string, KnightIdentityReceipt>
                    {
                        { "new-a", new KnightIdentityReceipt(Guid.NewGuid(), 2) },
                    });
                    Check.True(KnightIdentityQuotaRecovery.TrySelectSource(newBranch, contextKey, out epoch, out counts, out reason), "new branch quota: " + reason);
                    Check.Equal(activeNonEmpty, epoch, "only the active branch is used");
                    Check.Equal("0/0/1/0/0", Line(counts), "counts come from the active branch only");

                    // 有效 legacy(v1)-only Active：与 kind2 一样是有效来源，按该 Active 条目给配额（绝不旧 epoch）
                    KnightIdentityArchive legacyActive = KnightIdentityArchive.CreateEmpty();
                    AddSnapshot(legacyActive, contextKey, KnightIdentityArchive.NewScope(), 7, "2026-03-01T00:00:00.0000000+00:00", new Dictionary<string, KnightIdentityReceipt>
                    {
                        { "old-a", new KnightIdentityReceipt(Guid.NewGuid(), 1) },
                        { "old-b", new KnightIdentityReceipt(Guid.NewGuid(), 1) },
                    });
                    string legacyEpoch = KnightIdentityArchive.NewScope();
                    List<KnightIdentitySnapshotEntry> legacyEntries = new List<KnightIdentitySnapshotEntry>
                    {
                        new KnightIdentitySnapshotEntry("legacy-a", new KnightIdentityReceipt(Guid.NewGuid(), 4)),
                        new KnightIdentitySnapshotEntry("legacy-b", new KnightIdentityReceipt(Guid.NewGuid(), 4)),
                        new KnightIdentitySnapshotEntry("legacy-c", new KnightIdentityReceipt(Guid.NewGuid(), 3)),
                    };
                    Check.True(KnightIdentitySnapshot.TryCreateCore(KnightIdentityFingerprint.KindLegacy, KnightIdentityFingerprint.Sha256("{\"legacy\":1}", legacyEpoch),
                        "2027-03-01T00:00:00.0000000+00:00", legacyEntries, 0, out KnightIdentitySnapshot legacySnapshot, out string error),
                        "legacy snapshot built: " + error);
                    Check.Equal(KnightIdentityArchive.MutationStatus.Applied, legacyActive.RecordSnapshot(legacyEpoch, legacySnapshot), "legacy snapshot recorded");
                    Check.True(legacyActive.EnsureContext(contextKey, legacyEpoch, true), "legacy epoch becomes Active");
                    Check.True(KnightIdentityQuotaRecovery.TrySelectSource(legacyActive, contextKey, out string legacySource, out int[] legacyCounts, out reason),
                        "a supported legacy-only Active is a valid source: " + reason);
                    Check.Equal(legacyEpoch, legacySource, "the legacy Active epoch is used, not the old rev7 epoch");
                    Check.Equal("0/0/0/1/2", Line(legacyCounts), "counts come from the legacy Active entries");

                    // 真正无 source：Active 指向不在 archive 的 scope（invalid Active）→ 拒绝且不 fresh、不回扫旧 epoch
                    KnightIdentityArchive invalidActive = KnightIdentityArchive.CreateEmpty();
                    AddSnapshot(invalidActive, contextKey, KnightIdentityArchive.NewScope(), 3, "2026-03-01T00:00:00.0000000+00:00", new Dictionary<string, KnightIdentityReceipt>
                    {
                        { "old-a", new KnightIdentityReceipt(Guid.NewGuid(), 1) },
                    });
                    Check.True(invalidActive.TryGetContext(contextKey, out KnightIdentityContext invalidContext), "context present");
                    invalidContext.Active = new string('a', KnightIdentityFingerprint.HexLength); // 不存在于 archive 的 opaque scope
                    Check.False(KnightIdentityQuotaRecovery.TrySelectSource(invalidActive, contextKey, out _, out _, out reason), "an Active missing from the archive yields no source");
                    Check.Equal("no-active-source", reason, "no-active-source reported (never a silent fresh fallback)");

                    // 同 revision + 同时间戳但内容不一致（同一 Active epoch 内）：冲突
                    KnightIdentityArchive conflicting = KnightIdentityArchive.CreateEmpty();
                    string conflictEpoch = KnightIdentityArchive.NewScope();
                    AddSnapshot(conflicting, contextKey, conflictEpoch, 2, "2026-05-05T00:00:00.0000000+00:00", new Dictionary<string, KnightIdentityReceipt>
                    {
                        { "id-a", new KnightIdentityReceipt(Guid.NewGuid(), 1) },
                    });
                    AddSnapshot(conflicting, contextKey, conflictEpoch, 2, "2026-05-05T00:00:00.0000000+00:00", new Dictionary<string, KnightIdentityReceipt>
                    {
                        { "id-a", new KnightIdentityReceipt(Guid.NewGuid(), 4) },
                    });
                    Check.True(conflicting.EnsureContext(contextKey, conflictEpoch, true), "the conflict fixture epoch is Active");
                    Check.False(KnightIdentityQuotaRecovery.TrySelectSource(conflicting, contextKey, out _, out _, out reason), "same-priority disagreement is refused");
                    Check.Equal("conflict", reason, "conflict reason reported");

                    // 没有该 context：不做配额（交既有新骑士策略）
                    Check.False(KnightIdentityQuotaRecovery.TrySelectSource(conflicting, Sidecar.ContextKey(7), out _, out _, out reason), "unknown context has no source");
                    Check.Equal("no-context", reason, "unknown context reason reported");
                }
            });
        }

        // ------------------------------------------------------------------ 生产链

        private static void KnownMismatchRecoversQuotaThenSaveWritesNewRevisionAndReloadsExact()
        {
            Case.Run("known_mismatch_recovers_quota_then_save_writes_new_revision_and_reloads_exact", () =>
            {
                using (Fixture f = new Fixture())
                {
                    int[] history = { 4, 5, 4, 5, 4 };
                    List<KnightUnit> units = BuildKnights(30000, history);
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, units);
                    string contextKey = Sidecar.ContextKey(4);

                    // 下一会话：内容与全部 native ID 都变（原版重存/重载后 instanceID 全换）→ known-mismatch
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData changed = NativeSim.CloneIsland(island);
                    changed.biome++;
                    for (int i = 0; i < changed.objects.Count; i++) changed.objects[i].uniqueID = "churn-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    List<KnightUnit> reloaded = new List<KnightUnit>(history.Length);
                    CampaignSaveData.current = new CampaignSaveData { CurrentIsland = changed };
                    NativeSim.RunLoad(changed, (index, id) =>
                    {
                        KnightUnit unit = NativeSim.NewKnight(40000 + index);
                        reloaded.Add(unit);
                        return unit;
                    });

                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch",
                        "native id churn lands in known-mismatch");
                    Check.Equal(1, KnightIdentityLoadSeed.QuotaBatchCount, "the quota cohort was frozen from the load records");
                    Check.False(KnightIdentityRuntime.CanFlushSeed, "write protection holds before the quota pass");
                    foreach (KnightUnit unit in reloaded) Check.False(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "no receipt before quota");

                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.Equal(0, KnightIdentityLoadSeed.QuotaBatchCount, "the cohort was consumed exactly once");
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out kind) && kind == "historical-quota",
                        "binding switches to the selected historical epoch as historical-quota");
                    Check.True(KnightIdentityRuntime.CanFlushSeed, "quota recovery releases the write protection");

                    int[] recovered = StyleCounts(reloaded);
                    Check.Equal(22, Total(recovered), "every loaded knight got exactly one receipt");
                    Check.Equal("4/5/4/5/4", Line(recovered), "22 == 22 preserves the historical style counts exactly");
                    Check.True(Logged("historical-quota: knights=22"), "bounded quota receipt logged");
                    Check.True(Logged("old=4/5/4/5/4"), "log carries the historical counts");

                    // 重复巡检不得重铸：GUID 与 style 全部保持
                    List<Guid> minted = new List<Guid>();
                    foreach (KnightUnit unit in reloaded)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt receipt), "receipt present");
                        minted.Add(receipt.Id);
                    }
                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    int[] again = StyleCounts(reloaded);
                    Check.Equal("4/5/4/5/4", Line(again), "repeat integration pass never re-allocates");
                    for (int i = 0; i < reloaded.Count; i++)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(reloaded[i].Knight, out KnightIdentityReceipt receipt), "receipt still present");
                        Check.Same(minted[i], receipt.Id, "repeat pass never re-mints");
                    }

                    // 首次真实 Save：写所选历史 epoch 的新 revision（> MaxRevision），不新造 epoch
                    NativeSim.RunSave(changed, 0, 4, 0, reloaded);
                    KnightIdentityArchive archive = Sidecar.Load(f.SidecarPath);
                    Check.True(archive.TryGetContext(contextKey, out KnightIdentityContext context), "context registered");
                    Check.Equal(1, context.Epochs.Count, "no new epoch was created");
                    Check.Equal(1, archive.MaxRevision(contextKey), "first quota save writes revision MaxRevision+1");
                    Check.True(Logged("recovery revision saved:"), "pending revision save logged");

                    // 下一会话：exact 恢复同一批 GUID（不再走配额）
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData reload2 = NativeSim.CloneIsland(changed);
                    List<KnightUnit> finalUnits = new List<KnightUnit>();
                    NativeSim.RunLoad(reload2, (index, id) =>
                    {
                        KnightUnit unit = NativeSim.NewKnight(60000 + index);
                        finalUnits.Add(unit);
                        return unit;
                    });
                    Check.True(Logged("load-match scope=") && Logged("kind=exact"), "second load is an exact match");
                    for (int i = 0; i < finalUnits.Count; i++)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(finalUnits[i].Knight, out KnightIdentityReceipt restored), "identity restored");
                        Check.Same(minted[i], restored.Id, "exact restore keeps the minted GUID, no re-allocation");
                    }
                }
            });
        }

        private static void PartialReceiptsAndPanelPendingCancelTheWholeCohort()
        {
            Case.Run("partial_receipts_and_panel_pending_cancel_the_whole_quota_cohort", () =>
            {
                using (Fixture f = new Fixture())
                {
                    using (Fixture g = new Fixture())
                    {
                        // 用户动作优先：面板修订 pending 已挂 → 配额整批取消，且不改任何职业
                        IslandSaveData island = EnterKnownMismatch(g, 31000, new List<int> { 4, 5, 4, 5, 4 }, out string contextKey, out List<KnightUnit> reloaded);
                        KnightIdentityRuntime.ArmPanelRevision(contextKey);
                        KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                        Check.True(Logged("historical-quota-cancelled:panel-pending"), "panel pending wins");
                        foreach (KnightUnit unit in reloaded) Check.False(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "no style assigned behind the user action");
                    }

                    using (Fixture h = new Fixture())
                    {
                        // 部分已有收据（手动应用/其它来源）→ 整批取消，绝不覆盖、不混入第二套来源
                        EnterKnownMismatch(h, 32000, new List<int> { 4, 5, 4, 5, 4 }, out _, out List<KnightUnit> reloaded);
                        Check.True(KnightIdentityRuntime.TryPanelAssignAll(new[] { reloaded[0].Knight }, new[] { 3 }, out int assigned) && assigned == 1,
                            "manual-style receipt minted");
                        KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                        Check.True(Logged("historical-quota-cancelled:partial-receipts"), "partial receipts cancel the cohort");
                        foreach (KnightUnit unit in reloaded) Check.True(unit == reloaded[0] || !KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "no automatic assignment mixed in");
                    }

                    _ = f;
                }
            });
        }

        private static void QuotaBeforeFirstSaveIsDeterministicAndMintsFreshGuids()
        {
            Case.Run("quota_before_the_first_save_recomputes_the_same_counts_with_fresh_guids", () =>
            {
                using (Fixture f = new Fixture())
                {
                    IslandSaveData island = EnterKnownMismatch(f, 33000, new List<int> { 4, 5, 4, 5, 4 }, out _, out List<KnightUnit> firstSession);
                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.Equal("4/5/4/5/4", Line(StyleCounts(firstSession)), "first session quota");
                    List<Guid> firstIds = new List<Guid>();
                    foreach (KnightUnit unit in firstSession)
                    {
                        KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt receipt);
                        firstIds.Add(receipt.Id);
                    }

                    // 未保存退出 = 零持久化：下一会话重算同样数量、铸新 GUID（未持久化，不构成冲突）
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData reload = NativeSim.CloneIsland(island);
                    List<KnightUnit> secondSession = new List<KnightUnit>();
                    NativeSim.RunLoad(reload, (index, id) =>
                    {
                        KnightUnit unit = NativeSim.NewKnight(70000 + index);
                        secondSession.Add(unit);
                        return unit;
                    });
                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.Equal("4/5/4/5/4", Line(StyleCounts(secondSession)), "deterministic recompute before the first save");
                    for (int i = 0; i < secondSession.Count; i++)
                    {
                        KnightIdentityRuntime.TryGetReceipt(secondSession[i].Knight, out KnightIdentityReceipt receipt);
                        Check.NotEqual(firstIds[i], receipt.Id, "unstored receipts are re-minted, never carried across sessions");
                    }
                }
            });
        }

        private static void AllocationMatchesIndependentReferenceForAllCohortSizes()
        {
            Case.Run("quota_allocation_matches_an_independent_reference_for_every_cohort_size", () =>
            {
                int[][] histories =
                {
                    new[] { 4, 5, 4, 5, 4 },
                    new[] { 1, 1, 1, 1, 1 },
                    new[] { 7, 0, 0, 0, 0 },
                    new[] { 0, 0, 1, 0, 0 },
                    new[] { 2, 3, 5, 7, 11 },
                    new[] { 1, 0, 4, 0, 3 },
                };
                for (int h = 0; h < histories.Length; h++)
                {
                    int[] hist = histories[h];
                    for (int n = 1; n <= 512; n++)
                    {
                        int[] actual = KnightIdentityQuotaRecovery.Allocate(n, hist);
                        int[] expected = ReferenceAllocate(n, hist);
                        Check.Equal(Line(expected), Line(actual), "N=" + n + " H=" + Line(hist));
                    }
                }
                // 非法入口一律拒绝（不返回兜底分配）
                Check.True(KnightIdentityQuotaRecovery.Allocate(5, null) == null, "null history refused");
                Check.True(KnightIdentityQuotaRecovery.Allocate(5, new List<int> { 1, 1 }) == null, "wrong history length refused");
                Check.True(KnightIdentityQuotaRecovery.Allocate(5, new List<int> { 1, -1, 1, 1, 1 }) == null, "negative count refused");
                Check.True(KnightIdentityQuotaRecovery.Allocate(0, new List<int> { 1, 1, 1, 1, 1 }) == null, "empty cohort refused");
                Check.True(KnightIdentityQuotaRecovery.Allocate(5, new List<int> { 0, 0, 0, 0, 0 }) == null, "empty history refused");
            });
        }

        /// <summary>独立参考实现：long 精确余数排序（余数降序、平局 style ID 升序）后补席；不复用生产分支结构。</summary>
        private static int[] ReferenceAllocate(int n, int[] history)
        {
            long total = 0;
            for (int i = 0; i < history.Length; i++) total += history[i];
            if (total == 0) return null;

            int[] quota = new int[history.Length];
            long[] remainders = new long[history.Length];
            long seats = 0;
            for (int i = 0; i < history.Length; i++)
            {
                long scaled = (long)n * history[i];
                quota[i] = (int)(scaled / total);
                remainders[i] = scaled % total;
                seats += quota[i];
            }
            long remaining = n - seats;
            List<int> order = new List<int>();
            for (int i = 0; i < history.Length; i++) if (remainders[i] > 0) order.Add(i);
            order.Sort((a, b) => remainders[b] != remainders[a] ? remainders[b].CompareTo(remainders[a]) : a.CompareTo(b));
            for (int i = 0; i < order.Count && i < remaining; i++) quota[order[i]]++;
            return quota;
        }

        private static void EmptyLatestHandsOverToTheUniformRuleWithinTheAvailablePool()
        {
            Case.Run("empty_latest_hands_over_to_the_deterministic_uniform_rule_within_the_available_pool", () =>
            {
                using (Fixture f = new Fixture())
                {
                    string contextKey = Sidecar.ContextKey(4);

                    // 权威历史：旧的（rev 0，非空）与最新的（rev 3，空）= 当时岛上没有骑士
                    KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
                    string oldEpoch = KnightIdentityArchive.NewScope();
                    AddSnapshot(archive, contextKey, oldEpoch, 0, "2026-01-01T00:00:00.0000000+00:00", new Dictionary<string, KnightIdentityReceipt>
                    {
                        { "old-a", new KnightIdentityReceipt(Guid.NewGuid(), 1) },
                        { "old-b", new KnightIdentityReceipt(Guid.NewGuid(), 4) },
                    });
                    string emptyEpoch = KnightIdentityArchive.NewScope();
                    AddSnapshot(archive, contextKey, emptyEpoch, 3, "2027-01-01T00:00:00.0000000+00:00", new Dictionary<string, KnightIdentityReceipt>());
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f.SidecarPath));
                    Check.True(KnightIdentityArchiveStore.Save(f.SidecarPath, archive).Ok, "empty-latest fixture written");

                    // 本次加载：3 名骑士、内容对不上（known-mismatch）
                    IslandSaveData island = new IslandSaveData { land = 4, biome = 9 };
                    island.objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>();
                    for (int i = 0; i < 3; i++)
                    {
                        island.objects.Add(new IslandSaveData.ObjectData
                        {
                            name = "Knight(Clone)",
                            uniqueID = "el-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        });
                        island.objects[island.objects.Count - 1].componentData2.Add(new IslandSaveData.ObjectData.ComponentData
                        {
                            name = "Knight",
                            type = "KnightData",
                            data = "{\"rank\":1}",
                        });
                    }
                    List<KnightUnit> reloaded = new List<KnightUnit>();
                    CampaignSaveData.current = new CampaignSaveData { CurrentIsland = island };
                    NativeSim.RunLoad(island, (index, id) =>
                    {
                        KnightUnit unit = NativeSim.NewKnight(81000 + index);
                        reloaded.Add(unit);
                        return unit;
                    });
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "known-mismatch load");
                    Check.Equal(1, KnightIdentityLoadSeed.QuotaBatchCount, "empty-latest still builds a cohort");

                    // 可用池是子集 [1,3]：均匀分支只能从池内派发（不得发放不可用风格）
                    KnightIdentityQuotaRecovery.IntegrityPass(new List<int> { 1, 3 });
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out kind) && kind == "fresh-uniform",
                        "empty-latest flips the binding to fresh-uniform");
                    Check.True(KnightIdentityRuntime.CanFlushSeed, "write protection released by the fresh uniform branch");
                    Check.Equal("0/2/0/1/0", Line(StyleCounts(reloaded)), "uniform plan stays inside the available pool");
                    Check.True(Logged("fresh-uniform: empty-latest"), "the log states fresh/empty-latest, not historical recovery");
                    Check.False(Logged("historical-quota: knights=3"), "no fake historical-quota claim");

                    // 未保存退出 → 重放同一确定性结果（风格分布相同，GUID 重铸）
                    List<Guid> firstIds = new List<Guid>();
                    foreach (KnightUnit unit in reloaded)
                    {
                        KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt receipt);
                        firstIds.Add(receipt.Id);
                    }
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData replay = NativeSim.CloneIsland(island);
                    List<KnightUnit> secondSession = new List<KnightUnit>();
                    NativeSim.RunLoad(replay, (index, id) =>
                    {
                        KnightUnit unit = NativeSim.NewKnight(82000 + index);
                        secondSession.Add(unit);
                        return unit;
                    });
                    KnightIdentityQuotaRecovery.IntegrityPass(new List<int> { 1, 3 });
                    Check.Equal("0/2/0/1/0", Line(StyleCounts(secondSession)), "deterministic replay of the uniform result");

                    // 首次真实 Save：写所选有效来源 epoch 的 rev = MaxRevision+1，不新造 epoch；下一会话 exact 恢复
                    NativeSim.RunSave(replay, 0, 4, 0, secondSession);
                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(loaded.Archive.TryGetContext(contextKey, out KnightIdentityContext context), "context present");
                    Check.Equal(2, context.Epochs.Count, "no new epoch created (old + empty baseline only)");
                    Check.Equal(4, loaded.Archive.MaxRevision(contextKey), "revision advanced to MaxRevision+1 (3 -> 4)");
                    Check.True(Logged("recovery revision saved:"), "pending revision save logged");

                    List<Guid> minted = new List<Guid>();
                    foreach (KnightUnit unit in secondSession)
                    {
                        KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt receipt);
                        minted.Add(receipt.Id);
                    }
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData final = NativeSim.CloneIsland(replay);
                    List<KnightUnit> finalUnits = new List<KnightUnit>();
                    NativeSim.RunLoad(final, (index, id) =>
                    {
                        KnightUnit unit = NativeSim.NewKnight(83000 + index);
                        finalUnits.Add(unit);
                        return unit;
                    });
                    Check.True(Logged("load-match scope=") && Logged("kind=exact"), "next load is exact");
                    for (int i = 0; i < finalUnits.Count; i++)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(finalUnits[i].Knight, out KnightIdentityReceipt restored), "restored");
                        Check.Same(minted[i], restored.Id, "same GUID, no second allocation");
                    }
                }

                using (Fixture f = new Fixture())
                {
                    // 池不可用：绝不赋不可用风格，整批取消并保持写保护
                    EnterKnownMismatchWithEmptyLatest(f, out string contextKey, out List<KnightUnit> reloaded);
                    KnightIdentityQuotaRecovery.IntegrityPass(new List<int>());
                    Check.True(Logged("historical-quota-cancelled:no-style-pool"), "empty pool cancels the uniform branch");
                    foreach (KnightUnit unit in reloaded) Check.False(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "no style without a usable pool");
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "context stays unresolved");
                }
            });
        }

        /// <summary>造一个「最新快照为空」的权威来源 + 3 名 known-mismatch 装载骑士。</summary>
        private static void EnterKnownMismatchWithEmptyLatest(Fixture f, out string contextKey, out List<KnightUnit> reloaded)
        {
            contextKey = Sidecar.ContextKey(4);
            KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
            string oldEpoch = KnightIdentityArchive.NewScope();
            AddSnapshot(archive, contextKey, oldEpoch, 0, "2026-01-01T00:00:00.0000000+00:00", new Dictionary<string, KnightIdentityReceipt>
            {
                { "old-a", new KnightIdentityReceipt(Guid.NewGuid(), 1) },
            });
            AddSnapshot(archive, contextKey, KnightIdentityArchive.NewScope(), 3, "2027-01-01T00:00:00.0000000+00:00", new Dictionary<string, KnightIdentityReceipt>());
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f.SidecarPath));
            Check.True(KnightIdentityArchiveStore.Save(f.SidecarPath, archive).Ok, "empty-latest fixture written");

            IslandSaveData island = new IslandSaveData { land = 4, biome = 9 };
            island.objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>();
            for (int i = 0; i < 3; i++)
            {
                IslandSaveData.ObjectData record = new IslandSaveData.ObjectData
                {
                    name = "Knight(Clone)",
                    uniqueID = "el-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                };
                record.componentData2.Add(new IslandSaveData.ObjectData.ComponentData { name = "Knight", type = "KnightData", data = "{\"rank\":1}" });
                island.objects.Add(record);
            }
            List<KnightUnit> loaded = new List<KnightUnit>();
            CampaignSaveData.current = new CampaignSaveData { CurrentIsland = island };
            NativeSim.RunLoad(island, (index, id) =>
            {
                KnightUnit unit = NativeSim.NewKnight(84000 + index);
                loaded.Add(unit);
                return unit;
            });
            reloaded = loaded;
            Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "known-mismatch load");
        }

        private static void FailedQuotaSaveRetryKeepsGuidsAndConsumesRevisionOnlyOnSuccess()
        {
            Case.Run("a_failed_quota_save_retry_keeps_the_guids_and_consumes_the_revision_only_on_success", () =>
            {
                using (Fixture f = new Fixture())
                {
                    IslandSaveData island = EnterKnownMismatch(f, 35000, new List<int> { 4, 5, 4, 5, 4 }, out string contextKey, out List<KnightUnit> session);
                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    string epoch = Sidecar.ActiveEpoch(f.SidecarPath, 4);
                    Check.True(epoch != null, "historical epoch present");
                    Check.True(KnightIdentityRuntime.TryGetQuotaRevision(contextKey, epoch), "pending revision armed after recovery");

                    List<Guid> minted = new List<Guid>();
                    foreach (KnightUnit unit in session)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt receipt), "receipt present");
                        minted.Add(receipt.Id);
                    }

                    // 写盘失败（sidecar 损坏 → 只读降级）：不消费 pending、不重铸 GUID、文件一个字节不改
                    File.WriteAllBytes(f.SidecarPath, new byte[] { 0x01, 0x02, 0x03 });
                    byte[] broken = File.ReadAllBytes(f.SidecarPath);
                    NativeSim.RunSave(island, 0, 4, 0, session);
                    Check.True(Logged("sidecar-corrupt-readonly"), "the corrupt sidecar blocks the write");
                    Check.True(broken.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "failed save left the file untouched");
                    Check.True(KnightIdentityRuntime.TryGetQuotaRevision(contextKey, epoch), "a failed write never consumes the pending revision");
                    for (int i = 0; i < session.Count; i++)
                    {
                        KnightIdentityRuntime.TryGetReceipt(session[i].Knight, out KnightIdentityReceipt receipt);
                        Check.Same(minted[i], receipt.Id, "failed save never re-mints the runtime receipts");
                    }

                    // 重试成功：同一批 GUID 原样写入一次 rev=1，pending 消费
                    File.Delete(f.SidecarPath);
                    NativeSim.RunSave(island, 0, 4, 0, session);
                    Check.False(KnightIdentityRuntime.TryGetQuotaRevision(contextKey, epoch), "the successful write consumes the pending revision exactly once");
                    Check.True(Logged("recovery revision saved: rev=1"), "revision advanced from the (recreated) max 0 to 1");
                    for (int i = 0; i < session.Count; i++)
                    {
                        KnightIdentityRuntime.TryGetReceipt(session[i].Knight, out KnightIdentityReceipt receipt);
                        Check.Same(minted[i], receipt.Id, "retry keeps the same runtime receipts");
                    }

                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData reload = NativeSim.CloneIsland(island);
                    List<KnightUnit> finalUnits = new List<KnightUnit>();
                    NativeSim.RunLoad(reload, (index, id) =>
                    {
                        KnightUnit unit = NativeSim.NewKnight(86000 + index);
                        finalUnits.Add(unit);
                        return unit;
                    });
                    for (int i = 0; i < finalUnits.Count; i++)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(finalUnits[i].Knight, out KnightIdentityReceipt restored), "restored after retry");
                        Check.Same(minted[i], restored.Id, "retry wrote the same GUIDs");
                    }
                }
            });
        }

        private static void QuotaNeverTakesACohortWhileASaveIsInProgress()
        {
            Case.Run("a_ready_cohort_is_never_consumed_while_a_save_is_in_progress", () =>
            {
                using (Fixture f = new Fixture())
                {
                    EnterKnownMismatch(f, 36000, new List<int> { 4, 5, 4, 5, 4 }, out string contextKey, out List<KnightUnit> session);
                    Check.Equal(1, KnightIdentityLoadSeed.QuotaBatchCount, "cohort ready");

                    // 保存作用域打开期间（原生 isSavingGame + SaveCapture）：绝不取走 cohort、绝不铸收据
                    KnightIdentitySaveBridge.SaveCapture capture = KnightIdentitySaveBridge.BeginCapture(0, 4, 0);
                    IslandSaveData.isSavingGame = true;
                    try
                    {
                        KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                        Check.Equal(1, KnightIdentityLoadSeed.QuotaBatchCount, "the cohort is kept, not consumed");
                        foreach (KnightUnit unit in session) Check.False(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "no receipt minted during a save");
                        Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "context untouched");
                    }
                    finally
                    {
                        IslandSaveData.isSavingGame = false;
                        KnightIdentitySaveBridge.EndCapture(null, capture);
                    }

                    // 保存结束后同批照常恢复
                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.Equal(0, KnightIdentityLoadSeed.QuotaBatchCount, "the same cohort recovers after the save window");
                    Check.Equal("4/5/4/5/4", Line(StyleCounts(session)), "the kept cohort recovers the historical counts");
                }
            });
        }

        private static void ManualApplySupersedesTheQuotaPendingAcrossTwoSaves()
        {
            Case.Run("a_manual_apply_supersedes_the_quota_pending_so_two_saves_write_one_revision", () =>
            {
                using (Fixture f = new Fixture())
                {
                    IslandSaveData island = EnterKnownMismatch(f, 37000, new List<int> { 4, 5, 4, 5, 4 }, out string contextKey, out List<KnightUnit> session);
                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.Equal("4/5/4/5/4", Line(StyleCounts(session)), "quota applied");
                    string epoch = Sidecar.ActiveEpoch(f.SidecarPath, 4);
                    Check.True(KnightIdentityRuntime.TryGetQuotaRevision(contextKey, epoch), "quota pending armed");

                    // 用户动作（面板重派）：明确取代自动配额 pending
                    KnightIdentityRuntime.ArmPanelRevision(contextKey);
                    Check.False(KnightIdentityRuntime.TryGetQuotaRevision(contextKey, epoch), "the user action clears the quota pending");

                    // 第一次 Save 写用户修订（rev = MaxRevision+1），第二次 Save 不再产出任何自动 revision
                    NativeSim.RunSave(island, 0, 4, 0, session);
                    Check.True(Logged("user revision: rev=1"), "the first save writes the user revision");
                    Check.Equal(1, CountLogged("user revision: rev="), "exactly one user revision was written");
                    int snapshotsAfterFirst = CountSnapshots(contextKey, f);
                    int revisionAfterFirst = Sidecar.Load(f.SidecarPath).MaxRevision(contextKey);

                    // 第二次 Save：不再产出任何自动/配额 revision（残留 pending 已被用户动作清掉）
                    NativeSim.RunSave(island, 0, 4, 0, session);
                    Check.Equal(1, CountLogged("user revision: rev="), "the second save writes no further user revision");
                    Check.Equal(snapshotsAfterFirst, CountSnapshots(contextKey, f), "no second revision record was appended");
                    Check.Equal(revisionAfterFirst, Sidecar.Load(f.SidecarPath).MaxRevision(contextKey), "MaxRevision is unchanged by the second save");
                    Check.False(Logged("recovery revision saved:"), "the quota writer never ran after the user action");
                }
            });
        }

        private static int CountSnapshots(string contextKey, Fixture f)
        {
            KnightIdentityArchive archive = Sidecar.Load(f.SidecarPath);
            int total = 0;
            if (archive == null || !archive.TryGetContext(contextKey, out KnightIdentityContext context)) return 0;
            for (int i = 0; i < context.Epochs.Count; i++)
            {
                if (archive.TryGetSnapshots(context.Epochs[i], out IReadOnlyList<KnightIdentitySnapshot> snapshots)) total += snapshots.Count;
            }
            return total;
        }

        private static void TerminatedMembersAreDroppedAndTheRemainingCohortRecovers()
        {
            Case.Run("terminated_members_are_dropped_and_the_remaining_live_cohort_recovers", () =>
            {
                using (Fixture f = new Fixture())
                {
                    // 历史 5 人 [1,1,1,1,1]；本次只装载 4 人（cohort 有 5 条冻结记录，只有 4 个真实对象）
                    EnterKnownMismatchWithCount(f, 38000, new List<int> { 1, 1, 1, 1, 1 }, 4, out string contextKey, out List<KnightUnit> session);

                    // 一人被确证死亡（同 life isDead），另一人被销毁并换成新 life 的复用对象
                    session[0].Knight._damageable.isDead = true;
                    KnightUnit replacement = NativeSim.NewKnight(39099);
                    KnightIdentityRuntime.OnEnable(replacement.Knight);
                    session[1].DestroyForTests();

                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.True(Logged("dropped terminated=2"), "dead/destroyed members are dropped, not waited on");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(session[0].Knight, out _), "the dead member gets nothing");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(session[1].Knight, out _), "the destroyed member's old object gets nothing");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(replacement.Knight, out _), "the reused new life is never handed an identity");

                    // 剩余 N=2 按历史比例恢复（H=5 → 2 席：整数 0 + 最大余数 → [1,1,0,0,0]）
                    Check.Equal("1/1/0/0/0", Line(StyleCounts(new List<KnightUnit> { session[2], session[3] })), "remaining live members recover with the reduced N");
                    Check.Equal(2, CountReceipts(AllReceipts(session)), "exactly the 2 live members hold receipts (no hidden fifth actor)");
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "historical-quota", "context resolved for the live cohort");
                }
            });
        }

        private static void WorldReplacementCancelsTheCohortWithoutMinting()
        {
            Case.Run("a_world_replacement_cancels_the_cohort_without_minting", () =>
            {
                using (Fixture f = new Fixture())
                {
                    EnterKnownMismatch(f, 41000, new List<int> { 4, 5, 4, 5, 4 }, out string contextKey, out List<KnightUnit> session);

                    // world 确定换代（新 scene/gameLayer）：不猜、不恢复、不铸收据
                    NativeSim.ResetWorld(0x9911);
                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.True(Logged("historical-quota-cancelled:world-changed"), "the world swap cancels the cohort");
                    foreach (KnightUnit unit in session) Check.False(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "nothing minted after the world swap");
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "context stays unresolved");
                    Check.Equal(0, KnightIdentityLoadSeed.QuotaBatchCount, "the cancelled cohort is recycled");
                }
            });
        }

        private static void IncompleteOrFailedLoadsCancelTheCohortWithAReason()
        {
            Case.Run("incomplete_or_failed_loads_cancel_the_cohort_with_a_reason", () =>
            {
                using (Fixture f = new Fixture())
                {
                    // 不完整：5 条冻结记录只有 3 个真实对象（原生 Decay/失败少对象）
                    IslandSaveData island = EnterKnownMismatch(f, 45000, new List<int> { 1, 1, 1, 1, 1 }, out _, out _);
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData changed = NativeSim.CloneIsland(island);
                    List<KnightUnit> partial = new List<KnightUnit>();
                    KnightIdentityLoadBridge.LoadScope scope = KnightIdentityLoadBridge.Begin(changed);
                    try
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            KnightUnit unit = NativeSim.NewKnight(46000 + i);
                            partial.Add(unit);
                            KnightIdentityRuntime.OnEnable(unit.Knight);
                            KnightIdentityLoadBridge.HandleTryCreateOrFind(changed.objects[i], unit.Persistent);
                        }
                    }
                    finally
                    {
                        KnightIdentityLoadBridge.End(null, scope); // 外层成功但映射不完整
                    }
                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.True(Logged("historical-quota-cancelled:incomplete"), "an incomplete load cancels with a reason");
                    foreach (KnightUnit unit in partial) Check.False(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "no recovery from an incomplete load");
                    Check.Equal(0, KnightIdentityLoadSeed.QuotaBatchCount, "the incomplete cohort is recycled");
                }

                using (Fixture f = new Fixture())
                {
                    // 失败：外层 End 带异常（原生失败）——同样一次性取消并记原因，不无限重试
                    IslandSaveData island = EnterKnownMismatch(f, 47000, new List<int> { 1, 1, 1, 1, 1 }, out _, out _);
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData changed = NativeSim.CloneIsland(island);
                    List<KnightUnit> loaded = new List<KnightUnit>();
                    KnightIdentityLoadBridge.LoadScope scope = KnightIdentityLoadBridge.Begin(changed);
                    try
                    {
                        for (int i = 0; i < changed.objects.Count; i++)
                        {
                            KnightUnit unit = NativeSim.NewKnight(48000 + i);
                            loaded.Add(unit);
                            KnightIdentityRuntime.OnEnable(unit.Knight);
                            KnightIdentityLoadBridge.HandleTryCreateOrFind(changed.objects[i], unit.Persistent);
                        }
                    }
                    finally
                    {
                        KnightIdentityLoadBridge.End(new InvalidOperationException("native load failure"), scope);
                    }
                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.True(Logged("historical-quota-cancelled:native-failed"), "a failed load cancels with a reason");
                    foreach (KnightUnit unit in loaded) Check.False(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "no recovery from a failed load");
                    Check.Equal(0, KnightIdentityLoadSeed.QuotaBatchCount, "the failed cohort is recycled");
                }
            });
        }

        private static void ExactRollbackRebranchesActiveAndLaterMismatchUsesThatBranch()
        {
            Case.Run("an_exact_rollback_rebranches_active_and_a_later_mismatch_uses_that_branch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = BuildKnights(49000, new List<int> { 4, 0, 0, 0, 0 });
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, units);
                    string contextKey = Sidecar.ContextKey(4);
                    string epochOld = Sidecar.ActiveEpoch(f.SidecarPath, 4);
                    Check.True(epochOld != null, "old branch exists");

                    // 新代（Active=B，空基线）：配额来源绝不再看旧分支
                    IslandSaveData nextGeneration = NativeSim.CloneIsland(island);
                    nextGeneration.biome++;
                    Check.True(KnightIdentitySidecar.CommitGeneration(contextKey, KnightIdentityArchive.NewScope(), nextGeneration.Json()), "new generation committed");
                    string epochNew = Sidecar.ActiveEpoch(f.SidecarPath, 4);
                    Check.True(epochNew != null && epochNew != epochOld, "a new active generation exists");

                    // 精确回退到旧快照：exact 恢复 + 保存 → EnsureContext 把 Active 拨回旧分支
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData rollback = NativeSim.CloneIsland(island);
                    CampaignSaveData.current = new CampaignSaveData { CurrentIsland = rollback };
                    List<KnightUnit> back = new List<KnightUnit>();
                    NativeSim.RunLoad(rollback, (index, id) =>
                    {
                        KnightUnit unit = NativeSim.NewKnight(49100 + index);
                        back.Add(unit);
                        return unit;
                    });
                    Check.True(Logged("kind=exact"), "the rollback load is an exact match | " + DumpLog());
                    NativeSim.RunSave(rollback, 0, 4, 0, back);
                    Check.Equal(epochOld, Sidecar.ActiveEpoch(f.SidecarPath, 4), "the exact save re-branches Active to the old epoch");

                    // 再失配：来源只能是这个 Active 旧分支（4 名 style 0）
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData churned = NativeSim.CloneIsland(rollback);
                    churned.biome++;
                    for (int i = 0; i < churned.objects.Count; i++) churned.objects[i].uniqueID = "rb-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    CampaignSaveData.current = new CampaignSaveData { CurrentIsland = churned };
                    List<KnightUnit> session = new List<KnightUnit>();
                    NativeSim.RunLoad(churned, (index, id) =>
                    {
                        KnightUnit unit = NativeSim.NewKnight(49200 + index);
                        session.Add(unit);
                        return unit;
                    });
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "mismatch after the rebranch");
                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.Equal("4/0/0/0/0", Line(StyleCounts(session)), "the recovery uses the re-branched Active branch");
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out kind) && kind == "historical-quota", "recovery ran from the old branch");
                }
            });
        }

        private static void SameSceneWorldReplacementCancelsAndLandChangeCancels()
        {
            Case.Run("same_scene_world_replacement_and_a_land_change_both_cancel", () =>
            {
                using (Fixture f = new Fixture())
                {
                    EnterKnownMismatch(f, 50000, new List<int> { 4, 5, 4, 5, 4 }, out _, out List<KnightUnit> session);
                    int sceneHandle = Managers.Inst.world.gameObject.scene.handle;
                    NativeSim.ResetWorld(sceneHandle, sceneHandle + 0x1000); // 同 scene、新 world 指针
                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.True(Logged("historical-quota-cancelled:world-changed"), "a same-scene new world is a definitive change");
                    foreach (KnightUnit unit in session) Check.False(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "nothing minted");
                    Check.Equal(0, KnightIdentityLoadSeed.QuotaBatchCount, "the cohort is recycled");
                }

                using (Fixture f = new Fixture())
                {
                    EnterKnownMismatch(f, 51000, new List<int> { 4, 5, 4, 5, 4 }, out _, out List<KnightUnit> session);
                    // 同 world 同 scene，但当前岛已换（land 9 ≠ 批次 land 4）
                    CampaignSaveData.current = new CampaignSaveData { CurrentIsland = new IslandSaveData { land = 9 } };
                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.True(Logged("historical-quota-cancelled:context-changed"), "a change of the current island cancels the cohort");
                    foreach (KnightUnit unit in session) Check.False(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "nothing minted for another island");
                    Check.Equal(0, KnightIdentityLoadSeed.QuotaBatchCount, "the cohort is recycled");
                }
            });
        }

        private static void CurrentContextUnreadableKeepsTheCohortWaiting()
        {
            Case.Run("an_unreadable_current_context_keeps_the_cohort_waiting", () =>
            {
                using (Fixture f = new Fixture())
                {
                    IslandSaveData island = EnterKnownMismatch(f, 52000, new List<int> { 4, 5, 4, 5, 4 }, out string contextKey, out List<KnightUnit> session);
                    CampaignSaveData.current = null; // 读不到当前上下文：保留 cohort 等待，绝不先取走后取消
                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.Equal(1, KnightIdentityLoadSeed.QuotaBatchCount, "the cohort is kept while the context is unreadable");
                    foreach (KnightUnit unit in session) Check.False(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "no minting while waiting");

                    CampaignSaveData.current = new CampaignSaveData { CurrentIsland = island };
                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.Equal(0, KnightIdentityLoadSeed.QuotaBatchCount, "the same cohort recovers once the context is readable");
                    Check.Equal("4/5/4/5/4", Line(StyleCounts(session)), "recovered with the historical counts");
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "historical-quota", "context resolved");
                }
            });
        }

        private static void RealCohortSizesDriveTheRecovery()
        {
            Case.Run("real_serialized_cohort_sizes_drive_the_recovery", () =>
            {
                using (Fixture f = new Fixture())
                {
                    // 真实减少：历史 5 人，本次只序列化/生成 3 人
                    IslandSaveData island = EnterKnownMismatchWithCount(f, 53000, new List<int> { 1, 1, 1, 1, 1 }, 3, out _, out List<KnightUnit> session);
                    Check.Equal(3, island.objects.Count, "the serialized island really holds only 3 records");
                    Check.Equal(3, session.Count, "exactly 3 real actors were created (no hidden extras)");
                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.Equal("1/1/1/0/0", Line(StyleCounts(session)), "N=3 of H=5: floored shares fill the smallest styles");
                    Check.Equal(3, CountReceipts(AllReceipts(session)), "the whole live cohort is recovered (3 receipts)");

                    _ = f;
                }

                using (Fixture f = new Fixture())
                {
                    // 真实增加：历史 3 人，本次序列化 5 人（真实追加记录）
                    IslandSaveData island = EnterKnownMismatchWithCount(f, 54000, new List<int> { 0, 0, 1, 0, 0 }, 5, out _, out List<KnightUnit> session);
                    Check.Equal(5, island.objects.Count, "the serialized island really holds 5 records");
                    Check.Equal(5, session.Count, "exactly 5 real actors were created");
                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.Equal(5, CountReceipts(AllReceipts(session)), "every live member gets exactly one receipt");
                    Check.Equal(5, Total(StyleCounts(session)), "the recovery covers the whole live cohort");
                }
            });
        }

        private static void NativeOrderLoadQuotaSaveClearWorldReloadKeepsTheGuids()
        {
            Case.Run("native_order_load_quota_save_clear_world_reload_keeps_the_guids", () =>
            {
                using (Fixture f = new Fixture())
                {
                    // 会话 1：真实 native 顺序保存 6 名骑士，历史 [2,1,1,1,1]
                    List<KnightUnit> first = BuildKnights(42000, new List<int> { 2, 1, 1, 1, 1 });
                    IslandSaveData island = new IslandSaveData { land = 8 };
                    NativeSim.RunSaveNativeOrder(island, 0, 8, 0, first);
                    string contextKey = Sidecar.ContextKey(8);
                    Check.True(f.SidecarExists, "native-order save wrote the sidecar");

                    // 会话 2：内容/native ID 全变的真实顺序装载（原生随后清空 records），known-mismatch
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData changed = NativeSim.CloneIsland(island);
                    changed.biome++;
                    for (int i = 0; i < changed.objects.Count; i++) changed.objects[i].uniqueID = "no-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    List<KnightUnit> session = new List<KnightUnit>();
                    CampaignSaveData.current = new CampaignSaveData { CurrentIsland = changed };
                    NativeSim.RunLoadConsumingRecords(changed, (index, id) =>
                    {
                        KnightUnit unit = NativeSim.NewKnight(43000 + index);
                        session.Add(unit);
                        return unit;
                    });
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "known-mismatch after the consuming load");
                    Check.True(changed.objects == null, "the native load consumed island.objects");

                    KnightIdentityQuotaRecovery.IntegrityPass(Styles.All);
                    Check.Equal("2/1/1/1/1", Line(StyleCounts(session)), "quota kept the historical counts across the id churn");

                    // 真实顺序 Save（引用 GetID → 自身 GetID → Add records → finally 清映射 → ApplyCapture）
                    NativeSim.RunSaveNativeOrder(changed, 0, 8, 0, session);
                    Check.True(Logged("recovery revision saved:"), "the pending revision was written by the native-order save");

                    // 清世界 + 下一会话重载：exact 恢复同一批 GUID
                    List<Guid> minted = new List<Guid>();
                    foreach (KnightUnit unit in session)
                    {
                        KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt receipt);
                        minted.Add(receipt.Id);
                    }
                    NativeSim.ResetWorld(0x7722);
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData reload = NativeSim.CloneIsland(changed);
                    List<KnightUnit> finalUnits = new List<KnightUnit>();
                    CampaignSaveData.current = new CampaignSaveData { CurrentIsland = reload };
                    NativeSim.RunLoadConsumingRecords(reload, (index, id) =>
                    {
                        KnightUnit unit = NativeSim.NewKnight(44000 + index);
                        finalUnits.Add(unit);
                        return unit;
                    });
                    Check.True(Logged("load-match scope=") && Logged("kind=exact"), "the reload is an exact match");
                    for (int i = 0; i < finalUnits.Count; i++)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(finalUnits[i].Knight, out KnightIdentityReceipt restored), "restored");
                        Check.Same(minted[i], restored.Id, "same GUID across the world clear");
                    }
                }
            });
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>会话 1：按给定 style 分布保存 N 名骑士；会话 2：内容+native ID 全变后装载 → known-mismatch。</summary>
        private static IslandSaveData EnterKnownMismatch(Fixture f, int baseId, IReadOnlyList<int> history, out string contextKey, out List<KnightUnit> reloaded)
        {
            return EnterKnownMismatchWithCount(f, baseId, history, -1, out contextKey, out reloaded);
        }

        /// <summary>
        /// count &gt; 0 时本次**真实**只生成/序列化/装载该人数（records、JSON、冻结名单、实际 actor 全部一致）；
        /// count &gt; history 时真实追加新骑士记录（N 增加路径）。旧世代 actor 全部销毁并换新 world，避免隐藏角色。
        /// </summary>
        private static IslandSaveData EnterKnownMismatchWithCount(Fixture f, int baseId, IReadOnlyList<int> history, int count, out string contextKey, out List<KnightUnit> reloaded)
        {
            List<KnightUnit> units = BuildKnights(baseId, history);
            IslandSaveData island = new IslandSaveData { land = 4 };
            NativeSim.RunSave(island, 0, 4, 0, units);
            contextKey = Sidecar.ContextKey(4);

            // 旧世代退场：真实销毁 actor，换一个全新 world（同 scene 由 fixture 决定，避免隐藏角色干扰计数）
            for (int i = 0; i < units.Count; i++) units[i].DestroyForTests();
            KnightIdentityRuntime.ResetForTests();
            NativeSim.ResetWorld(0x6600 + (count > 0 ? count : 0));

            IslandSaveData changed = NativeSim.CloneIsland(island);
            changed.biome++;
            if (count > 0)
            {
                while (changed.objects.Count > count) changed.objects.RemoveAt(changed.objects.Count - 1);
                while (changed.objects.Count < count)
                {
                    IslandSaveData.ObjectData extra = new IslandSaveData.ObjectData
                    {
                        name = "Knight(Clone)",
                        uniqueID = "new-" + changed.objects.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    };
                    extra.componentData2.Add(new IslandSaveData.ObjectData.ComponentData { name = "Knight", type = "KnightData", data = "{\"rank\":1}" });
                    changed.objects.Add(extra);
                }
            }
            for (int i = 0; i < changed.objects.Count; i++) changed.objects[i].uniqueID = "churn-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            List<KnightUnit> loaded = new List<KnightUnit>();
            CampaignSaveData.current = new CampaignSaveData { CurrentIsland = changed }; // 当前原生上下文（消费前键匹配）
            NativeSim.RunLoad(changed, (index, id) =>
            {
                KnightUnit unit = NativeSim.NewKnight(baseId + 5000 + index);
                loaded.Add(unit);
                return unit;
            });
            reloaded = loaded;
            Check.Equal(changed.objects.Count, loaded.Count, "every serialized record spawned exactly one real actor");
            Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "known-mismatch session");
            _ = f;
            return changed;
        }

        /// <summary>创建 count 个真实 actor 并返回全部 receipt（含无收据的 null 槽），用于全量计数。</summary>
        private static List<KnightIdentityReceipt?> AllReceipts(IReadOnlyList<KnightUnit> units)
        {
            List<KnightIdentityReceipt?> receipts = new List<KnightIdentityReceipt?>(units.Count);
            for (int i = 0; i < units.Count; i++)
            {
                receipts.Add(KnightIdentityRuntime.TryGetReceipt(units[i].Knight, out KnightIdentityReceipt receipt) ? receipt : (KnightIdentityReceipt?)null);
            }
            return receipts;
        }

        private static int CountReceipts(IReadOnlyList<KnightIdentityReceipt?> receipts)
        {
            int count = 0;
            for (int i = 0; i < receipts.Count; i++) if (receipts[i].HasValue) count++;
            return count;
        }

        private static List<KnightUnit> BuildKnights(int baseId, IReadOnlyList<int> historicalCounts)
        {
            int[] styles = KnightIdentityQuotaRecovery.BuildPlan(historicalCounts, Total(historicalCounts));
            Check.True(styles != null, "historical counts form a plan");
            List<KnightUnit> units = new List<KnightUnit>(styles.Length);
            for (int i = 0; i < styles.Length; i++)
            {
                KnightUnit unit = NativeSim.NewKnight(baseId + i);
                KnightIdentityRuntime.OnEnable(unit.Knight);
                Check.True(KnightIdentityRuntime.TryResolve(unit.Knight, styles[i], 3u, Styles.All, out _), "resolve knight " + i);
                units.Add(unit);
            }
            return units;
        }

        private static int[] StyleCounts(IReadOnlyList<KnightUnit> units)
        {
            int[] counts = new int[KnightIdentityReceipt.StyleCount];
            for (int i = 0; i < units.Count; i++)
            {
                Check.True(KnightIdentityRuntime.TryGetReceipt(units[i].Knight, out KnightIdentityReceipt receipt), "receipt for unit " + i);
                counts[receipt.Style]++;
            }
            return counts;
        }

        private static int Total(IReadOnlyList<int> counts)
        {
            int total = 0;
            for (int i = 0; i < counts.Count; i++) total += counts[i];
            return total;
        }

        private static string Line(IReadOnlyList<int> counts)
        {
            return counts == null ? "<null>" : string.Join("/", counts);
        }

        private static int nextSnapshotNonce = 1;

        private static void AddSnapshot(KnightIdentityArchive archive, string contextKey, string epoch, int revision, string savedAtUtc,
            Dictionary<string, KnightIdentityReceipt> entries)
        {
            List<KnightIdentitySnapshotEntry> list = new List<KnightIdentitySnapshotEntry>();
            foreach (KeyValuePair<string, KnightIdentityReceipt> pair in entries)
            {
                list.Add(new KnightIdentitySnapshotEntry(pair.Key, pair.Value));
            }
            string hash = KnightIdentityFingerprint.Normalized("{\"quota\":\"" + epoch + "-" + nextSnapshotNonce++ .ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}", epoch);
            Check.True(KnightIdentitySnapshot.TryCreateCore(KnightIdentityFingerprint.KindNormalized, hash, savedAtUtc, list, revision,
                out KnightIdentitySnapshot snapshot, out string error), "snapshot created: " + error);
            Check.True(archive.RecordSnapshot(epoch, snapshot) == KnightIdentityArchive.MutationStatus.Applied, "snapshot recorded");
            Check.True(archive.EnsureContext(contextKey, epoch, true), "context owns epoch " + epoch);
        }

        private static string DumpLog()
        {
            LogSource log = KingdomEnhancedPlugin.Instance != null ? KingdomEnhancedPlugin.Instance.LogSource : null;
            if (log == null) return "<no log>";
            int start = log.Messages.Count > 14 ? log.Messages.Count - 14 : 0;
            return string.Join(" || ", log.Messages.GetRange(start, log.Messages.Count - start));
        }

        private static int CountLogged(string fragment)
        {
            LogSource log = KingdomEnhancedPlugin.Instance != null ? KingdomEnhancedPlugin.Instance.LogSource : null;
            if (log == null) return 0;
            int count = 0;
            for (int i = 0; i < log.Messages.Count; i++)
            {
                if (log.Messages[i].IndexOf(fragment, StringComparison.Ordinal) >= 0) count++;
            }
            return count;
        }

        private static bool Logged(string fragment)
        {
            LogSource log = KingdomEnhancedPlugin.Instance != null ? KingdomEnhancedPlugin.Instance.LogSource : null;
            if (log == null) return false;
            for (int i = 0; i < log.Messages.Count; i++)
            {
                if (log.Messages[i].IndexOf(fragment, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }
    }
}
