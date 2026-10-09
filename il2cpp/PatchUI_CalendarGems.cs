using System;
using System.Globalization;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// issue-199：当前离线普通战役储存钻石的只读 HUD 缓存。
/// 在 CalendarHud 既有半秒 Tick 内读取已加载的 GlobalSaveData._loaded 和明确当前槽位，
/// 不调用会在未就绪时构造存档的 loaded getter，不保留原生对象，不读钱包或写余额。
/// 联网/挑战来源未核实，显示未知；共享战役库存只显示一次。
/// </summary>
internal static class PatchUI_CalendarGems
{
    private const string UnknownText = "—";
    private const string StorageCaption = "储存钻石";

    private static string _valueText = UnknownText;
    private static bool _faultLogged;

    /// <summary>当前战役存放钻石数量，N0 格式；未就绪/不可信为 "—"，0 只在确实读到 0 时显示。</summary>
    internal static string ValueText => _valueText;

    /// <summary>固定标题"储存钻石"；存放余额属于整个战役，双本机也只显示一次。</summary>
    internal static string CaptionText => StorageCaption;

    internal static void Clear()
    {
        _valueText = UnknownText;
    }

    /// <summary>保留共享调用签名；kingdom 不参与统计。异常/未就绪不猜零或回退到其他战役。</summary>
    internal static void Refresh(Kingdom kingdom, Transform scene)
    {
        _valueText = UnknownText;
        try
        {
            if (scene == null || NetworkBigBoss.IsOnline) return;
            GlobalSaveData global = GlobalSaveData._loaded;
            if (global == null || global.Pointer == IntPtr.Zero || global._currentChallenge != 0) return;
            int slot = global._currentCampaign;
            var campaigns = global.campaigns;
            if (campaigns == null || slot < 0 || slot >= campaigns.Count) return;
            CampaignSaveData campaign = campaigns[slot];
            if (campaign == null || campaign.Pointer == IntPtr.Zero) return;
            int stored = campaign.storedGems;
            if (stored >= 0) _valueText = stored.ToString("N0", CultureInfo.InvariantCulture);
        }
        catch (Exception e)
        {
            WarnFaultOnce(e);
        }
    }

    private static void WarnFaultOnce(Exception e)
    {
        if (_faultLogged) return;
        _faultLogged = true;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning(
                "[CalendarGems] campaign storage unreadable; gems shown as unknown: " + e.GetType().Name);
        }
        catch
        {
            // 日志通道自身故障也不能影响 HUD。
        }
    }
}
