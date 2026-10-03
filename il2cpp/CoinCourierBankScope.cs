using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 国库装币身份解析的精确拒因：只解释"现在为什么不能从国库取币"，
/// 供状态文案原样投影；绝不描述招募/角色是否可用。
/// </summary>
internal enum CoinCourierBankReason
{
    Ready = 0,
    ModDisabled,
    NoWorldAuthority,
    NotPlaying,
    UnknownBiome,
    PostboxMissing,
    /// <summary>DynamicObjects 中没有 903 登记（读档未完成或旧场景残留）。</summary>
    EntryMissing,
    /// <summary>903 登记存在，但类型/ID/组件/双向回指不足以确认同一本体。</summary>
    EntryMismatch,
    /// <summary>本体已禁用/未激活/离开当前 gameLayer 与场景。</summary>
    BankerUnavailable,
    /// <summary>原生 kingdom.banker 非空且指向别的本体（混合态 fail-closed）。</summary>
    NativeConflict,
    ReadFault,
}

/// <summary>
/// 国库装币的唯一只读身份入口：当前权威银行家 = 原生 NetworkPostbox
/// <c>DynamicObjects</c> 固定 903 登记的对象本体，而不是 <c>Kingdom.banker</c> 便捷字段
/// （实际 2.4 读档时该字段允许为 null：Castle.CatchupToLevel 见到 903 已登记就跳过赋值，
/// Banker.Awake 的 903 登记才是原生存量判据）。
///
/// 顺序：总开关 → 世界主机权限 → 已识别 biome → Playing/世界就绪 →
/// 字典 903（类型/ID）→ referencedGO 自身 Banker → parentHeaderRef 双向同一 →
/// 本体启用/对象激活 → 当前 gameLayer/场景 → 与原生引用不冲突。
/// 只读、不扫描、不缓存、不回填 <c>kingdom.banker</c>、不新增银行实体；整体异常
/// 拒绝并只记一次日志。它不读也不改任何账本，客户端/暂停/保存由资金入口另行验证。
/// </summary>
internal static class CoinCourierBankScope
{
    /// <summary>原生银行家的固定登记 ID（Castle 创建与 Banker.Awake 注册共用 903）。</summary>
    internal const short CourierBankerNetId = 903;

    private static bool _loggedFault;

    /// <summary>
    /// 解析当前权威银行家。<paramref name="kingdom"/> 必须与当前 <c>Managers.kingdom</c>
    /// 同体；<c>kingdom.banker</c> 非 null 时必须与解析结果同指针（null 不再等于不存在）。
    /// </summary>
    internal static bool TryGetCurrentAuthorityBanker(Kingdom kingdom, out Banker banker,
        out CoinCourierBankReason reason)
    {
        banker = null;
        reason = CoinCourierBankReason.Ready;
        try
        {
            if (ModConfig.Enabled == null || !ModConfig.Enabled.Value)
            {
                reason = CoinCourierBankReason.ModDisabled;
                return false;
            }
            if (!NetworkBigBoss.HasWorldAuth)
            {
                reason = CoinCourierBankReason.NoWorldAuthority;
                return false;
            }
            BiomeHolder biome = BiomeHolder.Inst;
            if (biome == null || biome.BiomeIndex < 0)
            {
                reason = CoinCourierBankReason.UnknownBiome;
                return false;
            }
            Managers managers = Managers.Inst;
            if (managers == null || managers.game == null || managers.game.state != Game.State.Playing
                || managers.world == null || managers.kingdom == null)
            {
                reason = CoinCourierBankReason.NotPlaying;
                return false;
            }
            if (kingdom == null || kingdom.Pointer != managers.kingdom.Pointer)
            {
                reason = CoinCourierBankReason.NativeConflict;
                return false;
            }

            NetworkPostbox postbox = NetworkPostbox.Instance;
            if (postbox == null)
            {
                reason = CoinCourierBankReason.PostboxMissing;
                return false;
            }
            var dynamicObjects = postbox.DynamicObjects;
            if (dynamicObjects == null
                || !dynamicObjects.TryGetValue(CourierBankerNetId, out CRPCHeader header)
                || header == null)
            {
                reason = CoinCourierBankReason.EntryMissing;
                return false;
            }
            if (header.HeaderType != CRPCType.Dynamic || header.NetID != CourierBankerNetId)
            {
                reason = CoinCourierBankReason.EntryMismatch;
                return false;
            }
            GameObject referenced = header.referencedGO;
            if (referenced == null)
            {
                reason = CoinCourierBankReason.EntryMismatch;
                return false;
            }
            Banker candidate = referenced.GetComponent<Banker>();
            if (candidate == null)
            {
                reason = CoinCourierBankReason.EntryMismatch;
                return false;
            }
            // 双向登记：本体回指的 header 与字典命中的 header 必须是同一个，
            // 且登记 GO 就是本体自己的 GO——不接受任意传入对象。
            CRPCHeader parent = candidate.parentHeaderRef;
            if (parent == null || parent.Pointer != header.Pointer
                || candidate.gameObject == null || candidate.gameObject.Pointer != referenced.Pointer)
            {
                reason = CoinCourierBankReason.EntryMismatch;
                return false;
            }
            if (!candidate.enabled || !referenced.activeInHierarchy)
            {
                reason = CoinCourierBankReason.BankerUnavailable;
                return false;
            }
            if (!GreekBankScope.IsInCurrentLayer(candidate))
            {
                reason = CoinCourierBankReason.BankerUnavailable;
                return false;
            }
            Banker native = managers.kingdom.banker;
            if (native != null && native.Pointer != candidate.Pointer)
            {
                reason = CoinCourierBankReason.NativeConflict;
                return false;
            }
            banker = candidate;
            return true;
        }
        catch (Exception e)
        {
            banker = null;
            reason = CoinCourierBankReason.ReadFault;
            if (!_loggedFault)
            {
                _loggedFault = true;
                KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning(
                    "[CoinCourier] banker entry unreadable: " + e.GetType().Name);
            }
            return false;
        }
    }

    /// <summary>
    /// 传入的 banker 是否就是当前权威解析结果本体；每个最终扣款调用都重新解析一次
    /// （旧参数不会因为曾经合法而继续放行）。异常一律拒绝。
    /// </summary>
    internal static bool IsCurrentAuthorityBanker(Banker banker)
    {
        if (banker == null) return false;
        try
        {
            Managers managers = Managers.Inst;
            Kingdom kingdom = managers != null ? managers.kingdom : null;
            return TryGetCurrentAuthorityBanker(kingdom, out Banker current, out CoinCourierBankReason _)
                && current != null && current.Pointer == banker.Pointer;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
