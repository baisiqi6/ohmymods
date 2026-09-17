using System.Reflection;
using System.Collections;
using System.Text;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    static int passed;
    static string Root=Path.Combine(Path.GetTempPath(),"kem-hero-recruitment-"+Guid.NewGuid().ToString("N"));
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);passed++;}
    static string H(string value)=>HeroRecruitmentArchive.Hash(value,"test");
    static HeroPurchaseReceipt Receipt(int side,string id="native-1")=>new(){Id=Guid.NewGuid(),Side=side,NativeId=id};
    static void Main()
    {
        Directory.CreateDirectory(Root);
        try { ArchiveTests(); RuntimeTests(); SeatDisplayTests(); ContextTests(); Console.WriteLine($"PASS {passed} assertions (synthetic fixtures; production recruitment runtime and archive)"); }
        finally {Directory.Delete(Root,true);}
    }
    static void ArchiveTests()
    {
        var archive=new HeroRecruitmentArchive();var left=Receipt(-1);var right=Receipt(1,"native-2");
        Check(archive.Record(H("island1"), H("paid"), new[]{left,right}, HeroRecruitmentArchive.HashKindLegacy, true),"two seats accepted");
        Check(archive.TryGet(H("island1"),H("paid"),out var snap)&&snap.Seats.Count==2,"exact restore");
        Check(!archive.TryGet(H("island2"),H("paid"),out _),"other island isolated");
        Check(!archive.TryGet(H("island1"),H("vanilla-resave"),out _),"vanilla resave no guessed restore");
        Check(archive.LatestReservations(H("island1")).Count==2,"unknown source conservatively retains prepared purchases");
        Check(archive.ConfirmBaseline(H("island1"), H("paid"), new[]{left,right}, HeroRecruitmentArchive.HashKindLegacy, true),"native load confirms baseline");
        Check(archive.LatestReservations(H("island1")).Count==2&&archive.LatestReservations(H("island1")).All(x=>x.NativeId==""),"mismatch retains confirmed seats without owner guess");
        Check(!archive.Record(H("island1"), H("paid"), new[]{left}, HeroRecruitmentArchive.HashKindLegacy, true),"same snapshot conflicting purchase forbidden");
        Check(!archive.Record(H("island1"), H("duplicate-side"), new[]{left,Receipt(-1,"other")}, HeroRecruitmentArchive.HashKindLegacy, true),"duplicate side rejected");
        Check(!archive.Record(H("island1"), H("duplicate-owner"), new[]{left,Receipt(1)}, HeroRecruitmentArchive.HashKindLegacy, true),"duplicate native owner rejected");
        var duplicate=left.Copy();duplicate.Side=1;duplicate.NativeId="x";
        Check(!archive.Record(H("island1"), H("duplicate-guid"), new[]{left,duplicate}, HeroRecruitmentArchive.HashKindLegacy, true),"duplicate GUID rejected");
        Check(archive.Record(H("island1"), H("death"), Array.Empty<HeroPurchaseReceipt>(), HeroRecruitmentArchive.HashKindLegacy, true),"empty snapshot clears dead seats");
        var decoded=HeroRecruitmentArchive.Decode(archive.Encode(),out bool future);
        Check(!future&&decoded.TryGet(H("island1"),H("death"),out var empty)&&empty.Seats.Count==0,"empty death state roundtrip");
        Check(decoded.Encode().SequenceEqual(archive.Encode()),"deterministic encoding");
        for(int i=0;i<12;i++)Check(archive.Record(H("island1"), H("history"+i), new[]{left}, HeroRecruitmentArchive.HashKindLegacy, true),"history append");
        Check(archive.Scopes[H("island1")].Count==8,"bounded snapshot history");
        Check(archive.TryGet(H("island1"),H("paid"),out _),"confirmed baseline not evicted by uncommitted history");
        Check(archive.Record(H("islandX"), H("paid"), new[]{left}, HeroRecruitmentArchive.HashKindLegacy, true),"second scope recorded");
        string ctx=HeroRecruitmentArchive.ContextKey("global-v35",1,0,1);
        Check(archive.EnsureContext(ctx,H("island1"),true),"context registered to its first epoch");
        Check(archive.EnsureContext(ctx,H("islandX"),true)&&archive.Contexts[ctx].Epochs.Count==2,"confirmed generation appends an epoch");
        Check(archive.Contexts[ctx].Active==H("islandX"),"newest epoch is active");
        Check(archive.EnsureContext(ctx,H("island1"),false)&&archive.Contexts[ctx].Active==H("island1"),"older epoch rollback switches active");
        Check(archive.Contexts[ctx].Epochs.SequenceEqual(new[]{H("island1"),H("islandX")}),"newer epoch kept for rollback");
        Check(!archive.EnsureContext(ctx,H("island2"),false)&&!archive.EnsureContext(ctx,H("island2"),true),"epoch without a recorded scope refused");
        Check(!archive.EnsureContext(ctx,"not-a-hash",true),"invalid context key refused");
        Check(archive.Record(H("islandZ"),H("paid"),new[]{left},HeroRecruitmentArchive.HashKindLegacy,true),"third scope recorded");
        Check(archive.EnsureContext(HeroRecruitmentArchive.ContextKey("global-v35",1,0,2),H("islandZ"),false),"other land is an independent context");
        var contextRoundtrip=HeroRecruitmentArchive.Decode(archive.Encode(),out _);
        Check(contextRoundtrip.Contexts[ctx].Active==H("island1")&&contextRoundtrip.Contexts[ctx].Epochs.Count==2,"contexts roundtrip");
        // Provenance and hash function are part of a snapshot's identity, and epochs are single-owner.
        Check(!archive.Record(H("islandX"),H("paid"),new[]{left},HeroRecruitmentFingerprint.Kind,true),"same hash with a different kind refused");
        Check(!archive.Record(H("islandX"),H("paid"),new[]{left},HeroRecruitmentArchive.HashKindLegacy,false),"same hash with different provenance refused");
        Check(archive.Record(H("islandY"),H("paid"),new[]{left},HeroRecruitmentFingerprint.Kind,false),"kind-2 snapshot accepted");
        Check(archive.TryGet(H("islandY"),H("paid"),out var kinded)&&kinded.HashKind==HeroRecruitmentFingerprint.Kind&&!kinded.LegacyV1,"kind-2 provenance roundtrip");
        Check(!archive.EnsureContext(HeroRecruitmentArchive.ContextKey("global-v35",1,0,3),H("island1"),true),"epoch owned by another context is never migrated");
        string capContext=HeroRecruitmentArchive.ContextKey("global-v35",1,0,4);
        for(int i=0;i<HeroRecruitmentArchive.MaxEpochs;i++)
        {
            string scope=HeroRecruitmentArchive.NewScope();
            Check(archive.Record(scope,H("cap"),Array.Empty<HeroPurchaseReceipt>(),HeroRecruitmentArchive.HashKindLegacy,true),"cap scope recorded");
            Check(archive.EnsureContext(capContext,scope,true),"cap epoch registered");
        }
        string extra=HeroRecruitmentArchive.NewScope();
        Check(archive.Record(extra,H("cap"),Array.Empty<HeroPurchaseReceipt>(),HeroRecruitmentArchive.HashKindLegacy,true),"extra scope recorded");
        Check(!archive.EnsureContext(capContext,extra,true)&&archive.Contexts[capContext].Epochs.Count==HeroRecruitmentArchive.MaxEpochs,"epoch cap fails closed instead of dropping history");
        Check(archive.Scopes.ContainsKey(extra),"cap failure preserves every scope");
        Check(HeroRecruitmentArchive.Decode(archive.Encode(),out _).Contexts[capContext].Epochs.Count==HeroRecruitmentArchive.MaxEpochs,"capped context roundtrips");
        string valid=Encoding.UTF8.GetString(archive.Encode());
        HeroRecruitmentArchive.Decode(Encoding.UTF8.GetBytes(valid.Replace("\"schemaVersion\":2","\"schemaVersion\":3")),out future);
        Check(future,"future version detected");
        foreach(var invalid in new[]{valid.Replace("\"schemaVersion\":2","\"schemaVersion\":2,\"schemaVersion\":2"),valid.Replace("\"schemaVersion\":2","\"schemaVersion\":2,\"extra\":true"),"{}"})
        {bool rejected=false;try{HeroRecruitmentArchive.Decode(Encoding.UTF8.GetBytes(invalid),out _);}catch{rejected=true;}Check(rejected,"malformed schema rejected");}
        {bool rejected=false;try{HeroRecruitmentArchive.Decode(Encoding.UTF8.GetBytes(valid.Replace("\"contexts\":[","\"contextGroup\":[")),out _);}catch{rejected=true;}Check(rejected,"v2 without contexts rejected");}
        {bool dangling=false;try{HeroRecruitmentArchive.Decode(Encoding.UTF8.GetBytes(valid.Replace("\"epochs\":[\""+H("island1")+"\",\""+H("islandX")+"\"]","\"epochs\":[\""+H("island1")+"\",\""+new string('a',64)+"\"]")),out _);}catch{dangling=true;}Check(dangling,"epoch without a stored scope is corrupt");}
        {bool badKind=false;try{HeroRecruitmentArchive.Decode(Encoding.UTF8.GetBytes(valid.Replace("\"hashKind\":1","\"hashKind\":3")),out _);}catch{badKind=true;}Check(badKind,"unknown hash kind is corrupt");}
        {bool noKind=false;try{HeroRecruitmentArchive.Decode(Encoding.UTF8.GetBytes(valid.Replace("\"hashKind\":1,","")),out _);}catch{noKind=true;}Check(noKind,"missing hashKind is corrupt");}
        {bool noLegacy=false;try{HeroRecruitmentArchive.Decode(Encoding.UTF8.GetBytes(valid.Replace(",\"legacyV1\":true","")),out _);}catch{noLegacy=true;}Check(noLegacy,"missing legacyV1 is corrupt");}
        {var duplicateContext=System.Text.Json.Nodes.JsonNode.Parse(valid);var contexts=duplicateContext["contexts"].AsArray();var other=contexts.First(x=>x["context"].GetValue<string>()!=ctx);other["active"]=H("island1");other["epochs"]=new System.Text.Json.Nodes.JsonArray(H("island1"));bool shared=false;try{HeroRecruitmentArchive.Decode(Encoding.UTF8.GetBytes(duplicateContext.ToJsonString()),out _);}catch{shared=true;}Check(shared,"two contexts sharing an epoch is corrupt");}
        string v1Text="{\"schemaVersion\":1,\"scopes\":[{\"scope\":\""+H("only")+"\",\"baseline\":\"\",\"snapshots\":[{\"hash\":\""+H("paid")+"\",\"seats\":[{\"id\":\"11111111111111111111111111111111\",\"side\":-1,\"nativeId\":\"n1\"}]}]}]}";
        var v1Decoded=HeroRecruitmentArchive.Decode(Encoding.UTF8.GetBytes(v1Text),out bool v1Future);
        Check(!v1Future&&v1Decoded.Scopes.Count==1&&v1Decoded.Contexts.Count==0,"legacy v1 file accepted without contexts");
        string path=Path.Combine(Root,"archive.json");var missing=HeroRecruitmentArchiveStore.Load(path);
        Check(missing.Writable&&missing.Status==HeroRecruitmentArchiveStore.State.Missing,"missing archive initializable");
        Check(HeroRecruitmentArchiveStore.Save(path,missing,archive),"initial atomic save");
        var read=HeroRecruitmentArchiveStore.Load(path);Check(read.Writable,"read written archive");
        Check(!HeroRecruitmentArchiveStore.Save(path,missing,archive),"stale writer refused");
        Check(HeroRecruitmentArchiveStore.Save(path,read,archive),"replacement save makes backup");
        byte[] backup=File.ReadAllBytes(path+".bak");File.WriteAllText(path,"broken");
        var recovered=HeroRecruitmentArchiveStore.Load(path);Check(recovered.RecoveredBackup&&!recovered.Writable,"corrupt main valid backup readonly");
        Check(!HeroRecruitmentArchiveStore.Save(path,recovered,archive)&&File.ReadAllBytes(path+".bak").SequenceEqual(backup),"corrupt primary never destroys valid backup");
        File.WriteAllText(path,"{\"schemaVersion\":99,\"scopes\":[]}");
        Check(HeroRecruitmentArchiveStore.Load(path).Status==HeroRecruitmentArchiveStore.State.Unsupported,"future main not downgraded to backup");
    }

    static void Reset(bool keepFile=false)
    {
        foreach(string name in new[]{"Candidates","Islands","Logged","BoundRoots"})
        {
            object field=typeof(HeroRecruitment).GetField(name,BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
            field.GetType().GetMethod("Clear").Invoke(field,null);
        }
        foreach(string name in new[]{"_current","_load","_save"})typeof(HeroRecruitment).GetField(name,BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,null);
        typeof(HeroRecruitment).GetField("_nextSweep",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,0f);
        BepInEx.Paths.ConfigPath=Path.Combine(Root,"config");
        if(!keepFile&&Directory.Exists(BepInEx.Paths.ConfigPath))Directory.Delete(BepInEx.Paths.ConfigPath,true);
        Managers.Inst=new(){world=new(){gameLayer=new GameObject().Add(new Transform())}};
        GlobalSaveData.filename="global-v35";
        GlobalSaveData.loaded=new(){currentCampaign=1};CampaignSaveData.current=new(){CurrentIsland=new(){land=1}};
        HeroArcherRuntime.Enabled=true;HeroArcherRuntime.ActivateSuccess=true;HeroArcherNetwork.AllowsLocalHero=true;Time.time+=10;Time.unscaledTime+=10;
        Time.frameCount++;HeroRecruitment.Tick();
        if(!keepFile)Load("before-purchase");
    }
    static (Character c,Archer a,Persistent p) Actor(int side,bool archer=true)
    {
        var root=new GameObject();var damage=root.Add(new Damageable());var character=root.Add(new Character{_damageable=damage});
        var a=archer?root.Add(new Archer{side=side}):null;var persistent=root.Add(new Persistent());return(character,a,persistent);
    }
    static IslandSaveData.ObjectData Record(string id)=>new(){uniqueID=id,componentData2=new(){new(){name="Character",type="CharacterData"},new(){name="Archer",type="ArcherData"}}};
    static void Save(string json,params (string id,Persistent p)[] owners)
    {
        var island=CampaignSaveData.current.CurrentIsland;island.Json=Raw(json);island.objects=owners.Select(x=>Record(x.id)).ToList();
        IslandSaveData.CurrentlySavingIsland=island;IslandSaveData.isSavingGame=true;
        var capture=new HeroRecruitment.SaveCapture{Campaign=1,Land=island.land,Challenge=0};
        foreach(var pair in owners)capture.Capture(pair.p,pair.id);
        capture.Apply();IslandSaveData.CurrentlySavingIsland=null;IslandSaveData.isSavingGame=false;
    }
    // Test islands must be valid JSON: the kind-2 fingerprint refuses opaque labels exactly like the
    // real JsonUtility output it is fed at runtime.
    static string Raw(string label)=>label.Length>0&&label[0]=='{'?label:"{\"label\":\""+label+"\"}";
    static void Load(string json,params (string id,Persistent p)[] owners)
    {
        var island=CampaignSaveData.current.CurrentIsland;island.Json=Raw(json);island.isNew=false;island.objects=owners.Select(x=>Record(x.id)).ToList();
        var capture=new HeroRecruitment.LoadCapture();capture.Begin(island);
        for(int i=0;i<owners.Length;i++)capture.Capture(island.objects[i],owners[i].p);
        capture.End(true);
    }
    static HeroRecruitmentArchive Disk()=>HeroRecruitmentArchiveStore.Load(HeroRecruitment.ArchivePath).Archive;
    static void Generate(int land,string json)
    {
        CampaignSaveData.current.CurrentIsland=new(){land=land,isNew=true,playTimeDays=0,Json=Raw(json)};
        var virgin=new HeroRecruitment.VirginCapture();virgin.Begin(CampaignSaveData.current);virgin.Complete(CampaignSaveData.current);
    }
    static void RecycleHook(GameObject root,float delay,bool nativeRecycled,bool postfix=true)
    {
        var type=typeof(HeroRecruitment).GetNestedType("DespawnPatch",BindingFlags.NonPublic);
        object[] args={root,delay,null};
        type.GetMethod("Before",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);
        if(nativeRecycled)root.activeInHierarchy=false;
        if(postfix)type.GetMethod("After",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new[]{args[2]});
    }
    static void RuntimeTests()
    {
        Reset();var a=Actor(-1);var b=Actor(1);HeroRecruitment.Observe(a.a);HeroRecruitment.Observe(b.a);
        Check(!HeroRecruitment.IsPurchased(a.a)&&HeroRecruitment.CanPurchase,"old free selection not migrated");
        HeroArcherRuntime.ActivateSuccess=false;
        Check(!HeroRecruitment.TryPurchase(out _)&&!HeroRecruitment.IsPurchased(a.a)&&HeroRecruitment.CanPurchase,"failed visual activation rolls back purchase and seat");
        HeroArcherRuntime.ActivateSuccess=true;
        byte[] zeroBaseline=File.ReadAllBytes(HeroRecruitment.ArchivePath);
        int coins=20;Check(HeroRecruitment.TryPurchase(out _),"first native shop recruitment");coins-=8;
        Check(coins==12,"native 8 coin payment represented outside purchase logic");
        Check(File.ReadAllBytes(HeroRecruitment.ArchivePath).SequenceEqual(zeroBaseline),"purchase does not bind old snapshot or write archive");
        var first=HeroRecruitment.IsPurchased(a.a)?a:b;var second=HeroRecruitment.IsPurchased(a.a)?b:a;
        int fixedSide=HeroRecruitment.SeatSide(first.a);
        HeroArcherRuntime.Enabled=false;
        Check(HeroRecruitment.IsPurchased(first.a)&&!HeroRecruitment.CanPurchase,"switch off retains purchase blocks charges");
        HeroArcherRuntime.Enabled=true;Check(HeroRecruitment.IsPurchased(first.a),"switch on restores purchased actor");
        first.a.gameObject.activeInHierarchy=false;
        Time.frameCount++;Time.time+=3;Time.unscaledTime+=3;HeroRecruitment.Tick();
        Check(!HeroRecruitment.IsPurchased(first.a),"temporary inactive hero suspends effects");
        first.a.gameObject.activeInHierarchy=true;HeroRecruitment.OnEnable(first.a);HeroRecruitment.Observe(first.a);
        Check(HeroRecruitment.IsPurchased(first.a)&&HeroRecruitment.SeatSide(first.a)==fixedSide,"same owner temporary enable preserves paid identity and seat");
        first.a.Recruitable=false;first.a.side=-fixedSide;
        Check(HeroRecruitment.IsPurchased(first.a)&&HeroRecruitment.SeatSide(first.a)==fixedSide,"tower embark side changes do not change receipt");
        first.a.Recruitable=true;
        Check(HeroRecruitment.TryPurchase(out _),"other side second purchase");
        Check(!HeroRecruitment.CanPurchase&&!HeroRecruitment.TryPurchase(out _),"two seats prevents repeated charge");
        // A native role replacement has a new root and later pools the previous root.
        var peasant=Actor(0,false);var transfer=new HeroRecruitment.ReplacementCapture();transfer.Begin(first.c);transfer.Complete(peasant.c);
        HeroRecruitment.OnPoolDespawn(first.c.gameObject);
        Check(!HeroRecruitment.IsPurchased(first.a)&&!HeroRecruitment.CanPurchase,"equipment loss retains full seat count");
        Save("paid-and-demoted",("demoted",peasant.p),("second",second.p));
        var file=HeroRecruitmentArchiveStore.Load(HeroRecruitment.ArchivePath);
        Check(file.Status==HeroRecruitmentArchiveStore.State.Valid,"paid demoted hero saved");
        var rows=file.Archive.Scopes.Single().Value[0].Seats;
        Check(rows.Count==2&&rows.Any(x=>x.NativeId=="demoted"),"nonarcher successor persisted by exact native ID");
        var promoted=Actor(-fixedSide);transfer=new();transfer.Begin(peasant.c);transfer.Complete(promoted.c);HeroRecruitment.OnPoolDespawn(peasant.c.gameObject);
        Check(HeroRecruitment.IsPurchased(promoted.a)&&HeroRecruitment.SeatSide(promoted.a)==fixedSide,"pickup bow restores exact successor and fixed side");
        RecycleHook(promoted.c.gameObject,1,false);Check(HeroRecruitment.IsPurchased(promoted.a),"delayed recycle only schedules, preserves identity");
        RecycleHook(promoted.c.gameObject,0,false);Check(HeroRecruitment.IsPurchased(promoted.a),"recycle native guard refusal preserves identity");
        RecycleHook(promoted.c.gameObject,0,false,false);Check(HeroRecruitment.IsPurchased(promoted.a),"throw before native completion preserves identity");
        RecycleHook(promoted.c.gameObject,0,true);promoted.c.gameObject.activeInHierarchy=true;HeroRecruitment.OnEnable(promoted.a);HeroRecruitment.Observe(promoted.a);
        Check(!HeroRecruitment.IsPurchased(promoted.a)&&!HeroRecruitment.CanPurchase,"unproven pool reuse reserves without inheritance");
        byte[] originalArchive=File.ReadAllBytes(HeroRecruitment.ArchivePath);
        for(int i=0;i<10;i++)Save("unresolved-"+i,("other",second.p));
        Check(File.ReadAllBytes(HeroRecruitment.ArchivePath).SequenceEqual(originalArchive),"unresolved saves preserve provable origin history");
        Check(HeroRecruitment.StatusText.Contains("待恢复"),"unresolved seat explained in status");
        // Exact reload restores native owner mapping including a saved demotion.
        Reset(true);var loadedPeasant=Actor(0,false);var loadedOther=Actor((int)second.a.side);
        Load("paid-and-demoted",("demoted",loadedPeasant.p),("second",loadedOther.p));
        Check(HeroRecruitment.IsPurchased(loadedOther.a),"exact saved archer restored");
        var promotedAgain=Actor(-fixedSide);transfer=new();transfer.Begin(loadedPeasant.c);transfer.Complete(promotedAgain.c);
        Check(HeroRecruitment.IsPurchased(promotedAgain.a)&&HeroRecruitment.SeatSide(promotedAgain.a)==fixedSide,"saved peasant later restores hero");
        promotedAgain.c._damageable.Die();HeroRecruitment.Observe(promotedAgain.a);var replacement=Actor(fixedSide);HeroRecruitment.Observe(replacement.a);
        Check(HeroRecruitment.CanPurchase&&!HeroRecruitment.IsPurchased(promotedAgain.a),"confirmed death releases only its seat");
        Check(HeroRecruitment.HasFallenSeat(fixedSide)&&!HeroRecruitment.HasFallenSeat(-fixedSide),"only confirmed dead side receives torn banner feedback");
        Check(HeroRecruitment.TryPurchase(out _),"death vacancy requires fresh purchase");
        Check(!HeroRecruitment.HasFallenSeat(fixedSide),"successful new purchase replaces torn banner");
        Reset(true);var mismatch=Actor(-1);Load("vanilla-resave",("demoted",mismatch.p));HeroRecruitment.Observe(mismatch.a);
        Check(!HeroRecruitment.IsPurchased(mismatch.a)&&!HeroRecruitment.CanPurchase,"vanilla mismatch reserves seats never guesses owner");
        CampaignSaveData.current.CurrentIsland=new(){land=2};Time.frameCount++;HeroRecruitment.Tick();var islandTwo=Actor(-1);HeroRecruitment.Observe(islandTwo.a);
        Check(!HeroRecruitment.CanPurchase&&HeroRecruitment.StatusText.Contains("加载确认"),"new island without proven baseline explains temporary purchase gate");
        Load("island-two-before-purchase",("free",islandTwo.p));
        Check(HeroRecruitment.CanPurchase,"other island not locked by unrelated old seats");
        Reset();var unsaved=Actor(-1);HeroRecruitment.Observe(unsaved.a);Check(HeroRecruitment.TryPurchase(out _),"unsaved purchase");
        Reset(true);var reloaded=Actor(-1);Load("before-purchase",("native",reloaded.p));HeroRecruitment.Observe(reloaded.a);
        Check(!HeroRecruitment.IsPurchased(reloaded.a)&&HeroRecruitment.CanPurchase,"8 coins and purchase both rollback with unsaved native session");
        Reset();var notCommitted=Actor(-1);HeroRecruitment.Observe(notCommitted.a);Check(HeroRecruitment.TryPurchase(out _),"purchase before simulated disk failure");
        for(int i=0;i<12;i++)Save("prepared-not-committed-"+i,("paid",notCommitted.p));
        var pendingArchive=HeroRecruitmentArchiveStore.Load(HeroRecruitment.ArchivePath).Archive;
        string scope=pendingArchive.Scopes.Keys.Single();
        Check(pendingArchive.TryGet(scope,pendingArchive.Baselines[scope],out var baseline)&&baseline.Seats.Count==0&&baseline.HashKind==HeroRecruitmentFingerprint.Kind,"zero native baseline survives twelve uncommitted saves");
        Reset(true);var oldDiskActor=Actor(-1);Load("before-purchase",("free",oldDiskActor.p));HeroRecruitment.Observe(oldDiskActor.a);
        Check(!HeroRecruitment.IsPurchased(oldDiskActor.a)&&HeroRecruitment.CanPurchase,"native disk failure reload does not lock unpaid old snapshot");
        Reset();var paidBeforeVanilla=Actor(-1);HeroRecruitment.Observe(paidBeforeVanilla.a);Check(HeroRecruitment.TryPurchase(out _),"purchase before native then vanilla save");
        Save("native-paid-before-vanilla",("paid",paidBeforeVanilla.p));
        Reset(true);var vanillaUnknown=Actor(-1);Load("vanilla-new-hash-without-mod-paid-reload",("paid",vanillaUnknown.p));HeroRecruitment.Observe(vanillaUnknown.a);
        Check(!HeroRecruitment.IsPurchased(vanillaUnknown.a)&&!HeroRecruitment.CanPurchase&&HeroRecruitment.StatusText.Contains("待恢复"),"unknown vanilla resave reserves unconfirmed paid history without guessing");
        Reset();var freshForOnline=Actor(-1);HeroRecruitment.Observe(freshForOnline.a);
        HeroArcherNetwork.AllowsLocalHero=false;Check(!HeroRecruitment.CanPurchase&&!HeroRecruitment.TryPurchase(out _),"online no purchase");
        HeroArcherNetwork.AllowsLocalHero=true;
        Directory.CreateDirectory(Path.GetDirectoryName(HeroRecruitment.ArchivePath));File.WriteAllText(HeroRecruitment.ArchivePath,"{\"schemaVersion\":99,\"scopes\":[]}");
        Check(!HeroRecruitment.TryPurchase(out _),"future archive appearing during play blocks purchase");
        Check(File.ReadAllText(HeroRecruitment.ArchivePath).Contains("99"),"unsupported archive left untouched");
        Reset();CampaignSaveData.current.CurrentIsland=new(){land=7};Time.frameCount++;HeroRecruitment.Tick();
        var newIslandArcher=Actor(-1);HeroRecruitment.Observe(newIslandArcher.a);
        Check(!HeroRecruitment.CanPurchase,"virgin island before native apply not initialized");
        var virgin=new HeroRecruitment.VirginCapture();virgin.Begin(CampaignSaveData.current);
        Check(!HeroRecruitment.CanPurchase,"virgin prefix alone cannot enable charge");
        virgin.Complete(CampaignSaveData.current);
        Check(HeroRecruitment.CanPurchase,"successful native new-island apply enables shop without restart");
        Check(HeroRecruitment.TryPurchase(out _),"first native generated island purchase");
        var repeatVirgin=new HeroRecruitment.VirginCapture();repeatVirgin.Begin(CampaignSaveData.current);repeatVirgin.Complete(CampaignSaveData.current);
        Check(HeroRecruitment.IsPurchased(newIslandArcher.a),"repeated virgin callback cannot erase a paid receipt");
        CampaignSaveData.current.CurrentIsland=new(){land=8,isNew=false};Time.frameCount++;HeroRecruitment.Tick();
        var oldIslandArcher=Actor(-1);HeroRecruitment.Observe(oldIslandArcher.a);virgin=new();virgin.Begin(CampaignSaveData.current);virgin.Complete(CampaignSaveData.current);
        Check(!HeroRecruitment.CanPurchase,"existing island is not treated as virgin");
        Reset();var reusedPointer=Actor(-1);HeroRecruitment.Observe(reusedPointer.a);Check(HeroRecruitment.TryPurchase(out _),"purchase before pointer reuse test");
        reusedPointer.c.gameObject.InstanceId+=100000;
        HeroRecruitment.OnEnable(reusedPointer.a);HeroRecruitment.Observe(reusedPointer.a);
        Check(!HeroRecruitment.IsPurchased(reusedPointer.a)&&!HeroRecruitment.CanPurchase,"native pointer reuse with different GameObject identity never inherits purchase");
    }

    static void SeatDisplayTests()
    {
        Reset();HeroRecruitment.Tick();var left=Actor(-1);HeroRecruitment.Observe(left.a);
        Check(HeroRecruitment.GetShopSeatState(0)==HeroShopSeatState.Unavailable,"invalid side is unavailable");
        Check(HeroRecruitment.GetShopSeatState(-1)==HeroShopSeatState.Available,"left-only candidate shows left available");
        Check(HeroRecruitment.GetShopSeatState(1)==HeroShopSeatState.Unavailable,"left-only candidate never marks right available");
        left.a.Recruitable=false;
        Check(HeroRecruitment.GetShopSeatState(-1)==HeroShopSeatState.Unavailable,"temporarily ineligible free archer does not advertise stock");
        left.a.Recruitable=true;HeroArcherRuntime.Enabled=false;
        Check(HeroRecruitment.GetShopSeatState(-1)==HeroShopSeatState.Unavailable,"disabled empty seat unavailable");
        HeroArcherRuntime.Enabled=true;HeroArcherNetwork.AllowsLocalHero=false;
        Check(HeroRecruitment.GetShopSeatState(-1)==HeroShopSeatState.Unavailable,"online display unavailable");
        HeroArcherNetwork.AllowsLocalHero=true;
        Check(HeroRecruitment.TryPurchase(out _),"buy displayed left candidate");
        left.a.Recruitable=false;left.a.gameObject.activeInHierarchy=false;
        Check(HeroRecruitment.GetShopSeatState(-1)==HeroShopSeatState.Occupied,"temporary inactive/tower hero still shown as occupied");
        HeroArcherRuntime.Enabled=false;
        Check(HeroRecruitment.GetShopSeatState(-1)==HeroShopSeatState.Occupied,"effect toggle off preserves occupied label");
        HeroArcherRuntime.Enabled=true;left.a.gameObject.activeInHierarchy=true;
        var right=Actor(1);HeroRecruitment.Observe(right.a);
        Check(HeroRecruitment.GetShopSeatState(1)==HeroShopSeatState.Available,"vacant right side has its own candidate");
        left.a.side=1;left.a.Recruitable=true;right.a.Recruitable=false;
        Check(HeroRecruitment.GetShopSeatState(1)==HeroShopSeatState.Unavailable,"already purchased left hero moving right is not right stock");
        right.a.Recruitable=true;RecycleHook(left.a.gameObject,0,true);
        Check(HeroRecruitment.GetShopSeatState(-1)==HeroShopSeatState.Reserved,"unbound purchased side is reserved never available");
        Check(HeroRecruitment.GetShopSeatState(1)==HeroShopSeatState.Unavailable,"global unresolved owner gate blocks other-side stock");
        Reset();HeroRecruitment.Tick();right=Actor(1);HeroRecruitment.Observe(right.a);
        Check(HeroRecruitment.GetShopSeatState(1)==HeroShopSeatState.Available&&HeroRecruitment.GetShopSeatState(-1)==HeroShopSeatState.Unavailable,"right-only candidate does not advertise left stock");
        byte[] untouched=File.ReadAllBytes(HeroRecruitment.ArchivePath);
        for(int i=0;i<20;i++){HeroRecruitment.GetShopSeatState(-1);HeroRecruitment.GetShopSeatState(1);}
        Check(File.ReadAllBytes(HeroRecruitment.ArchivePath).SequenceEqual(untouched),"display queries never mutate archive");
        File.WriteAllText(HeroRecruitment.ArchivePath,"{\"schemaVersion\":99,\"scopes\":[]}");
        Check(!HeroRecruitment.TryPurchase(out _)&&HeroRecruitment.GetShopSeatState(1)==HeroShopSeatState.Unavailable,"read-only archive suppresses empty-side stock");
        Reset();HeroRecruitment.Tick();left=Actor(-1);HeroRecruitment.Observe(left.a);
        var global=GlobalSaveData.loaded;GlobalSaveData.loaded=null;
        Check(HeroRecruitment.GetShopSeatState(-1)==HeroShopSeatState.Unavailable,"missing global context display unavailable");
        GlobalSaveData.loaded=global;Time.frameCount++;
        Check(HeroRecruitment.GetShopSeatState(-1)==HeroShopSeatState.Unavailable,"query before current-frame Tick is fail-closed without hashing");
        HeroRecruitment.Tick();Check(HeroRecruitment.GetShopSeatState(-1)==HeroShopSeatState.Available,"current-frame Tick enables read-only display");
        CampaignSaveData.current.CurrentIsland=new(){land=99};Time.frameCount++;HeroRecruitment.Tick();left=Actor(-1);HeroRecruitment.Observe(left.a);
        Check(HeroRecruitment.GetShopSeatState(-1)==HeroShopSeatState.Unavailable,"unconfirmed island baseline never advertises stock");
    }

    static string Seats(string describe){int i=describe.IndexOf('[');return i<0?"":describe.Substring(i);}
    static string Epoch(string describe){int i=describe.IndexOf("epoch=",StringComparison.Ordinal);if(i<0)return "";i+=6;int j=describe.IndexOf(' ',i);string prefix=describe.Substring(i,j-i);return Disk().Scopes.Keys.Single(x=>x.StartsWith(prefix,StringComparison.Ordinal));}
    static string ContextOf(int campaign,int challenge,int land)=>HeroRecruitmentArchive.ContextKey(GlobalSaveData.filename,campaign,challenge,land);


    // The game recreates island.realStartDateTime on every load; identity must not follow it.
    static void ReloadIdentity()
    {
        Reset();
        var left=Actor(-1);var right=Actor(1);HeroRecruitment.Observe(left.a);HeroRecruitment.Observe(right.a);
        Check(HeroRecruitment.TryPurchase(out _)&&HeroRecruitment.TryPurchase(out _),"both seats purchased before the reload");
        Save("day0",("native-left",left.p),("native-right",right.p));
        string context=ContextOf(1,0,1);string epoch=Epoch(HeroRecruitment.DescribeForTests());
        string[] rows=Disk().Scopes[epoch][0].Seats.OrderBy(x=>x.Side).Select(x=>x.Id.ToString("N")).ToArray();
        CampaignSaveData.current.CurrentIsland.realStartDateTime=new DateTime(2026,9,2);
        var l2=Actor(-1);var r2=Actor(1);
        Load("day0",("native-left",l2.p),("native-right",r2.p));
        Check(HeroRecruitment.IsPurchased(l2.a)&&HeroRecruitment.IsPurchased(r2.a),"both paid seats restored after a reload with a recreated creation time");
        Check(Epoch(HeroRecruitment.DescribeForTests())==epoch,"reload keeps the same epoch");
        var after=Disk();
        Check(after.Contexts[context].Epochs.Count==1&&after.Contexts[context].Active==epoch,"one context, one epoch after the reload");
        Check(after.Scopes[epoch][0].Seats.OrderBy(x=>x.Side).Select(x=>x.Id.ToString("N")).SequenceEqual(rows),"reload keeps the same receipt GUIDs");
        int count=after.Scopes[epoch].Count;
        Save("day0",("native-left",l2.p),("native-right",r2.p));
        var l3=Actor(-1);var r3=Actor(1);
        Load("day0",("native-left",l3.p),("native-right",r3.p));
        Check(HeroRecruitment.IsPurchased(l3.a)&&HeroRecruitment.IsPurchased(r3.a),"repeated save/load keeps both seats");
        var repeat=Disk();
        Check(repeat.Scopes[epoch].Count==count&&repeat.Scopes[epoch][0].Seats.OrderBy(x=>x.Side).Select(x=>x.Id.ToString("N")).SequenceEqual(rows),"repeated save/load duplicates neither snapshots nor GUIDs");
    }

    // A confirmed new generation owns a fresh epoch; an older save still rolls back to its own.
    static void EpochRollback()
    {
        Reset();
        var a=Actor(-1);HeroRecruitment.Observe(a.a);
        Check(HeroRecruitment.TryPurchase(out _),"old-generation purchase");
        Save("old-generation",("old-hero",a.p));
        string context=ContextOf(1,0,1);string oldEpoch=Disk().Contexts[context].Active;
        Generate(1,"new-generation-island");
        var generated=Disk();string newEpoch=generated.Contexts[context].Active;
        Check(newEpoch!=oldEpoch&&generated.Contexts[context].Epochs.SequenceEqual(new[]{newEpoch,oldEpoch}),"new generation appends its own epoch");
        Check(generated.Scopes.ContainsKey(oldEpoch)&&generated.Scopes.ContainsKey(newEpoch),"both epoch scopes retained");
        Check(HeroRecruitment.CanPurchase&&!HeroRecruitment.IsPurchased(a.a),"new generation inherits no old seat");
        var p2=Actor(-1);Load("old-generation",("old-hero",p2.p));
        Check(HeroRecruitment.IsPurchased(p2.a)&&HeroRecruitment.SeatSide(p2.a)==-1,"older epoch rolls back with its paid receipt");
        var rolled=Disk();
        Check(rolled.Contexts[context].Active==oldEpoch&&rolled.Contexts[context].Epochs.SequenceEqual(new[]{oldEpoch,newEpoch}),"rollback reorders the active epoch without dropping the newer one");
    }

    // A native snapshot we never recorded reserves every provable seat; it must not write or charge.
    static void UnknownSnapshot()
    {
        Reset();
        var a=Actor(-1);HeroRecruitment.Observe(a.a);
        Check(HeroRecruitment.TryPurchase(out _),"purchase before an unknown native resave");
        Save("recorded",("paid-hero",a.p));
        byte[] file=File.ReadAllBytes(HeroRecruitment.ArchivePath);
        var b=Actor(-1);
        Load("vanilla-resave",("paid-hero",b.p));
        Check(!HeroRecruitment.IsPurchased(b.a)&&!HeroRecruitment.CanPurchase,"unknown snapshot reserves the paid seat without guessing an owner");
        Check(HeroRecruitment.StatusText.Contains("待恢复")&&HeroRecruitment.DescribeForTests().Contains("unresolved=True"),"unresolved quota is explained");
        Check(Seats(HeroRecruitment.DescribeForTests()).Contains(":reserved:"),"reserved seat carries no native owner");
        Check(File.ReadAllBytes(HeroRecruitment.ArchivePath).SequenceEqual(file),"unknown snapshot writes nothing");
    }

    // Two legacy epochs claiming different paid histories for the same island: never pick one blindly.
    static void ConflictingHistory()
    {
        Reset();
        string raw=Raw("conflict-island");
        string scopeA=HeroRecruitmentArchive.NewScope(),scopeB=HeroRecruitmentArchive.NewScope();
        string v1="{\"schemaVersion\":1,\"scopes\":["
            +"{\"scope\":\""+scopeA+"\",\"baseline\":\"\",\"snapshots\":[{\"hash\":\""+HeroRecruitmentArchive.Hash(raw,scopeA)+"\",\"seats\":[{\"id\":\"22222222222222222222222222222222\",\"side\":-1,\"nativeId\":\"hero-A\"}]}]},"
            +"{\"scope\":\""+scopeB+"\",\"baseline\":\"\",\"snapshots\":[{\"hash\":\""+HeroRecruitmentArchive.Hash(raw,scopeB)+"\",\"seats\":[{\"id\":\"33333333333333333333333333333333\",\"side\":1,\"nativeId\":\"hero-B\"}]}]}]}";
        File.WriteAllText(HeroRecruitment.ArchivePath,v1);
        var c1=Actor(-1);var c2=Actor(1);
        Load(raw,("hero-A",c1.p),("hero-B",c2.p));
        Check(!HeroRecruitment.IsPurchased(c1.a)&&!HeroRecruitment.IsPurchased(c2.a),"conflicting histories restore nothing");
        Check(!HeroRecruitment.CanPurchase&&HeroRecruitment.StatusText.Contains("待恢复"),"conflicting histories fail closed with the quota reserved");
        Check(HeroRecruitment.DescribeForTests().Contains("kind=conflict"),"conflict reported in the diagnostic");
        Check(File.ReadAllText(HeroRecruitment.ArchivePath)==v1,"conflicting archive is left untouched");
    }

    // Same file, other campaign / challenge / land: independent contexts, and a generation that
    // never completes must alter neither the context nor the archive.
    static void IsolationAndGeneration()
    {
        Reset();
        var a=Actor(-1);HeroRecruitment.Observe(a.a);
        Check(HeroRecruitment.TryPurchase(out _),"campaign one land one purchase");
        Save("campaign-one",("hero",a.p));
        GlobalSaveData.loaded.currentCampaign=2;CampaignSaveData.current.CurrentIsland=new(){land=1};Time.frameCount++;
        var b=Actor(-1);Load("campaign-two",("free",b.p));HeroRecruitment.Observe(b.a);
        Check(HeroRecruitment.CanPurchase&&!HeroRecruitment.IsPurchased(b.a),"other campaign is an independent context");
        GlobalSaveData.loaded.currentCampaign=1;GlobalSaveData.loaded.currentChallenge=1;CampaignSaveData.current.CurrentIsland=new(){land=1};Time.frameCount++;
        var c=Actor(-1);Load("challenge-one",("free",c.p));HeroRecruitment.Observe(c.a);
        Check(HeroRecruitment.CanPurchase&&!HeroRecruitment.IsPurchased(c.a),"other challenge is an independent context");
        GlobalSaveData.loaded.currentChallenge=0;CampaignSaveData.current.CurrentIsland=new(){land=2};Time.frameCount++;
        var d=Actor(-1);Load("land-two",("free",d.p));HeroRecruitment.Observe(d.a);
        Check(HeroRecruitment.CanPurchase&&!HeroRecruitment.IsPurchased(d.a),"other land is an independent context");
        byte[] before=File.ReadAllBytes(HeroRecruitment.ArchivePath);
        CampaignSaveData.current.CurrentIsland=new(){land=3,isNew=true,playTimeDays=0,Json=Raw("virgin-three")};
        var virgin=new HeroRecruitment.VirginCapture();virgin.Begin(CampaignSaveData.current);
        Managers.Inst.world=new(){gameLayer=new GameObject().Add(new Transform())};
        Time.frameCount++;HeroRecruitment.Tick();
        virgin.Complete(CampaignSaveData.current);
        Check(File.ReadAllBytes(HeroRecruitment.ArchivePath).SequenceEqual(before),"aborted generation writes nothing");
        Check(!HeroRecruitment.CanPurchase&&!Disk().Contexts.ContainsKey(ContextOf(1,0,3)),"aborted generation registers no context and no charge");
        var virgin2=new HeroRecruitment.VirginCapture();virgin2.Begin(CampaignSaveData.current);virgin2.Complete(CampaignSaveData.current);
        var e=Actor(-1);HeroRecruitment.Observe(e.a);
        Check(HeroRecruitment.CanPurchase&&Disk().Contexts.ContainsKey(ContextOf(1,0,3)),"confirmed new generation registers its epoch and can charge");
    }


    // A known context must still search unclaimed legacy scopes when its own match is a v1 empty
    // baseline: a fabricated empty epoch must never hide the paid epoch.
    static void LegacyBranches()
    {
        Reset();
        string j0=Raw("legacy-j0"),j1=Raw("legacy-j1");
        string scope651=HeroRecruitmentArchive.NewScope(),scope7035=HeroRecruitmentArchive.NewScope();
        string v1="{\"schemaVersion\":1,\"scopes\":["
            +"{\"scope\":\""+scope651+"\",\"baseline\":\"\",\"snapshots\":[{\"hash\":\""+HeroRecruitmentArchive.Hash(j0,scope651)+"\",\"seats\":[{\"id\":\"44444444444444444444444444444444\",\"side\":-1,\"nativeId\":\"hero-651\"}]}]},"
            +"{\"scope\":\""+scope7035+"\",\"baseline\":\"\",\"snapshots\":[{\"hash\":\""+HeroRecruitmentArchive.Hash(j0,scope7035)+"\",\"seats\":[]},{\"hash\":\""+HeroRecruitmentArchive.Hash(j1,scope7035)+"\",\"seats\":[{\"id\":\"55555555555555555555555555555555\",\"side\":1,\"nativeId\":\"hero-7035\"}]}]}]}";
        File.WriteAllText(HeroRecruitment.ArchivePath,v1);
        string context=ContextOf(1,0,1);
        var newest=Actor(1);Load(j1,("hero-7035",newest.p));
        Check(HeroRecruitment.IsPurchased(newest.a)&&HeroRecruitment.SeatSide(newest.a)==1,"latest paid epoch is restored under its own scope");
        Check(Disk().Contexts[context].Active==scope7035,"context pinned to the paid scope");
        var older=Actor(-1);Load(j0,("hero-651",older.p));
        Check(HeroRecruitment.IsPurchased(older.a)&&HeroRecruitment.SeatSide(older.a)==-1,"known context still recovers the older paid epoch");
        var rolled=Disk();
        Check(rolled.Contexts[context].Active==scope651&&rolled.Contexts[context].Epochs.Contains(scope7035),"older epoch becomes active and the newer one is preserved");
        var again=Actor(1);Load(j1,("hero-7035",again.p));
        Check(HeroRecruitment.IsPurchased(again.a)&&Disk().Contexts[context].Active==scope7035,"reloading the latest save restores its paid epoch");
        Check(Disk().Contexts[context].Epochs.Count==2,"both paid epochs stay in history");
    }

    // A v2-authoritative empty state beats legacy paid history for the same native snapshot: the v2
    // lineage already released that seat, so the legacy receipt must not resurrect it.
    static void AuthoritativeEmpty()
    {
        Reset();
        string raw=Raw("released-island");
        string context=ContextOf(1,0,1);
        string legacyScope=HeroRecruitmentArchive.NewScope(),v2Scope=HeroRecruitmentArchive.NewScope();
        var seeded=new HeroRecruitmentArchive();
        Check(seeded.Record(legacyScope,HeroRecruitmentArchive.Hash(raw,legacyScope),new[]{new HeroPurchaseReceipt{Id=new Guid("66666666666666666666666666666666"),Side=-1,NativeId="legacy-hero"}},HeroRecruitmentArchive.HashKindLegacy,true),"legacy paid epoch seeded");
        Check(seeded.EnsureContext(context,legacyScope,true),"legacy epoch claimed");
        Check(seeded.Record(v2Scope,HeroRecruitmentFingerprint.Hash(raw,v2Scope),Array.Empty<HeroPurchaseReceipt>(),HeroRecruitmentFingerprint.Kind,false),"authoritative empty epoch seeded");
        Check(seeded.EnsureContext(context,v2Scope,true),"authoritative epoch claimed");
        File.WriteAllBytes(HeroRecruitment.ArchivePath,seeded.Encode());
        var actor=Actor(-1);HeroRecruitment.Observe(actor.a);
        Load(raw,("legacy-hero",actor.p));
        Check(!HeroRecruitment.IsPurchased(actor.a)&&HeroRecruitment.CanPurchase,"authoritative empty beats legacy paid and cannot resurrect the seat");
        Check(HeroRecruitment.DescribeForTests().Contains("kind=authoritative-empty")&&HeroRecruitment.DescribeForTests().Contains("v1=False"),"authoritative empty reported without v1 provenance");
        Check(Disk().Scopes.ContainsKey(legacyScope),"legacy history is preserved, just not authoritative");
    }

    // An epoch owned by one context is never a migration source for another one.
    static void OwnershipIsolation()
    {
        Reset();
        string shared=Raw("shared-island");
        string context=ContextOf(1,0,1);
        string scope=HeroRecruitmentArchive.NewScope();
        var seeded=new HeroRecruitmentArchive();
        Check(seeded.Record(scope,HeroRecruitmentArchive.Hash(shared,scope),new[]{new HeroPurchaseReceipt{Id=new Guid("77777777777777777777777777777777"),Side=-1,NativeId="owned-hero"}},HeroRecruitmentArchive.HashKindLegacy,true),"owned scope seeded");
        Check(seeded.EnsureContext(context,scope,true),"scope owned by campaign one");
        File.WriteAllBytes(HeroRecruitment.ArchivePath,seeded.Encode());
        GlobalSaveData.loaded.currentCampaign=2;
        var other=Actor(-1);CampaignSaveData.current.CurrentIsland=new(){land=1};
        Load(shared,("owned-hero",other.p));HeroRecruitment.Observe(other.a);
        Check(!HeroRecruitment.IsPurchased(other.a)&&HeroRecruitment.CanPurchase,"another context never migrates an owned epoch");
        Check(Disk().Contexts[context].Epochs.SequenceEqual(new[]{scope}),"original ownership is unchanged");
        GlobalSaveData.loaded.currentCampaign=1;CampaignSaveData.current.CurrentIsland=new(){land=1};Time.frameCount++;
        var owner=Actor(-1);Load(shared,("owned-hero",owner.p));
        Check(HeroRecruitment.IsPurchased(owner.a)&&HeroRecruitment.SeatSide(owner.a)==-1,"the owning context still restores its seat");
    }

    static string Drift(string label,int play,int last,int islandTime,bool extra=false)
        =>"{\"label\":\""+label+"\",\"playTimeDays\":"+play+",\"lastPlayedTimeDays\":"+last+",\"_islandTimePlayed\":"+islandTime
          +",\"objects\":["+(extra?"{\"uniqueID\":\"changed\"}":"")+"]}";

    // v2 snapshots use the clock-independent fingerprint: clock variants of one island reload the
    // same receipts, while any object change is an unknown snapshot that never charges.
    static void ClockDrift()
    {
        Reset();
        string first=Drift("drift",10,9,100),second=Drift("drift",11,10,105),third=Drift("drift",12,11,110);
        var left=Actor(-1);var right=Actor(1);HeroRecruitment.Observe(left.a);HeroRecruitment.Observe(right.a);
        Check(HeroRecruitment.TryPurchase(out _)&&HeroRecruitment.TryPurchase(out _),"two seats purchased for the drift test");
        Save(first,("native-left",left.p),("native-right",right.p));
        string epoch=Epoch(HeroRecruitment.DescribeForTests());
        var l1=Actor(-1);var r1=Actor(1);
        Load(second,("native-left",l1.p),("native-right",r1.p));
        Check(HeroRecruitment.IsPurchased(l1.a)&&HeroRecruitment.IsPurchased(r1.a)&&Epoch(HeroRecruitment.DescribeForTests())==epoch,"clock drift alone restores the receipts");
        string receipts=Seats(HeroRecruitment.DescribeForTests());
        var l2=Actor(-1);var r2=Actor(1);
        Load(third,("native-left",l2.p),("native-right",r2.p));
        Check(HeroRecruitment.IsPurchased(l2.a)&&HeroRecruitment.IsPurchased(r2.a)&&Seats(HeroRecruitment.DescribeForTests())==receipts,"a second clock variant restores the same receipt GUIDs");
        byte[] before=File.ReadAllBytes(HeroRecruitment.ArchivePath);
        var l3=Actor(-1);var r3=Actor(1);
        Load(Drift("drift",10,9,100,true),("native-left",l3.p),("native-right",r3.p));
        Check(!HeroRecruitment.IsPurchased(l3.a)&&!HeroRecruitment.CanPurchase,"a real object change is an unknown snapshot and never charges");
        Check(File.ReadAllBytes(HeroRecruitment.ArchivePath).SequenceEqual(before),"an unknown snapshot writes nothing");
    }

    // A legacy empty snapshot may pin a context, but loading it must not launder v1 provenance.
    static void LegacyEmptyFlag()
    {
        Reset();
        string raw=Raw("legacy-empty-island");
        string context=ContextOf(1,0,1);
        string scope=HeroRecruitmentArchive.NewScope();
        string v1="{\"schemaVersion\":1,\"scopes\":[{\"scope\":\""+scope+"\",\"baseline\":\"\",\"snapshots\":[{\"hash\":\""+HeroRecruitmentArchive.Hash(raw,scope)+"\",\"seats\":[]}]}]}";
        File.WriteAllText(HeroRecruitment.ArchivePath,v1);
        Load(raw);
        var disk=Disk();
        Check(disk.Contexts[context].Active==scope,"legacy empty snapshot pins the epoch");
        Check(disk.TryGet(scope,HeroRecruitmentArchive.Hash(raw,scope),out var snapshot)&&snapshot.LegacyV1&&snapshot.HashKind==HeroRecruitmentArchive.HashKindLegacy,"confirmed legacy empty keeps its v1 provenance");
        Check(disk.Baselines[scope]==snapshot.Hash,"baseline points at the confirmed legacy snapshot");
        var later=Actor(-1);HeroRecruitment.Observe(later.a);
        Check(HeroRecruitment.CanPurchase&&!HeroRecruitment.IsPurchased(later.a),"an unlaundered legacy empty context may still charge");
        string paidScope=HeroRecruitmentArchive.NewScope();
        Check(disk.Record(paidScope,HeroRecruitmentArchive.Hash(raw,paidScope),new[]{new HeroPurchaseReceipt{Id=new Guid("88888888888888888888888888888888"),Side=1,NativeId="hero-late"}},HeroRecruitmentArchive.HashKindLegacy,true),"late legacy paid scope recorded");
        File.WriteAllBytes(HeroRecruitment.ArchivePath,disk.Encode());
        var late=Actor(1);Load(raw,("hero-late",late.p));
        Check(HeroRecruitment.IsPurchased(late.a)&&HeroRecruitment.SeatSide(late.a)==1,"legacy paid exact match still recovers over a legacy empty baseline");
        Check(Disk().Contexts[context].Epochs.Count==2,"the recovered epoch joins the same context");
    }

    // A failed load never alters the active epoch or the archive.
    static void FailedLoad()
    {
        Reset();
        var a=Actor(-1);HeroRecruitment.Observe(a.a);
        Check(HeroRecruitment.TryPurchase(out _),"purchase before the failed load");
        Save("failed-load-state",("paid",a.p));
        byte[] before=File.ReadAllBytes(HeroRecruitment.ArchivePath);
        string epoch=Epoch(HeroRecruitment.DescribeForTests());
        var island=CampaignSaveData.current.CurrentIsland;
        island.Json=Raw("failed-load-island");island.isNew=false;island.objects=new();
        var capture=new HeroRecruitment.LoadCapture();capture.Begin(island);capture.End(false);
        Check(File.ReadAllBytes(HeroRecruitment.ArchivePath).SequenceEqual(before),"failed load writes nothing");
        Check(Epoch(HeroRecruitment.DescribeForTests())==epoch,"failed load keeps the active epoch");
        Check(HeroRecruitment.IsPurchased(a.a),"failed load keeps the paid seat");
    }

    // Repeated virgin callbacks in one world reuse the epoch; a new-world regeneration appends one.
    static void OldReign()
    {
        Reset();
        string context=ContextOf(1,0,1);
        Generate(1,"first-reign");
        string firstEpoch=Disk().Contexts[context].Active;
        int initialEpochs=Disk().Contexts[context].Epochs.Count;
        var a=Actor(-1);HeroRecruitment.Observe(a.a);
        Check(HeroRecruitment.TryPurchase(out _),"old reign purchase");
        Save("first-reign-state",("old-hero",a.p));
        var repeat=new HeroRecruitment.VirginCapture();repeat.Begin(CampaignSaveData.current);repeat.Complete(CampaignSaveData.current);
        Check(Disk().Contexts[context].Epochs.Count==initialEpochs&&HeroRecruitment.IsPurchased(a.a),"repeated virgin callback keeps existing epochs and its seat");
        Managers.Inst.world=new(){gameLayer=new GameObject().Add(new Transform())};
        Time.frameCount++;HeroRecruitment.Tick();
        Generate(1,"second-reign");
        var disk=Disk();
        Check(disk.Contexts[context].Active!=firstEpoch&&disk.Contexts[context].Epochs.Count==initialEpochs+1&&disk.Scopes.ContainsKey(firstEpoch),"a new world regeneration appends an epoch and keeps the old scope");
        Check(!HeroRecruitment.IsPurchased(a.a),"a new reign inherits no old seat");
        var rolled=Actor(-1);Load("first-reign-state",("old-hero",rolled.p));
        Check(HeroRecruitment.IsPurchased(rolled.a)&&HeroRecruitment.SeatSide(rolled.a)==-1,"the old reign still rolls back with its paid receipt");
    }

    static void ContextTests()
    {
        ReloadIdentity();
        EpochRollback();
        UnknownSnapshot();
        ConflictingHistory();
        IsolationAndGeneration();
        LegacyBranches();
        AuthoritativeEmpty();
        OwnershipIsolation();
        ClockDrift();
        LegacyEmptyFlag();
        FailedLoad();
        OldReign();
        ConflictingFingerprint();
    }


    static void ConflictingFingerprint()
    {
        Reset();var a=Actor(-1);HeroRecruitment.Observe(a.a);
        Check(HeroRecruitment.TryPurchase(out _),"collision test has a paid runtime receipt");
        byte[] before=File.ReadAllBytes(HeroRecruitment.ArchivePath);
        Save("before-purchase",("collision-owner",a.p));
        Check(File.ReadAllBytes(HeroRecruitment.ArchivePath).SequenceEqual(before),"same fingerprint conflicting purchase cannot overwrite old snapshot");
        Check(HeroRecruitment.IsPurchased(a.a)&&!HeroRecruitment.CanPurchase&&HeroRecruitment.DescribeForTests().Contains("readonly=True"),"conflict preserves current paid runtime owner and blocks further charges");
    }
}
