using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using KingdomEnhancedMod;
internal static partial class Program
{
    private static byte[] Zip(string value){using var buffer=new MemoryStream();using(var gzip=new GZipStream(buffer,CompressionLevel.Fastest,true)){byte[] bytes=Encoding.UTF8.GetBytes(value);gzip.Write(bytes,0,bytes.Length);}return buffer.ToArray();}
    private static string NativePayload(string batch,string rows="[]",string extras="")
    {
        string heavy="{\"version\":4,\"campaigns\":[{\"slot\":0,\"guid\":\""+A+"\",\"islands\":[]}]}";
        string entries="{\"key\":\""+HeavyShieldSaveSchema.Key+"\",\"val\":"+JsonSerializer.Serialize(heavy)+"}";
        if(batch!=null)entries+=",{\"key\":\""+MountIslandGemRewardState.Key+"\",\"val\":"+JsonSerializer.Serialize(batch)+"}";
        string islands=string.Join(",",new string[]{"null","null","null","null","null","null","null","null","null","null","null","{\"objects\":"+rows+"}"});
        return "{\"_currentCampaign\":0,\"campaigns\":[{\"_islands\":["+islands+"]}],\"prefs\":{\"srzEntries\":["+entries+"]}"+extras+"}";
    }
    private static string PayloadFromNativeCapture()
    {
        var prefs=GlobalSaveData.loaded.prefs;
        string rows=UnityEngine.JsonUtility.ToJson(CampaignSaveData.current.GetIsland(11).objects,false);
        return NativePayload(Saved(A,1),rows);
    }
    static partial void PayloadTests()
    {
        Test("native writer validates batch without any Musketeer key",()=>{var expected=new MountIslandGemNativeExpectation{Slot=0,Guid=A,Batch=Saved(A,1)};expected.Rows[11]=new System.Collections.Generic.List<string>();Equal(true,MountIslandGemNativePayload.Verify(Zip(NativePayload(Saved(A,1))),expected,out _));});
        Test("native writer rejects absent, stale, duplicate and future batch copies",()=>{var e=new MountIslandGemNativeExpectation{Slot=0,Guid=A,Batch=Saved(A,1)};foreach(string batch in new[]{null,Saved(A,2),"future"})Equal(false,MountIslandGemNativePayload.Verify(Zip(NativePayload(batch)),e,out _));string raw=NativePayload(Saved(A,1));raw=raw.Replace("\"srzEntries\":[","\"srzEntries\":[{\"key\":\""+MountIslandGemRewardState.Key+"\",\"val\":"+JsonSerializer.Serialize(Saved(A,1))+"},");Equal(false,MountIslandGemNativePayload.Verify(Zip(raw),e,out _));});
        Test("native writer requires same selected campaign and durable GUID",()=>{var e=new MountIslandGemNativeExpectation{Slot=0,Guid=A,Batch=Saved(A,1)};string raw=NativePayload(Saved(A,1));Equal(false,MountIslandGemNativePayload.Verify(Zip(raw.Replace("\"_currentCampaign\":0","\"_currentCampaign\":1")),e,out _));Equal(false,MountIslandGemNativePayload.Verify(Zip(raw.Replace(A,B)),e,out _));});
        Test("native writer rejects duplicate JSON fields and truncated gzip",()=>{var e=new MountIslandGemNativeExpectation{Slot=0,Guid=A,Batch=Saved(A,1)};Equal(false,MountIslandGemNativePayload.Verify(Zip(NativePayload(Saved(A,1),extras:",\"_currentCampaign\":0")),e,out _));byte[] data=Zip(NativePayload(Saved(A,1)));Array.Resize(ref data,data.Length-4);Equal(false,MountIslandGemNativePayload.Verify(data,e,out _));});
        Test("native writer matches complete frozen owned row including native coins",()=>{var e=new MountIslandGemNativeExpectation{Slot=0,Guid=A,Batch=Saved(A,1)};string row="{\"uniqueID\":\"n1\",\"name\":\"KEM.MountIslandGems.v1/"+A+"/11/1/0\",\"prefabPath\":\"Prefabs/Objects/GemChestFixedAmount\",\"componentData2\":[{\"coins\":7}]}";e.Rows[11]=new System.Collections.Generic.List<string>{row};Equal(true,MountIslandGemNativePayload.Verify(Zip(NativePayload(Saved(A,1),"["+row+"]")),e,out _));Equal(false,MountIslandGemNativePayload.Verify(Zip(NativePayload(Saved(A,1))),e,out _));Equal(false,MountIslandGemNativePayload.Verify(Zip(NativePayload(Saved(A,1),"["+row.Replace("coins\":7","coins\":6")+"]")),e,out _));});
        Test("physical writer rejects swallowed Prefs failure after all native drops",()=>{Setup();MountIslandGemRewards.Tick();foreach(var chest in Boxes())new Chest._SpillCurrency_d__26{__4__this=chest}.MoveNext();Save();GlobalSaveData.loaded.prefs.FailSet=true;try{MountIslandGemRewards.BeginPrepare(GlobalSaveData.loaded.prefs);}catch{}Equal(false,MountIslandGemRewards.CanWriteNativePayload("/test/save","global-v16",Zip(NativePayload(null)),out _));});
        Test("restored new spill needs fresh capture despite durable issuance marker",()=>{Setup();GlobalSaveData.loaded.prefs.contents[MountIslandGemRewardState.Key]=Saved(A,1);var chest=Restore(0);MountIslandGemRewards.Tick();new Chest._SpillCurrency_d__26{__4__this=chest}.MoveNext();Equal(false,MountIslandGemRewards.CanSave(GlobalSaveData.loaded));Equal(false,MountIslandGemRewards.CanWriteNativePayload("/test/save","global-v16",Zip(PayloadFromNativeCapture()),out _));Save();Equal(true,MountIslandGemRewards.CanSave(GlobalSaveData.loaded));});
        Test("cold ordinary island with prior durable marker can still save",()=>{Setup();GlobalSaveData.loaded.prefs.contents[MountIslandGemRewardState.Key]=Saved(A,1);Managers.Inst.game.currentLand=0;MountIslandGemRewards.Tick();Equal(true,MountIslandGemRewards.CanWriteNativePayload("/test/save","global-v16",Zip(NativePayload(Saved(A,1))),out _));});
        Test("switching campaign preserves A proof while allowing B ordinary save",()=>{Setup();MountIslandGemRewards.Tick();Save();string native=PayloadFromNativeCapture();var b=new CampaignSaveData{CurrentLand=0};GlobalSaveData.loaded.campaigns.Add(b);GlobalSaveData.loaded.currentCampaign=1;CampaignSaveData.current=b;Managers.Inst.game.currentLand=0;string map="{\"version\":4,\"campaigns\":[{\"slot\":0,\"guid\":\""+A+"\",\"islands\":[]},{\"slot\":1,\"guid\":\""+B+"\",\"islands\":[]}]}";GlobalSaveData.loaded.prefs.contents[HeavyShieldSaveSchema.Key]=map;var prepare=MountIslandGemRewards.BeginPrepare(GlobalSaveData.loaded.prefs);GlobalSaveData.loaded.prefs.NativePrepare();MountIslandGemRewards.EndPrepare(prepare,true);var node=System.Text.Json.Nodes.JsonNode.Parse(native);node["_currentCampaign"]=1;node["campaigns"].AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("{\"_islands\":[]}"));foreach(var row in node["prefs"]["srzEntries"].AsArray())if(row["key"].GetValue<string>()==HeavyShieldSaveSchema.Key)row["val"]=map;Equal(true,MountIslandGemRewards.CanWriteNativePayload("/test/save","global-v16",Zip(node.ToJsonString()),out _));node["campaigns"][0]["_islands"][11]["objects"]=new System.Text.Json.Nodes.JsonArray();Equal(false,MountIslandGemRewards.CanWriteNativePayload("/test/save","global-v16",Zip(node.ToJsonString()),out _));});
        Test("markerless owned residual remains unsafe even with feature OFF",()=>{Setup();var go=UnityEngine.Object.Instantiate(UnityEngine.Resources.Prefab,Managers.Inst.world.gameLayer,false);go.name="KEM.MountIslandGems.v1/"+A+"/11/1/0";ModConfig.Enabled.Value=false;MountIslandGemRewards.Tick();Equal(false,MountIslandGemRewards.CanSave(GlobalSaveData.loaded));Equal(false,MountIslandGemRewards.CanWriteNativePayload("/test/save","global-v16",Zip(NativePayload(null)),out _));});
        Test("later Prefs copy modification cannot bypass frozen physical proof",()=>{Setup();MountIslandGemRewards.Tick();Save();var prefs=GlobalSaveData.loaded.prefs;var scope=MountIslandGemRewards.BeginPrepare(prefs);prefs.NativePrepare();MountIslandGemRewards.EndPrepare(scope,true);Equal(false,MountIslandGemRewards.CanWriteNativePayload("/test/save","global-v16",Zip(NativePayload("future changed",UnityEngine.JsonUtility.ToJson(CampaignSaveData.current.GetIsland(11).objects,false))),out _));});
        Test("failed prepare recovers only after full normal native prepare",()=>{Setup();MountIslandGemRewards.Tick();Save();var prefs=GlobalSaveData.loaded.prefs;prefs.FailSet=true;try{MountIslandGemRewards.BeginPrepare(prefs);}catch{}Equal(false,MountIslandGemRewards.CanWriteNativePayload("/test/save","global-v16",Zip(PayloadFromNativeCapture()),out _));prefs.FailSet=false;var scope=MountIslandGemRewards.BeginPrepare(prefs);prefs.NativePrepare();MountIslandGemRewards.EndPrepare(scope,true);Equal(true,MountIslandGemRewards.CanWriteNativePayload("/test/save","global-v16",Zip(PayloadFromNativeCapture()),out _));});
        Test("saved unopened row missing after successful native load cannot become an empty proof",()=>{
            Setup();GlobalSaveData.loaded.prefs.contents[MountIslandGemRewardState.Key]=Saved(A,1);
            var island=CampaignSaveData.current.GetIsland(11);
            var saved=new IslandSaveData.ObjectData{uniqueID="saved-missing",name="KEM.MountIslandGems.v1/"+A+"/11/1/0",prefabPath="Prefabs/Objects/GemChestFixedAmount",localPosition=new UnityEngine.Vector3(10,0,1.5f)};
            island.objects.Add(saved);
            var load=MountIslandGemRewards.BeginLevelLoad(Managers.Inst.level);MountIslandGemRewards.EndLevelLoad(load,true);
            Equal(0,Boxes().Length);Save();
            var prepare=MountIslandGemRewards.BeginPrepare(GlobalSaveData.loaded.prefs);GlobalSaveData.loaded.prefs.NativePrepare();MountIslandGemRewards.EndPrepare(prepare,true);
            Equal(false,MountIslandGemRewards.CanWriteNativePayload("/test/save","global-v16",Zip(PayloadFromNativeCapture()),out _));
            Save();Equal(false,MountIslandGemRewards.CanSave(GlobalSaveData.loaded));
            var proofs=(System.Collections.Generic.Dictionary<(string Guid,int Land),System.Collections.Generic.List<string>>)typeof(MountIslandGemRewards).GetField("RowProofs",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);
            Equal(1,proofs[(A,11)].Count);
            using(var raw=JsonDocument.Parse(proofs[(A,11)][0]))Equal("saved-missing",raw.RootElement.GetProperty("uniqueID").GetString());
            // Another successful load with the same native pointers cannot erase the locked unpaid row.
            load=MountIslandGemRewards.BeginLevelLoad(Managers.Inst.level);MountIslandGemRewards.EndLevelLoad(load,true);
            Equal(false,MountIslandGemRewards.CanSave(GlobalSaveData.loaded));Equal(1,proofs[(A,11)].Count);
            // Only an actually restored original snapshot plus its live box can release this failure.
            island.objects.Add(saved);var go=UnityEngine.Object.Instantiate(UnityEngine.Resources.Prefab,Managers.Inst.world.gameLayer,false);go.name=saved.name;go.GetComponent<Chest>().currencyAmount=7;go.transform.localPosition=saved.localPosition;
            load=MountIslandGemRewards.BeginLevelLoad(Managers.Inst.level);MountIslandGemRewards.EndLevelLoad(load,true);
            Equal(true,MountIslandGemRewards.CanSave(GlobalSaveData.loaded));
        });
        Test("completed spill from an older load cannot discharge a newly missing saved box",()=>{
            Setup();GlobalSaveData.loaded.prefs.contents[MountIslandGemRewardState.Key]=Saved(A,1);var chest=Restore(0);MountIslandGemRewards.Tick();
            var island=CampaignSaveData.current.GetIsland(11);var saved=island.objects[0];
            new Chest._SpillCurrency_d__26{__4__this=chest}.MoveNext();Save();UnityEngine.Object.Destroy(chest.gameObject);
            island.objects.Add(saved);var load=MountIslandGemRewards.BeginLevelLoad(Managers.Inst.level);MountIslandGemRewards.EndLevelLoad(load,true);
            Save();var prepare=MountIslandGemRewards.BeginPrepare(GlobalSaveData.loaded.prefs);GlobalSaveData.loaded.prefs.NativePrepare();MountIslandGemRewards.EndPrepare(prepare,true);
            Equal(false,MountIslandGemRewards.CanWriteNativePayload("/test/save","global-v16",Zip(PayloadFromNativeCapture()),out _));
        });
        Test("markerless saved owned row cannot regrant when native creation skipped it",()=>{
            Setup();var island=CampaignSaveData.current.GetIsland(11);
            island.objects.Add(new IslandSaveData.ObjectData{uniqueID="unmarked-missing",name="KEM.MountIslandGems.v1/"+A+"/11/1/0",prefabPath="Prefabs/Objects/GemChestFixedAmount",localPosition=new UnityEngine.Vector3(10,0,1.5f)});
            MountIslandGemRewards.Tick();Equal(0,Boxes().Length);Equal(false,MountIslandGemRewards.CanSave(GlobalSaveData.loaded));
            Equal(false,MountIslandGemRewards.CanWriteNativePayload("/test/save","global-v16",Zip(NativePayload(null)),out _));
        });
        Test("unrelated new selected campaign can first-save without its own durable GUID",()=>{
            Setup();GlobalSaveData.loaded.prefs.contents[MountIslandGemRewardState.Key]=Saved(A,1);
            var load=MountIslandGemRewards.BeginLevelLoad(Managers.Inst.level);MountIslandGemRewards.EndLevelLoad(load,true);
            var b=new CampaignSaveData{CurrentLand=0};GlobalSaveData.loaded.campaigns.Add(b);GlobalSaveData.loaded.currentCampaign=1;CampaignSaveData.current=b;Managers.Inst.game.currentLand=0;
            var prepare=MountIslandGemRewards.BeginPrepare(GlobalSaveData.loaded.prefs);GlobalSaveData.loaded.prefs.NativePrepare();MountIslandGemRewards.EndPrepare(prepare,true);
            var node=System.Text.Json.Nodes.JsonNode.Parse(NativePayload(Saved(A,1)));node["_currentCampaign"]=1;node["campaigns"].AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("{\"_islands\":[]}"));
            Equal(true,MountIslandGemRewards.CanWriteNativePayload("/test/save","global-v16",Zip(node.ToJsonString()),out _));
            foreach(var row in node["prefs"]["srzEntries"].AsArray())if(row["key"].GetValue<string>()==HeavyShieldSaveSchema.Key)row["val"]="{\"version\":4,\"campaigns\":[{\"slot\":0,\"guid\":\""+B+"\",\"islands\":[]}]}";
            Equal(false,MountIslandGemRewards.CanWriteNativePayload("/test/save","global-v16",Zip(node.ToJsonString()),out _));
        });
        Test("non-global physical writer remains native despite malformed payload",()=>{Setup();MountIslandGemRewards.Tick();Save();Equal(false,MountIslandGemRewards.CanWriteNativePayload(null,"global-v16",new byte[]{1},out _));Equal(true,MountIslandGemRewards.CanWriteNativePayload("/other","global-v16",new byte[]{1},out _));Equal(true,MountIslandGemRewards.CanWriteNativePayload("/test/save","other-file",new byte[]{1},out _));});
    }
}
