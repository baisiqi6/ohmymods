using System;
using System.Collections.Generic;
using System.IO;
using KingdomEnhancedMod;

namespace KnightIdentityRuntimeTests
{
    /// <summary>
    /// B（吸收态再基线化）：known-mismatch 会话零收据也能凭严格历史子集门自愈；
    /// 历史外新个体 / 冲突历史 / 容量耗尽一律 fail-closed；再基线后同会话不再重复建 epoch。
    /// </summary>
    internal static class RebaselineTests
    {
        internal static void Run()
        {
            AbsorbingStateRebaselinesAndNextSessionRestores();
            RebaselineGateRejectsUnknownIndividuals();
            RebaselineDropsInconsistentHistory();
            RebaselineIsFailClosedAtCapacity();
            PostRebaselineSessionSavesNormally();
        }

        private static void AbsorbingStateRebaselinesAndNextSessionRestores()
        {
            Case.Run("known_mismatch_save_rebaselines_a_new_epoch_with_the_carried_identity", () =>
            {
                using (Fixture f = new Fixture())
                {
                    // ---- 会话 1：正常保存一份带骑士的快照 ----
                    KnightUnit first = NativeSim.NewKnight(13001);
                    KnightIdentityReceipt original = ResolveHost(first, 2);
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, new List<KnightUnit> { first });
                    Check.True(f.SidecarExists, "session 1 wrote the sidecar");

                    string contextKey = Sidecar.ContextKey(4);
                    KnightIdentityArchiveStore.LoadResult initial = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(initial.Archive.TryGetContext(contextKey, out KnightIdentityContext context), "context registered");
                    string epoch0 = context.Active;
                    string hash0 = KnightIdentityFingerprint.Normalized(island.Json(), epoch0);

                    // ---- 会话 2：更像下一次启动；载入内容已变 → known-mismatch，全程零收据 ----
                    KnightIdentityRuntime.ResetForTests();
                    KnightUnit again = NativeSim.NewKnight(13001); // 同一 uniqueID（instanceID 确定性复用）
                    IslandSaveData changed = NativeSim.CloneIsland(island);
                    changed.objects[0].componentData2[0].data = "{\"rank\":9}";
                    NativeSim.RunLoad(changed, (index, uniqueId) => again);
                    Check.False(KnightIdentityRuntime.TryGetReceipt(again.Knight, out _), "absorbing load restores nothing");
                    Check.True(Logged("load-unresolved:known-mismatch"), "the load resolves as known-mismatch");
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch",
                        "binding carries the load kind");
                    Check.False(KnightIdentityRuntime.CanFlushSeed, "the normal flush path is blocked in the absorbing state");

                    // ---- 吸收态保存：零 tracked owner，门仍可触发（锚点在 owners 早退之前） ----
                    byte[] beforeBytes = File.ReadAllBytes(f.SidecarPath);
                    NativeSim.RunSave(changed, 0, 4, 0, new List<KnightUnit> { again });
                    Check.False(beforeBytes.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "rebaseline wrote the sidecar");
                    Check.True(Logged("rebaseline: context="), "rebaseline receipt logged");
                    Check.True(Logged("carried=1"), "one identity carried into the baseline");

                    KnightIdentityArchiveStore.LoadResult after = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(after.Archive.TryGetContext(contextKey, out KnightIdentityContext rebased), "context still registered");
                    Check.Equal(2, rebased.Epochs.Count, "old epoch kept plus exactly one new epoch");
                    Check.True(rebased.Owns(epoch0), "the pre-absorbing epoch is preserved");
                    string epoch1 = rebased.Active;
                    Check.NotEqual(epoch0, epoch1, "the new epoch is active");
                    Check.True(after.Archive.TryRestore(epoch0, hash0, changed.objects[0].uniqueID, out KnightIdentityReceipt old),
                        "old snapshot still restorable");
                    Check.Same(original.Id, old.Id, "old receipt preserved on disk");

                    string hash1 = KnightIdentityFingerprint.Normalized(changed.Json(), epoch1);
                    Check.True(after.Archive.TryRestore(epoch1, hash1, changed.objects[0].uniqueID, out KnightIdentityReceipt carried),
                        "new baseline carries the individual");
                    Check.Same(original.Id, carried.Id, "carried GUID equals the pre-absorbing identity");
                    Check.Equal(2, carried.Style, "carried style preserved");

                    Check.True(KnightIdentityContexts.TryGetBinding(contextKey, out string rebound, out bool reboundUnresolved, out bool reboundNew)
                        && rebound == epoch1 && !reboundUnresolved && reboundNew, "binding moved to the new epoch");
                    Check.True(KnightIdentityRuntime.CanFlushSeed, "context is resolved again after the rebaseline");

                    // ---- 会话 3：下一会话按 kind2 精确命中并恢复携带的身份 ----
                    KnightIdentityRuntime.ResetForTests();
                    KnightUnit third = NativeSim.NewKnight(13001);
                    IslandSaveData reloaded = NativeSim.CloneIsland(changed);
                    NativeSim.RunLoad(reloaded, (index, uniqueId) => third);
                    Check.True(KnightIdentityRuntime.TryGetReceipt(third.Knight, out KnightIdentityReceipt restored), "next session restores the identity");
                    Check.Same(original.Id, restored.Id, "restored GUID is the carried identity");
                    Check.True(Logged("load-match scope="), "next session is a load match");
                    Check.True(Logged("kind=exact"), "match kind recorded as the clock-free fingerprint");
                }
            });
        }

        private static void RebaselineGateRejectsUnknownIndividuals()
        {
            Case.Run("history_unknown_individuals_close_the_rebaseline_gate", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit first = NativeSim.NewKnight(13001);
                    ResolveHost(first, 2);
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, new List<KnightUnit> { first });
                    string contextKey = Sidecar.ContextKey(4);

                    KnightIdentityRuntime.ResetForTests();
                    KnightUnit again = NativeSim.NewKnight(13001);
                    KnightUnit stranger = NativeSim.NewKnight(15001); // 历史之外的新个体
                    IslandSaveData changed = NativeSim.CloneIsland(island);
                    changed.objects[0].componentData2[0].data = "{\"rank\":9}";
                    NativeSim.RunLoad(changed, (index, uniqueId) => again);
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "absorbing load");

                    byte[] before = File.ReadAllBytes(f.SidecarPath);
                    NativeSim.RunSave(changed, 0, 4, 0, new List<KnightUnit> { again, stranger });
                    Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "gate rejection writes nothing");
                    Check.True(Logged("rebaseline-gate-reject"), "gate rejection is logged");
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out kind) && kind == "known-mismatch",
                        "binding stays fail-closed");
                    Check.False(KnightIdentityRuntime.CanFlushSeed, "context stays unresolved");

                    KnightIdentityArchiveStore.LoadResult after = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(after.Archive.TryGetContext(contextKey, out KnightIdentityContext untouched) && untouched.Epochs.Count == 1,
                        "no epoch was created");
                }
            });
        }

        private static void RebaselineDropsInconsistentHistory()
        {
            Case.Run("inconsistent_history_receipts_are_dropped_and_the_individual_goes_fresh", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit first = NativeSim.NewKnight(13001);
                    KnightIdentityReceipt original = ResolveHost(first, 2);
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, new List<KnightUnit> { first });
                    string contextKey = Sidecar.ContextKey(4);
                    string uniqueId = island.objects[0].uniqueID;
                    KnightIdentityArchiveStore.LoadResult stored = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(stored.Archive.TryGetContext(contextKey, out KnightIdentityContext context), "context registered");
                    string epoch0 = context.Active;

                    // 同一 uniqueID 的第二份历史收据（不同 GUID）：历史不一致 → 绝不携带
                    Guid conflictingId = Guid.NewGuid();
                    Check.True(KnightIdentitySnapshot.TryCreate(KnightIdentityFingerprint.KindNormalized, new string('b', KnightIdentityFingerprint.HexLength),
                        DateTimeOffset.UtcNow, new[] { new KnightIdentitySnapshotEntry(uniqueId, new KnightIdentityReceipt(conflictingId, 1)) },
                        out KnightIdentitySnapshot conflicting, out string error), "conflicting snapshot built: " + error);
                    Check.Equal(KnightIdentityArchive.MutationStatus.Applied, stored.Archive.RecordSnapshot(epoch0, conflicting), "conflicting snapshot recorded");
                    Check.True(KnightIdentityArchiveStore.Save(f.SidecarPath, stored.Archive).Ok, "conflicting sidecar written");

                    KnightIdentityRuntime.ResetForTests();
                    KnightUnit again = NativeSim.NewKnight(13001);
                    IslandSaveData changed = NativeSim.CloneIsland(island);
                    changed.objects[0].componentData2[0].data = "{\"rank\":17}";
                    NativeSim.RunLoad(changed, (index, id) => again);
                    // 内容必须真正前进：若再基线 JSON 与旧快照逐字节相同，解析器会因跨 epoch 多命中且条目不同
                    // 判 conflict（既有契约的 fail-closed，不是本用例要验证的语义）。
                    changed.biome++;
                    NativeSim.RunSave(changed, 0, 4, 0, new List<KnightUnit> { again });
                    Check.True(Logged("rebaseline: context="), "rebaseline still happens (the individual is a known one)");
                    Check.True(Logged("carried=0"), "conflicting history is never carried");

                    KnightIdentityArchiveStore.LoadResult after = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(after.Archive.TryGetContext(contextKey, out KnightIdentityContext rebased), "context still registered");
                    string epoch1 = rebased.Active;
                    string hash1 = KnightIdentityFingerprint.Normalized(changed.Json(), epoch1);
                    Check.True(after.Archive.TryGetSnapshot(epoch1, hash1, out KnightIdentitySnapshot baseline) && baseline.Count == 0,
                        "baseline holds no guessed identity");

                    // 下一会话：内容已前进，空基线成为唯一精确命中（上下文恢复），该个体走现行 fresh 路径
                    KnightIdentityRuntime.ResetForTests();
                    KnightUnit third = NativeSim.NewKnight(13001);
                    IslandSaveData reloaded = NativeSim.CloneIsland(changed);
                    NativeSim.RunLoad(reloaded, (index, id) => third);
                    Check.True(Logged("load-match scope="), "the advanced snapshot matches exactly one stored snapshot");
                    Check.True(KnightIdentityRuntime.CanFlushSeed, "empty baseline resolves the context");
                    Check.True(KnightIdentityRuntime.TryResolve(third.Knight, 3, 0u, Styles.All, out _), "individual resolves fresh");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(third.Knight, out KnightIdentityReceipt fresh), "fresh receipt minted");
                    Check.Equal(3, fresh.Style, "fresh style comes from the native appearance");
                    Check.True(fresh.Id != original.Id && fresh.Id != conflictingId, "no conflicting history identity is reused");
                }
            });
        }

        private static void RebaselineIsFailClosedAtCapacity()
        {
            Case.Run("rebaseline_is_fail_closed_when_epoch_capacity_is_exhausted", () =>
            {
                using (Fixture f = new Fixture())
                {
                    string contextKey = Sidecar.ContextKey(4);
                    KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
                    KnightIdentityReceipt history = new KnightIdentityReceipt(Guid.NewGuid(), 2);
                    for (int i = 0; i < KnightIdentityArchive.MaxEpochs; i++)
                    {
                        string scope = i.ToString("x", System.Globalization.CultureInfo.InvariantCulture).PadLeft(KnightIdentityFingerprint.HexLength, '0');
                        string hash = new string('a', KnightIdentityFingerprint.HexLength - 1) + i.ToString("x", System.Globalization.CultureInfo.InvariantCulture);
                        Check.True(KnightIdentitySnapshot.TryCreate(KnightIdentityFingerprint.KindNormalized, hash, DateTimeOffset.UtcNow,
                            new[] { new KnightIdentitySnapshotEntry("Knight(Clone)-13001", history) },
                            out KnightIdentitySnapshot snapshot, out string error), "epoch fixture " + i + ": " + error);
                        Check.Equal(KnightIdentityArchive.MutationStatus.Applied, archive.RecordSnapshot(scope, snapshot), "epoch scope " + i);
                        Check.True(archive.EnsureContext(contextKey, scope, true), "epoch registered " + i);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(f.SidecarPath));
                    Check.True(KnightIdentityArchiveStore.Save(f.SidecarPath, archive).Ok, "capacity fixture written");

                    KnightUnit unit = NativeSim.NewKnight(13001);
                    IslandSaveData changed = new IslandSaveData { land = 4 };
                    changed.objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> { unit.ToRecord() };
                    NativeSim.RunLoad(changed, (index, id) => unit);
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "absorbing load");

                    byte[] before = File.ReadAllBytes(f.SidecarPath);
                    NativeSim.RunSave(changed, 0, 4, 0, new List<KnightUnit> { unit });
                    Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "epoch exhaustion writes nothing");
                    Check.True(Logged("rebaseline-capacity-context"), "capacity failure logged");
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out kind) && kind == "known-mismatch",
                        "context stays unresolved");
                }
            });

            Case.Run("rebaseline_is_fail_closed_when_scope_capacity_is_exhausted", () =>
            {
                using (Fixture f = new Fixture())
                {
                    string contextKey = Sidecar.ContextKey(4);
                    KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
                    KnightIdentityReceipt history = new KnightIdentityReceipt(Guid.NewGuid(), 2);
                    for (int i = 0; i < KnightIdentityArchive.MaxScopes; i++)
                    {
                        string scope = i.ToString("x2", System.Globalization.CultureInfo.InvariantCulture).PadLeft(KnightIdentityFingerprint.HexLength, '0');
                        string hash = (i + 0x100).ToString("x2", System.Globalization.CultureInfo.InvariantCulture).PadLeft(KnightIdentityFingerprint.HexLength, '0');
                        IReadOnlyList<KnightIdentitySnapshotEntry> entries = i == 0
                            ? new[] { new KnightIdentitySnapshotEntry("Knight(Clone)-13001", history) }
                            : Array.Empty<KnightIdentitySnapshotEntry>();
                        Check.True(KnightIdentitySnapshot.TryCreate(KnightIdentityFingerprint.KindNormalized, hash, DateTimeOffset.UtcNow,
                            entries, out KnightIdentitySnapshot snapshot, out string error), "scope fixture " + i + ": " + error);
                        Check.Equal(KnightIdentityArchive.MutationStatus.Applied, archive.RecordSnapshot(scope, snapshot), "scope " + i);
                        if (i == 0) Check.True(archive.EnsureContext(contextKey, scope, true), "context epoch registered");
                    }
                    Check.Equal(KnightIdentityArchive.MaxScopes, archive.ScopeCount, "scope capacity saturated");
                    Directory.CreateDirectory(Path.GetDirectoryName(f.SidecarPath));
                    Check.True(KnightIdentityArchiveStore.Save(f.SidecarPath, archive).Ok, "scope-capacity fixture written");

                    KnightUnit unit = NativeSim.NewKnight(13001);
                    IslandSaveData changed = new IslandSaveData { land = 4 };
                    changed.objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> { unit.ToRecord() };
                    NativeSim.RunLoad(changed, (index, id) => unit);
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "absorbing load");

                    byte[] before = File.ReadAllBytes(f.SidecarPath);
                    NativeSim.RunSave(changed, 0, 4, 0, new List<KnightUnit> { unit });
                    Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "scope exhaustion writes nothing");
                    Check.True(Logged("rebaseline-capacity-scope"), "scope capacity failure logged");
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out kind) && kind == "known-mismatch",
                        "context stays unresolved");
                }
            });
        }

        private static void PostRebaselineSessionSavesNormally()
        {
            Case.Run("a_same_session_save_after_the_rebaseline_stays_on_the_new_epoch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit first = NativeSim.NewKnight(13001);
                    ResolveHost(first, 2);
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, new List<KnightUnit> { first });
                    string contextKey = Sidecar.ContextKey(4);

                    KnightIdentityRuntime.ResetForTests();
                    KnightUnit again = NativeSim.NewKnight(13001);
                    IslandSaveData changed = NativeSim.CloneIsland(island);
                    changed.objects[0].componentData2[0].data = "{\"rank\":9}";
                    NativeSim.RunLoad(changed, (index, id) => again);
                    NativeSim.RunSave(changed, 0, 4, 0, new List<KnightUnit> { again });

                    KnightIdentityArchiveStore.LoadResult rebased = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(rebased.Archive.TryGetContext(contextKey, out KnightIdentityContext context), "context registered");
                    string rebaselineEpoch = context.Active;
                    int epochsAfterRebaseline = context.Epochs.Count;

                    // 自愈后本会话才可能分配新身份：新招募收到收据 → 后续保存走正常路径
                    KnightUnit recruit = NativeSim.NewKnight(16001);
                    KnightIdentityRuntime.OnEnable(recruit.Knight);
                    Check.True(KnightIdentityRuntime.TryResolve(recruit.Knight, 4, 0u, Styles.All, out _), "recruit resolves after the heal");
                    changed.biome++;
                    NativeSim.RunSave(changed, 0, 4, 0, new List<KnightUnit> { recruit });
                    Check.True(Logged("save scope="), "normal save receipt");

                    KnightIdentityArchiveStore.LoadResult latest = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(latest.Archive.TryGetContext(contextKey, out context), "context still registered");
                    Check.Equal(epochsAfterRebaseline, context.Epochs.Count, "no extra epoch in the same session");
                    Check.Equal(rebaselineEpoch, context.Active, "active epoch unchanged");
                    string hash = KnightIdentityFingerprint.Normalized(changed.Json(), rebaselineEpoch);
                    Check.True(latest.Archive.TryGetSnapshot(rebaselineEpoch, hash, out KnightIdentitySnapshot appended) && appended.Count == 1,
                        "the normal save lands in the rebaseline epoch");
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
