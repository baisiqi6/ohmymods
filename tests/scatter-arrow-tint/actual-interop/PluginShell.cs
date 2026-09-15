using System;
using BepInEx.Logging;

namespace KingdomEnhancedMod;

/// <summary>
/// 编译门外壳：只补齐 mod 自有（非 interop）依赖的最小表面——配置项、世界范围 helper、插件日志。
/// 这些文件本身在整工程编译门里用真实实现编译，这里只证明 ScatterArrowTint.cs 对发行 interop
/// （Arrow / ByteBuffer / SpriteRenderer / NetworkBigBoss / HarmonyLib）的成员引用成立。
/// </summary>
public static class ModConfig
{
    // 编译门只验证类型面（真实 BepInEx ConfigEntry<T> 无公开单参构造），不执行。
    public static BepInEx.Configuration.ConfigEntry<bool> Enabled;
    public static BepInEx.Configuration.ConfigEntry<bool> ArcherScatterEnabled;
}

internal static class ArcherOptionsScope
{
    internal static bool IsActive => false;

    internal static bool IsCurrent(UnityEngine.Component component) => false;

    internal static bool TryGetContext(out IntPtr world, out IntPtr layer, out int scene)
    {
        world = IntPtr.Zero;
        layer = IntPtr.Zero;
        scene = 0;
        return false;
    }
}

public class KingdomEnhancedPlugin
{
    public static KingdomEnhancedPlugin Instance;

    public ManualLogSource LogSource => null;
}
