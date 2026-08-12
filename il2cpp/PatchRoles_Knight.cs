using System;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 狂战士跨世界跟随骑士：移除生物群落限制，让 Berserker 在所有世界都能被骑士
/// TryRecruitAdditionalFollowers 招募。
///
/// 2.4.0 签名验证（interop Assembly-CSharp.dll）：
/// - Knight.TryRecruitAdditionalFollowers(int amount) : void —— 存在（2.1.0 同参）
/// - Knight._additionalFollowers : List&lt;Berserker&gt;（Il2CppSystem，公开属性）—— 存在（免反射）
/// - Berserker.IsAvailableForJob() : bool —— 存在
/// - Berserker.TryRecruit(Knight) : bool —— 存在
/// - Kingdom.Berserkers : List&lt;Berserker&gt;（Il2CppSystem）—— 存在
/// - 集合类型为 Il2CppSystem.*；List&lt;T&gt;.Sort 需 Il2CppSystem.Comparison 委托，
///   本组改用托管 System.Collections.Generic.List 排序，避开委托转换。
/// </summary>
[HarmonyPatch(typeof(Knight))]
public static class Knight_TryRecruitAdditionalFollowers_Patch
{
    [HarmonyPatch(nameof(Knight.TryRecruitAdditionalFollowers))]
    [HarmonyPrefix]
    public static bool TryRecruitAdditionalFollowers_Prefix(Knight __instance, int amount)
    {
        if (!ModConfig.Enabled.Value) return true;

        try
        {
            if (amount < 1) return true;

            var berserkers = Managers.Inst.kingdom.Berserkers;
            if (berserkers == null) return false;

            // 收集到托管 List 以便按 x 排序（避开 Il2CppSystem.Comparison 委托转换）
            var list = new System.Collections.Generic.List<Berserker>();
            for (int i = 0; i < berserkers.Count; i++)
                list.Add(berserkers[i]);
            list.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));

            int num = 0;
            int num2 = 0;
            while (num2 < list.Count && num < amount)
            {
                if (list[num2].IsAvailableForJob() && list[num2].TryRecruit(__instance))
                {
                    num++;
                    __instance._additionalFollowers.Add(list[num2]);
                }
                num2++;
            }

            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Roles] TryRecruitAdditionalFollowers: recruited " + num + " Berserkers");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }

        return false;
    }
}
