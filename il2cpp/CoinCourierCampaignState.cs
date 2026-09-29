using System;

namespace KingdomEnhancedMod;

/// <summary>
/// 单一详细就绪评估的结果种类；<see cref="Ready"/> 只表示"全部交易门通过"，其余种类
/// 只是对同一评估的如实分类（暂停/加载/保存/联机/权限/身份与存档故障），不是第二套判定。
/// </summary>
internal enum CoinCourierAvailability
{
    /// <summary>未接线或当前 campaign 身份未确认（默认）。</summary>
    NotBound = 0,
    /// <summary>当前存档模式不受支持（挑战等）。</summary>
    Unsupported,
    /// <summary>当前 global 的存档记录损坏/读取失败（owner 已关闭本模块）。</summary>
    Closed,
    /// <summary>捕获/重写等持久化故障（旧 staged 保留，经济冻结）。</summary>
    PersistenceFault,
    Online,
    NoAuthority,
    WorldUnavailable,
    /// <summary>非 Playing 且非 Menu（读档/结束等）。</summary>
    Loading,
    /// <summary>原生菜单暂停。</summary>
    Menu,
    /// <summary>Playing 且时标暂停（面板自动暂停等）。</summary>
    Paused,
    /// <summary>保存中（island-saving / SavingEnabled=false / Haglet 忙）。</summary>
    Saving,
    /// <summary>评估本身读取失败。</summary>
    ReadFault,
    Ready,
}

/// <summary>
/// 详细就绪评估的只读投影：<see cref="Kind"/> 驱动 UI 文案，<see cref="Detail"/> 是
/// 有界的诊断标签（关闭原因/故障标记，无则 null），只作说明，绝不参与交易判定。
/// </summary>
internal readonly struct CoinCourierAvailabilityInfo
{
    internal readonly CoinCourierAvailability Kind;
    internal readonly string Detail;

    internal CoinCourierAvailabilityInfo(CoinCourierAvailability kind, string detail)
    {
        Kind = kind;
        Detail = detail;
    }

    internal static CoinCourierAvailabilityInfo Of(CoinCourierAvailability kind) => new CoinCourierAvailabilityInfo(kind, null);
}

/// <summary>
/// 金币哥布林唯一且最窄的存档接入面：由后续真正的 campaign 保存 owner 实现并显式
/// <see cref="CoinCourierRuntime.Bind"/>；默认未绑定（null）。
///
/// 契约（保存 owner 必须遵守，运行时按此防守）：
/// * <see cref="Availability"/> 是唯一详细评估（暂停/加载/保存/联机/权限/身份与存档故障
///   分别可辨，读取异常不抛）；<see cref="Ready"/> 只投影"全部交易门通过"，绝不放松。
///   false 只冻结经济，绝不清空/丢弃任何 state；普通读档、存档切换、world 替换期间应给 false。
/// * <see cref="Owned"/> 与 <see cref="Purse"/> 由 owner 拥有；运行时/商店只读，
///   卸载场景、关闭功能、回池都不是死亡，绝不释放名额或清空余额。
/// * <see cref="TryRecordRecruitment"/> 只允许两种确定结果：true = 已确定记录为拥有；
///   false = 已确定未写入（调用方可走原生退款，角色不生成）。抛异常或结果不可判定
///   时运行时锁住本次付款上下文并有界诊断，不退款、不重购、不猜结果。
/// </summary>
internal interface ICoinCourierCampaignState
{
    CoinCourierAvailabilityInfo Availability { get; }
    bool Ready { get; }
    bool Owned { get; }
    CoinCourierPurse Purse { get; }
    bool TryRecordRecruitment();
}

/// <summary>
/// 对存档 owner 的防守式只读包装：owner 的 getter 抛异常/返回空一律按"未就绪"处理
/// （fail-closed），绝不让外部实现的故障把运行时推进到收费/取币路径。
/// </summary>
internal static class CoinCourierCampaignAccess
{
    internal static bool TryReadReady(ICoinCourierCampaignState state, out bool ready)
    {
        ready = false;
        if (state == null) return false;
        try { ready = state.Ready; return true; }
        catch { return false; }
    }

    /// <summary>详细就绪投影；读取异常按"不可读"（false），绝不外抛。</summary>
    internal static bool TryReadAvailability(ICoinCourierCampaignState state, out CoinCourierAvailabilityInfo info)
    {
        info = default;
        if (state == null) return false;
        try { info = state.Availability; return true; }
        catch { return false; }
    }

    internal static bool TryReadOwned(ICoinCourierCampaignState state, out bool owned)
    {
        owned = false;
        if (state == null) return false;
        try { owned = state.Owned; return true; }
        catch { return false; }
    }

    internal static bool TryReadPurse(ICoinCourierCampaignState state, out CoinCourierPurse purse)
    {
        purse = null;
        if (state == null) return false;
        try { purse = state.Purse; }
        catch { purse = null; }
        return purse != null;
    }

    /// <summary>只有 owner 明确 Ready 且能读出钱袋时，运行时才会进入 Open 行为。</summary>
    internal static bool TryReadEconomy(ICoinCourierCampaignState state, out bool ready, out bool owned,
        out CoinCourierPurse purse, out bool faulted)
    {
        ready = false;
        owned = false;
        purse = null;
        faulted = false;
        if (state == null) return false;
        try
        {
            ready = state.Ready;
            owned = state.Owned;
            purse = state.Purse;
            faulted = purse != null && purse.IsFaulted;
            return purse != null;
        }
        catch
        {
            purse = null;
            return false;
        }
    }

    /// <summary>
    /// 记录招募。返回 false = 调用抛异常/结果不可判定（未知，调用方必须锁住上下文）；
    /// recorded = 明确结果（true 已拥有 / false 确定未写入）。
    /// </summary>
    internal static bool TryRecordRecruitment(ICoinCourierCampaignState state, out bool recorded)
    {
        recorded = false;
        if (state == null) return false;
        try
        {
            recorded = state.TryRecordRecruitment();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
