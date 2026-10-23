// Immutable pre-Pop saved-geometry evidence. Nothing here reads native objects or files.
// The private constructors keep callers from inventing a proof; only the factories below create one,
// and only PalaceCourtArea publishes a proof after the exact Pop call closes successfully.
#nullable enable
using System;

namespace KingdomEnhancedMod;

internal sealed class PalaceSavedGeometryCandidate
{
    internal readonly PalaceLayoutRecipe Recipe;
    internal readonly string SnapshotSha256;
    internal readonly IntPtr GamePointer;
    internal readonly IntPtr GlobalPointer;
    internal readonly IntPtr CampaignPointer;
    internal readonly IntPtr IslandPointer;
    internal readonly IntPtr LevelPointer;
    internal readonly IntPtr WorldPointer;
    internal readonly IntPtr WorldLayerPointer;
    internal readonly int WorldScene;
    internal readonly int WorldLayerScene;
    internal readonly PalacePaidScope Owner;
    internal readonly string TripId;
    internal readonly long CallSequence;

    private PalaceSavedGeometryCandidate(PalaceLayoutRecipe recipe, string snapshotSha256,
        IntPtr game, IntPtr global, IntPtr campaign, IntPtr island, IntPtr level,
        IntPtr world, IntPtr worldLayer, int worldScene, int worldLayerScene,
        PalacePaidScope owner, string tripId, long callSequence)
    {
        Recipe = recipe; SnapshotSha256 = snapshotSha256;
        GamePointer = game; GlobalPointer = global; CampaignPointer = campaign;
        IslandPointer = island; LevelPointer = level;
        WorldPointer = world; WorldLayerPointer = worldLayer;
        WorldScene = worldScene; WorldLayerScene = worldLayerScene;
        Owner = owner; TripId = tripId; CallSequence = callSequence;
    }

    internal static PalaceSavedGeometryCandidate? Create(PalaceLayoutRecipe? recipe, string? snapshotSha256,
        IntPtr game, IntPtr global, IntPtr campaign, IntPtr island, IntPtr level,
        IntPtr world, IntPtr worldLayer, int worldScene, int worldLayerScene,
        PalacePaidScope? owner, string? tripId, long callSequence)
    {
        if (!PalaceLayoutRecipe.IsKnown(recipe!) || !Hash(snapshotSha256) || owner == null
            || string.IsNullOrEmpty(tripId) || !Guid.TryParseExact(tripId, "D", out Guid id) || id == Guid.Empty
            || game == IntPtr.Zero || global == IntPtr.Zero || campaign == IntPtr.Zero
            || island == IntPtr.Zero || level == IntPtr.Zero
            || world == IntPtr.Zero || worldLayer == IntPtr.Zero
            || callSequence <= 0) return null;
        return new PalaceSavedGeometryCandidate(recipe!, snapshotSha256!, game, global, campaign, island, level,
            world, worldLayer, worldScene, worldLayerScene, owner, tripId!, callSequence);
    }

    private static bool Hash(string? text)
    {
        if (text == null || text.Length != 64) return false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        }
        return true;
    }
}

internal sealed class PalaceSavedGeometryProof
{
    internal readonly PalaceSavedGeometryCandidate Candidate;
    internal readonly long PopSequence;

    private PalaceSavedGeometryProof(PalaceSavedGeometryCandidate candidate, long popSequence)
    {
        Candidate = candidate; PopSequence = popSequence;
    }

    internal static PalaceSavedGeometryProof? Publish(PalaceSavedGeometryCandidate? candidate, long callSequence)
    {
        if (candidate == null || callSequence <= 0 || callSequence != candidate.CallSequence) return null;
        return new PalaceSavedGeometryProof(candidate, callSequence);
    }
}
