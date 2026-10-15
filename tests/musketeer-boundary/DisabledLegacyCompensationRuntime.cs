using System;

namespace KingdomEnhancedMod;

// Unactivated feature boundary only. Existing identity/save/defense suites link the real
// Identity and Persistence, but do not instantiate the Unity compensation runtime. This
// substitute never creates a plan, grant, receipt, birth ticket or completed capture and
// must not be used as evidence for the compensation lifecycle or native writer guard.
internal static class MusketeerLegacyCompensationRuntime
{
    internal static void OnLoaded(MusketeerIdentity.IslandState state, string rawJson) { }
    internal static void Tick(MusketeerIdentity.IslandState state) { }
    internal static bool ExcludesSourceEpoch(string context, string epoch) => false;
    internal static bool OwnsBirth(Character character, Guid id, Guid request) => false;
    internal static bool TryGetPendingReceipt(MusketeerIdentity.IslandState state,
        out MusketeerLegacyCompensationReceipt pending) { pending = null; return false; }
    internal static void CapturePrepared(MusketeerLegacyCompensationReceipt complete) { }
    internal static string StatusText => "compensation runtime disabled in this host suite";
}

// These existing suites have no purchased hero careers. Only satisfy the newly linked
// cross-career exclusion API; hero purchase/identity behavior belongs to its own suites.
internal static class HeroRecruitment
{
    internal static bool HasPurchasedCareer(Character character) => false;
}
