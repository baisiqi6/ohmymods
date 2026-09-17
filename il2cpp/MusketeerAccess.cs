using UnityEngine;

namespace KingdomEnhancedMod;

internal static class MusketeerAccess
{
    internal static Transform World { get { try { return Managers.Inst?.world?.gameLayer; } catch { return null; } } }
    internal static bool TrackAllowed
    {
        get { try { return !NetworkBigBoss.IsOnline && NetworkBigBoss.HasWorldAuth && World != null && BiomeHolder.Inst != null && BiomeHolder.Inst.BiomeIndex >= 0; } catch { return false; } }
    }
    internal static bool Enabled => TrackAllowed && ModConfig.Enabled != null && ModConfig.Enabled.Value && ModConfig.MusketeerEnabled != null && ModConfig.MusketeerEnabled.Value;
    internal static bool Playing { get { try { return Enabled && Time.timeScale > 0f && Managers.Inst.game != null && Managers.Inst.game.state == Game.State.Playing; } catch { return false; } } }
    internal static bool InWorld(Component component) { try { return component != null && InWorld(component.gameObject); } catch { return false; } }
    internal static bool InWorld(GameObject root)
    {
        try { var layer = World; return root != null && root.activeInHierarchy && layer != null && layer.gameObject != null && layer.gameObject.activeInHierarchy && root.scene.handle == layer.gameObject.scene.handle && root.transform.IsChildOf(layer); }
        catch { return false; }
    }
}
