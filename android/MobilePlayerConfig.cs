using System;
using MelonLoader;

namespace KingdomEnhancedMod;

internal static class ModConfig
{
    private const string CategoryIdentifier = "OhMyMods.Android";

    // 会话总开关：无 UI、不持久化（不建 MelonPreferences entry），保留给现有消费者
    // （OptionalQoLScope.IsActive / PatchWorld_Mover 闸门）读取 .Value。
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

    internal static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        category = MelonPreferences.CreateCategory(CategoryIdentifier);
        // CreateEntry 内部经 File.SetupEntryFromRawValue 把 cfg 已存值吸入 entry（实际 IL
        // 已核），键缺失时保留默认值；不需要额外的 Load/每帧读回。
        SpeedMultiplier = category.CreateEntry<int>("SpeedMultiplier", 1);
        InfiniteSteedStamina = category.CreateEntry<bool>("InfiniteSteedStamina", false);
        HoldPurchaseEnabled = category.CreateEntry<bool>("HoldPurchaseEnabled", false);
        CalendarEnabled = category.CreateEntry<bool>("CalendarEnabled", false);
        // 手工编辑 cfg 可能写入越界值，在加载边界做唯一一次 clamp（不使用 validator：未证实
        // 可构造 ValueValidator 子类）；运行期不读回、不重试、不静默替换默认值。
        SpeedMultiplier.Value = Math.Clamp(SpeedMultiplier.Value, 1, 5);
        MelonLogger.Msg("ANDROID_SETTINGS_READY category=" + CategoryIdentifier
            + " speed=" + SpeedMultiplier.Value
            + " stamina=" + InfiniteSteedStamina.Value
            + " hold=" + HoldPurchaseEnabled.Value
            + " calendar=" + CalendarEnabled.Value);
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
