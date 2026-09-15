using System;
using System.Collections.Generic;
using System.IO;
using KingdomEnhancedMod;

namespace KnightIdentityRuntimeTests
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

    /// <summary>worker 目录内的临时目录：只删自己这一棵，绝不扫别的目录。</summary>
    internal sealed class TempDir : IDisposable
    {
        internal readonly string Path;

        internal TempDir()
        {
            Path = System.IO.Path.Combine(AppContext.BaseDirectory, "kir-tmp", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
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

    /// <summary>一条用例的 host + sidecar 环境（逐例隔离，串行）。</summary>
    internal sealed class Fixture : IDisposable
    {
        internal readonly TempDir Temp = new TempDir();

        internal Fixture(string filename = "campaign-slot-1.save", int campaign = 0, int challenge = 0, int world = 0x7777)
        {
            NativeSim.ResetAll();
            BepInEx.Paths.ConfigPath = Temp.Path;
            GlobalSaveData.filename = filename;
            GlobalSaveData.loaded = new GlobalSaveData { currentCampaign = campaign, currentChallenge = challenge };
            NativeSim.ResetWorld(world);
        }

        internal string SidecarPath
        {
            get { return System.IO.Path.Combine(Temp.Path, "KingdomEnhancedMod", "ModSave", "knight-identities.v1.json"); }
        }

        internal bool SidecarExists
        {
            get { return File.Exists(SidecarPath); }
        }

        public void Dispose()
        {
            Temp.Dispose();
        }
    }

    internal static class Styles
    {
        internal static readonly IReadOnlyList<int> All = new List<int> { 0, 1, 2, 3, 4 };

        internal static IReadOnlyList<int> Of(params int[] values)
        {
            return values;
        }
    }
}
