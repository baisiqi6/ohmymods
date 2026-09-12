using System;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// Adjusts native passenger capacities on the main Boat only.
/// Archer capacity remains the native four-slot layout.
/// </summary>
[HarmonyPatch(typeof(Boat), nameof(Boat.OnEnable))]
public static class Boat_MainCapacity_Patch
{
    public sealed class State
    {
        public bool Applied;
        public int Workers;
        public int Knights;
        public int Pikemen;
        public int Farmers;
    }

    [HarmonyPrefix]
    public static void Prefix(Boat __instance, out State __state)
    {
        __state = new State();
        if (!ModConfig.Enabled.Value || __instance == null) return;

        __state.Applied = true;
        __state.Workers = __instance.maxWorkers;
        __state.Knights = __instance.maxKnights;
        __state.Pikemen = __instance.maxPikemen;
        __state.Farmers = __instance.maxFarmers;
        __instance.maxWorkers = 8;
        __instance.maxKnights = 6;
        __instance.maxPikemen = 8;
        __instance.maxFarmers = 3;
    }

    [HarmonyPostfix]
    public static void Postfix(Boat __instance, State __state) => Restore(__instance, __state);

    [HarmonyFinalizer]
    public static Exception Finalizer(Boat __instance, State __state, Exception __exception)
    {
        Restore(__instance, __state);
        return __exception;
    }

    private static void Restore(Boat boat, State state)
    {
        if (boat == null || state == null || !state.Applied) return;
        boat.maxWorkers = state.Workers;
        boat.maxKnights = state.Knights;
        boat.maxPikemen = state.Pikemen;
        boat.maxFarmers = state.Farmers;
        state.Applied = false;
    }
}
