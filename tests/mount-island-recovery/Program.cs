using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

namespace MountIslandRecoveryTests
{
    internal static class Program
    {
        private static int _failures;

        private static void Check(bool condition, string name)
        {
            if (condition) return;
            _failures++;
            Console.WriteLine("FAIL: " + name);
        }

        private static void Main()
        {
            TokenTests();
            ScopeTests();
            PolicyTests();
            PlacementTests();
            MarkerTests();
            TransactionTests();
            GeometryTests();
            Console.WriteLine(_failures == 0 ? "ALL PASS" : _failures + " FAILURE(S)");
            Environment.Exit(_failures == 0 ? 0 : 1);
        }

        // ------------------------------------------------------------------ token

        private static void TokenTests()
        {
            var slot = new MountIslandRecoveryTokenSlot();
            MountIslandRecoveryToken first = slot.Begin("first");
            Check(slot.PublishReady(first), "token: first ready");
            Check(first.Ready, "token: ready bit");

            MountIslandRecoveryToken second = slot.Begin("second");
            Check(!slot.PublishReady(first), "token: old token cannot publish after new prefix");
            Check(!slot.CancelThenCurrent(first), "token: stale cancel does not clear current");
            Check(ReferenceEquals(slot.Current, second), "token: second remains current");
            Check(slot.PublishReady(second), "token: second ready");
            Check(ReferenceEquals(slot.Take(), second), "token: take returns current");
            Check(slot.Current == null, "token: take clears slot");
            Check(slot.Take() == null, "token: second take empty");
            Check(!slot.PublishReady(second), "token: consumed token cannot republish");

            MountIslandRecoveryToken third = slot.Begin("third");
            slot.Cancel(third);
            Check(slot.Current == null, "token: cancel clears current");
        }

        private static bool CancelThenCurrent(this MountIslandRecoveryTokenSlot slot, MountIslandRecoveryToken stale)
        {
            slot.Cancel(stale);
            return slot.Current == null;
        }

        // ------------------------------------------------------------------ scope

        private static MountIslandRecoveryScope Scope(
            ulong global = 10UL, ulong campaign = 1UL, int selected = 0, int reign = 1, ulong landData = 2UL,
            ulong islands = 3UL, int land = 11, ulong level = 4UL, int scene = 5,
            string sceneName = "island", int seed = 7,
            ulong layer = 6UL, int layerScene = 5,
            ulong holder = 8UL, int biome = 1, ulong island = 9UL, int islandLand = 11)
        {
            return new MountIslandRecoveryScope(global, campaign, selected, reign, landData, islands, land,
                level, scene, sceneName, seed, layer, layerScene, holder, biome, island, islandLand);
        }

        private static void ScopeTests()
        {
            MountIslandRecoveryScope baseline = Scope();
            Check(MountIslandRecoveryScope.SameLoad(baseline, Scope()), "scope: identical load");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(global: 11)), "scope: Global.loaded drift");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(campaign: 10)), "scope: campaign drift");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(selected: 1)), "scope: slot drift");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(reign: 2)), "scope: reign drift");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(landData: 20)), "scope: landData drift");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(islands: 30)), "scope: islands drift");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(land: 13)), "scope: land drift");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(level: 40)), "scope: Level drift");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(scene: 50)), "scope: scene handle drift");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(sceneName: "other")), "scope: scene name drift");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(seed: 70)), "scope: seed drift");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(layer: 60)), "scope: GameLayer drift");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(layerScene: 55)), "scope: GameLayer scene drift");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(holder: 80)), "scope: holder drift");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(biome: 2)), "scope: biome drift");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(island: 90)), "scope: island pointer drift");
            Check(!MountIslandRecoveryScope.SameLoad(baseline, Scope(islandLand: 13)), "scope: island.land drift");
            Check(MountIslandRecoveryScope.ConsumedObjectsClear(true), "scope: native null objects accepted");
            Check(!MountIslandRecoveryScope.ConsumedObjectsClear(false), "scope: non-null objects rejected");
            Check(MountIslandRecoveryScope.SameLoadExceptSeed(Scope(seed: 0), Scope(seed: 1729)),
                "scope: seed excluded from identity");
            Check(!MountIslandRecoveryScope.SameLoad(Scope(seed: 0), Scope(seed: 1729)),
                "scope: full load rejects seed drift");
            Check(MountIslandRecoveryScope.IsAllowedSeedTransition(7, 1729, 1729),
                "seed: legal pre!=post==saved");
            Check(MountIslandRecoveryScope.IsAllowedSeedTransition(1729, 1729, 1729),
                "seed: pre==post==saved allowed");
            Check(!MountIslandRecoveryScope.IsAllowedSeedTransition(7, 7, 1729),
                "seed: pre==post!=saved rejected");
            Check(!MountIslandRecoveryScope.IsAllowedSeedTransition(7, 1730, 1729),
                "seed: wrong post rejected");
            Check(!MountIslandRecoveryScope.IsAllowedSeedTransition(1729, 7, 1729),
                "seed: late drift rejected");
            Check(MountIslandRecoverySeedData.TryParse(
                    "{\"season\":0.0,\"cachedSeed\":1729,\"nextSpawnPostfix\":1}", out int parsedSeed, out _)
                && parsedSeed == 1729,
                "seed data: cachedSeed parsed");
            Check(MountIslandRecoverySeedData.TryParse(
                    "{\"cachedSeed\": -1866790347 }", out parsedSeed, out _)
                && parsedSeed == -1866790347,
                "seed data: negative cachedSeed parsed");
            Check(!MountIslandRecoverySeedData.TryParse("{\"seed\":1729}", out _, out _),
                "seed data: legacy-only rejected");
            Check(!MountIslandRecoverySeedData.TryParse(
                    "{\"cachedSeed\":1729,\"seed\":1729}", out _, out _),
                "seed data: legacy plus cached rejected");
            Check(!MountIslandRecoverySeedData.TryParse(
                    "{\"cachedSeed\":1729,\"cachedSeed\":1730}", out _, out _),
                "seed data: duplicate top-level key rejected");
            Check(!MountIslandRecoverySeedData.TryParse(
                    "{\"nested\":{\"cachedSeed\":1729}}", out _, out _),
                "seed data: nested-only rejected");
            Check(!MountIslandRecoverySeedData.TryParse("{\"season\":0}", out _, out _),
                "seed data: top-level missing rejected");
            Check(!MountIslandRecoverySeedData.TryParse("{\"cachedSeed\":1.5}", out _, out _),
                "seed data: float rejected");
            Check(!MountIslandRecoverySeedData.TryParse("{\"cachedSeed\":2147483648}", out _, out _),
                "seed data: overflow rejected");
            Check(!MountIslandRecoverySeedData.TryParse("[]", out _, out _),
                "seed data: non-object root rejected");
            Check(!MountIslandRecoverySeedData.TryParse("cachedSeed:1729", out _, out _),
                "seed data: missing braces rejected");
            Check(!MountIslandRecoverySeedData.TryParse("{\"cachedSeed\":1729", out _, out _),
                "seed data: truncated rejected");

            Check(MountIslandRecoveryPolicy.IsOriginalCompletion(true, true, false),
                "original: normal completion");
            Check(!MountIslandRecoveryPolicy.IsOriginalCompletion(false, true, false),
                "original: skipped rejected");
            Check(!MountIslandRecoveryPolicy.IsOriginalCompletion(true, false, false),
                "original: false rejected");
            Check(!MountIslandRecoveryPolicy.IsOriginalCompletion(true, true, true),
                "original: throw rejected");

            for (int i = 0; i < MountIslandRecoveryGeometry.Facilities.Length; i++)
            {
                Check(MountIslandRecoveryPolicy.IsTemplateFacilityParentValid(true, 1, true),
                    "template parent: valid row " + MountIslandRecoveryGeometry.Facilities[i].FacilityName);
            }
            Check(!MountIslandRecoveryPolicy.IsTemplateFacilityParentValid(false, 1, true),
                "template parent: missing GameLayer rejected");
            Check(!MountIslandRecoveryPolicy.IsTemplateFacilityParentValid(true, 2, true),
                "template parent: duplicate GameLayer rejected");
            Check(!MountIslandRecoveryPolicy.IsTemplateFacilityParentValid(true, 1, false),
                "template parent: wrong/nested layer rejected");
        }

        // ------------------------------------------------------------------ policy

        private static MountIslandRecoveryEvidence Ev(
            string id = "d", int type = 1, bool target = true, bool marker = false,
            bool receipt = false, bool other = false,
            MountIslandRecoveryLiveState live = MountIslandRecoveryLiveState.Missing,
            bool complete = false, bool dependency = true, bool seasonal = true,
            bool readable = true, bool unknown = false)
        {
            return new MountIslandRecoveryEvidence(id, type, target, marker, receipt, other,
                live, complete, dependency, seasonal, readable, unknown);
        }

        private static MountIslandRecoveryDecision Decision(MountIslandRecoveryPlan plan, string id)
        {
            for (int i = 0; i < plan.Arms.Count; i++)
            {
                if (plan.Arms[i].DefinitionId == id) return plan.Arms[i].Decision;
            }
            return (MountIslandRecoveryDecision)(-1);
        }

        private static void PolicyTests()
        {
            MountIslandRecoveryPlan plan = MountIslandRecoveryPolicy.Build(new[]
            {
                Ev("live-marker", live: MountIslandRecoveryLiveState.Single, complete: true, marker: true),
                Ev("live-no-marker", live: MountIslandRecoveryLiveState.Single, complete: true),
                Ev("live-incomplete", live: MountIslandRecoveryLiveState.Single, complete: false),
                Ev("duplicate", live: MountIslandRecoveryLiveState.Duplicate),
                Ev("unknown", live: MountIslandRecoveryLiveState.Unknown),
                Ev("missing-marker", marker: true),
                Ev("missing-receipt", receipt: true),
                Ev("other-grant", other: true),
                Ev("receipt-unknown", unknown: true),
                Ev("unreadable", readable: false),
                Ev("wrong-land", target: false),
                Ev("dep", dependency: false),
                Ev("season", seasonal: false),
                Ev("create"),
            });
            Check(Decision(plan, "live-marker") == MountIslandRecoveryDecision.Skip, "policy: live+marker skip");
            Check(Decision(plan, "live-no-marker") == MountIslandRecoveryDecision.MarkerOnly, "policy: live marker-only");
            Check(Decision(plan, "live-incomplete") == MountIslandRecoveryDecision.Reject, "policy: incomplete live reject");
            Check(Decision(plan, "duplicate") == MountIslandRecoveryDecision.Reject, "policy: duplicate reject");
            Check(Decision(plan, "unknown") == MountIslandRecoveryDecision.Reject, "policy: unknown reject");
            Check(Decision(plan, "missing-marker") == MountIslandRecoveryDecision.Skip, "policy: missing live+marker skip");
            Check(Decision(plan, "missing-receipt") == MountIslandRecoveryDecision.Skip, "policy: missing live+receipt skip");
            Check(Decision(plan, "other-grant") == MountIslandRecoveryDecision.Skip, "policy: other island grant skip");
            Check(Decision(plan, "receipt-unknown") == MountIslandRecoveryDecision.Reject, "policy: receipt unknown reject");
            Check(Decision(plan, "unreadable") == MountIslandRecoveryDecision.Reject, "policy: campaign unreadable reject");
            Check(Decision(plan, "wrong-land") == MountIslandRecoveryDecision.Skip, "policy: wrong split target skip");
            Check(Decision(plan, "dep") == MountIslandRecoveryDecision.Skip, "policy: dependency skip");
            Check(Decision(plan, "season") == MountIslandRecoveryDecision.Skip, "policy: seasonal skip");
            Check(Decision(plan, "create") == MountIslandRecoveryDecision.Create, "policy: clean create");
            Check(plan.Creates.Count == 1 && plan.MarkerOnly.Count == 1, "policy: partial groups independent");
            Check(plan.Diagnostics.Count == 5, "policy: rejects recorded as diagnostics");
        }

        // ------------------------------------------------------------------ placement

        private static MountIslandRecoveryInterval I(float min, float max)
            => new MountIslandRecoveryInterval(min, max);

        private static MountIslandRecoveryPlacementRequest R(string id, float half)
            => new MountIslandRecoveryPlacementRequest(id, half);

        private static bool Place(MountIslandRecoveryPlacementRequest[] requests,
            MountIslandRecoveryInterval[] tiles, MountIslandRecoveryInterval[] exclusions,
            float min, float max, out MountIslandRecoveryPlacement[] placements, out string reason,
            Func<float, float> snap = null, Func<float, float, bool> probe = null)
        {
            return MountIslandRecoveryPlacementPolicy.TryPlan(requests, tiles, exclusions, min, max,
                snap ?? (x => x), probe ?? ((x, h) => true), out placements, out reason);
        }

        private static void PlacementTests()
        {
            Check(Place(new[] { R("a", 2f), R("b", 2f) }, new[] { I(-10f, 10f) }, Array.Empty<MountIslandRecoveryInterval>(),
                    -10f, 10f, out var p1, out _)
                && p1.Length == 2 && p1[0].RootX == -8f && p1[1].RootX == -4f,
                "placement: sequential left-to-right");

            Check(Place(new[] { R("a", 2f) }, new[] { I(-10f, 10f) }, new[] { I(-1f, 1f) },
                    -10f, 10f, out var p2, out _)
                && p2[0].RootX == -8f,
                "placement: narrow hole excluded");

            Check(Place(new[] { R("a", 4f) }, new[] { I(-10f, 10f) }, new[] { I(-2f, 2f) },
                    -10f, 10f, out var p3, out _)
                && p3[0].RootX == -6f,
                "placement: bridge/beach exclusion");

            Check(!Place(new[] { R("a", 6f) }, new[] { I(-5f, 5f) }, Array.Empty<MountIslandRecoveryInterval>(),
                    -5f, 5f, out _, out _),
                "placement: no space fails batch");

            Check(Place(new[] { R("a", 1f), R("b", 1f) }, new[] { I(-5f, 5f) }, new[] { I(-2f, 2f) },
                    -5f, 5f, out var p4, out _)
                && p4[0].RootX == -4f && p4[1].RootX == 3f,
                "placement: building reservation preserved");

            Check(!Place(new[] { R("a", 5f) }, new[] { I(-4f, 4f) }, Array.Empty<MountIslandRecoveryInterval>(),
                    -4f, 4f, out _, out _),
                "placement: level edges reject");

            Check(Place(new[] { R("a", 1f) }, new[] { I(-10.1f, 10f) }, Array.Empty<MountIslandRecoveryInterval>(),
                    -10.1f, 10f, out var p5, out _, x => (float)Math.Floor(x * 4f) / 4f)
                && p5[0].RootX - 1f >= -10.1f - 1e-4f,
                "placement: snap remains inside interval");

            int probes = 0;
            Check(Place(new[] { R("a", 1f) }, new[] { I(-10f, 10f) }, Array.Empty<MountIslandRecoveryInterval>(),
                    -10f, 10f, out var p6, out _, null, (x, h) => ++probes > 1)
                && p6[0].RootX > -9f,
                "placement: failed probe advances candidate");

            Check(!Place(new[] { R("a", 0.6f) }, new[] { I(-1.05f, -0.05f), I(0.05f, 1.05f) },
                    Array.Empty<MountIslandRecoveryInterval>(), -1.05f, 1.05f, out _, out _),
                "placement: narrow interior gap cannot be bridged");
        }

        // ------------------------------------------------------------------ marker

        private static int[] A(params int[] values) => values;

        private static void MarkerTests()
        {
            Check(MountIslandMarkerBatch.SameSequence(A(1, 2), A(1, 2)), "marker: sequence equal");
            Check(!MountIslandMarkerBatch.SameSequence(A(1, 2), A(1, 3)), "marker: sequence differs");
            int[] deduped = MountIslandMarkerBatch.AppendAll(A(1, 2), A(2, 3, 3, 4));
            Check(MountIslandMarkerBatch.SameSequence(deduped, A(1, 2, 3, 4)), "marker: append dedupe and preserve");

            // committed normal
            int[][] pre = { A(1), A(2) };
            int[][] current = MountIslandMarkerBatch.ClonePreimage(pre);
            int writes = 0;
            MountIslandMarkerCommitOutcome outcome = MountIslandMarkerBatch.Commit(pre, 0, A(3),
                () => MountIslandMarkerBatch.ClonePreimage(current),
                (land, values) => { writes++; current[land] = (int[])values.Clone(); },
                out int[][] post, out string reason);
            Check(outcome == MountIslandMarkerCommitOutcome.Committed && writes == 1
                && MountIslandMarkerBatch.SameSequence(post[0], A(1, 3))
                && MountIslandMarkerBatch.SameSequence(post[1], A(2)),
                "marker: committed keeps other land");

            // setter throws after applying
            current = MountIslandMarkerBatch.ClonePreimage(pre);
            outcome = MountIslandMarkerBatch.Commit(pre, 0, A(3),
                () => MountIslandMarkerBatch.ClonePreimage(current),
                (land, values) => { current[land] = (int[])values.Clone(); throw new InvalidOperationException("setter"); },
                out post, out reason);
            Check(outcome == MountIslandMarkerCommitOutcome.Committed, "marker: readback postimage wins over setter throw");

            // setter throws before applying
            current = MountIslandMarkerBatch.ClonePreimage(pre);
            outcome = MountIslandMarkerBatch.Commit(pre, 0, A(3),
                () => MountIslandMarkerBatch.ClonePreimage(current),
                (land, values) => throw new InvalidOperationException("setter"),
                out post, out reason);
            Check(outcome == MountIslandMarkerCommitOutcome.NotCommitted, "marker: readback preimage => not committed");

            // setter throws and corrupts another land
            current = MountIslandMarkerBatch.ClonePreimage(pre);
            outcome = MountIslandMarkerBatch.Commit(pre, 0, A(3),
                () => MountIslandMarkerBatch.ClonePreimage(current),
                (land, values) => { current[land] = (int[])values.Clone(); current[1] = A(99); throw new InvalidOperationException("setter"); },
                out post, out reason);
            Check(outcome == MountIslandMarkerCommitOutcome.Unknown, "marker: unrelated readback => unknown");

            // pre-read mismatch / unavailable
            Check(MountIslandMarkerBatch.Commit(pre, 0, A(3), () => new[] { A(1), A(9) },
                    (land, values) => throw new Exception(), out _, out _) == MountIslandMarkerCommitOutcome.NotCommitted,
                "marker: preimage drift => no write");
            Check(MountIslandMarkerBatch.Commit(pre, 0, A(3), () => null,
                    (land, values) => throw new Exception(), out _, out _) == MountIslandMarkerCommitOutcome.NotCommitted,
                "marker: unreadable preimage => no write");

            // no-op: addition already present
            current = new[] { A(1, 3), A(2) };
            writes = 0;
            outcome = MountIslandMarkerBatch.Commit(new[] { A(1, 3), A(2) }, 0, A(3),
                () => MountIslandMarkerBatch.ClonePreimage(current),
                (land, values) => writes++,
                out post, out reason);
            Check(outcome == MountIslandMarkerCommitOutcome.Committed && writes == 0, "marker: already complete no write");
        }

        // ------------------------------------------------------------------ transaction

        private sealed class FakeHost : IMountIslandRecoveryHost
        {
            internal readonly List<int> Owned = new List<int>();
            internal int CreateCalls;
            internal int DiscardCalls;
            internal int CommitCalls;
            internal int FailCreateAt = -1;
            internal bool ValidateOk = true;
            internal bool ActivateOk = true;
            internal bool ThrowActivate;
            internal bool ThrowCommit;
            internal int Activated;
            internal int OldObjects = 7;
            internal MountIslandMarkerCommitOutcome MarkerOutcome = MountIslandMarkerCommitOutcome.Committed;
            internal int[] MarkerTypes;

            public bool TryCreateInactive(MountIslandRecoveryArm arm, float rootX, out int handle, out string reason)
            {
                int call = CreateCalls++;
                if (call == FailCreateAt) { handle = -1; reason = "fake create fail"; return false; }
                handle = Owned.Count;
                Owned.Add(handle);
                reason = string.Empty;
                return true;
            }

            public bool TryValidateCreatedBatch(out string reason)
            {
                reason = ValidateOk ? string.Empty : "fake validate fail";
                return ValidateOk;
            }

            public bool TryActivateCreatedBatch(out string reason)
            {
                if (ThrowActivate)
                {
                    if (Owned.Count > 0) Activated = 1;
                    throw new InvalidOperationException("fake activate throw");
                }
                reason = ActivateOk ? string.Empty : "fake activate fail";
                return ActivateOk;
            }

            public void DiscardCreatedBatch(string reason)
            {
                DiscardCalls++;
                Owned.Clear();
            }

            public MountIslandMarkerCommitOutcome CommitMarkers(int[] markerTypes, out string reason)
            {
                CommitCalls++;
                MarkerTypes = markerTypes;
                if (ThrowCommit) throw new InvalidOperationException("fake commit throw");
                reason = "fake marker";
                return MarkerOutcome;
            }
        }

        private static MountIslandRecoveryPlan PlanWith(params MountIslandRecoveryArm[] arms)
        {
            var plan = new MountIslandRecoveryPlan();
            for (int i = 0; i < arms.Length; i++) plan.Add(arms[i]);
            return plan;
        }

        private static MountIslandRecoveryArm Arm(string id, int type, MountIslandRecoveryDecision decision)
            => new MountIslandRecoveryArm(id, type, decision, "test");

        private static MountIslandRecoveryPlacement[] Placements(params string[] ids)
        {
            var result = new MountIslandRecoveryPlacement[ids.Length];
            for (int i = 0; i < ids.Length; i++)
                result[i] = new MountIslandRecoveryPlacement(ids[i], i * 10f, 1f);
            return result;
        }

        private static void TransactionTests()
        {
            var host = new FakeHost();
            var plan = PlanWith(Arm("a", 1, MountIslandRecoveryDecision.Create),
                Arm("m", 2, MountIslandRecoveryDecision.MarkerOnly));
            var result = MountIslandRecoveryTransaction.Run(plan, Placements("a"), host);
            Check(result.Outcome == MountIslandRecoveryExecutionOutcome.Committed
                && host.CreateCalls == 1 && host.CommitCalls == 1 && host.DiscardCalls == 0
                && MountIslandMarkerBatch.SameSequence(host.MarkerTypes, A(2, 1)),
                "transaction: commit success and marker order");

            var twoCreates = PlanWith(Arm("a", 1, MountIslandRecoveryDecision.Create),
                Arm("b", 3, MountIslandRecoveryDecision.Create));
            host = new FakeHost { FailCreateAt = 1 };
            result = MountIslandRecoveryTransaction.Run(twoCreates, Placements("a", "b"), host);
            Check(result.Outcome == MountIslandRecoveryExecutionOutcome.CreateFailed
                && host.CreateCalls == 2 && host.DiscardCalls == 1 && host.CommitCalls == 0,
                "transaction: clone fail discards owned and writes no marker");

            host = new FakeHost { FailCreateAt = 0 };
            result = MountIslandRecoveryTransaction.Run(plan, Placements("a"), host);
            Check(result.Outcome == MountIslandRecoveryExecutionOutcome.CreateFailed
                && host.DiscardCalls == 1 && host.CommitCalls == 0,
                "transaction: first clone fail still cleans partial owner");

            host = new FakeHost { ValidateOk = false };
            result = MountIslandRecoveryTransaction.Run(plan, Placements("a"), host);
            Check(result.Outcome == MountIslandRecoveryExecutionOutcome.ValidateFailed
                && host.DiscardCalls == 1 && host.CommitCalls == 0,
                "transaction: validate fail discards");

            host = new FakeHost { ActivateOk = false };
            result = MountIslandRecoveryTransaction.Run(plan, Placements("a"), host);
            Check(result.Outcome == MountIslandRecoveryExecutionOutcome.ActivateFailed
                && host.DiscardCalls == 1 && host.CommitCalls == 0,
                "transaction: activate fail discards");

            host = new FakeHost { ThrowActivate = true };
            result = MountIslandRecoveryTransaction.Run(twoCreates, Placements("a", "b"), host);
            Check(result.Outcome == MountIslandRecoveryExecutionOutcome.ActivateFailed
                && host.Activated == 1 && host.Owned.Count == 0 && host.OldObjects == 7
                && host.DiscardCalls == 1 && host.CommitCalls == 0,
                "transaction: midbatch activation exception discards all owned");

            host = new FakeHost { MarkerOutcome = MountIslandMarkerCommitOutcome.NotCommitted };
            result = MountIslandRecoveryTransaction.Run(plan, Placements("a"), host);
            Check(result.Outcome == MountIslandRecoveryExecutionOutcome.MarkerNotCommitted
                && host.DiscardCalls == 1,
                "transaction: marker not committed discards created");

            host = new FakeHost { MarkerOutcome = MountIslandMarkerCommitOutcome.Unknown };
            result = MountIslandRecoveryTransaction.Run(plan, Placements("a"), host);
            Check(result.Outcome == MountIslandRecoveryExecutionOutcome.MarkerUnknown
                && host.DiscardCalls == 0,
                "transaction: marker unknown retains created");

            host = new FakeHost { ThrowCommit = true };
            result = MountIslandRecoveryTransaction.Run(plan, Placements("a"), host);
            Check(result.Outcome == MountIslandRecoveryExecutionOutcome.MarkerUnknown
                && host.DiscardCalls == 0,
                "transaction: marker exception retains created");

            host = new FakeHost();
            plan = PlanWith(Arm("m", 2, MountIslandRecoveryDecision.MarkerOnly));
            result = MountIslandRecoveryTransaction.Run(plan, Array.Empty<MountIslandRecoveryPlacement>(), host);
            Check(result.Outcome == MountIslandRecoveryExecutionOutcome.Committed
                && host.CreateCalls == 0 && host.CommitCalls == 1 && host.DiscardCalls == 0,
                "transaction: marker-only path");

            host = new FakeHost();
            plan = PlanWith(Arm("a", 1, MountIslandRecoveryDecision.Create));
            result = MountIslandRecoveryTransaction.Run(plan, Array.Empty<MountIslandRecoveryPlacement>(), host);
            Check(result.Outcome == MountIslandRecoveryExecutionOutcome.InvalidPlan
                && host.CreateCalls == 0 && host.CommitCalls == 0 && host.DiscardCalls == 1,
                "transaction: missing placement invalidates batch");

            int[] types = MountIslandRecoveryTransaction.BuildMarkerTypes(
                PlanWith(Arm("a", 1, MountIslandRecoveryDecision.Create),
                    Arm("b", 1, MountIslandRecoveryDecision.MarkerOnly),
                    Arm("c", 3, MountIslandRecoveryDecision.Create)));
            Check(MountIslandMarkerBatch.SameSequence(types, A(1, 3)), "transaction: marker types dedupe");
        }

        // ------------------------------------------------------------------ geometry

        private static void GeometryTests()
        {
            Check(MountIslandRecoveryGeometry.Facilities.Length == 16, "geometry: 16 facility rows");
            Check(MountIslandRecoveryGeometry.StructureExclusions.Length == 25, "geometry: 25 structure exclusions");
            Check(MountIslandRecoveryGeometry.GeometrySourceSha256
                == "c46b486120a6df0181f8e7894e9583c546063064d18c91e5e48f5cc0cc7a4d5f",
                "geometry: source hash pinned");
            Check(MountIslandRecoveryGeometry.SurfaceSourceSha256
                == "d7fa15952d477d1b723f57ee61a32d6493b0874f46b79a877496754f674054db",
                "geometry: surface hash pinned");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < MountIslandRecoveryGeometry.Facilities.Length; i++)
            {
                MountIslandRecoveryGeometry.FacilityRow row = MountIslandRecoveryGeometry.Facilities[i];
                Check(ids.Add(row.DefinitionId), "geometry: unique definition " + row.DefinitionId);
                Check(paths.Add(row.FacilityPersistentPath), "geometry: unique path " + row.FacilityPersistentPath);
                Check(row.ReservedHalfWidth > 0f, "geometry: positive reserved half " + row.DefinitionId);
                float envelope = Math.Max(Math.Abs(row.EnvelopeMinX), Math.Abs(row.EnvelopeMaxX));
                Check(row.ReservedHalfWidth + 1e-4f >= envelope,
                    "geometry: reserved covers envelope " + row.DefinitionId);
                Check(CrossWorldMountCatalog.Definitions[i].Id == row.DefinitionId,
                    "geometry: catalog order aligns " + row.DefinitionId);
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < MountIslandRecoveryGeometry.StructureExclusions.Length; i++)
            {
                MountIslandRecoveryGeometry.StructureExclusion row = MountIslandRecoveryGeometry.StructureExclusions[i];
                Check(keys.Add(row.Kind + "|" + row.PersistentPath), "geometry: unique structure key");
                Check(MountIslandRecoveryGeometry.TryGetStructureExclusion(row.Kind, row.PersistentPath, out _),
                    "geometry: structure lookup");
            }
            Check(MountIslandRecoveryGeometry.IsEligibleTilePath("Prefabs/Environment/Tile Big"), "geometry: Tile Big eligible");
            Check(MountIslandRecoveryGeometry.IsEligibleTilePath("Prefabs/Environment/Tile Forest"), "geometry: Tile Forest eligible");
            Check(!MountIslandRecoveryGeometry.IsEligibleTilePath("Prefabs/Environment/Cave Tile Big"), "geometry: cave rejected");
            Check(!MountIslandRecoveryGeometry.IsEligibleTilePath("Prefabs/Environment/Tile Nothing"), "geometry: nothing rejected");
        }
    }
}
