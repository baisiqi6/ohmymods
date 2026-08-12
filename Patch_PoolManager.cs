using System;
using System.Reflection;
using UnityEngine;
using Harmony;

namespace MyMod
{
    /// <summary>
    /// 修复原生池丢失 bug：
    ///
    /// 根因：mod 在场景早期注册了 sync 池（cachedPools.Count > 0），导致
    /// PoolManager.OnLevelLoaded 的 `if (cachedPools == null || cachedPools.Count == 0)`
    /// 判断为 false，跳过 InitPools()——希腊世界的原生池（Coin Indicator / Fish /
    /// Building Dust 等）从未加载 → 投币/购买/特效全部崩溃。
    ///
    /// 修复：Prefix 强制执行 InitPools()（反射调私有方法），随后重新注册 mod 的
    /// sync 池（InitPools 会 ClearStaticReferences 清掉它们），再跳过原版。
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

                // InitPools 清掉了 mod 注册的 sync 池，重新注册
                ReRegisterModPools(__instance);

                // 跳过原版 OnLevelLoaded（它的 DoPreload 会遍历 cachedPools，
                // 我们刚 InitPools 重建后 cachedPools 是原生池，让原版 DoPreload 执行也行——
                // 但为避免双重逻辑，这里让原版跑 DoPreload）
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Patch_PoolManager error: " + e.Message);
                return true;
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
