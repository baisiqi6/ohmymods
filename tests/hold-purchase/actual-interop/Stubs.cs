using System;
using UnityEngine;
namespace KingdomEnhancedMod;
internal static class ModConfig { internal sealed class Flag { public bool Value=>throw new NotSupportedException(); } internal static Flag HoldPurchaseEnabled=>throw new NotSupportedException(); }
internal static class OptionalQoLScope { internal static bool IsActive=>throw new NotSupportedException(); internal static bool IsCurrent(Component value)=>throw new NotSupportedException(); }
internal static class ModPanel { internal static bool IsShown=>throw new NotSupportedException(); }
internal sealed class KingdomEnhancedPlugin { internal static KingdomEnhancedPlugin Instance=>throw new NotSupportedException(); internal Logger LogSource=>throw new NotSupportedException(); }
internal sealed class Logger { internal void LogWarning(string message)=>throw new NotSupportedException(); internal void LogInfo(string message)=>throw new NotSupportedException(); }
// Identity statics the source-linked manual-payable partial reads. The interop project only
// type-checks the production patch against real game assemblies; it never runs the shop.
internal static partial class MusketeerShop
{
    internal static GameObject _object;
    internal static PayableComponent _payable;
    internal static MusketeerShopOwner _owner;
    internal static Transform _layer;
    internal static bool _clearing;
    internal static bool _retiring;
}
internal sealed class MusketeerShopOwner : Component { }
