// Offline E2E reproduction of the real purchase chain:
//   HeroShop.OnPay -> HeroRecruitment.TryPurchase -> HeroArcherRuntime.TryActivatePurchased
// All three classes are the real production sources compiled from the current source tree;
// only engine/game/effect-slice boundaries are test-side stand-ins (see REPRO-RESULTS.md).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using KingdomEnhancedMod;
using UnityEngine;

internal sealed class CheckResult
{
    internal string Id;
    internal bool Ok;
    internal string Detail;
}

internal sealed class Actor
{
    internal GameObject Go;
    internal Archer Archer;
    internal Character Character;
    internal Damageable Damage;
    internal Persistent Persistent;
    internal Embarkee Embarkee;
    internal Knight Knight;
}

internal sealed class NativePaymentResult
{
    internal bool Gate;
    internal int FloatingAtGate;
    internal bool Consumed;
    internal bool Cancelled;
}

internal sealed class Host
{
    internal GameObject Layer;
    internal GameObject NativeShopGo;
    internal Player Player;
    internal Kingdom Kingdom;
    internal IslandSaveData Island;
    internal CampaignSaveData Campaign;

    internal static Host Create(int land, string json, bool isNew, double days, Host sharePlayer = null)
    {
        Managers.Inst = new Managers
        {
            game = new Game { state = Game.State.Playing },
            world = new World(),
            payables = new PayableManager(),
            kingdom = new Kingdom { HasBorderLoaded = true },
        };
        var host = new Host();
        host.Layer = new GameObject("GameLayer");
        Managers.Inst.world.gameLayer = host.Layer.transform;
        host.Kingdom = Managers.Inst.kingdom;

        // Ground reference: one active standard native shop inside the world layer.
        host.NativeShopGo = new GameObject("NativeBowShop");
        host.NativeShopGo.transform.SetParent(host.Layer.transform, false);
        var body = host.NativeShopGo.AddComponent<SpriteRenderer>();
        body.sprite = new Sprite { pivot = new Vector2(0.5f, 0f), rect = new Rect(0, 0, 16, 16), pixelsPerUnit = 32f };
        var shop = host.NativeShopGo.AddComponent<PayableShop>();
        var tag = host.NativeShopGo.AddComponent<ShopTag>();
        tag.type = PayableShop.ShopType.Bow;
        Managers.Inst.payables.Items.Add(shop);

        // Local player with 20 coins. A cross-land reload keeps the same player object
        // (the wallet survives island loads in the real game).
        if (sharePlayer != null)
        {
            host.Player = sharePlayer.Player;
        }
        else
        {
            var playerGo = new GameObject("Player");
            playerGo.transform.SetParent(host.Layer.transform, false);
            host.Player = playerGo.AddComponent<Player>();
            host.Player.wallet = playerGo.AddComponent<Wallet>();
            host.Player.wallet.Coins = 20;
        }
        host.Kingdom.playerOne = host.Player;

        GlobalSaveData.loaded = new GlobalSaveData { currentCampaign = 1, currentChallenge = 0 };

        GlobalSaveData.filename = "global-v35";
        host.Campaign = new CampaignSaveData();
        CampaignSaveData.current = host.Campaign;
        GlobalSaveData.loaded.campaigns.Add(new CampaignSaveData());
        GlobalSaveData.loaded.campaigns.Add(host.Campaign);
        GlobalSaveData.loaded.challenges.Add(new CampaignSaveData());
        host.Island = new IslandSaveData { land = land, isNew = isNew, playTimeDays = days, Json = json };
        host.Campaign.CurrentIsland = host.Island;
        PlaceIsland(host.Campaign, host.Island, land);

        NetworkPostbox.Instance = new NetworkPostbox();
        BiomeHolder.Inst = new BiomeHolder { BiomeIndex = 8 };
        ModConfig.Enabled.Value = true;
        ModConfig.HeroArcherEnabled.Value = true;
        NetworkBigBoss.HasWorldAuth = true;
        NetworkBigBoss.IsOnline = false;
        BepInEx.Paths.ConfigPath = Path.Combine(Program.OutRoot, "repro-config");
        Directory.CreateDirectory(BepInEx.Paths.ConfigPath);

        Time.time = 100f;
        Time.unscaledTime = 100f;
        Time.deltaTime = 0.02f;
        Time.frameCount = 10;

        HeroArcherVisuals.VisualProducible = true;
        HeroArcherVisuals.Clear();
        EmbarkableSim.Reset();
        KingdomEnhancedPlugin.Instance.LogSource.Clear();
        return host;
    }

    internal static void Mount(Host host)
    {
        Managers.Inst.world.gameLayer = host.Layer.transform;
        Managers.Inst.kingdom = host.Kingdom;
        CampaignSaveData.current = host.Campaign;
        GlobalSaveData.loaded.campaigns.Add(new CampaignSaveData());
        GlobalSaveData.loaded.campaigns.Add(host.Campaign);
        GlobalSaveData.loaded.challenges.Add(new CampaignSaveData());
        host.Campaign.CurrentIsland = host.Island;
        PlaceIsland(host.Campaign, host.Island, host.Island.land);
        GlobalSaveData.loaded.currentCampaign = 1;
        GlobalSaveData.loaded.currentChallenge = 0;
    }

    // issue #153: an island takes its positional slot in the campaign table exactly like real
    // 2.4 — a populated slot i carries land == i, and the padding slots are never-visited
    // placeholders that keep land = 0.
    internal static void PlaceIsland(CampaignSaveData campaign, IslandSaveData island, int slot)
    {
        while (campaign._islands.Count <= slot)
            campaign._islands.Add(new IslandSaveData { land = 0, isNew = false, playTimeDays = 0 });
        campaign._islands[slot] = island;
    }

    internal Actor CreateArcher(string name, Side side, bool embarkee = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(Layer.transform, false);
        var actor = new Actor { Go = go };
        actor.Archer = go.AddComponent<Archer>();
        actor.Character = go.AddComponent<Character>();
        actor.Damage = go.AddComponent<Damageable>();
        actor.Persistent = go.AddComponent<Persistent>();
        actor.Archer.side = side;
        actor.Archer._character = actor.Character;
        actor.Archer._damageable = actor.Damage;
        actor.Character._damageable = actor.Damage;
        if (embarkee)
        {
            actor.Embarkee = go.AddComponent<Embarkee>();
            actor.Archer._embarkee = actor.Embarkee;
            actor.Embarkee._owner = actor.Archer;
            EmbarkableSim.Register(actor.Embarkee);   // real units register when they are enabled
        }
        return actor;
    }

    internal Actor CreateWorkerSuccessor(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(Layer.transform, false);
        var actor = new Actor { Go = go };
        actor.Character = go.AddComponent<Character>();
        actor.Damage = go.AddComponent<Damageable>();
        actor.Persistent = go.AddComponent<Persistent>();
        actor.Embarkee = go.AddComponent<Embarkee>();
        actor.Embarkee._owner = go.AddComponent<WorkerOwner>();
        actor.Character._damageable = actor.Damage;
        EmbarkableSim.Register(actor.Embarkee);
        return actor;
    }

    /// <summary>A non-purchased local defender on the native Knight embark path (no Archer).</summary>
    internal Actor CreateKnight(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(Layer.transform, false);
        var actor = new Actor { Go = go };
        actor.Knight = go.AddComponent<Knight>();
        actor.Character = go.AddComponent<Character>();
        actor.Damage = go.AddComponent<Damageable>();
        actor.Persistent = go.AddComponent<Persistent>();
        actor.Embarkee = go.AddComponent<Embarkee>();
        actor.Character._damageable = actor.Damage;
        EmbarkableSim.Register(actor.Embarkee);
        return actor;
    }

    internal static void AdvanceFrame()
    {
        Time.frameCount++;
        Time.time += 0.1f;
        Time.unscaledTime += 0.1f;
    }

    /// <summary>A new island session: advance the clock past the shop recreate backoff.</summary>
    internal static void JumpClock(float time, int frame)
    {
        Time.time = time;
        Time.unscaledTime = time;
        Time.frameCount = frame;
    }
}

internal static partial class Program
{
    internal static string OutRoot;

    private const string VirginJsonA = "{\"land\":1,\"islandTime\":10}";
    private const string SavedJsonA = "{\"land\":1,\"islandTime\":11,\"hero\":\"npc-hero-A\"}";
    private const string VirginJsonB = "{\"land\":2,\"islandTime\":20,\"hero\":null}";
    private const string SavedJsonB = "{\"land\":2,\"islandTime\":21,\"hero\":\"npc-hero-B\"}";
    private const string DestJson = "{\"land\":7,\"islandTime\":30}";
    private const string OldHistoryJson = "{\"land\":9,\"islandTime\":1}";

    private static readonly List<CheckResult> Checks = new List<CheckResult>();
    private static string _scenario;

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: HeroPurchaseE2E <core|lands|carry> <outDir>");
            return 2;
        }
        _scenario = args[0];
        OutRoot = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(OutRoot);
        // Sidecar root must exist before any scenario (re)computes HeroRecruitment.ArchivePath.
        BepInEx.Paths.ConfigPath = Path.Combine(OutRoot, "repro-config");
        Directory.CreateDirectory(BepInEx.Paths.ConfigPath);
        try
        {
            if (_scenario.StartsWith("astra-")) AstraProbe();
            else if (_scenario.StartsWith("r1-")) R1Probe();
            else if (_scenario.StartsWith("r2-")) R2Probe();
            else if (_scenario.StartsWith("adj-")) Adjacent();
            else if (_scenario.StartsWith("r3-")) R3Probe();
            else if (_scenario.StartsWith("omp-")) RecoveryProbe();
            else if (_scenario == "purchase-isolation") RunNativeIsolation();
            else if (_scenario.StartsWith("purchase-")) PurchaseProbe();
            else if (_scenario == "core") RunCore();
            else if (_scenario == "lands") RunLands();
            else if (_scenario == "carry") RunCarry();
            else if (_scenario == "board") RunBoard();
            else if (_scenario.StartsWith("review-")) ReviewerProbe();
            else { Console.Error.WriteLine("unknown scenario: " + _scenario); return 2; }
        }
        catch (Exception e)
        {
            Check(false, "scenario-exception", e.ToString());
        }

        int failed = 0;
        foreach (var check in Checks) if (!check.Ok) failed++;
        Console.WriteLine("RESULT scenario=" + _scenario + " total=" + Checks.Count
            + " passed=" + (Checks.Count - failed) + " failed=" + failed);
        WriteResultJson(failed);
        return failed == 0 ? 0 : 1;
    }

    private static void Check(bool ok, string id, string detail = "")
    {
        Checks.Add(new CheckResult { Id = id, Ok = ok, Detail = detail });
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + id + (string.IsNullOrEmpty(detail) ? "" : "  | " + detail));
    }

    // ---------------- scenario: core ----------------


    private static void PurchaseProbe()
    {
        ResetArchive();
        var host = Host.Create(1, VirginJsonA, isNew: true, days: 0);
        BootVirginIsland(host);
        var payable = BootShop(2000f, 4000);
        var actor = host.CreateArcher("Paid", Side.Right);
        HeroArcherRuntime.Observe(actor.Archer);
        Check(payable != null && HeroShop.CanPurchase(), "setup-purchasable", HeroRecruitment.DescribeForTests() + " recruitable=" + HeroArcherRuntime.IsRecruitable(actor.Archer) + " payable=" + (payable != null));
        if (_scenario == "purchase-mismatch")
        {
            var player = host.Player;
            player.selectedPayable = payable; player._payState = Player.PayState.Transaction;
            payable.InvokeTransactionStarted(player);
            player.wallet.Coins -= 8;
            for(int i=0;i<8;i++) player._floatingCurrency.Add(new Currency());
            player._payState = Player.PayState.Completed; player._completingPayable = payable;
            Check(payable.Owner.CanPay(player), "final-gate-passed");
            // Inject an unsupported identity discontinuity between native final gate and callback.
            // This is conditional reachability, not proof native engine performs this mutation.
            var f = typeof(HeroShop).GetField("_payable", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            f.SetValue(null, null);
            payable.Owner.OnPay(player);
            player.ConsumedFloating += player._floatingCurrency.Count; player._floatingCurrency.Clear();
            Check(player.wallet.Coins == 12 && player.ConsumedFloating == 0 && player.DroppedFloating == 8,
                "mismatch-native-floating-returned-once");
            Check(!HeroRecruitment.IsPurchased(actor.Archer) && !LogContains("purchase completed")
                && player.wallet.AddCalls == 0, "mismatch-no-hero-no-wallet-mint");
            return;
        }
        if (_scenario == "purchase-callback-clear")
        {
            var player=host.Player;
            player.selectedPayable=payable; player._payState=Player.PayState.Transaction;
            payable.InvokeTransactionStarted(player);
            player.wallet.Coins-=8;
            for(int i=0;i<8;i++) player._floatingCurrency.Add(new Currency());
            player._payState=Player.PayState.Completed; player._completingPayable=payable;
            Check(payable.Owner.CanPay(player), "clear-old-ticket-final-gate");
            HeroShop.Clear("test-replace");
            int dropped=player.DroppedFloating, refunds=player.wallet.AddCalls;
            payable.Owner.OnPay(player);
            payable.Owner.OnPay(player);
            Check(player.wallet.Coins==12 && dropped==8 && player.DroppedFloating==dropped
                && refunds==0 && player.wallet.AddCalls==0 && player._floatingCurrency.Count==0,
                "clear-and-two-late-callbacks-return-only-native-floats-once");
            Check(!HeroRecruitment.IsPurchased(actor.Archer), "clear-does-not-create-free-hero");
            player.NativePickupDropped();
            player._completingPayable=null; player.selectedPayable=null; player._payState=Player.PayState.None;
            Host.JumpClock(2010f,4050); HeroArcherRuntime.Observe(actor.Archer); HeroRecruitment.Tick(); HeroShop.Tick();
            var replacement=FindShopPayable();
            Check(replacement!=null && replacement.Pointer!=payable.Pointer && HeroShop.CanPurchase(),
                "replacement-shop-keeps-new-ticket-independent", "replacement="+(replacement!=null)+" status="+HeroShop.StatusText+" last="+LastLog());
            // Inject a late native callback with its own eight floating objects after the new
            // shop exists. It may return those objects, but cannot use the new shop's ticket.
            player.wallet.Coins-=8;
            for(int i=0;i<8;i++) player._floatingCurrency.Add(new Currency());
            player._payState=Player.PayState.Completed; player._completingPayable=payable;
            int droppedBefore=player.DroppedFloating;
            payable.Owner.OnPay(player);
            Check(player.wallet.AddCalls==0 && player.DroppedFloating==droppedBefore+8
                && player._floatingCurrency.Count==0 && !HeroRecruitment.IsPurchased(actor.Archer),
                "old-owner-late-callback-cannot-settle-replacement-or-mint-wallet");
            player.NativePickupDropped();
            player._completingPayable=null; player._payState=Player.PayState.None;
            Host.AdvanceFrame(); HeroArcherRuntime.Observe(actor.Archer);
            Check(replacement!=null && NativePayment(host,replacement,true).Gate
                && HeroRecruitment.IsPurchased(actor.Archer) && player.wallet.Coins==12,
                "replacement-own-ticket-still-purchases-once");
            return;
        }
        Dictionary<string,string> rollbackPrefs = null;
        if (_scenario == "purchase-rollback")
        {
            var unpaid = host.CreateArcher("BeforePurchase", Side.Left);
            IslandSaveData.CurrentlySavingIsland = host.Island; IslandSaveData.isSavingGame = true;
            var empty = new HeroRecruitment.SaveCapture { Campaign=1, Land=1, Challenge=0 };
            empty.Capture(unpaid.Persistent, "unpaid-before-purchase");
            empty.MarkerSeen = true; empty.Apply();
            IslandSaveData.CurrentlySavingIsland = null; IslandSaveData.isSavingGame = false;
            rollbackPrefs = new Dictionary<string,string>(GlobalSaveData.loaded.prefs.contents);
            Check(rollbackPrefs.Count == 1, "prepurchase-native-empty-snapshot-staged");
        }
        Check(NativePayment(host, payable, true).Gate && host.Player.wallet.Coins == 12 && HeroRecruitment.IsPurchased(actor.Archer), "paid-control");
        string path = HeroRecruitment.ArchivePath;
        Check(HeroRecruitmentArchiveStore.Load(path).Archive.Scopes.Values.All(x => x.All(s => s.Seats.Count == 0)), "purchase-no-durable-paid-row");
        if (_scenario == "purchase-history")
        {
            var disk=HeroRecruitmentArchiveStore.Load(path);
            string oldScope=HeroRecruitmentArchive.NewScope();
            string oldHash=HeroRecruitmentArchive.Hash(OldHistoryJson,oldScope);
            Guid oldReceipt=Guid.NewGuid();
            Check(disk.Archive.Record(oldScope,oldHash,new[]{new HeroPurchaseReceipt
                { Id=oldReceipt, Side=-1, NativeId="" }},HeroRecruitmentArchive.HashKindLegacy,true)
                && HeroRecruitmentArchiveStore.Save(path,disk,disk.Archive),
                "legacy-empty-id-paid-history-fixture-written");
            SavePaidHero(host,actor,SavedJsonA,"npc-history-current");
            var after=HeroRecruitmentArchiveStore.Load(path).Archive;
            Check(after.TryGet(oldScope,oldHash,out var old) && old.LegacyV1
                && old.Seats.Count==1 && old.Seats[0].Id==oldReceipt && old.Seats[0].NativeId=="",
                "legacy-empty-id-history-preserved-byte-semantics");
            var other=new IslandSaveData { land=9,isNew=false,playTimeDays=1,Json=OldHistoryJson };
            host.Campaign.CurrentIsland=other;Host.PlaceIsland(host.Campaign,other,9);
            var oldLoad=new HeroRecruitment.LoadCapture(); oldLoad.Begin(other); oldLoad.End(true);
            Check(HeroRecruitment.DescribeForTests().Contains("unresolved=True") && !HeroRecruitment.CanPurchase,
                "unknown-legacy-owner-remains-reserved-without-guessing",HeroRecruitment.DescribeForTests());
            return;
        }
        if (_scenario == "purchase-range")
        {
            actor.Archer.shootRange = 37f;
            Host.AdvanceFrame(); HeroArcherRuntime.Observe(actor.Archer); HeroArcherRuntime.Tick();
            Check(HeroRecruitment.IsPurchased(actor.Archer) && !HeroArcherRuntime.IsHero(actor.Archer) && !HeroArcherVisuals.HasVisual(actor.Archer), "range-stolen-paid-invisible");
            actor.Archer.shootRange = 8f;
            for(int i=0;i<5;i++) { Host.AdvanceFrame(); HeroArcherRuntime.Observe(actor.Archer); HeroArcherRuntime.Tick(); }
            Check(!HeroArcherRuntime.IsHero(actor.Archer) && !HeroArcherRuntime.IsRecruitable(actor.Archer) && !HeroShop.CanPurchase(), "range-stolen-same-life-stays-blocked");
            return;
        }
        host.Island.Json = SavedJsonA; host.Island.isNew = false; host.Island.playTimeDays = 1;
        IslandSaveData.CurrentlySavingIsland = host.Island; IslandSaveData.isSavingGame = true;
        var capture = new HeroRecruitment.SaveCapture { Campaign=1, Land=1, Challenge=0 };
        if (_scenario == "purchase-empty-id")
        {
            var bystander = host.CreateArcher("NativeUnpaid", Side.Left);
            capture.Capture(bystander.Persistent, "unpaid-native");
            capture.MarkerSeen = true;
            capture.Apply();
            Check(HeroRecruitmentArchiveStore.Load(path).Archive.Scopes.Values.All(x => x.All(v => v.Seats.Count == 0)),
                "missing-live-paid-id-did-not-write-new-sidecar-row");
            Check(GlobalSaveData.loaded.prefs.contents.Count == 0,
                "missing-live-paid-id-did-not-stage-native-row");
            Check(!NativeGlobalSaveAllowed(), "missing-id-blocks-wallet-only-native-save");
            return;
        }
        if (_scenario == "purchase-prefs-fail")
        {
            var row=new IslandSaveData.ObjectData { uniqueID="npc-paid",Root=actor.Persistent };
            row.componentData2.Add(new IslandSaveData.ObjectData.ComponentData { name="Character",type="CharacterData" });
            host.Island.objects.Add(row); capture.Capture(actor.Persistent,"npc-paid"); capture.MarkerSeen=true;
            GlobalSaveData.loaded.prefs.FailWrite=true;
            capture.Apply();
            Check(GlobalSaveData.loaded.prefs.contents.Count==0 && !NativeGlobalSaveAllowed(),
                "prefs-write-failure-blocks-wallet-only-save");
            Check(!NativeCoroutineSaveAllowed(), "prefs-write-failure-blocks-coroutine-save-state-zero");
            GlobalSaveData.loaded.prefs.FailWrite=false;
            capture.Apply();
            Check(NativeGlobalSaveAllowed() && GlobalSaveData.loaded.prefs.contents.Count==1,
                "recovered-prefs-stage-allows-wallet-and-rights-together");
            return;
        }
        if (_scenario == "purchase-duplicate-id")
        {
            for(int i=0;i<2;i++)
            {
                var row=new IslandSaveData.ObjectData { uniqueID="npc-duplicate", Root=actor.Persistent };
                row.componentData2.Add(new IslandSaveData.ObjectData.ComponentData { name="Character",type="CharacterData" });
                host.Island.objects.Add(row);
            }
            capture.Capture(actor.Persistent,"npc-duplicate"); capture.MarkerSeen=true; capture.Apply();
            Check(GlobalSaveData.loaded.prefs.contents.Count==0 && !NativeGlobalSaveAllowed(),
                "duplicate-character-id-never-stages-paid-snapshot-or-wallet");
            Check(HeroRecruitmentArchiveStore.Load(path).Archive.Scopes.Values.All(x=>x.All(v=>v.Seats.Count==0)),
                "duplicate-id-does-not-mirror-bad-sidecar-row");
            return;
        }
        if (_scenario == "purchase-no-marker")
        {
            var record = new IslandSaveData.ObjectData { uniqueID="npc-paid", Root=actor.Persistent };
            record.componentData2.Add(new IslandSaveData.ObjectData.ComponentData { name="Character", type="CharacterData" });
            host.Island.objects.Add(record); capture.Capture(actor.Persistent, "npc-paid");
            capture.Apply();
            Check(GlobalSaveData.loaded.prefs.contents.Count == 0 && !NativeGlobalSaveAllowed(),
                "internal-native-save-failure-does-not-stage-or-save-wallet");
            return;
        }
        // Native Save normal tail is known; sidecar fails after native rights are staged.
        byte[] before=File.ReadAllBytes(path);
        var paidRecord=new IslandSaveData.ObjectData { uniqueID="npc-paid", Root=actor.Persistent };
        paidRecord.componentData2.Add(new IslandSaveData.ObjectData.ComponentData { name="Character", type="CharacterData" });
        host.Island.objects.Add(paidRecord); capture.Capture(actor.Persistent,"npc-paid");
        capture.MarkerSeen = true;
        if (_scenario == "purchase-io") { File.Delete(path); Directory.CreateDirectory(path); }
        try { capture.Apply(); }
        finally { if (_scenario == "purchase-io") { Directory.Delete(path); File.WriteAllBytes(path,before); } }
        Check(NativeGlobalSaveAllowed(), "paid-rights-and-wallet-share-native-save");
        Check(GlobalSaveData.loaded.prefs.contents.Count == 1, "native-rights-staged-with-wallet");
        var savedPrefs = new Dictionary<string,string>(GlobalSaveData.loaded.prefs.contents);
        IslandSaveData.isSavingGame=false; IslandSaveData.CurrentlySavingIsland=null;
        var reloaded = Host.Create(1, SavedJsonA, isNew:false, days:1, sharePlayer:host);
        foreach (var item in savedPrefs) GlobalSaveData.loaded.prefs.contents[item.Key] = item.Value;
        var restoredActor = reloaded.CreateArcher("ReloadedPaid", Side.Right);
        var restoredRecord = new IslandSaveData.ObjectData { uniqueID="npc-paid", Root=restoredActor.Persistent };
        restoredRecord.componentData2.Add(new IslandSaveData.ObjectData.ComponentData { name="Character", type="CharacterData" });
        reloaded.Island.objects.Add(restoredRecord);
        var load = new HeroRecruitment.LoadCapture(); load.Begin(reloaded.Island);
        load.Capture(restoredRecord,restoredActor.Persistent); load.End(true);
        Check(reloaded.Player.wallet.Coins == 12 && HeroRecruitment.IsPurchased(restoredActor.Archer),
            "sidecar-failed-native-reload-restores-paid-owner", HeroRecruitment.DescribeForTests());
        Check(!HeroRecruitment.CanPurchase, "restored-paid-seat-not-charged-again");
        // A prior native Global without this key must not import a future paid snapshot from
        // the later sidecar. The old native island and wallet are restored together.
        if (_scenario == "purchase-rollback")
        {
            var old = Host.Create(1, VirginJsonA, isNew:false, days:1);
            foreach (var item in rollbackPrefs) GlobalSaveData.loaded.prefs.contents[item.Key] = item.Value;
            var oldLoad = new HeroRecruitment.LoadCapture(); oldLoad.Begin(old.Island); oldLoad.End(true);
            Check(old.Player.wallet.Coins == 20 && HeroRecruitment.DescribeForTests().Contains("seats=0")
                && HeroRecruitment.DescribeForTests().Contains("kind=authoritative-empty"),
                "old-native-empty-wins-over-future-sidecar-paid", HeroRecruitment.DescribeForTests());
        }
    }

    private static void RunNativeIsolation()
    {
        ResetArchive();
        var host = Host.Create(1, VirginJsonA, isNew:true, days:0);
        BootVirginIsland(host);
        var payable = BootShop(2000f, 4000);
        var actor = host.CreateArcher("NativePaid", Side.Right);
        HeroArcherRuntime.Observe(actor.Archer);
        Check(NativePayment(host,payable,true).Gate, "isolation-original-paid");
        SavePaidHero(host,actor,SavedJsonA,"npc-isolation-paid");
        var global=GlobalSaveData.loaded;
        var paidCampaign=host.Campaign;
        var paidIsland=host.Island;
        paidIsland.isNew=false; paidIsland.playTimeDays=1;
        var paidRecord=host.Island.objects[0];
        Check(global.prefs.contents.Count==1 && NativeGlobalSaveAllowed(), "isolation-native-paid-staged");

        // Same island JSON and NativeId in a different campaign slot is not a paid owner.
        var other=global.campaigns[0];
        other.CurrentIsland=new IslandSaveData { land=1,isNew=false,playTimeDays=1,Json=SavedJsonA };
        Host.PlaceIsland(other, other.CurrentIsland, 1);
        global.currentCampaign=0; CampaignSaveData.current=other;
        var otherLoad=new HeroRecruitment.LoadCapture(); otherLoad.Begin(other.CurrentIsland); otherLoad.End(true);
        Check(HeroRecruitment.DescribeForTests().Contains("seats=0"),
            "same-json-other-campaign-has-no-paid-seat",HeroRecruitment.DescribeForTests());

        var challenge=new CampaignSaveData { CurrentIsland=new IslandSaveData { land=1,isNew=false,playTimeDays=1,Json=SavedJsonA } };
        Host.PlaceIsland(challenge, challenge.CurrentIsland, 1);
        global.challenges.Add(challenge);
        global.currentCampaign=1; global.currentChallenge=1; CampaignSaveData.current=challenge;
        var challengeLoad=new HeroRecruitment.LoadCapture(); challengeLoad.Begin(challenge.CurrentIsland); challengeLoad.End(true);
        Check(HeroRecruitment.DescribeForTests().Contains("seats=0"),
            "same-json-challenge-has-no-paid-seat",HeroRecruitment.DescribeForTests());

        // Challenge deletion can make its numeric tuple collide with a normal campaign's
        // historical sidecar key. The native account mapping must quarantine that alias.
        HeroNativeRights.BeforeCatalogMutation(global);
        global.challenges.RemoveAt(0);
        global.currentCampaign=1; global.currentChallenge=0; CampaignSaveData.current=challenge;
        var aliasLoad=new HeroRecruitment.LoadCapture(); aliasLoad.Begin(challenge.CurrentIsland); aliasLoad.End(true);
        Check(HeroRecruitment.DescribeForTests().Contains("native-context-mismatch")
            && !HeroRecruitment.DescribeForTests().Contains("seats=1"),
            "challenge-slot-alias-never-inherits-normal-paid",HeroRecruitment.DescribeForTests());

        global.currentCampaign=1; global.currentChallenge=0; CampaignSaveData.current=paidCampaign;
        var originalLoad=new HeroRecruitment.LoadCapture(); originalLoad.Begin(paidIsland);
        originalLoad.Capture(paidRecord,actor.Persistent); originalLoad.End(true);
        Check(HeroRecruitment.IsPurchased(actor.Archer), "original-campaign-paid-still-owned",HeroRecruitment.DescribeForTests());

        // Remove slot zero. The surviving native campaign must keep its private GUID and rights.
        HeroNativeRights.BeforeCatalogMutation(global);
        global.campaigns.RemoveAt(0);
        global.currentCampaign=0;
        var shifted=new HeroRecruitment.LoadCapture(); shifted.Begin(paidIsland);
        shifted.Capture(paidRecord,actor.Persistent); shifted.End(true);
        Check(HeroRecruitment.IsPurchased(actor.Archer),
            "deleted-earlier-slot-preserves-survivor-rights",HeroRecruitment.DescribeForTests());
        Check(NativeGlobalSaveAllowed(), "deleted-earlier-slot-native-catalog-saves");
        var shiftedPrefs=new Dictionary<string,string>(global.prefs.contents);

        var reloaded=Host.Create(1,SavedJsonA,isNew:false,days:1);
        GlobalSaveData.loaded.campaigns.RemoveAt(0);
        GlobalSaveData.loaded.currentCampaign=0;
        foreach(var pair in shiftedPrefs) GlobalSaveData.loaded.prefs.contents[pair.Key]=pair.Value;
        var survivor=reloaded.CreateArcher("Survivor",Side.Right);
        var survivorRow=new IslandSaveData.ObjectData { uniqueID="npc-isolation-paid",Root=survivor.Persistent };
        survivorRow.componentData2.Add(new IslandSaveData.ObjectData.ComponentData { name="Character",type="CharacterData" });
        reloaded.Island.objects.Add(survivorRow);
        var survivorLoad=new HeroRecruitment.LoadCapture(); survivorLoad.Begin(reloaded.Island);
        survivorLoad.Capture(survivorRow,survivor.Persistent); survivorLoad.End(true);
        Check(HeroRecruitment.IsPurchased(survivor.Archer),
            "shifted-catalog-roundtrip-restores-same-owner",HeroRecruitment.DescribeForTests());

        var file=Host.Create(1,SavedJsonA,isNew:false,days:1);
        GlobalSaveData.filename="another-native-file";
        var foreignLoad=new HeroRecruitment.LoadCapture(); foreignLoad.Begin(file.Island); foreignLoad.End(true);
        Check(HeroRecruitment.DescribeForTests().Contains("seats=0"),
            "same-json-other-file-has-no-paid-seat",HeroRecruitment.DescribeForTests());
    }

    private static bool NativeCoroutineSaveAllowed()
    {
        var routine=new GlobalSaveData._Save_d__89 { __1__state=0,__4__this=GlobalSaveData._loaded };
        var gate=typeof(HeroNativeRights).GetNestedType("SyncGate",System.Reflection.BindingFlags.NonPublic)
            .GetMethod("Prefix",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        object[] args={routine,true};
        bool proceed=(bool)gate.Invoke(null,args);
        return proceed && routine.__1__state==0;
    }

    private static bool NativeGlobalSaveAllowed()
    {
        var gate=typeof(HeroNativeRights).GetNestedType("AsyncGate", System.Reflection.BindingFlags.NonPublic)
            .GetMethod("Prefix", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        // Mirrors HarmonyX: the shared runOriginal local starts true and the prefix result is
        // ANDed into it by the generated wrapper.
        object[] args={GlobalSaveData._loaded, (Il2CppSystem.Action<Coatsink.Common.SaveLoadResult>)(_=>{}), true};
        return (bool)gate.Invoke(null,args);
    }

    private static void RunCore()
    {
        ResetArchive();
        var host = Host.Create(1, VirginJsonA, isNew: true, days: 0);
        BootVirginIsland(host);

        var actor = host.CreateArcher("HeroArcherCandidate", Side.Right);
        HeroArcherRuntime.Observe(actor.Archer);

        HeroShop.Tick();
        Check(HeroShop.CanPurchase(), "shop-can-purchase-after-create", "status=" + HeroRecruitment.StatusText);
        Check(HeroShop.CanPlayerPurchase(host.Player), "owner-canpay-ready");

        var payable = FindShopPayable();
        Check(payable != null, "shop-payable-registered");
        if (payable == null) return;

        string archivePath = HeroRecruitment.ArchivePath;
        int addBefore = host.Player.wallet.AddCalls;
        int pickupBefore = host.Player.wallet.PickupCalls;
        int dropBefore = host.Player.DroppedFloating;
        int consumeBefore = host.Player.ConsumedFloating;
        int cancelBefore = host.Player.CancelCalls;

        // Negative control: no TransactionStarted ticket, but all 8 coins are already floating.
        // The completed gate must refuse on the missing receipt, not on the coin count.
        var unarmed = NativePayment(host, payable, arm: false);
        Check(!unarmed.Gate, "native-gate-refuses-unarmed-8-floating-no-ticket");
        Check(unarmed.FloatingAtGate == 8, "unarmed-control-coins-present", "floating=" + unarmed.FloatingAtGate);
        Check(unarmed.Cancelled, "unarmed-control-took-native-cancel-path");
        Check(host.Player.wallet.Coins == 12 && host.Player.DroppedFloating == dropBefore + 8,
            "unarmed-control-wallet12-ground8-before-pickup",
            "coins=" + host.Player.wallet.Coins + " ground=" + host.Player.DroppedFloating);
        Check(Held(host) == 20, "unarmed-control-conservation-wallet+floating+dropped=20", "held=" + Held(host));
        Check(host.Player.CancelCalls == cancelBefore + 1, "unarmed-control-native-cancel-called");
        Check(host.Player.ConsumedFloating == consumeBefore, "unarmed-control-nothing-consumed");
        Check(host.Player.wallet.AddCalls == addBefore, "unarmed-control-no-mod-refund");

        // Explicit separate pickup step (never folded into the drop).
        host.Player.NativePickupDropped();
        Check(host.Player.wallet.Coins == 20 && host.Player.DroppedFloating == 0,
            "unarmed-control-pickup-restores-wallet-20", "coins=" + host.Player.wallet.Coins);
        Check(host.Player.wallet.PickupCalls == pickupBefore + 1 && host.Player.wallet.AddCalls == addBefore,
            "unarmed-control-pickup-is-not-mod-refund");

        Host.AdvanceFrame();

        // --- attempt 1: armed with ticket, engine cannot produce the hero visual -> mod refund ---
        HeroArcherVisuals.VisualProducible = false;
        string archiveBeforeFailure = Sha(archivePath);
        var attempt1 = NativePayment(host, payable, arm: true);
        Check(attempt1.Gate, "failure-attempt-native-completed-canpay-with-ticket");
        Check(attempt1.FloatingAtGate == 8, "failure-attempt-coins-present", "floating=" + attempt1.FloatingAtGate);
        Check(host.Player.wallet.Coins == 20, "failure-attempt-wallet-back-to-20", "coins=" + host.Player.wallet.Coins);
        Check(host.Player.wallet.AddCalls == addBefore + 1, "failure-attempt-mod-refund-used");
        Check(Held(host) == 20, "failure-attempt-conservation-wallet+floating+dropped=20", "held=" + Held(host));
        Check(host.Player.DroppedFloating == 0, "failure-attempt-no-native-drop");
        Check(host.Player.ConsumedFloating == consumeBefore + 8, "failure-attempt-native-consumed-the-floating-coins");
        Check(HeroRecruitment.SeatSide(actor.Archer) == 0, "failure-attempt-no-seat");
        Check(!HeroArcherRuntime.IsHero(actor.Archer), "failure-attempt-not-hero");
        Check(!HeroArcherVisuals.HasVisual(actor.Archer), "failure-attempt-no-visual");
        Check(Math.Abs(actor.Archer.shootRange - 8f) < 0.0001f, "failure-attempt-range-restored", "shootRange=" + actor.Archer.shootRange);
        Check(Sha(archivePath) == archiveBeforeFailure, "failure-attempt-archive-unchanged");
        Check(HeroRecruitment.DescribeForTests().Contains("seats=0"), "failure-attempt-no-receipt", HeroRecruitment.DescribeForTests());
        Check(LogContains("purchase rejected; refunded 8 coins"),
            "failure-attempt-refund-logged", LastLog());
        Check(LogContains("[HeroArcher] selected side=1") && LogContains("visual=False"),
            "failure-attempt-promotion-then-visual-reject",
            "production promoted the archer and rejected on the engine visual gate");

        Host.AdvanceFrame();

        // --- attempt 2: same armed ticket, engine produces the visual -> real receipt + hero ---
        HeroArcherVisuals.VisualProducible = true;
        string archiveBeforeSuccess = Sha(archivePath);
        var attempt2 = NativePayment(host, payable, arm: true);
        Check(attempt2.Gate, "success-attempt-native-completed-canpay-with-ticket");
        Check(host.Player.wallet.Coins == 12, "success-attempt-wallet-12", "coins=" + host.Player.wallet.Coins);
        Check(Held(host) == 12 && host.Player.ConsumedFloating == consumeBefore + 16,
            "success-attempt-charge-8-consumed", "held=" + Held(host) + " consumed=" + (host.Player.ConsumedFloating - consumeBefore));
        Check(host.Player.wallet.AddCalls == addBefore + 1, "success-attempt-no-extra-refund");
        Check(HeroRecruitment.SeatSide(actor.Archer) == 1, "success-attempt-paid-seat-side-right");
        Check(HeroArcherRuntime.IsHero(actor.Archer), "success-attempt-is-hero");
        Check(HeroArcherVisuals.HasVisual(actor.Archer), "success-attempt-has-visual");
        Check(Math.Abs(actor.Archer.shootRange - 16f) < 0.0001f, "success-attempt-range-2x", "shootRange=" + actor.Archer.shootRange);
        Check(LogContains("purchase completed"), "success-attempt-completed-logged", LastLog());
        Check(LogContains("visual=True"), "success-attempt-promotion-visual-true");
        string describe = HeroRecruitment.DescribeForTests();
        Check(describe.Contains("seats=1"), "success-attempt-one-paid-receipt", describe);

        // --- duplicate native callback for the already settled payment ---
        Host.AdvanceFrame();
        string archiveBeforeDuplicate = Sha(archivePath);
        int addBeforeDuplicate = host.Player.wallet.AddCalls;
        host.Player._completingPayable = payable;
        host.Player._payState = Player.PayState.Completed;
        payable.Owner.OnPay(host.Player);
        host.Player._completingPayable = null;
        host.Player._payState = Player.PayState.None;
        Check(host.Player.wallet.Coins == 12, "duplicate-callback-wallet-unchanged", "coins=" + host.Player.wallet.Coins);
        Check(Held(host) == 12, "duplicate-callback-held-unchanged", "held=" + Held(host));
        Check(host.Player.wallet.AddCalls == addBeforeDuplicate, "duplicate-callback-no-refund");
        Check(HeroRecruitment.SeatSide(actor.Archer) == 1, "duplicate-callback-seat-unchanged");
        Check(HeroRecruitment.DescribeForTests().Contains("seats=1"), "duplicate-callback-one-receipt", HeroRecruitment.DescribeForTests());
        Check(Sha(archivePath) == archiveBeforeDuplicate, "duplicate-callback-archive-unchanged");

        // --- native save: the paid receipt gains its native id ---
        SavePaidHero(host, actor, SavedJsonA, "npc-hero-A");
        string keyA = HeroRecruitmentArchive.ContextKey("global-v35", 1, 0, 1);
        Check(ArchiveHasReceipt(archivePath, keyA, "npc-hero-A", out string receiptDetail),
            "save-archive-paid-receipt", receiptDetail);
        Check(HeroArcherRuntime.IsHero(actor.Archer), "save-hero-still-active");
    }

    // ---------------- scenario: lands ----------------

    private static void RunLands()
    {
        ResetArchive();
        string archivePath = HeroRecruitment.ArchivePath;
        string keyA = HeroRecruitmentArchive.ContextKey("global-v35", 1, 0, 1);
        string keyB = HeroRecruitmentArchive.ContextKey("global-v35", 1, 0, 2);

        // Land A: real virgin epoch, real purchase, real native save.
        var hostA = Host.Create(1, VirginJsonA, isNew: true, days: 0);
        BootVirginIsland(hostA);
        var actorA = hostA.CreateArcher("ArcherA", Side.Right);
        HeroArcherRuntime.Observe(actorA.Archer);
        HeroShop.Tick();
        var payableA = FindShopPayable();
        Check(payableA != null && HeroShop.CanPurchase(), "lands-A-shop-ready");
        if (payableA == null) return;
        HeroArcherVisuals.VisualProducible = true;
        Check(NativePayment(hostA, payableA, arm: true).Gate, "lands-A-purchase-native-gate");
        Check(hostA.Player.wallet.Coins == 12, "lands-A-purchase-wallet-12", "coins=" + hostA.Player.wallet.Coins);
        SavePaidHero(hostA, actorA, SavedJsonA, "npc-hero-A");
        Check(ArchiveHasReceipt(archivePath, keyA, "npc-hero-A", out string aDetail), "lands-A-receipt-saved", aDetail);
        string receiptIdA = ReceiptIdOf(keyA);
        string epochA = EpochOf(keyA);
        string freezeAFull = CanonicalScope(archivePath, keyA, includeBaseline: true);
        string freezeASnapshots = CanonicalScope(archivePath, keyA, includeBaseline: false);
        Check(epochA.Length > 0 && receiptIdA.Length > 0 && !freezeAFull.StartsWith("<") && !freezeASnapshots.StartsWith("<"),
            "lands-A-frozen", "epoch=" + Short(epochA) + " receipt=" + Short(receiptIdA)
            + " frozen=" + freezeASnapshots.Length + " chars");
        string paidHashA = PaidSnapshotHash(keyA, "npc-hero-A");
        Check(paidHashA.Length > 0 && BaselineOf(keyA) != paidHashA,
            "lands-A-baseline-still-virgin-before-reload", "baseline=" + Short(BaselineOf(keyA)) + " paid=" + Short(paidHashA));
        int walletAfterA = hostA.Player.wallet.Coins;
        int addCallsAfterA = hostA.Player.wallet.AddCalls;

        // Land B: another island, real virgin epoch, real purchase, real save on the same wallet.
        var hostB = Host.Create(2, VirginJsonB, isNew: true, days: 0, sharePlayer: hostA);
        Host.JumpClock(106f, 30);
        BootVirginIsland(hostB);
        var actorB = hostB.CreateArcher("ArcherB", Side.Right);
        HeroArcherRuntime.Observe(actorB.Archer);
        HeroShop.Tick(); // world change: clears A's shop
        HeroShop.Tick(); // creates B's shop
        var payableB = FindShopPayable();
        Check(payableB != null && HeroShop.CanPurchase(), "lands-B-shop-ready");
        var bGate = payableB == null ? new NativePaymentResult() : NativePayment(hostB, payableB, arm: true);
        Check(bGate.Gate, "lands-B-purchase-native-gate");
        Check(hostB.Player.wallet.Coins == 4, "lands-B-deducted-8", "coins=" + hostB.Player.wallet.Coins);
        Check(hostB.Player.wallet.AddCalls == addCallsAfterA, "lands-B-no-refund");
        Check(HeroRecruitment.SeatSide(actorB.Archer) == 1 && HeroArcherRuntime.IsHero(actorB.Archer), "lands-B-own-hero");
        SavePaidHero(hostB, actorB, SavedJsonB, "npc-hero-B");
        Check(ArchiveHasReceipt(archivePath, keyB, "npc-hero-B", out string bDetail), "lands-B-own-receipt", bDetail);
        string receiptIdB = ReceiptIdOf(keyB);
        string freezeBFull = CanonicalScope(archivePath, keyB, includeBaseline: true);
        Check(receiptIdB.Length > 0 && !freezeBFull.StartsWith("<"),
            "lands-B-frozen", "receipt=" + Short(receiptIdB) + " frozen=" + freezeBFull.Length + " chars");
        Check(ReceiptIdOf(keyA) == receiptIdA, "lands-A-receipt-identity-stable-after-B");
        Check(CanonicalScope(archivePath, keyA, includeBaseline: true) == freezeAFull,
            "lands-A-scope-and-baseline-frozen-after-B",
            "A scope serialization byte-identical after B purchase/save");

        // Back to land A: reload the exact saved snapshot; A's paid seat must be restored, B's kept.
        Host.Mount(hostA);
        var actorA2 = hostA.CreateArcher("ArcherA2", Side.Right);
        HeroArcherRuntime.Observe(actorA2.Archer);
        var islandA2 = new IslandSaveData { land = 1, isNew = false, playTimeDays = 3, Json = SavedJsonA };
        var recordA2 = new IslandSaveData.ObjectData { uniqueID = "npc-hero-A", Root = actorA2.Persistent };
        recordA2.componentData2.Add(new IslandSaveData.ObjectData.ComponentData { name = "Character", type = "CharacterData" });
        islandA2.objects.Add(recordA2);
        hostA.Island = islandA2;
        hostA.Campaign.CurrentIsland = islandA2;
        Host.PlaceIsland(hostA.Campaign, islandA2, 1);
        var loadA = new HeroRecruitment.LoadCapture();
        loadA.Begin(islandA2);
        loadA.Capture(recordA2, actorA2.Persistent);
        loadA.End(true);

        Check(hostA.Player.wallet.Coins == 4 && hostA.Player.wallet.AddCalls == addCallsAfterA,
            "lands-load-A-no-charge", "coins=" + hostA.Player.wallet.Coins);
        Check(HeroRecruitment.SeatSide(actorA2.Archer) == 1, "lands-A-seat-restored");
        Check(HeroRecruitment.IsPurchased(actorA2.Archer), "lands-A-hero-history-bound");
        string describeA = HeroRecruitment.DescribeForTests();
        Check(describeA.Contains("seats=1") && describeA.Contains(":native:bound") && describeA.Contains(Short(receiptIdA)),
            "lands-A-reload-binds-A-receipt", describeA);
        Check(EpochOf(keyA) == epochA, "lands-A-epoch-stable", "epoch=" + Short(EpochOf(keyA)));
        Check(CanonicalScope(archivePath, keyA, includeBaseline: false) == freezeASnapshots,
            "lands-A-snapshot-content-stable-after-reload");
        Check(paidHashA.Length > 0 && BaselineOf(keyA) == paidHashA,
            "lands-A-baseline-advanced-to-own-paid-snapshot", "baseline=" + Short(BaselineOf(keyA)));
        Check(ArchiveHasReceipt(archivePath, keyB, "npc-hero-B", out string bAfter),
            "lands-B-receipt-survives-A-reload", bAfter);
        Check(ReceiptIdOf(keyB) == receiptIdB && CanonicalScope(archivePath, keyB, includeBaseline: true) == freezeBFull,
            "lands-B-receipt-and-scope-frozen-across-A-reload", "receipt=" + Short(ReceiptIdOf(keyB)));
    }

    // ---------------- scenario: carry (persistent refusal lifecycle) ----------------

    private static void RunCarry()
    {
        ResetArchive();
        string archivePath = HeroRecruitment.ArchivePath;
        // Unclaimed old paid history (as left behind by an upgraded v1 archive): a legacy scope
        // owned by no context, whose snapshot matches no destination snapshot.
        WriteUnclaimedPaidFixture(archivePath);

        var host = Host.Create(7, DestJson, isNew: false, days: 3);
        var actor = host.CreateArcher("DestArcher", Side.Right);
        var destRecord = new IslandSaveData.ObjectData { uniqueID = "npc-dest", Root = actor.Persistent };
        destRecord.componentData2.Add(new IslandSaveData.ObjectData.ComponentData { name = "Character", type = "CharacterData" });
        host.Island.objects.Add(destRecord);
        HeroArcherRuntime.Observe(actor.Archer);

        // TryPop/BeginLoad with the native carry field still set: the disjoint-history gate
        // must refuse to adopt, so EndLoad leaves the destination unresolved.
        host.Campaign.carryForward.present = true;
        var load = new HeroRecruitment.LoadCapture();
        load.Begin(host.Island);
        load.End(true);
        Check(HeroRecruitment.DescribeForTests().Contains("unresolved=True"), "carry-endload-cannot-adopt",
            HeroRecruitment.DescribeForTests());
        Check(HeroRecruitment.StatusText.Contains("历史待确认"), "carry-status-suspended", HeroRecruitment.StatusText);
        Check(!HeroRecruitment.CanPurchase, "carry-can-purchase-false-after-load");

        // Native ApplyToScene finishes and clears carry afterwards; nothing retries the load.
        host.Campaign.carryForward.present = false;
        for (int i = 0; i < 3; i++) { Host.AdvanceFrame(); HeroRecruitment.Tick(); }
        Check(HeroRecruitment.DescribeForTests().Contains("unresolved=True"), "carry-still-unresolved-after-carry-cleared-and-ticks",
            HeroRecruitment.DescribeForTests());
        Check(!HeroRecruitment.CanPurchase, "carry-can-purchase-still-false");

        // The shop appears but refuses payment; a full native payment can never charge.
        HeroShop.Tick();
        var payable = FindShopPayable();
        Check(payable != null, "carry-shop-created");
        Check(!HeroShop.CanPurchase() && payable != null && !HeroShop.CanPlayerPurchase(host.Player),
            "carry-shop-refuses-payment");
        string archiveBeforePayment = Sha(archivePath);
        int addBefore = host.Player.wallet.AddCalls;
        int pickupBefore = host.Player.wallet.PickupCalls;
        int consumeBefore = host.Player.ConsumedFloating;
        int cancelBefore = host.Player.CancelCalls;
        var attempt = payable == null ? new NativePaymentResult() : NativePayment(host, payable, arm: true);
        Check(!attempt.Gate, "carry-native-completed-gate-refuses");
        Check(attempt.FloatingAtGate == 8, "carry-payment-had-8-coins", "floating=" + attempt.FloatingAtGate);
        Check(host.Player.wallet.Coins == 12 && host.Player.DroppedFloating == 8,
            "carry-wallet12-ground8-before-pickup",
            "coins=" + host.Player.wallet.Coins + " ground=" + host.Player.DroppedFloating);
        Check(Held(host) == 20, "carry-conservation-wallet+floating+dropped=20", "held=" + Held(host));
        Check(host.Player.CancelCalls == cancelBefore + 1 && host.Player.ConsumedFloating == consumeBefore,
            "carry-cancel-no-consume");
        Check(host.Player.wallet.AddCalls == addBefore, "carry-no-mod-refund");
        Check(!LogContains("purchase completed"), "carry-no-charge-no-hero-event");
        Check(Sha(archivePath) == archiveBeforePayment, "carry-no-receipt-written");

        // Separate explicit pickup step.
        host.Player.NativePickupDropped();
        Check(host.Player.wallet.Coins == 20 && host.Player.DroppedFloating == 0 && host.Player.wallet.PickupCalls == pickupBefore + 1,
            "carry-pickup-restores-wallet-20", "coins=" + host.Player.wallet.Coins);

        // A native save while unresolved must preserve the state and write nothing.
        IslandSaveData.CurrentlySavingIsland = host.Island;
        IslandSaveData.isSavingGame = true;
        var save = new HeroRecruitment.SaveCapture { Campaign = 1, Land = 7, Challenge = 0 };
        save.Capture(actor.Persistent, "npc-dest");
        save.MarkerSeen = true;
        save.Apply();
        IslandSaveData.isSavingGame = false;
        IslandSaveData.CurrentlySavingIsland = null;
        Check(LogContains("save-preserve-unresolved"), "carry-save-preserves-unresolved", LastLog());
        Check(Sha(archivePath) == archiveBeforePayment, "carry-save-writes-nothing");
        Check(HeroRecruitment.DescribeForTests().Contains("unresolved=True"), "carry-still-unresolved-after-save",
            HeroRecruitment.DescribeForTests());

        // Control: identical destination and archive, but carry is already false at BeginLoad.
        // Adoption must succeed, proving the persistent refusal above is carry-gated.
        var actor2 = host.CreateArcher("DestArcher2", Side.Right);
        HeroArcherRuntime.Observe(actor2.Archer);
        var load2 = new HeroRecruitment.LoadCapture();
        load2.Begin(host.Island);
        load2.End(true);
        string describe = HeroRecruitment.DescribeForTests();
        Check(!describe.Contains("unresolved=True") && describe.Contains("kind=disjoint-history-fresh"),
            "carry-control-adopts-when-carry-false", describe);
        Check(LogContains("disjoint-history-fresh"), "carry-control-adoption-logged");
        Check(HeroRecruitment.CanPurchase, "carry-control-purchase-enabled");
    }

    // ---------------- scenario: board (embarkee protection) ----------------

    private static void RunBoard()
    {
        BoardEligibilityAndPayment();
        BoardPurchaseLifecycle();
        BoardRecycle();
        BoardFormerWorld();
        BoardLoadAndSuccessor();
    }

    /// <summary>Purchase eligibility needs a free, correctly-owned embarkee; a pending target or
    /// an embarked state blocks the shop and the payment gate keeps every coin conserved.</summary>
    private static void BoardEligibilityAndPayment()
    {
        ResetArchive();
        var host = Host.Create(1, VirginJsonA, isNew: true, days: 0);
        BootVirginIsland(host);
        var payable = BootShop(106f, 30);
        Check(payable != null, "board-shop-created");
        if (payable == null) return;
        var actor = host.CreateArcher("Eligible", Side.Right);
        HeroArcherRuntime.Observe(actor.Archer);
        var embarkable = CreateBoat(host).GetComponent<Embarkable>();

        Check(HeroArcherRuntime.IsRecruitable(actor.Archer), "board-eligible-with-free-embarkee");
        actor.Embarkee._owner = null;
        Check(!HeroArcherRuntime.IsRecruitable(actor.Archer), "board-null-native-owner-rejected");
        var wrongOwner = host.CreateWorkerSuccessor("WrongOwner");
        actor.Embarkee._owner = wrongOwner.Embarkee._owner;
        Check(!HeroArcherRuntime.IsRecruitable(actor.Archer), "board-wrong-root-native-owner-rejected");
        var wrongOwnerPayment = NativePayment(host, payable, arm: true);
        Check(!wrongOwnerPayment.Gate && wrongOwnerPayment.Cancelled && HeroRecruitment.SeatSide(actor.Archer) == 0,
            "board-wrong-owner-no-purchase");
        host.Player.NativePickupDropped();
        Check(host.Player.wallet.Coins == 20, "board-wrong-owner-coins-recovered");
        actor.Embarkee._owner = actor.Archer;
        Check(!HeroBoardingPolicy.BlocksTarget(actor.Embarkee, embarkable)
            && !HeroBoardingPolicy.BlocksEmbark(actor.Embarkee), "board-predicate-false-before-publish");
        Check(HookTargetPrefixAllows(actor.Embarkee, embarkable) && HookEmbarkPrefixAllows(actor.Embarkee),
            "board-prefix-allows-before-publish");

        var withoutEmbarkee = host.CreateArcher("NoEmbarkee", Side.Right, embarkee: false);
        HeroArcherRuntime.Observe(withoutEmbarkee.Archer);
        Check(!HeroArcherRuntime.IsRecruitable(withoutEmbarkee.Archer), "board-ineligible-without-embarkee");

        actor.Embarkee.EmbarkableTarget = embarkable;
        Check(!HeroArcherRuntime.IsRecruitable(actor.Archer), "board-ineligible-with-target");
        actor.Embarkee.EmbarkableTarget = null;
        actor.Embarkee.IsEmbarked = true;
        Check(!HeroArcherRuntime.IsRecruitable(actor.Archer), "board-ineligible-embarked");
        actor.Embarkee.IsEmbarked = false;

        // A native assignment before payment: the shop refuses and every coin stays accounted.
        actor.Embarkee.EmbarkableTarget = embarkable;
        Check(!HeroShop.CanPurchase(), "board-targeting-blocks-shop");
        var blocked = NativePayment(host, payable, arm: true);
        Check(!blocked.Gate && blocked.Cancelled, "board-targeting-native-gate-refuses");
        Check(host.Player.wallet.Coins == 12 && host.Player.DroppedFloating == 8 && Held(host) == 20,
            "board-targeting-coin-conservation", "coins=" + host.Player.wallet.Coins + " ground=" + host.Player.DroppedFloating);
        host.Player.NativePickupDropped();
        actor.Embarkee.EmbarkableTarget = null;
        Check(host.Player.wallet.Coins == 20, "board-targeting-pickup-restores");

        // A target that lands mid-transaction (after the ticket was armed) must also refuse.
        // Three frames clear the 0.25 s candidate-cache window left by the refused attempt.
        Host.AdvanceFrame();
        Host.AdvanceFrame();
        Host.AdvanceFrame();
        Check(HeroShop.CanPurchase(), "board-midtransaction-shop-ready");
        host.Player.selectedPayable = payable;
        host.Player._payState = Player.PayState.Transaction;
        payable.InvokeTransactionStarted(host.Player);
        actor.Embarkee.EmbarkableTarget = embarkable;   // mid-transaction native assignment
        host.Player.wallet.Coins -= HeroShop.Price;
        for (int i = 0; i < HeroShop.Price; i++) host.Player._floatingCurrency.Add(new Currency());
        host.Player._payState = Player.PayState.Completed;
        host.Player._completingPayable = payable;
        bool midGate = payable.Owner.CanPay(host.Player);
        if (midGate)
        {
            payable.Owner.OnPay(host.Player);
            host.Player.ConsumedFloating += host.Player._floatingCurrency.Count;
            host.Player._floatingCurrency.Clear();
        }
        else
        {
            host.Player.CancelTransaction();
            host.Player.DropFloatingCurrency();
        }
        host.Player._completingPayable = null;
        host.Player._payState = Player.PayState.None;
        host.Player.selectedPayable = null;
        Check(!midGate, "board-midtransaction-target-blocked");
        Check(host.Player.wallet.Coins == 12 && host.Player.DroppedFloating == 8 && Held(host) == 20,
            "board-midtransaction-coin-conservation", "coins=" + host.Player.wallet.Coins + " ground=" + host.Player.DroppedFloating);
        host.Player.NativePickupDropped();
        actor.Embarkee.EmbarkableTarget = null;
        Check(host.Player.wallet.Coins == 20, "board-midtransaction-pickup-restores");
    }

    /// <summary>Purchase publishes the receipt before disabling the embarkee, every release path
    /// restores it, and the hooks gate target/embark calls read-only on the confirmed career.</summary>
    private static void BoardPurchaseLifecycle()
    {
        ResetArchive();
        var host = Host.Create(1, VirginJsonA, isNew: true, days: 0);
        BootVirginIsland(host);
        var payable = BootShop(113f, 40);
        Check(payable != null, "board-lifecycle-shop-created");
        if (payable == null) return;
        var actor = host.CreateArcher("Hero", Side.Right);
        HeroArcherRuntime.Observe(actor.Archer);
        var embarkable = CreateBoat(host).GetComponent<Embarkable>();
        Check(HeroShop.CanPurchase(), "board-lifecycle-can-purchase");

        int unregBefore = EmbarkableSim.UnregisterEvents;
        string observedAtDisable = null;
        EmbarkableSim.OnNativeDisableProbe = _ => { observedAtDisable = HeroRecruitment.DescribeForTests(); };
        var buy = NativePayment(host, payable, arm: true);
        EmbarkableSim.OnNativeDisableProbe = null;
        Check(buy.Gate && host.Player.wallet.Coins == 12, "board-lifecycle-purchase-succeeds");
        Check(actor.Embarkee.enabled == false && !actor.Embarkee.IsTargetingEmbarkable,
            "board-lifecycle-borrows-embarkee");
        Check(EmbarkableSim.UnregisterEvents == unregBefore + 1 && !EmbarkableSim.IsRegistered(actor.Embarkee),
            "board-lifecycle-native-unregister-observed");
        Check(observedAtDisable != null && observedAtDisable.Contains("seats=1") && observedAtDisable.Contains("bound"),
            "board-lifecycle-receipt-public-before-disable", observedAtDisable ?? "no probe");
        Check(HeroRecruitment.DescribeForTests().Contains(":borrowed"), "board-lifecycle-describe-borrow",
            HeroRecruitment.DescribeForTests());
        Check(actor.Embarkee.ClearTargetCalls == 1, "board-lifecycle-native-clear-target-called");

        // Predicate and hook behaviour on the confirmed career; null cleanup stays allowed.
        Check(HeroBoardingPolicy.BlocksTarget(actor.Embarkee, embarkable), "board-predicate-true-after-purchase");
        Check(!HeroBoardingPolicy.BlocksTarget(actor.Embarkee, null), "board-null-target-cleanup-allowed");
        Check(HeroBoardingPolicy.BlocksEmbark(actor.Embarkee), "board-embark-blocked-after-purchase");
        int setCalls = actor.Embarkee.SetTargetCalls;
        Check(!CallThroughTargetHook(actor.Embarkee, embarkable) && actor.Embarkee.SetTargetCalls == setCalls,
            "board-hook-refuses-target-call");
        int embarkCalls = actor.Embarkee.EmbarkCalls;
        Check(!CallThroughEmbarkHook(actor.Embarkee) && actor.Embarkee.EmbarkCalls == embarkCalls,
            "board-hook-refuses-embark-call");
        Check(CallThroughTargetHook(actor.Embarkee, null), "board-hook-allows-null-cleanup-call");

        // A native Knight-side enumeration of an unrelated defender keeps working.
        var knight = host.CreateKnight("Defender");
        Check(!HeroBoardingPolicy.BlocksTarget(knight.Embarkee, embarkable)
            && !HeroBoardingPolicy.BlocksEmbark(knight.Embarkee), "board-knight-not-blocked");
        Check(CallThroughTargetHook(knight.Embarkee, embarkable) && knight.Embarkee.EmbarkableTarget == embarkable,
            "board-knight-target-set-through");
        knight.Embarkee.EmbarkableTarget = null;

        // The hooks are pure predicates: no state, no registrar, no field writes.
        string describeBefore = HeroRecruitment.DescribeForTests();
        int unreg = EmbarkableSim.UnregisterEvents, reg = EmbarkableSim.RegisterEvents, redist = EmbarkableSim.Redistributions;
        bool enabledBefore = actor.Embarkee.enabled;
        int setBefore = actor.Embarkee.SetTargetCalls, embarkBefore = actor.Embarkee.EmbarkCalls, clearBefore = actor.Embarkee.ClearTargetCalls;
        for (int i = 0; i < 3; i++)
        {
            HeroBoardingPolicy.BlocksTarget(actor.Embarkee, embarkable);
            HeroBoardingPolicy.BlocksTarget(actor.Embarkee, null);
            HeroBoardingPolicy.BlocksEmbark(actor.Embarkee);
            HookTargetPrefixAllows(actor.Embarkee, embarkable);
            HookEmbarkPrefixAllows(actor.Embarkee);
        }
        Check(actor.Embarkee.enabled == enabledBefore && actor.Embarkee.SetTargetCalls == setBefore
            && actor.Embarkee.EmbarkCalls == embarkBefore && actor.Embarkee.ClearTargetCalls == clearBefore
            && EmbarkableSim.UnregisterEvents == unreg && EmbarkableSim.RegisterEvents == reg
            && EmbarkableSim.Redistributions == redist && HeroRecruitment.DescribeForTests() == describeBefore,
            "board-hook-no-side-effects");

        // Repeated maintenance on the same life must not re-write or switch the borrow.
        Host.JumpClock(500f, 900);
        HeroRecruitment.Tick();
        Check(actor.Embarkee.enabled == false && EmbarkableSim.UnregisterEvents == unreg
            && HeroRecruitment.DescribeForTests().Contains(":borrowed"), "board-repeat-tick-keeps-same-borrow");

        // The visual toggle is not ownership: turning the feature off keeps the paid career.
        ModConfig.HeroArcherEnabled.Value = false;
        HeroArcherRuntime.Tick();
        Host.AdvanceFrame();
        HeroRecruitment.Tick();
        Check(!HeroArcherRuntime.Enabled && actor.Embarkee.enabled == false
            && HeroRecruitment.SeatSide(actor.Archer) == 1 && HeroRecruitment.DescribeForTests().Contains(":borrowed"),
            "board-f5-off-keeps-paid-career");
        ModConfig.HeroArcherEnabled.Value = true;
        HeroArcherRuntime.Observe(actor.Archer);
        HeroArcherRuntime.Tick();
        Check(HeroArcherRuntime.IsHero(actor.Archer), "board-f5-on-restores-hero");

        // Death releases the seat and hands the embarkee back.
        actor.Damage.Kill();
        Check(HeroRecruitment.SeatSide(actor.Archer) == 0, "board-death-seat-released");
        Check(actor.Embarkee.enabled == true && EmbarkableSim.IsRegistered(actor.Embarkee),
            "board-death-returns-embarkee");
        Check(!HeroBoardingPolicy.BlocksTarget(actor.Embarkee, embarkable)
            && !HeroBoardingPolicy.BlocksEmbark(actor.Embarkee), "board-predicate-false-after-revoke");

        // Activation failure rolls the whole purchase back and restores the embarkee.
        Host.AdvanceFrame();
        var failing = host.CreateArcher("Failing", Side.Right);
        HeroArcherRuntime.Observe(failing.Archer);
        HeroArcherVisuals.VisualProducible = false;
        HeroShop.Tick();
        int failUnreg = EmbarkableSim.UnregisterEvents, failReg = EmbarkableSim.RegisterEvents;
        var buy2 = NativePayment(host, payable, arm: true);
        HeroArcherVisuals.VisualProducible = true;
        Check(buy2.Gate, "board-rollback-native-gate");
        Check(host.Player.wallet.Coins == 12, "board-rollback-refunded", "coins=" + host.Player.wallet.Coins);
        Check(HeroRecruitment.SeatSide(failing.Archer) == 0, "board-rollback-no-seat");
        Check(failing.Embarkee.enabled == true && EmbarkableSim.IsRegistered(failing.Embarkee), "board-rollback-returns-embarkee");
        Check(EmbarkableSim.UnregisterEvents == failUnreg + 1 && EmbarkableSim.RegisterEvents == failReg + 1,
            "board-rollback-native-roundtrip");

        // A pre-disabled embarkee is never claimed; releasing it must not write it back to enabled.
        Host.AdvanceFrame();
        var reclusive = host.CreateArcher("PreDisabled", Side.Left);
        HeroArcherRuntime.Observe(reclusive.Archer);
        reclusive.Embarkee.enabled = false;   // already disabled by someone else
        Check(!EmbarkableSim.IsRegistered(reclusive.Embarkee), "board-predisabled-fixture");
        HeroShop.Tick();
        int preUnreg = EmbarkableSim.UnregisterEvents;
        var buy3 = NativePayment(host, payable, arm: true);
        Check(buy3.Gate && HeroRecruitment.SeatSide(reclusive.Archer) == -1, "board-predisabled-purchase");
        Check(reclusive.Embarkee.enabled == false && EmbarkableSim.UnregisterEvents == preUnreg,
            "board-predisabled-not-claimed");
        Check(HeroRecruitment.DescribeForTests().Contains("claims=0"), "board-predisabled-no-claim");
        reclusive.Damage.Kill();
        Check(reclusive.Embarkee.enabled == false, "board-predisabled-release-does-not-enable");
    }

    /// <summary>Pool recycle freezes the exact candidate life: an active object keeps ownership,
    /// an inactive one releases without an active registration, repeats are no-ops, a reused life
    /// never inherits the borrow, and a former-world object defers while it stays active.</summary>
    private static void BoardRecycle()
    {
        ResetArchive();
        var host = Host.Create(1, VirginJsonA, isNew: true, days: 0);
        BootVirginIsland(host);
        var payable = BootShop(120f, 50);
        Check(payable != null, "board-recycle-shop");
        if (payable == null) return;
        var actor = host.CreateArcher("Recycled", Side.Right);
        HeroArcherRuntime.Observe(actor.Archer);
        var embarkable = CreateBoat(host).GetComponent<Embarkable>();
        var buy = NativePayment(host, payable, arm: true);
        Check(buy.Gate && actor.Embarkee.enabled == false, "board-recycle-fixture-borrowed");

        // FastDespawn while the object is still active is not a provable despawn: ownership stays.
        FastDespawnViaPatch(actor.Go, 0f);
        Check(actor.Embarkee.enabled == false && HeroRecruitment.DescribeForTests().Contains(":borrowed"),
            "board-recycle-active-despawn-keeps-ownership");

        // The real recycle: the object is inactive when the immediate despawn runs.
        int regBefore = EmbarkableSim.RegisterEvents;
        int inactBefore = EmbarkableSim.InactiveRegisterSkips;
        actor.Go.SetActive(false);
        FastDespawnViaPatch(actor.Go, 0f);
        Check(actor.Embarkee.enabled == true && EmbarkableSim.InactiveRegisterSkips == inactBefore + 1
            && EmbarkableSim.RegisterEvents == regBefore, "board-recycle-inactive-release-no-register");
        // The paid seat itself is preserved (existing reserve semantics): only the owner is gone.
        Check(HeroRecruitment.SeatSide(actor.Archer) == 0 && HeroRecruitment.DescribeForTests().Contains("claims=0")
            && HeroRecruitment.DescribeForTests().Contains(":unbound"), "board-recycle-seat-reserved");

        // Repeated / reentrant postfix: nothing left to clear, no new life is written.
        FastDespawnViaPatch(actor.Go, 0f);
        Check(actor.Embarkee.enabled == true && EmbarkableSim.RegisterEvents == regBefore
            && EmbarkableSim.InactiveRegisterSkips == inactBefore + 1, "board-recycle-repeat-no-op");

        // A reused object (new life) does not inherit the old borrow.
        actor.Go.SetActive(true);
        HeroRecruitment.OnEnable(actor.Archer);
        Check(actor.Embarkee.enabled == true && !HeroBoardingPolicy.BlocksTarget(actor.Embarkee, embarkable)
            && !HeroRecruitment.DescribeForTests().Contains("claims=1"), "board-newlife-not-polluted");
    }

    /// <summary>A former-world object that stays active keeps its release responsibility; once
    /// inactive it may release without a registration, and a destroyed wrapper drops it silently.</summary>
    private static void BoardFormerWorld()
    {
        ResetArchive();
        var host = Host.Create(2, VirginJsonB, isNew: true, days: 0);
        BootVirginIsland(host);
        var payable = BootShop(140f, 70);
        Check(payable != null, "board-formerworld-shop");
        if (payable == null) return;
        var faraway = host.CreateArcher("Faraway", Side.Right);
        HeroArcherRuntime.Observe(faraway.Archer);
        var buy = NativePayment(host, payable, arm: true);
        Check(buy.Gate && faraway.Embarkee.enabled == false, "board-formerworld-fixture-borrowed");

        // The next world: the old object is now active in a former world.
        ReplaceWorldLayer(host);
        Host.AdvanceFrame();
        faraway.Damage.Kill();
        Check(faraway.Embarkee.enabled == false && HeroRecruitment.DescribeForTests().Contains("claims=1"),
            "board-formerworld-active-keeps-responsibility");

        // Inactive same-life release is allowed and does not trigger an active registration.
        int inact = EmbarkableSim.InactiveRegisterSkips;
        faraway.Go.SetActive(false);
        Host.JumpClock(700f, 1500);
        HeroRecruitment.Tick();
        Check(faraway.Embarkee.enabled == true && EmbarkableSim.InactiveRegisterSkips == inact + 1
            && HeroRecruitment.DescribeForTests().Contains("claims=0"), "board-formerworld-inactive-release");

        // A destroyed wrapper drops the responsibility without writing anything.
        var nextPayable = BootShop(1000f, 2100);
        Check(nextPayable != null, "board-destroyed-shop");
        if (nextPayable == null) return;
        var destroyed = host.CreateArcher("Destroyed", Side.Right);
        HeroArcherRuntime.Observe(destroyed.Archer);
        var buy2 = NativePayment(host, nextPayable, arm: true);
        Check(buy2.Gate && destroyed.Embarkee.enabled == false, "board-destroyed-fixture-borrowed");
        int reg = EmbarkableSim.RegisterEvents, unreg = EmbarkableSim.UnregisterEvents;
        destroyed.Character.gameObject = null;   // the wrapper is gone (destroyed object)
        Host.JumpClock(1100f, 2300);
        HeroRecruitment.Tick();
        Check(HeroRecruitment.DescribeForTests().Contains("claims=0")
            && EmbarkableSim.RegisterEvents == reg && EmbarkableSim.UnregisterEvents == unreg,
            "board-destroyed-drops-without-write");
    }

    /// <summary>Load borrows only after Ready + world binding, an old pending target is cleared
    /// natively and read back, and a role successor borrows before the predecessor is released.</summary>
    private static void BoardLoadAndSuccessor()
    {
        ResetArchive();
        var host = Host.Create(4, VirginJsonA, isNew: true, days: 0);
        BootVirginIsland(host);
        var payable = BootShop(1300f, 2600);
        Check(payable != null, "board-load-shop");
        if (payable == null) return;
        var actor = host.CreateArcher("Saved", Side.Right);
        HeroArcherRuntime.Observe(actor.Archer);
        var embarkable = CreateBoat(host).GetComponent<Embarkable>();
        var buy = NativePayment(host, payable, arm: true);
        Check(buy.Gate, "board-load-fixture-purchase");
        SavePaidHero(host, actor, SavedJsonA, "npc-board-hero");

        // An owner that is still embarked is never touched: no borrow, no state change.
        var embarked = host.CreateArcher("EmbarkedReload", Side.Right);
        HeroArcherRuntime.Observe(embarked.Archer);
        embarked.Embarkee.IsEmbarked = true;
        var islandEmb = new IslandSaveData { land = 4, isNew = false, playTimeDays = 3, Json = SavedJsonA };
        var recordEmb = new IslandSaveData.ObjectData { uniqueID = "npc-board-hero", Root = embarked.Persistent };
        recordEmb.componentData2.Add(new IslandSaveData.ObjectData.ComponentData { name = "Character", type = "CharacterData" });
        islandEmb.objects.Add(recordEmb);
        host.Island = islandEmb;
        host.Campaign.CurrentIsland = islandEmb;
        Host.PlaceIsland(host.Campaign, islandEmb, 4);
        int unregEmb = EmbarkableSim.UnregisterEvents;
        var loadEmb = new HeroRecruitment.LoadCapture();
        loadEmb.Begin(islandEmb);
        loadEmb.Capture(recordEmb, embarked.Persistent);
        loadEmb.End(true);
        Check(embarked.Embarkee.enabled == true && embarked.Embarkee.IsEmbarked
            && EmbarkableSim.UnregisterEvents == unregEmb && LogContains("borrow-skip-embarked"),
            "board-load-embarked-untouched");

        var reloaded = host.CreateArcher("Reloaded", Side.Right);
        HeroArcherRuntime.Observe(reloaded.Archer);
        var island = new IslandSaveData { land = 4, isNew = false, playTimeDays = 3, Json = SavedJsonA };
        var record = new IslandSaveData.ObjectData { uniqueID = "npc-board-hero", Root = reloaded.Persistent };
        record.componentData2.Add(new IslandSaveData.ObjectData.ComponentData { name = "Character", type = "CharacterData" });
        island.objects.Add(record);
        host.Island = island;
        host.Campaign.CurrentIsland = island;
        Host.PlaceIsland(host.Campaign, island, 4);
        // An old save can leave a pending, not-embarked target on the owner.
        reloaded.Embarkee.SetEmbarkableTarget(embarkable, -1);
        int unreg = EmbarkableSim.UnregisterEvents, clear = reloaded.Embarkee.ClearTargetCalls;
        var load = new HeroRecruitment.LoadCapture();
        load.Begin(island);
        load.Capture(record, reloaded.Persistent);
        Check(reloaded.Embarkee.enabled && reloaded.Embarkee.EmbarkableTarget != null,
            "board-load-not-borrowed-before-end");
        load.End(true);
        Check(reloaded.Embarkee.enabled == false, "board-load-ready-borrows");
        Check(reloaded.Embarkee.EmbarkableTarget == null && reloaded.Embarkee.ClearTargetCalls == clear + 1,
            "board-load-native-target-cleared");
        Check(EmbarkableSim.UnregisterEvents == unreg + 1, "board-load-native-unregister");
        Check(HeroRecruitment.SeatSide(reloaded.Archer) == 1 && HeroRecruitment.DescribeForTests().Contains(":borrowed"),
            "board-load-seat-bound-and-borrowed");
        Check(!LogContains("borrow-target-uncleared"), "board-load-no-uncleared-log", LastLog());

        var successor = host.CreateWorkerSuccessor("Successor");
        var replacement = new HeroRecruitment.ReplacementCapture();
        replacement.Begin(reloaded.Character);
        replacement.Complete(successor.Character);
        Check(successor.Embarkee.enabled == false && HeroRecruitment.HasPurchasedCareer(successor.Character),
            "board-successor-borrowed-keeps-seat");
        Check(reloaded.Embarkee.enabled == true, "board-successor-releases-predecessor");
        Check(HeroRecruitment.DescribeForTests().Contains(":borrowed"), "board-successor-describe");
    }

    private static GameObject CreateBoat(Host host)
    {
        var boat = new GameObject("Boat");
        boat.transform.SetParent(host.Layer.transform, false);
        boat.AddComponent<Embarkable>();
        return boat;
    }

    /// <summary>
    /// Rebuilds the MOD shop for a freshly created host. A different world layer makes the first
    /// Tick clear the former world's shop and the second one create the current shop; the clock
    /// jump clears the native 5 s recreate backoff left by an earlier segment in this process.
    /// </summary>
    private static PayableComponent BootShop(float time, int frame)
    {
        Host.JumpClock(time, frame);
        HeroShop.Tick();   // clears the former world's shop
        HeroShop.Tick();   // creates the current one
        return FindShopPayable();
    }

    /// <summary>
    /// Switches the runtime world to a fresh layer that carries its own reference native shop,
    /// so the MOD shop can be recreated there while the previous objects stay active in the
    /// former world (their WorldKey no longer matches).
    /// </summary>
    private static void ReplaceWorldLayer(Host host)
    {
        var layer = new GameObject("NextWorldLayer");
        var shopGo = new GameObject("NativeBowShop2");
        shopGo.transform.SetParent(layer.transform, false);
        var body = shopGo.AddComponent<SpriteRenderer>();
        body.sprite = new Sprite { pivot = new Vector2(0.5f, 0f), rect = new Rect(0, 0, 16, 16), pixelsPerUnit = 32f };
        var shop = shopGo.AddComponent<PayableShop>();
        var tag = shopGo.AddComponent<ShopTag>();
        tag.type = PayableShop.ShopType.Bow;
        Managers.Inst.payables.Items.Add(shop);
        host.Layer = layer;
        Managers.Inst.world.gameLayer = layer.transform;
    }

    /// <summary>Models the Harmony prefix contract: a false prefix skips the original method.</summary>
    private static bool CallThroughTargetHook(Embarkee embarkee, Embarkable target)
    {
        if (!HookTargetPrefixAllows(embarkee, target)) return false;
        embarkee.SetEmbarkableTarget(target, -1);
        return true;
    }

    private static bool CallThroughEmbarkHook(Embarkee embarkee)
    {
        if (!HookEmbarkPrefixAllows(embarkee)) return false;
        embarkee.Embark();
        return true;
    }

    private static bool HookTargetPrefixAllows(Embarkee embarkee, Embarkable target)
    {
        var method = typeof(HeroBoardingPolicy.TargetPatch).GetMethod("Before",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (bool)method.Invoke(null, new object[] { embarkee, target });
    }

    private static bool HookEmbarkPrefixAllows(Embarkee embarkee)
    {
        var method = typeof(HeroBoardingPolicy.EmbarkPatch).GetMethod("Before",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (bool)method.Invoke(null, new object[] { embarkee });
    }

    private static void FastDespawnViaPatch(GameObject root, float delay)
    {
        var before = typeof(HeroRecruitment.DespawnPatch).GetMethod("Before",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var after = typeof(HeroRecruitment.DespawnPatch).GetMethod("After",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        object[] args = { root, delay, null };
        before.Invoke(null, args);
        after.Invoke(null, new[] { args[2] });
    }

    private static object LiveCandidate(Character character)
    {
        var field = typeof(HeroRecruitment).GetField("Candidates",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var records = (System.Collections.IDictionary)field.GetValue(null);
        return records[character.Pointer];
    }

    // ---------------- shared helpers ----------------

    private static void ResetArchive()
    {
        string dir = Path.Combine(OutRoot, "repro-config");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private static void BootVirginIsland(Host host)
    {
        var virgin = new HeroRecruitment.VirginCapture();
        virgin.Begin(host.Campaign);
        virgin.Complete(host.Campaign);
    }

    /// <summary>
    /// Models the native 2.4 order for one payment attempt:
    /// TransactionStarted callback -> coins leave the wallet into floating slots ->
    /// Completed re-check (native CanPay) -> then either
    ///   armed &amp; accepted: TransactionComplete -> owner OnPay -> native consume/clear
    ///   (RVA 69b981..69ba9a), or
    ///   refused: native CancelTransaction + DropFloatingCurrency, which returns the coins
    ///   (PatchWorld_SpecialTowerRebuild.cs:1095-1097 documents that refused path).
    /// </summary>
    private static NativePaymentResult NativePayment(Host host, PayableComponent payable, bool arm)
    {
        var player = host.Player;
        var result = new NativePaymentResult();
        player.selectedPayable = payable;
        player._payState = Player.PayState.Transaction;
        if (arm) payable.InvokeTransactionStarted(player);
        player.wallet.Coins -= HeroShop.Price;
        for (int i = 0; i < HeroShop.Price; i++) player._floatingCurrency.Add(new Currency());
        player._payState = Player.PayState.Completed;
        player._completingPayable = payable;
        result.FloatingAtGate = player._floatingCurrency.Count;
        result.Gate = payable.Owner.CanPay(player);
        if (result.Gate)
        {
            payable.Owner.OnPay(player);
            player.ConsumedFloating += player._floatingCurrency.Count;
            player._floatingCurrency.Clear();
            result.Consumed = true;
        }
        else
        {
            player.CancelTransaction();
            player.DropFloatingCurrency();
            result.Cancelled = true;
        }
        player._completingPayable = null;
        player._payState = Player.PayState.None;
        player.selectedPayable = null;
        return result;
    }

    private static void SavePaidHero(Host host, Actor actor, string savedJson, string nativeId)
    {
        host.Island.Json = savedJson;
        var record = new IslandSaveData.ObjectData { uniqueID = nativeId, Root = actor.Persistent };
        record.componentData2.Add(new IslandSaveData.ObjectData.ComponentData { name = "Character", type = "CharacterData" });
        host.Island.objects.Add(record);
        IslandSaveData.CurrentlySavingIsland = host.Island;
        IslandSaveData.isSavingGame = true;
        var capture = new HeroRecruitment.SaveCapture { Campaign = 1, Land = host.Island.land, Challenge = 0 };
        capture.Capture(actor.Persistent, nativeId);
        capture.MarkerSeen = true; // stand-in for UpdateSavedWithRevisions normal tail
        capture.Apply();
        IslandSaveData.isSavingGame = false;
        IslandSaveData.CurrentlySavingIsland = null;
    }

    /// <summary>Fixture: one paid legacy scope owned by no context (unclaimed old history).</summary>
    private static void WriteUnclaimedPaidFixture(string archivePath)
    {
        var disk = HeroRecruitmentArchiveStore.Load(archivePath);
        string scope = HeroRecruitmentArchive.NewScope();
        string hash = HeroRecruitmentArchive.Hash(OldHistoryJson, scope);
        var seats = new List<HeroPurchaseReceipt>
        {
            new HeroPurchaseReceipt { Id = Guid.NewGuid(), Side = 1, NativeId = "npc-old-ghost" },
        };
        if (!disk.Archive.Record(scope, hash, seats, HeroRecruitmentArchive.HashKindLegacy, legacyV1: true))
            throw new InvalidOperationException("fixture scope refused");
        if (!HeroRecruitmentArchiveStore.Save(archivePath, disk, disk.Archive))
            throw new InvalidOperationException("fixture save refused");
    }

    private static PayableComponent FindShopPayable()
    {
        foreach (var pair in NetworkPostbox.Instance.MasterSemiCRPCHLookup)
            if (pair.Key != null && pair.Key.name == "KEM_HeroShop")
                return pair.Key.GetComponent<PayableComponent>();
        return null;
    }

    /// <summary>Coins still held by the player: wallet + floating slots + coins dropped in the scene.</summary>
    private static int Held(Host host)
        => host.Player.wallet.Coins + host.Player._floatingCurrency.Count + host.Player.DroppedFloating;

    private static string Sha(string path)
    {
        if (!File.Exists(path)) return "missing";
        using var stream = File.OpenRead(path);
        using var hash = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(hash.ComputeHash(stream)).ToLowerInvariant();
    }

    private static bool LogContains(string fragment)
    {
        foreach (var line in KingdomEnhancedPlugin.Instance.LogSource.Lines)
            if (line.Contains(fragment)) return true;
        return false;
    }

    private static string LastLog()
    {
        var lines = KingdomEnhancedPlugin.Instance.LogSource.Lines;
        return lines.Count > 0 ? lines[lines.Count - 1] : "";
    }

    private static HeroRecruitmentContext ContextOf(string path, string key)
    {
        var disk = HeroRecruitmentArchiveStore.Load(path);
        if (disk.Archive == null || !disk.Archive.TryGetContext(key, out var context)) return null;
        return context;
    }

    private static string EpochOf(string key)
        => ContextOf(HeroRecruitment.ArchivePath, key)?.Active ?? "";

    private static string BaselineOf(string key)
    {
        var disk = HeroRecruitmentArchiveStore.Load(HeroRecruitment.ArchivePath);
        if (disk.Archive == null || !disk.Archive.TryGetContext(key, out var context)) return "";
        return disk.Archive.Baselines.TryGetValue(context.Active, out var baseline) ? baseline : "";
    }

    private static string PaidSnapshotHash(string key, string nativeId)
    {
        var disk = HeroRecruitmentArchiveStore.Load(HeroRecruitment.ArchivePath);
        if (disk.Archive == null || !disk.Archive.TryGetContext(key, out var context)) return "";
        if (!disk.Archive.Scopes.TryGetValue(context.Active, out var snapshots)) return "";
        foreach (var snapshot in snapshots)
            foreach (var seat in snapshot.Seats)
                if (seat.NativeId == nativeId) return snapshot.Hash;
        return "";
    }

    private static string ReceiptIdOf(string key)
    {
        var context = ContextOf(HeroRecruitment.ArchivePath, key);
        if (context == null) return "";
        var disk = HeroRecruitmentArchiveStore.Load(HeroRecruitment.ArchivePath);
        if (disk.Archive == null || !disk.Archive.Scopes.TryGetValue(context.Active, out var snapshots)) return "";
        foreach (var snapshot in snapshots)
            foreach (var seat in snapshot.Seats)
                return seat.Id.ToString("N");
        return "";
    }

    /// <summary>Canonical frozen serialization of one context's active scope.</summary>
    private static string CanonicalScope(string path, string key, bool includeBaseline)
    {
        var disk = HeroRecruitmentArchiveStore.Load(path);
        if (disk.Archive == null || !disk.Archive.TryGetContext(key, out var context)) return "<missing-context>";
        var builder = new StringBuilder();
        builder.Append("epoch=").Append(context.Active);
        if (includeBaseline)
            builder.Append("|baseline=").Append(disk.Archive.Baselines.TryGetValue(context.Active, out var baseline) ? baseline : "");
        if (!disk.Archive.Scopes.TryGetValue(context.Active, out var snapshots)) return builder.Append("|no-scope").ToString();
        foreach (var snapshot in snapshots)
        {
            builder.Append("|snap:").Append(snapshot.Hash).Append('/').Append(snapshot.HashKind).Append('/').Append(snapshot.LegacyV1);
            foreach (var seat in snapshot.Seats)
                builder.Append("/seat:").Append(seat.Id).Append(',').Append(seat.Side).Append(',').Append(seat.NativeId);
        }
        return builder.ToString();
    }

    private static bool ArchiveHasContext(string path, string key)
    {
        var disk = HeroRecruitmentArchiveStore.Load(path);
        return disk.Archive != null && disk.Archive.TryGetContext(key, out _);
    }

    private static bool ArchiveHasReceipt(string path, string key, string nativeId, out string detail)
    {
        detail = "";
        var disk = HeroRecruitmentArchiveStore.Load(path);
        if (disk.Archive == null) { detail = "archive unreadable status=" + disk.Status; return false; }
        if (!disk.Archive.TryGetContext(key, out var context)) { detail = "context missing"; return false; }
        if (!disk.Archive.Scopes.TryGetValue(context.Active, out var snapshots)) { detail = "scope missing"; return false; }
        int found = 0;
        foreach (var snapshot in snapshots)
            foreach (var seat in snapshot.Seats)
                if (seat.NativeId == nativeId) found++;
        detail = "scope=" + Short(context.Active) + " epochs=" + context.Epochs.Count + " matches=" + found
            + " baseline=" + (disk.Archive.Baselines.TryGetValue(context.Active, out var baseline) ? Short(baseline) : "none");
        return found == 1;
    }

    private static string Short(string value)
        => string.IsNullOrEmpty(value) || value.Length < 8 ? value : value.Substring(0, 8);

    private static void WriteResultJson(int failed)
    {
        string path = Path.Combine(OutRoot, "result.json");
        var root = new Dictionary<string, object>();
        var scenarios = new Dictionary<string, object>();
        if (File.Exists(path))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Name == "scenarios")
                    {
                        foreach (var scenario in property.Value.EnumerateObject())
                            scenarios[scenario.Name] = scenario.Value.Clone();
                    }
                    else
                    {
                        root[property.Name] = property.Value.Clone();
                    }
                }
            }
            catch { }
        }
        var checks = new List<object>();
        foreach (var check in Checks)
            checks.Add(new Dictionary<string, object> { ["id"] = check.Id, ["ok"] = check.Ok, ["detail"] = check.Detail });
        scenarios[_scenario] = new Dictionary<string, object>
        {
            ["ok"] = failed == 0,
            ["total"] = Checks.Count,
            ["failed"] = failed,
            ["checks"] = checks,
        };
        root["scenarios"] = scenarios;
        root["boundaryStubs"] = BoundaryStubs;
        root["productionChain"] = new[]
        {
            "$(SrcRoot)/il2cpp/HeroShop.cs (HeroShop.OnPay)",
            "$(SrcRoot)/il2cpp/HeroRecruitment.cs (HeroRecruitment.TryPurchase, VirginCapture/LoadCapture/SaveCapture, embarkee borrow/release)",
            "$(SrcRoot)/il2cpp/HeroArcherRuntime.cs (HeroArcherRuntime.TryActivatePurchased, purchase embarkee readiness)",
            "$(SrcRoot)/il2cpp/HeroBoardingPolicy.cs (Embarkee.SetEmbarkableTarget / Embark targeted exclusion)",
        };
        root["note"] = "Offline reproduction. Boundary stubs are not game behaviour evidence; no game process was started.";
        File.WriteAllText(path, JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static readonly string[] BoundaryStubs =
    {
        "UnityEngine.* (Object/Component/GameObject/Transform/Sprite/SpriteRenderer/Texture2D/ImageConversion/Time/Mathf/JsonUtility) - engine value types and lifecycle only",
        "Il2CppInterop.Runtime.Injection.ClassInjector + Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp - registration shims; no native type injected",
        "Il2CppSystem.Action<T> + Il2CppSystem.Collections.Generic.List<T> - delegate/list ABI shims",
        "IPayableComponentOwner bridge (OwnerInterfaceBridges) - same name/arity bridge the ClassInjector performs; HeroShopOwnerInterop.WriteUnlocked still runs for real",
        "NetworkPostbox/CRPCHeader/CRPCStamp - SemiStatic registration tables matching production HeaderMatches reads",
        "PayableComponent/PayableShop/ShopTag/PayableManager - native payment component surface; InvokeTransactionStarted models native Payable.TransactionStarted dispatch",
        "Player/Wallet/Currency - pay-state, floating slots and wallet surface; wallet capacity not modelled. DropFloatingCurrency drops the floating coins into the scene (Player.DroppedFloating; wallet untouched); NativePickupDropped is a separate labelled step and credits via Wallet.NativePickupCredit (PickupCalls), distinct from a mod AddCurrency refund (Wallet.AddCalls)",
        "IslandSaveData/GlobalSaveData/CampaignSaveData/Persistent/Character/Damageable/Archer - game type surface used by the real recruitment load/save captures; CampaignSaveData.carryForward.present is the native carry field",
        "Embarkee/Embarkable/EmbarkableSim - boarding component surface: enabled models the native OnDisable (unregister + clear pending target + redistribute) and OnEnable (immediate register when active, deferred when inactive); EmbarkableSim is the registrar bookkeeping stand-in (membership/counters only, no scoring or slots). The two HeroBoardingPolicy prefixes are invoked exactly as Harmony would (false = original skipped) and are asserted side-effect free",
        "HeroArcherVisuals - engine-side visual gate: production decides Apply/Remove; VisualProducible controls whether the engine can produce the sprite (success/failure scenario)",
        "HeroArcherRange/HeroArcherMovement/HeroArcherGuardFacing/HeroArcherLiveDiagnostics - effect slices outside the purchase control chain",
        "HeavyShieldIdentity/HeavyShieldPersistence/MusketeerIdentity/PatchRoles_* - unrelated slice predicates, constant false",
        "BepInEx.Paths/HarmonyLib attributes/ModConfig/KingdomEnhancedPlugin - host/config/log boundaries; no Harmony patch is applied",
    };
    private static void ReviewerProbe()
    {
        ResetArchive();
        var host = Host.Create(1, VirginJsonA, isNew: true, days: 0);
        BootVirginIsland(host);
        var payable = BootShop(2000f, 4000);
        var actor = host.CreateArcher("ReviewerActor", Side.Right);
        HeroArcherRuntime.Observe(actor.Archer);
        if (_scenario == "review-postwrite-throw")
        {
            EmbarkableSim.OnNativeDisableProbe = _ => { throw new InvalidOperationException("review post-write failure"); };
            var buy = NativePayment(host, payable, arm: true);
            EmbarkableSim.OnNativeDisableProbe = null;
            // Revised contract: the write may happen, but the failed purchase must synchronously
            // release its claim before returning to native payment cancellation.
            Check(buy.Gate && buy.Consumed && host.Player.wallet.Coins == 20
                && HeroRecruitment.SeatSide(actor.Archer) == 0,
                "review-postwrite-fault-refunded-payment", HeroRecruitment.DescribeForTests());
            Check(actor.Embarkee.enabled && EmbarkableSim.IsRegistered(actor.Embarkee)
                && HeroRecruitment.DescribeForTests().Contains("claims=0"),
                "review-postwrite-failure-keeps-return-responsibility", HeroRecruitment.DescribeForTests());
        }
        else if (_scenario == "review-reentrant-unbind")
        {
            EmbarkableSim.OnNativeDisableProbe = _ => actor.Damage.Kill();
            var buy = NativePayment(host, payable, arm: true);
            EmbarkableSim.OnNativeDisableProbe = null;
            Check(HeroRecruitment.SeatSide(actor.Archer) == 0, "review-reentrant-death-removed-seat");
            Check(actor.Embarkee.enabled && EmbarkableSim.IsRegistered(actor.Embarkee)
                && HeroRecruitment.DescribeForTests().Contains("claims=0"),
                "review-reentrant-unbind-restores-embarkee", HeroRecruitment.DescribeForTests());
        }
        else if (_scenario == "review-pending-despawn")
        {
            Check(NativePayment(host, payable, arm: true).Gate, "review-fixture-paid");
            ReplaceWorldLayer(host);
            actor.Damage.Kill();
            Check(HeroRecruitment.DescribeForTests().Contains("claims=1"), "review-oldworld-retained-claim");
            actor.Go.SetActive(false);
            var before = typeof(HeroRecruitment.DespawnPatch).GetMethod("Before",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var after = typeof(HeroRecruitment.DespawnPatch).GetMethod("After",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            object[] frozen = { actor.Go, 0f, null };
            before.Invoke(null, frozen);
            FastDespawnViaPatch(actor.Go, 0f);
            Check(actor.Embarkee.enabled == true, "review-seatless-pending-despawn-returns-before-reuse", HeroRecruitment.DescribeForTests());
            actor.Go.transform.SetParent(host.Layer.transform, false);
            actor.Damage.isDead=false;
            actor.Go.SetActive(true);
            HeroRecruitment.OnEnable(actor.Archer);
            HeroRecruitment.Observe(actor.Archer);
            object newLife = LiveCandidate(actor.Character);
            after.Invoke(null, new[] { frozen[2] });
            Check(actor.Embarkee.enabled == true, "review-native-same-root-pool-life-not-disabled");
            Check(newLife != null && ReferenceEquals(newLife, LiveCandidate(actor.Character)),
                "review-stale-postfix-preserves-new-candidate");
        }
        else if (_scenario == "review-existing-claim-recheck")
        {
            Check(NativePayment(host, payable, arm: true).Gate, "review-existing-claim-fixture-paid");
            object candidate = LiveCandidate(actor.Character);
            var borrowMethod = typeof(HeroRecruitment).GetMethod("BorrowEmbarkee",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            actor.Embarkee.enabled = true; // native or third-party re-enable, without changing the receipt
            int unreg = EmbarkableSim.UnregisterEvents;
            bool reborrow = (bool)borrowMethod.Invoke(null, new[] { candidate });
            Check(!reborrow && actor.Embarkee.enabled && EmbarkableSim.UnregisterEvents == unreg,
                "review-stale-claim-does-not-claim-native-reenable");
            actor.Embarkee.enabled = false;
            actor.Embarkee.EmbarkableTarget = CreateBoat(host).GetComponent<Embarkable>();
            Check(!(bool)borrowMethod.Invoke(null, new[] { candidate }),
                "review-stale-claim-target-not-confirmed");
        }
        else if (_scenario == "review-return-reentrant-newclaim")
        {
            Check(NativePayment(host, payable, arm: true).Gate, "review-return-fixture-paid");
            object candidate = LiveCandidate(actor.Character);
            var claimField = candidate.GetType().GetField("Embarkee",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            object oldClaim = claimField.GetValue(candidate);
            object newClaim = System.Activator.CreateInstance(oldClaim.GetType(), nonPublic: true);
            EmbarkableSim.OnNativeEnableProbe = _ => claimField.SetValue(candidate, newClaim);
            var retireMethod = typeof(HeroRecruitment).GetMethod("RetireCandidate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            bool retired = (bool)retireMethod.Invoke(null, new[] { candidate });
            EmbarkableSim.OnNativeEnableProbe = null;
            Check(!retired && ReferenceEquals(claimField.GetValue(candidate), newClaim)
                && ReferenceEquals(LiveCandidate(actor.Character), candidate),
                "review-return-reentry-preserves-new-claim-and-candidate");
        }
        else if (_scenario == "review-component-replaced")
        {
            Check(NativePayment(host, payable, arm: true).Gate, "review-fixture-paid");
            var oldCandidate = LiveCandidate(actor.Character);
            var oldEmbarkee = actor.Embarkee;
            var newEmbarkee = actor.Go.AddComponent<Embarkee>();
            newEmbarkee._owner = actor.Archer;
            bool remapped = actor.Go.ReplaceQueryableComponent(oldEmbarkee, newEmbarkee);
            Check(remapped && newEmbarkee.Pointer != oldEmbarkee.Pointer
                && ReferenceEquals(actor.Go.GetComponent<Embarkee>(), newEmbarkee)
                && oldEmbarkee.gameObject == actor.Go && !oldEmbarkee.enabled,
                "review-replacement-query-points-to-new-live-component");
            actor.Damage.Kill();
            Check(!oldEmbarkee.enabled && HeroRecruitment.DescribeForTests().Contains("claims=1"),
                "review-replaced-live-component-retains-claim", HeroRecruitment.DescribeForTests());
            var retireMethod = typeof(HeroRecruitment).GetMethod("RetireCandidate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            bool retired = (bool)retireMethod.Invoke(null, new[] { oldCandidate });
            Check(!retired && ReferenceEquals(LiveCandidate(actor.Character), oldCandidate)
                && !oldEmbarkee.enabled && ReferenceEquals(actor.Go.GetComponent<Embarkee>(), newEmbarkee),
                "review-pointer-mismatch-keeps-old-candidate-and-claim", HeroRecruitment.DescribeForTests());
        }
    }

}
