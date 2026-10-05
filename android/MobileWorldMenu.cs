using System;
using KingdomEnhancedMod;

namespace OhMyMods.AndroidProbe;

/// <summary>
/// 世界页（presentation only，Issue #144）：七个设置/入口行，全部经 MobileModPanel.Step/Toggle
/// 这一条公共路径登记 tap rect 并绘制；不使用 GUI.Button、不自持固定坐标与样式。
/// 植被与野生动物行只打开子页（Layout.VegetationPage=true，World 页保持打开）。
/// 快速建造行只影响后续原生 InitializeBuild 调用（关闭不热还原已写入实例的 rate）；
/// 船只行只作用于以后新初始化的船（关闭不删除/不热改已登记的 slots）。
/// </summary>
internal static class MobileWorldMenu
{
    private static readonly Action CycleEnemyCount = ModConfig.CycleEnemyCount;
    private static readonly Action CycleEnemyTimeline = ModConfig.CycleEnemyTimeline;
    private static readonly Action ToggleMoney = ModConfig.ToggleMoney;
    private static readonly Action ToggleFastBuild = ModConfig.ToggleFastBuild;
    private static readonly Action ToggleBoatCapacity = ModConfig.ToggleBoatCapacity;
    private static readonly Action ToggleNightDeparture = ModConfig.ToggleNightDeparture;

    internal static void Draw(MobileModPanel panel, float width, float scale)
    {
        panel.Step("每波怪物数量", ModConfig.EnemyCountMultiplier.Value + "x",
            "后续怪物波次生成时生效。", CycleEnemyCount);
        panel.Step("怪物时间线推进", ModConfig.EnemyTimelineSpeed.Value + "x",
            "后续进攻计算时生效；倍率越高，敌军成长越快。", CycleEnemyTimeline);
        panel.Toggle("无限金币", ModConfig.InfiniteMoney.Value,
            "立即生效 · 君主支付不再消耗金币。", ToggleMoney);
        panel.Step("植被与野生动物", "进入", "农舍猫、森林消退与普通鹿。", panel.OpenVegetation);
        panel.Toggle("快速建造", ModConfig.FastBuild.Value,
            "后续建造初始化生效；已写入的速率保持。", ToggleFastBuild);
        panel.Toggle("船只额外乘员", ModConfig.BoatCapacityEnabled.Value,
            "之后新初始化的船生效；已登记槽位不改变。", ToggleBoatCapacity);
        panel.Toggle("远距夜袭出发补偿", ModConfig.NightDepartureEnabled.Value,
            "仅之后的普通夜袭排期，最多提前两游戏小时；实际远距效果待验。", ToggleNightDeparture);
    }
}
