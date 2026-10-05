using System;
using KingdomEnhancedMod;

namespace OhMyMods.AndroidProbe;

/// <summary>
/// 玩家页（presentation only，Issue #144）：五个设置各一行，全部经 MobileModPanel.Step/Toggle
/// 这一条公共路径登记 tap rect 并绘制；不使用 GUI.Button、不自持固定坐标与样式。
/// 内容高度由面板 cursor 单点累计，本方法不返回高度。
/// 中文名与 PC 面板同源；两个冷却行说明“下次技能调用生效、只缩放该次/基础冷却、原生追加不缩放”。
/// </summary>
internal static class MobilePlayerMenu
{
    private static readonly Action CycleSpeed = ModConfig.CycleSpeed;
    private static readonly Action ToggleStamina = ModConfig.ToggleStamina;
    private static readonly Action ToggleHold = ModConfig.ToggleHold;
    private static readonly Action CycleSteedCooldown = ModConfig.CycleSteedCooldown;
    private static readonly Action CycleStaffCooldown = ModConfig.CycleStaffCooldown;

    internal static void Draw(MobileModPanel panel, float width, float scale)
    {
        panel.Step("君主移动速度", ModConfig.SpeedMultiplier.Value + "x", "移动时生效。", CycleSpeed);
        panel.Toggle("坐骑无限体力", ModConfig.InfiniteSteedStamina.Value,
            "本机坐骑奔跑与滑翔不耗体力；关闭恢复自然消耗，技能冷却不变。", ToggleStamina);
        panel.Toggle("长按连续购买", ModConfig.HoldPurchaseEnabled.Value,
            "长按后加速续买；支持火药桶与火塔弹药，松开即停。", ToggleHold);
        panel.Step("坐骑技能冷却", ModConfig.SteedCooldownMultiplier.Value + "x",
            "下次技能调用生效；原生冷却因坐骑而异。", CycleSteedCooldown);
        panel.Step("法杖神器冷却", ModConfig.StaffCooldownMultiplier.Value + "x",
            "只缩放基础冷却，下次使用生效；原生目标追加时间不缩放。", CycleStaffCooldown);
    }
}
