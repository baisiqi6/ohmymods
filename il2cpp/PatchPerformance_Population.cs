using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
#if ANDROID
// Android（Il2CppInterop namespace-prefix 模式）把 Assembly-CSharp 的全局类型放在 Il2Cpp.* 下；
// 本文件与 PC 共用同一份逻辑，仅在此把文件用到的游戏类型显式映射到实际 interop 类型。
// Managers/World/NetworkBigBoss/Pool 由 android/GlobalAliases.cs 的 global alias 提供。
// ANDROID 修订只加平台门、接管/交还凭据与故障停止；PC 分支每行原样保留，预处理输出不变。
using Beggar = Il2Cpp.Beggar;
using BeggarCamp = Il2Cpp.BeggarCamp;
using CampaignSaveData = Il2Cpp.CampaignSaveData;
using Character = Il2Cpp.Character;
using Kingdom = Il2Cpp.Kingdom;
using PoolManager = Il2Cpp.PoolManager;
#endif

namespace KingdomEnhancedMod;

[HarmonyPatch(typeof(CampaignSaveData), nameof(CampaignSaveData.ApplyToScene))]
public static class PopulationPerformanceApplyPatch
{
    [HarmonyPostfix]
    public static void Postfix(CampaignSaveData __instance)
    {
#if ANDROID
        // ANDROID 默认 OFF：冷加载入口先纯托管门，零 native 访问。
        if (!PopulationPerformanceCoordinator.PopulationRequested) return;
#endif
        PopulationPerformanceCoordinator.BeginScene(__instance);
    }
}

/// <summary>
/// Authority-only population governor. Static job storage avoids managed generic instance
/// fields on an injected IL2CPP MonoBehaviour.
/// </summary>
public sealed class PopulationPerformanceCoordinator : MonoBehaviour
{
#if ANDROID
    /// <summary>
    /// Android 平台门：会话总开关 + 分批门 PopulationEnabled，恒为纯托管值。
    /// 关闭且无自有责任时，事件入口/协调器入口/观察入口都在任何 native 读取前退出。
    /// </summary>
    internal static bool PopulationRequested
        => ModConfig.Enabled != null && ModConfig.Enabled.Value
        && ModConfig.PopulationEnabled != null && ModConfig.PopulationEnabled.Value;

    /// <summary>纯托管终态门：本 scene 已停止（OFF/失权/scene 变化/异常/接管失败），事件入口在任何 native 读取前退出；
    /// 真实新 Apply callback（BeginScene）仍可重开，不由故障状态永久禁止。</summary>
    internal static bool SceneStopped => _phase == Phase.Faulted;
#endif

    private static int CampCapacity = ModConfig.DefaultBeggarCampCapacity;
    private static float ReplenishPeriod = ModConfig.DefaultBeggarSpawnIntervalSeconds;
    // Native SlowUpdate adds five seconds after its spawnInterval wait.
    // Keep a positive native wait: fallback cadence is max(6, configured seconds).
    private static float FallbackSpawnInterval => Mathf.Max(1f, ReplenishPeriod - 5f);

    private const float ReconcileInterval = 0.5f;
    private const float StableDelay = 3f;

    private sealed class CampProfile
    {
        public BeggarCamp Camp;
        public IntPtr Pointer;
        public int InstanceId;
        public int OriginalMax;
        public float OriginalInterval;
#if ANDROID
        // 本次接管的已提交值与两字段独立写凭据：只有成功写入过的字段才拥有凭据，
        // 释放时仅当原生当前值与 Applied 精确等值才还原 Original。
        public int AppliedMax;
        public float AppliedInterval;
        public bool MaxOwned;
        public bool IntervalOwned;
#endif
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

    private enum Phase
    {
        Waiting,
        Complete,
        Suspended,
        Faulted
    }

    private static readonly Dictionary<IntPtr, CampProfile> Profiles = new();
    private static readonly Dictionary<IntPtr, CampState> Camps = new();
    private static readonly Dictionary<IntPtr, Ownership> Owners = new();
    private static readonly Dictionary<IntPtr, int> BeggarEpochs = new();
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
#if !ANDROID
    private static float _retryAt;
#endif
    private static float _awaitSceneUntil;
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
#if ANDROID
        // ANDROID：进入即纯托管门（补员门 + 终态门），先于任何 __instance/native 读取。
        if (!PopulationRequested || SceneStopped) return;
#endif
        if (!IsObjectValid(camp)) return;
        // Awake runs on the main thread; seed persisted values even if attachment fails.
        RefreshSettings();
#if !ANDROID
        CaptureProfile(camp);
#endif
        if (!TryEnsureAttached())
        {
#if ANDROID
            // ANDROID：attach 不成功不写营地参数（保持原生），显式日志一次；
            // 不做 PC fallback 覆写、不静默等待/自动恢复。
            LogAndroidOnce(ref _attachFailureLogged, "attach failed; camp stays native");
            return;
#else
            ApplyFallback(camp);
            return;
#endif
        }

#if ANDROID
        // ANDROID：只有真实 current ctx 已就绪（与本 scene 记录的战役一致）、非终态且持有 world
        // authority 时才按凭据接管；ctx 未 ready / Faulted 时保持原生、零字段写。
        if (ProfileReadyForTakeOver(camp))
        {
            CaptureProfile(camp);
            if (Profiles.TryGetValue(camp.Pointer, out CampProfile profile)) TakeOverCamp(profile);
        }
        if (_campaign == null)
            _awaitSceneUntil = Mathf.Max(_awaitSceneUntil, Time.unscaledTime + 10f);
#else
        camp.spawnInterval = FallbackSpawnInterval;
        camp.maxBeggars = NetworkBigBoss.HasWorldAuth ? 0 : CampCapacity;
        if (_campaign == null)
            _awaitSceneUntil = Mathf.Max(_awaitSceneUntil, Time.unscaledTime + 10f);
#endif
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
#if ANDROID
        // ANDROID：入口纯托管门；关闭且无自有责任时零 native 访问、不 attach。
        if (!PopulationRequested)
        {
            ReleaseToNative();
            return;
        }
#endif
        RefreshSettings();
#if ANDROID
        // ANDROID 换 ctx：先把旧 scene 仍拥有的字段一次 exact 交还，再清/重建，不把旧 Original 绑给新 ctx。
        ReleaseToNative();
#endif
        if (!TryEnsureAttached())
        {
#if ANDROID
            // ANDROID：attach 不成功不写营地参数（保持原生），显式日志一次，不 fallback 覆写。
            LogAndroidOnce(ref _attachFailureLogged, "scene attach failed; camps stay native");
#else
            RestoreFallbackProfiles();
#endif
            return;
        }

        Managers managers = Managers.Inst;
        Kingdom kingdom = managers?.kingdom;
        World world = managers?.world;
        Transform sceneRoot = world?.gameLayer;
        if (campaign == null || kingdom == null || world == null || sceneRoot == null)
        {
#if ANDROID
            // ANDROID：ctx 缺失不写营地参数（保持原生），显式日志一次，不 fallback 覆写。
            LogAndroidOnce(ref _sceneContextLogged, "scene context missing; camps stay native");
#else
            RestoreFallbackProfiles();
#endif
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
        PopulationGrounding.Begin(world, sceneRoot, _generation);
        _phase = Phase.Waiting;
        _stableAt = Time.time + StableDelay;
        _nextReconcileAt = Time.time;
        _faultLogged = false;
        _spawnFailureLogged = false;
        _awaitSceneUntil = 0f;
        Camps.Clear();
        Owners.Clear();
        BeggarEpochs.Clear();
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
#if ANDROID
        // ANDROID：身份判定用纯托管 ReferenceEquals，先于任何 Unity/原生比较；随后才是故障/OFF 纯托管门。
        if (!ReferenceEquals(_instance, this)) return;
#else
        if (_instance != this) return;
#endif

        try
        {
#if ANDROID
            // ANDROID：已交还停止（失权/scene 变化/异常/Spawn 故障）后不自动恢复，等新 scene 或玩家重新 OFF→ON。
            if (_phase == Phase.Faulted) return;
            // ANDROID 默认 OFF：协调器入口纯托管门。关闭时若仍有自有责任，沿唯一释放方法
            // 一次交还仍拥有字段后停止；无自有责任时零 native 访问。
            if (!PopulationRequested)
            {
                StopAndRelease();
                return;
            }
#endif
            // Settings are read on the Unity thread at the existing reconciliation cadence.
            bool reconcileDue = Time.time >= _nextReconcileAt;
            if (reconcileDue)
            {
                _nextReconcileAt = Time.time + ReconcileInterval;
                RefreshSettings();
            }

            if (!ModConfig.Enabled.Value)
            {
                RestoreOriginalProfiles();
                SuspendWork();
                return;
            }

            if (!NetworkBigBoss.HasWorldAuth)
            {
#if ANDROID
                // ANDROID 失权＝释放触发点之一：唯一释放方法一次交还仍拥有字段，然后停止本 scene 中央工作。
                LogAndroidOnce(ref _releaseLogged, "authority lost; released to native");
                StopAndRelease();
#else
                RestoreFallbackProfiles();
                SuspendWork();
#endif
                return;
            }

            if (_campaign == null)
            {
#if ANDROID
                // ANDROID：晚营地有界等待窗口内保持原生；窗口结束仍未接入则显式停止，不无限恢复。
                if (Time.unscaledTime < _awaitSceneUntil) return;
                LogAndroidOnce(ref _sceneContextLogged, "no scene attached within the wait window; released");
                StopAndRelease();
                return;
#else
                if (Time.unscaledTime < _awaitSceneUntil) return;
                RestoreFallbackProfiles();
                SuspendWork();
                return;
#endif
            }

            if (!ValidateScene())
            {
#if ANDROID
                // ANDROID scene 变化＝释放触发点之一：一次交还后停止本 scene 中央工作。
                LogAndroidOnce(ref _releaseLogged, "scene changed; released to native");
                StopAndRelease();
#else
                RestoreFallbackProfiles();
                ClearRuntimeState(Phase.Suspended);
#endif
                return;
            }

            // Native Haglet waits use scaled game time. Do not replenish while the
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
#if !ANDROID
                if (Time.unscaledTime < _retryAt) return;
                _phase = Phase.Waiting;
                _stableAt = Time.time + StableDelay;
#endif
            }

            if (reconcileDue)
            {
                ReconcileOwnership();
#if ANDROID
                // ANDROID：0.5s 归属节拍只做接管与计数，不做 PC 的反复覆写；新营地沿原 SyncCampSet 捕获。
                TakeOverCurrentCamps();
#else
                ConfigureCurrentCampsForCentralMode();
#endif
            }

            if (_phase == Phase.Waiting && Time.time >= _stableAt && NetworkReady())
            {
                ReconcileOwnership();
                // Capacity limits future spawning only. Never despawn excess loaded NPCs.
                _phase = Phase.Complete;
            }

            if (_phase != Phase.Suspended && _phase != Phase.Faulted)
                ReplenishCamps();
        }
        catch (Exception e)
        {
#if ANDROID
            // ANDROID：异常即停止本 scene 中央工作并一次交还已确证字段；不做 2s 自动恢复、不盲目重做。
            LogAndroidOnce(ref _faultLogged, "governor stopped; released to native. error=" + e.GetType().Name);
            StopAndRelease();
#else
            RestoreFallbackProfiles();
            _phase = Phase.Faulted;
            _retryAt = Time.unscaledTime + 2f;
            if (!_faultLogged)
            {
                _faultLogged = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[Population] governor failed; fallback capacity=" + CampCapacity
                    + " cadence=" + (FallbackSpawnInterval + 5f) + " error="
                    + e.GetType().Name);
            }
#endif
        }
    }

    private void OnDisable()
    {
#if ANDROID
        // ANDROID：纯托管身份判定与终态锁先于 Unity 比较；禁用即释放触发点之一。
        if (!ReferenceEquals(_instance, this)) return;
#else
        if (_instance != this) return;
#endif
#if ANDROID
        // ANDROID：注入组件禁用＝释放触发点之一，一次交还仍拥有字段并清上下文；不按配置走 PC fallback/original 分支。
        LogAndroidOnce(ref _releaseLogged, "coordinator disabled; released to native");
        StopAndRelease();
#else
        if (ModConfig.Enabled.Value) RestoreFallbackProfiles();
        else RestoreOriginalProfiles();
        ClearRuntimeState(Phase.Suspended);
#endif
        _instance = null;
    }

    private void OnDestroy()
    {
#if ANDROID
        if (ReferenceEquals(_instance, this)) _instance = null;
#else
        if (_instance == this) _instance = null;
#endif
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
                bool firstObserved = false;
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
                    firstObserved = true;
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
                PopulationGrounding.Observe(beggar, owner.Camp?.Profile?.Camp, epoch, firstObserved);
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
#if ANDROID
            // ANDROID：差量不确定/故障时显式结束本轮补员，不靠修改字典引发枚举异常退出。
            if (!TrySpawnOne(state)) return;
#else
            TrySpawnOne(state);
#endif
        }
    }

#if ANDROID
    private static bool TrySpawnOne(CampState state)
#else
    private static void TrySpawnOne(CampState state)
#endif
    {
        BeggarCamp camp = state.Profile.Camp;
        if (!NetworkBigBoss.HasWorldAuth || !NetworkReady()
            || !IsCurrentSceneCamp(camp) || !IsRegisteredCamp(camp)
            || Managers.Inst?.tutorial == null
            || !Managers.Inst.tutorial.IsBeggarSpawnAllowed
            || !HasValidCampHeader(camp) || !HasSyncedBeggarPool())
        {
#if ANDROID
            return true;
#else
            return;
#endif
        }

#if ANDROID
        // ANDROID 提交门（非新增 scan）：配置/authority/context 已由上面的前置条件覆盖，
        // 这里只用现 commit 门复核该 Camp 仍拥有本次参数（原生当前值==Applied）。
        CampProfile owned = state.Profile;
        if (!owned.MaxOwned || !owned.IntervalOwned
            || camp.maxBeggars != owned.AppliedMax
            || camp.spawnInterval != owned.AppliedInterval)
        {
            // 外写偏离或未接管：不夺权、不周期覆写、不盲目重做；停止本 scene 中央补员并一次交还仍拥有字段。
            LogAndroidOnce(ref _releaseLogged, "camp parameters drifted or unclaimed; central replenish stopped");
            StopAndRelease();
            return false;
        }
#endif

        SpawnBefore.Clear();
        foreach (Beggar beggar in _kingdom.Beggars)
            if (IsCurrentSceneBeggar(beggar)) SpawnBefore.Add(beggar.Pointer);

        try { camp.SpawnBeggar(); }
        catch (Exception e)
        {
            LogSpawnFailure("invoke-" + e.GetType().Name);
#if ANDROID
            // ANDROID：调用异常不为已付操作做盲重试；停止本 scene 中央工作并一次交还，结束本轮补员。
            StopAndRelease();
            return false;
#else
            return;
#endif
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
#if ANDROID
            // ANDROID：名册差量不确定（0 或 2）＝停止中央补员并一次交还，不在下个 deadline 重做同次结果。
            StopAndRelease();
            return false;
#else
            return;
#endif
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
        PopulationGrounding.Observe(added, camp, GetBeggarEpoch(added.Pointer), false, true);
#if ANDROID
        if (!_spawnLogged)
        {
            _spawnLogged = true;
            try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("ANDROID_CAMP_POPULATION_SPAWN camp="
                + state.Profile.InstanceId + " actor=" + Owners[added.Pointer].InstanceId
                + " epoch=" + Owners[added.Pointer].Epoch + " delta=" + addedCount); }
            catch { /* The native spawn already succeeded; log failure never repeats it. */ }
        }
        return true;
#endif
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

    private static void RefreshSettings()
    {
        int capacity = Mathf.Clamp(ModConfig.BeggarCampCapacity?.Value ?? ModConfig.DefaultBeggarCampCapacity, 1, 20);
        int seconds = Mathf.Clamp(ModConfig.BeggarSpawnIntervalSeconds?.Value ?? ModConfig.DefaultBeggarSpawnIntervalSeconds, 1, 120);
        if (capacity == CampCapacity && seconds == ReplenishPeriod) return;
#if ANDROID
        bool intervalChanged = seconds != ReplenishPeriod;
#endif
        CampCapacity = capacity;
        ReplenishPeriod = seconds;
        // Moving either slider starts one new interval; no catch-up burst or deletion.
        float next = Time.time + ReplenishPeriod;
        foreach (CampState state in Camps.Values) state.NextSpawnAt = next;
#if ANDROID
        // ANDROID：间隔配置变化只在原 RefreshSettings 边界更新已拥有营地的备用 interval（同值不再写）。
        if (intervalChanged) UpdateOwnedIntervals();
#endif
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

#if ANDROID
    private static bool _scopeLogged, _spawnLogged;
    private static bool _attachFailureLogged;
    private static bool _sceneContextLogged;
    private static bool _releaseLogged;

    private static void LogAndroidOnce(ref bool flag, string message)
    {
        if (flag) return;
        flag = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[Population] " + message); }
        catch { }
    }

    /// <summary>
    /// ANDROID 真实设置动作（OFF→ON）只调用一次：读现成 `CampaignSaveData.current` getter
    /// 后复用 BeginScene；取不到当前战役时只记日志、不动任何原生值（不假装已生效）。
    /// </summary>
    internal static void EnableCurrentScene()
    {
        CampaignSaveData campaign;
        try { campaign = CampaignSaveData.current; }
        catch (Exception e) { LogAndroidOnce(ref _sceneContextLogged, "enable read failed: " + e.GetType().Name); return; }
        if (campaign == null)
        {
            LogAndroidOnce(ref _sceneContextLogged, "enable requested but no current campaign scene yet; keeping native");
            return;
        }
        BeginScene(campaign);
    }

    /// <summary>
    /// ANDROID：晚营地是否属于当前 capture 的 layer（只读对象身份，不读写 max/interval 参数）。
    /// </summary>
    internal static bool IsCapturableCamp(BeggarCamp camp)
    {
        try { return _sceneRoot != null && IsObjectValid(camp) && IsCurrentSceneCamp(camp); }
        catch { return false; }
    }

    /// <summary>
    /// 接管门：复用已有 ValidateScene（campaign/world/kingdom/gameLayer/current 身份全部一致）＋
    /// 目标营地属于 capture 时记录的 scene layer；非终态且持 world authority。
    /// 不同 world/scene/layer 的营地永不写参数。
    /// </summary>
    private static bool ProfileReadyForTakeOver(BeggarCamp camp)
    {
        if (SceneStopped || !NetworkBigBoss.HasWorldAuth) return false;
        if (!ValidateScene()) return false;
        return IsCurrentSceneCamp(camp);
    }

    /// <summary>
    /// ANDROID 首次接管：在真正写每个字段之前重捕该字段当前未 owned 的原生值（不把 Awake 早捕获
    /// 当基线），写成功才记凭据。任一 setter 失败即走唯一 Stop：终态锁 + 已确证字段一次 exact 交还，
    /// 不再留下一次 cadence 重试未 owned 字段。
    /// </summary>
    private static bool TakeOverCamp(CampProfile profile)
    {
        if (profile == null || !ProfileStillMatches(profile)) return false;
        BeggarCamp camp = profile.Camp;
        if (!profile.MaxOwned)
        {
            try
            {
                profile.OriginalMax = camp.maxBeggars;
                camp.maxBeggars = 0;
                profile.AppliedMax = 0;
                profile.MaxOwned = true;
            }
            catch (Exception e)
            {
                TakeOverFailed("max", e);
                return false;
            }
        }
        if (!profile.IntervalOwned)
        {
            try
            {
                profile.OriginalInterval = camp.spawnInterval;
                float target = FallbackSpawnInterval;
                camp.spawnInterval = target;
                profile.AppliedInterval = target;
                profile.IntervalOwned = true;
            }
            catch (Exception e)
            {
                TakeOverFailed("interval", e);
                return false;
            }
        }
        ObserveAndroidScope(profile);
        return true;
    }

    // One natural sample per process, receipt values only. Query failure cannot affect gameplay.
    private static void ObserveAndroidScope(CampProfile profile)
    {
        if (_scopeLogged) return;
        _scopeLogged = true;
        try
        {
            bool childOfLayer = profile.Camp.transform.IsChildOf(_sceneRoot);
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("ANDROID_CAMP_POPULATION_SCOPE camp=" + profile.InstanceId
                + " world=" + _worldPointer + " layer=" + _sceneRootPointer + " childOfLayer=" + childOfLayer
                + " originalMax=" + profile.OriginalMax + " originalInterval=" + profile.OriginalInterval
                + " appliedMax=" + profile.AppliedMax + " appliedInterval=" + profile.AppliedInterval);
        }
        catch { /* Diagnostic ends after one attempt; no retry or state repair. */ }
    }

    /// <summary>接管失败＝终态停止本 scene（唯一释放方法归还已确证字段），不整轮重试。</summary>
    private static void TakeOverFailed(string field, Exception e)
    {
        LogAndroidOnce(ref _attachFailureLogged, "takeover " + field + " write failed: " + e.GetType().Name
            + "; stopping scene");
        StopAndRelease();
    }

    /// <summary>0.5s 归属节拍上的首次接管：只碰尚未拥有凭据的当前场景营地；已拥有者零再写。</summary>
    private static void TakeOverCurrentCamps()
    {
        // 同一接管门（纯托管终态门 + ValidateScene + 逐营地 IsCurrentSceneCamp）；不同 world/scene/layer 永不写。
        if (SceneStopped || !ValidateScene()) return;
        foreach (CampState state in Camps.Values)
        {
            CampProfile profile = state.Profile;
            if (profile == null || (profile.MaxOwned && profile.IntervalOwned)) continue;
            if (!IsCurrentSceneCamp(profile.Camp)) continue;
            if (!TakeOverCamp(profile)) return; // 首次失败即结束本轮（终态已由 StopAndRelease 锁定）
            if (_phase == Phase.Faulted) return;
        }
    }

    /// <summary>间隔配置变化时更新已拥有营地的备用 interval：同一已拥有目标值不再写，外部写偏离不夺权。</summary>
    private static void UpdateOwnedIntervals()
    {
        float target = FallbackSpawnInterval;
        foreach (CampProfile profile in Profiles.Values)
        {
            if (!profile.IntervalOwned || !ProfileStillMatches(profile)) continue;
            try
            {
                if (!ProfileStillMatches(profile) || profile.Camp.spawnInterval == target
                    || profile.Camp.spawnInterval != profile.AppliedInterval) continue;
                profile.Camp.spawnInterval = target;
                profile.AppliedInterval = target;
            }
            catch (Exception e)
            {
                // 唯一 Stop：先锁终态再隔离交还，绝不留到下一 cadence 继续写。
                LogAndroidOnce(ref _releaseLogged, "interval update failed: " + e.GetType().Name
                    + "; stopping scene");
                StopAndRelease();
                return;
            }
        }
    }

    /// <summary>归还单个已确证字段：仅当原生当前值==本次 Applied 才还原 Original；尝试后释放责任，不重试。</summary>
    private static void RestoreField(CampProfile profile, bool maxField)
    {
        try
        {
            if (maxField)
            {
                if (profile.MaxOwned && profile.Camp.maxBeggars == profile.AppliedMax)
                    profile.Camp.maxBeggars = profile.OriginalMax;
            }
            else
            {
                if (profile.IntervalOwned && profile.Camp.spawnInterval == profile.AppliedInterval)
                    profile.Camp.spawnInterval = profile.OriginalInterval;
            }
        }
        catch (Exception e)
        {
            LogAndroidOnce(ref _releaseLogged, "restore failed: " + e.GetType().Name);
        }
        if (maxField) profile.MaxOwned = false;
        else profile.IntervalOwned = false;
    }

    /// <summary>
    /// ANDROID 唯一释放方法：一次性处理仍拥有的字段凭据。仅当原生当前值与本次 Applied 精确等值
    /// 才还原 Original（不抢外国 writer 已改变值、不夺权）；无论结果都释放责任并清账，
    /// 不逐帧 Restore、不重试、不镜像。触发点：OFF 真实动作 / 失权 / scene 变化 /
    /// 注入 OnDisable / 异常停止 / Spawn 提交门偏离。
    /// </summary>
    internal static void ReleaseToNative()
    {
        foreach (CampProfile profile in Profiles.Values)
        {
            if (!profile.MaxOwned && !profile.IntervalOwned) continue;
            try
            {
                if (!ProfileStillMatches(profile))
                {
                    profile.MaxOwned = false;
                    profile.IntervalOwned = false;
                    continue;
                }
                if (profile.MaxOwned) RestoreField(profile, true);
                if (profile.IntervalOwned) RestoreField(profile, false);
            }
            catch (Exception e)
            {
                // 单个 profile/字段抛错只隔离本项，不阻断其余仍拥有凭据的归还。
                LogAndroidOnce(ref _releaseLogged, "release isolated a stale profile: " + e.GetType().Name);
                profile.MaxOwned = false;
                profile.IntervalOwned = false;
            }
        }
    }

    /// <summary>
    /// ANDROID 唯一 Stop：先持久锁终态（后续任何事件含 Awake/Update 都不再写），再做 profile/field
    /// 级异常隔离的一次 exact 交还，最后必经清上下文/名单且保留终态（不自动重试、不自动重启）。
    /// </summary>
    internal static void StopAndRelease()
    {
        _phase = Phase.Faulted;
        try { ReleaseToNative(); }
        finally { ClearRuntimeState(Phase.Faulted); }
    }
#endif

    private static void SuspendWork()
    {
        _phase = Phase.Suspended;
        Camps.Clear();
        Owners.Clear();
        SpawnBefore.Clear();
        BeggarEpochs.Clear();
    }

    private static void ClearRuntimeState(Phase phase)
    {
        PopulationGrounding.Reset();
        _phase = phase;
        Camps.Clear();
        Owners.Clear();
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
