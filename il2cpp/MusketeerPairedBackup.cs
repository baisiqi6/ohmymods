using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KingdomEnhancedMod;

// Paired backup of one accepted native save: a single JSON container that stores the exact
// native bytes together with the digests of both sides of the pairing:
//
//   {"version":1,"file":"...","nativeSha256":"...","archiveSha256":"...","native":"<base64>"}
//
// Exactly those five fields, all required, types exact, unknown/duplicate fields, trailing
// content, UTF-8 BOM and non-canonical base64 refused. The career identity is never accepted
// from the caller: Save re-extracts the document from the given native bytes, and TryRecover
// decodes the career archive from the actual current native payload. Recovery accepts only a
// current native whose whole bytes equal the stored native (SHA-256 and byte-for-byte equality)
// and whose embedded key parses; it never guesses by recency, mtime or record count, never
// restores a native save, never writes a sidecar and never creates a character.
//
// Save writes a temp file, flushes it to disk (Flush(true)), then replaces the primary under a
// process lock with a compare-and-swap recheck of the observed primary bytes; a change observed
// between probe and replace is refused. This protects cooperating calls in this process - it is
// not an atomic cross-process CAS, so paired-writer ownership must still be constrained by the
// caller. The previous complete V1 container rotates to .bak exactly on that replace. A failed
// save leaves primary and bak untouched, and an unrecognized primary (corrupt, unreadable,
// foreign file or future version) is never overwritten and never rotated into .bak. TryRecover
// shows the primary first and falls back to a valid bak only when the primary is missing or
// corrupt; a future primary is never downgraded.
internal static class MusketeerPairedBackup
{
    internal const int Version = 1;
    internal const int MaxPairBytes = 50 * 1024 * 1024;

    private static readonly object Gate = new();

    private enum ContainerStatus { Missing, Valid, Corrupt, Unsupported, Io }

    private sealed class Container
    {
        internal string File;
        internal string NativeSha256;
        internal string ArchiveSha256;
        internal byte[] Native;
    }

    internal static bool Save(string path, string file, byte[] native, out string reason)
    {
        reason = null;
        if (!MusketeerNativeArchive.ValidFile(file)) { reason = "file"; return false; }
        if (string.IsNullOrEmpty(path)) { reason = "path"; return false; }
        if (native == null || native.Length == 0) { reason = "native"; return false; }
        if (native.Length > MusketeerNativeArchive.MaxNativeCompressedBytes) { reason = "native-size"; return false; }
        // The pairing is only ever built from the document that actually lives in the bytes.
        if (!MusketeerNativeArchive.TryExtractDocument(native, file, out _, out _, out byte[] archiveBytes, out bool unsupported, out string extractReason))
        {
            reason = unsupported ? "unsupported:" + extractReason : extractReason;
            return false;
        }
        string json = BuildContainer(file, ShaHex(native), ShaHex(archiveBytes), Convert.ToBase64String(native));
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxPairBytes) { reason = "size"; return false; }

        lock (Gate)
        {
            ContainerStatus status = ReadOne(path, out Container primary, out byte[] primaryBytes, out string probeReason);
            if (status != ContainerStatus.Missing && status != ContainerStatus.Valid) { reason = probeReason; return false; }
            if (status == ContainerStatus.Valid && !string.Equals(primary.File, file, StringComparison.Ordinal))
            {
                reason = "pair-file";
                return false;
            }
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                WriteDurable(temporary, bytes);
                if (status == ContainerStatus.Missing)
                {
                    // CAS: a primary that appeared after the probe must not be replaced.
                    if (File.Exists(path) || Directory.Exists(path)) { reason = "external"; return false; }
                    File.Move(temporary, path);
                }
                else
                {
                    if (!File.Exists(path)) { reason = "external"; return false; }
                    byte[] current;
                    try { current = File.ReadAllBytes(path); }
                    catch (Exception e) { reason = "io:" + e.GetType().Name; return false; }
                    if (!BytesEqual(current, primaryBytes)) { reason = "external"; return false; }
                    File.Replace(temporary, path, path + ".bak", true);
                }
                temporary = null;
                if (!BytesEqual(File.ReadAllBytes(path), bytes)) { reason = "readback"; return false; }
                return true;
            }
            catch (Exception e) { reason = "io:" + e.GetType().Name; return false; }
            finally { if (temporary != null) TryDelete(temporary); }
        }
    }

    internal static bool TryRecover(string path, string file, byte[] currentNative, out string raw, out MusketeerArchive archive, out string reason)
    {
        raw = null;
        archive = null;
        reason = null;
        if (!MusketeerNativeArchive.ValidFile(file)) { reason = "file"; return false; }
        if (string.IsNullOrEmpty(path)) { reason = "path"; return false; }
        if (currentNative == null || currentNative.Length == 0) { reason = "native"; return false; }
        if (currentNative.Length > MusketeerNativeArchive.MaxNativeCompressedBytes) { reason = "native-size"; return false; }

        if (!ReadPair(path, file, out Container container, out reason)) return false;
        if (!string.Equals(ShaHex(currentNative), container.NativeSha256, StringComparison.OrdinalIgnoreCase)
            || !BytesEqual(currentNative, container.Native))
        {
            reason = "mismatch";
            return false;
        }
        if (!MusketeerNativeArchive.TryExtractDocument(currentNative, file, out raw, out archive, out byte[] archiveBytes, out bool unsupported, out string extractReason))
        {
            raw = null;
            archive = null;
            reason = unsupported ? "unsupported:" + extractReason : extractReason;
            return false;
        }
        if (!string.Equals(ShaHex(archiveBytes), container.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            raw = null;
            archive = null;
            reason = "archive-digest";
            return false;
        }
        return true;
    }

    // Primary first; bak is consulted only when the primary is missing or corrupt. A future
    // primary is refused outright (no downgrade), and a valid container for another file is
    // never adopted as this file's pairing.
    private static bool ReadPair(string path, string file, out Container container, out string reason)
    {
        container = null;
        reason = null;
        ContainerStatus primaryStatus = ReadOne(path, out Container primary, out _, out string primaryReason);
        if (primaryStatus == ContainerStatus.Valid && string.Equals(primary.File, file, StringComparison.Ordinal))
        {
            container = primary;
            return true;
        }
        if (primaryStatus == ContainerStatus.Valid) { reason = "pair-file"; return false; }
        // A future primary is never downgraded, and an unreadable path (directory, I/O failure)
        // keeps its visible reason - only a missing or corrupt primary opens the bak fallback.
        if (primaryStatus != ContainerStatus.Missing && primaryStatus != ContainerStatus.Corrupt)
        {
            reason = primaryReason;
            return false;
        }

        ContainerStatus backupStatus = ReadOne(path + ".bak", out Container backup, out _, out string backupReason);
        if (backupStatus == ContainerStatus.Valid)
        {
            if (!string.Equals(backup.File, file, StringComparison.Ordinal)) { reason = "pair-file"; return false; }
            container = backup;
            return true;
        }
        if (primaryStatus == ContainerStatus.Missing)
        {
            reason = backupStatus == ContainerStatus.Missing ? "pair-missing" : backupReason;
            return false;
        }
        reason = backupStatus == ContainerStatus.Unsupported ? backupReason : primaryReason;
        return false;
    }

    private static ContainerStatus ReadOne(string path, out Container container, out byte[] fileBytes, out string reason)
    {
        container = null;
        fileBytes = null;
        reason = null;
        byte[] bytes;
        try
        {
            // A directory is an access failure, not a missing pair: it must never read as
            // "missing" (which would silently unlock the bak fallback or the write path).
            if (Directory.Exists(path)) { reason = "pair-directory"; return ContainerStatus.Io; }
            // Open the file directly: File.Exists returns false for an unsearchable parent or a
            // permission failure, which would silently turn a real access error into "missing".
            // Only a genuine FileNotFound/DirectoryNotFound is a missing pair; every other
            // failure stays an observable Io. The bounded read runs on this same opened handle,
            // so the observed length and the actual byte count cannot disagree silently - a
            // concurrent truncation or growth is refused instead of accepted.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = stream.Length;
            if (length < 2) { reason = "pair-corrupt"; return ContainerStatus.Corrupt; }
            if (length > MaxPairBytes) { reason = "pair-size"; return ContainerStatus.Corrupt; }
            bytes = new byte[(int)length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0) { reason = "pair-corrupt"; return ContainerStatus.Corrupt; }
                offset += read;
            }
            if (stream.ReadByte() != -1) { reason = "pair-corrupt"; return ContainerStatus.Corrupt; }
        }
        catch (FileNotFoundException) { return ContainerStatus.Missing; }
        catch (DirectoryNotFoundException) { return ContainerStatus.Missing; }
        catch (Exception e) { reason = "io:" + e.GetType().Name; return ContainerStatus.Io; }
        fileBytes = bytes;
        return ParseContainer(bytes, out container, out reason);
    }

    private static ContainerStatus ParseContainer(byte[] bytes, out Container container, out string reason)
    {
        container = null;
        reason = null;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            reason = "pair-bom";
            return ContainerStatus.Corrupt;
        }
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes); }
        catch (JsonException) { reason = "pair-json"; return ContainerStatus.Corrupt; }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { reason = "pair-document"; return ContainerStatus.Corrupt; }
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
            if (versionCount != 1 || version < 0) { reason = "pair-document"; return ContainerStatus.Corrupt; }
            if (version > Version) { reason = "pair-version"; return ContainerStatus.Unsupported; }
            if (version < Version) { reason = "pair-version"; return ContainerStatus.Corrupt; }
            // version == 1: exactly the five fields, all required, types exact.
            JsonElement fileNode = default, nativeShaNode = default, archiveShaNode = default, nativeNode = default;
            bool hasFile = false, hasNativeSha = false, hasArchiveSha = false, hasNative = false;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!names.Add(property.Name)) { reason = "pair-document"; return ContainerStatus.Corrupt; }
                switch (property.Name)
                {
                    case "version": break;
                    case "file": fileNode = property.Value; hasFile = true; break;
                    case "nativeSha256": nativeShaNode = property.Value; hasNativeSha = true; break;
                    case "archiveSha256": archiveShaNode = property.Value; hasArchiveSha = true; break;
                    case "native": nativeNode = property.Value; hasNative = true; break;
                    default: reason = "pair-document"; return ContainerStatus.Corrupt;
                }
            }
            if (!hasFile || !hasNativeSha || !hasArchiveSha || !hasNative)
            {
                reason = "pair-document";
                return ContainerStatus.Corrupt;
            }
            if (fileNode.ValueKind != JsonValueKind.String || nativeShaNode.ValueKind != JsonValueKind.String
                || archiveShaNode.ValueKind != JsonValueKind.String || nativeNode.ValueKind != JsonValueKind.String)
            {
                reason = "pair-document";
                return ContainerStatus.Corrupt;
            }
            string file = fileNode.GetString();
            string nativeSha = nativeShaNode.GetString();
            string archiveSha = archiveShaNode.GetString();
            if (!MusketeerNativeArchive.IsSha256Hex(nativeSha) || !MusketeerNativeArchive.IsSha256Hex(archiveSha))
            {
                reason = "pair-digest";
                return ContainerStatus.Corrupt;
            }
            if (!MusketeerNativeArchive.TryDecodeBase64(nativeNode.GetString(), MusketeerNativeArchive.MaxNativeCompressedBytes, out byte[] nativeBytes, out string base64Reason))
            {
                reason = base64Reason == "size" ? "pair-size" : "pair-native";
                return ContainerStatus.Corrupt;
            }
            if (!string.Equals(ShaHex(nativeBytes), nativeSha, StringComparison.OrdinalIgnoreCase))
            {
                reason = "pair-digest";
                return ContainerStatus.Corrupt;
            }
            // A V1 container is complete only when its stored native really carries the document
            // and the extracted archive matches archiveSha256. An embedded future document or
            // archive classifies as unsupported (never replaced, never downgraded to a bak);
            // every other extraction failure means the container is not intact.
            if (!MusketeerNativeArchive.TryExtractDocument(nativeBytes, file, out _, out _, out byte[] embeddedArchive, out bool embeddedUnsupported, out string embeddedReason))
            {
                if (embeddedUnsupported) { reason = embeddedReason; return ContainerStatus.Unsupported; }
                reason = embeddedReason;
                return ContainerStatus.Corrupt;
            }
            if (!string.Equals(ShaHex(embeddedArchive), archiveSha, StringComparison.OrdinalIgnoreCase))
            {
                reason = "archive-digest";
                return ContainerStatus.Corrupt;
            }
            container = new Container { File = file, NativeSha256 = nativeSha, ArchiveSha256 = archiveSha, Native = nativeBytes };
            return ContainerStatus.Valid;
        }
    }

    private static string BuildContainer(string file, string nativeSha, string archiveSha, string nativeBase64)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteString("file", file);
            writer.WriteString("nativeSha256", nativeSha);
            writer.WriteString("archiveSha256", archiveSha);
            writer.WriteString("native", nativeBase64);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteDurable(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(true);
    }

    private static string ShaHex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool BytesEqual(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
