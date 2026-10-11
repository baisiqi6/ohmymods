namespace KingdomEnhancedMod;
// Controlled transport seam for production persistence integration tests.
// The real adapter lifecycle is tested separately; this is not native hook proof.
internal static class HeavyShieldNativeLoadProof
{
    internal static GlobalSaveData Owner;
    internal static HeavyShieldLegacyLoadProof Proof;
    internal static bool TryMatch(GlobalSaveData global, string key, int slot,
        string guid, int challenge, int land, string legacy, string canonical)
        => Owner != null && global != null && Owner.Pointer == global.Pointer
           && GlobalSaveData._loaded?.Pointer == global.Pointer
           && Proof != null && Proof.Matches(key, slot, guid, challenge, land, legacy, canonical);
}
