namespace KingdomEnhancedMod
{
    // Existing persistence/network cases inject arbitrary choices at the allocation boundary.
    // Actual durable order is tested by tests/hermes-headwear-cycle against its real source.
    internal static class HermesHeadwearCycle
    {
        internal static int Calls;
        internal static bool Succeed = true;
        internal static bool TryAssign(int chancePercent, out int choice)
        {
            choice = -1;
            // Arbitrary allocation boundary for legacy receipt tests; real quota has its own linked suite.
            if (chancePercent < 100 && PatchDivine_HermesHeadwear.SampleOverride(0, 100) >= chancePercent)
                return true;
            Calls++;
            if (!Succeed) return false;
            choice = PatchDivine_HermesHeadwear.SampleOverride(0, 44);
            return true;
        }
    }
}
