using System;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 跨世界坐骑生成事务 scope：<c>Level.GenerateInternal(LevelConfig, int seed)</c> 前缀建立本 frame
/// （owner/目标 Level/授予状态），后缀结算、Finalizer 只终止自己的 frame——
/// 正常完成的内层 frame 不受外层异常影响，内层异常也不清外层（与 MapWidthScope 的 frame/幂等
/// close 约定一致，但状态由 __state 传递，不依赖全局深度）。
/// </summary>
[HarmonyPatch(typeof(Level), "GenerateInternal")]
internal static class PatchRide_CrossWorldMount_Scope
{
    [HarmonyPrefix]
    private static void Prefix(Level __instance, LevelConfig __0, int __1, out CrossWorldMountRuntime.Frame __state)
    {
        // root 统一入口：GetBlocks 之前捕获 Level/config/seed 并单次认领 fresh 票据。
        __state = CrossWorldMountRuntime.Open(__instance, __0, __1);
    }

    [HarmonyPostfix]
    private static void Postfix(Level __instance, CrossWorldMountRuntime.Frame __state)
    {
        CrossWorldMountRuntime.Close(__state, __instance);
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception, CrossWorldMountRuntime.Frame __state)
    {
        CrossWorldMountRuntime.Abort(__state);
        return __exception;
    }
}

/// <summary>
/// 放置观察点：<c>LevelBlock.CloneInto(Level, float)</c> 后缀。本 frame 注入的实例被克隆进
/// <c>level</c> 时记录该 Level 指针；提交时要求 == 本次 GenerateInternal 的目标 Level，
/// 因此"别的 Level 放了同名对象"不构成放置证据。
/// </summary>
[HarmonyPatch(typeof(LevelBlock), nameof(LevelBlock.CloneInto))]
internal static class PatchRide_CrossWorldMount_BlockPlaced
{
    [HarmonyPostfix]
    private static void Postfix(LevelBlock __instance, Level level)
    {
        CrossWorldMountRuntime.NoteBlockPlaced(__instance, level);
    }
}

/// <summary>
/// 唯一注入点：<c>LevelLayout.GetBlocks()</c> 后缀。
///
/// 顺序契约（审查必改 5）：本 postfix 使用 <c>Priority.Last</c>，在 HarmonyX 中 postfix 按
/// 优先级从高到低执行 —— 本补丁因此**在所有其他 GetBlocks postfix（含 MapWidthScope）之后**运行，
/// 地图倍率的基线永远不含本地块，本地块是倍率规划完成后的纯追加。组合回归见
/// tests/cross-world-mounts（OrderContract 断言 + adapter 组合反例）。
/// </summary>
[HarmonyPatch(typeof(LevelLayout), "GetBlocks")]
internal static class PatchRide_CrossWorldMount_GetBlocks
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref Il2CppSystem.Collections.Generic.List<LevelBlock> __result)
    {
        CrossWorldMountRuntime.TryApply(ref __result);
    }
}

/// <summary>
/// 依赖生命周期（契约 2/3）：
/// - <c>BiomeSpecificAssets.InitializeAssets</c> 后缀：原生刚重建映射表，强制重验外来 SteedType
///   （同实例重初始化也会清掉我们写入的条目，必须回来补）。
/// - <c>BiomeSpecificAssets.GetSteedByType</c> 前缀：首个查询/读档恢复之前保证映射与技能池存在；
///   全部就绪后热路径只做状态/管理器指针比较，不进登记/补池逻辑。
/// - <c>PoolManager.InitPools</c> 后缀：原生池缓存刚重建，按原生池定义强制重验同步池。
/// 三个入口都只受模组总开关门（不受"坐骑功能 OFF"影响，OFF 后已拥有的外来坐骑仍要能恢复）；
/// 逐个入口自身幂等、失败在下一次初始化点重试。
/// </summary>
[HarmonyPatch(typeof(BiomeSpecificAssets), nameof(BiomeSpecificAssets.InitializeAssets))]
internal static class PatchRide_CrossWorldMount_AssetsInit
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        CrossWorldMountDependencies.OnAssetsInitialized();
    }
}

[HarmonyPatch(typeof(BiomeSpecificAssets), nameof(BiomeSpecificAssets.GetSteedByType))]
internal static class PatchRide_CrossWorldMount_SteedQuery
{
    [HarmonyPrefix]
    private static void Prefix(BiomeSpecificAssets __instance)
    {
        CrossWorldMountDependencies.BeforeQuery(__instance);
    }

    /// <summary>
    /// 变体覆盖的结果适配（Kirin type6 / Wolf type13/17）：前缀保证依赖就绪后，后缀在
    /// 严格 scope（当前希腊离线普通战役 + 本战役已授予 + 当前 assets 实例）内把原生结果替换为
    /// 本定义变体；其余情况原样。位置参数 <c>__0</c> = 请求 SteedType（不依赖 interop 参数名）。
    /// </summary>
    [HarmonyPostfix]
    private static void Postfix(BiomeSpecificAssets __instance, SteedType __0, ref Steed __result)
    {
        Steed resolved = CrossWorldMountDependencies.ResolveQueryResult(__instance, (int)__0, __result);
        if (!ReferenceEquals(resolved, __result)) __result = resolved;
    }
}

/// <summary>
/// 变体获取点的生成路由（Kirin）：<c>SteedSpawn.SpawnSteed(Steed steedPrefab)</c> 前缀
/// （actual 签名 <c>System.Void SpawnSteed(Steed)</c>，VA 0x909650）。购买与设施恢复后的
/// <c>SpawnSteeds</c> 遍历共用该唯一入口；<see cref="CrossWorldMountDependencies.RouteSpawnSteed"/>
/// 只对"设施名全等已授予定义的获取设施 + 请求类型属于该定义 + 请求 prefab 不是本变体"替换，
/// 其余原样。不修改共享模板、steedPool 数组或任何原生字段。位置参数 <c>__0</c> = steedPrefab。
/// </summary>
[HarmonyPatch(typeof(SteedSpawn), nameof(SteedSpawn.SpawnSteed))]
internal static class PatchRide_CrossWorldMount_SpawnSteed
{
    [HarmonyPrefix]
    private static void Prefix(SteedSpawn __instance, ref Steed __0)
    {
        Steed resolved = CrossWorldMountDependencies.RouteSpawnSteed(__instance, __0);
        if (!ReferenceEquals(resolved, __0)) __0 = resolved;
    }
}

/// <summary>
/// R2 修复：<c>SteedSpawn.Pay()</c>（actual 同步入口 <c>System.Void Pay()</c> @0x90a080）在
/// <c>useSpawnAnimation=1</c> 时直接 <c>Instantiate(steeds[0])</c> 生成"备用体"
/// （saveBackupHackInstance，forceBlockPayment），不经 SpawnSteed 前缀；动画窗口保存会写出
/// Unicorn prefabPath（Persistent87388 persistInactive=1 → OnDisable 不注销）。
/// Prefix 把该设施 steeds 临时换成逐元素 RouteSpawnSteed 路由后的私有副本；Postfix 与
/// Finalizer 共用幂等归还，异常原样抛出。不改共享模板/steedPool/价格/资格/原生付款状态；
/// 后续真正出生仍走 SpawnSteed 前缀（已是正确 prefab 不重复处理）。
/// </summary>
[HarmonyPatch(typeof(SteedSpawn), nameof(SteedSpawn.Pay))]
internal static class PatchRide_CrossWorldMount_Pay
{
    [HarmonyPrefix]
    private static void Prefix(SteedSpawn __instance, out CrossWorldMountDependencies.PayRouteScope __state)
    {
        __state = CrossWorldMountDependencies.BeginPayRoute(__instance);
    }

    [HarmonyPostfix]
    private static void Postfix(CrossWorldMountDependencies.PayRouteScope __state)
    {
        CrossWorldMountDependencies.RestorePayRoute(__state);
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception, CrossWorldMountDependencies.PayRouteScope __state)
    {
        CrossWorldMountDependencies.RestorePayRoute(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(PoolManager), "InitPools")]
internal static class PatchRide_CrossWorldMount_PoolInit
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        CrossWorldMountDependencies.OnPoolsRebuilt();
    }
}
