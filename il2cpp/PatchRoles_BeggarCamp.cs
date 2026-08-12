using System;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 乞丐帐篷生成间隔 90 秒。游戏 SlowUpdate 公式为 (spawnInterval - 119f) 秒，
/// 默认 spawnInterval=120 → 实际等待 1 秒。目标 90 秒需设 spawnInterval = 90 + 119 = 209f
/// （设置型，非乘法叠加）。
///
/// 2.4.0 签名验证（interop Assembly-CSharp.dll）：
/// - BeggarCamp.Awake() : void —— 存在（private，interop 公开）
/// - BeggarCamp.spawnInterval : float（公开属性）—— 存在（免反射 SetValue）
/// - 协程 SlowUpdate 语义未变（仍读 spawnInterval - 119f），字段设置方案最稳。
/// </summary>
[HarmonyPatch(typeof(BeggarCamp))]
public static class BeggarCamp_Awake_Patch
{
    // 目标：生成间隔 90 秒。游戏公式 (spawnInterval - 119f)，故设 90 + 119 = 209f
    private const float TargetSpawnInterval = 209f;

    [HarmonyPatch(nameof(BeggarCamp.Awake))]
    [HarmonyPostfix]
    public static void Awake_Postfix(BeggarCamp __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        if (__instance == null) return;
        __instance.spawnInterval = TargetSpawnInterval;
    }
}
