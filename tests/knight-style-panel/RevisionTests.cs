using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KingdomEnhancedMod;

namespace KnightStylePanelTests
{
    /// <summary>
    /// 用户修订（已解析岛的面板重派持久化，Codex 发现 2/3）：
    /// 应用后挂修订 pending → 下一次原生 Save 写成同 hash、修订号 +1 的快照（旧记录保留）；
    /// 两次 apply 未保存只 bump 一次，自动保存继承当前修订不递增；
    /// Resolve 只在「修订号严格唯一最高」时按用户修订解除冲突（双 epoch 同 JSON 冲突 → 再基线 → 解除），
    /// 否则维持既有 conflict fail-closed；无修订号的旧档字节与行为不变，旧记录留在盘上可供手工回滚。
    /// </summary>
    internal static class RevisionTests
    {
        internal static void Run()
        {
            PanelRevisionPersistsThroughSaveAndReload();
            RepeatedAppliesBumpOnceAndAutomaticSavesInherit();
            PanelRebaselineRevisionResolvesConflictingEpochs();
            NonUniqueUserRevisionStaysFailClosed();
            PreRevisionRecordsStayReadableAndRestorable();
        }

        private static void PanelRevisionPersistsThroughSaveAndReload()
        {
            Case.Run("panel_revision_is_written_in_the_next_save_and_survives_reload", () =>
            {
                using (Fixture f = new Fixture())
                {
                    // 会话 1：普通已解析岛（2 名骑士，styles 0/1）→ 一次原生保存建立 rev0 快照
                    List<KnightUnit> units = Knights.Loaded(25000, 2);
                    Check.True(KnightIdentityRuntime.TryResolve(units[0].Knight, 0, 0u, Styles.All, out _), "session 1 knight 0 medieval");
                    Check.True(KnightIdentityRuntime.TryResolve(units[1].Knight, 1, 0u, Styles.All, out _), "session 1 knight 1 deadlands");
                    Guid[] guids = ReceiptIds(units);
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, units);
                    Check.True(f.SidecarExists, "session 1 saved the sidecar");
                    string contextKey = Sidecar.ContextKey(4);
                    string scope = Sidecar.ActiveEpoch(f.SidecarPath, 4);
                    Check.True(scope != null, "the fresh context owns an epoch");
                    string hash = KnightIdentityFingerprint.Normalized(island.Json(), scope);
                    string baselineText = File.ReadAllText(f.SidecarPath);
                    Check.True(baselineText.IndexOf("\"schemaVersion\":2", StringComparison.Ordinal) >= 0,
                        "without revisions the document keeps the schema2 shape | " + baselineText);
                    Check.False(baselineText.Contains("\"rev\""), "automatic records serialize no revision field");

                    // 会话 2：装载 → 面板把 2 名骑士都重派为 style 2（原生 JSON 不变 → 同 hash）
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData loaded = NativeSim.CloneIsland(island);
                    CampaignSaveData.current.CurrentIsland = loaded;
                    List<KnightUnit> again = new List<KnightUnit> { NativeSim.NewKnight(25000), NativeSim.NewKnight(25001) };
                    NativeSim.RunLoad(loaded, (index, uniqueIdArg) => again[index]);
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string loadKind) && loadKind == "exact",
                        "session 2 restores the exact rev0 match | " + loadKind);
                    Check.True(KnightIdentityRuntime.TryGetReceipt(again[0].Knight, out KnightIdentityReceipt before) && before.Style == 0,
                        "the rev0 style is restored before the panel runs");

                    UnitScanCache.TestKnights = Knights.ArrayOf(again);
                    KnightStylePanel.ResetForTests();
                    KnightStylePanel.Refresh();
                    Check.True(KnightStylePanel.Refreshed, "panel refresh ready on the resolved island | " + KnightStylePanel.Blocked);
                    Check.True(KnightStylePanel.HasContextBinding, "the panel sees the load binding");
                    Check.False(KnightStylePanel.ContextUnresolved, "the binding is resolved (revision-pending precondition)");
                    Check.True(KnightStylePanel.Session.Remove(0) && KnightStylePanel.Session.Remove(1), "release both rows into the pool");
                    Check.True(KnightStylePanel.Session.Add(2) && KnightStylePanel.Session.Add(2), "assign both knights to style 2");
                    Check.Equal(0, KnightStylePanel.Session.Pool, "pool emptied by the user");
                    KnightStylePanel.Apply();
                    Check.True(KnightStylePanel.Status != null && KnightStylePanel.Status.IndexOf("下一次原生保存", StringComparison.Ordinal) >= 0,
                        "the panel tells the user the write happens on the next save | " + KnightStylePanel.Status);
                    Check.True(Log.Saw("rebaseline=none revision=armed"), "apply log shows the revision pending | " + Log.Dump());
                    Check.True(KnightIdentityRuntime.PanelRevisionArmed, "revision pending armed on a resolved island");
                    Check.False(KnightIdentityRuntime.PanelRebaselineArmed, "no rebaseline pending is armed");
                    for (int i = 0; i < 2; i++)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(again[i].Knight, out KnightIdentityReceipt applied), "applied receipt " + i);
                        Check.Equal(2, applied.Style, "runtime style is 2 before the save " + i);
                        Check.Same(guids[i], applied.Id, "guid preserved by the panel reassignment " + i);
                    }

                    // 下一次原生保存：同 hash、修订号 +1 的新快照（旧 rev0 记录保留）
                    byte[] beforeBytes = File.ReadAllBytes(f.SidecarPath);
                    NativeSim.RunSave(loaded, 0, 4, 0, again);
                    Check.False(beforeBytes.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "the user revision wrote the sidecar");
                    Check.False(KnightIdentityRuntime.PanelRevisionArmed, "pending consumed on success");
                    Check.True(Log.Saw("user revision: rev=1"), "audit log with the revision number | " + Log.Dump());
                    Check.True(Log.Saw("prior records preserved"), "history explicitly preserved in the log");
                    string upgradedText = File.ReadAllText(f.SidecarPath);
                    Check.True(upgradedText.IndexOf("\"schemaVersion\":4", StringComparison.Ordinal) >= 0,
                        "a user revision upgrades the document to schema4 | " + upgradedText);
                    Check.True(upgradedText.IndexOf("\"rev\":1", StringComparison.Ordinal) >= 0, "the revision number is serialized");

                    KnightIdentityArchive stored = Sidecar.Load(f.SidecarPath);
                    Check.True(stored.TryGetSnapshots(scope, out IReadOnlyList<KnightIdentitySnapshot> snapshots), "scope history readable");
                    int rev0 = 0, rev1 = 0;
                    for (int i = 0; i < snapshots.Count; i++)
                    {
                        if (snapshots[i].Revision == 0) rev0++;
                        if (snapshots[i].Revision == 1) rev1++;
                    }
                    Check.Equal(1, rev0, "the automatic record is preserved");
                    Check.Equal(1, rev1, "exactly one user revision record");
                    string recordUniqueId = loaded.objects[0].uniqueID;
                    Check.True(stored.TryGetSnapshot(scope, hash, 0, out KnightIdentitySnapshot original), "the rev0 record stays addressable");
                    Check.True(original.TryGet(recordUniqueId, out KnightIdentityReceipt oldReceipt) && oldReceipt.Style == 0,
                        "the rev0 record still carries style 0");
                    Check.True(stored.TryGetSnapshot(scope, hash, 1, out KnightIdentitySnapshot revised), "the rev1 record exists");
                    Check.True(revised.TryGet(recordUniqueId, out KnightIdentityReceipt newReceipt) && newReceipt.Style == 2,
                        "the rev1 record carries style 2");
                    Check.Same(oldReceipt.Id, newReceipt.Id, "the revision keeps the same GUID");

                    // 会话 3：读档按修订优先命中 rev1 → 面板铸出的风格
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData reloaded = NativeSim.CloneIsland(loaded);
                    CampaignSaveData.current.CurrentIsland = reloaded;
                    List<KnightUnit> last = new List<KnightUnit> { NativeSim.NewKnight(25000), NativeSim.NewKnight(25001) };
                    NativeSim.RunLoad(reloaded, (index, uniqueId2) => last[index]);
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string resolvedKind) && resolvedKind == "revision",
                        "the revision match resolves the island | " + resolvedKind);
                    for (int i = 0; i < 2; i++)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(last[i].Knight, out KnightIdentityReceipt restored), "restored receipt " + i);
                        Check.Equal(2, restored.Style, "the user revision style survives the reload " + i);
                        Check.Same(guids[i], restored.Id, "guid survives the revision reload " + i);
                    }
                }
            });
        }

        private static void RepeatedAppliesBumpOnceAndAutomaticSavesInherit()
        {
            Case.Run("repeated_applies_before_a_save_bump_once_and_automatic_saves_inherit", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = Knights.Loaded(25100, 2);
                    Check.True(KnightIdentityRuntime.TryResolve(units[0].Knight, 0, 0u, Styles.All, out _), "knight 0 medieval");
                    Check.True(KnightIdentityRuntime.TryResolve(units[1].Knight, 1, 0u, Styles.All, out _), "knight 1 deadlands");
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, units);
                    string scope = Sidecar.ActiveEpoch(f.SidecarPath, 4);
                    string hash = KnightIdentityFingerprint.Normalized(island.Json(), scope);

                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData loaded = NativeSim.CloneIsland(island);
                    CampaignSaveData.current.CurrentIsland = loaded;
                    List<KnightUnit> again = new List<KnightUnit> { NativeSim.NewKnight(25100), NativeSim.NewKnight(25101) };
                    NativeSim.RunLoad(loaded, (index, uniqueId) => again[index]);
                    UnitScanCache.TestKnights = Knights.ArrayOf(again);
                    KnightStylePanel.ResetForTests();
                    KnightStylePanel.Refresh();
                    Check.True(KnightStylePanel.Refreshed, "panel ready | " + KnightStylePanel.Blocked);
                    Check.True(KnightStylePanel.Session.Remove(0) && KnightStylePanel.Session.Remove(1), "release both rows");
                    Check.True(KnightStylePanel.Session.Add(2) && KnightStylePanel.Session.Add(2), "both knights to style 2");

                    KnightStylePanel.Apply();
                    Check.True(KnightIdentityRuntime.PanelRevisionArmed, "first apply arms the pending");
                    KnightStylePanel.Apply();
                    Check.True(KnightIdentityRuntime.PanelRevisionArmed, "a second apply keeps the same single pending");
                    NativeSim.RunSave(loaded, 0, 4, 0, again);
                    Check.False(KnightIdentityRuntime.PanelRevisionArmed, "pending consumed by the save");
                    int[] afterFirst = RevisionCounts(f.SidecarPath, scope, hash, 3);
                    Check.Equal(1, afterFirst[0], "the automatic record stays");
                    Check.Equal(1, afterFirst[1], "two applies before one save bump exactly once");
                    Check.Equal(0, afterFirst[2], "no second bump before a save");

                    // 自动保存（内容未变）：继承本 scope 当前修订号，不抬升、不新增记录
                    NativeSim.RunSave(loaded, 0, 4, 0, again);
                    int[] afterAuto = RevisionCounts(f.SidecarPath, scope, hash, 3);
                    Check.Equal(1, afterAuto[1], "an automatic save inherits the current revision");
                    Check.Equal(0, afterAuto[2], "an automatic save never bumps the revision");
                    Check.Equal(2, afterAuto[0] + afterAuto[1] + afterAuto[2], "no extra record for the same hash");

                    // 再次应用 → 保存：修订链按盘上最大值继续 +1
                    KnightStylePanel.Refresh();
                    Check.True(KnightStylePanel.Refreshed, "panel re-refreshed | " + KnightStylePanel.Blocked);
                    Check.True(KnightStylePanel.Session.Remove(2), "release one knight from style 2");
                    Check.True(KnightStylePanel.Session.Add(4), "assign it to style 4");
                    KnightStylePanel.Apply();
                    Check.True(KnightIdentityRuntime.PanelRevisionArmed, "the next user action arms again");
                    NativeSim.RunSave(loaded, 0, 4, 0, again);
                    int[] afterSecond = RevisionCounts(f.SidecarPath, scope, hash, 3);
                    Check.Equal(1, afterSecond[1], "the rev1 record is preserved");
                    Check.Equal(1, afterSecond[2], "a later apply continues the chain at rev2");
                }
            });
        }

        private static void PanelRebaselineRevisionResolvesConflictingEpochs()
        {
            Case.Run("panel_rebaseline_revision_resolves_a_two_epoch_conflict", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = Knights.Loaded(25200, 1);
                    Check.True(KnightIdentityRuntime.TryResolve(units[0].Knight, 0, 0u, Styles.All, out _), "knight 0 medieval");
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, units);
                    string contextKey = Sidecar.ContextKey(4);
                    string scopeA = Sidecar.ActiveEpoch(f.SidecarPath, 4);
                    string json = island.Json();
                    string hashA = KnightIdentityFingerprint.Normalized(json, scopeA);
                    string uniqueId = island.objects[0].uniqueID;

                    // 手工制造第二个「未归属」epoch：同 JSON、同 uniqueID、不同收据 → 双 epoch 精确匹配且不一致
                    string scopeB = KnightIdentityArchive.NewScope();
                    string hashB = KnightIdentityFingerprint.Normalized(json, scopeB);
                    Check.True(KnightIdentitySnapshot.TryCreate(KnightIdentityFingerprint.KindNormalized, hashB, DateTimeOffset.UtcNow,
                        new[] { new KnightIdentitySnapshotEntry(uniqueId, new KnightIdentityReceipt(Guid.NewGuid(), 4)) },
                        out KnightIdentitySnapshot conflicting, out string built), "conflicting snapshot built: " + built);
                    KnightIdentityArchive archive = Sidecar.Load(f.SidecarPath);
                    Check.Equal(KnightIdentityArchive.MutationStatus.Applied, archive.RecordSnapshot(scopeB, conflicting), "conflicting epoch recorded");
                    Check.True(KnightIdentityArchiveStore.Save(f.SidecarPath, archive).Ok, "conflicting sidecar written");

                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData loaded = NativeSim.CloneIsland(island);
                    CampaignSaveData.current.CurrentIsland = loaded;
                    List<KnightUnit> again = new List<KnightUnit> { NativeSim.NewKnight(25200) };
                    NativeSim.RunLoad(loaded, (index, uniqueId2) => again[index]);
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string conflictKind) && conflictKind == "conflict",
                        "two disagreeing epochs stay a conflict before any revision | " + conflictKind);
                    Check.False(KnightIdentityRuntime.TryGetReceipt(again[0].Knight, out _), "no identity is guessed in a conflict");

                    // 面板应用 → unresolved → 设计 C pending → 下一次存档写成新 epoch 的修订号 1 快照
                    UnitScanCache.TestKnights = Knights.ArrayOf(again);
                    KnightStylePanel.ResetForTests();
                    KnightStylePanel.Refresh();
                    Check.True(KnightStylePanel.Refreshed, "panel ready on the conflicting island | " + KnightStylePanel.Blocked);
                    Check.True(KnightStylePanel.ContextUnresolved, "the panel sees the unresolved conflict");
                    Check.True(KnightStylePanel.Session.Add(2), "assign the pool knight to style 2");
                    KnightStylePanel.Apply();
                    Check.True(KnightStylePanel.Status != null && KnightStylePanel.Status.IndexOf("下一次原生保存", StringComparison.Ordinal) >= 0,
                        "the panel defers the write to the next save");
                    Check.True(KnightIdentityRuntime.PanelRebaselineArmed, "design C pending armed on the unresolved island");
                    Check.False(KnightIdentityRuntime.PanelRevisionArmed, "the revision pending is not used while unresolved");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(again[0].Knight, out KnightIdentityReceipt minted), "the panel minted the identity");
                    NativeSim.RunSave(loaded, 0, 4, 0, again);
                    Check.False(KnightIdentityRuntime.PanelRebaselineArmed, "pending consumed by the save");

                    string scopeC = Sidecar.ActiveEpoch(f.SidecarPath, 4);
                    Check.NotEqual(scopeA, scopeC, "the rebaseline created a new epoch");
                    KnightIdentityArchive after = Sidecar.Load(f.SidecarPath);
                    Check.True(after.TryGetSnapshots(scopeC, out IReadOnlyList<KnightIdentitySnapshot> cSnapshots), "the new epoch has history");
                    Check.Equal(1, cSnapshots.Count, "one baseline snapshot in the new epoch");
                    Check.Equal(1, cSnapshots[0].Revision, "the user rebaseline carries revision 1");
                    Check.True(after.TryGetSnapshot(scopeA, hashA, 0, out _), "the old epoch record is preserved");
                    Check.True(after.TryGetSnapshot(scopeB, hashB, 0, out _), "the conflicting record is preserved");

                    // 重 Resolve：修订号严格最高 → 解除冲突并恢复面板铸出的风格
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData reloaded = NativeSim.CloneIsland(loaded);
                    CampaignSaveData.current.CurrentIsland = reloaded;
                    List<KnightUnit> last = new List<KnightUnit> { NativeSim.NewKnight(25200) };
                    NativeSim.RunLoad(reloaded, (index, uniqueId2) => last[index]);
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string resolvedKind) && resolvedKind == "revision",
                        "the revision resolves the conflict | " + resolvedKind);
                    Check.True(KnightIdentityRuntime.TryGetReceipt(last[0].Knight, out KnightIdentityReceipt restored), "the applied identity restores");
                    Check.Equal(2, restored.Style, "the user-applied style wins");
                    Check.Same(minted.Id, restored.Id, "the applied GUID survives the rebaseline reload");
                    Check.True(KnightIdentityRuntime.CanFlushSeed, "the context is writable again");
                }
            });
        }

        private static void NonUniqueUserRevisionStaysFailClosed()
        {
            Case.Run("a_non_unique_highest_revision_stays_a_fail_closed_conflict", () =>
            {
                using (Fixture f = new Fixture())
                {
                    List<KnightUnit> units = Knights.Loaded(25300, 1);
                    Check.True(KnightIdentityRuntime.TryResolve(units[0].Knight, 0, 0u, Styles.All, out _), "knight 0 medieval");
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, units);
                    string contextKey = Sidecar.ContextKey(4);
                    string scopeA = Sidecar.ActiveEpoch(f.SidecarPath, 4);
                    string json = island.Json();
                    string hashA = KnightIdentityFingerprint.Normalized(json, scopeA);
                    string uniqueId = island.objects[0].uniqueID;

                    // 两个 scope 各有修订号 1、条目不一致 → 最高修订号不唯一 → 维持 conflict
                    KnightIdentityArchive archive = Sidecar.Load(f.SidecarPath);
                    Check.True(KnightIdentitySnapshot.TryCreate(KnightIdentityFingerprint.KindNormalized, hashA, DateTimeOffset.UtcNow,
                        new[] { new KnightIdentitySnapshotEntry(uniqueId, new KnightIdentityReceipt(Guid.NewGuid(), 2)) },
                        1, out KnightIdentitySnapshot revisedA, out string errorA), "rev1 record built: " + errorA);
                    Check.Equal(KnightIdentityArchive.MutationStatus.Applied, archive.RecordSnapshot(scopeA, revisedA), "rev1 record recorded in epoch A");
                    string scopeB = KnightIdentityArchive.NewScope();
                    string hashB = KnightIdentityFingerprint.Normalized(json, scopeB);
                    Check.True(KnightIdentitySnapshot.TryCreate(KnightIdentityFingerprint.KindNormalized, hashB, DateTimeOffset.UtcNow,
                        new[] { new KnightIdentitySnapshotEntry(uniqueId, new KnightIdentityReceipt(Guid.NewGuid(), 4)) },
                        1, out KnightIdentitySnapshot revisedB, out string errorB), "conflicting rev1 record built: " + errorB);
                    Check.Equal(KnightIdentityArchive.MutationStatus.Applied, archive.RecordSnapshot(scopeB, revisedB), "conflicting rev1 record recorded");
                    Check.True(KnightIdentityArchiveStore.Save(f.SidecarPath, archive).Ok, "tie fixture written");

                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData loaded = NativeSim.CloneIsland(island);
                    CampaignSaveData.current.CurrentIsland = loaded;
                    List<KnightUnit> again = new List<KnightUnit> { NativeSim.NewKnight(25300) };
                    NativeSim.RunLoad(loaded, (index, uniqueId2) => again[index]);
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "conflict",
                        "a non-unique highest revision stays fail-closed | " + kind);
                    Check.False(KnightIdentityRuntime.TryGetReceipt(again[0].Knight, out _), "no receipt is guessed under a tie");
                }
            });
        }

        private static void PreRevisionRecordsStayReadableAndRestorable()
        {
            Case.Run("pre_revision_records_stay_readable_and_restorable_after_a_revision", () =>
            {
                using (Fixture f = new Fixture())
                {
                    // 无修订存档：v2 字节形状，读回全部修订号 0
                    List<KnightUnit> units = Knights.Loaded(25400, 2);
                    Check.True(KnightIdentityRuntime.TryResolve(units[0].Knight, 0, 0u, Styles.All, out _), "knight 0 medieval");
                    Check.True(KnightIdentityRuntime.TryResolve(units[1].Knight, 1, 0u, Styles.All, out _), "knight 1 deadlands");
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, units);
                    string contextKey = Sidecar.ContextKey(4);
                    string scope = Sidecar.ActiveEpoch(f.SidecarPath, 4);
                    string hash = KnightIdentityFingerprint.Normalized(island.Json(), scope);
                    string v2Text = File.ReadAllText(f.SidecarPath);
                    Check.True(v2Text.IndexOf("\"schemaVersion\":2", StringComparison.Ordinal) >= 0, "no revision keeps the schema2 witness");
                    Check.False(v2Text.Contains("\"rev\""), "no rev field without a user revision");
                    Check.Equal(KnightIdentityArchiveStatus.Valid,
                        KnightIdentityArchive.Parse(Encoding.UTF8.GetBytes(v2Text), out KnightIdentityArchive reparsed, out string parseError),
                        "the v2 document parses: " + parseError);
                    Check.True(reparsed.TryGetSnapshots(scope, out IReadOnlyList<KnightIdentitySnapshot> v2Snapshots), "v2 history readable");
                    Check.Equal(0, v2Snapshots[0].Revision, "a missing rev field reads as revision 0");

                    // 会话 2：面板重派 → 保存 → rev1
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData loaded = NativeSim.CloneIsland(island);
                    CampaignSaveData.current.CurrentIsland = loaded;
                    List<KnightUnit> again = new List<KnightUnit> { NativeSim.NewKnight(25400), NativeSim.NewKnight(25401) };
                    NativeSim.RunLoad(loaded, (index, uniqueId) => again[index]);
                    UnitScanCache.TestKnights = Knights.ArrayOf(again);
                    KnightStylePanel.ResetForTests();
                    KnightStylePanel.Refresh();
                    Check.True(KnightStylePanel.Refreshed, "panel ready | " + KnightStylePanel.Blocked);
                    Check.True(KnightStylePanel.Session.Remove(0) && KnightStylePanel.Session.Remove(1), "release both rows");
                    Check.True(KnightStylePanel.Session.Add(2) && KnightStylePanel.Session.Add(2), "both knights to style 2");
                    KnightStylePanel.Apply();
                    Check.True(KnightIdentityRuntime.PanelRevisionArmed, "revision pending armed");
                    NativeSim.RunSave(loaded, 0, 4, 0, again);
                    Check.False(KnightIdentityRuntime.PanelRevisionArmed, "the revision was written");

                    // 回滚语义：旧记录仍在盘上（未被改写/淘汰）；手工只恢复旧记录即可回到旧风格
                    KnightIdentityArchive current = Sidecar.Load(f.SidecarPath);
                    Check.True(current.TryGetSnapshot(scope, hash, 0, out KnightIdentitySnapshot original),
                        "the pre-revision record survives on disk");
                    Check.True(current.TryGetSnapshot(scope, hash, 1, out _), "the revision record also stays on disk");
                    KnightIdentityArchive rolledBack = KnightIdentityArchive.CreateEmpty();
                    Check.Equal(KnightIdentityArchive.MutationStatus.Applied, rolledBack.RecordSnapshot(scope, original), "manual rollback keeps the old record");
                    Check.True(rolledBack.EnsureContext(contextKey, scope, false), "manual rollback points the context at the old epoch");
                    Check.True(KnightIdentityArchiveStore.Save(f.SidecarPath, rolledBack).Ok, "rollback archive written");

                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData reloaded = NativeSim.CloneIsland(loaded);
                    CampaignSaveData.current.CurrentIsland = reloaded;
                    List<KnightUnit> last = new List<KnightUnit> { NativeSim.NewKnight(25400), NativeSim.NewKnight(25401) };
                    NativeSim.RunLoad(reloaded, (index, uniqueId) => last[index]);
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "exact",
                        "the rollback archive resolves exactly | " + kind);
                    Check.True(KnightIdentityRuntime.TryGetReceipt(last[0].Knight, out KnightIdentityReceipt back0), "rollback receipt 0");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(last[1].Knight, out KnightIdentityReceipt back1), "rollback receipt 1");
                    Check.Equal(0, back0.Style, "the old style returns after the manual rollback");
                    Check.Equal(1, back1.Style, "the second old style returns after the manual rollback");
                }
            });
        }

        /// <summary>按 (scope, hash) 统计各修订号的记录数（index = 修订号）。</summary>
        private static int[] RevisionCounts(string path, string scope, string hash, int maxRevision)
        {
            int[] counts = new int[maxRevision + 1];
            KnightIdentityArchive archive = Sidecar.Load(path);
            if (archive == null || !archive.TryGetSnapshots(scope, out IReadOnlyList<KnightIdentitySnapshot> snapshots)) return counts;
            for (int i = 0; i < snapshots.Count; i++)
            {
                if (!string.Equals(snapshots[i].Hash, hash, StringComparison.Ordinal)) continue;
                int revision = snapshots[i].Revision;
                if (revision >= 0 && revision < counts.Length) counts[revision]++;
            }
            return counts;
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
