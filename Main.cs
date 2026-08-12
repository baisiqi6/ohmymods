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
        public static float mapSizeMultiplier = 1f;
        public static float enemyCountMultiplier = 1f;
        public static float enemyTimelineSpeed = 1f;

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
                Patch_Probe.Register(harmony);
                Patch_SidedShop.Register(harmony);
                Patch_PoolManager.Register(harmony);
                Patch_World.Register(harmony);
                Patch_BeggarCamp.Register(harmony);
                Patch_Artemis.Register(harmony);
                Patch_HermesStaff.Register(harmony);

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

            GUILayout.Label("地图大小: " + mapSizeMultiplier.ToString("F1") + "x");
            mapSizeMultiplier = GUILayout.HorizontalSlider(mapSizeMultiplier, 1f, 5f);

            GUILayout.Label("怪物数量: " + enemyCountMultiplier.ToString("F1") + "x");
            enemyCountMultiplier = GUILayout.HorizontalSlider(enemyCountMultiplier, 1f, 5f);

            GUILayout.Label("怪物出现速度: " + enemyTimelineSpeed.ToString("F1") + "x (1=原版, 2=快1倍)");
            enemyTimelineSpeed = GUILayout.HorizontalSlider(enemyTimelineSpeed, 1f, 5f);

            GUILayout.Space(10);

            GUILayout.Label("--- 银行信息 ---");
            try
            {
                Banker[] allBankers = UnityEngine.Object.FindObjectsOfType<Banker>();
                int totalWallet = 0;
                int totalStashed = 0;
                for (int i = 0; i < allBankers.Length; i++)
                {
                    if (allBankers[i] == null) continue;
                    var walletField = typeof(Banker).GetField("_wallet", BindingFlags.NonPublic | BindingFlags.Instance);
                    var stashedField = typeof(Banker).GetField("_stashedCoins", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (walletField != null)
                    {
                        Wallet w = walletField.GetValue(allBankers[i]) as Wallet;
                        if (w != null) totalWallet += w.Coins;
                    }
                    if (stashedField != null)
                    {
                        totalStashed += (int)stashedField.GetValue(allBankers[i]);
                    }
                }
                GUILayout.Label("银行家数量: " + allBankers.Length);
                GUILayout.Label("银行家身上: " + totalWallet + " 金币");
                GUILayout.Label("城堡金库: " + totalStashed + " 金币");
                GUILayout.Label("总计: " + (totalWallet + totalStashed) + " 金币");
            }
            catch { }

            GUILayout.Space(10);
            GUILayout.Label("作者：baisiqi");
        }
    }
}
