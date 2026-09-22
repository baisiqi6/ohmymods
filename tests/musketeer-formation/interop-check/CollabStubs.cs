// Compile-only collaborators for the 2.4 interop gate. This project compiles the REAL
// work/il2cpp/Patch_MusketeerFormation.cs against the REAL BepInEx IL2CPP interop assemblies;
// only mod-owned types the file calls are stubbed here, with the same member shapes the full
// plugin build uses. Nothing here runs.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod
{
    /// <summary>Real: il2cpp/MusketeerAccess.cs (TrackAllowed + config + offline authority).</summary>
    internal static class MusketeerAccess
    {
        internal static bool Enabled => throw new NotSupportedException();
        internal static bool InWorld(Component component) => throw new NotSupportedException();
    }

    /// <summary>Real: il2cpp/MusketeerIdentity.cs (bounded career registry reads).</summary>
    internal static class MusketeerIdentity
    {
        internal static bool IsUnit(Archer archer) => throw new NotSupportedException();
        internal static void CopyUnits(List<Archer> destination) => throw new NotSupportedException();
    }

    /// <summary>Real: il2cpp/HeroArcherRuntime.cs (live hero identity).</summary>
    internal static class HeroArcherRuntime
    {
        internal static bool IsHero(Archer archer) => throw new NotSupportedException();
    }

    /// <summary>Real: il2cpp/CrossbowmanLifecycle.cs (marker-backed identity reader; fail-closed).</summary>
    internal static class CrossbowmanLifecycle
    {
        internal static bool IsCrossbowman(Archer archer) => throw new NotSupportedException();
    }

    /// <summary>Real: il2cpp/PatchWorld_FleetBoatFormation.cs internal queries.</summary>
    internal static class PatchWorld_FleetBoatFormation
    {
        internal static bool HasMusketeerRow(Formation formation) => throw new NotSupportedException();
        internal static bool HasDirtyMusketeerTypes(Formation formation) => throw new NotSupportedException();
    }

    internal sealed class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance => throw new NotSupportedException();
        internal Logger LogSource => throw new NotSupportedException();
    }

    internal sealed class Logger
    {
        internal void LogInfo(string message) => throw new NotSupportedException();
        internal void LogWarning(string message) => throw new NotSupportedException();
    }
}
