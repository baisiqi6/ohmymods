using System;
using System.Collections;
using System.Text;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 墙基（Wall0 / tag "WallFoundation"）原生机制诊断（Issue #125 审查 P0-1 取证轮）。
///
/// 纯只读：不实例化、不注册、不写任何游戏状态；只产生日志。取证目标：
/// a) 外档墙基是否随边界扩张动态生成；生成与 OnBordersChanged 的先后；
///    MoveBorders/MoveBorderOnSide postfix 返回时新基底是否已同步存在
///    （日志 tag 前缀对照：move-all/move-onSide vs border-event+N+0.5s）。
/// b) 各档基底 GetHeaderFromObject 的注册实态（静态起始档 vs 动态外档的
///    持久化语义差异）。
/// c) Wall0 模板与活实例的 ScatteredObject 避障标签/MinSpacing/GetHalfWidth、
///    Payable 交互半径（playerPayDistance/offset）、Persistent/FixedTransform
///    组件在场情况。
/// d) 每次关卡加载与每次边界事件后的墙基快照（名字|x|y|header），含
///    Wall/WallWreck/ScaffoldingWall 计数。
///
/// 输出纪律：快照按内容指纹去重——内容不变的事件只留一行 no-change；
/// 变化才输出全量快照行。诊断轮结束后本文件收敛为墙基功能的常驻观测。
/// </summary>
public static class PatchWorld_WallSpotDiagnostics
{
    private const float LoadDelaySeconds = 5f;
    private const float PostEventDelaySeconds = 0.5f;

    private static Kingdom _subscribedKingdom;
    private static Il2CppSystem.Action _borderHandler;
    private static IntPtr _snapshotWorld;
    private static string _lastKey;
    private static float _lastScanTime = -1f;
    private static bool _eventCheckPending;
    private static bool _loggedTemplate;
    private static int _moveCalls;
    private static int _borderEvents;

    /// <summary>World.OnLevelLoaded postfix 入口：延迟协程（等场景/holder 就绪）。</summary>
    public static void Schedule(World world)
    {
        try
        {
            if (world == null || world.gameObject == null) return;
            world.StartCoroutine(DiagRoutine(world).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[WallSpotDiag] schedule failed: " + e);
        }
    }

    private static IEnumerator DiagRoutine(World world)
    {
        yield return new WaitForSeconds(LoadDelaySeconds);
        try
        {
            Run(world);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[WallSpotDiag] " + e);
        }
    }

    private static void Run(World world)
    {
        Managers managers = Managers.Inst;
        if (managers == null || managers.world == null || managers.kingdom == null
            || managers.world.Pointer != world.Pointer || world.gameLayer == null
            || world.gameObject == null || !world.gameObject.activeInHierarchy)
            return;

        if (_snapshotWorld != world.Pointer)
        {
            _snapshotWorld = world.Pointer;
            _lastKey = null; // 新世界：重新输出基线快照
            _eventCheckPending = false; // 场景切换复位（防协程被销毁后卡死，复审#2 P2-9）
        }

        BindKingdom(managers.kingdom);
        LogTemplateMetadataOnce(managers, world.gameLayer);
        LogSnapshot(world, "level-load+5s");
    }

    /// <summary>
    /// OnBordersChanged 订阅生命周期：Kingdom 是 per-scene 实例，换岛/读档换对象；
    /// 重绑前先对旧实例 remove（CoinCourierShop 纪律）。旧实例可能已销毁，
    /// remove 失败只记日志（诊断轮容忍；正式功能沿同一纪律加固）。
    /// </summary>
    private static void BindKingdom(Kingdom kingdom)
    {
        try
        {
            if (kingdom == null || kingdom.gameObject == null) return;
            if (_subscribedKingdom != null
                && _subscribedKingdom.Pointer == kingdom.Pointer) return;

            if (_subscribedKingdom != null && _borderHandler != null)
            {
                try { _subscribedKingdom.remove_OnBordersChanged(_borderHandler); }
                catch (Exception e)
                {
                    KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                        "[WallSpotDiag] old kingdom unsubscribe failed (scene torn down?): " + e.Message);
                }
            }

            _borderHandler = (Il2CppSystem.Action)OnBordersChangedFired;
            kingdom.add_OnBordersChanged(_borderHandler);
            _subscribedKingdom = kingdom;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[WallSpotDiag] OnBordersChanged subscribed (kingdom=" + kingdom.Pointer + ")");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[WallSpotDiag] subscribe failed: " + e.Message);
        }
    }

    private static void OnBordersChangedFired()
    {
        try
        {
            if (ModConfig.WallSpotDiagnostics == null
                || !ModConfig.WallSpotDiagnostics.Value) return;
            _borderEvents++;
            Managers managers = Managers.Inst;
            World world = managers != null ? managers.world : null;
            if (world == null || world.gameObject == null
                || !world.gameObject.activeInHierarchy) return;
            if (_eventCheckPending) return; // 已有待检：合并
            _eventCheckPending = true;
            world.StartCoroutine(PostEventCheck(world).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[WallSpotDiag] border event handler failed: " + e.Message);
        }
    }

    private static IEnumerator PostEventCheck(World world)
    {
        yield return new WaitForSeconds(PostEventDelaySeconds);
        _eventCheckPending = false;
        try
        {
            if (Managers.Inst == null || Managers.Inst.world == null
                || Managers.Inst.world.Pointer != world.Pointer) yield break;
            LogSnapshot(world, "border-event#" + _borderEvents + "+" + PostEventDelaySeconds + "s");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[WallSpotDiag] " + e.Message);
        }
    }

    /// <summary>MoveBorders/MoveBorderOnSide postfix：同步时刻快照（生成点取证）。</summary>
    internal static void LogMove(string kind)
    {
        try
        {
            if (ModConfig.WallSpotDiagnostics == null
                || !ModConfig.WallSpotDiagnostics.Value) return;
            _moveCalls++;
            Managers managers = Managers.Inst;
            World world = managers != null ? managers.world : null;
            if (world == null || world.gameLayer == null) return;
            LogSnapshot(world, "move-" + kind + "#" + _moveCalls);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[WallSpotDiag] " + e.Message);
        }
    }

    /// <summary>
    /// 墙基快照（内容指纹去重）。只列 gameLayer 子树、非船的活跃 WallFoundation；
    /// 变化才输出，未变化输出单行 no-change（事件时间线仍可对照）。
    /// </summary>
    private static void LogSnapshot(World world, string tag)
    {
        Transform layer = world != null ? world.gameLayer : null;
        if (layer == null) return;

        // 扫描节流：MoveBorders/MoveBorderOnSide 在边界动画期可能高频触发，
        // 0.25s 内不重复全场景 tag 扫描（诊断输出靠指纹去重，时序靠首扫）。
        // level-load 基线与 border-event 事件后检查豁免（证据完整性优先）。
        bool bypassThrottle = tag.StartsWith("level-load") || tag.StartsWith("border-event");
        float now = Time.unscaledTime;
        if (!bypassThrottle && now - _lastScanTime < 0.25f) return;
        _lastScanTime = now;

        var tagged = GameObject.FindGameObjectsWithTag("WallFoundation");
        var lines = new ListWrapper();
        if (tagged != null)
        {
            for (int i = 0; i < tagged.Length; i++)
            {
                GameObject go = tagged[i];
                if (go == null || go.transform == null || !go.transform.IsChildOf(layer)) continue;
                if (IsOnBoat(go.transform)) continue;
                string header = HeaderOf(go);
                lines.Add(go.name + "@x" + go.transform.position.x.ToString("F1")
                    + "/y" + go.transform.position.y.ToString("F1")
                    + "/h:" + header);
            }
        }
        lines.Sort();

        string key = string.Join("|", lines.Items);
        if (key == _lastKey)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[WallSpotDiag] " + tag + " no-change (foundations=" + lines.Count + ")");
            return;
        }
        _lastKey = key;

        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[WallSpotDiag] snapshot " + tag
            + " foundations=" + lines.Count
            + " wall=" + CountTag("Wall", layer)
            + " wreck=" + CountTag("WallWreck", layer)
            + " scaffoldWall=" + CountTag("ScaffoldingWall", layer)
            + " :: " + (key.Length == 0 ? "<none>" : key));
    }

    private static string HeaderOf(GameObject go)
    {
        try
        {
            if (NetworkPostbox.Instance == null) return "no-postbox";
            CRPCHeader header = NetworkPostbox.Instance.GetHeaderFromObject(go, true);
            return header != null ? header.HeaderType.ToString() : "null";
        }
        catch (Exception e)
        {
            return "err:" + e.GetType().Name;
        }
    }

    private static int CountTag(string tag, Transform layer)
    {
        try
        {
            var found = GameObject.FindGameObjectsWithTag(tag);
            int count = 0;
            if (found == null) return 0;
            for (int i = 0; i < found.Length; i++)
            {
                GameObject go = found[i];
                if (go == null || go.transform == null || !go.transform.IsChildOf(layer)) continue;
                if (IsOnBoat(go.transform)) continue;
                count++;
            }
            return count;
        }
        catch { return -1; }
    }

    /// <summary>
    /// 模板与首个活原生基底的元数据（一次）：ScatteredObject 避障/间距/半宽、
    /// Payable 交互半径、持久化组件在场。
    /// </summary>
    private static void LogTemplateMetadataOnce(Managers managers, Transform layer)
    {
        if (_loggedTemplate) return;
        _loggedTemplate = true;
        try
        {
            Holder holder = managers != null ? managers.holder : null;
            GameObject template = holder != null ? holder.wallLocationPrefab : null;
            try
            {
                GameObject swapped = template != null
                    ? BiomeData.GetAssetSwap<GameObject>(template) : null;
                if (swapped != null) template = swapped;
            }
            catch { }

            var sb = new StringBuilder("[WallSpotDiag] template");
            sb.Append(" prefab=").Append(template != null ? template.name : "<null>");
            int wallPrefabs = holder != null && holder.wallPrefabs != null
                ? holder.wallPrefabs.Length : -1;
            sb.Append(" wallPrefabs=").Append(wallPrefabs);
            if (template != null) DescribeObject(sb, template, "prefab");

            GameObject live = FindFirstFoundation(layer);
            if (live != null) DescribeObject(sb, live, "live");

            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(sb.ToString());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[WallSpotDiag] template metadata unavailable: " + e.Message);
        }
    }

    private static void DescribeObject(StringBuilder sb, GameObject go, string label)
    {
        sb.Append(" | ").Append(label).Append('[');
        try
        {
            ScatteredObject scatter = go.GetComponent<ScatteredObject>();
            if (scatter == null) sb.Append("no-scatter");
            else
            {
                sb.Append("avoid=");
                if (scatter.AvoidOverlapWith == null) sb.Append("null");
                else
                {
                    for (int i = 0; i < scatter.AvoidOverlapWith.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(scatter.AvoidOverlapWith[i]);
                    }
                }
                sb.Append(" minSpacing=").Append(scatter.MinSpacing.ToString("F2"));
                sb.Append(" usePayable=").Append(scatter.UsePayableForSpacing);
                try { sb.Append(" halfWidth=").Append(scatter.GetHalfWidth().ToString("F2")); }
                catch { sb.Append(" halfWidth=?"); }
            }
        }
        catch (Exception e) { sb.Append(" scatter-err:").Append(e.GetType().Name); }

        try
        {
            Payable payable = go.GetComponent<Payable>();
            if (payable == null) sb.Append(" no-payable");
            else
            {
                sb.Append(" payDist=").Append(payable.playerPayDistance.ToString("F2"));
                sb.Append(" payOff=(").Append(payable.playerPayPointOffset.x.ToString("F2"))
                  .Append(',').Append(payable.playerPayPointOffset.y.ToString("F2")).Append(')');
            }
        }
        catch (Exception e) { sb.Append(" payable-err:").Append(e.GetType().Name); }

        try
        {
            sb.Append(" persistent=").Append(go.GetComponent<Persistent>() != null);
            sb.Append(" fixedTransform=").Append(go.GetComponent<FixedTransform>() != null);
            sb.Append(" payableUpgrade=").Append(go.GetComponent<PayableUpgrade>() != null);
        }
        catch (Exception e) { sb.Append(" comp-err:").Append(e.GetType().Name); }
        sb.Append(']');
    }

    private static GameObject FindFirstFoundation(Transform layer)
    {
        var tagged = GameObject.FindGameObjectsWithTag("WallFoundation");
        if (tagged == null) return null;
        for (int i = 0; i < tagged.Length; i++)
        {
            GameObject go = tagged[i];
            if (go == null || go.transform == null || !go.transform.IsChildOf(layer)) continue;
            if (IsOnBoat(go.transform)) continue;
            return go;
        }
        return null;
    }

    /// <summary>祖先名含 "Boat" 判定（PatchWorld_TowerSpots 同款）。</summary>
    private static bool IsOnBoat(Transform t)
    {
        try
        {
            Transform walker = t.parent;
            for (int depth = 0; depth < 4 && walker != null; depth++)
            {
                string n = walker.name;
                if (n != null && n.Contains("Boat")) return true;
                walker = walker.parent;
            }
        }
        catch { }
        return false;
    }

    /// <summary>小工具：可排序的字符串收集（避免再引 System.Collections.Generic 之外的类型歧义）。</summary>
    private sealed class ListWrapper
    {
        public readonly System.Collections.Generic.List<string> Items =
            new System.Collections.Generic.List<string>();
        public int Count => Items.Count;
        public void Add(string s) => Items.Add(s);
        public void Sort() => Items.Sort(StringComparer.Ordinal);
    }
}

/// <summary>World.OnLevelLoaded postfix：调度诊断协程。</summary>
[HarmonyPatch(typeof(World), nameof(World.OnLevelLoaded))]
public static class World_WallSpotDiag_Host_Patch
{
    [HarmonyPostfix]
    private static void Postfix(World __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        PatchWorld_WallSpotDiagnostics.Schedule(__instance);
    }
}

/// <summary>私有长方法 postfix（生成点同步性取证；非 Dispose 类短方法，无地址折叠问题）。</summary>
[HarmonyPatch(typeof(Kingdom), "MoveBorders")]
public static class Kingdom_WallSpotDiag_MoveBorders_Patch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (!ModConfig.Enabled.Value) return;
        PatchWorld_WallSpotDiagnostics.LogMove("all");
    }
}

[HarmonyPatch(typeof(Kingdom), "MoveBorderOnSide")]
public static class Kingdom_WallSpotDiag_MoveOnSide_Patch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (!ModConfig.Enabled.Value) return;
        PatchWorld_WallSpotDiagnostics.LogMove("onSide");
    }
}
