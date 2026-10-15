using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// issue-200 双岛 fresh 观察门的**纯证据层**（private proposal，root 审查前不得进入 tracked 主线）。
    ///
    /// 不引用 Unity / Il2Cpp / Harmony；land 判定直接调用 tracked <see cref="MountIslandSplitPolicy"/>。
    ///
    /// 责任（补齐原 pre/post 责任，不改变 Native 权限）：
    /// - envelope 生命周期 <see cref="MountIslandEnvelopeState"/>：state0 **Started**（封印请求前像，含
    ///   owner 身份/前像 slot/历史正证快照）→ 完成 publish 后 **CompletedOnce**（一张候选）→
    ///   **Transferred**（单次转交 frame）或 **Closed**（撤销）。TryPublish 只接受 Started，
    ///   因此同 envelope 不可能二次 publish；无 seal 的 draft 不被当作新请求。
    /// - 完成后候选必须：非零 completion 后 slot（零 completion 保持 Unknown，不允许任何后来
    ///   known-empty pointer 承接）；pre/post 历史正证 OR 为假；同 routine/请求/身份。
    /// - 提交批量（<see cref="MountIslandCommitSession"/>）：Close 在**任何本帧 own marker 写入之前**
    ///   做一次 fresh 历史/身份复核；此后逐条只做 binding/level/config/slot/owner 复核（不读 island
    ///   history），因此本帧自己写入的 marker 不会被当作旧历史反证；外部非 own marker 仍会阻止批量。
    /// </summary>
    [Flags]
    internal enum MountIslandSaveLoadFlag
    {
        None = 0,
        Pending = 1,
        Init = 2,
        Metadata = 4,
        Save = 8,
        Load = 16,
        Delete = 32,
        Success = 64,
        Failure = 128,
        Busy = 256,
        Subsystem = 512,
        MissingData = 1024,
        CorruptedData = 2048,
        InsufficientSpace = 4096,
        Cancelled = 8192,
        InvalidVersion = 16384,
    }

    internal enum MountIslandCompletionClass
    {
        /// <summary>读取失败 / 非完成边界 / 未知 bit / 无法归属。</summary>
        Unknown = 0,
        /// <summary>Load+MissingData（可带 Failure，如 0x410 / 0x490）。</summary>
        MissingData = 1,
        /// <summary>Load+Success（0x50）：只表成功，不表首次。</summary>
        Success = 2,
        /// <summary>含 CorruptedData（内部 catch 写 0x890 即此类）。</summary>
        Corrupted = 3,
        Cancelled = 4,
        InvalidVersion = 5,
        /// <summary>原 Filer 失败 flags 原样传播，不重映射为 MissingData。</summary>
        FailureOther = 6,
    }

    /// <summary>一次 <c>MoveNext</c> 调用的完成快照（全部来自 typed 只读）。</summary>
    internal struct MountIslandLoadCompletion
    {
        internal bool FlagsReadOk;
        internal int RawFlags;
        internal bool ActiveResume;
        internal bool RunOriginal;
        internal bool CompletedNormally;
        internal int PostState;
    }

    internal static class MountIslandCompletionClassifier
    {
        internal const int KnownFlagsMask = 0x7FFF;

        internal static MountIslandCompletionClass Classify(MountIslandLoadCompletion completion)
        {
            if (!completion.FlagsReadOk) return MountIslandCompletionClass.Unknown;
            if (!completion.ActiveResume || !completion.RunOriginal || !completion.CompletedNormally
                || completion.PostState != -1)
                return MountIslandCompletionClass.Unknown;

            int flags = completion.RawFlags;
            if (flags == 0) return MountIslandCompletionClass.Unknown;
            if ((flags & ~KnownFlagsMask) != 0) return MountIslandCompletionClass.Unknown;
            if ((flags & (int)MountIslandSaveLoadFlag.InvalidVersion) != 0) return MountIslandCompletionClass.InvalidVersion;
            if ((flags & (int)MountIslandSaveLoadFlag.Cancelled) != 0) return MountIslandCompletionClass.Cancelled;
            if ((flags & (int)MountIslandSaveLoadFlag.CorruptedData) != 0) return MountIslandCompletionClass.Corrupted;

            if ((flags & (int)MountIslandSaveLoadFlag.MissingData) != 0)
            {
                const int allowed = (int)(MountIslandSaveLoadFlag.Load | MountIslandSaveLoadFlag.Failure
                    | MountIslandSaveLoadFlag.MissingData);
                if ((flags & ~allowed) != 0) return MountIslandCompletionClass.Unknown;
                if ((flags & (int)MountIslandSaveLoadFlag.Load) == 0) return MountIslandCompletionClass.Unknown;
                return MountIslandCompletionClass.MissingData;
            }

            if ((flags & (int)MountIslandSaveLoadFlag.Success) != 0)
            {
                int exact = (int)(MountIslandSaveLoadFlag.Load | MountIslandSaveLoadFlag.Success);
                return flags == exact ? MountIslandCompletionClass.Success : MountIslandCompletionClass.Unknown;
            }

            if ((flags & (int)MountIslandSaveLoadFlag.Failure) != 0)
            {
                const int allowed = (int)(MountIslandSaveLoadFlag.Load | MountIslandSaveLoadFlag.Failure);
                return (flags & ~allowed) != 0 ? MountIslandCompletionClass.Unknown : MountIslandCompletionClass.FailureOther;
            }

            return MountIslandCompletionClass.Unknown;
        }
    }

    /// <summary>
    /// 稳定 owner 身份：loadedGlobal 指针 + raw selected index + campaign 指针 + <c>campaign.reign</c> +
    /// landData 容器 + raw <c>_islands</c> 容器 + holder 实例/biome + **真实 <c>campaign.CurrentLand</c>**
    /// （不按请求参数回填）。
    /// </summary>
    internal struct MountIslandOwnerIdentity
    {
        internal ulong GlobalPointer;
        internal int SelectedCampaignIndex;
        internal ulong CampaignPointer;
        internal int ReignIndex;
        internal ulong LandDataPointer;
        internal ulong IslandsPointer;
        internal ulong HolderInstancePointer;
        internal int HolderBiomeIndex;
        internal int CurrentLand;

        internal bool IsComplete
        {
            get
            {
                return GlobalPointer != 0UL && SelectedCampaignIndex >= 0 && CampaignPointer != 0UL
                    && ReignIndex >= 0 && LandDataPointer != 0UL && IslandsPointer != 0UL
                    && HolderInstancePointer != 0UL && HolderBiomeIndex >= 0 && CurrentLand >= 0;
            }
        }

        internal static bool Matches(MountIslandOwnerIdentity a, MountIslandOwnerIdentity b)
        {
            return a.GlobalPointer == b.GlobalPointer
                && a.SelectedCampaignIndex == b.SelectedCampaignIndex
                && a.CampaignPointer == b.CampaignPointer
                && a.ReignIndex == b.ReignIndex
                && a.LandDataPointer == b.LandDataPointer
                && a.IslandsPointer == b.IslandsPointer
                && a.HolderInstancePointer == b.HolderInstancePointer
                && a.HolderBiomeIndex == b.HolderBiomeIndex
                && a.CurrentLand == b.CurrentLand;
        }
    }

    /// <summary>raw 目标岛历史视图（unknown → ReadsOk=false，fail-closed）。</summary>
    internal struct MountIslandHistoryView
    {
        internal bool ReadsOk;
        internal bool AnyPositive;
        internal bool SlotPresent;
        internal ulong SlotPointer;

        internal static MountIslandHistoryView Create(bool readsOk, bool anyPositive, bool slotPresent, ulong slotPointer)
        {
            return new MountIslandHistoryView
            {
                ReadsOk = readsOk,
                AnyPositive = anyPositive,
                SlotPresent = slotPresent,
                SlotPointer = slotPointer,
            };
        }
    }

    /// <summary>state0 封印的请求前像。</summary>
    internal struct MountIslandRequestEnvelope
    {
        internal bool ScopeOk;
        internal ulong RoutinePointer;
        internal int RequestCampaignIndex;
        internal int RequestChallengeId;
        internal int RequestLand;
        internal MountIslandOwnerIdentity Owner;
        internal bool PreSlotPresent;
        internal ulong PreSlotPointer;
        internal bool PreHistoryReadsOk;
        internal bool PreHistoryAnyPositive;
    }

    /// <summary>envelope 生命周期（one envelope = 一个请求）：None→Started→CompletedOnce→Transferred|Closed。</summary>
    internal enum MountIslandEnvelopeState
    {
        None = 0,
        /// <summary>state0 已封印；只允许一次完成 publish。</summary>
        Started = 1,
        /// <summary>本次完成已 publish 一张候选；同 envelope 不得再 publish。</summary>
        CompletedOnce = 2,
        /// <summary>候选已单次转交 frame。</summary>
        Transferred = 3,
        /// <summary>撤销（abort/unknown/下一 state0 覆盖前）。</summary>
        Closed = 4,
    }

    /// <summary>
    /// envelope 输入校验（防止 mutable 输入绕 scope helper）：challenge==0、扩展岛、owner 完整且
    /// <c>Owner.CurrentLand == RequestLand</c>、routine 非零、前像历史可读。
    /// </summary>
    internal static class MountIslandEnvelopeValidator
    {
        internal static bool IsSealable(MountIslandRequestEnvelope envelope)
        {
            if (!envelope.ScopeOk) return false;
            if (envelope.RoutinePointer == 0UL) return false;
            if (envelope.RequestChallengeId != 0) return false;
            if (!MountIslandSplitPolicy.IsExtensionLand(envelope.RequestLand)) return false;
            if (!envelope.Owner.IsComplete) return false;
            if (envelope.Owner.CurrentLand != envelope.RequestLand) return false;
            if (envelope.Owner.SelectedCampaignIndex != envelope.RequestCampaignIndex) return false;
            if (!envelope.PreHistoryReadsOk) return false;
            return true;
        }
    }

    /// <summary>完成的本次候选草案。</summary>
    internal struct MountIslandTicketDraft
    {
        internal MountIslandCompletionClass CompletionClass;
        internal int RawFlags;
        internal MountIslandOwnerIdentity Owner;
        internal ulong RoutinePointer;
        internal int RequestCampaignIndex;
        internal int RequestChallengeId;
        internal int RequestLand;
        internal bool HistoryReadsOk;
        internal bool HistoryAnyPositive;
        /// <summary>completion 后 raw slot 指针；候选要求非零并精确冻结（零 completion 保持 Unknown）。</summary>
        internal ulong SlotPointer;
        internal long IssuedGeneration;
    }

    /// <summary>转交给 frame 的一次性候选票据（frame 持有；Unknown = null）。</summary>
    internal sealed class MountIslandFrameTicket
    {
        private readonly MountIslandTicketDraft _draft;
        private bool _consumed;
        private bool _invalidated;

        internal MountIslandFrameTicket(MountIslandTicketDraft draft)
        {
            _draft = draft;
        }

        internal bool IsMissingCandidate => _draft.CompletionClass == MountIslandCompletionClass.MissingData;
        internal int RequestLand => _draft.RequestLand;
        internal int RequestCampaignIndex => _draft.RequestCampaignIndex;
        internal int RequestChallengeId => _draft.RequestChallengeId;
        internal ulong RoutinePointer => _draft.RoutinePointer;
        internal long IssuedGeneration => _draft.IssuedGeneration;
        internal int RawFlags => _draft.RawFlags;
        internal ulong SlotPointer => _draft.SlotPointer;
        internal MountIslandOwnerIdentity Owner => _draft.Owner;
        internal bool Consumed => _consumed;
        internal bool Invalidated => _invalidated;
        internal bool Usable => !_consumed && !_invalidated;

        internal void Consume()
        {
            _consumed = true;
        }

        /// <summary>
        /// 粘性失效：读 fault / 复核拒绝 / binding 失败即锁死，字段恢复也不能重新 fresh。
        /// 仅当票据代次仍等于 gate 当前代次（没有更新的请求）才撤 gate；否则只失效本票据，
        /// 绝不误撤另一个新 request 的 envelope/候选。
        /// </summary>
        internal void InvalidateAgainst(MountIslandFreshGate gate)
        {
            _invalidated = true;
            if (gate != null && _draft.IssuedGeneration == gate.Generation)
                gate.Revoke();
        }

        internal string Describe()
        {
            return "class=" + _draft.CompletionClass + " flags=0x" + _draft.RawFlags.ToString("X")
                + " land=" + _draft.RequestLand + " campaignIndex=" + _draft.RequestCampaignIndex
                + " challenge=" + _draft.RequestChallengeId + " slot=0x" + _draft.SlotPointer.ToString("X")
                + " gen=" + _draft.IssuedGeneration;
        }
    }

    /// <summary>
    /// 单槽 fresh 门（bounded）：一份 envelope（生命周期）+ 一张未转交票据 + 一个代次。
    /// </summary>
    internal sealed class MountIslandFreshGate
    {
        private MountIslandRequestEnvelope _envelope;
        private MountIslandEnvelopeState _state = MountIslandEnvelopeState.None;
        private MountIslandTicketDraft _pending;
        private bool _hasPending;
        private long _generation;

        internal long Generation => _generation;
        internal MountIslandEnvelopeState State => _state;
        internal bool EnvelopeSealed => _state == MountIslandEnvelopeState.Started
            || _state == MountIslandEnvelopeState.CompletedOnce
            || _state == MountIslandEnvelopeState.Transferred;
        internal MountIslandRequestEnvelope Envelope => _envelope;
        internal bool HasPending => _hasPending;

        /// <summary>state0 封印新请求：覆盖旧 envelope（撤销旧候选），代次递增；输入必须先通过校验。</summary>
        internal bool TrySealEnvelope(MountIslandRequestEnvelope envelope)
        {
            if (!MountIslandEnvelopeValidator.IsSealable(envelope))
            {
                Revoke();
                return false;
            }
            _envelope = envelope;
            _state = MountIslandEnvelopeState.Started;
            _hasPending = false;
            _generation++;
            return true;
        }

        /// <summary>撤销当前请求/候选（abort、unknown、owner 漂移、下一 state0 之前的显式清理）。</summary>
        internal long Revoke()
        {
            _state = MountIslandEnvelopeState.Closed;
            _hasPending = false;
            _generation++;
            return _generation;
        }

        /// <summary>
        /// 应用 advance 判定（Observer 每个 MoveNext 分支唯一入口）：只有 Abort 撤销，且仅在
        /// <paramref name="allowRevoke"/>（调用属于当前 envelope 的 routine，或本就无 envelope）时；
        /// Continue（正常 yield）与 Ignore（closed 重复）保持现有 Started/候选不动，绝不误撤另一新请求。
        /// </summary>
        internal long ApplyNonTerminal(MountIslandAdvanceOutcome outcome, bool allowRevoke)
        {
            if (outcome == MountIslandAdvanceOutcome.Abort && allowRevoke) return Revoke();
            return _generation;
        }

        /// <summary>
        /// 只允许在 Started（首次完成）publish 一次；post slot 必须非零并精确冻结
        /// （零 completion / 前像非零被替换 / post 正证一律拒绝）。
        /// </summary>
        internal bool TryPublish(MountIslandTicketDraft draft)
        {
            if (_state != MountIslandEnvelopeState.Started) return false;
            if (!_envelope.ScopeOk || !_envelope.PreHistoryReadsOk) return false;
            if (_envelope.PreHistoryAnyPositive) return false;
            if (_hasPending) return false;
            if (draft.CompletionClass != MountIslandCompletionClass.MissingData) return false;
            if (draft.RoutinePointer != _envelope.RoutinePointer) return false;
            if (draft.RequestCampaignIndex != _envelope.RequestCampaignIndex
                || draft.RequestChallengeId != _envelope.RequestChallengeId
                || draft.RequestLand != _envelope.RequestLand)
                return false;
            if (!MountIslandOwnerIdentity.Matches(draft.Owner, _envelope.Owner)) return false;
            if (draft.RequestChallengeId != 0) return false;
            if (!MountIslandSplitPolicy.IsExtensionLand(draft.RequestLand)) return false;
            if (!draft.HistoryReadsOk || draft.HistoryAnyPositive) return false;
            if (draft.SlotPointer == 0UL) return false;                       // 零 completion 保持 Unknown
            if (_envelope.PreSlotPointer != 0UL && draft.SlotPointer != _envelope.PreSlotPointer)
                return false;                                                 // 前像非零被替换
            draft.IssuedGeneration = _generation;
            _pending = draft;
            _hasPending = true;
            _state = MountIslandEnvelopeState.CompletedOnce;
            return true;
        }

        internal bool TryPeekPending(out MountIslandTicketDraft draft)
        {
            draft = _pending;
            return _hasPending;
        }

        /// <summary>claim 通过后的单次转交；Transferred 后同 envelope 不可能再 publish。</summary>
        internal MountIslandFrameTicket TakeFrameTicket()
        {
            if (!_hasPending || _state != MountIslandEnvelopeState.CompletedOnce) return null;
            _hasPending = false;
            _state = MountIslandEnvelopeState.Transferred;
            return new MountIslandFrameTicket(_pending);
        }
    }

    /// <summary>claim 判定（Open 前封同 scope：身份/land/私有 config/目标 Level/slot）。</summary>
    internal static class MountIslandClaimEvaluator
    {
        internal static bool Evaluate(MountIslandTicketDraft draft, bool generationMatches, bool identityMatches,
            bool ownedConfigConfirmed, bool targetLevelCaptured, bool slotPolicyOk)
        {
            if (draft.CompletionClass != MountIslandCompletionClass.MissingData) return false;
            return generationMatches && identityMatches && ownedConfigConfirmed && targetLevelCaptured && slotPolicyOk;
        }
    }

    // ------------------------------------------------------------------ 提交批量（own marker 语义）

    internal struct MountIslandBatchGrant
    {
        internal string DefinitionId;
        internal int Land;
        internal int SteedTypeId;
        internal bool Authorized;
        /// <summary>本次目标 Level 已观察到放置（Complete == CommitMarker）。</summary>
        internal bool Placed;
    }

    internal struct MountIslandMarkerWrite
    {
        internal string DefinitionId;
        internal int Land;
        internal int SteedTypeId;
    }

    /// <summary>
    /// Close 提交批量：**在一次** fresh 历史/身份复核（发生在任何本帧 marker 写入之前）之后，
    /// 逐条只做 binding/level/config/slot/owner 判断（<see cref="PlanGrant"/> 不接收任何 island
    /// history 输入）。因此：
    /// - 本帧自己写入的 expected marker 不可能被误判为"旧历史反证"（后写的 grant 不会因前一条
    ///   own marker 被拒）；
    /// - 外部新出现的非 own marker 在 batch 复核时即阻止整批 authorized 提交（batchVerified=false）；
    /// - partial placement（未观察到放置）或错误 land 的 grant 不写 marker。
    /// </summary>
    internal sealed class MountIslandCommitSession
    {
        private readonly bool _batchVerified;

        internal MountIslandCommitSession(bool batchVerified, string batchReason)
        {
            _batchVerified = batchVerified;
            BatchReason = batchReason ?? string.Empty;
        }

        internal bool BatchVerified => _batchVerified;
        internal string BatchReason { get; }

        internal bool PlanGrant(MountIslandBatchGrant grant, bool bindingOk, int frameLand,
            out MountIslandMarkerWrite write)
        {
            write = default;
            if (grant.Land != frameLand) return false;                  // 错误 land：拒
            if (!grant.Placed) return false;                            // partial placement：不写该 marker
            if (grant.Authorized && !_batchVerified) return false;      // 外部非 own 反证/unknown：整批拒
            if (grant.Authorized && !bindingOk) return false;           // binding（owner/level/config/slot，无 history）
            write = new MountIslandMarkerWrite
            {
                DefinitionId = grant.DefinitionId,
                Land = grant.Land,
                SteedTypeId = grant.SteedTypeId,
            };
            return true;
        }
    }

    // ------------------------------------------------------------------ MoveNext advance 判定（P1）

    /// <summary>每个 MoveNext 调用后的一次判定（Observer 每分支唯一入口）。</summary>
    internal enum MountIslandAdvanceOutcome
    {
        /// <summary>closed（preState -1）重复调用：拒绝且**不触碰**当前 envelope（不污染另一新请求）。</summary>
        Ignore = 0,
        /// <summary>正常 yield（Result true + 活动 postState 1/2）：保持 Started，不读 return flags、不发布。</summary>
        Continue = 1,
        /// <summary>真实终末（Result false + postState -1 + 活动恢复 + 同 envelope）：才允许 Classify/flags 读。</summary>
        Terminal = 2,
        /// <summary>unknown/mutated/skipped/异常/非法 postState：撤销活动请求证据。</summary>
        Abort = 3,
    }

    internal struct MountIslandAdvanceInput
    {
        internal int PreState;
        internal bool RunOriginal;
        internal bool PostfixRan;
        internal bool Result;
        internal int PostState;
        internal bool EnvelopeSealed;
        internal bool SameRoutine;
        internal bool SameRequest;
    }

    /// <summary>
    /// 生产纯 advance 判定：正常 yield 绝不撤 Started、绝不读未初始化的 return flags；
    /// 只有活动恢复的真实终末（false/-1）才进入 terminal 分类。
    /// </summary>
    internal static class MountIslandAdvanceClassifier
    {
        internal static MountIslandAdvanceOutcome Classify(MountIslandAdvanceInput input)
        {
            if (input.PreState == -1) return MountIslandAdvanceOutcome.Ignore;      // closed 重复：不动 gate
            if (!input.EnvelopeSealed) return MountIslandAdvanceOutcome.Abort;     // 无 envelope（含 no-state0）
            if (!input.SameRoutine || !input.SameRequest) return MountIslandAdvanceOutcome.Abort;  // mutated/mismatch
            if (!input.RunOriginal || !input.PostfixRan) return MountIslandAdvanceOutcome.Abort;   // skipped/未正常返回
            if (input.Result)
            {
                // 正常 yield：postState 必须是活动恢复状态；此处禁止以 return flags 判成败。
                if (input.PostState == 1 || input.PostState == 2) return MountIslandAdvanceOutcome.Continue;
                return MountIslandAdvanceOutcome.Abort;                            // 非法 postState
            }
            if (input.PreState == 1 || input.PreState == 2)
                return input.PostState == -1 ? MountIslandAdvanceOutcome.Terminal : MountIslandAdvanceOutcome.Abort;
            return MountIslandAdvanceOutcome.Abort;                                // state0 false：非预期
        }
    }

    // ------------------------------------------------------------------ stamp 闭合（R3）

    /// <summary>一次读取的完整 stamp：owner（含 holder/currentLand）+ routine + 请求 immutable fields + 代次。</summary>
    internal struct MountIslandObservationStamp
    {
        internal MountIslandOwnerIdentity Owner;
        internal ulong RoutinePointer;
        internal int RequestCampaignIndex;
        internal int RequestChallengeId;
        internal int RequestLand;
        internal long Generation;
    }

    /// <summary>
    /// before/after stamp 全等（pure；Observer 实际调用）：容器部分复用 runtime 已发布
    /// <c>MountIslandSnapshotIdentity.MatchesOwnerScope</c>，并额外比较 holder Native pointer/biome。
    /// 任一字段（holder/global/selected/currentLand/reign/islands/landData/请求 fields/routine/gen）变化 → false。
    /// </summary>
    internal static class MountIslandStampMatch
    {
        internal static bool Matches(MountIslandObservationStamp a, MountIslandObservationStamp b)
        {
            return a.RoutinePointer == b.RoutinePointer
                && a.RequestCampaignIndex == b.RequestCampaignIndex
                && a.RequestChallengeId == b.RequestChallengeId
                && a.RequestLand == b.RequestLand
                && a.Generation == b.Generation
                && MountIslandSnapshotIdentity.MatchesOwnerScope(
                    a.Owner.GlobalPointer, a.Owner.SelectedCampaignIndex, a.Owner.CurrentLand,
                    a.Owner.CampaignPointer, a.Owner.ReignIndex, a.Owner.LandDataPointer, a.Owner.IslandsPointer,
                    b.Owner.GlobalPointer, b.Owner.SelectedCampaignIndex, b.Owner.CurrentLand,
                    b.Owner.CampaignPointer, b.Owner.ReignIndex, b.Owner.LandDataPointer, b.Owner.IslandsPointer)
                && a.Owner.HolderInstancePointer == b.Owner.HolderInstancePointer
                && a.Owner.HolderBiomeIndex == b.Owner.HolderBiomeIndex;
        }
    }

    internal enum MountIslandGrantAdmission
    {
        Unknown = 0,
        Fresh = 1,
        Existing = 2,
    }

    // ------------------------------------------------------------------ prefix/finalizer/error 统一路由 fence（R4 复审 A）

    /// <summary>一次 prefix/finalizer/error 撤销尝试与当前 envelope 的关系。</summary>
    internal enum MountIslandRouteFence
    {
        /// <summary>gate 无 envelope：无可撤（no-op）。</summary>
        NoEnvelope = 0,
        /// <summary>不同 routine：foreign，禁撤（不毁新 current）。</summary>
        Foreign = 1,
        /// <summary>instance/snapshot 缺失：不能凭空撤。</summary>
        Unfenced = 2,
        /// <summary>prefix 之后已有新 seal/撤销（generation 已变）：stale，禁撤。</summary>
        Stale = 3,
        /// <summary>同 routine、请求字段不同：属当前请求的 mutated attempt，可 sticky close。</summary>
        Mutated = 4,
        /// <summary>同 routine 同请求：本请求 attempt，可 sticky close。</summary>
        OwnCurrent = 5,
    }

    /// <summary>
    /// Prefix active/no-state0、finalizer 异常与 catch 路径的**唯一**撤销路由（Observer 实际调用）：
    /// 只有 OwnCurrent/Mutated 才允许 Revoke；Foreign/Unfenced/Stale/NoEnvelope 一律 no-op，
    /// 因此 foreign/stale call 或缺失 snapshot 不能毁另一个新请求。
    /// </summary>
    internal static class MountIslandAdapterRouteFence
    {
        internal static MountIslandRouteFence Evaluate(bool envelopeSealed, ulong envelopeRoutine,
            int envelopeCampaignIndex, int envelopeChallengeId, int envelopeLand,
            bool attemptSnapshotOk, ulong attemptRoutine,
            int attemptCampaignIndex, int attemptChallengeId, int attemptLand,
            long attemptGeneration, long liveGeneration)
        {
            if (!envelopeSealed) return MountIslandRouteFence.NoEnvelope;
            if (!attemptSnapshotOk || attemptRoutine == 0UL) return MountIslandRouteFence.Unfenced;
            if (envelopeRoutine != attemptRoutine) return MountIslandRouteFence.Foreign;
            if (attemptGeneration != liveGeneration) return MountIslandRouteFence.Stale;
            if (envelopeCampaignIndex != attemptCampaignIndex
                || envelopeChallengeId != attemptChallengeId
                || envelopeLand != attemptLand)
                return MountIslandRouteFence.Mutated;
            return MountIslandRouteFence.OwnCurrent;
        }

        internal static bool CanRevoke(MountIslandRouteFence fence)
            => fence == MountIslandRouteFence.OwnCurrent || fence == MountIslandRouteFence.Mutated;

        /// <summary>统一落地：可撤 → Revoke；否则 no-op（返回 gate 当前代次）。</summary>
        internal static long ApplyRevocation(MountIslandFreshGate gate, MountIslandRouteFence fence)
            => CanRevoke(fence) ? gate.Revoke() : gate.Generation;
    }

    internal static class MountIslandGrantAdmissionEvaluator
    {
        internal static MountIslandGrantAdmission ClassifyCompletion(
            MountIslandCompletionClass completionClass, bool historyAnyPositive)
        {
            if (completionClass == MountIslandCompletionClass.MissingData)
                return historyAnyPositive ? MountIslandGrantAdmission.Unknown : MountIslandGrantAdmission.Fresh;
            if (completionClass == MountIslandCompletionClass.Success)
                return historyAnyPositive ? MountIslandGrantAdmission.Existing : MountIslandGrantAdmission.Unknown;
            return MountIslandGrantAdmission.Unknown;
        }

        internal static MountIslandGrantAdmission EvaluateFrameRecheck(
            MountIslandFrameTicket ticket,
            bool generationMatches,
            bool ownerMatches,
            bool levelMatches,
            bool configMatches,
            bool ownedConfigConfirmed,
            bool historyReadsOk,
            bool historyAnyPositive,
            bool slotMatches)
        {
            if (ticket == null || !ticket.Usable) return MountIslandGrantAdmission.Unknown;
            if (!ticket.IsMissingCandidate) return MountIslandGrantAdmission.Unknown;
            if (!generationMatches) return MountIslandGrantAdmission.Unknown;
            if (!ownerMatches || !levelMatches || !configMatches || !ownedConfigConfirmed)
                return MountIslandGrantAdmission.Unknown;
            if (!historyReadsOk) return MountIslandGrantAdmission.Unknown;
            if (!slotMatches) return MountIslandGrantAdmission.Unknown;
            if (historyAnyPositive) return MountIslandGrantAdmission.Unknown;
            return MountIslandGrantAdmission.Fresh;
        }
    }
}
