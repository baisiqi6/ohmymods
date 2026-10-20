using System;
using System.Collections.Generic;
using System.IO;
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
        LookupLifecycleCounterexamples();
        ExtensionIslandMapWiring();
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

        // 字典两键追加事务：release CAS 归还（append-rollback 复用同一决策），foreign 值绝不覆盖。
        // 纯决策逐值断言（执行/故障注入证据在 r2host 真实方法体 host，见 tests/mount-island-map/r2host）。
        var entry = MountIslandMapDictTransaction.Capture(11, true, 3, 10);
        Check(MountIslandMapDictTransaction.ShouldRestoreRelease(entry, 10, true, 10),
            "release restores prior when current is our last-owned");
        Check(!MountIslandMapDictTransaction.ShouldRestoreRelease(entry, 10, true, 7),
            "release leaves foreign value untouched");
        Check(!MountIslandMapDictTransaction.ShouldRestoreRelease(entry, 10, false, 0),
            "absent key not restored");
        var fresh = MountIslandMapDictTransaction.Capture(13, false, 0, 11);
        Check(MountIslandMapDictTransaction.ShouldRemoveRelease(fresh, 11, true, 11),
            "release removes key we created");
        Check(!MountIslandMapDictTransaction.ShouldRemoveRelease(fresh, 11, true, 4),
            "release keeps foreign value on fresh key");
        Check(!MountIslandMapDictTransaction.ShouldRemoveRelease(entry, 10, true, 10),
            "pre-existing key never removed");
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

    // ==================================================================================
    // 字典生命周期纯决策 counterexamples：直链真实 MountIslandMapDictTransaction/Entry，
    // 镜像产品 ReleaseLookup/DegradeLookupForRevoke 的编排顺序驱动真实 Dictionary<int,int>
    //（镜像已对齐生产：prior 同值零写通过、foreign continue 不读回；uncertain 门属代级状态，
    // 由 r2host 真实方法体 host 覆盖——见 tests/mount-island-map/r2host，不在此重复执行证据）。
    // 接线正确性（真实 ExtensionIslandMap 谁在何时调这些决策）由 ExtensionIslandMapWiring 覆盖。
    // ==================================================================================

    /// <summary>镜像产品 ReleaseLookup 的单键编排：prior 恢复/新建删除/foreign 不碰（无读回）。</summary>
    private static bool ReleaseKey(Dictionary<int, int> dict, in MountIslandMapDictEntry entry, int lastOwned)
    {
        bool present = dict.ContainsKey(entry.Physical);
        int current = present ? dict[entry.Physical] : 0;
        if (present && current == entry.PriorValue) return true;   // 已处于 prior 状态：零写通过
        if (MountIslandMapDictTransaction.ShouldRestoreRelease(entry, lastOwned, present, current))
        {
            dict[entry.Physical] = entry.PriorValue;
        }
        else if (MountIslandMapDictTransaction.ShouldRemoveRelease(entry, lastOwned, present, current))
        {
            dict.Remove(entry.Physical);
        }
        else
        {
            return true;   // foreign 改值/已删键：不接管不读回，责任消解
        }
        bool afterPresent = dict.ContainsKey(entry.Physical);
        int afterValue = afterPresent ? dict[entry.Physical] : 0;
        return MountIslandMapDictTransaction.ReleaseReadbackMatches(entry, afterPresent, afterValue);
    }

    private static void LookupLifecycleCounterexamples()
    {
        // --- 1) absent → append → 整代 clear → 再 append：无残留 9（旧实现的缺陷路径）。
        var dict = new Dictionary<int, int>();
        var priorA = MountIslandMapDictTransaction.Capture(11, false, 0, 10);
        var priorB = MountIslandMapDictTransaction.Capture(13, false, 0, 11);
        dict[11] = 10;
        dict[13] = 11;   // append 写入
        bool clean = ReleaseKey(dict, priorA, 10) && ReleaseKey(dict, priorB, 11);
        Check(clean, "generation release clean after append");
        Check(!dict.ContainsKey(11) && !dict.ContainsKey(13), "clear deletes keys absent before append (no residual 9)");
        Check(dict.Count == 0, "dict back to native-only after clear");
        // 再 append 的 first capture 又是 absent → 不再被拒（旧实现残留 9 会被当 foreign 拒绝）。
        bool reHad = dict.ContainsKey(11);
        Check(!reHad, "re-append sees absent prior (self cleanup no longer blocks re-registration)");
        // 对照（缺陷的决策级证明）：旧实现的 clear 走泛降级——降级 CAS 对 9 不 owned
        // （OwnsExpectedValue(9,10)==false）→ 不清理 → 残留 9；随后 append 的接管检查对 9
        // 同样不 owned → 拒绝重登记。本修复以 release 决策取代该路径。
        Check(!MountIslandMapDictTransaction.OwnsExpectedValue(
                  ExtensionIslandMapPlan.FallbackFocusUi,
                  ExtensionIslandMapPlan.LookupUiForPhysical(11, true)),
            "legacy degrade CAS never owned the residual 9 (append then refused: defect decision proven)");

        // --- 2) A revoke 自己写 fallback 9（兄弟 B 仍在）：A=9、B 保持 11；整代 clear 两者都归还。
        dict.Clear();
        dict[11] = 10;
        dict[13] = 11;
        Check(MountIslandMapDictTransaction.ShouldDegradeRevoke(10, 10), "A degrade allowed on owned 10");
        dict[11] = 9;   // RevokeSlot 降级写 + last-owned 账目更新为 9
        int lastOwnedA = 9, lastOwnedB = 11;
        Check(dict[13] == 11, "B mapping untouched while A revoked (hidden B keeps 13->11)");
        clean = ReleaseKey(dict, priorA, lastOwnedA) && ReleaseKey(dict, priorB, lastOwnedB);
        Check(clean, "generation release clean after own-fallback-9 revoke");
        Check(!dict.ContainsKey(11) && !dict.ContainsKey(13), "generation clear returns A's own 9 and B's 11");

        // --- 3) prior 存在/不存在：精确归还。
        dict.Clear();
        dict[11] = 3;   // 上一代遗留 prior=3（append 只接管 owned 期望值 10，3 会被拒——由 WriteLookup 测试覆盖）
        var priorHad = MountIslandMapDictTransaction.Capture(11, true, 3, 10);
        dict[11] = 10;   // 假设某代以 had=true 成功入账（当前值==owned 期望 10 的接管场景）
        Check(ReleaseKey(dict, priorHad, 10) && dict.ContainsKey(11) && dict[11] == 3,
            "release restores exact pre-existing prior value");

        // --- 4) 重复 clear 无写：CAS 已失配 → 决策不动字典。
        Check(!MountIslandMapDictTransaction.ShouldRestoreRelease(priorHad, 10, true, 3)
            && !MountIslandMapDictTransaction.ShouldRemoveRelease(priorHad, 10, true, 3),
            "repeat clear writes nothing (CAS mismatch after restore)");
        Check(!MountIslandMapDictTransaction.ShouldRestoreRelease(priorA, 10, false, 0)
            && !MountIslandMapDictTransaction.ShouldRemoveRelease(priorA, 10, false, 0),
            "repeat clear on deleted key writes nothing");

        // --- 5) 真 foreign 9（本代账目是 10，从没写过 9）：不认、不还、降级拒绝。
        Check(!MountIslandMapDictTransaction.ShouldRestoreRelease(priorHad, 10, true, 9)
            && !MountIslandMapDictTransaction.ShouldRemoveRelease(priorHad, 10, true, 9),
            "foreign 9 never owned when ledger says 10");
        Check(!MountIslandMapDictTransaction.ShouldDegradeRevoke(9, 10),
            "degrade refuses to treat foreign 9 as ours");
        Check(!MountIslandMapDictTransaction.ShouldRestoreRelease(priorHad, 10, true, 7)
            && !MountIslandMapDictTransaction.ShouldRemoveRelease(priorHad, 10, true, 7),
            "foreign rewrite left alone on release");
        Check(!MountIslandMapDictTransaction.ShouldDegradeRevoke(7, 10),
            "degrade refuses foreign value without generation authorization");

        // --- 6) 本代写的 9（last-owned 已记账）被整代归还认领。
        Check(MountIslandMapDictTransaction.ShouldRemoveRelease(priorA, 9, true, 9),
            "own fallback 9 removed at release when ledger recorded it");
        Check(MountIslandMapDictTransaction.ShouldRestoreRelease(priorHad, 9, true, 9),
            "own fallback 9 restores prior when ledger recorded it");

        // --- 7) postconditions：setter 静默失败 / remove 读回未知 → 责任保留（poison）。
        Check(MountIslandMapDictTransaction.ReleaseReadbackMatches(priorHad, true, 3),
            "restore readback matches prior");
        Check(!MountIslandMapDictTransaction.ReleaseReadbackMatches(priorHad, true, 9),
            "restore silent-failure readback (still 9) is not clean");
        Check(!MountIslandMapDictTransaction.ReleaseReadbackMatches(priorHad, true, 10),
            "restore silent-failure readback (still our written 10) is not clean");
        var priorFresh = MountIslandMapDictTransaction.Capture(13, false, 0, 11);
        Check(MountIslandMapDictTransaction.ReleaseReadbackMatches(priorFresh, false, 0),
            "remove readback clean when key gone");
        Check(!MountIslandMapDictTransaction.ReleaseReadbackMatches(priorFresh, true, 11),
            "remove readback unknown when key still present");
        Check(!MountIslandMapDictTransaction.ReleaseReadbackMatches(priorFresh, true, 5),
            "remove readback unknown when foreign residue present");
        Check(MountIslandMapDictTransaction.RequiresPoisonOnRollbackFailure(true),
            "unknown release readback poisons the menu generation");

        // --- 8) native 0..10/12 逐值：扩展永不写这些键。
        for (int physical = 0; physical <= 13; physical++)
        {
            if (physical == 11 || physical == 13) continue;
            Check(ExtensionIslandMapPlan.LookupUiForPhysical(physical, true) == -1
                && ExtensionIslandMapPlan.LookupUiForPhysical(physical, false) == -1,
                "native/reserved physical " + physical + " never written in either mode");
        }

        // --- 9) B hidden（LockHidden）不触发任何字典写：处置决策与键值互不干扰。
        Check(ExtensionIslandMapPlan.DecideSlotDisposition(true, true, true, true, false) == ExtensionSlotDisposition.LockHidden
            && ExtensionIslandMapPlan.LookupUiForPhysical(13, true) == 11,
            "hidden B keeps 13->11 (no degrade from LockHidden)");
    }

    // ==================================================================================
    // 真实 ExtensionIslandMap 调用接线（源码级断言：产品文件含 Unity/Il2Cpp 依赖无法离线执行，
    // 这里锁定生命周期编排本身——谁在何时 capture/入账/降级/归还，防止接线回退；
    // native-map-runtime 的替身不能充当这个产品类的执行证据）。
    // ==================================================================================

    private static string ReadProductionSource(string fileName)
    {
        // 输出目录可能在仓库外（隔离 bin/obj）：优先 KEM_SOURCE_ROOT 显式指定源码根（worktree），
        // 否则从程序集位置向上找 il2cpp/。
        string root = Environment.GetEnvironmentVariable("KEM_SOURCE_ROOT");
        if (!string.IsNullOrEmpty(root))
        {
            string explicitPath = Path.Combine(root, "il2cpp", fileName);
            if (File.Exists(explicitPath)) return File.ReadAllText(explicitPath);
        }
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            string candidate = Path.Combine(dir, "il2cpp", fileName);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("production source not found: " + fileName
            + " (set KEM_SOURCE_ROOT to the worktree root)");
    }

    private static string ExtractMethodBody(string source, string methodSignature)
    {
        int start = source.IndexOf(methodSignature, StringComparison.Ordinal);
        Check(start >= 0, "method present in ExtensionIslandMap.cs: " + methodSignature);
        if (start < 0) return "";
        int endInternal = source.IndexOf("\n        internal static", start, StringComparison.Ordinal);
        int endPrivate = source.IndexOf("\n        private static", start, StringComparison.Ordinal);
        int end = source.Length;
        if (endInternal >= 0) end = Math.Min(end, endInternal);
        if (endPrivate >= 0) end = Math.Min(end, endPrivate);
        return source.Substring(start, end - start);
    }

    private static void ExtensionIslandMapWiring()
    {
        string source = ReadProductionSource("ExtensionIslandMap.cs");

        // append 事务：capture 后、第一次写入前挂账并发布 pending；写固定用 exact dict；发布前复核 dict 身份。
        Check(source.Contains("AttachLookupLedger(generation, lookupDict, lookupPriors);"),
            "first-capture ledger attached before any write");
        Check(source.Contains("_generation = generation;   // pending 责任"),
            "pending ledger published before first dictionary write");
        Check(source.Contains("FixLookupOnDict(lookupDict, true, true)"), "append writes use the captured exact dictionary");
        Check(source.Contains("if (!LookupDictStillCurrent(menu, lookupDict)) return;"),
            "publish boundary rechecks menu dictionary identity (D1/D2 swap refuses publish)");
        Check(!source.Contains("private static bool FixLookup("),
            "ledger-less FixLookup entry point removed entirely (no value-based 9 writes)");
        Check(!source.Contains("RestoreLookupPriors("),
            "legacy void rollback without postconditions removed");

        // EnsureAppended 编排：no-extension 零写；失败 finally 走 append-rollback 账目归还。
        string ensureBody = ExtractMethodBody(source, "private static void EnsureAppended");
        int captureCount = 0;
        for (int at = ensureBody.IndexOf("CaptureLookupPriors(", StringComparison.Ordinal); at >= 0;
            at = ensureBody.IndexOf("CaptureLookupPriors(", at + 1, StringComparison.Ordinal))
        {
            captureCount++;
        }
        Check(ensureBody.IndexOf("FixLookup(", StringComparison.Ordinal) < 0,
            "no FixLookup call anywhere in EnsureAppended (no-extension writes zero)");
        int noExtStart = ensureBody.IndexOf("no-extension-authorized", StringComparison.Ordinal);
        Check(noExtStart >= 0
            && ensureBody.IndexOf("return;", noExtStart, StringComparison.Ordinal) >= 0
            && CountWritesBetween(ensureBody, noExtStart, ensureBody.IndexOf("return;", noExtStart, StringComparison.Ordinal)) == 0,
            "no-extension branch performs zero dictionary writes");
        Check(ensureBody.Contains("ReleaseLookup(generation, \"append-rollback\")"),
            "failed append rolls back the pending ledger with real postconditions");
        Check(ensureBody.Contains("if (LookupDictStillCurrent(menu, lookupDict))") == false,
            "dict identity check gates publish on refusal path only");

        // OnLandsCleared：按账目归还，不再泛降级 9；poison/_generation 只在责任消解后清。
        string clearBody = ExtractMethodBody(source, "internal static void OnLandsCleared");
        Check(!clearBody.Contains("FixLookup("), "native-clear no longer degrades via FixLookup(false,false)");
        Check(clearBody.Contains("ReleaseLookup(generation, \"native-clear\")"), "native-clear releases lookup by generation ledger");
        Check(clearBody.Contains("if (lookupClean)") && clearBody.Contains("_poisonedMenu = null")
            && clearBody.Contains("_generation = null"),
            "poison/generation cleared only after lookup responsibility settled");

        // TryReclaimGeneration：字典责任先归还；drift 与成功路径都不得无条件丢账。
        string reclaimBody = ExtractMethodBody(source, "private static void TryReclaimGeneration");
        Check(reclaimBody.Contains("ReleaseLookup(generation, \"reclaim\")"), "generation reclaim releases lookup ledger first");
        Check(reclaimBody.Contains("if (lookupClean) _generation = null;"), "map-drift path keeps unpaid ledger");
        int nullCount = 0;
        int unsafeNull = 0;
        for (int at = reclaimBody.IndexOf("_generation = null", StringComparison.Ordinal); at >= 0;
            at = reclaimBody.IndexOf("_generation = null", at + 1, StringComparison.Ordinal))
        {
            nullCount++;
            int windowStart = Math.Max(0, at - 120);
            if (reclaimBody.IndexOf("lookupClean", windowStart, at - windowStart, StringComparison.Ordinal) < 0) unsafeNull++;
        }
        Check(nullCount >= 2 && unsafeNull == 0, "every reclaim _generation=null sits behind the lookupClean gate");

        // RevokeSlot：降级走 exact dict + last-owned 账目；不再全量 FixLookup 重写两键。
        string revokeBody = ExtractMethodBody(source, "private static void RevokeSlot");
        Check(!revokeBody.Contains("FixLookup("), "revoke no longer rewrites both keys via FixLookup");
        Check(revokeBody.Contains("DegradeLookupForRevoke(revokeGeneration, slot)"), "revoke degrades via exact dict with ledger update");

        // 幂等刷新不重捕获、不写字典；新 append 不得覆盖未结旧代责任。
        int idempotentStart = ensureBody.IndexOf("RefreshAvailability(menu);", StringComparison.Ordinal);
        int idempotentEnd = ensureBody.IndexOf("return;   // 本轮不重建", StringComparison.Ordinal);
        Check(idempotentStart >= 0 && idempotentEnd > idempotentStart
            && ensureBody.IndexOf("FixLookup", idempotentStart, idempotentEnd - idempotentStart, StringComparison.Ordinal) < 0,
            "idempotent refresh no longer rewrites the lookup dict");
        Check(ensureBody.Contains("ReleaseLookup(previousGeneration,"),
            "new append settles previous generation ledger before replacing it");
        Check(ensureBody.Contains("pending-superseded"),
            "same-menu reentrant pending ledger also settled before replacement");

        // R3 SetActive 重入边界：visibility 全部先于 commit；guard 后纯 managed 赋值；
        // finally 数组回滚只 CAS 本次替换引用。
        int visAt = ensureBody.IndexOf("ApplySlotVisibility(created[s]);", StringComparison.Ordinal);
        int guardAt = ensureBody.IndexOf("if (!LookupCommitGuard(generation, menu, lookupDict)) return;", StringComparison.Ordinal);
        int commitAt = ensureBody.IndexOf("Slots[slotIndex] = slot;", StringComparison.Ordinal);
        Check(visAt >= 0 && guardAt > visAt && commitAt > guardAt,
            "R3 visibility precedes commit guard precedes managed commit");
        int tombstoneAt = ensureBody.IndexOf("if (!RemoveTombstoneForPendingGeneration(generation)) return;", StringComparison.Ordinal);
        Check(tombstoneAt > visAt && tombstoneAt < guardAt,
            "tombstone Unity wrapper checks precede final commit guard");
        int commitEnd = ensureBody.IndexOf("_menuToken = new object();", StringComparison.Ordinal);
        Check(commitEnd > commitAt
            && ensureBody.IndexOf("ApplySlotVisibility", commitAt, commitEnd - commitAt, StringComparison.Ordinal) < 0,
            "R3 managed commit loop contains no Unity writes");
        Check(ensureBody.Contains("RestoreReplacedArraysIfStillOurs(map, newLands, newButtons, originalLands, originalButtons);"),
            "R3 failed-append rollback restores arrays by CAS on replaced references only");
        int finAt = ensureBody.IndexOf("finally", StringComparison.Ordinal);
        string rollbackBody = ensureBody.Substring(finAt);
        Check(rollbackBody.Split("CanPoisonAppendAttempt(generation)", StringSplitOptions.None).Length - 1 == 2,
            "both failed-append poison writes retain attempt identity gates");
        Check(finAt > 0
            && ensureBody.IndexOf("map._lands = originalLands;", finAt, StringComparison.Ordinal) < 0,
            "R3 no unconditional original-array writeback in finally");
        string releaseBody = ExtractMethodBody(source, "private static bool ReleaseLookup");
        Check(releaseBody.Contains("if (generation.LookupUncertain)"),
            "release refuses to settle uncertain ledger as foreign");
    }

    /// <summary>统计区间内的字典写调用次数（setter/remove/restore），零写断言用。</summary>
    private static int CountWritesBetween(string body, int start, int end)
    {
        if (start < 0 || end <= start) return int.MaxValue;
        int writes = 0;
        for (int at = body.IndexOf("dict[", start, StringComparison.Ordinal); at >= 0 && at < end;
            at = body.IndexOf("dict[", at + 1, StringComparison.Ordinal))
        {
            writes++;
        }
        return writes;
    }
}
