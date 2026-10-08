using System.Runtime.CompilerServices;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// GUI.DrawTexture is an unstripped stub in this game's interop assembly and throws at runtime.
/// GUI.Box(Rect, GUIContent, GUIStyle) is a genuine native wrapper, so textures are drawn as the
/// background of a cached borderless style instead.
/// </summary>
internal static partial class ImGuiCompat
{
#if !ANDROID
    // Weak keys: CalendarHud recreates its textures after domain reloads, and orphaned entries
    // must not keep destroyed textures alive or serve stale styles for new instances.
    private static readonly ConditionalWeakTable<Texture2D, GUIStyle> TextureStyles = new();
    private static readonly ConditionalWeakTable<Texture2D, GUIStyle>.CreateValueCallback CreateStyle = BuildStyle;

    internal static void DrawTexture(Rect rect, Texture2D texture)
    {
        if (texture == null) return;
        GUIStyle style = TextureStyles.GetValue(texture, CreateStyle);
        // GUI.Box is a passive control; the empty content leaves only the texture background.
        GUI.Box(rect, GUIContent.none, style);
    }

    private static GUIStyle BuildStyle(Texture2D texture)
    {
        // 整体 RectOffset 赋值（border/padding/margin/overflow）在 interop 下同样经过缺失的
        // GUIStyle::AssignRectOffset_Injected 桥（Issue #144）；这里与 ModPanelStyles 一致，
        // 经 getter 取回自有 RectOffset 后用共享 WriteOffset 逐字段写全 0。
        GUIStyle style = new GUIStyle();
        style.normal.background = texture;
        ModPanelStyles.WriteOffset(style.border, 0, 0, 0, 0);
        ModPanelStyles.WriteOffset(style.padding, 0, 0, 0, 0);
        ModPanelStyles.WriteOffset(style.margin, 0, 0, 0, 0);
        ModPanelStyles.WriteOffset(style.overflow, 0, 0, 0, 0);
        style.stretchWidth = true;
        style.stretchHeight = true;
        style.fixedWidth = 0;
        style.fixedHeight = 0;
        style.wordWrap = false;
        style.richText = false;
        return style;
    }
#endif



    // 单份 Rect 交集（原 MobileModPanel 私有实现下沉）：面板 hit 裁剪与 native 背景
    // 视觉裁剪共用同一数学，不复制两份。
    internal static Rect IntersectRect(Rect a, Rect b)
    {
        float xMin = Mathf.Max(a.xMin, b.xMin), xMax = Mathf.Min(a.xMax, b.xMax);
        float yMin = Mathf.Max(a.yMin, b.yMin), yMax = Mathf.Min(a.yMax, b.yMax);
        return xMax > xMin && yMax > yMin ? new Rect(xMin, yMin, xMax - xMin, yMax - yMin) : new Rect(0f, 0f, 0f, 0f);
    }
}
