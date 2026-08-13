using System;
using UnityEngine;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 池修复（Steam 2.4.0 实机诊断 + SteamFixReviewer 终审修正版）：
///
/// 实机证据：读档进希腊世界后 Player.log 刷 90 万行 "Pool not found for Fish" +
/// Boat.Update NRE（每帧）→ 严重掉帧。根因两层：
///
/// 第一层：mod 在场景早期注册 sync 池（PatchRoles_Castle 的 EnsurePoolFor*）→
/// 2.4.0 池初始化被跳过（与 Mono 2.1.0 同款 bug）。修复：prefix PoolManager.Init
/// （OnLevelLoaded 是 Cpp2IL 生成 thunk，HarmonyX 挂不上原生虚调用——IL 核实）
/// 强制 InitPools()（反射私有方法）+ 重注册 mod sync 池。
///
/// 第二层：读档恢复（TryPopObjectsToScene）的 SpawnGO 在池未建时返回 null → NRE。
/// 修复：SpawnGO prefix——learningMode=false 时 CreatePoolFor 兜底 + 注册进
/// PoolManager 三缓存（cachedPools/cachedNamePoolPairs/cachedSyncIdPoolPairs，
/// sync=false——原生池语义；mod sync 池有 EnsurePoolFor* 专门路径）。
///
/// 【Reviewer 修正】：
/// - 幂等机制：2.1.0 Pool.Init 用 ContainsKey guard 不抛异常（反编译核实）——
///   "try CreatePoolFor 重复抛异常判已存在"错误，重复调用会静默建孤儿池 GO。
///   改用实例ID计数：成功 1 次、失败 >3 次本场景放弃；Init_Prefix 每场景清空。
/// - 兜底池必须入三缓存，否则 SpawnObjectFromNetwork（联机）在 cachedPools 找不到，
///   与本地 poolsByPrefab 双池分裂。
/// - 双 InitPools（原生 Init 也建）的孤儿池残留为已知权衡（场景卸载清理）。
///
/// IL2CPP 注意：SpawnGO 有默认参数（IL2CPP 生成完整 7 参重载），[HarmonyPatch]
/// 必须显式列出全部参数类型。
/// </summary>
public static class PatchPoolFix
{
    private static System.Reflection.FieldInfo _poolsByPrefabField;
    private static System.Reflection.MethodInfo _createPoolForMethod;

    // prefab.GetInstanceID() → 兜底尝试次数（0 = 已成功）。每场景（Init 强制）清空。
    private static readonly System.Collections.Generic.Dictionary<int, int> _fallbackAttempts = new();
    private const int MAX_ATTEMPTS = 3;

    public static bool HasPool(GameObject prefab)
    {
        if (_poolsByPrefabField == null)
        {
            _poolsByPrefabField = typeof(Pool).GetField("poolsByPrefab",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        }
        if (_poolsByPrefabField == null) return true;  // 拿不到就放行原逻辑
        var dict = _poolsByPrefabField.GetValue(null) as System.Collections.Generic.Dictionary<GameObject, Pool>;
        if (dict == null) return true;
        return dict.ContainsKey(prefab);
    }

    public static void TryCreatePoolFallback(GameObject prefab)
    {
        var managers = Managers.Inst;
        if (managers == null) return;
        var pm = managers.pools;
        if (pm == null) return;

        if (_createPoolForMethod == null)
        {
            _createPoolForMethod = typeof(PoolManager).GetMethod("CreatePoolFor",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        }
        if (_createPoolForMethod == null) return;

        int prefabId = prefab.GetInstanceID();
        if (_fallbackAttempts.TryGetValue(prefabId, out int attempts) && (attempts >= MAX_ATTEMPTS || attempts < 0))
        {
            return;  // 已成功（<0）或本场景失败超限
        }

        try
        {
            Pool pool = _createPoolForMethod.Invoke(pm, new object[] { prefab }) as Pool;
            if (pool == null)
            {
                _fallbackAttempts[prefabId] = attempts + 1;
                return;
            }

            // 注册进三缓存（sync=false，原生池语义；mod sync 池走 EnsurePoolFor* 路径）
            if (pm.cachedPools != null && !pm.cachedPools.Contains(pool)) pm.cachedPools.Add(pool);
            if (pm.cachedNamePoolPairs != null && !pm.cachedNamePoolPairs.ContainsKey(pool.prefab.name))
                pm.cachedNamePoolPairs.Add(pool.prefab.name, pool);
            if (pool.sync && pool.syncID != 0 && pm.cachedSyncIdPoolPairs != null
                && !pm.cachedSyncIdPoolPairs.ContainsKey((int)pool.syncID))
            {
                pm.cachedSyncIdPoolPairs.Add((int)pool.syncID, pool);
            }

            _fallbackAttempts[prefabId] = -1;  // 成功标记（负值）
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[PoolFix] Created missing pool for " + prefab.name);
        }
        catch (Exception)
        {
            _fallbackAttempts[prefabId] = attempts + 1;  // 失败计数，超限本场景放弃
        }
    }

    /// <summary>Init 强制重建后调用：清空本场景兜底计数。</summary>
    public static void ResetFallbackState()
    {
        _fallbackAttempts.Clear();
    }
}

[HarmonyPatch(typeof(PoolManager))]
public static class PoolManager_Init_Patch
{
    [HarmonyPatch(nameof(PoolManager.Init))]
    [HarmonyPrefix]
    public static void Init_Prefix(PoolManager __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            if (__instance == null) return;

            // 强制重建原生池（提前于读档恢复，修 Fish/Building Dust 等池缺失）。
            // 双建（原生 Init 也建）孤儿池残留为已知权衡：场景卸载清理。
            var initPoolsMethod = typeof(PoolManager).GetMethod("InitPools",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (initPoolsMethod == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError("[PoolFix] InitPools method not found via reflection!");
                return;
            }
            initPoolsMethod.Invoke(__instance, null);
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[PoolFix] Force InitPools() executed (native pools rebuilt)");

            // 新场景：清空兜底计数
            PatchPoolFix.ResetFallbackState();

            // InitPools 清掉了 mod 注册的 sync 池，重新注册（幂等）
            PatchRoles_Castle.ReRegisterModPools();
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(Pool))]
public static class Pool_SpawnGO_Fallback_Patch
{
    [HarmonyPatch(nameof(Pool.SpawnGO),
        new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Transform), typeof(bool), typeof(bool), typeof(bool) })]
    [HarmonyPrefix]
    public static void SpawnGO_Prefix(GameObject prefab)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            if (prefab == null) return;
            var managers = Managers.Inst;
            if (managers == null) return;
            var pm = managers.pools;
            if (pm == null) return;

            // learningMode：原版 SpawnGO 自己会 CreatePoolFor，无需兜底
            if (pm.learningMode) return;

            // 池缺失兜底（计数限次 + 三缓存注册）
            if (!PatchPoolFix.HasPool(prefab))
            {
                PatchPoolFix.TryCreatePoolFallback(prefab);
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}
