using System;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 临时测试参数：每个乞丐帐篷把可配置的生成等待段改为 1 秒，且每个帐篷
/// 最多保留 5 个乞丐。这是实例字段设置，不是全场共享上限。
///
/// 2.4.0 签名验证（interop Assembly-CSharp.dll）：
/// - BeggarCamp.Awake() : void —— 存在（private，interop 公开）
/// - BeggarCamp.spawnInterval : float（公开属性）—— 存在（免反射 SetValue）
/// - BeggarCamp.maxBeggars : int（公开属性）—— 存在（免反射 SetValue）
/// - 原生协程每轮先用约 5 秒重算附近乞丐，再等待 spawnInterval；所以空营地
///   实际观察约 6 秒生成一个，而非严格每秒一个。教程门控仍由原逻辑保留。
/// </summary>
[HarmonyPatch(typeof(BeggarCamp))]
public static class BeggarCamp_Awake_Patch
{
    private const float TargetSpawnInterval = 1f;
    private const int TargetMaxBeggarsPerCamp = 5;

    [HarmonyPatch(nameof(BeggarCamp.Awake))]
    [HarmonyPostfix]
    public static void Awake_Postfix(BeggarCamp __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        if (__instance == null) return;
        __instance.spawnInterval = TargetSpawnInterval;
        __instance.maxBeggars = TargetMaxBeggarsPerCamp;
        PatchRoles_Ninja.EnsureBeggarCampHidingSpots(__instance);
    }
}
