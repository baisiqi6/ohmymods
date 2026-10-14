#if ANDROID
using System;

namespace KingdomEnhancedMod;

/// <summary>
/// Android candidate-count field for DamageAffectedEnemies is compared directly.
/// Borrow 20 for this call only; the separate damage loop is not a total-hit cap.
/// </summary>
public static class PatchDivine_Artemis
{
    private const float AppliedCandidateLimit = 20f;

    internal sealed class Borrow
    {
        internal ArtemisArrow Arrow;
        internal float Original;
        internal bool Written;

        internal void End()
        {
            if (!Written) return;
            Written = false;
            try
            {
                if (Arrow._maxHitsPerArrow == AppliedCandidateLimit)
                    Arrow._maxHitsPerArrow = Original;
            }
            catch (Exception ex) { AndroidDivineParametersScope.LogError(ex); }
        }
    }

    internal static void DamageAffectedEnemies_Prefix(ArtemisArrow __instance, out Borrow __state)
    {
        __state = null;
        if (!AndroidDivineParametersScope.IsEnabled()) return;
        try
        {
            if (!AndroidDivineParametersScope.IsCurrentAuthority(__instance)) return;
            float original = __instance._maxHitsPerArrow;
            if (original == AppliedCandidateLimit) return;
            __state = new Borrow { Arrow = __instance, Original = original, Written = true };
            __instance._maxHitsPerArrow = AppliedCandidateLimit;
        }
        catch (Exception ex) { AndroidDivineParametersScope.LogError(ex); }
    }

    internal static Exception Finalizer(Exception __exception, Borrow __state)
    {
        __state?.End();
        return __exception;
    }
}
#else
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
#endif
