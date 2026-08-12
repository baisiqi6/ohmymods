using System;
using System.Reflection;
using UnityEngine;
using Harmony;
using Coatsink.Common;

namespace MyMod
{
    public static class Patch_Banker
    {
        private const string SHARED_STASH_KEY = "MyMod_SharedBankStash";
        private static int sharedStash = -1;

        private static int _bankerCheckFrame = 0;

        // 缓存的反射字段
        private static FieldInfo _banker_targetCoin;
        private static FieldInfo _banker_coinScanner;
        private static FieldInfo _banker_wallet;
        private static FieldInfo _banker_stashedCoins;
        private static MethodInfo _canPickUpMethod;
        private static bool _bankerFieldsCached = false;

        public static void Register(HarmonyInstance harmony)
        {
            var bankerType = typeof(Banker);

            PatchMethod(harmony, bankerType, "FinaliseEmerge", null, "FinaliseEmerge_Postfix");
            PatchMethod(harmony, bankerType, "HandleOnDayStart", null, "HandleOnDayStart_Postfix");
            PatchMethod(harmony, bankerType, "DropOff", "DropOff_Prefix", "DropOff_Postfix");
            PatchMethod(harmony, bankerType, "Hide", null, "Hide_Postfix");
            PatchMethod(harmony, bankerType, "Payout", null, "Payout_Postfix");
            PatchMethod(harmony, bankerType, "OpenCastleDoor", null, "OpenCastleDoor_Postfix");
            PatchMethod(harmony, bankerType, "Awake", "Awake_Prefix", "Awake_Postfix");
            PatchMethod(harmony, bankerType, "Update", null, "Update_Postfix");

            // ShouldHide - Prefix
            var shouldHideMethod = bankerType.GetMethod("ShouldHide", BindingFlags.NonPublic | BindingFlags.Instance);
            if (shouldHideMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_Banker).GetMethod("ShouldHide_Prefix"));
                harmony.Patch(shouldHideMethod, prefix, null);
                Debug.Log("[MyMod] Patched Banker.ShouldHide");
            }

            // GrabCoin - Prefix
            var grabCoinMethod = bankerType.GetMethod("GrabCoin", BindingFlags.NonPublic | BindingFlags.Instance);
            if (grabCoinMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_Banker).GetMethod("GrabCoin_Prefix"));
                harmony.Patch(grabCoinMethod, prefix, null);
                Debug.Log("[MyMod] Patched Banker.GrabCoin");
            }

            // ClaimCoins - Prefix
            var claimCoinsMethod = bankerType.GetMethod("ClaimCoins", BindingFlags.NonPublic | BindingFlags.Instance);
            if (claimCoinsMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_Banker).GetMethod("ClaimCoins_Prefix"));
                harmony.Patch(claimCoinsMethod, prefix, null);
                Debug.Log("[MyMod] Patched Banker.ClaimCoins");
            }
        }

        private static void PatchMethod(HarmonyInstance harmony, System.Type type, string methodName, string prefixName, string postfixName)
        {
            var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
            {
                Debug.LogError("[MyMod] Could not find " + type.Name + "." + methodName + "!");
                return;
            }

            HarmonyMethod prefix = null;
            HarmonyMethod postfix = null;

            if (prefixName != null)
                prefix = new HarmonyMethod(typeof(Patch_Banker).GetMethod(prefixName));
            if (postfixName != null)
                postfix = new HarmonyMethod(typeof(Patch_Banker).GetMethod(postfixName));

            harmony.Patch(method, prefix, postfix);
            Debug.Log("[MyMod] Patched " + type.Name + "." + methodName);
        }

        private static void CacheBankerFields()
        {
            if (_bankerFieldsCached) return;
            var bf = BindingFlags.NonPublic | BindingFlags.Instance;
            _banker_targetCoin = typeof(Banker).GetField("_targetCoin", bf);
            _banker_coinScanner = typeof(Banker).GetField("_coinScanner", bf);
            _banker_wallet = typeof(Banker).GetField("_wallet", bf);
            _banker_stashedCoins = typeof(Banker).GetField("_stashedCoins", bf);
            _canPickUpMethod = typeof(IPickupAttributeProviderExtensions).GetMethod("CanPickUp");
            _bankerFieldsCached = true;
        }

        // === 共享存款 ===

        private static int LoadSharedStash()
        {
            if (!PlayerPrefs.HasKey(SHARED_STASH_KEY)) return 500;
            return PlayerPrefs.GetInt(SHARED_STASH_KEY);
        }

        private static void SaveSharedStash(int value)
        {
            PlayerPrefs.SetInt(SHARED_STASH_KEY, value);
            PlayerPrefs.Save();
        }

        private static void EnsureLoaded()
        {
            if (sharedStash < 0)
            {
                sharedStash = LoadSharedStash();
                Debug.Log("[MyMod] Banker shared stash loaded: " + sharedStash);
            }
        }

        private static void SyncStash(Banker __instance)
        {
            CacheBankerFields();
            if (_banker_stashedCoins == null) return;
            EnsureLoaded();
            _banker_stashedCoins.SetValue(__instance, sharedStash);
        }

        private static void UpdateSharedStash(Banker __instance)
        {
            CacheBankerFields();
            if (_banker_stashedCoins == null) return;
            EnsureLoaded();
            int currentStash = (int)_banker_stashedCoins.GetValue(__instance);
            if (currentStash > sharedStash)
            {
                sharedStash = currentStash;
                SaveSharedStash(sharedStash);
            }
        }

        // === Postfix 补丁 ===

        public static void FinaliseEmerge_Postfix(Banker __instance)
        {
            if (!Main.Enabled) return;
            SyncStash(__instance);
        }

        public static void HandleOnDayStart_Postfix(Banker __instance)
        {
            if (!Main.Enabled) return;
            SyncStash(__instance);
        }

        public static void DropOff_Postfix(Banker __instance)
        {
            if (!Main.Enabled) return;
            UpdateSharedStash(__instance);
        }

        public static void Hide_Postfix(Banker __instance)
        {
            if (!Main.Enabled) return;
            UpdateSharedStash(__instance);
        }

        public static void Payout_Postfix(Banker __instance)
        {
            if (!Main.Enabled) return;
            CacheBankerFields();
            if (_banker_stashedCoins == null) return;
            EnsureLoaded();
            sharedStash = (int)_banker_stashedCoins.GetValue(__instance);
            SaveSharedStash(sharedStash);
        }

        public static void OpenCastleDoor_Postfix(Banker __instance)
        {
            if (!Main.Enabled) return;
            CacheBankerFields();
            if (_banker_stashedCoins == null) return;
            EnsureLoaded();
            _banker_stashedCoins.SetValue(__instance, sharedStash);
            Castle castle = SingletonMonoBehaviour<Managers>.Inst.kingdom.castle;
            if (castle != null) castle.SetStash(sharedStash);
        }

        // === Awake - 去重 + 扫描范围 + 移速 ===

        /// <summary>
        /// 关键：Banker.Awake 硬编码 RegisterObject(903, Dynamic)。多个 Banker 实例
        /// （原版 + 旧存档残留的 Banker_Extra 克隆）同时 Awake 时 NetID 903 冲突 →
        /// duplicate pseudodyn key → 网络层崩溃 → 原生池丢失 → 无法购买。
        /// Prefix 检测：如果场景里已有注册了 903 的 Banker（通常是原版），销毁自己并跳过 Awake。
        /// </summary>
        public static bool Awake_Prefix(Banker __instance)
        {
            if (!Main.Enabled) return true;

            try
            {
                // 诊断：打印当前场景所有 Banker
                Banker[] allBankers = UnityEngine.Object.FindObjectsOfType<Banker>();
                string names = "";
                foreach (var b in allBankers)
                {
                    if (b != null)
                        names += "[" + b.gameObject.name + "]";
                }
                Debug.Log("[MyMod] Banker.Awake_Prefix: current=" + __instance.gameObject.name
                    + " total=" + allBankers.Length + " all=" + names);

                // 任何重复 Banker 都销毁（不只 Banker_Extra）：
                // 游戏原版 Castle.CatchupToLevel 在单帧内多次执行时，DynamicObjects.TryGetValue(903)
                // 竞态导致生成多个 Banker（读档时 includePrevious=true 多次触发），
                // 每个都注册 NetID 903 → duplicate key → 网络崩溃 → 原生池丢失。
                // 规则：如果场景已有"其他" Banker 实例，当前实例销毁。
                foreach (var b in allBankers)
                {
                    if (b == null || b == __instance) continue;
                    if (b.gameObject.activeInHierarchy || b.gameObject.name == "Banker(Clone)")
                    {
                        Debug.Log("[MyMod] Banker.Awake_Prefix: destroying duplicate " + __instance.gameObject.name
                            + " (already have " + b.gameObject.name + ")");
                        UnityEngine.Object.Destroy(__instance.gameObject);
                        return false; // 跳过 Awake（不注册 903）
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Banker.Awake_Prefix error: " + e.Message);
                return true;
            }
        }

        public static void Awake_Postfix(Banker __instance)
        {
            if (!Main.Enabled) return;
            CacheBankerFields();

            try
            {
                var coinScanRangeField = typeof(Banker).GetField("coinScanRange", BindingFlags.Public | BindingFlags.Instance);
                if (coinScanRangeField != null) coinScanRangeField.SetValue(__instance, 300f);

                var coinScannerField = typeof(Banker).GetField("_coinScanner", BindingFlags.NonPublic | BindingFlags.Instance);
                if (coinScannerField != null)
                {
                    Scanner scanner = coinScannerField.GetValue(__instance) as Scanner;
                    if (scanner != null)
                    {
                        scanner.range = 300f;
                        scanner.rangeBehind = 300f;
                        var intervalField = typeof(Scanner).GetField("_interval", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (intervalField != null) intervalField.SetValue(scanner, 0.1f);
                    }
                }

                var coinGatherField = typeof(Banker).GetField("coinGatherTargetPercentage", BindingFlags.Public | BindingFlags.Instance);
                if (coinGatherField != null) coinGatherField.SetValue(__instance, 0.9f);

                var walkSpeedField = typeof(Banker).GetField("walkSpeed", BindingFlags.Public | BindingFlags.Instance);
                var runSpeedField = typeof(Banker).GetField("runSpeed", BindingFlags.Public | BindingFlags.Instance);
                if (walkSpeedField != null)
                {
                    float ws = (float)walkSpeedField.GetValue(__instance);
                    if (ws > 0 && ws < 5f) walkSpeedField.SetValue(__instance, ws * 15f);
                }
                if (runSpeedField != null)
                {
                    float rs = (float)runSpeedField.GetValue(__instance);
                    if (rs > 0 && rs < 5f) runSpeedField.SetValue(__instance, rs * 15f);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in Banker Awake patch: " + e.Message);
            }
        }

        // === Update - 银行家数量控制 ===

        public static void Update_Postfix(Banker __instance)
        {
            if (!Main.Enabled) return;

            int frame = Time.frameCount;
            if (frame - _bankerCheckFrame < 120) return;
            _bankerCheckFrame = frame;

            try
            {
                Banker[] allBankers = UnityEngine.Object.FindObjectsOfType<Banker>();
                int count = allBankers.Length;

                // 清理旧存档残留的 Banker_Extra 克隆（它们带着与原版 Banker 相同的
                // Persistent.path，读档时争抢 NetID 903 → duplicate pseudodyn key
                // → 网络层崩溃 → 原生池丢失 → 无法购买）。保留场景原版 Banker。
                bool hasOriginal = false;
                foreach (var b in allBankers)
                {
                    if (b != null && b.gameObject.name != "Banker_Extra")
                    {
                        hasOriginal = true;
                        break;
                    }
                }
                bool cleaned = false;
                foreach (var b in allBankers)
                {
                    if (b != null && b.gameObject.name == "Banker_Extra" && hasOriginal)
                    {
                        UnityEngine.Object.Destroy(b.gameObject);
                        cleaned = true;
                    }
                }
                if (cleaned)
                {
                    Debug.Log("[MyMod] Destroyed stale Banker_Extra clones (persistent path conflict)");
                    return;
                }

                if (count > 5)
                {
                    for (int i = 5; i < count; i++)
                        UnityEngine.Object.Destroy(allBankers[i].gameObject);
                    Debug.Log("[MyMod] Destroyed " + (count - 5) + " excess bankers");
                    return;
                }

                // 补员到 5 个已删除（ArchReviewer 2026-08-12 P1 修复）：
                // 原版 Banker.Awake 硬编码 NetworkPostbox.RegisterObject(903, Dynamic)（Banker.cs:54），
                // 网络层 NetID 903 唯一——克隆走 Awake 必 duplicate key 崩溃；不走 Awake 则无 FSM 无法工作。
                // "5 银行家"在 2.0.1 架构下不可实现，且原实现与 Awake_Prefix 去重自相矛盾
                // （每 120 帧 Instantiate/Destroy 循环 + 日志刷屏）。共享银行增强
                // （ShouldHide false / coinScanRange 300 / 瞬移收币）保留，单银行家即可。
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in Banker Update patch: " + e.Message);
            }
        }

        // === ShouldHide - 夜间不休息 ===

        public static bool ShouldHide_Prefix(ref bool __result)
        {
            if (!Main.Enabled) return true;
            __result = false;
            return false;
        }

        // === GrabCoin - 瞬移到金币 ===

        public static void GrabCoin_Prefix(Banker __instance)
        {
            if (!Main.Enabled) return;
            CacheBankerFields();

            try
            {
                DroppableCurrency targetCoin = _banker_targetCoin.GetValue(__instance) as DroppableCurrency;
                if (targetCoin == null) return;

                __instance.transform.position = targetCoin.transform.position;

                Wallet wallet = _banker_wallet.GetValue(__instance) as Wallet;
                if (wallet != null && wallet.Coins < wallet.TotalCapacity)
                {
                    wallet.Coins++;
                    if (wallet.Coins >= wallet.TotalCapacity)
                        wallet.CanGrabCoins = false;
                }

                targetCoin.gameObject.SetActive(false);
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in GrabCoin prefix: " + e.Message);
            }
        }

        // === DropOff Prefix - 瞬移到城堡 ===

        public static void DropOff_Prefix(Banker __instance)
        {
            if (!Main.Enabled) return;

            try
            {
                float campfireX = SingletonMonoBehaviour<Managers>.Inst.kingdom.campfirePosition;
                Vector3 pos = __instance.transform.position;
                pos.x = campfireX + 0.8f;
                __instance.transform.position = pos;
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in DropOff prefix: " + e.Message);
            }
        }

        // === ClaimCoins - 远程直接拾取 ===

        public static bool ClaimCoins_Prefix(Banker __instance)
        {
            if (!Main.Enabled) return true;
            CacheBankerFields();

            try
            {
                _banker_targetCoin.SetValue(__instance, null);

                Scanner coinScanner = _banker_coinScanner.GetValue(__instance) as Scanner;
                if (coinScanner == null) return false;

                Wallet wallet = _banker_wallet.GetValue(__instance) as Wallet;
                if (wallet == null || wallet.Coins >= wallet.TotalCapacity) return false;

                GameObject[] array;
                int all = coinScanner.GetAll(out array);

                for (int i = 0; i < all; i++)
                {
                    if (wallet.Coins >= wallet.TotalCapacity) break;

                    DroppableCurrency component = array[i].GetComponent<DroppableCurrency>();
                    if (component != null && component.isActiveAndEnabled && component.CurrencyType == CurrencyType.Coins)
                    {
                        bool canPickUp = (bool)_canPickUpMethod.Invoke(null, new object[] { __instance, component });
                        if (canPickUp && component.TryFriendlyClaim(__instance.gameObject, 60f))
                        {
                            wallet.Coins++;
                            if (wallet.Coins >= wallet.TotalCapacity)
                                wallet.CanGrabCoins = false;
                            Pool.Despawn(component.gameObject, true);
                        }
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in ClaimCoins prefix: " + e.Message);
                return true;
            }
        }

    }
}
