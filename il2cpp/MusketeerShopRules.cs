using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod;

internal static class MusketeerShopRules
{
    internal const int RackCapacity = 3;
    internal const int FrameWidth = 176, FrameHeight = 80, AtlasWidth = FrameWidth * 4;
    internal const float PivotX = 64f / FrameWidth, PivotY = 2f / FrameHeight;
    internal const float LayoutCenterX = 0.75f, HalfWidth = 2.75f;
    internal static float SlotX(int slot) => ValidSlot(slot) ? 2.8125f : throw new ArgumentOutOfRangeException(nameof(slot));
    internal static float SlotY(int slot) => ValidSlot(slot) ? 0.25f * (slot + 1) : throw new ArgumentOutOfRangeException(nameof(slot));
    internal const float SlotZ = -0.002f;
    internal static bool ValidSlot(int slot) => slot >= 0 && slot < RackCapacity;
    internal static bool TryMask(IEnumerable<int> slots, out int mask)
    {
        mask = 0;
        if (slots == null) return false;
        foreach (int slot in slots)
        {
            if (!ValidSlot(slot) || (mask & (1 << slot)) != 0) { mask = 0; return false; }
            mask |= 1 << slot;
        }
        return true;
    }
    internal static int FirstFreeSlot(IEnumerable<int> slots)
    {
        if (!TryMask(slots, out int mask)) return -1;
        for (int slot = 0; slot < RackCapacity; slot++) if ((mask & (1 << slot)) == 0) return slot;
        return -1;
    }

    // ---- rack-gate evidence (2026-09-27 lock diagnostics; pure values only, so the exact line
    // the shop logs is unit-testable without the game) ----

    /// <summary>One frame of purchase-gate evidence. The Active branch fills this from what it
    /// already observed - the dump never performs scene reads of its own (a second Observe per
    /// frame is exactly what the payment path must not pay). Clauses mirror RackContextReady,
    /// plus the remaining CanPurchase gates the operator needs to read a blocked shop.</summary>
    internal struct RackGateProbe
    {
        internal bool Clearing;     // shop teardown in progress
        internal bool Retiring;     // teardown retry window
        internal bool Shop;         // MOD shop object resident
        internal bool Layer;        // its GameLayer reference resident
        internal bool Saving;       // native save in progress
        internal bool TimescaleOk;  // Time.timeScale > 0
        internal bool Retention;    // HeroShopRetention.CanServe(frame probe)
        internal bool State;        // identity state present
        internal bool Ready;        // state.Ready
        internal bool ReadOnly;     // state.ReadOnly
        internal bool Unresolved;   // state.Unresolved
        internal bool Baseline;     // state.HasBaseline
        internal bool Epoch;        // state.Epoch != null
        internal bool Context;      // cached context matches the state
        internal bool LayoutReady;  // last rack maintenance pass completed
        internal int StockRestores; // pending rack-physics restores
        internal int Attempts;      // highest retry attempt among them
        internal int ResidualSlots; // H3' probe: gun careers with a rack claim but no live binding
        internal int RackItems;     // rack items from the last maintenance pass
        internal bool Ghost;        // an unclaimed item the layout could not place
    }

    /// <summary>True when the blocked gate is anything other than the explained, fully healthy,
    /// full-rack state (rack items still wait for pickup there, visible in the world). Every
    /// clause here matches <see cref="FailedRackClauses"/>; keep the two in sync.</summary>
    internal static bool ShouldDumpRackGate(in RackGateProbe p)
    {
        // P1-A（2026-09-27 复审）：面板自动暂停把 timeScale 置 0 而 Game.State 仍为 Playing，
        // 此时 rack 读失败只是暂停的下游结果——暂停为唯一根因时不触发也不消耗取证预算。
        if (!p.TimescaleOk && !AnyRootCauseBesidesPause(in p)) return false;
        return p.Clearing || p.Retiring || !p.Shop || !p.Layer || p.Saving || !p.TimescaleOk || !p.Retention
            || !p.State || !p.Ready || p.ReadOnly || p.Unresolved || !p.Baseline || !p.Epoch || !p.Context
            || !p.LayoutReady || p.StockRestores != 0 || p.ResidualSlots != 0 || p.Ghost
            || p.RackItems < RackCapacity;
    }

    // Registry-backed anomalies still count while paused; the rack item count does not, because
    // the paused read cannot produce one (RackContextReady gates on the same timescale clause).
    private static bool AnyRootCauseBesidesPause(in RackGateProbe p)
        => p.Clearing || p.Retiring || !p.Shop || !p.Layer || p.Saving || !p.Retention
            || !p.State || !p.Ready || p.ReadOnly || p.Unresolved || !p.Baseline || !p.Epoch || !p.Context
            || !p.LayoutReady || p.StockRestores != 0 || p.ResidualSlots != 0 || p.Ghost;

    /// <summary>The complete evidence line: every gate clause as name=value, the restore
    /// responsibility, the residual-slot probe and the ghost marker, preceded by the names of
    /// the currently failing clauses (failed=none when healthy).</summary>
    internal static string DescribeRackGate(in RackGateProbe p)
        => "rack-gate failed=" + FailedRackClauses(in p)
            + " clearing=" + p.Clearing + " retiring=" + p.Retiring + " shop=" + p.Shop + " layer=" + p.Layer
            + " saving=" + p.Saving + " timescaleOk=" + p.TimescaleOk + " retention=" + p.Retention
            + " state=" + p.State + " ready=" + p.Ready + " ReadOnly=" + p.ReadOnly
            + " Unresolved=" + p.Unresolved + " HasBaseline=" + p.Baseline + " Epoch=" + p.Epoch
            + " context=" + p.Context + " layoutReady=" + p.LayoutReady
            + " stockRestores=" + p.StockRestores + " attempts=" + p.Attempts
            + " residualSlots=" + p.ResidualSlots + " ghost=" + p.Ghost + " rack=" + p.RackItems + "/" + RackCapacity;

    private static string FailedRackClauses(in RackGateProbe p)
    {
        var failed = new List<string>(8);
        if (p.Clearing) failed.Add("clearing");
        if (p.Retiring) failed.Add("retiring");
        if (!p.Shop) failed.Add("shop");
        if (!p.Layer) failed.Add("layer");
        if (p.Saving) failed.Add("saving");
        if (!p.TimescaleOk) failed.Add("timescale");
        if (!p.Retention) failed.Add("retention");
        if (!p.State) failed.Add("state");
        if (!p.Ready) failed.Add("ready");
        if (p.ReadOnly) failed.Add("ReadOnly");
        if (p.Unresolved) failed.Add("Unresolved");
        if (!p.Baseline) failed.Add("HasBaseline");
        if (!p.Epoch) failed.Add("Epoch");
        if (!p.Context) failed.Add("context");
        if (!p.LayoutReady) failed.Add("layoutReady");
        if (p.StockRestores != 0) failed.Add("stockRestores");
        if (p.ResidualSlots != 0) failed.Add("residualSlots");
        if (p.Ghost) failed.Add("ghost");
        if (p.RackItems < RackCapacity) failed.Add("rackIncomplete");
        return failed.Count == 0 ? "none" : string.Join(",", failed);
    }
}

/// <summary>Bounded cadence of the rack-gate evidence line (plain values only, shared with the
/// regression suite): one line per <see cref="IntervalSeconds"/> at most, and a hard
/// <see cref="SessionBudget"/> lines per process. Healthy periods never consume the budget: the
/// shop asks only after its gate probe found something other than the explained full-rack state.</summary>
internal sealed class MusketeerRackDiagPolicy
{
    internal const float IntervalSeconds = 60f;
    internal const int SessionBudget = 12;
    private float _nextAt;
    private int _lines;
    internal int Lines => _lines;
    internal bool TryBegin(float now)
    {
        if (_lines >= SessionBudget || now < _nextAt) return false;
        _lines++;
        _nextAt = now + IntervalSeconds;
        return true;
    }
}

// A receipt belongs to one exact bound career/state, native life and shop instance.
// Reconciliation never pulls an already-placed gun back after an external position change.
internal sealed class MusketeerRackLayout
{
    internal readonly record struct Stamp(object Career, object State, long Life, long Pointer,
        int Instance, int Shop, long Layer);
    internal sealed class Item
    {
        internal int Slot;
        internal Stamp Identity;
        internal bool Unclaimed;
        internal Func<float, float, float, bool> MoveAndVerify;
    }
    private readonly Dictionary<int, Stamp> _placed = new();
    private readonly List<int> _slots = new(3);
    internal void Reset() => _placed.Clear();
    internal bool IsPlaced(Item item) => item != null && _placed.TryGetValue(item.Slot, out var old)
        && old == item.Identity;
    internal bool Reconcile(bool ready, IReadOnlyList<Item> items)
    {
        if (!ready || items == null || items.Count > MusketeerShopRules.RackCapacity) return false;
        _slots.Clear();
        foreach (var item in items)
        {
            if (item == null) return false;
            _slots.Add(item.Slot);
        }
        if (!MusketeerShopRules.TryMask(_slots, out int mask)) return false;
        bool complete = true;
        foreach (var item in items)
        {
            // A gun claimed by a resident waits out its walk-to-pickup window: it needs no
            // anchor, cannot fail the layout, and its placed receipt stays until the slot
            // leaves the snapshot below.
            if (!item.Unclaimed) continue;
            if (IsPlaced(item)) continue;
            bool moved = false;
            try { moved = item.MoveAndVerify != null && item.MoveAndVerify(MusketeerShopRules.SlotX(item.Slot),
                MusketeerShopRules.SlotY(item.Slot), MusketeerShopRules.SlotZ); }
            catch { }
            if (moved) _placed[item.Slot] = item.Identity;
            else complete = false;
        }
        for (int slot = 0; slot < MusketeerShopRules.RackCapacity; slot++)
            if ((mask & (1 << slot)) == 0) _placed.Remove(slot);
        return complete;
    }
}
