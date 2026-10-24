// Test-boundary stub shared by the shared-bank regression programs (capture/recovery/hotpath).
//
// The production `PatchWorld_WallSpots` class lives in a large Android-facing file that pulls in
// Unity/interop surface these host programs intentionally do not model. `MainBankerFixedDomain`
// only reads the single marker-prefix constant below to separate mod-generated wall bases from
// native ones, so the host harness exposes that real production value at the same boundary the
// tests already fake everywhere else. Keep this value identical to
// il2cpp/PatchWorld_WallSpots.cs (MarkerPrefix = "KEM_WallSpot").
namespace KingdomEnhancedMod;

public static class PatchWorld_WallSpots
{
    internal const string MarkerPrefix = "KEM_WallSpot";
}
