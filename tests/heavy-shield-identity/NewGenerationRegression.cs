using KingdomEnhancedMod;
using UnityEngine;

// Issue 252 / 286: a first generation target occupies campaign._islands at its
// real slot while its serialized land field is still the raw placeholder 0, or
// is normalized to the slot only inside the same native generation call. The
// generation boundary must freeze the unique pointer->slot identity instead of
// the raw land field, bind the session to that slot, and keep every old record
// untouched. A late land drift on the ordinary save path must never stage.
internal static class NewGenerationRegression
{
    private static long next = 9000000;
    private static int checks, failures;

    private static IntPtr Ptr() => new(++next);
    private static void Check(bool value, string name)
    { checks++; Console.WriteLine((value ? "PASS " : "FAIL ") + name); if (!value) failures++; }
    private static void Require(bool value, string name)
    { if (!value) throw new Exception("fixture: " + name); }

    private const string Json0 = "{\"land\":0,\"rev\":1}";
    private const string Json4 = "{\"land\":4,\"rev\":7}";
    private const string Json8 = "{\"land\":8,\"rev\":3}";
    private const string JsonRaw0 = "{\"land\":0,\"rev\":9}";
    private const string Guid = "55555555-5555-5555-5555-555555555555";
    private const string Mold = "66666666-6666-6666-6666-666666666666";
    private const string Claim = "67676767-6767-6767-6767-676767676767";

    private sealed class Env
    {
        internal GlobalSaveData Global;
        internal CampaignSaveData Campaign;
        internal IslandSaveData Island0, Island4, Island8;
        internal PrefsSaveData Prefs;
    }

    private static IslandSaveData MakeIsland(int land, double days, string json, bool isNew = false)
        => new IslandSaveData { Pointer = Ptr(), land = land, playTimeDays = days, Json = json, isNew = isNew };

    private static Env Setup(string key, int currentLand)
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
        campaign.CurrentLand = 8;
        campaign.CurrentIsland = i8;
        var prefs = new PrefsSaveData { Pointer = Ptr() };
        if (key != null) prefs.contents[HeavyShieldSaveSchema.Key] = key;
        var global = new GlobalSaveData { Pointer = Ptr(), prefs = prefs };
        global.campaigns.Add(campaign);
        GlobalSaveData._loaded = global;
        Managers.Inst = new Managers
        {
            game = new Game { Pointer = Ptr(), currentLand = currentLand },
            world = new World { Pointer = Ptr(), gameLayer = new GameObject(++next, (int)next).Add<Transform>() },
        };
        Time.timeScale = 1; Time.time = 0;
        ModConfig.Enabled.Value = true; ModConfig.HeavyShieldEnabled.Value = true;
        Game.SavingEnabled = true; NetworkBigBoss.HasWorldAuth = true; NetworkBigBoss.IsOnline = false;
        IslandSaveData.isSavingGame = false; IslandSaveData.CurrentlySavingIsland = null;
        HeavyShieldRuntime.AttachSucceeds = true; HeavyShieldRuntime.PreflightSucceeds = true;
        HeavyShieldRuntime.DeferAttach = false; HeavyShieldRuntime.AttachAttempts = 0;
        HeavyShieldRuntime.AttachCalls = HeavyShieldRuntime.DetachCalls = 0;
        return new Env { Global = global, Campaign = campaign, Island0 = i0, Island4 = i4, Island8 = i8, Prefs = prefs };
    }

    private static string Hash(string guid, int challenge, int land, string raw)
        => HeavyShieldSnapshotFingerprint.Hash(guid, challenge, land, raw);

    // An old paid island0 record with a live paid-tool claim. Nothing about the
    // new generation target may replace, relabel or clear it.
    private static string PaidKey()
    {
        var doc = new HeavyShieldSaveDocument();
        var campaign = new HeavyShieldSavedCampaign { Slot = 0, Guid = Guid, MoldReceipt = Mold };
        var row = new HeavyShieldSavedIsland
        {
            Challenge = 0, Land = 0,
            HashKind = HeavyShieldSnapshotFingerprint.CanonicalKind,
            SnapshotHash = Hash(Guid, 0, 0, Json0),
        };
        row.Claims.Add(new HeavyShieldSavedClaim
        {
            Receipt = Claim, Side = HeavyShieldQuota.Side.Left,
            Phase = HeavyShieldSavedClaimPhase.PaidTool, NativeId = "old-paid-native",
        });
        campaign.Islands.Add(row);
        doc.Campaigns.Add(campaign);
        Require(HeavyShieldSaveCodec.TrySerialize(doc, out string key, out var reason), "paid key: " + reason);
        return key;
    }

    // The ordinary save drift case needs an exact authoritative row for island8,
    // otherwise the load itself is already quarantined.
    private static string SaveKey()
    {
        var doc = new HeavyShieldSaveDocument();
        var campaign = new HeavyShieldSavedCampaign { Slot = 0, Guid = Guid, MoldReceipt = Mold };
        var old = new HeavyShieldSavedIsland
        {
            Challenge = 0, Land = 0,
            HashKind = HeavyShieldSnapshotFingerprint.CanonicalKind,
            SnapshotHash = Hash(Guid, 0, 0, Json0),
        };
        old.Claims.Add(new HeavyShieldSavedClaim
        {
            Receipt = Claim, Side = HeavyShieldQuota.Side.Left,
            Phase = HeavyShieldSavedClaimPhase.PaidTool, NativeId = "old-paid-native",
        });
        campaign.Islands.Add(old);
        campaign.Islands.Add(new HeavyShieldSavedIsland
        {
            Challenge = 0, Land = 8,
            HashKind = HeavyShieldSnapshotFingerprint.CanonicalKind,
            SnapshotHash = Hash(Guid, 0, 8, Json8),
        });
        doc.Campaigns.Add(campaign);
        Require(HeavyShieldSaveCodec.TrySerialize(doc, out string key, out var reason), "save key: " + reason);
        return key;
    }

    private static void MakeVirginRawZero(Env env)
    {
        env.Island8.land = 0;
        env.Island8.Json = JsonRaw0;
        env.Island8.isNew = true;
        env.Island8.playTimeDays = 0;
    }

    private static void MakeVirginNormalized(Env env)
    {
        env.Island8.land = 8;
        env.Island8.Json = Json8;
        env.Island8.isNew = true;
        env.Island8.playTimeDays = 0;
    }

    private static void Generate(Env env)
    {
        var generation = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
        HeavyShieldPersistence.EndNativeGeneration(generation, env.Campaign, true);
        HeavyShieldPersistence.TickBinding();
    }

    private static void Load(IslandSaveData island)
    {
        var scope = HeavyShieldPersistence.BeginNativeIslandLoad(island);
        HeavyShieldPersistence.EndNativeIslandLoad(scope, true);
        HeavyShieldPersistence.TickBinding();
    }

    private static void Prepare(PrefsSaveData prefs)
    {
        var scope = HeavyShieldPersistence.BeginNativePrefsPrepare(prefs);
        bool success = false;
        try { prefs.CopyToSerializedEntries(); success = true; }
        finally { HeavyShieldPersistence.EndNativePrefsPrepare(scope, success); }
    }

    private static bool TryGetKey(Env env, out HeavyShieldSaveDocument doc)
    {
        doc = null;
        return env.Prefs.contents.TryGetValue(HeavyShieldSaveSchema.Key, out string raw)
            && HeavyShieldSaveCodec.TryParse(raw, out doc, out _);
    }

    internal static void Run()
    {
        PaidRawZeroEnd();
        PaidNormalizedEnd();
        UnpaidRawZeroFirstEnrollment();
        RawZeroStaleCurrentLandFailsClosed();
        DivergentLandRejected();
        SaveFieldDriftRejected();
        RawZeroSavePathStaysRejected();
        Console.WriteLine($"GENERATION-SLOT regressions: {checks} checks, {failures} failures");
        if (failures != 0) throw new Exception("Generation-slot regressions failed: " + failures);
    }

    // The paid campaign proves its entitlement on island0 while the generation
    // target still reports raw land0. The old land0 row must not be mistaken for
    // the target, and the session must bind slot 8 before any quota is touched.
    private static void PaidRawZeroEnd()
    {
        string key = PaidKey();
        var env = Setup(key, 8);
        MakeVirginRawZero(env);
        Generate(env);
        var s = HeavyShieldIdentity.Current;
        Check(s != null && !s.Unknown && s.Land == 8,
            "paid raw0: generation binds the unique slot instead of land0");
        Check(s != null && s.Slot == 0 && s.Guid == Guid,
            "paid raw0: owner campaign identity survives the generation boundary");
        var view = HeavyShieldIdentity.GetQuotaView();
        Check(view.MoldUnlocked && view.LeftOccupied == 0 && view.RightOccupied == 0,
            "paid raw0: campaign entitlement restores without old island seats");
        Prepare(env.Prefs);
        HeavyShieldPersistence.TickBinding();
        Check(env.Prefs.contents[HeavyShieldSaveSchema.Key] == key,
            "paid raw0: old paid land0 record stays byte-identical");
        if (TryGetKey(env, out var doc))
        {
            var row = doc.Campaigns.Find(x => x.Guid == Guid)?.Islands.Find(x => x.Land == 0);
            Check(row != null && row.SnapshotHash == Hash(Guid, 0, 0, Json0)
                && row.Claims.Count == 1 && row.Claims[0].Receipt == Claim
                && row.Claims[0].NativeId == "old-paid-native",
                "paid raw0: old land0 hash, claim and native id are untouched");
        }
        else Check(false, "paid raw0: key still parses");
        Check(HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldLeft),
            "paid raw0: bound slot quotes the first settlement after Prepare");
    }

    // Native normalizes island.land to the slot during the same generation call.
    // This is the existing-expected shape and must stay green.
    private static void PaidNormalizedEnd()
    {
        string key = PaidKey();
        var env = Setup(key, 8);
        MakeVirginNormalized(env);
        Generate(env);
        var s = HeavyShieldIdentity.Current;
        Check(s != null && !s.Unknown && s.Land == 8,
            "paid normalized: generation binds the normalized slot");
        Prepare(env.Prefs);
        HeavyShieldPersistence.TickBinding();
        Check(env.Prefs.contents[HeavyShieldSaveSchema.Key] == key,
            "paid normalized: old paid land0 record stays byte-identical");
        Check(HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldLeft),
            "paid normalized: bound slot quotes the first settlement after Prepare");
    }

    // Unpaid first connect: the visited missing 0/4 baselines enroll at the
    // generation boundary, but the virgin target slot 8 is never pre-occupied.
    private static void UnpaidRawZeroFirstEnrollment()
    {
        var env = Setup(null, 8);
        MakeVirginRawZero(env);
        Generate(env);
        var s = HeavyShieldIdentity.Current;
        Check(s != null && !s.Unknown && s.Land == 8,
            "unpaid raw0: generation binds slot 8 before enrollment");
        Prepare(env.Prefs);
        HeavyShieldPersistence.TickBinding();
        Check(TryGetKey(env, out var doc), "unpaid raw0: published key parses");
        var campaign = doc?.Campaigns.Find(x => x.Guid == s?.Guid);
        Check(campaign != null && campaign.Islands.Find(x => x.Land == 0) != null
            && campaign.Islands.Find(x => x.Land == 4) != null
            && campaign.Islands.Find(x => x.Land == 8) == null,
            "unpaid raw0: visited 0/4 enroll, virgin slot 8 stays unoccupied");
        Check(HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
            "unpaid raw0: first Mold quote opens only after Prepare");
    }

    // Native has not switched currentLand to the slot yet: the enrollment commit
    // gate must stay closed rather than publish under a guessed land.
    private static void RawZeroStaleCurrentLandFailsClosed()
    {
        var env = Setup(null, 0);
        MakeVirginRawZero(env);
        Generate(env);
        var s = HeavyShieldIdentity.Current;
        Check(s == null || s.Unknown, "raw0 stale currentLand: session stays Unknown");
        Prepare(env.Prefs);
        Check(!env.Prefs.contents.ContainsKey(HeavyShieldSaveSchema.Key),
            "raw0 stale currentLand: nothing is published");
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold),
            "raw0 stale currentLand: no first purchase quote");
    }

    // A non-zero land that is not the frozen slot is never the same island.
    private static void DivergentLandRejected()
    {
        var env = Setup(null, 4);
        MakeVirginRawZero(env);
        env.Island8.land = 4;
        Generate(env);
        var s = HeavyShieldIdentity.Current;
        Check(s == null || s.Unknown, "divergent land: generation fails closed");
        Check(!env.Prefs.contents.ContainsKey(HeavyShieldSaveSchema.Key)
            || env.Prefs.contents[HeavyShieldSaveSchema.Key] == null,
            "divergent land: no baseline is published");
    }

    // The marker accepted the island while its field still equaled the scope
    // land; the field then drifts before the normal finalizer. Nothing may stage.
    private static void SaveFieldDriftRejected()
    {
        string key = SaveKey();
        var env = Setup(key, 8);
        MakeVirginNormalized(env);
        Load(env.Island8);
        Check(HeavyShieldIdentity.Current != null && !HeavyShieldIdentity.Current.Unknown,
            "save drift: exact load opens the island");
        IslandSaveData.isSavingGame = true; IslandSaveData.CurrentlySavingIsland = env.Island8;
        var scope = HeavyShieldPersistence.BeginNativeIslandSave(0, 8, 0);
        HeavyShieldPersistence.ObserveNativeIslandCaptured(scope, env.Island8);
        env.Island8.land = 0;
        env.Island8.Json = JsonRaw0;
        HeavyShieldPersistence.EndNativeIslandSave(scope, true);
        IslandSaveData.isSavingGame = false; IslandSaveData.CurrentlySavingIsland = null;
        Prepare(env.Prefs);
        Check(env.Prefs.contents[HeavyShieldSaveSchema.Key] == key,
            "save drift: pre-finalizer land drift never stages a mixed row");
    }

    // While native still reports raw land0, the ordinary save boundary cannot
    // match the slot-bound session and must keep the old record unchanged.
    private static void RawZeroSavePathStaysRejected()
    {
        string key = PaidKey();
        var env = Setup(key, 8);
        MakeVirginRawZero(env);
        Generate(env);
        Managers.Inst.game.currentLand = 0;
        IslandSaveData.isSavingGame = true; IslandSaveData.CurrentlySavingIsland = env.Island8;
        var scope = HeavyShieldPersistence.BeginNativeIslandSave(0, 0, 0);
        HeavyShieldPersistence.ObserveNativeIslandCaptured(scope, env.Island8);
        HeavyShieldPersistence.EndNativeIslandSave(scope, true);
        IslandSaveData.isSavingGame = false; IslandSaveData.CurrentlySavingIsland = null;
        Prepare(env.Prefs);
        Check(env.Prefs.contents[HeavyShieldSaveSchema.Key] == key,
            "raw0 save path: slot-bound session refuses the raw land0 capture");
    }
}
