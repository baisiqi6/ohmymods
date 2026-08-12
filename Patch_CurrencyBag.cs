using System;
using System.Reflection;
using UnityEngine;
using Harmony;
using Coatsink.Common;

namespace MyMod
{
    /// <summary>
    /// 赫尔墨斯钱袋（CurrencyBagType.Hermes）解锁 + 真实扩容 + UI 视觉适配。
    ///
    /// 三层机制（2.1.0 源码核实）：
    ///   1. 实际容量：Wallet.TotalCapacity = 1000（Wallet.cs:913 固定字段，全库零写入）。
    ///   2. 视觉容量：CurrencyBag.SpawnCurrency（CurrencyBag.cs:487-488）
    ///      `bagCurrency.Reset(flag2, count, count < 300)`——钱袋 UI 堆叠上限 300 个金币，
    ///      超出者 stack=false → 散落到地面（原版溢出设计）。
    ///   3. 钱袋是 HUD 元素（挂在 InterfaceCamera），无 Collider；金币拾取靠
    ///      金币×玩家物理碰撞重叠 + 点击 OverlapCircle（无"容器碰撞空间"）。
    ///
    /// 本 mod：
    ///   - 解锁：开局强制 ChangeCurrencyBag(Hermes)（OnGameStartHandler postfix）。
    ///   - 扩容：ChangeCurrencyBag postfix 按类型设玩家钱包容量（Hermes 2000 / Bag 1000）。
    ///   - UI：BagCurrency.Reset prefix 视觉堆叠上限 300→600；
    ///         CurrencyBag.Awake postfix + SetCurrencyBag postfix 放大 2.0x（金币堆子物体继承）。
    /// </summary>
    public static class Patch_CurrencyBag
    {
        private const int HermesCapacity = 2000;
        private const int BagCapacity = 1000;
        private const int VisualCoinLimit = 600;  // 原版硬编码 300（CurrencyBag.cs:487）
        private const float BagScaleMultiplier = 2.0f;  // 钱袋 UI 放大倍率（含金币堆，子物体继承）
        private static float _baseBagScale = -1f;       // 首次记录的原始基准（-1=未记录）

        public static void Register(HarmonyInstance harmony)
        {
            var handlerType = typeof(CurrencyBagHandler);

            // 1. 解锁：开局强制 Hermes
            var onGameStart = handlerType.GetMethod("OnGameStartHandler",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (onGameStart != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_CurrencyBag).GetMethod("OnGameStartHandler_Postfix"));
                harmony.Patch(onGameStart, null, postfix);
                Debug.Log("[MyMod] Patched CurrencyBagHandler.OnGameStartHandler (Hermes bag unlock)");
            }
            else
            {
                Debug.LogError("[MyMod] CurrencyBagHandler.OnGameStartHandler not found!");
            }

            // 2. 扩容：钱袋切换时按类型设钱包容量
            var changeMethod = handlerType.GetMethod("ChangeCurrencyBag",
                BindingFlags.Public | BindingFlags.Instance);
            if (changeMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_CurrencyBag).GetMethod("ChangeCurrencyBag_Postfix"));
                harmony.Patch(changeMethod, null, postfix);
                Debug.Log("[MyMod] Patched CurrencyBagHandler.ChangeCurrencyBag (capacity)");
            }
            else
            {
                Debug.LogError("[MyMod] CurrencyBagHandler.ChangeCurrencyBag not found!");
            }

            // 3. UI：视觉堆叠上限 300 → 600（超出仍散落）
            var resetMethod = typeof(BagCurrency).GetMethod("Reset",
                BindingFlags.Public | BindingFlags.Instance);
            if (resetMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_CurrencyBag).GetMethod("Reset_Prefix"));
                harmony.Patch(resetMethod, prefix, null);
                Debug.Log("[MyMod] Patched BagCurrency.Reset (visual coin limit " + VisualCoinLimit + ")");
            }
            else
            {
                Debug.LogError("[MyMod] BagCurrency.Reset not found!");
            }

            // 4. UI：钱袋整体放大（Awake 每实例一次）
            var awakeMethod = typeof(CurrencyBag).GetMethod("Awake",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (awakeMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_CurrencyBag).GetMethod("Awake_Postfix"));
                harmony.Patch(awakeMethod, null, postfix);
                Debug.Log("[MyMod] Patched CurrencyBag.Awake (UI scale x" + BagScaleMultiplier + ")");
            }
            else
            {
                Debug.LogError("[MyMod] CurrencyBag.Awake not found!");
            }

            // 5. UI 兜底：SetCurrencyBag 返回新钱袋实例，每局切换必经——重设 scale
            var setBagMethod = handlerType.GetMethod("SetCurrencyBag",
                BindingFlags.Public | BindingFlags.Instance);
            if (setBagMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_CurrencyBag).GetMethod("SetCurrencyBag_Postfix"));
                harmony.Patch(setBagMethod, null, postfix);
                Debug.Log("[MyMod] Patched CurrencyBagHandler.SetCurrencyBag (scale reapply)");
            }
            else
            {
                Debug.LogError("[MyMod] CurrencyBagHandler.SetCurrencyBag not found!");
            }
        }

        /// <summary>
        /// 钱袋 UI 放大（幂等，金币堆是 transform 子物体自动继承缩放）。
        /// </summary>
        public static void Awake_Postfix(CurrencyBag __instance)
        {
            if (!Main.Enabled) return;
            try
            {
                ApplyBagScale(__instance);
                Debug.Log("[MyMod] Bag scale applied: " + __instance.gameObject.name
                    + " -> " + __instance.transform.localScale.x);
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Bag scale error: " + e.Message);
            }
        }

        /// <summary>
        /// 每次切换钱袋后重设 scale（防 Awake 设置被显示流程覆盖；幂等）。
        /// </summary>
        public static void SetCurrencyBag_Postfix(CurrencyBag __result)
        {
            if (!Main.Enabled) return;
            try
            {
                ApplyBagScale(__result);
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] SetCurrencyBag scale error: " + e.Message);
            }
        }

        /// <summary>
        /// 绝对设置（非乘法累积）：首次记录原始基准，之后恒为 基准 × 倍率。
        /// </summary>
        private static void ApplyBagScale(CurrencyBag bag)
        {
            if (bag == null) return;
            Vector3 s = bag.transform.localScale;
            if (_baseBagScale < 0f)
            {
                _baseBagScale = Mathf.Max(s.x, s.y);  // 首次记录原始基准
            }
            float target = _baseBagScale * BagScaleMultiplier;
            s.x = Mathf.Sign(s.x) * target;
            s.y = Mathf.Sign(s.y) * target;
            bag.transform.localScale = s;
        }

        public static void OnGameStartHandler_Postfix(CurrencyBagHandler __instance)
        {
            if (!Main.Enabled) return;

            try
            {
                __instance.ChangeCurrencyBag(CurrencyBagType.Hermes, 0);
                __instance.ChangeCurrencyBag(CurrencyBagType.Hermes, 1);
                Debug.Log("[MyMod] Hermes currency bag unlocked");
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Hermes bag unlock error: " + e.Message);
            }
        }

        /// <summary>
        /// 钱袋切换后按类型设置对应玩家的钱包容量（幂等，每局重设）。
        /// </summary>
        public static void ChangeCurrencyBag_Postfix(CurrencyBagType currencyBagType, int playerIndex)
        {
            if (!Main.Enabled) return;

            try
            {
                int capacity = (currencyBagType == CurrencyBagType.Hermes) ? HermesCapacity : BagCapacity;
                var kingdom = SingletonMonoBehaviour<Managers>.Inst.kingdom;
                if (kingdom == null) return;

                Player player = (playerIndex == 0) ? kingdom.playerOne : kingdom.playerTwo;
                if (player != null && player.wallet != null)
                {
                    player.wallet.TotalCapacity = capacity;
                    Debug.Log("[MyMod] Player" + (playerIndex + 1) + " wallet capacity = " + capacity
                        + " (" + currencyBagType + ")");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Currency bag capacity error: " + e.Message);
            }
        }

        /// <summary>
        /// 视觉堆叠上限：nthCoin < 600 堆叠显示，超出散落（原版 300）。
        /// </summary>
        public static void Reset_Prefix(int nthCoin, ref bool stack)
        {
            if (!Main.Enabled) return;
            stack = nthCoin < VisualCoinLimit;
        }
    }
}
