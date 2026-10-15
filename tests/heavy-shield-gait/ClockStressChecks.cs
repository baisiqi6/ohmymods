using System;
using KingdomEnhancedMod;

internal static class ClockStressChecks
{
    internal static void Run()
    {
        ZeroAllocation();
    }

    private static void ZeroAllocation()
    {
        Runner.Section("allocation");
        var clock = new HeavyShieldGaitClock();
        var sample = new HeavyShieldGaitSample { Playing = true, MovingLoop = true, Facing = 1, ParentId = 7 };
        Pump(clock, ref sample, 4000);                       // warm JIT and all code paths
        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        Pump(clock, ref sample, 20000);
        long after = GC.GetAllocatedBytesForCurrentThread();
        Runner.Check(after == before, "alloc.perFrameZero delta=" + (after - before));
    }

    private static void Pump(HeavyShieldGaitClock clock, ref HeavyShieldGaitSample sample, int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            int frame = i + 1;
            sample.Frame = frame;
            sample.UnscaledTime = frame * 0.01;
            sample.WorldX = (i % 100) * 0.00625f;
            sample.AnchorX = (i % 100) * 0.00625f - 0.00625f;
            clock.Observe(sample, HeavyShieldGaitClock.BackWalkStrideWorld);
            clock.FrameOffset(6);
            clock.FrameOffset(8);
            if ((i & 4095) == 0) clock.InvalidateBaseline();
        }
    }
}
