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
///
/// World boundary: every entry point (creation, fixed-pool registration, Update,
/// claims, pickups and restock reservations) is gated by GreekBankScope — the current
/// world only. Leaving Greece/loading/disabled freezes procurement, releases local
/// reservations and drops this mod's actor/claim references; native rollback is applied
/// only to objects still inside the current gameLayer, so nothing from an unloaded
/// scene can receive an RPC or a write while another world is active.
/// </summary>
public static class PatchEconomy_BankAssistants
{
    // 0.6s keeps the same deterministic round-robin semantics while halving
    // whole-island registrar scans on crowded islands.
    internal const float SCAN_INTERVAL = 0.6f;
    internal const float COIN_MATURITY_SECONDS = 3f;
    // 每趟收集目标（2026-09-15 用户反馈：连续投币时助手每趟只收几枚就回家。真实原因是
    // 成熟快照一断流就 TeleportHome，与容量无关——容量下限是 100）。容量 helper 语义与
    // 下限不动，TripTarget 只是“这一趟收够了”的阈值：20 恒低于容量下限，完成检查
    // （扫描收工 / 链式补位 / 选择收集者）全部以它为准。
    internal const int TRIP_TARGET = 20;
    // 断流等待：活跃且本趟已收 >0 但未满趟、又找不到下一枚合法成熟币时，原地静止等一个
    // 有界时长 = 首个成熟币观察期 + 2 个扫描周期（3 + 2×0.6 = 4.2s）：断流当帧新落的币
    // 3s 成熟，再给一个扫描周期找到它；到点仍无币才回家收工。deadline 只建立一次、不续期。
    internal const float WAIT_GAP_SECONDS = COIN_MATURITY_SECONDS + 2f * SCAN_INTERVAL;
    // 农田币独立成熟时长（2026-08-30 需求）：农田币在玩家脚边成串弹出
    // （Farmland.DropCoins 每 0.1s 一枚），且原生 pickUpPolicy=Nobody 只有玩家能捡。
    // 3s 会在玩家弯腰捡币半途就吸走；取 4×3s=12s，约一个完整收获-捡币周期，
    // 给玩家留出自己捡的明显窗口。Wildlife 类币不会自动消失，晚吸无经济损失。
    internal const float FARM_COIN_MATURITY_SECONDS = 3f;
    internal const float SWEEP_RADIUS = 0.35f;
    internal const float ACTIVE_SCALING_STEP = 8f;
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
    // Preserve source-derived Greek sizes for the first two styles without baking
    // an already-scaled live banker into the cross-world cached pool templates.
    private static readonly float[] GreekVisualScaleY = new float[4];
    private static readonly Pool[] Pools = new Pool[4];
    private static Il2CppArrayBase<Banker> _allBankerPrefabs;
    private static bool _registeredCoordinatorType;
    private static bool _loggedControllerSet;
    private static float _nextControllerResolveAt;
    private static string _lastControllerFailure;

    public static void EnsureForMainBanker(Banker banker)
    {
        // 唯一世界判定：只有当前世界明确为希腊时才创建/绑定助手与固定池。
        if (!GreekBankScope.IsCurrentBanker(banker)) return;

        EnsureInjectedTypes();

        BankAssistantCoordinator existing = banker.GetComponent<BankAssistantCoordinator>();
        if (existing == null)
            existing = banker.gameObject.AddComponent<BankAssistantCoordinator>();

        BankAssistantCoordinator.AttachTo(banker);
        var managers = Managers.Inst;
        if (managers != null && managers.pools != null)
            EnsurePools(banker, managers.pools);
    }

    /// <summary>
    /// 供 Banker.Update 低频重试：当前希腊本体是否已经绑定协调器。Awake 可能早于
    /// 当前世界/层就绪，那时 EnsureForMainBanker 会被身份闸门挡下。
    /// 纯静态判定，不做 GetComponent（协调器类型注入前不能查组件）。
    /// </summary>
    internal static bool IsBound(Banker banker)
    {
        BankAssistantCoordinator coordinator = BankAssistantCoordinator.Instance;
        Banker main = BankAssistantCoordinator.MainBanker;
        return banker != null && coordinator != null && main != null
            && banker.Pointer == main.Pointer;
    }

    public static void HandlePoolManagerRebuilt(PoolManager poolManager)
    {
        // PoolManager 重建后所有缓存句柄都失效：无论当前世界，先无条件失效，
        // 否则旧世界的 Pool 句柄会被带回新世界继续使用。
        ClearPoolHandles();
        if (!GreekBankScope.IsActive || poolManager == null) return;

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
        // 固定 synced pool 只属于希腊世界：其他世界不得把本 mod 的池塞进原生池表。
        if (!GreekBankScope.IsActive || banker == null || poolManager == null) return;

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
            GreekVisualScaleY[i] = i == 2 ? 1.25f : i == 3 ? 1.2f : banker.transform.localScale.y;
            prefab.transform.localScale = GreekScaleScope.NativeScale(banker.transform);

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

    internal static void ApplyAssistantScale(GameObject actor)
    {
        if (actor == null) return;
        string actorName = actor.name;
        for (int i = 0; i < Prefabs.Length; i++)
        {
            if (!actorName.StartsWith(ASSISTANT_PREFIX + i + "_", StringComparison.Ordinal)) continue;
            // Templates stay neutral. OnEnable runs locally on either peer, with
            // no network operation and no world-authority requirement.
            if (Prefabs[i] != null && actor.Pointer != Prefabs[i].Pointer)
                GreekScaleScope.ApplyY(actor.transform, GreekVisualScaleY[i]);
            return;
        }
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
    private const float FARM_COIN_MATURITY_SECONDS = PatchEconomy_BankAssistants.FARM_COIN_MATURITY_SECONDS;
    private const int TRIP_TARGET = PatchEconomy_BankAssistants.TRIP_TARGET;
    private const float WAIT_GAP_SECONDS = PatchEconomy_BankAssistants.WAIT_GAP_SECONDS;
    private const float WORLD_SCAN_RANGE = PatchEconomy_BankAssistants.WORLD_SCAN_RANGE;
    private const float TELEPORT_APPROACH_DISTANCE = PatchEconomy_BankAssistants.TELEPORT_APPROACH_DISTANCE;
    private const float PICKUP_DISTANCE = PatchEconomy_BankAssistants.PICKUP_DISTANCE;
    private const float SWEEP_RADIUS = PatchEconomy_BankAssistants.SWEEP_RADIUS;
    private const float ACTIVE_SCALING_STEP = PatchEconomy_BankAssistants.ACTIVE_SCALING_STEP;
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
        // 该币适用的成熟等待时长：玩家投掷币 COIN_MATURITY_SECONDS，农田币
        // FARM_COIN_MATURITY_SECONDS（首次观测时按来源定型，见 IsFarmOriginCoin）。
        public float MaturitySeconds;
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
        public bool RestockReserved;
        // 断流等待 deadline（Time.time，>0 表示正在等下一枚成熟币）。首次断流建立，后续
        // 扫描不续期；新目标/新成功拾取/回家/借用/换世界/整表重置一律清零，防止残留。
        public float WaitDeadline;

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
    private static readonly HashSet<int> LiveClaimIds = new();
    private static readonly HashSet<int> SeenThisScan = new();
    private static readonly List<int> RemovalBuffer = new();
    private static readonly List<ObservedCoin> MatureBuffer = new();
    // 农田币来源标记（由本文件 Droppable_FarmCoinOrigin_Mark_Patch 写入）：
    // Droppable.Drop 只按 dropper tag 分类（Player/Archer/Worker/Farmer 之外一律
    // Wildlife），农田币与狩猎奖励/宝箱/灌木/树/罐子/骡子/银行家吐币/钓鱼竿共用
    // DropType.Wildlife，没有独立枚举值。只能用"dropper 属于 Farmland"这一精确
    // 来源标记圈定农田，不能放开整个 Wildlife 门槛（否则会抢弓箭手狩猎收入、
    // 把银行家取款吐币又吸回银行）。
    private static readonly HashSet<int> FarmOriginCoinIds = new();
    private static readonly HashSet<string> LoggedDiagnosticStates = new();
    private static readonly Il2CppReferenceArray<DroppableCurrency> ScanBuffer =
        new Il2CppReferenceArray<DroppableCurrency>(SCAN_BUFFER_SIZE);
    private static float _nextScanAt;
    private static float _nextDiagnosticsAt;
    private static bool _hadAuthority;
    private static bool _loggedReady;
    private static bool _loggedFirstAssignment;
    private static bool _loggedFirstSubmission;
    private static readonly bool[] ActiveCollector = new bool[Assistants.Length];
    // AssignNextTarget 单次尝试内已试过的候选币 id（认领失败退让次近候选用）。
    private static readonly HashSet<int> TriedThisChain = new();
    private static int _nextCollectorIndex;
    private static int _nextRestockAssistant;
    // 顺吸认领会同时占据多枚币，各自原始拾取策略必须按币记录，不能用单槽
    // OriginalPolicy 覆盖（否则回滚会把错误策略还原到别的币上）。
    private static readonly Dictionary<int, PickUpPolicy> SweepPolicies = new();
    private static readonly Dictionary<int, DroppableCurrency> SweepCoins = new();
    private static bool _cleanupPending, _cleanupDestroyActors, _cleanupSyncDespawn;
    private static int _lastLoggedActiveCount = -1;
    private static float _nextActiveCountLogAt;
    private static readonly int SpeedParameter = Animator.StringToHash("Speed");

    public BankAssistantCoordinator(IntPtr ptr) : base(ptr) { }
    public static bool HasMainBanker => _instance != null && GreekBankScope.IsCurrentBanker(_mainBanker);

    /// <summary>当前绑定的协调器与主银行家（同一程序集读取；避免外部 GetComponent）。</summary>
    internal static BankAssistantCoordinator Instance => _instance;
    internal static Banker MainBanker => _mainBanker;

    // ---- 农田币来源标记（Droppable_FarmCoinOrigin_*_Patch 调用）----
    internal static void MarkFarmCoin(int instanceId)
    {
        FarmOriginCoinIds.Add(instanceId);
    }

    internal static void ClearFarmCoin(int instanceId)
    {
        FarmOriginCoinIds.Remove(instanceId);
    }

    private static bool IsFarmOriginCoin(DroppableCurrency coin)
    {
        return coin != null && coin.gameObject != null
            && FarmOriginCoinIds.Contains(coin.gameObject.GetInstanceID());
    }

    /// <summary>
    /// 面板只读直查主银行家存款（2026-08-30 需求2，ModPanel.OnGUI 每帧调用）。
    /// 零分配、直字段读（先例 PatchEconomy_Banker L261/293）；当前不是希腊世界、
    /// _mainBanker 未就绪、或引用已属旧层/旧场景时一律返回 -1——绝不在 OnGUI 里触发
    /// 查找/解析，银行家解析交给现有 AttachTo 链。其他世界由 HUD 侧隐藏自定义银行栏。
    /// </summary>
    internal static int GetStashedCoinsForPanel()
    {
        try
        {
            if (!GreekBankScope.IsActive) return -1;
            Banker banker = _mainBanker;
            if (!GreekBankScope.IsCurrentBanker(banker)) return -1;
            return Math.Max(0, banker._stashedCoins);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// 共享窄身份判定：传入的 banker 必须就是当前协调器绑定的主银行家本体。
    /// 原生 kingdom.banker 允许为 null（存档载入的 fixedID903 银行家不会被
    /// Castle 重新赋引用，canonical ledger 的 IsCanonicalAuthorityBanker 同样容忍）；
    /// null 只在精确控制器/已知主银行家/当前世界证据齐全时放行。非 null 且指向
    /// 他人、协调器缺失/换绑、银行家死亡、旧层、异场景、菜单/暂停/失权一律拒绝。
    /// 不调用 RestockContextReady，避免递归。
    /// </summary>
    internal static bool IsCurrentRestockBanker(Banker banker)
    {
        try
        {
            // 希腊 scope + world auth + 本体身份/当前 gameLayer/scene 一次验证；
            // 其他世界与其他世界的银行家一律不是采购主体。
            if (!GreekBankScope.IsAuthorityBanker(banker) || Time.timeScale <= 0f) return false;
            BankAssistantCoordinator coordinator = _instance;
            Banker main = _mainBanker;
            if (coordinator == null || main == null) return false;
            if (banker.Pointer != main.Pointer) return false;
            GameObject coordinatorGO = coordinator.gameObject;
            GameObject bankerGO = banker.gameObject;
            if (coordinatorGO == null || bankerGO == null
                || coordinatorGO.Pointer != bankerGO.Pointer) return false;
            if (!bankerGO.activeInHierarchy) return false;
            var m = Managers.Inst;
            if (m == null || m.world == null || m.world.gameLayer == null
                || m.kingdom == null || m.game == null
                || m.game.state != Game.State.Playing) return false;
            Transform layer = m.world.gameLayer;
            if (layer == null || layer.gameObject == null || !layer.gameObject.activeInHierarchy
                || !bankerGO.transform.IsChildOf(layer)
                || bankerGO.scene.handle != layer.gameObject.scene.handle) return false;
            Banker nativeKingdomBanker = m.kingdom.banker;
            if (nativeKingdomBanker != null
                && nativeKingdomBanker.Pointer != banker.Pointer) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool RestockContextReady(out Transform layer)
    {
        layer = null;
        if (!IsCurrentRestockBanker(_mainBanker)) return false;
        // 谓词已验证 Managers/world/gameLayer 非空且 Playing。
        layer = Managers.Inst.world.gameLayer;
        return layer != null && layer.gameObject != null && layer.gameObject.activeInHierarchy;
    }

    internal static bool RestockReservationValid(int index, GameObject actor)
    {
        if (index < 0 || index >= Assistants.Length || !RestockContextReady(out var layer)) return false;
        var helper = Assistants[index];
        return helper.RestockReserved && actor != null && helper.Actor != null
            && actor.Pointer == helper.Actor.Pointer && actor.activeInHierarchy
            && actor.scene.handle == layer.gameObject.scene.handle && actor.transform.IsChildOf(layer)
            && helper.Target == null && !ActiveCollector[index]
            && (!NetworkBigBoss.IsOnline || (NetworkBigBoss.HasClientCaughtUp
                && helper.PositionSync != null && helper.PositionSync.parentHeaderRef != null));
    }

    internal static bool TryReserveForRestock(out int index, out GameObject actor)
    {
        index = -1; actor = null;
        if (!RestockContextReady(out var layer)) return false;
        int reserved = 0;
        foreach (var helper in Assistants) if (helper.RestockReserved) reserved++;
        if (reserved >= 2) return false;
        for (int pass = 0; pass < 2; pass++)
        for (int offset = 0; offset < Assistants.Length; offset++)
        {
            int i = (_nextRestockAssistant + offset) % Assistants.Length;
            var helper = Assistants[i];
            var candidate = helper.Actor;
            if (helper.RestockReserved || candidate == null || !candidate.activeInHierarchy
                || candidate.scene.handle != layer.gameObject.scene.handle || !candidate.transform.IsChildOf(layer)
                || (NetworkBigBoss.IsOnline && (!NetworkBigBoss.HasClientCaughtUp
                    || helper.PositionSync == null || helper.PositionSync.parentHeaderRef == null))) continue;
            if (pass == 0 && (ActiveCollector[i] || helper.Target != null)) continue;
            if (helper.Target != null) ReleaseTarget(helper);
            if (helper.CarriedCoins > 0 || helper.UncreditedCoins > 0) TeleportHomeAndDeposit(helper);
            if (helper.UncreditedCoins != 0 || helper.CarriedCoins != 0 || helper.Target != null) continue;
            if (!RestockContextReady(out var currentLayer) || currentLayer.Pointer != layer.Pointer
                || helper.Actor == null || helper.Actor.Pointer != candidate.Pointer || !candidate.activeInHierarchy) return false;
            ActiveCollector[i] = false;
            helper.Moving = false;
            SetAnimationSpeed(helper, 0f);
            helper.WaitDeadline = 0f;
            helper.RestockReserved = true;
            index = i; actor = candidate; _nextRestockAssistant = (i + 1) % Assistants.Length;
            return true;
        }
        return false;
    }

    internal static bool PlaceRestockAssistant(int index, GameObject actor, Vector3 position, float faceX)
    {
        if (!RestockReservationValid(index, actor) || !float.IsFinite(position.x)
            || !float.IsFinite(position.y) || !float.IsFinite(position.z) || !float.IsFinite(faceX)) return false;
        var helper = Assistants[index];
        actor.transform.position = position;
        FaceTowards(actor.transform, faceX);
        SetAnimationSpeed(helper, 0f);
        SendFullPosition(helper);
        return RestockReservationValid(index, actor);
    }

    // Only the exact leased actor may move here; normal collection/patrol skips this lease.
    // X-only motion keeps the actor's ground Y/Z, including shops on raised scenery.
    internal static bool MoveRestockAssistant(int index, GameObject actor, float targetX,
        float speed, float deltaTime, out bool arrived)
    {
        arrived = false;
        if (!RestockReservationValid(index, actor) || !float.IsFinite(targetX)
            || !float.IsFinite(speed) || speed <= 0f || !float.IsFinite(deltaTime)
            || deltaTime < 0f || !float.IsFinite(speed * deltaTime)) return false;
        var helper = Assistants[index];
        Transform actorT = actor.transform;
        Vector3 position = actorT.position;
        if (!float.IsFinite(position.x) || !float.IsFinite(position.y)
            || !float.IsFinite(position.z)) return false;
        if (Mathf.Abs(position.x - targetX) > 0.02f) FaceTowards(actorT, targetX);
        position.x = Mathf.MoveTowards(position.x, targetX, speed * deltaTime);
        actorT.position = position;
        arrived = Mathf.Abs(actorT.position.x - targetX) <= 0.02f;
        helper.Moving = !arrived;
        SetAnimationSpeed(helper, arrived ? 0f : speed);
        SendFullPosition(helper);
        return RestockReservationValid(index, actor);
    }

    internal static void ReleaseRestockAssistant(int index, GameObject actor, bool returnHome)
    {
        if (index < 0 || index >= Assistants.Length || actor == null) return;
        var helper = Assistants[index];
        if (helper.Actor == null || helper.Actor.Pointer != actor.Pointer || !helper.RestockReserved) return;
        bool canReturn = false;
        try { canReturn = returnHome && RestockReservationValid(index, actor); }
        catch { } // Invalid native context must still relinquish the local lease.
        finally { helper.RestockReserved = false; helper.Moving = false; helper.WaitDeadline = 0f; }
        if (canReturn) TeleportHomeAndDeposit(helper);
    }

    public static void AttachTo(Banker banker)
    {
        // 直接入口也走同一个世界/身份闸门：换世界/加载中不在这里绑定，
        // 由 Banker.Update 的低频重试在世界就绪后再绑。
        if (!GreekBankScope.IsCurrentBanker(banker)) return;

        TickPendingCleanup();
        if (_cleanupPending) return;
        BankAssistantCoordinator component = banker.GetComponent<BankAssistantCoordinator>();
        if (component == null) return;

        // 换绑（新银行家/新层）：旧记账整表清空，原生回滚只作用于仍属当前 gameLayer
        // 的对象；旧场景残留只丢引用，绝不向新世界发旧 actor 的 RPC。
        if (_instance != null && _instance != component)
            ResetAll(releaseClaims: NetworkBigBoss.HasWorldAuth, destroyActors: false);

        if (_cleanupPending) return;
        _instance = component;
        _mainBanker = banker;
        _nextScanAt = Time.time + SCAN_INTERVAL;
        _nextDiagnosticsAt = Time.time;
    }

    public static void HandlePoolRebuild(PoolManager poolManager)
    {
        // 直接入口的 scope 闸门：非希腊世界不 flush、不回收、不注册（池句柄已由
        // PatchEconomy_BankAssistants.HandlePoolManagerRebuilt 无条件失效）。
        if (!GreekBankScope.IsActive) return;

        PatchEconomy_BankAssistants.ClearPoolHandles();
        if (_instance == null || _mainBanker == null) return;

        FlushUncreditedCoins();
        ResetAll(releaseClaims: NetworkBigBoss.HasWorldAuth, destroyActors: true, syncDespawn: false);
        PatchEconomy_BankAssistants.EnsurePools(_mainBanker, poolManager);
    }

    private void Update()
    {
        if (_instance != this || _mainBanker == null) return;
        TickPendingCleanup();
        if (_cleanupPending) return;

        // 唯一世界判定。Unknown（加载中/读取异常）冻结但保留一切 receipt：加载图可能
        // 还不完整，此时回收会丢认领、把旧对象当新对象。已知非希腊（其他世界/关闭）
        // 才回收本 mod 的本地状态，且不再像旧实现那样直接 return 让助手/认领/采购单残留。
        GreekBankScope.Scope scope = GreekBankScope.Current();
        if (scope == GreekBankScope.Scope.Unknown)
        {
            ResetAll(true, false);
            return;
        }
        if (scope != GreekBankScope.Scope.Active)
        {
            SuspendForScopeExit();
            return;
        }

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

        // 经济动作前再验证“当前希腊权威本体”（换岛加载中、本体身份失效时本帧不动，
        // 绝不对旧层对象发 RPC、绝不向旧银行家入账）。
        if (!GreekBankScope.IsAuthorityBanker(_mainBanker)) return;
        if (managers.game == null || managers.game.state != Game.State.Playing) return;
        if (poolManager == null || managers.world == null || managers.world.gameLayer == null
            || managers.kingdom == null) return;

        // 换岛/场景卸载后旧 actor 引用必须在本帧工作前丢弃，避免向新世界发送旧对象 RPC。
        DropStaleWorldState(managers.world.gameLayer);
        if (_cleanupPending) return;
        EnsureFourActors(managers.world.gameLayer);
        PatchEconomy_AutoRestock.Tick(_mainBanker, managers, Time.time >= _nextScanAt);

        if (Time.time >= _nextScanAt)
        {
            _nextScanAt = Time.time + SCAN_INTERVAL;
            ScanAndDispatch(managers);
        }
        if (_cleanupPending) return;
        UpdateMovingAssistants();
        UpdateIdlePatrols(managers.kingdom);
    }

    /// <summary>
    /// 已知离开希腊世界 / 关闭总开关：冻结并清理本 mod 的本地状态。
    /// - 停采购：PatchEconomy_AutoRestock.Reset(false) 只释放本地 reservation，
    ///   不回家瞬移、不向旧 actor 发位置 RPC；
    /// - 释放本地 reservation、清本 mod 助手与 claim 记账；
    /// - 原生回滚（恢复拾取策略、清友好认领、池回收）只在“仍持有 world auth 且对象仍属
    ///   当前 world/gameLayer”时执行：客机不能替主机发认领/回收 RPC；旧场景对象更不碰，
    ///   只丢引用，绝不把旧 actor 的状态带进新世界；
    /// - 不向任何银行家入账：入账口本身已按希腊 scope + 权威身份闸门。
    /// CarriedCoins 只是“拾取时已入账”的视觉/容量计数，不当作待存真实币；
    /// UncreditedCoins 保留原语义（失败补记），此处不凭空增加也不当作已入账。
    /// </summary>
    private static void SuspendForScopeExit()
    {
        ResetAll(releaseClaims: true, destroyActors: true, syncDespawn: true);
        _hadAuthority = false;
    }

    private static bool CleanupContextReady()
    {
        if (GreekBankScope.Current() == GreekBankScope.Scope.Unknown) return false;
        Managers m = Managers.Inst;
        return m != null && m.world != null && m.world.gameLayer != null
            && m.world.gameLayer.gameObject != null && m.world.gameLayer.gameObject.activeInHierarchy
            && m.game != null && (m.game.state == Game.State.Playing
                || m.game.state == Game.State.NetworkClientPlaying || m.game.state == Game.State.Menu);
    }

    // Also called by the existing panel update, so disabled/destroyed coordinators
    // cannot strand an owned coin policy. Unknown/loading/authority loss defer.
    internal static void TickPendingCleanup()
    {
        if (!_cleanupPending) return;
        bool foreign = GreekBankScope.Current() == GreekBankScope.Scope.Inactive;
        ResetAll(true, _cleanupDestroyActors || foreign, _cleanupSyncDespawn || foreign);
    }

    private static bool TryRestoreClaim(DroppableCurrency coin, GameObject actor, PickUpPolicy original)
    {
        try
        {
            if (coin == null || coin.gameObject == null || !coin.isActiveAndEnabled) return true;
            if (!CleanupContextReady()) return false;
            // Read directly inside this try: a failed native read is not proof that
            // the coin left the world. Keep its receipt and retry on read failure.
            Transform layer = Managers.Inst.world.gameLayer;
            if (!coin.transform.IsChildOf(layer)
                || coin.gameObject.scene.handle != layer.gameObject.scene.handle) return true;
            // The actor may have left the layer. We restore only the current coin;
            // ClearFriendlyClaimIfClaimer and SendPolicyRPC target the coin, never
            // the old actor's PositionSync or pool.
            if (coin.friendlyClaimer != null && coin.friendlyClaimer != actor) return true;
            if (coin.pickUpPolicy != PickUpPolicy.OnlyClaimer && coin.pickUpPolicy != original) return true;
            if (!NetworkBigBoss.HasWorldAuth || (NetworkBigBoss.IsOnline
                && (!NetworkBigBoss.HasClientCaughtUp || coin.parentHeaderRef == null))) return false;
            coin.ClearFriendlyClaimIfClaimer(actor);
            coin.pickUpPolicy = original;
            coin.SendPolicyRPC();
            return true;
        }
        catch { return false; } // retain receipt, including a failed final RPC
    }

    /// <summary>
    /// 归还本补丁仍持有的顺吸认领：按币恢复原始拾取策略并清友好认领。只处理“仍在当前
    /// world/gameLayer”的活币；旧场景/已销毁的币只丢本地记账，绝不触碰原生对象。
    /// </summary>
    private static bool RollbackOwnedClaimPolicies()
    {
        bool complete = true;
        RemovalBuffer.Clear();
        RemovalBuffer.AddRange(SweepPolicies.Keys);
        foreach (int id in RemovalBuffer)
        {
            if (!SweepPolicies.TryGetValue(id, out PickUpPolicy original)) continue;
            SweepCoins.TryGetValue(id, out DroppableCurrency coin);
            GameObject actor = null;
            if (Claims.TryGetValue(id, out int owner) && owner >= 0 && owner < Assistants.Length)
                actor = Assistants[owner].Actor;
            if (!TryRestoreClaim(coin, actor, original)) { complete = false; continue; }
            SweepPolicies.Remove(id);
            SweepCoins.Remove(id);
            Claims.Remove(id);
        }
        RemovalBuffer.Clear();
        return complete;
    }

    /// <summary>
    /// 换岛/重建后旧场景对象只做本地清理：清掉本 mod 的认领记账与引用，绝不触碰
    /// 可能已被卸载的原生对象，也绝不向新世界发送它们的 RPC。
    /// </summary>
    private static void DropStaleWorldState(Transform gameLayer)
    {
        foreach (var helper in Assistants)
        {
            if ((helper.Actor != null && (!helper.Actor.activeInHierarchy
                    || !GreekBankScope.IsInCurrentLayer(helper.Actor)))
                || (helper.Target != null && !GreekBankScope.IsInCurrentLayer(helper.Target)))
            {
                ResetAll(true, false);
                return;
            }
        }
    }

    /// <summary>
    /// 只清本地认领记账，不触碰目标对象：用于对象已属旧场景（可能已卸载）的场合。
    /// </summary>
    private static void ForgetTargetLocally(AssistantState helper)
    {
        DroppableCurrency coin = helper.Target;
        helper.Target = null;
        helper.Moving = false;
        if (coin == null) return;
        try
        {
            if (coin.gameObject == null) return;
            int id = coin.gameObject.GetInstanceID();
            Claims.Remove(id);
            SweepPolicies.Remove(id);
            Observed.Remove(id);
            FarmOriginCoinIds.Remove(id);
        }
        catch
        {
            // 已销毁对象取不到 id：这些记账随 SuspendForScopeExit/DropStaleWorldState
            // 的整表清空一起丢弃，不会指到活对象上。
        }
    }

    private void OnDestroy()
    {
        if (_instance != this) return;
        if (GreekBankScope.IsAuthorityBanker(_mainBanker))
        {
            FlushUncreditedCoins();
            ResetAll(releaseClaims: true, destroyActors: false);
        }
        else
        {
            // 失权/非希腊/旧层：只丢本地引用，不向其他世界的对象发 RPC、不入账。
            ResetAll(releaseClaims: false, destroyActors: false);
        }
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
            if (helper.Actor != null && GreekBankScope.IsInCurrentLayer(helper.Actor)) continue;
            // 旧场景/已销毁的 actor 引用：只丢本地记账，绝不触碰旧对象。
            if (helper.Actor != null) ForgetTargetLocally(helper);
            else if (helper.Target != null) ReleaseTarget(helper);
            helper.Actor = null;
            helper.Animator = null;
            helper.PositionSync = null;
            helper.CarriedCoins = 0;
            helper.UncreditedCoins = 0;
            helper.RestockReserved = false;
            helper.WaitDeadline = 0f;
            ActiveCollector[i] = false;
            if (existingActors == null) existingActors = UnityEngine.Object.FindObjectsOfType<PositionSync>();
            string marker = ASSISTANT_PREFIX + i + "_";
            for (int j = 0; j < existingActors.Length; j++)
            {
                PositionSync candidate = existingActors[j];
                if (candidate == null || candidate.gameObject == null
                    || !candidate.gameObject.activeInHierarchy
                    || !candidate.gameObject.name.StartsWith(marker, StringComparison.Ordinal)
                    || !GreekBankScope.IsInCurrentLayer(candidate.gameObject)) continue;
                helper.Actor = candidate.gameObject;
                PatchEconomy_BankAssistants.ApplyAssistantScale(helper.Actor);
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
            if (helper.Actor != null && GreekBankScope.IsInCurrentLayer(helper.Actor)) continue;

            GameObject prefab = PatchEconomy_BankAssistants.GetPrefab(i);
            if (prefab == null || Pool.GetPoolFromPrefabAsset(prefab) == null) continue;

            Vector3 home = GetHomePosition(i);
            GameObject actor = Pool.SpawnGO(
                prefab, home, Quaternion.identity, gameLayer,
                allowInstantiate: false, allowCreatePool: false, assertNonNullPrefab: true);
            if (actor == null) continue;

            helper.Actor = actor;
            PatchEconomy_BankAssistants.ApplyAssistantScale(actor);
            helper.Animator = actor.GetComponent<Animator>();
            helper.PositionSync = actor.GetComponent<PositionSync>();
            helper.Target = null;
            helper.CarriedCoins = 0;
            helper.UncreditedCoins = 0;
            helper.Moving = false;
            helper.WaitDeadline = 0f;
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
            {
                AssistantState helper = Assistants[i];
                if (helper.Target != null) ReleaseTarget(helper);
                if (ActiveCollector[i])
                {
                    ClearWaitDeadline(helper);
                    TeleportHomeAndDeposit(helper);
                    ActiveCollector[i] = false;
                }
            }
            Observed.Clear();
            Claims.Clear();
            MatureBuffer.Clear();
            SweepPolicies.Clear();
            FarmOriginCoinIds.Clear();
            return;
        }

        SeenThisScan.Clear();
        MatureBuffer.Clear();

        int count;
        registrar.GetDroppablesInRange<DroppableCurrency>(
            kingdom.campfirePosition, WORLD_SCAN_RANGE, ScanBuffer, out count, null);

        // No registered droppables means there is no candidate work to assign.
        // Still run the small ownership cleanup below: a pooled coin may have
        // disappeared between scans, and returning here would leave its claim
        // behind until an instance id was reused.  Skip sorting/arbitration once
        // stale targets and claims have been retired.
        if (count <= 0)
        {
            CleanupNoCandidates();
            return;
        }

        float now = Time.time;
        int ordinaryPlayerCoins = 0;
        int outsideCoins = 0;
        int externallyClaimed = 0;
        int farmCoins = 0;
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
            bool farmOrigin = IsFarmOriginCoin(coin);
            if (!IsTrackableCoin(coin, domainLeft, domainRight, farmOrigin)) continue;
            if (farmOrigin) farmCoins++;

            int id = coin.gameObject.GetInstanceID();
            SeenThisScan.Add(id);
            if (!Observed.TryGetValue(id, out ObservedCoin observation))
            {
                observation = new ObservedCoin
                {
                    Coin = coin,
                    FirstObservedAt = now,
                    // 农田币走独立更长成熟期（给玩家留出自己捡的窗口，见常量注释）。
                    MaturitySeconds = farmOrigin
                        ? FARM_COIN_MATURITY_SECONDS
                        : COIN_MATURITY_SECONDS
                };
                Observed[id] = observation;
            }
            else
            {
                observation.Coin = coin;
            }

            if (!Claims.ContainsKey(id)
                && now - observation.FirstObservedAt >= observation.MaturitySeconds)
                MatureBuffer.Add(observation);
        }

        RemovalBuffer.Clear();
        foreach (var pair in Observed)
        {
            if (!SeenThisScan.Contains(pair.Key) && !Claims.ContainsKey(pair.Key))
                RemovalBuffer.Add(pair.Key);
        }
        for (int i = 0; i < RemovalBuffer.Count; i++)
        {
            Observed.Remove(RemovalBuffer[i]);
            FarmOriginCoinIds.Remove(RemovalBuffer[i]);
        }

        MatureBuffer.Sort(CompareObservedCoins);

        for (int i = 0; i < Assistants.Length; i++)
        {
            AssistantState helper = Assistants[i];
            if (helper.Target != null && !IsValidOwnedTarget(helper))
                ReleaseTarget(helper);
            // 只有活跃收集者可以持有目标；非活跃助手的认领一律释放。
            if (!ActiveCollector[i] && helper.Target != null)
                ReleaseTarget(helper);
        }

        // 满趟或演员消失的活跃收集者收工：回家清账并退出活跃集合。
        for (int i = 0; i < Assistants.Length; i++)
        {
            AssistantState helper = Assistants[i];
            if (!ActiveCollector[i]) continue;
            if (helper.Actor == null || !helper.Actor.activeInHierarchy)
            {
                if (helper.Target != null) ReleaseTarget(helper);
                ClearWaitDeadline(helper);
                ActiveCollector[i] = false;
            }
            else if (TripComplete(helper))
            {
                ClearWaitDeadline(helper);
                TeleportHomeAndDeposit(helper);
                ActiveCollector[i] = false;
            }
        }

        // 积压扩容：目标活跃数 = 1 + 成熟币数/8，上限为全部助手。
        int activeCount = CountActiveCollectors();
        int targetActive = Math.Min(Assistants.Length,
            1 + MatureBuffer.Count / (int)ACTIVE_SCALING_STEP);
        if (activeCount < targetActive && MatureBuffer.Count > 0)
            SelectNextCollectors(targetActive);

        // 分配：只给没有目标且属于活跃收集者集合的助手补分配。链式逻辑内部走
        // TryAssign（含全部在线门禁与瞬移规则），失败则收工：回家清账并退出集合。
        for (int i = 0; i < Assistants.Length; i++)
        {
            AssistantState helper = Assistants[i];
            if (!ActiveCollector[i] || helper.Target != null) continue;
            TryChainNextTarget(helper);
        }

        LogActiveCollectorCountIfChanged(now);

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
                $"[BankAssistants] scan observed={count}, playerCoins={ordinaryPlayerCoins}, farmCoins={farmCoins}, outside={outsideCoins}, tracked={Observed.Count}, mature={MatureBuffer.Count}, assigned={Claims.Count}, externallyClaimed={externallyClaimed}, collectors={CountActiveCollectors()}");
        }
    }

    private static bool IsTrackableCoin(DroppableCurrency coin,
        float domainLeft, float domainRight, bool farmOrigin = false)
    {
        if (coin == null || !coin.isActiveAndEnabled || coin.gameObject == null) return false;
        // 玩家投掷币按 DropType；农田币没有独立枚举值（2.1.0 源码 DropType 只有
        // Player/Wildlife/Citizen，农田币落 Wildlife 桶，与狩猎/宝箱等混同），走
        // Droppable_FarmCoinOrigin_Mark_Patch 的精确来源标记准入。
        if (coin.droppedBy != DropType.Player && !farmOrigin) return false;
        if (coin.CurrencyType != CurrencyType.Coins) return false;
        if (coin.IsFake()) return false;

        float x = coin.transform.position.x;
        // 主银行家领域排除的目的是避免与原生银行家抢币——但原生银行家只认
        // DropType.Player（Banker.ClaimCoins），领域内的农田币没有任何原生收集者，
        // 不豁免就永远没人捡。故农田币豁免领域排除，玩家投掷币照旧。
        if (!farmOrigin
            && PatchEconomy_Banker.IsInMainBankerDomain(x, domainLeft, domainRight))
            return false;
        // A temporary native claim must not reset the three-second maturity clock.
        // TryFriendlyClaim remains the atomic assignment gate below.
        return true;
    }

    private static void CleanupNoCandidates()
    {
        if (!RollbackOwnedClaimPolicies()) { _cleanupPending = true; return; }
        LiveClaimIds.Clear();
        for (int i = 0; i < Assistants.Length; i++)
        {
            AssistantState helper = Assistants[i];
            if (helper.Target != null && !IsValidOwnedTarget(helper))
                ReleaseTarget(helper);
            if (!ActiveCollector[i] && helper.Target != null)
                ReleaseTarget(helper);

            if (ActiveCollector[i] && helper.Target == null)
            {
                // count<=0 的短暂空快照与成熟断流同义：本趟已收>0 且未满趟时原地等待有界
                // gap（绝不从已清空的旧快照重新指派），到点仍无币才回家退出活跃集合。
                if (TryStartWaitForNextCoin(helper)) continue;
                ClearWaitDeadline(helper);
                if (helper.CarriedCoins > 0) TeleportHomeAndDeposit(helper);
                DeactivateCollector(i);
            }

            if (ActiveCollector[i] && helper.Target != null)
                LiveClaimIds.Add(helper.Target.gameObject.GetInstanceID());
        }

        RemovalBuffer.Clear();
        foreach (KeyValuePair<int, int> pair in Claims)
        {
            if (!LiveClaimIds.Contains(pair.Key)) RemovalBuffer.Add(pair.Key);
        }
        for (int i = 0; i < RemovalBuffer.Count; i++) Claims.Remove(RemovalBuffer[i]);

        Observed.Clear();
        MatureBuffer.Clear();
        SweepPolicies.Clear();
        SweepCoins.Clear();
        FarmOriginCoinIds.Clear();
    }

    private static int CompareCoinsDeterministically(DroppableCurrency left, DroppableCurrency right)
    {
        if (left == null) return right == null ? 0 : 1;
        if (right == null) return -1;
        int xCompare = left.transform.position.x.CompareTo(right.transform.position.x);
        if (xCompare != 0) return xCompare;
        return left.gameObject.GetInstanceID().CompareTo(right.gameObject.GetInstanceID());
    }

    private static int CompareObservedCoins(ObservedCoin left, ObservedCoin right)
    {
        if (left == null) return right == null ? 0 : 1;
        if (right == null) return -1;
        return CompareCoinsDeterministically(left.Coin, right.Coin);
    }

    private static int CountActiveCollectors()
    {
        int count = 0;
        for (int i = 0; i < ActiveCollector.Length; i++)
            if (ActiveCollector[i]) count++;
        return count;
    }

    private static void DeactivateCollector(int index)
    {
        if (index >= 0 && index < ActiveCollector.Length) ActiveCollector[index] = false;
    }

    private static void SelectNextCollectors(int targetActive)
    {
        int selected = CountActiveCollectors();
        // 轮转起点必须在循环外定格：循环体内推进 _nextCollectorIndex 再用它算
        // index 会在 3-4 并发时跳位（只激活 3 个且顺序偏离轮转）。
        int start = _nextCollectorIndex;
        int lastSelected = -1;
        for (int offset = 0; offset < Assistants.Length && selected < targetActive; offset++)
        {
            int index = (start + offset) % Assistants.Length;
            AssistantState helper = Assistants[index];
            if (helper.RestockReserved || ActiveCollector[index] || helper.Actor == null
                || !helper.Actor.activeInHierarchy || TripComplete(helper)) continue;

            ActiveCollector[index] = true;
            selected++;
            lastSelected = index;
        }
        if (lastSelected >= 0)
            _nextCollectorIndex = (lastSelected + 1) % Assistants.Length;
    }

    private static void LogActiveCollectorCountIfChanged(float now)
    {
        int active = CountActiveCollectors();
        if (active == _lastLoggedActiveCount || now < _nextActiveCountLogAt) return;
        _lastLoggedActiveCount = active;
        _nextActiveCountLogAt = now + 30f;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            $"[BankAssistants] active collectors={active} (mature={MatureBuffer.Count})");
    }

    private static bool TryAssign(AssistantState helper, DroppableCurrency coin)
    {
        if (_cleanupPending || !GreekBankScope.IsAuthorityBanker(_mainBanker)
            || helper == null || !GreekBankScope.IsInCurrentLayer(helper.Actor)
            || !GreekBankScope.IsInCurrentLayer(coin) || helper.Target != null) return false;
        if (helper.RestockReserved || helper.Actor == null || coin == null) return false;
        if (NetworkBigBoss.IsOnline
            && (!NetworkBigBoss.HasClientCaughtUp || helper.PositionSync == null
                || helper.PositionSync.parentHeaderRef == null
                || coin.parentHeaderRef == null)) return false;
        int id = coin.gameObject.GetInstanceID();
        if (Claims.ContainsKey(id)) return false;
        if (!coin.TryFriendlyClaim(helper.Actor, 20f)) return false;

        helper.OriginalPolicy = coin.pickUpPolicy;
        Claims[id] = helper.Index;
        helper.Target = coin;
        try
        {
            coin.pickUpPolicy = PickUpPolicy.OnlyClaimer;
            coin.SendPolicyRPC();
        }
        catch { _cleanupPending = true; return false; }

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

    // 从最新成熟快照里选未被认领且距离助手最近（|coin.x - actor.x| 最小，
    // 并列按 x 再按 instanceID 决定性）的合法金币；选中后走 TryAssign。
    // 最近候选认领失败（如被村民原生认领）时依次退让到次近候选，避免
    // "收工回家→下个扫描又选中同一枚"的瞬移抖动循环；全部失败才返回 false。
    private static bool AssignNextTarget(AssistantState helper)
    {
        if (helper.Actor == null) return false;
        // 全局在线门禁与具体币无关，提前预检，避免 client 未追上时
        // 对整个快照做 O(N²) 的逐候选空转（帧尖峰）。
        if (NetworkBigBoss.IsOnline
            && (!NetworkBigBoss.HasClientCaughtUp || helper.PositionSync == null
                || helper.PositionSync.parentHeaderRef == null)) return false;
        float actorX = helper.Actor.transform.position.x;
        TriedThisChain.Clear();
        while (true)
        {
            DroppableCurrency best = null;
            float bestDistance = float.MaxValue;
            float bestX = 0f;
            int bestId = 0;
            for (int i = 0; i < MatureBuffer.Count; i++)
            {
                DroppableCurrency candidate = MatureBuffer[i] != null ? MatureBuffer[i].Coin : null;
                if (candidate == null || candidate.gameObject == null
                    || !candidate.isActiveAndEnabled) continue;
                int candidateId = candidate.gameObject.GetInstanceID();
                if (Claims.ContainsKey(candidateId) || TriedThisChain.Contains(candidateId)) continue;

                float coinX = candidate.transform.position.x;
                float distance = Mathf.Abs(coinX - actorX);
                if (distance > bestDistance) continue;
                if (distance < bestDistance
                    || (distance == bestDistance && coinX < bestX)
                    || (distance == bestDistance && coinX == bestX && candidateId < bestId))
                {
                    best = candidate;
                    bestDistance = distance;
                    bestX = coinX;
                    bestId = candidateId;
                }
            }
            if (best == null) return false;
            if (TryAssign(helper, best)) return true;
            TriedThisChain.Add(best.gameObject.GetInstanceID());
        }
    }

    // 快照里是否存在比当前目标更近的未认领成熟币（用于决定链式是否换向）。
    private static bool HasCloserUnclaimed(AssistantState helper)
    {
        DroppableCurrency target = helper.Target;
        if (helper.Actor == null || target == null || target.gameObject == null) return false;
        int targetId = target.gameObject.GetInstanceID();
        float actorX = helper.Actor.transform.position.x;
        float targetDistance = Mathf.Abs(target.transform.position.x - actorX);
        for (int i = 0; i < MatureBuffer.Count; i++)
        {
            DroppableCurrency candidate = MatureBuffer[i] != null ? MatureBuffer[i].Coin : null;
            if (candidate == null || candidate.gameObject == null
                || !candidate.isActiveAndEnabled) continue;
            int candidateId = candidate.gameObject.GetInstanceID();
            if (candidateId == targetId || Claims.ContainsKey(candidateId)) continue;
            if (Mathf.Abs(candidate.transform.position.x - actorX) < targetDistance) return true;
        }
        return false;
    }

    // 每枚结算成功后的链式补位：满趟→回家清账并退出活跃集合；当前目标仍有效且
    // 就是最近的未认领币→保持（避免释放-重认领的 RPC 抖动）；否则释放旧目标后就近补链；
    // 补链失败：本趟已收>0 且未到 20 时原地等待一个断流 gap（保持 active，下一轮扫描
    // 继续找币），空手或等待到期才回家清账并退出。成功路径 Moving/动画速度全程保持
    // 奔跑，无停顿帧。
    private static bool TryChainNextTarget(AssistantState helper)
    {
        if (helper.RestockReserved) return false;
        if (TripComplete(helper))
        {
            ClearWaitDeadline(helper);
            TeleportHomeAndDeposit(helper);
            DeactivateCollector(helper.Index);
            return false;
        }

        if (helper.Target != null)
        {
            if (IsValidOwnedTarget(helper) && !HasCloserUnclaimed(helper))
                return true;
            ReleaseTarget(helper);
        }
        if (AssignNextTarget(helper))
        {
            ClearWaitDeadline(helper);
            return true;
        }

        helper.Moving = false;
        SetAnimationSpeed(helper, 0f);
        if (TryStartWaitForNextCoin(helper)) return false;
        ClearWaitDeadline(helper);
        if (helper.CarriedCoins > 0) TeleportHomeAndDeposit(helper);
        DeactivateCollector(helper.Index);
        return false;
    }

    private static void UpdateMovingAssistants()
    {
        float step = ASSISTANT_RUN_SPEED * Time.deltaTime;
        for (int i = 0; i < Assistants.Length; i++)
        {
            AssistantState helper = Assistants[i];
            if (helper.RestockReserved || helper.Actor == null || helper.Target == null) continue;
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

            // 顺路扫吸：移动后先吸收 SWEEP_RADIUS 内未认领的成熟币（目标币已在
            // Claims 中，天然跳过）。结算路径与目标币完全一致，一路跑一路吸。
            SweepNearbyCoins(helper);
            if (_cleanupPending) return;
            if (helper.Actor == null || helper.Target == null) continue;

            current = helper.Actor.transform.position;
            target = helper.Target.transform.position;
            target.y = current.y;
            target.z = current.z;
            if (Mathf.Abs(current.x - target.x) > PICKUP_DISTANCE) continue;

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
            FarmOriginCoinIds.Remove(id);
            helper.Target = null;
            helper.CarriedCoins++;
            // 新成功拾取=这一趟仍在继续：断流 deadline 清零，下一次断流重新计时。
            ClearWaitDeadline(helper);

            // 链式补位（当帧）：满趟→回家清账；断流→原地等待/收工；成功→保持奔跑无停顿。
            TryChainNextTarget(helper);
        }
    }

    // 顺路扫吸：仅 authority 侧（调用方已保证）。遍历最新成熟快照，对未被认领
    // 且 |coin.x - actor.x| <= SWEEP_RADIUS 的成熟金币，执行与目标币结算完全
    // 相同的认领（TryFriendlyClaim）→ 全部门禁（CanCommitPickup）→ SetFake/
    // pickedUp → DepositFromAssistant → 池回收 → Claims/Observed 清理 →
    // CarriedCoins++ 路径；结算后同样接链式补位。快照为空时直接跳过。
    private static void SweepNearbyCoins(AssistantState helper)
    {
        if (helper.Actor == null || MatureBuffer.Count == 0) return;
        float actorX = helper.Actor.transform.position.x;
        for (int i = 0; i < MatureBuffer.Count; i++)
        {
            DroppableCurrency coin = MatureBuffer[i] != null ? MatureBuffer[i].Coin : null;
            if (coin == null || coin.gameObject == null || !coin.isActiveAndEnabled) continue;
            if (Mathf.Abs(coin.transform.position.x - actorX) > SWEEP_RADIUS) continue;

            int id = coin.gameObject.GetInstanceID();
            if (Claims.ContainsKey(id)) continue;

            if (!TryClaimSweepCoin(helper, coin)) continue;
            if (!CanCommitPickup(helper, coin)
                || Pool.GetPoolByInstance(coin.gameObject) == null)
            {
                RollbackSweepClaim(helper, coin);
                continue;
            }

            // 与目标币完全相同的 authority 主线程事务：先冻结/标记物理币并同步，
            // 再入账恰好一枚。只有入账成功才池回收，经济与物理总数不会分叉。
            coin.SetFake(true);
            coin.pickedUp = true;
            if (NetworkBigBoss.IsOnline) coin.SyncPickedUpAndFake();
            int accepted = PatchEconomy_Banker.DepositFromAssistant(_mainBanker, 1);
            if (accepted != 1)
            {
                coin.pickedUp = false;
                coin.SetFake(false);
                if (NetworkBigBoss.IsOnline) coin.SyncPickedUpAndFake();
                RollbackSweepClaim(helper, coin);
                continue;
            }

            Pool.Despawn(coin.gameObject, true);
            SweepPolicies.Remove(id);
            SweepCoins.Remove(id);
            Claims.Remove(id);
            Observed.Remove(id);
            FarmOriginCoinIds.Remove(id);
            helper.CarriedCoins++;
            // 新成功拾取=这一趟仍在继续：断流 deadline 清零，下一次断流重新计时。
            ClearWaitDeadline(helper);

            // 链式补位；失败（含满趟/断流等待）意味着本帧停止继续扫。
            if (!TryChainNextTarget(helper)) return;
            if (helper.Actor == null) return;
            actorX = helper.Actor.transform.position.x;
        }
    }

    private static bool TryClaimSweepCoin(AssistantState helper, DroppableCurrency coin)
    {
        if (_cleanupPending || !GreekBankScope.IsAuthorityBanker(_mainBanker)
            || helper == null || !GreekBankScope.IsInCurrentLayer(helper.Actor)
            || !GreekBankScope.IsInCurrentLayer(coin)) return false;
        if (helper.RestockReserved || helper.Actor == null || coin == null) return false;
        if (NetworkBigBoss.IsOnline
            && (!NetworkBigBoss.HasClientCaughtUp || helper.PositionSync == null
                || helper.PositionSync.parentHeaderRef == null
                || coin.parentHeaderRef == null)) return false;
        int id = coin.gameObject.GetInstanceID();
        if (Claims.ContainsKey(id)) return false;
        if (!coin.TryFriendlyClaim(helper.Actor, 20f)) return false;

        // 顺吸可能同时持有多个认领，原始策略按币记录，绝不覆盖单槽 OriginalPolicy。
        SweepPolicies[id] = coin.pickUpPolicy;
        SweepCoins[id] = coin;
        Claims[id] = helper.Index;
        try
        {
            coin.pickUpPolicy = PickUpPolicy.OnlyClaimer;
            coin.SendPolicyRPC();
        }
        catch { _cleanupPending = true; return false; }
        return true;
    }

    // 认领回滚：恢复该币原始拾取策略、释放友好认领并清除 Claims 条目，
    // 与 ReleaseTarget 的回滚语义一致（回滚只作用于自己的认领）。
    private static void RollbackSweepClaim(AssistantState helper, DroppableCurrency coin)
    {
        if (coin == null || coin.gameObject == null) return;
        int id = coin.gameObject.GetInstanceID();
        if (SweepPolicies.TryGetValue(id, out PickUpPolicy original)
            && !TryRestoreClaim(coin, helper.Actor, original))
        { _cleanupPending = true; return; }
        SweepPolicies.Remove(id);
        SweepCoins.Remove(id);
        Claims.Remove(id);
    }

    private static void UpdateIdlePatrols(Kingdom kingdom)
    {
        if (!NetworkBigBoss.HasWorldAuth || kingdom == null) return;
        GetWallInterior(kingdom, out float wallLeft, out float wallRight);
        float step = ASSISTANT_PATROL_SPEED * Time.deltaTime;

        for (int i = 0; i < Assistants.Length; i++)
        {
            AssistantState helper = Assistants[i];
            if (helper.RestockReserved || ActiveCollector[i] || helper.Target != null || helper.Actor == null
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
        // 结算（含 SetFake/入账）只在希腊世界权威侧发生；其他世界一律拒绝。
        if (!GreekBankScope.IsActive || !NetworkBigBoss.HasWorldAuth
            || helper == null || helper.Actor == null
            || coin == null || coin.gameObject == null || !coin.isActiveAndEnabled
            || coin.pickedUp || coin.IsFake()) return false;
        if (!GreekBankScope.IsAuthorityBanker(_mainBanker)
            || !GreekBankScope.IsInCurrentLayer(helper.Actor)
            || !GreekBankScope.IsInCurrentLayer(coin)) return false;
        // 与 IsTrackableCoin 同步：农田币走来源标记准入（droppedBy 与扫描侧一致放宽）。
        if (coin.droppedBy != DropType.Player && !IsFarmOriginCoin(coin))
            return false;
        if (coin.CurrencyType != CurrencyType.Coins)
            return false;

        Managers managers = Managers.Inst;
        Kingdom kingdom = managers != null ? managers.kingdom : null;
        // 农田币同样豁免领域排除（原生银行家不捡农田币，领域内无收集者），
        // 否则扫描侧放行、结算侧拒绝会造成认领/回滚 RPC 抖动。
        if (!IsFarmOriginCoin(coin)
            && (!PatchEconomy_Banker.TryGetMainBankerDomain(
                    kingdom, out float domainLeft, out float domainRight)
                || PatchEconomy_Banker.IsInMainBankerDomain(
                    coin.transform.position.x, domainLeft, domainRight))) return false;

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

    private static bool ReleaseTarget(AssistantState helper)
    {
        DroppableCurrency coin = helper.Target;
        if (!TryRestoreClaim(coin, helper.Actor, helper.OriginalPolicy))
        { _cleanupPending = true; return false; }
        if (coin != null && coin.gameObject != null) Claims.Remove(coin.gameObject.GetInstanceID());
        helper.Target = null;
        helper.Moving = false;
        if (helper.Actor != null && GreekBankScope.IsInCurrentLayer(helper.Actor)) SetAnimationSpeed(helper, 0f);
        helper.PatrolResumeAt = Time.time + PatrolPauseSeconds(helper.Index);
        return true;
    }

    private static void TeleportHomeAndDeposit(AssistantState helper)
    {
        if (helper.Actor == null) return;
        // 回家=这一趟结束：断流 deadline 一律清零，绝不带进下一趟。
        ClearWaitDeadline(helper);
        if (helper.Target != null && !ReleaseTarget(helper)) return;

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

    // 用户拍板（2026-08-31）：原版容量=银行家钱包容量（资产个位数），投掷量上来后
    // 助手几个币就瞬移回家一趟，观感碎。保底 100 + 原生×10（回家是瞬移清账，
    // 大容量零额外成本；4 助手×100 ≈ 2000 币投掷量约 5 趟/人）。
    private const int AssistantCapacityFloor = 100;
    private const int AssistantCapacityMultiplier = 10;

    private static int GetAssistantCapacity()
    {
        Wallet wallet = _mainBanker != null ? _mainBanker._wallet : null;
        return wallet != null
            ? Math.Max(AssistantCapacityFloor, wallet.TotalCapacity * AssistantCapacityMultiplier)
            : AssistantCapacityFloor;
    }

    /// <summary>
    /// 本趟收集上限：每趟目标 TRIP_TARGET，但绝不超过真实容量（容量 helper 语义与下限
    /// 100 不动，20 恒成立）。扫描收工、链式补位、选择收集者共用同一个完成判定。
    /// </summary>
    private static int GetTripCapacity() => Math.Min(TRIP_TARGET, GetAssistantCapacity());

    private static bool TripComplete(AssistantState helper)
        => helper != null && helper.CarriedCoins >= GetTripCapacity();

    /// <summary>
    /// 断流等待：活跃收集者本趟已入账 &gt;0、未满趟，又找不到下一枚合法成熟币时，原地静止
    /// 等一个有界 gap。首个 deadline 一旦建立后续扫描不再续期；返回 true = 保持 active
    /// 让下一轮扫描继续找币。到点仍无币返回 false，调用方回家清 carried 并退出活跃集合。
    /// 空手（本趟未收过币）不等待，维持立即收工的原语义。
    /// </summary>
    private static bool TryStartWaitForNextCoin(AssistantState helper)
    {
        // 演员已消失/失活时没有可等待的收集者：保持原“立即收工”语义（扫描侧本就
        // 会把这类活跃收集者移出集合，这里覆盖 count<=0 的 CleanupNoCandidates 路径）。
        if (helper == null || helper.Actor == null || !helper.Actor.activeInHierarchy
            || helper.CarriedCoins <= 0 || TripComplete(helper)) return false;
        if (helper.WaitDeadline <= 0f)
            helper.WaitDeadline = Time.time + WAIT_GAP_SECONDS;
        return Time.time < helper.WaitDeadline;
    }

    private static void ClearWaitDeadline(AssistantState helper)
    {
        if (helper != null) helper.WaitDeadline = 0f;
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
        PatchEconomy_AutoRestock.Reset(false);
        AutoRestockCounts.Reset();
        // RestockReserved 与断流 deadline 都必须在失权/上下文未知的提前 return 之前清掉：
        // 残留 deadline 会把下一次收集当成同一趟等待。
        foreach (var helper in Assistants) { helper.RestockReserved = false; helper.WaitDeadline = 0f; }
        _cleanupPending = true;
        _cleanupDestroyActors |= destroyActors;
        _cleanupSyncDespawn |= syncDespawn;
        // Never discard live claim ownership merely because auth/biome is temporarily unknown.
        bool authority = NetworkBigBoss.HasWorldAuth;
        bool ready = CleanupContextReady();
        bool ownsNative = false;
        foreach (var helper in Assistants) ownsNative |= helper.Target != null || helper.Actor != null;
        foreach (var coin in SweepCoins.Values) ownsNative |= coin != null;
        // Idle actors still belong to this cleanup operation. Do not forget them
        // while loading or on a client; once every native object is gone, clearing
        // only these local records is safe even without authority.
        if (ownsNative && (!authority || !ready)) return;
        bool released = RollbackOwnedClaimPolicies();
        foreach (var helper in Assistants)
            if (helper.Target != null && !ReleaseTarget(helper)) released = false;
        if (!released) return;
        foreach (var helper in Assistants)
        {
            bool current = helper.Actor != null && GreekBankScope.IsInCurrentLayer(helper.Actor);
            if (_cleanupDestroyActors && current && authority && ready)
            {
                if (NetworkBigBoss.IsOnline && (!NetworkBigBoss.HasClientCaughtUp
                    || helper.PositionSync == null || helper.PositionSync.parentHeaderRef == null)) return;
                try
                {
                    if (_cleanupSyncDespawn && Pool.GetPoolByInstance(helper.Actor) != null)
                        Pool.Despawn(helper.Actor, true);
                    else UnityEngine.Object.Destroy(helper.Actor);
                }
                catch { return; }
            }
            helper.Actor = null;
            helper.Animator = null;
            helper.PositionSync = null;
            helper.CarriedCoins = 0; // already credited at pickup; never deposit twice
            helper.UncreditedCoins = 0;
            helper.Moving = false;
            helper.PatrolRight = (helper.Index & 1) == 0;
            helper.PatrolResumeAt = Time.time + PatrolPauseSeconds(helper.Index);
        }
        for (int i = 0; i < ActiveCollector.Length; i++) ActiveCollector[i] = false;
        _nextCollectorIndex = 0;
        Claims.Clear(); Observed.Clear(); SeenThisScan.Clear(); MatureBuffer.Clear();
        RemovalBuffer.Clear(); SweepPolicies.Clear(); SweepCoins.Clear(); FarmOriginCoinIds.Clear();
        _lastLoggedActiveCount = -1;
        _nextActiveCountLogAt = 0f;
        _nextScanAt = Time.time + SCAN_INTERVAL;
        _nextDiagnosticsAt = Time.time;
        _cleanupPending = _cleanupDestroyActors = _cleanupSyncDespawn = false;
    }

    /// <summary>
    /// 只处理“上次提交失败遗留的补记”。希腊入账在拾取时已完成，CarriedCoins 不是
    /// 待存金币，绝不能在这里当作真实币再入一次账（会复制钱）。非希腊/非权威一律不动。
    /// </summary>
    private static void FlushUncreditedCoins()
    {
        if (_mainBanker == null || !GreekBankScope.IsAuthorityBanker(_mainBanker)) return;
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

/// <summary>
/// 农田币来源标记（2026-08-30 需求1）。Droppable.Drop 只按 dropper tag 分类
/// （Player→Player；Archer/Worker/Farmer→Citizen；其余一律 Wildlife），农田币
/// （Farmland.DropCoins 以 backgroundRenderer 为 dropper）因此落入 Wildlife 桶，
/// 与野猪/鹿狩猎奖励、宝箱、灌木、树、罐子、骡子、银行家取款吐币、钓鱼竿共用
/// 同一枚举值——不存在"农田专属 DropType"。为满足"只纳入农田"，这里以
/// "dropper 属于 Farmland"精确打标；每次 Drop 重新评估，池化复用的币实例
/// 不可能携带过期农田标。打标只在 authority 侧 Drop 调用时发生，而扫描/认领/
/// 结算也只在 authority 侧运行，天然对齐。
/// </summary>
[HarmonyPatch(typeof(Droppable), nameof(Droppable.Drop),
    new[] { typeof(GameObject), typeof(Vector2), typeof(Vector2),
        typeof(PickUpPolicy), typeof(bool), typeof(bool), typeof(bool) })]
public static class Droppable_FarmCoinOrigin_Mark_Patch
{
    [HarmonyPostfix]
    public static void Postfix(Droppable __instance, GameObject dropper)
    {
        if (__instance == null || __instance.gameObject == null) return;
        int id = __instance.gameObject.GetInstanceID();
        // 只在当前希腊世界打新标记；其他世界绝不新增。清标始终允许（池复用必须清掉
        // 上一个实例的残留标记，否则坐骑技/狩猎掉币会被误当农田币）。
        if (dropper != null && dropper.GetComponentInParent<Farmland>() != null
            && GreekBankScope.IsActive)
            BankAssistantCoordinator.MarkFarmCoin(id);
        else
            BankAssistantCoordinator.ClearFarmCoin(id);
    }
}

/// <summary>
/// 两参 Drop(force, policy) 重载不传 dropper（坐骑技能掉币），必须清掉池化实例
/// 上一次农田掉落残留的标记，否则坐骑技掉出的币会被当农田币延迟吸走。
/// </summary>
[HarmonyPatch(typeof(Droppable), nameof(Droppable.Drop),
    new[] { typeof(Vector2), typeof(PickUpPolicy) })]
public static class Droppable_FarmCoinOrigin_Clear_Patch
{
    [HarmonyPostfix]
    public static void Postfix(Droppable __instance)
    {
        if (__instance == null || __instance.gameObject == null) return;
        BankAssistantCoordinator.ClearFarmCoin(__instance.gameObject.GetInstanceID());
    }
}

/// <summary>Pool lifecycle bridge for scoped sizing and client-side interpolation caches.</summary>
public class BankAssistantVisualLifecycle : MonoBehaviour
{
    public BankAssistantVisualLifecycle(IntPtr ptr) : base(ptr) { }

    private void OnEnable()
    {
        PatchEconomy_BankAssistants.ApplyAssistantScale(gameObject);
    }

    private void OnDisable()
    {
        if (gameObject != null)
        {
            GreekScaleScope.Restore(transform);
            PositionSync_BankAssistantAnimation_Patch.Forget(gameObject.GetInstanceID());
        }
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
        // 纯本地视觉补间，只作用于本 mod 的助手 actor（只可能在希腊世界存在）；
        // 不因无主机权限关闭，但离开希腊后不再驱动旧对象。
        if (!GreekBankScope.IsActive || NetworkBigBoss.HasWorldAuth
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
