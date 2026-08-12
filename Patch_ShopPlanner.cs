using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Harmony;

namespace MyMod
{
    /// <summary>
    /// ShopPlanner patch：用 Prefix 完全接管 InitializeShopTypePrefabPairs。
    ///
    /// 原版用 Dictionary.Add 不检查重复，shopPrefabs(Inspector) 和 curBiomeAssets.uniqueShopPrefabs
    /// 里都有 ChangeItem 时原版直接崩 ArgumentException，Postfix 没机会执行。
    /// 改用 Prefix 安全写入 + 跨生物群系注册，return false 跳过原版。
    /// </summary>
    public static class Patch_ShopPlanner
    {
        public static void Register(HarmonyInstance harmony)
        {
            var shopPlannerType = typeof(ShopPlanner);
            var initMethod = shopPlannerType.GetMethod("InitializeShopTypePrefabPairs", BindingFlags.NonPublic | BindingFlags.Instance);
            if (initMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_ShopPlanner).GetMethod("Prefix"));
                harmony.Patch(initMethod, prefix, null);
                Debug.Log("[MyMod] Patched ShopPlanner.InitializeShopTypePrefabPairs (Prefix)");
            }
            else
            {
                Debug.LogError("[MyMod] Could not find InitializeShopTypePrefabPairs method!");
            }
        }

        /// <summary>
        /// 完全替换原版初始化：安全写入 + 跨生物群系注册。
        /// </summary>
        public static bool Prefix(ShopPlanner __instance)
        {
            if (!Main.Enabled) return true; // 未启用时走原版（哪怕会崩，至少不引入新行为）

            try
            {
                // 反射获取私有字段
                var pairsField = typeof(ShopPlanner).GetField("shopTypePrefabPairs",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var shopPrefabsField = typeof(ShopPlanner).GetField("shopPrefabs",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (pairsField == null || shopPrefabsField == null)
                {
                    Debug.LogError("[MyMod] Required ShopPlanner fields not found, falling back to original");
                    return true;
                }

                var pairs = pairsField.GetValue(__instance) as Dictionary<PayableShop.ShopType, GameObject>;
                var shopPrefabs = shopPrefabsField.GetValue(__instance) as List<ShopTag>;
                if (pairs == null)
                {
                    Debug.LogError("[MyMod] shopTypePrefabPairs is null, falling back to original");
                    return true;
                }

                pairs.Clear();

                // 第一部分：原版逻辑——合并 shopPrefabs + curBiomeAssets.uniqueShopPrefabs
                int addedCount = 0;
                if (shopPrefabs != null)
                {
                    foreach (var shopTag in shopPrefabs)
                        addedCount += SafeAdd(pairs, shopTag, -1);
                }
                if (BiomeHolder.Inst != null && BiomeHolder.Inst.curBiomeAssets != null)
                {
                    var curShops = BiomeHolder.Inst.curBiomeAssets.uniqueShopPrefabs;
                    if (curShops != null)
                    {
                        foreach (var shopTag in curShops)
                            addedCount += SafeAdd(pairs, shopTag, BiomeHolder.Inst.BiomeIndex);
                    }
                }
                Debug.Log("[MyMod] Base shops loaded: " + addedCount + " entries");

                // 第二部分：跨生物群系——注册所有世界的商店 prefab
                var biomePathStringsField = typeof(BiomeHolder).GetField("biomePathStrings",
                    BindingFlags.Public | BindingFlags.Instance);
                if (biomePathStringsField != null)
                {
                    var biomePathStrings = biomePathStringsField.GetValue(BiomeHolder.Inst) as string[];
                    if (biomePathStrings != null)
                    {
                        int crossCount = 0;
                        for (int i = 0; i < biomePathStrings.Length; i++)
                        {
                            var path = biomePathStrings[i];
                            if (string.IsNullOrEmpty(path)) continue;

                            var biomeData = Resources.Load<BiomeData>(path);
                            if (biomeData == null || biomeData.biomeSpecificAssets == null) continue;

                            var uniqueShops = biomeData.biomeSpecificAssets.uniqueShopPrefabs;
                            if (uniqueShops == null) continue;

                            foreach (var shopTag in uniqueShops)
                            {
                                if (shopTag == null) continue;
                                crossCount += SafeAdd(pairs, shopTag, i);
                            }
                        }
                        Debug.Log("[MyMod] Cross-biome shops added: " + crossCount + " entries");
                    }
                }

                // 第三部分：希腊世界(biome=5)狂战士商店原生刷新——占用 ShieldShop 槽位(12/13)
                // 希腊城堡 optionalShopType=Pike，原版不会入队 ShieldShop（那是北境 biome=3 的），
                // 所以 12/13 槽位在希腊空闲。9/10（Pike）保留给原生长矛商店。
                // 配合 Patch_SidedShop 的 Awake 改写（Berserker 实例以 ShieldShop 身份自注册），
                // 全链路原生自洽：AddShop/ValidateShops/ShuffleEdge/存档/联机。
                if (BiomeHolder.Inst != null && BiomeHolder.Inst.BiomeIndex == 5)
                {
                    // 注意：Resources.Load 按名字找不到子目录下的资源，必须用 LoadAll 扫
                    var berserkerShop = FindShopTagPrefab("ShopBerserker_norselands");
                    if (berserkerShop != null)
                    {
                        pairs[PayableShop.ShopType.ShieldShopLeft] = berserkerShop.gameObject;
                        pairs[PayableShop.ShopType.ShieldShopRight] = berserkerShop.gameObject;
                        Debug.Log("[MyMod] Greece: ShieldShop slot -> ShopBerserker_norselands (native refresh, Pike kept)");
                    }
                    else
                    {
                        Debug.LogError("[MyMod] ShopBerserker_norselands not found in Resources!");
                    }
                }

                Debug.Log("[MyMod] Shop loading complete, total " + pairs.Count + " entries");
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in ShopPlanner Prefix: " + e.ToString());
                return true; // 出错时回退到原版
            }

            return false; // 跳过原版
        }

        private static ShopTag[] _allShopTagsCache;

        /// <summary>
        /// 用 LoadAll 查找 ShopTag prefab（Resources.Load 按名字找不到子目录下的资源）。
        /// </summary>
        private static ShopTag FindShopTagPrefab(string name)
        {
            if (_allShopTagsCache == null)
                _allShopTagsCache = Resources.LoadAll<ShopTag>("");
            foreach (var t in _allShopTagsCache)
            {
                if (t != null && t.gameObject.name == name) return t;
            }
            return null;
        }

        /// <summary>
        /// 安全写入：用 [] 赋值而非 Add，避免重复 key 崩溃。
        /// </summary>
        private static int SafeAdd(Dictionary<PayableShop.ShopType, GameObject> pairs, ShopTag shopTag, int biomeIdx)
        {
            if (shopTag == null) return 0;
            int added = 0;

            PayableSidedShop sidedShop;
            PayableWorkshop workshop;

            if (shopTag.TryGetComponent<PayableSidedShop>(out sidedShop))
            {
                var leftType = PayableSidedShop.GetSidedShopType(sidedShop.shopType, Side.Left);
                var rightType = PayableSidedShop.GetSidedShopType(sidedShop.shopType, Side.Right);
                if (!pairs.ContainsKey(leftType)) { pairs[leftType] = shopTag.gameObject; added++; }
                if (!pairs.ContainsKey(rightType)) { pairs[rightType] = shopTag.gameObject; added++; }
            }
            else if (shopTag.TryGetComponent<PayableWorkshop>(out workshop))
            {
                if (!pairs.ContainsKey(PayableShop.ShopType.WorkshopLeft)) { pairs[PayableShop.ShopType.WorkshopLeft] = shopTag.gameObject; added++; }
                if (!pairs.ContainsKey(PayableShop.ShopType.WorkshopRight)) { pairs[PayableShop.ShopType.WorkshopRight] = shopTag.gameObject; added++; }
            }
            else
            {
                // 普通商店：安全写入（重复 key 时跳过，避免原版 Add 崩溃）
                if (!pairs.ContainsKey(shopTag.type)) { pairs[shopTag.type] = shopTag.gameObject; added++; }
            }
            return added;
        }
    }
}
