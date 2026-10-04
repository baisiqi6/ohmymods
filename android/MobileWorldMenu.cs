using UnityEngine;

namespace OhMyMods.AndroidProbe;

/// <summary>
/// World page of the float panel: enemy count multiplier / threat-growth multiplier /
/// native infinite-money flag / farm-cat stocking / fast-build prefix / extra boat crew and Back.
/// Geometry matches the shipped row layout (248x64 buttons at x=16, rows 98/176/254/332/410/488/566);
/// the World page base height is 640 (FloatLayout.PanelHeight), so the Back row ends at 630 and
/// stays inside the same rectangle the touch filter uses. The cat row only switches future
/// stocking (next OnLevelLoaded); it never removes existing cats. The fast-build row only arms the
/// rate prefix on every native InitializeBuild call (before its _hasStarted early return); turning
/// it OFF leaves rates already written on that instance. The boat row only arms the capacity
/// borrow on later Boat.OnEnable calls; it never resizes already registered slots. Back reuses
/// ProbeTicker.Layout.WorldPage = false.
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
        if (GUI.Button(new Rect(px + 16 * scale, py + 332 * scale, 248 * scale, 64 * scale),
                KingdomEnhancedMod.ModConfig.FarmCatsEnabled.Value
                    ? "Stock cats: ON\nnext level; cats stay"
                    : "Stock cats: OFF\nnext level; cats stay", buttonStyle))
            KingdomEnhancedMod.ModConfig.ToggleFarmCats();
        if (GUI.Button(new Rect(px + 16 * scale, py + 410 * scale, 248 * scale, 64 * scale),
                KingdomEnhancedMod.ModConfig.FastBuild.Value
                    ? "Fast build: ON\ninit call; rates stay"
                    : "Fast build: OFF\ninit call; rates stay", buttonStyle))
            KingdomEnhancedMod.ModConfig.ToggleFastBuild();
        if (GUI.Button(new Rect(px + 16 * scale, py + 488 * scale, 248 * scale, 64 * scale),
                KingdomEnhancedMod.ModConfig.BoatCapacityEnabled.Value
                    ? "Extra boat crew: ON\nNewly initialized boats"
                    : "Extra boat crew: OFF\nNewly initialized boats", buttonStyle))
            KingdomEnhancedMod.ModConfig.ToggleBoatCapacity();
        if (GUI.Button(new Rect(px + 16 * scale, py + 566 * scale, 248 * scale, 64 * scale), "Back", buttonStyle))
            ProbeTicker.Layout.WorldPage = false;
    }
}
