using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 希腊火矢命中爆发（combat 模块，默认关闭；沿用 ModConfig.ArcherImpactEnabled）。
///
/// 范围（本文件只做这一件事）：
/// 仅在「希腊 style3 骑士的原生 FireAttacks 窗口内」，其所属弓手射出的原生 fire 箭
/// （<c>Archer._fireArrowAttack</c> 的 <c>_arrowPrefab.isFireArrow</c>）命中时：
/// 1) 跳过该箭 native <c>TryDamage</c> 的持续灼烧（ApplyDelayedDamage），其余分支按原生
///    逐字等价执行（authorityActive / null / IsDamagedBy(_damageSource) / perfect 倍率）；
/// 2) 在命中点（prefix 快照）做一次半径 0.25 的邻近敌人 AoE，每个敌人 1 点 Fire 伤害。
/// 原箭的直接伤害、弹跳、消失时序、impactSpawner 完全不动。
/// 关闭功能、换 world、非 style3 骑士、非 fire 箭、非其所属弓手 → 一律原版行为。
///
/// 关键设计（窄伤害替代，**不写任何原生可变字段**）：
/// - 本模块绝不改写 <c>isFireArrow</c>/<c>damageTicks</c>/<c>damagePerTick</c>/trail 等原生字段，
///   因此不需要任何「借值归还」所有权系统。
/// - 命中流程：既有 HitObject wrapper 在 Capture 之后调用 <see cref="BeginHit"/> 建立**不可变事务**
///   （leaseEpoch + volleyEpoch + arrow/source GO 身份 + 直接 Damageable 指针 + 命中点）；
///   <see cref="TrySubstituteDirectDamage"/> 作为 <c>Arrow.TryDamage</c> 的窄 Prefix，**只**匹配
///   「当前正在执行的这次命中事务 + 同一 arrow 生命 + 同一直接 Damageable 指针」；任何其它
///   TryDamage（其它箭、其它目标、无事务）一律放行原生。
/// - 拒绝替代时绝不回落原生 DOT：同一事务内的重入调用只返回「已处理」。
/// - <see cref="EndHit"/> 返回 bool：仅当本次 ticket 仍匹配、事务仍开、native <c>_hasHit</c>
///   由 false 变 true、且替代未失败时为 true（表示可画 FX）；先锁定 accepted 并关事务，再做 AoE。
///
/// 需求边界（本模块刻意不做）：
/// - 只挂在已核 native 入口：<c>ArrowAttack.FireArrowInternal</c>(0x4c7070)、
///   <c>Arrow.OnEnable</c>(0x4c90d0)、<c>Arrow.TryDamage</c>(0x4c98e0/368B)；
///   命中观测由既有 <c>Arrow.HitObject</c> wrapper 调用本模块 API（见 integration.md）。
/// - 不发 RPC、不改全局 Physics2D 状态、不改 BuffData/全局 buff/其它火焰逻辑、不注册 Pool、
///   不删箭、不做逐帧全场扫描。
///
/// 英雄来源（最小扩展；两个来源互不共享开关，也互不失效）：
/// 英雄弓箭手的**普通箭**（非 fire）同样允许绑定这套 volley/lease，从而复用同一份 FX 与 0.25/1 点爆发：
/// - 来源在**发射时**记录在 volley 上、随 lease 不可变携带（Greek / Hero）。hero 来源只要求
///   <see cref="HeroArcherCombat.IsHeroCombatEligible"/>（真实射击敌人）为真：不要求 fire prefab、
///   不要求 style3 骑士与 FireAttacks 窗口，也绝不改写任何原生字段（无原生 fire 长期灼烧参与）。
/// - 每个入口都按来源查对应开关：Greek → ArcherImpactEnabled、Hero → HeroArcherRuntime.Enabled。
///   因此「Greek 开关关闭时非英雄希腊箭拿不到特效」与「英雄关闭后飞行中的英雄箭不再爆炸」同时成立；
///   在空中的箭只按来源撤账，不因 Buff 过期而失效。
/// - hero 来源同样参与「取消 native 持续灼烧」的直接伤害替代：英雄在原生 FireAttacks buff 下打出的**真 fire 箭**
///   与其英雄开关联动（英雄关闭 → 立即回落原生灼烧）。英雄的**普通箭**没有 DOT 路径，直接伤害保持原生。
///
/// 时间语义：资格在**发射时**记录（scope + 该箭 OnEnable 绑定），中途 buff 过期不取消已在
/// 空中的 shot；功能关闭或换 world 立即失效（Tick 与每个入口先撤旧账）。
///
/// 容量（固定、有界；超限保守退回原版并限频计数，**不保证极端规模下全量生效**）：
/// 并发 lease ≤ <see cref="MaxLeases"/>、并发 volley ≤ <see cref="MaxVolleys"/>、
/// 嵌套深度 ≤ <see cref="MaxStack"/>（溢出掩蔽且对称回收）、单 volley 去重目标 ≤
/// <see cref="MaxVolleyTargets"/>、query 缓冲固定 <see cref="MaxOverlap"/>。
///
/// 目标生命（修复「同对象回池后新 life 被旧指针账本误去重」）：
/// - 去重账本按 <see cref="CombatTargetToken"/>（GO 身份 + Damageable 身份 + 全局单调 life）判定：
///   同一 life 同一 volley 仅一次；同一对象回池（停用/启用）拿到新 life 后可在**后续 burst** 再吃一次。
/// - 每次 burst 先对本次物理查询的全部候选做「目标 + life」快照（与查询同界 ≤ <see cref="MaxOverlap"/>，
///   不提交、不占账本、不按 32 名额截断，只做 burst 内局部去重），再逐项重跑完整复核（世代/权限/world、
///   完整 <see cref="TryResolveTarget"/>、同一目标、同一 life、半径、本 volley 已结算）—— 只有通过者才消耗
///   32 提交名额并**紧贴提交前**登记 volley 去重：上一目标回调造成的身份/资格/位置变化不会误伤后续目标，
///   复核失效而未提交的目标既不记账也不白占名额（同一 volley 的后续箭仍可在其恢复有效后命中），
///   本次查询的旧 collider 也不会打到该 burst 中才回池重生的新 life（新 life 留给后续 burst 判定）。
/// - life 证据不可用（注册/创建/取号失败，或 marker 仍禁用/处于观察缺口）时**显式放弃该目标**
///   （计数 + 限频日志），绝不退回 Damageable 指针去重、绝不用假 token；原生直接伤害与其余目标不受影响
///   （见 <see cref="CombatTargetLife"/>）。
/// - AoE 伤害统一经 <see cref="CombatDamage.Submit"/> 提交（原生 ReceiveDamage + 异常隔离，见该文件）；
///   Faulted 视为已消费不重试。
/// </summary>
internal static class PatchArcher_GreekImpact
{
    // ============================================================
    // 常量与容量
    // ============================================================

    /// <summary>爆发半径（游戏单位）：以命中点为圆心，按 collider 相交判定。</summary>
    internal const float Radius = 0.25f;
    /// <summary>每个邻近敌人的一次伤害值。</summary>
    internal const int BurstDamage = 1;

    internal const int MaxOverlap = 64;         // 固定 NonAlloc 查询缓冲
    internal const int MaxLeases = 1280;        // 并发箭生命账本（132 弓手 × 每发最多 5 箭）
    internal const int MaxVolleys = 256;        // 并发 shot group
    internal const int MaxStack = 16;           // 嵌套 scope 栈深
    internal const int MaxVolleyTargets = 32;   // 单 volley 去重目标数

    private const float MaxLeaseSeconds = 20f;
    private const float MaxVolleySeconds = 20f;
    private const int SweepPerTick = 24;        // 每帧有界清理预算

    internal const int NoScope = -1;            // 无 scope：不压栈、EndShot 无操作
    internal const int MaskedScope = -2;        // 有 scope 但屏蔽外层资格（无租赁）

    // 来源（不可变，随 volley/lease 携带）：同一套账本与 AoE 路径服务两个互不共享开关的来源。
    internal const int OriginGreek = 0;         // 希腊 style3 骑士窗口内的原生 fire 箭
    internal const int OriginHero = 1;          // 英雄弓箭手的箭（普通箭同样绑定，复用 FX 与 0.25/1 点爆发）

    // ============================================================
    // 统计（internal 供测试与实机核对；不逐帧日志）
    // ============================================================

    internal static int StatBursts, StatDamage, StatBufferFull, StatVolleyCap, StatLeaseCap,
        StatScopeCap, StatStackOverflow, StatReentry, StatLeaseRetired, StatArcherInvalidated,
        StatSourceInvalidated, StatNoTarget, StatStaleTicket, StatSubstituted, StatSubstituteFailed,
        StatSubstituteReentry, StatLifeDegraded, StatDamageFaulted;

    // ============================================================
    // 账本
    // ============================================================

    /// <summary>一次 <c>FireArrowInternal</c> 调用 = 一个 volley（原始箭 + 全部 scatter 额外箭）。
    /// 对象会被原地复用，身份以 <see cref="Epoch"/> 为准。</summary>
    private sealed class Volley
    {
        internal long Epoch;                 // 不可变世代（每次租赁 +1）；0 = 未使用/已失效
        internal bool Eligible;
        internal int Origin;                 // Greek / Hero：不可变来源（决定开关与箭类型要求）
        internal GameObject Source;          // 弓手 GO
        internal IntPtr SourcePtr;           // GO.Pointer
        internal int SourceGoId;             // GO InstanceID
        internal IntPtr World, Layer;
        internal int Scene;
        internal float BornAt;
        internal bool Closed;
        internal int RefCount;
        internal int TargetCount;
        internal CombatTargetToken[] Targets;   // 懒建，长度 MaxVolleyTargets（本 volley 已结算的 life token）
    }

    /// <summary>一支箭的一次存活（pool 复用即新世代）。身份 = <see cref="Epoch"/> + GO 身份。</summary>
    private sealed class Lease
    {
        internal bool Active;
        internal Arrow Arrow;                // 有界强引用
        internal IntPtr Ptr;                 // GO.Pointer
        internal int GoId;                   // GO InstanceID
        internal long Epoch;                 // 不可变世代（每次绑定 +1）
        internal bool Eligible;
        internal int Origin;                 // Greek / Hero：不可变来源（发射时固定）
        internal int VolleySlot;
        internal long VolleyEpoch;           // 绑定时的 volley 世代（不可变）
        internal GameObject Source;
        internal IntPtr SourcePtr;           // 弓手 GO.Pointer
        internal int SourceGoId;             // 弓手 GO InstanceID
        internal IntPtr World, Layer;
        internal int Scene;
        internal float BornAt;
        internal bool TxOpen;                // 命中事务进行中
        internal long TxEpoch;              // 同一箭生命内的不同命中也必须区分
        internal bool TxSubstituted;         // 本次事务已替代过一次 TryDamage
        internal bool TxFailed;              // 替代过程出错（异常已上抛）
        internal IntPtr TxTargetDamageable;  // 本次事务的直接命中 Damageable
    }

    /// <summary>
    /// 一次 shot scope 的凭据（值类型，可安全跨 Finalizer 传递）。Slot/Epoch/Depth/ResetEpoch
    /// 均为不可变快照：只有栈顶仍是本 scope 时才 pop/close，绝不动更晚的 scope；
    /// ReleaseAll 递增 <see cref="ResetEpoch"/> 使所有旧 ticket（含掩蔽项）立即失效。
    /// </summary>
    internal struct ShotTicket
    {
        internal bool Pushed;      // false → EndShot 完全无操作（关闭且无外层 scope 的零开销路径）
        internal bool Masked;
        internal int Slot;
        internal long Epoch;
        internal int Depth;
        internal long ResetEpoch;
    }

    /// <summary>一次命中的不可变事务凭据（wrapper 存 __state 原样回传）。</summary>
    internal struct HitTicket
    {
        internal bool Valid;
        internal int LeaseSlot;
        internal long LeaseEpoch;
        internal long TxEpoch;
        internal int VolleySlot;
        internal long VolleyEpoch;
        internal IntPtr ArrowPtr;
        internal int ArrowGoId;
        internal bool HasPoint;
        internal Vector2 Point;             // prefix 当刻命中点（native 之后 arrow 可能已移动/回池）
        internal IntPtr DirectDamageable;   // 直接命中的 Damageable（本次事务唯一可替代目标）
        internal long ResetEpoch;
    }

    private static readonly Lease[] Leases = new Lease[MaxLeases];
    private static readonly Volley[] Volleys = new Volley[MaxVolleys];
    private static readonly Dictionary<int, int> LeaseByGoId = new Dictionary<int, int>();
    private static readonly int[] StackSlot = new int[MaxStack];
    private static readonly long[] StackEpoch = new long[MaxStack];
    private static readonly int[] FreeLease = new int[MaxLeases];
    private static readonly int[] FreeVolley = new int[MaxVolleys];
    private static readonly Dictionary<string, int> Logs = new Dictionary<string, int>();

    private static int FreeLeaseCount, FreeVolleyCount, Depth, Cursor, SweepSlot;
    private static int ActiveLeases, ActiveVolleys;
    private static long NextLeaseEpoch, NextVolleyEpoch, NextTxEpoch, ResetEpoch;
    private static bool MaskOverflow;
    private static int BurstDepth;
    private static IntPtr ContextWorld, ContextLayer;
    private static int ContextScene;
    private static bool ContextSet;

    private static Il2CppReferenceArray<Collider2D> Buffer;
    private static StagedTarget[] Staging;      // burst 快照（懒建，长度 MaxOverlap = 物理查询上限），BurstDepth 保证不可重入
    private static ContactFilter2D Filter;
    private static bool FilterReady;
    private static int EnemyLayer = -1;

    static PatchArcher_GreekImpact()
    {
        RebuildFreeLists();
    }

    private static void RebuildFreeLists()
    {
        for (int i = 0; i < MaxLeases; i++) FreeLease[i] = i;
        FreeLeaseCount = MaxLeases;
        for (int i = 0; i < MaxVolleys; i++) FreeVolley[i] = i;
        FreeVolleyCount = MaxVolleys;
        ActiveLeases = 0;
        ActiveVolleys = 0;
    }

    // ============================================================
    // 基础设施
    // ============================================================

    private static bool IsEnabled => ModConfig.ArcherImpactEnabled != null && ModConfig.ArcherImpactEnabled.Value;

    /// <summary>
    /// 来源对应的开关（每个入口都按来源查，绝不用一个来源的开关放行另一个来源）：
    /// Greek = ArcherImpactEnabled + root 范围；Hero = runtime 契约 Enabled + root 范围。
    /// </summary>
    private static bool OriginEnabled(int origin)
    {
        if (origin == OriginHero) return HeroArcherCombat.HeroPossible;
        return IsEnabled && ArcherOptionsScope.IsActive;
    }

    /// <summary>
    /// 任一路径是否可用：既有 impact 渲染池（PatchArcher_Impact）用它决定是否保留 FX 资源，
    /// 两个来源都关时才释放；英雄只在 HeroArcherEnabled 打开时复用同一份像素火焰。
    /// </summary>
    internal static bool AnyOriginEnabled
    {
        get { return OriginEnabled(OriginGreek) || OriginEnabled(OriginHero); }
    }

    /// <summary>计数日志：首次与每 256 次各一行，避免刷屏。</summary>
    private static void Count(string key)
    {
        int n;
        Logs.TryGetValue(key, out n);
        Logs[key] = n + 1;
        if (n == 0 || (n & 255) == 255)
        {
            try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[GreekImpact] " + key + " count=" + (n + 1)); }
            catch { }
        }
    }

    private static bool Same(UnityEngine.Object a, UnityEngine.Object b) =>
        a != null && b != null && a.Pointer == b.Pointer;

    private static bool Live(GameObject go) => go != null && go.activeInHierarchy;

    private static int GoId(GameObject go)
    {
        try { return go != null ? go.GetInstanceID() : 0; }
        catch { return 0; }
    }

    private static bool CurrentComponent(Component component)
    {
        try { return component != null && component.gameObject != null && ArcherOptionsScope.IsCurrent(component); }
        catch { return false; }
    }

    private static bool CurrentGO(GameObject go)
    {
        try { return go != null && go.transform != null && ArcherOptionsScope.IsCurrent(go.transform); }
        catch { return false; }
    }

    /// <summary>原生 Buffable 上 FireAttacks 是否仍在有效窗口内（knight 与 archer 各查一次）。</summary>
    private static bool FireWindowOpen(Buffable buffable)
    {
        try
        {
            if (buffable == null || !buffable.enabled) return false;
            if (buffable._owner == null) return false;
            var applicable = buffable._applicableBuffs;
            if (applicable == null || !applicable.Contains(BuffType.FireAttacks)) return false;
            var expirations = buffable._activeBuffsExpirations;
            if (expirations == null) return false;
            float expiry;
            if (!expirations.TryGetValue(BuffType.FireAttacks, out expiry)) return false;
            return expiry > Time.time;
        }
        catch { return false; }
    }

    private static bool ArcherAlive(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null || !archer.isActiveAndEnabled) return false;
            Damageable damageable = archer._damageable;
            return damageable != null && !damageable.isDead;
        }
        catch { return false; }
    }

    /// <summary>发射时资格：双方 FireAttacks 窗口 + 所属关系 + fire prefab + 当前 world。</summary>
    private static bool ShotEligible(ArrowAttack attack, GameObject source)
    {
        try
        {
            if (attack == null || source == null) return false;
            if (!CurrentGO(source)) return false;
            Archer archer = source.GetComponent<Archer>();
            if (archer == null || !ArcherAlive(archer) || !CurrentComponent(archer)) return false;
            if (!Same(attack, archer._fireArrowAttack)) return false;
            Arrow prefab = attack._arrowPrefab;
            if (prefab == null || !prefab.isFireArrow) return false;
            Character character = archer._character;
            if (character == null || character.inert || character.grabbed) return false;
            Embarkee embarkee = archer._embarkee;
            if (embarkee == null || embarkee.IsEmbarked) return false;
            Knight knight = archer._knight;
            if (knight == null || knight.gameObject == null || !knight.isActiveAndEnabled) return false;
            if (knight._harmless) return false;
            Damageable knightDamageable = knight._damageable;
            if (knightDamageable == null || knightDamageable.isDead) return false;
            Character knightCharacter = knight._character;
            if (knightCharacter == null || knightCharacter.inert || knightCharacter.grabbed) return false;
            Embarkee knightEmbarkee = knight._embarkee;
            if (knightEmbarkee == null || knightEmbarkee.IsEmbarked) return false;
            if (!CurrentComponent(knight)) return false;
            if (!PatchRoles_KnightStyle.TryGetResolvedStyleIndex(knight, out int style) || style != 3) return false;
            return FireWindowOpen(knight.Buffable) && FireWindowOpen(archer.Buffable);
        }
        catch (Exception e)
        {
            Count("eligible:" + e.GetType().Name);
            return false;
        }
    }

    /// <summary>
    /// 发射时英雄资格（只读、零写入）：当前世界的存活弓手 + runtime 判定为英雄 + 本次确实射击敌人。
    /// 不要求 fire prefab / style3 骑士 / FireAttacks 窗口 —— 英雄的普通箭同样进入同一 volley。
    /// </summary>
    private static bool HeroShotEligible(ArrowAttack attack, GameObject source)
    {
        try
        {
            if (attack == null || source == null) return false;
            if (!CurrentGO(source)) return false;
            Archer archer = source.GetComponent<Archer>();
            if (archer == null || !ArcherAlive(archer) || !CurrentComponent(archer)) return false;
            Character character = archer._character;
            if (character == null || character.inert || character.grabbed) return false;
            Embarkee embarkee = archer._embarkee;
            if (embarkee == null || embarkee.IsEmbarked) return false;
            return HeroArcherCombat.IsHeroCombatEligible(archer);
        }
        catch (Exception e)
        {
            Count("hero-eligible:" + e.GetType().Name);
            return false;
        }
    }

    /// <summary>归属复核：arrow.archer 必须仍是发射时的那个弓手 GO、存活且仍在当前 world。</summary>
    private static bool SourceBound(Arrow arrow, Lease lease)
    {
        try
        {
            if (arrow == null || arrow.gameObject == null) return false;
            GameObject owner = arrow.archer;
            if (owner == null) return false;
            if (owner.Pointer != lease.SourcePtr || GoId(owner) != lease.SourceGoId) return false;
            if (!Live(owner) || !CurrentGO(owner)) return false;
            Archer archer = owner.GetComponent<Archer>();
            return archer != null && ArcherAlive(archer) && CurrentComponent(archer);
        }
        catch { return false; }
    }

    private static bool ContextMatches(Lease lease)
    {
        IntPtr world, layer; int scene;
        if (!ArcherOptionsScope.TryGetContext(out world, out layer, out scene)) return false;
        return world == lease.World && layer == lease.Layer && scene == lease.Scene;
    }

    /// <summary>world 变化（Tick 尚未跑）时先撤旧账：旧 ticket/lease 立即失效，再绑定新 context。</summary>
    private static bool EnsureContextFresh()
    {
        try
        {
            IntPtr world, layer; int scene;
            if (!ArcherOptionsScope.TryGetContext(out world, out layer, out scene))
            {
                if (HasState) ReleaseAll();
                ContextSet = false;
                return false;
            }
            if (!ContextSet || world != ContextWorld || layer != ContextLayer || scene != ContextScene)
            {
                if (HasState) ReleaseAll();
                ContextWorld = world; ContextLayer = layer; ContextScene = scene; ContextSet = true;
            }
            return true;
        }
        catch (Exception e)
        {
            Count("context:" + e.GetType().Name);
            if (HasState) ReleaseAll();
            return false;
        }
    }

    private static bool HasState => ActiveLeases > 0 || ActiveVolleys > 0 || Depth > 0;

    private static int FindLease(Arrow arrow)
    {
        try
        {
            if (arrow == null || arrow.gameObject == null) return -1;
            int goId = GoId(arrow.gameObject);
            int slot;
            if (!LeaseByGoId.TryGetValue(goId, out slot)) return -1;
            Lease lease = Leases[slot];
            if (lease == null || !lease.Active || lease.GoId != goId || lease.Ptr != arrow.Pointer) return -1;
            lease.Arrow = arrow;
            return slot;
        }
        catch { return -1; }
    }

    private static bool LeaseMatchesTicket(HitTicket ticket, out Lease lease)
    {
        lease = null;
        if (ticket.ResetEpoch != ResetEpoch) return false;
        if (ticket.LeaseSlot < 0 || ticket.LeaseSlot >= MaxLeases) return false;
        Lease candidate = Leases[ticket.LeaseSlot];
        if (candidate == null || !candidate.Active) return false;
        if (candidate.Epoch != ticket.LeaseEpoch) return false;
        if (candidate.TxEpoch != ticket.TxEpoch) return false;
        if (candidate.Ptr != ticket.ArrowPtr || candidate.GoId != ticket.ArrowGoId) return false;
        lease = candidate;
        return true;
    }

    // ============================================================
    // volley / lease 生命周期
    // ============================================================

    private static int RentVolley()
    {
        if (FreeVolleyCount <= 0) return -1;
        int slot = FreeVolley[--FreeVolleyCount];
        if (Volleys[slot] == null) Volleys[slot] = new Volley();
        ActiveVolleys++;
        return slot;
    }

    private static void FreeVolleySlot(int slot)
    {
        Volley volley = Volleys[slot];
        volley.Epoch = 0;                    // 旧 ShotTicket 立即失效
        volley.Eligible = false;
        volley.Origin = OriginGreek;
        volley.Closed = true;
        volley.RefCount = 0;
        volley.TargetCount = 0;
        volley.Source = null;
        FreeVolley[FreeVolleyCount++] = slot;
        if (ActiveVolleys > 0) ActiveVolleys--;
    }

    private static int RentLease()
    {
        if (FreeLeaseCount <= 0) return -1;
        int slot = FreeLease[--FreeLeaseCount];
        if (Leases[slot] == null) Leases[slot] = new Lease();
        return slot;
    }

    private static void ReturnLeaseSlot(int slot)
    {
        FreeLease[FreeLeaseCount++] = slot;
    }

    private static void RetireLease(int slot)
    {
        Lease lease = Leases[slot];
        if (lease == null || !lease.Active) return;
        int goId = lease.GoId;
        int mapped;
        if (goId != 0 && LeaseByGoId.TryGetValue(goId, out mapped) && mapped == slot) LeaseByGoId.Remove(goId);
        int volleySlot = lease.VolleySlot;
        long volleyEpoch = lease.VolleyEpoch;
        lease.Active = false;
        lease.TxOpen = false;
        lease.TxSubstituted = false;
        lease.TxFailed = false;
        lease.TxTargetDamageable = IntPtr.Zero;
        lease.Arrow = null;
        lease.Source = null;
        lease.Origin = OriginGreek;
        lease.VolleySlot = NoScope;
        lease.VolleyEpoch = 0;
        FreeLease[FreeLeaseCount++] = slot;
        if (ActiveLeases > 0) ActiveLeases--;
        StatLeaseRetired++;
        if (volleySlot >= 0 && volleySlot < MaxVolleys)
        {
            Volley volley = Volleys[volleySlot];
            if (volley != null && volley.Epoch == volleyEpoch)
            {
                if (volley.RefCount > 0) volley.RefCount--;
                if (volley.Closed && volley.RefCount == 0) FreeVolleySlot(volleySlot);
            }
        }
    }

    // ============================================================
    // Operator 接入入口
    // ============================================================

    /// <summary>根面板 ModPanel.Update 每帧调用。关闭时零扫描（O(1) 状态判断）。</summary>
    internal static void Tick()
    {
        try
        {
            if (!AnyOriginEnabled)
            {
                if (HasState) ReleaseAll();
                return;
            }
            if (!EnsureContextFresh())
            {
                if (HasState) ReleaseAll();
                return;
            }
            if (HasState) Sweep();
        }
        catch (Exception e)
        {
            Count("tick:" + e.GetType().Name);
        }
    }

    /// <summary>退出/关闭：清空全部 scope/lease 并使所有旧 ticket 永久失效。</summary>
    internal static void ReleaseAll()
    {
        for (int i = 0; i < MaxLeases; i++)
        {
            Lease lease = Leases[i];
            if (lease == null || !lease.Active) continue;
            lease.TxFailed = true;                 // 未完成的事务绝不再替代/AoE
            RetireLease(i);
        }
        LeaseByGoId.Clear();
        for (int i = 0; i < MaxVolleys; i++)
        {
            Volley volley = Volleys[i];
            if (volley == null) continue;
            volley.Epoch = 0;
            volley.Eligible = false;
            volley.Closed = true;
            volley.RefCount = 0;
            volley.TargetCount = 0;
            volley.Source = null;
        }
        RebuildFreeLists();
        Depth = 0; MaskOverflow = false; Cursor = 0; SweepSlot = 0;
        ResetEpoch++;                              // 旧 ShotTicket/HitTicket 全部失效
        ContextSet = false; ContextWorld = IntPtr.Zero; ContextLayer = IntPtr.Zero; ContextScene = 0;
        ClearBuffer();
        ClearStaging(Staging != null ? Staging.Length : 0);
    }

    /// <summary>有界清理：轮转扫描固定数量 lease/volley，退役已销毁/回池/命中/超期条目。</summary>
    private static void Sweep()
    {
        float now = Time.time;
        for (int n = 0; n < SweepPerTick; n++)
        {
            int slot = Cursor;
            Cursor = (Cursor + 1) % MaxLeases;
            Lease lease = Leases[slot];
            if (lease == null || !lease.Active) continue;
            Arrow arrow = lease.Arrow;
            bool stale = arrow == null || arrow.gameObject == null || !arrow.gameObject.activeInHierarchy
                || arrow._hasHit || now - lease.BornAt > MaxLeaseSeconds;
            if (!stale && !OriginEnabled(lease.Origin))
            {
                stale = true;                              // 来源开关已关：在空中的箭立即撤账（不再 FX/AoE）
                Count("lease-origin-off");
            }
            if (!stale) continue;
            if (now - lease.BornAt > MaxLeaseSeconds) Count("lease-expired");
            lease.TxFailed = true;
            RetireLease(slot);
        }
        int volleyBudget = SweepPerTick / 4;
        for (int n = 0; n < volleyBudget; n++)
        {
            int slot = SweepSlot;
            SweepSlot = (SweepSlot + 1) % MaxVolleys;
            Volley volley = Volleys[slot];
            if (volley == null || volley.Epoch == 0) continue;
            if (!volley.Closed && now - volley.BornAt > MaxVolleySeconds)
            {
                Count("scope-expired");
                FreeVolleySlot(slot);
            }
        }
    }

    /// <summary>
    /// FX 侧只读来源查询：该箭当前是否是「合资格箭」（lease 活跃、来源开关仍开、world/scene 与归属复核通过），
    /// 并给出其不可变来源标记（false = Greek、true = Hero）。任何失败都是 false（fail-closed）。
    /// </summary>
    internal static bool TryGetEligibleOrigin(Arrow arrow, out bool heroOrigin)
    {
        heroOrigin = false;
        try
        {
            if (ActiveLeases == 0) return false;                         // 无账本：零开销
            if (arrow == null || arrow.gameObject == null) return false;
            if (!EnsureContextFresh()) return false;
            int slot = FindLease(arrow);
            if (slot < 0) return false;
            Lease lease = Leases[slot];
            if (!OriginEnabled(lease.Origin)) return false;               // 该来源的开关已关
            if (!arrow.isFireArrow && !HeroArcherCombat.AllowsNormalArrow(lease.Origin == OriginHero)) return false;
            if (!lease.Eligible) return false;
            if (!ContextMatches(lease)) return false;
            if (!CurrentComponent(arrow)) return false;
            if (!SourceBound(arrow, lease)) return false;
            heroOrigin = lease.Origin == OriginHero;
            return true;
        }
        catch { return false; }
    }

    /// <summary>FX 前置门（既有 HitObject wrapper 的 Capture 调用）：等价于来源查询成功。</summary>
    internal static bool IsEligibleArrow(Arrow arrow) => TryGetEligibleOrigin(arrow, out _);

    /// <summary>该来源当前是否开启（FX 池按来源清理时查询；不看具体箭）。</summary>
    internal static bool IsOriginEnabledFor(bool heroOrigin) => OriginEnabled(heroOrigin ? OriginHero : OriginGreek);

    /// <summary>
    /// 弓手池复用世代失效（由 Operator 接入既有 Archer.OnEnable Prefix）：
    /// 1) 使该 GO 为 source 的活跃 volley 失效；2) 退役该 source 的旧 lease。
    /// 无活跃账本时 O(1) 直接返回，关闭功能不会因每个新弓手扫 1536 槽。
    /// </summary>
    internal static void OnArcherEnable(Archer archer)
    {
        try
        {
            if (ActiveLeases == 0 && ActiveVolleys == 0) return;
            if (archer == null || archer.gameObject == null) return;
            IntPtr sourcePtr = archer.gameObject.Pointer;      // GO 身份
            int sourceGoId = GoId(archer.gameObject);
            for (int i = 0; i < MaxVolleys; i++)
            {
                Volley volley = Volleys[i];
                if (volley == null || volley.Epoch == 0 || !volley.Eligible) continue;
                if (volley.SourcePtr != sourcePtr || volley.SourceGoId != sourceGoId) continue;
                volley.Eligible = false;
                StatSourceInvalidated++;
            }
            for (int i = 0; i < MaxLeases; i++)
            {
                Lease lease = Leases[i];
                if (lease == null || !lease.Active) continue;
                if (lease.SourcePtr != sourcePtr || lease.SourceGoId != sourceGoId) continue;
                lease.TxFailed = true;
                RetireLease(i);
                StatArcherInvalidated++;
            }
        }
        catch (Exception e)
        {
            Count("archer-enable:" + e.GetType().Name);
        }
    }

    // ============================================================
    // 发射：FireArrowInternal scope
    // ============================================================

    /// <summary>
    /// 开始一次 shot scope。关闭/不可用/不合资格/容量不足时**不租赁、不读 Unity 对象**：
    /// 无外层 scope 直接返回未压栈 ticket（零开销），有外层则压掩蔽项（屏蔽外层资格）。
    /// 任何 post-rent 异常都归还本次租赁的槽。
    /// </summary>
    internal static ShotTicket BeginShot(ArrowAttack attack, GameObject source)
    {
        int rented = -1;
        try
        {
            bool greekOn = IsEnabled && ArcherOptionsScope.IsActive;
            bool heroOn = HeroArcherCombat.HeroPossible;
            if ((!greekOn && !heroOn) || !NetworkBigBoss.HasWorldAuth || Time.timeScale <= 0f)
                return PushMasked();
            if (!EnsureContextFresh()) return PushMasked();
            IntPtr world, layer; int scene;
            if (!ArcherOptionsScope.TryGetContext(out world, out layer, out scene))
                return PushMasked();
            // 来源优先：英雄优先于希腊 —— 双开时该 volley 记 Hero，关掉英雄就不会留下一个仍被 Greek 开关
            // 撑着的活跃 volley；纯希腊（非英雄）行为完全不变。
            int origin;
            if (heroOn && HeroShotEligible(attack, source)) origin = OriginHero;
            else if (greekOn && ShotEligible(attack, source)) origin = OriginGreek;
            else return PushMasked();
            if (Depth >= MaxStack) return PushMasked();         // 溢出：掩蔽（Depth 仍对称自增）
            int slot = RentVolley();
            if (slot < 0)
            {
                StatScopeCap++;
                Count("scope-cap");
                return PushMasked();
            }
            rented = slot;
            Volley volley = Volleys[slot];
            volley.Epoch = ++NextVolleyEpoch;
            volley.Eligible = true;
            volley.Origin = origin;
            volley.Source = source;
            volley.SourcePtr = source != null ? source.Pointer : IntPtr.Zero;
            volley.SourceGoId = GoId(source);
            volley.World = world; volley.Layer = layer; volley.Scene = scene;
            volley.BornAt = Time.time;
            volley.Closed = false;
            volley.RefCount = 0;
            volley.TargetCount = 0;
            StackSlot[Depth] = slot;
            StackEpoch[Depth] = volley.Epoch;
            Depth++;
            MaskOverflow = Depth > MaxStack;
            ShotTicket ticket = new ShotTicket
            {
                Pushed = true, Slot = slot, Epoch = volley.Epoch, Depth = Depth, ResetEpoch = ResetEpoch
            };
            rented = -1;                                        // 已交给 ticket，不再回收
            return ticket;
        }
        catch (Exception e)
        {
            Count("begin-shot:" + e.GetType().Name);
            if (rented >= 0) FreeVolleySlot(rented);             // 归还本次租赁，绝不泄漏
            return PushMasked();
        }
    }

    private static ShotTicket PushMasked()
    {
        if (Depth <= 0) return default;                          // 无外层：不压栈，零开销、零 Unity 读
        ShotTicket ticket = new ShotTicket
        {
            Pushed = true, Masked = true, Slot = MaskedScope, Epoch = 0, ResetEpoch = ResetEpoch
        };
        if (Depth < MaxStack)
        {
            StackSlot[Depth] = MaskedScope;
            StackEpoch[Depth] = 0;
        }
        else
        {
            StatStackOverflow++;
        }
        Depth++;
        ticket.Depth = Depth;
        MaskOverflow = Depth > MaxStack;
        return ticket;
    }

    private static int CurrentScopeSlot()
    {
        if (Depth <= 0) return NoScope;
        if (Depth > MaxStack) return MaskedScope;
        return StackSlot[Depth - 1];
    }

    /// <summary>
    /// 结束 shot scope：只有该 volley 世代仍活跃、且 ticket 属于当前 ResetEpoch 才 close/free，
    /// 只有栈顶确实是本 scope 才 pop。ReleaseAll 后旧 ticket（含掩蔽项）一律无操作。
    /// </summary>
    internal static void EndShot(ShotTicket ticket)
    {
        try
        {
            if (!ticket.Pushed) return;
            if (ticket.ResetEpoch != ResetEpoch) { StatStaleTicket++; return; }
            if (ticket.Slot >= 0 && ticket.Slot < MaxVolleys)
            {
                Volley volley = Volleys[ticket.Slot];
                if (volley != null && volley.Epoch != 0 && volley.Epoch == ticket.Epoch)
                {
                    volley.Closed = true;
                    if (volley.RefCount == 0) FreeVolleySlot(ticket.Slot);
                }
                else
                {
                    StatStaleTicket++;
                    Count("stale-scope-ticket");
                }
            }
            if (Depth <= 0) { Count("end-unbalanced"); return; }
            if (Depth != ticket.Depth) { Count("scope-depth-mismatch"); return; }
            int index = Depth - 1;
            if (index >= MaxStack)
            {
                Depth--;                                       // 溢出层（数组外）：按 LIFO 直接回收
                MaskOverflow = Depth > MaxStack;
                return;
            }
            if (StackSlot[index] == ticket.Slot && StackEpoch[index] == ticket.Epoch)
            {
                Depth--;
                MaskOverflow = Depth > MaxStack;
            }
            else
            {
                Count("scope-pop-mismatch");
            }
        }
        catch (Exception e)
        {
            Count("end-shot:" + e.GetType().Name);
        }
    }

    /// <summary>Arrow.OnEnable Prefix：退役旧生命的 lease 并废弃其未完成事务（不写任何原生字段）。</summary>
    internal static void OnArrowEnable(Arrow arrow)
    {
        try
        {
            int slot = FindLease(arrow);
            if (slot < 0) return;
            Leases[slot].TxFailed = true;
            RetireLease(slot);
        }
        catch (Exception e)
        {
            Count("arrow-enable:" + e.GetType().Name);
        }
    }

    /// <summary>
    /// Arrow.OnEnable Postfix：新生命绑定当前 shot scope。native Spawn 尚未写 arrow.archer，
    /// 故归属取 scope.Source 快照；资格在此时一次性记录。中途失败必须归还刚租的槽。
    /// </summary>
    internal static void OnArrowSpawned(Arrow arrow)
    {
        int leaseSlot = -1;
        bool leaseActive = false, refCounted = false, indexed = false;
        try
        {
            if (arrow == null || arrow.gameObject == null) return;
            if (MaskOverflow) return;
            int slot = CurrentScopeSlot();
            if (slot < 0) return;                                        // 无 scope / 掩蔽 scope
            Volley volley = Volleys[slot];
            if (volley == null || volley.Epoch == 0 || !volley.Eligible) return;
            bool heroOrigin = volley.Origin == OriginHero;
            if (!OriginEnabled(volley.Origin)) return;                    // 来源开关已关：不绑定新生命
            if (!arrow.isFireArrow && !HeroArcherCombat.AllowsNormalArrow(heroOrigin)) return;  // 希腊仍限真 fire
            // Read native properties before publishing a lease. A failed read cannot consume capacity.
            IntPtr arrowPtr = arrow.Pointer;
            int arrowGoId = GoId(arrow.gameObject);
            float bornAt = Time.time;
            if (arrowPtr == IntPtr.Zero || arrowGoId == 0 || FindLease(arrow) >= 0) return;
            leaseSlot = RentLease();
            if (leaseSlot < 0)
            {
                StatLeaseCap++;
                Count("lease-cap");
                return;
            }
            Lease lease = Leases[leaseSlot];
            lease.Arrow = arrow;
            lease.Ptr = arrowPtr;
            lease.GoId = arrowGoId;
            lease.Epoch = ++NextLeaseEpoch;
            lease.Eligible = true;
            lease.Origin = volley.Origin;
            lease.VolleySlot = slot;
            lease.VolleyEpoch = volley.Epoch;
            lease.Source = volley.Source;
            lease.SourcePtr = volley.SourcePtr;
            lease.SourceGoId = volley.SourceGoId;
            lease.World = volley.World; lease.Layer = volley.Layer; lease.Scene = volley.Scene;
            lease.BornAt = bornAt;
            lease.TxOpen = false;
            lease.TxSubstituted = false;
            lease.TxFailed = false;
            lease.TxTargetDamageable = IntPtr.Zero;
            lease.Active = true;
            leaseActive = true;
            ActiveLeases++;
            volley.RefCount++;
            refCounted = true;
            if (lease.GoId != 0) { LeaseByGoId[lease.GoId] = leaseSlot; indexed = true; }
            leaseSlot = -1;                                              // 已建立，不再回收
        }
        catch (Exception e)
        {
            Count("arrow-spawned:" + e.GetType().Name);
            if (leaseSlot >= 0)
            {
                Lease lease = Leases[leaseSlot];
                if (lease != null)
                {
                    if (indexed && lease.GoId != 0)
                    {
                        int mapped;
                        if (LeaseByGoId.TryGetValue(lease.GoId, out mapped) && mapped == leaseSlot) LeaseByGoId.Remove(lease.GoId);
                    }
                    if (refCounted && lease.VolleySlot >= 0 && lease.VolleySlot < MaxVolleys)
                    {
                        Volley volley = Volleys[lease.VolleySlot];
                        if (volley != null && volley.RefCount > 0) volley.RefCount--;
                    }
                    if (leaseActive)
                    {
                        lease.Active = false;
                        if (ActiveLeases > 0) ActiveLeases--;
                    }
                    lease.Arrow = null;
                    lease.Source = null;
                    lease.VolleySlot = NoScope;
                    lease.VolleyEpoch = 0;
                }
                ReturnLeaseSlot(leaseSlot);                              // 中途失败：归还槽，绝不泄漏
            }
        }
    }

    // ============================================================
    // 命中事务（既有 HitObject wrapper：Capture → BeginHit → native → EndHit/AbortHit）
    // ============================================================

    /// <summary>
    /// 命中前缀：只建立本次命中的不可变事务快照，**不修改任何原生字段**。
    /// </summary>
    internal static HitTicket BeginHit(Arrow arrow, GameObject target)
    {
        HitTicket ticket = default;
        try
        {
            if (arrow == null || arrow.gameObject == null) return ticket;
            if (!arrow.authorityActive) return ticket;                 // 与 native TryDamage 同一前提
            if (!NetworkBigBoss.HasWorldAuth) return ticket;           // 只主机/单机
            if (Time.timeScale <= 0f) return ticket;
            if (arrow._hasHit) return ticket;                          // 原生立即早退
            if (!EnsureContextFresh()) return ticket;
            if (!CurrentComponent(arrow)) return ticket;
            int slot = FindLease(arrow);
            if (slot < 0) return ticket;
            Lease lease = Leases[slot];
            if (!lease.Active || !lease.Eligible || lease.TxOpen) return ticket;
            if (!OriginEnabled(lease.Origin)) return ticket;           // 来源开关已关：不建立命中事务
            if (!arrow.isFireArrow && !HeroArcherCombat.AllowsNormalArrow(lease.Origin == OriginHero)) return ticket;
            if (!ContextMatches(lease)) return ticket;
            if (!SourceBound(arrow, lease)) return ticket;

            Vector2 point;
            try { point = arrow.transform.position; }
            catch (Exception e) { Count("point:" + e.GetType().Name); return ticket; }
            Damageable direct = ResolveDamageable(target);

            ticket.Valid = true;
            ticket.LeaseSlot = slot;
            ticket.LeaseEpoch = lease.Epoch;
            ticket.TxEpoch = ++NextTxEpoch;
            ticket.VolleySlot = lease.VolleySlot;
            ticket.VolleyEpoch = lease.VolleyEpoch;
            ticket.ArrowPtr = lease.Ptr;
            ticket.ArrowGoId = lease.GoId;
            ticket.HasPoint = true;
            ticket.Point = point;
            ticket.DirectDamageable = direct != null ? direct.Pointer : IntPtr.Zero;
            ticket.ResetEpoch = ResetEpoch;

            lease.TxEpoch = ticket.TxEpoch;
            lease.TxOpen = true;
            lease.TxSubstituted = false;
            lease.TxFailed = false;
            lease.TxTargetDamageable = ticket.DirectDamageable;
        }
        catch (Exception e)
        {
            Count("begin-hit:" + e.GetType().Name);
            ticket.Valid = false;
        }
        return ticket;
    }

    /// <summary>
    /// Arrow.TryDamage 窄前缀（由本文件下方 Harmony 类调用）：返回 true 表示放行原生。
    /// 只匹配「当前正在执行的命中事务 + 同一 arrow 生命 + 同一直接 Damageable 指针」。
    /// </summary>
    internal static bool TrySubstituteDirectDamage(Arrow arrow, Damageable damageable, ref bool result)
    {
        // 匹配阶段：任何异常都必须放行原生（绝不误伤未被替代的路径）。
        int slot;
        try
        {
            if (arrow == null || arrow.gameObject == null) return true;
            if (!arrow.isFireArrow) return true;                       // 普通箭没有 DOT 路径：直接伤害保持原生
            if (!EnsureContextFresh() || !CurrentComponent(arrow)) return true;
            slot = FindLease(arrow);
            if (slot < 0) return true;
            // 按**来源**查开关（不是全局 Greek 开关）：英雄在自己开关下打出的真 fire 箭（原生 FireAttacks buff）
            // 同样要取消持续灼烧；该来源关闭或没有合资格 lease 时一律放行原生。
            if (!OriginEnabled(Leases[slot].Origin)) return true;
            if (!ContextMatches(Leases[slot]) || !SourceBound(arrow, Leases[slot])) return true;
        }
        catch (Exception e)
        {
            Count("substitute-match:" + e.GetType().Name);
            return true;
        }

        Lease lease = Leases[slot];
        IntPtr direct = lease.TxTargetDamageable;
        if (!lease.Active || !lease.Eligible || !lease.TxOpen) return true;
        if (direct == IntPtr.Zero) return true;                    // 无具体直接目标：交给原生
        if (damageable == null) return true;
        if (damageable.Pointer != direct) return true;             // 非本次命中的直接目标：交给原生
        if (lease.Arrow == null || lease.Arrow.Pointer != lease.Ptr) return true;

        if (lease.TxSubstituted)
        {
            // 同一事务的重入调用：绝不重复直接伤害，也绝不回落原生 DOT。
            StatSubstituteReentry++;
            result = true;
            return false;
        }
        long leaseEpoch = lease.Epoch, txEpoch = lease.TxEpoch, resetEpoch = ResetEpoch;
        lease.TxSubstituted = true;                                // 先标记，异常时也不会回落原生

        // 替代阶段：逐字等价 native TryDamage 除持续灼烧外的全部分支；异常不吞（沿 Finalizer 收事务）。
        try
        {
            if (!arrow.authorityActive || damageable == null)
            {
                result = false;
                return false;
            }
            if (damageable.IsDamagedBy(arrow._damageSource))
            {
                int amount = arrow._perfect
                    ? unchecked(arrow.hitDamage * arrow.perfectDamageMultiplier)
                    : arrow.hitDamage;
                damageable.ReceiveDamage(amount, arrow.archer, arrow._damageSource);
            }
            result = true;
            StatSubstituted++;
            return false;
        }
        catch
        {
            // ReceiveDamage can synchronously recycle this slot and then throw.
            if (ResetEpoch == resetEpoch && lease.Active && lease.Epoch == leaseEpoch && lease.TxEpoch == txEpoch)
                lease.TxFailed = true;
            StatSubstituteFailed++;
            throw;
        }
    }

    /// <summary>
    /// 命中后缀：返回 bool（true = 本次命中已接受且可画/已画 FX）。
    /// 先锁定 accepted + 关事务，再做 AoE；AoE 异常不影响原箭直接伤害与返回值。
    /// </summary>
    internal static bool EndHit(Arrow arrow, HitTicket ticket)
    {
        try
        {
            if (!ticket.Valid) return false;
            Lease lease;
            if (!LeaseMatchesTicket(ticket, out lease)) { StatStaleTicket++; return false; }
            if (!lease.TxOpen) return false;
            bool accepted;
            try { accepted = arrow != null && arrow.gameObject != null && arrow._hasHit; }
            catch (Exception e) { Count("accepted:" + e.GetType().Name); accepted = false; }
            bool failed = lease.TxFailed;
            lease.TxOpen = false;                                  // 先关事务（不可变快照仍留在 ticket 里）
            if (!accepted || failed)
            {
                if (failed) StatSubstituteFailed++;
                return false;
            }
            if (BurstStillValid(arrow, ticket, out _)) ApplyBurst(arrow, ticket);
            if (LeaseMatchesTicket(ticket, out Lease again)) RetireLease(ticket.LeaseSlot);
            return true;
        }
        catch (Exception e)
        {
            Count("end-hit:" + e.GetType().Name);
            return false;
        }
    }

    /// <summary>命中 Finalizer：幂等地清掉自己这一个事务；绝不触碰新生命、绝不做 AoE。</summary>
    internal static void AbortHit(Arrow arrow, HitTicket ticket)
    {
        try
        {
            if (!ticket.Valid) return;
            Lease lease;
            if (!LeaseMatchesTicket(ticket, out lease)) return;
            if (!lease.TxOpen) return;
            lease.TxOpen = false;
            if (!lease.TxFailed) StatSubstituteFailed++;
            lease.TxFailed = true;                                 // 事务未正常结束：不 AoE
            Count("abort");
        }
        catch (Exception e)
        {
            Count("abort-hit:" + e.GetType().Name);
        }
    }

    // ============================================================
    // AoE
    // ============================================================

    /// <summary>爆发前/每个目标前的完整复核：ticket 世代 + lease 活跃 + volley 世代 + 权限 + world + source。</summary>
    private static bool BurstStillValid(Arrow arrow, HitTicket ticket, out Lease lease)
    {
        lease = null;
        try
        {
            if (!NetworkBigBoss.HasWorldAuth || Time.timeScale <= 0f) return false;
            if (arrow == null || arrow.gameObject == null) return false;
            if (!arrow.authorityActive) return false;
            if (!LeaseMatchesTicket(ticket, out lease)) { lease = null; return false; }
            if (!OriginEnabled(lease.Origin)) return false;            // 来源开关已关：在空中的箭不再爆发
            if (!arrow.isFireArrow && !HeroArcherCombat.AllowsNormalArrow(lease.Origin == OriginHero)) return false;
            if (ticket.VolleySlot < 0 || ticket.VolleySlot >= MaxVolleys) { lease = null; return false; }
            Volley volley = Volleys[ticket.VolleySlot];
            if (volley == null || volley.Epoch != ticket.VolleyEpoch) { lease = null; return false; }
            if (!ContextMatches(lease)) return false;
            if (!CurrentComponent(arrow)) return false;
            if (!Live(lease.Source) || !CurrentGO(lease.Source)) return false;
            return SourceBound(arrow, lease);
        }
        catch { lease = null; return false; }
    }

    private static Damageable ResolveDamageable(GameObject go)
    {
        try { return go != null ? go.GetComponentInParent<Damageable>() : null; }
        catch { return null; }
    }

    /// <summary>显式 layer mask + include triggers 的固定缓冲查询（不改全局 queriesHitTriggers）。</summary>
    private static bool EnsureQuery()
    {
        if (FilterReady) return true;
        try
        {
            string name = null;
            try { name = Layers.Enemies; } catch { }
            if (string.IsNullOrEmpty(name)) name = "Enemies";
            int layer = LayerMask.NameToLayer(name);
            if (layer < 0)
            {
                Count("layer-missing");
                return false;
            }
            EnemyLayer = layer;
            ContactFilter2D filter = new ContactFilter2D();
            filter.useTriggers = true;          // 显式包含 trigger 受击 collider
            filter.useLayerMask = true;
            filter.layerMask = 1 << layer;      // 只 Enemies 层
            filter.useDepth = false;
            filter.useNormalAngle = false;
            Filter = filter;
            if (Buffer == null) Buffer = new Il2CppReferenceArray<Collider2D>(MaxOverlap);
            FilterReady = true;
            return true;
        }
        catch (Exception e)
        {
            Count("query-init:" + e.GetType().Name);
            return false;
        }
    }

    private static void ClearBuffer()
    {
        if (Buffer == null) return;
        try
        {
            for (int i = 0; i < Buffer.Length; i++) Buffer[i] = null;   // 不留旧 world 的 collider 引用
        }
        catch { }
    }

    /// <summary>burst 快照条目：本次物理查询当刻的目标 + life token + 其 collider（阶段 2 复核对）。</summary>
    private struct StagedTarget
    {
        internal Collider2D Collider;
        internal Damageable Target;
        internal CombatTargetToken Token;
    }

    /// <summary>
    /// 一次爆发：命中点快照 + 固定缓冲查询 + 两阶段处理。
    ///
    /// 阶段 1「快照」只做过滤 / 跳过本 volley 已结算 / burst 内**局部**去重 / 取 life token，
    /// 存进与物理查询同界（<see cref="MaxOverlap"/> = 64）的复用数组，**不提交、不登记 volley 账本、
    /// 也不按 32 提交名额截断**；
    /// 阶段 2「提交」逐条重新跑完整复核 —— <see cref="BurstStillValid"/>（世代/权限/world/来源）、
    /// 完整 <see cref="TryResolveTarget"/>（原 collider、active、Enemy.enabled、friendly、免疫、直接目标、world）、
    /// 同一 target、同一 life token、半径、本 volley 已结算 —— 全部通过后才消耗 32 名额并**紧贴
    /// <see cref="CombatDamage.Submit"/> 之前**登记 volley 去重。
    /// 这样：上一目标回调造成的身份/资格/位置变化不会让后续目标受伤；失效而未提交的目标既不记账
    /// 也不白占最后一个名额（同一 volley 的后续箭仍可在其恢复有效后命中）；同一次查询的旧 collider
    /// 也不会打到本 burst 中才回池重生的新 life（新 life 留给后续 burst 判定）；Faulted 已消费不重试。
    /// </summary>
    private static void ApplyBurst(Arrow arrow, HitTicket ticket)
    {
        if (BurstDepth > 0) { StatReentry++; Count("reentry"); return; }
        BurstDepth++;                                              // 只有本层最外 finally 递减
        int staged = 0;
        try
        {
            if (!ticket.HasPoint) return;
            if (!EnsureQuery()) return;
            Vector2 point = ticket.Point;
            int count;
            try { count = Physics2D.OverlapCircle(point, Radius, Filter, Buffer); }
            catch (Exception e)
            {
                Count("query:" + e.GetType().Name);
                return;
            }
            if (count <= 0) return;
            if (count >= MaxOverlap) { StatBufferFull++; Count("buffer-full"); }
            if (count > MaxOverlap) count = MaxOverlap;
            if (Staging == null) Staging = new StagedTarget[MaxOverlap];

            // 阶段 1（快照）：过滤 + 跳过本 volley 已结算 + burst 内局部去重 + life 记录。
            // 顺序要求：先解析 token 并跳过 VolleyContains/StagingContains（旧目标/重复目标不占快照），
            // **不按 32 提交名额截断** —— 名额留给阶段 2 在“确认真实有效”之后再消耗，否则前面失效的
            // 快照项会白占最后一个名额、把后面的新目标挤掉。staged ≤ count ≤ MaxOverlap（现成查询上限）。
            for (int i = 0; i < count; i++)
            {
                if (!BurstStillValid(arrow, ticket, out _)) break;              // 逐目标重核
                int volleySlot = ticket.VolleySlot;
                Volley volley = volleySlot >= 0 && volleySlot < MaxVolleys ? Volleys[volleySlot] : null;
                if (volley == null || volley.Epoch != ticket.VolleyEpoch) { Count("volley-recycled"); break; }
                Collider2D collider = Buffer[i];
                if (collider == null) continue;
                if (!WithinRadius(collider, point)) continue;                   // 查询缓冲可能已被回调改变
                Damageable target;
                if (!TryResolveTarget(collider, ticket.DirectDamageable, out target)) continue;
                CombatTargetToken token;
                if (!CombatTargetLife.TryResolve(target, out token))
                {
                    StatLifeDegraded++;                                         // 显式放弃：绝不退回指针去重
                    Count("life-degraded");
                    continue;
                }
                if (VolleyContains(volley, token)) continue;                    // 已结算目标不占快照（阶段 2 仍会终检）
                if (StagingContains(staged, token)) continue;                   // 局部去重（不占 volley 账本）
                Staging[staged].Collider = collider;
                Staging[staged].Target = target;
                Staging[staged].Token = token;
                staged++;
            }
            if (staged == 0) { StatNoTarget++; return; }

            // 阶段 2（提交）：先跳掉所有失效/旧 token 的快照项 —— 世代/权限/world（BurstStillValid）、
            // 完整 TryResolveTarget、同一目标、同一 life、半径、本 volley 已结算 —— 通过者才消耗 32 名额
            // （名额不足则计数并停止本次爆发，原策略不变）。因此名额不会被“打不到的目标”白占；
            // Faulted 不阻塞后续独立目标。
            int applied = 0;
            for (int i = 0; i < staged; i++)
            {
                if (!BurstStillValid(arrow, ticket, out Lease live)) break;
                int volleySlot = ticket.VolleySlot;
                Volley volley = volleySlot >= 0 && volleySlot < MaxVolleys ? Volleys[volleySlot] : null;
                if (volley == null || volley.Epoch != ticket.VolleyEpoch) { Count("volley-recycled"); break; }
                Collider2D collider = Staging[i].Collider;
                Damageable stagedTarget = Staging[i].Target;
                if (collider == null || stagedTarget == null) continue;
                Damageable target;
                if (!TryResolveTarget(collider, ticket.DirectDamageable, out target)) continue;   // 完整既有过滤
                if (!Same(target, stagedTarget)) continue;                                       // 同快照目标
                CombatTargetToken now;
                if (!CombatTargetLife.TryResolve(target, out now))
                {
                    StatLifeDegraded++;
                    Count("life-degraded");
                    continue;
                }
                if (!CombatTargetToken.Matches(Staging[i].Token, now)) continue;   // 本 burst 中回池重生 → 后续 burst 判定
                if (!WithinRadius(collider, point)) continue;                      // 每次实际伤害前复核距离
                if (volley.Targets == null) volley.Targets = new CombatTargetToken[MaxVolleyTargets];
                if (VolleyContains(volley, now)) continue;                         // 本 volley 已结算过该 life
                if (volley.TargetCount >= MaxVolleyTargets) { StatVolleyCap++; Count("volley-cap"); break; }
                volley.Targets[volley.TargetCount++] = now;                        // 紧贴提交前登记
                GameObject source = live.Source;
                CombatDamageResult outcome = CombatDamage.Submit(target, BurstDamage, source, DamageSource.Fire);
                if (outcome == CombatDamageResult.Submitted) applied++;
                else if (outcome == CombatDamageResult.Faulted) StatDamageFaulted++;
            }
            if (applied > 0) { StatBursts++; StatDamage += applied; }
            else StatNoTarget++;
        }
        catch (Exception e)
        {
            Count("burst:" + e.GetType().Name);
        }
        finally
        {
            ClearStaging(staged);                                   // 不跨 burst 持有 Unity 引用
            ClearBuffer();                                          // 查询抛错/部分写入也必须清引用
            BurstDepth--;
        }
    }

    /// <summary>burst 局部去重（只查本 burst 快照，不占 volley 账本）。</summary>
    private static bool StagingContains(int count, in CombatTargetToken token)
    {
        for (int i = 0; i < count; i++)
        {
            if (CombatTargetToken.Matches(Staging[i].Token, token)) return true;
        }
        return false;
    }

    /// <summary>清掉 burst 快照里的目标/collider 引用（销毁/回池后绝不留旧引用）。</summary>
    private static void ClearStaging(int count)
    {
        if (Staging == null || count <= 0) return;
        if (count > Staging.Length) count = Staging.Length;
        for (int i = 0; i < count; i++) Staging[i] = default;
    }

    private static bool WithinRadius(Collider2D collider, Vector2 point)
    {
        try
        {
            Vector2 closest = collider.ClosestPoint(point);
            float dx = closest.x - point.x, dy = closest.y - point.y;
            return dx * dx + dy * dy <= Radius * Radius + 1e-4f;
        }
        catch (Exception e)
        {
            Count("closest:" + e.GetType().Name);
            return false;
        }
    }

    private static bool VolleyContains(Volley volley, in CombatTargetToken token)
    {
        if (volley.Targets == null) return false;
        for (int i = 0; i < volley.TargetCount; i++)
        {
            if (CombatTargetToken.Matches(volley.Targets[i], token)) return true;
        }
        return false;
    }

    /// <summary>
    /// 命中候选过滤：collider/组件均需 enabled 且属于当前 world；Enemies 层 + 活跃 Enemy 组件 +
    /// 存活非无敌 Damageable + 非火免疫 + 非直接命中 + 非友军巨魔。
    /// </summary>
    private static bool TryResolveTarget(Collider2D collider, IntPtr directDamageable, out Damageable target)
    {
        target = null;
        try
        {
            if (!collider.enabled) return false;
            GameObject go = collider.gameObject;
            if (go == null || !go.activeInHierarchy) return false;
            if (EnemyLayer >= 0 && go.layer != EnemyLayer) return false;
            if (!CurrentComponent(collider)) return false;
            Damageable damageable = collider.GetComponentInParent<Damageable>();
            if (damageable == null || !damageable.enabled) return false;
            if (damageable.gameObject == null || !damageable.gameObject.activeInHierarchy) return false;
            if (damageable.isDead || damageable.invulnerable) return false;
            if (!CurrentComponent(damageable)) return false;
            if (damageable.Pointer == directDamageable) return false;        // 直接命中不重复 AoE
            if (!damageable.IsDamagedBy(DamageSource.Fire)) return false;    // 尊重火免疫
            Enemy enemy = collider.GetComponentInParent<Enemy>();
            if (enemy == null || !enemy.enabled || enemy.gameObject == null || !enemy.gameObject.activeInHierarchy) return false;
            if (!CurrentComponent(enemy)) return false;
            if (collider.GetComponentInParent<FriendlyTroll>() != null) return false;   // 友军巨魔
            target = damageable;
            return true;
        }
        catch (Exception e)
        {
            Count("target:" + e.GetType().Name);
            return false;
        }
    }
}

// ============================================================
// Harmony 钩子（只含已核 native 目标；helper 全在上方，避免按名字被自动发现）
// ============================================================

/// <summary>
/// 发射 scope：Prefix 开启（嵌套安全），Finalizer 结束。Finalizer 在所有 Postfix
/// （含既有 scatter 额外箭）之后运行，因此额外箭天然归入同一 volley 的 scope。
/// 不改写原方法执行、不吞异常（原异常原样返回）。
/// </summary>
[HarmonyPatch(typeof(ArrowAttack), "FireArrowInternal")]
internal static class ArrowAttack_FireArrowInternal_GreekImpact_Patch
{
    [HarmonyPrefix]
    private static void Prefix(ArrowAttack __instance, GameObject source, out PatchArcher_GreekImpact.ShotTicket __state)
    {
        __state = default;
        try { __state = PatchArcher_GreekImpact.BeginShot(__instance, source); }
        catch { }
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(PatchArcher_GreekImpact.ShotTicket __state, Exception __exception)
    {
        try { PatchArcher_GreekImpact.EndShot(__state); }
        catch { }
        return __exception;
    }
}

/// <summary>
/// 箭生命窗口：Prefix 退役旧世代 lease（废弃其未完成事务），Postfix 绑定新生命到当前 shot scope。
/// OnEnable 内绝不发 RPC、绝不写原生字段。
/// </summary>
[HarmonyPatch(typeof(Arrow), "OnEnable")]
internal static class Arrow_OnEnable_GreekImpact_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Arrow __instance)
    {
        try { PatchArcher_GreekImpact.OnArrowEnable(__instance); }
        catch { }
    }

    [HarmonyPostfix]
    private static void Postfix(Arrow __instance)
    {
        try { PatchArcher_GreekImpact.OnArrowSpawned(__instance); }
        catch { }
    }
}

/// <summary>
/// 窄伤害替代：只替代「当前命中事务 + 同一直接目标」的那一次 TryDamage，其余一律放行原生。
/// 前缀内不吞异常：替代阶段出错时异常上抛，由 HitObject 侧 Finalizer 收事务（绝不回落原生 DOT）。
/// </summary>
[HarmonyPatch(typeof(Arrow), "TryDamage")]
internal static class Arrow_TryDamage_GreekImpact_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(Arrow __instance, Damageable damageable, ref bool __result)
    {
        return PatchArcher_GreekImpact.TrySubstituteDirectDamage(__instance, damageable, ref __result);
    }
}
