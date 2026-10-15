// Production-linked producer-to-writer regression. Native/Unity calls are doubled;
// this does not prove Harmony injection, native ABI or the real filesystem writer.
using System;
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

    static string IslandJson(string gun, string unit)
    {
        var rows = new[]
        {
            gun == null ? null : Row(gun, "Prefabs/Objects/ToolBow"),
            unit == null ? null : Row(unit, "Prefabs/Characters/Archer")
        }.Where(x => x != null);
        return "{\"land\":0,\"objects\":[" + string.Join(",", rows) + "],\"playTimeDays\":1}";
    }

    static string Row(string id, string path) => JsonSerializer.Serialize(new { uniqueID = id, prefabPath = path });

    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL " + label);
        assertions++;
        Console.WriteLine("PASS " + label);
    }

    static void SetStatic(string name, object value)
        => typeof(MusketeerIdentity).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value);

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
        SetStatic("_lastTickFrame", -1);
        SetStatic("_contextFrame", -1);
    }

    static byte[] Payload()
    {
        string raw = GlobalSaveData.loaded.prefs.contents[MusketeerNativeArchive.Key];
        using var island = JsonDocument.Parse(Island.Json);
        string json = JsonSerializer.Serialize(new
        {
            campaigns = new[] { new { challengeId = 0, _islands = new[] { island.RootElement } } },
            challenges = Array.Empty<object>(),
            prefs = new { srzEntries = new[] { new { key = MusketeerNativeArchive.Key, val = raw } } }
        });
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
            gzip.Write(Encoding.UTF8.GetBytes(json));
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

    static MusketeerPersistence.SaveCapture Capture(string gunId, GameObject gun, string unitId, GameObject unit, bool missUnit = false)
    {
        Island.isNew = false;
        Island.playTimeDays = 1;
        Island.Json = IslandJson(gunId, unitId);
        Island.objects = new() { new() { uniqueID = gunId }, new() { uniqueID = unitId } };
        IslandSaveData.CurrentlySavingIsland = Island;
        IslandSaveData.isSavingGame = true;
        var capture = new MusketeerPersistence.SaveCapture { Campaign = 0, Challenge = 0, Land = 0 };
        capture.Begin();
        capture.Capture(gun.Add(new Persistent()), gunId);
        if (!missUnit) capture.Capture(unit.Add(new Persistent()), unitId);
        capture.Apply();
        typeof(MusketeerPersistence.SavePatch).GetMethod("Finally", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { null, capture });
        IslandSaveData.CurrentlySavingIsland = null;
        IslandSaveData.isSavingGame = false;
        return capture;
    }

    static void Main()
    {
        Directory.CreateDirectory(Root);
        BepInEx.Paths.ConfigPath = Path.Combine(Root, "config");
        Filer.folder = Path.Combine(Root, "Release");
        Directory.CreateDirectory(Filer.folder);
        Directory.CreateDirectory(Path.GetDirectoryName(MusketeerPersistence.ArchivePath));
        GlobalSaveData.loaded = new();
        GlobalSaveData.filename = "global-v35";
        var campaign = new CampaignSaveData
        {
            CurrentIsland = new IslandSaveData { land = 0, isNew = true, playTimeDays = 0, Json = IslandJson(null, null) }
        };
        CampaignSaveData.current = campaign;
        GlobalSaveData.loaded.campaigns.Add(campaign);
        Managers.Inst = new Managers { world = new World { gameLayer = new GameObject().Add(new Transform()) } };

        var virgin = new MusketeerPersistence.VirginCapture();
        virgin.Begin(campaign);
        virgin.Complete(campaign);
        Time.frameCount++;
        MusketeerIdentity.Tick();
        Check(MusketeerIdentity.CanPurchase, "virgin native Stage initializes paid baseline");
        Accept(Payload());

        var gun = new GameObject { tag = "Bow" };
        Check(MusketeerIdentity.TryRegisterPaidGun(gun.Add(new DroppableTool())), "register first paid gun");
        var promotedGun = new GameObject { tag = "Bow" };
        var promotedTool = promotedGun.Add(new DroppableTool());
        Check(MusketeerIdentity.TryRegisterPaidGun(promotedTool), "register second gun for unit");
        MusketeerIdentity.OnGunPickupBegin(promotedTool, out var promotion);
        var unit = new GameObject();
        var actor = unit.Add(new Character());
        CheckPromotion(unit, actor, promotion);
        var ids = MusketeerIdentity.Current.Careers.Select(x => x.Id).OrderBy(x => x).ToArray();

        Check(Capture("gun-H1", gun, "unit-H1", unit).Applied, "actual producer H1 Stage succeeds");
        byte[] h1 = Payload();
        Accept(h1);
        Check(Capture("gun-H2", gun, "unit-H2", unit).Applied, "actual producer H2 Stage succeeds");
        byte[] h2 = Payload();
        Accept(h2);
        Check(!h1.AsSpan().SequenceEqual(h2), "second save has distinct native bytes");

        Check(!Capture("gun-fault", gun, "unit-fault", unit, true).Applied, "missed active unit capture fails before Stage");
        var failure = MusketeerNativeSave.GatePayload(Filer.folder, GlobalSaveData.filename, Payload(), false, out _, out string why);
        Check(failure == MusketeerNativeSave.WriterVerdict.Fail && why == MusketeerNativeSave.ReasonCaptureFault,
            "actual SavePatch finalizer blocks stale outgoing payload: " + why);
        Check(Capture("gun-H3", gun, "unit-H3", unit).Applied, "next correct producer capture clears fault");
        byte[] h3 = Payload();
        Accept(h3);

        Check(MusketeerNativeArchive.TryExtract(h3, "global-v35", out string raw, out _, out _, out _),
            "extract actually accepted native raw for cold load");
        ClearIdentity();
        MusketeerNativeSave.ResetForTests();
        GlobalSaveData.loaded = new();
        GlobalSaveData.loaded.prefs.contents[MusketeerNativeArchive.Key] = raw;
        GlobalSaveData.loaded.campaigns.Add(campaign);
        Managers.Inst.world.gameLayer = new GameObject().Add(new Transform());
        var coldGun = new GameObject { tag = "Bow" };
        var coldTool = coldGun.Add(new DroppableTool());
        var coldUnit = new GameObject();
        coldUnit.Add(new Character());
        var coldArcher = coldUnit.Add(new Archer());
        var load = new MusketeerPersistence.LoadCapture();
        load.Begin(Island);
        load.Capture(Island.objects[0], coldGun.Add(new Persistent()));
        load.Capture(Island.objects[1], coldUnit.Add(new Persistent()));
        load.End(true);
        Time.frameCount++;
        MusketeerIdentity.Tick();
        Check(MusketeerIdentity.IsGun(coldTool) && MusketeerIdentity.IsUnit(coldArcher), "cold actual producer load restores exact gun + unit identities");
        Check(MusketeerIdentity.Current.Careers.Select(x => x.Id).OrderBy(x => x).SequenceEqual(ids), "cold load preserves exact paid GUID set");
        Check(MusketeerIdentity.CanPurchase, "cold exact baseline remains purchase-ready");
        MusketeerNativeSave.ResetForTests();
        Il2CppSystem.Threading.Tasks.Task.ForceFailure = true;
        MusketeerPersistence.ReadArchive(); // binds this owner and attempts real adapter prewarming
        Check(!MusketeerIdentity.CanPurchase, "actual adapter endpoint failure blocks purchases before charging");
        Check(!MusketeerIdentity.TryRegisterPaidGun(new GameObject { tag = "Bow" }.Add(new DroppableTool())),
            "actual adapter endpoint failure cannot create a new paid career");
        Check(MusketeerIdentity.IsGun(coldTool) && MusketeerIdentity.IsUnit(coldArcher),
            "unavailable rejection endpoint preserves existing paid identities");
        Console.WriteLine("ALL PASS producer integration: " + assertions + " assertions");
    }

    static void CheckPromotion(GameObject unit, Character actor, MusketeerIdentity.PromotionState promotion)
    {
        var archer = unit.Add(new Archer());
        MusketeerIdentity.OnGunPickupEnd(actor, promotion);
        MusketeerIdentity.OnGunPickupAbort(promotion);
        Check(MusketeerIdentity.IsUnit(archer), "actual career promotion preserves role");
    }
}
