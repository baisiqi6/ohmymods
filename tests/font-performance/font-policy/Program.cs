using System;

// source-linked 纯测试：直接编译 ../StaticBestFitPolicy.cs（production 策略本体，不做替身复写）。
//
// Unity 侧由 FakeText 驱动，驱动顺序与 PatchFont_StaticBestFit 适配层一一对应：
//   SetFontPrefix  == set_font prefix（登记 wanted；static incoming 先规范化 effective=false）
//   SetFontFinish  == set_font postfix/finalizer 结算（读回实际 font 能力）
//   ExternalSet    == set_resizeTextForBestFit prefix（先一次性消费内部许可；否则记录外部请求）
//   InternalWrite  == 适配层 WriteEffective（EnterInternalWrite + 直接 setter 调用）
//   Enable         == OnEnable prefix
// 本文件只做读取/写入/顺序的机械转发与断言，不复制任何决策逻辑；原 setter 的
// “等值 early-return 不写字”语义、Unity fake-null/未知字体（Kind=0/null）语义按报告谨慎建模。
// 含独立 review R2 的四个 FAIL 回归：dynamic true 更新、open-ticket false→true、
// unknown-liveness 保记录、nested same-life 外部 false。
namespace KingdomEnhancedMod;

internal static class Program
{
    private static int _fail;
    private static int _check;
    private static bool _coverage;

    private static void Check(bool ok, string name)
    {
        _check++;
        if (!ok)
        {
            _fail++;
            Console.WriteLine("FAIL " + name);
        }
        else
        {
            Console.WriteLine("ok   " + name);
        }
    }

    private sealed class FakeText
    {
        internal long Pointer;
        internal int Id;
        internal int Kind; // 0 = font null / unknown, 1 = static, 2 = dynamic
        internal bool BestFit;
        internal bool Alive = true;
        internal int Writes; // 原 setter 实际写入字段的次数（含内部写路径）

        internal FakeText(long pointer, int id, int kind, bool bestFit)
        {
            Pointer = pointer;
            Id = id;
            Kind = kind;
            BestFit = bestFit;
        }

        internal StaticBestFitPolicy.Life Life => new StaticBestFitPolicy.Life(Pointer, Id);
    }

    private static readonly Func<object, long, int, StaticBestFitPolicy.Liveness> Alive = (owner, pointer, id) =>
        owner is FakeText text && text.Alive && text.Pointer == pointer && text.Id == id
            ? StaticBestFitPolicy.Liveness.Alive
            : StaticBestFitPolicy.Liveness.Dead;

    private static StaticBestFitPolicy.Capability Cap(int kind)
    {
        if (kind == 1) return StaticBestFitPolicy.Capability.Static;
        if (kind == 2) return StaticBestFitPolicy.Capability.Dynamic;
        return StaticBestFitPolicy.Capability.Unknown;
    }

    private static void Reset()
    {
        StaticBestFitPolicy.ResetAll();
        _coverage = false;
    }

    // ---- 适配层等价驱动（只转发） ----

    /// <summary>适配层 WriteEffective 形状：exact life 一次性许可 + try/finally。</summary>
    private static void InternalWrite(FakeText text, bool value)
    {
        using (StaticBestFitPolicy.EnterInternalWrite(text.Life))
        {
            ExternalSet(text, value); // 本次直接 setter：前缀消费自身许可
        }
    }

    private static StaticBestFitPolicy.FontTicket SetFontPrefix(FakeText text, int incomingKind)
    {
        var ticket = StaticBestFitPolicy.BeginFontWrite(
            text.Life, text, text.BestFit, Cap(incomingKind), Alive, out bool coverage);
        _coverage |= coverage;
        if (ticket != null && ticket.Normalized)
            ApplyDecision(text, StaticBestFitPolicy.WriteAction.WriteFalse, forced: true);
        return ticket;
    }

    private static void SetFontFinish(FakeText text, StaticBestFitPolicy.FontTicket ticket)
    {
        if (ticket == null) return;
        var action = StaticBestFitPolicy.FinishFontWrite(ticket, Cap(text.Kind), text.BestFit, Alive, out bool coverage);
        _coverage |= coverage;
        if (action != StaticBestFitPolicy.WriteAction.None)
            ApplyDecision(text, action, forced: false);
    }

    /// <summary>0=无异常；1=字段写之前抛；2=字段写/脏标记之后抛。postfix 只在成功路径跑，finalizer 恒跑（幂等）。</summary>
    private static void SetFont(
        FakeText text, int incomingKind, Action midBeforeField = null, Action midAfterField = null, int throwStage = 0)
    {
        var ticket = SetFontPrefix(text, incomingKind);
        try
        {
            if (midBeforeField != null) midBeforeField();
            if (throwStage == 1) throw new InvalidOperationException("font-before-field");
            text.Kind = incomingKind; // 原字体字段写
            if (midAfterField != null) midAfterField();
            if (throwStage == 2) throw new InvalidOperationException("font-after-field");
            SetFontFinish(text, ticket); // postfix
        }
        finally
        {
            SetFontFinish(text, ticket); // finalizer（Closed 幂等）
        }
    }

    /// <summary>set_resizeTextForBestFit prefix + 原 setter（等值 early-return）：先一次性消费内部许可。</summary>
    private static void ExternalSet(FakeText text, bool requested)
    {
        bool value = requested;
        if (!StaticBestFitPolicy.TryConsumeInternalWrite(text.Life))
        {
            bool rewrite = StaticBestFitPolicy.NoteExternalBestFitSet(
                text.Life, text, value, Cap(text.Kind), Alive, out bool coverage);
            _coverage |= coverage;
            if (rewrite) value = false;
        }
        if (text.BestFit != value)
        {
            text.BestFit = value;
            text.Writes++;
        }
    }

    private static void Enable(FakeText text)
    {
        var action = StaticBestFitPolicy.NoteEnable(text.Life, text, Cap(text.Kind), text.BestFit, Alive, out bool coverage);
        _coverage |= coverage;
        if (action == StaticBestFitPolicy.WriteAction.None) return;
        ApplyDecision(text, action, forced: false);
    }

    /// <summary>适配层 WriteEffective 形状：readonly 读 → ApplyDecisionGate（最新 wanted/当前能力）→ 无日志直接写。</summary>
    private static bool ApplyDecision(FakeText text, StaticBestFitPolicy.WriteAction decided, bool forced)
    {
        var action = StaticBestFitPolicy.ApplyDecisionGate(text.Life, decided, forced, Cap(text.Kind), text.BestFit);
        if (action == StaticBestFitPolicy.WriteAction.None) return false;
        InternalWrite(text, action == StaticBestFitPolicy.WriteAction.WriteTrue);
        return true;
    }

    private static bool Wanted(FakeText text) => StaticBestFitPolicy.TryGetWanted(text.Life, out bool wanted) && wanted;

    private static bool HasRecord(FakeText text, out bool wanted) => StaticBestFitPolicy.TryGetWanted(text.Life, out wanted);

    private static int Main()
    {
        T1_SerializedStaticTrueOnEnable();
        T2_StaticTrueRequestNormalized();
        T3_ExternalFalseClearsEvenNoChange();
        T4_DynamicRestore();
        T5_RoundTrip();
        T6_PermitOneShotAndNestedSameLife();
        T7_NestedOtherTextNotShielded();
        T8_ThrowBeforeFieldRestoresTrue();
        T9_ThrowAfterStaticFieldKeepsFalse();
        T10_UnknownNotGuessed();
        T11_IdentityReuse();
        T12_CapacityKeepsLiveIntent();
        T13_ExternalFalseMidTransaction();
        T14_NestedSameTextTransactions();
        T15_FalseDuringDynamicAssignment();
        T16_EnableExistingLeaseKeepsWanted();
        T17_EnableRestoresWhenFontBecameDynamic();
        T18_PruneSkipsOpenTransaction();
        T19_PermitScopeExactSeq();
        T20_ExternalTrueAfterBeginFalse();
        T21_OpenTicketFalseThenTrue();
        T22_LivenessUnknownKeepsRecord();
        T23_LateFalseCancelsDecidedTrue();
        T24_StaleFalseReconciled();
        T25_GateUnknownAndRecordGone();
        T26_ReentryAfterWriteIsHonored();

        Console.WriteLine(_fail == 0 ? "ALL PASS " + _check + " checks" : _fail + "/" + _check + " FAILED");
        return _fail == 0 ? 0 : 1;
    }

    // 序列化 static + BestFit=true 首次 OnEnable：native cache invalidation 之前规范化。
    private static void T1_SerializedStaticTrueOnEnable()
    {
        Reset();
        var t = new FakeText(0x1000, 11, 1, true);
        Enable(t);
        Check(!t.BestFit && Wanted(t), "T1 enable(static,bestfit=true) -> effective=false, wanted=true");
        Check(t.Writes == 1, "T1 exactly one normalization write");
    }

    // static 下的外部 true 请求：改写本次参数、登记 wanted、不写字段。
    private static void T2_StaticTrueRequestNormalized()
    {
        Reset();
        var t = new FakeText(0x1100, 12, 1, false);
        ExternalSet(t, true);
        Check(!t.BestFit && Wanted(t) && t.Writes == 0, "T2 external true on static: request rewritten, wanted recorded");
    }

    // 静态下外部 false 即使 effective 已 false、原 setter no-change early-return 也清 wanted。
    private static void T3_ExternalFalseClearsEvenNoChange()
    {
        Reset();
        var t = new FakeText(0x1200, 13, 1, false);
        ExternalSet(t, true);
        ExternalSet(t, false);
        Check(!HasRecord(t, out _) && StaticBestFitPolicy.RecordCount == 0, "T3 external false clears wanted on no-change path");
    }

    // wanted 保留到 dynamic 字体回来：恢复 effective=true，且 postfix/finalizer 双结算只写一次。
    private static void T4_DynamicRestore()
    {
        Reset();
        var t = new FakeText(0x1300, 14, 1, true);
        Enable(t);
        SetFont(t, 2);
        Check(t.BestFit && Wanted(t), "T4 static->dynamic restores effective=true from wanted");
        Check(t.Writes == 2, "T4 restore writes once (finish idempotent)");
    }

    // dynamic→static→dynamic 往返：每次能力切换都保持不变量。
    private static void T5_RoundTrip()
    {
        Reset();
        var t = new FakeText(0x1400, 15, 2, true);
        SetFont(t, 1);
        Check(!t.BestFit && Wanted(t), "T5 dynamic->static normalizes effective=false");
        SetFont(t, 2);
        Check(t.BestFit, "T5 static->dynamic restores effective=true");
    }

    // R2：内部许可一次性消费——own 写被吞；同 Text 在 dirty 回调中的第二次 setter 层按外部请求记录。
    private static void T6_PermitOneShotAndNestedSameLife()
    {
        Reset();
        var probe = new FakeText(0x1500, 16, 1, false);
        using (StaticBestFitPolicy.EnterInternalWrite(probe.Life))
        {
            Check(StaticBestFitPolicy.TryConsumeInternalWrite(probe.Life), "T6 first consume true (own setter entry)");
            Check(!StaticBestFitPolicy.TryConsumeInternalWrite(probe.Life), "T6 second consume false (one-shot)");
        }
        Check(!StaticBestFitPolicy.TryConsumeInternalWrite(probe.Life), "T6 permit released with scope");

        var t = new FakeText(0x1510, 17, 1, true);
        Enable(t); // wanted=true, effective=false
        bool ownPreserved;
        using (StaticBestFitPolicy.EnterInternalWrite(t.Life))
        {
            ExternalSet(t, false); // 本次 own 直接写：前缀消费许可 → 不记录 → wanted 保持
            ownPreserved = Wanted(t);
            ExternalSet(t, false); // 同 Text 第二次 layer（dirty 回调形状）：无许可 → 外部请求 → 清 wanted
        }
        Check(ownPreserved, "T6 own first setter entry keeps wanted=true");
        Check(!Wanted(t), "T6 nested same-life external false clears wanted");
        SetFont(t, 2);
        Check(!t.BestFit, "T6 later dynamic font cannot resurrect the cleared true");
    }

    // 内部许可只对 exact life：另一个 Text 的外部 false 不被吞。
    private static void T7_NestedOtherTextNotShielded()
    {
        Reset();
        var a = new FakeText(0x1600, 18, 1, true);
        var b = new FakeText(0x1700, 19, 1, false);
        Enable(a);
        ExternalSet(b, true);
        using (StaticBestFitPolicy.EnterInternalWrite(a.Life))
        {
            ExternalSet(b, false);
        }
        Check(Wanted(a) && !HasRecord(b, out _), "T7 permit on A does not shield external false on B");
    }

    // 字体赋值在字段写之前失败：prefix 规范化掉的 true 必须按实际（仍旧 dynamic）font 恢复。
    private static void T8_ThrowBeforeFieldRestoresTrue()
    {
        Reset();
        var t = new FakeText(0x1800, 20, 2, true);
        bool threw = false;
        try { SetFont(t, 1, null, null, 1); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw && t.BestFit && Wanted(t), "T8 failed-before-field restores true over remaining dynamic font");
    }

    // 字体赋值写进 static 字段后失败：保持 false 且保 wanted（不得复活，也不得丢意图）。
    private static void T9_ThrowAfterStaticFieldKeepsFalse()
    {
        Reset();
        var t = new FakeText(0x1900, 21, 1, true);
        bool threw = false;
        try { SetFont(t, 1, null, null, 2); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw && !t.BestFit && Wanted(t), "T9 failed-after-static-field keeps effective=false and wanted=true");
    }

    // null/未知字体：不猜 false、不替换字体、不写。
    private static void T10_UnknownNotGuessed()
    {
        Reset();
        var t = new FakeText(0x1A00, 22, 1, true);
        Enable(t);
        SetFont(t, 0); // 字体被写成 null
        Check(!t.BestFit && Wanted(t), "T10 null actual font: no write, wanted kept");

        var u = new FakeText(0x1A10, 23, 0, true); // null font + bestfit=true
        Enable(u);
        Check(u.BestFit && !HasRecord(u, out _), "T10b enable with unknown font does not normalize/guess");
    }

    // 同 id 不同 pointer / 同 pointer 新 id 都不继承记录；确证 destroyed 才被有界 prune 掉。
    private static void T11_IdentityReuse()
    {
        Reset();
        var t = new FakeText(0x1B00, 24, 1, true);
        Enable(t);
        Check(HasRecord(t, out bool w) && w, "T11 record created for normalized text");
        Check(!StaticBestFitPolicy.TryGetWanted(new StaticBestFitPolicy.Life(0x1B01, 24), out _),
            "T11 same id different pointer does not inherit");
        Check(!StaticBestFitPolicy.TryGetWanted(new StaticBestFitPolicy.Life(0x1B00, 25), out _),
            "T11 reused pointer with new id does not inherit");
        t.Alive = false;
        int removed = StaticBestFitPolicy.Prune(Alive, 8);
        Check(removed == 1 && StaticBestFitPolicy.RecordCount == 0, "T11 prune removes proven-dead record");
    }

    // 容量满：不 evict live wanted，不“先置 false 却没记 wanted”；一次 coverage 提示。
    private static void T12_CapacityKeepsLiveIntent()
    {
        Reset();
        FakeText first = null;
        for (int i = 0; i < StaticBestFitPolicy.Capacity; i++)
        {
            var fill = new FakeText(0x100000 + i, 1000 + i, 1, true);
            Enable(fill);
            if (i == 0) first = fill;
        }
        Check(StaticBestFitPolicy.RecordCount == StaticBestFitPolicy.Capacity, "T12 filled to capacity");
        var over = new FakeText(0x500000, 9000, 1, false);
        ExternalSet(over, true);
        Check(over.BestFit && !HasRecord(over, out _), "T12 capacity full keeps original request/state");
        Check(StaticBestFitPolicy.RecordCount == StaticBestFitPolicy.Capacity && Wanted(first),
            "T12 live wanted never evicted");
        Check(_coverage && StaticBestFitPolicy.TryConsumeCoverageNotice(out int blocked) && blocked == 1,
            "T12 coverage notice returned once");
        Check(!StaticBestFitPolicy.TryConsumeCoverageNotice(out _), "T12 coverage notice only once");
        ExternalSet(over, false);
        Check(!over.BestFit, "T12 external false honored at capacity");
        first.Alive = false;
        Check(StaticBestFitPolicy.Prune(Alive, StaticBestFitPolicy.Capacity) == 1, "T12 dead entry pruned on bounded sweep");
        ExternalSet(over, true);
        Check(!over.BestFit && Wanted(over), "T12 after space freed the static-true request is normalized again");
    }

    // 外部 false 发生在原字体赋值期间：旧 capture 不得把 true 写回。
    private static void T13_ExternalFalseMidTransaction()
    {
        Reset();
        var t = new FakeText(0x2000, 30, 2, true);
        SetFont(t, 1, () => ExternalSet(t, false));
        Check(!t.BestFit && !HasRecord(t, out _), "T13 external false mid-transaction wins; record released");
    }

    // 同 Text 嵌套事务：内层 dynamic 恢复不被外层旧 capture 覆盖；外层 static 收尾仍合法。
    private static void T14_NestedSameTextTransactions()
    {
        Reset();
        var t = new FakeText(0x2200, 32, 1, true);
        SetFont(t, 1, () => SetFont(t, 2));
        Check(t.Kind == 1 && !t.BestFit && Wanted(t), "T14a nested dynamic restore then outer static stays legal");

        Reset();
        var u = new FakeText(0x2210, 33, 1, true);
        SetFont(u, 1, () =>
        {
            SetFont(u, 2);
            ExternalSet(u, false);
        });
        Check(u.Kind == 1 && !u.BestFit && !HasRecord(u, out _),
            "T14b external false after nested restore is not overwritten by outer capture");
    }

    // actual 在结算时是 dynamic，但期间到达了外部 false：不得恢复 true。
    private static void T15_FalseDuringDynamicAssignment()
    {
        Reset();
        var t = new FakeText(0x2100, 31, 1, true);
        Enable(t); // static+true -> wanted=true, effective=false
        SetFont(t, 2, () => ExternalSet(t, false));
        Check(!t.BestFit && !HasRecord(t, out _), "T15 mid-transaction false prevents stale true restore; record released");
    }

    // 已有 lease 时 OnEnable 只用已记 wanted，不因看到 effective=false 覆盖成 wanted=false。
    private static void T16_EnableExistingLeaseKeepsWanted()
    {
        Reset();
        var t = new FakeText(0x2300, 34, 1, true);
        Enable(t);
        Enable(t);
        Check(Wanted(t) && !t.BestFit, "T16 repeated enable keeps lease wanted=true without spurious restore");
    }

    // disabled/pooled 期间字体变成 dynamic（未经过 set_font）：Enable 按 wanted 恢复。
    private static void T17_EnableRestoresWhenFontBecameDynamic()
    {
        Reset();
        var t = new FakeText(0x2400, 35, 1, true);
        Enable(t);
        t.Kind = 2; // serialized/外部路径的字体变化
        Enable(t);
        Check(t.BestFit && Wanted(t), "T17 enable restores true when font became dynamic while disabled");
    }

    // 有未收尾事务的记录不被 prune 掉；事务关闭后才被清理。
    private static void T18_PruneSkipsOpenTransaction()
    {
        Reset();
        var t = new FakeText(0x2500, 36, 2, true);
        var ticket = SetFontPrefix(t, 1);
        t.Alive = false;
        Check(StaticBestFitPolicy.Prune(Alive, 8) == 0 && StaticBestFitPolicy.RecordCount == 1,
            "T18 prune skips record with open transaction");
        SetFontFinish(t, ticket);
        Check(StaticBestFitPolicy.Prune(Alive, 8) == 1 && StaticBestFitPolicy.RecordCount == 0,
            "T18 prune removes after transaction closed");
    }

    // 许可 scope 按唯一 seq 收尾：复制/重复 Dispose 不误关；异常退出不泄漏。
    private static void T19_PermitScopeExactSeq()
    {
        Reset();
        var t = new FakeText(0x2600, 37, 1, true);
        var a = StaticBestFitPolicy.EnterInternalWrite(t.Life);
        var b = StaticBestFitPolicy.EnterInternalWrite(t.Life);
        var copyA = a;
        copyA.Dispose(); // 关 seq_a
        a.Dispose();     // 重复 Dispose：无操作，不得误关 seq_b
        Check(StaticBestFitPolicy.TryConsumeInternalWrite(t.Life)
            && !StaticBestFitPolicy.TryConsumeInternalWrite(t.Life),
            "T19 duplicate dispose leaves later permit intact and consumable once");
        b.Dispose();
        Check(!StaticBestFitPolicy.TryConsumeInternalWrite(t.Life), "T19 all permits released");

        try
        {
            using (StaticBestFitPolicy.EnterInternalWrite(t.Life))
            {
                throw new InvalidOperationException("boom");
            }
        }
        catch (InvalidOperationException)
        {
        }
        Check(!StaticBestFitPolicy.TryConsumeInternalWrite(t.Life), "T19 scope unwinds on exception (permit removed)");
    }

    // R2 回归 1：无 record、begin(effective=false)，期间到达较新外部 dynamic true；Finish 不得写回 false。
    private static void T20_ExternalTrueAfterBeginFalse()
    {
        Reset();
        var t = new FakeText(0x2700, 40, 2, false); // dynamic + bestfit=false，无 record
        SetFont(t, 2, () => ExternalSet(t, true));
        Check(t.BestFit && Wanted(t), "T20 newer external true is not overwritten by the stale ticket");
    }

    // R2 回归 2：open-ticket 期间 false→true；最新 true 不能被旧事务改回 false，record 不得被删。
    private static void T21_OpenTicketFalseThenTrue()
    {
        Reset();
        var t = new FakeText(0x2710, 41, 1, true);
        Enable(t); // static+true -> wanted=true, effective=false
        SetFont(t, 2, () =>
        {
            ExternalSet(t, false);
            ExternalSet(t, true);
        });
        Check(t.BestFit && Wanted(t), "T21 later external true wins over mid-transaction false");
    }

    // R2 回归 3：liveness 读取异常/Unknown ≠ destroyed，保 wanted 记录；只有确证 Dead 才清。
    private static void T22_LivenessUnknownKeepsRecord()
    {
        Reset();
        var t = new FakeText(0x2800, 42, 1, true);
        Enable(t);
        Func<object, long, int, StaticBestFitPolicy.Liveness> throwing =
            (owner, pointer, id) => throw new InvalidOperationException("read-fault");
        Check(StaticBestFitPolicy.Prune(throwing, 8) == 0 && StaticBestFitPolicy.RecordCount == 1,
            "T22 validator read-fault (Unknown) keeps live wanted record");
        Func<object, long, int, StaticBestFitPolicy.Liveness> unknown =
            (owner, pointer, id) => StaticBestFitPolicy.Liveness.Unknown;
        Check(StaticBestFitPolicy.Prune(unknown, 8) == 0 && Wanted(t), "T22 explicit Unknown keeps record");
        Check(StaticBestFitPolicy.Prune(Alive, 8) == 0 && Wanted(t), "T22 Alive keeps record");
        t.Alive = false;
        Check(StaticBestFitPolicy.Prune(Alive, 8) == 1 && StaticBestFitPolicy.RecordCount == 0,
            "T22 proven dead is removed");
    }

    // R3 回归：已决 WriteTrue 之后、apply 之前到达外部 false → 末门取消 stale true（保当前 effective）。
    private static void T23_LateFalseCancelsDecidedTrue()
    {
        Reset();
        var t = new FakeText(0x2900, 43, 1, true);
        Enable(t); // wanted=true, effective=false
        var ticket = SetFontPrefix(t, 2);
        t.Kind = 2; // 原字体赋值完成 → dynamic
        var decided = StaticBestFitPolicy.FinishFontWrite(ticket, Cap(t.Kind), t.BestFit, Alive, out _);
        Check(decided == StaticBestFitPolicy.WriteAction.WriteTrue, "T23 decision is WriteTrue (dynamic restore)");
        ExternalSet(t, false); // 决策之后、apply 之前：日志订阅者 reentry 形状的新外部 false
        bool applied = ApplyDecision(t, decided, forced: false);
        Check(!applied && !t.BestFit, "T23 gate cancels stale true after late external false");
    }

    // R3 回归：已决 WriteFalse（serialized static 组合）之后字体变 dynamic 且最新 wanted=true → 末门改判 true；
    // 而 set_font prefix 的 forced false（即将赋 static）必须保持 false。
    private static void T24_StaleFalseReconciled()
    {
        Reset();
        var t = new FakeText(0x2910, 44, 1, true);
        var decided = StaticBestFitPolicy.NoteEnable(t.Life, t, Cap(1), true, Alive, out _);
        Check(decided == StaticBestFitPolicy.WriteAction.WriteFalse, "T24 decision is WriteFalse for serialized static combo");
        ApplyDecision(t, decided, forced: false); // 规范化执行（bit=false）
        Check(!t.BestFit && Wanted(t), "T24 serialized combo normalized first");
        t.Kind = 2; // 决策后字体变成 dynamic（serialized/外部路径，不经过 set_font）
        bool applied = ApplyDecision(t, decided, forced: false); // 旧决策在末门按最新 wanted && dynamic 复核
        Check(applied && t.BestFit, "T24 gate reconciles the stale false to latest wanted && dynamic");

        Reset();
        var u = new FakeText(0x2920, 45, 1, true);
        Enable(u); // wanted=true, effective=false
        bool forcedApplied = ApplyDecision(u, StaticBestFitPolicy.WriteAction.WriteFalse, forced: true);
        Check(forcedApplied && !u.BestFit, "T24 forced false (incoming static) stays false even while old font dynamic");
    }

    // R3 回归：末门不猜 Unknown、记录消失即取消、已一致不写。
    private static void T25_GateUnknownAndRecordGone()
    {
        Reset();
        var t = new FakeText(0x2A00, 46, 2, false);
        Enable(t); // 无 record 时会创建；先用 record 场景
        Check(StaticBestFitPolicy.ApplyDecisionGate(t.Life, StaticBestFitPolicy.WriteAction.WriteTrue, false,
            StaticBestFitPolicy.Capability.Unknown, false) == StaticBestFitPolicy.WriteAction.None,
            "T25 Unknown capability cancels every action (no guessing)");

        Reset();
        var u = new FakeText(0x2A10, 47, 1, true);
        Enable(u);               // record wanted=true, effective=false
        ExternalSet(u, false);   // 外部 false：清 intent 并释放 record（无事务）
        Check(!HasRecord(u, out _), "T25 record released by external false");
        Check(StaticBestFitPolicy.ApplyDecisionGate(u.Life, StaticBestFitPolicy.WriteAction.WriteTrue, false,
            Cap(u.Kind), u.BestFit) == StaticBestFitPolicy.WriteAction.None,
            "T25 missing latest record cancels the stale action");

        Reset();
        var v = new FakeText(0x2A20, 48, 1, true);
        Enable(v); // wanted=true, effective=false
        Check(StaticBestFitPolicy.ApplyDecisionGate(v.Life, StaticBestFitPolicy.WriteAction.WriteFalse, false,
            Cap(v.Kind), v.BestFit) == StaticBestFitPolicy.WriteAction.None,
            "T25 already-consistent state needs no write");
    }

    // R3 回归：写之后的 reentry 请求照常生效，且不存在任何残留旧动作可以在之后把它改回去。
    private static void T26_ReentryAfterWriteIsHonored()
    {
        Reset();
        var t = new FakeText(0x2B00, 49, 2, false);
        SetFont(t, 2, () => ExternalSet(t, true));
        Check(t.BestFit && Wanted(t), "T26 mid-transaction request applied");
        ExternalSet(t, false); // 写完成后的日志回调 reentry：新外部 false
        Check(!t.BestFit && !Wanted(t), "T26 post-write request is honored");
        Check(StaticBestFitPolicy.ApplyDecisionGate(t.Life, StaticBestFitPolicy.WriteAction.WriteTrue, false,
            Cap(t.Kind), t.BestFit) == StaticBestFitPolicy.WriteAction.None,
            "T26 no stale action remains to overwrite the correction");
    }
}
