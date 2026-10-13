using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace KingdomEnhancedMod;

// Issue #223 one-time legacy compensation kernel: pure value model, strict bounded JSON codec and
// caller-data validation only. No IO, no Unity, no Harmony, no game state, no Harmony hooks.
//
// Trust boundary: the config path carries a Plan only (TryParsePlan rejects receipt fields); a
// Receipt is built from the grant budget plus captures reported by the production callback and is
// read back from the versioned archive / native save (TryReadReceipt). Persisted receipts are
// always complete: a Pending receipt exists only in memory while the attempt is in flight, and
// the writer guard refuses to serialize one. This policy validates caller-provided data; it never
// claims the native captures were verified against the live game, and it never allocates grants,
// maps legacy ids or bypasses any purchase gate itself.
internal sealed class MusketeerLegacyCompensationPlan
{
    internal MusketeerLegacyCompensationPlan(
        int version,
        Guid requestId,
        string context,
        string sourceEpoch,
        string sourceArchiveSha,
        int sourceReservations,
        int units)
    {
        Version = version;
        RequestId = requestId;
        Context = context;
        SourceEpoch = sourceEpoch;
        SourceArchiveSha = sourceArchiveSha;
        SourceReservations = sourceReservations;
        Units = units;
    }

    internal int Version { get; }
    internal Guid RequestId { get; }
    internal string Context { get; }
    internal string SourceEpoch { get; }
    internal string SourceArchiveSha { get; }
    internal int SourceReservations { get; }
    internal int Units { get; }
}

// One capture as reported by the production callback for a single grant. The kernel checks the
// shape only: Kind 1 (unit), the live-only Archer prefab path and exactly one occurrence.
internal readonly struct MusketeerLegacyGrantCapture
{
    internal const int UnitKind = 1;
    internal const string MusketeerPrefabPath = "Prefabs/Characters/Archer";

    internal MusketeerLegacyGrantCapture(Guid grantId, string nativeId, int kind, string prefabPath, int nativeOccurrences)
    {
        GrantId = grantId;
        NativeId = nativeId;
        Kind = kind;
        PrefabPath = prefabPath;
        NativeOccurrences = nativeOccurrences;
    }

    internal Guid GrantId { get; }
    internal string NativeId { get; }
    internal int Kind { get; }
    internal string PrefabPath { get; }
    internal int NativeOccurrences { get; }
}

// Receipt for one compensation attempt.
// Pending  (Complete=false): GrantIds are the plan's unit budget (unique, non-empty; the caller
// allocates them freshly and must not reuse any legacy root id), and the capture entries are
// empty because nothing was captured yet.
// Completed (Complete=true): CapturedNativeIds holds exactly one non-empty, unique, <=256-char
// native id per grant, in grant order. Completion is a historical first-capture claim: later
// deaths never revoke it, and no live population count participates in this model or its policy.
internal sealed class MusketeerLegacyCompensationReceipt
{
    internal MusketeerLegacyCompensationReceipt(
        MusketeerLegacyCompensationPlan plan,
        string migrationEpoch,
        IReadOnlyList<Guid> grantIds,
        IReadOnlyList<string> capturedNativeIds,
        bool complete)
    {
        Plan = plan;
        MigrationEpoch = migrationEpoch;
        GrantIds = Copy(grantIds);
        CapturedNativeIds = Copy(capturedNativeIds);
        Complete = complete;
    }

    internal MusketeerLegacyCompensationPlan Plan { get; }
    internal string MigrationEpoch { get; }
    internal IReadOnlyList<Guid> GrantIds { get; }
    internal IReadOnlyList<string> CapturedNativeIds { get; }
    internal bool Complete { get; }

    // Every construction copies its inputs and hands back read-only views, so mutating the
    // caller's list/array afterwards (or casting the view back to an array) cannot change a
    // receipt that was already built.
    private static ReadOnlyCollection<T> Copy<T>(IReadOnlyList<T> items)
    {
        if (items == null) return null;
        var copy = new T[items.Count];
        for (int i = 0; i < items.Count; i++) copy[i] = items[i];
        return Array.AsReadOnly(copy);
    }
}

internal static class MusketeerLegacyCompensationPolicy
{
    internal const int PlanSchemaVersion = 1;
    internal const int MinSourceReservations = 2;
    internal const int MaxSourceReservations = 4096;
    internal const int MaxUnits = 32;
    internal const int MaxNativeIdLength = 256;
    internal const int MaxPlanJsonBytes = 16 * 1024;

    private static readonly JsonDocumentOptions StrictJson = new JsonDocumentOptions
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 8
    };

    // ------------------------------------------------------------------ plan

    internal static bool ValidatePlan(MusketeerLegacyCompensationPlan plan, out string reason)
    {
        if (plan == null) { reason = "plan is null"; return false; }
        if (plan.Version != PlanSchemaVersion) { reason = "version must be " + PlanSchemaVersion; return false; }
        if (plan.RequestId == Guid.Empty) { reason = "requestId must be non-empty"; return false; }
        if (!IsLowerHex64(plan.Context)) { reason = "context must be 64 lowercase hex chars"; return false; }
        if (!IsLowerHex64(plan.SourceEpoch)) { reason = "sourceEpoch must be 64 lowercase hex chars"; return false; }
        if (!IsLowerHex64(plan.SourceArchiveSha)) { reason = "sourceArchiveSha must be 64 lowercase hex chars"; return false; }
        if (plan.SourceReservations < MinSourceReservations || plan.SourceReservations > MaxSourceReservations)
        { reason = "sourceReservations must be " + MinSourceReservations + ".." + MaxSourceReservations; return false; }
        if (plan.Units != plan.SourceReservations / 2 || plan.Units < 1 || plan.Units > MaxUnits)
        { reason = "units must equal floor(sourceReservations/2) within 1.." + MaxUnits; return false; }
        reason = null;
        return true;
    }

    // Config-path entry point: a plan document only. Receipt fields (complete, migrationEpoch,
    // capturedNativeIds, a nested plan wrapper, ...) are unknown fields here and are rejected.
    internal static bool TryParsePlan(byte[] bytes, out MusketeerLegacyCompensationPlan plan, out string reason)
    {
        plan = null;
        if (bytes == null) { reason = "plan json is null"; return false; }
        if (bytes.Length == 0) { reason = "plan json is empty"; return false; }
        if (bytes.Length > MaxPlanJsonBytes) { reason = "plan json exceeds " + MaxPlanJsonBytes + " bytes"; return false; }
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        { reason = "plan json must not start with a BOM"; return false; }
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, StrictJson);
        }
        catch (JsonException)
        {
            reason = "plan json must be strict UTF-8 JSON";
            return false;
        }
        catch (InvalidOperationException)
        {
            // A property name with invalid UTF-8 can surface while parsing (transcode failure).
            reason = "plan json must be valid UTF-8";
            return false;
        }
        using (document)
        {
            // Decoding a string value or a property name with invalid UTF-8 throws
            // InvalidOperationException ("Cannot transcode invalid UTF-8 JSON text to UTF-16
            // string"). The trap is limited to this decode region and to that one exception type.
            try
            {
                return TryParsePlanElement(document.RootElement, out plan, out reason);
            }
            catch (InvalidOperationException)
            {
                plan = null;
                reason = "plan json must be valid UTF-8";
                return false;
            }
        }
    }

    // Shared strict body for a plan object, used by the config parser and by the nested plan of a
    // persisted receipt. Field set is exact; duplicates, unknown names and wrong types are
    // rejected, and the accepted set is order-independent.
    private static bool TryParsePlanElement(JsonElement node, out MusketeerLegacyCompensationPlan plan, out string reason)
    {
        plan = null;
        if (node.ValueKind != JsonValueKind.Object) { reason = "plan must be a JSON object"; return false; }
        int? version = null;
        int? reservations = null;
        int? units = null;
        string requestId = null;
        string context = null;
        string sourceEpoch = null;
        string sourceArchiveSha = null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in node.EnumerateObject())
        {
            if (!names.Add(property.Name)) { reason = "duplicate plan field " + property.Name; return false; }
            switch (property.Name)
            {
                case "version":
                    if (!TryGetInt32(property.Value, out int parsedVersion)) { reason = "version must be an integer"; return false; }
                    version = parsedVersion;
                    break;
                case "requestId":
                    if (!TryGetString(property.Value, out requestId)) { reason = "requestId must be a string"; return false; }
                    break;
                case "context":
                    if (!TryGetString(property.Value, out context)) { reason = "context must be a string"; return false; }
                    break;
                case "sourceEpoch":
                    if (!TryGetString(property.Value, out sourceEpoch)) { reason = "sourceEpoch must be a string"; return false; }
                    break;
                case "sourceArchiveSha":
                    if (!TryGetString(property.Value, out sourceArchiveSha)) { reason = "sourceArchiveSha must be a string"; return false; }
                    break;
                case "sourceReservations":
                    if (!TryGetInt32(property.Value, out int parsedReservations)) { reason = "sourceReservations must be an integer"; return false; }
                    reservations = parsedReservations;
                    break;
                case "units":
                    if (!TryGetInt32(property.Value, out int parsedUnits)) { reason = "units must be an integer"; return false; }
                    units = parsedUnits;
                    break;
                default:
                    reason = "unknown plan field " + property.Name;
                    return false;
            }
        }
        if (version == null || reservations == null || units == null
            || requestId == null || context == null || sourceEpoch == null || sourceArchiveSha == null)
        { reason = "plan is missing a required field"; return false; }
        if (!IsLowerHex32(requestId) || !Guid.TryParseExact(requestId, "N", out var requestGuid))
        { reason = "requestId must be a canonical 32-char lowercase hex GUID (N)"; return false; }
        plan = new MusketeerLegacyCompensationPlan(version.Value, requestGuid, context, sourceEpoch, sourceArchiveSha, reservations.Value, units.Value);
        return ValidatePlan(plan, out reason);
    }

    // Exact snapshot comparison against the source a production callback observed. Pure in-memory
    // comparison; no IO and no re-resolution happens here.
    internal static bool MatchesSource(
        MusketeerLegacyCompensationPlan plan,
        string context,
        string sourceEpoch,
        string sourceArchiveSha,
        int sourceReservations)
    {
        if (!ValidatePlan(plan, out _)) return false;
        return string.Equals(plan.Context, context, StringComparison.Ordinal)
            && string.Equals(plan.SourceEpoch, sourceEpoch, StringComparison.Ordinal)
            && string.Equals(plan.SourceArchiveSha, sourceArchiveSha, StringComparison.Ordinal)
            && plan.SourceReservations == sourceReservations;
    }

    // ------------------------------------------------------------------ receipts

    // The caller allocates exactly plan.Units fresh grants before the attempt starts. The kernel
    // enforces count/uniqueness/non-empty; the "never a legacy root id" allocation rule lives in
    // the caller because pure code cannot observe the legacy root.
    internal static bool TryCreatePending(
        MusketeerLegacyCompensationPlan plan,
        string migrationEpoch,
        IReadOnlyList<Guid> grants,
        out MusketeerLegacyCompensationReceipt receipt,
        out string reason)
    {
        receipt = null;
        if (!ValidatePlan(plan, out reason)) return false;
        if (!IsLowerHex64(migrationEpoch)) { reason = "migrationEpoch must be 64 lowercase hex chars"; return false; }
        if (string.Equals(migrationEpoch, plan.SourceEpoch, StringComparison.Ordinal))
        { reason = "migrationEpoch must differ from sourceEpoch"; return false; }
        if (grants == null) { reason = "grants is null"; return false; }
        if (grants.Count != plan.Units) { reason = "grants count must equal units"; return false; }
        var seen = new HashSet<Guid>();
        for (int i = 0; i < grants.Count; i++)
        {
            if (grants[i] == Guid.Empty) { reason = "grants must be non-empty"; return false; }
            if (!seen.Add(grants[i])) { reason = "grants must be unique"; return false; }
        }
        receipt = new MusketeerLegacyCompensationReceipt(plan, migrationEpoch, grants, Array.Empty<string>(), false);
        reason = null;
        return true;
    }

    // Completes a pending receipt against captures the production callback actually observed. To
    // complete, every grant must be covered exactly once by a matching unit capture: grant id in
    // the plan budget, unique native id, Kind 1, the Archer prefab path, one occurrence. A
    // completed receipt refuses further completions, so the budget can never be issued twice.
    internal static bool TryComplete(
        MusketeerLegacyCompensationReceipt pending,
        IReadOnlyList<MusketeerLegacyGrantCapture> captures,
        out MusketeerLegacyCompensationReceipt completed,
        out string reason)
    {
        completed = null;
        if (pending == null) { reason = "pending receipt is null"; return false; }
        if (!ValidateReceipt(pending, out reason)) return false;
        if (pending.Complete)
        { reason = "receipt is already complete; the grant budget is never re-issued"; return false; }
        if (captures == null) { reason = "captures is null"; return false; }
        if (captures.Count != pending.GrantIds.Count) { reason = "captures must cover every grant exactly once"; return false; }
        var grantIndex = new Dictionary<Guid, int>(pending.GrantIds.Count);
        for (int i = 0; i < pending.GrantIds.Count; i++) grantIndex.Add(pending.GrantIds[i], i);
        var nativeIds = new string[pending.GrantIds.Count];
        var filled = new bool[pending.GrantIds.Count];
        var seenNative = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < captures.Count; i++)
        {
            MusketeerLegacyGrantCapture capture = captures[i];
            if (!grantIndex.TryGetValue(capture.GrantId, out int index)) { reason = "capture grant is not part of this plan"; return false; }
            if (filled[index]) { reason = "capture grant is duplicated"; return false; }
            if (capture.NativeId == null || capture.NativeId.Length == 0 || capture.NativeId.Length > MaxNativeIdLength)
            { reason = "capture nativeId must be 1.." + MaxNativeIdLength + " chars"; return false; }
            if (capture.Kind != MusketeerLegacyGrantCapture.UnitKind) { reason = "capture kind must be " + MusketeerLegacyGrantCapture.UnitKind; return false; }
            if (!string.Equals(capture.PrefabPath, MusketeerLegacyGrantCapture.MusketeerPrefabPath, StringComparison.Ordinal))
            { reason = "capture prefabPath must be " + MusketeerLegacyGrantCapture.MusketeerPrefabPath; return false; }
            if (capture.NativeOccurrences != 1) { reason = "capture nativeOccurrences must be 1"; return false; }
            if (!seenNative.Add(capture.NativeId)) { reason = "captured native ids must be unique"; return false; }
            nativeIds[index] = capture.NativeId;
            filled[index] = true;
        }
        for (int i = 0; i < filled.Length; i++)
        {
            if (!filled[i]) { reason = "captures must cover every grant exactly once"; return false; }
        }
        completed = new MusketeerLegacyCompensationReceipt(pending.Plan, pending.MigrationEpoch, pending.GrantIds, nativeIds, true);
        reason = null;
        return true;
    }

    internal static bool ValidateReceipt(MusketeerLegacyCompensationReceipt receipt, out string reason)
    {
        if (receipt == null) { reason = "receipt is null"; return false; }
        if (!ValidatePlan(receipt.Plan, out reason)) return false;
        if (!IsLowerHex64(receipt.MigrationEpoch)) { reason = "migrationEpoch must be 64 lowercase hex chars"; return false; }
        if (string.Equals(receipt.MigrationEpoch, receipt.Plan.SourceEpoch, StringComparison.Ordinal))
        { reason = "migrationEpoch must differ from sourceEpoch"; return false; }
        var grants = receipt.GrantIds;
        if (grants == null || grants.Count != receipt.Plan.Units) { reason = "grantIds count must equal units"; return false; }
        var seen = new HashSet<Guid>();
        for (int i = 0; i < grants.Count; i++)
        {
            if (grants[i] == Guid.Empty) { reason = "grantIds must be non-empty"; return false; }
            if (!seen.Add(grants[i])) { reason = "grantIds must be unique"; return false; }
        }
        var captured = receipt.CapturedNativeIds;
        if (captured == null) { reason = "capturedNativeIds is null"; return false; }
        if (!receipt.Complete)
        {
            if (captured.Count != 0) { reason = "pending capture entries must all be empty"; return false; }
            reason = null;
            return true;
        }
        if (captured.Count != receipt.Plan.Units) { reason = "capturedNativeIds count must equal units"; return false; }
        var seenNative = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < captured.Count; i++)
        {
            string nativeId = captured[i];
            if (nativeId == null || nativeId.Length == 0 || nativeId.Length > MaxNativeIdLength)
            { reason = "capturedNativeIds entries must be 1.." + MaxNativeIdLength + " chars"; return false; }
            if (!seenNative.Add(nativeId)) { reason = "capturedNativeIds must be unique"; return false; }
        }
        reason = null;
        return true;
    }

    // ------------------------------------------------------------------ persisted receipt codec

    // Stable wire schema, written in a fixed field order. Only validated receipts are written, and
    // the guard keeps a pending receipt out of every persisted store ("native physical" must never
    // carry Pending).
    internal static void WriteReceipt(Utf8JsonWriter writer, MusketeerLegacyCompensationReceipt receipt)
    {
        if (writer == null) throw new ArgumentNullException(nameof(writer));
        if (receipt == null) throw new ArgumentNullException(nameof(receipt));
        if (!ValidateReceipt(receipt, out string reason)) throw new InvalidOperationException("receipt does not validate: " + reason);
        if (!receipt.Complete) throw new InvalidOperationException("pending receipts must not be persisted");
        writer.WriteStartObject();
        writer.WriteNumber("version", PlanSchemaVersion);
        writer.WritePropertyName("plan");
        WritePlanElement(writer, receipt.Plan);
        writer.WriteString("migrationEpoch", receipt.MigrationEpoch);
        writer.WriteStartArray("grantIds");
        for (int i = 0; i < receipt.GrantIds.Count; i++) writer.WriteStringValue(receipt.GrantIds[i].ToString("N"));
        writer.WriteEndArray();
        writer.WriteStartArray("capturedNativeIds");
        for (int i = 0; i < receipt.CapturedNativeIds.Count; i++) writer.WriteStringValue(receipt.CapturedNativeIds[i]);
        writer.WriteEndArray();
        writer.WriteBoolean("complete", true);
        writer.WriteEndObject();
    }

    // Archive / native-save entry point. Strict and bounded: exact field set, no duplicates, no
    // unknown or future fields, strict types, and the same plan body a config plan uses. A
    // persisted receipt is always complete; parsing a pending document fails closed.
    internal static bool TryReadReceipt(JsonElement node, out MusketeerLegacyCompensationReceipt receipt, out string reason)
    {
        // Decoding a string value or a property name with invalid UTF-8 throws
        // InvalidOperationException. The guard is limited to this decode region and to that one
        // exception type; valid UTF-8 documents take the unchanged path.
        try
        {
            return TryReadReceiptCore(node, out receipt, out reason);
        }
        catch (InvalidOperationException)
        {
            receipt = null;
            reason = "receipt json must be valid UTF-8";
            return false;
        }
    }

    private static bool TryReadReceiptCore(JsonElement node, out MusketeerLegacyCompensationReceipt receipt, out string reason)
    {
        receipt = null;
        if (node.ValueKind != JsonValueKind.Object) { reason = "receipt must be a JSON object"; return false; }
        int? version = null;
        MusketeerLegacyCompensationPlan plan = null;
        string migrationEpoch = null;
        List<Guid> grantIds = null;
        List<string> capturedNativeIds = null;
        bool? complete = null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in node.EnumerateObject())
        {
            if (!names.Add(property.Name)) { reason = "duplicate receipt field " + property.Name; return false; }
            switch (property.Name)
            {
                case "version":
                    if (!TryGetInt32(property.Value, out int parsedVersion)) { reason = "version must be an integer"; return false; }
                    version = parsedVersion;
                    break;
                case "plan":
                    if (!TryParsePlanElement(property.Value, out plan, out reason)) return false;
                    break;
                case "migrationEpoch":
                    if (!TryGetString(property.Value, out migrationEpoch)) { reason = "migrationEpoch must be a string"; return false; }
                    break;
                case "grantIds":
                    if (!TryReadGuidArray(property.Value, out grantIds, out reason)) return false;
                    break;
                case "capturedNativeIds":
                    if (!TryReadNativeIdArray(property.Value, out capturedNativeIds, out reason)) return false;
                    break;
                case "complete":
                    if (property.Value.ValueKind == JsonValueKind.True) complete = true;
                    else if (property.Value.ValueKind == JsonValueKind.False) complete = false;
                    else { reason = "complete must be a boolean"; return false; }
                    break;
                default:
                    reason = "unknown receipt field " + property.Name;
                    return false;
            }
        }
        if (version == null || plan == null || migrationEpoch == null || grantIds == null || capturedNativeIds == null || complete == null)
        { reason = "receipt is missing a required field"; return false; }
        if (version.Value != PlanSchemaVersion) { reason = "receipt version must be " + PlanSchemaVersion; return false; }
        var parsed = new MusketeerLegacyCompensationReceipt(plan, migrationEpoch, grantIds, capturedNativeIds, complete.Value);
        if (!ValidateReceipt(parsed, out reason)) return false;
        if (!parsed.Complete) { reason = "pending receipts must not be persisted"; return false; }
        receipt = parsed;
        reason = null;
        return true;
    }

    // Exact-match historical filter for the one compensated context: only the receipt of the same
    // context and the authenticated source epoch is historical (pending or complete), so a new
    // migration or another context can never resurrect the compensated legacy epoch.
    internal static bool ExcludesHistoricalEpoch(MusketeerLegacyCompensationReceipt receipt, string context, string epoch)
    {
        if (!ValidateReceipt(receipt, out _)) return false;
        return string.Equals(receipt.Plan.Context, context, StringComparison.Ordinal)
            && string.Equals(receipt.Plan.SourceEpoch, epoch, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ shared helpers

    private static void WritePlanElement(Utf8JsonWriter writer, MusketeerLegacyCompensationPlan plan)
    {
        writer.WriteStartObject();
        writer.WriteNumber("version", plan.Version);
        writer.WriteString("requestId", plan.RequestId.ToString("N"));
        writer.WriteString("context", plan.Context);
        writer.WriteString("sourceEpoch", plan.SourceEpoch);
        writer.WriteString("sourceArchiveSha", plan.SourceArchiveSha);
        writer.WriteNumber("sourceReservations", plan.SourceReservations);
        writer.WriteNumber("units", plan.Units);
        writer.WriteEndObject();
    }

    private static bool TryReadGuidArray(JsonElement value, out List<Guid> ids, out string reason)
    {
        ids = null;
        if (value.ValueKind != JsonValueKind.Array) { reason = "grantIds must be an array"; return false; }
        if (value.GetArrayLength() > MaxUnits) { reason = "grantIds exceeds the unit budget"; return false; }
        var list = new List<Guid>(value.GetArrayLength());
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String) { reason = "grantId must be a string"; return false; }
            string text = element.GetString();
            if (!IsLowerHex32(text) || !Guid.TryParseExact(text, "N", out var id) || id == Guid.Empty)
            { reason = "grantId must be a non-empty canonical 32-char lowercase hex GUID (N)"; return false; }
            list.Add(id);
        }
        ids = list;
        reason = null;
        return true;
    }

    private static bool TryReadNativeIdArray(JsonElement value, out List<string> ids, out string reason)
    {
        ids = null;
        if (value.ValueKind != JsonValueKind.Array) { reason = "capturedNativeIds must be an array"; return false; }
        if (value.GetArrayLength() > MaxUnits) { reason = "capturedNativeIds exceeds the unit budget"; return false; }
        var list = new List<string>(value.GetArrayLength());
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String) { reason = "capturedNativeId must be a string"; return false; }
            string text = element.GetString();
            if (text == null || text.Length == 0 || text.Length > MaxNativeIdLength)
            { reason = "capturedNativeId must be 1.." + MaxNativeIdLength + " chars"; return false; }
            list.Add(text);
        }
        ids = list;
        reason = null;
        return true;
    }

    private static bool TryGetString(JsonElement value, out string text)
    {
        if (value.ValueKind == JsonValueKind.String) { text = value.GetString(); return true; }
        text = null;
        return false;
    }

    private static bool TryGetInt32(JsonElement value, out int number)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number)) return true;
        number = 0;
        return false;
    }

    private static bool IsLowerHex64(string value)
    {
        if (value == null || value.Length != 64) return false;
        for (int i = 0; i < value.Length; i++)
        {
            if (!IsLowerHex(value[i])) return false;
        }
        return true;
    }

    private static bool IsLowerHex32(string value)
    {
        if (value == null || value.Length != 32) return false;
        for (int i = 0; i < value.Length; i++)
        {
            if (!IsLowerHex(value[i])) return false;
        }
        return true;
    }

    private static bool IsLowerHex(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
}
