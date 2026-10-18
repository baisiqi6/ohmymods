using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KingdomEnhancedMod;

// H4 host regression: the two production decision methods are extracted verbatim at build
// time. Identity, sidecar-read and structure-read boundaries are explicit plain stubs.
// This suite does not prove Unity/native ordering, real sidecar IO, payment or save/load.
internal static class Program
{
    private const string DescribeMarker = "    internal static bool TryDescribeDestination(";
    private const string PrepareMarker = "    internal static void NoteDestinationPrepare(";
    private const string FieldsStartMarker = "    private const float Tolerance";
    private const string FieldsEndMarker = "    private static IntPtr _prepareGame, _prepareGlobal, _prepareCampaign;";

    private static readonly List<object> Rows = new List<object>();
    private static int Assertions;
    private static int FailedAssertions;
    private static int PassedCases;
    private static int FailedCases;
    private static int PointerSerial;
    private static string SourcePath;
    private static string MetadataPath;
    private static string GeneratedPath;
    private static string CurrentSourceSha;
    private static string CurrentDescribeSha;
    private static string CurrentPrepareSha;

    private static int Main()
    {
        try
        {
            ResolvePaths();
            ValidateExtraction();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("HARD_FAIL extraction identity: " + e.GetBaseException().Message);
            return 2;
        }

        RunAll();
        var summary = new
        {
            suite = "palace-destination-admission",
            sourceSha256 = CurrentSourceSha,
            methods = new[]
            {
                new { name = "TryDescribeDestination", sha256 = CurrentDescribeSha },
                new { name = "NoteDestinationPrepare", sha256 = CurrentPrepareSha }
            },
            cases = PassedCases + FailedCases,
            passedCases = PassedCases,
            failedCases = FailedCases,
            assertions = Assertions,
            failedAssertions = FailedAssertions,
            results = Rows
        };
        Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        return FailedCases == 0 && FailedAssertions == 0 ? 0 : 1;
    }

    private static void ResolvePaths()
    {
        Dictionary<string, string> metadata = typeof(Program).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(a => a.Key, a => a.Value);
        SourcePath = metadata["AdmissionSourcePath"];
        MetadataPath = metadata["AdmissionExtractionMetadata"];
        GeneratedPath = metadata["AdmissionGeneratedSource"];
        foreach (string path in new[] { SourcePath, MetadataPath, GeneratedPath })
            if (!File.Exists(path)) throw new FileNotFoundException("missing generated identity artifact", path);
    }

    // One raw-byte snapshot for this check. The executed identity is the const baked into this
    // runner at compile time; mutable disk metadata/generated text cannot stand in for it.
    private static void ValidateExtraction()
    {
        byte[] sourceBytes = File.ReadAllBytes(SourcePath);
        string source = new UTF8Encoding(false, true).GetString(sourceBytes);
        CurrentSourceSha = Sha(sourceBytes);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(MetadataPath));
        JsonElement root = document.RootElement;

        string fields = ExtractFields(source);
        string fieldsSha = Sha(Encoding.UTF8.GetBytes(fields));
        Require("source", CurrentSourceSha, root.GetProperty("sourceSha256").GetString(),
            PalaceAdmissionSourceIdentity.SourceSha256);
        Require("fields", fieldsSha, root.GetProperty("fieldsSha256").GetString(),
            PalaceAdmissionSourceIdentity.FieldsSha256);

        var methods = root.GetProperty("methods").EnumerateArray().ToList();
        if (methods.Count != 2) throw new InvalidOperationException("metadata must pin exactly two methods");
        string describe = ExtractMethod(source, DescribeMarker);
        string prepare = ExtractMethod(source, PrepareMarker);
        CurrentDescribeSha = Sha(Encoding.UTF8.GetBytes(describe));
        CurrentPrepareSha = Sha(Encoding.UTF8.GetBytes(prepare));
        ValidateMethod(methods, "TryDescribeDestination", describe, CurrentDescribeSha,
            PalaceAdmissionSourceIdentity.DescribeSha256, PalaceAdmissionSourceIdentity.DescribeChars);
        ValidateMethod(methods, "NoteDestinationPrepare", prepare, CurrentPrepareSha,
            PalaceAdmissionSourceIdentity.PrepareSha256, PalaceAdmissionSourceIdentity.PrepareChars);
    }

    private static void ValidateMethod(List<JsonElement> methods, string name, string method,
        string sha, string compiledSha, int compiledChars)
    {
        JsonElement entry = methods.SingleOrDefault(m => m.GetProperty("name").GetString() == name);
        if (entry.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("metadata missing method " + name);
        if (method.Length != entry.GetProperty("chars").GetInt32())
            throw new InvalidOperationException(name + " body length mismatch: current=" + method.Length
                + " metadata=" + entry.GetProperty("chars").GetInt32());
        Require(name, sha, entry.GetProperty("sha256").GetString(), compiledSha);
        if (method.Length != compiledChars)
            throw new InvalidOperationException(name + " compiled body length mismatch: current=" + method.Length
                + " compiled=" + compiledChars);
    }

    private static void Require(string label, string current, string metadata, string compiled)
    {
        if (!string.Equals(current, metadata, StringComparison.Ordinal))
            throw new InvalidOperationException(label + " metadata mismatch: current=" + current + " metadata=" + metadata);
        if (!string.Equals(current, compiled, StringComparison.Ordinal))
            throw new InvalidOperationException(label + " compiled runner mismatch: current=" + current + " compiled=" + compiled);
    }

    private static string ExtractMethod(string source, string marker)
    {
        int start = source.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("production marker missing: " + marker.Trim());
        int depth = 0;
        bool bodyStarted = false;
        bool lineComment = false, blockComment = false, text = false, verbatim = false, character = false;
        for (int i = start; i < source.Length; i++)
        {
            char c = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : '\0';
            if (lineComment) { if (c == '\n') lineComment = false; continue; }
            if (blockComment) { if (c == '*' && next == '/') { blockComment = false; i++; } continue; }
            if (text) { if (c == '\\') { i++; continue; } if (c == '"') text = false; continue; }
            if (verbatim) { if (c == '"' && next == '"') { i++; continue; } if (c == '"') verbatim = false; continue; }
            if (character) { if (c == '\\') { i++; continue; } if (c == '\'') character = false; continue; }
            if (c == '/' && next == '/') { lineComment = true; i++; continue; }
            if (c == '/' && next == '*') { blockComment = true; i++; continue; }
            if (c == '"') { if (i > 0 && source[i - 1] == '@') verbatim = true; else text = true; continue; }
            if (c == '\'') { character = true; continue; }
            if (c == '{') { depth++; bodyStarted = true; continue; }
            if (c == '}')
            {
                depth--;
                if (bodyStarted && depth == 0) return source.Substring(start, i - start + 1);
            }
        }
        throw new InvalidOperationException("unbalanced production method: " + marker.Trim());
    }

    private static string ExtractFields(string source)
    {
        int start = source.IndexOf(FieldsStartMarker, StringComparison.Ordinal);
        int endMarker = source.IndexOf(FieldsEndMarker, start, StringComparison.Ordinal);
        if (start < 0 || endMarker < 0) throw new InvalidOperationException("cache-field markers missing");
        int lineEnd = source.IndexOf('\n', endMarker);
        if (lineEnd < 0) lineEnd = source.Length;
        return source.Substring(start, lineEnd - start).TrimEnd('\r');
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void RunAll()
    {
        Case("red.missing_noSlot12_isFresh", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(false);
            HostFixture.Metadata = null;
            HostFixture.ReadReason = "missing";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            string observation = Observe(accepted, support, reason, campaign);
            Check(accepted && support == PalaceDestinationSupport.Fresh,
                "missing + no slot12 must be Fresh after the source-side prepare: " + observation);
            Check(Classified, "missing is a completed classification, not an unperformed prepare: " + observation);
            Check(State == "none", "missing keeps sidecarState none: " + observation);
            Check(HostFixture.ReadCalls == 1, "prepare reads the sidecar once: " + observation);
            Prepare(campaign);
            Check(HostFixture.ReadCalls == 1, "same epoch must not re-read the sidecar");
        });

        Case("guard.missing_existingSlot12_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = null;
            HostFixture.ReadReason = "missing";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown && State == "none"
                && HostFixture.ReadCalls == 1 && campaign._islands[12] != null,
                "missing + existing slot12 must stay refused and preserved: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.corrupt_noSlot12_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(false);
            HostFixture.Metadata = null;
            HostFixture.ReadReason = "size:0";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown && !Classified
                && State == "unreadable:size:0",
                "corrupt sidecar must not be classified or accepted: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.corrupt_existingSlot12_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = null;
            HostFixture.ReadReason = "schema:bad";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown && !Classified
                && State == "unreadable:schema:bad" && campaign._islands[12] != null,
                "corrupt + existing slot12 must stay refused: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.foreign_noSlot12_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(false);
            HostFixture.Metadata = Metadata("22222222-2222-2222-2222-222222222222");
            HostFixture.ReadReason = "ok";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown && Classified
                && State == "other-campaign",
                "foreign campaign sidecar must be refused: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.foreign_existingSlot12_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = Metadata("22222222-2222-2222-2222-222222222222");
            HostFixture.ReadReason = "ok";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown
                && State == "other-campaign" && campaign._islands[12] != null,
                "foreign campaign + existing slot12 must stay refused: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.reignMismatch_existingSlot12_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true, reign: 1);
            HostFixture.Metadata = Metadata(reign: 2);
            HostFixture.ReadReason = "ok";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown
                && State == "reign-mismatch" && campaign._islands[12] != null,
                "same guid/slot different reign must be refused: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.ownedMetadata_noSlot12_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(false);
            HostFixture.Metadata = Metadata();
            HostFixture.ReadReason = "ok";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown
                && State == "this-campaign",
                "this-campaign sidecar without slot12 is an unknown combination: " + Observe(accepted, support, reason, campaign));
        });

        Case("control.ownedMetadata_validSaved_supported", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = Metadata();
            HostFixture.ReadReason = "ok";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(accepted && support == PalaceDestinationSupport.SupportedSaved && State == "this-campaign"
                && HostFixture.ReadCalls == 1,
                "owned sidecar with matching endpoints and structure must be supported: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.beforePrepare_refused_noIO", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(false);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown && !Classified
                && HostFixture.ReadCalls == 0,
                "describe before the prepare window must refuse without IO: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.bindingFailure_noIO_unclassified", () =>
        {
            Reset();
            HostFixture.BindOk = false;
            CampaignSaveData campaign = NewCampaign(false);
            Prepare(campaign);
            Check(!Classified && State == "unreadable:campaign-binding-failed" && HostFixture.ReadCalls == 0,
                "binding failure must not read or classify: state=" + State + " readCalls=" + HostFixture.ReadCalls);
        });

        Case("guard.metadataException_refused", () =>
        {
            Reset();
            HostFixture.ThrowRead = true;
            CampaignSaveData campaign = NewCampaign(false);
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown && !Classified
                && State.StartsWith("unreadable:fault:", StringComparison.Ordinal),
                "sidecar read exception must be refused as unreadable: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.endpointMismatch_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true, edgeMin: -22, edgeMax: 22);
            HostFixture.Metadata = Metadata(min: -21, max: 22);
            HostFixture.ReadReason = "ok";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown
                && State == "this-campaign" && reason.Contains("端点", StringComparison.Ordinal),
                "saved endpoints must match the sidecar: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.beachCountMismatch_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = Metadata();
            HostFixture.ReadReason = "ok";
            HostFixture.Structure.BeachObjectCount = 1;
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown
                && State == "this-campaign" && reason.Contains("Beach", StringComparison.Ordinal),
                "exactly two canonical beaches are required: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.beachLeftMismatch_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = Metadata();
            HostFixture.ReadReason = "ok";
            HostFixture.Structure.BeachLeftOk = false;
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown
                && State == "this-campaign" && reason.Contains("Beach", StringComparison.Ordinal),
                "left beach anchor must match: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.beachRightMismatch_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = Metadata();
            HostFixture.ReadReason = "ok";
            HostFixture.Structure.BeachRightOk = false;
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown
                && State == "this-campaign" && reason.Contains("Beach", StringComparison.Ordinal),
                "right beach anchor must match: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.tileGridMismatch_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = Metadata();
            HostFixture.ReadReason = "ok";
            HostFixture.Structure.TileGridOk = false;
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown
                && State == "this-campaign" && reason.Contains("Tile", StringComparison.Ordinal),
                "11x4u tile grid must match: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.structureUnreadable_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = Metadata();
            HostFixture.ReadReason = "ok";
            HostFixture.StructureReadable = false;
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown
                && State == "this-campaign" && reason.Contains("结构不可读", StringComparison.Ordinal),
                "unreadable saved structure must be refused: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.identityMismatch_afterPrepare_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = Metadata();
            HostFixture.ReadReason = "ok";
            Prepare(campaign);
            HostFixture.Guid = "22222222-2222-2222-2222-222222222222";
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown
                && HostFixture.ReadCalls == 1 && reason.Contains("身份", StringComparison.Ordinal),
                "identity changed after prepare must force a fresh prepare: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.reignChanged_afterPrepare_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = Metadata();
            HostFixture.ReadReason = "ok";
            Prepare(campaign);
            campaign.reign = 2;
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown
                && HostFixture.ReadCalls == 1 && reason.Contains("reign", StringComparison.Ordinal),
                "reign changed after prepare must force a fresh prepare: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.nullReasonEmpty_noSlot12_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(false);
            HostFixture.Metadata = null;
            HostFixture.ReadReason = "";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown && !Classified
                && State.StartsWith("unreadable:", StringComparison.Ordinal),
                "empty null-read reason is unknown, not missing: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.nullReasonIoError_existingSlot12_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = null;
            HostFixture.ReadReason = "io-error";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown && !Classified
                && State == "unreadable:io-error" && campaign._islands[12] != null,
                "IO error + existing slot12 must stay refused: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.nullReasonNearMiss_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(false);
            HostFixture.Metadata = null;
            HostFixture.ReadReason = "Missing";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown && !Classified
                && State == "unreadable:Missing",
                "only exact ordinal missing is classified, near misses are unknown: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.ownerReignMismatch_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true, reign: 2);
            HostFixture.Metadata = Metadata(reign: 2);
            HostFixture.ReadReason = "ok";
            HostFixture.OwnerReign = 1;
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown
                && State == "reign-mismatch",
                "native owner reign must agree with the sidecar: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.foreignSlotMismatch_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = Metadata(slot: 1);
            HostFixture.ReadReason = "ok";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown
                && State == "other-campaign",
                "same guid but different slot is another campaign: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.foreignPriorityOverReign_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true, reign: 1);
            HostFixture.Metadata = Metadata("22222222-2222-2222-2222-222222222222", slot: 7, reign: 99);
            HostFixture.ReadReason = "ok";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown
                && State == "other-campaign",
                "foreign identity is classified before reign mismatch: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.ownedMetadata_shortIslandList_refused", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(false, islandCount: 3);
            HostFixture.Metadata = Metadata();
            HostFixture.ReadReason = "ok";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown
                && State == "this-campaign" && reason.Contains("无 slot12", StringComparison.Ordinal),
                "a short island list has no slot12 and stays refused: " + Observe(accepted, support, reason, campaign));
        });

        Case("guard.missingExistingSlot12_sameEpochRefused_noIO", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = null;
            HostFixture.ReadReason = "missing";
            Prepare(campaign);
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(!accepted && support == PalaceDestinationSupport.UnsupportedOrUnknown
                && State == "none" && HostFixture.ReadCalls == 1 && campaign._islands[12] != null,
                "same epoch keeps the refusal and does not reread: " + Observe(accepted, support, reason, campaign));
        });

        Case("control.validSaved_sameEpochStable_noIO", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = Metadata();
            HostFixture.ReadReason = "ok";
            Prepare(campaign);
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(accepted && support == PalaceDestinationSupport.SupportedSaved
                && State == "this-campaign" && HostFixture.ReadCalls == 1,
                "same epoch keeps a valid saved destination stable: " + Observe(accepted, support, reason, campaign));
        });

        Case("control.validSaved_edgesAtBoundary_supported", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true, edgeMin: -100, edgeMax: 100);
            HostFixture.Metadata = Metadata(min: -100, max: 100);
            HostFixture.ReadReason = "ok";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(accepted && support == PalaceDestinationSupport.SupportedSaved,
                "matching non-default endpoints remain valid: " + Observe(accepted, support, reason, campaign));
        });

        Case("epoch.newGame_reclassifiesCorruptToForeign", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = null;
            HostFixture.ReadReason = "size:0";
            Prepare(campaign);
            Describe(campaign, out _, out _);
            Managers.Inst = new Managers(NewGame());
            HostFixture.Metadata = Metadata("22222222-2222-2222-2222-222222222222");
            HostFixture.ReadReason = "ok";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(HostFixture.ReadCalls == 2 && State == "other-campaign" && Classified && !accepted
                && support == PalaceDestinationSupport.UnsupportedOrUnknown,
                "new Game pointer must trigger exactly one reclassification: " + Observe(accepted, support, reason, campaign));
        });

        Case("epoch.newGlobal_reclassifiesForeignToMissing", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = Metadata("22222222-2222-2222-2222-222222222222");
            HostFixture.ReadReason = "ok";
            Prepare(campaign);
            GlobalSaveData.loaded = new GlobalSaveData((IntPtr)(++PointerSerial));
            HostFixture.Metadata = null;
            HostFixture.ReadReason = "missing";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(HostFixture.ReadCalls == 2 && State == "none" && !accepted
                && support == PalaceDestinationSupport.UnsupportedOrUnknown && campaign._islands[12] != null,
                "new Global pointer must reread and replace the cached sidecar state: " + Observe(accepted, support, reason, campaign));
        });

        Case("epoch.newCampaign_reclassifiesMissingToCorrupt", () =>
        {
            Reset();
            CampaignSaveData first = NewCampaign(true);
            HostFixture.Metadata = null;
            HostFixture.ReadReason = "missing";
            Prepare(first);
            CampaignSaveData second = NewCampaign(true);
            HostFixture.ReadReason = "schema:bad";
            Prepare(second);
            bool accepted = Describe(second, out PalaceDestinationSupport support, out string reason);
            Check(HostFixture.ReadCalls == 2 && State == "unreadable:schema:bad" && !Classified && !accepted
                && support == PalaceDestinationSupport.UnsupportedOrUnknown,
                "new Campaign pointer must reread and replace the cached sidecar state: " + Observe(accepted, support, reason, second));
        });

        Case("epoch.newReign_reclassifies", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true, reign: 1);
            HostFixture.Metadata = null;
            HostFixture.ReadReason = "size:0";
            Prepare(campaign);
            campaign.reign = 2;
            HostFixture.ReadReason = "missing";
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(HostFixture.ReadCalls == 2 && State == "none" && !accepted
                && support == PalaceDestinationSupport.UnsupportedOrUnknown && campaign._islands[12] != null,
                "new reign must reread and replace the cached sidecar state: " + Observe(accepted, support, reason, campaign));
        });

        Case("epoch.newGuid_reclassifies", () =>
        {
            Reset();
            CampaignSaveData campaign = NewCampaign(true);
            HostFixture.Metadata = Metadata();
            HostFixture.ReadReason = "ok";
            Prepare(campaign);
            HostFixture.Guid = "33333333-3333-3333-3333-333333333333";
            HostFixture.Metadata = Metadata();
            Prepare(campaign);
            bool accepted = Describe(campaign, out PalaceDestinationSupport support, out string reason);
            Check(accepted && support == PalaceDestinationSupport.SupportedSaved
                && State == "this-campaign" && HostFixture.ReadCalls == 2,
                "new durable GUID must reread and classify the new identity: " + Observe(accepted, support, reason, campaign));
        });
    }

    private static void Case(string name, Action body)
    {
        int assertionsBefore = Assertions;
        int failedBefore = FailedAssertions;
        string error = null;
        try
        {
            body();
        }
        catch (Exception e)
        {
            error = e.GetBaseException().Message;
        }
        bool passed = error == null && FailedAssertions == failedBefore;
        if (passed) PassedCases++; else FailedCases++;
        Rows.Add(new { name, passed, assertions = Assertions - assertionsBefore, error });
        Console.WriteLine((passed ? "PASS " : "FAIL ") + name + (error == null ? "" : ": " + error));
    }

    private static void Check(bool condition, string message)
    {
        Assertions++;
        if (!condition)
        {
            FailedAssertions++;
            throw new Exception(message);
        }
    }

    private static void Reset()
    {
        PointerSerial = 100;
        foreach (FieldInfo field in typeof(PalaceDestinationAdmission)
                     .GetFields(BindingFlags.Static | BindingFlags.NonPublic))
        {
            if (field.IsInitOnly || field.IsLiteral) continue;
            object value;
            if (field.FieldType == typeof(string)) value = "";
            else if (field.FieldType == typeof(int)) value = -1;
            else if (field.FieldType == typeof(bool)) value = false;
            else if (field.FieldType == typeof(IntPtr)) value = IntPtr.Zero;
            else continue;
            field.SetValue(null, value);
        }
        HostFixture.Reset();
        Managers.Inst = new Managers(NewGame());
        GlobalSaveData.loaded = new GlobalSaveData((IntPtr)(++PointerSerial));
    }

    private static CampaignSaveData NewCampaign(bool slot12, int islandCount = 13, int reign = 1,
        int edgeMin = -22, int edgeMax = 22)
    {
        var campaign = new CampaignSaveData((IntPtr)(++PointerSerial)) { reign = reign };
        for (int i = 0; i < islandCount; i++) campaign._islands.Add(null);
        if (slot12 && islandCount > 12)
            campaign._islands[12] = new IslandSaveData
            {
                _levelEdges = new IntRange { min = edgeMin, max = edgeMax }
            };
        return campaign;
    }

    private static Game NewGame() => new Game((IntPtr)(++PointerSerial));

    private static PalaceCourtArea.CourtAreaMetadataRead Metadata(string guid = null, int slot = 0,
        int reign = 1, int min = -22, int max = 22) =>
        new PalaceCourtArea.CourtAreaMetadataRead
        {
            CampaignGuid = guid ?? HostFixture.Guid,
            CampaignSlot = slot,
            Reign = reign,
            NativeEdgesMin = min,
            NativeEdgesMax = max
        };

    private static void Prepare(CampaignSaveData campaign) =>
        PalaceDestinationAdmission.NoteDestinationPrepare(campaign);

    private static bool Describe(CampaignSaveData campaign, out PalaceDestinationSupport support,
        out string reason) =>
        PalaceDestinationAdmission.TryDescribeDestination(campaign, out support, out reason);

    private static bool Classified => (bool)typeof(PalaceDestinationAdmission)
        .GetField("_classified", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

    private static string State => (string)typeof(PalaceDestinationAdmission)
        .GetField("_sidecarState", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

    private static string Observe(bool accepted, PalaceDestinationSupport support, string reason,
        CampaignSaveData campaign) =>
        "accepted=" + accepted + " support=" + support + " classified=" + Classified + " state=" + State
        + " readCalls=" + HostFixture.ReadCalls + " slot12=" + (campaign._islands.Count > 12 && campaign._islands[12] != null)
        + " reason=" + reason;
}
