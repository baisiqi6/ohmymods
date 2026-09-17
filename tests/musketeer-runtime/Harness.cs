using System;
using System.Collections.Generic;

namespace MusketeerRuntimeTests
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
                throw new Exception(message + " [expected=" + expected + " actual=" + actual + "]");
        }

        internal static void Near(double expected, double actual, double tolerance, string message)
        {
            if (Math.Abs(expected - actual) > tolerance)
                throw new Exception(message + " [expected=" + expected + " actual=" + actual + "]");
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
                Console.WriteLine("PASS " + name);
            }
            catch (Exception e)
            {
                Failed++;
                Failures.Add(name + ": " + e.Message);
                Console.WriteLine("FAIL " + name + ": " + e.Message);
            }
        }
    }

    internal static class Program
    {
        private static int Main()
        {
            AnimationTests.Run();
            CombatTests.Run();
            DeerHuntTests.Run();
            RuntimeTests.Run();
            WorldTests.Run();
            VisualTests.Run();

            Console.WriteLine();
            Console.WriteLine("passed=" + Case.Passed + " failed=" + Case.Failed);
            for (int i = 0; i < Case.Failures.Count; i++) Console.WriteLine("  FAIL " + Case.Failures[i]);
            return Case.Failed == 0 ? 0 : 1;
        }
    }
}
