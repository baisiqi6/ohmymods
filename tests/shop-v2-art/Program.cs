using System;
using KingdomEnhancedMod;

internal static class Program
{
    private static int _checks;
    private static int _failures;

    private static void Check(bool condition, string what)
    {
        _checks++;
        if (!condition)
        {
            _failures++;
            Console.WriteLine("FAIL  " + what);
        }
    }

    private static void CheckEqual(int actual, int expected, string what) =>
        Check(actual == expected, what + " (expected " + expected + ", got " + actual + ")");

    private static void CheckEqual(string actual, string expected, string what) =>
        Check(actual == expected, what + " (expected " + expected + ", got " + actual + ")");

    private static void CheckNear(float actual, float expected, string what) =>
        Check(Math.Abs(actual - expected) <= 1e-5f, what + " (expected " + expected + ", got " + actual + ")");

    private static int Main()
    {
        LayoutShape();
        FootWorldCoordinates();
        FrameTiming();
        HeroCells();
        NicheProjection();
        CrossChecks();
        Console.WriteLine("checks=" + _checks + " failures=" + _failures);
        return _failures == 0 ? 0 : 1;
    }

    private static void FootWorldCoordinates()
    {
        // The PNG supporting edge is top-row boundary78/70, which must map to world y0.
        CheckNear((80f - 78f) - ShopV2Layout.PivotY(ShopV2Kind.Hero) * 80f, 0f, "hero actual foot edge at world zero");
        CheckNear((72f - 70f) - ShopV2Layout.PivotY(ShopV2Kind.Shield) * 72f, 0f, "shield actual foot edge at world zero");
        CheckNear((80f - 78f) - ShopV2Layout.PivotY(ShopV2Kind.Musket) * 80f, 0f, "musket actual foot edge at world zero");
    }

    private static void LayoutShape()
    {
        // 契约固定：512x400 / 512x504 / 704x240，4 列，32 PPU，地面 pivot。
        CheckEqual(ShopV2Layout.AtlasWidth(ShopV2Kind.Hero), 512, "hero atlas width");
        CheckEqual(ShopV2Layout.AtlasHeight(ShopV2Kind.Hero), 400, "hero atlas height");
        CheckEqual(ShopV2Layout.Rows(ShopV2Kind.Hero), 5, "hero rows");
        CheckEqual(ShopV2Layout.CellCount(ShopV2Kind.Hero), 20, "hero cell count");
        CheckNear(ShopV2Layout.PivotX(ShopV2Kind.Hero), 0.5f, "hero pivot x");
        CheckNear(ShopV2Layout.PivotY(ShopV2Kind.Hero), 2f / 80f, "hero pivot y");

        CheckEqual(ShopV2Layout.AtlasWidth(ShopV2Kind.Shield), 512, "shield atlas width");
        CheckEqual(ShopV2Layout.AtlasHeight(ShopV2Kind.Shield), 504, "shield atlas height");
        CheckEqual(ShopV2Layout.Rows(ShopV2Kind.Shield), 7, "shield rows");
        CheckEqual(ShopV2Layout.CellCount(ShopV2Kind.Shield), 26, "shield cell count");
        CheckNear(ShopV2Layout.PivotY(ShopV2Kind.Shield), 2f / 72f, "shield pivot y");

        CheckEqual(ShopV2Layout.AtlasWidth(ShopV2Kind.Musket), 704, "musket atlas width");
        CheckEqual(ShopV2Layout.AtlasHeight(ShopV2Kind.Musket), 240, "musket atlas height");
        CheckEqual(ShopV2Layout.CellCount(ShopV2Kind.Musket), 10, "musket cell count");
        CheckNear(ShopV2Layout.PivotX(ShopV2Kind.Musket), 64f / 176f, "musket pivot x");
        CheckNear(ShopV2Layout.PivotY(ShopV2Kind.Musket), 2f / 80f, "musket pivot y");
        CheckEqual(ShopV2Layout.PixelsPerUnit, 32, "ppu");
        CheckEqual(ShopV2Layout.Columns, 4, "columns");
        CheckEqual((int)ShopV2Kind.Hero, 0, "kind hero id");
        CheckEqual((int)ShopV2Kind.Shield, 1, "kind shield id");
        CheckEqual((int)ShopV2Kind.Musket, 2, "kind musket id");

        // 有效格范围与末尾空槽（火枪 10/11、盾 26/27、英雄无）。
        Check(ShopV2Layout.IsValidCell(ShopV2Kind.Hero, 0) && ShopV2Layout.IsValidCell(ShopV2Kind.Hero, 19), "hero cells 0..19 valid");
        Check(!ShopV2Layout.IsValidCell(ShopV2Kind.Hero, 20) && !ShopV2Layout.IsValidCell(ShopV2Kind.Hero, -1), "hero cells out of range invalid");
        Check(ShopV2Layout.IsValidCell(ShopV2Kind.Shield, 25) && !ShopV2Layout.IsValidCell(ShopV2Kind.Shield, 26), "shield cell range");
        Check(ShopV2Layout.IsValidCell(ShopV2Kind.Musket, 9) && !ShopV2Layout.IsValidCell(ShopV2Kind.Musket, 10), "musket cell range");
        Check(ShopV2Layout.IsPaddingSlot(ShopV2Kind.Musket, 10) && ShopV2Layout.IsPaddingSlot(ShopV2Kind.Musket, 11) &&
              !ShopV2Layout.IsPaddingSlot(ShopV2Kind.Musket, 9), "musket padding slots 10/11");
        Check(ShopV2Layout.IsPaddingSlot(ShopV2Kind.Shield, 26) && ShopV2Layout.IsPaddingSlot(ShopV2Kind.Shield, 27) &&
              !ShopV2Layout.IsPaddingSlot(ShopV2Kind.Hero, 19), "shield padding slots 26/27; hero none");

        // 行/列映射：格 0 是 PNG 左上，Unity 底翻由 RowOf/ColumnOf 决定。
        CheckEqual(ShopV2Layout.RowOf(0), 0, "cell0 row");
        CheckEqual(ShopV2Layout.ColumnOf(0), 0, "cell0 column");
        CheckEqual(ShopV2Layout.RowOf(5), 1, "cell5 row");
        CheckEqual(ShopV2Layout.ColumnOf(5), 1, "cell5 column");
        CheckEqual(ShopV2Layout.RowOf(19), 4, "cell19 row");
        CheckEqual(ShopV2Layout.ColumnOf(19), 3, "cell19 column");

        CheckEqual(ShopV2Layout.StateLayerCount(ShopV2Kind.Hero), 2, "hero state layers");
        CheckEqual(ShopV2Layout.StateLayerCount(ShopV2Kind.Shield), 4, "shield state layers");
        CheckEqual(ShopV2Layout.StateLayerCount(ShopV2Kind.Musket), 0, "musket state layers");
        CheckEqual(ShopV2Layout.ResourceName(ShopV2Kind.Hero), "KingdomEnhancedMod.ShopV2Hero.png", "hero resource name");
        CheckEqual(ShopV2Layout.ResourceName(ShopV2Kind.Shield), "KingdomEnhancedMod.ShopV2Shield.png", "shield resource name");
        CheckEqual(ShopV2Layout.ResourceName(ShopV2Kind.Musket), "KingdomEnhancedMod.ShopV2Musket.png", "musket resource name");
        Check(!ShopV2Layout.IsKnown((ShopV2Kind)7), "unknown kind rejected");
    }

    private static void FrameTiming()
    {
        // 帧表：8 帧，周期 3.2s，帧 0 为 1.6s 长休息。
        CheckEqual(ShopV2Frames.FrameCount, 8, "frame count");
        CheckNear(ShopV2Frames.TotalSeconds, 3.2f, "cycle seconds");
        float sum = 0f;
        for (int i = 0; i < ShopV2Frames.Durations.Length; i++) sum += ShopV2Frames.Durations[i];
        CheckNear(sum, ShopV2Frames.TotalSeconds, "durations sum to cycle");
        CheckNear(ShopV2Frames.Durations[0], 1.6f, "rest duration");
        CheckNear(ShopV2Frames.Durations[7], 0.62f, "last frame duration");

        // 精确分界：边界处进入下一帧；周期尾回绕到帧 0。
        CheckEqual(ShopV2Frames.FrameAt(0f), 0, "t=0 frame0");
        CheckEqual(ShopV2Frames.FrameAt(1.599999f), 0, "t<1.6 frame0");
        CheckEqual(ShopV2Frames.FrameAt(1.6f), 1, "t=1.6 frame1");
        CheckEqual(ShopV2Frames.FrameAt(1.78f), 2, "t=1.78 frame2");
        CheckEqual(ShopV2Frames.FrameAt(1.96f), 3, "t=1.96 frame3");
        CheckEqual(ShopV2Frames.FrameAt(2.099f), 3, "just before frame4 boundary");
        CheckEqual(ShopV2Frames.FrameAt(2.101f), 4, "just inside frame4");
        CheckEqual(ShopV2Frames.FrameAt(2.22f), 5, "t=2.22 frame5");
        CheckEqual(ShopV2Frames.FrameAt(2.40f), 6, "t=2.40 frame6");
        CheckEqual(ShopV2Frames.FrameAt(2.579f), 6, "just before frame7 boundary");
        CheckEqual(ShopV2Frames.FrameAt(2.581f), 7, "just inside frame7");
        // 精确累计分界：分界点本身进入下一帧（累计按契约 duration 顺序，与实现同一 float 语义）。
        float boundary = 0f;
        for (int frame = 0; frame < ShopV2Frames.FrameCount - 1; frame++)
        {
            boundary += ShopV2Frames.Durations[frame];
            CheckEqual(ShopV2Frames.FrameAt(boundary), frame + 1, "exact boundary after frame " + frame + " advances");
        }
        // 单调扫过整周期：帧索引随 t 单调不减、覆盖 0..7。
        int previousFrame = 0;
        for (float t = 0f; t < ShopV2Frames.TotalSeconds; t += 0.01f)
        {
            int frame = ShopV2Frames.FrameAt(t);
            Check(frame >= previousFrame, "monotone at t=" + t);
            previousFrame = frame;
        }
        CheckEqual(previousFrame, 7, "sweep ends on frame7");
        CheckEqual(ShopV2Frames.FrameAt(3.199999f), 7, "t<3.2 frame7");
        CheckEqual(ShopV2Frames.FrameAt(3.2f), 0, "t=3.2 wraps frame0");
        CheckEqual(ShopV2Frames.FrameAt(6.4f), 0, "two cycles wraps frame0");
        CheckEqual(ShopV2Frames.FrameAt(-0.01f), 7, "negative wraps to last frame");
        CheckEqual(ShopV2Frames.FrameAt(float.NaN), 0, "NaN frame0");

        // 长休息值 / 暂停值稳定同返回：调用两次同值结果一致（caller 决定暂停，helper 不读时钟）。
        CheckEqual(ShopV2Frames.FrameAt(500000.25f), ShopV2Frames.FrameAt(500000.25f), "paused value stable");
        CheckEqual(ShopV2Frames.FrameAt(1.0f), ShopV2Frames.FrameAt(1.0f), "long rest value stable");
        CheckEqual(ShopV2Frames.FrameAt(500000.25f), ShopV2Frames.FrameAt(500000.25f % 3.2f), "long value equals wrapped value");

        // 所有 8 帧均可达，且长度与 durations 一一对应。
        for (int frame = 0; frame < ShopV2Frames.FrameCount; frame++)
        {
            float mid = 0f;
            for (int i = 0; i < frame; i++) mid += ShopV2Frames.Durations[i];
            mid += ShopV2Frames.Durations[frame] * 0.5f;
            CheckEqual(ShopV2Frames.FrameAt(mid), frame, "mid of frame " + frame);
        }
    }

    private static void HeroCells()
    {
        CheckEqual(ShopV2HeroPolicy.LeftFirstCell, 10, "hero left first cell");
        CheckEqual(ShopV2HeroPolicy.RightFirstCell, 15, "hero right first cell");
        // 左 10..14 / 右 15..19，两侧独立。
        for (int state = 0; state < ShopV2HeroPolicy.StateCount; state++)
        {
            CheckEqual(ShopV2HeroPolicy.CellFor(false, state), 10 + state, "left state " + state + " cell");
            CheckEqual(ShopV2HeroPolicy.CellFor(true, state), 15 + state, "right state " + state + " cell");
        }
        // 非法 state：绝不画 available，也不假装空位（Reserved=3）。
        int[] illegal = { -1, 5, 99, int.MinValue, int.MaxValue };
        foreach (int state in illegal)
        {
            CheckEqual(ShopV2HeroPolicy.CellFor(false, state), 13, "illegal left state " + state + " -> reserved");
            CheckEqual(ShopV2HeroPolicy.CellFor(true, state), 18, "illegal right state " + state + " -> reserved");
            Check(!ShopV2HeroPolicy.IsAvailable(state), "illegal state " + state + " not available");
        }
        Check(ShopV2HeroPolicy.IsAvailable(1), "state 1 available transparent");
        Check(!ShopV2HeroPolicy.IsAvailable(0) && !ShopV2HeroPolicy.IsAvailable(2) &&
              !ShopV2HeroPolicy.IsAvailable(3) && !ShopV2HeroPolicy.IsAvailable(4), "states 0/2/3/4 not available");
        // 左右独立：同一输入不串侧。
        Check(ShopV2HeroPolicy.CellFor(false, 2) != ShopV2HeroPolicy.CellFor(true, 2), "occupancy differs per side");
    }

    private static void NicheProjection()
    {
        // (ready, unknown, mold, leftExtra, rightExtra, leftCount, rightCount) -> (n0 lowerLeft, n1 upperLeft, n2 lowerRight, n3 upperRight)
        var rows = new (string name, bool ready, bool unknown, bool mold, bool leftExtra, bool rightExtra, int left, int right, int n0, int n1, int n2, int n3)[]
        {
            ("ready=false unknown=false -> unknown cover", false, false, true, false, false, 0, 0, 3, 3, 3, 3),
            ("ready=false unknown=false with counts -> unknown cover", false, false, true, true, true, 2, 2, 3, 3, 3, 3),
            ("unknown=true beats unlocked mold -> unknown cover", true, true, true, true, true, 1, 1, 3, 3, 3, 3),
            ("unknown=true beats locked mold -> unknown cover", true, true, false, false, false, 0, 0, 3, 3, 3, 3),
            ("mold locked -> fitted gem covers", true, false, false, false, false, 0, 0, 1, 1, 1, 1),
            ("mold locked with occupancy -> covers stay", true, false, false, true, true, 2, 2, 1, 1, 1, 1),
            ("no extra no occupancy -> base open, extra locked", true, false, true, false, false, 0, 0, 0, 1, 0, 1),
            ("no extra one occupant -> base sealed", true, false, true, false, false, 1, 0, 2, 1, 0, 1),
            ("no extra count=2 illegal -> unknown both", true, false, true, false, false, 2, 0, 3, 3, 0, 1),
            ("extra bought empty -> two open per side", true, false, true, true, true, 0, 0, 0, 0, 0, 0),
            ("extra bought one each -> base sealed, extra open", true, false, true, true, true, 1, 1, 2, 0, 2, 0),
            ("extra bought full -> two sealed per side", true, false, true, true, true, 2, 2, 2, 2, 2, 2),
            ("sides independent: left full right empty", true, false, true, true, true, 2, 0, 2, 2, 0, 0),
            ("sides independent: extra only on right", true, false, true, false, true, 1, 2, 2, 1, 2, 2),
            ("negative count illegal -> unknown both", true, false, true, true, true, -1, 0, 3, 3, 0, 0),
            ("count over max illegal -> unknown both", true, false, true, true, true, 0, 3, 0, 0, 3, 3),
        };

        foreach (var row in rows)
        {
            var states = ShopV2NichePolicy.Project(row.ready, row.unknown, row.mold, row.leftExtra, row.rightExtra, row.left, row.right);
            CheckEqual(states.Length, 4, row.name + " size");
            CheckEqual((int)states[0], row.n0, row.name + " lowerLeft");
            CheckEqual((int)states[1], row.n1, row.name + " upperLeft");
            CheckEqual((int)states[2], row.n2, row.name + " lowerRight");
            CheckEqual((int)states[3], row.n3, row.name + " upperRight");
            // 任何投影都不得把不可广告状态标成 Available 而 ready/unknown 失效。
            if (!row.ready || row.unknown)
                foreach (var state in states) Check(state == ShopV2NicheState.Unknown, row.name + " unknown cover never empty");
        }

        // 侧位归属：0/1 = 左，2/3 = 右；limit 随 extra。
        Check(!ShopV2NichePolicy.IsRight(0) && !ShopV2NichePolicy.IsRight(1), "niche 0/1 left");
        Check(ShopV2NichePolicy.IsRight(2) && ShopV2NichePolicy.IsRight(3), "niche 2/3 right");
        CheckEqual(ShopV2NichePolicy.Limit(false), 1, "limit without extra");
        CheckEqual(ShopV2NichePolicy.Limit(true), 2, "limit with extra");

        // 格位：10 + niche*4 + state，覆盖 10..25。
        CheckEqual(ShopV2NichePolicy.CellFor(0, ShopV2NicheState.Available), 10, "niche0 available cell");
        CheckEqual(ShopV2NichePolicy.CellFor(1, ShopV2NicheState.Locked), 15, "niche1 locked cell");
        CheckEqual(ShopV2NichePolicy.CellFor(2, ShopV2NicheState.Occupied), 20, "niche2 occupied cell");
        CheckEqual(ShopV2NichePolicy.CellFor(3, ShopV2NicheState.Unknown), 25, "niche3 unknown cell");
        for (int niche = 0; niche < ShopV2NichePolicy.Niches; niche++)
            for (int state = 0; state < 4; state++)
            {
                int cell = ShopV2NichePolicy.CellFor(niche, (ShopV2NicheState)state);
                Check(ShopV2Layout.IsValidCell(ShopV2Kind.Shield, cell), "niche cell in range: " + cell);
            }
    }

    private static void CrossChecks()
    {
        // 层序不重叠：rear0 / front1 / merchant2..9 / states10+。
        CheckEqual(ShopV2Layout.RearCell, 0, "rear cell");
        CheckEqual(ShopV2Layout.FrontCell, 1, "front cell");
        CheckEqual(ShopV2Layout.MerchantFirstCell, 2, "merchant first cell");
        CheckEqual(ShopV2Layout.MerchantFirstCell + ShopV2Layout.MerchantFrames - 1, 9, "merchant last cell");
        CheckEqual(ShopV2Layout.MerchantFirstCell + ShopV2Frames.FrameCount - 1, 9, "frame table fits merchant cells");
        Check(ShopV2Layout.FirstSeatCell >= ShopV2Layout.MerchantFirstCell + ShopV2Layout.MerchantFrames, "states start after merchant");
        foreach (ShopV2Kind kind in new[] { ShopV2Kind.Hero, ShopV2Kind.Shield, ShopV2Kind.Musket })
        {
            Check(ShopV2Layout.MerchantFirstCell + ShopV2Layout.MerchantFrames <= ShopV2Layout.CellCount(kind), kind + " merchant cells exist");
            Check(ShopV2Layout.RootPixelX(kind) < ShopV2Layout.CellWidth(kind), kind + " pivot x inside cell");
            Check(ShopV2Layout.RootPixelY(kind) < ShopV2Layout.CellHeight(kind), kind + " pivot inside cell");
        }
        for (int state = 0; state < ShopV2HeroPolicy.StateCount; state++)
        {
            Check(ShopV2Layout.IsValidCell(ShopV2Kind.Hero, ShopV2HeroPolicy.CellFor(false, state)), "hero left cell in range");
            Check(ShopV2Layout.IsValidCell(ShopV2Kind.Hero, ShopV2HeroPolicy.CellFor(true, state)), "hero right cell in range");
        }
    }
}
