using System;
using KingdomEnhancedMod;

namespace KnightIdentityArchiveTests
{
    /// <summary>纯模型：收据校验、快照去重、按 scope 保留 8 代、容量拒绝、严格恢复。</summary>
    internal static class ModelTests
    {
        internal static void Run()
        {
            Console.WriteLine("Model (receipt / snapshot / retention / capacity / restore)");

            Case.Run("model.receiptRejectsEmptyGuidAndStyleOutside0To4", () =>
            {
                for (int style = 0; style <= 4; style++)
                {
                    Check.True(new KnightIdentityReceipt(Build.Seed(1), style).IsValid, "style " + style + " must be valid");
                }
                Check.False(new KnightIdentityReceipt(Guid.Empty, 0).IsValid, "empty GUID rejected");
                Check.False(new KnightIdentityReceipt(Build.Seed(1), -1).IsValid, "style -1 rejected");
                Check.False(new KnightIdentityReceipt(Build.Seed(1), 5).IsValid, "style 5 rejected");
                Check.False(KnightIdentityReceipt.IsValidStyle(int.MaxValue), "style int.MaxValue rejected");
                Check.Equal(5, KnightIdentityReceipt.StyleCount, "style space is 0..4");
            });

            Case.Run("model.snapshotRejectsBadHashDuplicateKeyDuplicateGuidAndLongIds", () =>
            {
                string hash = Build.Hex('a');
                DateTimeOffset stamp = DateTimeOffset.UnixEpoch;

                Check.True(KnightIdentitySnapshot.TryCreate(hash, stamp, new[] { Build.E("k1", 1, 0) }, out _, out _), "baseline valid");
                Check.False(KnightIdentitySnapshot.TryCreate(Build.Hex('a').Substring(0, 63), stamp, Array.Empty<KnightIdentitySnapshotEntry>(), out _, out _), "63-hex hash rejected");
                Check.False(KnightIdentitySnapshot.TryCreate("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz", stamp, Array.Empty<KnightIdentitySnapshotEntry>(), out _, out _), "non-hex hash rejected");
                Check.False(KnightIdentitySnapshot.TryCreate(null, stamp, Array.Empty<KnightIdentitySnapshotEntry>(), out _, out _), "null hash rejected");

                Check.False(KnightIdentitySnapshot.TryCreate(hash, stamp, new[] { Build.E("k1", 1, 0), Build.E("k1", 2, 1) }, out _, out string duplicateKey), "duplicate uniqueID rejected");
                Check.Contains(duplicateKey, "duplicate uniqueID", "reason names the key collision");
                Check.False(KnightIdentitySnapshot.TryCreate(hash, stamp, new[] { Build.E("k1", 1, 0), Build.E("k2", 1, 1) }, out _, out string duplicateId), "duplicate GUID rejected");
                Check.Contains(duplicateId, "duplicate GUID", "reason names the GUID collision");

                Check.False(KnightIdentitySnapshot.TryCreate(hash, stamp, new[] { Build.E(string.Empty, 1, 0) }, out _, out _), "empty uniqueID rejected");
                Check.False(KnightIdentitySnapshot.TryCreate(hash, stamp, new[] { Build.E(new string('x', 257), 1, 0) }, out _, out _), "257-char uniqueID rejected");
                Check.True(KnightIdentitySnapshot.TryCreate(hash, stamp, new[] { Build.E(new string('x', 256), 1, 0) }, out _, out _), "256-char uniqueID allowed");

                Check.False(KnightIdentitySnapshot.TryCreate(hash, stamp, new[] { new KnightIdentitySnapshotEntry("k1", new KnightIdentityReceipt(Guid.Empty, 0)) }, out _, out _), "empty receipt GUID rejected");
                Check.False(KnightIdentitySnapshot.TryCreate(hash, stamp, new[] { new KnightIdentitySnapshotEntry("k1", new KnightIdentityReceipt(Build.Seed(1), 5)) }, out _, out _), "receipt style 5 rejected");
                Check.False(KnightIdentitySnapshot.TryCreate(hash, stamp, new[] { new KnightIdentitySnapshotEntry(null, new KnightIdentityReceipt(Build.Seed(1), 0)) }, out _, out _), "null uniqueID rejected");
            });

            Case.Run("model.snapshotEnforcesEntryCap512AndNormalizesHash", () =>
            {
                KnightIdentitySnapshotEntry[] atCap = Entries(512);
                Check.True(KnightIdentitySnapshot.TryCreate(Build.Hex('a'), DateTimeOffset.UnixEpoch, atCap, out KnightIdentitySnapshot capped, out _), "512 entries allowed");
                Check.False(KnightIdentitySnapshot.TryCreate(Build.Hex('a'), DateTimeOffset.UnixEpoch, Entries(513), out _, out _), "513 entries rejected");
                Check.Equal(Build.Hex('a'), capped.Hash, "hash stored as given");
                Check.True(KnightIdentitySnapshot.TryCreate(Build.Hex('a').ToUpperInvariant().Replace('A', 'B'), DateTimeOffset.UnixEpoch, atCap, out KnightIdentitySnapshot upper, out _), "uppercase hash accepted");
                Check.Equal(Build.Hex('b'), upper.Hash, "uppercase hash normalized to lowercase");
                Check.False(KnightIdentitySnapshot.TryCreate(Build.Hex('a'), DateTimeOffset.UnixEpoch, null, out _, out _), "null entries rejected");
            });

            Case.Run("model.snapshotHashIsBindingEvenWhenItCountsAreRebalanced", () =>
            {
                int[] counts = { 100, 0, 0, 0, 0 };
                int pickedForNewRecruit = KnightIdentityBalance.ChooseLeast(counts, new[] { 0, 1, 2 }, 0);
                Check.True(pickedForNewRecruit <= 2, "new recruit must respect available resource types");

                KnightIdentityArchive archive = Build.Archive(Build.Hex('a'), Build.Hex('b'), "knight-7", 1, 3);
                Check.True(archive.TryRestore(Build.Hex('a'), Build.Hex('b'), "knight-7", out KnightIdentityReceipt kept), "existing receipt still restorable");
                Check.Equal(Build.Seed(1), kept.Id, "existing GUID unchanged by counts/availability");
                Check.Equal(3, kept.Style, "existing style unchanged by counts/availability");
            });

            Case.Run("model.sameHashIsIdempotentWhileConflictingContentIsRejected", () =>
            {
                KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
                string scope = Build.Hex('a');
                string hash = Build.Hex('b');

                Check.Equal(KnightIdentityArchive.MutationStatus.Applied, archive.RecordSnapshot(scope, Build.Snapshot(hash, Build.E("k1", 1, 2))), "first record applies");
                Check.Equal(KnightIdentityArchive.MutationStatus.Unchanged, archive.RecordSnapshot(scope, Build.Snapshot(hash, Build.E("k1", 1, 2))), "equal content is idempotent");
                Check.True(archive.TryGetSnapshots(scope, out var snapshots), "scope exists");
                Check.Equal(1, snapshots.Count, "no duplicate generation for one hash");
                Check.Equal(1, archive.ScopeCount, "single scope");

                byte[] before = archive.SerializeToUtf8();
                Check.Equal(
                    KnightIdentityArchive.MutationStatus.RejectedConflict,
                    archive.RecordSnapshot(scope, Build.Snapshot(hash, DateTimeOffset.UnixEpoch.AddMinutes(1), new[] { Build.E("k1", 9, 3) })),
                    "same hash with different content is a conflict");
                Check.Equal(1, snapshots.Count, "conflict does not add a generation");
                Check.True(archive.TryRestore(scope, hash, "k1", out KnightIdentityReceipt original), "original receipt still restorable");
                Check.Equal(Build.Seed(1), original.Id, "conflicting record never overwrote the original GUID");
                Check.Equal(2, original.Style, "conflicting record never overwrote the original style");
                Check.True(before.AsSpan().SequenceEqual(archive.SerializeToUtf8()), "conflict leaves bytes identical");

                // 同一条记录重新上报（内容相同、时间戳更新、且已不是最近一代）：只刷新顺序，不新增条目。
                Check.Equal(KnightIdentityArchive.MutationStatus.Applied, archive.RecordSnapshot(scope, Build.Snapshot(Build.Hex('c'), Build.E("k2", 2, 4))), "second generation");
                Check.Equal(
                    KnightIdentityArchive.MutationStatus.Applied,
                    archive.RecordSnapshot(scope, Build.Snapshot(hash, DateTimeOffset.UnixEpoch.AddMinutes(2), new[] { Build.E("k1", 1, 2) })),
                    "re-reporting a known snapshot refreshes it");
                Check.Equal(2, snapshots.Count, "no new generation for a known hash");
                Check.Equal(hash, snapshots[0].Hash, "re-reported snapshot is the most recent again");
                Check.Equal(2, archive.TotalEntryCount, "entry total unchanged by the refresh");
            });

            Case.Run("model.keepsNewest8PerScopeAndEvictsOnlyOwnHistory", () =>
            {
                KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
                string scopeA = Build.Hex64(1);
                string scopeB = Build.Hex64(2);
                archive.RecordSnapshot(scopeB, Generation(Build.Hex('f'), 99));

                for (int generation = 0; generation < 9; generation++)
                {
                    Check.Equal(
                        KnightIdentityArchive.MutationStatus.Applied,
                        archive.RecordSnapshot(scopeA, Generation(Build.Hex((char)('0' + generation)), generation)),
                        "generation " + generation + " applies");
                }

                Check.True(archive.TryGetSnapshots(scopeA, out var scoped), "scopeA exists");
                Check.Equal(KnightIdentityArchive.MaxSnapshotsPerScope, scoped.Count, "keeps 8 generations");
                Check.False(archive.TryGetSnapshot(scopeA, Build.Hex('0'), out _), "oldest generation evicted");
                Check.True(archive.TryGetSnapshot(scopeA, Build.Hex('1'), out _), "2nd oldest kept");
                Check.True(archive.TryGetSnapshot(scopeA, Build.Hex('8'), out _), "newest kept");
                Check.True(archive.TryGetSnapshot(scopeB, Build.Hex('f'), out _), "other scope untouched");
                Check.Equal(8 * 5 + 5, archive.TotalEntryCount, "only scopeA history shrank");
                Check.True(archive.TryRestore(scopeA, Build.Hex('8'), "k0", out _), "newest generation restorable");
            });

            Case.Run("model.scopeCapRejectsInsteadOfDeletingOtherScopes", () =>
            {
                KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
                for (int scope = 0; scope < KnightIdentityArchive.MaxScopes; scope++)
                {
                    Check.Equal(
                        KnightIdentityArchive.MutationStatus.Applied,
                        archive.RecordSnapshot(Build.Hex64(scope), Build.Snapshot(Build.Hex('a'), Build.E("k", scope, 0))),
                        "scope " + scope + " applies");
                }

                Check.Equal(KnightIdentityArchive.MaxScopes, archive.ScopeCount, "at scope cap");
                Check.Equal(
                    KnightIdentityArchive.MutationStatus.RejectedCapacity,
                    archive.RecordSnapshot(Build.Hex64(1000), Build.Snapshot(Build.Hex('a'), Build.E("k", 1, 0))),
                    "129th scope rejected");
                Check.Equal(KnightIdentityArchive.MaxScopes, archive.ScopeCount, "no scope evicted");
                Check.True(archive.TryGetSnapshot(Build.Hex64(0), Build.Hex('a'), out _), "first scope still there");
                Check.Equal(
                    KnightIdentityArchive.MutationStatus.Applied,
                    archive.RecordSnapshot(Build.Hex64(0), Build.Snapshot(Build.Hex('b'), Build.E("k", 1, 1))),
                    "existing scope still writable at cap");
                Check.Equal(
                    KnightIdentityArchive.MutationStatus.RejectedInvalid,
                    archive.RecordSnapshot("not-a-hash", Build.Snapshot(Build.Hex('a'), Build.E("k", 1, 0))),
                    "bad scope key rejected");
                Check.Equal(
                    KnightIdentityArchive.MutationStatus.RejectedInvalid,
                    archive.RecordSnapshot(Build.Hex('c'), null),
                    "null snapshot rejected");
            });

            Case.Run("model.totalEntryCapRejectsWithoutCrossScopeEviction", () =>
            {
                KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
                for (int scope = 0; scope < KnightIdentityArchive.MaxScopes; scope++)
                {
                    for (int generation = 0; generation < KnightIdentityArchive.MaxSnapshotsPerScope; generation++)
                    {
                        Check.Equal(
                            KnightIdentityArchive.MutationStatus.Applied,
                            archive.RecordSnapshot(Build.Hex64(scope), Entries32(Build.Hex64(100 + generation))),
                            "scope " + scope + " gen " + generation);
                    }
                }

                Check.Equal(KnightIdentityArchive.MaxTotalEntries, archive.TotalEntryCount, "exactly at total cap");
                Check.Equal(
                    KnightIdentityArchive.MutationStatus.RejectedCapacity,
                    archive.RecordSnapshot(Build.Hex64(500), Build.Snapshot(Build.Hex64(900), Build.E("x", 1, 0))),
                    "new scope above total cap rejected");
                Check.Equal(KnightIdentityArchive.MaxScopes, archive.ScopeCount, "no scope deleted above total cap");
                Check.True(archive.TryGetSnapshots(Build.Hex64(0), out var scoped) && scoped.Count == KnightIdentityArchive.MaxSnapshotsPerScope, "victim scope history intact");

                Check.Equal(
                    KnightIdentityArchive.MutationStatus.RejectedCapacity,
                    archive.RecordSnapshot(Build.Hex64(1), Entries32(Build.Hex64(900), 33)),
                    "33 entries replacing 32 exceeds cap and is rejected");
                Check.Equal(
                    KnightIdentityArchive.MutationStatus.Applied,
                    archive.RecordSnapshot(Build.Hex64(1), Build.Snapshot(Build.Hex64(950), Build.E("z", 1, 0))),
                    "a new smaller generation may replace the oldest one of the same scope");
                Check.Equal(KnightIdentityArchive.MaxTotalEntries - 31, archive.TotalEntryCount, "shrink frees entries");
                Check.False(archive.TryGetSnapshot(Build.Hex64(1), Build.Hex64(100), out _), "the evicted generation is gone");
            });

            Case.Run("model.restoreRequiresExactScopeAndHashNeverGuesses", () =>
            {
                KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
                string scopeA = Build.Hex64(1);
                string scopeB = Build.Hex64(2);
                string hashX = Build.Hex('a');
                string hashY = Build.Hex('b');
                archive.RecordSnapshot(scopeA, Build.Snapshot(hashX, Build.E("knight-7", 1, 3)));
                archive.RecordSnapshot(scopeA, Build.Snapshot(hashY, Build.E("knight-7", 2, 1)));

                Check.True(archive.TryRestore(scopeA, hashX, "knight-7", out KnightIdentityReceipt first), "exact scope+hash restores");
                Check.Equal(Build.Seed(1), first.Id, "first generation GUID");
                Check.True(archive.TryRestore(scopeA, hashY, "knight-7", out KnightIdentityReceipt second), "same uniqueID under another snapshot");
                Check.Equal(Build.Seed(2), second.Id, "snapshot-bound GUID");

                Check.False(archive.TryRestore(scopeA, Build.Hex('c'), "knight-7", out _), "native resave (unknown hash) refused");
                Check.False(archive.TryRestore(scopeB, hashX, "knight-7", out _), "wrong scope refused");
                Check.False(archive.TryRestore(scopeA, hashX, "knight-8", out _), "unknown uniqueID refused");
                Check.False(archive.TryRestore("not-hex", hashX, "knight-7", out _), "bad scope key refused");
                Check.False(archive.TryRestore(scopeA, hashX, new string('n', 257), out _), "overlong uniqueID refused");
                Check.False(archive.TryRestore(scopeA, hashX, null, out _), "null uniqueID refused");
                Check.True(archive.TryRestore(scopeA, hashX.ToUpperInvariant(), "knight-7", out _), "hash casing normalized");
            });

            Case.Run("model.serializationOrderIsCanonical", () =>
            {
                KnightIdentityArchive left = KnightIdentityArchive.CreateEmpty();
                KnightIdentityArchive right = KnightIdentityArchive.CreateEmpty();
                left.RecordSnapshot(Build.Hex64(1), Build.Snapshot(Build.Hex('b'), Build.E("b", 2, 0), Build.E("a", 1, 1)));
                left.RecordSnapshot(Build.Hex64(2), Build.Snapshot(Build.Hex('c'), Build.E("k", 3, 2)));
                right.RecordSnapshot(Build.Hex64(2), Build.Snapshot(Build.Hex('c'), Build.E("k", 3, 2)));
                right.RecordSnapshot(Build.Hex64(1), Build.Snapshot(Build.Hex('b'), Build.E("a", 1, 1), Build.E("b", 2, 0)));

                Check.True(left.SerializeToUtf8().AsSpan().SequenceEqual(right.SerializeToUtf8()), "insertion order must not change bytes");
            });
        }

        private static KnightIdentitySnapshotEntry[] Entries(int count)
        {
            KnightIdentitySnapshotEntry[] entries = new KnightIdentitySnapshotEntry[count];
            for (int i = 0; i < count; i++) entries[i] = Build.E("k" + i, i, i % KnightIdentityReceipt.StyleCount);
            return entries;
        }

        /// <summary>一代 5 个骑士：k0..k4，风格 0..4。</summary>
        private static KnightIdentitySnapshot Generation(string hash, int generation)
        {
            KnightIdentitySnapshotEntry[] entries = new KnightIdentitySnapshotEntry[5];
            for (int i = 0; i < entries.Length; i++) entries[i] = Build.E("k" + i, generation * 10 + i, i);
            return Build.Snapshot(hash, entries);
        }

        /// <summary>一代 32 个骑士（默认）以压满总条目上限。</summary>
        private static KnightIdentitySnapshot Entries32(string hash, int count = 32)
        {
            KnightIdentitySnapshotEntry[] entries = new KnightIdentitySnapshotEntry[count];
            for (int i = 0; i < count; i++) entries[i] = Build.E("k" + i, i, i % KnightIdentityReceipt.StyleCount);
            return Build.Snapshot(hash, entries);
        }
    }
}
