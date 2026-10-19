using System;
using System.Collections.Generic;
using Coatsink.Common;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// #270 actual native BaseShowRoutine result handoff（UI → physical）。
    ///
    /// 边界（issue-270 decision-delta2 + verdict + native return API review）：
    /// - 只 hook 精确的 <c>MapTimelineMenu._BaseShowRoutine_d__76.MoveNext</c>（token 0x0600245e / RVA
    ///   0x732298 的 managed 类型）；不碰泛型 pop/getter/Return.op_Implicit。
    /// - Prefix 在 state0 捕获真实 show（owner/menu/iterator/campaign 强引用），每次调用把 exact show
    ///   放进 Harmony __state；Postfix 只在原调用正常返回 false 后处理该次 show。
    /// - Confirm Stage1 全部门通过、sound/getter 完成后、Hide 前调用 <see cref="TryArm"/>；ticket 绑定
    ///   expected UI/physical/owner/menu/iterator/show/campaign/登记 token。
    /// - 首次完成且 native hasValue 规范为 1、value == expected UI 时，只把 inline Return 的 4 字节
    ///   int 改成 physical；不写 hasValue/focus/target/Campaign，不构造 Nullable/Return、不 pop/peek。
    /// - None/非 expected UI/无票/旧 show/失配一律原样保留；重复 false 不二次映射（A physical11 不会
    ///   再按 B UI11 变 13）。Hide 早退/异常只撤销未提交票，已同步完成不可回滚；正常 Closing/Closed
    ///   不等待、不加 Tick。
    /// - 布局硬门来自 IL2CPP metadata：Return&lt;Nullable&lt;int&gt;&gt; 8B、Nullable 8B（bool0/int4）、
    ///   result 字段类型 exact、无引用字段；任一不符永久禁用本进程确认特性并只记一次可见日志。
    /// </summary>
    internal static class ExtensionIslandMapResult
    {
        private const string LogPrefix = "[ExtensionIslandMapResult] ";
        private const int FieldAttributeStatic = 0x0010;
        private const int MaxLayoutFields = 64;

        private static readonly string[] ReturnInstanceFields = { "value" };
        private static readonly string[] NullableInstanceFields = { "hasValue", "value" };
        private static readonly HashSet<string> LoggedKeys = new HashSet<string>(StringComparer.Ordinal);

        private static bool _layoutChecked;
        private static bool _layoutSupported;
        private static MapResultLayout _layout;
        private static string _layoutEvidence;

        private static Show _activeShow;

        private sealed class Show
        {
            internal IntPtr MenuPointer;
            internal IntPtr IteratorPointer;
            internal MapTimelineMenu._BaseShowRoutine_d__76 Iterator;
            internal IntPtr Campaign;
            internal Ticket Ticket;
            internal bool Completed;
            internal bool Revoked;
        }

        private sealed class Ticket
        {
            internal Show Show;
            internal MapResultTicket State;
            internal object Token;
            internal IntPtr Campaign;
        }

        // ------------------------------------------------------------------ Harmony 入口

        /// <summary>MoveNext Prefix：state0 建 show；其余调用只返回 exact iterator 所属 show。</summary>
        internal static object Capture(MapTimelineMenu._BaseShowRoutine_d__76 iterator)
        {
            try
            {
                if (iterator == null) return null;
                IntPtr pointer = iterator.Pointer;
                if (pointer == IntPtr.Zero) return null;

                if (iterator.__1__state == 0)
                {
                    if (!EnsureLayout(iterator)) return null;
                    return BeginShow(iterator);
                }

                Show active = _activeShow;
                return active != null && active.IteratorPointer == pointer ? active : null;
            }
            catch (Exception e)
            {
                LogOnce("capture", e);
                return null;
            }
        }

        /// <summary>MoveNext Postfix（仅原调用返回 false）：首次完成一次性消费。</summary>
        internal static void Completed(MapTimelineMenu._BaseShowRoutine_d__76 iterator, object state)
        {
            try
            {
                Show show = state as Show;
                if (show == null || iterator == null) return;
                IntPtr pointer = iterator.Pointer;
                if (pointer == IntPtr.Zero || pointer != show.IteratorPointer) return;
                if (show.Completed) return;   // 重复 false：同一次 show 绝不二次处理
                show.Completed = true;

                Ticket ticket = show.Ticket;
                if (ticket == null)
                {
                    ReleaseShow(show);
                    return;
                }

                bool campaignOk = CurrentCampaignPointer() == ticket.Campaign;
                bool tokenOk = false;
                try
                {
                    tokenOk = ExtensionIslandMap.TryGetVisualOwnerTokenForLand(ticket.State.Physical, out object currentToken)
                        && ReferenceEquals(currentToken, ticket.Token);
                }
                catch (Exception)
                {
                    tokenOk = false;
                }

                bool hasValue = false;
                int nativeValue = 0;
                bool readOk = TryReadInline(show, out hasValue, out nativeValue);

                var facts = new MapResultCompletionFacts
                {
                    MatchesTicket = ticket.Show == show && !show.Revoked,
                    LayoutSupported = _layoutSupported && readOk,
                    CampaignMatches = campaignOk,
                    TokenMatches = tokenOk,
                    HasValue = hasValue,
                    NativeValue = nativeValue
                };

                MapResultCompletionDecision decision = ExtensionIslandMapResultPlan.Complete(ref ticket.State, facts);
                if (decision == MapResultCompletionDecision.Commit)
                {
                    if (TryCommitInline(show, ticket, out MapResultCommitOutcome outcome))
                    {
                        ticket.State.Committed = true;
                        LogInfoOnce("commit", "committed native result: pid=" + Environment.ProcessId
                            + " ui=" + ticket.State.ExpectedUi + " physical=" + ticket.State.Physical
                            + " iterator=0x" + show.IteratorPointer.ToString("x")
                            + " menu=0x" + show.MenuPointer.ToString("x"));
                    }
                    else
                    {
                        LogErrorOnce("commit-" + outcome, "commit skipped: " + outcome);
                    }
                }

                ReleaseShow(show);
            }
            catch (Exception e)
            {
                LogOnce("completed", e);
            }
        }

        // ------------------------------------------------------------------ Operator 接口

        /// <summary>
        /// Stage1 成功路径、Hide 前绑定一次 ticket。返回 false 时调用者必须 consume 且不 Hide；
        /// 本方法自身重查 exact active show/menu/owner/iterator、当前 Menu.ActiveMap、current state、
        /// 实际 Hide target、合法 tuple、当前 campaign 与当前 slot world token。布局不支持同样 false。
        /// 成功后通过 <paramref name="confirmationHandle"/> 返回 exact ticket；调用者必须在 Hide 后
        /// 原样传给 AfterHide/Abort，禁止借用全局 last-ticket。
        /// </summary>
        internal static bool TryArm(
            MapTimelineMenuGreece menu, Menu hide, int ui, int physical,
            object registrationToken, IntPtr campaign, out object confirmationHandle)
        {
            confirmationHandle = null;
            try
            {
                if (!_layoutSupported || menu == null || hide == null
                    || registrationToken == null || campaign == IntPtr.Zero)
                    return false;
                if (!MountIslandSplitPolicy.TryGetPhysicalIndex(ui, out int expectedPhysical)
                    || expectedPhysical != physical)
                    return false;

                Show show = _activeShow;
                if (show == null || show.Completed || show.Revoked) return false;
                if (show.Ticket != null) return false;

                IntPtr menuPointer = menu.Pointer;
                IntPtr hidePointer = hide.Pointer;
                if (menuPointer == IntPtr.Zero || hidePointer == IntPtr.Zero) return false;
                if (show.MenuPointer != menuPointer) return false;
                if (show.Iterator == null || show.IteratorPointer == IntPtr.Zero
                    || show.Iterator.Pointer != show.IteratorPointer)
                    return false;

                IntPtr ownerNow = IntPtr.Zero;
                try
                {
                    MapTimelineMenu owner = show.Iterator.__4__this;
                    if (owner != null) ownerNow = owner.Pointer;
                }
                catch (Exception)
                {
                    ownerNow = IntPtr.Zero;
                }
                if (ownerNow != menuPointer) return false;

                bool finalCampaignMatches = show.Campaign == campaign
                    && CurrentCampaignPointer() == campaign;
                if (!finalCampaignMatches) return false;
                bool finalTokenMatches = false;
                try
                {
                    finalTokenMatches = ExtensionIslandMap.TryGetVisualOwnerTokenForLand(physical, out object finalToken)
                        && ReferenceEquals(finalToken, registrationToken);
                }
                catch (Exception)
                {
                    finalTokenMatches = false;
                }
                if (!finalTokenMatches) return false;

                // 最后关闭门：上面的 getter 若同步触发回调/新 show，旧 show 必须作废；
                // 重新读取 state/ActiveMap/Hide target，并用同一局部 hide 进入 arm+Hide。
                int finalState;
                try { finalState = (int)menu.CurrentState; }
                catch (Exception) { return false; }
                bool finalStateAllowed = ExtensionIslandMapResultPlan.IsArmMenuStateAllowed(finalState);
                bool finalActiveMap = false;
                try
                {
                    MapTimelineMenu active = hide.ActiveMap;
                    finalActiveMap = active != null && active.Pointer == menuPointer;
                }
                catch (Exception)
                {
                    finalActiveMap = false;
                }
                bool finalHideTarget = false;
                try
                {
                    Menu inst = Menu.InstExists ? Menu.Inst : null;
                    if (inst != null)
                    {
                        finalHideTarget = inst.Pointer == hidePointer;
                    }
                    else
                    {
                        Menu own = menu._menu;
                        finalHideTarget = own != null && own.Pointer == hidePointer;
                    }
                }
                catch (Exception)
                {
                    finalHideTarget = false;
                }
                if (!finalStateAllowed || !finalActiveMap || !finalHideTarget
                    || !ExtensionIslandMap.IsActiveMenu(menu))
                    return false;

                bool showStillActive = IsShowStillActive(show);
                if (!showStillActive) return false;

                var state = new MapResultTicket();
                var facts = new MapResultArmFacts
                {
                    LayoutSupported = _layoutSupported,
                    TupleExact = expectedPhysical == physical,
                    ActiveShowExists = showStillActive,
                    ShowMatches = showStillActive,
                    AlreadyArmed = show.Ticket != null,
                    AlreadyCompleted = show.Completed || show.Revoked,
                    CampaignMatches = finalCampaignMatches,
                    TokenMatches = finalTokenMatches,
                    MenuStateAllowed = finalStateAllowed,
                    ActiveMapMatches = finalActiveMap,
                    HideTargetValid = finalHideTarget,
                    Ui = ui,
                    Physical = physical
                };
                if (!ExtensionIslandMapResultPlan.TryArm(ref state, facts)) return false;

                var ticket = new Ticket
                {
                    Show = show,
                    State = state,
                    Token = registrationToken,
                    Campaign = campaign
                };
                show.Ticket = ticket;
                confirmationHandle = ticket;
                return true;
            }
            catch (Exception e)
            {
                LogOnce("arm", e);
                return false;
            }
        }

        /// <summary>
        /// Hide 返回后调用 exact handle：仍 Showing/Opening（state &gt;= 2）才撤销尚未提交票；
        /// Closing/Closed（0/1）不等待、不加 Tick，合法 UI 关闭保留 pending 等待 native 完成。
        /// handle 失配/旧 show/外来 menu 一律 no-op，不触碰新 show 的新票。
        /// </summary>
        internal static void AfterHide(MapTimelineMenuGreece menu, object confirmationHandle)
        {
            try
            {
                if (!(confirmationHandle is Ticket ticket)) return;
                Show show = ticket.Show;
                bool showExists = show != null;
                bool menuMatches = showExists && menu != null && show.MenuPointer == menu.Pointer;
                bool handleCurrent = showExists && ReferenceEquals(show.Ticket, ticket);
                if (!ExtensionIslandMapResultPlan.CanUseConfirmationHandle(
                        handleCurrent, showExists, menuMatches,
                        ticket.State.Revoked, ticket.State.Consumed, ticket.State.Committed))
                    return;

                int state;
                try { state = (int)menu.CurrentState; }
                catch (Exception) { state = 2; }   // 读未知 fail-closed
                if (ExtensionIslandMapResultPlan.ShouldRevokeAfterHide(state, ticket.State))
                    Revoke(ticket, "hide-still-showing");
            }
            catch (Exception e)
            {
                LogOnce("after-hide", e);
            }
        }

        /// <summary>
        /// Hide 抛异常时只撤销 exact handle 的未提交票；旧 handle/外来 menu no-op，
        /// 已同步完成（Consumed/Committed）不可假回滚。
        /// </summary>
        internal static void Abort(MapTimelineMenuGreece menu, object confirmationHandle)
        {
            try
            {
                if (!(confirmationHandle is Ticket ticket)) return;
                Show show = ticket.Show;
                bool showExists = show != null;
                bool menuMatches = showExists && menu != null && show.MenuPointer == menu.Pointer;
                bool handleCurrent = showExists && ReferenceEquals(show.Ticket, ticket);
                if (!ExtensionIslandMapResultPlan.CanUseConfirmationHandle(
                        handleCurrent, showExists, menuMatches,
                        ticket.State.Revoked, ticket.State.Consumed, ticket.State.Committed))
                    return;
                Revoke(ticket, "hide-abort");
            }
            catch (Exception e)
            {
                LogOnce("abort", e);
            }
        }

        /// <summary>native ClearLands：只撤销该 menu 当前 show/ticket，不影响其它 owner 或后续新 show。</summary>
        internal static void Clear(MapTimelineMenuGreece menu)
        {
            try
            {
                if (menu == null) return;
                IntPtr pointer = menu.Pointer;
                if (pointer == IntPtr.Zero) return;

                Show show = _activeShow;
                if (show != null && show.MenuPointer == pointer)
                {
                    if (show.Ticket != null) Revoke(show.Ticket, "native-clear");
                    ReleaseShow(show);
                }
            }
            catch (Exception e)
            {
                LogOnce("clear", e);
            }
        }

        // ------------------------------------------------------------------ show 生命周期

        private static Show BeginShow(MapTimelineMenu._BaseShowRoutine_d__76 iterator)
        {
            IntPtr iteratorPointer = iterator.Pointer;
            IntPtr ownerPointer = IntPtr.Zero;
            try
            {
                MapTimelineMenu owner = iterator.__4__this;
                if (owner != null) ownerPointer = owner.Pointer;
            }
            catch (Exception)
            {
                ownerPointer = IntPtr.Zero;
            }

            var show = new Show
            {
                MenuPointer = ownerPointer,
                IteratorPointer = iteratorPointer,
                Iterator = iterator,
                Campaign = CurrentCampaignPointer()
            };

            Show previous = _activeShow;
            if (previous != null && !ReferenceEquals(previous, show))
            {
                previous.Revoked = true;
                if (previous.Ticket != null) Revoke(previous.Ticket, "new-show");
                ReleaseShow(previous);
            }
            _activeShow = show;
            return show;
        }

        /// <summary>arm 最后一道 show 代次/iterator 门：任何同步回调换 show 都使旧资格失效。</summary>
        private static bool IsShowStillActive(Show show)
        {
            if (show == null || !ReferenceEquals(_activeShow, show)) return false;
            if (show.Completed || show.Revoked || show.Ticket != null) return false;
            if (show.Iterator == null || show.Iterator.Pointer != show.IteratorPointer) return false;
            int state;
            try { state = show.Iterator.__1__state; }
            catch (Exception) { return false; }
            if (state < 0) return false;
            // __1__state getter 期间若发生重入换 show，也必须拒绝。
            return ReferenceEquals(_activeShow, show) && !show.Revoked && show.Ticket == null;
        }

        private static void ReleaseShow(Show show)
        {
            if (show == null) return;
            show.Completed = true;
            show.Ticket = null;
            if (ReferenceEquals(_activeShow, show)) _activeShow = null;
            show.Iterator = null;
        }

        private static void Revoke(Ticket ticket, string reason)
        {
            if (ticket == null) return;
            if (ticket.State.Revoked || ticket.State.Committed || ticket.State.Consumed) return;
            ExtensionIslandMapResultPlan.Revoke(ref ticket.State);
            if (!ticket.State.Revoked) return;
            if (ticket.Show != null)
            {
                ticket.Show.Revoked = true;
                if (ReferenceEquals(ticket.Show.Ticket, ticket)) ticket.Show.Ticket = null;
            }
            LogInfoOnce("revoke-" + reason, "ticket revoked: " + reason);
        }

        // ------------------------------------------------------------------ inline result 读写

        private static unsafe bool TryReadInline(Show show, out bool hasValue, out int value)
        {
            hasValue = false;
            value = 0;
            if (!_layoutSupported || show == null) return false;
            IntPtr pointer = show.IteratorPointer;
            if (pointer == IntPtr.Zero || show.Iterator == null || show.Iterator.Pointer != pointer)
                return false;

            uint objectSize = IL2CPP.il2cpp_object_get_size(pointer);
            if (_layout.ResultFieldOffset < 0
                || (long)_layout.ResultFieldOffset + ExtensionIslandMapResultPlan.RequiredReturnSize > objectSize)
                return false;

            byte* field = (byte*)pointer + _layout.ResultFieldOffset;
            if (((long)(field + _layout.ValueOffset) & (ExtensionIslandMapResultPlan.RequiredAlignment - 1)) != 0)
                return false;

            byte raw = field[_layout.HasValueOffset];
            if (raw == 0) return true;
            if (raw != 1) return false;
            hasValue = true;
            value = *(int*)(field + _layout.ValueOffset);
            return true;
        }

        private static unsafe bool TryCommitInline(Show show, Ticket ticket, out MapResultCommitOutcome outcome)
        {
            outcome = MapResultCommitOutcome.Unsupported;
            if (!_layoutSupported || show == null || ticket == null) return false;
            IntPtr pointer = show.IteratorPointer;
            if (pointer == IntPtr.Zero || show.Iterator == null || show.Iterator.Pointer != pointer)
                return false;

            uint objectSize = IL2CPP.il2cpp_object_get_size(pointer);
            if (_layout.ResultFieldOffset < 0
                || (long)_layout.ResultFieldOffset + ExtensionIslandMapResultPlan.RequiredReturnSize > objectSize)
                return false;

            byte* field = (byte*)pointer + _layout.ResultFieldOffset;
            outcome = ExtensionIslandMapResultPlan.TryCommit(
                field, _layout, ticket.State.ExpectedUi, ticket.State.Physical);
            return outcome == MapResultCommitOutcome.Committed;
        }

        // ------------------------------------------------------------------ 布局硬门

        private static bool EnsureLayout(MapTimelineMenu._BaseShowRoutine_d__76 iterator)
        {
            if (_layoutChecked) return _layoutSupported;
            _layoutChecked = true;

            string reason = null;
            bool ok = false;
            try
            {
                ok = TryResolveLayout(iterator, out _layout, out reason);
            }
            catch (Exception e)
            {
                reason = e.GetType().Name + " " + e.Message;
                ok = false;
            }

            _layoutSupported = ok;
            if (!ok)
            {
                InvalidateForLayoutFailure();
                LogErrorOnce("layout", "confirmation disabled: " + (reason ?? "unknown"));
            }
            else
            {
                LogInfoOnce("layout-ok", _layoutEvidence);
            }
            return ok;
        }

        private static void InvalidateForLayoutFailure()
        {
            Show show = _activeShow;
            if (show != null)
            {
                if (show.Ticket != null) Revoke(show.Ticket, "layout-disabled");
                ReleaseShow(show);
            }
        }

        private static unsafe bool TryResolveLayout(
            MapTimelineMenu._BaseShowRoutine_d__76 iterator, out MapResultLayout layout, out string reason)
        {
            layout = default;
            reason = null;
            var facts = new MapResultLayoutFacts();

            IntPtr iteratorPointer = iterator.Pointer;
            if (iteratorPointer == IntPtr.Zero) { reason = "iterator-pointer"; return false; }
            IntPtr iteratorClass = Il2CppClassPointerStore<MapTimelineMenu._BaseShowRoutine_d__76>.NativeClassPtr;
            if (iteratorClass == IntPtr.Zero) { reason = "iterator-class"; return false; }
            if (IL2CPP.il2cpp_object_get_class(iteratorPointer) != iteratorClass)
            {
                reason = "iterator-class-mismatch";
                return false;
            }

            IntPtr resultField = IL2CPP.il2cpp_class_get_field_from_name(iteratorClass, "result");
            if (resultField == IntPtr.Zero) { reason = "result-field-missing"; return false; }
            facts.ResultFieldFound = true;
            facts.ResultFieldStatic = (IL2CPP.il2cpp_field_get_flags(resultField) & FieldAttributeStatic) != 0;
            int resultOffset = unchecked((int)IL2CPP.il2cpp_field_get_offset(resultField));
            facts.ResultFieldOffset = resultOffset;
            uint iteratorSize = IL2CPP.il2cpp_object_get_size(iteratorPointer);
            facts.ResultFieldInsideObject = resultOffset >= 0
                && (long)resultOffset + ExtensionIslandMapResultPlan.RequiredReturnSize <= iteratorSize;
            facts.ResultFieldAligned = (resultOffset & (ExtensionIslandMapResultPlan.RequiredAlignment - 1)) == 0
                && (((long)iteratorPointer + resultOffset) & (ExtensionIslandMapResultPlan.RequiredAlignment - 1)) == 0;

            IntPtr returnClass = Il2CppClassPointerStore<Routine.Return<Il2CppSystem.Nullable<int>>>.NativeClassPtr;
            if (returnClass == IntPtr.Zero) { reason = "return-class"; return false; }
            IntPtr resultType = IL2CPP.il2cpp_field_get_type(resultField);
            facts.ResultFieldTypeExact = resultType != IntPtr.Zero
                && IL2CPP.il2cpp_class_from_type(resultType) == returnClass;

            uint align = 0;
            facts.ReturnIsValueType = IL2CPP.il2cpp_class_is_valuetype(returnClass);
            if (!facts.ReturnIsValueType) { reason = "return-not-valuetype"; return false; }
            facts.ReturnSize = IL2CPP.il2cpp_class_value_size(returnClass, ref align);

            IntPtr nullableClass = Il2CppClassPointerStore<Il2CppSystem.Nullable<int>>.NativeClassPtr;
            IntPtr returnValueField = IL2CPP.il2cpp_class_get_field_from_name(returnClass, "value");
            if (returnValueField != IntPtr.Zero)
            {
                facts.ReturnValueOffset = unchecked((int)IL2CPP.il2cpp_field_get_offset(returnValueField));
                IntPtr returnValueType = IL2CPP.il2cpp_field_get_type(returnValueField);
                facts.ReturnValueTypeExact = nullableClass != IntPtr.Zero
                    && returnValueType != IntPtr.Zero
                    && IL2CPP.il2cpp_class_from_type(returnValueType) == nullableClass;
            }
            facts.ReturnNoReferenceFields = HasExactValueInstanceFields(returnClass, ReturnInstanceFields);
            facts.ReturnExactFieldSet = facts.ReturnNoReferenceFields;

            if (nullableClass != IntPtr.Zero)
            {
                facts.NullableIsValueType = IL2CPP.il2cpp_class_is_valuetype(nullableClass);
                if (!facts.NullableIsValueType) { reason = "nullable-not-valuetype"; return false; }
                facts.NullableSize = IL2CPP.il2cpp_class_value_size(nullableClass, ref align);
                int nullableInstanceSize = IL2CPP.il2cpp_class_instance_size(nullableClass);
                facts.NullableClassInstanceSize = nullableInstanceSize;
                long nullableHeader = -1;
                if (nullableInstanceSize >= ExtensionIslandMapResultPlan.RequiredNullableSize)
                    nullableHeader = (long)nullableInstanceSize
                        - ExtensionIslandMapResultPlan.RequiredNullableSize;
                facts.NullableClassHeaderDisplacement = nullableHeader;

                IntPtr hasValueField = IL2CPP.il2cpp_class_get_field_from_name(nullableClass, "hasValue");
                IntPtr valueField = IL2CPP.il2cpp_class_get_field_from_name(nullableClass, "value");

                if (hasValueField != IntPtr.Zero)
                {
                    facts.NullableHasValueOffset = unchecked((int)IL2CPP.il2cpp_field_get_offset(hasValueField));
                    IntPtr hasValueType = IL2CPP.il2cpp_field_get_type(hasValueField);
                    IntPtr hasValueClass = hasValueType == IntPtr.Zero
                        ? IntPtr.Zero
                        : IL2CPP.il2cpp_class_from_type(hasValueType);
                    facts.NullableHasValueTypeExact = IsSystemPrimitive(hasValueClass, "Boolean", 1);
                    if (facts.NullableHasValueTypeExact)
                        facts.NullableHasValueSize = IL2CPP.il2cpp_class_value_size(hasValueClass, ref align);
                }
                if (valueField != IntPtr.Zero)
                {
                    facts.NullableValueOffset = unchecked((int)IL2CPP.il2cpp_field_get_offset(valueField));
                    IntPtr valueType = IL2CPP.il2cpp_field_get_type(valueField);
                    IntPtr valueClass = valueType == IntPtr.Zero
                        ? IntPtr.Zero
                        : IL2CPP.il2cpp_class_from_type(valueType);
                    facts.NullableValueTypeExact = IsSystemPrimitive(valueClass, "Int32", 4);
                    if (facts.NullableValueTypeExact)
                        facts.NullableValueSize = IL2CPP.il2cpp_class_value_size(valueClass, ref align);
                }

                facts.NullableNoReferenceFields = HasExactValueInstanceFields(nullableClass, NullableInstanceFields);
                facts.NullableExactFieldSet = facts.NullableNoReferenceFields;
            }

            // Validate the exact result slot, field types and sizes before the getter boxes its bytes.
            if (!ExtensionIslandMapResultPlan.TryAcceptMetadataShape(facts, out reason)) return false;

            Routine.Return<Il2CppSystem.Nullable<int>> boxed;
            try
            {
                boxed = iterator.result;   // 仅用于布局校验：装箱副本 + object_unbox，绝不写回该副本
            }
            catch (Exception e)
            {
                reason = "result-getter:" + e.GetType().Name;
                return false;
            }
            if (boxed == null || boxed.Pointer == IntPtr.Zero) { reason = "boxed-return-null"; return false; }

            IntPtr boxPointer = boxed.Pointer;
            if (boxPointer == IntPtr.Zero
                || IL2CPP.il2cpp_object_get_class(boxPointer) != returnClass)
            {
                reason = "boxed-return-class";
                return false;
            }
            IntPtr payload = IL2CPP.il2cpp_object_unbox(boxPointer);
            uint boxedSize = IL2CPP.il2cpp_object_get_size(boxPointer);
            facts.BoxedReturnObjectSize = boxedSize;
            facts.BoxedReturnHeaderDisplacement = payload == IntPtr.Zero
                ? 0
                : (long)payload - (long)boxPointer;

            // Prove both object/payload ranges and normalize boxed field coordinates before reading bytes.
            if (!ExtensionIslandMapResultPlan.TryNormalizeLayout(
                    facts.ReturnValueOffset, facts.NullableHasValueOffset, facts.NullableValueOffset,
                    facts.BoxedReturnHeaderDisplacement,
                    facts.NullableClassInstanceSize, facts.NullableClassHeaderDisplacement,
                    facts.BoxedReturnObjectSize, out _, out _, out _, out reason))
            {
                GC.KeepAlive(boxed);
                return false;
            }

            byte* inlineResult = (byte*)iteratorPointer + resultOffset;
            facts.BoxedReturnInlineMatches = BytesEqual(
                inlineResult, (byte*)payload, ExtensionIslandMapResultPlan.RequiredReturnSize);
            byte* buffer = stackalloc byte[ExtensionIslandMapResultPlan.RequiredReturnSize];
            for (int i = 0; i < ExtensionIslandMapResultPlan.RequiredReturnSize; i++) buffer[i] = 0;
            IL2CPP.il2cpp_field_get_value(boxPointer, returnValueField, buffer);
            facts.BoxedReturnFieldReadMatches = BytesEqual(
                inlineResult, buffer, ExtensionIslandMapResultPlan.RequiredReturnSize);
            GC.KeepAlive(boxed);

            if (!ExtensionIslandMapResultPlan.TryAcceptLayout(facts, out MapResultLayout accepted, out reason))
                return false;
            layout = accepted;
            _layoutEvidence = "layout ok: pid=" + Environment.ProcessId
                + " resultOffset=" + resultOffset + " Return=" + facts.ReturnSize
                + " Nullable=" + facts.NullableSize
                + " raw=" + facts.ReturnValueOffset + "/" + facts.NullableHasValueOffset + "/" + facts.NullableValueOffset
                + " header=" + facts.BoxedReturnHeaderDisplacement
                + " nullableClassSize=" + facts.NullableClassInstanceSize
                + " payload=" + accepted.ReturnValueOffset + "/" + accepted.HasValueOffset + "/" + accepted.ValueOffset;
            return true;
        }

        /// <summary>
        /// Nullable 的 hasValue/value 必须是规范 System.Boolean/System.Int32（metadata 名称 + 命名空间 +
        /// value size），不依赖 primitive 的 Il2CppClassPointerStore 初始化。
        /// </summary>
        private static bool IsSystemPrimitive(IntPtr valueTypeClass, string name, int expectedSize)
        {
            if (valueTypeClass == IntPtr.Zero) return false;
            if (!IL2CPP.il2cpp_class_is_valuetype(valueTypeClass)) return false;
            string ns = IL2CPP.il2cpp_class_get_namespace_(valueTypeClass);
            string actualName = IL2CPP.il2cpp_class_get_name_(valueTypeClass);
            if (!string.Equals(ns, "System", StringComparison.Ordinal)
                || !string.Equals(actualName, name, StringComparison.Ordinal))
                return false;
            uint align = 0;
            return IL2CPP.il2cpp_class_value_size(valueTypeClass, ref align) == expectedSize;
        }

        /// <summary>
        /// 只接受 expected 中列出的全部实例字段、且每个字段都是值类型；任何额外实例字段或引用字段
        /// 都拒绝（Return/Nullable 是纯 8 字节无引用 payload）。静态字段不占实例 payload。
        /// </summary>
        private static bool HasExactValueInstanceFields(IntPtr valueTypeClass, string[] expected)
        {
            if (valueTypeClass == IntPtr.Zero || expected == null || expected.Length == 0) return false;
            var seen = new bool[expected.Length];
            int count = 0;
            IntPtr iterator = IntPtr.Zero;
            for (int guard = 0; guard < MaxLayoutFields; guard++)
            {
                IntPtr field = IL2CPP.il2cpp_class_get_fields(valueTypeClass, ref iterator);
                if (field == IntPtr.Zero)
                {
                    if (count != expected.Length) return false;
                    for (int i = 0; i < seen.Length; i++)
                        if (!seen[i]) return false;
                    return true;
                }

                int flags = IL2CPP.il2cpp_field_get_flags(field);
                if ((flags & FieldAttributeStatic) != 0) continue;   // 静态字段不在实例 layout 内

                IntPtr fieldType = IL2CPP.il2cpp_field_get_type(field);
                if (fieldType == IntPtr.Zero) return false;
                IntPtr fieldClass = IL2CPP.il2cpp_class_from_type(fieldType);
                if (fieldClass == IntPtr.Zero || !IL2CPP.il2cpp_class_is_valuetype(fieldClass))
                    return false;   // 引用字段 = 不允许的 payload

                string name = IL2CPP.il2cpp_field_get_name_(field);
                int index = Array.IndexOf(expected, name);
                if (index < 0 || seen[index]) return false;
                seen[index] = true;
                count++;
            }
            return false;
        }

        // ------------------------------------------------------------------ 小工具

        private static IntPtr CurrentCampaignPointer()
        {
            try
            {
                CampaignSaveData campaign = CampaignSaveData.current;
                return campaign != null ? campaign.Pointer : IntPtr.Zero;
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }
        }

        private static unsafe bool BytesEqual(byte* left, byte* right, int count)
        {
            if (left == null || right == null) return false;
            for (int i = 0; i < count; i++)
                if (left[i] != right[i]) return false;
            return true;
        }

        private static void LogInfoOnce(string key, string message)
        {
            try
            {
                if (!LoggedKeys.Add(key)) return;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(LogPrefix + message);
            }
            catch (Exception) { }
        }

        private static void LogErrorOnce(string key, string message)
        {
            try
            {
                if (!LoggedKeys.Add(key)) return;
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(LogPrefix + message);
            }
            catch (Exception) { }
        }

        private static void LogOnce(string key, Exception e)
        {
            LogErrorOnce(key, e.GetType().Name + ": " + e.Message);
        }
    }

    [HarmonyPatch(typeof(MapTimelineMenu._BaseShowRoutine_d__76), "MoveNext")]
    internal static class ExtensionIslandMapResult_BaseShowMoveNext_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(MapTimelineMenu._BaseShowRoutine_d__76 __instance, out object __state)
        {
            __state = ExtensionIslandMapResult.Capture(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(MapTimelineMenu._BaseShowRoutine_d__76 __instance, bool __result, object __state)
        {
            if (!__result) ExtensionIslandMapResult.Completed(__instance, __state);
        }
    }
}
