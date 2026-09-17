using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KingdomEnhancedMod;

// Saved object records are frozen by IslandSaveData.Save. The three live island clocks
// can advance before GlobalSaveData.SaveAsync serializes the containing global save.
// Exclude only those top-level clocks; retain every other property and complete object data.
internal static class HeroRecruitmentFingerprint
{
    internal const int Kind = 2;
    internal static string Hash(string json, string scope)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("island object required");
        using var stream = new MemoryStream();
        var names = new HashSet<string>(StringComparer.Ordinal);
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new FormatException("duplicate island property");
                if (property.Name is "playTimeDays" or "lastPlayedTimeDays" or "_islandTimePlayed") continue;
                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("hero-island-objects-v2\n" + scope + "\n"));
        hash.AppendData(stream.ToArray());
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
