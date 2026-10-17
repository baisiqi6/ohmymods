using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

// Offline regression for the paid musketeer career sidecar: archive schema/store (incl. rack
// stock metadata), exact tool -> unit -> dropped tool transfers with per-call promotion scopes,
// load restoration by witnessed native id, fail-closed conflict/pending snapshots, rack physics
// restore, pool reuse life boundaries and the no-first-N rule.
internal static class Program
{
    static int passed;
    static string Root = Path.Combine(Path.GetTempPath(), "kem-musketeer-identity-" + Guid.NewGuid().ToString("N"));
    static string Context1 => MusketeerArchive.ContextKey("global-v35", 1, 0, 1);

    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; }

    static void Main()
    {
        Directory.CreateDirectory(Root);
        try
        {
            // Isolated red/green replay: one case at a time so the first failing assertion cannot
            // mask the following ones. Unset -> the full suite.
            string only = Environment.GetEnvironmentVariable("KEM_CASE");
            if (!string.IsNullOrEmpty(only))
            {
                switch (only)
                {
                    case "r2-explicit-raw0": R2ExplicitRawZeroTests(); break;
                    case "cold-preimage": ColdPreimageTests(); break;
                    case "r4-background": BackgroundSlotSaveTests(); break;
                    case "r4-explicit-sticky": ExplicitWitnessStickyTests(); break;
                    case "r4-source-shapes": ExplicitSourceShapeTests(); break;
                    case "r4-preimage": PreimageResolutionTests(); break;
                    case "r4-projection": PreimageProjectionTests(); break;
                    case "r4-pins": DeferredBoundaryPinTests(); break;
                    case "r4d-witness-world": WitnessWorldFreezeTests(); break;
                    case "r4d-virgin-exact": VirginExactOnlyTests(); break;
                    default: throw new Exception("unknown case " + only);
                }
                Console.WriteLine("CASE PASS " + only + " assertions=" + passed);
                return;
            }
            ArchiveTests();
            StoreTests();
            StoreReliabilityTests();
            ContextTests();
            SlotGenerationTests();
            DeferredSaveTests();
            R2ExplicitRawZeroTests();
            ColdPreimageTests();
            BackgroundSlotSaveTests();
            ExplicitWitnessStickyTests();
            ExplicitSourceShapeTests();
            PreimageResolutionTests();
            PreimageProjectionTests();
            DeferredBoundaryPinTests();
            WitnessWorldFreezeTests();
            VirginExactOnlyTests();
            ChallengeOwnerTests();
            WitnessBoundaryTests();
            SharedWiringTests();
            TransferTests();
            DropBoundaryTests();
            PromotionScopeTests();
            SaveLoadTests();
            StockTests();
            StockLifecycleTests();
            ReleaseRackClaimTests();
            StockRestoreAbandonTests();
            StockRestoreNotInWorldAbandonTests();
            PoolLifeTests();
            CapacityTests();
            Console.WriteLine("PASS " + passed + " assertions (real musketeer archive/identity/persistence)");
        }
        finally { try { Directory.Delete(Root, true); } catch { } }
    }

    // ---- helpers ----

    static string H(string value) => MusketeerArchive.Hash(value, "test");
    static string ArchivePath => MusketeerPersistence.ArchivePath;
    static MusketeerArchive Disk() => MusketeerArchiveStore.Load(ArchivePath).Archive;
    static byte[] DiskBytes() => File.ReadAllBytes(ArchivePath);
    static MusketeerArchiveStore.ReadResult DiskResult() => MusketeerArchiveStore.Load(ArchivePath);
    static string Raw(string label) => label.Length > 0 && label[0] == '{' ? label : "{\"label\":\"" + label + "\"}";
    static string IslandJson(params string[] ids) => "{\"rows\":\"" + string.Join(",", ids) + "\"}";
    static (string id, GameObject go) Row(string id, GameObject go) => (id, go);

    static (GameObject go, DroppableTool tool) Gun(string tag = "Bow")
    {
        var go = new GameObject(); go.tag = tag; var tool = go.Add(new DroppableTool()); return (go, tool);
    }

    static (GameObject go, Character character, Archer archer) Unit()
    {
        var go = new GameObject(); var character = go.Add(new Character()); var archer = go.Add(new Archer()); return (go, character, archer);
    }

    static Rigidbody2D Body(GameObject go, bool kinematic = false, float vx = 0f, float vy = 0f)
        => go.Add(new Rigidbody2D { isKinematic = kinematic, velocity = new Vector2 { x = vx, y = vy } });

    static Persistent Persist(GameObject go) => go.GetComponent<Persistent>() ?? go.Add(new Persistent());
    static bool TryRegister(DroppableTool tool, int stockSlot = -1) => MusketeerIdentity.TryRegisterPaidGun(tool, stockSlot);

    static void SetStatic(string name, object value)
        => typeof(MusketeerIdentity).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value);
    static object StaticOrNull(string name)
        => typeof(MusketeerIdentity).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
    static void SetStaticIfPresent(string name, object value)
        => typeof(MusketeerIdentity).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, value);

    static void ClearStatics()
    {
        MusketeerIdentity.Islands.Clear();
        Logger.Lines.Clear();
        MusketeerArchiveLog.ResetForTests();
        foreach (string name in new[] { "Roots", "Bound", "Logged" })
        {
            var field = typeof(MusketeerIdentity).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            field.GetValue(null).GetType().GetMethod("Clear").Invoke(field.GetValue(null), null);
        }
        SetStatic("_current", null); SetStatic("_contextKey", null); SetStaticIfPresent("_generation", null);
        SetStatic("_promotionDepth", 0); SetStatic("_bindCanary", false);
        SetStatic("_nextLife", 0L); SetStatic("_nextSweep", 0f);
        SetStatic("_lastTickFrame", -1); SetStatic("_contextFrame", -1); SetStatic("_contextWorld", 0L);
    }

    static int RootCount()
    {
        var field = typeof(MusketeerIdentity).GetField("Roots", BindingFlags.NonPublic | BindingFlags.Static);
        return ((ICollection)field.GetValue(null)).Count;
    }

    static void Reset(bool keepFile = false)
    {
        ClearStatics();
        BepInEx.Paths.ConfigPath = Path.Combine(Root, "config");
        if (!keepFile && Directory.Exists(BepInEx.Paths.ConfigPath)) Directory.Delete(BepInEx.Paths.ConfigPath, true);
        Managers.Inst = new Managers { world = new World { gameLayer = new GameObject().Add(new Transform()) } };
        GlobalSaveData.filename = "global-v35";
        var home = new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = Raw("current") };
        var campaign = BuildCampaign(1, home);
        GlobalSaveData.loaded = new GlobalSaveData { currentCampaign = 1, currentChallenge = 0, CurrentCampaign = campaign };
        GlobalSaveData.loaded.campaigns.Add(null);
        GlobalSaveData.loaded.campaigns.Add(campaign);
        CampaignSaveData.current = campaign;
        MusketeerAccess.TrackAllowed = true; MusketeerAccess.Enabled = true; MusketeerAccess.Playing = true; MusketeerAccess.InWorldResult = true;
        Time.frameCount++; Time.time += 10; Time.unscaledTime += 10;
        MusketeerIdentity.InvalidateContextCache();
        MusketeerIdentity.Tick();
        if (!keepFile) Load("before-purchase");
    }

    static IslandSaveData Island() => CampaignSaveData.current.CurrentIsland;

    // Canonical campaign catalog: 12 positional slots, populated members carry land == slot,
    // never-visited placeholders keep land 0. The current owner is registered explicitly; no
    // stub setter infers a slot from land.
    static CampaignSaveData BuildCampaign(int homeSlot, IslandSaveData home)
    {
        var campaign = new CampaignSaveData { challengeId = 0 };
        for (int i = 0; i < 12; i++)
            campaign._islands.Add(new IslandSaveData { land = 0, isNew = false, playTimeDays = 0, Json = Raw("placeholder") });
        if (home != null) campaign._islands[homeSlot] = home;
        campaign.CurrentLand = homeSlot;
        campaign.CurrentIsland = home;
        return campaign;
    }

    static void InstallIsland(CampaignSaveData campaign, int slot, IslandSaveData island)
    {
        while (campaign._islands.Count <= slot) campaign._islands.Add(new IslandSaveData { land = 0 });
        campaign._islands[slot] = island;
    }

    static void UseCampaign(CampaignSaveData campaign)
    {
        GlobalSaveData.loaded.CurrentCampaign = campaign;
        CampaignSaveData.current = campaign;
    }

    static void SetCurrentIsland(IslandSaveData island, int slot)
    {
        var campaign = CampaignSaveData.current;
        InstallIsland(campaign, slot, island);
        campaign.CurrentIsland = island;
        campaign.CurrentLand = slot;
    }

    static MusketeerPersistence.SaveCapture CreateSave()
    {
        var island = Island();
        return new MusketeerPersistence.SaveCapture { Campaign = GlobalSaveData.loaded.currentCampaign, Land = island.land, Challenge = GlobalSaveData.loaded.currentChallenge };
    }

    // go == null models a native row that never reached GetID (ordinary object, hidden object or
    // a missed capture): it is present in the island rows but carries no career.
    static MusketeerPersistence.SaveCapture SaveOn(IslandSaveData island, int campaign, int land, int challenge,
        string json, params (string id, GameObject go)[] rows)
    {
        island.Json = Raw(json); island.isNew = false; island.playTimeDays = 1;
        island.objects = rows.Select(x => new IslandSaveData.ObjectData { uniqueID = x.id }).ToList();
        IslandSaveData.CurrentlySavingIsland = island; IslandSaveData.isSavingGame = true;
        var capture = new MusketeerPersistence.SaveCapture { Campaign = campaign, Land = land, Challenge = challenge };
        capture.Begin();
        foreach (var row in rows) if (row.go != null) capture.Capture(Persist(row.go), row.id);
        capture.Apply();
        typeof(MusketeerPersistence.SavePatch).GetMethod("Finally", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { null, capture });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        return capture;
    }

    static void Save(string json, params (string id, GameObject go)[] rows)
    {
        var island = Island();
        SaveOn(island, GlobalSaveData.loaded.currentCampaign, island.land, GlobalSaveData.loaded.currentChallenge, json, rows);
    }

    // The island latches (an unrelated persistent reached GetID) but the live career's expected
    // row never did.
    static void SaveWithMissedRow(string json, GameObject foreign)
    {
        var island = Island();
        island.Json = Raw(json); island.isNew = false; island.playTimeDays = 1;
        island.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "foreign-row" } };
        IslandSaveData.CurrentlySavingIsland = island; IslandSaveData.isSavingGame = true;
        var capture = CreateSave();
        capture.Begin();   // the real Save prefix always freezes the source before any GetID
        capture.Capture(Persist(foreign), "foreign-row");
        capture.Apply();
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
    }

    static MusketeerPersistence.LoadCapture LoadBegin(string json, params string[] ids)
    {
        var island = Island();
        island.Json = Raw(json); island.isNew = false; island.playTimeDays = 1;
        island.objects = ids.Select(id => new IslandSaveData.ObjectData { uniqueID = id }).ToList();
        var capture = new MusketeerPersistence.LoadCapture();
        capture.Begin(island);
        return capture;
    }

    static void Capture(MusketeerPersistence.LoadCapture capture, int index, GameObject go)
        => capture.Capture(Island().objects[index], Persist(go));

    static void Load(string json, params (string id, GameObject go)[] rows)
        => LoadOn(Island(), json, rows);

    static MusketeerPersistence.LoadCapture LoadBeginOn(IslandSaveData island, string json, params string[] ids)
    {
        island.Json = Raw(json); island.isNew = false; island.playTimeDays = 1;
        island.objects = ids.Select(id => new IslandSaveData.ObjectData { uniqueID = id }).ToList();
        var capture = new MusketeerPersistence.LoadCapture();
        capture.Begin(island);
        return capture;
    }

    static void CaptureOn(IslandSaveData island, MusketeerPersistence.LoadCapture capture, int index, GameObject go)
        => capture.Capture(island.objects[index], Persist(go));

    static void LoadOn(IslandSaveData island, string json, params (string id, GameObject go)[] rows)
    {
        var capture = LoadBeginOn(island, json, rows.Select(x => x.id).ToArray());
        for (int i = 0; i < rows.Length; i++) if (rows[i].go != null) CaptureOn(island, capture, i, rows[i].go);
        capture.End(true);
    }

    // A real generation event for the current owner: the target is installed at its positional
    // slot (land stays whatever the fixture set), then Begin/Complete and a fresh Tick resolve it.
    static void Generate(int slot, string json)
    {
        var campaign = CampaignSaveData.current;
        var island = new IslandSaveData { land = slot, isNew = true, playTimeDays = 0, Json = Raw(json) };
        SetCurrentIsland(island, slot);
        var virgin = new MusketeerPersistence.VirginCapture();
        virgin.Begin(campaign);
        virgin.Complete(campaign);
        Time.frameCount++; MusketeerIdentity.Tick();
    }

    static List<MusketeerCareer> RowsOf(string contextKey)
        => Disk().Scopes[Disk().Contexts[contextKey].Active][0].Records;

    // ---- archive schema and store ----

    static void ArchiveTests()
    {
        var archive = new MusketeerArchive();
        var gun = new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun };
        var unit = new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindUnit, NativeId = "row-1" };
        Check(archive.Record(H("island1"), H("paid"), new[] { gun, unit }), "records accepted");
        Check(archive.TryGet(H("island1"), H("paid"), out var snapshot) && snapshot.Records.Count == 2, "exact restore");
        Check(!archive.TryGet(H("island2"), H("paid"), out _), "other island isolated");
        Check(!archive.TryGet(H("island1"), H("vanilla-resave"), out _), "unknown snapshot has no guessed restore");
        Check(archive.LatestReservations(H("island1")).Count == 2, "unknown source conservatively retains prepared careers");
        Check(archive.LatestReservations(H("island1")).All(x => x.NativeId == ""), "reservation never carries a stale native id");
        Check(archive.ConfirmBaseline(H("island1"), H("paid"), new[] { gun, unit }), "native load confirms the baseline");
        Check(archive.LatestReservations(H("island1")).Count == 2 && archive.LatestReservations(H("island1")).All(x => x.NativeId == ""), "confirmed baseline still reserves without an owner guess");
        Check(!archive.Record(H("island1"), H("paid"), new[] { gun }), "same snapshot with a conflicting history forbidden");
        Check(!archive.Record(H("island1"), H("dup-guid"), new[] { gun, new MusketeerCareer { Id = gun.Id, Kind = MusketeerCareer.KindGun } }), "duplicate GUID rejected");
        Check(!archive.Record(H("island1"), H("dup-native"), new[] { unit, new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindUnit, NativeId = "row-1" } }), "duplicate native id rejected");
        Check(!archive.Record(H("island1"), H("bad-kind"), new[] { new MusketeerCareer { Id = Guid.NewGuid(), Kind = 9 } }), "unknown career kind rejected");
        Check(!archive.Record(H("island1"), H("bad-native"), new[] { new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun, NativeId = new string('x', 300) } }), "over-long native id rejected");
        Check(!archive.Record("not-a-hash", H("x"), new[] { gun }), "invalid scope rejected");
        // rack stock metadata: guns only, bounded, part of snapshot identity, never reserved
        var stockGun = new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun, NativeId = "stock-row", StockSlot = 1 };
        Check(archive.Record(H("stock"), H("stock-snap"), new[] { stockGun }), "stock gun accepted");
        Check(archive.TryGet(H("stock"), H("stock-snap"), out var stockSnap) && stockSnap.Records[0].StockSlot == 1, "rack slot roundtrip");
        Check(!archive.Record(H("stock"), H("stock-snap"), new[] { new MusketeerCareer { Id = stockGun.Id, Kind = MusketeerCareer.KindGun, NativeId = "stock-row", StockSlot = 2 } }), "same snapshot with another rack slot refused");
        Check(!archive.Record(H("stock"), H("stock-slot-range"), new[] { new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun, StockSlot = MusketeerArchive.MaxStockSlots } }), "rack slot out of range rejected");
        Check(!archive.Record(H("stock"), H("stock-slot-dup"), new[] { stockGun, new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun, StockSlot = 1 } }), "two careers on one rack slot rejected");
        Check(!archive.Record(H("stock"), H("stock-unit"), new[] { new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindUnit, StockSlot = 0 } }), "unit with a rack slot rejected");
        Check(archive.LatestReservations(H("stock")).All(x => x.StockSlot == MusketeerCareer.NoStockSlot), "reservation never carries a rack claim");
        // zero authoritative: a newer confirmed empty snapshot ends the paid reservation walk
        Check(archive.ConfirmBaseline(H("island1"), H("released"), Array.Empty<MusketeerCareer>()), "authoritative empty baseline recorded");
        Check(archive.LatestReservations(H("island1")).Count == 0, "zero authoritative state releases older paid reservations");
        for (int i = 0; i < 12; i++) Check(archive.Record(H("island1"), H("history" + i), new[] { gun }), "history append");
        Check(archive.Scopes[H("island1")].Count == MusketeerArchive.MaxSnapshots, "bounded snapshot history");
        Check(archive.TryGet(H("island1"), H("released"), out _), "pinned baseline never evicted");
        // context -> epoch bookkeeping
        string context = MusketeerArchive.ContextKey("global-v35", 1, 0, 1);
        string scope = H("island1");
        Check(archive.EnsureContext(context, scope, true), "context registered to its first epoch");
        string other = H("islandX");
        Check(archive.Record(other, H("paid"), new[] { gun }), "second scope recorded");
        Check(archive.EnsureContext(context, other, true) && archive.Contexts[context].Epochs.Count == 2, "confirmed epoch appended");
        Check(archive.Contexts[context].Active == other, "newest epoch is active");
        Check(archive.EnsureContext(context, scope, false) && archive.Contexts[context].Active == scope, "older epoch rolls the active scope back");
        Check(archive.Contexts[context].Epochs.SequenceEqual(new[] { scope, other }), "newer epoch kept for rollback");
        Check(!archive.EnsureContext(context, H("islandZ"), true), "epoch without a recorded scope refused");
        Check(!archive.EnsureContext(MusketeerArchive.ContextKey("global-v35", 1, 0, 2), scope, true), "epoch owned by another context is never migrated");
        var decoded = MusketeerArchive.Decode(archive.Encode(), out bool future);
        Check(!future && decoded.Encode().SequenceEqual(archive.Encode()), "deterministic encoding roundtrip");
        Check(decoded.Contexts[context].Active == scope && decoded.Contexts[context].Epochs.Count == 2, "contexts roundtrip");
        Check(decoded.TryGet(H("stock"), H("stock-snap"), out var stockBack) && stockBack.Records[0].StockSlot == 1, "rack slot survives encoding");
        string text = System.Text.Encoding.UTF8.GetString(archive.Encode());
        MusketeerArchive.Decode(System.Text.Encoding.UTF8.GetBytes(text.Replace("\"schemaVersion\":2", "\"schemaVersion\":4")), out bool futureVersion);
        Check(futureVersion, "future version detected");
        foreach (string invalid in new[] { text.Replace("\"schemaVersion\":2", "\"schemaVersion\":2,\"extra\":true"), "{}" })
        {
            bool rejected = false;
            try { MusketeerArchive.Decode(System.Text.Encoding.UTF8.GetBytes(invalid), out _); } catch { rejected = true; }
            Check(rejected, "malformed schema rejected");
        }
        {
            bool rejected = false;
            try { MusketeerArchive.Decode(System.Text.Encoding.UTF8.GetBytes(text.Replace("\"hashKind\":2", "\"hashKind\":3")), out _); } catch { rejected = true; }
            Check(rejected, "unknown hash kind refused");
        }
        {
            bool rejected = false;
            try { MusketeerArchive.Decode(System.Text.Encoding.UTF8.GetBytes(text.Replace(",\"stockSlot\":-1", "")), out _); } catch { rejected = true; }
            Check(rejected, "missing rack slot field refused");
        }
    }

    static void StoreTests()
    {
        string path = Path.Combine(Root, "store.json");
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        var archive = new MusketeerArchive();
        Check(archive.Record(H("scope"), H("snapshot"), new[] { new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun } }), "record for store");
        var missing = MusketeerArchiveStore.Load(path);
        Check(missing.Writable && missing.Status == MusketeerArchiveStore.State.Missing, "missing archive initializable");
        Check(MusketeerArchiveStore.Save(path, missing, archive), "initial atomic save");
        var read = MusketeerArchiveStore.Load(path);
        Check(read.Writable && read.Status == MusketeerArchiveStore.State.Valid, "written archive readable");
        Check(!MusketeerArchiveStore.Save(path, missing, archive), "stale writer refused (compare and swap)");
        Check(MusketeerArchiveStore.Save(path, read, archive), "replacement save keeps a durable backup");
        byte[] backup = File.ReadAllBytes(path + ".bak");
        File.WriteAllText(path, "broken");
        var recovered = MusketeerArchiveStore.Load(path);
        Check(recovered.RecoveredBackup && !recovered.Writable, "corrupt main with a valid backup is read-only");
        Check(!MusketeerArchiveStore.Save(path, recovered, archive) && File.ReadAllBytes(path + ".bak").SequenceEqual(backup), "corrupt primary never destroys the valid backup");
        File.WriteAllText(path, "{\"schemaVersion\":99,\"scopes\":[],\"contexts\":[]}");
        Check(MusketeerArchiveStore.Load(path).Status == MusketeerArchiveStore.State.Unsupported, "future main not downgraded to the backup");
    }

    // ---- 读失败回退 / 写失败重试（A 加固，2026-09-20；不改变任何身份语义） ----

    static void StoreReliabilityTests()
    {
        Logger.Lines.Clear();
        MusketeerArchiveLog.ResetForTests();

        string path = Path.Combine(Root, "reliability.json");
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".bak")) File.Delete(path + ".bak");

        var archive = new MusketeerArchive();
        Check(archive.Record(H("scope"), H("snapshot"), new[] { new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun } }), "reliability snapshot");
        Check(MusketeerArchiveStore.Save(path, MusketeerArchiveStore.Load(path), archive), "reliability first save");
        Check(archive.Record(H("scope"), H("snapshot-2"), new[] { new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun } }), "reliability second snapshot");
        Check(MusketeerArchiveStore.Save(path, MusketeerArchiveStore.Load(path), archive), "reliability replacement save keeps a backup");

        // 读主文件 IO 失败 → 依然查备份；降级只读并落一次性 Warning（此前该路径全程静默）
        byte[] main = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var io = MusketeerArchiveStore.Load(path);
            Check(io.Status == MusketeerArchiveStore.State.Valid && io.RecoveredBackup && !io.Writable,
                "an unreadable main falls back to the read-only backup");
        }
        Check(main.SequenceEqual(File.ReadAllBytes(path)), "the fallback never rewrites the main file");
        Check(Warned("sidecar load RecoveredBackup"), "the backup fallback is logged once | " + Dump());

        // 备份也不可用：保持 IoError 降级，并记录状态与异常摘要
        File.Delete(path + ".bak");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var ioOnly = MusketeerArchiveStore.Load(path);
            Check(ioOnly.Status == MusketeerArchiveStore.State.IoError && !ioOnly.Writable, "an unreadable main without a backup stays IoError");
        }
        Check(Warned("sidecar load IoError"), "the plain IO failure is logged with its status | " + Dump());

        // 重建备份供写路径使用
        var staged = MusketeerArchiveStore.Load(path).Archive;
        Check(staged.Record(H("scope"), H("snapshot-3"), new[] { new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun } }), "third snapshot staged");
        Check(MusketeerArchiveStore.Save(path, MusketeerArchiveStore.Load(path), staged), "backup recreated");

        // 写失败（IO 类）在锁持续时重试一次仍失败 → false + 一次性 retry=1 警告；释放锁后同一保存立即成功
        var expected = MusketeerArchiveStore.Load(path);
        Check(expected.Writable, "writable expected state");
        var fourth = expected.Archive;
        Check(fourth.Record(H("scope"), H("snapshot-4"), new[] { new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun } }), "fourth snapshot staged");
        byte[] beforeFailure = File.ReadAllBytes(path);
        // 跨平台 IO 故障注入：Unix 的 rename 不受 FileShare 建议锁约束（macOS 实测 .bak 被
        // FileShare.None 打开时 File.Replace 仍成功），故用目录占住备份目标让 Replace 真实失败；
        // Windows 的 ReplaceFile 在该形态下同样失败。断言语义与集合保持不变。
        File.Delete(path + ".bak");
        Directory.CreateDirectory(path + ".bak");
        try
        {
            Check(!MusketeerArchiveStore.Save(path, expected, fourth), "save fails while the backup target stays obstructed");
        }
        finally
        {
            Directory.Delete(path + ".bak");
        }
        Check(Warned("write-retry=1 failed"), "the failed retry is logged once | " + Dump());
        Check(beforeFailure.SequenceEqual(File.ReadAllBytes(path)), "a failed retry keeps the previous file");
        Check(!Directory.GetFiles(Path.GetDirectoryName(path)).Any(f => f.Contains(".tmp-")), "no temp file is left behind");
        Check(MusketeerArchiveStore.Save(path, expected, fourth), "the same save succeeds once the obstruction is gone");

        // 瞬态失败：重试窗口内恢复 → 单次重试内恢复（同样使用跨平台目录占用注入）
        var expected2 = MusketeerArchiveStore.Load(path);
        Check(expected2.Writable, "writable expected state (second)");
        var fifth = expected2.Archive;
        Check(fifth.Record(H("scope"), H("snapshot-5"), new[] { new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun } }), "fifth snapshot staged");
        File.Delete(path + ".bak");
        Directory.CreateDirectory(path + ".bak");
        try
        {
            var releaser = new System.Threading.Thread(() => { System.Threading.Thread.Sleep(80); Directory.Delete(path + ".bak"); }) { IsBackground = true };
            releaser.Start();
            Check(MusketeerArchiveStore.Save(path, expected2, fifth), "one retry heals a transient obstruction");
            releaser.Join();
        }
        finally
        {
            if (Directory.Exists(path + ".bak")) Directory.Delete(path + ".bak");
        }
        Check(Logged("write-retry=1 recovered"), "the healed retry is logged | " + Dump());
        Check(MusketeerArchiveStore.Load(path).Archive.TryGet(H("scope"), H("snapshot-5"), out _), "the fifth snapshot persisted");

        // 保护性拒绝（compare-and-swap 预检）绝不重试
        Logger.Lines.Clear();
        var stale = MusketeerArchiveStore.Load(path);
        var sixth = stale.Archive;
        Check(sixth.Record(H("scope"), H("snapshot-6"), new[] { new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun } }), "sixth snapshot staged");
        Check(MusketeerArchiveStore.Save(path, MusketeerArchiveStore.Load(path), sixth), "another writer advances the disk state");
        Check(!MusketeerArchiveStore.Save(path, stale, sixth), "a stale writer is refused (compare and swap)");
        Check(!Logged("retry"), "refusals are never retried | " + Dump());
    }

    static bool Logged(string fragment) => Logger.Lines.Exists(line => line.Contains(fragment));
    static bool Warned(string fragment) => Logger.Lines.Exists(line => line.StartsWith("W:", StringComparison.Ordinal) && line.Contains(fragment));
    static string Dump() => string.Join(" | ", Logger.Lines);

    // ---- island contexts, generation, authority ----

    static void ContextTests()
    {
        Reset();
        Check(MusketeerIdentity.CanPurchase, "confirmed baseline enables the shop");
        MusketeerNativeSave.RejectEndpointUnavailable = true;
        Check(!MusketeerIdentity.CanPurchase, "unavailable save rejection endpoint disables purchases before charging");
        Check(!TryRegister(Gun().tool), "unavailable save rejection endpoint refuses paid career registration");
        Check(MusketeerIdentity.StatusText.Contains("同步保存接口不可用"), "unavailable save endpoint is visible to the player");
        MusketeerNativeSave.RejectEndpointUnavailable = false;
        // Rebinding the native owner may clear its memory barrier before the selected prepared
        // island is replaced. Its pending grant must still block all charging/replenishment.
        var compensation = MusketeerIdentity.Current;
        compensation.CompensationPending = true;
        int beforeCompensationGate = compensation.Careers.Count;
        Check(!MusketeerIdentity.CanPurchase, "pending prepared island blocks purchases with no native owner barrier");
        Check(!TryRegister(Gun().tool), "pending prepared island refuses paid gun registration");
        Check(!MusketeerIdentity.TryGetRestockCounts(out _, out _), "pending prepared island blocks replenishment counts");
        Check(compensation.Careers.Count == beforeCompensationGate, "blocked purchase creates no extra career");
        compensation.CompensationPending = false;
        Check(MusketeerIdentity.CanPurchase, "cleared compensation restores the proven purchase gate");
        var first = Gun();
        Check(TryRegister(first.tool), "register gun on the first island");
        Save(IslandJson("row-gun-1"), Row("row-gun-1", first.go));
        SetCurrentIsland(new IslandSaveData { land = 2, isNew = false, playTimeDays = 1, Json = Raw("land2") }, 2);
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase, "new island without a proven baseline gates purchases");
        Generate(2, "land2-generation");
        Check(MusketeerIdentity.CanPurchase, "successful new-island apply enables the shop");
        var second = Gun();
        Check(TryRegister(second.tool), "purchase on the new island");
        var repeat = new MusketeerPersistence.VirginCapture();
        repeat.Begin(CampaignSaveData.current); repeat.Complete(CampaignSaveData.current);
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.IsGun(second.tool), "repeated generation callback cannot erase a paid gun");
        Save(IslandJson("row-gun-2"), Row("row-gun-2", second.go));
        var disk = Disk();
        string context1 = MusketeerArchive.ContextKey("global-v35", 1, 0, 1);
        string context2 = MusketeerArchive.ContextKey("global-v35", 1, 0, 2);
        Check(disk.Contexts.ContainsKey(context1) && disk.Contexts.ContainsKey(context2), "both island contexts recorded");
        Check(RowsOf(context2).Any(r => r.NativeId == "row-gun-2"), "second island snapshot scoped to its own epoch");
        Check(RowsOf(context1).All(r => r.NativeId != "row-gun-2"), "careers never leak between islands");
        var third = Gun();
        MusketeerAccess.TrackAllowed = false;
        Check(!TryRegister(third.tool), "online / non-authoritative registration refused");
        Check(!MusketeerIdentity.CanPurchase, "online / non-authoritative purchase gate");
        Check(!MusketeerIdentity.IsGun(second.tool), "marked gun is not exposed online");
        Check(!MusketeerIdentity.IsGun(third.tool), "untracked gun stays unmarked offline");
        var offlineUnits = new List<Archer>(); MusketeerIdentity.CopyUnits(offlineUnits);
        Check(offlineUnits.Count == 0, "copy never exposes bindings while authority is lost");
        MusketeerAccess.TrackAllowed = true;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.IsGun(second.tool), "authority restored keeps existing identity");
        // a foreign layer (object outside the current gameLayer) is never exposed
        MusketeerAccess.InWorldResult = false;
        Check(!MusketeerIdentity.IsGun(second.tool), "foreign layer binding is not exposed");
        MusketeerAccess.InWorldResult = true;
        Check(MusketeerIdentity.IsGun(second.tool), "same-world binding is exposed again");
        // the same land in another campaign slot is an independent context
        GlobalSaveData.loaded.currentCampaign = 2;
        var campaign2 = BuildCampaign(1, new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = Raw("campaign2") });
        while (GlobalSaveData.loaded.campaigns.Count <= 2) GlobalSaveData.loaded.campaigns.Add(null);
        GlobalSaveData.loaded.campaigns[2] = campaign2;
        UseCampaign(campaign2);
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase, "same land in another campaign is a different context");
        Generate(1, "campaign2-generation");
        Check(MusketeerIdentity.CanPurchase, "second campaign island initialized independently");
        var campaignGun = Gun();
        Check(TryRegister(campaignGun.tool), "purchase in the second campaign");
        Save(IslandJson("campaign2-row"), Row("campaign2-row", campaignGun.go));
        string contextCampaign2 = MusketeerArchive.ContextKey("global-v35", 2, 0, 1);
        Check(RowsOf(contextCampaign2).Any(r => r.NativeId == "campaign2-row"), "second campaign purchase scoped to its own context");
        Check(RowsOf(context1).All(r => r.NativeId != "campaign2-row"), "campaign slots never share careers");
        // a runtime world change (new gameLayer) invalidates bindings stamped in the old world
        Managers.Inst.world.gameLayer = new GameObject().Add(new Transform());
        Time.frameCount++; Time.unscaledTime += 10;
        MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.IsGun(campaignGun.tool), "world change releases the old world's binding");
        Check(MusketeerIdentity.DescribeForTests().Contains("reserved=1"), "the career survives the world change as a reservation");
    }

    // ---- issue #251: the island's positional table slot, not a placeholder land field ----

    static string Ctx(int campaign, int challenge, int slot)
        => MusketeerArchive.ContextKey("global-v35", campaign, challenge, slot);

    static string RowId(string id) => "{\"uniqueID\":\"" + id + "\"}";

    static string IslandJsonAt(int land, params string[] rows)
        => "{\"land\":" + land + ",\"objects\":[" + string.Join(",", rows) + "],\"playTimeDays\":1}";

    static IslandSaveData GeneratePlaceholder(int slot, string label)
    {
        var target = new IslandSaveData { land = 0, isNew = true, playTimeDays = 0, Json = Raw(label) };
        SetCurrentIsland(target, slot);
        var campaign = CampaignSaveData.current;
        var virgin = new MusketeerPersistence.VirginCapture();
        virgin.Begin(campaign);
        virgin.Complete(campaign);
        Time.frameCount++; MusketeerIdentity.Tick();
        return target;
    }

    static object InvokePatch(Type type, string name, object[] args)
    {
        var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        if (method == null) throw new Exception("missing patch method " + type.Name + "." + name);
        return method.Invoke(null, args);
    }

    // Reflection keeps the regression compilable against the pre-fix product for the red replay;
    // the behavior assertion (not a missing symbol) is the evidence.
    static string SlotLookupResult(CampaignSaveData campaign, IslandSaveData island, out int slot)
    {
        slot = -1;
        var method = typeof(MusketeerIdentity).GetMethod("IslandSlot",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (method == null) return "missing";
        object[] args = { campaign, island, 0 };
        object result = method.Invoke(null, args);
        slot = (int)args[2];
        return result?.ToString() ?? "null";
    }

    static bool CatalogOwnerIs(CampaignSaveData expected, int campaign, int challenge)
    {
        var method = typeof(MusketeerIdentity).GetMethod("CatalogOwner",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (method == null) return false;
        return ReferenceEquals(method.Invoke(null, new object[] { GlobalSaveData.loaded, campaign, challenge }), expected);
    }

    static (MusketeerPersistence.SaveCapture capture, IslandSaveData target, string context,
        string epoch, byte[] disk, GameObject gun) BeginCapturedDeferred(string label)
    {
        Reset();
        var target = GeneratePlaceholder(11, label);
        target.isNew = false; target.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, label + " starts authorized");
        var gun = Gun(); Check(TryRegister(gun.tool), label + " gun registered");
        target.Json = Raw(IslandJsonAt(0, RowId(label + "-gun")));
        target.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = label + "-gun" } };
        IslandSaveData.CurrentlySavingIsland = target; IslandSaveData.isSavingGame = true;
        var capture = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = -1, Challenge = 0 };
        capture.Begin();
        capture.Capture(Persist(gun.go), label + "-gun");
        Check(capture.ContextKey != null && capture.Island != null, label + " first capture succeeds");
        string context = capture.ContextKey;
        string epoch = MusketeerIdentity.Islands[context].Epoch;
        byte[] disk = DiskBytes();
        return (capture, target, context, epoch, disk, gun.go);
    }

    static void CheckDeferredClosed(string label, string context, string epoch, byte[] disk,
        MusketeerPersistence.SaveCapture capture)
    {
        Check(!capture.Applied && capture.Conflict, label + " closes without staging");
        Check(MusketeerIdentity.Islands[context].Epoch == epoch, label + " preserves the epoch/proof");
        Check(DiskBytes().SequenceEqual(disk), label + " writes no archive bytes");
    }

    static IslandSaveData AuthorizedPlaceholder(string label, out string context, out string epoch,
        out Guid[] paid, out byte[] disk)
    {
        Reset();
        var target = GeneratePlaceholder(11, label);
        target.isNew = false; target.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, label + " starts authorized");
        var gun = Gun(); Check(TryRegister(gun.tool), label + " paid record registered");
        context = Ctx(1, 0, 11);
        epoch = MusketeerIdentity.Islands[context].Epoch;
        paid = MusketeerIdentity.Islands[context].Careers.Select(x => x.Id).OrderBy(x => x).ToArray();
        disk = DiskBytes();
        return target;
    }

    static void CheckWitnessStateUnchanged(string label, string context, string epoch, Guid[] paid, byte[] disk)
    {
        Check(MusketeerIdentity.Islands[context].Epoch == epoch
            && MusketeerIdentity.Islands[context].Careers.Select(x => x.Id).OrderBy(x => x).SequenceEqual(paid),
            label + " leaves the old epoch and paid records unchanged");
        Check(DiskBytes().SequenceEqual(disk), label + " writes no archive bytes");
    }

    static void Promote(MusketeerIdentity.Career career, (GameObject go, Character character, Archer archer) unit)
    {
        MusketeerIdentity.OnGunPickupBegin(career.Tool, out var pickup);
        MusketeerIdentity.OnGunPickupEnd(unit.character, pickup);
        MusketeerIdentity.OnGunPickupAbort(pickup);
    }

    static void SlotGenerationTests()
    {
        Reset();
        var home = MusketeerIdentity.Current;
        var homeGun = Gun(); Check(TryRegister(homeGun.tool), "home gun registered");
        var homeGun2 = Gun(); Check(TryRegister(homeGun2.tool), "home unit gun registered");
        var homeUnit = Unit();
        Promote(home.Careers[1], homeUnit);
        Save(IslandJson("home-gun", "home-unit"), Row("home-gun", homeGun.go), Row("home-unit", homeUnit.go));
        Check(home.Careers.Count == 2, "home paid owners recorded");
        Check(home.ContextKey == Context1 && Disk().Contexts.ContainsKey(Context1),
            "land==slot islands keep their existing context key bytes");
        Guid[] homeIds = home.Careers.Select(x => x.Id).OrderBy(x => x).ToArray();
        string homeEpoch = home.Epoch;
        string[] homeRows = RowsOf(Context1).Select(x => x.NativeId).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        var target = GeneratePlaceholder(11, "slot11-generation");
        string context11 = Ctx(1, 0, 11);
        Check(MusketeerIdentity.Current != null && MusketeerIdentity.Current.ContextKey == context11,
            "virgin land0 generation identities the positional slot 11");
        Check(MusketeerIdentity.Current != home, "slot11 owns an independent state");
        Check(target.land == 0, "generation never rewrites the native land field");
        Check(Logger.Lines.Count(line => line.Contains("[Musketeer] virgin-epoch:")
            && line.Contains(":slot=11") && line.Contains(":dataLand=0")
            && line.Contains(":campaign=1") && line.Contains(":challenge=0")) == 1,
            "generation log records slot/dataLand/campaign/challenge exactly once");
        Check(MusketeerIdentity.Current.Careers.Count == 0, "fresh slot11 inherits no home career");
        Check(home.Careers.Select(x => x.Id).OrderBy(x => x).SequenceEqual(homeIds), "home active identities untouched");
        Check(home.Epoch == homeEpoch, "home epoch untouched");
        Check(RowsOf(Context1).Select(x => x.NativeId).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(homeRows),
            "home accepted proof rows untouched");
        Check(RowsOf(context11).Count == 0, "generation baseline is empty, not a copy of home");

        // Later live frames, after the island stopped being "new", still resolve the same slot
        // because the completed generation is the only proof that this placeholder is slot 11.
        target.isNew = false; target.playTimeDays = 3;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "generation receipt authorizes a later live placeholder");
        var gun11 = Gun(); Check(TryRegister(gun11.tool), "purchase gun on generating island");
        var gun11b = Gun(); Check(TryRegister(gun11b.tool), "purchase unit gun on generating island");
        var unit11 = Unit();
        Promote(MusketeerIdentity.Current.Careers.Single(x => x.Tool == gun11b.tool), unit11);
        Guid[] slot11Ids = MusketeerIdentity.Current.Careers.Select(x => x.Id).OrderBy(x => x).ToArray();
        Check(slot11Ids.Length == 2 && !slot11Ids.Intersect(homeIds).Any(), "slot11 paid identities are independent");
        Check(home.Careers.Count == 2 && target.land == 0, "home identity and raw land stay untouched");

        // First save: GetID arrives while the native field still says 0, then the native side
        // finalizes the target as land 11 before the capture applies.
        string epoch11 = MusketeerIdentity.Current.Epoch;
        target.isNew = false; target.playTimeDays = 4;
        target.Json = Raw(IslandJsonAt(0, RowId("slot11-gun"), RowId("slot11-unit")));
        target.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "slot11-gun" }, new() { uniqueID = "slot11-unit" } };
        IslandSaveData.CurrentlySavingIsland = target; IslandSaveData.isSavingGame = true;
        var deferred = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = -1, Challenge = 0 };
        deferred.Begin();
        deferred.Capture(Persist(gun11.go), "slot11-gun");
        deferred.Capture(Persist(unit11.go), "slot11-unit");
        Check(deferred.ContextKey == context11, "-1 GetID resolves slot 11 from the owner table, not raw land 0");
        target.land = 11;
        target.Json = Raw(IslandJsonAt(11, RowId("slot11-gun"), RowId("slot11-unit")));
        deferred.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, deferred });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        Check(deferred.Applied, "first saved island stages under the generation epoch");
        Check(MusketeerIdentity.Current.Epoch == epoch11, "first save updates proof without minting a new epoch");
        Check(RowsOf(context11).Any(x => x.NativeId == "slot11-gun") && RowsOf(context11).Any(x => x.NativeId == "slot11-unit"),
            "first save writes the exact slot11 rows");
        Check(home.Epoch == homeEpoch, "home epoch remains unchanged after the first slot11 save");

        // The modeled stale field can return to 0 after a successful save; the same receipt keeps
        // authorizing this exact island without any periodic rewrite.
        target.land = 0; target.playTimeDays = 5;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "post-save raw land 0 still resolves through the same receipt");
        var afterSave = Gun(); Check(TryRegister(afterSave.tool), "purchase under the post-save receipt");
        Check(MusketeerIdentity.Current.Epoch == epoch11, "post-save purchase does not change the epoch");
    }

    static void DeferredSaveTests()
    {
        Reset();
        var target = GeneratePlaceholder(11, "deferred");
        target.isNew = false; target.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "deferred fixture starts inside the slot11 receipt");
        var gun = Gun(); Check(TryRegister(gun.tool), "deferred gun registered");
        string context11 = Ctx(1, 0, 11);
        string epoch11 = MusketeerIdentity.Current.Epoch;

        target.Json = Raw(IslandJsonAt(0, RowId("deferred-gun")));
        target.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "deferred-gun" } };
        IslandSaveData.CurrentlySavingIsland = target; IslandSaveData.isSavingGame = true;
        var capture = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = -1, Challenge = 0 };
        capture.Begin();
        capture.Capture(Persist(gun.go), "deferred-gun");
        Check(capture.ContextKey == context11 && capture.Island != null,
            "-1 capture uses the frozen owner slot while the data land is still 0");
        target.land = 11;
        target.Json = Raw(IslandJsonAt(11, RowId("deferred-gun")));
        capture.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, capture });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        Check(capture.Applied && MusketeerIdentity.Current.Epoch == epoch11, "-1 apply updates the same epoch after native land 11");

        // Explicit old-island saves never inherit the current scene island.
        var oldIsland = new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = Raw(IslandJsonAt(1)) };
        InstallIsland(CampaignSaveData.current, 1, oldIsland);
        var foreign = new GameObject();
        var explicitSave = SaveOn(oldIsland, 1, 1, 0, IslandJsonAt(1), ("foreign-row", foreign));
        Check(explicitSave.ContextKey == Context1, "explicit old-island save keeps its own context");
        Check(MusketeerIdentity.Current.ContextKey == context11, "explicit old-island save does not move the live context");

        // Owner and file drift close a deferred capture before it can build a key.
        var ownerDrift = new IslandSaveData { land = 0, isNew = false, playTimeDays = 2, Json = Raw(IslandJsonAt(0)) };
        SetCurrentIsland(ownerDrift, 11);
        var driftCampaign = BuildCampaign(1, new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = Raw("drift") });
        GlobalSaveData.loaded.campaigns[1] = driftCampaign;
        IslandSaveData.CurrentlySavingIsland = ownerDrift; IslandSaveData.isSavingGame = true;
        var ownerCapture = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = -1, Challenge = 0 };
        ownerCapture.Begin();
        ownerCapture.Capture(Persist(gun.go), "drift-gun");
        Check(ownerCapture.ContextKey == null && ownerCapture.Island == null, "catalog owner drift closes the -1 capture");
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;

        GlobalSaveData.filename = "global-v36";
        var fileCapture = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = -1, Challenge = 0 };
        fileCapture.Begin();
        IslandSaveData.CurrentlySavingIsland = target; IslandSaveData.isSavingGame = true;
        fileCapture.Capture(Persist(gun.go), "file-gun");
        Check(fileCapture.ContextKey == null && fileCapture.Island == null, "file drift closes the -1 capture");
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        GlobalSaveData.filename = "global-v35";

        // After-Begin owner drift: Begin must freeze the original owner; replacing the catalog
        // entry afterwards closes the capture even though GetID still arrives.
        Reset();
        var afterOwnerTarget = GeneratePlaceholder(11, "after-owner");
        afterOwnerTarget.isNew = false; afterOwnerTarget.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "after-Begin owner fixture starts authorized");
        var afterOwnerGun = Gun(); Check(TryRegister(afterOwnerGun.tool), "after-Begin owner gun registered");
        afterOwnerTarget.Json = Raw(IslandJsonAt(0, RowId("after-owner-gun")));
        afterOwnerTarget.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "after-owner-gun" } };
        IslandSaveData.CurrentlySavingIsland = afterOwnerTarget; IslandSaveData.isSavingGame = true;
        var afterOwner = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = -1, Challenge = 0 };
        afterOwner.Begin();
        string afterOwnerEpoch = MusketeerIdentity.Current.Epoch;
        byte[] afterOwnerBefore = DiskBytes();
        GlobalSaveData.loaded.campaigns[1] = BuildCampaign(1, new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = Raw("after-owner-replacement") });
        afterOwner.Capture(Persist(afterOwnerGun.go), "after-owner-gun");
        Check(afterOwner.ContextKey == null && afterOwner.Island == null,
            "owner drift after Begin closes the deferred capture");
        afterOwner.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, afterOwner });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        Check(DiskBytes().SequenceEqual(afterOwnerBefore), "after-Begin owner drift writes no archive bytes");
        Check(MusketeerIdentity.Islands[Ctx(1, 0, 11)].Epoch == afterOwnerEpoch,
            "after-Begin owner drift preserves the slot11 epoch/proof");

        // After-Begin file drift: the frozen file must be the one still loaded at GetID.
        Reset();
        var afterFileTarget = GeneratePlaceholder(11, "after-file");
        afterFileTarget.isNew = false; afterFileTarget.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "after-Begin file fixture starts authorized");
        var afterFileGun = Gun(); Check(TryRegister(afterFileGun.tool), "after-Begin file gun registered");
        afterFileTarget.Json = Raw(IslandJsonAt(0, RowId("after-file-gun")));
        afterFileTarget.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "after-file-gun" } };
        IslandSaveData.CurrentlySavingIsland = afterFileTarget; IslandSaveData.isSavingGame = true;
        var afterFile = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = -1, Challenge = 0 };
        afterFile.Begin();
        string afterFileEpoch = MusketeerIdentity.Current.Epoch;
        byte[] afterFileBefore = DiskBytes();
        GlobalSaveData.filename = "global-v36";
        afterFile.Capture(Persist(afterFileGun.go), "after-file-gun");
        Check(afterFile.ContextKey == null && afterFile.Island == null,
            "file drift after Begin closes the deferred capture");
        afterFile.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, afterFile });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        Check(DiskBytes().SequenceEqual(afterFileBefore), "after-Begin file drift writes no archive bytes");
        GlobalSaveData.filename = "global-v35";
        Check(MusketeerIdentity.Islands[Ctx(1, 0, 11)].Epoch == afterFileEpoch,
            "after-Begin file drift preserves the slot11 epoch/proof");

        // -2 is not the deferred sentinel: the original negative-land rejection stays.
        Reset();
        var negativeTarget = GeneratePlaceholder(11, "negative-two");
        negativeTarget.isNew = false; negativeTarget.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "negative-land fixture starts authorized");
        var negativeGun = Gun(); Check(TryRegister(negativeGun.tool), "negative-land gun registered");
        negativeTarget.Json = Raw(IslandJsonAt(0, RowId("negative-gun")));
        negativeTarget.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "negative-gun" } };
        byte[] negativeBefore = DiskBytes();
        string negativeEpoch = MusketeerIdentity.Current.Epoch;
        IslandSaveData.CurrentlySavingIsland = negativeTarget; IslandSaveData.isSavingGame = true;
        var negative = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = -2, Challenge = 0 };
        negative.Begin();
        negative.Capture(Persist(negativeGun.go), "negative-gun");
        Check(negative.ContextKey == null && negative.Island == null, "-2 has no key and no island");
        negative.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, negative });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        Check(!negative.Applied && DiskBytes().SequenceEqual(negativeBefore), "-2 stages and writes nothing");
        Check(MusketeerIdentity.Islands[Ctx(1, 0, 11)].Epoch == negativeEpoch,
            "-2 preserves the slot11 epoch/proof");

        // Apply must not require CurrentlySavingIsland/isSavingGame to still be set: native may
        // have cleared them before the postfix. Only the frozen source and captured island count.
        var applyStatics = BeginCapturedDeferred("apply-statics");
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        applyStatics.target.land = 11;
        applyStatics.target.Json = Raw(IslandJsonAt(11, RowId("apply-statics-gun")));
        applyStatics.capture.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, applyStatics.capture });
        Check(applyStatics.capture.Applied, "Apply stages with cleared native save statics");
        Check(MusketeerIdentity.Islands[applyStatics.context].Epoch == applyStatics.epoch
            && !DiskBytes().SequenceEqual(applyStatics.disk),
            "Apply keeps the epoch while staging from the frozen source alone");

        // The frozen -1 source is re-proven at Apply too. A file change after the first capture
        // must close the capture without looking at the (possibly cleared) native statics.
        var applyFile = BeginCapturedDeferred("apply-file");
        GlobalSaveData.filename = "global-v36";
        applyFile.capture.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, applyFile.capture });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        GlobalSaveData.filename = "global-v35";
        CheckDeferredClosed("after-capture file drift", applyFile.context, applyFile.epoch, applyFile.disk, applyFile.capture);

        // Owner replacement after the first capture is re-checked at Apply.
        var applyOwner = BeginCapturedDeferred("apply-owner");
        GlobalSaveData.loaded.campaigns[1] = BuildCampaign(1, new IslandSaveData
        { land = 1, isNew = false, playTimeDays = 1, Json = Raw("apply-owner-replacement") });
        applyOwner.capture.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, applyOwner.capture });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        CheckDeferredClosed("after-capture owner drift", applyOwner.context, applyOwner.epoch, applyOwner.disk, applyOwner.capture);

        // Table replacement after the first capture is re-checked at Apply.
        var applyTable = BeginCapturedDeferred("apply-table");
        CampaignSaveData.current._islands[11] = new IslandSaveData { land = 0 };
        applyTable.capture.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, applyTable.capture });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        CheckDeferredClosed("after-capture table drift", applyTable.context, applyTable.epoch, applyTable.disk, applyTable.capture);

        // A later GetID observes the mismatch; restoring the old file before Apply must not
        // revive the capture.
        var secondCapture = BeginCapturedDeferred("second-capture");
        GlobalSaveData.filename = "global-v36";
        secondCapture.capture.Capture(Persist(Gun().go), "second-gun");
        Check(secondCapture.capture.Conflict, "later capture observes the frozen-source mismatch");
        GlobalSaveData.filename = "global-v35";
        secondCapture.capture.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, secondCapture.capture });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        CheckDeferredClosed("second capture source loss", secondCapture.context, secondCapture.epoch, secondCapture.disk, secondCapture.capture);

        // A land value that diverges from the frozen slot is also re-checked at Apply.
        var applyLand = BeginCapturedDeferred("apply-land");
        applyLand.target.land = 10;
        applyLand.capture.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, applyLand.capture });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        CheckDeferredClosed("after-capture land divergence", applyLand.context, applyLand.epoch, applyLand.disk, applyLand.capture);

        // A divergent land/slot shape is rejected by the same resolver before the deferred
        // capture can build any key, mint an epoch or charge for a gun.
        Reset();
        var divergent = GeneratePlaceholder(11, "deferred-divergent");
        divergent.land = 10;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase, "divergent deferred source blocks the live identity");
        byte[] beforeDivergent = DiskBytes();
        IslandSaveData.CurrentlySavingIsland = divergent; IslandSaveData.isSavingGame = true;
        var divergentCapture = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = -1, Challenge = 0 };
        divergentCapture.Begin();
        divergentCapture.Capture(Persist(Gun().go), "divergent-gun");
        Check(divergentCapture.ContextKey == null && divergentCapture.Island == null,
            "-1 capture rejects a divergent land/slot without a key");
        divergentCapture.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, divergentCapture });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        Check(DiskBytes().SequenceEqual(beforeDivergent), "divergent -1 capture mints no epoch and writes nothing");
    }

    // R2 failure shape: explicit Save(campaign, 11, challenge) while the generated island's native
    // land field is still 0. The witness comes from the real VirginCapture chain above; nothing is
    // injected and the native field is never rewritten to satisfy the capture.
    static void R2ExplicitRawZeroTests()
    {
        Reset();
        var target = GeneratePlaceholder(11, "r2-explicit");
        string context11 = Ctx(1, 0, 11);
        string epoch11 = MusketeerIdentity.Current.Epoch;
        target.isNew = false; target.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "R2 explicit raw0 starts inside the real generation receipt");
        var gun = Gun(); Check(TryRegister(gun.tool), "R2 gun registered");
        var unitGun = Gun(); Check(TryRegister(unitGun.tool), "R2 unit gun registered");
        var unit = Unit(); Promote(MusketeerIdentity.Current.Careers.Single(x => x.Tool == unitGun.tool), unit);
        string json = IslandJsonAt(0, RowId("r2-gun"), RowId("r2-unit"), RowId("r2-plain"));
        target.Json = Raw(json);
        target.objects = new List<IslandSaveData.ObjectData>
        {
            new() { uniqueID = "r2-gun" }, new() { uniqueID = "r2-unit" }, new() { uniqueID = "r2-plain" }
        };
        IslandSaveData.CurrentlySavingIsland = target; IslandSaveData.isSavingGame = true;
        var capture = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = 11, Challenge = 0 };
        capture.Begin();
        capture.Capture(Persist(gun.go), "r2-gun");
        capture.Capture(Persist(unit.go), "r2-unit");
        Check(capture.Island != null, "R2 explicit raw0 first GetID latches the frozen owner slot");
        Check(capture.ContextKey == context11, "R2 explicit raw0 keeps the args context slot 11");
        capture.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, capture });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        Check(capture.Applied && !capture.Conflict, "R2 explicit raw0 save stages without rewriting the native land");
        Check(target.land == 0, "R2 native land field stays 0 (never written by the mod)");
        Check(MusketeerIdentity.Islands[context11].Epoch == epoch11, "R2 explicit raw0 keeps the generation epoch");
        var rows = RowsOf(context11);
        Check(rows.Any(x => x.NativeId == "r2-gun" && x.Kind == MusketeerCareer.KindGun)
            && rows.Any(x => x.NativeId == "r2-unit" && x.Kind == MusketeerCareer.KindUnit),
            "R2 explicit raw0 stages the witnessed gun and unit rows");
        Check(rows.Count == 2, "R2 unmarked native rows are not invented as careers");
    }

    // Cold restore from a stored raw0 preimage: the native loader is only modeled by the proven
    // 0 -> requested-slot store, so the DTO copy (never the original instance) carries land 11.
    static void ColdPreimageTests()
    {
        Reset();
        string context11 = Ctx(1, 0, 11);
        string epoch = MusketeerArchive.NewScope();
        var record = new MusketeerCareer
        {
            Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun, NativeId = "cold-gun", StockSlot = MusketeerCareer.NoStockSlot
        };
        string raw0 = IslandJsonAt(0, RowId("cold-gun"));
        string hash0 = MusketeerArchive.IslandHash(raw0, epoch);
        var archive = new MusketeerArchive();
        Check(archive.ConfirmBaseline(epoch, hash0, new[] { record }), "cold fixture records the raw0 baseline");
        Check(archive.EnsureContext(context11, epoch, true), "cold fixture registers the slot 11 context");
        Check(MusketeerArchiveStore.Save(ArchivePath, MusketeerArchiveStore.Load(ArchivePath), archive),
            "cold fixture archive is written through the store");

        var rewritten = new IslandSaveData
        {
            land = 11, isNew = false, playTimeDays = 4, Json = Raw(IslandJsonAt(11, RowId("cold-gun"))),
            objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "cold-gun" } }
        };
        var coldCampaign = BuildCampaign(1, new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = Raw("cold-home") });
        InstallIsland(coldCampaign, 11, rewritten);
        coldCampaign.CurrentIsland = rewritten; coldCampaign.CurrentLand = 11;
        GlobalSaveData.loaded = new GlobalSaveData { currentCampaign = 1, currentChallenge = 0, CurrentCampaign = coldCampaign };
        GlobalSaveData.loaded.campaigns.Add(null);
        GlobalSaveData.loaded.campaigns.Add(coldCampaign);
        CampaignSaveData.current = coldCampaign;
        Managers.Inst = new Managers { world = new World { gameLayer = new GameObject().Add(new Transform()) } };
        MusketeerIdentity.InvalidateContextCache();
        Time.frameCount++;

        var gun = Gun();
        string projected = MusketeerArchive.IslandHashPreimage(IslandJsonAt(11, RowId("cold-gun")), epoch, 11);
        Check(projected == hash0, "cold fixture preimage projects exactly the trusted slot token to land 0");
        var capture = new MusketeerPersistence.LoadCapture();
        capture.Begin(rewritten);
        Check(MusketeerIdentity.Islands.ContainsKey(context11), "cold load installs the slot 11 state");
        Check(MusketeerIdentity.Islands[context11].MatchKind == "preimage",
            "cold rawslot load is classified as the bounded preimage compatibility path");
        capture.Capture(rewritten.objects[0], Persist(gun.go));
        capture.End(true);
        Check(MusketeerIdentity.IsGun(gun.tool), "cold rawslot load restores the paid gun through the raw0 preimage");
        Check(MusketeerIdentity.Islands[context11].Epoch == epoch, "cold preimage load keeps the stored epoch");
        Check(MusketeerIdentity.Islands[context11].Careers.Count == 1
            && MusketeerIdentity.Islands[context11].Careers[0].Id == record.Id,
            "cold preimage load restores the exact paid GUID");
        Check(MusketeerIdentity.Islands[context11].HasBaseline, "cold preimage load keeps the pinned raw0 baseline");
        var after = MusketeerArchiveStore.Load(ArchivePath).Archive;
        Check(after.TryGet(epoch, hash0, out var kept) && kept.Records.Count == 1,
            "cold preimage load never rewrites the stored old hash");
        Check(Disk().Scopes[epoch].Count == 1, "cold preimage load adds no compatibility checkpoint");

        // A subsequent real Save stages the actual serialized JSON hash (the old preimage hash is
        // only ever used to explain the historical baseline).
        string land11Json = IslandJsonAt(11, RowId("cold-gun"));
        rewritten.playTimeDays = 5;
        rewritten.Json = Raw(land11Json);
        rewritten.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "cold-gun" } };
        var realSave = SaveOn(rewritten, 1, 11, 0, land11Json, ("cold-gun", gun.go));
        string actualHash = MusketeerArchive.IslandHash(land11Json, epoch);
        Check(realSave.Applied, "post-preimage real save stages through the frozen source");
        Check(actualHash != hash0 && Disk().Scopes[epoch][0].Hash == actualHash,
            "post-preimage real save stages the actual serialized JSON hash");
        Check(MusketeerIdentity.Islands[context11].Epoch == epoch, "post-preimage real save keeps the epoch");

        // Non-land content that differs from the stored fingerprint never matches the preimage.
        var changed = new IslandSaveData
        {
            land = 11, isNew = false, playTimeDays = 4, Json = Raw(IslandJsonAt(11, RowId("cold-gun-changed"))),
            objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "cold-gun-changed" } }
        };
        var changedCampaign = BuildCampaign(1, new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = Raw("cold-home") });
        InstallIsland(changedCampaign, 11, changed);
        changedCampaign.CurrentIsland = changed; changedCampaign.CurrentLand = 11;
        GlobalSaveData.loaded = new GlobalSaveData { currentCampaign = 1, currentChallenge = 0, CurrentCampaign = changedCampaign };
        GlobalSaveData.loaded.campaigns.Add(null);
        GlobalSaveData.loaded.campaigns.Add(changedCampaign);
        CampaignSaveData.current = changedCampaign;
        Managers.Inst = new Managers { world = new World { gameLayer = new GameObject().Add(new Transform()) } };
        MusketeerIdentity.InvalidateContextCache();
        Time.frameCount++;
        var changedGun = Gun();
        string beforeChanged = Convert.ToHexString(DiskBytes());
        var changedCapture = new MusketeerPersistence.LoadCapture();
        changedCapture.Begin(changed);
        changedCapture.Capture(changed.objects[0], Persist(changedGun.go));
        changedCapture.End(true);
        Check(!MusketeerIdentity.IsGun(changedGun.tool), "changed native rows never match the preimage");
        Check(MusketeerIdentity.Islands[context11].Unresolved, "changed native rows keep the paid history as reservations");
        Check(!MusketeerIdentity.CanPurchase, "changed native rows block new charges");
        Check(Convert.ToHexString(DiskBytes()) == beforeChanged, "changed native rows write no baseline");
    }

    // ---- R4: frozen save source, preimage compatibility, deferred-boundary pins ----

    static MusketeerCareer Paid(Guid id, int kind, string nativeId, int stock = MusketeerCareer.NoStockSlot)
        => new() { Id = id, Kind = kind, NativeId = nativeId, StockSlot = stock };

    static void BackgroundSlotSaveTests()
    {
        Reset();
        string homeEpoch = MusketeerIdentity.Current.Epoch;
        Guid[] homeIds = MusketeerIdentity.Current.Careers.Select(x => x.Id).OrderBy(x => x).ToArray();
        var contexts = new Dictionary<int, string>();
        var epochs = new Dictionary<int, string>();
        var islands = new Dictionary<int, IslandSaveData>();
        var guns = new Dictionary<int, GameObject>();
        foreach (int slot in new[] { 0, 4, 8 })
        {
            var target = GeneratePlaceholder(slot, "bg-" + slot);
            target.isNew = false; target.playTimeDays = 2;
            target.land = slot;   // visited island: raw land equals its positional slot (0 stays 0)
            Time.frameCount++; MusketeerIdentity.Tick();
            Check(MusketeerIdentity.CanPurchase, "background slot " + slot + " starts authorized");
            var gun = Gun(); Check(TryRegister(gun.tool), "background slot " + slot + " paid gun registered");
            contexts[slot] = Ctx(1, 0, slot);
            epochs[slot] = MusketeerIdentity.Islands[contexts[slot]].Epoch;
            islands[slot] = target; guns[slot] = gun.go;
        }
        var scene = GeneratePlaceholder(11, "bg-11");
        scene.isNew = false; scene.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        string ctx11 = Ctx(1, 0, 11);
        string epoch11 = MusketeerIdentity.Current.Epoch;
        Guid[] ids11 = MusketeerIdentity.Current.Careers.Select(x => x.Id).OrderBy(x => x).ToArray();
        string HomeRows() => string.Join("|", RowsOf(Context1).OrderBy(x => x.NativeId, StringComparer.Ordinal)
            .Select(x => x.Id + ":" + x.Kind + ":" + x.NativeId + ":" + x.StockSlot));
        string Rows11() => string.Join("|", RowsOf(ctx11).OrderBy(x => x.NativeId, StringComparer.Ordinal)
            .Select(x => x.Id + ":" + x.Kind + ":" + x.NativeId + ":" + x.StockSlot));
        string homeRowsBefore = HomeRows();
        string rows11Before = Rows11();
        int homeSnapshots = Disk().Scopes[homeEpoch].Count;
        int snapshots11 = Disk().Scopes[epoch11].Count;
        foreach (int slot in new[] { 0, 4, 8 })
        {
            string row = "bg-row-" + slot;
            var capture = SaveOn(islands[slot], 1, slot, 0, IslandJsonAt(slot, RowId(row)), (row, guns[slot]));
            Check(capture.Applied && capture.ContextKey == contexts[slot],
                "explicit background Save(" + slot + ") stages under its own frozen context");
            Check(islands[slot].land == slot, "background Save(" + slot + ") leaves the native land value untouched");
            Check(MusketeerIdentity.Islands[contexts[slot]].Epoch == epochs[slot],
                "background Save(" + slot + ") keeps its own epoch");
            Check(RowsOf(contexts[slot]).Any(x => x.NativeId == row),
                "background Save(" + slot + ") writes its exact row");
        }
        Check(MusketeerIdentity.Islands[ctx11].Epoch == epoch11
            && MusketeerIdentity.Islands[ctx11].Careers.Select(x => x.Id).OrderBy(x => x).SequenceEqual(ids11),
            "background saves never move the current slot11 epoch or paid rows");
        Check(MusketeerIdentity.Current.ContextKey == ctx11, "background saves never move the live context");
        Check(MusketeerIdentity.Islands[Context1].Epoch == homeEpoch
            && MusketeerIdentity.Islands[Context1].Careers.Select(x => x.Id).OrderBy(x => x).SequenceEqual(homeIds)
            && HomeRows() == homeRowsBefore && Disk().Scopes[homeEpoch].Count == homeSnapshots,
            "background saves leave the home context rows, hashes and snapshots untouched");
        Check(Rows11() == rows11Before && Disk().Scopes[epoch11].Count == snapshots11,
            "background saves leave the slot11 rows, StockSlot metadata and snapshots untouched");
    }

    static (IslandSaveData target, GameObject gun) ExplicitRawZeroFixture(string label)
    {
        var target = GeneratePlaceholder(11, label);
        target.isNew = false; target.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, label + " starts authorized by the real generation receipt");
        var gun = Gun(); Check(TryRegister(gun.tool), label + " paid gun registered");
        target.playTimeDays = 3;
        target.Json = Raw(IslandJsonAt(0, RowId(label + "-gun")));
        target.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = label + "-gun" } };
        IslandSaveData.CurrentlySavingIsland = target; IslandSaveData.isSavingGame = true;
        return (target, gun.go);
    }

    static void ExplicitWitnessStickyTests()
    {
        // First raw0 capture latches the exact witness; world replacement then loses it, and the
        // later raw==slot value never releases the obligation.
        Reset();
        var first = ExplicitRawZeroFixture("sticky-world");
        string ctx11 = Ctx(1, 0, 11);
        string epoch = MusketeerIdentity.Islands[ctx11].Epoch;
        byte[] disk = DiskBytes();
        var capture = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = 11, Challenge = 0 };
        capture.Begin();
        capture.Capture(Persist(first.gun), "sticky-world-gun");
        Check(capture.Island != null && capture.WitnessRequired,
            "explicit raw0 first capture latches the generation witness obligation");
        Managers.Inst = new Managers { world = new World { gameLayer = new GameObject().Add(new Transform()) } };
        first.target.land = 11;
        first.target.Json = Raw(IslandJsonAt(11, RowId("sticky-world-gun")));
        capture.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, capture });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        Check(!capture.Applied && capture.Conflict,
            "world drift after a raw0 capture stays closed even though raw now reads the slot");
        Check(DiskBytes().SequenceEqual(disk), "sticky witness loss writes no archive bytes");
        Check(MusketeerIdentity.Islands[ctx11].Epoch == epoch, "sticky witness loss keeps the generation epoch");

        // An explicitly revoked witness is equivalent: Apply must not fall back to a current scene.
        Reset();
        var second = ExplicitRawZeroFixture("sticky-revoke");
        string ctx11b = Ctx(1, 0, 11);
        string epochB = MusketeerIdentity.Islands[ctx11b].Epoch;
        byte[] diskB = DiskBytes();
        var revoked = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = 11, Challenge = 0 };
        revoked.Begin();
        revoked.Capture(Persist(second.gun), "sticky-revoke-gun");
        Check(revoked.Island != null && revoked.WitnessRequired, "second raw0 capture latches the witness");
        MusketeerIdentity.RevokeGenerationWitnessForLoad(second.target);
        second.target.land = 11;
        second.target.Json = Raw(IslandJsonAt(11, RowId("sticky-revoke-gun")));
        revoked.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, revoked });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        Check(!revoked.Applied && revoked.Conflict, "a revoked witness fails Apply after the raw value changed");
        Check(DiskBytes().SequenceEqual(diskB) && MusketeerIdentity.Islands[ctx11b].Epoch == epochB,
            "revoked witness refusal preserves the archive and epoch");

        // Symmetric direction: first capture at raw==slot needs nothing, but observing raw0 later
        // in the same save requires the same exact witness, which a background slot never had.
        Reset();
        var background = GeneratePlaceholder(4, "sticky-bg");
        background.isNew = false; background.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        var bgGun = Gun(); Check(TryRegister(bgGun.tool), "background gun registered");
        var scene = GeneratePlaceholder(11, "sticky-bg-11");
        scene.isNew = false; scene.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        string bgContext = Ctx(1, 0, 4);
        string bgEpoch = MusketeerIdentity.Islands[bgContext].Epoch;
        byte[] bgDisk = DiskBytes();
        background.land = 4;   // visited island: first capture observes raw==slot, no witness
        background.playTimeDays = 3;
        background.Json = Raw(IslandJsonAt(4, RowId("sticky-bg-gun")));
        background.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "sticky-bg-gun" } };
        IslandSaveData.CurrentlySavingIsland = background; IslandSaveData.isSavingGame = true;
        var symmetric = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = 4, Challenge = 0 };
        symmetric.Begin();
        symmetric.Capture(Persist(bgGun.go), "sticky-bg-gun");
        Check(symmetric.Island != null && !symmetric.WitnessRequired,
            "first raw==slot capture needs no generation witness");
        background.land = 0;
        symmetric.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, symmetric });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        Check(!symmetric.Applied && symmetric.Conflict,
            "raw0 observed after a raw-slot first capture needs the same witness and sticks");
        Check(DiskBytes().SequenceEqual(bgDisk) && MusketeerIdentity.Islands[bgContext].Epoch == bgEpoch,
            "symmetric witness rejection preserves the background archive and epoch");
    }

    static void ExplicitSourceShapeTests()
    {
        // A first GetID for any island other than the frozen explicit source closes the capture.
        Reset();
        var target = GeneratePlaceholder(11, "shape-other");
        target.isNew = false; target.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        var gun = Gun(); Check(TryRegister(gun.tool), "shape-search gun registered");
        var other = CampaignSaveData.current._islands[1];
        string ctx11 = Ctx(1, 0, 11);
        string epoch = MusketeerIdentity.Islands[ctx11].Epoch;
        byte[] disk = DiskBytes();
        target.Json = Raw(IslandJsonAt(0, RowId("shape-other-gun")));
        target.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "shape-other-gun" } };
        IslandSaveData.CurrentlySavingIsland = other; IslandSaveData.isSavingGame = true;
        var wrongFirst = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = 11, Challenge = 0 };
        wrongFirst.Begin();
        wrongFirst.Capture(Persist(gun.go), "shape-other-gun");
        Check(wrongFirst.Island == null && wrongFirst.Conflict,
            "explicit Save(11) whose first GetID observes another island closes the capture");
        wrongFirst.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, wrongFirst });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        Check(DiskBytes().SequenceEqual(disk) && MusketeerIdentity.Islands[ctx11].Epoch == epoch,
            "wrong first island stages nothing and preserves the epoch");

        // Duplicate table pointer: no unique source at all.
        Reset();
        var dup = GeneratePlaceholder(11, "shape-dup");
        dup.isNew = false; dup.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        var dupGun = Gun(); Check(TryRegister(dupGun.tool), "duplicate-shape gun registered");
        CampaignSaveData.current._islands[5] = CampaignSaveData.current._islands[1];
        byte[] dupDisk = DiskBytes();
        IslandSaveData.CurrentlySavingIsland = CampaignSaveData.current._islands[1]; IslandSaveData.isSavingGame = true;
        var duplicate = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = 1, Challenge = 0 };
        duplicate.Begin();
        duplicate.Capture(Persist(dupGun.go), "shape-dup-gun");
        Check(duplicate.Island == null && duplicate.Conflict, "a duplicated owner-table pointer is not a unique source");
        Check(DiskBytes().SequenceEqual(dupDisk), "duplicate source stages nothing");
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;

        // Island present only at another positional slot than the explicit argument.
        Reset();
        var outside = GeneratePlaceholder(11, "shape-outside");
        outside.isNew = false; outside.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        var outsideGun = Gun(); Check(TryRegister(outsideGun.tool), "outside-shape gun registered");
        byte[] outsideDisk = DiskBytes();
        IslandSaveData.CurrentlySavingIsland = outside; IslandSaveData.isSavingGame = true;
        var mismatch = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = 10, Challenge = 0 };
        mismatch.Begin();
        mismatch.Capture(Persist(outsideGun.go), "shape-outside-gun");
        Check(mismatch.Island == null && mismatch.Conflict,
            "an island only present at another slot is rejected for the explicit args");
        Check(DiskBytes().SequenceEqual(outsideDisk), "outside-slot source stages nothing");
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;

        // A zero native pointer is never an identity.
        Reset();
        var zeroPointer = GeneratePlaceholder(11, "shape-zero-pointer");
        zeroPointer.isNew = false; zeroPointer.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        var zeroPointerGun = Gun(); Check(TryRegister(zeroPointerGun.tool), "zero-pointer gun registered");
        zeroPointer.Pointer = IntPtr.Zero;
        byte[] zeroPointerDisk = DiskBytes();
        IslandSaveData.CurrentlySavingIsland = zeroPointer; IslandSaveData.isSavingGame = true;
        var zeroPointerCapture = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = 11, Challenge = 0 };
        zeroPointerCapture.Begin();
        zeroPointerCapture.Capture(Persist(zeroPointerGun.go), "shape-zero-pointer-gun");
        Check(zeroPointerCapture.Island == null && zeroPointerCapture.Conflict,
            "a zero native island pointer is rejected as a save source");
        Check(DiskBytes().SequenceEqual(zeroPointerDisk), "zero native pointer stages nothing");
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;

        // No GetID at all: the capture never latches and stages nothing.
        Reset();
        var zero = GeneratePlaceholder(11, "shape-zero");
        zero.isNew = false; zero.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        byte[] zeroDisk = DiskBytes();
        IslandSaveData.CurrentlySavingIsland = zero; IslandSaveData.isSavingGame = true;
        var noRows = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = 11, Challenge = 0 };
        noRows.Begin();
        noRows.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, noRows });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        Check(!noRows.Applied && !noRows.Conflict && DiskBytes().SequenceEqual(zeroDisk),
            "an explicit save without any GetID stages nothing");
    }

    static void PreimageResolutionTests()
    {
        string context = Ctx(1, 0, 11);
        string epoch = MusketeerArchive.NewScope();
        string json0 = IslandJsonAt(0, RowId("preimage-paid"));
        string jsonSlot = IslandJsonAt(11, RowId("preimage-paid"));
        string json4 = IslandJsonAt(4, RowId("preimage-paid"));
        string json8 = IslandJsonAt(8, RowId("preimage-paid"));
        Guid id = Guid.NewGuid();
        var archive = new MusketeerArchive();
        Check(archive.ConfirmBaseline(epoch, MusketeerArchive.IslandHash(json0, epoch), new[] { Paid(id, MusketeerCareer.KindGun, "preimage-paid") }),
            "preimage fixture records the raw0 baseline");
        Check(archive.EnsureContext(context, epoch, true), "preimage fixture registers the context");

        var agreed = MusketeerPersistence.Resolve(archive, context, jsonSlot, false, 11);
        Check(agreed.Kind == "preimage" && agreed.Epoch == epoch && !agreed.Unresolved,
            "a trusted slot resolves the unique raw0 preimage");
        Check(agreed.MatchHash == MusketeerArchive.IslandHash(json0, epoch),
            "the preimage match keeps the stored old hash");
        Check(agreed.Records.Count == 1 && agreed.Records[0].Id == id, "the preimage match restores the paid record");

        var untrusted = MusketeerPersistence.Resolve(archive, context, jsonSlot, false);
        Check(untrusted.Kind == "unknown-paid" && untrusted.Unresolved && untrusted.MatchHash == null,
            "without a proven slot the projection is never used and the paid history stays reserved");

        var reverse = MusketeerPersistence.Resolve(archive, context, json0, false, 11);
        Check(reverse.Kind == "exact" && reverse.Epoch == epoch,
            "a raw0 payload is exact and never projected forward to the slot");

        Check(archive.Record(epoch, MusketeerArchive.IslandHash(jsonSlot, epoch), Array.Empty<MusketeerCareer>()),
            "preimage fixture adds an empty exact snapshot in the same epoch");
        var emptyVsPaid = MusketeerPersistence.Resolve(archive, context, jsonSlot, false, 11);
        Check(emptyVsPaid.Kind == "conflict" && emptyVsPaid.Unresolved,
            "empty exact vs paid preimage records quarantine");
        Check(emptyVsPaid.Records.Any(x => x.Id == id), "quarantine preserves the paid history");

        string epoch2 = MusketeerArchive.NewScope();
        Guid otherId = Guid.NewGuid();
        Check(archive.Record(epoch2, MusketeerArchive.IslandHash(jsonSlot, epoch2), new[] { Paid(otherId, MusketeerCareer.KindGun, "preimage-other") }),
            "preimage fixture records a cross-epoch exact snapshot");
        Check(archive.EnsureContext(context, epoch2, true), "preimage fixture activates the second epoch");
        var crossEpoch = MusketeerPersistence.Resolve(archive, context, jsonSlot, false, 11);
        Check(crossEpoch.Kind == "conflict" && crossEpoch.Records.Count >= 2,
            "cross-epoch paid candidates quarantine and preserve both histories");

        // Agreeing candidates: the pinned baseline selects its epoch; no arbitrary newest-wins.
        string pinnedContext = Ctx(1, 0, 4);
        string pinnedEpoch = MusketeerArchive.NewScope();
        string otherEpoch = MusketeerArchive.NewScope();
        var paid = Paid(Guid.NewGuid(), MusketeerCareer.KindUnit, "preimage-agree");
        var pinned = new MusketeerArchive();
        Check(pinned.ConfirmBaseline(pinnedEpoch, MusketeerArchive.IslandHash(json0, pinnedEpoch), new[] { paid }),
            "pinned fixture baseline");
        Check(pinned.EnsureContext(pinnedContext, pinnedEpoch, true), "pinned fixture context");
        Check(pinned.Record(otherEpoch, MusketeerArchive.IslandHash(json0, otherEpoch), new[] { paid }),
            "same paid content in a second epoch");
        Check(pinned.EnsureContext(pinnedContext, otherEpoch, true), "second epoch becomes active");
        var picked = MusketeerPersistence.Resolve(pinned, pinnedContext, json4, false, 4);
        Check(picked.Kind == "preimage" && picked.Epoch == pinnedEpoch,
            "agreeing candidates pick the baseline-pinned epoch");

        // A completed compensation receipt makes its source epoch historical: never searched.
        string excludedContext = Ctx(1, 0, 8);
        string sourceEpoch = MusketeerArchive.NewScope();
        string migrationEpoch = MusketeerArchive.NewScope();
        var grant = Guid.NewGuid();
        var plan = new MusketeerLegacyCompensationPlan(1, Guid.NewGuid(), excludedContext, sourceEpoch, new string('a', 64), 2, 1);
        Check(MusketeerLegacyCompensationPolicy.TryCreatePending(plan, migrationEpoch, new[] { grant }, out var pending, out string reason), reason);
        Check(MusketeerLegacyCompensationPolicy.TryComplete(pending,
            new[] { new MusketeerLegacyGrantCapture(grant, "grant-1", MusketeerLegacyGrantCapture.UnitKind,
                MusketeerLegacyGrantCapture.MusketeerPrefabPath, 1) }, out var completed, out reason), reason);
        var excluded = new MusketeerArchive();
        Check(excluded.Record(sourceEpoch, MusketeerArchive.IslandHash(json0, sourceEpoch),
            new[] { Paid(Guid.NewGuid(), MusketeerCareer.KindGun, "preimage-source") }), "excluded fixture source snapshot");
        Check(excluded.EnsureContext(excludedContext, sourceEpoch, true), "excluded fixture context");
        excluded.Compensations[plan.RequestId] = completed;
        var hidden = MusketeerPersistence.Resolve(excluded, excludedContext, json8, false, 8);
        Check(hidden.Kind == "unknown-paid" && hidden.Unresolved,
            "a compensated historical epoch is never searched for the preimage");
    }

    static void PreimageProjectionTests()
    {
        string scope = "a".PadRight(64, 'b');
        Check(MusketeerArchive.IslandHashPreimage("{\"land\":4,\"objects\":[]}", scope, 4)
            == MusketeerArchive.IslandHash("{\"land\":0,\"objects\":[]}", scope),
            "projection rewrites exactly the trusted top-level land token to 0");
        string unicode = "{\"land\":4,\"note\":\"火枪手\",\"objects\":[]}";
        Check(MusketeerArchive.IslandHashPreimage(unicode, scope, 4)
            == MusketeerArchive.IslandHash("{\"land\":0,\"note\":\"火枪手\",\"objects\":[]}", scope),
            "all non-land bytes are preserved verbatim (UTF-8 round trip)");
        Check(MusketeerArchive.IslandHashPreimage("{\"land\":4.0,\"objects\":[]}", scope, 4) == null, "fraction land is refused");
        Check(MusketeerArchive.IslandHashPreimage("{\"land\":4e0,\"objects\":[]}", scope, 4) == null, "exponent land is refused");
        Check(MusketeerArchive.IslandHashPreimage("{\"land\":-4,\"objects\":[]}", scope, 4) == null, "signed land is refused");
        Check(MusketeerArchive.IslandHashPreimage("{\"land\":04,\"objects\":[]}", scope, 4) == null, "leading-zero land is refused");
        Check(MusketeerArchive.IslandHashPreimage("{\"land\":\"4\",\"objects\":[]}", scope, 4) == null, "string land is refused");
        Check(MusketeerArchive.IslandHashPreimage("{\"land\":4,\"land\":0,\"objects\":[]}", scope, 4) == null, "duplicate land is refused");
        Check(MusketeerArchive.IslandHashPreimage("{\"land\":0,\"objects\":[{\"land\":4}]}", scope, 4) == null,
            "a nested land property never substitutes the top-level token");
        Check(MusketeerArchive.IslandHashPreimage("{\"objects\":[{\"land\":4}]}", scope, 4) == null,
            "a missing top-level land yields no preimage");
        Check(MusketeerArchive.IslandHashPreimage("{\"land\":4,\"objects\":[]}", scope, 5) == null, "a different slot yields no preimage");
        string exact = "{\"land\":4,\"objects\":[]}";
        Check(MusketeerArchive.IslandHashPreimage(exact, scope, 4) != MusketeerArchive.IslandHash(exact, scope),
            "the projected fingerprint differs from the exact hash and is never the save hash");
        Check(MusketeerArchive.IslandHashPreimage(exact, scope, 0) == null, "slot 0 is never projected");
    }

    static void DeferredBoundaryPinTests()
    {
        // Issue #256 (explicitly deferred): a raw==slot island with no stored context is still
        // adopted, minting a fresh epoch and an empty baseline. R4 does not change this path.
        Reset();
        var cold = new IslandSaveData { land = 11, isNew = false, playTimeDays = 5, Json = Raw(IslandJsonAt(11)) };
        var campaign = BuildCampaign(1, new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = Raw("pin-home") });
        InstallIsland(campaign, 11, cold);
        campaign.CurrentIsland = cold; campaign.CurrentLand = 11;
        GlobalSaveData.loaded.campaigns[1] = campaign;
        GlobalSaveData.loaded.CurrentCampaign = campaign;
        CampaignSaveData.current = campaign;
        string ctx11 = Ctx(1, 0, 11);
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.Islands.TryGetValue(ctx11, out var pinPre) || pinPre.Epoch == null,
            "rawslot adoption pin starts without a slot11 epoch");
        var load = new MusketeerPersistence.LoadCapture();
        load.Begin(cold);
        Check(MusketeerIdentity.Islands.ContainsKey(ctx11) && MusketeerIdentity.Islands[ctx11].MatchKind == "fresh",
            "rawslot unknown-context legacy adoption still starts a fresh epoch (Issue #256 pin)");
        load.End(true);
        Check(MusketeerIdentity.Islands[ctx11].HasBaseline,
            "rawslot legacy adoption still confirms an empty baseline (Issue #256 pin)");
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "rawslot legacy adoption still authorizes purchases (Issue #256 pin)");

        // Same key with a new island pointer/world: raw0 without a witness cannot inherit charge
        // authority (unchanged), while raw==slot keeps the existing adoption behavior.
        Reset();
        var home = MusketeerIdentity.Current;
        var paid = Gun(); Check(TryRegister(paid.tool), "same-key pin paid gun registered");
        Save(IslandJson("pin-gun"), Row("pin-gun", paid.go));
        string homeEpoch = home.Epoch;
        Guid[] ids = home.Careers.Select(x => x.Id).OrderBy(x => x).ToArray();
        var replaced = new IslandSaveData { land = 0, isNew = false, playTimeDays = 2, Json = Raw(IslandJsonAt(0)) };
        InstallIsland(CampaignSaveData.current, 1, replaced);
        CampaignSaveData.current.CurrentIsland = replaced; CampaignSaveData.current.CurrentLand = 1;
        Managers.Inst = new Managers { world = new World { gameLayer = new GameObject().Add(new Transform()) } };
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase && !TryRegister(Gun().tool),
            "same-key raw0 island/world replacement without a witness cannot charge (unchanged)");
        Check(MusketeerIdentity.Islands[Context1].Epoch == homeEpoch
            && MusketeerIdentity.Islands[Context1].Careers.Select(x => x.Id).OrderBy(x => x).SequenceEqual(ids),
            "the refused raw0 replacement preserves the old epoch and paid rights");
        replaced.land = 1; replaced.Json = Raw(IslandJsonAt(1));
        Managers.Inst = new Managers { world = new World { gameLayer = new GameObject().Add(new Transform()) } };
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase,
            "rawslot same-key lifecycle adoption still authorizes purchases (Issue #256 pin)");
        Check(MusketeerIdentity.Islands[Context1].Epoch == homeEpoch,
            "rawslot adoption reuses the previous epoch (Issue #256 pin)");
    }

    // R4D-1: the witness obligation must remember the world it was first proven in. A newly
    // generated receipt in another world cannot continue the old capture, even though every
    // current pointer/slot/context still matches.
    static void WitnessWorldFreezeTests()
    {
        Reset();
        var target = GeneratePlaceholder(11, "world-freeze");
        target.isNew = false; target.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "world-freeze fixture starts authorized");
        var gun = Gun(); Check(TryRegister(gun.tool), "world-freeze paid gun registered");
        string context11 = Ctx(1, 0, 11);
        var firstSave = SaveOn(target, 1, 11, 0, IslandJsonAt(0, RowId("world-freeze-gun")), ("world-freeze-gun", gun.go));
        Check(firstSave.Applied, "world-freeze fixture records the paid raw0 history in W1");
        string oldEpoch = MusketeerIdentity.Islands[context11].Epoch;
        string oldHash = MusketeerArchive.IslandHash(IslandJsonAt(0, RowId("world-freeze-gun")), oldEpoch);
        Guid paidId = MusketeerIdentity.Islands[context11].Careers.Single().Id;

        target.isNew = false; target.playTimeDays = 3;
        target.Json = Raw(IslandJsonAt(0, RowId("world-freeze-gun")));
        target.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "world-freeze-gun" } };
        IslandSaveData.CurrentlySavingIsland = target; IslandSaveData.isSavingGame = true;
        var capture = new MusketeerPersistence.SaveCapture { Campaign = 1, Land = 11, Challenge = 0 };
        capture.Begin();
        capture.Capture(Persist(gun.go), "world-freeze-gun");
        Check(capture.Island != null && capture.WitnessRequired && !capture.Conflict,
            "W1 first raw0 capture latches and verifies its witness");

        // Real new generation in W2 on the same island/slot/context. The once guard allows it
        // because the world differs; this is a genuinely established new receipt, not an injected
        // flag and not merely an expired old witness.
        Managers.Inst = new Managers { world = new World { gameLayer = new GameObject().Add(new Transform()) } };
        target.isNew = true; target.playTimeDays = 0;
        target.Json = Raw(IslandJsonAt(0));
        var regeneration = new MusketeerPersistence.VirginCapture();
        regeneration.Begin(CampaignSaveData.current);
        regeneration.Complete(CampaignSaveData.current);
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(capture.ContextKey == Ctx(1, 0, 11), "W2 generation keeps the same context key");
        Check(Disk().Contexts[context11].Epochs.Count >= 2, "W2 generation committed a new epoch");

        capture.Capture(Persist(gun.go), "world-freeze-gun");
        Check(capture.Conflict, "a witness rebuilt in W2 cannot continue the W1 capture");
        capture.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, capture });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        Check(!capture.Applied, "the W2-latched capture cannot stage");
        var archive = Disk();
        Check(archive.TryGet(oldEpoch, oldHash, out var oldSnapshot)
            && oldSnapshot.Records.Any(x => x.Id == paidId && x.NativeId == "world-freeze-gun"),
            "the original W1 epoch keeps its paid history");
        Check(archive.Contexts[context11].Epochs.Contains(oldEpoch), "the original W1 epoch remains in the context");
    }

    // R4D-2: a brand-new generation is not a historical raw0 load. VirginCapture must resolve
    // exact-only, so an old raw0 history in the same context cannot be inherited or quarantined
    // against the freshly committed epoch.
    static void VirginExactOnlyTests()
    {
        Reset();
        string context11 = Ctx(1, 0, 11);
        string oldEpoch = MusketeerArchive.NewScope();
        string oldJson = "{\"land\":0,\"objects\":[],\"playTimeDays\":0}";
        string slotJson = "{\"land\":11,\"objects\":[],\"playTimeDays\":0}";
        var reservation = Paid(Guid.NewGuid(), MusketeerCareer.KindGun, "");
        var archive = new MusketeerArchive();
        Check(archive.ConfirmBaseline(oldEpoch, MusketeerArchive.IslandHash(oldJson, oldEpoch), new[] { reservation }),
            "virgin-exact fixture records the old raw0 reservation epoch");
        Check(archive.EnsureContext(context11, oldEpoch, true), "virgin-exact fixture registers the old context");
        Check(MusketeerArchiveStore.Save(ArchivePath, MusketeerArchiveStore.Load(ArchivePath), archive),
            "virgin-exact fixture writes the old history through the store");

        var campaign = CampaignSaveData.current;
        var target = new IslandSaveData { land = 11, isNew = true, playTimeDays = 0, Json = Raw(slotJson) };
        InstallIsland(campaign, 11, target);
        campaign.CurrentIsland = target; campaign.CurrentLand = 11;
        MusketeerIdentity.InvalidateContextCache();
        var virgin = new MusketeerPersistence.VirginCapture();
        virgin.Begin(campaign);
        virgin.Complete(campaign);
        Time.frameCount++; MusketeerIdentity.Tick();

        var context = Disk().Contexts[context11];
        string newEpoch = context.Epochs.Single(epoch => epoch != oldEpoch);
        Check(MusketeerIdentity.Islands[context11].Epoch == newEpoch,
            "new generation keeps its own epoch instead of inheriting the raw0 history");
        Check(MusketeerIdentity.Islands[context11].HasBaseline && !MusketeerIdentity.Islands[context11].Unresolved,
            "new generation keeps its own empty baseline and is not quarantined");
        Check(MusketeerIdentity.Islands[context11].Careers.Count == 0, "new generation inherits no old paid record");
        Check(MusketeerIdentity.CanPurchase, "a clean new generation stays purchase-ready");
        Check(Disk().Scopes[newEpoch].Count == 1 && Disk().Scopes[newEpoch][0].Records.Count == 0,
            "the new epoch has exactly its empty baseline");
        Check(Disk().TryGet(oldEpoch, MusketeerArchive.IslandHash(oldJson, oldEpoch), out var oldSnapshot)
            && oldSnapshot.Records.Count == 1 && oldSnapshot.Records[0].Id == reservation.Id,
            "the old raw0 scope and paid reservation stay byte-identical");
        Check(Disk().LatestReservations(oldEpoch).Count == 1, "the old paid reservation remains visible for history");
    }

    static void ChallengeOwnerTests()
    {
        Reset();
        var normal = CampaignSaveData.current;
        var challengeHome = new IslandSaveData { land = 2, isNew = false, playTimeDays = 1, Json = Raw("challenge-home") };
        var challenge = BuildCampaign(2, challengeHome);
        challenge.challengeId = 7;
        GlobalSaveData.loaded.challenges.Add(challenge);
        GlobalSaveData.loaded.currentCampaign = -1;
        GlobalSaveData.loaded.currentChallenge = 7;
        GlobalSaveData.loaded.CurrentCampaign = challenge;
        CampaignSaveData.current = challenge;
        MusketeerIdentity.InvalidateContextCache();
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.Current != null
            && MusketeerIdentity.Current.ContextKey == Ctx(-1, 7, 2),
            "live challenge owner resolves a unique challengeId with campaign index -1");
        Check(CatalogOwnerIs(challenge, -1, 7),
            "catalog challenge owner accepts campaign index -1");

        // Virgin generation under the same negative campaign index.
        var challengeTarget = new IslandSaveData { land = 0, isNew = true, playTimeDays = 0, Json = Raw("challenge-generation") };
        SetCurrentIsland(challengeTarget, 11);
        var virgin = new MusketeerPersistence.VirginCapture();
        virgin.Begin(challenge);
        virgin.Complete(challenge);
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.Current.ContextKey == Ctx(-1, 7, 11) && MusketeerIdentity.CanPurchase,
            "generation under campaign -1 / challenge 7 keys its unique slot");

        // -1 deferred save under the same owner.
        var challengeGun = Gun(); Check(TryRegister(challengeGun.tool), "challenge paid gun registered");
        challengeTarget.isNew = false; challengeTarget.playTimeDays = 2;
        challengeTarget.Json = Raw(IslandJsonAt(0, RowId("challenge-gun")));
        challengeTarget.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "challenge-gun" } };
        Time.frameCount++; MusketeerIdentity.Tick();
        IslandSaveData.CurrentlySavingIsland = challengeTarget; IslandSaveData.isSavingGame = true;
        var challengeSave = new MusketeerPersistence.SaveCapture { Campaign = -1, Land = -1, Challenge = 7 };
        challengeSave.Begin();
        challengeSave.Capture(Persist(challengeGun.go), "challenge-gun");
        Check(challengeSave.ContextKey == Ctx(-1, 7, 11) && challengeSave.Island != null,
            "-1 deferred save resolves the unique challenge owner with campaign -1");
        challengeTarget.land = 11;
        challengeTarget.Json = Raw(IslandJsonAt(11, RowId("challenge-gun")));
        challengeSave.Apply();
        InvokePatch(typeof(MusketeerPersistence.SavePatch), "Finally", new object[] { null, challengeSave });
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
        Check(challengeSave.Applied, "challenge deferred save stages under the unique challenge owner");

        // Exact load under the same owner.
        challenge.CurrentIsland = challengeHome;
        challenge.CurrentLand = 2;
        MusketeerIdentity.InvalidateContextCache();
        var load = new MusketeerPersistence.LoadCapture();
        load.Begin(challengeHome);
        load.End(true);
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.Current.ContextKey == Ctx(-1, 7, 2),
            "exact load resolves the unique challenge owner with campaign -1");

        // Duplicate challengeId across the two catalogs is ambiguous: no key, no stage, no epoch.
        var duplicate = BuildCampaign(2, new IslandSaveData { land = 2, isNew = false, playTimeDays = 1, Json = Raw("challenge-duplicate") });
        duplicate.challengeId = 7;
        GlobalSaveData.loaded.campaigns.Add(duplicate);
        Check(!CatalogOwnerIs(challenge, -1, 7),
            "duplicate challengeId across catalogs is refused");
        byte[] duplicateBefore = DiskBytes();
        challenge.CurrentIsland = challengeHome;
        challenge.CurrentLand = 2;
        MusketeerIdentity.InvalidateContextCache();
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase && !TryRegister(Gun().tool),
            "duplicate challengeId blocks live identity without charging");
        Check(DiskBytes().SequenceEqual(duplicateBefore), "duplicate challengeId mints no epoch or archive bytes");
        GlobalSaveData.loaded.campaigns.Remove(duplicate);

        // A normal campaign index of -1 is still invalid.
        GlobalSaveData.loaded.currentCampaign = -1;
        GlobalSaveData.loaded.currentChallenge = 0;
        GlobalSaveData.loaded.CurrentCampaign = normal;
        CampaignSaveData.current = normal;
        normal.CurrentIsland = normal._islands[1];
        normal.CurrentLand = 1;
        MusketeerIdentity.InvalidateContextCache();
        Check(!CatalogOwnerIs(normal, -1, 0),
            "normal negative campaign index is not an owner");
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase, "normal negative campaign blocks live identity");
    }

    static void WitnessBoundaryTests()
    {
        // Exact island load revokes the receipt even when the load itself cannot proceed.
        Reset();
        var target = GeneratePlaceholder(11, "load-revoke");
        target.isNew = false; target.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick(); Check(MusketeerIdentity.CanPurchase, "receipt before load");
        byte[] beforeLoad = DiskBytes();
        var load = new MusketeerPersistence.LoadCapture();
        load.Begin(target);
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase && !TryRegister(Gun().tool), "exact island load revokes the generation receipt");
        Check(DiskBytes().SequenceEqual(beforeLoad), "receipt revocation writes no archive data");

        // World replacement revokes it as well.
        Reset();
        target = GeneratePlaceholder(11, "world-revoke");
        target.isNew = false; target.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick(); Check(MusketeerIdentity.CanPurchase, "receipt before world change");
        Managers.Inst.world.gameLayer = new GameObject().Add(new Transform());
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase && !TryRegister(Gun().tool), "world change revokes the generation receipt");

        // Owner replacement with the same catalog index revokes it.
        Reset();
        target = GeneratePlaceholder(11, "owner-revoke");
        target.isNew = false; target.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick(); Check(MusketeerIdentity.CanPurchase, "receipt before owner change");
        var replacement = BuildCampaign(1, new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = Raw("replacement") });
        GlobalSaveData.loaded.campaigns[1] = replacement;
        GlobalSaveData.loaded.CurrentCampaign = replacement;
        CampaignSaveData.current = replacement;
        var replacementTarget = new IslandSaveData { land = 0, isNew = false, playTimeDays = 2, Json = Raw("replacement-target") };
        InstallIsland(replacement, 11, replacementTarget);
        replacement.CurrentIsland = replacementTarget;
        replacement.CurrentLand = 11;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase && !TryRegister(Gun().tool),
            "owner change revokes the generation receipt for the old owner's slot");

        // The catalog entry, the native getter and the scene static must name one instance.
        Reset();
        var catalogMismatch = BuildCampaign(1, new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = Raw("catalog") });
        GlobalSaveData.loaded.campaigns[1] = catalogMismatch;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase && !TryRegister(Gun().tool),
            "catalog owner disagreement with the scene owner blocks identity");
        Reset();
        GlobalSaveData.loaded.CurrentCampaign = BuildCampaign(1, new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = Raw("getter") });
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase, "native getter disagreement with the scene static blocks identity");

        // File replacement with the same in-memory table revokes it.
        Reset();
        target = GeneratePlaceholder(11, "file-revoke");
        target.isNew = false; target.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick(); Check(MusketeerIdentity.CanPurchase, "receipt before file change");
        GlobalSaveData.filename = "global-v36";
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase, "file change revokes the generation receipt");
        GlobalSaveData.filename = "global-v35";

        // A positional move invalidates the slot stored in the receipt.
        Reset();
        target = GeneratePlaceholder(11, "slot-revoke");
        target.isNew = false; target.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick(); Check(MusketeerIdentity.CanPurchase, "receipt before slot move");
        CampaignSaveData.current._islands[10] = target;
        CampaignSaveData.current._islands[11] = new IslandSaveData { land = 0 };
        CampaignSaveData.current.CurrentLand = 10;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase, "slot move revokes the generation receipt");

        // A -> B (same owner/world) -> A: observing another current island is itself a
        // lifecycle change; merely pausing authorization must not revive the old receipt.
        var visitA = AuthorizedPlaceholder("A->B->A", out string visitCtx, out string visitEpoch,
            out Guid[] visitPaid, out byte[] visitDisk);
        var homeB = CampaignSaveData.current._islands[1];
        CampaignSaveData.current.CurrentIsland = homeB; CampaignSaveData.current.CurrentLand = 1;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.Current.ContextKey == Context1, "B resolves its own home context");
        CampaignSaveData.current.CurrentIsland = visitA; CampaignSaveData.current.CurrentLand = 11;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase && !TryRegister(Gun().tool),
            "A->B->A does not revive the generation receipt");
        CheckWitnessStateUnchanged("A->B->A", visitCtx, visitEpoch, visitPaid, visitDisk);

        // A -> null -> A: a null current island is a lifecycle change too.
        var nullA = AuthorizedPlaceholder("A->null->A", out string nullCtx, out string nullEpoch,
            out Guid[] nullPaid, out byte[] nullDisk);
        CampaignSaveData.current.CurrentIsland = null; CampaignSaveData.current.CurrentLand = 11;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase, "null current island revokes the generation receipt");
        CampaignSaveData.current.CurrentIsland = nullA;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase && !TryRegister(Gun().tool),
            "A->null->A does not revive the generation receipt");
        CheckWitnessStateUnchanged("A->null->A", nullCtx, nullEpoch, nullPaid, nullDisk);

        // Native getter disagreement clears the receipt; restoring the getter must not revive it.
        var getterA = AuthorizedPlaceholder("getter-mismatch", out string getterCtx, out string getterEpoch,
            out Guid[] getterPaid, out byte[] getterDisk);
        var originalGetter = GlobalSaveData.loaded.CurrentCampaign;
        GlobalSaveData.loaded.CurrentCampaign = BuildCampaign(1, new IslandSaveData
        { land = 1, isNew = false, playTimeDays = 1, Json = Raw("getter-mismatch-owner") });
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase, "native getter disagreement revokes the generation receipt");
        GlobalSaveData.loaded.CurrentCampaign = originalGetter;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase && !TryRegister(Gun().tool),
            "getter recovery does not revive the generation receipt");
        CheckWitnessStateUnchanged("getter-mismatch", getterCtx, getterEpoch, getterPaid, getterDisk);

        // Scene static disagreement clears it as well; restoring the static must not revive it.
        var staticA = AuthorizedPlaceholder("static-mismatch", out string staticCtx, out string staticEpoch,
            out Guid[] staticPaid, out byte[] staticDisk);
        var originalStatic = CampaignSaveData.current;
        CampaignSaveData.current = BuildCampaign(1, new IslandSaveData
        { land = 1, isNew = false, playTimeDays = 1, Json = Raw("static-mismatch-owner") });
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase, "scene static disagreement revokes the generation receipt");
        CampaignSaveData.current = originalStatic;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase && !TryRegister(Gun().tool),
            "static recovery does not revive the generation receipt");
        CheckWitnessStateUnchanged("static-mismatch", staticCtx, staticEpoch, staticPaid, staticDisk);
        Check(staticA.land == 0, "lifecycle revocation does not rewrite native land");

        // Zero native pointers are not identities even when the table has one matching wrapper.
        Reset();
        var zeroOwner = CampaignSaveData.current;
        var zeroIsland = zeroOwner.CurrentIsland;
        IntPtr ownerPointer = zeroOwner.Pointer;
        IntPtr islandPointer = zeroIsland.Pointer;
        byte[] zeroBefore = DiskBytes();
        zeroOwner.Pointer = IntPtr.Zero;
        Check(SlotLookupResult(zeroOwner, zeroIsland, out int zeroSlot) == "NotFound"
            && zeroSlot == -1, "zero owner pointer is rejected before table/field reads");
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase, "zero owner pointer produces no live key");
        Check(DiskBytes().SequenceEqual(zeroBefore), "zero owner pointer mints no epoch or archive bytes");
        zeroOwner.Pointer = ownerPointer;
        zeroIsland.Pointer = IntPtr.Zero;
        Check(SlotLookupResult(zeroOwner, zeroIsland, out int zeroIslandSlot) == "NotFound"
            && zeroIslandSlot == -1, "zero island pointer is rejected before table/field reads");
        zeroIsland.Pointer = islandPointer;
        Check(SlotLookupResult(zeroOwner, zeroIsland, out int restoredSlot) == "Ok"
            && restoredSlot == 1, "restored pointers resolve the unique table member again");

        // Without a receipt, a non-virgin land0 placeholder is never accepted as slot 11.
        Reset();
        target = GeneratePlaceholder(11, "no-receipt");
        target.isNew = false; target.playTimeDays = 2;
        SetStaticIfPresent("_generation", null);
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase && !TryRegister(Gun().tool), "no witness means no live land0 identity");

        // Divergent, duplicate and out-of-table members fail closed without charging or writing.
        Reset();
        target = GeneratePlaceholder(11, "divergent");
        int beforeContexts = Disk().Contexts.Count;
        int beforeScopes = Disk().Scopes.Count;
        int beforeCareers = MusketeerIdentity.Current.Careers.Count;
        target.land = 10;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase && !TryRegister(Gun().tool), "non-zero divergent land is rejected");
        Check(Disk().Contexts.Count == beforeContexts && Disk().Scopes.Count == beforeScopes
            && MusketeerIdentity.Current.Careers.Count == beforeCareers, "divergent placeholder mints no epoch or career");

        Reset();
        target = GeneratePlaceholder(11, "duplicate");
        CampaignSaveData.current._islands[5] = target;
        beforeContexts = Disk().Contexts.Count;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase, "duplicate island pointer is rejected");
        Check(Disk().Contexts.Count == beforeContexts, "duplicate pointer mints no context");

        Reset();
        target = GeneratePlaceholder(11, "missing");
        CampaignSaveData.current._islands[11] = new IslandSaveData { land = 0 };
        beforeContexts = Disk().Contexts.Count;
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.CanPurchase, "out-of-table island is rejected");
        Check(Disk().Contexts.Count == beforeContexts, "out-of-table island mints no context");

        // A save of another island only reads the receipt; it does not revoke it.
        Reset();
        target = GeneratePlaceholder(11, "foreign-save");
        target.isNew = false; target.playTimeDays = 2;
        Time.frameCount++; MusketeerIdentity.Tick(); Check(MusketeerIdentity.CanPurchase, "receipt before foreign save");
        var home = CampaignSaveData.current._islands[1];
        var foreign = new GameObject();
        SaveOn(home, 1, 1, 0, IslandJsonAt(1), ("home-foreign-row", foreign));
        Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "a save of another island does not revoke the receipt");
    }

    static void SharedWiringTests()
    {
        HeavyShieldWiringSpy.Reset();
        Reset();
        GlobalSaveData.loaded.prefs.contents["synthetic-paid-heavy-prefs"] = "{\"paid\":true}";
        string heavyBefore = GlobalSaveData.loaded.prefs.contents["synthetic-paid-heavy-prefs"];

        var campaign = CampaignSaveData.current;
        var island = new IslandSaveData { land = 0, isNew = true, playTimeDays = 0, Json = Raw("wiring") };
        SetCurrentIsland(island, 11);
        object[] generation = { campaign, null };
        InvokePatch(typeof(MusketeerPersistence.VirginPatch), "Before", generation);
        var state = generation[1];
        Check(HeavyShieldWiringSpy.GenerationBegins == 1 && HeavyShieldWiringSpy.GenerationOpen == 1,
            "generation begin wired exactly once");
        InvokePatch(typeof(MusketeerPersistence.VirginPatch), "After", new object[] { campaign, state });
        InvokePatch(typeof(MusketeerPersistence.VirginPatch), "Finally", new object[] { null, campaign, state });
        Check(HeavyShieldWiringSpy.GenerationEnds == 1 && HeavyShieldWiringSpy.GenerationOpen == 0
            && HeavyShieldWiringSpy.LastGenerationSuccess == true, "generation end/begin pair closes exactly once");
        Check(GlobalSaveData.loaded.prefs.contents["synthetic-paid-heavy-prefs"] == heavyBefore,
            "generation leaves synthetic paid Heavy prefs bytes untouched");

        // An exception finalizer closes the same token as a failure, and a second End is not
        // counted as a second balanced close.
        object[] failing = { campaign, null };
        InvokePatch(typeof(MusketeerPersistence.VirginPatch), "Before", failing);
        var failingState = failing[1];
        InvokePatch(typeof(MusketeerPersistence.VirginPatch), "Finally",
            new object[] { new InvalidOperationException("generation failed"), campaign, failingState });
        Check(HeavyShieldWiringSpy.GenerationEnds == 2 && HeavyShieldWiringSpy.GenerationOpen == 0
            && HeavyShieldWiringSpy.LastGenerationSuccess == false, "exception finalizer closes generation as failed");
        InvokePatch(typeof(MusketeerPersistence.VirginPatch), "Finally", new object[] { null, campaign, failingState });
        Check(HeavyShieldWiringSpy.GenerationDoubleEnds == 1 && HeavyShieldWiringSpy.GenerationOpen == 0,
            "repeated generation end is detected, not double counted");

        // Nested load scopes are paired LIFO and leave no open token.
        HeavyShieldWiringSpy.Reset();
        Reset();
        var home = Island();
        home.Json = Raw(IslandJsonAt(1)); home.isNew = false; home.playTimeDays = 1;
        home.objects = new List<IslandSaveData.ObjectData>();
        object[] outer = { home, null };
        InvokePatch(typeof(MusketeerPersistence.PopPatch), "Before", outer);
        object[] inner = { home, null };
        InvokePatch(typeof(MusketeerPersistence.PopPatch), "Before", inner);
        Check(HeavyShieldWiringSpy.LoadBegins == 2 && HeavyShieldWiringSpy.LoadOpen == 2, "nested load scopes both open");
        InvokePatch(typeof(MusketeerPersistence.PopPatch), "Finally", new object[] { null, true, inner[1] });
        Check(HeavyShieldWiringSpy.LoadEnds == 1 && HeavyShieldWiringSpy.LoadOpen == 1, "inner load scope closes first");
        InvokePatch(typeof(MusketeerPersistence.PopPatch), "Finally", new object[] { null, true, outer[1] });
        Check(HeavyShieldWiringSpy.LoadEnds == 2 && HeavyShieldWiringSpy.LoadOpen == 0
            && HeavyShieldWiringSpy.LastLoadSuccess == true, "outer load scope closes last, all tokens paired");
    }

    // ---- transfers: tool -> unit -> dropped tool, one owner, never a Peasant ----

    static void TransferTests()
    {
        Reset();
        var guns = new List<(GameObject go, DroppableTool tool)>();
        for (int i = 0; i < 5; i++)
        {
            var gun = Gun();
            Check(TryRegister(gun.tool), "multiple paid guns registered");
            Check(MusketeerIdentity.IsGun(gun.tool), "registered gun marked");
            Check(MusketeerIdentity.StockSlot(gun.tool) == -1, "plain registration carries no rack claim");
            guns.Add(gun);
        }
        Check(!MusketeerIdentity.IsGun(null) && !MusketeerIdentity.IsUnit(null) && !MusketeerIdentity.IsMarked(null), "null queries are safe");
        var units = new List<(GameObject go, Character character, Archer archer)>();
        foreach (var gun in guns)
        {
            MusketeerIdentity.OnGunPickupBegin(gun.tool, out var state);
            Check(MusketeerIdentity.GunPromotionInProgress, "promotion window open for other mod prefixes");
            var unit = Unit();
            MusketeerIdentity.OnGunPickupEnd(unit.character, state);
            Check(MusketeerIdentity.GunPromotionInProgress, "window stays open until the finalizer");
            MusketeerIdentity.OnGunPickupAbort(state);
            Check(MusketeerIdentity.IsUnit(unit.archer) && !MusketeerIdentity.IsGun(gun.tool), "career moved to the exact unit with a single owner");
            Check(!MusketeerIdentity.IsMarked(gun.go), "the consumed gun no longer carries the career");
            units.Add(unit);
        }
        Check(!MusketeerIdentity.GunPromotionInProgress, "promotion windows all closed");
        var unitList = new List<Archer>(); MusketeerIdentity.CopyUnits(unitList);
        var gunList = new List<DroppableTool>(); MusketeerIdentity.CopyGuns(gunList);
        Check(unitList.Count == 5 && gunList.Count == 0, "five unit careers, no gun careers");
        Check(unitList.Distinct().Count() == 5, "no duplicate owners in the copy");
        // the unit -> gun transfer is observed at the exact native drop call: the dropper is the
        // paid unit and the dropped object is the one native code passed to Droppable.Drop
        var dropped = Gun();
        dropped.tool.dropper = units[0].go;
        MusketeerIdentity.OnDroppableDrop(dropped.tool, dropped.tool.dropper);
        Check(!MusketeerIdentity.IsUnit(units[0].archer) && MusketeerIdentity.IsGun(dropped.tool), "old unit identity ends, the exact dropped gun owns the career");
        Check(MusketeerIdentity.StockSlot(dropped.tool) == -1, "a dropped gun never carries a rack claim");
        var noDropper = Gun();
        noDropper.tool.dropper = units[1].go; // stale stored owner, but this native call passed null
        MusketeerIdentity.OnDroppableDrop(noDropper.tool);
        Check(MusketeerIdentity.IsUnit(units[1].archer), "null current dropper cannot steal a stale stored owner career");
        Check(!MusketeerIdentity.IsGun(noDropper.tool), "a drop without a native dropper transfers nothing");
        var strangerDrop = Gun();
        strangerDrop.tool.dropper = new GameObject();
        MusketeerIdentity.OnDroppableDrop(strangerDrop.tool, strangerDrop.tool.dropper);
        Check(!MusketeerIdentity.IsGun(strangerDrop.tool), "a drop by an untracked object transfers nothing");
        MusketeerAccess.TrackAllowed = false;
        var offlineDrop = Gun();
        offlineDrop.tool.dropper = units[3].go;
        MusketeerIdentity.OnDroppableDrop(offlineDrop.tool, offlineDrop.tool.dropper);
        Check(!MusketeerIdentity.IsGun(offlineDrop.tool) && !MusketeerIdentity.IsUnit(units[3].archer), "lost authority does not expose either owner");
        MusketeerAccess.TrackAllowed = true;
        Check(!MusketeerIdentity.IsGun(offlineDrop.tool) && MusketeerIdentity.IsUnit(units[3].archer), "lost authority preserved the original owner without transfer");
        var six = Gun();
        Check(TryRegister(six.tool), "sixth gun registered");
        MusketeerIdentity.OnGunPickupBegin(six.tool, out var sixState);
        var peasant = new GameObject(); var peasantCharacter = peasant.Add(new Character());
        MusketeerIdentity.OnGunPickupEnd(peasantCharacter, sixState);
        MusketeerIdentity.OnGunPickupAbort(sixState);
        Check(!MusketeerIdentity.IsMarked(peasant), "a career never attaches to a Peasant");
        Check(!MusketeerIdentity.GunPromotionInProgress, "window closed after a refused transfer");
        var hammerRoot = new GameObject(); hammerRoot.tag = "Hammer"; var hammer = hammerRoot.Add(new DroppableTool());
        hammer.dropper = units[1].go;
        MusketeerIdentity.OnDroppableDrop(hammer, hammer.dropper);
        Check(!MusketeerIdentity.IsGun(hammer) && MusketeerIdentity.IsUnit(units[1].archer), "a non-bow drop leaves the paid career with its unit");
        var afterUnits = new List<Archer>(); MusketeerIdentity.CopyUnits(afterUnits);
        var afterGuns = new List<DroppableTool>(); MusketeerIdentity.CopyGuns(afterGuns);
        Check(afterUnits.Count == 4 && afterGuns.Count == 1, "copies reflect exactly the current owners");
        Check(MusketeerIdentity.IsUnit(units[2].archer), "untouched careers keep their owner");
    }

    // ---- the drop boundary: Character.DropItem must never be detoured ----

    // Live-log regression pin: Character.DropItem's only parameter is
    // Il2CppSystem.Nullable<Vector2>; native callers pass an empty Nullable (the Grab and death
    // paths call DropItem(null)), and the HarmonyX IL2CPP detour wrapper cannot marshal it: the
    // copied interop stub converts the managed-null argument through Il2CppObjectBaseToPtrNotNull,
    // which throws NullReferenceException before the native body ever runs. That detour both
    // logged NREs and silently swallowed every native bow drop. The paid unit -> gun transfer is
    // observed at Droppable.Drop (see OnDroppableDrop); this check fails loudly if a detour on
    // the nullable boundary is ever reintroduced.
    static void DropBoundaryTests()
    {
        var offenders = Assembly.GetExecutingAssembly().GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false)
                .Cast<HarmonyLib.HarmonyPatch>().Select(a => (type: t, patch: a)))
            .Where(x => x.patch.TargetType == typeof(Character) && x.patch.TargetMethod == "DropItem")
            .Select(x => x.type.FullName)
            .ToList();
        Check(offenders.Count == 0, "no Harmony detour on Character.DropItem (its nullable direction is unsafe under the IL2CPP trampoline): " + string.Join(",", offenders));
    }

    // ---- per-call promotion scopes: nesting, consumption, reuse, failure ----

    static void PromotionScopeTests()
    {
        Reset();
        var outer = Gun(); var inner = Gun(); var plainRoot = new GameObject();
        Check(TryRegister(outer.tool) && TryRegister(inner.tool), "two paid guns");
        MusketeerIdentity.OnGunPickupBegin(outer.tool, out var outerState);
        Check(MusketeerIdentity.GunPromotionInProgress, "outer scope opened");
        // an unmarked tool inside the same call never engages or closes the shared scope
        MusketeerIdentity.OnGunPickupBegin(plainRoot.Add(new DroppableTool()), out var plainState);
        Check(!plainState.Engaged && MusketeerIdentity.GunPromotionInProgress, "unmarked nested tool engages nothing");
        MusketeerIdentity.OnGunPickupEnd(null, plainState);
        MusketeerIdentity.OnGunPickupAbort(plainState);
        Check(MusketeerIdentity.GunPromotionInProgress, "unmarked nested finalizer cannot close the marked scope");
        // a marked nested promotion keeps the flag until both finalizers ran
        MusketeerIdentity.OnGunPickupBegin(inner.tool, out var innerState);
        Check(innerState.Engaged && MusketeerIdentity.GunPromotionInProgress, "nested marked scope opened");
        var innerUnit = Unit();
        MusketeerIdentity.OnGunPickupEnd(innerUnit.character, innerState);
        Check(MusketeerIdentity.GunPromotionInProgress, "postfix never closes the scope");
        MusketeerIdentity.OnGunPickupAbort(innerState);
        Check(MusketeerIdentity.GunPromotionInProgress, "inner finalizer leaves the outer scope open");
        var outerUnit = Unit();
        MusketeerIdentity.OnGunPickupEnd(outerUnit.character, outerState);
        MusketeerIdentity.OnGunPickupAbort(outerState);
        Check(!MusketeerIdentity.GunPromotionInProgress, "outermost finalizer closes the scope");
        Check(MusketeerIdentity.IsUnit(innerUnit.archer) && MusketeerIdentity.IsUnit(outerUnit.archer), "both nested careers transferred");
        // the captured career is consumed by a confirmed despawn inside the call, then the same
        // pool root is reused for another paid gun before the postfix runs
        var consumed = Gun();
        Check(TryRegister(consumed.tool, 0), "consumed gun registered on rack slot 0");
        MusketeerIdentity.OnGunPickupBegin(consumed.tool, out var consumedState);
        MusketeerIdentity.OnPoolDespawnBegin(consumed.go, 0f, out var despawn);
        consumed.go.activeInHierarchy = false;
        MusketeerIdentity.OnPoolDespawnEnd(despawn);
        consumed.go.activeInHierarchy = true;
        MusketeerIdentity.OnPoolSpawn(consumed.go);
        var reused = consumed.go.GetComponent<DroppableTool>();
        Check(TryRegister(reused, 1), "same pool root reused for a new rack gun");
        var consumedUnit = Unit();
        MusketeerIdentity.OnGunPickupEnd(consumedUnit.character, consumedState);
        MusketeerIdentity.OnGunPickupAbort(consumedState);
        Check(MusketeerIdentity.IsUnit(consumedUnit.archer), "a career consumed by a confirmed despawn still transfers to the returned archer");
        Check(MusketeerIdentity.IsGun(reused) && MusketeerIdentity.StockSlot(reused) == 1, "the reused root keeps its own career and rack slot");
        var scopeUnits = new List<Archer>(); MusketeerIdentity.CopyUnits(scopeUnits);
        var scopeGuns = new List<DroppableTool>(); MusketeerIdentity.CopyGuns(scopeGuns);
        Check(scopeUnits.Count == 3 && scopeGuns.Count == 1, "no career is duplicated onto the reused gun");
        // a failed native call fabricates no owner: the career stays an unbound reservation
        var failed = Gun(); Check(TryRegister(failed.tool, 2), "failed-call gun registered");
        MusketeerIdentity.OnGunPickupBegin(failed.tool, out var failedState);
        MusketeerIdentity.OnGunPickupEnd(null, failedState);
        MusketeerIdentity.OnGunPickupAbort(failedState);
        Check(!MusketeerIdentity.IsGun(failed.tool), "failed native call leaves the gun unbound");
        Check(MusketeerIdentity.DescribeForTests().Contains("reserved=1"), "the failed career stays a paid reservation");
    }

    // ---- save/load restoration, fail-closed snapshots ----

    static void SaveLoadTests()
    {
        Reset();
        var gun1 = Gun(); var gun2 = Gun();
        Check(TryRegister(gun1.tool) && TryRegister(gun2.tool), "two paid guns");
        var u1 = Unit();
        MusketeerIdentity.OnGunPickupBegin(gun1.tool, out var pickup);
        MusketeerIdentity.OnGunPickupEnd(u1.character, pickup);
        MusketeerIdentity.OnGunPickupAbort(pickup);
        // the island also carries ordinary native rows that no career claims
        Save(IslandJson("native-unit", "native-gun", "tree-1"), Row("native-unit", u1.go), Row("native-gun", gun2.go), Row("tree-1", null));
        var file = DiskResult();
        Check(file.Status == MusketeerArchiveStore.State.Valid, "snapshot written");
        string scope = file.Archive.Scopes.Keys.Single();
        var rows = file.Archive.Scopes[scope][0].Records;
        Check(rows.Count == 2 && rows.Any(x => x.NativeId == "native-unit" && x.Kind == MusketeerCareer.KindUnit) && rows.Any(x => x.NativeId == "native-gun" && x.Kind == MusketeerCareer.KindGun), "witnessed native ids by kind");
        Reset(true);
        var l1 = Unit(); var l2 = Gun();
        Load(IslandJson("native-unit", "native-gun", "tree-1"), Row("native-unit", l1.go), Row("native-gun", l2.go), Row("tree-1", null));
        Check(MusketeerIdentity.IsUnit(l1.archer) && MusketeerIdentity.IsGun(l2.tool), "paid gun and unit restored by exact native id");
        Check(MusketeerIdentity.DescribeForTests().Contains("bound=2"), "both careers bound");
        Check(!MusketeerIdentity.HasUnresolved && MusketeerIdentity.CanPurchase, "unrelated native rows do not disturb the load");
        var stranger = Unit();
        Check(!MusketeerIdentity.IsUnit(stranger.archer), "no ordinal assignment to unrelated archers");
        byte[] beforeUnknown = DiskBytes();
        var l3 = Unit(); var l4 = Gun();
        Load(IslandJson("renamed-unit", "native-gun"), Row("renamed-unit", l3.go), Row("native-gun", l4.go));
        Check(!MusketeerIdentity.IsUnit(l3.archer) && !MusketeerIdentity.IsGun(l4.tool), "changed native id never guesses an owner");
        Check(MusketeerIdentity.HasUnresolved && !MusketeerIdentity.CanPurchase, "unknown paid snapshot blocks new charges");
        Check(MusketeerIdentity.StatusText.Contains("暂停购买"), "status explains the blocked purchase");
        Check(DiskBytes().SequenceEqual(beforeUnknown), "unknown snapshot writes nothing");
        // 盘面继续前进（自动保存）也绝不自动再基线：musketeer 侧没有 B，unknown-paid 保持 unresolved
        Save(IslandJson("renamed-unit", "native-gun"), Row("renamed-unit", l3.go), Row("native-gun", l4.go));
        Check(DiskBytes().SequenceEqual(beforeUnknown), "a further save cannot rewrite the unknown-paid sidecar");
        Check(MusketeerIdentity.HasUnresolved && !MusketeerIdentity.CanPurchase, "unknown-paid state stays unresolved (no musketeer rebaseline)");
        Check(Logged("save-preserve-unresolved"), "the refuse-to-write path is logged | " + Dump());
        // an exact snapshot whose row object never materialized: nothing binds, nothing is
        // guessed, no baseline is inherited and charges stay blocked
        Reset(true);
        byte[] beforePending = DiskBytes();
        var pendingCapture = LoadBegin(IslandJson("native-unit", "native-gun", "tree-1"), "native-unit", "native-gun");
        var pendingUnit = Unit();
        Capture(pendingCapture, 0, pendingUnit.go);
        pendingCapture.End(true);
        Check(MusketeerIdentity.IsUnit(pendingUnit.archer), "present row bound");
        var pendingGuns = new List<DroppableTool>(); MusketeerIdentity.CopyGuns(pendingGuns);
        Check(pendingGuns.Count == 0, "an unmaterialized row is never re-assigned to another object");
        var strangerRow = Unit();
        Check(!MusketeerIdentity.IsUnit(strangerRow.archer), "no ordinal fallback for the unresolved row");
        Check(MusketeerIdentity.HasUnresolved && !MusketeerIdentity.CanPurchase, "unmaterialized row blocks charges");
        Check(MusketeerIdentity.DescribeForTests().Contains("baseline=False"), "unmaterialized row never inherits a baseline");
        Check(DiskBytes().SequenceEqual(beforePending), "unmaterialized row writes no baseline");
        // duplicate tracked rows conflict: nothing binds, charges are blocked
        Reset(true);
        byte[] beforeConflict = DiskBytes();
        var island = Island();
        island.Json = Raw(IslandJson("native-unit", "native-gun", "tree-1")); island.isNew = false; island.playTimeDays = 1;
        island.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "native-unit" }, new() { uniqueID = "native-unit" } };
        var conflictCapture = new MusketeerPersistence.LoadCapture();
        conflictCapture.Begin(island);
        Check(conflictCapture.Conflict, "duplicate tracked row id is a conflict");
        conflictCapture.End(true);
        Check(MusketeerIdentity.DescribeForTests().Contains("reserved=2"), "conflicting load binds nothing");
        Check(MusketeerIdentity.HasUnresolved && !MusketeerIdentity.CanPurchase, "conflicting load blocks charges");
        Check(DiskBytes().SequenceEqual(beforeConflict), "conflict load writes no baseline");
        // a witnessed row that restores as the wrong kind is a conflict too
        Reset(true);
        var wrongKind = Gun();
        var kindCapture = LoadBegin(IslandJson("native-unit", "native-gun", "tree-1"), "native-unit");
        Capture(kindCapture, 0, wrongKind.go);
        kindCapture.End(true);
        Check(!MusketeerIdentity.IsGun(wrongKind.tool) && !MusketeerIdentity.IsMarked(wrongKind.go), "kind mismatch never re-labels the object");
        Check(MusketeerIdentity.HasUnresolved && !MusketeerIdentity.CanPurchase, "kind mismatch blocks charges");
        // a failed load restores the old state and its bindings, after partial binding
        Reset();
        var oldGun = Gun(); Check(TryRegister(oldGun.tool), "old binding before a failed load");
        Save(IslandJson("row-gun-1"), Row("row-gun-1", oldGun.go));
        var failCapture = LoadBegin(IslandJson("row-gun-1"), "row-gun-1");
        var partialGun = Gun();
        Capture(failCapture, 0, partialGun.go);
        failCapture.End(false);
        Check(MusketeerIdentity.IsGun(oldGun.tool), "failed load keeps the old state and binding");
        Check(!MusketeerIdentity.IsGun(partialGun.tool), "partially bound objects from the failed load are dropped");
        // a live career whose expected row never reached GetID refuses the whole sidecar write
        Reset();
        var live = Gun(); Check(TryRegister(live.tool), "live gun before a missed capture");
        byte[] beforeRefusal = DiskBytes();
        SaveWithMissedRow(IslandJson("row-missed"), new GameObject());
        Check(DiskBytes().SequenceEqual(beforeRefusal), "missed expected marked row preserves the old baseline");
        Check(MusketeerIdentity.IsGun(live.tool), "refused save keeps the live identity");
        var live2 = Gun(); Check(TryRegister(live2.tool), "second live gun");
        Save(IslandJson("dup"), Row("dup", live.go), Row("dup", live2.go));
        Check(DiskBytes().SequenceEqual(beforeRefusal), "duplicate captured ids preserve the old baseline");
        // a temporarily hidden career is persisted as a reservation and re-witnessed later
        Reset();
        var hidden = Gun(); Check(TryRegister(hidden.tool), "hidden gun registered");
        hidden.go.activeInHierarchy = false;
        Check(!MusketeerIdentity.IsGun(hidden.tool), "inactive gun is not an active binding");
        Check(MusketeerIdentity.DescribeForTests().Contains("bound=1"), "hidden gun keeps its career bound in memory");
        // Native inactive objects are absent from saved rows. The later visible save
        // must have different native JSON, not conflicting metadata for one snapshot.
        SaveWithMissedRow(IslandJson(), new GameObject());
        var hiddenRows = RowsOf(Context1);
        Check(hiddenRows.Count == 1 && hiddenRows[0].NativeId == "" && hiddenRows[0].StockSlot == -1,
            "hidden career persisted as a reservation without a stale id or rack claim"
            + " rows=" + hiddenRows.Count + " " + MusketeerIdentity.DescribeForTests() + " | " + Dump());
        hidden.go.activeInHierarchy = true;
        Save(IslandJson("row-hidden"), Row("row-hidden", hidden.go));
        var witnessed = RowsOf(Context1);
        Check(witnessed.Count == 1 && witnessed[0].NativeId == "row-hidden", "reactivated career re-witnesses its native id");
        // a future sidecar appearing during play blocks new charges and is left untouched
        File.WriteAllText(ArchivePath, "{\"schemaVersion\":99,\"scopes\":[],\"contexts\":[]}");
        var blocked = Gun();
        Check(!TryRegister(blocked.tool), "future sidecar refuses registration");
        Check(MusketeerIdentity.DescribeForTests().Contains("readonly=True"), "future sidecar marks the island read-only");
        Check(File.ReadAllText(ArchivePath).Contains("99"), "unsupported sidecar left untouched");
    }

    // ---- paid rack metadata: 3 slots, transfer/drop clearing, exact physics restore ----

    static void StockTests()
    {
        Reset();
        var rack = new List<(GameObject go, DroppableTool tool)>();
        for (int i = 0; i < MusketeerIdentity.StockSlots; i++)
        {
            var gun = Gun();
            Check(TryRegister(gun.tool, i), "rack slot registered");
            Check(MusketeerIdentity.StockSlot(gun.tool) == i, "rack slot reported");
            rack.Add(gun);
        }
        var overflow = Gun();
        Check(!TryRegister(overflow.tool, 0), "an occupied rack slot refuses a second live gun");
        Check(!TryRegister(overflow.tool, MusketeerIdentity.StockSlots), "out of range rack slot refused");
        Save(IslandJson("rack-0", "rack-1", "rack-2"),
            Row("rack-0", rack[0].go), Row("rack-1", rack[1].go), Row("rack-2", rack[2].go));
        var saved = RowsOf(Context1).OrderBy(r => r.StockSlot).ToList();
        Check(saved.Count == 3 && saved[0].StockSlot == 0 && saved[1].StockSlot == 1 && saved[2].StockSlot == 2, "rack slots persisted with their witnessed rows");
        Reset(true);
        var back = new List<(GameObject go, DroppableTool tool)>();
        for (int i = 0; i < MusketeerIdentity.StockSlots; i++)
        {
            var gun = Gun();
            Body(gun.go, kinematic: false, vx: 3f + i, vy: 4f + i);
            back.Add(gun);
        }
        Load(IslandJson("rack-0", "rack-1", "rack-2"),
            Row("rack-0", back[0].go), Row("rack-1", back[1].go), Row("rack-2", back[2].go));
        for (int i = 0; i < MusketeerIdentity.StockSlots; i++)
        {
            Check(MusketeerIdentity.StockSlot(back[i].tool) == i, "rack slot restored by exact id");
            var body = back[i].go.GetComponent<Rigidbody2D>();
            Check(body != null && body.isKinematic && body.velocity.x == 0f && body.velocity.y == 0f, "restored rack gun becomes kinematic at rest");
        }
        Check(MusketeerIdentity.DescribeForTests().Contains("stock=3"), "three rack guns exposed");
        // a conflict that appears after a stock row already bound: the load is partial, so no
        // physics write happens and charges stay blocked
        Reset(true);
        var stocked = Gun(); var stockedBody = Body(stocked.go, kinematic: false, vx: 7f, vy: 8f);
        var wrongKindRow = Unit();
        var island = Island();
        island.Json = Raw(IslandJson("rack-0", "rack-1", "rack-2")); island.isNew = false; island.playTimeDays = 1;
        island.objects = new List<IslandSaveData.ObjectData> { new() { uniqueID = "rack-0" }, new() { uniqueID = "rack-1" }, new() { uniqueID = "rack-2" } };
        var conflict = new MusketeerPersistence.LoadCapture();
        conflict.Begin(island);
        Capture(conflict, 0, stocked.go);
        Capture(conflict, 1, wrongKindRow.go);
        conflict.End(true);
        Check(conflict.Conflict, "a wrong-kind tracked row is a conflict");
        Check(!stockedBody.isKinematic && stockedBody.velocity.x == 7f, "no rack physics write when the load is partial");
        Check(MusketeerIdentity.HasUnresolved && !MusketeerIdentity.CanPurchase, "partial load blocks charges");
        // promotion and drops clear the rack claim (fresh archive: a session with unproven paid
        // history intentionally blocks charges until a native load confirms)
        Reset();
        var unit = Unit();
        var promotedGun = Gun(); Check(TryRegister(promotedGun.tool, 1), "rack gun before promotion");
        MusketeerIdentity.OnGunPickupBegin(promotedGun.tool, out var pickup);
        MusketeerIdentity.OnGunPickupEnd(unit.character, pickup);
        MusketeerIdentity.OnGunPickupAbort(pickup);
        Check(MusketeerIdentity.IsUnit(unit.archer) && MusketeerIdentity.StockSlot(promotedGun.tool) == -1, "promotion clears the rack claim");
        var dropped = Gun();
        dropped.tool.dropper = unit.go;
        MusketeerIdentity.OnDroppableDrop(dropped.tool, dropped.tool.dropper);
        Check(MusketeerIdentity.IsGun(dropped.tool) && MusketeerIdentity.StockSlot(dropped.tool) == -1, "native drop produces an ordinary gun");
        // an explicit Drop of a tracked rack gun revokes the claim
        var knocked = Gun(); Check(TryRegister(knocked.tool, 2), "rack gun before a native drop");
        Check(MusketeerIdentity.StockSlot(knocked.tool) == 2, "rack claim present");
        MusketeerIdentity.OnDroppableDrop(knocked.tool);
        Check(MusketeerIdentity.StockSlot(knocked.tool) == -1 && MusketeerIdentity.IsGun(knocked.tool), "dropping a rack gun revokes only the rack claim");
        // a released slot frees the next registration
        var recycled = Gun();
        Check(TryRegister(recycled.tool, 2), "released rack slot accepts a new gun");
    }

    // ---- stock lifecycle: collected claims, save proof, pending restore retries ----

    static void StockLifecycleTests()
    {
        // a resident pickup still transfers the exact career although the native pickup flag is
        // already set before Character.Promote runs (identity must never require pickedUp=false)
        Reset();
        var picked = Gun(); Check(TryRegister(picked.tool, 0), "rack gun before a resident pickup");
        picked.tool.pickedUp = true;
        MusketeerIdentity.OnGunPickupBegin(picked.tool, out var pickup);
        var unit = Unit();
        MusketeerIdentity.OnGunPickupEnd(unit.character, pickup);
        MusketeerIdentity.OnGunPickupAbort(pickup);
        Check(MusketeerIdentity.IsUnit(unit.archer), "a pickedUp rack gun still transfers by exact career");
        Check(MusketeerIdentity.StockSlot(picked.tool) == -1, "promotion clears the rack claim");
        // reconciliation revokes a collected rack gun without touching the native item
        Reset();
        var collected = Gun(); Check(TryRegister(collected.tool, 1), "rack gun before collection");
        collected.tool.pickedUp = true;
        Time.unscaledTime += 10; Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.StockSlot(collected.tool) == -1, "a collected gun loses the rack claim at the next Tick");
        Check(MusketeerIdentity.IsGun(collected.tool) && collected.tool.pickedUp, "identity stays and the native pickup is not undone");
        // an enemy-carried gun loses the claim the same way
        Reset();
        var claimed = Gun(); Check(TryRegister(claimed.tool, 2), "rack gun before an enemy claim");
        claimed.tool.enemyClaimer = new GameObject();
        Time.unscaledTime += 10; Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.StockSlot(claimed.tool) == -1, "an enemy-carried gun loses the rack claim at the next Tick");
        Check(MusketeerIdentity.IsGun(claimed.tool), "the enemy claim never drops the paid identity");
        // an ordinary untouched rack gun keeps its claim through reconciliation and the save
        Reset();
        var kept = Gun(); Check(TryRegister(kept.tool, 0), "ordinary rack gun");
        Time.unscaledTime += 10; Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.StockSlot(kept.tool) == 0, "an unpicked rack gun keeps its claim");
        Save(IslandJson("kept-row"), Row("kept-row", kept.go));
        Check(RowsOf(Context1)[0].StockSlot == 0, "untouched rack claim is written at save");
        // the authoritative save refuses to write a claim that is no longer provable
        Reset();
        var taken = Gun(); Check(TryRegister(taken.tool, 1), "rack gun before a save-time revocation");
        taken.tool.pickedUp = true;
        Save(IslandJson("taken-row"), Row("taken-row", taken.go));
        Check(RowsOf(Context1)[0].StockSlot == -1, "a collected gun is written without a rack claim");
        // lost authority performs no rack mutation at all
        Reset();
        var online = Gun(); Check(TryRegister(online.tool, 2), "rack gun before an online window");
        Check(MusketeerIdentity.DescribeForTests().Contains("stock=1"), "claim present before the online window");
        online.tool.pickedUp = true;
        MusketeerAccess.TrackAllowed = false;
        Time.unscaledTime += 10; Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.DescribeForTests().Contains("stock=1"), "lost authority performs no rack mutation");
        MusketeerAccess.TrackAllowed = true;
        Time.unscaledTime += 10; Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.DescribeForTests().Contains("stock=0"), "authority restored revokes the collected claim");
        // a foreign layer is never mutated either
        Reset();
        var foreign = Gun(); Check(TryRegister(foreign.tool, 0), "rack gun before a foreign-layer window");
        foreign.tool.pickedUp = true;
        MusketeerAccess.InWorldResult = false;
        Time.unscaledTime += 10; Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.DescribeForTests().Contains("stock=1"), "a foreign layer is never mutated");
        MusketeerAccess.InWorldResult = true;
        Time.unscaledTime += 10; Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.DescribeForTests().Contains("stock=0"), "same-world reconciliation revokes the claim");
        // a rack restore without a body stays owned: charges blocked, retried, then unlocked
        Reset();
        var rack = Gun(); Check(TryRegister(rack.tool, 0), "rack gun before a body-less restore");
        Save(IslandJson("pending-row"), Row("pending-row", rack.go));
        Reset(true);
        var bodyless = Gun();
        bodyless.go.Add(new Transform { x = 4.5f, y = 1.25f });
        Load(IslandJson("pending-row"), Row("pending-row", bodyless.go));
        Check(MusketeerIdentity.StockSlot(bodyless.tool) == 0, "claim restored from the witnessed row");
        Check(!MusketeerIdentity.CanPurchase && MusketeerIdentity.HasUnresolved, "a failed rack restore blocks charges");
        Check(MusketeerIdentity.DescribeForTests().Contains("stockPending=1"), "failed restore stays tracked as pending");
        Body(bodyless.go);
        Time.unscaledTime += 10; Time.frameCount++; MusketeerIdentity.Tick();
        var restored = bodyless.go.GetComponent<Rigidbody2D>();
        Check(restored != null && restored.isKinematic && restored.velocity.x == 0f && restored.velocity.y == 0f, "the retry freezes the same proven stock gun");
        var marker = bodyless.go.GetComponent<Transform>();
        Check(marker != null && marker.x == 4.5f && marker.y == 1.25f, "rack restore never repositions the native object");
        Check(!MusketeerIdentity.HasUnresolved && MusketeerIdentity.CanPurchase, "a successful retry unlocks the shop");
        Check(MusketeerIdentity.DescribeForTests().Contains("stockPending=0"), "no pending responsibility is left behind");
        // a pending gun that gets consumed releases the block instead of locking the shop
        Reset();
        var consumed = Gun(); Check(TryRegister(consumed.tool, 1), "rack gun before a pending consumption");
        Save(IslandJson("consumed-row"), Row("consumed-row", consumed.go));
        Reset(true);
        var again = Gun();
        Load(IslandJson("consumed-row"), Row("consumed-row", again.go));
        Check(MusketeerIdentity.DescribeForTests().Contains("stockPending=1"), "body-less restore is pending again");
        MusketeerIdentity.OnPoolDespawnBegin(again.go, 0f, out var despawn);
        again.go.activeInHierarchy = false;
        MusketeerIdentity.OnPoolDespawnEnd(despawn);
        Time.unscaledTime += 10; Time.frameCount++; MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.HasUnresolved && MusketeerIdentity.CanPurchase, "a consumed pending gun releases the charge block");
        Check(MusketeerIdentity.DescribeForTests().Contains("stockPending=0"), "the pending entry leaves with its binding");
    }

    // ---- 2026-09-27 silent-lock repair: release clears the rack claim (H3'), the bounded
    // reconciliation gives an unrecoverable restore up instead of blocking forever (H1') ----

    static void ReleaseRackClaimTests()
    {
        // A released gun keeps no rack claim: a reservation that still carried a slot made every
        // later rack read fail closed for the whole session (empty shelf, silent lock). The paid
        // career survives as a reservation; only the claim goes.
        Reset();
        var dead = Gun(); Check(TryRegister(dead.tool, 1), "rack gun before a confirmed despawn");
        Check(MusketeerIdentity.StockSlot(dead.tool) == 1, "rack claim present before the release");
        MusketeerIdentity.OnPoolDespawnBegin(dead.go, 0f, out var despawn);
        dead.go.activeInHierarchy = false;
        MusketeerIdentity.OnPoolDespawnEnd(despawn);
        Check(MusketeerIdentity.ResidualStockClaims(MusketeerIdentity.Current) == 0, "confirmed despawn releases the rack claim (H3')");
        Check(MusketeerIdentity.DescribeForTests().Contains("reserved=1"), "the released career stays paid as a reservation");
        Check(MusketeerIdentity.CanPurchase, "a released rack gun cannot lock the purchase gate");
        // promote-lost and promote-not-archer are the other two release paths of a rack gun
        Reset();
        var lost = Gun(); Check(TryRegister(lost.tool, 2), "rack gun before a lost promotion");
        MusketeerIdentity.OnGunPickupBegin(lost.tool, out var lostState);
        MusketeerIdentity.OnGunPickupEnd(null, lostState);
        MusketeerIdentity.OnGunPickupAbort(lostState);
        Check(MusketeerIdentity.ResidualStockClaims(MusketeerIdentity.Current) == 0, "a lost promotion never leaves a rack claim");
        var nonArcher = Gun(); Check(TryRegister(nonArcher.tool, 2), "the released slot accepts the next rack gun");
        MusketeerIdentity.OnGunPickupBegin(nonArcher.tool, out var nonArcherState);
        var plain = new GameObject(); var plainCharacter = plain.Add(new Character());
        MusketeerIdentity.OnGunPickupEnd(plainCharacter, nonArcherState);
        MusketeerIdentity.OnGunPickupAbort(nonArcherState);
        Check(MusketeerIdentity.ResidualStockClaims(MusketeerIdentity.Current) == 0, "a non-archer promotion result never leaves a rack claim");
        // a release drops the career's pending physics restore immediately (no sweep needed)
        Reset();
        var rack = Gun(); Check(TryRegister(rack.tool, 0), "rack gun before a pending restore");
        Save(IslandJson("row-release-pending"), Row("row-release-pending", rack.go));
        Reset(true);
        var pending = Gun();
        pending.go.Add(new Transform { x = 0.75f, y = 0.25f });
        Load(IslandJson("row-release-pending"), Row("row-release-pending", pending.go));
        Check(MusketeerIdentity.DescribeForTests().Contains("stockPending=1"), "body-less load starts a pending restore");
        Check(!MusketeerIdentity.CanPurchase, "the pending restore blocks new charges");
        MusketeerIdentity.OnPoolDespawnBegin(pending.go, 0f, out var pendingEnd);
        pending.go.activeInHierarchy = false;
        MusketeerIdentity.OnPoolDespawnEnd(pendingEnd);
        Check(MusketeerIdentity.DescribeForTests().Contains("stockPending=0"), "the release drops the pending restore immediately");
        Check(MusketeerIdentity.CanPurchase, "the released pending gun ends the charge block");
    }

    static void StockRestoreAbandonTests()
    {
        // A restore that cannot succeed is retried by the 2s sweep, but after
        // StockRestoreAbandonAttempts tries the claim is given up (~60s): the shop sells again and
        // the paid career, the gun and its binding are never forgotten or despawned.
        Reset();
        var rack = Gun(); Check(TryRegister(rack.tool, 1), "rack gun before an unrecoverable restore");
        Save(IslandJson("row-abandon"), Row("row-abandon", rack.go));
        Reset(true);
        var bodyless = Gun();
        bodyless.go.Add(new Transform { x = 0.75f, y = 0.25f });
        Load(IslandJson("row-abandon"), Row("row-abandon", bodyless.go));
        Check(MusketeerIdentity.DescribeForTests().Contains("stockPending=1"), "body-missing restore starts pending");
        for (int i = 0; i < MusketeerIdentity.StockRestoreAbandonAttempts - 1; i++)
        { Time.unscaledTime += 10; Time.frameCount++; MusketeerIdentity.Tick(); }
        Check(MusketeerIdentity.DescribeForTests().Contains("stockPending=1"), "the retry is bounded, not given up early");
        Check(!MusketeerIdentity.CanPurchase, "a pending restore still blocks charges before the cap");
        Check(Logged("stock-physics-retry"), "the pre-cap retry log fires at attempt 8 | " + Dump());
        Time.unscaledTime += 10; Time.frameCount++; MusketeerIdentity.Tick();
        Check(MusketeerIdentity.DescribeForTests().Contains("stockPending=0"), "the capped attempt gives the claim up");
        Check(MusketeerIdentity.StockSlot(bodyless.tool) == -1, "the abandoned claim is revoked");
        Check(MusketeerIdentity.ResidualStockClaims(MusketeerIdentity.Current) == 0, "abandonment leaves no residual slot");
        Check(MusketeerIdentity.IsGun(bodyless.tool), "the paid gun survives abandonment (never forgotten or despawned)");
        Check(MusketeerIdentity.DescribeForTests().Contains("bound=1"), "the abandoned gun keeps its live binding");
        Check(MusketeerIdentity.CanPurchase && !MusketeerIdentity.HasUnresolved, "the purchase gate recovers after abandonment");
        Check(!MusketeerIdentity.StatusText.Contains("暂停购买"), "the status line stops reporting a paused purchase");
        Check(Logged("stock-restore-abandoned-body-missing"), "the give-up reason is logged in its bucket | " + Dump());
        // the abandoned slot accepts the next paid gun
        var replacement = Gun(); Check(TryRegister(replacement.tool, 1), "the abandoned rack slot is reusable");
        var guns = new List<DroppableTool>(); MusketeerIdentity.CopyGuns(guns);
        Check(guns.Count == 2, "both paid guns stay tracked after the give-up");
    }

    static void StockRestoreNotInWorldAbandonTests()
    {
        // A gun that never reaches this runtime world again must not block the shop forever; the
        // give-up names the world bucket and still never touches the paid identity.
        Reset();
        var rack = Gun(); Check(TryRegister(rack.tool, 1), "rack gun before a foreign-world restore");
        Save(IslandJson("row-notinworld"), Row("row-notinworld", rack.go));
        Reset(true);
        var stray = Gun();
        stray.go.Add(new Transform { x = 0.75f, y = 0.25f });
        Load(IslandJson("row-notinworld"), Row("row-notinworld", stray.go));
        Check(MusketeerIdentity.DescribeForTests().Contains("stockPending=1"), "body-less restore starts pending");
        Body(stray.go);                        // the body exists now; only the world gate can fail
        MusketeerAccess.InWorldResult = false; // the gun never comes back into this runtime world
        for (int i = 0; i < MusketeerIdentity.StockRestoreAbandonAttempts; i++)
        { Time.unscaledTime += 10; Time.frameCount++; MusketeerIdentity.Tick(); }
        Check(MusketeerIdentity.DescribeForTests().Contains("stockPending=0"), "a restore that never reaches the world is given up");
        Check(MusketeerIdentity.StockSlot(stray.tool) == -1, "the not-in-world claim is revoked");
        Check(Logged("stock-restore-abandoned-not-in-world"), "the give-up names the world bucket | " + Dump());
        MusketeerAccess.InWorldResult = true;
        Check(MusketeerIdentity.IsGun(stray.tool), "the paid gun and its binding survive the give-up");
        Check(stray.go.activeInHierarchy, "the gun was never despawned by the give-up");
        Check(MusketeerIdentity.DescribeForTests().Contains("bound=1") && MusketeerIdentity.DescribeForTests().Contains("stock=0"),
            "identity intact, claim gone");
    }

    // ---- pool life boundaries and pointer reuse ----

    static void PoolLifeTests()
    {
        Reset();
        var gun = Gun(); Check(TryRegister(gun.tool), "gun registered");
        Check(MusketeerIdentity.IsGun(gun.tool), "gun active");
        MusketeerIdentity.OnPoolDespawnBegin(gun.go, 1f, out var delayed);
        Check(!delayed.Tracked, "delayed despawn only schedules");
        MusketeerIdentity.OnPoolDespawnBegin(gun.go, 0f, out var refused);
        Check(refused.Tracked, "immediate despawn captured");
        MusketeerIdentity.OnPoolDespawnEnd(refused);
        Check(MusketeerIdentity.IsGun(gun.tool), "native guard refusal preserves the identity");
        MusketeerIdentity.OnPoolDespawnBegin(gun.go, 0f, out var confirmed);
        gun.go.activeInHierarchy = false;
        MusketeerIdentity.OnPoolDespawnEnd(confirmed);
        Check(!MusketeerIdentity.IsGun(gun.tool), "confirmed despawn releases the live reference");
        Check(MusketeerIdentity.DescribeForTests().Contains("reserved=1"), "released career stays paid and reserved");
        // pooled pointer reuse starts a new life and can never inherit the released career
        gun.go.activeInHierarchy = true;
        MusketeerIdentity.OnPoolSpawn(gun.go);
        var reused = gun.go.GetComponent<DroppableTool>();
        Check(TryRegister(reused), "new purchase on the reused pool instance");
        var gunList = new List<DroppableTool>(); MusketeerIdentity.CopyGuns(gunList);
        Check(gunList.Count == 1 && ReferenceEquals(gunList[0], reused), "exactly one gun career on the reused instance");
        // an address reused by a different GameObject instance loses validation and is swept
        var promoted = Unit();
        var gun2 = Gun(); Check(TryRegister(gun2.tool), "gun before transfer");
        MusketeerIdentity.OnGunPickupBegin(gun2.tool, out var state); MusketeerIdentity.OnGunPickupEnd(promoted.character, state);
        MusketeerIdentity.OnGunPickupAbort(state);
        Check(MusketeerIdentity.IsUnit(promoted.archer), "career bound to the unit");
        promoted.go.InstanceId += 100000;
        Time.unscaledTime += 10; Time.frameCount++;
        MusketeerIdentity.Tick();
        Check(!MusketeerIdentity.IsUnit(promoted.archer), "recycled address never inherits identity");
        Check(MusketeerIdentity.DescribeForTests().Contains("reserved=2"), "released careers stay reserved");
        Time.unscaledTime += 10; Time.frameCount++;
        MusketeerIdentity.Tick();
        Check(RootCount() <= 3, "dead slots are pruned without unbounded growth");
    }

    // ---- capacity: paid references are never evicted ----

    static void CapacityTests()
    {
        Reset();
        GameObject firstRoot = null;
        for (int i = 0; i < MusketeerArchive.MaxRecords; i++)
        {
            var gun = Gun();
            if (i == 0) firstRoot = gun.go;
            if (!TryRegister(gun.tool)) throw new Exception("registration failed at " + i);
        }
        Check(MusketeerIdentity.DescribeForTests().Contains("records=" + MusketeerArchive.MaxRecords), "capacity reached exactly");
        byte[] before = DiskBytes();
        var overflow = Gun();
        Check(!TryRegister(overflow.tool), "capacity overflow refused instead of losing a paid reference");
        Check(MusketeerIdentity.DescribeForTests().Contains("records=" + MusketeerArchive.MaxRecords), "no paid reference evicted at capacity");
        Check(MusketeerIdentity.IsGun(firstRoot.GetComponent<DroppableTool>()), "the first paid career is still tracked");
        Check(DiskBytes().SequenceEqual(before), "capacity failure writes nothing");
    }
}
