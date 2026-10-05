using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
#if ANDROID
using Il2CppCoatsink.Common;
#else
using Coatsink.Common;
#endif
using HarmonyLib;

namespace KingdomEnhancedMod;

// New hero receipts travel with the native wallet in GlobalSaveData.prefs. The sidecar remains
// untouched as the source for saves written before this key existed. The key only contains
// snapshots captured at the normal end of IslandSaveData.Save. A failed capture is kept as an
// in-memory source responsibility (never written as a fabricated snapshot): the last successful
// checkpoint stays the immutable record, the global save is refused until a complete capture of
// the same source re-verifies the pairing, and a re-entered source can rebind its owner from
// that checkpoint when the loaded island still contains the paid character.
internal static class HeroNativeRights
{
    private const string Key = "KingdomEnhancedMod_HeroRights_v1";
    private const int MaxDocumentBytes = 1536 * 1024;

    private sealed class Document
    {
        public int Version { get; set; } = 1;
        public string File { get; set; }
        public List<string> Campaigns { get; set; } = new();
        public List<string> Challenges { get; set; } = new();
        public Dictionary<string, string> SidecarContexts { get; set; } = new();
        public string Archive { get; set; }
    }

    // One live source responsibility: the owning campaign/challenge object, the stable native
    // context key it resolves to (null until a healthy catalog read can resolve it), and the
    // land. A responsibility ends only when its owner is provably gone from a catalog read that
    // succeeded, when its receipt is consumed/abandoned, or when a complete capture re-verifies
    // the context.
    private sealed class Custody
    {
        public string Key;
        public int Land;
        public bool Campaign;
        public IntPtr Owner;
    }

    private static GlobalSaveData _global;
    private static PrefsSaveData _prefs;
    private static string _file;
    private static string _stored;
    private static Document _document;
    private static HeroRecruitmentArchive _archive;
    private static readonly List<IntPtr> CampaignRefs = new();
    private static readonly List<IntPtr> ChallengeRefs = new();
    private static readonly Dictionary<Guid, Custody> Pending = new();
    private static readonly List<Custody> InvalidCaptures = new();
    private static bool _fault;
    private static bool _dirty;

    private static bool Current(GlobalSaveData value)
        => value != null && value.Pointer != IntPtr.Zero && GlobalSaveData._loaded != null
            && value.Pointer == GlobalSaveData._loaded.Pointer && value.prefs != null
            && value.prefs.contents != null && !string.IsNullOrEmpty(GlobalSaveData.filename)
            && GlobalSaveData.filename.Length <= 256;

    private static bool Catalog(GlobalSaveData value, List<IntPtr> campaigns, List<IntPtr> challenges)
    {
        if (value.campaigns == null || value.challenges == null
            || value.campaigns.Count > HeroRecruitmentArchive.MaxContexts
            || value.challenges.Count > HeroRecruitmentArchive.MaxContexts) return false;
        foreach (var item in value.campaigns)
        {
            if (item == null || item.Pointer == IntPtr.Zero || campaigns.Contains(item.Pointer)) return false;
            campaigns.Add(item.Pointer);
        }
        foreach (var item in value.challenges)
        {
            if (item == null || item.Pointer == IntPtr.Zero || challenges.Contains(item.Pointer)) return false;
            challenges.Add(item.Pointer);
        }
        return true;
    }

    private static bool Same(GlobalSaveData value, PrefsSaveData prefs, string file)
        => _global != null && _global.Pointer == value.Pointer && _prefs != null
            && _prefs.Pointer == prefs.Pointer && _file == file;

    // True when the prefs read completed; a missing key is a successful read of null. Only a
    // throwing read is a transient failure and stays fail-closed on this call.
    private static bool Read(PrefsSaveData prefs, out string stored)
    {
        stored = null;
        try { prefs.contents.TryGetValue(Key, out stored); return true; }
        catch { return false; }
    }

    private static bool Fault()
    {
        _fault = true;
        return false;
    }

    // A transient read failure is retried on every call: only a document that was actually read
    // and validated is healthy, a genuinely corrupt one keeps failing, and in-flight
    // responsibilities (pending receipts, failed captures) are retained across retries. They are
    // never cleared as a way to unlock a save.
    private static bool Bind()
    {
        try
        {
            var value = GlobalSaveData._loaded;
            if (!Current(value)) return false;
            string file = GlobalSaveData.filename;
            var prefs = value.prefs;
            if (_document != null && !_fault && Same(value, prefs, file))
            {
                // This call fails closed on a throwing read; the next call re-validates and may
                // recover once the same owner/prefs/file reads healthily again.
                if (!Read(prefs, out string current)) return Fault();
                if (current == _stored) return SyncCatalog(value);
            }
            return Revalidate(value, file, prefs);
        }
        catch { _fault = true; return false; }
    }

    private static bool Revalidate(GlobalSaveData value, string file, PrefsSaveData prefs)
    {
        if (!Read(prefs, out string stored)) return Fault();       // unreadable prefs: retry later
        bool same = Same(value, prefs, file);
        if (same && _document != null && stored == _stored)
        {
            _fault = false;
            return SyncCatalog(value);
        }
        var campaigns = new List<IntPtr>(); var challenges = new List<IntPtr>();
        if (!Catalog(value, campaigns, challenges)) return Fault();
        if (stored == null && same && _document != null)
        {
            // The key vanished while the same Global is loaded: keep the in-memory document and
            // its responsibilities instead of dropping an in-flight receipt with a prefs sweep.
            _fault = false;
            _stored = null;
            return SyncCatalog(value);
        }
        Document doc;
        HeroRecruitmentArchive archive;
        if (stored == null)
        {
            archive = new HeroRecruitmentArchive();
            doc = new Document { File = file, Archive = Convert.ToBase64String(archive.Encode()) };
            for (int i = 0; i < campaigns.Count; i++) doc.Campaigns.Add(Guid.NewGuid().ToString("N"));
            for (int i = 0; i < challenges.Count; i++) doc.Challenges.Add(Guid.NewGuid().ToString("N"));
        }
        else
        {
            if (Encoding.UTF8.GetByteCount(stored) > MaxDocumentBytes) return Fault();
            doc = JsonSerializer.Deserialize<Document>(stored);
            if (doc == null || doc.Version != 1 || doc.File != file || doc.Campaigns == null
                || doc.Challenges == null || doc.Archive == null
                || doc.Campaigns.Count != campaigns.Count || doc.Challenges.Count != challenges.Count
                || !ValidGuids(doc.Campaigns, doc.Challenges)
                || doc.SidecarContexts == null || doc.SidecarContexts.Count > 256
                || doc.SidecarContexts.Any(x => !HeroRecruitmentArchive.HashValid(x.Key)
                    || !HeroRecruitmentArchive.HashValid(x.Value))) return Fault();
            archive = HeroRecruitmentArchive.Decode(Convert.FromBase64String(doc.Archive), out bool unsupported);
            if (unsupported || archive == null) return Fault();
        }
        _global = value; _prefs = prefs; _file = file; _stored = stored;
        _document = doc; _archive = archive; _fault = false; _dirty = false;
        if (!same)
        {
            // A different Global (real load / new game) starts from its own wallet and catalog:
            // responsibilities of the replaced instance do not carry over.
            Pending.Clear();
            InvalidCaptures.Clear();
            CampaignRefs.Clear(); CampaignRefs.AddRange(campaigns);
            ChallengeRefs.Clear(); ChallengeRefs.AddRange(challenges);
            CatalogGeneration++;
            return true;
        }
        if (CampaignRefs.Count == doc.Campaigns.Count && ChallengeRefs.Count == doc.Challenges.Count)
        {
            // Same Global, new content: keep surviving objects' private GUIDs, then release only
            // the responsibilities whose owner is provably gone.
            if (!Remap(campaigns, CampaignRefs, doc.Campaigns)
                || !Remap(challenges, ChallengeRefs, doc.Challenges)) return Fault();
        }
        else
        {
            CampaignRefs.Clear(); CampaignRefs.AddRange(campaigns);
            ChallengeRefs.Clear(); ChallengeRefs.AddRange(challenges);
        }
        Reconcile(campaigns, challenges);
        return true;
    }

    private static bool ValidGuids(List<string> first, List<string> second)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in first.Concat(second))
            if (!Guid.TryParseExact(id, "N", out _) || !seen.Add(id)) return false;
        return true;
    }

    private static bool SyncCatalog(GlobalSaveData value)
    {
        if (_document == null) return Fault();
        var campaigns = new List<IntPtr>(); var challenges = new List<IntPtr>();
        if (!Catalog(value, campaigns, challenges)) return Fault();
        if (!Remap(campaigns, CampaignRefs, _document.Campaigns)
            || !Remap(challenges, ChallengeRefs, _document.Challenges)) return Fault();
        if (!ValidGuids(_document.Campaigns, _document.Challenges)) return Fault();
        Reconcile(campaigns, challenges);
        return true;
    }

    // The native catalog is tracked by actual campaign objects during this process. Deletion or
    // insertion moves their private GUIDs with surviving objects; rights never follow a slot.
    private static bool Remap(List<IntPtr> now, List<IntPtr> before, List<string> ids)
    {
        if (before.Count != ids.Count) return false;
        if (now.SequenceEqual(before)) return true;
        var next = new List<string>(now.Count);
        foreach (IntPtr pointer in now)
        {
            int index = before.IndexOf(pointer);
            next.Add(index >= 0 ? ids[index] : Guid.NewGuid().ToString("N"));
        }
        if (!ValidGuids(next, new List<string>())) return false;
        ids.Clear(); ids.AddRange(next);
        before.Clear(); before.AddRange(now);
        CatalogGeneration++;
        _dirty = true;
        return true;
    }

    // Bumped whenever the live catalog objects were re-mapped onto their private GUIDs (a slot
    // moved or a save was adopted). Runtime caches keyed by object/owner pointers watch this to
    // invalidate stale owner resolutions without re-resolving the native context every frame.
    internal static int CatalogGeneration { get; private set; }

    // Responsibilities end only when their owning native object is provably gone from a catalog
    // read that succeeded: Remap has just kept every surviving pointer on its old GUID, so a
    // missing pointer (or a new GUID on a reused pointer) is a confirmed deletion. A transient
    // catalog read failure never reaches this method. A capture responsibility recorded while
    // the prefs were unreadable has no key yet: a healthy catalog resolves its exact identity
    // and keeps it; reading recovery alone never clears it.
    private static void Reconcile(List<IntPtr> campaigns, List<IntPtr> challenges)
    {
        if (Pending.Count != 0)
        {
            var dead = new List<Guid>();
            foreach (var pair in Pending)
                if (!Live(pair.Value, campaigns, challenges)) dead.Add(pair.Key);
            foreach (var receipt in dead) Pending.Remove(receipt);
        }
        for (int i = InvalidCaptures.Count - 1; i >= 0; i--)
        {
            var custody = InvalidCaptures[i];
            if (custody.Key == null)
            {
                var resolved = CustodyOfOwner(custody.Owner, custody.Land);
                if (resolved == null || resolved.Key == null) InvalidCaptures.RemoveAt(i);
                else InvalidCaptures[i] = resolved;
                continue;
            }
            if (!Live(custody, campaigns, challenges)) InvalidCaptures.RemoveAt(i);
        }
    }

    private static bool Live(Custody custody, List<IntPtr> campaigns, List<IntPtr> challenges)
    {
        if (custody == null || custody.Key == null) return false;
        var pointers = custody.Campaign ? campaigns : challenges;
        var ids = custody.Campaign ? _document.Campaigns : _document.Challenges;
        int index = pointers.IndexOf(custody.Owner);
        if (index < 0 || index >= ids.Count) return false;
        return ContextKeyOf(custody.Campaign, ids[index], custody.Land) == custody.Key;
    }

    private static string ContextKeyOf(bool campaign, string guid, int land)
        => _file == null || guid == null ? null
            : HeroRecruitmentArchive.Hash(_file + "\n" + (campaign ? "campaign" : "challenge")
                + "\n" + guid + "\n" + land, "hero-native-context");

    private static bool ResolveOwner(int campaign, int challenge, int land, out bool isCampaign, out string guid, out IntPtr owner)
    {
        isCampaign = false; guid = null; owner = IntPtr.Zero;
        if (land < 0 || _document == null || _global == null || _global.currentCampaign != campaign
            || _global.currentChallenge != challenge) return false;
        var current = _global.GetCurrentCampaign();
        if (current == null || current.Pointer == IntPtr.Zero || CampaignSaveData.current == null
            || current.Pointer != CampaignSaveData.current.Pointer) return false;
        int normal = CampaignRefs.IndexOf(current.Pointer);
        int special = ChallengeRefs.IndexOf(current.Pointer);
        if ((normal >= 0) == (special >= 0)) return false;
        isCampaign = normal >= 0;
        guid = isCampaign ? _document.Campaigns[normal] : _document.Challenges[special];
        owner = current.Pointer;
        return guid != null && !string.IsNullOrEmpty(guid);
    }

    private static string Context(int campaign, int challenge, int land)
        => ResolveOwner(campaign, challenge, land, out bool isCampaign, out string guid, out _)
            ? ContextKeyOf(isCampaign, guid, land) : null;

    private static Custody CustodyOf(int campaign, int challenge, int land)
        => ResolveOwner(campaign, challenge, land, out bool isCampaign, out string guid, out IntPtr owner)
            ? new Custody { Key = ContextKeyOf(isCampaign, guid, land), Land = land, Campaign = isCampaign, Owner = owner }
            : null;

    // The exact source identity of the currently loaded Global for one land: the Global object,
    // the native owner object (campaign or challenge) and the land. Null when none can be proven;
    // a numeric context key is a lookup alias and is never accepted as proof of ownership.
    internal static bool TrySourceIdentity(int land, out IntPtr global, out IntPtr owner)
    {
        global = IntPtr.Zero; owner = IntPtr.Zero;
        if (!Bind() || land < 0 || _global == null) return false;
        if (!ResolveOwner(_global.currentCampaign, _global.currentChallenge, land, out _, out _, out owner))
            return false;
        global = _global.Pointer;
        return global != IntPtr.Zero && owner != IntPtr.Zero;
    }

    // Same identity, keyed only by the frozen native owner pointer: used for capture
    // responsibilities that were recorded while the prefs could not be read, and when the
    // runtime current-campaign indices may already have moved.
    private static Custody CustodyOfOwner(IntPtr owner, int land)
    {
        if (land < 0 || owner == IntPtr.Zero || _document == null) return null;
        int normal = CampaignRefs.IndexOf(owner);
        int special = ChallengeRefs.IndexOf(owner);
        if ((normal >= 0) == (special >= 0)) return null;
        bool isCampaign = normal >= 0;
        string guid = isCampaign ? _document.Campaigns[normal] : _document.Challenges[special];
        return new Custody { Key = ContextKeyOf(isCampaign, guid, land), Land = land, Campaign = isCampaign, Owner = owner };
    }

    internal static bool Available => Bind();

    // A native context, once present, is the authority for this Global's island snapshots.
    // An older native rollback therefore cannot import a later sidecar purchase. A sidecar alias
    // only quarantines the legacy sidecar import: it never vetoes the context's own native rights.
    internal static bool Resolve(string sidecarContext, int campaign, int challenge, IslandSaveData island, string json,
        bool generationPending, out HeroRecruitmentContexts.Resolution result, out bool known,
        out bool baseline)
    {
        result = null; known = false; baseline = false;
        if (!Bind() || island == null) return false;
        string key = Context(campaign, challenge, island.land);
        if (key == null) return false;
        bool native = _archive.TryGetContext(key, out _);
        if (!native && _document.SidecarContexts.TryGetValue(sidecarContext, out string mapped) && mapped != key)
        {
            known = true;
            result = new HeroRecruitmentContexts.Resolution { Kind = "native-context-mismatch", Unresolved = true };
            return true;
        }
        known = native;
        if (!known) return true;
        result = HeroRecruitmentContexts.Resolve(_archive, key, island, json, generationPending);
        // A mismatched incoming snapshot must not silently re-pair this context's reservation
        // from its checkpoint here: doing so cleared Unresolved before MergeSessionRebind could
        // hand the re-entered source its own current paid set, which revived a settled death and
        // dropped a same-session second purchase. The unresolved reservation stays fail-closed
        // and is only settled by MergeSessionRebind (qualified session plus checkpoint proofs).
        baseline = result.Epoch != null && _archive.Baselines.ContainsKey(result.Epoch);
        return true;
    }

    // A source with an open capture-failure responsibility is re-entered with what this session
    // actually paid for: its own current receipts, with proven native ids supplied by the last
    // confirmed checkpoint. An absent paid row leaves the seat reserved while the responsibility
    // keeps refusing the save. Never a historical union. True means the caller may carry over the
    // bindings it had already observed for these receipts.
    //
    // sessionSeats is null unless the caller proved the previous runtime state belongs to this
    // exact source (same Global object, native owner and land). Only such a qualified session may
    // speak for the seat set, and there an exactly empty set is the authority (a verified death):
    // it must never be revived from an older checkpoint. An unqualified caller gets the checkpoint
    // safe path instead.
    internal static bool MergeSessionRebind(int campaign, int challenge, int land,
        IReadOnlyList<HeroPurchaseReceipt> sessionSeats, HeroRecruitmentContexts.Resolution result)
    {
        if (result == null || _document == null || _archive == null) return false;
        // issue-150 v3: foreign-epoch legitimately carries zero seats (this island adopted its
        // own epoch because every stored walk is another island's) yet must still rebind this
        // source's session purchases; unknown/unknown-mixed keep the unresolved-with-seats
        // shape. Restores, fresh and quarantine stay out (unchanged).
        if (result.Kind == "foreign-epoch")
        {
            // Seats are empty by construction; custody/checkpoint/session gates below decide.
        }
        else if (result.Kind == "unknown" || result.Kind == "unknown-mixed")
        {
            if (!result.Unresolved || result.Seats.Count == 0) return false;
        }
        else return false;
        var custody = CustodyOf(campaign, challenge, land);
        if (custody == null || custody.Key == null || !InvalidFor(custody)) return false;
        var checkpoint = CheckpointSeats(custody.Key);
        // Without a source-qualified session the last confirmed checkpoint is the only rebind
        // evidence. A qualified session needs none when it reports an exactly empty seat set.
        if (sessionSeats == null && checkpoint == null) return false;
        var seats = SessionSeatsWithCheckpoint(sessionSeats, checkpoint);
        if (seats == null) return false;
        result.Seats = seats;
        result.Unresolved = false;
        return true;
    }

    // A source-qualified session is the authority: its own paid receipts, with a native id the
    // confirmed checkpoint had already proven for the same receipt filled in. A session without
    // its own receipts keeps the checkpoint itself (the same source was re-entered before any new
    // purchase); a null session means no qualified source evidence at all.
    private static List<HeroPurchaseReceipt> SessionSeatsWithCheckpoint(
        IReadOnlyList<HeroPurchaseReceipt> session, List<HeroPurchaseReceipt> checkpoint)
    {
        if (session == null) return checkpoint;
        var merged = new List<HeroPurchaseReceipt>(session.Count);
        foreach (var seat in session)
        {
            var copy = seat.Copy();
            if (copy.NativeId.Length == 0 && checkpoint != null)
            {
                var proven = checkpoint.Find(x => x.Id == seat.Id && x.Side == seat.Side);
                if (proven != null && proven.NativeId.Length > 0) copy.NativeId = proven.NativeId;
            }
            merged.Add(copy);
        }
        return merged;
    }

    private static bool InvalidFor(Custody custody)
    {
        foreach (var entry in InvalidCaptures)
            if (entry.Land == custody.Land && entry.Owner == custody.Owner) return true;
        return false;
    }

    // The last confirmed pairing of this context's active epoch: its receipts keep their proven
    // native ids. Only a checkpoint with at least one such id can rebind a re-entered source.
    private static List<HeroPurchaseReceipt> CheckpointSeats(string key)
    {
        if (_archive == null || !_archive.TryGetContext(key, out var context)) return null;
        if (!_archive.Baselines.TryGetValue(context.Active, out string baseline)) return null;
        if (!_archive.Scopes.TryGetValue(context.Active, out var snapshots)) return null;
        var snapshot = snapshots.Find(x => x.Hash == baseline);
        if (snapshot == null || snapshot.Seats.Count == 0) return null;
        if (!snapshot.Seats.Any(x => x.NativeId.Length > 0)) return null;
        return snapshot.Seats.Select(x => x.Copy()).ToList();
    }

    internal static bool Track(Guid receipt)
    {
        if (receipt == Guid.Empty || !Bind() || Pending.ContainsKey(receipt) || Pending.Count >= 2) return false;
        var island = CampaignSaveData.current?.CurrentIsland;
        if (island == null) return false;
        var custody = CustodyOf(_global.currentCampaign, _global.currentChallenge, island.land);
        if (custody == null || custody.Key == null) return false;
        Pending.Add(receipt, custody);
        return true;
    }

    internal static void Abandon(Guid receipt) => Pending.Remove(receipt);

    // Called by the save capture when an island save that would re-pair this context's owned
    // rights did not complete. The responsibility is recorded from the capture's already frozen
    // and verified owner pointer: it must exist even when this very call cannot read the prefs,
    // and a later healthy read only resolves its exact identity (never clears it). The last
    // successful checkpoint stays the immutable record; the global save stays refused until a
    // complete capture of the same source re-verifies the pairing.
    internal static void NoteCaptureInvalid(IntPtr owner, int land)
    {
        try
        {
            if (owner == IntPtr.Zero || land < 0) return;
            Custody custody = null;
            if (Bind())
            {
                custody = CustodyOfOwner(owner, land);
                // A readable snapshot that has no owned rights for this source has nothing to
                // mis-pair; the un-staged purchase case is covered by the Pending gate.
                if (custody == null || custody.Key == null || !HasRights(custody.Key)) return;
            }
            if (custody == null) custody = new Custody { Key = null, Land = land, Owner = owner };
            foreach (var entry in InvalidCaptures)
                if (entry.Owner == owner && entry.Land == land) return;
            InvalidCaptures.Add(custody);
        }
        catch { }
    }

    private static bool HasRights(string key)
    {
        if (_archive == null || !_archive.TryGetContext(key, out var context)) return false;
        foreach (string epoch in context.Epochs)
            if (_archive.Scopes.TryGetValue(epoch, out var snapshots) && snapshots.Any(x => x.Seats.Count > 0))
                return true;
        return false;
    }

    internal static bool Stage(string sidecarContext, int campaign, int challenge, IslandSaveData island, string hash,
        string epoch, IReadOnlyList<HeroPurchaseReceipt> seats, bool newEpoch)
    {
        if (!Bind() || island == null || !HeroRecruitmentArchive.HashValid(sidecarContext)
            || !HeroRecruitmentArchive.HashValid(hash)
            || !HeroRecruitmentArchive.HashValid(epoch) || !HeroRecruitmentArchive.ValidSeats(seats)) return false;
        var custody = CustodyOf(campaign, challenge, island.land);
        if (custody == null || custody.Key == null) return false;
        string key = custody.Key;
        try
        {
            var copy = HeroRecruitmentArchive.Decode(_archive.Encode(), out bool unsupported);
            if (unsupported || copy == null
                || !copy.ConfirmBaseline(epoch, hash, seats, HeroRecruitmentFingerprint.Kind, false)
                || !copy.EnsureContext(key, epoch, newEpoch)) return false;
            var mappings = new Dictionary<string, string>(_document.SidecarContexts, StringComparer.Ordinal);
            // A context whose own native identity is already known stages independently of the
            // legacy numeric alias (that alias exists only to quarantine legacy sidecar imports
            // for unknown native contexts). Existing aliases — live or stale — are never
            // re-pointed, so the other survivors keep their recorded isolation.
            if (!_archive.TryGetContext(key, out _)
                && mappings.TryGetValue(sidecarContext, out string previous) && previous != key) return false;
            if (!mappings.ContainsKey(sidecarContext))
            {
                if (mappings.Count >= 256) return false;
                mappings[sidecarContext] = key;
            }
            if (!Write(copy, mappings)) return false;
            foreach (var seat in seats) Pending.Remove(seat.Id);
            InvalidCaptures.RemoveAll(x => x.Key == key || (x.Land == custody.Land && x.Owner == custody.Owner));
            return true;
        }
        catch { return false; }
    }

    private static bool Write(HeroRecruitmentArchive archive, Dictionary<string, string> mappings)
    {
        try
        {
            var doc = new Document { File = _file, Campaigns = new(_document.Campaigns),
                Challenges = new(_document.Challenges), SidecarContexts = new(mappings),
                Archive = Convert.ToBase64String(archive.Encode()) };
            string json = JsonSerializer.Serialize(doc);
            if (Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes) return false;
            _prefs.SetString(Key, json);
            if (!Read(_prefs, out string readback) || readback != json) return false;
            _document = doc; _archive = archive; _stored = json; _dirty = false;
            return true;
        }
        catch { return false; }
    }

    private static bool Prepare(GlobalSaveData value)
    {
        if (!Current(value) || !Bind() || Pending.Count != 0 || InvalidCaptures.Count != 0) return false;
        if (_stored == null && !_dirty) return true;
        return !_dirty || Write(_archive, _document.SidecarContexts);
    }

    internal static void BeforeCatalogMutation(GlobalSaveData value)
    {
        try { if (Current(value)) Bind(); } catch { _fault = true; }
    }

    [HarmonyPatch(typeof(GlobalSaveData), nameof(GlobalSaveData.SaveAsync))]
    private static class AsyncGate
    {
        // HarmonyX 2.10.2 runs every prefix and ANDs their results, so a second gate on the same
        // native method (the shared bank) may refuse after this one. The by-ref injected
        // __runOriginal flag marks an already-refused original: only the first refusing gate
        // completes the native failure callback, whichever order the two gates run in.
        [HarmonyPrefix] private static bool Prefix(GlobalSaveData __instance,
            Il2CppSystem.Action<SaveLoadResult> __0, ref bool __runOriginal)
        {
            bool first = __runOriginal;
            if (Prepare(__instance)) return true;
            if (first)
            {
                try { __0?.Invoke(SaveLoadResult.Save | SaveLoadResult.Failure); } catch { }
            }
            __runOriginal = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(GlobalSaveData._Save_d__89), nameof(GlobalSaveData._Save_d__89.MoveNext))]
    private static class SyncGate
    {
        [HarmonyPrefix] private static bool Prefix(GlobalSaveData._Save_d__89 __instance, ref bool __result)
        {
            if (__instance == null || __instance.__1__state != 0 || Prepare(__instance.__4__this)) return true;
            try
            {
                var boxed = __instance.@return;
                boxed.value = SaveLoadResult.Save | SaveLoadResult.Failure;
                __instance.@return = boxed;
            }
            catch { }
            __instance.__1__state = -1;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(GlobalSaveData), nameof(GlobalSaveData.CreateNewCampaign))]
    private static class CreateCampaignPatch
    { [HarmonyPrefix] private static void Prefix(GlobalSaveData __instance) => BeforeCatalogMutation(__instance); }

    [HarmonyPatch(typeof(GlobalSaveData), nameof(GlobalSaveData.TryDeleteCampaignAsync))]
    private static class DeleteCampaignPatch
    { [HarmonyPrefix] private static void Prefix(GlobalSaveData __instance) => BeforeCatalogMutation(__instance); }

    [HarmonyPatch(typeof(GlobalSaveData.__TryDeleteCampaign_d__91),
        nameof(GlobalSaveData.__TryDeleteCampaign_d__91.MoveNext))]
    private static class DeleteCampaignRoutinePatch
    {
        [HarmonyPrefix] private static void Prefix(GlobalSaveData.__TryDeleteCampaign_d__91 __instance)
        { if (__instance != null && __instance.__1__state == 0) BeforeCatalogMutation(GlobalSaveData._loaded); }
    }

    [HarmonyPatch(typeof(GlobalSaveData), nameof(GlobalSaveData.DeleteChallenge))]
    private static class DeleteChallengePatch
    { [HarmonyPrefix] private static void Prefix(GlobalSaveData __instance) => BeforeCatalogMutation(__instance); }

    [HarmonyPatch(typeof(GlobalSaveData.__TryDeleteChallenge_d__94),
        nameof(GlobalSaveData.__TryDeleteChallenge_d__94.MoveNext))]
    private static class DeleteChallengeRoutinePatch
    {
        [HarmonyPrefix] private static void Prefix(GlobalSaveData.__TryDeleteChallenge_d__94 __instance)
        { if (__instance != null && __instance.__1__state == 0) BeforeCatalogMutation(GlobalSaveData._loaded); }
    }
}
