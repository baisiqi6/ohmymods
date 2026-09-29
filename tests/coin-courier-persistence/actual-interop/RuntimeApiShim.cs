// RuntimeApiShim.cs — 仅编译门使用：CoinCourierRuntime / KingdomEnhancedPlugin 属于同一插件
// 程序集内的其它生产文件（CoinCourierRuntime.cs / KingdomEnhancedPlugin.cs），它们依赖整个
// Unity/Il2Cpp 世界，不放进本门。这里只按真实生产文件的签名提供最小声明，让本门专注验证：
// CoinCourierSaveData / CoinCourierPersistence / MusketeerPersistence 对真实 2.4 interop
// 程序集成员的使用能够编译。
using BepInEx.Logging;

namespace KingdomEnhancedMod
{
    /// <summary>Save prefix 的取消入口替身（真实实现在 CoinCourierShop.cs，整插件构建覆盖）。</summary>
    internal static class CoinCourierShop
    {
        internal static void CancelPendingTransactions()
        {
        }
    }

    internal static class CoinCourierRuntime
    {
        internal static ICoinCourierCampaignState State => null;

        internal static void Bind(ICoinCourierCampaignState state)
        {
        }

        internal static void Unbind(ICoinCourierCampaignState state)
        {
        }
    }
}

public class KingdomEnhancedPlugin
{
    public static KingdomEnhancedPlugin Instance;
    public ManualLogSource LogSource;
}
