using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Harmony;
using Coatsink.Common;

namespace MyMod
{
    public static class Patch_Knight
    {
        public static void Register(HarmonyInstance harmony)
        {
            var knightType = typeof(Knight);
            var recruitMethod = knightType.GetMethod("TryRecruitAdditionalFollowers", BindingFlags.Public | BindingFlags.Instance);
            if (recruitMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_Knight).GetMethod("TryRecruitAdditionalFollowers_Prefix"));
                harmony.Patch(recruitMethod, prefix, null);
                Debug.Log("[MyMod] Patched Knight.TryRecruitAdditionalFollowers");
            }
        }

        public static bool TryRecruitAdditionalFollowers_Prefix(Knight __instance, int amount)
        {
            if (!Main.Enabled) return true;

            try
            {
                if (amount < 1) return true;

                // 移除生物群落限制，让 Berserker 在所有世界都能跟随骑士
                List<Berserker> list = new List<Berserker>(SingletonMonoBehaviour<Managers>.Inst.kingdom.Berserkers);
                list.Sort((Berserker a, Berserker b) => a.transform.position.x.CompareTo(b.transform.position.x));
                int num = 0;
                int num2 = 0;
                while (num2 < list.Count && num < amount)
                {
                    if (list[num2].IsAvailableForJob() && list[num2].TryRecruit(__instance))
                    {
                        num++;
                        var field = typeof(Knight).GetField("_additionalFollowers", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (field != null)
                        {
                            var additionalFollowers = field.GetValue(__instance) as List<Berserker>;
                            if (additionalFollowers != null)
                            {
                                additionalFollowers.Add(list[num2]);
                            }
                        }
                    }
                    num2++;
                }

                Debug.Log("[MyMod] TryRecruitAdditionalFollowers: recruited " + num + " Berserkers");
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in Knight TryRecruitAdditionalFollowers patch: " + e.Message);
            }

            return false;
        }
    }
}