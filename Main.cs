using System;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;
using Harmony;

namespace MyMod
{
    public static class Main
    {
        public static bool Enabled { get; private set; }
        public static UnityModManager.ModEntry ModEntry { get; private set; }

        // Mod 设置
        public static bool infiniteMoney = false;
        public static int speedMultiplier = 2;
        public static bool fastBuild = false;
        // 岛大小倍率：固定 2x（世界生成参数，动态调整会改变已生成世界，禁止滑块/运行时修改）
        public static float mapSizeMultiplier = 2f;
        public static float enemyCountMultiplier = 1f;
        public static float enemyTimelineSpeed = 1f;

        // OnGUI 反射缓存（ArchReviewer P3 修复：避免每帧 GetField）
        private static FieldInfo _bankerWalletField;
        private static FieldInfo _bankerStashedField;
        private static int _bankerInfoFrame = -1;
        private static string _bankerInfoCache = "";

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Debug.Log("[MyMod] === Load() started ===");
            ModEntry = modEntry;
            Enabled = modEntry.Active;

            try
            {
                var harmony = HarmonyInstance.Create(modEntry.Info.Id);

                Patch_ShopPlanner.Register(harmony);
                Patch_Castle.Register(harmony);
                Patch_Mover.Register(harmony);
                Patch_Construction.Register(harmony);
                Patch_Kingdom.Register(harmony);
                Patch_Holder.Register(harmony);
                Patch_FriendlyTroll.Register(harmony);
                Patch_EnemyManager.Register(harmony);
                Patch_Knight.Register(harmony);
                Patch_Banker.Register(harmony);
                Patch_Worker.Register(harmony);
                Patch_WorkerScale.Register(harmony);
                Patch_Character.Register(harmony);
                Patch_SidedShop.Register(harmony);
                Patch_PoolManager.Register(harmony);
                Patch_World.Register(harmony);
                Patch_BeggarCamp.Register(harmony);
                Patch_Artemis.Register(harmony);
                Patch_HermesStaff.Register(harmony);
                Patch_Level.Register(harmony);
                Patch_CurrencyBag.Register(harmony);

                Debug.Log("[MyMod] Harmony patches applied successfully!");
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Failed to apply harmony patches: " + e.ToString());
                return false;
            }

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;

            Debug.Log("[MyMod] Mod loaded successfully!");
            return true;
        }

        public static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            return true;
        }

        public static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("=== 王国两位君主 Mod ===");
            GUILayout.Label("功能：所有世界的兵种商店都会出现");
            GUILayout.Space(10);

            GUILayout.Label("--- 测试功能 ---");

            bool newInfiniteMoney = GUILayout.Toggle(infiniteMoney, "无限金币");
            if (newInfiniteMoney != infiniteMoney)
            {
                infiniteMoney = newInfiniteMoney;
                Wallet.InfiniteMoney = newInfiniteMoney;
            }

            GUILayout.Label("移动速度倍率: " + speedMultiplier + "x");
            speedMultiplier = (int)GUILayout.HorizontalSlider(speedMultiplier, 1, 5);

            bool newFastBuild = GUILayout.Toggle(fastBuild, "快速建造 (约2秒)");
            if (newFastBuild != fastBuild)
            {
                fastBuild = newFastBuild;
                ConstructionBuildingComponent.AllAutoBuild = newFastBuild;
            }

            // 岛大小固定 2x（Patch_Level 使用）——世界生成参数，动态调整会改变
            // 已生成/已访问的岛（下次进入时按新宽度重新生成），不可由滑块控制，定死。
            GUILayout.Label("岛大小: 固定 2x（世界生成参数，不可调）");

            GUILayout.Label("怪物数量: " + enemyCountMultiplier.ToString("F1") + "x");
            enemyCountMultiplier = GUILayout.HorizontalSlider(enemyCountMultiplier, 1f, 5f);

            GUILayout.Label("怪物出现速度: " + enemyTimelineSpeed.ToString("F1") + "x (1=原版, 2=快1倍)");
            enemyTimelineSpeed = GUILayout.HorizontalSlider(enemyTimelineSpeed, 1f, 5f);

            GUILayout.Space(10);

            GUILayout.Label("--- 银行信息 ---");
            try
            {
                // 每 120 帧刷新一次（OnGUI 每帧执行），FieldInfo 只解析一次
                if (_bankerInfoFrame != Time.frameCount / 120)
                {
                    _bankerInfoFrame = Time.frameCount / 120;
                    if (_bankerWalletField == null)
                    {
                        _bankerWalletField = typeof(Banker).GetField("_wallet", BindingFlags.NonPublic | BindingFlags.Instance);
                        _bankerStashedField = typeof(Banker).GetField("_stashedCoins", BindingFlags.NonPublic | BindingFlags.Instance);
                    }

                    Banker[] allBankers = UnityEngine.Object.FindObjectsOfType<Banker>();
                    int totalWallet = 0;
                    int totalStashed = 0;
                    for (int i = 0; i < allBankers.Length; i++)
                    {
                        if (allBankers[i] == null) continue;
                        if (_bankerWalletField != null)
                        {
                            Wallet w = _bankerWalletField.GetValue(allBankers[i]) as Wallet;
                            if (w != null) totalWallet += w.Coins;
                        }
                        if (_bankerStashedField != null)
                        {
                            totalStashed += (int)_bankerStashedField.GetValue(allBankers[i]);
                        }
                    }
                    _bankerInfoCache = "银行家数量: " + allBankers.Length
                        + "\n银行家身上: " + totalWallet + " 金币"
                        + "\n城堡金库: " + totalStashed + " 金币"
                        + "\n总计: " + (totalWallet + totalStashed) + " 金币";
                }
                GUILayout.Label(_bankerInfoCache);
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] OnGUI banker info error: " + e.Message);
            }

            GUILayout.Space(10);
            GUILayout.Label("作者：baisiqi");
        }
    }
}
