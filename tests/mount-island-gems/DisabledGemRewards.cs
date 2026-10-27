namespace KingdomEnhancedMod;

// Optional-feature boundary for the existing Musketeer-only host suites.
// Those suites exercise their real writer prefixes; Gem policy is tested by MountIslandGemTests.
internal static class MountIslandGemRewards
{
    internal static bool CanWriteNativePayload(string folder, string filename, byte[] data, out string reason)
    { reason = null; return true; }
}
