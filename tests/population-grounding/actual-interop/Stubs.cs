namespace KingdomEnhancedMod;
internal static class ModConfig { internal sealed class Flag { public bool Value => false; } internal static Flag Enabled => null; }
internal sealed class KingdomEnhancedPlugin { internal static KingdomEnhancedPlugin Instance => null; internal Logger LogSource => null; }
internal sealed class Logger { internal void LogInfo(string value) { } internal void LogWarning(string value) { } }
