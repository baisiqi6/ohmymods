using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Repairs the guard slot ranks that the native free-archer distribution hands to its consumers
/// (issue-78 part A).
///
/// Native ground truth (2.4 GameAssembly Kingdom.DistributeFreeArchers, measured
/// 0x59D0B1-0x59D1D9): the left counter starts at 0 and increments, but the right counter starts
/// at `freeCount - quota` where `quota = max((totalArchers - leftFollowers + rightFollowers) / 2,
/// 0)` mixes knight follower counts into the split. The counter is only correct while the actual
/// right free count equals `freeCount - quota`: with more right free archers
/// (`rightFreeCount > freeCount - quota`) the loop runs past zero and writes negative depths
/// (measured: free20/lf4/rf4 -> 5..-4, free20/lf40/rf40 -> -31..-40); with fewer it leaves the
/// last right slot above zero and writes shifted high ranks. Only the left slots are
/// native-correct.
///
/// This file runs at the native distribution boundary - the MusketeerDefense postfix of
/// Kingdom.DistributeFreeArchers - BEFORE the musketeer rebalance, and independently of it: no
/// capture, no musketeer config gate, no scene scan, no second distribution. It reads the cache
/// the native call just rebuilt, keeps every side exactly as native decided (safe-side head,
/// first/last forced units and any quota-driven split included) and rewrites only the depths that
/// are not this batch's rank: left 0..L-1 and right R-1..0, both in native cache order (the
/// native right counter's decreasing direction). A batch already matching those ranks writes
/// nothing, so a healthy native call is a no-op.
///
/// Facts this boundary intentionally does NOT change:
///  * Native still writes the bad ranks transiently inside its own call; consumers running before
///    this postfix see them. This repairs the state delivered at the distribution boundary, not
///    the native first write, and it does not cover Archer.ApplyData load restoration (part B,
///    still pending the first-consumer event-chain evidence).
///  * DefenseSpacing's 3-second depth pass keeps its own positive-cap / negative-rescue policy;
///    this file clamps nothing, invents no depth range and never moves a unit between sides,
///    walls or slots.
///  * A batch that no longer matches "the set native just distributed" (unreadable/null item,
///    not native `isAvailable`, invalid side, duplicate object) is refused whole with one bounded
///    log - never partially written, never defaulted to side 0 / depth 0.
/// </summary>
internal static class GuardRankDistribution
{
    private const int MaxLoggedKeys = 16;
    private const int MaxEventLogs = 32;

    private struct Entry
    {
        internal Archer Archer;
        internal Side Side;
        internal int Depth;
    }

    // Only bounded diagnostics survive across calls; the batch snapshot itself is built from
    // locals inside Reindex so no Archer wrapper is held past that call.
    private static readonly HashSet<string> Logged = new();
    private static int _eventLogs;

    /// <summary>
    /// Entry point for the native distribution postfix. Never throws: a failure here must not
    /// block the musketeer handoff that follows in the same postfix.
    /// </summary>
    internal static void ReindexAfterNative(Kingdom kingdom)
    {
        try { Reindex(kingdom); }
        catch (Exception e) { Once("reindex-" + e.GetType().Name, "rank reindex failed: " + e.GetType().Name); }
    }

    private static void Reindex(Kingdom kingdom)
    {
        if (!Context(kingdom)) return;

        var cache = kingdom._availableArchersCache;
        if (cache == null) { Refuse("missing-cache", 0); return; }

        int count;
        try { count = cache.Count; }
        catch { Refuse("unreadable-cache", 0); return; }
        if (count == 0) return;

        // Pre-read and validate the whole batch before the first write: a broken item refuses the
        // batch instead of leaving half the queue rewritten. The buffers are locals of this call -
        // the snapshot belongs to the native distribution that just ran, and no Archer wrapper may
        // outlive it.
        var batch = new List<Entry>(count);
        var seen = new HashSet<IntPtr>();
        for (int i = 0; i < count; i++)
        {
            Archer archer;
            Side side;
            int depth;
            try
            {
                archer = cache[i];
                if (archer == null || archer.gameObject == null) { Refuse("invalid-item", count); return; }
                if (!seen.Add(archer.Pointer)) { Refuse("duplicate-item", count); return; }
                if (!archer.isAvailable) { Refuse("unavailable-item", count); return; }   // native free-archer predicate
                side = archer._guardSide;
                depth = archer._guardDepth;
            }
            catch
            {
                Refuse("unreadable-item", count);
                return;
            }
            if (side != Side.Left && side != Side.Right) { Refuse("invalid-side", count); return; }
            batch.Add(new Entry { Archer = archer, Side = side, Depth = depth });
        }

        int left = 0, right = 0;
        for (int i = 0; i < batch.Count; i++)
        {
            if (batch[i].Side == Side.Left) left++; else right++;
        }

        // One forward pass, the native counter shapes: left 0..L-1, right R-1..0 (both in cache
        // order; sides are never re-decided here). A per-item write failure is isolated and the
        // remaining items are still attempted; the failed item's end state is never assumed.
        int leftCounter = 0, rightCounter = right, corrected = 0, failed = 0;
        for (int i = 0; i < batch.Count; i++)
        {
            Entry entry = batch[i];
            int target = entry.Side == Side.Left ? leftCounter++ : --rightCounter;
            if (target == entry.Depth) continue;
            try
            {
                entry.Archer.SetGuardSide(entry.Side, target);
                corrected++;
            }
            catch
            {
                failed++;   // no retry, no rollback; the remaining items continue below
            }
        }

        // Bounded summary also when every write failed (corrected == 0); a partial batch is never
        // reported as a completed reindex.
        if (corrected > 0 || failed > 0)
        {
            if (failed > 0)
                Once("write-failed", "a rank write failed; this batch may be partial and the failed item's state is unknown");
            if (_eventLogs < MaxEventLogs)
            {
                _eventLogs++;
                Info("reindex: total=" + count + " left=" + left + " right=" + right
                    + " corrected=" + corrected
                    + (failed > 0 ? " failed=" + failed + " partial (failed state unknown)" : ""));
            }
        }
    }

    /// <summary>
    /// Offline world-authoritative context for the current kingdom instance: total switch on, the
    /// managers point at this exact kingdom, and the world/gameLayer exist. No Playing/time-scale
    /// gate - a distribution that happens during loading or while paused is still a real one. The
    /// per-item topology is deliberately not gated (the native cache is the batch authority).
    /// </summary>
    private static bool Context(Kingdom kingdom)
    {
        if (kingdom == null) return false;
        if (ModConfig.Enabled == null || !ModConfig.Enabled.Value) return false;
        if (NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth) return false;
        Managers managers = Managers.Inst;
        if (managers == null || managers.kingdom == null || managers.kingdom.Pointer != kingdom.Pointer) return false;
        var world = managers.world;
        return world != null && world.gameLayer != null;
    }

    private static void Refuse(string reason, int count)
    {
        Once("refuse-" + reason, "batch refused (" + reason
            + (count > 0 ? ", count=" + count : "") + "); no ranks written");
    }

    private static void Info(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[GuardRank] " + message); }
        catch { }
    }

    private static void Once(string key, string message)
    {
        try
        {
            if (Logged.Count >= MaxLoggedKeys || !Logged.Add(key)) return;
            KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[GuardRank] " + message);
        }
        catch { }
    }
}
