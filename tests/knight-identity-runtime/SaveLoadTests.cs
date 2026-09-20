using System;
using System.Collections.Generic;
using System.IO;
using KingdomEnhancedMod;

namespace KnightIdentityRuntimeTests
{
    /// <summary>Save/Load 桥用例：精确快照、Guid 保留、nested 上下文、失配降级、schema 只读、目标岛、life 校验。</summary>
    internal static class SaveLoadTests
    {
        internal static void Run()
        {
            SaveWritesExactSnapshotAndLoadRestoresGuid();
            RuntimeStartDateAndClocksDoNotChangeIdentity();
            LegacyExactMatchClaimsTheContext();
            MissingSidecarIsLogged();
            SaveCaptureNeedsGetIdWindow();
            NestedScopesRestorePreviousContext();
            MismatchKeepsArchiveWithoutAllocating();
            UnknownSchemaIsNeverOverwritten();
            SaveUsesActualTargetIsland();
            OwnerLifeChangeSkipsSnapshotEntry();
            LoadScopeProtectsReceiptUntilScopeEnds();
            RetryFailureIsLoggedOnce();
        }

        private static void SaveWritesExactSnapshotAndLoadRestoresGuid()
        {
            Case.Run("save_writes_exact_snapshot_and_load_restores_same_guid", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit first = NativeSim.NewKnight(11001);
                    KnightUnit second = NativeSim.NewKnight(11002);
                    KnightIdentityReceipt firstReceipt = ResolveHost(first, 2);
                    KnightIdentityReceipt secondReceipt = ResolveHost(second, 4);

                    IslandSaveData island = new IslandSaveData { land = 3 };
                    NativeSim.RunSave(island, 0, 3, 0, new List<KnightUnit> { first, second });

                    // 原生字段未被本模块修改
                    Check.Equal(2, island.objects.Count, "native object count unchanged");
                    Check.Equal(IslandSaveData.GetID(first.Persistent), island.objects[0].uniqueID, "uniqueID untouched");
                    Check.Equal("{\"rank\":1}", island.objects[0].componentData2[0].data, "component payload untouched");
                    Check.Equal(1, island.objects[0].componentData2.Count, "no component added");
                    Check.True(IslandSaveData.CurrentlySavingIsland == null, "native static restored");

                    // 保存后：稳定上下文登记 + 该 epoch 下的 kind2 时钟无关快照
                    Check.True(f.SidecarExists, "host wrote the sidecar");
                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.Equal(KnightIdentityArchiveStatus.Valid, loaded.Status, "sidecar readable");
                    Check.True(loaded.Archive.TryGetContext(Sidecar.ContextKey(island.land), out KnightIdentityContext context), "stable context registered");
                    string epoch = context.Active;
                    string expectedHash = KnightIdentityFingerprint.Normalized(NativeSim.CloneIsland(island).Json(), epoch);
                    Check.True(loaded.Archive.TryRestore(epoch, expectedHash, island.objects[0].uniqueID, out KnightIdentityReceipt storedFirst),
                        "first knight found in the exact snapshot");
                    Check.Same(firstReceipt.Id, storedFirst.Id, "Guid preserved in the archive");
                    Check.True(loaded.Archive.TryRestore(epoch, expectedHash, island.objects[1].uniqueID, out KnightIdentityReceipt storedSecond),
                        "second knight found");
                    Check.Same(secondReceipt.Id, storedSecond.Id, "second Guid preserved");

                    // 同一份岛 JSON 再读档：同一 scope+hash 命中，收据绑回新对象
                    IslandSaveData reloaded = NativeSim.CloneIsland(island);
                    KnightUnit firstAgain = NativeSim.NewKnight(12001);
                    KnightUnit secondAgain = NativeSim.NewKnight(12002);
                    NativeSim.RunLoad(reloaded, (index, uniqueId) => index == 0 ? firstAgain : secondAgain);

                    Check.True(KnightIdentityRuntime.TryGetReceipt(firstAgain.Knight, out KnightIdentityReceipt restoredFirst), "first restored");
                    Check.Same(firstReceipt.Id, restoredFirst.Id, "load keeps the same Guid");
                    Check.Equal(2, restoredFirst.Style, "load keeps the same style");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(secondAgain.Knight, out KnightIdentityReceipt restoredSecond), "second restored");
                    Check.Same(secondReceipt.Id, restoredSecond.Id, "second Guid kept across load");
                    Check.Equal(IslandSaveData.GetID(first.Persistent), reloaded.objects[0].uniqueID, "load did not rewrite uniqueID");

                    // 事件级简洁回执（供实机验收）
                    Check.True(Logged("save scope="), "save receipt logged");
                    Check.True(Logged("entries=2"), "save receipt carries the entry count");
                    Check.True(Logged("load-match scope="), "load match logged");
                    Check.True(Logged("kind=exact"), "load match records the clock-free fingerprint kind");
                }
            });
        }

        private static void RuntimeStartDateAndClocksDoNotChangeIdentity()
        {
            Case.Run("runtime_start_date_and_clock_drift_keep_the_same_identity", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit unit = NativeSim.NewKnight(21001);
                    KnightIdentityReceipt receipt = ResolveHost(unit, 2);

                    IslandSaveData island = new IslandSaveData { land = 2 };
                    island.playTimeDays = 10.5;
                    island.lastPlayedTimeDays = 9.25;
                    island.islandTimePlayed = 12345;
                    NativeSim.RunSave(island, 0, 2, 0, new List<KnightUnit> { unit });

                    // 下一次读取：realStartDateTime 重建（不同 ticks）+ 三个活时钟前进，对象数据不变
                    IslandSaveData reloaded = NativeSim.CloneIsland(island);
                    reloaded.realStartDateTime = new DateTime(638111111111111111L, DateTimeKind.Utc);
                    reloaded.playTimeDays = 11.75;
                    reloaded.lastPlayedTimeDays = 10.5;
                    reloaded.islandTimePlayed = 12990;
                    KnightUnit again = NativeSim.NewKnight(21002);
                    NativeSim.RunLoad(reloaded, (index, uniqueId) => again);

                    Check.True(KnightIdentityRuntime.TryGetReceipt(again.Knight, out KnightIdentityReceipt restored), "clock drift still restores");
                    Check.Same(receipt.Id, restored.Id, "same Guid across runtime start dates");
                    Check.Equal(2, restored.Style, "same style across runtime start dates");
                    Check.True(Logged("load-match scope="), "restore is an exact match");

                    // 对象数据变化：同一上下文但快照对不上 → 不恢复、不种
                    IslandSaveData changed = NativeSim.CloneIsland(island);
                    changed.objects[0].componentData2[0].data = "{\"rank\":7}";
                    KnightUnit third = NativeSim.NewKnight(21003);
                    NativeSim.RunLoad(changed, (index, uniqueId) => third);
                    Check.False(KnightIdentityRuntime.TryGetReceipt(third.Knight, out _), "object change is never restored");
                    Check.True(Logged("load-unresolved:known-mismatch"), "object change is unresolved");
                    Check.Equal(0, KnightIdentityLoadSeed.BatchCount, "no seed batch for the changed snapshot");
                }
            });
        }

        private static void LegacyExactMatchClaimsTheContext()
        {
            Case.Run("legacy_exact_match_restores_receipts_and_persists_the_context_mapping", () =>
            {
                using (Fixture f = new Fixture())
                {
                    IslandSaveData island = new IslandSaveData { land = 12 };
                    island.objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>();
                    KnightUnit original = NativeSim.NewKnight(22001);
                    island.objects.Add(original.ToRecord());
                    string json = island.Json();

                    // v1 世代：不透明 legacy scope（内嵌已消失的运行时 ticks）+ kind1 全量 hash
                    string legacyScope = new string('a', KnightIdentityFingerprint.HexLength);
                    string legacyHash = KnightIdentityFingerprint.Sha256(json, legacyScope);
                    Guid oldGuid = Guid.NewGuid();
                    Check.True(KnightIdentitySnapshot.TryCreate(
                            KnightIdentityFingerprint.KindLegacy, legacyHash, DateTimeOffset.UtcNow,
                            new[] { new KnightIdentitySnapshotEntry(island.objects[0].uniqueID, new KnightIdentityReceipt(oldGuid, 3)) },
                            out KnightIdentitySnapshot oldSnapshot, out string error),
                        "legacy snapshot built: " + error);
                    KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
                    Check.Equal(KnightIdentityArchive.MutationStatus.Applied, archive.RecordSnapshot(legacyScope, oldSnapshot), "legacy snapshot recorded");
                    Directory.CreateDirectory(Path.GetDirectoryName(f.SidecarPath));
                    Check.True(KnightIdentityArchiveStore.Save(f.SidecarPath, archive).Ok, "legacy sidecar written");

                    KnightUnit reloaded = NativeSim.NewKnight(22002);
                    NativeSim.RunLoad(island, (index, uniqueId) => reloaded);

                    Check.True(KnightIdentityRuntime.TryGetReceipt(reloaded.Knight, out KnightIdentityReceipt restored), "legacy receipt restored");
                    Check.Same(oldGuid, restored.Id, "legacy Guid preserved");
                    Check.Equal(3, restored.Style, "legacy style preserved");
                    Check.True(Logged("load-match scope="), "legacy match logged as an exact match");
                    Check.True(Logged("kind=legacy"), "legacy match records the legacy fingerprint kind");
                    Check.True(Logged("claim scope="), "the context mapping is persisted once");

                    KnightIdentityArchiveStore.LoadResult after = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(after.Archive.TryGetContext(Sidecar.ContextKey(island.land), out KnightIdentityContext context), "stable context persisted");
                    Check.Equal(legacyScope, context.Active, "the legacy scope became the context epoch");
                    Check.True(after.Archive.TryRestore(legacyScope, legacyHash, island.objects[0].uniqueID, out KnightIdentityReceipt kept), "legacy record preserved on disk");
                    Check.Same(oldGuid, kept.Id, "legacy Guid on disk unchanged");
                }
            });
        }

        private static void MissingSidecarIsLogged()
        {
            Case.Run("missing_sidecar_load_is_logged_as_a_reseed", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit unit = NativeSim.NewKnight(24001);
                    IslandSaveData island = new IslandSaveData { land = 10, objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>() };
                    island.objects.Add(unit.ToRecord());
                    NativeSim.RunLoad(island, (index, uniqueId) => unit);

                    Check.True(Logged("load-sidecar-missing"), "main+backup missing is logged once");
                    Check.True(Logged("load-fresh scope="), "the fresh path still proceeds");
                    Check.False(f.SidecarExists, "a plain load never writes the sidecar");
                }
            });
        }

        private static void SaveCaptureNeedsGetIdWindow()
        {
            Case.Run("save_capture_only_inside_getid_window_with_matching_save_args", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit unit = NativeSim.NewKnight(17501);
                    ResolveHost(unit, 0);

                    // 1) prefix 阶段岛仍为 null：没有任何 GetID 就不该写
                    KnightIdentitySaveBridge.SaveCapture empty = KnightIdentitySaveBridge.BeginCapture(0, 3, 0);
                    KnightIdentitySaveBridge.ApplyCapture(empty);
                    KnightIdentitySaveBridge.EndCapture(null, empty);
                    Check.False(f.SidecarExists, "no GetID => no island => no write");

                    // 2) 正在保存的是别的岛（land 不匹配本次 Save 参数）：不认领
                    IslandSaveData foreign = new IslandSaveData { land = 8 };
                    IslandSaveData.isSavingGame = true;
                    IslandSaveData.CurrentlySavingIsland = foreign;
                    IslandSaveData._currentlySavingIsland = foreign;
                    KnightUnit other = NativeSim.NewKnight(17502);
                    ResolveHost(other, 1);
                    IslandSaveData.ObjectData foreignRecord = other.ToRecord();
                    KnightIdentitySaveBridge.SaveCapture mismatch = KnightIdentitySaveBridge.BeginCapture(0, 3, 0);
                    KnightIdentitySaveBridge.HandleGetId(other.Persistent, foreignRecord.uniqueID);
                    KnightIdentitySaveBridge.EndCapture(null, mismatch);
                    KnightIdentitySaveBridge.ApplyCapture(mismatch);
                    Check.False(f.SidecarExists, "foreign island is never captured");

                    // 3) 实际 Save 的岛出现（body 内）→ 同一 scope 捕获并写入
                    IslandSaveData target = new IslandSaveData { land = 3 };
                    IslandSaveData.CurrentlySavingIsland = target;
                    IslandSaveData._currentlySavingIsland = target;
                    KnightIdentitySaveBridge.SaveCapture real = KnightIdentitySaveBridge.BeginCapture(0, 3, 0);
                    IslandSaveData.ObjectData record = unit.ToRecord();
                    target.objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>();
                    target.objects.Add(record);
                    KnightIdentitySaveBridge.HandleGetId(unit.Persistent, record.uniqueID);
                    KnightIdentitySaveBridge.ApplyCapture(real);
                    KnightIdentitySaveBridge.EndCapture(null, real);
                    IslandSaveData.isSavingGame = false;
                    IslandSaveData.CurrentlySavingIsland = null;
                    IslandSaveData._currentlySavingIsland = null;
                    Check.True(f.SidecarExists, "island captured during GetID is written | log=" + Dump());
                    Check.True(Logged("save scope="), "save receipt logged");
                }
            });
        }

        private static void NestedScopesRestorePreviousContext()
        {
            Case.Run("nested_save_and_load_scopes_restore_previous_context", () =>
            {
                using (Fixture f = new Fixture())
                {
                    IslandSaveData outerIsland = new IslandSaveData { land = 1 };
                    IslandSaveData innerIsland = new IslandSaveData { land = 2 };

                    KnightIdentitySaveBridge.SaveCapture outer = KnightIdentitySaveBridge.BeginCapture(0, 1, 0);
                    KnightIdentitySaveBridge.SaveCapture inner = KnightIdentitySaveBridge.BeginCapture(0, 2, 0);
                    Check.True(KnightIdentitySaveBridge.HasActiveSaveCapture, "capture active");
                    KnightIdentitySaveBridge.EndCapture(null, inner);
                    Check.True(KnightIdentitySaveBridge.HasActiveSaveCapture, "outer capture survives the nested one");
                    Check.True(KnightIdentitySaveBridge.EndCapture(new InvalidOperationException("boom"), outer) is InvalidOperationException,
                        "finalizer returns the original exception");
                    Check.False(KnightIdentitySaveBridge.HasActiveSaveCapture, "no capture left");

                    KnightIdentityLoadBridge.LoadScope outerScope = KnightIdentityLoadBridge.Begin(outerIsland);
                    KnightIdentityLoadBridge.LoadScope innerScope = KnightIdentityLoadBridge.Begin(innerIsland);
                    Check.True(ReferenceEquals(KnightIdentityLoadBridge.Current, innerScope), "inner load scope current");
                    Check.True(KnightIdentityLoadBridge.End(null, innerScope) == null, "finalizer passes no exception through");
                    Check.True(ReferenceEquals(KnightIdentityLoadBridge.Current, outerScope), "outer load scope restored");
                    KnightIdentityLoadBridge.End(null, outerScope);
                    Check.True(KnightIdentityLoadBridge.Current == null, "no load scope left");
                }
            });
        }

        private static void MismatchKeepsArchiveWithoutAllocating()
        {
            Case.Run("hash_mismatch_stays_unresolved_after_load_prime_integrity_and_save", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit original = NativeSim.NewKnight(13001);
                    KnightIdentityReceipt originalReceipt = ResolveHost(original, 1);

                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, new List<KnightUnit> { original });
                    KnightIdentityArchiveStore.LoadResult saved = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(saved.IsUsable, "save recorded the stable context");
                    Check.True(saved.Archive.TryGetContext(Sidecar.ContextKey(4), out KnightIdentityContext context), "context registered");
                    string epoch = context.Active;
                    string originalHash = KnightIdentityFingerprint.Normalized(island.Json(), epoch);
                    byte[] before = File.ReadAllBytes(f.SidecarPath);

                    // 外部修改（原版重存/别的模块改了载荷）：同一上下文，快照对不上
                    IslandSaveData modified = NativeSim.CloneIsland(island);
                    modified.objects[0].componentData2[0].data = "{\"rank\":9}";
                    KnightUnit reloadedUnit = NativeSim.NewKnight(14001);
                    NativeSim.RunLoad(modified, (index, uniqueId) => reloadedUnit);

                    Check.False(KnightIdentityRuntime.TryGetReceipt(reloadedUnit.Knight, out _), "mismatch must not restore an identity");
                    Check.True(Logged("load-unresolved:known-mismatch"), "known-history mismatch is unresolved and logged");

                    // 已知历史对不上：绝不自动重种，sidecar 逐字节保留
                    Check.Equal(0, KnightIdentityLoadSeed.BatchCount, "no seed batch for an unresolved known context");
                    Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "sidecar preserved byte-for-byte");

                    int callbacks = 0;
                    for (int i = 0; i < 3; i++)
                    {
                        KnightIdentityRuntime.PrimeExisting(new[] { reloadedUnit.Knight }, k => { callbacks++; return 4; });
                        Check.False(KnightIdentityRuntime.TryResolve(reloadedUnit.Knight, -1, (uint)i, Styles.All, out _), "no migration after load ends");
                        Check.False(KnightIdentityRuntime.TryResolve(reloadedUnit.Knight, 2, 99u, Styles.All, out _), "existing appearance does not mint identity");
                        KnightIdentityRuntime.Poll();
                        KnightIdentityLoadSeed.Flush();
                    }
                    Check.Equal(0, callbacks, "integrity does not even calculate a legacy style");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(reloadedUnit.Knight, out _), "no new GUID/style receipt");
                    NativeSim.RunSave(modified, 0, 4, 0, new List<KnightUnit> { original, reloadedUnit });
                    Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "save after unresolved load preserves disk bytes");

                    // 旧记录保留：磁盘上仍能找到原来那一代
                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(loaded.Archive.TryRestore(epoch, originalHash, island.objects[0].uniqueID, out KnightIdentityReceipt kept),
                        "old snapshot survives the mismatch");
                    Check.Same(originalReceipt.Id, kept.Id, "old Guid still on disk");
                }
            });
        }

        private static void UnknownSchemaIsNeverOverwritten()
        {
            Case.Run("unknown_schema_sidecar_is_read_only", () =>
            {
                using (Fixture f = new Fixture())
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(f.SidecarPath));
                    byte[] foreign = System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":3,\"scopes\":[]}");
                    File.WriteAllBytes(f.SidecarPath, foreign);

                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.Equal(KnightIdentityArchiveStatus.UnsupportedVersion, loaded.Status, "unknown version detected");

                    KnightUnit unit = NativeSim.NewKnight(15001);
                    ResolveHost(unit, 0);
                    IslandSaveData island = new IslandSaveData { land = 6 };
                    NativeSim.RunSave(island, 0, 6, 0, new List<KnightUnit> { unit });

                    Check.Equal(foreign.Length, new FileInfo(f.SidecarPath).Length, "file length unchanged");
                    byte[] after = File.ReadAllBytes(f.SidecarPath);
                    for (int i = 0; i < foreign.Length; i++)
                    {
                        if (foreign[i] != after[i]) throw new Exception("unknown schema file was rewritten at byte " + i);
                    }
                    Check.True(Logged("sidecar-version-readonly"), "read-only degrade is logged");
                }
            });
        }

        private static void SaveUsesActualTargetIsland()
        {
            Case.Run("save_snapshot_uses_the_actual_target_island_not_current", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit target = NativeSim.NewKnight(16001);
                    KnightIdentityReceipt targetReceipt = ResolveHost(target, 3);

                    IslandSaveData decoy = new IslandSaveData { land = 9 };
                    decoy.realStartDateTime = new DateTime(600000000000000000L, DateTimeKind.Utc);
                    CampaignSaveData.current = new CampaignSaveData { CurrentIsland = decoy };

                    IslandSaveData targetIsland = new IslandSaveData { land = 5 };
                    NativeSim.RunSave(targetIsland, 0, 5, 0, new List<KnightUnit> { target });

                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(loaded.Archive.TryGetContext(Sidecar.ContextKey(5), out KnightIdentityContext context), "target island context registered");
                    string epoch = context.Active;
                    string targetHash = KnightIdentityFingerprint.Normalized(targetIsland.Json(), epoch);
                    Check.True(loaded.Archive.TryRestore(epoch, targetHash, targetIsland.objects[0].uniqueID, out KnightIdentityReceipt stored),
                        "snapshot recorded under the target island epoch");
                    Check.Same(targetReceipt.Id, stored.Id, "target identity stored");

                    Check.False(loaded.Archive.TryGetContext(Sidecar.ContextKey(9), out _), "no context for the decoy island");
                    Check.Equal(1, loaded.Archive.ContextCount, "only the target island was written");
                }
            });
        }

        private static void OwnerLifeChangeSkipsSnapshotEntry()
        {
            Case.Run("owner_life_change_before_capture_rejects_entire_snapshot", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit stable = NativeSim.NewKnight(17001);
                    KnightUnit reborn = NativeSim.NewKnight(17002);
                    ResolveHost(stable, 0);
                    ResolveHost(reborn, 1);

                    IslandSaveData island = new IslandSaveData { land = 7 };
                    KnightIdentitySaveBridge.SaveCapture capture = KnightIdentitySaveBridge.BeginCapture(0, 7, 0);
                    IslandSaveData.isSavingGame = true; // 原生主体在 GetID 之前设置
                    IslandSaveData.CurrentlySavingIsland = island;
                    IslandSaveData._currentlySavingIsland = island;
                    island.objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>();
                    foreach (KnightUnit unit in new[] { stable, reborn })
                    {
                        IslandSaveData.ObjectData record = unit.ToRecord();
                        island.objects.Add(record);
                        KnightIdentitySaveBridge.HandleGetId(unit.Persistent, record.uniqueID);
                    }
                    IslandSaveData.isSavingGame = false; // 原生 finally
                    IslandSaveData.CurrentlySavingIsland = null;
                    IslandSaveData._currentlySavingIsland = null;
                    KnightIdentityRuntime.OnEnable(reborn.Knight); // 捕获后 owner 换了 life

                    KnightIdentitySaveBridge.ApplyCapture(capture);
                    KnightIdentitySaveBridge.EndCapture(null, capture);

                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.Equal(KnightIdentityArchiveStatus.Missing, loaded.Status, "conflicting lifetime rejects the whole new snapshot");
                }
            });
        }

        private static void LoadScopeProtectsReceiptUntilScopeEnds()
        {
            Case.Run("load_binding_holds_until_a_new_life_reenable_clears_it", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit original = NativeSim.NewKnight(18001);
                    KnightIdentityReceipt receipt = ResolveHost(original, 4);
                    IslandSaveData island = new IslandSaveData { land = 8 };
                    NativeSim.RunSave(island, 0, 8, 0, new List<KnightUnit> { original });

                    IslandSaveData reloaded = NativeSim.CloneIsland(island);
                    KnightIdentityLoadBridge.LoadScope scope = KnightIdentityLoadBridge.Begin(reloaded);
                    try
                    {
                        KnightUnit unit = NativeSim.NewKnight(19001);
                        KnightIdentityRuntime.OnEnable(unit.Knight);
                        KnightIdentityLoadBridge.HandleTryCreateOrFind(reloaded.objects[0], unit.Persistent);
                        Check.True(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt bound), "bound during load");
                        Check.Same(receipt.Id, bound.Id, "same Guid restored");

                        long boundLife = KnightIdentityRuntime.GetLifetime(unit.Knight);
                        KnightIdentityRuntime.OnEnable(unit.Knight); // 池复用：绑定后才触发的新 life
                        Check.True(KnightIdentityRuntime.GetLifetime(unit.Knight) > boundLife, "life advances on re-enable");
                        Check.False(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "a new life never inherits the previous receipt");
                    }
                    finally
                    {
                        KnightIdentityLoadBridge.End(null, scope);
                    }
                }

                // 作用域结束后同一对象再次 OnEnable = 真正的新生命，收据清除
                using (Fixture f = new Fixture())
                {
                    KnightUnit unit = NativeSim.NewKnight(20001);
                    KnightIdentityReceipt receipt = ResolveHost(unit, 1);
                    long before = KnightIdentityRuntime.GetLifetime(unit.Knight);
                    KnightIdentityRuntime.OnEnable(unit.Knight);
                    Check.False(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "receipt cleared on the new life");
                    Check.True(KnightIdentityRuntime.GetLifetime(unit.Knight) > before, "life advanced");
                }
            });
        }

        private static string Dump()
        {
            LogSource log = KingdomEnhancedPlugin.Instance != null ? KingdomEnhancedPlugin.Instance.LogSource : null;
            return log == null ? "<no log>" : string.Join(" | ", log.Messages);
        }

        private static void RetryFailureIsLoggedOnce()
        {
            Case.Run("a_failed_write_retry_is_logged_once_and_keeps_the_previous_file", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit unit = NativeSim.NewKnight(25001);
                    ResolveHost(unit, 1);
                    IslandSaveData island = new IslandSaveData { land = 11 };
                    NativeSim.RunSave(island, 0, 11, 0, new List<KnightUnit> { unit });
                    island.biome++;
                    NativeSim.RunSave(island, 0, 11, 0, new List<KnightUnit> { unit });
                    Check.True(File.Exists(f.SidecarPath + ".bak"), "second save creates the backup used by the write path");

                    byte[] before = File.ReadAllBytes(f.SidecarPath);
                    using (FileStream locked = new FileStream(f.SidecarPath + ".bak", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        island.biome++;
                        NativeSim.RunSave(island, 0, 11, 0, new List<KnightUnit> { unit });
                        island.biome++;
                        NativeSim.RunSave(island, 0, 11, 0, new List<KnightUnit> { unit });
                    }

                    Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "a failed retry keeps the previous file");
                    Check.True(Logged("sidecar-save:Failed"), "the existing failed path still runs");
                    Check.Equal(1, LoggedCount("write-retry=1 failed"), "the failed retry is logged exactly once");
                }
            });
        }

        private static KnightIdentityReceipt ResolveHost(KnightUnit unit, int existingStyle)
        {
            KnightIdentityRuntime.OnEnable(unit.Knight);
            Check.True(KnightIdentityRuntime.TryResolve(unit.Knight, existingStyle, 3u, Styles.All, out int style), "resolve " + unit.Go.name);
            Check.True(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt receipt), "receipt " + unit.Go.name);
            Check.Equal(existingStyle, receipt.Style, "style " + unit.Go.name);
            return receipt;
        }

        private static bool Logged(string fragment)
        {
            return LoggedCount(fragment) > 0;
        }

        private static int LoggedCount(string fragment)
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
    }
}
