using System;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// <c>IslandSaveData.&lt;_TryLoad&gt;d__54.MoveNext</c> 请求入口动作（纯策略，无 Unity/Il2Cpp/Harmony）。
    ///
    /// 边界来源（#260 review2 已闭合的当前 ARM 语义）：
    /// - PC0 是新 iterator 的初始化边界，只在这里/首次接管时封印请求前像；
    /// - PC1 是数据请求前像，正常本体的唯一赋值来自 PC0，因此“再次 PC1”不是普通 yield 恢复；
    /// - PC2 是同一次请求的 storage 恢复，绝不补 seal；
    /// - 合法 Haglet reset-point 复用可以直接从 PC1 开始，此时没有 PC0 envelope，必须读取当前前像并
    ///   封印新代次；同一 generation 已经进入过 PC1 后再来 PC1 则是 restart，必须换新代次。
    ///
    /// 本文件只做判定，不读 Native、不写 Gate、不持有游戏对象。所有原 owner/slot/holder/config/history/
    /// fresh 门仍由既有 <see cref="MountIslandFreshGate"/> 与 Observer 闭合，不因入口修复放宽。
    /// </summary>
    internal enum MountIslandRequestEntryAction
    {
        /// <summary>closed/首见 PC2/不匹配的 PC2 等：不 seal、不读 flags；由调用方按 route fence 处理。</summary>
        Reject = 0,
        /// <summary>PC0：新请求边界，尝试封印新 envelope（失败则保持 Unknown）。</summary>
        SealInitial = 1,
        /// <summary>PC1：Started + 同 routine/request + 本 generation 尚未进入 PC1，续用该前像并标记入口。</summary>
        ContinueAndMarkEntry1 = 2,
        /// <summary>PC1：新数据请求/restart，重新读取当前前像并封印新代次，随后标记入口。</summary>
        SealNewGeneration = 3,
        /// <summary>PC2：只续用已捕获过数据入口的当前请求，绝不补 seal。</summary>
        ResumeOnly = 4,
    }

    internal struct MountIslandRequestEntryInput
    {
        internal int PreState;
        /// <summary>Gate 当前 envelope 状态；只有 Started 允许 PC1 续用/PC2 恢复。</summary>
        internal MountIslandEnvelopeState EnvelopeState;
        /// <summary>envelope 存在且 routine 指针精确相等（不按名字/近似值）。</summary>
        internal bool SameRoutine;
        /// <summary>campaignIndex/challengeId/land 三项与 envelope 精确相等。</summary>
        internal bool SameRequest;
        /// <summary>当前 live generation 已在本代次进入过 PC1（由 tracker 提供）。</summary>
        internal bool Entry1SeenForCurrentGeneration;
    }

    internal static class MountIslandRequestEntryPolicy
    {
        /// <summary>
        /// 输入全部显式；任何 gate 变异（代次 bump、foreign seal、CompletedOnce/Transferred/Closed、字段变化）
        /// 都会自然落入 SealNewGeneration / Reject，不依赖隐式状态。
        /// </summary>
        internal static MountIslandRequestEntryAction Decide(in MountIslandRequestEntryInput input)
        {
            switch (input.PreState)
            {
                case 0:
                    // state 0 是唯一初始化边界：即使旧 envelope 仍 Started，也必须建立本请求。
                    return MountIslandRequestEntryAction.SealInitial;

                case 1:
                    if (input.EnvelopeState == MountIslandEnvelopeState.Started
                        && input.SameRoutine
                        && input.SameRequest
                        && !input.Entry1SeenForCurrentGeneration)
                        return MountIslandRequestEntryAction.ContinueAndMarkEntry1;

                    // 同参数 restart / CompletedOnce / foreign / 无 envelope：都按新数据请求重读前像。
                    return MountIslandRequestEntryAction.SealNewGeneration;

                case 2:
                    // PC2 只能恢复已经捕获过数据入口的本请求；首见 2 或任何不匹配都拒绝，绝不补 seal。
                    if (input.EnvelopeState == MountIslandEnvelopeState.Started
                        && input.SameRoutine
                        && input.SameRequest
                        && input.Entry1SeenForCurrentGeneration)
                        return MountIslandRequestEntryAction.ResumeOnly;
                    return MountIslandRequestEntryAction.Reject;

                default:
                    return MountIslandRequestEntryAction.Reject;
            }
        }

        internal static bool RequiresSeal(MountIslandRequestEntryAction action)
            => action == MountIslandRequestEntryAction.SealInitial
                || action == MountIslandRequestEntryAction.SealNewGeneration;
    }

    /// <summary>
    /// 单槽入口 tracker：只记住一个 live generation 和“该代次已进入 PC1”一个 bit，不持 Native 对象、
    /// 不建 dictionary、不周期扫描。close/claim 由 Observer 接线调用 <see cref="Clear"/>。
    ///
    /// 任何 generation 变化都会清掉 seen；因此 foreign seal / gate revoke / 新 seal 之后，同指针同参数的
    /// PC1 不会被误当成当前请求的继续。
    /// </summary>
    internal sealed class MountIslandRequestEntryTracker
    {
        private long _generation = -1;
        private bool _entry1Seen;

        internal long Generation => _generation;
        internal bool Entry1Seen => _entry1Seen;

        internal bool Entry1SeenFor(long generation)
            => generation >= 0 && generation == _generation && _entry1Seen;

        /// <summary>观察 live generation；值变化时清 seen。相同值不会清除已标记的入口。</summary>
        internal void ObserveGeneration(long generation)
        {
            if (generation < 0)
            {
                Clear();
                return;
            }
            if (generation == _generation) return;
            _generation = generation;
            _entry1Seen = false;
        }

        /// <summary>标记当前 generation 已进入 PC1；负值不建立任何有效标记。</summary>
        internal void MarkEntry1(long generation)
        {
            if (generation < 0)
            {
                Clear();
                return;
            }
            _generation = generation;
            _entry1Seen = true;
        }

        internal void Clear()
        {
            _generation = -1;
            _entry1Seen = false;
        }
    }

    /// <summary>
    /// 终结读取前的唯一授权门：先由既有 <see cref="MountIslandAdvanceClassifier"/> 判定 Terminal，
    /// 再由既有 <see cref="MountIslandAdapterRouteFence"/> 判定 OwnCurrent，且 prefix 快照可读。
    ///
    /// 关键反例：旧 attempt 的 PrefixGeneration=N，新 seal 已到 N+1，routine/request 恰好相同。
    /// fence 必须使用 attempt 捕获的 N 与 live N+1 比较得到 Stale；绝不能用 live generation 重建
    /// attempt stamp 再得出 OwnCurrent。Stale/Foreign/Unfenced 一律不读 flags、不发布、不撤新 current。
    /// </summary>
    internal interface IMountIslandTerminalFlagsReader
    {
        bool TryRead(out int flags, out string reason);
    }

    internal static class MountIslandTerminalReadGate
    {
        /// <param name="entryAuthorized">
        /// 本 attempt 的 prefix 已由 <see cref="MountIslandRequestEntryPolicy"/> 授权（PC0/PC1 成功
        /// seal、合法续用或 PC2 resume）。入口拒绝/读失败的 attempt 即使 finalizer 看到同 routine/
        /// request 也不能读 flags 或发布。
        /// </param>
        internal static bool IsAuthorized(MountIslandAdvanceOutcome outcome, MountIslandRouteFence fence,
            bool snapshotReadOk, bool entryAuthorized)
        {
            return entryAuthorized
                && snapshotReadOk
                && outcome == MountIslandAdvanceOutcome.Terminal
                && fence == MountIslandRouteFence.OwnCurrent;
        }

        internal static bool TryReadFlags(MountIslandAdvanceOutcome outcome, MountIslandRouteFence fence,
            bool snapshotReadOk, bool entryAuthorized, IMountIslandTerminalFlagsReader reader,
            out int flags, out string reason)
        {
            flags = 0;
            reason = "terminal not authorized";
            if (!IsAuthorized(outcome, fence, snapshotReadOk, entryAuthorized)) return false;
            if (reader == null)
            {
                reason = "terminal reader missing";
                return false;
            }
            return reader.TryRead(out flags, out reason);
        }
    }

    /// <summary>
    /// Native reader 的**纯**元数据形状判定（不引用 Unity/Il2Cpp；host 测试可直接调用生产实现）。
    /// <see cref="MountIslandNativeResult"/> 只负责把 live metadata 映射为本结构；任何一项不满足即
    /// sticky 失败，绝不尝试较小 buffer、猜 offset 或默认成功。
    /// </summary>
    internal struct MountIslandNativeResultShape
    {
        internal bool RoutineClassPresent;
        internal bool RoutineClassReferenceType;
        internal bool ReturnFieldPresent;
        internal bool ReturnFieldParentExact;
        internal bool ReturnFieldNonStatic;
        internal bool ReturnFieldTypeNotByRefOrPointer;
        /// <summary>独立 <c>il2cpp_type_is_byref</c> 标志必须为 false（kind 检查不足以排除 byref）。</summary>
        internal bool ReturnFieldByRefFlagFree;
        internal bool ReturnClassExact;
        internal bool ReturnClassValueType;
        internal int ReturnValueSize;
        internal uint ReturnAlignment;
        internal bool EnumClassPresent;
        internal bool EnumIsEnum;
        internal bool EnumValueType;
        internal bool EnumBaseTypeIsInt32;
        internal bool EnumBaseTypeKindNotByRefOrPointer;
        internal bool EnumBaseTypeByRefFlagFree;
        internal int EnumValueSize;
        internal uint EnumAlignment;
        internal int NonStaticFieldCount;
        internal bool ValueFieldPresent;
        internal bool ValueFieldNamedValue;
        internal bool ValueFieldTypeNotByRefOrPointer;
        /// <summary>独立 <c>il2cpp_type_is_byref</c> 标志必须为 false。</summary>
        internal bool ValueFieldByRefFlagFree;
        internal bool ValueFieldClassExactEnum;
    }

    internal static class MountIslandNativeResultShapePolicy
    {
        internal const int ExpectedPayloadSize = 4;

        internal static bool IsValid(in MountIslandNativeResultShape shape)
        {
            return shape.RoutineClassPresent
                && shape.RoutineClassReferenceType
                && shape.ReturnFieldPresent
                && shape.ReturnFieldParentExact
                && shape.ReturnFieldNonStatic
                && shape.ReturnFieldTypeNotByRefOrPointer
                && shape.ReturnFieldByRefFlagFree
                && shape.ReturnClassExact
                && shape.ReturnClassValueType
                && shape.ReturnValueSize == ExpectedPayloadSize
                && IsLegalAlignment(shape.ReturnAlignment)
                && shape.EnumClassPresent
                && shape.EnumIsEnum
                && shape.EnumValueType
                && shape.EnumBaseTypeIsInt32
                && shape.EnumBaseTypeKindNotByRefOrPointer
                && shape.EnumBaseTypeByRefFlagFree
                && shape.EnumValueSize == ExpectedPayloadSize
                && IsLegalAlignment(shape.EnumAlignment)
                && shape.NonStaticFieldCount == 1
                && shape.ValueFieldPresent
                && shape.ValueFieldNamedValue
                && shape.ValueFieldTypeNotByRefOrPointer
                && shape.ValueFieldByRefFlagFree
                && shape.ValueFieldClassExactEnum;
        }

        /// <summary>4 字节 payload 的合法对齐必须非零、不超过 payload，并整除 payload。</summary>
        internal static bool IsLegalAlignment(uint alignment)
            => alignment != 0
                && alignment <= ExpectedPayloadSize
                && (ExpectedPayloadSize % alignment) == 0;

        internal static string Describe(in MountIslandNativeResultShape shape)
        {
            return "routine=" + shape.RoutineClassPresent
                + "/" + shape.RoutineClassReferenceType
                + " returnField=" + shape.ReturnFieldPresent
                + "/" + shape.ReturnFieldParentExact
                + "/" + shape.ReturnFieldNonStatic
                + "/" + shape.ReturnFieldTypeNotByRefOrPointer
                + "/byrefFree=" + shape.ReturnFieldByRefFlagFree
                + " returnClass=" + shape.ReturnClassExact
                + "/" + shape.ReturnClassValueType
                + "/size=" + shape.ReturnValueSize
                + "/align=" + shape.ReturnAlignment
                + " enum=" + shape.EnumClassPresent
                + "/" + shape.EnumIsEnum
                + "/" + shape.EnumValueType
                + "/int32=" + shape.EnumBaseTypeIsInt32
                + "/kindOk=" + shape.EnumBaseTypeKindNotByRefOrPointer
                + "/byrefFree=" + shape.EnumBaseTypeByRefFlagFree
                + "/size=" + shape.EnumValueSize
                + "/align=" + shape.EnumAlignment
                + " valueFields=" + shape.NonStaticFieldCount
                + "/" + shape.ValueFieldPresent
                + "/" + shape.ValueFieldNamedValue
                + "/" + shape.ValueFieldTypeNotByRefOrPointer
                + "/byrefFree=" + shape.ValueFieldByRefFlagFree
                + "/" + shape.ValueFieldClassExactEnum;
        }
    }
}
