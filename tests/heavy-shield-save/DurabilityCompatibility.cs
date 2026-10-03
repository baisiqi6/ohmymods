using System.Text.Json.Nodes;
using KingdomEnhancedMod;

internal static class DurabilityCompatibility
{
    private static int checks;
    private static void Check(bool value, string name)
    { checks++; if (!value) throw new Exception("durability compatibility: " + name); }

    internal static void Run()
    {
        Check(HeavyShieldSaveSchema.Key == "KEM.HeavyShield.Campaigns.v1"
            && new HeavyShieldSaveDocument().Version == 2
            && HeavyShieldBlockPolicy.InitialDurability == 4
            && HeavyShieldSaveSchema.MaxDurability == 4, "new schema keeps original authority key");
        var legacy = Fixture();
        Check(HeavyShieldSaveCodec.TrySerialize(legacy, out var raw, out _), "real v1 fixture serializes");
        Check(legacy.Version == 1 && legacy.Campaigns[0].Islands[0].Claims[0].Durability == 3,
            "serialize preserves caller version and durability");
        Check(HeavyShieldSaveCodec.TryParse(raw, out var loaded, out _), "real v1 fixture parses");
        Check(loaded.Version == 2 && !ReferenceEquals(legacy, loaded), "upgrade belongs only to parsed object");
        Check(HeavyShieldSaveCodec.TrySerialize(loaded, out var upgraded, out _)
            && SameExceptVersion(raw, upgraded), "all campaigns/islands/receipts/native IDs/flags survive upgrade");
        Check(HeavyShieldSaveCodec.TryParse(upgraded, out var reloaded, out _)
            && HeavyShieldSaveCodec.TrySerialize(reloaded, out var second, out _)
            && second == upgraded, "v2 roundtrip is stable without further migration");
        Check(legacy.Version == 1, "parse and upgrade do not mutate source document");

        foreach (var claim in loaded.Campaigns[0].Islands[0].Claims)
        {
            var policy = new HeavyShieldBlockPolicy();
            Check(policy.Restore(claim.Durability, claim.PendingBreak, claim.RetirementUnknown),
                $"v1 remaining {claim.Durability} restores into actual policy");
            Check(policy.Durability == claim.Durability && !policy.Activate(),
                $"v1 remaining {claim.Durability} cannot become fresh four");
            if (claim.Durability > 0)
                for (int remaining = claim.Durability - 1; remaining >= 0; remaining--)
                    Check(policy.EvaluateHit(Front()) == (remaining == 0
                            ? HeavyShieldBlockPolicy.HitResult.ShieldBroken : HeavyShieldBlockPolicy.HitResult.Blocked)
                        && policy.Durability == remaining, "old remaining hit budget is exact");
            Check(policy.Durability == 0 && policy.PendingBreak && !policy.TryBeginBash(0),
                "old zero cannot return to combat");
            Check(policy.TryBeginDemote(out var lease) && !policy.TryBeginDemote(out _)
                && policy.ConfirmDemote(lease), "old pending gets at most one native retirement lease");
        }
        var unknown = loaded.Campaigns[0].Islands[1].Claims[1];
        var unknownPolicy = new HeavyShieldBlockPolicy();
        Check(unknownPolicy.Restore(unknown.Durability, unknown.PendingBreak, unknown.RetirementUnknown)
            && unknownPolicy.Durability == 0 && unknownPolicy.RetirementUnknown
            && !unknownPolicy.TryBeginDemote(out _) && !unknownPolicy.Activate()
            && unknownPolicy.EvaluateHit(Front()) == HeavyShieldBlockPolicy.HitResult.PassThrough,
            "v1 zero unknown retirement cannot retry or resurrect");
        Check(loaded.Campaigns[0].Islands[1].Claims[0].Durability == 3,
            "old paid tool remains three before pickup");

        // V1 allowed inconsistent pending flags. Codec compatibility preserves
        // them; it must not invent a repair or tighten unrelated legacy rules.
        var unusual = legacy.Campaigns[1].Islands[0].Claims[0];
        unusual.Durability = 1; unusual.PendingBreak = unusual.RetirementUnknown = true;
        Check(HeavyShieldSaveCodec.TrySerialize(legacy, out var unusualRaw, out _)
            && HeavyShieldSaveCodec.TryParse(unusualRaw, out var unusualLoaded, out _)
            && unusualLoaded.Campaigns[1].Islands[0].Claims[0].Durability == 1
            && unusualLoaded.Campaigns[1].Islands[0].Claims[0].PendingBreak
            && unusualLoaded.Campaigns[1].Islands[0].Claims[0].RetirementUnknown,
            "legacy anomalous pending state is retained without repair");

        var omitted = JsonNode.Parse(raw).AsObject();
        omitted.Remove("version");
        foreach (var campaign in omitted["campaigns"].AsArray())
            foreach (var island in campaign["islands"].AsArray())
                foreach (var claim in island["claims"].AsArray()) claim.AsObject().Remove("durability");
        Check(HeavyShieldSaveCodec.TryParse(omitted.ToJsonString(), out var defaulted, out _)
            && defaulted.Version == 2
            && defaulted.Campaigns.SelectMany(x => x.Islands).SelectMany(x => x.Claims)
                .All(x => x.Durability == 3), "missing version/durability use original v1 default three");
        Check(HeavyShieldSaveCodec.TrySerialize(defaulted, out var defaultedRaw, out _)
            && JsonNode.Parse(defaultedRaw)["campaigns"][0]["islands"][0]["claims"][0]["durability"].GetValue<int>() == 3,
            "old omitted durability becomes explicit three when later serialized");
        var explicitV1 = JsonNode.Parse(raw).AsObject();
        explicitV1["campaigns"][0]["islands"][0]["claims"][0].AsObject().Remove("durability");
        Check(HeavyShieldSaveCodec.TryParse(explicitV1.ToJsonString(), out var missingClaim, out _)
            && missingClaim.Campaigns[0].Islands[0].Claims[0].Durability == 3,
            "explicit v1 with missing claim durability defaults three");
        explicitV1["version"] = 2;
        Check(!HeavyShieldSaveCodec.TryParse(explicitV1.ToJsonString(), out var absentV2, out _)
            && absentV2 == null, "v2 claim omission is rejected instead of refilled");
        var inactiveOmission = JsonNode.Parse(raw).AsObject(); inactiveOmission["version"] = 2;
        inactiveOmission["campaigns"][1]["islands"][0]["claims"][0].AsObject().Remove("durability");
        Check(!HeavyShieldSaveCodec.TryParse(inactiveOmission.ToJsonString(), out _, out _),
            "v2 explicit durability is required even in inactive campaign unresolved claims");
        var defaultedUnknown = defaulted.Campaigns[0].Islands[1].Claims[1];
        var omittedUnknownPolicy = new HeavyShieldBlockPolicy();
        Check(defaultedUnknown.PendingBreak && defaultedUnknown.RetirementUnknown
            && !omittedUnknownPolicy.Restore(defaultedUnknown.Durability,
                defaultedUnknown.PendingBreak, defaultedUnknown.RetirementUnknown)
            && omittedUnknownPolicy.State != HeavyShieldBlockPolicy.LifeState.Guarding,
            "old omitted durability never clears unknown flags to activate a broken career");
        var badVersion = JsonNode.Parse(raw).AsObject();
        foreach (int version in new[] { -1, 0, 3, 4, int.MaxValue })
        {
            badVersion["version"] = version;
            Check(!HeavyShieldSaveCodec.TryParse(badVersion.ToJsonString(), out var bad, out _)
                && bad == null, $"unknown version {version} rejected");
        }
        badVersion["version"] = "2";
        Check(!HeavyShieldSaveCodec.TryParse(badVersion.ToJsonString(), out _, out _), "string version rejected");
        badVersion["version"] = null;
        Check(!HeavyShieldSaveCodec.TryParse(badVersion.ToJsonString(), out _, out _), "null version rejected");
        var badDurability = JsonNode.Parse(raw).AsObject();
        badDurability["campaigns"][0]["islands"][0]["claims"][0]["durability"] = 4;
        Check(!HeavyShieldSaveCodec.TryParse(badDurability.ToJsonString(), out var overLegacy, out _)
            && overLegacy == null, "v1 explicit four rejected before upgrade");
        badDurability.Remove("version");
        Check(!HeavyShieldSaveCodec.TryParse(badDurability.ToJsonString(), out _, out _),
            "missing version retains old upper bound three");
        badDurability["version"] = 2;
        Check(HeavyShieldSaveCodec.TryParse(badDurability.ToJsonString(), out var validFour, out _)
            && validFour.Campaigns[0].Islands[0].Claims[0].Durability == 4, "v2 explicit four accepted");
        badDurability["campaigns"][0]["islands"][0]["claims"][0]["durability"] = 5;
        Check(!HeavyShieldSaveCodec.TryParse(badDurability.ToJsonString(), out _, out _), "v2 five rejected");
        badDurability["campaigns"][0]["islands"][0]["claims"][0]["durability"] = -1;
        Check(!HeavyShieldSaveCodec.TryParse(badDurability.ToJsonString(), out _, out _), "negative durability rejected");
        legacy.Campaigns[0].Islands[0].Claims[0].Durability = 4;
        Check(!HeavyShieldSaveCodec.TrySerialize(legacy, out _, out _)
            && legacy.Version == 1 && legacy.Campaigns[0].Islands[0].Claims[0].Durability == 4,
            "invalid v1 serialize neither upgrades nor clamps caller");

        NewFourRoundtrips();
        Console.WriteLine($"PASS heavy-shield v1/v2 codec and production policy compatibility: {checks} checks");
    }

    private static void NewFourRoundtrips()
    {
        var doc = Fixture(); doc.Version = 2;
        var claim = doc.Campaigns[0].Islands[0].Claims[0];
        claim.Durability = 4;
        var policy = new HeavyShieldBlockPolicy();
        Check(policy.Activate() && policy.Durability == 4, "fresh purchase has four durability");
        for (int remaining = 4; remaining >= 0; remaining--)
        {
            claim.Durability = policy.Durability;
            claim.PendingBreak = policy.PendingBreak;
            claim.RetirementUnknown = policy.RetirementUnknown;
            Check(HeavyShieldSaveCodec.TrySerialize(doc, out var raw, out _)
                && HeavyShieldSaveCodec.TryParse(raw, out doc, out _), $"v2 remaining {remaining} roundtrip");
            claim = doc.Campaigns[0].Islands[0].Claims[0];
            var restored = new HeavyShieldBlockPolicy();
            Check(restored.Restore(claim.Durability, claim.PendingBreak, claim.RetirementUnknown)
                && restored.Durability == remaining && !restored.Activate(), "restore preserves each new wear stage");
            policy = restored;
            if (remaining > 0)
            {
                Check(policy.TryBeginBash(0), "each positive stage can fight");
                policy.EndBash();
                Check(policy.EvaluateHit(Front()) == (remaining == 1
                    ? HeavyShieldBlockPolicy.HitResult.ShieldBroken : HeavyShieldBlockPolicy.HitResult.Blocked),
                    "one frontal hit spends one point");
            }
        }
        Check(policy.TryBeginDemote(out var lease) && !policy.TryBeginDemote(out _), "final break opens one demotion");
        claim.RetirementUnknown = policy.RetirementUnknown;
        Check(HeavyShieldSaveCodec.TrySerialize(doc, out var uncertainRaw, out _), "uncertain native call serializes");
        Check(HeavyShieldSaveCodec.TryParse(uncertainRaw, out var uncertain, out _), "uncertain native call parses");
        claim = uncertain.Campaigns[0].Islands[0].Claims[0];
        var loadedUnknown = new HeavyShieldBlockPolicy();
        Check(loadedUnknown.Restore(claim.Durability, claim.PendingBreak, claim.RetirementUnknown)
            && !loadedUnknown.TryBeginDemote(out _) && !loadedUnknown.Activate(), "loaded final unknown never repeats native retirement");
        Check(policy.ConfirmDemote(lease) && !policy.TryBeginDemote(out _), "verified replacement confirms exactly once");
    }

    private static HeavyShieldBlockPolicy.Hit Front() => new(true, true, 1, 0, 1);
    private static bool SameExceptVersion(string before, string after)
    {
        var expected = JsonNode.Parse(before); expected["version"] = 2;
        return JsonNode.DeepEquals(expected, JsonNode.Parse(after));
    }
    private static HeavyShieldSavedClaim Claim(int durability, HeavyShieldQuota.Side side,
        HeavyShieldSavedClaimPhase phase = HeavyShieldSavedClaimPhase.Soldier, bool unknown = false)
        => new() { Receipt = HeavyShieldSaveCodec.NewGuid(), Side = side, Phase = phase,
            NativeId = phase == HeavyShieldSavedClaimPhase.SoldierUnresolved ? "" : $"native-{HeavyShieldSaveCodec.NewGuid()}",
            Durability = durability, PendingBreak = durability == 0, RetirementUnknown = unknown };
    private static HeavyShieldSaveDocument Fixture()
    {
        var doc = new HeavyShieldSaveDocument { Version = 1 };
        for (int slot = 0; slot < 2; slot++)
        {
            var campaign = new HeavyShieldSavedCampaign { Slot = slot, Guid = HeavyShieldSaveCodec.NewGuid(),
                MoldReceipt = HeavyShieldSaveCodec.NewGuid(), LeftExtraReceipt = HeavyShieldSaveCodec.NewGuid(),
                RightExtraReceipt = HeavyShieldSaveCodec.NewGuid() };
            for (int land = 0; land < 2; land++)
                campaign.Islands.Add(new HeavyShieldSavedIsland { Challenge = slot, Land = land,
                    SnapshotHash = HeavyShieldSaveCodec.SnapshotHash(campaign.Guid, slot, land, $"{{\"slot\":{slot},\"land\":{land}}}") });
            doc.Campaigns.Add(campaign);
        }
        var claims = doc.Campaigns[0].Islands[0].Claims;
        claims.Add(Claim(3, HeavyShieldQuota.Side.Left)); claims.Add(Claim(2, HeavyShieldQuota.Side.Right));
        claims.Add(Claim(1, HeavyShieldQuota.Side.Left)); claims.Add(Claim(0, HeavyShieldQuota.Side.Right));
        doc.Campaigns[0].Islands[1].Claims.Add(Claim(3, HeavyShieldQuota.Side.Left, HeavyShieldSavedClaimPhase.PaidTool));
        doc.Campaigns[0].Islands[1].Claims.Add(Claim(0, HeavyShieldQuota.Side.Right, unknown: true));
        doc.Campaigns[1].Islands[0].Claims.Add(Claim(1, HeavyShieldQuota.Side.Right, HeavyShieldSavedClaimPhase.SoldierUnresolved));
        return doc;
    }
}
