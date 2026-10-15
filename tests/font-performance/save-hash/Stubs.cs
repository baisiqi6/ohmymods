namespace KingdomEnhancedMod;

// 纯测试替身：真实 Plugin/日志类型依赖 BepInEx/Unity，仅补编译所需的最小壳。
// 供 MusketeerArchive.cs 的日志入口使用，不参与任何断言。
internal sealed class PerfTestLogSource
{
    internal void LogInfo(string message) { }
    internal void LogWarning(string message) { }
}

internal sealed class KingdomEnhancedPlugin
{
    internal static KingdomEnhancedPlugin Instance;
    internal PerfTestLogSource LogSource = new PerfTestLogSource();
}
