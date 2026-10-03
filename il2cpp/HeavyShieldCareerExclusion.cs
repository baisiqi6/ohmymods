using HarmonyLib;

namespace KingdomEnhancedMod;

// Actual 2.4: SetEmbarkableTarget 0x4fd4c0 (380 bytes) and Embark 0x4fc500
// (477 bytes) are unique long bodies, and neither checks component.enabled.
// Null target is native cleanup, so it must remain available during borrowing.
internal static class HeavyShieldCareerExclusion
{
    [HarmonyPatch(typeof(Embarkee), nameof(Embarkee.SetEmbarkableTarget),
        new[] { typeof(Embarkable), typeof(int) })]
    internal static class TargetPatch
    {
        [HarmonyPrefix]
        private static bool Before(Embarkee __instance, Embarkable __0)
            => __0 == null || __instance == null
                || !HeavyShieldIdentity.IsKnownCareerRoot(__instance.gameObject);
    }

    [HarmonyPatch(typeof(Embarkee), nameof(Embarkee.Embark), new System.Type[0])]
    internal static class EmbarkPatch
    {
        [HarmonyPrefix]
        private static bool Before(Embarkee __instance)
            => __instance == null || !HeavyShieldIdentity.IsKnownCareerRoot(__instance.gameObject);
    }
}
