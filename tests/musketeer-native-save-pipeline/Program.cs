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

        string only = Environment.GetEnvironmentVariable("KEM_CASE");
        if (!string.IsNullOrEmpty(only))
        {
            switch (only)
            {
                case "wrongslot-samehash": WrongSlotSameHashTests(); break;
                case "r2-explicit-raw0-hooks": R2ExplicitRawZeroHookTests(); break;
                case "preimage-ambiguity": PreimageAmbiguityTests(); break;
                case "p2-cross-epoch": P2CrossEpochTests(); break;
                case "boarding-voyage": BoardingVoyageTests(); break;
                default: throw new Exception("unknown case " + only);
            }
            Console.WriteLine("CASE PASS " + only + " assertions=" + assertions);
            return;
        }

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
        var zero = BeginDeferred(empty);
        zero.Apply();
        FinishSave(zero);
        var zeroGate = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, Payload(), false, out var zeroCall, out string zeroReason);
        Console.WriteLine("ZERO-GETID no-purchase: verdict=" + zeroGate + " reason=" + zeroReason);
        // R4 bounded compatibility (policy, not a source proof): the outgoing payload differs from
        // the generation baseline only by the native-finalized land token on the same empty island.
        Check(zeroGate == MusketeerNativeSave.WriterVerdict.Pass,
            "zero-purchase land-only finalization matches the bounded preimage: " + zeroReason);
        MusketeerNativeSave.WriterStarted(zeroCall,
            new Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> { IsCompleted = true });
        // The original zero-GetID protection: a save that changed hash-significant native content
        // without any GetID capture cannot pass, and a rejection leaves the native file untouched.
        byte[] beforeZero = File.ReadAllBytes(DiskPath);
        empty.Json = IslandJsonAt(10, 1, "{\"uniqueID\":\"slot10-zero-foreign\"}");
        var zeroChanged = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, Payload(), false, out _, out string zeroReason2);
        Console.WriteLine("ZERO-GETID changed-content: verdict=" + zeroChanged + " reason=" + zeroReason2);
        Check(zeroChanged == MusketeerNativeSave.WriterVerdict.Fail && zeroReason2 == MusketeerNativeSave.ReasonNoMatchingSnapshot,
            "zero-purchase first save without GetID cannot pass changed native content: " + zeroReason2);
        Check(File.ReadAllBytes(DiskPath).SequenceEqual(beforeZero), "zero-GetID refusal leaves the native file bytes unchanged");
        empty.Json = IslandJsonAt(10, 1);

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
        // R2 through the real hook methods plus the cold read of that accepted raw0 payload.
        // The endpoint-failure switch is cleared: this scenario needs real reject tasks.
        Il2CppSystem.Threading.Tasks.Task.ForceFailure = false;
        MusketeerNativeSave.ResetForTests();
        R2ExplicitRawZeroHookTests();
        ClearIdentity();
        MusketeerNativeSave.ResetForTests();
        WrongSlotSameHashTests();
        ClearIdentity();
        MusketeerNativeSave.ResetForTests();
        PreimageAmbiguityTests();
        ClearIdentity();
        MusketeerNativeSave.ResetForTests();
        P2CrossEpochTests();
        MusketeerRuntime.ResetBoardingDouble();
        MusketeerNativeSave.ResetForTests();
        BoardingVoyageTests();
        Console.WriteLine("ALL PASS producer integration: " + assertions + " assertions");
    }

    // Same-hash wrong-slot: the proof names slot 11. Moving the identical island JSON to another
    // positional member must not satisfy it, even though the hash and bytes are unchanged.
    static void WrongSlotSameHashTests()
    {
        var home = new IslandSaveData { land = 0, isNew = false, playTimeDays = 1, Json = IslandJsonAt(0, 1) };
        var target = new IslandSaveData { land = 0, isNew = true, playTimeDays = 0, Json = IslandJsonAt(0, 0) };
        var campaign = BuildCampaign(home, target);
        campaign.CurrentIsland = target; campaign.CurrentLand = 11;
        GlobalSaveData.loaded = new GlobalSaveData { currentCampaign = 0, currentChallenge = 0, CurrentCampaign = campaign };
        GlobalSaveData.filename = "global-v35";
        GlobalSaveData.loaded.campaigns.Add(campaign);
        CampaignSaveData.current = campaign;
        Managers.Inst = new Managers { world = new World { gameLayer = new GameObject().Add(new Transform()) } };
        var virgin = new MusketeerPersistence.VirginCapture();
        virgin.Begin(campaign);
        virgin.Complete(campaign);
        Time.frameCount++;
        MusketeerIdentity.Tick();
        Check(MusketeerIdentity.Current.ContextKey == Context(0, 0, 11), "wrongslot fixture generated positional slot 11");
        var gun = new GameObject { tag = "Bow" };
        Check(MusketeerIdentity.TryRegisterPaidGun(gun.Add(new DroppableTool())), "wrongslot paid gun registered");

        string slotJson = IslandJsonAt(11, 4, Row("wrongslot-gun", ToolBowPath));
        target.isNew = false; target.playTimeDays = 2;
        target.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "wrongslot-gun" } };
        target.Json = IslandJsonAt(0, 2, Row("wrongslot-gun", ToolBowPath));
        var deferred = BeginDeferred(target);
        deferred.Capture(gun.Add(new Persistent()), "wrongslot-gun");
        Check(deferred.ContextKey == Context(0, 0, 11), "wrongslot deferred source resolves slot 11");
        target.land = 11; target.playTimeDays = 4; target.Json = slotJson;
        deferred.Apply();
        FinishSave(deferred);
        Check(deferred.Applied, "wrongslot fixture stages the slot11 proof");

        // The control goes through the real accepted writer: its lease must be released before the
        // mutated payload is judged (otherwise the next verdict is merely Busy).
        Accept(Payload());
        Console.WriteLine("WRONGSLOT control payload accepted by the real gate");

        home.Json = slotJson;                                        // identical island moved to index 0
        target.Json = IslandJsonAt(11, 6, Row("wrongslot-other", ArcherPath));
        var verdict = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, Payload(), false, out _, out string reason);
        Check(verdict != MusketeerNativeSave.WriterVerdict.Pass,
            "same JSON moved to another slot cannot satisfy the slot11 proof: " + reason);
        Check(reason == MusketeerNativeSave.ReasonNoMatchingSnapshot,
            "wrong-slot rejection is a snapshot/index miss, not a catalog or hash-kind artifact: " + reason);

        home.Json = IslandJsonAt(0, 1);
        target.Json = slotJson;
        var restored = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, Payload(), false, out var restoredCall, out string restoredReason);
        Check(restored == MusketeerNativeSave.WriterVerdict.Pass, "restoring the positional payload passes again: " + restoredReason);
        MusketeerNativeSave.WriterStarted(restoredCall,
            new Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> { IsCompleted = true });
    }

    static MusketeerPersistence.SaveCapture HookBefore(int campaign, int land, int challenge)
    {
        var method = typeof(MusketeerPersistence.SavePatch).GetMethod("Before", BindingFlags.Static | BindingFlags.NonPublic);
        object[] args = { campaign, land, challenge, null };
        method.Invoke(null, args);
        return (MusketeerPersistence.SaveCapture)args[3];
    }

    static void HookAfter(MusketeerPersistence.SaveCapture capture)
        => typeof(MusketeerPersistence.SavePatch).GetMethod("After", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { capture });

    static void HookFinally(MusketeerPersistence.SaveCapture capture)
        => typeof(MusketeerPersistence.SavePatch).GetMethod("Finally", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { null, capture });

    static void HookGetId(GameObject root, string id)
        => typeof(MusketeerPersistence.GetIdPatch).GetMethod("After", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { root.Add(new Persistent()), id });

    // R2 through the real Harmony entry methods: Save(0,11,0) while the generated island's native
    // land field is still 0. Then the cold read rebuilds a fresh owner from the accepted payload:
    // raw0 is still refused without a native load, and only the explicit DTO copy whose sole delta
    // is the proven native land rewrite restores the original epoch/rows through the preimage.
    static void R2ExplicitRawZeroHookTests()
    {
        var home = new IslandSaveData { land = 0, isNew = false, playTimeDays = 1, Json = IslandJsonAt(0, 1) };
        var target = new IslandSaveData { land = 0, isNew = true, playTimeDays = 0, Json = IslandJsonAt(0, 0) };
        var campaign = BuildCampaign(home, target);
        campaign.CurrentIsland = target; campaign.CurrentLand = 11;
        GlobalSaveData.loaded = new GlobalSaveData { currentCampaign = 0, currentChallenge = 0, CurrentCampaign = campaign };
        GlobalSaveData.filename = "global-v35";
        GlobalSaveData.loaded.campaigns.Add(campaign);
        CampaignSaveData.current = campaign;
        Managers.Inst = new Managers { world = new World { gameLayer = new GameObject().Add(new Transform()) } };
        var virgin = new MusketeerPersistence.VirginCapture();
        virgin.Begin(campaign);
        virgin.Complete(campaign);
        Time.frameCount++;
        MusketeerIdentity.Tick();
        string context11 = Context(0, 0, 11);
        string epoch11 = MusketeerIdentity.Current.Epoch;
        Check(MusketeerIdentity.Current.ContextKey == context11, "R2 hook fixture generated slot 11 through the real chain");

        var gun = new GameObject { tag = "Bow" };
        var unitGun = new GameObject { tag = "Bow" };
        Check(MusketeerIdentity.TryRegisterPaidGun(gun.Add(new DroppableTool()), 1), "R2 hook paid rack gun registered");
        Check(MusketeerIdentity.TryRegisterPaidGun(unitGun.Add(new DroppableTool())), "R2 hook unit gun registered");
        MusketeerIdentity.OnGunPickupBegin(unitGun.GetComponent<DroppableTool>(), out var promotion);
        var unit = new GameObject();
        CheckPromotion(unit, promotion);
        Guid[] ids = MusketeerIdentity.Current.Careers.Select(x => x.Id).OrderBy(x => x).ToArray();
        Check(ids.Length == 2, "R2 hook fixture has exactly the two paid careers");

        string json0 = IslandJsonAt(0, 2, Row("slot11-gun", ToolBowPath), Row("slot11-unit", ArcherPath),
            "{\"uniqueID\":\"slot11-plain\",\"prefabPath\":\"Prefabs/Objects/Tree\"}");
        target.isNew = false; target.playTimeDays = 2; target.land = 0; target.Json = json0;
        target.objects = new List<IslandSaveData.ObjectData>
        {
            new() { uniqueID = "slot11-gun" }, new() { uniqueID = "slot11-unit" }, new() { uniqueID = "slot11-plain" }
        };
        IslandSaveData.CurrentlySavingIsland = target; IslandSaveData.isSavingGame = true;
        var capture = HookBefore(0, 11, 0);
        HookGetId(gun, "slot11-gun");
        HookGetId(unit, "slot11-unit");
        Check(capture.Island != null && capture.ContextKey == context11,
            "R2 hook first GetID latches the frozen owner slot 11 while raw land is 0");
        Check(target.land == 0, "R2 hook never rewrites the native land field");
        HookAfter(capture);
        HookFinally(capture);
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        Check(capture.Applied && !capture.Conflict, "R2 hook Apply stages under the frozen generation epoch");
        Check(MusketeerIdentity.Islands[context11].Epoch == epoch11, "R2 hook save keeps the generation epoch");
        var staged = Payload();
        Accept(staged);   // the real physical gate must accept the raw0 producer payload
        Console.WriteLine("R2 HOOK accepted raw0 producer payload through the real gate");

        // ---- cold read from the actual accepted payload ----
        Check(MusketeerNativeArchive.TryExtract(staged, "global-v35", out string nativeRaw, out _, out _, out string extractReason),
            "R2 cold fixture decodes the accepted native payload: " + extractReason);
        // The cold DTO is rebuilt from the actual accepted native gzip, not from a local literal:
        // parse root.campaigns[0]._islands[11], its raw land token and its object rows.
        string acceptedDocument = Decompress(staged);
        using var accepted = JsonDocument.Parse(acceptedDocument);
        var acceptedIslands = accepted.RootElement.GetProperty("campaigns")[0].GetProperty("_islands");
        Check(acceptedIslands.GetArrayLength() == 12, "accepted payload carries twelve positional islands");
        var acceptedTarget = acceptedIslands[11];
        Check(acceptedTarget.ValueKind == JsonValueKind.Object
            && acceptedTarget.EnumerateObject().Count(property => property.NameEquals("land")) == 1
            && acceptedTarget.GetProperty("land").GetInt32() == 0,
            "accepted target island payload really carries a single raw land 0");
        string acceptedIslandJson = acceptedTarget.GetRawText();
        int landToken = acceptedIslandJson.IndexOf("\"land\":0", StringComparison.Ordinal);
        Check(landToken >= 0 && acceptedIslandJson.IndexOf("\"land\":0", landToken + 1, StringComparison.Ordinal) < 0,
            "accepted island JSON has exactly one raw land token to rewrite");
        string[] acceptedRowIds = acceptedTarget.GetProperty("objects").EnumerateArray()
            .Select(row => row.GetProperty("uniqueID").GetString()).ToArray();
        Check(acceptedRowIds.Length == 3 && acceptedRowIds.Contains("slot11-gun")
            && acceptedRowIds.Contains("slot11-unit") && acceptedRowIds.Contains("slot11-plain"),
            "accepted target island payload carries the captured row ids");
        var coldHome = new IslandSaveData { land = 0, isNew = false, playTimeDays = 1, Json = IslandJsonAt(0, 1) };
        var coldRaw0 = new IslandSaveData
        {
            land = 0, isNew = false, playTimeDays = 2, Json = acceptedIslandJson,
            objects = acceptedRowIds.Select(id => new IslandSaveData.ObjectData { uniqueID = id }).ToList()
        };
        var cold = BuildCampaign(coldHome, coldRaw0);
        cold.CurrentIsland = coldRaw0; cold.CurrentLand = 11;
        GlobalSaveData.loaded = new GlobalSaveData { currentCampaign = 0, currentChallenge = 0, CurrentCampaign = cold };
        GlobalSaveData.loaded.campaigns.Add(cold);
        GlobalSaveData.loaded.prefs.contents[MusketeerNativeArchive.Key] = nativeRaw;
        CampaignSaveData.current = cold;
        Managers.Inst = new Managers { world = new World { gameLayer = new GameObject().Add(new Transform()) } };
        ClearIdentity();
        MusketeerNativeSave.ResetForTests();
        Time.frameCount++;
        var raw0Load = new MusketeerPersistence.LoadCapture();
        raw0Load.Begin(coldRaw0);
        Check(!MusketeerIdentity.Islands.ContainsKey(context11),
            "cold non-virgin raw0 load without a native rewrite is still rejected");
        bool raw0Untouched = false;
        try
        {
            raw0Untouched = MusketeerNativeArchive.TryDecode("global-v35", nativeRaw, out _, out _, out _);
        }
        catch (Exception) { }
        Check(raw0Untouched, "raw0 rejection leaves the native archive readable and unchanged");

        string dtoJson = acceptedIslandJson.Substring(0, landToken) + "\"land\":11"
            + acceptedIslandJson.Substring(landToken + "\"land\":0".Length);
        var dto = new IslandSaveData
        {
            land = 11, isNew = false, playTimeDays = 2, Json = dtoJson,
            objects = acceptedRowIds.Select(id => new IslandSaveData.ObjectData { uniqueID = id }).ToList()
        };
        Check(dto.Json != acceptedIslandJson && dto.Json.Length == acceptedIslandJson.Length + 1,
            "cold DTO copy differs from the accepted island only by the native land rewrite");
        cold._islands[11] = dto; cold.CurrentIsland = dto; cold.CurrentLand = 11;
        var coldGun = new GameObject { tag = "Bow" };
        var coldGunTool = coldGun.Add(new DroppableTool());
        var coldUnit = new GameObject();
        coldUnit.Add(new Character()); coldUnit.Add(new Archer());
        string acceptedHash = MusketeerArchive.IslandHash(json0, epoch11);
        Check(MusketeerArchive.IslandHashPreimage(dto.Json, epoch11, 11) == acceptedHash,
            "cold DTO projection equals the accepted raw0 hash");
        var load = new MusketeerPersistence.LoadCapture();
        load.Begin(dto);
        load.Capture(dto.objects[0], coldGun.Add(new Persistent()));
        load.Capture(dto.objects[1], coldUnit.Add(new Persistent()));
        load.End(true);
        Check(MusketeerIdentity.IsGun(coldGunTool), "cold DTO load restores the paid rack gun");
        Check(MusketeerIdentity.IsUnit(coldUnit.GetComponent<Archer>()), "cold DTO load restores the paid unit");
        Check(MusketeerIdentity.Current.Epoch == epoch11, "cold DTO load restores the original epoch");
        Check(MusketeerIdentity.Current.Careers.Select(x => x.Id).OrderBy(x => x).SequenceEqual(ids),
            "cold DTO load restores the exact paid GUID set");
        Check(MusketeerIdentity.Current.Careers.Any(x => x.Kind == MusketeerCareer.KindUnit && x.NativeId == "slot11-unit")
            && MusketeerIdentity.Current.Careers.Any(x => x.Kind == MusketeerCareer.KindGun && x.NativeId == "slot11-gun"),
            "cold DTO load restores kind and witnessed NativeId");
        Check(MusketeerIdentity.StockSlot(coldGunTool) == 1,
            "cold DTO load restores the rack StockSlot");
        Check(MusketeerIdentity.Current.HasBaseline, "cold DTO load keeps the pinned raw0 baseline");

        // The cold owner now holds a proof for the old raw0 hash. A payload with the DTO's land11
        // island satisfies it only through the bounded preimage, and a content change still fails.
        var coldPass = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, Payload(), false, out var coldCall, out string coldWhy);
        Check(coldPass == MusketeerNativeSave.WriterVerdict.Pass, "cold DTO payload passes the physical gate via the preimage: " + coldWhy);
        MusketeerNativeSave.WriterStarted(coldCall,
            new Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> { IsCompleted = true });
        dto.Json = IslandJsonAt(11, 2, Row("slot11-gun", ToolBowPath), Row("slot11-unit", ArcherPath),
            "{\"uniqueID\":\"slot11-plain\",\"prefabPath\":\"Prefabs/Objects/Tree\"}",
            "{\"uniqueID\":\"slot11-extra\",\"prefabPath\":\"Prefabs/Objects/Tree\"}");
        var coldFail = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, Payload(), false, out _, out string coldFailWhy);
        Check(coldFail == MusketeerNativeSave.WriterVerdict.Fail && coldFailWhy == MusketeerNativeSave.ReasonNoMatchingSnapshot,
            "a non-land content change never satisfies the preimage: " + coldFailWhy);
    }

    // Indexed physical proof: when the proof epoch carries both an exact land-slot snapshot and a
    // raw0 preimage snapshot whose paid records disagree, the writer refuses as ambiguous instead
    // of choosing the candidate favorable to the proof hash.
    static void PreimageAmbiguityTests()
    {
        var home = new IslandSaveData { land = 0, isNew = false, playTimeDays = 1, Json = IslandJsonAt(0, 1) };
        string json0 = IslandJsonAt(0, 2, Row("amb-gun", ToolBowPath));
        string jsonSlot = IslandJsonAt(11, 2, Row("amb-gun", ToolBowPath));
        var target = new IslandSaveData { land = 11, isNew = false, playTimeDays = 2, Json = jsonSlot };
        var campaign = BuildCampaign(home, target);
        campaign.CurrentIsland = target; campaign.CurrentLand = 11;
        GlobalSaveData.loaded = new GlobalSaveData { currentCampaign = 0, currentChallenge = 0, CurrentCampaign = campaign };
        GlobalSaveData.filename = "global-v35";
        GlobalSaveData.loaded.campaigns.Add(campaign);
        CampaignSaveData.current = campaign;
        Managers.Inst = new Managers { world = new World { gameLayer = new GameObject().Add(new Transform()) } };
        string context = Context(0, 0, 11);
        string epoch = MusketeerArchive.NewScope();
        var paid = new MusketeerCareer
        {
            Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun, NativeId = "amb-gun", StockSlot = MusketeerCareer.NoStockSlot
        };
        var archive = new MusketeerArchive();
        Check(archive.Record(epoch, MusketeerArchive.IslandHash(json0, epoch), new[] { paid }),
            "ambiguity fixture records the paid raw0 preimage snapshot");
        Check(archive.Record(epoch, MusketeerArchive.IslandHash(jsonSlot, epoch), Array.Empty<MusketeerCareer>()),
            "ambiguity fixture records the empty exact land-slot snapshot");
        Check(archive.EnsureContext(context, epoch, true), "ambiguity fixture registers the slot 11 context");
        Check(MusketeerIdentity.TryContextKey(0, 0, 11, out _), "ambiguity fixture observes the slot 11 source");
        var expected = MusketeerNativeSave.ReadArchive();
        Check(MusketeerNativeSave.Stage(context, epoch, MusketeerArchive.IslandHash(jsonSlot, epoch), expected, archive),
            "ambiguity fixture stages the exact land-slot proof through the real producer API");
        var verdict = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, Payload(), false, out _, out string reason);
        Check(verdict == MusketeerNativeSave.WriterVerdict.Fail && reason == MusketeerNativeSave.ReasonScopeAmbiguous,
            "disagreeing exact/preimage candidates refuse as ScopeAmbiguous: " + reason);
    }

    // ---- R4 round3 P2: a preimage-used proof must respect every eligible epoch ----

    static string P2Json(int land) => IslandJsonAt(land, 2, Row("p2-gun", ToolBowPath));

    static (CampaignSaveData campaign, IslandSaveData target) P2Campaign(string jsonSlot, bool challenge)
    {
        var home = new IslandSaveData { land = 0, isNew = false, playTimeDays = 1, Json = IslandJsonAt(0, 1) };
        var target = new IslandSaveData { land = 11, isNew = false, playTimeDays = 2, Json = jsonSlot };
        var campaign = BuildCampaign(home, target);
        campaign.CurrentIsland = target; campaign.CurrentLand = 11;
        if (challenge) campaign.challengeId = 7;
        var global = new GlobalSaveData
        {
            currentCampaign = challenge ? -1 : 0,
            currentChallenge = challenge ? 7 : 0,
            CurrentCampaign = campaign
        };
        GlobalSaveData.filename = "global-v35";
        if (challenge) global.challenges.Add(campaign); else global.campaigns.Add(campaign);
        GlobalSaveData.loaded = global;
        CampaignSaveData.current = campaign;
        Managers.Inst = new Managers { world = new World { gameLayer = new GameObject().Add(new Transform()) } };
        return (campaign, target);
    }

    static string P2Context(bool challenge) => challenge ? Context(-1, 7, 11) : Context(0, 0, 11);

    static void P2Stage(MusketeerArchive archive, string context, string epoch, string hash, bool challenge)
    {
        Check(MusketeerIdentity.TryContextKey(challenge ? -1 : 0, challenge ? 7 : 0, 11, out _),
            "P2 fixture observes the indexed slot");
        var expected = MusketeerNativeSave.ReadArchive();
        Check(MusketeerNativeSave.Stage(context, epoch, hash, expected, archive), "P2 proof stages through the producer API");
    }

    static MusketeerNativeSave.WriterVerdict P2Gate(out string reason)
    {
        var verdict = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, Payload(), false, out var call, out reason);
        if (verdict == MusketeerNativeSave.WriterVerdict.Pass && call != null)
            MusketeerNativeSave.WriterStarted(call, new Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> { IsCompleted = true });
        return verdict;
    }

    static MusketeerArchive P2Base(string context, string epoch, string json0, MusketeerCareer paid)
    {
        var archive = new MusketeerArchive();
        Check(archive.ConfirmBaseline(epoch, MusketeerArchive.IslandHash(json0, epoch), new[] { paid }), "P2 base snapshot");
        Check(archive.EnsureContext(context, epoch, true), "P2 base context");
        return archive;
    }

    static void P2AddEpoch(MusketeerArchive archive, string context, string epoch, string jsonSlot, params MusketeerCareer[] records)
    {
        Check(archive.Record(epoch, MusketeerArchive.IslandHash(jsonSlot, epoch), records), "P2 other epoch snapshot");
        Check(archive.EnsureContext(context, epoch, true), "P2 other epoch joins the context");
    }

    static MusketeerCareer P2Record(Guid id, string nativeId, int kind = MusketeerCareer.KindGun)
        => new() { Id = id, Kind = kind, NativeId = nativeId, StockSlot = MusketeerCareer.NoStockSlot };

    static void P2CrossEpochTests()
    {
        string json0 = P2Json(0);
        string jsonSlot = P2Json(11);
        var paid = P2Record(Guid.NewGuid(), "p2-gun");

        // control + business red on the current NativeSave: Ea/H0 is satisfied by the preimage,
        // while Eb holds the exact land-slot snapshot with empty records.
        ClearIdentity(); MusketeerNativeSave.ResetForTests();
        P2Campaign(jsonSlot, false);
        string context = P2Context(false);
        string epochA = MusketeerArchive.NewScope();
        var archive = P2Base(context, epochA, json0, paid);
        string hashA = MusketeerArchive.IslandHash(json0, epochA);
        P2Stage(archive, context, epochA, hashA, false);
        var control = P2Gate(out string controlWhy);
        Check(control == MusketeerNativeSave.WriterVerdict.Pass,
            "P2 control: proof Ea/H0 is satisfied by the preimage: " + controlWhy);
        string epochB = MusketeerArchive.NewScope();
        P2AddEpoch(archive, context, epochB, jsonSlot);
        P2Stage(archive, context, epochA, hashA, false);
        byte[] diskBefore = File.Exists(DiskPath) ? File.ReadAllBytes(DiskPath) : null;
        string prefsBefore = GlobalSaveData.loaded.prefs.contents[MusketeerNativeArchive.Key];
        string pairBefore = File.Exists(PairPath) ? Convert.ToHexString(File.ReadAllBytes(PairPath)) : "";
        var p2 = P2Gate(out string p2Why);
        Check(p2 == MusketeerNativeSave.WriterVerdict.Fail && p2Why == MusketeerNativeSave.ReasonScopeAmbiguous,
            "preimage-used proof rejects a conflicting exact snapshot in another eligible epoch: " + p2Why);
        bool diskSame = diskBefore == null
            ? !File.Exists(DiskPath)
            : File.Exists(DiskPath) && File.ReadAllBytes(DiskPath).SequenceEqual(diskBefore);
        Check(diskSame
            && GlobalSaveData.loaded.prefs.contents[MusketeerNativeArchive.Key] == prefsBefore
            && (File.Exists(PairPath) ? Convert.ToHexString(File.ReadAllBytes(PairPath)) : "") == pairBefore,
            "P2 rejection leaves native disk/pair/prefs unchanged");

        // Agreeing cross-epoch records still pass.
        ClearIdentity(); MusketeerNativeSave.ResetForTests();
        P2Campaign(jsonSlot, false);
        context = P2Context(false); epochA = MusketeerArchive.NewScope();
        archive = P2Base(context, epochA, json0, paid);
        hashA = MusketeerArchive.IslandHash(json0, epochA);
        P2Stage(archive, context, epochA, hashA, false);
        P2AddEpoch(archive, context, MusketeerArchive.NewScope(), jsonSlot,
            P2Record(paid.Id, paid.NativeId, paid.Kind));
        P2Stage(archive, context, epochA, hashA, false);
        Check(P2Gate(out string sameWhy) == MusketeerNativeSave.WriterVerdict.Pass,
            "agreeing cross-epoch records still pass: " + sameWhy);

        // A different paid career in Eb conflicts.
        ClearIdentity(); MusketeerNativeSave.ResetForTests();
        P2Campaign(jsonSlot, false);
        context = P2Context(false); epochA = MusketeerArchive.NewScope();
        archive = P2Base(context, epochA, json0, paid);
        hashA = MusketeerArchive.IslandHash(json0, epochA);
        P2Stage(archive, context, epochA, hashA, false);
        P2AddEpoch(archive, context, MusketeerArchive.NewScope(), jsonSlot, P2Record(Guid.NewGuid(), "p2-other"));
        P2Stage(archive, context, epochA, hashA, false);
        Check(P2Gate(out string qWhy) == MusketeerNativeSave.WriterVerdict.Fail
            && qWhy == MusketeerNativeSave.ReasonScopeAmbiguous,
            "a different paid career in another epoch is ambiguous: " + qWhy);

        // Baseline-pinned conflicting history is not favored.
        ClearIdentity(); MusketeerNativeSave.ResetForTests();
        P2Campaign(jsonSlot, false);
        context = P2Context(false); epochA = MusketeerArchive.NewScope();
        archive = P2Base(context, epochA, json0, paid);
        hashA = MusketeerArchive.IslandHash(json0, epochA);
        P2Stage(archive, context, epochA, hashA, false);
        string pinnedEpoch = MusketeerArchive.NewScope();
        Check(archive.ConfirmBaseline(pinnedEpoch, MusketeerArchive.IslandHash(jsonSlot, pinnedEpoch),
            new[] { P2Record(Guid.NewGuid(), "p2-pinned") }), "P2 pinned conflicting snapshot");
        Check(archive.EnsureContext(context, pinnedEpoch, true), "P2 pinned epoch joins the context");
        P2Stage(archive, context, epochA, hashA, false);
        Check(P2Gate(out string pinnedWhy) == MusketeerNativeSave.WriterVerdict.Fail
            && pinnedWhy == MusketeerNativeSave.ReasonScopeAmbiguous,
            "a baseline-pinned conflicting epoch does not win: " + pinnedWhy);

        // HistoricalOnly exclusion (valid persisted Complete ledger) skips the source epoch.
        ClearIdentity(); MusketeerNativeSave.ResetForTests();
        P2Campaign(jsonSlot, false);
        context = P2Context(false); epochA = MusketeerArchive.NewScope();
        archive = P2Base(context, epochA, json0, paid);
        hashA = MusketeerArchive.IslandHash(json0, epochA);
        P2Stage(archive, context, epochA, hashA, false);
        string sourceEpoch = MusketeerArchive.NewScope();
        string migrationEpoch = MusketeerArchive.NewScope();
        var q1 = P2Record(Guid.NewGuid(), ""); var q2 = P2Record(Guid.NewGuid(), "");
        P2AddEpoch(archive, context, sourceEpoch, jsonSlot, q1, q2);
        var grant = Guid.NewGuid();
        Check(archive.Record(migrationEpoch, MusketeerArchive.IslandHash("{\"migration\":1}", migrationEpoch),
            new[] { P2Record(grant, "grant-1", MusketeerCareer.KindUnit) }), "P2 migration snapshot");
        Check(archive.EnsureContext(context, migrationEpoch, true), "P2 migration epoch joins the context");
        var plan = new MusketeerLegacyCompensationPlan(1, Guid.NewGuid(), context, sourceEpoch, new string('a', 64), 2, 1);
        Check(MusketeerLegacyCompensationPolicy.TryCreatePending(plan, migrationEpoch, new[] { grant }, out var pending, out string pendingWhy), pendingWhy);
        Check(MusketeerLegacyCompensationPolicy.TryComplete(pending,
            new[] { new MusketeerLegacyGrantCapture(grant, "grant-1", MusketeerLegacyGrantCapture.UnitKind,
                MusketeerLegacyGrantCapture.MusketeerPrefabPath, 1) }, out var completed, out string completeWhy), completeWhy);
        archive.Compensations[plan.RequestId] = completed;
        P2Stage(archive, context, epochA, hashA, false);
        Check(P2Gate(out string historicalWhy) == MusketeerNativeSave.WriterVerdict.Pass,
            "HistoricalOnly source epoch is excluded without runtime state: " + historicalWhy);

        // Cross-context history is never searched.
        ClearIdentity(); MusketeerNativeSave.ResetForTests();
        P2Campaign(jsonSlot, false);
        context = P2Context(false); epochA = MusketeerArchive.NewScope();
        archive = P2Base(context, epochA, json0, paid);
        hashA = MusketeerArchive.IslandHash(json0, epochA);
        P2Stage(archive, context, epochA, hashA, false);
        string otherContext = Context(0, 0, 4);
        string otherEpoch = MusketeerArchive.NewScope();
        Check(archive.Record(otherEpoch, MusketeerArchive.IslandHash(jsonSlot, otherEpoch),
            new[] { P2Record(Guid.NewGuid(), "p2-other-context") }), "P2 other-context snapshot");
        Check(archive.EnsureContext(otherContext, otherEpoch, true), "P2 other-context registration");
        P2Stage(archive, context, epochA, hashA, false);
        Check(P2Gate(out string crossContextWhy) == MusketeerNativeSave.WriterVerdict.Pass,
            "cross-context histories are not part of this proof: " + crossContextWhy);

        // Exact-used proof pins the R3 acceptance surface: no cross-epoch scan.
        ClearIdentity(); MusketeerNativeSave.ResetForTests();
        P2Campaign(jsonSlot, false);
        context = P2Context(false); epochA = MusketeerArchive.NewScope();
        var exactArchive = new MusketeerArchive();
        Check(exactArchive.ConfirmBaseline(epochA, MusketeerArchive.IslandHash(jsonSlot, epochA), new[] { paid }),
            "P2 exact base snapshot");
        Check(exactArchive.EnsureContext(context, epochA, true), "P2 exact base context");
        P2Stage(exactArchive, context, epochA, MusketeerArchive.IslandHash(jsonSlot, epochA), false);
        P2AddEpoch(exactArchive, context, MusketeerArchive.NewScope(), jsonSlot, P2Record(Guid.NewGuid(), "p2-other"));
        P2Stage(exactArchive, context, epochA, MusketeerArchive.IslandHash(jsonSlot, epochA), false);
        Check(P2Gate(out string exactWhy) == MusketeerNativeSave.WriterVerdict.Pass,
            "exact-used proofs keep the pre-P2 acceptance surface: " + exactWhy);

        // Challenge proof: unique challengeId locates the same indexed island; P2 applies.
        ClearIdentity(); MusketeerNativeSave.ResetForTests();
        P2Campaign(jsonSlot, true);
        context = P2Context(true); epochA = MusketeerArchive.NewScope();
        archive = P2Base(context, epochA, json0, paid);
        hashA = MusketeerArchive.IslandHash(json0, epochA);
        P2Stage(archive, context, epochA, hashA, true);
        Check(P2Gate(out string challengeControlWhy) == MusketeerNativeSave.WriterVerdict.Pass,
            "challenge control passes: " + challengeControlWhy);
        P2AddEpoch(archive, context, MusketeerArchive.NewScope(), jsonSlot);
        P2Stage(archive, context, epochA, hashA, true);
        Check(P2Gate(out string challengeWhy) == MusketeerNativeSave.WriterVerdict.Fail
            && challengeWhy == MusketeerNativeSave.ReasonScopeAmbiguous,
            "challenge preimage-used proof respects the same cross-epoch rule: " + challengeWhy);

        // Non-land content change still misses every candidate.
        ClearIdentity(); MusketeerNativeSave.ResetForTests();
        var fixture = P2Campaign(jsonSlot, false);
        context = P2Context(false); epochA = MusketeerArchive.NewScope();
        archive = P2Base(context, epochA, json0, paid);
        hashA = MusketeerArchive.IslandHash(json0, epochA);
        P2Stage(archive, context, epochA, hashA, false);
        fixture.target.Json = IslandJsonAt(11, 2, Row("p2-gun", ToolBowPath), Row("p2-extra", ArcherPath));
        Check(P2Gate(out string changedWhy) == MusketeerNativeSave.WriterVerdict.Fail
            && changedWhy == MusketeerNativeSave.ReasonNoMatchingSnapshot,
            "non-land content changes still miss every candidate: " + changedWhy);

        // Real compensation writer barrier is checked before payload validation.
        ClearIdentity(); MusketeerNativeSave.ResetForTests();
        P2Campaign(jsonSlot, false);
        context = P2Context(false);
        File.WriteAllBytes(DiskPath, new byte[] { 1, 2, 3 });
        Check(MusketeerIdentity.TryContextKey(0, 0, 11, out _), "P2 compensation observes the slot");
        Check(MusketeerNativeSave.BeginLegacyCompensation(Guid.NewGuid(), context, MusketeerArchive.NewScope(),
            new[] { Guid.NewGuid() }, out string barrierWhy), barrierWhy);
        // The barrier is checked before any payload inspection, so arbitrary bytes are enough to
        // prove precedence: this cannot pass through the preimage policy.
        var blocked = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, new byte[] { 1, 2, 3 }, false, out _, out string blockedWhy);
        Check(blocked == MusketeerNativeSave.WriterVerdict.Fail && blockedWhy == "LegacyCompensationPartial",
            "pending/materialized compensation blocks the writer before validation: " + blockedWhy);

        // A valid persisted Complete ledger takes over through HistoricalOnly (runtime disabled in
        // this host, so the pass can only come from the archive exclusion).
        ClearIdentity(); MusketeerNativeSave.ResetForTests();
        P2Campaign(jsonSlot, false);
        context = P2Context(false); epochA = MusketeerArchive.NewScope();
        archive = P2Base(context, epochA, json0, paid);
        hashA = MusketeerArchive.IslandHash(json0, epochA);
        string ledgerSource = MusketeerArchive.NewScope();
        string ledgerMigration = MusketeerArchive.NewScope();
        P2AddEpoch(archive, context, ledgerSource, jsonSlot, P2Record(Guid.NewGuid(), ""), P2Record(Guid.NewGuid(), ""));
        var ledgerGrant = Guid.NewGuid();
        Check(archive.Record(ledgerMigration, MusketeerArchive.IslandHash("{\"migration\":2}", ledgerMigration),
            new[] { P2Record(ledgerGrant, "grant-1", MusketeerCareer.KindUnit) }), "P2 ledger migration snapshot");
        Check(archive.EnsureContext(context, ledgerMigration, true), "P2 ledger migration epoch");
        var ledgerPlan = new MusketeerLegacyCompensationPlan(1, Guid.NewGuid(), context, ledgerSource, new string('b', 64), 2, 1);
        Check(MusketeerLegacyCompensationPolicy.TryCreatePending(ledgerPlan, ledgerMigration, new[] { ledgerGrant }, out var ledgerPending, out string ledgerWhy), ledgerWhy);
        Check(MusketeerLegacyCompensationPolicy.TryComplete(ledgerPending,
            new[] { new MusketeerLegacyGrantCapture(ledgerGrant, "grant-1", MusketeerLegacyGrantCapture.UnitKind,
                MusketeerLegacyGrantCapture.MusketeerPrefabPath, 1) }, out var ledgerComplete, out ledgerWhy), ledgerWhy);
        archive.Compensations[ledgerPlan.RequestId] = ledgerComplete;
        P2Stage(archive, context, epochA, hashA, false);
        Check(P2Gate(out string ledgerPassWhy) == MusketeerNativeSave.WriterVerdict.Pass,
            "a persisted Complete ledger lets HistoricalOnly take over the same payload: " + ledgerPassWhy);
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

    // Issue281 boarding policy on the real producer/writer pipeline（真实 Identity/Persistence/
    // NativeSave；Runtime helper 用边界 double——真实字段归还由 runtime 套件证明）：异常已登船
    // musketeer 在显式当前源保存捕获前降级，出航保存完成、无 CaptureFault；未完成降级时原
    // missed-row Guard 与真实 writer gate 拒绝保持；拒绝的物理写不动旧磁盘；无 Reserved 幽灵。
    static void BoardingVoyageTests()
    {
        ClearIdentity();
        MusketeerNativeSave.ResetForTests();
        MusketeerRuntime.ResetBoardingDouble();

        var home = new IslandSaveData { land = 0, isNew = true, playTimeDays = 0, Json = IslandJsonAt(0, 0) };
        var campaign = BuildCampaign(home, null);
        GlobalSaveData.loaded = new GlobalSaveData { currentCampaign = 0, currentChallenge = 0, CurrentCampaign = campaign };
        GlobalSaveData.filename = "global-v35";
        GlobalSaveData.loaded.campaigns.Add(campaign);
        CampaignSaveData.current = campaign;
        Managers.Inst = new Managers { world = new World { gameLayer = new GameObject().Add(new Transform()) } };
        MusketeerAccess.World = Managers.Inst.world.gameLayer;
        var virgin = new MusketeerPersistence.VirginCapture();
        virgin.Begin(campaign);
        virgin.Complete(campaign);
        Time.frameCount++;
        MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "boarding fixture baseline ready");

        // paid careers：unit 的枪（转换后消失）、货架枪（stock slot 0 保留）、普通付费枪（留守）。
        var unitGunGo = new GameObject { tag = "Bow" };
        var unitGun = unitGunGo.Add(new DroppableTool());
        Check(MusketeerIdentity.TryRegisterPaidGun(unitGun), "unit career gun registered");
        MusketeerIdentity.OnGunPickupBegin(unitGun, out var promotion);
        var unitGo = new GameObject();
        var unitCharacter = unitGo.Add(new Character());
        var unitArcher = unitGo.Add(new Archer());
        var unitEmbarkee = unitGo.Add(new Embarkee());
        unitArcher._embarkee = unitEmbarkee;
        unitEmbarkee._owner = new UnitOwnerRef(unitArcher);
        MusketeerIdentity.OnGunPickupEnd(unitCharacter, promotion);
        var rackGunGo = new GameObject { tag = "Bow" };
        var rackGun = rackGunGo.Add(new DroppableTool());
        Check(MusketeerIdentity.TryRegisterPaidGun(rackGun, 0), "rack gun registered with stock slot 0");
        var plainGunGo = new GameObject { tag = "Bow" };
        Check(MusketeerIdentity.TryRegisterPaidGun(plainGunGo.Add(new DroppableTool())), "plain paid gun registered");
        Check(MusketeerIdentity.IsUnit(unitArcher), "unit musketeer is known before boarding");
        var unitCareer = MusketeerIdentity.Current.Careers.Find(c => c.Kind == MusketeerCareer.KindUnit);
        Check(unitCareer != null, "unit career located");
        Guid unitCareerId = unitCareer.Id;
        var before = MusketeerIdentity.Current.Careers
            .OrderBy(x => x.Id).Select(x => (x.Id, x.Kind, x.StockSlot)).ToList();

        // forced aboard（异常状态：原生 registrar 已经接受登船）。
        unitEmbarkee.IsEmbarked = true;

        // 三入口 gate 对已登船者同样拒绝；纯 gate 绝不触发降级 helper。
        bool gate = true;
        MusketeerBoardingPolicy.RefineCanEmbark(unitArcher, ref gate);
        Check(!gate, "aboard musketeer still fails CanEmbark");
        var target = new GameObject().Add(new Embarkable());
        Check(!MusketeerBoardingPolicy.AllowSetEmbarkableTarget(unitEmbarkee, target),
            "aboard musketeer gets no new boarding target");
        Check(!MusketeerBoardingPolicy.AllowEmbark(unitEmbarkee), "aboard musketeer cannot re-embark");
        Check(MusketeerRuntime.PrepareCalls == 0, "pure gates never invoke the demotion helper");

        // 对照：降级未完成（helper false）→ 显式当前源保存仍撞原 missed-row Guard，
        // 真实 writer gate 以 CaptureFault 拒绝。
        MusketeerRuntime.NextResult = false;
        MusketeerBoardingPolicy.DemoteConfirmedEmbarkedBeforeSave(0, 0, 0);
        Check(MusketeerRuntime.PrepareCalls == 1, "demotion helper consulted exactly once");
        Check(MusketeerIdentity.Current.Careers.Count == before.Count,
            "incomplete field return keeps every paid identity");
        home.Json = IslandJsonAt(0, 1, Row("rack-gun", ToolBowPath), Row("plain-gun", ToolBowPath));
        home.objects = new List<IslandSaveData.ObjectData>
        {
            new() { uniqueID = "rack-gun" }, new() { uniqueID = "plain-gun" }
        };
        IslandSaveData.CurrentlySavingIsland = home; IslandSaveData.isSavingGame = true;
        var blocked = new MusketeerPersistence.SaveCapture { Campaign = 0, Land = 0, Challenge = 0 };
        blocked.Begin();
        blocked.Capture(rackGunGo.Add(new Persistent()), "rack-gun");
        blocked.Capture(plainGunGo.Add(new Persistent()), "plain-gun");
        blocked.Apply();
        FinishSave(blocked);
        Check(!blocked.Applied, "un-demoted aboard musketeer keeps the missed-row refusal");
        var faultGate = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, Payload(), false, out _, out string faultWhy);
        Check(faultGate == MusketeerNativeSave.WriterVerdict.Fail && faultWhy == MusketeerNativeSave.ReasonCaptureFault,
            "real writer gate keeps the capture fault until a complete capture: " + faultWhy);

        // 真实降级路径（边界 double 返回 true；真实字段归还由 runtime 套件证明）。
        MusketeerRuntime.NextResult = true;
        Check(MusketeerBoardingPolicy.TryDemoteConfirmedEmbarked(unitArcher),
            "aboard unit demotes through the shared proof");
        Check(MusketeerIdentity.Current.Careers.Count == before.Count - 1,
            "exactly the unit career left the roster");
        var after = MusketeerIdentity.Current.Careers
            .OrderBy(x => x.Id).Select(x => (x.Id, x.Kind, x.StockSlot)).ToList();
        Check(before.FindAll(b => b.Kind != MusketeerCareer.KindUnit).TrueForAll(b => after.Contains(b)),
            "staying paid identities and the rack slot are untouched");
        Check(after.TrueForAll(a => a.Kind != MusketeerCareer.KindUnit), "no unit career remains on the roster");
        Check(!MusketeerIdentity.IsUnit(unitArcher), "demoted unit is no longer a musketeer");

        // 幂等：save-path 降级对已移除的 career 无操作。
        int demoteCalls = MusketeerRuntime.PrepareCalls;
        MusketeerBoardingPolicy.DemoteConfirmedEmbarkedBeforeSave(0, 0, 0);
        Check(MusketeerRuntime.PrepareCalls == demoteCalls, "save-path demotion is idempotent");

        // 同形状显式当前源保存：真实 Apply 完成、真实 gate 通过（无 CaptureFault）。
        var completed = SaveExplicit(home, 0, home.Json,
            ("rack-gun", rackGunGo), ("plain-gun", plainGunGo));
        Check(completed.Applied, "demoted voyage save completes through the real capture path");
        var passGate = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, Payload(), false,
            out var passCall, out string passWhy);
        Check(passGate == MusketeerNativeSave.WriterVerdict.Pass && passCall != null,
            "no capture fault after the demotion: " + passWhy);
        File.WriteAllBytes(DiskPath, Payload());
        MusketeerNativeSave.WriterStarted(passCall,
            new Il2CppSystem.Threading.Tasks.Task<SaveLoadResult> { IsCompleted = true });

        // 物理写保护（措辞限定：本 case 不调用真实 writer，证明的是真实 gate 拒绝时旧磁盘不动；
        // 真实 writer I/O 故障注入不在本套件）：无新捕获的变更内容 payload 被真实 gate 拒绝，
        // 磁盘 bytes 不动。
        byte[] diskBaseline = File.ReadAllBytes(DiskPath);
        home.Json = IslandJsonAt(0, 2, Row("rack-gun", ToolBowPath), Row("plain-gun", ToolBowPath),
            Row("stowaway", ArcherPath));
        var stale = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, Payload(), false,
            out _, out string staleWhy);
        Check(stale == MusketeerNativeSave.WriterVerdict.Fail,
            "changed native content without a new capture is still refused at the real gate: " + staleWhy);
        Check(File.ReadAllBytes(DiskPath).SequenceEqual(diskBaseline),
            "gate refusal without invoking the writer keeps the old disk bytes");

        // 无 Reserved 幽灵：磁盘 archive 的任何 snapshot 都不再携带被转换 unit 的 career 行。
        var disk = MusketeerArchiveStore.Load(MusketeerPersistence.ArchivePath).Archive;
        bool ghost = false;
        foreach (var snapshots in disk.Scopes.Values)
            foreach (var snapshot in snapshots)
                foreach (var record in snapshot.Records)
                    if (record.Id == unitCareerId) ghost = true;
        Check(!ghost, "no reserved ghost row for the demoted unit career");

        Console.WriteLine("BOARDING-VOYAGE demoted-save pipeline complete: unit career " + unitCareerId.ToString("N"));
    }
}
