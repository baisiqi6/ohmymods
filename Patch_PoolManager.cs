using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Harmony;
using Coatsink.Common;

namespace MyMod
{
    /// <summary>
    /// 池系统修复（两层，均经 reviewer 交叉审核修正）：
    ///
    /// 第一层（2.0.1 时代，保留）：mod 在场景早期注册 sync 池（cachedPools.Count > 0）导致
    /// PoolManager.OnLevelLoaded 的 `if (cachedPools == null || Count == 0)` 判 false，
    /// 跳过 InitPools()——原生池从未加载。修复：Prefix 强制 InitPools() + 重注册 mod 池。
    ///
    /// 第二层（读档恢复 NRE，2026-08-12 reviewer 修正后的正确根因）：
    /// 读档时 ProgramDirector → IslandSaveData.TryPopObjectsToScene 恢复场景对象
    /// （Archer.SetKnight → Character.UpgradeTransitionFX → Pool.SpawnGO("Transform Sparkles")）
    /// 发生在 Managers.OnLevelLoaded **之前**——彼时 PoolManager.Init 的
    /// `if (!SceneManager.GetSceneByName("main").isLoaded) return;` 早退导致 InitPools 未跑，
    /// 且读档场景的 PoolManager 被序列化为 learningMode=false → SpawnGO 的
    /// "learningMode 现场建池"兜底失效 → "Pool not found" → NRE → 乞丐 Promote 等中断。
    ///
    /// 修复：Pool.SpawnGO prefix——池缺失且非 learningMode 时，直接 CreatePoolFor 现场建池
    /// （与 learningMode 同路径，对象正常进池系统回收）。不依赖时序，覆盖一切"池未建但有人 Spawn"场景。
    ///
    /// 【已废弃】RegisterAllBiomePools（全 biome 池补注册）：假设错误（Sparkles/Building Dust
    /// 本就在 particlePools 每 biome 都建）+ syncID=119 跨 biome 冲突（Boat_Fleet_Greece vs
    /// Warrior_Ghost_norselands 同 syncID，Dictionary.Add 抛异常中断注册 → 半初始化池进
    /// cachedPools → 每帧 DoUpdate NRE）。删除。
    /// </summary>
    public static class Patch_PoolManager
    {
        private static FieldInfo _poolsByPrefabField;
        private static MethodInfo _createPoolForMethod;

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

            // 第二层：SpawnGO 池缺失兜底（读档恢复 NRE 真根因）
            var spawnGOMethod = typeof(Pool).GetMethod("SpawnGO",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (spawnGOMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_PoolManager).GetMethod("SpawnGO_Prefix"));
                harmony.Patch(spawnGOMethod, prefix, null);
                Debug.Log("[MyMod] Patched Pool.SpawnGO (missing-pool fallback)");
            }
            else
            {
                Debug.LogError("[MyMod] Could not find Pool.SpawnGO!");
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
        /// 池缺失兜底：prefab 无池且非 learningMode 时现场 CreatePoolFor。
        /// 只影响"池没建"场景，正常路径零开销（除一次静态字典 ContainsKey）。
        /// </summary>
        public static void SpawnGO_Prefix(GameObject prefab)
        {
            if (!Main.Enabled) return;
            try
            {
                if (prefab == null) return;
                var managers = SingletonMonoBehaviour<Managers>.Inst;
                if (managers == null) return;
                var pm = managers.pools;
                if (pm == null) return;

                // learningMode：原版 SpawnGO 自己会 CreatePoolFor，无需兜底
                if (pm.learningMode) return;

                // 反射缓存
                if (_poolsByPrefabField == null)
                {
                    _poolsByPrefabField = typeof(Pool).GetField("poolsByPrefab",
                        BindingFlags.NonPublic | BindingFlags.Static);
                }
                if (_createPoolForMethod == null)
                {
                    _createPoolForMethod = typeof(PoolManager).GetMethod("CreatePoolFor",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                if (_poolsByPrefabField == null || _createPoolForMethod == null) return;

                var dict = _poolsByPrefabField.GetValue(null) as Dictionary<GameObject, Pool>;
                if (dict == null) return;
                if (dict.ContainsKey(prefab)) return;  // 池已存在，原逻辑正常

                // 池缺失：现场建池（与 learningMode 同路径）
                _createPoolForMethod.Invoke(pm, new object[] { prefab });
                Debug.Log("[MyMod] Created missing pool for " + prefab.name);
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] SpawnGO fallback error: " + e.Message);
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
