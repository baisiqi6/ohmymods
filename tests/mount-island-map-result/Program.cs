using KingdomEnhancedMod;

/// <summary>
/// #270 handoff 回归：编译真实 ExtensionIslandMapResultPlan，验证
/// 布局硬门、ticket 一次性提交、None/失配原样保留、重复 false 不二次映射、
/// Hide 早退/异常/同步完成边界，以及 inline 4 字节写只改 value。
/// </summary>
internal static unsafe class Program
{
    private static int Passed;
    private static int Failed;
    private static int Assertions;

    private static void Test(string name, Action body)
    {
        try
        {
            body();
            Passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            Failed++;
            Console.WriteLine("FAIL " + name + ": " + e.GetBaseException().Message);
        }
    }

    private static void Check(bool condition, string why)
    {
        Assertions++;
        if (!condition) throw new Exception(why);
    }

    private static void Eq<T>(T expected, T actual, string why)
    {
        Assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{why}: expected {expected}, got {actual}");
    }

    private static MapResultLayoutFacts ValidLayoutFacts() => new MapResultLayoutFacts
    {
        ResultFieldFound = true,
        ResultFieldStatic = false,
        ResultFieldTypeExact = true,
        ResultFieldOffset = 40,
        ResultFieldInsideObject = true,
        ResultFieldAligned = true,
        ReturnSize = 8,
        ReturnIsValueType = true,
        ReturnValueOffset = 16,
        ReturnValueTypeExact = true,
        ReturnNoReferenceFields = true,
        ReturnExactFieldSet = true,
        NullableSize = 8,
        NullableIsValueType = true,
        NullableHasValueOffset = 16,
        NullableValueOffset = 20,
        NullableHasValueTypeExact = true,
        NullableValueTypeExact = true,
        NullableHasValueSize = 1,
        NullableValueSize = 4,
        NullableNoReferenceFields = true,
        NullableExactFieldSet = true,
        BoxedReturnHeaderDisplacement = 16,
        BoxedReturnObjectSize = 24,
        NullableClassInstanceSize = 24,
        NullableClassHeaderDisplacement = 16,
        BoxedReturnInlineMatches = true,
        BoxedReturnFieldReadMatches = true
    };

    private static MapResultArmFacts ValidArmFacts(int ui = 10, int physical = 11) => new MapResultArmFacts
    {
        LayoutSupported = true,
        TupleExact = MountIslandSplitPolicy.TryGetPhysicalIndex(ui, out int expected) && expected == physical,
        ActiveShowExists = true,
        ShowMatches = true,
        AlreadyArmed = false,
        AlreadyCompleted = false,
        CampaignMatches = true,
        TokenMatches = true,
        MenuStateAllowed = true,
        ActiveMapMatches = true,
        HideTargetValid = true,
        Ui = ui,
        Physical = physical
    };

    private static MapResultTicket ArmedTicket(int ui = 10, int physical = 11)
    {
        var ticket = new MapResultTicket();
        Check(ExtensionIslandMapResultPlan.TryArm(ref ticket, ValidArmFacts(ui, physical)), "arm fixture");
        return ticket;
    }

    private static MapResultCompletionFacts ValidCompletion(
        MapResultTicket ticket, bool hasValue = true, int? nativeValue = null, bool matches = true) =>
        new MapResultCompletionFacts
        {
            MatchesTicket = matches,
            LayoutSupported = true,
            CampaignMatches = true,
            TokenMatches = true,
            HasValue = hasValue,
            NativeValue = nativeValue ?? ticket.ExpectedUi
        };

    private static MapResultLayout ValidLayout() => new MapResultLayout(40, 0, 0, 4);

    private static byte[] Buffer(int hasValue, int value, byte padding)
    {
        var buffer = new byte[8];
        buffer[0] = (byte)hasValue;
        buffer[1] = padding;
        buffer[2] = (byte)(padding + 1);
        buffer[3] = (byte)(padding + 2);
        buffer[4] = (byte)value;
        buffer[5] = (byte)(value >> 8);
        buffer[6] = (byte)(value >> 16);
        buffer[7] = (byte)(value >> 24);
        return buffer;
    }

    private static int ReadValue(byte[] buffer)
        => buffer[4] | (buffer[5] << 8) | (buffer[6] << 16) | (buffer[7] << 24);

    private static MapResultCommitOutcome Commit(byte[] buffer, int expectedUi, int physical)
    {
        fixed (byte* pointer = buffer)
            return ExtensionIslandMapResultPlan.TryCommit(pointer, ValidLayout(), expectedUi, physical);
    }

    public static int Main()
    {
        Test("A UI10 -> physical11：完成交接只写 4 字节", () =>
        {
            var ticket = ArmedTicket(10, 11);
            Eq(10, ticket.ExpectedUi, "expected UI");
            Eq(11, ticket.Physical, "physical");
            Check(ticket.Armed && !ticket.Consumed && !ticket.Committed, "initial ticket");

            var buffer = Buffer(1, 10, 0xA1);
            var decision = ExtensionIslandMapResultPlan.Complete(ref ticket, ValidCompletion(ticket));
            Eq(MapResultCompletionDecision.Commit, decision, "completion decision");
            Check(ticket.Consumed && !ticket.Committed, "Complete consumes but does not claim commit");
            Eq(MapResultCommitOutcome.Committed, Commit(buffer, ticket.ExpectedUi, ticket.Physical), "commit outcome");
            Eq(11, ReadValue(buffer), "value becomes physical");
            Eq((byte)1, buffer[0], "hasValue preserved");
            Eq((byte)0xA1, buffer[1], "padding byte 1 preserved");
            Eq((byte)0xA2, buffer[2], "padding byte 2 preserved");
            Eq((byte)0xA3, buffer[3], "padding byte 3 preserved");
            ticket.Committed = true;
            Eq(11, ReadValue(buffer), "committed value");
        });

        Test("B UI11 -> physical13：完成交接只写 4 字节", () =>
        {
            var ticket = ArmedTicket(11, 13);
            var buffer = Buffer(1, 11, 0x51);
            Eq(MapResultCompletionDecision.Commit, ExtensionIslandMapResultPlan.Complete(ref ticket, ValidCompletion(ticket)), "decision");
            Eq(MapResultCommitOutcome.Committed, Commit(buffer, ticket.ExpectedUi, ticket.Physical), "commit");
            Eq(13, ReadValue(buffer), "value becomes 13");
            Check(buffer[0] == 1 && buffer[1] == 0x51 && buffer[2] == 0x52 && buffer[3] == 0x53, "only int changed");
        });

        Test("A 重复 false 保持 11，绝不把 A physical11 再映射成 B physical13", () =>
        {
            var ticket = ArmedTicket(10, 11);
            var buffer = Buffer(1, 10, 0x33);
            Eq(MapResultCompletionDecision.Commit, ExtensionIslandMapResultPlan.Complete(ref ticket, ValidCompletion(ticket)), "first decision");
            Eq(MapResultCommitOutcome.Committed, Commit(buffer, ticket.ExpectedUi, ticket.Physical), "first commit");
            ticket.Committed = true;

            // 第二次 false 时 native 值已是首次提交的 11；即使调用者误按 B UI11 再试，也必须保持原值。
            var second = ValidCompletion(ticket, nativeValue: 11);
            Eq(MapResultCompletionDecision.LeaveUnchanged,
                ExtensionIslandMapResultPlan.Complete(ref ticket, second), "second decision");
            Eq(MapResultCommitOutcome.WrongValue, Commit(buffer, 10, 13), "no re-map A11->B13");
            Eq(11, ReadValue(buffer), "value remains 11");
        });

        Test("wrong expected UI 原样保留", () =>
        {
            var buffer = Buffer(1, 9, 0x77);
            Eq(MapResultCommitOutcome.WrongValue, Commit(buffer, 10, 11), "wrong value");
            Eq(9, ReadValue(buffer), "unchanged");
            Eq((byte)0x77, buffer[1], "padding unchanged");
        });

        Test("None 原样保留，不宣称可用取消", () =>
        {
            var buffer = Buffer(0, 10, 0x18);
            Eq(MapResultCommitOutcome.NoValue, Commit(buffer, 10, 11), "None not committed");
            Eq(0, buffer[0], "hasValue still false");
            Eq(10, ReadValue(buffer), "value bytes unchanged");

            var ticket = ArmedTicket(10, 11);
            Eq(MapResultCompletionDecision.LeaveUnchanged,
                ExtensionIslandMapResultPlan.Complete(ref ticket, ValidCompletion(ticket, hasValue: false)), "None decision");
            Check(ticket.Consumed && !ticket.Committed, "None consumes the completion only");
        });

        Test("非规范 hasValue 原样保留", () =>
        {
            var buffer = Buffer(2, 10, 0x44);
            Eq(MapResultCommitOutcome.InvalidHasValue, Commit(buffer, 10, 11), "non-canonical true");
            Eq((byte)2, buffer[0], "hasValue unchanged");
            Eq(10, ReadValue(buffer), "value unchanged");
        });

        Test("native/foreign/无票：非本 ticket 的 show 返回 Stale 且不消费原票", () =>
        {
            var ticket = ArmedTicket(10, 11);
            var foreign = ValidCompletion(ticket, matches: false);
            Eq(MapResultCompletionDecision.Stale,
                ExtensionIslandMapResultPlan.Complete(ref ticket, foreign), "foreign completion");
            Check(ticket.Armed && !ticket.Consumed && !ticket.Committed, "original ticket untouched");
            Eq(MapResultCompletionDecision.Commit,
                ExtensionIslandMapResultPlan.Complete(ref ticket, ValidCompletion(ticket)), "original still commits");
        });

        Test("menu/iterator/show/campaign/token/arm 失配全部拒绝", () =>
        {
            var cases = new (string Name, Action<MapResultArmFacts> Mutate)[]
            {
                ("layout", f => f.LayoutSupported = false),
                ("tuple", f => f.TupleExact = false),
                ("active-show", f => f.ActiveShowExists = false),
                ("show", f => f.ShowMatches = false),
                ("already-armed", f => f.AlreadyArmed = true),
                ("completed", f => f.AlreadyCompleted = true),
                ("campaign", f => f.CampaignMatches = false),
                ("token", f => f.TokenMatches = false),
                ("menu-state", f => f.MenuStateAllowed = false),
                ("active-map", f => f.ActiveMapMatches = false),
                ("hide-target", f => f.HideTargetValid = false)
            };
            foreach (var item in cases)
            {
                var facts = ValidArmFacts();
                item.Mutate(facts);
                var ticket = new MapResultTicket();
                Check(!ExtensionIslandMapResultPlan.TryArm(ref ticket, facts), item.Name + " must reject");
                Check(!ticket.Armed && !ticket.Consumed && !ticket.Committed, item.Name + " must not arm");
            }
            Check(ExtensionIslandMapResultPlan.IsArmMenuStateAllowed(2), "ShowingMapOnly allowed");
            Check(ExtensionIslandMapResultPlan.IsArmMenuStateAllowed(3), "ShowingWithTimeline allowed");
            Check(!ExtensionIslandMapResultPlan.IsArmMenuStateAllowed(0), "Closing rejected");
            Check(!ExtensionIslandMapResultPlan.IsArmMenuStateAllowed(1), "Closed rejected");
            Check(ExtensionIslandMapResultPlan.IsArmMenuStateAllowed(4), "Opening4 allowed (native-compatible >=2)");
            Check(!ExtensionIslandMapResultPlan.IsArmMenuStateAllowed(5), "unknown state rejected");
            Check(!ExtensionIslandMapResultPlan.IsArmMenuStateAllowed(-1), "negative unknown state rejected");
            Check(MountIslandSplitPolicy.TryGetPhysicalIndex(10, out int a) && a == 11, "A tuple 10->11");
            Check(MountIslandSplitPolicy.TryGetPhysicalIndex(11, out int b) && b == 13, "B tuple 11->13");
            Check(!MountIslandSplitPolicy.TryGetPhysicalIndex(10, out int wrongA) || wrongA != 13, "10->13 rejected");
            Check(!MountIslandSplitPolicy.TryGetPhysicalIndex(11, out int wrongB) || wrongB != 11, "11->11 rejected");
            Check(!MountIslandSplitPolicy.TryGetPhysicalIndex(9, out _), "native UI9 rejected");
        });

        Test("completion 失配：campaign/token 改变时原样保留", () =>
        {
            var campaign = ArmedTicket();
            var campaignFacts = ValidCompletion(campaign);
            campaignFacts.CampaignMatches = false;
            Eq(MapResultCompletionDecision.LeaveUnchanged,
                ExtensionIslandMapResultPlan.Complete(ref campaign, campaignFacts), "campaign mismatch");
            Check(campaign.Consumed && !campaign.Committed, "campaign mismatch consumes only");

            var token = ArmedTicket();
            var tokenFacts = ValidCompletion(token);
            tokenFacts.TokenMatches = false;
            Eq(MapResultCompletionDecision.LeaveUnchanged,
                ExtensionIslandMapResultPlan.Complete(ref token, tokenFacts), "token mismatch");
            Check(token.Consumed && !token.Committed, "token mismatch consumes only");
        });

        Test("新 show 使旧票失效，旧 iterator 迟到不影响新票", () =>
        {
            var oldTicket = ArmedTicket(10, 11);
            ExtensionIslandMapResultPlan.Revoke(ref oldTicket);
            Check(oldTicket.Revoked, "old ticket revoked by new show");
            Eq(MapResultCompletionDecision.LeaveUnchanged,
                ExtensionIslandMapResultPlan.Complete(ref oldTicket, ValidCompletion(oldTicket, nativeValue: 10)), "old late");

            var newTicket = ArmedTicket(11, 13);
            Eq(MapResultCompletionDecision.Stale,
                ExtensionIslandMapResultPlan.Complete(ref newTicket, ValidCompletion(newTicket, matches: false)), "old event not new ticket");
            Check(newTicket.Armed && !newTicket.Consumed, "new ticket still armed");
            Eq(MapResultCompletionDecision.Commit,
                ExtensionIslandMapResultPlan.Complete(ref newTicket, ValidCompletion(newTicket)), "new ticket commits");
        });

        Test("revoke/nativeClear：撤销后不提交", () =>
        {
            var ticket = ArmedTicket();
            ExtensionIslandMapResultPlan.Revoke(ref ticket);
            Check(ticket.Revoked && !ticket.Armed, "revoked");
            Eq(MapResultCompletionDecision.LeaveUnchanged,
                ExtensionIslandMapResultPlan.Complete(ref ticket, ValidCompletion(ticket)), "revoked completion");
            Check(!ticket.Committed, "revoked ticket never commits");
            ExtensionIslandMapResultPlan.Revoke(ref ticket);
            Check(ticket.Revoked, "second revoke idempotent");
        });

        Test("Hide 早退：Still Showing/Opening 才撤销，Closing/Closed 保留 pending", () =>
        {
            var ticket = ArmedTicket();
            Check(!ExtensionIslandMapResultPlan.ShouldRevokeAfterHide(0, ticket), "Closing no revoke");
            Check(!ExtensionIslandMapResultPlan.ShouldRevokeAfterHide(1, ticket), "Closed no revoke");
            Check(ExtensionIslandMapResultPlan.ShouldRevokeAfterHide(2, ticket), "ShowingMapOnly revoke");
            Check(ExtensionIslandMapResultPlan.ShouldRevokeAfterHide(3, ticket), "ShowingWithTimeline revoke");
            Check(ExtensionIslandMapResultPlan.ShouldRevokeAfterHide(4, ticket), "Opening revoke");
            ExtensionIslandMapResultPlan.Revoke(ref ticket);
            Check(!ExtensionIslandMapResultPlan.ShouldRevokeAfterHide(2, ticket), "revoked ticket no second revoke");
        });

        Test("Hide 异常（Abort）撤销未提交票", () =>
        {
            var ticket = ArmedTicket(10, 11);
            ExtensionIslandMapResultPlan.Revoke(ref ticket);   // Abort(menu) before native completion
            Check(ticket.Revoked && !ticket.Armed && !ticket.Committed, "abort revokes pending ticket");
            Eq(MapResultCompletionDecision.LeaveUnchanged,
                ExtensionIslandMapResultPlan.Complete(ref ticket, ValidCompletion(ticket)), "late completion after abort");
            Check(!ticket.Committed, "aborted ticket never commits");
        });

        Test("同步完成之后 Abort 不可假回滚", () =>
        {
            var ticket = ArmedTicket();
            Eq(MapResultCompletionDecision.Commit,
                ExtensionIslandMapResultPlan.Complete(ref ticket, ValidCompletion(ticket)), "sync completion");
            ticket.Committed = true;
            ExtensionIslandMapResultPlan.Revoke(ref ticket);
            Check(ticket.Committed && !ticket.Revoked, "committed stays committed");
            Check(!ExtensionIslandMapResultPlan.ShouldRevokeAfterHide(2, ticket), "committed ticket not revoked by hide state");

            var consumed = ArmedTicket();
            consumed.Consumed = true;
            ExtensionIslandMapResultPlan.Revoke(ref consumed);
            Check(!consumed.Revoked, "consumed ticket cannot be fake-rolled-back");
        });

        Test("completion 非 expected UI / 无票：原样保留", () =>
        {
            var ticket = ArmedTicket(10, 11);
            Eq(MapResultCompletionDecision.LeaveUnchanged,
                ExtensionIslandMapResultPlan.Complete(ref ticket, ValidCompletion(ticket, nativeValue: 9)), "wrong native UI");
            Check(ticket.Consumed && !ticket.Committed, "wrong native UI consumes only");

            var noTicket = new MapResultTicket();
            Eq(MapResultCompletionDecision.LeaveUnchanged,
                ExtensionIslandMapResultPlan.Complete(ref noTicket, ValidCompletion(noTicket)), "no ticket");
            Check(!noTicket.Armed && !noTicket.Consumed && !noTicket.Committed, "no ticket untouched");
        });

        Test("生产归一化：raw boxed 16/16/20 + Return header16 + Nullable class size24 -> payload 0/0/4", () =>
        {
            Check(ExtensionIslandMapResultPlan.TryNormalizeLayout(
                    16, 16, 20, 16, 24, 16, 24,
                    out int returnValue, out int hasValue, out int value, out string reason),
                "realistic normalization: " + reason);
            Eq(0, returnValue, "Return.value payload offset");
            Eq(0, hasValue, "Nullable.hasValue payload offset");
            Eq(4, value, "Nullable.value payload offset");

            Check(!ExtensionIslandMapResultPlan.TryNormalizeLayout(
                16, 16, 20, 16, 24, 8, 24, out _, out _, out _, out _), "fact header != instance-derived header");
            Check(!ExtensionIslandMapResultPlan.TryNormalizeLayout(
                16, 16, 20, 16, 20, 16, 24, out _, out _, out _, out _), "class size/header mismatch");
            Check(!ExtensionIslandMapResultPlan.TryNormalizeLayout(
                16, 16, 20, 16, 4, -4, 24, out _, out _, out _, out _), "instance size underflow");
            Check(!ExtensionIslandMapResultPlan.TryNormalizeLayout(
                16, 16, 20, 8, 24, 16, 24, out _, out _, out _, out _), "Return header too small/mismatch");
            Check(!ExtensionIslandMapResultPlan.TryNormalizeLayout(
                16, 16, 20, 16, 24, 16, 20, out _, out _, out _, out _), "Return boxed too small");
            Check(!ExtensionIslandMapResultPlan.TryNormalizeLayout(
                20, 16, 20, 16, 24, 16, 32, out _, out _, out _, out _), "Return raw outside payload0");
            Check(!ExtensionIslandMapResultPlan.TryNormalizeLayout(
                16, 20, 20, 16, 24, 16, 24, out _, out _, out _, out _), "hasValue raw outside payload0");
            Check(!ExtensionIslandMapResultPlan.TryNormalizeLayout(
                16, 16, 16, 16, 24, 16, 24, out _, out _, out _, out _), "value raw outside payload4");
        });

        Test("exact confirmation handle：旧/外来 handle 不影响新票", () =>
        {
            Check(ExtensionIslandMapResultPlan.CanUseConfirmationHandle(
                true, true, true, false, false, false), "current valid handle");
            Check(!ExtensionIslandMapResultPlan.CanUseConfirmationHandle(
                true, true, false, false, false, false), "foreign menu handle");
            Check(!ExtensionIslandMapResultPlan.CanUseConfirmationHandle(
                true, false, true, false, false, false), "missing show");
            Check(!ExtensionIslandMapResultPlan.CanUseConfirmationHandle(
                false, true, true, false, false, false), "stale handle");
            Check(!ExtensionIslandMapResultPlan.CanUseConfirmationHandle(
                true, true, true, true, false, false), "revoked handle");
            Check(!ExtensionIslandMapResultPlan.CanUseConfirmationHandle(
                true, true, true, false, true, false), "consumed handle");
            Check(!ExtensionIslandMapResultPlan.CanUseConfirmationHandle(
                true, true, true, false, false, true), "committed handle");

            var oldTicket = ArmedTicket(10, 11);
            var newTicket = ArmedTicket(11, 13);   // 模拟同 menu/新 show 已先 arm 新票
            ExtensionIslandMapResultPlan.Revoke(ref oldTicket);
            Check(!ExtensionIslandMapResultPlan.CanUseConfirmationHandle(
                true, true, true, oldTicket.Revoked, oldTicket.Consumed, oldTicket.Committed),
                "old handle after new arm is no-op");
            Check(newTicket.Armed && !newTicket.Revoked, "new ticket preserved");
            Eq(MapResultCompletionDecision.Commit,
                ExtensionIslandMapResultPlan.Complete(ref newTicket, ValidCompletion(newTicket)),
                "new ticket still commits");
        });

        Test("布局合法基线", () =>
        {
            Check(ExtensionIslandMapResultPlan.TryAcceptLayout(ValidLayoutFacts(), out MapResultLayout layout, out string reason),
                "baseline accepted: " + reason);
            Eq(40, layout.ResultFieldOffset, "result offset");
            Eq(0, layout.ReturnValueOffset, "return value offset");
            Eq(0, layout.HasValueOffset, "hasValue offset");
            Eq(4, layout.ValueOffset, "value offset");
        });

        Test("布局失败：size/offset/type/越界/对齐/引用/boxed 任一不符即拒绝", () =>
        {
            var mutations = new (string Name, Action<MapResultLayoutFacts> Mutate)[]
            {
                ("result-missing", f => f.ResultFieldFound = false),
                ("result-static", f => f.ResultFieldStatic = true),
                ("result-type", f => f.ResultFieldTypeExact = false),
                ("result-header", f => f.ResultFieldOffset = IntPtr.Size),
                ("result-outside", f => f.ResultFieldInsideObject = false),
                ("result-unaligned", f => f.ResultFieldAligned = false),
                ("result-misaligned-offset", f => { f.ResultFieldOffset = 42; f.ResultFieldAligned = true; }),
                ("return-size-4", f => f.ReturnSize = 4),
                ("return-size-16", f => f.ReturnSize = 16),
                ("return-not-valuetype", f => f.ReturnIsValueType = false),
                ("return-value-offset", f => { f.ReturnValueOffset = 20; f.BoxedReturnObjectSize = 32; }),
                ("return-value-type", f => f.ReturnValueTypeExact = false),
                ("return-ref", f => f.ReturnNoReferenceFields = false),
                ("return-extra", f => f.ReturnExactFieldSet = false),
                ("nullable-size-4", f => f.NullableSize = 4),
                ("nullable-size-16", f => f.NullableSize = 16),
                ("nullable-not-valuetype", f => f.NullableIsValueType = false),
                ("hasvalue-offset", f => f.NullableHasValueOffset = 20),
                ("value-offset-0", f => f.NullableValueOffset = 16),
                ("value-offset-out-of-range", f => f.NullableValueOffset = 24),
                ("hasvalue-type", f => f.NullableHasValueTypeExact = false),
                ("value-type", f => f.NullableValueTypeExact = false),
                ("hasvalue-size", f => f.NullableHasValueSize = 2),
                ("value-size", f => f.NullableValueSize = 8),
                ("nullable-ref", f => f.NullableNoReferenceFields = false),
                ("nullable-extra", f => f.NullableExactFieldSet = false),
                ("nullable-instance-underflow", f => { f.NullableClassInstanceSize = 4; f.NullableClassHeaderDisplacement = -4; }),
                ("nullable-instance-mismatch", f => { f.NullableClassInstanceSize = 20; f.NullableClassHeaderDisplacement = 16; }),
                ("header-mismatch", f => f.NullableClassHeaderDisplacement = 8),
                ("nullable-header-zero", f => f.NullableClassHeaderDisplacement = 0),
                ("nullable-header-unaligned", f => f.NullableClassHeaderDisplacement = IntPtr.Size + 4),
                ("boxed-displacement-zero", f => f.BoxedReturnHeaderDisplacement = 0),
                ("boxed-displacement-unaligned", f => f.BoxedReturnHeaderDisplacement = IntPtr.Size + 1),
                ("boxed-displacement-too-small", f => f.BoxedReturnHeaderDisplacement = IntPtr.Size),
                ("boxed-size-small", f => f.BoxedReturnObjectSize = 20),
                ("boxed-inline", f => f.BoxedReturnInlineMatches = false),
                ("boxed-field-read", f => f.BoxedReturnFieldReadMatches = false)
            };
            foreach (var item in mutations)
            {
                var facts = ValidLayoutFacts();
                item.Mutate(facts);
                Check(!ExtensionIslandMapResultPlan.TryAcceptLayout(facts, out _, out string reason),
                    item.Name + " must be rejected");
                Check(!string.IsNullOrEmpty(reason), item.Name + " must expose a reason");
            }
        });

        Test("等值 expectedUI==physical 仍返回 Committed（不得误判为未提交）", () =>
        {
            var buffer = Buffer(1, 11, 0x99);
            Eq(MapResultCommitOutcome.Committed, Commit(buffer, 11, 11), "equal write is a commit");
            Eq(11, ReadValue(buffer), "value unchanged but committed");
            Eq((byte)0x99, buffer[1], "padding unchanged");
        });

        Test("无条件写边界：null 指针/错误 layout/非 4 对齐全部拒绝", () =>
        {
            Eq(MapResultCommitOutcome.Unsupported,
                ExtensionIslandMapResultPlan.TryCommit(null, ValidLayout(), 10, 11), "null pointer");
            var wrongLayout = new MapResultLayout(40, 1, 0, 4);
            var buffer = Buffer(1, 10, 0);
            fixed (byte* pointer = buffer)
                Eq(MapResultCommitOutcome.Unsupported,
                    ExtensionIslandMapResultPlan.TryCommit(pointer, wrongLayout, 10, 11), "wrong layout");
            // pointer+4 自然 4 对齐；改成偏移 5 的 layout / 非 4 对齐指针都被硬门拒绝
            var badLayout = new MapResultLayout(40, 0, 0, 5);
            fixed (byte* pointer = buffer)
                Eq(MapResultCommitOutcome.Unsupported,
                    ExtensionIslandMapResultPlan.TryCommit(pointer, badLayout, 10, 11), "bad value offset");
            var unalignedBuffer = new byte[9];
            fixed (byte* pointer = unalignedBuffer)
                Eq(MapResultCommitOutcome.Unsupported,
                    ExtensionIslandMapResultPlan.TryCommit(pointer + 1, ValidLayout(), 10, 11), "unaligned pointer");
        });

        Test("读取前元数据硬门与读取后字节校验分阶段，不伪造装箱观测", () =>
        {
            var f = ValidLayoutFacts();
            f.BoxedReturnInlineMatches = false;
            f.BoxedReturnFieldReadMatches = false;
            f.BoxedReturnHeaderDisplacement = 0;
            f.BoxedReturnObjectSize = 0;
            Check(ExtensionIslandMapResultPlan.TryAcceptMetadataShape(f, out _), "shape is checkable before boxing");
            Check(!ExtensionIslandMapResultPlan.TryAcceptLayout(f, out _, out _), "full layout still needs real box evidence");
            f.ResultFieldTypeExact = false;
            Check(!ExtensionIslandMapResultPlan.TryAcceptMetadataShape(f, out _), "wrong result type rejected before getter");
            f = ValidLayoutFacts(); f.ResultFieldInsideObject = false;
            Check(!ExtensionIslandMapResultPlan.TryAcceptMetadataShape(f, out _), "result range rejected before getter");
            f = ValidLayoutFacts(); f.ReturnIsValueType = false;
            Check(!ExtensionIslandMapResultPlan.TryAcceptMetadataShape(f, out _), "reference Return rejected before unbox");
            f = ValidLayoutFacts(); f.NullableValueSize = 8;
            Check(!ExtensionIslandMapResultPlan.TryAcceptMetadataShape(f, out _), "wrong primitive size rejected before payload read");
        });

        Console.WriteLine($"RESULT passed={Passed} failed={Failed} assertions={Assertions}");
        return Failed == 0 ? 0 : 1;
    }
}
