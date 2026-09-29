using System;
using System.Collections.Generic;
using System.IO;
using KingdomEnhancedMod;

namespace KnightIdentityRuntimeTests
{
    /// <summary>
    /// Save 成员校验（knight-load-roundtrip item 6/7）：最终 island.objects 名单 + GetID 捕获证据的联合判定——
    /// 明确 tagSquire 才排除（且必须复核仍是同一对象/同 life/仍 Squire）、未被序列化的引用歧义不阻断、
    /// 实际成员缺证据 / 同 ID 异 owner / 非法 ID / 读失败一律拒写。
    /// </summary>
    internal static class SaveMemberValidationTests
    {
        internal static void Run()
        {
            MixedSquireRecordsAreExcludedAndKnightsStillSave();
            ASquirePromotedAfterCaptureRejectsTheSave();
            UnserializedReferenceAmbiguityDoesNotBlockMemberSave();
            MemberReferenceAmbiguityStillBlocksTheSave();
            InvalidMemberUniqueIdIsRejected();
            UnreadableMemberRecordIsRejected();
            CrossTypeDuplicateIdRejectsTheSave();
            NonKnightRecordsAreNeverKnightGated();
            AmbiguityOnANonKnightRecordIdStillBlocks();
        }

        private static void MixedSquireRecordsAreExcludedAndKnightsStillSave()
        {
            Case.Run("mixed_squire_records_are_explicitly_excluded_and_knights_still_save", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit first = NativeSim.NewKnight(51001);
                    KnightIdentityReceipt firstReceipt = Resolve(first, 1);
                    KnightUnit second = NativeSim.NewKnight(51002);
                    KnightIdentityReceipt secondReceipt = Resolve(second, 3);
                    KnightUnit squire = NativeSim.NewKnight(51003, tag: "Squire", name: "Squire(Clone)");
                    KnightIdentityRuntime.OnEnable(squire.Knight); // 侍从也被跟踪，但没有收据

                    IslandSaveData island = new IslandSaveData { land = 6 };
                    NativeSim.RunSave(island, 0, 6, 0, new List<KnightUnit> { first, second, squire });

                    Check.True(Logged("save scope="), "the save went through (the Squire record is an explicit exclusion)");
                    Check.False(Logged("save-member-missing"), "a Squire record is never treated as a missing member");
                    KnightIdentityArchive archive = Sidecar.Load(f.SidecarPath);
                    string epoch = Sidecar.ActiveEpoch(f.SidecarPath, 6);
                    string hash = KnightIdentityFingerprint.Normalized(NativeSim.CloneIsland(island).Json(), epoch);
                    Check.True(archive.TryGetSnapshot(epoch, hash, out KnightIdentitySnapshot match), "the save-form snapshot exists");
                    Check.Equal(2, match.Count, "only the two real knights are in the snapshot");

                    KnightIdentityRuntime.ResetForTests();
                    IslandSaveData reloaded = NativeSim.CloneIsland(island);
                    KnightUnit firstAgain = NativeSim.NewKnight(52001);
                    KnightUnit secondAgain = NativeSim.NewKnight(52002);
                    KnightUnit squireAgain = NativeSim.NewKnight(52003, tag: "Squire", name: "Squire(Clone)");
                    NativeSim.RunLoad(reloaded, (index, id) => index == 0 ? firstAgain : index == 1 ? secondAgain : squireAgain);
                    Check.True(KnightIdentityRuntime.TryGetReceipt(firstAgain.Knight, out KnightIdentityReceipt restoredFirst), "first knight restored");
                    Check.Same(firstReceipt.Id, restoredFirst.Id, "first GUID kept");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(secondAgain.Knight, out KnightIdentityReceipt restoredSecond), "second knight restored");
                    Check.Same(secondReceipt.Id, restoredSecond.Id, "second GUID kept");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(squireAgain.Knight, out _), "the Squire never receives a receipt");
                }
            });
        }

        private static void ASquirePromotedAfterCaptureRejectsTheSave()
        {
            Case.Run("a_squire_promoted_after_capture_is_never_excluded_and_rejects_the_save", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit knight = NativeSim.NewKnight(53001);
                    Resolve(knight, 2);
                    KnightUnit squire = NativeSim.NewKnight(53002, tag: "Squire", name: "Squire(Clone)");
                    KnightIdentityRuntime.OnEnable(squire.Knight);

                    IslandSaveData island = new IslandSaveData { land = 6 };
                    NativeSim.RunSave(island, 0, 6, 0, new List<KnightUnit> { knight, squire }, unit =>
                    {
                        if (unit == squire) unit.Go.tag = "Knight"; // 捕获后晋升：排除证据失效
                    });

                    Check.True(Logged("save-member-excluded-changed"), "a promoted Squire can no longer be excluded");
                    Check.False(f.SidecarExists, "the rejected save wrote no sidecar at all");
                }
            });
        }

        private static void UnserializedReferenceAmbiguityDoesNotBlockMemberSave()
        {
            Case.Run("unserialized_reference_ambiguity_does_not_block_a_valid_member_save", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit member = NativeSim.NewKnight(54001);
                    KnightIdentityReceipt receipt = Resolve(member, 4);
                    KnightUnit foreignA = NativeSim.NewKnight(54002);
                    KnightIdentityRuntime.OnEnable(foreignA.Knight);
                    KnightUnit foreignB = NativeSim.NewKnight(54003);
                    KnightIdentityRuntime.OnEnable(foreignB.Knight);

                    IslandSaveData island = new IslandSaveData { land = 6 };
                    NativeSim.RunSave(island, 0, 6, 0, new List<KnightUnit> { member }, _ =>
                    {
                        // 本次 save 为两个未被序列化的引用对象捕获了同一个 uniqueID：可定位且证明不在最终记录里
                        KnightIdentitySaveBridge.HandleGetId(foreignA.Persistent, "ghost-reference");
                        KnightIdentitySaveBridge.HandleGetId(foreignB.Persistent, "ghost-reference");
                    });

                    Check.True(Logged("save-evidence-gap ignored=1"), "non-member ambiguity is reported as ignored");
                    string epoch = Sidecar.ActiveEpoch(f.SidecarPath, 6);
                    Check.True(epoch != null, "the member save was written");
                    KnightIdentityArchive archive = Sidecar.Load(f.SidecarPath);
                    string hash = KnightIdentityFingerprint.Normalized(NativeSim.CloneIsland(island).Json(), epoch);
                    Check.True(archive.TryRestore(epoch, hash, island.objects[0].uniqueID, out KnightIdentityReceipt stored), "member is in the snapshot");
                    Check.Same(receipt.Id, stored.Id, "member GUID preserved");
                }
            });
        }

        private static void MemberReferenceAmbiguityStillBlocksTheSave()
        {
            Case.Run("member_reference_ambiguity_still_blocks_the_save", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit member = NativeSim.NewKnight(55001);
                    Resolve(member, 1);
                    KnightUnit other = NativeSim.NewKnight(55002);
                    KnightIdentityRuntime.OnEnable(other.Knight);

                    IslandSaveData island = new IslandSaveData { land = 6 };
                    NativeSim.RunSave(island, 0, 6, 0, new List<KnightUnit> { member }, _ =>
                    {
                        // 成员 uniqueID 落到两个不同 owner：不可信证据，整份拒写
                        KnightIdentitySaveBridge.HandleGetId(member.Persistent, island.objects[0].uniqueID);
                        KnightIdentitySaveBridge.HandleGetId(other.Persistent, island.objects[0].uniqueID);
                    });

                    Check.True(Logged("save-evidence-gap"), "member ambiguity is still a gap");
                    Check.False(f.SidecarExists, "the conflicting save wrote nothing");
                }
            });
        }

        private static void InvalidMemberUniqueIdIsRejected()
        {
            Case.Run("an_invalid_member_unique_id_is_rejected", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit member = NativeSim.NewKnight(56001);
                    Resolve(member, 0);

                    IslandSaveData island = new IslandSaveData { land = 6 };
                    NativeSim.RunSave(island, 0, 6, 0, new List<KnightUnit> { member }, _ =>
                    {
                        island.objects[0].uniqueID = string.Empty; // 实际成员记录 ID 非法：绝不静默丢人
                    });

                    Check.True(Logged("save-member-invalid-id"), "the invalid member id is rejected");
                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(loaded.Status != KnightIdentityArchiveStatus.Valid
                        || !loaded.Archive.TryGetContext(Sidecar.ContextKey(6), out _), "no snapshot written");
                }
            });
        }

        private static void UnreadableMemberRecordIsRejected()
        {
            Case.Run("an_unreadable_member_record_is_rejected", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit member = NativeSim.NewKnight(57001);
                    Resolve(member, 3);

                    IslandSaveData island = new IslandSaveData { land = 6 };
                    NativeSim.RunSave(island, 0, 6, 0, new List<KnightUnit> { member }, _ =>
                    {
                        island.objects[0].ThrowOnComponentsReadForTests = true; // 未知记录：必须 fail-closed
                    });

                    Check.True(Logged("save-member-record-unreadable"), "the unreadable member record is rejected");
                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(f.SidecarPath);
                    Check.True(loaded.Status != KnightIdentityArchiveStatus.Valid
                        || !loaded.Archive.TryGetContext(Sidecar.ContextKey(6), out _), "no snapshot written");
                }
            });
        }

        private static void CrossTypeDuplicateIdRejectsTheSave()
        {
            Case.Run("a_cross_type_duplicate_id_rejects_the_save", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit member = NativeSim.NewKnight(58001);
                    Resolve(member, 1);

                    IslandSaveData island = new IslandSaveData { land = 6 };
                    NativeSim.RunSave(island, 0, 6, 0, new List<KnightUnit> { member }, _ =>
                    {
                        // 非 Knight 记录复用骑士的 uniqueID：无法证明该 ID 属于哪条记录
                        island.objects.Add(new IslandSaveData.ObjectData
                        {
                            name = "Tree",
                            uniqueID = island.objects[0].uniqueID,
                        });
                    });

                    Check.True(Logged("save-member-cross-type-id"), "cross-type duplicate id rejected");
                    Check.False(f.SidecarExists, "nothing written");
                }
            });
        }

        private static void NonKnightRecordsAreNeverKnightGated()
        {
            Case.Run("non_knight_records_are_never_subject_to_the_knight_id_gate", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit member = NativeSim.NewKnight(59001);
                    KnightIdentityReceipt receipt = Resolve(member, 2);

                    IslandSaveData island = new IslandSaveData { land = 6 };
                    NativeSim.RunSave(island, 0, 6, 0, new List<KnightUnit> { member }, _ =>
                    {
                        // 其他对象类型的 ID 语义不同：非法 ID 不应误伤整份骑士保存
                        island.objects.Add(new IslandSaveData.ObjectData { name = "Tree", uniqueID = string.Empty });
                    });

                    Check.False(Logged("save-member-invalid-id"), "a non-knight invalid id never trips the knight gate");
                    string epoch = Sidecar.ActiveEpoch(f.SidecarPath, 6);
                    Check.True(epoch != null, "the knight save went through");
                    KnightIdentityArchive archive = Sidecar.Load(f.SidecarPath);
                    string hash = KnightIdentityFingerprint.Normalized(NativeSim.CloneIsland(island).Json(), epoch);
                    Check.True(archive.TryRestore(epoch, hash, island.objects[0].uniqueID, out KnightIdentityReceipt stored), "member saved");
                    Check.Same(receipt.Id, stored.Id, "member GUID preserved");
                }
            });
        }

        private static void AmbiguityOnANonKnightRecordIdStillBlocks()
        {
            Case.Run("ambiguity_on_an_id_that_belongs_to_a_non_knight_record_still_blocks", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit member = NativeSim.NewKnight(61001);
                    Resolve(member, 4);
                    KnightUnit foreignA = NativeSim.NewKnight(61002);
                    KnightIdentityRuntime.OnEnable(foreignA.Knight);
                    KnightUnit foreignB = NativeSim.NewKnight(61003);
                    KnightIdentityRuntime.OnEnable(foreignB.Knight);
                    const string sharedId = "shared-non-knight-id";

                    IslandSaveData island = new IslandSaveData { land = 6 };
                    NativeSim.RunSave(island, 0, 6, 0, new List<KnightUnit> { member }, _ =>
                    {
                        // 该 ID 属于最终 records 里的一条非 Knight 记录：歧义绝不能按「不是骑士」忽略
                        island.objects.Add(new IslandSaveData.ObjectData { name = "Tree", uniqueID = sharedId });
                        KnightIdentitySaveBridge.HandleGetId(foreignA.Persistent, sharedId);
                        KnightIdentitySaveBridge.HandleGetId(foreignB.Persistent, sharedId);
                    });

                    Check.True(Logged("save-evidence-gap"), "ambiguity on any final record id blocks the save");
                    Check.False(f.SidecarExists, "nothing written");
                }
            });
        }

        private static KnightIdentityReceipt Resolve(KnightUnit unit, int existingStyle)
        {
            KnightIdentityRuntime.OnEnable(unit.Knight);
            Check.True(KnightIdentityRuntime.TryResolve(unit.Knight, existingStyle, 3u, Styles.All, out _), "resolve " + unit.Go.name);
            Check.True(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt receipt), "receipt " + unit.Go.name);
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
