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
/// Keep each world's native behaviour: one native Greece squad remains
/// untouched, the supplement adds one Greece squad and two Norselands squads.
/// The Norselands prefabs are cloned while inactive into deterministic custom
/// synced pools because the native 2.4 archer pool ID collides with FleetBoat.
/// All twenty ghosts remain tracked by the Cerberus ability for cleanup.
/// Cerberus cooldown is 22.5 seconds while the mod is enabled, instead of 30.
/// </summary>
public static class PatchDivine_GhostSquads
{
    private const float OriginalCerberusCooldownSeconds = 30f;
    private const float EnhancedCerberusCooldownSeconds = 22.5f;
    internal const int SyncIdMin = 30130;
    internal const int SyncIdMax = 30131;

    private const int NorseLeaderSyncId = 30130;
    private const int NorseArcherSyncId = 30131;
    private const int NorseGhostDurationSeconds = 30;
    private const string GreeceLeaderName = "Warrior_Ghost_Leader_Greece";
    private const string GreeceArcherName = "Warrior_Ghost_Greece";
    private const string NorseLeaderName = "Warrior_Ghost_Leader_norselands";
    private const string NorseArcherName = "Warrior_Ghost_norselands";
    private const string NorseSummonLeaderName = "KEM_Warrior_Ghost_Leader_Norse_CerberusSummon";
    private const string NorseSummonArcherName = "KEM_Warrior_Ghost_Norse_CerberusSummon";

    private static WarriorGhostLeader _norseLeader;
    private static WarriorGhost _norseArcher;
    private static WarriorGhostLeader _norseSummonLeader;
    private static WarriorGhost _norseSummonArcher;
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
            if (!EnsureNorseSummonPrefabs()) return;

            bool leaderReady = EnsureSyncedPool(pools, _norseSummonLeader.gameObject,
                NorseLeaderSyncId, "ghost-leader:norse-native-behaviour");
            bool archerReady = EnsureSyncedPool(pools, _norseSummonArcher.gameObject,
                NorseArcherSyncId, "ghost-archer:norse-native-behaviour");

            if (leaderReady && archerReady && !_loggedReady)
            {
                _loggedReady = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[GhostSquads] Norse native-behaviour pools ready (syncIDs=30130/30131, duration=30s)");
            }
        }
        catch (Exception e)
        {
            LogFailure("pool setup failed", e);
        }
    }

    private static bool EnsureNorseSummonPrefabs()
    {
        if (_norseSummonLeader != null && _norseSummonArcher != null) return true;

        FindSourcePrefabs();
        if (_norseLeader == null || _norseArcher == null)
        {
            LogFailure("unique exact Norselands ghost prefabs were not found", null);
            return false;
        }

        GameObject inactiveRoot = new GameObject("KEM_GhostSquadPrefabStaging");
        inactiveRoot.SetActive(false);
        try
        {
            GameObject leaderGo = UnityEngine.Object.Instantiate(
                _norseLeader.gameObject, inactiveRoot.transform, false);
            GameObject archerGo = UnityEngine.Object.Instantiate(
                _norseArcher.gameObject, inactiveRoot.transform, false);
            if (leaderGo == null || archerGo == null)
            {
                LogFailure("inactive Norselands prefab clone returned null", null);
                return false;
            }

            leaderGo.SetActive(false);
            archerGo.SetActive(false);
            leaderGo.name = NorseSummonLeaderName;
            archerGo.name = NorseSummonArcherName;

            WarriorGhostLeader summonLeader = leaderGo.GetComponent<WarriorGhostLeader>();
            WarriorGhost summonArcher = archerGo.GetComponent<WarriorGhost>();
            if (summonLeader == null || summonArcher == null
                || summonLeader.TryCast<WarriorGhostLeaderGreece>() != null
                || summonArcher.TryCast<WarriorGhostGreece>() != null)
            {
                LogFailure("cloned prefabs did not preserve Norselands behaviour components", null);
                return false;
            }

            leaderGo.transform.SetParent(null, false);
            archerGo.transform.SetParent(null, false);
            UnityEngine.Object.DontDestroyOnLoad(leaderGo);
            UnityEngine.Object.DontDestroyOnLoad(archerGo);
            _norseSummonLeader = summonLeader;
            _norseSummonArcher = summonArcher;
            return true;
        }
        finally
        {
            UnityEngine.Object.Destroy(inactiveRoot);
        }
    }

    private static void FindSourcePrefabs()
    {
        if (_norseLeader == null)
        {
            WarriorGhostLeader norse = null;
            int norseMatches = 0;
            var leaders = Resources.LoadAll<WarriorGhostLeader>("");
            for (int i = 0; i < leaders.Length; i++)
            {
                WarriorGhostLeader leader = leaders[i];
                if (leader == null || leader.gameObject == null) continue;
                if (leader.gameObject.name == NorseLeaderName
                    && leader.TryCast<WarriorGhostLeaderGreece>() == null)
                {
                    norse = leader;
                    norseMatches++;
                }
            }

            _norseLeader = norseMatches == 1 ? norse : null;
        }

        if (_norseArcher == null)
        {
            WarriorGhost norse = null;
            int norseMatches = 0;
            var archers = Resources.LoadAll<WarriorGhost>("");
            for (int i = 0; i < archers.Length; i++)
            {
                WarriorGhost archer = archers[i];
                if (archer == null || archer.gameObject == null) continue;
                if (archer.gameObject.name == NorseArcherName
                    && archer.TryCast<WarriorGhostGreece>() == null)
                {
                    norse = archer;
                    norseMatches++;
                }
            }

            _norseArcher = norseMatches == 1 ? norse : null;
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
        if (_norseSummonLeader == null || _norseSummonArcher == null) return false;
        Pool leaderPool = Pool.GetPoolFromPrefabAsset(_norseSummonLeader.gameObject);
        Pool archerPool = Pool.GetPoolFromPrefabAsset(_norseSummonArcher.gameObject);
        return leaderPool != null && leaderPool.sync && leaderPool.syncID == NorseLeaderSyncId
            && archerPool != null && archerPool.sync && archerPool.syncID == NorseArcherSyncId;
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

    internal static void ApplyCooldownProfile(SummonGhostSteedAbility ability)
    {
        if (ability == null) return;
        ability._cooldown = ModConfig.Enabled.Value
            ? EnhancedCerberusCooldownSeconds
            : OriginalCerberusCooldownSeconds;
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

        // Native squad #1 is Greece. Add Greece #2, then two true Norselands
        // behaviour squads. Every squad has its own local formation leader.
        for (int squadIndex = 1; squadIndex <= 3; squadIndex++)
        {
            bool isNorse = squadIndex > 1;
            WarriorGhostLeader leaderPrefab = isNorse ? _norseSummonLeader : ability._vanguardPrefab;
            WarriorGhost archerPrefab = isNorse ? _norseSummonArcher : ability._archerPrefab;
            int duration = isNorse ? NorseGhostDurationSeconds : 0;

            if (!CanContinue(ability)) yield break;
            WarriorGhostLeader leader = null;
            HelsGhost leaderGhost = SpawnGhost(
                ability, holder, leaderPrefab.gameObject, squadIndex, 0, duration);
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
                    ability, holder, archerPrefab.gameObject, squadIndex, member, duration);
                if (archer == null) yield break;
                archer.AddToFormation(ref leader);
                yield return null;
            }
        }

        if (CanContinue(ability) && ability._activeGhosts.Count == 20)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[GhostSquads] Cerberus summon completed: leaders=4 archers=16 (2 Greece / 2 Norse native behaviour)");
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
        int memberIndex,
        int durationSeconds)
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
            if (durationSeconds > 0)
                ghost.Duration = durationSeconds;
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
    [HarmonyPrefix]
    public static void Activate_Prefix(SummonGhostSteedAbility __instance)
    {
        PatchDivine_GhostSquads.ApplyCooldownProfile(__instance);
    }

    [HarmonyPostfix]
    public static void Activate_Postfix(SummonGhostSteedAbility __instance)
    {
        PatchDivine_GhostSquads.AfterActivate(__instance);
    }
}

[HarmonyPatch(typeof(SummonGhostSteedAbility), nameof(SummonGhostSteedAbility.RemoveActiveGhost))]
public static class SummonGhostSteedAbility_RemoveActiveGhost_Cooldown_Patch
{
    [HarmonyPrefix]
    public static void Prefix(SummonGhostSteedAbility __instance)
    {
        PatchDivine_GhostSquads.ApplyCooldownProfile(__instance);
    }
}

/// <summary>
/// UpdateSummonStatus starts cooldown/fog coroutines when the last ghost is
/// removed.  During scene teardown (area unload while leash-held ghosts are
/// still alive) the component is already deactivating and the native
/// StartCoroutine call receives a null routine (observed ArgumentNullException
/// in the il2cpp trampoline).  Skip it there; in normal play the ability is
/// active and the original runs untouched.
/// </summary>
[HarmonyPatch(typeof(SummonGhostSteedAbility), "UpdateSummonStatus")]
public static class SummonGhostSteedAbility_UpdateSummonStatus_Teardown_Guard_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(SummonGhostSteedAbility __instance)
    {
        if (__instance == null || __instance.gameObject == null) return false;
        if (!__instance.gameObject.activeInHierarchy) return false;
        return true;
    }
}

[HarmonyPatch(typeof(SummonGhostSteedAbility), nameof(SummonGhostSteedAbility.DespawnUnits))]
public static class SummonGhostSteedAbility_DespawnUnits_Cooldown_Patch
{
    [HarmonyPrefix]
    public static void Prefix(SummonGhostSteedAbility __instance)
    {
        PatchDivine_GhostSquads.ApplyCooldownProfile(__instance);
    }
}
