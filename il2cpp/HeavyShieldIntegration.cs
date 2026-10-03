using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

// Thin callbacks owned by the existing save/lifecycle patches. No second detours,
// identity ledger, native payload mutation or payment authority live here.
internal static class HeavyShieldIntegration
{
    internal sealed class SaveScope
    {
        internal SaveScope Previous;
        internal HeavyShieldPersistence.SaveCapture Capture;
        internal bool Closed;
    }
    internal sealed class LoadScope
    {
        internal LoadScope Previous;
        internal HeavyShieldPersistence.LoadCapture Capture;
        internal bool Closed;
    }
    private static SaveScope _save;
    private static LoadScope _load;
    private static int _poolDepth;
    private static readonly HashSet<string> Faults = new();

    internal static void Fault(string point, Exception error)
    {
        if (Faults.Count >= 32 || !Faults.Add(point)) return;
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[HeavyShield] callback " + point + ": " + error.GetType().Name); }
        catch { }
    }

    internal static void BeginPoolSpawn() => _poolDepth++;
    internal static bool InPoolSpawn => _poolDepth > 0;
    internal static void EndPoolSpawn() { if (_poolDepth > 0) _poolDepth--; }
    internal static void ObserveArcherEnable(Archer archer)
    {
        try { if (_poolDepth > 0 && !HeavyShieldRuntime.CarrierMutationInProgress && archer != null)
            HeavyShieldIdentity.ObservePoolFreshLife(archer.gameObject, true); }
        catch (Exception e) { Fault("pool-enable", e); }
    }
    internal static void BeforePoolDespawn(GameObject root, float delay)
    {
        if (delay > 0f) return;
        try { HeavyShieldRuntime.BeforeNativePoolDespawn(root); }
        catch (Exception e) { Fault("pool-death", e); }
    }
    internal static void AfterPoolDespawn(GameObject root, float delay)
    {
        try { if (delay <= 0f && root != null && !root.activeInHierarchy
            && !HeavyShieldRuntime.HoldsNativeDemoteProof(root))
            HeavyShieldIdentity.ObservePoolDespawn(root, delay); }
        catch (Exception e) { Fault("pool-despawn", e); }
    }

    internal static void Tick()
    {
        try { HeavyShieldPersistence.TickBinding(); } catch (Exception e) { Fault("binding", e); }
        try { HeavyShieldRuntime.Tick(); } catch (Exception e) { Fault("runtime", e); }
        try { HeavyShieldShopShell.Tick(ModConfig.Enabled != null && ModConfig.Enabled.Value
            && ModConfig.HeavyShieldEnabled != null && ModConfig.HeavyShieldEnabled.Value); }
        catch (Exception e) { Fault("shop", e); }
    }

    internal static SaveScope BeginSave(int campaign, int land, int challenge)
    {
        try { HeavyShieldShopShell.CancelPendingTransactionsBeforeNativeSave(); }
        catch (Exception e) { Fault("save-cancel", e); }
        // A failed/disabled inner capture still masks an outer capture.
        var scope = new SaveScope { Previous = _save };
        _save = scope;
        try { scope.Capture = HeavyShieldPersistence.BeginNativeIslandSave(campaign, land, challenge); }
        catch (Exception e) { Fault("save-begin", e); }
        return scope;
    }

    internal static void ObserveId(Persistent root, string id)
    {
        try { if (_save?.Capture != null) HeavyShieldPersistence.ObserveNativeId(_save.Capture, root, id); }
        catch (Exception e) { Fault("save-id", e); }
    }

    internal static void ObserveMarker(IslandSaveData island)
    {
        try { if (_save?.Capture != null) HeavyShieldPersistence.ObserveNativeIslandCaptured(_save.Capture, island); }
        catch (Exception e) { Fault("save-marker", e); }
    }

    internal static void EndSave(SaveScope scope, bool normalReturn)
    {
        if (scope == null || scope.Closed) return;
        scope.Closed = true;
        try { if (scope.Capture != null) HeavyShieldPersistence.EndNativeIslandSave(scope.Capture,
            normalReturn && ReferenceEquals(_save, scope)); }
        catch (Exception e) { Fault("save-end", e); }
        finally { if (ReferenceEquals(_save, scope)) _save = scope.Previous; }
    }

    internal static LoadScope BeginLoad(IslandSaveData island)
    {
        var scope = new LoadScope { Previous = _load };
        _load = scope;
        try { scope.Capture = HeavyShieldPersistence.BeginNativeIslandLoad(island); }
        catch (Exception e) { Fault("load-begin", e); }
        return scope;
    }

    internal static void ObserveLoadRow(IslandSaveData.ObjectData row, Persistent root)
    {
        try { if (_load?.Capture != null) HeavyShieldPersistence.ObserveNativeLoadRow(_load.Capture, row, root); }
        catch (Exception e) { Fault("load-row", e); }
    }

    internal static HeavyShieldPersistence.CreateCapture BeginLoadRow(IslandSaveData.ObjectData row)
    {
        try { return HeavyShieldPersistence.BeginNativeLoadRow(_load?.Capture, row); }
        catch (Exception e) { Fault("load-row-begin", e); return null; }
    }

    internal static void EndLoadRow(HeavyShieldPersistence.CreateCapture scope)
    {
        try { if (scope != null) HeavyShieldPersistence.EndNativeLoadRow(scope); }
        catch (Exception e) { Fault("load-row-end", e); }
    }

    internal static void EndLoad(LoadScope scope, bool success)
    {
        if (scope == null || scope.Closed) return;
        scope.Closed = true;
        try { if (scope.Capture != null) HeavyShieldPersistence.EndNativeIslandLoad(scope.Capture,
            success && ReferenceEquals(_load, scope)); }
        catch (Exception e) { Fault("load-end", e); }
        finally { if (ReferenceEquals(_load, scope)) _load = scope.Previous; }
    }

    internal static HeavyShieldPersistence.PrepareCapture BeginPrepare(PrefsSaveData prefs)
    {
        try { return HeavyShieldPersistence.BeginNativePrefsPrepare(prefs); }
        catch (Exception e) { Fault("prefs-prepare-begin", e); return null; }
    }
    internal static void EndPrepare(HeavyShieldPersistence.PrepareCapture scope, bool normalReturn)
    {
        try { if (scope != null) HeavyShieldPersistence.EndNativePrefsPrepare(scope, normalReturn); }
        catch (Exception e) { Fault("prefs-prepare-end", e); }
    }
    internal static void BeforeCampaignMutation(GlobalSaveData global)
    {
        try { HeavyShieldPersistence.BeforeCampaignMutation(global); }
        catch (Exception e) { Fault("campaign-before", e); }
    }
    internal static void AfterCampaignCreated(GlobalSaveData global, CampaignSaveData campaign)
    {
        try { HeavyShieldPersistence.AfterCampaignCreated(global, campaign); }
        catch (Exception e) { Fault("campaign-created", e); }
    }
    internal static HeavyShieldPersistence.GenerationCapture BeginGeneration(CampaignSaveData campaign)
    {
        try { return HeavyShieldPersistence.BeginNativeGeneration(campaign); }
        catch (Exception e) { Fault("generation-begin", e); return null; }
    }
    internal static void EndGeneration(HeavyShieldPersistence.GenerationCapture scope, CampaignSaveData campaign, bool success)
    {
        try { if (scope != null) HeavyShieldPersistence.EndNativeGeneration(scope, campaign, success); }
        catch (Exception e) { Fault("generation-end", e); }
    }
}
