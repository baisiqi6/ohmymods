#if !ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace KingdomEnhancedMod;

// Existing durable campaign GUID + exact native scene. Never derive identity from time/slot.
internal readonly record struct MountIslandGemOwner(IntPtr Global, IntPtr Campaign, IntPtr Game,
    IntPtr World, IntPtr Level, IntPtr Layer, int Slot, int Land, string Guid);

internal sealed class MountIslandGemRewardState
{
    internal const string Key = "KEM.MountIslandGems.v1";
    private const int MaxCampaigns = 64, MaxBytes = 16384;
    private readonly string _original;
    private readonly bool _valid;
    private readonly HashSet<string> _knownGuids = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _staged = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Guid, int Land), MountIslandGemOwner> _live = new();
    internal bool Ready { get; private set; }
    internal string Fault { get; private set; }
    internal bool CanSerialize => _valid;
    internal bool HasStaged => _staged.Count != 0;

    internal MountIslandGemRewardState(string raw)
    {
        _original = raw;
        Ready = _valid = raw == null || TryParse(raw);
        foreach (var guid in _staged.Keys) _knownGuids.Add(guid);
        if (!Ready) Fault = "invalid-batch-payload";
    }

    internal static bool TryPlan(int land, out int count, out int amount)
    { count = land == 11 || land == 13 ? 4 : 0; amount = count == 0 ? 0 : 7; return count != 0; }
    private static int Bit(int land) => land == 11 ? 1 : land == 13 ? 2 : 0;
    private static bool ValidGuid(string value)
        => System.Guid.TryParseExact(value, "D", out var guid) && guid != System.Guid.Empty && guid.ToString("D") == value;
    internal bool StagedIssued(string guid, int land)
        => _valid && _staged.TryGetValue(guid, out int bits) && (bits & Bit(land)) != 0;
    internal bool CanIssue(string guid, int land)
        => Ready && ValidGuid(guid) && Bit(land) != 0 && !StagedIssued(guid, land)
            && !_live.ContainsKey((guid, land)) && (_knownGuids.Contains(guid) || _knownGuids.Count < MaxCampaigns);
    internal bool RecordLive(MountIslandGemOwner owner)
    {
        if (!CanIssue(owner.Guid, owner.Land)) return false;
        _live.Add((owner.Guid, owner.Land), owner);
        _knownGuids.Add(owner.Guid);
        return true;
    }
    internal bool LiveMatches(MountIslandGemOwner owner)
        => _live.TryGetValue((owner.Guid, owner.Land), out var live) && live == owner;
    internal void Freeze(string reason) { Ready = false; Fault = reason; }

    internal sealed class Capture
    {
        internal MountIslandGemOwner Owner;
        internal IntPtr Island;
        internal bool MarkerSeen, Closed;
    }
    internal Capture BeginCapture(MountIslandGemOwner owner, IntPtr island)
        => Ready && (LiveMatches(owner) || StagedIssued(owner.Guid, owner.Land)) && island != IntPtr.Zero ? new Capture { Owner = owner, Island = island } : null;
    internal void Marker(Capture scope, IntPtr island)
    { if (scope != null && !scope.Closed && island == scope.Island) scope.MarkerSeen = true; }
    internal bool EndCapture(Capture scope, MountIslandGemOwner current, bool normal, bool complete)
    {
        if (scope == null || scope.Closed) return false;
        scope.Closed = true;
        if (!Ready || !normal || !complete || !scope.MarkerSeen || scope.Owner != current || (!LiveMatches(current) && !StagedIssued(current.Guid, current.Land))) return false;
        _staged.TryGetValue(current.Guid, out int bits);
        _staged[current.Guid] = bits | Bit(current.Land);
        return true;
    }

    internal string SerializeStaged()
    {
        if (!_valid) return _original;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteNumber("version", 1); writer.WriteStartArray("campaigns");
            foreach (var row in _staged)
            { writer.WriteStartObject(); writer.WriteString("guid", row.Key); writer.WriteNumber("batch1", row.Value); writer.WriteEndObject(); }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private bool TryParse(string raw)
    {
        if (raw.Length == 0 || raw.Length > MaxBytes || Encoding.UTF8.GetByteCount(raw) > MaxBytes) return false;
        try
        {
            using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 6 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            bool version = false, campaigns = false;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Name == "version" && !version && property.Value.TryGetInt32(out int v) && v == 1) version = true;
                else if (property.Name == "campaigns" && !campaigns && property.Value.ValueKind == JsonValueKind.Array)
                {
                    campaigns = true;
                    foreach (var row in property.Value.EnumerateArray())
                    {
                        if (row.ValueKind != JsonValueKind.Object || _staged.Count >= MaxCampaigns) return false;
                        string guid = null; int bits = 0; bool seenBits = false;
                        foreach (var field in row.EnumerateObject())
                        {
                            if (field.Name == "guid" && guid == null && field.Value.ValueKind == JsonValueKind.String)
                                guid = field.Value.GetString();
                            else if (field.Name == "batch1" && !seenBits && field.Value.TryGetInt32(out bits) && bits >= 1 && bits <= 3)
                                seenBits = true;
                            else return false;
                        }
                        if (!ValidGuid(guid) || !seenBits || !_staged.TryAdd(guid, bits)) return false;
                    }
                }
                else return false;
            }
            return version && campaigns;
        }
        catch (Exception) { return false; }
    }
}

// One guard per retained native iterator object/life. MoveNext owns SpawnCurrency/Drop.
internal sealed class MountIslandGemDrain
{
    private bool _started, _draining;
    internal bool Complete { get; private set; }
    internal bool Unknown { get; private set; }
    internal void Fail() { Unknown = true; }
    internal bool AfterMove(bool owned, int initialState, Func<int> readState, Func<bool> move, bool nativeResult = true)
    {
        if (!owned || _draining || Complete || Unknown) return nativeResult;
        if (_started || initialState != 0) { Unknown = true; return nativeResult; }
        _started = true;
        _draining = true;
        try
        {
            for (int steps = 0; ; steps++)
            {
                int state = readState();
                if (state == 3) { Complete = true; return nativeResult; } // Native ten-second fade wait remains.
                if (!nativeResult || (state != 1 && state != 2) || steps == 16)
                { Unknown = true; return nativeResult; }
                nativeResult = move();
            }
        }
        catch (Exception) { Unknown = true; return nativeResult; }
        finally { _draining = false; }
    }
}
internal sealed class MountIslandGemNativeExpectation
{
    internal int Slot;
    internal string Guid, Batch;
    internal readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<string>> Rows = new();
    internal readonly Dictionary<string, MountIslandGemNativeCampaignProof> Campaigns = new(StringComparer.Ordinal);
}
internal sealed class MountIslandGemNativeCampaignProof
{
    internal int Slot;
    internal readonly Dictionary<int, List<string>> Rows = new();
}
internal static class MountIslandGemNativePayload
{
    private const int MaxCompressed = 32 * 1024 * 1024, MaxJson = 128 * 1024 * 1024;
    private static readonly uint[] CrcTable = CreateCrcTable();
    internal static bool Verify(byte[] data, MountIslandGemNativeExpectation expected, out string reason)
    {
        reason = "gem-payload";
        try
        {
            if (expected == null || data == null || data.Length < 18 || data.Length > MaxCompressed
                || data[0] != 31 || data[1] != 139 || data[2] != 8 || (data[3] & 224) != 0) return false;
            using var input = new MemoryStream(data, false);
            using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var output = new MemoryStream();
            byte[] block = new byte[32768]; uint crc = uint.MaxValue; int count;
            while ((count = gzip.Read(block, 0, block.Length)) > 0)
            {
                if (output.Length + count > MaxJson) { reason = "gem-size"; return false; }
                output.Write(block, 0, count);
                for (int i = 0; i < count; i++) crc = CrcTable[(crc ^ block[i]) & 255] ^ (crc >> 8);
            }
            uint footerCrc = BitConverter.ToUInt32(data, data.Length - 8), footerSize = BitConverter.ToUInt32(data, data.Length - 4);
            if ((crc ^ uint.MaxValue) != footerCrc || footerSize != output.Length) { reason = "gem-gzip-footer"; return false; }
            using var document = JsonDocument.Parse(output.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Unique(root)
                || !root.TryGetProperty("_currentCampaign", out var selected) || !selected.TryGetInt32(out int slot) || slot != expected.Slot
                || !root.TryGetProperty("campaigns", out var campaigns) || campaigns.ValueKind != JsonValueKind.Array
                || slot < -1 || slot >= campaigns.GetArrayLength()) { reason = "gem-native-owner"; return false; }
            if (!root.TryGetProperty("prefs", out var prefs) || prefs.ValueKind != JsonValueKind.Object
                || !prefs.TryGetProperty("srzEntries", out var entries) || entries.ValueKind != JsonValueKind.Array) return false;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in entries.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String
                    || !row.TryGetProperty("val", out var val) || val.ValueKind != JsonValueKind.String
                    || !values.TryAdd(key.GetString(), val.GetString())) { reason = "gem-prefs-duplicate"; return false; }
            }
            if (!values.TryGetValue(MountIslandGemRewardState.Key, out string batch) || batch != expected.Batch
                || !new MountIslandGemRewardState(batch).Ready) { reason = "gem-batch-copy"; return false; }
            if (!values.TryGetValue(HeavyShieldSaveSchema.Key, out string map)
                || !HeavyShieldSaveCodec.TryParse(map, out var ids, out _)) { reason = "gem-guid-map"; return false; }
            if (slot >= 0)
            {
                int matches = 0;
                foreach (var id in ids.Campaigns) if (id.Slot == slot && (expected.Guid == null || id.Guid == expected.Guid)) matches++;
                if (matches != 1) { reason = "gem-guid-slot"; return false; }
            }
            foreach (var protectedCampaign in expected.Campaigns)
            {
                int matches = 0;
                foreach (var id in ids.Campaigns) if (id.Slot == protectedCampaign.Value.Slot && id.Guid == protectedCampaign.Key) matches++;
                if (matches != 1 || !VerifyRows(campaigns, protectedCampaign.Value.Slot, protectedCampaign.Value.Rows, out reason)) return false;
            }
            if (!VerifyRows(campaigns, slot, expected.Rows, out reason)) return false;
            reason = null; return true;
        }
        catch (Exception) { return false; }
    }
    private static bool VerifyRows(JsonElement campaigns, int slot, Dictionary<int, List<string>> proofs, out string reason)
    {
        reason = "gem-owned-island";
        if (proofs.Count == 0) return true;
        if (slot < 0 || slot >= campaigns.GetArrayLength()) return false;
        var campaign = campaigns[slot];
        if (campaign.ValueKind != JsonValueKind.Object || !campaign.TryGetProperty("_islands", out var islands)
            || islands.ValueKind != JsonValueKind.Array) return false;
        foreach (var proof in proofs)
        {
            if (proof.Key < 0 || proof.Key >= islands.GetArrayLength()) return false;
            var island = islands[proof.Key];
            if (island.ValueKind != JsonValueKind.Object || !island.TryGetProperty("objects", out var rows)
                || rows.ValueKind != JsonValueKind.Array) return false;
            var owned = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) continue;
                if (!name.GetString().StartsWith("KEM.MountIslandGems.v1/", StringComparison.Ordinal)) continue;
                if (!owned.TryAdd(name.GetString(), row)) return false;
            }
            if (owned.Count != proof.Value.Count) { reason = "gem-owned-row-count"; return false; }
            foreach (string frozen in proof.Value)
            {
                using var saved = JsonDocument.Parse(frozen);
                var row = saved.RootElement;
                if (!owned.TryGetValue(row.GetProperty("name").GetString(), out var actual) || !Same(row, actual))
                { reason = "gem-owned-row-copy"; return false; }
            }
        }
        return true;
    }
    private static bool Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) if (!names.Add(property.Name) || !Unique(property.Value)) return false;
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) if (!Unique(item)) return false;
        return true;
    }
    private static bool Same(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind) return false;
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                int count = 0, other = 0;
                foreach (var field in expected.EnumerateObject()) { count++; if (!actual.TryGetProperty(field.Name, out var value) || !Same(field.Value, value)) return false; }
                foreach (var field in actual.EnumerateObject()) other++;
                return count == other;
            case JsonValueKind.Array:
                if (expected.GetArrayLength() != actual.GetArrayLength()) return false;
                for (int i = 0; i < expected.GetArrayLength(); i++) if (!Same(expected[i], actual[i])) return false;
                return true;
            case JsonValueKind.Number: return expected.GetDouble() == actual.GetDouble();
            case JsonValueKind.String: return expected.GetString() == actual.GetString();
            default: return true;
        }
    }
    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (int i = 0; i < table.Length; i++) { uint crc = (uint)i; for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1; table[i] = crc; }
        return table;
    }
}
#endif
