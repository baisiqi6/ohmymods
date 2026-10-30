using System;
using UnityEngine;

namespace KingdomEnhancedMod;

// One Troll.ApplyCollisionDamage call's positive block witness. The ingress stack's
// top frame wins, so a nested arrow/special/inactive frame can never mark or consume
// an outer Troll scope. The scope is reference-based; no global ledger, clock or queue.
internal sealed class HeavyShieldTrollScope : IDisposable
{
    internal readonly Troll Source;
    internal readonly IntPtr OldPause;
    internal readonly bool OldPauseKnown;
    internal HeavyShieldCombat.Impact Impact;
    internal HeavyShieldCampaignToken Campaign;
    internal bool Active;
    internal bool Blocked;
    internal bool Consumed;
    internal int BlockDirection;
    internal IntPtr DamageReturnPause { get; private set; }
    internal bool DamageReturnKnown => _damageReturnKnown && !_damageReturnFailed;
    private bool _damageReturnKnown, _damageReturnFailed;
    private bool _closed;
    private IDisposable _frame;

    internal HeavyShieldTrollScope(Troll source, IntPtr oldPause, bool oldPauseKnown)
    { Source = source; OldPause = oldPause; OldPauseKnown = oldPauseKnown; }

    internal void Bind(in HeavyShieldCombat.Impact impact, bool active, in HeavyShieldCampaignToken campaign)
    { Impact = impact; Active = active; Campaign = campaign; }

    internal void Attach(IDisposable frame) => _frame = frame;

    // Called only after HeavyShieldRuntime.TryBlock returned true in HeavyShieldCombat.TryBlock.
    // Wear is deliberately not evidence: a rate-limited block that charges no durability still marks.
    internal void MarkBlocked(float relativeX)
    {
        if (!Active || _closed || Consumed || !float.IsFinite(relativeX) || relativeX == 0f) return;
        BlockDirection = Math.Sign(relativeX);
        Blocked = true;
    }

    // ReceiveDamage's Finalizer runs even when our Prefix blocked the hit. A child
    // collision may have replaced _pauseMotion inside that chain. Only a pause created
    // after this return boundary can belong to the enclosing native collision tail.
    internal void RecordDamageReturn(bool completedNormally)
    {
        if (!Active || _closed || Consumed) return;
        if (!completedNormally || _damageReturnFailed)
        { _damageReturnFailed = true; _damageReturnKnown = false; return; }
        if (!Blocked) return;
        try
        {
            if (Source == null) throw new InvalidOperationException("missing collision source");
            var pause = Source._pauseMotion;
            DamageReturnPause = pause?.Pointer ?? IntPtr.Zero;
            _damageReturnKnown = true; // Known null is distinct from a failed observation.
        }
        catch { _damageReturnFailed = true; _damageReturnKnown = false; }
    }

    // The postfix consumes exactly once before validating anything. A repeated call, a
    // disposed scope, or an exception return therefore can never emit a second rebound.
    internal bool TryConsumeBlocked()
    {
        if (_closed || Consumed) return false;
        Consumed = true;
        return Active && Blocked;
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        Blocked = false;
        var frame = _frame;
        _frame = null;
        frame?.Dispose();
    }
}

// Reuses the native PauseMotion that ApplyCollisionDamage already created and started.
// Only its still-waiting velocityAfter vector is replaced; no coroutine, OnPush, damage,
// rigidbody or transform write is introduced.
internal static class HeavyShieldRebound
{
    private const float Strength = 1.2f;
    private static readonly float NativeDuration = 1f / 15f;

    internal static void Apply(HeavyShieldTrollScope scope, Troll troll, Damageable target)
    {
        if (scope == null || !scope.TryConsumeBlocked()) return;
        try
        {
            if (!TryPrepare(scope, troll, target, out var pause, out int away, out var force)) return;
            var scale = troll.transform.localScale;
            // Native shape is Vector2.Scale(knightBounceForce, localScale.xy). The explicit
            // 1.2 is a config multiplier, not a second multiply of the native 0.8 * velocity.
            float x = force.x * scale.x * Strength;
            float y = force.y * scale.y * Strength;
            if (!float.IsFinite(x) || x == 0f || !float.IsFinite(y)) return;
            // Keep the native signed vector. Unknown/inward configuration skips feedback.
            if (Math.Sign(x) != away) return;
            pause.velocityAfter = new Vector2(x, y);
        }
        catch { }
    }

    private static bool TryPrepare(HeavyShieldTrollScope scope, Troll troll, Damageable target,
        out Troll._PauseMotion_d__114 pause, out int away, out Vector2 force)
    {
        pause = null;
        away = 0;
        force = default;
        var impact = scope.Impact;
        if (troll == null || target == null || !scope.Active || !scope.OldPauseKnown) return false;
        if (!HeavyShieldRuntime.Playing) return false;
        if (!HeavyShieldIdentity.ValidateCampaign(scope.Campaign)) return false;
        if (!HeavyShieldIdentity.ValidateCareer(impact.Career)) return false;
        if (scope.Source == null || scope.Source.Pointer != troll.Pointer) return false;
        if (impact.Arrow || impact.Kind != DamageSource.Troll) return false;
        if (scope.Source == null || scope.Source.Pointer != troll.Pointer
            || troll.gameObject.Pointer != impact.Source.Pointer || target.Pointer != impact.Target.Pointer) return false;
        if (!HeavyShieldRuntime.TryGetCareer(target, out var career) || career != impact.Career) return false;
        if (!IsOrdinaryTroll(troll)) return false;
        if (!CombatTargetLife.TryResolve(target, out var targetLife)
            || !CombatTargetToken.Matches(targetLife, impact.TargetLife)) return false;
        var sourceDamage = troll.GetComponent<Damageable>();
        if (sourceDamage == null || !CombatTargetLife.TryResolve(sourceDamage, out var sourceLife)
            || !CombatTargetToken.Matches(sourceLife, impact.SourceLife)) return false;

        float sourceX = troll.transform.position.x;
        float targetX = target.transform.position.x;
        float relativeX = sourceX - targetX;
        if (!float.IsFinite(sourceX) || !float.IsFinite(targetX)
            || !float.IsFinite(relativeX) || relativeX == 0f) return false;
        int relative = Math.Sign(relativeX);
        if (relative != scope.BlockDirection) return false;
        away = relative;

        var current = troll._pauseMotion;
        if (!scope.DamageReturnKnown || current == null || current.Pointer == scope.OldPause
            || current.Pointer == scope.DamageReturnPause) return false;
        pause = current.TryCast<Troll._PauseMotion_d__114>();
        if (pause == null) return false;
        var owner = pause.__4__this;
        if (owner == null || owner.Pointer != troll.Pointer) return false;
        if (pause.__1__state != 1) return false;
        float duration = pause.duration;
        if (!float.IsFinite(duration) || duration != NativeDuration) return false;
        var after = pause.velocityAfter;
        if (!float.IsFinite(after.x) || !float.IsFinite(after.y)) return false;

        force = troll.knightBounceForce;
        return float.IsFinite(force.x) && float.IsFinite(force.y);
    }

    private static bool IsOrdinaryTroll(Troll troll)
    {
        if (!HeavyShieldRuntime.IsOrdinaryEnemy(troll.gameObject, false)) return false;
        var enemy = troll.gameObject.GetComponent<Enemy>();
        return enemy != null && (enemy.Type == EnemyType.TrollWeak
            || enemy.Type == EnemyType.TrollMedium || enemy.Type == EnemyType.ToughTroll);
    }
}
