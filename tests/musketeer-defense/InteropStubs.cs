using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod
{
    // Canonical mod-side API stubs for the actual-interop compile check. Everything game-side
    // (Kingdom/Archer/EnemyManager/Managers/Side/...) resolves against the real 2.4 interop.
    internal static class MusketeerAccess
    {
        internal static bool Enabled => false;
        internal static Transform World => null;
        internal static bool InWorld(Component component) => false;
    }

    internal static class MusketeerIdentity
    {
        internal static void CopyUnits(List<Archer> destination) { }
        internal static bool IsUnit(Archer actor) => false;
    }

    internal static class UnitScanCache
    {
        internal static Archer[] GetArchers(float maxAgeSec = 3f) => null;
    }

    internal static class NativeIndexWitness
    {
        // This deliberately names the concrete 2.4 metadata type, beyond var/Count duck typing.
        internal static Archer At(Kingdom kingdom, int index)
        {
            Il2CppSystem.Collections.Generic.List<Archer> list = kingdom._availableArchersCache;
            return index < list.Count ? list[index] : null;
        }
    }

    internal class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance = null;
        internal LogSource LogSource = null;
    }

    internal class LogSource
    {
        internal void LogInfo(string message) { }
        internal void LogWarning(string message) { }
    }
}
