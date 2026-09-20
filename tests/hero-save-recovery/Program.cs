// One local, evidence-bound repair STAGER. It never writes the game or live sidecar.
// Not a runtime fuzzy matcher and not shipped as MOD gameplay code.
using System.Security.Cryptography;
using System.Text.Json;
using KingdomEnhancedMod;

var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
var task=Path.Combine(root,"docs/project-harness/tasks/hero-save-restore-20260915");
var receipts=Path.Combine(task,"receipts");
var candidate="C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hero-save-restore-20260915/candidate";
void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
string Sha(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
var sourceBytes=File.ReadAllBytes(Path.Combine(receipts,"latest-hero-identities.json"));
var archive=HeroRecruitmentArchive.Decode(sourceBytes,out bool unsupported);
Require(!unsupported,"source schema supported");
var json=File.ReadAllText(Path.Combine(receipts,"latest-current-island.json"));
using var island=JsonDocument.Parse(json);
using var oldProof=JsonDocument.Parse(File.ReadAllText(Path.Combine(receipts,"native-proof.json")));
using var proof=JsonDocument.Parse(File.ReadAllText(Path.Combine(receipts,"latest-native-proof.json")));
var log=File.ReadAllText(Path.Combine(receipts,"latest.log"));
int campaign=proof.RootElement.GetProperty("campaign").GetInt32();
int challenge=proof.RootElement.GetProperty("challenge").GetInt32();
int land=proof.RootElement.GetProperty("land").GetInt32();
Require(campaign==oldProof.RootElement.GetProperty("campaign").GetInt32()
    &&challenge==oldProof.RootElement.GetProperty("challenge").GetInt32()
    &&land==oldProof.RootElement.GetProperty("land").GetInt32(),"frozen proofs describe same native campaign/island");
var sourceEpochs=oldProof.RootElement.GetProperty("exactRawIslandMatches").EnumerateArray()
    .Where(x=>x.GetProperty("seats").GetArrayLength()==0)
    .Select(x=>x.GetProperty("scope").GetString())
    .Where(x=>archive.Scopes.ContainsKey(x)&&log.Contains("loaded:"+x.Substring(0,8)+":0"))
    .ToArray();
Require(sourceEpochs.Length==1,"one verified legacy empty-alias epoch in the actual startup log");
string epoch=sourceEpochs[0];
var origin=archive.Scopes[epoch][0];
var lastSaved=System.Text.RegularExpressions.Regex.Matches(log,@"\[HeroRecruitment\] saved:([a-f0-9]{8})").Last();
Require(lastSaved.Groups[1].Value==origin.Hash.Substring(0,8),"last successful sidecar save log matches the source snapshot");
string aliasBaseline=oldProof.RootElement.GetProperty("exactRawIslandMatches").EnumerateArray().Single(x=>x.GetProperty("scope").GetString()==epoch).GetProperty("hash").GetString();
Require(archive.Baselines[epoch]==aliasBaseline,"source epoch still pins the proven legacy empty alias");
var seats=origin.Seats.Select(x=>x.Copy()).ToList();
Require(seats.Count==2&&HeroRecruitmentArchive.ValidSeats(seats),"two existing paid receipts required");
foreach(var seat in seats)
{
    Require(log.Contains("purchased:"+seat.Id.ToString("N")),"paid GUID must appear in frozen purchase log");
    var objects=island.RootElement.GetProperty("objects").EnumerateArray()
        .Where(x=>x.GetProperty("uniqueID").GetString()==seat.NativeId).ToArray();
    Require(objects.Length==1,"native ID must be unique in frozen current island");
    var parts=objects[0].GetProperty("componentData2").EnumerateArray().ToArray();
    Require(parts.Any(x=>x.GetProperty("name").GetString()=="Character"&&x.GetProperty("type").GetString()=="CharacterData"),"native character record required");
    var archer=parts.Single(x=>x.GetProperty("name").GetString()=="Archer");
    using var data=JsonDocument.Parse(archer.GetProperty("data").GetString());
    Require(!data.RootElement.GetProperty("despawnOnLoad").GetBoolean(),"do not restore a record marked despawn-on-load");
}
string context=HeroRecruitmentArchive.ContextKey("global-v35",campaign,challenge,land);
string hash=HeroRecruitmentFingerprint.Hash(json,epoch);
Require(archive.Scopes[epoch].Count<HeroRecruitmentArchive.MaxSnapshots,"repair must not evict history");
Require(!archive.TryGet(epoch,hash,out _),"repair snapshot not already present");
archive.Scopes[epoch].Insert(0,new HeroRecruitmentSnapshot{Hash=hash,Seats=seats,HashKind=HeroRecruitmentFingerprint.Kind,LegacyV1=false});
archive.Baselines[epoch]=hash;
Require(archive.EnsureContext(context,epoch,false),"associate only the verified current epoch");
var bytes=archive.Encode();
var readback=HeroRecruitmentArchive.Decode(bytes,out unsupported);
Require(!unsupported&&readback.TryGet(epoch,hash,out var restored)&&HeroRecruitmentArchive.Same(restored.Seats,seats),"staged decode and exact receipt roundtrip");
var original=HeroRecruitmentArchive.Decode(sourceBytes,out _);
foreach(var pair in original.Scopes)
foreach(var previous in pair.Value)
    Require(readback.TryGet(pair.Key,previous.Hash,out var kept)&&HeroRecruitmentArchive.Same(previous.Seats,kept.Seats),"every previous snapshot and paid GUID retained");
Directory.CreateDirectory(candidate);
File.WriteAllBytes(Path.Combine(candidate,"hero-identities.v2-recovered.json"),bytes);
File.WriteAllText(Path.Combine(receipts,"recovery-staged.json"),JsonSerializer.Serialize(new{
    stagedOnly=true,nativeFileSha256=proof.RootElement.GetProperty("nativeFileSha256").GetString(),
    originalSidecarSha256=Sha(sourceBytes),recoveredSidecarSha256=Sha(bytes),context,epoch,hash,
    sourceHash=origin.Hash,seatIds=seats.Select(x=>x.Id.ToString("N")).ToArray(),
    nativeIds=seats.Select(x=>x.NativeId).ToArray(),oldScopesPreserved=original.Scopes.Count,
    nativeFilesWritten=false,runtimeAliveNotClaimed=true
},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("STAGED ONLY: two existing paid GUIDs and all previous history preserved; no live data written.");
