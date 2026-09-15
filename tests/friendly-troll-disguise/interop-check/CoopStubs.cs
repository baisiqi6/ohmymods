using System;
using UnityEngine;

// 协作 API 替身（仅本核对工程使用；canonical 里由 ModConfig.cs / OptionalQoLScope.cs /
// PatchDivine_HermesHeadwear.cs 提供）。gameTypes 一律用真实 interop，不在此替身。

namespace KingdomEnhancedMod
{
    /// <summary>BepInEx ConfigEntry&lt;bool&gt; 的最小形状（只用到 .Value）。</summary>
    public sealed class ConfigEntry<T>
    {
        public T Value;
    }

    public static class ModConfig
    {
        public static ConfigEntry<bool> Enabled;
    }

    internal static class OptionalQoLScope
    {
        internal static bool IsCurrent(Component component)
        {
            throw new NotSupportedException("核对工程不运行");
        }
    }

    internal static class PatchDivine_HermesHeadwear
    {
        internal static bool HasDisguiseHeadwear(FriendlyTroll troll)
        {
            throw new NotSupportedException("核对工程不运行");
        }
    }
}
