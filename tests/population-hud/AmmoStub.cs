namespace KingdomEnhancedMod;
internal static class SiegeAmmoCounts
{
    internal static bool Ready = true, LastEnabled;
    internal static int Barrels, FireAmmo;
    internal static bool Refresh(Managers managers, bool enabled, float now, bool force = false)
    { LastEnabled = enabled; return enabled && Ready; }
    internal static int Count(int role) => role == 6 ? Barrels : FireAmmo;
}
