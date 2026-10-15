using System;
using KingdomEnhancedMod;

internal static class ClockPhaseChecks
{
    internal static void Run()
    {
        FrameCount();
        WrapOffsets();
        TinyProgress();
        ExtremeStride();
        InvalidateAndReset();
    }

    private static void FrameCount()
    {
        Runner.Section("frame-count");
        var c = new HeavyShieldGaitClock();
        Runner.Check(c.FrameOffset(0) == -1, "count.zeroMinusOne");
        Runner.Check(c.FrameOffset(-5) == -1, "count.negativeMinusOne");
        c.Observe(Fx.S(0, 1.0, 0f, 0f), Fx.Walk);
        Runner.Check(c.FrameOffset(0) == -1, "count.zeroAfterSeed");
    }

    private static void WrapOffsets()
    {
        Runner.Section("wrap-offsets");
        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(0, 1.0, 0f, 0f), Fx.Walk);
        c.Observe(Fx.S(1, 1.1, 0.196875f, 0f), Fx.Walk);            // ~0.3 walk stride
        Runner.Near(c.Phase, 0.3d, 1e-6, "wrap.phase30");
        Runner.Check(c.FrameOffset(6) == 1, "wrap.offset6");
        Runner.Check(c.FrameOffset(8) == 2, "wrap.offset8");

        var high = new HeavyShieldGaitClock();
        high.Observe(Fx.S(0, 1.0, 0f, 0f), Fx.Walk);
        high.Observe(Fx.S(1, 1.1, 0.9999999f, 0f), 1d);
        Runner.Check(high.Phase >= 0d && high.Phase < 1d, "wrap.highPhaseBounded");
        Runner.Check(high.FrameOffset(6) == 5, "wrap.highOffset6");
        Runner.Check(high.FrameOffset(8) == 7, "wrap.highOffset8");

        var half = new HeavyShieldGaitClock();
        half.Observe(Fx.S(0, 1.0, 0f, 0f), Fx.Walk);
        half.Observe(Fx.S(1, 1.1, Fx.HalfWalk, 0f), Fx.Walk);
        Runner.Check(half.FrameOffset(int.MaxValue) == 1073741823, "wrap.hugeCountInBounds");
    }

    private static void TinyProgress()
    {
        Runner.Section("tiny-progress");
        var c = new HeavyShieldGaitClock();
        float x = 0f, previous = 0f;
        double t = 0d;
        c.Observe(Fx.S(0, t, x, x), Fx.Walk);
        for (int i = 1; i <= 240; i++)
        {
            previous = x;
            x += 1f / 1024f;
            t += 0.01;
            c.Observe(Fx.S(i, t, x, previous), Fx.Walk);
        }
        Runner.Near(c.Phase, 240d / 1024d / 0.65625d, 1e-9, "tiny.accumulatesWithoutZeroing");
        Runner.Check(c.FrameOffset(6) == 2, "tiny.offset6Crosses");
    }

    private static void ExtremeStride()
    {
        Runner.Section("extreme-stride");
        var denormal = new HeavyShieldGaitClock();
        denormal.Observe(Fx.S(0, 1.0, 0f, 0f), Fx.Walk);
        denormal.Observe(Fx.S(1, 1.1, Fx.TenthWalk, 0f), 5e-324d);  // delta overflows to infinity
        Runner.Check(double.IsFinite(denormal.Phase) && denormal.Phase == 0d, "denormalStride.noOverflowPhase");
        Runner.Check(denormal.HasBaseline, "denormalStride.primesCurrent");

        var tiny = new HeavyShieldGaitClock();
        tiny.Observe(Fx.S(0, 1.0, 0f, 0f), Fx.Walk);
        float x = 0f, previous = 0f;
        double t = 1.0;
        for (int i = 1; i <= 5000; i++)
        {
            previous = x;
            x += 0.0000005f;
            t += 0.001;
            tiny.Observe(Fx.S(i, t, x, previous), 1e-9d);
        }
        Runner.Check(double.IsFinite(tiny.Phase), "tinyStride.phaseFinite");
        Runner.Check(tiny.Phase >= 0d && tiny.Phase < 1d, "tinyStride.phaseBounded");
        Runner.Check(tiny.FrameOffset(int.MaxValue) >= 0, "tinyStride.hugeCountBounded");
    }

    private static void InvalidateAndReset()
    {
        Runner.Section("invalidate-reset");
        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(0, 1.0, 0f, 0f), Fx.Walk);
        c.Observe(Fx.S(1, 1.1, Fx.TenthWalk, 0f), Fx.Walk);
        Runner.Near(c.Phase, 0.1d, 1e-6, "trigger.beforePhase");
        c.InvalidateBaseline();                                      // event boundary (trigger)
        c.Observe(Fx.S(2, 1.2, 0.328125f, 0.328125f), Fx.Walk);      // event span only seeds
        Runner.Near(c.Phase, 0.1d, 1e-6, "trigger.eventSpanNotCounted");
        c.Observe(Fx.S(3, 1.3, 0.328125f + Fx.TenthWalk, 0.328125f), Fx.Walk);
        Runner.Near(c.Phase, 0.2d, 1e-6, "trigger.loopResumes");

        c.InvalidateBaseline();
        c.ResetPhase();                                              // event cancelled: fence to zero
        Runner.Check(c.Phase == 0d && !c.HasBaseline, "cancel.phaseCleared");
        c.Observe(Fx.S(4, 1.4, 5f, 5f), Fx.Walk);
        Runner.Check(c.Phase == 0d && c.HasBaseline, "cancel.observesAsSeed");
        c.Observe(Fx.S(5, 1.5, 5f + Fx.TenthWalk, 5f), Fx.Walk);
        Runner.Near(c.Phase, 0.1d, 1e-6, "cancel.advancesFromZero");

        c.InvalidateBaseline();
        c.InvalidateBaseline();
        Runner.Check(!c.HasBaseline, "invalidate.idempotent");
        Runner.Near(c.Phase, 0.1d, 1e-6, "invalidate.keepsPhase");
    }
}
