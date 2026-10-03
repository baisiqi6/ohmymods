using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 局部地面/空间验证：地面掩码来自当前世界真实 GroundCollider 所在层，
/// 落点/站位用一次局部射线确认（绝不用 GroundCollider.bounds.max.y 冒充"处处可站"），
/// 净空用原生 "Obstacles" 掩码的点查询。任何读不到/异常一律 fail-closed。
/// </summary>
internal static class CoinCourierGround
{
    private const int ProbeCapacity = 4;

    private static int _groundMask;
    private static int _obstacleMask;
    private static Il2CppStructArray<RaycastHit2D> _probeHits;

    internal static void Invalidate() { _groundMask = 0; _obstacleMask = 0; }

    private static bool EnsureMasks()
    {
        if (_groundMask != 0) return true;
        try
        {
            BoxCollider2D ground = World.GroundCollider;
            if (ground == null) return false;
            GameObject root = ground.gameObject;
            if (root == null) return false;
            int layer = root.layer;
            if (layer < 0 || layer > 31) return false;
            _groundMask = 1 << layer;
            if (_obstacleMask == 0) _obstacleMask = LayerMask.GetMask("Obstacles");
            return _groundMask != 0;
        }
        catch (Exception)
        {
            _groundMask = 0;
            return false;
        }
    }

    /// <summary>在 x 处向下做一次局部线段查询，返回真实地面表面高度（最高命中面）。</summary>
    internal static bool TryProbe(float x, out float groundY)
    {
        groundY = 0f;
        if (!float.IsFinite(x) || !EnsureMasks()) return false;
        try
        {
            BoxCollider2D ground = World.GroundCollider;
            if (ground == null) return false;
            float top = ground.bounds.max.y;
            if (!float.IsFinite(top)) return false;
            if (_probeHits == null || _probeHits.Length != ProbeCapacity)
                _probeHits = new Il2CppStructArray<RaycastHit2D>(ProbeCapacity);
            // 与弹道侧同一已验证重载：LinecastNonAlloc(Vector2, Vector2, Il2CppStructArray<RaycastHit2D>, int)。
            float startY = top + 2f;
            int hits = Physics2D.LinecastNonAlloc(new Vector2(x, startY),
                new Vector2(x, top - CoinCourierTiming.GroundProbeDepth), _probeHits, _groundMask);
            if (hits <= 0 || hits > _probeHits.Length) return false;
            float best = float.NegativeInfinity;
            for (int i = 0; i < hits; i++)
            {
                RaycastHit2D hit = _probeHits[i];
                if (hit.collider == null) continue;
                // 竖直向下线段：命中高度 = 起点高度 - 沿途距离（只用已验证的 distance 字段）。
                float y = startY - hit.distance;
                if (float.IsFinite(y) && y > best) best = y;
            }
            if (!float.IsFinite(best)) return false;
            groundY = best;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 该点是否没有真实障碍物（墙/建筑）。用一次完整局部列表查询（不截断），只认
    /// 非触发体且 bounds 覆盖该点的 collider；触发体（无关 FX）不阻站位。
    /// 掩码/查询不可用时 fail-closed。
    /// </summary>
    internal static bool ObstacleFree(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y)) return false;
        if (!EnsureMasks()) return false;
        if (_obstacleMask == 0) return false;
        try
        {
            const float half = 0.05f;
            var hits = Physics2D.OverlapAreaAll(new Vector2(x - half, y - half),
                new Vector2(x + half, y + half), _obstacleMask);
            if (hits == null) return false;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D collider = hits[i];
                if (collider == null || collider.isTrigger) continue;
                Bounds bounds = collider.bounds;
                if (!float.IsFinite(bounds.min.x) || !float.IsFinite(bounds.max.x)
                    || !float.IsFinite(bounds.min.y) || !float.IsFinite(bounds.max.y)) continue;
                if (x >= bounds.min.x && x <= bounds.max.x && y >= bounds.min.y && y <= bounds.max.y)
                    return false;
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>地面存在且落点净空：所有显形/站位都以此为准。</summary>
    internal static bool TryStandable(float x, out float groundY)
    {
        groundY = 0f;
        if (!TryProbe(x, out groundY)) return false;
        return ObstacleFree(x, groundY + CoinCourierTiming.LandingClearance);
    }

    /// <summary>
    /// 固定主城锚（招募标记与已招哥布林出生/回家共用）：x = 营火 + <see cref="CoinCourierTiming.HomeOffsetX"/>，
    /// y 只用该点一次 <see cref="TryProbe"/> 读当前真实地面。没有候选窗口/建筑排除/净空/付款避让；
    /// 世界或地面不可读时返回 false——调用方等待，绝不猜高度、绝不重选点。
    /// </summary>
    internal static bool TryResolveHome(Kingdom kingdom, out float x, out float y)
    {
        x = 0f;
        y = 0f;
        if (kingdom == null) return false;
        float campfire;
        try { campfire = kingdom.campfirePosition; } catch (Exception) { return false; }
        if (!float.IsFinite(campfire)) return false;
        float homeX = campfire + CoinCourierTiming.HomeOffsetX;
        if (!float.IsFinite(homeX)) return false;
        if (!TryProbe(homeX, out float groundY) || !float.IsFinite(groundY)) return false;
        x = homeX;
        y = groundY;
        return true;
    }
}

/// <summary>
/// 金币哥布林运行时（唯一行为所有者）：银行等待 → 左侧逐枚取币 → 起跳离场 →
/// 目标外侧两人高显形 → 下落/落地缓冲 → 短跑近身 → 逐枚配送 → 起跳下一处/回银行。
///
/// 单状态机持有自己的仅视觉 root 运动（无 Character/Damageable/Wallet/GreedAI/Mover，
/// 不改根缩放做动画）；所有经济只经 <see cref="CoinCourierEconomy"/> 两个窄入口，
/// 所有资格先过离线+权威+当前可玩世界+存档 owner Ready/Owned 闸门。未绑定真实
/// 存档 owner 时默认不生成角色、不收费、不取币、不配送。
///
/// <see cref="Bind"/> 是本模块与存档 owner 之间唯一的接线口（默认 null）；
/// 场景卸载/功能关闭/回池只清场景 GO，绝不改动 campaign 的 Owned/钱袋。
/// </summary>
internal static class CoinCourierRuntime
{
    private enum LeisureMotion { Pause, Outbound, Returning }

    private sealed class Scene
    {
        internal CoinCourierView View;
        internal Transform Probe;
        internal Transform Layer;
        internal CoinCourierPhase Phase = CoinCourierPhase.Wait;
        internal float Elapsed;
        internal bool ActionConsumed;
        internal Vector3 Position;
        internal float HomeX;
        internal float HomeY;
        internal float CampfireX;
        internal float GroundY;
        internal float SpawnY;
        internal int Facing = 1;
        internal bool Visible = true;
        internal CoinCourierTravel Travel = CoinCourierTravel.Bank;
        internal Knight Target;
        internal long TargetLife;
        internal int TargetSide;
        internal CoinCourierRules.VisitPlan Plan;
        internal int Sent;
        internal float TargetX;
        internal float LandingX;
        internal float VisitDeadlineAt;
        internal float VisitMoved;
        internal float CheckAt;
        internal LeisureMotion LeisureMotion;
        internal float LeisureTargetX;
        internal float LeisurePauseRemaining;
        internal float LeisurePoseSeconds;
        internal float LeisureWalkSeconds;
        internal float LeisureBlockedRemaining;
        internal int LeisureSide = 1;
        internal bool LeisureDaytime;
        internal bool StopDeliver;
        internal bool AbortVisit;
    }

    private static readonly Scene S = new();
    private static readonly Dictionary<long, float> NextEligibleAt = new(16);
    private static readonly List<long> PruneBuffer = new(16);
    private static readonly HashSet<string> LoggedOnce = new();
    private static ICoinCourierCampaignState _state;
    private static IntPtr _worldPtr;
    private static IntPtr _layerPtr;
    private static int _lastServedSide;
    private static int _enemyMask;
    private static float _collectBlockedUntil;

    /// <summary>
    /// 原地休闲动作时钟（2026-10-03 用户要求空闲活泼）：Idle 段 2s + Leisure 段
    /// （4 帧 × 0.6s = 2.4s）合计一个 4.4s 循环，回绕长度沿用既有的同值节奏常量
    /// <see cref="CoinCourierTiming.LeisurePauseSeconds"/>。驻留与受阻时只由该时钟决定帧；
    /// 绝不每帧归零、绝不 NaN、暂停（delta=0）不推进，也不依赖外部重置。
    /// </summary>
    private const float LeisureIdlePoseSeconds = 2f;
    private const float LeisurePoseLoopSeconds = CoinCourierTiming.LeisurePauseSeconds;

    /// <summary>同 world 纯视觉重建的下一次尝试时刻（有界重试：失败绝不每帧重建 GO）。</summary>
    private static float _viewRetryAt;
    private static bool _recruitmentLocked;
    private static bool _recruitmentLockLogged;
    private static bool _frozenLogged;
    private static CoinCourierFxHandle _fxHandle;

    /// <summary>唯一存档接线口（由 campaign 保存 owner 显式绑定；默认 null = 未接线）。</summary>
    internal static ICoinCourierCampaignState State => _state;

    /// <summary>
    /// 当前身份是否已确认（窄只读接口，复用唯一详细 Availability 投影，不新增判定/缓存）：
    /// NotBound/Unsupported/Closed/ReadFault 为否，其余（含 PersistenceFault/暂停/保存等交易门）为是。
    /// </summary>
    internal static bool IdentityConfirmed
    {
        get
        {
            try
            {
                if (!TryReadStatusAvailability(_state, out CoinCourierAvailabilityInfo availability)) return false;
                switch (availability.Kind)
                {
                    case CoinCourierAvailability.NotBound:
                    case CoinCourierAvailability.Unsupported:
                    case CoinCourierAvailability.Closed:
                    case CoinCourierAvailability.ReadFault:
                        return false;
                    default:
                        return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    internal static void Bind(ICoinCourierCampaignState state)
    {
        if (!ReferenceEquals(_state, state))
        {
            // 新 owner/新世代：上一世代的"记录结果未知"不再约束本次绑定。
            _recruitmentLocked = false;
            _recruitmentLockLogged = false;
        }
        _state = state;
    }

    internal static void Unbind(ICoinCourierCampaignState state)
    {
        if (ReferenceEquals(_state, state)) _state = null;
    }

    /// <summary>招募记录结果不可判定后锁定本付款上下文（不退款、不重购）。</summary>
    internal static bool RecruitmentLocked => _recruitmentLocked;

    internal static void LatchRecruitment(string reason)
    {
        _recruitmentLocked = true;
        if (_recruitmentLockLogged) return;
        _recruitmentLockLogged = true;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource?.LogError("[CoinCourier] recruitment context locked: " + reason);
        }
        catch (Exception)
        {
        }
    }

    internal static bool RecruitmentOwned
        => CoinCourierCampaignAccess.TryReadOwned(_state, out bool owned) && owned;

    /// <summary>招募点是否可以开放：owner 已绑定、明确 Ready、尚未拥有、上下文未锁定。</summary>
    internal static bool CanOpenRecruitment()
    {
        if (_recruitmentLocked) return false;
        ICoinCourierCampaignState state = _state;
        if (state == null) return false;
        if (!CoinCourierCampaignAccess.TryReadReady(state, out bool ready) || !ready) return false;
        if (!CoinCourierCampaignAccess.TryReadOwned(state, out bool owned)) return false;
        return !owned;
    }

    internal static bool FeatureEnabled
    {
        get
        {
            try
            {
                return ModConfig.Enabled != null && ModConfig.Enabled.Value
                    && ModConfig.CoinCourierEnabled != null && ModConfig.CoinCourierEnabled.Value;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    internal static int RecruitPrice => ReadInt(ModConfig.CoinCourierRecruitPrice, 1, 20, 8);

    internal static int PurseCapacity => ReadInt(ModConfig.CoinCourierPurseCapacity, 1, 40, 12);

    private static int MaxCoinsPerVisit => ReadInt(ModConfig.CoinCourierMaxCoinsPerVisit, 1, 12, 4);

    private static float KnightCooldown
    {
        get
        {
            try
            {
                ConfigEntry<float> entry = ModConfig.CoinCourierKnightCooldown;
                if (entry == null) return 15f;
                float value = entry.Value;
                if (!float.IsFinite(value)) return 15f;
                return value < 0f ? 0f : value > 120f ? 120f : value;
            }
            catch (Exception)
            {
                return 15f;
            }
        }
    }

    private static int ReadInt(ConfigEntry<int> entry, int min, int max, int fallback)
    {
        try
        {
            if (entry == null) return fallback;
            int value = entry.Value;
            return value < min ? min : value > max ? max : value;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    /// <summary>
    /// 面板状态文本（唯一只读投影）：身份与存档故障优先于暂停/保存等交易门；暂停只说
    /// "暂不可交易"，绝不当成存档损坏。<see cref="RecruitmentStatusText"/> 与它共用同一
    /// 详细评估（owner 的唯一 Availability），不新增第二套游戏条件、不缓存 Ready。
    /// </summary>
    internal static string StatusText => DescribeStatus(false);

    /// <summary>招募点/商店行的同一投影（招募语义：只回答能否付款/是否已拥有）。</summary>
    internal static string RecruitmentStatusText() => DescribeStatus(true);

    private static string DescribeStatus(bool recruitment)
    {
        try
        {
            if (!FeatureEnabled) return "已关闭";
            ICoinCourierCampaignState state = _state;
            if (!TryReadStatusAvailability(state, out CoinCourierAvailabilityInfo availability))
                return recruitment ? "存档状态不可读 · 暂不可招募" : "存档状态不可读 · 交易已冻结";
            // 身份/模式/读取故障先行：NotBound/Unsupported/Closed/readfault 绝不读取旧 Owned/Purse/锁定。
            switch (availability.Kind)
            {
                case CoinCourierAvailability.Closed:
                case CoinCourierAvailability.ReadFault:
                    return FaultText(availability, recruitment);
                case CoinCourierAvailability.NotBound:
                    return recruitment ? "未接线或身份待确认 · 暂不可招募" : "未接线或身份待确认 · 交易已冻结";
                case CoinCourierAvailability.Unsupported:
                    return recruitment ? "当前存档模式不可用 · 暂不可招募" : "当前存档模式不可用 · 交易已冻结";
            }
            // 身份已确认（PersistenceFault 也算）：未知投影优先于暂停/保存等交易门，两者可同时出现。
            UnknownProjection unknown = UnknownProjection.Read(state, _recruitmentLocked);
            bool ownedReadable = CoinCourierCampaignAccess.TryReadOwned(state, out bool owned);
            string lockText = unknown.RecruitmentLocked
                ? (recruitment ? "招募结果待确认 · 已锁定（不退款不重试）" : "招募记录结果未知 · 已锁定（不退款不重试）")
                : null;
            string fundsText = unknown.PurseUnknown
                ? FaultKindText(unknown.PurseFaultKind) + " · 已冻结（余额保留 " + unknown.PurseCoins + "）"
                : null;
            string persistenceText = availability.Kind == CoinCourierAvailability.PersistenceFault
                ? "存档捕获故障" + DetailSuffix(availability.Detail)
                : null;
            string blocker = BlockerText(availability.Kind);
            if (recruitment)
            {
                if (lockText != null)
                    return JoinFault(lockText, persistenceText, blocker == null ? null : blocker + " · 暂不可招募", null);
            }
            else if (unknown.RecruitmentLocked || unknown.PurseUnknown)
            {
                string lead = unknown.RecruitmentLocked ? lockText
                    : ownedReadable && owned ? "已招募"
                    : null;
                return JoinFault(lead, fundsText, persistenceText, blocker);
            }
            if (persistenceText != null)
                return recruitment ? persistenceText + " · 暂不可招募" : persistenceText + " · 交易已冻结";
            if (!ownedReadable)
                return recruitment ? "招募状态不可读 · 暂不可招募" : "存档状态不可读 · 交易已冻结";
            CoinCourierPurse purse = null;
            if (owned && !CoinCourierCampaignAccess.TryReadPurse(state, out purse))
                return "已招募 · 钱袋不可读";
            string baseText;
            if (owned)
                baseText = recruitment ? "已招募" : "已招募 · 钱袋 " + purse.Coins + "/" + PurseCapacity;
            else
                baseText = recruitment ? "招募点：" + RecruitPrice + " 金币" : "未招募 · 招募价 " + RecruitPrice + " 币";
            if (blocker == null) return baseText;
            if (owned) return baseText + " · " + blocker + (recruitment ? " · 暂不可招募" : "");
            return recruitment ? blocker + " · 暂不可招募" : baseText + " · " + blocker;
        }
        catch (Exception)
        {
            return "状态不可用";
        }
    }

    /// <summary>
    /// 当前已确认身份的只读未知投影（唯一实现，状态/国库两行共用的读取；行内呈现各自取舍）：
    /// 招募锁 + 钱袋未决故障。只读、不缓存、不新增游戏条件；正常在途 pending 不算 unknown。
    /// 读取失败按"无未知"处理，绝不据此改变交易规则。
    /// </summary>
    private readonly struct UnknownProjection
    {
        internal readonly bool RecruitmentLocked;
        internal readonly bool PurseUnknown;
        internal readonly int PurseCoins;
        internal readonly CoinCourierFaultKind PurseFaultKind;

        private UnknownProjection(bool recruitmentLocked, bool purseUnknown, int purseCoins,
            CoinCourierFaultKind purseFaultKind)
        {
            RecruitmentLocked = recruitmentLocked;
            PurseUnknown = purseUnknown;
            PurseCoins = purseCoins;
            PurseFaultKind = purseFaultKind;
        }

        internal static UnknownProjection Read(ICoinCourierCampaignState state, bool recruitmentLocked)
        {
            if (CoinCourierCampaignAccess.TryReadPurse(state, out CoinCourierPurse purse) && purse.IsFaulted)
                return new UnknownProjection(recruitmentLocked, true, purse.Coins, purse.Fault.Kind);
            return new UnknownProjection(recruitmentLocked, false, 0, CoinCourierFaultKind.None);
        }
    }

    /// <summary>钱袋未决故障的如实命名（取币/配送两种未知），绝不猜结果。</summary>
    private static string FaultKindText(CoinCourierFaultKind kind)
        => kind == CoinCourierFaultKind.BagUnknown ? "取币结果未知" : "配送结果未知";

    /// <summary>把非空片段按固定顺序拼成一行（避免数组/params 分配；UI 专用）。</summary>
    private static string JoinFault(string first, string second, string third, string fourth)
    {
        string result = null;
        if (first != null) result = first;
        if (second != null) result = result == null ? second : result + " · " + second;
        if (third != null) result = result == null ? third : result + " · " + third;
        if (fourth != null) result = result == null ? fourth : result + " · " + fourth;
        return result;
    }

    /// <summary>
    /// 状态文案的唯一评估读取：state 存在时读它；state==null 时即时向唯一 owner
    /// （Persistence）核对"当前 global 是否已关闭"，否则 NotBound。不缓存、不发布。
    /// </summary>
    private static bool TryReadStatusAvailability(ICoinCourierCampaignState state, out CoinCourierAvailabilityInfo info)
    {
        if (state == null)
        {
            if (CoinCourierPersistence.TryGetCurrentClosedDiagnostic(out info)) return true;
            info = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.NotBound);
            return true;
        }
        return CoinCourierCampaignAccess.TryReadAvailability(state, out info);
    }

    /// <summary>交易门的中性说明：只有精确 Menu 或 Playing 且时标暂停才叫"暂停"。</summary>
    private static string BlockerText(CoinCourierAvailability kind)
    {
        switch (kind)
        {
            case CoinCourierAvailability.Ready:
            case CoinCourierAvailability.PersistenceFault:
            case CoinCourierAvailability.Closed:
            case CoinCourierAvailability.ReadFault:
                return null;
            case CoinCourierAvailability.Online: return "仅限离线单机";
            case CoinCourierAvailability.NoAuthority: return "当前无主机权限";
            case CoinCourierAvailability.WorldUnavailable: return "世界未就绪";
            case CoinCourierAvailability.Loading: return "游戏加载中";
            case CoinCourierAvailability.Menu:
            case CoinCourierAvailability.Paused: return "游戏已暂停";
            case CoinCourierAvailability.Saving: return "保存中";
            case CoinCourierAvailability.NotBound: return "存档身份待确认";
            case CoinCourierAvailability.Unsupported: return "当前存档模式不可用";
            default: return "暂不可交易";
        }
    }

    /// <summary>身份/存档故障文本：真实故障绝不降级成"暂停后可恢复"。</summary>
    private static string FaultText(CoinCourierAvailabilityInfo info, bool recruitment)
    {
        string tail = recruitment ? "暂不可招募" : "交易已冻结";
        switch (info.Kind)
        {
            case CoinCourierAvailability.Closed:
                return "存档记录损坏" + DetailSuffix(info.Detail) + " · " + tail;
            case CoinCourierAvailability.PersistenceFault:
                return "存档捕获故障" + DetailSuffix(info.Detail) + " · " + tail;
            default:
                return "存档状态不可读 · " + tail;
        }
    }

    private static string DetailSuffix(string detail)
    {
        if (string.IsNullOrEmpty(detail)) return "";
        return "（" + (detail.Length > 48 ? detail.Substring(0, 48) : detail) + "）";
    }

    /// <summary>
    /// 国库装币状态的独立解释：先复用同一详细评估的阻断投影与同一未知投影（资金未决优先），
    /// 全部通过后才报告国库登记是否已连接——"已连接"不承诺可取或余额充足，也不参与任何
    /// 交易门；读取异常只影响本行文本。产品文案不暴露内部实现 ID。
    /// </summary>
    internal static string TreasuryStatusText
    {
        get
        {
            try
            {
                if (!FeatureEnabled) return "已关闭";
                ICoinCourierCampaignState state = _state;
                if (!TryReadStatusAvailability(state, out CoinCourierAvailabilityInfo availability))
                    return "存档状态不可读 · 装袋已冻结";
                switch (availability.Kind)
                {
                    case CoinCourierAvailability.Closed:
                        return "存档记录损坏" + DetailSuffix(availability.Detail) + " · 装袋已冻结";
                    case CoinCourierAvailability.ReadFault:
                        return "存档状态不可读 · 装袋已冻结";
                    case CoinCourierAvailability.NotBound:
                        return "存档身份待确认 · 装袋已冻结";
                    case CoinCourierAvailability.Unsupported:
                        return "当前存档模式不可用 · 装袋已冻结";
                }
                // 身份已确认：资金未决（unknown）优先于暂停/保存/连接状态。
                UnknownProjection unknown = UnknownProjection.Read(state, false);
                string fundsText = unknown.PurseUnknown
                    ? FaultKindText(unknown.PurseFaultKind) + " · 已冻结（余额保留 " + unknown.PurseCoins + "）"
                    : null;
                string persistenceText = availability.Kind == CoinCourierAvailability.PersistenceFault
                    ? "存档捕获故障" + DetailSuffix(availability.Detail)
                    : null;
                string blocker = BlockerText(availability.Kind);
                if (fundsText != null)
                    return JoinFault(fundsText, persistenceText, blocker, null);
                if (persistenceText != null)
                    return JoinFault(persistenceText, "装袋已冻结", blocker, null);
                if (blocker != null) return blocker + " · 暂不能装袋";
                Managers managers = Managers.Inst;
                Kingdom kingdom = managers != null ? managers.kingdom : null;
                if (CoinCourierBankScope.TryGetCurrentAuthorityBanker(kingdom, out Banker banker,
                        out CoinCourierBankReason reason) && banker != null)
                    return "国库已连接";
                return BankReasonText(reason) + " · 暂不能装袋";
            }
            catch (Exception)
            {
                return "国库状态不可读";
            }
        }
    }

    private static string BankReasonText(CoinCourierBankReason reason)
    {
        switch (reason)
        {
            case CoinCourierBankReason.ModDisabled: return "已关闭";
            case CoinCourierBankReason.NoWorldAuthority: return "无主机权限";
            case CoinCourierBankReason.NotPlaying:
            case CoinCourierBankReason.UnknownBiome: return "世界未就绪";
            case CoinCourierBankReason.PostboxMissing:
            case CoinCourierBankReason.EntryMissing: return "银行登记未就绪";
            case CoinCourierBankReason.EntryMismatch:
            case CoinCourierBankReason.NativeConflict: return "银行身份不一致";
            case CoinCourierBankReason.BankerUnavailable: return "银行当前不可用";
            case CoinCourierBankReason.ReadFault: return "银行状态不可读";
            default: return "银行不可用";
        }
    }

    /// <summary>仅视觉角色的材质参照（只读；优先当前权威银行家，其次任一本岛骑士）。排序不再
    /// 取自参考：自有角色固定 numeric layer0/order1，material/参考缺失时走既有 fallback。</summary>
    internal static SpriteRenderer ResolveVisualReference()
    {
        try
        {
            Managers managers = Managers.Inst;
            Kingdom kingdom = managers != null ? managers.kingdom : null;
            if (CoinCourierBankScope.TryGetCurrentAuthorityBanker(kingdom, out Banker banker,
                    out CoinCourierBankReason _) && banker != null && banker.gameObject != null)
            {
                SpriteRenderer renderer = banker.GetComponentInChildren<SpriteRenderer>();
                if (renderer != null) return renderer;
            }
            Knight[] knights = UnitScanCache.GetKnights();
            if (knights != null)
            {
                for (int i = 0; i < knights.Length; i++)
                {
                    Knight knight = knights[i];
                    if (knight == null || knight.gameObject == null) continue;
                    SpriteRenderer renderer = knight.GetComponentInChildren<SpriteRenderer>();
                    if (renderer != null) return renderer;
                }
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    internal static void Tick()
    {
        float delta = GameDelta();
        // 共享传送 FX 的 Tick 已集中到 ModPanel（哥布林关闭时税收助手仍能播放）；
        // 金币飞行仍由本运行时独占推进。
        CoinCourierCoinFlight.Tick(delta);
        try
        {
            TickCore(delta);
        }
        catch (Exception e)
        {
            LogOnce("tick-" + e.GetType().Name, "tick failed: " + e.GetType().Name);
        }
    }

    private static float GameDelta()
    {
        try
        {
            if (Time.timeScale <= 0f) return 0f;
            float delta = Time.deltaTime;
            if (!float.IsFinite(delta) || delta < 0f) return 0f;
            // 卡帧上限：阶段/表现绝不在一帧内跳过多段；进入动作仍由状态机保证一次。
            return delta > 0.25f ? 0.25f : delta;
        }
        catch (Exception)
        {
            return 0f;
        }
    }

    private static void TickCore(float delta)
    {
        CoinCourierShop.Tick();
        Managers managers = Managers.Inst;
        bool context = TryContext(managers, out Kingdom kingdom, out Transform layer);
        bool playing = false;
        bool menu = false;
        if (context)
        {
            try
            {
                Game.State state = managers.game.state;
                playing = state == Game.State.Playing;
                menu = state == Game.State.Menu;
            }
            catch (Exception)
            {
            }
        }
        // 场景身份：驻留场景必须仍属同一 world/layer——暂停中的世界替换也是硬失效，立即清场。
        bool sceneIdentity = S.View == null
            || (context && _worldPtr == managers.world.Pointer && _layerPtr == layer.Pointer);
        bool worldValid = context && (playing || menu) && sceneIdentity;
        bool timeScaleOk;
        bool hasAuth;
        bool online;
        bool saving;
        try { timeScaleOk = Time.timeScale > 0f; } catch (Exception) { timeScaleOk = false; }
        try { hasAuth = NetworkBigBoss.HasWorldAuth; } catch (Exception) { hasAuth = false; }
        try { online = NetworkBigBoss.IsOnline; } catch (Exception) { online = true; }
        try { saving = IslandSaveData.isSavingGame; } catch (Exception) { saving = true; }
        bool paused = !timeScaleOk || menu;
        bool bound = _state != null;
        bool ready = false;
        bool owned = false;
        bool faulted = false;
        CoinCourierPurse purse = null;
        if (bound) CoinCourierCampaignAccess.TryReadEconomy(_state, out ready, out owned, out purse, out faulted);
        // 先硬身份/权限/世界失效（清场），再暂停/保存/未就绪/故障（原地冻结）；Ready=false 不清 state、不动余额。
        CoinCourierBehaviorGate gate = CoinCourierGates.EvaluateBehavior(FeatureEnabled, hasAuth, online,
            worldValid, bound, owned, paused, saving, ready, faulted);
        if (gate != CoinCourierBehaviorGate.Open)
        {
            HandleGate(gate);
            return;
        }
        if (purse == null)
        {
            ClearScene("purse-missing");
            return;
        }
        if (!EnsureScene(managers, kingdom, layer)) return;
        TickBehavior(purse, kingdom, delta);
        RenderCurrent();
    }

    private static void HandleGate(CoinCourierBehaviorGate gate)
    {
        if (CoinCourierSceneLifecycle.ShouldClearScene(gate))
        {
            ClearScene(gate.ToString());
            return;
        }
        if (CoinCourierSceneLifecycle.ShouldFreezeInPlace(gate))
        {
            if (gate == CoinCourierBehaviorGate.Faulted) LogFrozenOnce("purse-faulted");
            RenderCurrent();
            return;
        }
        RenderCurrent();
    }

    private static bool TryContext(Managers managers, out Kingdom kingdom, out Transform layer)
    {
        kingdom = null;
        layer = null;
        if (managers == null || managers.game == null || managers.world == null
            || managers.kingdom == null || managers.world.gameLayer == null) return false;
        kingdom = managers.kingdom;
        layer = managers.world.gameLayer;
        return true;
    }

    private static bool EnsureScene(Managers managers, Kingdom kingdom, Transform layer)
    {
        IntPtr world = managers.world.Pointer;
        IntPtr layerPtr = layer.Pointer;

        // 世界/层换代才是硬失效：沿用原有的整场清理，随后走下面的完整初始化。
        if (S.View != null && (_worldPtr != world || _layerPtr != layerPtr))
            ClearScene("world-change");

        // 同 world 纯视觉失效（root/transform/renderer 任一原生对象被回收，fake-null）：
        // 只替换自有显示对象——行为相位/ActionConsumed/Plan/Sent/VisitDeadline/目标/冷却/purse
        // 全部原样保留，绝不走 ClearScene。创建失败时保留旧句柄作"本世界已初始化"的标记，
        // 按 ViewRetrySeconds 节奏有界重试，绝不做每帧的创建尝试。
        if (S.View != null && !ViewAlive())
        {
            if (Time.time < _viewRetryAt) return false;
            if (TryReplaceView(layer)) return true;
            _viewRetryAt = Time.time + ViewRetrySeconds;
            return false;
        }
        if (S.View != null) return true;

        if (!CoinCourierGround.TryResolveHome(kingdom, out float homeX, out float homeY))
        {
            LogOnce("home", "courier home unavailable; waiting for ground at the campfire");
            return false;
        }
        CoinCourierView view = CoinCourierVisuals.Create(layer, ResolveVisualReference());
        if (view == null) return false;   // 表现层已一次性告警，fail-closed
        S.View = view;
        S.Layer = layer;
        S.HomeX = homeX;
        S.HomeY = homeY;
        S.CampfireX = kingdom.campfirePosition;
        S.GroundY = homeY;
        S.Position = new Vector3(homeX, homeY, layer.position.z);
        S.Facing = 1;
        S.Visible = true;
        S.Phase = CoinCourierPhase.Wait;
        S.Elapsed = 0f;
        S.ActionConsumed = false;
        S.Target = null;
        S.TargetLife = 0;
        S.Sent = 0;
        S.StopDeliver = false;
        S.Travel = CoinCourierTravel.Bank;
        S.CheckAt = 0f;
        S.LeisureMotion = LeisureMotion.Pause;
        S.LeisureTargetX = homeX;
        S.LeisurePauseRemaining = CoinCourierTiming.LeisurePauseSeconds;
        S.LeisurePoseSeconds = 0f;
        S.LeisureWalkSeconds = 0f;
        S.LeisureBlockedRemaining = 0f;
        S.LeisureSide = 1;
        S.LeisureDaytime = false;
        _worldPtr = world;
        _layerPtr = layerPtr;
        return true;
    }

    /// <summary>自有 view 的原生后台是否全部存活：root/transform/renderer 任一 fake-null 即失效。</summary>
    private static bool ViewAlive()
    {
        CoinCourierView view = S.View;
        if (view == null || view.Destroyed) return false;
        try { return view.Root != null && view.Transform != null && view.Renderer != null; }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// 同 world 纯视觉替换：只重建显示句柄，行为相位/计划/冷却/钱包一律不动。
    /// 失败返回 false——旧句柄保留作"本世界已初始化"的标记，避免回落到初次初始化归零。
    /// </summary>
    private static bool TryReplaceView(Transform layer)
    {
        CoinCourierView replacement = CoinCourierVisuals.Create(layer, ResolveVisualReference());
        if (replacement == null) return false;
        CoinCourierView stale = S.View;
        S.View = replacement;
        S.Layer = layer;
        CoinCourierVisuals.Destroy(stale);   // 旧原生已回收时只清登记；根仍存活则真正销毁，避免残留
        _viewRetryAt = 0f;
        LogOnce("view-replaced", "display view rebuilt in place; behaviour state kept");
        return true;
    }

    /// <summary>同 world 纯视觉重建的有界重试节奏（秒）：失败后至少间隔这么久才再建 GO。</summary>
    private const float ViewRetrySeconds = 0.5f;

    // ---- 银行等待 / 取币 ----

    private static void TickBehavior(CoinCourierPurse purse, Kingdom kingdom, float delta)
    {
        switch (S.Phase)
        {
            case CoinCourierPhase.Wait:
                TickWait(purse, kingdom, delta);
                break;
            case CoinCourierPhase.Collect:
                TickCollect(purse, kingdom, delta);
                break;
            case CoinCourierPhase.JumpOut:
                TickFixed(delta, OnJumpComplete);
                break;
            case CoinCourierPhase.TeleportIn:
                TickTeleport(delta);
                break;
            case CoinCourierPhase.Fall:
                TickFall(delta);
                break;
            case CoinCourierPhase.Land:
                TickFixed(delta, EnterApproach);
                break;
            case CoinCourierPhase.Approach:
                TickApproach(delta);
                break;
            case CoinCourierPhase.Deliver:
                TickDeliver(purse, delta);
                break;
            case CoinCourierPhase.Frozen:
                if (!purse.IsFaulted) EnterWait();   // owner 解决未知结果后自然恢复
                break;
        }
    }

    private static void TickFixed(float delta, Action onComplete)
    {
        CoinCourierPhaseStep step = CoinCourierPhaseMachine.Step(S.Phase, S.Elapsed, delta, S.ActionConsumed);
        S.Elapsed += delta;
        if (step.Completed) onComplete();
    }

    private static void TickWait(CoinCourierPurse purse, Kingdom kingdom, float delta)
    {
        if (purse.IsFaulted)
        {
            EnterFrozen("purse-fault");
            return;
        }
        float now = Time.time;
        if (now >= S.CheckAt)
        {
            S.CheckAt = now + CoinCourierTiming.WaitCheckSeconds;
            if (TryStartVisit(purse)) return;
            if (purse.Coins < PurseCapacity && now >= _collectBlockedUntil)
            {
                Banker banker = CurrentBanker(kingdom);
                if (banker != null
                    && (!PatchEconomy_Banker.TryReadCourierStash(banker, out int coins) || coins > 0))
                {
                    if (AtHome()) EnterCollect();
                    else BeginLeisureReturn();
                    if (S.Phase == CoinCourierPhase.Collect) return;
                }
            }
        }
        TickLeisure(kingdom, delta, now);
    }

    private static Banker CurrentBanker(Kingdom kingdom)
    {
        try
        {
            if (kingdom == null) return null;
            return CoinCourierBankScope.TryGetCurrentAuthorityBanker(kingdom, out Banker banker,
                out CoinCourierBankReason _) ? banker : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool AtHome()
        => Math.Abs(S.Position.x - S.HomeX) <= 0.01f
            && Math.Abs(S.Position.y - S.HomeY) <= 0.05f;

    private static void BeginLeisureReturn()
    {
        if (S.LeisureMotion == LeisureMotion.Returning) return;
        S.LeisureMotion = LeisureMotion.Returning;
        S.LeisureTargetX = S.HomeX;
        S.LeisureBlockedRemaining = 0f;
        S.LeisureWalkSeconds = 0f;
    }

    /// <summary>
    /// 原地休闲时钟：只被有效 delta 推进，一个完整循环后回绕。夜间与走位探测失败
    /// 都只暂停/保持移动门，绝不清零该相位，因此原地 Idle→Leisure 连续播放。
    /// </summary>
    private static void AdvanceLeisurePose(float delta)
    {
        if (!(delta > 0f)) return;   // 暂停/非法 delta 不推进
        float next = S.LeisurePoseSeconds + delta;
        S.LeisurePoseSeconds = next >= LeisurePoseLoopSeconds ? next % LeisurePoseLoopSeconds : next;
    }

    /// <summary>Only this runtime moves the display root. Service checks stay in TickWait.</summary>
    private static void TickLeisure(Kingdom kingdom, float delta, float now)
    {
        try { S.LeisureDaytime = kingdom != null && kingdom.isDaytime; }
        catch (Exception) { S.LeisureDaytime = false; }
        if (!S.LeisureDaytime && !AtHome()) BeginLeisureReturn();

        if (S.LeisureMotion == LeisureMotion.Pause)
        {
            AdvanceLeisurePose(delta);
            if (!S.LeisureDaytime)
            {
                // 夜间不发起闲走（安全站位与偶遇门不变），原地循环照常播放。
                S.LeisurePauseRemaining = CoinCourierTiming.LeisurePauseSeconds;
                return;
            }
            S.LeisurePauseRemaining -= delta;
            if (S.LeisurePauseRemaining > 0f) return;
            float targetX = AtHome()
                ? S.HomeX + S.LeisureSide * CoinCourierTiming.LeisureRadius : S.HomeX;
            // A failed local probe leaves the courier standing. No search for a new site.
            if (!CoinCourierGround.TryStandable(S.Position.x, out float hereY)
                || Math.Abs(hereY - S.Position.y) > CoinCourierTiming.LeisureMaxGroundStep
                || !CoinCourierGround.TryStandable(targetX, out float targetY)
                || Math.Abs(targetY - S.Position.y) > CoinCourierTiming.LeisureMaxGroundStep
                || !TryReadEnemyNear(new Vector3(targetX, targetY, LayerZ()), out bool targetThreat)
                || targetThreat)
            {
                // 探测失败只重置走位节奏；原地循环相位保持连续（不再清零）。
                S.LeisurePauseRemaining = CoinCourierTiming.LeisurePauseSeconds;
                return;
            }
            S.LeisureTargetX = targetX;
            S.LeisureMotion = AtHome() ? LeisureMotion.Outbound : LeisureMotion.Returning;
            if (S.LeisureMotion == LeisureMotion.Outbound) S.LeisureSide = -S.LeisureSide;
            S.LeisureBlockedRemaining = 0f;
            S.LeisureWalkSeconds = 0f;
        }

        if (S.LeisureBlockedRemaining > 0f)
        {
            // 受阻驻留同样连续播 Idle→Leisure（不再冻结在 Idle 首帧）；移动门保持原样，
            // 到点后仍按原有节奏重试下一步。
            AdvanceLeisurePose(delta);
            S.LeisureBlockedRemaining -= delta;
            if (S.LeisureBlockedRemaining > 0f) return;
            S.LeisureBlockedRemaining = 0f;
        }
        if (!(delta > 0f)) return;
        if (!TryReadEnemyNear(S.Position, out bool currentThreat))
        {
            S.LeisureBlockedRemaining = CoinCourierTiming.WaitCheckSeconds;
            return;
        }
        if (currentThreat)
        {
            // The existing visible jump + hidden transfer is reserved for an actual threat.
            DepartHome("leisure-enemy");
            return;
        }
        float remaining = S.LeisureTargetX - S.Position.x;
        float maxStep = CoinCourierTiming.LeisureWalkSpeed * delta;
        float nextX = Math.Abs(remaining) <= maxStep
            ? S.LeisureTargetX : S.Position.x + (remaining > 0f ? maxStep : -maxStep);
        if (!CoinCourierGround.TryStandable(nextX, out float nextY)
            || Math.Abs(nextY - S.Position.y) > CoinCourierTiming.LeisureMaxGroundStep)
        {
            S.LeisureBlockedRemaining = CoinCourierTiming.WaitCheckSeconds;
            return;
        }
        Vector3 next = new Vector3(nextX, nextY, LayerZ());
        if (!TryReadEnemyNear(next, out bool nextThreat))
        {
            S.LeisureBlockedRemaining = CoinCourierTiming.WaitCheckSeconds;
            return;
        }
        if (nextThreat)
        {
            DepartHome("leisure-enemy");
            return;
        }
        S.Facing = remaining >= 0f ? 1 : -1;
        S.Position = next;
        S.LeisureWalkSeconds += delta * 0.35f; // short steps, slower than the delivery run
        if (nextX != S.LeisureTargetX) return;
        if (S.LeisureMotion == LeisureMotion.Returning && AtHome())
        {
            // Recheck a new Knight and current bank identity/funds on the next frame.
            S.CheckAt = now;
        }
        S.LeisureMotion = LeisureMotion.Pause;
        S.LeisurePauseRemaining = CoinCourierTiming.LeisurePauseSeconds;
        S.LeisurePoseSeconds = 0f;
        S.LeisureWalkSeconds = 0f;
    }

    private static void EnterWait()
    {
        S.Phase = CoinCourierPhase.Wait;
        S.Elapsed = 0f;
        S.ActionConsumed = false;
        S.LeisureMotion = AtHome() ? LeisureMotion.Pause : LeisureMotion.Returning;
        S.LeisureTargetX = S.HomeX;
        S.LeisurePauseRemaining = CoinCourierTiming.LeisurePauseSeconds;
        S.LeisurePoseSeconds = 0f;
        S.LeisureWalkSeconds = 0f;
        S.LeisureBlockedRemaining = 0f;
    }

    private static void EnterCollect()
    {
        S.Phase = CoinCourierPhase.Collect;
        S.Elapsed = 0f;
        S.ActionConsumed = false;
    }

    private static void EndCollect()
    {
        EnterWait();
        S.CheckAt = Time.time;   // 立刻尝试起跳离场
    }

    private static void TickCollect(CoinCourierPurse purse, Kingdom kingdom, float delta)
    {
        CoinCourierPhaseStep step = CoinCourierPhaseMachine.Step(S.Phase, S.Elapsed, delta, S.ActionConsumed);
        if (step.Action == CoinCourierAction.BagOne)
        {
            S.ActionConsumed = true;
            CoinCourierResult result = CoinCourierEconomy.TryBagOneCoin(purse, CurrentBanker(kingdom), PurseCapacity);
            if (result.Applied)
            {
                CoinCourierCoinFlight.Begin(BankCoinOrigin(), CoinBagPoint(), SortingLayer(), SortingOrder());
                if (purse.Coins >= PurseCapacity) EndCollect();
                return;
            }
            if (result.Status == CoinCourierStatus.Indeterminate || result.Reason == CoinCourierReason.Frozen)
            {
                EnterFrozen("bag-" + result.Reason);
                return;
            }
            if (result.Reason == CoinCourierReason.BankEmpty)
                _collectBlockedUntil = Time.time + CoinCourierTiming.BankRetrySeconds;
            else
                _collectBlockedUntil = Time.time + CoinCourierTiming.WaitCheckSeconds;
            EndCollect();
            return;
        }
        S.Elapsed += delta;
        if (!step.Completed) return;
        S.Elapsed = 0f;
        S.ActionConsumed = false;
        if (purse.Coins >= PurseCapacity) EndCollect();
    }

    // ---- 目标访问：离场/显形/落地/接近/配送 ----

    private static bool TryStartVisit(CoinCourierPurse purse)
    {
        int coins = purse.AvailableCoins;
        if (coins <= 0) return false;
        if (!CoinCourierTargeting.TrySelect(coins, Time.time, _lastServedSide, MaxCoinsPerVisit,
                NextEligibleAt, out Knight target, out CoinCourierRules.VisitPlan plan)) return false;
        if (target == null || plan.LifeId == 0 || plan.CoinsToSend <= 0) return false;
        if (plan.Side != -1 && plan.Side != 1) return false;   // 无有效朝向：不猜落点
        float targetX;
        try { targetX = target.transform.position.x; } catch (Exception) { return false; }
        // 骑士"后方"= 背离其 Side 朝向；side 无效/无法解出时拒绝，绝不退化成"营火右侧"。
        if (!CoinCourierPlacementRules.TryLandingX(targetX, plan.Side, out float landingX)) return false;
        if (!CoinCourierGround.TryStandable(landingX, out float groundY))
        {
            CooldownTarget(plan.LifeId, CoinCourierTiming.ThreatRetrySeconds);
            return false;
        }
        Vector3 spawn = new Vector3(landingX, groundY + CoinCourierTiming.LandingHeight, LayerZ());
        if (!CoinCourierGround.ObstacleFree(landingX, groundY + CoinCourierTiming.LandingHeight) || EnemyNear(spawn))
        {
            CooldownTarget(plan.LifeId, CoinCourierTiming.ThreatRetrySeconds);
            return false;
        }
        S.Target = target;
        S.TargetLife = plan.LifeId;
        S.TargetSide = plan.Side;
        S.Plan = plan;
        S.Sent = 0;
        S.StopDeliver = false;
        S.AbortVisit = false;
        S.TargetX = targetX;
        S.LandingX = landingX;
        S.VisitDeadlineAt = Time.time + CoinCourierTiming.VisitDeadlineSeconds;
        S.VisitMoved = 0f;
        S.GroundY = groundY;
        S.Facing = targetX >= S.Position.x ? 1 : -1;
        S.Travel = CoinCourierTravel.Target;
        _lastServedSide = plan.Side;
        EnterJump();
        return true;
    }

    private static void EnterJump()
    {
        S.Phase = CoinCourierPhase.JumpOut;
        S.Elapsed = 0f;
        S.ActionConsumed = false;
        S.Visible = true;
    }

    private static void OnJumpComplete()
    {
        // "原地短线收拢"：离场位置画一条，再在终点进入隐藏传送段。
        ShowTeleportFx(S.Position);
        if (S.Travel == CoinCourierTravel.Target)
        {
            S.Position = new Vector3(S.LandingX, S.GroundY + CoinCourierTiming.LandingHeight, LayerZ());
            S.Facing = S.TargetX >= S.Position.x ? 1 : -1;
        }
        else
        {
            S.Position = new Vector3(S.HomeX, S.HomeY, LayerZ());
            S.Facing = 1;
        }
        S.Phase = CoinCourierPhase.TeleportIn;
        S.Elapsed = 0f;
        S.ActionConsumed = false;
        S.Visible = false;
    }

    private static void TickTeleport(float delta)
    {
        CoinCourierPhaseStep step = CoinCourierPhaseMachine.Step(S.Phase, S.Elapsed, delta, S.ActionConsumed);
        S.Elapsed += delta;
        if (!step.Completed) return;
        if (S.Travel == CoinCourierTravel.Target)
        {
            float targetX = TargetXNow();
            // 飞行前后 side 变化 → 原"后方"点已失效：带回银行，绝不按错误落点显形。
            if (CurrentSide(S.Target) != S.TargetSide)
            {
                SnapHome("side-changed");
                return;
            }
            if (!StillValidTarget()
                || !CoinCourierPlacementRules.WithinBudgetReach(targetX - S.Position.x)
                || EnemyNear(S.Position))
            {
                SnapHome("air-cancel");
                return;
            }
            // 空中再验一次落点：地面存在且高度未变（世界变化/新建筑一律放弃）。
            if (!CoinCourierGround.TryStandable(S.LandingX, out float groundNow)
                || Math.Abs(groundNow - S.GroundY) > 0.5f)
            {
                SnapHome("ground-changed");
                return;
            }
            // "目标上方短线展开、显形"。
            ShowTeleportFx(S.Position);
            S.SpawnY = S.Position.y;
            S.Phase = CoinCourierPhase.Fall;
            S.Elapsed = 0f;
            S.ActionConsumed = false;
            S.Visible = true;
            return;
        }
        ShowTeleportFx(S.Position);
        S.Position = new Vector3(S.HomeX, S.HomeY, LayerZ());
        S.GroundY = S.HomeY;
        S.Facing = 1;
        S.Visible = true;
        EnterWait();
        S.CheckAt = Time.time + CoinCourierTiming.ArrivalPauseSeconds;
    }

    private static void TickFall(float delta)
    {
        S.Elapsed += delta;
        float height = CoinCourierPlacementRules.FallHeight(S.Elapsed, CoinCourierTiming.FallSeconds,
            S.SpawnY - S.GroundY);
        S.Position = new Vector3(S.Position.x, S.GroundY + height, S.Position.z);
        if (S.Elapsed < CoinCourierTiming.FallSeconds) return;
        S.Position = new Vector3(S.Position.x, S.GroundY, S.Position.z);
        S.Phase = CoinCourierPhase.Land;
        S.Elapsed = 0f;
        S.ActionConsumed = false;
    }

    private static void EnterApproach()
    {
        // 预算/期限只在 visit 开始时冻结：阶段切换绝不重置。
        S.Phase = CoinCourierPhase.Approach;
        S.Elapsed = 0f;
        S.ActionConsumed = false;
    }

    private static void TickApproach(float delta)
    {
        if (!StillValidTarget())
        {
            DepartHome("target-invalid");
            return;
        }
        if (Time.time >= S.VisitDeadlineAt)
        {
            DepartHome("deadline");
            return;
        }
        if (EnemyNear(S.Position))
        {
            DepartHome("enemy");
            return;
        }
        float targetX = TargetXNow();
        float dx = targetX - S.Position.x;
        S.Facing = dx >= 0f ? 1 : -1;
        S.Elapsed += delta;   // 仅作 Run 动画时钟；期限用 VisitDeadlineAt，不用它计时
        if (CoinCourierPlacementRules.ApproachReached(dx))
        {
            EnterDeliver();
            return;
        }
        // 从冻结的 landingX 起最多 VisitMoveBudget：单步夹住剩余预算，绝不越界。
        float budgetLeft = CoinCourierPlacementRules.RemainingMoveBudget(S.VisitMoved);
        if (!(budgetLeft > 0f))
        {
            // 预算耗尽：仍在交接距离内就进入交接（首币可达），否则按原 Aborted 路径回银行。
            if (CoinCourierPlacementRules.WithinHandoffRange(dx))
            {
                EnterDeliver();
                return;
            }
            DepartHome("budget");
            return;
        }
        float step = CoinCourierTiming.RunSpeed * delta;
        if (step > budgetLeft) step = budgetLeft;
        float nextX = Math.Abs(dx) <= step ? targetX : S.Position.x + (dx >= 0f ? step : -step);
        S.VisitMoved += Math.Abs(nextX - S.Position.x);
        S.Position = new Vector3(nextX, S.GroundY, S.Position.z);
    }

    private static void EnterDeliver()
    {
        S.Phase = CoinCourierPhase.Deliver;
        S.Elapsed = 0f;
        S.ActionConsumed = false;
        S.StopDeliver = false;
    }

    private static void TickDeliver(CoinCourierPurse purse, float delta)
    {
        CoinCourierPhaseStep step = CoinCourierPhaseMachine.Step(S.Phase, S.Elapsed, delta, S.ActionConsumed);
        if (step.Action == CoinCourierAction.DeliverOne)
        {
            S.ActionConsumed = true;
            // 每枚交接前复验：身份/朝向/配额/敌情/期限，任何一项不成立都不交接。
            if (!StillValidTarget() || CurrentSide(S.Target) != S.TargetSide || !PlanAllowsNext(purse)
                || EnemyNear(S.Position) || Time.time >= S.VisitDeadlineAt)
            {
                S.StopDeliver = true;   // 目标失效/朝向改变/配额或钱包不允/敌近/超时：携余币回银行
                S.AbortVisit = true;
            }
            else
            {
                float dx = TargetXNow() - S.Position.x;
                // 一次 Approach→Deliver 之后不再追随：目标离开交接距离就结束本访问回银行（可完成部分补给）。
                if (!CoinCourierPlacementRules.WithinHandoffRange(dx))
                {
                    S.StopDeliver = true;
                    S.AbortVisit = true;   // 超距=确定未尝试：不 reserve、不打 Indeterminate、不退已送币
                }
                else
                {
                    CoinCourierResult result = CoinCourierEconomy.TryDeliverOne(purse, S.Target, S.TargetLife);
                    if (result.Applied)
                    {
                        S.Sent++;
                        CoinCourierCoinFlight.Begin(CoinBagPoint(), TargetBagPoint(), SortingLayer(), SortingOrder());
                    }
                    else if (result.Status == CoinCourierStatus.Indeterminate || result.Reason == CoinCourierReason.Frozen)
                    {
                        EnterFrozen("deliver-" + result.Reason);
                        return;
                    }
                    else
                    {
                        S.StopDeliver = true;   // 明确拒绝（满钱包/目标失效）：余币带回银行
                        S.AbortVisit = true;
                    }
                }
            }
        }
        S.Elapsed += delta;
        if (!step.Completed) return;
        if (S.StopDeliver || S.Sent >= S.Plan.CoinsToSend || purse.AvailableCoins <= 0)
        {
            EndVisit(purse, S.AbortVisit
                ? CoinCourierVisitOutcome.Aborted
                : CoinCourierVisitOutcome.Completed);
            return;
        }
        S.Elapsed = 0f;
        S.ActionConsumed = false;
    }

    private static bool PlanAllowsNext(CoinCourierPurse purse)
    {
        try
        {
            Wallet wallet = S.Target.Wallet;
            if (wallet == null) return false;
            return CoinCourierRules.CanSendNextCoin(S.Plan, S.TargetLife, wallet.Coins, wallet.TotalCapacity,
                purse.AvailableCoins, S.Sent);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void EndVisit(CoinCourierPurse purse, CoinCourierVisitOutcome outcome)
    {
        bool aborted = outcome == CoinCourierVisitOutcome.Aborted;
        // 中止的骑士也进冷却：回银行后不会立刻再飞同一个（避免出-回-出的抖动）。
        if (S.TargetLife != 0)
            CooldownTarget(S.TargetLife, aborted ? CoinCourierTiming.ThreatRetrySeconds : KnightCooldown);
        S.Target = null;
        S.TargetLife = 0;
        S.Sent = 0;
        S.Plan = default;
        S.TargetSide = 0;
        S.VisitDeadlineAt = 0f;
        S.VisitMoved = 0f;
        // 只有正常完成且仍有可支配币才允许直接飞下一处；中止（敌情/失效/超距/拒绝）一律携币回银行。
        if (CoinCourierVisitPolicy.MayChainNextVisit(outcome, purse.AvailableCoins) && TryStartVisit(purse)) return;
        DepartHome(aborted ? "aborted" : "visit-done");
        S.AbortVisit = false;
    }

    private static void DepartHome(string reason)
    {
        if (S.TargetLife != 0) CooldownTarget(S.TargetLife, CoinCourierTiming.ThreatRetrySeconds);
        S.Target = null;
        S.TargetLife = 0;
        S.Sent = 0;
        S.Plan = default;
        S.TargetSide = 0;
        S.VisitMoved = 0f;
        S.StopDeliver = false;
        S.Travel = CoinCourierTravel.Bank;
        S.Facing = 1;
        EnterJump();
        if (!string.IsNullOrEmpty(reason) && reason != "visit-done")
            LogOnce("depart-" + reason, "visit returning to bank: " + reason);
    }

    /// <summary>空中/隐藏阶段取消：直接回银行显形（不做地面跳跃，避免在空中演跑步）。</summary>
    private static void SnapHome(string reason)
    {
        if (S.TargetLife != 0) CooldownTarget(S.TargetLife, CoinCourierTiming.ThreatRetrySeconds);
        S.Target = null;
        S.TargetLife = 0;
        S.Sent = 0;
        S.Plan = default;
        S.TargetSide = 0;
        S.VisitMoved = 0f;
        S.StopDeliver = false;
        S.Travel = CoinCourierTravel.Bank;
        S.Facing = 1;
        S.Position = new Vector3(S.HomeX, S.HomeY, LayerZ());
        S.GroundY = S.HomeY;
        S.Visible = true;
        ShowTeleportFx(S.Position);
        EnterWait();
        S.CheckAt = Time.time + CoinCourierTiming.ArrivalPauseSeconds;
        LogOnce("cancelled-" + reason, "visit cancelled: " + reason);
    }

    private static void EnterFrozen(string reason)
    {
        S.Phase = CoinCourierPhase.Frozen;
        S.Elapsed = 0f;
        S.ActionConsumed = false;
        S.Visible = true;
        S.Target = null;
        S.TargetLife = 0;
        S.Sent = 0;
        S.Plan = default;
        S.TargetSide = 0;
        S.VisitMoved = 0f;
        S.StopDeliver = false;
        LogFrozenOnce(reason);
    }

    private static void LogFrozenOnce(string reason)
    {
        if (_frozenLogged) return;
        _frozenLogged = true;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning(
                "[CoinCourier] economy frozen; balance retained: " + reason);
        }
        catch (Exception)
        {
        }
    }

    // ---- 目标有效性 / 敌情 / 冷却 ----

    private static bool StillValidTarget()
    {
        if (S.Target == null || S.TargetLife == 0) return false;
        try
        {
            return CoinCourierTargeting.IsCurrentDeliveryCandidate(S.Target, S.TargetLife, out Wallet wallet)
                && wallet != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>骑士当前朝向侧（±1，与 plan.Side 同语义）；读不到/无效返回 0（调用方据此拒绝/取消）。</summary>
    private static int CurrentSide(Knight knight)
    {
        try
        {
            if (knight == null) return 0;
            Side side = knight.side;
            if (side == Side.Left) return -1;
            if (side == Side.Right) return 1;
            return 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static float TargetXNow()
    {
        try { return S.Target != null ? S.Target.transform.position.x : S.TargetX; }
        catch (Exception) { return float.NaN; }
    }

    private static bool EnemyNear(Vector3 point)
        => !TryReadEnemyNear(point, out bool near) || near;

    /// <summary>Keep unknown distinct for leisure; delivery retains its fail-closed wrapper.</summary>
    private static bool TryReadEnemyNear(Vector3 point, out bool near)
    {
        near = false;
        try
        {
            EnsureProbe();
            if (S.Probe == null) return false;
            S.Probe.position = point;
            S.Probe.localScale = Vector3.one;
            if (_enemyMask == 0) _enemyMask = LayerMask.GetMask("Enemies");
            if (_enemyMask == 0) return false;
            // 与武士冲刺同一已验证 API：局部矩形扫描，绝不全场遍历。
            GameObject closest = Scanner.ScanClosest(S.Probe, CoinCourierTiming.ThreatRadius, _enemyMask,
                null, CoinCourierTiming.ThreatRadius, 1f, true);
            near = closest != null;
            return true;
        }
        catch (Exception e)
        {
            LogOnce("enemy-scan-" + e.GetType().Name, "enemy scan unavailable: " + e.GetType().Name);
            return false;
        }
    }

    private static void EnsureProbe()
    {
        if (S.Probe != null) return;
        try
        {
            Transform parent = S.Layer;
            GameObject probe = new GameObject("KEM_CoinCourierProbe");
            if (parent != null) probe.transform.SetParent(parent, false);
            probe.transform.position = S.Position;
            S.Probe = probe.transform;
        }
        catch (Exception e)
        {
            S.Probe = null;
            LogOnce("probe-" + e.GetType().Name, "probe create failed: " + e.GetType().Name);
        }
    }

    private static void CooldownTarget(long life, float seconds)
    {
        if (life == 0) return;
        if (!CoinCourierRules.TryNextEligibleAt(Time.time, seconds, out float readyAt)) return;
        // 只延后不提前：已有的更长冷却（访问完成）绝不被取消路径缩短。
        if (NextEligibleAt.TryGetValue(life, out float current) && current >= readyAt) return;
        NextEligibleAt[life] = readyAt;
        if (NextEligibleAt.Count > CoinCourierTiming.NextEligibleCapacity) PruneEligible(Time.time);
    }

    private static void PruneEligible(float now)
    {
        PruneBuffer.Clear();
        foreach (KeyValuePair<long, float> pair in NextEligibleAt)
        {
            if (pair.Value <= now) PruneBuffer.Add(pair.Key);
        }
        for (int i = 0; i < PruneBuffer.Count; i++) NextEligibleAt.Remove(PruneBuffer[i]);
        if (NextEligibleAt.Count > CoinCourierTiming.NextEligibleCapacity) NextEligibleAt.Clear();
    }

    // ---- 表现 ----

    private static float LayerZ()
    {
        try { return S.Layer != null ? S.Layer.position.z : 0f; }
        catch (Exception) { return 0f; }
    }

    private static int SortingLayer()
    {
        try { return S.View != null && S.View.Renderer != null ? S.View.Renderer.sortingLayerID : 0; }
        catch (Exception) { return 0; }
    }

    private static int SortingOrder()
    {
        try { return S.View != null && S.View.Renderer != null ? S.View.Renderer.sortingOrder : 0; }
        catch (Exception) { return 0; }
    }

    private static Color TeleportColor()
        => new Color(0.95f, 0.82f, 0.42f, 0.85f);

    /// <summary>只持有/取消自己的句柄：共享 FX 池中的其他业务 effect 绝不受影响。
    /// 哥布林传送显式用竖纹（Vertical）；横纹默认只保留给旧调用形状。</summary>
    private static void ShowTeleportFx(Vector3 position)
    {
        CancelTeleportFx();
        try
        {
            _fxHandle = CoinCourierTeleportFx.Begin(position, TeleportColor(), 1f,
                SortingLayer(), SortingOrder(), CoinCourierTeleportStyle.Vertical);
        }
        catch (Exception e)
        {
            _fxHandle = default;
            LogOnce("fx-" + e.GetType().Name, "teleport fx unavailable: " + e.GetType().Name);
        }
    }

    private static void CancelTeleportFx()
    {
        if (!_fxHandle.IsValid) return;
        try { CoinCourierTeleportFx.Cancel(_fxHandle); } catch (Exception) { }
        _fxHandle = default;
    }

    private static Vector3 CoinBagPoint()
        => S.Position + new Vector3(0f, CoinCourierTiming.CollectPointUp, 0f);

    private static Vector3 BankCoinOrigin()
    {
        // 国库一侧的手部位置：主城锚（取币站位）朝营火（城堡）方向偏移。
        float toward = S.CampfireX - S.HomeX;
        float direction = toward > 0f ? 1f : toward < 0f ? -1f : 1f;
        return new Vector3(S.HomeX + 0.7f * direction, S.HomeY + 0.6f, LayerZ());
    }

    private static Vector3 TargetBagPoint()
    {
        Vector3 point = S.Position + new Vector3(0f, CoinCourierTiming.CollectPointUp + 0.1f, 0f);
        try
        {
            if (S.Target != null)
            {
                Vector3 target = S.Target.transform.position;
                point = new Vector3(target.x, target.y + 0.5f, point.z);
            }
        }
        catch (Exception)
        {
        }
        return point;
    }

    private static void RenderCurrent()
    {
        if (S.View == null || !ViewAlive()) return;   // 原生后台已回收：不动死对象，等 EnsureScene 的正常替换
        float phase = S.Elapsed;
        CoinCourierPose pose = PoseFor(S.Phase, ref phase);
        CoinCourierVisuals.Render(S.View, S.Position, S.Facing >= 0, pose, phase, S.Visible);
    }

    private static CoinCourierPose PoseFor(CoinCourierPhase phase, ref float phaseSeconds)
    {
        switch (phase)
        {
            case CoinCourierPhase.Collect:
                return CoinCourierPose.Collect;
            case CoinCourierPhase.JumpOut:
                return CoinCourierPose.Jump;
            case CoinCourierPhase.Fall:
                return CoinCourierPose.Fall;
            case CoinCourierPhase.Land:
                return CoinCourierPose.Land;
            case CoinCourierPhase.Approach:
                return CoinCourierPose.Run;
            case CoinCourierPhase.Deliver:
                return CoinCourierPose.Deliver;
            case CoinCourierPhase.TeleportIn:
                return CoinCourierPose.Fall;   // 隐藏中，仅维持有效帧
            case CoinCourierPhase.Wait:
            {
                // 走位中播 Run；驻留与受阻（含夜间）一律播原地休闲循环，相位由
                // AdvanceLeisurePose 单一时钟推进（绝不再冻结在 Idle 首帧）。
                if (S.LeisureMotion != LeisureMotion.Pause && S.LeisureBlockedRemaining <= 0f)
                {
                    phaseSeconds = S.LeisureWalkSeconds;
                    return CoinCourierPose.Run;
                }
                phaseSeconds = S.LeisurePoseSeconds;
                if (phaseSeconds < LeisureIdlePoseSeconds)
                {
                    return CoinCourierPose.Idle;
                }
                phaseSeconds -= LeisureIdlePoseSeconds;
                return CoinCourierPose.Leisure;
            }
            default:
                phaseSeconds = 0f;   // Frozen：原地待机，不再有经济动作
                return CoinCourierPose.Idle;
        }
    }

    /// <summary>场景清场（世界卸载/功能关闭/失权/未绑定）：只销毁自有 GO 与相位，
    /// 绝不改动 campaign 的 Owned/钱袋/记录。</summary>
    private static void ClearScene(string reason)
    {
        try
        {
            if (S.View != null)
            {
                CoinCourierVisuals.Destroy(S.View);
                S.View = null;
            }
            if (S.Probe != null)
            {
                UnityEngine.Object.Destroy(S.Probe.gameObject);
                S.Probe = null;
            }
        }
        catch (Exception e)
        {
            LogOnce("clear-" + e.GetType().Name, "scene clear failed: " + e.GetType().Name);
        }
        // 硬生命周期清理：飞行金币绝不跨 world 存活；共享传送 FX 只取消自己的句柄，
        // 绝不在业务关闭时清共享池（税收助手等其他调用方不受影响）。
        CoinCourierCoinFlight.Clear();
        CancelTeleportFx();
        S.Layer = null;
        S.Phase = CoinCourierPhase.Wait;
        S.Elapsed = 0f;
        S.ActionConsumed = false;
        S.Target = null;
        S.TargetLife = 0;
        S.TargetSide = 0;
        S.Sent = 0;
        S.Plan = default;
        S.VisitDeadlineAt = 0f;
        S.VisitMoved = 0f;
        S.StopDeliver = false;
        S.AbortVisit = false;
        S.Visible = true;
        S.Facing = 1;
        S.Travel = CoinCourierTravel.Bank;
        S.CheckAt = 0f;
        S.LeisureMotion = LeisureMotion.Pause;
        S.LeisureTargetX = 0f;
        S.LeisurePauseRemaining = 0f;
        S.LeisurePoseSeconds = 0f;
        S.LeisureWalkSeconds = 0f;
        S.LeisureBlockedRemaining = 0f;
        S.LeisureSide = 1;
        S.LeisureDaytime = false;
        _worldPtr = IntPtr.Zero;
        _layerPtr = IntPtr.Zero;
        _lastServedSide = 0;
        _collectBlockedUntil = 0f;
        _viewRetryAt = 0f;
        _enemyMask = 0;
        NextEligibleAt.Clear();
        CoinCourierGround.Invalidate();
        if (!string.IsNullOrEmpty(reason)) LogOnce("clear-" + reason, "scene cleared: " + reason);
    }

    private static void LogOnce(string key, string message)
    {
        if (!LoggedOnce.Add(key)) return;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[CoinCourier] " + message);
        }
        catch (Exception)
        {
        }
    }
}
