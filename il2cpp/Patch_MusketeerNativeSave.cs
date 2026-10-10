using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using Coatsink.Common;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace KingdomEnhancedMod;

// Native-Prefs authority for paid musketeer careers (Issue198 runtime slice).
//
// The career archive travels inside the game's own compressed global payload under the single
// native Prefs key (MusketeerNativeArchive.Key) instead of a sidecar committed before the native
// write. This class owns three responsibilities:
//
//  1. The read bridge (ReadArchive): the loaded native key is the authority. Only a missing key
//     opens the same-session paired backup (strict SHA + byte equality against the current disk
//     file) and then the legacy sidecar migration source; a RecoveredBackup sidecar stays
//     read-only. A present corrupt/future key never falls back and is never rewritten.
//  2. The staged checkpoint (Stage/SeedProof/ObserveContext/NoteCaptureFailure): the main thread
//     freezes the exact archive bytes (immutable raw) into the loaded prefs and atomically
//     publishes the context's RequiredProof - the explicit (context, epoch, hash) plus the
//     actually witnessed campaign/challenge/land and live catalog owner. A capture fault blocks
//     the next physical write until the source is provably gone or a successful Stage clears it.
//  3. The physical writer gate (Harmony prefixes on Filer.DotNetAgent.SaveFileToDiskAsync and
//     Filer.DotNetAgent._SaveFileToDisk_d__10.MoveNext): before any Directory/FileStream work the
//     actual outgoing payload is decoded and validated against a frozen rights snapshot. Rejection
//     completes that writer's native Failure result; the gate never rewrites prefs and never
//     touches the outgoing bytes. Accepted writes produce the single paired backup container (and
//     the legacy sidecar mirror) only after reading the real file back.
//
// Producer/observer and writer-entry capture produce managed immutable snapshots. The actual
// factory invocation thread remains a native acceptance check; the callback never reads GlobalSaveData, the live
// catalog or any Unity getter, and it is associated with its own frozen observation by content -
// never by guessing which writer attempt belongs to which callback. The writer lease is a
// per-canonical-path mutual exclusion for the physical copy only. A native factory that has not
// returned its Task yet, a sync coroutine body in flight and a pending async Task are all Busy
// with the lease kept; only a terminal Task witness releases an async lease and only a confirmed
// factory exception without a Task leaves the sticky UnknownWriter fault. The sync coroutine has
// no yield, so its lease is released by that same MoveNext call's finalizer.
internal static class MusketeerNativeSave
{
    // Native SaveLoadResult bits (current 2.4 ARM64): Save 8, Success 64, Failure 128, Busy 256,
    // Cancelled 8192, and the serializer's no-change result is exactly Save|Success = 72.
    internal const int ResultSave = 8;
    internal const int ResultSuccess = 64;
    internal const int ResultFailure = 128;
    internal const int ResultBusy = 256;
    internal const int ResultCancelled = 8192;
    internal const int ResultFailureCode = ResultSave | ResultFailure;                 // 136
    internal const int ResultBusyCode = ResultSave | ResultFailure | ResultBusy;       // 392
    internal const int NoChangeCode = ResultSave | ResultSuccess;                      // 72

    internal const string ReasonNoMatchingSnapshot = "NoMatchingSnapshot";
    internal const string ReasonScopeAmbiguous = "ScopeAmbiguous";
    internal const string ReasonPrefsKeyLost = "PrefsKeyLost";
    internal const string ReasonUnknownWriter = "UnknownWriter";
    internal const string ReasonCatalogInconsistent = "CatalogInconsistent";
    internal const string ReasonCaptureFault = "CaptureFault";

    // Observed 2.4 object rows: a native Archer row and a dropped ToolBow row are identified by
    // their prefabPath; the row's uniqueID is the same value the archive stores as NativeId.
    private const string ArcherPrefabPath = "Prefabs/Characters/Archer";
    private const string ToolBowPrefabPath = "Prefabs/Objects/ToolBow";

    private const int MaxPendingObservations = 8;
    private const int MaxObserved = 256;
    private const int MaxProofs = 256;
    private const int MaxLoggedKeys = 64;

    private static readonly object Sync = new();
    private static readonly Dictionary<string, Lease> Leases = new(StringComparer.Ordinal);
    private static readonly HashSet<string> UnknownWriters = new(StringComparer.Ordinal);
    private static readonly List<SaveObservation> PendingObservations = new();
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);
    private static long _nextSequence;
    private static long _nextOwnerId;
    private static OwnerState _owner;
    private static Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> _failureTask;
    private static Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> _busyTask;
    private static bool _rejectTasksUnavailable;

    // One loaded GlobalSaveData instance (exact pointer + prefs pointer + filename) owns one
    // staged state. A different instance starts clean: responsibilities never carry over.
    internal sealed class OwnerState
    {
        internal long Id;
        internal IntPtr Global;
        internal IntPtr Prefs;
        internal string File;
        internal string DiskPath;                        // canonical Filer.folder + File, captured on the main thread
        internal string StagedRaw;                       // frozen immutable document raw
        internal readonly Dictionary<string, Observation> Observed = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, Proof> Proofs = new(StringComparer.Ordinal);
        internal readonly HashSet<string> Protected = new(StringComparer.Ordinal);
        internal readonly HashSet<string> Faults = new(StringComparer.Ordinal);
        internal long LastConsumedSequence;
    }

    internal sealed class Observation
    {
        internal int Campaign;
        internal int Challenge;
        internal int Land;
        internal IntPtr Owner;
    }

    internal sealed class Proof
    {
        internal string Context;
        internal string Epoch;
        internal string Hash;
        internal int Campaign;
        internal int Challenge;
        internal int Land;
        internal IntPtr Owner;
    }

    // Immutable managed copy of one RequiredProof; the frozen snapshot consumed by the callback
    // path never aliases live state.
    internal sealed class FrozenProof
    {
        internal string Context;
        internal string Epoch;
        internal string Hash;
        internal int Campaign;
        internal int Challenge;
        internal int Land;
        internal IntPtr Owner;
    }

    // Everything the validator may read: the frozen document raw (null when no checkpoint is
    // staged yet), the proofs and the catalog pointers captured before callback execution. The
    // background callback consumes only these managed arrays.
    internal sealed class RightsSnapshot
    {
        internal bool Protected;
        internal string DocumentRaw;
        internal FrozenProof[] Proofs = Array.Empty<FrozenProof>();
        internal bool CatalogReadable;
        internal IntPtr[] Campaigns = Array.Empty<IntPtr>();
        internal IntPtr[] Challenges = Array.Empty<IntPtr>();

        internal int SlotOf(IntPtr owner)
        {
            if (owner == IntPtr.Zero) return -1;
            for (int i = 0; i < Campaigns.Length; i++)
                if (Campaigns[i] == owner) return i;
            return -1;
        }

        internal bool ContainsOwner(IntPtr owner)
        {
            if (owner == IntPtr.Zero) return false;
            for (int i = 0; i < Campaigns.Length; i++)
                if (Campaigns[i] == owner) return true;
            for (int i = 0; i < Challenges.Length; i++)
                if (Challenges[i] == owner) return true;
            return false;
        }
    }

    private sealed class Lease
    {
        internal bool Sync;
        internal Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> Task;   // strong root until terminal
        internal bool TaskKnown;
    }

    internal sealed class Attempt
    {
        internal OwnerState Owner;
        internal long Sequence;
        internal string Path;
        internal string File;
        internal RightsSnapshot Rights;
        internal bool Consumed;
    }

    internal enum WriterVerdict { Pass, Fail, Busy, UnknownWriter }

    internal sealed class WriterCall
    {
        internal string Path;
        internal string File;
        internal Attempt Attempt;
    }

    internal sealed class SaveObservation
    {
        internal OwnerState Owner;
        internal long Sequence;
        internal string File;
        internal string Path;
        internal RightsSnapshot Rights;
        internal System.Action<SaveLoadResult> Managed;   // managed root until the callback completes
        internal int Consumption;                        // exactly-once guard across the whole wrapper
    }

    // ------------------------------------------------------------------ API

    // Bridge read for MusketeerPersistence (CreateState/Commit). The loaded native key is the
    // authority. While a checkpoint is staged on this exact owner, a key that was changed or lost
    // externally is reported corrupt (fail visible, staged record preserved) instead of being
    // adopted or rewritten. Only a missing key opens the same-session paired backup and then the
    // legacy sidecar.
    internal static MusketeerArchiveStore.ReadResult ReadArchive()
    {
        try
        {
            OwnerState owner = OwnerForBinding();
            if (owner != null)
            {
                if (!TryReadPrefsKey(owner, out string raw))
                    return new MusketeerArchiveStore.ReadResult { Status = MusketeerArchiveStore.State.Corrupt, Detail = "prefs-unreadable" };
                if (raw != null)
                {
                    if (owner.StagedRaw != null && !string.Equals(raw, owner.StagedRaw, StringComparison.Ordinal))
                        return new MusketeerArchiveStore.ReadResult
                        {
                            Status = MusketeerArchiveStore.State.Corrupt,
                            Detail = "native-key-changed",
                            Original = Encoding.UTF8.GetBytes(raw)
                        };
                    if (MusketeerNativeArchive.TryDecode(owner.File, raw, out var archive, out bool unsupported, out string decodeReason))
                    {
                        owner.StagedRaw ??= raw;
                        return new MusketeerArchiveStore.ReadResult
                        {
                            Status = MusketeerArchiveStore.State.Valid,
                            Archive = archive,
                            Original = Encoding.UTF8.GetBytes(raw)
                        };
                    }
                    return new MusketeerArchiveStore.ReadResult
                    {
                        Status = unsupported ? MusketeerArchiveStore.State.Unsupported : MusketeerArchiveStore.State.Corrupt,
                        Detail = decodeReason,
                        Original = Encoding.UTF8.GetBytes(raw)
                    };
                }
                if (owner.StagedRaw != null)
                    return new MusketeerArchiveStore.ReadResult { Status = MusketeerArchiveStore.State.Corrupt, Detail = "native-key-lost" };
                var pair = TryPairRecovery(owner);
                if (pair != null) return pair;
            }
            // No native key for this owner: the legacy sidecar is the migration source and keeps
            // its existing status mapping (a valid .bak stays RecoveredBackup/read-only).
            return MusketeerArchiveStore.Load(MusketeerPersistence.ArchivePath);
        }
        catch (Exception e)
        {
            Log("read-exception", e);
            return new MusketeerArchiveStore.ReadResult { Status = MusketeerArchiveStore.State.Corrupt, Detail = "read-exception" };
        }
    }

    // Same-session paired recovery: only when the native key is missing and this owner has nothing
    // staged to lose. The current disk file must be byte-identical (SHA-256 + bytes) to the pair's
    // stored native; the archive is then decoded from that actual file. Nothing is written back to
    // the native file, nothing is fabricated and a corrupt/future native key never reaches here.
    private static MusketeerArchiveStore.ReadResult TryPairRecovery(OwnerState owner)
    {
        string path = EnsureOwnerPath(owner);
        if (path == null) return null;
        byte[] bytes = ReadAllBytesBounded(path);
        if (bytes == null) return null;
        if (!MusketeerPairedBackup.TryRecover(PairPath(owner.File), owner.File, bytes, out _, out var archive, out string reason))
        {
            if (!string.Equals(reason, "pair-missing", StringComparison.Ordinal))
                LogOnce("pair-recovery:" + reason, Short(owner.File));
            return null;
        }
        LogEvent("pair-recovered:" + Short(owner.File));
        return new MusketeerArchiveStore.ReadResult
        {
            Status = MusketeerArchiveStore.State.Valid,
            Archive = archive,
            Original = bytes,
            Detail = "pair"
        };
    }

    // Called from MusketeerIdentity.TryContextKey on every successful context resolution: records
    // the actual source metadata (campaign/challenge/land and the live catalog owner) the context
    // was witnessed under. No hash is ever reverse-engineered from these numbers.
    internal static void ObserveContext(string key, int campaign, int challenge, int land)
    {
        try
        {
            if (!MusketeerArchive.HashValid(key) || land < 0) return;
            OwnerState owner = OwnerForBinding();
            if (owner == null) return;
            lock (Sync)
            {
                if (owner.Observed.Count >= MaxObserved && !owner.Observed.ContainsKey(key))
                {
                    LogOnce("observe-overflow", key);
                    return;
                }
                owner.Observed[key] = new Observation
                {
                    Campaign = campaign,
                    Challenge = challenge,
                    Land = land,
                    Owner = LiveCatalogOwner(campaign, challenge)
                };
            }
        }
        catch (Exception e) { Log("observe-exception", e); }
    }

    // Freezes the exact captured checkpoint into the loaded prefs (in memory only) and, in the same
    // success, publishes/updates this context's RequiredProof to the explicit hash with the actually
    // witnessed locator. The explicit hash selects exactly one snapshot inside the context's own
    // epoch: a missing exact snapshot is NoMatchingSnapshot and a non-unique scope/snapshot is
    // ScopeAmbiguous - never a positional pick, and never deferred to the caller.
    internal static bool Stage(string contextKey, string epoch, string hash, MusketeerArchiveStore.ReadResult expected, MusketeerArchive updated)
    {
        try
        {
            if (!MusketeerArchive.HashValid(contextKey) || !MusketeerArchive.HashValid(epoch) || !MusketeerArchive.HashValid(hash))
                return StageFail("args");
            if (expected == null || updated == null) return StageFail("null");
            // The caller's read must be writable (missing or valid, never a recovered backup):
            // staging from a corrupt/future/backup read would contradict the read-only contract.
            if (!expected.Writable) return StageFail("expected:" + expected.Status);
            if (_rejectTasksUnavailable) return StageFail("reject-endpoint-unavailable");
            OwnerState owner = OwnerForBinding();
            if (owner == null) return StageFail("owner");
            Observation observation;
            lock (Sync)
            {
                if (!owner.Observed.TryGetValue(contextKey, out observation) || observation == null)
                    return StageFail("no-source");
            }
            if (!updated.TryGetContext(contextKey, out var context)) return StageFail("context");
            int epochCount = 0;
            foreach (string candidate in context.Epochs)
                if (string.Equals(candidate, epoch, StringComparison.Ordinal)) epochCount++;
            if (epochCount != 1 || updated.ScopeClaimedBy(epoch, contextKey)) return StageFail(ReasonScopeAmbiguous);
            if (!updated.Scopes.TryGetValue(epoch, out var snapshots)) return StageFail(ReasonNoMatchingSnapshot);
            int matches = 0;
            foreach (var snapshot in snapshots)
                if (string.Equals(snapshot.Hash, hash, StringComparison.Ordinal)) matches++;
            if (matches == 0) return StageFail(ReasonNoMatchingSnapshot);
            if (matches != 1) return StageFail(ReasonScopeAmbiguous);
            if (!CasSource(owner, expected, out string casReason)) return StageFail(casReason);
            if (!MusketeerNativeArchive.TryEncode(owner.File, updated, out string raw, out string encodeReason))
                return StageFail("encode:" + encodeReason);
            if (!TryWritePrefsKey(owner, raw))
                return StageFail("readback");
            lock (Sync)
            {
                owner.StagedRaw = raw;
                owner.Proofs[contextKey] = new Proof
                {
                    Context = contextKey,
                    Epoch = epoch,
                    Hash = hash,
                    Campaign = observation.Campaign,
                    Challenge = observation.Challenge,
                    Land = observation.Land,
                    Owner = observation.Owner
                };
                owner.Protected.Add(contextKey);
                owner.Faults.Remove(contextKey);
            }
            LogEvent("staged:" + Short(contextKey) + ":" + Short(epoch) + ":" + Short(hash));
            return true;
        }
        catch (Exception e)
        {
            Log("stage-exception", e);
            return false;
        }
    }

    private static bool StageFail(string reason)
    {
        LogOnce("stage-fail:" + reason, reason);
        return false;
    }

    // Load-path seeding (MusketeerPersistence.CreateState, exact and not unresolved). The save
    // path no longer depends on this: Stage publishes its own proof in the same success.
    internal static void SeedProof(string context, string epoch, string hash)
    {
        try
        {
            if (!MusketeerArchive.HashValid(context) || !MusketeerArchive.HashValid(epoch) || !MusketeerArchive.HashValid(hash)) return;
            if (_rejectTasksUnavailable) return;
            OwnerState owner = OwnerForBinding();
            if (owner == null) return;
            lock (Sync)
            {
                if (!owner.Observed.TryGetValue(context, out var observation))
                {
                    LogOnce("seed-no-source", context);
                    return;
                }
                if (owner.Proofs.Count >= MaxProofs && !owner.Proofs.ContainsKey(context))
                {
                    LogOnce("seed-overflow", context);
                    return;
                }
                owner.Proofs[context] = new Proof
                {
                    Context = context,
                    Epoch = epoch,
                    Hash = hash,
                    Campaign = observation.Campaign,
                    Challenge = observation.Challenge,
                    Land = observation.Land,
                    Owner = observation.Owner
                };
                owner.Protected.Add(context);
            }
        }
        catch (Exception e) { Log("seed-exception", e); }
    }

    // Called from the island-save finalizer when a protected capture did not apply. The next
    // physical writer fails closed for that context until the source is provably gone or a
    // successful Stage clears it.
    internal static void NoteCaptureFailure(string context)
    {
        try
        {
            if (!MusketeerArchive.HashValid(context)) return;
            OwnerState owner = OwnerForBinding();
            if (owner == null) return;
            lock (Sync)
            {
                owner.Faults.Add(context);
                LogOnce("capture-fault", Short(context));
            }
        }
        catch (Exception e) { Log("fault-exception", e); }
    }

    // ------------------------------------------------------------------ owner binding

    private static OwnerState OwnerForBinding()
    {
        GlobalSaveData loaded = GlobalSaveData.loaded;
        if (loaded == null) return null;
        string file = GlobalSaveData.filename;
        if (!MusketeerNativeArchive.ValidFile(file)) return null;
        IntPtr prefs = IntPtr.Zero;
        try { if (loaded.prefs != null) prefs = loaded.prefs.Pointer; } catch { return null; }
        if (prefs == IntPtr.Zero) return null;
        OwnerState current = _owner;
        if (current != null && current.Global == loaded.Pointer && current.Prefs == prefs
            && string.Equals(current.File, file, StringComparison.Ordinal)) return current;
        var bound = new OwnerState { Id = ++_nextOwnerId, Global = loaded.Pointer, Prefs = prefs, File = file };
        _owner = bound;
        PrewarmRejectTasks();
        LogEvent("owner:" + Short(file));
        return bound;
    }

    // The loaded pointer/prefs pointer/filename still describe this owner. A replaced Global must
    // never let an old owner's Native key carry save responsibility into a new session.
    private static bool CurrentOwnerIs(OwnerState owner)
    {
        try
        {
            GlobalSaveData loaded = GlobalSaveData.loaded;
            if (loaded == null || loaded.Pointer != owner.Global) return false;
            var prefs = loaded.prefs;
            return prefs != null && prefs.Pointer == owner.Prefs
                && string.Equals(GlobalSaveData.filename, owner.File, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    // Capture the owner's concrete file path from Filer.folder before callback execution.
    // The callback uses the captured path and never comes here.
    private static string EnsureOwnerPath(OwnerState owner)
    {
        if (owner.DiskPath != null) return owner.DiskPath;
        try { owner.DiskPath = CanonicalPath(Filer.folder, owner.File); }
        catch { return null; }
        return owner.DiskPath;
    }

    private static IntPtr LiveCatalogOwner(int campaign, int challenge)
    {
        try
        {
            GlobalSaveData loaded = GlobalSaveData.loaded;
            if (loaded == null || campaign < 0 || challenge < 0) return IntPtr.Zero;
            if (challenge == 0)
            {
                var campaigns = loaded.campaigns;
                if (campaigns == null || campaign >= campaigns.Count) return IntPtr.Zero;
                var entry = campaigns[campaign];
                return entry != null ? entry.Pointer : IntPtr.Zero;
            }
            // A challenge context is witnessed through campaigns/challenges entries carrying that
            // challengeId. Ambiguity is recorded as "no owner" and fails closed at the writer.
            IntPtr found = IntPtr.Zero;
            int count = 0;
            for (int group = 0; group < 2; group++)
            {
                var list = group == 0 ? loaded.campaigns : loaded.challenges;
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var entry = list[i];
                    if (entry == null || entry.challengeId != challenge) continue;
                    count++;
                    found = entry.Pointer;
                }
            }
            return count == 1 ? found : IntPtr.Zero;
        }
        catch { return IntPtr.Zero; }
    }

    private static bool TryReadPrefsKey(OwnerState owner, out string raw)
    {
        raw = null;
        try
        {
            GlobalSaveData loaded = GlobalSaveData.loaded;
            if (loaded == null || loaded.Pointer != owner.Global) return false;
            var prefs = loaded.prefs;
            if (prefs == null || prefs.Pointer != owner.Prefs) return false;
            var contents = prefs.contents;
            if (contents == null) return false;
            if (contents.TryGetValue(MusketeerNativeArchive.Key, out string found)) raw = found;
            return true;
        }
        catch { return false; }
    }

    private static bool TryWritePrefsKey(OwnerState owner, string raw)
    {
        try
        {
            GlobalSaveData loaded = GlobalSaveData.loaded;
            if (loaded == null || loaded.Pointer != owner.Global) return false;
            var prefs = loaded.prefs;
            if (prefs == null || prefs.Pointer != owner.Prefs || prefs.contents == null) return false;
            prefs.contents[MusketeerNativeArchive.Key] = raw;
            return prefs.contents.TryGetValue(MusketeerNativeArchive.Key, out string readback)
                && string.Equals(readback, raw, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    // Compare-and-swap against the exact bytes the caller read. When the native key vanished, a
    // legacy sidecar whose bytes still match stays a valid CAS base (the migration write is the
    // first native key ever published for this owner).
    private static bool CasSource(OwnerState owner, MusketeerArchiveStore.ReadResult expected, out string reason)
    {
        reason = null;
        if (!TryReadPrefsKey(owner, out string raw))
        {
            reason = ReasonCatalogInconsistent;
            return false;
        }
        if (raw != null)
        {
            if (expected.Original != null && BytesEqual(Encoding.UTF8.GetBytes(raw), expected.Original)) return true;
            reason = ReasonPrefsKeyLost;
            return false;
        }
        if (expected.Original == null)
        {
            byte[] legacy = ReadAllBytesQuiet(MusketeerPersistence.ArchivePath);
            if (legacy == null || legacy.Length == 0) return true;
            reason = ReasonPrefsKeyLost;
            return false;
        }
        byte[] sidecar = ReadAllBytesQuiet(MusketeerPersistence.ArchivePath);
        if (sidecar != null && BytesEqual(sidecar, expected.Original)) return true;
        // A same-session paired recovery base: the accepted native file itself is the source that
        // produced this read, so its unchanged bytes are a valid CAS base for the first re-stage.
        if (string.Equals(expected.Detail, "pair", StringComparison.Ordinal))
        {
            string path = EnsureOwnerPath(owner);
            byte[] disk = path != null ? ReadAllBytesBounded(path) : null;
            if (disk != null && BytesEqual(disk, expected.Original)) return true;
        }
        reason = ReasonPrefsKeyLost;
        return false;
    }

    // ------------------------------------------------------------------ writer gate

    internal static string CanonicalPath(string folder, string file)
    {
        try
        {
            string combined = string.IsNullOrEmpty(folder) ? (file ?? "") : Path.Combine(folder, file);
            return Path.GetFullPath(combined);
        }
        catch { return (folder ?? "") + "|" + (file ?? ""); }
    }

    // The physical writer gate. Pass-through only for files that are not the exact current owner
    // save at its captured location; everything else is validated before the native original runs.
    // A rejection never takes a lease ("reject validation发生在nativeoriginal前则无lease").
    internal static WriterVerdict GatePayload(string folder, string filename, byte[] data, bool sync, out WriterCall call, out string reason)
    {
        call = null;
        reason = null;
        try
        {
            if (!MusketeerNativeArchive.ValidFile(filename)) return WriterVerdict.Pass;
            OwnerState owner = _owner;
            if (owner == null || !string.Equals(filename, owner.File, StringComparison.Ordinal)) return WriterVerdict.Pass;
            if (!CurrentOwnerIs(owner)) return WriterVerdict.Pass;      // replaced Global: no stale responsibility
            string ownerPath = EnsureOwnerPath(owner);
            if (ownerPath == null)
            {
                LogOnce("owner-path-unknown", Short(filename));
                return WriterVerdict.Pass;
            }
            if (!string.Equals(CanonicalPath(folder, filename), ownerPath, StringComparison.Ordinal))
                return WriterVerdict.Pass;                              // another folder's file: not our physical save
            string path = ownerPath;
            lock (Sync)
            {
                if (!TryEnterPath(path, out WriterVerdict blocked, out reason)) return blocked;
            }
            // Payload validation freezes the rights and reads the catalog once at writer entry;
            // it never rewrites prefs and never takes a lease, so a rejection leaves this path free.
            string failure = ValidateOutgoing(data, owner, out string documentRaw, out RightsSnapshot rights);
            if (failure != null)
            {
                reason = failure;
                LogOnce("gate-fail:" + failure, Short(filename) + ":" + failure);
                return WriterVerdict.Fail;
            }
            lock (Sync)
            {
                // Re-check after validation: another thread must not have entered meanwhile.
                if (!TryEnterPath(path, out WriterVerdict blocked, out reason)) return blocked;
                // Only the sync coroutine gets an attempt token: its finalizer harvests the same
                // MoveNext call, so the binding is real. The async callback path is bound by content
                // (its own frozen observation rights), never by a guessed attempt.
                var attempt = sync
                    ? new Attempt
                    {
                        Owner = owner,
                        Sequence = ++_nextSequence,
                        Path = path,
                        File = filename,
                        Rights = rights
                    }
                    : null;
                Leases[path] = new Lease { Sync = sync };
                call = new WriterCall { Path = path, File = filename, Attempt = attempt };
                return WriterVerdict.Pass;
            }
        }
        catch (Exception e)
        {
            Log("gate-exception", e);
            reason = "GateException";
            return WriterVerdict.Fail;
        }
    }

    // UnknownWriter is sticky per canonical path (only a process restart or a real terminal
    // witness clears it); a pending writer returns Busy and is never released on a timer.
    private static bool TryEnterPath(string path, out WriterVerdict blocked, out string reason)
    {
        blocked = WriterVerdict.Pass;
        reason = null;
        if (UnknownWriters.Contains(path))
        {
            blocked = WriterVerdict.UnknownWriter;
            reason = ReasonUnknownWriter;
            return false;
        }
        if (!Leases.TryGetValue(path, out var lease)) return true;
        if (!lease.Sync && lease.TaskKnown)
        {
            if (lease.Task != null && lease.Task.IsCompleted)
            {
                Leases.Remove(path);   // terminal witness: the previous physical copy is over
                return true;
            }
        }
        // A native factory that has not returned its Task yet, a sync coroutine body in flight and
        // a pending async Task are all known in-flight owners: Busy, lease kept, never a timer.
        blocked = WriterVerdict.Busy;
        reason = "Busy";
        return false;
    }

    // Async postfix: hold the real Task until its terminal state is witnessed.
    internal static void WriterStarted(WriterCall call, Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> task)
    {
        if (call == null) return;
        try
        {
            lock (Sync)
            {
                if (!Leases.TryGetValue(call.Path, out var lease) || lease.Sync) return;
                if (task == null)
                {
                    UnknownWriters.Add(call.Path);
                    Leases.Remove(call.Path);
                    LogOnce("unknown-writer-null", call.Path);
                    return;
                }
                lease.Task = task;
                lease.TaskKnown = true;
                if (task.IsCompleted) Leases.Remove(call.Path);
            }
        }
        catch (Exception e) { Log("writer-started", e); }
    }

    // Async finalizer: a real Task that is already rooted must keep its root until its terminal
    // state (another patch's postfix may have thrown after this Task was witnessed). Only a
    // confirmed exception with no witnessed Task leaves the sticky, actionable UnknownWriter fault;
    // the plain entering window stays Busy with its lease held.
    internal static void WriterFaulted(WriterCall call, Exception exception)
    {
        if (call == null) return;
        try
        {
            lock (Sync)
            {
                if (!Leases.TryGetValue(call.Path, out var lease) || lease.Sync) return;
                if (lease.TaskKnown && lease.Task != null)
                {
                    if (exception != null) LogOnce("writer-fault-kept-task", call.Path + ":" + exception.GetType().Name);
                    return;
                }
                if (exception == null)
                {
                    LogOnce("writer-no-task", call.Path);
                    return;
                }
                UnknownWriters.Add(call.Path);
                Leases.Remove(call.Path);
                LogOnce("unknown-writer-fault", call.Path + ":" + exception.GetType().Name);
            }
        }
        catch (Exception e) { Log("writer-faulted", e); }
    }

    // Sync finalizer: this call owned the lease for its whole MoveNext body (no yield).
    internal static void SyncWriterFinished(WriterCall call)
    {
        if (call == null) return;
        try
        {
            lock (Sync)
            {
                if (Leases.TryGetValue(call.Path, out var lease) && lease.Sync) Leases.Remove(call.Path);
            }
        }
        catch (Exception e) { Log("sync-finish", e); }
    }

    internal static void ResetForTests()
    {
        lock (Sync)
        {
            Leases.Clear();
            UnknownWriters.Clear();
            PendingObservations.Clear();
            _owner = null;
            _nextSequence = 0;
            _nextOwnerId = 0;
            _failureTask = null;
            _busyTask = null;
            _rejectTasksUnavailable = false;
            Logged.Clear();
        }
    }

    // ------------------------------------------------------------------ payload validation

    // Live writer-entry capture: reconcile provably deleted sources first, then judge the capture
    // faults of still-protected contexts, then validate the outgoing bytes. The frozen rights the
    // writer attempt and the accepted callback carry are produced here.
    private static string ValidateOutgoing(byte[] data, OwnerState owner, out string documentRaw, out RightsSnapshot rights)
    {
        documentRaw = null;
        if (owner == null)
        {
            rights = new RightsSnapshot();
            return null;
        }
        rights = CaptureRights(owner);
        if (!rights.Protected) return null;
        ReconcileDeletedSources(owner, rights);
        lock (Sync)
        {
            foreach (string fault in owner.Faults)
                if (owner.Protected.Contains(fault) || owner.Proofs.ContainsKey(fault))
                    return ReasonCaptureFault;
        }
        return ValidatePayload(data, owner.File, rights, out documentRaw);
    }

    private static RightsSnapshot CaptureRights(OwnerState owner)
    {
        var rights = new RightsSnapshot();
        lock (Sync)
        {
            rights.DocumentRaw = owner.StagedRaw;
            if (owner.Proofs.Count > 0)
            {
                var proofs = new FrozenProof[owner.Proofs.Count];
                int index = 0;
                foreach (var proof in owner.Proofs.Values)
                    proofs[index++] = new FrozenProof
                    {
                        Context = proof.Context,
                        Epoch = proof.Epoch,
                        Hash = proof.Hash,
                        Campaign = proof.Campaign,
                        Challenge = proof.Challenge,
                        Land = proof.Land,
                        Owner = proof.Owner
                    };
                rights.Proofs = proofs;
            }
        }
        rights.Protected = rights.DocumentRaw != null || rights.Proofs.Length > 0;
        if (rights.Proofs.Length == 0) return rights;
        var catalog = CaptureCatalog();
        if (catalog == null) return rights;
        rights.CatalogReadable = catalog.Readable;
        rights.Campaigns = catalog.Campaigns.ToArray();
        rights.Challenges = catalog.Challenges.ToArray();
        return rights;
    }

    // A proof is required only while its witnessed source exists. A source that is provably absent
    // from a successful catalog read has its proof and fault revoked; an unreadable catalog proves
    // nothing and the validation fails closed instead.
    private static void ReconcileDeletedSources(OwnerState owner, RightsSnapshot rights)
    {
        if (!rights.CatalogReadable) return;
        var revoked = new List<string>();
        lock (Sync)
        {
            foreach (var proof in owner.Proofs.Values)
            {
                if (proof.Owner == IntPtr.Zero) continue;                 // never witnessed: not provable
                // A campaign source is proven absent from the campaign list; a challenge source may
                // live in either list, so absence is judged over the union. A readable catalog that
                // no longer contains the witnessed pointer is the only deletion proof.
                bool absent = proof.Challenge == 0
                    ? rights.SlotOf(proof.Owner) < 0
                    : !rights.ContainsOwner(proof.Owner);
                if (absent) revoked.Add(proof.Context);
            }
            foreach (string context in revoked)
            {
                owner.Proofs.Remove(context);
                owner.Faults.Remove(context);
            }
        }
        if (revoked.Count == 0) return;
        var survivors = new List<FrozenProof>(rights.Proofs.Length);
        foreach (var proof in rights.Proofs)
            if (!revoked.Contains(proof.Context)) survivors.Add(proof);
        rights.Proofs = survivors.ToArray();
        rights.Protected = rights.DocumentRaw != null || rights.Proofs.Length > 0;
    }

    // The validator reads only the frozen managed snapshot and the actual payload bytes. Zero
    // rights means the file simply carries no musket rights yet: it passes through untouched (a
    // corrupt/foreign key, if any, is preserved).
    private static string ValidatePayload(byte[] data, string file, RightsSnapshot rights, out string documentRaw)
    {
        documentRaw = null;
        if (rights == null || !rights.Protected) return null;
        if (!MusketeerNativeArchive.TryExtractDocument(data, file, out string raw, out var archive, out _, out bool unsupported, out string extractReason))
        {
            // While a checkpoint is protected, a payload whose native key is absent, altered or
            // structurally unusable is exactly the lost-key case; the detail stays in the log.
            LogOnce("invalid-document:" + extractReason, Short(file));
            if (unsupported) return "UnsupportedDocument";
            return ReasonPrefsKeyLost;
        }
        documentRaw = raw;
        if (rights.DocumentRaw != null && !string.Equals(raw, rights.DocumentRaw, StringComparison.Ordinal)) return ReasonPrefsKeyLost;
        if (rights.Proofs.Length == 0) return null;
        if (!TryDecompressPayload(data, out byte[] json)) return "DocumentInvalid:gzip";
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException) { return "DocumentInvalid:json"; }
        using (document)
        {
            foreach (var proof in rights.Proofs)
            {
                string failure = ValidateProof(proof, archive, document.RootElement, rights);
                if (failure != null) return failure;
            }
        }
        return null;
    }

    private static string ValidateProof(FrozenProof proof, MusketeerArchive archive, JsonElement root, RightsSnapshot rights)
    {
        if (proof.Challenge == 0)
        {
            if (!rights.CatalogReadable) return ReasonCatalogInconsistent;
            if (proof.Owner == IntPtr.Zero) return ReasonCatalogInconsistent;
            int slot = rights.SlotOf(proof.Owner);
            // A revoked (deleted) source no longer requires its snapshot; the live gate reconciler
            // removed it already, a frozen accepted snapshot always had it present.
            if (slot < 0) return null;
            return ValidateProofIslands(proof, slot, archive, root);
        }
        // C7 (explicit, symmetric with the campaign rule): a frozen challenge proof whose witnessed
        // pointer is provably absent from the captured union is already revoked, not required.
        // An unwitnessed owner (zero) or an unreadable catalog decides nothing here; the challenge
        // itself is still located by its unique challengeId below.
        if (rights.CatalogReadable && proof.Owner != IntPtr.Zero && !rights.ContainsOwner(proof.Owner)) return null;
        return ValidateProofIslands(proof, proof.Campaign, archive, root);
    }

    private static string ValidateProofIslands(FrozenProof proof, int campaignSlot, MusketeerArchive archive, JsonElement root)
    {
        if (!archive.TryGetContext(proof.Context, out var context)) return ReasonNoMatchingSnapshot;
        int epochCount = 0;
        foreach (string candidate in context.Epochs)
            if (string.Equals(candidate, proof.Epoch, StringComparison.Ordinal)) epochCount++;
        if (epochCount != 1 || archive.ScopeClaimedBy(proof.Epoch, proof.Context)) return ReasonScopeAmbiguous;
        if (!archive.Scopes.TryGetValue(proof.Epoch, out var snapshots)) return ReasonNoMatchingSnapshot;
        int matches = 0;
        MusketeerSnapshot matched = null;
        foreach (var snapshot in snapshots)
            if (string.Equals(snapshot.Hash, proof.Hash, StringComparison.Ordinal)) { matches++; matched = snapshot; }
        if (matches == 0) return ReasonNoMatchingSnapshot;
        if (matches != 1) return ReasonScopeAmbiguous;

        JsonElement entry = default;
        if (campaignSlot >= 0 && proof.Challenge == 0)
        {
            if (!TryArrayElement(root, "campaigns", campaignSlot, out entry)) return ReasonCatalogInconsistent;
        }
        else
        {
            // A native challenge is matched by challengeId alone; the index is never trusted.
            int count = 0;
            foreach (string array in new[] { "campaigns", "challenges" })
            {
                if (!root.TryGetProperty(array, out var entries) || entries.ValueKind != JsonValueKind.Array) continue;
                foreach (var candidate in entries.EnumerateArray())
                {
                    if (candidate.ValueKind != JsonValueKind.Object) continue;
                    if (!candidate.TryGetProperty("challengeId", out var idNode)
                        || idNode.ValueKind != JsonValueKind.Number
                        || !idNode.TryGetInt32(out int id)
                        || id != proof.Challenge) continue;
                    count++;
                    entry = candidate;
                }
            }
            if (count != 1) return ReasonCatalogInconsistent;
        }
        if (!entry.TryGetProperty("_islands", out var islands) || islands.ValueKind != JsonValueKind.Array)
            return ReasonCatalogInconsistent;
        int islandMatches = 0;
        JsonElement islandMatch = default;
        foreach (var island in islands.EnumerateArray())
        {
            if (island.ValueKind != JsonValueKind.Object) continue;
            if (!island.TryGetProperty("land", out var landNode) || !landNode.TryGetInt32(out int land) || land != proof.Land) continue;
            string hash;
            try { hash = MusketeerArchive.IslandHash(island.GetRawText(), proof.Epoch); }
            catch (Exception) { continue; }
            if (!string.Equals(hash, proof.Hash, StringComparison.Ordinal)) continue;
            islandMatches++;
            islandMatch = island;
        }
        if (islandMatches == 0) return ReasonNoMatchingSnapshot;
        if (islandMatches != 1) return ReasonScopeAmbiguous;
        if (!islandMatch.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Array) return "IslandObjects";
        foreach (var record in matched.Records)
        {
            if (string.IsNullOrEmpty(record.NativeId)) continue;
            int rows = 0;
            bool kindOk = false;
            foreach (var row in objects.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                if (!row.TryGetProperty("uniqueID", out var idNode) || idNode.ValueKind != JsonValueKind.String) continue;
                if (!string.Equals(idNode.GetString(), record.NativeId, StringComparison.Ordinal)) continue;
                rows++;
                string prefab = row.TryGetProperty("prefabPath", out var prefabNode) && prefabNode.ValueKind == JsonValueKind.String
                    ? prefabNode.GetString() : null;
                kindOk = record.Kind == MusketeerCareer.KindUnit
                    ? string.Equals(prefab, ArcherPrefabPath, StringComparison.Ordinal)
                    : string.Equals(prefab, ToolBowPrefabPath, StringComparison.Ordinal);
            }
            if (rows == 0) return "NativeIdMissing";
            if (rows != 1) return "NativeIdDuplicate";
            if (!kindOk) return "NativeIdKind";
        }
        return null;
    }

    private static bool TryArrayElement(JsonElement root, string name, int index, out JsonElement element)
    {
        element = default;
        if (index < 0) return false;
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return false;
        int i = 0;
        foreach (var candidate in array.EnumerateArray())
        {
            if (i++ != index) continue;
            element = candidate;
            return candidate.ValueKind == JsonValueKind.Object;
        }
        return false;
    }

    // Content read only: integrity (header, bounded size, CRC/ISIZE trailer) was already validated
    // by TryExtractDocument over the exact same bytes.
    private static bool TryDecompressPayload(byte[] data, out byte[] json)
    {
        json = null;
        try
        {
            using var input = new MemoryStream(data, false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            byte[] buffer = new byte[81920];
            int total = 0;
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > MusketeerNativeArchive.MaxNativeDecompressedBytes) return false;
                output.Write(buffer, 0, read);
            }
            json = output.ToArray();
            return true;
        }
        catch (Exception) { return false; }
    }

    private sealed class CatalogSnapshot
    {
        internal bool Readable;
        internal readonly List<IntPtr> Campaigns = new();
        internal readonly List<IntPtr> Challenges = new();
    }

    // One bounded catalog read at capture time; no scene access, no Unity getter.
    private static CatalogSnapshot CaptureCatalog()
    {
        try
        {
            GlobalSaveData loaded = GlobalSaveData.loaded;
            if (loaded == null) return null;
            var campaigns = loaded.campaigns;
            var challenges = loaded.challenges;
            if (campaigns == null || challenges == null) return null;
            var snapshot = new CatalogSnapshot { Readable = true };
            for (int i = 0; i < campaigns.Count; i++)
            {
                var entry = campaigns[i];
                snapshot.Campaigns.Add(entry != null ? entry.Pointer : IntPtr.Zero);
            }
            for (int i = 0; i < challenges.Count; i++)
            {
                var entry = challenges[i];
                snapshot.Challenges.Add(entry != null ? entry.Pointer : IntPtr.Zero);
            }
            return snapshot;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ accepted backup

    // Captured on the main thread from the SaveAsync prefix: the owner, this observation's
    // monotonic sequence, the concrete file path (Filer.folder + filename, never guessed) and the
    // frozen rights (staged raw + proofs + catalog pointer snapshot) that a no-change callback
    // verifies against. The observation stays statically rooted until its callback completes.
    internal static SaveObservation BeginSaveObservation(GlobalSaveData instance)
    {
        try
        {
            if (instance == null) return null;
            GlobalSaveData loaded = GlobalSaveData.loaded;
            if (loaded == null || loaded.Pointer != instance.Pointer) return null;
            OwnerState owner = _owner;
            if (owner == null || !CurrentOwnerIs(owner)) return null;
            string path = EnsureOwnerPath(owner);
            if (path == null)
            {
                LogOnce("observe-no-path", Short(owner.File));
                return null;
            }
            var observation = new SaveObservation
            {
                Owner = owner,
                Sequence = ++_nextSequence,
                File = owner.File,
                Path = path,
                Rights = CaptureRights(owner)
            };
            lock (Sync)
            {
                // At capacity, an unfinished observation is never evicted: it is the explicit
                // managed root of a callback that must still be forwarded exactly once. New
                // callbacks are left unwrapped (the observer returns true without replacing the
                // argument), which only degrades the paired backup and stays observable.
                if (PendingObservations.Count >= MaxPendingObservations)
                {
                    LogOnce("observe-cap:" + owner.File, Short(owner.File));
                    return null;
                }
                PendingObservations.Add(observation);
            }
            return observation;
        }
        catch (Exception e) { Log("observe-save", e); return null; }
    }

    // Wrapped callback: the original callback is forwarded exactly once with its unmodified result
    // (every result, including late ones). A duplicate wrapper invocation never repeats it; own
    // failures never leak into the native continuation. Only a result carrying the success bit and
    // none of Failure/Busy/Cancelled produces/verifies a pairing.
    internal static void CompleteSaveObservation(SaveObservation observation, SaveLoadResult result, Il2CppSystem.Action<SaveLoadResult> original)
    {
        if (observation == null)
        {
            Forward(original, result);
            return;
        }
        if (Interlocked.Exchange(ref observation.Consumption, 1) != 0)
        {
            LogOnce("callback-duplicate", Short(observation.File));
            return;
        }
        try
        {
            if (Accepted((int)result)) HarvestObservation(observation, (int)result);
        }
        catch (Exception e) { Log("harvest-exception", e); }
        finally
        {
            try { Forward(original, result); }
            finally
            {
                observation.Managed = null;
                lock (Sync) PendingObservations.Remove(observation);
            }
        }
    }

    private static void Forward(Il2CppSystem.Action<SaveLoadResult> original, SaveLoadResult result)
    {
        try { original?.Invoke(result); }
        catch (Exception e) { LogOnce("callback-throw", e.GetType().Name); }
    }

    private static bool Accepted(int result)
        => (result & ResultSuccess) != 0
           && (result & (ResultFailure | ResultBusy | ResultCancelled)) == 0;

    // An accepted callback verifies its own immutable observation against the actual disk file,
    // including serializer no-change 72. No writer-attempt order is inferred from callback order.
    private static bool HarvestObservation(SaveObservation observation, int result)
        => Harvest(observation.File, observation.Path, observation.Rights, observation.Owner, observation.Sequence, result);

    // Sync finalizer path: this call's own attempt, harvested in the same MoveNext call.
    internal static bool HarvestAttempt(Attempt attempt, int result)
    {
        if (attempt == null) return false;
        if (!MarkAttempt(attempt)) return false;
        return Harvest(attempt.File, attempt.Path, attempt.Rights, attempt.Owner, attempt.Sequence, result);
    }

    // Read back the actual file the accepted writer produced and pair it only when the frozen
    // document is on disk and its frozen proofs still validate against those bytes. The pair
    // container itself is derived from the disk payload; the legacy sidecar is mirrored only
    // through a CAS write over a missing/valid file (corrupt or future originals are preserved).
    private static bool Harvest(string file, string path, RightsSnapshot rights, OwnerState tokenOwner, long tokenSequence, int result)
    {
        try
        {
            if (tokenOwner == null || !MarkSequence(tokenOwner, tokenSequence)) return false;
            byte[] bytes = ReadAllBytesBounded(path);
            if (bytes == null) { LogOnce("harvest-read:" + file, path); return false; }
            if (!MusketeerNativeArchive.TryExtractDocument(bytes, file, out string raw, out var archive, out byte[] archiveBytes, out bool unsupported, out string extractReason))
            {
                LogOnce("harvest-extract:" + (unsupported ? "unsupported:" + extractReason : extractReason), file);
                return false;
            }
            if (rights?.DocumentRaw != null && !string.Equals(raw, rights.DocumentRaw, StringComparison.Ordinal))
            {
                LogOnce("harvest-mismatch", file);
                return false;
            }
            string failure = ValidatePayload(bytes, file, rights, out _);
            if (failure != null) { LogOnce("harvest-proof:" + failure, file); return false; }
            if (!MusketeerPairedBackup.Save(PairPath(file), file, bytes, out string pairReason))
            {
                LogOnce("harvest-pair:" + pairReason, file);
                return false;
            }
            MirrorSidecar(archiveBytes, archive);
            LogEvent(result == NoChangeCode ? "paired:verified-existing:" + Short(file) : "paired:" + Short(file));
            return true;
        }
        catch (Exception e)
        {
            Log("harvest", e);
            return false;
        }
    }

    internal static bool MarkAttempt(Attempt attempt)
    {
        lock (Sync)
        {
            if (attempt == null || attempt.Consumed) return false;
            attempt.Consumed = true;   // idempotent: one sync MoveNext call harvests exactly once
            return true;
        }
    }

    // Only a strictly newer sequence from the still-current owner may pair or mirror. A late
    // callback of an older save (or of a replaced owner) is dropped without touching any file.
    private static bool MarkSequence(OwnerState owner, long sequence)
    {
        lock (Sync)
        {
            if (!ReferenceEquals(owner, _owner))
            {
                LogOnce("harvest-owner", Short(owner.File));
                return false;
            }
            if (sequence <= owner.LastConsumedSequence)
            {
                LogOnce("harvest-late", Short(owner.File));
                return false;
            }
            owner.LastConsumedSequence = sequence;
            return true;
        }
    }

    private static void MirrorSidecar(byte[] archiveBytes, MusketeerArchive archive)
    {
        try
        {
            if (archiveBytes == null || archive == null) return;
            string path = MusketeerPersistence.ArchivePath;
            var current = MusketeerArchiveStore.Load(path);
            if (current.Status != MusketeerArchiveStore.State.Missing && current.Status != MusketeerArchiveStore.State.Valid)
            {
                LogOnce("mirror-degraded:" + current.Status, current.Detail);
                return;
            }
            if (!current.Writable || !MusketeerArchiveStore.Save(path, current, archive))
                LogOnce("mirror-degraded:write", current.Detail);
        }
        catch (Exception e) { Log("mirror", e); }
    }

    private static string PairPath(string file) => Path.Combine(Path.GetDirectoryName(MusketeerPersistence.ArchivePath), "musketeer-pair-" + file + ".json");

    // ------------------------------------------------------------------ small helpers

    private static byte[] ReadAllBytesBounded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            if (info.Length <= 0 || info.Length > MusketeerNativeArchive.MaxNativeCompressedBytes) return null;
            return File.ReadAllBytes(path);
        }
        catch { return null; }
    }

    private static byte[] ReadAllBytesQuiet(string path)
    {
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch { return null; }
    }

    private static bool BytesEqual(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);

    private static string Short(string value) => string.IsNullOrEmpty(value) || value.Length < 8 ? value : value.Substring(0, 8);

    private static void LogEvent(string message) => Emit(message, null, warning: false);

    private static void LogOnce(string key, string detail)
    {
        lock (Sync)
        {
            if (!Logged.Contains(key) && Logged.Count >= MaxLoggedKeys) return;
            if (!Logged.Add(key)) return;
        }
        Emit(key, detail, warning: true);
    }

    private static void Log(string key, Exception error) => LogOnce(key, error?.GetType().Name + ": " + error?.Message);

    private static void Emit(string text, string detail, bool warning)
    {
        try
        {
            string line = string.IsNullOrEmpty(detail) ? text : text + ": " + detail;
            if (line.Length > 240) line = line.Substring(0, 240);
            if (warning) KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[MusketeerSync] " + line);
            else KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[MusketeerSync] " + line);
        }
        catch { }
    }

    // ------------------------------------------------------------------ Harmony adapter

    // Warm and strongly root the completed rejection Tasks. When this endpoint cannot be produced
    // (AOT generic failure) the module reports itself unavailable: no new staging is accepted and a
    // rejection that cannot be expressed never pretends to be a normal Failure result.
    private static void PrewarmRejectTasks()
    {
        if (_failureTask == null) RejectTask(ResultFailureCode);
        if (_busyTask == null) RejectTask(ResultBusyCode);
    }

    private static Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> RejectTask(int value)
    {
        try
        {
            if (value == ResultBusyCode) return _busyTask ??= Il2CppSystem.Threading.Tasks.Task.FromResult((SaveLoadResult)value);
            return _failureTask ??= Il2CppSystem.Threading.Tasks.Task.FromResult((SaveLoadResult)value);
        }
        catch (Exception e)
        {
            if (!_rejectTasksUnavailable)
            {
                _rejectTasksUnavailable = true;
                LogOnce("reject-task-unavailable", e.GetType().Name);
            }
            return null;
        }
    }

    internal static bool RejectEndpointUnavailable => _rejectTasksUnavailable;

    // Async physical writer (the single factory the Filer awaits): reject before the async state
    // machine creates any File/Directory work, otherwise take the per-path lease and capture the
    // attempt. The real Task is rooted by the postfix; the finalizer keeps unknown-writer faults.
    [HarmonyPatch(typeof(Filer.DotNetAgent), "SaveFileToDiskAsync",
        new[] { typeof(string), typeof(string), typeof(string), typeof(Il2CppStructArray<byte>) })]
    internal static class Musketeer_AsyncWriterGate
    {
        [HarmonyPrefix]
        internal static bool Prefix(Filer.DotNetAgent __instance, string filename, string title, string details,
            Il2CppStructArray<byte> data, ref Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> __result, out WriterCall __state)
        {
            __state = null;
            try
            {
                string folder = null;
                try { if (__instance != null) folder = __instance.folder; } catch { }
                var verdict = GatePayload(folder, filename, ToManaged(data), sync: false, out var call, out string reason);
                if (verdict == WriterVerdict.Pass) { __state = call; return true; }
                __result = RejectTask(verdict == WriterVerdict.Busy ? ResultBusyCode : ResultFailureCode);
                if (__result == null) LogOnce("async-reject-unavailable", reason ?? "?");
                return false;
            }
            catch (Exception e)
            {
                Log("async-gate", e);
                __result = RejectTask(ResultFailureCode);
                return false;
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(WriterCall __state, Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> __result)
        {
            // __state is non-null only when this call actually passed the gate and took its lease.
            if (__state != null) WriterStarted(__state, __result);
        }

        [HarmonyFinalizer]
        internal static Exception Finally(Exception __exception, WriterCall __state)
        {
            try { WriterFaulted(__state, __exception); } catch (Exception e) { Log("async-finally", e); }
            return __exception;
        }
    }

    // Sync physical writer coroutine: state 0 is the only body and it writes directly through a
    // FileStream. Rejection sets the native boxed Return to Failure (or Failure|Busy) and skips
    // the body; a successful call (Return == 72) verifies the readback in the same finalizer.
    [HarmonyPatch(typeof(Filer.DotNetAgent._SaveFileToDisk_d__10), "MoveNext")]
    internal static class Musketeer_SyncWriterGate
    {
        [HarmonyPrefix]
        internal static bool Prefix(Filer.DotNetAgent._SaveFileToDisk_d__10 __instance, ref bool __result, out WriterCall __state)
        {
            __state = null;
            if (__instance == null) return true;
            try
            {
                if (__instance.__1__state != 0) return true;
                string filename = __instance.filename;
                string folder = null;
                try { if (__instance.__4__this != null) folder = __instance.__4__this.folder; } catch { }
                var verdict = GatePayload(folder, filename, ToManaged(__instance.data), sync: true, out var call, out string reason);
                if (verdict == WriterVerdict.Pass) { __state = call; return true; }
                __result = false;
                return RejectSync(__instance, verdict == WriterVerdict.Busy ? ResultBusyCode : ResultFailureCode, reason);
            }
            catch (Exception e)
            {
                Log("sync-gate", e);
                __result = false;
                return RejectSync(__instance, ResultFailureCode, "gate-exception");
            }
        }

        [HarmonyFinalizer]
        internal static Exception Finally(Exception __exception, Filer.DotNetAgent._SaveFileToDisk_d__10 __instance, WriterCall __state)
        {
            try
            {
                if (__state != null)
                {
                    SyncWriterFinished(__state);
                    if (__exception == null && __instance != null && (int)__instance.@return.value == NoChangeCode)
                        HarvestAttempt(__state.Attempt, NoChangeCode);
                }
            }
            catch (Exception e) { Log("sync-finally", e); }
            return __exception;
        }

        private static bool RejectSync(Filer.DotNetAgent._SaveFileToDisk_d__10 instance, int value, string reason)
        {
            try
            {
                var boxed = instance.@return;
                boxed.value = (SaveLoadResult)value;
                instance.@return = boxed;
                if ((int)instance.@return.value != value)
                    LogOnce("sync-reject-readback", reason ?? "?");
            }
            catch (Exception e) { Log("sync-reject-write", e); }
            try { instance.__1__state = -1; } catch (Exception) { }
            LogOnce("sync-reject:" + (reason ?? "?"), Short(instance.filename));
            return false;
        }
    }

    // SaveAsync observer (runs last, bypasses once another gate already refused the original):
    // wraps the native callback with a managed delegate that is forwarded exactly once.
    [HarmonyPatch(typeof(GlobalSaveData), nameof(GlobalSaveData.SaveAsync))]
    internal static class Musketeer_SaveAsyncObserver
    {
        [HarmonyPrefix, HarmonyPriority(Priority.Last)]
        internal static bool Prefix(GlobalSaveData __instance, ref Il2CppSystem.Action<SaveLoadResult> __0, ref bool __runOriginal)
        {
            if (!__runOriginal) return true;
            try
            {
                var original = __0;
                if (original == null || __instance == null) return true;
                var observation = BeginSaveObservation(__instance);
                if (observation == null) return true;
                System.Action<SaveLoadResult> managed = result => CompleteSaveObservation(observation, result, original);
                observation.Managed = managed;                       // explicit managed root until completion
                __0 = (Il2CppSystem.Action<SaveLoadResult>)managed;  // converted IL2CPP delegate replaces the argument
            }
            catch (Exception e) { Log("save-observer", e); }
            return true;
        }
    }

    private static byte[] ToManaged(Il2CppStructArray<byte> data)
    {
        if (data == null) return null;
        try
        {
            int length = data.Length;
            if (length <= 0 || length > MusketeerNativeArchive.MaxNativeCompressedBytes) return null;
            var bytes = new byte[length];
            for (int i = 0; i < length; i++) bytes[i] = data[i];
            return bytes;
        }
        catch (Exception e) { Log("native-bytes", e); return null; }
    }
}
