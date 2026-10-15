using System.Text.Json;
using System.Text.Json.Nodes;
using KingdomEnhancedMod;

static class Program
{
    static int n;
    static readonly string Context = new('a',64), Source = new('b',64), Migration = new('c',64), OldHash = new('d',64), NewHash = new('e',64);
    static void Check(bool x,string why) { n++; if (!x) throw new Exception(why); }
    static string Id(int i) => "000000000000000000000000"+i.ToString("x8");
    static object Row(int i,string native) => new { id=Id(i), kind=1, nativeId=native, stockSlot=-1 };
    static JsonObject Fixture(bool compensation)
    {
        var oldRows=Enumerable.Range(1,17).Select(i=>Row(i,"old-"+i)).ToArray();
        var scopes=new List<object>{new {scope=Source,baseline=OldHash,snapshots=new[]{new{hash=OldHash,hashKind=2,records=oldRows}}}};
        if (compensation) scopes.Add(new {scope=Migration,baseline=NewHash,snapshots=new[]{new{hash=NewHash,hashKind=2,records=Enumerable.Range(101,8).Select(i=>Row(i,"new-"+i)).ToArray()}}});
        var root=JsonSerializer.SerializeToNode(new {schemaVersion=compensation?3:2,scopes,contexts=new[]{new {context=Context,active=compensation?Migration:Source,epochs=compensation?new[]{Migration,Source}:new[]{Source}}}}).AsObject();
        if(compensation)root["compensations"]=JsonSerializer.SerializeToNode(new[]{new{
            version=1,plan=new{version=1,requestId=Id(999),context=Context,sourceEpoch=Source,sourceArchiveSha=new string('f',64),sourceReservations=17,units=8},
            migrationEpoch=Migration,grantIds=Enumerable.Range(101,8).Select(Id).ToArray(),capturedNativeIds=Enumerable.Range(101,8).Select(i=>"new-"+i).ToArray(),complete=true}});
        return root;
    }
    static byte[] Bytes(JsonNode node)=>System.Text.Encoding.UTF8.GetBytes(node.ToJsonString());
    static void MustReject(JsonObject f,string why)
    {
        try { var a=MusketeerArchive.Decode(Bytes(f),out bool u); Check(a==null||u,why); }
        catch(FormatException){n++;} catch(InvalidOperationException){n++;}
    }
    static int Main()
    {
        try{
            var plain=Fixture(false);var plainBytes=Bytes(plain);var old=MusketeerArchive.Decode(plainBytes,out bool unsupported);
            Check(old!=null&&!unsupported,"legacy schema2 readable");Check(old.Encode().SequenceEqual(plainBytes),"unused feature retains exact canonical v2 bytes");
            var fixture=Fixture(true);var archive=MusketeerArchive.Decode(Bytes(fixture),out unsupported);
            Check(archive!=null&&!unsupported,"authorized schema3 compensation ledger readable");
            var encoded=JsonNode.Parse(archive.Encode()).AsObject();
            Check(encoded["schemaVersion"].GetValue<int>()==3,"receipt archive stays schema3");
            var source=encoded["scopes"].AsArray().Single(s=>s["scope"].GetValue<string>()==Source);
            Check(JsonNode.DeepEquals(source,plain["scopes"][0]),"old17 snapshot and baseline retained exactly");
            Check(source["snapshots"][0]["records"].AsArray().Count==17,"17 historical records preserved");
            Check(encoded["compensations"][0]["grantIds"].AsArray().Count==8,"only8 new grant identities");
            Check(encoded["contexts"][0]["epochs"].AsArray().Count==2,"new and historical epochs both retained");
            var again=MusketeerArchive.Decode(archive.Encode(),out unsupported);Check(again!=null&&!unsupported,"schema3 second read");Check(again.Encode().SequenceEqual(archive.Encode()),"ledger encoding stable");
            var bad=Fixture(true);bad["compensations"].AsArray().Add(bad["compensations"][0].DeepClone());MustReject(bad,"duplicate request rejected");
            bad=Fixture(true);bad["compensations"][0]["plan"]["sourceEpoch"]=new string('9',64);MustReject(bad,"unknown historical epoch rejected");
            bad=Fixture(true);bad["compensations"][0]["migrationEpoch"]=new string('9',64);MustReject(bad,"unknown migration epoch rejected");
            bad=Fixture(true);bad["compensations"][0]["plan"]["context"]=new string('9',64);MustReject(bad,"foreign context rejected");
            bad=Fixture(true);bad["compensations"][0]["grantIds"][0]=Id(1);MustReject(bad,"old career cannot become new grant");
            bad=Fixture(true);bad["scopes"][1]["snapshots"][0]["records"]=new JsonArray();MustReject(bad,"complete ledger cannot omit all granted careers");
            bad=Fixture(true);bad["scopes"][1]["snapshots"][0]["records"].AsArray().RemoveAt(7);MustReject(bad,"complete ledger cannot omit eighth granted career");
            var reserved=Fixture(true);foreach(var row in reserved["scopes"][1]["snapshots"][0]["records"].AsArray())row["nativeId"]="";
            Check(MusketeerArchive.Decode(Bytes(reserved),out unsupported)!=null&&!unsupported,"dead grant reservations retain completion without rebirth");
            bad=Fixture(true);bad["compensations"]=new JsonArray();MustReject(bad,"empty version3 ledger rejected");
            bad=Fixture(false);bad["compensations"]=new JsonArray();MustReject(bad,"schema2 unknown ledger field rejected");
            bad=Fixture(true);bad["schemaVersion"]=4;var future=MusketeerArchive.Decode(Bytes(bad),out unsupported);Check(future==null&&unsupported,"unknown future version stays unsupported");
            Console.WriteLine($"Archive assertions {n}/{n} PASS");return 0;
        }catch(Exception e){Console.Error.WriteLine($"Archive assertion {n}: {e.Message}");return 1;}
    }
}
