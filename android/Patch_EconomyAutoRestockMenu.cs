using System;
using MelonLoader;
using KingdomEnhancedMod;

namespace OhMyMods.AndroidProbe;

/// <summary>
/// 经济页 · 九类自动补货（presentation only，Issue #262）：每类两行，开关走
/// MobileModPanel.Toggle，目标数量走 Root 统一提供的 MobileModPanel.Quantity；
/// 不画自定坐标/GUI.Button，不自持布局或手势，滚动与命中的裁剪由公共面板负责。
/// 类别顺序、中文标题与目标单位对齐 PC ModPanel.RestockControl；帮助沿用 PC
/// “双倍金库付款 · PatchEconomy_AutoRestock.GetSummary(role)”。GetSummary 只读
/// GreekBankScope.Current()、既有摘要缓存与开关 entry，不扫描、不写配置；本页 Draw
/// 只读当前 entry 与摘要，不 clamp、不落盘。
/// 九类顺序：工匠、弓箭手、火枪手·火枪、忍者、狂战士、无业村民·面包、农民·镰刀、
/// 投石车·火药桶、希腊火焰塔·弹药。role 6/7 的目标单位为“份”，其余为“人”。
/// 每次点按只改对应真实 entry 一次、记一条日志、Save 一次；目标到达 1/200 边界时
/// 值不变，直接返回，不写、不记、不 Save。Toggle 不调用 Tick/Ensure/Spawn；
/// 运行时消费由既有协调器推进。Android 实机触摸、原生采购与保存/读档未在本文件
/// 验证；绘制异常沿用外层 renderFailed→Hide，不在此吞异常。
/// 返回世界行复用 panel.CloseEconomy，World 页保持打开。
/// </summary>
internal static class MobileEconomyMenu
{
    private const int MinTarget = 1;
    private const int MaxTarget = 200;
    private const string HelpPrefix = "双倍金库付款 · ";

    private readonly struct Row
    {
        internal readonly int Role;
        internal readonly string Title;
        internal readonly string Unit;
        internal readonly Action Toggle;
        internal readonly Action Decrement;
        internal readonly Action Increment;

        internal Row(int role, string title, string unit)
        {
            Role = role;
            Title = title;
            Unit = unit;
            // 只捕获 role，不捕获配置 entry；entry 在 Draw/点按时按当前 ModConfig 字段读取。
            Toggle = () => ToggleRole(role);
            Decrement = () => DecrementTarget(role);
            Increment = () => IncrementTarget(role);
        }
    }

    // 与 PC ModPanel.RestockControl 完全同序：0、1、8、2、3、4、5、6、7。
    private static readonly Row[] Rows =
    {
        new Row(0, "工匠", "人"),
        new Row(1, "弓箭手", "人"),
        new Row(8, "火枪手 · 火枪", "人"),
        new Row(2, "忍者", "人"),
        new Row(3, "狂战士", "人"),
        new Row(4, "无业村民 · 面包", "人"),
        new Row(5, "农民 · 镰刀", "人"),
        new Row(6, "投石车 · 火药桶", "份"),
        new Row(7, "希腊火焰塔 · 弹药", "份"),
    };

    internal static void Draw(MobileModPanel panel, float width, float scale)
    {
        foreach (Row row in Rows)
        {
            string help = HelpPrefix + PatchEconomy_AutoRestock.GetSummary(row.Role);
            panel.Toggle(row.Title + "自动补货", EnabledEntry(row.Role).Value, help, row.Toggle);
            panel.Quantity(row.Title + "目标（" + row.Unit + "）", TargetEntry(row.Role).Value, help,
                row.Decrement, row.Increment);
        }
        panel.Step("返回世界", "←", "回到世界设置。", panel.CloseEconomy);
    }

    // ---- 真实配置入口：每次调用按 role 取当前字段；静态初始化只建行与缓存委托，
    //      不读取 entry，避免早于 MobilePlayerConfig.Initialize 捕获 null/旧值。 ----

    private static MelonPreferences_Entry<bool> EnabledEntry(int role)
    {
        switch (role)
        {
            case 0: return ModConfig.AutoRestockWorkersEnabled;
            case 1: return ModConfig.AutoRestockArchersEnabled;
            case 8: return ModConfig.AutoRestockMusketeersEnabled;
            case 2: return ModConfig.AutoRestockNinjasEnabled;
            case 3: return ModConfig.AutoRestockBerserkersEnabled;
            case 4: return ModConfig.AutoRestockPeasantsEnabled;
            case 5: return ModConfig.AutoRestockFarmersEnabled;
            case 6: return ModConfig.AutoRestockCatapultBarrelsEnabled;
            case 7: return ModConfig.AutoRestockFireTowerAmmoEnabled;
            default: throw new ArgumentOutOfRangeException(nameof(role), role, "unknown auto-restock role");
        }
    }

    private static MelonPreferences_Entry<int> TargetEntry(int role)
    {
        switch (role)
        {
            case 0: return ModConfig.AutoRestockWorkersTarget;
            case 1: return ModConfig.AutoRestockArchersTarget;
            case 8: return ModConfig.AutoRestockMusketeersTarget;
            case 2: return ModConfig.AutoRestockNinjasTarget;
            case 3: return ModConfig.AutoRestockBerserkersTarget;
            case 4: return ModConfig.AutoRestockPeasantsTarget;
            case 5: return ModConfig.AutoRestockFarmersTarget;
            case 6: return ModConfig.AutoRestockCatapultBarrelsTarget;
            case 7: return ModConfig.AutoRestockFireTowerAmmoTarget;
            default: throw new ArgumentOutOfRangeException(nameof(role), role, "unknown auto-restock role");
        }
    }

    // ---- 点按动作：唯一写入点。每个动作一次真实 entry 写 + 一条日志 + 一次 Save；
    //      目标到边界时值不变，直接返回；不触发 Tick/扫描/池/ensure。 ----

    private static void ToggleRole(int role)
    {
        MelonPreferences_Entry<bool> entry = EnabledEntry(role);
        bool value = !entry.Value;
        entry.Value = value;
        MelonLogger.Msg("ANDROID_ECONOMY_AUTORESTOCK role=" + role + " enabled=" + value);
        ModConfig.Save();
    }

    private static void DecrementTarget(int role)
    {
        MelonPreferences_Entry<int> entry = TargetEntry(role);
        int current = entry.Value;
        int value = Math.Max(MinTarget, current - 1);
        if (value == current) return;
        entry.Value = value;
        MelonLogger.Msg("ANDROID_ECONOMY_AUTORESTOCK role=" + role + " target=" + value);
        ModConfig.Save();
    }

    private static void IncrementTarget(int role)
    {
        MelonPreferences_Entry<int> entry = TargetEntry(role);
        int current = entry.Value;
        int value = Math.Min(MaxTarget, current + 1);
        if (value == current) return;
        entry.Value = value;
        MelonLogger.Msg("ANDROID_ECONOMY_AUTORESTOCK role=" + role + " target=" + value);
        ModConfig.Save();
    }
}
