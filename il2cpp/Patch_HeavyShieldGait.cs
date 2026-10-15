// Distance-driven frame clock for the three heavy-shield locomotion loops. Pure logic:
// no Unity, Harmony, registry or scene state; one clock instance per pose machine.
// The owner samples the carrier transform each frame and feeds the sample in; this file
// only turns signed world displacement into a canonical loop phase and a frame index.
//
// Visual cadence references at 320/7 detail pixels per world unit:
// BackWalk 30 px/cycle = 0.65625, BackRun 48 px/cycle = 1.05, Advance 24 px/cycle = 0.525.
// The selected-B locomotion art is preserved. These values tune playback cadence;
// they do not claim measured source strides or exact planted-foot locking.
// Native Run/Advance calibration remains separate from the source-linked replay.
using System;

namespace KingdomEnhancedMod;

/// <summary>One read-only carrier motion sample. AnchorX/Y is the current parent's projection
/// of the previous sample's local position (the owner's job); with no baseline the owner passes
/// the current world position instead.</summary>
internal struct HeavyShieldGaitSample
{
    internal int Frame;
    internal int ParentId;
    internal int Facing;
    internal double UnscaledTime;
    internal float LocalX;
    internal float LocalY;
    internal float LocalZ;
    internal float WorldX;
    internal float WorldY;
    internal float AnchorX;
    internal float AnchorY;
    internal bool Playing;
    internal bool MovingLoop;
}

/// <summary>Phase advances with signed carrier displacement relative to the display facing:
/// one stride of world distance is one loop, reverse motion reads the loop backwards.
/// Thresholds are presentation-only; an implausible span is dropped whole, never clamped,
/// partially counted, or made up from time later.</summary>
internal sealed class HeavyShieldGaitClock
{
    internal const double BackWalkStrideWorld = 0.65625d;  // 30 detail px/cycle * 7/320
    internal const double BackRunStrideWorld = 1.05d;      // 48 detail px/cycle * 7/320
    internal const double AdvanceStrideWorld = 0.525d;     // 24 detail px/cycle * 7/320

    private const double MaxUnscaledGap = 0.25d;
    private const float MaxWorldStepX = 1f;
    private const float MaxRelativeStepX = 1f;
    private const float MaxRelativeStepY = 0.25f;

    private double _phase;
    private bool _hasBaseline;
    private int _frame;
    private int _parentId;
    private int _facing;
    private double _unscaledTime;
    private float _localX;
    private float _localY;
    private float _localZ;
    private float _worldX;

    /// <summary>Canonical loop phase in [0,1); negative displacement wraps below zero.</summary>
    internal double Phase => _phase;

    /// <summary>True while a validated sample is stored as the span origin.</summary>
    internal bool HasBaseline => _hasBaseline;

    /// <summary>Last consumed local position; the owner projects it through the current parent.</summary>
    internal float PreviousLocalX => _localX;
    internal float PreviousLocalY => _localY;
    internal float PreviousLocalZ => _localZ;

    /// <summary>Drop the baseline, keep the phase (pause, parent/facing handover, event fences).</summary>
    internal void InvalidateBaseline()
    {
        _hasBaseline = false;
    }

    /// <summary>Rebuild from canonical phase zero: no borrowed motion, no stale span.</summary>
    internal void ResetPhase()
    {
        _phase = 0d;
        _hasBaseline = false;
    }

    /// <summary>Consume one sample. Phase advances only across two validated frames; every other
    /// path rebuilds the baseline so no span is partially counted or replayed.</summary>
    internal void Observe(in HeavyShieldGaitSample sample, double strideWorld)
    {
        if (!IsValid(in sample) || !(strideWorld > 0d) || !double.IsFinite(strideWorld))
        {
            _hasBaseline = false; // paused, non-loop or invalid: keep phase, never fall back to time
            return;
        }

        if (!_hasBaseline)
        {
            Store(in sample);
            _hasBaseline = true; // first sample, loop reentry and revalidation only seed
            return;
        }

        if (sample.Frame == _frame)
        {
            Store(in sample); // duplicate frame: consume the latest position, never advance
            return;
        }

        bool spanAccepted =
            sample.Frame > _frame &&
            sample.ParentId == _parentId &&
            sample.Facing == _facing &&
            sample.UnscaledTime > _unscaledTime &&
            sample.UnscaledTime - _unscaledTime <= MaxUnscaledGap &&
            Math.Abs(sample.WorldX - _worldX) <= MaxWorldStepX &&
            Math.Abs(sample.WorldX - sample.AnchorX) <= MaxRelativeStepX &&
            Math.Abs(sample.WorldY - sample.AnchorY) <= MaxRelativeStepY;

        if (spanAccepted)
        {
            double delta = (sample.WorldX - sample.AnchorX) * sample.Facing / strideWorld;
            double next = _phase + delta;
            if (double.IsFinite(delta) && double.IsFinite(next))
                _phase = next - Math.Floor(next); // bounded modulo: [0,1) for any finite input
        }

        Store(in sample); // accepted, dropped or replayed: the current sample becomes the origin
    }

    /// <summary>Frame index inside a sequence of <paramref name="frameCount"/> frames, or -1
    /// when the count is unusable.</summary>
    internal int FrameOffset(int frameCount)
    {
        if (frameCount <= 0)
            return -1;
        int offset = (int)Math.Floor(_phase * frameCount);
        if (offset < 0)
            return 0;
        if (offset >= frameCount)
            return frameCount - 1;
        return offset;
    }

    private static bool IsValid(in HeavyShieldGaitSample sample)
    {
        if (!sample.Playing || !sample.MovingLoop)
            return false;
        if (sample.Facing != 1 && sample.Facing != -1)
            return false;
        if (!double.IsFinite(sample.UnscaledTime))
            return false;
        if (!float.IsFinite(sample.LocalX) || !float.IsFinite(sample.LocalY) || !float.IsFinite(sample.LocalZ))
            return false;
        if (!float.IsFinite(sample.WorldX) || !float.IsFinite(sample.WorldY))
            return false;
        if (!float.IsFinite(sample.AnchorX) || !float.IsFinite(sample.AnchorY))
            return false;
        return true;
    }

    private void Store(in HeavyShieldGaitSample sample)
    {
        _frame = sample.Frame;
        _parentId = sample.ParentId;
        _facing = sample.Facing;
        _unscaledTime = sample.UnscaledTime;
        _localX = sample.LocalX;
        _localY = sample.LocalY;
        _localZ = sample.LocalZ;
        _worldX = sample.WorldX;
    }
}
