using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Coatsink.Common;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using KingdomEnhancedMod;
using static MusketeerNativeSaveAdapterTests.Fx;

namespace MusketeerNativeSaveAdapterTests;

internal static class Check
{
    private static int _failures;
    private static int _cases;

    internal static void Case(string name) { _cases++; Console.WriteLine("[case] " + name); }

    internal static void True(bool condition, string label)
    {
        if (condition) return;
        _failures++;
        Console.WriteLine("  FAIL " + label);
    }

    internal static void False(bool condition, string label) => True(!condition, label);

    internal static void Eq(object actual, object expected, string label)
    {
        if (Equals(actual, expected)) return;
        _failures++;
        Console.WriteLine("  FAIL " + label + " expected=" + expected + " actual=" + actual);
    }

    internal static int Finish()
    {
        Console.WriteLine(_failures == 0
            ? "ALL PASS (" + _cases + " cases)"
            : _failures + " FAILURE(S) across " + _cases + " cases");
        return _failures == 0 ? 0 : 1;
    }
}

// Isolated host: temp folder + fresh loaded Global, exactly like the game's single loaded save.
internal sealed class Harness : IDisposable
{
    internal readonly string Root;
    internal readonly string Folder;
    internal readonly string File = Fx.NativeName;
    internal string DiskPath => MusketeerNativeSave.CanonicalPath(Folder, File);
    internal string PairPath => Path.Combine(Path.GetDirectoryName(MusketeerPersistence.ArchivePath), "musketeer-pair-" + File + ".json");

    internal Harness()
    {
        Root = Path.Combine(Path.GetTempPath(), "musk-save-adapter-" + Guid.NewGuid().ToString("N"));
        Folder = Path.Combine(Root, "Release");
        Directory.CreateDirectory(Folder);
        string modSave = Path.Combine(Root, "ModSave");
        Directory.CreateDirectory(modSave);
        MusketeerPersistence.ArchivePath = Path.Combine(modSave, "musketeer-identities.v2.json");
        GlobalSaveData.filename = File;
        GlobalSaveData.loaded = new GlobalSaveData();
        GlobalSaveData.loaded.prefs = new PrefsSaveData();
        Filer.folder = Folder;
        MusketeerNativeSave.ResetForTests();
    }

    internal GlobalSaveData Global => GlobalSaveData.loaded;
    internal CampaignSaveData Campaign(int index)
    {
        while (Global.campaigns.Count <= index) Global.campaigns.Add(new CampaignSaveData());
        return Global.campaigns[index];
    }

    public void Dispose()
    {
        GlobalSaveData.loaded = null;
        try { Directory.Delete(Root, true); } catch { }
    }
}

internal static class Program
{
    private static int Main()
    {
        Check.Case("read: native key wins over a valid legacy sidecar");
        ReadNativePriority();
        Check.Case("read: missing native key falls back to the legacy sidecar (migration)");
        ReadLegacyMigration();
        Check.Case("read: RecoveredBackup sidecar stays read-only");
        ReadRecoveredBackupReadOnly();
        Check.Case("read: corrupt / future native key has no fallback");
        ReadCorruptAndUnsupportedNoFallback();
        Check.Case("read: staged key changed or lost externally fails visible and preserves staged");
        ReadExternalChangeFailsVisible();
        Check.Case("stage: exact snapshot required (NoMatchingSnapshot / ScopeAmbiguous) and CAS");
        StageExactAndCas();
        Check.Case("gate: staged checkpoint + proof passes and stays byte-identical");
        GatePassAndCheckpoint();
        Check.Case("gate: missing / altered document is PrefsKeyLost");
        GatePrefsKeyLost();
        Check.Case("gate: island fingerprint, native id uniqueness and kind");
        GateIslandChecks();
        Check.Case("gate: provably deleted owner revokes the proof; unprovable source fails closed");
        GateCatalogRevokeAndFail();
        Check.Case("gate: challenge is matched by unique challengeId");
        GateChallengeMatch();
        Check.Case("gate: capture fault blocks until Stage clears it");
        GateCaptureFault();
        Check.Case("gate: no rights and non-owner files pass through untouched");
        GatePassThrough();
        Check.Case("writer lease: pending is Busy, terminal Task releases, sync mutex");
        LeaseSemantics();
        Check.Case("writer lease: sticky UnknownWriter (never Busy, never timed out)");
        UnknownWriterSticky();
        Check.Case("writer adapter: rejection before the native original; next valid save passes");
        AdapterRejectThenNextValid();
        Check.Case("sync writer adapter: boxed Failure return, lease release, Return==72 readback");
        SyncAdapter();
        Check.Case("harvest: accepted callback pairs the real disk file and forwards once");
        HarvestPairs();
        Check.Case("harvest: 72 no-change verifies the existing file (verified-existing)");
        HarvestNoChange();
        Check.Case("harvest: disk document mismatch and late callbacks never pair/mirror");
        HarvestMismatchAndLate();
        Check.Case("harvest: non-success results forward without pairing");
        HarvestRejectionsForward();
        Check.Case("legacy: unknown-paid records migrate untouched, no fabricated ids/base");
        LegacyUnknownPreserved();
        Check.Case("dropped gun keeps its GUID and per-island source isolation");
        DroppedGunPerIsland();
        Check.Case("mirror: corrupt sidecar is preserved, pair still saved");
        MirrorDegraded();
        Check.Case("review repro 1: H1 load -> H2 save must move the proof to H2");
        ReviewConsecutiveSaveMovesProof();
        Check.Case("review repro 2: first Stage without an extra SeedProof must still protect rows");
        ReviewFirstStageProtects();
        Check.Case("review repro 3: duplicate callback forwards the original continuation once");
        ReviewDuplicateCallbackOnce();
        Check.Case("review repro 4: provably deleted source clears its capture fault");
        ReviewDeletedSourceClearsFault();
        Check.Case("pair: missing native key recovers only from the byte-identical disk file");
        PairRecovery();
        Check.Case("owner: a replaced Global is never gated by the stale owner");
        StaleOwnerBypass();
        Check.Case("frozen accept: a callback after a later Stage pairs its own attempt");
        FrozenAcceptAfterLaterStage();
        Check.Case("reject endpoint: unavailable endpoint refuses new staging instead of faking Failure");
        RejectEndpointUnavailable();
        Check.Case("residual: capacity keeps every unfinished delegate root and refuses new wrapping");
        ResidualCapacityKeepsRoots();
        Check.Case("residual: reordered callbacks never mis-attribute a pair");
        ResidualReorderedCallbacks();
        Check.Case("residual: entering factory window is Busy and a real terminal witness clears it");
        ResidualEnteringWindowBusy();
        Check.Case("residual: provably deleted challenge source revokes like a campaign source");
        ResidualDeletedChallengeSource();
        Check.Case("residual: a rooted Task survives another patch's finalizer exception");
        ResidualKnownTaskException();
        return Check.Finish();
    }

    // ---------------------------------------------------------------- helpers

    private sealed class Seeded
    {
        internal string Context;
        internal string Scope;
        internal string Hash;
        internal MusketeerArchive Archive;
    }

    private static Seeded Seed(Harness h, int campaign, int challenge, int land, string islandJson,
        List<MusketeerCareer> records)
    {
        if (challenge == 0) h.Campaign(campaign);
        string context = MusketeerArchive.ContextKey(h.File, campaign, challenge, land);
        MusketeerNativeSave.ObserveContext(context, campaign, challenge, land);
        string scope = MusketeerArchive.NewScope();
        string hash = MusketeerArchive.IslandHash(islandJson, scope);
        var archive = new MusketeerArchive();
        Check.True(archive.Record(scope, hash, records), "record snapshot");
        Check.True(archive.EnsureContext(context, scope, true), "ensure context");
        var expected = MusketeerNativeSave.ReadArchive();
        Check.True(MusketeerNativeSave.Stage(context, scope, hash, expected, archive), "stage checkpoint");
        return new Seeded { Context = context, Scope = scope, Hash = hash, Archive = archive };
    }

    private static string StagedDocument()
    {
        GlobalSaveData.loaded.prefs.contents.TryGetValue(MusketeerNativeArchive.Key, out string doc);
        return doc;
    }

    private static byte[] PayloadWithStaged(string campaignsJson, string challengesJson = "")
    {
        string doc = StagedDocument();
        return doc == null
            ? Gzip(Global(campaignsJson, challengesJson))
            : Gzip(Global(campaignsJson, challengesJson, Pref(MusketeerNativeArchive.Key, doc)));
    }

    private static MusketeerCareer Career(int kind, string nativeId, int stockSlot = -1)
        => new() { Id = Guid.NewGuid(), Kind = kind, NativeId = nativeId ?? "", StockSlot = stockSlot };

    private static (bool Run, Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> Task, MusketeerNativeSave.WriterCall Call)
        GateAsync(Harness h, byte[] payload, string folder = null)
    {
        var agent = new Filer.DotNetAgent { folder = folder ?? h.Folder };
        var data = new Il2CppStructArray<byte>(payload);
        Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> result = null;
        bool run = MusketeerNativeSave.Musketeer_AsyncWriterGate.Prefix(agent, h.File, "title", "details", data, ref result, out var state);
        return (run, result, state);
    }

    // The real accepted async flow: SaveAsync prefix wraps the callback, the writer gate accepts,
    // the postfix witnesses a completed Task, the native body writes the payload, and the wrapped
    // callback then verifies/pairs the actual disk file. Returns whether a pair file exists.
    private static bool AcceptedAsync(Harness h, byte[] payload, int result = 72, byte[] diskPayload = null)
    {
        Il2CppSystem.Action<SaveLoadResult> callback = new Il2CppSystem.Action<SaveLoadResult>((SaveLoadResult r) => { });
        bool runOriginal = true;
        MusketeerNativeSave.Musketeer_SaveAsyncObserver.Prefix(h.Global, ref callback, ref runOriginal);
        var gate = GateAsync(h, payload);
        if (!gate.Run) return false;
        MusketeerNativeSave.Musketeer_AsyncWriterGate.Postfix(gate.Call,
            new Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> { IsCompleted = true });
        File.WriteAllBytes(h.DiskPath, diskPayload ?? payload);
        callback.Invoke((SaveLoadResult)result);
        return File.Exists(h.PairPath);
    }

    // Direct core-gate call that plays the writer lifecycle (the real adapter does this in its
    // postfix): a passing call immediately witnesses a completed Task, so the path is free again.
    private static MusketeerNativeSave.WriterVerdict Gate(Harness h, byte[] payload,
        out MusketeerNativeSave.WriterCall call, out string reason, string folder = null, bool sync = false)
    {
        var verdict = MusketeerNativeSave.GatePayload(folder ?? h.Folder, h.File, payload, sync, out call, out reason);
        if (verdict == MusketeerNativeSave.WriterVerdict.Pass && call != null && !sync)
            MusketeerNativeSave.WriterStarted(call, new Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> { IsCompleted = true });
        return verdict;
    }

    private static Filer.DotNetAgent._SaveFileToDisk_d__10 SyncInstance(Harness h, byte[] payload, string folder = null)
        => new()
        {
            __1__state = 0,
            filename = h.File,
            data = payload,
            __4__this = new Filer.DotNetAgent { folder = folder ?? h.Folder }
        };

    private static string IslandWithGun(int land, string gunId, string gunName = "Dropped ToolBow")
        => Island(land, Row(gunId, ToolBowPrefab, gunName));

    // ---------------------------------------------------------------- cases

    private static void ReadNativePriority()
    {
        using var h = new Harness();
        // A valid legacy sidecar exists, but the loaded native key must win.
        var legacy = new MusketeerArchive();
        string legacyScope = Hex('a');
        string legacyHash = Hex('b');
        Check.True(legacy.Record(legacyScope, legacyHash, new List<MusketeerCareer> { Career(MusketeerCareer.KindGun, "") }), "legacy record");
        File.WriteAllBytes(MusketeerPersistence.ArchivePath, legacy.Encode());

        var native = new MusketeerArchive();
        string nativeScope = Hex('c');
        string nativeHash = Hex('d');
        Check.True(native.Record(nativeScope, nativeHash, new List<MusketeerCareer>()), "native record");
        string nativeContext = MusketeerArchive.ContextKey(h.File, 0, 0, 0);
        Check.True(native.EnsureContext(nativeContext, nativeScope, true), "native context");
        Check.True(MusketeerNativeArchive.TryEncode(h.File, native, out string raw, out _), "encode native doc");
        h.Global.prefs.contents[MusketeerNativeArchive.Key] = raw;

        var read = MusketeerNativeSave.ReadArchive();
        Check.Eq(read.Status, MusketeerArchiveStore.State.Valid, "native read status");
        Check.True(read.Archive.Scopes.ContainsKey(nativeScope), "native archive wins");
        Check.False(read.Archive.Scopes.ContainsKey(legacyScope), "legacy not adopted");
        Check.Eq(read.RecoveredBackup, false, "not recovered backup");
    }

    private static void ReadLegacyMigration()
    {
        using var h = new Harness();
        var legacy = new MusketeerArchive();
        string scope = Hex('e');
        string hash = Hex('f');
        string context = MusketeerArchive.ContextKey(h.File, 0, 0, 0);
        Check.True(legacy.Record(scope, hash, new List<MusketeerCareer> { Career(MusketeerCareer.KindUnit, "Archer P7 [22]--1") }), "legacy record");
        Check.True(legacy.EnsureContext(context, scope, true), "legacy context");
        byte[] bytes = legacy.Encode();
        File.WriteAllBytes(MusketeerPersistence.ArchivePath, bytes);

        var read = MusketeerNativeSave.ReadArchive();
        Check.Eq(read.Status, MusketeerArchiveStore.State.Valid, "migration status");
        Check.True(read.Writable, "migration writable");
        Check.True(read.Archive.Scopes.ContainsKey(scope), "legacy archive surfaced");
        Check.Eq(read.Original?.Length, bytes.Length, "original bytes carried for CAS");
    }

    private static void ReadRecoveredBackupReadOnly()
    {
        using var h = new Harness();
        var archive = new MusketeerArchive();
        string scope = Hex('1');
        string hash = Hex('2');
        Check.True(archive.Record(scope, hash, new List<MusketeerCareer>()), "record");
        File.WriteAllBytes(MusketeerPersistence.ArchivePath + ".bak", archive.Encode());
        File.WriteAllBytes(MusketeerPersistence.ArchivePath, Encoding.UTF8.GetBytes("{\"broken\":1}"));

        var read = MusketeerNativeSave.ReadArchive();
        Check.Eq(read.Status, MusketeerArchiveStore.State.Valid, "bak read");
        Check.True(read.RecoveredBackup, "recovered backup flagged");
        Check.False(read.Writable, "recovered backup stays read-only");
    }

    private static void ReadCorruptAndUnsupportedNoFallback()
    {
        using var h = new Harness();
        var legacy = new MusketeerArchive();
        string scope = Hex('3');
        Check.True(legacy.Record(scope, Hex('4'), new List<MusketeerCareer>()), "legacy record");
        File.WriteAllBytes(MusketeerPersistence.ArchivePath, legacy.Encode());

        h.Global.prefs.contents[MusketeerNativeArchive.Key] = "{\"version\":1,\"file\":\"" + h.File + "\",\"archive\":\"%%% \"}";
        var corrupt = MusketeerNativeSave.ReadArchive();
        Check.Eq(corrupt.Status, MusketeerArchiveStore.State.Corrupt, "corrupt native key status");
        Check.False(corrupt.Writable, "corrupt native key not writable");
        Check.True(corrupt.Archive == null, "no legacy fallback for corrupt native key");

        h.Global.prefs.contents[MusketeerNativeArchive.Key] = "{\"version\":9,\"file\":\"" + h.File + "\",\"archive\":\"\"}";
        var future = MusketeerNativeSave.ReadArchive();
        Check.Eq(future.Status, MusketeerArchiveStore.State.Unsupported, "future native key unsupported");
        Check.False(future.Writable, "future native key not writable");
        Check.True(future.Archive == null, "no fallback for future native key");
    }

    private static void ReadExternalChangeFailsVisible()
    {
        using var h = new Harness();
        var seeded = Seed(h, 0, 0, 0, Island(0, ""), new List<MusketeerCareer>());
        string staged = StagedDocument();
        Check.True(staged != null, "staged document exists");

        string other = "{\"version\":1,\"file\":\"" + h.File + "\",\"archive\":\"AAAA\"}";
        h.Global.prefs.contents[MusketeerNativeArchive.Key] = other;
        var changed = MusketeerNativeSave.ReadArchive();
        Check.Eq(changed.Status, MusketeerArchiveStore.State.Corrupt, "external change reported");
        Check.False(changed.Writable, "external change not writable");
        Check.False(MusketeerNativeSave.Stage(seeded.Context, seeded.Scope, seeded.Hash, changed, seeded.Archive), "stage refused on changed source");

        // The staged record is untouched: a payload carrying the external bytes fails closed,
        // one carrying the staged document still validates.
        byte[] externalPayload = Gzip(Global(Campaign(Island(0, "")), "", Pref(MusketeerNativeArchive.Key, other)));
        var verdict = Gate(h, externalPayload, out _, out string reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Fail, "external bytes fail the gate");
        Check.Eq(reason, MusketeerNativeSave.ReasonPrefsKeyLost, "external bytes are PrefsKeyLost");
        byte[] stagedPayload = Gzip(Global(Campaign(Island(0, "")), "", Pref(MusketeerNativeArchive.Key, staged)));
        Check.Eq(Gate(h, stagedPayload, out _, out reason),
            MusketeerNativeSave.WriterVerdict.Pass, "staged record still the authority: " + reason);

        h.Global.prefs.contents.Remove(MusketeerNativeArchive.Key);
        var lost = MusketeerNativeSave.ReadArchive();
        Check.Eq(lost.Status, MusketeerArchiveStore.State.Corrupt, "key loss reported");
        Check.False(lost.Writable, "key loss not writable");
    }

    private static void StageExactAndCas()
    {
        using var h = new Harness();
        string context = MusketeerArchive.ContextKey(h.File, 0, 0, 0);
        MusketeerNativeSave.ObserveContext(context, 0, 0, 0);
        string scope = MusketeerArchive.NewScope();
        string hash = MusketeerArchive.IslandHash(Island(0, ""), scope);
        var archive = new MusketeerArchive();
        Check.True(archive.Record(scope, hash, new List<MusketeerCareer>()), "record");

        // No context mapping yet: Stage must refuse the unmapped scope.
        var expected = MusketeerNativeSave.ReadArchive();
        Check.False(MusketeerNativeSave.Stage(context, scope, hash, expected, archive), "stage without context mapping refused");

        Check.True(archive.EnsureContext(context, scope, true), "ensure context");
        string unseen = MusketeerArchive.ContextKey(h.File, 1, 0, 0);
        Check.False(MusketeerNativeSave.Stage(unseen, scope, hash, expected, archive), "stage without a witnessed source refused");
        var wrongHash = Hex('9');
        Check.False(MusketeerNativeSave.Stage(context, scope, wrongHash, expected, archive), "NoMatchingSnapshot on absent exact hash");
        Check.False(MusketeerNativeSave.Stage(context, Hex('8'), hash, expected, archive), "ScopeAmbiguous when the epoch is not this context's");

        var stale = new MusketeerArchiveStore.ReadResult { Status = MusketeerArchiveStore.State.Valid, Archive = archive, Original = Encoding.UTF8.GetBytes("{\"stale\":true}") };
        Check.False(MusketeerNativeSave.Stage(context, scope, hash, stale, archive), "CAS refuses a stale expected read");

        Check.True(MusketeerNativeSave.Stage(context, scope, hash, expected, archive), "stage succeeds on the exact read");
        string staged = StagedDocument();
        Check.True(staged != null, "staged document published");
        Check.True(MusketeerNativeArchive.TryExtract(Gzip(Global(Campaign(Island(0, "")), "", Pref(MusketeerNativeArchive.Key, staged))), h.File,
            out string raw, out var roundTrip, out _, out string reason), "staged doc round-trips through the native payload: " + reason);
        Check.Eq(raw, staged, "raw identical");
        Check.True(roundTrip.Scopes.ContainsKey(scope), "archive identical");
    }

    private static void GatePassAndCheckpoint()
    {
        using var h = new Harness();
        string island = Island(0, Row("Archer P7 [22]--1", ArcherPrefab));
        var records = new List<MusketeerCareer> { Career(MusketeerCareer.KindUnit, "Archer P7 [22]--1") };
        Seed(h, 0, 0, 0, island, records);

        byte[] payload = PayloadWithStaged(Campaign(island));
        File.WriteAllBytes(h.DiskPath, payload);
        var verdict = Gate(h, payload, out var call, out string reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Pass, "valid payload passes: " + reason);
        Check.True(call != null, "writer call captured");

        // The async path carries no attempt token: the observation freezes the exact staged raw
        // and the proofs, and the callback validates the disk content against that snapshot.
        var observation = MusketeerNativeSave.BeginSaveObservation(h.Global);
        Check.True(observation != null, "observation captured");
        Check.Eq(observation.Rights.DocumentRaw, StagedDocument(), "observation freezes the exact staged raw");
        Check.True(observation.Rights.Proofs.Length > 0, "observation freezes the proofs");
    }

    private static void GatePrefsKeyLost()
    {
        using var h = new Harness();
        Seed(h, 0, 0, 0, Island(0, ""), new List<MusketeerCareer>());

        byte[] withoutKey = Gzip(Global(Campaign(Island(0, "")), ""));
        var verdict = Gate(h, withoutKey, out _, out string reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Fail, "missing key fails closed");
        Check.Eq(reason, MusketeerNativeSave.ReasonPrefsKeyLost, "reason PrefsKeyLost");

        string staged = StagedDocument();
        byte[] altered = Gzip(Global(Campaign(Island(0, "")), "", Pref(MusketeerNativeArchive.Key, staged.Substring(0, staged.Length - 2) + "  }")));
        verdict = Gate(h, altered, out _, out reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Fail, "altered document fails closed");
        Check.Eq(reason, MusketeerNativeSave.ReasonPrefsKeyLost, "altered document is PrefsKeyLost");
    }

    private static void GateIslandChecks()
    {
        using var h = new Harness();

        // The witness row for the recorded native id is absent from the island that carries the hash.
        Seed(h, 0, 0, 0, Island(0, ""), new List<MusketeerCareer> { Career(MusketeerCareer.KindUnit, "Ghost--1") });
        var verdict = Gate(h, PayloadWithStaged(Campaign(Island(0, ""))), out _, out string reason);
        Check.Eq(reason, "NativeIdMissing", "missing witness row refused");

        // Two rows share the witnessed id.
        string duplicated = Island(0, Row("Archer P7 [22]--1", ArcherPrefab) + "," + Row("Archer P7 [22]--1", ArcherPrefab));
        Seed(h, 0, 0, 0, duplicated, new List<MusketeerCareer> { Career(MusketeerCareer.KindUnit, "Archer P7 [22]--1") });
        verdict = Gate(h, PayloadWithStaged(Campaign(duplicated)), out _, out reason);
        Check.Eq(reason, "NativeIdDuplicate", "duplicate witness row refused");

        // The row is a ToolBow while the career is recorded as a unit (kind mismatch).
        string wrongKind = Island(0, Row("Archer P7 [22]--1", ToolBowPrefab));
        Seed(h, 0, 0, 0, wrongKind, new List<MusketeerCareer> { Career(MusketeerCareer.KindUnit, "Archer P7 [22]--1") });
        verdict = Gate(h, PayloadWithStaged(Campaign(wrongKind)), out _, out reason);
        Check.Eq(reason, "NativeIdKind", "kind mismatch refused");

        // A different island JSON no longer fingerprints to the proof hash.
        string island = Island(0, Row("Archer P7 [22]--1", ArcherPrefab));
        Seed(h, 0, 0, 0, island, new List<MusketeerCareer> { Career(MusketeerCareer.KindUnit, "Archer P7 [22]--1") });
        verdict = Gate(h, PayloadWithStaged(Campaign(Island(0, Row("Archer P7 [22]--2", ArcherPrefab)))), out _, out reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Fail, "fingerprint mismatch fails");
        Check.Eq(reason, MusketeerNativeSave.ReasonNoMatchingSnapshot, "fingerprint mismatch reason");
    }

    private static void GateCatalogRevokeAndFail()
    {
        using var h = new Harness();
        string island = Island(0, "");
        Seed(h, 0, 0, 0, island, new List<MusketeerCareer>());
        byte[] payload = PayloadWithStaged(Campaign(island));

        // Source provably deleted from a successful catalog read: proof revoked, payload passes.
        h.Global.campaigns.Clear();
        var verdict = Gate(h, payload, out _, out string reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Pass, "deleted owner revokes the proof: " + reason);

        // A fresh proof, but the live catalog cannot be read: fail closed.
        MusketeerNativeSave.ResetForTests();
        GlobalSaveData.loaded = h.Global;
        Seed(h, 0, 0, 0, island, new List<MusketeerCareer>());
        byte[] restaged = PayloadWithStaged(Campaign(island));
        h.Global.campaigns = null;
        verdict = Gate(h, restaged, out _, out reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Fail, "unreadable catalog fails closed");
        Check.Eq(reason, MusketeerNativeSave.ReasonCatalogInconsistent, "reason CatalogInconsistent");
    }

    private static void GateChallengeMatch()
    {
        using var h = new Harness();
        string island = Island(0, Row("Archer P7 [22]--1", ArcherPrefab));
        var records = new List<MusketeerCareer> { Career(MusketeerCareer.KindUnit, "Archer P7 [22]--1") };
        string context = MusketeerArchive.ContextKey(h.File, 0, 7, 0);
        MusketeerNativeSave.ObserveContext(context, 0, 7, 0);
        string scope = MusketeerArchive.NewScope();
        string hash = MusketeerArchive.IslandHash(island, scope);
        var archive = new MusketeerArchive();
        Check.True(archive.Record(scope, hash, records), "challenge record");
        Check.True(archive.EnsureContext(context, scope, true), "challenge context");
        Check.True(MusketeerNativeSave.Stage(context, scope, hash, MusketeerNativeSave.ReadArchive(), archive), "challenge stage");
        MusketeerNativeSave.SeedProof(context, scope, hash);

        // Unique challengeId match: pass.
        byte[] payload = PayloadWithStaged(Campaign(Island(0, ""), challengeId: 0) + "," + Campaign(island, challengeId: 7));
        var verdict = Gate(h, payload, out _, out string reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Pass, "unique challengeId match passes: " + reason);

        // No challenge entry for that id: CatalogInconsistent (never index-guessed).
        byte[] noEntry = PayloadWithStaged(Campaign(island, challengeId: 0));
        verdict = Gate(h, noEntry, out _, out reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Fail, "missing challenge entry fails");
        Check.Eq(reason, MusketeerNativeSave.ReasonCatalogInconsistent, "missing challengeId is CatalogInconsistent");

        // Two entries with the same challengeId: ambiguous, fail closed.
        byte[] ambiguous = PayloadWithStaged(Campaign(island, challengeId: 7) + "," + Campaign(island, challengeId: 7));
        verdict = Gate(h, ambiguous, out _, out reason);
        Check.Eq(reason, MusketeerNativeSave.ReasonCatalogInconsistent, "duplicate challengeId is CatalogInconsistent");
    }

    private static void GateCaptureFault()
    {
        using var h = new Harness();
        string island = Island(0, "");
        var seeded = Seed(h, 0, 0, 0, island, new List<MusketeerCareer>());
        byte[] payload = PayloadWithStaged(Campaign(island));

        MusketeerNativeSave.NoteCaptureFailure(seeded.Context);
        var verdict = Gate(h, payload, out _, out string reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Fail, "capture fault blocks the writer");
        Check.Eq(reason, MusketeerNativeSave.ReasonCaptureFault, "reason CaptureFault");

        // A successful Stage for that context clears the fault.
        Check.True(MusketeerNativeSave.Stage(seeded.Context, seeded.Scope, seeded.Hash, MusketeerNativeSave.ReadArchive(), seeded.Archive), "re-stage");
        verdict = Gate(h, PayloadWithStaged(Campaign(island)), out _, out reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Pass, "fault cleared by stage");
    }

    private static void GatePassThrough()
    {
        using var h = new Harness();
        Seed(h, 0, 0, 0, Island(0, ""), new List<MusketeerCareer>());

        // A non-owner file (settings/other mod data) is never validated, leased or rewritten.
        byte[] foreign = Gzip("{\"prefs\":{\"srzEntries\":[]}}");
        var verdict = MusketeerNativeSave.GatePayload(h.Folder, "settings.txt", foreign, false, out var call, out string reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Pass, "non-owner file passes: " + reason);
        Check.True(call == null, "non-owner file takes no lease/attempt");

        // Nothing protected yet: a payload without the key (or with a foreign corrupt key) passes
        // through unchanged even for the owner file.
        MusketeerNativeSave.ResetForTests();
        var fresh = new Harness();
        byte[] noRights = Gzip(Global(Campaign(Island(0, "")), "", Pref("KEM.HeavyShield.Campaigns.v1", "{\"version\":2}")));
        verdict = Gate(fresh, noRights, out _, out reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Pass, "no-rights payload passes through: " + reason);
        fresh.Dispose();
        h.Dispose();
    }

    private static void LeaseSemantics()
    {
        using var h = new Harness();
        string island = Island(0, "");
        Seed(h, 0, 0, 0, island, new List<MusketeerCareer>());
        byte[] payload = PayloadWithStaged(Campaign(island));

        var first = GateAsync(h, payload);
        Check.True(first.Run, "first async pass");
        var running = new Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> { IsCompleted = false };
        MusketeerNativeSave.Musketeer_AsyncWriterGate.Postfix(first.Call, running);

        var second = GateAsync(h, payload);
        Check.False(second.Run, "re-entry while pending is refused");
        Check.Eq((int)second.Task.Result, MusketeerNativeSave.ResultBusyCode, "pending returns Save|Failure|Busy");

        // A sync writer for the same canonical path must also back off while pending.
        var sync = SyncInstance(h, payload);
        bool syncRun = true;
        bool syncShouldRun = MusketeerNativeSave.Musketeer_SyncWriterGate.Prefix(sync, ref syncRun, out _);
        Check.False(syncShouldRun, "sync writer refused while async pending");
        Check.Eq((int)sync.@return.value, MusketeerNativeSave.ResultBusyCode, "sync busy result");
        Check.Eq(sync.__1__state, -1, "sync rejected call ends the coroutine");

        // Terminal witness releases the lease.
        running.IsCompleted = true;
        var third = GateAsync(h, payload);
        Check.True(third.Run, "terminal task releases the lease");
    }

    private static void UnknownWriterSticky()
    {
        using var h = new Harness();
        string island = Island(0, "");
        Seed(h, 0, 0, 0, island, new List<MusketeerCareer>());
        byte[] payload = PayloadWithStaged(Campaign(island));

        var first = GateAsync(h, payload);
        Check.True(first.Run, "first async pass");
        MusketeerNativeSave.Musketeer_AsyncWriterGate.Finally(new InvalidOperationException("factory"), first.Call);

        var second = GateAsync(h, payload);
        Check.False(second.Run, "unknown writer blocks the next request");
        Check.Eq((int)second.Task.Result, MusketeerNativeSave.ResultFailureCode, "unknown writer returns Save|Failure, not Busy");

        // Other folders are not this owner's physical save: they bypass the gate entirely.
        var other = GateAsync(h, payload, Path.Combine(h.Root, "OtherRelease"));
        Check.True(other.Run, "other canonical path bypasses");
        Check.True(other.Call == null, "other canonical path takes no lease/attempt");
    }

    private static void AdapterRejectThenNextValid()
    {
        using var h = new Harness();
        string island = Island(0, Row("Archer P7 [22]--1", ArcherPrefab));
        var records = new List<MusketeerCareer> { Career(MusketeerCareer.KindUnit, "Archer P7 [22]--1") };
        Seed(h, 0, 0, 0, island, records);

        // Broken island rows: the adapter must reject before the native original runs.
        byte[] broken = PayloadWithStaged(Campaign(Island(0, "")));
        var rejected = GateAsync(h, broken);
        Check.False(rejected.Run, "rejected payload skips the native original");
        Check.Eq((int)rejected.Task.Result, MusketeerNativeSave.ResultFailureCode, "rejection completes Save|Failure");

        // Rejection must not have taken a lease: the next valid save goes straight through.
        byte[] valid = PayloadWithStaged(Campaign(island));
        var accepted = GateAsync(h, valid);
        Check.True(accepted.Run, "next valid save passes without Busy");
    }

    private static void SyncAdapter()
    {
        using var h = new Harness();
        string island = Island(0, Row("Archer P7 [22]--1", ArcherPrefab));
        var records = new List<MusketeerCareer> { Career(MusketeerCareer.KindUnit, "Archer P7 [22]--1") };
        Seed(h, 0, 0, 0, island, records);

        byte[] broken = PayloadWithStaged(Campaign(Island(0, "")));
        var rejected = SyncInstance(h, broken);
        bool result = true;
        bool run = MusketeerNativeSave.Musketeer_SyncWriterGate.Prefix(rejected, ref result, out var rejectedState);
        Check.False(run, "sync rejection skips the body");
        Check.False(result, "__result false");
        Check.Eq(rejected.__1__state, -1, "state finalised");
        Check.Eq((int)rejected.@return.value, MusketeerNativeSave.ResultFailureCode, "boxed return carries Save|Failure");
        Check.True(rejectedState == null, "rejected sync call takes no lease");
        MusketeerNativeSave.Musketeer_SyncWriterGate.Finally(null, rejected, rejectedState);

        // Next valid synchronous save: passes, writes the file, releases the lease and (Return==72)
        // verifies the readback into a pair.
        byte[] valid = PayloadWithStaged(Campaign(island));
        var accepted = SyncInstance(h, valid);
        result = true;
        run = MusketeerNativeSave.Musketeer_SyncWriterGate.Prefix(accepted, ref result, out var state);
        Check.True(run, "valid sync save passes");
        Check.True(state != null, "sync lease captured");
        File.WriteAllBytes(h.DiskPath, valid);                 // the native body writes directly
        accepted.@return.value = SaveLoadResult.Save | SaveLoadResult.Success;
        MusketeerNativeSave.Musketeer_SyncWriterGate.Finally(null, accepted, state);
        Check.True(File.Exists(h.PairPath), "sync success produced the readback pair");

        // The sync lease is gone: the next async save passes.
        var next = GateAsync(h, PayloadWithStaged(Campaign(island)));
        Check.True(next.Run, "sync lease released by its finalizer");
    }

    private static void HarvestPairs()
    {
        using var h = new Harness();
        string island = Island(0, "");
        Seed(h, 0, 0, 0, island, new List<MusketeerCareer>());
        byte[] payload = PayloadWithStaged(Campaign(island));
        File.WriteAllBytes(h.DiskPath, payload);

        var gate = GateAsync(h, payload);
        Check.True(gate.Run, "gate pass");

        int forwarded = 0;
        SaveLoadResult forwardedResult = SaveLoadResult.None;
        Il2CppSystem.Action<SaveLoadResult> callback = new Il2CppSystem.Action<SaveLoadResult>((SaveLoadResult r) => { forwarded++; forwardedResult = r; });
        var original = callback;
        bool runOriginal = true;
        bool keep = MusketeerNativeSave.Musketeer_SaveAsyncObserver.Prefix(h.Global, ref callback, ref runOriginal);
        Check.True(keep, "observer keeps the original running");
        Check.False(ReferenceEquals(callback, original), "callback was wrapped");
        callback.Invoke(SaveLoadResult.Save | SaveLoadResult.Success);
        Check.Eq(forwarded, 1, "original callback forwarded exactly once");
        Check.Eq((int)forwardedResult, 72, "result forwarded unchanged");
        Check.True(File.Exists(h.PairPath), "paired container written from the real disk file");
        Check.True(File.Exists(MusketeerPersistence.ArchivePath), "legacy sidecar mirrored");
        Check.True(MusketeerPairedBackup.TryRecover(h.PairPath, h.File, payload, out _, out var recovered, out string recoverReason),
            "pair recovers the archive: " + recoverReason);
        Check.True(recovered.Scopes.Count > 0, "recovered archive carries scopes");
    }

    private static void HarvestNoChange()
    {
        using var h = new Harness();
        string island = Island(0, "");
        Seed(h, 0, 0, 0, island, new List<MusketeerCareer>());
        byte[] payload = PayloadWithStaged(Campaign(island));
        File.WriteAllBytes(h.DiskPath, payload);

        // No writer ran (serializer no-change): the existing disk file is verified as-is.
        int forwarded = 0;
        Il2CppSystem.Action<SaveLoadResult> callback = new Il2CppSystem.Action<SaveLoadResult>((SaveLoadResult r) => { forwarded++; });
        bool runOriginal = true;
        MusketeerNativeSave.Musketeer_SaveAsyncObserver.Prefix(h.Global, ref callback, ref runOriginal);
        callback.Invoke((SaveLoadResult)MusketeerNativeSave.NoChangeCode);
        Check.Eq(forwarded, 1, "72 forwarded once");
        Check.True(File.Exists(h.PairPath), "72 verified-existing produced a pair");
    }

    private static void HarvestMismatchAndLate()
    {
        using var h = new Harness();
        string island = Island(0, "");
        Seed(h, 0, 0, 0, island, new List<MusketeerCareer>());
        byte[] payload = PayloadWithStaged(Campaign(island));
        File.WriteAllBytes(h.DiskPath, payload);

        // The disk no longer carries this observation's frozen document: never pair, never mirror.
        byte[] different = Gzip(Global(Campaign(island), "",
            Pref(MusketeerNativeArchive.Key, MusketeerNativeArchive.TryEncode(h.File, new MusketeerArchive(), out string emptyDoc, out _) ? emptyDoc : "")));
        int forwarded = 0;
        Il2CppSystem.Action<SaveLoadResult> callback = new Il2CppSystem.Action<SaveLoadResult>((SaveLoadResult r) => { forwarded++; });
        bool runOriginal = true;
        MusketeerNativeSave.Musketeer_SaveAsyncObserver.Prefix(h.Global, ref callback, ref runOriginal);
        var gate = GateAsync(h, payload);
        Check.True(gate.Run, "gate pass");
        MusketeerNativeSave.Musketeer_AsyncWriterGate.Postfix(gate.Call,
            new Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> { IsCompleted = true });
        File.WriteAllBytes(h.DiskPath, different);
        callback.Invoke((SaveLoadResult)MusketeerNativeSave.NoChangeCode);
        Check.Eq(forwarded, 1, "mismatch still forwards the result once");
        Check.False(File.Exists(h.PairPath), "different disk content never pairs");

        // Late callback: an older sequence for the same owner is dropped after a newer harvest and
        // the pair still mirrors the newest accepted bytes.
        File.WriteAllBytes(h.DiskPath, payload);
        Check.True(AcceptedAsync(h, payload), "newest accepted save pairs");
        byte[] newest = File.ReadAllBytes(h.DiskPath);
        Check.True(MusketeerPairedBackup.TryRecover(h.PairPath, h.File, newest, out _, out _, out string reason),
            "pair recovers the newest accepted bytes: " + reason);
    }

    private static void HarvestRejectionsForward()
    {
        using var h = new Harness();
        Seed(h, 0, 0, 0, Island(0, ""), new List<MusketeerCareer>());
        byte[] payload = PayloadWithStaged(Campaign(Island(0, "")));
        File.WriteAllBytes(h.DiskPath, payload);

        foreach (SaveLoadResult result in new[]
        {
            SaveLoadResult.Save | SaveLoadResult.Failure,
            SaveLoadResult.Save | SaveLoadResult.Failure | SaveLoadResult.Busy,
            SaveLoadResult.Save | SaveLoadResult.Cancelled
        })
        {
            int forwarded = 0;
            Il2CppSystem.Action<SaveLoadResult> callback = new Il2CppSystem.Action<SaveLoadResult>((SaveLoadResult r) => { forwarded++; });
            bool runOriginal = true;
            MusketeerNativeSave.Musketeer_SaveAsyncObserver.Prefix(h.Global, ref callback, ref runOriginal);
            callback.Invoke(result);
            Check.Eq(forwarded, 1, "non-success forwarded once: " + result);
        }
        Check.False(File.Exists(h.PairPath), "rejected results never pair");

        // A throwing original callback is contained and logged, never propagated.
        Il2CppSystem.Action<SaveLoadResult> throwing = new Il2CppSystem.Action<SaveLoadResult>((SaveLoadResult r) => throw new InvalidOperationException("callback"));
        bool run = true;
        MusketeerNativeSave.Musketeer_SaveAsyncObserver.Prefix(h.Global, ref throwing, ref run);
        bool threw = false;
        try { throwing.Invoke(SaveLoadResult.Save | SaveLoadResult.Failure); } catch { threw = true; }
        Check.False(threw, "callback exception swallowed");
    }

    private static void LegacyUnknownPreserved()
    {
        using var h = new Harness();
        var legacy = new MusketeerArchive();
        string scope = Hex('5');
        string hash = Hex('6');
        string context = MusketeerArchive.ContextKey(h.File, 0, 0, 0);
        var reservations = new List<MusketeerCareer>();
        for (int i = 0; i < 17; i++) reservations.Add(Career(i % 2 == 0 ? MusketeerCareer.KindUnit : MusketeerCareer.KindGun, ""));
        Check.True(legacy.Record(scope, hash, reservations), "legacy reservations recorded");
        Check.True(legacy.EnsureContext(context, scope, true), "legacy context");
        File.WriteAllBytes(MusketeerPersistence.ArchivePath, legacy.Encode());

        var read = MusketeerNativeSave.ReadArchive();
        Check.Eq(read.Status, MusketeerArchiveStore.State.Valid, "legacy read");
        var records = read.Archive.Scopes[scope][0].Records;
        Check.Eq(records.Count, 17, "17 legacy records preserved");
        bool anyId = false;
        foreach (var record in records) if (record.NativeId.Length > 0) anyId = true;
        Check.False(anyId, "no native id fabricated for unknown-paid legacy");

        // Migration commit stages the same archive; nothing about the legacy history auto-unlocks.
        // The load path witnessed the context before CreateState, so the observation exists.
        string island = Island(0, "");
        h.Campaign(0);
        MusketeerNativeSave.ObserveContext(context, 0, 0, 0);
        string migratedHash = MusketeerArchive.IslandHash(island, scope);
        Check.True(read.Archive.Record(scope, migratedHash, records), "legacy archive records the new snapshot");
        Check.True(MusketeerNativeSave.Stage(context, scope, migratedHash, read, read.Archive), "migration stage");
        string staged = StagedDocument();
        Check.True(MusketeerNativeArchive.TryDecode(h.File, staged, out var stagedArchive, out _, out string decodeReason), "staged decode: " + decodeReason);
        Check.Eq(stagedArchive.Scopes[scope].Count, 2, "old snapshot and new baseline both retained");
        int nullIds = 0, totalRecords = 0;
        foreach (var snapshot in stagedArchive.Scopes[scope])
            foreach (var record in snapshot.Records)
            {
                totalRecords++;
                if (record.NativeId.Length == 0) nullIds++;
            }
        Check.Eq(totalRecords, 34, "both snapshots retain the 17 legacy reservations");
        Check.Eq(nullIds, totalRecords, "no native id invented anywhere after migration");
    }

    private static void DroppedGunPerIsland()
    {
        using var h = new Harness();
        string gunRow = Row("ToolBow--4711", ToolBowPrefab, "Dropped ToolBow");
        string archerRow = Row("Archer P7 [22]--1", ArcherPrefab);
        string islandHome = Island(0, gunRow);
        string islandFar = Island(4, archerRow);

        var gun = Career(MusketeerCareer.KindGun, "ToolBow--4711");
        var unit = Career(MusketeerCareer.KindUnit, "Archer P7 [22]--1");
        string home = MusketeerArchive.ContextKey(h.File, 0, 0, 0);
        string far = MusketeerArchive.ContextKey(h.File, 0, 0, 4);
        h.Campaign(0);
        MusketeerNativeSave.ObserveContext(home, 0, 0, 0);
        MusketeerNativeSave.ObserveContext(far, 0, 0, 4);
        string homeScope = MusketeerArchive.NewScope();
        string farScope = MusketeerArchive.NewScope();
        string homeHash = MusketeerArchive.IslandHash(islandHome, homeScope);
        string farHash = MusketeerArchive.IslandHash(islandFar, farScope);

        var archive = new MusketeerArchive();
        Check.True(archive.Record(homeScope, homeHash, new List<MusketeerCareer> { gun }), "home record");
        Check.True(archive.Record(farScope, farHash, new List<MusketeerCareer> { unit }), "far record");
        Check.True(archive.EnsureContext(home, homeScope, true), "home context");
        Check.True(archive.EnsureContext(far, farScope, true), "far context");
        Check.True(MusketeerNativeSave.Stage(home, homeScope, homeHash, MusketeerNativeSave.ReadArchive(), archive), "home stage");
        Check.True(MusketeerNativeSave.Stage(far, farScope, farHash, MusketeerNativeSave.ReadArchive(), archive), "far stage");

        byte[] valid = PayloadWithStaged(Campaign(islandHome + "," + islandFar));
        var verdict = Gate(h, valid, out _, out string reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Pass, "two islands with their own careers: " + reason);

        // The gun row moved to another island: the home proof can no longer find its witness.
        byte[] moved = PayloadWithStaged(Campaign(Island(0, "") + "," + Island(4, archerRow + "," + gunRow)));
        verdict = Gate(h, moved, out _, out reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Fail, "gun moved away from its island fails");
        Check.Eq(reason, MusketeerNativeSave.ReasonNoMatchingSnapshot, "moved gun reason");
    }

    // Review r1 repro 1: the save path only calls Commit -> Stage. A second island save (H1 -> H2)
    // must move the context's proof to H2, not leave it at H1.
    private static void ReviewConsecutiveSaveMovesProof()
    {
        using var h = new Harness();
        string islandA = Island(0, Row("Archer P7 [22]--1", ArcherPrefab));
        string islandB = Island(0, Row("Archer P7 [22]--2", ArcherPrefab));
        string context = MusketeerArchive.ContextKey(h.File, 0, 0, 0);
        h.Campaign(0);
        MusketeerNativeSave.ObserveContext(context, 0, 0, 0);
        string scope = MusketeerArchive.NewScope();
        string hashA = MusketeerArchive.IslandHash(islandA, scope);
        string hashB = MusketeerArchive.IslandHash(islandB, scope);

        var archive = new MusketeerArchive();
        Check.True(archive.Record(scope, hashA, new List<MusketeerCareer> { Career(MusketeerCareer.KindUnit, "Archer P7 [22]--1") }), "H1 record");
        Check.True(archive.EnsureContext(context, scope, true), "context");
        Check.True(MusketeerNativeSave.Stage(context, scope, hashA, MusketeerNativeSave.ReadArchive(), archive), "H1 stage");
        MusketeerNativeSave.SeedProof(context, scope, hashA);   // the load path seeds the H1 proof

        var loaded = MusketeerNativeSave.ReadArchive();
        Check.True(loaded.Status == MusketeerArchiveStore.State.Valid && loaded.Archive != null, "H1 reload");
        Check.True(loaded.Archive.Record(scope, hashB, new List<MusketeerCareer> { Career(MusketeerCareer.KindUnit, "Archer P7 [22]--2") }), "H2 record");
        Check.True(MusketeerNativeSave.Stage(context, scope, hashB, loaded, loaded.Archive), "H2 stage");

        var verdict = Gate(h, PayloadWithStaged(Campaign(islandB)), out _, out string reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Pass, "H2 must be the live proof: " + reason);
        verdict = Gate(h, PayloadWithStaged(Campaign(islandA)), out _, out reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Fail, "stale H1 island must no longer match");
        Check.Eq(reason, MusketeerNativeSave.ReasonNoMatchingSnapshot, "stale H1 reason");
    }

    // Review r1 repro 2: with no manual SeedProof the first Stage must still protect its rows.
    private static void ReviewFirstStageProtects()
    {
        using var h = new Harness();
        string islandA = Island(0, Row("Archer P7 [22]--1", ArcherPrefab));
        string context = MusketeerArchive.ContextKey(h.File, 0, 0, 0);
        h.Campaign(0);
        MusketeerNativeSave.ObserveContext(context, 0, 0, 0);
        string scope = MusketeerArchive.NewScope();
        string hashA = MusketeerArchive.IslandHash(islandA, scope);
        var archive = new MusketeerArchive();
        Check.True(archive.Record(scope, hashA, new List<MusketeerCareer> { Career(MusketeerCareer.KindUnit, "Archer P7 [22]--1") }), "record");
        Check.True(archive.EnsureContext(context, scope, true), "context");
        Check.True(MusketeerNativeSave.Stage(context, scope, hashA, MusketeerNativeSave.ReadArchive(), archive), "first stage");

        var verdict = Gate(h, PayloadWithStaged(Campaign(Island(0, Row("Archer P7 [22]--9", ArcherPrefab)))), out _, out string reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Fail, "first staged checkpoint must validate rows");
        Check.Eq(reason, MusketeerNativeSave.ReasonNoMatchingSnapshot, "mismatched rows reason");
    }

    // Review r1 repro 3: the wrapped callback must forward the original continuation exactly once.
    private static void ReviewDuplicateCallbackOnce()
    {
        using var h = new Harness();
        Seed(h, 0, 0, 0, Island(0, ""), new List<MusketeerCareer>());
        int forwarded = 0;
        Il2CppSystem.Action<SaveLoadResult> callback = new Il2CppSystem.Action<SaveLoadResult>((SaveLoadResult r) => { forwarded++; });
        bool run = true;
        MusketeerNativeSave.Musketeer_SaveAsyncObserver.Prefix(h.Global, ref callback, ref run);
        callback.Invoke(SaveLoadResult.Save | SaveLoadResult.Failure);
        callback.Invoke(SaveLoadResult.Save | SaveLoadResult.Failure);
        Check.Eq(forwarded, 1, "duplicate original continuation");
    }

    // Review r1 repro 4: a source provably deleted from a successful catalog read must have its
    // proof and capture fault revoked before the next legitimate save is judged.
    private static void ReviewDeletedSourceClearsFault()
    {
        using var h = new Harness();
        string island = Island(0, "");
        var seeded = Seed(h, 0, 0, 0, island, new List<MusketeerCareer>());
        byte[] payload = PayloadWithStaged(Campaign(island));
        MusketeerNativeSave.NoteCaptureFailure(seeded.Context);
        h.Global.campaigns.Clear();
        var verdict = Gate(h, payload, out _, out string reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Pass, "deleted faulty source must not lock the save: " + reason);
    }

    // Same-session pair recovery: the native key is gone, the disk file is byte-identical to the
    // accepted pair's native, so the archive is decoded from that actual file. A changed disk file
    // never recovers, and nothing is written back to the native save.
    private static void PairRecovery()
    {
        using var h = new Harness();
        string island = Island(0, "");
        var seeded = Seed(h, 0, 0, 0, island, new List<MusketeerCareer>());
        byte[] payload = PayloadWithStaged(Campaign(island));
        File.WriteAllBytes(h.DiskPath, payload);
        Check.True(AcceptedAsync(h, payload), "pair written");
        Check.True(File.Exists(h.PairPath), "pair exists");

        // Key loss: the staged record is gone (fresh module state) and the disk still matches the pair.
        MusketeerNativeSave.ResetForTests();
        h.Global.prefs.contents.Remove(MusketeerNativeArchive.Key);
        var read = MusketeerNativeSave.ReadArchive();
        Check.Eq(read.Status, MusketeerArchiveStore.State.Valid, "pair recovery binds");
        Check.Eq(read.Detail, "pair", "pair source classified");
        Check.True(read.Archive.Scopes.Count > 0, "recovered archive carries scopes");
        Check.True(h.Global.prefs.contents.Count == 0, "recovery never writes the native key");

        Check.True(read.Archive.Scopes.ContainsKey(seeded.Scope), "recovered scope present");

        // A disk file that no longer equals the pair's native never recovers.
        MusketeerNativeSave.ResetForTests();
        File.Delete(MusketeerPersistence.ArchivePath);           // no legacy fallback in this check
        File.WriteAllBytes(h.DiskPath, Gzip(Global(Campaign(island), "")));
        read = MusketeerNativeSave.ReadArchive();
        Check.Eq(read.Status, MusketeerArchiveStore.State.Missing, "mismatched disk does not recover");
        Check.True(read.Detail != "pair", "no pair classification on a mismatch");

        // The recovered identity can be committed again: the unchanged accepted disk file is the
        // CAS base, and the next Stage republishes the native key in memory only.
        MusketeerNativeSave.ResetForTests();
        File.WriteAllBytes(h.DiskPath, payload);
        var recovered = MusketeerNativeSave.ReadArchive();
        Check.Eq(recovered.Detail, "pair", "pair still the source");
        h.Campaign(0);
        MusketeerNativeSave.ObserveContext(seeded.Context, 0, 0, 0);
        Check.True(MusketeerNativeSave.Stage(seeded.Context, seeded.Scope, seeded.Hash, recovered, recovered.Archive), "pair recovery re-stages");
        Check.True(h.Global.prefs.contents.ContainsKey(MusketeerNativeArchive.Key), "native key republished in memory");
    }

    // A replaced loaded Global must not be gated by the previous owner's staged key: the new
    // session starts clean instead of inheriting the old responsibility.
    private static void StaleOwnerBypass()
    {
        using var h = new Harness();
        Seed(h, 0, 0, 0, Island(0, ""), new List<MusketeerCareer>());

        var replacement = new GlobalSaveData { prefs = new PrefsSaveData() };
        GlobalSaveData.loaded = replacement;
        byte[] payload = Gzip(Global(Campaign(Island(0, "")), ""));
        var verdict = Gate(h, payload, out var call, out string reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Pass, "replaced Global bypasses the stale owner: " + reason);
        Check.True(call == null, "no lease taken for the replaced Global");
        GlobalSaveData.loaded = h.Global;
    }

    // Frozen accept: island save H2 (Stage B) after the global writer attempt A but before A's
    // callback. A's own disk file must still produce A's pair from A's frozen snapshot.
    private static void FrozenAcceptAfterLaterStage()
    {
        using var h = new Harness();
        string islandA = Island(0, Row("Archer P7 [22]--1", ArcherPrefab));
        string islandB = Island(0, Row("Archer P7 [22]--2", ArcherPrefab));
        var recordsA = new List<MusketeerCareer> { Career(MusketeerCareer.KindUnit, "Archer P7 [22]--1") };
        string context = MusketeerArchive.ContextKey(h.File, 0, 0, 0);
        h.Campaign(0);
        MusketeerNativeSave.ObserveContext(context, 0, 0, 0);
        string scopeA = MusketeerArchive.NewScope();
        string hashA = MusketeerArchive.IslandHash(islandA, scopeA);
        var archiveA = new MusketeerArchive();
        Check.True(archiveA.Record(scopeA, hashA, recordsA), "A record");
        Check.True(archiveA.EnsureContext(context, scopeA, true), "A context");
        Check.True(MusketeerNativeSave.Stage(context, scopeA, hashA, MusketeerNativeSave.ReadArchive(), archiveA), "A stage");
        byte[] payloadA = PayloadWithStaged(Campaign(islandA));

        // Real flow: SaveAsync prefix freezes the observation, then the writer attempt starts.
        int forwarded = 0;
        SaveLoadResult forwardedResult = SaveLoadResult.None;
        Il2CppSystem.Action<SaveLoadResult> callback = new Il2CppSystem.Action<SaveLoadResult>((SaveLoadResult r) => { forwarded++; forwardedResult = r; });
        bool runOriginal = true;
        MusketeerNativeSave.Musketeer_SaveAsyncObserver.Prefix(h.Global, ref callback, ref runOriginal);
        var gateA = GateAsync(h, payloadA);
        Check.True(gateA.Run, "A accepted");

        // A later island save stages H2 in a new epoch scope while A is still in flight.
        string scopeB = MusketeerArchive.NewScope();
        string hashB = MusketeerArchive.IslandHash(islandB, scopeB);
        var archiveB = new MusketeerArchive();
        Check.True(archiveB.Record(scopeB, hashB, new List<MusketeerCareer> { Career(MusketeerCareer.KindUnit, "Archer P7 [22]--2") }), "B record");
        Check.True(archiveB.EnsureContext(context, scopeB, true), "B context");
        Check.True(MusketeerNativeSave.Stage(context, scopeB, hashB, MusketeerNativeSave.ReadArchive(), archiveB), "B stage");

        // A's accepted write is what is on disk; A's callback must pair it from A's frozen rights.
        File.WriteAllBytes(h.DiskPath, payloadA);
        callback.Invoke((SaveLoadResult)MusketeerNativeSave.NoChangeCode);   // the prefix replaced callback with the wrapper
        Check.Eq(forwarded, 1, "A continuation forwarded once");
        Check.Eq((int)forwardedResult, MusketeerNativeSave.NoChangeCode, "A result forwarded unchanged");
        Check.True(File.Exists(h.PairPath), "A pair produced from A's frozen snapshot");
        Check.True(File.Exists(h.PairPath), "A pair file exists");
        Check.True(MusketeerPairedBackup.TryRecover(h.PairPath, h.File, payloadA, out _, out var recovered, out string reason) && recovered != null,
            "A pair matches A's disk payload: " + reason);
        Check.True(recovered != null && recovered.Scopes.ContainsKey(scopeA), "A pair carries A's scope");
    }

    // When the native rejection endpoint cannot be produced, the module must not pretend a normal
    // Failure result exists: no new staging is accepted and unowned payloads pass through.
    private static void RejectEndpointUnavailable()
    {
        using var h = new Harness();
        Il2CppSystem.Threading.Tasks.Task.ForceFailure = true;
        try
        {
            string context = MusketeerArchive.ContextKey(h.File, 0, 0, 0);
            h.Campaign(0);
            MusketeerNativeSave.ObserveContext(context, 0, 0, 0);   // binds the owner and prewarms the endpoint
            Check.True(MusketeerNativeSave.RejectEndpointUnavailable, "endpoint reported unavailable");
            string scope = MusketeerArchive.NewScope();
            string hash = MusketeerArchive.IslandHash(Island(0, ""), scope);
            var archive = new MusketeerArchive();
            Check.True(archive.Record(scope, hash, new List<MusketeerCareer>()), "record");
            Check.True(archive.EnsureContext(context, scope, true), "context");
            Check.False(MusketeerNativeSave.Stage(context, scope, hash, MusketeerNativeSave.ReadArchive(), archive),
                "no new staging while the rejection endpoint is unavailable");
            var verdict = Gate(h, Gzip(Global(Campaign(Island(0, "")), "")), out _, out string reason);
            Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Pass, "no obligations: pass-through: " + reason);
            var observation = MusketeerNativeSave.BeginSaveObservation(h.Global);
            Check.True(observation == null || !observation.Rights.Protected, "no rights frozen for the pass-through");
        }
        finally { Il2CppSystem.Threading.Tasks.Task.ForceFailure = false; }
    }

    // C1: at capacity an unfinished observation is never evicted (it roots a callback that must
    // still be forwarded once); the new callback stays unwrapped and the refusal is observable.
    private static void ResidualCapacityKeepsRoots()
    {
        using var h = new Harness();
        Seed(h, 0, 0, 0, Island(0, ""), new List<MusketeerCareer>());
        var wrapped = new List<Il2CppSystem.Action<SaveLoadResult>>();
        int forwarded = 0;
        for (int i = 0; i < 8; i++)
        {
            Il2CppSystem.Action<SaveLoadResult> callback = new Il2CppSystem.Action<SaveLoadResult>((SaveLoadResult r) => { forwarded++; });
            var original = callback;
            bool run = true;
            MusketeerNativeSave.Musketeer_SaveAsyncObserver.Prefix(h.Global, ref callback, ref run);
            Check.True(!ReferenceEquals(callback, original), "observation " + i + " wrapped");
            wrapped.Add(callback);
        }
        Il2CppSystem.Action<SaveLoadResult> ninth = new Il2CppSystem.Action<SaveLoadResult>((SaveLoadResult r) => { forwarded += 100; });
        var ninthOriginal = ninth;
        bool ninthRun = true;
        MusketeerNativeSave.Musketeer_SaveAsyncObserver.Prefix(h.Global, ref ninth, ref ninthRun);
        Check.True(ReferenceEquals(ninth, ninthOriginal), "ninth callback left unwrapped (pass-through)");
        Check.True(ninthRun, "observer still allows the original to run");

        var field = typeof(MusketeerNativeSave).GetField("PendingObservations",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var roots = (System.Collections.ICollection)field.GetValue(null);
        Check.Eq(roots.Count, 8, "capacity keeps every unfinished root");

        // The oldest unfinished root still forwards and still pairs from the actual disk file.
        byte[] payload = PayloadWithStaged(Campaign(Island(0, "")));
        File.WriteAllBytes(h.DiskPath, payload);
        wrapped[0].Invoke((SaveLoadResult)MusketeerNativeSave.NoChangeCode);
        Check.Eq(forwarded, 1, "oldest unfinished root forwards once");
        Check.True(File.Exists(h.PairPath), "oldest unfinished root still pairs");
        bool logged = false;
        foreach (string warning in KingdomEnhancedPlugin.Instance.LogSource.Warnings)
            if (warning.IndexOf("observe-cap:", StringComparison.Ordinal) >= 0) logged = true;
        Check.True(logged, "capacity refusal logged per file");
    }

    // C2/C4: the callback is associated with its own frozen observation by content only. The
    // synthetic interleave (two observers before either writer, a Stage in between) must never
    // pair A's bytes under B's callback or vice versa - it degrades visibly instead.
    private static void ResidualReorderedCallbacks()
    {
        using var h = new Harness();
        string islandA = Island(0, Row("A", ArcherPrefab));
        Seed(h, 0, 0, 0, islandA, new List<MusketeerCareer> { Career(MusketeerCareer.KindUnit, "A") });
        byte[] payloadA = PayloadWithStaged(Campaign(islandA));

        Il2CppSystem.Action<SaveLoadResult> callbackA = new Il2CppSystem.Action<SaveLoadResult>((SaveLoadResult r) => { });
        bool runA = true;
        MusketeerNativeSave.Musketeer_SaveAsyncObserver.Prefix(h.Global, ref callbackA, ref runA);
        Il2CppSystem.Action<SaveLoadResult> callbackB = new Il2CppSystem.Action<SaveLoadResult>((SaveLoadResult r) => { });
        bool runB = true;
        MusketeerNativeSave.Musketeer_SaveAsyncObserver.Prefix(h.Global, ref callbackB, ref runB);
        var gateA = GateAsync(h, payloadA);
        Check.True(gateA.Run, "writer A accepted");
        MusketeerNativeSave.Musketeer_AsyncWriterGate.Postfix(gateA.Call,
            new Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> { IsCompleted = true });

        string islandB = Island(0, Row("B", ArcherPrefab));
        Seed(h, 0, 0, 0, islandB, new List<MusketeerCareer> { Career(MusketeerCareer.KindUnit, "B") });
        byte[] payloadB = PayloadWithStaged(Campaign(islandB));
        var gateB = GateAsync(h, payloadB);
        Check.True(gateB.Run, "writer B accepted");
        MusketeerNativeSave.Musketeer_AsyncWriterGate.Postfix(gateB.Call,
            new Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> { IsCompleted = true });

        File.WriteAllBytes(h.DiskPath, payloadB);
        callbackB.Invoke((SaveLoadResult)MusketeerNativeSave.NoChangeCode);
        Check.False(File.Exists(h.PairPath), "B never pairs a document it did not freeze (no guessed attribution)");
        callbackA.Invoke((SaveLoadResult)MusketeerNativeSave.NoChangeCode);
        Check.False(File.Exists(h.PairPath), "late A never pairs the newer bytes");

        // The realistic order (observation frozen after its own stage) pairs the current bytes.
        Check.True(AcceptedAsync(h, payloadB), "realistic accepted order pairs the current bytes");
        Check.True(MusketeerPairedBackup.TryRecover(h.PairPath, h.File, payloadB, out _, out _, out string reason),
            "pair recovers the actual disk bytes: " + reason);
    }

    // C3/C5: the native factory window (no Task returned yet) and a sync body in flight are Busy
    // with the lease kept; only a witnessed terminal Task releases the path.
    private static void ResidualEnteringWindowBusy()
    {
        using var h = new Harness();
        Seed(h, 0, 0, 0, Island(0, ""), new List<MusketeerCareer>());
        byte[] payload = PayloadWithStaged(Campaign(Island(0, "")));
        var first = GateAsync(h, payload);
        Check.True(first.Run, "first native factory allowed");
        var second = GateAsync(h, payload);
        Check.False(second.Run, "second factory rejected while the first is entering");
        Check.Eq((int)second.Task.Result, MusketeerNativeSave.ResultBusyCode, "entering factory is Busy, not sticky UnknownWriter");
        MusketeerNativeSave.Musketeer_AsyncWriterGate.Postfix(first.Call,
            new Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> { IsCompleted = true });
        var third = GateAsync(h, payload);
        Check.True(third.Run, "first real terminal Task witness allows the next save");
    }

    // C4/C6: a challenge source is revoked by the witnessed pointer's absence from the captured
    // campaigns+challenges union (a readable catalog only); while it still exists the fault blocks.
    private static void ResidualDeletedChallengeSource()
    {
        using var h = new Harness();
        string context = MusketeerArchive.ContextKey(h.File, 0, 7, 0);
        string scope = MusketeerArchive.NewScope();
        string island = Island(0, "");
        h.Global.challenges.Add(new CampaignSaveData { challengeId = 7 });
        MusketeerNativeSave.ObserveContext(context, 0, 7, 0);
        string hash = MusketeerArchive.IslandHash(island, scope);
        var archive = new MusketeerArchive();
        Check.True(archive.Record(scope, hash, new List<MusketeerCareer>()), "challenge record");
        Check.True(archive.EnsureContext(context, scope, true), "challenge context");
        Check.True(MusketeerNativeSave.Stage(context, scope, hash, MusketeerNativeSave.ReadArchive(), archive), "challenge stage");
        MusketeerNativeSave.NoteCaptureFailure(context);

        byte[] payload = PayloadWithStaged(Campaign(island));
        var verdict = Gate(h, payload, out _, out string reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Fail, "challenge fault blocks while the source exists");
        Check.Eq(reason, MusketeerNativeSave.ReasonCaptureFault, "existing challenge source is CaptureFault");

        h.Global.challenges.Clear();
        verdict = Gate(h, PayloadWithStaged(Campaign(island)), out _, out reason);
        Check.Eq(verdict, MusketeerNativeSave.WriterVerdict.Pass, "provably deleted challenge revokes responsibility: " + reason);
    }

    // C3: another patch's finalizer exception after this Task was rooted must not drop the root or
    // poison the path: the lease keeps the real Task and stays Busy until its terminal state.
    private static void ResidualKnownTaskException()
    {
        using var h = new Harness();
        Seed(h, 0, 0, 0, Island(0, ""), new List<MusketeerCareer>());
        byte[] payload = PayloadWithStaged(Campaign(Island(0, "")));
        var first = GateAsync(h, payload);
        Check.True(first.Run, "gate pass");
        var running = new Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> { IsCompleted = false };
        MusketeerNativeSave.Musketeer_AsyncWriterGate.Postfix(first.Call, running);
        MusketeerNativeSave.Musketeer_AsyncWriterGate.Finally(new InvalidOperationException("other-postfix"), first.Call);

        var second = GateAsync(h, payload);
        Check.False(second.Run, "rooted Task keeps the path closed");
        Check.Eq((int)second.Task.Result, MusketeerNativeSave.ResultBusyCode, "rooted Task stays Busy, never sticky Unknown");
        running.IsCompleted = true;
        var third = GateAsync(h, payload);
        Check.True(third.Run, "real terminal witness releases the path");
    }

    private static void MirrorDegraded()
    {
        using var h = new Harness();
        Seed(h, 0, 0, 0, Island(0, ""), new List<MusketeerCareer>());
        byte[] payload = PayloadWithStaged(Campaign(Island(0, "")));
        File.WriteAllBytes(h.DiskPath, payload);

        byte[] foreign = Encoding.UTF8.GetBytes("{\"foreign\":\"future\"}");
        File.WriteAllBytes(MusketeerPersistence.ArchivePath, foreign);

        Check.True(AcceptedAsync(h, payload), "pair still saved from the real disk payload");
        Check.True(File.Exists(h.PairPath), "pair exists");
        Check.True(File.ReadAllBytes(MusketeerPersistence.ArchivePath).AsSpan().SequenceEqual(foreign), "corrupt sidecar preserved, not overwritten");
    }
}
