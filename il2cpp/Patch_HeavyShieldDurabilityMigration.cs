namespace KingdomEnhancedMod;

internal static class HeavyShieldDurabilityMigration
{
    internal const int CurrentVersion = 5;
    internal const int Maximum = 90;
    internal static int MaximumForVersion(int version) => version switch
    {
        1 => 3,
        2 or 3 => 4,
        4 => 40,
        CurrentVersion => Maximum,
        _ => 0,
    };
    internal static bool TryUpgrade(int sourceVersion, int durability, bool broken, out int upgraded)
    {
        upgraded = 0;
        int oldMaximum = MaximumForVersion(sourceVersion);
        if (oldMaximum == 0 || durability < 0 || durability > oldMaximum || broken != (durability == 0))
            return false;

        // Bounds are checked in the original format before rounding up the positive remainder.
        upgraded = (durability * Maximum + oldMaximum - 1) / oldMaximum;
        return true;
    }
    internal static int WearBand(int durability) => durability switch
    {
        < 0 or > Maximum => -1,
        > 67 => 0, // > 75%
        > 45 => 1, // > 50%
        > 22 => 2, // > 25%
        _ => 3,
    };
}
