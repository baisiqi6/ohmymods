using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Bounded native adapters for existing paid rack identities. No discovery or new rights.</summary>
internal sealed class CustomShopStocks
{
    private sealed class Item
    {
        internal DroppableTool Tool;
        internal ShopStockAccess Access;
        internal Func<bool> Exact;
        internal Func<bool> Stock;
        internal Func<bool> Parked;
        internal Action<bool> SetParked;
    }
    internal static readonly CustomShopStocks Musket = new(3);
    internal static readonly CustomShopStocks Shield = new(1);
    private readonly Dictionary<ShopStockToken, Item> _items = new();
    private readonly CustomShopStockTransit _transit;
    private Transform _layer;
    internal bool Pending => _items.Count != 0;
    internal string Fault => _transit.LastFault;
    internal bool Holds(DroppableTool tool)
    {
        if (tool == null) return false;
        foreach (var item in _items.Values) if (item.Tool != null && item.Tool.Pointer == tool.Pointer) return true;
        return false;
    }
    private CustomShopStocks(int capacity) { _transit = new(capacity, 5); }

    internal bool Capture(MusketeerIdentity.Career career, Transform layer)
    {
        if (!ModConfig.Enabled.Value || !ModConfig.MusketeerEnabled.Value) return false;
        if (career?.Tool == null || career.State == null || layer == null
            || career.StockSlot < 0 || career.StockSlot >= MusketeerIdentity.StockSlots) return false;
        var tool = career.Tool;
        var token = new ShopStockToken(career.Id.ToString("D"), layer.Pointer.ToInt64(), career.Life,
            tool.Pointer.ToInt64(), career.GoId, career.StockSlot, career);
        bool Exact() => career.Id.ToString("D") == token.PaidId && career.Life == token.Life
            && career.GoId == token.Instance && career.Tool != null && career.Tool.Pointer == tool.Pointer
            && MusketeerIdentity.HasExactStockBinding(career);
        return Capture(token, tool, layer, Exact, () => career.StockSlot == token.Slot,
            () => MusketeerGunVisuals.IsParked(tool), p => MusketeerGunVisuals.SetParked(tool, p));
    }

    internal bool Capture(DroppableTool tool, Transform layer)
    {
        if (!ModConfig.Enabled.Value || !ModConfig.HeavyShieldEnabled.Value) return false;
        if (tool == null || layer == null || !HeavyShieldIdentity.TryGetPaidBow(tool, out var handle)) return false;
        var token = new ShopStockToken(handle.Receipt.ToString("D"), handle.World, handle.Life,
            tool.Pointer.ToInt64(), handle.GoId, 0, HeavyShieldIdentity.Current);
        bool Exact() => HeavyShieldIdentity.HasExactPaidStockBinding(in handle)
            && tool.gameObject != null && tool.gameObject.Pointer == handle.Root;
        return Capture(token, tool, layer, Exact,
            () => HeavyShieldIdentity.TryGetPaidBow(tool, out var actual) && actual == handle,
            () => HeavyShieldShopShell.IsStockParked(tool), p => HeavyShieldShopShell.SetStockParked(tool, p));
    }

    private bool Capture(ShopStockToken token, DroppableTool tool, Transform layer, Func<bool> exact,
        Func<bool> stock, Func<bool> parked, Action<bool> setParked)
    {
        if (!CanBegin(layer)) return false;
        if (_layer != null && _layer.Pointer != layer.Pointer && !ReleaseAll()) return false;
        _layer = layer;
        if (!_items.TryGetValue(token, out var item))
        {
            item = new Item { Tool = tool, Exact = exact, Stock = stock, Parked = parked, SetParked = setParked };
            var captured = item;
            item.Access = new ShopStockAccess
            {
                Token = token, Read = t => Read(captured, t, layer),
                SetFriendlyPolicy = (t, value) => Write(captured, t, layer, () => tool.pickUpPolicy = (PickUpPolicy)value),
                SetEnemyPolicy = (t, value) => Write(captured, t, layer, () => tool.CurrentEnemyPolicy = (PickUpPolicy)value),
                SetVisualParked = (t, value) => Write(captured, t, layer, () => setParked(value)),
                Move = (t, x, y, z) => Write(captured, t, layer, () =>
                {
                    if (!CanBegin(layer) || tool.pickedUp || tool.friendlyClaimer != null || tool.enemyClaimer != null
                        || tool._rigidbody == null || !tool._rigidbody.isKinematic)
                        throw new InvalidOperationException("rack move no longer authorized");
                    tool.transform.position = new Vector3(x, y, z);
                })
            };
        }
        bool result = _transit.Protect(item.Access);
        // A partial native write still retains its owner until restored.
        if (_transit.IsHeld(token)) _items[token] = item;
        return result;
    }

    private static bool CanBegin(Transform layer)
        => layer != null && layer.gameObject != null && layer.gameObject.activeInHierarchy
            && !NetworkBigBoss.IsOnline && NetworkBigBoss.HasWorldAuth
            && Managers.Inst?.world?.gameLayer != null && Managers.Inst.world.gameLayer.Pointer == layer.Pointer
            && Managers.Inst?.game != null && (Managers.Inst.game.state == Game.State.Playing
                || Managers.Inst.game.state == Game.State.Loading);

    private static ShopStockObservation Read(Item item, ShopStockToken token, Transform layer)
    {
            var tool = item.Tool;
            if (!item.Exact() || tool == null || tool.Pointer.ToInt64() != token.Pointer
                || tool.gameObject == null || tool.gameObject.GetInstanceID() != token.Instance
                || layer == null || layer.Pointer.ToInt64() != token.World || tool.transform == null
                || !tool.transform.IsChildOf(layer)) return default;
            var p = tool.transform.position;
            // Release deliberately uses exact old-life proof, without the positive gameplay gate.
            return new ShopStockObservation { Valid = true, PickedUp = tool.pickedUp,
                FriendlyClaimed = tool.friendlyClaimer != null, EnemyClaimed = tool.enemyClaimer != null,
                StockOwned = item.Stock(), VisualParked = item.Parked(),
                FriendlyPolicy = (int)tool.pickUpPolicy, EnemyPolicy = (int)tool.CurrentEnemyPolicy,
                X = p.x, Y = p.y, Z = p.z };
    }
    private static void Write(Item item, ShopStockToken token, Transform layer, Action write)
    {
        if (!Read(item, token, layer).Valid) throw new InvalidOperationException("stock life changed before write");
        write();
    }

    internal bool Park(Vector3 anchor)
    {
        if (!Pending) return true;
        if (!CanBegin(_layer)) return false;
        bool ok = true;
        foreach (var item in _items.Values) ok &= _transit.Park(item.Access, anchor.x, anchor.y, anchor.z);
        Prune(); return ok;
    }
    internal bool Place(DroppableTool tool, Vector3 target)
    {
        foreach (var pair in _items)
            if (pair.Value.Tool != null && tool != null && pair.Value.Tool.Pointer == tool.Pointer)
            {
                bool ok = CanBegin(_layer) && _transit.Place(pair.Value.Access, target.x, target.y, target.z);
                Prune(); return ok;
            }
        return true;
    }
    internal bool ReleaseRoot(GameObject root)
    {
        bool ok = true;
        if (root == null) return true;
        foreach (var pair in _items)
            if (pair.Value.Tool != null && pair.Value.Tool.gameObject != null
                && pair.Value.Tool.gameObject.Pointer == root.Pointer) ok &= _transit.Release(pair.Key);
        Prune(); return ok;
    }
    internal bool ReleaseAll()
    {
        bool ok = _transit.ReleaseAll(); Prune();
        if (!Pending) _layer = null;
        return ok;
    }
    internal bool CheckContext(bool enabled)
    {
        if (!Pending) return true;
        var game = Managers.Inst?.game;
        if (!enabled || NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth
            || _layer == null || Managers.Inst?.world?.gameLayer == null
            || Managers.Inst.world.gameLayer.Pointer != _layer.Pointer || game == null
            || (game.state != Game.State.Playing && game.state != Game.State.Menu && game.state != Game.State.Loading))
            return ReleaseAll();
        return true;
    }
    private void Prune()
    {
        List<ShopStockToken> remove = null;
        foreach (var pair in _items) if (!_transit.IsHeld(pair.Key)) (remove ??= new()).Add(pair.Key);
        if (remove != null) foreach (var token in remove) _items.Remove(token);
    }
}
