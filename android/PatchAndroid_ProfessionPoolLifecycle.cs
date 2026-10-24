using System;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// Android：native PoolManager.InitPools 正常返回是本 PM“池定义已创建”的唯一凭证。
/// prefix 只撤销上一份授权；postfix 只在原始方法正常返回后保存当前 PM，并按
/// CanRegisterCustomPools 交给各 writer 幂等登记。World.OnLevelLoaded 仅消费，不授权。
/// 不建立 scene-ready/finalizer/retry 队列；inactive 时仍记录定义事实，由写入门禁止
/// 向 teardown 追加池。原始方法抛错时 HarmonyX 不执行 postfix，
/// bf102 的外层 catch 无法把失败变成授权。
/// </summary>
[HarmonyPatch(typeof(PoolManager), nameof(PoolManager.InitPools))]
internal static class AndroidProfessionPoolLifecycle
{
    private static PoolManager _nativeDefinitionsOwner;

    [HarmonyPrefix]
    private static void InitPoolsPrefix()
    {
        _nativeDefinitionsOwner = null;
    }

    [HarmonyPostfix]
    private static void InitPoolsPostfix(PoolManager __instance)
    {
        if (__instance == null) return;
        _nativeDefinitionsOwner = __instance;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[AndroidPoolLifecycle] native InitPools returned pm=0x"
                + __instance.Pointer.ToString("X")
                + " count=" + (__instance.cachedPools != null ? __instance.cachedPools.Count : -1)
                + " active=" + __instance.isActiveAndEnabled);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[AndroidPoolLifecycle] return observation failed: " + e);
        }
        RegisterPreparedPools(__instance);
    }

    /// <summary>
    /// 唯一写入门：owner 必须是当前 PM 的同一 Unity 实例、仍是 Managers.Inst.pools，
    /// 且组件 active。任何一项不成立都 fail-closed，避免销毁后 pointer 复用或 teardown 写入。
    /// </summary>
    internal static bool CanRegisterCustomPools(PoolManager pm)
    {
        try
        {
            if (!ModConfig.Enabled.Value) return false;
            PoolManager owner = _nativeDefinitionsOwner;
            if (owner == null || pm == null) return false;
            if (owner != pm || !pm.isActiveAndEnabled) return false;

            Managers managers = Managers.Inst;
            return managers != null && managers.pools != null && managers.pools == pm;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// World.OnLevelLoaded 与 InitPools 正常返回共用；不做 Count/Playing/资源猜测。
    /// </summary>
    internal static void RegisterPreparedPools(PoolManager pm)
    {
        if (!CanRegisterCustomPools(pm)) return;
        try
        {
            PatchRoles_NorseSquad.EnsureNorseArcherPool();
            PatchRoles_Crossbowman.EnsureBoltPoolRegistered();
            PatchEconomy_BankAssistants.EnsurePreparedPools(pm);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[AndroidPoolLifecycle] register failed: " + e);
        }
    }
}

/// <summary>
/// 已有 World.OnLevelLoaded 边界只消费 native 定义事实；不为 PM 新增 callee detour。
/// </summary>
[HarmonyPatch(typeof(World), nameof(World.OnLevelLoaded))]
internal static class AndroidProfessionPoolActivation
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        Managers managers = Managers.Inst;
        if (managers != null)
            AndroidProfessionPoolLifecycle.RegisterPreparedPools(managers.pools);
    }
}
