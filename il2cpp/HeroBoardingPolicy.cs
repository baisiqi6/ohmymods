using HarmonyLib;

namespace KingdomEnhancedMod;

// Actual 2.4: Embarkee.SetEmbarkableTarget 0x4fd4c0 (380 bytes) and Embarkee.Embark 0x4fc500
// (477 bytes) are unique long bodies and neither checks component.enabled. Knight's
// __EmbarkArchers_d__248.MoveNext calls SetEmbarkableTarget(-1) directly, bypassing CanEmbark,
// so the native eligibility predicates alone cannot keep a paid hero off a boat.
//
// Both hooks are read-only gates. A non-null boarding target, or a boarding call, is refused
// only when the embarkee is confirmed to belong to a live purchased career in the current
// island/world (registered candidate identity, never a wrapper guess). Null-target cleanup stays
// available for the native OnDisable/Unregister path, and every other unit keeps the native
// behaviour. The predicates never Tick, never borrow or release an embarkee, never disable or
// restore anything, never clear a target and never redistribute the registrar; they only read
// the existing purchase tables.
internal static class HeroBoardingPolicy
{
    internal static bool BlocksTarget(Embarkee embarkee, Embarkable target)
        => target != null && HeroRecruitment.IsProtectedEmbarkeeCareer(embarkee);

    internal static bool BlocksEmbark(Embarkee embarkee)
        => HeroRecruitment.IsProtectedEmbarkeeCareer(embarkee);

    [HarmonyPatch(typeof(Embarkee), nameof(Embarkee.SetEmbarkableTarget),
        new[] { typeof(Embarkable), typeof(int) })]
    internal static class TargetPatch
    {
        [HarmonyPrefix]
        private static bool Before(Embarkee __instance, Embarkable __0) => !BlocksTarget(__instance, __0);
    }

    [HarmonyPatch(typeof(Embarkee), nameof(Embarkee.Embark), new System.Type[0])]
    internal static class EmbarkPatch
    {
        [HarmonyPrefix]
        private static bool Before(Embarkee __instance) => !BlocksEmbark(__instance);
    }
}
