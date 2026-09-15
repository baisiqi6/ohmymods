using BepInEx.Logging;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 编译校验替身：只为让生产分支（无 HERMES_CYCLE_TEST 宏）在本工程内可编译。
    /// 真实类型是 canonical il2cpp/KingdomEnhancedPlugin.cs（成员形状一致：静态 Instance + LogSource）。
    /// </summary>
    internal sealed class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance;

        internal ManualLogSource LogSource
        {
            get { return null; }
        }
    }
}
