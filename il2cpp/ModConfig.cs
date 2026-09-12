using BepInEx.Configuration;

namespace KingdomEnhancedMod;

/// <summary>
/// 全局设置（BepInConfig 承载，替代 UMM Settings）。
/// 对应 Mono 版 Main.cs 的设置项。开箱即用：默认值即目标值，用户改
/// BepInEx/config/KingdomEnhancedMod.cfg 即可，无需游戏内 UI。
/// </summary>
public static class ModConfig
{
    internal const int DefaultBeggarSpawnIntervalSeconds = 120;
    internal const int DefaultBeggarCampCapacity = 4;

    public static ConfigEntry<bool> Enabled;
    public static ConfigEntry<bool> InfiniteMoney;
    public static ConfigEntry<int> SpeedMultiplier;
    public static ConfigEntry<bool> FastBuild;
    public static ConfigEntry<bool> ShowCalendarHud;
    public static ConfigEntry<bool> ShowPopulationHud;
    public static ConfigEntry<int> BeggarSpawnIntervalSeconds;
    public static ConfigEntry<int> BeggarCampCapacity;
    public static ConfigEntry<float> MapSizeMultiplier;
    public static ConfigEntry<float> TowerSpotMultiplier;
    public static ConfigEntry<float> EnemyCountMultiplier;
    public static ConfigEntry<float> EnemyTimelineSpeed;
    public static ConfigEntry<float> StaffCooldownMultiplier;
    public static ConfigEntry<float> SteedCooldownMultiplier;
    public static ConfigEntry<bool> AutoRestockWorkersEnabled, AutoRestockArchersEnabled,
        AutoRestockNinjasEnabled, AutoRestockBerserkersEnabled, AutoRestockPeasantsEnabled;
    public static ConfigEntry<int> AutoRestockWorkersTarget, AutoRestockArchersTarget,
        AutoRestockNinjasTarget, AutoRestockBerserkersTarget, AutoRestockPeasantsTarget;

    public static void Init(ConfigFile config)
    {
        Enabled = config.Bind("General", "Enabled", true,
            "总开关：关闭后所有 patch 走原版逻辑");

        InfiniteMoney = config.Bind("Economy", "InfiniteMoney", false,
            "无限金币：开启后玩家金币用不完");

        AutoRestockWorkersEnabled = config.Bind("AutoRestock", "WorkersEnabled", false, "税收官从金库自动购买工匠锤子");
        AutoRestockArchersEnabled = config.Bind("AutoRestock", "ArchersEnabled", false, "税收官从金库自动购买弓箭手道具");
        AutoRestockNinjasEnabled = config.Bind("AutoRestock", "NinjasEnabled", false, "税收官从金库自动购买忍者道具");
        AutoRestockBerserkersEnabled = config.Bind("AutoRestock", "BerserkersEnabled", false, "税收官从金库自动购买狂战士药水");
        AutoRestockPeasantsEnabled = config.Bind("AutoRestock", "PeasantsEnabled", false,
            "无业村民不足时，税收官全天从金库到面包房购买面包，按原生流程吸引流浪者吃面包入籍");
        var targetDescription = new ConfigDescription("职业目标：现有人数、店内待领道具与在途采购合计，店满等待；全天从金库按店价的2倍采购",
            new AcceptableValueRange<int>(1, 200));
        AutoRestockWorkersTarget = config.Bind("AutoRestock", "WorkersTarget", 15, targetDescription);
        AutoRestockArchersTarget = config.Bind("AutoRestock", "ArchersTarget", 15, targetDescription);
        AutoRestockNinjasTarget = config.Bind("AutoRestock", "NinjasTarget", 15, targetDescription);
        AutoRestockBerserkersTarget = config.Bind("AutoRestock", "BerserkersTarget", 15, targetDescription);
        AutoRestockPeasantsTarget = config.Bind("AutoRestock", "PeasantsTarget", 15,
            new ConfigDescription("无业村民目标：现有Peasant、面包库存、吃面包后招募中的村民及在途采购合计；满架等待，不包含工匠或其他职业",
                new AcceptableValueRange<int>(1, 200)));

        SpeedMultiplier = config.Bind("Player", "SpeedMultiplier", 2,
            "君主移动速度倍率（1-5x）");

        ShowCalendarHud = config.Bind("Display", "ShowCalendarHud", false,
            "常驻时间与银行：总天数、整点、季节图标、季内天数、下一季开始日和银行存款；只读显示");

        ShowPopulationHud = config.Bind("Display", "ShowPopulationHud", true,
            "常驻人数：单机或联机主机显示本岛存活职业和五世界骑士人数，不含待领装备；客机名册不可用时显示提示");

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

        BeggarSpawnIntervalSeconds = config.Bind("Population", "BeggarSpawnIntervalSeconds", DefaultBeggarSpawnIntervalSeconds,
            new ConfigDescription("乞丐刷新间隔（游戏秒，1-120）；正常协调器按此间隔补员，原生回退最短约6秒",
                new AcceptableValueRange<int>(1, 120)));
        BeggarCampCapacity = config.Bind("Population", "BeggarCampCapacity", DefaultBeggarCampCapacity,
            new ConfigDescription("每个帐篷的补员上限（1-20）；仅影响后续刷新，降低上限或读档不删除已有乞丐",
                new AcceptableValueRange<int>(1, 20)));

        // 人口设置由中央协调器在主线程的现有0.5秒核对周期读取，不订阅Unity事件。
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
