using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 怪物数量/时间线倍率（ModConfig.EnemyCountMultiplier / EnemyTimelineSpeed）。
///   - AddEnemies：prefix 缩放 multiplier（每波怪物数量倍率）。
///   - GetEnemies：prefix 缩放 targetDay/multiplierDay/daysOnCurrentIsland（时间线推进倍率）。
///
/// 2.4.0 签名验证（E:/QQ/.../BepInEx/interop/Assembly-CSharp.dll）：
///   - EnemyManager.AddEnemies(EnemyType, AnimationCurve, int targetDay, float multiplier,
///       List&lt;EnemyBlueprint&gt;, ref string log)  存在 ✓ private void
///   - EnemyManager.GetEnemies(Wave, int targetDay, int multiplierDay, int daysOnCurrentIsland,
///       bool logsEnabled = false)              存在 ✓ public List&lt;EnemyBlueprint&gt;
///   结论：轻微漂移——Mono 的 AddEnemies.multiplier 是 ref float，2.4.0 是 float（按值）。
///   HarmonyX prefix 仍可用 `ref float multiplier` 拦截并缩放（prefix 先于原方法执行，
///   对按值值类型参数注入 ref 可改变原方法实际接收到的值，与 Mono 的 SetSpeed_Prefix 同法）。
///   GetEnemies 三 int 参数同样按值，`ref int` 拦截同法。
/// </summary>
[HarmonyPatch(typeof(EnemyManager))]
public static class PatchWorld_EnemyManager
{
    [HarmonyPatch("AddEnemies")]
    [HarmonyPrefix]
    public static bool AddEnemies_Prefix(ref float multiplier)
    {
        if (!ModConfig.Enabled.Value || ModConfig.EnemyCountMultiplier.Value <= 1f) return true;

        try
        {
            multiplier *= ModConfig.EnemyCountMultiplier.Value;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }

        return true;
    }

    [HarmonyPatch("GetEnemies", new[] { typeof(Wave), typeof(int), typeof(int), typeof(int), typeof(bool) })]
    [HarmonyPrefix]
    public static bool GetEnemies_Prefix(ref int targetDay, ref int multiplierDay, ref int daysOnCurrentIsland)
    {
        if (!ModConfig.Enabled.Value) return true;

        try
        {
            if (ModConfig.EnemyTimelineSpeed.Value > 1f)
            {
                targetDay = Mathf.RoundToInt(targetDay * ModConfig.EnemyTimelineSpeed.Value);
                multiplierDay = Mathf.RoundToInt(multiplierDay * ModConfig.EnemyTimelineSpeed.Value);
                daysOnCurrentIsland = Mathf.RoundToInt(daysOnCurrentIsland * ModConfig.EnemyTimelineSpeed.Value);
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }

        return true;
    }
}
