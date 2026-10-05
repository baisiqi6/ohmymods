using System;
using MelonLoader;

namespace KingdomEnhancedMod;

internal static class ModConfig
{
    private const string CategoryIdentifier = "OhMyMods.Android";

    // 会话总开关：无 UI、不持久化（不建 MelonPreferences entry），保留给现有消费者
    // （OptionalQoLScope.IsActive / PatchWorld_Mover / PatchWorld_EnemyManager 闸门）读取 .Value。
    internal sealed class Setting<T> { internal T Value; internal Setting(T value) { Value = value; } }
    internal static readonly Setting<bool> Enabled = new(true);

    // 设置唯一来源：loader 标准 MelonPreferences（UserData/MelonPreferences.cfg 的
    // [OhMyMods.Android] 分节，不 SetFilePath 自写路径）。Initialize() 由 Operator 在
    // OnInitializeMelon 的所有 patch 注册与任何 UI 读取之前调用一次。
    private static bool initialized;
    private static MelonPreferences_Category category;
    internal static MelonPreferences_Entry<int> SpeedMultiplier;
    internal static MelonPreferences_Entry<bool> InfiniteSteedStamina;
    internal static MelonPreferences_Entry<bool> HoldPurchaseEnabled;
    internal static MelonPreferences_Entry<bool> CalendarEnabled;
    internal static MelonPreferences_Entry<float> EnemyCountMultiplier;
    internal static MelonPreferences_Entry<float> EnemyTimelineSpeed;
    internal static MelonPreferences_Entry<float> SteedCooldownMultiplier;
    internal static MelonPreferences_Entry<float> StaffCooldownMultiplier;
    internal static MelonPreferences_Entry<bool> InfiniteMoney;
    internal static MelonPreferences_Entry<bool> FarmCatsEnabled;
    internal static MelonPreferences_Entry<bool> FastBuild;
    internal static MelonPreferences_Entry<bool> BoatCapacityEnabled;
    internal static MelonPreferences_Entry<float> MapSizeMultiplier;
    internal static MelonPreferences_Entry<bool> FastForestRecedeEnabled;
    internal static MelonPreferences_Entry<bool> DeerPopulationEnabled;
    internal static MelonPreferences_Entry<bool> DenseThicketsEnabled;

    // InfiniteMoney 原生写（Il2Cpp.Wallet.InfiniteMoney）的唯一注入点：本文件保持零
    // Il2Cpp.* 引用（hosttests 直接编译同一份），由平台侧 Probe.OnInitializeMelon 传入
    // lambda；条目加载完成与 UI 切换时各调用一次，不订阅事件、不每帧写、不做状态镜像。
    private static Action<bool> applyInfiniteMoney;

    internal static void Initialize(Action<bool> moneyApplier)
    {
        // null Applier 是接线错误：先于 initialized 置位抛错，一次坏调用不会吞掉后续正式
        // 初始化；不提供默认 fake callback，也不静默跳过原生写。
        ArgumentNullException.ThrowIfNull(moneyApplier);
        if (initialized) return;
        initialized = true;
        category = MelonPreferences.CreateCategory(CategoryIdentifier);
        // CreateEntry 内部经 File.SetupEntryFromRawValue 把 cfg 已存值吸入 entry（实际 IL
        // 已核），键缺失时保留默认值；不需要额外的 Load/每帧读回。
        SpeedMultiplier = category.CreateEntry<int>("SpeedMultiplier", 1);
        InfiniteSteedStamina = category.CreateEntry<bool>("InfiniteSteedStamina", false);
        HoldPurchaseEnabled = category.CreateEntry<bool>("HoldPurchaseEnabled", false);
        CalendarEnabled = category.CreateEntry<bool>("CalendarEnabled", false);
        EnemyCountMultiplier = category.CreateEntry<float>("EnemyCountMultiplier", 1f);
        EnemyTimelineSpeed = category.CreateEntry<float>("EnemyTimelineSpeed", 1f);
        SteedCooldownMultiplier = category.CreateEntry<float>("SteedCooldownMultiplier", 1f);
        StaffCooldownMultiplier = category.CreateEntry<float>("StaffCooldownMultiplier", 1f);
        InfiniteMoney = category.CreateEntry<bool>("InfiniteMoney", false);
        FarmCatsEnabled = category.CreateEntry<bool>("FarmCatsEnabled", false);
        FastBuild = category.CreateEntry<bool>("FastBuild", false);
        BoatCapacityEnabled = category.CreateEntry<bool>("BoatCapacityEnabled", false);
        MapSizeMultiplier = category.CreateEntry<float>("MapSizeMultiplier", 1f);
        FastForestRecedeEnabled = category.CreateEntry<bool>("FastForestRecedeEnabled", false);
        DeerPopulationEnabled = category.CreateEntry<bool>("DeerPopulationEnabled", false);
        DenseThicketsEnabled = category.CreateEntry<bool>("DenseThicketsEnabled", false);
        // 手工编辑 cfg 可能写入越界值，在加载边界做唯一一次 clamp（不使用 validator：未证实
        // 可构造 ValueValidator 子类）；运行期不读回、不重试、不静默替换默认值。
        SpeedMultiplier.Value = Math.Clamp(SpeedMultiplier.Value, 1, 5);
        EnemyCountMultiplier.Value = SanitizeMultiplier("EnemyCountMultiplier", EnemyCountMultiplier.Value, 1f, 5f);
        EnemyTimelineSpeed.Value = SanitizeMultiplier("EnemyTimelineSpeed", EnemyTimelineSpeed.Value, 1f, 5f);
        SteedCooldownMultiplier.Value = SanitizeMultiplier("SteedCooldownMultiplier", SteedCooldownMultiplier.Value, 0.2f, 1f);
        StaffCooldownMultiplier.Value = SanitizeMultiplier("StaffCooldownMultiplier", StaffCooldownMultiplier.Value, 0.2f, 1f);
        MapSizeMultiplier.Value = SanitizeMultiplier("MapSizeMultiplier", MapSizeMultiplier.Value, 1f, 5f);
        applyInfiniteMoney = moneyApplier;
        applyInfiniteMoney(InfiniteMoney.Value);
        MelonLogger.Msg("ANDROID_SETTINGS_READY category=" + CategoryIdentifier
            + " speed=" + SpeedMultiplier.Value
            + " stamina=" + InfiniteSteedStamina.Value
            + " hold=" + HoldPurchaseEnabled.Value
            + " calendar=" + CalendarEnabled.Value
            + " enemyCount=" + EnemyCountMultiplier.Value
            + " growth=" + EnemyTimelineSpeed.Value
            + " money=" + InfiniteMoney.Value
            + " cats=" + FarmCatsEnabled.Value
            + " fastBuild=" + FastBuild.Value
            + " cooldown=" + SteedCooldownMultiplier.Value
            + " boat=" + BoatCapacityEnabled.Value
            + " map=" + MapSizeMultiplier.Value
            + " staff=" + StaffCooldownMultiplier.Value
            + " forestRecede=" + FastForestRecedeEnabled.Value
            + " deerPopulation=" + DeerPopulationEnabled.Value
            + " denseThickets=" + DenseThicketsEnabled.Value);
    }

    // 三个倍率的加载边界：有限值 clamp 到 [min,max]；NaN/Infinity 是非法外部输入，回 1 并
    // Warning 一次（Math.Clamp(NaN,…) 返回 NaN，不能直接用）。只改内存，不回写 cfg。
    private static float SanitizeMultiplier(string entryName, float value, float min, float max)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            MelonLogger.Warning("ANDROID_SETTINGS_WARNING entry=" + entryName + " non-finite value fallback=1");
            return 1f;
        }
        return Math.Clamp(value, min, max);
    }

    internal static void CycleSpeed()
    {
        SpeedMultiplier.Value = SpeedMultiplier.Value % 5 + 1;
        MelonLogger.Msg("ANDROID_PLAYER_SPEED multiplier=" + SpeedMultiplier.Value);
        Save();
    }

    internal static void ToggleStamina()
    {
        InfiniteSteedStamina.Value = !InfiniteSteedStamina.Value;
        MelonLogger.Msg("ANDROID_PLAYER_STAMINA enabled=" + InfiniteSteedStamina.Value);
        Save();
    }

    internal static void ToggleHold()
    {
        HoldPurchaseEnabled.Value = !HoldPurchaseEnabled.Value;
        MelonLogger.Msg("ANDROID_PLAYER_HOLD_PURCHASE enabled=" + HoldPurchaseEnabled.Value);
        Save();
    }

    // 冷却档位步进（坐骑与法杖共用同一份数字逻辑）：1→0.8→0.6→0.4→0.2→1；载入的中间值
    // 转到下一较低 20% 档（0.5→0.4）。只用 float32 Ceiling/除法（index / 5f），无 clamp、
    // 无原生重写、无事件扫描；helper 不落盘，切换方各自一次落盘。
    private static float NextCooldownStep(float current)
    {
        int index = (int)MathF.Ceiling(current * 5f) - 1;
        return index <= 0 ? 1f : index / 5f;
    }

    internal static void CycleSteedCooldown()
    {
        SteedCooldownMultiplier.Value = NextCooldownStep(SteedCooldownMultiplier.Value);
        MelonLogger.Msg("ANDROID_PLAYER_STEED_COOLDOWN multiplier=" + SteedCooldownMultiplier.Value);
        Save();
    }

    internal static void CycleStaffCooldown()
    {
        StaffCooldownMultiplier.Value = NextCooldownStep(StaffCooldownMultiplier.Value);
        MelonLogger.Msg("ANDROID_PLAYER_STAFF_COOLDOWN multiplier=" + StaffCooldownMultiplier.Value);
        Save();
    }

    // UI 档位步进：先落到下一整数档再进一（1.5→2、4.5→5、5→1），避免把合法载入的
    // 小数（如 4.5）推成 5.5 越出 1..5 契约；只用 MathF.Floor，不加逐帧 clamp/守卫。
    internal static void CycleEnemyCount()
    {
        EnemyCountMultiplier.Value = MathF.Floor(EnemyCountMultiplier.Value) % 5f + 1f;
        MelonLogger.Msg("ANDROID_WORLD_ENEMY_COUNT multiplier=" + EnemyCountMultiplier.Value);
        Save();
    }

    internal static void CycleEnemyTimeline()
    {
        EnemyTimelineSpeed.Value = MathF.Floor(EnemyTimelineSpeed.Value) % 5f + 1f;
        MelonLogger.Msg("ANDROID_WORLD_THREAT_GROWTH multiplier=" + EnemyTimelineSpeed.Value);
        Save();
    }

    // entry 改 → 原生写 apply 一次 → 落盘一次；顺序固定，失败路径不重试、不补偿。
    internal static void ToggleMoney()
    {
        InfiniteMoney.Value = !InfiniteMoney.Value;
        applyInfiniteMoney(InfiniteMoney.Value);
        MelonLogger.Msg("ANDROID_WORLD_MONEY enabled=" + InfiniteMoney.Value);
        Save();
    }

    // 猫只补未来：开关只决定后续 OnLevelLoaded 是否补齐，不删除/不回收已有猫；切换一次落盘一次。
    internal static void ToggleFarmCats()
    {
        FarmCatsEnabled.Value = !FarmCatsEnabled.Value;
        MelonLogger.Msg("ANDROID_FARM_CATS enabled=" + FarmCatsEnabled.Value);
        Save();
    }

    // 快速森林退缩只缩放之后进入原生 FadeAndRemove 的等待参数（共享 prefix；关闭不回收进行中的淡出、
    // 不扫描世界、不改已写数值）；切换一次落盘一次。
    internal static void ToggleFastForestRecede()
    {
        FastForestRecedeEnabled.Value = !FastForestRecedeEnabled.Value;
        MelonLogger.Msg("ANDROID_WORLD_FAST_FOREST_RECEDE enabled=" + FastForestRecedeEnabled.Value);
        Save();
    }

    // 普通鹿数量与补充频率只作用于之后自然进入原生 PopulationController.Update 的调用（共享 prefix；
    // 关闭不删除/不回收已有动物、不改原生存档、不热还原本次已归还之外的实例）；切换一次落盘一次。
    internal static void ToggleDeerPopulation()
    {
        DeerPopulationEnabled.Value = !DeerPopulationEnabled.Value;
        MelonLogger.Msg("ANDROID_WORLD_DEER_POPULATION enabled=" + DeerPopulationEnabled.Value);
        Save();
    }

    // 快速建造只作用于后续 InitializeBuild 调用（共享前缀在原生 _hasStarted 早退前写 rate）；
    // 关闭不热还原已写入实例的 rate，不建镜像、不重试；切换一次落盘一次。
    internal static void ToggleFastBuild()
    {
        FastBuild.Value = !FastBuild.Value;
        MelonLogger.Msg("ANDROID_WORLD_FAST_BUILD enabled=" + FastBuild.Value);
        Save();
    }

    // 额外船容量只作用于以后新初始化的船（Boat.OnEnable 借用窗口，原生 RegisterUnitSlots 前）；
    // 关闭不删除/不热改已登记的 slots，不做全船扫描、不重试；切换一次落盘一次。
    internal static void ToggleBoatCapacity()
    {
        BoatCapacityEnabled.Value = !BoatCapacityEnabled.Value;
        MelonLogger.Msg("ANDROID_WORLD_BOAT_CAPACITY enabled=" + BoatCapacityEnabled.Value);
        Save();
    }

    // 地图长度档位 1→2→3→4→5→1；与敌人倍率同一 MathF.Floor 步进：先落到下一整数档再进一
    // （4.5→5、5→1），合法载入的小数（如 4.5）不会被推成 5.5 越出 1..5 契约。倍率只被
    // 新岛的原生生成 scope 读取；不改变已生成岛、不追溯/不重生成、不做运行期 clamp；切换一次落盘一次。
    internal static void CycleMapSize()
    {
        MapSizeMultiplier.Value = MathF.Floor(MapSizeMultiplier.Value) % 5f + 1f;
        MelonLogger.Msg("ANDROID_ISLAND_MAP_SIZE multiplier=" + MapSizeMultiplier.Value);
        Save();
    }

    // 切换类写入的唯一落盘点（每次切换恰好一次）。loader 0.7.3 的
    // MelonPreferences_Category.SaveToFile(bool) 在内部 try/catch 全部异常，失败时自行
    // MelonLogger.Error("Error while Saving Preferences to <path>: <ex>") 并置 File.WasError
    // （实际 IL 见本段回执 evidence/melonprefs-category-il.json），失败对设备日志可见；
    // 这里不捕获、不重试、不轮询、不镜像状态。printmsg:false 避免输出“已保存”成功文案。
    internal static void Save()
    {
        category.SaveToFile(false);
    }
}
