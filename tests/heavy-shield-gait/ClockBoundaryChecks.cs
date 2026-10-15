using System;
using KingdomEnhancedMod;

internal static class ClockBoundaryChecks
{
    private delegate void SampleMutation(ref HeavyShieldGaitSample sample);

    internal static void Run()
    {
        UnscaledGap();
        WorldStepX();
        RelativeStepX();
        RelativeStepY();
        InvalidNumbers();
        InvalidStride();
        NegativeZero();
    }

    private static void UnscaledGap()
    {
        Runner.Section("gap");
        var ok = new HeavyShieldGaitClock();
        ok.Observe(Fx.S(0, 2.0, 0f, 0f), Fx.Walk);
        ok.Observe(Fx.S(1, 2.25, Fx.TenthWalk, 0f), Fx.Walk);
        Runner.Near(ok.Phase, 0.1d, 1e-6, "gap.quarterAccepted");

        var tooLate = new HeavyShieldGaitClock();
        tooLate.Observe(Fx.S(0, 2.0, 0f, 0f), Fx.Walk);
        tooLate.Observe(Fx.S(1, 2.2500001, Fx.TenthWalk, 0f), Fx.Walk);
        Runner.Check(tooLate.Phase == 0d, "gap.overQuarterDropped");
        Runner.Check(tooLate.HasBaseline, "gap.overQuarterPrimed");

        var frozen = new HeavyShieldGaitClock();
        frozen.Observe(Fx.S(0, 2.0, 0f, 0f), Fx.Walk);
        frozen.Observe(Fx.S(1, 2.0, Fx.TenthWalk, 0f), Fx.Walk);
        Runner.Check(frozen.Phase == 0d, "gap.zeroDropped");

        var backwards = new HeavyShieldGaitClock();
        backwards.Observe(Fx.S(0, 2.0, 0f, 0f), Fx.Walk);
        backwards.Observe(Fx.S(1, 1.9, Fx.TenthWalk, 0f), Fx.Walk);
        Runner.Check(backwards.Phase == 0d, "gap.negativeDropped");
    }

    private static void WorldStepX()
    {
        Runner.Section("world-dx");
        var atLimit = new HeavyShieldGaitClock();
        atLimit.Observe(Fx.S(0, 1.0, 0f, 0f), Fx.Walk);
        atLimit.Observe(Fx.S(1, 1.1, 1f, 0.5f), Fx.Walk);          // world dx = 1 accepted
        Runner.Near(atLimit.Phase, 0.5d / 0.65625d, 1e-12, "worlddx.oneAccepted");

        var over = new HeavyShieldGaitClock();
        over.Observe(Fx.S(0, 1.0, 0f, 0f), Fx.Walk);
        over.Observe(Fx.S(1, 1.1, 1.0000001f, 1.0000001f), Fx.Walk); // world dx > 1, relative 0
        Runner.Check(over.Phase == 0d, "worlddx.overOneDropped");
        Runner.Check(over.HasBaseline, "worlddx.overOnePrimed");
    }

    private static void RelativeStepX()
    {
        Runner.Section("relative-dx");
        var atLimit = new HeavyShieldGaitClock();
        atLimit.Observe(Fx.S(0, 1.0, 0f, 0f), Fx.Walk);
        atLimit.Observe(Fx.S(1, 1.1, 0.5f, -0.5f), Fx.Walk);        // relative 1.0, world 0.5
        Runner.Near(atLimit.Phase, 1d / 0.65625d - 1d, 1e-12, "reldx.oneAccepted");

        var over = new HeavyShieldGaitClock();
        over.Observe(Fx.S(0, 1.0, 0f, 0f), Fx.Walk);
        over.Observe(Fx.S(1, 1.1, 0.9f, -0.1500001f), Fx.Walk);     // relative > 1, world < 1
        Runner.Check(over.Phase == 0d, "reldx.overOneDropped");
        Runner.Check(over.HasBaseline, "reldx.overOnePrimed");
    }

    private static void RelativeStepY()
    {
        Runner.Section("relative-dy");
        Runner.Near(ProbeRelativeY(0.25f, 0f), 0.1d, 1e-6, "reldy.plusQuarterAccepted");
        Runner.Near(ProbeRelativeY(-0.25f, 0f), 0.1d, 1e-6, "reldy.minusQuarterAccepted");
        Runner.Check(ProbeRelativeY(0.2501f, 0f) == 0d, "reldy.plusOverQuarterDropped");
        Runner.Check(ProbeRelativeY(-0.2501f, 0f) == 0d, "reldy.minusOverQuarterDropped");
    }

    private static double ProbeRelativeY(float worldY, float anchorY)
    {
        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(0, 1.0, 0f, 0f), Fx.Walk);
        c.Observe(Fx.S(1, 1.1, Fx.TenthWalk, 0f, worldY: worldY, anchorY: anchorY), Fx.Walk);
        return c.Phase;
    }

    private static void InvalidNumbers()
    {
        Runner.Section("invalid-numbers");
        Bad((ref HeavyShieldGaitSample s) => s.WorldX = float.NaN, "nan.worldX");
        Bad((ref HeavyShieldGaitSample s) => s.WorldY = float.PositiveInfinity, "inf.worldY");
        Bad((ref HeavyShieldGaitSample s) => s.AnchorX = float.NegativeInfinity, "negInf.anchorX");
        Bad((ref HeavyShieldGaitSample s) => s.AnchorY = float.NaN, "nan.anchorY");
        Bad((ref HeavyShieldGaitSample s) => s.LocalX = float.NaN, "nan.localX");
        Bad((ref HeavyShieldGaitSample s) => s.LocalY = float.PositiveInfinity, "inf.localY");
        Bad((ref HeavyShieldGaitSample s) => s.LocalZ = float.NegativeInfinity, "negInf.localZ");
        Bad((ref HeavyShieldGaitSample s) => s.UnscaledTime = double.NaN, "nan.time");

        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(0, 1.0, 0f, 0f, localX: 1.5f), Fx.Walk);
        var poisoned = Fx.S(1, 1.1, Fx.TenthWalk, 0f, localX: float.NaN);
        c.Observe(poisoned, Fx.Walk);
        Runner.Check(float.IsFinite(c.PreviousLocalX), "nan.previousLocalStaysFinite");
    }

    private static void Bad(SampleMutation mutate, string label)
    {
        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(0, 1.0, 0f, 0f, localX: 3.5f), Fx.Walk);
        var sample = Fx.S(1, 1.1, Fx.TenthWalk, 0f);
        mutate(ref sample);
        c.Observe(sample, Fx.Walk);
        Runner.Check(!c.HasBaseline, label + ".baselineDropped");
        Runner.Check(c.Phase == 0d, label + ".phaseKept");
        Runner.Check(float.IsFinite(c.PreviousLocalX), label + ".noNaNStored");
    }

    private static void InvalidStride()
    {
        Runner.Section("invalid-stride");
        Strike(double.NaN, "stride.nan");
        Strike(double.PositiveInfinity, "stride.positiveInfinity");
        Strike(double.NegativeInfinity, "stride.negativeInfinity");
        Strike(0d, "stride.zero");
        Strike(-0d, "stride.negativeZero");
        Strike(-0.5d, "stride.negative");
    }

    private static void Strike(double stride, string label)
    {
        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(0, 1.0, 0f, 0f), Fx.Walk);
        c.Observe(Fx.S(1, 1.1, Fx.TenthWalk, 0f), stride);
        Runner.Check(!c.HasBaseline, label + ".baselineDropped");
        Runner.Check(c.Phase == 0d, label + ".phaseKept");
    }

    private static void NegativeZero()
    {
        Runner.Section("negative-zero");
        var c = new HeavyShieldGaitClock();
        c.Observe(Fx.S(0, 1.0, 0f, 0f), Fx.Walk);
        c.Observe(Fx.S(1, 1.1, -0f, 0f), Fx.Walk);
        Runner.Check(c.Phase == 0d && !double.IsNegative(c.Phase), "negativeZero.staysPositiveZero");
        c.Observe(Fx.S(2, 1.2, Fx.TenthWalk, -0f), Fx.Walk);
        Runner.Near(c.Phase, 0.1d, 1e-6, "negativeZero.laterProgressCounts");
    }
}
