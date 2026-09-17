using System;

namespace KingdomEnhancedMod;

/// <summary>
/// Freshness policy for the cached native Bow tool prefab (plain values only - no Unity or
/// interop types - so the payment gate is unit-testable outside the game). The real objects stay
/// in MusketeerShop; this class decides whether one captured prefab/pool/world tuple is still
/// exactly usable, and rate-limits rediscovery.
///
/// A capture is usable only while:
///  * the exact PoolManager, world GameLayer and biome identities it was captured in are
///    unchanged (a world, biome or scene change must never spawn through another world's pool);
///  * the captured prefab name still resolves through the live PoolManager to the captured pool
///    instance (PoolManager.InitPools re-registers fresh Pool instances, so a rebuild changes the
///    pointer and the capture dies instead of spawning through a stale pool);
///  * the caller (MusketeerShop) confirms its wrapper still matches the captured prefab pointer
///    and Unity instance id, so a reused address is never mistaken for the old asset.
/// Any mismatch - missing binding, failed resolution, different mapping - is "not usable".
/// The payment path only reads this state; it never discovers, loads or enumerates assets.
/// </summary>
internal sealed class MusketeerBowCachePolicy
{
    /// <summary>Minimum seconds between two discovery attempts while no binding is usable.</summary>
    internal const float RetrySeconds = 1.5f;

    /// <summary>Identities that pin one capture to one exact runtime world.</summary>
    internal struct Context
    {
        internal bool WorldReady;   // PoolManager + world GameLayer + biome were all readable
        internal long PoolManager;  // Managers.pools instance
        internal long World;        // world.gameLayer instance
        internal long Biome;        // BiomeHolder instance
        internal int BiomeIndex;    // biome index (the same layer can host different biome content)
    }

    private struct Binding
    {
        internal long Prefab;
        internal int PrefabGoId;
        internal string PrefabName;
        internal long Pool;
        internal long PoolManager;
        internal long World;
        internal long Biome;
        internal int BiomeIndex;
    }

    private Binding _binding;
    private bool _has;
    private int _attempts;
    private float _retryAt;

    internal bool HasBinding => _has;

    /// <summary>Discovery attempts started so far (diagnostics; the payment path must never raise it).</summary>
    internal int DiscoveryAttempts => _attempts;

    internal string PrefabName => _has ? _binding.PrefabName : string.Empty;

    /// <summary>Stores one validated capture. Fail-closed: any missing identity value is ignored
    /// and the policy stays unbound (the shop then keeps payment blocked).</summary>
    internal void Capture(long prefabPointer, int prefabGoId, string prefabName, long poolPointer, in Context context)
    {
        if (prefabPointer == 0 || string.IsNullOrEmpty(prefabName) || poolPointer == 0 || !context.WorldReady) return;
        _binding = new Binding
        {
            Prefab = prefabPointer,
            PrefabGoId = prefabGoId,
            PrefabName = prefabName,
            Pool = poolPointer,
            PoolManager = context.PoolManager,
            World = context.World,
            Biome = context.Biome,
            BiomeIndex = context.BiomeIndex,
        };
        _has = true;
        _retryAt = 0f;   // a fresh capture ends the retry window
    }

    /// <summary>Drops the capture. The retry clock is kept: after a stale capture is dropped the
    /// next maintenance pass may rediscover immediately, while repeated failures stay on the
    /// one-attempt-per-window budget (maintenance can never become a per-frame full scan).
    /// </summary>
    internal void Invalidate()
    {
        _has = false;
        _binding = default;
    }

    /// <summary>The cached wrapper must still be the same native prefab (pointer + Unity instance id).</summary>
    internal bool MatchesPrefab(long prefabPointer, int prefabGoId)
        => _has && _binding.Prefab == prefabPointer && _binding.PrefabGoId == prefabGoId;

    /// <summary>Freshness read used by the payment gate: exact same world/pool identities and the
    /// prefab name must still resolve to the captured pool instance. Read-only, no side effects.</summary>
    internal bool TryUse(in Context context, Func<string, long> resolvePool)
    {
        if (!_has || !context.WorldReady) return false;
        if (context.PoolManager != _binding.PoolManager || context.World != _binding.World
            || context.Biome != _binding.Biome || context.BiomeIndex != _binding.BiomeIndex) return false;
        return resolvePool != null && resolvePool(_binding.PrefabName) == _binding.Pool;
    }

    /// <summary>Bounded rediscovery gate: at most one attempt per <see cref="RetrySeconds"/>
    /// window while unbound. A successful <see cref="Capture"/> stops further attempts.</summary>
    internal bool TryBeginAttempt(float now)
    {
        if (_has) return false;
        if (now < _retryAt) return false;
        _retryAt = now + RetrySeconds;
        _attempts++;
        return true;
    }

    /// <summary>Pushes the next allowed discovery attempt to <paramref name="now"/> + seconds
    /// without starting one and without moving the window backwards. A capture that fails its live
    /// self-check uses this so a repeated failure stays on the same bounded cadence instead of
    /// re-scanning every frame.</summary>
    internal void DeferRetry(float now, float seconds)
    {
        float until = now + seconds;
        if (until > _retryAt) _retryAt = until;
    }
}
