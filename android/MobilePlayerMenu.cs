using UnityEngine;

namespace OhMyMods.AndroidProbe;

/// <summary>
/// Player page of the float panel: speed / stamina / hold-purchase / steed-cooldown /
/// staff-base-cooldown and Back. Geometry keeps the shipped row layout (248x64 buttons at x=16,
/// rows 98/176/254/332/410/488); the Player page base height is 562 (FloatLayout.PanelHeight),
/// so the last row ends at 552 and stays inside the same rectangle the touch filter uses. Both
/// cooldown labels are stated relative to the call-time cooldown (next ability call), not to a
/// prefab value or a retroactive schedule; the staff label names the base portion only (the
/// native per-target additive time stays native). Back reuses
/// ProbeTicker.Layout.PlayerPage = false.
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
        if (GUI.Button(new Rect(px + 16 * scale, py + 332 * scale, 248 * scale, 64 * scale),
                "Cooldown: " + KingdomEnhancedMod.ModConfig.SteedCooldownMultiplier.Value + "x next call", buttonStyle))
            KingdomEnhancedMod.ModConfig.CycleSteedCooldown();
        if (GUI.Button(new Rect(px + 16 * scale, py + 410 * scale, 248 * scale, 64 * scale),
                "Staff base cooldown: " + KingdomEnhancedMod.ModConfig.StaffCooldownMultiplier.Value + "x", buttonStyle))
            KingdomEnhancedMod.ModConfig.CycleStaffCooldown();
        if (GUI.Button(new Rect(px + 16 * scale, py + 488 * scale, 248 * scale, 64 * scale), "Back", buttonStyle))
            ProbeTicker.Layout.PlayerPage = false;
    }
}
