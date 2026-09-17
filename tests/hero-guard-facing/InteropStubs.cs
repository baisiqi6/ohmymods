// 仅用于 actual 2.4 interop 编译核对：HeroArcherRuntime / KingdomEnhancedPlugin 是 MOD 侧类型，
// 不在游戏 interop 里；本文件只补齐这两个引用点，其余（Archer/Mover/Director/Kingdom/...）必须来自
// E 盘 2.4 interop 程序集——编译通过即证明这些原生成员真实存在。
namespace KingdomEnhancedMod
{
    internal static class HeroArcherRuntime
    {
        internal static bool Enabled => false;
        internal static bool IsHero(Archer archer) => false;
        internal static int CurrentActorLife(Archer archer) => 0;
    }

    internal class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance = null;
        internal Log LogSource = null;
    }

    internal class Log
    {
        internal void LogInfo(string message) { }
        internal void LogWarning(string message) { }
    }
}
