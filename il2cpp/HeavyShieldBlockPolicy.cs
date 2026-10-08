using System;

namespace KingdomEnhancedMod;

// One instance belongs to one shield soldier's current life. The game bridge owns
// identity, native damage, knockback and the deferred Peasant conversion.
internal sealed class HeavyShieldBlockPolicy
{
    internal const int InitialDurability = 4;
    internal const int BashDamage = 1;
    internal const int MaxBashTargets = 3;
    internal const float BashCooldownSeconds = 10f;
    internal const int MaxDemoteAttempts = 1;

    internal enum HitResult : byte
    {
        PassThrough,
        Blocked,
        ShieldBroken
    }

    internal enum LifeState : byte
    {
        Inactive,
        Guarding,
        PendingDemote,
        Demoting,
        Demoted,
        Dead
    }

    internal readonly struct Hit
    {
        internal readonly bool IsDirect;
        internal readonly bool HasSource;
        internal readonly float SourceX;
        internal readonly float DefenderX;
        internal readonly int Facing; // -1 or +1; any other value is unknown.

        internal Hit(bool isDirect, bool hasSource, float sourceX, float defenderX, int facing)
        {
            IsDirect = isDirect;
            HasSource = hasSource;
            SourceX = sourceX;
            DefenderX = defenderX;
            Facing = facing;
        }
    }

    internal LifeState State { get; private set; }
    internal int Durability { get; private set; }
    internal bool BashActive => _bashActive;
    internal bool RetirementUnknown { get; private set; }
    internal bool PendingBreak => State == LifeState.PendingDemote || State == LifeState.Demoting;

    private bool _bashActive;
    private bool _hasBashed;
    private float _nextBashAt;
    private long _target0;
    private long _target1;
    private long _target2;
    private int _targetCount;
    private ulong _nextDemoteLease;
    private ulong _activeDemoteLease;
    private int _demoteAttempts;
    private bool _impactClaimed;

    // Call on unload, disable, load/restore and pool reuse before binding a new life.
    internal void Reset()
    {
        State = LifeState.Inactive;
        Durability = 0;
        _hasBashed = false;
        _nextBashAt = 0f;
        _activeDemoteLease = 0;
        _demoteAttempts = 0;
        RetirementUnknown = false;
        ClearBash();
    }

    internal bool Activate()
    {
        if (State != LifeState.Inactive) return false;
        State = LifeState.Guarding;
        Durability = InitialDurability;
        return true;
    }

    // A repeated attachment never calls Activate. Loaded wear is the authority.
    internal bool Restore(int durability, bool pendingBreak, bool retirementUnknown)
    {
        if (State != LifeState.Inactive || durability < 0 || durability > InitialDurability
            || (durability == 0) != pendingBreak || (retirementUnknown && !pendingBreak)) return false;
        Durability = durability;
        RetirementUnknown = retirementUnknown;
        State = retirementUnknown ? LifeState.Demoting : pendingBreak ? LifeState.PendingDemote : LifeState.Guarding;
        _demoteAttempts = retirementUnknown ? MaxDemoteAttempts : 0;
        return true;
    }

    internal HitResult EvaluateHit(in Hit hit)
    {
        if (State != LifeState.Guarding) return HitResult.PassThrough;
        if (!hit.IsDirect || !hit.HasSource || (hit.Facing != -1 && hit.Facing != 1)
            || float.IsNaN(hit.SourceX) || float.IsInfinity(hit.SourceX)
            || float.IsNaN(hit.DefenderX) || float.IsInfinity(hit.DefenderX))
            return HitResult.PassThrough;

        float relativeX = hit.SourceX - hit.DefenderX;
        // A zero separation does not provide evidence of a frontal strike.
        if (relativeX == 0f || Math.Sign(relativeX) != hit.Facing)
            return HitResult.PassThrough;

        --Durability;
        if (Durability > 0) return HitResult.Blocked;
        State = LifeState.PendingDemote;
        ClearBash();
        return HitResult.ShieldBroken;
    }

    // Begin only after the current damage chain and after checking native death.
    // Keep the lease while native conversion is uncertain; do not retry a call
    // that may already have created a Peasant.
    internal bool TryBeginDemote(out ulong lease)
    {
        lease = 0;
        if (State != LifeState.PendingDemote || _demoteAttempts >= MaxDemoteAttempts) return false;
        ++_demoteAttempts;
        if (++_nextDemoteLease == 0) ++_nextDemoteLease;
        _activeDemoteLease = _nextDemoteLease;
        lease = _activeDemoteLease;
        State = LifeState.Demoting;
        RetirementUnknown = true; // record before the native call can partially succeed
        return true;
    }

    // Call only after verifying that the native replacement succeeded.
    internal bool ConfirmDemote(ulong lease)
    {
        if (State != LifeState.Demoting || lease == 0 || lease != _activeDemoteLease) return false;
        _activeDemoteLease = 0;
        State = LifeState.Demoted;
        RetirementUnknown = false;
        return true;
    }

    // Call only after verifying that native conversion made no replacement.
    // An exception alone is not proof: it may have happened after creation.
    internal bool AbortDemoteUnchanged(ulong lease)
    {
        if (State != LifeState.Demoting || lease == 0 || lease != _activeDemoteLease) return false;
        _activeDemoteLease = 0;
        // Native Demote is at-most-once even when the caller saw no replacement.
        // Keep the uncertain retirement occupied for identity reconciliation.
        State = LifeState.Demoting;
        RetirementUnknown = true;
        return true;
    }

    internal void MarkDead()
    {
        State = LifeState.Dead;
        Durability = 0;
        _activeDemoteLease = 0;
        RetirementUnknown = false;
        ClearBash();
    }

    // nowSeconds must be game time (paused time excluded); the bridge only invokes
    // target registration at the animation's impact frame.
    internal bool TryBeginBash(float nowSeconds)
    {
        if (State != LifeState.Guarding || _bashActive || float.IsNaN(nowSeconds)
            || float.IsInfinity(nowSeconds) || (_hasBashed && nowSeconds < _nextBashAt))
            return false;
        ClearBash();
        _bashActive = true;
        _hasBashed = true;
        _nextBashAt = nowSeconds + BashCooldownSeconds;
        _impactClaimed = false;
        return true;
    }

    internal bool TryClaimBashImpact()
    {
        if (!_bashActive || _impactClaimed || State != LifeState.Guarding) return false;
        _impactClaimed = true;
        return true;
    }

    // targetIdentity must identify the current target life, not a reused instance ID.
    // The caller submits BashDamage and knockback only when this returns true.
    internal bool TryRegisterBashTarget(long targetIdentity, bool isOrdinaryEnemy)
    {
        if (!_bashActive || State != LifeState.Guarding || !isOrdinaryEnemy
            || targetIdentity == 0 || _targetCount >= MaxBashTargets
            || targetIdentity == _target0 || targetIdentity == _target1 || targetIdentity == _target2)
            return false;
        switch (_targetCount++)
        {
            case 0: _target0 = targetIdentity; break;
            case 1: _target1 = targetIdentity; break;
            default: _target2 = targetIdentity; break;
        }
        return true;
    }

    internal void EndBash() => ClearBash();

    private void ClearBash()
    {
        _bashActive = false;
        _targetCount = 0;
        _target0 = 0;
        _target1 = 0;
        _target2 = 0;
    }
}
