using System;
using KingdomEnhancedMod;

namespace OhMyMods.AndroidProbe;

/// <summary>
/// 岛屿生成与建筑点位：复用公共 Step/Toggle 触屏控件。点位 hooks 只在启动时选择，
/// 已有原生持久化点位不会随整组停用而删除；密度退役沿共享源加载/边界路径执行。
/// </summary>
internal static class MobileGenerationMenu
{
    private static readonly Action CycleMapSize = ModConfig.CycleMapSize;
    private static readonly Action ToggleWorldLayout = ModConfig.ToggleWorldLayout;
    private static readonly Action CycleTowerSpots = ModConfig.CycleTowerSpotMultiplier;
    private static readonly Action CycleWallSpots = ModConfig.CycleWallSpotMultiplier;

    internal static void Draw(MobileModPanel panel, float width, float scale)
    {
        panel.Step("地图大小", ModConfig.MapSizeMultiplier.Value + "x", "仅新岛生成生效；已生成岛不变。", CycleMapSize);
        panel.Toggle("建筑点位调整", ModConfig.WorldLayoutEnabled.Value,
            "仅单机 · 重启游戏后应用开关；关闭保留已有点位。", ToggleWorldLayout);
        panel.Step("箭塔基底密度", ModConfig.TowerSpotMultiplier.Value + "x",
            "开启点位调整后，重新载入地图生效；1倍回收可删未建点位。", CycleTowerSpots);
        panel.Step("墙基密度", ModConfig.WallSpotMultiplier.Value + "x",
            "开启后重载或边界扩张生效；1倍回收可删未建点位，实际密度受间距限制。", CycleWallSpots);
    }
}
