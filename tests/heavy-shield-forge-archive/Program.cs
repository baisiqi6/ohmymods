using KingdomEnhancedMod;

var cases = new (string Name, Action Run)[]
{
    ("one unlock across islands and restarts", OneUnlockAcrossIslands),
    ("separate campaign lineages", SeparateCampaigns),
    ("corrupt and future sidecars remain untouched", DamagedSidecars),
    ("oversized sidecar is fail closed", OversizedSidecar),
    ("unavailable storage never claims success", FailedSave),
    ("invalid lineage key refused", InvalidKeys),
    ("left and right seats persist independently", SideSeatsAcrossRestart),
    ("duplicate seat payment cannot open both sides", DuplicateSeatPayment),
    ("v1 mold record migrates on first seat purchase", V1Migration),
    ("invalid seat archive remains untouched", InvalidSeatArchive),
    ("seat write failure never grants a seat", SeatWriteFailure),
    ("one receipt cannot fund mold and seat", CrossPurchaseReceipt),
    ("v2 legacy records migrate without invented mold receipt", V2Migration),
    ("corrupt v3 mold receipts fail closed", InvalidMoldArchive),
};

foreach (var test in cases)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}
Console.WriteLine($"PASS {cases.Length} forge archive scenarios");

static void OneUnlockAcrossIslands()
{
    InTemporaryDirectory(path =>
    {
        const string lineage = "slot-a/campaign-17";
        var first = new HeavyShieldForgeArchive(path);
        Check(HeavyShieldForgeArchive.GemPrice == 4, "native price contract");
        Check(first.Lookup(lineage) == HeavyShieldForgeArchive.LookupResult.Locked, "new lineage locked");
        string moldReceipt = Guid.NewGuid().ToString("D");
        Check(first.RecordAfterCompletedPayment(lineage, moldReceipt) == HeavyShieldForgeArchive.RecordResult.Saved,
            "complete payment persists receipt");
        var reloaded = new HeavyShieldForgeArchive(path);
        Check(reloaded.Lookup(lineage) == HeavyShieldForgeArchive.LookupResult.Unlocked,
            "same lineage remains unlocked on another island and process lifetime");
        byte[] before = File.ReadAllBytes(path);
        Check(reloaded.RecordAfterCompletedPayment(lineage, moldReceipt)
            == HeavyShieldForgeArchive.RecordResult.SameReceiptAlreadySaved,
            "duplicate receipt is idempotent");
        Check(reloaded.RecordAfterCompletedPayment(lineage, Guid.NewGuid().ToString("D"))
            == HeavyShieldForgeArchive.RecordResult.AlreadyUnlocked,
            "distinct second mold charge must be refunded");
        Check(reloaded.TryGetMoldReceipt(lineage, out string? restoredMold)
            == HeavyShieldForgeArchive.LookupResult.Unlocked && restoredMold == moldReceipt,
            "mold receipt survives restart");
        Check(before.SequenceEqual(File.ReadAllBytes(path)), "duplicate does not rewrite sidecar");
    });
}

static void SeparateCampaigns()
{
    InTemporaryDirectory(path =>
    {
        var archive = new HeavyShieldForgeArchive(path);
        Check(archive.RecordAfterCompletedPayment("slot-a/campaign-17", Guid.NewGuid().ToString("D"))
            == HeavyShieldForgeArchive.RecordResult.Saved,
            "first campaign saved");
        Check(archive.Lookup("slot-a/campaign-18") == HeavyShieldForgeArchive.LookupResult.Locked,
            "new campaign is not granted old qualification");
        Check(archive.RecordAfterCompletedPayment("slot-a/campaign-18", Guid.NewGuid().ToString("D"))
            == HeavyShieldForgeArchive.RecordResult.Saved,
            "second campaign saved separately");
        var reloaded = new HeavyShieldForgeArchive(path);
        Check(reloaded.Lookup("slot-a/campaign-17") == HeavyShieldForgeArchive.LookupResult.Unlocked
            && reloaded.Lookup("slot-a/campaign-18") == HeavyShieldForgeArchive.LookupResult.Unlocked,
            "both persisted keys survive replacement");
    });
}

static void DamagedSidecars()
{
    InTemporaryDirectory(path =>
    {
        var archive = new HeavyShieldForgeArchive(path);
        foreach (var (json, expected) in new[]
        {
            ("{broken", HeavyShieldForgeArchive.LookupResult.Corrupt),
            ("{\"version\":4,\"unlocked\":[]}", HeavyShieldForgeArchive.LookupResult.Unsupported),
            ("{\"version\":1,\"version\":1,\"unlocked\":[]}", HeavyShieldForgeArchive.LookupResult.Corrupt),
            ("{\"version\":1,\"unlocked\":[\"key\",\"key\"]}", HeavyShieldForgeArchive.LookupResult.Corrupt),
        })
        {
            File.WriteAllText(path, json);
            byte[] before = File.ReadAllBytes(path);
            Check(archive.Lookup("key") == expected, "read status protects malformed/future schema");
            Check(archive.RecordAfterCompletedPayment("key", Guid.NewGuid().ToString("D"))
                == HeavyShieldForgeArchive.RecordResult.Unavailable,
                "bad existing archive refuses payment receipt");
            Check(before.SequenceEqual(File.ReadAllBytes(path)), "bad file is never overwritten");
        }
    });
}

static void FailedSave()
{
    InTemporaryDirectory(path =>
    {
        var archive = new HeavyShieldForgeArchive(path);
        // Simulate another writer. This must be reported as a failed save, so
        // the payment bridge can cancel/refund rather than granting a false unlock.
        using (var locked = new FileStream(path + ".lock", FileMode.OpenOrCreate,
                   FileAccess.ReadWrite, FileShare.None))
            Check(archive.RecordAfterCompletedPayment("campaign", Guid.NewGuid().ToString("D"))
                == HeavyShieldForgeArchive.RecordResult.SaveFailed,
                "lock contention is a failed save");
        Check(archive.Lookup("campaign") == HeavyShieldForgeArchive.LookupResult.Locked,
            "failed save did not unlock");
        Check(archive.RecordAfterCompletedPayment("campaign", Guid.NewGuid().ToString("D"))
            == HeavyShieldForgeArchive.RecordResult.Saved,
            "later attempt can succeed");
    });
}

static void OversizedSidecar()
{
    InTemporaryDirectory(path =>
    {
        byte[] payload = new byte[HeavyShieldForgeArchive.MaxBytes + 1];
        Array.Fill(payload, (byte)' ');
        File.WriteAllBytes(path, payload);
        var archive = new HeavyShieldForgeArchive(path);
        Check(archive.Lookup("campaign") == HeavyShieldForgeArchive.LookupResult.Corrupt,
            "bounded loader rejects oversized data");
        Check(archive.RecordAfterCompletedPayment("campaign", Guid.NewGuid().ToString("D"))
            == HeavyShieldForgeArchive.RecordResult.Unavailable,
            "oversized data cannot be replaced");
        Check(payload.SequenceEqual(File.ReadAllBytes(path)), "oversized file unchanged");
    });
}

static void InvalidKeys()
{
    InTemporaryDirectory(path =>
    {
        var archive = new HeavyShieldForgeArchive(path);
        foreach (string? key in new[] { null, "", "  ", "line\nbreak", new string('x', 513) })
        {
            Check(archive.Lookup(key!) == HeavyShieldForgeArchive.LookupResult.InvalidKey, "invalid read key");
            Check(archive.RecordAfterCompletedPayment(key!, Guid.NewGuid().ToString("D"))
                == HeavyShieldForgeArchive.RecordResult.InvalidKey,
                "invalid write key");
        }
        Check(!File.Exists(path), "invalid calls did not create sidecar");
        try { _ = new HeavyShieldForgeArchive("relative.json"); throw new Exception("relative path accepted"); }
        catch (ArgumentException) { }
    });
}

static void SideSeatsAcrossRestart()
{
    InTemporaryDirectory(path =>
    {
        const string lineage = "slot-a/campaign-17";
        var archive = new HeavyShieldForgeArchive(path);
        Check(HeavyShieldForgeArchive.ExtraSeatGemPrice == 2, "extra seat native price contract");
        Check(archive.LookupExtraSeat(lineage, HeavyShieldForgeArchive.Side.Left)
            == HeavyShieldForgeArchive.LookupResult.Locked, "left initially locked");
        string left = Guid.NewGuid().ToString("D"), right = Guid.NewGuid().ToString("D");
        Check(archive.RecordExtraSeatAfterCompletedPayment(lineage, HeavyShieldForgeArchive.Side.Left, left)
            == HeavyShieldForgeArchive.RecordResult.ForgeLocked, "no extra seat before mold");
        Check(archive.RecordAfterCompletedPayment(lineage, Guid.NewGuid().ToString("D"))
            == HeavyShieldForgeArchive.RecordResult.Saved,
            "mold unlock");
        Check(archive.RecordExtraSeatAfterCompletedPayment(lineage, HeavyShieldForgeArchive.Side.Left, left)
            == HeavyShieldForgeArchive.RecordResult.Saved, "left seat saved");
        var anotherIsland = new HeavyShieldForgeArchive(path);
        Check(anotherIsland.LookupExtraSeat(lineage, HeavyShieldForgeArchive.Side.Left)
            == HeavyShieldForgeArchive.LookupResult.Unlocked, "left survives restart");
        Check(anotherIsland.LookupExtraSeat(lineage, HeavyShieldForgeArchive.Side.Right)
            == HeavyShieldForgeArchive.LookupResult.Locked, "right still requires distinct purchase");
        Check(anotherIsland.TryGetSeatReceipts(lineage, out string? restoredLeft,
                out string? restoredRight) == HeavyShieldForgeArchive.LookupResult.Unlocked
            && restoredLeft == left && restoredRight == null, "persisted receipt restores left quota only");
        Check(anotherIsland.RecordExtraSeatAfterCompletedPayment(lineage, HeavyShieldForgeArchive.Side.Right, right)
            == HeavyShieldForgeArchive.RecordResult.Saved, "right seat saved");
        Check(new HeavyShieldForgeArchive(path).TryGetSeatReceipts(lineage, out restoredLeft,
                out restoredRight) == HeavyShieldForgeArchive.LookupResult.Unlocked
            && restoredLeft == left && restoredRight == right, "both distinct receipts survive restart");
        Check(new HeavyShieldForgeArchive(path).LookupExtraSeat(lineage, HeavyShieldForgeArchive.Side.Right)
            == HeavyShieldForgeArchive.LookupResult.Unlocked, "right survives restart");
        Check(new HeavyShieldForgeArchive(path).LookupExtraSeat("other-campaign", HeavyShieldForgeArchive.Side.Left)
            == HeavyShieldForgeArchive.LookupResult.Locked, "seat does not cross campaigns");
    });
}

static void DuplicateSeatPayment()
{
    InTemporaryDirectory(path =>
    {
        const string lineage = "campaign";
        var archive = new HeavyShieldForgeArchive(path);
        archive.RecordAfterCompletedPayment(lineage, Guid.NewGuid().ToString("D"));
        string receipt = Guid.NewGuid().ToString("D");
        Check(archive.RecordExtraSeatAfterCompletedPayment(lineage, HeavyShieldForgeArchive.Side.Left, receipt)
            == HeavyShieldForgeArchive.RecordResult.Saved, "first seat purchase");
        byte[] before = File.ReadAllBytes(path);
        var second = new HeavyShieldForgeArchive(path);
        Check(second.RecordExtraSeatAfterCompletedPayment(lineage, HeavyShieldForgeArchive.Side.Left, receipt)
            == HeavyShieldForgeArchive.RecordResult.SameReceiptAlreadySaved,
            "same payment idempotent on same side; no refund of original purchase");
        Check(second.RecordExtraSeatAfterCompletedPayment(lineage, HeavyShieldForgeArchive.Side.Right, receipt)
            == HeavyShieldForgeArchive.RecordResult.ReceiptAlreadyUsed, "same payment cannot grant right");
        Check(second.RecordExtraSeatAfterCompletedPayment("other", HeavyShieldForgeArchive.Side.Right, receipt)
            == HeavyShieldForgeArchive.RecordResult.ReceiptAlreadyUsed, "same payment cannot cross campaign");
        Check(second.RecordExtraSeatAfterCompletedPayment(lineage, HeavyShieldForgeArchive.Side.Left,
                Guid.NewGuid().ToString("D")) == HeavyShieldForgeArchive.RecordResult.AlreadyUnlocked,
            "different payment cannot re-buy occupied left seat");
        Check(before.SequenceEqual(File.ReadAllBytes(path)), "duplicates do not rewrite file");
        Check(second.LookupExtraSeat(lineage, HeavyShieldForgeArchive.Side.Right)
            == HeavyShieldForgeArchive.LookupResult.Locked, "right remains locked");
    });
}

static void V1Migration()
{
    InTemporaryDirectory(path =>
    {
        File.WriteAllText(path, "{\"version\":1,\"unlocked\":[\"campaign\"]}");
        var archive = new HeavyShieldForgeArchive(path);
        Check(archive.Lookup("campaign") == HeavyShieldForgeArchive.LookupResult.Unlocked,
            "v1 mold retained");
        Check(archive.LookupExtraSeat("campaign", HeavyShieldForgeArchive.Side.Left)
            == HeavyShieldForgeArchive.LookupResult.Locked, "v1 has no extra seats");
        Check(archive.TryGetSeatReceipts("campaign", out string? beforeLeft,
                out string? beforeRight) == HeavyShieldForgeArchive.LookupResult.Unlocked
            && beforeLeft == null && beforeRight == null, "v1 restores no spent receipts");
        Check(archive.TryGetMoldReceipt("campaign", out string? oldMold)
            == HeavyShieldForgeArchive.LookupResult.Unlocked && oldMold == null,
            "v1 legacy mold provenance remains unknown");
        Check(archive.RecordAfterCompletedPayment("campaign", Guid.NewGuid().ToString("D"))
            == HeavyShieldForgeArchive.RecordResult.AlreadyUnlocked,
            "new receipt cannot be treated as retry of v1 unlock");
        Check(archive.RecordExtraSeatAfterCompletedPayment("campaign", HeavyShieldForgeArchive.Side.Left,
                Guid.NewGuid().ToString("D")) == HeavyShieldForgeArchive.RecordResult.Saved,
            "valid purchase migrates v1");
        Check(File.ReadAllText(path).Contains("\"version\":3"), "v3 written");
        Check(new HeavyShieldForgeArchive(path).Lookup("campaign") == HeavyShieldForgeArchive.LookupResult.Unlocked,
            "mold retained after migration");
    });
}

static void InvalidSeatArchive()
{
    InTemporaryDirectory(path =>
    {
        string receipt = Guid.NewGuid().ToString("D");
        var archive = new HeavyShieldForgeArchive(path);
        foreach (string json in new[]
        {
            "{\"version\":2,\"unlocked\":[\"campaign\"],\"seatPurchases\":[{\"lineage\":\"campaign\",\"side\":\"left\",\"receipt\":\"not-guid\"}]}",
            "{\"version\":2,\"unlocked\":[\"campaign\"],\"seatPurchases\":[{\"lineage\":\"other\",\"side\":\"left\",\"receipt\":\"" + receipt + "\"}]}",
            "{\"version\":2,\"unlocked\":[\"campaign\"],\"seatPurchases\":[],\"extra\":0}",
            "{\"version\":4,\"unlocked\":[\"campaign\"],\"seatPurchases\":[]}",
        })
        {
            File.WriteAllText(path, json);
            byte[] before = File.ReadAllBytes(path);
            Check(archive.LookupExtraSeat("campaign", HeavyShieldForgeArchive.Side.Left) is
                HeavyShieldForgeArchive.LookupResult.Corrupt or HeavyShieldForgeArchive.LookupResult.Unsupported,
                "bad/future seat archive fail closed");
            Check(archive.TryGetSeatReceipts("campaign", out string? failedLeft,
                    out string? failedRight) is HeavyShieldForgeArchive.LookupResult.Corrupt
                    or HeavyShieldForgeArchive.LookupResult.Unsupported
                && failedLeft == null && failedRight == null,
                "bad/future archive cannot restore stale quota receipts");
            Check(archive.RecordExtraSeatAfterCompletedPayment("campaign", HeavyShieldForgeArchive.Side.Left,
                    Guid.NewGuid().ToString("D")) == HeavyShieldForgeArchive.RecordResult.Unavailable,
                "bad/future seat archive refuses receipt");
            Check(before.SequenceEqual(File.ReadAllBytes(path)), "bad/future seat archive unchanged");
        }
    });
}

static void SeatWriteFailure()
{
    InTemporaryDirectory(path =>
    {
        var archive = new HeavyShieldForgeArchive(path);
        archive.RecordAfterCompletedPayment("campaign", Guid.NewGuid().ToString("D"));
        string receipt = Guid.NewGuid().ToString("D");
        using (var locked = new FileStream(path + ".lock", FileMode.OpenOrCreate,
                   FileAccess.ReadWrite, FileShare.None))
            Check(archive.RecordExtraSeatAfterCompletedPayment("campaign", HeavyShieldForgeArchive.Side.Left,
                    receipt) == HeavyShieldForgeArchive.RecordResult.SaveFailed, "lock contention fails");
        Check(archive.LookupExtraSeat("campaign", HeavyShieldForgeArchive.Side.Left)
            == HeavyShieldForgeArchive.LookupResult.Locked, "failure did not grant seat");
        Check(archive.RecordExtraSeatAfterCompletedPayment("campaign", HeavyShieldForgeArchive.Side.Left,
                "bad-receipt") == HeavyShieldForgeArchive.RecordResult.InvalidReceipt,
            "invalid receipt refuses mutation");
        Check(archive.RecordExtraSeatAfterCompletedPayment("campaign", (HeavyShieldForgeArchive.Side)2,
                receipt) == HeavyShieldForgeArchive.RecordResult.InvalidKey,
            "invalid side refuses mutation");
    });
}

static void CrossPurchaseReceipt()
{
    InTemporaryDirectory(path =>
    {
        var archive = new HeavyShieldForgeArchive(path);
        string mold = Guid.NewGuid().ToString("D");
        string seat = Guid.NewGuid().ToString("D");
        Check(archive.RecordAfterCompletedPayment("campaign", mold)
            == HeavyShieldForgeArchive.RecordResult.Saved, "mold purchased");
        Check(archive.RecordExtraSeatAfterCompletedPayment("campaign", HeavyShieldForgeArchive.Side.Left, mold)
            == HeavyShieldForgeArchive.RecordResult.ReceiptAlreadyUsed,
            "mold receipt cannot buy extra seat");
        Check(archive.RecordExtraSeatAfterCompletedPayment("campaign", HeavyShieldForgeArchive.Side.Left, seat)
            == HeavyShieldForgeArchive.RecordResult.Saved, "separate seat receipt accepted");
        Check(archive.RecordAfterCompletedPayment("other-campaign", seat)
            == HeavyShieldForgeArchive.RecordResult.ReceiptAlreadyUsed,
            "seat receipt cannot buy another mold");
        Check(archive.RecordAfterCompletedPayment("other-campaign", mold)
            == HeavyShieldForgeArchive.RecordResult.ReceiptAlreadyUsed,
            "mold receipt cannot buy another campaign mold");
        Check(archive.Lookup("other-campaign") == HeavyShieldForgeArchive.LookupResult.Locked,
            "cross-purpose attempts did not unlock other campaign");
    });
}

static void V2Migration()
{
    InTemporaryDirectory(path =>
    {
        string oldSeat = Guid.NewGuid().ToString("D");
        File.WriteAllText(path, "{\"version\":2,\"unlocked\":[\"campaign\"],\"seatPurchases\":[" +
            "{\"lineage\":\"campaign\",\"side\":\"left\",\"receipt\":\"" + oldSeat + "\"}]}");
        var archive = new HeavyShieldForgeArchive(path);
        Check(archive.Lookup("campaign") == HeavyShieldForgeArchive.LookupResult.Unlocked,
            "v2 mold retained");
        Check(archive.TryGetMoldReceipt("campaign", out string? unknownMold)
            == HeavyShieldForgeArchive.LookupResult.Unlocked && unknownMold == null,
            "v2 does not invent old mold receipt");
        Check(archive.TryGetSeatReceipts("campaign", out string? left, out string? right)
            == HeavyShieldForgeArchive.LookupResult.Unlocked && left == oldSeat && right == null,
            "v2 seat receipt retained");
        Check(archive.RecordAfterCompletedPayment("campaign", Guid.NewGuid().ToString("D"))
            == HeavyShieldForgeArchive.RecordResult.AlreadyUnlocked,
            "new mold receipt is distinct charge against v2 unlock");
        string newSeat = Guid.NewGuid().ToString("D");
        Check(archive.RecordExtraSeatAfterCompletedPayment("campaign", HeavyShieldForgeArchive.Side.Right, newSeat)
            == HeavyShieldForgeArchive.RecordResult.Saved, "v2 migrates on right seat purchase");
        Check(File.ReadAllText(path).Contains("\"version\":3"), "v3 format after migration");
        var reloaded = new HeavyShieldForgeArchive(path);
        Check(reloaded.TryGetMoldReceipt("campaign", out unknownMold)
            == HeavyShieldForgeArchive.LookupResult.Unlocked && unknownMold == null,
            "unknown mold provenance stays unknown after migration");
        Check(reloaded.TryGetSeatReceipts("campaign", out left, out right)
            == HeavyShieldForgeArchive.LookupResult.Unlocked && left == oldSeat && right == newSeat,
            "both seat receipts survive migration");
    });
}

static void InvalidMoldArchive()
{
    InTemporaryDirectory(path =>
    {
        string receipt = Guid.NewGuid().ToString("D");
        var archive = new HeavyShieldForgeArchive(path);
        foreach (string json in new[]
        {
            "{\"version\":3,\"unlocked\":[\"campaign\"],\"seatPurchases\":[],\"moldPurchases\":[{\"lineage\":\"other\",\"receipt\":\"" + receipt + "\"}]}",
            "{\"version\":3,\"unlocked\":[\"campaign\"],\"seatPurchases\":[],\"moldPurchases\":[{\"lineage\":\"campaign\",\"receipt\":\"bad\"}]}",
            "{\"version\":3,\"unlocked\":[\"campaign\"],\"seatPurchases\":[],\"moldPurchases\":[{\"lineage\":\"campaign\",\"receipt\":\"" + receipt + "\"},{\"lineage\":\"campaign\",\"receipt\":\"" + Guid.NewGuid().ToString("D") + "\"}]}",
            "{\"version\":3,\"unlocked\":[\"campaign\"],\"seatPurchases\":[{\"lineage\":\"campaign\",\"side\":\"left\",\"receipt\":\"" + receipt + "\"}],\"moldPurchases\":[{\"lineage\":\"campaign\",\"receipt\":\"" + receipt + "\"}]}",
        })
        {
            File.WriteAllText(path, json);
            byte[] before = File.ReadAllBytes(path);
            Check(archive.TryGetMoldReceipt("campaign", out string? unknown)
                == HeavyShieldForgeArchive.LookupResult.Corrupt && unknown == null,
                "bad v3 mold data fails closed");
            Check(archive.RecordAfterCompletedPayment("campaign", Guid.NewGuid().ToString("D"))
                == HeavyShieldForgeArchive.RecordResult.Unavailable,
                "bad v3 cannot be overwritten by purchase");
            Check(before.SequenceEqual(File.ReadAllBytes(path)), "bad v3 bytes untouched");
        }
    });
}

static void InTemporaryDirectory(Action<string> run)
{
    string directory = Path.Combine(Path.GetTempPath(), "heavy-shield-forge-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try { run(Path.Combine(directory, "forge.json")); }
    finally { Directory.Delete(directory, true); }
}

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
}
