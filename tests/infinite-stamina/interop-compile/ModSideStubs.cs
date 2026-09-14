// interop 编译核验工程的 mod 侧符号 stub：真实定义来自 root 注册后的 ModConfig/ModPanel
// （ConfigEntry<bool> InfiniteSteedStamina）与仓库里的 OptionalQoLScope / KingdomEnhancedPlugin。
// 这里只提供同名同形的最小面，让 PatchRide_InfiniteStamina.cs 能对着真实 Assembly-CSharp
// interop DLL 编一次 —— 游戏侧 API 存在性/签名由真实 DLL 保证，不是本文件。
using System;

namespace KingdomEnhancedMod
{
    public static class ModConfig
    {
        public static ConfigEntryBool InfiniteSteedStamina = new ConfigEntryBool();
        public sealed class ConfigEntryBool { public bool Value; }
    }

    internal static class OptionalQoLScope
    {
        internal static bool IsActive => true;
        internal static bool IsCurrent(UnityEngine.Component component) => component != null;
    }

    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance;
        public Logger LogSource = new Logger();
        public sealed class Logger
        {
            public void LogError(string message) { }
            public void LogWarning(string message) { }
            public void LogInfo(string message) { }
        }
    }
}
