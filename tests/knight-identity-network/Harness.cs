using System;
using System.Collections.Generic;

namespace KnightIdentityNetworkTests
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
                Failures.Add(name + ": " + e.Message);
                Console.WriteLine("  FAIL  " + name + " -> " + e.Message);
            }
        }
    }
}
