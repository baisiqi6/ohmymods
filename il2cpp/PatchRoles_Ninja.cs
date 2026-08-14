using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Cross-biome Ninja runtime support for Greece.
///
/// The Ninja prefab can be imported through Holder without importing the bamboo
/// biome's object-pool collection.  Its projectile and smoke routines both call
/// Pool.Spawn with instantiation disabled, so their native pool definitions must
/// be recreated during Holder initialization rather than on the attack hot path.
///
/// 2.4.0 resources.assets (Object Pools/bamboo) is the source of truth here:
/// - ThrowingStar: preload=0, sync=true, syncID=41, capacity=0, expendable=false
/// - Smokebomb:    preload=0, sync=false (serialized ID 43 is not registered)
/// </summary>
public static class PatchRoles_Ninja
{
    private const short THROWING_STAR_SYNC_ID = 41;
    private const short SMOKEBOMB_SERIALIZED_ID = 43;
    private static readonly string[] THICKET_ANCHOR_NAMES =
    {
        "KEM_NinjaHidingSpot_Left",
        "KEM_NinjaHidingSpot_Center",
        "KEM_NinjaHidingSpot_Right"
    };
    private static readonly float[] THICKET_ANCHOR_LOCAL_X = { -1.1f, 0f, 1.1f };
    private static bool _loggedThicketHookEntered;
    private static bool _loggedThicketAdded;
    private static bool _loggedThicketReused;

    public static void EnsureRuntimePoolsInGreece(Holder holder)
    {
        if (!ModConfig.Enabled.Value) return;
        if (BiomeHolder.Inst == null
            || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return;
        if (holder == null || holder.tagCharacterPairs == null) return;

        try
        {
            var managers = Managers.Inst;
            var poolManager = managers != null ? managers.pools : null;
            if (poolManager == null) return;

            Character character = null;
            if (!holder.tagCharacterPairs.TryGetValue("Ninja", out character)
                || character == null) return;

            Ninja ninjaPrefab = character.GetComponent<Ninja>();
            if (ninjaPrefab == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[Ninja] Holder['Ninja'] prefab has no Ninja component; runtime pools not registered");
                return;
            }

            // Fixed order is intentional.  Both peers execute this Holder postfix,
            // and the networked projectile uses its native deterministic pool ID.
            Arrow arrowPrefab = ninjaPrefab.arrowPrefab;
            GameObject throwingStar = arrowPrefab != null ? arrowPrefab.gameObject : null;
            if (throwingStar == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[Ninja] Holder['Ninja'].arrowPrefab is null; ThrowingStar pool not registered");
            }
            else
            {
                EnsurePool(
                    poolManager,
                    throwingStar,
                    sync: true,
                    syncId: THROWING_STAR_SYNC_ID,
                    label: "ThrowingStar");
            }

            GameObject smokebomb = ninjaPrefab.smokebombPrefab;
            if (smokebomb == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[Ninja] Holder['Ninja'].smokebombPrefab is null; Smokebomb pool not registered");
            }
            else
            {
                EnsurePool(
                    poolManager,
                    smokebomb,
                    sync: false,
                    syncId: SMOKEBOMB_SERIALIZED_ID,
                    label: "Smokebomb");
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Ninja] Runtime pool registration failed: " + e);
        }
    }

    private static void EnsurePool(
        PoolManager poolManager,
        GameObject prefab,
        bool sync,
        short syncId,
        string label)
    {
        if (poolManager == null || prefab == null) return;

        // A fixed network ID is safe only if it is either free or already belongs
        // to this exact prefab.  Never replace an unrelated native sync pool.
        if (sync && poolManager.cachedSyncIdPoolPairs != null
            && poolManager.cachedSyncIdPoolPairs.ContainsKey(syncId))
        {
            Pool byId = poolManager.cachedSyncIdPoolPairs[syncId];
            if (byId == null || byId.prefab != prefab)
            {
                string conflictingName = byId != null && byId.prefab != null
                    ? byId.prefab.name
                    : "<null>";
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[Ninja] Refusing " + label + " pool registration: syncID "
                    + syncId + " is already used by " + conflictingName);
                return;
            }
        }

        Pool pool = Pool.GetPoolFromPrefabAsset(prefab);
        bool created = pool == null;
        if (created)
        {
            DestroyOrphanPools(poolManager, prefab);
            pool = poolManager.CreatePoolFor(prefab);
            if (pool == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[Ninja] CreatePoolFor returned null for " + label);
                return;
            }
        }

        // Match the native bamboo definitions exactly.  In particular, smoke is
        // local visual state and must never be added to cachedSyncIdPoolPairs.
        pool.preload = 0;
        pool.sync = sync;
        pool.syncID = syncId;
        pool.capacity = 0;
        pool.expendable = false;

        if (poolManager.cachedPools != null && !poolManager.cachedPools.Contains(pool))
            poolManager.cachedPools.Add(pool);

        if (poolManager.cachedNamePoolPairs != null)
            poolManager.cachedNamePoolPairs[prefab.name] = pool;

        if (sync && poolManager.cachedSyncIdPoolPairs != null)
            poolManager.cachedSyncIdPoolPairs[syncId] = pool;

        if (created)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[Ninja] Registered " + (sync ? "synced" : "local")
                + " pool for " + label + (sync ? " (syncID=" + syncId + ")" : string.Empty));
        }
    }

    private static void DestroyOrphanPools(PoolManager poolManager, GameObject prefab)
    {
        Pool[] physicalPools = poolManager.GetComponentsInChildren<Pool>();
        for (int i = 0; i < physicalPools.Length; i++)
        {
            Pool physical = physicalPools[i];
            if (physical != null && physical.prefab == prefab
                && Pool.GetPoolFromPrefabAsset(prefab) == null)
            {
                UnityEngine.Object.Destroy(physical.gameObject);
            }
        }
    }

    public static void EnsureThicketHidingSpots(Grass grass)
    {
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return;
        if (BiomeHolder.Inst == null
            || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return;
        if (grass == null || grass._thicket == null) return;

        try
        {
            GameObject thicket = grass._thicket;
            var managers = Managers.Inst;
            var kingdom = managers != null ? managers.kingdom : null;
            float x = thicket.transform.position.x;
            string sideLabel = kingdom != null
                ? (x < kingdom.campfirePosition ? Side.Left : Side.Right).ToString()
                : "<unknown>";

            if (!_loggedThicketHookEntered)
            {
                _loggedThicketHookEntered = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[Ninja] World.AddThicket hook entered (side=" + sideLabel + ", x=" + x + ")");
            }

            bool added = false;
            bool reused = false;

            // Compatibility with the previous mod build, which placed one
            // HidingSpot directly on the thicket root.  Greece's native thicket
            // prefab has none, so an enabled root component is the legacy center
            // slot.  Keep it and add only the two offset child anchors: total 3.
            HidingSpot legacyRootSpot = thicket.GetComponent<HidingSpot>();
            Transform namedCenter = thicket.transform.Find(THICKET_ANCHOR_NAMES[1]);
            bool useLegacyRootAsCenter = legacyRootSpot != null
                && legacyRootSpot.enabled
                && namedCenter == null;

            if (useLegacyRootAsCenter)
            {
                reused |= ReRegisterExistingAnchorIfMissing(legacyRootSpot, kingdom);
                for (int i = 0; i < THICKET_ANCHOR_NAMES.Length; i += 2)
                {
                    bool anchorAdded;
                    HidingSpot anchor = EnsureChildAnchor(thicket, i, out anchorAdded);
                    added |= anchorAdded;
                    if (!anchorAdded)
                        reused |= ReRegisterExistingAnchorIfMissing(anchor, kingdom);
                }
            }
            else
            {
                // If a partial/new three-anchor layout coexists with the legacy
                // root component, disable only that old HidingSpot.  Its native
                // OnDisable unregisters/notifies safely; no other root component
                // is touched and a fourth hiding slot cannot survive.
                if (legacyRootSpot != null && legacyRootSpot.enabled)
                    legacyRootSpot.enabled = false;

                for (int i = 0; i < THICKET_ANCHOR_NAMES.Length; i++)
                {
                    bool anchorAdded;
                    HidingSpot anchor = EnsureChildAnchor(thicket, i, out anchorAdded);
                    added |= anchorAdded;
                    if (!anchorAdded)
                        reused |= ReRegisterExistingAnchorIfMissing(anchor, kingdom);
                }
            }

            if (added && !_loggedThicketAdded)
            {
                _loggedThicketAdded = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[Ninja] Prepared 3 Greece thicket hiding anchors"
                    + (useLegacyRootAsCenter ? " (legacy root used as center)" : string.Empty)
                    + " (side=" + sideLabel + ", x=" + x + ")");
            }
            if (reused && !_loggedThicketReused)
            {
                _loggedThicketReused = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[Ninja] Re-registered reused Greece thicket hiding anchors (3 anchors, side="
                    + sideLabel + ", x=" + x + ")");
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Ninja] Failed to prepare HidingSpots for spawned thicket: " + e);
        }
    }

    private static HidingSpot EnsureChildAnchor(GameObject thicket, int index, out bool added)
    {
        added = false;
        Transform anchorTransform = thicket.transform.Find(THICKET_ANCHOR_NAMES[index]);
        GameObject anchorObject;
        if (anchorTransform == null)
        {
            anchorObject = new GameObject(THICKET_ANCHOR_NAMES[index]);
            anchorTransform = anchorObject.transform;
            anchorTransform.SetParent(thicket.transform, false);
            added = true;
        }
        else
        {
            anchorObject = anchorTransform.gameObject;
        }

        // Normalize all three positions on every pool spawn.  World-space x stays
        // distinct, so Kingdom sorting and Ninja's native outside-wall filter work
        // independently for each single-occupancy HidingSpot.
        anchorTransform.localPosition = new Vector3(THICKET_ANCHOR_LOCAL_X[index], 0f, 0f);

        HidingSpot hidingSpot = anchorObject.GetComponent<HidingSpot>();
        if (hidingSpot == null)
        {
            // Do not register manually in the creation frame: native Start does it.
            hidingSpot = anchorObject.AddComponent<HidingSpot>();
            added = true;
        }
        return hidingSpot;
    }

    private static bool ReRegisterExistingAnchorIfMissing(HidingSpot hidingSpot, Kingdom kingdom)
    {
        if (hidingSpot == null || kingdom == null || !hidingSpot.isActiveAndEnabled) return false;

        float x = hidingSpot.transform.position.x;
        Side side = x < kingdom.campfirePosition ? Side.Left : Side.Right;
        var sideSpots = kingdom.GetHidingSpotList(side);
        if (sideSpots == null || sideSpots.Contains(hidingSpot)) return false;

        // Native HidingSpot.OnDisable unregisters and notifies the old Ninja but
        // retains _hider.  Clear that stale occupancy only when this pooled anchor
        // genuinely needs registration; never clear a still-registered occupant.
        hidingSpot.SetHider(null);
        kingdom.RegisterHidingSpot(hidingSpot);
        return true;
    }
}

[HarmonyPatch(typeof(Holder), nameof(Holder.InitializeTagCharacterPairs))]
public static class NinjaRuntimePoolWarmup_Patch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    public static void Postfix(Holder __instance)
    {
        PatchRoles_Ninja.EnsureRuntimePoolsInGreece(__instance);
    }
}

[HarmonyPatch(typeof(World), nameof(World.AddThicket))]
public static class World_AddThicket_NinjaHidingSpot_Patch
{
    [HarmonyPostfix]
    public static void Postfix(Grass grass)
    {
        PatchRoles_Ninja.EnsureThicketHidingSpots(grass);
    }
}
