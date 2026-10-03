using System;
#if !HEAVY_SHIELD_COMBAT_CORE_ONLY
using System.Collections.Generic;
using UnityEngine;
#endif

namespace KingdomEnhancedMod;

// The top frame always wins, including disabled and overflow frames. A token is
// idempotent so Harmony finalizers can safely close after either normal or exceptional return.
internal sealed class HeavyShieldIngressStack<T>
{
    internal const int Capacity = 32;
    private readonly T[] _values = new T[Capacity];
    private readonly bool[] _active = new bool[Capacity];
    private int _depth;
    private bool _corrupt;
    internal int Depth => _depth;
    internal sealed class Scope : IDisposable
    {
        private HeavyShieldIngressStack<T> _owner;
        private readonly int _depth;
        internal Scope(HeavyShieldIngressStack<T> owner, int depth) { _owner = owner; _depth = depth; }
        public void Dispose() { var owner = _owner; _owner = null; owner?.Close(_depth); }
    }
    internal Scope Push(T value, bool active)
    {
        int depth = ++_depth;
        if (depth <= Capacity) { _values[depth - 1] = value; _active[depth - 1] = active; }
        return new Scope(this, depth);
    }
    internal bool TryPeek(out T value)
    {
        value = default;
        if (_corrupt || _depth <= 0 || _depth > Capacity || !_active[_depth - 1]) return false;
        value = _values[_depth - 1];
        return true;
    }
    private void Close(int depth)
    {
        if (depth != _depth) { _corrupt = true; return; }
        if (_depth <= Capacity) { _values[_depth - 1] = default; _active[_depth - 1] = false; }
        if (--_depth == 0) _corrupt = false;
    }
}

internal static class HeavyShieldDirection
{
    internal static bool ArrowFront(float velocityX, int facing)
        => float.IsFinite(velocityX) && velocityX != 0f && (facing == -1 || facing == 1)
            && Math.Sign(velocityX) == -facing;
}

#if !HEAVY_SHIELD_COMBAT_CORE_ONLY
internal static class HeavyShieldCombat
{
    private sealed class FlightScope : IDisposable
    {
        private IDisposable _flight, _mask;
        internal FlightScope(IDisposable flight, IDisposable mask) { _flight = flight; _mask = mask; }
        public void Dispose() { _mask?.Dispose(); _mask = null; _flight?.Dispose(); _flight = null; }
    }
    internal sealed class DamageScope : IDisposable
    {
        private bool _closed;
        internal DamageScope() { ++_damageDepth; }
        public void Dispose() { if (!_closed) { _closed = true; --_damageDepth; } }
    }
    internal readonly struct Impact
    {
        internal readonly Damageable Target;
        internal readonly GameObject Source;
        internal readonly DamageSource Kind;
        internal readonly CombatTargetToken TargetLife, SourceLife;
        internal readonly HeavyShieldCareerHandle Career;
        internal readonly float VelocityX;
        internal readonly bool Arrow;
        internal Impact(Damageable target, GameObject source, DamageSource kind,
            CombatTargetToken targetLife, CombatTargetToken sourceLife,
            HeavyShieldCareerHandle career, bool arrow, float velocityX)
        { Target = target; Source = source; Kind = kind; TargetLife = targetLife; SourceLife = sourceLife;
          Career = career; Arrow = arrow; VelocityX = velocityX; }
    }
    internal readonly struct Flight
    {
        internal readonly IntPtr Arrow;
        internal readonly CombatTargetToken Life;
        internal readonly float Vx;
        internal Flight(IntPtr arrow, CombatTargetToken life, float vx) { Arrow = arrow; Life = life; Vx = vx; }
    }
    [ThreadStatic] private static HeavyShieldIngressStack<Impact> _impacts;
    [ThreadStatic] private static HeavyShieldIngressStack<Flight> _flights;
    [ThreadStatic] private static int _damageDepth;
    private static readonly Dictionary<int, Impact> TrollIntents = new();
    private static long _intentWorld, _intentGeneration;
    private static string _intentCampaign;
    private static HeavyShieldIngressStack<Impact> Impacts => _impacts ??= new();
    private static HeavyShieldIngressStack<Flight> Flights => _flights ??= new();
    internal static bool InNativeDamageStack => _damageDepth != 0 || Impacts.Depth != 0 || Flights.Depth != 0;
    internal static DamageScope EnterDamage() => new();
    internal static IDisposable EnterTrollIntent(Troll troll, Damageable target)
    { ObserveTrollIntent(troll, target); return Impacts.Push(default, false); }

    internal static void ObserveTrollIntent(Troll troll, Damageable target)
    {
        try
        {
            int id = troll != null ? troll.gameObject.GetInstanceID() : 0;
            if (id == 0) return;
            TrollIntents.Remove(id);
            if (!HeavyShieldRuntime.Playing || !ResetIntentContext()) return;
            if (TrollIntents.Count >= 64 || !TryImpact(target, troll.gameObject, troll.damageSource, false, 0, out var impact)) return;
            TrollIntents[id] = impact;
        }
        catch { }
    }
    internal static IDisposable EnterTroll(Troll troll, Damageable target)
    {
        Impact value = default;
        bool active = false;
        try
        {
            int id = troll.gameObject.GetInstanceID();
            active = HeavyShieldRuntime.Playing && ResetIntentContext() && TrollIntents.TryGetValue(id, out var intent)
                && TryImpact(target, troll.gameObject, troll.damageSource, false, 0, out value)
                && SameImpact(intent, value);
            TrollIntents.Remove(id);
        }
        catch { }
        return Impacts.Push(value, active);
    }
    internal static IDisposable EnterArrowHit(Arrow arrow)
    {
        Flight flight = default;
        bool active = false;
        try
        {
            if (!HeavyShieldRuntime.Playing || arrow == null || arrow._damageSource != DamageSource.GreedProjectile
                || !HeavyShieldRuntime.IsOrdinaryEnemy(arrow.archer, true))
                return new FlightScope(Flights.Push(default, false), Impacts.Push(default, false));
            // Projectile life is separate from the shooter life. The marker has no Update/save interface.
            // Arrow prefabs need not have Damageable, so the root marker proves life directly.
            var marker = arrow.gameObject.GetComponent<CombatTargetLifeMarker>();
            if (marker == null && CombatTargetLife.EnsureRegistered()) marker = arrow.gameObject.AddComponent<CombatTargetLifeMarker>();
            float vx = arrow._rigidbody.velocity.x;
            if (marker != null && marker.enabled && marker.Observing && arrow.gameObject.activeInHierarchy
                && float.IsFinite(vx) && vx != 0f)
            {
                var token = new CombatTargetToken { GoPointer = arrow.gameObject.Pointer,
                    GoId = arrow.gameObject.GetInstanceID(), Damageable = arrow.Pointer, Life = marker.EnsureLife() };
                flight = new Flight(arrow.Pointer, token, vx);
                active = token.Life > 0;
            }
        }
        catch { }
        var scope = Flights.Push(flight, active);
        return new FlightScope(scope, Impacts.Push(default, false)); // HitObject alone is never a direct-damage receipt
    }
    internal static IDisposable EnterArrowDamage(Arrow arrow, Damageable target)
    {
        Impact value = default;
        bool active = false;
        try
        {
            var marker = arrow.gameObject.GetComponent<CombatTargetLifeMarker>();
            active = Flights.TryPeek(out var flight) && flight.Arrow == arrow.Pointer && marker != null
                && marker.enabled && marker.Observing && marker.Life == flight.Life.Life
                && arrow.gameObject.Pointer == flight.Life.GoPointer && arrow.gameObject.GetInstanceID() == flight.Life.GoId
                && TryImpact(target, arrow.archer, arrow._damageSource, true, flight.Vx, out value);
        }
        catch { }
        return Impacts.Push(value, active);
    }
    private static bool TryImpact(Damageable target, GameObject source, DamageSource kind,
        bool arrow, float vx, out Impact value)
    {
        value = default;
        if (!HeavyShieldRuntime.Playing || target == null || source == null || !source.activeInHierarchy
            || !HeavyShieldRuntime.TryGetCareer(target, out var career)
            || !HeavyShieldIdentity.ValidateCareer(career) || !HeavyShieldRuntime.IsOrdinaryEnemy(source, arrow)) return false;
        if (arrow ? kind != DamageSource.GreedProjectile : kind != DamageSource.Troll) return false;
        var sourceDamage = source.GetComponent<Damageable>();
        if (sourceDamage == null || sourceDamage.isDead || !sourceDamage.enabled
            || !CombatTargetLife.TryResolve(sourceDamage, out var sourceLife)
            || !CombatTargetLife.TryResolve(target, out var targetLife)) return false;
        value = new Impact(target, source, kind, targetLife, sourceLife, career, arrow, vx);
        return true;
    }
    private static bool SameImpact(in Impact a, in Impact b)
        => a.Career == b.Career && a.Kind == b.Kind && CombatTargetToken.Matches(a.TargetLife, b.TargetLife)
            && CombatTargetToken.Matches(a.SourceLife, b.SourceLife);

    private static bool ResetIntentContext()
    {
        if (!HeavyShieldIdentity.TryGetCampaign(out var campaign)) { TrollIntents.Clear(); return false; }
        long world = Managers.Inst.world.gameLayer.Pointer.ToInt64();
        if (world != _intentWorld || campaign.OwnerGeneration != _intentGeneration || campaign.Guid != _intentCampaign)
        {
            TrollIntents.Clear(); _intentWorld = world; _intentGeneration = campaign.OwnerGeneration; _intentCampaign = campaign.Guid;
        }
        return true;
    }

    internal static bool TryBlock(Damageable target, int damage, GameObject source, DamageSource kind)
    {
        try
        {
            if (damage <= 0 || target == null || source == null || !Impacts.TryPeek(out var ticket)
                || ticket.Target.Pointer != target.Pointer || ticket.Source.Pointer != source.Pointer || ticket.Kind != kind
                || target.invulnerable || target.isDead || !target.enabled || !target.gameObject.activeInHierarchy
                || !target.IsDamagedBy(kind) || !HeavyShieldIdentity.ValidateCareer(ticket.Career)
                || !CombatTargetLife.TryResolve(target, out var defenderLife)
                || !CombatTargetLife.TryResolve(source.GetComponent<Damageable>(), out var sourceLife)
                || !CombatTargetToken.Matches(defenderLife, ticket.TargetLife)
                || !CombatTargetToken.Matches(sourceLife, ticket.SourceLife)) return false;
            return HeavyShieldRuntime.TryBlock(ticket.Career, ticket.Arrow, ticket.VelocityX, source.transform.position.x);
        }
        catch { return false; }
    }
    internal static void ObserveDamageEnd(Damageable target)
    {
        try { HeavyShieldRuntime.ObserveNativeDeath(target); } catch { }
    }
}
#endif
