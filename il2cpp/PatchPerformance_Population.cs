using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

[HarmonyPatch(typeof(CampaignSaveData), nameof(CampaignSaveData.ApplyToScene))]
public static class PopulationPerformanceApplyPatch
{
    [HarmonyPostfix]
    public static void Postfix(CampaignSaveData __instance)
    {
        PopulationPerformanceCoordinator.BeginScene(__instance);
    }
}

/// <summary>
/// Authority-only population governor. Static job storage avoids managed generic instance
/// fields on an injected IL2CPP MonoBehaviour.
/// </summary>
public sealed class PopulationPerformanceCoordinator : MonoBehaviour
{
    internal const int CampCapacity = 5;
    internal const float FallbackSpawnInterval = 1f;

    private const float ReconcileInterval = 0.5f;
    private const float StableDelay = 3f;
    private const float ReplenishPeriod = 6f;

    private sealed class CampProfile
    {
        public BeggarCamp Camp;
        public IntPtr Pointer;
        public int InstanceId;
        public int OriginalMax;
        public float OriginalInterval;
    }

    private sealed class CampState
    {
        public CampProfile Profile;
        public float NextSpawnAt;
        public int LastOwned = -1;
        public int Owned;
    }

    private sealed class Ownership
    {
        public Beggar Beggar;
        public IntPtr Pointer;
        public int InstanceId;
        public int NetId;
        public int Epoch;
        public CampState Camp;
        public bool Seen;
    }

    private sealed class CleanupCandidate
    {
        public Beggar Beggar;
        public IntPtr Pointer;
        public int InstanceId;
        public int NetId;
        public int Epoch;
        public CampState Camp;
    }

    private enum Phase
    {
        Waiting,
        Cleaning,
        Complete,
        Suspended,
        Faulted
    }

    private static readonly Dictionary<IntPtr, CampProfile> Profiles = new();
    private static readonly Dictionary<IntPtr, CampState> Camps = new();
    private static readonly Dictionary<IntPtr, Ownership> Owners = new();
    private static readonly Dictionary<IntPtr, int> BeggarEpochs = new();
    private static readonly List<CleanupCandidate> Cleanup = new();
    private static readonly List<IntPtr> ScratchKeys = new();
    private static readonly HashSet<IntPtr> SpawnBefore = new();

    private static bool _registered;
    private static PopulationPerformanceCoordinator _instance;
    private static CampaignSaveData _campaign;
    private static Kingdom _kingdom;
    private static World _world;
    private static Transform _sceneRoot;
    private static IntPtr _campaignPointer;
    private static IntPtr _kingdomPointer;
    private static IntPtr _worldPointer;
    private static IntPtr _sceneRootPointer;
    private static int _generation;
    private static Phase _phase = Phase.Suspended;
    private static float _stableAt;
    private static float _nextReconcileAt;
    private static float _retryAt;
    private static float _awaitSceneUntil;
    private static int _cleanupIndex;
    private static int _cleanupBefore;
    private static int _cleanupAssigned;
    private static int _cleanupProtected;
    private static int _cleanupRemoved;
    private static int _cleanupSkipped;
    private static bool _faultLogged;
    private static bool _spawnFailureLogged;

    public PopulationPerformanceCoordinator(IntPtr ptr) : base(ptr) { }

    internal static void CaptureProfile(BeggarCamp camp)
    {
        if (!IsObjectValid(camp)) return;
        IntPtr pointer = camp.Pointer;
        int instanceId = camp.gameObject.GetInstanceID();
        if (Profiles.TryGetValue(pointer, out CampProfile existing)
            && existing.InstanceId == instanceId)
        {
            return;
        }

        Profiles[pointer] = new CampProfile
        {
            Camp = camp,
            Pointer = pointer,
            InstanceId = instanceId,
            OriginalMax = camp.maxBeggars,
            OriginalInterval = camp.spawnInterval
        };
    }

    internal static void ConfigureCamp(BeggarCamp camp)
    {
        if (!IsObjectValid(camp)) return;
        CaptureProfile(camp);
        if (!TryEnsureAttached())
        {
            ApplyFallback(camp);
            return;
        }

        camp.spawnInterval = FallbackSpawnInterval;
        camp.maxBeggars = NetworkBigBoss.HasWorldAuth ? 0 : CampCapacity;
        if (_campaign == null)
            _awaitSceneUntil = Mathf.Max(_awaitSceneUntil, Time.unscaledTime + 10f);
    }

    internal static void ForgetCamp(BeggarCamp camp)
    {
        if (camp == null) return;
        IntPtr pointer = camp.Pointer;
        Profiles.Remove(pointer);
        Camps.Remove(pointer);

        ScratchKeys.Clear();
        foreach (KeyValuePair<IntPtr, Ownership> pair in Owners)
        {
            if (pair.Value.Camp?.Profile?.Pointer == pointer) ScratchKeys.Add(pair.Key);
        }
        for (int i = 0; i < ScratchKeys.Count; i++) Owners.Remove(ScratchKeys[i]);
    }

    internal static void ForgetBeggar(Beggar beggar)
    {
        if (beggar == null) return;
        Owners.Remove(beggar.Pointer);
    }

    internal static void BeginBeggarIncarnation(Beggar beggar)
    {
        if (beggar == null) return;
        IntPtr pointer = beggar.Pointer;
        BeggarEpochs.TryGetValue(pointer, out int epoch);
        BeggarEpochs[pointer] = epoch == int.MaxValue ? 1 : epoch + 1;
        Owners.Remove(pointer);
    }

    internal static void BeginScene(CampaignSaveData campaign)
    {
        if (!TryEnsureAttached())
        {
            RestoreFallbackProfiles();
            return;
        }

        Managers managers = Managers.Inst;
        Kingdom kingdom = managers?.kingdom;
        World world = managers?.world;
        Transform sceneRoot = world?.gameLayer;
        if (campaign == null || kingdom == null || world == null || sceneRoot == null)
        {
            RestoreFallbackProfiles();
            return;
        }

        _campaign = campaign;
        _kingdom = kingdom;
        _world = world;
        _sceneRoot = sceneRoot;
        _campaignPointer = campaign.Pointer;
        _kingdomPointer = kingdom.Pointer;
        _worldPointer = world.Pointer;
        _sceneRootPointer = sceneRoot.Pointer;
        _generation++;
        _phase = Phase.Waiting;
        _stableAt = Time.time + StableDelay;
        _nextReconcileAt = Time.time;
        _cleanupIndex = 0;
        _cleanupRemoved = 0;
        _cleanupSkipped = 0;
        _faultLogged = false;
        _spawnFailureLogged = false;
        _awaitSceneUntil = 0f;
        Camps.Clear();
        Owners.Clear();
        BeggarEpochs.Clear();
        Cleanup.Clear();
    }

    private static bool TryEnsureAttached()
    {
        try
        {
            if (!_registered)
            {
                if (!ClassInjector.IsTypeRegisteredInIl2Cpp(
                        typeof(PopulationPerformanceCoordinator)))
                {
                    ClassInjector.RegisterTypeInIl2Cpp(
                        typeof(PopulationPerformanceCoordinator));
                }
                _registered = true;
            }

            Managers managers = Managers.Inst;
            World world = managers?.world;
            if (world == null || world.gameObject == null) return false;
            PopulationPerformanceCoordinator coordinator =
                world.GetComponent<PopulationPerformanceCoordinator>();
            if (coordinator == null)
                coordinator = world.gameObject.AddComponent<PopulationPerformanceCoordinator>();
            if (coordinator == null) return false;
            _instance = coordinator;
            return true;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[Population] coordinator attach failed: " + e.GetType().Name);
            return false;
        }
    }

    private void Update()
    {
        if (_instance != this) return;

        try
        {
            if (!ModConfig.Enabled.Value)
            {
                RestoreOriginalProfiles();
                SuspendWork();
                return;
            }

            if (!NetworkBigBoss.HasWorldAuth)
            {
                RestoreFallbackProfiles();
                SuspendWork();
                return;
            }

            if (_campaign == null)
            {
                if (Time.unscaledTime < _awaitSceneUntil) return;
                RestoreFallbackProfiles();
                SuspendWork();
                return;
            }

            if (!ValidateScene())
            {
                RestoreFallbackProfiles();
                ClearRuntimeState(Phase.Suspended);
                return;
            }

            // Native Haglet waits use scaled game time. Do not clean or replenish while the
            // pause/menu time scale is zero.
            if (Time.timeScale <= 0f) return;

            if (_phase == Phase.Suspended)
            {
                _phase = Phase.Waiting;
                _stableAt = Time.time + StableDelay;
                _nextReconcileAt = Time.time;
            }

            if (_phase == Phase.Faulted)
            {
                if (Time.unscaledTime < _retryAt) return;
                _phase = Phase.Waiting;
                _stableAt = Time.time + StableDelay;
            }

            if (Time.time >= _nextReconcileAt)
            {
                _nextReconcileAt = Time.time + ReconcileInterval;
                ReconcileOwnership();
                ConfigureCurrentCampsForCentralMode();
            }

            switch (_phase)
            {
                case Phase.Waiting:
                    if (Time.time >= _stableAt && NetworkReady())
                    {
                        ReconcileOwnership();
                        _phase = Phase.Cleaning;
                        BuildCleanupQueue();
                    }
                    break;
                case Phase.Cleaning:
                    ProcessOneCleanup();
                    break;
            }

            if (_phase != Phase.Suspended && _phase != Phase.Faulted)
                ReplenishCamps();
        }
        catch (Exception e)
        {
            RestoreFallbackProfiles();
            _phase = Phase.Faulted;
            _retryAt = Time.unscaledTime + 2f;
            if (!_faultLogged)
            {
                _faultLogged = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[Population] governor failed; fallback=5/1 error="
                    + e.GetType().Name);
            }
        }
    }

    private void OnDisable()
    {
        if (_instance != this) return;
        if (ModConfig.Enabled.Value) RestoreFallbackProfiles();
        else RestoreOriginalProfiles();
        ClearRuntimeState(Phase.Suspended);
        _instance = null;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private static bool ValidateScene()
    {
        Managers managers = Managers.Inst;
        return _campaign != null && CampaignSaveData.current != null
            && CampaignSaveData.current.Pointer == _campaignPointer
            && managers != null && managers.kingdom != null && managers.world != null
            && managers.world.gameLayer != null
            && managers.kingdom.Pointer == _kingdomPointer
            && managers.world.Pointer == _worldPointer
            && managers.world.gameLayer.Pointer == _sceneRootPointer;
    }

    private static bool NetworkReady()
    {
        return !NetworkBigBoss.IsOnline || !NetworkBigBoss.IsClientPresent
            || NetworkBigBoss.HasClientCaughtUp;
    }

    private static void ReconcileOwnership()
    {
        SyncCampSet();
        foreach (Ownership owner in Owners.Values) owner.Seen = false;
        foreach (CampState camp in Camps.Values) camp.Owned = 0;

        if (_kingdom?.Beggars != null)
        {
            foreach (Beggar beggar in _kingdom.Beggars)
            {
                if (!IsCurrentSceneBeggar(beggar)) continue;
                IntPtr pointer = beggar.Pointer;
                int instanceId = beggar.gameObject.GetInstanceID();
                int netId = GetBeggarNetId(beggar);
                int epoch = GetBeggarEpoch(pointer);
                if (!Owners.TryGetValue(pointer, out Ownership owner)
                    || owner.InstanceId != instanceId
                    || owner.Epoch != epoch
                    || (owner.NetId != int.MinValue && netId != int.MinValue
                        && owner.NetId != netId))
                {
                    owner = new Ownership
                    {
                        Beggar = beggar,
                        Pointer = pointer,
                        InstanceId = instanceId,
                        NetId = netId,
                        Epoch = epoch
                    };
                    Owners[pointer] = owner;
                }

                owner.Beggar = beggar;
                if (netId != int.MinValue) owner.NetId = netId;
                owner.Seen = true;
                CampState explicitCamp = GetCampState(beggar.camp);
                if (explicitCamp != null) owner.Camp = explicitCamp;
                else if (!IsCampStateCurrent(owner.Camp)
                    && (_phase != Phase.Waiting || Time.time >= _stableAt))
                {
                    owner.Camp = FindNearestCamp(beggar);
                }
                if (owner.Camp != null) owner.Camp.Owned++;
            }
        }

        ScratchKeys.Clear();
        foreach (KeyValuePair<IntPtr, Ownership> pair in Owners)
        {
            if (!pair.Value.Seen) ScratchKeys.Add(pair.Key);
        }
        for (int i = 0; i < ScratchKeys.Count; i++) Owners.Remove(ScratchKeys[i]);

        float now = Time.time;
        foreach (CampState camp in Camps.Values)
        {
            if (camp.LastOwned < 0 || (camp.LastOwned >= CampCapacity
                    && camp.Owned < CampCapacity))
            {
                camp.NextSpawnAt = now + ReplenishPeriod;
            }
            camp.LastOwned = camp.Owned;
        }
    }

    private static void SyncCampSet()
    {
        ScratchKeys.Clear();
        foreach (KeyValuePair<IntPtr, CampState> pair in Camps)
            ScratchKeys.Add(pair.Key);

        if (_kingdom?.BeggarCamps != null)
        {
            foreach (BeggarCamp camp in _kingdom.BeggarCamps)
            {
                if (!IsCurrentSceneCamp(camp)) continue;
                IntPtr pointer = camp.Pointer;
                ScratchKeys.Remove(pointer);
                CaptureProfile(camp);
                if (!Camps.TryGetValue(pointer, out CampState state)
                    || state.Profile.InstanceId != camp.gameObject.GetInstanceID())
                {
                    state = new CampState
                    {
                        Profile = Profiles[pointer],
                        NextSpawnAt = Time.time + ReplenishPeriod
                    };
                    Camps[pointer] = state;
                }
            }
        }

        for (int i = 0; i < ScratchKeys.Count; i++) Camps.Remove(ScratchKeys[i]);
    }

    private static CampState GetCampState(BeggarCamp camp)
    {
        if (!IsCurrentSceneCamp(camp)) return null;
        return Camps.TryGetValue(camp.Pointer, out CampState state) ? state : null;
    }

    private static CampState FindNearestCamp(Beggar beggar)
    {
        if (beggar == null || beggar.settler || Camps.Count == 0) return null;
        float x = beggar.transform.position.x;
        float best = float.PositiveInfinity;
        CampState chosen = null;
        foreach (CampState state in Camps.Values)
        {
            float distance = Mathf.Abs(x - state.Profile.Camp.transform.position.x);
            if (distance < best || (Mathf.Approximately(distance, best)
                    && (chosen == null || state.Profile.Camp.transform.position.x
                        < chosen.Profile.Camp.transform.position.x)))
            {
                best = distance;
                chosen = state;
            }
        }
        return chosen;
    }

    private static void ConfigureCurrentCampsForCentralMode()
    {
        foreach (CampState state in Camps.Values)
        {
            BeggarCamp camp = state.Profile.Camp;
            if (!IsCurrentSceneCamp(camp)) continue;
            camp.spawnInterval = FallbackSpawnInterval;
            camp.maxBeggars = 0;
        }
    }

    private static void ReplenishCamps()
    {
        if (_phase == Phase.Waiting || !NetworkReady()) return;
        float now = Time.time;
        foreach (CampState state in Camps.Values)
        {
            if (state.Owned >= CampCapacity || now < state.NextSpawnAt) continue;
            state.NextSpawnAt = now + ReplenishPeriod;
            TrySpawnOne(state);
        }
    }

    private static void TrySpawnOne(CampState state)
    {
        BeggarCamp camp = state.Profile.Camp;
        if (!NetworkBigBoss.HasWorldAuth || !NetworkReady()
            || !IsCurrentSceneCamp(camp) || !IsRegisteredCamp(camp)
            || Managers.Inst?.tutorial == null
            || !Managers.Inst.tutorial.IsBeggarSpawnAllowed
            || !HasValidCampHeader(camp) || !HasSyncedBeggarPool())
        {
            return;
        }

        SpawnBefore.Clear();
        foreach (Beggar beggar in _kingdom.Beggars)
            if (IsCurrentSceneBeggar(beggar)) SpawnBefore.Add(beggar.Pointer);

        try { camp.SpawnBeggar(); }
        catch (Exception e)
        {
            LogSpawnFailure("invoke-" + e.GetType().Name);
            return;
        }

        Beggar added = null;
        int addedCount = 0;
        foreach (Beggar beggar in _kingdom.Beggars)
        {
            if (!IsCurrentSceneBeggar(beggar) || SpawnBefore.Contains(beggar.Pointer)) continue;
            added = beggar;
            addedCount++;
        }

        if (addedCount != 1 || added == null)
        {
            LogSpawnFailure("delta-" + addedCount);
            return;
        }

        Owners[added.Pointer] = new Ownership
        {
            Beggar = added,
            Pointer = added.Pointer,
            InstanceId = added.gameObject.GetInstanceID(),
            NetId = GetBeggarNetId(added),
            Epoch = GetBeggarEpoch(added.Pointer),
            Camp = state,
            Seen = true
        };
        state.Owned++;
        state.LastOwned = state.Owned;
    }

    private static bool HasSyncedBeggarPool()
    {
        Managers managers = Managers.Inst;
        Character prefab = managers?.holder?.GetCharacterByTag("Beggar");
        PoolManager poolManager = managers?.pools;
        if (prefab == null || prefab.gameObject == null || poolManager == null) return false;
        Pool pool = Pool.GetPoolFromPrefabAsset(prefab.gameObject);
        if (pool == null || !pool.sync || pool.syncID <= 0
            || poolManager.cachedSyncIdPoolPairs == null
            || !poolManager.cachedSyncIdPoolPairs.ContainsKey((int)pool.syncID)) return false;
        Pool mapped = poolManager.cachedSyncIdPoolPairs[(int)pool.syncID];
        return mapped != null && mapped.Pointer == pool.Pointer;
    }

    private static bool HasValidCampHeader(BeggarCamp camp)
    {
        return !NetworkBigBoss.IsOnline || camp.parentHeaderRef != null;
    }

    private static void LogSpawnFailure(string reason)
    {
        if (_spawnFailureLogged) return;
        _spawnFailureLogged = true;
        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
            "[Population] replenish deferred: " + reason);
    }

    private static void BuildCleanupQueue()
    {
        Cleanup.Clear();
        _cleanupIndex = 0;
        _cleanupRemoved = 0;
        _cleanupSkipped = 0;
        _cleanupBefore = Owners.Count;
        _cleanupAssigned = 0;
        _cleanupProtected = 0;

        foreach (CampState camp in Camps.Values)
        {
            var protectedOwners = new List<Ownership>();
            var safeOwners = new List<Ownership>();
            foreach (Ownership owner in Owners.Values)
            {
                if (owner.Camp != camp || !owner.Seen) continue;
                _cleanupAssigned++;
                if (IsProtected(owner.Beggar)) protectedOwners.Add(owner);
                else safeOwners.Add(owner);
            }

            _cleanupProtected += protectedOwners.Count;
            safeOwners.Sort((a, b) =>
            {
                float ax = Mathf.Abs(a.Beggar.transform.position.x
                    - camp.Profile.Camp.transform.position.x);
                float bx = Mathf.Abs(b.Beggar.transform.position.x
                    - camp.Profile.Camp.transform.position.x);
                int compare = ax.CompareTo(bx);
                return compare != 0 ? compare : a.Pointer.ToInt64().CompareTo(b.Pointer.ToInt64());
            });

            int safeKeep = Mathf.Max(0, CampCapacity - protectedOwners.Count);
            for (int i = safeKeep; i < safeOwners.Count; i++)
            {
                Ownership owner = safeOwners[i];
                Cleanup.Add(new CleanupCandidate
                {
                    Beggar = owner.Beggar,
                    Pointer = owner.Pointer,
                    InstanceId = owner.InstanceId,
                    NetId = owner.NetId,
                    Epoch = owner.Epoch,
                    Camp = camp
                });
            }
        }

        if (Cleanup.Count == 0) FinishCleanup();
    }

    private static void ProcessOneCleanup()
    {
        if (_cleanupIndex >= Cleanup.Count)
        {
            FinishCleanup();
            return;
        }

        CleanupCandidate candidate = Cleanup[_cleanupIndex++];
        if (!NetworkBigBoss.HasWorldAuth || !NetworkReady()
            || !ValidateScene() || !CanDespawnCandidate(candidate))
        {
            _cleanupSkipped++;
            return;
        }

        ReconcileOwnership();
        if (!Owners.TryGetValue(candidate.Pointer, out Ownership owner)
            || owner.InstanceId != candidate.InstanceId || owner.Epoch != candidate.Epoch
            || (owner.NetId != int.MinValue && candidate.NetId != int.MinValue
                && owner.NetId != candidate.NetId)
            || owner.Camp != candidate.Camp
            || candidate.Camp.Owned <= CampCapacity || IsProtected(candidate.Beggar))
        {
            _cleanupSkipped++;
            return;
        }

        try
        {
            Pool.Despawn(candidate.Beggar.gameObject, true);
            Owners.Remove(candidate.Pointer);
            candidate.Camp.Owned = Mathf.Max(0, candidate.Camp.Owned - 1);
            _cleanupRemoved++;
        }
        catch
        {
            _cleanupSkipped++;
        }
    }

    private static bool CanDespawnCandidate(CleanupCandidate candidate)
    {
        Beggar beggar = candidate.Beggar;
        if (beggar == null || beggar.Pointer != candidate.Pointer
            || beggar.gameObject == null
            || !beggar.gameObject.activeInHierarchy
            || beggar.gameObject.GetInstanceID() != candidate.InstanceId
            || GetBeggarEpoch(candidate.Pointer) != candidate.Epoch
            || (candidate.NetId != int.MinValue && GetBeggarNetId(beggar) != candidate.NetId)
            || !IsCurrentSceneBeggar(beggar) || IsProtected(beggar)) return false;
        Pool pool = Pool.GetPoolFromPrefabInstance(beggar.gameObject);
        if (pool == null || !pool.sync) return false;
        if (NetworkBigBoss.IsOnline)
        {
            if (beggar.parentHeaderRef == null || NetworkPostbox.Instance == null) return false;
            CRPCHeader header = NetworkPostbox.Instance.GetHeaderFromDynamicObject(
                beggar.gameObject, false);
            if (header == null || header.NetID != beggar.parentHeaderRef.NetID) return false;
        }
        return true;
    }

    private static bool IsProtected(Beggar beggar)
    {
        if (beggar == null || !IsCurrentSceneBeggar(beggar)) return true;
        try
        {
            return beggar.settler || beggar._baker != null || beggar._isEating
                || beggar.ShouldPlayerControl() || beggar.DespawnOnLoad
                || beggar._character == null || beggar._character.grabbed
                || beggar._character.inert
                || (beggar._petrifiable != null && beggar._petrifiable.IsPetrified)
                || !HasSafeDespawnIdentity(beggar);
        }
        catch { return true; }
    }

    private static void FinishCleanup()
    {
        ReconcileOwnership();
        int residual = 0;
        foreach (CampState camp in Camps.Values)
            residual += Mathf.Max(0, camp.Owned - CampCapacity);

        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[PopulationCleanup] before=" + _cleanupBefore
            + " assigned=" + _cleanupAssigned
            + " protected=" + _cleanupProtected
            + " removed=" + _cleanupRemoved
            + " skipped=" + _cleanupSkipped
            + " residual=" + residual
            + " camps=" + Camps.Count);

        Cleanup.Clear();
        _phase = Phase.Complete;
    }

    private static bool IsRegisteredCamp(BeggarCamp camp)
    {
        if (_kingdom?.BeggarCamps == null) return false;
        foreach (BeggarCamp registered in _kingdom.BeggarCamps)
            if (registered != null && registered.Pointer == camp.Pointer) return true;
        return false;
    }

    private static bool IsCurrentSceneCamp(BeggarCamp camp)
    {
        return IsObjectValid(camp) && _sceneRoot != null && camp.transform != null
            && camp.gameObject.scene.handle == _sceneRoot.gameObject.scene.handle;
    }

    private static bool IsCurrentSceneBeggar(Beggar beggar)
    {
        return IsObjectValid(beggar) && _sceneRoot != null && beggar.transform != null
            && beggar.gameObject.scene.handle == _sceneRoot.gameObject.scene.handle;
    }

    private static bool IsCampStateCurrent(CampState state)
    {
        return state != null && state.Profile != null
            && Camps.TryGetValue(state.Profile.Pointer, out CampState current)
            && ReferenceEquals(current, state) && IsCurrentSceneCamp(state.Profile.Camp);
    }

    private static bool IsObjectValid(Component component)
    {
        try { return component != null && component.gameObject != null; }
        catch { return false; }
    }

    private static void ApplyFallback(BeggarCamp camp)
    {
        if (!IsObjectValid(camp)) return;
        camp.maxBeggars = CampCapacity;
        camp.spawnInterval = FallbackSpawnInterval;
    }

    private static void RestoreFallbackProfiles()
    {
        foreach (CampProfile profile in Profiles.Values)
        {
            try
            {
                if (ProfileStillMatches(profile)) ApplyFallback(profile.Camp);
            }
            catch { /* Continue restoring other camps after one stale wrapper. */ }
        }
    }

    private static void RestoreOriginalProfiles()
    {
        foreach (CampProfile profile in Profiles.Values)
        {
            try
            {
                if (!ProfileStillMatches(profile)) continue;
                profile.Camp.maxBeggars = profile.OriginalMax;
                profile.Camp.spawnInterval = profile.OriginalInterval;
            }
            catch { /* Continue restoring other camps after one stale wrapper. */ }
        }
    }

    private static bool ProfileStillMatches(CampProfile profile)
    {
        return profile != null && IsObjectValid(profile.Camp)
            && profile.Camp.Pointer == profile.Pointer
            && profile.Camp.gameObject.GetInstanceID() == profile.InstanceId;
    }

    private static int GetBeggarNetId(Beggar beggar)
    {
        try { return beggar?.parentHeaderRef != null ? beggar.parentHeaderRef.NetID : int.MinValue; }
        catch { return int.MinValue; }
    }

    private static int GetBeggarEpoch(IntPtr pointer)
    {
        if (!BeggarEpochs.TryGetValue(pointer, out int epoch))
        {
            epoch = 1;
            BeggarEpochs[pointer] = epoch;
        }
        return epoch;
    }

    private static bool HasSafeDespawnIdentity(Beggar beggar)
    {
        if (beggar == null || beggar.gameObject == null) return false;
        Pool pool = Pool.GetPoolFromPrefabInstance(beggar.gameObject);
        if (pool == null || !pool.sync) return false;
        if (!NetworkBigBoss.IsOnline) return true;
        if (beggar.parentHeaderRef == null || NetworkPostbox.Instance == null) return false;
        CRPCHeader header = NetworkPostbox.Instance.GetHeaderFromDynamicObject(
            beggar.gameObject, false);
        return header != null && header.NetID == beggar.parentHeaderRef.NetID;
    }

    private static void SuspendWork()
    {
        _phase = Phase.Suspended;
        Camps.Clear();
        Owners.Clear();
        Cleanup.Clear();
        SpawnBefore.Clear();
        BeggarEpochs.Clear();
    }

    private static void ClearRuntimeState(Phase phase)
    {
        _phase = phase;
        Camps.Clear();
        Owners.Clear();
        Cleanup.Clear();
        SpawnBefore.Clear();
        BeggarEpochs.Clear();
        _campaign = null;
        _kingdom = null;
        _world = null;
        _sceneRoot = null;
        _campaignPointer = IntPtr.Zero;
        _kingdomPointer = IntPtr.Zero;
        _worldPointer = IntPtr.Zero;
        _sceneRootPointer = IntPtr.Zero;
    }
}
