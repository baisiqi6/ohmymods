using UnityEngine;

namespace OhMyMods.AndroidProbe;

/// <summary>
/// Island-generation page of the float panel: the map-length multiplier for newly generated
/// islands and Back. Geometry keeps the shipped row layout (248x64 buttons at x=16, rows
/// 98 Length / 176 Back); the page base height is 250 (FloatLayout.PanelHeight), so Back ends
/// at 240 and stays inside the same rectangle the touch filter uses. The value is only read by
/// the native GenerateInternal scope of a future island generation: it neither resizes the
/// current island nor regenerates it, and 1x keeps the native layout. Back reuses
/// ProbeTicker.Layout.GenerationPage = false.
/// </summary>
internal static class MobileGenerationMenu
{
    internal static void Draw(float px, float py, float scale, GUIStyle labelStyle, GUIStyle titleStyle, GUIStyle buttonStyle)
    {
        GUI.Label(new Rect(px + 16 * scale, py + 12 * scale, 248 * scale, 42 * scale), "Island generation", titleStyle);
        GUI.Label(new Rect(px + 16 * scale, py + 54 * scale, 248 * scale, 40 * scale), "New islands only", labelStyle);
        if (GUI.Button(new Rect(px + 16 * scale, py + 98 * scale, 248 * scale, 64 * scale),
                "Length: " + KingdomEnhancedMod.ModConfig.MapSizeMultiplier.Value + "x", buttonStyle))
            KingdomEnhancedMod.ModConfig.CycleMapSize();
        if (GUI.Button(new Rect(px + 16 * scale, py + 176 * scale, 248 * scale, 64 * scale), "Back", buttonStyle))
            ProbeTicker.Layout.GenerationPage = false;
    }
}
