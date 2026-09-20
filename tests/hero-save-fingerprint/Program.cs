using System;
using System.IO;
using System.Text.Json.Nodes;
using KingdomEnhancedMod;

var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../docs/project-harness/tasks/hero-save-restore-20260915/receipts/latest-current-island.json"));
var text=File.ReadAllText(path);
var original=JsonNode.Parse(text);
const string scope="fixture-scope";
string first=HeroRecruitmentFingerprint.Hash(text,scope);
int count=0;
void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
foreach(string field in new[]{"playTimeDays","lastPlayedTimeDays","_islandTimePlayed"})
{
    var node=original.DeepClone();node[field]=123456;
    Check(first==HeroRecruitmentFingerprint.Hash(node.ToJsonString(),scope),"top clock ignored: "+field);
}
var changed=original.DeepClone();changed["land"]=123;
Check(first!=HeroRecruitmentFingerprint.Hash(changed.ToJsonString(),scope),"island identity preserved");
changed=original.DeepClone();changed["objects"].AsArray().RemoveAt(0);
Check(first!=HeroRecruitmentFingerprint.Hash(changed.ToJsonString(),scope),"complete roster preserved");
changed=original.DeepClone();changed["objects"][0]["playTimeDays"]=123;
Check(first!=HeroRecruitmentFingerprint.Hash(changed.ToJsonString(),scope),"nested properties not ignored");
changed=original.DeepClone();changed["newNativeField"]=true;
Check(first!=HeroRecruitmentFingerprint.Hash(changed.ToJsonString(),scope),"unknown future properties preserved");
Check(first!=HeroRecruitmentFingerprint.Hash(text,"other-scope"),"epochs separated");
bool invalid=false;try{HeroRecruitmentFingerprint.Hash("{\"objects\":[],\"objects\":[]}",scope);}catch(FormatException){invalid=true;}
Check(invalid,"duplicates rejected");
Console.WriteLine("PASS "+count+" real-island fingerprint assertions");
