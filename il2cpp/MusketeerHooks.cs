using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class MusketeerHooks
{
    private static bool _shotFault;
    [HarmonyPatch(typeof(ArrowAttack), nameof(ArrowAttack.FireArrowInternal))]
    private static class FirePatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Before(ArrowAttack __instance, GameObject source)
        {
            if (!MusketeerAccess.Enabled || source == null || !MusketeerIdentity.IsMarked(source)) return true;
            try { MusketeerCombat.TryHandleShot(__instance, source); }
            catch (Exception e)
            {
                if (!_shotFault) { _shotFault = true; KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[Musketeer] shot withheld: " + e); }
            }
            return false; // positively identified musketeers never leak a native arrow on failure
        }
    }
    private static bool HeroCannotTakeGun(Peasant peasant, Droppable droppable)
    {
        if (peasant == null || droppable == null) return false;
        var gun = droppable.TryCast<DroppableTool>();
        return gun != null && MusketeerIdentity.IsGun(gun) && HeroRecruitment.HasPurchasedCareer(peasant.GetComponent<Character>());
    }
    // Native target assignment and physical pickup both need the career boundary. A hero
    // demoted to Peasant can pick up an ordinary bow, but not consume another paid career.
    [HarmonyPatch(typeof(Peasant), nameof(Peasant.CanPickupDroppable))]
    private static class EligibilityPatch
    {
        [HarmonyPostfix] private static void After(Peasant __instance, Droppable droppable, ref bool __result)
        { if (__result && HeroCannotTakeGun(__instance, droppable)) __result = false; }
    }
    [HarmonyPatch(typeof(Peasant), nameof(Peasant.SetDroppableTarget))]
    private static class TargetPatch
    {
        [HarmonyPrefix] private static void Before(Peasant __instance, ref Droppable droppable)
        { if (HeroCannotTakeGun(__instance, droppable)) droppable = null; }
    }
    [HarmonyPatch(typeof(Peasant), nameof(Peasant.HandleToolPickup))]
    private static class PickupPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)] private static bool Before(Peasant __instance, DroppableTool tool)
        {
            if (!HeroCannotTakeGun(__instance, tool)) return true;
            __instance.SetDroppableTarget(null);
            return false;
        }
    }
    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.Save), new[] { typeof(int), typeof(int), typeof(int) })]
    private static class CancelPaymentBeforeSavePatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)] private static void Before()
        { try { MusketeerShop.CancelPendingTransactions(); } catch { } }
    }
}
