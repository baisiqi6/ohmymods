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

    private static void OnHermesHeadwearSettingsChanged(object sender, System.EventArgs args)
        => PatchDivine_HermesHeadwear.OnSettingsChanged();

    public static ConfigEntry<bool> Enabled;
    public static ConfigEntry<bool> InfiniteMoney;
    public static ConfigEntry<bool> InfiniteSteedStamina;
    public static ConfigEntry<int> SpeedMultiplier;
    public static ConfigEntry<bool> FastBuild;
    public static ConfigEntry<bool> ShowCalendarHud;
    public static ConfigEntry<bool> ShowPopulationHud;
    public static ConfigEntry<bool> HoldPurchaseEnabled, DenseThicketsEnabled, FastForestRecedeEnabled;
    public static ConfigEntry<bool> ArcherScatterEnabled, ArcherRateEnabled, ArcherImpactEnabled;
    public static ConfigEntry<bool> HeroArcherEnabled;
    public static ConfigEntry<bool> MusketeerEnabled;
    public static ConfigEntry<int> ArcherVolleyCount;
    public static ConfigEntry<float> ArcherRateMultiplier;
    public static ConfigEntry<bool> HermesHeadwearEnabled;
    public static ConfigEntry<int> HermesHeadwearChancePercent;
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
    public static ConfigEntry<bool> AutoRestockFarmersEnabled, AutoRestockCatapultBarrelsEnabled, AutoRestockFireTowerAmmoEnabled;
    public static ConfigEntry<bool> AutoRestockMusketeersEnabled;
    public static ConfigEntry<int> AutoRestockWorkersTarget, AutoRestockArchersTarget,
        AutoRestockNinjasTarget, AutoRestockBerserkersTarget, AutoRestockPeasantsTarget;
    public static ConfigEntry<int> AutoRestockFarmersTarget, AutoRestockCatapultBarrelsTarget, AutoRestockFireTowerAmmoTarget;
    public static ConfigEntry<int> AutoRestockMusketeersTarget;

    public static void Init(ConfigFile config)
    {
        Enabled = config.Bind("General", "Enabled", true,
            "总开关：关闭后所有 patch 走原版逻辑");

        InfiniteMoney = config.Bind("Economy", "InfiniteMoney", false,
            "无限金币：开启后玩家金币用不完");

        HoldPurchaseEnabled = config.Bind("Convenience", "HoldPurchaseEnabled", false,
            "所有世界：开始按原版投币，持续按住后加快投槽并连续购买同店商品；关闭保留原版操作");
        DenseThicketsEnabled = config.Bind("Convenience", "DenseThicketsEnabled", false,
            "所有世界：灌木生长间距减半；关闭后额外灌木快速枯萎，清理完成前不能重新开启");
        FastForestRecedeEnabled = config.Bind("Convenience", "FastForestRecedeEnabled", false,
            "所有世界：砍树后的原生森林消退等待缩至三分之一；关闭后的新消退按原版等待");
        ArcherScatterEnabled = config.Bind("Archer", "ScatterEnabled", false, "所有世界：仅中世纪骑士的弓箭手随从攻击敌人时散射；打猎单发，额外箭呈淡金色，密集射击时自动限流");
        MusketeerEnabled = config.Bind("Musketeer", "Enabled", false,
            "火铳铺：所有世界可选。4金币购买火枪，居民拾取成为地面火铳手；基础伤害2、射程为原生普通弓手1.5倍、较慢装填，直线命中前排，不上箭塔。第一版仅单机；关闭恢复原生外观与行为，职业记录保留。");
        HeroArcherEnabled = config.Bind("Archer", "HeroArcherEnabled", false,
            "英雄驿站：领地中段花8金币升级现有弓箭手，每侧最多1名，购买占位直到英雄死亡。关闭暂停商店与英雄效果，已购名额保留。英雄移速1.5倍、射速1.5倍、射程2倍、对敌3箭，火焰半径0.25/额外1点；仅单机。");
        ArcherVolleyCount = config.Bind("Archer", "VolleyCount", 3,
            new ConfigDescription("中世纪随从每发箭的总数量（含原生主箭），上限3支；高负载时额外箭受全场限额约束", new AcceptableValueRange<int>(1, 3)));
        ArcherRateEnabled = config.Bind("Archer", "RateEnabled", false, "所有世界：加快弓箭手准备、连射和冷却节奏，关闭恢复原版节奏");
        ArcherRateMultiplier = config.Bind("Archer", "RateMultiplier", 1.5f,
            new ConfigDescription("弓箭手射速倍率，上限2倍；不改变移动和全局时间", new AcceptableValueRange<float>(1f, 2f)));
        ArcherImpactEnabled = config.Bind("Archer", "ImpactEnabled", false, "希腊骑士火焰状态下的随从火矢：以半径0.25、一次1点范围伤害替代持续灼烧；直接命中者不重复受伤，同轮散射去重。单机/主机显示作者像素火焰；关闭恢复原版");
        HermesHeadwearEnabled = config.Bind("HermesHeadwear", "Enabled", true,
            "法杖新转化小怪按累计配额获得44款轮换头饰；佩戴面具或派对帽时不被主动选敌，范围伤害保留；关闭隐藏新增头饰，重开保持选择");
        HermesHeadwearChancePercent = config.Bind("HermesHeadwear", "ChancePercent", 30,
            new ConfigDescription("累计头饰配额百分比，30表示每新转化10只发放3顶，跨技能与重启接续；已有小怪和读档不重新分配", new AcceptableValueRange<int>(0, 100)));
        HermesHeadwearEnabled.SettingChanged += OnHermesHeadwearSettingsChanged;
        Enabled.SettingChanged += OnHermesHeadwearSettingsChanged;

        AutoRestockWorkersEnabled = config.Bind("AutoRestock", "WorkersEnabled", false, "税收官从金库自动购买工匠锤子");
        AutoRestockArchersEnabled = config.Bind("AutoRestock", "ArchersEnabled", false, "税收官从金库自动购买弓箭手道具");
        AutoRestockNinjasEnabled = config.Bind("AutoRestock", "NinjasEnabled", false, "税收官从金库自动购买忍者道具");
        AutoRestockBerserkersEnabled = config.Bind("AutoRestock", "BerserkersEnabled", false, "税收官从金库自动购买狂战士药水");
        AutoRestockPeasantsEnabled = config.Bind("AutoRestock", "PeasantsEnabled", false,
            "无业村民不足时，税收官全天从金库到面包房购买面包，按原生流程吸引流浪者吃面包入籍");
        AutoRestockFarmersEnabled = config.Bind("AutoRestock", "FarmersEnabled", false, "希腊世界：农民不足时，税收官从金库购买镰刀");
        AutoRestockCatapultBarrelsEnabled = config.Bind("AutoRestock", "CatapultBarrelsEnabled", false, "希腊世界：税收官从金库为已有投石车采购火药桶");
        AutoRestockFireTowerAmmoEnabled = config.Bind("AutoRestock", "FireTowerAmmoEnabled", false, "希腊世界：税收官从金库为已有希腊火焰塔补充弹药");
        AutoRestockMusketeersEnabled = config.Bind("AutoRestock", "MusketeersEnabled", false,
            "希腊单机：启用火铳铺后，税收官按目标人数从金库自动采购火枪；每把8金币，手动价仍为4金币");
        var targetDescription = new ConfigDescription("职业目标：现有人数、店内待领道具与在途采购合计，店满等待；全天从金库按店价的2倍采购",
            new AcceptableValueRange<int>(1, 200));
        AutoRestockWorkersTarget = config.Bind("AutoRestock", "WorkersTarget", 15, targetDescription);
        AutoRestockArchersTarget = config.Bind("AutoRestock", "ArchersTarget", 15, targetDescription);
        AutoRestockNinjasTarget = config.Bind("AutoRestock", "NinjasTarget", 15, targetDescription);
        AutoRestockBerserkersTarget = config.Bind("AutoRestock", "BerserkersTarget", 15, targetDescription);
        AutoRestockFarmersTarget = config.Bind("AutoRestock", "FarmersTarget", 15, targetDescription);
        AutoRestockMusketeersTarget = config.Bind("AutoRestock", "MusketeersTarget", 15,
            new ConfigDescription("火枪手目标：存活火枪手、已购可用待拾火枪及在途订单合计；满架等待，不计入普通弓箭手目标",
                new AcceptableValueRange<int>(1, 200)));
        var ammoDescription = new ConfigDescription("全岛弹药目标：现有未消耗弹药（含运输和已装填）与在途采购合计；原生容量不足时等待。自动采购按原价2倍扣款",
            new AcceptableValueRange<int>(1, 200));
        AutoRestockCatapultBarrelsTarget = config.Bind("AutoRestock", "CatapultBarrelsTarget", 15, ammoDescription);
        AutoRestockFireTowerAmmoTarget = config.Bind("AutoRestock", "FireTowerAmmoTarget", 15, ammoDescription);
        AutoRestockPeasantsTarget = config.Bind("AutoRestock", "PeasantsTarget", 15,
            new ConfigDescription("无业村民目标：现有Peasant、面包库存、吃面包后招募中的村民及在途采购合计；满架等待，不包含工匠或其他职业",
                new AcceptableValueRange<int>(1, 200)));

        InfiniteSteedStamina = config.Bind("Player", "InfiniteSteedStamina", false,
            "所有世界：本机控制的坐骑奔跑与滑翔不消耗体力；关闭恢复自然消耗，不改变坐骑技能冷却");

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
