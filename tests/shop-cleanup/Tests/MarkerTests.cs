// MarkerTests.cs — the legacy "MyMod_BerserkerShop" clone path (the other DestroyImmediate site),
// including the Legacy→Placed upgrade when the same object also occupies a ShieldShop slot.
using System;
using UnityEngine;

namespace ShopCleanupTests
{
    internal static class MarkerTests
    {
        private const float PastBackoff = 6f;

        internal static void Register()
        {
            Runner.Add("legacy marker is registered then cleaned exactly once", MarkerRegisteredThenCleanedOnce);
            Runner.Add("marker whose slot belongs to another shop retires", MarkerSlotTakenByCorrectShopRetires);
            Runner.Add("marker without ShopTag is cleaned without slot writes", MarkerWithoutShopTagCleanedWithoutSlotWrites);
            Runner.Add("a real Berserker marker is left alone", RealBerserkerMarkerLeftAlone);
            Runner.Add("marker cleanup refills only after the real destruction", MarkerCleanupRefillsAfterDestruction);
            Runner.Add("legacy entry upgrades to a slot entry for the same object", LegacyUpgradesToSlotEntry);
            Runner.Add("legacy marker with a changed live tag retires without action", LegacyLiveTagChangeRetires);
        }

        private static void MarkerRegisteredThenCleanedOnce()
        {
            Sim.Boot();
            GameObject marker = Shops.PlaceLegacyMarker(Sim.Planner, PayableShop.ShopType.Bow);
            PayableShop markerShop = marker.GetComponent<PayableShop>();
            Sim.ClearPlacementBookkeeping();

            bool pending = TestApi.Ensure();

            Check.Equal(1, TestApi.PendingCount, "the legacy marker registers exactly one cleanup");
            Check.Equal(0, Sim.DestroyCalls, "the callback does not destroy the marker");
            Check.True(marker != null && marker.Alive && marker.ActiveSelf, "the marker is untouched by the callback");
            Check.False(pending,
                "a legacy-only entry does not block the ShieldShop slots (it is not a slot entry)");

            TestApi.Tick();

            Check.Equal(1, Sim.Planner.RemoveShopCalls, "the marker is deregistered once (its recorded slot still held it)");
            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "the Bow slot registration is cleared once");
            Check.Null(TestApi.Slot(PayableShop.ShopType.Bow), "the slot is empty");
            Check.False(marker.ActiveSelf, "the marker is deactivated");
            Check.Equal(1, Sim.DestroyCalls, "one deferred destroy");
            Check.True(marker.Alive, "the destroy is deferred, not immediate");

            Sim.FlushDestruction();
            Check.Equal(1, markerShop.OnDestroyCalls, "the native marker OnDestroy ran");
            Check.Equal(2, Sim.Planner.RemoveShopCalls, "the marker's own OnDestroy calls RemoveShop again");
            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "the second call clears nothing");

            TestApi.Tick();

            Check.Equal(0, TestApi.PendingCount, "the marker entry completes");
            Check.True(TestApi.Log.InfosContain("Shop cleanup finished"), "the completion is logged");
        }

        private static void MarkerSlotTakenByCorrectShopRetires()
        {
            Sim.Boot();
            GameObject marker = Shops.PlaceLegacyMarker(Sim.Planner, PayableShop.ShopType.Bow);
            TestApi.Ensure();

            // 正确的商店接管了 Bow 槽位（第三方或原生补位）。
            GameObject correct = Shops.Place(Sim.Planner, PayableShop.ShopType.Bow, Shops.BerserkerTag, "ShopBowNative");
            Sim.ClearPlacementBookkeeping();

            TestApi.Tick();

            Check.Equal(0, Sim.Planner.RemoveShopCalls, "the marker is not deregistered when its recorded slot belongs to another shop");
            Check.Equal(0, Sim.DestroyCalls, "and it is not destroyed");
            Check.True(Sim.Planner.HasPlacedShop(PayableShop.ShopType.Bow, correct), "the other shop's registration is untouched");
            Check.Equal(0, TestApi.PendingCount, "the entry retires");
            Check.True(TestApi.Log.InfosContain("holds a different shop"), "the reason is logged");
        }

        private static void MarkerWithoutShopTagCleanedWithoutSlotWrites()
        {
            Sim.Boot();

            // 旧克隆有时连 ShopTag 都没有：没有标签就没有槽位写入可言，销毁是安全的。
            var markerGo = new GameObject(Shops.MarkerName);
            markerGo.transform.Parent = Sim.World.gameLayer;
            PayableShop markerShop = markerGo.AddComponent<PayableShop>();
            markerShop.itemPrefab = Shops.MakeItem(Shops.LegacyWrongTag);

            // 槽位被别人占着：这份记录里的对象没有 tag，不能因此被拿去比对默认槽位（Bow）。
            GameObject occupant = Shops.Place(Sim.Planner, PayableShop.ShopType.Bow, Shops.BerserkerTag, "ShopBowOccupant");
            Sim.ClearPlacementBookkeeping();

            TestApi.Ensure();
            TestApi.Tick();

            Check.Equal(0, Sim.Planner.RemoveShopCalls, "without a ShopTag there is nothing to deregister");
            Check.Equal(1, Sim.DestroyCalls, "the marker is still cleaned with a deferred destroy");
            Check.True(Sim.Planner.HasPlacedShop(PayableShop.ShopType.Bow, occupant), "the occupying shop is untouched");
            Check.False(markerGo.ActiveSelf, "the marker is deactivated");
        }

        private static void RealBerserkerMarkerLeftAlone()
        {
            Sim.Boot();
            GameObject marker = Shops.Place(Sim.Planner, PayableShop.ShopType.Bow, Shops.BerserkerTag, Shops.MarkerName);
            Sim.ClearPlacementBookkeeping();

            Check.False(TestApi.Ensure(), "a real Berserker shop is not registered for cleanup");
            Check.Equal(0, TestApi.PendingCount, "nothing is registered");

            TestApi.Tick();

            Check.Equal(0, Sim.DestroyCalls, "nothing is destroyed");
            Check.Equal(0, Sim.Planner.RemoveShopCalls, "nothing is deregistered");
            Check.True(marker != null && marker.Alive && marker.ActiveSelf, "the shop keeps operating");
        }

        private static void MarkerCleanupRefillsAfterDestruction()
        {
            Sim.Boot();
            Shops.PlaceLegacyMarker(Sim.Planner, PayableShop.ShopType.Bow);
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            TestApi.Tick();
            Check.False(Sim.Planner.QueuedTypes.Contains(TestApi.Left),
                "no ShieldShopLeft placement while the marker is still alive");
            TestApi.Tick();
            Check.Equal(1, Sim.DestroyCalls, "the destroy request is not repeated every frame");

            Sim.FlushDestruction();
            TestApi.Tick();

            Check.Equal(0, TestApi.PendingCount, "the marker entry completes after destruction");
            Check.True(Sim.Planner.QueuedTypes.Contains(TestApi.Left),
                "the ShieldShopLeft slot is filled again through the completion callback");
            Check.Equal((Side?)Side.Left, Sim.Planner.QueueSides[TestApi.Left], "left side is requested");
        }

        private static void LegacyUpgradesToSlotEntry()
        {
            Sim.Boot();

            // 旧命名残留恰好注册在 ShieldShopLeft 槽位上：先被名字发现，再被槽位检查发现。
            GameObject marker = Shops.Place(Sim.Planner, TestApi.Left, Shops.LegacyWrongTag, Shops.MarkerName);
            Sim.ClearPlacementBookkeeping();

            bool pending = TestApi.Ensure();

            Check.True(pending, "the same object is registered as a pending cleanup");
            Check.Equal(1, TestApi.PendingCount, "the two detections merge into one entry");
            Check.True(TestApi.Pending(Sim.Planner, TestApi.Left),
                "the entry is upgraded to a slot entry, so the slot cannot be refilled early");
            Check.False(Sim.Planner.QueuedTypes.Contains(TestApi.Left),
                "no premature refill of the slot while the cleanup is pending");

            TestApi.Tick();
            Check.Equal(1, Sim.Planner.RemoveShopCalls, "the upgraded entry deregisters its slot once");
            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "and the slot is released once");
            Check.True(marker != null && marker.Alive && !marker.ActiveSelf, "the old shop is deactivated and deferred");

            Sim.FlushDestruction();
            TestApi.Tick();

            Check.Equal(0, TestApi.PendingCount, "the cleanup completes");
            Check.True(Sim.Planner.QueuedTypes.Contains(TestApi.Left), "and the slot is refilled afterwards");
        }

        private static void LegacyLiveTagChangeRetires()
        {
            Sim.Boot();
            GameObject marker = Shops.PlaceLegacyMarker(Sim.Planner, PayableShop.ShopType.Bow);
            TestApi.Ensure();
            Check.Equal(1, TestApi.PendingCount, "precondition: the marker is registered");
            Sim.ClearPlacementBookkeeping();

            // 动态 tag 被改：登记时记录的槽位不再可信，不能拿新 tag 去注销别的槽位。
            marker.GetComponent<ShopTag>().type = PayableShop.ShopType.Scythe;

            TestApi.Tick();

            Check.Equal(0, Sim.Planner.RemoveShopCalls, "no deregistration under a changed tag");
            Check.Equal(0, Sim.DestroyCalls, "and no destroy");
            Check.True(marker != null && marker.Alive && marker.ActiveSelf, "the object is left untouched");
            Check.Equal(0, TestApi.PendingCount, "the entry retires");
            Check.True(TestApi.Log.InfosContain("shop tag changed"), "the reason is logged");
        }
    }
}
