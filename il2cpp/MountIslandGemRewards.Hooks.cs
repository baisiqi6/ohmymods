#if !ANDROID
using System;
using HarmonyLib;

namespace KingdomEnhancedMod;

internal static partial class MountIslandGemRewards
{
    // Unique, exception-rethrowing bodies: Mac240 0x6f4e38 (488B), Win243 0x1805c2e90 (480B).
    [HarmonyPatch(typeof(Level), nameof(Level.OnLevelLoaded))]
    private static class LevelLoadedPatch
    {
        [HarmonyPrefix] private static void Prefix(Level __instance, out LevelLoadScope __state)
            => __state = BeginLevelLoad(__instance);
        [HarmonyFinalizer] private static Exception Finalizer(LevelLoadScope __state, bool __runOriginal, Exception __exception)
        { EndLevelLoad(__state, __runOriginal && __exception == null); return __exception; }
    }
    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.Save), new[] { typeof(int), typeof(int), typeof(int) })]
    private static class IslandCapturePatch
    {
        [HarmonyPrefix] private static void Prefix(int __0, int __1, int __2, out SaveScope __state)
            => __state = BeginIslandSave(__0, __1, __2);
        [HarmonyFinalizer] private static Exception Finalizer(SaveScope __state, bool __runOriginal, Exception __exception)
        { EndIslandSave(__state, __runOriginal && __exception == null); return __exception; }
    }
    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.UpdateSavedWithRevisions))]
    private static class IslandMarkerPatch
    {
        [HarmonyPostfix] private static void Postfix(IslandSaveData __instance, bool __runOriginal)
            => ObserveMarker(__instance, __runOriginal);
    }
    [HarmonyPatch(typeof(PrefsSaveData), nameof(PrefsSaveData.PrepareBeforeSave))]
    private static class PreparePatch
    {
        [HarmonyPrefix] private static void Prefix(PrefsSaveData __instance, out PrepareScope __state)
            => __state = BeginPrepare(__instance);
        [HarmonyFinalizer] private static Exception Finalizer(PrepareScope __state, bool __runOriginal, Exception __exception)
        { if (__exception == null) EndPrepare(__state, __runOriginal); else PrepareFailed(); return __exception; }
    }
    // Current Mac240 native body 0xae02bc and Win243 0x18099b3d0 are long unique MoveNext bodies.
    // Player OnTriggerStay2D inlines the factory; never rely on a SpillCurrency factory hook.
    [HarmonyPatch(typeof(Chest._SpillCurrency_d__26), nameof(Chest._SpillCurrency_d__26.MoveNext))]
    private static class SpillPatch
    {
        [HarmonyPrefix] private static void Prefix(Chest._SpillCurrency_d__26 __instance, out SpillCall __state)
            => __state = BeginSpill(__instance);
        [HarmonyPostfix] private static void Postfix(SpillCall __state, ref bool __result, bool __runOriginal)
        { if (__runOriginal) EndSpill(__state, ref __result); }
        [HarmonyFinalizer] private static Exception Finalizer(SpillCall __state, Exception __exception)
        { if (__exception != null) FailSpill(__state); return __exception; }
    }

}
#endif
