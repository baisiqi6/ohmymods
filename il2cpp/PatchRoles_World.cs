using System;
using UnityEngine;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 希腊世界自动生成草地：World.OnLevelLoaded 之后，若希腊世界(biome=5)还没有草地，
/// 沿世界边界均匀撒 Grass（跳过 NotGrassable 阻挡）。
///
/// 2.4.0 签名验证（interop Assembly-CSharp.dll）：
/// - World.OnLevelLoaded() : void —— 存在（override，无参）
/// - World.HasGrass() : bool —— 存在（替代原反射读取私有 _grass HashSet）
/// - World.worldBounds : Sided&lt;float&gt;（.left/.right）—— 存在
/// - World.AddGrass(Grass)/ExpandGrass() : void —— 存在
/// - World.gameLayer : Transform —— 存在
/// - Holder.grassPrefab : Grass —— 存在
/// - Pool.Spawn&lt;T&gt;(T, Vector3, Quaternion, Transform = null, bool = true) where T : Component —— 存在
/// - Physics2D.OverlapArea(Vector2, Vector2, int layerMask, float minDepth, float maxDepth) ——
///   【差异】2.1.0 的 3 参 OverlapArea(pointA,pointB,layerMask) 已移除，改用 5 参重载
/// - LayerMask.GetMask(params string[]) —— 存在
/// </summary>
[HarmonyPatch(typeof(World))]
public static class World_OnLevelLoaded_Patch
{
    private const int GREECE_BIOME_INDEX = 5;

    [HarmonyPatch(nameof(World.OnLevelLoaded))]
    [HarmonyPostfix]
    public static void OnLevelLoaded_Postfix(World __instance)
    {
        if (!ModConfig.Enabled.Value) return;

        try
        {
            if (BiomeHolder.Inst.BiomeIndex != GREECE_BIOME_INDEX) return;

            if (__instance.HasGrass()) return;

            Grass grassPrefab = Managers.Inst.holder.grassPrefab;
            if (grassPrefab == null) return;

            float leftBound = __instance.worldBounds.left;
            float rightBound = __instance.worldBounds.right;

            int notGrassableMask = LayerMask.GetMask("NotGrassable");

            int grassCount = 0;
            float spacing = 15f;

            for (float x = leftBound + 10f; x < rightBound - 10f; x += spacing)
            {
                Vector2 checkMin = new Vector2(x - 2f, 0f);
                Vector2 checkMax = new Vector2(x + 2f, 1f);

                Collider2D blocker = Physics2D.OverlapArea(checkMin, checkMax, notGrassableMask,
                    float.NegativeInfinity, float.PositiveInfinity);
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

            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Roles] Spawned " + grassCount + " initial grass patches in Greece biome");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}
