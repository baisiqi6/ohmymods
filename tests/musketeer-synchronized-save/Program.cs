using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KingdomEnhancedMod;

static class Check
{
    internal static int Count;

    internal static void Eq<T>(T expected, T actual, string label)
    {
        if (!Equals(expected, actual))
            throw new Exception(label + ": expected <" + expected + ">, got <" + actual + ">");
        Count++;
    }

    internal static void True(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Count++;
    }

    internal static void False(bool condition, string label) => True(!condition, label);
}

// Contract tests for the pure synchronized-save slice: MusketeerNativeArchive document codec
// and MusketeerPairedBackup container. Production sources are linked in (no test-only copy).
// Isolated temp directories only; no game files and no shared paths are touched.
static class Program
{
    private const string NativeFile = "global-v35";
    private static readonly UTF8Encoding NoBom = new(false);
    private static string Root;

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "kem-musketeer-synchronized-save-" + Guid.NewGuid().ToString("N"));
        Root = root;
        Directory.CreateDirectory(root);
        try
        {
            Constants();
            EncodeInputRules();
            DocumentRoundtripIdentity();
            ContextIsolation();
            DecodeRules();
            ExtractRules();
            ExtractHappyPath();
            PairSaveRules();
            PairMatchingRules();
            PairBackupLifecycle();
            ReviewR1Regressions();
            PairOverwriteRules();
            ReviewR2Regressions();
            Console.WriteLine("PASS " + Check.Count + " assertions (musketeer synchronized-save pure helpers)");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("FAIL after " + Check.Count + " passed assertions");
            Console.WriteLine(e.ToString());
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    // ---- fixed contract constants ---------------------------------------------------------

    private static void Constants()
    {
        Console.WriteLine("[case] constants");
        Check.Eq("KingdomEnhancedMod_MusketeerRights_v1", MusketeerNativeArchive.Key, "fixed native prefs key");
        Check.Eq(1, MusketeerNativeArchive.Version, "document version 1");
        Check.Eq(24 * 1024 * 1024, MusketeerNativeArchive.MaxDocumentBytes, "document max 24MB");
        Check.Eq(16 * 1024 * 1024, MusketeerNativeArchive.MaxArchiveBytes, "archive max 16MB");
        Check.Eq(32 * 1024 * 1024, MusketeerNativeArchive.MaxNativeCompressedBytes, "native compressed max 32MB");
        Check.Eq(128 * 1024 * 1024, MusketeerNativeArchive.MaxNativeDecompressedBytes, "native decompressed max 128MB");
        Check.Eq(50 * 1024 * 1024, MusketeerPairedBackup.MaxPairBytes, "pair max 50MB");
        Check.Eq(1, MusketeerPairedBackup.Version, "pair version 1");
    }

    // ---- MusketeerNativeArchive -----------------------------------------------------------------

    private static void EncodeInputRules()
    {
        Console.WriteLine("[case] encode input rules");
        var archive = new MusketeerArchive();
        Check.False(MusketeerNativeArchive.TryEncode(null, archive, out _, out string reason), "null file refused");
        Check.Eq("file", reason, "null file reason");
        Check.False(MusketeerNativeArchive.TryEncode("", archive, out _, out reason), "empty file refused");
        Check.Eq("file", reason, "empty file reason");
        Check.False(MusketeerNativeArchive.TryEncode("a/b", archive, out _, out reason), "slash file refused");
        Check.Eq("file", reason, "slash file reason");
        Check.False(MusketeerNativeArchive.TryEncode("a\\b", archive, out _, out reason), "backslash file refused");
        Check.Eq("file", reason, "backslash file reason");
        Check.False(MusketeerNativeArchive.TryEncode("a:b", archive, out _, out reason), "colon file refused");
        Check.Eq("file", reason, "colon file reason");
        Check.False(MusketeerNativeArchive.TryEncode(NativeFile, null, out _, out reason), "null archive refused");
        Check.Eq("archive", reason, "null archive reason");
    }

    private static void DocumentRoundtripIdentity()
    {
        Console.WriteLine("[case] document roundtrip identity (dropped unit -> gun keeps GUID)");
        var archive = BuildCareerArchive(out string scope, out string unitHash, out string gunHash, out Guid careerId);
        string contextKey = MusketeerArchive.ContextKey(NativeFile, 1, 0, 1);
        Check.True(archive.TryGetContext(contextKey, out var context) && context.Active == scope, "context registered");
        Check.True(MusketeerNativeArchive.TryEncode(NativeFile, archive, out string raw, out string encodeReason), "encode: " + encodeReason);
        Check.True(raw.StartsWith("{\"version\":1,\"file\":\"global-v35\",\"archive\":\"", StringComparison.Ordinal), "document exact field set/order");
        Check.True(raw.EndsWith("\"}", StringComparison.Ordinal), "document has no trailing content");
        Check.True(MusketeerNativeArchive.TryDecode(NativeFile, raw, out var decoded, out bool unsupported, out string decodeReason), "decode: " + decodeReason);
        Check.False(unsupported, "decoded document supported");
        Check.True(decoded.TryGet(scope, unitHash, out var unitSnapshot), "unit snapshot present");
        Check.True(decoded.TryGet(scope, gunHash, out var gunSnapshot), "gun snapshot present");
        Check.Eq(careerId, unitSnapshot.Records.Single(x => x.Kind == MusketeerCareer.KindUnit).Id, "unit keeps GUID");
        var transferred = gunSnapshot.Records.Single(x => x.Id == careerId);
        Check.Eq(MusketeerCareer.KindGun, transferred.Kind, "dropped unit saved as gun keeps same GUID");
        Check.Eq("npc-row-9", transferred.NativeId, "gun native id kept");
        Check.Eq(2, transferred.StockSlot, "gun stock slot kept");
        Check.True(MusketeerArchive.Same(archive.Scopes[scope].Find(x => x.Hash == unitHash).Records, unitSnapshot.Records), "unit records identity");
        Check.True(MusketeerArchive.Same(archive.Scopes[scope].Find(x => x.Hash == gunHash).Records, gunSnapshot.Records), "gun records identity");
        Check.True(decoded.TryGetContext(contextKey, out var decodedContext) && decodedContext.Active == scope, "context roundtrip");
        Check.True(archive.Encode().AsSpan().SequenceEqual(decoded.Encode()), "archive bytes identity after roundtrip");
    }

    private static void ContextIsolation()
    {
        Console.WriteLine("[case] island/context isolation");
        var archive = new MusketeerArchive();
        string scopeA = MusketeerArchive.NewScope(), scopeB = MusketeerArchive.NewScope();
        string hashA = MusketeerArchive.Hash("island-A", scopeA), hashB = MusketeerArchive.Hash("island-B", scopeB);
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        Check.True(archive.Record(scopeA, hashA, new[] { new MusketeerCareer { Id = idA, Kind = MusketeerCareer.KindUnit, NativeId = "npc-A" } }), "record island A");
        Check.True(archive.Record(scopeB, hashB, new[] { new MusketeerCareer { Id = idB, Kind = MusketeerCareer.KindUnit, NativeId = "npc-B" } }), "record island B");
        string contextA = MusketeerArchive.ContextKey(NativeFile, 1, 0, 1), contextB = MusketeerArchive.ContextKey(NativeFile, 1, 0, 2);
        Check.True(archive.EnsureContext(contextA, scopeA, true), "context A registered");
        Check.True(archive.EnsureContext(contextB, scopeB, true), "context B registered");
        Check.True(MusketeerNativeArchive.TryEncode(NativeFile, archive, out string raw, out _), "encode");
        Check.True(MusketeerNativeArchive.TryDecode(NativeFile, raw, out var decoded, out _, out string reason), "decode: " + reason);
        Check.True(decoded.TryGetContext(contextA, out var decodedA) && decodedA.Active == scopeA && decodedA.Epochs.Single() == scopeA, "context A isolated");
        Check.True(decoded.TryGetContext(contextB, out var decodedB) && decodedB.Active == scopeB && decodedB.Epochs.Single() == scopeB, "context B isolated");
        Check.True(decoded.TryGet(scopeA, hashA, out var snapshotA) && snapshotA.Records.Single().Id == idA, "A record in A scope");
        Check.False(decoded.TryGet(scopeB, hashA, out _), "A hash never crosses scopes");
        Check.False(decoded.TryGet(scopeA, hashB, out _), "B hash never in A scope");
    }

    private static void DecodeRules()
    {
        Console.WriteLine("[case] document decode rules");
        var archive = BuildCareerArchive(out _, out _, out _, out _);
        Check.True(MusketeerNativeArchive.TryEncode(NativeFile, archive, out string good, out _), "baseline encode");
        string payload = ArchiveBase64(good);

        MustReject(null, "document", "null raw refused");
        MustReject("", "document", "empty raw refused");
        MustReject("\uFEFF" + good, "bom", "utf8 BOM refused");
        MustReject("not json", "json", "garbage refused");
        MustReject("[]", "document", "non-object refused");
        MustReject("{\"version\":1,\"file\":\"global-v35\"}", "document", "missing field refused");
        MustReject(Doc("1", Esc(NativeFile), Esc(payload), "\"extra\":1,"), "document", "unknown field refused");
        MustReject("{\"version\":1,\"file\":" + Esc(NativeFile) + ",\"archive\":" + Esc(payload) + ",\"archive\":" + Esc(payload) + "}", "document", "duplicate field refused");
        MustReject(Doc("\"1\"", Esc(NativeFile), Esc(payload)), "document", "string version refused");
        MustReject(Doc("1", "1", Esc(payload)), "document", "number file refused");
        MustReject(Doc("1", Esc(NativeFile), "true"), "document", "bool archive refused");
        MustReject(Doc("0", Esc(NativeFile), Esc(payload)), "document", "version 0 refused");
        MustReject(Doc("2", Esc(NativeFile), Esc(payload)), "version", "future document is unsupported", true);
        MustReject(Doc("1", Esc("other-native-file"), Esc(payload)), "file", "document file mismatch refused");
        MustReject(good + "x", "json", "trailing content refused");
        MustReject("x" + good, "json", "leading content refused");

        MustReject(Doc("1", Esc(NativeFile), Esc(payload + "=")), "base64", "extra padding refused");
        MustReject(Doc("1", Esc(NativeFile), Esc("AA A A A")), "base64", "whitespace base64 refused");
        MustReject(Doc("1", Esc(NativeFile), Esc("AA-_")), "base64", "url-safe base64 refused");
        MustReject(Doc("1", Esc(NativeFile), Esc("AAA")), "base64", "truncated base64 refused");
        MustReject(Doc("1", Esc(NativeFile), Esc("AAAA=")), "base64", "bad padding length refused");
        MustReject(Doc("1", Esc(NativeFile), Esc("")), "base64", "empty archive refused");

        MustReject(Doc("1", Esc(NativeFile), Esc(Convert.ToBase64String(Encoding.UTF8.GetBytes("hello")))), "archive", "archive payload not an archive");
        MustReject(Doc("1", Esc(NativeFile), Esc(Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"schemaVersion\":3}")))), "archive-version", "future archive is unsupported", true);
        MustReject(Doc("1", Esc(NativeFile), Esc(new string('A', 22_400_000))), "archive-size", "archive over 16MB refused");
        MustReject(new string('a', 25 * 1024 * 1024), "size", "document over 24MB refused");
        Check.False(MusketeerNativeArchive.TryDecode("x/y", good, out _, out _, out string badFileReason), "bad file argument refused");
        Check.Eq("file", badFileReason, "bad file argument reason");
    }

    private static void ExtractRules()
    {
        Console.WriteLine("[case] extract rules");
        var archive = BuildCareerArchive(out _, out _, out _, out _);
        Check.True(MusketeerNativeArchive.TryEncode(NativeFile, archive, out string document, out _), "encode");
        string payload = ArchiveBase64(document);

        MustRejectNative(null, "native", "null native refused");
        MustRejectNative(new byte[0], "native", "empty native refused");
        MustRejectNative(new byte[32 * 1024 * 1024 + 1], "size", "compressed over 32MB refused");
        MustRejectNative(new byte[] { 1, 2, 3, 4 }, "gzip", "not gzip refused");
        MustRejectNative(Gzip("not json"), "json", "gzip but not json refused");
        MustRejectNative(GzipRaw(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{}")).ToArray()), "bom", "utf8 BOM refused");
        MustRejectNative(Gzip("[]"), "root", "root not object refused");
        MustRejectNative(Gzip("{}"), "prefs", "no prefs refused");
        MustRejectNative(Gzip("{\"prefs\":[]}"), "prefs", "prefs not object refused");
        MustRejectNative(Gzip("{\"prefs\":{},\"prefs\":{}}"), "root", "duplicate root property refused");
        MustRejectNative(Gzip("{\"prefs\":{\"srzEntries\":{}}}"), "entries", "entries not array refused");
        MustRejectNative(Gzip(GlobalJson("[1]")), "entry", "entry not object refused");
        MustRejectNative(Gzip(GlobalJson("{\"key\":\"x\"}")), "entry", "entry missing val refused");
        MustRejectNative(Gzip(GlobalJson("{\"key\":\"x\",\"val\":5}")), "entry", "entry val not string refused");
        MustRejectNative(Gzip(GlobalJson("{\"key\":\"x\",\"val\":\"y\",\"key\":\"x\"}")), "entry", "duplicate entry property refused");
        MustRejectNative(Gzip(GlobalJson(Entry("other-key", "x"))), "native-key-missing", "missing musk key refused");
        MustRejectNative(Gzip(GlobalJson(Entry(MusketeerNativeArchive.Key, document) + "," + Entry(MusketeerNativeArchive.Key, document))), "native-key", "duplicate musk key refused");
        MustRejectNative(Gzip(GlobalJson(Entry(MusketeerNativeArchive.Key, Doc("2", Esc(NativeFile), Esc(payload))))), "version", "future document propagated", true);
        MustRejectNative(Gzip(GlobalJson(Entry(MusketeerNativeArchive.Key, Doc("1", Esc("other"), Esc(payload))))), "file", "document file mismatch refused");
        MustRejectNative(GzipRaw(new byte[129 * 1024 * 1024]), "size", "decompressed over 128MB refused");
    }

    private static void ExtractHappyPath()
    {
        Console.WriteLine("[case] extract from real-shaped gzip global");
        var archive = BuildCareerArchive(out string scope, out string unitHash, out _, out Guid careerId);
        Check.True(MusketeerNativeArchive.TryEncode(NativeFile, archive, out string document, out _), "encode");
        string global = GlobalJson(Entry("unrelated", "value") + "," + Entry(MusketeerNativeArchive.Key, document) + "," + Entry("another", "value"));
        byte[] native = Gzip(global);
        Check.True(MusketeerNativeArchive.TryExtract(native, NativeFile, out string raw, out var decoded, out bool unsupported, out string reason), "extract: " + reason);
        Check.False(unsupported, "supported");
        Check.Eq(document, raw, "raw equals actual payload exactly");
        Check.True(decoded.TryGet(scope, unitHash, out var snapshot) && snapshot.Records.Any(x => x.Id == careerId), "identity survives gzip path");
        Check.False(MusketeerNativeArchive.TryExtract(native, "other-native-file", out _, out _, out _, out reason), "extract file mismatch refused");
        Check.Eq("file", reason, "extract file mismatch reason");
    }

    // ---- MusketeerPairedBackup ------------------------------------------------------------------

    private static void PairSaveRules()
    {
        Console.WriteLine("[case] pair save rules");
        string dir = NewDir();
        string path = Path.Combine(dir, "musketeer-pair.json");
        var archive = BuildCareerArchive(out _, out _, out _, out _);
        Check.True(MusketeerNativeArchive.TryEncode(NativeFile, archive, out string document, out _), "encode");
        byte[] native = Gzip(GlobalJson(Entry("other", "x") + "," + Entry(MusketeerNativeArchive.Key, document)));

        Check.False(MusketeerPairedBackup.Save(path, "bad/name", native, out string reason), "bad file refused");
        Check.Eq("file", reason, "bad file reason");
        Check.False(MusketeerPairedBackup.Save(path, NativeFile, null, out reason), "null native refused");
        Check.Eq("native", reason, "null native reason");
        Check.False(MusketeerPairedBackup.Save(path, NativeFile, new byte[0], out reason), "empty native refused");
        Check.Eq("native", reason, "empty native reason");
        Check.False(MusketeerPairedBackup.Save(path, NativeFile, new byte[32 * 1024 * 1024 + 1], out reason), "oversized native refused");
        Check.Eq("native-size", reason, "oversized native reason");
        Check.False(MusketeerPairedBackup.Save(path, NativeFile, new byte[] { 9, 9, 9 }, out reason), "non-gzip native refused");
        Check.Eq("gzip", reason, "non-gzip native reason");
        Check.False(MusketeerPairedBackup.Save(path, NativeFile, Gzip(GlobalJson(Entry("other", "x"))), out reason), "native without musk key refused");
        Check.Eq("native-key-missing", reason, "native without musk key reason");
        Check.False(MusketeerPairedBackup.Save(path, NativeFile, Gzip(GlobalJson(Entry(MusketeerNativeArchive.Key, Doc("2", Esc(NativeFile), Esc(ArchiveBase64(document)))))), out reason), "future document native refused");
        Check.Eq("unsupported:version", reason, "future document native reason");
        Check.False(Directory.EnumerateFileSystemEntries(dir).Any(), "failed saves created nothing");

        Check.True(MusketeerPairedBackup.Save(path, NativeFile, native, out string saveReason), "save: " + saveReason);
        string container = File.ReadAllText(path, NoBom);
        Check.True(container.StartsWith("{\"version\":1,\"file\":\"global-v35\",\"nativeSha256\":\"", StringComparison.Ordinal), "pair exact field set/order");
        Check.True(container.Contains("\"archiveSha256\":\"", StringComparison.Ordinal), "pair archive digest field");
        Check.True(container.Contains("\"native\":\"", StringComparison.Ordinal), "pair native field");
        using (var parsed = JsonDocument.Parse(container))
        {
            var root = parsed.RootElement;
            Check.Eq(ShaHex(native), root.GetProperty("nativeSha256").GetString(), "stored native digest matches bytes");
            byte[] stored = Convert.FromBase64String(root.GetProperty("native").GetString());
            Check.True(stored.AsSpan().SequenceEqual(native), "stored native bytes exact");
        }
        Check.False(File.Exists(path + ".bak"), "first save has no bak");
        Check.Eq(1, Directory.GetFileSystemEntries(dir).Length, "save writes exactly the primary");
    }

    private static void PairMatchingRules()
    {
        Console.WriteLine("[case] paired recovery matching");
        string dir = NewDir();
        string path = Path.Combine(dir, "pair.json");
        var archiveA = BuildCareerArchive(out string scopeA, out string hashA, out _, out Guid idA);
        Check.True(MusketeerNativeArchive.TryEncode(NativeFile, archiveA, out string documentA, out _), "encode A");
        byte[] nativeA = Gzip(GlobalJson(Entry(MusketeerNativeArchive.Key, documentA)));
        var archiveB = BuildCareerArchive(out string scopeB, out string hashB, out _, out Guid idB);
        Check.True(MusketeerNativeArchive.TryEncode(NativeFile, archiveB, out string documentB, out _), "encode B");
        byte[] nativeB = Gzip(GlobalJson(Entry("other", "x") + "," + Entry(MusketeerNativeArchive.Key, documentB)));
        Check.True(MusketeerPairedBackup.Save(path, NativeFile, nativeA, out string saveReason), "save A: " + saveReason);
        string pair = File.ReadAllText(path, NoBom);

        Check.True(MusketeerPairedBackup.TryRecover(path, NativeFile, nativeA, out string raw, out var recovered, out string recoverReason), "recover same pair: " + recoverReason);
        Check.Eq(documentA, raw, "recovered raw exact");
        Check.True(recovered.TryGet(scopeA, hashA, out var snapshot) && snapshot.Records.Any(x => x.Id == idA), "recovered archive identity");
        Check.Eq(pair, File.ReadAllText(path, NoBom), "recover leaves pair untouched");
        Check.Eq(1, Directory.GetFileSystemEntries(dir).Length, "recover writes nothing");

        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, nativeB, out _, out _, out recoverReason), "different native refused");
        Check.Eq("mismatch", recoverReason, "different native reason");
        byte[] tweaked = (byte[])nativeA.Clone();
        tweaked[tweaked.Length - 1] ^= 0x01;
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, tweaked, out _, out _, out recoverReason), "one-byte native change refused");
        Check.Eq("mismatch", recoverReason, "byte change reason");
        Check.False(MusketeerPairedBackup.TryRecover(path, "other-native-file", nativeA, out _, out _, out recoverReason), "foreign file name refused");
        Check.Eq("pair-file", recoverReason, "foreign file name reason");

        File.WriteAllText(path, FlipField(pair, "\"nativeSha256\":\""), NoBom);
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, nativeA, out _, out _, out recoverReason), "tampered native digest refused");
        Check.Eq("pair-digest", recoverReason, "tampered native digest reason");
        File.WriteAllText(path, FlipField(pair, "\"archiveSha256\":\""), NoBom);
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, nativeA, out _, out _, out recoverReason), "tampered archive digest refused");
        Check.Eq("archive-digest", recoverReason, "tampered archive digest reason");
        File.WriteAllText(path, FlipField(pair, "\"native\":\""), NoBom);
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, nativeA, out _, out _, out recoverReason), "tampered native payload refused");
        Check.Eq("pair-digest", recoverReason, "tampered native payload reason");

        File.WriteAllText(path, InjectAfterBrace(pair, "\"extra\":1,"), NoBom);
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, nativeA, out _, out _, out recoverReason), "unknown pair field refused");
        Check.Eq("pair-document", recoverReason, "unknown pair field reason");
        File.WriteAllText(path, pair.Replace("\"file\":\"global-v35\",", "\"file\":\"global-v35\",\"file\":\"global-v35\",", StringComparison.Ordinal), NoBom);
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, nativeA, out _, out _, out recoverReason), "duplicate pair field refused");
        Check.Eq("pair-document", recoverReason, "duplicate pair field reason");
        File.WriteAllText(path, pair.Replace("\"version\":1,", "\"version\":\"1\",", StringComparison.Ordinal), NoBom);
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, nativeA, out _, out _, out recoverReason), "string pair version refused");
        Check.Eq("pair-document", recoverReason, "string pair version reason");
        File.WriteAllText(path, pair.Replace("\"version\":1,", "\"version\":2,", StringComparison.Ordinal), NoBom);
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, nativeA, out _, out _, out recoverReason), "future pair refused");
        Check.Eq("pair-version", recoverReason, "future pair reason");
        File.WriteAllText(path, pair + "x", NoBom);
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, nativeA, out _, out _, out recoverReason), "trailing pair content refused");
        Check.Eq("pair-json", recoverReason, "trailing pair content reason");
        File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(pair)).ToArray());
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, nativeA, out _, out _, out recoverReason), "pair BOM refused");
        Check.Eq("pair-bom", recoverReason, "pair BOM reason");

        File.WriteAllText(path, pair, NoBom);
        File.Delete(path);
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, nativeA, out _, out _, out recoverReason), "missing pair refused");
        Check.Eq("pair-missing", recoverReason, "missing pair reason");

        File.WriteAllBytes(path, new byte[MusketeerPairedBackup.MaxPairBytes + 1]);
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, nativeA, out _, out _, out recoverReason), "oversized pair refused");
        Check.Eq("pair-size", recoverReason, "oversized pair reason");
        File.WriteAllBytes(path, new byte[] { 1 });
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, nativeA, out _, out _, out recoverReason), "stub pair refused");
        Check.Eq("pair-corrupt", recoverReason, "stub pair reason");
    }

    private static void PairBackupLifecycle()
    {
        Console.WriteLine("[case] pair backup lifecycle under corruption");
        string dir = NewDir();
        string path = Path.Combine(dir, "pair.json");
        var archive1 = BuildCareerArchive(out string scope1, out string hash1, out _, out Guid id1);
        Check.True(MusketeerNativeArchive.TryEncode(NativeFile, archive1, out string document1, out _), "encode 1");
        byte[] native1 = Gzip(GlobalJson(Entry(MusketeerNativeArchive.Key, document1)));
        var archive2 = BuildCareerArchive(out string scope2, out _, out _, out _);
        Check.True(MusketeerNativeArchive.TryEncode(NativeFile, archive2, out string document2, out _), "encode 2");
        byte[] native2 = Gzip(GlobalJson(Entry(MusketeerNativeArchive.Key, document2)));

        Check.True(MusketeerPairedBackup.Save(path, NativeFile, native1, out string saveReason), "save 1: " + saveReason);
        byte[] primary1 = File.ReadAllBytes(path);
        Check.True(MusketeerPairedBackup.Save(path, NativeFile, native2, out saveReason), "save 2: " + saveReason);
        byte[] primary2 = File.ReadAllBytes(path);
        Check.True(File.Exists(path + ".bak"), "second save creates bak");
        Check.True(File.ReadAllBytes(path + ".bak").AsSpan().SequenceEqual(primary1), "bak keeps previous primary bytes");
        using (var parsed = JsonDocument.Parse(Encoding.UTF8.GetString(primary2)))
            Check.Eq(ShaHex(native2), parsed.RootElement.GetProperty("nativeSha256").GetString(), "primary now describes native2");
        Check.True(MusketeerPairedBackup.TryRecover(path, NativeFile, native2, out string raw2, out _, out string recoverReason), "primary recovers current pair: " + recoverReason);
        Check.Eq(document2, raw2, "current raw exact");
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, native1, out _, out _, out recoverReason), "older native never guessed from bak while primary valid");
        Check.Eq("mismatch", recoverReason, "older native reason");

        // Corrupt primary: bak (native1) still recovers exactly that pairing.
        byte[] bakBytes = File.ReadAllBytes(path + ".bak");
        byte[] corruption = Encoding.UTF8.GetBytes("corrupted primary bytes");
        File.WriteAllBytes(path, corruption);
        Check.True(MusketeerPairedBackup.TryRecover(path, NativeFile, native1, out string raw1, out var recovered1, out recoverReason), "corrupt primary uses valid bak: " + recoverReason);
        Check.Eq(document1, raw1, "bak recovered raw exact");
        Check.True(recovered1.TryGet(scope1, hash1, out var snapshot1) && snapshot1.Records.Any(x => x.Id == id1), "bak recovered identity");
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, native2, out _, out _, out recoverReason), "corrupt primary + mismatching bak refused");
        Check.Eq("mismatch", recoverReason, "corrupt primary + mismatching bak reason");
        Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(corruption), "recover never repairs primary");
        Check.True(File.ReadAllBytes(path + ".bak").AsSpan().SequenceEqual(bakBytes), "recover never rewrites bak");

        // Corrupt primary refuses Save and preserves both files untouched.
        Check.False(MusketeerPairedBackup.Save(path, NativeFile, native2, out string refuseReason), "corrupt primary refuses save");
        Check.Eq("pair-json", refuseReason, "corrupt primary save reason");
        Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(corruption), "primary untouched on refused save");
        Check.True(File.ReadAllBytes(path + ".bak").AsSpan().SequenceEqual(bakBytes), "bak untouched on refused save");

        // Future primary refuses Save and TryRecover (no downgrade to a matching bak).
        string future = Encoding.UTF8.GetString(bakBytes).Replace("\"version\":1,", "\"version\":2,", StringComparison.Ordinal);
        File.WriteAllText(path, future, NoBom);
        Check.False(MusketeerPairedBackup.Save(path, NativeFile, native1, out refuseReason), "future primary refuses save");
        Check.Eq("pair-version", refuseReason, "future primary save reason");
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, native1, out _, out _, out recoverReason), "future primary refuses recovery");
        Check.Eq("pair-version", recoverReason, "future primary recovery reason");
        Check.True(File.ReadAllBytes(path + ".bak").AsSpan().SequenceEqual(bakBytes), "future primary never overwrites bak");

        // Missing primary recovers from bak; missing both is observable.
        File.Delete(path);
        Check.True(MusketeerPairedBackup.TryRecover(path, NativeFile, native1, out string rawMissing, out _, out recoverReason), "missing primary uses bak: " + recoverReason);
        Check.Eq(document1, rawMissing, "missing primary bak raw exact");
        File.Delete(path + ".bak");
        Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, native1, out _, out _, out recoverReason), "missing pair refused");
        Check.Eq("pair-missing", recoverReason, "missing pair reason");
    }

    private static void PairOverwriteRules()
    {
        Console.WriteLine("[case] pair overwrite rules");
        string parent = NewDir();
        string path = Path.Combine(parent, "pair.json");
        var archive = BuildCareerArchive(out _, out _, out _, out _);
        Check.True(MusketeerNativeArchive.TryEncode(NativeFile, archive, out string document, out _), "encode");
        byte[] native = Gzip(GlobalJson(Entry(MusketeerNativeArchive.Key, document)));

        string occupied = Path.Combine(parent, "occupied");
        Directory.CreateDirectory(occupied);
        Check.False(MusketeerPairedBackup.Save(occupied, NativeFile, native, out string reason), "directory path refused");
        Check.Eq("pair-directory", reason, "directory path reason");
        Check.True(Directory.Exists(occupied), "directory kept");
        Check.Eq(1, Directory.GetFileSystemEntries(parent).Length, "no temp leftovers after directory refusal");

        Check.True(MusketeerPairedBackup.Save(path, NativeFile, native, out _), "initial save");
        var foreignArchive = BuildCareerArchive(out _, out _, out _, out _);
        Check.True(MusketeerNativeArchive.TryEncode("other-native-file", foreignArchive, out string foreignDocument, out _), "encode foreign");
        byte[] foreignNative = Gzip(GlobalJson(Entry(MusketeerNativeArchive.Key, foreignDocument)));
        Check.False(MusketeerPairedBackup.Save(path, "other-native-file", foreignNative, out reason), "foreign file primary refuses save");
        Check.Eq("pair-file", reason, "foreign file primary reason");
        Check.False(File.Exists(path + ".bak"), "foreign file refusal does not rotate bak");
    }

    // ---- r1 review regressions -----------------------------------------------------------------

    private static void ReviewR1Regressions()
    {
        Console.WriteLine("[case] r1 review regressions");
        var archive1 = BuildCareerArchive(out _, out _, out _, out _);
        Check.True(MusketeerNativeArchive.TryEncode(NativeFile, archive1, out string document1, out _), "encode 1");
        byte[] native1 = Gzip(GlobalJson(Entry(MusketeerNativeArchive.Key, document1)));
        var archive2 = BuildCareerArchive(out _, out _, out _, out _);
        Check.True(MusketeerNativeArchive.TryEncode(NativeFile, archive2, out string document2, out _), "encode 2");
        byte[] native2 = Gzip(GlobalJson(Entry(MusketeerNativeArchive.Key, document2)));
        var archive3 = BuildCareerArchive(out _, out _, out _, out _);
        Check.True(MusketeerNativeArchive.TryEncode(NativeFile, archive3, out string document3, out _), "encode 3");
        byte[] native3 = Gzip(GlobalJson(Entry(MusketeerNativeArchive.Key, document3)));

        // (1) A V1 container whose archive digest does not match its embedded document is not a
        // complete valid primary: Save refuses and keeps both files, the old good bak still works.
        string dir1 = NewDir();
        string path1 = Path.Combine(dir1, "pair.json");
        Check.True(MusketeerPairedBackup.Save(path1, NativeFile, native1, out _), "save good 1");
        Check.True(MusketeerPairedBackup.Save(path1, NativeFile, native2, out _), "save good 2");
        byte[] goodBak = File.ReadAllBytes(path1 + ".bak");
        string tamperedPrimary = FlipField(File.ReadAllText(path1, NoBom), "\"archiveSha256\":\"");
        File.WriteAllText(path1, tamperedPrimary, NoBom);
        Check.False(MusketeerPairedBackup.Save(path1, NativeFile, native3, out string reason), "incomplete V1 primary refuses save");
        Check.Eq("archive-digest", reason, "incomplete V1 primary save reason");
        Check.True(File.ReadAllBytes(path1).AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(tamperedPrimary)), "incomplete primary untouched");
        Check.True(File.ReadAllBytes(path1 + ".bak").AsSpan().SequenceEqual(goodBak), "old good bak preserved");
        Check.True(MusketeerPairedBackup.TryRecover(path1, NativeFile, native1, out string raw1, out _, out string recoverReason), "old good bak still recovers: " + recoverReason);
        Check.Eq(document1, raw1, "old good bak recovery raw exact");

        // (1b) A V1 container whose stored native carries no usable document is not complete
        // either: Save refuses, the good bak survives and still recovers its own pairing.
        string dir1B = NewDir();
        string path1B = Path.Combine(dir1B, "pair.json");
        Check.True(MusketeerPairedBackup.Save(path1B, NativeFile, native1, out _), "save 1");
        Check.True(MusketeerPairedBackup.Save(path1B, NativeFile, native2, out _), "save 2");
        byte[] bak1B = File.ReadAllBytes(path1B + ".bak");
        byte[] noKeyNative = Gzip(GlobalJson(Entry("other", "x")));
        WriteRawPair(path1B, NativeFile, noKeyNative, new string('0', 64));
        byte[] noKeyPrimary = File.ReadAllBytes(path1B);
        Check.False(MusketeerPairedBackup.Save(path1B, NativeFile, native3, out reason), "no-document primary refuses save");
        Check.Eq("native-key-missing", reason, "no-document primary save reason");
        Check.True(File.ReadAllBytes(path1B).AsSpan().SequenceEqual(noKeyPrimary), "no-document primary untouched");
        Check.True(File.ReadAllBytes(path1B + ".bak").AsSpan().SequenceEqual(bak1B), "no-document refusal keeps bak");
        Check.True(MusketeerPairedBackup.TryRecover(path1B, NativeFile, native1, out string raw1B, out _, out recoverReason), "no-document primary recovers good bak: " + recoverReason);
        Check.Eq(document1, raw1B, "no-document bak recovery raw exact");
        Check.False(MusketeerPairedBackup.TryRecover(path1B, NativeFile, noKeyNative, out _, out _, out recoverReason), "no-document primary refuses recover");
        Check.Eq("mismatch", recoverReason, "no-document recover reason");
        byte[] badJsonNative = Gzip("{}");
        WriteRawPair(path1B, NativeFile, badJsonNative, new string('0', 64));
        Check.False(MusketeerPairedBackup.Save(path1B, NativeFile, native3, out reason), "malformed native primary refuses save");
        Check.Eq("prefs", reason, "malformed native primary reason");

        // (2) A primary whose stored native embeds a future document is unsupported: never
        // replaced, never downgraded to a matching bak.
        string futureDocument = "{\"version\":2,\"file\":\"" + NativeFile + "\",\"archive\":\"\"}";
        byte[] futureNative = Gzip(GlobalJson(Entry(MusketeerNativeArchive.Key, futureDocument)));
        string dir2 = NewDir();
        string path2 = Path.Combine(dir2, "pair.json");
        Check.True(MusketeerPairedBackup.Save(path2, NativeFile, native1, out _), "seed pair");
        WriteRawPair(path2, NativeFile, futureNative, new string('0', 64));
        byte[] futurePrimaryBytes = File.ReadAllBytes(path2);
        Check.False(MusketeerPairedBackup.Save(path2, NativeFile, native2, out reason), "future embedded primary refuses save");
        Check.Eq("version", reason, "future embedded primary save reason");
        Check.True(File.ReadAllBytes(path2).AsSpan().SequenceEqual(futurePrimaryBytes), "future embedded primary untouched on save");
        Check.False(MusketeerPairedBackup.TryRecover(path2, NativeFile, futureNative, out _, out _, out recoverReason), "future embedded primary refuses recover");
        Check.Eq("version", recoverReason, "future embedded primary recover reason");
        Check.False(File.Exists(path2 + ".bak"), "future embedded refusal created no bak");

        string dir3 = NewDir();
        string path3 = Path.Combine(dir3, "pair.json");
        Check.True(MusketeerPairedBackup.Save(path3, NativeFile, native1, out _), "save 1");
        Check.True(MusketeerPairedBackup.Save(path3, NativeFile, native2, out _), "save 2");
        byte[] bakBytes3 = File.ReadAllBytes(path3 + ".bak");
        WriteRawPair(path3, NativeFile, futureNative, new string('0', 64));
        Check.False(MusketeerPairedBackup.TryRecover(path3, NativeFile, native1, out _, out _, out recoverReason), "future embedded primary refuses exact bak downgrade");
        Check.Eq("version", recoverReason, "future embedded downgrade reason");
        Check.False(MusketeerPairedBackup.Save(path3, NativeFile, native3, out reason), "future embedded primary refuses save with bak");
        Check.Eq("version", reason, "future embedded save with bak reason");
        Check.True(File.ReadAllBytes(path3 + ".bak").AsSpan().SequenceEqual(bakBytes3), "future embedded refusal keeps bak");

        // (3) A future container/document version is unsupported even when the structure gains
        // or loses fields; duplicated or malformed version stays corrupt.
        string dir4 = NewDir();
        string path4 = Path.Combine(dir4, "pair.json");
        Check.True(MusketeerPairedBackup.Save(path4, NativeFile, native2, out _), "save 1");
        Check.True(MusketeerPairedBackup.Save(path4, NativeFile, native2, out _), "save 2");
        byte[] bakBytes4 = File.ReadAllBytes(path4 + ".bak"); // this bak exactly matches native2
        string v1Pair = File.ReadAllText(path4, NoBom);
        File.WriteAllText(path4, v1Pair.Replace("\"version\":1,", "\"version\":2,\"extra\":1,", StringComparison.Ordinal), NoBom);
        Check.False(MusketeerPairedBackup.TryRecover(path4, NativeFile, native2, out _, out _, out recoverReason), "future shaped pair refused even with matching bak");
        Check.Eq("pair-version", recoverReason, "future shaped pair reason");
        Check.False(MusketeerPairedBackup.Save(path4, NativeFile, native3, out reason), "future shaped pair refuses save");
        Check.Eq("pair-version", reason, "future shaped pair save reason");
        Check.True(File.ReadAllBytes(path4 + ".bak").AsSpan().SequenceEqual(bakBytes4), "future shaped refusal keeps bak");
        File.Delete(path4 + ".bak");
        File.WriteAllText(path4, v1Pair.Replace("\"version\":1,", "\"version\":1,\"version\":1,", StringComparison.Ordinal), NoBom);
        Check.False(MusketeerPairedBackup.TryRecover(path4, NativeFile, native2, out _, out _, out recoverReason), "duplicated pair version refused");
        Check.Eq("pair-document", recoverReason, "duplicated pair version reason");
        File.WriteAllText(path4, v1Pair.Replace("\"version\":1,", "\"version\":\"1\",", StringComparison.Ordinal), NoBom);
        Check.False(MusketeerPairedBackup.TryRecover(path4, NativeFile, native2, out _, out _, out recoverReason), "malformed pair version refused");
        Check.Eq("pair-document", recoverReason, "malformed pair version reason");
        File.WriteAllText(path4, v1Pair.Replace("\"version\":1,", "\"extra\":1,\"version\":2,", StringComparison.Ordinal), NoBom);
        Check.False(MusketeerPairedBackup.TryRecover(path4, NativeFile, native2, out _, out _, out recoverReason), "future pair version after new field refused");
        Check.Eq("pair-version", recoverReason, "future pair version after new field reason");
        File.WriteAllText(path4, "{\"version\":2}", NoBom);
        Check.False(MusketeerPairedBackup.TryRecover(path4, NativeFile, native2, out _, out _, out recoverReason), "minimal future pair refused");
        Check.Eq("pair-version", recoverReason, "minimal future pair reason");

        string futureShapeDocument = "{\"version\":2,\"file\":\"" + NativeFile + "\",\"archive\":\"\",\"extra\":1}";
        Check.False(MusketeerNativeArchive.TryDecode(NativeFile, futureShapeDocument, out _, out bool futureUnsupported, out string futureReason), "future shaped document refused");
        Check.True(futureUnsupported, "future shaped document unsupported");
        Check.Eq("version", futureReason, "future shaped document reason");
        Check.False(MusketeerNativeArchive.TryDecode(NativeFile, "{\"version\":2}", out _, out futureUnsupported, out futureReason), "future minimal document refused");
        Check.True(futureUnsupported, "future minimal document unsupported");
        Check.Eq("version", futureReason, "future minimal document reason");
        Check.False(MusketeerNativeArchive.TryDecode(NativeFile, "{\"extra\":1,\"version\":2}", out _, out futureUnsupported, out futureReason), "future document version after new field refused");
        Check.True(futureUnsupported, "future document version after new field unsupported");
        Check.Eq("version", futureReason, "future document version after new field reason");
        MustRejectNative(futureNative, "version", "future shaped document propagated through extract", true);
        string payload1 = ArchiveBase64(document1);
        string duplicateVersionDocument = "{\"version\":1,\"version\":1,\"file\":\"" + NativeFile + "\",\"archive\":\"" + payload1 + "\"}";
        Check.False(MusketeerNativeArchive.TryDecode(NativeFile, duplicateVersionDocument, out _, out futureUnsupported, out futureReason), "duplicated document version refused");
        Check.False(futureUnsupported, "duplicated document version not unsupported");
        Check.Eq("document", futureReason, "duplicated document version reason");
        string malformedVersionDocument = "{\"version\":\"2\",\"file\":\"" + NativeFile + "\",\"archive\":\"" + payload1 + "\"}";
        Check.False(MusketeerNativeArchive.TryDecode(NativeFile, malformedVersionDocument, out _, out futureUnsupported, out futureReason), "malformed document version refused");
        Check.False(futureUnsupported, "malformed document version not unsupported");
        Check.Eq("document", futureReason, "malformed document version reason");

        // (4) gzip integrity: truncation, appended garbage and digest tampering are refused.
        // The trailer check is also exercised directly, because the runtime gzip reader itself
        // may (or may not, depending on runtime) surface its own failure first.
        byte[] good = Gzip(GlobalJson(Entry(MusketeerNativeArchive.Key, document1)));
        uint goodCrc = ReadUInt32Le(good, good.Length - 8);
        uint goodSize = ReadUInt32Le(good, good.Length - 4);
        Check.True(MusketeerNativeArchive.ValidateTrailer(good, goodCrc, goodSize, out _), "gzip trailer matches");
        Check.False(MusketeerNativeArchive.ValidateTrailer(FlipByte(good, good.Length - 5), goodCrc, goodSize, out string trailerReason), "trailer tampered CRC refused");
        Check.Eq("gzip-crc", trailerReason, "trailer tampered CRC reason");
        Check.False(MusketeerNativeArchive.ValidateTrailer(FlipByte(good, good.Length - 1), goodCrc, goodSize, out trailerReason), "trailer tampered ISIZE refused");
        Check.Eq("gzip-size", trailerReason, "trailer tampered ISIZE reason");
        Check.False(MusketeerNativeArchive.ValidateTrailer(Slice(good, 0, good.Length - 8), goodCrc, goodSize, out _), "trailer missing refused");
        Check.False(MusketeerNativeArchive.ValidateTrailer(Slice(good, 0, good.Length - 1), goodCrc, goodSize, out _), "trailer one byte short refused");
        Check.False(MusketeerNativeArchive.ValidateTrailer(Slice(good, 0, 10), goodCrc, goodSize, out trailerReason), "trailer too short refused");
        Check.Eq("gzip", trailerReason, "trailer too short reason");
        for (int cut = 1; cut <= 8; cut++)
            MustRejectGzip(Slice(good, 0, good.Length - cut), "gzip footer truncated cut=" + cut);
        MustRejectGzip(Concat(good, Encoding.UTF8.GetBytes("garbage!")), "gzip appended garbage");
        MustRejectGzip(Concat(good, new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00 }), "gzip fake second member header");
        MustRejectGzip(Concat(good, good), "gzip concatenated member");
        MustRejectGzip(Slice(good, 0, 10), "gzip header only");
        Check.False(MusketeerNativeArchive.TryExtract(FlipByte(good, 0), NativeFile, out _, out _, out _, out string footerReason), "gzip bad header magic refused");
        Check.Eq("gzip-header", footerReason, "gzip bad header magic reason");
        Check.False(MusketeerNativeArchive.TryExtract(FlipByte(good, good.Length - 5), NativeFile, out _, out _, out _, out footerReason), "gzip tampered CRC refused");
        Check.True(footerReason != null && footerReason.StartsWith("gzip", StringComparison.Ordinal), "gzip tampered CRC gzip-classified, got <" + footerReason + ">");
        Check.False(MusketeerNativeArchive.TryExtract(FlipByte(good, good.Length - 1), NativeFile, out _, out _, out _, out footerReason), "gzip tampered ISIZE refused");
        Check.True(footerReason != null && footerReason.StartsWith("gzip", StringComparison.Ordinal), "gzip tampered ISIZE gzip-classified, got <" + footerReason + ">");
        Check.True(MusketeerNativeArchive.TryExtract(good, NativeFile, out _, out _, out _, out footerReason), "intact gzip still accepted: " + footerReason);

        // (5) A directory primary is an access failure, never "missing", and never falls back.
        string dir5 = NewDir();
        string occupied = Path.Combine(dir5, "occupied");
        Directory.CreateDirectory(occupied);
        Check.False(MusketeerPairedBackup.Save(occupied, NativeFile, native1, out reason), "directory primary refuses save");
        Check.Eq("pair-directory", reason, "directory primary save reason");
        Check.True(Directory.Exists(occupied), "directory primary kept");
        Check.Eq(1, Directory.GetFileSystemEntries(dir5).Length, "directory primary left no temp");
        string donor = Path.Combine(dir5, "donor.json");
        Check.True(MusketeerPairedBackup.Save(donor, NativeFile, native1, out _), "donor pair");
        File.Copy(donor, occupied + ".bak");
        Check.False(MusketeerPairedBackup.TryRecover(occupied, NativeFile, native1, out _, out _, out recoverReason), "directory primary refuses recover");
        Check.Eq("pair-directory", recoverReason, "directory primary recover reason");
        Check.True(File.Exists(occupied + ".bak"), "directory primary never consumes bak");

        // (5b) An inaccessible primary (exclusive open) is an observable failure: no save and
        // no fallback to a valid bak.
        string dir6 = NewDir();
        string path6 = Path.Combine(dir6, "pair.json");
        Check.True(MusketeerPairedBackup.Save(path6, NativeFile, native1, out _), "save 1");
        Check.True(MusketeerPairedBackup.Save(path6, NativeFile, native2, out _), "save 2");
        byte[] bak6 = File.ReadAllBytes(path6 + ".bak");
        byte[] primary6 = File.ReadAllBytes(path6);
        using (new FileStream(path6, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Check.False(MusketeerPairedBackup.Save(path6, NativeFile, native3, out reason), "locked primary refuses save");
            Check.True(reason != null && reason.StartsWith("io:", StringComparison.Ordinal), "locked primary save reason=" + reason);
            Check.False(MusketeerPairedBackup.TryRecover(path6, NativeFile, native1, out _, out _, out recoverReason), "locked primary refuses recover");
            Check.True(recoverReason != null && recoverReason.StartsWith("io:", StringComparison.Ordinal), "locked primary recover reason=" + recoverReason);
        }
        Check.True(File.ReadAllBytes(path6).AsSpan().SequenceEqual(primary6), "locked primary untouched");
        Check.True(File.ReadAllBytes(path6 + ".bak").AsSpan().SequenceEqual(bak6), "locked refusal keeps bak");
    }

    // ---- r2 review regression ------------------------------------------------------------------

    private static void ReviewR2Regressions()
    {
        Console.WriteLine("[case] r2 review regression (unsearchable parent directory)");
        if (OperatingSystem.IsWindows())
        {
            Console.WriteLine("SKIP path-permission case: not a unix platform");
            return;
        }
        string dir = NewDir();
        string locked = Path.Combine(dir, "locked");
        Directory.CreateDirectory(locked);
        string path = Path.Combine(locked, "pair.json");
        var archive1 = BuildCareerArchive(out _, out _, out _, out _);
        Check.True(MusketeerNativeArchive.TryEncode(NativeFile, archive1, out string document1, out _), "encode 1");
        byte[] native1 = Gzip(GlobalJson(Entry(MusketeerNativeArchive.Key, document1)));
        var archive2 = BuildCareerArchive(out _, out _, out _, out _);
        Check.True(MusketeerNativeArchive.TryEncode(NativeFile, archive2, out string document2, out _), "encode 2");
        byte[] native2 = Gzip(GlobalJson(Entry(MusketeerNativeArchive.Key, document2)));
        Check.True(MusketeerPairedBackup.Save(path, NativeFile, native1, out _), "save 1");
        Check.True(MusketeerPairedBackup.Save(path, NativeFile, native2, out _), "save 2");
        byte[] primaryBytes = File.ReadAllBytes(path);
        byte[] bakBytes = File.ReadAllBytes(path + ".bak");

        const uint RestoreMode = 0x1ED; // 0755
        Check.True(Chmod(locked, 0), "chmod locked directory to 000");
        try
        {
            bool blocked;
            try
            {
                using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                blocked = false;
            }
            catch (UnauthorizedAccessException) { blocked = true; }
            catch (IOException) { blocked = true; }
            if (!blocked)
            {
                // A privileged environment (e.g. root) does not enforce the permission we set;
                // reporting SKIP is honest - this case must not pretend to be a valid proof.
                Console.WriteLine("SKIP path-permission assertions: environment does not enforce directory permissions");
            }
            else
            {
                Check.False(MusketeerPairedBackup.Save(path, NativeFile, native1, out string saveReason), "unsearchable primary refuses save");
                Check.True(saveReason != null && saveReason.StartsWith("io:", StringComparison.Ordinal), "unsearchable primary save reason=" + saveReason);
                Check.False(MusketeerPairedBackup.TryRecover(path, NativeFile, native1, out _, out _, out string recoverReason), "unsearchable primary refuses recover");
                Check.True(recoverReason != null && recoverReason.StartsWith("io:", StringComparison.Ordinal), "unsearchable primary recover reason=" + recoverReason);
                Check.True(recoverReason != "pair-missing", "permission failure is never reported as missing");
            }
        }
        finally
        {
            Check.True(Chmod(locked, RestoreMode), "restore directory permissions");
        }
        Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(primaryBytes), "permission case keeps primary");
        Check.True(File.ReadAllBytes(path + ".bak").AsSpan().SequenceEqual(bakBytes), "permission case keeps bak");
        Check.True(MusketeerPairedBackup.TryRecover(path, NativeFile, native2, out _, out _, out string restoredReason), "permissions restored, recovery works again: " + restoredReason);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static bool Chmod(string path, uint mode) => chmod(path, mode) == 0;

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int chmod(string path, uint mode);

    private static void MustReject(string raw, string expectedReason, string label, bool expectedUnsupported = false)
    {
        Check.False(MusketeerNativeArchive.TryDecode(NativeFile, raw, out _, out bool unsupported, out string reason), label);
        Check.Eq(expectedUnsupported, unsupported, label + " unsupported flag");
        Check.Eq(expectedReason, reason, label + " reason");
    }

    private static void MustRejectNative(byte[] native, string expectedReason, string label, bool expectedUnsupported = false)
    {
        Check.False(MusketeerNativeArchive.TryExtract(native, NativeFile, out _, out _, out bool unsupported, out string reason), label);
        Check.Eq(expectedUnsupported, unsupported, label + " unsupported flag");
        Check.Eq(expectedReason, reason, label + " reason");
    }

    private static MusketeerArchive BuildCareerArchive(out string scope, out string unitHash, out string gunHash, out Guid careerId)
    {
        var archive = new MusketeerArchive();
        scope = MusketeerArchive.NewScope();
        unitHash = MusketeerArchive.Hash("island-first-snapshot", scope);
        gunHash = MusketeerArchive.Hash("island-second-snapshot", scope);
        careerId = Guid.NewGuid();
        Check.True(archive.Record(scope, unitHash, new[]
        {
            new MusketeerCareer { Id = careerId, Kind = MusketeerCareer.KindUnit, NativeId = "npc-row-7" },
            new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun },
        }), "record unit + reservation");
        Check.True(archive.Record(scope, gunHash, new[]
        {
            new MusketeerCareer { Id = careerId, Kind = MusketeerCareer.KindGun, NativeId = "npc-row-9", StockSlot = 2 },
        }), "record dropped gun transfer");
        Check.True(archive.EnsureContext(MusketeerArchive.ContextKey(NativeFile, 1, 0, 1), scope, true), "register context");
        return archive;
    }

    private static string Doc(string version, string fileJson, string archiveJson, string extra = "")
        => "{\"version\":" + version + ",\"file\":" + fileJson + "," + extra + "\"archive\":" + archiveJson + "}";

    private static string Esc(string value) => JsonSerializer.Serialize(value);

    private static string GlobalJson(string entriesJson)
        => "{\"serializedSaveDataVersion\":16,\"_currentCampaign\":0,\"_currentChallenge\":0,"
           + "\"campaigns\":[{\"currentLand\":1,\"_islands\":[{\"land\":1,\"biome\":3,\"objects\":[]}]}],"
           + "\"challenges\":[],\"prefs\":{\"contents\":{},\"srzEntries\":[" + entriesJson + "]}}";

    private static string Entry(string key, string val) => "{\"key\":" + Esc(key) + ",\"val\":" + Esc(val) + "}";

    private static byte[] Gzip(string text) => GzipRaw(Encoding.UTF8.GetBytes(text));

    private static byte[] GzipRaw(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
            gzip.Write(data, 0, data.Length);
        return output.ToArray();
    }

    private static string ArchiveBase64(string documentRaw)
    {
        using var document = JsonDocument.Parse(documentRaw);
        return document.RootElement.GetProperty("archive").GetString();
    }

    private static string ShaHex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void MustRejectGzip(byte[] input, string label)
    {
        Check.False(MusketeerNativeArchive.TryExtract(input, NativeFile, out _, out _, out _, out string reason), label);
        Console.WriteLine("OBSERVED " + label + " reason=" + reason);
        Check.True(reason != null && reason.StartsWith("gzip", StringComparison.Ordinal), label + " gzip-classified reason, got <" + reason + ">");
    }

    private static byte[] Slice(byte[] source, int start, int length)
    {
        var result = new byte[length];
        Array.Copy(source, start, result, 0, length);
        return result;
    }

    private static uint ReadUInt32Le(byte[] bytes, int offset)
        => (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);

    private static byte[] Concat(byte[] first, byte[] second)
    {
        var result = new byte[first.Length + second.Length];
        first.CopyTo(result, 0);
        second.CopyTo(result, first.Length);
        return result;
    }

    private static byte[] FlipByte(byte[] source, int index)
    {
        var result = (byte[])source.Clone();
        result[index] ^= 0x01;
        return result;
    }

    private static void WriteRawPair(string path, string file, byte[] native, string archiveSha, int version = 1, string extra = "")
    {
        string json = "{\"version\":" + version + ",\"file\":" + Esc(file) + "," + extra
            + "\"nativeSha256\":" + Esc(ShaHex(native)) + ",\"archiveSha256\":" + Esc(archiveSha)
            + ",\"native\":" + Esc(Convert.ToBase64String(native)) + "}";
        File.WriteAllText(path, json, NoBom);
    }

    private static string FlipField(string container, string fieldPrefix)
    {
        int index = container.IndexOf(fieldPrefix, StringComparison.Ordinal) + fieldPrefix.Length;
        char current = container[index];
        return container.Remove(index, 1).Insert(index, current == '0' ? "1" : "0");
    }

    private static string InjectAfterBrace(string container, string fragment)
        => container.Substring(0, 1) + fragment + container.Substring(1);

    private static string NewDir()
    {
        string dir = Path.Combine(Root, "case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
