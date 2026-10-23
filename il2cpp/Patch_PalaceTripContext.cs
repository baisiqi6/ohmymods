// 单键 native prefs 行程记录（KEM.Palace.Trip.v1）：严格有界读写 + campaign 身份绑定。
// V1 = 原 11 keys 无锚记录（旧档原样读写，不迁移、不补锚）；V2 = 12 keys，含真实入口锚
// sourceEntryAnchor（PalaceEntryAnchorData，必须非 null 且内部一致）；V3 = 13 keys，= V2 的 12 keys +
// 唯一 sourceClockReceipt（真实 PalaceSourceClockReceipt 的 canonical JSON 串，必须与外层 tuple 全等：
// tripId/campaignGuid 按 ordinal 逐字符，与真实 Clock SameTripId / Scope.Equals 同语义，不做大小写归一）。
// V4 adds destinationLayoutId/version to V3 (15 keys); it requires a known immutable recipe.
// V1-V3 never gain layout keys or inferred defaults; V4 retains the V3 receipt/size contracts.
// schema 由 root 实际 version 决定，形态不互相塞字段：V1 带锚或带 receipt、V2 缺锚或带 receipt、V3 缺锚或
// 缺 receipt 都判 Invalid；receipt 键出现在 V1/V2（null 也一样）按未知键拒绝，不迁移、不补 receipt。
// TryParse 对 root、嵌套锚与 receipt 都做 exact key 复核（重复/未知/缺字段/类型/非有限/非整 side 全拒），
// 缺字段不会被 Json 默认成 0 当实测；锚与 receipt 的捕获由 Root 在既有旅程窗口接线，本文件不产生副作用。
// 上限：legacy（V1/V2）root JSON ≤ 1024 chars 原样保持；V3 root JSON ≤ 16384 chars 且 ≤ 16384 UTF8 bytes；
// 内层 receipt 串 ≤ 8192 UTF8 bytes 由 Clock 真实 codec（PalaceClockLimits.MaxReceiptBytes）自查。
// 只做身份读取（HeavyShieldIdentity 已存 campaignGuid/slot）与 strict System.Text.Json 记录，
// 不写钱包/PreviousLand/坐骑，不做迁移 seed（migrated 记录由 Root 在停游戏后人工插入）。
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KingdomEnhancedMod;

/// <summary>Single trip: V1=11 keys, V2=12 with anchor, V3=13 with receipt, V4=15 with known destination layout.</summary>
internal sealed class PalaceTripRecord
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("tripId")] public string TripId { get; set; } = "";
    [JsonPropertyName("campaignGuid")] public string CampaignGuid { get; set; } = "";
    [JsonPropertyName("campaignSlot")] public int CampaignSlot { get; set; } = -1;
    [JsonPropertyName("biome")] public int Biome { get; set; } = -1;
    [JsonPropertyName("reign")] public int Reign { get; set; } = -1;
    [JsonPropertyName("start")] public int Start { get; set; }
    [JsonPropertyName("sourceLand")] public int SourceLand { get; set; } = -1;
    [JsonPropertyName("palaceLand")] public int PalaceLand { get; set; } = PalaceLand12Runtime.LandIndex;
    [JsonPropertyName("phase")] public string Phase { get; set; } = "";
    [JsonPropertyName("migrationEvidence")] public string MigrationEvidence { get; set; } = "";
    /// <summary>V2 入口锚；V1 必须 null（serialize 时省略，保持旧 11 字段原形）。</summary>
    [JsonPropertyName("sourceEntryAnchor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PalaceEntryAnchorData SourceEntryAnchor { get; set; }
    /// <summary>V3 来源时钟收据（Clock canonical JSON 串）；V1/V2 必须 null（serialize 时省略）。</summary>
    [JsonPropertyName("sourceClockReceipt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string SourceClockReceipt { get; set; }
    // Layout is absent (unknown) in v1-v3, required and known in v4.
#nullable enable
    [JsonPropertyName("destinationLayoutId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DestinationLayoutId { get; set; }
    [JsonPropertyName("destinationLayoutVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DestinationLayoutVersion { get; set; }
    [JsonIgnore]
    public PalaceLayoutRecipe? DestinationLayout => Version == 4 && DestinationLayoutId != null && DestinationLayoutVersion.HasValue
        && PalaceLayoutRecipe.TryResolve(DestinationLayoutId, DestinationLayoutVersion.Value, out var recipe) ? recipe : null;
#nullable restore

}

/// <summary>
/// 记录的唯一读写入口：区分 missing / ReadFault / Invalid，绝不默认 origin。
/// 写入 = SetString + same-string readback + strict parsed tuple 复核（两项都要过）。
/// schema 按 version 严格分流：V1 无锚无 receipt / V2 必须带非 null 且内部一致的真实锚且无 receipt /
/// V3 必须带真实锚 + 与外层 tuple（ordinal 全等）绑定的真实 receipt（canonical 串）；本类不自造 receipt 事实。
/// </summary>
internal static class PalaceTripContext
{
    internal const string PrefsKey = "KEM.Palace.Trip.v1";
    internal const string PhasePalaceSelected = "PalaceSelected";
    internal const string PhaseReturnSelected = "ReturnSelected";
    private const int MaxJsonChars = 1024;      // legacy V1/V2 root JSON chars 上限（原语义不变）
    private const int MaxJsonCharsV3 = 16384;   // V3 root JSON chars 上限
    private const int MaxJsonBytesV3 = 16384;   // V3 root JSON UTF8 bytes 上限
    private const int MaxEvidenceChars = 128;

    private static readonly string[] ExpectedKeysV1 =
    {
        "version", "tripId", "campaignGuid", "campaignSlot", "biome", "reign",
        "start", "sourceLand", "palaceLand", "phase", "migrationEvidence"
    };

    private static readonly string[] ExpectedKeysV2 =
    {
        "version", "tripId", "campaignGuid", "campaignSlot", "biome", "reign",
        "start", "sourceLand", "palaceLand", "phase", "migrationEvidence", "sourceEntryAnchor"
    };

    private static readonly string[] ExpectedKeysV3 =
    {
        "version", "tripId", "campaignGuid", "campaignSlot", "biome", "reign",
        "start", "sourceLand", "palaceLand", "phase", "migrationEvidence", "sourceEntryAnchor",
        "sourceClockReceipt"
    };

    private static readonly string[] ExpectedKeysV4 =
    {
        "version", "tripId", "campaignGuid", "campaignSlot", "biome", "reign",
        "start", "sourceLand", "palaceLand", "phase", "migrationEvidence", "sourceEntryAnchor",
        "sourceClockReceipt", "destinationLayoutId", "destinationLayoutVersion"
    };

    private static readonly string[] AnchorKeys =
    {
        "entryX", "entryY", "entryZ", "sourcePlayerX", "standX", "side"
    };

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = false
    };

    internal enum ReadStatus { Ok, Missing, Fault, Invalid }

    /// <summary>全局 prefs 读一条记录；missing=键不存在，Fault=prefs/contents/异常，Invalid=解析/校验失败。</summary>
    internal static ReadStatus Read(GlobalSaveData global, out PalaceTripRecord record, out string detail)
    {
        record = null;
        detail = "";
        try
        {
            if (global == null) { detail = "global-missing"; return ReadStatus.Fault; }
            PrefsSaveData prefs = global.prefs;
            if (prefs == null) { detail = "prefs-missing"; return ReadStatus.Fault; }
            var contents = prefs.contents;
            if (contents == null) { detail = "contents-missing"; return ReadStatus.Fault; }
            if (!contents.TryGetValue(PrefsKey, out string json)) { detail = "key-missing"; return ReadStatus.Missing; }
            if (!JsonEnvelopeOk(json, out string sizeReason)) { detail = sizeReason; return ReadStatus.Invalid; }
            if (!TryParse(json, out record, out string reason)) { detail = reason; return ReadStatus.Invalid; }
            detail = "ok:" + json.Length;
            return ReadStatus.Ok;
        }
        catch (Exception e)
        {
            record = null;
            detail = "read-fault:" + e.GetType().Name;
            return ReadStatus.Fault;
        }
    }

    /// <summary>写入 + readback：SetString 后必须读回同一字符串且 parsed tuple 完全一致，否则失败（不掩盖）。</summary>
    internal static bool Write(GlobalSaveData global, PalaceTripRecord record, out string detail)
    {
        detail = "";
        try
        {
            if (global == null || record == null) { detail = "input"; return false; }
            if (!Validate(record, out string reason)) { detail = "record:" + reason; return false; }
            string json = JsonSerializer.Serialize(record, Options);
            if (!JsonEnvelopeOk(json, out string sizeReason)) { detail = sizeReason; return false; }
            if (!LegacyCharsOk(record.Version, json, out string legacyReason)) { detail = legacyReason; return false; }
            PrefsSaveData prefs = global.prefs;
            if (prefs == null) { detail = "prefs-missing"; return false; }
            if (prefs.contents == null) { detail = "contents-missing"; return false; }
            prefs.SetString(PrefsKey, json);
            var contents = prefs.contents;
            if (contents == null) { detail = "contents-missing-after-write"; return false; }
            if (!contents.TryGetValue(PrefsKey, out string readback)) { detail = "readback-missing"; return false; }
            if (!string.Equals(readback, json, StringComparison.Ordinal)) { detail = "readback-string"; return false; }
            if (!TryParse(readback, out PalaceTripRecord parsed, out string parseReason))
            { detail = "readback-parse:" + parseReason; return false; }
            if (!TupleEquals(parsed, record)) { detail = "readback-tuple"; return false; }
            detail = "ok:" + json.Length;
            return true;
        }
        catch (Exception e)
        {
            detail = "write-fault:" + e.GetType().Name;
            return false;
        }
    }

    /// <summary>
    /// campaign 身份绑定（只读 HeavyShieldIdentity 已存身份，不作为钱 Ready 门）：
    /// token.Global/Campaign == 当前实例、Guid valid、Current.Slot == global.currentCampaign。
    /// </summary>
    internal static bool TryBinding(GlobalSaveData global, CampaignSaveData campaign,
        out string campaignGuid, out int slot, out string reason)
    {
        campaignGuid = "";
        slot = -1;
        reason = "";
        try
        {
            if (global == null || campaign == null || campaign.Pointer == IntPtr.Zero)
            { reason = "context"; return false; }
            // Campaign identity is owned by the durable GUID map. A newly generated island can have
            // no HeavyShield money/session key yet; that must not prevent an existing campaign's travel.
            if (!PalaceLand12Runtime.TryReadSavedCampaignBinding(global, campaign, out string savedGuid, out int savedSlot, out string savedReason))
            { reason = "durable-guid-map:" + savedReason; return false; }
            if (!Guid.TryParse(savedGuid, out Guid guid) || guid == Guid.Empty)
            { reason = "guid-invalid"; return false; }
            if (HeavyShieldIdentity.TryGetCampaign(out HeavyShieldCampaignToken token)
                && (token.Global != global.Pointer || token.Campaign != campaign.Pointer || token.Guid != savedGuid))
            { reason = "live-campaign-token-mismatch"; return false; }
            campaignGuid = guid.ToString("D");
            slot = savedSlot;
            return true;
        }
        catch (Exception e)
        {
            reason = "binding-fault:" + e.GetType().Name;
            return false;
        }
    }

    /// <summary>回程目标存在性：existingCampaign.GetIsland(land) 为实在可读 native 记录（不新造 source）。</summary>
    internal static bool TryTargetIsland(CampaignSaveData campaign, int land, out string detail)
    {
        detail = "";
        try
        {
            if (campaign == null || land < 0 || land > 11) { detail = "land-range:" + land; return false; }
            var islands = campaign._islands;
            if (islands == null || land >= islands.Count) { detail = "island-slot-missing"; return false; }
            IslandSaveData island = islands[land];
            if (island == null || island.Pointer == IntPtr.Zero) { detail = "island-missing"; return false; }
            if (island.land != 0 && island.land != land) { detail = "island-land:" + island.land; return false; }
            if (island.objects == null || island.objects.Count == 0) { detail = "saved-island-empty"; return false; }
            detail = "island-ok:" + island.Pointer.ToInt64();
            return true;
        }
        catch (Exception e)
        {
            detail = "island-fault:" + e.GetType().Name;
            return false;
        }
    }

    /// <summary>记录与当前 campaign 的一致性约束（biome/reign/start 只为一致性，不是唯一 ID）。</summary>
    internal static bool RecordMatches(CampaignSaveData campaign, string campaignGuid, int slot,
        PalaceTripRecord record, out string reason)
    {
        reason = "";
        try
        {
            if (campaign == null || record == null) { reason = "input"; return false; }
            if (!Guid.TryParse(record.CampaignGuid, out Guid recordGuid) || recordGuid == Guid.Empty)
            { reason = "record-guid"; return false; }
            if (!Guid.TryParse(campaignGuid, out Guid boundGuid) || recordGuid != boundGuid)
            { reason = "record-guid-mismatch"; return false; }
            if (record.CampaignSlot != slot) { reason = "record-slot"; return false; }
            if (record.Biome != campaign.biomeIndex) { reason = "record-biome"; return false; }
            if (record.Reign != campaign.reign) { reason = "record-reign"; return false; }
            if (record.Start != campaign.realStartDateTime) { reason = "record-start"; return false; }
            return true;
        }
        catch (Exception e)
        {
            reason = "record-fault:" + e.GetType().Name;
            return false;
        }
    }

    internal static bool TupleEquals(PalaceTripRecord a, PalaceTripRecord b)
    {
        if (a == null || b == null) return false;
        if ((a.Version == 4 || b.Version == 4)
            && (!PalaceLayoutRecipe.IsKnown(a.DestinationLayout) || !PalaceLayoutRecipe.IsKnown(b.DestinationLayout))) return false;
        return a.Version == b.Version
            && string.Equals(a.TripId, b.TripId, StringComparison.Ordinal)
            && string.Equals(a.CampaignGuid, b.CampaignGuid, StringComparison.Ordinal)
            && a.CampaignSlot == b.CampaignSlot
            && a.Biome == b.Biome
            && a.Reign == b.Reign
            && a.Start == b.Start
            && a.SourceLand == b.SourceLand
            && a.PalaceLand == b.PalaceLand
            && string.Equals(a.Phase, b.Phase, StringComparison.Ordinal)
            && string.Equals(a.MigrationEvidence, b.MigrationEvidence, StringComparison.Ordinal)
            && string.Equals(a.DestinationLayoutId, b.DestinationLayoutId, StringComparison.Ordinal)
            && a.DestinationLayoutVersion == b.DestinationLayoutVersion
            && AnchorEquals(a.Version, a.SourceEntryAnchor, b.SourceEntryAnchor)
            && ReceiptEquals(a, b);
    }

    internal static bool Validate(PalaceTripRecord r, out string reason)
    {
        reason = "";
        if (r == null) { reason = "null"; return false; }
        if (r.Version != 1 && r.Version != 2 && r.Version != 3 && r.Version != 4) { reason = "version:" + r.Version; return false; }
        if (r.Version == 4)
        {
            if (!PalaceLayoutRecipe.IsKnown(r.DestinationLayout)) { reason = "v4-layout-unknown"; return false; }
        }
        else if (r.DestinationLayoutId != null || r.DestinationLayoutVersion != null)
        { reason = "legacy-layout-not-null"; return false; }
        if (!ValidGuidD(r.TripId)) { reason = "tripId"; return false; }
        if (!ValidGuidD(r.CampaignGuid)) { reason = "campaignGuid"; return false; }
        if (r.CampaignSlot < 0) { reason = "campaignSlot:" + r.CampaignSlot; return false; }
        if (r.Biome < 0) { reason = "biome:" + r.Biome; return false; }
        if (r.Reign < 0) { reason = "reign:" + r.Reign; return false; }
        if (r.SourceLand < 0 || r.SourceLand > 11) { reason = "sourceLand:" + r.SourceLand; return false; }
        if (r.PalaceLand != PalaceLand12Runtime.LandIndex) { reason = "palaceLand:" + r.PalaceLand; return false; }
        if (r.Phase != PhasePalaceSelected && r.Phase != PhaseReturnSelected) { reason = "phase:" + r.Phase; return false; }
        if (r.MigrationEvidence == null || r.MigrationEvidence.Length > MaxEvidenceChars || !PrintableAscii(r.MigrationEvidence))
        { reason = "migrationEvidence"; return false; }
        if (r.Version == 1)
        {
            if (r.SourceEntryAnchor != null) { reason = "v1-anchor-not-null"; return false; }
            if (r.SourceClockReceipt != null) { reason = "v1-receipt-not-null"; return false; }
        }
        else
        {
            if (r.SourceEntryAnchor == null)
            { reason = (r.Version == 2 ? "v2-anchor-missing" : "v3-anchor-missing"); return false; }
            if (!PalaceReturnAnchor.ValidateData(r.SourceEntryAnchor, out string anchorReason))
            { reason = "anchor:" + anchorReason; return false; }
            if (r.Version == 2)
            {
                if (r.SourceClockReceipt != null) { reason = "v2-receipt-not-null"; return false; }
            }
            else if (!TryReceiptEvidence(r, out _, out string receiptReason))
            { reason = receiptReason; return false; }
        }
        return true;
    }

    /// <summary>
    /// 严格解析：按 root 实际 version 选 schema（1→11 keys 无锚；2→12 keys 带锚；3→13 keys 带锚 + receipt），
    /// root/锚/receipt 都拒绝重复/未知/缺字段/类型不符；锚六字段必须齐全，坐标有限可表达为 float，side 为
    /// 整数 ±1；V3 receipt 必须是字符串并经真实 Clock strict codec 验证 + canonical 归一化，且与外层 tuple
    /// ordinal 全等（receipt 内 8KiB/字段形状由 Clock codec 自持）；取值一致性交 Validate 复核。上限：入口包络
    /// ≤16384 chars 且 ≤16384 UTF8 bytes，legacy（V1/V2）另受原 1024 chars 限制。internal 仅供 Root 做
    /// CLR 边界解析检查，读写路径不变。
    /// </summary>
    internal static bool TryParse(string json, out PalaceTripRecord record, out string reason)
    {
        record = null;
        reason = "";
        if (!JsonEnvelopeOk(json, out reason)) return false;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { reason = "not-object"; return false; }
            int count = 0;
            bool versionSeen = false;
            int version = 0;
            JsonElement anchorElement = default;
            JsonElement receiptElement = default;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                count++;
                if (!names.Add(property.Name)) { reason = "duplicate-key:" + property.Name; return false; }
                if (property.Name == "version")
                {
                    versionSeen = true;
                    if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out version))
                    { reason = "version-type"; return false; }
                }
                else if (property.Name == "sourceEntryAnchor")
                {
                    anchorElement = property.Value;
                }
                else if (property.Name == "sourceClockReceipt")
                {
                    receiptElement = property.Value;
                }
            }
            if (!versionSeen) { reason = "version-missing"; return false; }
            if (version != 1 && version != 2 && version != 3 && version != 4) { reason = "version:" + version; return false; }
            if (!LegacyCharsOk(version, json, out reason)) return false;
            string[] expected = version == 1 ? ExpectedKeysV1 : version == 2 ? ExpectedKeysV2 : version == 3 ? ExpectedKeysV3 : ExpectedKeysV4;
            if (count != expected.Length) { reason = "key-count:" + count; return false; }
            foreach (string name in names)
            {
                bool known = false;
                for (int i = 0; i < expected.Length; i++)
                    if (expected[i] == name) { known = true; break; }
                if (!known) { reason = "unknown-key:" + name; return false; }
            }
            if (version >= 2 && !CheckAnchorShape(anchorElement, out reason)) return false;
            if ((version == 3 || version == 4) && !CheckReceiptShape(receiptElement, out reason)) return false;
            PalaceTripRecord parsed = JsonSerializer.Deserialize<PalaceTripRecord>(json, Options);
            if (parsed == null) { reason = "deserialize-null"; return false; }
            if (version == 3 || version == 4)
            {
                if (!TryDecodeReceipt(parsed.SourceClockReceipt, out PalaceSourceClockReceipt receipt, out string receiptReason))
                { reason = "receipt:" + receiptReason; return false; }
                parsed.SourceClockReceipt = receipt.WriteCanonicalStrict();   // 归一化为 Clock canonical 串
            }
            if (!Validate(parsed, out reason)) return false;
            record = parsed;
            return true;
        }
        catch (Exception e)
        {
            reason = "parse-fault:" + e.GetType().Name;
            return false;
        }
    }

    /// <summary>
    /// 嵌套锚结构复核：必须 object 且 exact 六个 lowerCamel keys（重复/未知/缺字段全拒）；五个坐标必须是
    /// 有限且可表达为 float 的 number；side 必须是整数 ±1（小数/指数形式一律拒，与反序列化同域）。
    /// 缺字段不会落到 Json 默认 0 被当实测。
    /// </summary>
    private static bool CheckAnchorShape(JsonElement anchor, out string reason)
    {
        reason = "";
        if (anchor.ValueKind != JsonValueKind.Object) { reason = "anchor-not-object"; return false; }
        int count = 0;
        JsonElement sideElement = default;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in anchor.EnumerateObject())
        {
            count++;
            if (!names.Add(property.Name)) { reason = "anchor-duplicate-key:" + property.Name; return false; }
            bool known = false;
            for (int i = 0; i < AnchorKeys.Length; i++)
                if (AnchorKeys[i] == property.Name) { known = true; break; }
            if (!known) { reason = "anchor-unknown-key:" + property.Name; return false; }
            if (property.Name == "side") { sideElement = property.Value; }
            else if (!JsonFiniteFloat(property.Value))
            { reason = "anchor-field:" + property.Name; return false; }
        }
        if (count != AnchorKeys.Length) { reason = "anchor-key-count:" + count; return false; }
        if (sideElement.ValueKind != JsonValueKind.Number
            || !sideElement.TryGetInt32(out int side) || (side != 1 && side != -1))
        { reason = "anchor-side"; return false; }
        return true;
    }

    /// <summary>坐标类 number：有限且能表达为 float（NaN/Infinity/超范围一律拒）。</summary>
    private static bool JsonFiniteFloat(JsonElement element)
        => element.ValueKind == JsonValueKind.Number
            && element.TryGetSingle(out float value) && float.IsFinite(value);

    /// <summary>锚逐字段 exact 有限 float 等值；两个 null 只在 V1 可相等（V2 的 null 锚已被 Validate 拒绝）。</summary>
    private static bool AnchorEquals(int version, PalaceEntryAnchorData x, PalaceEntryAnchorData y)
    {
        if (x == null && y == null) return version == 1;
        if (x == null || y == null) return false;
        return EqualFinite(x.EntryX, y.EntryX)
            && EqualFinite(x.EntryY, y.EntryY)
            && EqualFinite(x.EntryZ, y.EntryZ)
            && EqualFinite(x.SourcePlayerX, y.SourcePlayerX)
            && EqualFinite(x.StandX, y.StandX)
            && x.Side == y.Side;
    }

    private static bool EqualFinite(float x, float y)
        => x == y && !float.IsNaN(x) && !float.IsInfinity(x);

    /// <summary>V3 receipt 结构形状：必须是 string 且非空（深层 strict 校验在 Validate 经真实 Clock codec）。</summary>
    private static bool CheckReceiptShape(JsonElement receipt, out string reason)
    {
        reason = "";
        if (receipt.ValueKind != JsonValueKind.String) { reason = "receipt-type"; return false; }
        if (string.IsNullOrEmpty(receipt.GetString())) { reason = "receipt-empty"; return false; }
        return true;
    }

    /// <summary>
    /// 窄 receipt 解码：真实 PalaceSourceClockReceipt.TryParseStrict（8KiB/字段形状/canonical 规则由 Clock
    /// codec 自持，本文件不复制重写）。只补接结构解码异常（lone escaped surrogate 触发的
    /// InvalidOperationException 与 JsonException）按失败返回；不 catch 环境致命异常基类，不内置任何默认值。
    /// </summary>
    private static bool TryDecodeReceipt(string text, out PalaceSourceClockReceipt receipt, out string reason)
    {
        receipt = null;
        reason = "";
        if (string.IsNullOrEmpty(text)) { reason = "empty"; return false; }
        try
        {
            if (!PalaceSourceClockReceipt.TryParseStrict(text, out receipt, out string reject))
            { receipt = null; reason = "strict:" + reject; return false; }
            return true;
        }
        catch (JsonException e)
        {
            receipt = null;
            reason = "json:" + e.GetType().Name;
            return false;
        }
        catch (InvalidOperationException e)
        {
            receipt = null;
            reason = "token:" + e.GetType().Name;
            return false;
        }
    }

    /// <summary>
    /// V3 绑定：receipt 的 tripId/campaignGuid 按 ordinal 逐字符比较（与真实 Clock SameTripId /
    /// Scope.Equals 同语义，不做大小写归一），campaignSlot/biome/reign/start/sourceLand 精确相等；
    /// start 是 opaque int 全域（0/负值合法，未知不在此表达）。
    /// </summary>
    private static bool ReceiptBindsOuter(PalaceTripRecord r, PalaceSourceClockReceipt receipt, out string reason)
    {
        reason = "";
        if (!string.Equals(r.TripId, receipt.TripId, StringComparison.Ordinal))
        { reason = "tripId"; return false; }
        if (!string.Equals(r.CampaignGuid, receipt.Scope.CampaignGuid, StringComparison.Ordinal))
        { reason = "campaignGuid"; return false; }
        if (receipt.Scope.CampaignSlot != r.CampaignSlot) { reason = "campaignSlot"; return false; }
        if (receipt.Scope.Biome != r.Biome) { reason = "biome"; return false; }
        if (receipt.Scope.Reign != r.Reign) { reason = "reign"; return false; }
        if (receipt.Scope.Start != r.Start) { reason = "start"; return false; }
        if (receipt.Scope.SourceLand != r.SourceLand) { reason = "sourceLand"; return false; }
        return true;
    }

    /// <summary>
    /// V3 receipt 完整证据：非空、经真实 Clock codec 合法、且与外层 tuple（ordinal）全等；reason 与
    /// Validate 的既有文案一致（v3-receipt-missing / receipt:* / receipt-binding:*）。不复制 codec。
    /// </summary>
    private static bool TryReceiptEvidence(PalaceTripRecord r, out PalaceSourceClockReceipt receipt, out string reason)
    {
        receipt = null;
        reason = "";
        if (r == null) { reason = "record-null"; return false; }
        if (r.Version != 3 && r.Version != 4) { reason = "version:" + r.Version; return false; }
        if (string.IsNullOrEmpty(r.SourceClockReceipt)) { reason = "v3-receipt-missing"; return false; }
        if (!TryDecodeReceipt(r.SourceClockReceipt, out receipt, out string receiptReason))
        { reason = "receipt:" + receiptReason; return false; }
        if (!ReceiptBindsOuter(r, receipt, out string bindReason))
        { reason = "receipt-binding:" + bindReason; return false; }
        return true;
    }

    /// <summary>
    /// tuple 层 receipt 语义：只有两侧 version ∈ {1,2} 且 SourceClockReceipt 严格为 null 才相等（空串或任何
    /// 非 null 收据都不相等）；否则两侧都必须带与各自外层 tuple 全等的 V3 真实 receipt，再比 canonical 字节
    /// （等语义不同转义/空白相等）。缺失/非法/未绑定 receipt 一律不等。
    /// </summary>
    private static bool ReceiptEquals(PalaceTripRecord a, PalaceTripRecord b)
    {
        if (a == null || b == null) return false;
        if (a.SourceClockReceipt == null && b.SourceClockReceipt == null)
            return (a.Version == 1 || a.Version == 2) && (b.Version == 1 || b.Version == 2);
        if (a.SourceClockReceipt == null || b.SourceClockReceipt == null) return false;
        if (!TryReceiptEvidence(a, out PalaceSourceClockReceipt ra, out _)) return false;
        if (!TryReceiptEvidence(b, out PalaceSourceClockReceipt rb, out _)) return false;
        return string.Equals(ra.WriteCanonicalStrict(), rb.WriteCanonicalStrict(), StringComparison.Ordinal);
    }

    /// <summary>入口包络（Read/Write/TryParse 共用）：非空、≤16384 chars、≤16384 UTF8 bytes。</summary>
    internal static bool JsonEnvelopeOk(string json, out string reason)
    {
        reason = "";
        if (string.IsNullOrEmpty(json)) { reason = "json-size:" + (json != null ? json.Length : -1); return false; }
        if (json.Length > MaxJsonCharsV3) { reason = "json-size:" + json.Length; return false; }
        int bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > MaxJsonBytesV3) { reason = "json-bytes:" + bytes; return false; }
        return true;
    }

    /// <summary>legacy（V1/V2）原 1024 chars 上限保持；V3 不受此限（只受入口包络 16384 约束）。</summary>
    internal static bool LegacyCharsOk(int version, string json, out string reason)
    {
        reason = "";
        if (version < 3 && json.Length > MaxJsonChars) { reason = "json-size-legacy:" + json.Length; return false; }
        return true;
    }

    private static bool ValidGuidD(string text)
        => !string.IsNullOrEmpty(text) && text.Length == 36
            && Guid.TryParseExact(text, "D", out Guid parsed) && parsed != Guid.Empty;

    private static bool PrintableAscii(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c < 0x20 || c > 0x7E) return false;
        }
        return true;
    }
}
