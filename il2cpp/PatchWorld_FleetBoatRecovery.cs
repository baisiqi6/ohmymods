using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Restores missing Call of Olympus fleet-boat ownership from the four boat-granting quests.
/// Active boats, standby count, and the captured carry-forward count are alternate lifecycle
/// representations of one ownership total; they are deliberately never added together.
/// </summary>
[HarmonyPatch(typeof(CampaignSaveData), nameof(CampaignSaveData.ApplyToScene))]
public static class PatchWorld_FleetBoatRecovery
{
    private const int MaxFleetBoats = 4;
    private static bool _berthCoordinatorRegistered;

    public sealed class ApplyState
    {
        public bool Eligible;
        public int CapturedCarry;
    }

    [HarmonyPrefix]
    public static void Prefix(CampaignSaveData __instance, out ApplyState __state)
    {
        __state = new ApplyState();
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth
            || !IsEligibleCampaign(__instance))
        {
            return;
        }

        __state.Eligible = true;
        try
        {
            CampaignSaveData.CarryForward carry = __instance.carryForward;
            // ApplyCarryForward applies fleet boats for every present group, including future
            // enum values; the group itself must not narrow the ownership safety net.
            if (carry != null && carry.present)
            {
                __state.CapturedCarry = Mathf.Clamp(carry.numFleetBoats, 0, MaxFleetBoats);
            }
        }
        catch
        {
            // Prefix capture is only a cleared-carry safety net. Quest ownership remains usable.
            __state.CapturedCarry = 0;
        }
    }

    [HarmonyPostfix]
    public static void Postfix(CampaignSaveData __instance, ApplyState __state)
    {
        if (__state == null || !__state.Eligible || !ModConfig.Enabled.Value
            || !NetworkBigBoss.HasWorldAuth || !IsEligibleCampaign(__instance))
        {
            return;
        }

        int expected = 0;
        int active = 0;
        int standby = 0;
        int desired = 0;
        int materialized = 0;
        int missing = 0;
        int recovered = 0;
        string mode = "unchanged";
        Exception failure = null;

        try
        {
            Managers managers = Managers.Inst;
            Kingdom kingdom = managers?.kingdom;
            World world = managers?.world;
            if (kingdom == null || world == null || world.gameLayer == null)
            {
                mode = "not-ready";
                return;
            }

            expected = CountExpectedOwnership();
            active = CountActiveBoats(kingdom, world.gameLayer);
            standby = Mathf.Clamp(kingdom.NumFleetBoatsOnStandby, 0, MaxFleetBoats);
            desired = Mathf.Clamp(Mathf.Max(expected, __state.CapturedCarry), 0, MaxFleetBoats);

            // This mirrors PopulateCarryForward's source priority. Never sum lifecycle forms.
            materialized = active > 0 ? active : standby;
            missing = Mathf.Max(0, desired - materialized);
            if (missing == 0)
            {
                mode = "complete";
                return;
            }

            // A pre-existing mixed representation is anomalous. Do not mutate either side and
            // risk making the next native PopulateCarryForward silently ignore more ownership.
            if (active > 0 && standby > 0)
            {
                mode = "mixed-existing-deferred";
                return;
            }

            // Once standby exists it remains the sole representation for this scene. Promoting
            // it is idempotent and intentionally does not also spawn active instances.
            if (standby > 0)
            {
                kingdom._numFleetBoatsOnStandby = desired;
                recovered = desired - standby;
                mode = "standby-promoted";
                return;
            }

            bool riverless = managers.game?.currentLevelConfig == null
                || managers.game.currentLevelConfig.riverless;
            if (riverless)
            {
                if (active == 0)
                {
                    kingdom._numFleetBoatsOnStandby = desired;
                    recovered = desired;
                    mode = "standby-riverless";
                }
                else
                {
                    mode = "active-riverless-deferred";
                }
                return;
            }

            if (!TryGetSpawnContext(managers, kingdom, world, out FleetBoat prefab,
                    out Transform parent, out Vector3 spawnPosition, out string blockedReason))
            {
                if (active == 0)
                {
                    kingdom._numFleetBoatsOnStandby = desired;
                    recovered = desired;
                    mode = "standby-" + blockedReason;
                }
                else
                {
                    mode = "active-" + blockedReason + "-deferred";
                }
                return;
            }

            mode = active > 0 ? "active-recovered" : "spawned-from-zero";
            for (int i = 0; i < missing; i++)
            {
                FleetBoat spawned = null;
                try
                {
                    spawned = Pool.Spawn(prefab,
                        spawnPosition + new Vector3(i * 0.5f, 0f, 0f),
                        Quaternion.identity, parent, true);
                    if (!IsRegisteredActiveBoat(spawned, kingdom, parent))
                    {
                        if (spawned != null && spawned.gameObject != null)
                        {
                            Pool.Despawn(spawned.gameObject, true);
                        }
                        break;
                    }
                }
                catch
                {
                    if (spawned != null && spawned.gameObject != null)
                    {
                        try { Pool.Despawn(spawned.gameObject, true); }
                        catch { /* Best-effort cleanup of an unregistered pool instance. */ }
                    }
                    break;
                }
            }

            int finalActive = CountActiveBoats(kingdom, parent);
            recovered = Mathf.Max(0, finalActive - active);
            if (active == 0 && finalActive == 0)
            {
                // Pool validation succeeded but native registration did not. With no active
                // representation at all, standby is the only lossless retry path.
                kingdom._numFleetBoatsOnStandby = desired;
                recovered = desired;
                mode = "standby-spawn-failed";
            }
            else if (finalActive < desired)
            {
                // At least one active boat materialized. Never add standby beside it; the next
                // ApplyToScene will retry only the remaining active gap.
                mode += "-partial";
            }
        }
        catch (Exception e)
        {
            failure = e;
            mode = "failed-" + e.GetType().Name;
        }
        finally
        {
            string summary = "[FleetBoatRecovery] expected=" + expected
                + " active=" + active
                + " standby=" + standby
                + " carry=" + __state.CapturedCarry
                + " desired=" + desired
                + " materialized=" + materialized
                + " missing=" + missing
                + " recovered=" + recovered
                + " mode=" + mode;
            if (failure == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(summary);
            }
            else
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(summary);
            }

            TryScheduleBerthNormalization(__instance);
        }
    }

    private static void TryScheduleBerthNormalization(CampaignSaveData campaign)
    {
        try
        {
            if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth
                || !IsEligibleCampaign(campaign)) return;

            Managers managers = Managers.Inst;
            World world = managers?.world;
            Kingdom kingdom = managers?.kingdom;
            if (world == null || world.gameObject == null || world.gameLayer == null
                || kingdom == null) return;

            if (!_berthCoordinatorRegistered)
            {
                if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(FleetBoatBerthCoordinator)))
                    ClassInjector.RegisterTypeInIl2Cpp(typeof(FleetBoatBerthCoordinator));
                _berthCoordinatorRegistered = true;
            }

            FleetBoatBerthCoordinator coordinator =
                world.GetComponent<FleetBoatBerthCoordinator>();
            if (coordinator == null)
                coordinator = world.gameObject.AddComponent<FleetBoatBerthCoordinator>();
            if (coordinator != null)
                coordinator.Begin(campaign, kingdom, world, world.gameLayer);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[FleetBoatBerth] boats=0 updated=0 waited=0 mode=schedule-failed-"
                + e.GetType().Name);
        }
    }

    private static int CountExpectedOwnership()
    {
        int count = 0;
        if (QuestManager.GetQuestCompleted(QuestType.GodIslandAthena)) count++;
        if (QuestManager.GetQuestCompleted(QuestType.GodIslandArtemis)) count++;
        if (QuestManager.GetQuestCompleted(QuestType.GodIslandHephaestus)) count++;
        if (QuestManager.GetQuestCompleted(QuestType.GodIslandHermes)) count++;
        return Mathf.Clamp(count, 0, MaxFleetBoats);
    }

    internal static bool IsEligibleCampaign(CampaignSaveData campaign)
    {
        GlobalSaveData global = GlobalSaveData.loaded;
        BiomeHolder biomeHolder = BiomeHolder.Inst;
        return campaign != null && global != null && !global.InChallenge
            && campaign.BiomeIndex == BiomeHolder.GreeceBiomeIndex
            && biomeHolder != null && biomeHolder.BiomeIndex == BiomeHolder.GreeceBiomeIndex;
    }

    private static int CountActiveBoats(Kingdom kingdom, Transform sceneRoot)
    {
        if (kingdom.FleetBoats == null || sceneRoot == null) return 0;

        var seen = new HashSet<IntPtr>();
        int count = 0;
        for (int i = 0; i < kingdom.FleetBoats.Count; i++)
        {
            FleetBoat boat = kingdom.FleetBoats[i];
            if (!IsSceneBoat(boat, sceneRoot) || !seen.Add(boat.Pointer)) continue;
            count++;
            if (count == MaxFleetBoats) break;
        }
        return count;
    }

    internal static bool IsSceneBoat(FleetBoat boat, Transform sceneRoot)
    {
        return boat != null && boat.gameObject != null && boat.gameObject.activeInHierarchy
            && boat.transform != null && sceneRoot != null && boat.transform.IsChildOf(sceneRoot);
    }

    private static bool IsRegisteredActiveBoat(FleetBoat boat, Kingdom kingdom, Transform sceneRoot)
    {
        if (!IsSceneBoat(boat, sceneRoot) || kingdom?.FleetBoats == null) return false;
        for (int i = 0; i < kingdom.FleetBoats.Count; i++)
        {
            FleetBoat registered = kingdom.FleetBoats[i];
            if (registered != null && registered.Pointer == boat.Pointer) return true;
        }
        return false;
    }

    private static bool TryGetSpawnContext(Managers managers, Kingdom kingdom, World world,
        out FleetBoat prefab, out Transform parent, out Vector3 spawnPosition, out string reason)
    {
        prefab = null;
        parent = world?.gameLayer;
        spawnPosition = Vector3.zero;
        reason = "not-ready";

        PoolManager poolManager = managers?.pools;
        BiomeHolder biomeHolder = BiomeHolder.Inst;
        if (poolManager == null || parent == null || kingdom?.boatSailPosition == null
            || biomeHolder == null || biomeHolder.curBiomeAssets == null)
        {
            return false;
        }

        prefab = biomeHolder.curBiomeAssets.fleetBoatPrefab;
        if (prefab == null || prefab.gameObject == null)
        {
            reason = "no-prefab";
            return false;
        }

        Pool pool = Pool.GetPoolFromPrefabAsset(prefab.gameObject);
        if (pool == null || !pool.sync || pool.syncID <= 0
            || poolManager.cachedSyncIdPoolPairs == null
            || !poolManager.cachedSyncIdPoolPairs.ContainsKey((int)pool.syncID))
        {
            reason = "no-synced-pool";
            return false;
        }

        Pool mapped = poolManager.cachedSyncIdPoolPairs[(int)pool.syncID];
        if (mapped == null || mapped.Pointer != pool.Pointer)
        {
            reason = "pool-mismatch";
            return false;
        }

        float sailX = kingdom.boatSailPosition.transform.position.x;
        if (float.IsNaN(sailX) || float.IsInfinity(sailX))
        {
            reason = "invalid-position";
            return false;
        }

        spawnPosition = new Vector3(sailX + 5f, 0f, 0f);
        reason = "ready";
        return true;
    }
}

/// <summary>
/// One-shot, authority-only berth refresh after ApplyToScene. All job state is static because
/// injected IL2CPP MonoBehaviours must not depend on managed generic instance-field layout.
/// </summary>
public sealed class FleetBoatBerthCoordinator : MonoBehaviour
{
    private const float PollInterval = 0.25f;
    private const float Timeout = 12f;

    private sealed class BoatSnapshot
    {
        public FleetBoat Boat;
        public IntPtr Pointer;
        public int BoatNumber;
    }

    private static readonly List<BoatSnapshot> Boats = new();
    private static FleetBoatBerthCoordinator _instance;
    private static CampaignSaveData _campaign;
    private static Kingdom _kingdom;
    private static World _world;
    private static Transform _sceneRoot;
    private static IntPtr _campaignPointer;
    private static IntPtr _kingdomPointer;
    private static IntPtr _worldPointer;
    private static IntPtr _sceneRootPointer;
    private static bool _pending;
    private static int _generation;
    private static int _activeGeneration;
    private static float _startedAt;
    private static float _deadline;
    private static float _nextPollAt;
    private static string _waitReason;

    public FleetBoatBerthCoordinator(IntPtr ptr) : base(ptr) { }

    public void Begin(CampaignSaveData campaign, Kingdom kingdom, World world, Transform sceneRoot)
    {
        if (campaign == null || kingdom == null || world == null || sceneRoot == null) return;

        var candidates = new List<BoatSnapshot>();
        var seen = new HashSet<IntPtr>();
        if (kingdom.FleetBoats != null)
        {
            for (int i = 0; i < kingdom.FleetBoats.Count; i++)
            {
                FleetBoat boat = kingdom.FleetBoats[i];
                if (!PatchWorld_FleetBoatRecovery.IsSceneBoat(boat, sceneRoot)
                    || !seen.Add(boat.Pointer)) continue;
                candidates.Add(new BoatSnapshot
                {
                    Boat = boat,
                    Pointer = boat.Pointer,
                    BoatNumber = boat._boatNumber
                });
                if (candidates.Count > 4) break;
            }
        }

        if (candidates.Count < 2) return;
        if (candidates.Count > 4)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[FleetBoatBerth] boats=" + candidates.Count
                + " updated=0 waited=0 mode=cancelled-count");
            return;
        }

        if (_pending)
        {
            if (_campaignPointer == campaign.Pointer && _kingdomPointer == kingdom.Pointer
                && _worldPointer == world.Pointer && _sceneRootPointer == sceneRoot.Pointer
                && SameBoatSet(candidates))
            {
                return;
            }
            Finish("replaced", 0);
        }

        _instance = this;
        _campaign = campaign;
        _kingdom = kingdom;
        _world = world;
        _sceneRoot = sceneRoot;
        _campaignPointer = campaign.Pointer;
        _kingdomPointer = kingdom.Pointer;
        _worldPointer = world.Pointer;
        _sceneRootPointer = sceneRoot.Pointer;
        Boats.Clear();
        Boats.AddRange(candidates);
        _activeGeneration = ++_generation;
        _startedAt = Time.unscaledTime;
        _deadline = _startedAt + Timeout;
        _nextPollAt = _startedAt;
        _waitReason = "not-ready";
        _pending = true;
    }

    private void Update()
    {
        if (!_pending || _instance != this || Time.unscaledTime < _nextPollAt) return;
        _nextPollAt = Time.unscaledTime + PollInterval;

        if (!ValidateJobIdentity(out string cancelReason))
        {
            Finish(cancelReason, 0);
            return;
        }

        if (!TryValidateBoats(out string boatReason))
        {
            if (boatReason == "unsafe-state" || boatReason == "base-not-ready")
            {
                _waitReason = boatReason;
                if (Time.unscaledTime < _deadline) return;
                Finish("timeout-" + _waitReason, 0);
                return;
            }

            Finish(boatReason, 0);
            return;
        }

        int updated = 0;
        for (int i = 0; i < Boats.Count; i++)
        {
            FleetBoat boat = Boats[i].Boat;
            try
            {
                // Preserve the native current side. In 2.4, true forbids falling back to the
                // common BoatSailPosition; the ready side base and BoatNumber provide spacing.
                boat.UpdateBase(true);
                updated++;
            }
            catch
            {
                // Continue so one stale wrapper cannot prevent the other valid boats moving.
            }
        }

        Finish(updated == Boats.Count ? "updated" : "updated-partial", updated);
    }

    private static bool ValidateJobIdentity(out string reason)
    {
        reason = "cancelled";
        if (!ModConfig.Enabled.Value)
        {
            reason = "cancelled-disabled";
            return false;
        }
        if (_activeGeneration != _generation)
        {
            reason = "cancelled-generation";
            return false;
        }
        if (!NetworkBigBoss.HasWorldAuth)
        {
            reason = "cancelled-no-authority";
            return false;
        }
        if (!PatchWorld_FleetBoatRecovery.IsEligibleCampaign(_campaign))
        {
            reason = "cancelled-campaign";
            return false;
        }
        CampaignSaveData currentCampaign = CampaignSaveData.current;
        if (currentCampaign == null || currentCampaign.Pointer != _campaignPointer)
        {
            reason = "cancelled-campaign-replaced";
            return false;
        }

        Managers managers = Managers.Inst;
        if (managers == null || managers.kingdom == null || managers.world == null
            || managers.world.gameLayer == null
            || managers.kingdom.Pointer != _kingdomPointer
            || managers.world.Pointer != _worldPointer
            || managers.world.gameLayer.Pointer != _sceneRootPointer)
        {
            reason = "cancelled-scene";
            return false;
        }
        return true;
    }

    private static bool TryValidateBoats(out string reason)
    {
        reason = "ready";
        if (_kingdom == null || _sceneRoot == null || _kingdom.FleetBoats == null)
        {
            reason = "cancelled-scene";
            return false;
        }

        var numbers = new HashSet<int>();
        for (int i = 0; i < Boats.Count; i++)
        {
            BoatSnapshot snapshot = Boats[i];
            FleetBoat boat = snapshot.Boat;
            if (boat == null || boat.Pointer != snapshot.Pointer
                || !PatchWorld_FleetBoatRecovery.IsSceneBoat(boat, _sceneRoot)
                || !IsStillRegistered(boat))
            {
                reason = "cancelled-boat-inactive";
                return false;
            }
            if (boat._boatNumber != snapshot.BoatNumber || boat._boatNumber < 1
                || boat._boatNumber > 4 || !numbers.Add(boat._boatNumber))
            {
                reason = "cancelled-boat-identity";
                return false;
            }
            if (boat.Side != Side.Left && boat.Side != Side.Right)
            {
                reason = "cancelled-side";
                return false;
            }
            if (!HasReadyBase(_kingdom, boat.Side))
            {
                reason = "base-not-ready";
                return false;
            }

            StateMachine fsm = boat._fsm;
            if (fsm == null || boat._mover == null)
            {
                reason = "cancelled-not-ready";
                return false;
            }
            // The whole captured batch must be quiescent. GoToNewBase is the 2.4 successor
            // to FirstArrival, but UpdateBase is deferred until that transition reaches Idle.
            if (fsm.Current != FleetBoat.State.Idle)
            {
                reason = "unsafe-state";
                return false;
            }
        }
        return true;
    }

    private static bool HasReadyBase(Kingdom kingdom, Side side)
    {
        GameObject baseObject = null;
        try
        {
            PayableBorder border = kingdom.borderBanner != null
                ? kingdom.borderBanner[side] : null;
            if (border != null && border.gameObject != null && border.gameObject.activeInHierarchy)
                baseObject = border.gameObject;
            else if (kingdom.intactWall != null)
                baseObject = kingdom.intactWall[side];
        }
        catch
        {
            return false;
        }

        if (baseObject == null || !baseObject.activeInHierarchy || baseObject.transform == null)
            return false;
        float x = baseObject.transform.position.x;
        return !float.IsNaN(x) && !float.IsInfinity(x);
    }

    private static bool IsStillRegistered(FleetBoat boat)
    {
        for (int i = 0; i < _kingdom.FleetBoats.Count; i++)
        {
            FleetBoat registered = _kingdom.FleetBoats[i];
            if (registered != null && registered.Pointer == boat.Pointer) return true;
        }
        return false;
    }

    private static bool SameBoatSet(List<BoatSnapshot> candidates)
    {
        if (candidates.Count != Boats.Count) return false;
        for (int i = 0; i < candidates.Count; i++)
        {
            bool found = false;
            for (int j = 0; j < Boats.Count; j++)
            {
                if (candidates[i].Pointer != Boats[j].Pointer) continue;
                found = true;
                break;
            }
            if (!found) return false;
        }
        return true;
    }

    private static void Finish(string mode, int updated)
    {
        if (!_pending) return;
        float waited = Mathf.Max(0f, Time.unscaledTime - _startedAt);
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[FleetBoatBerth] boats=" + Boats.Count
            + " updated=" + updated
            + " waited=" + waited.ToString("F2")
            + " mode=" + mode);

        _pending = false;
        Boats.Clear();
        _campaign = null;
        _kingdom = null;
        _world = null;
        _sceneRoot = null;
        _campaignPointer = IntPtr.Zero;
        _kingdomPointer = IntPtr.Zero;
        _worldPointer = IntPtr.Zero;
        _sceneRootPointer = IntPtr.Zero;
        _waitReason = null;
    }

    private void OnDisable()
    {
        if (_instance == this && _pending)
            Finish("cancelled-world-disabled", 0);
        if (_instance == this) _instance = null;
    }
}
