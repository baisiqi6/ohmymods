using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 共享扫描缓存（抖动治理）：多套监督协程各自独立 FindObjectsOfType——
/// DefenseSpacing（3s：Archer+Knight）、KnightStyle（5s IntegrityPass：Knight；
/// StyleFollowersByLookup：Archer）、Crossbowman（5s IntegrityPass：
/// CrossbowmanMarker）、SerpentLeash（10s：WorldEatingSerpent）——巡检拍同帧
/// 叠加时一场多次全场景扫描，实测 avg 6.7-7.0ms 但 max 37-56ms（顿挫）。
/// 本缓存把每类扫描统一为"每 maxAge 窗口至多真扫一次"：
/// - 过期（Time.time 距上次真扫 &gt; maxAgeSec）才重扫，未过期直接返回缓存数组；
/// - 帧内节流天然成立：同一帧内 Time.time 不变，即使 maxAgeSec=0（强制刷新）
///   的重复调用也只真扫一次，其余全部命中缓存；
/// - 多消费者（如 DefenseSpacing 与 KnightStyle 的 Archer/Knight 扫描）在窗口
///   内共享同一份数组，同帧叠加退化为一次。
///
/// 保守原则（行为语义零变化）：返回的永远是某次真实 FindObjectsOfType 的结果，
/// 只是"扫描年龄"最大等于各消费者原来的巡检节奏（Archer/Knight 3s、marker 5s、
/// 蛇 10s——新鲜度等价于原节奏，核查结论见各消费点注释）。数组按引用共享：
/// 所有消费方只读迭代（逐项判 null/activeInHierarchy），无人改写数组内容；
/// 池游戏 despawn=SetActive(false)（对象仍在缓存里但 activeInHierarchy=false，
/// 各消费方的既有判活检查自然跳过），真 Destroy 的残骸由各 pass 的 try/catch
/// 兜底，最多记一条日志，下次过期重扫自愈。
///
/// 线程前提（不加锁）：所有消费者都是 World 协程 / Harmony postfix（主线程）；
/// Time.time 与 FindObjectsOfType 本就是 main-thread-only API，跨线程调用在
/// 前提上即不可能——新增消费方必须同样在主线程调用。
///
/// 世界边界：各消费方的 SupervisorRoutine 重启（新世界 OnLevelLoaded）时调用
/// InvalidateAll()——新世界首轮 pass 必须拿到全新扫描（读档恢复路径依赖首轮
/// 看到全部存量单位；KnightStyle 的 5s 首拍尤其如此）。
///
/// 写路径失效钩子（预留，暂不接线）：InvalidateArchers() 等供"大量增删对象后
/// 需要立刻看到"的写路径调用（如 Crossbowman.RecomputeOnLoad 批量增删 marker
/// 后可调 InvalidateCrossbowmanMarkers()）。当前核查结论：所有消费点的新鲜度
/// 要求都与原巡检节奏等价（转职/换皮的即时性由 Promote / ConvertToSoldier /
/// ConvertToHunter / OnEnable 的 postfix 事件路径保证，慢速 pass 只是兜底），
/// 无需强制失效，故不接线。
///
/// CrossbowmanMarker 特例：FindObjectsOfType 要求该类型已 ClassInjector 注册，
/// 消费方（Crossbowman.IntegrityPass）在调用 Get 前先 EnsureMarkerRegistered()
/// ——与其他直接调用点的既有约定一致。
/// </summary>
internal static class UnitScanCache
{
    // ---- 每类型：缓存数组 + 上次真扫的 Time.time ----
    // 初始 NegativeInfinity 保证首次 Get 必扫；maxAgeSec=0 时同帧重复 Get
    // 因 Time.time 不变（差值 0 <= 0）也命中缓存，帧内节流成立。
    private static Archer[] _archers = Array.Empty<Archer>();
    private static float _archersScannedAt = float.NegativeInfinity;
    private static Knight[] _knights = Array.Empty<Knight>();
    private static float _knightsScannedAt = float.NegativeInfinity;
    private static CrossbowmanMarker[] _crossbowmanMarkers = Array.Empty<CrossbowmanMarker>();
    private static float _crossbowmanMarkersScannedAt = float.NegativeInfinity;
    private static WorldEatingSerpent[] _serpents = Array.Empty<WorldEatingSerpent>();
    private static float _serpentsScannedAt = float.NegativeInfinity;

    private static bool _loggedActive;

    /// <summary>
    /// 场上全体 Archer（默认 3s 窗口：DefenseSpacing 3s 拍与 KnightStyle 5s 拍
    /// 共用一份）。消费者：DefenseSpacing.DepthClampPass（深度钳制/列队诊断/
    /// 夜间纠偏/白天散开）、KnightStyle.StyleFollowersByLookup（随从联动）。
    /// </summary>
    internal static Archer[] GetArchers(float maxAgeSec = 3f)
    {
        float now = Time.time;
        if (now - _archersScannedAt <= maxAgeSec) return _archers;
        _archers = UnityEngine.Object.FindObjectsOfType<Archer>();
        _archersScannedAt = now;
        LogActiveOnce();
        return _archers;
    }

    /// <summary>
    /// 场上全体 Knight（默认 3s 窗口：DefenseSpacing 3s 拍与 KnightStyle 5s 拍
    /// 共用一份）。消费者：DefenseSpacing.ScanKnights（诊断/rank 压缩）、
    /// KnightStyle.IntegrityPass（上风格/重断言兜底）。
    /// </summary>
    internal static Knight[] GetKnights(float maxAgeSec = 3f)
    {
        float now = Time.time;
        if (now - _knightsScannedAt <= maxAgeSec) return _knights;
        _knights = UnityEngine.Object.FindObjectsOfType<Knight>();
        _knightsScannedAt = now;
        LogActiveOnce();
        return _knights;
    }

    /// <summary>
    /// 场上全体 CrossbowmanMarker（默认 5s 窗口=原巡检节奏）。消费者：
    /// Crossbowman.IntegrityPass（战斗包完整性兜底）。调用前须已
    /// EnsureMarkerRegistered()（消费方既有约定，见类注释特例）。
    /// </summary>
    internal static CrossbowmanMarker[] GetCrossbowmanMarkers(float maxAgeSec = 5f)
    {
        float now = Time.time;
        if (now - _crossbowmanMarkersScannedAt <= maxAgeSec) return _crossbowmanMarkers;
        _crossbowmanMarkers = UnityEngine.Object.FindObjectsOfType<CrossbowmanMarker>();
        _crossbowmanMarkersScannedAt = now;
        LogActiveOnce();
        return _crossbowmanMarkers;
    }

    /// <summary>
    /// 场上全体 WorldEatingSerpent（默认 10s 窗口=原复扫节奏；最终岛最多一条）。
    /// 消费者：SerpentLeash.SupervisorRoutine（锚点复推）。
    /// </summary>
    internal static WorldEatingSerpent[] GetSerpents(float maxAgeSec = 10f)
    {
        float now = Time.time;
        if (now - _serpentsScannedAt <= maxAgeSec) return _serpents;
        _serpents = UnityEngine.Object.FindObjectsOfType<WorldEatingSerpent>();
        _serpentsScannedAt = now;
        LogActiveOnce();
        return _serpents;
    }

    // ---- 写路径失效钩子（预留，暂不接线，见类注释）----

    /// <summary>作废 Archer 缓存（下次 Get 强制重扫）。预留：大量增删 Archer 后调用。</summary>
    internal static void InvalidateArchers()
    {
        _archers = Array.Empty<Archer>();
        _archersScannedAt = float.NegativeInfinity;
    }

    /// <summary>作废 Knight 缓存（下次 Get 强制重扫）。预留：大量增删 Knight 后调用。</summary>
    internal static void InvalidateKnights()
    {
        _knights = Array.Empty<Knight>();
        _knightsScannedAt = float.NegativeInfinity;
    }

    /// <summary>
    /// 作废 CrossbowmanMarker 缓存（下次 Get 强制重扫）。预留：RecomputeOnLoad
    /// 批量 Apply/Strip marker 后可选调用——当前不接（marker 增删后 ≤5s 内
    /// 自然重扫即可，巡检本就是兜底节奏）。
    /// </summary>
    internal static void InvalidateCrossbowmanMarkers()
    {
        _crossbowmanMarkers = Array.Empty<CrossbowmanMarker>();
        _crossbowmanMarkersScannedAt = float.NegativeInfinity;
    }

    /// <summary>作废 WorldEatingSerpent 缓存（下次 Get 强制重扫）。预留。</summary>
    internal static void InvalidateSerpents()
    {
        _serpents = Array.Empty<WorldEatingSerpent>();
        _serpentsScannedAt = float.NegativeInfinity;
    }

    /// <summary>
    /// 全类型失效（世界边界）：四个消费方的 SupervisorRoutine 重启时调用——
    /// 新世界（岛跳/新战役）首轮 pass 必须拿到全新扫描，杜绝跨世界残影。
    /// 幂等，重复调用无害。
    /// </summary>
    internal static void InvalidateAll()
    {
        InvalidateArchers();
        InvalidateKnights();
        InvalidateCrossbowmanMarkers();
        InvalidateSerpents();
    }

    private static void LogActiveOnce()
    {
        if (_loggedActive) return;
        _loggedActive = true;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[ScanCache] shared scans active");
    }
}
