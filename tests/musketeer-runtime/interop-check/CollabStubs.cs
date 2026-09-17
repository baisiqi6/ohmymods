using System;
using UnityEngine;

// 协作 API 替身（只在本核对工程使用；canonical 里由 MusketeerAccess.cs / MusketeerIdentity.cs /
// PatchRoles_Crossbowman.cs / KingdomEnhancedPlugin.cs 提供）。gameTypes 一律用真实 interop。
namespace KingdomEnhancedMod
{
    /// <summary>Operator 契约同形（MusketeerAccess.cs 的真实形状）。</summary>
    internal static class MusketeerAccess
    {
        internal static Transform World { get { throw new NotSupportedException("核对工程不运行"); } }
        internal static bool TrackAllowed { get { throw new NotSupportedException("核对工程不运行"); } }
        internal static bool Enabled { get { throw new NotSupportedException("核对工程不运行"); } }
        internal static bool Playing { get { throw new NotSupportedException("核对工程不运行"); } }
        internal static bool InWorld(GameObject root) { throw new NotSupportedException("核对工程不运行"); }
        internal static bool InWorld(Component component) { throw new NotSupportedException("核对工程不运行"); }
    }

    /// <summary>Identity worker 契约同形（MusketeerIdentity 的导出面）。</summary>
    internal static class MusketeerIdentity
    {
        internal static void Tick() { throw new NotSupportedException("核对工程不运行"); }
        internal static bool CanPurchase { get { throw new NotSupportedException("核对工程不运行"); } }
        internal static bool HasUnresolved { get { throw new NotSupportedException("核对工程不运行"); } }
        internal static string StatusText { get { throw new NotSupportedException("核对工程不运行"); } }
        internal static bool IsMarked(GameObject root) { throw new NotSupportedException("核对工程不运行"); }
        internal static bool IsUnit(Archer actor) { throw new NotSupportedException("核对工程不运行"); }
        internal static bool IsGun(DroppableTool tool) { throw new NotSupportedException("核对工程不运行"); }
        internal static bool TryRegisterPaidGun(DroppableTool tool) { throw new NotSupportedException("核对工程不运行"); }
        internal static void CopyUnits(System.Collections.Generic.List<Archer> destination) { throw new NotSupportedException("核对工程不运行"); }
        internal static void CopyGuns(System.Collections.Generic.List<DroppableTool> destination) { throw new NotSupportedException("核对工程不运行"); }
        internal static void ForgetUnpaidGun(DroppableTool tool) { throw new NotSupportedException("核对工程不运行"); }
        internal static bool GunPromotionInProgress { get { throw new NotSupportedException("核对工程不运行"); } }
    }

    /// <summary>弩手模块（canonical PatchRoles_Crossbowman.cs）的只读谓词。</summary>
    internal static class PatchRoles_Crossbowman
    {
        internal static bool IsCrossbowman(Archer archer) { throw new NotSupportedException("核对工程不运行"); }
    }

    /// <summary>BepInEx 插件单例的日志面（canonical KingdomEnhancedPlugin.cs）。</summary>
    internal sealed class InteropCheckLogSource
    {
        public void LogInfo(string message) { throw new NotSupportedException("核对工程不运行"); }
        public void LogWarning(string message) { throw new NotSupportedException("核对工程不运行"); }
        public void LogError(string message) { throw new NotSupportedException("核对工程不运行"); }
    }

    internal sealed class InteropCheckPlugin
    {
        public readonly InteropCheckLogSource LogSource = new InteropCheckLogSource();
    }

    internal static class KingdomEnhancedPlugin
    {
        internal static InteropCheckPlugin Instance = new InteropCheckPlugin();
    }
}
