using System;
using System.Reflection;
using UnityEngine;
using Harmony;
using Coatsink.Common;

namespace MyMod
{
    public static class Patch_World
    {
        private const int GREECE_BIOME_INDEX = 5;

        public static void Register(HarmonyInstance harmony)
        {
            var worldType = typeof(World);
            var method = worldType.GetMethod("OnLevelLoaded", BindingFlags.Public | BindingFlags.Instance);
            if (method != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_World).GetMethod("OnLevelLoaded_Postfix"));
                harmony.Patch(method, null, postfix);
                Debug.Log("[MyMod] Patched World.OnLevelLoaded");
            }
        }

        public static void OnLevelLoaded_Postfix(World __instance)
        {
            if (!Main.Enabled) return;

            try
            {
                if (BiomeHolder.Inst.BiomeIndex != GREECE_BIOME_INDEX) return;

                var grassField = typeof(World).GetField("_grass", BindingFlags.NonPublic | BindingFlags.Instance);
                if (grassField != null)
                {
                    var grassList = grassField.GetValue(__instance) as System.Collections.Generic.HashSet<Grass>;
                    if (grassList != null && grassList.Count > 0) return;
                }

                Grass grassPrefab = SingletonMonoBehaviour<Managers>.Inst.holder.grassPrefab;
                if (grassPrefab == null) return;

                float leftBound = __instance.worldBounds.left;
                float rightBound = __instance.worldBounds.right;

                int grassCount = 0;
                float spacing = 15f;

                for (float x = leftBound + 10f; x < rightBound - 10f; x += spacing)
                {
                    Vector2 checkMin = new Vector2(x - 2f, 0f);
                    Vector2 checkMax = new Vector2(x + 2f, 1f);

                    Collider2D blocker = Physics2D.OverlapArea(checkMin, checkMax, LayerMask.GetMask(new string[] { "NotGrassable" }));
                    if (blocker != null) continue;

                    Vector3 position = new Vector3(x, 1f, 0f);
                    Grass grass = Pool.Spawn<Grass>(grassPrefab, position, Quaternion.identity, __instance.gameLayer, true);
                    if (grass != null)
                    {
                        __instance.AddGrass(grass);
                        grassCount++;
                    }
                }

                if (grassCount > 0) __instance.ExpandGrass();

                Debug.Log("[MyMod] Spawned " + grassCount + " initial grass patches in Greece biome");
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in World.OnLevelLoaded patch: " + e.Message);
            }
        }
    }
}