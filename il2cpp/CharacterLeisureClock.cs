using System;

namespace KingdomEnhancedMod;

/// <summary>Per-visual, scaled-time leisure overlay. No native animation or movement writes.</summary>
internal sealed class CharacterLeisureClock
{
    internal const float StableStandSeconds = .75f;
    internal const float ActionSeconds = 2.4f;
    internal const int FramesPerAction = 8;

    private readonly uint _seed;
    private uint _cycle;
    private float _cooldown;
    private float _stand;
    private float _elapsed;
    private bool _active;
    private int _nextAction;
    private int _action;

    internal CharacterLeisureClock(int actorKey)
    {
        _seed = unchecked((uint)actorKey) ^ 0x9e3779b9u;
        _nextAction = (int)(_seed & 1u);
        RenewCooldown();
    }

    internal bool Active => _active;
    internal float Cooldown => _cooldown;

    /// <summary>Returns 0..15 or -1. False means higher-priority native state won this frame.</summary>
    internal int Tick(float scaledDelta, bool permitted)
    {
        float dt = float.IsFinite(scaledDelta) && scaledDelta > 0f
            ? Math.Min(scaledDelta, .1f) : 0f; // no resume catch-up
        if (!permitted)
        {
            if (_active) Cancel();
            _stand = 0f;
            _cooldown = Math.Max(0f, _cooldown - dt); // daily activity can consume cooldown
            return -1;
        }

        if (_active)
        {
            _elapsed += dt;
            if (_elapsed >= ActionSeconds)
            {
                Cancel();
                return -1;
            }
            int offset = Math.Min(FramesPerAction - 1, (int)(_elapsed * FramesPerAction / ActionSeconds));
            return _action * FramesPerAction + offset;
        }

        _cooldown = Math.Max(0f, _cooldown - dt);
        _stand += dt;
        if (_cooldown > 0f || _stand < StableStandSeconds) return -1;
        _active = true;
        _elapsed = 0f;
        _stand = 0f;
        _action = _nextAction;
        _nextAction = 1 - _nextAction;
        return _action * FramesPerAction;
    }

    /// <summary>Real attack/target events cancel immediately, including same-frame pre-Sync events.</summary>
    internal void Cancel()
    {
        _active = false;
        _elapsed = 0f;
        _stand = 0f;
        RenewCooldown();
    }

    private void RenewCooldown()
    {
        uint v = _seed + ++_cycle * 0x85ebca6bu;
        v ^= v >> 16;
        v *= 0x7feb352du;
        v ^= v >> 15;
        _cooldown = 10f + v % 801u / 100f;
    }
}
