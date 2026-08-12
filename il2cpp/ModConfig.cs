using BepInEx.Configuration;

namespace KingdomEnhancedMod;

/// <summary>
/// 全局设置（BepInConfig 承载，替代 UMM Settings）。
/// 对应 Mono 版 Main.cs 的设置项。开箱即用：默认值即目标值，用户改
/// BepInEx/config/KingdomEnhancedMod.cfg 即可，无需游戏内 UI。
/// </summary>
public static class ModConfig
{
    public static ConfigEntry<bool> Enabled;
    public static ConfigEntry<bool> InfiniteMoney;
    public static ConfigEntry<int> SpeedMultiplier;
    public static ConfigEntry<bool> FastBuild;
    public static ConfigEntry<float> MapSizeMultiplier;
    public static ConfigEntry<float> EnemyCountMultiplier;
    public static ConfigEntry<float> EnemyTimelineSpeed;

    public static void Init(ConfigFile config)
    {
        Enabled = config.Bind("General", "Enabled", true,
            "总开关：关闭后所有 patch 走原版逻辑");

        InfiniteMoney = config.Bind("Economy", "InfiniteMoney", false,
            "无限金币：开启后玩家金币用不完");

        SpeedMultiplier = config.Bind("Player", "SpeedMultiplier", 2,
            "君主移动速度倍率（1-5x）");

        FastBuild = config.Bind("Build", "FastBuild", false,
            "快速建造：建筑约 2 秒建成");

        MapSizeMultiplier = config.Bind("World", "MapSizeMultiplier", 2f,
            "地图大小倍率（1-5x）");

        EnemyCountMultiplier = config.Bind("Enemy", "EnemyCountMultiplier", 1f,
            "每波怪物数量倍率（1-5x）");

        EnemyTimelineSpeed = config.Bind("Enemy", "EnemyTimelineSpeed", 1f,
            "怪物时间线推进速度倍率（1-5x）");
    }
}
