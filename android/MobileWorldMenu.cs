using UnityEngine;

namespace OhMyMods.AndroidProbe;

/// <summary>
/// World page of the float panel: enemy count multiplier / threat-growth multiplier /
/// native infinite-money flag and Back. Geometry matches the shipped row layout
/// (248x64 buttons at x=16, rows 98/176/254/332); the World page base height is 406
/// (FloatLayout.PanelHeight), so the last row ends at 396 and stays inside the same
/// rectangle the touch filter uses. Back reuses ProbeTicker.Layout.WorldPage = false.
/// </summary>
internal static class MobileWorldMenu
{
    internal static void Draw(float px, float py, float scale, GUIStyle labelStyle, GUIStyle titleStyle, GUIStyle buttonStyle)
    {
        GUI.Label(new Rect(px + 16 * scale, py + 12 * scale, 248 * scale, 42 * scale), "World", titleStyle);
        GUI.Label(new Rect(px + 16 * scale, py + 54 * scale, 248 * scale, 40 * scale), "World settings", labelStyle);
        if (GUI.Button(new Rect(px + 16 * scale, py + 98 * scale, 248 * scale, 64 * scale),
                "Enemies: " + KingdomEnhancedMod.ModConfig.EnemyCountMultiplier.Value + "x", buttonStyle))
            KingdomEnhancedMod.ModConfig.CycleEnemyCount();
        if (GUI.Button(new Rect(px + 16 * scale, py + 176 * scale, 248 * scale, 64 * scale),
                "Threat growth: " + KingdomEnhancedMod.ModConfig.EnemyTimelineSpeed.Value + "x", buttonStyle))
            KingdomEnhancedMod.ModConfig.CycleEnemyTimeline();
        if (GUI.Button(new Rect(px + 16 * scale, py + 254 * scale, 248 * scale, 64 * scale),
                KingdomEnhancedMod.ModConfig.InfiniteMoney.Value ? "Infinite money: ON" : "Infinite money: OFF", buttonStyle))
            KingdomEnhancedMod.ModConfig.ToggleMoney();
        if (GUI.Button(new Rect(px + 16 * scale, py + 332 * scale, 248 * scale, 64 * scale), "Back", buttonStyle))
            ProbeTicker.Layout.WorldPage = false;
    }
}
