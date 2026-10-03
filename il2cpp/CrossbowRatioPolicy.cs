using System;

namespace KingdomEnhancedMod;

/// <summary>Pure four-slot selection shared by new bow promotions and load recompute.</summary>
internal static class CrossbowRatioPolicy
{
    internal const float Default = .25f;

    internal static float Normalize(float value)
    {
        if (value == .5f || value == .75f || value == 1f) return value;
        return Default; // includes NaN, infinity and unsupported config values
    }

    internal static int Percent(float value) => (int)(Normalize(value) * 100f);

    /// <summary>Zero-based eligible index: 25% = slot 4; 50% = 2/4; 75% = 2/3/4.</summary>
    internal static bool Selected(int eligibleIndex, float configuredRatio)
    {
        int slot = eligibleIndex & 3; // also preserves the four-slot cycle if the long-lived counter wraps
        float ratio = Normalize(configuredRatio);
        return slot == 3 || (ratio >= .5f && slot == 1)
            || (ratio >= .75f && slot == 2) || ratio == 1f;
    }

    /// <summary>Quantize a true UI input to one of the four approved values.</summary>
    internal static float SnapSlider(float raw)
    {
        if (!float.IsFinite(raw)) return Default;
        int quarters = (int)Math.Floor(raw * 4f + .5f);
        if (quarters < 1) quarters = 1;
        if (quarters > 4) quarters = 4;
        return quarters * .25f;
    }
}
