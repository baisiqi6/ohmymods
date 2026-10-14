using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using Coatsink.Common;
using KingdomEnhancedMod;
using static MusketeerNativeSaveAdapterTests.Fx;

// Host contracts only. Capture rows below are explicit synthetic fixture inputs, not native
// GetID observations or evidence that eight live units were spawned. Physical writes use a
// temp directory after the real adapter Prefix allows its body; real Stage/Gate/Harvest code
// validates those bytes. No game, profile, user saves or production runtime is launched here.
internal static class Program
{
    static int assertions, cases, failures;
    static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }
    static void Case(string name, Action body)
    {
        cases++;
        try { body(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    static int LeaseCount => ((IDictionary)typeof(MusketeerNativeSave)
        .GetField("Leases", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Count;
    static MusketeerNativeSave.OwnerState Owner => (MusketeerNativeSave.OwnerState)typeof(MusketeerNativeSave)
        .GetField("_owner", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

    sealed class Host : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "musk-legacy-gate-" + Guid.NewGuid().ToString("N"));
        internal readonly string Context, SourceEpoch = MusketeerArchive.NewScope(), Epoch = MusketeerArchive.NewScope();
        internal readonly Guid Request = Guid.NewGuid();
        internal readonly Guid[] Grants = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        internal readonly string[] NativeIds = Enumerable.Range(0, 8).Select(i => "host-fixture-archer-" + i).ToArray();
        internal readonly byte[] InitialNative, LegacyBytes;
        internal readonly MusketeerLegacyCompensationReceipt Pending, Complete;
        internal string Folder => Path.Combine(Root, "Release");
        internal string Disk => MusketeerNativeSave.CanonicalPath(Folder, NativeName);
        internal string Pair => Path.Combine(Path.GetDirectoryName(MusketeerPersistence.ArchivePath), "musketeer-pair-" + NativeName + ".json");
        internal string Raw => GlobalSaveData.loaded.prefs.contents.TryGetValue(MusketeerNativeArchive.Key, out var raw) ? raw : null;

        internal Host()
        {
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(Path.Combine(Root, "ModSave"));
            MusketeerPersistence.ArchivePath = Path.Combine(Root, "ModSave", "musketeer-identities.v2.json");
            GlobalSaveData.filename = NativeName;
            GlobalSaveData.loaded = new();
            GlobalSaveData.loaded.campaigns.Add(new CampaignSaveData());
            Filer.folder = Folder;
            MusketeerNativeSave.ResetForTests();
            Context = MusketeerArchive.ContextKey(NativeName, 0, 0, 0);
            var legacy = new MusketeerArchive();
            var oldRows = Enumerable.Range(0, 17).Select(i => new MusketeerCareer {
                Id = Guid.NewGuid(), Kind = MusketeerCareer.KindUnit, NativeId = "historical-host-" + i, StockSlot = -1 }).ToArray();
            Check(legacy.ConfirmBaseline(SourceEpoch, Hex('a'), oldRows), "legacy fixture records");
            Check(legacy.EnsureContext(Context, SourceEpoch, true), "legacy fixture context");
            LegacyBytes = legacy.Encode();
            File.WriteAllBytes(MusketeerPersistence.ArchivePath, LegacyBytes);
            InitialNative = Gzip(Global(Campaign(Island(0, "")), ""));
            File.WriteAllBytes(Disk, InitialNative);
            MusketeerNativeSave.ObserveContext(Context, 0, 0, 0);
            var plan = new MusketeerLegacyCompensationPlan(1, Request, Context, SourceEpoch,
                Convert.ToHexString(SHA256.HashData(LegacyBytes)).ToLowerInvariant(), 17, 8);
            Check(MusketeerLegacyCompensationPolicy.TryCreatePending(plan, Epoch, Grants, out Pending, out var why), "pending fixture: " + why);
            var captures = Grants.Select((id, i) => new MusketeerLegacyGrantCapture(id, NativeIds[i], 1, ArcherPrefab, 1)).ToArray();
            Check(MusketeerLegacyCompensationPolicy.TryComplete(Pending, captures, out Complete, out why), "complete fixture: " + why);
        }
        internal void Begin()
        {
            Check(MusketeerNativeSave.BeginLegacyCompensation(Request, Context, Epoch, Grants, out var why), "real Begin: " + why);
            Check(Owner.Protected.Count == 0 && Owner.Proofs.Count == 0, "Begin is before any protected Stage");
            Check(Owner.LegacyWriterBlocked && MusketeerNativeSave.AnyLegacyCompensationPending, "partial and purchase barriers acquired");
            Check(LeaseCount == 0, "Begin checks writer occupancy without taking a lease");
        }
        internal string IslandRows(int count = 8, string path = ArcherPrefab)
            => Island(0, string.Join(",", NativeIds.Take(count).Select(id => Row(id, path))));
        internal bool Stage(int count, bool includeReceipt, string path = ArcherPrefab, bool mismatchIds = false)
        {
            var read = MusketeerNativeSave.ReadArchive();
            var archive = read.Archive;
            var island = IslandRows(count, path);
            var hash = MusketeerArchive.IslandHash(island, Epoch);
            var rows = Grants.Take(count).Select((id, i) => new MusketeerCareer {
                Id = id, Kind = 1, NativeId = mismatchIds ? "mismatch-" + i : NativeIds[i], StockSlot = -1 }).ToArray();
            Check(archive.ConfirmBaseline(Epoch, hash, rows), "migration fixture records");
            Check(archive.EnsureContext(Context, Epoch, true), "migration fixture context");
            if (includeReceipt) archive.Compensations[Request] = Complete;
            return MusketeerNativeSave.Stage(Context, Epoch, hash, read, archive);
        }
        internal byte[] Payload(string island = null, bool includeKey = true)
            => Gzip(Global(Campaign(island ?? IslandRows()), "",
                includeKey && Raw != null ? new[] { Pref(MusketeerNativeArchive.Key, Raw) } : Array.Empty<string>()));
        internal Filer.DotNetAgent._SaveFileToDisk_d__10 Sync(byte[] payload, string filename = NativeName)
            => new() { __1__state = 0, filename = filename, data = payload, __4__this = new Filer.DotNetAgent { folder = Folder } };
        internal void AssertNoWriteOrAccept()
        {
            Check(File.ReadAllBytes(Disk).AsSpan().SequenceEqual(InitialNative), "no physical native write");
            Check(File.ReadAllBytes(MusketeerPersistence.ArchivePath).AsSpan().SequenceEqual(LegacyBytes), "legacy source unmodified");
            Check(!File.Exists(Pair), "no paired backup before accepted writer");
            Check(!MusketeerNativeSave.LegacyCompensationAccepted(Request, Context, Epoch), "no acceptance before verified disk Harvest");
        }
        internal void AssertPartialRejected()
        {
            var payload = Payload();
            foreach (bool sync in new[] { false, true })
            {
                var verdict = MusketeerNativeSave.GatePayload(Folder, NativeName, payload, sync, out var call, out var why);
                Check(verdict == MusketeerNativeSave.WriterVerdict.Fail && why == "LegacyCompensationPartial", "partial gate fails both physical writer shapes: " + why);
                Check(call == null && LeaseCount == 0, "rejection has no lease/attempt");
            }
            var instance = Sync(payload);
            bool result = true;
            bool run = MusketeerNativeSave.Musketeer_SyncWriterGate.Prefix(instance, ref result, out var state);
            Check(!run && !result && state == null, "sync body skipped before it can write");
            Check(MusketeerNativeSave.ReadSyncReturnCode(instance.@return) == MusketeerNativeSave.ResultFailureCode, "sync native field reports Failure 136");
            Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> task = null;
            run = MusketeerNativeSave.Musketeer_AsyncWriterGate.Prefix(new Filer.DotNetAgent { folder = Folder },
                NativeName, "", "", payload, ref task, out state);
            Check(!run && state == null && task != null && (int)task.Result == MusketeerNativeSave.ResultFailureCode, "async body skipped with actual rejection endpoint");
            Check(LeaseCount == 0, "adapter rejection keeps lease table empty");
            AssertNoWriteOrAccept();
        }
        public void Dispose()
        {
            GlobalSaveData.loaded = null;
            Directory.Delete(Root, true);
        }
    }

    static void CompleteStage(Host h, string path = ArcherPrefab)
    {
        h.Begin();
        Check(h.Stage(8, true, path), "source-linked real Complete8 Stage");
        Check(!Owner.LegacyWriterBlocked, "only complete matching eight-row proof clears partial writer barrier");
        Check(MusketeerNativeSave.AnyLegacyCompensationPending, "purchases remain pending after Stage");
        h.AssertNoWriteOrAccept();
    }

    static int Main()
    {
        Case("zero Protected still blocks both writers and has no physical write or lease", () => {
            using var h = new Host(); h.Begin(); h.AssertPartialRejected();
        });
        Case("empty provisional schema2 Stage keeps writer barrier", () => {
            using var h = new Host(); h.Begin(); Check(h.Stage(0, false), "provisional Stage succeeds");
            Check(Owner.LegacyWriterBlocked, "zero-row Stage does not release barrier"); h.AssertPartialRejected();
        });
        Case("complete ledger missing eighth witnessed grant is rejected before Stage and keeps barrier", () => {
            using var h = new Host(); h.Begin(); Check(!h.Stage(7, true), "schema3 without eighth grant witness cannot be staged");
            Check(h.Raw == null && Owner.Protected.Count == 0, "invalid ledger never publishes prefs or protected proof");
            Check(Owner.LegacyWriterBlocked, "missing eighth row keeps barrier"); h.AssertPartialRejected();
        });
        Case("eight rows without ledger cannot release barrier", () => {
            using var h = new Host(); h.Begin(); Check(h.Stage(8, false), "eight-row schema2 Stage"); h.AssertPartialRejected();
        });
        Case("eight row GUIDs with different native IDs cannot release barrier", () => {
            using var h = new Host(); h.Begin(); Check(h.Stage(8, true, mismatchIds: true), "mismatched native row Stage"); h.AssertPartialRejected();
        });
        Case("duplicate Begin or capture budget cannot replace the active barrier", () => {
            using var h = new Host(); h.Begin();
            Check(!MusketeerNativeSave.BeginLegacyCompensation(h.Request, h.Context, h.Epoch, h.Grants, out _), "same request Begin refused");
            var duplicate = h.Grants.ToArray(); duplicate[7] = duplicate[0];
            Check(!MusketeerNativeSave.BeginLegacyCompensation(Guid.NewGuid(), h.Context, h.Epoch, duplicate, out _), "duplicate grant Begin refused");
            var captures = h.Grants.Select((id, i) => new MusketeerLegacyGrantCapture(id, h.NativeIds[i], 1, ArcherPrefab, 1)).ToArray();
            captures[7] = captures[0];
            Check(!MusketeerLegacyCompensationPolicy.TryComplete(h.Pending, captures, out _, out _), "duplicate capture cannot create complete ledger");
            h.AssertPartialRejected();
        });
        Case("foreign file and folder retain pass-through while target remains blocked", () => {
            using var h = new Host(); h.Begin();
            var foreign = MusketeerNativeSave.GatePayload(h.Folder, "global-v36", h.Payload(), false, out var call, out _);
            Check(foreign == MusketeerNativeSave.WriterVerdict.Pass && call == null && LeaseCount == 0, "non-target file untouched");
            foreign = MusketeerNativeSave.GatePayload(Path.Combine(h.Root, "OtherFolder"), NativeName, h.Payload(), true, out call, out _);
            Check(foreign == MusketeerNativeSave.WriterVerdict.Pass && call == null && LeaseCount == 0, "other folder untouched");
            h.AssertPartialRejected();
        });
        Case("Complete8 Stage unlocks writing but only actual paired disk Harvest unlocks purchases", () => {
            using var h = new Host(); CompleteStage(h);
            var instance = h.Sync(h.Payload()); bool result = true;
            Check(MusketeerNativeSave.Musketeer_SyncWriterGate.Prefix(instance, ref result, out var call), "real sync Prefix accepts complete fixture");
            Check(call?.Attempt != null && LeaseCount == 1, "accepted writer owns exact lease and attempt");
            Check(!MusketeerNativeSave.LegacyCompensationAccepted(h.Request, h.Context, h.Epoch), "lease acquisition is not acceptance");
            File.WriteAllBytes(h.Disk, h.Payload()); // only the allowed host writer body is simulated
            instance.@return.value = (SaveLoadResult)MusketeerNativeSave.NoChangeCode;
            MusketeerNativeSave.Musketeer_SyncWriterGate.Finally(null, instance, call);
            Check(LeaseCount == 0 && File.Exists(h.Pair), "real finalizer releases lease and pairs validated disk bytes");
            Check(MusketeerNativeSave.LegacyCompensationAccepted(h.Request, h.Context, h.Epoch), "actual Harvest sets Accepted");
            Check(!MusketeerNativeSave.AnyLegacyCompensationPending, "purchase barrier clears only after Harvest");
            Check(!MusketeerNativeSave.HarvestAttempt(call.Attempt, MusketeerNativeSave.NoChangeCode), "same attempt cannot Harvest twice");
            Check(!MusketeerLegacyCompensationPolicy.TryComplete(h.Complete, Array.Empty<MusketeerLegacyGrantCapture>(), out _, out _), "completed ledger cannot reissue budget");
            Check(MusketeerNativeArchive.TryExtractDocument(File.ReadAllBytes(h.Disk), NativeName, out _, out var archive, out _, out _, out _), "same native gzip contains readable ledger");
            Check(archive.Compensations[h.Request].GrantIds.SequenceEqual(h.Grants), "exact eight GUID budget preserved");
            Check(archive.LatestReservations(h.SourceEpoch).Count == 17 && archive.HistoricalOnly(h.Context, h.SourceEpoch), "old seventeen rights retained as history");
        });
        foreach (string bad in new[] { "missing-key", "missing-row", "duplicate-row", "wrong-prefab", "invalid-gzip" })
            Case("bad outgoing " + bad + " never takes lease or accepts", () => {
                using var h = new Host(); CompleteStage(h, bad == "wrong-prefab" ? ToolBowPrefab : ArcherPrefab);
                byte[] payload = bad switch {
                    "missing-key" => h.Payload(includeKey: false),
                    "missing-row" => h.Payload(h.IslandRows(7)),
                    "duplicate-row" => h.Payload(Island(0, string.Join(",", h.NativeIds.Select(id => Row(id, ArcherPrefab))) + "," + Row(h.NativeIds[0], ArcherPrefab))),
                    "invalid-gzip" => new byte[] { 1, 2, 3 },
                    _ => h.Payload(h.IslandRows(8, ToolBowPrefab))
                };
                var verdict = MusketeerNativeSave.GatePayload(h.Folder, NativeName, payload, true, out var call, out _);
                Check(verdict == MusketeerNativeSave.WriterVerdict.Fail && call == null && LeaseCount == 0, "bad physical payload rejected before lease");
                Check(MusketeerNativeSave.AnyLegacyCompensationPending, "bad payload cannot unlock buying"); h.AssertNoWriteOrAccept();
            });
        Case("callback success with different disk bytes cannot accept compensation", () => {
            using var h = new Host(); CompleteStage(h);
            Il2CppSystem.Action<SaveLoadResult> callback = new((SaveLoadResult _) => { }); bool run = true;
            MusketeerNativeSave.Musketeer_SaveAsyncObserver.Prefix(GlobalSaveData.loaded, ref callback, ref run);
            var payload = h.Payload();
            var verdict = MusketeerNativeSave.GatePayload(h.Folder, NativeName, payload, false, out var call, out _);
            Check(verdict == MusketeerNativeSave.WriterVerdict.Pass, "real async gate accepts valid payload");
            MusketeerNativeSave.WriterStarted(call, new Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> { IsCompleted = true });
            // Keep the original physical file (the native key never reached disk).
            callback.Invoke((SaveLoadResult)MusketeerNativeSave.NoChangeCode);
            Check(MusketeerNativeSave.AnyLegacyCompensationPending, "callback result alone cannot unlock purchases"); h.AssertNoWriteOrAccept();
        });
        foreach (bool adapter in new[] { false, true })
            Case("old zero-rights " + (adapter ? "SaveAsync callback" : "BeginSaveObservation") + " cannot accept later compensation", () => {
                using var h = new Host();
                int forwards = 0;
                Il2CppSystem.Action<SaveLoadResult> original = new((SaveLoadResult result) => {
                    Check((int)result == MusketeerNativeSave.NoChangeCode, "old continuation receives original 72"); forwards++;
                });
                Il2CppSystem.Action<SaveLoadResult> callback = original;
                MusketeerNativeSave.SaveObservation observation;
                if (adapter)
                {
                    bool run = true;
                    Check(MusketeerNativeSave.Musketeer_SaveAsyncObserver.Prefix(GlobalSaveData.loaded, ref callback, ref run), "old SaveAsync prefix runs");
                    observation = ((IEnumerable)typeof(MusketeerNativeSave).GetField("PendingObservations", BindingFlags.NonPublic | BindingFlags.Static)
                        .GetValue(null)).Cast<MusketeerNativeSave.SaveObservation>().Single();
                }
                else observation = MusketeerNativeSave.BeginSaveObservation(GlobalSaveData.loaded);
                Check(observation != null && !observation.Rights.Protected && observation.Rights.Proofs.Length == 0
                    && observation.Rights.DocumentRaw == null, "observation froze zero-rights before compensation Begin");
                CompleteStage(h);
                File.WriteAllBytes(h.Disk, h.Payload()); // later valid host disk output, without a newer observer callback
                if (adapter) callback.Invoke((SaveLoadResult)MusketeerNativeSave.NoChangeCode);
                else MusketeerNativeSave.CompleteSaveObservation(observation, (SaveLoadResult)MusketeerNativeSave.NoChangeCode, original);
                Check(forwards == 1, "old continuation forwarded once");
                Check(!MusketeerNativeSave.LegacyCompensationAccepted(h.Request, h.Context, h.Epoch), "zero-rights old observer must not accept a newer complete receipt");
                Check(MusketeerNativeSave.AnyLegacyCompensationPending, "old callback cannot unlock purchasing");
            });
        Case("capture-start fence prevents pre-capture cancellation", () => {
            using var h = new Host(); h.Begin(); Check(h.Stage(0, false), "provisional Stage");
            MusketeerNativeSave.NoteLegacySceneCapture(h.Context);
            Check(!MusketeerNativeSave.CancelLegacyBeforeCapture(h.Request, h.Context, h.Epoch, out _), "scene capture forbids automatic cancel"); h.AssertPartialRejected();
        });
        Case("without compensation a zero-rights owner preserves old writer behavior", () => {
            using var h = new Host();
            var verdict = MusketeerNativeSave.GatePayload(h.Folder, NativeName, h.InitialNative, true, out var call, out _);
            Check(verdict == MusketeerNativeSave.WriterVerdict.Pass && call != null && LeaseCount == 1, "ordinary zero-rights writer accepted");
            MusketeerNativeSave.SyncWriterFinished(call); Check(LeaseCount == 0, "ordinary writer lease released"); h.AssertNoWriteOrAccept();
        });
        Console.WriteLine($"{cases} cases, {assertions} assertions, {failures} failures; HOST CONTRACTS ONLY");
        return failures == 0 ? 0 : 1;
    }
}
