using System;
using System.Collections.Generic;
using UnityEngine;

// 协作 API 替身（只在本核对工程使用；canonical 里由 MusketeerIdentity.cs / MusketeerAccess.cs /
// ModConfig.cs / KingdomEnhancedPlugin.cs 提供）。只声明 MusketeerShop.cs 实际消费的形状；
// 一律不运行（调用即抛），核对的是真实 interop 元数据下能否编译。
namespace KingdomEnhancedMod
{
    /// <summary>Operator 契约同形（MusketeerAccess.cs 的导出面）。</summary>
    internal static class MusketeerAccess
    {
        internal static bool Enabled => throw new NotSupportedException();
        internal static bool InWorld(GameObject root) { throw new NotSupportedException("核对工程不运行"); }
        internal static bool InWorld(Component component) { throw new NotSupportedException("核对工程不运行"); }
    }

    /// <summary>Identity worker 契约同形（MusketeerIdentity 的导出面）。</summary>
    internal static class MusketeerIdentity
    {
        internal sealed class IslandState {
            internal readonly List<Career> Careers = new(); internal readonly List<object> StockRestores = new();
            internal bool Ready, ReadOnly, Unresolved, HasBaseline; internal string Epoch, ContextKey; internal long World;
        }
        internal static bool TryContext(out string context, out long world, bool fresh = false) => throw new NotSupportedException();
        internal sealed class Career { internal int Kind, StockSlot; internal long Life; internal DroppableTool Tool; }
        internal static IslandState Current => throw new NotSupportedException();
        internal static bool StockClaimProven(Career career) => throw new NotSupportedException();
        internal static bool TryGetRestockCounts(out int live, out int guns) { throw new NotSupportedException(); }
        internal static bool CanPurchase { get { throw new NotSupportedException("核对工程不运行"); } }
        internal static string StatusText { get { throw new NotSupportedException("核对工程不运行"); } }
        internal static void CopyGuns(List<DroppableTool> destination) { throw new NotSupportedException("核对工程不运行"); }
        internal static int StockSlot(DroppableTool tool) { throw new NotSupportedException("核对工程不运行"); }
        internal static bool TryRegisterPaidGun(DroppableTool tool, int stockSlot = -1) { throw new NotSupportedException("核对工程不运行"); }
        internal static void ForgetUnpaidGun(DroppableTool tool) { throw new NotSupportedException("核对工程不运行"); }
    }

    /// <summary>配置门同形（只在核心路径读 .Value）。</summary>
    internal static class ModConfig
    {
        internal sealed class Flag { internal bool Value { get { throw new NotSupportedException("核对工程不运行"); } } }
        internal static Flag Enabled { get { throw new NotSupportedException("核对工程不运行"); } }
        internal static Flag MusketeerEnabled { get { throw new NotSupportedException("核对工程不运行"); } }
    }

    /// <summary>BepInEx 插件单例的日志面（canonical KingdomEnhancedPlugin.cs）。</summary>
    internal static class KingdomEnhancedPlugin
    {
        internal sealed class LogSink { public void LogInfo(string message) { throw new NotSupportedException("核对工程不运行"); } }
        internal sealed class Plugin { public LogSink LogSource = new LogSink(); }
        internal static Plugin Instance = new Plugin();
    }
}

namespace KingdomEnhancedMod { internal static class GreekBankScope { internal static bool IsActive => throw new NotSupportedException(); } internal static class PatchEconomy_Banker { internal static bool TrySpendForAutoRestock(Banker banker,int price) => throw new NotSupportedException(); } }

namespace KingdomEnhancedMod { internal static class MusketeerCareer { internal const int KindGun=2, NoStockSlot=-1; } }
