using System;
using UnityEngine;

// ============================================================================
// 协作 API 替身（真实实现在 canonical il2cpp/ 下）。
// ModConfig / NetworkBigBoss / OptionalQoLScope 是 root 侧模块，PatchDivine_HermesHeadwear
// 是另一 worker 提供的 44 款头饰 API —— 本任务只依赖其 bool 返回值，故用可驱动的替身。
// ============================================================================

namespace KingdomEnhancedMod
{
    /// <summary>ModConfig.Enabled（原生为 ConfigEntry&lt;bool&gt;）的值替身。</summary>
    public sealed class ConfigEntry<T>
    {
        public T Value;
    }

    public static class ModConfig
    {
        public static ConfigEntry<bool> Enabled = new ConfigEntry<bool> { Value = true };
    }

    public static class NetworkBigBoss
    {
        public static bool HasWorldAuth;
    }

    /// <summary>
    /// 与 root 的 OptionalQoLScope.IsCurrent(Component) 同签名。真实实现按
    /// Managers.Inst.world.gameLayer + scene.handle + transform 父链判定“本岛/本世代”；
    /// 替身保留 activeInHierarchy 部分，用 <see cref="SameIsland"/> 代替场景/父链判定
    /// （换岛滞留 → false）。
    /// </summary>
    internal static class OptionalQoLScope
    {
        internal static bool SameIsland = true;

        internal static bool IsCurrent(Component component)
        {
            try
            {
                return component != null && component.gameObject != null
                    && component.gameObject.activeInHierarchy && SameIsland;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Operator 提供的 44 款头饰查询（本任务不实现）。替身可被驱动：返回值、抛错、
    /// 调用次数与实参 troll（用于断言是真委托，不是测试镜像判定）。
    /// </summary>
    internal static class PatchDivine_HermesHeadwear
    {
        internal static bool DisguiseHeadwear;
        internal static bool Throw;
        internal static int Calls;
        internal static FriendlyTroll LastTroll;

        internal static bool HasDisguiseHeadwear(FriendlyTroll troll)
        {
            Calls++;
            LastTroll = troll;
            if (Throw) throw new InvalidOperationException("headwear query boom");
            return DisguiseHeadwear;
        }
    }
}
