using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Greece bank assistants.
///
/// Security/integrity boundary:
/// - the only real Banker remains Kingdom.banker (and therefore the only NetID 903);
/// - assistant prefabs are constructed from rendering/animation data and contain only
///   SpriteRenderer, Animator, Rigidbody2D and PositionSync;
/// - only world-authority scans, claims, despawns coins and calls the atomic deposit entry;
/// - peers receive the four deterministic synced-pool objects and PositionSync updates only.
/// </summary>
public static class PatchEconomy_BankAssistants
{
    internal const float SCAN_INTERVAL = 0.5f;
    internal const float COIN_MATURITY_SECONDS = 3f;
    // Registrar already iterates its central dropped-item list. Using the full float
    // range preserves the promised whole-island scan on unusually long islands.
    internal const float WORLD_SCAN_RANGE = float.MaxValue;
    internal const float TELEPORT_APPROACH_DISTANCE = 2f;
    internal const float PICKUP_DISTANCE = 0.22f;
    internal const float ASSISTANT_RUN_SPEED = 3.2f;
    internal const float ASSISTANT_PATROL_SPEED = 0.8f;
    internal const float PATROL_HALF_WIDTH = 0.35f;
    internal const float WALL_MARGIN = 0.25f;
    internal const int SCAN_BUFFER_SIZE = 1024;
    internal const string ASSISTANT_PREFIX = "KEM_BankAssistant_";

    // Fixed IDs are deliberately outside the native pools and the existing 30000+
    // cross-biome role sequence. Both peers register these in the same fixed order.
    private static readonly short[] PoolSyncIds = { 30120, 30121, 30122, 30123 };
    private static readonly string[] ControllerNames =
    {
        "banker",
        "banker_bamboo",
        "banker_deadlands",
        "banker_norselands"
    };
    internal static readonly float[] HomeOffsets = { -1.65f, -0.75f, 1.05f, 1.95f };

    private static readonly GameObject[] Prefabs = new GameObject[4];
    private static readonly Pool[] Pools = new Pool[4];
    private static Il2CppArrayBase<Banker> _allBankerPrefabs;
    private static bool _registeredCoordinatorType;
    private static bool _loggedControllerSet;
    private static float _nextControllerResolveAt;
    private static string _lastControllerFailure;

    public static void EnsureForMainBanker(Banker banker)
    {
        if (!ModConfig.Enabled.Value || banker == null) return;
        if (BiomeHolder.Inst == null
            || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return;

        EnsureInjectedTypes();

        BankAssistantCoordinator existing = banker.GetComponent<BankAssistantCoordinator>();
        if (existing == null)
            existing = banker.gameObject.AddComponent<BankAssistantCoordinator>();

        BankAssistantCoordinator.AttachTo(banker);
        var managers = Managers.Inst;
        if (managers != null && managers.pools != null)
            EnsurePools(banker, managers.pools);
    }

    public static void HandlePoolManagerRebuilt(PoolManager poolManager)
    {
        if (!ModConfig.Enabled.Value || poolManager == null) return;
        if (BiomeHolder.Inst == null
            || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return;

        EnsureInjectedTypes();
        BankAssistantCoordinator.HandlePoolRebuild(poolManager);
        if (BankAssistantCoordinator.HasMainBanker) return;

        // PoolManager commonly initializes before Castle creates the runtime Banker.
        // Register on both peers from the inert resource prefab so an early host spawn
        // can never arrive before the client knows these fixed pool IDs.
        Banker source = FindBankerPrefab();
        if (source != null) EnsurePools(source, poolManager);
    }

    private static void EnsureInjectedTypes()
    {
        if (_registeredCoordinatorType) return;
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(BankAssistantCoordinator)))
            ClassInjector.RegisterTypeInIl2Cpp(typeof(BankAssistantCoordinator));
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(BankAssistantVisualLifecycle)))
            ClassInjector.RegisterTypeInIl2Cpp(typeof(BankAssistantVisualLifecycle));
        _registeredCoordinatorType = true;
    }

    internal static bool IsAssistantPositionSync(PositionSync positionSync)
    {
        return positionSync != null && positionSync.gameObject != null
            && positionSync.gameObject.name.StartsWith(ASSISTANT_PREFIX, StringComparison.Ordinal);
    }

    internal static void EnsurePools(Banker banker, PoolManager poolManager)
    {
        if (banker == null || poolManager == null) return;

        EnsurePrefabs(banker);
        for (int i = 0; i < Prefabs.Length; i++)
        {
            GameObject prefab = Prefabs[i];
            if (prefab == null) continue;

            short syncId = PoolSyncIds[i];
            Pool byId = null;
            if (poolManager.cachedSyncIdPoolPairs != null
                && poolManager.cachedSyncIdPoolPairs.ContainsKey(syncId))
            {
                byId = poolManager.cachedSyncIdPoolPairs[syncId];
                if (byId == null || byId.prefab == null
                    || byId.prefab.name != prefab.name)
                {
                    string conflict = byId != null && byId.prefab != null
                        ? byId.prefab.name : "<null>";
                    KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                        "[BankAssistants] Refusing syncID " + syncId
                        + ": already owned by " + conflict);
                    Pools[i] = null;
                    continue;
                }
            }

            Pool pool = byId ?? Pool.GetPoolFromPrefabAsset(prefab);
            if (pool == null)
            {
                DestroyOrphanPools(poolManager, prefab);
                pool = poolManager.CreatePoolFor(prefab);
            }
            if (pool == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[BankAssistants] CreatePoolFor failed: " + prefab.name);
                continue;
            }

            pool.preload = 0;
            pool.sync = true;
            pool.syncID = syncId;
            pool.capacity = 0;
            pool.expendable = false;

            if (poolManager.cachedPools != null && !poolManager.cachedPools.Contains(pool))
                poolManager.cachedPools.Add(pool);
            if (poolManager.cachedNamePoolPairs != null)
                poolManager.cachedNamePoolPairs[prefab.name] = pool;
            if (poolManager.cachedSyncIdPoolPairs != null)
                poolManager.cachedSyncIdPoolPairs[syncId] = pool;

            Pools[i] = pool;
        }
    }

    private static void EnsurePrefabs(Banker banker)
    {
        bool allReady = true;
        for (int i = 0; i < Prefabs.Length; i++)
            allReady &= Prefabs[i] != null;
        if (allReady) return;

        // EnsurePools is called from Update. Retry later if the biome asset graph is
        // still loading, but never rescan resources or repeat the same error per frame.
        if (Time.unscaledTime < _nextControllerResolveAt) return;
        _nextControllerResolveAt = Time.unscaledTime + 2f;

        Animator sourceAnimator = banker.GetComponent<Animator>();
        SpriteRenderer sourceRenderer = banker.GetComponent<SpriteRenderer>();
        if (!TryResolveControllers(sourceAnimator, out RuntimeAnimatorController[] controllers))
            return;

        for (int i = 0; i < Prefabs.Length; i++)
        {
            if (Prefabs[i] != null) continue;

            GameObject prefab = new GameObject(ASSISTANT_PREFIX + i + "_" + ControllerNames[i]);
            prefab.SetActive(false);
            prefab.hideFlags = HideFlags.HideAndDontSave;
            prefab.layer = banker.gameObject.layer;
            prefab.transform.localScale = banker.transform.localScale;
            if (i == 2 || i == 3)
            {
                Vector3 visualScale = prefab.transform.localScale;
                visualScale.y = i == 2 ? 1.25f : 1.2f;
                prefab.transform.localScale = visualScale;
            }

            SpriteRenderer renderer = prefab.AddComponent<SpriteRenderer>();
            if (sourceRenderer != null)
            {
                renderer.sprite = sourceRenderer.sprite;
                renderer.sharedMaterial = sourceRenderer.sharedMaterial;
                renderer.color = sourceRenderer.color;
                renderer.flipX = sourceRenderer.flipX;
                renderer.flipY = sourceRenderer.flipY;
                renderer.sortingLayerID = sourceRenderer.sortingLayerID;
                renderer.sortingOrder = sourceRenderer.sortingOrder;
            }

            Animator animator = prefab.AddComponent<Animator>();
            animator.runtimeAnimatorController = controllers[i];
            if (sourceAnimator != null)
            {
                animator.avatar = sourceAnimator.avatar;
                animator.applyRootMotion = false;
                animator.updateMode = sourceAnimator.updateMode;
                animator.cullingMode = sourceAnimator.cullingMode;
            }

            Rigidbody2D body = prefab.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;

            PositionSync positionSync = prefab.AddComponent<PositionSync>();
            positionSync.onConnectPosSync = true;
            positionSync.syncDeltaThreshold = 0.04f;
            positionSync.syncTimeMinInterval = 0.1f;
            positionSync.enforceHeadingSync = true;
            positionSync.fullAccuracyYSync = true;
            positionSync.disableAnimPassthrough = true;
            prefab.AddComponent<BankAssistantVisualLifecycle>();

            // Static invariant: the assistant is not a second bank account or persistence owner.
            if (prefab.GetComponent<Banker>() != null || prefab.GetComponent<Wallet>() != null
                || prefab.GetComponent<Persistent>() != null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[BankAssistants] Forbidden component found on " + prefab.name);
                UnityEngine.Object.Destroy(prefab);
                continue;
            }

            Prefabs[i] = prefab;
        }
    }

    private static bool TryResolveControllers(Animator sourceAnimator,
        out RuntimeAnimatorController[] resolved)
    {
        resolved = new RuntimeAnimatorController[ControllerNames.Length];
        bool ambiguous = false;

        RuntimeAnimatorController liveController = sourceAnimator != null
            ? sourceAnimator.runtimeAnimatorController : null;
        ConsiderController(liveController, resolved, ref ambiguous);
        AnimatorOverrideController liveOverride = liveController as AnimatorOverrideController;
        if (liveOverride != null)
            ConsiderController(liveOverride.runtimeAnimatorController, resolved, ref ambiguous);

        // These preload entries are the authoritative biome asset graph. Unlike
        // Resources.LoadAll(""), they retain direct references to animator overrides
        // that do not live in a Resources folder.
        BiomeHolder holder = BiomeHolder.Inst;
        if (holder != null)
        {
            if (holder.biomePreloadData != null)
            {
                for (int i = 0; i < holder.biomePreloadData.Length; i++)
                    GatherSwapControllers(holder.biomePreloadData[i], resolved, ref ambiguous);
            }
            if (holder.biomeData != null)
            {
                for (int i = 0; i < holder.biomeData.Length; i++)
                {
                    BiomeData data = holder.biomeData[i];
                    if (data != null)
                        GatherSwapControllers(data.swapData, resolved, ref ambiguous);
                }
            }
        }

        // Runtime discovery catches already-loaded controllers supplied outside the
        // preload tables without assuming a Resources path.
        var loadedControllers = Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>();
        for (int i = 0; i < loadedControllers.Length; i++)
            ConsiderController(loadedControllers[i], resolved, ref ambiguous);
        var loadedOverrides = Resources.FindObjectsOfTypeAll<AnimatorOverrideController>();
        for (int i = 0; i < loadedOverrides.Length; i++)
        {
            ConsiderController(loadedOverrides[i], resolved, ref ambiguous);
            ConsiderController(loadedOverrides[i]?.runtimeAnimatorController, resolved, ref ambiguous);
        }

        bool complete = !ambiguous;
        string missing = "";
        bool duplicateInstance = false;
        var ids = new HashSet<int>();
        for (int i = 0; i < resolved.Length; i++)
        {
            RuntimeAnimatorController controller = resolved[i];
            if (controller == null)
            {
                complete = false;
                missing += (missing.Length == 0 ? "" : ",") + ControllerNames[i];
            }
            else if (!ids.Add(controller.GetInstanceID()))
            {
                complete = false;
                duplicateInstance = true;
            }
        }

        if (!complete)
        {
            string failure = "missing=" + (missing.Length == 0 ? "none" : missing)
                + "; ambiguousNames=" + ambiguous
                + "; duplicateInstances=" + duplicateInstance;
            if (!string.Equals(_lastControllerFailure, failure, StringComparison.Ordinal))
            {
                _lastControllerFailure = failure;
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[BankAssistants] Banker controller set unavailable; assistants fail closed ("
                    + failure + ")");
            }
            return false;
        }

        _lastControllerFailure = null;
        if (!_loggedControllerSet)
        {
            _loggedControllerSet = true;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[BankAssistants] Resolved unique banker controllers: banker, banker_bamboo, banker_deadlands, banker_norselands");
        }
        return complete;
    }

    private static void GatherSwapControllers(BiomeSwapData swapData,
        RuntimeAnimatorController[] resolved, ref bool ambiguous)
    {
        if (swapData == null || swapData.animatorSwapPool == null) return;
        for (int i = 0; i < swapData.animatorSwapPool.Count; i++)
        {
            BiomeSwapData.AnimatorSwapData item = swapData.animatorSwapPool[i];
            if (item == null) continue;
            ConsiderController(item.original, resolved, ref ambiguous);
            ConsiderController(item.swap, resolved, ref ambiguous);
        }
    }

    private static void ConsiderController(RuntimeAnimatorController candidate,
        RuntimeAnimatorController[] resolved, ref bool ambiguous)
    {
        if (candidate == null) return;
        for (int i = 0; i < ControllerNames.Length; i++)
        {
            if (!string.Equals(candidate.name, ControllerNames[i],
                    StringComparison.OrdinalIgnoreCase)) continue;
            if (resolved[i] == null)
                resolved[i] = candidate;
            else if (resolved[i].GetInstanceID() != candidate.GetInstanceID())
            {
                ambiguous = true;
            }
            return;
        }
    }

    private static Banker FindBankerPrefab()
    {
        if (_allBankerPrefabs == null) _allBankerPrefabs = Resources.LoadAll<Banker>("");
        Banker fallback = null;
        for (int i = 0; i < _allBankerPrefabs.Length; i++)
        {
            Banker candidate = _allBankerPrefabs[i];
            if (candidate == null) continue;
            fallback ??= candidate;
            if (string.Equals(candidate.gameObject.name, "Banker", StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        return fallback;
    }

    private static void DestroyOrphanPools(PoolManager poolManager, GameObject prefab)
    {
        Pool[] physical = poolManager.GetComponentsInChildren<Pool>();
        for (int i = 0; i < physical.Length; i++)
        {
            Pool item = physical[i];
            if (item != null && item.prefab == prefab
                && Pool.GetPoolFromPrefabAsset(prefab) == null)
                UnityEngine.Object.Destroy(item.gameObject);
        }
    }

    internal static GameObject GetPrefab(int index)
    {
        return index >= 0 && index < Prefabs.Length ? Prefabs[index] : null;
    }

    internal static void ClearPoolHandles()
    {
        for (int i = 0; i < Pools.Length; i++) Pools[i] = null;
    }
}

/// <summary>
/// One central authority-side scheduler. All managed state is static because injected
/// IL2CPP MonoBehaviours must not rely on managed generic instance-field layout.
/// </summary>
public class BankAssistantCoordinator : MonoBehaviour
{
    private const float SCAN_INTERVAL = PatchEconomy_BankAssistants.SCAN_INTERVAL;
    private const float COIN_MATURITY_SECONDS = PatchEconomy_BankAssistants.COIN_MATURITY_SECONDS;
    private const float WORLD_SCAN_RANGE = PatchEconomy_BankAssistants.WORLD_SCAN_RANGE;
    private const float TELEPORT_APPROACH_DISTANCE = PatchEconomy_BankAssistants.TELEPORT_APPROACH_DISTANCE;
    private const float PICKUP_DISTANCE = PatchEconomy_BankAssistants.PICKUP_DISTANCE;
    private const float ASSISTANT_RUN_SPEED = PatchEconomy_BankAssistants.ASSISTANT_RUN_SPEED;
    private const float ASSISTANT_PATROL_SPEED = PatchEconomy_BankAssistants.ASSISTANT_PATROL_SPEED;
    private const float PATROL_HALF_WIDTH = PatchEconomy_BankAssistants.PATROL_HALF_WIDTH;
    private const float WALL_MARGIN = PatchEconomy_BankAssistants.WALL_MARGIN;
    private const int SCAN_BUFFER_SIZE = PatchEconomy_BankAssistants.SCAN_BUFFER_SIZE;
    private const string ASSISTANT_PREFIX = PatchEconomy_BankAssistants.ASSISTANT_PREFIX;
    private static readonly float[] HomeOffsets = PatchEconomy_BankAssistants.HomeOffsets;

    private sealed class ObservedCoin
    {
        public DroppableCurrency Coin;
        public float FirstObservedAt;
    }

    private sealed class AssistantState
    {
        public readonly int Index;
        public GameObject Actor;
        public Animator Animator;
        public PositionSync PositionSync;
        public DroppableCurrency Target;
        public PickUpPolicy OriginalPolicy;
        public int CarriedCoins;
        public int UncreditedCoins;
        public bool Moving;
        public bool PatrolRight;
        public float PatrolResumeAt;

        public AssistantState(int index) { Index = index; }
    }

    private static BankAssistantCoordinator _instance;
    private static Banker _mainBanker;
    private static readonly AssistantState[] Assistants =
    {
        new AssistantState(0), new AssistantState(1),
        new AssistantState(2), new AssistantState(3)
    };
    private static readonly Dictionary<int, ObservedCoin> Observed = new();
    private static readonly Dictionary<int, int> Claims = new();
    private static readonly HashSet<int> SeenThisScan = new();
    private static readonly List<int> RemovalBuffer = new();
    private static readonly List<DroppableCurrency> MatureBuffer = new();
    private static readonly HashSet<string> LoggedDiagnosticStates = new();
    private static readonly Il2CppReferenceArray<DroppableCurrency> ScanBuffer =
        new Il2CppReferenceArray<DroppableCurrency>(SCAN_BUFFER_SIZE);
    private static float _nextScanAt;
    private static float _nextDiagnosticsAt;
    private static bool _hadAuthority;
    private static bool _loggedReady;
    private static bool _loggedFirstAssignment;
    private static bool _loggedFirstSubmission;
    private static int _collectorIndex = -1;
    private static int _nextCollectorIndex;
    private static readonly int SpeedParameter = Animator.StringToHash("Speed");

    public BankAssistantCoordinator(IntPtr ptr) : base(ptr) { }
    public static bool HasMainBanker => _instance != null && _mainBanker != null;

    public static void AttachTo(Banker banker)
    {
        if (banker == null) return;
        BankAssistantCoordinator component = banker.GetComponent<BankAssistantCoordinator>();
        if (component == null) return;

        if (_instance != null && _instance != component)
            ResetAll(releaseClaims: NetworkBigBoss.HasWorldAuth, destroyActors: false);

        _instance = component;
        _mainBanker = banker;
        _nextScanAt = Time.time + SCAN_INTERVAL;
        _nextDiagnosticsAt = Time.time;
    }

    public static void HandlePoolRebuild(PoolManager poolManager)
    {
        PatchEconomy_BankAssistants.ClearPoolHandles();
        if (_instance == null || _mainBanker == null) return;

        FlushUncreditedCoins();
        ResetAll(releaseClaims: NetworkBigBoss.HasWorldAuth, destroyActors: true, syncDespawn: false);
        PatchEconomy_BankAssistants.EnsurePools(_mainBanker, poolManager);
    }

    private void Update()
    {
        if (_instance != this || _mainBanker == null) return;
        if (!ModConfig.Enabled.Value)
        {
            if (NetworkBigBoss.HasWorldAuth) FlushUncreditedCoins();
            ResetAll(releaseClaims: NetworkBigBoss.HasWorldAuth, destroyActors: true, syncDespawn: true);
            return;
        }
        if (BiomeHolder.Inst == null
            || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return;

        Managers managers = Managers.Inst;
        PoolManager poolManager = managers != null ? managers.pools : null;
        // Both peers must keep retrying deterministic fixed-pool registration after
        // a late controller load. Clients stop immediately afterwards and never
        // spawn assistants, claim currency or touch the ledger.
        if (poolManager != null)
            PatchEconomy_BankAssistants.EnsurePools(_mainBanker, poolManager);

        bool authority = NetworkBigBoss.HasWorldAuth;
        if (!authority)
        {
            if (_hadAuthority)
            {
                FlushUncreditedCoins();
                ResetAll(releaseClaims: false, destroyActors: false);
            }
            _hadAuthority = false;
            return;
        }
        _hadAuthority = true;
        if (Mathf.Approximately(Time.timeScale, 0f)) return;

        if (poolManager == null || managers.world == null || managers.kingdom == null) return;

        EnsureFourActors(managers.world.gameLayer);

        if (Time.time >= _nextScanAt)
        {
            _nextScanAt = Time.time + SCAN_INTERVAL;
            ScanAndDispatch(managers);
        }
        UpdateMovingAssistants();
        UpdateIdlePatrols(managers.kingdom);
    }

    private void OnDestroy()
    {
        if (_instance != this) return;
        if (NetworkBigBoss.HasWorldAuth) FlushUncreditedCoins();
        ResetAll(releaseClaims: NetworkBigBoss.HasWorldAuth, destroyActors: false);
        _instance = null;
        _mainBanker = null;
        _loggedReady = false;
    }

    private static void EnsureFourActors(Transform gameLayer)
    {
        // During host migration or a same-scene pool rebuild, adopt already-synced
        // instances before spawning. This is also the runtime duplicate guard.
        Il2CppArrayBase<PositionSync> existingActors = null;
        for (int i = 0; i < Assistants.Length; i++)
        {
            AssistantState helper = Assistants[i];
            if (helper.Actor != null && helper.Actor.activeInHierarchy) continue;
            if (helper.Target != null) ReleaseTarget(helper);
            if (_collectorIndex == i) _collectorIndex = -1;
            if (existingActors == null) existingActors = UnityEngine.Object.FindObjectsOfType<PositionSync>();
            string marker = ASSISTANT_PREFIX + i + "_";
            for (int j = 0; j < existingActors.Length; j++)
            {
                PositionSync candidate = existingActors[j];
                if (candidate == null || candidate.gameObject == null
                    || !candidate.gameObject.activeInHierarchy
                    || !candidate.gameObject.name.StartsWith(marker, StringComparison.Ordinal)) continue;
                helper.Actor = candidate.gameObject;
                helper.Animator = helper.Actor.GetComponent<Animator>();
                helper.PositionSync = candidate;
                helper.PatrolRight = (i & 1) == 0;
                helper.PatrolResumeAt = Time.time + PatrolPauseSeconds(i);
                if (NetworkBigBoss.IsOnline && candidate.parentHeaderRef != null)
                    candidate.SetSyncAndRemote(true, true);
                break;
            }
        }

        for (int i = 0; i < Assistants.Length; i++)
        {
            AssistantState helper = Assistants[i];
            if (helper.Actor != null && helper.Actor.activeInHierarchy) continue;

            GameObject prefab = PatchEconomy_BankAssistants.GetPrefab(i);
            if (prefab == null || Pool.GetPoolFromPrefabAsset(prefab) == null) continue;

            Vector3 home = GetHomePosition(i);
            GameObject actor = Pool.SpawnGO(
                prefab, home, Quaternion.identity, gameLayer,
                allowInstantiate: false, allowCreatePool: false, assertNonNullPrefab: true);
            if (actor == null) continue;

            helper.Actor = actor;
            helper.Animator = actor.GetComponent<Animator>();
            helper.PositionSync = actor.GetComponent<PositionSync>();
            helper.Target = null;
            helper.CarriedCoins = 0;
            helper.UncreditedCoins = 0;
            helper.Moving = false;
            helper.PatrolRight = (i & 1) == 0;
            helper.PatrolResumeAt = Time.time + PatrolPauseSeconds(i);
            SetAnimationSpeed(helper, 0f);
            if (NetworkBigBoss.IsOnline && helper.PositionSync != null
                && helper.PositionSync.parentHeaderRef != null)
            {
                // Explicitly establish authority/client direction after the synced pool
                // has registered its dynamic header. Clients only receive transforms.
                helper.PositionSync.SetSyncAndRemote(true, true);
            }
        }

        if (!_loggedReady)
        {
            int ready = 0;
            for (int i = 0; i < Assistants.Length; i++)
                if (Assistants[i].Actor != null) ready++;
            if (ready == 4)
            {
                _loggedReady = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[BankAssistants] Authority spawned deterministic 4-assistant pool");
            }
        }
    }

    private static void ScanAndDispatch(Managers managers)
    {
        DroppableRegistrar registrar = managers.dropManager;
        Kingdom kingdom = managers.kingdom;
        if (registrar == null || kingdom == null || _mainBanker == null) return;
        if (!PatchEconomy_Banker.TryGetMainBankerDomain(
                kingdom, out float domainLeft, out float domainRight))
        {
            for (int i = 0; i < Assistants.Length; i++)
                if (Assistants[i].Target != null) ReleaseTarget(Assistants[i]);
            if (_collectorIndex >= 0) FinishCollector(returnHome: true);
            Observed.Clear();
            Claims.Clear();
            MatureBuffer.Clear();
            return;
        }

        SeenThisScan.Clear();
        MatureBuffer.Clear();

        int count;
        registrar.GetDroppablesInRange<DroppableCurrency>(
            kingdom.campfirePosition, WORLD_SCAN_RANGE, ScanBuffer, out count, null);

        float now = Time.time;
        int ordinaryPlayerCoins = 0;
        int outsideCoins = 0;
        int externallyClaimed = 0;
        for (int i = 0; i < count; i++)
        {
            DroppableCurrency coin = ScanBuffer[i];
            if (coin != null && coin.isActiveAndEnabled && coin.gameObject != null
                && coin.droppedBy == DropType.Player
                && coin.CurrencyType == CurrencyType.Coins && !coin.IsFake())
            {
                ordinaryPlayerCoins++;
                if (!PatchEconomy_Banker.IsInMainBankerDomain(
                        coin.transform.position.x, domainLeft, domainRight))
                {
                    outsideCoins++;
                    int coinId = coin.gameObject.GetInstanceID();
                    if (coin.friendlyClaimer != null && !Claims.ContainsKey(coinId))
                        externallyClaimed++;
                }
            }
            if (!IsTrackableCoin(coin, domainLeft, domainRight)) continue;

            int id = coin.gameObject.GetInstanceID();
            SeenThisScan.Add(id);
            if (!Observed.TryGetValue(id, out ObservedCoin observation))
            {
                observation = new ObservedCoin { Coin = coin, FirstObservedAt = now };
                Observed[id] = observation;
            }
            else
            {
                observation.Coin = coin;
            }

            if (!Claims.ContainsKey(id)
                && now - observation.FirstObservedAt >= COIN_MATURITY_SECONDS)
                MatureBuffer.Add(coin);
        }

        RemovalBuffer.Clear();
        foreach (var pair in Observed)
        {
            if (!SeenThisScan.Contains(pair.Key) && !Claims.ContainsKey(pair.Key))
                RemovalBuffer.Add(pair.Key);
        }
        for (int i = 0; i < RemovalBuffer.Count; i++) Observed.Remove(RemovalBuffer[i]);

        MatureBuffer.Sort(CompareCoinsDeterministically);

        for (int i = 0; i < Assistants.Length; i++)
        {
            AssistantState helper = Assistants[i];
            if (helper.Target != null && !IsValidOwnedTarget(helper))
                ReleaseTarget(helper);
            if (i != _collectorIndex && helper.Target != null)
                ReleaseTarget(helper);
        }

        if (_collectorIndex >= 0)
        {
            AssistantState collector = Assistants[_collectorIndex];
            int capacity = GetAssistantCapacity();
            if (collector.Actor == null || !collector.Actor.activeInHierarchy)
            {
                FinishCollector(returnHome: false);
            }
            else if (collector.CarriedCoins >= capacity)
            {
                FinishCollector(returnHome: true);
            }
        }

        if (_collectorIndex < 0 && MatureBuffer.Count > 0)
            SelectNextCollector();

        if (_collectorIndex >= 0)
        {
            AssistantState collector = Assistants[_collectorIndex];
            if (collector.Target == null)
            {
                bool assigned = false;
                for (int j = 0; j < MatureBuffer.Count; j++)
                {
                    DroppableCurrency candidate = MatureBuffer[j];
                    if (candidate == null) continue;
                    int candidateId = candidate.gameObject.GetInstanceID();
                    if (Claims.ContainsKey(candidateId)) continue;
                    if (!TryAssign(collector, candidate)) continue;
                    assigned = true;
                    break;
                }

                // A temporarily claimed/header-blocked mature coin keeps the same
                // collector. An actually empty mature batch completes and rotates.
                if (!assigned && MatureBuffer.Count == 0)
                    FinishCollector(returnHome: true);
            }
        }

        bool relevantDiagnostics = outsideCoins > 0 || Observed.Count > 0
            || MatureBuffer.Count > 0 || Claims.Count > 0 || externallyClaimed > 0;
        string diagnosticSignature = relevantDiagnostics
            ? (outsideCoins > 0 ? "O" : "-")
                + (Observed.Count > 0 ? "T" : "-")
                + (MatureBuffer.Count > 0 ? "M" : "-")
                + (Claims.Count > 0 ? "A" : "-")
                + (externallyClaimed > 0 ? "C" : "-")
            : null;
        if (relevantDiagnostics && now >= _nextDiagnosticsAt
            && LoggedDiagnosticStates.Add(diagnosticSignature))
        {
            _nextDiagnosticsAt = now + 5f;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                $"[BankAssistants] scan observed={count}, playerCoins={ordinaryPlayerCoins}, outside={outsideCoins}, tracked={Observed.Count}, mature={MatureBuffer.Count}, assigned={Claims.Count}, externallyClaimed={externallyClaimed}");
        }
    }

    private static bool IsTrackableCoin(DroppableCurrency coin,
        float domainLeft, float domainRight)
    {
        if (coin == null || !coin.isActiveAndEnabled || coin.gameObject == null) return false;
        if (coin.droppedBy != DropType.Player || coin.CurrencyType != CurrencyType.Coins) return false;
        if (coin.IsFake()) return false;

        float x = coin.transform.position.x;
        // The main Banker owns the symmetric second-wall domain (with safe fallbacks).
        // Assistants also collect from built outer layers beyond that inner domain.
        if (PatchEconomy_Banker.IsInMainBankerDomain(x, domainLeft, domainRight))
            return false;
        // A temporary native claim must not reset the three-second maturity clock.
        // TryFriendlyClaim remains the atomic assignment gate below.
        return true;
    }

    private static int CompareCoinsDeterministically(DroppableCurrency left, DroppableCurrency right)
    {
        if (left == null) return right == null ? 0 : 1;
        if (right == null) return -1;
        int xCompare = left.transform.position.x.CompareTo(right.transform.position.x);
        if (xCompare != 0) return xCompare;
        return left.gameObject.GetInstanceID().CompareTo(right.gameObject.GetInstanceID());
    }

    private static bool SelectNextCollector()
    {
        int capacity = GetAssistantCapacity();
        for (int offset = 0; offset < Assistants.Length; offset++)
        {
            int index = (_nextCollectorIndex + offset) % Assistants.Length;
            AssistantState helper = Assistants[index];
            if (helper.Actor == null || !helper.Actor.activeInHierarchy
                || helper.CarriedCoins >= capacity) continue;

            _collectorIndex = index;
            _nextCollectorIndex = (index + 1) % Assistants.Length;
            return true;
        }
        return false;
    }

    private static void FinishCollector(bool returnHome)
    {
        if (_collectorIndex < 0 || _collectorIndex >= Assistants.Length) return;
        AssistantState helper = Assistants[_collectorIndex];
        if (helper.Target != null) ReleaseTarget(helper);
        if (returnHome) TeleportHomeAndDeposit(helper);
        _collectorIndex = -1;
    }

    private static bool TryAssign(AssistantState helper, DroppableCurrency coin)
    {
        if (helper.Actor == null || coin == null) return false;
        if (NetworkBigBoss.IsOnline
            && (!NetworkBigBoss.HasClientCaughtUp || helper.PositionSync == null
                || helper.PositionSync.parentHeaderRef == null
                || coin.parentHeaderRef == null)) return false;
        int id = coin.gameObject.GetInstanceID();
        if (Claims.ContainsKey(id)) return false;
        if (!coin.TryFriendlyClaim(helper.Actor, 20f)) return false;

        helper.OriginalPolicy = coin.pickUpPolicy;
        coin.pickUpPolicy = PickUpPolicy.OnlyClaimer;
        coin.SendPolicyRPC();
        Claims[id] = helper.Index;
        helper.Target = coin;

        float coinX = coin.transform.position.x;
        bool needsApproachTeleport = helper.CarriedCoins == 0
            || Mathf.Abs(helper.Actor.transform.position.x - coinX) > 6f;
        if (needsApproachTeleport)
        {
            float castleDirection = Mathf.Sign(Managers.Inst.kingdom.campfirePosition - coinX);
            if (Mathf.Approximately(castleDirection, 0f)) castleDirection = 1f;
            Vector3 approach = coin.transform.position;
            approach.x += castleDirection * TELEPORT_APPROACH_DISTANCE;
            approach.z = helper.Actor.transform.position.z;
            helper.Actor.transform.position = approach;
            SendFullPosition(helper);
        }
        FaceTowards(helper.Actor.transform, coinX);
        helper.Moving = true;
        SetAnimationSpeed(helper, ASSISTANT_RUN_SPEED);
        if (!_loggedFirstAssignment)
        {
            _loggedFirstAssignment = true;
            float age = Observed.TryGetValue(id, out ObservedCoin observation)
                ? Time.time - observation.FirstObservedAt : -1f;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                $"[BankAssistants] first assignment helper={helper.Index}, coin={id}, x={coinX:F2}, age={age:F2}s");
        }
        return true;
    }

    private static void UpdateMovingAssistants()
    {
        float step = ASSISTANT_RUN_SPEED * Time.deltaTime;
        for (int i = 0; i < Assistants.Length; i++)
        {
            AssistantState helper = Assistants[i];
            if (helper.Actor == null || helper.Target == null) continue;
            if (!IsValidOwnedTarget(helper))
            {
                ReleaseTarget(helper);
                continue;
            }

            Vector3 current = helper.Actor.transform.position;
            Vector3 target = helper.Target.transform.position;
            target.y = current.y;
            target.z = current.z;
            FaceTowards(helper.Actor.transform, target.x);
            helper.Actor.transform.position = Vector3.MoveTowards(current, target, step);

            if (Mathf.Abs(helper.Actor.transform.position.x - target.x) > PICKUP_DISTANCE) continue;

            DroppableCurrency collected = helper.Target;
            int id = collected.gameObject.GetInstanceID();
            if (!CanCommitPickup(helper, collected)
                || Pool.GetPoolByInstance(collected.gameObject) == null)
            {
                ReleaseTarget(helper);
                continue;
            }

            // Authority main thread transaction: freeze/mark the physical coin first and
            // sync that state, then credit exactly one coin. Only a successful credit is
            // followed by pool despawn, so the economic and physical totals cannot diverge.
            collected.SetFake(true);
            collected.pickedUp = true;
            if (NetworkBigBoss.IsOnline) collected.SyncPickedUpAndFake();
            int accepted = PatchEconomy_Banker.DepositFromAssistant(_mainBanker, 1);
            if (accepted != 1)
            {
                collected.pickedUp = false;
                collected.SetFake(false);
                if (NetworkBigBoss.IsOnline) collected.SyncPickedUpAndFake();
                ReleaseTarget(helper);
                continue;
            }

            if (!_loggedFirstSubmission)
            {
                _loggedFirstSubmission = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    $"[BankAssistants] first submission helper={helper.Index}, coin={id}, accepted={accepted}");
            }

            // Do not start DroppableCurrency.MoveTo(..., destroyAfter:true): that creates
            // another asynchronous in-flight ownership window. The assistant already ran
            // the visible final segment; synced pool despawn is the deterministic network
            // equivalent after the pickup state and ledger commit have both completed.
            Pool.Despawn(collected.gameObject, true);
            Claims.Remove(id);
            Observed.Remove(id);
            helper.Target = null;
            helper.CarriedCoins++;
            helper.Moving = false;
            SetAnimationSpeed(helper, 0f);
        }
    }

    private static void UpdateIdlePatrols(Kingdom kingdom)
    {
        if (!NetworkBigBoss.HasWorldAuth || kingdom == null) return;
        GetWallInterior(kingdom, out float wallLeft, out float wallRight);
        float step = ASSISTANT_PATROL_SPEED * Time.deltaTime;

        for (int i = 0; i < Assistants.Length; i++)
        {
            AssistantState helper = Assistants[i];
            if (i == _collectorIndex || helper.Target != null || helper.Actor == null
                || !helper.Actor.activeInHierarchy) continue;

            float center = Mathf.Clamp(kingdom.campfirePosition + HomeOffsets[i],
                wallLeft, wallRight);
            float laneLeft = Mathf.Clamp(center - PATROL_HALF_WIDTH, wallLeft, wallRight);
            float laneRight = Mathf.Clamp(center + PATROL_HALF_WIDTH, wallLeft, wallRight);

            Vector3 position = helper.Actor.transform.position;
            float clampedX = Mathf.Clamp(position.x, wallLeft, wallRight);
            if (!Mathf.Approximately(position.x, clampedX))
            {
                position.x = clampedX;
                helper.Actor.transform.position = position;
                SendFullPosition(helper);
            }

            if (laneRight - laneLeft <= 0.02f)
            {
                helper.Moving = false;
                SetAnimationSpeed(helper, 0f);
                continue;
            }

            if (Time.time < helper.PatrolResumeAt)
            {
                helper.Moving = false;
                SetAnimationSpeed(helper, 0f);
                continue;
            }

            float targetX = helper.PatrolRight ? laneRight : laneLeft;
            if (Mathf.Abs(position.x - targetX) <= 0.02f)
            {
                helper.PatrolRight = !helper.PatrolRight;
                helper.PatrolResumeAt = Time.time + PatrolPauseSeconds(i);
                helper.Moving = false;
                SetAnimationSpeed(helper, 0f);
                continue;
            }

            FaceTowards(helper.Actor.transform, targetX);
            position.x = Mathf.Clamp(
                Mathf.MoveTowards(position.x, targetX, step), wallLeft, wallRight);
            helper.Actor.transform.position = position;
            helper.Moving = true;
            SetAnimationSpeed(helper, ASSISTANT_PATROL_SPEED);
        }
    }

    private static float PatrolPauseSeconds(int index)
    {
        return 2f + Mathf.Clamp(index, 0, Assistants.Length - 1);
    }

    private static void GetWallInterior(Kingdom kingdom, out float left, out float right)
    {
        left = kingdom.GetBorderSide(Side.Left) + WALL_MARGIN;
        right = kingdom.GetBorderSide(Side.Right) - WALL_MARGIN;
        if (float.IsNaN(left) || float.IsInfinity(left)
            || float.IsNaN(right) || float.IsInfinity(right) || left > right)
        {
            left = kingdom.campfirePosition;
            right = kingdom.campfirePosition;
        }
    }

    private static bool CanCommitPickup(AssistantState helper, DroppableCurrency coin)
    {
        if (!NetworkBigBoss.HasWorldAuth || helper == null || helper.Actor == null
            || coin == null || coin.gameObject == null || !coin.isActiveAndEnabled
            || coin.pickedUp || coin.IsFake()) return false;
        if (coin.droppedBy != DropType.Player || coin.CurrencyType != CurrencyType.Coins)
            return false;

        Managers managers = Managers.Inst;
        Kingdom kingdom = managers != null ? managers.kingdom : null;
        if (!PatchEconomy_Banker.TryGetMainBankerDomain(
                kingdom, out float domainLeft, out float domainRight)
            || PatchEconomy_Banker.IsInMainBankerDomain(
                coin.transform.position.x, domainLeft, domainRight)) return false;

        int id = coin.gameObject.GetInstanceID();
        if (!Claims.TryGetValue(id, out int owner) || owner != helper.Index
            || coin.friendlyClaimer != helper.Actor) return false;

        if (NetworkBigBoss.IsOnline
            && (!NetworkBigBoss.HasClientCaughtUp || coin.parentHeaderRef == null
                || helper.PositionSync == null || helper.PositionSync.parentHeaderRef == null))
            return false;
        return true;
    }

    private static bool IsValidOwnedTarget(AssistantState helper)
    {
        DroppableCurrency coin = helper.Target;
        if (coin == null || coin.gameObject == null || !coin.isActiveAndEnabled) return false;
        int id = coin.gameObject.GetInstanceID();
        if (!Claims.TryGetValue(id, out int owner) || owner != helper.Index) return false;
        return coin.friendlyClaimer == helper.Actor;
    }

    private static void ReleaseTarget(AssistantState helper)
    {
        DroppableCurrency coin = helper.Target;
        helper.Target = null;
        helper.Moving = false;
        SetAnimationSpeed(helper, 0f);
        helper.PatrolResumeAt = Time.time + PatrolPauseSeconds(helper.Index);
        if (coin == null || coin.gameObject == null) return;

        int id = coin.gameObject.GetInstanceID();
        Claims.Remove(id);
        if (coin.isActiveAndEnabled
            && (coin.friendlyClaimer == helper.Actor || coin.friendlyClaimer == null))
        {
            coin.ClearFriendlyClaimIfClaimer(helper.Actor);
            coin.pickUpPolicy = helper.OriginalPolicy;
            coin.SendPolicyRPC();
        }
    }

    private static void TeleportHomeAndDeposit(AssistantState helper)
    {
        if (helper.Actor == null) return;
        if (helper.Target != null) ReleaseTarget(helper);

        Vector3 home = GetHomePosition(helper.Index);
        if (Vector3.Distance(helper.Actor.transform.position, home) > 0.05f)
        {
            helper.Actor.transform.position = home;
            SendFullPosition(helper);
        }
        helper.Moving = false;
        SetAnimationSpeed(helper, 0f);
        helper.PatrolResumeAt = Time.time + PatrolPauseSeconds(helper.Index);

        // Economic ownership was committed at successful pickup. Home is visual/capacity
        // delivery only; the fallback below applies solely to a previously failed commit.
        if (helper.UncreditedCoins > 0)
        {
            int accepted = PatchEconomy_Banker.DepositFromAssistant(
                _mainBanker, helper.UncreditedCoins);
            if (accepted > 0) helper.UncreditedCoins -= accepted;
        }
        if (helper.UncreditedCoins == 0) helper.CarriedCoins = 0;
    }

    private static int GetAssistantCapacity()
    {
        Wallet wallet = _mainBanker != null ? _mainBanker._wallet : null;
        return wallet != null ? Math.Max(1, wallet.TotalCapacity) : 1;
    }

    private static Vector3 GetHomePosition(int index)
    {
        Vector3 home = _mainBanker != null ? _mainBanker.transform.position : Vector3.zero;
        Managers managers = Managers.Inst;
        if (managers != null && managers.kingdom != null)
        {
            Kingdom kingdom = managers.kingdom;
            GetWallInterior(kingdom, out float left, out float right);
            home.x = Mathf.Clamp(
                kingdom.campfirePosition + HomeOffsets[index], left, right);
        }
        return home;
    }

    private static void FaceTowards(Transform actor, float targetX)
    {
        Vector3 scale = actor.localScale;
        float sign = targetX >= actor.position.x ? 1f : -1f;
        scale.x = Mathf.Max(0.01f, Mathf.Abs(scale.x)) * sign;
        actor.localScale = scale;
    }

    private static void SetAnimationSpeed(AssistantState helper, float speed)
    {
        if (helper.Animator != null) helper.Animator.SetFloat(SpeedParameter, speed);
    }

    private static void SendFullPosition(AssistantState helper)
    {
        if (NetworkBigBoss.IsOnline && helper.PositionSync != null)
            helper.PositionSync.SendFullPos(false);
    }

    private static void ResetAll(bool releaseClaims, bool destroyActors, bool syncDespawn = false)
    {
        for (int i = 0; i < Assistants.Length; i++)
        {
            AssistantState helper = Assistants[i];
            if (releaseClaims && helper.Target != null) ReleaseTarget(helper);
            else helper.Target = null;

            if (destroyActors && helper.Actor != null)
            {
                if (syncDespawn && Pool.GetPoolByInstance(helper.Actor) != null)
                    Pool.Despawn(helper.Actor, true);
                else
                    UnityEngine.Object.Destroy(helper.Actor);
            }

            helper.Actor = null;
            helper.Animator = null;
            helper.PositionSync = null;
            helper.CarriedCoins = 0;
            helper.UncreditedCoins = 0;
            helper.Moving = false;
            helper.PatrolRight = (i & 1) == 0;
            helper.PatrolResumeAt = Time.time + PatrolPauseSeconds(i);
        }
        _collectorIndex = -1;
        _nextCollectorIndex = 0;
        Claims.Clear();
        Observed.Clear();
        SeenThisScan.Clear();
        MatureBuffer.Clear();
        RemovalBuffer.Clear();
        _nextScanAt = Time.time + SCAN_INTERVAL;
        _nextDiagnosticsAt = Time.time;
    }

    private static void FlushUncreditedCoins()
    {
        if (!NetworkBigBoss.HasWorldAuth || _mainBanker == null) return;
        for (int i = 0; i < Assistants.Length; i++)
        {
            AssistantState helper = Assistants[i];
            if (helper.UncreditedCoins <= 0) continue;
            int accepted = PatchEconomy_Banker.DepositFromAssistant(
                _mainBanker, helper.UncreditedCoins);
            if (accepted > 0) helper.UncreditedCoins -= accepted;
        }
    }
}

[HarmonyPatch(typeof(PoolManager), nameof(PoolManager.Init))]
public static class PoolManager_BankAssistants_Init_Patch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    public static void Postfix(PoolManager __instance)
    {
        PatchEconomy_BankAssistants.HandlePoolManagerRebuilt(__instance);
    }
}

/// <summary>Pool lifecycle bridge for clearing client-side visual interpolation caches.</summary>
public class BankAssistantVisualLifecycle : MonoBehaviour
{
    public BankAssistantVisualLifecycle(IntPtr ptr) : base(ptr) { }

    private void OnDisable()
    {
        if (gameObject != null)
            PositionSync_BankAssistantAnimation_Patch.Forget(gameObject.GetInstanceID());
    }
}

/// <summary>
/// PositionSync does not drive animation when a lightweight assistant has no Mover.
/// This local visual-only postfix derives Speed from received position deltas on clients;
/// authority animation is driven directly by the coordinator.
/// </summary>
[HarmonyPatch(typeof(PositionSync), nameof(PositionSync.Update))]
public static class PositionSync_BankAssistantAnimation_Patch
{
    private static readonly Dictionary<int, Vector3> LastPositions = new();
    private static readonly Dictionary<int, float> LastTimes = new();
    private static readonly int SpeedParameter = Animator.StringToHash("Speed");

    public static void Forget(int instanceId)
    {
        LastPositions.Remove(instanceId);
        LastTimes.Remove(instanceId);
    }

    [HarmonyPostfix]
    public static void Postfix(PositionSync __instance)
    {
        if (!ModConfig.Enabled.Value || NetworkBigBoss.HasWorldAuth
            || !PatchEconomy_BankAssistants.IsAssistantPositionSync(__instance)) return;

        GameObject actor = __instance.gameObject;
        int id = actor.GetInstanceID();
        Vector3 position = actor.transform.position;
        float now = Time.unscaledTime;
        float speed = 0f;
        if (LastPositions.TryGetValue(id, out Vector3 lastPosition)
            && LastTimes.TryGetValue(id, out float lastTime))
        {
            float elapsed = now - lastTime;
            if (elapsed > 0.0001f) speed = Mathf.Abs(position.x - lastPosition.x) / elapsed;
        }
        LastPositions[id] = position;
        LastTimes[id] = now;

        Animator animator = actor.GetComponent<Animator>();
        if (animator != null) animator.SetFloat(SpeedParameter, speed);
    }
}
