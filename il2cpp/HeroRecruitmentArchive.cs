using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KingdomEnhancedMod;

// Independent purchase receipts. Native IDs are valid only inside one exact island snapshot.
// An empty NativeId reserves a purchased seat whose owner cannot currently be proven.
internal sealed class HeroPurchaseReceipt
{
    internal Guid Id;
    internal int Side;
    internal string NativeId = "";
    internal HeroPurchaseReceipt Copy(bool reserve = false) => new() { Id = Id, Side = Side, NativeId = reserve ? "" : NativeId };
}

internal sealed class HeroRecruitmentSnapshot
{
    internal string Hash;
    internal int HashKind = HeroRecruitmentArchive.HashKindLegacy; // 1 = legacy raw JSON hash, 2 = clock-independent fingerprint
    internal bool LegacyV1 = true;                                  // provenance: written by the v1 code path
    internal List<HeroPurchaseReceipt> Seats = new();
}

// Persistent island identity -> ordered epoch scopes (newest first, Active == Epochs[0]).
// Epoch scope ids are opaque: legacy ones embed runtime creation ticks, new ones are random.
// An epoch scope belongs to at most one context.
internal sealed class HeroRecruitmentContext
{
    internal string Active;
    internal readonly List<string> Epochs = new();
}

internal sealed class HeroRecruitmentArchive
{
    internal const int Version = 2;
    internal const int LegacyVersion = 1;
    internal const int HashKindLegacy = 1;
    internal const int MaxScopes = 128;
    internal const int MaxSnapshots = 8;
    internal const int MaxContexts = 64;
    internal const int MaxEpochs = 8;
    internal readonly Dictionary<string, List<HeroRecruitmentSnapshot>> Scopes = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, string> Baselines = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, HeroRecruitmentContext> Contexts = new(StringComparer.Ordinal);
    internal static bool HashValid(string value) => value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
    internal static string Hash(string text, string scope) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope + "\n" + text))).ToLowerInvariant();
    internal static bool HashKindValid(int kind) => kind == HashKindLegacy || kind == HeroRecruitmentFingerprint.Kind;

    // Stable identity of one island lineage: save file, campaign/challenge slots and land.
    // Runtime DateTime ticks and instance ids are never part of it: the game recreates
    // realStartDateTime on every load, which is exactly why the legacy tick scopes rot.
    internal static string ContextKey(string file, int campaign, int challenge, int land)
    {
        var builder = new StringBuilder(192);
        AppendField(builder, "file", file);
        AppendField(builder, "campaign", campaign.ToString(CultureInfo.InvariantCulture));
        AppendField(builder, "challenge", challenge.ToString(CultureInfo.InvariantCulture));
        AppendField(builder, "land", land.ToString(CultureInfo.InvariantCulture));
        return Hash(builder.ToString(), "hero-context");
    }

    // Opaque epoch scope for a confirmed new generation. Random, never derived from runtime state.
    internal static string NewScope() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private static void AppendField(StringBuilder builder, string name, string value)
    {
        if (value == null) value = string.Empty;
        builder.Append(name).Append('=').Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('\n');
    }

    internal bool TryGetContext(string key, out HeroRecruitmentContext context)
    {
        context = null;
        return key != null && Contexts.TryGetValue(key, out context) && context != null;
    }

    internal bool TryGet(string scope, string hash, out HeroRecruitmentSnapshot snapshot)
    {
        snapshot = null;
        if (!Scopes.TryGetValue(scope, out var list)) return false;
        snapshot = list.Find(x => x.Hash == hash);
        return snapshot != null;
    }

    internal bool ScopeHasKind(string scope, int kind) => Scopes.TryGetValue(scope, out var list) && list.Any(x => x.HashKind == kind);

    // A scope can be owned by exactly one context: migrating an epoch that already belongs to
    // another context would silently move paid history between islands.
    internal bool ScopeClaimedBy(string scope, string exceptContextKey)
    {
        foreach (var pair in Contexts)
            if (pair.Key != exceptContextKey && pair.Value.Epochs.Contains(scope)) return true;
        return false;
    }

    internal bool ScopeClaimed(string scope)
    {
        foreach (var pair in Contexts)
            if (pair.Value.Epochs.Contains(scope)) return true;
        return false;
    }

    // Register or refresh context -> epoch. allowNewEpoch is only true for a confirmed new
    // generation: paid epochs stay in history for rollback, and every failure is fail-closed.
    internal bool EnsureContext(string key, string scope, bool allowNewEpoch)
    {
        if (!HashValid(key) || !HashValid(scope) || !Scopes.ContainsKey(scope)) return false;
        if (ScopeClaimedBy(scope, key)) return false;
        if (!Contexts.TryGetValue(key, out var context))
        {
            if (Contexts.Count >= MaxContexts) return false;
            context = new HeroRecruitmentContext(); Contexts.Add(key, context);
            context.Epochs.Add(scope); context.Active = scope;
            return true;
        }
        if (context.Active == scope) return true;
        int index = context.Epochs.IndexOf(scope);
        if (index >= 0)
        {
            // Older epoch of the same context: roll back the active scope, keep the newer history.
            context.Epochs.RemoveAt(index); context.Epochs.Insert(0, scope); context.Active = scope;
            return true;
        }
        if (!allowNewEpoch) return false;
        // Capacity is fail-closed: dropping an old epoch silently could lose paid references.
        if (context.Epochs.Count >= MaxEpochs) return false;
        context.Epochs.Insert(0, scope); context.Active = scope;
        return true;
    }

    internal List<HeroPurchaseReceipt> LatestReservations(string scope)
    {
        // Exact old baseline restores exactly (including zero), so an uncommitted save cannot
        // charge or lock that rollback. A completely unknown snapshot is ambiguous: native save
        // may have committed and then vanilla may have resaved it. Reserve purchased seats from
        // the baseline and preparations newer than it; never resurrect history older than the
        // last confirmed baseline (e.g. a hero whose death was actually loaded).
        var result = new List<HeroPurchaseReceipt>();
        if (!Scopes.TryGetValue(scope, out var list)) return result;
        Baselines.TryGetValue(scope, out string baseline);
        foreach (var snapshot in list)
        {
            foreach (var seat in snapshot.Seats)
                if (!result.Any(x => x.Side == seat.Side)) result.Add(seat.Copy(true));
            if (snapshot.Hash == baseline) break;
        }
        return result;
    }

    internal bool ConfirmBaseline(string scope, string hash, IReadOnlyList<HeroPurchaseReceipt> seats, int hashKind, bool legacyV1)
    {
        if (!Record(scope, hash, seats, hashKind, legacyV1)) return false;
        Baselines[scope] = hash;
        return true;
    }

    internal static bool ValidSeats(IReadOnlyList<HeroPurchaseReceipt> seats)
    {
        if (seats == null || seats.Count > 2) return false;
        var ids = new HashSet<Guid>(); var sides = new HashSet<int>(); var native = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seat in seats)
        {
            if (seat == null || seat.Id == Guid.Empty || (seat.Side != -1 && seat.Side != 1)
                || seat.NativeId == null || seat.NativeId.Length > 256 || !ids.Add(seat.Id) || !sides.Add(seat.Side)) return false;
            if (seat.NativeId.Length > 0 && !native.Add(seat.NativeId)) return false;
        }
        return true;
    }

    // Same snapshot cannot describe two conflicting purchase histories. Preserve the first one.
    // A hash is bound to its hash function and provenance: the same hash with different flags is
    // a different snapshot and is refused instead of being silently rewritten.
    internal bool Record(string scope, string hash, IReadOnlyList<HeroPurchaseReceipt> seats, int hashKind, bool legacyV1)
    {
        if (!HashValid(scope) || !HashValid(hash) || !HashKindValid(hashKind) || !ValidSeats(seats)) return false;
        if (!Scopes.TryGetValue(scope, out var list))
        {
            if (Scopes.Count >= MaxScopes) return false;
            list = new(); Scopes.Add(scope, list);
        }
        var old = list.Find(x => x.Hash == hash);
        if (old != null)
        {
            if (old.HashKind != hashKind || old.LegacyV1 != legacyV1 || !Same(old.Seats, seats)) return false;
            list.Remove(old); list.Insert(0, old); return true;
        }
        list.Insert(0, new() { Hash = hash, HashKind = hashKind, LegacyV1 = legacyV1, Seats = seats.Select(x => x.Copy()).OrderBy(x => x.Side).ToList() });
        if (list.Count > MaxSnapshots)
        {
            Baselines.TryGetValue(scope, out string pinned);
            int evict = list.FindLastIndex(x => x.Hash != pinned);
            if (evict >= 0) list.RemoveAt(evict);
        }
        return true;
    }

    internal static bool Same(IReadOnlyList<HeroPurchaseReceipt> a, IReadOnlyList<HeroPurchaseReceipt> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var x in a)
            if (!b.Any(y => x.Id == y.Id && x.Side == y.Side && x.NativeId == y.NativeId)) return false;
        return true;
    }

    internal byte[] Encode()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteNumber("schemaVersion", Version); writer.WriteStartArray("scopes");
            foreach (var pair in Scopes.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject(); writer.WriteString("scope", pair.Key);
                writer.WriteString("baseline", Baselines.TryGetValue(pair.Key, out string baseline) ? baseline : "");
                writer.WriteStartArray("snapshots");
                foreach (var snapshot in pair.Value)
                {
                    writer.WriteStartObject(); writer.WriteString("hash", snapshot.Hash);
                    writer.WriteNumber("hashKind", snapshot.HashKind); writer.WriteBoolean("legacyV1", snapshot.LegacyV1);
                    writer.WriteStartArray("seats");
                    foreach (var seat in snapshot.Seats.OrderBy(x => x.Side))
                    {
                        writer.WriteStartObject(); writer.WriteString("id", seat.Id.ToString("N")); writer.WriteNumber("side", seat.Side);
                        writer.WriteString("nativeId", seat.NativeId); writer.WriteEndObject();
                    }
                    writer.WriteEndArray(); writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("contexts");
            foreach (var pair in Contexts.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject(); writer.WriteString("context", pair.Key); writer.WriteString("active", pair.Value.Active);
                writer.WriteStartArray("epochs");
                foreach (string epoch in pair.Value.Epochs) writer.WriteStringValue(epoch);
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    internal static HeroRecruitmentArchive Decode(byte[] bytes, out bool unsupported)
    {
        unsupported = false;
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count(x => x.Name == "schemaVersion") != 1)
            throw new FormatException("schema version");
        int version = root.GetProperty("schemaVersion").GetInt32();
        if (version != LegacyVersion && version != Version) { unsupported = true; return null; }
        // All duplicate/unknown fields are rejected, never silently discarded on rewrite.
        Fields(root, version == LegacyVersion ? new[] { "schemaVersion", "scopes" } : new[] { "schemaVersion", "scopes", "contexts" });
        var archive = new HeroRecruitmentArchive();
        var scopes = root.GetProperty("scopes");
        if (scopes.GetArrayLength() > MaxScopes) throw new FormatException("scope capacity");
        foreach (var node in scopes.EnumerateArray())
        {
            Fields(node, "scope", "baseline", "snapshots"); var key = node.GetProperty("scope").GetString();
            if (!HashValid(key) || archive.Scopes.ContainsKey(key)) throw new FormatException("scope");
            var list = new List<HeroRecruitmentSnapshot>(); archive.Scopes.Add(key, list);
            var snapshots = node.GetProperty("snapshots");
            if (snapshots.GetArrayLength() > MaxSnapshots) throw new FormatException("snapshot capacity");
            foreach (var data in snapshots.EnumerateArray())
            {
                Fields(data, version == LegacyVersion ? new[] { "hash", "seats" } : new[] { "hash", "hashKind", "legacyV1", "seats" });
                var hash = data.GetProperty("hash").GetString();
                if (!HashValid(hash) || list.Any(x => x.Hash == hash)) throw new FormatException("snapshot");
                int hashKind = version == LegacyVersion ? HashKindLegacy : data.GetProperty("hashKind").GetInt32();
                bool legacyV1 = version == LegacyVersion || data.GetProperty("legacyV1").GetBoolean();
                if (!HashKindValid(hashKind)) throw new FormatException("hash kind");
                var seats = new List<HeroPurchaseReceipt>();
                if (data.GetProperty("seats").GetArrayLength() > 2) throw new FormatException("seat capacity");
                foreach (var seat in data.GetProperty("seats").EnumerateArray())
                {
                    Fields(seat, "id", "side", "nativeId");
                    if (!Guid.TryParseExact(seat.GetProperty("id").GetString(), "N", out var id)) throw new FormatException("identity");
                    seats.Add(new() { Id = id, Side = seat.GetProperty("side").GetInt32(), NativeId = seat.GetProperty("nativeId").GetString() });
                }
                if (!ValidSeats(seats)) throw new FormatException("seats");
                list.Add(new() { Hash = hash, HashKind = hashKind, LegacyV1 = legacyV1, Seats = seats });
            }
            string baseline = node.GetProperty("baseline").GetString();
            if (baseline == null || (baseline.Length > 0 && (!HashValid(baseline) || !list.Any(x => x.Hash == baseline)))) throw new FormatException("baseline");
            if (baseline.Length > 0) archive.Baselines.Add(key, baseline);
        }
        if (version == LegacyVersion) return archive;
        var contexts = root.GetProperty("contexts");
        if (contexts.GetArrayLength() > MaxContexts) throw new FormatException("context capacity");
        foreach (var node in contexts.EnumerateArray())
        {
            Fields(node, "context", "active", "epochs");
            string key = node.GetProperty("context").GetString();
            if (!HashValid(key) || archive.Contexts.ContainsKey(key)) throw new FormatException("context");
            var context = new HeroRecruitmentContext();
            var epochs = node.GetProperty("epochs");
            if (epochs.GetArrayLength() < 1 || epochs.GetArrayLength() > MaxEpochs) throw new FormatException("epoch capacity");
            foreach (var value in epochs.EnumerateArray())
            {
                string epoch = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                if (!HashValid(epoch) || context.Epochs.Contains(epoch) || !archive.Scopes.ContainsKey(epoch)) throw new FormatException("epoch");
                context.Epochs.Add(epoch);
            }
            context.Active = node.GetProperty("active").GetString();
            // Active is always the most recent epoch; a file violating that is corrupt, not migrated.
            if (context.Active != context.Epochs[0]) throw new FormatException("active epoch");
            archive.Contexts.Add(key, context);
        }
        // An epoch scope may belong to exactly one context.
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var context in archive.Contexts.Values)
            foreach (string epoch in context.Epochs)
                if (!owners.Add(epoch)) throw new FormatException("shared epoch");
        return archive;
    }

    private static void Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new FormatException("object required");
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in value.EnumerateObject())
            if (!names.Contains(p.Name) || !found.Add(p.Name)) throw new FormatException("unknown/duplicate field");
        if (found.Count != names.Length) throw new FormatException("missing field");
    }
}

internal static class HeroRecruitmentArchiveStore
{
    internal const int MaxBytes = 1024 * 1024;
    internal enum State { Missing, Valid, Corrupt, Unsupported, IoError }
    internal sealed class ReadResult
    {
        internal State Status;
        internal HeroRecruitmentArchive Archive;
        internal byte[] Original;
        internal bool RecoveredBackup;
        internal bool Writable => (Status == State.Missing || Status == State.Valid) && !RecoveredBackup;
    }

    internal static ReadResult Load(string path)
    {
        var result = ReadOne(path);
        // Unknown future schema must not be replaced by an older backup.
        if (result.Status != State.Corrupt && result.Status != State.Missing) return result;
        var backup = ReadOne(path + ".bak");
        if (backup.Status == State.Valid)
        {
            backup.RecoveredBackup = true; backup.Original = result.Original;
            return backup; // readable, deliberately read-only until explicit recovery
        }
        if (result.Status == State.Missing && backup.Status != State.Missing)
            return new() { Status = backup.Status == State.Unsupported ? State.Unsupported : State.Corrupt };
        return result;
    }

    private static ReadResult ReadOne(string path)
    {
        try
        {
            var size = new FileInfo(path).Length;
            if (size < 2 || size > MaxBytes) return new() { Status = State.Corrupt };
            var bytes = File.ReadAllBytes(path);
            try
            {
                var archive = HeroRecruitmentArchive.Decode(bytes, out bool unsupported);
                return new() { Status = unsupported ? State.Unsupported : State.Valid, Archive = archive, Original = bytes };
            }
            catch { return new() { Status = State.Corrupt, Original = bytes }; }
        }
        catch (FileNotFoundException) { return new() { Status = State.Missing, Archive = new() }; }
        catch (DirectoryNotFoundException) { return new() { Status = State.Missing, Archive = new() }; }
        catch { return new() { Status = State.IoError }; }
    }

    internal static bool Save(string path, ReadResult expected, HeroRecruitmentArchive archive)
    {
        string temporary = null;
        try
        {
            if (expected == null || !expected.Writable || archive == null) return false;
            var current = Load(path);
            if (!current.Writable || current.Status != expected.Status || !Equal(current.Original, expected.Original)) return false;
            byte[] bytes = archive.Encode();
            if (bytes.Length > MaxBytes) return false;
            HeroRecruitmentArchive.Decode(bytes, out bool unsupported);
            if (unsupported) return false;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
            if (current.Status == State.Missing) File.Move(temporary, path);
            else File.Replace(temporary, path, path + ".bak", true);
            temporary = null;
            return Equal(File.ReadAllBytes(path), bytes);
        }
        catch { return false; }
        finally { if (temporary != null) { try { File.Delete(temporary); } catch { } } }
    }

    private static bool Equal(byte[] a, byte[] b) => a == null ? b == null : b != null && a.AsSpan().SequenceEqual(b);
}
