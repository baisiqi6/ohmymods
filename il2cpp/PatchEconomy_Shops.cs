using System;
using UnityEngine;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 跨世界商店注册 + 狂战士商店身份改写。
/// 迁移自 Mono Patch_ShopPlanner.cs + Patch_SidedShop.cs（UMM + Harmony 1.2）。
///
/// 2.4.0 签名验证结果（get_type_members.py 核对 interop Assembly-CSharp.dll）：
///   - ShopPlanner.InitializeShopTypePrefabPairs(): private void —— 存在，签名一致。
///   - ShopPlanner.shopTypePrefabPairs: Dictionary&lt;PayableShop.ShopType, GameObject&gt; —— 存在。
///   - ShopPlanner.shopPrefabs: List&lt;ShopTag&gt; —— 存在（Il2CppSystem 集合，非标准 .NET）。
///   - PayableSidedShop.Awake(): protected override void —— 存在（interop 暴露为 public）。
///   - PayableSidedShop.shopType: UnsidedShopType 字段 —— 存在。
///   - PayableSidedShop.GetSidedShopType(UnsidedShopType, Side): static ShopType —— 存在，签名一致。
///   - PayableShop.ShopType 枚举 —— 存在，但新增 Pike_OLDHANDLE 占位（枚举值重排，按名引用不受影响）。
///   - PayableShop.UnsidedShopType 枚举：{ Pike, Workshop, ShieldShop } —— 存在。
///   - PayableShop.itemPrefab: Droppable —— 存在（Droppable : MonoBehaviour，CompareTag 可用）。
///   - PayableWorkshop : Payable —— 存在。
///   - ShopTag.type: PayableShop.ShopType 字段 —— 存在。
///   - BiomeHolder.Inst / BiomeIndex / curBiomeAssets / biomePathStrings —— 全部存在。
///   - BiomeHolder.GreeceBiomeIndex: static int —— 存在（新增，替代硬编码 biome=5）。
///   - BiomeData.biomeSpecificAssets: BiomeSpecificAssets —— 存在；uniqueShopPrefabs: List&lt;ShopTag&gt; —— 存在。
///   - Side 枚举：{ Left = -1, Right = 1 } —— 一致。
///
/// 迁移说明：
///   - 字段访问由 Mono 反射改为 interop public 属性直接访问。
///   - 希腊世界判定由硬编码 BiomeIndex==5 改为 BiomeHolder.GreeceBiomeIndex（版本间 biome 编号可能漂移）。
///   - Resources.LoadAll&lt;ShopTag&gt; 返回 Il2CppArrayBase&lt;ShopTag&gt;（foreach 遍历）。
/// </summary>
public static class PatchEconomy_Shops
{
    // === ShopPlanner：完全接管 InitializeShopTypePrefabPairs ===

    /// <summary>
    /// 完全替换原版初始化：安全写入 + 跨生物群系注册。return false 跳过原版。
    /// </summary>
    [HarmonyPatch(typeof(ShopPlanner), nameof(ShopPlanner.InitializeShopTypePrefabPairs))]
    [HarmonyPrefix]
    public static bool ShopPlanner_Initialize_Prefix(ShopPlanner __instance)
    {
        if (!ModConfig.Enabled.Value) return true; // 未启用时走原版

        try
        {
            var pairs = __instance.shopTypePrefabPairs;
            var shopPrefabs = __instance.shopPrefabs;
            if (pairs == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Economy] shopTypePrefabPairs is null, falling back to original");
                return true;
            }

            pairs.Clear();
            var biomeHolder = BiomeHolder.Inst;
            int biomeIdx = (biomeHolder != null) ? biomeHolder.BiomeIndex : -1;

            // 第一部分：原版逻辑——合并 shopPrefabs + curBiomeAssets.uniqueShopPrefabs
            int addedCount = 0;
            if (shopPrefabs != null)
            {
                foreach (var shopTag in shopPrefabs)
                    addedCount += SafeAdd(pairs, shopTag);
            }
            if (biomeHolder != null && biomeHolder.curBiomeAssets != null)
            {
                var curShops = biomeHolder.curBiomeAssets.uniqueShopPrefabs;
                if (curShops != null)
                {
                    foreach (var shopTag in curShops)
                        addedCount += SafeAdd(pairs, shopTag);
                }
            }
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Economy] Base shops loaded: " + addedCount + " entries");

            // 第二部分：跨生物群系——注册所有世界的商店 prefab
            if (biomeHolder != null)
            {
                var biomePathStrings = biomeHolder.biomePathStrings;
                if (biomePathStrings != null)
                {
                    int crossCount = 0;
                    for (int i = 0; i < biomePathStrings.Length; i++)
                    {
                        string path = biomePathStrings[i];
                        if (string.IsNullOrEmpty(path)) continue;

                        var biomeData = Resources.Load<BiomeData>(path);
                        if (biomeData == null || biomeData.biomeSpecificAssets == null) continue;

                        var uniqueShops = biomeData.biomeSpecificAssets.uniqueShopPrefabs;
                        if (uniqueShops == null) continue;

                        foreach (var shopTag in uniqueShops)
                        {
                            if (shopTag == null) continue;
                            crossCount += SafeAdd(pairs, shopTag);
                        }
                    }
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Economy] Cross-biome shops added: " + crossCount + " entries");
                }
            }

            // 第三部分：希腊世界狂战士商店原生刷新——占用 ShieldShop 槽位(12/13)
            // 配合 SidedShop Awake 改写（Berserker 实例以 ShieldShop 身份自注册）。
            if (biomeHolder != null && biomeIdx == BiomeHolder.GreeceBiomeIndex)
            {
                var berserkerShop = FindShopTagPrefab("ShopBerserker_norselands");
                if (berserkerShop != null)
                {
                    pairs[PayableShop.ShopType.ShieldShopLeft] = berserkerShop.gameObject;
                    pairs[PayableShop.ShopType.ShieldShopRight] = berserkerShop.gameObject;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        "[Economy] Greece: ShieldShop slot -> ShopBerserker_norselands (native refresh, Pike kept)");
                }
                else
                {
                    KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                        "[Economy] ShopBerserker_norselands not found in Resources!");
                }
            }

            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Economy] Shop loading complete, total " + pairs.Count + " entries");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
            return true; // 出错时回退到原版
        }

        return false; // 跳过原版
    }

    /// <summary>用 LoadAll 查找 ShopTag prefab（Resources.Load 按名字找不到子目录下的资源）。</summary>
    private static ShopTag FindShopTagPrefab(string name)
    {
        var all = Resources.LoadAll<ShopTag>("");
        foreach (var t in all)
        {
            if (t != null && t.gameObject.name == name) return t;
        }
        return null;
    }

    /// <summary>
    /// 安全写入：用 [] 赋值而非 Add，避免重复 key 崩溃。
    /// pairs 为 Il2CppSystem.Collections.Generic.Dictionary（interop 集合类型）。
    /// </summary>
    private static int SafeAdd(
        Il2CppSystem.Collections.Generic.Dictionary<PayableShop.ShopType, GameObject> pairs,
        ShopTag shopTag)
    {
        if (shopTag == null) return 0;
        int added = 0;

        PayableSidedShop sidedShop = shopTag.GetComponent<PayableSidedShop>();
        if (sidedShop != null)
        {
            var leftType = PayableSidedShop.GetSidedShopType(sidedShop.shopType, Side.Left);
            var rightType = PayableSidedShop.GetSidedShopType(sidedShop.shopType, Side.Right);
            if (!pairs.ContainsKey(leftType)) { pairs[leftType] = shopTag.gameObject; added++; }
            if (!pairs.ContainsKey(rightType)) { pairs[rightType] = shopTag.gameObject; added++; }
        }
        else
        {
            PayableWorkshop workshop = shopTag.GetComponent<PayableWorkshop>();
            if (workshop != null)
            {
                if (!pairs.ContainsKey(PayableShop.ShopType.WorkshopLeft)) { pairs[PayableShop.ShopType.WorkshopLeft] = shopTag.gameObject; added++; }
                if (!pairs.ContainsKey(PayableShop.ShopType.WorkshopRight)) { pairs[PayableShop.ShopType.WorkshopRight] = shopTag.gameObject; added++; }
            }
            else
            {
                // 普通商店：安全写入（重复 key 时跳过，避免原版 Add 崩溃）
                if (!pairs.ContainsKey(shopTag.type)) { pairs[shopTag.type] = shopTag.gameObject; added++; }
            }
        }
        return added;
    }

    // === SidedShop：狂战士商店身份改写 ===

    /// <summary>判断是否为希腊世界的狂战士工具商店。</summary>
    private static bool IsGreeceBerserkerShop(PayableSidedShop shop)
    {
        if (shop == null) return false;
        if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return false;

        PayableShop ps = shop.GetComponent<PayableShop>();
        return ps != null && ps.itemPrefab != null && ps.itemPrefab.CompareTag("BerserkerTool");
    }

    /// <summary>
    /// 临时把 shopType 从 Pike 换成 ShieldShop，让原版 OverrideSide 计算 12/13 槽位。
    /// 异常时不做改写（避免误伤非 Berserker 商店）。
    /// </summary>
    [HarmonyPatch(typeof(PayableSidedShop), nameof(PayableSidedShop.Awake))]
    [HarmonyPrefix]
    public static void SidedShop_Awake_Prefix(PayableSidedShop __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            if (__instance == null) return;
            if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return;

            PayableShop ps = __instance.GetComponent<PayableShop>();
            bool isBerserker = ps != null && ps.itemPrefab != null && ps.itemPrefab.CompareTag("BerserkerTool");
            if (!isBerserker) return;

            __instance.shopType = PayableShop.UnsidedShopType.ShieldShop;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    /// <summary>恢复原始 shopType（Pike），实例行为不受影响。</summary>
    [HarmonyPatch(typeof(PayableSidedShop), nameof(PayableSidedShop.Awake))]
    [HarmonyPostfix]
    public static void SidedShop_Awake_Postfix(PayableSidedShop __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            if (!IsGreeceBerserkerShop(__instance)) return;
            __instance.shopType = PayableShop.UnsidedShopType.Pike;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}
