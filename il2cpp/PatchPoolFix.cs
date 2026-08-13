using System;
using UnityEngine;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 池修复（2026-08-13 Steam 2.4.0 实机诊断版，IL 反汇编核对后重写）：
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
/// 修复：SpawnGO prefix——learningMode=false 时 try CreatePoolFor 幂等兜底
/// （CreatePoolFor 对已存在池会因 Dictionary.Add 重复键抛异常 → catch = 已存在）。
/// 用托管 HashSet&lt;int&gt;（prefab.GetInstanceID()）缓存已兜底过的 prefab，
/// 避免每帧反射 Invoke。IL2CPP 下反射游戏私有静态字典不可靠，弃用。
///
/// IL2CPP 注意：SpawnGO 有默认参数（IL2CPP 生成完整 7 参重载），[HarmonyPatch]
/// 必须显式列出全部参数类型。
/// </summary>
public static class PatchPoolFix
{
    private static System.Reflection.MethodInfo _createPoolForMethod;
    private static readonly System.Collections.Generic.HashSet<int> _fallbackDone = new();

    public static void EnsurePoolViaInit(PoolManager pm)
    {
        if (pm == null) return;
        var initPoolsMethod = typeof(PoolManager).GetMethod("InitPools",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (initPoolsMethod == null)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[PoolFix] InitPools method not found via reflection!");
            return;
        }
        initPoolsMethod.Invoke(pm, null);
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[PoolFix] Force InitPools() executed (native pools rebuilt)");
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
        if (_fallbackDone.Contains(prefabId)) return;

        try
        {
            _createPoolForMethod.Invoke(pm, new object[] { prefab });
            _fallbackDone.Add(prefabId);
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[PoolFix] Created missing pool for " + prefab.name);
        }
        catch (Exception)
        {
            // 已存在（Dictionary.Add 重复键）或创建失败——标记避免反复尝试
            _fallbackDone.Add(prefabId);
        }
    }
}

[HarmonyPatch(typeof(PoolManager))]
public static class PoolManager_Init_Patch
{
    [HarmonyPatch(nameof(PoolManager.Init))]
    [HarmonyPrefix]
    public static void Init_Prefix(PoolManager __instance)
    {
        // 诊断日志：验证 patch 是否挂上
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[PoolFix] Init_Prefix ENTERED");
        if (!ModConfig.Enabled.Value) return;
        try
        {
            if (__instance == null) return;

            // 强制重建原生池（提前于读档恢复，修 Fish/Building Dust 等池缺失）
            PatchPoolFix.EnsurePoolViaInit(__instance);

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
    private static bool _logged;

    [HarmonyPatch(nameof(Pool.SpawnGO),
        new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Transform), typeof(bool), typeof(bool), typeof(bool) })]
    [HarmonyPrefix]
    public static void SpawnGO_Prefix(GameObject prefab)
    {
        if (!_logged)
        {
            _logged = true;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[PoolFix] SpawnGO_Prefix ENTERED first time");
        }
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

            // 池缺失兜底（try CreatePoolFor 幂等 + HashSet 缓存）
            PatchPoolFix.TryCreatePoolFallback(prefab);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}
