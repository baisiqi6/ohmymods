using System.Text.Json.Nodes;
using KingdomEnhancedMod;

internal static class NinetyDurabilityCompatibility
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool ok, string name) { checks++; if (!ok) throw new Exception("ninety durability: " + name); }

        Check(HeavyShieldSaveSchema.Version == 5 && HeavyShieldSaveSchema.MaxDurability == 90
            && HeavyShieldSaveSchema.FortyMaxDurability == 40
            && HeavyShieldSaveSchema.FingerprintVersion == 3
            && HeavyShieldSaveSchema.Key == "KEM.HeavyShield.Campaigns.v1",
            "schema 5 keeps the authority key and fingerprint version");
        Check(new HeavyShieldSaveDocument().Version == 5
            && new HeavyShieldSavedClaim().Durability == 90,
            "fresh documents and claims default to ninety");

        // One-time proportional scaling, checked against the original version range first.
        foreach (var row in new[] { (4, 20, 45), (4, 1, 3), (4, 40, 90), (4, 0, 0),
            (1, 1, 30), (1, 2, 60), (1, 3, 90), (2, 1, 23), (2, 4, 90), (3, 1, 23), (5, 90, 90) })
        {
            var input = Fixture(row.Item1, row.Item2);
            string raw = input.ToJsonString();
            Check(HeavyShieldSaveCodec.TryParse(raw, out var parsed, out _), "source accepted " + row);
            var claim = parsed.Campaigns[0].Islands[0].Claims[0];
            Check(parsed.Version == 5 && claim.Durability == row.Item3, "scaled exactly once " + row);
            Check(HeavyShieldSaveCodec.TrySerialize(parsed, out var saved, out _), "serialized v5 " + row);
            Check(HeavyShieldSaveCodec.TryParse(saved, out var again, out _)
                && again.Campaigns[0].Islands[0].Claims[0].Durability == row.Item3, "reparse never amplifies " + row);
        }
        foreach (var row in new[] { (4, 41), (4, -1), (5, 91), (2, 5), (1, 4) })
        {
            Check(!HeavyShieldSaveCodec.TryParse(Fixture(row.Item1, row.Item2).ToJsonString(), out _, out _),
                "outside original range rejects " + row);
        }
        Check(HeavyShieldSaveCodec.TryParse(Fixture(4, 0).ToJsonString()
                .Replace("\"pendingBreak\":true", "\"pendingBreak\":false"), out var zeroNoPending, out _)
            && zeroNoPending.Campaigns[0].Islands[0].Claims[0].Durability == 0
            && !zeroNoPending.Campaigns[0].Islands[0].Claims[0].PendingBreak
            && !new HeavyShieldBlockPolicy().Restore(0, false, false),
            "zero without pending survives the codec but never restores into a live policy");

        // A quarantined row migrates with every other claim and keeps its flags.
        var quarantine = Fixture(4, 20);
        quarantine["campaigns"][0]["islands"][0]["claims"][0]["pendingBreak"] = true;
        quarantine["campaigns"][0]["islands"][0]["claims"][0]["retirementUnknown"] = true;
        Check(HeavyShieldSaveCodec.TryParse(quarantine.ToJsonString(), out var quarantined, out _)
            && quarantined.Version == 5
            && quarantined.Campaigns[0].Islands[0].Claims[0].Durability == 45
            && quarantined.Campaigns[0].Islands[0].Claims[0].PendingBreak
            && quarantined.Campaigns[0].Islands[0].Claims[0].RetirementUnknown,
            "quarantined v4 claim scales once and keeps unknown/pending flags");

        // Wear bands are the same percentages of the new maximum.
        foreach (var row in new[] { (-1, -1), (0, 3), (22, 3), (23, 2), (45, 2),
            (46, 1), (67, 1), (68, 0), (90, 0), (91, -1) })
            Check(HeavyShieldDurabilityMigration.WearBand(row.Item1) == row.Item2, "wear band " + row);

        var worn = new HeavyShieldBlockPolicy();
        Check(worn.Restore(20, false, false), "worn v5 career restores");
        Check(worn.TryRepair(20) && worn.Durability == 90, "repair restores the unified maximum");
        Check(!worn.TryRepair(90) && worn.Durability == 90, "full shield cannot repair again");

        var broken = new HeavyShieldBlockPolicy();
        Check(broken.Restore(0, true, false) && broken.Durability == 0 && broken.PendingBreak,
            "explicit zero remains a pending break");
        Check(!new HeavyShieldBlockPolicy().Restore(90, true, false),
            "full durability with pending break remains invalid");
        Console.WriteLine($"PASS schema5 ninety durability compatibility: {checks} checks");
    }

    private static JsonObject Fixture(int version, int durability) => new()
    {
        ["version"] = version,
        ["campaigns"] = new JsonArray(new JsonObject
        {
            ["slot"] = 0, ["guid"] = "11111111-1111-4111-8111-111111111111",
            ["moldReceipt"] = "22222222-2222-4222-8222-222222222222",
            ["islands"] = new JsonArray(new JsonObject
            {
                ["challenge"] = 0, ["land"] = 1, ["snapshotHash"] = new string('a', 64), ["hashKind"] = 1,
                ["claims"] = new JsonArray(new JsonObject
                {
                    ["receipt"] = "33333333-3333-4333-8333-333333333333", ["side"] = 0,
                    ["phase"] = 2, ["nativeId"] = "known-original-id", ["durability"] = durability,
                    ["pendingBreak"] = durability == 0, ["retirementUnknown"] = false
                })
            })
        })
    };
}
