using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 英雄弓手金箭的「穿墙」例外（用户 2026-09-17 锁定的最简方案）：英雄 shot 作用域内射出的每支箭与
/// 当前世界的墙碰撞体互相忽略（<c>Physics2D.IgnoreCollision</c>），箭不再被城墙挡下，正常穿越去攻击
/// 墙外目标；敌人/地面/其它原生分支与伤害语义完全不变。**只动显示/碰撞对层面**：不改弹道、伤害、
/// 射速、池语义，不写墙，不销毁任何对象。
///
/// 挂点（复用 HeroArcherArrowVisuals 既有发射作用域，**不新增任何 Harmony 钩子**）：
///   * <see cref="Apply"/>：Arrow.OnEnable 后缀（Postfix Priority.Last）的英雄分支在「外观写入成功」
///     之后调用——资格门就是既有作用域（非英雄/功能关闭/溢出/外观资源不可用自然不进这个分支）。
///   * <see cref="Restore"/>：ResetArrow（Arrow.OnEnable 前缀 Priority.First）**无条件**调用，
///     在原生 OnEnable 之前把上一任英雄箭留下的无视对恢复成原生碰撞（关闭/非英雄复用的箭同样清理）。
///
/// 墙碰撞体缓存（本文件私有，不改 PatchShared_ScanCache）：
///   * <c>FindObjectsOfType&lt;Wall&gt;()</c>（仅活动墙）→ 每墙 <c>GetComponentsInChildren&lt;Collider2D&gt;(false)</c>
///     （仅活动碰撞体）展开为扁平列表（硬上限 <see cref="MaxWallColliders"/>，超出截断 = fail-closed）；
///   * TTL <see cref="CacheTtlSeconds"/> 秒 + world/layer/scene 换代失效（沿用 ArcherOptionsScope 的
///     world 上下文，与 HeroArcher* 其它 slice 的换代判定一致）；
///   * 空/查询失败 → fail-closed：本次不穿墙（保持原生碰撞），绝不抛异常；失败保留旧快照，
///     池复用的归还路径仍能到达上一批碰撞体。
///
/// 有界日志 <c>[HeroArcherWallPierce]</c>：每 world 换代最多 <see cref="MaxLogsPerWorld"/> 条
/// （换代扫描摘要 / 首次 apply 对数 / 截断 / 失败），沿用 Logged HashSet once 模式。
/// 主线程前提：所有入口都来自 Unity 主线程（OnEnable/ResetArrow 钩子），无锁。
///
/// 已知边界（均未实机验证，见任务回执）：IgnoreCollision 的引擎级效果（穿墙、墙的
/// OnCollisionEnter2D 不再触发）与池复用恢复的观感都要游戏内验证；被 SetActive(false) 但仍存活的墙
/// 不进入活动扫描，这种墙上的旧无视对可能在快照换代后无法再被归还命中（极窄）。
/// </summary>
internal static class HeroArcherWallPierce
{
    /// <summary>墙碰撞体快照 TTL（unscaled 秒）：窗口内复用同一份扫描结果，绝不每箭全场扫描。</summary>
    private const float CacheTtlSeconds = 5f;
    /// <summary>扁平碰撞体列表硬上限：超出即截断并记一次日志（fail-closed 方向 = 少穿墙，绝不无界增长）。</summary>
    private const int MaxWallColliders = 256;
    /// <summary>每个 world 换代最多日志条数（防刷屏；换代时重置预算与 once 键）。</summary>
    private const int MaxLogsPerWorld = 8;

    private static readonly List<Collider2D> WallColliders = new List<Collider2D>(64);
    private static readonly List<Collider2D> Scratch = new List<Collider2D>(64);
    private static readonly HashSet<string> Logged = new HashSet<string>();

    private static bool _cacheValid;
    private static float _cachedAt;
    private static IntPtr _cachedWorld;
    private static IntPtr _cachedLayer;
    private static int _cachedScene;
    private static int _logBudget;
    private static int _scanCount;

    // ---------- 只读观测（测试/诊断） ----------

    /// <summary>当前快照里的墙碰撞体数（无快照 = 0），测试与诊断用。</summary>
    internal static int CachedColliderCount => _cacheValid ? WallColliders.Count : 0;

    /// <summary>本进程的 FindObjectsOfType 扫描次数（测试用）。</summary>
    internal static int ScanCount => _scanCount;

    // ============================================================
    // 应用 / 归还（只从 HeroArcherArrowVisuals 的既有分支调用）
    // ============================================================

    /// <summary>
    /// 英雄 shot 内给这支箭挂上「无视全部活动墙碰撞体」：调用量 = 快照碰撞体数（≤ <see cref="MaxWallColliders"/>），
    /// 单对失败隔离（其余对照常）。拿不到箭碰撞体 / world 上下文不可用 / 快照不可用（空或查询失败）→
    /// fail-closed 直接返回（保持原生碰撞）。绝不抛异常进原生调用链。
    /// </summary>
    internal static void Apply(Arrow arrow)
    {
        try
        {
            Collider2D arrowCollider = ArrowCollider(arrow);
            if (arrowCollider == null) return;
            IntPtr world, layer;
            int scene;
            if (!TryGetWorldContext(out world, out layer, out scene)) return;
            if (!EnsureSnapshot(world, layer, scene)) return;
            int total = WallColliders.Count;
            if (total == 0) return;
            int pairs = 0;
            for (int i = 0; i < total; i++)
            {
                Collider2D wall = WallColliders[i];
                if (wall == null) continue;
                try
                {
                    Physics2D.IgnoreCollision(arrowCollider, wall, true);
                    pairs++;
                }
                catch (Exception)
                {
                    // 单个碰撞体失败隔离（销毁中的墙等）：其余对继续；下次池复用由 Restore 兜底。
                }
            }
            if (pairs > 0) LogOnce("apply", "hero arrow ignores " + pairs + "/" + total + " active wall collider(s) this shot");
        }
        catch (Exception e)
        {
            LogOnce("apply-failed", "wall pierce apply failed; arrow keeps native wall collisions: " + e.GetType().Name);
        }
    }

    /// <summary>
    /// 池复用/新生命归还（Arrow.OnEnable 前缀，先于原生 OnEnable 重置）：把可能写过的墙无视对恢复成
    /// 原生碰撞（false）。**无条件**（功能关闭/非英雄复用同样清理）、**从不重扫**（归还只处理可能已经
    /// 写过的旧对，宁多写一次 false 也不漏）；单对失败隔离。绝不触碰地面无视对（缓存里没有 GroundCollider）。
    /// </summary>
    internal static void Restore(Arrow arrow)
    {
        try
        {
            if (!_cacheValid) return;                              // 从未扫描过：没有任何可能写过的对
            Collider2D arrowCollider = ArrowCollider(arrow);
            if (arrowCollider == null) return;
            int total = WallColliders.Count;
            for (int i = 0; i < total; i++)
            {
                Collider2D wall = WallColliders[i];
                if (wall == null) continue;
                try { Physics2D.IgnoreCollision(arrowCollider, wall, false); }
                catch (Exception)
                {
                    // 单个失败隔离（销毁中的墙等）：其余对继续归还。
                }
            }
        }
        catch (Exception e)
        {
            LogOnce("restore-failed", "wall pierce restore failed; an arrow may keep ignoring a wall: " + e.GetType().Name);
        }
    }

    // ============================================================
    // 墙碰撞体快照（TTL + world 换代）
    // ============================================================

    /// <summary>快照新鲜度 = 同 world/layer/scene + TTL 未过期；过期/换代才重扫，且只有成功重扫才可用。</summary>
    private static bool EnsureSnapshot(IntPtr world, IntPtr layer, int scene)
    {
        if (_cacheValid && _cachedWorld == world && _cachedLayer == layer && _cachedScene == scene
            && Now() - _cachedAt <= CacheTtlSeconds) return true;
        return Rescan(world, layer, scene);
    }

    /// <summary>
    /// 扫描活动墙 → 展开活动墙碰撞体到 Scratch；成功才整体替换快照（失败保留旧快照给归还路径）。
    /// 单墙枚举失败只跳过该墙（该墙保持原生碰撞），不丢整份扫描。摘要/截断/失败各记一次（每换代）。
    /// </summary>
    private static bool Rescan(IntPtr world, IntPtr layer, int scene)
    {
        _scanCount++;
        try
        {
            Scratch.Clear();
            int truncated = 0;
            int wallFailures = 0;
            var walls = UnityEngine.Object.FindObjectsOfType<Wall>();
            int wallCount = walls != null ? walls.Length : 0;
            for (int i = 0; i < wallCount; i++)
            {
                Wall wall = walls[i];
                if (wall == null || wall.gameObject == null) continue;   // FindObjectsOfType 只回活动对象；防御半销毁
                try
                {
                    var colliders = wall.GetComponentsInChildren<Collider2D>(false);
                    if (colliders == null) continue;
                    for (int c = 0; c < colliders.Length; c++)
                    {
                        Collider2D collider = colliders[c];
                        if (collider == null) continue;
                        if (Scratch.Count >= MaxWallColliders) { truncated++; continue; }
                        Scratch.Add(collider);
                    }
                }
                catch (Exception)
                {
                    wallFailures++;                                       // 单墙失败：该墙保持原生碰撞
                }
            }

            bool newWorld = !_cacheValid || _cachedWorld != world || _cachedLayer != layer || _cachedScene != scene;
            if (newWorld)
            {
                Logged.Clear();                     // 换代：once 键与日志预算一起重置（每 world 有界）
                _logBudget = MaxLogsPerWorld;
            }
            WallColliders.Clear();
            WallColliders.AddRange(Scratch);
            _cachedWorld = world;
            _cachedLayer = layer;
            _cachedScene = scene;
            _cachedAt = Now();
            _cacheValid = true;
            LogOnce("scan", "wall snapshot: " + WallColliders.Count + " active collider(s) from " + wallCount + " active wall(s)");
            if (truncated > 0) LogOnce("truncated", "wall collider list capped at " + MaxWallColliders
                + "; " + truncated + " extra collider(s) keep native collisions");
            if (wallFailures > 0) LogOnce("wall-failed", wallFailures + " wall(s) could not be enumerated; they keep native collisions");
            return true;
        }
        catch (Exception e)
        {
            LogOnce("scan-failed", "wall scan failed; arrows keep native collisions until the next attempt: " + e.GetType().Name);
            return false;
        }
    }

    // ============================================================
    // 工具
    // ============================================================

    /// <summary>箭的原生碰撞体（Require.Component&lt;Collider2D&gt; 在 Awake 已挂好）；读不到 = fail-closed。</summary>
    private static Collider2D ArrowCollider(Arrow arrow)
    {
        try
        {
            if (arrow == null || arrow.gameObject == null) return null;
            return arrow._collider;
        }
        catch (Exception) { return null; }
    }

    /// <summary>当前 world 上下文（与 HeroArcher* 其它 slice 同一判定）；任何一步不确定 → false（fail-closed）。</summary>
    private static bool TryGetWorldContext(out IntPtr world, out IntPtr layer, out int scene)
    {
        world = IntPtr.Zero;
        layer = IntPtr.Zero;
        scene = 0;
        try
        {
            if (!ArcherOptionsScope.TryGetContext(out world, out layer, out scene)) return false;
            return world != IntPtr.Zero && layer != IntPtr.Zero;
        }
        catch (Exception) { return false; }
    }

    private static float Now()
    {
        try { return Time.unscaledTime; }
        catch (Exception) { return 0f; }
    }

    /// <summary>每 world 换代内的 once 日志（键在换代时清空），并受 <see cref="MaxLogsPerWorld"/> 预算约束。</summary>
    private static void LogOnce(string key, string message)
    {
        try
        {
            if (_logBudget <= 0) return;
            if (!Logged.Add(key)) return;
            _logBudget--;
            KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[HeroArcherWallPierce] " + message);
        }
        catch (Exception) { }
    }

    // ---------- 测试钩子（仅 internal；不做任何游戏写入、不销毁任何对象） ----------

    /// <summary>清空快照与日志预算，供测试用例之间隔离；无游戏写入。</summary>
    internal static void ResetForTests()
    {
        WallColliders.Clear();
        Scratch.Clear();
        Logged.Clear();
        _cacheValid = false;
        _cachedAt = 0f;
        _cachedWorld = IntPtr.Zero;
        _cachedLayer = IntPtr.Zero;
        _cachedScene = 0;
        _logBudget = 0;
        _scanCount = 0;
    }
}
