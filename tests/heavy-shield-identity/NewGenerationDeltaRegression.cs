using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

// Delta for issue 252/286 review: native wrapper aliasing, read-before-effect
// generation finalizer, key/scope drift, nested closure, normalized first save
// and raw0-still save rejection. Synthetic fixture only; no native timing claim.
internal static class NewGenerationDeltaRegression
{
    private static long next = 12000000;
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
    private const string Guid = "77777777-7777-7777-7777-777777777777";
    private const string Mold = "88888888-8888-8888-8888-888888888888";
    private const string Claim = "89898989-8989-8989-8989-898989898989";

    private sealed class Env
    {
        internal GlobalSaveData Global; internal CampaignSaveData Campaign;
        internal IslandSaveData Island0, Island4, Island8; internal PrefsSaveData Prefs;
    }

    private static IslandSaveData Island(int land, double days, string json, bool isNew = false)
        => new IslandSaveData { Pointer = Ptr(), land = land, playTimeDays = days, Json = json, isNew = isNew };

    private static Env Setup(string key, int currentLand)
    {
        var campaign = new CampaignSaveData { Pointer = Ptr() };
        var i0 = Island(0, 12, Json0); var i4 = Island(4, 5, Json4); var i8 = Island(8, 3, Json8);
        campaign._islands.Add(i0);
        for (int i = 1; i <= 3; i++) campaign._islands.Add(Island(0, 0, "{}"));
        campaign._islands.Add(i4);
        for (int i = 5; i <= 7; i++) campaign._islands.Add(Island(0, 0, "{}"));
        campaign._islands.Add(i8);
        campaign.CurrentLand = 8; campaign.CurrentIsland = i8;
        var prefs = new PrefsSaveData { Pointer = Ptr() };
        if (key != null) prefs.contents[HeavyShieldSaveSchema.Key] = key;
        var global = new GlobalSaveData { Pointer = Ptr(), prefs = prefs };
        global.campaigns.Add(campaign); GlobalSaveData._loaded = global;
        Managers.Inst = new Managers
        { game = new Game { Pointer = Ptr(), currentLand = currentLand },
          world = new World { Pointer = Ptr(), gameLayer = new GameObject(++next, (int)next).Add<Transform>() } };
        Time.timeScale = 1; Time.time = 0;
        ModConfig.Enabled.Value = ModConfig.HeavyShieldEnabled.Value = true;
        Game.SavingEnabled = true; NetworkBigBoss.HasWorldAuth = true; NetworkBigBoss.IsOnline = false;
        IslandSaveData.isSavingGame = false; IslandSaveData.CurrentlySavingIsland = null;
        HeavyShieldRuntime.AttachSucceeds = true; HeavyShieldRuntime.PreflightSucceeds = true;
        HeavyShieldRuntime.DeferAttach = false; HeavyShieldRuntime.AttachAttempts = 0;
        HeavyShieldRuntime.AttachCalls = HeavyShieldRuntime.DetachCalls = 0;
        return new Env { Global = global, Campaign = campaign, Island0 = i0, Island4 = i4, Island8 = i8, Prefs = prefs };
    }

    private static string Hash(string g, int c, int l, string raw) => HeavyShieldSnapshotFingerprint.Hash(g, c, l, raw);

    private static string Key(bool claim)
    {
        var doc = new HeavyShieldSaveDocument();
        var c = new HeavyShieldSavedCampaign { Slot = 0, Guid = Guid, MoldReceipt = Mold };
        var row = new HeavyShieldSavedIsland { Challenge = 0, Land = 0,
            HashKind = HeavyShieldSnapshotFingerprint.CanonicalKind, SnapshotHash = Hash(Guid, 0, 0, Json0) };
        if (claim) row.Claims.Add(new HeavyShieldSavedClaim { Receipt = Claim,
            Side = HeavyShieldQuota.Side.Left, Phase = HeavyShieldSavedClaimPhase.PaidTool, NativeId = "old-native" });
        c.Islands.Add(row); doc.Campaigns.Add(c);
        Require(HeavyShieldSaveCodec.TrySerialize(doc, out string key, out var r), "key: " + r);
        return key;
    }

    private static string KeyWith8()
    {
        var doc = new HeavyShieldSaveDocument();
        var c = new HeavyShieldSavedCampaign { Slot = 0, Guid = Guid, MoldReceipt = Mold };
        c.Islands.Add(new HeavyShieldSavedIsland { Challenge = 0, Land = 0,
            HashKind = HeavyShieldSnapshotFingerprint.CanonicalKind, SnapshotHash = Hash(Guid, 0, 0, Json0) });
        c.Islands.Add(new HeavyShieldSavedIsland { Challenge = 0, Land = 8,
            HashKind = HeavyShieldSnapshotFingerprint.CanonicalKind, SnapshotHash = Hash(Guid, 0, 8, Json8) });
        doc.Campaigns.Add(c);
        Require(HeavyShieldSaveCodec.TrySerialize(doc, out string key, out var r), "key8: " + r);
        return key;
    }

    private static void Virgin(Env e) { e.Island8.land = 0; e.Island8.Json = JsonRaw0; e.Island8.isNew = true; e.Island8.playTimeDays = 0; }
    private static void Load(IslandSaveData i)
    { var s = HeavyShieldPersistence.BeginNativeIslandLoad(i); HeavyShieldPersistence.EndNativeIslandLoad(s, true); HeavyShieldPersistence.TickBinding(); }
    private static void Prepare(PrefsSaveData p)
    { var s = HeavyShieldPersistence.BeginNativePrefsPrepare(p); bool ok = false;
      try { p.CopyToSerializedEntries(); ok = true; } finally { HeavyShieldPersistence.EndNativePrefsPrepare(s, ok); } }
    private static void EndGeneration(CampaignSaveData c) { var g = HeavyShieldPersistence.BeginNativeGeneration(c); HeavyShieldPersistence.EndNativeGeneration(g, c, true); HeavyShieldPersistence.TickBinding(); }
    private static string Raw(Env e) => e.Prefs.contents.TryGetValue(HeavyShieldSaveSchema.Key, out var r) ? r : null;
    private static void SetOwnerGeneration(long value)
    {
        var owner = typeof(HeavyShieldPersistence).GetField("_global", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        owner?.GetType().GetField("Generation", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(owner, value);
    }

    internal static void Run()
    {
        AliasPositiveAndNegative();
        RawZeroToNormalized();
        DriftMatrix();
        NestedAndDuplicate();
        NormalizedSaveRoundtrip();
        RawZeroStillSaveRejected();
        SameTargetLiveResponsibility();
        SameTargetKnownRowArmed();
        ZeroWorldRejected();
        FirstOwnedPreflightAccepted();
        OriginAnchorRejected();
        OriginSessionsOnlyRejected();
        Console.WriteLine($"GENERATION-DELTA regressions: {checks} checks, {failures} failures");
        if (failures != 0) throw new Exception("Generation delta regressions failed: " + failures);
    }

    private static void AliasPositiveAndNegative()
    {
        var env = Setup(Key(false), 8); Virgin(env);
        var alias = new CampaignSaveData { Pointer = env.Campaign.Pointer };
        Require(ReferenceEquals(alias._islands, env.Campaign._islands) && alias.CurrentIsland == env.Island8,
            "alias shares native-backed table");
        var gen = HeavyShieldPersistence.BeginNativeGeneration(alias);
        env.Island8.land = 8; env.Island8.Json = Json8;
        HeavyShieldPersistence.EndNativeGeneration(gen, alias, true); HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldIdentity.Current != null && !HeavyShieldIdentity.Current.Unknown
            && HeavyShieldIdentity.Current.Land == 8, "same native Pointer different wrapper binds the slot");

        var bad = Setup(Key(false), 8); Virgin(bad); HeavyShieldPersistence.TickBinding();
        var before = HeavyShieldIdentity.Current; bool beforeUnknown = before?.Unknown ?? true;
        var foreign = new CampaignSaveData { Pointer = Ptr() };
        var badGen = HeavyShieldPersistence.BeginNativeGeneration(bad.Campaign);
        HeavyShieldPersistence.EndNativeGeneration(badGen, foreign, true); HeavyShieldPersistence.TickBinding();
        Check(ReferenceEquals(HeavyShieldIdentity.Current, before) && (before?.Unknown ?? true) == beforeUnknown,
            "different Pointer wrapper never binds or mutates the scoped session");
    }

    private static void RawZeroToNormalized()
    {
        var env = Setup(Key(false), 8); Virgin(env);
        string old = Raw(env);
        var gen = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
        env.Island8.land = 8; env.Island8.Json = Json8; // explicit fixture-only normalization
        HeavyShieldPersistence.EndNativeGeneration(gen, env.Campaign, true); HeavyShieldPersistence.TickBinding();
        var s = HeavyShieldIdentity.Current;
        Check(s != null && !s.Unknown && s.Land == 8, "true prefix raw0 -> End normalized binds slot");
        Prepare(env.Prefs); HeavyShieldPersistence.TickBinding();
        Check(Raw(env) == old, "true raw0->8 keeps old paid land0 bytes");
        Check(HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldLeft),
            "true raw0->8 quote after Prepare");
    }

    private sealed class Snapshot
    {
        internal HeavyShieldIdentity.Session Session; internal bool Unknown; internal string Key;
        internal int Claims; internal bool Mold; internal int Staged;
    }

    private static Snapshot Snap(Env e)
    {
        var s = HeavyShieldIdentity.Current;
        var staged = typeof(HeavyShieldPersistence).GetField("_global", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        int count = 0;
        if (staged != null)
            count = ((System.Collections.IDictionary)staged.GetType().GetField("StagedVersions",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(staged))?.Count ?? 0;
        return new Snapshot { Session = s, Unknown = s?.Unknown ?? true, Key = Raw(e),
            Claims = s?.Claims.Count ?? -1, Mold = HeavyShieldIdentity.GetQuotaView().MoldUnlocked, Staged = count };
    }

    private static void CheckRejected(string name, Env e, Snapshot before)
    {
        var after = Snap(e);
        Check(ReferenceEquals(after.Session, before.Session) && after.Unknown == before.Unknown
            && after.Key == before.Key && after.Claims == before.Claims && after.Mold == before.Mold
            && after.Staged == before.Staged, name + ": scope rejected without session/quota/stage/key change");
    }

    private static void DriftMatrix()
    {
        foreach (bool paid in new[] { false, true })
        {
            string tag = paid ? "paid" : "unpaid";
            Drift(tag + " gameLand", paid, e => Managers.Inst.game.currentLand = 4);
            Drift(tag + " prefs", paid, e => e.Global.prefs = new PrefsSaveData { Pointer = Ptr() });
            Drift(tag + " key", paid, e => e.Prefs.contents[HeavyShieldSaveSchema.Key] = "{\"v\":999}");
            Drift(tag + " world", paid, e => Managers.Inst.world.gameLayer = new GameObject(++next, (int)next).Add<Transform>());
            Drift(tag + " challenge", paid, e => e.Global.currentChallenge = 1);
            Drift(tag + " campaign member", paid, e =>
            {
                var other = new CampaignSaveData { Pointer = Ptr() };
                other._islands.Add(e.Island4); e.Global.campaigns.Add(other); e.Global.currentCampaign = 1;
            });
            Drift(tag + " unique slot", paid, e => e.Campaign._islands.Add(e.Island8));
            Drift(tag + " owner generation", paid, e => { var g = e.Global; SetOwnerGeneration(999999); });
        }
    }

    private static void Drift(string name, bool paid, Action<Env> mutate)
    {
        var env = Setup(paid ? Key(false) : null, 8);
        if (paid)
        {
            // Authoritative paid key for this island keeps the pre-generation
            // session healthy; an extra claim-free row is enough for the guard.
            env.Prefs.contents[HeavyShieldSaveSchema.Key] = KeyWith8();
            Load(env.Island8);
        }
        else HeavyShieldPersistence.TickBinding();
        Virgin(env);
        var gen = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
        mutate(env);
        var before = Snap(env); // intentionally mutated preimage; End must not add more
        HeavyShieldPersistence.EndNativeGeneration(gen, env.Campaign, true);
        CheckRejected(name, env, before);
    }

    private static void NestedAndDuplicate()
    {
        var env = Setup(null, 8);
        env.Island8.land = 8; env.Island8.Json = Json8;
        env.Island8.isNew = true; env.Island8.playTimeDays = 0;
        var a = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
        var b = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
        HeavyShieldPersistence.EndNativeGeneration(b, env.Campaign, true); HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldIdentity.Current != null && !HeavyShieldIdentity.Current.Unknown,
            "top scope commits the normalized generation");
        Prepare(env.Prefs); HeavyShieldPersistence.TickBinding();
        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.Mold, out _),
            "nested fixture reserves a live Mold");
        int pending = HeavyShieldIdentity.Current.Pending.Count;
        var quota = HeavyShieldIdentity.GetQuotaView();
        HeavyShieldPersistence.EndNativeGeneration(a, env.Campaign, true); // stale older scope
        HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldIdentity.Current != null && !HeavyShieldIdentity.Current.Unknown
            && HeavyShieldIdentity.Current.Pending.Count == pending
            && HeavyShieldIdentity.GetQuotaView().MoldUnlocked == quota.MoldUnlocked,
            "stale older close cannot reset live quota/claims");
        HeavyShieldPersistence.EndNativeGeneration(b, env.Campaign, true); // duplicate same object
        HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldIdentity.Current.Pending.Count == pending,
            "duplicate same-object close is idempotent");
    }

    private static void NormalizedSaveRoundtrip()
    {
        var env = Setup(Key(false), 8); Virgin(env);
        var gen = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
        env.Island8.land = 8; env.Island8.Json = Json8; // fixture simulates native normalization only
        HeavyShieldPersistence.EndNativeGeneration(gen, env.Campaign, true); HeavyShieldPersistence.TickBinding();
        bool ready = HeavyShieldIdentity.Current != null && !HeavyShieldIdentity.Current.Unknown
            && HeavyShieldIdentity.Current.Land == 8;
        Check(ready, "normalized generation ready");
        if (!ready) return;
        IslandSaveData.isSavingGame = true; IslandSaveData.CurrentlySavingIsland = env.Island8;
        var save = HeavyShieldPersistence.BeginNativeIslandSave(0, 8, 0);
        HeavyShieldPersistence.ObserveNativeIslandCaptured(save, env.Island8);
        HeavyShieldPersistence.EndNativeIslandSave(save, true);
        IslandSaveData.isSavingGame = false; IslandSaveData.CurrentlySavingIsland = null;
        Prepare(env.Prefs);
        string key = Raw(env);
        Check(HeavyShieldSaveCodec.TryParse(key, out var doc, out _)
            && doc.Campaigns[0].Islands.Find(x => x.Land == 8)?.SnapshotHash == Hash(Guid, 0, 8, Json8)
            && doc.Campaigns[0].Islands.Find(x => x.Land == 0)?.SnapshotHash == Hash(Guid, 0, 0, Json0),
            "first normalized save stages B row8 and preserves A land0 row");

        // Cold load against fresh global/island/pointers, never the live fixture.
        var coldIsland = Island(8, 0, Json8);
        var coldCampaign = new CampaignSaveData { Pointer = Ptr(), CurrentLand = 8, CurrentIsland = coldIsland };
        coldCampaign._islands.Add(Island(0, 12, Json0));
        for (int i = 1; i <= 7; i++) coldCampaign._islands.Add(Island(0, 0, "{}"));
        coldCampaign._islands.Add(coldIsland);
        var coldPrefs = new PrefsSaveData { Pointer = Ptr() };
        // Rebuild the native preimage from the real Prepare output before the
        // first cold load; no second Load may mask a missing preimage.
        foreach (var row in env.Prefs.SerializedContents)
            coldPrefs.contents[row.Key] = row.Value;
        coldPrefs.CopyToSerializedEntries();
        var coldGlobal = new GlobalSaveData { Pointer = Ptr(), prefs = coldPrefs };
        coldGlobal.campaigns.Add(coldCampaign); GlobalSaveData._loaded = coldGlobal;
        Managers.Inst = new Managers { game = new Game { Pointer = Ptr(), currentLand = 8 },
            world = new World { Pointer = Ptr(), gameLayer = new GameObject(++next, (int)next).Add<Transform>() } };
        Load(coldIsland);
        Check(HeavyShieldIdentity.Current != null && !HeavyShieldIdentity.Current.Unknown
            && HeavyShieldIdentity.Current.Land == 8 && HeavyShieldIdentity.GetQuotaView().MoldUnlocked,
            "cold load restores normalized island8 without Unknown");
        Check(HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldLeft),
            "cold load keeps the slot quote boundary");
    }

    private static void RawZeroStillSaveRejected()
    {
        var env = Setup(Key(false), 8); Virgin(env); string old = Raw(env);
        var gen = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
        HeavyShieldPersistence.EndNativeGeneration(gen, env.Campaign, true); HeavyShieldPersistence.TickBinding();
        bool bound = HeavyShieldIdentity.Current != null && HeavyShieldIdentity.Current.Land == 8;
        Check(bound, "raw0 generation bound slot before save negative");
        if (!bound) return;
        IslandSaveData.isSavingGame = true; IslandSaveData.CurrentlySavingIsland = env.Island8;
        var save = HeavyShieldPersistence.BeginNativeIslandSave(0, 8, 0);
        HeavyShieldPersistence.ObserveNativeIslandCaptured(save, env.Island8); // island.land still 0
        HeavyShieldPersistence.EndNativeIslandSave(save, true);
        IslandSaveData.isSavingGame = false; IslandSaveData.CurrentlySavingIsland = null;
        Prepare(env.Prefs);
        Check(Raw(env) == old, "raw0-still: gameLand8/request8/field0 rejected by the land gate");
    }

    private static void SameTargetLiveResponsibility()
    {
        var env = Setup(Key(false), 8); Virgin(env);
        EndGeneration(env.Campaign);
        Prepare(env.Prefs); HeavyShieldPersistence.TickBinding();
        Require(HeavyShieldIdentity.Current != null && !HeavyShieldIdentity.Current.Unknown,
            "same-target fixture generation");
        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.ShieldLeft, out var lease),
            "same-target live pending");
        var origin = HeavyShieldIdentity.Current;
        bool armedUnknown = origin.Unknown;
        int pending = origin.Pending.Count;
        var view = HeavyShieldIdentity.GetQuotaView();
        int claims = origin.Claims.Count; string key = Raw(env);
        int staged = Snap(env).Staged;
        Require(view.ShopOccupied, "same-target fixture occupies the shop slot");
        env.Island8.land = 8; env.Island8.Json = Json8;
        env.Island8.isNew = true; env.Island8.playTimeDays = 0;
        var gen = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
        HeavyShieldPersistence.EndNativeGeneration(gen, env.Campaign, true);
        Check(ReferenceEquals(HeavyShieldIdentity.Current, origin)
            && origin.Unknown == armedUnknown
            && HeavyShieldIdentity.ValidatePurchase(lease)
            && origin.Pending.Count == pending && origin.Claims.Count == claims
            && HeavyShieldIdentity.GetQuotaView().ShopOccupied == view.ShopOccupied
            && HeavyShieldIdentity.GetQuotaView().LeftLimit == view.LeftLimit
            && Raw(env) == key && Snap(env).Staged == staged,
            "same-target live responsibility keeps session/quota/claims/stage/key");
    }

    private static void ZeroWorldRejected()
    {
        foreach (bool paid in new[] { true, false })
            foreach (bool normalized in new[] { true, false })
            {
                var env = Setup(paid ? Key(false) : null, 8);
                if (paid) Load(env.Island8);
                else HeavyShieldPersistence.TickBinding();
                if (normalized) { env.Island8.land = 8; env.Island8.Json = Json8; }
                else Virgin(env);
                env.Island8.isNew = true; env.Island8.playTimeDays = 0;
                Managers.Inst.world.gameLayer = null; // WorldKey == 0
                var gen = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
                var before = Snap(env);
                HeavyShieldPersistence.EndNativeGeneration(gen, env.Campaign, true);
                CheckRejected($"world0 {(paid ? "paid" : "unpaid")} {(normalized ? "normalized" : "raw0")}",
                    env, before);
            }
    }

    private static Env PreflightFixture()
    {
        var env = Setup(null, 0);
        env.Campaign._islands.Clear(); env.Campaign._islands.Add(env.Island8);
        env.Campaign.CurrentLand = 0; env.Campaign.CurrentIsland = env.Island8;
        env.Island8.land = 0; env.Island8.Json = JsonRaw0;
        env.Island8.isNew = true; env.Island8.playTimeDays = 0;
        HeavyShieldPersistence.TickBinding();
        HeavyShieldIdentity.Current.Unknown = false;
        return env;
    }

    private static void FirstOwnedPreflightAccepted()
    {
        var env = PreflightFixture();
        var origin = HeavyShieldIdentity.Current;
        var gen = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
        Check(HeavyShieldPersistence.PreflightKeyWrite(HeavyShieldIdentity.Current),
            "first owned preflight really writes the missing key");
        HeavyShieldPersistence.EndNativeGeneration(gen, env.Campaign, true);
        HeavyShieldPersistence.TickBinding();
        Check(HeavyShieldIdentity.Current != null && !HeavyShieldIdentity.Current.Unknown
            && !ReferenceEquals(HeavyShieldIdentity.Current, origin) && Raw(env) != null,
            "same-owner Missing->first preflight 0->1 commits the generation");

        var external = PreflightFixture();
        var externalGen = HeavyShieldPersistence.BeginNativeGeneration(external.Campaign);
        var externalOrigin = HeavyShieldIdentity.Current; bool unknown = externalOrigin.Unknown;
        external.Prefs.contents[HeavyShieldSaveSchema.Key] = "{\"external\":1}";
        HeavyShieldPersistence.EndNativeGeneration(externalGen, external.Campaign, true);
        HeavyShieldPersistence.TickBinding();
        Check(ReferenceEquals(HeavyShieldIdentity.Current, externalOrigin)
            && externalOrigin.Unknown == unknown && Raw(external) == "{\"external\":1}",
            "external raw on missing-key generation is rejected without side effects");
    }

    private static void OriginAnchorRejected()
    {
        var env = Setup(Key(false), 8); Virgin(env);
        HeavyShieldPersistence.TickBinding();
        var gen = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
        var origin = HeavyShieldIdentity.Current;
        var other = new HeavyShieldIdentity.Session
        {
            Global = origin.Global, Campaign = origin.Campaign,
            OwnerGeneration = origin.OwnerGeneration, Guid = origin.Guid,
            Slot = origin.Slot, Land = 8, Challenge = origin.Challenge,
            World = origin.World, Phase = origin.Phase, Unknown = true,
        };
        var owner = typeof(HeavyShieldPersistence).GetField("_global", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        var sessions = (System.Collections.IDictionary)owner.GetType().GetField("Sessions",
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner);
        sessions[origin.Campaign] = other;
        HeavyShieldIdentity.Install(other);
        env.Island8.land = 8; env.Island8.Json = Json8;
        string key = Raw(env);
        HeavyShieldPersistence.EndNativeGeneration(gen, env.Campaign, true);
        Check(Raw(env) == key && HeavyShieldIdentity.Current != null && HeavyShieldIdentity.Current.Unknown,
            "replaced origin session rejects the detached draft");
    }

    private static void SameTargetKnownRowArmed()
    {
        var env = Setup(KeyWith8(), 8);
        Load(env.Island8);
        Require(HeavyShieldIdentity.Current != null && !HeavyShieldIdentity.Current.Unknown,
            "known-row armed fixture loads exact");
        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.ShieldLeft, out var lease),
            "known-row armed reserve");
        var origin = HeavyShieldIdentity.Current;
        bool unknown = origin.Unknown; int pending = origin.Pending.Count;
        string key = Raw(env); int staged = Snap(env).Staged;
        env.Island8.isNew = true; env.Island8.playTimeDays = 0;
        var gen = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
        HeavyShieldPersistence.EndNativeGeneration(gen, env.Campaign, true);
        Check(ReferenceEquals(HeavyShieldIdentity.Current, origin) && origin.Unknown == unknown
            && HeavyShieldIdentity.ValidatePurchase(lease) && origin.Pending.Count == pending
            && Raw(env) == key && Snap(env).Staged == staged,
            "known target row + armed lease beats the staged-row quarantine");
    }

    private static void OriginSessionsOnlyRejected()
    {
        var env = Setup(Key(false), 8); Virgin(env);
        EndGeneration(env.Campaign); Prepare(env.Prefs); HeavyShieldPersistence.TickBinding();
        var origin = HeavyShieldIdentity.Current;
        Require(origin != null && !origin.Unknown, "sessions-only fixture origin");
        var gen = HeavyShieldPersistence.BeginNativeGeneration(env.Campaign);
        var other = new HeavyShieldIdentity.Session
        {
            Global = origin.Global, Campaign = origin.Campaign,
            OwnerGeneration = origin.OwnerGeneration, Guid = origin.Guid,
            Slot = origin.Slot, Land = 8, Challenge = origin.Challenge,
            World = origin.World, Phase = origin.Phase, Unknown = false,
        };
        var owner = typeof(HeavyShieldPersistence).GetField("_global", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        var sessions = (System.Collections.IDictionary)owner.GetType().GetField("Sessions",
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner);
        sessions[origin.Campaign] = other;
        env.Island8.land = 8; env.Island8.Json = Json8;
        string key = Raw(env); var view = HeavyShieldIdentity.GetQuotaView(); int staged = Snap(env).Staged;
        HeavyShieldPersistence.EndNativeGeneration(gen, env.Campaign, true);
        Check(ReferenceEquals(HeavyShieldIdentity.Current, origin) && !other.Unknown
            && Raw(env) == key && Snap(env).Staged == staged
            && HeavyShieldIdentity.GetQuotaView().LeftLimit == view.LeftLimit,
            "owner.Sessions replacement without Current swap is rejected pre-Tick");
    }
}
