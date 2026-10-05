using System;
using KingdomEnhancedMod;

namespace OhMyMods.AndroidProbe;

/// <summary>
/// 植被与野生动物页（presentation only，Issue #144）：三个开关行经 MobileModPanel.Toggle 的
/// 公共路径登记 tap rect 并绘制；不使用 GUI.Button、不自持固定坐标与样式。
/// 猫行只影响以后关卡载入时的补齐/瘦身（不删除已有猫）；森林行只作用于之后进入原生
/// FadeAndRemove 的调用（关闭不回收进行中的淡出）；鹿行只作用于之后自然进入原生
/// PopulationController.Update 的调用（希腊普通鹿目标/补充 x3；关闭不回收已有动物、不改存档）。
/// 密灌木行是唯一请求入口：只调用一次共享 TrySet 请求（真实启停与额外实例回收由主线程 Tick
/// 推进；保存只在共享源三个真实写入点各发生一次，本页不落盘）。状态行仅在已开启或回收中显示：
/// 默认 OFF 且无自有责任时条件短路，展示不触 CurrentWorld。
/// 返回世界行只清 VegetationPage，World 页保持打开。
/// </summary>
internal static class MobileVegetationMenu
{
    private static readonly Action ToggleFarmCats = ModConfig.ToggleFarmCats;
    private static readonly Action ToggleFastForestRecede = ModConfig.ToggleFastForestRecede;
    private static readonly Action ToggleDeerPopulation = ModConfig.ToggleDeerPopulation;
    private static readonly Action ToggleDenseThickets = RequestDenseThickets;

    /// <summary>密灌木 UI 请求：恰好一次共享 TrySet（配置写与保存都在共享源内完成）。</summary>
    private static void RequestDenseThickets()
        => PatchWorld_OptionalVegetation.TrySetDenseThickets(!ModConfig.DenseThicketsEnabled.Value);

    internal static void Draw(MobileModPanel panel, float width, float scale)
    {
        panel.Toggle("农舍猫补给", ModConfig.FarmCatsEnabled.Value,
            "下次关卡载入生效；已有猫保留。", ToggleFarmCats);
        panel.Toggle("森林快速消退", ModConfig.FastForestRecedeEnabled.Value,
            "之后开始的消退等待缩至三分之一；关闭不回收进行中的淡出。", ToggleFastForestRecede);
        panel.Toggle("普通鹿数量", ModConfig.DeerPopulationEnabled.Value,
            "仅希腊普通鹿：目标与补充 ×3；关闭不回收已有动物。", ToggleDeerPopulation);
        panel.Toggle("密灌木", ModConfig.DenseThicketsEnabled.Value,
            "间距减半，额外灌木由原生生成；关闭只回收额外实例，回收中暂不能重开。", ToggleDenseThickets);
        // OFF 且无自有责任时 IsCleaning 以 managed 空表短路，不触 CurrentWorld；开启/回收中才展示状态。
        if (ModConfig.DenseThicketsEnabled.Value || PatchWorld_OptionalVegetation.IsCleaning)
            panel.Info("密灌木状态", PatchWorld_OptionalVegetation.DenseStatus, "回收完成前不会重新开启。");
        panel.Step("返回世界", "←", "回到世界设置。", panel.CloseVegetation);
    }
}
