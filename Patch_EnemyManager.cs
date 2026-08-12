using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Harmony;

namespace MyMod
{
    public static class Patch_EnemyManager
    {
        public static void Register(HarmonyInstance harmony)
        {
            var enemyManagerType = typeof(EnemyManager);

            var addEnemiesMethod = enemyManagerType.GetMethod("AddEnemies", BindingFlags.NonPublic | BindingFlags.Instance);
            if (addEnemiesMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_EnemyManager).GetMethod("AddEnemies_Prefix"));
                harmony.Patch(addEnemiesMethod, prefix, null);
                Debug.Log("[MyMod] Patched EnemyManager.AddEnemies");
            }

            var getEnemiesMethod = enemyManagerType.GetMethod("GetEnemies", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(Wave), typeof(int), typeof(int), typeof(int), typeof(bool) }, null);
            if (getEnemiesMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_EnemyManager).GetMethod("GetEnemies_Prefix"));
                harmony.Patch(getEnemiesMethod, prefix, null);
                Debug.Log("[MyMod] Patched EnemyManager.GetEnemies");
            }
        }

        public static bool AddEnemies_Prefix(EnemyType type, AnimationCurve curve, int targetDay, ref float multiplier, List<EnemyBlueprint> list, ref string log)
        {
            if (!Main.Enabled || Main.enemyCountMultiplier <= 1f) return true;

            try
            {
                multiplier *= Main.enemyCountMultiplier;
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in EnemyManager AddEnemies patch: " + e.Message);
            }

            return true;
        }

        public static bool GetEnemies_Prefix(Wave wave, ref int targetDay, ref int multiplierDay, ref int daysOnCurrentIsland, bool logsEnabled)
        {
            if (!Main.Enabled) return true;

            try
            {
                if (Main.enemyTimelineSpeed > 1f)
                {
                    targetDay = Mathf.RoundToInt(targetDay * Main.enemyTimelineSpeed);
                    multiplierDay = Mathf.RoundToInt(multiplierDay * Main.enemyTimelineSpeed);
                    daysOnCurrentIsland = Mathf.RoundToInt(daysOnCurrentIsland * Main.enemyTimelineSpeed);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in EnemyManager GetEnemies prefix: " + e.Message);
            }

            return true;
        }
    }
}