using System;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// Prevents enemies from selecting or picking up hermits as loot.
///
/// Troll.ShouldGrabLoot and Troll.PickupLoot both consult
/// Droppable.CanBePickedUpByEnemy(). Returning false only for a Droppable that
/// belongs to a Hermit keeps the native targeting/pickup pipeline intact for
/// coins, gems, dogs, cats, tools, crowns, and every other droppable.
/// It deliberately does not change damage handling, movement, mounting, or
/// building-upgrade behaviour, so hermits are protected from kidnapping rather
/// than made globally invulnerable.
/// </summary>
[HarmonyPatch(typeof(Droppable), nameof(Droppable.CanBePickedUpByEnemy))]
public static class PatchRoles_Hermit
{
    private static bool _loggedFirstBlockedPickup;

    [HarmonyPostfix]
    public static void CanBePickedUpByEnemy_Postfix(Droppable __instance, ref bool __result)
    {
        if (!ModConfig.Enabled.Value || !__result || __instance == null)
        {
            return;
        }

        try
        {
            if (__instance.GetComponent<Hermit>() != null)
            {
                __result = false;
                if (!_loggedFirstBlockedPickup)
                {
                    _loggedFirstBlockedPickup = true;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        "[Roles] Prevented an enemy from kidnapping a hermit.");
                }
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}
