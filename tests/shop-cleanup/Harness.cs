// Harness.cs — assertion helpers, the case runner, and the shop fixtures the cases use.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShopCleanupTests
{
    internal static class Check
    {
        internal static void True(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        internal static void False(bool condition, string message) => True(!condition, message);

        internal static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception(message + " (expected " + expected + ", actual " + actual + ")");
        }

        internal static void NotNull(object value, string message)
        {
            if (value == null) throw new Exception(message);
        }

        internal static void Null(object value, string message)
        {
            if (value != null) throw new Exception(message);
        }

        internal static void Contains(string haystack, string needle, string message)
        {
            if (haystack == null || !haystack.Contains(needle))
                throw new Exception(message + " :: " + (haystack ?? "<null>"));
        }
    }

    /// <summary>One case: shared static state is reset before every body (see Runner).</summary>
    internal sealed class Case
    {
        internal readonly string Name;
        internal readonly Action Body;

        internal Case(string name, Action body)
        {
            Name = name;
            Body = body;
        }
    }

    internal static class Runner
    {
        private static readonly List<Case> Cases = new List<Case>();

        internal static void Add(string name, Action body) => Cases.Add(new Case(name, body));

        internal static int Run(string[] filters)
        {
            int passed = 0;
            int total = 0;
            for (int i = 0; i < Cases.Count; i++)
            {
                Case current = Cases[i];
                if (!Matches(current.Name, filters)) continue;
                total++;
                try
                {
                    Sim.Reset();
                    current.Body();
                    passed++;
                    Console.WriteLine("PASS " + current.Name);
                }
                catch (Exception e)
                {
                    Console.WriteLine("FAIL " + current.Name + ": " + e.Message);
                    Console.WriteLine("     at " + e.StackTrace);
                }
            }
            Console.WriteLine("RESULT " + passed + "/" + total + " passed");
            return passed == total ? 0 : 1;
        }

        private static bool Matches(string name, string[] filters)
        {
            if (filters == null || filters.Length == 0) return true;
            for (int i = 0; i < filters.Length; i++)
            {
                if (name.IndexOf(filters[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Native-style shop placement: a GameObject under the world game layer with ShopTag +
    /// PayableShop, registered through AddShop the way PayableShop.Awake does.
    /// </summary>
    internal static class Shops
    {
        internal const string BerserkerTag = "BerserkerTool";
        internal const string LegacyWrongTag = "ToolBow"; // 旧版克隆卖的东西
        internal const string MarkerName = "MyMod_BerserkerShop";

        internal static GameObject Place(ShopPlanner planner, PayableShop.ShopType slot, string itemTag,
            string name = null)
        {
            var go = new GameObject(name ?? ("Shop_" + slot));
            go.transform.Parent = Sim.World.gameLayer;

            ShopTag tag = go.AddComponent<ShopTag>();
            tag.type = slot;

            PayableShop shop = go.AddComponent<PayableShop>();
            shop.itemPrefab = MakeItem(itemTag);

            planner.AddShop(go);
            return go;
        }

        internal static GameObject PlaceWrong(ShopPlanner planner, PayableShop.ShopType slot)
            => Place(planner, slot, LegacyWrongTag, "ShopWrong_" + slot);

        internal static GameObject PlaceBerserker(ShopPlanner planner, PayableShop.ShopType slot)
            => Place(planner, slot, BerserkerTag, "ShopBerserker_" + slot);

        internal static GameObject PlaceLegacyMarker(ShopPlanner planner, PayableShop.ShopType slot)
            => Place(planner, slot, LegacyWrongTag, MarkerName);

        internal static Droppable MakeItem(string itemTag)
        {
            var go = new GameObject("Item_" + itemTag);
            go.Tag = itemTag;
            return go.AddComponent<Droppable>();
        }

        /// <summary>Greece world with one wrong shop occupying the ShieldShopLeft slot.</summary>
        internal static GameObject WrongShopInLeftSlot()
        {
            Sim.Boot();
            GameObject wrong = PlaceWrong(Sim.Planner, PayableShop.ShopType.ShieldShopLeft);
            Sim.ClearPlacementBookkeeping();
            return wrong;
        }
    }

    internal static class TestApi
    {
        internal const PayableShop.ShopType Left = PayableShop.ShopType.ShieldShopLeft;
        internal const PayableShop.ShopType Right = PayableShop.ShopType.ShieldShopRight;

        internal static bool Ensure() => KingdomEnhancedMod.PatchRoles_Castle.EnsureBerserkerToolShopInGreece(Sim.Castle);

        internal static void Tick() => KingdomEnhancedMod.ShopCleanupQueue.TickPendingCleanup();

        internal static int PendingCount => KingdomEnhancedMod.ShopCleanupQueue.PendingCount;

        internal static bool Pending(ShopPlanner planner, PayableShop.ShopType type)
            => KingdomEnhancedMod.ShopCleanupQueue.IsPending(planner, type);

        internal static ManualLogSource Log => KingdomEnhancedPlugin.Instance.LogSource;

        internal static GameObject Slot(PayableShop.ShopType type) => Sim.Planner._placedShops[(int)type];
    }
}
