using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Cerberus' Greece summon is natively one WarriorGhostLeaderGreece plus four
/// WarriorGhostGreece archers.  The Norselands artefact uses the same formation
/// interfaces, but its concrete classes have different lifetime/AI overrides.
///
/// Keep Greece behaviour for every summoned unit and build two deterministic
/// visual-only variants by cloning the Greece prefabs while inactive, then
/// copying the Norselands animator set and initial sprite.  One native Greece
/// squad remains untouched; the supplement adds one Greece squad and two
/// Norselands-looking Greece-logic squads for a total of four leaders and
/// sixteen archers.
/// </summary>
public static class PatchDivine_GhostSquads
{
    internal const int SyncIdMin = 30130;
    internal const int SyncIdMax = 30131;

    private const int NorseVisualLeaderSyncId = 30130;
    private const int NorseVisualArcherSyncId = 30131;
    private const string GreeceLeaderName = "Warrior_Ghost_Leader_Greece";
    private const string GreeceArcherName = "Warrior_Ghost_Greece";
    private const string NorseLeaderName = "Warrior_Ghost_Leader_norselands";
    private const string NorseArcherName = "Warrior_Ghost_norselands";
    private const string VisualLeaderName = "KEM_Warrior_Ghost_Leader_NorseVisual_GreeceLogic";
    private const string VisualArcherName = "KEM_Warrior_Ghost_NorseVisual_GreeceLogic";

    private static WarriorGhostLeaderGreece _greeceLeader;
    private static WarriorGhostGreece _greeceArcher;
    private static WarriorGhostLeader _norseLeader;
    private static WarriorGhost _norseArcher;
    private static WarriorGhostLeaderGreece _visualLeader;
    private static WarriorGhostGreece _visualArcher;
    private static bool _loggedReady;
    private static bool _loggedFailure;

    internal static void EnsurePools()
    {
        if (!ModConfig.Enabled.Value) return;
        if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return;

        try
        {
            PoolManager pools = Managers.Inst != null ? Managers.Inst.pools : null;
            if (pools == null) return;
            if (!EnsureVisualPrefabs()) return;

            bool leaderReady = EnsureSyncedPool(pools, _visualLeader.gameObject,
                NorseVisualLeaderSyncId, "ghost-leader:norse-visual");
            bool archerReady = EnsureSyncedPool(pools, _visualArcher.gameObject,
                NorseVisualArcherSyncId, "ghost-archer:norse-visual");

            if (leaderReady && archerReady && !_loggedReady)
            {
                _loggedReady = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[GhostSquads] Norse visual Greece-logic pools ready (syncIDs=30130/30131)");
            }
        }
        catch (Exception e)
        {
            LogFailure("pool setup failed", e);
        }
    }

    private static bool EnsureVisualPrefabs()
    {
        if (_visualLeader != null && _visualArcher != null) return true;

        FindSourcePrefabs();
        if (_greeceLeader == null || _greeceArcher == null || _norseLeader == null || _norseArcher == null)
        {
            LogFailure("exact Greece/Norselands ghost prefabs were not all found", null);
            return false;
        }

        GameObject inactiveRoot = new GameObject("KEM_GhostSquadPrefabStaging");
        inactiveRoot.SetActive(false);
        try
        {
            GameObject leaderGo = UnityEngine.Object.Instantiate(
                _greeceLeader.gameObject, inactiveRoot.transform, false);
            GameObject archerGo = UnityEngine.Object.Instantiate(
                _greeceArcher.gameObject, inactiveRoot.transform, false);
            if (leaderGo == null || archerGo == null)
            {
                LogFailure("inactive Greece prefab clone returned null", null);
                return false;
            }

            leaderGo.SetActive(false);
            archerGo.SetActive(false);
            leaderGo.name = VisualLeaderName;
            archerGo.name = VisualArcherName;

            WarriorGhostLeaderGreece visualLeader = leaderGo.GetComponent<WarriorGhostLeaderGreece>();
            WarriorGhostGreece visualArcher = archerGo.GetComponent<WarriorGhostGreece>();
            if (visualLeader == null || visualArcher == null)
            {
                LogFailure("cloned prefabs lost their Greece behaviour components", null);
                return false;
            }

            CopyLeaderVisuals(visualLeader, _norseLeader);
            CopyArcherVisuals(visualArcher, _norseArcher);

            leaderGo.transform.SetParent(null, false);
            archerGo.transform.SetParent(null, false);
            UnityEngine.Object.DontDestroyOnLoad(leaderGo);
            UnityEngine.Object.DontDestroyOnLoad(archerGo);
            _visualLeader = visualLeader;
            _visualArcher = visualArcher;
            return true;
        }
        finally
        {
            UnityEngine.Object.Destroy(inactiveRoot);
        }
    }

    private static void FindSourcePrefabs()
    {
        if (_greeceLeader == null || _norseLeader == null)
        {
            WarriorGhostLeaderGreece greece = null;
            WarriorGhostLeader norse = null;
            int greeceMatches = 0;
            int norseMatches = 0;
            var leaders = Resources.LoadAll<WarriorGhostLeader>("");
            for (int i = 0; i < leaders.Length; i++)
            {
                WarriorGhostLeader leader = leaders[i];
                if (leader == null || leader.gameObject == null) continue;
                if (leader.gameObject.name == GreeceLeaderName)
                {
                    WarriorGhostLeaderGreece exact = leader.TryCast<WarriorGhostLeaderGreece>();
                    if (exact != null)
                    {
                        greece = exact;
                        greeceMatches++;
                    }
                }
                else if (leader.gameObject.name == NorseLeaderName
                    && leader.TryCast<WarriorGhostLeaderGreece>() == null)
                {
                    norse = leader;
                    norseMatches++;
                }
            }

            _greeceLeader = greeceMatches == 1 ? greece : null;
            _norseLeader = norseMatches == 1 ? norse : null;
        }

        if (_greeceArcher == null || _norseArcher == null)
        {
            WarriorGhostGreece greece = null;
            WarriorGhost norse = null;
            int greeceMatches = 0;
            int norseMatches = 0;
            var archers = Resources.LoadAll<WarriorGhost>("");
            for (int i = 0; i < archers.Length; i++)
            {
                WarriorGhost archer = archers[i];
                if (archer == null || archer.gameObject == null) continue;
                if (archer.gameObject.name == GreeceArcherName)
                {
                    WarriorGhostGreece exact = archer.TryCast<WarriorGhostGreece>();
                    if (exact != null)
                    {
                        greece = exact;
                        greeceMatches++;
                    }
                }
                else if (archer.gameObject.name == NorseArcherName
                    && archer.TryCast<WarriorGhostGreece>() == null)
                {
                    norse = archer;
                    norseMatches++;
                }
            }

            _greeceArcher = greeceMatches == 1 ? greece : null;
            _norseArcher = norseMatches == 1 ? norse : null;
        }
    }

    private static void CopyLeaderVisuals(WarriorGhostLeaderGreece target, WarriorGhostLeader source)
    {
        target._animators = source._animators;
        CopyRendererAndAnimator(target.gameObject, source.gameObject);
    }

    private static void CopyArcherVisuals(WarriorGhostGreece target, WarriorGhost source)
    {
        target._animators = source._animators;
        CopyRendererAndAnimator(target.gameObject, source.gameObject);
    }

    private static void CopyRendererAndAnimator(GameObject target, GameObject source)
    {
        Animator targetAnimator = target.GetComponent<Animator>();
        Animator sourceAnimator = source.GetComponent<Animator>();
        if (targetAnimator != null && sourceAnimator != null)
            targetAnimator.runtimeAnimatorController = sourceAnimator.runtimeAnimatorController;

        SpriteRenderer targetRenderer = target.GetComponent<SpriteRenderer>();
        SpriteRenderer sourceRenderer = source.GetComponent<SpriteRenderer>();
        if (targetRenderer != null && sourceRenderer != null)
        {
            targetRenderer.sprite = sourceRenderer.sprite;
            targetRenderer.color = sourceRenderer.color;
            targetRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
        }
    }

    private static bool EnsureSyncedPool(PoolManager manager, GameObject prefab, int syncId, string label)
    {
        if (manager.cachedSyncIdPoolPairs != null && manager.cachedSyncIdPoolPairs.ContainsKey(syncId))
        {
            Pool byId = manager.cachedSyncIdPoolPairs[syncId];
            if (byId == null || byId.prefab != prefab)
            {
                LogFailure("syncID " + syncId + " is already owned by another prefab", null);
                return false;
            }
        }

        Pool pool = Pool.GetPoolFromPrefabAsset(prefab);
        if (pool == null)
        {
            Pool[] physicalPools = manager.GetComponentsInChildren<Pool>();
            for (int i = 0; i < physicalPools.Length; i++)
            {
                Pool old = physicalPools[i];
                if (old != null && old.prefab == prefab)
                    UnityEngine.Object.Destroy(old.gameObject);
            }

            pool = manager.CreatePoolFor(prefab);
            if (pool == null)
            {
                LogFailure("CreatePoolFor returned null for " + label, null);
                return false;
            }
            pool.sync = true;
            pool.syncID = (short)syncId;
        }

        if (!pool.sync || pool.syncID != syncId)
        {
            LogFailure(label + " has unexpected pool sync configuration", null);
            return false;
        }

        if (manager.cachedPools != null && !manager.cachedPools.Contains(pool))
            manager.cachedPools.Add(pool);
        if (manager.cachedNamePoolPairs != null)
        {
            if (manager.cachedNamePoolPairs.ContainsKey(prefab.name)
                && manager.cachedNamePoolPairs[prefab.name] != pool)
            {
                LogFailure(label + " prefab name is already mapped to another pool", null);
                return false;
            }
            manager.cachedNamePoolPairs[prefab.name] = pool;
        }
        if (manager.cachedSyncIdPoolPairs != null)
            manager.cachedSyncIdPoolPairs[syncId] = pool;
        return true;
    }

    private static bool PoolsReady()
    {
        if (_visualLeader == null || _visualArcher == null) return false;
        Pool leaderPool = Pool.GetPoolFromPrefabAsset(_visualLeader.gameObject);
        Pool archerPool = Pool.GetPoolFromPrefabAsset(_visualArcher.gameObject);
        return leaderPool != null && leaderPool.sync && leaderPool.syncID == NorseVisualLeaderSyncId
            && archerPool != null && archerPool.sync && archerPool.syncID == NorseVisualArcherSyncId;
    }

    internal static void AfterActivate(SummonGhostSteedAbility ability)
    {
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return;
        if (ability == null || ability.gameObject == null || !ability.gameObject.activeInHierarchy) return;
        if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return;
        if (ability._rider == null || ability._vanguardPrefab == null || ability._archerPrefab == null) return;
        if (ability._vanguardPrefab.gameObject.name != GreeceLeaderName
            || ability._archerPrefab.gameObject.name != GreeceArcherName) return;

        // The native formation routine has one shared _spawnedLeader. Increasing
        // only its two count fields would attach all archers to the last leader,
        // so retain the native 1+4 and add three complete local formations.
        if (ability._vanguardsToSpawn != 1 || ability._archersToSpawn != 4)
        {
            LogFailure("native Cerberus summon is no longer configured as 1 leader + 4 archers", null);
            return;
        }
        if (ability.TryCast<IGhostHolder>() == null)
        {
            LogFailure("SummonGhostSteedAbility could not cast to IGhostHolder", null);
            return;
        }

        EnsurePools();
        if (!PoolsReady())
        {
            LogFailure("supplement pools were not ready at activation", null);
            return;
        }

        ability.StartCoroutine(SpawnSupplementalSquads(ability).WrapToIl2Cpp());
    }

    private static IEnumerator SpawnSupplementalSquads(SummonGhostSteedAbility ability)
    {
        float deadline = Time.time + 3f;
        while (ability != null && ability.gameObject != null && ability.gameObject.activeInHierarchy
            && ability._activeGhosts != null && ability._activeGhosts.Count < 5 && Time.time < deadline)
            yield return null;

        if (!CanContinue(ability) || ability._activeGhosts.Count < 5)
        {
            LogFailure("native 1+4 squad did not finish before the supplement timeout", null);
            yield break;
        }

        IGhostHolder holder = ability.TryCast<IGhostHolder>();
        if (holder == null) yield break;

        // Native squad #1 is Greece.  Add Greece #2, then two Norse-visual
        // squads whose concrete behaviour components remain Greece subclasses.
        for (int squadIndex = 1; squadIndex <= 3; squadIndex++)
        {
            WarriorGhostLeader leaderPrefab = squadIndex == 1 ? ability._vanguardPrefab : _visualLeader;
            WarriorGhost archerPrefab = squadIndex == 1 ? ability._archerPrefab : _visualArcher;

            if (!CanContinue(ability)) yield break;
            WarriorGhostLeader leader = null;
            HelsGhost leaderGhost = SpawnGhost(
                ability, holder, leaderPrefab.gameObject, squadIndex, 0);
            if (leaderGhost == null) yield break;
            leaderGhost.AddToFormation(ref leader);
            if (leader == null)
            {
                LogFailure("spawned leader did not establish a formation", null);
                yield break;
            }
            yield return null;

            for (int member = 1; member <= 4; member++)
            {
                if (!CanContinue(ability)) yield break;
                HelsGhost archer = SpawnGhost(
                    ability, holder, archerPrefab.gameObject, squadIndex, member);
                if (archer == null) yield break;
                archer.AddToFormation(ref leader);
                yield return null;
            }
        }

        if (CanContinue(ability) && ability._activeGhosts.Count == 20)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[GhostSquads] Cerberus summon completed: leaders=4 archers=16 (2 Greece / 2 Norse visual)");
        }
        else if (CanContinue(ability))
        {
            LogFailure(
                "supplement finished with an unexpected tracked ghost count="
                + ability._activeGhosts.Count,
                null);
        }
    }

    private static HelsGhost SpawnGhost(
        SummonGhostSteedAbility ability,
        IGhostHolder holder,
        GameObject prefab,
        int squadIndex,
        int memberIndex)
    {
        try
        {
            float facing = ability._rider.transform.localScale.x >= 0f ? 1f : -1f;
            float squadOffset = 1.5f - (squadIndex - 1) * 1.75f;
            float memberOffset = memberIndex == 0 ? 0f : memberIndex * 0.28f;
            Vector3 position = ability.transform.position;
            position.x += (squadOffset - memberOffset) * facing;

            GameObject spawned = Pool.SpawnGO(
                prefab, position, Quaternion.identity, null, false, false, true);
            HelsGhost ghost = spawned != null ? spawned.GetComponent<HelsGhost>() : null;
            if (ghost == null)
            {
                LogFailure("Pool.SpawnGO returned an invalid ghost", null);
                return null;
            }

            ghost.GhostHolder = holder;
            ghost.Summoner = ability._rider;
            ghost.StartDeathCountdown();
            ability._activeGhosts.Add(ghost);
            return ghost;
        }
        catch (Exception e)
        {
            LogFailure("supplement spawn failed", e);
            return null;
        }
    }

    private static bool CanContinue(SummonGhostSteedAbility ability)
    {
        return ModConfig.Enabled.Value && NetworkBigBoss.HasWorldAuth
            && ability != null && ability.gameObject != null && ability.gameObject.activeInHierarchy
            && ability._rider != null && ability._activeGhosts != null;
    }

    private static void LogFailure(string message, Exception exception)
    {
        if (_loggedFailure) return;
        _loggedFailure = true;
        string text = "[GhostSquads] " + message;
        if (exception != null) text += ": " + exception;
        KingdomEnhancedPlugin.Instance?.LogSource.LogError(text);
    }
}

[HarmonyPatch(typeof(SummonGhostSteedAbility), nameof(SummonGhostSteedAbility.Activate))]
public static class SummonGhostSteedAbility_ExpandedSquads_Patch
{
    [HarmonyPostfix]
    public static void Activate_Postfix(SummonGhostSteedAbility __instance)
    {
        PatchDivine_GhostSquads.AfterActivate(__instance);
    }
}
