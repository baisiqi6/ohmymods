using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod;

/// <summary>
/// Reads the project's shared, bounded Knight roster and hands pure snapshots
/// to <see cref="CoinCourierRules"/>. The budget passed in is the coins available in
/// the courier purse, never the treasury. No scene search, native write or RPC.
/// </summary>
internal static class CoinCourierTargeting
{
    private static readonly List<CoinCourierRules.KnightSnapshot> Snapshots = new(16);
    private static readonly List<Knight> Owners = new(16);
    private static bool _loggedFault;

    internal static bool TrySelect(
        int purseCoins,
        float now,
        int lastServedSide,
        int maxCoinsPerVisit,
        IReadOnlyDictionary<long, float> nextEligibleAt,
        out Knight target,
        out CoinCourierRules.VisitPlan plan)
    {
        target = null;
        plan = default;
        try
        {
            Snapshots.Clear();
            Owners.Clear();
            if (!NetworkBigBoss.HasWorldAuth || purseCoins <= 0) return false;

            Knight[] knights = UnitScanCache.GetKnights();
            if (knights == null) return false;
            for (int i = 0; i < knights.Length; i++)
            {
                Knight knight = knights[i];
                if (!CurrentCandidate(knight)) continue;
                long life = KnightIdentityRuntime.GetLifetime(knight);
                if (life <= 0) continue; // untracked/pool-ambiguous identities fail closed
                Wallet wallet = knight.Wallet;
                if (wallet == null) continue;

                float readyAt = 0f;
                if (nextEligibleAt != null) nextEligibleAt.TryGetValue(life, out readyAt);
                Snapshots.Add(new CoinCourierRules.KnightSnapshot(
                    life, (int)knight.side, wallet.Coins, wallet.TotalCapacity,
                    true, true, false, readyAt));
                Owners.Add(knight);
            }

            if (!CoinCourierRules.TryPlan(Snapshots, purseCoins, now, lastServedSide, maxCoinsPerVisit, out plan)) return false;
            for (int i = 0; i < Snapshots.Count; i++)
            {
                if (Snapshots[i].LifeId != plan.LifeId) continue;
                Knight knight = Owners[i];
                // Same per-coin validation the economy layer runs before any wallet write.
                if (!IsCurrentDeliveryCandidate(knight, plan.LifeId, out Wallet wallet)) return false;
                if (wallet.Coins >= wallet.TotalCapacity) return false;
                target = knight;
                return true;
            }
            return false;
        }
        catch (Exception e)
        {
            target = null;
            plan = default;
            if (!_loggedFault)
            {
                _loggedFault = true;
                KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning(
                    "[CoinCourier] target roster deferred: " + e.GetType().Name);
            }
            return false;
        }
    }

    /// <summary>
    /// Minimal current-target validity check shared with the economy layer: the Knight
    /// must still be the live, non-dead, non-grabbed, non-embarking actor in the current
    /// world layer, with the expected process lifetime and a readable wallet reference.
    /// Reads the wallet reference only; it never reads or writes wallet values. Any
    /// state that cannot be read or confirmed fails closed.
    /// </summary>
    internal static bool IsCurrentDeliveryCandidate(Knight knight, long expectedLife, out Wallet wallet)
    {
        wallet = null;
        try
        {
            if (expectedLife == 0 || !CurrentCandidate(knight)) return false;
            if (KnightIdentityRuntime.GetLifetime(knight) != expectedLife) return false;
            Wallet current = knight.Wallet;
            if (current == null) return false;
            wallet = current;
            return true;
        }
        catch (Exception e)
        {
            wallet = null;
            if (!_loggedFault)
            {
                _loggedFault = true;
                KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning(
                    "[CoinCourier] target check deferred: " + e.GetType().Name);
            }
            return false;
        }
    }

    /// <summary>
    /// Roster eligibility for the courier: alive (Damageable not dead), not inert or
    /// grabbed, not boarding or already bound for a boat, and not in the native
    /// Embarking state. Mirrors the existing Knight gates in this assembly; any
    /// state that cannot be read or confirmed stays out.
    /// </summary>
    private static bool CurrentCandidate(Knight knight)
    {
        if (knight == null || !knight.enabled || knight.gameObject == null
            || !knight.gameObject.activeInHierarchy || !GreekBankScope.IsInCurrentLayer(knight)
            || knight._character == null || knight._character.inert || knight._character.grabbed
            || knight._damageable == null || knight._damageable.isDead
            || knight._fsm == null || knight._fsm.Current == Knight.State.Embarking) return false;
        Embarkee embarkee = knight._embarkee;
        if (embarkee != null && (embarkee.IsEmbarked || embarkee.IsTargetingEmbarkable
            || embarkee.EmbarkableTarget != null)) return false;
        return knight.side == Side.Left || knight.side == Side.Right;
    }
}
