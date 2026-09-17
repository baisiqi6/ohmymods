using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod;

internal static class MusketeerShopRules
{
    internal const int RackCapacity = 3;
    internal static float SlotX(int slot) => -0.9f + 0.9f * slot;
    internal static int FirstFreeSlot(IEnumerable<float> existingLocalX)
    {
        int mask = 0;
        foreach (float x in existingLocalX)
        {
            if (!float.IsFinite(x)) return -1;
            for (int slot = 0; slot < RackCapacity; slot++)
                if (Math.Abs(x - SlotX(slot)) < 0.34f) mask |= 1 << slot;
        }
        for (int slot = 0; slot < RackCapacity; slot++) if ((mask & (1 << slot)) == 0) return slot;
        return -1;
    }
}
