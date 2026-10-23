// 宫廷本地日历 + 来源时钟收据：纯值 / 纯策略（无 Unity / BepInEx / Harmony 引用，可 source-link 进测试项目）。
//
// 本文件承载三块职责的纯决策部分（已批准合同 exact-gate-verdict APPROVE_EXACT_PALACE_VISIT_CLOCK_SOURCE）：
// 1. PalaceSourceClockReceipt：出发 source checkpoint 的严格值收据 + strict codec，供 Root 随 TripV3 持久化。
// 2. PalaceLocalDayPolicy / PalaceClockAnchor / PalaceClockPolicyAdapter：宫廷 own12 Director 的局部日历
//    （复用 C5 纯算法：getter 服务 localday、setter 吸收翻日、ApplyData 从真实宫廷 DirectorData 恢复、
//    raw 锚冻结观察；原 campaign raw 两数只读不写）。
// 3. PalaceClockReturnArm / PalaceReturnCallState：回程在真实 IslandSaveData.TryPopObjectsToScene 之前的单次
//    lastPlayedTimeDays 派生策略（同 trip/来源锚、单次、skip/漂移零写、throw 保责任；结果只记当次调用 arm）。
//
// 本文件不触碰任何 native 对象；引擎翻译层在 Patch_PalaceVisitClock.cs。收据内嵌哈希只保 shape/身份一致性，
// 不是 native 事实认证（native 捕获与物理核对由 Root 的 capture 桥提供 facts）。

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KingdomEnhancedMod;

internal static class PalaceClockLimits
{
    internal const int ReceiptVersion = 1;
    internal const int MaxReceiptBytes = 8192;      // Root TripV3 有界上限（8KiB）
    internal const int MaxTripIdChars = 64;
    internal const int ShaHexChars = 64;
    internal const int MaxGuidChars = 36;
}

/// <summary>
/// 一次旅程的 owner/reign 范围（与 W 的 PalaceTripRecord 同源字段：CampaignGuid/CampaignSlot/Biome/
/// Reign/Start(campaign.realStartDateTime)/SourceLand）。cold 的 owner/receipt 比对必须包含 Start。
/// </summary>
internal readonly struct PalaceClockScope : IEquatable<PalaceClockScope>
{
    internal readonly string CampaignGuid;
    internal readonly int CampaignSlot;
    internal readonly int Biome;
    internal readonly int Reign;
    internal readonly int Start;            // campaign.realStartDateTime（native int；不做 DateTime/Ticks 转换）
    internal readonly int SourceLand;

    internal PalaceClockScope(string campaignGuid, int campaignSlot, int biome, int reign, int start,
        int sourceLand)
    {
        CampaignGuid = campaignGuid;
        CampaignSlot = campaignSlot;
        Biome = biome;
        Reign = reign;
        Start = start;
        SourceLand = sourceLand;
    }

    internal bool ShapeOk()
        => !string.IsNullOrEmpty(CampaignGuid)
           && CampaignSlot >= 0 && Biome >= 0 && Reign >= 0
           && SourceLand >= 0 && SourceLand <= 11;

    /// <summary>semantic owner（Root 门内读取的 guid/start）与 typed slot/biome/reign 是否与本 scope 全等。
    /// guid 按值比较（十六进制大小写不敏感），start/slot/biome/reign 精确相等。</summary>
    internal bool MatchesSemanticOwner(string guid, int slot, int biome, int reign, int start)
        => guid != null
           && string.Equals(CampaignGuid, guid, StringComparison.OrdinalIgnoreCase)
           && CampaignSlot == slot && Biome == biome && Reign == reign && Start == start;

    public bool Equals(PalaceClockScope other)
        => string.Equals(CampaignGuid, other.CampaignGuid, StringComparison.Ordinal)
           && CampaignSlot == other.CampaignSlot && Biome == other.Biome && Reign == other.Reign
           && Start == other.Start && SourceLand == other.SourceLand;

    public override bool Equals(object obj) => obj is PalaceClockScope other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (CampaignGuid != null ? CampaignGuid.GetHashCode() : 0);
            hash = hash * 31 + CampaignSlot;
            hash = hash * 31 + Biome;
            hash = hash * 31 + Reign;
            hash = hash * 31 + Start;
            hash = hash * 31 + SourceLand;
            return hash;
        }
    }

    internal string Describe()
        => "guid=" + (CampaignGuid ?? "-") + " slot=" + CampaignSlot + " biome=" + Biome + " reign=" + Reign
           + " start=" + Start + " source=" + SourceLand;
}

/// <summary>
/// Root native capture 桥提供的出发 checkpoint facts（全部字段必需；本类型没有"未知"表示，
/// 因此不存在用默认 0 补未知的路径——不能让 0 冒充真实事实）。
/// </summary>
internal readonly struct PalaceClockReceiptFacts
{
    internal readonly string TripId;
    internal readonly string CampaignGuid;
    internal readonly int CampaignSlot;
    internal readonly int Biome;
    internal readonly int Reign;
    internal readonly int Start;
    internal readonly int SourceLand;
    internal readonly string SourceCheckpointSha;
    internal readonly int RawCampaignTotalDay;
    internal readonly int RawReignDay;
    internal readonly double SourcePlayTimeDays;
    internal readonly double SourceLastPlayedTimeDays;
    internal readonly int SourceIslandTimePlayed;
    internal readonly int LastPlayedReign;
    internal readonly string SourceDirectorSha;
    internal readonly string SourceObjectsSha;

    internal PalaceClockReceiptFacts(string tripId, string campaignGuid, int campaignSlot, int biome, int reign,
        int start, int sourceLand, string sourceCheckpointSha, int rawCampaignTotalDay, int rawReignDay,
        double sourcePlayTimeDays, double sourceLastPlayedTimeDays, int sourceIslandTimePlayed, int lastPlayedReign,
        string sourceDirectorSha, string sourceObjectsSha)
    {
        TripId = tripId;
        CampaignGuid = campaignGuid;
        CampaignSlot = campaignSlot;
        Biome = biome;
        Reign = reign;
        Start = start;
        SourceLand = sourceLand;
        SourceCheckpointSha = sourceCheckpointSha;
        RawCampaignTotalDay = rawCampaignTotalDay;
        RawReignDay = rawReignDay;
        SourcePlayTimeDays = sourcePlayTimeDays;
        SourceLastPlayedTimeDays = sourceLastPlayedTimeDays;
        SourceIslandTimePlayed = sourceIslandTimePlayed;
        LastPlayedReign = lastPlayedReign;
        SourceDirectorSha = sourceDirectorSha;
        SourceObjectsSha = sourceObjectsSha;
    }

    internal PalaceClockScope Scope()
        => new PalaceClockScope(CampaignGuid, CampaignSlot, Biome, Reign, Start, SourceLand);
}

/// <summary>
/// 出发 source checkpoint 的严格摘要收据：只存必要 scalar + 完整 SourceDirectorData/对象摘要 SHA
/// （完整 Data 本体交 native，不嵌巨大原 JSON）。canonical 编码 = 定序 JSON、未知/重复/缺字段拒绝。
/// </summary>
internal sealed class PalaceSourceClockReceipt
{
    private static readonly string[] FieldOrder =
    {
        "version", "tripId", "campaignGuid", "campaignSlot", "biome", "reign", "start", "sourceLand",
        "sourceCheckpointSha", "rawCampaignTotalDay", "rawReignDay", "sourcePlayTimeDays",
        "sourceLastPlayedTimeDays", "sourceIslandTimePlayed", "lastPlayedReign", "sourceDirectorSha",
        "sourceObjectsSha",
    };

    internal readonly int Version;
    internal readonly string TripId;
    internal readonly PalaceClockScope Scope;
    internal readonly string SourceCheckpointSha;
    internal readonly int RawCampaignTotalDay;
    internal readonly int RawReignDay;
    internal readonly double SourcePlayTimeDays;
    internal readonly double SourceLastPlayedTimeDays;
    internal readonly int SourceIslandTimePlayed;
    internal readonly int LastPlayedReign;
    internal readonly string SourceDirectorSha;
    internal readonly string SourceObjectsSha;

    private PalaceSourceClockReceipt(int version, string tripId, in PalaceClockScope scope,
        string sourceCheckpointSha, int rawCampaignTotalDay, int rawReignDay, double sourcePlayTimeDays,
        double sourceLastPlayedTimeDays, int sourceIslandTimePlayed, int lastPlayedReign, string sourceDirectorSha,
        string sourceObjectsSha)
    {
        Version = version;
        TripId = tripId;
        Scope = scope;
        SourceCheckpointSha = sourceCheckpointSha;
        RawCampaignTotalDay = rawCampaignTotalDay;
        RawReignDay = rawReignDay;
        SourcePlayTimeDays = sourcePlayTimeDays;
        SourceLastPlayedTimeDays = sourceLastPlayedTimeDays;
        SourceIslandTimePlayed = sourceIslandTimePlayed;
        LastPlayedReign = lastPlayedReign;
        SourceDirectorSha = sourceDirectorSha;
        SourceObjectsSha = sourceObjectsSha;
    }

    /// <summary>
    /// 从 Root 已验证的 native capture facts 生成收据。只做 shape/范围验证；不读游戏、不补未知。
    /// rawCampaignTotalDay/rawReignDay 是出发时刻真实读到的 campaign.totalGameDays /
    /// currentReign.daysSurvived（原生 int 计数，>=0；0 是合法真实值——host 首日就是 0/0；负数拒绝）。
    /// 未知不由 0 表达：facts 必须来自成功的原生读取，读不到就没有 receipt，绝不用默认值补未知。
    /// </summary>
    internal static bool TryCreateFromVerifiedFacts(in PalaceClockReceiptFacts facts,
        out PalaceSourceClockReceipt receipt, out string reject)
    {
        receipt = null;
        if (!IsTripId(facts.TripId)) { reject = "tripId shape"; return false; }
        if (!IsCanonicalGuid(facts.CampaignGuid)) { reject = "campaignGuid shape"; return false; }
        if (!facts.Scope().ShapeOk()) { reject = "scope shape: " + facts.Scope().Describe(); return false; }
        if (!IsShaHex(facts.SourceCheckpointSha)) { reject = "sourceCheckpointSha shape"; return false; }
        if (!IsShaHex(facts.SourceDirectorSha)) { reject = "sourceDirectorSha shape"; return false; }
        if (!IsShaHex(facts.SourceObjectsSha)) { reject = "sourceObjectsSha shape"; return false; }
        if (facts.RawCampaignTotalDay < 0)
        { reject = "rawCampaignTotalDay < 0 (负数非法；未知由 read-success 表达)"; return false; }
        if (facts.RawReignDay < 0)
        { reject = "rawReignDay < 0 (负数非法；未知由 read-success 表达)"; return false; }
        if (!IsNonNegativeFinite(facts.SourcePlayTimeDays)) { reject = "sourcePlayTimeDays invalid"; return false; }
        if (!IsNonNegativeFinite(facts.SourceLastPlayedTimeDays))
        { reject = "sourceLastPlayedTimeDays invalid"; return false; }
        if (facts.SourceIslandTimePlayed < 0) { reject = "sourceIslandTimePlayed < 0"; return false; }
        if (facts.LastPlayedReign < 0) { reject = "lastPlayedReign < 0"; return false; }

        receipt = new PalaceSourceClockReceipt(PalaceClockLimits.ReceiptVersion, facts.TripId, facts.Scope(),
            facts.SourceCheckpointSha.ToLowerInvariant(), facts.RawCampaignTotalDay, facts.RawReignDay,
            facts.SourcePlayTimeDays, facts.SourceLastPlayedTimeDays, facts.SourceIslandTimePlayed,
            facts.LastPlayedReign, facts.SourceDirectorSha.ToLowerInvariant(),
            facts.SourceObjectsSha.ToLowerInvariant());
        reject = null;
        return true;
    }

    internal bool SameTripId(string tripId) => string.Equals(TripId, tripId, StringComparison.Ordinal);

    /// <summary>cold 的 owner/receipt 比对：全 scope 相等，Start 必须参与。</summary>
    internal bool SameScope(in PalaceClockScope scope) => Scope.Equals(scope);

    internal bool SameRawAnchor(int rawCampaignTotalDay, int rawReignDay)
        => RawCampaignTotalDay == rawCampaignTotalDay && RawReignDay == rawReignDay;

    internal bool SameSourceCheckpoint(string sha)
        => string.Equals(SourceCheckpointSha, NormalizeSha(sha), StringComparison.Ordinal);

    internal bool SameSourceObjects(string sha)
        => string.Equals(SourceObjectsSha, NormalizeSha(sha), StringComparison.Ordinal);

    /// <summary>canonical 定序 JSON（单行、无多余空白）。所有字段已在校验时归一化。</summary>
    internal string WriteCanonicalStrict()
    {
        var sb = new StringBuilder(512);
        sb.Append('{');
        for (int i = 0; i < FieldOrder.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(FieldOrder[i]).Append('"').Append(':');
            switch (FieldOrder[i])
            {
                case "version": sb.Append(Version.ToString(CultureInfo.InvariantCulture)); break;
                case "tripId": AppendJsonString(sb, TripId); break;
                case "campaignGuid": AppendJsonString(sb, Scope.CampaignGuid); break;
                case "campaignSlot": sb.Append(Scope.CampaignSlot.ToString(CultureInfo.InvariantCulture)); break;
                case "biome": sb.Append(Scope.Biome.ToString(CultureInfo.InvariantCulture)); break;
                case "reign": sb.Append(Scope.Reign.ToString(CultureInfo.InvariantCulture)); break;
                case "start": sb.Append(Scope.Start.ToString(CultureInfo.InvariantCulture)); break;
                case "sourceLand": sb.Append(Scope.SourceLand.ToString(CultureInfo.InvariantCulture)); break;
                case "sourceCheckpointSha": AppendJsonString(sb, SourceCheckpointSha); break;
                case "rawCampaignTotalDay": sb.Append(RawCampaignTotalDay.ToString(CultureInfo.InvariantCulture)); break;
                case "rawReignDay": sb.Append(RawReignDay.ToString(CultureInfo.InvariantCulture)); break;
                case "sourcePlayTimeDays": sb.Append(DoubleText(SourcePlayTimeDays)); break;
                case "sourceLastPlayedTimeDays": sb.Append(DoubleText(SourceLastPlayedTimeDays)); break;
                case "sourceIslandTimePlayed": sb.Append(SourceIslandTimePlayed.ToString(CultureInfo.InvariantCulture)); break;
                case "lastPlayedReign": sb.Append(LastPlayedReign.ToString(CultureInfo.InvariantCulture)); break;
                case "sourceDirectorSha": AppendJsonString(sb, SourceDirectorSha); break;
                case "sourceObjectsSha": AppendJsonString(sb, SourceObjectsSha); break;
                default: throw new InvalidOperationException("unknown receipt field");
            }
        }
        sb.Append('}');
        return sb.ToString();
    }

    // double 走 R 往返格式，保证 readback 逐位一致。
    private static string DoubleText(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static void AppendJsonString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    /// <summary>
    /// strict 解析：仅接受本类 canonical 形状 —— 单对象、17 个已知字段各一次、类型/范围严格、
    /// 上限 8KiB、对象后无尾随内容。缺字段、未知字段、重复字段、错误类型、越界一律拒绝。
    /// </summary>
    internal static bool TryParseStrict(string text, out PalaceSourceClockReceipt receipt, out string reject)
    {
        receipt = null;
        if (text == null) { reject = "null"; return false; }
        int byteCount = Encoding.UTF8.GetByteCount(text);
        if (byteCount <= 0 || byteCount > PalaceClockLimits.MaxReceiptBytes)
        { reject = "size out of bounds: " + byteCount; return false; }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        string version = null, tripId = null, guid = null, sourceLand = null, checkpoint = null;
        string slot = null, biome = null, reign = null, start = null;
        string rawTotal = null, rawReign = null, play = null, lastPlayed = null, island = null, lastReign = null;
        string director = null, objects = null;

        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text), new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 2,
            });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            { reject = "not an object"; return false; }
            while (true)
            {
                if (!reader.Read()) { reject = "truncated object"; return false; }
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType != JsonTokenType.PropertyName) { reject = "non-property token"; return false; }
                string name = reader.GetString();
                if (Array.IndexOf(FieldOrder, name) < 0) { reject = "unknown field: " + name; return false; }
                if (!seen.Add(name)) { reject = "duplicate field: " + name; return false; }
                if (!reader.Read()) { reject = "truncated value"; return false; }
                switch (name)
                {
                    case "version": if (!ReadIntText(ref reader, out version)) { reject = "version type"; return false; } break;
                    case "tripId": if (!ReadString(ref reader, out tripId)) { reject = "tripId type"; return false; } break;
                    case "campaignGuid": if (!ReadString(ref reader, out guid)) { reject = "campaignGuid type"; return false; } break;
                    case "campaignSlot": if (!ReadIntText(ref reader, out slot)) { reject = "campaignSlot type"; return false; } break;
                    case "biome": if (!ReadIntText(ref reader, out biome)) { reject = "biome type"; return false; } break;
                    case "reign": if (!ReadIntText(ref reader, out reign)) { reject = "reign type"; return false; } break;
                    case "start": if (!ReadIntText(ref reader, out start)) { reject = "start type"; return false; } break;
                    case "sourceLand": if (!ReadIntText(ref reader, out sourceLand)) { reject = "sourceLand type"; return false; } break;
                    case "sourceCheckpointSha": if (!ReadString(ref reader, out checkpoint)) { reject = "sourceCheckpointSha type"; return false; } break;
                    case "rawCampaignTotalDay": if (!ReadIntText(ref reader, out rawTotal)) { reject = "rawCampaignTotalDay type"; return false; } break;
                    case "rawReignDay": if (!ReadIntText(ref reader, out rawReign)) { reject = "rawReignDay type"; return false; } break;
                    case "sourcePlayTimeDays": if (!ReadDoubleText(ref reader, out play)) { reject = "sourcePlayTimeDays type"; return false; } break;
                    case "sourceLastPlayedTimeDays": if (!ReadDoubleText(ref reader, out lastPlayed)) { reject = "sourceLastPlayedTimeDays type"; return false; } break;
                    case "sourceIslandTimePlayed": if (!ReadIntText(ref reader, out island)) { reject = "sourceIslandTimePlayed type"; return false; } break;
                    case "lastPlayedReign": if (!ReadIntText(ref reader, out lastReign)) { reject = "lastPlayedReign type"; return false; } break;
                    case "sourceDirectorSha": if (!ReadString(ref reader, out director)) { reject = "sourceDirectorSha type"; return false; } break;
                    case "sourceObjectsSha": if (!ReadString(ref reader, out objects)) { reject = "sourceObjectsSha type"; return false; } break;
                }
            }
            if (reader.Read()) { reject = "trailing content"; return false; }
        }
        catch (JsonException e)
        {
            reject = "json: " + e.Message;
            return false;
        }
        catch (InvalidOperationException e)
        {
            // Utf8JsonReader.GetString rejects lone UTF-16 surrogate escapes.
            reject = "invalid decoded string: " + e.GetType().Name;
            return false;
        }

        if (seen.Count != FieldOrder.Length) { reject = "missing fields: " + (FieldOrder.Length - seen.Count); return false; }

        try
        {
            if (ParseInt(version) != PalaceClockLimits.ReceiptVersion) { reject = "version unsupported"; return false; }
            var facts = new PalaceClockReceiptFacts(tripId, guid, ParseInt(slot), ParseInt(biome), ParseInt(reign),
                ParseInt(start), ParseInt(sourceLand), checkpoint, ParseInt(rawTotal), ParseInt(rawReign),
                ParseDouble(play), ParseDouble(lastPlayed), ParseInt(island), ParseInt(lastReign), director, objects);
            if (!TryCreateFromVerifiedFacts(facts, out receipt, out reject)) return false;
            return true;
        }
        catch (Exception e)
        {
            // 数值超出目标类型（例如 20 位十进制整数）一律按 shape 拒绝，绝不带异常上抛。
            receipt = null;
            reject = "value conversion: " + e.GetType().Name;
            return false;
        }
    }

    internal string Describe()
        => "trip=" + TripId + " " + Scope.Describe() + " raw=" + RawCampaignTotalDay + "/" + RawReignDay
           + " srcPlay=" + SourcePlayTimeDays.ToString("R", CultureInfo.InvariantCulture)
           + " srcLastPlayed=" + SourceLastPlayedTimeDays.ToString("R", CultureInfo.InvariantCulture)
           + " srcIslandTime=" + SourceIslandTimePlayed + " srcReign=" + LastPlayedReign;

    // ---- 校验辅助 ----

    private static bool IsTripId(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > PalaceClockLimits.MaxTripIdChars) return false;
        foreach (char c in value)
        {
            bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                      || c == '.' || c == '_' || c == ':' || c == '-';
            if (!ok) return false;
        }
        return true;
    }

    private static bool IsCanonicalGuid(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > PalaceClockLimits.MaxGuidChars) return false;
        if (!Guid.TryParseExact(value, "D", out Guid parsed)) return false;
        return string.Equals(parsed.ToString("D"), value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsShaHex(string value)
        => value != null && value.Length == PalaceClockLimits.ShaHexChars && IsHex64(value);

    private static bool IsHex64(string value)
    {
        foreach (char c in value)
        {
            bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!ok) return false;
        }
        return true;
    }

    private static string NormalizeSha(string sha) => sha != null ? sha.ToLowerInvariant() : null;

    private static bool IsNonNegativeFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;

    // ---- strict 读取辅助（数字文本严格，拒绝 1.0 冒充 int、拒绝 +/前导零以外一切）----

    private static bool ReadString(ref Utf8JsonReader reader, out string value)
    {
        value = null;
        if (reader.TokenType != JsonTokenType.String) return false;
        value = reader.GetString();
        return value != null;
    }

    private static bool ReadIntText(ref Utf8JsonReader reader, out string text)
    {
        if (!ReadNumberText(ref reader, out text)) return false;
        if (text.Length == 0 || text.Length > 20) return false;
        for (int i=0; i<text.Length; i++)
        {
            char c=text[i];
            if (i==0 && c=='-' && text.Length>1) continue;
            if (c < '0' || c > '9') return false;
        }
        return true;
    }

    private static bool ReadDoubleText(ref Utf8JsonReader reader, out string text)
    {
        if (!ReadNumberText(ref reader, out text)) return false;
        if (text.Length == 0 || text.Length > 32) return false;
        foreach (char c in text)
        {
            bool ok = (c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-';
            if (!ok) return false;
        }
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
               && IsNonNegativeFinite(value);
    }

    private static bool ReadNumberText(ref Utf8JsonReader reader, out string text)
    {
        text = null;
        if (reader.TokenType != JsonTokenType.Number) return false;
        text = reader.HasValueSequence
            ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
            : Encoding.UTF8.GetString(reader.ValueSpan);
        return text.Length > 0;
    }

    private static int ParseInt(string text) => int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static double ParseDouble(string text)
        => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}

// ---- 宫廷本地日历（C5 pure policy 的最小化再实现：只服务 own12 Director 的 localday） ----

internal enum PalaceLocalDayGetter { PassNative, ServeLocal }

internal enum PalaceLocalDaySetter { PassNative, Absorb, AbsorbUnknown }

internal enum PalaceSetterMode { Pass, AbsorbOwned, AbsorbUnknown }

internal enum PalaceScopeVerdict { Foreign, Owned, Unknown }

internal enum PalaceApplyDataKind { None, Missing, Typed, WrongType, ReadFailed }

internal enum PalaceApplyAction
{
    None,
    Hydrated,
    HeldUnknown,        // 同 epoch sticky：保持 unknown，不做恢复签名
    PendingNoData,
    NewIslandDay1,
    RejectedCorrupt,
    RejectedMismatch,
}

/// <summary>Per-generation scalar calendar. ApplyDepth 用每 epoch 独立 token 计数，陈旧 finalizer 不能动新 epoch。</summary>
internal sealed class PalaceLocalDayPolicy
{
    internal int LocalDays;
    internal bool HasLocal;
    internal bool CorruptSaved;

    private readonly HashSet<long> _applyTokens = new HashSet<long>();
    private long _nextApplyToken;
    internal int ApplyDepth => _applyTokens.Count;

    internal long Hydrations;
    internal long CorruptRejects;
    internal long Day1Seeds;
    internal long GetterServed;
    internal long GetterPassed;
    internal long SetterAbsorbed;
    internal long SetterPassed;
    internal long ResetsProtected;
    internal long BaselinePendingAbsorbs;
    internal long StaleEnds;

    /// <summary>读取宫廷 DirectorData.daysInReign：只接受 >=1；0/负值是损坏，绝不用伪造的 1 顶替。</summary>
    internal bool HydrateFromSaved(int savedDays)
    {
        Hydrations++;
        if (savedDays >= 1)
        {
            LocalDays = savedDays;
            HasLocal = true;
            CorruptSaved = false;
            return true;
        }
        HasLocal = false;
        CorruptSaved = true;
        CorruptRejects++;
        return false;
    }

    /// <summary>显式新岛 day1 基线（仅完整 fresh 合同成立时调用）。</summary>
    internal void AdoptNewIslandDay1()
    {
        LocalDays = 1;
        HasLocal = true;
        CorruptSaved = false;
        Day1Seeds++;
    }

    internal long BeginApply()
    {
        long token = ++_nextApplyToken;
        _applyTokens.Add(token);
        return token;
    }

    /// <summary>只结束本实例上的本 token；未知/重复 token（陈旧 finalizer）忽略。</summary>
    internal bool EndApply(long token)
    {
        if (!_applyTokens.Remove(token)) { StaleEnds++; return false; }
        return true;
    }

    internal PalaceLocalDayGetter DecideGetter(bool servingHealthy)
    {
        if (!servingHealthy || CorruptSaved || !HasLocal)
        {
            GetterPassed++;
            return PalaceLocalDayGetter.PassNative;
        }
        GetterServed++;
        return PalaceLocalDayGetter.ServeLocal;
    }

    internal PalaceLocalDaySetter DecideSetter(PalaceSetterMode mode, int value)
    {
        if (mode == PalaceSetterMode.Pass)
        {
            SetterPassed++;
            return PalaceLocalDaySetter.PassNative;
        }
        // Owned / suspended / fail-closed unknown（持有 lease）：绝不落到原生 global setter。
        if (ApplyDepth > 0)
        {
            ResetsProtected++;
            SetterAbsorbed++;
            return PalaceLocalDaySetter.Absorb;
        }
        if (mode == PalaceSetterMode.AbsorbOwned && !CorruptSaved && HasLocal && value >= 1)
        {
            LocalDays = value;
        }
        else if (mode == PalaceSetterMode.AbsorbOwned)
        {
            // 基线（ApplyData hydrate / fresh day1）建立前：吸收翻日但绝不采用，
            // 避免把 raw 派生值当宫廷 localday 服务或保存。
            BaselinePendingAbsorbs++;
        }
        SetterAbsorbed++;
        return PalaceLocalDaySetter.Absorb;
    }
}

/// <summary>
/// 只读的 raw 冻结声明。一旦 invalidate 即封存：同 epoch 重捕不得清除 clock-unknown，只有新 epoch 有新锚。
/// 锚值语义：出发 checkpoint（campaign.totalGameDays / currentReign.daysSurvived）在宫廷期间必须相等。
/// </summary>
internal sealed class PalaceClockAnchor
{
    internal bool Captured;
    internal bool ClockUnknown;
    internal int CampaignTotalGameDays;
    internal int ReignDaysSurvived;
    internal string Drift;

    /// <summary>从已验证 receipt 的 raw 值播种（绑定 epoch 时）。封存后不再重捕。</summary>
    internal bool Seed(int totalGameDays, int daysSurvived)
    {
        if (ClockUnknown) return false;
        CampaignTotalGameDays = totalGameDays;
        ReignDaysSurvived = daysSurvived;
        Captured = true;
        return true;
    }

    internal bool Capture(int totalGameDays, int daysSurvived)
    {
        if (ClockUnknown) return false;
        CampaignTotalGameDays = totalGameDays;
        ReignDaysSurvived = daysSurvived;
        Captured = true;
        return true;
    }

    internal bool Observe(int totalGameDays, int daysSurvived)
    {
        if (!Captured || ClockUnknown) return false;
        if (totalGameDays != CampaignTotalGameDays || daysSurvived != ReignDaysSurvived)
        {
            ClockUnknown = true;
            Drift = "raw moved: totalGameDays " + CampaignTotalGameDays + "->" + totalGameDays +
                    ", daysSurvived " + ReignDaysSurvived + "->" + daysSurvived;
            return false;
        }
        return true;
    }

    internal void Invalidate(string why)
    {
        Captured = false;
        ClockUnknown = true;
        Drift = why;
    }

    /// <summary>合法新岛 day1 的基线重载：已封存的 clock-unknown 绝不重开/重捕；
    /// 已捕获（receipt 播种）的 raw 锚也绝不被清除——fresh day1 不得把已捕锚变成待捕。</summary>
    internal bool ArmBaselineCapture()
    {
        if (ClockUnknown || Captured) return false;
        Drift = null;
        return true;
    }
}

/// <summary>一次 runtime 调用的普通快照；引擎侧填 per-read 成功标记，本类型不触碰引擎对象。</summary>
internal struct PalaceScopeObservation
{
    internal bool Readable;      // 所有身份/land/owner 读取都成功
    internal bool ChecksPass;    // Greek world-auth、instance==Managers.director、active、scene 有效 + semantic owner 门内读取成功且与 typed 一致
    internal bool HasIdentity;   // 全部身份指针可用且非零，owner==campaign
    internal long InstancePtr;
    internal long LoadedPtr, OwnerPtr, CampaignPtr, GamePtr, DirectorPtr;
    internal long SceneHandle;
    internal int CampaignIndex, Reign, Biome;
    internal string SemanticGuid;             // Root PalaceSaveBindingSource.TryGetSemanticOwner 门内返回的冻结 owner guid
    internal int Start;                       // 同门返回的 campaign.realStartDateTime（native int）；无门成功时为 0
    internal int CampaignLand, GameLand;
    internal bool RawAnchorReadable;          // campaign.totalGameDays + currentReign.daysSurvived 已读到
    internal int RawCampaignTotalDay, RawReignDay;
    internal string SceneName;   // 仅供观测；绝不作为生命周期键
    internal string Fail;
}

internal sealed class PalaceClockLease
{
    internal long Epoch;
    internal long LoadedPtr, OwnerPtr, CampaignPtr, GamePtr, DirectorPtr;
    internal long SceneHandle;
    internal int CampaignIndex, Reign, Biome;
    internal bool Suspended;      // 已确证离开/退役；计数保留
    internal bool StickyUnknown;
    internal readonly PalaceLocalDayPolicy Policy = new PalaceLocalDayPolicy();
    internal readonly PalaceClockAnchor Anchor = new PalaceClockAnchor();

    // 绑定证据：epoch 只从已验证 receipt 的 scope 绑定（guid/slot/reign/biome/start/sourceLand 可读子集）。
    internal string BoundTripId;
    internal PalaceClockScope BoundScope;

    /// <summary>同 ptr 同内容才算同一代：语义 owner（guid/slot/biome/reign/start）必须与绑定 receipt 全等；
    /// 同 ptr 内容语义变更同样拒服务，不只首绑。</summary>
    internal bool SameIdentity(in PalaceScopeObservation o)
    {
        return LoadedPtr == o.LoadedPtr && OwnerPtr == o.OwnerPtr && CampaignPtr == o.CampaignPtr
            && GamePtr == o.GamePtr && DirectorPtr == o.DirectorPtr
            && SceneHandle == o.SceneHandle && CampaignIndex == o.CampaignIndex && Reign == o.Reign
            && Biome == o.Biome
            && BoundScope.MatchesSemanticOwner(o.SemanticGuid, o.CampaignIndex, o.Biome, o.Reign, o.Start);
    }

    internal bool MarkSticky(string why)
    {
        if (StickyUnknown) return false;
        StickyUnknown = true;
        Anchor.Invalidate(why);
        return true;
    }
}

internal struct PalaceScopeDecision
{
    internal PalaceScopeVerdict Verdict;
    internal bool LeaseHeld;
    internal PalaceClockLease Lease;
    internal bool StickyFault;   // 本次调用新触发
    internal bool NewEpoch;
    internal string Reason;
}

internal struct PalaceApplyFacts
{
    internal PalaceApplyDataKind DataKind;
    internal int SavedDays;
    internal int LastReignNum;
    internal bool IslandReadable;
    internal bool IslandLand12;
    internal int IslandLastPlayedReign;
    internal int SavedRevisionsCount;
    internal int ObjectCount;
    internal bool FirstGenerationToken;
    internal bool IsNewCampaign;          // 仅记录，绝不单独作为 fresh 依据
}

internal struct PalaceApplyDecision
{
    internal PalaceScopeDecision Scope;
    internal bool Begin;
    internal PalaceApplyAction Action;
    internal string Reason;
}

/// <summary>纯 finalizer 配对：只有自己开的 token 才能结束。</summary>
internal static class PalaceApplyTokenPairing
{
    internal static bool EndIfBegan(PalaceLocalDayPolicy policy, bool began, long token)
    {
        if (!began || policy == null) return false;
        return policy.EndApply(token);
    }
}

internal enum PalaceArmedKind { None, Outbound, Cold }

/// <summary>
/// 纯 runtime adapter：scope/epoch/绑定/receipt/锚/finalize 证据。引擎 gate 是它的薄翻译层。
/// raw 两数只读观察，任何路径都不写 campaign 标量。
/// </summary>
internal sealed class PalaceClockPolicyAdapter
{
    internal PalaceClockLease Lease;
    internal long NextEpoch;
    internal long NewEpochs;
    internal long ForeignPasses;
    internal long UnleasedPasses;
    internal long UnknownHoldings;
    internal long UnbindableGreens;
    internal long Retires;

    internal PalaceArmedKind ArmedKind;
    internal PalaceSourceClockReceipt ArmedReceipt;
    internal long ExcludedSourceGamePtr;    // 出发时旧 source Game 指针：land12 窗口内不得当 palace 绑定

    /// <summary>进程级 latch：任何 owned clock-unknown 都禁止在同一 trial 内重新声称健康冻结。只有新进程/显式新 trial 重置。</summary>
    internal bool TrialUnknown;
    internal string TrialUnknownReason;
    internal long TrialUnknownEvents;

    internal void NoteTrialUnknown(string why)
    {
        TrialUnknownEvents++;
        if (!TrialUnknown) { TrialUnknown = true; TrialUnknownReason = why; }
    }

    internal bool RaiseSticky(PalaceClockLease lease, string why)
    {
        if (lease == null) return false;
        bool raised = lease.MarkSticky(why);
        NoteTrialUnknown(why);
        return raised;
    }

    /// <summary>从出发 native checkpoint 绑定（Root 在 Reload 前调用；receipt 已由 Root 物理核对）。</summary>
    internal bool TryArmOutbound(PalaceSourceClockReceipt receipt, string currentTripId, long sourceGamePtr,
        out string reject)
    {
        if (receipt == null) { reject = "no receipt"; return false; }
        if (!receipt.SameTripId(currentTripId)) { reject = "trip mismatch"; return false; }
        if (!receipt.Scope.ShapeOk()) { reject = "scope shape"; return false; }
        if (sourceGamePtr == 0) { reject = "source game ptr zero (无法排除旧 source Game)"; return false; }
        ArmedKind = PalaceArmedKind.Outbound;
        ArmedReceipt = receipt;
        ExcludedSourceGamePtr = sourceGamePtr;
        reject = null;
        return true;
    }

    /// <summary>
    /// 冷启（进程启动时已在宫廷）：Root 读现 trip 绑定的 receipt 并给出当前 owner scope 与 raw 锚；
    /// scope 比对包含 Start；raw 必须等于 receipt（防重置为新锚掩盖漂移）。
    /// </summary>
    internal bool TryArmCold(PalaceSourceClockReceipt receipt, string currentTripId, in PalaceClockScope currentScope,
        int rawCampaignTotalDay, int rawReignDay, out string reject)
    {
        if (receipt == null) { reject = "no receipt"; return false; }
        if (!receipt.SameTripId(currentTripId)) { reject = "trip mismatch"; return false; }
        if (!receipt.SameScope(currentScope)) { reject = "owner scope mismatch: " + currentScope.Describe(); return false; }
        if (!receipt.SameRawAnchor(rawCampaignTotalDay, rawReignDay))
        {
            reject = "raw anchor drift vs receipt: " + rawCampaignTotalDay + "/" + rawReignDay;
            return false;
        }
        ArmedKind = PalaceArmedKind.Cold;
        ArmedReceipt = receipt;
        ExcludedSourceGamePtr = 0;
        reject = null;
        return true;
    }

    /// <summary>Root 报告真实 scope 丢失/旅程结束：注销本功能绑定（TrialUnknown 不在此清除）。
    /// 已有 ReturnArm 只撤销写权、保留旧结果/故障记录；一次旅程/进程内绝不新建/重置。</summary>
    internal void Retire(string reason)
    {
        Retires++;
        if (Lease != null) Lease.Suspended = true;
        Lease = null;
        ArmedKind = PalaceArmedKind.None;
        ArmedReceipt = null;
        ExcludedSourceGamePtr = 0;
        RetireReason = reason;
        if (ReturnArm != null)
        {
            ReturnArm.Retired = true;
            ReturnArm.RetiredReason = reason;
        }
    }

    internal string RetireReason;

    /// <summary>own12 绑定门：receipt 全 scope 语义（guid/slot/reign/biome/start）+ raw 锚 + 排除旧 source Game。</summary>
    internal bool CanBindOwn12(in PalaceScopeObservation o, out string reject)
    {
        if (ArmedKind == PalaceArmedKind.None || ArmedReceipt == null)
        { reject = "not armed"; return false; }
        if (!o.HasIdentity || !o.ChecksPass)
        { reject = "identity/context checks not pass: " + (o.Fail ?? "-"); return false; }
        if (o.CampaignLand != 12 || o.GameLand != 12)
        { reject = "lands not 12/12: " + o.CampaignLand + "/" + o.GameLand; return false; }
        if (o.GamePtr == 0 || (ExcludedSourceGamePtr != 0 && o.GamePtr == ExcludedSourceGamePtr))
        { reject = "old source game at land12 window is not palace"; return false; }
        if (!o.RawAnchorReadable)
        { reject = "raw anchor unreadable at bind"; return false; }
        // raw 两数在绑定时刻必须仍等于出发 checkpoint（先推进再每帧改回一律不允许，也不补写回）。
        if (!ArmedReceipt.SameRawAnchor(o.RawCampaignTotalDay, o.RawReignDay))
        {
            reject = "raw drift vs receipt at bind: " + o.RawCampaignTotalDay + "/" + o.RawReignDay
                     + " vs " + ArmedReceipt.RawCampaignTotalDay + "/" + ArmedReceipt.RawReignDay;
            return false;
        }
        if (o.CampaignIndex != ArmedReceipt.Scope.CampaignSlot)
        { reject = "slot mismatch: " + o.CampaignIndex + " vs " + ArmedReceipt.Scope.CampaignSlot; return false; }
        if (o.Reign != ArmedReceipt.Scope.Reign)
        { reject = "reign mismatch: " + o.Reign + " vs " + ArmedReceipt.Scope.Reign; return false; }
        if (o.Biome != ArmedReceipt.Scope.Biome)
        { reject = "biome mismatch: " + o.Biome + " vs " + ArmedReceipt.Scope.Biome; return false; }
        if (ArmedKind == PalaceArmedKind.Outbound && ExcludedSourceGamePtr == 0)
        { reject = "outbound arm without source game exclusion"; return false; }
        if (!ArmedReceipt.Scope.MatchesSemanticOwner(o.SemanticGuid, o.CampaignIndex, o.Biome, o.Reign, o.Start))
        {
            reject = "semantic owner mismatch: guid=" + (o.SemanticGuid ?? "-") + " start=" + o.Start
                     + " vs receipt guid=" + ArmedReceipt.Scope.CampaignGuid + " start=" + ArmedReceipt.Scope.Start;
            return false;
        }
        reject = null;
        return true;
    }

    /// <summary>只读锚观察。漂移/挂起触发 sticky（并触发 trial latch）；TrialUnknown 下绝不新捕冻结。</summary>
    internal bool ObserveAnchor(PalaceClockLease lease, int totalGameDays, int daysSurvived)
    {
        if (lease == null || lease.StickyUnknown) return false;
        if (TrialUnknown)
        {
            lease.Anchor.Invalidate("trial unknown: no new freeze claim");
            return false;
        }
        bool ok = lease.Anchor.Captured
            ? lease.Anchor.Observe(totalGameDays, daysSurvived)
            : lease.Anchor.Capture(totalGameDays, daysSurvived);
        if (!ok && lease.Anchor.ClockUnknown)
        {
            RaiseSticky(lease, "raw anchor drift: " + lease.Anchor.Drift);
            return false;
        }
        return ok;
    }

    internal bool FreezeClaimHealthy(PalaceClockLease lease)
        => !TrialUnknown && lease != null && !lease.StickyUnknown && !lease.Suspended
           && lease.Anchor.Captured && !lease.Anchor.ClockUnknown;

    /// <summary>
    /// 异常路径 setter 守卫。true = 吸收原生 global setter。持有 lease 且未确证是另一 director 即吸收（含
    /// sticky/实例指针不可读）；只有显式 suspended（已退役/已在 source）或确证 foreign director 放行。
    /// 绝不改动 lease（foreign 放行不得污染已捕获 epoch）。
    /// </summary>
    internal bool DecideFallbackAbsorb(bool instancePtrKnown, long instancePtr)
    {
        PalaceClockLease lease = Lease;
        if (lease == null || lease.Suspended) return false;
        if (instancePtrKnown && instancePtr != 0 && instancePtr != lease.DirectorPtr) return false;
        return true;
    }

    /// <summary>
    /// 持有 lease 的 production setter 管线：先观察本次调用的 raw 锚，再从 lease 现场状态推导 mode。
    /// 本次观察触发漂移时强制 AbsorbUnknown —— 观察前的 AbsorbOwned 不会在刚失去时钟证据的调用上采用数值。
    /// anchorRead=false 是证据丢失路径：本方法自身 RaiseSticky（sticky + trial latch）并返回 AbsorbUnknown，
    /// 绝不采用 value（观察前决定的 AbsorbOwned 不会落到这次调用上）。
    /// </summary>
    internal PalaceLocalDaySetter DecideSetterWithAnchor(in PalaceScopeDecision d, int value, bool anchorRead,
        int totalGameDays, int daysSurvived)
    {
        PalaceClockLease lease = d.Lease;
        if (lease == null) return PalaceLocalDaySetter.PassNative;
        if (!anchorRead)
        {
            RaiseSticky(lease, "raw anchor unreadable in observation");
            lease.Policy.DecideSetter(PalaceSetterMode.AbsorbUnknown, value);
            return PalaceLocalDaySetter.AbsorbUnknown;
        }
        if (d.Verdict != PalaceScopeVerdict.Foreign)
            ObserveAnchor(lease, totalGameDays, daysSurvived);
        return lease.Policy.DecideSetter(SetterModeFor(d), value);
    }

    internal PalaceScopeDecision DecideScope(in PalaceScopeObservation o)
    {
        var d = new PalaceScopeDecision();
        if (!o.Readable)
        {
            // Unknown 不是 Foreign。同一捕获 director + 证据丢失 = sticky fail-closed。
            if (Lease != null && !Lease.Suspended && o.InstancePtr != 0 && o.InstancePtr == Lease.DirectorPtr)
            {
                d.Verdict = PalaceScopeVerdict.Unknown;
                d.LeaseHeld = true;
                d.Lease = Lease;
                d.StickyFault = RaiseSticky(Lease, "evidence lost: " + (o.Fail ?? "unreadable"));
                d.Reason = "owned lease, evidence lost";
                return d;
            }
            d.Verdict = PalaceScopeVerdict.Unknown;
            d.Reason = "unreadable without own lease: " + (o.Fail ?? "unreadable");
            UnleasedPasses++;
            return d;
        }

        bool green = o.ChecksPass && o.CampaignLand == 12 && o.GameLand == 12;
        if (green)
        {
            if (Lease != null && Lease.SameIdentity(o))
            {
                if (Lease.Suspended)
                {
                    // 同代无新证据重入：保持，不洗白。
                    d.Verdict = PalaceScopeVerdict.Unknown;
                    d.LeaseHeld = true;
                    d.Lease = Lease;
                    d.Reason = "suspended lease re-entered without new generation evidence";
                    UnknownHoldings++;
                    return d;
                }
                d.Verdict = PalaceScopeVerdict.Owned;
                d.LeaseHeld = true;
                d.Lease = Lease;
                d.Reason = "owned epoch " + Lease.Epoch;
                return d;
            }

            // 新代（含首绑）：只在 receipt 绑定门通过时建 epoch；否则绝不把 land12 窗口里的
            // 旧 source Game / 未验证上下文当 palace。旧 lease 保留但不服务本实例。
            if (!CanBindOwn12(o, out string bindReject))
            {
                d.Verdict = PalaceScopeVerdict.Unknown;
                d.Reason = "green but unbindable: " + bindReject;
                UnbindableGreens++;
                UnleasedPasses++;
                return d;
            }
            CreateEpoch(o, ref d);
            return d;
        }

        // 可读但非 green：foreign caller B 只放行它自己，绝不动仍活着的 owned lease A。
        if (Lease != null && o.HasIdentity)
        {
            bool managersIsLease = o.DirectorPtr != 0 && o.DirectorPtr == Lease.DirectorPtr;
            bool callerIsLease = o.InstancePtr != 0 && o.InstancePtr == Lease.DirectorPtr;
            if (!managersIsLease)
            {
                Lease.Suspended = true;
                d.Verdict = PalaceScopeVerdict.Foreign;
                d.Reason = "captured director retired (managers moved on)";
                ForeignPasses++;
                return d;
            }
            if (!callerIsLease)
            {
                d.Verdict = PalaceScopeVerdict.Foreign;
                d.Reason = "foreign caller passed through; owned lease untouched";
                ForeignPasses++;
                return d;
            }
            if (IsConfirmedSourceContext(o))
            {
                Lease.Suspended = true;
                d.Verdict = PalaceScopeVerdict.Foreign;
                d.Reason = "confirmed source land " + o.CampaignLand + "; lease suspended";
                ForeignPasses++;
                return d;
            }
            bool bothOwnLand = o.CampaignLand == 12 && o.GameLand == 12;
            bool singleSided = (o.CampaignLand == 12) != (o.GameLand == 12);
            d.Verdict = PalaceScopeVerdict.Unknown;
            d.LeaseHeld = true;
            d.Lease = Lease;
            if (bothOwnLand)
            {
                d.StickyFault = RaiseSticky(Lease, "not green: " + (o.Fail ?? "unverifiable"));
                d.Reason = "owned lease, not green";
            }
            else if (singleSided)
            {
                d.Reason = "single-sided land drift: unknown holding";
            }
            else
            {
                d.Reason = "non-source land pair (" + o.CampaignLand + "/" + o.GameLand + "): unknown holding";
            }
            UnknownHoldings++;
            return d;
        }

        if (IsConfirmedSourceContext(o))
        {
            d.Verdict = PalaceScopeVerdict.Foreign;
            d.Reason = "confirmed source context without own lease";
            ForeignPasses++;
            return d;
        }
        d.Verdict = PalaceScopeVerdict.Unknown;
        d.Reason = "out-of-scope unverified";
        UnleasedPasses++;
        return d;
    }

    /// <summary>
    /// 确证 source 0..11 上下文：campaign 与 game 对同一合法 source land 一致，且 owner 身份 + scene/authority
    /// 上下文全部完好。其余（不匹配/越界/上下文未确证）都保持 unknown。
    /// </summary>
    private static bool IsConfirmedSourceContext(in PalaceScopeObservation o)
    {
        return o.HasIdentity && o.ChecksPass
            && o.CampaignLand == o.GameLand
            && o.CampaignLand >= 0 && o.CampaignLand < 12;
    }

    private void CreateEpoch(in PalaceScopeObservation o, ref PalaceScopeDecision d)
    {
        long epoch = ++NextEpoch;
        PalaceSourceClockReceipt receipt = ArmedReceipt;
        Lease = new PalaceClockLease
        {
            Epoch = epoch,
            LoadedPtr = o.LoadedPtr, OwnerPtr = o.OwnerPtr, CampaignPtr = o.CampaignPtr,
            GamePtr = o.GamePtr, DirectorPtr = o.DirectorPtr,
            SceneHandle = o.SceneHandle, CampaignIndex = o.CampaignIndex, Reign = o.Reign, Biome = o.Biome,
            BoundTripId = receipt.TripId, BoundScope = receipt.Scope,
        };
        if (TrialUnknown)
        {
            Lease.Anchor.Invalidate("trial unknown: no new freeze claim");
        }
        else
        {
            // 锚从出发 checkpoint 播种：raw 必须保持 receipt 两数（不先推进再每帧改回）。
            Lease.Anchor.Seed(receipt.RawCampaignTotalDay, receipt.RawReignDay);
        }
        NewEpochs++;
        d.Verdict = PalaceScopeVerdict.Owned;
        d.LeaseHeld = true;
        d.Lease = Lease;
        d.NewEpoch = true;
        d.Reason = "owned epoch " + Lease.Epoch + " bound trip=" + Lease.BoundTripId;
    }

    internal PalaceApplyDecision DecideApply(in PalaceScopeObservation o, in PalaceApplyFacts f)
    {
        PalaceScopeDecision scope = DecideScope(o);
        var result = new PalaceApplyDecision { Scope = scope };
        if (scope.Verdict != PalaceScopeVerdict.Owned || scope.Lease == null || scope.Lease.Suspended)
        {
            result.Action = PalaceApplyAction.None;
            result.Reason = "not pinned own12 for this apply call";
            return result;
        }
        result.Begin = true;
        PalaceClockLease lease = scope.Lease;

        // 同 epoch sticky unknown 绝不被 hydrate/day1 清除。不做恢复签名。
        if (lease.StickyUnknown)
        {
            result.Action = PalaceApplyAction.HeldUnknown;
            result.Reason = "same-epoch sticky unknown: apply kept unknown";
            return result;
        }

        switch (f.DataKind)
        {
            case PalaceApplyDataKind.Typed:
                if (f.SavedDays < 1)
                {
                    result.Action = PalaceApplyAction.RejectedCorrupt;
                    result.Reason = "DirectorData.daysInReign=" + f.SavedDays + " (<1)";
                    return result;
                }
                if (!f.IslandReadable || !f.IslandLand12 || f.IslandLastPlayedReign != f.LastReignNum
                    || o.Reign != lease.Reign)
                {
                    result.Action = PalaceApplyAction.RejectedMismatch;
                    result.Reason = "reign/island evidence mismatch: saved lastReignNum=" + f.LastReignNum +
                                    " island lastPlayedReign=" + f.IslandLastPlayedReign +
                                    " islandReadable=" + f.IslandReadable + " islandLand12=" + f.IslandLand12 +
                                    " leaseReign=" + lease.Reign;
                    return result;
                }
                result.Action = PalaceApplyAction.Hydrated;
                result.Reason = "fresh owned same-reign reload";
                return result;
            case PalaceApplyDataKind.Missing:
                if (IsNewIslandContract(f))
                {
                    result.Action = PalaceApplyAction.NewIslandDay1;
                    result.Reason = "full fresh-contract new island (null data, land12, lastPlayedReign<=0, no revisions/objects)";
                }
                else
                {
                    result.Action = PalaceApplyAction.PendingNoData;
                    result.Reason = "missing DirectorData without fresh-island contract";
                }
                return result;
            default:
                // WrongType / ReadFailed / None：unknown；绝不伪造 day1。
                result.Action = PalaceApplyAction.PendingNoData;
                result.Reason = "DirectorData kind=" + f.DataKind + ": unknown, no fake day1";
                return result;
        }
    }

    private static bool IsNewIslandContract(in PalaceApplyFacts f)
    {
        return f.IslandReadable && f.IslandLand12 && f.IslandLastPlayedReign <= 0
            && ((f.SavedRevisionsCount == 0 && f.ObjectCount == 0) || f.FirstGenerationToken);
    }

    /// <summary>把决策施加到它捕获的那个 lease。绝不在此清除 sticky unknown。返回日志短描述。</summary>
    internal string ApplyOutcome(in PalaceApplyDecision ad, in PalaceApplyFacts f)
    {
        PalaceClockLease lease = ad.Scope.Lease;
        if (lease == null) return "no-lease";
        switch (ad.Action)
        {
            case PalaceApplyAction.Hydrated:
                if (lease.Policy.HydrateFromSaved(f.SavedDays))
                    return "hydrated day=" + lease.Policy.LocalDays;
                RaiseSticky(lease, "hydrate rejected");
                return "hydrate-rejected";
            case PalaceApplyAction.HeldUnknown:
                return "held-unknown day=" + lease.Policy.LocalDays;
            case PalaceApplyAction.NewIslandDay1:
                if (lease.Policy.HasLocal && lease.Policy.Day1Seeds == 1 && lease.Policy.Hydrations == 0
                    && !lease.Policy.CorruptSaved)
                    return "day1-already-established";
                if (lease.Policy.HasLocal || lease.Policy.Day1Seeds != 0 || lease.Policy.Hydrations != 0
                    || lease.Policy.CorruptSaved)
                {
                    string conflict = lease.Policy.CorruptSaved
                        ? (lease.Policy.HasLocal ? "fresh-conflict:corrupt-local" : "fresh-conflict:corrupt-no-local")
                        : lease.Policy.Hydrations > 0 ? "fresh-conflict:hydrated-baseline"
                        : (lease.Policy.Day1Seeds != 0 || lease.Policy.Hydrations != 0)
                            ? "fresh-conflict:anomalous-counts" : "fresh-conflict:inconsistent-local-state";
                    RaiseSticky(lease, conflict);
                    return conflict;
                }
                lease.Policy.AdoptNewIslandDay1();
                // 只在尚无锚时重开基线；receipt 已捕获的 raw 锚绝不被 fresh day1 清除（锚内自保）。
                lease.Anchor.ArmBaselineCapture();
                return "day1";
            case PalaceApplyAction.PendingNoData:
                RaiseSticky(lease, "missing/unknown DirectorData");
                return "pending-no-data";
            case PalaceApplyAction.RejectedCorrupt:
                lease.Policy.HydrateFromSaved(f.SavedDays);
                RaiseSticky(lease, "corrupt saved day");
                return "corrupt";
            case PalaceApplyAction.RejectedMismatch:
                RaiseSticky(lease, "reign/island mismatch");
                return "mismatch";
            default:
                return "none";
        }
    }

    /// <summary>native ApplyData finalize 接线：本次调用期间的原生异常标记 exact lease unknown，然后只结束本 token。</summary>
    internal string FinishApply(bool began, PalaceLocalDayPolicy policy, PalaceClockLease leaseRef, long token,
        bool nativeException)
    {
        if (!began || policy == null) return "noop";
        if (nativeException && leaseRef != null)
            RaiseSticky(leaseRef, "native ApplyData exception");
        policy.EndApply(token);
        return nativeException ? "ended-sticky" : "ended";
    }

    /// <summary>scope 决策到 setter mode 的 canonical 映射。Foreign/越界 unknown 放行；Owned/Suspended/held unknown 吸收。</summary>
    internal static PalaceSetterMode SetterModeFor(in PalaceScopeDecision d)
    {
        if (d.Verdict == PalaceScopeVerdict.Owned && d.Lease != null)
            return (d.Lease.StickyUnknown || d.Lease.Suspended)
                ? PalaceSetterMode.AbsorbUnknown
                : PalaceSetterMode.AbsorbOwned;
        if (d.Verdict == PalaceScopeVerdict.Unknown && d.LeaseHeld && d.Lease != null)
            return PalaceSetterMode.AbsorbUnknown;
        return PalaceSetterMode.Pass;
    }

    // ---- 回程单次派生（责任 3） ----

    internal PalaceClockReturnArm ReturnArm;

    /// <summary>
    /// Root 在新 Campaign.ApplyToScene 之前、核过 exact 新 Game/campaign._islands[source] 指针/来源与
    /// receipt/object 摘要后调用（handoff 边界已核，不是 Reload 前预知未来 ptr）。
    /// 拒绝 = 本次不派生（零写）：raw 锚漂移、object 摘要不一致、指针缺失/为 0、trip 不一致。
    /// 同进程一次旅程只有一个 ReturnArm：任何未决/consumed/fault/retired 的旧 arm 都不准被新 arm 覆盖。
    /// </summary>
    internal bool TryArmReturn(in PalaceClockReturnFacts f, out string reject)
    {
        if (f.Receipt == null) { reject = "no receipt"; return false; }
        if (ReturnArm != null)
        {
            reject = "return arm already exists in this process: " + ReturnArm.Describe();
            return false;
        }
        if (!f.Receipt.SameTripId(f.TripId)) { reject = "trip mismatch"; return false; }
        if (f.CampaignPtr == 0 || f.OwnerPtr == 0 || f.GlobalPtr == 0 || f.GamePtr == 0 || f.SourceIslandPtr == 0)
        { reject = "pointer evidence incomplete"; return false; }
        if (!f.Receipt.SameRawAnchor(f.CurrentRawCampaignTotalDay, f.CurrentRawReignDay))
        {
            reject = "raw anchor drift vs receipt: " + f.CurrentRawCampaignTotalDay + "/" + f.CurrentRawReignDay;
            return false;
        }
        if (!f.Receipt.SameSourceObjects(f.CurrentObjectsSha))
        { reject = "source objects digest mismatch"; return false; }
        ReturnArm = new PalaceClockReturnArm(f);
        reject = null;
        return true;
    }

    internal PalaceReturnDecision DecideReturn(in PalaceReturnObservation o, out string reason)
    {
        PalaceClockReturnArm arm = ReturnArm;
        if (arm == null) { reason = "not armed"; return PalaceReturnDecision.NotArmed; }
        if (arm.Retired)
        { reason = "retired (write right revoked): " + (arm.RetiredReason ?? "-"); return PalaceReturnDecision.Retired; }
        if (arm.Consumed || arm.Fault) { reason = "already consumed/faulted: " + (arm.FaultReason ?? "-"); return PalaceReturnDecision.ConsumedOrFaulted; }
        if (!o.RunOriginal) { reason = "runOriginal false"; return PalaceReturnDecision.RunOriginalFalse; }
        if (o.InstancePtr != arm.SourceIslandPtr)
        { reason = "not armed source island: " + o.InstancePtr.ToString("x") + " vs " + arm.SourceIslandPtr.ToString("x"); return PalaceReturnDecision.EvidenceDrift; }
        if (o.CampaignPtr != arm.CampaignPtr || o.OwnerPtr != arm.OwnerPtr || o.GlobalPtr != arm.GlobalPtr)
        { reason = "campaign/owner/global drift"; return PalaceReturnDecision.EvidenceDrift; }
        if (o.GamePtr != arm.GamePtr) { reason = "game window drift"; return PalaceReturnDecision.EvidenceDrift; }
        if (o.CurrentLand != arm.Receipt.Scope.SourceLand)
        { reason = "current land " + o.CurrentLand + " != source " + arm.Receipt.Scope.SourceLand; return PalaceReturnDecision.EvidenceDrift; }
        reason = "single derivation: lastPlayedTimeDays <- campaign.playTimeDays";
        return PalaceReturnDecision.Derive;
    }

    /// <summary>写前调用：单次语义，无论后续结果都不再重试；缺失/retired arm 一律零写。</summary>
    internal void BeginReturnWrite()
    {
        PalaceClockReturnArm arm = ReturnArm;
        if (arm == null || arm.Retired) return;
        arm.BeginWrite();
    }

    internal void RecordReturnResult(double originalLastPlayed, double campaignPlayTime, double readback)
    {
        PalaceClockReturnArm arm = ReturnArm;
        if (arm == null || arm.Retired) return;
        arm.RecordResult(originalLastPlayed, campaignPlayTime, readback);
    }

    internal void RecordReturnThrow(string exceptionType)
    {
        PalaceClockReturnArm arm = ReturnArm;
        if (arm == null || arm.Retired) return;
        arm.RecordThrow(exceptionType);
    }

    internal string StateSummary()
    {
        PalaceClockLease lease = Lease;
        return "armed=" + ArmedKind + " trips=" + (ArmedReceipt != null ? ArmedReceipt.TripId : "-")
               + " epochs=" + NextEpoch + " lease=" + (lease != null ? lease.Epoch.ToString() : "none")
               + " suspended=" + (lease != null && lease.Suspended)
               + " sticky=" + (lease != null && lease.StickyUnknown)
               + " days=" + (lease != null ? lease.Policy.LocalDays : 0)
               + " hasLocal=" + (lease != null && lease.Policy.HasLocal)
               + " hydrated=" + (lease != null ? lease.Policy.Hydrations : 0)
               + " served=" + (lease != null ? lease.Policy.GetterServed : 0)
               + " absorbed=" + (lease != null ? lease.Policy.SetterAbsorbed : 0)
               + " cachedResets=" + (lease != null ? lease.Policy.ResetsProtected : 0)
               + " foreign=" + ForeignPasses + " unknown=" + UnknownHoldings + " unbindable=" + UnbindableGreens
               + " anchor=" + (lease != null && lease.Anchor.Captured
                   ? ("raw " + lease.Anchor.CampaignTotalGameDays + "/" + lease.Anchor.ReignDaysSurvived
                      + (lease.Anchor.ClockUnknown ? " CLOCK-UNKNOWN " + lease.Anchor.Drift : ""))
                   : "uncaptured")
               + " returnArm=" + (ReturnArm != null ? ReturnArm.Describe() : "none")
               + " trialUnknown=" + TrialUnknown + (TrialUnknown ? " (" + TrialUnknownReason + ")" : "");
    }
}

internal enum PalaceReturnDecision { NotArmed, Retired, ConsumedOrFaulted, RunOriginalFalse, EvidenceDrift, Derive }

/// <summary>Root 核过的回程事实（exact 新 Game 窗口 / campaign._islands[source] / receipt / object 摘要）。
/// Root 在新 Campaign.ApplyToScene 之前已核 handoff 边界并传入；不是 Reload 前预知未来 ptr。</summary>
internal readonly struct PalaceClockReturnFacts
{
    internal readonly string TripId;
    internal readonly PalaceSourceClockReceipt Receipt;
    internal readonly long CampaignPtr, OwnerPtr, GlobalPtr, GamePtr, SourceIslandPtr;
    internal readonly int CurrentRawCampaignTotalDay, CurrentRawReignDay;
    internal readonly string CurrentObjectsSha;

    internal PalaceClockReturnFacts(string tripId, PalaceSourceClockReceipt receipt, long campaignPtr, long ownerPtr,
        long globalPtr, long gamePtr, long sourceIslandPtr, int currentRawCampaignTotalDay, int currentRawReignDay,
        string currentObjectsSha)
    {
        TripId = tripId;
        Receipt = receipt;
        CampaignPtr = campaignPtr;
        OwnerPtr = ownerPtr;
        GlobalPtr = globalPtr;
        GamePtr = gamePtr;
        SourceIslandPtr = sourceIslandPtr;
        CurrentRawCampaignTotalDay = currentRawCampaignTotalDay;
        CurrentRawReignDay = currentRawReignDay;
        CurrentObjectsSha = currentObjectsSha;
    }
}

internal readonly struct PalaceReturnObservation
{
    internal readonly bool RunOriginal;
    internal readonly long InstancePtr, CampaignPtr, OwnerPtr, GlobalPtr, GamePtr;
    internal readonly int CurrentLand;

    internal PalaceReturnObservation(bool runOriginal, long instancePtr, long campaignPtr, long ownerPtr,
        long globalPtr, long gamePtr, int currentLand)
    {
        RunOriginal = runOriginal;
        InstancePtr = instancePtr;
        CampaignPtr = campaignPtr;
        OwnerPtr = ownerPtr;
        GlobalPtr = globalPtr;
        GamePtr = gamePtr;
        CurrentLand = currentLand;
    }
}

/// <summary>单次 LastPlayed 派生的责任记录：original/new/campaign 读回与结果，不是付款权威。</summary>
internal sealed class PalaceClockReturnArm
{
    internal readonly string TripId;
    internal readonly PalaceSourceClockReceipt Receipt;
    internal readonly long CampaignPtr, OwnerPtr, GlobalPtr, GamePtr, SourceIslandPtr;
    internal readonly int RawCampaignTotalDay, RawReignDay;

    internal bool Consumed;
    internal bool Fault;
    internal string FaultReason;
    internal int Attempts;
    internal bool Attempted;
    internal double OriginalLastPlayed;
    internal double CampaignPlayTime;
    internal double Readback;
    internal bool ReadbackOk;
    internal string Thrown;
    internal bool NativeResultSeen;
    internal bool NativeResultValue;
    internal bool NativeResultFalse;   // Kept as sticky historical failure evidence.
    internal bool Retired;             // 写权已撤销（Retire）；旧结果/故障记录保留
    internal string RetiredReason;

    internal PalaceClockReturnArm(in PalaceClockReturnFacts f)
    {
        TripId = f.TripId;
        Receipt = f.Receipt;
        CampaignPtr = f.CampaignPtr;
        OwnerPtr = f.OwnerPtr;
        GlobalPtr = f.GlobalPtr;
        GamePtr = f.GamePtr;
        SourceIslandPtr = f.SourceIslandPtr;
        RawCampaignTotalDay = f.CurrentRawCampaignTotalDay;
        RawReignDay = f.CurrentRawReignDay;
    }

    internal void BeginWrite()
    {
        Consumed = true;   // 单次：写前即消费，throw/失败也不在本窗口重试
        Attempted = true;
        Attempts++;
    }

    internal void RecordResult(double originalLastPlayed, double campaignPlayTime, double readback)
    {
        OriginalLastPlayed = originalLastPlayed;
        CampaignPlayTime = campaignPlayTime;
        Readback = readback;
        ReadbackOk = readback == campaignPlayTime;
        if (!ReadbackOk)
        {
            Fault = true;
            FaultReason = "readback mismatch: " + readback.ToString("R", CultureInfo.InvariantCulture)
                          + " != " + campaignPlayTime.ToString("R", CultureInfo.InvariantCulture);
        }
    }

    internal void RecordThrow(string exceptionType)
    {
        Fault = true;
        Thrown = exceptionType;
        FaultReason = "throw: " + exceptionType;
    }

    /// <summary>原生 TryPop 的结果（仅当该方法返回 bool 时可见）。false = 保责任并报。</summary>
    internal void RecordNativeResult(bool result)
    {
        if (NativeResultSeen)
        {
            if (!result) NativeResultFalse = true;
            Fault = true;
            FaultReason = "duplicate native TryPop result";
            return;
        }
        NativeResultSeen = true;
        NativeResultValue = result;
        if (result) return;
        NativeResultFalse = true;
        Fault = true;
        FaultReason = "native TryPop returned false";
    }

    internal string Describe()
        => "trip=" + TripId + " src=" + Receipt.Scope.SourceLand
           + (Attempted
               ? (" attempted=" + Attempts + " orig=" + OriginalLastPlayed.ToString("R", CultureInfo.InvariantCulture)
                  + " campaign=" + CampaignPlayTime.ToString("R", CultureInfo.InvariantCulture)
                  + " readback=" + Readback.ToString("R", CultureInfo.InvariantCulture)
                  + (ReadbackOk ? " ok" : " MISMATCH"))
               : " not-attempted")
           + (Fault ? " FAULT(" + FaultReason + ")" : "")
           + (NativeResultFalse ? " nativeFalse" : "")
           + (Consumed && !Attempted ? " consumed" : "")
           + (Retired ? " retired(" + (RetiredReason ?? "-") + ")" : "");
}

/// <summary>
/// TryPop 单次调用的 per-call 责任状态：只绑定当次调用捕获的 arm/ISD/Game/attempt。
/// false/throw 只更新这一个 call 的 arm；finalizer/postfix 绝不复读全局状态猜原调用。
/// </summary>
internal sealed class PalaceReturnCallState
{
    internal readonly PalaceClockReturnArm Arm;
    internal readonly long InstancePtr;   // 当次 prefix 观察到的 ISD 指针
    internal readonly long GamePtr;       // 当次 prefix 观察到的 Game 窗口
    internal readonly int Attempt;        // arm.Attempts（写前消费后的尝试序号）

    internal PalaceReturnCallState(PalaceClockReturnArm arm, long instancePtr, long gamePtr, int attempt)
    {
        Arm = arm;
        InstancePtr = instancePtr;
        GamePtr = gamePtr;
        Attempt = attempt;
    }

    /// <summary>该 call state 是否绑定到这个 arm（foreign/后续调用不得借用）。</summary>
    internal bool OwnsArm(PalaceClockReturnArm candidate)
        => candidate != null && ReferenceEquals(Arm, candidate);

    /// <summary>原生返回 false：只落本 call 的 arm；未消费（零写）的调用不记责任。</summary>
    internal void NoteNativeFalse() => NoteNativeResult(false);

    internal void NoteNativeResult(bool result)
    {
        if (Arm == null || !Arm.Attempted) return;
        Arm.RecordNativeResult(result);
    }

    /// <summary>原生/派生异常：只落本 call 的 arm，异常类型原样记录（不吞不替代）。</summary>
    internal void NoteThrow(string exceptionType)
    {
        if (Arm == null || !Arm.Attempted) return;
        Arm.RecordThrow(exceptionType);
    }
}
