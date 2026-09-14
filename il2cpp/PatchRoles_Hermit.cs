using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Host-owned hermit pickup policy receipts, registered by long native lifecycle methods.
/// Never detour the short CanBePickedUpByEnemy getter. Only CurrentEnemyPolicy is changed;
/// general pickup, native reset policy, damage, mounting, and networking remain native-owned.
/// </summary>
public static class PatchRoles_Hermit
{
    private sealed class Receipt
    {
        internal Droppable Droppable;
        internal Hermit Hermit;
        internal GameObject Object;
        internal IntPtr DroppablePtr, HermitPtr, ObjectPtr;
        internal int DroppableId, HermitId, ObjectId;
        internal bool Bound, Owned, Contested;
        internal IntPtr WorldPtr, LayerPtr;
        internal int Scene;
        internal PickUpPolicy Original;
    }

    private struct Scope
    {
        internal Transform Layer;
        internal IntPtr WorldPtr, LayerPtr;
        internal int Scene;
    }

    private static readonly Dictionary<IntPtr, Receipt> Tracked = new();
    private static readonly List<IntPtr> Work = new();
    private static bool _dirty, _lastEnabled, _lastAuthority, _lastHasScope, _loggedProtection, _loggedFailure;
    private static IntPtr _lastWorld, _lastLayer;
    private static int _lastScene;
    private static float _nextCheck;

    internal static void OnEnabled(Droppable droppable)
    {
        try
        {
            if (droppable == null || droppable.gameObject == null) return;
            GameObject go = droppable.gameObject;
            Hermit hermit = go.GetComponent<Hermit>();
            if (hermit == null) return; // Real same-object component, never object names or children.
            IntPtr pointer = droppable.Pointer;
            if (pointer == IntPtr.Zero) return;
            if (!Tracked.TryGetValue(pointer, out Receipt receipt) || !SameIdentity(receipt, droppable, hermit, go))
            {
                Tracked[pointer] = new Receipt
                {
                    Droppable = droppable, Hermit = hermit, Object = go,
                    DroppablePtr = pointer, HermitPtr = hermit.Pointer, ObjectPtr = go.Pointer,
                    DroppableId = droppable.GetInstanceID(), HermitId = hermit.GetInstanceID(), ObjectId = go.GetInstanceID()
                };
            }
            // Duplicate OnEnable keeps the original receipt, including a suspended ownership claim.
            // Disabled/client registrations remain available for later host/config changes.
            _dirty = true;
            Tick();
        }
        catch (Exception ex) { LogFailure(ex); }
    }

    internal static void OnDisabled(Droppable droppable)
    {
        try
        {
            if (droppable == null) return;
            IntPtr pointer = droppable.Pointer;
            if (Tracked.TryGetValue(pointer, out Receipt receipt)
                && droppable.GetInstanceID() == receipt.DroppableId)
                Tracked.Remove(pointer);
            // Native Droppable.OnDisable resets its own enemy policy; do not restore here.
        }
        catch (Exception ex) { LogFailure(ex); }
    }

    /// <summary>Existing main-thread ModPanel.Update calls this; no new driver or scene scan.</summary>
    public static void Tick()
    {
        if (Tracked.Count == 0) return;
        try
        {
            bool enabled = ModConfig.Enabled != null && ModConfig.Enabled.Value;
            bool authority = NetworkBigBoss.HasWorldAuth;
            bool hasScope = TryScope(out Scope scope);
            bool changed = enabled != _lastEnabled || authority != _lastAuthority || hasScope != _lastHasScope
                || (hasScope && (scope.WorldPtr != _lastWorld || scope.LayerPtr != _lastLayer || scope.Scene != _lastScene));
            float now = Time.unscaledTime;
            if (!_dirty && !changed && now < _nextCheck) return;
            _dirty = false;
            _lastEnabled = enabled; _lastAuthority = authority;
            _lastHasScope = hasScope;
            // A temporary unavailable/loading context must not erase the identity of a known paused world.
            if (hasScope) { _lastWorld = scope.WorldPtr; _lastLayer = scope.LayerPtr; _lastScene = scope.Scene; }
            _nextCheck = now + 0.5f;

            Work.Clear();
            Work.AddRange(Tracked.Keys);
            foreach (IntPtr pointer in Work)
            {
                if (!Tracked.TryGetValue(pointer, out Receipt receipt)) continue;
                try
                {
                    if (!Process(receipt, hasScope, scope, enabled, authority)) Tracked.Remove(pointer);
                }
                catch (Exception ex) { LogFailure(ex); }
            }
        }
        catch (Exception ex) { LogFailure(ex); }
    }

    private static bool TryScope(out Scope scope)
    {
        scope = default;
        Managers managers = Managers.Inst;
        World world = managers != null ? managers.world : null;
        Game game = managers != null ? managers.game : null;
        Transform layer = world != null ? world.gameLayer : null;
        if (world == null || game == null || layer == null) return false;
        IntPtr worldPtr = world.Pointer, layerPtr = layer.Pointer;
        int scene = layer.gameObject.scene.handle;
        Game.State state = game.state;
        bool knownPause = state == Game.State.Menu && worldPtr == _lastWorld
            && layerPtr == _lastLayer && scene == _lastScene;
        if (state != Game.State.Playing && state != Game.State.NetworkClientPlaying && !knownPause) return false;
        scope = new Scope { Layer = layer, WorldPtr = worldPtr, LayerPtr = layerPtr, Scene = scene };
        return true;
    }

    // False retires a stale generation without writing to it. Pending initial attachment is retained.
    private static bool Process(Receipt receipt, bool hasScope, Scope scope, bool enabled, bool authority)
    {
        if (!SameIdentity(receipt, receipt.Droppable, receipt.Hermit, receipt.Object)
            || !receipt.Object.activeInHierarchy) return false;
        // Missing context is not proof of departure: retain Original without writing until scope returns.
        if (!hasScope) return true;
        GameObject go = receipt.Object;
        bool inScope = go.scene.handle == scope.Scene && go.transform.IsChildOf(scope.Layer);
        if (receipt.Bound)
        {
            if (!inScope || receipt.WorldPtr != scope.WorldPtr || receipt.LayerPtr != scope.LayerPtr
                || receipt.Scene != scope.Scene) return false;
        }
        else
        {
            // Pool enable can precede parenting. A different loaded scene is already outside this world.
            if (go.scene.handle != scope.Scene) return false;
            if (!inScope) return true;
            receipt.Bound = true;
            receipt.WorldPtr = scope.WorldPtr; receipt.LayerPtr = scope.LayerPtr; receipt.Scene = scope.Scene;
        }

        // Authority loss suspends the receipt. Never write native state or forget Original on a client.
        if (!authority) return true;
        PickUpPolicy current = receipt.Droppable.CurrentEnemyPolicy;
        if (receipt.Owned)
        {
            if (current != PickUpPolicy.Nobody)
            {
                receipt.Owned = false;
                receipt.Contested = true; // External policy owns this generation; never overwrite it later.
            }
            else if (!enabled)
            {
                receipt.Droppable.CurrentEnemyPolicy = receipt.Original;
                receipt.Owned = false;
            }
            return true;
        }
        if (!enabled || receipt.Contested
            || (current != PickUpPolicy.Anybody && current != PickUpPolicy.EnemyOnly)) return true;
        receipt.Original = current;
        receipt.Owned = true;
        receipt.Droppable.CurrentEnemyPolicy = PickUpPolicy.Nobody;
        if (!_loggedProtection)
        {
            _loggedProtection = true;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Roles] Hermit enemy pickup protected: id="
                + receipt.ObjectId + " original=" + (int)current + " current=" + (int)PickUpPolicy.Nobody);
        }
        return true;
    }

    private static bool SameIdentity(Receipt receipt, Droppable droppable, Hermit hermit, GameObject go)
    {
        return droppable != null && hermit != null && go != null
            && droppable.Pointer == receipt.DroppablePtr && hermit.Pointer == receipt.HermitPtr
            && go.Pointer == receipt.ObjectPtr && droppable.GetInstanceID() == receipt.DroppableId
            && hermit.GetInstanceID() == receipt.HermitId && go.GetInstanceID() == receipt.ObjectId
            && droppable.gameObject != null && droppable.gameObject.Pointer == receipt.ObjectPtr
            && hermit.gameObject != null && hermit.gameObject.Pointer == receipt.ObjectPtr;
    }

    private static void LogFailure(Exception ex)
    {
        if (_loggedFailure) return;
        _loggedFailure = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[Roles] Hermit pickup receipt deferred: " + ex.GetType().Name); }
        catch { }
    }
}

[HarmonyPatch(typeof(Droppable), nameof(Droppable.OnEnable))]
internal static class Droppable_OnEnable_HermitPickupPolicy_Patch
{
    [HarmonyPostfix]
    internal static void Postfix(Droppable __instance) => PatchRoles_Hermit.OnEnabled(__instance);
}

[HarmonyPatch(typeof(Droppable), nameof(Droppable.OnDisable))]
internal static class Droppable_OnDisable_HermitPickupPolicy_Patch
{
    [HarmonyPrefix]
    internal static void Prefix(Droppable __instance) => PatchRoles_Hermit.OnDisabled(__instance);
}
