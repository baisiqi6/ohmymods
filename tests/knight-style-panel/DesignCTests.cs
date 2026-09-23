using System;
using System.Collections.Generic;
using System.IO;
using KingdomEnhancedMod;

namespace KnightStylePanelTests
{
    /// <summary>
    /// 设计 C（用户锚定再基线化）：unresolved 岛上的面板应用在下一次原生 Save 的捕获作用域内以
    /// save 形态 JSON 写入新 epoch 基线（旧历史全保留）；证据缺口/名单变化 fail-closed（pending 保持）；
    /// 未保存退出=丢弃（重载回原状）；pending 只消费自己的上下文。
    /// </summary>
    internal static class DesignCTests
    {
        internal static void Run()
        {
            PanelRebaselinePersistsThroughSaveAndReload();
            PanelRebaselineWaitsWhenTheRosterChanged();
            PanelRebaselineIsDroppedWhenNoAppliedKnightRemains();
            PanelRebaselineOnlyConsumesItsOwnContext();
            PanelChangeWithoutSaveIsDiscardedOnReload();
        }

        private static void PanelRebaselinePersistsThroughSaveAndReload()
        {
            Case.Run("panel_rebaseline_is_written_in_the_next_save_and_survives_reload", () =>
            {
                using (Fixture f = new Fixture())
                {
                    WriteLegacyHistory(f, out string legacyScope, out string legacyHash, out Guid legacyGuid);

                    List<KnightUnit> units = Knights.Loaded(24000, 3);
                    IslandSaveData island = LoadLegacyPendingIsland(units);
                    Check.True(Log.Saw("load-unresolved:legacy-pending"), "the island resolves as legacy-pending | " + Log.Dump());
                    Check.False(KnightIdentityRuntime.CanFlushSeed, "unresolved context blocks the normal flush");
                    string contextKey = Sidecar.ContextKey(4);
                    Check.True(KnightIdentityContexts.TryGetBinding(contextKey, out _, out bool unresolved, out _) && unresolved, "binding is unresolved");

                    // 面板路径：刷新（读数）→ 设置目标 [1,1,0,0,1] → 应用 → 挂设计 C pending
                    UnitScanCache.TestKnights = Knights.ArrayOf(units);
                    KnightStylePanel.ResetForTests();
                    KnightStylePanel.Refresh();
                    Check.True(KnightStylePanel.Refreshed, "panel refresh ready on the legacy-pending island | " + KnightStylePanel.Blocked);
                    Check.Equal(3, KnightStylePanel.Session.Pool, "all three knights are unconfirmed");
                    Check.True(KnightStylePanel.HasContextBinding, "the panel sees the load binding");
                    Check.True(KnightStylePanel.ContextUnresolved, "the panel sees the unresolved context (arming precondition)");
                    Check.Equal(contextKey, KnightStylePanel.ContextKey, "panel context matches the island key");
                    Check.True(KnightStylePanel.Session.Add(0) && KnightStylePanel.Session.Add(1) && KnightStylePanel.Session.Add(4),
                        "spread the pool over styles 0/1/4");
                    Check.Equal(0, KnightStylePanel.Session.Pool, "pool emptied by the user");
                    KnightStylePanel.Apply();
                    Check.True(KnightStylePanel.Status != null && KnightStylePanel.Status.IndexOf("下一次原生保存", StringComparison.Ordinal) >= 0,
                        "the panel tells the user the write happens on the next save | " + KnightStylePanel.Status);
                    Check.True(Log.Saw("apply: knights=3 reroll=off before=0/0/0/0/0 after=1/1/0/0/1 pool->greece=0 rebaseline=armed"),
                        "panel apply log with the armed rebaseline | " + Log.Dump());
                    Check.True(KnightIdentityRuntime.PanelRebaselineArmed, "design C pending armed by the panel");

                    int[] styles = new int[3];
                    for (int i = 0; i < 3; i++)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(units[i].Knight, out KnightIdentityReceipt applied), "applied receipt " + i);
                        styles[i] = applied.Style;
                    }
                    Check.SequenceEqual(new[] { 1, 1, 0, 0, 1 }, Knights.CountStyles(styles), "applied counts match the targets");

                    // apply 时点的装载形态 JSON 会被原生清空/Decay：写入绝不能用它（死 hash）
                    island.objects = null;

                    byte[] beforeBytes = File.ReadAllBytes(f.SidecarPath);
                    NativeSim.RunSave(island, 0, 4, 0, units); // save 形态：RunSave 重建 objects

                    Check.False(beforeBytes.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "rebaseline wrote the sidecar");
                    Check.True(Log.Saw("user-anchored rebaseline: knights=3 styles="), "log format | " + Log.Dump());
                    Check.True(Log.Saw("prior records preserved"), "the log states history is preserved");
                    Check.False(KnightIdentityRuntime.PanelRebaselineArmed, "pending consumed");
                    Check.True(KnightIdentityRuntime.CanFlushSeed, "the context is resolved again after the rebaseline");
                    Check.True(KnightIdentityContexts.TryGetBinding(contextKey, out string epoch, out bool stillUnresolved, out _)
                        && !stillUnresolved && !string.IsNullOrEmpty(epoch), "binding moved to the new epoch");

                    KnightIdentityArchiveStore.LoadResult stored = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(stored.Archive.TryRestore(legacyScope, legacyHash, "old-unique-id", out KnightIdentityReceipt preserved),
                        "legacy history is still restorable");
                    Check.Same(legacyGuid, preserved.Id, "legacy guid untouched");
                    Check.Equal(2, preserved.Style, "legacy style untouched");
                    Check.True(stored.Archive.TryGetContext(contextKey, out KnightIdentityContext context) && context.Epochs.Count == 1,
                        "exactly one new epoch claimed (the unclaimed legacy scope stays outside the context)");

                    // 读档（save 形态内容）：三个身份按 kind2 精确命中恢复
                    Guid[] guids = new Guid[3];
                    int[] assignedStyles = new int[3];
                    for (int i = 0; i < 3; i++)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(units[i].Knight, out KnightIdentityReceipt receipt), "pre-reload receipt " + i);
                        guids[i] = receipt.Id;
                        assignedStyles[i] = receipt.Style;
                    }

                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData reloaded = NativeSim.CloneIsland(island);
                    List<KnightUnit> again = new List<KnightUnit>();
                    for (int i = 0; i < 3; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(24000 + i);
                        again.Add(unit);
                    }
                    NativeSim.RunLoad(reloaded, (index, uniqueId) => again[index]);
                    Check.True(Log.Saw("load-match scope="), "the save-form baseline matches exactly | " + Log.Dump());
                    Check.True(Log.Saw("kind=exact"), "match kind recorded as the clock-free fingerprint");
                    for (int i = 0; i < 3; i++)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(again[i].Knight, out KnightIdentityReceipt restored), "restored receipt " + i);
                        Check.Same(guids[i], restored.Id, "guid survives the rebaseline reload " + i);
                        Check.Equal(assignedStyles[i], restored.Style, "style survives the rebaseline reload " + i);
                    }
                }
            });
        }

        private static void PanelRebaselineWaitsWhenTheRosterChanged()
        {
            Case.Run("panel_rebaseline_is_fail_closed_when_the_saved_roster_no_longer_covers_the_applied_knights", () =>
            {
                using (Fixture f = new Fixture())
                {
                    WriteLegacyHistory(f, out _, out _, out _);
                    List<KnightUnit> units = Knights.Loaded(24100, 2);
                    IslandSaveData island = LoadLegacyPendingIsland(units);
                    string contextKey = Sidecar.ContextKey(4);
                    Check.True(KnightIdentityRuntime.TryPanelAssignAll(Knights.ArrayOf(units), new[] { 2, 3 }, out _), "panel assignment applied");
                    KnightIdentityRuntime.ArmPanelRebaseline(contextKey);
                    Check.True(KnightIdentityRuntime.PanelRebaselineArmed, "pending armed");

                    byte[] before = File.ReadAllBytes(f.SidecarPath);
                    NativeSim.RunSave(island, 0, 4, 0, new List<KnightUnit> { units[0] }); // 只有一名进入本次捕获
                    Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "an incomplete roster writes nothing");
                    Check.True(Log.Saw("user-anchored rebaseline incomplete"), "incompleteness logged | " + Log.Dump());
                    Check.True(KnightIdentityRuntime.PanelRebaselineArmed, "pending kept for the next save");
                    Check.False(KnightIdentityRuntime.CanFlushSeed, "the context stays unresolved");

                    NativeSim.RunSave(island, 0, 4, 0, units); // 完整名单：写入
                    Check.False(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "the complete roster writes");
                    Check.False(KnightIdentityRuntime.PanelRebaselineArmed, "pending consumed on success");
                    Check.True(KnightIdentityRuntime.CanFlushSeed, "context resolved after the write");
                }
            });
        }

        private static void PanelRebaselineIsDroppedWhenNoAppliedKnightRemains()
        {
            Case.Run("panel_rebaseline_is_dropped_when_no_applied_knight_remains", () =>
            {
                using (Fixture f = new Fixture())
                {
                    WriteLegacyHistory(f, out _, out _, out _);
                    List<KnightUnit> units = Knights.Loaded(24200, 2);
                    IslandSaveData island = LoadLegacyPendingIsland(units);
                    string contextKey = Sidecar.ContextKey(4);
                    Check.True(KnightIdentityRuntime.TryPanelAssignAll(Knights.ArrayOf(units), new[] { 0, 1 }, out _), "panel assignment applied");
                    KnightIdentityRuntime.ArmPanelRebaseline(contextKey);
                    Check.True(KnightIdentityRuntime.PanelRebaselineArmed, "pending armed");

                    for (int i = 0; i < units.Count; i++) units[i].DestroyForTests(); // 应用过的骑士全部离场
                    KnightUnit other = NativeSim.NewKnight(24250); // 岛上另一名骑士（非本批应用对象）
                    KnightIdentityRuntime.OnEnable(other.Knight);
                    byte[] before = File.ReadAllBytes(f.SidecarPath);
                    NativeSim.RunSave(island, 0, 4, 0, new List<KnightUnit> { other });
                    Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "nothing to write without the applied knights");
                    Check.True(Log.Saw("no applied knight remains"), "drop logged | " + Log.Dump());
                    Check.False(KnightIdentityRuntime.PanelRebaselineArmed, "a stale pending is released");
                }
            });
        }

        private static void PanelRebaselineOnlyConsumesItsOwnContext()
        {
            Case.Run("panel_rebaseline_only_consumes_its_own_context", () =>
            {
                using (Fixture f = new Fixture())
                {
                    WriteLegacyHistory(f, out _, out _, out _);
                    List<KnightUnit> units = Knights.Loaded(24300, 2);
                    IslandSaveData island = LoadLegacyPendingIsland(units);
                    string contextKey = Sidecar.ContextKey(4);
                    Check.True(KnightIdentityRuntime.TryPanelAssignAll(Knights.ArrayOf(units), new[] { 0, 1 }, out _), "panel assignment applied");
                    KnightIdentityRuntime.ArmPanelRebaseline(contextKey);
                    Check.True(KnightIdentityRuntime.PanelRebaselineArmed, "pending armed");

                    // 另一个岛的保存：pending 绝不被消费、也绝不写别的上下文
                    KnightUnit otherUnit = NativeSim.NewKnight(24350);
                    KnightIdentityRuntime.OnEnable(otherUnit.Knight);
                    IslandSaveData other = new IslandSaveData { land = 9 };
                    byte[] before = File.ReadAllBytes(f.SidecarPath);
                    NativeSim.RunSave(other, 0, 9, 0, new List<KnightUnit> { otherUnit });
                    Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "another island writes nothing");
                    Check.True(KnightIdentityRuntime.PanelRebaselineArmed, "pending stays armed for its own island");

                    NativeSim.RunSave(island, 0, 4, 0, units);
                    Check.False(KnightIdentityRuntime.PanelRebaselineArmed, "the own island consumes it");
                    Check.False(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "the own island writes the baseline");
                }
            });
        }

        private static void PanelChangeWithoutSaveIsDiscardedOnReload()
        {
            Case.Run("panel_change_without_a_save_is_discarded_on_reload", () =>
            {
                using (Fixture f = new Fixture())
                {
                    // 会话 1：一个普通（未解析？否——首个保存建立 fresh 上下文）快照：2 名骑士 styles 0/1
                    List<KnightUnit> units = Knights.Loaded(24400, 2);
                    Check.True(KnightIdentityRuntime.TryResolve(units[0].Knight, 0, 0u, Styles.All, out _), "session 1 knight 0 medieval");
                    Check.True(KnightIdentityRuntime.TryResolve(units[1].Knight, 1, 0u, Styles.All, out _), "session 1 knight 1 deadlands");
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, units);
                    Check.True(f.SidecarExists, "session 1 saved");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(units[0].Knight, out KnightIdentityReceipt first), "session 1 receipt");

                    // 会话 2：装载 → 面板重派（仅在内存）→ 不保存
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData loadedIsland = NativeSim.CloneIsland(island);
                    CampaignSaveData.current.CurrentIsland = loadedIsland;
                    List<KnightUnit> again = new List<KnightUnit> { NativeSim.NewKnight(24400), NativeSim.NewKnight(24401) };
                    NativeSim.RunLoad(loadedIsland, (index, uniqueId) => again[index]);
                    Check.True(KnightIdentityRuntime.TryGetReceipt(again[0].Knight, out KnightIdentityReceipt restored) && restored.Style == 0,
                        "session 2 restores style 0");
                    Check.True(KnightIdentityRuntime.TryPanelAssignAll(Knights.ArrayOf(again), new[] { 3, 4 }, out _), "session 2 panel reassigns in memory");

                    // 会话 3：未保存退出 → 按磁盘快照重载：旧风格回来（既有身份写入语义）
                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData reloaded = NativeSim.CloneIsland(island);
                    CampaignSaveData.current.CurrentIsland = reloaded;
                    List<KnightUnit> last = new List<KnightUnit> { NativeSim.NewKnight(24400), NativeSim.NewKnight(24401) };
                    NativeSim.RunLoad(reloaded, (index, uniqueId) => last[index]);
                    Check.True(KnightIdentityRuntime.TryGetReceipt(last[0].Knight, out KnightIdentityReceipt back), "reload restores the receipt");
                    Check.Same(first.Id, back.Id, "guid unchanged");
                    Check.Equal(0, back.Style, "the unsaved panel change is gone (same semantics as every identity write)");
                }
            });
        }

        /// <summary>写入一份未归属 legacy 历史（模拟旧世代），使当前岛解析为 legacy-pending。</summary>
        private static void WriteLegacyHistory(Fixture f, out string legacyScope, out string legacyHash, out Guid legacyGuid)
        {
            legacyScope = new string('a', KnightIdentityFingerprint.HexLength);
            legacyHash = KnightIdentityFingerprint.Sha256("an older native island json", legacyScope);
            legacyGuid = Guid.NewGuid();
            Check.True(KnightIdentitySnapshot.TryCreate(KnightIdentityFingerprint.KindLegacy, legacyHash, DateTimeOffset.UtcNow,
                new[] { new KnightIdentitySnapshotEntry("old-unique-id", new KnightIdentityReceipt(legacyGuid, 2)) },
                out KnightIdentitySnapshot snapshot, out string error), "legacy fixture built: " + error);
            KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
            Check.Equal(KnightIdentityArchive.MutationStatus.Applied, archive.RecordSnapshot(legacyScope, snapshot), "legacy snapshot recorded");
            Directory.CreateDirectory(Path.GetDirectoryName(f.SidecarPath));
            Check.True(KnightIdentityArchiveStore.Save(f.SidecarPath, archive).Ok, "legacy fixture written");
        }

        /// <summary>装载给定骑士（预先创建）作为 land=4 的岛，并让上下文解析为 legacy-pending。</summary>
        private static IslandSaveData LoadLegacyPendingIsland(List<KnightUnit> units)
        {
            IslandSaveData island = new IslandSaveData { land = 4, objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>() };
            for (int i = 0; i < units.Count; i++) island.objects.Add(units[i].ToRecord());
            CampaignSaveData.current.CurrentIsland = island;
            NativeSim.RunLoad(island, (index, uniqueId) => units[index]);
            return island;
        }
    }
}
