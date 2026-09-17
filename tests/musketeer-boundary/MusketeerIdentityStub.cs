namespace KingdomEnhancedMod;
// Boundary double only: production identity is tested in musketeer-identity.
internal static class MusketeerIdentity
{
    internal static object Marked;
    internal static bool IsUnit(object actor) => actor != null && object.ReferenceEquals(actor, Marked);
    internal static bool IsMarked(object actor) => IsUnit(actor);
    internal static bool IsGun(object tool) => IsUnit(tool);
    internal static bool GunPromotionInProgress;
}
