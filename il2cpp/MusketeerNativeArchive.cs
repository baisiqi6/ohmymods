using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace KingdomEnhancedMod;

// Native-Prefs authority for paid musketeer careers. The game save file's root.prefs holds
// string rows ("srzEntries"); this class encodes the existing MusketeerArchive v2 career bytes
// into the single fixed key KingdomEnhancedMod_MusketeerRights_v1 as a strict JSON document:
//
//   {"version":1,"file":"<GlobalSaveData.filename>","archive":"<base64 of Archive.Encode()>"}
//
// Exactly those three fields, all required, types exact, unknown/duplicate fields, trailing
// content, UTF-8 BOM and non-canonical base64 refused. "file" is the exact native basename
// (never a path) and is checked on every entry point. version > 1 means a newer writer exists:
// it is reported unsupported and never downgraded or silently rewritten. The career schema,
// contexts, epochs and records are MusketeerArchive's own - nothing is re-derived here.
//
// TryExtract reads the bytes the game actually persisted (bounded gzip), locates the single
// Musk row in root.prefs.srzEntries and parses that exact string value; the global save is
// never re-serialized. Missing (no key), corrupt and unsupported stay distinguishable: a
// missing key reports "native-key-missing" with unsupported=false, structural/base64/identity
// failures report their corrupt reason, and a future document or future archive reports
// unsupported=true with reason "version"/"archive-version".
internal static class MusketeerNativeArchive
{
    internal const string Key = "KingdomEnhancedMod_MusketeerRights_v1";
    internal const int Version = 1;
    internal const int MaxDocumentBytes = 24 * 1024 * 1024;
    internal const int MaxArchiveBytes = MusketeerArchiveStore.MaxBytes;
    internal const int MaxNativeCompressedBytes = 32 * 1024 * 1024;
    internal const int MaxNativeDecompressedBytes = 128 * 1024 * 1024;

    // Basename only: non-empty and free of path separators and the Windows drive/stream colon.
    // No character white-list beyond that (a legal native save name is never over-rejected).
    internal static bool ValidFile(string file)
        => !string.IsNullOrEmpty(file)
           && file.IndexOf('/') < 0 && file.IndexOf('\\') < 0 && file.IndexOf(':') < 0;

    internal static bool IsSha256Hex(string value)
    {
        if (value == null || value.Length != 64) return false;
        foreach (char c in value)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
        return true;
    }

    // Strict standard base64. Convert.FromBase64String silently skips whitespace and accepts
    // some non-canonical forms, so acceptance requires a byte-identical re-encode; anything
    // else is refused instead of normalized. The length pre-check bounds allocation.
    internal static bool TryDecodeBase64(string text, int maxBytes, out byte[] bytes, out string reason)
    {
        bytes = null;
        reason = null;
        if (string.IsNullOrEmpty(text)) { reason = "base64"; return false; }
        if ((long)text.Length > 4 * ((long)maxBytes + 2) / 3) { reason = "size"; return false; }
        try { bytes = Convert.FromBase64String(text); }
        catch (FormatException) { reason = "base64"; return false; }
        if (!string.Equals(Convert.ToBase64String(bytes), text, StringComparison.Ordinal))
        {
            bytes = null;
            reason = "base64";
            return false;
        }
        if (bytes.Length > maxBytes) { bytes = null; reason = "size"; return false; }
        return true;
    }

    internal static bool TryEncode(string file, MusketeerArchive archive, out string raw, out string reason)
    {
        raw = null;
        reason = null;
        if (!ValidFile(file)) { reason = "file"; return false; }
        if (archive == null) { reason = "archive"; return false; }
        byte[] bytes;
        try { bytes = archive.Encode(); }
        catch (Exception) { reason = "encode"; return false; }
        if (bytes == null || bytes.Length == 0) { reason = "archive"; return false; }
        if (bytes.Length > MaxArchiveBytes) { reason = "archive-size"; return false; }
        string json = BuildDocument(file, Convert.ToBase64String(bytes));
        if (Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes) { reason = "size"; return false; }
        // Never publish a document the strict reader cannot read back.
        if (!TryParseDocument(file, json, out _, out _, out _, out string checkReason))
        {
            reason = "encode:" + checkReason;
            return false;
        }
        raw = json;
        return true;
    }

    internal static bool TryDecode(string file, string raw, out MusketeerArchive archive, out bool unsupported, out string reason)
        => TryParseDocument(file, raw, out archive, out _, out unsupported, out reason);

    internal static bool TryExtract(byte[] nativeCompressed, string file, out string raw, out MusketeerArchive archive, out bool unsupported, out string reason)
        => TryExtractDocument(nativeCompressed, file, out raw, out archive, out _, out unsupported, out reason);

    internal static bool TryExtractDocument(byte[] nativeCompressed, string file, out string raw, out MusketeerArchive archive, out byte[] archiveBytes, out bool unsupported, out string reason)
    {
        raw = null;
        archive = null;
        archiveBytes = null;
        unsupported = false;
        reason = null;
        if (!ValidFile(file)) { reason = "file"; return false; }
        if (nativeCompressed == null || nativeCompressed.Length == 0) { reason = "native"; return false; }
        if (nativeCompressed.Length > MaxNativeCompressedBytes) { reason = "size"; return false; }
        if (!TryDecompress(nativeCompressed, out byte[] decompressed, out reason)) return false;
        if (decompressed.Length >= 3 && decompressed[0] == 0xEF && decompressed[1] == 0xBB && decompressed[2] == 0xBF)
        {
            reason = "bom";
            return false;
        }
        JsonDocument document;
        try { document = JsonDocument.Parse(decompressed); }
        catch (JsonException) { reason = "json"; return false; }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { reason = "root"; return false; }
            var rootNames = new HashSet<string>(StringComparer.Ordinal);
            JsonElement prefs = default;
            bool hasPrefs = false;
            foreach (var property in root.EnumerateObject())
            {
                if (!rootNames.Add(property.Name)) { reason = "root"; return false; }
                if (property.Name == "prefs") { prefs = property.Value; hasPrefs = true; }
            }
            if (!hasPrefs) { reason = "prefs"; return false; }
            if (prefs.ValueKind != JsonValueKind.Object) { reason = "prefs"; return false; }
            var prefNames = new HashSet<string>(StringComparer.Ordinal);
            JsonElement entries = default;
            bool hasEntries = false;
            foreach (var property in prefs.EnumerateObject())
            {
                if (!prefNames.Add(property.Name)) { reason = "prefs"; return false; }
                if (property.Name == "srzEntries") { entries = property.Value; hasEntries = true; }
            }
            if (!hasEntries) { reason = "entries"; return false; }
            if (entries.ValueKind != JsonValueKind.Array) { reason = "entries"; return false; }

            // The payload must be the string value that actually persisted. A duplicated Musk row
            // makes the source ambiguous and fails the whole extraction instead of picking one.
            string payload = null;
            int matches = 0;
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
                if (!string.Equals(key, Key, StringComparison.Ordinal)) continue;
                if (++matches > 1) { reason = "native-key"; return false; }
                payload = val;
            }
            if (matches == 0) { reason = "native-key-missing"; return false; }
            if (!TryParseDocument(file, payload, out archive, out archiveBytes, out unsupported, out string documentReason))
            {
                reason = documentReason;
                return false;
            }
            raw = payload;
            return true;
        }
    }

    internal static bool TryParseDocument(string file, string raw, out MusketeerArchive archive, out byte[] archiveBytes, out bool unsupported, out string reason)
    {
        archive = null;
        archiveBytes = null;
        unsupported = false;
        reason = null;
        if (!ValidFile(file)) { reason = "file"; return false; }
        if (raw == null || raw.Length == 0) { reason = "document"; return false; }
        if (raw[0] == '\uFEFF') { reason = "bom"; return false; }
        if (Encoding.UTF8.GetByteCount(raw) > MaxDocumentBytes) { reason = "size"; return false; }
        JsonDocument document;
        try { document = JsonDocument.Parse(raw); }
        catch (JsonException) { reason = "json"; return false; }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { reason = "document"; return false; }
            // The version decides the schema first: one readable integer, exactly once. A future
            // version is unsupported regardless of how its remaining fields look (a newer writer
            // may add or drop fields); a duplicated or malformed version stays corrupt.
            int version = -1;
            int versionCount = 0;
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name != "version") continue;
                versionCount++;
                if (versionCount == 1 && property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetInt32(out int parsed)) version = parsed;
            }
            if (versionCount != 1 || version < 0) { reason = "document"; return false; }
            if (version > Version) { unsupported = true; reason = "version"; return false; }
            if (version < Version) { reason = "document"; return false; }
            // version == 1: exactly the three fields, all required, types exact.
            JsonElement fileNode = default, archiveNode = default;
            bool hasFile = false, hasArchive = false;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!names.Add(property.Name)) { reason = "document"; return false; }
                switch (property.Name)
                {
                    case "version": break;
                    case "file": fileNode = property.Value; hasFile = true; break;
                    case "archive": archiveNode = property.Value; hasArchive = true; break;
                    default: reason = "document"; return false;
                }
            }
            if (!hasFile || !hasArchive) { reason = "document"; return false; }
            if (fileNode.ValueKind != JsonValueKind.String || archiveNode.ValueKind != JsonValueKind.String)
            {
                reason = "document";
                return false;
            }
            if (!string.Equals(fileNode.GetString(), file, StringComparison.Ordinal)) { reason = "file"; return false; }
            if (!TryDecodeBase64(archiveNode.GetString(), MaxArchiveBytes, out byte[] bytes, out string base64Reason))
            {
                reason = base64Reason == "size" ? "archive-size" : base64Reason;
                return false;
            }
            try
            {
                var decoded = MusketeerArchive.Decode(bytes, out bool archiveUnsupported);
                if (archiveUnsupported)
                {
                    unsupported = true;
                    reason = "archive-version";
                    return false;
                }
                if (decoded == null) { reason = "archive"; return false; }
                archive = decoded;
                archiveBytes = bytes;
                return true;
            }
            catch (Exception) { reason = "archive"; return false; }
        }
    }

    // Bounded gzip with integrity checking: the compressed input length is capped by the caller,
    // the decompressed size is capped here while streaming, the standard header is verified, and
    // the streamed CRC32/ISIZE are compared against the trailer actually stored in the payload,
    // so truncated footers, appended garbage and ordinary concatenated payload corruption are
    // refused. This is integrity validation of the current native output; it does not claim
    // adversarial single-member boundary proofs.
    private static bool TryDecompress(byte[] input, out byte[] output, out string reason)
    {
        output = null;
        reason = null;
        if (input.Length < 18) { reason = "gzip"; return false; }
        if (input[0] != 0x1F || input[1] != 0x8B || input[2] != 0x08 || (input[3] & 0xE0) != 0)
        {
            reason = "gzip-header";
            return false;
        }
        try
        {
            using var compressed = new MemoryStream(input, writable: false);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var plain = new MemoryStream();
            byte[] chunk = new byte[81920];
            uint crc = 0xFFFFFFFFu;
            int total = 0;
            int read;
            while ((read = gzip.Read(chunk, 0, chunk.Length)) > 0)
            {
                total += read;
                if (total > MaxNativeDecompressedBytes) { reason = "size"; return false; }
                for (int i = 0; i < read; i++) crc = Crc32Table[(crc ^ chunk[i]) & 0xFF] ^ (crc >> 8);
                plain.Write(chunk, 0, read);
            }
            crc = ~crc;
            if (!ValidateTrailer(input, crc, (uint)total, out reason)) return false;
            output = plain.ToArray();
            return true;
        }
        catch (Exception) { reason = "gzip"; return false; }
    }

    // The CRC32/ISIZE computed from the actually decompressed bytes must equal the trailer
    // stored at the end of the payload. The runtime's own gzip reader is not trusted to
    // validate the trailer (it does not do so consistently across target runtimes), so this
    // stays an independent check. Internal for direct contract tests.
    internal static bool ValidateTrailer(byte[] payload, uint crc32, uint size, out string reason)
    {
        reason = null;
        if (payload == null || payload.Length < 18) { reason = "gzip"; return false; }
        if (crc32 != ReadUInt32Le(payload, payload.Length - 8)) { reason = "gzip-crc"; return false; }
        if (size != ReadUInt32Le(payload, payload.Length - 4)) { reason = "gzip-size"; return false; }
        return true;
    }

    private static uint ReadUInt32Le(byte[] bytes, int offset)
        => (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (int index = 0; index < table.Length; index++)
        {
            uint value = (uint)index;
            for (int bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            table[index] = value;
        }
        return table;
    }

    private static string BuildDocument(string file, string archiveBase64)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteString("file", file);
            writer.WriteString("archive", archiveBase64);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
