using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 神器权杖（HermesStaff）数值 patch：
///   1. 控制数量：Awake 后把 _maximumConvertedTrolls 写为 32（StartAbilityRoutine 以
///      Mathf.Min(命中数, _maximumConvertedTrolls) 硬截断，字段即有效上限）。
///   1b. 扫描范围：Awake postfix 把 _abilityRange 与 _trollScanner.range/rangeBehind
///      扩为序列化原始值的 2 倍，原始值按实例缓存，重复 Awake 幂等不叠加。
///   2. 控制永久：FriendlyTroll.ShouldRevertToTroll() prefix 强制返回 false 并跳过原方法
///      （revert 永不触发）；mod 关闭时返回 true 走原逻辑（可开关）。
///   3. 基础冷却 30 秒 → 30 秒 × 面板倍率（2026-08-24 由固定 11.25 改为倍率制，
///      StaffCooldownMultiplier 默认 0.375 → 11.25 秒，与旧常量行为一致；0.2 最短 = 1/5）；
///      关闭 mod 恢复原版 30 秒。
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

    /// <summary>有效转化上限：StartAbilityRoutine 用 Mathf.Min(命中数, _maximumConvertedTrolls) 硬截断，
    /// 字段值即有效上限，直接写 32。</summary>
    private const int EffectiveMaxConvertedTrolls = 32;

    /// <summary>扫描范围倍率：_abilityRange 与 Scanner.range/rangeBehind 同为原生值 × 2。</summary>
    private const float AbilityRangeMultiplier = 2f;

    /// <summary>记录每个 staff 实例的序列化原始 _abilityRange（Awake 可能重复触发），
    /// 每次都从原始值重算 ×2，保证幂等，不会 4x/8x 叠加。实例销毁后由 GC 回收键值。</summary>
    private static readonly System.Collections.Generic.Dictionary<HermesStaff, float> OriginalAbilityRanges = new();

    private static void ApplyCooldownProfile(HermesStaff staff)
    {
        if (staff == null) return;
        // 倍率制（2026-08-24）：强化 CD = 原版 30s × StaffCooldownMultiplier（面板滑块，
        // 默认 0.375 → 11.25s，等价旧常量 EnhancedCooldownSeconds=11.25）；
        // mod 关闭时走原版 30s 分支保持不变。
        staff._itemCooldown = ModConfig.Enabled.Value
            ? OriginalCooldownSeconds * ModConfig.StaffCooldownMultiplier.Value
            : OriginalCooldownSeconds;
    }

    /// <summary>
    /// 倍率改动回调（ModConfig.Init 接线，InfiniteMoney 同款模式）：
    /// 对在场 HermesStaff 重跑 profile，运行中的权杖即时换算。事件低频，
    /// FindObjectsOfType 全量扫可接受。若 BepInEx 从文件监视线程触发 SettingChanged，
    /// Unity API 会抛异常 —— try/catch 兜住，主线程（面板滑块）路径不受影响；
    /// 且 Awake/CanActivate/TriggerItemAbility 三个读取点每次使用都会重算，
    /// 正确性本就不依赖本回调。
    /// </summary>
    internal static void OnStaffCooldownMultiplierChanged(object sender, EventArgs e)
    {
        try
        {
            var staffs = UnityEngine.Object.FindObjectsOfType<HermesStaff>();
            foreach (var staff in staffs)
            {
                ApplyCooldownProfile(staff);
            }
        }
        catch (Exception ex)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError($"[HermesStaff] staff cooldown reapply failed: {ex}");
        }
    }

    [HarmonyPatch(nameof(HermesStaff.Awake))]
    [HarmonyPostfix]
    public static void HermesStaff_Awake_Postfix(HermesStaff __instance)
    {
        try
        {
            ApplyCooldownProfile(__instance);
            if (!ModConfig.Enabled.Value) return;
            __instance._maximumConvertedTrolls = EffectiveMaxConvertedTrolls;

            // 幂等扩程：原生 Awake 已用原范围构造 _trollScanner，postfix 里同步把字段和
            // scanner 的 range/rangeBehind 一起扩到 原始值 × 2（Scanner 构造时两者同值）。
            // 重复 Awake 时原始 Awake 会用（已扩大的）_abilityRange 重建 scanner，这里再以
            // 保存的原始值重算，字段与 scanner 始终收敛到 原始 × 2，不会叠加。
            if (!OriginalAbilityRanges.TryGetValue(__instance, out float originalRange))
            {
                originalRange = __instance._abilityRange;
                OriginalAbilityRanges[__instance] = originalRange;
            }

            float doubledRange = originalRange * AbilityRangeMultiplier;
            __instance._abilityRange = doubledRange;
            Scanner trollScanner = __instance._trollScanner;
            if (trollScanner != null)
            {
                trollScanner.range = doubledRange;
                trollScanner.rangeBehind = doubledRange;
            }
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
