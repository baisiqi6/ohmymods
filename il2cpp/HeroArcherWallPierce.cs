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
///     返回本次接管的账本，由视觉回执持有（谁接管谁归还，同一本账）。
///   * 归还：池复用/功能关闭/换世界/owner 否决/巡检/Clear 全部汇进 **HeroArcherArrowVisuals 的单一
///     回执缝合点**（TryRestore → <see cref="RestoreLedger"/>）：外观归还完成 **且** 碰撞账本清空，
///     回执才退休；任一未完成 → 保留回执 + 退避重试。<see cref="Restore"/> 是同一账本的直接入口
///     （按箭身份归还，供 ResetArrow 前缀兜底与测试/工具直调），绝不重扫、绝不 blanket 写 false。
///
/// 碰撞账本（本文件私有，逐箭有界；替代旧「按全局快照 blanket 归还 false」的写法，本次修复核心）：
///   * 只接管原值 <c>GetIgnoreCollision == false</c> 的 pair：写 true **前**先记账（native 写成功后才抛
///     异常也不丢责任），本来已是 true 的 pair **不碰不记**（不是本模块接管的，绝不改回 false）。
///   * 同一本账**只对应一个实际 collider**：每次 Apply 重验箭头碰撞体的 native 指针与账本记录一致；
///     不一致/读不到 → 拒新写、保旧账（旧账只归还它自己写过的那个对象，绝不把新 collider 记进旧账）。
///   * 记下第一笔之后的任何外层异常（含某个墙的原生 null 检查抛出）都把这份已有责任**交回调用方**
///     （视觉回执持有），绝不在全局表里留下 Tick/Clear 够不到的孤儿账。
///   * 记账内容 = 实际写过的 Collider2D 引用（不是快照位置/下标）→ TTL 换代、单墙枚举失败、快照整体
///     替换都不会让旧 pair 失去归还路径；重复 Apply 幂等（已记账不重写、不清账，只补新墙/重试未确认项）。
///   * 读取异常 = 未知：不写、不认领，只留「待重探」责任（<see cref="RetryPending"/> 退避重试，不扫描
///     世界）；归还写失败 = 保留账本项重试。只有**明确销毁/停用**（false-null / 所在 GO 停用 = 碰撞体
///     已离开物理世界、Unity 随之清除 ignore 状态）才视作该项已由引擎清除。
///   * 硬上限：每箭 ≤ <see cref="MaxWallColliders"/> 对、全局 ≤ <see cref="MaxLedgerArrows"/> 箭；
///     表满 = **拒绝新 Apply**（一个 pair 都不写），绝不驱逐仍有未归还 pair 的账本。
///
/// 墙碰撞体快照（**只负责发现墙**，绝不承担归还责任）：<c>FindObjectsOfType&lt;Wall&gt;()</c>（仅活动墙）
/// → 每墙 <c>GetComponentsInChildren&lt;Collider2D&gt;(false)</c>（仅活动碰撞体）展开扁平列表
/// （硬上限 <see cref="MaxWallColliders"/>，超出截断 = fail-closed）；TTL <see cref="CacheTtlSeconds"/> 秒
/// + world/layer/scene 换代失效（沿用 ArcherOptionsScope 的 world 上下文，与 HeroArcher* 其它 slice 一致）；
/// 空/查询失败 → fail-closed：本次不穿墙（保持原生碰撞），绝不抛异常；失败保留旧快照。
///
/// 有界日志 <c>[HeroArcherWallPierce]</c>：每 world 换代最多 <see cref="MaxLogsPerWorld"/> 条
/// （换代扫描摘要 / apply 对数 / 截断 / 失败 / 表满），沿用 Logged HashSet once 模式。
/// 主线程前提：所有入口都来自 Unity 主线程（OnEnable/Tick 钩子），无锁。
///
/// 已知边界（均未实机验证，见任务回执）：IgnoreCollision 的引擎级效果（穿墙、墙的
/// OnCollisionEnter2D 不再触发）与池复用恢复的观感都要游戏内验证；Unity 在碰撞体随对象停用/销毁时
/// **直接清除** ignore 状态，这种复位不经本模块、无法观测，本文件按「明确停用/销毁 = 已清除」处理。
/// </summary>
internal static class HeroArcherWallPierce
{
    // ---------- 常量（全部硬上限） ----------

    /// <summary>墙碰撞体快照 TTL（unscaled 秒）：窗口内复用同一份扫描结果，绝不每箭全场扫描。</summary>
    private const float CacheTtlSeconds = 5f;
    /// <summary>扁平碰撞体列表硬上限：超出即截断并记一次日志（fail-closed 方向 = 少穿墙，绝不无界增长）。</summary>
    private const int MaxWallColliders = 256;
    /// <summary>每 world 换代最多日志条数（防刷屏；换代时重置预算与 once 键）。</summary>
    private const int MaxLogsPerWorld = 8;
    /// <summary>全局账本上限（= 视觉回执容量）：满 = 拒绝新 Apply，绝不驱逐仍有未归还 pair 的账本。</summary>
    internal const int MaxLedgerArrows = HeroArcherArrowVisuals.Capacity;
    /// <summary>账本项读/写失败后的重试间隔（unscaled 秒；与外观归还同一节奏）。</summary>
    private const float RetrySeconds = 0.5f;

    // ---------- 状态 ----------

    private static readonly List<Collider2D> WallColliders = new List<Collider2D>(64);
    private static readonly List<Collider2D> Scratch = new List<Collider2D>(64);
    private static readonly HashSet<string> Logged = new HashSet<string>();
    /// <summary>逐箭账本表：只作身份索引用（账本本身挂在视觉回执上，两者同生共死）。</summary>
    private static readonly PierceLedger[] Ledgers = new PierceLedger[MaxLedgerArrows];

    private static bool _cacheValid;
    private static float _cachedAt;
    private static IntPtr _cachedWorld;
    private static IntPtr _cachedLayer;
    private static int _cachedScene;
    private static int _logBudget;
    private static int _scanCount;
    private static int _liveLedgers;

    // ---------- 只读观测（测试/诊断） ----------

    /// <summary>当前快照里的墙碰撞体数（无快照 = 0），测试与诊断用。</summary>
    internal static int CachedColliderCount => _cacheValid ? WallColliders.Count : 0;

    /// <summary>本进程的 FindObjectsOfType 扫描次数（测试用）。</summary>
    internal static int ScanCount => _scanCount;

    /// <summary>在案（未退休）的箭账本数（测试用）。</summary>
    internal static int LedgerCount => _liveLedgers;

    // ============================================================
    // 逐箭碰撞账本
    // ============================================================

    /// <summary>
    /// 一支箭的墙碰撞账本（有界 ≤ <see cref="MaxWallColliders"/> 对）：只记本模块真正接管/尝试接管的
    /// (箭碰撞体, 墙碰撞体) 对与每个对的写状态。由视觉回执持有，归还走回执的单一缝合点。
    /// </summary>
    internal sealed class PierceLedger
    {
        /// <summary>true 写入成功：必须归还 false。</summary>
        internal const byte StateOwned = 0;
        /// <summary>true 写入抛异常（native 侧可能已成功）：保留责任，重写或归还。</summary>
        internal const byte StateUnwritten = 1;
        /// <summary>GetIgnoreCollision 读取异常（未知，未写）：只重探，不认领。</summary>
        internal const byte StateProbe = 2;

        internal IntPtr GoPtr;
        internal int GoId;
        internal IntPtr ArrowPtr;
        internal Collider2D ArrowCollider;                  // 实际写过的箭碰撞体（身份，不随快照换代）
        internal readonly Collider2D[] Walls = new Collider2D[MaxWallColliders];
        internal readonly byte[] States = new byte[MaxWallColliders];
        internal int Count;
        internal int PendingCount;                          // StateUnwritten + StateProbe 条数（O(1) 判定待重试）
        internal bool Retired;                              // 无未归还责任：表槽已释放
        internal float NextRetry;

        internal bool Pending => PendingCount > 0;

        internal int IndexOf(Collider2D wall)
        {
            for (int i = 0; i < Count; i++) if (ReferenceEquals(Walls[i], wall)) return i;
            return -1;
        }

        /// <summary>读取异常的对：不写、不认领，只留待重探责任；false = 每箭上限已满（调用方 fail-closed 跳过）。</summary>
        internal bool AddProbe(Collider2D wall)
        {
            if (Count >= MaxWallColliders) return false;
            Walls[Count] = wall;
            States[Count] = StateProbe;
            Count++;
            PendingCount++;
            return true;
        }

        /// <summary>先记账后写；返回下标（-1 = 每箭上限已满，调用方按 fail-closed 跳过该墙）。</summary>
        internal int AddUnwritten(Collider2D wall)
        {
            if (Count >= MaxWallColliders) return -1;
            Walls[Count] = wall;
            States[Count] = StateUnwritten;
            Count++;
            PendingCount++;
            return Count - 1;
        }

        internal void MarkWritten(int index)
        {
            if (States[index] == StateOwned) return;
            States[index] = StateOwned;
            PendingCount--;
        }

        /// <summary>探测成功读到「原值 false」：责任从「待重探」转为「待重写」（仍在待重试计数内）。</summary>
        internal void MarkUnwritten(int index)
        {
            if (States[index] == StateProbe) States[index] = StateUnwritten;
        }

        /// <summary>换尾删除（下标顺序无语义）：归还成功 / 引擎已清除 / 确认非本模块接管 时调用。</summary>
        internal void RemoveAt(int index)
        {
            if (States[index] != StateOwned) PendingCount--;
            Count--;
            if (index != Count)
            {
                Walls[index] = Walls[Count];
                States[index] = States[Count];
            }
            Walls[Count] = null;
        }
    }

    // ============================================================
    // 应用 / 归还
    // ============================================================

    /// <summary>
    /// 英雄 shot 内给这支箭挂上「无视活动墙碰撞体」：只接管原值 false 的 pair 并逐个记账，原值已是 true
    /// 的 pair 一律不碰（不是本模块的，绝不归还成 false）。每箭重复调用幂等：已记账的 pair 不重复写、
    /// 不清账，只补记新出现的墙并重试未确认项。返回本箭账本（表满/无身份/快照不可用 = null，
    /// 此时**不写任何 pair**），供视觉回执持有。绝不抛异常进原生调用链。
    /// </summary>
    internal static PierceLedger Apply(Arrow arrow)
    {
        IntPtr arrowPtr = IntPtr.Zero;
        IntPtr goPtr = IntPtr.Zero;
        int goId = 0;
        try
        {
            if (arrow == null) return null;
            try { arrowPtr = arrow.Pointer; }
            catch (Exception) { arrowPtr = IntPtr.Zero; }
            if (TryGoIdentity(arrow.gameObject, out IntPtr go, out int id)) { goPtr = go; goId = id; }
        }
        catch (Exception) { arrowPtr = IntPtr.Zero; goPtr = IntPtr.Zero; goId = 0; }
        return ApplyCore(arrow, goPtr, goId, arrowPtr);
    }

    /// <summary>视觉回执自己的身份版本（GO 指针 + InstanceID + Arrow 指针）：与回执记账同一套身份，不重复读。</summary>
    internal static PierceLedger Apply(Arrow arrow, IntPtr goPtr, int goId, IntPtr arrowPtr)
    {
        return ApplyCore(arrow, goPtr, goId, arrowPtr);
    }

    private static PierceLedger ApplyCore(Arrow arrow, IntPtr goPtr, int goId, IntPtr arrowPtr)
    {
        // 账本在 try 外：一旦记下第一笔责任，之后任何外层异常（例如某个墙的原生 null 检查抛出）都必须把
        // 这份已有责任交回调用方（回执持有）——绝不在全局表里留下 Tick/Clear 够不到的孤儿账。
        PierceLedger ledger = null;
        try
        {
            Collider2D arrowCollider = ArrowCollider(arrow);
            if (arrowCollider == null) return null;
            if (goPtr == IntPtr.Zero && arrowPtr == IntPtr.Zero) return null;  // 无身份 = 无法承担归还责任

            ledger = FindLedger(goPtr, goId, arrowPtr);
            if (ledger != null && !SameColliderIdentity(ledger.ArrowCollider, arrowCollider))
            {
                // 同一本账必须对应同一个实际 collider：池复用换过 collider = 换了归还目标。
                // 拒新写、保旧账——旧账只归还它自己写过的那个对象，绝不把新 collider 记进旧账。
                LogOnce("collider-changed", "the arrow collider changed or is unreadable; the ledger keeps its own hand-back target and refuses new writes");
                return ledger;
            }

            IntPtr world, layer;
            int scene;
            if (!TryGetWorldContext(out world, out layer, out scene)) return ledger;
            if (!EnsureSnapshot(world, layer, scene)) return ledger;            // 扫描失败：本次不穿墙；已有责任照旧交回

            int total = WallColliders.Count;
            int pairs = 0;
            for (int i = 0; i < total; i++)
            {
                Collider2D wall = WallColliders[i];
                if (wall == null) continue;
                if (ledger != null && ledger.IndexOf(wall) >= 0) continue;     // 已记账：幂等，重试交给 RetryPending

                bool ignored;
                try { ignored = Physics2D.GetIgnoreCollision(arrowCollider, wall); }
                catch (Exception)
                {
                    // 读取异常 = 未知：不写、不认领，只留待重探责任（有界重试）。
                    if (ledger == null) ledger = RentLedger(goPtr, goId, arrowPtr, arrowCollider);
                    if (ledger == null) { LogOnce("ledger-full", LedgerFullMessage()); return null; }
                    if (!ledger.AddProbe(wall)) LogOnce("pairs-full", PairsFullMessage());
                    continue;
                }
                if (ignored) continue;                                         // 原本 true：不是本模块接管的，绝不碰

                if (ledger == null) ledger = RentLedger(goPtr, goId, arrowPtr, arrowCollider);
                if (ledger == null) { LogOnce("ledger-full", LedgerFullMessage()); return null; }
                int index = ledger.AddUnwritten(wall);                         // 先记账后写：半写不丢账
                if (index < 0)
                {
                    LogOnce("pairs-full", PairsFullMessage());
                    continue;
                }
                try
                {
                    Physics2D.IgnoreCollision(arrowCollider, wall, true);
                    ledger.MarkWritten(index);
                    pairs++;
                }
                catch (Exception)
                {
                    // native 写可能已经成功：状态停在 Unwritten，稍后重写或归还（绝不当作没发生）。
                }
            }
            if (ledger != null && ledger.Count == 0) RetireLedger(ledger);     // 没记下任何责任：释放表槽
            if (pairs > 0) LogOnce("apply", "hero arrow ignores " + pairs + "/" + total + " active wall collider(s) this shot");
            return ledger;
        }
        catch (Exception e)
        {
            LogOnce("apply-failed", "wall pierce apply failed; already-recorded pairs stay owed (no half-write loss): " + e.GetType().Name);
            return ledger;
        }
    }

    /// <summary>
    /// 按箭身份直接归还该箭自己的账本（ResetArrow 前缀的兜底与测试/工具入口；生产归还的权威路径是
    /// 视觉回执缝合点里的 <see cref="RestoreLedger"/>）。只写账本里实际接管的 pair，绝不重扫、
    /// 绝不 blanket 写 false；失败项保留账本重试。绝不抛异常。
    /// </summary>
    internal static void Restore(Arrow arrow)
    {
        try
        {
            if (_liveLedgers == 0) return;
            PierceLedger ledger = FindLedgerForArrow(arrow);
            if (ledger == null) return;
            if (!RestoreLedger(ledger)) LogOnce("restore-incomplete", "a wall ignore pair could not be handed back yet; the ledger keeps it for retry");
        }
        catch (Exception e)
        {
            LogOnce("restore-failed", "wall pierce restore failed; an arrow may keep ignoring a wall: " + e.GetType().Name);
        }
    }

    /// <summary>
    /// 归还账本（视觉回执 TryRestore 的唯一调用点）：逐项写回 false，成功即删项；写失败 = 保留项重试；
    /// 只有能证明「引擎已清除」（该侧对象明确销毁/停用，碰撞体已离开物理世界）才直接删项。
    /// 返回 true = 账本清空（可以退休），false = 仍有未归还项（调用方保留回执并退避）。绝不抛异常。
    /// </summary>
    internal static bool RestoreLedger(PierceLedger ledger)
    {
        if (ledger == null || ledger.Retired) return true;
        try
        {
            Collider2D arrowCollider = ledger.ArrowCollider;
            bool arrowCleared;
            try { arrowCleared = IsEngineCleared(arrowCollider); }
            catch (Exception) { return false; }                    // 读异常 = 未知：保留全部责任
            if (arrowCleared) { RetireLedger(ledger); return true; } // 箭碰撞体已销毁/停用：引擎已清除该箭全部 pair

            for (int i = ledger.Count - 1; i >= 0; i--)
            {
                if (!RestoreEntry(ledger, i, arrowCollider)) continue;   // 写失败且对象仍活：保留重试
                ledger.RemoveAt(i);
            }
            if (ledger.Count == 0) { RetireLedger(ledger); return true; }
            return false;
        }
        catch (Exception e)
        {
            LogOnce("restore-failed", "wall pierce restore failed; the ledger keeps its pairs for retry: " + e.GetType().Name);
            return ledger.Retired || ledger.Count == 0;
        }
    }

    /// <summary>
    /// 归还单个 pair：true = 可删项。读取异常一律保留重试；只有明确销毁/停用（引擎已清除该 pair）才不写也删。
    /// </summary>
    private static bool RestoreEntry(PierceLedger ledger, int index, Collider2D arrowCollider)
    {
        if (ledger.States[index] == PierceLedger.StateProbe) return true;     // 从未写过：无责任可还
        Collider2D wall = ledger.Walls[index];
        bool wallCleared;
        try { wallCleared = IsEngineCleared(wall); }
        catch (Exception) { return false; }                                  // 读异常 = 未知：保留重试
        if (wallCleared) return true;                                        // 墙已销毁/停用：引擎已清除该 pair
        try
        {
            Physics2D.IgnoreCollision(arrowCollider, wall, false);
            return true;
        }
        catch (Exception)
        {
            return false;                                                    // 写失败：保留责任重试（绝不半途丢账）
        }
    }

    /// <summary>账本是否还有待重试项（未确认的 true 写入 / 未决的读取探测）：O(1)，供巡检每帧判定。</summary>
    internal static bool HasPendingRetry(PierceLedger ledger)
    {
        return ledger != null && !ledger.Retired && ledger.Pending;
    }

    /// <summary>
    /// 账本是否还有**未结清的责任**（至少一项未归还/未确认/待重探）：O(1)。
    /// 供视觉回执判定「旧责任未清 → 封锁新 renderer/新生命接管」（B1）。
    /// </summary>
    internal static bool HasUnsettled(PierceLedger ledger)
    {
        return ledger != null && !ledger.Retired && ledger.Count > 0;
    }

    /// <summary>
    /// 退避重试（巡检调用，**不扫描世界**，只处理账本里已有的碰撞体）：
    /// StateProbe → 重读：false 补写 true，true 说明不是本模块接管的（删项），异常继续挂账；
    /// StateUnwritten → 重写 true（native 写成功后才抛异常的情况在此补记）。绝不抛异常。
    /// </summary>
    internal static void RetryPending(PierceLedger ledger)
    {
        if (ledger == null || ledger.Retired || !ledger.Pending) return;
        try
        {
            float now = Now();
            if (now < ledger.NextRetry) return;
            Collider2D arrowCollider = ledger.ArrowCollider;
            bool arrowCleared;
            try { arrowCleared = IsEngineCleared(arrowCollider); }
            catch (Exception) { ledger.NextRetry = now + RetrySeconds; return; }   // 读异常 = 未知：保留重试
            if (arrowCleared) { RetireLedger(ledger); return; }

            for (int i = ledger.Count - 1; i >= 0; i--)
            {
                byte state = ledger.States[i];
                if (state == PierceLedger.StateOwned) continue;
                Collider2D wall = ledger.Walls[i];
                bool wallCleared;
                try { wallCleared = IsEngineCleared(wall); }
                catch (Exception) { continue; }                                    // 读异常 = 未知：保留
                if (wallCleared) { ledger.RemoveAt(i); continue; }                 // 明确销毁/停用：引擎已清除
                if (state == PierceLedger.StateProbe)
                {
                    bool ignored;
                    try { ignored = Physics2D.GetIgnoreCollision(arrowCollider, wall); }
                    catch (Exception) { continue; }                                // 仍未知：保留账本项，下次再探
                    if (ignored) { ledger.RemoveAt(i); continue; }                 // 已是 true：不是本模块接管的
                    ledger.MarkUnwritten(i);
                }
                try
                {
                    Physics2D.IgnoreCollision(arrowCollider, wall, true);
                    ledger.MarkWritten(i);
                }
                catch (Exception)
                {
                    // native 写可能已成功：保留 Unwritten，下次重写或归还。
                }
            }
            if (ledger.Count == 0) { RetireLedger(ledger); return; }
            if (ledger.Pending) ledger.NextRetry = now + RetrySeconds;
        }
        catch (Exception e)
        {
            LogOnce("retry-failed", "wall pierce retry failed; the ledger keeps its pairs: " + e.GetType().Name);
        }
    }

    // ============================================================
    // 墙碰撞体快照（只发现墙；TTL + world 换代）
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
    // 账本表
    // ============================================================

    /// <summary>按身份在表里找活动账本；表满/无身份 = null（调用方 fail-closed，绝不驱逐已有账本）。</summary>
    private static PierceLedger FindLedger(IntPtr goPtr, int goId, IntPtr arrowPtr)
    {
        for (int i = 0; i < MaxLedgerArrows; i++)
        {
            PierceLedger ledger = Ledgers[i];
            if (ledger == null || ledger.Retired) continue;
            if (SameArrowIdentity(ledger, goPtr, goId, arrowPtr)) return ledger;
        }
        return null;
    }

    /// <summary>
    /// 同一支箭的身份判定：双方都有 Arrow 指针时按 Arrow 指针（同 GO 上的另一个 arrow 组件绝不混淆）；
    /// Arrow 指针不可读时退回 GO 指针 + InstanceID。任何一侧身份都读不到 = 不匹配。
    /// </summary>
    private static bool SameArrowIdentity(PierceLedger ledger, IntPtr goPtr, int goId, IntPtr arrowPtr)
    {
        if (arrowPtr != IntPtr.Zero && ledger.ArrowPtr != IntPtr.Zero) return ledger.ArrowPtr == arrowPtr;
        if (goPtr != IntPtr.Zero && ledger.GoPtr != IntPtr.Zero) return ledger.GoPtr == goPtr && ledger.GoId == goId;
        return false;
    }

    /// <summary>租一个账本：已有的按身份复用（幂等，不建第二本账）；否则占一个空槽；表满 = null。</summary>
    private static PierceLedger RentLedger(IntPtr goPtr, int goId, IntPtr arrowPtr, Collider2D arrowCollider)
    {
        PierceLedger existing = FindLedger(goPtr, goId, arrowPtr);
        if (existing != null) return existing;
        for (int i = 0; i < MaxLedgerArrows; i++)
        {
            if (Ledgers[i] != null) continue;
            PierceLedger ledger = new PierceLedger
            {
                GoPtr = goPtr,
                GoId = goId,
                ArrowPtr = arrowPtr,
                ArrowCollider = arrowCollider,
            };
            Ledgers[i] = ledger;
            _liveLedgers++;
            return ledger;
        }
        return null;
    }

    /// <summary>账本清空即退休并释放表槽（对象可能仍被视觉回执引用：Retired 后一切归还都是 no-op）。</summary>
    private static void RetireLedger(PierceLedger ledger)
    {
        if (ledger == null || ledger.Retired) return;
        ledger.Retired = true;
        ledger.Count = 0;
        ledger.PendingCount = 0;
        ledger.NextRetry = 0f;
        ledger.ArrowCollider = null;
        for (int i = 0; i < MaxLedgerArrows; i++)
        {
            if (!ReferenceEquals(Ledgers[i], ledger)) continue;
            Ledgers[i] = null;
            if (_liveLedgers > 0) _liveLedgers--;
            return;
        }
    }

    /// <summary>按 Arrow 组件读身份找账本（字段读不到按未知处理，绝不因此丢账）。</summary>
    private static PierceLedger FindLedgerForArrow(Arrow arrow)
    {
        if (arrow == null) return null;
        IntPtr arrowPtr = IntPtr.Zero;
        try { arrowPtr = arrow.Pointer; }
        catch (Exception) { arrowPtr = IntPtr.Zero; }
        IntPtr goPtr = IntPtr.Zero;
        int goId = 0;
        bool haveGo = false;
        try { haveGo = TryGoIdentity(arrow.gameObject, out goPtr, out goId); }
        catch (Exception) { haveGo = false; }
        if (arrowPtr == IntPtr.Zero && !haveGo) return null;
        return FindLedger(haveGo ? goPtr : IntPtr.Zero, haveGo ? goId : 0, arrowPtr);
    }

    private static string LedgerFullMessage()
    {
        return "wall pierce ledger table full (" + MaxLedgerArrows
            + " arrows); this arrow keeps native wall collisions (no unrestored ledger evicted)";
    }

    private static string PairsFullMessage()
    {
        return "one arrow reached " + MaxWallColliders + " wall pairs; further walls keep native collisions";
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

    /// <summary>GO native 身份（指针 + InstanceID）；任何一步读不到 = false。</summary>
    private static bool TryGoIdentity(GameObject gameObject, out IntPtr goPtr, out int goId)
    {
        goPtr = IntPtr.Zero;
        goId = 0;
        try
        {
            if (gameObject == null) return false;
            goPtr = gameObject.Pointer;
            goId = gameObject.GetInstanceID();
            return goPtr != IntPtr.Zero && goId != 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// 账本记录的实际碰撞体与本次箭碰撞体是否同一 native 对象（指针比较，绝不用 wrapper ReferenceEquals：
    /// IL2CPP 下 wrapper 可能每帧重建）。任一侧 null / 指针读异常 = 未知 → false（调用方拒新写、保旧账）。
    /// </summary>
    private static bool SameColliderIdentity(Collider2D recorded, Collider2D current)
    {
        try
        {
            if (recorded == null || current == null) return false;
            IntPtr recordedPtr = recorded.Pointer;
            IntPtr currentPtr = current.Pointer;
            return recordedPtr != IntPtr.Zero && recordedPtr == currentPtr;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// 「引擎已清除该碰撞体的 ignore 状态」的判据：对象明确销毁（null/无 GO）或所在 GO 已停用
    /// （碰撞体已移出物理世界，Unity 文档明确 ignore 状态随之丢失）。读异常按未知抛出，由调用方保留重试。
    /// </summary>
    private static bool IsEngineCleared(Collider2D collider)
    {
        if (collider == null) return true;
        GameObject gameObject = collider.gameObject;
        if (gameObject == null) return true;
        return !gameObject.activeSelf;
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

    /// <summary>清空快照/账本表与日志预算，供测试用例之间隔离；无游戏写入。</summary>
    internal static void ResetForTests()
    {
        WallColliders.Clear();
        Scratch.Clear();
        Logged.Clear();
        for (int i = 0; i < MaxLedgerArrows; i++) Ledgers[i] = null;
        _liveLedgers = 0;
        _cacheValid = false;
        _cachedAt = 0f;
        _cachedWorld = IntPtr.Zero;
        _cachedLayer = IntPtr.Zero;
        _cachedScene = 0;
        _logBudget = 0;
        _scanCount = 0;
    }
}
