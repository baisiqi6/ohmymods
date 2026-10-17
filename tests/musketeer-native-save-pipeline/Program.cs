// Production-linked producer-to-writer regression. Native/Unity calls are doubled;
// this does not prove Harmony injection, native ABI or the real filesystem writer.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using KingdomEnhancedMod;
using UnityEngine;
using Coatsink.Common;

class Program
{
    static int assertions;
    static readonly string Root = Path.Combine(AppContext.BaseDirectory, "evidence", Guid.NewGuid().ToString("N"));
    static string DiskPath => Path.Combine(Filer.folder, GlobalSaveData.filename);
    static string PairPath => Path.Combine(Path.GetDirectoryName(MusketeerPersistence.ArchivePath), "musketeer-pair-global-v35.json");
    static IslandSaveData Island => CampaignSaveData.current.CurrentIsland;
    static CampaignSaveData Campaign => CampaignSaveData.current;
    const string ToolBowPath = "Prefabs/Objects/ToolBow";
    const string ArcherPath = "Prefabs/Characters/Archer";

    static string Context(int campaign, int challenge, int slot)
        => MusketeerArchive.ContextKey(GlobalSaveData.filename, campaign, challenge, slot);

    static string IslandJsonAt(int land, double playTime, params string[] rows)
        => "{\"land\":" + land + ",\"objects\":[" + string.Join(",", rows) + "],\"playTimeDays\":"
            + playTime.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";

    static string Row(string id, string path) => JsonSerializer.Serialize(new { uniqueID = id, prefabPath = path });

    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL " + label);
        assertions++;
        Console.WriteLine("PASS " + label);
    }

    static void SetStatic(string name, object value)
        => typeof(MusketeerIdentity).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, value);

    static void ClearIdentity()
    {
        MusketeerIdentity.Islands.Clear();
        foreach (string field in new[] { "Roots", "Bound", "Logged" })
        {
            var value = typeof(MusketeerIdentity).GetField(field, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            value.GetType().GetMethod("Clear").Invoke(value, null);
        }
        SetStatic("_current", null);
        SetStatic("_contextKey", null);
        SetStatic("_generation", null);
        SetStatic("_lastTickFrame", -1);
        SetStatic("_contextFrame", -1);
        SetStatic("_contextWorld", 0L);
    }

    static IslandSaveData Placeholder(int index)
        => new IslandSaveData
        {
            land = 0,
            isNew = false,
            playTimeDays = 100 + index,
            // playTimeDays is excluded from the island hash; this marker keeps every unvisited
            // placeholder hash-distinct so the writer can prove exactly one land0 island.
            Json = "{\"land\":0,\"objects\":[],\"playTimeDays\":" + (100 + index)
                + ",\"placeholder\":" + index + "}"
        };

    static CampaignSaveData BuildCampaign(IslandSaveData home, IslandSaveData target)
    {
        var campaign = new CampaignSaveData { challengeId = 0, CurrentLand = 0 };
        for (int i = 0; i < 12; i++) campaign._islands.Add(Placeholder(i));
        campaign._islands[0] = home;
        if (target != null) campaign._islands[11] = target;
        campaign.CurrentIsland = home;
        return campaign;
    }

    static void WriteCatalog(Utf8JsonWriter writer, string name, Il2CppSystem.Collections.Generic.List<CampaignSaveData> list)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        if (list != null)
        {
            foreach (var campaign in list)
            {
                if (campaign == null) { writer.WriteNullValue(); continue; }
                writer.WriteStartObject();
                writer.WriteNumber("challengeId", campaign.challengeId);
                writer.WritePropertyName("_islands");
                writer.WriteStartArray();
                if (campaign._islands != null)
                {
                    foreach (var island in campaign._islands)
                    {
                        if (island == null) { writer.WriteNullValue(); continue; }
                        using var document = JsonDocument.Parse(island.Json);
                        document.RootElement.WriteTo(writer);
                    }
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();
    }

    static byte[] Payload()
    {
        string raw = GlobalSaveData.loaded.prefs.contents[MusketeerNativeArchive.Key];
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
        {
            using (var writer = new Utf8JsonWriter(gzip))
            {
                writer.WriteStartObject();
                WriteCatalog(writer, "campaigns", GlobalSaveData.loaded.campaigns);
                WriteCatalog(writer, "challenges", GlobalSaveData.loaded.challenges);
                writer.WritePropertyName("prefs");
                writer.WriteStartObject();
                writer.WritePropertyName("srzEntries");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("key", MusketeerNativeArchive.Key);
                writer.WriteString("val", raw);
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
        }
        return output.ToArray();
    }

    static void Accept(byte[] payload)
    {
        int callbacks = 0;
        Il2CppSystem.Action<SaveLoadResult> callback = (System.Action<SaveLoadResult>)(result =>
        {
            callbacks++;
            Check((int)result == 72, "original callback success unmodified");
        });
        bool runOriginal = true;
        MusketeerNativeSave.Musketeer_SaveAsyncObserver.Prefix(GlobalSaveData.loaded, ref callback, ref runOriginal);
        var verdict = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, payload, false, out var call, out string reason);
        Check(verdict == MusketeerNativeSave.WriterVerdict.Pass && call != null, "actual producer payload writer passes: " + reason);
        File.WriteAllBytes(DiskPath, payload);
        MusketeerNativeSave.WriterStarted(call, new Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> { IsCompleted = true });
        callback.Invoke((SaveLoadResult)72);
        Check(callbacks == 1, "callback forwards once");
        Check(MusketeerPairedBackup.TryRecover(PairPath, GlobalSaveData.filename, payload, out _, out _, out string pairReason),
            "accepted pair matches this exact native payload: " + pairReason);
    }

    static void FinishSave(MusketeerPersistence.SaveCapture capture)
    {
        typeof(MusketeerPersistence.SavePatch).GetMethod("Finally", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { null, capture });
        IslandSaveData.CurrentlySavingIsland = null;
        IslandSaveData.isSavingGame = false;
    }

    static MusketeerPersistence.SaveCapture SaveExplicit(IslandSaveData island, int land, string json,
        params (string id, GameObject go)[] rows)
    {
        island.Json = json; island.isNew = false; island.playTimeDays = 1;
        island.objects = rows.Select(x => new IslandSaveData.ObjectData { uniqueID = x.id }).ToList();
        IslandSaveData.CurrentlySavingIsland = island; IslandSaveData.isSavingGame = true;
        var capture = new MusketeerPersistence.SaveCapture
        {
            Campaign = GlobalSaveData.loaded.currentCampaign,
            Land = land,
            Challenge = GlobalSaveData.loaded.currentChallenge
        };
        capture.Begin();
        foreach (var row in rows) if (row.go != null) capture.Capture(row.go.Add(new Persistent()), row.id);
        capture.Apply();
        FinishSave(capture);
        return capture;
    }

    static MusketeerPersistence.SaveCapture BeginDeferred(IslandSaveData island)
    {
        IslandSaveData.CurrentlySavingIsland = island; IslandSaveData.isSavingGame = true;
        var capture = new MusketeerPersistence.SaveCapture
        {
            Campaign = GlobalSaveData.loaded.currentCampaign,
            Land = -1,
            Challenge = GlobalSaveData.loaded.currentChallenge
        };
        capture.Begin();
        return capture;
    }

    static void Main()
    {
        Directory.CreateDirectory(Root);
        BepInEx.Paths.ConfigPath = Path.Combine(Root, "config");
        Filer.folder = Path.Combine(Root, "Release");
        Directory.CreateDirectory(Filer.folder);
        Directory.CreateDirectory(Path.GetDirectoryName(MusketeerPersistence.ArchivePath));

        var home = new IslandSaveData { land = 0, isNew = true, playTimeDays = 0, Json = IslandJsonAt(0, 0) };
        var campaign = BuildCampaign(home, null);
        GlobalSaveData.loaded = new GlobalSaveData { currentCampaign = 0, currentChallenge = 0, CurrentCampaign = campaign };
        GlobalSaveData.filename = "global-v35";
        GlobalSaveData.loaded.campaigns.Add(campaign);
        CampaignSaveData.current = campaign;
        Managers.Inst = new Managers { world = new World { gameLayer = new GameObject().Add(new Transform()) } };
        GlobalSaveData.loaded.prefs.contents["synthetic-paid-heavy-prefs"] = "{\"paid\":true}";
        string heavyPrefsBefore = GlobalSaveData.loaded.prefs.contents["synthetic-paid-heavy-prefs"];
        Check(campaign._islands.Count == 12 && campaign._islands[0] == home && campaign._islands[11] != home,
            "fixture exposes twelve positional island slots");

        var virgin = new MusketeerPersistence.VirginCapture();
        virgin.Begin(campaign);
        virgin.Complete(campaign);
        Time.frameCount++;
        MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "virgin home slot0 initializes the paid baseline");
        Check(MusketeerIdentity.Current.ContextKey == Context(0, 0, 0), "home generation keys slot 0");
        byte[] homeGeneration = Payload();
        using (var document = JsonDocument.Parse(homeGeneration.Length > 0 ? Decompress(homeGeneration) : "{}"))
        {
            Check(document.RootElement.GetProperty("campaigns")[0].GetProperty("_islands").GetArrayLength() == 12,
                "native payload carries the canonical twelve-island array");
        }
        Accept(homeGeneration);

        var homeGun = new GameObject { tag = "Bow" };
        var homePromotedGun = new GameObject { tag = "Bow" };
        Check(MusketeerIdentity.TryRegisterPaidGun(homeGun.Add(new DroppableTool())), "register first paid gun");
        Check(MusketeerIdentity.TryRegisterPaidGun(homePromotedGun.Add(new DroppableTool())), "register second gun for unit");
        MusketeerIdentity.OnGunPickupBegin(homePromotedGun.GetComponent<DroppableTool>(), out var homePromotion);
        var homeUnit = new GameObject();
        CheckPromotion(homeUnit, homePromotion);
        var homeIds = MusketeerIdentity.Current.Careers.Select(x => x.Id).OrderBy(x => x).ToArray();
        string homeEpoch = MusketeerIdentity.Current.Epoch;
        string homeJson1 = IslandJsonAt(0, 1, Row("gun-H1", ToolBowPath), Row("unit-H1", ArcherPath));
        string homeJson2 = IslandJsonAt(0, 2, Row("gun-H2", ToolBowPath), Row("unit-H2", ArcherPath));
        string homeJson3 = IslandJsonAt(0, 3, Row("gun-H3", ToolBowPath), Row("unit-H3", ArcherPath));
        Check(SaveExplicit(home, 0, homeJson1,
            ("gun-H1", homeGun), ("unit-H1", homeUnit)).Applied, "actual producer H1 Stage succeeds");
        byte[] h1 = Payload();
        Accept(h1);

        Check(SaveExplicit(home, 0, homeJson2,
            ("gun-H2", homeGun), ("unit-H2", homeUnit)).Applied, "actual producer H2 Stage succeeds");
        byte[] h2 = Payload();
        Accept(h2);
        Check(!h1.AsSpan().SequenceEqual(h2), "second save has distinct native bytes");

        // Existing capture-missed protection: a live paid row that never reached GetID refuses
        // the whole sidecar and leaves the writer-facing CaptureFault in place.
        home.Json = homeJson2;
        home.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "gun-H2" }, new() { uniqueID = "unit-H2" } };
        IslandSaveData.CurrentlySavingIsland = home; IslandSaveData.isSavingGame = true;
        var faultCapture = new MusketeerPersistence.SaveCapture { Campaign = 0, Land = 0, Challenge = 0 };
        faultCapture.Begin();
        faultCapture.Capture(homeGun.Add(new Persistent()), "gun-H2");
        faultCapture.Apply();
        FinishSave(faultCapture);
        var faultVerdict = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, Payload(), false, out _, out string faultWhy);
        Check(faultVerdict == MusketeerNativeSave.WriterVerdict.Fail && faultWhy == MusketeerNativeSave.ReasonCaptureFault,
            "missed active paid row blocks the stale outgoing payload: " + faultWhy);
        Check(SaveExplicit(home, 0, homeJson3, ("gun-H3", homeGun), ("unit-H3", homeUnit)).Applied,
            "next correct producer capture clears the fault");
        byte[] h3 = Payload();
        Accept(h3);

        // ---- generation at slot 11 while its native land field still says 0 ----
        var target = new IslandSaveData { land = 0, isNew = true, playTimeDays = 0, Json = IslandJsonAt(0, 0) };
        campaign._islands[11] = target;
        campaign.CurrentIsland = target;
        campaign.CurrentLand = 11;
        virgin = new MusketeerPersistence.VirginCapture();
        virgin.Begin(campaign);
        virgin.Complete(campaign);
        Time.frameCount++;
        MusketeerIdentity.Tick();
        string context11 = Context(0, 0, 11);
        Check(MusketeerIdentity.Current.ContextKey == context11, "virgin land0 generation keys positional slot 11");
        Check(target.land == 0, "generation does not rewrite the native land field");
        Check(MusketeerIdentity.Current.Careers.Count == 0, "slot11 generation inherits no home GUID");
        Check(homeEpoch == MusketeerIdentity.Islands[Context(0, 0, 0)].Epoch, "home epoch survives slot11 generation");
        Check(MusketeerIdentity.Islands[Context(0, 0, 0)].Careers.Select(x => x.Id).OrderBy(x => x).SequenceEqual(homeIds),
            "home active identities survive slot11 generation");

        // The island is no longer "new", and later live frames still resolve slot11 through the
        // completed generation receipt (no periodic land rewrite).
        target.isNew = false; target.playTimeDays = 3;
        Time.frameCount++;
        MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "generation receipt authorizes a later live land0 placeholder");
        var slotGun = new GameObject { tag = "Bow" };
        var slotUnitGun = new GameObject { tag = "Bow" };
        Check(MusketeerIdentity.TryRegisterPaidGun(slotGun.Add(new DroppableTool())), "purchase gun on slot11");
        Check(MusketeerIdentity.TryRegisterPaidGun(slotUnitGun.Add(new DroppableTool())), "purchase unit gun on slot11");
        MusketeerIdentity.OnGunPickupBegin(slotUnitGun.GetComponent<DroppableTool>(), out var slotPromotion);
        var slotUnit = new GameObject();
        CheckPromotion(slotUnit, slotPromotion);
        Guid[] slotIds = MusketeerIdentity.Current.Careers.Select(x => x.Id).OrderBy(x => x).ToArray();
        Check(slotIds.Length == 2 && !slotIds.Intersect(homeIds).Any(), "slot11 paid identities are independent");

        // First save: GetID resolves slot11 while the native data land is still 0, then the native
        // side finalizes land 11 (field + JSON) before the postfix applies.
        string epoch11 = MusketeerIdentity.Current.Epoch;
        target.objects = new List<IslandSaveData.ObjectData>
        {
            new() { uniqueID = "slot11-gun" }, new() { uniqueID = "slot11-unit" }
        };
        target.Json = IslandJsonAt(0, 2, Row("slot11-gun", ToolBowPath), Row("slot11-unit", ArcherPath));
        var deferred = BeginDeferred(target);
        deferred.Capture(slotGun.Add(new Persistent()), "slot11-gun");
        deferred.Capture(slotUnit.Add(new Persistent()), "slot11-unit");
        Check(deferred.ContextKey == context11, "-1 GetID uses the frozen owner slot, not raw data land 0");
        target.land = 11;
        target.playTimeDays = 4;
        target.Json = IslandJsonAt(11, 4, Row("slot11-gun", ToolBowPath), Row("slot11-unit", ArcherPath));
        deferred.Apply();
        FinishSave(deferred);
        Check(deferred.Applied, "first slot11 save stages under the same generation epoch");
        Check(MusketeerIdentity.Current.Epoch == epoch11, "first save updates proof without a new epoch");
        byte[] slotPayload = Payload();
        Accept(slotPayload);
        Check(MusketeerIdentity.Islands[Context(0, 0, 0)].Epoch == homeEpoch, "first slot11 save leaves home epoch untouched");
        Check(MusketeerIdentity.Islands[Context(0, 0, 0)].Careers.Select(x => x.Id).OrderBy(x => x).SequenceEqual(homeIds),
            "first slot11 save leaves home paid GUIDs untouched");
        Check(GlobalSaveData.loaded.prefs.contents["synthetic-paid-heavy-prefs"] == heavyPrefsBefore,
            "generation/save leave synthetic paid Heavy prefs bytes untouched");

        // Same-session stale native field returns to 0 after the accepted save: the same receipt
        // continues to authorize this exact island (modeled, not a claim about native behavior).
        target.land = 0; target.playTimeDays = 5;
        Time.frameCount++;
        MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "post-save raw land 0 remains authorized by the same receipt");
        var postSaveGun = new GameObject { tag = "Bow" };
        Check(MusketeerIdentity.TryRegisterPaidGun(postSaveGun.Add(new DroppableTool())), "purchase under the post-save receipt");
        Check(MusketeerIdentity.Current.Epoch == epoch11, "post-save purchase does not change the epoch");

        // An explicit Save(0) while CurrentIsland is 11 keeps the old-island source: it must never
        // be rewritten to the current scene slot.
        var explicitOld = SaveExplicit(home, 0, homeJson3,
            ("gun-H3", homeGun), ("unit-H3", homeUnit));
        Check(explicitOld.ContextKey == Context(0, 0, 0), "explicit Save(0) keeps context 0 while current island is 11");
        Check(explicitOld.Applied, "explicit old-island save stages under its own context");
        Check(MusketeerIdentity.Current.ContextKey == context11, "explicit old-island save does not move the live context");
        Accept(Payload());

        // A foreign-island save only reads the generation receipt; it never revokes it.
        Time.frameCount++;
        MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "foreign explicit save does not revoke the current receipt");

        // ---- cold load: exact native IDs, then return to home without losing either epoch ----
        Check(MusketeerNativeArchive.TryExtract(slotPayload, "global-v35", out string raw, out _, out _, out _),
            "extract actually accepted native raw for cold load");
        string homeSavedJson = home.Json;
        string targetSavedJson = target.Json;
        ClearIdentity();
        MusketeerNativeSave.ResetForTests();
        var loadedHome = new IslandSaveData { land = 0, isNew = false, playTimeDays = 1, Json = homeSavedJson };
        var loadedTarget = new IslandSaveData
        {
            land = 11, isNew = false, playTimeDays = 4, Json = targetSavedJson,
            objects = new List<IslandSaveData.ObjectData>
            {
                new() { uniqueID = "slot11-gun" }, new() { uniqueID = "slot11-unit" }
            }
        };
        var reloaded = BuildCampaign(loadedHome, loadedTarget);
        reloaded.CurrentIsland = loadedTarget;
        reloaded.CurrentLand = 11;
        GlobalSaveData.loaded = new GlobalSaveData { currentCampaign = 0, currentChallenge = 0, CurrentCampaign = reloaded };
        GlobalSaveData.loaded.prefs.contents[MusketeerNativeArchive.Key] = raw;
        GlobalSaveData.loaded.campaigns.Add(reloaded);
        CampaignSaveData.current = reloaded;
        Managers.Inst.world.gameLayer = new GameObject().Add(new Transform());
        var coldSlotGun = new GameObject { tag = "Bow" };
        var coldSlotTool = coldSlotGun.Add(new DroppableTool());
        var coldSlotUnit = new GameObject();
        var coldSlotArcher = coldSlotUnit.Add(new Archer());
        coldSlotUnit.Add(new Character());
        var loadTarget = new MusketeerPersistence.LoadCapture();
        loadTarget.Begin(loadedTarget);
        loadTarget.Capture(loadedTarget.objects[0], coldSlotGun.Add(new Persistent()));
        loadTarget.Capture(loadedTarget.objects[1], coldSlotUnit.Add(new Persistent()));
        loadTarget.End(true);
        Time.frameCount++;
        MusketeerIdentity.Tick();
        Check(MusketeerIdentity.IsGun(coldSlotTool) && MusketeerIdentity.IsUnit(coldSlotArcher),
            "cold load restores exact slot11 gun + unit identities");
        Check(MusketeerIdentity.Current.Careers.Select(x => x.Id).OrderBy(x => x).SequenceEqual(slotIds),
            "cold load preserves the exact slot11 paid GUID set");
        Check(MusketeerIdentity.Current.Epoch == epoch11, "cold load restores the slot11 epoch");
        Check(MusketeerIdentity.CanPurchase, "cold slot11 baseline remains purchase-ready");

        var coldHomeGun = new GameObject { tag = "Bow" };
        var coldHomeTool = coldHomeGun.Add(new DroppableTool());
        var coldHomeUnit = new GameObject();
        var coldHomeArcher = coldHomeUnit.Add(new Archer());
        coldHomeUnit.Add(new Character());
        loadedHome.objects = new List<IslandSaveData.ObjectData>
        {
            new() { uniqueID = "gun-H3" }, new() { uniqueID = "unit-H3" }
        };
        var loadHome = new MusketeerPersistence.LoadCapture();
        loadHome.Begin(loadedHome);
        loadHome.Capture(loadedHome.objects[0], coldHomeGun.Add(new Persistent()));
        loadHome.Capture(loadedHome.objects[1], coldHomeUnit.Add(new Persistent()));
        loadHome.End(true);
        reloaded.CurrentIsland = loadedHome;
        reloaded.CurrentLand = 0;
        Time.frameCount++;
        MusketeerIdentity.Tick();
        Check(MusketeerIdentity.IsGun(coldHomeTool) && MusketeerIdentity.IsUnit(coldHomeArcher),
            "cold load restores exact home slot0 identities");
        Check(MusketeerIdentity.Current.Careers.Select(x => x.Id).OrderBy(x => x).SequenceEqual(homeIds),
            "return to home preserves the original paid GUID set");
        Check(MusketeerIdentity.Current.Epoch == homeEpoch, "return to home preserves the original epoch");
        Check(MusketeerIdentity.Islands[context11].Epoch == epoch11
            && MusketeerIdentity.Islands[context11].Careers.Select(x => x.Id).OrderBy(x => x).SequenceEqual(slotIds),
            "slot11 state survives the return-to-home load");

        // ---- zero-GetID shapes: record the real Apply/Finalizer/Gate outcome, never invent one ----
        var empty = new IslandSaveData { land = 0, isNew = true, playTimeDays = 0, Json = IslandJsonAt(0, 0) };
        reloaded._islands[10] = empty;
        reloaded.CurrentIsland = empty;
        reloaded.CurrentLand = 10;
        virgin = new MusketeerPersistence.VirginCapture();
        virgin.Begin(reloaded);
        virgin.Complete(reloaded);
        Time.frameCount++;
        MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "empty slot10 generation provides a baseline");
        empty.land = 10; empty.isNew = false; empty.playTimeDays = 1; empty.Json = IslandJsonAt(10, 1);
        byte[] beforeZero = File.Exists(DiskPath) ? File.ReadAllBytes(DiskPath) : null;
        var zero = BeginDeferred(empty);
        zero.Apply();
        FinishSave(zero);
        var zeroGate = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, Payload(), false, out _, out string zeroReason);
        Console.WriteLine("ZERO-GETID no-purchase: verdict=" + zeroGate + " reason=" + zeroReason);
        Check(zeroGate != MusketeerNativeSave.WriterVerdict.Pass,
            "zero-purchase first save without GetID cannot silently pass: " + zeroReason);
        bool zeroFileSame = beforeZero == null
            ? !File.Exists(DiskPath)
            : File.Exists(DiskPath) && File.ReadAllBytes(DiskPath).SequenceEqual(beforeZero);
        Check(zeroFileSame, "zero-GetID refusal leaves the native file bytes unchanged");

        // Repair the fixture with a real complete slot10 checkpoint. This clears the empty group's
        // stale proof through the production Capture/Apply/Stage path, not by deleting anything.
        var slot10RepairRoot = new GameObject();
        empty.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "slot10-foreign" } };
        empty.Json = IslandJsonAt(10, 1, "{\"uniqueID\":\"slot10-foreign\"}");
        IslandSaveData.CurrentlySavingIsland = empty; IslandSaveData.isSavingGame = true;
        var slot10Repair = new MusketeerPersistence.SaveCapture { Campaign = 0, Land = 10, Challenge = 0 };
        slot10Repair.Begin();
        slot10Repair.Capture(slot10RepairRoot.Add(new Persistent()), "slot10-foreign");
        slot10Repair.Apply();
        FinishSave(slot10Repair);
        Check(slot10Repair.Applied, "real slot10 checkpoint capture/apply stages a matching proof");
        // This Accept is the real pre-change Gate Pass for the active-paid fixture too: every
        // proof, including slot11, matched the complete repaired payload.
        Accept(Payload());
        Console.WriteLine("ACTIVE-PAID pre-change payload accepted by the real gate");

        // With every proof repaired, the paid fixture is live before any change. Only then can a
        // paid-specific mutation be attributed to the active-paid group.
        reloaded.CurrentIsland = loadedTarget;
        reloaded.CurrentLand = 11;
        Time.frameCount++;
        MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "paid fixture is live before the missed-GetID save");
        byte[] paidBefore = File.ReadAllBytes(DiskPath);
        string paidProofHash = MusketeerIdentity.Islands[context11].MatchHash;
        Guid[] paidIds = MusketeerIdentity.Islands[context11].Careers.Select(x => x.Id).OrderBy(x => x).ToArray();
        Check(!string.IsNullOrEmpty(paidProofHash), "slot11 state exposes its accepted proof hash");

        // Change the paid island's native objects (hash-significant; not just the excluded clock),
        // then save without any GetID: no sidecar stage and an independent real Gate refusal.
        string paidOldJson = loadedTarget.Json;
        var paidOldObjects = loadedTarget.objects;
        loadedTarget.playTimeDays = 6;
        loadedTarget.Json = IslandJsonAt(11, 6, Row("slot11-gun", ToolBowPath), Row("slot11-unit", ArcherPath),
            "{\"uniqueID\":\"slot11-foreign\"}");
        loadedTarget.objects = new List<IslandSaveData.ObjectData>
        {
            new() { uniqueID = "slot11-gun" }, new() { uniqueID = "slot11-unit" }, new() { uniqueID = "slot11-foreign" }
        };
        string paidChangedHash = MusketeerArchive.IslandHash(loadedTarget.Json, epoch11);
        Check(paidChangedHash != paidProofHash, "active-paid change is hash-significant (objects, not only clock)");
        Console.WriteLine("ACTIVE-PAID proof hash: before=" + paidProofHash + " changed=" + paidChangedHash);
        var missed = BeginDeferred(loadedTarget);
        missed.Apply();
        FinishSave(missed);
        var missedGate = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, Payload(), false, out _, out string missedReason);
        Console.WriteLine("ZERO-GETID active-paid: verdict=" + missedGate + " reason=" + missedReason);
        Check(missedGate == MusketeerNativeSave.WriterVerdict.Fail && missedReason == MusketeerNativeSave.ReasonNoMatchingSnapshot,
            "active paid row with missed GetID fails its own hash proof: " + missedReason);
        Check(File.ReadAllBytes(DiskPath).SequenceEqual(paidBefore), "active-paid missed-GetID refusal leaves native bytes unchanged");
        Check(MusketeerIdentity.Islands[context11].Epoch == epoch11
            && MusketeerIdentity.Islands[context11].Careers.Select(x => x.Id).OrderBy(x => x).SequenceEqual(paidIds),
            "active-paid missed-GetID refusal preserves epoch and paid rights");
        loadedTarget.playTimeDays = 4;
        loadedTarget.Json = paidOldJson;
        loadedTarget.objects = paidOldObjects;
        var paidRestored = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, Payload(), false, out var paidRestoredCall, out string paidRestoredReason);
        Check(paidRestored == MusketeerNativeSave.WriterVerdict.Pass,
            "restoring the captured paid JSON passes again, isolating the changed hash: " + paidRestoredReason);
        MusketeerNativeSave.WriterStarted(paidRestoredCall,
            new Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> { IsCompleted = true });

        // Existing adapter endpoint failure behavior remains unchanged.
        MusketeerNativeSave.ResetForTests();
        Il2CppSystem.Threading.Tasks.Task.ForceFailure = true;
        MusketeerPersistence.ReadArchive();
        Check(!MusketeerIdentity.CanPurchase, "actual adapter endpoint failure blocks purchases before charging");
        Check(!MusketeerIdentity.TryRegisterPaidGun(new GameObject { tag = "Bow" }.Add(new DroppableTool())),
            "actual adapter endpoint failure cannot create a new paid career");
        Check(MusketeerIdentity.IsGun(coldSlotTool) && MusketeerIdentity.IsUnit(coldSlotArcher),
            "unavailable rejection endpoint preserves existing paid identities");
        Console.WriteLine("ALL PASS producer integration: " + assertions + " assertions");
    }

    static string Decompress(byte[] payload)
    {
        using var input = new MemoryStream(payload);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    static void CheckPromotion(GameObject unit, MusketeerIdentity.PromotionState promotion)
    {
        var actor = unit.GetComponent<Character>() ?? unit.Add(new Character());
        var archer = unit.Add(new Archer());
        MusketeerIdentity.OnGunPickupEnd(actor, promotion);
        MusketeerIdentity.OnGunPickupAbort(promotion);
        Check(MusketeerIdentity.IsUnit(archer), "actual career promotion preserves role");
    }
}
