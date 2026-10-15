using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

#if !KEM_HEAVY_SHIELD_SNAPSHOT_PURE_TESTS
using System.Reflection;
using HarmonyLib;
#endif

namespace KingdomEnhancedMod;

// Issue 198 loader-side source bridge (bounded slice). Two pure pieces plus one native adapter:
//
// 1. HeavyShieldSnapshotFingerprint: the clock-independent island fingerprint used by the
//    schema v3 per-island hashKind. Same principle and same writer pattern as the
//    verified MusketeerArchive.IslandHash / HeroRecruitmentFingerprint, but a distinct
//    domain prefix and material layout, so no v2 string can ever collide with v3.
//
// 2. HeavyShieldLegacyLoadProof: extracts bounded legacy-witness rows from one raw native
//    global JSON string (the exact bytes the native loader feeds to
//    JsonUtility.FromJsonOverwrite). Island bytes are taken with JsonElement.GetRawText()
//    from the original parse; the stored legacy hash must reproduce byte-exactly. No
//    candidate enumeration, no guess: a row that cannot be reproduced is simply not proven.
//
// 3. HeavyShieldNativeLoadProof: the capture adapter around
//    UnityEngine.JsonUtility.FromJsonOverwrite(string, Il2CppSystem.Object). It never
//    mutates native state, never changes arguments, never blocks the call and returns the
//    original exception unchanged. It only seals a single bounded pending candidate; a
//    proof is published exclusively by a later exact TryMatch where the captured target is
//    the installed GlobalSaveData._loaded and the payload and every hash context match.
//    Native hook safety (address folding / real patching) is a separate operator acceptance
//    gate; this file only compiles the patch.

internal static class HeavyShieldSnapshotFingerprint
{
    // Aligned with the existing HeroRecruitmentArchive/MusketeerArchive numbering:
    // 1 = legacy raw-bytes hash, 2 = clock-independent canonical fingerprint.
    internal const int LegacyKind = 1;
    internal const int CanonicalKind = 2;

    private const string Domain = "heavy-shield-island-objects-v3";

    // Canonical input contract: guid must be canonical "D", challenge/land >= 0, the island
    // JSON must be an object without duplicate top-level properties. Everything invalid
    // throws FormatException; callers already treat that as fail-closed.
    internal static string Hash(string guid, int challenge, int land, string rawIslandJson)
    {
        if (guid == null || !Guid.TryParseExact(guid, "D", out var parsed)
            || guid != parsed.ToString("D"))
            throw new FormatException("guid");
        if (challenge < 0) throw new FormatException("challenge");
        if (land < 0) throw new FormatException("land");
        if (string.IsNullOrEmpty(rawIslandJson)) throw new FormatException("island");
        JsonDocument document;
        try { document = JsonDocument.Parse(rawIslandJson); }
        catch (JsonException) { throw new FormatException("island json"); }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new FormatException("island object required");
            using var stream = new MemoryStream();
            var names = new HashSet<string>(StringComparer.Ordinal);
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new FormatException("duplicate island property");
                    // Exactly the three island play-time clocks may still advance between the
                    // island snapshot and the global write; everything else participates.
                    if (property.Name is "playTimeDays" or "lastPlayedTimeDays" or "_islandTimePlayed") continue;
                    property.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.UTF8.GetBytes(Domain + "\n" + guid + "\n"
                + challenge.ToString(CultureInfo.InvariantCulture) + "\n"
                + land.ToString(CultureInfo.InvariantCulture) + "\n"));
            CanonicalHashBuffer.AppendTo(hash, stream);
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
    }
}

internal sealed class HeavyShieldLegacyLoadProof
{
    internal readonly struct RowKey : IEquatable<RowKey>
    {
        internal readonly int Slot;
        internal readonly string Guid;
        internal readonly int Challenge;
        internal readonly int Land;

        internal RowKey(int slot, string guid, int challenge, int land)
        { Slot = slot; Guid = guid; Challenge = challenge; Land = land; }

        public bool Equals(RowKey other)
            => Slot == other.Slot && Challenge == other.Challenge && Land == other.Land
               && string.Equals(Guid, other.Guid, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is RowKey other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Slot, Guid == null ? 0 : StringComparer.Ordinal.GetHashCode(Guid),
                Challenge, Land);
    }

    internal sealed class Row
    {
        internal string OldHash;
        internal string NewCanonicalHash;
    }

    // Bounded extractor: hard cap on the raw global input and on the produced rows; the
    // schema caps (32 campaigns x 128 islands) bound the scan itself.
    internal const int MaxGlobalBytes = 64 * 1024 * 1024;
    internal const int MaxProofRows = HeavyShieldSaveSchema.MaxCampaigns * HeavyShieldSaveSchema.MaxIslands;

    private readonly Dictionary<RowKey, Row> _rows;

    // The exact native-Prefs payload string (the HeavyShieldSaveSchema.Key entry val) this
    // proof was extracted from. Matches requires byte-exact equality, so a proof can never
    // be adopted for a different payload revision.
    internal string OriginalKey { get; }

    private HeavyShieldLegacyLoadProof(string originalKey, Dictionary<RowKey, Row> rows)
    {
        OriginalKey = originalKey;
        _rows = rows;
    }

    internal static bool TryCreate(string rawGlobalJson, out HeavyShieldLegacyLoadProof proof, out string reason)
    {
        proof = null;
        if (!TryExtract(rawGlobalJson, out string originalKey, out var rows, out reason)) return false;
        proof = new HeavyShieldLegacyLoadProof(originalKey, rows);
        return true;
    }

    // Exact-context match. Every field is required; anything missing, null or different
    // returns false. The caller supplies the legacy hash recorded in the saved row and the
    // canonical fingerprint of the island JSON it currently holds; both must be exactly the
    // ones this proof reproduced from the raw source payload.
    internal bool Matches(string originalKey, int slot, string guid, int challenge, int land,
        string storedLegacyHash, string currentCanonicalHash)
    {
        if (originalKey == null || !string.Equals(originalKey, OriginalKey, StringComparison.Ordinal))
            return false;
        if (guid == null || storedLegacyHash == null || currentCanonicalHash == null) return false;
        if (!_rows.TryGetValue(new RowKey(slot, guid, challenge, land), out var row)) return false;
        return string.Equals(row.OldHash, storedLegacyHash, StringComparison.Ordinal)
            && string.Equals(row.NewCanonicalHash, currentCanonicalHash, StringComparison.Ordinal);
    }

    // Strict bounded extraction from one raw native global JSON string:
    // * the HeavyShield prefs entry must exist exactly once and its val must parse with the
    //   real HeavyShieldSaveCodec (no second parser with different rules);
    // * saved campaign Slot maps to the native campaigns array position; a saved row without
    //   a native counterpart is skipped, never guessed;
    // * a saved island row is proven only when the native _islands array actually holds a
    //   non-null object at position == land whose own land == land and whose raw bytes
    //   (JsonElement.GetRawText of the original parse) reproduce the stored legacy hash with
    //   HeavyShieldSaveCodec.SnapshotHash; island bytes are never re-serialized.
    internal static bool TryExtract(string rawGlobalJson, out string originalKey,
        out Dictionary<RowKey, Row> rows, out string reason)
    {
        originalKey = null;
        rows = null;
        reason = null;
        if (rawGlobalJson == null) { reason = "null"; return false; }
        if (Encoding.UTF8.GetByteCount(rawGlobalJson) > MaxGlobalBytes) { reason = "size"; return false; }
        JsonDocument global;
        try { global = JsonDocument.Parse(rawGlobalJson); }
        catch (JsonException) { reason = "json"; return false; }
        using (global)
        {
            var root = global.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { reason = "root"; return false; }
            bool hasPrefs = false, hasCampaigns = false;
            JsonElement prefs = default, campaigns = default;
            var rootNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!rootNames.Add(property.Name)) { reason = "duplicate"; return false; }
                if (property.Name == "prefs") { prefs = property.Value; hasPrefs = true; }
                else if (property.Name == "campaigns") { campaigns = property.Value; hasCampaigns = true; }
            }
            if (!hasPrefs) { reason = "prefs"; return false; }
            if (!hasCampaigns) { reason = "campaigns"; return false; }
            if (prefs.ValueKind != JsonValueKind.Object) { reason = "prefs"; return false; }
            if (campaigns.ValueKind != JsonValueKind.Array) { reason = "campaigns"; return false; }

            bool hasEntries = false;
            JsonElement entries = default;
            var prefsNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in prefs.EnumerateObject())
            {
                if (!prefsNames.Add(property.Name)) { reason = "duplicate"; return false; }
                if (property.Name == "srzEntries") { entries = property.Value; hasEntries = true; }
            }
            if (!hasEntries) { reason = "entries"; return false; }
            if (entries.ValueKind != JsonValueKind.Array) { reason = "entries"; return false; }

            // The raw source payload is our own previous writing; duplicates would make the
            // source ambiguous, so they fail the whole extraction instead of picking a winner.
            string payload = null;
            int payloadCount = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) { reason = "entry"; return false; }
                string key = null, val = null;
                var entryNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in entry.EnumerateObject())
                {
                    if (!entryNames.Add(property.Name)) { reason = "entry"; return false; }
                    if (property.Name == "key")
                    {
                        if (property.Value.ValueKind != JsonValueKind.String) { reason = "entry"; return false; }
                        key = property.Value.GetString();
                    }
                    else if (property.Name == "val")
                    {
                        if (property.Value.ValueKind != JsonValueKind.String) { reason = "entry"; return false; }
                        val = property.Value.GetString();
                    }
                }
                if (key == null || val == null) { reason = "entry"; return false; }
                if (key != HeavyShieldSaveSchema.Key) continue;
                payloadCount++;
                if (payloadCount > 1) { reason = "native-key"; return false; }
                payload = val;
            }
            if (payloadCount == 0) { reason = "native-key-missing"; return false; }

            if (!TryScanPayloadStructure(payload, out reason)) return false;
            if (!HeavyShieldSaveCodec.TryParse(payload, out var document, out _))
            { reason = "payload"; return false; }

            int campaignCount = campaigns.GetArrayLength();
            if (campaignCount > HeavyShieldSaveSchema.MaxCampaigns) { reason = "capacity"; return false; }
            var natives = new List<JsonElement>(campaignCount);
            foreach (var element in campaigns.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) { reason = "campaign"; return false; }
                var nativeNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                    if (!nativeNames.Add(property.Name)) { reason = "duplicate"; return false; }
                natives.Add(element);
            }

            var result = new Dictionary<RowKey, Row>();
            foreach (var savedCampaign in document.Campaigns)
            {
                if (savedCampaign.Slot >= natives.Count) continue;
                var native = natives[savedCampaign.Slot];
                if (!native.TryGetProperty("_islands", out var islands)) continue;
                if (islands.ValueKind != JsonValueKind.Array) { reason = "islands"; return false; }
                int islandCount = islands.GetArrayLength();
                if (islandCount > HeavyShieldSaveSchema.MaxIslands) { reason = "capacity"; return false; }
                foreach (var savedIsland in savedCampaign.Islands)
                {
                    // Only rows whose digest is explicitly (v3) or implicitly (v1/v2)
                    // the legacy raw-bytes hash can witness a legacy source; a row that
                    // already carries the canonical fingerprint is never relabelled.
                    if (savedIsland.HashKind != HeavyShieldSnapshotFingerprint.LegacyKind) continue;
                    int land = savedIsland.Land;
                    if (land >= islandCount) continue;
                    var island = islands[land];
                    if (island.ValueKind != JsonValueKind.Object) continue;
                    if (!TryReadLand(island, out int nativeLand) || nativeLand != land) continue;
                    string raw = island.GetRawText();
                    string legacy = HeavyShieldSaveCodec.SnapshotHash(
                        savedCampaign.Guid, savedIsland.Challenge, land, raw);
                    if (legacy == null
                        || !string.Equals(legacy, savedIsland.SnapshotHash, StringComparison.Ordinal))
                        continue;
                    string canonical;
                    try
                    {
                        canonical = HeavyShieldSnapshotFingerprint.Hash(
                            savedCampaign.Guid, savedIsland.Challenge, land, raw);
                    }
                    catch (FormatException) { continue; }
                    if (result.Count >= MaxProofRows) { reason = "capacity"; return false; }
                    result[new RowKey(savedCampaign.Slot, savedCampaign.Guid,
                        savedIsland.Challenge, land)] = new Row
                    {
                        OldHash = legacy,
                        NewCanonicalHash = canonical,
                    };
                }
            }
            if (result.Count == 0) { reason = "none"; return false; }
            originalKey = payload;
            rows = result;
            return true;
        }
    }

    private static bool TryReadLand(JsonElement island, out int land)
    {
        land = -1;
        bool found = false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in island.EnumerateObject())
        {
            if (!names.Add(property.Name)) return false;
            if (property.Name != "land") continue;
            if (property.Value.ValueKind != JsonValueKind.Number
                || !property.Value.TryGetInt32(out land)) return false;
            found = true;
        }
        return found && land >= 0;
    }

    // Rejects duplicate top-level/campaign/island property names: slot/guid/challenge/
    // land/hashKind integrity is what the extractor relies on. The per-island hashKind
    // itself is validated by the real codec during TryParse.
    private static bool TryScanPayloadStructure(string payload, out string reason)
    {
        reason = null;
        if (payload == null) { reason = "payload"; return false; }
        JsonDocument parsed;
        try { parsed = JsonDocument.Parse(payload); }
        catch (JsonException) { reason = "payload"; return false; }
        using (parsed)
        {
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { reason = "payload"; return false; }
            var rootNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!rootNames.Add(property.Name)) { reason = "payload"; return false; }
            if (!root.TryGetProperty("campaigns", out var campaigns)) return true;
            if (campaigns.ValueKind != JsonValueKind.Array) { reason = "payload"; return false; }
            foreach (var campaign in campaigns.EnumerateArray())
            {
                if (campaign.ValueKind != JsonValueKind.Object) { reason = "payload"; return false; }
                var campaignNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in campaign.EnumerateObject())
                    if (!campaignNames.Add(property.Name)) { reason = "payload"; return false; }
                if (!campaign.TryGetProperty("islands", out var islands)) continue;
                if (islands.ValueKind != JsonValueKind.Array) { reason = "payload"; return false; }
                foreach (var island in islands.EnumerateArray())
                {
                    if (island.ValueKind != JsonValueKind.Object) { reason = "payload"; return false; }
                    var islandNames = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in island.EnumerateObject())
                        if (!islandNames.Add(property.Name)) { reason = "payload"; return false; }
                }
            }
            return true;
        }
    }
}

#if !KEM_HEAVY_SHIELD_SNAPSHOT_PURE_TESTS
// Native adapter. Holds exactly one bounded pending candidate; the proof is published only
// by TryMatch, never by the capture itself. It never installs a Global, restores Prefs,
// writes a native key or touches disk/streams, and it never suppresses or rewrites the
// patched call. The exact-target retention here keeps the captured native object rooted so
// its address cannot be reused by a different Global while the candidate lives.
internal static class HeavyShieldNativeLoadProof
{
    internal sealed class Capture
    {
        internal GlobalSaveData Target;
        internal IntPtr Pointer;
        internal Capture Previous;
        internal long Epoch;
        internal bool Conflict;
        internal bool Closed;
    }

    private sealed class Candidate
    {
        internal IntPtr Pointer;
        internal GlobalSaveData Target;
        internal string OriginalKey;
        internal Dictionary<HeavyShieldLegacyLoadProof.RowKey, HeavyShieldLegacyLoadProof.Row> Rows;
    }

    private static readonly object Gate = new object();
    private static Capture _open;
    private static Candidate _pending;
    private static int _depth;
    private static long _epoch;

    // Harmony prefix entry. Only a native GlobalSaveData target opens a capture scope; every
    // other FromJsonOverwrite caller costs one type check and zero parsing. Never throws.
    internal static Capture BeginCapture(GlobalSaveData target)
    {
        try
        {
            if (target == null) return null;
            lock (Gate)
            {
                // A new Global deserialize replaces the loaded source object; the previous
                // single candidate must not survive it.
                _pending = null;
                var scope = new Capture
                {
                    Target = target,
                    Pointer = target.Pointer,
                    Previous = _open,
                    Epoch = ++_epoch,
                };
                _depth++;
                if (_depth > 1)
                {
                    // Nested (same target or another Global): every open scope fails closed.
                    scope.Conflict = true;
                    _epoch++;
                }
                _open = scope;
                return scope;
            }
        }
        catch { return null; }
    }

    // Harmony finalizer entry. Seals at most one bounded pending candidate on a normal
    // return; any earlier failure, nesting or conflict already cleared the candidate.
    internal static void EndCapture(Capture scope, string json, bool normalReturn)
    {
        if (scope == null) return;
        try
        {
            lock (Gate)
            {
                if (scope.Closed) return;
                bool top = ReferenceEquals(_open, scope);
                scope.Closed = true;
                _depth--;
                if (top) _open = scope.Previous;
                while (_open != null && _open.Closed) _open = _open.Previous;
                if (!top) { _pending = null; _epoch++; return; }
                if (!normalReturn || scope.Conflict || scope.Epoch != _epoch) return;
                if (string.IsNullOrEmpty(json)) return;
                if (!HeavyShieldLegacyLoadProof.TryExtract(json, out string originalKey,
                        out var rows, out _))
                    return;
                _pending = new Candidate
                {
                    Pointer = scope.Pointer,
                    Target = scope.Target,
                    OriginalKey = originalKey,
                    Rows = rows,
                };
            }
        }
        catch { }
    }

    // The only publication point. Requires the captured target to be the installed loaded
    // global, the exact payload string and the exact per-context legacy/canonical hashes.
    // Never mutates anything; false on every doubt.
    internal static bool TryMatch(GlobalSaveData exactGlobal, string originalKey, int slot,
        string guid, int challenge, int land, string storedLegacyHash, string currentCanonicalHash)
    {
        try
        {
            if (exactGlobal == null || originalKey == null || guid == null
                || storedLegacyHash == null || currentCanonicalHash == null) return false;
            lock (Gate)
            {
                var candidate = _pending;
                if (candidate == null || candidate.Pointer == IntPtr.Zero
                    || candidate.Target == null || candidate.Target.Pointer != candidate.Pointer
                    || candidate.Pointer != exactGlobal.Pointer) return false;
                // Read-only static: the captured pointer must be the object this load
                // installed; a later Global deserialize has already replaced the candidate.
                var loaded = GlobalSaveData._loaded;
                if (loaded == null || loaded.Pointer != exactGlobal.Pointer) return false;
                if (!string.Equals(candidate.OriginalKey, originalKey, StringComparison.Ordinal))
                    return false;
                if (!candidate.Rows.TryGetValue(
                        new HeavyShieldLegacyLoadProof.RowKey(slot, guid, challenge, land), out var row))
                    return false;
                if (!string.Equals(row.OldHash, storedLegacyHash, StringComparison.Ordinal)) return false;
                if (!string.Equals(row.NewCanonicalHash, currentCanonicalHash, StringComparison.Ordinal))
                    return false;
                return true;
            }
        }
        catch { return false; }
    }

    // Exact overload: UnityEngine.JsonUtility.FromJsonOverwrite(string, Il2CppSystem.Object).
#if !KEM_HEAVY_SHIELD_SKIP_NATIVE_SOURCE
    [HarmonyPatch]
#endif
    internal static class SourceCapturePatch
    {
        private static MethodBase TargetMethod()
            => AccessTools.Method(typeof(UnityEngine.JsonUtility), "FromJsonOverwrite",
                new[] { typeof(string), typeof(Il2CppSystem.Object) });

        // Void prefix: arguments are observed only, never rewritten; a managed failure here
        // must not escape into the native load path.
        private static void Prefix(string __0, Il2CppSystem.Object __1, out Capture __state)
        {
            __state = null;
            try
            {
                var global = __1 == null ? null : __1.TryCast<GlobalSaveData>();
                if (global == null) return;
                __state = HeavyShieldNativeLoadProof.BeginCapture(global);
            }
            catch { __state = null; }
        }

        private static Exception Finalizer(string __0, Exception __exception, Capture __state)
        {
            HeavyShieldNativeLoadProof.EndCapture(__state, __0, __exception == null);
            return __exception;
        }
    }
}
#endif
