using System.Text.Json.Nodes;
using KingdomEnhancedMod;

internal static class FortyDurabilityCompatibility
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool ok, string name) { checks++; if (!ok) throw new Exception("v1-v4 compatibility: " + name); }
        // Literal budgets distinguish proportional migration from refill and repeated amplification.
        // Historical v4 fixtures must keep their original 0..40 range and scale once into 0..90.
        foreach (var row in new[] { (1, 0, 0), (1, 1, 30), (1, 2, 60), (1, 3, 90),
            (2, 0, 0), (2, 1, 23), (2, 2, 45), (2, 3, 68), (2, 4, 90),
            (3, 0, 0), (3, 1, 23), (3, 2, 45), (3, 3, 68), (3, 4, 90),
            (4, 0, 0), (4, 1, 3), (4, 2, 5), (4, 20, 45), (4, 39, 88), (4, 40, 90) })
        {
            foreach (int kind in row.Item1 >= 3 ? new[] { 1, 2 } : new[] { 1 })
            {
                var input = Fixture(row.Item1, row.Item2, kind);
                string raw = input.ToJsonString();
                Check(HeavyShieldSaveCodec.TryParse(raw, out var parsed, out _), "accept source " + row);
                var claim = parsed.Campaigns[0].Islands[0].Claims[0];
                Check(parsed.Version == HeavyShieldSaveSchema.Version && claim.Durability == row.Item3, "scale once " + row);
                var expected = JsonNode.Parse(raw); expected["version"] = HeavyShieldSaveSchema.Version;
                expected["campaigns"][0]["islands"][0]["claims"][0]["durability"] = row.Item3;
                Check(HeavyShieldSaveCodec.TrySerialize(parsed, out var serialized, out _)
                    && JsonNode.DeepEquals(expected, JsonNode.Parse(serialized)), "all other fields survive " + row);
                Check(HeavyShieldSaveCodec.TryParse(serialized, out var again, out _)
                    && again.Campaigns[0].Islands[0].Claims[0].Durability == row.Item3, "no second scaling " + row);
            }
        }
        foreach (int version in new[] { 1, 2, 3, 4, 5 })
        {
            var tooHigh = Fixture(version, version == 1 ? 4 : version < 4 ? 5 : version == 4 ? 41 : 91, 1);
            Check(!HeavyShieldSaveCodec.TryParse(tooHigh.ToJsonString(), out _, out _), "reject source overflow " + version);
            var missing = Fixture(version, 2, 1);
            missing["campaigns"][0]["islands"][0]["claims"][0].AsObject().Remove("durability");
            bool ok = HeavyShieldSaveCodec.TryParse(missing.ToJsonString(), out var parsed, out _);
            Check(version == 1 ? ok && parsed.Campaigns[0].Islands[0].Claims[0].Durability == 90 : !ok,
                "only v1 omitted wear has legacy full default " + version);
        }
        foreach (int version in new[] { 3, 4, 5 })
        {
            var missingKind = Fixture(version, 2, 2);
            missingKind["campaigns"][0]["islands"][0].AsObject().Remove("hashKind");
            Check(!HeavyShieldSaveCodec.TryParse(missingKind.ToJsonString(), out _, out _), "v3+ requires fingerprint kind");
            string duplicateKind = Fixture(version, 2, 2).ToJsonString().Replace("\"hashKind\":2", "\"hashKind\":2,\"hashKind\":1");
            Check(!HeavyShieldSaveCodec.TryParse(duplicateKind, out _, out _), "duplicate fingerprint kind rejected");
        }
        var anomalous = Fixture(3, 2, 2);
        anomalous["campaigns"][0]["islands"][0]["claims"][0]["pendingBreak"] = true;
        anomalous["campaigns"][0]["islands"][0]["claims"][0]["retirementUnknown"] = true;
        Check(HeavyShieldSaveCodec.TryParse(anomalous.ToJsonString(), out var quarantine, out _), "preserve readable historic quarantine");
        var q = quarantine.Campaigns[0].Islands[0].Claims[0];
        Check(q.Durability == 45 && q.PendingBreak && q.RetirementUnknown
            && !new HeavyShieldBlockPolicy().Restore(q.Durability, q.PendingBreak, q.RetirementUnknown), "flags never washed into a live career");
        Console.WriteLine($"PASS v1-v4 proportional durability and fingerprint compatibility: {checks} checks");
    }

    private static JsonObject Fixture(int version, int durability, int kind) => new()
    {
        ["version"] = version,
        ["campaigns"] = new JsonArray(new JsonObject
        {
            ["slot"] = 0, ["guid"] = "11111111-1111-4111-8111-111111111111",
            ["moldReceipt"] = "22222222-2222-4222-8222-222222222222",
            ["islands"] = new JsonArray(new JsonObject
            {
                ["challenge"] = 0, ["land"] = 1, ["snapshotHash"] = new string('a', 64), ["hashKind"] = kind,
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
