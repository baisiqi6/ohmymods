using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod;

internal readonly record struct HeavyShieldBannerCandidate(Guid Receipt, long Life, int Side, float X, bool Eligible);

internal static class HeavyShieldBannerSelection
{
    // The adapter supplies only the verified current-island roster, never a scene scan.
    internal static bool TrySelect(IReadOnlyList<HeavyShieldBannerCandidate> candidates, int side, float bannerX,
        out HeavyShieldBannerCandidate chosen)
    {
        chosen = default;
        if (candidates == null || candidates.Count > 4 || (side != -1 && side != 1)
            || !float.IsFinite(bannerX)) return false;

        // Repeated receipts make career/life ownership ambiguous, including an
        // otherwise ineligible duplicate. Validate the complete bounded roster first.
        for (var i = 0; i < candidates.Count; i++)
            for (var j = 0; j < i; j++)
                if (candidates[i].Receipt == candidates[j].Receipt) return false;

        var found = false;
        var nearestDistance = double.PositiveInfinity;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!candidate.Eligible || candidate.Receipt == Guid.Empty || candidate.Life <= 0
                || candidate.Side != side || !float.IsFinite(candidate.X)) continue;

            // Widen before subtraction: two finite float positions can have a
            // distance larger than float.MaxValue without being invalid positions.
            var distance = Math.Abs((double)candidate.X - bannerX);
            if (!found || distance < nearestDistance
                || (distance == nearestDistance && candidate.Receipt.CompareTo(chosen.Receipt) < 0))
            {
                chosen = candidate;
                nearestDistance = distance;
                found = true;
            }
        }
        return found;
    }
}

internal sealed class HeavyShieldBannerBinding
{
    private long _lastActivation;
    private long _formation;
    private long _world;
    private int _side;
    private Guid _receipt;
    private long _life;

    internal bool TryBind(long activation, long formation, long world, int side, in HeavyShieldBannerCandidate chosen)
    {
        if (activation <= 0 || formation <= 0 || world <= 0 || (side != -1 && side != 1)
            || activation <= _lastActivation) return false;

        // A legal activation makes its only choice now, even if that choice is
        // empty. A late candidate or a lost escort cannot fill it retroactively.
        End();
        _lastActivation = activation;
        if (!chosen.Eligible || chosen.Receipt == Guid.Empty || chosen.Life <= 0
            || chosen.Side != side || !float.IsFinite(chosen.X)) return false;

        _formation = formation;
        _world = world;
        _side = side;
        _receipt = chosen.Receipt;
        _life = chosen.Life;
        return true;
    }

    // This observes the bound owner's current facts and ENDS the binding on a
    // mismatch. It is not a side-effect-free membership query over arbitrary
    // soldiers: the adapter must select the bound receipt/life before calling.
    internal bool IsCurrent(long activation, long formation, long world, int side, bool active, Guid receipt, long life)
    {
        if (active && _receipt != Guid.Empty && activation == _lastActivation
            && formation == _formation && world == _world && side == _side
            && receipt == _receipt && life == _life) return true;

        End();
        return false;
    }

    internal void End()
    {
        _formation = 0;
        _world = 0;
        _side = 0;
        _receipt = Guid.Empty;
        _life = 0;
        // Keep the activation watermark after ordinary loss or deactivation.
    }

    // Only the owner/world teardown may restart the activation sequence.
    internal void Reset()
    {
        End();
        _lastActivation = 0;
    }
}
