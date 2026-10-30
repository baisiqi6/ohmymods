using System.Text.Json.Nodes;
using System.Reflection;
using System.Text.Json;
using KingdomEnhancedMod;
using UnityEngine;

internal static class DurabilityCompatibility
{
    private static long next = 3000000;
    private static int checks, failures;
    private static IntPtr Ptr() => new(++next);
    private static GameObject Root(string tag = null) => new(++next, (int)next) { Tag = tag };
    private static void Check(bool value, string label)
    { checks++; if (!value) { failures++; Console.WriteLine("FAIL durability: " + label); } }
    private static void Require(bool value, string label)
    { if (!value) throw new Exception("durability fixture: " + label); }

    internal static void Run()
    {
        checks = failures = 0;
        foreach (int remaining in new[] { 3, 2, 1, 0 }) SoldierRoundtrip(remaining, false);
        SoldierRoundtrip(3, false, omitted: true);
        SoldierRoundtrip(0, true);
        PaidToolPromotion();
        FreshForty();
        V2OriginalRawAndDrift();
        foreach (int version in new[] { 2, 3 })
            foreach (var row in new[] { (1, 23), (2, 45), (3, 68), (4, 90) })
                LaterLegacyRoundtrip(version, row.Item1, row.Item2);
        BadStagedFailsPreflight();
        RepairCommit();
        Console.WriteLine($"{(failures == 0 ? "PASS" : "FAIL")} heavy-shield real identity/persistence durability compatibility: {checks} checks, {failures} failures");
        if (failures != 0) throw new Exception($"durability compatibility failed: {failures}");
    }

    private static (GlobalSaveData Global, CampaignSaveData Campaign, IslandSaveData Island) Setup(string key, string json)
    {
        var island = new IslandSaveData { Pointer = Ptr(), land = 0, Json = json };
        var campaign = new CampaignSaveData { Pointer = Ptr(), CurrentIsland = island, CurrentLand = 0 };
        var global = new GlobalSaveData { Pointer = Ptr(), prefs = new PrefsSaveData { Pointer = Ptr() } };
        global.campaigns.Add(campaign);
        // The second native campaign keeps the codec clone/reconcile survivor
        // set honest, including an inactive campaign's unknown paid career.
        global.campaigns.Add(new CampaignSaveData { Pointer = Ptr(), CurrentIsland = island });
        if (key != null) global.prefs.contents[HeavyShieldSaveSchema.Key] = key;
        GlobalSaveData._loaded = global;
        Managers.Inst = new Managers { game = new Game { Pointer = Ptr(), currentLand = 0 },
            world = new World { Pointer = Ptr(), gameLayer = Root().Add<Transform>() } };
        Time.time = 0; Time.timeScale = 1;
        Game.SavingEnabled = NetworkBigBoss.HasWorldAuth = true; NetworkBigBoss.IsOnline = false;
        ModConfig.Enabled.Value = ModConfig.HeavyShieldEnabled.Value = true;
        IslandSaveData.isSavingGame = false; IslandSaveData.CurrentlySavingIsland = null;
        HeavyShieldRuntime.AttachSucceeds = HeavyShieldRuntime.PreflightSucceeds = true;
        HeavyShieldRuntime.DeferAttach = false;
        HeavyShieldRuntime.AttachCalls = HeavyShieldRuntime.AttachAttempts = HeavyShieldRuntime.DetachCalls = 0;
        HeavyShieldShopShell.ThrowObserve = false;
        return (global, campaign, island);
    }

    private static string LegacyPayload(int remaining, bool unknown, bool paid, string json)
    {
        var doc = new HeavyShieldSaveDocument { Version = 1 };
        for (int slot = 0; slot < 2; slot++)
        {
            var campaign = new HeavyShieldSavedCampaign { Slot = slot, Guid = HeavyShieldSaveCodec.NewGuid(),
                MoldReceipt = HeavyShieldSaveCodec.NewGuid(), RightExtraReceipt = HeavyShieldSaveCodec.NewGuid() };
            campaign.Islands.Add(new HeavyShieldSavedIsland { Land = 0,
                SnapshotHash = HeavyShieldSaveCodec.SnapshotHash(campaign.Guid, 0, 0, json) });
            campaign.Islands.Add(new HeavyShieldSavedIsland { Land = 1,
                SnapshotHash = HeavyShieldSaveCodec.SnapshotHash(campaign.Guid, 0, 1, "{\"away\":true}") });
            doc.Campaigns.Add(campaign);
        }
        doc.Campaigns[0].Islands[0].Claims.Add(new HeavyShieldSavedClaim {
            Receipt = HeavyShieldSaveCodec.NewGuid(), Side = HeavyShieldQuota.Side.Left,
            Phase = paid ? HeavyShieldSavedClaimPhase.PaidTool : HeavyShieldSavedClaimPhase.Soldier,
            NativeId = "legacy-current", Durability = remaining, PendingBreak = remaining == 0, RetirementUnknown = unknown });
        doc.Campaigns[0].Islands[1].Claims.Add(new HeavyShieldSavedClaim {
            Receipt = HeavyShieldSaveCodec.NewGuid(), Side = HeavyShieldQuota.Side.Right,
            Phase = HeavyShieldSavedClaimPhase.PaidTool, NativeId = "away-paid", Durability = 3 });
        doc.Campaigns[1].Islands[1].Claims.Add(new HeavyShieldSavedClaim {
            Receipt = HeavyShieldSaveCodec.NewGuid(), Side = HeavyShieldQuota.Side.Right,
            Phase = HeavyShieldSavedClaimPhase.SoldierUnresolved, NativeId = "", Durability = 0,
            PendingBreak = true, RetirementUnknown = true });
        Require(HeavyShieldSaveCodec.TrySerialize(doc, out var raw, out _), "legacy payload serialize");
        return raw;
    }

    private static (Archer Archer, DroppableTool Tool, Persistent Persistent) Load(IslandSaveData island, bool paid)
    {
        var root = Root(paid ? "Bow" : null);
        var archer = paid ? null : root.Add<Archer>();
        var tool = paid ? root.Add<DroppableTool>() : null;
        var persistent = root.Add<Persistent>();
        if (island.objects.Count == 0) island.objects.Add(new() { Pointer = Ptr(), uniqueID = "legacy-current" });
        var scope = HeavyShieldPersistence.BeginNativeIslandLoad(island);
        var local = HeavyShieldPersistence.BeginNativeLoadRow(scope, island.objects[0]);
        HeavyShieldPersistence.ObserveNativeLoadRow(scope, island.objects[0], persistent);
        HeavyShieldPersistence.EndNativeLoadRow(local);
        HeavyShieldPersistence.EndNativeIslandLoad(scope, true);
        HeavyShieldPersistence.TickBinding();
        return (archer, tool, persistent);
    }
    private static void Stage(IslandSaveData island, Persistent root)
    {
        IslandSaveData.CurrentlySavingIsland = island; IslandSaveData.isSavingGame = true;
        var scope = HeavyShieldPersistence.BeginNativeIslandSave(0, 0, 0);
        HeavyShieldPersistence.ObserveNativeId(scope, root, "legacy-current");
        HeavyShieldPersistence.ObserveNativeIslandCaptured(scope, island);
        HeavyShieldPersistence.EndNativeIslandSave(scope, true);
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
    }
    private static void Prepare(PrefsSaveData prefs)
    {
        var scope = HeavyShieldPersistence.BeginNativePrefsPrepare(prefs);
        prefs.CopyToSerializedEntries(); HeavyShieldPersistence.EndNativePrefsPrepare(scope, true);
    }

    private static void SoldierRoundtrip(int remaining, bool unknown, bool omitted = false)
    {
        int migrated = remaining switch { 0 => 0, 1 => 30, 2 => 60, 3 => 90,
            _ => throw new ArgumentOutOfRangeException(nameof(remaining)) };
        const string json = "{\"land\":0,\"legacy\":true}";
        var raw = LegacyPayload(remaining, unknown, false, json);
        if (omitted)
        {
            var payload = JsonNode.Parse(raw).AsObject(); payload.Remove("version");
            payload["campaigns"][0]["islands"][0]["claims"][0].AsObject().Remove("durability");
            raw = payload.ToJsonString();
        }
        var env = Setup(raw, json);
        ModConfig.HeavyShieldEnabled.Value = false;
        var actor = Load(env.Island, false);
        Check(env.Global.prefs.contents[HeavyShieldSaveSchema.Key] == raw
            && env.Global.prefs.SerializedContents.Count == 0, "parse/clone cannot write native authority while disabled");
        ModConfig.HeavyShieldEnabled.Value = true; HeavyShieldPersistence.TickBinding();
        Check(env.Global.prefs.contents[HeavyShieldSaveSchema.Key] == raw,
            $"v1 {remaining}/unknown={unknown} activation/preflight keeps original raw until Stage/Prepare");
        if (!unknown)
        {
            Check(HeavyShieldIdentity.Current.KeyReady, "existing authoritative key still completes preflight readback");
            Check(OwnerField<string>("ExpectedRaw") == raw && OwnerField<bool>("KeyPreflighted"),
                "legacy preflight bookkeeping binds exact original raw");
        }
        Require(HeavyShieldIdentity.TryGetSoldier(actor.Archer, out var handle), "legacy soldier bound");
        Check(HeavyShieldIdentity.TryGetCombatState(handle, out var combat)
            && combat == new HeavyShieldSavedCombatState(migrated, remaining == 0, unknown),
            "identity proportionally migrates legacy durability while preserving both flags");
        Check(!HeavyShieldIdentity.UpdateCombatState(handle, new(migrated + 1, false, false)),
            "ordinary update cannot increase migrated durability or clear terminal flags");
        var policy = new HeavyShieldBlockPolicy();
        Check(policy.Restore(combat.Durability, combat.PendingBreak, combat.RetirementUnknown)
            && policy.Durability == migrated && !policy.Activate(), "actual policy accepts exact migrated state without reset");
        if (remaining == 0)
        {
            Check(!policy.TryBeginBash(0)
                && policy.EvaluateHit(new(true, true, 1, 0, 1), 0d) == HeavyShieldBlockPolicy.HitResult.PassThrough,
                "loaded zero is terminal in actual policy");
            Check(unknown ? !policy.TryBeginDemote(out _) : policy.TryBeginDemote(out _) && !policy.TryBeginDemote(out _),
                "pending gets once, unknown gets no repeated native retirement");
        }
        Check(HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1
            && !HeavyShieldIdentity.GetQuotaView().ShopOccupied
            && !HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.Mold)
            && !HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldLeft), "legacy receipt keeps entitlement and occupied seat");
        Stage(env.Island, actor.Persistent);
        Check(env.Global.prefs.contents[HeavyShieldSaveSchema.Key] == raw
            && HeavyShieldPersistence.CampaignRestorePending(HeavyShieldIdentity.Current), "Stage keeps native raw and exposes unprepared revision");
        Prepare(env.Global.prefs);
        var prepared = env.Global.prefs.SerializedContents[HeavyShieldSaveSchema.Key];
        var expected = JsonNode.Parse(raw); expected["version"] = HeavyShieldSaveSchema.Version;
        string savedGuid = expected["campaigns"][0]["guid"].GetValue<string>();
        expected["campaigns"][0]["islands"][0]["hashKind"] = HeavyShieldSnapshotFingerprint.CanonicalKind;
        expected["campaigns"][0]["islands"][0]["snapshotHash"] =
            HeavyShieldSnapshotFingerprint.Hash(savedGuid, 0, 0, json);
        // Every stored v1 row migrates, including away-island paid tools and quarantined
        // careers. All other fields stay in this exact whole-document comparison.
        expected["campaigns"][0]["islands"][0]["claims"][0]["durability"] = migrated;
        expected["campaigns"][0]["islands"][1]["claims"][0]["durability"] = 90;
        expected["campaigns"][1]["islands"][1]["claims"][0]["durability"] = 0;
        Check(JsonNode.DeepEquals(expected, JsonNode.Parse(prepared))
            && !HeavyShieldPersistence.CampaignRestorePending(HeavyShieldIdentity.Current), "successful Prepare upgrades current fingerprint and schema while preserving every other row and entitlement");
        Managers.Inst.world.gameLayer = Root().Add<Transform>();
        var returned = Load(env.Island, false);
        Check(HeavyShieldIdentity.TryGetSoldier(returned.Archer, out var nextHandle)
            && HeavyShieldIdentity.TryGetCombatState(nextHandle, out var nextCombat) && nextCombat == combat
            && HeavyShieldIdentity.Current.Phase == HeavyShieldIdentityPhase.Staged
            && !HeavyShieldIdentity.GetQuotaView().Unknown, "Prepared v4 restores current stage without false priorPrepared mismatch");
    }

    private static void PaidToolPromotion()
    {
        const string json = "{\"land\":0,\"legacyPaid\":true}";
        var raw = LegacyPayload(3, false, true, json);
        var env = Setup(raw, json); var actor = Load(env.Island, true);
        Require(HeavyShieldIdentity.TryGetPaidBow(actor.Tool, out var paid), "legacy paid bound");
        Check(env.Global.prefs.contents[HeavyShieldSaveSchema.Key] == raw
            && HeavyShieldIdentity.Current.Claims[paid.Receipt].Combat.Durability == 90 && HeavyShieldIdentity.Current.KeyReady,
            "legacy full paid-tool restore preserves raw and migrates three to ninety");
        Stage(env.Island, actor.Persistent); Prepare(env.Global.prefs);
        Check(HeavyShieldSaveCodec.TryParse(env.Global.prefs.SerializedContents[HeavyShieldSaveSchema.Key], out var toolSaved, out _)
            && toolSaved.Version == HeavyShieldSaveSchema.Version && toolSaved.Campaigns[0].Islands[0].Claims[0].Durability == 90
            && !HeavyShieldPersistence.CampaignRestorePending(HeavyShieldIdentity.Current), "legacy full paid tool stages as v5 with ninety, retaining prepared gate");
        var peasant = Root().Add<Peasant>(); var archer = Root().Add<Archer>();
        Check(HeavyShieldIdentity.CanNativePickup(peasant, actor.Tool), "legacy paid tool remains claimable");
        actor.Tool.pickedUp = true;
        HeavyShieldPromotionBridge.Before(peasant, actor.Tool, out var scope);
        actor.Tool.gameObject.activeInHierarchy = false;
        HeavyShieldIdentity.ObservePoolDespawn(actor.Tool.gameObject, 0);
        HeavyShieldPromotionBridge.After(archer, scope); HeavyShieldPromotionBridge.Finally(scope);
        Check(HeavyShieldIdentity.TryGetSoldier(archer, out var soldier)
            && HeavyShieldIdentity.TryGetCombatState(soldier, out var combat) && combat.Durability == 90
            && HeavyShieldRuntime.LastRestored.Durability == 90
            && !HeavyShieldIdentity.GetQuotaView().ShopOccupied && HeavyShieldIdentity.GetQuotaView().LeftOccupied == 1,
            "host pickup promotes the proportionally migrated tool without another refill, duplicate career or lost side");
    }

    private static void V2OriginalRawAndDrift()
    {
        const string json = "{\"land\":0,\"v2Original\":true}";
        var payload = JsonNode.Parse(LegacyPayload(3, false, false, json));
        payload["version"] = 2;
        payload["campaigns"][0]["islands"][0]["claims"][0]["durability"] = 4;
        // Formatting makes the native string intentionally different from the
        // codec's normal minified output while keeping a valid v2 document.
        string raw = " \n" + payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        var env = Setup(raw, json); var actor = Load(env.Island, false);
        Check(env.Global.prefs.contents[HeavyShieldSaveSchema.Key] == raw
            && env.Global.prefs.SerializedContents.Count == 0
            && HeavyShieldIdentity.Current.KeyReady, "unstaged existing v2 stays byte-for-byte original while preflight completes");
        Check(OwnerField<string>("ExpectedRaw") == raw && OwnerField<bool>("KeyPreflighted")
            && OwnerField<string>("OriginalRaw") == raw, "v2 bookkeeping holds native authority rather than codec output");
        Require(HeavyShieldIdentity.TryGetSoldier(actor.Archer, out var handle), "original v2 soldier");
        Check(HeavyShieldIdentity.TryGetCombatState(handle, out var combat) && combat.Durability == 90,
            "v2 original explicit four migrates to ninety");
        string changed = raw + " "; env.Global.prefs.contents[HeavyShieldSaveSchema.Key] = changed;
        Check(!HeavyShieldPersistence.PreflightKeyWrite(HeavyShieldIdentity.Current)
            && HeavyShieldIdentity.Current.Unknown && HeavyShieldIdentity.Current.StageFault
            && env.Global.prefs.contents[HeavyShieldSaveSchema.Key] == changed
            && OwnerField<string>("ExpectedRaw") == raw,
            "byte-only native drift freezes without overwriting authority even after prior preflight");
        Check(!HeavyShieldIdentity.CanReservePurchase(HeavyShieldPurchaseKind.ShieldRight),
            "drift frozen state cannot authorize a new fee");
    }

    private static void LaterLegacyRoundtrip(int version, int remaining, int expected)
    {
        const string json = "{\"land\":0,\"laterLegacy\":true}";
        var payload = JsonNode.Parse(LegacyPayload(3, false, false, json));
        payload["version"] = version;
        var current = payload["campaigns"][0]["islands"][0];
        current["claims"][0]["durability"] = remaining;
        if (version == 3)
        {
            current["hashKind"] = HeavyShieldSnapshotFingerprint.CanonicalKind;
            current["snapshotHash"] = HeavyShieldSnapshotFingerprint.Hash(
                payload["campaigns"][0]["guid"].GetValue<string>(), 0, 0, json);
        }
        string receipt = current["claims"][0]["receipt"].GetValue<string>();
        string raw = payload.ToJsonString();
        var env = Setup(raw, json); var actor = Load(env.Island, false);
        Require(HeavyShieldIdentity.TryGetSoldier(actor.Archer, out var handle), $"v{version} soldier bound");
        Check(env.Global.prefs.contents[HeavyShieldSaveSchema.Key] == raw && HeavyShieldIdentity.Current.KeyReady,
            $"v{version} original authority remains unchanged until save");
        Check(HeavyShieldIdentity.TryGetCombatState(handle, out var combat)
            && combat == new HeavyShieldSavedCombatState(expected, false, false)
            && HeavyShieldRuntime.LastRestored == combat && handle.Receipt.ToString("D") == receipt
            && handle.Side == HeavyShieldQuota.Side.Left,
            $"v{version} {remaining} proportionally becomes {expected} with same receipt and side");
        Check(!HeavyShieldIdentity.UpdateCombatState(handle, new(expected + 1, false, false)),
            $"v{version} migrated durability still cannot rise through ordinary persistence");
        Stage(env.Island, actor.Persistent); Prepare(env.Global.prefs);
        Require(HeavyShieldSaveCodec.TryParse(env.Global.prefs.SerializedContents[HeavyShieldSaveSchema.Key],
            out var saved, out _), $"v{version} prepared document parses");
        var savedCurrent = saved.Campaigns[0].Islands[0].Claims[0];
        var away = saved.Campaigns[0].Islands[1].Claims[0];
        var quarantined = saved.Campaigns[1].Islands[1].Claims[0];
        Check(saved.Version == 5 && savedCurrent.Durability == expected && savedCurrent.Receipt == receipt
            && savedCurrent.Side == HeavyShieldQuota.Side.Left && !savedCurrent.PendingBreak && !savedCurrent.RetirementUnknown
            && away.Durability == 68 && quarantined.Durability == 0 && quarantined.PendingBreak && quarantined.RetirementUnknown,
            $"v{version} current, away and quarantined claims all survive v5 preparation");
        Managers.Inst.world.gameLayer = Root().Add<Transform>();
        var reloaded = Load(env.Island, false);
        Check(HeavyShieldIdentity.TryGetSoldier(reloaded.Archer, out var nextHandle)
            && HeavyShieldIdentity.TryGetCombatState(nextHandle, out var nextCombat) && nextCombat == combat
            && nextHandle.Receipt == handle.Receipt && nextHandle.Side == handle.Side,
            $"v{version} prepared v4 reload does not scale durability a second time");
    }

    // Identity and persistence are production sources. Native objects and lifecycle transport
    // use this suite's controlled host seams; these calls do not prove Runtime.IsCompleting.
    private static void RepairCommit()
    {
        var env = RepairCareer();
        var handle = env.Handle;
        var policy = env.Policy;
        var original = new HeavyShieldSavedCombatState(20, false, false);
        var completion = new HeavyShieldRepairCompletion(1, handle.Receipt, handle.Life, handle.World, 1, 20);
        Check(!HeavyShieldIdentity.UpdateCombatState(handle, new(21, false, false))
            && HeavyShieldIdentity.TryGetCombatState(handle, out var unchanged) && unchanged == original,
            "ordinary state update still rejects increases despite the explicit repair API");
        foreach (var wrong in new[] { completion with { Serial = 0 }, completion with { Receipt = Guid.NewGuid() },
            completion with { Life = handle.Life + 1 }, completion with { World = handle.World + 1 },
            completion with { ShopLife = 0 }, completion with { ExpectedDurability = 19 },
            completion with { ExpectedDurability = 21 } })
        {
            Check(!HeavyShieldIdentity.TryCompleteRepair(handle, wrong, policy)
                && policy.Durability == 20 && HeavyShieldIdentity.TryGetCombatState(handle, out unchanged)
                && unchanged == original, "mismatched completion changes neither policy nor claim");
        }
        foreach (var wrong in new[] { handle with { Root = Ptr() }, handle with { GoId = handle.GoId + 1 },
            handle with { Side = HeavyShieldQuota.Side.Right }, handle with { Land = handle.Land + 1 },
            handle with { CampaignGuid = HeavyShieldSaveCodec.NewGuid() } })
        {
            Check(!HeavyShieldIdentity.TryCompleteRepair(wrong, completion, policy)
                && policy.Durability == 20 && HeavyShieldIdentity.TryGetCombatState(handle, out unchanged)
                && unchanged == original, "mismatched exact career changes neither durable value");
        }
        var stale = handle with { Life = handle.Life + 1 };
        Check(!HeavyShieldIdentity.TryCompleteRepair(stale, completion with { Life = stale.Life }, policy)
            && policy.Durability == 20 && HeavyShieldIdentity.TryGetCombatState(handle, out unchanged)
            && unchanged == original, "matching stale completion and handle do not replace current life proof");
        var mismatchedPolicy = new HeavyShieldBlockPolicy();
        Require(mismatchedPolicy.Restore(19, false, false), "mismatched repair policy");
        Check(!HeavyShieldIdentity.TryCompleteRepair(handle, completion, mismatchedPolicy)
            && mismatchedPolicy.Durability == 19 && HeavyShieldIdentity.TryGetCombatState(handle, out unchanged)
            && unchanged == original, "policy and claim disagreement is rejected before either write");
        Check(!HeavyShieldIdentity.TryCompleteRepair(handle, completion, null)
            && HeavyShieldIdentity.TryGetCombatState(handle, out unchanged) && unchanged == original,
            "missing policy cannot partially repair claim");
        Require(policy.TryBeginBash(1), "busy repair policy");
        Check(!HeavyShieldIdentity.TryCompleteRepair(handle, completion, policy)
            && policy.Durability == 20 && policy.BashActive
            && HeavyShieldIdentity.TryGetCombatState(handle, out unchanged) && unchanged == original,
            "busy real policy rejects repair and leaves claim unchanged");
        policy.EndBash();

        Check(HeavyShieldIdentity.TryCompleteRepair(handle, completion, policy)
            && policy.Durability == 90 && !policy.PendingBreak && !policy.RetirementUnknown
            && HeavyShieldIdentity.TryGetCombatState(handle, out var repaired)
            && repaired == new HeavyShieldSavedCombatState(90, false, false),
            "valid managed repair commits current policy and claim to ninety together");
        Check(!HeavyShieldIdentity.TryCompleteRepair(handle, completion, policy)
            && !HeavyShieldIdentity.TryCompleteRepair(handle, completion with { Serial = 2, ExpectedDurability = 90 }, policy)
            && policy.Durability == 90 && HeavyShieldIdentity.TryGetCombatState(handle, out repaired)
            && repaired == new HeavyShieldSavedCombatState(90, false, false),
            "repeat completion and full-durability completion cannot repair again");
        Stage(env.Island, env.Persistent); Prepare(env.Global.prefs);
        Check(HeavyShieldSaveCodec.TryParse(env.Global.prefs.SerializedContents[HeavyShieldSaveSchema.Key], out var saved, out _)
            && saved.Version == 5 && saved.Campaigns[0].Islands[0].Claims[0].Durability == 90
            && !saved.Campaigns[0].Islands[0].Claims[0].PendingBreak
            && !saved.Campaigns[0].Islands[0].Claims[0].RetirementUnknown
            && saved.Campaigns[0].Islands[0].Claims[0].Receipt == handle.Receipt.ToString("D")
            && saved.Campaigns[0].Islands[0].Claims[0].Side == handle.Side,
            "real identity sampling and Stage/Prepare persist the repaired exact career at ninety");
        Managers.Inst.world.gameLayer = Root().Add<Transform>();
        var cold = Load(env.Island, false);
        Check(HeavyShieldIdentity.TryGetSoldier(cold.Archer, out var coldHandle)
            && HeavyShieldIdentity.TryGetCombatState(coldHandle, out var coldCombat)
            && coldCombat == new HeavyShieldSavedCombatState(90, false, false)
            && coldHandle.Receipt == handle.Receipt, "saved repair reloads once at ninety without another completion");

        // Historic inconsistent quarantine flags are intentionally retained by the codec.
        // Prove claim flags themselves bar repair even if the separate policy is guarding.
        foreach (var flags in new[] { (true, false), (false, true), (true, true) })
        {
            var blocked = RepairCareer();
            var state = new HeavyShieldSavedCombatState(20, flags.Item1, flags.Item2);
            Require(HeavyShieldIdentity.UpdateCombatState(blocked.Handle, state), "quarantined repair claim");
            var blockedCompletion = new HeavyShieldRepairCompletion(1, blocked.Handle.Receipt, blocked.Handle.Life,
                blocked.Handle.World, 1, 20);
            Check(!HeavyShieldIdentity.TryCompleteRepair(blocked.Handle, blockedCompletion, blocked.Policy)
                && blocked.Policy.Durability == 20
                && HeavyShieldIdentity.TryGetCombatState(blocked.Handle, out var retained) && retained == state,
                "pending or unknown claim cannot repair or silently clear either flag");
        }
    }

    private static (GlobalSaveData Global, IslandSaveData Island, Persistent Persistent,
        HeavyShieldCareerHandle Handle, HeavyShieldBlockPolicy Policy) RepairCareer()
    {
        const string json = "{\"land\":0,\"repair\":true}";
        var payload = JsonNode.Parse(LegacyPayload(2, false, false, json));
        payload["version"] = HeavyShieldSaveSchema.Version;
        payload["campaigns"][0]["islands"][0]["claims"][0]["durability"] = 20;
        var env = Setup(payload.ToJsonString(), json);
        var actor = Load(env.Island, false);
        Require(HeavyShieldIdentity.TryGetSoldier(actor.Archer, out var handle)
            && HeavyShieldIdentity.Current.KeyReady && !HeavyShieldIdentity.Current.Unknown
            && !HeavyShieldIdentity.Current.StageFault, "repair exact current career fixture");
        var policy = new HeavyShieldBlockPolicy();
        Require(policy.Restore(20, false, false), "repair guarding policy fixture");
        return (env.Global, env.Island, actor.Persistent, handle, policy);
    }

    private static object Owner()
        => typeof(HeavyShieldPersistence).GetField("_global", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static T OwnerField<T>(string name)
    {
        var owner = Owner();
        return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner);
    }
    private static void BadStagedFailsPreflight()
    {
        const string json = "{\"land\":0,\"badStaged\":true}";
        string raw = LegacyPayload(3, false, false, json);
        var env = Setup(raw, json); ModConfig.HeavyShieldEnabled.Value = false; Load(env.Island, false);
        var staged = OwnerField<HeavyShieldSaveDocument>("Staged");
        staged.Version = 99;
        ModConfig.HeavyShieldEnabled.Value = true;
        Check(!HeavyShieldPersistence.PreflightKeyWrite(HeavyShieldIdentity.Current)
            && !HeavyShieldIdentity.Current.KeyReady && !OwnerField<bool>("KeyPreflighted")
            && env.Global.prefs.contents[HeavyShieldSaveSchema.Key] == raw
            && OwnerField<string>("ExpectedRaw") == raw, "bad startup Staged cannot gain KeyReady via original-string branch");
        staged.Version = HeavyShieldSaveSchema.Version;
        Check(HeavyShieldPersistence.PreflightKeyWrite(HeavyShieldIdentity.Current)
            && HeavyShieldIdentity.Current.KeyReady && OwnerField<bool>("KeyPreflighted")
            && env.Global.prefs.contents[HeavyShieldSaveSchema.Key] == raw,
            "restored valid startup clone keeps original authority and preflight bookkeeping");
    }

    private static void FreshForty()
    {
        var env = Setup(null, "{\"land\":0,\"fresh\":true}");
        var load = HeavyShieldPersistence.BeginNativeIslandLoad(env.Island);
        HeavyShieldPersistence.EndNativeIslandLoad(load, true); HeavyShieldPersistence.TickBinding();
        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.Mold, out var mold)
            && HeavyShieldIdentity.CompleteGemPayment(mold) == HeavyShieldCommitResult.Applied, "fresh mold");
        var root = Root("Bow"); var bow = root.Add<DroppableTool>();
        Require(HeavyShieldIdentity.TryReservePurchase(HeavyShieldPurchaseKind.ShieldLeft, out var purchase)
            && HeavyShieldIdentity.BindIssuedPaidBow(purchase, bow) == HeavyShieldCommitResult.Applied, "fresh shield");
        Require(HeavyShieldIdentity.TryGetPaidBow(bow, out var paid), "fresh paid tool");
        Check(HeavyShieldIdentity.Current.Claims[paid.Receipt].Combat.Durability == 90, "new paid tool initializes through actual SchemaMax to ninety");
        var peasant = Root().Add<Peasant>(); var archer = Root().Add<Archer>(); bow.pickedUp = true;
        HeavyShieldPromotionBridge.Before(peasant, bow, out var scope); root.activeInHierarchy = false;
        HeavyShieldIdentity.ObservePoolDespawn(root, 0);
        HeavyShieldPromotionBridge.After(archer, scope); HeavyShieldPromotionBridge.Finally(scope);
        Require(HeavyShieldIdentity.TryGetSoldier(archer, out var soldier), "fresh soldier");
        Check(HeavyShieldIdentity.TryGetCombatState(soldier, out var fresh) && fresh.Durability == 90,
            "new promotion keeps ninety durability");
        var persistent = archer.gameObject.Add<Persistent>();
        env.Island.objects.Add(new() { Pointer = Ptr(), uniqueID = "legacy-current" });
        Stage(env.Island, persistent); Prepare(env.Global.prefs);
        Check(HeavyShieldSaveCodec.TryParse(env.Global.prefs.SerializedContents[HeavyShieldSaveSchema.Key], out var freshSaved, out _)
            && freshSaved.Version == HeavyShieldSaveSchema.Version && freshSaved.Campaigns[0].Islands[0].Claims[0].Durability == 90,
            "fresh ninety-point career saves through real Stage/Prepare as v5");
        var policy = new HeavyShieldBlockPolicy(); Require(policy.Restore(90, false, false), "fresh policy");
        for (int remaining = 89; remaining >= 0; remaining--)
        {
            var result = policy.EvaluateHit(new(true, true, 1, 0, 1), 1000d - remaining);
            Check(HeavyShieldIdentity.UpdateCombatState(soldier, new(policy.Durability, policy.PendingBreak, policy.RetirementUnknown))
                && HeavyShieldIdentity.TryGetCombatState(soldier, out var state) && state.Durability == remaining
                && result == (remaining == 0 ? HeavyShieldBlockPolicy.HitResult.ShieldBroken : HeavyShieldBlockPolicy.HitResult.Blocked),
                "fresh ninety-point wear updates actual identity monotonically");
            Stage(env.Island, persistent); Prepare(env.Global.prefs);
            Check(HeavyShieldSaveCodec.TryParse(env.Global.prefs.SerializedContents[HeavyShieldSaveSchema.Key], out var wornSaved, out _)
                && wornSaved.Campaigns[0].Islands[0].Claims[0].Durability == remaining
                && wornSaved.Campaigns[0].Islands[0].Claims[0].PendingBreak == (remaining == 0),
                "real Stage/Prepare persists each new wear point without repair");
        }
        Check(!HeavyShieldIdentity.UpdateCombatState(soldier, new(90, false, false)), "broken new soldier cannot heal either");
        Require(policy.TryBeginDemote(out var lease), "final retirement lease");
        Check(HeavyShieldIdentity.UpdateCombatState(soldier, new(0, true, true))
            && policy.ConfirmDemote(lease) && !policy.TryBeginDemote(out _), "final native retirement proof remains at most once");
    }
}
