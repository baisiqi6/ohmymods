using System;
using MelonLoader;
using KingdomEnhancedMod;

namespace OhMyMods.AndroidProbe;

/// <summary>
/// 职业与弓弩页（presentation only，Issue #232）：九个真实设置经 MobileModPanel.Step/Toggle
/// 公共路径登记 tap rect 并绘制，职业状态经 Info 复用既有只读 getter；不使用 GUI.Button、
/// 不自持固定坐标与样式，Draw 只读取（比例显示归一不写回、不落盘）。
/// 六个开关各自反转对应真实 entry，同一次点按内回执一条日志并调用 ModConfig.Save 一次；
/// 三个档位沿用共享语义——弩手比例只有 CrossbowRatioPolicy 的 25/50/75/100 四档
/// （0 等非法值按 25% 起跳），散射支数 1..3 循环（含主箭），射速倍率 1→1.25→1.5→1.75→2→1；
/// 合法非档位射速（如 1.37）读取原样显示、不写回，点按才进入下一更大 0.25 档。
/// 加载边界已在 MobilePlayerConfig 归一/clamp，本页不重复 clamp/fallback。
/// 开关的运行时消费由既有 Probe.OnUpdate 与各子系统 Tick 入口推进，本页不调用
/// Tick/Clear/Spawn/重算；火铳铺/英雄驿站状态行直接返回缓存字段，盾卫状态行含既有
/// native prefs/current world 只读访问——Android 实机触摸、保存与这些 native 读取未验证；
/// 绘制异常沿用既有 renderFailed→Hide，不在此包装。
/// 返回世界行只清 ProfessionPage，World 页保持打开。
/// </summary>
internal static class MobileProfessionMenu
{
    private static readonly Action CycleCrossbowRatio = AdvanceCrossbowRatio;
    private static readonly Action ToggleMusketeer = FlipMusketeer;
    private static readonly Action ToggleHeroArcher = FlipHeroArcher;
    private static readonly Action ToggleHeavyShield = FlipHeavyShield;
    private static readonly Action ToggleScatter = FlipScatter;
    private static readonly Action CycleVolley = AdvanceVolley;
    private static readonly Action ToggleRate = FlipRate;
    private static readonly Action CycleRateMultiplier = AdvanceRateMultiplier;
    private static readonly Action ToggleImpact = FlipImpact;

    internal static void Draw(MobileModPanel panel, float width, float scale)
    {
        panel.Step("普通弩手招募比例", CrossbowRatioPolicy.Percent(ModConfig.CrossbowRecruitmentRatio.Value) + "%",
            "新捡弓立即生效；已有单位下次读档重算。弩手不参与骑士/举旗补员，已有队员不改；联机双方请使用相同比例。",
            CycleCrossbowRatio);
        panel.Toggle("火铳铺", ModConfig.MusketeerEnabled.Value,
            "所有世界·单机：4金币买枪转职，不上塔；举旗另带最多4名火枪手，白天猎鹿、不伤小动物。关闭保留职业记录。",
            ToggleMusketeer);
        panel.Info("火铳铺状态", MusketeerShop.StatusText, "");
        panel.Toggle("英雄驿站", ModConfig.HeroArcherEnabled.Value,
            "所有世界·单机：投8金币训练地面英雄，不上箭塔；每侧1名，死亡才空位。商店红旗表示占位，关闭保留已购名额。",
            ToggleHeroArcher);
        panel.Info("英雄驿站状态", HeroShop.StatusText, "");
        panel.Toggle("宝石盾卫", ModConfig.HeavyShieldEnabled.Value,
            "所有世界·单机：4宝石解锁盾模，6金币购买盾具；每岛左右各1席，可各花2宝石扩至2席。驻守本岛，不随船，不自动补货。",
            ToggleHeavyShield);
        panel.Info("宝石盾卫状态", HeavyShieldIdentity.StatusText, "");
        panel.Toggle("中世纪随从散射", ModConfig.ArcherScatterEnabled.Value,
            "所有世界 · 仅中世纪骑士的弓箭手随从对敌散射；打猎单发，额外箭淡金色，总数含主箭。",
            ToggleScatter);
        panel.Step("散射箭支数", ModConfig.ArcherVolleyCount.Value + " 支 / 发",
            "散射开启时生效；总数含主箭。", CycleVolley);
        panel.Toggle("弓箭手射速", ModConfig.ArcherRateEnabled.Value,
            "所有世界 · 最高 2 倍，关闭恢复原版；不加快移动和游戏时间。", ToggleRate);
        panel.Step("射速倍率", ModConfig.ArcherRateMultiplier.Value.ToString("0.##") + " 倍",
            "射速开关开启时生效。", CycleRateMultiplier);
        panel.Toggle("希腊随从火矢爆发", ModConfig.ArcherImpactEnabled.Value,
            "随从火矢以半径 0.25、一次 1 点范围伤害替代灼烧；直击不叠加，同轮散射去重。关闭恢复原版。",
            ToggleImpact);
        panel.Step("返回世界", "←", "回到世界设置。", panel.CloseProfession);
    }

    // ---- 点按动作：唯一写入点。每个动作恰好一次真实 entry 写 + 一次回执 + 一次 Save；
    //      无镜像/回滚/重试，不触发 Tick/扫描/recompute，运行时消费由既有子系统推进。 ----

    private static void FlipMusketeer()
    {
        ModConfig.MusketeerEnabled.Value = !ModConfig.MusketeerEnabled.Value;
        MelonLogger.Msg("ANDROID_PROFESSION_MUSKETEER enabled=" + ModConfig.MusketeerEnabled.Value);
        ModConfig.Save();
    }

    private static void FlipHeroArcher()
    {
        ModConfig.HeroArcherEnabled.Value = !ModConfig.HeroArcherEnabled.Value;
        MelonLogger.Msg("ANDROID_PROFESSION_HERO_ARCHER enabled=" + ModConfig.HeroArcherEnabled.Value);
        ModConfig.Save();
    }

    private static void FlipHeavyShield()
    {
        ModConfig.HeavyShieldEnabled.Value = !ModConfig.HeavyShieldEnabled.Value;
        MelonLogger.Msg("ANDROID_PROFESSION_HEAVY_SHIELD enabled=" + ModConfig.HeavyShieldEnabled.Value);
        ModConfig.Save();
    }

    private static void FlipScatter()
    {
        ModConfig.ArcherScatterEnabled.Value = !ModConfig.ArcherScatterEnabled.Value;
        MelonLogger.Msg("ANDROID_ARCHER_SCATTER enabled=" + ModConfig.ArcherScatterEnabled.Value);
        ModConfig.Save();
    }

    private static void FlipRate()
    {
        ModConfig.ArcherRateEnabled.Value = !ModConfig.ArcherRateEnabled.Value;
        MelonLogger.Msg("ANDROID_ARCHER_RATE enabled=" + ModConfig.ArcherRateEnabled.Value);
        ModConfig.Save();
    }

    private static void FlipImpact()
    {
        ModConfig.ArcherImpactEnabled.Value = !ModConfig.ArcherImpactEnabled.Value;
        MelonLogger.Msg("ANDROID_ARCHER_IMPACT enabled=" + ModConfig.ArcherImpactEnabled.Value);
        ModConfig.Save();
    }

    // 四档 25→50→75→100→25：按共享 Normalize 归一到合法档位后推进，非法载入值按 25% 起跳。
    private static void AdvanceCrossbowRatio()
    {
        float current = CrossbowRatioPolicy.Normalize(ModConfig.CrossbowRecruitmentRatio.Value);
        ModConfig.CrossbowRecruitmentRatio.Value = current == 1f ? .25f : current + .25f;
        MelonLogger.Msg("ANDROID_PROFESSION_CROSSBOW_RATIO percent="
            + CrossbowRatioPolicy.Percent(ModConfig.CrossbowRecruitmentRatio.Value));
        ModConfig.Save();
    }

    // 散射支数 1→2→3→1（含主箭总数）；不动散射开关本身，已射出的箭不追溯。
    private static void AdvanceVolley()
    {
        ModConfig.ArcherVolleyCount.Value = ModConfig.ArcherVolleyCount.Value % 3 + 1;
        MelonLogger.Msg("ANDROID_ARCHER_VOLLEY arrows=" + ModConfig.ArcherVolleyCount.Value);
        ModConfig.Save();
    }

    // 射速倍率 1→1.25→1.5→1.75→2→1；非档位合法值（如 1.37）点按进入下一更大 0.25 档，
    // 2 直接回 1。当前值不做 Round，保证点按结果严格大于原值或按档位回绕。
    private static void AdvanceRateMultiplier()
    {
        float next = MathF.Floor(ModConfig.ArcherRateMultiplier.Value * 4f) / 4f + .25f;
        ModConfig.ArcherRateMultiplier.Value = next > 2f ? 1f : next;
        MelonLogger.Msg("ANDROID_ARCHER_RATE_MULTIPLIER multiplier=" + ModConfig.ArcherRateMultiplier.Value);
        ModConfig.Save();
    }
}
