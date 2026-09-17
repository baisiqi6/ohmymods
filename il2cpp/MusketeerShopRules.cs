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
            if (!item.Unclaimed) { complete = false; continue; }
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
