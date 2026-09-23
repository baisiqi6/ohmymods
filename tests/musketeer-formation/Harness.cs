using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

namespace MusketeerFormationTests
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

        internal static void Sequence<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string message)
        {
            if (expected == null || actual == null || expected.Count != actual.Count)
            {
                int expectedCount = expected?.Count ?? -1;
                int actualCount = actual?.Count ?? -1;
                throw new Exception(message + " [counts differ: expected=" + expectedCount + " actual=" + actualCount + "]");
            }
            for (int i = 0; i < expected.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
                    throw new Exception(message + " [index " + i + " expected=" + expected[i] + " actual=" + actual[i] + "]");
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

    internal static class Fixture
    {
        internal static void Reset()
        {
            MusketeerIdentity.Units.Clear();
            MusketeerIdentity.MarkedEnabled = true;
            MusketeerAccess.Enabled = true;
            MusketeerAccess.InWorldResult = true;
            HeroArcherRuntime.Heroes.Clear();
            CrossbowmanLifecycle.Crossbowmen.Clear();
            CrossbowmanLifecycle.IdentityEnabled = true;
            CrossbowmanLifecycle.ThrowOnRead = false;
            PatchWorld_FleetBoatFormation.MusketeerRow = true;
            PatchWorld_FleetBoatFormation.DirtyFormation = null;
            PatchMusketeerFormation.EndDirected();
        }

        internal static Archer NewArcher(float x, bool marked = true)
        {
            var gameObject = new GameObject();
            Archer archer = gameObject.Add<Archer>();
            archer.transform.position = new Vector3(x, 0f, 0f);
            if (marked) MusketeerIdentity.Units.Add(archer);
            return archer;
        }

        internal static Formation NewFormation(float x,
            Formation.FormationType type = Formation.FormationType.PlayerFormation)
        {
            var gameObject = new GameObject();
            Formation formation = gameObject.Add<Formation>();
            formation.transform.position = new Vector3(x, 0f, 0f);
            formation.formationType = type;
            return formation;
        }
    }
}
