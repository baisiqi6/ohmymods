namespace KingdomEnhancedMod;

// Storage substitute for the pre-existing identity/rack/defense lifecycle fixtures. These
// suites deliberately keep their disk fixtures and do not prove native synchronization.
// The synchronized-save and native-save-adapter suites link the real new production code.
internal static class MusketeerNativeSave
{
    internal static bool RejectEndpointUnavailable;
    internal static int StageCalls, CaptureFailures, LegacyCaptureNotes;
    // The old disk-fixture suites never activate compensation. Keep this platform boundary
    // disabled; the new native-gate suite links real Patch_MusketeerNativeSave instead.
    internal static bool AnyLegacyCompensationPending => false;
    internal static bool BeginLegacyCompensation(System.Guid request, string context, string epoch,
        System.Collections.Generic.IReadOnlyList<System.Guid> grants, out string reason)
        { reason = "compensation disabled in legacy disk-fixture host"; return false; }
    internal static bool CancelLegacyBeforeCapture(System.Guid request, string context, string epoch,
        out string reason) { reason = "compensation disabled in legacy disk-fixture host"; return false; }
    internal static void NoteLegacySceneCapture(string context) { LegacyCaptureNotes++; }
    internal static MusketeerArchiveStore.ReadResult ReadArchive()
        => MusketeerArchiveStore.Load(MusketeerPersistence.ArchivePath);

    internal static bool Stage(string context, string epoch, string hash,
        MusketeerArchiveStore.ReadResult expected, MusketeerArchive updated)
    {
        StageCalls++;
        return updated.TryGetContext(context, out var owner) && owner.Epochs.Contains(epoch)
            && updated.TryGet(epoch, hash, out _)
            && MusketeerArchiveStore.Save(MusketeerPersistence.ArchivePath, expected, updated);
    }

    internal static void ObserveContext(string context, int campaign, int challenge, int land) { }
    internal static void SeedProof(string context, string epoch, string hash) { }
    internal static void NoteCaptureFailure(string context) { CaptureFailures++; }
}
