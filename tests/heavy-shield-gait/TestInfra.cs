using System;
using KingdomEnhancedMod;

internal static class Runner
{
    internal static int Failures;

    internal static void Section(string name) => Console.WriteLine("## " + name);

    internal static void Check(bool ok, string label)
    {
        if (!ok) Failures++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + label);
    }

    internal static void Near(double actual, double expected, double tolerance, string label)
        => Check(Math.Abs(actual - expected) <= tolerance,
            label + " actual=" + actual.ToString("R") + " expected=" + expected.ToString("R"));

    internal static int Finish()
    {
        Console.WriteLine(Failures == 0 ? "RESULT PASS" : "RESULT FAIL failures=" + Failures);
        return Failures == 0 ? 0 : 2;
    }
}

internal static class Fx
{
    internal const double Walk = HeavyShieldGaitClock.BackWalkStrideWorld;
    internal const double Run = HeavyShieldGaitClock.BackRunStrideWorld;
    internal const double Advance = HeavyShieldGaitClock.AdvanceStrideWorld;
    internal const float HalfWalk = 0.328125f;     // 0.5 walk stride, exact dyadic
    internal const float QuarterWalk = 0.1640625f; // 0.25 walk stride, exact dyadic
    internal const float TenthWalk = 0.065625f;    // ~0.1 walk stride

    internal static HeavyShieldGaitSample S(int frame, double time, float worldX, float anchorX,
        int facing = 1, int parent = 7, bool playing = true, bool loop = true,
        float worldY = 0f, float anchorY = 0f,
        float localX = 0f, float localY = 0f, float localZ = 0f)
        => new HeavyShieldGaitSample
        {
            Frame = frame, UnscaledTime = time, Facing = facing, ParentId = parent,
            Playing = playing, MovingLoop = loop,
            WorldX = worldX, WorldY = worldY, AnchorX = anchorX, AnchorY = anchorY,
            LocalX = localX, LocalY = localY, LocalZ = localZ,
        };
}
