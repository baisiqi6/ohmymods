using KingdomEnhancedMod;
using UnityEngine;

// Issue 167 first-connect baseline enrollment regressions. Every case drives the
// product's own native-boundary entry points; the only stubs are the existing
// synthetic native layer. Positive cases are expected to fail (RED) against the
// unmodified product sources and pass against the candidate sources.
internal static class EnrollmentRegression
{
    private static long next = 5000000;
    private static int checks, failures;

    private static IntPtr Ptr() => new(++next);
    private static void Check(bool value, string name)
    { checks++; Console.WriteLine((value ? "PASS " : "FAIL ") + name); if (!value) failures++; }
    private static void Require(bool value, string name)
    { if (!value) throw new Exception("fixture: " + name); }
    private static GameObject Root(string tag = null) => new(++next, (int)next) { Tag = tag };

    private const string Json0 = "{\"land\":0,\"rev\":1}";
    private const string Json4 = "{\"land\":4,\"rev\":7}";
    private const string Json8 = "{\"land\":8,\"rev\":3}";

    private sealed class Env
    {
        internal GlobalSaveData Global;
        internal CampaignSaveData Campaign;
        internal IslandSaveData Island0, Island4, Island8;
        internal PrefsSaveData Prefs;
    }

    private static IslandSaveData MakeIsland(int land, double days, string json, bool isNew = false)
        => new IslandSaveData { Pointer = Ptr(), land = land, playTimeDays = days, Json = json, isNew = isNew };

    private static Env Setup(string key, int currentLand, bool twoCampaigns = false)
    {
        var campaign = new CampaignSaveData { Pointer = Ptr() };
        var i0 = MakeIsland(0, 12, Json0);
        var i4 = MakeIsland(4, 5, Json4);
        var i8 = MakeIsland(8, 3, Json8);
        campaign._islands.Add(i0);
        for (int i = 1; i <= 3; i++) campaign._islands.Add(MakeIsland(0, 0, "{}"));
        campaign._islands.Add(i4);
        for (int i = 5; i <= 7; i++) campaign._islands.Add(MakeIsland(0, 0, "{}"));
        campaign._islands.Add(i8);
        campaign.CurrentLand = currentLand;
        campaign.CurrentIsland = currentLand == 8 ? i8 : currentLand == 4 ? i4 : i0;
        var prefs = new PrefsSaveData { Pointer = Ptr() };
        if (key != null) prefs.contents[HeavyShieldSaveSchema.Key] = key;
        var global = new GlobalSaveData { Pointer = Ptr(), prefs = prefs };
        global.campaigns.Add(campaign);
        if (twoCampaigns)
        {
            var other = new CampaignSaveData { Pointer = Ptr() };
            var otherIsland = MakeIsland(0, 9, "{\"land\":0,\"rev\":2}");
            other._islands.Add(otherIsland);
            other.CurrentLand = 0; other.CurrentIsland = otherIsland;
            global.campaigns.Add(other);
        }
        GlobalSaveData._loaded = global;
        Managers.Inst = new Managers
        {
            game = new Game { Pointer = Ptr(), currentLand = currentLand },
            world = new World { Pointer = Ptr(), gameLayer = Root().Add<Transform>() },
        };
        Time.timeScale = 1; Time.time = 0;
        ModConfig.Enabled.Value = true; ModConfig.HeavyShieldEnabled.Value = true;
        Game.SavingEnabled = true; NetworkBigBoss.HasWorldAuth = true; NetworkBigBoss.IsOnline = false;
        IslandSaveData.isSavingGame = false; IslandSaveData.CurrentlySavingIsland = null;
        HeavyShieldRuntime.AttachSucceeds = true; HeavyShieldRuntime.PreflightSucceeds = true;
        HeavyShieldRuntime.DeferAttach = false; HeavyShieldRuntime.AttachAttempts = 0;
        HeavyShieldRuntime.AttachCalls = HeavyShieldRuntime.DetachCalls = 0;
        HeavyShieldShopShell.ObservedBowCalls = 0; HeavyShieldShopShell.ThrowObserve = false;
        return new Env
        {
            Global = global, Campaign = campaign, Island0 = i0, Island4 = i4, Island8 = i8,
            Prefs = prefs,
        };
    }

    private static void Load(IslandSaveData island, bool success = true)
    {
        var scope = HeavyShieldPersistence.BeginNativeIslandLoad(island);
        HeavyShieldPersistence.EndNativeIslandLoad(scope, success);
        HeavyShieldPersistence.TickBinding();
    }

    private static void Prepare(PrefsSaveData prefs)
    {
        var scope = HeavyShieldPersistence.BeginNativePrefsPrepare(prefs);
        bool success = false;
        try { prefs.CopyToSerializedEntries(); success = true; }
        finally { HeavyShieldPersistence.EndNativePrefsPrepare(scope, success); }
    }

    private static void Stage(Env env, IslandSaveData island)
    {
        IslandSaveData.isSavingGame = true; IslandSaveData.CurrentlySavingIsland = island;
        var s = HeavyShieldPersistence.BeginNativeIslandSave(env.Global.currentCampaign, island.land, 0);
        HeavyShieldPersistence.ObserveNativeIslandCaptured(s, island);
        HeavyShieldPersistence.EndNativeIslandSave(s, true);
        IslandSaveData.isSavingGame = false; IslandSaveData.CurrentlySavingIsland = null;
        Prepare(env.Prefs);
    }

    private static void Switch(Env env, IslandSaveData island)
    {
        env.Campaign.CurrentIsland = island; env.Campaign.CurrentLand = island.land;
        Managers.Inst.game.currentLand = island.land;
        Managers.Inst.world.gameLayer = Root().Add<Transform>();
    }

    private static string Hash(string guid, int challenge, int land, string raw)
        => HeavyShieldSaveCodec.SnapshotHash(guid, challenge, land, raw);

    private static bool SessionUnknown => HeavyShieldIdentity.Current == null || HeavyShieldIdentity.Current.Unknown;

    private static string FixtureKey()
    {
        // Observed-shape synthetic fixture: two campaigns, only land0 recorded,
        // native visited 0/4/8. No player GUID, hash or save data is embedded.
        // The private option is used only for local evidence; nothing is copied
        // and the fixture bytes are the codec-canonical form of the observed doc.
        string privatePath = Environment.GetEnvironmentVariable("PRIVATE_HEAVY_FIXTURE");
        HeavyShieldSaveDocument doc;
        if (!string.IsNullOrEmpty(privatePath))
            Require(HeavyShieldSaveCodec.TryParse(File.ReadAllText(privatePath), out doc, out _), "private fixture parses");
        else
        {
            doc = new HeavyShieldSaveDocument();
            foreach (var pair in new[] { (0, "11111111-1111-1111-1111-111111111111"),
                (1, "22222222-2222-2222-2222-222222222222") })
            {
                var campaign = new HeavyShieldSavedCampaign { Slot = pair.Item1, Guid = pair.Item2 };
                campaign.Islands.Add(new HeavyShieldSavedIsland { Challenge = 0, Land = 0,
                    SnapshotHash = Hash(pair.Item2, 0, 0, Json0) });
                doc.Campaigns.Add(campaign);
            }
        }
        Require(HeavyShieldSaveCodec.TrySerialize(doc, out string canonical, out var reason), "fixture serializes: " + reason);
        return canonical;
    }

    private static bool TryGetKey(Env env, out HeavyShieldSaveDocument doc)
    {
        doc = null;
        return env.Prefs.contents.TryGetValue(HeavyShieldSaveSchema.Key, out string raw)
            && HeavyShieldSaveCodec.TryParse(raw, out doc, out _);
    }

    internal static void Run()
    {
        Flagship();
        ExactCurrentWithMissingOthers();
        KeyMissingFirstConnect();
        NewGenerationFirstConnect();
        GuardKnownHashMismatch();
        GuardPaid();
        GuardLiveEvidence();
        GuardKeyChanged();
        GuardFailedKey();
        GuardFailedPop();
        GuardNestedScope();
        GuardIdentityChanges();
        GuardCampaignReplacement();
        GuardCapacity();
        GuardDocumentByteCapacity();
        ReviewCounterprobes();
        ReviewV2Counterprobes();
        GenerationRecovery();
        NoEnrollmentNeeded();
        PaidNormalPath();
        RecoverySuccess();
        Console.WriteLine($"ENROLLMENT regressions: {checks} checks, {failures} failures");
        if (failures != 0) throw new Exception("Enrollment regressions failed: " + failures);
    }

    // Observed-shape synthetic key v2 (slot0 known land0, no mold) + current land8 missing +
    // native visited [0,4,8]: one normal pop registers 4/8, publishes only through
    // the real Prepare, and the first purchase proceeds on the next normal save.
    private static void Flagship()
    {
        string fixtureKey = FixtureKey();
        Require(HeavyShieldSaveCodec.TryParse(fixtureKey, out var before, out _), "fixture doc");
        string guid = before.Campaigns[0].Guid;
        string land0Hash = before.Campaigns[0].Islands[0].SnapshotHash;
        var env = Setup(fixtureKey, 8, twoCampaigns: true);

        var scope = HeavyShieldPersistence.BeginNativeIslandLoad(env.Island8);
        Check(env.Prefs.contents[HeavyShieldSaveSchema.Key] == fixtureKey, "flagship: load prefix writes nothing");
        HeavyShieldPersistence.EndNativeIslandLoad(scope, true);
        HeavyShieldPersistence.TickBinding();

        Check(!SessionUnknown, "flagship: missing-row quarantine released by committed baseline");
        Check(HeavyShieldIdentity.StatusText.Contains("等待保存完成"), "flagship: prepare barrier message");
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold), "flagship: mold refused before Prepare");

        Prepare(env.Prefs);
        HeavyShieldPersistence.TickBinding();

        Check(TryGetKey(env, out var after), "flagship: published key parses");
        if (after != null)
        {
            Check(after.Campaigns.Count == 2, "flagship: other campaign row preserved");
            var campaign = after.Campaigns.Find(x => x.Guid == guid);
            Check(campaign != null, "flagship: campaign GUID unchanged");
            var row4 = campaign?.Islands.Find(x => x.Land == 4);
            var row8 = campaign?.Islands.Find(x => x.Land == 8);
            Check(row4 != null && row8 != null, "flagship: visited missing 4/8 registered");
            Check(row4 != null && row4.HashKind == HeavyShieldSnapshotFingerprint.CanonicalKind && row4.SnapshotHash == HeavyShieldSnapshotFingerprint.Hash(guid, 0, 4, Json4), "flagship: land4 hash is the frozen load snapshot");
            Check(row8 != null && row8.HashKind == HeavyShieldSnapshotFingerprint.CanonicalKind && row8.SnapshotHash == HeavyShieldSnapshotFingerprint.Hash(guid, 0, 8, Json8), "flagship: land8 hash is the frozen load snapshot");
            var land0 = campaign?.Islands.Find(x => x.Land == 0);
            Check(land0 != null && land0.HashKind == HeavyShieldSnapshotFingerprint.CanonicalKind
                && land0.SnapshotHash == HeavyShieldSnapshotFingerprint.Hash(guid, 0, 0, Json0)
                && land0.Claims.Count == 0, "flagship: unpaid legacy land0 is upgraded before first fee");
            Check(campaign != null && campaign.Islands.Count == 3, "flagship: only known0 + visited 4/8, no placeholders");
        }

        bool quotable = HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold);
        Check(quotable, "flagship: mold quotable after real Prepare");
        if (!quotable) return;

        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.Mold, out var moldLease), "flagship: mold reserve");
        Require(HeavyShieldIdentity.CompleteGemPayment(moldLease) == HeavyShieldCommitResult.Applied, "flagship: mold applied");
        env.Island8.Json = "{\"land\":8,\"rev\":4}";
        Stage(env, env.Island8);
        Switch(env, env.Island4);
        Load(env.Island4);
        Check(!SessionUnknown, "flagship: land4 binds exact after save+prepare");
        Check(HeavyShieldIdentity.GetQuotaView().MoldUnlocked, "flagship: paid mold entitlement retained");
        Check(HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldLeft),
            "flagship: shield quotable on land4 instead of re-locked");
    }

    // Exact current land0 with other visited islands missing must still register
    // the missing baseline before the first Gem.
    private static void ExactCurrentWithMissingOthers()
    {
        string guid = "11111111-1111-1111-1111-111111111111";
        var doc = new HeavyShieldSaveDocument();
        var campaign = new HeavyShieldSavedCampaign { Slot = 0, Guid = guid };
        campaign.Islands.Add(new HeavyShieldSavedIsland { Challenge = 0, Land = 0, SnapshotHash = Hash(guid, 0, 0, Json0) });
        doc.Campaigns.Add(campaign);
        Require(HeavyShieldSaveCodec.TrySerialize(doc, out string key, out var reason), "exact0 key: " + reason);

        var env = Setup(key, 0);
        Load(env.Island0);
        Check(!SessionUnknown, "exact0: current island stays exact");
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
            "exact0: first purchase held until the baseline is published");
        Prepare(env.Prefs);
        HeavyShieldPersistence.TickBinding();
        Check(TryGetKey(env, out var after) && after.Campaigns.Find(x => x.Guid == guid) is { } registered
            && registered.Islands.Find(x => x.Land == 4) != null && registered.Islands.Find(x => x.Land == 8) != null,
            "exact0: other visited missing islands enrolled before first purchase");
        Check(HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold), "exact0: mold quotable after Prepare");
    }

    // Confirmed-missing key on an advanced save: the visited baseline is registered
    // before the first Gem instead of after it.
    private static void KeyMissingFirstConnect()
    {
        var env = Setup(null, 8);
        Load(env.Island8);
        Check(!env.Prefs.contents.ContainsKey(HeavyShieldSaveSchema.Key), "keymissing: load writes nothing before Prepare");
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
            "keymissing: existing visited baseline holds the first purchase");
        Prepare(env.Prefs);
        HeavyShieldPersistence.TickBinding();
        Check(TryGetKey(env, out var after), "keymissing: published key parses");
        if (after != null)
        {
            string guid = HeavyShieldIdentity.Current?.Guid;
            var row = after.Campaigns.Find(x => x.Guid == guid);
            Check(row != null, "keymissing: same native lineage GUID");
            Check(row != null && row.Islands.Count == 3 && row.Islands.Find(x => x.Land == 0) != null
                && row.Islands.Find(x => x.Land == 4) != null && row.Islands.Find(x => x.Land == 8) != null,
                "keymissing: visited 0/4/8 baseline registered");
        }
        Check(HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold), "keymissing: mold quotable after Prepare");
    }

    // Verified new generation: existing visited islands are registered before the
    // first Gem; the new island itself is never pre-occupied.
    private static void NewGenerationFirstConnect()
    {
        var env = Setup(null, 8);
        env.Island8.isNew = true; env.Island8.playTimeDays = 0;
        var generation = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
        HeavyShieldPersistence.EndNativeGeneration(generation, env.Campaign, true);
        HeavyShieldPersistence.TickBinding();
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
            "newgen: visited baseline holds the first purchase");
        Prepare(env.Prefs);
        HeavyShieldPersistence.TickBinding();
        Check(TryGetKey(env, out var after), "newgen: published key parses");
        {
            var row = after != null && after.Campaigns.Count > 0 ? after.Campaigns[0] : null;
            Check(row != null && row.Islands.Find(x => x.Land == 0) != null && row.Islands.Find(x => x.Land == 4) != null
                && row.Islands.Find(x => x.Land == 8) == null,
                "newgen: existing visited 0/4 registered, new 8 not pre-occupied");
        }
        Check(HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold), "newgen: mold quotable after Prepare");
    }

    private static void GuardKnownHashMismatch()
    {
        string guid = "22222222-2222-2222-2222-222222222222";
        var doc = new HeavyShieldSaveDocument();
        var campaign = new HeavyShieldSavedCampaign { Slot = 0, Guid = guid };
        campaign.Islands.Add(new HeavyShieldSavedIsland { Challenge = 0, Land = 0, HashKind = HeavyShieldSnapshotFingerprint.CanonicalKind, SnapshotHash = HeavyShieldSnapshotFingerprint.Hash(guid, 0, 0, Json0) });
        campaign.Islands.Add(new HeavyShieldSavedIsland { Challenge = 0, Land = 8, HashKind = HeavyShieldSnapshotFingerprint.CanonicalKind, SnapshotHash = new string('a', 64) });
        doc.Campaigns.Add(campaign);
        Require(HeavyShieldSaveCodec.TrySerialize(doc, out string key, out _), "mismatch key");
        var env = Setup(key, 8);
        Load(env.Island8);
        Check(SessionUnknown, "guard mismatch: known row hash mismatch stays Unknown");
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold), "guard mismatch: no quote");
        Prepare(env.Prefs);
        Check(env.Prefs.contents[HeavyShieldSaveSchema.Key] == key, "guard mismatch: no baseline published");
    }

    private static void GuardPaid()
    {
        string guid = "33333333-3333-3333-3333-333333333333";
        string mold = "44444444-4444-4444-4444-444444444444";
        string claim = "45454545-4545-4545-4545-454545454545";
        foreach (bool withClaim in new[] { false, true })
        {
            var doc = new HeavyShieldSaveDocument();
            var campaign = new HeavyShieldSavedCampaign { Slot = 0, Guid = guid, MoldReceipt = mold };
            var island = new HeavyShieldSavedIsland { Challenge = 0, Land = 0, SnapshotHash = Hash(guid, 0, 0, Json0) };
            if (withClaim)
                island.Claims.Add(new HeavyShieldSavedClaim
                { Receipt = claim, Side = HeavyShieldQuota.Side.Left, Phase = HeavyShieldSavedClaimPhase.PaidTool, NativeId = "row" });
            campaign.Islands.Add(island);
            doc.Campaigns.Add(campaign);
            Require(HeavyShieldSaveCodec.TrySerialize(doc, out string key, out var reason), "paid key: " + reason);
            var env = Setup(key, 8);
            Load(env.Island8);
            Check(SessionUnknown, "guard paid(claim=" + withClaim + "): paid campaign stays Unknown");
            Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold), "guard paid(claim=" + withClaim + "): no quote");
            Prepare(env.Prefs);
            Check(env.Prefs.contents[HeavyShieldSaveSchema.Key] == key, "guard paid(claim=" + withClaim + "): no baseline published");
        }
    }

    private static void GuardLiveEvidence()
    {
        // live pending: a fresh-generation island is usable, then a mold reservation
        // must block any baseline registration on the next pop. The other slots are
        // unvisited in this fixture so no first-connect responsibility is active.
        var pending = Setup(null, 8);
        pending.Island8.isNew = true; pending.Island8.playTimeDays = 0;
        pending.Island0.playTimeDays = 0; pending.Island4.playTimeDays = 0;
        Load(pending.Island8);
        Require(!SessionUnknown, "pending fixture: fresh island usable");
        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.Mold, out _), "pending fixture: mold reserved");
        Load(pending.Island4);
        Check(SessionUnknown, "guard livePending: pending charge keeps the next pop Unknown");
        Check(TryGetKey(pending, out var pendingDoc) && pendingDoc.Campaigns.Count == 0,
            "guard livePending: no baseline rows committed");

        // live completed (never saved): completed receipt and PaidEvidence block it.
        var completed = Setup(null, 8);
        completed.Island8.isNew = true; completed.Island8.playTimeDays = 0;
        completed.Island0.playTimeDays = 0; completed.Island4.playTimeDays = 0;
        Load(completed.Island8);
        Require(!SessionUnknown, "completed fixture: fresh island usable");
        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.Mold, out var moldLease), "completed fixture: reserve");
        Require(HeavyShieldIdentity.CompleteGemPayment(moldLease) == HeavyShieldCommitResult.Applied, "completed fixture: complete");
        Load(completed.Island4);
        Check(SessionUnknown, "guard liveCompleted: unsaved paid receipt keeps the next pop Unknown");
        Check(TryGetKey(completed, out var completedDoc) && completedDoc.Campaigns.Count == 0,
            "guard liveCompleted: no baseline rows committed");
    }

    private static void GuardKeyChanged()
    {
        string actualKey = FixtureKey();
        var env = Setup(actualKey, 8, twoCampaigns: true);
        HeavyShieldPersistence.TickBinding();
        string changed = "{\"version\":2,\"campaigns\":[]}";
        env.Prefs.contents[HeavyShieldSaveSchema.Key] = changed;
        Load(env.Island8);
        Check(SessionUnknown, "guard keychanged: external key change stays Unknown");
        Prepare(env.Prefs);
        Check(env.Prefs.contents[HeavyShieldSaveSchema.Key] == changed, "guard keychanged: key never overwritten");
    }

    private static void GuardFailedKey()
    {
        var env = Setup("not-json", 8);
        Load(env.Island8);
        Check(HeavyShieldIdentity.Current == null, "guard failed key: no session on unparsable key");
        Prepare(env.Prefs);
        Check(env.Prefs.contents[HeavyShieldSaveSchema.Key] == "not-json", "guard failed key: key never overwritten");
    }

    private static void GuardFailedPop()
    {
        string actualKey = FixtureKey();
        var env = Setup(actualKey, 8, twoCampaigns: true);
        var scope = HeavyShieldPersistence.BeginNativeIslandLoad(env.Island8);
        HeavyShieldPersistence.EndNativeIslandLoad(scope, false);
        HeavyShieldPersistence.TickBinding();
        Check(SessionUnknown, "guard false pop: session stays Unknown");
        Prepare(env.Prefs);
        Check(env.Prefs.contents[HeavyShieldSaveSchema.Key] == actualKey, "guard false pop: nothing published");
        Load(env.Island8);
        Prepare(env.Prefs);
        Check(TryGetKey(env, out var after) && after.Campaigns.Find(x => x.Islands.Find(y => y.Land == 8) != null) != null,
            "guard false pop: a later successful pop completes the baseline");
    }

    private static void GuardNestedScope()
    {
        string actualKey = FixtureKey();
        var env = Setup(actualKey, 8, twoCampaigns: true);
        var outer = HeavyShieldPersistence.BeginNativeIslandLoad(env.Island8);
        var inner = HeavyShieldPersistence.BeginNativeIslandLoad(env.Island8);
        HeavyShieldPersistence.EndNativeIslandLoad(outer, true);
        Prepare(env.Prefs);
        Check(env.Prefs.contents[HeavyShieldSaveSchema.Key] == actualKey, "guard nested: non-top scope commits nothing");
        HeavyShieldPersistence.EndNativeIslandLoad(inner, true);
        Prepare(env.Prefs);
        Check(env.Prefs.contents[HeavyShieldSaveSchema.Key] == actualKey,
            "guard nested: changed Prepared copy invalidates the captured upgrade preimage");
        Load(env.Island8);
        Prepare(env.Prefs);
        Check(TryGetKey(env, out var after) && after.Campaigns.Find(x => x.Islands.Find(y => y.Land == 8) != null) != null,
            "guard nested: a later isolated lifecycle completes with a fresh preimage");
    }

    private static void GuardIdentityChanges()
    {
        GuardNoCommit("guard world change", env => Managers.Inst.world.gameLayer = Root().Add<Transform>());
        GuardNoCommit("guard prefs change", env => env.Global.prefs = new PrefsSaveData { Pointer = Ptr() });
        GuardNoCommit("guard island change", env =>
        {
            env.Campaign.CurrentIsland = env.Island4;
            Managers.Inst.game.currentLand = 4;
        });
        GuardNoCommit("guard member change", env => env.Campaign._islands.RemoveAt(8));
    }

    private static void GuardCampaignReplacement()
    {
        string actualKey = FixtureKey();
        var env = Setup(actualKey, 8, twoCampaigns: true);
        var scope = HeavyShieldPersistence.BeginNativeIslandLoad(env.Island8);
        env.Global.campaigns[0] = new CampaignSaveData { Pointer = Ptr(), CurrentIsland = env.Island8 };
        HeavyShieldPersistence.EndNativeIslandLoad(scope, true);
        try { HeavyShieldPersistence.TickBinding(); } catch { }
        Check(SessionUnknown, "guard campaign change: session stays Unknown");
        Prepare(env.Prefs);
        // A replaced native campaign object is a new GUID lineage by design (the old
        // record is reconciled away); what must never appear is an enrolled baseline.
        bool clean = true;
        if (TryGetKey(env, out var doc))
            foreach (var campaign in doc.Campaigns)
                if (campaign.Islands.Exists(x => x.Land == 4 || x.Land == 8)) clean = false;
        Check(clean, "guard campaign change: no baseline rows committed");
    }

    private static void GuardNoCommit(string name, Action<Env> mutate)
    {
        string actualKey = FixtureKey();
        var env = Setup(actualKey, 8, twoCampaigns: true);
        var scope = HeavyShieldPersistence.BeginNativeIslandLoad(env.Island8);
        mutate(env);
        HeavyShieldPersistence.EndNativeIslandLoad(scope, true);
        try { HeavyShieldPersistence.TickBinding(); } catch { }
        Check(SessionUnknown, name + ": session stays Unknown");
        Prepare(env.Prefs);
        Check(env.Prefs.contents[HeavyShieldSaveSchema.Key] == actualKey, name + ": no baseline published");
    }

    private static void GuardCapacity()
    {
        var env = Setup(null, 8);
        while (env.Campaign._islands.Count < 129)
        {
            int land = env.Campaign._islands.Count;
            env.Campaign._islands.Add(MakeIsland(land, 3, "{\"land\":" + land + "}"));
        }
        Load(env.Island8);
        Check(SessionUnknown, "guard capacity(table): out-of-bounds table keeps the first purchase closed");
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
            "guard capacity(table): no first purchase quote");
        Prepare(env.Prefs);
        Check(!TryGetKey(env, out var tableDoc) || tableDoc.Campaigns.Count == 0,
            "guard capacity(table): out-of-bounds table registers nothing");

        string guid = "55555555-5555-5555-5555-555555555555";
        var doc = new HeavyShieldSaveDocument();
        var campaign = new HeavyShieldSavedCampaign { Slot = 0, Guid = guid };
        for (int land = 0; land < 128; land++)
            campaign.Islands.Add(new HeavyShieldSavedIsland { Challenge = 1, Land = land, SnapshotHash = Hash(guid, 1, land, "x") });
        doc.Campaigns.Add(campaign);
        Require(HeavyShieldSaveCodec.TrySerialize(doc, out string key, out _), "capacity doc key");
        var full = Setup(key, 8);
        Load(full.Island8);
        Check(SessionUnknown, "guard capacity(doc): campaign over island cap stays Unknown");
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
            "guard capacity(doc): no first purchase quote");
        Prepare(full.Prefs);
        Check(full.Prefs.contents[HeavyShieldSaveSchema.Key] == key, "guard capacity(doc): no partial baseline");
    }

    // Reviewer counterprobes, as permanent regressions.
    private static void ReviewCounterprobes()
    {
        // P2-2: a frozen non-current member replaced by a new native object must abort.
        {
            var env = Setup(FixtureKey(), 8, twoCampaigns: true);
            var scope = HeavyShieldPersistence.BeginNativeIslandLoad(env.Island8);
            env.Campaign._islands[4] = MakeIsland(4, 4, "{\"land\":4,\"rev\":100}");
            HeavyShieldPersistence.EndNativeIslandLoad(scope, true);
            Prepare(env.Prefs);
            bool committed = TryGetKey(env, out var doc)
                && doc.Campaigns.Find(x => x.Islands.Find(y => y.Land == 4) != null) != null;
            Check(!committed, "probe noncurrent replace: swapped member never committed");
            Check(SessionUnknown, "probe noncurrent replace: first purchase stays closed");
        }
        // P2-3: a world replacement laundered by TickBinding must still abort.
        {
            var env = Setup(FixtureKey(), 8, twoCampaigns: true);
            var scope = HeavyShieldPersistence.BeginNativeIslandLoad(env.Island8);
            Managers.Inst.world.gameLayer = Root().Add<Transform>();
            HeavyShieldPersistence.TickBinding();
            HeavyShieldPersistence.EndNativeIslandLoad(scope, true);
            Prepare(env.Prefs);
            bool committed = TryGetKey(env, out var doc)
                && doc.Campaigns.Find(x => x.Islands.Find(y => y.Land == 8) != null) != null;
            Check(!committed, "probe world then tick: frozen world boundary never laundered");
            Check(SessionUnknown, "probe world then tick: first purchase stays closed");
        }
        // P2-1: exact current with an unreadable missing other must not fall through.
        {
            var env = Setup(Exact0Key(), 0);
            env.Island4.objects = null;
            Load(env.Island0);
            Check(SessionUnknown, "probe exact unreadable other: session stays blocked");
            Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
                "probe exact unreadable other: no first purchase");
        }
        // P2-1: the same at the verified new-generation boundary.
        {
            var env = Setup(null, 8);
            env.Island8.isNew = true; env.Island8.playTimeDays = 0;
            env.Island4.objects = null;
            var generation = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
            HeavyShieldPersistence.EndNativeGeneration(generation, env.Campaign, true);
            HeavyShieldPersistence.TickBinding();
            Check(SessionUnknown, "probe newgen unreadable other: stays blocked");
            Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
                "probe newgen unreadable other: no first purchase");
        }
    }

    // Nothing missing: no first-connect barrier may appear.
    private static void NoEnrollmentNeeded()
    {
        string guid = "66666666-6666-6666-6666-666666666666";
        var doc = new HeavyShieldSaveDocument();
        var campaign = new HeavyShieldSavedCampaign { Slot = 0, Guid = guid };
        campaign.Islands.Add(new HeavyShieldSavedIsland { Challenge = 0, Land = 0, HashKind = HeavyShieldSnapshotFingerprint.CanonicalKind, SnapshotHash = HeavyShieldSnapshotFingerprint.Hash(guid, 0, 0, Json0) });
        campaign.Islands.Add(new HeavyShieldSavedIsland { Challenge = 0, Land = 4, HashKind = HeavyShieldSnapshotFingerprint.CanonicalKind, SnapshotHash = HeavyShieldSnapshotFingerprint.Hash(guid, 0, 4, Json4) });
        campaign.Islands.Add(new HeavyShieldSavedIsland { Challenge = 0, Land = 8, HashKind = HeavyShieldSnapshotFingerprint.CanonicalKind, SnapshotHash = HeavyShieldSnapshotFingerprint.Hash(guid, 0, 8, Json8) });
        doc.Campaigns.Add(campaign);
        Require(HeavyShieldSaveCodec.TrySerialize(doc, out string key, out _), "none-needed key");
        var env = Setup(key, 0);
        Load(env.Island0);
        Check(!SessionUnknown && HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
            "none-needed: no missing visited member keeps the first purchase open");
        Check(env.Prefs.contents[HeavyShieldSaveSchema.Key] == key, "none-needed: load writes nothing");
    }

    // A paid campaign keeps the original rules: the paid Exact path is not frozen.
    private static void PaidNormalPath()
    {
        string guid = "77777777-7777-7777-7777-777777777777";
        string mold = "88888888-8888-8888-8888-888888888888";
        var doc = new HeavyShieldSaveDocument();
        var campaign = new HeavyShieldSavedCampaign { Slot = 0, Guid = guid, MoldReceipt = mold };
        campaign.Islands.Add(new HeavyShieldSavedIsland { Challenge = 0, Land = 0, SnapshotHash = Hash(guid, 0, 0, Json0) });
        doc.Campaigns.Add(campaign);
        Require(HeavyShieldSaveCodec.TrySerialize(doc, out string key, out _), "paid-normal key");
        var env = Setup(key, 0);
        Load(env.Island0);
        Check(!SessionUnknown, "paid-normal: paid exact path is not frozen");
        Check(HeavyShieldIdentity.GetQuotaView().MoldUnlocked, "paid-normal: mold retained");
        Check(HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldLeft),
            "paid-normal: shield still quotable");
    }

    // A blocked first connection recovers on the next correct lifecycle.
    private static void RecoverySuccess()
    {
        string guid = "99999999-9999-9999-9999-999999999999";
        var doc = new HeavyShieldSaveDocument();
        var campaign = new HeavyShieldSavedCampaign { Slot = 0, Guid = guid };
        campaign.Islands.Add(new HeavyShieldSavedIsland { Challenge = 0, Land = 0, SnapshotHash = Hash(guid, 0, 0, Json0) });
        doc.Campaigns.Add(campaign);
        Require(HeavyShieldSaveCodec.TrySerialize(doc, out string key, out _), "recovery key");
        var env = Setup(key, 0);
        env.Island4.objects = null;
        Load(env.Island0);
        Check(SessionUnknown, "recovery: blocked while the visited member is unreadable");
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
            "recovery: first purchase stays closed while blocked");
        env.Island4.objects = new List<IslandSaveData.ObjectData>();
        Load(env.Island0);
        Check(!SessionUnknown, "recovery: next correct load releases the block");
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
            "recovery: prepare barrier still holds the first purchase");
        Prepare(env.Prefs);
        HeavyShieldPersistence.TickBinding();
        Check(TryGetKey(env, out var after) && after.Campaigns.Find(x => x.Guid == guid) is { } registered
            && registered.Islands.Find(x => x.Land == 4) != null && registered.Islands.Find(x => x.Land == 8) != null,
            "recovery: baseline committed on the correct lifecycle");
        Check(HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
            "recovery: first purchase opens after Prepare");
    }

    // Reviewer v2 counterprobes: the first-connect responsibility is established by
    // the unpaid proof and survives temporary key/context changes; only the next
    // correct lifecycle may complete it.
    private static void ReviewV2Counterprobes()
    {
        // (a) generation: the key changes between Begin and End, then is restored.
        {
            var env = Setup(FixtureKey(), 8, twoCampaigns: true);
            env.Island8.isNew = true; env.Island8.playTimeDays = 0;
            var gen = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
            HeavyShieldPersistence.TickBinding();
            string oldKey = env.Prefs.contents[HeavyShieldSaveSchema.Key];
            env.Prefs.contents[HeavyShieldSaveSchema.Key] = "changed";
            HeavyShieldPersistence.EndNativeGeneration(gen, env.Campaign, true);
            env.Prefs.contents[HeavyShieldSaveSchema.Key] = oldKey;
            HeavyShieldPersistence.TickBinding();
            Check(HeavyShieldIdentity.Current != null && SessionUnknown,
                "probe generation key change: responsibility survives the restored key");
            Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
                "probe generation key change: no first purchase quote after restore");
        }
        // (b) exact load: gameLand temporarily diverges in the prefix, restored later.
        {
            var env = Setup(Exact0Key(), 0);
            Managers.Inst.game.currentLand = 4;
            var scope = HeavyShieldPersistence.BeginNativeIslandLoad(env.Island0);
            HeavyShieldPersistence.EndNativeIslandLoad(scope, true);
            Managers.Inst.game.currentLand = 0;
            HeavyShieldPersistence.TickBinding();
            Check(HeavyShieldIdentity.Current != null && SessionUnknown,
                "probe exact load context change: responsibility survives the restored context");
            Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
                "probe exact load context change: no first purchase quote after restore");
        }
    }

    // A blocked generation is released by the next correct generation event.
    private static void GenerationRecovery()
    {
        var env = Setup(FixtureKey(), 8, twoCampaigns: true);
        env.Island8.isNew = true; env.Island8.playTimeDays = 0;
        string oldKey = env.Prefs.contents[HeavyShieldSaveSchema.Key];
        var blocked = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
        HeavyShieldPersistence.TickBinding();
        env.Prefs.contents[HeavyShieldSaveSchema.Key] = "changed";
        HeavyShieldPersistence.EndNativeGeneration(blocked, env.Campaign, true);
        env.Prefs.contents[HeavyShieldSaveSchema.Key] = oldKey;
        HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldIdentity.Current != null && SessionUnknown,
            "generation recovery: key change leaves the first purchase closed");
        var next = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
        HeavyShieldPersistence.EndNativeGeneration(next, env.Campaign, true);
        HeavyShieldPersistence.TickBinding();
        Check(!SessionUnknown, "generation recovery: next correct generation releases");
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
            "generation recovery: prepare barrier still holds");
        Prepare(env.Prefs);
        HeavyShieldPersistence.TickBinding();
        Check(TryGetKey(env, out var after) && after.Campaigns.Find(x => x.Islands.Find(y => y.Land == 4) != null) != null,
            "generation recovery: visited baseline committed");
        Check(HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
            "generation recovery: first purchase opens after Prepare");
    }

    private static string Exact0Key()
    {
        string guid = "11111111-1111-1111-1111-111111111111";
        var doc = new HeavyShieldSaveDocument();
        var campaign = new HeavyShieldSavedCampaign { Slot = 0, Guid = guid };
        campaign.Islands.Add(new HeavyShieldSavedIsland { Challenge = 0, Land = 0, SnapshotHash = Hash(guid, 0, 0, Json0) });
        doc.Campaigns.Add(campaign);
        Require(HeavyShieldSaveCodec.TrySerialize(doc, out string key, out _), "exact0 key");
        return key;
    }

    // The whole authoritative document has a byte cap in addition to per-island
    // counts. A valid near-cap record in another campaign must not be made
    // unpublishable by enrolling the current unpaid campaign's two baselines.
    private static void GuardDocumentByteCapacity()
    {
        int receiptNumber = 10000;
        string Receipt() => (++receiptNumber).ToString("x32").Insert(8, "-").Insert(13, "-").Insert(18, "-").Insert(23, "-");
        string guid = "11111111-1111-1111-1111-111111111111";
        var doc = new HeavyShieldSaveDocument();
        var current = new HeavyShieldSavedCampaign { Slot = 0, Guid = guid };
        current.Islands.Add(new HeavyShieldSavedIsland { Challenge = 0, Land = 0,
            SnapshotHash = Hash(guid, 0, 0, Json0) });
        doc.Campaigns.Add(current);
        var other = new HeavyShieldSavedCampaign
        {
            Slot = 1, Guid = "22222222-2222-2222-2222-222222222222",
            MoldReceipt = Receipt(), LeftExtraReceipt = Receipt(), RightExtraReceipt = Receipt(),
        };
        doc.Campaigns.Add(other);
        bool full = false;
        HeavyShieldSavedClaim tail = null;
        for (int land = 0; land < HeavyShieldSaveSchema.MaxIslands && !full; land++)
        {
            var island = new HeavyShieldSavedIsland { Challenge = 0, Land = land, SnapshotHash = new string('a', 64) };
            other.Islands.Add(island);
            for (int i = 0; i < HeavyShieldSaveSchema.MaxClaimsPerIsland; i++)
            {
                var claim = new HeavyShieldSavedClaim
                {
                    Receipt = Receipt(), Side = (HeavyShieldQuota.Side)(i % 2),
                    Phase = HeavyShieldSavedClaimPhase.Soldier,
                    NativeId = new string('x', HeavyShieldSaveSchema.MaxNativeIdLength), Durability = 0,
                };
                island.Claims.Add(claim);
                if (HeavyShieldSaveCodec.TrySerialize(doc, out _, out _)) continue;
                claim.NativeId = "x";
                if (HeavyShieldSaveCodec.TrySerialize(doc, out _, out _)) tail = claim;
                else island.Claims.Remove(claim);
                full = true;
                break;
            }
        }
        Require(tail != null, "byte capacity fixture has a bounded padding claim");
        for (int length = 2; length <= HeavyShieldSaveSchema.MaxNativeIdLength; length++)
        {
            tail.NativeId = new string('x', length);
            if (HeavyShieldSaveCodec.TrySerialize(doc, out _, out _)) continue;
            tail.NativeId = new string('x', length - 1);
            break;
        }
        Require(HeavyShieldSaveCodec.TrySerialize(doc, out string key, out _), "byte capacity fixture is valid");
        Require(System.Text.Encoding.UTF8.GetByteCount(key) == HeavyShieldSaveSchema.MaxDocumentBytes,
            "byte capacity fixture is exactly at the document cap");
        var env = Setup(key, 8, twoCampaigns: true);
        Load(env.Island8);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var owner = typeof(HeavyShieldPersistence).GetField("_global",
            flags | System.Reflection.BindingFlags.Static).GetValue(null);
        var staged = (HeavyShieldSaveDocument)owner.GetType().GetField("Staged", flags).GetValue(owner);
        var sessions = (System.Collections.IDictionary)owner.GetType().GetField("Sessions", flags).GetValue(owner);
        var otherSession = (HeavyShieldIdentity.Session)sessions[env.Global.campaigns[1].Pointer];
        Check(HeavyShieldIdentity.Current != null && SessionUnknown,
            "byte capacity: unavailable enrollment keeps current purchase closed");
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
            "byte capacity: no Mold quote before any publication");
        Check(staged.Campaigns.Find(x => x.Guid == guid).Islands.Count == 1,
            "byte capacity: no partial baseline staged");
        Check(HeavyShieldSaveCodec.TrySerialize(staged, out string unchanged, out _) && unchanged == key,
            "byte capacity: original staged document remains serializable and unchanged");
        Check(env.Prefs.contents[HeavyShieldSaveSchema.Key] == key,
            "byte capacity: native key unchanged after failed enrollment");
        Prepare(env.Prefs);
        Check(env.Prefs.contents[HeavyShieldSaveSchema.Key] == key,
            "byte capacity: ordinary Prepare preserves original native key");
        Check(env.Prefs.SerializedContents.TryGetValue(HeavyShieldSaveSchema.Key, out string copied) && copied == key,
            "byte capacity: ordinary Prepare still publishes a valid exact native key");
        Check(!otherSession.StageFault,
            "byte capacity: ordinary Prepare does not FreezeAll the other paid campaign");
    }

}
