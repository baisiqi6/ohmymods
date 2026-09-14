using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Scope shared by the three opt-in convenience features.</summary>
internal static class OptionalQoLScope
{
    internal static bool IsActive
    {
        get
        {
            try
            {
                return ModConfig.Enabled != null && ModConfig.Enabled.Value
                    && BiomeHolder.Inst != null
                    && BiomeHolder.Inst.BiomeIndex >= 0;
            }
            catch { return false; }
        }
    }

    internal static bool IsCurrent(Component component)
    {
        try
        {
            var world = Managers.Inst != null ? Managers.Inst.world : null;
            var layer = world != null ? world.gameLayer : null;
            return component != null && component.gameObject != null
                && component.gameObject.activeInHierarchy && layer != null
                && layer.gameObject != null && layer.gameObject.activeInHierarchy
                && component.gameObject.scene.handle == layer.gameObject.scene.handle
                && component.transform.IsChildOf(layer);
        }
        catch { return false; }
    }
}
