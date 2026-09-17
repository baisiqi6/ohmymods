using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Fair left/right guard distribution for the paid musketeer subset, applied at the native
/// Kingdom.DistributeFreeArchers boundary only.
///
/// Native ground truth (2.1.0 logic reference Kingdom.cs:2651-2725, same shape in 2.4 per the
/// measured guard fields): the native pass collects every available archer, sorts them by x
/// (`leftToRightArchers`) and fills the LEFT side with the leftmost half; the first sorted archer
/// is always forced Left and the last always Right. Side assignment is therefore a function of the
/// sorted rank, not of the unit's own side of the campfire. Musketeers are all promoted at one
/// shop/rack x, so one bought batch lands in the same band of the sorted list and the whole batch
/// is sent to the same wall; walking there only reinforces that rank, so the native pass can never
/// split them. With exactly one side completely safe, the native pass additionally keeps up to
/// MAX_SAFE_SIDE_ARCHERS (4) of the sorted head on the safe side - the same leftmost cluster, so
/// the whole batch can just sit out the fight.
///
/// This file rebalances ONLY the marked, currently free musketeer archers, after the native pass.
/// The user asked for the firearm subset itself to be spread evenly left/right, so the split is
/// independent of the native per-side danger state: ordinary archers keep the native danger-based
/// allocation untouched, while this subset splits evenly whenever both sides have a usable guard
/// target, including the native campfire/minimum-extent fallback when a wall is absent.
/// Explicit horn/campaign guard overrides keep the subset on native assignment.
///
///  * Prefix captures each unit's prior side before the native pass overwrites it, postfix decides.
///    The native pass is never skipped, replaced or re-entered. Identity binding queues one
///    additional native pass after promotion/load readiness, outside the original call.
///  * Counts stay within one (Right takes the odd one, native parity). Units keep their prior side
///    unless the counts must be fixed; movers are chosen nearest the target wall (largest x towards
///    Right, smallest x towards Left). A repeat pass with balanced counts and matching sides writes
///    nothing - no every-refresh oscillation.
///  * Excluded from the subset (native free-archer semantics): unmarked/ordinary/hero archers,
///    knight followers, tower/guard posts, formation members, embarked or boarding, grabbed,
///    inert/stationary, hidden, player-controlled, dying, off-world and out-of-layer units.
///  * Writes go through the native Archer.SetGuardSide(side, depth) only. A moved/placed unit takes
///    the smallest depth nobody on that side holds (untouched ordinary archers are read through the
///    current native indexed available-archer list, never written), avoiding stale scene caches;
///    enforcement writes keep the unit's own native depth when that slot is still free. No invented
///    depth range - depths are native rank indices, and DefenseSpacing's clamp pass continues to pull
///    overly deep ranks back into bow range for every archer. No transform/teleport write, no
///    Mover/position replacement, no callback destroy, no per-frame scan and no save schema - writes
///    happen only at the long native distribution boundary.
/// </summary>
internal static class MusketeerDefense
{
    // Depth values are native rank indices behind the wall; this file never invents a range.
    // 2.5 y is the tower-height guard shared with DefenseSpacing.
    private const float TowerHeightY = 2.5f;
    private const int MaxLoggedKeys = 32;
    private const int MaxEventLogs = 32;

    private struct Entry
    {
        internal Archer Archer;
        internal int GoId;
        internal Side PriorSide;
        internal int PriorDepth;
    }

    private static readonly List<Entry> Captured = new();
    private static readonly List<Archer> Units = new();
    private static readonly List<Side> Decisions = new();
    private static readonly List<int> Writers = new();
    private static readonly HashSet<int> UsedLeft = new();
    private static readonly HashSet<int> UsedRight = new();
    private static readonly HashSet<string> Logged = new();
    private static readonly Comparison<Entry> ByPosition = CompareByPosition;
    private static bool _capturing;
    private static int _captureFrame = -1;
    private static float _captureTime;
    private static IntPtr _canaryWorld;
    private static bool _canaryLogged;
    private static int _eventLogs;
    private static bool _redistributing;

    private static void Info(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[MusketeerDefense] " + message); }
        catch { }
    }

    private static void Once(string key, string message)
    {
        try
        {
            if (Logged.Count >= MaxLoggedKeys || !Logged.Add(key)) return;
            KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[MusketeerDefense] " + message);
        }
        catch { }
    }

    // ---- gates ---------------------------------------------------------------

    /// <summary>Local, offline, world-authoritative context for the active kingdom instance.</summary>
    private static bool Context(Kingdom kingdom)
    {
        try
        {
            if (kingdom == null || !MusketeerAccess.Enabled) return false;
            Managers managers = Managers.Inst;
            if (managers == null || managers.kingdom == null
                || managers.kingdom.Pointer != kingdom.Pointer) return false;
            Transform layer = MusketeerAccess.World;
            return layer != null && layer.gameObject != null;
        }
        catch { return false; }
    }

    /// <summary>
    /// Both sides have a usable guard target and no forced override. The per-side danger state is
    /// deliberately NOT consulted: the firearm subset splits by user request even while one side is
    /// completely safe (otherwise the native pass parks the whole shop batch on that side), while
    /// ordinary archers keep the native danger-based allocation untouched. Ask native for each
    /// target, so a missing wall can use its campfire/minimum-extent fallback. A horn/campaign
    /// override that redirects either side, or a non-finite target, still closes the split.
    /// </summary>
    private static bool SplitOpen(Kingdom kingdom)
    {
        try
        {
            if (kingdom.HasOverrideGuardPosition()) return false;   // horn: every defender is sent to one wall
            var left = kingdom.GetGuardPosition(Side.Left);
            var right = kingdom.GetGuardPosition(Side.Right);
            return left.Key == Side.Left && right.Key == Side.Right
                && float.IsFinite(left.Value) && float.IsFinite(right.Value);
        }
        catch { return false; }
    }

    /// <summary>
    /// A marked musketeer that native itself treats as a free archer right now. Everything native
    /// reserves for another job/lifecycle is left untouched.
    /// </summary>
    private static bool Adjustable(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return false;
            if (!archer.gameObject.activeInHierarchy || !archer.gameObject.activeSelf) return false;
            if (!MusketeerIdentity.IsUnit(archer)) return false;
            if (!MusketeerAccess.InWorld(archer)) return false;
            if (!archer.isAvailable) return false;                    // native free-archer gate
            if (archer._knight != null) return false;                 // knight follower: native formation slot
            if (archer._guardSlot != null || archer.inGuardSlot) return false;   // tower/guard post
            Embarkee embarkee = archer._embarkee;
            if (embarkee != null && (embarkee.IsEmbarked || embarkee.EmbarkableTarget != null)) return false;
            if (archer.GetFormation() != null) return false;          // shield wall / formation member
            if (archer.ShouldPlayerControl()) return false;           // player-controlled unit
            if (archer.harmless) return false;                        // hidden (hiding spot / quest)
            Character character = archer._character;
            if (character == null || character.inert || character.grabbed || character.isStationary) return false;
            Damageable damageable = archer._damageable;
            if (damageable == null || damageable.isDead) return false;
            Transform transform = archer.transform;
            if (transform == null || transform.position.y > TowerHeightY) return false;
            return true;
        }
        catch { return false; }
    }

    // ---- native boundary -----------------------------------------------------

    // Called only for a bounded pending identity-binding event, after that state is ready and
    // promotion has unwound. Never call recursively from a native distribution hook. Returning
    // false requests one of the identity event's finite retries; ordinary frames do no work.
    internal static bool RedistributeAfterBindings()
    {
        if (_capturing && (_captureFrame != Time.frameCount || _captureTime != Time.unscaledTime))
            _capturing = false;
        if (_redistributing || _capturing) return false;
        try
        {
            Kingdom kingdom = Managers.Inst?.kingdom;
            if (!Context(kingdom) || !SplitOpen(kingdom)) return true;
            _redistributing = true;
            kingdom.DistributeFreeArchers();
            return true;
        }
        catch (Exception e) { Once("binding-distribution", "binding distribution failed: " + e.GetType().Name); return false; }
        finally { _redistributing = false; }
    }

    internal static bool Begin(Kingdom kingdom)
    {
        try
        {
            if (Context(kingdom))
            {
                // One canary per world: proves the native distribution boundary is hooked even
                // when the split gate later declines this particular call.
                Transform layer = MusketeerAccess.World;
                if (layer != null && layer.Pointer != _canaryWorld)
                {
                    _canaryWorld = layer.Pointer;
                    _canaryLogged = false;
                    _eventLogs = 0;
                }
                if (!_canaryLogged)
                {
                    _canaryLogged = true;
                    Info("native distribution boundary active (canary)");
                }
            }
            // A postfix skipped by a native throw cannot leave the capture in flight forever.
            // A genuinely nested native distribution shares the exact frame+time of its owner.
            if (_capturing && (_captureFrame != Time.frameCount || _captureTime != Time.unscaledTime))
                _capturing = false;
            if (_capturing) return false;   // nested call: the outer distribution owns the decision
            Captured.Clear();
            if (!Context(kingdom) || !SplitOpen(kingdom)) return false;
            Units.Clear();
            MusketeerIdentity.CopyUnits(Units);
            for (int i = 0; i < Units.Count; i++)
            {
                Archer archer = Units[i];
                if (!Adjustable(archer)) continue;
                Captured.Add(new Entry
                {
                    Archer = archer,
                    GoId = SafeId(archer.gameObject),
                    PriorSide = archer._guardSide,
                    PriorDepth = archer._guardDepth
                });
            }
            if (Captured.Count == 0) return false;
            _capturing = true;
            _captureFrame = Time.frameCount;
            _captureTime = Time.unscaledTime;
            return true;
        }
        catch (Exception e)
        {
            _capturing = false;
            Captured.Clear();
            Once("begin", "capture failed: " + e.GetType().Name);
            return false;
        }
    }

    internal static void End(Kingdom kingdom)
    {
        _capturing = false;
        try
        {
            if (Captured.Count == 0) return;
            if (!Context(kingdom) || !SplitOpen(kingdom)) return;
            Rebalance(kingdom);
        }
        catch (Exception e) { Once("apply", "rebalance failed: " + e.GetType().Name); }
        finally { Captured.Clear(); }
    }

    // ---- decision ------------------------------------------------------------

    private static void Rebalance(Kingdom kingdom)
    {
        int count = Captured.Count;
        Captured.Sort(ByPosition);

        Decisions.Clear();
        int left = 0, right = 0, neutral = 0;
        for (int i = 0; i < count; i++)
        {
            Side prior = Captured[i].PriorSide;
            if (prior == Side.Left) { Decisions.Add(Side.Left); left++; }
            else if (prior == Side.Right) { Decisions.Add(Side.Right); right++; }
            else { Decisions.Add(prior); neutral++; }
        }

        // Units without a prior side (new career / lost assignment) fill the smaller side first;
        // a lone one takes Left and a tie among several takes Right - the native "first sorted
        // archer Left, Right keeps the odd one" outcome.
        bool firstNeutral = true;
        for (int i = 0; i < count; i++)
        {
            if (Decisions[i] == Side.Left || Decisions[i] == Side.Right) continue;
            Side side;
            if (left < right) side = Side.Left;
            else if (left > right) side = Side.Right;
            else side = firstNeutral ? Side.Left : Side.Right;
            firstNeutral = false;
            Decisions[i] = side;
            if (side == Side.Left) left++; else right++;
        }

        // Minimal fix-up: bring the difference inside one by moving the units nearest the
        // destination wall (largest x towards Right, smallest x towards Left).
        int moved = 0;
        int diff = left - right;
        if (diff > 1)
        {
            int need = diff / 2;
            for (int i = count - 1; i >= 0 && moved < need; i--)
                if (Decisions[i] == Side.Left) { Decisions[i] = Side.Right; moved++; }
            left -= moved;
            right += moved;
        }
        else if (diff < -1)
        {
            int need = (-diff) / 2;
            for (int i = 0; i < count && moved < need; i++)
                if (Decisions[i] == Side.Right) { Decisions[i] = Side.Left; moved++; }
            right -= moved;
            left += moved;
        }

        // Split the subset into enforcement writers (native put them on the other side) and
        // residents (already on the decided side, keeping the native depth). Resident depths are
        // registered first so a writer never lands on a slot a resident is keeping.
        UsedLeft.Clear();
        UsedRight.Clear();
        Writers.Clear();
        for (int i = 0; i < count; i++)
        {
            Archer archer = Captured[i].Archer;
            if (archer == null || archer.gameObject == null) continue;
            Side current;
            try { current = archer._guardSide; }
            catch { continue; }
            if (current != Decisions[i])
            {
                if (Adjustable(archer)) Writers.Add(i);
                continue;
            }
            if (!Adjustable(archer)) continue;
            int depth = archer._guardDepth;
            if (depth < 0) continue;
            if (Decisions[i] == Side.Left) UsedLeft.Add(depth); else UsedRight.Add(depth);
        }

        int written = 0;
        if (Writers.Count > 0)
        {
            // Only a pending write pays for the occupied-slot census. Untouched ordinary archers
            // keep both side and depth (never written here). The native pass has just rebuilt
            // this indexed List<Archer>, including actors enabled in this same frame. Never use
            // the 3-second scene cache or enumerate the native HashSet (pit 26).
            if (!CensusOccupiedSlots(kingdom)) return;
            for (int w = 0; w < Writers.Count; w++)
            {
                int i = Writers[w];
                Side want = Decisions[i];
                HashSet<int> used = want == Side.Left ? UsedLeft : UsedRight;
                int depth = ReuseOrFreeDepth(Captured[i], want, used);
                Captured[i].Archer.SetGuardSide(want, depth);
                used.Add(depth);
                written++;
            }
        }

        if ((written > 0 || moved > 0) && _eventLogs < MaxEventLogs)
        {
            _eventLogs++;
            Info("rebalanced: units=" + count + " neutral=" + neutral
                + " moved=" + moved + " written=" + written
                + " left=" + left + " right=" + right);
        }
    }

    /// <summary>
    /// Occupied depth slots per side, including ordinary archers this file never writes. Only
    /// wall-defender-style units occupy depth ranks (available archers in the native pass); knight
    /// followers and tower/guard-slot archers position from their own systems, so they are skipped.
    /// </summary>
    private static bool CensusOccupiedSlots(Kingdom kingdom)
    {
        var archers = kingdom._availableArchersCache;
        if (archers == null) return false;
        for (int i = 0; i < archers.Count; i++)
        {
            Archer other = archers[i];
            if (other == null || other.gameObject == null || !other.gameObject.activeInHierarchy) continue;
            if (!MusketeerAccess.InWorld(other)) continue;
            if (other._knight != null || other._guardSlot != null || other.inGuardSlot) continue;
            Side side;
            int depth;
            try { side = other._guardSide; depth = other._guardDepth; }
            catch { continue; }
            if (depth < 0) continue;
            if (side == Side.Left) UsedLeft.Add(depth);
            else if (side == Side.Right) UsedRight.Add(depth);
        }
        return true;
    }

    /// <summary>
    /// Enforcement writes keep the native depth the unit already carries on the decided side when
    /// that slot is still unoccupied; placements/moves take the smallest unoccupied depth. No
    /// invented depth range: depths are native rank indices, and DefenseSpacing's clamp pass keeps
    /// pulling overly deep ranks back into bow range for every archer (it writes depths, this file
    /// only writes sides, so the two never fight).
    /// </summary>
    private static int ReuseOrFreeDepth(Entry entry, Side side, HashSet<int> used)
    {
        if (side == entry.PriorSide && entry.PriorDepth >= 0 && !used.Contains(entry.PriorDepth))
            return entry.PriorDepth;
        int depth = 0;
        while (used.Contains(depth)) depth++;
        return depth;
    }

    private static int SafeId(GameObject root)
    {
        try { return root != null ? root.GetInstanceID() : 0; }
        catch { return 0; }
    }

    private static int CompareByPosition(Entry a, Entry b)
    {
        float ax = 0f, bx = 0f;
        try { if (a.Archer != null && a.Archer.transform != null) ax = a.Archer.transform.position.x; }
        catch { ax = 0f; }
        try { if (b.Archer != null && b.Archer.transform != null) bx = b.Archer.transform.position.x; }
        catch { bx = 0f; }
        if (!float.IsFinite(ax)) ax = 0f;
        if (!float.IsFinite(bx)) bx = 0f;
        int cmp = ax.CompareTo(bx);
        return cmp != 0 ? cmp : a.GoId.CompareTo(b.GoId);
    }
}

/// <summary>
/// Prefix captures the marked units' prior sides before the native pass overwrites them; postfix
/// rebalances the subset. The native method body always runs (the prefix returns void) and the
/// postfix only acts for the outermost call that owns a capture.
/// </summary>
[HarmonyPatch(typeof(Kingdom), nameof(Kingdom.DistributeFreeArchers))]
internal static class Kingdom_DistributeFreeArchers_MusketeerDefense_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Kingdom __instance, out bool __state)
    { __state = MusketeerDefense.Begin(__instance); }

    [HarmonyPostfix]
    private static void Postfix(Kingdom __instance, bool __state)
    {
        if (__state) MusketeerDefense.End(__instance);
    }
}
