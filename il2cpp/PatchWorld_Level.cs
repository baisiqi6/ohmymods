using System;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 新生成岛实际长度倍率（ModConfig.MapSizeMultiplier，issue-77）。
/// 原生布局只在 GenerateInternal 内生成一次：本补丁仅建立同步调用 scope 并捕获倍率快照
/// （前缀打开、后缀关闭、Finalizer 归还；支持嵌套）。实际宽度扩展由 LevelLayout.GetBlocks
/// 后缀在本 scope 内完成一次（MapWidthScope / MapWidthPlanner）。
///
/// 不再改写 LevelConfig.minLevelWidth（旧临时阈值乘法已删除）；不新增 GetWidth/TotalWidth
/// detour，不重写原生 GenerateInternal，不以 Level.isGenerating 作为门。
/// Finalizer 必须原样返回传入的 __exception：Harmony 语义下返回 null 会吞掉原生异常，
/// 返回同一实例则异常继续抛出（清理只归还 scope，绝不替换生成异常）。
/// 2.4.0 签名：Level.GenerateInternal(LevelConfig config, int seed)（private，按名挂载）；
/// LevelLayout.GetBlocks() 返回本次 layout 的独立 List（改它不动 layout/shared 账本）。
/// </summary>
[HarmonyPatch(typeof(Level), "GenerateInternal")]
internal static class PatchWorld_Level
{
    [HarmonyPrefix]
    private static void Prefix(out MapWidthScope.Frame __state)
    {
        MapWidthScope.Open(ModConfig.Enabled.Value, ModConfig.MapSizeMultiplier.Value, out __state);
    }

    [HarmonyPostfix]
    private static void Postfix(Level __instance, MapWidthScope.Frame __state)
    {
        MapWidthScope.Close(__instance, __state);
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception, MapWidthScope.Frame __state)
    {
        MapWidthScope.Abort(__state);
        return __exception;
    }
}

/// <summary>
/// GetBlocks 返回本次布局的独立 List；只在 GenerateInternal scope 内、每个 scope 首次调用时
/// 做一次规划替换。scope 之外 / 倍率 1 / MOD 关闭时原样返回，零副作用。
/// </summary>
[HarmonyPatch(typeof(LevelLayout), "GetBlocks")]
internal static class PatchWorld_Level_GetBlocks
{
    [HarmonyPostfix]
    private static void Postfix(LevelLayout __instance, ref Il2CppSystem.Collections.Generic.List<LevelBlock> __result)
    {
        MapWidthScope.TryApply(__instance, ref __result);
    }
}
