using System;
using KingdomEnhancedMod;

internal static class Program
{
    private static int _failed;
    private static int _passed;

    private static void Test(string name, Action body)
    {
        try { body(); Console.WriteLine("PASS " + name); _passed++; }
        catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e.Message); _failed++; }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    private static MountIslandOwnerIdentity Owner(int land, ulong campaign = 0xC0, ulong global = 0x61,
        int index = 0, int reign = 0, ulong landData = 0x1DA7A, ulong islands = 0x151A,
        ulong holder = 0x401DE2, int biome = 5)
    {
        return new MountIslandOwnerIdentity
        {
            GlobalPointer = global, SelectedCampaignIndex = index, CampaignPointer = campaign,
            ReignIndex = reign, LandDataPointer = landData, IslandsPointer = islands,
            HolderInstancePointer = holder, HolderBiomeIndex = biome, CurrentLand = land,
        };
    }

    private static MountIslandHistoryView History(bool readsOk = true, bool any = false, ulong slot = 0x111,
        bool nonMap = false, double mapDays = 0d)
        => MountIslandHistoryView.CreateObserved(readsOk, any, slot != 0UL, slot, nonMap, mapDays);

    private static void Main()
    {
        Test("PC0 always seals initial", () =>
        {
            var input = new MountIslandRequestEntryInput { PreState = 0 };
            Check(MountIslandRequestEntryPolicy.Decide(input) == MountIslandRequestEntryAction.SealInitial, "PC0");
        });

        Test("PC1 started same request continues once", () =>
        {
            var input = new MountIslandRequestEntryInput
            {
                PreState = 1, EnvelopeState = MountIslandEnvelopeState.Started,
                SameRoutine = true, SameRequest = true, Entry1SeenForCurrentGeneration = false,
            };
            Check(MountIslandRequestEntryPolicy.Decide(input) == MountIslandRequestEntryAction.ContinueAndMarkEntry1, "continue");
            input.Entry1SeenForCurrentGeneration = true;
            Check(MountIslandRequestEntryPolicy.Decide(input) == MountIslandRequestEntryAction.SealNewGeneration, "restart");
        });

        Test("PC1 mismatches reseal, never resume", () =>
        {
            var input = new MountIslandRequestEntryInput
            {
                PreState = 1, EnvelopeState = MountIslandEnvelopeState.Started,
                SameRoutine = false, SameRequest = false,
            };
            Check(MountIslandRequestEntryPolicy.Decide(input) == MountIslandRequestEntryAction.SealNewGeneration, "mismatch");
        });

        Test("PC2 resumes only an already-entered current request", () =>
        {
            var input = new MountIslandRequestEntryInput
            {
                PreState = 2, EnvelopeState = MountIslandEnvelopeState.Started,
                SameRoutine = true, SameRequest = true, Entry1SeenForCurrentGeneration = true,
            };
            Check(MountIslandRequestEntryPolicy.Decide(input) == MountIslandRequestEntryAction.ResumeOnly, "resume");
            input.Entry1SeenForCurrentGeneration = false;
            Check(MountIslandRequestEntryPolicy.Decide(input) == MountIslandRequestEntryAction.Reject, "first PC2");
            input.Entry1SeenForCurrentGeneration = true;
            input.EnvelopeState = MountIslandEnvelopeState.Transferred;
            Check(MountIslandRequestEntryPolicy.Decide(input) == MountIslandRequestEntryAction.Reject, "transferred PC2");
        });

        Test("closed/unknown preState rejects", () =>
        {
            Check(MountIslandRequestEntryPolicy.Decide(new MountIslandRequestEntryInput { PreState = -1 })
                == MountIslandRequestEntryAction.Reject, "closed");
            Check(MountIslandRequestEntryPolicy.Decide(new MountIslandRequestEntryInput { PreState = 7 })
                == MountIslandRequestEntryAction.Reject, "unknown");
        });

        Test("entry tracker clears on generation change, keeps same generation", () =>
        {
            var tracker = new MountIslandRequestEntryTracker();
            tracker.ObserveGeneration(3);
            tracker.MarkEntry1(3);
            Check(tracker.Entry1SeenFor(3), "marked");
            tracker.ObserveGeneration(3);
            Check(tracker.Entry1SeenFor(3), "same generation kept");
            tracker.ObserveGeneration(4);
            Check(!tracker.Entry1SeenFor(4) && !tracker.Entry1SeenFor(3), "generation change cleared");
            tracker.MarkEntry1(-1);
            Check(tracker.Generation == -1 && !tracker.Entry1Seen, "negative clear");
        });

        Test("terminal read gate requires terminal + own current + snapshot + entry", () =>
        {
            Check(MountIslandTerminalReadGate.IsAuthorized(MountIslandAdvanceOutcome.Terminal,
                MountIslandRouteFence.OwnCurrent, true, true), "authorized");
            Check(!MountIslandTerminalReadGate.IsAuthorized(MountIslandAdvanceOutcome.Terminal,
                MountIslandRouteFence.Stale, true, true), "stale");
            Check(!MountIslandTerminalReadGate.IsAuthorized(MountIslandAdvanceOutcome.Terminal,
                MountIslandRouteFence.OwnCurrent, false, true), "snapshot");
            Check(!MountIslandTerminalReadGate.IsAuthorized(MountIslandAdvanceOutcome.Terminal,
                MountIslandRouteFence.OwnCurrent, true, false), "entry");
            Check(!MountIslandTerminalReadGate.IsAuthorized(MountIslandAdvanceOutcome.Continue,
                MountIslandRouteFence.OwnCurrent, true, true), "not terminal");
        });

        Test("terminal flags reader refusal keeps flags zero and reason", () =>
        {
            var reader = new RefusingReader();
            bool ok = MountIslandTerminalReadGate.TryReadFlags(MountIslandAdvanceOutcome.Terminal,
                MountIslandRouteFence.OwnCurrent, true, true, reader, out int flags, out string reason);
            Check(!ok && flags == 0 && reason == "reader-refused", "refusal");
        });

        Test("native shape policy rejects every partial shape", () =>
        {
            var valid = ValidShape();
            Check(MountIslandNativeResultShapePolicy.IsValid(valid), "valid");
            var candidates = new[]
            {
                Mutate(valid, s => { s.RoutineClassPresent = false; return s; }),
                Mutate(valid, s => { s.ReturnFieldByRefFlagFree = false; return s; }),
                Mutate(valid, s => { s.ReturnValueSize = 8; return s; }),
                Mutate(valid, s => { s.ReturnAlignment = 3; return s; }),
                Mutate(valid, s => { s.EnumBaseTypeIsInt32 = false; return s; }),
                Mutate(valid, s => { s.NonStaticFieldCount = 2; return s; }),
                Mutate(valid, s => { s.ValueFieldClassExactEnum = false; return s; }),
            };
            foreach (var candidate in candidates)
                Check(!MountIslandNativeResultShapePolicy.IsValid(candidate), "partial accepted");
        });

        Test("departure capture requires one show, target extension land, source != target", () =>
        {
            var history = new MountIslandTransitionHistory();
            history.Begin("show");
            Check(history.Capture("show", "confirm", "token", Owner(8), 11, History()) != null, "valid capture");
            Check(history.Capture("other", "confirm", "token", Owner(8), 11, History()) == null, "foreign show");
            Check(history.Capture("show", "confirm", "token", Owner(11), 11, History()) == null, "same land");
            Check(history.Capture("show", "confirm", "token", Owner(8), 12, History()) == null, "non extension land");
        });

        Test("publish rejects dirty preimage and consumes epoch", () =>
        {
            var history = new MountIslandTransitionHistory();
            history.Begin("show");
            var dirty = history.Capture("show", "confirm", "token", Owner(8), 11, History(any: true));
            Check(dirty != null, "capture allows caller to pass raw history");
            Check(!history.Publish(dirty, "confirm", "token", Owner(8), true), "dirty published");
            Check(history.TakePending() == null, "dirty became pending");
            Check(!history.Publish(dirty, "confirm", "token", Owner(8), true), "dirty republished");

            history.Begin("show");
            var clean = history.Capture("show", "confirm", "token", Owner(8), 11, History());
            Check(history.Publish(clean, "confirm", "token", Owner(8), true), "clean publish");
            Check(history.TakePending() == clean, "pending lost");
            Check(history.TakePending() == null, "pending taken twice");
        });

        Test("publish rejects identity/token drift and not-current completion", () =>
        {
            foreach (var mode in new[] { "owner", "token", "confirm", "notcurrent" })
            {
                var history = new MountIslandTransitionHistory();
                history.Begin("show");
                var pre = history.Capture("show", "confirm", "token", Owner(8), 11, History());
                if (mode == "owner") Owner(8, campaign: 0xC1); // drift source read below
                bool current = mode != "notcurrent";
                object token = mode == "token" ? "other" : "token";
                object confirm = mode == "confirm" ? "other" : "confirm";
                var source = mode == "owner" ? Owner(8, campaign: 0xC1) : Owner(8);
                Check(!history.Publish(pre, confirm, token, source, current), mode + " published");
                Check(history.TakePending() == null, mode + " pending");
            }
        });

        Test("new show cancels old unpublished departure", () =>
        {
            var history = new MountIslandTransitionHistory();
            history.Begin("old");
            var pre = history.Capture("old", "confirm", "token", Owner(8), 11, History());
            history.Begin("new");
            Check(!history.Publish(pre, "confirm", "token", Owner(8), true), "old show published");
            Check(history.TakePending() == null, "old pending");
        });

        Test("bind requires exact target identity, routine, generation and land", () =>
        {
            var history = new MountIslandTransitionHistory();
            history.Begin("show");
            var pre = history.Capture("show", "confirm", "token", Owner(8), 11, History());
            Check(history.Publish(pre, "confirm", "token", Owner(8), true), "publish");
            var taken = history.TakePending();
            Check(history.TryBindTaken(taken, Owner(11), 11, 0x900, 4, History(), out var proof), "valid bind");
            Check(proof != null && proof.Epoch == pre.Epoch && proof.SourceLand == 8, "proof metadata");
            Check(proof.Matches(Owner(11), 0x900, 4, 11), "proof match");
            Check(!proof.Matches(Owner(11), 0x900, 5, 11), "generation drift");
            Check(!proof.Matches(Owner(11), 0x901, 4, 11), "routine drift");
            Check(!proof.Matches(Owner(13), 0x900, 4, 11), "owner drift");
            Check(!proof.Matches(Owner(11), 0x900, 4, 13), "land drift");

            var history2 = new MountIslandTransitionHistory();
            history2.Begin("show");
            var pre2 = history2.Capture("show", "confirm", "token", Owner(8), 11, History());
            history2.Publish(pre2, "confirm", "token", Owner(8), true);
            var taken2 = history2.TakePending();
            Check(!history2.TryBindTaken(taken2, Owner(11, campaign: 0xC1), 11, 0x900, 4, History(), out _), "campaign drift");
            Check(!history2.TryBindTaken(taken2, Owner(11, reign: 1), 11, 0x900, 4, History(), out _), "reign drift");
            Check(!history2.TryBindTaken(taken2, Owner(11, holder: 0x401DE3), 11, 0x900, 4, History(), out _), "holder drift");
            Check(!history2.TryBindTaken(taken2, Owner(11), 11, 0, 4, History(), out _), "zero routine");
        });

        Test("proof AllowsHistory rejects non-map positives and slot drift", () =>
        {
            var history = new MountIslandTransitionHistory();
            history.Begin("show");
            var pre = history.Capture("show", "confirm", "token", Owner(8), 11, History(slot: 0x111));
            history.Publish(pre, "confirm", "token", Owner(8), true);
            var proof = Bind(history, Owner(11), 11, History(slot: 0x111));

            Check(proof.AllowsHistory(History(slot: 0x111, mapDays: 12.5)), "map-only positive rejected");
            Check(!proof.AllowsHistory(History(slot: 0x111, nonMap: true)), "non-map positive admitted");
            Check(!proof.AllowsHistory(History(slot: 0x111, readsOk: false)), "unreadable admitted");
            Check(!proof.AllowsHistory(History(slot: 0x222)), "slot drift admitted");
            Check(!proof.AllowsHistory(History(slot: 0x111, mapDays: double.NaN)), "NaN admitted");
            Check(!proof.AllowsHistory(History(slot: 0x111, mapDays: -1d)), "negative admitted");

            var history2 = new MountIslandTransitionHistory();
            history2.Begin("show");
            var pre2 = history2.Capture("show", "confirm", "token", Owner(8), 11, History(any: true, nonMap: true));
            history2.Publish(pre2, "confirm", "token", Owner(8), true);
            var taken = history2.TakePending();
            Check(!history2.TryBindTaken(taken, Owner(11), 11, 0x900, 4, History(), out _), "dirty preimage bound");
        });

        Test("zero pre slot allows first materialized slot", () =>
        {
            var history = new MountIslandTransitionHistory();
            history.Begin("show");
            var pre = history.Capture("show", "confirm", "token", Owner(8), 11, History(slot: 0));
            history.Publish(pre, "confirm", "token", Owner(8), true);
            var proof = Bind(history, Owner(11), 11, History(slot: 0x777));
            Check(proof.AllowsHistory(History(slot: 0x777)), "first slot rejected");
        });

        Test("DecideWithCurrentTransitionTime is fail-closed without a current proof", () =>
        {
            var state = new CrossWorldMountIslandState(
                landValid: true, markerOnIsland: false, markerInCampaign: false, islandVisited: false,
                receiptOnIsland: false, lastPlayedDays: 1d, grantedDefinitionCount: 0,
                receiptInCampaign: false, campaignEvidenceReadable: true);
            Check(CrossWorldMountPolicy.DecideWithCurrentTransitionTime(state, default)
                == CrossWorldMountDecision.Skip, "default admitted");
        });

        Console.WriteLine(_failed == 0 ? "ALL PASS (" + _passed + ")" : "FAILED " + _failed + "/" + (_passed + _failed));
        Environment.Exit(_failed == 0 ? 0 : 1);
    }

    private static MountIslandTransitionProof Bind(MountIslandTransitionHistory history,
        MountIslandOwnerIdentity target, int land, MountIslandHistoryView current)
    {
        var taken = history.TakePending();
        Check(history.TryBindTaken(taken, target, land, 0x900, 4, current, out var proof), "bind failed");
        return proof;
    }

    private sealed class RefusingReader : IMountIslandTerminalFlagsReader
    {
        public bool TryRead(out int flags, out string reason)
        {
            flags = 0; reason = "reader-refused"; return false;
        }
    }

    private static MountIslandNativeResultShape ValidShape()
    {
        return new MountIslandNativeResultShape
        {
            RoutineClassPresent = true, RoutineClassReferenceType = true,
            ReturnFieldPresent = true, ReturnFieldParentExact = true, ReturnFieldNonStatic = true,
            ReturnFieldTypeNotByRefOrPointer = true, ReturnFieldByRefFlagFree = true,
            ReturnClassExact = true, ReturnClassValueType = true, ReturnValueSize = 4, ReturnAlignment = 4,
            EnumClassPresent = true, EnumIsEnum = true, EnumValueType = true,
            EnumBaseTypeIsInt32 = true, EnumBaseTypeKindNotByRefOrPointer = true,
            EnumBaseTypeByRefFlagFree = true, EnumValueSize = 4, EnumAlignment = 4,
            NonStaticFieldCount = 1, ValueFieldPresent = true, ValueFieldNamedValue = true,
            ValueFieldTypeNotByRefOrPointer = true, ValueFieldByRefFlagFree = true,
            ValueFieldClassExactEnum = true,
        };
    }

    private static MountIslandNativeResultShape Mutate(MountIslandNativeResultShape source,
        Func<MountIslandNativeResultShape, MountIslandNativeResultShape> mutate)
        => mutate(source);
}
