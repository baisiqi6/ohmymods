// Bounded, source-only package loader for the frozen Capital400 R12 delivery.
//
// Boundary: pure BCL. No Unity type, texture decode, native sizing, runtime IO, static cache,
// fee gate or promotion authority. The loader verifies the bootstrap index SHA, every indexed
// resource SHA, strict UTF-8 JSON, the reviewed R1 registry/layout reader, PNG IHDR size
// arithmetic and palette/asset binding. The returned package keeps only the 86 reviewed PNG
// byte arrays; CopyPngBytes always returns a clone. Source.NativeAccepted/PaidResourceGate stay
// false — this file never turns the offline package into a native or paid-resource claim.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace KingdomEnhancedMod;

/// <summary>Verified Capital400 R12 package: parsed R1 source plus private reviewed PNG bytes.</summary>
internal sealed class PalaceCapitalPackage
{
    private readonly Dictionary<string, byte[]> _pngByAssetId;

    internal PalaceProductionSource Source { get; }
    internal int ImageCount { get; }
    internal long TotalBytes { get; } // retained PNG payload only; index/README/JSON bytes are not kept

    internal PalaceCapitalPackage(PalaceProductionSource source, Dictionary<string, byte[]> pngByAssetId)
    {
        Source = source;
        _pngByAssetId = new Dictionary<string, byte[]>(pngByAssetId.Count, StringComparer.Ordinal);
        foreach (var pair in pngByAssetId) _pngByAssetId.Add(pair.Key, (byte[])pair.Value.Clone());
        ImageCount = _pngByAssetId.Count;
        long total = 0;
        foreach (var pair in _pngByAssetId) total = checked(total + pair.Value.Length);
        TotalBytes = total;
    }

    /// <summary>Returns a fresh clone per call so callers cannot mutate package state.</summary>
    internal byte[] CopyPngBytes(string assetId)
    {
        if (assetId == null) throw new ArgumentNullException(nameof(assetId));
        if (!_pngByAssetId.TryGetValue(assetId, out byte[] bytes))
            throw new ArgumentException("unknown-asset:" + assetId, nameof(assetId));
        return (byte[])bytes.Clone();
    }
}

internal static class PalaceCapitalResources
{
    internal const string Prefix = "KingdomEnhancedMod.KEM_Palace400R12/";
    private const string IndexName = "SHA256.json";
    internal const string IndexLogicalName = Prefix + IndexName;
    internal const string BootstrapIndexSha256 =
        "9239913d1975024ed0ad5bcf79534715ea390a39d6636e9426c1da24f6a141db";
    internal const int MaxIndexBytes = 512 * 1024;
    internal const int MaxResourceBytes = 1024 * 1024;
    internal const int MaxTotalBytes = 16 * 1024 * 1024;
    internal const int MaxPngRgbaBytes = 16 * 1024 * 1024;
    internal const int IndexedResourceCount = 91;
    internal const int PngResourceCount = 86;
    internal const int TotalResourceCount = 92;

    private const int MaxPathLength = 256;
    private const int MaxIndexDepth = 4;

    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <summary>
    /// Loads the package through caller-supplied streams. Every stream returned by
    /// <paramref name="openResource"/> is disposed, including on failure. Failure returns null
    /// package, false and a non-empty reason; no exception escapes.
    /// </summary>
    internal static bool TryLoad(Func<string, Stream> openResource, IReadOnlyCollection<string> availableNames,
        out PalaceCapitalPackage package, out string reason)
    {
        package = null;
        reason = "";
        try { return TryLoadCore(openResource, availableNames, out package, out reason); }
        catch (Exception e) { package = null; reason = "load-fault:" + e.GetType().Name; return false; }
    }

    private static bool TryLoadCore(Func<string, Stream> openResource, IReadOnlyCollection<string> availableNames,
        out PalaceCapitalPackage package, out string reason)
    {
        package = null;
        reason = "";
        if (openResource == null) { reason = "open-null"; return false; }
        if (availableNames == null) { reason = "names-null"; return false; }
        if (!CollectAvailableNames(availableNames, out var available, out reason)) return false;

        if (!TryReadResource(openResource, IndexLogicalName, MaxIndexBytes, out byte[] indexBytes, out reason))
            return false;
        if (!PalaceResourceBytes.MatchesSha256(indexBytes, BootstrapIndexSha256, out _))
        {
            // Exact bootstrap gate: a forged/corrupt index is rejected here, before parsing.
            reason = "index-sha";
            return false;
        }
        if (!TryParseIndex(indexBytes, out var entries, out reason)) return false;

        var expected = new HashSet<string>(StringComparer.Ordinal) { IndexLogicalName };
        foreach (string key in entries.Keys) expected.Add(Prefix + key);
        if (available.Count != TotalResourceCount) { reason = "available-count:" + available.Count; return false; }
        if (!available.SetEquals(expected)) { reason = "available-set"; return false; }

        var keys = new List<string>(entries.Keys);
        keys.Sort(StringComparer.Ordinal);
        var resources = new Dictionary<string, byte[]>(IndexedResourceCount, StringComparer.Ordinal);
        long total = indexBytes.Length;
        foreach (string key in keys)
        {
            if (!TryReadResource(openResource, Prefix + key, MaxResourceBytes, out byte[] bytes, out reason))
                return false;
            total = checked(total + bytes.Length);
            if (total > MaxTotalBytes) { reason = "total-budget:" + total; return false; }
            resources.Add(key, bytes);
        }

        return TryBind(entries, resources, out package, out reason);
    }

    /// <summary>
    /// Pure post-read verification seam (internal for the host suite): assumes the caller already
    /// verified the index bootstrap SHA, then checks every resource SHA before any JSON parse.
    /// Production only reaches it through <see cref="TryLoad"/>.
    /// </summary>
    internal static bool TryBind(IReadOnlyDictionary<string, string> indexEntries,
        IReadOnlyDictionary<string, byte[]> resources, out PalaceCapitalPackage package, out string reason)
    {
        package = null;
        reason = "";
        try { return TryBindCore(indexEntries, resources, out package, out reason); }
        catch (OverflowException) { package = null; reason = "number-overflow"; return false; }
        catch (Exception e) { package = null; reason = "bind-fault:" + e.GetType().Name; return false; }
    }

    private static bool TryBindCore(IReadOnlyDictionary<string, string> indexEntries,
        IReadOnlyDictionary<string, byte[]> resources, out PalaceCapitalPackage package, out string reason)
    {
        package = null;
        reason = "";
        if (indexEntries == null) { reason = "index-null"; return false; }
        if (resources == null) { reason = "resources-null"; return false; }
        if (indexEntries.Count != IndexedResourceCount) { reason = "index-count:" + indexEntries.Count; return false; }
        if (resources.Count != IndexedResourceCount) { reason = "resource-count:" + resources.Count; return false; }
        foreach (string required in new[] { "registry.json", "layout.json", "palette.json" })
            if (!indexEntries.ContainsKey(required)) { reason = "required-missing:" + required; return false; }

        long total = 0;
        foreach (var entry in indexEntries)
        {
            string key = entry.Key;
            if (!SafeIndexPath(key) || string.Equals(key, IndexName, StringComparison.Ordinal))
            { reason = "index-path"; return false; }
            if (!IsSha256Hex(entry.Value)) { reason = "index-hash"; return false; }
            if (!resources.TryGetValue(key, out byte[] bytes) || bytes == null)
            { reason = "resource-missing:" + key; return false; }
            if (bytes.Length > MaxResourceBytes) { reason = "resource-size:" + key; return false; }
            total = checked(total + bytes.Length);
            if (total > MaxTotalBytes) { reason = "total-budget:" + total; return false; }
        }
        foreach (string key in resources.Keys)
            if (!indexEntries.ContainsKey(key)) { reason = "resource-extra:" + key; return false; }

        // Full SHA verification of the whole payload happens before any JSON parse.
        foreach (var entry in indexEntries)
        {
            if (!PalaceResourceBytes.MatchesSha256(resources[entry.Key], entry.Value, out _))
            { reason = "resource-sha:" + entry.Key; return false; }
        }

        if (!TryStrictUtf8(resources, "registry.json", out string registryJson, out reason)) return false;
        if (!TryStrictUtf8(resources, "layout.json", out string layoutJson, out reason)) return false;
        if (!TryStrictUtf8(resources, "palette.json", out _, out reason)) return false;
        foreach (string key in indexEntries.Keys)
        {
            if (!key.EndsWith(".json", StringComparison.Ordinal)) continue;
            if (key == "registry.json" || key == "layout.json" || key == "palette.json") continue;
            if (!TryStrictUtf8(resources, key, out _, out reason)) return false;
        }

        if (!PalaceProductionSource.TryParse(registryJson, layoutJson, out PalaceProductionSource source, out string parseReason))
        { reason = "source:" + parseReason; return false; }
        if (source.NativeAccepted || source.PaidResourceGate) { reason = "source-status"; return false; }
        if (!string.Equals(source.RegistrySha256, indexEntries["registry.json"], StringComparison.OrdinalIgnoreCase))
        { reason = "registry-sha"; return false; }
        if (!string.Equals(source.LayoutSha256, indexEntries["layout.json"], StringComparison.OrdinalIgnoreCase))
        { reason = "layout-sha"; return false; }
        if (!string.Equals(source.PaletteSha256, indexEntries["palette.json"], StringComparison.OrdinalIgnoreCase))
        { reason = "palette-sha"; return false; }

        var assetsByPath = new Dictionary<string, PalaceProductionSource.Asset>(StringComparer.Ordinal);
        foreach (var asset in source.Assets)
            if (!assetsByPath.TryAdd(asset.RelativePath, asset))
            { reason = "duplicate-asset-path:" + asset.RelativePath; return false; }
        if (source.Assets.Count != PngResourceCount || assetsByPath.Count != PngResourceCount)
        { reason = "asset-count:" + source.Assets.Count; return false; }

        var pngKeys = new List<string>();
        foreach (string key in indexEntries.Keys)
            if (key.EndsWith(".png", StringComparison.Ordinal)) pngKeys.Add(key);
        if (pngKeys.Count != PngResourceCount) { reason = "png-count:" + pngKeys.Count; return false; }
        pngKeys.Sort(StringComparer.Ordinal);

        long rgbaBytes = 0;
        var pngByAssetId = new Dictionary<string, byte[]>(PngResourceCount, StringComparer.Ordinal);
        foreach (string key in pngKeys)
        {
            if (!assetsByPath.TryGetValue(key, out PalaceProductionSource.Asset asset))
            { reason = "png-asset-missing:" + key; return false; }
            if (!string.Equals(asset.DeclaredPngSha256, indexEntries[key], StringComparison.OrdinalIgnoreCase))
            { reason = "png-asset-sha:" + key; return false; }
            if (!PalaceResourceBytes.TryReadPngSize(resources[key], out int width, out int height, out string pngReason))
            { reason = "png:" + key + ":" + pngReason; return false; }
            if (width != asset.Width || height != asset.Height) { reason = "png-size:" + key; return false; }
            long pixels = checked((long)width * height);
            rgbaBytes = checked(rgbaBytes + pixels * 4);
            if (rgbaBytes > MaxPngRgbaBytes) { reason = "png-rgba-budget:" + rgbaBytes; return false; }
            if (!pngByAssetId.TryAdd(asset.Id, resources[key]))
            { reason = "duplicate-asset-id:" + asset.Id; return false; }
        }

        package = new PalaceCapitalPackage(source, pngByAssetId);
        return true;
    }

    private static bool CollectAvailableNames(IReadOnlyCollection<string> source, out HashSet<string> names, out string reason)
    {
        names = new HashSet<string>(StringComparer.Ordinal);
        reason = "";
        foreach (string name in source)
        {
            if (name == null) { reason = "name-null"; return false; }
            if (!name.StartsWith(Prefix, StringComparison.Ordinal)) continue; // other mod prefixes are unconstrained
            if (!names.Add(name)) { reason = "available-duplicate:" + name; return false; }
            if (names.Count > TotalResourceCount) { reason = "available-count"; return false; }
        }
        return true;
    }

    private static bool TryReadResource(Func<string, Stream> openResource, string logicalName, long maxBytes,
        out byte[] bytes, out string reason)
    {
        bytes = null;
        reason = "";
        Stream stream = null;
        bool readSucceeded = false;
        try
        {
            stream = openResource(logicalName);
            if (stream == null) reason = "stream-null";
            else if (!PalaceResourceBytes.TryReadBounded(stream, maxBytes, out bytes, out string readReason))
                reason = readReason;
            else readSucceeded = true;
        }
        catch (Exception e) { reason = "open-fault:" + e.GetType().Name; }
        finally
        {
            if (stream != null)
            {
                try { stream.Dispose(); }
                catch (Exception e)
                {
                    string disposal = "dispose-fault:" + e.GetType().Name;
                    reason = string.IsNullOrEmpty(reason) ? disposal : reason + ";" + disposal;
                    readSucceeded = false;
                }
            }
        }
        if (!readSucceeded) bytes = null;
        return readSucceeded;
    }

    /// <summary>Strict UTF-8; depth 4; duplicate keys, non-string/short hashes and unsafe paths rejected.</summary>
    internal static bool TryParseIndex(byte[] utf8, out IReadOnlyDictionary<string, string> entries, out string reason)
    {
        entries = null;
        reason = "";
        if (utf8 == null || utf8.Length == 0) { reason = "index-empty"; return false; }
        try { StrictUtf8.GetCharCount(utf8); }
        catch (DecoderFallbackException) { reason = "index-utf8"; return false; }
        try
        {
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
            {
                MaxDepth = MaxIndexDepth,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            { reason = "index-json"; return false; }
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            while (true)
            {
                if (!reader.Read()) { reason = "index-json"; return false; }
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType != JsonTokenType.PropertyName) { reason = "index-json"; return false; }
                string key = reader.GetString();
                if (!reader.Read() || reader.TokenType != JsonTokenType.String)
                { reason = "index-value"; return false; }
                string hash = reader.GetString();
                if (!SafeIndexPath(key) || string.Equals(key, IndexName, StringComparison.Ordinal))
                { reason = "index-path"; return false; }
                if (!IsSha256Hex(hash)) { reason = "index-hash"; return false; }
                if (!map.TryAdd(key, hash)) { reason = "index-duplicate"; return false; }
                if (map.Count > IndexedResourceCount) { reason = "index-count"; return false; }
            }
            if (reader.Read()) { reason = "index-trailing"; return false; }
            entries = map;
            return true;
        }
        catch (JsonException) { reason = "index-json"; return false; }
        catch (Exception e) { reason = "index-fault:" + e.GetType().Name; return false; }
    }

    private static bool TryStrictUtf8(IReadOnlyDictionary<string, byte[]> resources, string key,
        out string text, out string reason)
    {
        text = null;
        reason = "";
        if (!resources.TryGetValue(key, out byte[] bytes) || bytes == null)
        { reason = "resource-missing:" + key; return false; }
        try { text = StrictUtf8.GetString(bytes); return true; }
        catch (DecoderFallbackException) { reason = "utf8:" + key; return false; }
    }

    private static bool SafeIndexPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > MaxPathLength) return false;
        if (path[0] == '/' || path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0) return false;
        string[] segments = path.Split('/');
        foreach (string segment in segments)
        {
            if (segment.Length == 0 || segment == "." || segment == "..") return false;
            foreach (char c in segment)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9'
                    || c == '-' || c == '_' || c == '.')) return false;
        }
        return true;
    }

    private static bool IsSha256Hex(string hash)
    {
        if (hash == null || hash.Length != 64) return false;
        foreach (char c in hash)
            if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F')) return false;
        return true;
    }
}
