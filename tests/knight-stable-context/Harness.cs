using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using KingdomEnhancedMod;

namespace KnightStableContextTests
{
    internal static class Check
    {
        internal static void True(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        internal static void False(bool condition, string message)
        {
            if (condition) throw new Exception(message);
        }

        internal static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message + " [expected=" + expected + " actual=" + actual + "]");
            }
        }

        internal static void NotEqual<T>(T unexpected, T actual, string message)
        {
            if (EqualityComparer<T>.Default.Equals(unexpected, actual))
            {
                throw new Exception(message + " [unexpected=" + unexpected + "]");
            }
        }

        internal static void Same(Guid expected, Guid actual, string message)
        {
            if (expected != actual) throw new Exception(message + " [expected=" + expected + " actual=" + actual + "]");
        }
    }

    internal static class Case
    {
        internal static int Passed;
        internal static int Failed;
        internal static readonly List<string> Failures = new List<string>();

        internal static void Run(string name, Action body)
        {
            try
            {
                body();
                Passed++;
                Console.WriteLine("  PASS  " + name);
            }
            catch (Exception e)
            {
                Failed++;
                Failures.Add(name + " -> " + e.Message);
                Console.WriteLine("  FAIL  " + name);
                Console.WriteLine("        " + e.Message);
            }
        }
    }

    internal static class Build
    {
        internal static Guid Seed(int index)
        {
            return Guid.ParseExact((index + 1).ToString("x8") + new string('0', 24), "N");
        }

        /// <summary>第 n 个合法 64-hex scope/context 值。</summary>
        internal static string Hex64(int index)
        {
            return index.ToString("x", CultureInfo.InvariantCulture).PadLeft(KnightIdentityFingerprint.HexLength, '0');
        }

        internal static KnightIdentitySnapshotEntry E(string uniqueId, int seed, int style)
        {
            return new KnightIdentitySnapshotEntry(uniqueId, new KnightIdentityReceipt(Seed(seed), style));
        }

        internal static KnightIdentitySnapshot Snapshot(int kind, string hash, DateTimeOffset savedAt, params KnightIdentitySnapshotEntry[] entries)
        {
            if (!KnightIdentitySnapshot.TryCreate(kind, hash, savedAt, entries, out KnightIdentitySnapshot snapshot, out string error))
            {
                throw new Exception("snapshot build failed: " + error);
            }
            return snapshot;
        }

        internal static KnightIdentitySnapshot Snapshot(int kind, string hash, params KnightIdentitySnapshotEntry[] entries)
        {
            return Snapshot(kind, hash, DateTimeOffset.UnixEpoch, entries);
        }

        /// <summary>一份最小原生岛 JSON：三个活时钟 + 一个对象载荷（可改 marker 模拟对象变化）。</summary>
        internal static string IslandJson(string marker, double playTimeDays = 10, double lastPlayedTimeDays = 9, double islandTimePlayed = 5000)
        {
            StringBuilder builder = new StringBuilder(256);
            builder.Append("{\"playTimeDays\":").Append(playTimeDays.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"lastPlayedTimeDays\":").Append(lastPlayedTimeDays.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"_islandTimePlayed\":").Append(islandTimePlayed.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"lastPlayedReign\":1,\"biome\":0,\"land\":9,\"objects\":[{\"name\":\"").Append(marker)
                .Append("\",\"uniqueID\":\"knight-1\",\"components\":[{\"name\":\"Knight\",\"type\":\"KnightData\",\"data\":\"{\\\"rank\\\":1}\"}]}]}");
            return builder.ToString();
        }

        internal static KnightIdentityArchive Archive(string scope, KnightIdentitySnapshot snapshot)
        {
            KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
            KnightIdentityArchive.MutationStatus status = archive.RecordSnapshot(scope, snapshot);
            if (status != KnightIdentityArchive.MutationStatus.Applied) throw new Exception("fixture record must apply, got " + status);
            return archive;
        }

        internal static string Context(int land)
        {
            return KnightIdentityArchive.ContextKey("campaign-slot-1.save", 0, 0, land);
        }
    }
}
