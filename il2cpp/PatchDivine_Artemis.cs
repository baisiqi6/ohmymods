using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 希腊神器弓箭（ArtemisArrow）：单发箭伤害次数 = 20 次。
///
/// 结算点 DamageAffectedEnemies 内上限表达式为 `_maxHitsPerArrow + 20f`（见 Mono 版 Patch_Artemis 注释）。
/// 要得到"恰好 20 次"，须设 _maxHitsPerArrow = 0f（0 + 20 = 20；设 20 会变 40）。每次结算前写 0f，
/// 对任何生成/池化路径都成立，无需 OnEnable 时序。
///
/// 2.4.0 签名验证（E:/QQ/.../BepInEx/interop/Assembly-CSharp.dll）：
///   - ArtemisArrow.DamageAffectedEnemies(GameObject hitEnemy = null) 存在 ✓ private void
///   - ArtemisArrow._maxHitsPerArrow                                 存在 ✓ public float
///   结论：无漂移。_maxHitsPerArrow 由 Mono 的私有字段（反射）变为 public 字段，可直接赋值。
/// </summary>
[HarmonyPatch(typeof(ArtemisArrow), "DamageAffectedEnemies")]
public static class PatchDivine_Artemis
{
    [HarmonyPrefix]
    public static void DamageAffectedEnemies_Prefix(ArtemisArrow __instance)
    {
        if (!ModConfig.Enabled.Value) return;

        try
        {
            __instance._maxHitsPerArrow = 0f;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}
