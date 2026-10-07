using System;
using System.Globalization;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// issue-175：顶部日历 HUD 的"本机君主随身钻石"只读缓存。
/// 由 CalendarHud 既有 0.5 秒 Tick 调用 Refresh：每次都从当前 Kingdom 重新取两名玩家引用，
/// 不跨 scope 持有 Player/Wallet；只读钱包，不写游戏状态，不做扫描，不读存档/金库。
/// 任一槽位读取失败只让该槽位显示未知，绝不向调用方上抛异常（日历本体不因钻石故障被 Clear）。
/// ValueText/CaptionText 只在 Refresh/Clear 内更新。
/// </summary>
internal static class PatchUI_CalendarGems
{
    private const string UnknownText = "—";
    private const string PurseCaption = "随身钻石";

    private static string _valueText = UnknownText;
    private static string _captionText = PurseCaption;
    private static bool _faultLogged;

    /// <summary>单本机为裸数量，双本机保留槽位身份（余额未知显示—）为 "1P &lt;P1数量&gt;"，未知为 "—"。</summary>
    internal static string ValueText => _valueText;

    /// <summary>单钱包为 "随身钻石"，双本机为 "2P 钻石 &lt;P2数量&gt;"，未知保持 "随身钻石"。</summary>
    internal static string CaptionText => _captionText;

    internal static void Clear()
    {
        _valueText = UnknownText;
        _captionText = PurseCaption;
    }

    internal static void Refresh(Kingdom kingdom, Transform scene)
    {
        bool hasFirst = false, hasSecond = false;
        Slot first = default, second = default;
        try
        {
            if (kingdom != null && scene != null)
            {
                hasFirst = TryReadSlot(SafePlayer(kingdom, 0), scene, out first);
                hasSecond = TryReadSlot(SafePlayer(kingdom, 1), scene, out second);
                // 两个槽位引用同一本机玩家时不重复显示；不同玩家即使钱包错误别名也保留各自身份。
                if (hasFirst && hasSecond && first.Player == second.Player)
                    hasSecond = false;
            }
        }
        catch (Exception e)
        {
            // 兜底：kingdom/scene 自身访问失败时整体视为不可用，仍不抛给 CalendarHud。
            WarnFaultOnce(e);
            hasFirst = false;
            hasSecond = false;
        }

        if (hasFirst && hasSecond)
        {
            // 双本机各报各的，绝不合计成一个钱包。
            _valueText = "1P " + Format(first.Gems);
            _captionText = "2P 钻石 " + Format(second.Gems);
        }
        else if (hasFirst || hasSecond)
        {
            // 唯一本机玩家（含仅 P2）显示裸数字或未知，不误合并两名本机玩家。
            _valueText = Format(hasFirst ? first.Gems : second.Gems);
            _captionText = PurseCaption;
        }
        else
        {
            _valueText = UnknownText;
            _captionText = PurseCaption;
        }
    }

    private static Player SafePlayer(Kingdom kingdom, int slot)
    {
        try
        {
            return slot == 0 ? kingdom.playerOne : kingdom.playerTwo;
        }
        catch (Exception e)
        {
            WarnFaultOnce(e);
            return null;
        }
    }

    /// <summary>单槽位 O(1) 读取：先确认当前层活动本机玩家存在，再读余额；钱包缺失/故障不抹去玩家身份。</summary>
    private static bool TryReadSlot(Player player, Transform scene, out Slot slot)
    {
        slot = default;
        if (player == null) return false;
        try
        {
            if (!player.hasLocalAuthority) return false;            // 远程玩家钱包不混入
            GameObject go = player.gameObject;
            if (go == null || !go.activeInHierarchy) return false;  // 池化/未激活本体不读
            Transform transform = player.transform;
            if (transform == null || !transform.IsChildOf(scene)) return false; // 只认当前世界层
            IntPtr playerPtr = player.Pointer;
            if (playerPtr == IntPtr.Zero) return false;
            slot = new Slot(playerPtr, IntPtr.Zero, -1); // 已知本机槽位，余额暂未知
            Wallet wallet = player.wallet;
            if (wallet == null || wallet.Pointer == IntPtr.Zero) return true;
            slot = new Slot(playerPtr, wallet.Pointer, -1);
            Player owner = wallet._playerRef;                       // 现代 2.4 接口：钱包自引用
            if (owner == null || owner.Pointer != playerPtr) return true;
            int gems = wallet.Gems;                                 // readonly getter 路径
            if (gems < 0) return true;                             // 负值属于未知状态，不猜 0
            slot = new Slot(playerPtr, wallet.Pointer, gems);
            return true;
        }
        catch (Exception e)
        {
            WarnFaultOnce(e);
            return slot.Player != IntPtr.Zero; // 身份已确认时仅将余额保持未知
        }
    }

    private static string Format(int gems) => gems < 0 ? UnknownText : gems.ToString("N0", CultureInfo.InvariantCulture);

    private static void WarnFaultOnce(Exception e)
    {
        if (_faultLogged) return;
        _faultLogged = true;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning(
                "[CalendarGems] local wallet unreadable; gems shown as unknown: " + e.GetType().Name);
        }
        catch
        {
            // 日志通道自身故障也不能影响 HUD。
        }
    }

    private readonly struct Slot
    {
        internal readonly IntPtr Player, Wallet;
        internal readonly int Gems;

        internal Slot(IntPtr player, IntPtr wallet, int gems)
        {
            Player = player;
            Wallet = wallet;
            Gems = gems;
        }
    }
}
