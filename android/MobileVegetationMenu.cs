using System;
using KingdomEnhancedMod;

namespace OhMyMods.AndroidProbe;

/// <summary>
/// 植被与野生动物页（presentation only，Issue #144）：三个开关行经 MobileModPanel.Toggle 的
/// 公共路径登记 tap rect 并绘制；不使用 GUI.Button、不自持固定坐标与样式。
/// 猫行只影响以后关卡载入时的补齐/瘦身（不删除已有猫）；森林行只作用于之后进入原生
/// FadeAndRemove 的调用（关闭不回收进行中的淡出）；鹿行只作用于之后自然进入原生
/// PopulationController.Update 的调用（希腊普通鹿目标/补充 x3；关闭不回收已有动物、不改存档）。
/// 返回世界行只清 VegetationPage，World 页保持打开。
/// </summary>
internal static class MobileVegetationMenu
{
    private static readonly Action ToggleFarmCats = ModConfig.ToggleFarmCats;
    private static readonly Action ToggleFastForestRecede = ModConfig.ToggleFastForestRecede;
    private static readonly Action ToggleDeerPopulation = ModConfig.ToggleDeerPopulation;

    internal static void Draw(MobileModPanel panel, float width, float scale)
    {
        panel.Toggle("农舍猫补给", ModConfig.FarmCatsEnabled.Value,
            "下次关卡载入生效；已有猫保留。", ToggleFarmCats);
        panel.Toggle("森林快速消退", ModConfig.FastForestRecedeEnabled.Value,
            "之后开始的消退等待缩至三分之一；关闭不回收进行中的淡出。", ToggleFastForestRecede);
        panel.Toggle("普通鹿数量", ModConfig.DeerPopulationEnabled.Value,
            "仅希腊普通鹿：目标与补充 ×3；关闭不回收已有动物。", ToggleDeerPopulation);
        panel.Step("返回世界", "←", "回到世界设置。", panel.CloseVegetation);
    }
}
