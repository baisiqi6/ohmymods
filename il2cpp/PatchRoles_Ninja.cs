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
    private static readonly string[] TREE_ANCHOR_NAMES =
    {
        "KEM_NinjaTreeHidingSpot"
    };
    private static readonly float[] TREE_ANCHOR_LOCAL_X = { 0f };
    private static readonly string[] BEGGAR_CAMP_ANCHOR_NAMES =
    {
        "KEM_NinjaCampHidingSpot_FarLeft",
        "KEM_NinjaCampHidingSpot_Left",
        "KEM_NinjaCampHidingSpot_Center",
        "KEM_NinjaCampHidingSpot_Right",
        "KEM_NinjaCampHidingSpot_FarRight"
    };
    private static readonly float[] BEGGAR_CAMP_ANCHOR_LOCAL_X = { -2f, -1f, 0f, 1f, 2f };
    private static bool _loggedThicketHookEntered;
    private static bool _loggedThicketAdded;
    private static bool _loggedThicketReused;
    private static bool _loggedTreeAdded;
    private static bool _loggedTreeReused;
    private static bool _loggedBeggarCampAdded;
    private static bool _loggedBeggarCampReused;

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
            bool useLegacyRootAsCenter;
            PrepareAnchorSet(
                thicket,
                THICKET_ANCHOR_NAMES,
                THICKET_ANCHOR_LOCAL_X,
                kingdom,
                out added,
                out reused,
                out useLegacyRootAsCenter);

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

    public static void EnsureTreeHidingSpot(PayableTree tree)
    {
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return;
        if (BiomeHolder.Inst == null
            || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return;
        if (tree == null || tree.gameObject == null || !tree.gameObject.activeInHierarchy) return;

        try
        {
            var managers = Managers.Inst;
            var kingdom = managers != null ? managers.kingdom : null;
            if (kingdom == null) return;

            bool added;
            bool reused;
            bool usedRoot;
            PrepareAnchorSet(
                tree.gameObject,
                TREE_ANCHOR_NAMES,
                TREE_ANCHOR_LOCAL_X,
                kingdom,
                out added,
                out reused,
                out usedRoot);

            float x = tree.transform.position.x;
            if (added && !_loggedTreeAdded)
            {
                _loggedTreeAdded = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[Ninja] Prepared 1 Greece tree hiding anchor (side="
                    + GetSideLabel(kingdom, x) + ", x=" + x + ")");
            }
            if (reused && !_loggedTreeReused)
            {
                _loggedTreeReused = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[Ninja] Re-registered reused Greece tree hiding anchor (side="
                    + GetSideLabel(kingdom, x) + ", x=" + x + ")");
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Ninja] Failed to prepare HidingSpot for Greece tree: " + e);
        }
    }

    public static void EnsureBeggarCampHidingSpots(BeggarCamp camp)
    {
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return;
        if (BiomeHolder.Inst == null
            || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return;
        if (camp == null || camp.gameObject == null || !camp.gameObject.activeInHierarchy) return;

        try
        {
            var managers = Managers.Inst;
            var kingdom = managers != null ? managers.kingdom : null;
            if (kingdom == null) return;

            bool added;
            bool reused;
            bool usedRoot;
            PrepareAnchorSet(
                camp.gameObject,
                BEGGAR_CAMP_ANCHOR_NAMES,
                BEGGAR_CAMP_ANCHOR_LOCAL_X,
                kingdom,
                out added,
                out reused,
                out usedRoot);

            float x = camp.transform.position.x;
            if (added && !_loggedBeggarCampAdded)
            {
                _loggedBeggarCampAdded = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[Ninja] Prepared 5 Greece beggar-camp hiding anchors (side="
                    + GetSideLabel(kingdom, x) + ", x=" + x + ")");
            }
            if (reused && !_loggedBeggarCampReused)
            {
                _loggedBeggarCampReused = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[Ninja] Re-registered reused Greece beggar-camp hiding anchors (5 anchors, side="
                    + GetSideLabel(kingdom, x) + ", x=" + x + ")");
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Ninja] Failed to prepare HidingSpots for Greece beggar camp: " + e);
        }
    }

    private static void PrepareAnchorSet(
        GameObject owner,
        string[] anchorNames,
        float[] localX,
        Kingdom kingdom,
        out bool added,
        out bool reused,
        out bool usedRootAsCenter)
    {
        added = false;
        reused = false;
        usedRootAsCenter = false;
        if (owner == null || anchorNames == null || localX == null
            || anchorNames.Length == 0 || anchorNames.Length != localX.Length) return;

        int centerIndex = anchorNames.Length / 2;
        HidingSpot rootSpot = owner.GetComponent<HidingSpot>();
        Transform namedCenter = owner.transform.Find(anchorNames[centerIndex]);
        usedRootAsCenter = rootSpot != null && rootSpot.enabled && namedCenter == null;

        if (usedRootAsCenter)
        {
            reused |= ReRegisterExistingAnchorIfMissing(rootSpot, kingdom);
        }
        else if (rootSpot != null && rootSpot.enabled)
        {
            // A named layout supersedes the legacy/native root slot.  Disabling
            // only HidingSpot invokes its native unregister/occupant notification
            // without changing any other component on the tree/camp/thicket.
            rootSpot.enabled = false;
        }

        for (int i = 0; i < anchorNames.Length; i++)
        {
            if (usedRootAsCenter && i == centerIndex) continue;

            bool anchorAdded;
            HidingSpot anchor = EnsureChildAnchor(owner, anchorNames[i], localX[i], out anchorAdded);
            added |= anchorAdded;
            if (!anchorAdded)
                reused |= ReRegisterExistingAnchorIfMissing(anchor, kingdom);
        }
    }

    private static HidingSpot EnsureChildAnchor(
        GameObject owner,
        string anchorName,
        float localX,
        out bool added)
    {
        added = false;
        Transform anchorTransform = owner.transform.Find(anchorName);
        GameObject anchorObject;
        if (anchorTransform == null)
        {
            anchorObject = new GameObject(anchorName);
            anchorTransform = anchorObject.transform;
            anchorTransform.SetParent(owner.transform, false);
            added = true;
        }
        else
        {
            anchorObject = anchorTransform.gameObject;
        }

        // Normalize every slot on creation/reuse.  Distinct world-space x values
        // let Kingdom's native ordering and Ninja's outside-wall filter operate
        // independently for each single-occupancy HidingSpot.
        anchorTransform.localPosition = new Vector3(localX, 0f, 0f);

        HidingSpot hidingSpot = anchorObject.GetComponent<HidingSpot>();
        if (hidingSpot == null)
        {
            // Do not register manually in the creation frame: native Start does it.
            hidingSpot = anchorObject.AddComponent<HidingSpot>();
            added = true;
        }
        return hidingSpot;
    }

    private static string GetSideLabel(Kingdom kingdom, float x)
    {
        return kingdom != null
            ? (x < kingdom.campfirePosition ? Side.Left : Side.Right).ToString()
            : "<unknown>";
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

[HarmonyPatch(typeof(PayableTree), nameof(PayableTree.OnEnable))]
public static class PayableTree_OnEnable_NinjaHidingSpot_Patch
{
    [HarmonyPostfix]
    public static void Postfix(PayableTree __instance)
    {
        PatchRoles_Ninja.EnsureTreeHidingSpot(__instance);
    }
}
