using System;
using KingdomEnhancedMod;

/// <summary>Console checks for the pure HeavyShieldLeapBash sampler. No NuGet, no Unity:
/// the production module is compiled into this project and exercised directly.</summary>
internal static class Program
{
    private const float Lift = 1f / 12f;
    private const float Impact = 3f / 12f;
    private const float Land = 5f / 12f;
    private const float Reach = 0.18f;
    private const float Peak = 0.12f;

    private static int _failures;

    private static int Main()
    {
        CheckConstants();
        CheckZeroInputs();
        CheckBoundaries();
        CheckImpactPeak();
        CheckFrameGrid();
        CheckMirror();
        CheckArcShape();
        CheckPurity();

        Console.WriteLine(_failures == 0 ? "ALL PASS" : _failures + " FAILURES");
        return _failures == 0 ? 0 : 1;
    }

    private static void CheckConstants()
    {
        Near(HeavyShieldLeapBash.LiftAtSeconds, Lift, "LiftAtSeconds == 1/12");
        Near(HeavyShieldLeapBash.ImpactAtSeconds, Impact, "ImpactAtSeconds == 3/12");
        Near(HeavyShieldLeapBash.LandAtSeconds, Land, "LandAtSeconds == 5/12");
        Near(HeavyShieldLeapBash.PeakHeight, Peak, "PeakHeight == 0.12");
        Near(HeavyShieldLeapBash.ForwardReach, Reach, "ForwardReach == 0.18");
        Check(HeavyShieldLeapBash.ImpactAtSeconds == (Lift + Land) * 0.5f, "impact is the lift/land midpoint");
        Check(HeavyShieldLeapBash.ImpactAtSeconds * 12f == 3f, "impact aligns with frame 3 of the 12 fps bash");
        Check(HeavyShieldLeapBash.LandAtSeconds * 12f == 5f, "land aligns with frame 5 (3 recovery frames remain of 8)");
    }

    private static void CheckZeroInputs()
    {
        bool allZero = true;
        for (int i = 0; i <= 700; i++)
        {
            float t = i * 0.001f;
            if (!IsZero(HeavyShieldLeapBash.Sample(false, t, 1)) || !IsZero(HeavyShieldLeapBash.Sample(false, t, -1)))
                allZero = false;
        }
        Check(allZero, "bashing=false -> (0,0) across 0..0.7 s");

        bool invalidZero = true;
        int[] badFacing = { 0, 2, -2, int.MinValue, int.MaxValue };
        float[] badElapsed = { float.NaN, float.PositiveInfinity, float.NegativeInfinity };
        foreach (int facing in badFacing)
        {
            foreach (float t in new[] { 0.1f, Impact, 0.4f })
                if (!IsZero(HeavyShieldLeapBash.Sample(true, t, facing))) invalidZero = false;
        }
        foreach (float t in badElapsed)
        {
            if (!IsZero(HeavyShieldLeapBash.Sample(true, t, 1)) || !IsZero(HeavyShieldLeapBash.Sample(true, t, -1)))
                invalidZero = false;
        }
        if (!IsZero(HeavyShieldLeapBash.Sample(true, float.PositiveInfinity, 0))) invalidZero = false;
        Check(invalidZero, "invalid facing / non-finite elapsed -> (0,0)");
    }

    private static void CheckBoundaries()
    {
        Check(IsZero(HeavyShieldLeapBash.Sample(true, Lift, 1)), "elapsed == LiftAt -> (0,0)");
        Check(IsZero(HeavyShieldLeapBash.Sample(true, Lift - 0.001f, 1)), "elapsed < LiftAt -> (0,0)");
        Check(IsZero(HeavyShieldLeapBash.Sample(true, 0f, 1)), "elapsed == 0 -> (0,0)");
        Check(IsZero(HeavyShieldLeapBash.Sample(true, -1f, 1)), "negative elapsed -> (0,0)");
        Check(IsZero(HeavyShieldLeapBash.Sample(true, Land, 1)), "elapsed == LandAt -> (0,0)");
        Check(IsZero(HeavyShieldLeapBash.Sample(true, Land + 0.001f, 1)), "elapsed > LandAt -> (0,0)");
        Check(IsZero(HeavyShieldLeapBash.Sample(true, 8f / 12f, 1)), "bash sequence end (8 frames) -> (0,0)");
        Check(IsZero(HeavyShieldLeapBash.Sample(true, 0.667f, 1)), "full 0.667 s bash duration -> (0,0)");
        Check(IsZero(HeavyShieldLeapBash.Sample(true, 100f, 1)), "late elapsed -> (0,0)");

        // Continuity at the lift/land seams: a sample one microsecond inside stays near zero.
        Near(HeavyShieldLeapBash.Sample(true, Lift + 1e-6f, 1).Y, 0f, 1e-4f, "lift seam is continuous");
        Near(HeavyShieldLeapBash.Sample(true, Land - 1e-6f, 1).Y, 0f, 1e-4f, "land seam is continuous");
    }

    private static void CheckImpactPeak()
    {
        var right = HeavyShieldLeapBash.Sample(true, Impact, 1);
        Near(right.X, Reach, 1e-5f, "X peaks at +ForwardReach on impact (facing +1)");
        Near(right.Y, Peak, 1e-5f, "Y peaks at PeakHeight on impact");

        var left = HeavyShieldLeapBash.Sample(true, Impact, -1);
        Near(left.X, -Reach, 1e-5f, "X peaks at -ForwardReach on impact (facing -1)");
        Near(left.Y, Peak, 1e-5f, "Y peak is facing independent");
    }

    private static void CheckFrameGrid()
    {
        bool tail = true;
        for (int frame = 0; frame <= 8; frame++)
        {
            float t = frame / 12f;
            if (frame <= 1 || frame >= 5)
                if (!IsZero(HeavyShieldLeapBash.Sample(true, t, 1))) tail = false;
        }
        Check(tail, "frames 0-1 (before lift) and 5-8 (land + recovery) are (0,0)");

        var rise = HeavyShieldLeapBash.Sample(true, 2f / 12f, 1);
        var fall = HeavyShieldLeapBash.Sample(true, 4f / 12f, 1);
        Near(rise.Y, Peak * 0.75f, 1e-5f, "frame 2 is a quarter arc (0.75 H)");
        Near(fall.Y, Peak * 0.75f, 1e-5f, "frame 4 mirrors frame 2");
        Near(rise.X, fall.X, 1e-5f, "frame 2/4 forward reach is symmetric");
        Check(HeavyShieldLeapBash.Sample(true, 3f / 12f, 1).Y > rise.Y, "peak sits strictly above frame 2/4");
    }

    private static void CheckMirror()
    {
        bool mirror = true;
        for (int i = 1; i < 5000 && mirror; i++)
        {
            float t = Lift + (Land - Lift) * (i / 5000f);
            var right = HeavyShieldLeapBash.Sample(true, t, 1);
            var left = HeavyShieldLeapBash.Sample(true, t, -1);
            if (right.Y != left.Y || right.X != -left.X) mirror = false;
        }
        Check(mirror, "facing mirrors X exactly and leaves Y identical");
    }

    private static void CheckArcShape()
    {
        const int steps = 4000;
        bool bounds = true, rising = true, falling = true, continuous = true, positive = true;
        float prev = 0f;

        for (int i = 0; i <= steps; i++)
        {
            float t = Lift + (Impact - Lift) * (i / (float)steps);
            var o = HeavyShieldLeapBash.Sample(true, t, 1);
            if (o.Y < 0f || o.Y > Peak + 1e-6f || o.X < 0f || o.X > Reach + 1e-6f) bounds = false;
            if (i > 0 && o.Y < prev - 1e-9f) rising = false;
            if (i > 0 && Math.Abs(o.Y - prev) > 1e-3f) continuous = false;
            if (i > 0 && i < steps && o.Y <= 0f) positive = false;
            prev = o.Y;
        }
        Check(rising, "arc rises monotonically from lift to impact");
        Check(continuous, "rise is continuous (no step larger than 1e-3 H per sample)");
        Check(positive, "arc is positive strictly inside the rise");

        prev = Peak * 2f;
        for (int i = 0; i <= steps; i++)
        {
            float t = Impact + (Land - Impact) * (i / (float)steps);
            var o = HeavyShieldLeapBash.Sample(true, t, 1);
            if (o.Y < 0f || o.Y > Peak + 1e-6f || o.X < 0f || o.X > Reach + 1e-6f) bounds = false;
            if (i > 0 && o.Y > prev + 1e-9f) falling = false;
            if (i > 0 && Math.Abs(o.Y - prev) > 1e-3f) continuous = false;
            if (i > 0 && i < steps && o.Y <= 0f) positive = false;
            prev = o.Y;
        }
        Check(falling, "arc falls monotonically from impact to land");
        Check(bounds, "every in-window sample stays within reach/height bounds");
        Check(positive, "arc is positive strictly inside the fall");

        // Wide sweep: nothing outside the window is ever non-zero, no matter the facing.
        bool outsideZero = true;
        for (int i = -100; i <= 900; i++)
        {
            float t = i * 0.001f;
            if (t <= Lift || t >= Land) continue;
            if (IsZero(HeavyShieldLeapBash.Sample(true, t, 1))) outsideZero = false;
        }
        Check(outsideZero, "all in-window samples are non-zero (arc never collapses early)");
    }

    private static void CheckPurity()
    {
        var before = HeavyShieldLeapBash.Sample(true, Impact, 1);
        for (int i = 0; i < 256; i++)
        {
            float t = (i % 97) * 0.004f;
            HeavyShieldLeapBash.Sample(i % 2 == 0, t, i % 3 == 0 ? -1 : 1);
        }
        var after = HeavyShieldLeapBash.Sample(true, Impact, 1);
        Check(before.X == after.X && before.Y == after.Y, "sampler is stateless (interleaved calls do not change results)");
    }

    private static bool IsZero(HeavyShieldLeapBash.Offset o) => o.X == 0f && o.Y == 0f;

    private static void Check(bool ok, string name)
    {
        if (!ok) _failures++;
        Console.WriteLine((ok ? "pass " : "FAIL ") + name);
    }

    private static void Near(float actual, float expected, string name) => Near(actual, expected, 0f, name);

    private static void Near(float actual, float expected, float tolerance, string name)
        => Check(Math.Abs(actual - expected) <= tolerance,
            name + " [actual=" + actual.ToString("R") + " expected=" + expected.ToString("R") +
            (tolerance > 0f ? " tol=" + tolerance.ToString("R") : "") + "]");
}
