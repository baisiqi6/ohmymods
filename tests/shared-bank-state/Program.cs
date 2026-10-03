using System;
using PrivateBankR3;

static class Check
{
    static int count;
    static void Eq<T>(T expected, T actual, string label)
    {
        if (!Equals(expected, actual)) throw new Exception(label + ": expected " + expected + ", got " + actual);
        count++;
    }
    static void True(bool condition, string label) => Eq(true, condition, label);
    static void Main()
    {
        var owner = new object();
        var a = new BankCatalogEntry(101, BankCategory.Normal, 0);
        var state = new SharedBankState(owner);
        True(state.BindOnce(owner, new[] { a }, null), "new owner binds");
        Eq(SaveDocumentStatus.NoKey, state.BuildSaveDocument(owner, out _), "new owner no key");
        True(state.ObserveLive(owner, 101, 90), "live first seed");
        Eq(SaveDocumentStatus.NoKey, state.BuildSaveDocument(owner, out _), "seed alone not staged");
        True(state.CaptureSucceeded(owner, 2, new[] { new BankCapture(101, 100) }), "native row100");
        True(state.ObserveLive(owner, 101, 90), "later live90");
        Eq(SaveDocumentStatus.Ready, state.BuildSaveDocument(owner, out string first), "ready after native capture");
        True(first.Contains("\"coins\":100"), "document uses row100, not later live90");
        Eq(SaveDocumentStatus.Ready, state.BuildSaveDocument(owner, out string retry), "failed readback retry ready");
        Eq(first, retry, "failed readback retry preserves document");
        True(state.MarkWriteVerified(owner, retry), "verified readback");

        True(state.CaptureFailed(owner, 2, 101), "old source fault");
        True(state.CaptureSucceeded(owner, 3, new[] { new BankCapture(101, 90) }, false),
            "no-physical stages frozen90");
        Eq(SaveDocumentStatus.Rejected, state.BuildSaveDocument(owner, out _),
            "no-physical retains old source fault");
        True(state.CaptureSucceeded(owner, 2, new[] { new BankCapture(101, 90) }),
            "source2 complete capture clears only own fault");
        Eq(SaveDocumentStatus.Ready, state.BuildSaveDocument(owner, out string cleared),
            "after source fault repaired ready");
        True(cleared.Contains("\"coins\":90"), "document stage90");

        var challenges = new SharedBankState(owner = new object());
        var c0 = new BankCatalogEntry(201, BankCategory.Challenge, 0);
        var c1 = new BankCatalogEntry(202, BankCategory.Challenge, 1);
        True(challenges.BindOnce(owner, new[] { c0, c1 }, null), "two challenges bind");
        True(challenges.ObserveLive(owner, 201, 50), "deleted challenge live");
        True(challenges.ObserveLive(owner, 202, 70), "survivor challenge live");
        True(challenges.CaptureSucceeded(owner, 1, new[] { new BankCapture(202, 70) }), "survivor stage");
        True(challenges.CaptureFailed(owner, 1, 201), "deleted challenge fault");
        True(challenges.ReconcileCatalog(owner,
            new[] { new BankCatalogEntry(202, BankCategory.Challenge, 0) }), "delete and reorder");
        True(challenges.TryReadLive(owner, 202, out int live), "survivor live readable");
        Eq(70, live, "survivor balance retained");
        Eq(SaveDocumentStatus.Ready, challenges.BuildSaveDocument(owner, out string reordered),
            "deleted fault removed");
        True(reordered.Contains("\"ordinal\":0"), "survivor moved slot");
        True(!reordered.Contains("\"coins\":50"), "deleted balance absent");
        Console.WriteLine("PASS " + count + " R3 state assertions; actual candidate source linked");
    }
}
