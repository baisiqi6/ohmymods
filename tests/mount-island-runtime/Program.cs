// production-linked 纯测试（真实源文件直编进本程序集，非 stub、非镜像模型）：
// - Patch_MountIslandSplitPolicy.cs（WT 实际 policy 文件，原样 link）
// - CrossWorldMountData.cs / ExtensionIslandPlan.cs（proposal 副本）
// - Patch_MountIslandFilePropsRefresh.cs / Patch_MountIslandAvailabilityPolicy.cs（proposal 新增纯 helper，
//   production 的 FileProps 三钩子/可用性+旅行判定/快照稳定性调用同一类型）
// 覆盖：A/B stable-Id membership、16 目录冻结、Decide 证据/unknown 规则、DistributeSeams 稳定；
// refresh 状态机（lateMAX/prefixFailure/postfix-skip/finalizer-skip/finalizerException/nested-fail-outer/stale/unowned）；
// 可用性 vs 旅行门分离（visited-missing 可见但不可出航 / read-unknown 双闭 / first-time eligible）、
// B 资格（allBGranted/eventExpired）、快照身份与单元稳定性（ownerChanged/槽内替换 否决）。
// interop/Unity 侧（真实读取、Harmony 装配、Native 行为）不在本测试范围。
using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

internal static class Program
{
    private static int _failures;

    private static void Check(bool ok, string name)
    {
        if (ok) { Console.WriteLine("PASS " + name); }
        else { _failures++; Console.WriteLine("FAIL " + name); }
    }

    private static CrossWorldMountIslandState State(
        bool markerOnIsland = false, bool markerInCampaign = false, bool visited = false,
        bool receiptOnIsland = false, double lastPlayed = 0d,
        bool receiptInCampaign = false, bool evidenceReadable = false, bool landValid = true)
        => new CrossWorldMountIslandState(landValid, markerOnIsland, markerInCampaign, visited,
            receiptOnIsland, lastPlayed, 0, receiptInCampaign, evidenceReadable);

    private static MountIslandAvailabilityOutcome Avail(
        int land = 13, bool scopeProven = true, bool current = false,
        bool visitedKnown = true, bool visited = false,
        bool generatedKnown = true, bool generated = false,
        bool feature = true, bool hasEligible = true)
        => MountIslandAvailabilityPolicy.Decide(land, scopeProven, current, visitedKnown, visited,
            generatedKnown, generated, feature, hasEligible);

    private static MountIslandTravelOutcome Travel(
        int land = 13, bool scopeProven = true, bool current = false,
        bool visitedKnown = true, bool visited = false,
        bool generatedKnown = true, bool generated = false, bool slotPresent = true,
        bool feature = true, bool hasEligible = true)
        => MountIslandAvailabilityPolicy.DecideTravel(land, scopeProven, current, visitedKnown, visited,
            generatedKnown, generated, slotPresent, feature, hasEligible);

    private static int Main()
    {
        // ---------- A. policy 表（原文件 link） ----------
        Check(MountIslandSplitPolicy.GetTargetLand("norselands.wolf") == MountIslandSplitPolicy.PrimaryLand,
            "wolf(SteedType=13) grouped to primary by Id, not by steed value");
        Check(MountIslandSplitPolicy.GetTargetLand("bamboo.kirin") == MountIslandSplitPolicy.PrimaryLand,
            "bamboo.kirin -> primary");
        Check(MountIslandSplitPolicy.GetTargetLand("anniversary.rainbowpony") == MountIslandSplitPolicy.SecondaryLand,
            "rainbowpony -> secondary");
        Check(MountIslandSplitPolicy.GetTargetLand("no.such.id") == -1, "unknown id -> -1");
        Check(MountIslandSplitPolicy.GetTargetLand(null) == -1, "null id -> -1");
        Check(MountIslandSplitPolicy.GetTargetLand("NORSELANDS.WOLF") == -1, "membership is case-sensitive");
        Check(MountIslandFilePropsRefresh.RequiredCapacity == MountIslandSplitPolicy.RequiredFileCapacity,
            "refresh machine references real policy RequiredFileCapacity (=14)");
        Check(MountIslandSplitPolicy.RequiredFileCapacity == 14, "policy requires capacity 14");

        Check(MountIslandSplitPolicy.IsExtensionLand(11) && MountIslandSplitPolicy.IsExtensionLand(13),
            "extension lands 11/13");
        for (int land = 0; land <= 12; land++)
        {
            if (land == 11) continue;   // primary extension island
            Check(!MountIslandSplitPolicy.IsExtensionLand(land), "native/palace land " + land + " not extension");
        }
        Check(!MountIslandSplitPolicy.IsExtensionLand(14), "land 14 not extension");

        Check(MountIslandSplitPolicy.TryGetMapIndex(11, out int ui11) && ui11 == 10, "physical 11 -> UI 10");
        Check(MountIslandSplitPolicy.TryGetMapIndex(13, out int ui13) && ui13 == 11, "physical 13 -> UI 11");
        Check(!MountIslandSplitPolicy.TryGetMapIndex(10, out int ui10) && ui10 == -1, "native 10 untouched (no UI9 mapping)");
        Check(!MountIslandSplitPolicy.TryGetMapIndex(12, out _), "palace 12 not routed by policy");
        Check(MountIslandSplitPolicy.TryGetPhysicalIndex(10, out int p10) && p10 == 11, "UI 10 -> physical 11");
        Check(MountIslandSplitPolicy.TryGetPhysicalIndex(11, out int p11) && p11 == 13, "UI 11 -> physical 13");
        Check(!MountIslandSplitPolicy.TryGetPhysicalIndex(9, out _), "native UI 9 not routed");

        // ---------- B. catalog 16 → A8/B8（冻结 Id 集合） ----------
        var expectA = new HashSet<string>(StringComparer.Ordinal)
        {
            "norselands.gullinbursti", "norselands.sleipnir", "norselands.reindeer", "norselands.catcart",
            "norselands.kelpie", "norselands.hrimfaxe", "norselands.wolf", "bamboo.kirin",
        };
        var expectB = new HashSet<string>(StringComparer.Ordinal)
        {
            "woodlands.beetle", "woodlands.golem", "woodlands.gamigin", "woodlands.mansion",
            "swamp.eggsteed", "deadlands.grave", "santahouse.reindeer", "anniversary.rainbowpony",
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int countA = 0, countB = 0, countUnknown = 0;
        foreach (CrossWorldMountDefinition def in CrossWorldMountCatalog.Definitions)
        {
            Check(seen.Add(def.Id), "definition id unique: " + def.Id);
            int target = MountIslandSplitPolicy.GetTargetLand(def.Id);
            if (target == 11) countA++;
            else if (target == 13) countB++;
            else countUnknown++;
            Check(CrossWorldMountPolicy.ShouldGrantDefinition(def, target),
                "ShouldGrantDefinition true on own target: " + def.Id);
            Check(!CrossWorldMountPolicy.ShouldGrantDefinition(def, target == 11 ? 13 : 11),
                "ShouldGrantDefinition false on sibling land: " + def.Id);
            Check(!CrossWorldMountPolicy.ShouldGrantDefinition(def, 12),
                "palace land 12 never a mount target: " + def.Id);
            Check(expectA.Contains(def.Id) == (target == 11) && expectB.Contains(def.Id) == (target == 13),
                "grouping matches frozen Id set: " + def.Id);
        }
        Check(CrossWorldMountCatalog.Definitions.Length == 16, "catalog stays 16 definitions");
        Check(countA == 8 && countB == 8 && countUnknown == 0, "A8/B8 exact split (no unknown/unassigned)");

        // ---------- C. Decide 证据与 unknown 规则 ----------
        Check(CrossWorldMountPolicy.Decide(State(landValid: false)) == CrossWorldMountDecision.Skip,
            "invalid land -> Skip");
        Check(CrossWorldMountPolicy.Decide(State(evidenceReadable: true)) == CrossWorldMountDecision.GrantAndInject,
            "fresh island + readable evidence + no grants -> GrantAndInject");
        Check(CrossWorldMountPolicy.Decide(State()) == CrossWorldMountDecision.Skip,
            "unknown campaign evidence -> Skip (never newgrant)");
        Check(CrossWorldMountPolicy.Decide(State(receiptOnIsland: true)) == CrossWorldMountDecision.InjectOnly,
            "island receipt -> InjectOnly even when campaign evidence unreadable");
        Check(CrossWorldMountPolicy.Decide(State(markerOnIsland: true)) == CrossWorldMountDecision.InjectOnly,
            "island marker -> InjectOnly");
        Check(CrossWorldMountPolicy.Decide(State(receiptInCampaign: true, evidenceReadable: true))
            == CrossWorldMountDecision.Skip, "campaign-wide receipt blocks newgrant");
        Check(CrossWorldMountPolicy.Decide(State(markerInCampaign: true, evidenceReadable: true))
            == CrossWorldMountDecision.Skip, "other-island marker blocks newgrant");
        Check(CrossWorldMountPolicy.Decide(State(visited: true, evidenceReadable: true))
            == CrossWorldMountDecision.Skip, "visited island -> Skip (no retro-insert)");
        Check(CrossWorldMountPolicy.Decide(State(lastPlayed: 0.5d, evidenceReadable: true))
            == CrossWorldMountDecision.Skip, "played island -> Skip");

        // ---------- D. DistributeSeams 稳定/均匀（A/B 共用同一规则） ----------
        int[] s1 = ExtensionIslandPlan.DistributeSeams(14, 8);
        int[] s2 = ExtensionIslandPlan.DistributeSeams(14, 8);
        Check(s1.Length == 8, "seam count == definition count");
        bool same = s1.Length == s2.Length;
        for (int i = 0; same && i < s1.Length; i++) same = s1[i] == s2[i];
        Check(same, "seams deterministic across calls");
        int[] expect = { 1, 2, 4, 6, 8, 9, 11, 13 };
        bool exact = s1.Length == expect.Length;
        for (int i = 0; exact && i < expect.Length; i++) exact = s1[i] == expect[i];
        Check(exact, "seams evenly distributed: " + string.Join(",", s1));
        bool ordered = true;
        for (int i = 1; i < s1.Length; i++) if (s1[i] < s1[i - 1]) ordered = false;
        Check(ordered, "seams non-decreasing (stable publish order)");
        int[] dense = ExtensionIslandPlan.DistributeSeams(3, 8);
        bool inRange = dense.Length == 8;
        for (int i = 0; inRange && i < dense.Length; i++) inRange = dense[i] >= 1 && dense[i] <= 3;
        Check(inRange, "definitions exceeding seams stay within [1, interior] and are shared");

        // ---------- E. refresh 状态机（production 同一类型；单一 hook + postfix/finalizer __runOriginal） ----------
        MountIslandFilePropsRefresh.Begin(out var c1);
        MountIslandFilePropsRefresh.NoteSubmittedMax(c1, 14);
        MountIslandFilePropsRefresh.NoteOriginalRun(c1, true);
        Check(MountIslandFilePropsRefresh.Confirm(c1, false, 14, true) == MountIslandRefreshResult.Confirmed,
            "refresh: confirmed when original ran and currentMAX == submittedMAX == 14");
        Check(MountIslandFilePropsRefresh.IsPrepared(14), "refresh: ready at currentMAX == completed");
        Check(!MountIslandFilePropsRefresh.IsPrepared(20), "refresh: ready rejected when MAX drifts off completed");

        MountIslandFilePropsRefresh.Begin(out var c2);
        MountIslandFilePropsRefresh.NoteSubmittedMax(c2, 14);
        MountIslandFilePropsRefresh.NoteOriginalRun(c2, true);
        Check(MountIslandFilePropsRefresh.Confirm(c2, false, 20, true) == MountIslandRefreshResult.CapacityMismatch,
            "lateMAX: current 20 != submitted 14 -> not confirmed");
        Check(!MountIslandFilePropsRefresh.IsPrepared(20), "lateMAX: not-ready despite high MAX");

        MountIslandFilePropsRefresh.Begin(out var c3);
        MountIslandFilePropsRefresh.NotePrefixFailed(c3);
        MountIslandFilePropsRefresh.NoteSubmittedMax(c3, 14);
        MountIslandFilePropsRefresh.NoteOriginalRun(c3, true);
        Check(MountIslandFilePropsRefresh.Confirm(c3, false, 14, true) == MountIslandRefreshResult.CapacityMismatch,
            "prefixFailure: refresh can never become ready");
        Check(!MountIslandFilePropsRefresh.IsPrepared(14), "prefixFailure: end cannot wash state to ready");

        MountIslandFilePropsRefresh.Begin(out var c4);
        MountIslandFilePropsRefresh.NoteSubmittedMax(c4, 14);
        MountIslandFilePropsRefresh.NoteOriginalRun(c4, false);   // postfix __runOriginal=false（原方法被 skip）
        Check(MountIslandFilePropsRefresh.Confirm(c4, false, 14, true) == MountIslandRefreshResult.ChainSkipped,
            "postfix skip: __runOriginal=false -> not confirmed");
        Check(!MountIslandFilePropsRefresh.IsPrepared(14), "postfix skip: stays not-ready");

        MountIslandFilePropsRefresh.Begin(out var c4b);
        MountIslandFilePropsRefresh.NoteSubmittedMax(c4b, 14);
        MountIslandFilePropsRefresh.NoteOriginalRun(c4b, true);
        Check(MountIslandFilePropsRefresh.Confirm(c4b, false, 14, false) == MountIslandRefreshResult.ChainSkipped,
            "finalizer skip: readonly __runOriginal=false -> not confirmed");

        MountIslandFilePropsRefresh.Begin(out var c5);
        MountIslandFilePropsRefresh.NoteSubmittedMax(c5, 14);
        MountIslandFilePropsRefresh.NoteOriginalRun(c5, true);
        Check(MountIslandFilePropsRefresh.Confirm(c5, true, 14, true) == MountIslandRefreshResult.FailedOrAborted,
            "finalizerException: exception beats postfix evidence -> not ready");
        Check(!MountIslandFilePropsRefresh.IsPrepared(14), "finalizerException: stays not-ready");

        MountIslandFilePropsRefresh.Begin(out var c6);
        MountIslandFilePropsRefresh.NoteSubmittedMax(c6, 13);
        MountIslandFilePropsRefresh.NoteOriginalRun(c6, true);
        Check(MountIslandFilePropsRefresh.Confirm(c6, false, 13, true) == MountIslandRefreshResult.CapacityMismatch,
            "submitted 13 < required -> not ready");

        MountIslandFilePropsRefresh.Begin(out var outer);
        MountIslandFilePropsRefresh.NoteSubmittedMax(outer, 14);
        MountIslandFilePropsRefresh.Begin(out var inner);        // 嵌套 native 可执行返回
        MountIslandFilePropsRefresh.NoteSubmittedMax(inner, 14);
        MountIslandFilePropsRefresh.NoteOriginalRun(inner, true);
        Check(MountIslandFilePropsRefresh.Confirm(inner, false, 14, true) == MountIslandRefreshResult.NotOwner,
            "nested: inner confirm is not owner (no ready write)");
        Check(!MountIslandFilePropsRefresh.IsPrepared(14), "nested: not ready before outer confirm");
        MountIslandFilePropsRefresh.NoteOriginalRun(outer, true);
        Check(MountIslandFilePropsRefresh.Confirm(outer, false, 14, true) == MountIslandRefreshResult.NestedObserved,
            "nested: outer confirm fails closed (NestedObserved)");
        Check(!MountIslandFilePropsRefresh.IsPrepared(14), "nested: outer ready stays false");

        MountIslandFilePropsRefresh.Begin(out var first);
        MountIslandFilePropsRefresh.NoteSubmittedMax(first, 14);
        MountIslandFilePropsRefresh.NoteOriginalRun(first, true);
        Check(MountIslandFilePropsRefresh.Confirm(first, false, 14, true) == MountIslandRefreshResult.Confirmed,
            "stale: first refresh confirmed");
        MountIslandFilePropsRefresh.Begin(out var second);
        Check(!MountIslandFilePropsRefresh.IsPrepared(14), "stale: next refresh clears ready pre-confirmation");
        Check(MountIslandFilePropsRefresh.Confirm(first, false, 14, true) == MountIslandRefreshResult.None,
            "stale: duplicate finalizer of old call ignored");
        MountIslandFilePropsRefresh.NoteSubmittedMax(second, 14);
        MountIslandFilePropsRefresh.NoteOriginalRun(second, true);
        Check(MountIslandFilePropsRefresh.Confirm(second, false, 14, true) == MountIslandRefreshResult.Confirmed,
            "stale: current refresh confirms after nesting-free run");

        MountIslandFilePropsRefresh.NoteUnownedAttempt();
        Check(!MountIslandFilePropsRefresh.IsPrepared(14), "unowned attempt (state missing) invalidates ready");

        // ---------- F. 展示 vs 旅行门（visited-missing 可见但不可出航；unknown 双闭） ----------
        Check(!MountIslandAvailabilityPolicy.IsAvailable(Avail(visitedKnown: false, feature: true)),
            "availability: unknown visit never passes via feature switch");
        Check(!MountIslandAvailabilityPolicy.IsAvailable(Avail(generatedKnown: false, feature: true)),
            "availability: unknown raw slot never passes via feature switch");
        Check(Avail(current: true, visitedKnown: false, generatedKnown: false, feature: false)
            == MountIslandAvailabilityOutcome.CurrentRecovery,
            "availability: current recovery wins even with unreadable visited");
        // visited 且 raw slot 缺失：展示恢复可见，但旅行门 false
        Check(Avail(visited: true) == MountIslandAvailabilityOutcome.VisitedRecovery
            && MountIslandAvailabilityPolicy.IsAvailable(Avail(visited: true)),
            "availability: visited recovery stays visible");
        Check(Travel(visited: true, slotPresent: false) == MountIslandTravelOutcome.RecoveryDataMissing
            && !MountIslandAvailabilityPolicy.IsTravelAllowed(Travel(visited: true, slotPresent: false)),
            "travel: visited but raw slot missing -> denied (no auto-create)");
        Check(Travel(current: true, slotPresent: false) == MountIslandTravelOutcome.RecoveryDataMissing,
            "travel: current-positive but slot missing -> denied");
        Check(Travel(visited: true, slotPresent: true) == MountIslandTravelOutcome.VisitedRebuildable
            && MountIslandAvailabilityPolicy.IsTravelAllowed(Travel(visited: true, slotPresent: true)),
            "travel: visited with slot present -> rebuildable");
        Check(Travel(generated: true, slotPresent: true) == MountIslandTravelOutcome.ExistingRebuildable,
            "travel: generation-history positive -> rebuildable");
        Check(Travel(visitedKnown: false) == MountIslandTravelOutcome.UnknownEvidence
            && Travel(generatedKnown: false) == MountIslandTravelOutcome.UnknownEvidence,
            "travel: read-unknown closed both ways");
        Check(Travel(feature: true, hasEligible: true, visited: false, generated: false, slotPresent: false)
            == MountIslandTravelOutcome.FirstTimeEligible
            && MountIslandAvailabilityPolicy.IsTravelAllowed(
                Travel(feature: true, hasEligible: true, visited: false, generated: false, slotPresent: false)),
            "travel: fresh eligible new13 allowed");
        Check(Travel(land: 12) == MountIslandTravelOutcome.NotExtensionLand
            && Travel(land: 10) == MountIslandTravelOutcome.NotExtensionLand
            && !MountIslandAvailabilityPolicy.IsTravelAllowed(Travel(land: 12)),
            "travel: palace 12 / native lands not routed");
        Check(Travel(feature: true, hasEligible: false) == MountIslandTravelOutcome.NoEligibleRemaining,
            "travel: no eligible target -> closed");
        Check(Avail(feature: false, visited: false, generated: false) == MountIslandAvailabilityOutcome.NoEligibleRemaining,
            "availability: feature OFF without recovery -> closed");

        // ---------- G. B 资格（allBGranted / eventExpired） ----------
        var allGranted = new List<MountIslandNewGrantCandidate>();
        for (int i = 0; i < 8; i++) allGranted.Add(new MountIslandNewGrantCandidate("b" + i, true, true));
        Check(!MountIslandNewGrantEligibility.HasEligibleTarget(allGranted),
            "B eligibility: all B granted -> no eligible (closed)");
        var eventExpired = new List<MountIslandNewGrantCandidate>
        {
            new MountIslandNewGrantCandidate("anniversary.rainbowpony", false, false),
        };
        Check(!MountIslandNewGrantEligibility.HasEligibleTarget(eventExpired),
            "B eligibility: un-granted but season-inactive only -> no eligible (closed)");
        var someOpen = new List<MountIslandNewGrantCandidate>
        {
            new MountIslandNewGrantCandidate("b0", true, true),
            new MountIslandNewGrantCandidate("b1", false, true),
        };
        Check(MountIslandNewGrantEligibility.HasEligibleTarget(someOpen),
            "B eligibility: un-granted + season eligible -> eligible");
        Check(!MountIslandNewGrantEligibility.HasEligibleTarget(null),
            "B eligibility: missing candidate list (unknown) -> closed");

        // ---------- H. 快照身份 + 单元稳定性（ownerChanged / 槽内替换否决） ----------
        Check(MountIslandSnapshotIdentity.Matches(1UL, 2, 3UL, 4UL, 1UL, 2, 3UL, 4UL),
            "identity: unchanged four containers match");
        Check(!MountIslandSnapshotIdentity.Matches(1UL, 2, 3UL, 4UL, 9UL, 2, 3UL, 4UL),
            "identity: campaign change rejected");
        Check(!MountIslandSnapshotIdentity.Matches(1UL, 2, 3UL, 4UL, 1UL, 3, 3UL, 4UL),
            "identity: reign change rejected");
        Check(!MountIslandSnapshotIdentity.Matches(1UL, 2, 3UL, 4UL, 1UL, 2, 9UL, 4UL),
            "identity: landData container change rejected");
        Check(!MountIslandSnapshotIdentity.Matches(1UL, 2, 3UL, 4UL, 1UL, 2, 3UL, 9UL),
            "identity: _islands container change rejected");
        Check(!MountIslandSnapshotIdentity.Matches(0UL, 2, 3UL, 4UL, 0UL, 2, 3UL, 4UL),
            "identity: zero campaign pointer rejected");
        Check(!MountIslandSnapshotIdentity.Matches(1UL, -1, 3UL, 4UL, 1UL, -1, 3UL, 4UL),
            "identity: unreadable reign rejected");

        var cells = new[] { new MountIslandCellFingerprint(11UL, 21UL, 3), new MountIslandCellFingerprint(0UL, 0UL, -1) };
        var lands = new[] { new MountIslandLandFingerprint(31UL, 2) };
        Check(MountIslandCellStability.Matches(cells,
            new[] { new MountIslandCellFingerprint(11UL, 21UL, 3), new MountIslandCellFingerprint(0UL, 0UL, -1) },
            lands, new[] { new MountIslandLandFingerprint(31UL, 2) }),
            "cells: identical fingerprints stable");
        Check(!MountIslandCellStability.Matches(cells,
            new[] { new MountIslandCellFingerprint(12UL, 21UL, 3), new MountIslandCellFingerprint(0UL, 0UL, -1) },
            lands, new[] { new MountIslandLandFingerprint(31UL, 2) }),
            "cells: island object replaced rejected");
        Check(!MountIslandCellStability.Matches(cells,
            new[] { new MountIslandCellFingerprint(11UL, 22UL, 3), new MountIslandCellFingerprint(0UL, 0UL, -1) },
            lands, new[] { new MountIslandLandFingerprint(31UL, 2) }),
            "cells: objects list replaced rejected");
        Check(!MountIslandCellStability.Matches(cells,
            new[] { new MountIslandCellFingerprint(11UL, 21UL, 4), new MountIslandCellFingerprint(0UL, 0UL, -1) },
            lands, new[] { new MountIslandLandFingerprint(31UL, 2) }),
            "cells: objects count change rejected");
        Check(!MountIslandCellStability.Matches(cells,
            new[] { new MountIslandCellFingerprint(11UL, 21UL, 3), new MountIslandCellFingerprint(0UL, 0UL, -1) },
            lands, new[] { new MountIslandLandFingerprint(32UL, 2) }),
            "cells: marker array replaced rejected");
        Check(!MountIslandCellStability.Matches(cells,
            new[] { new MountIslandCellFingerprint(11UL, 21UL, 3), new MountIslandCellFingerprint(0UL, 0UL, -1) },
            lands, new[] { new MountIslandLandFingerprint(31UL, 3) }),
            "cells: marker length change rejected");
        Check(!MountIslandCellStability.Matches(cells,
            new[] { new MountIslandCellFingerprint(11UL, 21UL, 3) },
            lands, new[] { new MountIslandLandFingerprint(31UL, 2) }),
            "cells: slot count mismatch rejected");
        Check(!MountIslandCellStability.Matches(null, null, null, null), "cells: missing fingerprints rejected");

        // ---------- I. R1/R2 授权事实与 owner scope ----------
        var factsBefore = new Dictionary<string, bool> { { "a", true }, { "b", false } };
        var factsSame = new Dictionary<string, bool> { { "a", true }, { "b", false } };
        var factsFlipped = new Dictionary<string, bool> { { "a", false }, { "b", false } };
        var factsKeyChanged = new Dictionary<string, bool> { { "a", true }, { "c", false } };
        Check(MountIslandGrantFacts.SameReceiptFacts(factsBefore, factsSame), "receipt facts: identical set matches");
        Check(!MountIslandGrantFacts.SameReceiptFacts(factsBefore, factsFlipped),
            "receipt facts: bool flip rejected (record replaced / receipt string changed)");
        Check(!MountIslandGrantFacts.SameReceiptFacts(factsBefore, factsKeyChanged), "receipt facts: key set change rejected");
        Check(!MountIslandGrantFacts.SameReceiptFacts(factsBefore, null), "receipt facts: missing set rejected (unknown)");

        int[] seq = { 21, 22 };
        Check(MountIslandGrantFacts.SameIntSequence(seq, new[] { 21, 22 }), "marker content: identical elements match");
        Check(!MountIslandGrantFacts.SameIntSequence(seq, new[] { 21, 23 }),
            "marker content: element change rejected (same ptr/length)");
        Check(!MountIslandGrantFacts.SameIntSequence(seq, new[] { 21 }), "marker content: length change rejected");
        Check(MountIslandGrantFacts.SameIntSequence(null, null), "marker content: both null match");
        Check(!MountIslandGrantFacts.SameIntSequence(null, new int[0]), "marker content: null vs empty rejected");

        Check(MountIslandSnapshotIdentity.MatchesOwnerScope(1UL, 2, 11, 3UL, 4, 5UL, 6UL, 1UL, 2, 11, 3UL, 4, 5UL, 6UL),
            "owner scope: identical scope matches");
        Check(!MountIslandSnapshotIdentity.MatchesOwnerScope(1UL, 2, 11, 3UL, 4, 5UL, 6UL, 9UL, 2, 11, 3UL, 4, 5UL, 6UL),
            "owner scope: Global.loaded pointer change rejected");
        Check(!MountIslandSnapshotIdentity.MatchesOwnerScope(1UL, 2, 11, 3UL, 4, 5UL, 6UL, 1UL, 7, 11, 3UL, 4, 5UL, 6UL),
            "owner scope: raw selected slot change rejected");
        Check(!MountIslandSnapshotIdentity.MatchesOwnerScope(1UL, 2, 11, 3UL, 4, 5UL, 6UL, 1UL, 2, 13, 3UL, 4, 5UL, 6UL),
            "owner scope: CurrentLand change rejected");
        Check(!MountIslandSnapshotIdentity.MatchesOwnerScope(1UL, -1, 11, 3UL, 4, 5UL, 6UL, 1UL, -1, 11, 3UL, 4, 5UL, 6UL),
            "owner scope: unselected (menu) rejected");

        Check(MountIslandGrantFacts.AllowsGrantPublish(true, true),
            "publish gate: current authority + same raw owner allows publish");
        Check(!MountIslandGrantFacts.AllowsGrantPublish(false, true), "publish gate: stale authority -> no publish");
        Check(!MountIslandGrantFacts.AllowsGrantPublish(true, false), "publish gate: raw owner changed -> no publish");
        Check(!MountIslandGrantFacts.AllowsGrantPublish(false, false), "publish gate: both stale -> no publish");

        Console.WriteLine(_failures == 0 ? "ALL PASS" : _failures + " FAILURES");
        return _failures == 0 ? 0 : 1;
    }
}
