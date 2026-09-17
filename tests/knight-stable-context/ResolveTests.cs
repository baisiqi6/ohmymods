using System;
using KingdomEnhancedMod;

namespace KnightStableContextTests
{
    /// <summary>稳定上下文解析的全部保守边界：精确匹配、时钟漂移、legacy 迁移、冲突、回退与 fail closed。</summary>
    internal static class ResolveTests
    {
        internal static void Run()
        {
            ContextKeyIsStableAndSeparatesIslands();
            FreshContextAllocatesOneOpaqueEpoch();
            SameIslandSameContentRestoresSameReceipts();
            ClockDriftMatchesButObjectChangeDoesNot();
            LegacyExactMatchClaimsAndRestoresOriginalReceipts();
            DisagreeingExactMatchesAreUnresolved();
            AgreeingMatchesPickDeterministicallyWithKindPreference();
            ClaimedScopeIsNeverMigratedIntoAnotherContext();
            KnownContextMismatchIsUnresolved();
            UnclaimedHistoryBlocksAFreshContext();
            RollbackToAnOlderRecordedSnapshotRestoresThatGeneration();
            MatchedEmptySnapshotRestoresNothingButIsNotUnresolved();
            InvalidOrUnusableInputsAreUnresolved();
            ResolveNeverMutatesTheArchive();
            OlderEpochHistoryAndWrongKindStayUnresolved();
        }

        private static void OlderEpochHistoryAndWrongKindStayUnresolved()
        {
            Case.Run("empty_active_epoch_does_not_hide_older_history", () =>
            {
                string context = Build.Context(9), old = Build.Hex64(21), active = Build.Hex64(22);
                var archive = Build.Archive(old, Build.Snapshot(2, Build.Hex64(23), Build.E("old", 21, 2)));
                archive.RecordSnapshot(active, Build.Snapshot(2, Build.Hex64(24)));
                Check.True(archive.EnsureContext(context, old, true), "old epoch registered");
                Check.True(archive.EnsureContext(context, active, true), "empty active epoch registered");
                Check.True(KnightIdentityContexts.Resolve(archive, context, Build.IslandJson("changed")).Unresolved, "older nonempty history blocks unmatched population");
            });
            Case.Run("snapshot_kind_must_match_the_hash_recipe", () =>
            {
                string scope = Build.Hex64(25), json = Build.IslandJson("A");
                var archive = Build.Archive(scope, Build.Snapshot(2, KnightIdentityFingerprint.Sha256(json, scope), Build.E("old", 22, 1)));
                archive.RecordSnapshot(scope, Build.Snapshot(1, Build.Hex64(26)));
                Check.True(KnightIdentityContexts.Resolve(archive, Build.Context(9), json).Unresolved, "legacy digest does not claim a kind2 receipt");
            });
        }

        private static void ContextKeyIsStableAndSeparatesIslands()
        {
            Case.Run("context_key_is_stable_and_separates_file_campaign_challenge_land", () =>
            {
                string baseline = KnightIdentityArchive.ContextKey("slot-1.save", 0, 0, 9);
                Check.Equal(KnightIdentityFingerprint.HexLength, baseline.Length, "context key is 64 chars");
                Check.Equal(baseline, KnightIdentityArchive.ContextKey("slot-1.save", 0, 0, 9), "same identity is stable");
                Check.NotEqual(baseline, KnightIdentityArchive.ContextKey("slot-2.save", 0, 0, 9), "another file is another context");
                Check.NotEqual(baseline, KnightIdentityArchive.ContextKey("slot-1.save", 1, 0, 9), "another campaign is another context");
                Check.NotEqual(baseline, KnightIdentityArchive.ContextKey("slot-1.save", 0, 1, 9), "another challenge is another context");
                Check.NotEqual(baseline, KnightIdentityArchive.ContextKey("slot-1.save", 0, 0, 8), "another land is another context");
                Check.True(KnightIdentitySidecarFingerprintIsHex(baseline), "context key is lowercase hex");
            });
        }

        private static void FreshContextAllocatesOneOpaqueEpoch()
        {
            Case.Run("fresh_context_gets_one_opaque_epoch_and_a_kind2_fresh_hash", () =>
            {
                KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
                string json = Build.IslandJson("A");
                KnightIdentityResolution resolution = KnightIdentityContexts.Resolve(archive, Build.Context(9), json);
                Check.Equal("fresh", resolution.Kind, "empty archive means fresh");
                Check.False(resolution.Unresolved, "fresh is writable");
                Check.True(resolution.NewEpoch, "epoch is not owned yet");
                Check.True(KnightIdentityFingerprint.IsHex64(resolution.Epoch), "epoch is a 64-hex opaque scope");
                Check.False(resolution.Epoch == Build.Hex64(1), "epoch is not derived from any constant");
                Check.True(resolution.Receipts == null, "nothing to restore");
                Check.Equal(KnightIdentityFingerprint.Normalized(json, resolution.Epoch), resolution.FreshHash, "fresh hash is the kind2 fingerprint under the new epoch");
            });
        }

        private static void SameIslandSameContentRestoresSameReceipts()
        {
            Case.Run("same_island_content_restores_the_same_receipts", () =>
            {
                string json = Build.IslandJson("A");
                string scope = Build.Hex64(1);
                KnightIdentityArchive archive = Build.Archive(scope,
                    Build.Snapshot(KnightIdentityFingerprint.KindNormalized, KnightIdentityFingerprint.Normalized(json, scope), Build.E("k-1", 1, 3)));
                string contextKey = Build.Context(9);

                KnightIdentityResolution first = KnightIdentityContexts.Resolve(archive, contextKey, json);
                KnightIdentityResolution second = KnightIdentityContexts.Resolve(archive, contextKey, json);
                Check.Equal("exact", first.Kind, "exact match");
                Check.False(first.Unresolved, "exact match is not unresolved");
                Check.Equal(scope, first.Epoch, "matched epoch");
                Check.True(first.NewEpoch, "unclaimed scope must be claimed");
                Check.True(first.Receipts.TryGetValue("k-1", out KnightIdentityReceipt receipt), "receipt restored");
                Check.Same(Build.Seed(1), receipt.Id, "guid restored");
                Check.Equal(3, receipt.Style, "style restored");
                Check.Equal(first.Epoch, second.Epoch, "same content resolves to the same epoch");
                Check.Same(receipt.Id, second.Receipts["k-1"].Id, "same content resolves to the same guid");
            });
        }

        private static void ClockDriftMatchesButObjectChangeDoesNot()
        {
            Case.Run("top_level_clock_drift_matches_while_any_object_change_is_rejected", () =>
            {
                string recorded = Build.IslandJson("A", 10, 9, 5000);
                string scope = Build.Hex64(2);
                KnightIdentityArchive archive = Build.Archive(scope,
                    Build.Snapshot(KnightIdentityFingerprint.KindNormalized, KnightIdentityFingerprint.Normalized(recorded, scope), Build.E("k-1", 2, 1)));
                string contextKey = Build.Context(9);

                string drifted = Build.IslandJson("A", 12.5, 11.25, 5900);
                KnightIdentityResolution match = KnightIdentityContexts.Resolve(archive, contextKey, drifted);
                Check.Equal("exact", match.Kind, "only the three clocks moved: exact match");
                Check.False(match.Unresolved, "clock drift is not a conflict");

                string changed = Build.IslandJson("B", 10, 9, 5000);
                KnightIdentityResolution mismatch = KnightIdentityContexts.Resolve(archive, contextKey, changed);
                Check.Equal("legacy-pending", mismatch.Kind, "object change never matches");
                Check.True(mismatch.Unresolved, "object change with unmatched history is unresolved");
                Check.True(mismatch.Receipts == null, "nothing restored on an object change");
            });
        }

        private static void LegacyExactMatchClaimsAndRestoresOriginalReceipts()
        {
            Case.Run("legacy_raw_snapshot_is_migrated_by_exact_match_and_keeps_guid_and_style", () =>
            {
                string json = Build.IslandJson("A");
                string legacyScope = Build.Hex64(3);
                string legacyHash = KnightIdentityFingerprint.Sha256(json, legacyScope);
                KnightIdentityArchive archive = Build.Archive(legacyScope,
                    Build.Snapshot(KnightIdentityFingerprint.KindLegacy, legacyHash, Build.E("k-1", 3, 4)));
                string contextKey = Build.Context(9);

                KnightIdentityResolution resolution = KnightIdentityContexts.Resolve(archive, contextKey, json);
                Check.Equal("legacy", resolution.Kind, "legacy snapshot matched with the legacy hash");
                Check.Equal(legacyScope, resolution.Epoch, "matched legacy scope becomes the epoch");
                Check.Equal(KnightIdentityFingerprint.KindLegacy, resolution.MatchKind, "match kind is legacy");
                Check.Equal(legacyHash, resolution.MatchHash, "stored hash is reported");
                Check.True(resolution.NewEpoch, "legacy scope is not owned yet");
                Check.Same(Build.Seed(3), resolution.Receipts["k-1"].Id, "legacy guid preserved");
                Check.Equal(4, resolution.Receipts["k-1"].Style, "legacy style preserved");
            });
        }

        private static void DisagreeingExactMatchesAreUnresolved()
        {
            Case.Run("two_disagreeing_exact_matches_are_unresolved_and_write_nothing", () =>
            {
                string json = Build.IslandJson("A");
                string scopeA = Build.Hex64(4);
                string scopeB = Build.Hex64(5);
                KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
                archive.RecordSnapshot(scopeA, Build.Snapshot(KnightIdentityFingerprint.KindLegacy, KnightIdentityFingerprint.Sha256(json, scopeA), Build.E("k-1", 4, 1)));
                archive.RecordSnapshot(scopeB, Build.Snapshot(KnightIdentityFingerprint.KindLegacy, KnightIdentityFingerprint.Sha256(json, scopeB), Build.E("k-1", 5, 2)));
                string contextKey = Build.Context(9);

                KnightIdentityResolution resolution = KnightIdentityContexts.Resolve(archive, contextKey, json);
                Check.Equal("conflict", resolution.Kind, "conflicting histories quarantine");
                Check.True(resolution.Unresolved, "conflict is unresolved");
                Check.True(resolution.Epoch == null, "no epoch is offered");
                Check.True(resolution.Receipts == null, "no receipts are restored");
                Check.True(resolution.FreshHash == null, "no fresh hash is offered");
            });
        }

        private static void AgreeingMatchesPickDeterministicallyWithKindPreference()
        {
            Case.Run("agreeing_matches_pick_deterministically_with_kind2_preference", () =>
            {
                string json = Build.IslandJson("A");
                string scopeA = Build.Hex64(6);
                string scopeB = Build.Hex64(7);
                KnightIdentitySnapshotEntry[] entries = { Build.E("k-1", 6, 0) };
                KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
                archive.RecordSnapshot(scopeB, Build.Snapshot(KnightIdentityFingerprint.KindLegacy, KnightIdentityFingerprint.Sha256(json, scopeB), entries));
                archive.RecordSnapshot(scopeA, Build.Snapshot(KnightIdentityFingerprint.KindLegacy, KnightIdentityFingerprint.Sha256(json, scopeA), entries));
                KnightIdentityResolution legacyPick = KnightIdentityContexts.Resolve(archive, Build.Context(9), json);
                Check.Equal(scopeA, legacyPick.Epoch, "ordinal order breaks the tie deterministically");

                archive.RecordSnapshot(scopeB, Build.Snapshot(KnightIdentityFingerprint.KindNormalized, KnightIdentityFingerprint.Normalized(json, scopeB), entries));
                KnightIdentityResolution kind2Pick = KnightIdentityContexts.Resolve(archive, Build.Context(9), json);
                Check.Equal(scopeB, kind2Pick.Epoch, "the clock-independent kind2 match wins the tie");
                Check.Equal(KnightIdentityFingerprint.KindNormalized, kind2Pick.MatchKind, "kind2 reported");
            });
        }

        private static void ClaimedScopeIsNeverMigratedIntoAnotherContext()
        {
            Case.Run("a_scope_claimed_by_one_context_is_never_migrated_into_another", () =>
            {
                string json = Build.IslandJson("A");
                string scope = Build.Hex64(8);
                KnightIdentityArchive archive = Build.Archive(scope,
                    Build.Snapshot(KnightIdentityFingerprint.KindNormalized, KnightIdentityFingerprint.Normalized(json, scope), Build.E("k-1", 8, 2)));
                Check.True(archive.EnsureContext(Build.Context(9), scope, true), "first context claims the scope");

                KnightIdentityResolution other = KnightIdentityContexts.Resolve(archive, Build.Context(10), json);
                Check.Equal("fresh", other.Kind, "claimed scope is not a migration source for another context");
                Check.NotEqual(scope, other.Epoch, "another context never reuses the claimed epoch");
                Check.True(other.NewEpoch, "the other context gets its own epoch");
            });
        }

        private static void KnownContextMismatchIsUnresolved()
        {
            Case.Run("known_context_that_never_recorded_this_snapshot_is_unresolved", () =>
            {
                string recorded = Build.IslandJson("A");
                string drifted = Build.IslandJson("B");
                string scope = Build.Hex64(11);
                KnightIdentityArchive archive = Build.Archive(scope,
                    Build.Snapshot(KnightIdentityFingerprint.KindNormalized, KnightIdentityFingerprint.Normalized(recorded, scope), Build.E("k-1", 11, 1)));
                string contextKey = Build.Context(9);
                Check.True(archive.EnsureContext(contextKey, scope, true), "context owns the recorded epoch");

                KnightIdentityResolution resolution = KnightIdentityContexts.Resolve(archive, contextKey, drifted);
                Check.Equal("known-mismatch", resolution.Kind, "known history mismatch");
                Check.True(resolution.Unresolved, "unresolved: LoadSeed must not overwrite known history");
                Check.True(resolution.Epoch == null, "no epoch offered for the mismatched snapshot");
            });
        }

        private static void UnclaimedHistoryBlocksAFreshContext()
        {
            Case.Run("unclaimed_history_blocks_a_fresh_context_until_exact_evidence", () =>
            {
                string json = Build.IslandJson("A");
                string scope = Build.Hex64(12);
                KnightIdentityArchive archive = Build.Archive(scope,
                    Build.Snapshot(KnightIdentityFingerprint.KindLegacy, KnightIdentityFingerprint.Sha256("other island json", scope), Build.E("old", 12, 2)));

                KnightIdentityResolution blocked = KnightIdentityContexts.Resolve(archive, Build.Context(9), json);
                Check.Equal("legacy-pending", blocked.Kind, "unclaimed history blocks a fresh claim");
                Check.True(blocked.Unresolved, "fail closed while unclaimed history remains");
                Check.True(blocked.Epoch == null, "no epoch offered");

                // 空条目历史不阻塞（没有身份可丢）
                KnightIdentityArchive emptyHistory = KnightIdentityArchive.CreateEmpty();
                emptyHistory.RecordSnapshot(Build.Hex64(13), Build.Snapshot(KnightIdentityFingerprint.KindLegacy, Build.Hex64(14)));
                KnightIdentityResolution allowed = KnightIdentityContexts.Resolve(emptyHistory, Build.Context(9), json);
                Check.Equal("fresh", allowed.Kind, "empty history does not block a genuine new island");
                Check.False(allowed.Unresolved, "fresh context is writable");
            });
        }

        private static void RollbackToAnOlderRecordedSnapshotRestoresThatGeneration()
        {
            Case.Run("native_rollback_to_an_older_recorded_snapshot_restores_that_generation", () =>
            {
                string older = Build.IslandJson("A");
                string newer = Build.IslandJson("B");
                string scope = Build.Hex64(15);
                KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
                archive.RecordSnapshot(scope,
                    Build.Snapshot(KnightIdentityFingerprint.KindNormalized, KnightIdentityFingerprint.Normalized(older, scope), Build.E("k-old", 15, 0)));
                archive.RecordSnapshot(scope,
                    Build.Snapshot(KnightIdentityFingerprint.KindNormalized, KnightIdentityFingerprint.Normalized(newer, scope), Build.E("k-new", 16, 1)));
                string contextKey = Build.Context(9);
                Check.True(archive.EnsureContext(contextKey, scope, true), "context owns the epoch");

                KnightIdentityResolution rollback = KnightIdentityContexts.Resolve(archive, contextKey, older);
                Check.Equal("exact", rollback.Kind, "older content still matches its recorded snapshot");
                Check.False(rollback.NewEpoch, "epoch already owned");
                Check.Same(Build.Seed(15), rollback.Receipts["k-old"].Id, "the older generation's guid is restored");
                Check.False(rollback.Receipts.ContainsKey("k-new"), "the newer generation is not mixed in");
            });
        }

        private static void MatchedEmptySnapshotRestoresNothingButIsNotUnresolved()
        {
            Case.Run("an_exact_empty_snapshot_matches_without_receipts_and_is_not_unresolved", () =>
            {
                string json = Build.IslandJson("A");
                string scope = Build.Hex64(16);
                KnightIdentityArchive archive = Build.Archive(scope,
                    Build.Snapshot(KnightIdentityFingerprint.KindNormalized, KnightIdentityFingerprint.Normalized(json, scope)));

                KnightIdentityResolution resolution = KnightIdentityContexts.Resolve(archive, Build.Context(9), json);
                Check.Equal("exact", resolution.Kind, "empty snapshot is still an exact match");
                Check.False(resolution.Unresolved, "an exact empty match is authoritative, not unresolved");
                Check.True(resolution.Receipts != null, "receipts dictionary is present");
                Check.Equal(0, resolution.Receipts.Count, "nothing to restore");
                Check.True(resolution.FreshHash == null, "no fresh snapshot is offered");
            });
        }

        private static void InvalidOrUnusableInputsAreUnresolved()
        {
            Case.Run("invalid_or_unusable_inputs_fail_closed", () =>
            {
                string json = Build.IslandJson("A");
                Check.True(KnightIdentityContexts.Resolve(null, Build.Context(9), json).Unresolved, "null archive is unresolved");
                Check.True(KnightIdentityContexts.Resolve(KnightIdentityArchive.CreateEmpty(), "not-hex", json).Unresolved, "bad context key is unresolved");
                Check.True(KnightIdentityContexts.Resolve(KnightIdentityArchive.CreateEmpty(), Build.Context(9), null).Unresolved, "null json is unresolved");
                Check.Equal("unusable-json", KnightIdentityContexts.Resolve(KnightIdentityArchive.CreateEmpty(), Build.Context(9), "[1,2,3]").Kind,
                    "a non-object island payload is unusable, not fresh");
                Check.True(KnightIdentityContexts.Resolve(KnightIdentityArchive.CreateEmpty(), Build.Context(9), "[]").Unresolved, "unusable json is unresolved");
            });
        }

        private static void ResolveNeverMutatesTheArchive()
        {
            Case.Run("resolve_never_mutates_the_archive", () =>
            {
                string json = Build.IslandJson("A");
                string scope = Build.Hex64(17);
                KnightIdentityArchive archive = Build.Archive(scope,
                    Build.Snapshot(KnightIdentityFingerprint.KindNormalized, KnightIdentityFingerprint.Normalized(json, scope), Build.E("k-1", 17, 1)));
                byte[] before = archive.SerializeToUtf8();

                KnightIdentityContexts.Resolve(archive, Build.Context(9), json);
                KnightIdentityContexts.Resolve(archive, Build.Context(10), Build.IslandJson("B"));
                KnightIdentityContexts.Resolve(archive, Build.Context(11), "[broken]");
                Check.True(before.AsSpan().SequenceEqual(archive.SerializeToUtf8()), "resolution is read-only");
                Check.Equal(1, archive.ScopeCount, "no scope was added");
                Check.Equal(0, archive.ContextCount, "no context was registered");
            });
        }

        private static bool KnightIdentitySidecarFingerprintIsHex(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return value.Length > 0;
        }
    }
}
