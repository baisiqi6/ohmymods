using System;
using System.Reflection;
using UnityEngine;
using Harmony;

namespace MyMod
{
    public static class Patch_Construction
    {
        public static void Register(HarmonyInstance harmony)
        {
            var buildType = typeof(ConstructionBuildingComponent);
            var initBuildMethod = buildType.GetMethod("InitializeBuild", BindingFlags.Public | BindingFlags.Instance);
            if (initBuildMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_Construction).GetMethod("Prefix"));
                harmony.Patch(initBuildMethod, prefix, null);
                Debug.Log("[MyMod] Patched ConstructionBuildingComponent.InitializeBuild");
            }
            else
            {
                Debug.LogError("[MyMod] Could not find InitializeBuild method!");
            }
        }

        public static void Prefix(ConstructionBuildingComponent __instance)
        {
            if (!Main.Enabled || !Main.fastBuild) return;

            try
            {
                var autoBuildRateField = typeof(ConstructionBuildingComponent).GetField("_autoBuildRate", BindingFlags.NonPublic | BindingFlags.Instance);
                if (autoBuildRateField != null)
                {
                    autoBuildRateField.SetValue(__instance, 50f);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in Construction patch: " + e.ToString());
            }
        }
    }
}
