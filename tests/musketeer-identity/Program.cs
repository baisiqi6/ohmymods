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
            ArchiveTests();
            StoreTests();
            StoreReliabilityTests();
            ContextTests();
            TransferTests();
            DropBoundaryTests();
            PromotionScopeTests();
            SaveLoadTests();
            StockTests();
            StockLifecycleTests();
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
        SetStatic("_current", null); SetStatic("_contextKey", null);
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
        GlobalSaveData.loaded = new GlobalSaveData { currentCampaign = 1, currentChallenge = 0 };
        CampaignSaveData.current = new CampaignSaveData { CurrentIsland = new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = Raw("current") } };
        MusketeerAccess.TrackAllowed = true; MusketeerAccess.Enabled = true; MusketeerAccess.Playing = true; MusketeerAccess.InWorldResult = true;
        Time.frameCount++; Time.time += 10; Time.unscaledTime += 10;
        MusketeerIdentity.InvalidateContextCache();
        MusketeerIdentity.Tick();
        if (!keepFile) Load("before-purchase");
    }

    static IslandSaveData Island() => CampaignSaveData.current.CurrentIsland;

    static MusketeerPersistence.SaveCapture CreateSave()
    {
        var island = Island();
        return new MusketeerPersistence.SaveCapture { Campaign = GlobalSaveData.loaded.currentCampaign, Land = island.land, Challenge = GlobalSaveData.loaded.currentChallenge };
    }

    // go == null models a native row that never reached GetID (ordinary object, hidden object or
    // a missed capture): it is present in the island rows but carries no career.
    static void Save(string json, params (string id, GameObject go)[] rows)
    {
        var island = Island();
        island.Json = Raw(json); island.isNew = false; island.playTimeDays = 1;
        island.objects = rows.Select(x => new IslandSaveData.ObjectData { uniqueID = x.id }).ToList();
        IslandSaveData.CurrentlySavingIsland = island; IslandSaveData.isSavingGame = true;
        var capture = CreateSave();
        foreach (var row in rows) if (row.go != null) capture.Capture(Persist(row.go), row.id);
        capture.Apply();
        IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
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
    {
        var capture = LoadBegin(json, rows.Select(x => x.id).ToArray());
        for (int i = 0; i < rows.Length; i++) if (rows[i].go != null) Capture(capture, i, rows[i].go);
        capture.End(true);
    }

    static void Generate(int land, string json)
    {
        CampaignSaveData.current.CurrentIsland = new IslandSaveData { land = land, isNew = true, playTimeDays = 0, Json = Raw(json) };
        var virgin = new MusketeerPersistence.VirginCapture();
        virgin.Begin(CampaignSaveData.current);
        virgin.Complete(CampaignSaveData.current);
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
        MusketeerArchive.Decode(System.Text.Encoding.UTF8.GetBytes(text.Replace("\"schemaVersion\":2", "\"schemaVersion\":3")), out bool futureVersion);
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
        using (var lockedBackup = new FileStream(path + ".bak", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Check(!MusketeerArchiveStore.Save(path, expected, fourth), "save fails while the backup stays locked");
        }
        Check(Warned("write-retry=1 failed"), "the failed retry is logged once | " + Dump());
        Check(beforeFailure.SequenceEqual(File.ReadAllBytes(path)), "a failed retry keeps the previous file");
        Check(!Directory.GetFiles(Path.GetDirectoryName(path)).Any(f => f.Contains(".tmp-")), "no temp file is left behind");
        Check(MusketeerArchiveStore.Save(path, expected, fourth), "the same save succeeds once the lock is gone");

        // 瞬态失败：重试窗口内锁释放 → 单次重试内恢复
        var expected2 = MusketeerArchiveStore.Load(path);
        Check(expected2.Writable, "writable expected state (second)");
        var fifth = expected2.Archive;
        Check(fifth.Record(H("scope"), H("snapshot-5"), new[] { new MusketeerCareer { Id = Guid.NewGuid(), Kind = MusketeerCareer.KindGun } }), "fifth snapshot staged");
        FileStream releaseLock = new FileStream(path + ".bak", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var releaser = new System.Threading.Thread(() => { System.Threading.Thread.Sleep(80); releaseLock.Dispose(); }) { IsBackground = true };
            releaser.Start();
            Check(MusketeerArchiveStore.Save(path, expected2, fifth), "one retry heals a transient lock");
            releaser.Join();
        }
        finally
        {
            releaseLock.Dispose();
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
        var first = Gun();
        Check(TryRegister(first.tool), "register gun on the first island");
        Save(IslandJson("row-gun-1"), Row("row-gun-1", first.go));
        CampaignSaveData.current.CurrentIsland = new IslandSaveData { land = 2, isNew = false, playTimeDays = 1, Json = Raw("land2") };
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
        CampaignSaveData.current.CurrentIsland = new IslandSaveData { land = 1, isNew = false, playTimeDays = 1, Json = Raw("campaign2") };
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
        Check(hiddenRows.Count == 1 && hiddenRows[0].NativeId == "" && hiddenRows[0].StockSlot == -1, "hidden career persisted as a reservation without a stale id or rack claim");
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
