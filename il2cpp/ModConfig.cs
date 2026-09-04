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
    public static ConfigEntry<float> TowerSpotMultiplier;
    public static ConfigEntry<float> EnemyCountMultiplier;
    public static ConfigEntry<float> EnemyTimelineSpeed;
    public static ConfigEntry<float> StaffCooldownMultiplier;
    public static ConfigEntry<float> SteedCooldownMultiplier;

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

        // 箭塔基底（可购买塔位）密度倍数：1=原生密度（不补点），2=目标间距减半
        // （约两倍点位），上限 4。对原生参考集幂等补放（间距估计/铺点范围只取
        // 原生基底，反复读档密度不爬升），现有存档读档即生效。
        TowerSpotMultiplier = config.Bind("World", "TowerSpotMultiplier", 2f,
            new ConfigDescription("箭塔基底密度倍数（1=原生密度，最大4）",
                new AcceptableValueRange<float>(1f, 4f)));

        EnemyCountMultiplier = config.Bind("Enemy", "EnemyCountMultiplier", 1f,
            "每波怪物数量倍率（1-5x）");

        EnemyTimelineSpeed = config.Bind("Enemy", "EnemyTimelineSpeed", 1f,
            "怪物时间线推进速度倍率（1-5x）");

        // ---- Cooldown（2026-08-24 需求：神器/坐骑 CD 各一个面板滑块，最多缩到原版 1/5）----
        // 神器默认 0.375 = 现行强化值 11.25/30：装上即维持既有手感；1.0 = 原版 30 秒。
        StaffCooldownMultiplier = config.Bind("Cooldown", "StaffCooldownMultiplier", 0.375f,
            "神器权杖CD倍率（0.2=最短，为原版1/5；1.0=原版30秒）");

        // 坐骑默认 1.0（原版）：各坐骑原生 CD 不同（prefab 序列化值），倍率统一乘在原生值上。
        SteedCooldownMultiplier = config.Bind("Cooldown", "SteedCooldownMultiplier", 1.0f,
            "坐骑技能CD倍率（0.2=最短，为原生1/5；1.0=原生）");

        // 接线无限金币（2.4.0 Wallet.InfiniteMoney 为 public static 属性）：
        // 配置改动即时生效 + 启动应用初值。Mono 版由 OnGUI toggle 驱动，此处等价迁移。
        InfiniteMoney.SettingChanged += OnInfiniteMoneyChanged;
        Wallet.InfiniteMoney = InfiniteMoney.Value;

        // CD 倍率改动 → 对在场实例重跑 profile（InfiniteMoney 同款接线模式）。
        // 面板滑块在主线程（OnGUI）触发；事件低频，patch 侧 FindObjectsOfType 全量扫可接受。
        // 两个 patch 本身都有读取点前缀（每次使用时重算），此处仅让字段即时一致并留日志，
        // 正确性不依赖本回调。
        StaffCooldownMultiplier.SettingChanged += PatchDivine_HermesStaff.OnStaffCooldownMultiplierChanged;
        SteedCooldownMultiplier.SettingChanged += PatchRide_SteedCooldown.OnMultiplierChanged;
    }

    private static void OnInfiniteMoneyChanged(object sender, System.EventArgs e)
    {
        Wallet.InfiniteMoney = InfiniteMoney.Value;
    }
}
