using System;
using HarmonyLib;
#if ANDROID
// Android（Il2CppInterop namespace-prefix 模式）把 Assembly-CSharp 的全局类型放在 Il2Cpp.* 下；
// 本文件与 PC 共用同一份逻辑，仅在此把文件用到的游戏类型显式映射到实际 interop 类型，
// 其余代码（含 PC 分支）逐字相同。UnityEngine/Il2CppSystem 类型两个平台同名，无需映射。
using Forest = Il2Cpp.Forest;
using ForestItem = Il2Cpp.ForestItem;
#endif
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 快速森林退缩 FastForestRecede（ModConfig.FastForestRecedeEnabled，两平台默认关闭）。
///   开启：ForestItem.FadeAndRemove 的 Prefix 把等待时间缩到 1/3（delay &gt; 0 时 delay / 3，
///   否则 removeDelay * Random(0.5, 1.5) / 3）。原生把 delay 交给 StartCoroutine 的淡出协程，
///   本 patch 只改本次调用的传参：不改全局时间，不碰原生淡出、Destroy、森林边界更新链，也不改
///   removeDelay 字段，不 hook 协程 factory 或 MoveNext。
///   范围：只对非 controlsForestSize、且 _forest 属于当前 world 的 gameLayer、item 与当前
///   gameLayer 同 scene 的实例生效。视差背景里的 ForestItem 可能不是 gameLayer 子孙，因此只校验
///   _forest 归属与 item 所在 scene，不以 item 自身的 IsChildOf(gameLayer) 判断。
///   边界（保留既有 source 语义）：prefix 发生在原生权威/inactive 早退之前，被拒绝或 inactive
///   的调用可能比纯原生多消耗一次 RNG；预计算结果非有限/非正时不提交 ref，交回原生后 native
///   else 分支会再掷一次 Random——0 参数路径的已知风险，不新增守卫、扫描或补偿。
///   故障：访问失败只发一次 warning（单 bool，一生一次）并保留本次原生等待参数；本文件不写
///   native 字段、不重建协程、不建扫描/缓存/镜像/重试。
/// root 契约（只依赖，不在此实现）：ModConfig.FastForestRecedeEnabled 为布尔配置项；
/// OptionalQoLScope.IsActive 由 root 决定可用世界；KingdomEnhancedPlugin.Instance.LogSource 提供日志。
/// 联机边界：只改本机调用的等待参数，删除权威与核心链路全部原生；手机/联机实测另记。
/// </summary>
internal static class PatchWorld_FastForestRecede
{
    /// <summary>原生森林退缩等待时间的除数：3 即等待缩到 1/3。用户若要 1/2 或 1/5 只改这里。</summary>
    internal const float ForestRecedeMultiplier = 3f;

    /// <summary>访问失败只提示一次：一生一次，日志自身不外抛。</summary>
    private static bool _warned;

    /// <summary>配置与可用世界门：默认关闭时先于一切 item/native 访问返回。</summary>
    private static bool FastRecedeRequested()
    {
        try
        {
            var entry = ModConfig.FastForestRecedeEnabled;
            return entry != null && entry.Value && OptionalQoLScope.IsActive;
        }
        catch (Exception error)
        {
            WarnOnce(error);
            return false;
        }
    }

    /// <summary>
    /// ForestItem.FadeAndRemove prefix：只缩放本次调用的等待时间，其余原生链路原样保留。
    /// </summary>
    internal static void ScaleForestRecedeDelay(ForestItem item, ref float delay)
    {
        try
        {
            if (!FastRecedeRequested()) return;
            if (item == null || item.controlsForestSize || item.removedByForest) return;
            if (!ForestBelongsToCurrentWorld(item)) return;
            float baseDelay = delay > 0f ? delay : item.removeDelay * UnityEngine.Random.Range(0.5f, 1.5f);
            if (!float.IsFinite(baseDelay) || !(baseDelay > 0f)) return;
            float scaled = baseDelay / ForestRecedeMultiplier;
            if (!float.IsFinite(scaled) || !(scaled > 0f)) return;
            delay = scaled;
        }
        catch (Exception error)
        {
            WarnOnce(error);
        }
    }

    private static bool ForestBelongsToCurrentWorld(ForestItem item)
    {
        World world = CurrentWorld();
        if (world == null || world.gameLayer == null) return false;
        Forest forest = item._forest;
        if (forest == null || forest.gameObject == null) return false;
        if (!OptionalQoLScope.IsCurrent(forest)) return false;
        GameObject layer = world.gameLayer.gameObject;
        GameObject itemObject = item.gameObject;
        if (layer == null || itemObject == null) return false;
        // 视差背景里的 item 可能不是 gameLayer 子孙，因此只要求同一场景。
        return itemObject.scene.handle == layer.scene.handle
            && forest.gameObject.scene.handle == layer.scene.handle;
    }

    private static World CurrentWorld()
    {
        try
        {
            Managers managers = Managers.Inst;
            return managers != null ? managers.world : null;
        }
        catch (Exception error)
        {
            WarnOnce(error);
            return null;
        }
    }

    /// <summary>
    /// 访问失败的单一提示边界：本次保留原生等待参数（不写 ref、不改 native 状态），只提示一次，
    /// 日志自身失败也不外抛、不重试。
    /// </summary>
    private static void WarnOnce(Exception error)
    {
        if (_warned) return;
        _warned = true;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[FastForestRecede] 森林退缩 prefix 访问失败，本次保留原生等待参数：" + error.GetType().Name);
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(ForestItem), nameof(ForestItem.FadeAndRemove))]
internal static class ForestItem_FadeAndRemove_OptionalVegetation_Patch
{
    [HarmonyPrefix]
    internal static void Prefix(ForestItem __instance, ref float delay)
        => PatchWorld_FastForestRecede.ScaleForestRecedeDelay(__instance, ref delay);
}
