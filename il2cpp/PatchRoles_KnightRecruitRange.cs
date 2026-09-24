using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 骑士小队补员距离（2026-09-24 玩家反馈"14 骑士只有 42 随从，对不上 1 骑士 4 随从"）：
/// 原生 Knight.Update 每 10s 以 maxRange=10 调 FetchArchersForJob（Knight.cs:408）——自由弓手
/// 白天散在墙外打猎（SquadRefill 实测 nearest=50-80 格），10 格窗口长期够不着，编制停在
/// 1-3/4。原生候选按就近排序（Kingdom.cs FetchArchersForJob 的 Sort），放大窗口只会
/// 优先取近者，行为安全；minFreeArchers 预留、占用/守卫/弩手过滤全部原样。
/// 2.1.0 全源码唯一小半径调用方即骑士补员；守位等走 MaxValue 天然不受影响。挂主开关。
/// </summary>
internal static class PatchRoles_KnightRecruitRange
{
    private const float KnightSquadRecruitRange = 120f;

    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.FetchArchersForJob))]
    [HarmonyPriority(Priority.High)] // 先于 SquadRefillDiag 前缀改参，诊断日志记到真实生效值
    internal static class Kingdom_FetchArchersForJob_RecruitRange_Patch
    {
        private static void Prefix(GameObject jobObject, ref float maxRange)
        {
            try
            {
                if (ModConfig.Enabled?.Value != true) return;
                if (maxRange >= KnightSquadRecruitRange) return;
                Knight knight = jobObject != null ? jobObject.GetComponent<Knight>() : null;
                if (knight == null) return;
                maxRange = KnightSquadRecruitRange;
            }
            catch
            {
                // 门失败保持原值（原生 10 格行为）。
            }
        }
    }
}
