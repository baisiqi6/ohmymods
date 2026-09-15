using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KingdomEnhancedMod;

namespace KnightIdentityArchiveTests
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

        internal static void Contains(string haystack, string needle, string message)
        {
            if (haystack == null || haystack.IndexOf(needle, StringComparison.Ordinal) < 0)
            {
                throw new Exception(message + " [haystack=" + haystack + "]");
            }
        }

        internal static void Throws<TException>(Action body, string message) where TException : Exception
        {
            try
            {
                body();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception e)
            {
                throw new Exception(message + " [wrong exception " + e.GetType().Name + "]");
            }
            throw new Exception(message + " [no exception]");
        }
    }

    /// <summary>一条用例；共享静态（无）与临时目录逐例隔离，串行执行。</summary>
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

    /// <summary>worker 目录内的临时目录：只删自己这一棵，绝不扫别的目录。</summary>
    internal sealed class TempDir : IDisposable
    {
        internal readonly string Path;

        internal TempDir()
        {
            Path = System.IO.Path.Combine(AppContext.BaseDirectory, "kid-tmp", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string File(string name)
        {
            return System.IO.Path.Combine(Path, name);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
            }
            try
            {
                Directory.Delete(System.IO.Path.GetDirectoryName(Path));
            }
            catch (IOException)
            {
            }
        }
    }

    internal static class Build
    {
        /// <summary>确定性 GUID（Seed(0) 非空）。</summary>
        internal static Guid Seed(int index)
        {
            return Guid.ParseExact((index + 1).ToString("x8") + new string('0', 24), "N");
        }

        internal static string Hex(char c)
        {
            return new string(c, KnightIdentityFingerprint.HexLength);
        }

        /// <summary>第 n 个合法 64-hex scopeKey / snapshotHash。</summary>
        internal static string Hex64(int index)
        {
            return index.ToString("x", CultureInfo.InvariantCulture).PadLeft(KnightIdentityFingerprint.HexLength, '0');
        }

        internal static KnightIdentitySnapshotEntry E(string uniqueId, int seed, int style)
        {
            return new KnightIdentitySnapshotEntry(uniqueId, new KnightIdentityReceipt(Seed(seed), style));
        }

        internal static KnightIdentitySnapshot Snapshot(string hash, params KnightIdentitySnapshotEntry[] entries)
        {
            return Snapshot(hash, DateTimeOffset.UnixEpoch, entries);
        }

        internal static KnightIdentitySnapshot Snapshot(string hash, DateTimeOffset savedAt, KnightIdentitySnapshotEntry[] entries)
        {
            if (!KnightIdentitySnapshot.TryCreate(hash, savedAt, entries, out KnightIdentitySnapshot snapshot, out string error))
            {
                throw new Exception("snapshot build failed: " + error);
            }
            return snapshot;
        }

        internal static KnightIdentityArchive Archive(string scopeKey, string hash, string uniqueId, int seed, int style)
        {
            KnightIdentityArchive archive = KnightIdentityArchive.CreateEmpty();
            KnightIdentityArchive.MutationStatus status = archive.RecordSnapshot(scopeKey, Snapshot(hash, E(uniqueId, seed, style)));
            Check.Equal(KnightIdentityArchive.MutationStatus.Applied, status, "fixture record must apply");
            return archive;
        }

        internal static byte[] Bytes(string text)
        {
            return System.Text.Encoding.UTF8.GetBytes(text);
        }

        internal static void WriteText(string path, string text)
        {
            System.IO.File.WriteAllText(path, text);
        }

        internal static string[] FilesIn(string directory)
        {
            string[] files = Directory.GetFiles(directory);
            for (int i = 0; i < files.Length; i++) files[i] = System.IO.Path.GetFileName(files[i]);
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }
    }
}
