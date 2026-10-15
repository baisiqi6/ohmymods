using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// <c>IslandSaveData.&lt;_TryLoad&gt;d__54.MoveNext</c> 完成的**只读**观察与 fresh 票据门
    /// （private proposal，root 审查 + endpoint 审验注册前不得进入 tracked 主线）。
    ///
    /// 只读性契约：只经 typed getter 读取；不写 <c>__1__state</c>/<c>@return</c>/<c>_buffer_5__2</c>/
    /// campaign/land，不调用 factory / <c>MoveNext</c>/<c>Dispose</c>/<c>GetIsland</c>/<c>CurrentIsland</c>，
    /// 不为制造数据主动 load/storage；<c>@return</c> 只读 copy；不写 intent key、不周期扫描、不创建 config。
    ///
    /// R3 advance：每次 MoveNext 由 <see cref="MountIslandAdvanceClassifier"/> 判定——正常 yield
    /// （Result true + 活动 postState 1/2 + 同 envelope）保持 Started 且**不读 return flags**；
    /// 只有活动恢复的真实终末（false + -1）才进入 terminal 分类并读取 typed flags。
    /// closed state -1 重复 Ignore；mutated/skipped/非法 postState Abort（仅撤属于本 routine 的 envelope）。
    ///
    /// R3 stamp 闭合：oncompleted/claim/recheck/binding 一律 before（owner+完整请求+holder）→ **一次**
    /// history/slot + config 读取 → after 同 stamp，纯 <see cref="MountIslandStampMatch"/> 复核，
    /// 紧邻 publish/take/return true；任何字段变化或读取 fault 走 sticky 失效
    /// （<see cref="MountIslandFrameTicket.InvalidateAgainst"/>，不误撤另一新请求）。
    /// </summary>
    internal static class MountIslandFreshObservation
    {
        private const string LogPrefix = "[MountIslandFresh]";
        internal static readonly MountIslandFreshGate Gate = new MountIslandFreshGate();
        private static readonly HashSet<string> LoggedKeys = new HashSet<string>(StringComparer.Ordinal);

        private enum FactKind
        {
            History = 0,
            SlotOnly = 1,
        }

        // ------------------------------------------------------------------ endpoint 侧

        /// <summary>prefix：state 0 封印请求 envelope（stamp 闭合）；1/2 核 envelope 可用性；-1 只拒绝。</summary>
        internal static bool OnLoadStart(IslandSaveData.__TryLoad_d__54 instance, long prefixGeneration,
            out long issuedGeneration, out string detail)
        {
            issuedGeneration = -1;
            detail = "unreadable";
            try
            {
                if (instance == null) return false;
                int preState = instance.__1__state;
                if (preState == 0)
                {
                    // state 0 = 新请求边界：撤旧 envelope/票据（R5 next-state0 语义）。
                    if (!TryBuildEnvelope(instance, out MountIslandRequestEnvelope envelope, out detail))
                    {
                        Gate.Revoke();
                        LogError("state 0 envelope not sealed; this request stays Unknown: " + detail);
                        return false;
                    }
                    if (!Gate.TrySealEnvelope(envelope))
                    {
                        LogError("state 0 envelope failed seal validation: " + detail);
                        return false;
                    }
                    issuedGeneration = Gate.Generation;   // 本次成功 seal 的 issued generation（不是 pre-seal 值）
                    Log("state 0 envelope sealed (Started): routine=0x" + envelope.RoutinePointer.ToString("X")
                        + " campaignIndex=" + envelope.RequestCampaignIndex + " challenge=" + envelope.RequestChallengeId
                        + " land=" + envelope.RequestLand + " currentLand=" + envelope.Owner.CurrentLand
                        + " slot=0x" + envelope.PreSlotPointer.ToString("X")
                        + " prePositive=" + envelope.PreHistoryAnyPositive
                        + " issuedGen=" + issuedGeneration);
                    detail = "ok";
                    return true;
                }

                if (preState == 1 || preState == 2)
                {
                    bool sealedNow = Gate.EnvelopeSealed
                        && Gate.Envelope.RoutinePointer == PointerOf(instance);
                    if (sealedNow)
                        issuedGeneration = Gate.Generation;   // routine 未变 ⇒ generation 即本请求 issued 值
                    else
                    {
                        // 统一路由 fence：只有属于当前 envelope 的 own/mutated attempt 才撤；
                        // foreign/stale/unfenced 一律保新 current（不能毁另一个新请求）。
                        MountIslandRouteFence fence = EvaluateAttempt(instance, snapshotOk: true, prefixGeneration: prefixGeneration);
                        MountIslandAdapterRouteFence.ApplyRevocation(Gate, fence);
                        if (MountIslandAdapterRouteFence.CanRevoke(fence))
                            LogError("active resume without matching state 0 envelope: fenced revocation (fence="
                                + fence + ", preState=" + preState + ")");
                        else
                            LogError("active resume not fenced to current request (fence=" + fence
                                + "); new current request preserved (preState=" + preState + ")");
                    }
                    detail = sealedNow ? "ok" : "no matching state 0 envelope (fenced check applied)";
                    return sealedNow;
                }

                detail = "closed state " + preState;
                return false;
            }
            catch (Exception e)
            {
                detail = "load start read fault: " + e.GetType().Name;
                // 读 fault 的 attempt 不可 fence（Unfenced/NoEnvelope → no-op）；不凭空撤新 current。
                MountIslandRouteFence fence = EvaluateAttempt(instance, snapshotOk: false, prefixGeneration: prefixGeneration);
                MountIslandAdapterRouteFence.ApplyRevocation(Gate, fence);
                return false;
            }
        }

        /// <summary>
        /// 构造一次 attempt 的 route fence（统一撤销策略；Observer 各 prefix/finalizer/error 入口共用）。
        /// instance/请求字段读取失败 → Unfenced（禁撤）；prefix 之后已发生新 seal/撤销 → Stale（禁撤）。
        /// </summary>
        private static MountIslandRouteFence EvaluateAttempt(IslandSaveData.__TryLoad_d__54 instance,
            bool snapshotOk, long prefixGeneration)
        {
            try
            {
                if (instance == null) return MountIslandRouteFence.Unfenced;
                bool sealedNow = Gate.EnvelopeSealed;
                MountIslandRequestEnvelope envelope = sealedNow ? Gate.Envelope : default;
                return MountIslandAdapterRouteFence.Evaluate(
                    sealedNow, envelope.RoutinePointer,
                    envelope.RequestCampaignIndex, envelope.RequestChallengeId, envelope.RequestLand,
                    attemptSnapshotOk: snapshotOk, attemptRoutine: PointerOf(instance),
                    attemptCampaignIndex: instance.campaignIndex,
                    attemptChallengeId: instance.challengeId,
                    attemptLand: instance.land,
                    attemptGeneration: prefixGeneration, liveGeneration: Gate.Generation);
            }
            catch (Exception)
            {
                return MountIslandRouteFence.Unfenced;
            }
        }

        /// <summary>finalizer 唯一完成点（无外层异常）：先 advance 判定，再按分支处理。</summary>
        internal static void OnFinalized(IslandSaveData.__TryLoad_d__54 instance, MoveNextObservation observation)
        {
            try
            {
                if (instance == null || observation == null) return;
                if (observation.PreState == -1)
                {
                    LogOnce("closed-repeat", "repeated closed (state -1) call ignored; current envelope untouched");
                    return;
                }

                int postState;
                bool postOk;
                try
                {
                    postState = instance.__1__state;
                    postOk = true;
                }
                catch (Exception)
                {
                    postState = -99;
                    postOk = false;
                }

                bool envelopeSealed = Gate.EnvelopeSealed && Gate.State != MountIslandEnvelopeState.Closed;
                bool sameRoutine = false;
                bool sameRequest = false;
                if (envelopeSealed)
                {
                    try
                    {
                        MountIslandRequestEnvelope envelope = Gate.Envelope;
                        sameRoutine = envelope.RoutinePointer == PointerOf(instance);
                        if (sameRoutine)
                            sameRequest = envelope.RequestCampaignIndex == instance.campaignIndex
                                && envelope.RequestChallengeId == instance.challengeId
                                && envelope.RequestLand == instance.land;
                    }
                    catch (Exception)
                    {
                        sameRoutine = false;
                        sameRequest = false;
                    }
                }

                MountIslandAdvanceOutcome outcome = MountIslandAdvanceClassifier.Classify(new MountIslandAdvanceInput
                {
                    PreState = observation.PreState,
                    RunOriginal = observation.RunOriginal,
                    PostfixRan = observation.PostfixRan,
                    Result = observation.Result,
                    PostState = postOk ? postState : -99,
                    EnvelopeSealed = envelopeSealed,
                    SameRoutine = sameRoutine,
                    SameRequest = sameRequest,
                });

                if (outcome == MountIslandAdvanceOutcome.Ignore)
                {
                    LogOnce("advance-ignore", "advance Ignore: closed repeat call; no gate change");
                    return;
                }

                // 统一 route fence：仅属于当前 envelope 的 own/mutated attempt 才撤；
                // foreign/stale/unfenced/无 envelope 一律 no-op（外来调用不毁另一新请求）。
                MountIslandRouteFence fence = EvaluateAttempt(instance, snapshotOk: observation.SnapshotReadOk,
                    prefixGeneration: observation.PrefixGeneration);

                if (outcome == MountIslandAdvanceOutcome.Continue)
                {
                    LogOnce("advance-yield-" + observation.PreState,
                        "advance Continue: normal yield (postState=" + postState + "); Started preserved, flags not read");
                    return;
                }
                if (outcome == MountIslandAdvanceOutcome.Abort)
                {
                    MountIslandAdapterRouteFence.ApplyRevocation(Gate, fence);
                    LogError("advance Abort (mutated/skipped/invalid postState/no envelope; preState="
                        + observation.PreState + " postState=" + (postOk ? postState.ToString() : "read-fault")
                        + " fence=" + fence + ")");
                    return;
                }

                // Terminal：真实终末（false + -1 + 活动恢复 + 同 envelope）才读 flags。
                CompleteTerminal(instance, observation, flagsSource: true);
            }
            catch (Exception e)
            {
                MountIslandRouteFence fence = EvaluateAttempt(instance, snapshotOk: observation != null && observation.SnapshotReadOk,
                    prefixGeneration: observation != null ? observation.PrefixGeneration : -1);
                MountIslandAdapterRouteFence.ApplyRevocation(Gate, fence);
                LogError("finalized observation failed (fence=" + fence + "): " + e.GetType().Name + " " + e.Message);
            }
        }

        private static void CompleteTerminal(IslandSaveData.__TryLoad_d__54 instance, MoveNextObservation observation, bool flagsSource)
        {
            int flags;
            bool flagsReadOk;
            try
            {
                flags = (int)instance.@return.value;
                flagsReadOk = true;
            }
            catch (Exception)
            {
                flags = 0;
                flagsReadOk = false;
            }

            var completion = new MountIslandLoadCompletion
            {
                FlagsReadOk = flagsReadOk,
                RawFlags = flags,
                ActiveResume = observation.PreState == 1 || observation.PreState == 2,
                RunOriginal = observation.RunOriginal,
                CompletedNormally = observation.PostfixRan && !observation.Result,
                PostState = observation.PreState == 1 || observation.PreState == 2 ? -1 : 0,
            };
            MountIslandCompletionClass classification = MountIslandCompletionClassifier.Classify(completion);
            if (classification != MountIslandCompletionClass.MissingData)
            {
                RevokeTerminalScope(instance, observation);
                LogOnce("closed-terminal-" + observation.PreState + "-" + (flagsReadOk ? flags : -1),
                    "terminal classified " + classification + " (flags="
                    + (flagsReadOk ? "0x" + flags.ToString("X") : "unreadable")
                    + "); active request revoked, no fresh candidate");
                return;
            }

            if (Gate.State != MountIslandEnvelopeState.Started || !Gate.EnvelopeSealed)
            {
                LogOnce("terminal-not-started", "terminal MissingData but envelope is not Started; no publish");
                return;
            }
            MountIslandRequestEnvelope envelope = Gate.Envelope;
            long expectedGeneration = Gate.Generation;   // live 读取（不回填 ticket/envelope 旧值）
            MountIslandObservationStamp expected = NewOwnerStamp(envelope.Owner, envelope.RoutinePointer,
                envelope.RequestCampaignIndex, envelope.RequestChallengeId, envelope.RequestLand, expectedGeneration);

            // stamp 闭合：before（owner+请求+holder+live gen）→ 一次 history → after；紧邻 publish。
            if (!TryReadInstanceStamp(instance, out MountIslandObservationStamp before, out string beforeReason)
                || !MountIslandStampMatch.Matches(before, expected))
            {
                RevokeTerminalScope(instance, observation);
                LogError("terminal before-stamp mismatch (live gen=" + (beforeReason == "ok" ? before.Generation.ToString() : beforeReason)
                    + ", expected=" + expectedGeneration + "); no candidate");
                return;
            }
            if (!TryReadHistory(envelope.RequestLand, out MountIslandHistoryView post))
            {
                RevokeTerminalScope(instance, observation);
                LogError("terminal history unreadable; active request revoked, no candidate");
                return;
            }
            if (!TryReadInstanceStamp(instance, out MountIslandObservationStamp after, out string afterReason)
                || !MountIslandStampMatch.Matches(before, after))
            {
                RevokeTerminalScope(instance, observation);
                LogError("terminal after-stamp mismatch (live gen=" + after.Generation + " vs " + before.Generation
                    + ", " + afterReason + "); no candidate");
                return;
            }

            if (envelope.PreHistoryAnyPositive || post.AnyPositive)
            {
                RevokeTerminalScope(instance, observation);
                Log("terminal revoked: prior/post generation evidence present (pre="
                    + envelope.PreHistoryAnyPositive + " post=" + post.AnyPositive + ")");
                return;
            }
            if (envelope.PreSlotPointer != 0UL && post.SlotPointer != envelope.PreSlotPointer)
            {
                RevokeTerminalScope(instance, observation);
                LogError("terminal revoked: target raw slot unexpectedly replaced (pre=0x"
                    + envelope.PreSlotPointer.ToString("X") + " post=0x" + post.SlotPointer.ToString("X") + ")");
                return;
            }
            if (post.SlotPointer == 0UL)
            {
                RevokeTerminalScope(instance, observation);
                Log("terminal MissingData with zero post slot stays Unknown (no wildcard)");
                return;
            }

            var draft = new MountIslandTicketDraft
            {
                CompletionClass = classification,
                RawFlags = flags,
                Owner = after.Owner,
                RoutinePointer = envelope.RoutinePointer,
                RequestCampaignIndex = envelope.RequestCampaignIndex,
                RequestChallengeId = envelope.RequestChallengeId,
                RequestLand = envelope.RequestLand,
                HistoryReadsOk = post.ReadsOk,
                HistoryAnyPositive = post.AnyPositive,
                SlotPointer = post.SlotPointer,
            };
            if (Gate.TryPublish(draft))
                Log("native missing-record fresh candidate captured (CompletedOnce): " + DescribeDraft(draft));
            else
            {
                RevokeTerminalScope(instance, observation);
                LogOnce("publish-rejected-" + envelope.RequestLand,
                    "terminal MissingData did not publish (gen=" + Gate.Generation + "); active request revoked");
            }
        }

                /// <summary>
        /// terminal 分支统一撤销：按本 attempt 的 issued generation 与当前 live 代次评估 route fence
        /// （foreign/stale/unfenced 一律 no-op；own-current negative/success 撤本自己，不产生 fresh）。
        /// </summary>
        private static void RevokeTerminalScope(IslandSaveData.__TryLoad_d__54 instance, MoveNextObservation observation)
        {
            MountIslandRouteFence fence = EvaluateAttempt(instance,
                snapshotOk: observation != null && observation.SnapshotReadOk,
                prefixGeneration: observation != null ? observation.PrefixGeneration : -1);
            MountIslandAdapterRouteFence.ApplyRevocation(Gate, fence);
        }

internal static void OnAborted(IslandSaveData.__TryLoad_d__54 instance, MoveNextObservation observation, Exception exception)
        {
            MountIslandRouteFence fence = EvaluateAttempt(instance,
                snapshotOk: observation != null && observation.SnapshotReadOk,
                prefixGeneration: observation != null ? observation.PrefixGeneration : -1);
            MountIslandAdapterRouteFence.ApplyRevocation(Gate, fence);
            if (MountIslandAdapterRouteFence.CanRevoke(fence))
                LogError("TryLoad MoveNext threw (" + exception.GetType().Name + "); fenced revocation applied (fence=" + fence + ")");
            else
                LogError("TryLoad MoveNext threw (" + exception.GetType().Name + "); attempt not fenced (fence=" + fence
                    + "); new current request preserved, this attempt produces no fresh");
        }

        // ------------------------------------------------------------------ frame 侧

        /// <summary>GetBlocks/Clone/Close 的 ticket 来源段：Usable + 代次 + land，sticky。</summary>
        private static bool TryBeginTicketPath(CrossWorldMountRuntime.Frame frame, int land, out MountIslandFrameTicket ticket, out string reason)
        {
            ticket = frame != null ? frame.FreshTicket : null;
            reason = "no fresh candidate ticket for this frame";
            if (ticket == null) return false;
            if (!ticket.Usable) { reason = "ticket consumed/invalidated (sticky)"; return false; }
            if (ticket.IssuedGeneration != Gate.Generation)
            {
                ticket.InvalidateAgainst(Gate);
                reason = "ticket generation superseded";
                return false;
            }
            if (land >= 0 && land != ticket.RequestLand)
            {
                ticket.InvalidateAgainst(Gate);
                reason = "ticket land mismatch (ticket=" + ticket.RequestLand + " request=" + land + ")";
                return false;
            }
            return true;
        }

        /// <summary>GenerateInternal prefix 的 claim：stamp 闭合 + 五条件，失败撤销，成功单次转交。</summary>
        internal static MountIslandFrameTicket ClaimForFrame(CrossWorldMountRuntime.Frame frame)
        {
            try
            {
                if (frame == null || frame.Config == null) return null;
                if (!Gate.TryPeekPending(out MountIslandTicketDraft draft))
                {
                    LogOnce("claim-none", "no pending fresh candidate at generation open (frame holds Unknown)");
                    return null;
                }

                MountIslandObservationStamp expected = NewOwnerStamp(draft.Owner, draft.RoutinePointer,
                    draft.RequestCampaignIndex, draft.RequestChallengeId, draft.RequestLand, draft.IssuedGeneration);
                bool factsOk = TryReadStampedFacts(frame.Config, draft.RequestLand, expected, FactKind.History,
                    out MountIslandHistoryView history, out ulong slotPointer, out bool ownedConfig, out string factsReason);
                bool generationMatches = draft.IssuedGeneration == Gate.Generation;
                bool targetLevelCaptured = frame.TargetLevelPointer != 0UL;
                bool slotPolicyOk = factsOk && history.ReadsOk && !history.AnyPositive
                    && draft.SlotPointer != 0UL && slotPointer == draft.SlotPointer;

                if (!factsOk
                    || !MountIslandClaimEvaluator.Evaluate(draft, generationMatches, identityMatches: true,
                        ownedConfigConfirmed: ownedConfig, targetLevelCaptured: targetLevelCaptured, slotPolicyOk: slotPolicyOk))
                {
                    RevokeDraftScope(draft);
                    LogError("pending fresh candidate dropped at claim (generation=" + generationMatches
                        + " ownedConfig=" + ownedConfig + " targetLevel=" + targetLevelCaptured
                        + " slot=" + slotPolicyOk + " facts=" + factsOk + " [" + factsReason + "]); active request revoked");
                    return null;
                }
                if (draft.IssuedGeneration != Gate.Generation)   // take 前再核 live 代次
                {
                    LogError("pending fresh candidate dropped: generation changed immediately before take (newer gate preserved)");
                    return null;
                }

                MountIslandFrameTicket ticket = Gate.TakeFrameTicket();
                if (ticket != null)
                    Log("fresh candidate transferred to generation frame (Transferred): " + ticket.Describe());
                return ticket;
            }
            catch (Exception e)
            {
                LogError("claim failed: " + e.GetType().Name + " " + e.Message);
                return null;
            }
        }

        /// <summary>claim 拒绝的撤销只作用于仍属该 draft 代次的请求，绝不误撤已出现的新 gate。</summary>
        private static void RevokeDraftScope(MountIslandTicketDraft draft)
        {
            if (draft.IssuedGeneration == Gate.Generation) Gate.Revoke();
        }

        /// <summary>GetBlocks 授权复核（读已存在历史反证）。任何 unknown/拒绝 → sticky 失效。</summary>
        internal static bool IsGrantAuthorized(CrossWorldMountRuntime.Frame frame, int land, out string reason)
        {
            return Recheck(frame, land, frame != null && frame.TargetLevelPointer != 0UL, out reason);
        }

        /// <summary>CloneInto 放置记录前的复核（含目标 Level 指针精确匹配）。</summary>
        internal static bool IsPlacementAuthorized(CrossWorldMountRuntime.Frame frame, Level level, out string reason)
        {
            return Recheck(frame, -1, frame != null && frame.TargetLevelPointer != 0UL
                && PointerOf(level) == frame.TargetLevelPointer, out reason);
        }

        /// <summary>Close 的**一次** batch fresh 复核（在任何本帧 marker 写入之前）。</summary>
        internal static bool BeginCommitBatch(CrossWorldMountRuntime.Frame frame, out string reason)
        {
            return Recheck(frame, -1, frame != null && frame.LevelPointer != 0UL
                && frame.LevelPointer == frame.TargetLevelPointer, out reason);
        }

        /// <summary>GetBlocks/Clone/Close 通用复核：stamp 闭合 + 单次 history；拒绝即 sticky。</summary>
        private static bool Recheck(CrossWorldMountRuntime.Frame frame, int land, bool levelMatches, out string reason)
        {
            if (!TryBeginTicketPath(frame, land, out MountIslandFrameTicket ticket, out reason)) return false;
            try
            {
                MountIslandObservationStamp expected = NewOwnerStamp(ticket.Owner, ticket.RoutinePointer,
                    ticket.RequestCampaignIndex, ticket.RequestChallengeId, ticket.RequestLand, ticket.IssuedGeneration);
                if (!TryReadStampedFacts(frame.Config, ticket.RequestLand, expected, FactKind.History,
                        out MountIslandHistoryView history, out ulong slotPointer, out bool ownedConfig, out string factsReason))
                {
                    ticket.InvalidateAgainst(Gate);
                    reason = "stamped facts unavailable: " + factsReason;
                    return false;
                }

                bool slotMatches = ticket.SlotPointer != 0UL && slotPointer == ticket.SlotPointer;
                bool configMatches = frame.Config != null && frame.ConfigPointer != 0UL;
                bool generationMatches = ticket.IssuedGeneration == Gate.Generation;   // live（facts 之后）
                MountIslandGrantAdmission admission = MountIslandGrantAdmissionEvaluator.EvaluateFrameRecheck(
                    ticket, generationMatches: generationMatches, ownerMatches: true, levelMatches: levelMatches,
                    configMatches: configMatches, ownedConfigConfirmed: ownedConfig,
                    historyReadsOk: history.ReadsOk, historyAnyPositive: history.AnyPositive, slotMatches: slotMatches);

                if (admission != MountIslandGrantAdmission.Fresh)
                {
                    ticket.InvalidateAgainst(Gate);
                    reason = "fresh recheck denied (admission=" + admission
                        + ", slotMatches=" + slotMatches + ", ownedConfig=" + ownedConfig
                        + ", generationMatches=" + generationMatches
                        + ", historyReadsOk=" + history.ReadsOk
                        + (history.ReadsOk ? ", historyPositive=" + history.AnyPositive : "")
                        + ", levelMatches=" + levelMatches + ")";
                    return false;
                }
                if (ticket.IssuedGeneration != Gate.Generation)
                {
                    ticket.InvalidateAgainst(Gate);
                    reason = "generation changed immediately before confirm";
                    return false;
                }

                reason = "fresh candidate confirmed (" + ticket.Describe() + ", slot=0x" + slotPointer.ToString("X") + ")";
                return true;
            }
            catch (Exception e)
            {
                ticket.InvalidateAgainst(Gate);
                reason = "recheck failed (sticky): " + e.GetType().Name + " " + e.Message;
                return false;
            }
        }

        /// <summary>
        /// 提交批量逐条 binding：stamp 闭合的 slot 指针 + config 事实（**不读 history**），
        /// 本帧 own marker 写入不影响判定；任何拒绝 sticky 失效。
        /// </summary>
        internal static bool IsCommitBindingAuthorized(CrossWorldMountRuntime.Frame frame, int land, out string reason)
        {
            if (!TryBeginTicketPath(frame, land, out MountIslandFrameTicket ticket, out reason)) return false;
            try
            {
                MountIslandObservationStamp expected = NewOwnerStamp(ticket.Owner, ticket.RoutinePointer,
                    ticket.RequestCampaignIndex, ticket.RequestChallengeId, ticket.RequestLand, ticket.IssuedGeneration);
                if (!TryReadStampedFacts(frame.Config, ticket.RequestLand, expected, FactKind.SlotOnly,
                        out _, out ulong slotNow, out bool ownedConfig, out string factsReason))
                {
                    ticket.InvalidateAgainst(Gate);
                    reason = "stamped slot/config facts unavailable: " + factsReason;
                    return false;
                }

                if (frame.LevelPointer == 0UL || frame.LevelPointer != frame.TargetLevelPointer)
                {
                    ticket.InvalidateAgainst(Gate);
                    reason = "target level identity mismatch";
                    return false;
                }
                if (frame.Config == null || frame.ConfigPointer == 0UL || !ownedConfig)
                {
                    ticket.InvalidateAgainst(Gate);
                    reason = "target config is not the exact private owned config";
                    return false;
                }
                if (ticket.SlotPointer == 0UL || slotNow != ticket.SlotPointer)
                {
                    ticket.InvalidateAgainst(Gate);
                    reason = "frozen slot pointer replaced/missing at commit binding";
                    return false;
                }
                if (ticket.IssuedGeneration != Gate.Generation)   // binding ok 前再核 live 代次
                {
                    ticket.InvalidateAgainst(Gate);
                    reason = "generation changed immediately before binding confirm";
                    return false;
                }
                reason = "binding ok (" + ticket.Describe() + ")";
                return true;
            }
            catch (Exception e)
            {
                ticket.InvalidateAgainst(Gate);
                reason = "commit binding failed (sticky): " + e.GetType().Name + " " + e.Message;
                return false;
            }
        }

        /// <summary>批量写入之后的 owner/land 终核（不回溯已写 marker；失败 sticky）。</summary>
        internal static bool EndCommitBatch(CrossWorldMountRuntime.Frame frame, out string reason)
        {
            reason = "unknown";
            try
            {
                MountIslandFrameTicket ticket = frame != null ? frame.FreshTicket : null;
                if (ticket == null) { reason = "no ticket"; return true; }
                if (TryReadOwnerIdentitySingle(ticket.RequestLand, out MountIslandOwnerIdentity now)
                    && MountIslandOwnerIdentity.Matches(ticket.Owner, now))
                {
                    reason = "ok";
                    return true;
                }
                ticket.InvalidateAgainst(Gate);
                reason = "owner/currentLand changed during marker batch (already-written markers cannot be reverted)";
                return false;
            }
            catch (Exception e)
            {
                reason = e.GetType().Name;
                return false;
            }
        }

        internal static void OnFrameClosed(CrossWorldMountRuntime.Frame frame)
        {
            try
            {
                MountIslandFrameTicket ticket = frame != null ? frame.FreshTicket : null;
                if (ticket == null) return;
                ticket.Consume();
                LogOnce("frame-closed-" + ticket.RequestLand,
                    "frame closed; fresh candidate ticket consumed/discarded (land=" + ticket.RequestLand + ")");
            }
            catch (Exception)
            {
            }
        }

        // ------------------------------------------------------------------ stamp 闭合 / typed 读取

        private static bool TryReadStampedFacts(LevelConfig config, int land, MountIslandObservationStamp expected,
            FactKind kind, out MountIslandHistoryView history, out ulong slotPointer, out bool ownedConfig, out string reason)
        {
            history = default;
            slotPointer = 0UL;
            ownedConfig = false;
            reason = "unknown";

            if (!TryReadOwnerStamp(land, expected.RoutinePointer, expected.RequestCampaignIndex,
                    expected.RequestChallengeId, out MountIslandObservationStamp before, out string beforeReason))
            {
                reason = "before stamp: " + beforeReason;
                return false;
            }
            if (!MountIslandStampMatch.Matches(before, expected))
            {
                reason = "before stamp != expected (owner/request/holder/gen; live gen=" + before.Generation
                    + " expected=" + expected.Generation + ")";
                return false;
            }

            // 唯一一次 facts 读取（history 或 slot 指针）+ config 事实，随后立即 after stamp（再次 live gen）。
            if (kind == FactKind.History)
            {
                if (!TryReadHistory(land, out history)) { reason = "history unreadable"; return false; }
                slotPointer = history.SlotPointer;
            }
            else
            {
                if (!TryReadSlotPointerOnly(land, out slotPointer)) { reason = "slot pointer unreadable"; return false; }
            }
            ownedConfig = ExtensionIslandRuntime.IsOwnedConfig(config, land);

            if (!TryReadOwnerStamp(land, expected.RoutinePointer, expected.RequestCampaignIndex,
                    expected.RequestChallengeId, out MountIslandObservationStamp after, out string afterReason))
            {
                reason = "after stamp: " + afterReason;
                return false;
            }
            if (!MountIslandStampMatch.Matches(before, after))
            {
                reason = "stamp changed across facts read (live gen=" + after.Generation + " vs " + before.Generation
                    + "; holder/global/selected/currentLand/container)";
                return false;
            }
            if (after.Generation != expected.Generation)
            {
                reason = "generation changed across facts read (live=" + after.Generation
                    + " expected=" + expected.Generation + ")";
                return false;
            }
            reason = "ok";
            return true;
        }

        private static MountIslandObservationStamp NewOwnerStamp(MountIslandOwnerIdentity owner, ulong routine,
            int campaignIndex, int challengeId, int land, long generation)
        {
            return new MountIslandObservationStamp
            {
                Owner = owner,
                RoutinePointer = routine,
                RequestCampaignIndex = campaignIndex,
                RequestChallengeId = challengeId,
                RequestLand = land,
                Generation = generation,
            };
        }

        /// <summary>
        /// 单次 owner 读 + 传入 immutable fields 组成 stamp；**generation 取 live Gate.Generation**
        /// （绝不回填 expected 值），因此 facts 读取期间的新 seal/撤销会体现在 after stamp 上。
        /// </summary>
        private static bool TryReadOwnerStamp(int land, ulong routine, int campaignIndex, int challengeId,
            out MountIslandObservationStamp stamp, out string reason)
        {
            stamp = default;
            reason = "owner unreadable";
            if (!TryReadOwnerIdentitySingle(land, out MountIslandOwnerIdentity owner)) return false;
            stamp = NewOwnerStamp(owner, routine, campaignIndex, challengeId, land, Gate.Generation);
            reason = "ok";
            return true;
        }

        /// <summary>completion 用：读实例请求 immutable + owner + **live Gate.Generation** 组成 stamp。</summary>
        private static bool TryReadInstanceStamp(IslandSaveData.__TryLoad_d__54 instance,
            out MountIslandObservationStamp stamp, out string reason)
        {
            stamp = default;
            reason = "instance stamp unreadable";
            try
            {
                int campaignIndex = instance.campaignIndex;
                int challengeId = instance.challengeId;
                int land = instance.land;
                if (!TryReadOwnerIdentitySingle(land, out MountIslandOwnerIdentity owner)) return false;
                stamp = NewOwnerStamp(owner, PointerOf(instance), campaignIndex, challengeId, land, Gate.Generation);
                reason = "ok";
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryBuildEnvelope(IslandSaveData.__TryLoad_d__54 instance,
            out MountIslandRequestEnvelope envelope, out string detail)
        {
            envelope = default;
            detail = "unreadable";
            try
            {
                int campaignIndex = instance.campaignIndex;
                int challengeId = instance.challengeId;
                int land = instance.land;
                if (challengeId != 0) { detail = "challenge != 0"; return false; }
                if (!MountIslandSplitPolicy.IsExtensionLand(land)) { detail = "not extension land " + land; return false; }
                GlobalSaveData global = GlobalSaveData.loaded;
                if (global == null) { detail = "loaded global missing"; return false; }
                if (global.InChallenge) { detail = "in-challenge"; return false; }
                if (NetworkBigBoss.IsOnline) { detail = "online"; return false; }
                if (IslandSaveData.isSavingGame) { detail = "isSavingGame"; return false; }

                // stamp 闭合：before → 一次 history（前像）→ after（两侧都读 live generation）。
                ulong routine = PointerOf(instance);
                if (!TryReadOwnerStamp(land, routine, campaignIndex, challengeId, out MountIslandObservationStamp before, out string beforeReason))
                {
                    detail = "owner identity unreadable (" + beforeReason + ")";
                    return false;
                }
                if (!TryReadHistory(land, out MountIslandHistoryView pre)) { detail = "pre history unreadable"; return false; }
                if (!TryReadOwnerStamp(land, routine, campaignIndex, challengeId, out MountIslandObservationStamp after, out string afterReason)
                    || !MountIslandStampMatch.Matches(before, after))
                {
                    detail = "stamp changed during pre-image read (live gen=" + after.Generation
                        + " vs " + before.Generation + ")";
                    return false;
                }
                MountIslandOwnerIdentity owner = after.Owner;
                if (owner.SelectedCampaignIndex != campaignIndex) { detail = "request campaignIndex != raw selected index"; return false; }
                if (owner.HolderBiomeIndex != BiomeHolder.GreeceBiomeIndex) { detail = "not greek biome"; return false; }

                envelope = new MountIslandRequestEnvelope
                {
                    ScopeOk = true,
                    RoutinePointer = routine,
                    RequestCampaignIndex = campaignIndex,
                    RequestChallengeId = challengeId,
                    RequestLand = land,
                    Owner = owner,
                    PreSlotPresent = pre.SlotPresent,
                    PreSlotPointer = pre.SlotPointer,
                    PreHistoryReadsOk = pre.ReadsOk,
                    PreHistoryAnyPositive = pre.AnyPositive,
                };
                detail = "ok";
                return true;
            }
            catch (Exception e)
            {
                detail = "envelope read fault: " + e.GetType().Name;
                return false;
            }
        }

        /// <summary>单次 owner 读（真实 <c>campaign.CurrentLand</c> 必须 == 请求 land，不回填参数）。</summary>
        private static bool TryReadOwnerIdentitySingle(int land, out MountIslandOwnerIdentity identity)
        {
            identity = default;
            try
            {
                if (land < 0) return false;
                // runtime 已发布 raw owner helper（无 GetCurrentCampaign fallback）：
                //   ExtensionIslandRuntime.TryResolveRawOwner(out GlobalSaveData global, out int selectedIndex, out CampaignSaveData owner)
                if (!ExtensionIslandRuntime.TryResolveRawOwner(out GlobalSaveData global, out int selected, out CampaignSaveData current))
                    return false;

                CampaignSaveData.ReignInfo reign = current.currentReign;
                Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData =
                    reign != null ? reign.landData : null;
                Il2CppSystem.Collections.Generic.List<IslandSaveData> islands = current._islands;
                BiomeHolder holder = BiomeHolder.Inst;

                identity = new MountIslandOwnerIdentity
                {
                    GlobalPointer = PointerOf(global),
                    SelectedCampaignIndex = selected,
                    CampaignPointer = PointerOf(current),
                    ReignIndex = current.reign,
                    LandDataPointer = landData != null ? CrossWorldMountDependencies.PointerOfList(landData) : 0UL,
                    IslandsPointer = PointerOf(islands),
                    HolderInstancePointer = PointerOf(holder),
                    HolderBiomeIndex = holder != null ? holder.BiomeIndex : -1,
                    CurrentLand = current.CurrentLand,
                };
                if (identity.CurrentLand != land) return false;
                return identity.IsComplete;
            }
            catch (Exception e)
            {
                LogOnce("identity-" + e.GetType().Name, "owner identity read failed: " + e.Message);
                identity = default;
                return false;
            }
        }

        /// <summary>raw <c>_islands</c> 槽只读（不 GetIsland）；结构/played 不可读 → false（unknown）。</summary>
        private static bool TryReadHistory(int land, out MountIslandHistoryView view)
        {
            view = default;
            try
            {
                if (land < 0) return false;
                CampaignSaveData campaign = CampaignSaveData.current;
                if (campaign == null) return false;
                Il2CppSystem.Collections.Generic.List<IslandSaveData> islands = campaign._islands;
                if (islands == null) return false;
                Il2CppSystem.Collections.Generic.List<int> visited = campaign.visitedIslands;
                if (visited == null) return false;
                CampaignSaveData.ReignInfo reign = campaign.currentReign;
                if (reign == null) return false;
                Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData = reign.landData;
                if (landData == null) return false;

                bool slotPresent = false;
                bool anyPositive = false;
                ulong slotPointer = 0UL;
                if (land < islands.Count)
                {
                    IslandSaveData slot = islands[land];
                    if (slot != null)
                    {
                        slotPresent = true;
                        slotPointer = PointerOf(slot);

                        double playDays = slot.playTimeDays;
                        double lastPlayed = slot.lastPlayedTimeDays;
                        if (BadDays(playDays) || BadDays(lastPlayed)) return false;
                        if (playDays > 0d || lastPlayed > 0d) anyPositive = true;

                        Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> objects = slot.objects;
                        if (objects != null && objects.Count > 0) anyPositive = true;
                        Il2CppSystem.Collections.Generic.List<int> revisions = slot.savedWithRevisions;
                        if (revisions != null && revisions.Count > 0) anyPositive = true;
                    }
                }

                if (visited.Contains(land)) anyPositive = true;

                if (land < landData.Count)
                {
                    CampaignSaveData.LandMapData entry = landData[land];
                    if (entry != null)
                    {
                        double mapDays = entry.lastPlayedTimeDays;
                        if (BadDays(mapDays)) return false;
                        if (mapDays > 0d) anyPositive = true;
                        Il2CppStructArray<SteedType> spawns = entry.steedSpawns;
                        if (spawns != null && spawns.Length > 0) anyPositive = true;
                    }
                }

                view = MountIslandHistoryView.Create(true, anyPositive, slotPresent, slotPointer);
                return true;
            }
            catch (Exception e)
            {
                LogOnce("history-" + land + "-" + e.GetType().Name,
                    "island history read failed (land " + land + "): " + e.Message);
                view = default;
                return false;
            }
        }

        /// <summary>只读 raw slot 指针（不读 objects/played/revision/visited/landData）：commit binding 用。</summary>
        private static bool TryReadSlotPointerOnly(int land, out ulong pointer)
        {
            pointer = 0UL;
            try
            {
                if (land < 0) return false;
                CampaignSaveData campaign = CampaignSaveData.current;
                if (campaign == null) return false;
                Il2CppSystem.Collections.Generic.List<IslandSaveData> islands = campaign._islands;
                if (islands == null) return false;
                if (land >= islands.Count) return true;
                IslandSaveData slot = islands[land];
                pointer = PointerOf(slot);
                return true;
            }
            catch (Exception)
            {
                pointer = 0UL;
                return false;
            }
        }

        private static bool BadDays(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0d;
        }

        private static ulong PointerOf(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase value)
        {
            try { return value != null ? (ulong)value.Pointer.ToInt64() : 0UL; }
            catch (Exception) { return 0UL; }
        }

        private static string DescribeDraft(MountIslandTicketDraft draft)
        {
            return "class=" + draft.CompletionClass + " flags=0x" + draft.RawFlags.ToString("X")
                + " campaignIndex=" + draft.RequestCampaignIndex + " challenge=" + draft.RequestChallengeId
                + " land=" + draft.RequestLand + " currentLand=" + draft.Owner.CurrentLand
                + " slot=0x" + draft.SlotPointer.ToString("X") + " gen=" + draft.IssuedGeneration;
        }

        // ------------------------------------------------------------------ logging

        private static void Log(string message)
        {
            try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(LogPrefix + " " + message); }
            catch (Exception) { }
        }

        private static void LogOnce(string key, string message)
        {
            try
            {
                if (!LoggedKeys.Add(key)) return;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(LogPrefix + " " + message);
            }
            catch (Exception) { }
        }

        private static void LogError(string message)
        {
            try { KingdomEnhancedPlugin.Instance?.LogSource.LogError(LogPrefix + " " + message); }
            catch (Exception) { }
        }
    }

    /// <summary>prefix/postfix/finalizer 共享的一次调用状态（只读快照，不写原字段）。</summary>
    internal sealed class MoveNextObservation
    {
        internal bool SnapshotReadOk;
        internal bool EnvelopeSealed;
        internal int PreState;
        internal int RequestCampaignIndex;
        internal int RequestChallengeId;
        internal int RequestLand;
        internal bool RunOriginal;
        internal bool PostfixRan;
        internal bool Result;
        internal bool Finalized;
        internal long PrefixGeneration = -1;   // prefix 时刻的 gate 代次（route fence 用）
    }

    /// <summary>
    /// 只读 endpoint observer：<c>IslandSaveData.&lt;_TryLoad&gt;d__54.MoveNext</c>（attrs 已写、**未注册**）。
    ///
    /// 注册边界：**root 审查 + endpoint 独立审验（typed target、实际运行原执行透明性、Native 地址
    /// alias/owner/桥接核验）通过后**才可加入注册；本文件不注册、不写安装。静态依据：token 0x06003abd /
    /// ARM 0x892aec / 3284 bytes / LC 下一界 0x8937c0（native-capacity-audit/tryload54-exact.json）。
    /// </summary>
    [HarmonyPatch(typeof(IslandSaveData.__TryLoad_d__54), "MoveNext")]
    internal static class Patch_MountIslandFreshObserver
    {
        [HarmonyPrefix]
        private static void Prefix(IslandSaveData.__TryLoad_d__54 __instance, bool __runOriginal, out MoveNextObservation __state)
        {
            var observation = new MoveNextObservation();
            __state = observation;
            try
            {
                if (__instance == null) return;
                observation.PreState = __instance.__1__state;
                observation.RequestCampaignIndex = __instance.campaignIndex;
                observation.RequestChallengeId = __instance.challengeId;
                observation.RequestLand = __instance.land;
                observation.RunOriginal = __runOriginal;
                observation.SnapshotReadOk = true;
                long preGeneration = MountIslandFreshObservation.Gate.Generation;
                observation.PrefixGeneration = preGeneration;
                observation.EnvelopeSealed = MountIslandFreshObservation.OnLoadStart(__instance, preGeneration,
                    out long issuedGeneration, out _);
                if (observation.EnvelopeSealed && issuedGeneration >= 0)
                    observation.PrefixGeneration = issuedGeneration;   // 本次成功 seal 的 issued generation
            }
            catch (Exception)
            {
                observation.SnapshotReadOk = false;
            }
        }

        [HarmonyPostfix]
        private static void Postfix(bool __result, bool __runOriginal, MoveNextObservation __state)
        {
            try
            {
                if (__state == null) return;
                __state.PostfixRan = true;
                __state.Result = __result;
                __state.RunOriginal = __runOriginal;
            }
            catch (Exception)
            {
            }
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, IslandSaveData.__TryLoad_d__54 __instance, MoveNextObservation __state)
        {
            try
            {
                if (__state == null || __state.Finalized) return __exception;
                __state.Finalized = true;
                if (__exception != null)
                    MountIslandFreshObservation.OnAborted(__instance, __state, __exception);
                else
                    MountIslandFreshObservation.OnFinalized(__instance, __state);
            }
            catch (Exception)
            {
            }
            return __exception;
        }
    }
}
