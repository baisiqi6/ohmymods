using System;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 附加希腊岛（物理 land11）原生入口适配。全部 target 已在 actual interop（arm64 2.4）反射核实存在，
/// 不存在的方法绝不挂载（PatchAll 对解析失败的 attribute 会拖垮整个插件）。
///
/// 配置/容量/主线：
/// - <c>BiomeHolder.GetConfigFromIndex(int)</c>：先过 exact current-holder 实例门，仅
///   "确证 scope + 当前 campaign11"接管；其余（错误 holder/其他 world/联机/挑战/campaign 非 11）原样透传。
/// - <c>IslandSaveData.UpdateFileProps()</c>：前缀清 ready 并抬 MAX；后缀确认 table-ready；
///   Finalizer 原样保留 __exception（失败保持未准备，允许后续有界重试）。
/// - 不挂 <c>IslandSaveData.GetFilePropsForLand(int,int,int)</c>：其 FileProps 返回（两个 Il2CppString
///   指针）在 ARM64 非 blittable struct-return Harmony 桥不安全；文件寻址 bootstrap 只经冷启动
///   UpdateFileProps 前缀与旅行前 <see cref="ExtensionIslandRuntime.EnsureReady"/> 显式确认，
///   不承诺插件晚热挂载后的读档场景。
/// - <c>Game.DefeatGreed(bool,float)</c>：附加岛场景级完成保险门。
///
/// 洞口入口隔离（仅 land11 生成/加载窗口内登记的那座右 Cliff 实例；原岛零改动）：
/// - <c>BombablePortal.Start</c> / <c>BombablePortal.ActivatePortal(Player)</c> 跳过；
/// - <c>SidedCaveData.get_CanBuildBomb</c> 返回 false（不给出付费资格）；
/// - <c>CliffPortalState.ChangeState(State,bool,bool,bool,bool)</c> 拒绝非 None 目标（None 原样）；
/// - <c>PayableBombPurchase.SpawnBomb</c> / <c>PayableForge.SpawnBomb</c> 两个直接造弹提交门；
/// - <c>Game.SetCurrentLand(int)</c> 后置清登记（指针复用防护）。
/// 不 hook Portal/Damageable/Persistent/PortalData/Crumble/rebuild 链。
///
/// 候选核对（native-terrain §8）：<c>Level.GetLevelBlocks()</c> 后置仅在 land11 生成窗口核对
/// Portal/BeachLeft/CliffRight 三块身份，缺失即中止本次生成。
/// </summary>
[HarmonyPatch(typeof(BiomeHolder), nameof(BiomeHolder.GetConfigFromIndex), new[] { typeof(int) })]
internal static class PatchExtensionIsland_ConfigGet
{
    /// <summary>仅接管返回 true 的场景；其余原样执行原生。</summary>
    [HarmonyPrefix]
    private static bool Prefix(BiomeHolder __instance, int configIndex, ref LevelConfig __result)
    {
        if (!ExtensionIslandRuntime.TryHandleConfigRequest(__instance, configIndex, out LevelConfig config))
            return true;
        __result = config;
        return false;
    }
}

/// <summary>
/// 文件容量与 table-ready（root 二审修正，单一 hook）：prefix 建立本次 refresh 身份（__state）、
/// 清 ready；postfix/finalizer 由同一 HarmonyX binder 注入 **readonly `bool __runOriginal`**
/// （actual 0Harmony 2.10.2 反编译事实见 runtime-followup-review.md:24：WritePostfixes 经
/// EmitCallParameter 的 variables.TryGetValue→Ldloc，WriteFinalizers 同 variables）——
/// postfix 为 false 时不记"已执行"，finalizer 复核 false 不确认；finalizer 是唯一收尾。
/// 原 exception 原样返回；不使用 Priority.Last 位置补丁。
/// </summary>
[HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.UpdateFileProps))]
internal static class PatchExtensionIsland_FilePropsRefresh
{
    /// <summary>prefix：身份 + 冻结（提升后 submitted MAX 在桥内记录）+ 清 ready；异常转 NotePrefixFailed。</summary>
    [HarmonyPrefix]
    private static void Prefix(out MountIslandFilePropsRefresh.RefreshCall __state)
    {
        ExtensionIslandRuntime.OnFilePropsRefreshBegin(out __state);
    }

    /// <summary>postfix：按 binder 注入的 __runOriginal 记录执行证据（false = 原方法被 skip）。</summary>
    [HarmonyPostfix]
    private static void Postfix(MountIslandFilePropsRefresh.RefreshCall __state, bool __runOriginal)
    {
        ExtensionIslandRuntime.OnFilePropsRefreshOriginalRun(__state, __runOriginal);
    }

    /// <summary>
    /// finalizer：唯一收尾，复核 readonly __runOriginal；未拥有/异常/skip/嵌套/容量不符一律不确认 ready。
    /// __state 缺失 = prefix 链未到本类 → 失效 ready。原异常原样保留。
    /// </summary>
    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception, MountIslandFilePropsRefresh.RefreshCall __state,
        bool __runOriginal)
    {
        if (__state == null)
            ExtensionIslandRuntime.OnFilePropsRefreshUnowned();
        else
            ExtensionIslandRuntime.OnFilePropsRefreshFinished(__state, __exception != null, __runOriginal);
        return __exception;   // 原样保留原生异常，不吞
    }
}

/// <summary>
/// 主线完成保险门：Greek 特判（<c>BiomeIndex==5</c>）可能让附加岛的洞口流程误报战役完成。
/// 只对确证 Greek 普通离线 + 当前场景 physical land11 跳过原生 <c>Game.DefeatGreed</c>；
/// 0..10、其他世界与挑战原样执行。Boat/SailAway 不受影响。
/// </summary>
[HarmonyPatch(typeof(Game), nameof(Game.DefeatGreed), new[] { typeof(bool), typeof(float) })]
internal static class PatchExtensionIsland_DefeatGreed
{
    [HarmonyPrefix]
    private static bool Prefix(Game __instance)
    {
        return !ExtensionIslandRuntime.ShouldBlockDefeatGreed(__instance);
    }
}

/// <summary>land11 受限洞口实例的 <c>BombablePortal.Start</c> 跳过（无洞内 BossHill 时不初始化）。</summary>
[HarmonyPatch(typeof(BombablePortal), nameof(BombablePortal.Start))]
internal static class PatchExtensionIsland_CavePortalStart
{
    [HarmonyPrefix]
    private static bool Prefix(BombablePortal __instance)
    {
        return !ExtensionIslandRuntime.ShouldSkipBombablePortalStart(__instance);
    }
}

/// <summary>
/// 登记点 1：<c>BombablePortal.Awake</c> 后置（保留原生初始化/缓存）——land11 生成/加载窗口内
/// 创建的实例登记为受限洞口。Start 晚于全部 Awake/OnEnable，因此 Start 前缀能看到登记。
/// </summary>
[HarmonyPatch(typeof(BombablePortal), nameof(BombablePortal.Awake))]
internal static class PatchExtensionIsland_CavePortalAwake
{
    [HarmonyPostfix]
    private static void Postfix(BombablePortal __instance)
    {
        ExtensionIslandRuntime.NoteBombablePortalAwake(__instance);
    }
}

/// <summary>
/// 登记点 2：<c>CliffPortalState.OnEnable</c> 后置（保留原生局部初始化）——与 Awake 双通道保证
/// ChangeState/CanBuildBomb 判定前实例已登记。
/// </summary>
[HarmonyPatch(typeof(CliffPortalState), nameof(CliffPortalState.OnEnable))]
internal static class PatchExtensionIsland_CaveStateEnabled
{
    [HarmonyPostfix]
    private static void Postfix(CliffPortalState __instance)
    {
        ExtensionIslandRuntime.NoteCaveStateEnabled(__instance);
    }
}

/// <summary>land11 受限洞口实例的 <c>ActivatePortal(Player)</c> 跳过（阻止入洞启动）。</summary>
[HarmonyPatch(typeof(BombablePortal), nameof(BombablePortal.ActivatePortal), new[] { typeof(Player) })]
internal static class PatchExtensionIsland_CavePortalActivate
{
    [HarmonyPrefix]
    private static bool Prefix(BombablePortal __instance)
    {
        return !ExtensionIslandRuntime.ShouldSkipBombablePortalActivate(__instance);
    }
}

/// <summary>受限洞口的 <c>SidedCaveData.CanBuildBomb</c> 恒 false（不改变付费链其他部分）。</summary>
[HarmonyPatch(typeof(SidedCaveData), "get_CanBuildBomb")]
internal static class PatchExtensionIsland_CaveCanBuildBomb
{
    [HarmonyPrefix]
    private static bool Prefix(SidedCaveData __instance, ref bool __result)
    {
        if (!ExtensionIslandRuntime.ShouldBlockCanBuildBomb(__instance)) return true;
        __result = false;
        return false;
    }
}

/// <summary>
/// 受限洞口的 <c>CliffPortalState.ChangeState</c>：None 原样（正常恢复），任何非 None 目标在副作用前拒绝。
/// </summary>
[HarmonyPatch(typeof(CliffPortalState), nameof(CliffPortalState.ChangeState),
    new[] { typeof(CliffPortalState.State), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
internal static class PatchExtensionIsland_CaveStateChange
{
    [HarmonyPrefix]
    private static bool Prefix(CliffPortalState __instance, CliffPortalState.State newState)
    {
        return !ExtensionIslandRuntime.ShouldBlockCaveStateChange(__instance, newState);
    }
}

/// <summary>提交门：land11 当前窗口直接调用 <c>PayableBombPurchase.SpawnBomb</c> 不产生炸弹。</summary>
[HarmonyPatch(typeof(PayableBombPurchase), nameof(PayableBombPurchase.SpawnBomb))]
internal static class PatchExtensionIsland_BombPurchaseSpawn
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !ExtensionIslandRuntime.ShouldBlockBombSpawn();
    }
}

/// <summary>提交门：land11 当前窗口直接调用 <c>PayableForge.SpawnBomb</c> 不产生炸弹。</summary>
[HarmonyPatch(typeof(PayableForge), nameof(PayableForge.SpawnBomb))]
internal static class PatchExtensionIsland_ForgeSpawn
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !ExtensionIslandRuntime.ShouldBlockBombSpawn();
    }
}

/// <summary>换岛后清受限洞口登记（指针复用防护；新窗口在实例创建时重新登记）。</summary>
[HarmonyPatch(typeof(Game), nameof(Game.SetCurrentLand), new[] { typeof(int) })]
internal static class PatchExtensionIsland_LandChanged
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        ExtensionIslandRuntime.OnCurrentLandChanged();
    }
}

/// <summary>native-terrain §8：land11 生成窗口核对必需 Greek 候选身份，缺失即中止本次生成。</summary>
[HarmonyPatch(typeof(Level), nameof(Level.GetLevelBlocks))]
internal static class PatchExtensionIsland_BlockCandidates
{
    [HarmonyPostfix]
    private static void Postfix(Level __instance, ref Il2CppSystem.Collections.Generic.List<LevelBlock> __result)
    {
        ExtensionIslandRuntime.VerifyExtensionCandidates(__instance, __result);
    }
}
