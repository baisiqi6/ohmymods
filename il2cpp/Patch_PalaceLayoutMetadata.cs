// Pure metadata/geometry readers. CourtArea remains the only sidecar IO and publication owner.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KingdomEnhancedMod;

internal sealed class PalaceCourtAreaMetadata
{
    [JsonPropertyName("v")] public int V { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("campaignGuid")] public string CampaignGuid { get; set; } = "";
    [JsonPropertyName("campaignSlot")] public int CampaignSlot { get; set; } = -1;
    [JsonPropertyName("reign")] public int Reign { get; set; } = -1;
    [JsonPropertyName("nativeEdgesMin")] public int NativeEdgesMin { get; set; }
    [JsonPropertyName("nativeEdgesMax")] public int NativeEdgesMax { get; set; }
    [JsonPropertyName("left")] public double Left { get; set; }
    [JsonPropertyName("right")] public double Right { get; set; }
    [JsonPropertyName("blockWidth")] public int BlockWidth { get; set; }
    [JsonPropertyName("addedBlocks")] public int AddedBlocks { get; set; }
    [JsonPropertyName("layoutId")] public string LayoutId { get; set; } = "";
    [JsonPropertyName("layoutVersion")] public int LayoutVersion { get; set; }
}

internal static class PalaceLayoutMetadata
{
    internal const int MaxJsonChars = 2048;
    private static readonly string[] OldKeys = { "v", "kind", "campaignGuid", "campaignSlot", "reign", "nativeEdgesMin", "nativeEdgesMax", "left", "right", "blockWidth", "addedBlocks" };

    internal static bool TryParse(string json, out PalaceCourtAreaMetadata metadata, out string reason)
        => TryParse(json, out metadata, out _, out reason);

    internal static bool TryParse(string json, out PalaceCourtAreaMetadata metadata,
        out PalaceLayoutRecipe recipe, out string reason)
    {
        metadata = null; recipe = null; reason = "";
        if (string.IsNullOrEmpty(json) || json.Length > MaxJsonChars) { reason = "size"; return false; }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { reason = "not-object"; return false; }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty p in root.EnumerateObject())
                if (!names.Add(p.Name)) { reason = "duplicate-key:" + p.Name; return false; }
            if (!root.TryGetProperty("v", out JsonElement v) || !v.TryGetInt32(out int schema)
                || (schema != 1 && schema != 2 && schema != 3)) { reason = "version"; return false; }
            foreach (string name in names)
            {
                bool known = Array.IndexOf(OldKeys, name) >= 0 || (schema == 3 && (name == "layoutId" || name == "layoutVersion"));
                if (!known) { reason = "unknown-key:" + name; return false; }
            }
            if (names.Count != (schema == 3 ? 13 : 11)) { reason = "key-count:" + names.Count; return false; }
            PalaceCourtAreaMetadata parsed = JsonSerializer.Deserialize<PalaceCourtAreaMetadata>(json);
            if (!Validate(parsed, out recipe, out reason)) return false;
            metadata = parsed;
            return true;
        }
        catch (JsonException) { reason = "json"; return false; }
        catch (InvalidOperationException) { reason = "type"; return false; }
    }

    internal static bool Validate(PalaceCourtAreaMetadata m, out PalaceLayoutRecipe recipe, out string reason)
    {
        recipe = null; reason = "";
        if (m == null) { reason = "null"; return false; }
        if (!ValidGuidD(m.CampaignGuid)) { reason = "campaignGuid"; return false; }
        if (m.CampaignSlot < 0 || m.Reign < 0) { reason = "owner"; return false; }
        if (m.NativeEdgesMin > m.NativeEdgesMax) { reason = "edges"; return false; }
        if (!double.IsFinite(m.Left) || !double.IsFinite(m.Right) || !(m.Left < m.Right)) { reason = "range"; return false; }
        if (m.V == 1)
        {
            if (m.Kind != "palace-court-area" || m.LayoutId != "" || m.LayoutVersion != 0) { reason = "legacy-kind-layout"; return false; }
            if (Math.Abs(m.Right - m.Left - 48) > .000001 || m.BlockWidth <= 0 || m.BlockWidth > 48
                || 48 % m.BlockWidth != 0 || m.AddedBlocks != 48 / m.BlockWidth) { reason = "legacy48"; return false; }
            return true; // Added region, not a whole-island known recipe.
        }
        if (m.Kind != "palace-dedicated-island") { reason = "kind"; return false; }
        if (m.V == 2 && m.LayoutId == "" && m.LayoutVersion == 0) recipe = PalaceLayoutRecipe.Known44;
        else if (m.V == 3 && PalaceLayoutRecipe.TryResolve(m.LayoutId, m.LayoutVersion, out PalaceLayoutRecipe resolved)
            && ReferenceEquals(resolved, PalaceLayoutRecipe.Capital400)) recipe = resolved;
        else { reason = "schema-layout"; return false; }
        if (m.Left != recipe.Left || m.Right != recipe.Right || m.NativeEdgesMin != recipe.Left
            || m.NativeEdgesMax != recipe.Right || m.BlockWidth != recipe.TileWidth || m.AddedBlocks != recipe.TileCount)
        { recipe = null; reason = "fixed-recipe"; return false; }
        return true;
    }

    internal static bool TrySerialize(PalaceCourtAreaMetadata m, out string json, out string reason)
    {
        json = null;
        if (!Validate(m, out _, out reason)) return false;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteNumber("v", m.V); writer.WriteString("kind", m.Kind);
            writer.WriteString("campaignGuid", m.CampaignGuid); writer.WriteNumber("campaignSlot", m.CampaignSlot);
            writer.WriteNumber("reign", m.Reign); writer.WriteNumber("nativeEdgesMin", m.NativeEdgesMin);
            writer.WriteNumber("nativeEdgesMax", m.NativeEdgesMax); writer.WriteNumber("left", m.Left);
            writer.WriteNumber("right", m.Right); writer.WriteNumber("blockWidth", m.BlockWidth); writer.WriteNumber("addedBlocks", m.AddedBlocks);
            if (m.V == 3) { writer.WriteString("layoutId", m.LayoutId); writer.WriteNumber("layoutVersion", m.LayoutVersion); }
            writer.WriteEndObject();
        }
        json = Encoding.UTF8.GetString(stream.ToArray());
        if (json.Length > MaxJsonChars) { json = null; reason = "size"; return false; }
        return true;
    }

    internal static bool ContentEquals(PalaceCourtAreaMetadata a, PalaceCourtAreaMetadata b)
        => a != null && b != null && a.V == b.V && a.Kind == b.Kind && a.CampaignGuid == b.CampaignGuid
            && a.CampaignSlot == b.CampaignSlot && a.Reign == b.Reign && a.NativeEdgesMin == b.NativeEdgesMin
            && a.NativeEdgesMax == b.NativeEdgesMax && a.Left == b.Left && a.Right == b.Right
            && a.BlockWidth == b.BlockWidth && a.AddedBlocks == b.AddedBlocks
            && a.LayoutId == b.LayoutId && a.LayoutVersion == b.LayoutVersion;

    private static bool ValidGuidD(string text) => !string.IsNullOrEmpty(text) && text.Length == 36
        && Guid.TryParseExact(text, "D", out Guid parsed) && parsed != Guid.Empty;
}

internal readonly struct PalaceTileSpan
{
    internal readonly float Left, Right;
    internal PalaceTileSpan(float left, float right) { Left = left; Right = right; }
}

internal static class PalaceLayoutCoverage
{
    // Reads a frozen native IslandSaveData JSON projection, never a file or native object.
    internal static bool TryValidateSavedGeometry(string json, PalaceLayoutRecipe recipe, out string reason)
    {
        reason="";
        if (!PalaceLayoutRecipe.IsKnown(recipe) || string.IsNullOrEmpty(json) || json.Length>64*1024*1024)
        { reason="saved-input"; return false; }
        try
        {
            using JsonDocument doc=JsonDocument.Parse(json,new JsonDocumentOptions { MaxDepth=64 });
            JsonElement root=doc.RootElement;
            if (!NoDuplicates(root) || !root.TryGetProperty("objects",out var objects)
                || objects.ValueKind!=JsonValueKind.Array || objects.GetArrayLength()>8192)
            { reason="saved-objects"; return false; }
            var ids=new HashSet<string>(StringComparer.Ordinal);
            var spans=new List<PalaceTileSpan>(); int groundCount=0;
            foreach (var row in objects.EnumerateArray())
            {
                if (!row.TryGetProperty("uniqueID",out var uid) || uid.ValueKind!=JsonValueKind.String
                    || string.IsNullOrEmpty(uid.GetString()) || !ids.Add(uid.GetString()))
                { reason="saved-uniqueID"; return false; }
                string path=row.GetProperty("prefabPath").GetString();
                JsonElement allComponents=row.GetProperty("componentData2");
                if(allComponents.ValueKind!=JsonValueKind.Array) { reason="saved-components"; return false; }
                foreach(var component in allComponents.EnumerateArray())
                {
                    string componentType=component.GetProperty("type").GetString();
                    if ((componentType=="GroundData" && path!="Prefabs/Environment/Ground")
                        || (componentType=="TileData" && path!="Prefabs/Environment/Tile Big"))
                    { reason="saved-foreign-geometry"; return false; }
                }
                if (path!="Prefabs/Environment/Ground" && path!="Prefabs/Environment/Tile Big") continue;
                JsonElement position=row.GetProperty("localPosition"), scale=row.GetProperty("localScale");
                if (!Vector(scale,1,1,1) || !FiniteNumber(position,"x",out double x)
                    || !FiniteNumber(position,"y",out double y) || y!=0 || !FiniteNumber(position,"z",out _))
                { reason="saved-transform"; return false; }
                JsonElement components=row.GetProperty("componentData2");
                if (components.ValueKind!=JsonValueKind.Array) { reason="saved-components"; return false; }
                string componentName=path=="Prefabs/Environment/Ground"?"Ground":"Tile";
                string type=componentName=="Ground"?"GroundData":"TileData";
                int matches=0;
                foreach (var component in components.EnumerateArray())
                {
                    if (component.GetProperty("name").GetString()!=componentName || component.GetProperty("type").GetString()!=type) continue;
                    matches++;
                    string data=component.GetProperty("data").GetString();
                    if (data==null || data.Length>8192) { reason="saved-data-size"; return false; }
                    using JsonDocument inner=JsonDocument.Parse(data);
                    JsonElement facts=inner.RootElement;
                    if (!NoDuplicates(facts)) { reason="saved-data-duplicate"; return false; }
                    if (componentName=="Ground")
                    {
                        if (x!=0 || !Vector(position,0,0,0)
                            || !Vector2(facts.GetProperty("colliderOffset"),0,.4375)
                            || !Vector2(facts.GetProperty("colliderSize"),recipe.Width,.875))
                        { reason="saved-ground-shape"; return false; }
                    }
                    else if (!facts.TryGetProperty("noGround",out var noGround) || noGround.ValueKind!=JsonValueKind.False)
                    { reason="saved-tile-noGround"; return false; }
                }
                if (matches!=1) { reason="saved-component-count"; return false; }
                if (componentName=="Ground") groundCount++;
                else
                {
                    double index=(x-recipe.Left)/recipe.TileWidth;
                    if(index<0 || index>=recipe.TileCount || index!=Math.Truncate(index))
                    { reason="saved-tile-grid"; return false; }
                    spans.Add(new PalaceTileSpan((float)x,(float)x+recipe.TileWidth));
                }
                // Saved TileData has no width. This verifies saved X/grid; live GetRight still proves the real width.
            }
            if (groundCount!=1) { reason="saved-ground-count"; return false; }
            return TryValidate(recipe,spans,spans.Count,out reason);
        }
        catch (JsonException) { reason="saved-json"; return false; }
        catch (InvalidOperationException) { reason="saved-type"; return false; }
        catch (KeyNotFoundException) { reason="saved-field"; return false; }
    }

    private static bool FiniteNumber(JsonElement obj,string key,out double value)
    {
        value=0;
        return obj.ValueKind==JsonValueKind.Object && obj.TryGetProperty(key,out var number)
            && number.ValueKind==JsonValueKind.Number && number.TryGetDouble(out value) && double.IsFinite(value);
    }
    private static bool Vector2(JsonElement e,double x,double y)
        => FiniteNumber(e,"x",out double vx)&&FiniteNumber(e,"y",out double vy)&&vx==x&&vy==y;
    private static bool Vector(JsonElement e,double x,double y,double z)
        => Vector2(e,x,y)&&FiniteNumber(e,"z",out double vz)&&vz==z;
    private static bool NoDuplicates(JsonElement e)
    {
        if (e.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in e.EnumerateObject()) if(!names.Add(p.Name)||!NoDuplicates(p.Value))return false;
        }
        else if(e.ValueKind==JsonValueKind.Array)
            foreach(var child in e.EnumerateArray())if(!NoDuplicates(child))return false;
        return true;
    }

    // The adapter supplies actual native Tile.GetLeft/GetRight spans; no assumed tile width is filled in.
    internal static bool TryValidate(PalaceLayoutRecipe recipe, IReadOnlyList<PalaceTileSpan> spans,
        int actualTileCount, out string reason)
    {
        reason = "";
        if (!PalaceLayoutRecipe.IsKnown(recipe) || spans == null || actualTileCount != recipe.TileCount || spans.Count != recipe.TileCount)
        { reason = "tile-count-or-recipe"; return false; }
        var sorted = new List<PalaceTileSpan>(spans);
        sorted.Sort((a, b) => a.Left.CompareTo(b.Left));
        for (int i = 0; i < sorted.Count; i++)
            if (!float.IsFinite(sorted[i].Left) || !float.IsFinite(sorted[i].Right)
                || sorted[i].Left != recipe.TileLeft(i) || sorted[i].Right != recipe.TileLeft(i) + recipe.TileWidth)
            { reason = "tile-segment:" + i.ToString(CultureInfo.InvariantCulture); return false; }
        return true;
    }
}
