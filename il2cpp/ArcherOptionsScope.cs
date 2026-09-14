using System;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class ArcherOptionsScope
{
    internal static bool IsActive
    {
        get
        {
            try { return OptionalQoLScope.IsActive && Managers.Inst != null
                && Managers.Inst.game != null && Managers.Inst.game.playingOrInMenuWithClient; }
            catch { return false; }
        }
    }
    internal static bool IsCurrent(Component component) => OptionalQoLScope.IsCurrent(component);

    internal static bool TryGetContext(out IntPtr world, out IntPtr layer, out int scene)
    {
        world = layer = IntPtr.Zero; scene = 0;
        try
        {
            var current = Managers.Inst != null ? Managers.Inst.world : null;
            var root = current != null ? current.gameLayer : null;
            if (!IsActive || root == null || root.gameObject == null || !root.gameObject.activeInHierarchy) return false;
            world = current.Pointer; layer = root.Pointer; scene = root.gameObject.scene.handle;
            return world != IntPtr.Zero && layer != IntPtr.Zero;
        }
        catch { return false; }
    }
}
