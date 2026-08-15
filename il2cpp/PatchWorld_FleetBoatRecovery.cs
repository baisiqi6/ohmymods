using System;
using System.Collections.Generic;
using HarmonyLib;
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

    private static bool IsEligibleCampaign(CampaignSaveData campaign)
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

    private static bool IsSceneBoat(FleetBoat boat, Transform sceneRoot)
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
