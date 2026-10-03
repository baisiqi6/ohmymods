using System;
using System.Collections.Generic;

#if !CORE_ONLY
using UnityEngine;
#endif

namespace KingdomEnhancedMod;

// One receipt authorizes one exact native tool life and one Promote attempt.
internal sealed class HeavyShieldIssuedToolLedger
{
    internal readonly record struct Key(long Pointer, int InstanceId, long World);
    internal readonly record struct Lease(Key Key, long Life, Guid Issue);
    private sealed class Entry
    {
        internal Lease Lease;
        internal bool Attempted;
    }

    private readonly Dictionary<long, Entry> _byPointer = new();
    private readonly HashSet<Guid> _issues = new();
    private readonly HashSet<Guid> _restoredInWorld = new();
    private long _nextLife;
    private long _world;
    private const int MaxIssuesPerSession = 4096;

    internal void SetWorld(long world)
    {
        if (_world == world) return;
        _world = world;
        _byPointer.Clear();
        _restoredInWorld.Clear();
    }

    internal bool Register(Key key, Guid issue, out Lease lease)
    {
        lease = default;
        if (key.Pointer == 0 || key.InstanceId == 0 || key.World == 0 || key.World != _world
            || issue == Guid.Empty || _issues.Count >= MaxIssuesPerSession || _issues.Contains(issue)
            || _byPointer.ContainsKey(key.Pointer)) return false;
        lease = new Lease(key, ++_nextLife, issue);
        _byPointer.Add(key.Pointer, new Entry { Lease = lease });
        _issues.Add(issue);
        return true;
    }

    internal bool IsIssued(Key key)
        => _byPointer.TryGetValue(key.Pointer, out var entry)
           && !entry.Attempted && entry.Lease.Key == key && key.World == _world;

    // Exact native snapshot restoration is a different authority than issuing
    // a second paid shield. The same receipt may be re-bound once in a new world.
    internal bool RegisterRestored(Key key, Guid issue, out Lease lease)
    {
        lease = default;
        if (key.Pointer == 0 || key.InstanceId == 0 || key.World == 0 || key.World != _world
            || issue == Guid.Empty || _byPointer.ContainsKey(key.Pointer)
            || !_restoredInWorld.Add(issue)) return false;
        lease = new Lease(key, ++_nextLife, issue);
        _byPointer.Add(key.Pointer, new Entry { Lease = lease });
        _issues.Add(issue);
        return true;
    }

    internal bool TryBegin(Key key, out Lease lease)
    {
        lease = default;
        if (!IsIssued(key)) return false;
        var entry = _byPointer[key.Pointer];
        entry.Attempted = true; // reentrant or failed native Promote cannot replay
        lease = entry.Lease;
        return true;
    }

    internal bool IsSameLife(Lease lease)
        => lease.Key.World == _world
           && _byPointer.TryGetValue(lease.Key.Pointer, out var entry)
           && entry.Lease == lease;

    internal void Revoke(Key key)
    {
        if (_byPointer.TryGetValue(key.Pointer, out var entry) && entry.Lease.Key == key)
            _byPointer.Remove(key.Pointer);
    }

    internal void Recycle(long pointer, int instanceId)
    {
        if (_byPointer.TryGetValue(pointer, out var entry)
            && entry.Lease.Key.InstanceId == instanceId)
            _byPointer.Remove(pointer);
    }

    internal void Spawn(long pointer, bool freshLife)
    {
        // FastSpawn may hand back a cached active object. Only a witnessed new
        // native pool life invalidates the previous tool at this address.
        if (freshLife) _byPointer.Remove(pointer);
    }
}

#if !CORE_ONLY
// Called by the existing native Promote/Pool patch owners. No duplicate hooks.
internal static class HeavyShieldPromotionBridge
{
    internal sealed class PromotionState
    {
        internal PromotionState Previous;
        internal HeavyShieldIssuedToolLedger.Lease Lease;
        internal HeavyShieldCareerHandle Career;
        internal HeavyShieldCampaignToken Campaign;
        internal Character Source;
        internal IntPtr ToolRoot;
        internal bool Engaged;
        internal bool Consumed;
        internal bool Closed;
    }

    private static readonly HeavyShieldIssuedToolLedger Issued = new();
    private static PromotionState _active;
    internal static bool ShieldPromotionInProgress => _active != null && _active.Engaged && !_active.Closed;
    internal static bool IsConsumingRoot(GameObject root)
        => root != null && _active != null && _active.Engaged && !_active.Closed
           && _active.ToolRoot == root.Pointer;

    internal static bool MatchesActiveProof(in HeavyShieldCareerHandle handle, Character source)
    {
        var active = _active;
        if (active == null || !active.Engaged || active.Closed || source == null
            || active.Career != handle || active.Source == null
            || active.Source.Pointer != source.Pointer
            || !HeavyShieldIdentity.ValidateCampaign(active.Campaign)
            || (!active.Consumed && !Issued.IsSameLife(active.Lease))) return false;
        return true;
    }

    internal static bool RegisterIssuedTool(DroppableTool tool, Guid issue, bool restored = false)
    {
        try
        {
            if (!NativeBow(tool) || tool.pickedUp || !InCurrentWorld(tool.gameObject)
                || MusketeerIdentity.IsGun(tool)) return false;
            var key = KeyOf(tool.gameObject);
            Issued.SetWorld(key.World);
            return restored ? Issued.RegisterRestored(key, issue, out _)
                : Issued.Register(key, issue, out _);
        }
        catch { return false; }
    }

    internal static void RevokeIssuedTool(DroppableTool tool)
    {
        try { if (tool?.gameObject != null) Issued.Revoke(KeyOf(tool.gameObject)); }
        catch { }
    }

    // Prefix captures this one call and spends its lease. Native tool overload
    // runs unchanged. All other postfixes run while this scope is still active.
    internal static void Before(Character source, DroppableTool tool, out PromotionState state)
    {
        state = new PromotionState { Previous = _active };
        _active = state;
        try
        {
            if (HeavyShieldIdentity.Current == null || HeavyShieldIdentity.Current.Unknown
                || HeavyShieldIdentity.Current.StageFault
                || MusketeerIdentity.GunPromotionInProgress || source == null
                || source.GetComponent<Peasant>() == null || !NativeBow(tool)
                || !InCurrentWorld(source.gameObject) || !tool.pickedUp
                || !HeavyShieldIdentity.TryGetPaidBow(tool, out var career)
                || MusketeerIdentity.IsMarked(source.gameObject)
                || HeroRecruitment.HasPurchasedCareer(source)
                || MusketeerIdentity.IsGun(tool)) return;
            if (!HeavyShieldIdentity.TryGetCampaign(out var campaign)) return;
            var key = KeyOf(tool.gameObject);
            Issued.SetWorld(key.World);
            if (!Issued.TryBegin(key, out var lease) || lease.Issue != career.Receipt) return;
            state.Lease = lease;
            state.Career = career;
            state.Campaign = campaign;
            state.Source = source;
            state.ToolRoot = tool.gameObject.Pointer;
            state.Engaged = true;
        }
        catch { }
    }

    internal static void After(Character result, PromotionState state)
    {
        if (state == null || !state.Engaged || state.Closed
            || !ReferenceEquals(_active, state)) return;
        try
        {
            if (result == null || result.gameObject == null
                || result.GetComponent<Archer>() == null || !InCurrentWorld(result.gameObject)
                || MusketeerIdentity.IsUnit(result.GetComponent<Archer>())
                || (!state.Consumed && !Issued.IsSameLife(state.Lease))) return;
            HeavyShieldIdentity.AcceptNativePromotion(state.Career, state.Source, result);
        }
        catch { }
    }

    // Existing owner calls from finalizer, including exception path. The scope
    // remains visible through every Promote postfix and is never leaked.
    internal static void Finally(PromotionState state)
    {
        if (state == null || state.Closed) return;
        state.Closed = true;
        if (ReferenceEquals(_active, state)) _active = state.Previous;
    }

    internal static void OnPoolDespawn(GameObject root, float delay)
    {
        if (root == null || delay > 0f) return;
        try
        {
            if (_active != null && _active.Engaged && !_active.Closed
                && _active.ToolRoot == root.Pointer
                && root.GetInstanceID() == _active.Lease.Key.InstanceId)
                _active.Consumed = true;
            Issued.Recycle(root.Pointer.ToInt64(), root.GetInstanceID());
        }
        catch { }
    }

    internal static void OnPoolSpawn(GameObject root, bool freshLife)
    {
        if (root == null || !freshLife) return;
        try { Issued.Spawn(root.Pointer.ToInt64(), true); }
        catch { }
    }

    private static bool NativeBow(DroppableTool tool)
    {
        if (tool == null || tool.gameObject == null || !tool.CompareTag("Bow")) return false;
        var pool = Pool.GetPoolFromPrefabInstance(tool.gameObject);
        return pool != null && pool.prefab != null && pool.prefab.name == "ToolBow";
    }

    private static bool InCurrentWorld(GameObject root)
    {
        try
        {
            var layer = Managers.Inst?.world?.gameLayer;
            return root != null && layer != null && layer.gameObject.activeInHierarchy
                && root.activeInHierarchy;
        }
        catch { return false; }
    }

    private static HeavyShieldIssuedToolLedger.Key KeyOf(GameObject root)
    {
        var layer = Managers.Inst?.world?.gameLayer;
        return new(root.Pointer.ToInt64(), root.GetInstanceID(),
            layer == null ? 0 : layer.Pointer.ToInt64());
    }
}
#endif
