using System;
using System.Reflection;
using UnityEngine;
using Harmony;

namespace MyMod
{
    /// <summary>
    /// 修复原生池丢失 bug（两层）：
    ///
    /// 第一层（2.0.1 时代）：mod 在场景早期注册 sync 池（cachedPools.Count > 0）导致
    /// PoolManager.OnLevelLoaded 的 `if (cachedPools == null || Count == 0)` 判 false，
    /// 跳过 InitPools()——原生池（Coin Indicator/Fish/Building Dust 等）从未加载 → 投币崩溃。
    /// 修复：Prefix 强制 InitPools()，随后重注册 mod sync 池。
    ///
    /// 第二层（2.1.0 发现）：InitPools 只注册 particlePools + **当前 biome** 的池资产
    /// （"Object Pools/<biome>"），通用特效池（Transform Sparkles/Building Dust/Snow 等）
    /// 在其他 biome 的 BiomeObjectPools 里——希腊世界读档恢复特效对象时池缺失 →
    /// Character.UpgradeTransitionFX NRE → 乞丐捡金币 Promote("Peasant") 中断
    /// （用户报"扔金币乞丐捡不了"）。修复：RegisterAllBiomePools 补注册全部 biome 的池资产
    /// （按 prefab 名去重，防 Dictionary.Add 重名炸）。
    /// </summary>
    public static class Patch_PoolManager
    {
        public static void Register(HarmonyInstance harmony)
        {
            var pmType = typeof(PoolManager);
            var onLevelLoaded = pmType.GetMethod("OnLevelLoaded",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (onLevelLoaded != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_PoolManager).GetMethod("OnLevelLoaded_Prefix"));
                harmony.Patch(onLevelLoaded, prefix, null);
                Debug.Log("[MyMod] Patched PoolManager.OnLevelLoaded (force InitPools)");
            }
            else
            {
                Debug.LogError("[MyMod] Could not find PoolManager.OnLevelLoaded!");
            }
        }

        public static bool OnLevelLoaded_Prefix(PoolManager __instance)
        {
            if (!Main.Enabled) return true;  // mod 关闭：走原版逻辑

            try
            {
                if (__instance == null) return true;

                // 强制重建原生池（忽略 cachedPools 非空跳过逻辑）
                var initPoolsMethod = typeof(PoolManager).GetMethod("InitPools",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (initPoolsMethod != null)
                {
                    initPoolsMethod.Invoke(__instance, null);
                    Debug.Log("[MyMod] Force InitPools() executed (native pools rebuilt)");
                }

                // 2.1.0 补全：全 biome 池（Sparkles/Building Dust/Snow 等特效池）
                RegisterAllBiomePools(__instance);

                // InitPools 清掉了 mod 注册的 sync 池，重新注册
                ReRegisterModPools(__instance);

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Patch_PoolManager error: " + e.Message);
                return true;
            }
        }

        /// <summary>
        /// 补注册全部 biome 的 BiomeObjectPools 池（按 prefab 名去重）。
        /// 注册逻辑与 PoolManager.CreateAndInitializePoolsFromCollection 等价
        /// （Instantiate + SetName + 缓存三件套 + Init）。
        /// </summary>
        private static void RegisterAllBiomePools(PoolManager pm)
        {
            try
            {
                var cachedPoolsField = typeof(PoolManager).GetField("cachedPools",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var namePairsField = typeof(PoolManager).GetField("cachedNamePoolPairs",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var syncIdField = typeof(PoolManager).GetField("cachedSyncIdPoolPairs",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (cachedPoolsField == null || namePairsField == null || syncIdField == null) return;

                var cachedPools = cachedPoolsField.GetValue(pm) as System.Collections.Generic.List<Pool>;
                var namePairs = namePairsField.GetValue(pm) as System.Collections.Generic.Dictionary<string, Pool>;
                var syncIdPairs = syncIdField.GetValue(pm) as System.Collections.Generic.Dictionary<int, Pool>;
                if (cachedPools == null || namePairs == null || syncIdPairs == null) return;

                int added = 0;
                var allCollections = Resources.LoadAll<BiomeObjectPools>("");
                foreach (var collection in allCollections)
                {
                    if (collection == null || collection.biomeObjectPools == null) continue;
                    foreach (var poolPrefab in collection.biomeObjectPools)
                    {
                        if (poolPrefab == null || poolPrefab.prefab == null) continue;
                        if (namePairs.ContainsKey(poolPrefab.prefab.name)) continue;  // 已注册跳过

                        Pool inst = UnityEngine.Object.Instantiate<Pool>(poolPrefab, pm.transform);
                        inst.SetName();
                        cachedPools.Add(inst);
                        namePairs.Add(inst.prefab.name, inst);
                        if (inst.sync && inst.syncID != 0)
                        {
                            syncIdPairs.Add((int)inst.syncID, inst);
                        }
                        try
                        {
                            inst.Init(null);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError("[MyMod] Pool init error (" + inst.name + "): " + ex.Message);
                        }
                        added++;
                    }
                }
                Debug.Log("[MyMod] RegisterAllBiomePools: added " + added + " missing pools");
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] RegisterAllBiomePools error: " + e.Message);
            }
        }

        /// <summary>
        /// 重新注册 mod 的跨生物群系 sync 池（InitPools 清掉了它们）。
        /// </summary>
        private static void ReRegisterModPools(PoolManager pm)
        {
            try
            {
                if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != 5) return;

                // 工具池
                Patch_Castle.EnsurePoolForDroppableTool("ToolNinja");
                Patch_Castle.EnsurePoolForDroppableTool("ToolBerserker");
                // 角色池（从 Holder 拿 prefab）
                Patch_Castle.EnsurePoolForCharacter("Ninja");
                Patch_Castle.EnsurePoolForCharacter("Berserker");
                Patch_Castle.EnsurePoolForCharacter("BerserkerLeader");
                Patch_Castle.EnsurePoolForCharacter("Worker");
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] ReRegisterModPools error: " + e.Message);
            }
        }
    }
}
