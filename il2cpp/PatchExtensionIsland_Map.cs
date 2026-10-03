using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 扩展岛地图层 Harmony 目标（真实 interop 签名，均由 ABI 探针 + Cecil 反射审计核验）：
    /// - <c>MapTimelineMenuGreece.LoadLands(Il2CppReferenceArray&lt;GameObject&gt;, GameObject)</c>
    /// - <c>MapTimelineMenuGreece.ClearLands()</c>
    /// - <c>MapTimelineMenuGreece.UpdateLands(ReignInfo)</c>
    /// - <c>UILand.UpdateLand(ReignInfo, int)</c>
    /// - <c>MapTimelineMenuGreece.Confirm(int)</c>
    /// 现 3 个 map hook 都在 MapMountIcons.cs（map-worker 负责），本文件是 travel 独立入口。
    /// </summary>
    [HarmonyPatch(typeof(MapTimelineMenuGreece), nameof(MapTimelineMenuGreece.LoadLands))]
    internal static class ExtensionIslandMap_LoadLands_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(MapTimelineMenuGreece __instance,
            Il2CppReferenceArray<GameObject> __0, GameObject __1)
        {
            ExtensionIslandMap.OnLandsLoaded(__instance, __0, __1);
        }
    }

    [HarmonyPatch(typeof(MapTimelineMenuGreece), nameof(MapTimelineMenuGreece.ClearLands))]
    internal static class ExtensionIslandMap_ClearLands_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(MapTimelineMenuGreece __instance)
        {
            ExtensionIslandMap.OnLandsCleared(__instance);
            MapMountIcons.OnMenuLandsCleared(__instance);   // world-overview-only：exact owner 展示组即刻中性化（唯一新增行）
        }
    }

    [HarmonyPatch(typeof(MapTimelineMenuGreece), nameof(MapTimelineMenuGreece.UpdateLands))]
    internal static class ExtensionIslandMap_UpdateLands_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(MapTimelineMenuGreece __instance)
        {
            ExtensionIslandMap.BeforeLandsUpdated(__instance);
        }
    }

    [HarmonyPatch(typeof(UILand), nameof(UILand.UpdateLand))]
    internal static class ExtensionIslandMap_UpdateLand_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(UILand __instance, ref int __1)
        {
            if (ExtensionIslandMap.TryRemapUpdateLand(__instance, __1, out int physical)) __1 = physical;
        }
    }

    [HarmonyPatch(typeof(MapTimelineMenuGreece), nameof(MapTimelineMenuGreece.Confirm))]
    internal static class ExtensionIslandMap_Confirm_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(MapTimelineMenuGreece __instance, int __0)
        {
            return !ExtensionIslandMap.TryHandleConfirm(__instance, __0);
        }
    }
}
