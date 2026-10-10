namespace KingdomEnhancedMod;

// Host double for the pure helpers: MusketeerArchive's bounded one-time diagnostics route
// through the plugin log source and nothing else in the linked sources touches host types.
internal sealed class KingdomEnhancedPlugin
{
    internal static KingdomEnhancedPlugin Instance = new();
    internal Log LogSource = new();

    internal sealed class Log
    {
        internal void LogInfo(string text) { }
        internal void LogWarning(string text) { }
    }
}
