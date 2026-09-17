// Collaborator stubs for the compile-only IL2CPP 2.4 interop gate.
//
// This project compiles the real work/il2cpp/PatchArcher_Impact.cs against the REAL
// BepInEx IL2CPP interop assemblies (Assembly-CSharp, UnityEngine.*, Il2CppInterop).
// Only the mod-owned collaborators that the fx module calls are stubbed here, and every
// member mirrors the real signature that the full plugin build uses (see
// il2cpp/PatchArcher_GreekImpact.cs, ArcherOptionsScope.cs, ModConfig.cs,
// KingdomEnhancedPlugin.cs in the same snapshot). Nothing here runs.

using System;
using UnityEngine;

namespace KingdomEnhancedMod
{
    internal static class ModConfig
    {
        internal sealed class Flag { internal bool Value => throw new NotSupportedException(); }
        internal static Flag ArcherImpactEnabled => throw new NotSupportedException();
    }

    internal static class ArcherOptionsScope
    {
        internal static bool IsActive => throw new NotSupportedException();
        internal static bool IsCurrent(Component component) => throw new NotSupportedException();

        internal static bool TryGetContext(out IntPtr world, out IntPtr layer, out int scene)
        {
            world = default;
            layer = default;
            scene = 0;
            throw new NotSupportedException();
        }
    }

    /// <summary>FX 侧消费的希腊火矢接口（真实实现见 PatchArcher_GreekImpact.cs）。</summary>
    internal static class PatchArcher_GreekImpact
    {
        internal static bool AnyOriginEnabled => throw new NotSupportedException();
        internal static bool IsOriginEnabledFor(bool heroOrigin) => throw new NotSupportedException();

        internal static bool TryGetEligibleOrigin(Arrow arrow, out bool heroOrigin)
        {
            heroOrigin = false;
            throw new NotSupportedException();
        }

        internal struct HitTicket
        {
            internal bool Valid;
        }

        internal static HitTicket BeginHit(Arrow arrow, GameObject target) => throw new NotSupportedException();
        internal static bool EndHit(Arrow arrow, HitTicket ticket) => throw new NotSupportedException();
        internal static void AbortHit(Arrow arrow, HitTicket ticket) => throw new NotSupportedException();
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
