using BepInEx.Logging;

namespace KingdomEnhancedMod;

/// <summary>
/// Compile-gate shell only. The shipped plugin class lives in il2cpp/KingdomEnhancedPlugin.cs and is
/// outside this gate, but the guard logs through KingdomEnhancedPlugin.Instance.LogSource, so mirror
/// exactly that surface - the logger type is the real BepInEx ManualLogSource from the shipped core.
/// </summary>
public class KingdomEnhancedPlugin
{
    public static KingdomEnhancedPlugin Instance;
    public ManualLogSource LogSource => null;
}
