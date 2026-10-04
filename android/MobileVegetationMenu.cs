using UnityEngine;

namespace OhMyMods.AndroidProbe;

/// <summary>
/// Vegetation page of the float panel: the farm-cat stocking switch moved from the World page,
/// the fast forest-recede switch and Back. Geometry: 248x64 buttons at x=16, rows 98 cats /
/// 176 forest recede / 254 Back; the page base height is 328 (FloatLayout.PanelHeight), so the
/// Back row ends at 318 and stays inside the same rectangle the touch filter uses. The cat row
/// reuses the previous World-page copy unchanged and only switches future stocking (next
/// OnLevelLoaded); it never removes existing cats. The forest-recede row only arms the shared
/// ForestItem.FadeAndRemove prefix on later calls; turning it OFF leaves fades already started.
/// Back clears only VegetationPage, returning to the still-open World page
/// (ProbeTicker.Layout.WorldPage stays true).
/// </summary>
internal static class MobileVegetationMenu
{
    internal static void Draw(float px, float py, float scale, GUIStyle labelStyle, GUIStyle titleStyle, GUIStyle buttonStyle)
    {
        GUI.Label(new Rect(px + 16 * scale, py + 12 * scale, 248 * scale, 42 * scale), "Vegetation", titleStyle);
        GUI.Label(new Rect(px + 16 * scale, py + 54 * scale, 248 * scale, 40 * scale), "World vegetation", labelStyle);
        if (GUI.Button(new Rect(px + 16 * scale, py + 98 * scale, 248 * scale, 64 * scale),
                KingdomEnhancedMod.ModConfig.FarmCatsEnabled.Value
                    ? "Stock cats: ON\nnext level; cats stay"
                    : "Stock cats: OFF\nnext level; cats stay", buttonStyle))
            KingdomEnhancedMod.ModConfig.ToggleFarmCats();
        if (GUI.Button(new Rect(px + 16 * scale, py + 176 * scale, 248 * scale, 64 * scale),
                KingdomEnhancedMod.ModConfig.FastForestRecedeEnabled.Value
                    ? "Fast forest recede: ON\nnext fades; wait /3"
                    : "Fast forest recede: OFF\nnext fades; wait /3", buttonStyle))
            KingdomEnhancedMod.ModConfig.ToggleFastForestRecede();
        if (GUI.Button(new Rect(px + 16 * scale, py + 254 * scale, 248 * scale, 64 * scale), "Back", buttonStyle))
            ProbeTicker.Layout.VegetationPage = false;
    }
}
