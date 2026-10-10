namespace KingdomEnhancedMod;

// Storage substitute for the pre-existing identity/rack/defense lifecycle fixtures. These
// suites deliberately keep their disk fixtures and do not prove native synchronization.
// The synchronized-save and native-save-adapter suites link the real new production code.
internal static class MusketeerNativeSave
{
    internal static bool RejectEndpointUnavailable;
    internal static MusketeerArchiveStore.ReadResult ReadArchive()
        => MusketeerArchiveStore.Load(MusketeerPersistence.ArchivePath);

    internal static bool Stage(string context, string epoch, string hash,
        MusketeerArchiveStore.ReadResult expected, MusketeerArchive updated)
        => updated.TryGetContext(context, out var owner) && owner.Epochs.Contains(epoch)
            && updated.TryGet(epoch, hash, out _)
            && MusketeerArchiveStore.Save(MusketeerPersistence.ArchivePath, expected, updated);

    internal static void ObserveContext(string context, int campaign, int challenge, int land) { }
    internal static void SeedProof(string context, string epoch, string hash) { }
    internal static void NoteCaptureFailure(string context) { }
}
