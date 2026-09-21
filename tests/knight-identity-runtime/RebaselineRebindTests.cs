using System;
using System.Collections.Generic;
using System.IO;
using KingdomEnhancedMod;

namespace KnightIdentityRuntimeTests
{
    /// <summary>
    /// B 的重绑定事务：再基线化后只有「本次 save 有 live 证据（同 life + tag + 活跃、且无不同收据）」的
    /// carried uniqueID 才放行普通快照；任一缺口维持写保护。dropped（历史不一致 / 同 GUID 输者）只清
    /// FailedLoad，交给既有 fresh 路径，save 路径内绝不铸造收据。
    /// </summary>
    internal static class RebaselineRebindTests
    {
        internal static void Run()
        {
            CarriedIndividualWithoutEvidenceKeepsWriteProtection();
            CarriedIndividualWithChangedLifeIsNeverRebound();
            ConsistentCarriedIndividualIsReboundWhileTheInconsistentOneGoesFresh();
            DuplicateGuidAcrossUniqueIdsKeepsOneIdentityAndNeverWritesDuplicates();
            OwnerKeepingTheCarriedReceiptPassesTheInPlaceCheck();
            ReceiptHoldingOwnerThatChangesLifeBeforeApplyIsNeverRebound();
            DuplicateGetIdForADifferentOwnerIsAnEvidenceGap();
            DuplicateGetIdWithAnUntrackedOwnerIsAnEvidenceGap();
            DuplicateGetIdForTheSameOwnerIsIdempotent();
            DuplicateGetIdAfterALifeChangeIsAnEvidenceGap();
            DuplicateDiskRecordIsRejected();
            CaptureEvidenceOverflowIsAGap();
        }

        private static void CarriedIndividualWithoutEvidenceKeepsWriteProtection()
        {
            Case.Run("a_carried_individual_without_live_evidence_keeps_the_context_fail_closed", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit first = NativeSim.NewKnight(17001);
                    KnightIdentityReceipt original = Resolve(first, 2);
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, new List<KnightUnit> { first });
                    string contextKey = Sidecar.ContextKey(4);

                    KnightIdentityRuntime.ResetForTests();
                    KnightUnit again = NativeSim.NewKnight(17001);
                    IslandSaveData changed = NativeSim.CloneIsland(island);
                    changed.objects[0].componentData2[0].data = "{\"rank\":9}";
                    NativeSim.RunLoad(changed, (index, id) => again);
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "absorbing load");

                    // 本次 save 只为非骑士对象调用了 GetID：盘上有该骑士记录，但没有任何 live owner 证据。
                    // pristine 与后来被 RunSave 重建记录的 changed 分开，保证下一会话装载的内容就是基线内容。
                    changed.biome++;
                    IslandSaveData pristine = NativeSim.CloneIsland(changed);
                    KnightUnit nonKnight = NativeSim.NewKnight(17101, tag: "Tree");
                    RunCapture(pristine, 0, 4, 0, () => KnightIdentitySaveBridge.HandleGetId(nonKnight.Persistent, "record-without-knight-evidence"));

                    Check.True(Logged("rebaseline: context="), "the baseline was still written");
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out kind) && kind == "rebaseline-incomplete",
                        "the incomplete rebind is recorded explicitly");
                    Check.False(KnightIdentityRuntime.CanFlushSeed, "write protection is kept while evidence is missing");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(again.Knight, out _), "no receipt was invented without evidence");

                    // 后续普通保存不得产出任何新快照：否则该个体会从下一代快照里静默消失。
                    byte[] before = File.ReadAllBytes(f.SidecarPath);
                    NativeSim.RunSave(changed, 0, 4, 0, new List<KnightUnit> { again });
                    Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "the next save writes nothing");
                    Check.False(Logged("save-preserve-unresolved"), "the blocked save never even reaches the snapshot build");

                    // 下一会话按基线精确命中：身份照常恢复，没有丢。
                    KnightIdentityRuntime.ResetForTests();
                    KnightUnit third = NativeSim.NewKnight(17001);
                    NativeSim.RunLoad(NativeSim.CloneIsland(pristine), (index, id) => third);
                    Check.True(KnightIdentityRuntime.TryGetReceipt(third.Knight, out KnightIdentityReceipt restored), "identity restored in the next session");
                    Check.Same(original.Id, restored.Id, "restored GUID is the carried identity");
                    Check.Equal(2, restored.Style, "restored style is the carried style");
                }
            });
        }

        private static void CarriedIndividualWithChangedLifeIsNeverRebound()
        {
            Case.Run("a_carried_individual_whose_live_life_changed_is_never_rebound", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit first = NativeSim.NewKnight(18001);
                    KnightIdentityReceipt original = Resolve(first, 3);
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, new List<KnightUnit> { first });
                    string contextKey = Sidecar.ContextKey(4);

                    KnightIdentityRuntime.ResetForTests();
                    KnightUnit again = NativeSim.NewKnight(18001);
                    IslandSaveData changed = NativeSim.CloneIsland(island);
                    changed.objects[0].componentData2[0].data = "{\"rank\":9}";
                    NativeSim.RunLoad(changed, (index, id) => again);
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "absorbing load");

                    // 捕获之后、ApplyCapture 之前：对象被销毁，同 uniqueID 换成新对象（life 前进）。
                    changed.biome++; // 让基线 JSON 真正前进，下一会话只精确命中基线
                    KnightUnit replacement = null;
                    NativeSim.RunSave(changed, 0, 4, 0, new List<KnightUnit> { again }, captured =>
                    {
                        captured.DestroyForTests();
                        replacement = NativeSim.NewKnight(18001);
                        KnightIdentityRuntime.OnEnable(replacement.Knight);
                    });
                    Check.True(replacement != null, "the replacement object exists");

                    Check.False(KnightIdentityRuntime.TryGetReceipt(replacement.Knight, out _), "the new life is never handed the carried receipt");
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out kind) && kind == "rebaseline-incomplete",
                        "the changed life is treated as an evidence gap");
                    Check.False(KnightIdentityRuntime.CanFlushSeed, "write protection is kept when the evidence life changed");
                    Check.False(KnightIdentityRuntime.TryResolve(replacement.Knight, 3, 0u, Styles.All, out _), "the unresolved context blocks fresh minting too");

                    // 下一会话身份照常从基线恢复：缺口只影响本会话的写入，不丢历史。
                    KnightIdentityRuntime.ResetForTests();
                    KnightUnit third = NativeSim.NewKnight(18001);
                    NativeSim.RunLoad(NativeSim.CloneIsland(changed), (index, id) => third);
                    Check.True(KnightIdentityRuntime.TryGetReceipt(third.Knight, out KnightIdentityReceipt restored), "identity restored in the next session");
                    Check.Same(original.Id, restored.Id, "restored GUID is the carried identity");
                }
            });
        }

        private static void ConsistentCarriedIndividualIsReboundWhileTheInconsistentOneGoesFresh()
        {
            Case.Run("only_the_consistent_carried_individual_is_rebound_and_the_inconsistent_one_goes_fresh", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit kept = NativeSim.NewKnight(19001);
                    KnightIdentityReceipt keptOriginal = Resolve(kept, 1);
                    KnightUnit conflicted = NativeSim.NewKnight(19002);
                    KnightIdentityReceipt conflictedOriginal = Resolve(conflicted, 2);
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, new List<KnightUnit> { kept, conflicted });
                    string contextKey = Sidecar.ContextKey(4);
                    string keptUniqueId = island.objects[0].uniqueID;
                    string conflictedUniqueId = island.objects[1].uniqueID;

                    // 同一 uniqueID 再记一份不同 GUID 的历史 → 该 uniqueID 历史不一致（绝不携带）。
                    KnightIdentityArchiveStore.LoadResult stored = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(stored.Archive.TryGetContext(contextKey, out KnightIdentityContext context), "context registered");
                    Check.True(KnightIdentitySnapshot.TryCreate(KnightIdentityFingerprint.KindNormalized, new string('c', KnightIdentityFingerprint.HexLength),
                        DateTimeOffset.UtcNow, new[] { new KnightIdentitySnapshotEntry(conflictedUniqueId, new KnightIdentityReceipt(Guid.NewGuid(), 2)) },
                        out KnightIdentitySnapshot conflicting, out string error), "conflicting snapshot built: " + error);
                    Check.Equal(KnightIdentityArchive.MutationStatus.Applied, stored.Archive.RecordSnapshot(context.Active, conflicting), "conflicting history recorded");
                    Check.True(KnightIdentityArchiveStore.Save(f.SidecarPath, stored.Archive).Ok, "conflicting sidecar written");

                    KnightIdentityRuntime.ResetForTests();
                    KnightUnit keptAgain = NativeSim.NewKnight(19001), conflictedAgain = NativeSim.NewKnight(19002);
                    IslandSaveData changed = NativeSim.CloneIsland(island);
                    changed.objects[0].componentData2[0].data = "{\"rank\":5}";
                    changed.biome++;
                    NativeSim.RunLoad(changed, (index, id) => index == 0 ? keptAgain : conflictedAgain);
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "absorbing load");

                    NativeSim.RunSave(changed, 0, 4, 0, new List<KnightUnit> { keptAgain, conflictedAgain });
                    Check.True(Logged("carried=1"), "the consistent individual is carried");
                    Check.True(Logged("dropped=1"), "the inconsistent individual is dropped");
                    Check.True(KnightIdentityRuntime.CanFlushSeed, "the context is resolved again");

                    Check.True(KnightIdentityRuntime.TryGetReceipt(keptAgain.Knight, out KnightIdentityReceipt keptBack), "the consistent individual is rebound in-session");
                    Check.Same(keptOriginal.Id, keptBack.Id, "rebound GUID is the historical one");
                    Check.Equal(1, keptBack.Style, "rebound style is the historical one");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(conflictedAgain.Knight, out _), "the inconsistent individual gets no guessed receipt");

                    // 被丢弃者走既有 fresh 路径（同一 TryResolve 门）：GUID 与两份历史都不同。
                    Check.True(KnightIdentityRuntime.TryResolve(conflictedAgain.Knight, conflictedOriginal.Style, 0u, Styles.All, out _), "the dropped individual resolves fresh");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(conflictedAgain.Knight, out KnightIdentityReceipt fresh), "fresh receipt minted");
                    Check.True(fresh.Id != keptOriginal.Id && fresh.Id != conflictedOriginal.Id, "fresh GUID matches no history");

                    // 混合保存 + 重载：拿回的历史身份与 fresh 身份都稳定。
                    changed.biome++;
                    NativeSim.RunSave(changed, 0, 4, 0, new List<KnightUnit> { keptAgain, conflictedAgain });
                    Check.True(Logged("save scope="), "the follow-up save uses the normal path");
                    Check.False(Logged("save-duplicate-guid"), "no duplicate-GUID rejection");

                    KnightIdentityRuntime.ResetForTests();
                    KnightUnit keptLast = NativeSim.NewKnight(19001), conflictedLast = NativeSim.NewKnight(19002);
                    NativeSim.RunLoad(NativeSim.CloneIsland(changed), (index, id) => index == 0 ? keptLast : conflictedLast);
                    Check.True(KnightIdentityRuntime.TryGetReceipt(keptLast.Knight, out KnightIdentityReceipt lastKept), "the consistent identity restores");
                    Check.Same(keptOriginal.Id, lastKept.Id, "the historical GUID survives the reload");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(conflictedLast.Knight, out KnightIdentityReceipt lastFresh), "the fresh identity restores");
                    Check.Same(fresh.Id, lastFresh.Id, "the fresh GUID survives the reload");
                }
            });
        }

        private static void DuplicateGuidAcrossUniqueIdsKeepsOneIdentityAndNeverWritesDuplicates()
        {
            Case.Run("a_duplicate_guid_across_unique_ids_keeps_one_identity_and_never_writes_duplicates", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit first = NativeSim.NewKnight(20001);
                    KnightIdentityReceipt shared = Resolve(first, 4);
                    KnightUnit other = NativeSim.NewKnight(20002); // 只用来取得第二个 uniqueID（无收据）
                    string otherUniqueId = other.ToRecord().uniqueID;
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, new List<KnightUnit> { first });
                    string contextKey = Sidecar.ContextKey(4);
                    string firstUniqueId = island.objects[0].uniqueID;

                    // 历史并集里第二个 uniqueID 携带同一个 GUID（跨 uniqueID 同 GUID 可达）。
                    KnightIdentityArchiveStore.LoadResult stored = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(stored.Archive.TryGetContext(contextKey, out KnightIdentityContext context), "context registered");
                    Check.True(KnightIdentitySnapshot.TryCreate(KnightIdentityFingerprint.KindNormalized, new string('d', KnightIdentityFingerprint.HexLength),
                        DateTimeOffset.UtcNow, new[] { new KnightIdentitySnapshotEntry(otherUniqueId, shared) },
                        out KnightIdentitySnapshot twin, out string error), "twin snapshot built: " + error);
                    Check.Equal(KnightIdentityArchive.MutationStatus.Applied, stored.Archive.RecordSnapshot(context.Active, twin), "twin history recorded");
                    Check.True(KnightIdentityArchiveStore.Save(f.SidecarPath, stored.Archive).Ok, "twin sidecar written");

                    KnightIdentityRuntime.ResetForTests();
                    KnightUnit firstAgain = NativeSim.NewKnight(20001), otherAgain = NativeSim.NewKnight(20002);
                    IslandSaveData changed = NativeSim.CloneIsland(island);
                    changed.objects.Add(other.ToRecord());
                    changed.biome++;
                    NativeSim.RunLoad(changed, (index, id) => index == 0 ? firstAgain : otherAgain);
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "absorbing load");

                    NativeSim.RunSave(changed, 0, 4, 0, new List<KnightUnit> { firstAgain, otherAgain });
                    Check.True(Logged("carried=1"), "exactly one duplicate-GUID individual is carried");
                    Check.True(Logged("dropped=1"), "the duplicate-GUID loser is dropped");
                    Check.False(Logged("rebaseline-snapshot:"), "no duplicate-GUID snapshot rejection happened during the heal");

                    KnightIdentityArchiveStore.LoadResult rebased = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(rebased.Archive.TryGetContext(contextKey, out KnightIdentityContext rebasedContext), "context still registered");
                    string rebaselineEpoch = rebasedContext.Active;
                    string baselineHash = KnightIdentityFingerprint.Normalized(changed.Json(), rebaselineEpoch);
                    Check.True(rebased.Archive.TryGetSnapshot(rebaselineEpoch, baselineHash, out KnightIdentitySnapshot baseline), "baseline snapshot exists");
                    Check.Equal(1, baseline.Count, "the baseline holds exactly one of the two duplicate-GUID individuals");
                    Check.True(baseline.TryGet(firstUniqueId, out KnightIdentityReceipt winner) && winner.Id == shared.Id,
                        "the smallest uniqueID keeps the shared GUID");
                    Check.False(baseline.TryGet(otherUniqueId, out _), "the loser is never written into the snapshot");

                    Check.True(KnightIdentityRuntime.TryGetReceipt(firstAgain.Knight, out KnightIdentityReceipt boundWinner), "the winner is rebound in-session");
                    Check.Same(shared.Id, boundWinner.Id, "the winner keeps the historical GUID");
                    Check.True(KnightIdentityRuntime.CanFlushSeed, "the context is resolved again");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(otherAgain.Knight, out _), "the loser still has no receipt");

                    Check.True(KnightIdentityRuntime.TryResolve(otherAgain.Knight, 3, 0u, Styles.All, out _), "the loser resolves fresh");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(otherAgain.Knight, out KnightIdentityReceipt fresh), "fresh receipt minted for the loser");
                    Check.NotEqual(shared.Id, fresh.Id, "the fresh GUID differs from the shared historical one");

                    changed.biome++;
                    NativeSim.RunSave(changed, 0, 4, 0, new List<KnightUnit> { firstAgain, otherAgain });
                    Check.False(Logged("save-duplicate-guid"), "the follow-up save is not rejected");

                    string mixedHash = KnightIdentityFingerprint.Normalized(changed.Json(), rebaselineEpoch);
                    Check.True(KnightIdentityArchiveStore.Load(f.SidecarPath).Archive.TryGetSnapshot(rebaselineEpoch, mixedHash, out KnightIdentitySnapshot mixed),
                        "the follow-up snapshot exists");
                    Check.Equal(2, mixed.Count, "both individuals are archived in the follow-up snapshot");
                    Check.True(mixed.TryGet(firstUniqueId, out KnightIdentityReceipt keptWinner) && keptWinner.Id == shared.Id, "the winner keeps the shared GUID in the new snapshot");
                    Check.True(mixed.TryGet(otherUniqueId, out KnightIdentityReceipt keptLoser) && keptLoser.Id == fresh.Id, "the loser's fresh identity is archived");
                }
            });
        }

        private static void OwnerKeepingTheCarriedReceiptPassesTheInPlaceCheck()
        {
            Case.Run("an_owner_that_keeps_the_carried_receipt_passes_the_in_place_terminal_check", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit first = NativeSim.NewKnight(21001);
                    IslandSaveData changed = EnterAbsorbingSessionKeepingReceipt(first, 4, out string contextKey, out string uniqueId, out KnightIdentityReceipt original);
                    Check.True(KnightIdentityRuntime.TryGetReceipt(first.Knight, out KnightIdentityReceipt kept) && kept.Id == original.Id,
                        "the reused owner still holds the historical receipt");
                    Check.False(KnightIdentityRuntime.CanFlushSeed, "the normal flush path is blocked by the absorbing binding");

                    NativeSim.RunSave(changed, 0, 4, 0, new List<KnightUnit> { first });
                    Check.True(Logged("rebaseline: context="), "the heal ran");
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string after) && after == "rebaseline",
                        "the in-place terminal check found no gap and the binding moved to the rebaselined epoch");
                    Check.True(KnightIdentityRuntime.CanFlushSeed, "the context is resolved again");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(first.Knight, out KnightIdentityReceipt keptAfter) && keptAfter.Equals(original),
                        "the receipt is handed back idempotently");
                    Check.True(BaselineCarries(f, changed, contextKey, uniqueId, out KnightIdentityReceipt carried), "the baseline carries the identity");
                    Check.Same(original.Id, carried.Id, "carried GUID unchanged");
                }
            });
        }

        private static void ReceiptHoldingOwnerThatChangesLifeBeforeApplyIsNeverRebound()
        {
            Case.Run("an_owner_that_changes_life_before_apply_is_never_rebound", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit first = NativeSim.NewKnight(21101);
                    IslandSaveData changed = EnterAbsorbingSessionKeepingReceipt(first, 4, out string contextKey, out string uniqueId, out KnightIdentityReceipt original);

                    // 捕获（GetID 时仍持同收据）之后、ApplyCapture 之前换 life：捕获值不等于现状。
                    byte[] before = File.ReadAllBytes(f.SidecarPath);
                    NativeSim.RunSave(changed, 0, 4, 0, new List<KnightUnit> { first },
                        captured => KnightIdentityRuntime.OnEnable(captured.Knight));

                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "rebaseline-incomplete",
                        "the stale capture is treated as an evidence gap");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(first.Knight, out _), "the new life gets no receipt");
                    Check.False(KnightIdentityRuntime.CanFlushSeed, "write protection is kept for the new life");
                    byte[] afterRebaseline = File.ReadAllBytes(f.SidecarPath);
                    Check.False(before.AsSpan().SequenceEqual(afterRebaseline), "the baseline itself was still written");

                    NativeSim.RunSave(changed, 0, 4, 0, new List<KnightUnit> { first });
                    Check.True(afterRebaseline.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "the next save writes nothing");
                    Check.True(BaselineCarries(f, changed, contextKey, uniqueId, out KnightIdentityReceipt carried), "the baseline keeps the identity");
                    Check.Same(original.Id, carried.Id, "carried GUID is unchanged");
                }
            });
        }

        private static void DuplicateGetIdForADifferentOwnerIsAnEvidenceGap()
        {
            Case.Run("a_duplicate_getid_for_a_different_owner_is_an_evidence_gap", () =>
            {
                using (Fixture f = new Fixture())
                {
                    IslandSaveData changed = EnterAbsorbingSession(22101, 4, out string contextKey, out KnightUnit again, out string uniqueId, out _);
                    KnightUnit other = NativeSim.NewKnight(22102);
                    KnightIdentityRuntime.OnEnable(other.Knight); // 第二个被跟踪 owner（无收据）

                    byte[] before = File.ReadAllBytes(f.SidecarPath);
                    RunCapture(changed, 0, 4, 0, () =>
                    {
                        KnightIdentitySaveBridge.HandleGetId(again.Persistent, uniqueId);
                        KnightIdentitySaveBridge.HandleGetId(other.Persistent, uniqueId);
                    });

                    Check.True(Logged("save-evidence-gap"), "the ambiguous capture is reported before any write");
                    Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "no rebaseline touched the sidecar");
                    Check.False(KnightIdentityRuntime.CanFlushSeed, "the context stays fail-closed");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(again.Knight, out _), "the first owner is not handed the historical receipt");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(other.Knight, out _), "nor is the second owner");
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "the binding is untouched");
                }
            });
        }

        private static void DuplicateGetIdWithAnUntrackedOwnerIsAnEvidenceGap()
        {
            // tagKnight 但从未登记（如条目容量满未建条目）：拿不到 life 证据。两个出现顺序都必须判缺口。
            foreach (bool untrackedFirst in new[] { true, false })
            {
                Case.Run("a_duplicate_getid_with_an_untracked_owner_is_an_evidence_gap(" + (untrackedFirst ? "untracked-first" : "untracked-second") + ")", () =>
                {
                    using (Fixture f = new Fixture())
                    {
                        IslandSaveData changed = EnterAbsorbingSession(22601, 4, out string contextKey, out KnightUnit again, out string uniqueId, out _);
                        KnightUnit untracked = NativeSim.NewKnight(22602); // 不 OnEnable：没有条目，也没有 life

                        byte[] before = File.ReadAllBytes(f.SidecarPath);
                        RunCapture(changed, 0, 4, 0, () =>
                        {
                            if (untrackedFirst)
                            {
                                KnightIdentitySaveBridge.HandleGetId(untracked.Persistent, uniqueId);
                                KnightIdentitySaveBridge.HandleGetId(again.Persistent, uniqueId);
                            }
                            else
                            {
                                KnightIdentitySaveBridge.HandleGetId(again.Persistent, uniqueId);
                                KnightIdentitySaveBridge.HandleGetId(untracked.Persistent, uniqueId);
                            }
                        });

                        Check.True(Logged("save-evidence-gap"), "the life-less owner makes the capture an evidence gap");
                        Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "no rebaseline touched the sidecar");
                        Check.False(KnightIdentityRuntime.CanFlushSeed, "the context stays fail-closed");
                        Check.False(KnightIdentityRuntime.TryGetReceipt(again.Knight, out _), "the tracked owner is not handed the historical receipt");
                        Check.False(KnightIdentityRuntime.TryGetReceipt(untracked.Knight, out _), "nor is the life-less owner");
                        Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "the binding is untouched");
                    }
                });
            }
        }

        private static void DuplicateGetIdForTheSameOwnerIsIdempotent()
        {
            Case.Run("a_duplicate_getid_for_the_same_owner_is_idempotent", () =>
            {
                using (Fixture f = new Fixture())
                {
                    IslandSaveData changed = EnterAbsorbingSession(22201, 4, out _, out KnightUnit again, out string uniqueId, out KnightIdentityReceipt original);

                    RunCapture(changed, 0, 4, 0, () =>
                    {
                        KnightIdentitySaveBridge.HandleGetId(again.Persistent, uniqueId);
                        KnightIdentitySaveBridge.HandleGetId(again.Persistent, uniqueId);
                    });

                    Check.False(Logged("save-evidence-gap"), "same owner and same life is idempotent");
                    Check.True(Logged("rebaseline: context="), "the heal still runs");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(again.Knight, out KnightIdentityReceipt restored) && restored.Equals(original),
                        "the single owner is rebound to the carried identity");
                    Check.True(KnightIdentityRuntime.CanFlushSeed, "the context is resolved again");
                }
            });
        }

        private static void DuplicateGetIdAfterALifeChangeIsAnEvidenceGap()
        {
            Case.Run("a_duplicate_getid_after_a_life_change_is_an_evidence_gap", () =>
            {
                using (Fixture f = new Fixture())
                {
                    IslandSaveData changed = EnterAbsorbingSession(22301, 4, out string contextKey, out KnightUnit again, out string uniqueId, out _);

                    byte[] before = File.ReadAllBytes(f.SidecarPath);
                    RunCapture(changed, 0, 4, 0, () =>
                    {
                        KnightIdentitySaveBridge.HandleGetId(again.Persistent, uniqueId);
                        KnightIdentityRuntime.OnEnable(again.Knight); // 同 owner 换 life
                        KnightIdentitySaveBridge.HandleGetId(again.Persistent, uniqueId);
                    });

                    Check.True(Logged("save-evidence-gap"), "the changed life is an ambiguous evidence gap");
                    Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "no rebaseline touched the sidecar");
                    Check.False(KnightIdentityRuntime.CanFlushSeed, "the context stays fail-closed");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(again.Knight, out _), "no receipt is invented for the new life");
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "the binding is untouched");
                }
            });
        }

        private static void DuplicateDiskRecordIsRejected()
        {
            Case.Run("a_duplicate_disk_record_for_the_same_unique_id_is_rejected", () =>
            {
                using (Fixture f = new Fixture())
                {
                    IslandSaveData changed = EnterAbsorbingSession(22401, 4, out string contextKey, out _, out _, out _);

                    // 盘记录本身重复（同 uniqueID 两条 Knight 记录）：去重结果不能当作唯一 owner 的证据。
                    IslandSaveData duplicated = NativeSim.CloneIsland(changed);
                    duplicated.objects.Add(NativeSim.CloneIsland(changed).objects[0]);
                    KnightUnit nonKnight = NativeSim.NewKnight(22402, tag: "Tree");

                    byte[] before = File.ReadAllBytes(f.SidecarPath);
                    RunCapture(duplicated, 0, 4, 0, () => KnightIdentitySaveBridge.HandleGetId(nonKnight.Persistent, "record-without-knight-evidence"));

                    Check.True(Logged("rebaseline-duplicate-record"), "the duplicate disk record is rejected");
                    Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "no rebaseline touched the sidecar");
                    Check.False(KnightIdentityRuntime.CanFlushSeed, "the context stays fail-closed");
                    Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "the binding is untouched");
                }
            });
        }

        private static void CaptureEvidenceOverflowIsAGap()
        {
            Case.Run("capture_evidence_overflow_is_a_gap_and_never_passes_silently", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit unit = NativeSim.NewKnight(22501);
                    KnightIdentityRuntime.OnEnable(unit.Knight);
                    Check.True(KnightIdentityRuntime.TryResolve(unit.Knight, 1, 0u, Styles.All, out _), "resolve");
                    IslandSaveData island = new IslandSaveData { land = 4 };
                    NativeSim.RunSave(island, 0, 4, 0, new List<KnightUnit> { unit });
                    string uniqueId = island.objects[0].uniqueID;

                    // 证据集合达到容量上限后继续登记：截断必须当缺口，绝不静默放行（否则普通路径会照写快照）。
                    byte[] before = File.ReadAllBytes(f.SidecarPath);
                    island.biome++;
                    RunCapture(island, 0, 4, 0, () =>
                    {
                        KnightIdentitySaveBridge.HandleGetId(unit.Persistent, uniqueId);
                        for (int i = 0; i <= KnightIdentitySaveBridge.MaxCapturedEvidence; i++)
                        {
                            KnightIdentitySaveBridge.HandleGetId(unit.Persistent, "pad-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                    });

                    Check.True(Logged("save-evidence-gap"), "the capacity overflow is treated as a gap");
                    Check.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(f.SidecarPath)), "the overflowing capture wrote nothing");
                }
            });
        }

        /// <summary>
        /// 会话 1 保存 U→G；会话 2 装载内容已变 → known-mismatch（旧骑士回 FailedLoad、无收据）。
        /// 返回会话 2 的岛（记录内容已重新前进）。
        /// </summary>
        private static IslandSaveData EnterAbsorbingSession(int instanceId, int land, out string contextKey, out KnightUnit again, out string uniqueId, out KnightIdentityReceipt original)
        {
            KnightUnit first = NativeSim.NewKnight(instanceId);
            original = Resolve(first, 2);
            IslandSaveData island = new IslandSaveData { land = land };
            NativeSim.RunSave(island, 0, land, 0, new List<KnightUnit> { first });
            contextKey = Sidecar.ContextKey(land);
            uniqueId = island.objects[0].uniqueID;

            KnightIdentityRuntime.ResetForTests();
            again = NativeSim.NewKnight(instanceId);
            IslandSaveData changed = NativeSim.CloneIsland(island);
            changed.objects[0].componentData2[0].data = "{\"rank\":9}";
            changed.biome++;
            KnightUnit loadedOwner = again;
            NativeSim.RunLoad(changed, (index, id) => loadedOwner);
            Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch", "absorbing load");
            return changed;
        }

        /// <summary>会话 2 known-mismatch 装载，但对象不重新 OnEnable（复用未重启用）：owner 保留旧收据。</summary>
        private static IslandSaveData EnterAbsorbingSessionKeepingReceipt(KnightUnit owner, int land, out string contextKey, out string uniqueId, out KnightIdentityReceipt original)
        {
            original = Resolve(owner, 2);
            IslandSaveData island = new IslandSaveData { land = land };
            NativeSim.RunSave(island, 0, land, 0, new List<KnightUnit> { owner });
            contextKey = Sidecar.ContextKey(land);
            uniqueId = island.objects[0].uniqueID;

            IslandSaveData changed = NativeSim.CloneIsland(island);
            changed.objects[0].componentData2[0].data = "{\"rank\":9}";
            changed.biome++;
            KnightIdentityLoadBridge.LoadScope scope = KnightIdentityLoadBridge.Begin(changed);
            try
            {
                KnightIdentityLoadBridge.HandleTryCreateOrFind(changed.objects[0], owner.Persistent); // 不 OnEnable：收据保留
            }
            finally
            {
                KnightIdentityLoadBridge.End(null, scope);
            }
            Check.True(KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind) && kind == "known-mismatch",
                "absorbing binding without a re-enable");
            return changed;
        }

        /// <summary>再基线化 epoch 的基线快照是否携带该 uniqueID 的收据。</summary>
        private static bool BaselineCarries(Fixture f, IslandSaveData island, string contextKey, string uniqueId, out KnightIdentityReceipt receipt)
        {
            receipt = default;
            KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(f.SidecarPath);
            if (loaded.Archive == null || !loaded.Archive.TryGetContext(contextKey, out KnightIdentityContext context)) return false;
            string hash = KnightIdentityFingerprint.Normalized(island.Json(), context.Active);
            return loaded.Archive.TryRestore(context.Active, hash, uniqueId, out receipt);
        }

        /// <summary>
        /// 手工驱动一次 save 捕获：用生产捕获路径（GetID 后缀）建 scope，调用方在 <paramref name="body"/> 里按需要
        /// 的次序/ID 触发 GetID（证据缺口与重复回调场景），随后走 Priority.Last 的 ApplyCapture。
        /// </summary>
        private static void RunCapture(IslandSaveData island, int campaign, int land, int challenge, Action body)
        {
            KnightIdentitySaveBridge.SaveCapture state = KnightIdentitySaveBridge.BeginCapture(campaign, land, challenge);
            try
            {
                IslandSaveData.isSavingGame = true;
                IslandSaveData.CurrentlySavingIsland = island;
                IslandSaveData._currentlySavingIsland = island;
                body();
            }
            finally
            {
                IslandSaveData.isSavingGame = false;
                IslandSaveData.CurrentlySavingIsland = null;
                IslandSaveData._currentlySavingIsland = null;
            }
            KnightIdentitySaveBridge.ApplyCapture(state);
            KnightIdentitySaveBridge.EndCapture(null, state);
        }

        private static KnightIdentityReceipt Resolve(KnightUnit unit, int existingStyle)
        {
            KnightIdentityRuntime.OnEnable(unit.Knight);
            Check.True(KnightIdentityRuntime.TryResolve(unit.Knight, existingStyle, 3u, Styles.All, out _), "resolve " + unit.Go.name);
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
