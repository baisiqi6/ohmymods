using UnityEngine;

namespace OhMyMods.AndroidProbe;

/// <summary>
/// Player page of the float panel: speed / stamina / hold-purchase toggles and Back.
/// Geometry matches the shipped 0.0.8 row layout (248x64 buttons at x=16, rows 98/176/254/332)
/// plus the new hold row; the Player page base height is 406 (FloatLayout.PanelHeight), so the
/// last row ends at 396 and stays inside the same rectangle the touch filter uses.
/// Back reuses ProbeTicker.Layout.PlayerPage = false.
/// </summary>
internal static class MobilePlayerMenu
{
    internal static void Draw(float px, float py, float scale, GUIStyle labelStyle, GUIStyle titleStyle, GUIStyle buttonStyle)
    {
        GUI.Label(new Rect(px + 16 * scale, py + 12 * scale, 248 * scale, 42 * scale), "Player", titleStyle);
        GUI.Label(new Rect(px + 16 * scale, py + 54 * scale, 248 * scale, 40 * scale), "Device settings", labelStyle);
        if (GUI.Button(new Rect(px + 16 * scale, py + 98 * scale, 248 * scale, 64 * scale),
                "Speed: " + KingdomEnhancedMod.ModConfig.SpeedMultiplier.Value + "x", buttonStyle))
            KingdomEnhancedMod.ModConfig.CycleSpeed();
        if (GUI.Button(new Rect(px + 16 * scale, py + 176 * scale, 248 * scale, 64 * scale),
                KingdomEnhancedMod.ModConfig.InfiniteSteedStamina.Value ? "Stamina: unlimited" : "Stamina: normal", buttonStyle))
            KingdomEnhancedMod.ModConfig.ToggleStamina();
        if (GUI.Button(new Rect(px + 16 * scale, py + 254 * scale, 248 * scale, 64 * scale),
                KingdomEnhancedMod.ModConfig.HoldPurchaseEnabled.Value ? "Hold purchase: ON" : "Hold purchase: OFF", buttonStyle))
            KingdomEnhancedMod.ModConfig.ToggleHold();
        if (GUI.Button(new Rect(px + 16 * scale, py + 332 * scale, 248 * scale, 64 * scale), "Back", buttonStyle))
            ProbeTicker.Layout.PlayerPage = false;
    }
}
