using HarmonyLib;
#if ANDROID
// Android（Il2CppInterop namespace-prefix 模式）把 Assembly-CSharp 的全局类型放在 Il2Cpp.* 下；
// 本文件与 PC 共用同一份逻辑，仅在此把文件用到的游戏类型显式映射到实际 interop 类型。
// 其余代码（含 PC 分支）逐字相同：ANDROID 只新增入口纯托管门，并预处理排除未移植的
// Ninja 藏点一行；PC 预处理输出不变。
using Beggar = Il2Cpp.Beggar;
using BeggarCamp = Il2Cpp.BeggarCamp;
#endif

namespace KingdomEnhancedMod;

/// <summary>
/// 每帐篷乞丐上限入口。中央协调器使原生 SlowUpdate 保持存活，
/// 但在正常工作时以 maxBeggars=0 抑制它生成，改由 world-authority
/// 按稳定营地归属与面板配置节拍补员（默认 120 秒、每营地 4 人）。
/// 容量仅限制后续补员，降低上限或读档均不清除已有乞丐；原生回退最短约 6 秒。
///
/// 2.4.0 签名验证（interop Assembly-CSharp.dll）：
/// - BeggarCamp.Awake() : void —— 存在（private，interop 公开）
/// - BeggarCamp.spawnInterval : float（公开属性）—— 存在（免反射 SetValue）
/// - BeggarCamp.maxBeggars : int（公开属性）—— 存在（免反射 SetValue）
/// - SpawnBeggar() : void —— 2.4 interop 公开wrapper，中央协调器可直接调用。
/// Android：默认 OFF 时以上每个入口先做纯托管门，在 __instance 的 null/Pointer/native
/// 访问之前退出；ON 由真实设置动作/冷加载/晚营地走 Coordinator，无自有责任不写营地参数。
/// </summary>
[HarmonyPatch(typeof(BeggarCamp))]
public static class BeggarCamp_Awake_Patch
{
    [HarmonyPatch(nameof(BeggarCamp.Awake))]
    [HarmonyPrefix]
    public static void Awake_Prefix(BeggarCamp __instance)
    {
#if ANDROID
        // ANDROID：默认 OFF / 本 scene 终态 或 晚营地非当前 layer → 纯托管门在任何原生读取前退出，
        // 不做早捕获（Android 的 Original 只在首次真正写字段前按当前未 owned 值捕获）。
        if (!PopulationPerformanceCoordinator.PopulationRequested
            || PopulationPerformanceCoordinator.SceneStopped) return;
        if (!PopulationPerformanceCoordinator.IsCapturableCamp(__instance)) return;
#endif
        PopulationPerformanceCoordinator.CaptureProfile(__instance);
    }

    [HarmonyPatch(nameof(BeggarCamp.Awake))]
    [HarmonyPostfix]
    public static void Awake_Postfix(BeggarCamp __instance)
    {
#if ANDROID
        // ANDROID 默认 OFF / 本 scene 终态：纯托管门先于 __instance 的 null/Pointer/native 访问。
        if (!PopulationPerformanceCoordinator.PopulationRequested
            || PopulationPerformanceCoordinator.SceneStopped) return;
#endif
        if (__instance == null) return;
        if (ModConfig.Enabled.Value)
        {
            PopulationPerformanceCoordinator.ConfigureCamp(__instance);
#if !ANDROID
            PatchRoles_Ninja.EnsureBeggarCampHidingSpots(__instance);
#endif
        }
    }

    [HarmonyPatch(nameof(BeggarCamp.OnDestroy))]
    [HarmonyPrefix]
    public static void OnDestroy_Prefix(BeggarCamp __instance)
    {
#if ANDROID
        // ANDROID 默认 OFF / 本 scene 终态：无自有责任直接退出，不读 Pointer。
        if (!PopulationPerformanceCoordinator.PopulationRequested
            || PopulationPerformanceCoordinator.SceneStopped) return;
#endif
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
#if ANDROID
        // ANDROID 默认 OFF：epoch 只在中央模式拥有责任时维护，先纯托管退出。
        if (!PopulationPerformanceCoordinator.PopulationRequested) return;
#endif
        PopulationPerformanceCoordinator.BeginBeggarIncarnation(__instance);
    }

    [HarmonyPatch(nameof(Beggar.OnDisable))]
    [HarmonyPrefix]
    public static void OnDisable_Prefix(Beggar __instance)
    {
#if ANDROID
        if (!PopulationPerformanceCoordinator.PopulationRequested) return;
#endif
        PopulationPerformanceCoordinator.ForgetBeggar(__instance);
    }
}
