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
