// 仅用于 actual 2.4 interop 编译核对：ArcherOptionsScope / KingdomEnhancedPlugin 是 MOD 侧类型，
// 不在游戏 interop 里；本文件只补齐这两个引用点，其余（Arrow/_collider、Collider2D、Physics2D、
// Wall、FindObjectsOfType<Wall>、Time.unscaledTime、Object.Destroy…）必须全部来自 E 盘 2.4 interop
// 程序集——编译通过即证明这些原生成员真实存在。
namespace KingdomEnhancedMod
{
    internal static class ArcherOptionsScope
    {
        internal static bool TryGetContext(out System.IntPtr world, out System.IntPtr layer, out int scene)
        {
            world = System.IntPtr.Zero;
            layer = System.IntPtr.Zero;
            scene = 0;
            return false;
        }
    }

    /// <summary>HeroArcherWallPierce 的账本上限 = 视觉回执容量（同一 source of truth）。本 compile-only 门
    /// 只编穿墙 slice 一个生产文件，故只补这一个常量；两文件真正同编的核对由 tests/hero-arrow-pierce 与
    /// tests/hero-artemis-arrow 的替身套件负责（那里 Capacity 就是真实声明）。</summary>
    internal static class HeroArcherArrowVisuals
    {
        internal const int Capacity = 128;
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
