using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace MusketeerNativeSaveAdapterTests;

// Fixture builders producing the actual 2.4 global payload shape:
//   root{serializedSaveDataVersion, campaigns[{_islands[{land, objects[{uniqueID,prefabPath,...}]}], challengeId}],
//        challenges[], prefs{srzEntries[{key,val}]}}
internal static class Fx
{
    internal const string NativeName = "global-v35";
    internal const string ArcherPrefab = "Prefabs/Characters/Archer";
    internal const string ToolBowPrefab = "Prefabs/Objects/ToolBow";

    internal static string J(string value) => JsonSerializer.Serialize(value);

    internal static string Row(string uniqueId, string prefabPath, string name = null)
        => "{\"name\":" + J(name ?? uniqueId) + ",\"prefabPath\":" + J(prefabPath)
           + ",\"hierarchyPath\":\"Level/GameLayer/\",\"uniqueID\":" + J(uniqueId) + ",\"mode\":0}";

    internal static string Island(int land, string objectsJson, string extra = "")
        => "{\"playTimeDays\":10.5,\"lastPlayedTimeDays\":11.5,\"_islandTimePlayed\":12,\"repopGrassHack\":false,"
           + "\"biome\":0,\"land\":" + land + ",\"objects\":[" + objectsJson + "],\"lastPlayedReign\":1" + extra + "}";

    internal static string ChallengeIsland(int land, string objectsJson)
        => Island(land, objectsJson);

    internal static string Campaign(string islandsJson, int challengeId = 0)
        => "{\"_islands\":[" + islandsJson + "],\"challengeId\":" + challengeId + ",\"currentLand\":0}";

    internal static string Global(string campaignsJson, string challengesJson, params string[] prefsEntries)
        => "{\"serializedSaveDataVersion\":16,\"campaigns\":[" + campaignsJson + "],\"challenges\":[" + challengesJson
           + "],\"prefs\":{\"srzEntries\":[" + string.Join(",", prefsEntries) + "]}}";

    internal static string Pref(string key, string val) => "{\"key\":" + J(key) + ",\"val\":" + J(val) + "}";

    internal static byte[] Gzip(string json)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(json));
        return output.ToArray();
    }

    internal static string Hex(char c) => new(c, 64);
}
