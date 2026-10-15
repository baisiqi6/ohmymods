using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using KingdomEnhancedMod;

// 纯证据层测试：直接编译链接生产文件 proposal/il2cpp/Patch_MountIslandFreshEvidence.cs +
// runtime-worker 已发布的纯 helper Patch_MountIslandAvailabilityPolicy.cs（MountIslandSnapshotIdentity）+
// tracked Patch_MountIslandSplitPolicy.cs（不镜像实现）。
// 驱动 envelope 生命周期（Started→CompletedOnce→Transferred/Closed）、非零 slot 冻结、
// claim/recheck、提交 batch（own marker 语义）。
internal static class Program
{
    private static int _checks;
    private static readonly List<string> Failures = new List<string>();

    private static void Check(bool condition, string label)
    {
        _checks++;
        if (!condition) Failures.Add(label);
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

    private static int Main()
    {
        ClassifierBattery();
        MetadataCrossCheck();
        EnvelopeLifecycle();
        SlotFrozenBinding();
        ClaimAndRecheck();
        SnapshotIdentity();
        CommitBatch();
        AdvanceLifecycle();
        StickyInvalidation();
        StampMatrix();
        AdapterRouteFence();
        LiveGenerationClosure();
        PrefixIssuedGenerationSeam();
        TerminalBranchProbe();

        Console.WriteLine("checks=" + _checks + " failures=" + Failures.Count);
        foreach (string failure in Failures) Console.WriteLine("FAIL: " + failure);
        return Failures.Count == 0 ? 0 : 1;
    }

    private static void EnvelopeLifecycle()
    {
        // 无 seal 的 draft 不是新请求
        var gate = new MountIslandFreshGate();
        Check(!gate.TryPublish(Draft()), "publish rejected: no sealed state 0 request");

        // seal 校验（mutable 输入不得绕 scope helper）
        Check(!gate.TrySealEnvelope(Envelope(scope: false)), "seal rejected: scope not confirmed");
        Check(!gate.TrySealEnvelope(Envelope(land: 7)), "seal rejected: non-extension land");
        Check(!gate.TrySealEnvelope(Envelope(preReadsOk: false)), "seal rejected: pre history unreadable");
        Check(!gate.TrySealEnvelope(Envelope(owner: Owner(11, currentLand: 3))),
            "seal rejected: owner.CurrentLand != requested land (no parameter backfill)");
        Check(!gate.TrySealEnvelope(Envelope(owner: Owner(11, selected: 1))),
            "seal rejected: selected index != request campaignIndex");
        Check(gate.State == MountIslandEnvelopeState.Closed, "failed seal leaves gate Closed");

        // Started → CompletedOnce → Transferred（一次性）
        Check(gate.TrySealEnvelope(Envelope()), "seal accepted -> Started");
        Check(gate.State == MountIslandEnvelopeState.Started, "state is Started after seal");
        Check(gate.TryPublish(Draft()), "first completion publishes -> CompletedOnce");
        Check(gate.State == MountIslandEnvelopeState.CompletedOnce, "state is CompletedOnce after publish");
        Check(!gate.TryPublish(Draft()), "second publish on same envelope rejected (CompletedOnce)");
        MountIslandFrameTicket ticket = gate.TakeFrameTicket();
        Check(ticket != null, "single transfer to frame -> Transferred");
        Check(gate.State == MountIslandEnvelopeState.Transferred, "state is Transferred after take");
        Check(!gate.TryPublish(Draft()), "publish after transfer rejected (no new seal)");
        Check(gate.TakeFrameTicket() == null, "second transfer rejected");

        // 新 state0 显式 seal 新请求：代次递增、旧票据失效
        long before = gate.Generation;
        Check(gate.TrySealEnvelope(Envelope(routine: 0x200)), "new state 0 seals new request");
        Check(gate.Generation == before + 1, "new state 0 bumps generation");
        Check(ticket.IssuedGeneration != gate.Generation, "old ticket generation superseded");
        Check(!gate.HasPending, "old pending cleared by new seal");

        // abort/unknown 显式撤销：遗留候选不可被后续抢占
        gate.Revoke();
        Check(gate.State == MountIslandEnvelopeState.Closed, "revoke closes envelope");
        Check(!gate.HasPending, "revoke clears pending candidate");
        Check(!gate.TryPublish(Draft()), "publish after revoke rejected (no seal)");
        Check(!gate.EnvelopeSealed, "revoked envelope is not sealed");

        // 前像/请求/身份耦合（post flags 不能洗掉 pre 正证）
        var coupled = new MountIslandFreshGate();
        coupled.TrySealEnvelope(Envelope(prePositive: true));
        Check(!coupled.TryPublish(Draft()), "publish rejected: pre-image positive");
        var tamper = new MountIslandFreshGate();
        tamper.TrySealEnvelope(Envelope());
        Check(!tamper.TryPublish(Draft(campaignIndex: 1)), "publish rejected: request campaignIndex tampered");
        Check(!tamper.TryPublish(Draft(land: 13)), "publish rejected: request land tampered");
        Check(!tamper.TryPublish(Draft(routine: 0xBEEF)), "publish rejected: routine mismatch");
        Check(!tamper.TryPublish(Draft(owner: Owner(11, global: 0xFFFF))), "publish rejected: global replaced");
        Check(!tamper.TryPublish(Draft(owner: Owner(11, currentLand: 13))),
            "publish rejected: live currentLand drift");
        Check(!tamper.TryPublish(Draft(historyReadsOk: false)), "publish rejected: post history unreadable");
        Check(!tamper.TryPublish(Draft(historyPositive: true)), "publish rejected: post history positive");
        Check(!tamper.TryPublish(Draft(flags: 0x50)), "publish rejected: Success is not a fresh candidate");
        Check(!tamper.TryPublish(Draft(flags: 0x890)), "publish rejected: Corrupted completion");
    }

    private static void SlotFrozenBinding()
    {
        var gate = new MountIslandFreshGate();
        gate.TrySealEnvelope(Envelope(preSlot: 0x1000));
        Check(!gate.TryPublish(Draft(slot: 0x2000)), "publish rejected: pre non-zero slot replaced");
        Check(gate.TryPublish(Draft(slot: 0x1000)), "publish accepted: same pre slot pointer");

        var zero = new MountIslandFreshGate();
        zero.TrySealEnvelope(Envelope(preSlot: 0));
        Check(!zero.TryPublish(Draft(slot: 0)), "publish rejected: zero post slot stays Unknown (no wildcard)");
        Check(zero.TryPublish(Draft(slot: 0x3000)), "publish accepted: non-zero placeholder created within same request");

        var bound = new MountIslandFreshGate();
        bound.TrySealEnvelope(Envelope(preSlot: 0));
        bound.TryPublish(Draft(slot: 0x3000));
        Check(bound.TryPeekPending(out MountIslandTicketDraft frozen) && frozen.SlotPointer == 0x3000,
            "ticket freezes exact non-zero post slot");

        // recheck：slot 被另一个 empty 替换 → deny（非零精确比较）
        MountIslandFrameTicket ticket = bound.TakeFrameTicket();
        Check(MountIslandGrantAdmissionEvaluator.EvaluateFrameRecheck(ticket, true, true, true, true, true, true, false, false)
            == MountIslandGrantAdmission.Unknown, "recheck: slot replaced by another empty pointer -> Unknown");
        Check(MountIslandGrantAdmissionEvaluator.EvaluateFrameRecheck(ticket, true, true, true, true, true, true, false, true)
            == MountIslandGrantAdmission.Fresh, "recheck: same non-zero slot -> Fresh");
    }

    private static void ClaimAndRecheck()
    {
        var gate = new MountIslandFreshGate();
        gate.TrySealEnvelope(Envelope(preSlot: 0x1000));
        gate.TryPublish(Draft(slot: 0x1000));
        Check(gate.TryPeekPending(out MountIslandTicketDraft draft), "claim setup: pending draft");
        Check(MountIslandClaimEvaluator.Evaluate(draft, true, true, true, true, true), "claim accepted when all match");
        Check(!MountIslandClaimEvaluator.Evaluate(draft, false, true, true, true, true), "claim rejected: generation superseded");
        Check(!MountIslandClaimEvaluator.Evaluate(draft, true, false, true, true, true), "claim rejected: owner/global/currentLand drift");
        Check(!MountIslandClaimEvaluator.Evaluate(draft, true, true, false, true, true), "claim rejected: config not exact private owned");
        Check(!MountIslandClaimEvaluator.Evaluate(draft, true, true, true, false, true), "claim rejected: target level not captured");
        Check(!MountIslandClaimEvaluator.Evaluate(draft, true, true, true, true, false), "claim rejected: slot policy violated");

        MountIslandFrameTicket ticket = gate.TakeFrameTicket();
        Check(ticket != null && ticket.RequestLand == 11 && ticket.SlotPointer == 0x1000, "claim transfer freezes land+slot");
        Check(MountIslandGrantAdmissionEvaluator.EvaluateFrameRecheck(ticket, true, true, true, true, true, true, false, true)
            == MountIslandGrantAdmission.Fresh, "recheck all-match -> Fresh");
        Check(MountIslandGrantAdmissionEvaluator.EvaluateFrameRecheck(ticket, true, false, true, true, true, true, false, true)
            == MountIslandGrantAdmission.Unknown, "recheck owner/currentLand drift -> Unknown");
        Check(MountIslandGrantAdmissionEvaluator.EvaluateFrameRecheck(ticket, false, true, true, true, true, true, false, true)
            == MountIslandGrantAdmission.Unknown, "recheck generation superseded -> Unknown");
        Check(MountIslandGrantAdmissionEvaluator.EvaluateFrameRecheck(ticket, true, true, true, true, true, true, true, true)
            == MountIslandGrantAdmission.Unknown, "recheck history positive -> Unknown");
        Check(MountIslandGrantAdmissionEvaluator.EvaluateFrameRecheck(ticket, true, true, true, false, true, true, false, true)
            == MountIslandGrantAdmission.Unknown, "recheck owned-config missing -> Unknown");
        Check(MountIslandGrantAdmissionEvaluator.EvaluateFrameRecheck(ticket, true, true, false, true, true, true, false, true)
            == MountIslandGrantAdmission.Unknown, "recheck level identity missing -> Unknown");
        Check(MountIslandGrantAdmissionEvaluator.EvaluateFrameRecheck(ticket, true, true, true, true, false, true, false, true)
            == MountIslandGrantAdmission.Unknown, "recheck history unreadable -> Unknown");
        ticket.Consume();
        Check(MountIslandGrantAdmissionEvaluator.EvaluateFrameRecheck(ticket, true, true, true, true, true, true, false, true)
            == MountIslandGrantAdmission.Unknown, "recheck consumed ticket -> Unknown");
    }

    private static void SnapshotIdentity()
    {
        // runtime 已发布纯 helper：before/after 身份必须一致（不允许采混合身份）
        Check(MountIslandSnapshotIdentity.MatchesOwnerScope(
                0xC0DE, 0, 11, 0xAAA0, 2, 0xBBB0, 0xCCC0,
                0xC0DE, 0, 11, 0xAAA0, 2, 0xBBB0, 0xCCC0),
            "MatchesOwnerScope: identical before/after -> true");
        Check(!MountIslandSnapshotIdentity.MatchesOwnerScope(
                0xC0DE, 0, 11, 0xAAA0, 2, 0xBBB0, 0xCCC0,
                0xC0DE, 0, 13, 0xAAA0, 2, 0xBBB0, 0xCCC0),
            "MatchesOwnerScope: currentLand changed during snapshot -> false");
        Check(!MountIslandSnapshotIdentity.MatchesOwnerScope(
                0xC0DE, 0, 11, 0xAAA0, 2, 0xBBB0, 0xCCC0,
                0xBEEF, 0, 11, 0xAAA0, 2, 0xBBB0, 0xCCC0),
            "MatchesOwnerScope: global replaced during snapshot -> false");
        Check(!MountIslandSnapshotIdentity.MatchesOwnerScope(
                0xC0DE, 0, 11, 0xAAA0, 2, 0xBBB0, 0xCCC0,
                0xC0DE, 1, 11, 0xAAA0, 2, 0xBBB0, 0xCCC0),
            "MatchesOwnerScope: selected index changed during snapshot -> false");
    }

    private static void CommitBatch()
    {
        MountIslandBatchGrant Grant(string id, int steed, int land = 11, bool placed = true, bool authorized = true)
        {
            return new MountIslandBatchGrant
            {
                DefinitionId = id,
                Land = land,
                SteedTypeId = steed,
                Authorized = authorized,
                Placed = placed,
            };
        }

        // 8 条全提交（own marker 先写不阻塞后写：PlanGrant 无 history 输入）
        var session = new MountIslandCommitSession(true, "batch fresh verified before own writes");
        int writes = 0;
        for (int i = 0; i < 8; i++)
            if (session.PlanGrant(Grant("def" + i, 20 + i), bindingOk: true, frameLand: 11, out _)) writes++;
        Check(writes == 8, "commit batch: all 8 grants commit (own marker cannot reject itself)");

        // 外部非 own marker → batch 未验证：整批 authorized 拒
        var blocked = new MountIslandCommitSession(false, "external non-own marker present");
        int blockedWrites = 0;
        for (int i = 0; i < 8; i++)
            if (blocked.PlanGrant(Grant("def" + i, 20 + i), true, 11, out _)) blockedWrites++;
        Check(blockedWrites == 0, "commit batch: external non-own marker blocks all authorized markers");

        // partial placement：仅未放置的 grant 不写
        var partial = new MountIslandCommitSession(true, "ok");
        int partialWrites = 0;
        bool skipped = false;
        for (int i = 0; i < 8; i++)
        {
            bool ok = partial.PlanGrant(Grant("def" + i, 20 + i, placed: i != 3), true, 11, out _);
            if (ok) partialWrites++;
            else skipped = skipped || i == 3;
        }
        Check(partialWrites == 7 && skipped, "commit batch: partial placement skips only the unplaced grant");

        // 错误 land：拒
        Check(!partial.PlanGrant(Grant("def-x", 30, land: 13), true, frameLand: 11, out _),
            "commit batch: wrong land grant rejected");

        // binding 失败：该条拒（authorized）
        Check(!partial.PlanGrant(Grant("def-y", 31), bindingOk: false, frameLand: 11, out _),
            "commit batch: binding failure aborts that grant");

        // 非授权（重建）路径不受 batch 门影响
        var rebuild = new MountIslandCommitSession(false, "no ticket");
        Check(rebuild.PlanGrant(Grant("def-r", 32, authorized: false), false, 11, out _),
            "commit batch: unauthorized rebuild grant commits without fresh batch");
    }

    private static MountIslandAdvanceOutcome Advance(int preState, bool result, int postState,
        bool envelopeSealed = true, bool sameRoutine = true, bool sameRequest = true, bool runOriginal = true, bool postfixRan = true)
    {
        return MountIslandAdvanceClassifier.Classify(new MountIslandAdvanceInput
        {
            PreState = preState,
            RunOriginal = runOriginal,
            PostfixRan = postfixRan,
            Result = result,
            PostState = postState,
            EnvelopeSealed = envelopeSealed,
            SameRoutine = sameRoutine,
            SameRequest = sameRequest,
        });
    }

    private static void AdvanceLifecycle()
    {
        // direct-production 完整链条：state0 true 保 Started → state1 true 保 → state2 false/-1 Terminal → publish → take once
        var gate = new MountIslandFreshGate();
        gate.TrySealEnvelope(Envelope());
        Check(Advance(0, true, 1) == MountIslandAdvanceOutcome.Continue, "advance: state0 true + postState1 -> Continue");
        gate.ApplyNonTerminal(MountIslandAdvanceOutcome.Continue, allowRevoke: true);
        Check(gate.State == MountIslandEnvelopeState.Started && gate.EnvelopeSealed,
            "advance: normal first yield preserves Started");
        Check(Advance(1, true, 2) == MountIslandAdvanceOutcome.Continue, "advance: state1 true + postState2 -> Continue");
        gate.ApplyNonTerminal(MountIslandAdvanceOutcome.Continue, allowRevoke: true);
        Check(gate.State == MountIslandEnvelopeState.Started, "advance: mid yield preserves Started (no publish)");
        Check(Advance(2, false, -1) == MountIslandAdvanceOutcome.Terminal, "advance: state2 false + postState -1 -> Terminal");
        gate.ApplyNonTerminal(MountIslandAdvanceOutcome.Terminal, allowRevoke: true);
        Check(gate.TryPublish(Draft(slot: 0x1000)), "advance: terminal MissingData publishes once");
        MountIslandFrameTicket ticket = gate.TakeFrameTicket();
        Check(ticket != null && gate.State == MountIslandEnvelopeState.Transferred, "advance: take once -> Transferred");
        Check(gate.TakeFrameTicket() == null, "advance: second take rejected");

        // contrasts
        Check(Advance(0, true, 1, envelopeSealed: false) == MountIslandAdvanceOutcome.Abort,
            "advance: no-state0 call -> Abort");
        Check(Advance(0, true, 1, sameRoutine: false) == MountIslandAdvanceOutcome.Abort,
            "advance: changed/foreign routine -> Abort");
        Check(Advance(0, true, 1, sameRequest: false) == MountIslandAdvanceOutcome.Abort,
            "advance: changed request -> Abort");
        Check(Advance(0, true, 1, runOriginal: false) == MountIslandAdvanceOutcome.Abort,
            "advance: skipped original -> Abort");
        Check(Advance(0, true, 1, postfixRan: false) == MountIslandAdvanceOutcome.Abort,
            "advance: postfix absent (exception path) -> Abort");
        Check(Advance(1, true, 5) == MountIslandAdvanceOutcome.Abort, "advance: invalid postState on yield -> Abort");
        Check(Advance(2, false, 2) == MountIslandAdvanceOutcome.Abort, "advance: non-terminal postState on false -> Abort");
        Check(Advance(0, false, -1) == MountIslandAdvanceOutcome.Abort, "advance: state0 false -> Abort");
        Check(Advance(-1, false, -1) == MountIslandAdvanceOutcome.Ignore, "advance: closed repeat -> Ignore");

        // closed repeat / foreign abort 不毁另一新请求
        var other = new MountIslandFreshGate();
        other.TrySealEnvelope(Envelope(routine: 0x200));
        other.ApplyNonTerminal(MountIslandAdvanceOutcome.Ignore, allowRevoke: true);
        other.ApplyNonTerminal(MountIslandAdvanceOutcome.Abort, allowRevoke: false);
        Check(other.State == MountIslandEnvelopeState.Started && other.EnvelopeSealed,
            "advance: foreign/closed call cannot revoke another new request");
        Check(other.TryPublish(Draft(routine: 0x200, slot: 0x1000)), "advance: other request still publishable");

        // outer error（finalizer exception）→ Abort 语义：撤销当前请求
        var error = new MountIslandFreshGate();
        error.TrySealEnvelope(Envelope());
        error.Revoke();
        Check(error.State == MountIslandEnvelopeState.Closed && !error.HasPending,
            "advance: outer exception path revokes active request");
    }

    private static void StickyInvalidation()
    {
        var gate = new MountIslandFreshGate();
        gate.TrySealEnvelope(Envelope(preSlot: 0x1000));
        gate.TryPublish(Draft(slot: 0x1000));
        MountIslandFrameTicket ticket = gate.TakeFrameTicket();
        Check(ticket.Usable, "sticky setup: ticket usable");

        // 读 fault / 拒绝 → sticky：字段恢复也不能重新 fresh
        ticket.InvalidateAgainst(gate);
        Check(ticket.Invalidated && !ticket.Usable, "sticky: invalidation sticks");
        Check(gate.State == MountIslandEnvelopeState.Closed, "sticky: same-generation invalidation revokes its envelope");
        Check(MountIslandGrantAdmissionEvaluator.EvaluateFrameRecheck(ticket, true, true, true, true, true, true, false, true)
            == MountIslandGrantAdmission.Unknown, "sticky: recovered facts cannot re-fresh an invalidated ticket");
        Check(MountIslandGrantAdmissionEvaluator.EvaluateFrameRecheck(ticket, true, false, true, true, true, true, false, true)
            == MountIslandGrantAdmission.Unknown, "sticky: denied recheck stays Unknown after failure");

        // 另一个新 request：旧票据失效不得误撤新 envelope
        var fresh = new MountIslandFreshGate();
        fresh.TrySealEnvelope(Envelope(routine: 0x100));
        fresh.TryPublish(Draft(routine: 0x100, slot: 0x1000));
        MountIslandFrameTicket oldTicket = fresh.TakeFrameTicket();
        fresh.TrySealEnvelope(Envelope(routine: 0x200));   // 新请求，代次 +1
        oldTicket.InvalidateAgainst(fresh);                // 旧票据失效
        Check(oldTicket.Invalidated, "sticky: superseded ticket invalidated");
        Check(fresh.State == MountIslandEnvelopeState.Started && fresh.EnvelopeSealed,
            "sticky: superseded ticket cannot revoke the newer request");
        Check(fresh.TryPublish(Draft(routine: 0x200, slot: 0x1000)), "sticky: newer request still publishable");
        Check(MountIslandGrantAdmissionEvaluator.EvaluateFrameRecheck(oldTicket, true, true, true, true, true, true, false, true)
            == MountIslandGrantAdmission.Unknown, "sticky: invalidated ticket never returns Fresh");
    }

    private static void StampMatrix()
    {
        MountIslandObservationStamp Base() => new MountIslandObservationStamp
        {
            Owner = Owner(11),
            RoutinePointer = 0x9000,
            RequestCampaignIndex = 0,
            RequestChallengeId = 0,
            RequestLand = 11,
            Generation = 5,
        };

        Check(MountIslandStampMatch.Matches(Base(), Base()), "stamp: identical stamps match");
        MountIslandObservationStamp Mutate(Func<MountIslandObservationStamp, MountIslandObservationStamp> f) => f(Base());
        Check(!MountIslandStampMatch.Matches(Base(), Mutate(s => { s.Owner = Owner(11, holder: 0xBEEF); return s; })),
            "stamp: holder pointer change rejected");
        Check(!MountIslandStampMatch.Matches(Base(), Mutate(s => { s.Owner = Owner(11, biome: 2); return s; })),
            "stamp: holder biome change rejected");
        Check(!MountIslandStampMatch.Matches(Base(), Mutate(s => { s.Owner = Owner(11, global: 0xBEEF); return s; })),
            "stamp: global change rejected");
        Check(!MountIslandStampMatch.Matches(Base(), Mutate(s => { s.Owner = Owner(11, selected: 1); return s; })),
            "stamp: selected index change rejected");
        Check(!MountIslandStampMatch.Matches(Base(), Mutate(s => { s.Owner = Owner(11, currentLand: 13); return s; })),
            "stamp: currentLand change rejected");
        Check(!MountIslandStampMatch.Matches(Base(), Mutate(s => { s.Owner = Owner(11, campaign: 0xBEEF); return s; })),
            "stamp: campaign change rejected");
        Check(!MountIslandStampMatch.Matches(Base(), Mutate(s => { s.RoutinePointer = 0xBEEF; return s; })),
            "stamp: routine change rejected");
        Check(!MountIslandStampMatch.Matches(Base(), Mutate(s => { s.RequestCampaignIndex = 1; return s; })),
            "stamp: request campaignIndex change rejected");
        Check(!MountIslandStampMatch.Matches(Base(), Mutate(s => { s.RequestChallengeId = 5; return s; })),
            "stamp: request challenge change rejected");
        Check(!MountIslandStampMatch.Matches(Base(), Mutate(s => { s.RequestLand = 13; return s; })),
            "stamp: request land change rejected");
        Check(!MountIslandStampMatch.Matches(Base(), Mutate(s => { s.Generation = 6; return s; })),
            "stamp: generation change rejected");
    }

    private static void AdapterRouteFence()
    {
        // 模拟 Observer Prefix active 分支 / Finalizer exception 分支：统一走 production fence helper。
        MountIslandRouteFence Route(MountIslandFreshGate gate, ulong attemptRoutine, bool snapshotOk,
            int campaignIndex = 0, int challengeId = 0, int land = 11, long? attemptGeneration = null)
        {
            bool sealedNow = gate.EnvelopeSealed;
            return MountIslandAdapterRouteFence.Evaluate(sealedNow,
                sealedNow ? gate.Envelope.RoutinePointer : 0UL,
                sealedNow ? gate.Envelope.RequestCampaignIndex : -1,
                sealedNow ? gate.Envelope.RequestChallengeId : -1,
                sealedNow ? gate.Envelope.RequestLand : -1,
                snapshotOk, attemptRoutine, campaignIndex, challengeId, land,
                attemptGeneration ?? gate.Generation, gate.Generation);
        }

        // A active / B sealed：A 的 foreign active 不得毁 B
        var gate = new MountIslandFreshGate();
        gate.TrySealEnvelope(Envelope(routine: 0xBBB));
        MountIslandRouteFence foreign = Route(gate, attemptRoutine: 0xAAA, snapshotOk: true);
        MountIslandAdapterRouteFence.ApplyRevocation(gate, foreign);
        Check(foreign == MountIslandRouteFence.Foreign, "adapter: foreign active classified Foreign");
        Check(gate.State == MountIslandEnvelopeState.Started && gate.EnvelopeSealed,
            "adapter: foreign active preserves the new request (no revoke)");
        Check(gate.TryPublish(Draft(routine: 0xBBB, slot: 0x1000)), "adapter: preserved new request still publishable");

        // A closed / B sealed：stale（prefix 代次已过期）不得毁 B
        var staleGate = new MountIslandFreshGate();
        staleGate.TrySealEnvelope(Envelope(routine: 0xBBB));
        long live = staleGate.Generation;
        MountIslandRouteFence stale = Route(staleGate, attemptRoutine: staleGate.Envelope.RoutinePointer,
            snapshotOk: true, attemptGeneration: live - 2);
        MountIslandAdapterRouteFence.ApplyRevocation(staleGate, stale);
        Check(stale == MountIslandRouteFence.Stale, "adapter: stale generation classified Stale");
        Check(staleGate.State == MountIslandEnvelopeState.Started, "adapter: stale attempt preserves current request");

        // prefix snapshot 缺失：Unfenced，不得凭空撤
        var unfencedGate = new MountIslandFreshGate();
        unfencedGate.TrySealEnvelope(Envelope());
        MountIslandRouteFence unfenced = Route(unfencedGate, attemptRoutine: 0x9000, snapshotOk: false);
        MountIslandAdapterRouteFence.ApplyRevocation(unfencedGate, unfenced);
        Check(unfenced == MountIslandRouteFence.Unfenced, "adapter: missing snapshot classified Unfenced");
        Check(unfencedGate.State == MountIslandEnvelopeState.Started, "adapter: missing snapshot cannot revoke");

        // A exception（自己 routine）：own fault 撤
        var ownGate = new MountIslandFreshGate();
        ownGate.TrySealEnvelope(Envelope(routine: 0x9000));
        MountIslandRouteFence own = Route(ownGate, attemptRoutine: 0x9000, snapshotOk: true);
        MountIslandAdapterRouteFence.ApplyRevocation(ownGate, own);
        Check(own == MountIslandRouteFence.OwnCurrent, "adapter: own attempt classified OwnCurrent");
        Check(ownGate.State == MountIslandEnvelopeState.Closed, "adapter: own fault revokes its request");

        // 同 routine 但请求字段被改：Mutated 撤
        var mutatedGate = new MountIslandFreshGate();
        mutatedGate.TrySealEnvelope(Envelope(routine: 0x9000));
        MountIslandRouteFence mutated = Route(mutatedGate, attemptRoutine: 0x9000, snapshotOk: true, land: 13);
        MountIslandAdapterRouteFence.ApplyRevocation(mutatedGate, mutated);
        Check(mutated == MountIslandRouteFence.Mutated, "adapter: same routine changed request classified Mutated");
        Check(mutatedGate.State == MountIslandEnvelopeState.Closed, "adapter: mutated attempt sticky-closes its request");

        // foreign exception 保新 request（与 active 同策略）
        var excGate = new MountIslandFreshGate();
        excGate.TrySealEnvelope(Envelope(routine: 0xBBB));
        MountIslandRouteFence foreignException = Route(excGate, attemptRoutine: 0xAAA, snapshotOk: true);
        MountIslandAdapterRouteFence.ApplyRevocation(excGate, foreignException);
        Check(excGate.State == MountIslandEnvelopeState.Started && excGate.EnvelopeSealed,
            "adapter: foreign exception preserves the new request");
        Check(excGate.TryPublish(Draft(routine: 0xBBB, slot: 0x1000)), "adapter: new request after foreign exception publishable");
    }

    private static MountIslandObservationStamp LiveStamp(MountIslandFreshGate gate, ulong routine,
        int campaignIndex, int challengeId, int land, MountIslandOwnerIdentity owner)
    {
        // 模拟 Observer 的 live 采样：generation 直接读 gate（不回填 expected）
        return new MountIslandObservationStamp
        {
            Owner = owner,
            RoutinePointer = routine,
            RequestCampaignIndex = campaignIndex,
            RequestChallengeId = challengeId,
            RequestLand = land,
            Generation = gate.Generation,
        };
    }

    private static void LiveGenerationClosure()
    {
        var gate = new MountIslandFreshGate();
        gate.TrySealEnvelope(Envelope(routine: 0x9000));
        gate.TryPublish(Draft(routine: 0x9000, slot: 0x1000));
        MountIslandFrameTicket ticket = gate.TakeFrameTicket();
        Check(ticket != null && ticket.IssuedGeneration == gate.Generation, "live gen: ticket stamped with live generation");

        MountIslandObservationStamp before = LiveStamp(gate, ticket.RoutinePointer,
            ticket.RequestCampaignIndex, ticket.RequestChallengeId, ticket.RequestLand, ticket.Owner);
        gate.TrySealEnvelope(Envelope(routine: 0x200));   // facts 期间发生新 seal（代次 +1）
        MountIslandObservationStamp after = LiveStamp(gate, ticket.RoutinePointer,
            ticket.RequestCampaignIndex, ticket.RequestChallengeId, ticket.RequestLand, ticket.Owner);

        Check(!MountIslandStampMatch.Matches(before, after),
            "live gen: new seal during facts breaks before/after stamp closure");
        Check(after.Generation != ticket.IssuedGeneration, "live gen: after stamp samples new live generation");

        ticket.InvalidateAgainst(gate);
        Check(ticket.Invalidated, "live gen: superseded ticket invalidated (sticky)");
        Check(gate.State == MountIslandEnvelopeState.Started && gate.EnvelopeSealed,
            "live gen: invalidating the old ticket does not revoke the newer gate");
        Check(gate.TryPublish(Draft(routine: 0x200, slot: 0x1000)), "live gen: newer request remains publishable");
        Check(MountIslandGrantAdmissionEvaluator.EvaluateFrameRecheck(ticket, ticket.IssuedGeneration == gate.Generation,
            true, true, true, true, true, false, true) == MountIslandGrantAdmission.Unknown,
            "live gen: generation-mismatched recheck stays Unknown");
    }

    private static void PrefixIssuedGenerationSeam()
    {
        // 生产 prefix seam 真实顺序：preGen → seal（gen+1）→ attempt 使用 issued generation → own fault 撤
        var gate = new MountIslandFreshGate();
        long preGen = gate.Generation;
        Check(gate.TrySealEnvelope(Envelope(routine: 0x9000)), "prefix seam: state0 seal ok");
        long issued = gate.Generation;
        Check(issued == preGen + 1, "prefix seam: seal issues generation pre+1");

        MountIslandRouteFence ownFault = MountIslandAdapterRouteFence.Evaluate(true, gate.Envelope.RoutinePointer,
            gate.Envelope.RequestCampaignIndex, gate.Envelope.RequestChallengeId, gate.Envelope.RequestLand,
            true, 0x9000, 0, 0, 11, issued, gate.Generation);
        Check(ownFault == MountIslandRouteFence.OwnCurrent,
            "prefix seam: own fault fenced OwnCurrent when using issued generation");
        MountIslandAdapterRouteFence.ApplyRevocation(gate, ownFault);
        Check(gate.State == MountIslandEnvelopeState.Closed, "prefix seam: own fault revokes own started request");

        // 误用 pre-seal 代次（旧 bug 路径）：Stale，不撤
        var bugGate = new MountIslandFreshGate();
        bugGate.TrySealEnvelope(Envelope(routine: 0x9000));
        long issued2 = bugGate.Generation;
        MountIslandRouteFence staleWithPre = MountIslandAdapterRouteFence.Evaluate(true, bugGate.Envelope.RoutinePointer,
            0, 0, 11, true, 0x9000, 0, 0, 11, issued2 - 1, bugGate.Generation);
        Check(staleWithPre == MountIslandRouteFence.Stale, "prefix seam: pre-seal generation would be Stale (bug avoided)");

        // facts 期间 foreign seal：不得借新 generation 判 own（routine 优先 → Foreign，不撤新）
        var foreignGate = new MountIslandFreshGate();
        foreignGate.TrySealEnvelope(Envelope(routine: 0x9000));
        long issuedA = foreignGate.Generation;
        foreignGate.TrySealEnvelope(Envelope(routine: 0x200));
        MountIslandRouteFence borrowed = MountIslandAdapterRouteFence.Evaluate(true, foreignGate.Envelope.RoutinePointer,
            0, 0, 11, true, 0x9000, 0, 0, 11, issuedA, foreignGate.Generation);
        Check(borrowed == MountIslandRouteFence.Foreign, "prefix seam: foreign seal mid-facts classified Foreign");
        MountIslandAdapterRouteFence.ApplyRevocation(foreignGate, borrowed);
        Check(foreignGate.State == MountIslandEnvelopeState.Started, "prefix seam: new seal not revoked by stale attempt");
        Check(foreignGate.TryPublish(Draft(routine: 0x200, slot: 0x1000)), "prefix seam: new request still publishable");
    }

    private static void TerminalBranchProbe()
    {
        // 与 Observer CompleteTerminal 相同顺序：advance Terminal → fence（issued gen）→ facts 失败 → ApplyRevocation。
        var own = new MountIslandFreshGate();
        own.TrySealEnvelope(Envelope(routine: 0x9000));
        long issued = own.Generation;
        MountIslandRouteFence ownFence = MountIslandAdapterRouteFence.Evaluate(true, own.Envelope.RoutinePointer,
            own.Envelope.RequestCampaignIndex, own.Envelope.RequestChallengeId, own.Envelope.RequestLand,
            true, 0x9000, 0, 0, 11, issued, own.Generation);
        MountIslandAdapterRouteFence.ApplyRevocation(own, ownFence);   // 模拟 negative facts / stamp 失败分支
        Check(ownFence == MountIslandRouteFence.OwnCurrent && own.State == MountIslandEnvelopeState.Closed,
            "terminal probe: own negative/success revokes own request only");

        var interleaved = new MountIslandFreshGate();
        interleaved.TrySealEnvelope(Envelope(routine: 0x9000));
        long issuedA = interleaved.Generation;
        interleaved.TrySealEnvelope(Envelope(routine: 0x200));         // facts 期间新 seal
        MountIslandRouteFence stale = MountIslandAdapterRouteFence.Evaluate(true, interleaved.Envelope.RoutinePointer,
            interleaved.Envelope.RequestCampaignIndex, interleaved.Envelope.RequestChallengeId, interleaved.Envelope.RequestLand,
            true, 0x9000, 0, 0, 11, issuedA, interleaved.Generation);
        MountIslandAdapterRouteFence.ApplyRevocation(interleaved, stale);   // 模拟 before/after-stamp 失败
        Check(stale == MountIslandRouteFence.Foreign && interleaved.State == MountIslandEnvelopeState.Started,
            "terminal probe: stamp failure during new seal does not destroy the new request");
        Check(interleaved.TryPublish(Draft(routine: 0x200, slot: 0x1000)), "terminal probe: new request still publishable");

        var unfenced = new MountIslandFreshGate();
        unfenced.TrySealEnvelope(Envelope(routine: 0x9000));
        MountIslandRouteFence noSnapshot = MountIslandAdapterRouteFence.Evaluate(true, unfenced.Envelope.RoutinePointer,
            0, 0, 11, false, 0x9000, 0, 0, 11, unfenced.Generation, unfenced.Generation);
        MountIslandAdapterRouteFence.ApplyRevocation(unfenced, noSnapshot);
        Check(noSnapshot == MountIslandRouteFence.Unfenced && unfenced.State == MountIslandEnvelopeState.Started,
            "terminal probe: unreadable facts do not revoke");
    }

    private static void ClassifierBattery()
    {
        Check(MountIslandCompletionClassifier.Classify(Completion(0x410)) == MountIslandCompletionClass.MissingData,
            "0x410 -> MissingData");
        Check(MountIslandCompletionClassifier.Classify(Completion(0x490)) == MountIslandCompletionClass.MissingData,
            "0x490 -> MissingData");
        Check(MountIslandCompletionClassifier.Classify(Completion(0x890)) == MountIslandCompletionClass.Corrupted,
            "0x890 -> Corrupted");
        Check(MountIslandCompletionClassifier.Classify(Completion(0x50)) == MountIslandCompletionClass.Success,
            "0x50 -> Success");
        Check(MountIslandCompletionClassifier.Classify(Completion(0)) == MountIslandCompletionClass.Unknown,
            "old zero -> Unknown");
        Check(MountIslandCompletionClassifier.Classify(Completion(0x90)) == MountIslandCompletionClass.FailureOther,
            "0x90 -> FailureOther");
        Check(MountIslandCompletionClassifier.Classify(Completion(0x410 | 0x10000)) == MountIslandCompletionClass.Unknown,
            "unknown high bit -> Unknown");
        Check(MountIslandCompletionClassifier.Classify(Completion(0x450)) == MountIslandCompletionClass.Unknown,
            "0x450 -> Unknown");
        Check(MountIslandCompletionClassifier.Classify(Completion(0x150)) == MountIslandCompletionClass.Unknown,
            "0x150 -> Unknown");
        Check(MountIslandCompletionClassifier.Classify(Completion(0x1000)) == MountIslandCompletionClass.Unknown,
            "InsufficientSpace without Load -> Unknown");
        Check(MountIslandCompletionClassifier.Classify(Completion(0x410, flagsReadOk: false)) == MountIslandCompletionClass.Unknown,
            "flags read fault -> Unknown");
        Check(MountIslandCompletionClassifier.Classify(Completion(0x410, runOriginal: false)) == MountIslandCompletionClass.Unknown,
            "skip original -> Unknown");
        Check(MountIslandCompletionClassifier.Classify(Completion(0x410, completedNormally: false)) == MountIslandCompletionClass.Unknown,
            "finalizer failure/outer exception -> Unknown");
        Check(MountIslandCompletionClassifier.Classify(Completion(0x410, postState: 1)) == MountIslandCompletionClass.Unknown,
            "non-terminal post state -> Unknown");
        Check(MountIslandCompletionClassifier.Classify(Completion(0x410, preState: -1)) == MountIslandCompletionClass.Unknown,
            "repeated closed call -> Unknown");
        Check(MountIslandCompletionClassifier.Classify(Completion(0x410, preState: 0)) == MountIslandCompletionClass.Unknown,
            "state 0 start call -> Unknown");
        Check(MountIslandGrantAdmissionEvaluator.ClassifyCompletion(MountIslandCompletionClass.Success, true) == MountIslandGrantAdmission.Existing,
            "Success + proof -> Existing");
        Check(MountIslandGrantAdmissionEvaluator.ClassifyCompletion(MountIslandCompletionClass.Success, false) == MountIslandGrantAdmission.Unknown,
            "Success without proof -> Unknown (never Fresh)");
        Check(MountIslandGrantAdmissionEvaluator.ClassifyCompletion(MountIslandCompletionClass.MissingData, false) == MountIslandGrantAdmission.Fresh,
            "MissingData without positive -> Fresh");
        Check(MountIslandGrantAdmissionEvaluator.ClassifyCompletion(MountIslandCompletionClass.Corrupted, false) == MountIslandGrantAdmission.Unknown,
            "Corrupted -> Unknown");
    }

    private static void MetadataCrossCheck()
    {
        string dll = Environment.GetEnvironmentVariable("KEM_GAME_INTEROP");
        if (string.IsNullOrEmpty(dll))
        {
            Console.WriteLine("SKIP native SaveLoadResult metadata cross-check: set KEM_GAME_INTEROP to a matching Assembly-CSharp.dll");
            return;
        }
        Check(File.Exists(dll), "supplied interop Assembly-CSharp.dll exists");
        if (!File.Exists(dll)) return;

        var interop = new Dictionary<string, int>(StringComparer.Ordinal);
        using (FileStream stream = File.OpenRead(dll))
        using (var pe = new PEReader(stream))
        {
            MetadataReader md = pe.GetMetadataReader();
            foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
            {
                TypeDefinition type = md.GetTypeDefinition(handle);
                if (md.GetString(type.Name) != "SaveLoadResult" || md.GetString(type.Namespace) != "Coatsink.Common")
                    continue;
                foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
                {
                    FieldDefinition field = md.GetFieldDefinition(fieldHandle);
                    string name = md.GetString(field.Name);
                    if (name == "value__") continue;
                    ConstantHandle constantHandle = field.GetDefaultValue();
                    if (constantHandle.IsNil) continue;
                    BlobReader blob = md.GetBlobReader(md.GetConstant(constantHandle).Value);
                    interop[name] = blob.ReadInt32();
                }
            }
        }

        Check(interop.Count == 16, "interop SaveLoadResult has 16 members (actual=" + interop.Count + ")");
        foreach (string name in Enum.GetNames(typeof(MountIslandSaveLoadFlag)))
        {
            if (name == "None") continue;
            int mine = (int)(MountIslandSaveLoadFlag)Enum.Parse(typeof(MountIslandSaveLoadFlag), name);
            Check(interop.TryGetValue(name, out int actual) && actual == mine,
                $"interop enum {name}: expected mirror {mine}, actual {(interop.TryGetValue(name, out int v) ? v.ToString() : "missing")}");
        }
    }
}
