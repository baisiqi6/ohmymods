using System.Runtime.CompilerServices;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// GUI.DrawTexture is an unstripped stub in this game's interop assembly and throws at runtime.
/// GUI.Box(Rect, GUIContent, GUIStyle) is a genuine native wrapper, so textures are drawn as the
/// background of a cached borderless style instead.
/// </summary>
internal static class ImGuiCompat
{
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
        return new GUIStyle
            {
                normal = { background = texture },
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                overflow = new RectOffset(0, 0, 0, 0),
                stretchWidth = true,
                stretchHeight = true,
                fixedWidth = 0,
                fixedHeight = 0,
                wordWrap = false,
                richText = false
            };
    }
}
