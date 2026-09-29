using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod;

/// <summary>
/// Pure selection and per-visit spending bounds for the purse-funded coin courier.
/// Callers supply only their already-registered Knights; this class never scans a scene.
/// A plan is not a debit or a wallet credit. The authority-side transfer owns that commit.
/// The per-visit quota and both clock values are caller-supplied: this class reads no
/// configuration and keeps no state, so a future runtime passes its configured values in.
/// </summary>
internal static class CoinCourierRules
{
    internal const int MaxCoinsPerVisit = 4;
    internal const float KnightCooldownSeconds = 15f;

    internal readonly record struct KnightSnapshot(
        long LifeId,
        int Side,
        int Coins,
        int Capacity,
        bool Alive,
        bool SameWorld,
        bool InTransit,
        float NextEligibleAt);

    internal readonly record struct VisitPlan(long LifeId, int Side, int CoinsToSend);

    /// <summary>
    /// Prioritizes the lowest fill ratio; ties go to the side not served last,
    /// then the larger deficit and stable life id. <paramref name="purseCoins"/> is the
    /// coins available in the courier purse for this visit (never the treasury) and
    /// <paramref name="maxCoinsPerVisit"/> the caller's per-visit quota; a non-positive
    /// value rejects the plan. Caller validates a safe landing and the same Knight
    /// life before committing each individual coin.
    /// </summary>
    internal static bool TryPlan(
        IReadOnlyList<KnightSnapshot> knights,
        int purseCoins,
        float now,
        int lastServedSide,
        int maxCoinsPerVisit,
        out VisitPlan plan)
    {
        plan = default;
        if (knights == null || purseCoins <= 0 || maxCoinsPerVisit <= 0 || !float.IsFinite(now)) return false;

        bool found = false;
        KnightSnapshot best = default;
        for (int i = 0; i < knights.Count; i++)
        {
            KnightSnapshot candidate = knights[i];
            if (!Eligible(candidate, now)) continue;
            if (!found || Prefer(candidate, best, lastServedSide))
            {
                found = true;
                best = candidate;
            }
        }
        if (!found) return false;

        int shortage = best.Capacity - best.Coins;
        int amount = Math.Min(maxCoinsPerVisit, Math.Min(shortage, purseCoins));
        if (amount <= 0) return false;
        plan = new VisitPlan(best.LifeId, best.Side, amount);
        return true;
    }

    internal static bool CanSendNextCoin(
        VisitPlan plan,
        long currentLifeId,
        int currentCoins,
        int currentCapacity,
        int remainingPurseCoins,
        int sentThisVisit)
    {
        if (plan.LifeId == 0 || plan.LifeId != currentLifeId
            || sentThisVisit < 0 || sentThisVisit >= plan.CoinsToSend
            || currentCoins < 0 || currentCapacity <= 0 || currentCoins >= currentCapacity
            || remainingPurseCoins <= 0) return false;
        return true;
    }

    /// <summary>
    /// Deadline contract for the same Knight's next visit: pure addition over the caller's
    /// game clock, no storage and no timer. Rejects non-finite inputs, negative cooldowns
    /// and a deadline no float can hold; zero cooldown legitimately yields
    /// <paramref name="now"/>. A future runtime stores the returned value instead of
    /// hard-coding <see cref="KnightCooldownSeconds"/>.
    /// </summary>
    internal static bool TryNextEligibleAt(float now, float cooldownSeconds, out float nextEligibleAt)
    {
        nextEligibleAt = 0f;
        if (!float.IsFinite(now) || !float.IsFinite(cooldownSeconds) || cooldownSeconds < 0f) return false;
        double deadline = (double)now + cooldownSeconds;
        if (deadline > float.MaxValue) return false;
        nextEligibleAt = (float)deadline;
        return true;
    }

    private static bool Eligible(KnightSnapshot knight, float now)
        => knight.LifeId != 0 && knight.Alive && knight.SameWorld && !knight.InTransit
            && (knight.Side == -1 || knight.Side == 1)
            && knight.Capacity > 0 && knight.Coins >= 0 && knight.Coins < knight.Capacity
            && float.IsFinite(knight.NextEligibleAt) && now >= knight.NextEligibleAt;

    private static bool Prefer(KnightSnapshot candidate, KnightSnapshot current, int lastServedSide)
    {
        long left = (long)candidate.Coins * current.Capacity;
        long right = (long)current.Coins * candidate.Capacity;
        if (left != right) return left < right;

        bool candidateAlternates = candidate.Side != lastServedSide;
        bool currentAlternates = current.Side != lastServedSide;
        if (candidateAlternates != currentAlternates) return candidateAlternates;

        int candidateDeficit = candidate.Capacity - candidate.Coins;
        int currentDeficit = current.Capacity - current.Coins;
        if (candidateDeficit != currentDeficit) return candidateDeficit > currentDeficit;
        return candidate.LifeId < current.LifeId;
    }
}
