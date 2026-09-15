using System;
using System.Collections.Generic;

namespace MedievalScatterPolicyTests
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

    /// <summary>stub 对象所有可变状态的写入计数：调用策略前后不变 = 判定纯读（参数不被写）。</summary>
    internal static class Evidence
    {
        internal static int Writes;
        internal static void Wrote() => Writes++;
    }
}
