using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

// production-linked 纯测试：
//  - proposal/il2cpp/Patch_MountIslandSplitPolicy.cs（unused pure policy，逐字 Compile link）
//  - proposal/il2cpp/ExtensionIslandMapPlan.cs（本 slice 的 per-land tuple 提案）
//  - proposal/il2cpp/Patch_MountIslandMapLayout.cs（新纯几何/分页 helper）
//  - WT il2cpp/CrossWorldMountData.cs（只读生产文件；分页 PageCount 交叉核对）
//  - WT il2cpp/ExtensionIslandPlan.cs（CrossWorldMountData 的纯依赖，只读链接）
// 无 Unity/Il2Cpp/Harmony；不注册、不落盘、不启动游戏。
internal static class Program
{
    private static int _checks;
    private static int _failed;

    private static void Check(bool condition, string what)
    {
        _checks++;
        if (condition) return;
        _failed++;
        Console.WriteLine("FAIL: " + what);
    }

    private static readonly string[] GroupA =
    {
        "norselands.gullinbursti", "norselands.sleipnir", "norselands.reindeer", "norselands.catcart",
        "norselands.kelpie", "norselands.hrimfaxe", "norselands.wolf", "bamboo.kirin",
    };

    private static readonly string[] GroupA2 =
    {
    };

    private static readonly string[] GroupB =
    {
        "woodlands.beetle", "woodlands.golem", "woodlands.gamigin", "woodlands.mansion",
        "swamp.eggsteed", "deadlands.grave", "santahouse.reindeer", "anniversary.rainbowpony",
    };

    private static void Main()
    {
        TupleAndPolicyComposition();
        Shapes();
        Remap();
        ConfirmStates();
        Availability();
        Lookup();
        AuthorityAndTombstone();
        ArrayReplace();
        Geometry();
        PageCapacityAndTransactions();
        PageHolderTransaction();
        Paging();

        Console.WriteLine($"checks={_checks} passed={_checks - _failed} failed={_failed}");
        Console.WriteLine(_failed == 0 ? "RESULT: PASS" : "RESULT: FAIL");
        Environment.Exit(_failed == 0 ? 0 : 1);
    }

    private static void TupleAndPolicyComposition()
    {
        var ids = new List<string>();
        ids.AddRange(GroupA);
        ids.AddRange(GroupA2);
        ids.AddRange(GroupB);
        Check(ids.Count == 16, "16 ids collected");

        int aCount = 0, bCount = 0;
        foreach (string id in ids)
        {
            int land = MountIslandSplitPolicy.GetTargetLand(id);
            bool okUi = ExtensionIslandMapPlan.TryGetSlotUi(land, out int ui);
            bool okLand = ExtensionIslandMapPlan.TryGetSlotPhysical(ui, out int back);
            Check(okUi && okLand && back == land, "tuple roundtrip for " + id);
            if (land == 11) { aCount++; Check(ui == 10, "A lands on UI10: " + id); }
            else if (land == 13) { bCount++; Check(ui == 11, "B lands on UI11: " + id); }
            else { Check(false, "unexpected target land for " + id); }
        }
        Check(aCount == 8 && bCount == 8, "8+8 stable split");

        Check(!ExtensionIslandMapPlan.TryGetSlotUi(10, out int n10) && n10 == -1, "native physical 10 not ours");
        Check(ExtensionIslandMapPlan.IsNativeUiSlot(9), "UI9 native");
        Check(!ExtensionIslandMapPlan.IsNativeUiSlot(10) && !ExtensionIslandMapPlan.IsNativeUiSlot(11), "UI10/11 not native");
        Check(!ExtensionIslandMapPlan.IsExtensionUi(9) && !ExtensionIslandMapPlan.IsExtensionUi(12), "9/12 not extension UI");
        Check(ExtensionIslandMapPlan.TryGetSlotUi(11, out int u11) && u11 == ExtensionIslandMapPlan.PrimaryUi, "11->10");
        Check(ExtensionIslandMapPlan.TryGetSlotUi(13, out int u13) && u13 == ExtensionIslandMapPlan.SecondaryUi, "13->11");
    }

    private static void Shapes()
    {
        Check(ExtensionIslandMapPlan.HasNativeShape(10, 10, 10), "native shape 10/10/10");
        Check(!ExtensionIslandMapPlan.HasNativeShape(12, 12, 12), "12 is not native shape");
        Check(ExtensionIslandMapPlan.HasExtendedShape(12, 12, 12), "extended shape 12/12/12");
        Check(!ExtensionIslandMapPlan.HasExtendedShape(11, 12, 12), "detail short is not extended");
        Check(!ExtensionIslandMapPlan.HasExtendedShape(12, 11, 12), "overview lands short is not extended");
        Check(!ExtensionIslandMapPlan.HasExtendedShape(12, 12, 11), "overview buttons short is not extended");
        Check(ExtensionIslandMapPlan.ExtendedUiCount == 12 && ExtensionIslandMapPlan.NativeUiCount == 10, "10+2=12");
    }

    private static void Remap()
    {
        Check(ExtensionIslandMapPlan.ShouldRemapUpdateLand(10, 10, out int a) && a == 11, "exact A instance 10->11");
        Check(ExtensionIslandMapPlan.ShouldRemapUpdateLand(11, 11, out int b) && b == 13, "exact B instance 11->13");
        Check(!ExtensionIslandMapPlan.ShouldRemapUpdateLand(10, 11, out int c) && c == 11, "cross tuple not remapped");
        Check(!ExtensionIslandMapPlan.ShouldRemapUpdateLand(-1, 10, out int d) && d == 10, "unregistered not remapped");
        Check(!ExtensionIslandMapPlan.ShouldRemapUpdateLand(10, 9, out int e) && e == 9, "native UI9 passthrough");
    }

    private static void ConfirmStates()
    {
        // 阶段 0：只切单岛视图，不产生结果。
        Check(ExtensionIslandMapPlan.DecideConfirm(true, true, 0, true, true, 10, 10, 0)
            == ExtensionConfirmAction.Consumed, "A phase0 consumed");
        Check(ExtensionIslandMapPlan.DecideConfirm(true, true, 0, true, true, 11, 11, 0)
            == ExtensionConfirmAction.Consumed, "B phase0 consumed");

        // 阶段 1 成功：landResult 必须是对应 tuple 的 physical。
        Check(ExtensionIslandMapPlan.DecideConfirm(true, true, 1, true, true, 10, 10, 0)
            == ExtensionConfirmAction.Succeed, "A phase1 succeed");
        Check(ExtensionIslandMapPlan.TryGetConfirmResultLand(10, out int ra) && ra == 11, "A result physical 11");
        Check(ExtensionIslandMapPlan.DecideConfirm(true, true, 1, true, true, 11, 11, 0)
            == ExtensionConfirmAction.Succeed, "B phase1 succeed");
        Check(ExtensionIslandMapPlan.TryGetConfirmResultLand(11, out int rb) && rb == 13, "B result physical 13");
        Check(!ExtensionIslandMapPlan.TryGetConfirmResultLand(9, out _), "native UI has no extension result");
        Check(!ExtensionIslandMapPlan.TryGetConfirmResultLand(12, out _), "UI12 has no extension result");

        // 拒绝/透传矩阵。
        Check(ExtensionIslandMapPlan.DecideConfirm(true, true, 1, true, true, 10, 10, 11)
            == ExtensionConfirmAction.Consumed, "current==target(A) consumed");
        Check(ExtensionIslandMapPlan.DecideConfirm(true, true, 1, true, true, 11, 11, 13)
            == ExtensionConfirmAction.Consumed, "current==target(B) consumed");
        Check(ExtensionIslandMapPlan.DecideConfirm(true, true, 1, true, true, 0, 10, 0)
            == ExtensionConfirmAction.Consumed, "focus mismatch consumed");
        Check(ExtensionIslandMapPlan.DecideConfirm(true, true, 1, false, true, 10, 10, 0)
            == ExtensionConfirmAction.Consumed, "browse-only consumed");
        Check(ExtensionIslandMapPlan.DecideConfirm(true, true, 1, true, false, 10, 10, 0)
            == ExtensionConfirmAction.Consumed, "travel gate closed consumed");
        Check(ExtensionIslandMapPlan.DecideConfirm(true, false, 1, true, true, 10, 10, 0)
            == ExtensionConfirmAction.Consumed, "revoked A consumed");
        Check(ExtensionIslandMapPlan.DecideConfirm(true, false, 1, true, true, 11, 11, 0)
            == ExtensionConfirmAction.Consumed, "revoked B consumed");
        Check(ExtensionIslandMapPlan.DecideConfirm(true, true, 2, true, true, 10, 10, 0)
            == ExtensionConfirmAction.PassThrough, "unknown state not success");
        Check(ExtensionIslandMapPlan.DecideConfirm(false, true, 1, true, true, 10, 10, 0)
            == ExtensionConfirmAction.PassThrough, "foreign menu passthrough");
        Check(ExtensionIslandMapPlan.DecideConfirm(true, true, 1, true, true, 9, 9, 0)
            == ExtensionConfirmAction.PassThrough, "native UI9 passthrough");
        Check(ExtensionIslandMapPlan.DecideConfirm(true, true, 1, true, true, 12, 12, 0)
            == ExtensionConfirmAction.PassThrough, "UI12 passthrough");

        // 阶段 0 按钮可用性按 tuple 目标判。
        Check(ExtensionIslandMapPlan.Stage0ConfirmInteractable(true, true, 0, 11), "A button enabled");
        Check(!ExtensionIslandMapPlan.Stage0ConfirmInteractable(true, true, 11, 11), "A button disabled when on A");
        Check(!ExtensionIslandMapPlan.Stage0ConfirmInteractable(true, true, 13, 13), "B button disabled when on B");
        Check(!ExtensionIslandMapPlan.Stage0ConfirmInteractable(false, true, 0, 13), "button disabled when cannot select");
    }

    private static void Availability()
    {
        Check(ExtensionIslandMapPlan.ResolveCanTravel(true, true, true), "travel all true");
        Check(!ExtensionIslandMapPlan.ResolveCanTravel(false, true, true), "unknown snapshot forbids travel");
        Check(!ExtensionIslandMapPlan.ResolveCanTravel(true, false, true), "unavailable slot forbids travel");
        Check(!ExtensionIslandMapPlan.ResolveCanTravel(true, true, false), "campaign gate closed forbids travel");
        Check(!ExtensionIslandMapPlan.ResolveCanTravel(false, false, false), "all false forbids travel");
    }

    private static void Lookup()
    {
        Check(ExtensionIslandMapPlan.LookupUiForPhysical(11, true) == 10, "A dict 11->10");
        Check(ExtensionIslandMapPlan.LookupUiForPhysical(13, true) == 11, "B dict 13->11 even when unavailable slot node");
        Check(ExtensionIslandMapPlan.LookupUiForPhysical(11, false) == 9, "A degenerate focus 9 when UI absent");
        Check(ExtensionIslandMapPlan.LookupUiForPhysical(13, false) == 9, "B degenerate focus 9 when UI absent");
        Check(ExtensionIslandMapPlan.LookupUiForPhysical(10, true) == -1, "native physical 10 never written");
        Check(ExtensionIslandMapPlan.LookupUiForPhysical(0, true) == -1, "native physical 0 never written");
        Check(ExtensionIslandMapPlan.LookupUiForPhysical(12, true) == -1, "reserved palace 12 never written");
        Check(ExtensionIslandMapPlan.FallbackFocusUi == 9, "fallback focus UI9");
    }

    private static void AuthorityAndTombstone()
    {
        Check(ExtensionIslandMapPlan.HasAuthority(true, true, true, true), "full authority");
        Check(!ExtensionIslandMapPlan.HasAuthority(false, true, true, true), "foreign same-index instance no authority");
        Check(!ExtensionIslandMapPlan.HasAuthority(true, false, true, true), "other menu no authority");
        Check(!ExtensionIslandMapPlan.HasAuthority(true, true, false, true), "campaign change no authority");
        Check(!ExtensionIslandMapPlan.HasAuthority(true, true, true, false), "broken slots no authority");
        Check(ExtensionIslandMapPlan.ShouldConsumeTombstone(true, true, 10), "tombstone consumes A ui");
        Check(ExtensionIslandMapPlan.ShouldConsumeTombstone(true, true, 11), "tombstone consumes B ui");
        Check(!ExtensionIslandMapPlan.ShouldConsumeTombstone(true, true, 9), "tombstone does not consume native ui");
        Check(!ExtensionIslandMapPlan.ShouldConsumeTombstone(true, false, 11), "no tombstone for this menu -> passthrough");
        Check(!ExtensionIslandMapPlan.ShouldConsumeTombstone(false, true, 11), "other menu tombstone not ours");
    }

    private static void ArrayReplace()
    {
        Check(ExtensionIslandMapPlan.DecideArrayReplace(true, true) == ExtensionArrayReplaceOutcome.Complete, "both confirmed complete");
        Check(ExtensionIslandMapPlan.DecideArrayReplace(true, false) == ExtensionArrayReplaceOutcome.Rollback, "second array failed -> rollback");
        Check(ExtensionIslandMapPlan.DecideArrayReplace(false, true) == ExtensionArrayReplaceOutcome.Rollback, "first array failed -> rollback");
        Check(ExtensionIslandMapPlan.DecideArrayReplace(false, false) == ExtensionArrayReplaceOutcome.Rollback, "both failed -> rollback");
    }

    private static void Geometry()
    {
        var band = new MountIslandFrame(0f, 0f, 200f, 60f);
        Check(MountIslandOverviewLayout.TryPlanTwoFrames(band, 30f, 20f, out MountIslandFrame p, out MountIslandFrame s, out float scale),
            "two frames planned in wide band");
        Check(p.IsValid && s.IsValid, "both frames valid");
        Check(p.IsInside(band) && s.IsInside(band), "both frames inside paper");
        Check(!p.Intersects(s), "frames disjoint");
        Check(p.HorizontalGap(s) >= MountIslandOverviewLayout.MinFrameGap - 0.001f, "min gap respected");
        Check(!MountIslandOverviewLayout.SameFrame(p, s), "distinct frames");
        Check(scale <= 1f + 0.0001f, "scale never larger than native proportion");
        Check(scale > 0f, "planned scale positive");
        Check(Math.Abs(p.Width - s.Width) < 0.0001f && Math.Abs(p.Height - s.Height) < 0.0001f, "unified scale for both islands");
        Check(Math.Abs(p.Width / p.Height - 30f / 20f) < 0.0001f, "primary aspect preserved");
        Check(Math.Abs(s.Width / s.Height - 30f / 20f) < 0.0001f, "secondary aspect preserved");

        // shore/outline/hitbox/ship 同框语义。
        var same = new MountIslandFrame(p.X0, p.Y0, p.X1, p.Y1);
        Check(MountIslandOverviewLayout.SameFrame(p, same), "identical frame components share frame");
        var drifted = new MountIslandFrame(p.X0, p.Y0, p.X1 + 0.1f, p.Y1);
        Check(!MountIslandOverviewLayout.SameFrame(p, drifted), "drifted frame rejected");

        // 无魔数下限：窄/矮常规窗口不被常量拦下（仍是同一算法/同 scale，不 clamp）。
        var narrow = new MountIslandFrame(0f, 0f, 12f, 60f);
        Check(MountIslandOverviewLayout.TryPlanTwoFrames(narrow, 30f, 20f, out _, out _, out float narrowScale)
            && narrowScale > 0f, "narrow window still planned (no magic floor)");
        Check(!MountIslandOverviewLayout.TryPlanTwoFrames(narrow, 30f, 20f,
                MountIslandOverviewLayout.SuggestedMinimumFrameScale, out _, out _, out float flooredScale)
            && flooredScale == 0f, "caller-supplied minScale rejects, no clamp");
        var shortBand = new MountIslandFrame(0f, 0f, 200f, 10f);
        Check(MountIslandOverviewLayout.TryPlanTwoFrames(shortBand, 30f, 20f, out _, out _, out _),
            "short window planned without floor");
        var degenerateBand = new MountIslandFrame(0f, 0f, 200f, 2f);
        Check(!MountIslandOverviewLayout.TryPlanTwoFrames(degenerateBand, 30f, 20f, out _, out _, out _),
            "degenerate band rejected");
        Check(!MountIslandOverviewLayout.TryPlanTwoFrames(default, 30f, 20f, out _, out _, out _), "degenerate band rejected");
        Check(!MountIslandOverviewLayout.TryPlanTwoFrames(band, 0f, 20f, out _, out _, out _), "zero base width rejected");

        // 总览零自有资源请求 / native 不接管。
        Check(!MountIslandOverviewLayout.BuildsOwnResourceRequests(true), "overview builds zero own requests");
        Check(MountIslandOverviewLayout.BuildsOwnResourceRequests(false), "detail may build its requests");
        for (int ui = 0; ui <= 12; ui++)
        {
            Check(ExtensionIslandMapPlan.IsExtensionUi(ui) == (ui == 10 || ui == 11),
                "exact extension UI classification for " + ui);
        }

        // base 归一：native 1x art × 共同 uniformScale 后的实际呈现尺寸（不是 raw prefab）。
        Check(Math.Abs(MountIslandOverviewLayout.NormalizeBaseSize(30f, 0.6f) - 18f) < 0.0001f,
            "base normalized by applied uniform scale");
        Check(MountIslandOverviewLayout.NormalizeBaseSize(30f, 0f) == 0f
            && MountIslandOverviewLayout.NormalizeBaseSize(0f, 1f) == 0f, "degenerate base rejected");
        Check(MountIslandOverviewLayout.TryBaseInAspect(18f, 18f, 228f / 84f, out float baseW, out float baseH)
            && Math.Abs(baseW / baseH - 228f / 84f) < 0.0001f && baseW <= 18f && baseH <= 18f,
            "base converted to shore aspect within native bbox");

        // state 图标（船标）：保持自身尺寸、按锚点定位、不得拉成整岛 bbox。
        var islandBox = new MountIslandFrame(10f, 10f, 70f, 40f);
        Check(MountIslandOverviewLayout.TryAnchorStateIcon(islandBox, 6f, 4f, 40f, 25f, out MountIslandFrame ship),
            "state icon anchored inside island frame");
        Check(Math.Abs(ship.Width - 6f) < 0.0001f && Math.Abs(ship.Height - 4f) < 0.0001f,
            "state icon keeps its own size (not stretched to island bbox)");
        Check(!MountIslandOverviewLayout.SameFrame(islandBox, ship), "state icon is not the island bbox");
        Check(!MountIslandOverviewLayout.SameFrame(new MountIslandFrame(p.X0, p.Y0, p.X1, p.Y1),
                new MountIslandFrame(s.X0, s.Y0, s.X1, s.Y1)), "terrain frames of A/B are distinct")
            ;
        Check(MountIslandOverviewLayout.TryAnchorStateIcon(islandBox, 6f, 4f, 40f, 25f, out MountIslandFrame ship2)
            && MountIslandOverviewLayout.SameAnchorPoint(40f, 25f, 40f, 25f, 0.001f), "state anchor comparable across built frames");
        Check(!MountIslandOverviewLayout.TryAnchorStateIcon(islandBox, 200f, 100f, 40f, 25f, out _),
            "state icon larger than island rejected (no clamp)");
        Check(!MountIslandOverviewLayout.TryAnchorStateIcon(islandBox, 6f, 4f, 200f, 25f, out _),
            "state anchor outside island rejected");
    }

    private static void PageCapacityAndTransactions()
    {
        // 有限枚举页容量：每个 candidate 的每一页都必须通过真实预验；首个全页可行才 commit。
        int count = 16;
        bool AllFit8(int off, int len) => off >= 0 && len == 8;
        Check(MountIslandDetailPagePlan.TryFindUniformPageCapacity(count, count, AllFit8, out int cap8) && cap8 == 8,
            "capacity 8 accepted when every page fits");
        Check(MountIslandDetailPagePlan.CoversAllOnce(count, cap8, 1), "capacity 8 covers all entries once");

        // 非贪心：7 宽首页不可行、6 宽（页 6,6,4）全页可行 → 必须降到 6（枚举每页预验，不是只看首前缀）。
        bool NonGreedy(int off, int len) => len <= 7 && !(off == 0 && len == 7);   // 仅首页 7 宽不可行
        Check(MountIslandDetailPagePlan.TryFindUniformPageCapacity(16, 16, NonGreedy, out int cap6) && cap6 == 6,
            "non-greedy: 7 rejected (first page), capacity 6 chosen across all pages");
        Check(MountIslandDetailPagePlan.CoversAllOnce(16, cap6, 1), "capacity 6 covers all entries once");

        // 宽末页场景：count 15，页(8,7)因首宽 8 失败；页(7,7,1)全过 → 7。
        bool WideLastOk(int off, int len) => len <= 7;
        Check(MountIslandDetailPagePlan.TryFindUniformPageCapacity(15, 15, WideLastOk, out int capWide) && capWide == 7,
            "wide last page: capacity 7 with last page 1");
        Check(MountIslandDetailPagePlan.TryPageRange(15, capWide, 1, 2, out int lastOff, out int lastLen)
            && lastOff == 14 && lastLen == 1, "last page holds the single remaining entry");

        // 任一（含第 2 页起的）失败页即拒绝该 candidate，直到出现全页可行的更小容量。
        bool SomePageFails(int off, int len) => len <= 5 && !(off == 5 && len == 5);
        Check(MountIslandDetailPagePlan.TryFindUniformPageCapacity(16, 16, SomePageFails, out int cap4) && cap4 == 4,
            "later-page failure rejects candidate 5; capacity 4 adopted");
        Check(!MountIslandDetailPagePlan.TryFindUniformPageCapacity(16, 16, (off, len) => false, out int none) && none == 0,
            "no feasible capacity -> 0 (whole fallback)");
        Check(!MountIslandDetailPagePlan.TryFindUniformPageCapacity(0, 16, AllFit8, out _), "empty request set -> no capacity");
        Check(!MountIslandDetailPagePlan.TryFindUniformPageCapacity(16, 16, null, out _), "null planner rejected");

        // 字典两键追加事务：CAS 恢复/移除，foreign 值绝不覆盖。
        var entry = MountIslandMapDictTransaction.Capture(11, true, 3, 10);
        Check(MountIslandMapDictTransaction.ShouldRestoreRollback(entry, true, 10), "rollback restores prior when value is ours");
        Check(!MountIslandMapDictTransaction.ShouldRestoreRollback(entry, true, 7), "rollback leaves foreign value untouched");
        Check(!MountIslandMapDictTransaction.ShouldRestoreRollback(entry, false, 0), "absent key not restored");
        var fresh = MountIslandMapDictTransaction.Capture(13, false, 0, 11);
        Check(MountIslandMapDictTransaction.ShouldRemoveRollback(fresh, true, 11), "rollback removes key we created");
        Check(!MountIslandMapDictTransaction.ShouldRemoveRollback(fresh, true, 4), "rollback keeps foreign value on fresh key");
        Check(!MountIslandMapDictTransaction.ShouldRemoveRollback(entry, true, 10), "pre-existing key never removed");
        Check(MountIslandMapDictTransaction.OwnsExpectedValue(10, 10) && !MountIslandMapDictTransaction.OwnsExpectedValue(9, 10),
            "degrade only on owned expected mapping");
        Check(MountIslandMapDictTransaction.RequiresPoisonOnRollbackFailure(true),
            "unknown readback after rollback poisons the menu generation");
        Check(!MountIslandMapDictTransaction.RequiresPoisonOnRollbackFailure(false),
            "clean rollback does not poison");

        // tuple 处置：授权/可用性未知关闭只 LockHidden，不 Revoke；真外部失效才 Revoke。
        Check(ExtensionIslandMapPlan.DecideSlotDisposition(true, true, true, true, false) == ExtensionSlotDisposition.LockHidden,
            "unauthorized but intact registration -> LockHidden");
        Check(ExtensionIslandMapPlan.DecideSlotDisposition(true, true, true, true, true) == ExtensionSlotDisposition.Proceed,
            "authorized -> Proceed");
        Check(ExtensionIslandMapPlan.DecideSlotDisposition(true, true, false, true, true) == ExtensionSlotDisposition.Revoked,
            "broken slots -> Revoked");
        Check(ExtensionIslandMapPlan.DecideSlotDisposition(true, false, true, true, true) == ExtensionSlotDisposition.Revoked,
            "campaign change -> Revoked");
        Check(ExtensionIslandMapPlan.DecideSlotDisposition(true, true, true, false, true) == ExtensionSlotDisposition.Revoked,
            "scope lost -> Revoked");
    }

    private static void PageHolderTransaction()
    {
        // 生产 helper：翻页组事务顺序/回滚（同一 helper 被 MapMountIcons.RebuildDetailPageIcons 调用）。
        var calls = new List<string>();
        bool ok = MountIslandPageHolderTransaction.TryRunPageSwap(
            () => calls.Add("swap"),
            () => calls.Add("activate"),
            () => calls.Add("destroyOld"),
            () => calls.Add("rollback"));
        Check(ok && string.Join(",", calls) == "swap,activate,destroyOld",
            "page swap order: swap -> activate -> destroyOld");

        // activate 失败：必须回滚并保留旧页（不能吞成成功、不能先销旧组）。
        var calls2 = new List<string>();
        bool ok2 = MountIslandPageHolderTransaction.TryRunPageSwap(
            () => calls2.Add("swap"),
            () => throw new InvalidOperationException("activate"),
            () => calls2.Add("destroyOld"),
            () => calls2.Add("rollback"));
        Check(!ok2 && calls2.Contains("rollback") && !calls2.Contains("destroyOld"),
            "activate failure rolls back and never destroys the old group");

        // swap 失败：同样回滚，且不得经过 activate/destroyOld。
        var calls3 = new List<string>();
        bool ok3 = MountIslandPageHolderTransaction.TryRunPageSwap(
            () => throw new InvalidOperationException("swap"),
            () => calls3.Add("activate"),
            () => calls3.Add("destroyOld"),
            () => { calls3.Add("rollback"); return; });
        Check(!ok3 && calls3.Count == 1 && calls3[0] == "rollback", "swap failure rolls back only");
        Check(!MountIslandPageHolderTransaction.TryRunPageSwap(null, () => { }, () => { }, () => { }),
            "missing callbacks rejected");
    }

    private static void Paging()
    {
        int[] counts = { 8, 7, 15, 16 };
        foreach (int count in counts)
        {
            foreach (var (rows, columns) in new[] { (4, 2), (4, 1), (2, 2) })
            {
                int perPage = rows * columns;
                Check(MountIslandDetailPagePlan.CoversAllOnce(count, rows, columns), $"cover all once for {count} at {perPage}/page");
                int pages = MountIslandDetailPagePlan.PageCount(count, rows, columns);
                Check(pages == MountMapLayoutPlan.PageCount(count, rows, columns), $"cross-check real PageCount for {count}/{perPage}");
                int cursor = 0;
                for (int page = 0; page < pages; page++)
                {
                    Check(MountIslandDetailPagePlan.TryPageRange(count, rows, columns, page, out int start, out int length),
                        $"page {page} of {count} reachable");
                    Check(start == cursor, $"page {page} contiguous for {count}");
                    cursor += length;
                }
                Check(cursor == count, $"all {count} entries reachable at {perPage}/page");
                Check(!MountIslandDetailPagePlan.TryPageRange(count, rows, columns, pages, out _, out _), "out of range page rejected");
            }
        }
        Check(MountIslandDetailPagePlan.PageCount(0, 4, 2) == 0, "zero entries -> no pages");
        Check(!MountIslandDetailPagePlan.CoversAllOnce(0, 4, 2), "empty detail has no page");
        Check(MountIslandDetailPagePlan.TryPageRange(15, 4, 2, 1, out int s1, out int l1) && s1 == 8 && l1 == 7, "legacy 15 second page holds 7");
        Check(MountIslandDetailPagePlan.TryPageRange(16, 4, 2, 1, out int s2, out int l2) && s2 == 8 && l2 == 8, "legacy 16 second page holds 8");
    }
}
