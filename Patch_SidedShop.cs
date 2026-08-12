using System;
using System.Reflection;
using UnityEngine;
using Harmony;

namespace MyMod
{
    /// <summary>
    /// 狂战士商店身份改写（方案C核心）：
    ///
    /// 北境原版把狂战士商店伪装成 PikeLeft/PikeRight(9/10) 类型（ShopBerserker_norselands 的
    /// ShopTag.type=PikeLeft）。希腊世界 Pike 槽位被长矛商店占用，我们把狂战士商店注册到
    /// ShieldShopLeft/Right(12/13) 槽位（希腊空闲），但实例的 PayableSidedShop.Awake 会按
    /// 自身 shopType(Pike) 自注册到 9/10 —— 身份错乱。
    ///
    /// 解法：patch PayableSidedShop.Awake，当实例是"希腊世界的 BerserkerTool 商店"时，
    /// 临时把 shopType 从 Pike 换成 ShieldShop，让原版 OverrideSide 计算 12/13 并写 tag，
    /// Postfix 恢复 shopType。全链路（AddShop/ShuffleEdge/ValidateShops/存档/联机）
    /// 按原生 ShieldShop 语义自洽。
    /// </summary>
    public static class Patch_SidedShop
    {
        public static void Register(HarmonyInstance harmony)
        {
            var sidedShopType = typeof(PayableSidedShop);
            var awakeMethod = sidedShopType.GetMethod("Awake",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (awakeMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_SidedShop).GetMethod("Awake_Prefix"));
                var postfix = new HarmonyMethod(typeof(Patch_SidedShop).GetMethod("Awake_Postfix"));
                harmony.Patch(awakeMethod, prefix, postfix);
                Debug.Log("[MyMod] Patched PayableSidedShop.Awake (Berserker shop identity rewrite)");
            }
            else
            {
                Debug.LogError("[MyMod] Could not find PayableSidedShop.Awake!");
            }
        }

        // 判断是否为希腊世界的狂战士工具商店
        private static bool IsGreeceBerserkerShop(PayableSidedShop shop)
        {
            if (shop == null) return false;
            if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != 5) return false;

            PayableShop ps = shop.GetComponent<PayableShop>();
            return ps != null && ps.itemPrefab != null
                && ps.itemPrefab.CompareTag("BerserkerTool");
        }

        /// <summary>
        /// 临时把 shopType 从 Pike 换成 ShieldShop，让 OverrideSide 计算 12/13 槽位。
        /// 注意：异常时也强制改写（身份一致性优先），否则实例会以 Pike 身份注册到 9/10
        /// 覆盖真实长矛商店槽位，造成连锁错乱。
        /// </summary>
        public static void Awake_Prefix(PayableSidedShop __instance)
        {
            if (!Main.Enabled) return;

            try
            {
                if (__instance == null) return;
                if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != 5) return;
                PayableShop ps = __instance.GetComponent<PayableShop>();
                bool isBerserker = ps != null && ps.itemPrefab != null
                    && ps.itemPrefab.CompareTag("BerserkerTool");
                if (!isBerserker) return;

                __instance.shopType = PayableShop.UnsidedShopType.ShieldShop;
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Patch_SidedShop.Awake_Prefix error: " + e.Message);
                // 异常时无法判断身份，不做改写（避免误伤非 Berserker 商店）
            }
        }

        /// <summary>
        /// 恢复原始 shopType（Pike），实例行为不受影响（消费方只有 Ninja.GetDojo）。
        /// </summary>
        public static void Awake_Postfix(PayableSidedShop __instance)
        {
            try
            {
                if (!IsGreeceBerserkerShop(__instance)) return;
                __instance.shopType = PayableShop.UnsidedShopType.Pike;
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Patch_SidedShop.Awake_Postfix error: " + e.Message);
            }
        }
    }
}
