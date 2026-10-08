namespace KingdomEnhancedMod;

/// <summary>Pure display-offset sampling for the heavy-shield bash leap. The career owner
/// passes the pose machine's Bash action clock; nothing here owns time, state, physics,
/// targets or damage. Non-bash actions and out-of-range samples are exactly (0, 0).</summary>
internal static class HeavyShieldLeapBash
{
    // The Bash clip is 8 frames at 12 fps (~0.667 s): lift starts at frame 1,
    // the existing impact claim fires at frame 3, landing completes at frame 5.
    // Frames 5..7 are the recovery tail and must stay at zero.
    internal const float LiftAtSeconds = 1f / 12f;
    internal const float ImpactAtSeconds = 3f / 12f;
    internal const float LandAtSeconds = 5f / 12f;
    internal const float PeakHeight = 0.12f;
    internal const float ForwardReach = 0.18f;

    internal readonly struct Offset
    {
        internal readonly float X;
        internal readonly float Y;

        internal Offset(float x, float y) { X = x; Y = y; }
    }

    /// <summary>World-space display offset for the exclusive KEM_HeavyShieldBody root.
    /// facing is the carrier's ±1 side sign: X is forward reach, Y is height. The arc
    /// peaks exactly at ImpactAtSeconds and is continuous at both lift and land. The
    /// caller supplies the current frame's anchor; offsets never accumulate.</summary>
    internal static Offset Sample(bool bashing, float elapsed, int facing)
    {
        if (!bashing || (facing != 1 && facing != -1) || !float.IsFinite(elapsed)
            || elapsed <= LiftAtSeconds || elapsed >= LandAtSeconds)
            return default;

        float p = (elapsed - LiftAtSeconds) / (LandAtSeconds - LiftAtSeconds);
        float arc = 4f * p * (1f - p);
        return new Offset(facing * ForwardReach * arc, PeakHeight * arc);
    }
}
