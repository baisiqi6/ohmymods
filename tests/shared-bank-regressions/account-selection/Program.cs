// Account selection regression for Issue318.
//
// The production boundary under test is SharedBankNative.Account plus the real BeginSave /
// Prepare / AsyncGate / SyncGate chain. GetCurrentCampaign models the shipped 2.4.3 native
// contract in bank-combo/NativeStubs.cs: normal campaigns by index when currentChallenge==0,
// otherwise find-first in `challenges` by CampaignSaveData.challengeId. All source files in the
// production chain are linked unmodified from SourceRoot; nothing here reimplements Account.
using System;
using System.Reflection;
using KingdomEnhancedMod;
using PrivateBankR3;

internal static class Program
{
    private const string BankKey = "MyMod_SharedBankNative_v1";
    // Opaque, synthetic purchase values: never parsed, only preserved byte-for-byte.
    private const string HeroSentinel = "KingdomEnhancedMod_HeroRights_v1";
    private const string MusketeerSentinel = "KingdomEnhancedMod_MusketeerLegacy_v1";
    private const string PaidRightsSentinel = "KingdomEnhancedMod_PaidRights_v1";
    private const int FailureBits = (int)Coatsink.Common.SaveLoadResult.Save
        | (int)Coatsink.Common.SaveLoadResult.Failure;

    private static readonly MethodInfo EnsureMethod =
        typeof(SharedBankNative).GetMethod("Ensure", Harness.AnyStatic)
        ?? throw new Exception("missing Ensure");
    private static readonly MethodInfo AccountMethod =
        typeof(SharedBankNative).GetMethod("Account", Harness.AnyStatic)
        ?? throw new Exception("missing Account");
    private static readonly MethodInfo PrepareMethod =
        typeof(SharedBankNative).GetMethod("Prepare", Harness.AnyStatic)
        ?? throw new Exception("missing Prepare");
    // The interop dictionary stub exposes no enumerator; inspect its own backing store so the
    // assertion covers every staged purchase key, not just the bank key and a count.
    private static readonly FieldInfo PrefsItems =
        typeof(Il2CppSystem.Collections.Generic.Dictionary<string, string>)
            .GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new Exception("missing prefs backing dictionary");

    private static int Main()
    {
        PlayerIdOrder();
        PlayerIdOrderSavePath();
        SparseChallengeIds();
        PermutedNonDenseChallengeIds();
        NormalCampaignPosition();
        SyntheticBalancesIsolated();
        Rejections();
        UnresolvedCaptureStillBlocks();
        UnreadablePrefsStillBlock();
        ForeignOwnerStillBlocks();
        return Harness.Finish();
    }

    // ----------------------------------------------------------- fixtures

    private static GlobalSaveData World(int normalCount, int[] challengeIds,
        int currentCampaign, int currentChallenge)
    {
        Fixture.NewWorld();
        GlobalSaveData g = GlobalSaveData._loaded;
        g.campaigns.Clear();
        for (int i = 0; i < normalCount; i++) g.campaigns.Add(new CampaignSaveData());
        foreach (int id in challengeIds)
            g.challenges.Add(new CampaignSaveData { challengeId = id });
        g.currentCampaign = currentCampaign;
        g.currentChallenge = currentChallenge;
        g.prefs.contents[HeroSentinel] = "synthetic-hero-purchase-sentinel";
        g.prefs.contents[MusketeerSentinel] = "synthetic-musketeer-purchase-sentinel";
        g.prefs.contents[PaidRightsSentinel] = "synthetic-paid-rights-sentinel";
        return g;
    }

    private static string Doc(params (BankCategory Category, int Ordinal, int Coins)[] records)
        => BankDocument.Write(records);

    // ---------------------------------------------------------- invocation

    private static bool Ensure(bool reconcile = false)
        => (bool)EnsureMethod.Invoke(null, new object[] { reconcile });

    private static bool Account(out nint account)
    {
        object[] args = { (nint)0 };
        bool ok = (bool)AccountMethod.Invoke(null, args);
        account = (nint)args[0];
        return ok;
    }

    private static bool Prepare(GlobalSaveData g)
        => (bool)PrepareMethod.Invoke(null, new object[] { g });

    private static CampaignSaveData[] Refs()
        => Harness.GetStatic<CampaignSaveData[]>(typeof(SharedBankNative), "_campaignRefs");

    private static SharedBankState State()
        => Harness.GetStatic<SharedBankState>(typeof(SharedBankNative), "_state");

    private static object Token()
        => Harness.GetStatic(typeof(SharedBankNative), "_token");

    private static long Ptr(CampaignSaveData value) => (long)value.Pointer;

    private sealed class PrefsSnapshot
    {
        public System.Collections.Generic.SortedDictionary<string, string> Values;
    }

    private static PrefsSnapshot Snapshot(GlobalSaveData g)
    {
        var items = (System.Collections.Generic.Dictionary<string, string>)
            PrefsItems.GetValue(g.prefs.contents);
        var copy = new System.Collections.Generic.SortedDictionary<string, string>(
            StringComparer.Ordinal);
        foreach (System.Collections.Generic.KeyValuePair<string, string> pair in items)
            copy[pair.Key] = pair.Value;
        return new PrefsSnapshot { Values = copy };
    }

    private static void SamePrefs(PrefsSnapshot before, GlobalSaveData g, string label)
    {
        PrefsSnapshot now = Snapshot(g);
        Harness.Eq(before.Values.Count, now.Values.Count, label + " pair count");
        foreach (System.Collections.Generic.KeyValuePair<string, string> pair in before.Values)
        {
            Harness.True(now.Values.TryGetValue(pair.Key, out string value),
                label + " missing " + pair.Key);
            Harness.True(pair.Value == value, label + " value changed for " + pair.Key);
        }
    }

    private sealed class AsyncRun
    {
        public bool Ran;
        public bool RunOriginal;
        public int Callbacks;
        public int Bits;
    }

    private static AsyncRun Async(GlobalSaveData g)
    {
        int callbacks = 0, bits = 0;
        var callback = new Il2CppSystem.Action<Coatsink.Common.SaveLoadResult>(value =>
        {
            callbacks++;
            bits = (int)value;
        });
        var gate = typeof(SharedBankNative).GetNestedType("AsyncGate", Harness.AnyStatic)
            ?? throw new Exception("missing AsyncGate");
        var prefix = gate.GetMethod("Prefix", Harness.AnyStatic)
            ?? throw new Exception("missing AsyncGate.Prefix");
        object[] args = { g, callback, true };
        bool result = (bool)prefix.Invoke(null, args);
        return new AsyncRun
        {
            Ran = result && (bool)args[2],
            RunOriginal = (bool)args[2],
            Callbacks = callbacks,
            Bits = bits,
        };
    }

    private sealed class SyncRun
    {
        public bool Ran;
        public bool Result;
        public GlobalSaveData._Save_d__89 Routine;
    }

    private static SyncRun Sync(GlobalSaveData g)
    {
        var routine = new GlobalSaveData._Save_d__89 { __1__state = 0, __4__this = g };
        var gate = typeof(SharedBankNative).GetNestedType("SyncGate", Harness.AnyStatic)
            ?? throw new Exception("missing SyncGate");
        var prefix = gate.GetMethod("Prefix", Harness.AnyStatic)
            ?? throw new Exception("missing SyncGate.Prefix");
        object[] args = { routine, true };
        bool result = (bool)prefix.Invoke(null, args);
        return new SyncRun { Ran = result && (bool)args[1], Result = (bool)args[1], Routine = routine };
    }

    // ------------------------------------------------------------- cases

    private static void PlayerIdOrder()
    {
        Harness.Test("account-player-id-order-selects-last-selected-id2", () =>
        {
            GlobalSaveData g = World(4, new[] { 10, 9, 4, 8, 1, 2 }, 3, 2);
            PrefsSnapshot before = Snapshot(g);
            Harness.Eq(3, before.Values.Count, "three synthetic purchase sentinels staged");
            Harness.True(before.Values.ContainsKey(HeroSentinel), "hero sentinel visible");
            Harness.True(before.Values.ContainsKey(MusketeerSentinel), "musketeer sentinel visible");
            Harness.True(before.Values.ContainsKey(PaidRightsSentinel), "paid-rights sentinel visible");
            Harness.True(Ensure(), "healthy ensure before account");
            Harness.True(Harness.GetStatic(typeof(SharedBankNative), "_reason") == null,
                "no prior fault");
            Harness.True(Account(out nint account), "account resolves from native pointer");
            Harness.Eq(Ptr(g.challenges[5]), (long)account, "challenge-list index 5 pointer");
            Harness.Eq(2, g.challenges[5].challengeId, "last selected challenge id");
            Harness.False(account == (nint)g.challenges[1].Pointer,
                "must not select challenge id 9 by currentChallenge-1");
            SamePrefs(before, g, "selection leaves prefs");
        });
    }

    private static void PlayerIdOrderSavePath()
    {
        Harness.Test("account-player-id-order-no-bank-save-gates-unblocked", () =>
        {
            GlobalSaveData g = World(4, new[] { 10, 9, 4, 8, 1, 2 }, 3, 2);
            NetworkPostbox.Instance = new NetworkPostbox();
            PrefsSnapshot before = Snapshot(g);
            Harness.True(Ensure(), "healthy no-key ensure");
            Harness.False(State().IsCorrupt, "missing bank key is a valid bind");
            Harness.False(g.prefs.contents.TryGetValue(BankKey, out _), "no bank key present");

            SharedBankNative.Scope scope = SharedBankNative.BeginSave(3, 0, 2);
            Harness.True(scope.Account != 0, "BeginSave resolved the current account");
            Harness.Eq(Ptr(g.challenges[5]), (long)scope.Account, "scope account is id 2 pointer");

            Harness.True(Prepare(g), "Prepare allows the save");
            Harness.True(Harness.GetStatic(typeof(SharedBankNative), "_reason") == null,
                "no save account fault");

            AsyncRun async = Async(g);
            Harness.True(async.Ran, "async gate allows the save");
            Harness.True(async.RunOriginal, "async runOriginal stays true");
            Harness.Eq(0, async.Callbacks, "async failure callback count");

            SyncRun sync = Sync(g);
            Harness.True(sync.Ran, "sync gate allows the save");
            Harness.True(sync.Result, "sync result stays true");
            Harness.Eq(0, sync.Routine.__1__state, "sync state untouched");
            Harness.Eq(0, sync.Routine.ReturnWrites, "sync boxed return untouched");

            Harness.False(g.prefs.contents.TryGetValue(BankKey, out _), "still no bank key");
            SamePrefs(before, g, "save path leaves prefs");
        });
    }

    private static void SparseChallengeIds()
    {
        Harness.Test("account-sparse-challenge-ids", () =>
        {
            GlobalSaveData g = World(2, new[] { 30, 5000, 2, 71 }, 0, 2);
            PrefsSnapshot before = Snapshot(g);
            Harness.True(Ensure(), "healthy ensure");
            Harness.True(Account(out nint account), "sparse challenge id resolves");
            Harness.Eq(Ptr(g.challenges[2]), (long)account, "found by id 2, not ordinal 1");
            Harness.Eq(2, g.challenges[2].challengeId, "resolved challenge id");
            SamePrefs(before, g, "sparse selection leaves prefs");
        });
    }

    private static void PermutedNonDenseChallengeIds()
    {
        Harness.Test("account-permuted-non-dense-challenge-ids", () =>
        {
            GlobalSaveData g = World(3, new[] { 7, 1, 99, 3, 42 }, 1, 42);
            PrefsSnapshot before = Snapshot(g);
            Harness.True(Ensure(), "healthy ensure");
            Harness.True(Account(out nint account), "non-dense challenge id resolves");
            Harness.Eq(Ptr(g.challenges[4]), (long)account, "found by id 42");
            Harness.Eq(42, g.challenges[4].challengeId, "resolved challenge id");
            SamePrefs(before, g, "permuted selection leaves prefs");
        });
    }

    private static void NormalCampaignPosition()
    {
        Harness.Test("account-normal-campaign-position", () =>
        {
            GlobalSaveData g = World(3, new[] { 10, 9, 4, 8 }, 2, 0);
            g.campaigns[0].challengeId = 2; // normal rows keep whatever id they had; unused here
            PrefsSnapshot before = Snapshot(g);
            Harness.True(Ensure(), "healthy ensure");
            Harness.True(Account(out nint account), "normal campaign resolves");
            Harness.Eq(Ptr(g.campaigns[2]), (long)account, "normal prefix index 2");
            SamePrefs(before, g, "normal selection leaves prefs");
        });
    }

    private static void SyntheticBalancesIsolated()
    {
        Harness.Test("account-synthetic-balances-61-93-isolated-prefs-unchanged", () =>
        {
            GlobalSaveData g = World(4, new[] { 10, 9, 4, 8, 1, 2 }, 0, 0);
            g.prefs.contents[BankKey] = Doc(
                (BankCategory.Normal, 0, 61),
                (BankCategory.Challenge, 5, 93));
            PrefsSnapshot before = Snapshot(g);
            Harness.Eq(4, before.Values.Count, "three sentinels plus the bank key staged");
            Harness.True(before.Values.ContainsKey(HeroSentinel), "hero sentinel visible");
            Harness.True(before.Values.ContainsKey(MusketeerSentinel), "musketeer sentinel visible");
            Harness.True(before.Values.ContainsKey(PaidRightsSentinel), "paid-rights sentinel visible");
            Harness.True(Ensure(), "document binds");
            SharedBankState state = State();
            object token = Token();

            Harness.True(Account(out nint normal), "normal current resolves");
            Harness.Eq(Ptr(g.campaigns[0]), (long)normal, "normal pointer");
            Harness.True(SharedBankNative.TryLive(out _, out nint liveNormal, out int normalCoins),
                "normal live read");
            Harness.Eq(61, normalCoins, "normal balance");
            Harness.Eq(Ptr(g.campaigns[0]), (long)liveNormal, "normal live pointer");
            Harness.True(state.TryReadLive(token, (nint)g.challenges[5].Pointer, out int other),
                "challenge live read");
            Harness.Eq(93, other, "challenge balance isolated");

            g.currentChallenge = 2;
            Harness.True(Account(out nint challenge), "challenge current resolves");
            Harness.Eq(Ptr(g.challenges[5]), (long)challenge, "challenge pointer");
            Harness.True(SharedBankNative.TryLive(out _, out _, out int challengeCoins),
                "challenge live read after switch");
            Harness.Eq(93, challengeCoins, "challenge balance");
            Harness.True(state.TryReadLive(token, (nint)g.campaigns[0].Pointer, out int kept),
                "normal live read after switch");
            Harness.Eq(61, kept, "normal balance isolated");
            SamePrefs(before, g, "balances leave raw prefs");
        });
    }

    private static void Rejections()
    {
        Harness.Test("account-rejects-null-current", () =>
        {
            GlobalSaveData g = World(2, new[] { 1, 2 }, 0, 9);
            Harness.True(Ensure(), "healthy ensure");
            Harness.False(Account(out nint account), "unknown challenge id returns null");
            Harness.Eq(0, (long)account, "no account leaks");
        });

        Harness.Test("account-rejects-out-of-range-normal-current", () =>
        {
            GlobalSaveData g = World(1, Array.Empty<int>(), 5, 0);
            Harness.True(Ensure(), "healthy ensure");
            Harness.False(Account(out nint account), "out-of-range normal index returns null");
            Harness.Eq(0, (long)account, "no account leaks");
        });

        Harness.Test("account-rejects-zero-current-pointer", () =>
        {
            GlobalSaveData g = World(2, new[] { 1, 2 }, 0, 1);
            Harness.True(Ensure(), "healthy ensure");
            g.challenges[0].Pointer = IntPtr.Zero; // native wrapper with a zero pointer
            Harness.False(Account(out nint account), "zero pointer is rejected");
            Harness.Eq(0, (long)account, "zero must not be reported as success");
        });

        Harness.Test("account-rejects-missing-current-wrapper", () =>
        {
            GlobalSaveData g = World(2, new[] { 1, 2 }, 0, 1);
            Harness.True(Ensure(), "healthy ensure");
            g.challenges[0] = new CampaignSaveData { challengeId = 1 };
            Harness.False(Account(out nint account), "current absent from cached refs");
            Harness.Eq(0, (long)account, "no account leaks");
        });

        Harness.Test("account-rejects-duplicate-current-across-groups", () =>
        {
            GlobalSaveData g = World(2, new[] { 1, 2 }, 0, 1);
            Harness.True(Ensure(), "healthy ensure");
            CampaignSaveData[] refs = Refs();
            var alias = new CampaignSaveData { challengeId = 1 };
            alias.Pointer = g.challenges[0].Pointer;
            var tampered = (CampaignSaveData[])refs.Clone();
            tampered[0] = alias;          // same native pointer at normal index 0 ...
            tampered[2] = refs[2];        // ... and at challenge index 2
            Harness.SetStatic(typeof(SharedBankNative), "_campaignRefs", tampered);
            Harness.False(Account(out nint account), "duplicate pointer across groups rejected");
            Harness.Eq(0, (long)account, "no account leaks");
        });

        Harness.Test("account-rejects-challenge-current-at-normal-position", () =>
        {
            GlobalSaveData g = World(2, new[] { 1, 2 }, 0, 1);
            Harness.True(Ensure(), "healthy ensure");
            CampaignSaveData[] refs = Refs();
            var tampered = (CampaignSaveData[])refs.Clone();
            tampered[0] = refs[2];        // challenge object in the normal prefix
            tampered[2] = refs[0];
            Harness.SetStatic(typeof(SharedBankNative), "_campaignRefs", tampered);
            Harness.False(Account(out nint account), "challenge kind in normal prefix rejected");
            Harness.Eq(0, (long)account, "no account leaks");
        });

        Harness.Test("account-rejects-normal-current-at-challenge-position", () =>
        {
            GlobalSaveData g = World(2, new[] { 1, 2 }, 0, 0);
            Harness.True(Ensure(), "healthy ensure");
            CampaignSaveData[] refs = Refs();
            var tampered = (CampaignSaveData[])refs.Clone();
            tampered[0] = refs[2];        // challenge object first
            tampered[2] = refs[0];        // normal object in the challenge suffix
            Harness.SetStatic(typeof(SharedBankNative), "_campaignRefs", tampered);
            Harness.False(Account(out nint account), "normal kind in challenge suffix rejected");
            Harness.Eq(0, (long)account, "no account leaks");
        });

        Harness.Test("account-rejects-normal-count-past-cache", () =>
        {
            GlobalSaveData g = World(4, new[] { 1, 2 }, 0, 0);
            Harness.True(Ensure(), "healthy ensure");
            Harness.Eq(6, Refs().Length, "cached array covers both groups");
            for (int i = 0; i < 3; i++) g.campaigns.Add(new CampaignSaveData());
            Harness.True(g.campaigns.Count > Refs().Length, "native normal count exceeds cache");
            Harness.False(Account(out nint account), "out-of-cache normal partition rejected");
            Harness.Eq(0, (long)account, "no account leaks");
        });

        Harness.Test("account-rejects-null-cache-entry", () =>
        {
            GlobalSaveData g = World(2, new[] { 1, 2 }, 0, 0);
            Harness.True(Ensure(), "healthy ensure");
            CampaignSaveData[] refs = Refs();
            var tampered = (CampaignSaveData[])refs.Clone();
            tampered[0] = null;
            Harness.SetStatic(typeof(SharedBankNative), "_campaignRefs", tampered);
            Harness.False(Account(out nint account), "null cached entry cannot match");
            Harness.Eq(0, (long)account, "no account leaks");
        });
    }

    private static void UnresolvedCaptureStillBlocks()
    {
        Harness.Test("account-unresolved-capture-still-blocks-save-gates", () =>
        {
            GlobalSaveData g = World(4, new[] { 10, 9, 4, 8, 1, 2 }, 3, 2);
            Harness.True(Ensure(), "healthy ensure");
            Harness.True(Account(out nint account), "account resolves");
            Harness.True(State().CaptureFailed(Token(), 0, account),
                "synthetic unresolved capture accepted");
            Harness.True(Account(out nint still), "selection itself survives the unresolved capture");
            Harness.Eq((long)account, (long)still, "same account");
            Harness.False(Prepare(g), "document stays rejected while a capture is unresolved");
            AsyncRun async = Async(g);
            Harness.False(async.Ran, "async gate refuses");
            Harness.False(async.RunOriginal, "async runOriginal cleared");
            Harness.Eq(1, async.Callbacks, "exactly one failure callback");
            Harness.Eq(FailureBits, async.Bits, "failure bits");
            SyncRun sync = Sync(g);
            Harness.False(sync.Ran, "sync gate refuses");
            Harness.Eq(-1, sync.Routine.__1__state, "sync terminal state");
            Harness.Eq(FailureBits, (int)sync.Routine.@return.value, "sync boxed failure");
        });
    }

    private static void UnreadablePrefsStillBlock()
    {
        Harness.Test("account-unreadable-prefs-still-blocks-account-and-gates", () =>
        {
            GlobalSaveData g = World(4, new[] { 10, 9, 4, 8, 1, 2 }, 3, 2);
            Harness.True(Ensure(), "healthy ensure");
            Harness.True(Account(out _), "account resolves before the read fault");
            g.prefs.contents.ThrowOnRead = true;
            Harness.SetStatic(typeof(SharedBankNative), "_global", null);
            Harness.False(Account(out nint account), "unreadable prefs fail closed");
            Harness.Eq(0, (long)account, "no account leaks");
            Harness.False(Prepare(g), "prepare stays refused");
            AsyncRun async = Async(g);
            Harness.False(async.Ran, "async gate refuses");
            Harness.Eq(1, async.Callbacks, "exactly one failure callback");
        });
    }

    private static void ForeignOwnerStillBlocks()
    {
        Harness.Test("account-foreign-owner-still-blocks-prepare-and-gate", () =>
        {
            GlobalSaveData g = World(4, new[] { 10, 9, 4, 8, 1, 2 }, 3, 2);
            PrefsSnapshot before = Snapshot(g);
            Harness.True(Ensure(), "healthy ensure");
            Harness.True(Account(out _), "account resolves");
            GlobalSaveData._loaded = new GlobalSaveData();
            Harness.False(Prepare(g), "old owner cannot prepare the replacement global");
            AsyncRun async = Async(g);
            Harness.False(async.Ran, "async gate refuses the foreign owner");
            Harness.Eq(1, async.Callbacks, "exactly one failure callback");
            Harness.Eq(FailureBits, async.Bits, "failure bits");
            SamePrefs(before, g, "foreign owner leaves old prefs");
        });
    }
}
