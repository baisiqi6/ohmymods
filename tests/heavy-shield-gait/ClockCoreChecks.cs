using System;
using KingdomEnhancedMod;

internal static class ClockCoreChecks
{
    internal static void Run()
    {
        SeedOnly();
        ForwardHalfStride();
        FacingSign();
        FpsEquivalence();
        ZeroMovement();
        VariableSpeed();
        RoundTrip();
        StrideSwitch();
        ParentChange();
        FacingChange();
        NegativeProjected();
        PauseResume();
        DuplicateFrame();
        FrameRegression();
        FrameJumpForward();
    }

    private static void SeedOnly()
    {
        Runner.Section("seed");
        var c = new HeavyShieldGaitClock();
        Runner.Check(c.Phase == 0d && !c.HasBaseline, "seed.startsEmpty");
        c.Observe(Fx.S(1, 1.0, 5f, 5f, localX: 2.5f), Fx.Walk);
        Runner.Check(c.HasBaseline, "seed.baselineStored");
        Runner.Check(c.Phase == 0d, "seed.noPhaseFromSeed");
        Runner.Check(c.PreviousLocalX == 2.5f, "seed.previousLocalExposed");
    }

    private static void ForwardHalfStride()
    {
        Runner.Section("forward");
        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(1, 1.0, 4f, 4f), Fx.Walk);
        c.Observe(Fx.S(2, 1.1, 4f + Fx.HalfWalk, 4f), Fx.Walk);
        Runner.Near(c.Phase, 0.5d, 1e-12, "forward.halfStrideHalfPhase");
        Runner.Check(c.Phase >= 0d && c.Phase < 1d, "forward.phaseBounded");
    }

    private static void FacingSign()
    {
        Runner.Section("facing-sign");
        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(1, 1.0, 0f, 0f, facing: -1), Fx.Walk);
        c.Observe(Fx.S(2, 1.1, -Fx.QuarterWalk, 0f, facing: -1), Fx.Walk);
        Runner.Near(c.Phase, 0.25d, 1e-12, "facing.backwardFacingAdvances");
        c.Observe(Fx.S(3, 1.2, Fx.QuarterWalk, -Fx.QuarterWalk, facing: -1), Fx.Walk);
        Runner.Near(c.Phase, 0.75d, 1e-12, "facing.reverseWrapsBelowZero");
        Runner.Check(c.Phase >= 0d && c.Phase < 1d, "facing.phaseBounded");
    }

    private static void FpsEquivalence()
    {
        Runner.Section("fps-equivalence");
        int[] counts = { 3, 6, 12 };                 // 30/60/120 fps style subdivision of one path
        double[] steps = { 1.0 / 30.0, 1.0 / 60.0, 1.0 / 120.0 };
        double[] phases = new double[3];
        for (int k = 0; k < 3; k++)
        {
            var c = new HeavyShieldGaitClock();
            double t = 1.0;
            float x = -2f, previous = -2f;
            c.Observe(Fx.S(0, t, x, x), Fx.Walk);
            float step = Fx.HalfWalk / counts[k];
            for (int i = 1; i <= counts[k]; i++)
            {
                previous = x;
                x += step;
                t += steps[k];
                c.Observe(Fx.S(i, t, x, previous), Fx.Walk);
            }
            phases[k] = c.Phase;
            Runner.Check(c.FrameOffset(5) == 2, "fps.split" + counts[k] + ".offset5");
            Runner.Check(c.FrameOffset(7) == 3, "fps.split" + counts[k] + ".offset7");
        }
        Runner.Near(phases[0], 0.5d, 1e-9, "fps.equivalent30");
        Runner.Near(phases[1], 0.5d, 1e-9, "fps.equivalent60");
        Runner.Near(phases[2], 0.5d, 1e-9, "fps.equivalent120");
    }

    private static void ZeroMovement()
    {
        Runner.Section("zero-movement");
        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(0, 1.0, 3.5f, 3.5f), Fx.Walk);
        for (int i = 1; i <= 30; i++)
            c.Observe(Fx.S(i, 1.0 + i * 0.02, 3.5f, 3.5f), Fx.Walk);
        Runner.Check(c.Phase == 0d, "zero.phaseStaysZero");
        Runner.Check(c.HasBaseline, "zero.baselineKept");
        Runner.Check(c.FrameOffset(6) == 0, "zero.offset6");
    }

    private static void VariableSpeed()
    {
        Runner.Section("variable-speed");
        var c = new HeavyShieldGaitClock();
        float x = 10f;
        c.Observe(Fx.S(0, 1.0, x, x), Fx.Walk);
        float[] deltas = { 0.109375f, 0.0546875f, -0.1640625f, 0.21875f };
        int frame = 1;
        double t = 1.0;
        foreach (float d in deltas)
        {
            float previous = x;
            x += d;
            t += 0.03;
            c.Observe(Fx.S(frame++, t, x, previous), Fx.Walk);
        }
        Runner.Near(c.Phase, 0.21875d / 0.65625d, 1e-9, "variable.totalDistanceWins");
    }

    private static void RoundTrip()
    {
        Runner.Section("roundtrip");
        var c = new HeavyShieldGaitClock();
        float x = 6f;
        c.Observe(Fx.S(0, 1.0, x, x), Fx.Walk);
        float previous = x;
        x += Fx.HalfWalk;
        c.Observe(Fx.S(1, 1.1, x, previous), Fx.Walk);
        Runner.Near(c.Phase, 0.5d, 1e-12, "roundtrip.forwardHalf");
        previous = x;
        x -= Fx.HalfWalk;
        c.Observe(Fx.S(2, 1.2, x, previous), Fx.Walk);
        Runner.Near(c.Phase, 0d, 1e-12, "roundtrip.returnsToStart");
    }

    private static void StrideSwitch()
    {
        Runner.Section("stride-switch");
        var c = new HeavyShieldGaitClock();
        float x = 0f;
        c.Observe(Fx.S(0, 1.0, x, x), Fx.Walk);
        float previous = x;
        x += Fx.TenthWalk;
        c.Observe(Fx.S(1, 1.1, x, previous), Fx.Walk);
        previous = x;
        x += 0.105f;                                  // 0.1 run stride
        c.Observe(Fx.S(2, 1.2, x, previous), Fx.Run);
        previous = x;
        x += 0.0525f;                                 // 0.1 advance stride
        c.Observe(Fx.S(3, 1.3, x, previous), Fx.Advance);
        Runner.Near(c.Phase, 0.3d, 1e-6, "stride.perCallStrideRespected");
    }

    private static void ParentChange()
    {
        Runner.Section("parent-change");
        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(0, 1.0, 0f, 0f, parent: 7), Fx.Walk);
        c.Observe(Fx.S(1, 1.1, Fx.TenthWalk, 0f, parent: 8), Fx.Walk);
        Runner.Check(c.Phase == 0d, "parent.changeDropsSpan");
        Runner.Check(c.HasBaseline, "parent.primedAtChange");
        c.Observe(Fx.S(2, 1.2, 2f * Fx.TenthWalk, Fx.TenthWalk, parent: 8), Fx.Walk);
        Runner.Near(c.Phase, 0.1d, 1e-6, "parent.nextSpanCounts");
    }

    private static void FacingChange()
    {
        Runner.Section("facing-change");
        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(0, 1.0, 0f, 0f, facing: 1), Fx.Walk);
        c.Observe(Fx.S(1, 1.1, 3f * Fx.TenthWalk, 0f, facing: -1), Fx.Walk);
        Runner.Check(c.Phase == 0d, "facing.changeDropsSpan");
        Runner.Check(c.HasBaseline, "facing.primedAtChange");
        c.Observe(Fx.S(2, 1.2, Fx.TenthWalk, 3f * Fx.TenthWalk, facing: -1), Fx.Walk);
        Runner.Near(c.Phase, 0.2d, 1e-6, "facing.newDirectionCounts");
    }

    private static void NegativeProjected()
    {
        Runner.Section("negative-projected");
        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(0, 1.0, -1024.5f, -1024.5f, localX: -7.25f, localY: 3.5f, localZ: -1f), Fx.Walk);
        Runner.Check(c.PreviousLocalX == -7.25f && c.PreviousLocalZ == -1f, "negative.localsKept");
        c.Observe(Fx.S(1, 1.1, -1024.625f, -1024.5f), Fx.Walk);
        Runner.Near(c.Phase, 1.0d - 0.125d / 0.65625d, 1e-12, "negative.signedWrap");
        Runner.Check(c.Phase >= 0d && c.Phase < 1d, "negative.phaseBounded");

        var nonUnit = new HeavyShieldGaitClock();
        nonUnit.Observe(Fx.S(0, 1.0, -37.5f, -37.5f), Fx.Walk);
        nonUnit.Observe(Fx.S(1, 1.1, -37.25f, -37.5f), Fx.Walk);
        Runner.Near(nonUnit.Phase, 0.25d / 0.65625d, 1e-9, "negative.quarterStepCounts");
    }

    private static void PauseResume()
    {
        Runner.Section("pause-resume");
        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(0, 1.0, 0f, 0f, localX: 0.5f), Fx.Walk);
        c.Observe(Fx.S(1, 1.1, 0.9f, 0.9f, playing: false, localX: 0.7f), Fx.Walk);
        Runner.Check(!c.HasBaseline, "pause.invalidatesBaseline");
        Runner.Check(c.Phase == 0d, "pause.phaseKept");
        Runner.Check(float.IsFinite(c.PreviousLocalX), "pause.noPoisonedLocal");
        c.Observe(Fx.S(2, 3.0, 0.9f, 0.9f), Fx.Walk);
        Runner.Check(c.HasBaseline, "resume.reseeds");
        Runner.Check(c.Phase == 0d, "resume.noExternalJump");
        c.Observe(Fx.S(3, 3.1, 0.9f + Fx.TenthWalk, 0.9f), Fx.Walk);
        Runner.Near(c.Phase, 0.1d, 1e-6, "resume.loopAdvances");
    }

    private static void DuplicateFrame()
    {
        Runner.Section("duplicate-frame");
        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(0, 1.0, 0f, 0f, localX: 0.1f), Fx.Walk);
        c.Observe(Fx.S(1, 1.1, Fx.TenthWalk, 0f), Fx.Walk);
        Runner.Near(c.Phase, 0.1d, 1e-6, "duplicate.firstSpanCounted");
        c.Observe(Fx.S(1, 1.1, Fx.HalfWalk, 0f, localX: 0.9f, localY: -0.25f, localZ: 1.75f), Fx.Walk);
        Runner.Near(c.Phase, 0.1d, 1e-6, "duplicate.sameFrameNoAdvance");
        Runner.Check(c.PreviousLocalX == 0.9f && c.PreviousLocalY == -0.25f && c.PreviousLocalZ == 1.75f,
            "duplicate.consumesLatestLocal");
        c.Observe(Fx.S(2, 1.2, Fx.HalfWalk + Fx.TenthWalk, Fx.HalfWalk), Fx.Walk);
        Runner.Near(c.Phase, 0.2d, 1e-6, "duplicate.nextSpanFromLatestBaseline");
    }

    private static void FrameRegression()
    {
        Runner.Section("frame-regression");
        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(10, 1.0, 0f, 0f), Fx.Walk);
        c.Observe(Fx.S(12, 1.1, Fx.TenthWalk, 0f), Fx.Walk);
        Runner.Near(c.Phase, 0.1d, 1e-6, "regression.pre");
        c.Observe(Fx.S(11, 1.2, 2f * Fx.TenthWalk, 0f), Fx.Walk);
        Runner.Near(c.Phase, 0.1d, 1e-6, "regression.dropsSpan");
        Runner.Check(c.HasBaseline, "regression.primed");
        c.Observe(Fx.S(12, 1.3, 3f * Fx.TenthWalk, 2f * Fx.TenthWalk), Fx.Walk);
        Runner.Near(c.Phase, 0.2d, 1e-6, "regression.nextSpanCounts");
    }

    private static void FrameJumpForward()
    {
        Runner.Section("frame-jump");
        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(10, 1.0, 0f, 0f), Fx.Walk);
        c.Observe(Fx.S(1010, 1.1, Fx.TenthWalk, 0f), Fx.Walk);
        Runner.Near(c.Phase, 0.1d, 1e-6, "jump.nonConsecutiveFramesAccepted");
    }
}
