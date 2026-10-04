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
    internal static MelonPreferences_Entry<bool> InfiniteMoney;
    internal static MelonPreferences_Entry<bool> FarmCatsEnabled;

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
        InfiniteMoney = category.CreateEntry<bool>("InfiniteMoney", false);
        FarmCatsEnabled = category.CreateEntry<bool>("FarmCatsEnabled", false);
        // 手工编辑 cfg 可能写入越界值，在加载边界做唯一一次 clamp（不使用 validator：未证实
        // 可构造 ValueValidator 子类）；运行期不读回、不重试、不静默替换默认值。
        SpeedMultiplier.Value = Math.Clamp(SpeedMultiplier.Value, 1, 5);
        EnemyCountMultiplier.Value = SanitizeMultiplier("EnemyCountMultiplier", EnemyCountMultiplier.Value);
        EnemyTimelineSpeed.Value = SanitizeMultiplier("EnemyTimelineSpeed", EnemyTimelineSpeed.Value);
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
            + " cats=" + FarmCatsEnabled.Value);
    }

    // 两个倍率的加载边界：有限值 clamp 到 1..5；NaN/Infinity 是非法外部输入，回 1 并
    // Warning 一次（Math.Clamp(NaN,1,5) 返回 NaN，不能直接用）。只改内存，不回写 cfg。
    private static float SanitizeMultiplier(string entryName, float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            MelonLogger.Warning("ANDROID_SETTINGS_WARNING entry=" + entryName + " non-finite value fallback=1");
            return 1f;
        }
        return Math.Clamp(value, 1f, 5f);
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
