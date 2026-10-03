using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KingdomEnhancedMod;

// Pure native-Prefs payload. The native string is the only durable authority for
// HeavyShield gems and paid careers. A live receipt is not a persisted receipt.
internal static class HeavyShieldSaveSchema
{
    internal const string Key = "KEM.HeavyShield.Campaigns.v1";
    internal const int LegacyVersion = 1;
    internal const int Version = 2;
    internal const int MaxCampaigns = 32;
    internal const int MaxIslands = 128;
    internal const int MaxClaimsPerIsland = 4;
    internal const int MaxDocumentBytes = 256 * 1024;
    internal const int MaxNativeIdLength = 512;
    internal const int LegacyMaxDurability = 3;
    internal const int MaxDurability = 4;
}

internal sealed class HeavyShieldSaveDocument
{
    public int Version { get; set; } = HeavyShieldSaveSchema.Version;
    public List<HeavyShieldSavedCampaign> Campaigns { get; set; } = new();
}

internal sealed class HeavyShieldSavedCampaign
{
    public int Slot { get; set; }
    public string Guid { get; set; }
    public string MoldReceipt { get; set; }
    public string LeftExtraReceipt { get; set; }
    public string RightExtraReceipt { get; set; }
    public List<HeavyShieldSavedIsland> Islands { get; set; } = new();
}

internal sealed class HeavyShieldSavedIsland
{
    public int Challenge { get; set; }
    public int Land { get; set; }
    public string SnapshotHash { get; set; }
    public List<HeavyShieldSavedClaim> Claims { get; set; } = new();
}

internal enum HeavyShieldSavedClaimPhase : byte
{
    PaidTool = 1, Soldier = 2, PaidToolUnresolved = 3, SoldierUnresolved = 4
}

internal sealed class HeavyShieldSavedClaim
{
    public string Receipt { get; set; }
    public HeavyShieldQuota.Side Side { get; set; }
    public HeavyShieldSavedClaimPhase Phase { get; set; }
    // Empty native ID is allowed only for a quarantined claim; never for a
    // normally restored paid tool or soldier.
    public string NativeId { get; set; }
    private int _durability = HeavyShieldSaveSchema.MaxDurability;
    // Presence belongs to this freshly deserialized object, never native Prefs.
    // V1 omitted this field with a default of three; V2 must carry it explicitly.
    internal bool DurabilitySpecified { get; private set; }
    public int Durability
    {
        get => _durability;
        set { _durability = value; DurabilitySpecified = true; }
    }
    public bool PendingBreak { get; set; }
    public bool RetirementUnknown { get; set; }
}

internal enum HeavyShieldNativeKeyRead : byte
{
    Failed, ConfirmedMissing, Present
}

internal enum HeavyShieldSnapshotResolution : byte
{
    Unknown, ConfirmedFresh, Exact
}

internal static class HeavyShieldSaveCaptureGate
{
    internal static bool CanStage(bool normalReturn, bool sceneExactAtBegin,
        bool markerSeen, bool conflict, bool sameSceneAtEnd, bool exactIsland)
        => normalReturn && sceneExactAtBegin && markerSeen && !conflict
           && sameSceneAtEnd && exactIsland;
}

internal static class HeavyShieldKeyGate
{
    // Native CanPay may ask before Started. A never advertises a charge until
    // the first-key SetString/readback has succeeded in a prior TickBinding.
    internal static bool CanAdvertise(bool contextReady, bool keyReady)
        => contextReady && keyReady;
}

// Exact TryCreateOrFind scope mask. An inner ordinary row temporarily masks an
// outer paid row; exiting restores the prior proof. The native adapter uses this
// same core, so synthetic tests exercise the actual nesting rule.
internal sealed class HeavyShieldLoadRowMask
{
    internal sealed class Scope
    {
        internal Scope Previous;
        internal bool Owned;
        internal bool Closed;
    }

    private Scope _top;
    internal bool Active => _top != null && _top.Owned && !_top.Closed;
    internal Scope Begin()
    {
        var scope = new Scope { Previous = _top };
        _top = scope;
        return scope;
    }
    internal bool MarkOwned(Scope scope)
    {
        if (scope == null || scope.Closed || !ReferenceEquals(_top, scope)) return false;
        scope.Owned = true;
        return true;
    }
    internal void End(Scope scope)
    {
        if (scope == null || scope.Closed) return;
        scope.Closed = true;
        if (ReferenceEquals(_top, scope)) _top = scope.Previous;
    }
}

internal static class HeavyShieldSaveCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static string NewGuid() => Guid.NewGuid().ToString("D");

    internal static string SnapshotHash(string campaignGuid, int challenge, int land, string rawIslandJson)
    {
        if (!CanonicalGuid(campaignGuid, out _) || challenge < 0 || land < 0
            || string.IsNullOrEmpty(rawIslandJson)) return null;
        string material = campaignGuid + ":" + challenge + ":" + land + ":" + rawIslandJson;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    // Missing is a trusted observation only after a successful native key read.
    // A known campaign/island record with an empty claim list still requires an
    // exact snapshot hash. Mismatch never becomes an empty fresh island.
    internal static HeavyShieldSnapshotResolution Resolve(
        HeavyShieldNativeKeyRead keyRead, HeavyShieldSaveDocument document,
        int slot, int challenge, int land, string snapshotHash,
        out HeavyShieldSavedCampaign campaign, out HeavyShieldSavedIsland island)
    {
        campaign = null;
        island = null;
        if (keyRead == HeavyShieldNativeKeyRead.Failed) return HeavyShieldSnapshotResolution.Unknown;
        if (keyRead == HeavyShieldNativeKeyRead.ConfirmedMissing)
            return document == null ? HeavyShieldSnapshotResolution.ConfirmedFresh : HeavyShieldSnapshotResolution.Unknown;
        if (keyRead != HeavyShieldNativeKeyRead.Present || document == null
            || !TryValidate(document, out _) || !ValidHash(snapshotHash))
            return HeavyShieldSnapshotResolution.Unknown;
        foreach (var row in document.Campaigns)
            if (row.Slot == slot) { campaign = row; break; }
        if (campaign == null) return HeavyShieldSnapshotResolution.Unknown;
        foreach (var row in campaign.Islands)
            if (row.Challenge == challenge && row.Land == land)
            { island = row; break; }
        if (island == null) return HeavyShieldSnapshotResolution.Unknown;
        return string.Equals(island.SnapshotHash, snapshotHash, StringComparison.Ordinal)
            ? HeavyShieldSnapshotResolution.Exact : HeavyShieldSnapshotResolution.Unknown;
    }

    internal static bool TryParse(string json, out HeavyShieldSaveDocument document, out string reason)
    {
        document = null;
        reason = null;
        if (string.IsNullOrWhiteSpace(json)) { reason = "empty"; return false; }
        if (Encoding.UTF8.GetByteCount(json) > HeavyShieldSaveSchema.MaxDocumentBytes)
        { reason = "size"; return false; }
        try
        {
            using var raw = JsonDocument.Parse(json);
            if (raw.RootElement.ValueKind != JsonValueKind.Object)
            { reason = "document"; return false; }
            int sourceVersion = HeavyShieldSaveSchema.LegacyVersion;
            if (raw.RootElement.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out sourceVersion)))
            { reason = "document"; return false; }
            if (sourceVersion != HeavyShieldSaveSchema.LegacyVersion
                && sourceVersion != HeavyShieldSaveSchema.Version)
            { reason = "document"; return false; }
            document = JsonSerializer.Deserialize<HeavyShieldSaveDocument>(json, Options);
            document.Version = sourceVersion;
            if (document.Campaigns != null)
                foreach (var campaign in document.Campaigns)
                    if (campaign?.Islands != null)
                        foreach (var island in campaign.Islands)
                            if (island?.Claims != null)
                                foreach (var claim in island.Claims)
                                    if (claim != null && !claim.DurabilitySpecified)
                                    {
                                        if (sourceVersion == HeavyShieldSaveSchema.Version)
                                        { document = null; reason = "claim"; return false; }
                                        claim.Durability = HeavyShieldSaveSchema.LegacyMaxDurability;
                                    }
        }
        catch { document = null; reason = "json"; return false; }
        if (TryValidate(document, out reason))
        {
            // Validate against the source version before upgrading only this
            // isolated object. The caller still owns the original native string.
            document.Version = HeavyShieldSaveSchema.Version;
            return true;
        }
        document = null;
        return false;
    }

    internal static bool TrySerialize(HeavyShieldSaveDocument document, out string json, out string reason)
    {
        json = null;
        if (!TryValidate(document, out reason)) return false;
        try
        {
            string serialized = JsonSerializer.Serialize(document, Options);
            if (Encoding.UTF8.GetByteCount(serialized) > HeavyShieldSaveSchema.MaxDocumentBytes)
            { reason = "size"; return false; }
            json = serialized;
            return true;
        }
        catch { reason = "serialize"; return false; }
    }

    // Native campaign pointers, captured before mutation, decide survivor
    // membership. Slots merely describe their current order; a new object at
    // an old slot has a new GUID and cannot inherit the deleted record.
    internal static bool TryReconcileStaged(HeavyShieldSaveDocument staged,
        IReadOnlyDictionary<string, int> survivors, out HeavyShieldSaveDocument result)
    {
        result = null;
        if (survivors == null || !TrySerialize(staged, out string json, out _)
            || !TryParse(json, out result, out _)) return false;
        result.Campaigns.RemoveAll(x => !survivors.ContainsKey(x.Guid));
        foreach (var row in result.Campaigns) row.Slot = survivors[row.Guid];
        if (TryValidate(result, out _)) return true;
        result = null;
        return false;
    }

    internal static bool TryValidate(HeavyShieldSaveDocument document, out string reason)
    {
        reason = null;
        if (document == null || (document.Version != HeavyShieldSaveSchema.LegacyVersion
                && document.Version != HeavyShieldSaveSchema.Version)
            || document.Campaigns == null || document.Campaigns.Count > HeavyShieldSaveSchema.MaxCampaigns)
        { reason = "document"; return false; }
        int maxDurability = document.Version == HeavyShieldSaveSchema.LegacyVersion
            ? HeavyShieldSaveSchema.LegacyMaxDurability : HeavyShieldSaveSchema.MaxDurability;
        var slots = new HashSet<int>();
        var campaigns = new HashSet<string>(StringComparer.Ordinal);
        var receipts = new HashSet<Guid>();
        foreach (var campaign in document.Campaigns)
        {
            if (campaign == null || campaign.Slot < 0 || !slots.Add(campaign.Slot)
                || !CanonicalGuid(campaign.Guid, out _) || !campaigns.Add(campaign.Guid)
                || campaign.Islands == null || campaign.Islands.Count > HeavyShieldSaveSchema.MaxIslands)
            { reason = "campaign"; return false; }
            if (!OptionalReceipt(campaign.MoldReceipt, receipts)
                || !OptionalReceipt(campaign.LeftExtraReceipt, receipts)
                || !OptionalReceipt(campaign.RightExtraReceipt, receipts)
                || ((campaign.LeftExtraReceipt != null || campaign.RightExtraReceipt != null)
                    && campaign.MoldReceipt == null))
            { reason = "entitlement"; return false; }
            var islands = new HashSet<(int, int)>();
            foreach (var island in campaign.Islands)
            {
                if (island == null || island.Challenge < 0 || island.Land < 0
                    || !islands.Add((island.Challenge, island.Land))
                    || !ValidHash(island.SnapshotHash) || island.Claims == null
                    || island.Claims.Count > HeavyShieldSaveSchema.MaxClaimsPerIsland)
                { reason = "island"; return false; }
                int left = 0, right = 0, shop = 0;
                foreach (var claim in island.Claims)
                {
                    if (claim == null || !CanonicalGuid(claim.Receipt, out var receipt)
                        || !receipts.Add(receipt) || claim.Side > HeavyShieldQuota.Side.Right
                        || claim.Phase < HeavyShieldSavedClaimPhase.PaidTool
                        || claim.Phase > HeavyShieldSavedClaimPhase.SoldierUnresolved
                        || claim.Durability < 0 || claim.Durability > maxDurability
                        || claim.NativeId == null || claim.NativeId.Length > HeavyShieldSaveSchema.MaxNativeIdLength
                        || ((claim.Phase == HeavyShieldSavedClaimPhase.PaidTool
                             || claim.Phase == HeavyShieldSavedClaimPhase.Soldier)
                            && claim.NativeId.Length == 0))
                    { reason = "claim"; return false; }
                    if (claim.Side == HeavyShieldQuota.Side.Left) left++; else right++;
                    if (claim.Phase == HeavyShieldSavedClaimPhase.PaidTool
                        || claim.Phase == HeavyShieldSavedClaimPhase.PaidToolUnresolved) shop++;
                }
                if (left > (campaign.LeftExtraReceipt == null ? 1 : 2)
                    || right > (campaign.RightExtraReceipt == null ? 1 : 2)
                    || shop > 1 || (island.Claims.Count > 0 && campaign.MoldReceipt == null))
                { reason = "quota"; return false; }
            }
        }
        return true;
    }

    private static bool OptionalReceipt(string value, HashSet<Guid> seen)
        => value == null || (CanonicalGuid(value, out var receipt) && seen.Add(receipt));

    private static bool CanonicalGuid(string value, out Guid guid)
        => Guid.TryParseExact(value, "D", out guid)
           && value == guid.ToString("D");

    private static bool ValidHash(string value)
    {
        if (value == null || value.Length != 64) return false;
        foreach (char c in value)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        return true;
    }
}
