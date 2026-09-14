using System;
using System.Globalization;
using System.Text.Json;

namespace KingdomEnhancedMod;

/// <summary>
/// Hermes 头饰收据（receipt）的纯编解码器：不触碰 Unity / IL2CPP / Harmony，只做
/// 字符串与字节的字节级精确加工，便于直接单测。
///
/// 两种载体共用同一份语义（choice：-1 = 保持原生外观，0..43 = 固定选择码）：
/// 1. 落盘形式：原生 <c>FriendlyTrollData</c> 组件 JSON 里多出的一个顶层属性
///    <c>kemHermesHeadwear</c>，值为 <c>{"v":1,"id":"&lt;guid N&gt;","choice":N}</c>。
/// 注入/替换只动这一个值区间，其余字节（含原生 maskIndex/trollHealth/toughTroll
/// 与未知属性、缩进、转义）逐字节原样保留；改写后必须整体能重新解析成对象，
/// 否则一律返回 null 交给调用方保持原样（fail closed）。
/// 2. 线路形式：原生 FriendlyTroll 三个序列化字段之后追加的 30 字节版本化尾巴
///    （magic 'KHM1' / version / 16 字节 Guid / choice / hostEnabled / revision）。
///
/// 未知 schema（v != 1、超长、解析失败、Guid 非法、choice 越界）一律判为
/// <see cref="ReceiptInfo.Unrecognized"/>：不产生运行时收据、不改写盘上内容、不重抽选。
/// </summary>
internal static class HermesHeadwearCodec
{
    /// <summary>-1：显式“本次转化不戴头饰”，与“没有收据”语义不同。</summary>
    internal const int ChoiceNone = -1;

    /// <summary>固定选择码下界（世界组第一顶）。</summary>
    internal const int MinChoice = 0;

    /// <summary>
    /// 固定选择码上界 43：中世纪/竹/死亡之地/北境/希腊 各 6 顶（0..29）+ 派对帽
    /// 0..6（30..36）+ 派对面具 0..6（37..43）。常规帽 index 6 永不在选择空间内。
    /// </summary>
    internal const int MaxChoice = 43;

    /// <summary>盘上收据的属性名（唯一键，替换而不追加第二份）。</summary>
    internal const string MetadataKey = "kemHermesHeadwear";

    /// <summary>当前收据 schema 版本。</summary>
    internal const int MetadataVersion = 1;

    /// <summary>单个收据文本上限：超过即视为未知 schema，原样保留、不再改写。</summary>
    internal const int MaxMetadataChars = 1024;

    /// <summary>单组件 payload 文本上限：超过即放弃注入（不解析、不改写）。</summary>
    internal const int MaxComponentDataChars = 262144;

    /// <summary>尾巴总字节数：4 magic + 1 version + 16 token + 4 choice + 1 hostEnabled + 4 revision。</summary>
    internal const int TailBytes = 30;

    /// <summary>尾巴 magic（小端写入，字节序为 'K','H','M','1'）。</summary>
    internal const int TailMagic = 0x314D484B;

    /// <summary>magic 的字节序列，供调用方在消费字节前做无副作用探测。</summary>
    internal static readonly byte[] TailMagicBytes = { 0x4B, 0x48, 0x4D, 0x31 };

    /// <summary>尾巴 schema 版本。</summary>
    internal const byte TailVersion = 1;

    private static readonly JsonDocumentOptions ParseOptions = new JsonDocumentOptions
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16,
    };

    /// <summary>选择码是否合法（含 <see cref="ChoiceNone"/>）。</summary>
    internal static bool IsChoiceCode(int choice)
    {
        return choice >= ChoiceNone && choice <= MaxChoice;
    }

    /// <summary>真正的头饰选择码（不含 -1）。</summary>
    internal static bool IsHeadwearChoice(int choice)
    {
        return choice >= MinChoice && choice <= MaxChoice;
    }

    // ---------------------------------------------------------------- 盘上收据

    /// <summary>一次盘上收据观察结果；<see cref="None"/> 与 <see cref="Unrecognized"/> 是共享实例。</summary>
    internal sealed class ReceiptInfo
    {
        /// <summary>组件 payload 里没有我们的属性（旧存档 / 原生对象）。</summary>
        internal static readonly ReceiptInfo None = new ReceiptInfo(false, false, Guid.Empty, ChoiceNone);

        /// <summary>属性存在但不能识别：原样保留，不产生运行时收据，不重抽选。</summary>
        internal static readonly ReceiptInfo Unrecognized = new ReceiptInfo(true, false, Guid.Empty, ChoiceNone);

        internal readonly bool Found;
        internal readonly bool Recognized;
        internal readonly Guid Token;
        internal readonly int Choice;

        private ReceiptInfo(bool found, bool recognized, Guid token, int choice)
        {
            Found = found;
            Recognized = recognized;
            Token = token;
            Choice = choice;
        }

        internal static ReceiptInfo For(Guid token, int choice)
        {
            return new ReceiptInfo(true, true, token, choice);
        }
    }

    /// <summary>读取组件 payload 顶层的 <c>kemHermesHeadwear</c> 并解析。</summary>
    internal static ReceiptInfo ReadComponentMetadata(string componentJson)
    {
        string raw = ExtractMetadataValue(componentJson);
        if (raw == null) return ReceiptInfo.None;
        if (raw.Length == 0 || raw.Length > MaxMetadataChars) return ReceiptInfo.Unrecognized;
        if (!TryParseVersion1(raw, out Guid token, out int choice)) return ReceiptInfo.Unrecognized;
        return ReceiptInfo.For(token, choice);
    }

    /// <summary>返回顶层属性原始值文本（含嵌套对象原文）；不存在或 payload 不可解析时返回 null。</summary>
    internal static string ExtractMetadataValue(string componentJson)
    {
        if (!TryScan(componentJson, out ScanResult scan)) return null;
        if (scan.ValueStart < 0) return null;
        return componentJson.Substring(scan.ValueStart, scan.ValueEnd - scan.ValueStart);
    }

    /// <summary>v1 收据文本（Guid 为 N 格式，choice 为不变文化十进制）。</summary>
    internal static string BuildMetadataJson(Guid token, int choice)
    {
        return $"{{\"v\":{MetadataVersion.ToString(CultureInfo.InvariantCulture)},\"id\":\"{token.ToString("N", CultureInfo.InvariantCulture)}\",\"choice\":{choice.ToString(CultureInfo.InvariantCulture)}}}";
    }

    /// <summary>
    /// 在组件 payload 顶层注入/替换 <c>kemHermesHeadwear</c>。成功返回新文本，
    /// 任何结构异常（不是对象、括号不配对、重复键、尾逗号、超长、结果非法 JSON）返回 null。
    /// </summary>
    internal static string InjectMetadata(string componentJson, string metadataJson)
    {
        if (componentJson == null || metadataJson == null) return null;
        if (metadataJson.Length == 0 || metadataJson.Length > MaxMetadataChars) return null;
        if (!TryScan(componentJson, out ScanResult scan)) return null;

        string updated;
        if (scan.ValueStart >= 0)
        {
            updated = string.Concat(
                componentJson.Substring(0, scan.ValueStart),
                metadataJson,
                componentJson.Substring(scan.ValueEnd));
        }
        else if (scan.Empty)
        {
            // 空对象：必须把属性名一起写进去（只插值是非法 JSON）。
            updated = string.Concat(
                componentJson.Substring(0, scan.CloseBraceIndex),
                "\"" + MetadataKey + "\":" + metadataJson,
                componentJson.Substring(scan.CloseBraceIndex));
        }
        else
        {
            string separator = componentJson.IndexOf('\n') >= 0
                ? ",\n    \"" + MetadataKey + "\":"
                : ",\"" + MetadataKey + "\":";
            updated = string.Concat(
                componentJson.Substring(0, scan.LastMemberEnd),
                separator,
                metadataJson,
                componentJson.Substring(scan.LastMemberEnd));
        }

        return IsWellFormedObject(updated) ? updated : null;
    }

    private static bool TryParseVersion1(string raw, out Guid token, out int choice)
    {
        token = Guid.Empty;
        choice = ChoiceNone;
        if (raw.Length == 0 || raw[0] != '{') return false;
        try
        {
            using (JsonDocument document = JsonDocument.Parse(raw, ParseOptions))
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;

                if (!root.TryGetProperty("v", out JsonElement versionElement)
                    || versionElement.ValueKind != JsonValueKind.Number
                    || !versionElement.TryGetInt32(out int version)
                    || version != MetadataVersion)
                    return false;

                if (!root.TryGetProperty("id", out JsonElement idElement)
                    || idElement.ValueKind != JsonValueKind.String)
                    return false;
                string idText = idElement.GetString();
                if (string.IsNullOrEmpty(idText) || !Guid.TryParse(idText, out Guid parsedToken))
                    return false;
                if (parsedToken == Guid.Empty) return false;

                if (!root.TryGetProperty("choice", out JsonElement choiceElement)
                    || choiceElement.ValueKind != JsonValueKind.Number
                    || !choiceElement.TryGetInt32(out int parsedChoice))
                    return false;
                if (!IsChoiceCode(parsedChoice)) return false;

                token = parsedToken;
                choice = parsedChoice;
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // ---------------------------------------------------------------- 线路尾巴

    /// <summary>线路尾巴解出的值；<see cref="Token"/> 恒非空。</summary>
    internal readonly struct Tail
    {
        internal Tail(Guid token, int choice, bool hostEnabled, int revision)
        {
            Token = token;
            Choice = choice;
            HostEnabled = hostEnabled;
            Revision = revision;
        }

        internal readonly Guid Token;
        internal readonly int Choice;
        internal readonly bool HostEnabled;
        internal readonly int Revision;
    }

    /// <summary>就地写入 30 字节尾巴；token 为空 / choice 越界 / 目标过小返回 false 且不改动目标。</summary>
    internal static bool WriteTail(Span<byte> destination, Guid token, int choice, bool hostEnabled, int revision)
    {
        if (destination.Length < TailBytes) return false;
        if (token == Guid.Empty) return false;
        if (!IsChoiceCode(choice)) return false;
        if (revision < 0) revision = 0;

        int offset = 0;
        WriteInt32(destination, ref offset, TailMagic);
        destination[offset] = TailVersion;
        offset++;
        if (!token.TryWriteBytes(destination.Slice(offset, 16))) return false;
        offset += 16;
        WriteInt32(destination, ref offset, choice);
        destination[offset] = hostEnabled ? (byte)1 : (byte)0;
        offset++;
        WriteInt32(destination, ref offset, revision);
        return offset == TailBytes;
    }

    /// <summary>解析 30 字节尾巴；magic/版本/choice 范围/token 非空/revision 非负任一不符即 false。</summary>
    internal static bool TryReadTail(ReadOnlySpan<byte> source, out Tail tail)
    {
        tail = default;
        if (source.Length < TailBytes) return false;

        int offset = 0;
        if (ReadInt32(source, ref offset) != TailMagic) return false;
        if (source[offset] != TailVersion) return false;
        offset++;
        Guid token = new Guid(source.Slice(offset, 16));
        offset += 16;
        if (token == Guid.Empty) return false;
        int choice = ReadInt32(source, ref offset);
        if (!IsChoiceCode(choice)) return false;
        bool hostEnabled = source[offset] != 0;
        offset++;
        int revision = ReadInt32(source, ref offset);
        if (revision < 0) return false;

        tail = new Tail(token, choice, hostEnabled, revision);
        return true;
    }

    private static void WriteInt32(Span<byte> destination, ref int offset, int value)
    {
        destination[offset] = (byte)value;
        destination[offset + 1] = (byte)(value >> 8);
        destination[offset + 2] = (byte)(value >> 16);
        destination[offset + 3] = (byte)(value >> 24);
        offset += 4;
    }

    private static int ReadInt32(ReadOnlySpan<byte> source, ref int offset)
    {
        int value = source[offset]
            | (source[offset + 1] << 8)
            | (source[offset + 2] << 16)
            | (source[offset + 3] << 24);
        offset += 4;
        return value;
    }

    // ---------------------------------------------------------------- JSON 扫描

    private struct ScanResult
    {
        internal bool Empty;
        internal int ValueStart;
        internal int ValueEnd;
        internal int LastMemberEnd;
        internal int CloseBraceIndex;
    }

    /// <summary>
    /// 单遍扫描顶层成员：定位 <see cref="MetadataKey"/> 的值区间、最后一个成员值的
    /// 结束位置、以及对象的结束花括号位置。任何结构异常一律 false。
    /// </summary>
    private static bool TryScan(string json, out ScanResult result)
    {
        result = default;
        if (json == null) return false;
        int length = json.Length;
        if (length < 2 || length > MaxComponentDataChars) return false;

        int start = 0;
        while (start < length && IsJsonWhitespace(json[start])) start++;
        int end = length;
        while (end > start && IsJsonWhitespace(json[end - 1])) end--;
        if (end - start < 2 || json[start] != '{' || json[end - 1] != '}') return false;

        int index = start + 1;
        int lastMemberEnd = -1;
        int valueStart = -1;
        int valueEnd = -1;
        int closeBraceIndex = -1;
        bool anyMember = false;
        bool afterComma = false;

        while (true)
        {
            SkipWhitespace(json, ref index, end);
            if (index >= end) return false;

            if (json[index] == '}')
            {
                if (afterComma && anyMember) return false; // 尾逗号：不做任何改写
                closeBraceIndex = index;
                break;
            }
            if (anyMember && !afterComma) return false; // 成员间缺少逗号

            if (json[index] != '"') return false;
            int nameStart = index;
            if (!SkipString(json, ref index, end)) return false;
            bool isTarget = IsTargetName(json, nameStart, index);

            SkipWhitespace(json, ref index, end);
            if (index >= end || json[index] != ':') return false;
            index++;
            SkipWhitespace(json, ref index, end);

            int memberValueStart = index;
            if (!SkipValue(json, ref index, end)) return false;
            int memberValueEnd = index;

            anyMember = true;
            afterComma = false;
            lastMemberEnd = memberValueEnd;
            if (isTarget)
            {
                if (valueStart >= 0) return false; // 重复键：拒绝改写
                valueStart = memberValueStart;
                valueEnd = memberValueEnd;
            }

            SkipWhitespace(json, ref index, end);
            if (index >= end) return false;
            if (json[index] == ',')
            {
                index++;
                afterComma = true;
                continue;
            }
            if (json[index] == '}')
            {
                closeBraceIndex = index;
                break;
            }
            return false;
        }

        if (closeBraceIndex < 0) return false;
        result.Empty = !anyMember;
        result.ValueStart = valueStart;
        result.ValueEnd = valueEnd;
        result.LastMemberEnd = lastMemberEnd;
        result.CloseBraceIndex = closeBraceIndex;
        return true;
    }

    private static bool IsTargetName(string json, int nameStart, int nameEndExclusive)
    {
        if (nameEndExclusive - nameStart != MetadataKey.Length + 2) return false;
        if (json[nameStart] != '"' || json[nameEndExclusive - 1] != '"') return false;
        for (int i = 0; i < MetadataKey.Length; i++)
        {
            if (json[nameStart + 1 + i] != MetadataKey[i]) return false;
        }
        return true;
    }

    private static bool SkipString(string json, ref int index, int end)
    {
        int i = index;
        if (i >= end || json[i] != '"') return false;
        i++;
        while (i < end)
        {
            char c = json[i];
            if (c == '\\')
            {
                i++;
                if (i >= end) return false;
                char escape = json[i];
                if (escape == 'u')
                {
                    if (i + 4 >= end) return false;
                    for (int k = 1; k <= 4; k++)
                    {
                        if (!IsHexDigit(json[i + k])) return false;
                    }
                    i += 5;
                    continue;
                }
                if (escape != '"' && escape != '\\' && escape != '/' && escape != 'b'
                    && escape != 'f' && escape != 'n' && escape != 'r' && escape != 't')
                    return false;
                i++;
                continue;
            }
            if (c == '"')
            {
                index = i + 1;
                return true;
            }
            if (c < 0x20) return false;
            i++;
        }
        return false;
    }

    private static bool SkipValue(string json, ref int index, int end)
    {
        if (index >= end) return false;
        char c = json[index];
        if (c == '{' || c == '[')
        {
            int depth = 0;
            int i = index;
            while (i < end)
            {
                char t = json[i];
                if (t == '"')
                {
                    if (!SkipString(json, ref i, end)) return false;
                    continue;
                }
                if (t == '{' || t == '[')
                {
                    depth++;
                    i++;
                    continue;
                }
                if (t == '}' || t == ']')
                {
                    depth--;
                    i++;
                    if (depth == 0)
                    {
                        index = i;
                        return true;
                    }
                    continue;
                }
                i++;
            }
            return false;
        }

        if (c == '"') return SkipString(json, ref index, end);

        int literalStart = index;
        while (index < end && !IsLiteralTerminator(json[index])) index++;
        if (index == literalStart) return false;
        return IsJsonLiteral(json, literalStart, index);
    }

    private static bool IsJsonLiteral(string json, int start, int endExclusive)
    {
        int length = endExclusive - start;
        if (length == 0) return false;
        if (length == 4 && json[start] == 't')
        {
            return json[start + 1] == 'r' && json[start + 2] == 'u' && json[start + 3] == 'e';
        }
        if (length == 4 && json[start] == 'n')
        {
            return json[start + 1] == 'u' && json[start + 2] == 'l' && json[start + 3] == 'l';
        }
        if (length == 5 && json[start] == 'f')
        {
            return json[start + 1] == 'a' && json[start + 2] == 'l'
                && json[start + 3] == 's' && json[start + 4] == 'e';
        }
        for (int i = start; i < endExclusive; i++)
        {
            char c = json[i];
            if (c >= '0' && c <= '9') continue;
            if (c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') continue;
            return false;
        }
        return length > 0;
    }

    private static bool IsLiteralTerminator(char c)
    {
        return c == ',' || c == '}' || c == ']' || IsJsonWhitespace(c);
    }

    private static bool IsHexDigit(char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }

    private static void SkipWhitespace(string json, ref int index, int end)
    {
        while (index < end && IsJsonWhitespace(json[index])) index++;
    }

    private static bool IsJsonWhitespace(char c)
    {
        return c == ' ' || c == '\t' || c == '\n' || c == '\r';
    }

    private static bool IsWellFormedObject(string json)
    {
        if (json == null) return false;
        try
        {
            using (JsonDocument document = JsonDocument.Parse(json, ParseOptions))
            {
                return document.RootElement.ValueKind == JsonValueKind.Object;
            }
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
