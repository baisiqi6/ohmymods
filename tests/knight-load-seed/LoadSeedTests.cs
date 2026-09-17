// KnightIdentityLoadSeed 回归：Load 期「首次迁移来源种子」的整批写入与全部保守边界。
// 直接编译生产 il2cpp/KnightIdentityLoadSeed.cs + KnightIdentityRuntime.cs + KnightIdentityArchive.cs，
// native 边界用本目录 NativeStubs.cs（复制自 tests/knight-identity-runtime，仅 ObjectData 增加模拟 Pointer）。
// 测试显式按 Operator 接线次序驱动 Begin/Capture/Complete/Flush，不新增任何生产 hook。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using KingdomEnhancedMod;

namespace KnightLoadSeedTests
{
    internal static class LoadSeedTests
    {
        private static readonly IReadOnlyList<int> Styles = new List<int> { 0, 1, 2, 3, 4 };

        internal static void Run()
        {
            MigrationSeedStabilizesKnightsWithoutNativeSave();
            PartialReceiptsAreNotWrittenEarly();
            DuplicateCaptureIsIdempotent();
            RepeatedBeginAndCompleteAreIdempotent();
            DecayRemovedObjectsAreNeverHalfWritten();
            SourceGuardsRejectWholeBatch();
            RecordPointerMismatchRejectsWholeBatch();
            UnreadableRecordsRejectWholeBatch();
            SquiresAreKnownExcludedNotIncomplete();
            OwnerChangesDropTheBatch();
            OwnerReadinessWaitsInsteadOfDropping();
            NativeFailureNeverSeeds();
            NestedLoadsStayIsolated();
            ClientNeverSeeds();
            CorruptOrUnknownSidecarIsProtectedWithBackoffRetry();
            ExistingHistoryIsPreserved();
            NewRecruitsNeverJoinTheSeed();
            ClearKeepsMemoryAndDiskUntouched();
            BatchCapacityIsBounded();
        }

        // ------------------------------------------------------------------ 主回归

        private static void MigrationSeedStabilizesKnightsWithoutNativeSave()
        {
            Case.Run("migration_seed_stabilizes_22_knights_without_any_native_save", () =>
            {
                using (Fixture f = new Fixture())
                {
                    // 原生 global 档：整个流程里必须字节/时间戳都不变（本模块不 Save）
                    string nativePath = System.IO.Path.Combine(f.Temp.Path, "global-v35.bin");
                    File.WriteAllText(nativePath, "native-save-bytes");
                    string nativeHashBefore = Sha256File(nativePath);
                    DateTime nativeWriteBefore = File.GetLastWriteTimeUtc(nativePath);

                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(4);
                    for (int i = 0; i < 22; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(21000 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }
                    string[] ids = IdsOf(island);
                    string originalJson = island.Json();
                    Dictionary<Knight, int> legacy = LegacyOf(units, i => i % 5);

                    SeededLoad run = RunSeededLoad(island, units);
                    Check.True(run.Succeeded, "native TryPop reported success");
                    Check.True(island.objects == null, "native sort/decay ended with objects = null");
                    Check.Equal(1, KnightIdentityLoadSeed.PendingCount, "complete mapping becomes one pending batch");
                    Check.False(f.SidecarExists, "nothing is written before the integrity pass");

                    IntegrityPass(units, legacy); // Operator：既有 PrimeExisting + ApplyKnightStyle 之后
                    Check.True(f.SidecarExists, "seed written during the existing 5s pass");
                    Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "persisted batch recycled");
                    Check.True(Logged("seed scope="), "seed receipt logged | " + Dump());

                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.Equal(KnightIdentityArchiveStatus.Valid, loaded.Status, "sidecar readable");
                    Check.True(loaded.Archive.TryGetContext(Sidecar.ContextKey(island.land), out KnightIdentityContext context),
                        "stable context registered for the island");
                    string scopeKey = context.Active;
                    string hash = KnightIdentityFingerprint.Normalized(originalJson, scopeKey);
                    Check.True(loaded.Archive.TryGetSnapshot(scopeKey, hash, out KnightIdentitySnapshot snapshot),
                        "kind2 snapshot stored under the stable epoch");
                    Check.Equal(22, snapshot.Count, "all 22 knights stored in one table");
                    Check.True(loaded.Archive.TryGetSnapshots(scopeKey, out IReadOnlyList<KnightIdentitySnapshot> scoped), "scope history readable");
                    Check.Equal(1, scoped.Count, "exactly one snapshot for this scope");

                    KnightIdentityReceipt[] seeded = new KnightIdentityReceipt[22];
                    for (int i = 0; i < 22; i++)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(units[i].Knight, out seeded[i]), "runtime receipt " + i);
                        Check.Equal(i % 5, seeded[i].Style, "legacy style pinned " + i);
                        Check.True(loaded.Archive.TryRestore(scopeKey, hash, ids[i], out KnightIdentityReceipt stored), "stored id " + i);
                        Check.Same(seeded[i].Id, stored.Id, "guid stored " + i);
                        Check.Equal(seeded[i].Style, stored.Style, "style stored " + i);
                    }

                    Check.False(KnightIdentitySaveBridge.HasActiveSaveCapture, "no native Save scope was ever opened");
                    Check.True(IslandSaveData.CurrentlySavingIsland == null && !IslandSaveData.isSavingGame, "native save statics untouched");

                    // 同原始 native JSON（同 id、不同 instance/指针）：精确命中，不再种
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData reloaded = NewIsland(4);
                    for (int i = 0; i < 22; i++) reloaded.objects.Add(Record(ids[i], 0x60000000L + i));
                    Check.Equal(originalJson, reloaded.Json(), "second island carries the same original native JSON");

                    List<KnightUnit> second = new List<KnightUnit>();
                    for (int i = 0; i < 22; i++) second.Add(NativeSim.NewKnight(31000 + i));
                    SeededLoad reload = RunSeededLoad(reloaded, second, nullOutList: false);
                    Check.True(reload.Succeeded, "second load succeeded");
                    Check.Equal(0, KnightIdentityLoadSeed.BatchCount, "an exact snapshot hit never re-seeds");

                    for (int i = 0; i < 22; i++)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(second[i].Knight, out KnightIdentityReceipt restored), "restored receipt " + i);
                        Check.Same(seeded[i].Id, restored.Id, "same guid after reload " + i);
                        Check.Equal(seeded[i].Style, restored.Style, "same style after reload " + i);
                    }

                    IntegrityPass(second, LegacyOf(second, i => i % 5)); // 再来一轮：不得新增快照
                    KnightIdentityArchiveStore.LoadResult after = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(after.Archive.TryGetSnapshot(scopeKey, hash, out KnightIdentitySnapshot same), "seeded snapshot still there");
                    Check.Equal(22, same.Count, "no extra entry (recruit/duplicate) added");
                    Check.True(after.Archive.TryGetSnapshots(scopeKey, out IReadOnlyList<KnightIdentitySnapshot> afterScoped), "history readable");
                    Check.Equal(1, afterScoped.Count, "no second snapshot for the same scope");

                    Check.Equal(nativeHashBefore, Sha256File(nativePath), "native save file bytes unchanged");
                    Check.Equal(nativeWriteBefore, File.GetLastWriteTimeUtc(nativePath), "native save file mtime unchanged");
                }
            });
        }

        private static void PartialReceiptsAreNotWrittenEarly()
        {
            Case.Run("partial_receipts_are_never_written_early", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(5);
                    for (int i = 0; i < 3; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(22000 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }

                    RunSeededLoad(island, units);
                    Check.Equal(1, KnightIdentityLoadSeed.PendingCount, "complete mapping pending");

                    KnightIdentityLoadSeed.Flush(); // 收据还没就绪
                    Check.False(f.SidecarExists, "no receipts yet => nothing written");
                    Check.Equal(1, KnightIdentityLoadSeed.PendingCount, "batch held for a later pass");

                    IntegrityPass(units, LegacyOf(units, i => 2, 1)); // 只有 1/3 就绪
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "a partial receipt set is never written");

                    IntegrityPass(units, LegacyOf(units, i => 2)); // 全部就绪
                    Check.True(f.SidecarExists, "written once every receipt is ready");
                    Check.True(Logged("seed scope="), "seed receipt logged");
                }
            });
        }

        private static void DuplicateCaptureIsIdempotent()
        {
            Case.Run("repeating_the_same_capture_pair_is_idempotent", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(6);
                    for (int i = 0; i < 2; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(23000 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }

                    string originalJson = island.Json(); // 原生排序/Decay 之前（也是 Begin 算 hash 的那份 JSON）
                    SeededLoad run = RunSeededLoad(island, units, (r, i, unit, record) =>
                    {
                        KnightIdentityLoadSeed.Capture(r.Scope, record, unit.Persistent);
                        KnightIdentityLoadSeed.Capture(r.Scope, record, unit.Persistent); // 重复同 pair
                        KnightIdentityLoadBridge.HandleTryCreateOrFind(record, unit.Persistent);
                    });
                    Check.True(run.Succeeded, "native success");
                    Check.Equal(1, KnightIdentityLoadSeed.PendingCount, "duplicates stay one batch");

                    IntegrityPass(units, LegacyOf(units, i => 1));
                    Check.True(f.SidecarExists, "duplicate captures do not block the seed");
                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(loaded.Archive.TryGetContext(Sidecar.ContextKey(island.land), out KnightIdentityContext context), "stable context registered");
                    string scopeKey = context.Active;
                    Check.True(loaded.Archive.TryGetSnapshot(scopeKey, KnightIdentityFingerprint.Normalized(originalJson, scopeKey), out KnightIdentitySnapshot snapshot),
                        "exact snapshot stored");
                    Check.Equal(2, snapshot.Count, "both knights stored exactly once");
                }
            });
        }

        private static void RepeatedBeginAndCompleteAreIdempotent()
        {
            Case.Run("repeated_begin_and_complete_are_idempotent", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(6);
                    for (int i = 0; i < 2; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(23500 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }
                    string originalJson = island.Json();

                    // 真实接线与手动调用可能同时发生：重复 Begin/Complete 绝不重复初始化或改变结果
                    KnightIdentityLoadBridge.LoadScope scope = KnightIdentityLoadBridge.Begin(island);
                    KnightIdentityLoadSeed.Begin(scope);
                    KnightIdentityLoadSeed.Begin(scope);
                    KnightIdentityRuntime.OnEnable(units[0].Knight);
                    KnightIdentityLoadSeed.Capture(scope, island.objects[0], units[0].Persistent);
                    KnightIdentityLoadSeed.Begin(scope); // 捕获之后重复 Begin：不得重置
                    KnightIdentityRuntime.OnEnable(units[1].Knight);
                    KnightIdentityLoadSeed.Capture(scope, island.objects[1], units[1].Persistent);
                    KnightIdentityLoadBridge.End(null, scope);
                    KnightIdentityLoadSeed.Complete(scope, true);
                    KnightIdentityLoadSeed.Complete(scope, false); // 重复 Complete：幂等

                    Check.Equal(1, KnightIdentityLoadSeed.BatchCount, "repeated Begin keeps exactly one batch");
                    Check.Equal(1, KnightIdentityLoadSeed.PendingCount, "the successful Complete wins");

                    IntegrityPass(units, LegacyOf(units, i => 1));
                    Check.True(f.SidecarExists, "seed written");
                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(loaded.Archive.TryGetContext(Sidecar.ContextKey(island.land), out KnightIdentityContext context), "stable context registered");
                    string scopeKey = context.Active;
                    Check.True(loaded.Archive.TryGetSnapshot(scopeKey, KnightIdentityFingerprint.Normalized(originalJson, scopeKey), out KnightIdentitySnapshot snapshot),
                        "exact snapshot stored");
                    Check.Equal(2, snapshot.Count, "both knights stored");
                    Check.Equal(0, KnightIdentityLoadSeed.BatchCount, "persisted batch recycled");
                }
            });
        }

        private static void DecayRemovedObjectsAreNeverHalfWritten()
        {
            Case.Run("objects_removed_by_native_decay_never_get_a_half_table", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(7);
                    for (int i = 0; i < 5; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(24000 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }

                    SeededLoad run = RunSeededLoad(island, units, (r, i, unit, record) =>
                    {
                        if (i < 3) return; // 前 3 条被原生 Decay 删掉了：没有 TryCreateOrFind
                        KnightIdentityLoadBridge.HandleTryCreateOrFind(record, unit.Persistent);
                    });

                    Check.True(run.Succeeded, "native load still succeeded");
                    Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "partial mapping is never pending");
                    IntegrityPass(units, LegacyOf(units, i => 0));
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "no seed for an incomplete snapshot");
                    Check.True(Logged("seed-batch-rejected:incomplete"), "incomplete reason logged | " + Dump());
                }
            });
        }

        // ------------------------------------------------------------------ 冻结/捕获守卫

        private static void SourceGuardsRejectWholeBatch()
        {
            Case.Run("duplicate_unique_id_rejects_the_batch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    IslandSaveData island = NewIsland(8);
                    island.objects.Add(Record("dup-id", 0x70000001L));
                    island.objects.Add(Record("dup-id", 0x70000002L));
                    OpenAndComplete(island, true);
                    Check.Equal(0, KnightIdentityLoadSeed.BatchCount, "duplicate id: no batch");
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "nothing written");
                    Check.True(Logged("seed-reject:duplicate-id"), "reason logged | " + Dump());
                }
            });

            Case.Run("invalid_unique_id_rejects_the_batch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    IslandSaveData island = NewIsland(8);
                    island.objects.Add(Record(string.Empty, 0x70100001L));
                    island.objects.Add(Record(new string('x', 257), 0x70100002L));
                    OpenAndComplete(island, true);
                    Check.Equal(0, KnightIdentityLoadSeed.BatchCount, "invalid id: no batch");
                    Check.True(Logged("seed-reject:invalid-id"), "reason logged | " + Dump());
                }
            });

            Case.Run("more_than_512_knight_records_rejects_the_batch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    IslandSaveData island = NewIsland(9);
                    for (int i = 0; i < KnightIdentityLoadSeed.MaxFrozenRecords + 1; i++)
                    {
                        island.objects.Add(Record("cap-" + i.ToString(CultureInfo.InvariantCulture), 0x71000000L + i));
                    }
                    OpenAndComplete(island, true);
                    Check.Equal(0, KnightIdentityLoadSeed.BatchCount, "over 512: no batch");
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "nothing written");
                    Check.True(Logged("seed-reject:over-cap"), "reason logged | " + Dump());
                }
            });

            Case.Run("zero_record_pointer_rejects_the_batch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    IslandSaveData island = NewIsland(9);
                    island.objects.Add(Record("zero-ptr", 0L));
                    OpenAndComplete(island, true);
                    Check.Equal(0, KnightIdentityLoadSeed.BatchCount, "zero pointer: no batch");
                    Check.True(Logged("seed-reject:zero-pointer"), "reason logged | " + Dump());
                }
            });

            Case.Run("one_id_two_owners_conflicts_the_batch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit first = NativeSim.NewKnight(25001);
                    KnightUnit second = NativeSim.NewKnight(25002);
                    IslandSaveData island = NewIsland(10);
                    IslandSaveData.ObjectData record = Record("one-id", 0x72000001L);
                    island.objects.Add(record);

                    KnightIdentityLoadBridge.LoadScope scope = KnightIdentityLoadBridge.Begin(island);
                    KnightIdentityLoadSeed.Begin(scope);
                    KnightIdentityRuntime.OnEnable(first.Knight);
                    KnightIdentityLoadSeed.Capture(scope, record, first.Persistent);
                    KnightIdentityRuntime.OnEnable(second.Knight);
                    KnightIdentityLoadSeed.Capture(scope, record, second.Persistent); // 同一 id 另一个 owner
                    KnightIdentityLoadBridge.End(null, scope);
                    KnightIdentityLoadSeed.Complete(scope, true);

                    Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "conflict never becomes pending");
                    IntegrityPass(new List<KnightUnit> { first, second }, LegacyOf(new List<KnightUnit> { first, second }, i => 3));
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "conflicted batch is never written");
                    Check.True(Logged("seed-batch-rejected:owner-life-conflict"), "conflict reason logged | " + Dump());
                }
            });

            Case.Run("one_owner_two_ids_conflicts_the_batch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit unit = NativeSim.NewKnight(26001);
                    IslandSaveData island = NewIsland(10);
                    IslandSaveData.ObjectData first = Record("owner-a", 0x73000001L);
                    IslandSaveData.ObjectData second = Record("owner-b", 0x73000002L);
                    island.objects.Add(first);
                    island.objects.Add(second);

                    KnightIdentityLoadBridge.LoadScope scope = KnightIdentityLoadBridge.Begin(island);
                    KnightIdentityLoadSeed.Begin(scope);
                    KnightIdentityRuntime.OnEnable(unit.Knight);
                    KnightIdentityLoadSeed.Capture(scope, first, unit.Persistent);
                    KnightIdentityLoadSeed.Capture(scope, second, unit.Persistent); // 同一 owner 第二个 id
                    KnightIdentityLoadBridge.End(null, scope);
                    KnightIdentityLoadSeed.Complete(scope, true);

                    Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "conflict never becomes pending");
                    IntegrityPass(new List<KnightUnit> { unit }, LegacyOf(new List<KnightUnit> { unit }, i => 3));
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "conflicted batch is never written");
                    Check.True(Logged("seed-batch-rejected:owner-two-ids"), "conflict reason logged | " + Dump());
                }
            });
        }

        private static void RecordPointerMismatchRejectsWholeBatch()
        {
            Case.Run("a_changed_record_instance_rejects_the_whole_batch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit first = NativeSim.NewKnight(27001);
                    KnightUnit second = NativeSim.NewKnight(27002);
                    IslandSaveData island = NewIsland(11);
                    IslandSaveData.ObjectData firstRecord = first.ToRecord();
                    IslandSaveData.ObjectData secondRecord = second.ToRecord();
                    IslandSaveData.ObjectData stranger = Record(secondRecord.uniqueID); // 同 id、另一个 record 实例
                    island.objects.Add(firstRecord);
                    island.objects.Add(secondRecord);

                    List<KnightUnit> units = new List<KnightUnit> { first, second };
                    SeededLoad run = RunSeededLoad(island, units, (r, i, unit, record) =>
                    {
                        KnightIdentityLoadBridge.HandleTryCreateOrFind(i == 0 ? firstRecord : stranger, unit.Persistent);
                    });

                    Check.True(run.Succeeded, "native success");
                    Check.True(Logged("seed-batch-rejected:record-mismatch"), "a changed record instance rejects the batch | " + Dump());
                    Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "a rejected batch is never pending");
                    IntegrityPass(units, LegacyOf(units, i => 1));
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "nothing written for a rejected batch");
                }
            });

            Case.Run("a_copy_of_an_already_captured_record_rejects_the_batch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit unit = NativeSim.NewKnight(27100);
                    IslandSaveData island = NewIsland(11);
                    IslandSaveData.ObjectData record = unit.ToRecord();
                    island.objects.Add(record);
                    IslandSaveData.ObjectData copy = Record(record.uniqueID); // 同 id、同内容、另一个实例

                    List<KnightUnit> units = new List<KnightUnit> { unit };
                    SeededLoad run = RunSeededLoad(island, units, (r, i, u, frozen) =>
                    {
                        KnightIdentityLoadBridge.HandleTryCreateOrFind(frozen, u.Persistent); // 正确 pair 先捕获
                        KnightIdentityLoadBridge.HandleTryCreateOrFind(copy, u.Persistent);   // 复制记录：指针不符
                    });

                    Check.True(run.Succeeded, "native success");
                    Check.True(Logged("seed-batch-rejected:record-mismatch"), "the copy rejects the whole batch | " + Dump());
                    Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "no pending after the mismatch");
                    IntegrityPass(units, LegacyOf(units, i => 1));
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "nothing written");
                }
            });
        }

        private static void UnreadableRecordsRejectWholeBatch()
        {
            Case.Run("an_unreadable_knight_record_rejects_the_batch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit first = NativeSim.NewKnight(27201);
                    KnightUnit second = NativeSim.NewKnight(27202);
                    IslandSaveData island = NewIsland(11);
                    IslandSaveData.ObjectData firstRecord = first.ToRecord();
                    IslandSaveData.ObjectData unreadable = second.ToRecord();
                    island.objects.Add(firstRecord);
                    island.objects.Add(unreadable);

                    // 冻结时（原生 sort/Decay 之前）组件列表读不出来：必须是整批拒绝，绝不当作非 Knight 忽略
                    unreadable.ThrowOnComponentsReadForTests = true;
                    KnightIdentityLoadBridge.LoadScope scope = KnightIdentityLoadBridge.Begin(island);
                    unreadable.ThrowOnComponentsReadForTests = false;
                    KnightIdentityLoadBridge.End(null, scope, true);

                    Check.Equal(0, KnightIdentityLoadSeed.BatchCount, "unknown record: no batch");
                    IntegrityPass(new List<KnightUnit> { first, second }, LegacyOf(new List<KnightUnit> { first, second }, i => 1));
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "nothing written");
                    Check.True(Logged("seed-reject:record-read"), "unknown record reason logged | " + Dump());
                }
            });
        }

        private static void SquiresAreKnownExcludedNotIncomplete()
        {
            Case.Run("a_mixed_knight_and_squire_island_seeds_only_the_knights", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> knights = new List<KnightUnit>();
                    List<KnightUnit> squires = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(25);
                    for (int i = 0; i < 2; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(42000 + i);
                        knights.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(42100 + i, "Squire"); // Squire 同样带 Knight/KnightData
                        squires.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }
                    string[] knightIds = new string[] { island.objects[0].uniqueID, island.objects[1].uniqueID };
                    string[] squireIds = new string[] { island.objects[2].uniqueID, island.objects[3].uniqueID, island.objects[4].uniqueID };
                    string originalJson = island.Json();

                    List<KnightUnit> all = new List<KnightUnit>(knights);
                    all.AddRange(squires);
                    SeededLoad run = RunSeededLoad(island, all);
                    Check.True(run.Succeeded, "native success");
                    Check.Equal(1, KnightIdentityLoadSeed.PendingCount, "a mixed island maps completely (knights + excluded squires)");

                    IntegrityPass(knights, LegacyOf(knights, i => 1)); // 只有真实 Knight 需要收据
                    Check.True(f.SidecarExists, "the mixed island still seeds");

                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(loaded.Archive.TryGetContext(Sidecar.ContextKey(island.land), out KnightIdentityContext context), "stable context registered");
                    string scopeKey = context.Active;
                    Check.True(loaded.Archive.TryGetSnapshot(scopeKey, KnightIdentityFingerprint.Normalized(originalJson, scopeKey), out KnightIdentitySnapshot snapshot),
                        "exact snapshot stored");
                    Check.Equal(2, snapshot.Count, "only the two real knights are stored");
                    for (int i = 0; i < 2; i++) Check.True(snapshot.TryGet(knightIds[i], out _), "knight stored " + i);
                    for (int i = 0; i < 3; i++) Check.False(snapshot.TryGet(squireIds[i], out _), "excluded squire not stored " + i);
                    Check.True(Logged("excluded=3"), "exclusion counted in the receipt | " + Dump());
                }
            });

            Case.Run("an_all_squire_island_never_writes_an_empty_table", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> squires = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(26);
                    for (int i = 0; i < 2; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(42200 + i, "Squire");
                        squires.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }

                    RunSeededLoad(island, squires);
                    Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "all-squire mapping is never pending");
                    IntegrityPass(squires, null);
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "an empty table is never written");
                    Check.True(Logged("seed-batch-rejected:no-knights"), "reason logged | " + Dump());
                }
            });

            Case.Run("a_squire_id_rebound_to_another_owner_conflicts", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit first = NativeSim.NewKnight(42301, "Squire");
                    KnightUnit second = NativeSim.NewKnight(42302, "Squire");
                    IslandSaveData island = NewIsland(27);
                    IslandSaveData.ObjectData record = first.ToRecord();
                    island.objects.Add(record);

                    KnightIdentityLoadBridge.LoadScope scope = KnightIdentityLoadBridge.Begin(island);
                    KnightIdentityLoadSeed.Begin(scope);
                    KnightIdentityRuntime.OnEnable(first.Knight);
                    KnightIdentityLoadSeed.Capture(scope, record, first.Persistent);
                    KnightIdentityRuntime.OnEnable(second.Knight);
                    KnightIdentityLoadSeed.Capture(scope, record, second.Persistent); // 同一 id 另一个 owner
                    KnightIdentityLoadBridge.End(null, scope);
                    KnightIdentityLoadSeed.Complete(scope, true);

                    Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "conflict never becomes pending");
                    Check.True(Logged("seed-batch-rejected:owner-life-conflict"), "conflict reason logged | " + Dump());
                }
            });

            Case.Run("a_squire_promoted_to_knight_on_the_same_root_conflicts", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit unit = NativeSim.NewKnight(42401, "Squire");
                    IslandSaveData island = NewIsland(28);
                    IslandSaveData.ObjectData record = unit.ToRecord();
                    island.objects.Add(record);

                    KnightIdentityLoadBridge.LoadScope scope = KnightIdentityLoadBridge.Begin(island);
                    KnightIdentityLoadSeed.Begin(scope);
                    KnightIdentityRuntime.OnEnable(unit.Knight);
                    KnightIdentityLoadSeed.Capture(scope, record, unit.Persistent);
                    unit.Go.tag = "Knight"; // 同 id 同 owner，但角色改成 Knight
                    KnightIdentityLoadSeed.Capture(scope, record, unit.Persistent);
                    KnightIdentityLoadBridge.End(null, scope);
                    KnightIdentityLoadSeed.Complete(scope, true);

                    Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "a role change on the same id conflicts");
                    Check.True(Logged("seed-batch-rejected:owner-life-conflict"), "conflict reason logged | " + Dump());
                }
            });

            Case.Run("a_squire_promoted_after_capture_drops_the_batch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> knights = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(29);
                    for (int i = 0; i < 2; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(42500 + i);
                        knights.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }
                    KnightUnit squire = NativeSim.NewKnight(42550, "Squire");
                    island.objects.Add(squire.ToRecord());
                    List<KnightUnit> all = new List<KnightUnit>(knights) { squire };

                    RunSeededLoad(island, all);
                    IntegrityPass(knights, LegacyOf(knights, i => 1));
                    Check.True(f.SidecarExists, "seed written first");

                    // 排除条目晋升为 Knight：不能把新 Knight 漏在原快照外 → 下个批次整体丢弃（这里已持久化的批次不受影响）
                    KnightIdentityRuntime.ResetForTests();
                    File.Delete(f.SidecarPath); // 让下一次加载重新走迁移路径（无精确快照）
                    island.objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>();
                    foreach (KnightUnit unit in all) island.objects.Add(unit.ToRecord());
                    RunSeededLoad(island, all);
                    Prime(knights, LegacyOf(knights, i => 1)); // 只 Prime：晋升前不落盘
                    squire.Go.tag = "Knight";
                    KnightIdentityLoadSeed.Flush();
                    Check.True(Logged("seed-dropped:excluded-changed"), "a promoted exclusion drops the batch | " + Dump());
                }
            });
        }

        private static void OwnerReadinessWaitsInsteadOfDropping()
        {
            Case.Run("an_inactive_owner_waits_instead_of_dropping", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(31);
                    for (int i = 0; i < 2; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(44000 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }

                    RunSeededLoad(island, units);
                    IntegrityPass(units, LegacyOf(units, i => 1));
                    Check.True(f.SidecarExists, "seed written while active");

                    // 新一批：inactive 只等待，不丢批、不落盘
                    KnightIdentityRuntime.ResetForTests();
                    File.Delete(f.SidecarPath); // 让下一次加载重新走迁移路径（无精确快照）
                    island.objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>();
                    foreach (KnightUnit unit in units) island.objects.Add(unit.ToRecord());
                    RunSeededLoad(island, units);
                    Prime(units, LegacyOf(units, i => 1)); // 只 Prime：Flush 由本用例显式控制

                    units[1].Go.activeInHierarchy = false;
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "an inactive owner waits");
                    Check.Equal(1, KnightIdentityLoadSeed.PendingCount, "the batch is kept while waiting");
                    Check.False(Logged("seed-dropped:"), "waiting is not a drop | " + Dump());

                    units[1].Go.activeInHierarchy = true;
                    KnightIdentityLoadSeed.Flush();
                    Check.True(f.SidecarExists, "the batch persists once ready again");
                }
            });

            Case.Run("a_read_failure_waits_instead_of_dropping", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(32);
                    for (int i = 0; i < 2; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(44100 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }

                    RunSeededLoad(island, units);
                    IntegrityPass(units, LegacyOf(units, i => 1));
                    Check.True(f.SidecarExists, "seed written before the failure");
                    File.Delete(f.SidecarPath);

                    KnightIdentityRuntime.ResetForTests();
                    File.Delete(f.SidecarPath); // 让下一次加载重新走迁移路径（无精确快照）
                    island.objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>();
                    foreach (KnightUnit unit in units) island.objects.Add(unit.ToRecord());
                    RunSeededLoad(island, units);
                    Prime(units, LegacyOf(units, i => 1)); // 只 Prime：Flush 由本用例显式控制

                    units[0].Knight.ThrowOnGameObjectReadForTests = true; // 读异常：不是确证销毁
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "an unreadable owner waits");
                    Check.Equal(1, KnightIdentityLoadSeed.PendingCount, "the batch is kept");
                    Check.False(Logged("seed-dropped:"), "a read failure is not a drop | " + Dump());

                    units[0].Knight.ThrowOnGameObjectReadForTests = false;
                    KnightIdentityLoadSeed.Flush();
                    Check.True(f.SidecarExists, "the batch persists once readable again");
                }
            });
        }

        private static void OwnerChangesDropTheBatch()
        {
            Case.Run("a_new_life_after_capture_drops_the_batch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(12);
                    for (int i = 0; i < 3; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(28000 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }

                    RunSeededLoad(island, units);
                    KnightIdentityRuntime.OnEnable(units[1].Knight); // OnEnable 新 life：不得重新认领
                    IntegrityPass(units, LegacyOf(units, i => 4));
                    KnightIdentityLoadSeed.Flush();

                    Check.False(f.SidecarExists, "a changed life drops the batch");
                    Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "dropped batch recycled");
                    Check.True(Logged("seed-dropped:owner-changed"), "drop logged | " + Dump());
                }
            });

            Case.Run("a_dead_owner_drops_the_batch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(13);
                    for (int i = 0; i < 3; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(29000 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }

                    RunSeededLoad(island, units);
                    units[2].Knight._damageable.isDead = true;
                    IntegrityPass(units, LegacyOf(units, i => 4));
                    KnightIdentityLoadSeed.Flush();

                    Check.False(f.SidecarExists, "a dead owner drops the batch");
                    Check.True(Logged("seed-dropped:owner-changed"), "drop logged | " + Dump());
                }
            });

            Case.Run("a_world_change_drops_the_batch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(14);
                    for (int i = 0; i < 3; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(30000 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }

                    RunSeededLoad(island, units);
                    NativeSim.ResetWorld(0x9001); // 换 world/scene：旧 owner 明确不再属于这里
                    IntegrityPass(units, null);
                    KnightIdentityLoadSeed.Flush();

                    Check.False(f.SidecarExists, "a world change drops the batch");
                    Check.True(Logged("seed-dropped:owner-changed"), "drop logged | " + Dump());
                }
            });
        }

        private static void NativeFailureNeverSeeds()
        {
            Case.Run("native_exception_never_seeds", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(15);
                    for (int i = 0; i < 4; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(32000 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }

                    SeededLoad run = RunSeededLoad(island, units, (r, i, unit, record) =>
                    {
                        KnightIdentityLoadSeed.Capture(r.Scope, record, unit.Persistent);
                        if (i == 2) throw new InvalidOperationException("native boom");
                        KnightIdentityLoadBridge.HandleTryCreateOrFind(record, unit.Persistent);
                    });

                    Check.False(run.Succeeded, "native load failed");
                    Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "failure never becomes pending");
                    IntegrityPass(units, LegacyOf(units, i => 2));
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "no seed after a native failure");
                    Check.True(Logged("seed-batch-rejected:native-failed"), "reason logged | " + Dump());
                }
            });

            Case.Run("native_false_result_never_seeds", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(16);
                    for (int i = 0; i < 2; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(33000 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }

                    SeededLoad run = RunSeededLoad(island, units, null, nullOutList: true, nativeResult: false);
                    Check.False(run.Succeeded, "native returned false");
                    Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "false result never becomes pending");
                    IntegrityPass(units, LegacyOf(units, i => 2));
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "no seed when the native load reports false");
                }
            });
        }

        private static void NestedLoadsStayIsolated()
        {
            Case.Run("nested_load_scopes_keep_independent_batches", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit outerUnit = NativeSim.NewKnight(34001);
                    KnightUnit innerUnit = NativeSim.NewKnight(34002);
                    IslandSaveData outerIsland = NewIsland(17);
                    IslandSaveData innerIsland = NewIsland(18);
                    IslandSaveData.ObjectData outerRecord = outerUnit.ToRecord();
                    IslandSaveData.ObjectData innerRecord = innerUnit.ToRecord();
                    outerIsland.objects.Add(outerRecord);
                    innerIsland.objects.Add(innerRecord);
                    string outerJson = outerIsland.Json();
                    string innerJson = innerIsland.Json();

                    KnightIdentityLoadBridge.LoadScope outerScope = KnightIdentityLoadBridge.Begin(outerIsland);
                    KnightIdentityLoadSeed.Begin(outerScope);
                    KnightIdentityLoadBridge.LoadScope innerScope = KnightIdentityLoadBridge.Begin(innerIsland);
                    KnightIdentityLoadSeed.Begin(innerScope);

                    Check.Equal(2, KnightIdentityLoadSeed.BatchCount, "each nested scope owns its own batch");
                    Check.True(ReferenceEquals(KnightIdentityLoadBridge.Current, innerScope), "inner scope is current");

                    KnightIdentityRuntime.OnEnable(innerUnit.Knight);
                    KnightIdentityLoadSeed.Capture(innerScope, innerRecord, innerUnit.Persistent);
                    KnightIdentityRuntime.OnEnable(outerUnit.Knight);
                    KnightIdentityLoadSeed.Capture(outerScope, outerRecord, outerUnit.Persistent);

                    Check.True(KnightIdentityLoadBridge.End(null, innerScope) == null, "inner finalizer passes no exception");
                    KnightIdentityLoadSeed.Complete(innerScope, true);
                    Check.Equal(1, KnightIdentityLoadSeed.PendingCount, "inner batch pending");
                    Check.False(KnightIdentityContexts.TryGetBinding(innerScope.ContextKey, out _, out _, out _), "inner binding not committed before outer succeeds");
                    Check.True(KnightIdentityLoadBridge.End(null, outerScope) == null, "outer finalizer passes no exception");
                    KnightIdentityLoadSeed.Complete(outerScope, true);
                    Check.Equal(2, KnightIdentityLoadSeed.PendingCount, "outer batch pending too");
                    Check.True(KnightIdentityContexts.TryGetBinding(innerScope.ContextKey, out string committedInner, out bool innerUnresolved, out _) && committedInner == innerScope.ScopeKey && !innerUnresolved, "successful outer commits inner scope unchanged");

                    List<KnightUnit> all = new List<KnightUnit> { outerUnit, innerUnit };
                    IntegrityPass(all, LegacyOf(all, i => 3));
                    Check.True(f.SidecarExists, "both nested batches written");

                    string outerKey = Sidecar.ActiveEpoch(f.SidecarPath, outerIsland.land);
                    string innerKey = Sidecar.ActiveEpoch(f.SidecarPath, innerIsland.land);
                    Check.True(outerKey != null && innerKey != null, "both stable contexts registered");
                    Check.NotEqual(outerKey, innerKey, "nested islands are different contexts");
                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(loaded.Archive.TryGetSnapshot(outerKey, KnightIdentityFingerprint.Normalized(outerJson, outerKey), out KnightIdentitySnapshot outerSnapshot),
                        "outer snapshot stored");
                    Check.Equal(1, outerSnapshot.Count, "outer snapshot carries only its own knight");
                    Check.True(loaded.Archive.TryGetSnapshot(innerKey, KnightIdentityFingerprint.Normalized(innerJson, innerKey), out KnightIdentitySnapshot innerSnapshot),
                        "inner snapshot stored");
                    Check.Equal(1, innerSnapshot.Count, "inner snapshot carries only its own knight");
                    Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "all batches recycled");
                }
            });
        }

        private static void ClientNeverSeeds()
        {
            Case.Run("a_client_never_seeds_the_sidecar", () =>
            {
                using (Fixture f = new Fixture())
                {
                    NetworkBigBoss.HasWorldAuth = false; // 客机：收据只来自主机
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(19);
                    for (int i = 0; i < 2; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(35000 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }

                    RunSeededLoad(island, units);
                    Check.Equal(0, KnightIdentityLoadSeed.BatchCount, "client has no seed batch");
                    Check.False(KnightIdentityRuntime.TryResolve(units[0].Knight, -1, 3u, Styles, out _), "client never allocates a guid");
                    IntegrityPass(units, null);
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "client writes nothing");
                }
            });
        }

        private static void CorruptOrUnknownSidecarIsProtectedWithBackoffRetry()
        {
            Case.Run("unknown_schema_is_never_overwritten_and_retries_after_backoff", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(20);
                    for (int i = 0; i < 2; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(36000 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }

                    RunSeededLoad(island, units); // sidecar Missing：真实接线开始种子批次
                    Prime(units, LegacyOf(units, i => 1));

                    // 第一次 Flush 之前外部放入未知 schema 的 sidecar：批次保留，文件绝不覆盖
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f.SidecarPath));
                    string protectedJson = "{\"schemaVersion\":3,\"scopes\":{}}";
                    File.WriteAllText(f.SidecarPath, protectedJson);

                    KnightIdentityLoadSeed.Flush(); // Flush #1：写入被拒（未知版本）
                    Check.Equal(1, KnightIdentityLoadSeed.PendingCount, "batch kept while the file is protected");
                    Check.Equal(protectedJson, File.ReadAllText(f.SidecarPath), "unknown schema file untouched");

                    File.Delete(f.SidecarPath); // 外部修好：现在可以写
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "retry respects the 5s backoff");

                    System.Threading.Thread.Sleep(5200);
                    KnightIdentityLoadSeed.Flush();
                    Check.True(f.SidecarExists, "retry after the backoff writes the seed");
                    Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "persisted batch recycled after retry");
                }
            });

            Case.Run("a_corrupt_sidecar_is_never_overwritten", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(21);
                    for (int i = 0; i < 2; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(37000 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }

                    RunSeededLoad(island, units); // sidecar Missing：真实接线开始种子批次
                    Prime(units, LegacyOf(units, i => 1));

                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f.SidecarPath));
                    string corruptJson = "{ this is not the archive schema";
                    File.WriteAllText(f.SidecarPath, corruptJson);

                    KnightIdentityLoadSeed.Flush();
                    Check.Equal(corruptJson, File.ReadAllText(f.SidecarPath), "corrupt file untouched");
                    Check.Equal(1, KnightIdentityLoadSeed.PendingCount, "batch kept for a later pass");
                }
            });
        }

        private static void ExistingHistoryIsPreserved()
        {
            Case.Run("an_unmatched_original_resave_preserves_history_without_seeding", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(22);
                    for (int i = 0; i < 2; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(38000 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }

                    // 旧档：一个未归属的 legacy scope（raw hash 指向另一份 native json），属于某个曾经的世代
                    string legacyScope = new string('a', KnightIdentityFingerprint.HexLength);
                    string oldHash = KnightIdentityFingerprint.Sha256("an older native island json", legacyScope);
                    Guid oldGuid = Guid.NewGuid();
                    List<KnightIdentitySnapshotEntry> oldEntries = new List<KnightIdentitySnapshotEntry>
                    {
                        new KnightIdentitySnapshotEntry("old-unique-id", new KnightIdentityReceipt(oldGuid, 2)),
                    };
                    Check.True(KnightIdentitySnapshot.TryCreate(KnightIdentityFingerprint.KindLegacy, oldHash, DateTimeOffset.UtcNow, oldEntries, out KnightIdentitySnapshot oldSnapshot, out string error),
                        "old snapshot built: " + error);

                    KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
                    Check.Equal(KnightIdentityArchive.MutationStatus.Applied, archive.RecordSnapshot(legacyScope, oldSnapshot), "old snapshot recorded");
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f.SidecarPath));
                    Check.True(KnightIdentityArchiveStore.Save(f.SidecarPath, archive).Ok, "old snapshot written");
                    byte[] before = File.ReadAllBytes(f.SidecarPath);

                    // 原版重存后失配（uniqueID 全变）：仍有未归属历史 → unresolved —— 不种、不覆盖、不迁移
                    RunSeededLoad(island, units);
                    Check.Equal(0, KnightIdentityLoadSeed.BatchCount, "unclaimed legacy history blocks the seed");
                    Check.True(Logged("load-unresolved:legacy-pending"), "reason logged | " + Dump());
                    IntegrityPass(units, LegacyOf(units, i => 4));
                    KnightIdentityLoadSeed.Flush();
                    Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "sidecar preserved byte-for-byte");

                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(loaded.Archive.TryRestore(legacyScope, oldHash, "old-unique-id", out KnightIdentityReceipt preserved), "old snapshot preserved");
                    Check.Same(oldGuid, preserved.Id, "old guid untouched");
                    Check.Equal(2, preserved.Style, "old style untouched");
                }
            });
        }

        private static void NewRecruitsNeverJoinTheSeed()
        {
            Case.Run("later_recruits_never_join_the_original_source_table", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(23);
                    for (int i = 0; i < 3; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(39000 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }
                    string originalJson = island.Json();

                    RunSeededLoad(island, units);
                    IntegrityPass(units, LegacyOf(units, i => 1));
                    Check.True(f.SidecarExists, "seed written");

                    // 后续新招募：正常桥接（不进这张原始来源表）
                    KnightUnit recruit = NativeSim.NewKnight(39500);
                    KnightIdentityRuntime.MarkPromoted(recruit.Knight);
                    Check.True(KnightIdentityRuntime.TryResolve(recruit.Knight, -1, 7u, Styles, out _), "recruit resolves");

                    IntegrityPass(units, LegacyOf(units, i => 1));
                    KnightIdentityLoadSeed.Flush();

                    KnightIdentityArchiveStore.LoadResult seededLoad = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(seededLoad.Archive.TryGetContext(Sidecar.ContextKey(island.land), out KnightIdentityContext context), "stable context registered");
                    string scopeKey = context.Active;
                    string seedHash = KnightIdentityFingerprint.Normalized(originalJson, scopeKey);
                    Check.True(seededLoad.Archive.TryGetSnapshot(scopeKey, seedHash, out KnightIdentitySnapshot seeded),
                        "original source table still there");
                    Check.Equal(3, seeded.Count, "no recruit added to the original source table");

                    // 正常原生 Save 仍旧桥：新招募进入 Save 写出的另一份快照
                    List<KnightUnit> saved = new List<KnightUnit>(units) { recruit };
                    IslandSaveData saveIsland = NewIsland(23);
                    for (int i = 0; i < saved.Count; i++) saveIsland.objects.Add(saved[i].ToRecord());
                    string saveJson = saveIsland.Json();
                    NativeSim.RunSave(saveIsland, 0, saveIsland.land, 0, saved);
                    Check.True(Logged("save scope="), "save bridge still writes | " + Dump());

                    KnightIdentityArchiveStore.LoadResult after = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(after.Archive.TryGetSnapshot(scopeKey, seedHash, out KnightIdentitySnapshot untouched), "seed untouched by the save");
                    Check.Equal(3, untouched.Count, "seed still carries exactly its own knights");
                    string saveHash = KnightIdentityFingerprint.Normalized(saveJson, scopeKey);
                    Check.True(after.Archive.TryGetSnapshot(scopeKey, saveHash, out KnightIdentitySnapshot savedSnapshot), "save snapshot stored separately");
                    Check.Equal(4, savedSnapshot.Count, "save snapshot carries the recruit");
                }
            });
        }

        private static void ClearKeepsMemoryAndDiskUntouched()
        {
            Case.Run("clear_drops_memory_and_writes_nothing", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = new List<KnightUnit>();
                    IslandSaveData island = NewIsland(24);
                    for (int i = 0; i < 2; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(40000 + i);
                        units.Add(unit);
                        island.objects.Add(unit.ToRecord());
                    }

                    SeededLoad run = RunSeededLoad(island, units);
                    Check.Equal(1, KnightIdentityLoadSeed.PendingCount, "pending before clear");

                    KnightIdentityLoadSeed.Clear();
                    Check.Equal(0, KnightIdentityLoadSeed.BatchCount, "batches cleared");
                    Check.Equal(0, KnightIdentityLoadSeed.PendingCount, "nothing pending after clear");

                    KnightIdentityLoadSeed.Complete(run.Scope, true); // 悬空 Complete 不得复活批次
                    IntegrityPass(units, LegacyOf(units, i => 1));
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "cleared memory never writes");
                }
            });
        }

        private static void BatchCapacityIsBounded()
        {
            Case.Run("at_most_four_batches_and_the_fifth_is_rejected", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightIdentityLoadBridge.LoadScope> scopes = new List<KnightIdentityLoadBridge.LoadScope>();
                    List<KnightUnit> units = new List<KnightUnit>();
                    List<IslandSaveData> islands = new List<IslandSaveData>();
                    List<IslandSaveData.ObjectData> records = new List<IslandSaveData.ObjectData>();

                    for (int i = 0; i < 5; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(41000 + i);
                        units.Add(unit);
                        IslandSaveData island = NewIsland(30 + i);
                        IslandSaveData.ObjectData record = unit.ToRecord();
                        island.objects.Add(record);
                        islands.Add(island);
                        records.Add(record);
                    }

                    for (int i = 0; i < 5; i++)
                    {
                        KnightIdentityLoadBridge.LoadScope scope = KnightIdentityLoadBridge.Begin(islands[i]);
                        scopes.Add(scope);
                        KnightIdentityLoadSeed.Begin(scope);
                        KnightIdentityRuntime.OnEnable(units[i].Knight);
                        KnightIdentityLoadSeed.Capture(scope, records[i], units[i].Persistent);
                    }

                    Check.Equal(KnightIdentityLoadSeed.MaxBatches, KnightIdentityLoadSeed.BatchCount, "the fifth batch is rejected");
                    Check.True(Logged("seed-batch-cap"), "capacity reason logged | " + Dump());

                    for (int i = 4; i >= 0; i--) // 真实嵌套按 LIFO 结束（End 自带 Complete）
                    {
                        KnightIdentityLoadBridge.End(null, scopes[i], true);
                    }
                    Check.Equal(KnightIdentityLoadSeed.MaxBatches, KnightIdentityLoadSeed.PendingCount, "only four batches pending");

                    IntegrityPass(units, LegacyOf(units, i => i));
                    Check.True(f.SidecarExists, "four batches written | " + KnightIdentityLoadSeed.DescribeForTests() + " | " + Dump());

                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    for (int i = 0; i < 4; i++)
                    {
                        string key = Sidecar.ActiveEpoch(f.SidecarPath, islands[i].land);
                        Check.True(key != null, "context " + i + " registered");
                        Check.True(loaded.Archive.TryGetSnapshot(key, KnightIdentityFingerprint.Normalized(islands[i].Json(), key), out _), "scope " + i + " stored");
                    }
                    Check.False(loaded.Archive.TryGetContext(Sidecar.ContextKey(islands[4].land), out _),
                        "the rejected fifth context is never written");
                }
            });
        }

        // ------------------------------------------------------------------ 驱动（模拟 Operator 接线）

        private sealed class SeededLoad
        {
            internal KnightIdentityLoadBridge.LoadScope Scope;
            internal bool Succeeded;
        }

        /// <summary>
        /// 一次原生 TryPop 周期的真实接线（canonical LoadBridge 已把种子接进 Begin/HandleTryCreateOrFind/End）：
        /// LoadBridge.Begin（内部种子 Begin）→ 逐条（OnEnable → TryCreateOrFind 后缀，内部种子 Capture）→
        /// 原生排序/Decay（含 objects = null）→ LoadBridge.End（内部种子 Complete，带 native 成功位）。
        /// </summary>
        private static SeededLoad RunSeededLoad(
            IslandSaveData island,
            IList<KnightUnit> units,
            Action<SeededLoad, int, KnightUnit, IslandSaveData.ObjectData> perUnit = null,
            bool nullOutList = true,
            bool nativeResult = true)
        {
            SeededLoad run = new SeededLoad();
            IslandSaveData.ObjectData[] records = new IslandSaveData.ObjectData[units.Count];
            for (int i = 0; i < units.Count; i++) records[i] = island.objects[i];

            run.Scope = KnightIdentityLoadBridge.Begin(island);

            Exception pending = null;
            try
            {
                for (int i = 0; i < units.Count; i++)
                {
                    KnightUnit unit = units[i];
                    IslandSaveData.ObjectData record = records[i];
                    KnightIdentityRuntime.OnEnable(unit.Knight);
                    if (perUnit != null)
                    {
                        perUnit(run, i, unit, record);
                        continue;
                    }
                    KnightIdentityLoadBridge.HandleTryCreateOrFind(record, unit.Persistent);
                }

                if (nullOutList)
                {
                    for (int i = 0; i < island.objects.Count; i++) island.objects[i] = null;
                    island.objects = null; // 原生最终清空：此后 helper 不得再依赖原 list
                }
            }
            catch (Exception e)
            {
                pending = e;
            }
            finally
            {
                KnightIdentityLoadBridge.End(pending, run.Scope, nativeResult);
            }

            run.Succeeded = pending == null && nativeResult;
            return run;
        }

        /// <summary>既有 5s IntegrityPass 的 PrimeExisting + ApplyKnightStyle 段（不带 Flush）。</summary>
        private static void Prime(IList<KnightUnit> units, IDictionary<Knight, int> legacy)
        {
            Knight[] knights = new Knight[units.Count];
            for (int i = 0; i < units.Count; i++) knights[i] = units[i].Knight;
            KnightIdentityRuntime.PrimeExisting(knights, k => legacy != null && legacy.TryGetValue(k, out int style) ? style : -1);
        }

        /// <summary>既有 5s IntegrityPass：PrimeExisting + ApplyKnightStyle 循环，然后（Operator）种子 Flush。</summary>
        private static void IntegrityPass(IList<KnightUnit> units, IDictionary<Knight, int> legacy)
        {
            Prime(units, legacy);
            KnightIdentityLoadSeed.Flush();
        }

        /// <summary>只开 scope 并立即按 native 结果结束（用于冻结阶段就被拒的用例）；Complete 由 LoadBridge.End 触发。</summary>
        private static void OpenAndComplete(IslandSaveData island, bool succeeded)
        {
            KnightIdentityLoadBridge.LoadScope scope = KnightIdentityLoadBridge.Begin(island);
            KnightIdentityLoadBridge.End(null, scope, succeeded);
        }

        // ------------------------------------------------------------------ 构造

        private static IslandSaveData NewIsland(int land)
        {
            IslandSaveData island = new IslandSaveData { land = land };
            island.objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>();
            return island;
        }

        private static IslandSaveData.ObjectData Record(string uniqueId, long pointer = -1, string name = "Knight(Clone)")
        {
            IslandSaveData.ObjectData record = new IslandSaveData.ObjectData
            {
                name = name,
                uniqueID = uniqueId,
            };
            if (pointer >= 0) record.SetPointerForTests(pointer);
            record.componentData2.Add(new IslandSaveData.ObjectData.ComponentData
            {
                name = "Knight",
                type = "KnightData",
                data = "{\"rank\":1}",
            });
            return record;
        }

        private static string[] IdsOf(IslandSaveData island)
        {
            string[] ids = new string[island.objects.Count];
            for (int i = 0; i < island.objects.Count; i++) ids[i] = island.objects[i].uniqueID;
            return ids;
        }

        private static Dictionary<Knight, int> LegacyOf(IList<KnightUnit> units, Func<int, int> style, int count = -1)
        {
            Dictionary<Knight, int> legacy = new Dictionary<Knight, int>();
            int limit = count < 0 ? units.Count : Math.Min(count, units.Count);
            for (int i = 0; i < limit; i++) legacy[units[i].Knight] = style(i);
            return legacy;
        }

        private static string Sha256File(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
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

        private static string Dump()
        {
            LogSource log = KingdomEnhancedPlugin.Instance != null ? KingdomEnhancedPlugin.Instance.LogSource : null;
            return log == null ? "<no log>" : string.Join(" | ", log.Messages);
        }
    }
}
