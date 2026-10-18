using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

// #260 Worker A contract tests.
//
// These tests link the production pure files unchanged (no mirrored policy):
//   il2cpp/Patch_MountIslandRequestEntry.cs   (entry policy, tracker, terminal read gate, shape policy)
//   il2cpp/Patch_MountIslandFreshEvidence.cs  (gate, advance classifier, route fence)
//   il2cpp/Patch_MountIslandAvailabilityPolicy.cs / Patch_MountIslandSplitPolicy.cs (existing dependencies)
//
// Native metadata/read behaviour is NOT exercised here. The separate InteropCheck project only proves
// that Patch_MountIslandNativeResult.cs compiles against the installed interop assemblies; it does not
// load or execute GameAssembly.
internal static class Program
{
    private static int _checks;
    private static readonly List<string> Failures = new List<string>();

    private static void Check(bool condition, string label)
    {
        _checks++;
        if (!condition) Failures.Add(label);
    }

    private static MountIslandOwnerIdentity Owner(int land, ulong campaign = 0xAAA0, int selected = 0,
        ulong global = 0xC0DE, ulong holder = 0xF00D, int biome = 1, int currentLand = -1)
    {
        return new MountIslandOwnerIdentity
        {
            GlobalPointer = global,
            SelectedCampaignIndex = selected,
            CampaignPointer = campaign,
            ReignIndex = 2,
            LandDataPointer = 0xBBB0UL,
            IslandsPointer = 0xCCC0UL,
            HolderInstancePointer = holder,
            HolderBiomeIndex = biome,
            CurrentLand = currentLand < 0 ? land : currentLand,
        };
    }

    private static MountIslandRequestEnvelope Envelope(int land = 11, ulong routine = 0x9000,
        int campaignIndex = 0, ulong preSlot = 0x1000, bool prePositive = false, bool preReadsOk = true,
        bool scope = true, MountIslandOwnerIdentity owner = default)
    {
        return new MountIslandRequestEnvelope
        {
            ScopeOk = scope,
            RoutinePointer = routine,
            RequestCampaignIndex = campaignIndex,
            RequestChallengeId = 0,
            RequestLand = land,
            Owner = owner.GlobalPointer != 0UL ? owner : Owner(land),
            PreSlotPresent = preSlot != 0,
            PreSlotPointer = preSlot,
            PreHistoryReadsOk = preReadsOk,
            PreHistoryAnyPositive = prePositive,
        };
    }

    private static MountIslandTicketDraft Draft(int land = 11, int flags = 0x410, ulong routine = 0x9000,
        int campaignIndex = 0, ulong slot = 0x1000, bool historyReadsOk = true, bool historyPositive = false,
        MountIslandOwnerIdentity owner = default)
    {
        return new MountIslandTicketDraft
        {
            CompletionClass = MountIslandCompletionClassifier.Classify(Completion(flags)),
            RawFlags = flags,
            Owner = owner.GlobalPointer != 0UL ? owner : Owner(land),
            RoutinePointer = routine,
            RequestCampaignIndex = campaignIndex,
            RequestChallengeId = 0,
            RequestLand = land,
            HistoryReadsOk = historyReadsOk,
            HistoryAnyPositive = historyPositive,
            SlotPointer = slot,
        };
    }

    private static MountIslandLoadCompletion Completion(int flags, int preState = 1, bool runOriginal = true,
        bool completedNormally = true, int postState = -1, bool flagsReadOk = true)
    {
        return new MountIslandLoadCompletion
        {
            FlagsReadOk = flagsReadOk,
            RawFlags = flags,
            ActiveResume = preState == 1 || preState == 2,
            RunOriginal = runOriginal,
            CompletedNormally = completedNormally,
            PostState = postState,
        };
    }

    private static MountIslandAdvanceInput AdvanceInput(int preState, bool result, int postState,
        bool envelopeSealed = true, bool sameRoutine = true, bool sameRequest = true,
        bool runOriginal = true, bool postfixRan = true)
    {
        return new MountIslandAdvanceInput
        {
            PreState = preState,
            RunOriginal = runOriginal,
            PostfixRan = postfixRan,
            Result = result,
            PostState = postState,
            EnvelopeSealed = envelopeSealed,
            SameRoutine = sameRoutine,
            SameRequest = sameRequest,
        };
    }

    private static MountIslandRequestEntryInput EntryInput(int preState,
        MountIslandEnvelopeState state = MountIslandEnvelopeState.Started,
        bool sameRoutine = true, bool sameRequest = true, bool entrySeen = false)
    {
        return new MountIslandRequestEntryInput
        {
            PreState = preState,
            EnvelopeState = state,
            SameRoutine = sameRoutine,
            SameRequest = sameRequest,
            Entry1SeenForCurrentGeneration = entrySeen,
        };
    }

    private static MountIslandRouteFence EvaluateFence(MountIslandFreshGate gate, ulong attemptRoutine,
        int campaignIndex, int challengeId, int land, long attemptGeneration, bool snapshotOk = true)
    {
        bool sealedNow = gate.EnvelopeSealed;
        MountIslandRequestEnvelope envelope = sealedNow ? gate.Envelope : default;
        return MountIslandAdapterRouteFence.Evaluate(
            sealedNow, envelope.RoutinePointer,
            envelope.RequestCampaignIndex, envelope.RequestChallengeId, envelope.RequestLand,
            snapshotOk, attemptRoutine, campaignIndex, challengeId, land,
            attemptGeneration, gate.Generation);
    }

    private sealed class CountingReader : IMountIslandTerminalFlagsReader
    {
        internal int Calls;
        internal int Value;
        internal string Error = "ok";

        public bool TryRead(out int flags, out string reason)
        {
            Calls++;
            flags = Value;
            reason = Error;
            return true;
        }
    }

    private static int Main()
    {
        PolicyTable();
        TrackerBounded();
        ZeroToOneSealsOnce();
        LegitReusePublishesOnce();
        CachedSuccessRevokesOwn();
        RestartNewGeneration();
        StaleAttemptReaderZero();
        ForeignUnreadableSkippedThrowClosed();
        Pc0FailurePc1Rebuild();
        OldEmptyStillRejected();
        NormalYieldKeepsStarted();
        ShapePolicyBattery();

        Console.WriteLine("checks=" + _checks + " failures=" + Failures.Count);
        foreach (string failure in Failures) Console.WriteLine("FAIL: " + failure);
        return Failures.Count == 0 ? 0 : 1;
    }

    private static void PolicyTable()
    {
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(0, MountIslandEnvelopeState.None))
            == MountIslandRequestEntryAction.SealInitial, "pc0 always starts a new request boundary");
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(0, MountIslandEnvelopeState.Started))
            == MountIslandRequestEntryAction.SealInitial, "pc0 reseal replaces any previous request");

        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(1))
            == MountIslandRequestEntryAction.ContinueAndMarkEntry1,
            "pc1 started same routine/request with entry unseen reuses the sealed pre-image");
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(1, entrySeen: true))
            == MountIslandRequestEntryAction.SealNewGeneration,
            "pc1 started same routine/request after entry seen is a restart -> new generation");
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(1, sameRoutine: false))
            == MountIslandRequestEntryAction.SealNewGeneration,
            "pc1 different routine -> new data request");
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(1, sameRequest: false))
            == MountIslandRequestEntryAction.SealNewGeneration,
            "pc1 mutated request fields -> new data request");
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(1, MountIslandEnvelopeState.CompletedOnce))
            == MountIslandRequestEntryAction.SealNewGeneration,
            "pc1 after CompletedOnce -> new generation");
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(1, MountIslandEnvelopeState.Transferred))
            == MountIslandRequestEntryAction.SealNewGeneration,
            "pc1 after Transferred -> new generation");
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(1, MountIslandEnvelopeState.Closed))
            == MountIslandRequestEntryAction.SealNewGeneration,
            "pc1 without a Started envelope -> new generation");
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(1, MountIslandEnvelopeState.None))
            == MountIslandRequestEntryAction.SealNewGeneration,
            "pc1 with no envelope -> new generation");

        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(2, entrySeen: true))
            == MountIslandRequestEntryAction.ResumeOnly,
            "pc2 resumes the same captured request and never seals");
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(2, entrySeen: false))
            == MountIslandRequestEntryAction.Reject,
            "pc2 first seen without a captured data entry is rejected");
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(2, MountIslandEnvelopeState.CompletedOnce, entrySeen: true))
            == MountIslandRequestEntryAction.Reject,
            "pc2 after CompletedOnce is rejected (no new seal)");
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(2, MountIslandEnvelopeState.None, entrySeen: true))
            == MountIslandRequestEntryAction.Reject,
            "pc2 with no envelope is rejected (no seal)");
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(2, sameRoutine: false, entrySeen: true))
            == MountIslandRequestEntryAction.Reject,
            "pc2 different routine is rejected");
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(2, sameRequest: false, entrySeen: true))
            == MountIslandRequestEntryAction.Reject,
            "pc2 mutated request fields are rejected");
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(-1, MountIslandEnvelopeState.Started, entrySeen: true))
            == MountIslandRequestEntryAction.Reject,
            "closed prestate is rejected");
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(3, MountIslandEnvelopeState.Started, entrySeen: true))
            == MountIslandRequestEntryAction.Reject,
            "unexpected prestate is rejected");

        Check(MountIslandRequestEntryPolicy.RequiresSeal(MountIslandRequestEntryAction.SealInitial),
            "SealInitial requires seal");
        Check(MountIslandRequestEntryPolicy.RequiresSeal(MountIslandRequestEntryAction.SealNewGeneration),
            "SealNewGeneration requires seal");
        Check(!MountIslandRequestEntryPolicy.RequiresSeal(MountIslandRequestEntryAction.ContinueAndMarkEntry1),
            "ContinueAndMarkEntry1 never reseals");
        Check(!MountIslandRequestEntryPolicy.RequiresSeal(MountIslandRequestEntryAction.ResumeOnly),
            "ResumeOnly never reseals");
        Check(!MountIslandRequestEntryPolicy.RequiresSeal(MountIslandRequestEntryAction.Reject),
            "Reject never seals");
    }

    private static void TrackerBounded()
    {
        var tracker = new MountIslandRequestEntryTracker();
        Check(tracker.Generation == -1 && !tracker.Entry1Seen, "tracker starts empty");
        Check(!tracker.Entry1SeenFor(0) && !tracker.Entry1SeenFor(-1), "empty tracker has no seen entry");

        tracker.MarkEntry1(5);
        Check(tracker.Generation == 5 && tracker.Entry1Seen, "tracker marks the issued generation");
        Check(tracker.Entry1SeenFor(5), "seen query matches the single tracked generation");
        Check(!tracker.Entry1SeenFor(4) && !tracker.Entry1SeenFor(6) && !tracker.Entry1SeenFor(-1),
            "seen query never leaks to another generation");

        tracker.ObserveGeneration(6);
        Check(tracker.Generation == 6 && !tracker.Entry1SeenFor(6),
            "a generation bump clears the entry-seen bit");
        tracker.MarkEntry1(6);
        tracker.ObserveGeneration(6);
        Check(tracker.Entry1SeenFor(6), "observing the same generation preserves the seen bit");

        tracker.MarkEntry1(-1);
        Check(tracker.Generation == -1 && !tracker.Entry1Seen, "negative generation never marks a seen entry");

        tracker.MarkEntry1(7);
        tracker.Clear();
        Check(tracker.Generation == -1 && !tracker.Entry1Seen && !tracker.Entry1SeenFor(7),
            "clear removes the single tracked generation and seen bit");
    }

    private static void ZeroToOneSealsOnce()
    {
        var gate = new MountIslandFreshGate();
        var tracker = new MountIslandRequestEntryTracker();

        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(0, gate.State))
            == MountIslandRequestEntryAction.SealInitial, "0->1: pc0 decision seals once");
        Check(gate.TrySealEnvelope(Envelope()), "0->1: pc0 seal accepted");
        long issued = gate.Generation;
        tracker.ObserveGeneration(issued);
        Check(issued == 1, "0->1: first seal issues generation 1");

        MountIslandRequestEntryAction pc1 = MountIslandRequestEntryPolicy.Decide(
            EntryInput(1, gate.State, entrySeen: tracker.Entry1SeenFor(gate.Generation)));
        Check(pc1 == MountIslandRequestEntryAction.ContinueAndMarkEntry1,
            "0->1: first pc1 reuses the sealed pre-image");
        tracker.MarkEntry1(issued);
        Check(gate.Generation == issued, "0->1: continuation does not seal a second time");
        Check(gate.State == MountIslandEnvelopeState.Started, "0->1: gate remains Started");

        MountIslandRequestEntryAction pc2 = MountIslandRequestEntryPolicy.Decide(
            EntryInput(2, gate.State, entrySeen: tracker.Entry1SeenFor(gate.Generation)));
        Check(pc2 == MountIslandRequestEntryAction.ResumeOnly, "0->1->2: pc2 resumes only");
        Check(gate.Generation == issued, "0->1->2: resume does not seal");
    }

    private static void LegitReusePublishesOnce()
    {
        var gate = new MountIslandFreshGate();
        var tracker = new MountIslandRequestEntryTracker();
        Check(gate.TrySealEnvelope(Envelope()), "reuse: state0 seal accepted");
        long issued = gate.Generation;
        tracker.ObserveGeneration(issued);

        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(1, gate.State,
                entrySeen: tracker.Entry1SeenFor(gate.Generation)))
            == MountIslandRequestEntryAction.ContinueAndMarkEntry1, "reuse: data entry continues current request");
        tracker.MarkEntry1(issued);
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(2, gate.State,
                entrySeen: tracker.Entry1SeenFor(gate.Generation)))
            == MountIslandRequestEntryAction.ResumeOnly, "reuse: storage resume does not seal");

        MountIslandAdvanceInput terminalInput = AdvanceInput(preState: 2, result: false, postState: -1);
        Check(MountIslandAdvanceClassifier.Classify(terminalInput) == MountIslandAdvanceOutcome.Terminal,
            "reuse: 1->2->false/-1 is a terminal attempt");
        MountIslandRouteFence fence = EvaluateFence(gate, gate.Envelope.RoutinePointer,
            gate.Envelope.RequestCampaignIndex, gate.Envelope.RequestChallengeId, gate.Envelope.RequestLand,
            issued);
        Check(fence == MountIslandRouteFence.OwnCurrent, "reuse: terminal fences OwnCurrent");

        var reader = new CountingReader { Value = 0x410 };
        Check(MountIslandTerminalReadGate.TryReadFlags(MountIslandAdvanceOutcome.Terminal, fence,
                snapshotReadOk: true, entryAuthorized: true, reader, out int flags, out string reason),
            "reuse: authorized terminal reads native flags");
        Check(reader.Calls == 1 && flags == 0x410 && reason == "ok",
            "reuse: reader called exactly once with raw 0x410");
        Check(MountIslandCompletionClassifier.Classify(Completion(flags, preState: 2))
            == MountIslandCompletionClass.MissingData, "reuse: raw flags classify MissingData");

        Check(gate.TryPublish(Draft()), "reuse: first MissingData publish accepted");
        Check(!gate.TryPublish(Draft()), "reuse: same envelope cannot publish twice");
        Check(gate.State == MountIslandEnvelopeState.CompletedOnce, "reuse: state is CompletedOnce");
    }

    private static void CachedSuccessRevokesOwn()
    {
        var gate = new MountIslandFreshGate();
        Check(gate.TrySealEnvelope(Envelope()), "cached success: state0 seal");
        long generation = gate.Generation;
        var tracker = new MountIslandRequestEntryTracker();
        tracker.ObserveGeneration(generation);
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(1, gate.State,
                entrySeen: tracker.Entry1SeenFor(gate.Generation)))
            == MountIslandRequestEntryAction.ContinueAndMarkEntry1, "cached success: pc1 continues");
        tracker.MarkEntry1(generation);

        MountIslandAdvanceInput terminalInput = AdvanceInput(preState: 1, result: false, postState: -1);
        Check(MountIslandAdvanceClassifier.Classify(terminalInput) == MountIslandAdvanceOutcome.Terminal,
            "cached success: pc1 false/-1 is Terminal");
        MountIslandRouteFence fence = EvaluateFence(gate, 0x9000, 0, 0, 11, generation);
        Check(fence == MountIslandRouteFence.OwnCurrent, "cached success: fence OwnCurrent");

        var reader = new CountingReader { Value = 0x50 };
        Check(MountIslandTerminalReadGate.TryReadFlags(MountIslandAdvanceOutcome.Terminal, fence,
                snapshotReadOk: true, entryAuthorized: true, reader, out int flags, out _),
            "cached success: authorized reader");
        Check(MountIslandCompletionClassifier.Classify(Completion(flags)) == MountIslandCompletionClass.Success,
            "cached success: 0x50 classifies Success");
        Check(!gate.TryPublish(Draft(flags: flags)), "cached success: Success never publishes a fresh candidate");

        MountIslandAdapterRouteFence.ApplyRevocation(gate, fence);
        Check(gate.State == MountIslandEnvelopeState.Closed,
            "cached success: own success terminal revokes its own scope");
    }

    private static void RestartNewGeneration()
    {
        var gate = new MountIslandFreshGate();
        var tracker = new MountIslandRequestEntryTracker();
        Check(gate.TrySealEnvelope(Envelope()), "restart: first seal");
        long first = gate.Generation;
        tracker.ObserveGeneration(first);
        tracker.MarkEntry1(first);

        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(1, gate.State,
                entrySeen: tracker.Entry1SeenFor(gate.Generation)))
            == MountIslandRequestEntryAction.SealNewGeneration,
            "restart: same pointer/args after entry seen is a new data request");
        Check(gate.TrySealEnvelope(Envelope()), "restart: same pointer/args new seal accepted");
        long second = gate.Generation;
        Check(second > first, "restart: new seal issues a new generation");

        var pendingGate = new MountIslandFreshGate();
        Check(pendingGate.TrySealEnvelope(Envelope()), "pending restart: first seal");
        long pendingFirst = pendingGate.Generation;
        Check(pendingGate.TryPublish(Draft()), "pending restart: old request publishes a pending candidate");
        Check(pendingGate.HasPending, "pending restart: pending candidate present");

        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(1, MountIslandEnvelopeState.CompletedOnce))
            == MountIslandRequestEntryAction.SealNewGeneration,
            "pending restart: pc1 after CompletedOnce starts a new generation");
        Check(pendingGate.TrySealEnvelope(Envelope()), "pending restart: new generation seal accepted");
        Check(!pendingGate.HasPending, "pending restart: old pending candidate invalidated");
        Check(pendingGate.State == MountIslandEnvelopeState.Started && pendingGate.Generation > pendingFirst,
            "pending restart: gate is a fresh Started generation");
    }

    private static void StaleAttemptReaderZero()
    {
        var gate = new MountIslandFreshGate();
        Check(gate.TrySealEnvelope(Envelope()), "stale: old request sealed");
        long oldGeneration = gate.Generation;
        Check(gate.TrySealEnvelope(Envelope()), "stale: new request sealed with same pointer/args");
        long newGeneration = gate.Generation;
        Check(newGeneration == oldGeneration + 1, "stale: new seal is N+1");

        MountIslandAdvanceInput terminalInput = AdvanceInput(preState: 1, result: false, postState: -1);
        Check(MountIslandAdvanceClassifier.Classify(terminalInput) == MountIslandAdvanceOutcome.Terminal,
            "stale: same-routine/request terminal shape is Terminal");

        MountIslandRouteFence staleFence = EvaluateFence(gate, 0x9000, 0, 0, 11, oldGeneration);
        Check(staleFence == MountIslandRouteFence.Stale,
            "stale: old attempt generation N versus live N+1 fences Stale");

        var staleReader = new CountingReader { Value = 0x410 };
        Check(!MountIslandTerminalReadGate.TryReadFlags(MountIslandAdvanceOutcome.Terminal, staleFence,
                snapshotReadOk: true, entryAuthorized: true, staleReader, out _, out _),
            "stale: terminal read rejected before native flags");
        Check(staleReader.Calls == 0, "stale: native flags reader call count is zero");

        MountIslandAdapterRouteFence.ApplyRevocation(gate, staleFence);
        Check(gate.State == MountIslandEnvelopeState.Started && gate.Generation == newGeneration,
            "stale: new current request is preserved");
        Check(gate.TryPublish(Draft()), "stale: new current request still publishable");

        var ownGate = new MountIslandFreshGate();
        ownGate.TrySealEnvelope(Envelope());
        long ownGeneration = ownGate.Generation;
        MountIslandRouteFence ownFence = EvaluateFence(ownGate, 0x9000, 0, 0, 11, ownGeneration);
        Check(ownFence == MountIslandRouteFence.OwnCurrent, "own: current attempt fences OwnCurrent");
        var ownReader = new CountingReader { Value = 72 };
        Check(MountIslandTerminalReadGate.TryReadFlags(MountIslandAdvanceOutcome.Terminal, ownFence,
                snapshotReadOk: true, entryAuthorized: true, ownReader, out int ownFlags, out _),
            "own: authorized terminal reads flags");
        Check(ownReader.Calls == 1 && ownFlags == 72, "own: reader called once with raw value");

        Check(!MountIslandTerminalReadGate.IsAuthorized(MountIslandAdvanceOutcome.Terminal,
            MountIslandRouteFence.Mutated, snapshotReadOk: true, entryAuthorized: true),
            "mutated request is never terminal-read authorized");
        Check(!MountIslandTerminalReadGate.IsAuthorized(MountIslandAdvanceOutcome.Terminal,
            MountIslandRouteFence.Foreign, snapshotReadOk: true, entryAuthorized: true),
            "foreign request is never terminal-read authorized");
        Check(!MountIslandTerminalReadGate.IsAuthorized(MountIslandAdvanceOutcome.Terminal,
            MountIslandRouteFence.Unfenced, snapshotReadOk: true, entryAuthorized: true),
            "unfenced request is never terminal-read authorized");
        Check(!MountIslandTerminalReadGate.IsAuthorized(MountIslandAdvanceOutcome.Terminal,
            MountIslandRouteFence.NoEnvelope, snapshotReadOk: true, entryAuthorized: true),
            "no envelope is never terminal-read authorized");
        Check(!MountIslandTerminalReadGate.IsAuthorized(MountIslandAdvanceOutcome.Terminal,
            MountIslandRouteFence.OwnCurrent, snapshotReadOk: false, entryAuthorized: true),
            "unreadable snapshot is never terminal-read authorized");
        Check(!MountIslandTerminalReadGate.IsAuthorized(MountIslandAdvanceOutcome.Terminal,
            MountIslandRouteFence.OwnCurrent, snapshotReadOk: true, entryAuthorized: false),
            "rejected entry is never terminal-read authorized even when the finalizer sees an own fence");
        Check(!MountIslandTerminalReadGate.IsAuthorized(MountIslandAdvanceOutcome.Continue,
            MountIslandRouteFence.OwnCurrent, snapshotReadOk: true, entryAuthorized: true),
            "normal yield is never terminal-read authorized");
        Check(!MountIslandTerminalReadGate.IsAuthorized(MountIslandAdvanceOutcome.Abort,
            MountIslandRouteFence.OwnCurrent, snapshotReadOk: true, entryAuthorized: true),
            "abort is never terminal-read authorized");
        Check(!MountIslandTerminalReadGate.IsAuthorized(MountIslandAdvanceOutcome.Ignore,
            MountIslandRouteFence.OwnCurrent, snapshotReadOk: true, entryAuthorized: true),
            "closed repeat is never terminal-read authorized");

        var rejectedReader = new CountingReader { Value = 0x410 };
        Check(!MountIslandTerminalReadGate.TryReadFlags(MountIslandAdvanceOutcome.Terminal, ownFence,
                snapshotReadOk: true, entryAuthorized: false, rejectedReader, out _, out _),
            "rejected entry: terminal read rejected");
        Check(rejectedReader.Calls == 0, "rejected entry: native flags reader call count is zero");
    }

    private static void ForeignUnreadableSkippedThrowClosed()
    {
        var foreignGate = new MountIslandFreshGate();
        foreignGate.TrySealEnvelope(Envelope(routine: 0x9000));
        long foreignGeneration = foreignGate.Generation;
        MountIslandRouteFence foreign = EvaluateFence(foreignGate, 0x200, 0, 0, 11, foreignGeneration);
        Check(foreign == MountIslandRouteFence.Foreign, "foreign: different routine fences Foreign");
        MountIslandAdapterRouteFence.ApplyRevocation(foreignGate, foreign);
        Check(foreignGate.State == MountIslandEnvelopeState.Started,
            "foreign: failure does not revoke the current request");

        var unfencedGate = new MountIslandFreshGate();
        unfencedGate.TrySealEnvelope(Envelope());
        long unfencedGeneration = unfencedGate.Generation;
        MountIslandRouteFence unfenced = EvaluateFence(unfencedGate, 0x9000, 0, 0, 11,
            unfencedGeneration, snapshotOk: false);
        Check(unfenced == MountIslandRouteFence.Unfenced, "unreadable: missing snapshot fences Unfenced");
        MountIslandAdapterRouteFence.ApplyRevocation(unfencedGate, unfenced);
        Check(unfencedGate.State == MountIslandEnvelopeState.Started,
            "unreadable: failure does not revoke the current request");

        var skippedInput = AdvanceInput(preState: 1, result: false, postState: -1, runOriginal: false);
        Check(MountIslandAdvanceClassifier.Classify(skippedInput) == MountIslandAdvanceOutcome.Abort,
            "skipped original classifies Abort");
        Check(!MountIslandTerminalReadGate.IsAuthorized(MountIslandAdvanceOutcome.Abort,
            MountIslandRouteFence.OwnCurrent, snapshotReadOk: true, entryAuthorized: true),
            "skipped original never reads flags");
        var skippedForeignGate = new MountIslandFreshGate();
        skippedForeignGate.TrySealEnvelope(Envelope());
        MountIslandRouteFence skippedForeign = EvaluateFence(skippedForeignGate, 0x200, 0, 0, 11,
            skippedForeignGate.Generation);
        Check(skippedForeign == MountIslandRouteFence.Foreign, "skipped foreign attempt fences Foreign");
        MountIslandAdapterRouteFence.ApplyRevocation(skippedForeignGate, skippedForeign);
        Check(skippedForeignGate.State == MountIslandEnvelopeState.Started,
            "skipped foreign attempt preserves current");

        var throwGate = new MountIslandFreshGate();
        throwGate.TrySealEnvelope(Envelope());
        long throwGeneration = throwGate.Generation;
        MountIslandRouteFence throwOwn = EvaluateFence(throwGate, 0x9000, 0, 0, 11, throwGeneration);
        Check(throwOwn == MountIslandRouteFence.OwnCurrent, "throwing own attempt fences OwnCurrent");
        MountIslandAdapterRouteFence.ApplyRevocation(throwGate, throwOwn);
        Check(throwGate.State == MountIslandEnvelopeState.Closed,
            "throwing own attempt revokes its own request");

        var throwForeignGate = new MountIslandFreshGate();
        throwForeignGate.TrySealEnvelope(Envelope());
        MountIslandRouteFence throwForeign = EvaluateFence(throwForeignGate, 0x200, 0, 0, 11,
            throwForeignGate.Generation);
        MountIslandAdapterRouteFence.ApplyRevocation(throwForeignGate, throwForeign);
        Check(throwForeign == MountIslandRouteFence.Foreign
            && throwForeignGate.State == MountIslandEnvelopeState.Started,
            "throwing foreign attempt does not destroy the new request");

        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(-1)) == MountIslandRequestEntryAction.Reject,
            "closed prestate entry action is Reject");
        Check(MountIslandAdvanceClassifier.Classify(AdvanceInput(preState: -1, result: false, postState: -1))
            == MountIslandAdvanceOutcome.Ignore, "closed prestate advance is Ignore");
        var closedGate = new MountIslandFreshGate();
        closedGate.TrySealEnvelope(Envelope());
        long closedGeneration = closedGate.Generation;
        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(-1)) == MountIslandRequestEntryAction.Reject,
            "closed prestate policy action is Reject");
        Check(closedGate.Generation == closedGeneration && closedGate.State == MountIslandEnvelopeState.Started,
            "closed prestate policy never touches the gate");
    }

    private static void Pc0FailurePc1Rebuild()
    {
        var gate = new MountIslandFreshGate();
        long before = gate.Generation;
        Check(!gate.TrySealEnvelope(Envelope(preReadsOk: false)),
            "pc0 failure: unreadable pre-image seal rejected");
        Check(gate.State == MountIslandEnvelopeState.Closed && gate.Generation == before + 1,
            "pc0 failure: failed seal closes and bumps generation");

        Check(MountIslandRequestEntryPolicy.Decide(EntryInput(1, gate.State, entrySeen: false))
            == MountIslandRequestEntryAction.SealNewGeneration,
            "pc0 failure: pc1 is allowed to re-seal");
        Check(gate.TrySealEnvelope(Envelope()), "pc0 failure: pc1 new seal succeeds");
        Check(gate.State == MountIslandEnvelopeState.Started && gate.Generation > before + 1,
            "pc0 failure: pc1 rebuild is a fresh Started generation");

        var ownFailureGate = new MountIslandFreshGate();
        ownFailureGate.TrySealEnvelope(Envelope());
        long ownGeneration = ownFailureGate.Generation;
        MountIslandRouteFence ownFence = EvaluateFence(ownFailureGate, 0x9000, 0, 0, 11, ownGeneration);
        Check(ownFence == MountIslandRouteFence.OwnCurrent, "rebuild: own failure fences OwnCurrent");
        MountIslandAdapterRouteFence.ApplyRevocation(ownFailureGate, ownFence);
        Check(ownFailureGate.State == MountIslandEnvelopeState.Closed, "rebuild: own failure revokes own scope");
        Check(ownFailureGate.TrySealEnvelope(Envelope()), "rebuild: a later request can seal again");

        var foreignFailureGate = new MountIslandFreshGate();
        foreignFailureGate.TrySealEnvelope(Envelope());
        long foreignGeneration = foreignFailureGate.Generation;
        MountIslandRouteFence foreignFence = EvaluateFence(foreignFailureGate, 0x200, 0, 0, 11, foreignGeneration);
        MountIslandAdapterRouteFence.ApplyRevocation(foreignFailureGate, foreignFence);
        Check(foreignFence == MountIslandRouteFence.Foreign
            && foreignFailureGate.State == MountIslandEnvelopeState.Started,
            "rebuild: foreign failure does not revoke the new current");
    }

    private static void OldEmptyStillRejected()
    {
        var gate = new MountIslandFreshGate();
        Check(gate.TrySealEnvelope(Envelope(prePositive: true)),
            "old-empty: a positive-history request can still be observed");
        Check(!gate.TryPublish(Draft()), "old-empty: positive pre-image never publishes fresh");
        Check(gate.State == MountIslandEnvelopeState.Started && !gate.HasPending,
            "old-empty: rejected publish leaves no pending candidate");

        Check(!gate.TrySealEnvelope(Envelope(preReadsOk: false)),
            "old-empty: unreadable pre-image cannot seal");
        Check(!gate.TryPublish(Draft()), "old-empty: unsealed gate never publishes");
    }

    private static void NormalYieldKeepsStarted()
    {
        var gate = new MountIslandFreshGate();
        gate.TrySealEnvelope(Envelope());
        long generation = gate.Generation;
        var tracker = new MountIslandRequestEntryTracker();
        tracker.ObserveGeneration(generation);
        tracker.MarkEntry1(generation);

        MountIslandAdvanceInput yield1 = AdvanceInput(preState: 1, result: true, postState: 2);
        Check(MountIslandAdvanceClassifier.Classify(yield1) == MountIslandAdvanceOutcome.Continue,
            "yield: 1->2 is Continue");
        Check(!MountIslandTerminalReadGate.IsAuthorized(MountIslandAdvanceOutcome.Continue,
            MountIslandRouteFence.OwnCurrent, snapshotReadOk: true, entryAuthorized: true),
            "yield: 1->2 does not read flags");
        Check(gate.State == MountIslandEnvelopeState.Started && !gate.HasPending
            && gate.Generation == generation, "yield: 1->2 keeps Started and publishes nothing");

        MountIslandAdvanceInput yield2 = AdvanceInput(preState: 2, result: true, postState: 2);
        Check(MountIslandAdvanceClassifier.Classify(yield2) == MountIslandAdvanceOutcome.Continue,
            "yield: 2->2 is Continue");
        Check(gate.State == MountIslandEnvelopeState.Started && !gate.HasPending,
            "yield: 2->2 keeps Started and publishes nothing");
    }

    private static MountIslandNativeResultShape ValidShape()
    {
        return new MountIslandNativeResultShape
        {
            RoutineClassPresent = true,
            RoutineClassReferenceType = true,
            ReturnFieldPresent = true,
            ReturnFieldParentExact = true,
            ReturnFieldNonStatic = true,
            ReturnFieldTypeNotByRefOrPointer = true,
            ReturnFieldByRefFlagFree = true,
            ReturnClassExact = true,
            ReturnClassValueType = true,
            ReturnValueSize = 4,
            ReturnAlignment = 4,
            EnumClassPresent = true,
            EnumIsEnum = true,
            EnumValueType = true,
            EnumBaseTypeIsInt32 = true,
            EnumBaseTypeKindNotByRefOrPointer = true,
            EnumBaseTypeByRefFlagFree = true,
            EnumValueSize = 4,
            EnumAlignment = 4,
            NonStaticFieldCount = 1,
            ValueFieldPresent = true,
            ValueFieldNamedValue = true,
            ValueFieldTypeNotByRefOrPointer = true,
            ValueFieldByRefFlagFree = true,
            ValueFieldClassExactEnum = true,
        };
    }

    private static void CheckInvalid(MountIslandNativeResultShape valid, string label,
        Func<MountIslandNativeResultShape, MountIslandNativeResultShape> mutate)
    {
        Check(!MountIslandNativeResultShapePolicy.IsValid(mutate(valid)), label);
    }

    private static void ShapePolicyBattery()
    {
        MountIslandNativeResultShape valid = ValidShape();
        Check(MountIslandNativeResultShapePolicy.IsValid(valid), "shape: fully valid metadata accepted");

        CheckInvalid(valid, "shape: missing routine class", s => { s.RoutineClassPresent = false; return s; });
        CheckInvalid(valid, "shape: routine class is a value type", s => { s.RoutineClassReferenceType = false; return s; });
        CheckInvalid(valid, "shape: return field missing", s => { s.ReturnFieldPresent = false; return s; });
        CheckInvalid(valid, "shape: return field declared on a parent", s => { s.ReturnFieldParentExact = false; return s; });
        CheckInvalid(valid, "shape: return field static", s => { s.ReturnFieldNonStatic = false; return s; });
        CheckInvalid(valid, "shape: return field byref/pointer kind", s => { s.ReturnFieldTypeNotByRefOrPointer = false; return s; });
        CheckInvalid(valid, "shape: return field independent byref flag", s => { s.ReturnFieldByRefFlagFree = false; return s; });
        CheckInvalid(valid, "shape: return field class not exact", s => { s.ReturnClassExact = false; return s; });
        CheckInvalid(valid, "shape: return class not valuetype", s => { s.ReturnClassValueType = false; return s; });
        CheckInvalid(valid, "shape: return size not 4", s => { s.ReturnValueSize = 8; return s; });
        CheckInvalid(valid, "shape: return size zero", s => { s.ReturnValueSize = 0; return s; });
        CheckInvalid(valid, "shape: return alignment zero", s => { s.ReturnAlignment = 0; return s; });
        CheckInvalid(valid, "shape: return alignment does not divide size", s => { s.ReturnAlignment = 3; return s; });
        CheckInvalid(valid, "shape: return alignment too large", s => { s.ReturnAlignment = 8; return s; });
        CheckInvalid(valid, "shape: enum class missing", s => { s.EnumClassPresent = false; return s; });
        CheckInvalid(valid, "shape: enum flag false", s => { s.EnumIsEnum = false; return s; });
        CheckInvalid(valid, "shape: enum not valuetype", s => { s.EnumValueType = false; return s; });
        CheckInvalid(valid, "shape: enum base type not Int32", s => { s.EnumBaseTypeIsInt32 = false; return s; });
        CheckInvalid(valid, "shape: enum base type byref/pointer kind", s => { s.EnumBaseTypeKindNotByRefOrPointer = false; return s; });
        CheckInvalid(valid, "shape: enum base type independent byref flag", s => { s.EnumBaseTypeByRefFlagFree = false; return s; });
        CheckInvalid(valid, "shape: enum size not 4", s => { s.EnumValueSize = 8; return s; });
        CheckInvalid(valid, "shape: enum size zero", s => { s.EnumValueSize = 0; return s; });
        CheckInvalid(valid, "shape: enum alignment zero", s => { s.EnumAlignment = 0; return s; });
        CheckInvalid(valid, "shape: enum alignment too large", s => { s.EnumAlignment = 8; return s; });
        CheckInvalid(valid, "shape: no non-static return field", s => { s.NonStaticFieldCount = 0; return s; });
        CheckInvalid(valid, "shape: more than one non-static return field", s => { s.NonStaticFieldCount = 2; return s; });
        CheckInvalid(valid, "shape: value field missing", s => { s.ValueFieldPresent = false; return s; });
        CheckInvalid(valid, "shape: value field not named value", s => { s.ValueFieldNamedValue = false; return s; });
        CheckInvalid(valid, "shape: value field byref/pointer kind", s => { s.ValueFieldTypeNotByRefOrPointer = false; return s; });
        CheckInvalid(valid, "shape: value field independent byref flag", s => { s.ValueFieldByRefFlagFree = false; return s; });
        CheckInvalid(valid, "shape: value field class not exact enum", s => { s.ValueFieldClassExactEnum = false; return s; });

        Check(MountIslandNativeResultShapePolicy.IsLegalAlignment(1), "shape: alignment 1 legal");
        Check(MountIslandNativeResultShapePolicy.IsLegalAlignment(2), "shape: alignment 2 legal");
        Check(MountIslandNativeResultShapePolicy.IsLegalAlignment(4), "shape: alignment 4 legal");
        Check(!MountIslandNativeResultShapePolicy.IsLegalAlignment(0), "shape: alignment 0 illegal");
        Check(!MountIslandNativeResultShapePolicy.IsLegalAlignment(3), "shape: alignment 3 illegal");
        Check(!MountIslandNativeResultShapePolicy.IsLegalAlignment(8), "shape: alignment 8 illegal");
    }
}
