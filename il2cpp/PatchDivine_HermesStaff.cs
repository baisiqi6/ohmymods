using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 神器权杖（HermesStaff）数值 patch：
///   1. 控制数量：Awake 后把 _maximumConvertedTrolls 8 → 16（有效上限 16 + 8 = 24，保证至少控 16 个；
///      见 Mono 版 Patch_HermesStaff 对 `+8` 余量的分析）。
///   2. 控制永久：FriendlyTroll.ShouldRevertToTroll() prefix 强制返回 false 并跳过原方法
///      （revert 永不触发）；mod 关闭时返回 true 走原逻辑（可开关）。
///   3. 2.4.0 基础冷却 30 秒 → 11.25 秒（用户拍板：现行 22.5 减半）；关闭 mod 恢复 30 秒。
///
/// 2.4.0 签名验证（E:/QQ/.../BepInEx/interop/Assembly-CSharp.dll）：
///   - HermesStaff.Awake()                存在 ✓ public override void
///   - HermesStaff._maximumConvertedTrolls 存在 ✓ public int
///   - FriendlyTroll.ShouldRevertToTroll() 存在 ✓ public bool
///   结论：无漂移。_maximumConvertedTrolls 由 Mono 的私有字段（反射）变为 public 字段，可直接赋值。
///   ShouldRevertToTroll 为 public，nameof 可用（Mono 需 BindingFlags.NonPublic 反射）。
/// </summary>
[HarmonyPatch(typeof(HermesStaff))]
public static class PatchDivine_HermesStaff
{
    private const float OriginalCooldownSeconds = 30f;
    private const float EnhancedCooldownSeconds = 11.25f;

    private static void ApplyCooldownProfile(HermesStaff staff)
    {
        if (staff == null) return;
        staff._itemCooldown = ModConfig.Enabled.Value
            ? EnhancedCooldownSeconds
            : OriginalCooldownSeconds;
    }

    [HarmonyPatch(nameof(HermesStaff.Awake))]
    [HarmonyPostfix]
    public static void HermesStaff_Awake_Postfix(HermesStaff __instance)
    {
        try
        {
            ApplyCooldownProfile(__instance);
            if (!ModConfig.Enabled.Value) return;
            __instance._maximumConvertedTrolls = 16;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    [HarmonyPatch(nameof(HermesStaff.CanActivate))]
    [HarmonyPrefix]
    public static void HermesStaff_CanActivate_Prefix(HermesStaff __instance)
    {
        try
        {
            ApplyCooldownProfile(__instance);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    [HarmonyPatch(nameof(HermesStaff.TriggerItemAbility))]
    [HarmonyPrefix]
    public static void HermesStaff_TriggerItemAbility_Prefix(HermesStaff __instance)
    {
        try
        {
            ApplyCooldownProfile(__instance);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    [HarmonyPatch(typeof(FriendlyTroll), nameof(FriendlyTroll.ShouldRevertToTroll))]
    [HarmonyPrefix]
    public static bool FriendlyTroll_ShouldRevertToTroll_Prefix(ref bool __result)
    {
        if (!ModConfig.Enabled.Value) return true; // 未启用：走原方法

        __result = false;                           // 永不 revert → 控制时间永久
        return false;                               // 跳过原方法
    }
}
