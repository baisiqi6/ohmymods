namespace KingdomEnhancedMod;

internal static class HeavyShieldDurabilityMigration
{
    internal const int CurrentVersion = 4;
    internal const int Maximum = 40;
    internal static bool TryUpgrade(int sourceVersion, int durability, bool broken, out int upgraded)
    {
        upgraded = 0;
        int oldMaximum = sourceVersion switch { 1 => 3, 2 or 3 => 4, CurrentVersion => Maximum, _ => 0 };
        if (oldMaximum == 0 || durability < 0 || durability > oldMaximum || broken != (durability == 0))
            return false;

        // Bounds are checked in the original format before rounding up the positive remainder.
        upgraded = (durability * Maximum + oldMaximum - 1) / oldMaximum;
        return true;
    }
    internal static int WearBand(int durability) => durability switch
    {
        < 0 or > Maximum => -1,
        > 30 => 0,
        > 20 => 1,
        > 10 => 2,
        _ => 3,
    };
}
