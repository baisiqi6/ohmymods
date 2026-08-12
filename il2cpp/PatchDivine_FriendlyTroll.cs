using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 友好巨魔不攻击飞行怪（CrownStealer）。
///
/// Mono 版 Patch_FriendlyTroll 用 transpiler 改 MoveToTargetRoutine 的目标筛选，但因 Harmony 1.2 的
/// transpiler 崩溃被整体禁用（Patch_FriendlyTroll.cs 内保留为 disabled 备查）。本迁移用 HarmonyX
/// prefix 重新实现：挂在目标校验 FriendlyTroll.IsTargetValid 上，目标为 CrownStealer（飞行怪）时
/// 视为无效目标 → 巨魔不追不攻。
///
/// 已知局限（源自 IsTargetValid 只在"校验"处生效，不参与 MoveToTargetRoutine 内部的选目标循环）：
///   若 CrownStealer 恰为距离最近的敌人，巨魔会反复"选它→校验失败→游荡 2 秒"，期间不会转而攻击
///   次近的地面敌人。这是"不追飞行怪"目标的可接受近似；彻底在选目标循环内排除需 patch 生成的状态机
///   MoveNext（脆弱，暂不做）。详见 notes-world.md 待 Operator 决策清单。
///
/// 2.4.0 签名验证（E:/QQ/.../BepInEx/interop/Assembly-CSharp.dll）：
///   - FriendlyTroll.IsTargetValid(Damageable target, Transform thisTransform, float maxAttackDistance)
///       存在 ✓ private static bool
///   - FriendlyTroll.MoveToTargetRoutine() 存在 ✓ public IEnumerator
///   - CrownStealer 存在 ✓ public class CrownStealer : Enemy（飞行怪）
///   结论：方法均在。IsTargetValid 为 private static，用字符串名挂载；prefix 仅需 target + ref __result。
/// </summary>
[HarmonyPatch(typeof(FriendlyTroll), "IsTargetValid")]
public static class PatchDivine_FriendlyTroll
{
    [HarmonyPrefix]
    public static bool IsTargetValid_Prefix(Damageable target, ref bool __result)
    {
        if (!ModConfig.Enabled.Value) return true;
        if (target == null) return true;

        try
        {
            if (target.GetComponent<CrownStealer>() != null)
            {
                __result = false; // 目标为飞行怪 → 无效，跳过原校验
                return false;
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }

        return true;
    }
}
