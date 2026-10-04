// Thin Android bridges for the shared desktop source il2cpp/PatchPlayer_HoldPurchase.cs.
// Only the surfaces that source actually touches are implemented (ModPanel.IsShown,
// KingdomEnhancedPlugin.Instance.LogSource); ModConfig and OptionalQoLScope live in their
// existing Android files (MobilePlayerConfig.cs / OptionalQoLScope.cs). Nothing here reads or
// writes game state, and no game value is faked.
using MelonLoader;

namespace KingdomEnhancedMod;

/// <summary>桌面 ModPanel.IsShown -> 浮球展开态：面板打开时会话按契约立即结束。</summary>
internal static class ModPanel
{
    internal static bool IsShown => OhMyMods.AndroidProbe.ProbeTicker.Layout.Expanded;
}

/// <summary>
/// 桌面 KingdomEnhancedPlugin.Instance.LogSource -> MelonLoader 日志。
/// Instance 只读单例：Operator 必须在 OnInitializeMelon 最早处调用 Initialize()
/// （设备冷启动日志 OHMYMODS_ANDROID_LOG_SOURCE_READY 可核验）；未接线时 Instance 为 null，
/// 共享 Hold 源按 `Instance?.LogSource` 静默丢弃诊断——不用自愈式补初始化掩盖漏接。
/// </summary>
internal sealed class KingdomEnhancedPlugin
{
    private static KingdomEnhancedPlugin instance;
    private readonly Logger logSource = new Logger();

    internal static KingdomEnhancedPlugin Instance => instance;

    internal Logger LogSource => logSource;

    internal static void Initialize()
    {
        if (instance != null) return;
        instance = new KingdomEnhancedPlugin();
        MelonLogger.Msg("OHMYMODS_ANDROID_LOG_SOURCE_READY");
    }
}

/// <summary>
/// 桌面 Logger 桥：直接写 MelonLoader 日志（Info -> Msg）。共享源以 Exception 调用
/// LogError，签名收 object；message 非空由调用点保证（catch 块的异常对象）。
/// </summary>
internal sealed class Logger
{
    internal void LogWarning(string message) => MelonLogger.Warning(message);
    internal void LogInfo(string message) => MelonLogger.Msg(message);
    internal void LogError(object message) => MelonLogger.Error(message.ToString());
}
