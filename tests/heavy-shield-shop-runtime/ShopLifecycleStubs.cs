using KingdomEnhancedMod;
using UnityEngine;

// Native engine and unrelated career boundaries only. The real shop, stock controller,
// native adapters, shield visual and light adapter are all source-linked by Tests.csproj.
public class Holder { public UnityEngine.Object firePrefab = new(); }
public class FirePlaceholder : Component { public bool burnsAtNightOnly = true; }
public class BambooLamp : Component { }
// Only the lifecycle boundary under test is simulated. This is not native execution.
public class AutoLightRegister : Component
{
    public static bool IgnoreNightLightOnlyWrites, ThrowNightLightOnlyWrites;
    private bool _nightLightOnly = true;
    public bool nightLightOnly
    {
        get => _nightLightOnly;
        set
        {
            if (ThrowNightLightOnlyWrites) throw new InvalidOperationException("night policy unavailable");
            if (!IgnoreNightLightOnlyWrites) _nightLightOnly = value;
        }
    }
    public int EnableCalls;
    public bool? FirstEnableNightLightOnly;
    public void OnEnable()
    {
        if (EnableCalls++ == 0) FirstEnableNightLightOnly = _nightLightOnly;
    }
}
public static class Blender
{
    public static bool LightsOn, Throw;
    public static bool ShouldLightsBeOn(Vector3 position)
    { if (Throw) throw new InvalidOperationException("sample unavailable"); return LightsOn; }
}
namespace KingdomEnhancedMod
{
    internal static class ModConfig
    {
        internal sealed class Flag { internal bool Value = true; }
        internal static Flag Enabled = new(), MusketeerEnabled = new(), HeavyShieldEnabled = new();
    }
    internal static class MusketeerIdentity
    {
        internal const int StockSlots = 3;
        internal sealed class State { }
        internal sealed class Career
        {
            internal DroppableTool Tool; internal State State;
            internal int StockSlot, GoId; internal long Life; internal Guid Id;
        }
        internal static bool HasExactStockBinding(Career career) => false;
    }
    internal static class MusketeerGunVisuals
    {
        internal static bool IsParked(DroppableTool tool) => false;
        internal static void SetParked(DroppableTool tool, bool parked) => throw new NotSupportedException("unrelated musket visual");
    }
}
