using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using KingdomEnhancedMod;
using static KingdomEnhancedMod.MusketeerLegacyCompensationPolicy;

// Pure-kernel regression suite for the Issue #223 one-time legacy compensation leaf. It compiles
// the production value/codec file directly: no Unity, no game startup, no native calls, no disk
// IO (in-memory streams only). Negative coverage is the point: every accept path has a reject
// sibling with an independent expected outcome, and the fixtures are synthetic.
internal static class Program
{
    private const string Rid = "11112222333344445555666677778888";
    private const string RidLetters = "aabbccddeeff00112233445566778899";
    private const string GrantLetters = "0011aabb2233ccdd4455eeff66778899";
    private const string HexA = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string HexB = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";
    private const string HexC = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";
    private const string ContextName = "c0ffee00c0ffee00c0ffee00c0ffee00c0ffee00c0ffee00c0ffee00c0ffee00";
    private const string ArcherPath = "Prefabs/Characters/Archer";
    private const string NativeRowA = "native-row-a";
    private const string NativeRowB = "native-row-b";

    private static int _checks;
    private static int _failed;

    private static void Check(bool condition, string reason)
    {
        _checks++;
        if (condition) return;
        _failed++;
        Console.WriteLine("FAIL: " + reason);
    }

    private static Guid G(string hex32) => Guid.ParseExact(hex32, "N");

    private static Guid Grant(int index) => G("00112233445566778899aabbccddee" + index.ToString("x2"));

    private static Guid[] Grants8()
    {
        var grants = new Guid[8];
        for (int i = 0; i < grants.Length; i++) grants[i] = Grant(i + 1);
        return grants;
    }

    private static MusketeerLegacyGrantCapture Capture(Guid grantId, string nativeId)
        => new MusketeerLegacyGrantCapture(grantId, nativeId, 1, ArcherPath, 1);

    private static MusketeerLegacyGrantCapture[] Captures8(Guid[] grants)
    {
        var captures = new MusketeerLegacyGrantCapture[grants.Length];
        for (int i = 0; i < grants.Length; i++) captures[i] = Capture(grants[i], "row-" + (i + 1).ToString("D2"));
        return captures;
    }

    private static MusketeerLegacyGrantCapture[] Reversed(MusketeerLegacyGrantCapture[] captures)
    {
        var reversed = new MusketeerLegacyGrantCapture[captures.Length];
        for (int i = 0; i < captures.Length; i++) reversed[i] = captures[captures.Length - 1 - i];
        return reversed;
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Concat(byte[] first, byte[] second)
    {
        var result = new byte[first.Length + second.Length];
        Array.Copy(first, result, first.Length);
        Array.Copy(second, 0, result, first.Length, second.Length);
        return result;
    }

    private static string PlanJson(string requestId, string context, string epoch, string sha, string reservations, string units, string version = "1")
        => "{\"version\":" + version + ",\"requestId\":\"" + requestId + "\",\"context\":\"" + context
            + "\",\"sourceEpoch\":\"" + epoch + "\",\"sourceArchiveSha\":\"" + sha
            + "\",\"sourceReservations\":" + reservations + ",\"units\":" + units + "}";

    private static string Plan2Json() => PlanJson(Rid, ContextName, HexA, HexB, "4", "2");

    private static string ReceiptJson(string version, string planJson, string migrationEpoch, string grantIds, string captured, string complete, string suffix = "")
        => "{\"version\":" + version + ",\"plan\":" + planJson + ",\"migrationEpoch\":\"" + migrationEpoch
            + "\",\"grantIds\":" + grantIds + ",\"capturedNativeIds\":" + captured + ",\"complete\":" + complete + suffix + "}";

    private static string Receipt2Json(string complete = "true", string captured = "[\"native-row-a\",\"native-row-b\"]")
        => ReceiptJson("1", Plan2Json(), HexC,
            GrantJson("10000000000000000000000000000001", "10000000000000000000000000000002"), captured, complete);

    private static string GrantJson(params string[] hex)
    {
        var builder = new StringBuilder("[");
        for (int i = 0; i < hex.Length; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append('"').Append(hex[i]).Append('"');
        }
        return builder.Append(']').ToString();
    }

    private static string CaptureJson(params string[] ids)
    {
        var builder = new StringBuilder("[");
        for (int i = 0; i < ids.Length; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append('"').Append(ids[i]).Append('"');
        }
        return builder.Append(']').ToString();
    }

    private static MusketeerLegacyCompensationPlan Plan17()
        => new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName, HexA, HexB, 17, 8);

    private static byte[] Utf8WithByte(string json, int index, byte value)
    {
        var bytes = Utf8(json);
        bytes[index] = value;
        return bytes;
    }

    // Probes for the decode-bomb case: Parsing may succeed while decoding a string value or a
    // property name throws. These helpers print the escaped exception (proof) and fail the check.
    private static bool PlanRejects(byte[] bytes)
    {
        try
        {
            return !TryParsePlan(bytes, out _, out _);
        }
        catch (Exception exception)
        {
            Console.WriteLine("THROWS: TryParsePlan escaped " + exception.GetType().Name + ": " + exception.Message);
            return false;
        }
    }

    private static bool ReceiptRejects(byte[] utf8Json)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8Json);
            return !TryReadReceipt(document.RootElement, out _, out _);
        }
        catch (Exception exception)
        {
            Console.WriteLine("THROWS: receipt decode escaped " + exception.GetType().Name + ": " + exception.Message);
            return false;
        }
    }

    private static bool ParsePlan(string json) => TryParsePlan(Utf8(json), out _, out _);

    private static bool ParsePlan(string json, out MusketeerLegacyCompensationPlan plan, out string reason)
        => TryParsePlan(Utf8(json), out plan, out reason);

    private static string Encode(MusketeerLegacyCompensationReceipt receipt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            MusketeerLegacyCompensationPolicy.WriteReceipt(writer, receipt);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool Decode(string json, out MusketeerLegacyCompensationReceipt receipt, out string reason)
    {
        using var document = JsonDocument.Parse(json);
        return TryReadReceipt(document.RootElement, out receipt, out reason);
    }

    private static bool EncodeThrows(MusketeerLegacyCompensationReceipt receipt)
    {
        try
        {
            Encode(receipt);
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static bool Throws(Type exceptionType, Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception exception)
        {
            return exceptionType.IsInstanceOfType(exception);
        }
    }

    private static void PlanValidation()
    {
        var plan = Plan17();
        Check(ValidatePlan(plan, out _), "17 reservations -> 8 units validates");
        Check(plan.Version == 1 && plan.RequestId == G(Rid) && plan.Context == ContextName
            && plan.SourceEpoch == HexA && plan.SourceArchiveSha == HexB
            && plan.SourceReservations == 17 && plan.Units == 8, "plan exposes the frozen field set");
        Check(ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName, HexA, HexB, 2, 1), out _),
            "2 reservations -> 1 unit validates");
        Check(ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName, HexA, HexB, 64, 32), out _),
            "64 reservations -> 32 units validates");
        Check(ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName, HexA, HexB, 65, 32), out _),
            "65 reservations -> 32 units is the cap boundary");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName, HexA, HexB, 1, 0), out _),
            "1 reservation floors to 0 and is rejected");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName, HexA, HexB, 3, 2), out _),
            "3 reservations -> 2 units is not floor(3/2)");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName, HexA, HexB, 66, 33), out _),
            "66 reservations -> 33 units exceeds the unit cap");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName, HexA, HexB, 4096, 2048), out _),
            "4096 reservations cannot pass as 2048 units");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName, HexA, HexB, 4097, 32), out _),
            "4097 reservations exceeds the reservation cap");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(2, G(Rid), ContextName, HexA, HexB, 17, 8), out _),
            "plan version 2 is a future version");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(0, G(Rid), ContextName, HexA, HexB, 17, 8), out _),
            "plan version 0 is rejected");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(1, Guid.Empty, ContextName, HexA, HexB, 17, 8), out _),
            "empty requestId is rejected");
        Check(ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), HexC, HexA, HexB, 17, 8), out _),
            "another 64-hex context is accepted");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), "", HexA, HexB, 17, 8), out _),
            "empty context is rejected");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName.Substring(0, 63), HexA, HexB, 17, 8), out _),
            "63-char context is rejected");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName + "c", HexA, HexB, 17, 8), out _),
            "65-char context is rejected");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName.ToUpperInvariant(), HexA, HexB, 17, 8), out _),
            "uppercase context is rejected");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName.Substring(1) + "g", HexA, HexB, 17, 8), out _),
            "non-hex context is rejected");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName.Substring(0, 62) + "\u4e2d\u6587", HexA, HexB, 17, 8), out _),
            "unicode context is rejected");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), " " + ContextName.Substring(1), HexA, HexB, 17, 8), out _),
            "whitespace context is rejected");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName, HexA.ToUpperInvariant(), HexB, 17, 8), out _),
            "uppercase sourceEpoch is rejected");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName, HexA.Substring(1), HexB, 17, 8), out _),
            "63-char sourceEpoch is rejected");
        Check(!ValidatePlan(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName, HexA, HexB.Substring(1) + "g", 17, 8), out _),
            "non-hex sourceArchiveSha is rejected");
        Check(!ValidatePlan(null, out _), "null plan is rejected");

        Check(MatchesSource(plan, ContextName, HexA, HexB, 17), "exact source matches the plan");
        Check(!MatchesSource(plan, "other-context", HexA, HexB, 17), "context mismatch fails the source match");
        Check(!MatchesSource(plan, ContextName, HexC, HexB, 17), "epoch mismatch fails the source match");
        Check(!MatchesSource(plan, ContextName, HexA, HexA, 17), "sha mismatch fails the source match");
        Check(!MatchesSource(plan, ContextName, HexA, HexB, 16), "reservation mismatch fails the source match");
        Check(!MatchesSource(null, ContextName, HexA, HexB, 17), "null plan never matches the source");
        Check(!MatchesSource(new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName, HexA.ToUpperInvariant(), HexB, 17, 8),
            ContextName, HexA.ToUpperInvariant(), HexB, 17), "invalid plan never matches the source even when fields agree");
    }

    private static void PlanJsonStrictness()
    {
        string valid = PlanJson(Rid, ContextName, HexA, HexB, "17", "8");
        Check(ParsePlan(valid), "canonical config plan json parses");
        Check(ParsePlan(valid, out var parsed, out _)
            && parsed.Version == 1 && parsed.RequestId == G(Rid) && parsed.Context == ContextName
            && parsed.SourceEpoch == HexA && parsed.SourceArchiveSha == HexB
            && parsed.SourceReservations == 17 && parsed.Units == 8, "parsed plan fields are exact");
        Check(ParsePlan("{\"units\":8,\"context\":\"" + ContextName + "\",\"requestId\":\"" + Rid
            + "\",\"sourceArchiveSha\":\"" + HexB + "\",\"version\":1,\"sourceEpoch\":\"" + HexA
            + "\",\"sourceReservations\":17}"), "field order does not matter");
        Check(ParsePlan(PlanJson(Rid, HexC, HexA, HexB, "17", "8")), "64-hex context parses");
        Check(ParsePlan(PlanJson(Rid, ContextName, HexA, HexB, "65", "32")), "65 -> 32 parses");
        Check(ParsePlan(PlanJson(RidLetters, ContextName, HexA, HexB, "2", "1")), "2 -> 1 parses");

        Check(!ParsePlan(PlanJson(Rid, ContextName, HexA.ToUpperInvariant(), HexB, "17", "8")), "uppercase sourceEpoch json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName, HexA.Substring(0, 63) + "g", HexB, "17", "8")), "non-hex sourceEpoch json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName, HexA, HexB.ToUpperInvariant(), "17", "8")), "uppercase sha json rejected");
        Check(!ParsePlan(PlanJson(RidLetters.ToUpperInvariant(), ContextName, HexA, HexB, "17", "8")), "uppercase requestId json rejected");
        Check(!ParsePlan(PlanJson(Rid.Substring(1), ContextName, HexA, HexB, "17", "8")), "31-char requestId json rejected");
        Check(!ParsePlan(PlanJson("11112222-3333-4444-5555-666677778888", ContextName, HexA, HexB, "17", "8")), "dashed requestId json rejected");
        Check(!ParsePlan(PlanJson(Rid, "", HexA, HexB, "17", "8")), "empty context json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName.Substring(0, 63), HexA, HexB, "17", "8")), "63-char context json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName + "c", HexA, HexB, "17", "8")), "65-char context json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName.ToUpperInvariant(), HexA, HexB, "17", "8")), "uppercase context json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName.Substring(1) + "g", HexA, HexB, "17", "8")), "non-hex context json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName.Substring(0, 62) + "\\u4e2d\\u6587", HexA, HexB, "17", "8")), "unicode context json rejected");
        Check(!ParsePlan(PlanJson(Rid, " " + ContextName.Substring(1), HexA, HexB, "17", "8")), "whitespace context json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName, HexA, HexB, "1", "0")), "1 -> 0 json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName, HexA, HexB, "17", "7")), "units mismatch json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName, HexA, HexB, "66", "33")), "unit cap json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName, HexA, HexB, "4096", "2048")), "reservation cap json rejected");

        Check(!ParsePlan(PlanJson(Rid, ContextName, HexA, HexB, "17", "8", version: "2")), "future version json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName, HexA, HexB, "17", "8", version: "0")), "version 0 json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName, HexA, HexB, "17", "8", version: "\"1\"")), "string version json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName, HexA, HexB, "17", "8", version: "1.0")), "float version json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName, HexA, HexB, "\"17\"", "8")), "string reservations json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName, HexA, HexB, "17.5", "8")), "float reservations json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName, HexA, HexB, "true", "8")), "bool reservations json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName, HexA, HexB, "17", "8.5")), "float units json rejected");
        Check(!ParsePlan(PlanJson(Rid, ContextName, HexA, HexB, "17", "null")), "null units json rejected");

        Check(!ParsePlan("{\"version\":1,\"version\":1,\"requestId\":\"" + Rid + "\",\"context\":\"" + ContextName
            + "\",\"sourceEpoch\":\"" + HexA + "\",\"sourceArchiveSha\":\"" + HexB
            + "\",\"sourceReservations\":17,\"units\":8}"), "duplicate field json rejected");
        Check(!ParsePlan(valid.Substring(0, valid.Length - 1) + ",\"issued\":8}"), "unknown field json rejected");
        Check(!ParsePlan(valid.Substring(0, valid.Length - 1) + ",\"complete\":true}"), "receipt complete field is not a config field");
        Check(!ParsePlan(valid.Substring(0, valid.Length - 1) + ",\"migrationEpoch\":\"" + HexC + "\"}"), "receipt migrationEpoch is not a config field");
        Check(!ParsePlan(valid.Substring(0, valid.Length - 1) + ",\"capturedNativeIds\":[]}"), "receipt capture field is not a config field");
        Check(!ParsePlan("{\"plan\":" + valid + "}"), "nested plan wrapper is not a config plan");
        Check(!ParsePlan(Receipt2Json()), "a full receipt document is rejected by the config parser");

        Check(!ParsePlan(valid + " " + valid), "trailing document json rejected");
        Check(!ParsePlan("[" + valid + "]"), "array root json rejected");
        Check(!ParsePlan(valid.Substring(0, valid.Length - 1)), "unterminated json rejected");
        Check(!ParsePlan(valid.Substring(0, valid.Length - 1) + ",}"), "trailing comma json rejected");
        Check(!ParsePlan("{\"version\":1/*c*/}"), "comment json rejected");
        Check(!ParsePlan(""), "empty json rejected");
        Check(!TryParsePlan(null, out _, out _), "null bytes rejected");
        Check(!TryParsePlan(new byte[0], out _, out _), "zero-length bytes rejected");
        Check(!TryParsePlan(new byte[16385], out _, out _), "over-16KiB bytes rejected");
        Check(!TryParsePlan(Concat(new byte[] { 0xEF, 0xBB, 0xBF }, Utf8(valid)), out _, out _), "utf-8 BOM rejected");
        Check(!TryParsePlan(new byte[] { 0x7B, 0x22, 0x61, 0x22, 0x3A, 0xFF, 0x7D }, out _, out _), "invalid utf-8 rejected");
        Check(!TryParsePlan(new byte[] { 0xFF, 0xFE, 0x7B, 0x00, 0x7D, 0x00 }, out _, out _), "utf-16 bytes rejected");
    }

    private static void PendingBudget()
    {
        var plan = Plan17();
        var grants = Grants8();
        Check(TryCreatePending(plan, HexC, grants, out var pending, out _), "8 grants create a pending receipt");
        Check(!pending.Complete, "pending receipt is not complete");
        Check(pending.GrantIds.Count == 8 && pending.CapturedNativeIds.Count == 0, "pending keeps 8 grants and no captures");
        Check(ValidateReceipt(pending, out _), "pending receipt validates");
        Check(ReferenceEquals(pending.Plan, plan) && pending.MigrationEpoch == HexC, "pending keeps the plan and migration epoch");

        Check(!TryCreatePending(plan, HexC, null, out _, out _), "null grants rejected");
        Check(!TryCreatePending(plan, HexC, grants[..7], out _, out _), "7 grants rejected");
        Check(!TryCreatePending(plan, HexC, grants.Concat(new[] { Grant(9) }).ToArray(), out _, out _), "9 grants rejected");
        var duplicate = Grants8();
        duplicate[3] = duplicate[2];
        Check(!TryCreatePending(plan, HexC, duplicate, out _, out _), "duplicate grants rejected");
        var withEmpty = Grants8();
        withEmpty[0] = Guid.Empty;
        Check(!TryCreatePending(plan, HexC, withEmpty, out _, out _), "empty grant rejected");
        Check(!TryCreatePending(plan, HexA, grants, out _, out _), "migration epoch identical to source epoch rejected");
        Check(!TryCreatePending(plan, HexC.ToUpperInvariant(), grants, out _, out _), "uppercase migration epoch rejected");
        Check(!TryCreatePending(plan, HexC.Substring(1), grants, out _, out _), "short migration epoch rejected");
        Check(!TryCreatePending(null, HexC, grants, out _, out _), "null plan rejected for pending");

        var inputList = new List<Guid>(grants);
        Check(TryCreatePending(plan, HexC, inputList, out var fromList, out _), "pending from a list input");
        Guid firstFromList = fromList.GrantIds[0];
        inputList[0] = Grant(200);
        inputList.Add(Grant(201));
        Check(fromList.GrantIds.Count == 8 && fromList.GrantIds[0] == firstFromList,
            "list mutation after the factory does not leak into the receipt");

        var inputArray = Grants8();
        Check(TryCreatePending(plan, HexC, inputArray, out var fromArray, out _), "pending from an array input");
        Guid firstFromArray = fromArray.GrantIds[0];
        inputArray[0] = Grant(200);
        Check(fromArray.GrantIds[0] == firstFromArray, "array mutation after the factory does not leak into the receipt");
        Check(!(fromArray.GrantIds is Guid[]), "grant view is not a mutable array");
    }

    private static void CaptureCompletion()
    {
        var plan = Plan17();
        var grants = Grants8();
        var captures = Captures8(grants);
        Check(TryCreatePending(plan, HexC, grants, out var pending, out _), "pending for completion");
        Check(TryComplete(pending, Reversed(captures), out var completed, out _), "shuffled captures complete against the full grant set");
        Check(completed.Complete && completed.CapturedNativeIds.Count == 8, "completed receipt carries 8 captures");
        Check(completed.GrantIds.Count == 8 && completed.GrantIds[0] == grants[0] && completed.GrantIds[7] == grants[7],
            "completed receipt keeps the grant budget");
        Check(completed.CapturedNativeIds[0] == "row-01" && completed.CapturedNativeIds[7] == "row-08",
            "captures are ordered by grant, not by report order");
        Check(ValidateReceipt(completed, out _), "completed receipt validates");
        Check(!(completed.CapturedNativeIds is string[]), "capture view is not a mutable array");
        Check(!TryComplete(completed, captures, out _, out _), "a completed receipt refuses the budget again");
        Check(TryComplete(pending, captures, out var again, out _)
            && again.CapturedNativeIds[0] == completed.CapturedNativeIds[0]
            && again.CapturedNativeIds[7] == completed.CapturedNativeIds[7], "completion is a pure deterministic function");

        Check(!TryComplete(null, captures, out _, out _), "null pending rejected");
        Check(!TryComplete(pending, null, out _, out _), "null captures rejected");
        Check(!TryComplete(pending, captures[..7], out _, out _), "7 captures rejected");
        var captures9 = new MusketeerLegacyGrantCapture[9];
        Array.Copy(captures, captures9, 8);
        captures9[8] = Capture(Grant(9), "row-09");
        Check(!TryComplete(pending, captures9, out _, out _), "9 captures rejected");

        var duplicatedGrant = (MusketeerLegacyGrantCapture[])captures.Clone();
        duplicatedGrant[1] = Capture(grants[0], "row-11");
        Check(!TryComplete(pending, duplicatedGrant, out _, out _), "duplicate capture grant rejected");
        var unknownGrant = (MusketeerLegacyGrantCapture[])captures.Clone();
        unknownGrant[1] = Capture(Grant(9), "row-12");
        Check(!TryComplete(pending, unknownGrant, out _, out _), "capture outside the grant set rejected");
        var emptyGrant = (MusketeerLegacyGrantCapture[])captures.Clone();
        emptyGrant[1] = Capture(Guid.Empty, "row-13");
        Check(!TryComplete(pending, emptyGrant, out _, out _), "empty capture grant rejected");
        var duplicateNative = (MusketeerLegacyGrantCapture[])captures.Clone();
        duplicateNative[1] = Capture(grants[1], "row-01");
        Check(!TryComplete(pending, duplicateNative, out _, out _), "duplicate native id rejected");
        var missingNative = (MusketeerLegacyGrantCapture[])captures.Clone();
        missingNative[1] = Capture(grants[1], "");
        Check(!TryComplete(pending, missingNative, out _, out _), "empty native id rejected");
        var oversizedNative = (MusketeerLegacyGrantCapture[])captures.Clone();
        oversizedNative[1] = Capture(grants[1], new string('n', 257));
        Check(!TryComplete(pending, oversizedNative, out _, out _), "257-char native id rejected");
        var boundaryNative = (MusketeerLegacyGrantCapture[])captures.Clone();
        boundaryNative[1] = Capture(grants[1], new string('n', 256));
        Check(TryComplete(pending, boundaryNative, out _, out _), "256-char native id is the accepted boundary");
        var wrongKind = (MusketeerLegacyGrantCapture[])captures.Clone();
        wrongKind[1] = new MusketeerLegacyGrantCapture(grants[1], "row-14", 2, ArcherPath, 1);
        Check(!TryComplete(pending, wrongKind, out _, out _), "gun kind 2 rejected for the unit grant");
        var zeroKind = (MusketeerLegacyGrantCapture[])captures.Clone();
        zeroKind[1] = new MusketeerLegacyGrantCapture(grants[1], "row-15", 0, ArcherPath, 1);
        Check(!TryComplete(pending, zeroKind, out _, out _), "kind 0 rejected");
        var wrongPath = (MusketeerLegacyGrantCapture[])captures.Clone();
        wrongPath[1] = new MusketeerLegacyGrantCapture(grants[1], "row-16", 1, "Prefabs/Objects/ToolBow", 1);
        Check(!TryComplete(pending, wrongPath, out _, out _), "tool prefab path rejected");
        var wrongCasePath = (MusketeerLegacyGrantCapture[])captures.Clone();
        wrongCasePath[1] = new MusketeerLegacyGrantCapture(grants[1], "row-17", 1, "prefabs/characters/archer", 1);
        Check(!TryComplete(pending, wrongCasePath, out _, out _), "prefab path is exact ordinal");
        var zeroOccurrences = (MusketeerLegacyGrantCapture[])captures.Clone();
        zeroOccurrences[1] = new MusketeerLegacyGrantCapture(grants[1], "row-18", 1, ArcherPath, 0);
        Check(!TryComplete(pending, zeroOccurrences, out _, out _), "occurrence 0 rejected");
        var twiceOccurrences = (MusketeerLegacyGrantCapture[])captures.Clone();
        twiceOccurrences[1] = new MusketeerLegacyGrantCapture(grants[1], "row-19", 1, ArcherPath, 2);
        Check(!TryComplete(pending, twiceOccurrences, out _, out _), "occurrence 2 rejected");
    }

    private static void ReceiptValidation()
    {
        var plan = Plan17();
        var grants = Grants8();
        var natives8 = new string[8];
        for (int i = 0; i < natives8.Length; i++) natives8[i] = new string('n', 255) + i.ToString();

        Check(ValidateReceipt(new MusketeerLegacyCompensationReceipt(plan, HexC, grants, Array.Empty<string>(), false), out _),
            "pending receipt validates with empty captures");
        Check(!ValidateReceipt(new MusketeerLegacyCompensationReceipt(plan, HexC, grants, new[] { NativeRowA }, false), out _),
            "pending receipt with captured entries is rejected");
        Check(ValidateReceipt(new MusketeerLegacyCompensationReceipt(plan, HexC, grants, natives8, true), out _),
            "complete receipt with 8 distinct 255-char ids validates");
        Check(!ValidateReceipt(new MusketeerLegacyCompensationReceipt(plan, HexC, grants, Array.Empty<string>(), true), out _),
            "complete receipt without captures is rejected");
        Check(!ValidateReceipt(null, out _), "null receipt rejected");
        Check(!ValidateReceipt(new MusketeerLegacyCompensationReceipt(plan, HexC, grants[..7], Array.Empty<string>(), false), out _),
            "grant count below units rejected");
        var duplicated = Grants8();
        duplicated[5] = duplicated[4];
        Check(!ValidateReceipt(new MusketeerLegacyCompensationReceipt(plan, HexC, duplicated, Array.Empty<string>(), false), out _),
            "duplicate grants rejected");
        var withEmpty = Grants8();
        withEmpty[0] = Guid.Empty;
        Check(!ValidateReceipt(new MusketeerLegacyCompensationReceipt(plan, HexC, withEmpty, Array.Empty<string>(), false), out _),
            "empty grant rejected");
        Check(!ValidateReceipt(new MusketeerLegacyCompensationReceipt(plan, HexC, null, Array.Empty<string>(), false), out _),
            "null grant list rejected");
        Check(!ValidateReceipt(new MusketeerLegacyCompensationReceipt(plan, HexC, grants, null, false), out _),
            "null capture list rejected");
        Check(!ValidateReceipt(new MusketeerLegacyCompensationReceipt(plan, HexC, grants, null, true), out _),
            "null capture list rejected when complete");
        Check(!ValidateReceipt(new MusketeerLegacyCompensationReceipt(plan, HexC, grants, natives8[..7], true), out _),
            "7 captures rejected when complete");
        Check(!ValidateReceipt(new MusketeerLegacyCompensationReceipt(plan, HexC, grants,
            new[] { "", "b", "c", "d", "e", "f", "g", "h" }, true), out _), "empty native id rejected when complete");
        Check(!ValidateReceipt(new MusketeerLegacyCompensationReceipt(plan, HexC, grants,
            new[] { new string('n', 257), "b", "c", "d", "e", "f", "g", "h" }, true), out _), "257-char native id rejected when complete");
        Check(!ValidateReceipt(new MusketeerLegacyCompensationReceipt(plan, HexC, grants,
            new[] { "a", "a", "c", "d", "e", "f", "g", "h" }, true), out _), "duplicate native ids rejected when complete");
        Check(!ValidateReceipt(new MusketeerLegacyCompensationReceipt(plan, HexA, grants, Array.Empty<string>(), false), out _),
            "migration epoch equal to source epoch rejected");
        Check(!ValidateReceipt(new MusketeerLegacyCompensationReceipt(plan, HexC.ToUpperInvariant(), grants, Array.Empty<string>(), false), out _),
            "uppercase migration epoch rejected");
        Check(!ValidateReceipt(new MusketeerLegacyCompensationReceipt(null, HexC, grants, Array.Empty<string>(), false), out _),
            "null plan rejected inside the receipt");
    }

    private static void ReceiptCodec()
    {
        var plan = new MusketeerLegacyCompensationPlan(1, G(Rid), ContextName, HexA, HexB, 4, 2);
        var g1 = G("10000000000000000000000000000001");
        var g2 = G("10000000000000000000000000000002");
        var receipt = new MusketeerLegacyCompensationReceipt(plan, HexC, new[] { g1, g2 }, new[] { NativeRowA, NativeRowB }, true);

        string expected = "{\"version\":1,\"plan\":{\"version\":1,\"requestId\":\"" + Rid + "\",\"context\":\"" + ContextName
            + "\",\"sourceEpoch\":\"" + HexA + "\",\"sourceArchiveSha\":\"" + HexB
            + "\",\"sourceReservations\":4,\"units\":2},\"migrationEpoch\":\"" + HexC
            + "\",\"grantIds\":[\"10000000000000000000000000000001\",\"10000000000000000000000000000002\"]"
            + ",\"capturedNativeIds\":[\"native-row-a\",\"native-row-b\"],\"complete\":true}";
        string encoded = Encode(receipt);
        Check(encoded == expected, "receipt wire schema is exact and stable");
        Check(Encode(receipt) == encoded, "receipt writer is deterministic");
        Check(Decode(encoded, out var roundtrip, out _), "written receipt reads back");
        Check(roundtrip.Plan.Version == 1 && roundtrip.Plan.RequestId == plan.RequestId && roundtrip.Plan.Context == ContextName
            && roundtrip.Plan.SourceEpoch == HexA && roundtrip.Plan.SourceArchiveSha == HexB
            && roundtrip.Plan.SourceReservations == 4 && roundtrip.Plan.Units == 2, "roundtrip plan fields are exact");
        Check(roundtrip.MigrationEpoch == HexC && roundtrip.GrantIds.Count == 2 && roundtrip.GrantIds[1] == g2
            && roundtrip.CapturedNativeIds[1] == NativeRowB && roundtrip.Complete, "roundtrip receipt fields are exact");

        Check(TryCreatePending(Plan17(), HexC, Grants8(), out var pending, out _), "pending for the writer guard");
        Check(EncodeThrows(pending), "pending receipt is refused by the writer guard");
        Check(EncodeThrows(new MusketeerLegacyCompensationReceipt(Plan17(), HexC, Grants8()[..7], Array.Empty<string>(), false)),
            "invalid receipt is refused by the writer");
        Check(Throws(typeof(ArgumentNullException), () => MusketeerLegacyCompensationPolicy.WriteReceipt(null, receipt)),
            "null writer rejected");
        Check(Throws(typeof(ArgumentNullException), () => MusketeerLegacyCompensationPolicy.WriteReceipt(new Utf8JsonWriter(new MemoryStream()), null)),
            "null receipt rejected by the writer");
    }

    private static void ReceiptJsonStrictness()
    {
        string valid = Receipt2Json();
        Check(Decode(valid, out var receipt, out _), "canonical receipt json reads");
        Check(receipt.Complete && receipt.GrantIds.Count == 2 && receipt.CapturedNativeIds.Count == 2, "canonical receipt shape");

        string reordered = "{\"complete\":true,\"capturedNativeIds\":[\"native-row-a\",\"native-row-b\"]"
            + ",\"grantIds\":[\"10000000000000000000000000000001\",\"10000000000000000000000000000002\"]"
            + ",\"migrationEpoch\":\"" + HexC + "\",\"plan\":" + Plan2Json() + ",\"version\":1}";
        Check(Decode(reordered, out _, out _), "receipt field order does not matter");

        Check(!Decode(Receipt2Json(complete: "false", captured: "[]"), out _, out _), "persisted pending receipt rejected");
        Check(!Decode(Receipt2Json(complete: "false"), out _, out _), "persisted pending receipt with captures rejected");
        Check(!Decode(ReceiptJson("1", Plan2Json(), HexC,
            GrantJson("10000000000000000000000000000001", "10000000000000000000000000000002"),
            CaptureJson(NativeRowA, NativeRowB), "true", ",\"issued\":8"), out _, out _), "unknown receipt field rejected");
        Check(!Decode(ReceiptJson("1,\"version\":1", Plan2Json(), HexC,
            GrantJson("10000000000000000000000000000001", "10000000000000000000000000000002"),
            CaptureJson(NativeRowA, NativeRowB), "true"), out _, out _), "duplicate receipt version rejected");
        Check(!Decode("{\"version\":1,\"plan\":" + Plan2Json() + ",\"migrationEpoch\":\"" + HexC
            + "\",\"grantIds\":" + GrantJson("10000000000000000000000000000001", "10000000000000000000000000000002")
            + ",\"capturedNativeIds\":" + CaptureJson(NativeRowA, NativeRowB) + "}", out _, out _), "missing complete field rejected");
        Check(!Decode(ReceiptJson("2", Plan2Json(), HexC,
            GrantJson("10000000000000000000000000000001", "10000000000000000000000000000002"),
            CaptureJson(NativeRowA, NativeRowB), "true"), out _, out _), "future receipt version rejected");
        Check(!Decode(ReceiptJson("0", Plan2Json(), HexC,
            GrantJson("10000000000000000000000000000001", "10000000000000000000000000000002"),
            CaptureJson(NativeRowA, NativeRowB), "true"), out _, out _), "receipt version 0 rejected");
        Check(!Decode(ReceiptJson("1", Plan2Json().Substring(0, Plan2Json().Length - 1) + ",\"complete\":true}",
            HexC, GrantJson("10000000000000000000000000000001", "10000000000000000000000000000002"),
            CaptureJson(NativeRowA, NativeRowB), "true"), out _, out _), "receipt field injected into the plan rejected");
        Check(!Decode(ReceiptJson("1", "[]", HexC,
            GrantJson("10000000000000000000000000000001", "10000000000000000000000000000002"),
            CaptureJson(NativeRowA, NativeRowB), "true"), out _, out _), "plan must be an object");

        Check(!Decode(ReceiptJson("1", Plan2Json(), HexC,
            GrantJson("10000000000000000000000000000001"), CaptureJson(NativeRowA, NativeRowB), "true"), out _, out _),
            "grant count below units rejected");
        Check(!Decode(ReceiptJson("1", Plan2Json(), HexC,
            GrantJson("10000000000000000000000000000001", "10000000000000000000000000000002", "10000000000000000000000000000003"),
            CaptureJson(NativeRowA, NativeRowB), "true"), out _, out _), "grant count above units rejected");
        Check(!Decode(ReceiptJson("1", Plan2Json(), HexC,
            GrantJson("10000000000000000000000000000001", "10000000000000000000000000000001"),
            CaptureJson(NativeRowA, NativeRowB), "true"), out _, out _), "duplicate grant ids rejected");
        Check(!Decode(ReceiptJson("1", Plan2Json(), HexC,
            GrantJson(GrantLetters.ToUpperInvariant(), "10000000000000000000000000000002"),
            CaptureJson(NativeRowA, NativeRowB), "true"), out _, out _), "uppercase grant id rejected");
        Check(!Decode(ReceiptJson("1", Plan2Json(), HexC,
            GrantJson("11112222-3333-4444-5555-666677778888", "10000000000000000000000000000002"),
            CaptureJson(NativeRowA, NativeRowB), "true"), out _, out _), "dashed grant id rejected");
        Check(!Decode(ReceiptJson("1", Plan2Json(), HexC, "{}", CaptureJson(NativeRowA, NativeRowB), "true"), out _, out _),
            "grantIds must be an array");

        Check(!Decode(Receipt2Json(captured: "[\"native-row-a\"]"), out _, out _), "capture count below units rejected");
        Check(!Decode(Receipt2Json(captured: "[\"native-row-a\",\"native-row-b\",\"native-row-c\"]"), out _, out _),
            "capture count above units rejected");
        Check(!Decode(Receipt2Json(captured: "[\"native-row-a\",\"native-row-a\"]"), out _, out _), "duplicate capture ids rejected");
        Check(!Decode(Receipt2Json(captured: "[\"\",\"native-row-b\"]"), out _, out _), "empty capture id rejected");
        Check(!Decode(Receipt2Json(captured: "[\"" + new string('n', 257) + "\",\"native-row-b\"]"), out _, out _),
            "257-char capture id rejected");
        Check(Decode(Receipt2Json(captured: "[\"" + new string('n', 256) + "\",\"" + new string('m', 256) + "\"]"), out _, out _),
            "256-char capture id boundary accepted");
        Check(!Decode(Receipt2Json(captured: "\"native-row\""), out _, out _), "capturedNativeIds must be an array");

        Check(!Decode(Receipt2Json(complete: "\"true\""), out _, out _), "string complete rejected");
        Check(!Decode(Receipt2Json(complete: "1"), out _, out _), "numeric complete rejected");
        Check(!Decode(ReceiptJson("1", Plan2Json(), HexC.ToUpperInvariant(),
            GrantJson("10000000000000000000000000000001", "10000000000000000000000000000002"),
            CaptureJson(NativeRowA, NativeRowB), "true"), out _, out _), "uppercase migration epoch rejected");
        Check(!Decode(ReceiptJson("1", Plan2Json(), HexA,
            GrantJson("10000000000000000000000000000001", "10000000000000000000000000000002"),
            CaptureJson(NativeRowA, NativeRowB), "true"), out _, out _), "receipt migration epoch equal to source epoch rejected");

        Check(!Decode("[]", out _, out _), "array root receipt rejected");
        Check(!Decode("{}", out _, out _), "empty object receipt rejected");
        Check(!TryReadReceipt(default, out _, out _), "undefined JsonElement rejected");
    }

    private static void Utf8DecodingStrictness()
    {
        string plan = PlanJson(Rid, ContextName, HexA, HexB, "17", "8");
        int contextValue = plan.IndexOf("\"" + ContextName + "\"", StringComparison.Ordinal) + 1 + 12;
        Check(PlanRejects(Utf8WithByte(plan, contextValue, 0xFF)),
            "0xFF inside the plan context string is rejected without throwing");

        int contextName = plan.IndexOf("\"context\"", StringComparison.Ordinal) + 1 + 2;
        Check(PlanRejects(Utf8WithByte(plan, contextName, 0xFF)),
            "0xFF inside a plan property name is rejected without throwing");

        string receipt = Receipt2Json();
        int captureValue = receipt.IndexOf(NativeRowA, StringComparison.Ordinal) + 3;
        Check(ReceiptRejects(Utf8WithByte(receipt, captureValue, 0xFF)),
            "0xFF inside a capturedNativeIds string is rejected without throwing");
    }

    private static void HistoricalExclusion()
    {
        var plan = Plan17();
        var grants = Grants8();
        Check(TryCreatePending(plan, HexC, grants, out var pending, out _), "pending for exclusion");
        Check(TryComplete(pending, Captures8(grants), out var completed, out _), "completed for exclusion");
        Check(ExcludesHistoricalEpoch(completed, ContextName, HexA), "exact context and source epoch are historical");
        Check(ExcludesHistoricalEpoch(pending, ContextName, HexA), "pending in-flight receipt also excludes the source epoch");
        Check(!ExcludesHistoricalEpoch(completed, ContextName, HexC), "the new migration epoch is not historical");
        Check(!ExcludesHistoricalEpoch(completed, "other-context", HexA), "another context is not excluded");
        Check(!ExcludesHistoricalEpoch(completed, ContextName, HexB), "another epoch is not excluded");
        Check(!ExcludesHistoricalEpoch(completed, ContextName.ToUpperInvariant(), HexA), "context comparison is exact ordinal");
        Check(!ExcludesHistoricalEpoch(completed, ContextName, HexA.ToUpperInvariant()), "epoch comparison is exact ordinal");
        Check(!ExcludesHistoricalEpoch(null, ContextName, HexA), "null receipt never excludes");
        Check(!ExcludesHistoricalEpoch(new MusketeerLegacyCompensationReceipt(plan, HexC, grants[..7], Array.Empty<string>(), false),
            ContextName, HexA), "invalid receipt never excludes");
    }

    private static int Main()
    {
        PlanValidation();
        PlanJsonStrictness();
        PendingBudget();
        CaptureCompletion();
        ReceiptValidation();
        ReceiptCodec();
        ReceiptJsonStrictness();
        Utf8DecodingStrictness();
        HistoricalExclusion();
        Console.WriteLine($"checks={_checks} passed={_checks - _failed} failed={_failed}");
        Console.WriteLine(_failed == 0 ? "RESULT: PASS" : "RESULT: FAIL");
        return _failed == 0 ? 0 : 1;
    }
}
