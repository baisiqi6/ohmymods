using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KingdomEnhancedMod;

// One paid musketeer career. A career owns exactly one exchangeable artifact at a time: the
// native ToolBow row (KindGun) or the native Archer row (KindUnit); it transfers
// gun -> unit -> dropped gun and never splits. NativeId is a native row uniqueID that was
// witnessed by a live capture inside the exact island snapshot that stores it; a reservation
// (no proven owner) is serialized with an empty NativeId and never guesses one back.
// StockSlot is rack metadata, not identity: 0..MaxStockSlots-1 marks a gun that still sits on
// the paid shop rack (restorable as kinematic on load), -1 means ordinary dropped gun or unit.
internal sealed class MusketeerCareer
{
    internal const int KindUnit = 1;
    internal const int KindGun = 2;
    internal const int NoStockSlot = -1;
    internal Guid Id;
    internal int Kind;
    internal string NativeId = "";
    internal int StockSlot = NoStockSlot;
    internal MusketeerCareer Copy(bool reserve = false) => new()
    {
        Id = Id,
        Kind = Kind,
        NativeId = reserve ? "" : NativeId,
        StockSlot = reserve ? NoStockSlot : StockSlot
    };
}

internal sealed class MusketeerSnapshot
{
    internal string Hash;
    internal int HashKind = MusketeerArchive.HashKindFingerprint;
    internal List<MusketeerCareer> Records = new();
}

// Persistent island identity -> ordered epoch scopes (newest first, Active == Epochs[0]).
// Epoch scopes are opaque random ids; a scope belongs to at most one context.
internal sealed class MusketeerContext
{
    internal string Active;
    internal readonly List<string> Epochs = new();
}

// Island sidecar for paid musketeer careers. Deliberately not the hero archive: ordinary
// units/guns have no two-seat quota, so records are keyed by career GUID alone, a snapshot
// holds up to MaxRecords rows, and nothing is ever assigned by ordinal position.
internal sealed class MusketeerArchive
{
    internal const int Version = 2;
    internal const int HashKindFingerprint = 2;
    internal const int MaxRecords = 4096;   // per island state: actors + guns, no eviction
    internal const int MaxStockSlots = 3;   // paid shop rack slots (0..2); -1 = no rack claim
    internal const int MaxScopes = 128;
    internal const int MaxSnapshots = 8;
    internal const int MaxContexts = 64;
    internal const int MaxEpochs = 8;
    internal const int MaxNativeIdLength = 256;
    internal readonly Dictionary<string, List<MusketeerSnapshot>> Scopes = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, string> Baselines = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, MusketeerContext> Contexts = new(StringComparer.Ordinal);

    internal static bool HashValid(string value) => value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
    internal static string Hash(string text, string scope) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope + "\n" + text))).ToLowerInvariant();
    internal static bool HashKindValid(int kind) => kind == HashKindFingerprint;

    // Clock-independent island fingerprint (same v2 principle as the hero archive): the native
    // save freezes the object records while the three island play-time clocks may still advance
    // before the containing global save is written, so exactly those top-level fields are
    // excluded. Everything else, rows included, participates in the hash.
    internal static string IslandHash(string json, string scope)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("island object required");
        using var stream = new MemoryStream();
        var names = new HashSet<string>(StringComparer.Ordinal);
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new FormatException("duplicate island property");
                if (property.Name is "playTimeDays" or "lastPlayedTimeDays" or "_islandTimePlayed") continue;
                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("musketeer-island-objects-v2\n" + scope + "\n"));
        hash.AppendData(stream.ToArray());
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    // Stable identity of one island lineage: save file, campaign/challenge slots and land.
    // Runtime DateTime ticks and instance ids are never part of it: the game recreates
    // realStartDateTime on every load, which is exactly why runtime-tick scopes rot.
    internal static string ContextKey(string file, int campaign, int challenge, int land)
    {
        var builder = new StringBuilder(192);
        AppendField(builder, "file", file);
        AppendField(builder, "campaign", campaign.ToString(CultureInfo.InvariantCulture));
        AppendField(builder, "challenge", challenge.ToString(CultureInfo.InvariantCulture));
        AppendField(builder, "land", land.ToString(CultureInfo.InvariantCulture));
        return Hash(builder.ToString(), "musketeer-context");
    }

    // Opaque epoch scope for a confirmed generation / first record. Random, never derived
    // from runtime state.
    internal static string NewScope() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private static void AppendField(StringBuilder builder, string name, string value)
    {
        if (value == null) value = string.Empty;
        builder.Append(name).Append('=').Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('\n');
    }

    internal bool TryGetContext(string key, out MusketeerContext context)
    {
        context = null;
        return key != null && Contexts.TryGetValue(key, out context) && context != null;
    }

    internal bool TryGet(string scope, string hash, out MusketeerSnapshot snapshot)
    {
        snapshot = null;
        if (!Scopes.TryGetValue(scope, out var list)) return false;
        snapshot = list.Find(x => x.Hash == hash);
        return snapshot != null;
    }

    // A scope can be owned by exactly one context: moving an epoch across islands would move
    // paid careers with it.
    internal bool ScopeClaimedBy(string scope, string exceptContextKey)
    {
        foreach (var pair in Contexts)
            if (pair.Key != exceptContextKey && pair.Value.Epochs.Contains(scope)) return true;
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
            context = new MusketeerContext(); Contexts.Add(key, context);
            context.Epochs.Add(scope); context.Active = scope;
            return true;
        }
        if (context.Active == scope) return true;
        int index = context.Epochs.IndexOf(scope);
        if (index >= 0)
        {
            // Older epoch of the same context: roll the active scope back, keep newer history.
            context.Epochs.RemoveAt(index); context.Epochs.Insert(0, scope); context.Active = scope;
            return true;
        }
        if (!allowNewEpoch) return false;
        // Capacity is fail-closed: dropping an old epoch silently would lose paid references.
        if (context.Epochs.Count >= MaxEpochs) return false;
        context.Epochs.Insert(0, scope); context.Active = scope;
        return true;
    }

    // Records of the active epoch newer than (or equal to) its confirmed baseline, kept as
    // reservations: the unknown-snapshot case. Native may have saved and then vanilla resaved,
    // so the last witnessed history is preserved (owner unproven, no native id) instead of
    // being written off or bound to whatever row happens to carry a recycled id.
    internal List<MusketeerCareer> LatestReservations(string scope)
    {
        var result = new List<MusketeerCareer>();
        if (!Scopes.TryGetValue(scope, out var list)) return result;
        Baselines.TryGetValue(scope, out string baseline);
        foreach (var snapshot in list)
        {
            foreach (var record in snapshot.Records)
                if (!result.Any(x => x.Id == record.Id)) result.Add(record.Copy(true));
            if (snapshot.Hash == baseline) break;
        }
        return result;
    }

    internal bool ConfirmBaseline(string scope, string hash, IReadOnlyList<MusketeerCareer> records)
    {
        if (!Record(scope, hash, records)) return false;
        Baselines[scope] = hash;
        return true;
    }

    internal static bool ValidRecords(IReadOnlyList<MusketeerCareer> records)
    {
        if (records == null || records.Count > MaxRecords) return false;
        var ids = new HashSet<Guid>();
        var native = new HashSet<string>(StringComparer.Ordinal);
        var slots = new HashSet<int>();
        foreach (var record in records)
        {
            if (record == null || record.Id == Guid.Empty || (record.Kind != MusketeerCareer.KindUnit && record.Kind != MusketeerCareer.KindGun)) return false;
            if (record.NativeId == null || record.NativeId.Length > MaxNativeIdLength) return false;
            // Rack metadata belongs to guns only, and one rack slot holds at most one career.
            if (record.StockSlot < MusketeerCareer.NoStockSlot || record.StockSlot >= MaxStockSlots) return false;
            if (record.Kind == MusketeerCareer.KindUnit && record.StockSlot != MusketeerCareer.NoStockSlot) return false;
            if (record.StockSlot != MusketeerCareer.NoStockSlot && !slots.Add(record.StockSlot)) return false;
            if (!ids.Add(record.Id)) return false;
            if (record.NativeId.Length > 0 && !native.Add(record.NativeId)) return false;
        }
        return true;
    }

    // The same native snapshot cannot describe two conflicting career histories. A duplicate id
    // for one hash is refused instead of silently rewritten; the pinned baseline is never evicted.
    internal bool Record(string scope, string hash, IReadOnlyList<MusketeerCareer> records)
    {
        if (!HashValid(scope) || !HashValid(hash) || !ValidRecords(records)) return false;
        if (!Scopes.TryGetValue(scope, out var list))
        {
            if (Scopes.Count >= MaxScopes) return false;
            list = new(); Scopes.Add(scope, list);
        }
        var old = list.Find(x => x.Hash == hash);
        if (old != null)
        {
            if (old.HashKind != HashKindFingerprint || !Same(old.Records, records)) return false;
            list.Remove(old); list.Insert(0, old); return true;
        }
        list.Insert(0, new() { Hash = hash, HashKind = HashKindFingerprint, Records = records.Select(x => x.Copy()).ToList() });
        if (list.Count > MaxSnapshots)
        {
            Baselines.TryGetValue(scope, out string pinned);
            int evict = list.FindLastIndex(x => x.Hash != pinned);
            if (evict >= 0) list.RemoveAt(evict);
        }
        return true;
    }

    internal static bool Same(IReadOnlyList<MusketeerCareer> a, IReadOnlyList<MusketeerCareer> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var x in a)
            if (!b.Any(y => x.Id == y.Id && x.Kind == y.Kind && x.NativeId == y.NativeId && x.StockSlot == y.StockSlot)) return false;
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
                    writer.WriteNumber("hashKind", snapshot.HashKind);
                    writer.WriteStartArray("records");
                    foreach (var record in snapshot.Records.OrderBy(x => x.Id))
                    {
                        writer.WriteStartObject(); writer.WriteString("id", record.Id.ToString("N")); writer.WriteNumber("kind", record.Kind);
                        writer.WriteString("nativeId", record.NativeId); writer.WriteNumber("stockSlot", record.StockSlot); writer.WriteEndObject();
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

    internal static MusketeerArchive Decode(byte[] bytes, out bool unsupported)
    {
        unsupported = false;
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count(x => x.Name == "schemaVersion") != 1)
            throw new FormatException("schema version");
        int version = root.GetProperty("schemaVersion").GetInt32();
        if (version != Version) { unsupported = true; return null; }
        // Duplicate/unknown fields are rejected, never silently discarded on rewrite.
        Fields(root, "schemaVersion", "scopes", "contexts");
        var archive = new MusketeerArchive();
        var scopes = root.GetProperty("scopes");
        if (scopes.GetArrayLength() > MaxScopes) throw new FormatException("scope capacity");
        foreach (var node in scopes.EnumerateArray())
        {
            Fields(node, "scope", "baseline", "snapshots"); var key = node.GetProperty("scope").GetString();
            if (!HashValid(key) || archive.Scopes.ContainsKey(key)) throw new FormatException("scope");
            var list = new List<MusketeerSnapshot>(); archive.Scopes.Add(key, list);
            var snapshots = node.GetProperty("snapshots");
            if (snapshots.GetArrayLength() > MaxSnapshots) throw new FormatException("snapshot capacity");
            foreach (var data in snapshots.EnumerateArray())
            {
                Fields(data, "hash", "hashKind", "records");
                var hash = data.GetProperty("hash").GetString();
                if (!HashValid(hash) || list.Any(x => x.Hash == hash)) throw new FormatException("snapshot");
                int hashKind = data.GetProperty("hashKind").GetInt32();
                if (!HashKindValid(hashKind)) throw new FormatException("hash kind");
                var records = new List<MusketeerCareer>();
                var recordNodes = data.GetProperty("records");
                if (recordNodes.GetArrayLength() > MaxRecords) throw new FormatException("record capacity");
                foreach (var recordNode in recordNodes.EnumerateArray())
                {
                    Fields(recordNode, "id", "kind", "nativeId", "stockSlot");
                    if (!Guid.TryParseExact(recordNode.GetProperty("id").GetString(), "N", out var id)) throw new FormatException("identity");
                    var nativeId = recordNode.GetProperty("nativeId").GetString() ?? "";
                    records.Add(new()
                    {
                        Id = id,
                        Kind = recordNode.GetProperty("kind").GetInt32(),
                        NativeId = nativeId,
                        StockSlot = recordNode.GetProperty("stockSlot").GetInt32()
                    });
                }
                if (!ValidRecords(records)) throw new FormatException("records");
                list.Add(new() { Hash = hash, HashKind = hashKind, Records = records });
            }
            string baseline = node.GetProperty("baseline").GetString();
            if (baseline == null || (baseline.Length > 0 && (!HashValid(baseline) || !list.Any(x => x.Hash == baseline)))) throw new FormatException("baseline");
            if (baseline.Length > 0) archive.Baselines.Add(key, baseline);
        }
        var contexts = root.GetProperty("contexts");
        if (contexts.GetArrayLength() > MaxContexts) throw new FormatException("context capacity");
        foreach (var node in contexts.EnumerateArray())
        {
            Fields(node, "context", "active", "epochs");
            string key = node.GetProperty("context").GetString();
            if (!HashValid(key) || archive.Contexts.ContainsKey(key)) throw new FormatException("context");
            var context = new MusketeerContext();
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

// Single writer for the sidecar: the caller mutates a freshly loaded disk state, the context
// registration rides in the same compare-and-swap, and a write only lands when the on-disk
// bytes are still the ones that were read. Any failure leaves the previous baseline untouched.
internal static class MusketeerArchiveStore
{
    internal const int MaxBytes = 16 * 1024 * 1024;
    internal enum State { Missing, Valid, Corrupt, Unsupported, IoError }
    internal sealed class ReadResult
    {
        internal State Status;
        internal MusketeerArchive Archive;
        internal byte[] Original;
        internal bool RecoveredBackup;
        internal bool Writable => (Status == State.Missing || Status == State.Valid) && !RecoveredBackup;
    }

    internal static ReadResult Load(string path)
    {
        var result = ReadOne(path);
        // An unknown future schema must never be replaced by an older backup.
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
                var archive = MusketeerArchive.Decode(bytes, out bool unsupported);
                return new() { Status = unsupported ? State.Unsupported : State.Valid, Archive = archive, Original = bytes };
            }
            catch { return new() { Status = State.Corrupt, Original = bytes }; }
        }
        catch (FileNotFoundException) { return new() { Status = State.Missing, Archive = new() }; }
        catch (DirectoryNotFoundException) { return new() { Status = State.Missing, Archive = new() }; }
        catch { return new() { Status = State.IoError }; }
    }

    internal static bool Save(string path, ReadResult expected, MusketeerArchive archive)
    {
        string temporary = null;
        try
        {
            if (expected == null || !expected.Writable || archive == null) return false;
            var current = Load(path);
            if (!current.Writable || current.Status != expected.Status || !Equal(current.Original, expected.Original)) return false;
            byte[] bytes = archive.Encode();
            if (bytes.Length > MaxBytes) return false;
            MusketeerArchive.Decode(bytes, out bool unsupported);
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
