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
    ///      超出者 stack=false → 散落到地面（CollideWithGround + 缩放 0.8，原版溢出设计）。
    ///   3. 钱袋是 HUD 元素（挂在 InterfaceCamera），RecalcPosition 按玩家/合作模式定位右上角。
    ///
    /// 本 mod：
    ///   - 解锁：开局强制 ChangeCurrencyBag(Hermes)（OnGameStartHandler postfix，首局持久化+特效）。
    ///   - 扩容：ChangeCurrencyBag postfix 按类型设玩家钱包容量（Hermes 2000 / Bag 1000），
    ///     每局 ChangeCurrencyBag 必触发 → 读档后自动重设（TotalCapacity 非持久字段）。
    ///   - UI：BagCurrency.Reset prefix 重设视觉堆叠上限 600（原 300，与容量同比例 30%），
    ///     超出 600 仍保留散落溢出设计。
    /// </summary>
    public static class Patch_CurrencyBag
    {
        private const int HermesCapacity = 2000;
        private const int BagCapacity = 1000;
        private const int VisualCoinLimit = 600;  // 原版硬编码 300（CurrencyBag.cs:487）
        private const float BagScaleMultiplier = 1.3f;  // 钱袋 UI 放大倍率（含金币堆，子物体继承）

        public static void Register(HarmonyInstance harmony)
        {
            // 1. 解锁：开局强制 Hermes
            var handlerType = typeof(CurrencyBagHandler);
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
            // 4. UI：钱袋整体放大 1.3x（金币堆是子物体，继承缩放一起变大）
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
        }

        /// <summary>
        /// 钱袋 UI 放大（Awake 每实例一次，幂等；BagCurrency 是 transform 子物体自动继承）。
        /// </summary>
        public static void Awake_Postfix(CurrencyBag __instance)
        {
            if (!Main.Enabled) return;
            try
            {
                Vector3 s = __instance.transform.localScale;
                s.x *= BagScaleMultiplier;
                s.y *= BagScaleMultiplier;
                __instance.transform.localScale = s;
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Bag scale error: " + e.Message);
            }
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
