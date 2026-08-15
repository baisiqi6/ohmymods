using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 每帐篷乞丐上限入口。中央协调器使原生 SlowUpdate 保持存活，
/// 但在正常工作时以 maxBeggars=0 抑制它生成，改由 world-authority
/// 按稳定营地归属与约 6 秒节拍补员。
///
/// 2.4.0 签名验证（interop Assembly-CSharp.dll）：
/// - BeggarCamp.Awake() : void —— 存在（private，interop 公开）
/// - BeggarCamp.spawnInterval : float（公开属性）—— 存在（免反射 SetValue）
/// - BeggarCamp.maxBeggars : int（公开属性）—— 存在（免反射 SetValue）
/// - SpawnBeggar() : void —— 2.4 interop 公开wrapper，中央协调器可直接调用。
/// </summary>
[HarmonyPatch(typeof(BeggarCamp))]
public static class BeggarCamp_Awake_Patch
{
    [HarmonyPatch(nameof(BeggarCamp.Awake))]
    [HarmonyPrefix]
    public static void Awake_Prefix(BeggarCamp __instance)
    {
        PopulationPerformanceCoordinator.CaptureProfile(__instance);
    }

    [HarmonyPatch(nameof(BeggarCamp.Awake))]
    [HarmonyPostfix]
    public static void Awake_Postfix(BeggarCamp __instance)
    {
        if (__instance == null) return;
        if (ModConfig.Enabled.Value)
        {
            PopulationPerformanceCoordinator.ConfigureCamp(__instance);
            PatchRoles_Ninja.EnsureBeggarCampHidingSpots(__instance);
        }
    }

    [HarmonyPatch(nameof(BeggarCamp.OnDestroy))]
    [HarmonyPrefix]
    public static void OnDestroy_Prefix(BeggarCamp __instance)
    {
        PopulationPerformanceCoordinator.ForgetCamp(__instance);
    }
}

[HarmonyPatch(typeof(Beggar))]
public static class Beggar_PopulationLifecycle_Patch
{
    [HarmonyPatch(nameof(Beggar.OnEnable))]
    [HarmonyPrefix]
    public static void OnEnable_Prefix(Beggar __instance)
    {
        PopulationPerformanceCoordinator.BeginBeggarIncarnation(__instance);
    }

    [HarmonyPatch(nameof(Beggar.OnDisable))]
    [HarmonyPrefix]
    public static void OnDisable_Prefix(Beggar __instance)
    {
        PopulationPerformanceCoordinator.ForgetBeggar(__instance);
    }
}
