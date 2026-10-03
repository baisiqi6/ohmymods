using System;
using System.Collections.Generic;

namespace MapWidthTests
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

        internal static void Same(object expected, object actual, string message)
        {
            if (!ReferenceEquals(expected, actual)) throw new Exception(message + " (reference mismatch)");
        }

        internal static void NotSame(object unexpected, object actual, string message)
        {
            if (ReferenceEquals(unexpected, actual)) throw new Exception(message + " (unexpected same reference)");
        }

        internal static void Contains(string fragment, IReadOnlyList<string> lines, string message)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].IndexOf(fragment, StringComparison.Ordinal) >= 0) return;
            }
            throw new Exception(message + " [missing fragment=" + fragment + " lines=" + lines.Count + "]");
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
                Console.WriteLine("  FAIL  " + name + " -> " + e.Message);
            }
        }
    }

    internal static class Program
    {
        private static int Main()
        {
            Console.WriteLine("map-width planner/bridge tests (console / serial)");
            Console.WriteLine();
            KingdomEnhancedMod.TestLog.Install();

            PlannerTests.Run();
            BridgeTests.Run();
            FinalizerTests.Run();

            Console.WriteLine();
            Console.WriteLine("total: passed=" + Case.Passed + " failed=" + Case.Failed);
            for (int i = 0; i < Case.Failures.Count; i++) Console.WriteLine("FAILED: " + Case.Failures[i]);
            return Case.Failed == 0 && Case.Passed > 0 ? 0 : 1;
        }
    }

    internal static class Candidates
    {
        /// <summary>标准岛（level1/3/4 静态取证）：Forest 8；Clearing 12/16/20。</summary>
        internal static List<KingdomEnhancedMod.MapWidthCandidate> Standard(bool includeForestFirst = false)
        {
            var list = new List<KingdomEnhancedMod.MapWidthCandidate>();
            if (includeForestFirst) list.Add(new KingdomEnhancedMod.MapWidthCandidate("Forest_Blocks|3|8", 8, false));
            list.Add(new KingdomEnhancedMod.MapWidthCandidate("Clearing Small_Blocks|5|12", 12, true));
            list.Add(new KingdomEnhancedMod.MapWidthCandidate("Clearing_Blocks|5|16", 16, true));
            list.Add(new KingdomEnhancedMod.MapWidthCandidate("Clearing Large_Blocks|5|20", 20, true));
            if (!includeForestFirst) list.Add(new KingdomEnhancedMod.MapWidthCandidate("Forest_Blocks|3|8", 8, false));
            return list;
        }

        /// <summary>Norse 取证宽度：Forest 8；Clearing 16/20（无 Small）。</summary>
        internal static List<KingdomEnhancedMod.MapWidthCandidate> Norse()
        {
            return new List<KingdomEnhancedMod.MapWidthCandidate>
            {
                new KingdomEnhancedMod.MapWidthCandidate("Clearing_Blocks|5|16", 16, true),
                new KingdomEnhancedMod.MapWidthCandidate("Clearing Large_Blocks|5|20", 20, true),
                new KingdomEnhancedMod.MapWidthCandidate("Forest_Blocks|3|8", 8, false),
            };
        }
    }
}
