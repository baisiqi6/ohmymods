// HeroArcherNativePose 纯逻辑 + 原生采样桥契约测试（替身 Unity；真实 interop 由 operator 编译 + 复核）。
// 运行： C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/native-pose/Tests.csproj

using System;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class Program
{
    private static int _failed;
    private static int _passed;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { _passed++; return; }
        _failed++;
        Console.WriteLine("FAIL " + name + (detail.Length > 0 ? " :: " + detail : ""));
    }

    private static int Main()
    {
        ResolveActionMapsTheFiveDrawnStates();
        ResolveActionRejectsZeroAndForeignHashes();
        LoopPhasesTakeFractionOfAnyTurnCount();
        SingleShotsClampToLastFrame();
        PrepareWindowNormalizesClipTime();
        IllegalPhasesAreDeterministicAndBounded();
        FrameLayoutCoversAtlasExactlyOnce();
        BridgeMapsCurrentStateToFrame();
        BridgeReadsNextOnlyDuringUnfinishedTransition();
        BridgeCollapsesMissingDisabledAndThrowingAnimators();
        BridgeReportsUndrawnStatesAsUnknown();
        RepeatedSamplesAreStableForSamePhase();
        EventLogDeduplicatesPerActor();
        EventLogStopsAtGlobalCapacity();
        EventLogStaysBoundedAcrossManyActors();

        Console.WriteLine();
        Console.WriteLine(_failed == 0
            ? "ALL NATIVE-POSE TESTS PASSED (" + _passed + ")"
            : _failed + " NATIVE-POSE TEST(S) FAILED, " + _passed + " passed");
        return _failed == 0 ? 0 : 1;
    }

    private static HeroArcherNativeStateHashes Table => new HeroArcherNativeStateHashes(1001, 1002, 1003, 1004, 1005);

    private static void ResolveActionMapsTheFiveDrawnStates()
    {
        HeroArcherNativeStateHashes table = Table;
        Check("ResolveAction.stand", HeroArcherNativePose.ResolveAction(1001, in table) == HeroArcherNativeAction.Stand);
        Check("ResolveAction.walk", HeroArcherNativePose.ResolveAction(1002, in table) == HeroArcherNativeAction.Walk);
        Check("ResolveAction.run", HeroArcherNativePose.ResolveAction(1003, in table) == HeroArcherNativeAction.Run);
        Check("ResolveAction.prepare", HeroArcherNativePose.ResolveAction(1004, in table) == HeroArcherNativeAction.Prepare);
        Check("ResolveAction.shoot", HeroArcherNativePose.ResolveAction(1005, in table) == HeroArcherNativeAction.Shoot);
        Check("ResolveAction.loops", HeroArcherNativePose.IsLooping(HeroArcherNativeAction.Stand)
            && HeroArcherNativePose.IsLooping(HeroArcherNativeAction.Walk)
            && HeroArcherNativePose.IsLooping(HeroArcherNativeAction.Run)
            && !HeroArcherNativePose.IsLooping(HeroArcherNativeAction.Prepare)
            && !HeroArcherNativePose.IsLooping(HeroArcherNativeAction.Shoot));
    }

    private static void ResolveActionRejectsZeroAndForeignHashes()
    {
        HeroArcherNativeStateHashes table = Table;
        Check("ResolveAction.zero", HeroArcherNativePose.ResolveAction(0, in table) == HeroArcherNativeAction.Unknown);
        Check("ResolveAction.foreign", HeroArcherNativePose.ResolveAction(424242, in table) == HeroArcherNativeAction.Unknown);
        // 换 controller（Norse 世界）后未绘状态名不应误判成已绘状态。
        Check("ResolveAction.defend", HeroArcherNativePose.ResolveAction(1000, in table) == HeroArcherNativeAction.Unknown);
    }

    private static void LoopPhasesTakeFractionOfAnyTurnCount()
    {
        // Stand = 0..9（10 帧）：相位取小数（原生循环会累积 3.7 = 第 4 圈中途）。
        Check("loop.stand.t0", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Stand, 0f) == 0);
        Check("loop.stand.t01", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Stand, 0.1f) == 1);
        Check("loop.stand.t05", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Stand, 0.5f) == 5);
        Check("loop.stand.t0999", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Stand, 0.999f) == 9);
        Check("loop.stand.t37", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Stand, 3.7f) == 7);
        Check("loop.stand.t40", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Stand, 4f) == 0);
        Check("loop.stand.large", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Stand, 1000000.5f) == 5);
        // 负相位（时间源异常/回退）安全归一化，不越界。
        Check("loop.stand.neg", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Stand, -0.25f) == 7);
        Check("loop.stand.negWhole", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Stand, -1f) == 0);
        // 帧末边界：紧贴 1 的两侧都落在末帧/首帧，不越界。
        Check("loop.stand.edge", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Stand, 0.9999999f) == 9
            && HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Stand, -0.0000001f) == 9);
        Check("loop.walk", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Walk, 0.3f) == 11
            && HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Walk, 1.5f) == 13
            && HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Walk, 0.999f) == 15);
        Check("loop.run", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Run, 0.875f) == 21
            && HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Run, 0.1666f) == 16);
        Check("fraction.bounds", HeroArcherNativePose.Fraction(3.7f) >= 0.699f
            && HeroArcherNativePose.Fraction(3.7f) <= 0.701f
            && HeroArcherNativePose.Fraction(-0.25f) == 0.75f
            && HeroArcherNativePose.Fraction(2f) == 0f);
    }

    private static void SingleShotsClampToLastFrame()
    {
        // 无 clip 信息（clipLength/窗口默认 0）= 退化路径：Prepare 仍按原始相位 clamp；
        // 生产路径由 PrepareWindowNormalizesClipTime 覆盖。
        Check("single.prepare.start", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0f) == 22);
        Check("single.prepare.mid", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.5f) == 24);
        Check("single.prepare.early", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.24f) == 22);
        Check("single.prepare.atOne", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 1f) == 25);
        Check("single.prepare.past", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 5f) == 25);
        Check("single.prepare.neg", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, -3f) == 22);
        // Shoot = 26..30（5 帧）。Shoot 始终按整段 clip 归一（原生会播完整 clip 再退出）。
        Check("single.shoot.start", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Shoot, 0f) == 26);
        Check("single.shoot.mid", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Shoot, 0.6f) == 29);
        Check("single.shoot.atOne", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Shoot, 1f) == 30);
        Check("single.shoot.past", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Shoot, 9f) == 30);
        Check("single.shoot.ignoresWindow", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Shoot, 0.6f, 0.3f, 0.1f) == 29,
            "Shoot 不参与 Prepare 窗口归一");
    }

    /// <summary>
    /// Prepare 有效窗口（2026-09-15 live-fix）：原生只播 shootPrepTime 那么长的前缀，clip 却长得多。
    /// 全局相位归一只会停在 22 号帧（希腊 2.167s clip：原生退出时 nt≈0.11）；按 clip 时间/窗口归一才能走完拉弓。
    /// </summary>
    private static void PrepareWindowNormalizesClipTime()
    {
        const float GreekClip = 2.1666665f;

        // 希腊 prepare clip 2.167s、窗口 0.25s：clip 时间（nt × 长度）除以窗口 → 4 帧在窗口内走完。
        Check("window.greeceStart", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0f, GreekClip, 0.25f) == 22);
        Check("window.greeceQuarter", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.03f, GreekClip, 0.25f) == 23,
            "ct≈0.065 → 23");
        Check("window.greeceHalf", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.06f, GreekClip, 0.25f) == 24,
            "ct≈0.13 → 24");
        Check("window.greeceNativeExit", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.107f, GreekClip, 0.25f) == 25,
            "实测原生退出点 ct≈0.23 → 必须已到末帧 25");
        // 旧全局相位归一在同一退出点只给 22（回归对照：这正是用户看不到拉弓的算法）。
        Check("window.legacyGlobalPhaseWouldStall", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.107f) == 22,
            "无窗口信息时 0.107 → 22（对照）");
        // 窗口外钳到末帧，不越界。
        Check("window.greecePast", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.5f, GreekClip, 0.25f) == 25);
        // 基础 clip 0.5s 同样按窗口归一（ct = nt × 0.5）。
        Check("window.baseClip", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.25f, 0.5f, 0.25f) == 24
            && HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.5f, 0.5f, 0.25f) == 25,
            "基础 clip：nt 0.25 → ct 0.125 → 24；nt 0.5 → ct 0.25 → 25");
        // 循环动作不受窗口影响。
        Check("window.loopUnaffected", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Walk, 0.3f, 1.5f, 0.25f) == 11);

        // 非法输入：确定性回退原始相位，不抛、不越界。
        Check("window.illegalClipLength", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.5f, float.NaN, 0.25f) == 24
            && HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.5f, -1f, 0.25f) == 24
            && HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.5f, float.PositiveInfinity, 0.25f) == 24);
        Check("window.illegalWindow", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.5f, 2f, float.NaN) == 24
            && HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.5f, 2f, 0f) == 24
            && HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.5f, 2f, -1f) == 24);
        Check("window.overflowClipTime", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, float.MaxValue, float.MaxValue, 0.25f) == 25,
            "clip 时间溢出 → 回退原始相位 clamp（确定性 25）");
        Check("window.phaseHelper", Math.Abs(HeroArcherNativePose.PreparePhase(0.1f, 2f, 0.25f) - 0.8f) < 1e-5f
            && Math.Abs(HeroArcherNativePose.PreparePhase(0.1f, 0f, 0.25f) - 0.1f) < 1e-5f
            && HeroArcherNativePose.PreparePhase(float.NaN, 2f, 0.25f) == 0f);

        // 窗口只来自调用方（operator 桥记录的原生同一步读值）：本模块不做种子/钳制/测量。
        Check("window.noMeasurementApi",
            HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.03f, GreekClip, 0.25f) == 23
            && HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, 0.03f, GreekClip, 0.12f) == 24,
            "窗口是纯输入：0.25 → 23，0.12 → 24（同一相位）");

        // 桥：clip 长度进采样；带窗口时 Prepare 在希腊早期相位已推进到 23（不再卡 22）。
        Animator windowed = new Animator { Current = new AnimatorStateInfo(1004, 0.03f, GreekClip) };
        HeroArcherNativePoseSample sample = HeroArcherNativeSampler.Sample(windowed, 0.25f);
        Check("bridge.prepareWindowed", sample.Action == HeroArcherNativeAction.Prepare && sample.Frame == 23
            && Math.Abs(sample.ClipLength - GreekClip) < 1e-4f,
            "got frame=" + sample.Frame + " len=" + sample.ClipLength);
        // 旧调用面（不传窗口）→ 原始相位：既有 73 项语义不变。
        Animator legacy = new Animator { Current = new AnimatorStateInfo(1004, 0.4f, 0.5f) };
        Check("bridge.prepareNoWindowFallsBack", HeroArcherNativeSampler.Sample(legacy).Frame == 23);
        // clip 长度非法 → 0（诊断可见），Prepare 回退原始相位。
        Animator badLength = new Animator { Current = new AnimatorStateInfo(1004, 0.4f, float.NaN) };
        HeroArcherNativePoseSample bad = HeroArcherNativeSampler.Sample(badLength, 0.25f);
        Check("bridge.badClipLengthZero", bad.ClipLength == 0f && bad.Frame == 23,
            "len=" + bad.ClipLength + " frame=" + bad.Frame);
    }

    private static void IllegalPhasesAreDeterministicAndBounded()
    {
        // NaN/±Inf：确定性退回该动作首帧，不抛、不越界、不跳帧。
        Check("illegal.nan", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Stand, float.NaN) == 0
            && HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Walk, float.NaN) == 10
            && HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Run, float.NaN) == 16
            && HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Prepare, float.NaN) == 22
            && HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Shoot, float.NaN) == 26);
        Check("illegal.inf", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Stand, float.PositiveInfinity) == 0
            && HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Walk, float.NegativeInfinity) == 10);
        // 未知动作：-1（调用方不写 sprite、转挂起）。
        Check("illegal.unknown", HeroArcherNativePose.FrameFor(HeroArcherNativeAction.Unknown, 0.5f) == -1
            && HeroArcherNativePose.FirstFrame(HeroArcherNativeAction.Unknown) == -1
            && HeroArcherNativePose.FrameCount(HeroArcherNativeAction.Unknown) == 0);
    }

    private static void FrameLayoutCoversAtlasExactlyOnce()
    {
        HeroArcherNativeAction[] actions =
        {
            HeroArcherNativeAction.Stand, HeroArcherNativeAction.Walk, HeroArcherNativeAction.Run,
            HeroArcherNativeAction.Prepare, HeroArcherNativeAction.Shoot,
        };
        bool[] covered = new bool[HeroArcherPoseAtlas.FrameCount];
        bool ok = true;
        bool rectInside = true;
        for (int a = 0; a < actions.Length; a++)
        {
            int first = HeroArcherNativePose.FirstFrame(actions[a]);
            int count = HeroArcherNativePose.FrameCount(actions[a]);
            ok &= first >= 0 && count > 0 && first + count <= HeroArcherPoseAtlas.FrameCount;
            for (int i = 0; i < count; i++) covered[first + i] = true;
        }
        for (int i = 0; i < covered.Length; i++) ok &= covered[i];

        // 31 帧全部有归属、末格（index 31）不属于任何动作且不建 sprite。
        bool lastCellEmpty = !HeroArcherPoseAtlas.IsValidFrame(HeroArcherPoseAtlas.FrameCount);
        Check("layout.fiveStatesCover31Frames", ok && lastCellEmpty,
            "五种已绘状态必须恰好覆盖 0..30 且末格空白");

        // 每个有效帧的网格坐标与像素 Rect 必须在 384x128 内（坐标方法不越界）。
        bool cellsOk = true;
        for (int frame = 0; frame < HeroArcherPoseAtlas.FrameCount; frame++)
        {
            if (!HeroArcherPoseAtlas.FrameToCell(frame, out int column, out int row)) { cellsOk = false; break; }
            cellsOk &= column >= 0 && column < HeroArcherPoseAtlas.Columns
                && row >= 0 && row < HeroArcherPoseAtlas.Rows;
            float x = column * HeroArcherPoseAtlas.CellWidth;
            float y = (HeroArcherPoseAtlas.Rows - 1 - row) * HeroArcherPoseAtlas.CellHeight;
            rectInside &= x + HeroArcherPoseAtlas.CellWidth <= HeroArcherPoseAtlas.SheetWidth
                && y + HeroArcherPoseAtlas.CellHeight <= HeroArcherPoseAtlas.SheetHeight;
        }
        Check("layout.frameToCellInBounds", cellsOk, "每个有效帧必须有合法网格坐标");
        Check("layout.spriteRectsInsideSheet", rectInside, "sprite Rect 必须完全落在 384x128 内");
        Check("layout.lastCellRejected", !HeroArcherPoseAtlas.FrameToCell(HeroArcherPoseAtlas.FrameCount, out _, out _)
            && !HeroArcherPoseAtlas.FrameToCell(-1, out _, out _)
            && !HeroArcherPoseAtlas.FrameToCell(int.MaxValue, out _, out _),
            "末格/负帧/越界帧必须被 FrameToCell 拒绝");
        Check("layout.sheetSize", HeroArcherPoseAtlas.SheetWidth == 384 && HeroArcherPoseAtlas.SheetHeight == 128
            && HeroArcherPoseAtlas.Columns == 8 && HeroArcherPoseAtlas.Rows == 4
            && HeroArcherPoseAtlas.CellWidth == 48 && HeroArcherPoseAtlas.CellHeight == 32
            && HeroArcherPoseAtlas.FrameCount == 31,
            "新布局常量必须与 operator 断言的 8x4/48x32/384x128/31 一致");
    }

    private static HeroArcherNativePoseSample Sample(Animator animator) => HeroArcherNativeSampler.Sample(animator);

    private static void BridgeMapsCurrentStateToFrame()
    {
        Animator animator = new Animator { Current = new AnimatorStateInfo(1002, 0.6f) };
        HeroArcherNativePoseSample sample = Sample(animator);
        Check("bridge.walk", sample.Action == HeroArcherNativeAction.Walk && sample.Frame == 13
            && sample.StateHash == 1002 && Math.Abs(sample.NormalizedTime - 0.6f) < 1e-6f
            && sample.Fallback == HeroArcherNativeFallback.None && sample.Drawn,
            "got action=" + sample.Action + " frame=" + sample.Frame + " fallback=" + sample.Fallback);
        Check("bridge.baseLayer", animator.LastLayer == 0, "桥必须只读 base layer(0)，got " + animator.LastLayer);
        Check("bridge.noNextReadWithoutTransition", animator.NextReads == 0, "非转场帧不得读 next state");

        Animator standing = new Animator { Current = new AnimatorStateInfo(1001, 2.25f) };
        HeroArcherNativePoseSample stand = Sample(standing);
        Check("bridge.standSecondLoop", stand.Action == HeroArcherNativeAction.Stand && stand.Frame == 2,
            "stand 第 3 圈相位 2.25 → 帧 2，got " + stand.Frame);
    }

    private static void BridgeReadsNextOnlyDuringUnfinishedTransition()
    {
        Animator animator = new Animator
        {
            InTransition = true,
            Current = new AnimatorStateInfo(1004, 0.4f),   // Prepare 未播完
            Next = new AnimatorStateInfo(1005, 0.1f),      // Shoot
        };
        HeroArcherNativePoseSample during = Sample(animator);
        Check("bridge.transitionKeepsCurrent", during.Action == HeroArcherNativeAction.Prepare && during.Frame == 23,
            "current 未播完必须继续 current，got " + during.Action + "/" + during.Frame);
        Check("bridge.nextSkippedWhileCurrentRunning", animator.NextReads == 0,
            "current 未播完时不得读 next，got " + animator.NextReads);

        animator.Current = new AnimatorStateInfo(1004, 1.2f);   // Prepare 播完 → 固定取 next
        HeroArcherNativePoseSample after = Sample(animator);
        Check("bridge.waitsForNativeStateChange", after.Action == HeroArcherNativeAction.Prepare && after.Frame == 25,
            "等待原生完成状态转换，got " + after.Action + "/" + after.Frame);
        Check("bridge.neverPredictsNext", animator.NextReads == 0,
            "无论累计循环数都不读取 next，got " + animator.NextReads);
    }

    private static void BridgeCollapsesMissingDisabledAndThrowingAnimators()
    {
        var invalid = Sample(new Animator { Current = new AnimatorStateInfo(1002, float.NaN) });
        Check("bridge.invalidPhaseFallsBack", !invalid.Drawn && invalid.Fallback == HeroArcherNativeFallback.Unreadable);
        Check("atlas.clothLift", HeroArcherPoseAtlas.TorsoLiftPixels(17)==2 && HeroArcherPoseAtlas.TorsoLiftPixels(12)==1 && HeroArcherPoseAtlas.TorsoLiftPixels(26)==0);
        HeroArcherNativePoseSample missing = Sample(null);
        Check("bridge.nullAnimator", missing.Fallback == HeroArcherNativeFallback.NoAnimator && !missing.Drawn);

        Animator disabled = new Animator { isActiveAndEnabled = false, Current = new AnimatorStateInfo(1002, 0.5f) };
        HeroArcherNativePoseSample off = Sample(disabled);
        Check("bridge.disabled", off.Fallback == HeroArcherNativeFallback.AnimatorDisabled && !off.Drawn
            && off.Frame == -1);

        Animator broken = new Animator { ThrowOnRead = true };
        HeroArcherNativePoseSample unreadable = Sample(broken);
        Check("bridge.throwingAnimator", unreadable.Fallback == HeroArcherNativeFallback.Unreadable && !unreadable.Drawn,
            "读取异常必须收敛成 Unreadable，绝不外抛");
    }

    private static void BridgeReportsUndrawnStatesAsUnknown()
    {
        Animator ghost = new Animator { Current = new AnimatorStateInfo(1006, 0.5f) };   // "Ghost Die"
        HeroArcherNativePoseSample die = Sample(ghost);
        Check("bridge.ghostDie", die.Fallback == HeroArcherNativeFallback.UnknownState
            && die.Action == HeroArcherNativeAction.Unknown && die.Frame == -1 && !die.Drawn);

        Animator spawn = new Animator { Current = new AnimatorStateInfo(1007, 0.25f) };  // Spawn
        Check("bridge.spawn", Sample(spawn).Fallback == HeroArcherNativeFallback.UnknownState);

        Animator foreign = new Animator { Current = new AnimatorStateInfo(12345, 0.5f) }; // 换皮 controller 的其它状态
        HeroArcherNativePoseSample other = Sample(foreign);
        Check("bridge.foreignState", other.Fallback == HeroArcherNativeFallback.UnknownState
            && other.StateHash == 12345, "未绘状态必须报告自身 hash 供诊断，got " + other.StateHash);

        Animator empty = new Animator();   // controller 为空 → hash 0
        Check("bridge.emptyStateHash", Sample(empty).Fallback == HeroArcherNativeFallback.UnknownState);
    }

    private static void RepeatedSamplesAreStableForSamePhase()
    {
        // 相位只来自原生：同一相位重复采样必须给出同一帧（没有自有时钟参与，也没有事件重启动）。
        Animator animator = new Animator { Current = new AnimatorStateInfo(1001, 0.6f) };
        int first = Sample(animator).Frame;
        for (int i = 0; i < 100; i++)
        {
            if (Sample(animator).Frame != first)
            {
                Check("stable.samePhaseSameFrame", false, "第 " + i + " 次重复采样变了帧");
                return;
            }
        }
        Check("stable.samePhaseSameFrame", first == 6, "stand 0.6 → 帧 6，got " + first);
        // 相位前进才换帧；相位停住（原生暂停 Idleness）则帧不前进。
        animator.Current = new AnimatorStateInfo(1001, 0.6f);
        Check("stable.frozenPhase", Sample(animator).Frame == 6);
    }

    private static HeroArcherNativeEventKey Key(int actor, int life, int hash, bool native, bool hero,
        HeroArcherNativeFallback reason = HeroArcherNativeFallback.None)
        => new HeroArcherNativeEventKey(actor, life, hash, native, hero, reason);

    private static void EventLogDeduplicatesPerActor()
    {
        HeroArcherNativeEventLog log = new HeroArcherNativeEventLog();
        Check("events.firstAccepted", log.Accept(Key(1, 1, 1001, true, true)));
        Check("events.sameTransitionRejected", !log.Accept(Key(1, 1, 1001, true, true)));
        // 另一个 actor 的相同键不因上一个 actor 被吞掉（每个 actor 独立槽）。
        Check("events.otherActorAccepted", log.Accept(Key(2, 1, 1001, true, true)));
        Check("events.otherActorRepeatRejected", !log.Accept(Key(2, 1, 1001, true, true)));
        Check("events.actorChangedHashAccepted", log.Accept(Key(1, 1, 1002, true, true)));
        Check("events.visibilityChangeAccepted", log.Accept(Key(1, 1, 1002, true, false)));
        Check("events.fallbackChangeAccepted", log.Accept(Key(1, 1, 1002, true, false, HeroArcherNativeFallback.UnknownState)));
        Check("events.count", log.Count == 5, "count=" + log.Count);
    }

    private static void EventLogStopsAtGlobalCapacity()
    {
        HeroArcherNativeEventLog log = new HeroArcherNativeEventLog();
        int accepted = 0;
        for (int i = 0; i < HeroArcherNativeEventLog.Capacity + 50; i++)
        {
            // 每条都是新转场（hash 随 i 变化）→ 全被接受，直到上限。
            if (log.Accept(Key(1, 1, 2000 + i, true, true))) accepted++;
        }
        Check("events.capacityExact", accepted == HeroArcherNativeEventLog.Capacity,
            "accepted=" + accepted + " capacity=" + HeroArcherNativeEventLog.Capacity);
        Check("events.exhaustedFlag", log.Exhausted && log.Count == HeroArcherNativeEventLog.Capacity);
        Check("events.postCapacityRejected", !log.Accept(Key(9, 9, 777, true, true)));

        log.Reset();
        Check("events.reset", !log.Exhausted && log.Count == 0 && log.Accept(Key(1, 1, 1001, true, true)));
    }

    private static void EventLogStaysBoundedAcrossManyActors()
    {
        // 真实上限 = 每侧 1 名英雄（2 actor）：两条不变转场在 200 次采样里只记 2 行（每 actor 一条槽）。
        HeroArcherNativeEventLog two = new HeroArcherNativeEventLog();
        int twoAccepted = 0;
        for (int i = 0; i < 100; i++)
        {
            if (two.Accept(Key(1, 1, 1001, true, true))) twoAccepted++;
            if (two.Accept(Key(2, 7, 1001, true, true))) twoAccepted++;
        }
        Check("events.twoActorsDeduped", twoAccepted == 2, "accepted=" + twoAccepted + "（同 actor 相同转场只记一次）");

        // actor 频繁换（池化/换 life 抖动）：槽位固定 + 全局上限，绝不随采样数增长。
        HeroArcherNativeEventLog churn = new HeroArcherNativeEventLog();
        const int Actors = 12;
        for (int i = 0; i < Actors * 100; i++)
            churn.Accept(Key(1 + i % Actors, 1 + i / Actors, 1001, true, true));
        Check("events.churnBounded", churn.Count <= HeroArcherNativeEventLog.Capacity && churn.Exhausted,
            "count=" + churn.Count + "（1200 次采样必须停在 " + HeroArcherNativeEventLog.Capacity + " 行以内）");
    }
}
