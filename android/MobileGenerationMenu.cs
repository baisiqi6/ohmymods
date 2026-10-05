using System;
using KingdomEnhancedMod;

namespace OhMyMods.AndroidProbe;

/// <summary>
/// 岛屿生成页（presentation only，Issue #144）：地图大小一行经 MobileModPanel.Step 的公共路径
/// 登记 tap rect 并绘制；不使用 GUI.Button、不自持固定坐标与样式。倍率只被新岛的原生
/// GenerateInternal scope 读取：不改变已生成岛、不追溯/不重生成。
/// </summary>
internal static class MobileGenerationMenu
{
    private static readonly Action CycleMapSize = ModConfig.CycleMapSize;

    internal static void Draw(MobileModPanel panel, float width, float scale)
        => panel.Step("地图大小", ModConfig.MapSizeMultiplier.Value + "x", "仅新岛生成生效；已生成岛不变。", CycleMapSize);
}
