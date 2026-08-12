using System;
using System.Reflection;
using UnityEngine;
using Harmony;

namespace MyMod
{
    /// <summary>
    /// 解锁赫尔墨斯钱袋（CurrencyBagType.Hermes，希腊 DLC 的第二钱袋）。
    ///
    /// 机制（2.1.0 源码 CurrencyBagHandler.cs）：
    ///   OnGameStartHandler（每次游戏开始触发一次）：按存档状态
    ///   ChangeCurrencyBag(CampaignSaveData.current.GetCurrencyBagStatus(), 0/1) 初始化钱袋。
    ///   ChangeCurrencyBag(type, idx)：换 prefab（_regularBags/_hermesBags）、
    ///   type > status 时 SetCurrencyBagStatus 持久化 + 升级特效（PlayUpgradeEffect）、
    ///   ChangeCurrencyBag 内部对 P1/P2 都调用（playerTwo 对象总存在）。
    ///
    /// 实现：postfix 强制 ChangeCurrencyBag(Hermes, 0/1)——
    ///   - 首局触发 _isUpgrade=true → 存档持久化 Hermes + 升级特效；
    ///   - 之后每局 status 已是 Hermes → 无特效但正常切换（幂等）；
    ///   - 联机 P2 同步切换；CurrencyBagPayable.CanPay 因 status=Hermes 变 false（购买点自然失效）。
    /// </summary>
    public static class Patch_CurrencyBag
    {
        public static void Register(HarmonyInstance harmony)
        {
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
    }
}
