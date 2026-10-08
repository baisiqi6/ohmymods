using System;
using KingdomEnhancedMod;
using UnityEngine;

namespace OhMyMods.AndroidProbe;

// Issue #188：只读随身钻石 HUD 的两个被动单行 Label（左日期、右钻石），由既有
// MobileCalendar.Draw(FloatLayout) 入口调用。本类不驱动 Tick、不 Refresh 钱包、不 catch/log/
// 锁存（故障责任仍在 MobileCalendar）；文本只来自调用方日期行与共享 PatchUI_CalendarGems 的
// ValueText/CaptionText 只读缓存，不重算余额、不碰游戏状态、不做输入。
internal static class PatchUI_MobileCalendarHud
{
    private static readonly Color Gold = new Color(.93f, .78f, .47f, 1f);
    // 唯一 style 生命周期：首次绘制惰性创建（自 GUI.skin.label 复制），之后只随 scale 改字号；
    // 无每帧 new、不镜像 GUI 状态。
    private static GUIStyle style;

    internal static void Draw(string calendar, FloatLayout layout)
    {
        float scale = layout.Scale;
        float safeWidth = layout.SafeRight - layout.SafeLeft;
        float width = Math.Min(900 * scale, safeWidth - 16 * scale);
        if (width <= 0f) return;                              // 可用宽非正：直接不画
        if (layout.SafeTop + 6 * scale + 30 * scale > layout.SafeBottom) return; // 8x8 退化区等：不撑开安全区
        float height = 30 * scale;
        float x = layout.SafeLeft + (safeWidth - width) / 2; // 在既有安全区内水平居中
        float y = layout.SafeTop + 6 * scale;
        float leftWidth = width * 0.6f;
        float rightWidth = width - leftWidth;
        var color = GUI.color;
        var contentColor = GUI.contentColor;
        try
        {
            if (style == null)
            {
                var created = new GUIStyle(GUI.skin.label);
                created.alignment = TextAnchor.MiddleLeft;
                created.wordWrap = false;
                style = created; // 所有一次性配置成功后才交给共享缓存。
            }
            style.fontSize = Math.Max(8, (int)(18 * scale));
            GUI.color = Color.white;
            GUI.contentColor = Gold;
            GUI.Label(new Rect(x, y, leftWidth, height), calendar, style);
            GUI.Label(new Rect(x + leftWidth, y, rightWidth, height), GemsText(), style);
        }
        finally { GUI.color = color; GUI.contentColor = contentColor; } // 实际写入的两个 GUI 颜色只在 finally 恢复
    }

    // 单槽：CaptionText + ' ' + ValueText（"随身钻石 <数量>"）。双槽时共享 ValueText 固定以 "1P " 前缀
    // 标识槽位身份，故 1P 内容在前、2P caption（"2P 钻石 <数量>"）在后，绝不倒置；两段全部复用共享
    // 只读文本，不重算余额。
    private static string GemsText()
    {
        string value = PatchUI_CalendarGems.ValueText;
        string caption = PatchUI_CalendarGems.CaptionText;
        return value.StartsWith("1P ", StringComparison.Ordinal) ? "钻石 " + value + " | " + caption : caption + " " + value;
    }
}
