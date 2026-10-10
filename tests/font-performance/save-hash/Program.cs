using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KingdomEnhancedMod;

// source-linked 纯测试（net8.0）：直接编译生产 helper（PatchPerf_CanonicalHashBuffer.cs）
// 与三个真实 canonical hash 源（HeroRecruitmentFingerprint / MusketeerArchive.IslandHash /
// HeavyShieldSnapshotFingerprint），旧 oracle 真走 AppendData(stream.ToArray())。
//
// 断言目标：
//  1) 新 helper 与旧 ToArray 路径在真实三域输出逐字节一致（含 capacity/stale tail/Unicode/NUL）；
//  2) 只读 committed Length：capacity 与 Length 之后的陈旧字节绝不参与 hash；
//  3) 属性顺序、重复属性失败关闭、三时钟例外、既有 validator 行为不变；
//  4) 独立 spec golden（canonical 文本 == 输入文本的样本）验证 oracle 本身；
//  5) alloc 差值 = 真实 canonical stream.Length/次（LOH 级），并如实标注非 Native 结论。
internal static class Program
{
    private static int _fail;
    private static int _checks;

    private delegate void Appender(IncrementalHash hash, MemoryStream stream);

    // 旧生产行（oracle 必须真正走 ToArray）
    private static readonly Appender OldAppend = (hash, stream) => hash.AppendData(stream.ToArray());
    // 新 helper 路径
    private static readonly Appender NewAppend = (hash, stream) => CanonicalHashBuffer.AppendTo(hash, stream);

    private const string Guid = "0f8fad5b-d9cb-469f-a165-70867728950e";
    private const string ClockJson = "{\"playTimeDays\":11,\"lastPlayedTimeDays\":22,\"_islandTimePlayed\":33,\"rows\":1}";
    private const string ClockJsonMoved = "{\"playTimeDays\":91,\"lastPlayedTimeDays\":2,\"_islandTimePlayed\":0,\"rows\":1}";
    private const string ClockJsonData = "{\"playTimeDays\":11,\"lastPlayedTimeDays\":22,\"_islandTimePlayed\":33,\"rows\":2}";

    private static void Check(bool ok, string name)
    {
        _checks++;
        if (!ok) { _fail++; Console.WriteLine("FAIL " + name); }
        else Console.WriteLine("ok   " + name);
    }

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try { action(); return false; }
        catch (TException) { return true; }
        catch { return false; }
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    // 独立见证：spec 头 + 期望的 canonical 文本（仅用于 canonical 文本 == 输入文本的样本）。
    private static string SpecHash(string header, string canonicalText)
    {
        var headerBytes = Encoding.UTF8.GetBytes(header);
        var textBytes = Encoding.UTF8.GetBytes(canonicalText);
        var joined = new byte[headerBytes.Length + textBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, joined, 0, headerBytes.Length);
        Buffer.BlockCopy(textBytes, 0, joined, headerBytes.Length, textBytes.Length);
        return Hex(SHA256.HashData(joined));
    }

    private static string HeroHeader(string scope) => "hero-island-objects-v2\n" + scope + "\n";
    private static string MuskHeader(string scope) => "musketeer-island-objects-v2\n" + scope + "\n";
    private static string HeavyHeader(string guid, int challenge, int land) => "heavy-shield-island-objects-v3\n" + guid + "\n"
        + challenge.ToString(CultureInfo.InvariantCulture) + "\n" + land.ToString(CultureInfo.InvariantCulture) + "\n";

    // 三域共用的 canonical writer 纯复刻（与真实源除 append 策略外逐行同构）。
    private static string HashVia(string json, string header, Appender append, out int written, int capacity = 0, int staleTail = 0)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("island object required");
        using var stream = capacity > 0 ? new MemoryStream(capacity) : new MemoryStream();
        if (staleTail > 0)
        {
            var junk = new byte[staleTail];
            Array.Fill(junk, (byte)'A');
            stream.Write(junk, 0, junk.Length);   // 先写满，再回退长度：Length 之后留下陈旧字节
            stream.SetLength(0);
        }
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
        written = checked((int)stream.Length);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(header));
        append(hash, stream);
        return Hex(hash.GetHashAndReset());
    }

    private static string HashVia(string json, string header, Appender append)
        => HashVia(json, header, append, out _, 0, 0);

    private static int Main()
    {
        T1_SpecGolden();
        T2_LengthNotCapacity();
        T3_UnicodeAndNul();
        T4_PropertyOrder();
        T5_FailClosedValidation();
        T6_ThreeClocksAndScope();
        T7_DomainSeparation();
        T8_LargeSnapshot();
        T9_AllocDelta();
        T10_NullSurface();
        Console.WriteLine(_fail == 0 ? ("ALL PASS (" + _checks + " checks)") : ("FAILURES=" + _fail + "/" + _checks));
        return _fail == 0 ? 0 : 1;
    }

    // 1) 独立 spec golden：canonical 文本 == 输入文本，验证 oracle 与 helper 都对得上规范
    private static void T1_SpecGolden()
    {
        const string json = "{\"a\":1,\"b\":\"x\"}";
        Check(HashVia(json, HeroHeader("s1"),  OldAppend, out _, 0, 0) == SpecHash(HeroHeader("s1"), json), "spec golden hero (old)");
        Check(HashVia(json, HeroHeader("s1"),  NewAppend, out _, 0, 0) == SpecHash(HeroHeader("s1"), json), "spec golden hero (helper)");
        Check(HeroRecruitmentFingerprint.Hash(json, "s1") == SpecHash(HeroHeader("s1"), json), "real HeroRecruitmentFingerprint == spec");
        Check(MusketeerArchive.IslandHash(json, "s1") == SpecHash(MuskHeader("s1"), json), "real MusketeerArchive.IslandHash == spec");
        Check(HeavyShieldSnapshotFingerprint.Hash(Guid, 3, 12, json) == SpecHash(HeavyHeader(Guid, 3, 12), json), "real HeavyShield == spec");
    }

    // 2) 只读 committed Length：capacity 更大 + Length 之后陈旧字节不得参与
    private static void T2_LengthNotCapacity()
    {
        const string json = "{\"rows\":2,\"name\":\"x\"}";
        string d = HashVia(json, HeroHeader("s"),  NewAppend, out int len, 0, 0);
        string sized = HashVia(json, HeroHeader("s"), NewAppend, out int len2, 4096, 3000);
        string oldSized = HashVia(json, HeroHeader("s"),  OldAppend, out _, 4096, 3000);
        string oldPlain = HashVia(json, HeroHeader("s"),  OldAppend, out _, 0, 0);
        Check(len == len2 && len > 0 && len2 < 4096, "stale-tail/capacity case keeps same committed Length");
        Check(d == sized && d == oldSized && d == oldPlain, "helper == old ToArray across capacity/stale tail");
        Check(HeroRecruitmentFingerprint.Hash(json, "s") == d, "real hero source == helper on stale-tail semantics");

        // 反证：整 buffer（capacity）参与 hash 会得到不同结果 -> slice 是承重的
        string naive = HashFullBuffer(json);
        Check(naive != d, "hashing whole capacity would differ (slice is load-bearing)");

        // 字节级契约：GetBuffer()[0..Length] == ToArray() 字节
        using var stream = new MemoryStream(4096);
        var stale = new byte[3000];
        Array.Fill(stale, (byte)'Z');
        stream.Write(stale, 0, stale.Length);
        stream.SetLength(0);
        using (var writer = new Utf8JsonWriter(stream)) { writer.WriteStartObject(); writer.WriteNumber("rows", 2); writer.WriteEndObject(); }
        var toArray = stream.ToArray();
        var view = stream.GetBuffer().AsSpan(0, checked((int)stream.Length));
        Check(toArray.Length == stream.Length && stream.Capacity > stream.Length && view.SequenceEqual(toArray),
            "GetBuffer[0..Length] is byte-identical to ToArray()");
    }

    private static string HashFullBuffer(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream(4096);
        var stale = new byte[3000];
        Array.Fill(stale, (byte)'A');
        stream.Write(stale, 0, stale.Length);
        stream.SetLength(0);
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject()) property.WriteTo(writer);
            writer.WriteEndObject();
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(HeroHeader("s")));
        hash.AppendData(stream.GetBuffer());   // 故意不做 Length 切片
        return Hex(hash.GetHashAndReset());
    }

    // 3) Unicode / NUL / 转义：等价输入同一输出，内容差异必须改变输出
    private static void T3_UnicodeAndNul()
    {
        const string escaped = "{\"s\":\"\\u4e2d\\u6587\\ud83d\\ude00\"}";
        const string literal = "{\"s\":\"\u4e2d\u6587\U0001F600\"}";
        const string nul = "{\"s\":\"a\\u0000b\\tc\\\"d\\\\e\"}";
        const string nulOther = "{\"s\":\"a\\u0001b\\tc\\\"d\\\\e\"}";
        Check(HashVia(escaped, HeroHeader("s"), NewAppend) == HashVia(escaped, HeroHeader("s"), OldAppend), "unicode: helper == old");
        Check(HashVia(escaped, HeroHeader("s"), NewAppend) == HashVia(literal, HeroHeader("s"), NewAppend), "unicode: escaped == literal input");
        Check(HeroRecruitmentFingerprint.Hash(literal, "s") == HashVia(literal, HeroHeader("s"), NewAppend), "unicode: real hero == helper");
        Check(MusketeerArchive.IslandHash(nul, "s") == HashVia(nul, MuskHeader("s"), NewAppend), "nul: real musk == helper");
        Check(HeavyShieldSnapshotFingerprint.Hash(Guid, 0, 0, nul) == HashVia(nul, HeavyHeader(Guid, 0, 0), OldAppend), "nul: real heavy == old ToArray oracle");
        Check(HashVia(nul, HeroHeader("s"), NewAppend) != HashVia(nulOther, HeroHeader("s"), NewAppend), "nul vs \\u0001 differs");
    }

    // 4) 属性顺序保持
    private static void T4_PropertyOrder()
    {
        const string ab = "{\"a\":1,\"b\":2}";
        const string ba = "{\"b\":2,\"a\":1}";
        Check(HashVia(ab, HeroHeader("s"), NewAppend) == SpecHash(HeroHeader("s"), ab) && HashVia(ba, HeroHeader("s"), NewAppend) == SpecHash(HeroHeader("s"), ba), "order: helper matches spec for both orders");
        Check(HashVia(ab, HeroHeader("s"), OldAppend) == HashVia(ab, HeroHeader("s"), NewAppend)
            && HashVia(ba, HeroHeader("s"), OldAppend) == HashVia(ba, HeroHeader("s"), NewAppend), "order: old == helper both orders");
        Check(HashVia(ab, HeroHeader("s"), NewAppend) != HashVia(ba, HeroHeader("s"), NewAppend), "order participates in hash");
        Check(HeroRecruitmentFingerprint.Hash(ba, "s") == HashVia(ba, HeroHeader("s"), NewAppend), "order: real hero == helper");
    }

    // 5) 失败关闭与既有 validator
    private static void T5_FailClosedValidation()
    {
        const string dup = "{\"a\":1,\"a\":2}";
        const string array = "[1,2]";
        Check(Throws<FormatException>(() => HeroRecruitmentFingerprint.Hash(dup, "s")), "hero duplicate throws FormatException");
        Check(Throws<FormatException>(() => MusketeerArchive.IslandHash(dup, "s")), "musk duplicate throws FormatException");
        Check(Throws<FormatException>(() => HeavyShieldSnapshotFingerprint.Hash(Guid, 0, 0, dup)), "heavy duplicate throws FormatException");
        Check(Throws<FormatException>(() => HeroRecruitmentFingerprint.Hash(array, "s")), "hero array root throws FormatException");
        Check(Throws<FormatException>(() => MusketeerArchive.IslandHash(array, "s")), "musk array root throws FormatException");
        Check(Throws<FormatException>(() => HeavyShieldSnapshotFingerprint.Hash(Guid, 0, 0, array)), "heavy array root throws FormatException");
        Check(Throws<FormatException>(() => HashVia(dup, HeroHeader("s"), NewAppend)) && Throws<FormatException>(() => HashVia(dup, HeroHeader("s"), OldAppend)), "replica duplicate fails closed on both paths");
        Check(Throws<FormatException>(() => HeavyShieldSnapshotFingerprint.Hash("not-a-guid", 0, 0, "{}")), "heavy invalid guid preserved");
        Check(Throws<FormatException>(() => HeavyShieldSnapshotFingerprint.Hash(Guid, -1, 0, "{}")), "heavy negative challenge preserved");
        Check(Throws<FormatException>(() => HeavyShieldSnapshotFingerprint.Hash(Guid, 0, -1, "{}")), "heavy negative land preserved");
        Check(Throws<FormatException>(() => HeavyShieldSnapshotFingerprint.Hash(Guid, 0, 0, "")), "heavy empty island preserved");
        Check(Throws<ArgumentNullException>(() => HeroRecruitmentFingerprint.Hash(null, "s")), "hero null json preserved (ArgumentNullException)");
    }

    // 6) 三时钟例外 + scope 参与（真实三源，旧路径当前行为 vs helper 复刻）
    private static void T6_ThreeClocksAndScope()
    {
        Check(HeroRecruitmentFingerprint.Hash(ClockJson, "s") == HeroRecruitmentFingerprint.Hash(ClockJsonMoved, "s"), "hero: 3 clocks excluded");
        Check(HashVia(ClockJsonMoved, HeroHeader("s"), NewAppend) == HeroRecruitmentFingerprint.Hash(ClockJson, "s"), "hero: helper equals real with moved clocks");
        Check(HeroRecruitmentFingerprint.Hash(ClockJson, "s") != HeroRecruitmentFingerprint.Hash(ClockJsonData, "s"), "hero: data change changes hash");
        Check(HeroRecruitmentFingerprint.Hash(ClockJson, "a") != HeroRecruitmentFingerprint.Hash(ClockJson, "b"), "hero: scope participates");
        Check(MusketeerArchive.IslandHash(ClockJson, "s") == MusketeerArchive.IslandHash(ClockJsonMoved, "s")
            && HashVia(ClockJsonMoved, MuskHeader("s"), NewAppend) == MusketeerArchive.IslandHash(ClockJson, "s"), "musk: clocks excluded, helper == real");
        Check(HeavyShieldSnapshotFingerprint.Hash(Guid, 3, 12, ClockJson) == HeavyShieldSnapshotFingerprint.Hash(Guid, 3, 12, ClockJsonMoved)
            && HashVia(ClockJsonMoved, HeavyHeader(Guid, 3, 12), NewAppend) == HeavyShieldSnapshotFingerprint.Hash(Guid, 3, 12, ClockJson)
            && HeavyShieldSnapshotFingerprint.Hash(Guid, 3, 12, ClockJson) != HeavyShieldSnapshotFingerprint.Hash(Guid, 3, 13, ClockJson), "heavy: clocks excluded, land participates, helper == real");
    }

    // 7) 域分离（不共享 hash 空间）
    private static void T7_DomainSeparation()
    {
        string hero = HeroRecruitmentFingerprint.Hash(ClockJson, "s");
        string musk = MusketeerArchive.IslandHash(ClockJson, "s");
        string heavy = HeavyShieldSnapshotFingerprint.Hash(Guid, 0, 0, ClockJson);
        Check(hero != musk && musk != heavy && hero != heavy, "three domains stay separated");
    }

    // 8) 大 snapshot（> LOH 阈值）真实源 == helper
    private static void T8_LargeSnapshot()
    {
        string json = BuildIsland(12000);
        string real = HeroRecruitmentFingerprint.Hash(json, "big");
        string helper = HashVia(json, HeroHeader("big"),  NewAppend, out int len, 0, 0);
        string old = HashVia(json, HeroHeader("big"), OldAppend);
        Check(len > 85000, "large snapshot crosses LOH threshold (" + len + " bytes)");
        Check(real == helper && helper == old, "large snapshot: real source == helper == old oracle");
    }

    // 9) alloc 差值 = canonical stream.Length/次（如实度量，非 Native 结论）
    private static void T9_AllocDelta()
    {
        string json = BuildIsland(40000);
        Check(HashVia(json, HeroHeader("bench"),  NewAppend, out int len, 0, 0) == HashVia(json, HeroHeader("bench"), OldAppend), "bench: outputs identical");
        HashVia(json, HeroHeader("bench"), OldAppend);
        HashVia(json, HeroHeader("bench"), NewAppend);
        const int iterations = 6;
        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) HashVia(json, HeroHeader("bench"), OldAppend);
        sw.Stop();
        long oldAlloc = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
        double oldMs = sw.Elapsed.TotalMilliseconds / iterations;
        before = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        for (int i = 0; i < iterations; i++) HashVia(json, HeroHeader("bench"), NewAppend);
        sw.Stop();
        long newAlloc = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
        double newMs = sw.Elapsed.TotalMilliseconds / iterations;
        long saved = oldAlloc - newAlloc;
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "bench canonical={0}B alloc_old={1}B alloc_new={2}B saved={3}B/call time_old={4:F2}ms time_new={5:F2}ms (managed only, not a Native claim)",
            len, oldAlloc, newAlloc, saved, oldMs, newMs));
        Check(saved >= len * 9L / 10 && saved <= len * 2L, "alloc delta is canonical stream.Length scale");
    }

    // 10) 异常面不扩大：null stream / null hash 与旧行同为 NRE
    private static void T10_NullSurface()
    {
        Check(Throws<NullReferenceException>(() => OldAppend(null, new MemoryStream())), "old: null hash -> NRE");
        Check(Throws<NullReferenceException>(() => NewAppend(null, new MemoryStream())), "helper: null hash -> NRE");
        Check(Throws<NullReferenceException>(() => OldAppend(IncrementalHash.CreateHash(HashAlgorithmName.SHA256), null)), "old: null stream -> NRE");
        Check(Throws<NullReferenceException>(() => NewAppend(IncrementalHash.CreateHash(HashAlgorithmName.SHA256), null)), "helper: null stream -> NRE");
    }

    private static string BuildIsland(int properties)
    {
        var builder = new StringBuilder(properties * 40);
        builder.Append('{');
        for (int i = 0; i < properties; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append("\"prop").Append(i.ToString("D6", CultureInfo.InvariantCulture))
                .Append("\":\"value-").Append(i.ToString(CultureInfo.InvariantCulture)).Append('-')
                .Append(new string('x', 12)).Append('"');
        }
        builder.Append('}');
        return builder.ToString();
    }
}
