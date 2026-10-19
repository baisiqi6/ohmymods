using System;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// #270 结果交接纯决策（零 Unity/IL2CPP/Harmony 依赖，测试直接编译本文件）。
    ///
    /// 原生事实（2.4 ARM64 只读证据）：<c>MapTimelineMenu+&lt;BaseShowRoutine&gt;d__76.MoveNext</c>
    /// 在完成分支把 focusedLand 装进 inline <c>Routine.Return&lt;Il2CppSystem.Nullable&lt;int&gt;&gt; result</c>；
    /// Greek/Game 转发直接解包 <c>Nullable.Value</c>，None 会走 NoValue 异常。因此本层：
    /// - 只在 exact show/ticket 的首次完成，且 native hasValue 规范为 1、value == expected UI 时，
    ///   把 inline Return 的 4 字节 int 改为捕获的 physical；不写 hasValue/其它字节。
    /// - 不构造 Nullable/Return，不调用 pop/peek/泛型 ctor，不把 None 升级成成功或取消。
    /// - 无票、失配、旧 show、None、非 expected UI 一律原样保留并消费本次完成，绝不二次映射。
    ///
    /// 偏移常量来自 IL2CPP metadata 的硬门；实际解析/校验在 runtime adapter 中完成并把事实送进
    /// <see cref="TryAcceptLayout"/>。布局任一不符即永久禁用本进程确认特性（fail-closed）。
    /// </summary>
    internal readonly struct MapResultLayout
    {
        internal readonly int ResultFieldOffset;
        internal readonly int ReturnValueOffset;
        internal readonly int HasValueOffset;
        internal readonly int ValueOffset;

        internal MapResultLayout(int resultFieldOffset, int returnValueOffset, int hasValueOffset, int valueOffset)
        {
            ResultFieldOffset = resultFieldOffset;
            ReturnValueOffset = returnValueOffset;
            HasValueOffset = hasValueOffset;
            ValueOffset = valueOffset;
        }
    }

    /// <summary>布局事实：runtime adapter 从 IL2CPP metadata/boxed getter 取得，本类型只做纯判定。</summary>
    internal sealed class MapResultLayoutFacts
    {
        internal bool ResultFieldFound;
        internal bool ResultFieldStatic;
        internal bool ResultFieldTypeExact;
        internal int ResultFieldOffset;
        internal bool ResultFieldInsideObject;
        internal bool ResultFieldAligned;
        internal int ReturnSize;
        internal bool ReturnIsValueType;
        /// <summary>metadata 原始 offset（boxed 坐标，含对象头）。</summary>
        internal int ReturnValueOffset;
        internal bool ReturnValueTypeExact;
        internal bool ReturnNoReferenceFields;
        internal bool ReturnExactFieldSet;
        internal int NullableSize;
        internal bool NullableIsValueType;
        /// <summary>metadata 原始 offset（boxed 坐标，含对象头）。</summary>
        internal int NullableHasValueOffset;
        /// <summary>metadata 原始 offset（boxed 坐标，含对象头）。</summary>
        internal int NullableValueOffset;
        internal bool NullableHasValueTypeExact;
        internal bool NullableValueTypeExact;
        internal int NullableHasValueSize;
        internal int NullableValueSize;
        internal bool NullableNoReferenceFields;
        internal bool NullableExactFieldSet;
        internal long BoxedReturnHeaderDisplacement;
        internal uint BoxedReturnObjectSize;
        /// <summary>il2cpp_class_instance_size(Nullable&lt;int&gt;)，metadata/class ABI，不是 boxed 观测。</summary>
        internal int NullableClassInstanceSize;
        /// <summary>Nullable class instance size - value size，class ABI 推导，不是 boxed 观测。</summary>
        internal long NullableClassHeaderDisplacement;
        internal bool BoxedReturnInlineMatches;
        internal bool BoxedReturnFieldReadMatches;
    }

    internal sealed class MapResultArmFacts
    {
        internal bool LayoutSupported;
        internal bool TupleExact;
        internal bool ActiveShowExists;
        internal bool ShowMatches;
        internal bool AlreadyArmed;
        internal bool AlreadyCompleted;
        internal bool CampaignMatches;
        internal bool TokenMatches;
        internal bool MenuStateAllowed;
        internal bool ActiveMapMatches;
        internal bool HideTargetValid;
        internal int Ui;
        internal int Physical;
    }

    internal sealed class MapResultCompletionFacts
    {
        internal bool MatchesTicket;
        internal bool LayoutSupported;
        internal bool CampaignMatches;
        internal bool TokenMatches;
        internal bool HasValue;
        internal int NativeValue;
    }

    internal struct MapResultTicket
    {
        internal bool Armed;
        internal bool Consumed;
        internal bool Committed;
        internal bool Revoked;
        internal int ExpectedUi;
        internal int Physical;
    }

    internal enum MapResultCompletionDecision
    {
        /// <summary>不是本次 ticket 的 show（旧 iterator 迟到）：不消费、不写。</summary>
        Stale,
        /// <summary>本次 show 的完成已消费，但不能/不应写：native 结果原样保留。</summary>
        LeaveUnchanged,
        /// <summary>本次 show 的首次完成满足全部条件：允许一次 4 字节 int 提交。</summary>
        Commit
    }

    internal enum MapResultCommitOutcome
    {
        Unsupported,
        NoValue,
        InvalidHasValue,
        WrongValue,
        Committed
    }

    internal static class ExtensionIslandMapResultPlan
    {
        internal const int RequiredReturnSize = 8;
        internal const int RequiredNullableSize = 8;
        internal const int RequiredHasValueSize = 1;
        internal const int RequiredValueSize = 4;
        internal const int RequiredReturnValueOffset = 0;
        internal const int RequiredHasValueOffset = 0;
        internal const int RequiredValueOffset = 4;
        internal const int RequiredAlignment = 4;
        /// <summary>MapTimelineMenu.State.ShowingMapOnly。</summary>
        internal const int MenuStateShowingMapOnly = 2;
        /// <summary>MapTimelineMenu.State.ShowingWithTimeline。</summary>
        internal const int MenuStateShowingWithTimeline = 3;
        /// <summary>MapTimelineMenu.State.Opening。</summary>
        internal const int MenuStateOpening = 4;

        /// <summary>
        /// TryArm 前的 current-state 门：保留 native-compatible 的 &gt;=2 已知显示/打开态
        /// （ShowingMapOnly2/ShowingWithTimeline3/Opening4）；Closing0/Closed1 与未知值 false
        /// （fail-closed，不 Hide）。
        /// </summary>
        internal static bool IsArmMenuStateAllowed(int currentState)
            => currentState == MenuStateShowingMapOnly
            || currentState == MenuStateShowingWithTimeline
            || currentState == MenuStateOpening;

        /// <summary>
        /// AfterHide/Abort 的 exact handle 门：handle 存在、show 存在、menu 身份一致且票未完成/撤销。
        /// 旧 handle 指向旧 Ticket；新 show/new ticket 不会被旧调用清除。
        /// </summary>
        internal static bool CanUseConfirmationHandle(
            bool handleValid, bool showExists, bool menuMatches,
            bool revoked, bool consumed, bool committed)
            => handleValid && showExists && menuMatches && !revoked && !consumed && !committed;

        /// <summary>
        /// 布局硬门：任一事实不符都返回 false（调用方永久禁用本进程确认特性）。
        /// value-type field 的 metadata offset 按 boxed 对象坐标传入，本方法用实测公共 header
        /// displacement 归一化为 inline payload 坐标；实际 Return box 用于校验 header、
        /// boxed size/bounds 与 inline 字节一致性；Nullable 仅读取 class ABI。iterator.result 本身是 reference-type 对象坐标，
        /// 不减去 header。
        /// </summary>
        // Only metadata/class facts: safe to evaluate before any result payload is boxed/read.
        internal static bool TryAcceptMetadataShape(MapResultLayoutFacts f, out string reason)
        {
            reason = null;
            if (f == null) { reason = "facts-null"; return false; }
            if (!f.ResultFieldFound) { reason = "result-field-missing"; return false; }
            if (f.ResultFieldStatic) { reason = "result-field-static"; return false; }
            if (!f.ResultFieldTypeExact) { reason = "result-field-type"; return false; }
            if (f.ResultFieldOffset < IntPtr.Size * 2) { reason = "result-offset-header"; return false; }
            if (!f.ResultFieldInsideObject) { reason = "result-outside-object"; return false; }
            if (!f.ResultFieldAligned || (f.ResultFieldOffset & (RequiredAlignment - 1)) != 0)
            {
                reason = "result-unaligned";
                return false;
            }
            if (f.ReturnSize != RequiredReturnSize || !f.ReturnIsValueType)
            {
                reason = "return-size-or-type";
                return false;
            }
            if (!f.ReturnValueTypeExact
                || !f.ReturnNoReferenceFields
                || !f.ReturnExactFieldSet)
            {
                reason = "return-value-field";
                return false;
            }
            if (f.NullableSize != RequiredNullableSize || !f.NullableIsValueType)
            {
                reason = "nullable-size-or-type";
                return false;
            }
            if (f.NullableHasValueSize != RequiredHasValueSize
                || f.NullableValueSize != RequiredValueSize
                || !f.NullableHasValueTypeExact
                || !f.NullableValueTypeExact)
            {
                reason = "nullable-field-types";
                return false;
            }
            if (!f.NullableNoReferenceFields || !f.NullableExactFieldSet)
            {
                reason = "nullable-ref-payload";
                return false;
            }
            return true;
        }

        internal static bool TryAcceptLayout(MapResultLayoutFacts f, out MapResultLayout layout, out string reason)
        {
            layout = default;
            if (!TryAcceptMetadataShape(f, out reason)) return false;
            if (!TryNormalizeLayout(
                    f.ReturnValueOffset, f.NullableHasValueOffset, f.NullableValueOffset,
                    f.BoxedReturnHeaderDisplacement,
                    f.NullableClassInstanceSize, f.NullableClassHeaderDisplacement,
                    f.BoxedReturnObjectSize,
                    out int returnValuePayloadOffset, out int hasValuePayloadOffset,
                    out int valuePayloadOffset, out reason))
                return false;
            if (!f.BoxedReturnInlineMatches) { reason = "boxed-inline-mismatch"; return false; }
            if (!f.BoxedReturnFieldReadMatches) { reason = "boxed-field-read"; return false; }
            layout = new MapResultLayout(
                f.ResultFieldOffset, returnValuePayloadOffset, hasValuePayloadOffset, valuePayloadOffset);
            return true;
        }

        /// <summary>
        /// IL2CPP metadata 的 value-type field offset 是 boxed 坐标（相对 boxed 对象指针，含对象头）。
        /// 本方法用实测的 Return box object_unbox displacement 与 Nullable class instance size
        /// 减 value size 的 class ABI header 交叉校验，再归一化 Return.value / Nullable.hasValue/value
        /// 到 inline payload 坐标；Return 仅使用既有 getter 的装箱副本，不另行构造或装箱 Nullable，
        /// 也不把 class ABI 推导冒充 boxed Nullable 观测。任何 field_get_value / unsafe 访问前都校验对象大小与范围。
        /// iterator.result 本身的 offset 是 reference-type 对象坐标，不在此归一化。
        /// </summary>
        internal static bool TryNormalizeLayout(
            int returnValueRawOffset,
            int hasValueRawOffset,
            int valueRawOffset,
            long returnHeaderDisplacement,
            int nullableClassInstanceSize,
            long nullableClassHeaderDisplacement,
            uint returnBoxedSize,
            out int returnValuePayloadOffset,
            out int hasValuePayloadOffset,
            out int valuePayloadOffset,
            out string reason)
        {
            returnValuePayloadOffset = 0;
            hasValuePayloadOffset = 0;
            valuePayloadOffset = 0;
            reason = null;

            if (nullableClassInstanceSize < RequiredNullableSize)
            {
                reason = "nullable-instance-underflow";
                return false;
            }
            long derivedNullableHeader = (long)nullableClassInstanceSize - RequiredNullableSize;
            if (nullableClassHeaderDisplacement != derivedNullableHeader)
            {
                reason = "nullable-header-instance-mismatch";
                return false;
            }
            if (derivedNullableHeader + RequiredNullableSize != nullableClassInstanceSize)
            {
                reason = "nullable-instance-size";
                return false;
            }

            long minimumHeader = (long)IntPtr.Size * 2;
            if (returnHeaderDisplacement < minimumHeader
                || (returnHeaderDisplacement & (IntPtr.Size - 1)) != 0)
            {
                reason = "return-header";
                return false;
            }
            if (derivedNullableHeader != returnHeaderDisplacement)
            {
                reason = "header-mismatch";
                return false;
            }
            if ((long)nullableClassInstanceSize
                != returnHeaderDisplacement + RequiredNullableSize)
            {
                reason = "nullable-instance-header-mismatch";
                return false;
            }
            if (returnHeaderDisplacement + RequiredReturnSize > returnBoxedSize)
            {
                reason = "return-boxed-bounds";
                return false;
            }
            if (returnValueRawOffset < returnHeaderDisplacement
                || (long)returnValueRawOffset + RequiredReturnSize > returnBoxedSize)
            {
                reason = "return-raw-bounds";
                return false;
            }
            if (hasValueRawOffset < derivedNullableHeader
                || (long)hasValueRawOffset + RequiredHasValueSize > nullableClassInstanceSize)
            {
                reason = "hasvalue-raw-bounds";
                return false;
            }
            if (valueRawOffset < derivedNullableHeader
                || (long)valueRawOffset + RequiredValueSize > nullableClassInstanceSize)
            {
                reason = "value-raw-bounds";
                return false;
            }

            long returnPayload = (long)returnValueRawOffset - returnHeaderDisplacement;
            long hasValuePayload = (long)hasValueRawOffset - derivedNullableHeader;
            long valuePayload = (long)valueRawOffset - derivedNullableHeader;
            if (returnPayload != RequiredReturnValueOffset)
            {
                reason = "return-value-offset";
                return false;
            }
            if (hasValuePayload != RequiredHasValueOffset)
            {
                reason = "hasvalue-offset";
                return false;
            }
            if (valuePayload != RequiredValueOffset)
            {
                reason = "value-offset";
                return false;
            }
            if (returnPayload + RequiredNullableSize > RequiredReturnSize
                || hasValuePayload + RequiredHasValueSize > RequiredNullableSize
                || valuePayload + RequiredValueSize > RequiredNullableSize)
            {
                reason = "payload-bounds";
                return false;
            }

            returnValuePayloadOffset = (int)returnPayload;
            hasValuePayloadOffset = (int)hasValuePayload;
            valuePayloadOffset = (int)valuePayload;
            return true;
        }

        /// <summary>
        /// 唯一的原生内存写：<paramref name="returnField"/> 指向 iterator inline Return 起始。
        /// hasValue 必须恰为 1（规范 true），value 必须等于本次 UI；写仅覆盖 value 的 4 字节。
        /// </summary>
        internal static unsafe MapResultCommitOutcome TryCommit(
            byte* returnField, in MapResultLayout layout, int expectedUi, int physical)
        {
            if (returnField == null) return MapResultCommitOutcome.Unsupported;
            if (layout.ReturnValueOffset != RequiredReturnValueOffset
                || layout.HasValueOffset != RequiredHasValueOffset
                || layout.ValueOffset != RequiredValueOffset)
            {
                return MapResultCommitOutcome.Unsupported;
            }
            byte* nullable = returnField + layout.ReturnValueOffset;
            long valueAddress = (long)(nullable + layout.ValueOffset);
            if ((valueAddress & (RequiredAlignment - 1)) != 0)
                return MapResultCommitOutcome.Unsupported;
            byte hasValue = nullable[layout.HasValueOffset];
            if (hasValue == 0) return MapResultCommitOutcome.NoValue;
            if (hasValue != 1) return MapResultCommitOutcome.InvalidHasValue;
            if (*(int*)(nullable + layout.ValueOffset) != expectedUi)
                return MapResultCommitOutcome.WrongValue;
            *(int*)(nullable + layout.ValueOffset) = physical;
            return MapResultCommitOutcome.Committed;
        }

        internal static bool TryArm(ref MapResultTicket ticket, MapResultArmFacts f)
        {
            if (f == null) return false;
            if (!f.LayoutSupported || !f.TupleExact) return false;
            if (!f.ActiveShowExists || !f.ShowMatches) return false;
            if (f.AlreadyArmed || f.AlreadyCompleted) return false;
            if (!f.CampaignMatches || !f.TokenMatches) return false;
            if (!f.MenuStateAllowed || !f.ActiveMapMatches || !f.HideTargetValid) return false;
            ticket = new MapResultTicket
            {
                Armed = true,
                ExpectedUi = f.Ui,
                Physical = f.Physical
            };
            return true;
        }

        /// <summary>
        /// 首次完成裁决：非本 ticket 的 show 返回 Stale（不修改票）；本 ticket 的完成无论能否提交
        /// 都消费一次（Consumed），保证重复 false 不会把 A physical11 再按 B UI11 映射到 13。
        /// </summary>
        internal static MapResultCompletionDecision Complete(ref MapResultTicket ticket, MapResultCompletionFacts f)
        {
            if (f == null || !f.MatchesTicket) return MapResultCompletionDecision.Stale;
            if (!ticket.Armed || ticket.Consumed || ticket.Revoked)
                return MapResultCompletionDecision.LeaveUnchanged;
            ticket.Armed = false;
            ticket.Consumed = true;
            if (ticket.Committed) return MapResultCompletionDecision.LeaveUnchanged;
            if (!f.LayoutSupported) return MapResultCompletionDecision.LeaveUnchanged;
            if (!f.CampaignMatches || !f.TokenMatches) return MapResultCompletionDecision.LeaveUnchanged;
            if (!f.HasValue) return MapResultCompletionDecision.LeaveUnchanged;
            if (f.NativeValue != ticket.ExpectedUi) return MapResultCompletionDecision.LeaveUnchanged;
            return MapResultCompletionDecision.Commit;
        }

        internal static bool ShouldRevokeAfterHide(int menuState, in MapResultTicket ticket)
        {
            if (!ticket.Armed || ticket.Consumed || ticket.Committed || ticket.Revoked) return false;
            return menuState >= 2;   // Closing/Closed 不等待；仍 Showing/Opening 才撤销未提交票
        }

        internal static void Revoke(ref MapResultTicket ticket)
        {
            if (ticket.Committed || ticket.Consumed) return;   // 已同步完成的结果不可假回滚
            ticket.Revoked = true;
            ticket.Armed = false;
        }
    }
}
