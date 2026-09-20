using System;
using System.IO;
using System.Text;
using System.Threading;
using KingdomEnhancedMod;
using SaveStatus = KingdomEnhancedMod.KnightIdentityArchiveStore.SaveStatus;

namespace KnightIdentityArchiveTests
{
    /// <summary>落盘：temp+原子替换、备份轮换、损坏/未知版本、失败保持旧文件、未知字段保持。</summary>
    internal static class FileIoTests
    {
        internal static void Run()
        {
            Case.Run("io.v1UpgradeWithReservedUnknownFieldOrFullRootRefusesWithoutWriting", () =>
            {
                foreach (bool fullRoot in new[] { false, true })
                {
                    using (TempDir dir = new TempDir())
                    {
                        var json = new System.Text.StringBuilder("{\"schemaVersion\":1,\"scopes\":[]");
                        if (fullRoot) for (int i = 0; i < 254; i++) json.Append(",\"extra").Append(i).Append("\":0");
                        else json.Append(",\"contexts\":{\"futureData\":1}");
                        json.Append('}');
                        byte[] original = System.Text.Encoding.UTF8.GetBytes(json.ToString());
                        Check.Equal(KnightIdentityArchiveStatus.Valid, KnightIdentityArchive.Parse(original, out var archive, out _), "v1 input remains valid");
                        string path = dir.File("archive.json"), backup = KnightIdentityArchiveStore.BackupPath(path);
                        File.WriteAllBytes(path, original); File.WriteAllBytes(backup, original);
                        Check.False(KnightIdentityArchiveStore.Save(path, archive).Ok, "unreadable upgrade refused");
                        Check.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "main untouched");
                        Check.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(backup)), "backup untouched");
                    }
                }
            });
            Console.WriteLine("File I/O (atomic save / backup / corruption / unknown schema / unknown fields)");

            Case.Run("io.missingThenCreatedThenByteExactRoundTrip", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    string scope = Build.Hex64(1);
                    string hash = Build.Hex('a');

                    KnightIdentityArchiveStore.LoadResult missing = KnightIdentityArchiveStore.Load(path);
                    Check.Equal(KnightIdentityArchiveStatus.Missing, missing.Status, "absent file is Missing");
                    Check.False(missing.IsUsable, "Missing has no archive");

                    KnightIdentityArchive archive = Build.Archive(scope, hash, "knight-1", 1, 2);
                    KnightIdentityArchiveStore.SaveResult created = KnightIdentityArchiveStore.Save(path, archive);
                    Check.Equal(SaveStatus.Created, created.Status, "new file written with Move");
                    Check.True(created.Ok, "created is ok");

                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(path);
                    Check.Equal(KnightIdentityArchiveStatus.Valid, loaded.Status, "valid after save");
                    Check.False(loaded.RecoveredBackup, "main file was used");
                    Check.True(loaded.Archive.TryRestore(scope, hash, "knight-1", out KnightIdentityReceipt receipt), "restore after roundtrip");
                    Check.Equal(Build.Seed(1), receipt.Id, "GUID survives");
                    Check.Equal(2, receipt.Style, "style survives");
                    Check.True(archive.SerializeToUtf8().AsSpan().SequenceEqual(loaded.Archive.SerializeToUtf8()), "byte-exact roundtrip");
                    Check.Equal(1, Build.FilesIn(dir.Path).Length, "only the archive file exists");
                }
            });

            Case.Run("io.replayIsUnchangedAndRotationKeepsPreviousBackup", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    string backup = KnightIdentityArchiveStore.BackupPath(path);
                    string scope = Build.Hex64(1);

                    KnightIdentityArchive first = Build.Archive(scope, Build.Hex('a'), "knight-1", 1, 0);
                    Check.Equal(SaveStatus.Created, KnightIdentityArchiveStore.Save(path, first).Status, "first save");
                    byte[] firstBytes = File.ReadAllBytes(path);

                    Check.Equal(SaveStatus.Unchanged, KnightIdentityArchiveStore.Save(path, first).Status, "replay of identical content is Unchanged");
                    Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(firstBytes), "replay leaves the file alone");
                    Check.False(File.Exists(backup), "replay creates no backup");

                    Check.Equal(
                        KnightIdentityArchive.MutationStatus.Applied,
                        first.RecordSnapshot(scope, Build.Snapshot(Build.Hex('b'), Build.E("knight-2", 2, 4))),
                        "second generation recorded");
                    Check.Equal(SaveStatus.Replaced, KnightIdentityArchiveStore.Save(path, first).Status, "second save replaces");
                    Check.True(File.ReadAllBytes(backup).AsSpan().SequenceEqual(firstBytes), "backup holds the previous file byte-for-byte");
                    Check.Equal(KnightIdentityArchiveStatus.Valid, KnightIdentityArchiveStore.Load(backup).Status, "backup is itself a valid archive");

                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(path);
                    Check.True(loaded.IsUsable && loaded.Archive.TryRestore(scope, Build.Hex('b'), "knight-2", out _), "both generations reachable");
                    Check.Equal(2, Build.FilesIn(dir.Path).Length, "main and backup only");
                    Check.False(Array.Exists(Build.FilesIn(dir.Path), name => name.Contains(".tmp-")), "no temp files");
                }
            });

            Case.Run("io.corruptMainRecoversFromBackupAndNeverOverwritesIt", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    string backup = KnightIdentityArchiveStore.BackupPath(path);
                    string scope = Build.Hex64(1);

                    KnightIdentityArchive first = Build.Archive(scope, Build.Hex('a'), "knight-1", 1, 1);
                    KnightIdentityArchiveStore.Save(path, first);
                    first.RecordSnapshot(scope, Build.Snapshot(Build.Hex('b'), Build.E("knight-2", 2, 2)));
                    KnightIdentityArchiveStore.Save(path, first);
                    byte[] backupBytes = File.ReadAllBytes(backup);

                    Build.WriteText(path, "{ this is not json");
                    KnightIdentityArchiveStore.LoadResult recovered = KnightIdentityArchiveStore.Load(path);
                    Check.Equal(KnightIdentityArchiveStatus.Valid, recovered.Status, "valid backup recovered");
                    Check.True(recovered.RecoveredBackup, "RecoveredBackup flag set");
                    Check.True(recovered.Archive.TryRestore(scope, Build.Hex('a'), "knight-1", out _), "recovered generation restorable");
                    Check.Contains(recovered.Detail, "main:", "detail names the unusable main file");

                    byte[] corruptBytes = File.ReadAllBytes(path);
                    KnightIdentityArchive third = Build.Archive(scope, Build.Hex('c'), "knight-3", 3, 3);
                    KnightIdentityArchiveStore.SaveResult refused = KnightIdentityArchiveStore.Save(path, third);
                    Check.Equal(SaveStatus.RefusedCorruptMain, refused.Status, "save refuses to burn the valid backup");
                    Check.False(refused.Ok, "refusal is not ok");
                    Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(corruptBytes), "corrupt main untouched");
                    Check.True(File.ReadAllBytes(backup).AsSpan().SequenceEqual(backupBytes), "valid backup untouched");

                    KnightIdentityArchiveStore.SaveResult recoveredSave = KnightIdentityArchiveStore.RecoverMainFromBackup(path);
                    Check.True(recoveredSave.Ok, "explicit repair succeeds");
                    Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(backupBytes), "main rebuilt from backup content");
                    Check.True(File.ReadAllBytes(backup).AsSpan().SequenceEqual(backupBytes), "repair leaves backup alone");
                    Check.Equal(KnightIdentityArchiveStatus.Valid, KnightIdentityArchiveStore.Load(path).Status, "readable after repair");
                    Check.False(KnightIdentityArchiveStore.Load(path).RecoveredBackup, "main is the live file again");
                    Check.Equal(SaveStatus.Replaced, KnightIdentityArchiveStore.Save(path, third).Status, "saving works after repair");
                    Check.True(KnightIdentityArchiveStore.Load(path).Archive.TryRestore(scope, Build.Hex('c'), "knight-3", out _), "new identity persisted");
                }
            });

            Case.Run("io.corruptMainIsRefusedUntilExplicitRecovery", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    Build.WriteText(path, "{ this is not json");
                    byte[] corruptBytes = File.ReadAllBytes(path);
                    Check.Equal(KnightIdentityArchiveStatus.Corrupt, KnightIdentityArchiveStore.Load(path).Status, "corrupt main");

                    KnightIdentityArchive archive = Build.Archive(Build.Hex64(1), Build.Hex('a'), "knight-1", 1, 0);
                    KnightIdentityArchiveStore.SaveResult refused = KnightIdentityArchiveStore.Save(path, archive);
                    Check.Equal(SaveStatus.RefusedCorruptMain, refused.Status, "a plain save never replaces a corrupt main");
                    Check.False(refused.Ok, "refusal is not ok");
                    Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(corruptBytes), "corrupt main untouched");
                    Check.False(File.Exists(KnightIdentityArchiveStore.BackupPath(path)), "no bogus backup created");

                    KnightIdentityArchiveStore.SaveResult repair = KnightIdentityArchiveStore.RecoverMainFromBackup(path);
                    Check.False(repair.Ok, "explicit recovery needs a usable backup");
                    Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(corruptBytes), "failed recovery changes nothing");

                    // 唯一的合法 create 路径：主文件确实缺失（运维删掉坏档后）才允许新建。
                    File.Delete(path);
                    Check.Equal(KnightIdentityArchiveStatus.Missing, KnightIdentityArchiveStore.Load(path).Status, "missing after the operator removed the bad file");
                    KnightIdentityArchiveStore.SaveResult created = KnightIdentityArchiveStore.Save(path, archive);
                    Check.Equal(SaveStatus.Created, created.Status, "missing main is the only create path");
                    Check.True(KnightIdentityArchiveStore.Load(path).Archive.TryRestore(Build.Hex64(1), Build.Hex('a'), "knight-1", out _), "archive readable again");
                }
            });

            Case.Run("io.unknownSchemaIsNeitherMissingNorDowngraded", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    string backup = KnightIdentityArchiveStore.BackupPath(path);
                    string scope = Build.Hex64(1);

                    KnightIdentityArchiveStore.Save(path, Build.Archive(scope, Build.Hex('a'), "knight-1", 1, 0));
                    KnightIdentityArchive first = KnightIdentityArchiveStore.Load(path).Archive;
                    first.RecordSnapshot(scope, Build.Snapshot(Build.Hex('b'), Build.E("knight-2", 2, 1)));
                    KnightIdentityArchiveStore.Save(path, first);

                    string newerSchema = "{\"schemaVersion\":3,\"scopes\":[]}";
                    Build.WriteText(path, newerSchema);
                    byte[] mainBytes = File.ReadAllBytes(path);
                    byte[] backupBytes = File.ReadAllBytes(backup);

                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(path);
                    Check.Equal(KnightIdentityArchiveStatus.UnsupportedVersion, loaded.Status, "unknown schema surfaced");
                    Check.NotEqual(KnightIdentityArchiveStatus.Missing, loaded.Status, "Unsupported must not be reported as Missing");
                    Check.False(loaded.IsUsable, "no archive handed out for an unknown schema");
                    Check.False(loaded.RecoveredBackup, "unknown main schema must not silently downgrade to the backup");

                    KnightIdentityArchiveStore.SaveResult result = KnightIdentityArchiveStore.Save(path, Build.Archive(Build.Hex64(9), Build.Hex('c'), "knight-9", 9, 4));
                    Check.Equal(SaveStatus.RefusedUnknownVersion, result.Status, "refuses to overwrite an unknown schema");
                    Check.False(result.Ok, "refusal is not ok");
                    Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(mainBytes), "unknown-schema file untouched");
                    Check.True(File.ReadAllBytes(backup).AsSpan().SequenceEqual(backupBytes), "backup untouched");
                    Check.Equal(2, Build.FilesIn(dir.Path).Length, "no temp written");
                }
            });

            Case.Run("io.higherSchemaWithForeignBodyIsUnsupportedNotCorrupt", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    string foreign = "{\"scopes\":[{\"weird\":1}],\"schemaVersion\":3}";
                    Build.WriteText(path, foreign);
                    byte[] before = File.ReadAllBytes(path);

                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(path);
                    Check.Equal(KnightIdentityArchiveStatus.UnsupportedVersion, loaded.Status, "foreign body must never be downgraded to Corrupt");
                    Check.False(loaded.IsUsable, "no archive handed out");
                    Check.NotEqual(KnightIdentityArchiveStatus.Missing, loaded.Status, "Unsupported must not be reported as Missing");

                    KnightIdentityArchiveStore.SaveResult result = KnightIdentityArchiveStore.Save(path, Build.Archive(Build.Hex64(1), Build.Hex('a'), "knight-1", 1, 0));
                    Check.Equal(SaveStatus.RefusedUnknownVersion, result.Status, "refuses to overwrite a higher schema even if its body looks foreign");
                    Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(before), "higher-schema file untouched");
                    Check.False(Array.Exists(Build.FilesIn(dir.Path), name => name.Contains(".tmp-")), "no temp left");
                }
            });

            Case.Run("io.missingMainWithValidBackupRecoversAndCreates", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    string backup = KnightIdentityArchiveStore.BackupPath(path);
                    string scope = Build.Hex64(1);

                    KnightIdentityArchiveStore.Save(path, Build.Archive(scope, Build.Hex('a'), "knight-1", 1, 0));
                    KnightIdentityArchive second = KnightIdentityArchiveStore.Load(path).Archive;
                    second.RecordSnapshot(scope, Build.Snapshot(Build.Hex('b'), Build.E("knight-2", 2, 1)));
                    KnightIdentityArchiveStore.Save(path, second);
                    byte[] backupBytes = File.ReadAllBytes(backup);
                    File.Delete(path);

                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(path);
                    Check.True(loaded.IsUsable && loaded.RecoveredBackup, "backup used when the main file is gone");
                    Check.True(loaded.Archive.TryRestore(scope, Build.Hex('a'), "knight-1", out _), "backup generation readable");

                    Check.Equal(SaveStatus.Created, KnightIdentityArchiveStore.Save(path, second).Status, "main recreated");
                    Check.Equal(KnightIdentityArchiveStatus.Valid, KnightIdentityArchiveStore.Load(path).Status, "main valid again");
                    Check.True(File.ReadAllBytes(backup).AsSpan().SequenceEqual(backupBytes), "backup left untouched by the recreate");
                }
            });

            Case.Run("io.ioFailureKeepsOldFileAndLeavesNoTemp", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    string backup = KnightIdentityArchiveStore.BackupPath(path);
                    string scope = Build.Hex64(1);

                    KnightIdentityArchiveStore.Save(path, Build.Archive(scope, Build.Hex('a'), "knight-1", 1, 0));
                    KnightIdentityArchive second = KnightIdentityArchiveStore.Load(path).Archive;
                    second.RecordSnapshot(scope, Build.Snapshot(Build.Hex('b'), Build.E("knight-2", 2, 1)));
                    KnightIdentityArchiveStore.Save(path, second);
                    byte[] mainBytes = File.ReadAllBytes(path);
                    byte[] backupBytes = File.ReadAllBytes(backup);

                    KnightIdentityArchive third = KnightIdentityArchiveStore.Load(path).Archive;
                    third.RecordSnapshot(scope, Build.Snapshot(Build.Hex('c'), Build.E("knight-3", 3, 2)));
                    using (FileStream lockedBackup = new FileStream(backup, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        KnightIdentityArchiveStore.SaveResult failed = KnightIdentityArchiveStore.Save(path, third);
                        Check.Equal(SaveStatus.Failed, failed.Status, "save fails while the backup file is locked");
                        Check.False(failed.Ok, "failure is not ok");
                        Check.True(failed.Retried, "the failed write is flagged as retried");
                    }

                    Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(mainBytes), "old file kept after failure");
                    Check.True(File.ReadAllBytes(backup).AsSpan().SequenceEqual(backupBytes), "backup kept after failure");
                    Check.False(Array.Exists(Build.FilesIn(dir.Path), name => name.Contains(".tmp-")), "own temp cleaned up, nothing else touched");

                    Check.Equal(SaveStatus.Replaced, KnightIdentityArchiveStore.Save(path, third).Status, "retry succeeds once the lock is gone");
                    Check.True(KnightIdentityArchiveStore.Load(path).Archive.TryRestore(scope, Build.Hex('c'), "knight-3", out _), "newest identity persisted");
                }
            });

            Case.Run("io.transientIoFailureHealsWithinOneRetry", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    string backup = KnightIdentityArchiveStore.BackupPath(path);
                    string scope = Build.Hex64(1);

                    KnightIdentityArchive first = Build.Archive(scope, Build.Hex('a'), "knight-1", 1, 0);
                    Check.Equal(SaveStatus.Created, KnightIdentityArchiveStore.Save(path, first).Status, "first save");
                    first.RecordSnapshot(scope, Build.Snapshot(Build.Hex('b'), Build.E("knight-2", 2, 1)));
                    Check.Equal(SaveStatus.Replaced, KnightIdentityArchiveStore.Save(path, first).Status, "second save creates the backup");
                    KnightIdentityArchive third = KnightIdentityArchiveStore.Load(path).Archive;
                    third.RecordSnapshot(scope, Build.Snapshot(Build.Hex('c'), Build.E("knight-3", 3, 2)));

                    FileStream locked = new FileStream(backup, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    try
                    {
                        Thread release = new Thread(() => { Thread.Sleep(80); locked.Dispose(); }) { IsBackground = true };
                        release.Start();
                        KnightIdentityArchiveStore.SaveResult healed = KnightIdentityArchiveStore.Save(path, third);
                        Check.Equal(SaveStatus.Replaced, healed.Status, "one retry heals a transient lock");
                        Check.True(healed.Retried, "the healed write is flagged as retried");
                        release.Join();
                    }
                    finally
                    {
                        locked.Dispose();
                    }

                    Check.True(KnightIdentityArchiveStore.Load(path).Archive.TryRestore(scope, Build.Hex('c'), "knight-3", out _), "newest identity persisted");
                    Check.False(Array.Exists(Build.FilesIn(dir.Path), name => name.Contains(".tmp-")), "no temp files after the healed retry");
                }
            });

            Case.Run("io.protectiveRefusalsAreNeverRetried", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    KnightIdentityArchive archive = Build.Archive(Build.Hex64(1), Build.Hex('a'), "knight-1", 1, 0);

                    Build.WriteText(path, "{ this is not json");
                    KnightIdentityArchiveStore.SaveResult corrupt = KnightIdentityArchiveStore.Save(path, archive);
                    Check.Equal(SaveStatus.RefusedCorruptMain, corrupt.Status, "corrupt main refused");
                    Check.False(corrupt.Retried, "a corrupt-main refusal is never retried");

                    Build.WriteText(path, "{\"schemaVersion\":99,\"scopes\":[]}");
                    KnightIdentityArchiveStore.SaveResult version = KnightIdentityArchiveStore.Save(path, archive);
                    Check.Equal(SaveStatus.RefusedUnknownVersion, version.Status, "unknown schema refused");
                    Check.False(version.Retried, "an unknown-schema refusal is never retried");
                    Check.Equal("{\"schemaVersion\":99,\"scopes\":[]}", File.ReadAllText(path), "refused files stay untouched");
                }
            });

            Case.Run("io.directoryTargetIsNeverWrittenInto", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string target = dir.File("as-a-directory");
                    Directory.CreateDirectory(target);
                    string bystander = dir.File("bystander.txt");
                    Build.WriteText(bystander, "untouched");

                    KnightIdentityArchiveStore.SaveResult result = KnightIdentityArchiveStore.Save(target, Build.Archive(Build.Hex64(1), Build.Hex('a'), "knight-1", 1, 0));
                    Check.False(result.Ok, "saving onto a directory is refused");
                    Check.True(Directory.Exists(target), "the target is still a directory");
                    Check.Equal(0, Directory.GetFiles(target).Length, "nothing was written into it");
                    Check.Equal("untouched", File.ReadAllText(bystander), "unrelated files untouched");
                    Check.False(Array.Exists(Build.FilesIn(dir.Path), name => name.Contains(".tmp-")), "no temp left after failure");
                }
            });

            Case.Run("io.unreadableMainIsCorruptNeverMissing", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    KnightIdentityArchiveStore.Save(path, Build.Archive(Build.Hex64(1), Build.Hex('a'), "knight-1", 1, 0));

                    using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(path);
                        Check.Equal(KnightIdentityArchiveStatus.Corrupt, loaded.Status, "locked main is Corrupt");
                        Check.NotEqual(KnightIdentityArchiveStatus.Missing, loaded.Status, "locked main is never Missing");
                        Check.False(loaded.IsUsable, "no archive when unreadable");
                    }

                    Check.True(KnightIdentityArchiveStore.Load(path).Archive.TryRestore(Build.Hex64(1), Build.Hex('a'), "knight-1", out _), "readable once released");
                }
            });

            Case.Run("io.rejectsOversizedEmptyDeepAndMalformedDocuments", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");

                    using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                    {
                        byte[] chunk = new byte[1024 * 1024];
                        for (int i = 0; i < chunk.Length; i++) chunk[i] = (byte)' ';
                        for (int i = 0; i < 17; i++) stream.Write(chunk, 0, chunk.Length);
                    }
                    Check.Equal(KnightIdentityArchiveStatus.Corrupt, KnightIdentityArchiveStore.Load(path).Status, "file above 16 MiB refused");

                    string[] malformed =
                    {
                        string.Empty,
                        "{\"schemaVersion\":1,\"scopes\":" + new string('[', 40) + new string(']', 40) + "}",
                        "[]",
                        "{\"scopes\":[]}",
                        "{\"schemaVersion\":\"1\",\"scopes\":[]}",
                        "{\"schemaVersion\":1,\"schemaVersion\":1}",
                        "{\"schemaVersion\":1,}",
                        "{\"schemaVersion\":1,\"scopes\":[{\"scopeKey\":\"short\",\"snapshots\":[]}]}",
                        "{\"schemaVersion\":1,\"scopes\":[{\"scopeKey\":\"" + Build.Hex('a') + "\",\"snapshots\":[{\"hash\":\"" + Build.Hex('b') + "\",\"savedAtUtc\":\"not-a-time\",\"entries\":[]}]}]}",
                        "{\"schemaVersion\":1,\"scopes\":[{\"scopeKey\":\"" + Build.Hex('a') + "\",\"snapshots\":[{\"hash\":\"" + Build.Hex('b') + "\",\"savedAtUtc\":\"2026-09-14T00:00:00+00:00\",\"entries\":[{\"u\":\"k\",\"id\":\"00000000-0000-0000-0000-000000000000\",\"style\":0}]}]}]}",
                        "{\"schemaVersion\":1,\"scopes\":[{\"scopeKey\":\"" + Build.Hex('a') + "\",\"snapshots\":[{\"hash\":\"" + Build.Hex('b') + "\",\"savedAtUtc\":\"2026-09-14T00:00:00+00:00\",\"entries\":[{\"u\":\"k\",\"id\":\"" + Build.Seed(1).ToString("N") + "\",\"style\":9}]}]}]}",
                        "{\"schemaVersion\":1,\"scopes\":[{\"scopeKey\":\"" + Build.Hex('a') + "\",\"snapshots\":[{\"hash\":\"" + Build.Hex('b') + "\",\"savedAtUtc\":\"2026-09-14T00:00:00+00:00\",\"entries\":[{\"u\":\"k\",\"id\":\"" + Build.Seed(1).ToString("N") + "\",\"style\":0,\"u\":\"k2\"}]}]}]}",
                    };
                    foreach (string document in malformed)
                    {
                        Build.WriteText(path, document);
                        KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(path);
                        Check.Equal(KnightIdentityArchiveStatus.Corrupt, loaded.Status, "malformed document must be Corrupt: " + Excerpt(document));
                        Check.False(loaded.IsUsable, "no archive for malformed document");
                    }
                }
            });

            Case.Run("io.rootUnknownFieldsSurviveLoadMutateSaveCycles", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    string backup = KnightIdentityArchiveStore.BackupPath(path);
                    string scope = Build.Hex64(1);
                    string hash = Build.Hex('a');
                    string document =
                        "{\"schemaVersion\":1,\"customRoot\":{\"a\":[1,2,null],\"b\":\"x\"},\"scopes\":[{"
                        + "\"scopeKey\":\"" + scope + "\",\"snapshots\":[{"
                        + "\"hash\":\"" + hash + "\",\"savedAtUtc\":\"2026-09-14T00:00:00.0000000+00:00\",\"entries\":[{"
                        + "\"u\":\"knight-1\",\"id\":\"" + Build.Seed(1).ToString("N") + "\",\"style\":3}]}]}]}";
                    Build.WriteText(path, document);

                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(path);
                    Check.Equal(KnightIdentityArchiveStatus.Valid, loaded.Status, "hand written document loads");
                    Check.True(loaded.Archive.TryRestore(scope, hash, "knight-1", out KnightIdentityReceipt receipt), "hand written entry readable");
                    Check.Equal(3, receipt.Style, "hand written style kept");

                    Check.Equal(KnightIdentityArchive.MutationStatus.Applied, loaded.Archive.RecordSnapshot(scope, Build.Snapshot(Build.Hex('b'), Build.E("knight-2", 2, 1))), "new generation recorded");
                    Check.Equal(SaveStatus.Replaced, KnightIdentityArchiveStore.Save(path, loaded.Archive).Status, "saved over the hand written file");

                    string text = File.ReadAllText(path);
                    Check.Contains(text, "\"customRoot\":{\"a\":[1,2,null],\"b\":\"x\"}", "root unknown field preserved verbatim");
                    Check.Contains(File.ReadAllText(backup), "\"customRoot\"", "backup keeps root unknown fields too");

                    KnightIdentityArchiveStore.LoadResult again = KnightIdentityArchiveStore.Load(path);
                    Check.Equal(KnightIdentityArchiveStatus.Valid, again.Status, "still valid after a second cycle");
                    Check.Contains(Encoding.UTF8.GetString(again.Archive.SerializeToUtf8()), "\"customRoot\":{\"a\":[1,2,null],\"b\":\"x\"}", "root unknown field survives repeated cycles");
                }
            });

            Case.Run("io.nestedUnknownOrDuplicateFieldsAreCorrupt", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    string scope = Build.Hex64(1);
                    string hash = Build.Hex('a');
                    string entry = "{\"u\":\"knight-1\",\"id\":\"" + Build.Seed(1).ToString("N") + "\",\"style\":0}";
                    string snapshot = "{\"hash\":\"" + hash + "\",\"savedAtUtc\":\"2026-09-14T00:00:00+00:00\",\"entries\":[" + entry + "]}";
                    string document = "{\"schemaVersion\":1,\"scopes\":[{\"scopeKey\":\"" + scope + "\",\"snapshots\":[" + snapshot + "]}]}";
                    Check.Equal(KnightIdentityArchiveStatus.Valid, LoadText(path, document), "baseline document is valid");

                    Check.Equal(
                        KnightIdentityArchiveStatus.Corrupt,
                        LoadText(path, document.Replace("\"scopeKey\":", "\"customScope\":7,\"scopeKey\":")),
                        "unknown scope field is Corrupt");
                    Check.Equal(
                        KnightIdentityArchiveStatus.Corrupt,
                        LoadText(path, document.Replace("\"entries\":", "\"customSnapshot\":\"x\",\"entries\":")),
                        "unknown snapshot field is Corrupt");
                    Check.Equal(
                        KnightIdentityArchiveStatus.Corrupt,
                        LoadText(path, document.Replace("\"style\":0", "\"style\":0,\"customEntry\":1")),
                        "unknown entry field is Corrupt");
                    Check.Equal(
                        KnightIdentityArchiveStatus.Corrupt,
                        LoadText(path, document.Replace("\"style\":0", "\"style\":0,\"u\":\"other\"")),
                        "duplicate entry field is Corrupt");
                    Check.Equal(
                        KnightIdentityArchiveStatus.Corrupt,
                        LoadText(path, document.Replace("\"scopes\":", "\"scopes\":[],\"scopes\":")),
                        "duplicate root field is Corrupt");
                }
            });

            Case.Run("io.unknownBackupBlocksEveryOverwritePath", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    string backup = KnightIdentityArchiveStore.BackupPath(path);
                    string scope = Build.Hex64(1);
                    string futureSchema = "{\"schemaVersion\":3,\"scopes\":[{\"weird\":1}]}";

                    KnightIdentityArchiveStore.Save(path, Build.Archive(scope, Build.Hex('a'), "knight-1", 1, 0));
                    KnightIdentityArchive second = KnightIdentityArchiveStore.Load(path).Archive;
                    second.RecordSnapshot(scope, Build.Snapshot(Build.Hex('b'), Build.E("knight-2", 2, 1)));
                    KnightIdentityArchiveStore.Save(path, second);
                    byte[] mainBytes = File.ReadAllBytes(path);
                    KnightIdentityArchive third = KnightIdentityArchiveStore.Load(path).Archive;
                    third.RecordSnapshot(scope, Build.Snapshot(Build.Hex('c'), Build.E("knight-3", 3, 2)));

                    // (a) 主文件有效 + 备份是更高版本：写入会把更高版本的备份轮换掉 → 拒绝
                    Build.WriteText(backup, futureSchema);
                    byte[] futureBytes = File.ReadAllBytes(backup);
                    KnightIdentityArchiveStore.SaveResult validMain = KnightIdentityArchiveStore.Save(path, third);
                    Check.Equal(SaveStatus.RefusedUnknownVersion, validMain.Status, "a valid main must not rotate a future backup away");
                    Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(mainBytes), "main bytes kept");
                    Check.True(File.ReadAllBytes(backup).AsSpan().SequenceEqual(futureBytes), "future backup bytes kept");

                    // (b) 主文件损坏 + 备份是更高版本：既不碰主文件，也不碰备份
                    Build.WriteText(path, "{ broken");
                    byte[] corruptBytes = File.ReadAllBytes(path);
                    KnightIdentityArchiveStore.SaveResult corruptMain = KnightIdentityArchiveStore.Save(path, third);
                    Check.Equal(SaveStatus.RefusedUnknownVersion, corruptMain.Status, "a future backup blocks even the corrupt-main path");
                    Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(corruptBytes), "corrupt main bytes kept");
                    Check.True(File.ReadAllBytes(backup).AsSpan().SequenceEqual(futureBytes), "future backup untouched by the refusal");

                    // (c) 主文件缺失 + 备份是更高版本：绝不新建低版本主档去遮蔽它
                    File.Delete(path);
                    KnightIdentityArchiveStore.SaveResult missingMain = KnightIdentityArchiveStore.Save(path, third);
                    Check.Equal(SaveStatus.RefusedUnknownVersion, missingMain.Status, "a missing main must not shadow a future backup with a lower version");
                    Check.False(File.Exists(path), "no lower-version main was created");
                    Check.True(File.ReadAllBytes(backup).AsSpan().SequenceEqual(futureBytes), "future backup untouched");
                    Check.False(Array.Exists(Build.FilesIn(dir.Path), name => name.Contains(".tmp-")), "no temp left");
                }
            });

            Case.Run("io.overSizedSerializationIsRefusedAndKeepsMainAndBackup", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    string backup = KnightIdentityArchiveStore.BackupPath(path);
                    string scope = Build.Hex64(1);

                    KnightIdentityArchiveStore.Save(path, Build.Archive(scope, Build.Hex('a'), "knight-1", 1, 0));
                    KnightIdentityArchive second = KnightIdentityArchiveStore.Load(path).Archive;
                    second.RecordSnapshot(scope, Build.Snapshot(Build.Hex('b'), Build.E("knight-2", 2, 1)));
                    KnightIdentityArchiveStore.Save(path, second);
                    byte[] backupBytes = File.ReadAllBytes(backup);

                    Build.WriteText(path, PaddedDocument((int)KnightIdentityArchiveStore.MaxFileBytes - 400, scope, Build.Hex('c')));
                    byte[] paddedBytes = File.ReadAllBytes(path);
                    Check.True(paddedBytes.Length < KnightIdentityArchiveStore.MaxFileBytes, "padded document is still under the cap");
                    KnightIdentityArchive oversized = KnightIdentityArchiveStore.Load(path).Archive;
                    Check.Equal(
                        KnightIdentityArchive.MutationStatus.Applied,
                        oversized.RecordSnapshot(scope, SnapshotOf(Build.Hex('d'), 16)),
                        "the padded archive still accepts one more generation in memory");

                    KnightIdentityArchiveStore.SaveResult result = KnightIdentityArchiveStore.Save(path, oversized);
                    Check.Equal(SaveStatus.Failed, result.Status, "serialization above 16 MiB is refused before any write");
                    Check.False(result.Ok, "refusal is not ok");
                    Check.False(result.Retried, "a size refusal is never retried");
                    Check.Contains(result.Detail, "above", "detail names the byte cap");
                    Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(paddedBytes), "main file kept byte-for-byte");
                    Check.True(File.ReadAllBytes(backup).AsSpan().SequenceEqual(backupBytes), "backup kept byte-for-byte");
                    Check.False(Array.Exists(Build.FilesIn(dir.Path), name => name.Contains(".tmp-")), "no temp left");
                }
            });

            Case.Run("io.parseRejectsNullEmptyAndOverSizedInput", () =>
            {
                Check.Equal(KnightIdentityArchiveStatus.Corrupt, KnightIdentityArchive.Parse(null, out KnightIdentityArchive nullArchive, out string nullError), "null bytes are Corrupt");
                Check.True(nullArchive == null, "no archive for null input");
                Check.Contains(nullError, "null", "detail names the null input");
                Check.Equal(KnightIdentityArchiveStatus.Corrupt, KnightIdentityArchive.Parse(Array.Empty<byte>(), out _, out string emptyError), "empty bytes are Corrupt");
                Check.Contains(emptyError, "empty", "detail names the empty input");
                Check.Equal(KnightIdentityArchiveStatus.Corrupt, KnightIdentityArchive.Parse(new byte[KnightIdentityArchiveStore.MaxFileBytes + 1], out _, out string bigError), "input above the cap is Corrupt");
                Check.Contains(bigError, "above", "detail names the byte cap");

                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    Build.WriteText(path, PaddedDocument((int)KnightIdentityArchiveStore.MaxFileBytes, Build.Hex64(1), Build.Hex('a')));
                    Check.Equal(KnightIdentityArchiveStore.MaxFileBytes, new FileInfo(path).Length, "boundary file sits exactly at the cap");
                    Check.Equal(KnightIdentityArchiveStatus.Valid, KnightIdentityArchiveStore.Load(path).Status, "exactly at the cap is still accepted");
                }
            });

            Case.Run("io.parseEnforcesAccumulatedEntryCap", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");

                    Build.WriteText(path, BigDocument(64, 8, 64));
                    KnightIdentityArchiveStore.LoadResult atCap = KnightIdentityArchiveStore.Load(path);
                    Check.Equal(KnightIdentityArchiveStatus.Valid, atCap.Status, "exactly MaxTotalEntries is accepted");
                    Check.Equal(KnightIdentityArchive.MaxTotalEntries, atCap.Archive.TotalEntryCount, "document really sits at the cap");

                    Build.WriteText(path, BigDocument(65, 8, 64));
                    KnightIdentityArchiveStore.LoadResult over = KnightIdentityArchiveStore.Load(path);
                    Check.Equal(KnightIdentityArchiveStatus.Corrupt, over.Status, "one scope above the entry cap is rejected while parsing");
                    Check.Contains(over.Detail, "more than " + KnightIdentityArchive.MaxTotalEntries, "detail names the accumulated cap");
                }
            });

            Case.Run("io.rootFieldCountIsCappedAndUnknownNamesDeduplicated", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    string scope = Build.Hex64(1);
                    int lastExtra = KnightIdentityArchive.MaxRootFieldCount - 3;

                    Build.WriteText(path, ManyRootFields(KnightIdentityArchive.MaxRootFieldCount - 2, scope));
                    KnightIdentityArchiveStore.LoadResult atCap = KnightIdentityArchiveStore.Load(path);
                    Check.Equal(KnightIdentityArchiveStatus.Valid, atCap.Status, "the root field cap is inclusive");
                    string text = Encoding.UTF8.GetString(atCap.Archive.SerializeToUtf8());
                    Check.Contains(text, "\"extra0\":[0]", "first unknown root field preserved");
                    Check.Contains(text, "\"extra" + lastExtra + "\":[" + lastExtra + "]", "last unknown root field preserved");

                    Build.WriteText(path, ManyRootFields(KnightIdentityArchive.MaxRootFieldCount - 1, scope));
                    Check.Equal(KnightIdentityArchiveStatus.Corrupt, KnightIdentityArchiveStore.Load(path).Status, "one root field above the cap is Corrupt");

                    Build.WriteText(path, "{\"schemaVersion\":1,\"dup\":1,\"dup\":2,\"scopes\":[]}");
                    Check.Equal(KnightIdentityArchiveStatus.Corrupt, KnightIdentityArchiveStore.Load(path).Status, "duplicate unknown root field name is Corrupt");
                }
            });

            Case.Run("io.neverTouchesUnrelatedFilesOrNativeSaves", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    string nativeSave = dir.File("saveData.json");
                    Build.WriteText(nativeSave, "{\"nativeUniqueIDs\":[1,2,3],\"gold\":1234}");
                    byte[] nativeBytes = File.ReadAllBytes(nativeSave);

                    KnightIdentityArchive archive = Build.Archive(Build.Hex64(1), Build.Hex('a'), "knight-1", 1, 0);
                    KnightIdentityArchiveStore.Save(path, archive);
                    KnightIdentityArchive second = KnightIdentityArchiveStore.Load(path).Archive;
                    second.RecordSnapshot(Build.Hex64(1), Build.Snapshot(Build.Hex('b'), Build.E("knight-2", 2, 1)));
                    KnightIdentityArchiveStore.Save(path, second);

                    Check.True(File.ReadAllBytes(nativeSave).AsSpan().SequenceEqual(nativeBytes), "native save file untouched byte-for-byte");
                    Check.Equal(3, Build.FilesIn(dir.Path).Length, "only archive, backup and the native file exist");
                }
            });
            Case.Run("io.legacyV1FileUpgradesToV2WithKindsAndContexts", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    string backup = KnightIdentityArchiveStore.BackupPath(path);
                    string scope = Build.Hex64(1);
                    string legacyHash = Build.Hex('a');
                    string entry = "{\"u\":\"knight-1\",\"id\":\"" + Build.Seed(1).ToString("N") + "\",\"style\":3}";
                    Build.WriteText(path,
                        "{\"schemaVersion\":1,\"scopes\":[{\"scopeKey\":\"" + scope + "\",\"snapshots\":[{\"hash\":\"" + legacyHash
                        + "\",\"savedAtUtc\":\"2026-09-14T00:00:00+00:00\",\"entries\":[" + entry + "]}]}]}");
                    byte[] legacyBytes = File.ReadAllBytes(path);

                    KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(path);
                    Check.Equal(KnightIdentityArchiveStatus.Valid, loaded.Status, "v1 file still loads");
                    Check.Equal(0, loaded.Archive.ContextCount, "v1 has no contexts");
                    Check.True(loaded.Archive.TryGetSnapshot(scope, legacyHash, out KnightIdentitySnapshot legacy), "legacy snapshot readable");
                    Check.Equal(KnightIdentityFingerprint.KindLegacy, legacy.Kind, "legacy snapshots are kind 1");
                    Check.True(loaded.Archive.TryRestore(scope, legacyHash, "knight-1", out KnightIdentityReceipt legacyReceipt), "legacy receipt restorable");
                    Check.Equal(3, legacyReceipt.Style, "legacy style kept");

                    string contextKey = Build.Hex64(9);
                    string normalizedHash = Build.Hex('b');
                    Check.Equal(
                        KnightIdentityArchive.MutationStatus.Applied,
                        loaded.Archive.RecordSnapshot(scope, Build.Snapshot(KnightIdentityFingerprint.KindNormalized, normalizedHash, DateTimeOffset.UnixEpoch, new[] { Build.E("knight-1", 1, 3) })),
                        "kind2 snapshot recorded");
                    Check.True(loaded.Archive.EnsureContext(contextKey, scope, true), "context registered");
                    Check.Equal(SaveStatus.Replaced, KnightIdentityArchiveStore.Save(path, loaded.Archive).Status, "v2 written");
                    Check.True(File.ReadAllBytes(backup).AsSpan().SequenceEqual(legacyBytes), "backup keeps the legacy v1 bytes for rollback");

                    KnightIdentityArchiveStore.LoadResult again = KnightIdentityArchiveStore.Load(path);
                    Check.True(again.IsUsable, "v2 file readable");
                    Check.True(again.Archive.TryGetContext(contextKey, out KnightIdentityContext context), "context persisted");
                    Check.Equal(scope, context.Active, "active epoch persisted");
                    Check.True(again.Archive.TryGetSnapshot(scope, legacyHash, out KnightIdentitySnapshot keptLegacy), "legacy snapshot preserved");
                    Check.Equal(KnightIdentityFingerprint.KindLegacy, keptLegacy.Kind, "legacy kind preserved");
                    Check.True(again.Archive.ScopeHasKind(scope, KnightIdentityFingerprint.KindNormalized), "kind2 presence tracked");
                    Check.Contains(Encoding.UTF8.GetString(again.Archive.SerializeToUtf8()), "\"contexts\":[{\"context\":\"" + contextKey + "\"", "context is serialized deterministically");
                }
            });

            Case.Run("io.contextsAreClosedAndValidated", () =>
            {
                using (TempDir dir = new TempDir())
                {
                    string path = dir.File("archive.json");
                    string scope = Build.Hex64(1);
                    string other = Build.Hex64(2);
                    string contextA = Build.Hex64(9);
                    string contextB = Build.Hex64(10);
                    string hash = Build.Hex('a');
                    string entry = "{\"u\":\"k\",\"id\":\"" + Build.Seed(1).ToString("N") + "\",\"style\":0}";
                    string snapshot = "{\"hash\":\"" + hash + "\",\"kind\":2,\"savedAtUtc\":\"2026-09-14T00:00:00+00:00\",\"entries\":[" + entry + "]}";
                    string scopes = "\"scopes\":[{\"scopeKey\":\"" + scope + "\",\"snapshots\":[" + snapshot + "]}]";
                    string ok = "{\"schemaVersion\":2," + scopes + ",\"contexts\":[{\"context\":\"" + contextA + "\",\"active\":\"" + scope + "\",\"epochs\":[\"" + scope + "\"]}]}";
                    Check.Equal(KnightIdentityArchiveStatus.Valid, LoadText(path, ok), "well formed v2 document is valid");
                    Check.Equal(KnightIdentityArchiveStatus.Corrupt, LoadText(path, "{\"schemaVersion\":2," + scopes + "}"), "missing contexts is Corrupt");
                    Check.Equal(KnightIdentityArchiveStatus.Corrupt, LoadText(path, ok.Replace("\"active\":\"" + scope + "\"", "\"active\":\"" + other + "\"")), "active outside epochs is Corrupt");
                    Check.Equal(KnightIdentityArchiveStatus.Corrupt, LoadText(path, ok.Replace("\"epochs\":[\"" + scope + "\"]", "\"epochs\":[\"" + other + "\"]")), "epoch without a scope is Corrupt");
                    Check.Equal(KnightIdentityArchiveStatus.Corrupt, LoadText(path, ok.Replace("\"kind\":2,", "\"kind\":2,\"kind\":2,")), "duplicate kind is Corrupt");
                    Check.Equal(KnightIdentityArchiveStatus.Corrupt, LoadText(path, ok.Replace("\"kind\":2", "\"kind\":3")), "unknown kind is Corrupt");
                    Check.Equal(KnightIdentityArchiveStatus.Corrupt, LoadText(path, ok.Replace("{\"context\":", "{\"customCtx\":1,\"context\":")), "unknown context field is Corrupt");
                    Check.Equal(
                        KnightIdentityArchiveStatus.Corrupt,
                        LoadText(path, "{\"schemaVersion\":2," + scopes + ",\"contexts\":[{\"context\":\"" + contextA + "\",\"active\":\"" + scope + "\",\"epochs\":[\"" + scope + "\"]},{\"context\":\"" + contextB + "\",\"active\":\"" + scope + "\",\"epochs\":[\"" + scope + "\"]}]}"),
                        "one epoch owned by two contexts is Corrupt");
                }
            });

        }

        private static KnightIdentityArchiveStatus LoadText(string path, string document)
        {
            Build.WriteText(path, document);
            return KnightIdentityArchiveStore.Load(path).Status;
        }

        /// <summary>带一个 root 扩展字段、总字节数恰好为 targetBytes 的合法档（用于逼近 16 MiB 上限）。</summary>
        private static string PaddedDocument(int targetBytes, string scope, string hash)
        {
            const string marker = "\"pad\":\"\"";
            string baseDocument = "{\"schemaVersion\":1," + marker + ",\"scopes\":[{\"scopeKey\":\"" + scope
                + "\",\"snapshots\":[{\"hash\":\"" + hash + "\",\"savedAtUtc\":\"2026-09-14T00:00:00+00:00\",\"entries\":[{\"u\":\"knight-1\",\"id\":\""
                + Build.Seed(1).ToString("N") + "\",\"style\":0}]}]}]}";
            int padding = targetBytes - Encoding.UTF8.GetByteCount(baseDocument);
            return baseDocument.Replace(marker, "\"pad\":\"" + new string('a', padding) + "\"");
        }

        /// <summary>scopes × 8 代 × entriesPerSnapshot 条的小条目档，用于压 MaxTotalEntries。</summary>
        private static string BigDocument(int scopes, int snapshotsPerScope, int entriesPerSnapshot)
        {
            StringBuilder text = new StringBuilder(4 * 1024 * 1024);
            text.Append("{\"schemaVersion\":1,\"scopes\":[");
            for (int s = 0; s < scopes; s++)
            {
                if (s > 0) text.Append(',');
                text.Append("{\"scopeKey\":\"").Append(Build.Hex64(s)).Append("\",\"snapshots\":[");
                for (int g = 0; g < snapshotsPerScope; g++)
                {
                    if (g > 0) text.Append(',');
                    text.Append("{\"hash\":\"").Append(Build.Hex64(g)).Append("\",\"savedAtUtc\":\"2026-09-14T00:00:00+00:00\",\"entries\":[");
                    for (int e = 0; e < entriesPerSnapshot; e++)
                    {
                        if (e > 0) text.Append(',');
                        text.Append("{\"u\":\"k").Append(e).Append("\",\"id\":\"").Append(Build.Seed(e).ToString("N")).Append("\",\"style\":0}");
                    }
                    text.Append("]}");
                }
                text.Append("]}");
            }
            text.Append("]}");
            return text.ToString();
        }

        /// <summary>schemaVersion + scopes + unknownCount 个未知 root 字段。</summary>
        private static string ManyRootFields(int unknownCount, string scope)
        {
            StringBuilder text = new StringBuilder(16 * 1024);
            text.Append("{\"schemaVersion\":1");
            for (int i = 0; i < unknownCount; i++) text.Append(",\"extra").Append(i).Append("\":[").Append(i).Append(']');
            text.Append(",\"scopes\":[{\"scopeKey\":\"").Append(scope).Append("\",\"snapshots\":[]}]}");
            return text.ToString();
        }

        private static KnightIdentitySnapshot SnapshotOf(string hash, int count)
        {
            KnightIdentitySnapshotEntry[] entries = new KnightIdentitySnapshotEntry[count];
            for (int i = 0; i < count; i++) entries[i] = Build.E("k" + i, i, i % KnightIdentityReceipt.StyleCount);
            return Build.Snapshot(hash, entries);
        }

        private static string Excerpt(string document)
        {
            return document.Length <= 40 ? document : document.Substring(0, 40) + "...";
        }
    }
}
