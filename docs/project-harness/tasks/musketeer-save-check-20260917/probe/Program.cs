using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using KingdomEnhancedMod;

// Read only. The production decoder/fingerprint are linked; no Save/Commit is invoked.
string nativePath=args[0], archivePath=args[1];
byte[] nativeBytes=File.ReadAllBytes(nativePath), archiveBytes=File.ReadAllBytes(archivePath);
using var compressed=new MemoryStream(nativeBytes);
using var gzip=new GZipStream(compressed,CompressionMode.Decompress);
using var reader=new StreamReader(gzip);
using var native=JsonDocument.Parse(reader.ReadToEnd());
var global=native.RootElement;
int campaignIndex=global.GetProperty("_currentCampaign").GetInt32(), challenge=global.GetProperty("_currentChallenge").GetInt32();
var campaign=global.GetProperty("campaigns")[campaignIndex];
int land=campaign.GetProperty("currentLand").GetInt32();
var island=campaign.GetProperty("_islands").EnumerateArray().Single(x=>x.GetProperty("land").GetInt32()==land);
string raw=island.GetRawText();
var archive=MusketeerArchive.Decode(archiveBytes,out bool unsupported);
if(unsupported||archive==null)throw new Exception("Archive unsupported");
string contextKey=MusketeerArchive.ContextKey(Path.GetFileName(nativePath),campaignIndex,challenge,land);
if(!archive.Contexts.TryGetValue(contextKey,out var context))throw new Exception("Current native context does not match archive");
var objects=island.GetProperty("objects").EnumerateArray().ToArray();
var matches=new List<object>();
foreach(var scope in context.Epochs){
    string hash=MusketeerArchive.IslandHash(raw,scope);
    foreach(var snapshot in archive.Scopes[scope].Where(s=>s.Hash==hash)){
        var rows=snapshot.Records.Select(r=>new {
            kind=r.Kind,
            nativeId=r.NativeId,
            occurrences=objects.Count(o=>o.TryGetProperty("uniqueID",out var id)&&id.GetString()==r.NativeId)
        }).ToArray();
        matches.Add(new {scope,hash,records=rows.Length,units=rows.Count(r=>r.kind==1),guns=rows.Count(r=>r.kind==2),
            uniqueGuids=snapshot.Records.Select(r=>r.Id).Distinct().Count(),
            uniqueNativeIds=rows.Select(r=>r.nativeId).Distinct().Count(),allRowsExactlyOnce=rows.All(r=>r.occurrences==1),rows});
    }
}
Console.WriteLine(JsonSerializer.Serialize(new{
    nativeSha256=Convert.ToHexString(SHA256.HashData(nativeBytes)),archiveSha256=Convert.ToHexString(SHA256.HashData(archiveBytes)),
    campaign=campaignIndex,challenge,land,biome=island.GetProperty("biome").GetInt32(),
    archiveValid=true,currentContextMatched=true,activeScope=context.Active,matchingSnapshots=matches,
    originalFilesUnchanged=nativeBytes.SequenceEqual(File.ReadAllBytes(nativePath))&&archiveBytes.SequenceEqual(File.ReadAllBytes(archivePath))
},new JsonSerializerOptions{WriteIndented=true}));
