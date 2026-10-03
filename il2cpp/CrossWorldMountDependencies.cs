using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 外来坐骑的公共依赖闭包（契约 2/3）：按定义登记 SteedType 查询映射，并按能力引用接入
/// 原生技能同步池。所有状态按**单个定义/单个类型**表达，不用"added&gt;0"之类的全局成功缓存。
///
/// 查询/恢复（契约 2）：
/// - 每个定义类型 + 其进阶别名（P2）各自独立判定：已有映射只按 <c>objectSteedTypePairs</c> 的
///   **精确键**（非 Unity-null）判定 —— 原生 getter 缺键时会回退 Horse Regular（type 8），该回退
///   不是真实映射，不能用来判存在性；精确键已有不同 prefab → 保留原生、该定义不得授予。
///   无精确键 → 加载 prefab、校验 <c>Steed.steedType</c> 后写入（写入前保证 <c>biomeSteeds</c>
///   同 type 不会出现不同/重复实例，避免下一次原生重建 Add 撞键），再经真实 getter
///   （含 <c>BiomeData.GetPrefabSwap</c>）回读核对。
/// - 失败不写永久黑名单：状态可在明确初始化点（资产初始化/池重建/生成前/查询前）重试，
///   仅做短冷却防查询路径抖动。
/// - 资产实例换代（换世界回来）或同实例重初始化（InitializeAssets 再次执行）都会强制重验。
/// - 依赖登记不受"坐骑功能开关"影响（OFF 后已拥有的外来坐骑仍要能查询恢复）；调用方只按
///   模组总开关与希腊上下文设门。
/// - 重入门：登记/补池自身作用域内发生的原生回读（GetSteedByType 会再走我们的查询前缀）仍执行
///   真实原生查询，但不会再次启动登记/补池；作用域同步且以 try/finally 归还，异常也不会滞留。
///
/// 技能同步池（契约 3）：对定义了 <c>PoolCollection</c> 的坐骑，沿 Steed prefab 上的能力组件
/// （SpitSteedAbility._spitPrefab / KelpieSteedAbility.{summer,winter}AttackPrefab）找到攻击预制体，
/// 再从原生 <c>BiomeObjectPools</c> 集合里按 prefab 指针取出**原生池定义**（sync/syncID/preload/
/// capacity/expendable），用现有可靠注册路径（Pool.GetPoolFromPrefabAsset / PoolManager.CreatePoolFor
/// + 三缓存）注册进当前 PoolManager，并回读核对。syncID 冲突（已被别的 prefab 占用）即失败，
/// 不覆盖；池管理器换代或 InitPools 重建后强制重验。
/// </summary>
internal static class CrossWorldMountDependencies
{
    private const string LogPrefix = "[CrossWorldMount]";
    /// <summary>失败后的重试冷却（毫秒）：只用于查询热路径防抖动，初始化点强制重试不受限。</summary>
    private const int RetryCooldownMs = 5000;

    private sealed class SteedState
    {
        internal int TypeId;
        internal string Path;
        internal string Label;
        internal bool Ready;
        internal bool NativeMapping;
        internal ulong ResolvedPointer;
        internal string Failure;
        internal int NextRetryTick;
        internal bool LoggedFailure;
        /// <summary>变体覆盖定义（希腊原生表已有同类型别的世界变体）：不写共享表。</summary>
        internal bool VariantOverride;
        /// <summary>变体覆盖解析出的本定义 prefab 实例（只读资源解析，等同 Resources 缓存）。</summary>
        internal Steed Instance;
        /// <summary>所属定义（marker/receipt 归属与设施名判定用）。</summary>
        internal CrossWorldMountDefinition Definition;
    }

    private sealed class PoolState
    {
        internal bool Ready;
        internal string Failure;
        internal int NextRetryTick;
        internal bool LoggedFailure;
    }

    private static SteedState[] _steedStates;
    private static PoolState[] _poolStates;
    private static ulong _verifiedAssetsPointer;
    private static bool _verified;
    private static bool _allSteedsReady;
    private static ulong _poolManagerPointer;
    private static bool _allPoolsReady;
    private static Il2CppArrayBase<BiomeObjectPools> _poolCollections;
    /// <summary>登记/补池重入门（单线程、同步作用域）：作用域内的原生回读不再启动第二份登记/补池。</summary>
    private static bool _inDependencyScope;

    /// <summary>变体覆盖类型集合（catalog 派生；非该集合的查询/生成走 O(1) 早退）。</summary>
    private static readonly HashSet<int> VariantTypes = BuildVariantTypes();
    /// <summary>查询覆盖 receipt 兜底：正结果按稳定身份（campaign/reign/landData）保持，负结果短冷却。</summary>
    private static readonly HashSet<string> _receiptGranted = new HashSet<string>(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> _receiptNegativeTicks = new Dictionary<string, int>(StringComparer.Ordinal);
    private static ulong _receiptCampaignPointer;
    private static int _receiptReignIndex = int.MinValue;
    private static ulong _receiptLandDataPointer;
    /// <summary>未命中回执的重扫间隔（毫秒）；写标记/换战役立即失效。</summary>
    private const int ReceiptRetryMs = 2000;

    /// <summary>诊断：已就绪的外来 SteedType 数（含别名）。</summary>
    internal static int ReadySteedCount
    {
        get
        {
            if (_steedStates == null) return 0;
            int n = 0;
            for (int i = 0; i < _steedStates.Length; i++) if (_steedStates[i].Ready) n++;
            return n;
        }
    }

    internal static bool AllSteedsReady => _allSteedsReady;

    /// <summary>
    /// 授予门：本定义必需的查询映射（主类型 + 全部 P2 别名）+ 技能池依赖全部就绪。
    /// 逐条独立判定：其他定义/别名的失败不阻断本条（未做副作用，先由 EnsureAll 落状态）。
    /// </summary>
    internal static bool IsDefinitionReady(CrossWorldMountDefinition definition)
    {
        if (definition == null) return false;
        EnsureStates();
        int index = IndexOfDefinition(definition);
        if (index < 0) return false;
        if (!_steedStates[index].Ready) return false;
        // P2 别名属于本定义的获取链：原生保存可能记录别名类型，任一别名缺失/不等价都不得授予。
        for (int a = 0; a < definition.Aliases.Length; a++)
        {
            if (!IsSteedTypeReady(definition.Aliases[a].SteedTypeId)) return false;
        }
        if (definition.PoolCollection != null && definition.PoolCollection.Length > 0)
        {
            if (_poolStates == null || !_poolStates[index].Ready) return false;
        }
        return true;
    }

    /// <summary>按 SteedType 查登记状态（定义 + 别名表内类型唯一；未找到视作未就绪）。</summary>
    private static bool IsSteedTypeReady(int typeId)
    {
        for (int i = 0; i < _steedStates.Length; i++)
            if (_steedStates[i].TypeId == typeId) return _steedStates[i].Ready;
        return false;
    }

    internal static bool IsPoolDefinition(CrossWorldMountDefinition definition)
        => definition != null && definition.PoolCollection != null && definition.PoolCollection.Length > 0;

    /// <summary>诊断文本（首个未就绪定义的原因），仅日志用。</summary>
    internal static string FirstNotReadyReason()
    {
        EnsureStates();
        for (int i = 0; i < _steedStates.Length; i++)
        {
            if (!_steedStates[i].Ready)
                return "type " + _steedStates[i].TypeId + ": " + (_steedStates[i].Failure ?? "pending");
        }
        if (_poolStates != null)
        {
            for (int i = 0; i < _poolStates.Length; i++)
            {
                if (!_poolStates[i].Ready && IsPoolDefinition(CrossWorldMountCatalog.Definitions[i]))
                    return "pool " + CrossWorldMountCatalog.Definitions[i].Id + ": " + (_poolStates[i].Failure ?? "pending");
            }
        }
        return "ready";
    }

    // ------------------------------------------------------------------ entries

    /// <summary>生成前/初始化点：登记缺失定义并补池（幂等；就绪时直接返回）。</summary>
    internal static void EnsureAll()
    {
        EnsureRegistered(false);
        EnsurePools(false);
    }

    /// <summary>资产初始化后（含同实例重初始化）：强制重验所有 SteedType 映射。</summary>
    internal static void OnAssetsInitialized()
    {
        _verified = false;
        _allSteedsReady = false;
        EnsureRegistered(true);
    }

    /// <summary>
    /// GetSteedByType 前缀：首个查询/恢复前保证映射与技能池存在。
    /// 全就绪热路径只做状态/管理器指针比较（不扫描资源、不建池）；失败项沿用各自冷却，
    /// 初始化点（InitializeAssets/InitPools/生成前）才强制重试。
    /// </summary>
    internal static void BeforeQuery(BiomeSpecificAssets assets)
    {
        try
        {
            if (assets == null) return;
            if (!ModConfig.Enabled.Value) return;
            bool mappingsReady = _verified && _allSteedsReady && PointerOf(assets) == _verifiedAssetsPointer;
            if (!mappingsReady)
            {
                EnsureRegistered(false);
                mappingsReady = _allSteedsReady;
            }
            // 映射齐备不代表技能池就绪（OnAssetsInitialized 只登记映射）：查询入口同时承担池恢复。
            if (mappingsReady && !PoolsCurrent()) EnsurePools(false);
        }
        catch (Exception) { /* 查询前缀绝不打断原生调用 */ }
    }

    /// <summary>技能池是否仍有效：全体池就绪 + 当前 PoolManager 未换代（廉价，不触发资源扫描）。</summary>
    private static bool PoolsCurrent()
    {
        if (!_allPoolsReady) return false;
        try
        {
            Managers managers = Managers.Inst;
            return managers != null && managers.pools != null && PointerOf(managers.pools) == _poolManagerPointer;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>PoolManager.InitPools 之后：原生池缓存已重建，强制重验本功能登记的同步池。</summary>
    internal static void OnPoolsRebuilt()
    {
        try
        {
            if (!ModConfig.Enabled.Value) return;
            _allPoolsReady = false;
            if (_poolStates != null) for (int i = 0; i < _poolStates.Length; i++) _poolStates[i].Ready = false;
            EnsurePools(true);
        }
        catch (Exception e)
        {
            LogError("pool rebuild hook failed: " + e.GetType().Name + " " + e.Message);
        }
    }

    // ------------------------------------------------------------------ steed registration

    private static void EnsureRegistered(bool force)
    {
        if (_inDependencyScope) return;   // 登记自身的原生回读不再启动第二份登记
        _inDependencyScope = true;
        try
        {
            if (!GreeceContext(out BiomeSpecificAssets assets)) return;
            ulong pointer = PointerOf(assets);
            bool instanceChanged = !_verified || pointer != _verifiedAssetsPointer;
            if (!instanceChanged && _allSteedsReady && !force) return;

            _verified = true;
            _verifiedAssetsPointer = pointer;
            EnsureStates();

            int now = Environment.TickCount;
            int changed = 0;
            bool allReady = true;
            for (int i = 0; i < _steedStates.Length; i++)
            {
                SteedState state = _steedStates[i];
                if (state.VariantOverride)
                {
                    // 变体覆盖：希腊共享表已有同类型别的世界变体，这里只解析本定义 prefab
                    // （只读），就绪判定与授权 scope 分离，避免"依赖未就绪→永不授予"的循环门。
                    if (state.Ready && state.Instance != null && !force) continue;
                    state.Ready = false;
                    if (!force && !instanceChanged && state.NextRetryTick > now)
                    {
                        allReady = false;
                        continue;
                    }
                    if (TryResolveVariant(state, now)) changed++;
                    if (!state.Ready) allReady = false;
                    continue;
                }
                if (state.Ready)
                {
                    // 就绪也复核一次：资产重初始化可能已清掉映射（force/instanceChanged 时必查）。
                    if (StillResolved(assets, state)) continue;
                    state.Ready = false;
                }
                if (!force && !instanceChanged && state.NextRetryTick > now)
                {
                    allReady = false;
                    continue;
                }
                if (TryRegister(assets, state, now)) changed++;
                if (!state.Ready) allReady = false;
            }

            bool wasReady = _allSteedsReady;
            _allSteedsReady = allReady;
            if (changed > 0 || (allReady && !wasReady))
            {
                Log("steed registration: ready=" + ReadySteedCount + "/" + _steedStates.Length
                    + (allReady ? " (all types resolvable)" : " firstGap=" + FirstNotReadyReason()));
            }
        }
        catch (Exception e)
        {
            _allSteedsReady = false;
            LogError("steed registration failed: " + e.GetType().Name + " " + e.Message);
        }
        finally
        {
            _inDependencyScope = false;
        }
    }

    /// <summary>就绪后每次热路径的廉价复核：映射仍指向同一实例。</summary>
    private static bool StillResolved(BiomeSpecificAssets assets, SteedState state)
    {
        try
        {
            Steed current = assets.GetSteedByType((SteedType)state.TypeId);
            return current != null && PointerOf(current) == state.ResolvedPointer;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 变体覆盖定义的就绪判定（只读）：解析本定义 prefab 资源并校验 <c>Steed.steedType</c>
    /// 等于本类型。不写 shared 表、不读 <c>objectSteedTypePairs</c>——希腊原生表里的同类型
    /// 别的世界变体（type6=Unicorn、type13/17=Greek 狼）字节/实例保持不变。
    /// </summary>
    private static bool TryResolveVariant(SteedState state, int now)
    {
        try
        {
            GameObject prefabObject = Resources.Load<GameObject>(state.Path);
            if (prefabObject == null)
            {
                Fail(state, now, "variant prefab resource missing: " + state.Path);
                return false;
            }
            Steed resolved = prefabObject.GetComponent<Steed>();
            if (resolved == null)
            {
                Fail(state, now, "variant prefab has no Steed component: " + state.Path);
                return false;
            }
            if (resolved.steedType != (SteedType)state.TypeId)
            {
                Fail(state, now, "variant prefab steedType mismatch: " + (int)resolved.steedType
                    + " != " + state.TypeId + " (" + state.Path + ")");
                return false;
            }

            bool first = !state.Ready;
            state.Instance = resolved;
            state.ResolvedPointer = PointerOf(resolved);
            state.Ready = true;
            state.Failure = null;
            state.NextRetryTick = 0;
            if (first)
                Log("variant resource resolved (query override): type " + state.TypeId
                    + " (" + state.Label + ", " + state.Path + ")");
            return true;
        }
        catch (Exception e)
        {
            Fail(state, now, e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// 单条类型：精确键已有等价映射→采纳；缺失→加载校验后写入并回读核对。成功幂等。
    /// 存在性只按 <c>objectSteedTypePairs</c> 精确键与 <c>biomeSteeds</c> 真实列表内容判定：
    /// 原生 getter 缺键时会回退 Horse Regular（type 8），不能把该回退当成“已有映射”；
    /// 回退只允许出现在最终真实 getter 回读里（经 <c>BiomeData.GetPrefabSwap</c>）。
    /// </summary>
    private static bool TryRegister(BiomeSpecificAssets assets, SteedState state, int now)
    {
        try
        {
            GameObject prefabObject = Resources.Load<GameObject>(state.Path);
            if (prefabObject == null)
            {
                Fail(state, now, "prefab resource missing: " + state.Path);
                return false;
            }
            Steed expected = prefabObject.GetComponent<Steed>();
            if (expected == null)
            {
                Fail(state, now, "prefab has no Steed component: " + state.Path);
                return false;
            }
            if (expected.steedType != (SteedType)state.TypeId)
            {
                Fail(state, now, "prefab steedType mismatch: " + (int)expected.steedType + " != " + state.TypeId
                    + " (" + state.Path + ")");
                return false;
            }

            Il2CppSystem.Collections.Generic.Dictionary<SteedType, Steed> pairs = assets.objectSteedTypePairs;
            if (pairs == null)
            {
                Fail(state, now, "objectSteedTypePairs container is null");
                return false;
            }
            Il2CppSystem.Collections.Generic.List<Steed> steeds = assets.biomeSteeds;
            if (steeds == null)
            {
                Fail(state, now, "biomeSteeds container is null");
                return false;
            }

            ulong pointer = PointerOf(expected);
            bool nativeMapping = false;
            if (pairs.TryGetValue((SteedType)state.TypeId, out Steed mapped) && mapped != null)
            {
                if (PointerOf(mapped) != pointer)
                {
                    Fail(state, now, "native mapping for type " + state.TypeId + " points to a different prefab; kept native");
                    return false;
                }
                // 原生/早期登记与期望 prefab 等价：不改写，按就绪采纳；仍走真实 getter 回读。
                nativeMapping = true;
            }
            else
            {
                // 键缺失或值为 Unity-null：字典由 biomeSteeds 重建，先保证列表不会出现同 type 的第二实例。
                bool present = false;
                for (int i = 0; i < steeds.Count; i++)
                {
                    Steed item = steeds[i];
                    if (item == null || item.steedType != (SteedType)state.TypeId) continue;
                    if (PointerOf(item) != pointer)
                    {
                        Fail(state, now, "biomeSteeds already holds a different instance for type " + state.TypeId
                            + "; kept native");
                        return false;
                    }
                    present = true;
                }
                if (!present) steeds.Add(expected);
                pairs[(SteedType)state.TypeId] = expected;
            }

            // 最终真实 getter 回读（原生路径含 BiomeData.GetPrefabSwap）：必须解回期望 prefab。
            Steed readback = assets.GetSteedByType((SteedType)state.TypeId);
            if (readback == null || PointerOf(readback) != pointer)
            {
                Fail(state, now, "registration readback mismatch");
                return false;
            }

            if (nativeMapping && !state.Ready)
                Log("steed type " + state.TypeId + " resolved by equivalent mapping (" + state.Path + ")");
            else if (!nativeMapping)
                Log("registered foreign steed type " + state.TypeId + " (" + state.Label + ", " + state.Path + ")");
            state.Ready = true;
            state.NativeMapping = nativeMapping;
            state.ResolvedPointer = pointer;
            state.Failure = null;
            state.NextRetryTick = 0;
            return true;
        }
        catch (Exception e)
        {
            Fail(state, now, e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    private static void Fail(SteedState state, int now, string reason)
    {
        state.Ready = false;
        state.Failure = reason;
        state.NextRetryTick = unchecked(now + RetryCooldownMs);
        if (!state.LoggedFailure)
        {
            state.LoggedFailure = true;
            LogError("steed dependency not ready (type " + state.TypeId + " " + state.Label + "): " + reason);
        }
    }

    // ------------------------------------------------------------------ variant query / spawn routing

    private static HashSet<int> BuildVariantTypes()
    {
        var set = new HashSet<int>();
        for (int i = 0; i < CrossWorldMountCatalog.Definitions.Length; i++)
        {
            CrossWorldMountDefinition definition = CrossWorldMountCatalog.Definitions[i];
            if (!definition.VariantOverride) continue;
            set.Add(definition.SteedTypeId);
            for (int a = 0; a < definition.Aliases.Length; a++) set.Add(definition.Aliases[a].SteedTypeId);
        }
        return set;
    }

    /// <summary>
    /// <c>GetSteedByType</c> 后缀的结果适配（变体覆盖）。全部成立才替换：总开关 ON；
    /// 请求类型属于变体覆盖集合且本定义资源已就绪；查询的是当前希腊/MtOlympus 资产实例；
    /// 非挑战、非联机；当前战役已授予本定义（reign 标记 或 当前岛设施回执）。
    /// 其余情况（未授予旧战役/其他 world/挑战/联机/未就绪）一律原样返回原生结果。
    /// </summary>
    internal static Steed ResolveQueryResult(BiomeSpecificAssets assets, int requestedType, Steed nativeResult)
    {
        try
        {
            if (assets == null || !VariantTypes.Contains(requestedType)) return nativeResult;
            SteedState state = FindVariantState(requestedType);
            if (state == null || !state.Ready || state.Instance == null) return nativeResult;
            if (!GreekOfflineAssets(assets)) return nativeResult;
            if (!GrantedInCurrentCampaign(state)) return nativeResult;

            Steed resolved = state.Instance;
            try
            {
                // 与原生 getter 语义一致：覆盖值也过一遍当前 biome/挑战替换。Greek/MtOlympus
                // 现表无这两组 original（恒等）；挑战已被 scope 排除，不会走到 challengeSwap。
                Steed swapped = BiomeData.GetPrefabSwap(resolved);
                if (swapped != null) resolved = swapped;
            }
            catch (Exception e)
            {
                // 泛型 AOT 缺口等异常不应影响坐骑恢复：回退为未 swap 的值，仅提示一次。
                LogOnce("query-swap-" + e.GetType().Name,
                    "GetPrefabSwap fallback failed (kept variant prefab): " + e.Message);
            }
            return resolved;
        }
        catch (Exception)
        {
            return nativeResult;
        }
    }

    /// <summary>
    /// <c>SteedSpawn.SpawnSteed</c> 前缀的窄路由：购买与设施恢复生成（SpawnSteeds → SpawnSteed）
    /// 共用该入口。只有"设施名全等本定义获取设施 + 请求类型属于本定义 + 请求 prefab 不是本变体
    /// + 已授予 scope"才替换；不改共享模板/数组/池，不匹配即原样返回。
    /// </summary>
    internal static Steed RouteSpawnSteed(SteedSpawn spawn, Steed requested)
    {
        try
        {
            if (spawn == null || requested == null) return requested;
            int typeId = (int)requested.steedType;
            if (!VariantTypes.Contains(typeId)) return requested;
            SteedState state = FindVariantState(typeId);
            if (state == null || state.Definition == null || !state.Ready || state.Instance == null) return requested;
            if (SamePrefab(requested, state.Instance)) return requested;
            if (!SpawnBelongsToFacility(spawn, state.Definition.FacilityName)) return requested;
            if (!GreekOfflineAssets(null)) return requested;
            if (!GrantedInCurrentCampaign(state)) return requested;
            return state.Instance;
        }
        catch (Exception)
        {
            return requested;
        }
    }

    /// <summary>
    /// SteedSpawn.Pay 动画分支的临时路由 scope（review R2）：记录被临时替换的设施与其原始
    /// steeds 数组引用，Postfix/Finalizer 共用幂等归还。
    /// </summary>
    internal sealed class PayRouteScope
    {
        internal SteedSpawn Spawn;
        internal Il2CppReferenceArray<Steed> Original;
        internal bool Installed;
        internal bool Restored;
    }

    /// <summary>
    /// Pay 前缀：Sakura（useSpawnAnimation=1）的原生 Pay 会直接 Instantiate(steeds[0]) 生成
    /// "备用体"（不经 SpawnSteed 前缀），动画窗口保存会写出 Unicorn prefabPath（Persistent
    /// persistInactive=1 不注销）。这里在**同一已确证 scope** 下把该设施的 steeds 字段临时换成
    /// 逐元素 RouteSpawnSteed 路由后的私有数组副本：不改原数组内容、不改 steedPool、不改共享模板。
    /// 无需路由 / 任一 scope 门未过 → 返回 null 且不产生任何半状态；赋值失败不动字段。
    /// </summary>
    internal static PayRouteScope BeginPayRoute(SteedSpawn spawn)
    {
        try
        {
            if (spawn == null) return null;
            Il2CppReferenceArray<Steed> original = spawn.steeds;
            if (original == null || original.Length == 0) return null;

            bool changed = false;
            var routed = new Il2CppReferenceArray<Steed>(original.Length);
            for (int i = 0; i < original.Length; i++)
            {
                Steed item = original[i];
                Steed resolved = item != null ? RouteSpawnSteed(spawn, item) : null;
                routed[i] = resolved;
                if (PointerOf(resolved) != PointerOf(item)) changed = true;
            }
            if (!changed) return null;

            var scope = new PayRouteScope { Spawn = spawn, Original = original };
            try
            {
                spawn.steeds = routed;   // 原生 Pay 本次调用窗口内看到路由后的备用体
                scope.Installed = true;
            }
            catch (Exception e)
            {
                LogOnce("pay-route-install-" + e.GetType().Name, "pay route install failed: " + e.Message);
                return null;
            }
            return scope;
        }
        catch (Exception e)
        {
            LogOnce("pay-route-" + e.GetType().Name, "pay route failed: " + e.Message);
            return null;
        }
    }

    /// <summary>Postfix/Finalizer 共用：幂等归还 Pay 窗口前的原 steeds 引用（重复调用无副作用）。</summary>
    internal static void RestorePayRoute(PayRouteScope scope)
    {
        try
        {
            if (scope == null || !scope.Installed || scope.Restored) return;
            if (scope.Spawn == null || scope.Original == null)
            {
                scope.Restored = true;
                return;
            }
            scope.Spawn.steeds = scope.Original;
            scope.Restored = true;
        }
        catch (Exception e)
        {
            LogOnce("pay-route-restore-" + e.GetType().Name, "pay route restore failed: " + e.Message);
        }
    }

    /// <summary>写 native 标记后调用：receipt 兜底缓存与身份立即失效（marker 已可直接命中）。</summary>
    internal static void InvalidateGrantCache()
    {
        _receiptGranted.Clear();
        _receiptNegativeTicks.Clear();
        _receiptCampaignPointer = 0UL;
        _receiptReignIndex = int.MinValue;
        _receiptLandDataPointer = 0UL;
    }

    private static SteedState FindVariantState(int requestedType)
    {
        if (_steedStates == null) return null;
        for (int i = 0; i < _steedStates.Length; i++)
        {
            SteedState state = _steedStates[i];
            if (state.VariantOverride && state.TypeId == requestedType) return state;
        }
        return null;
    }

    private static bool SamePrefab(Steed a, Steed b)
    {
        if (a == null || b == null) return false;
        GameObject left = a.gameObject;
        GameObject right = b.gameObject;
        if (left == null || right == null) return false;
        return PointerOf(left) == PointerOf(right);
    }

    /// <summary>设施身份：SteedSpawn 自身或祖先 GO 名（去 "(Clone)" 后缀）与定义设施名全等。</summary>
    private static bool SpawnBelongsToFacility(SteedSpawn spawn, string facilityName)
    {
        if (spawn == null || string.IsNullOrEmpty(facilityName)) return false;
        try
        {
            GameObject go = spawn.gameObject;
            int guard = 0;
            while (go != null && guard++ < 16)
            {
                if (NameMatchesFacility(go.name, facilityName)) return true;
                Transform parent = go.transform != null ? go.transform.parent : null;
                go = parent != null ? parent.gameObject : null;
            }
        }
        catch (Exception) { }
        return false;
    }

    private static bool NameMatchesFacility(string name, string facilityName)
    {
        if (string.IsNullOrEmpty(name)) return false;
        const string cloneSuffix = "(Clone)";
        if (name.EndsWith(cloneSuffix, StringComparison.Ordinal))
            name = name.Substring(0, name.Length - cloneSuffix.Length);
        return string.Equals(name, facilityName, StringComparison.Ordinal);
    }

    /// <summary>
    /// 希腊/MtOlympus 变体覆盖的严格 scope：总开关 ON；Greek 资产实例存在且（expected 非空时）
    /// 就是查询的实例，并且与依赖层最近一次验证的 assets 一致（换代未重验不偷换）；
    /// 非联机；Global 存在且非挑战；<c>Global.GetCurrentCampaign()</c> 非空并与
    /// <c>CampaignSaveData.current</c> 同一实例——菜单/无选中时原生 current 会回退 GetCampaign(0)，
    /// 此时必须保留 native。缺失/不一致/异常一律 fail-closed。
    /// </summary>
    private static bool GreekOfflineAssets(BiomeSpecificAssets expected)
    {
        try
        {
            if (!ModConfig.Enabled.Value) return false;
            BiomeHolder holder = BiomeHolder.Inst;
            if (holder == null || holder.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return false;
            BiomeSpecificAssets current = holder.curBiomeAssets;
            if (current == null) return false;
            if (expected != null && PointerOf(current) != PointerOf(expected)) return false;
            // 依赖就绪是在该 assets 实例上验证的：换代未重验（旧资源状态）不偷换。
            if (!_verified || PointerOf(current) != _verifiedAssetsPointer) return false;
            if (NetworkBigBoss.IsOnline) return false;
            GlobalSaveData global = GlobalSaveData.loaded;
            if (global == null) return false;
            if (global.InChallenge) return false;
            CampaignSaveData currentCampaign = CampaignSaveData.current;
            if (currentCampaign == null) return false;
            CampaignSaveData owner = global.GetCurrentCampaign();
            if (owner == null) return false;
            return PointerOf(owner) == PointerOf(currentCampaign);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 已授予判定：本战役 reign.landData 任一岛的 steedSpawns 含本定义主类型（原生标记），
    /// 或当前岛存档里存在本定义的获取设施回执（标记被覆盖时的兜底，按战役指针缓存）。
    /// 只读；receipt 扫描仅当 marker 未命中时进行。
    /// </summary>
    private static bool GrantedInCurrentCampaign(SteedState state)
    {
        try
        {
            CrossWorldMountDefinition definition = state.Definition;
            if (definition == null) return false;
            CampaignSaveData campaign = CampaignSaveData.current;
            if (campaign == null) return false;
            CampaignSaveData.ReignInfo reign = campaign.currentReign;
            if (reign == null) return false;

            Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData = reign.landData;
            if (landData != null)
            {
                for (int i = 0; i < landData.Count; i++)
                {
                    CampaignSaveData.LandMapData entry = landData[i];
                    if (entry == null) continue;
                    Il2CppStructArray<SteedType> spawns = entry.steedSpawns;
                    if (spawns == null) continue;
                    for (int j = 0; j < spawns.Length; j++)
                    {
                        if ((int)spawns[j] == definition.SteedTypeId) return true;
                    }
                }
            }
            return ReceiptFallback(campaign, definition);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// receipt 兜底（marker 未命中时）：正结果按**稳定身份**缓存 —— campaign 指针 +
    /// <c>campaign.reign</c> 整数 + 当前 Reign 的 landData 容器指针（ReignInfo 是值类型，
    /// get_currentReign 每次 il2cpp_value_box 新包装，不能用包装指针）。
    /// 阴性缓存额外绑定**准确 land**：同 owner 从岛 A 切到岛 B 时立即重扫 B 的设施回执，
    /// 不会被 A 的 2 秒阴性挡住（正结果仍在同一稳定身份内保持）。CurrentLand 未知（&lt;0）时
    /// fail-closed：不使用也不写入 receipt 兜底，不把未知当作沿用旧阴性。
    /// 同 campaign 换王朝/换 landData 容器/切 campaign 都整体失效重查。
    /// </summary>
    private static bool ReceiptFallback(CampaignSaveData campaign, CrossWorldMountDefinition definition)
    {
        CampaignSaveData.ReignInfo reign = campaign != null ? campaign.currentReign : null;
        ulong landDataPointer = reign != null ? PointerOfList(reign.landData) : 0UL;
        if (landDataPointer == 0UL) return false;
        int reignIndex;
        try { reignIndex = campaign.reign; }
        catch (Exception) { return false; }

        ulong campaignPointer = PointerOf(campaign);
        if (campaignPointer != _receiptCampaignPointer || reignIndex != _receiptReignIndex
            || landDataPointer != _receiptLandDataPointer)
        {
            _receiptCampaignPointer = campaignPointer;
            _receiptReignIndex = reignIndex;
            _receiptLandDataPointer = landDataPointer;
            _receiptGranted.Clear();
            _receiptNegativeTicks.Clear();
        }
        string key = definition.Id;
        if (_receiptGranted.Contains(key)) return true;

        int land = CurrentLand(campaign);
        if (land < 0) return false;   // land 未知：fail-closed，不沿用也不写入旧阴性
        string negativeKey = key + "@" + land;
        int now = Environment.TickCount;
        if (_receiptNegativeTicks.TryGetValue(negativeKey, out int retryAt) && retryAt > now) return false;

        bool found = CrossWorldMountRuntime.IslandHasReceipt(campaign, land, definition);
        if (found)
        {
            _receiptGranted.Add(key);
            _receiptNegativeTicks.Remove(negativeKey);
            return true;
        }
        _receiptNegativeTicks[negativeKey] = unchecked(now + ReceiptRetryMs);
        return false;
    }

    private static int CurrentLand(CampaignSaveData campaign)
    {
        try
        {
            Managers managers = Managers.Inst;
            if (managers != null && managers.game != null) return managers.game.currentLand;
        }
        catch (Exception) { }
        try { return campaign != null ? campaign.currentLand : -1; }
        catch (Exception) { return -1; }
    }

    // ------------------------------------------------------------------ skill pools

    private static void EnsurePools(bool force)
    {
        if (_inDependencyScope) return;   // 补池自身的原生回读不再启动第二份登记/补池
        _inDependencyScope = true;
        try
        {
            if (!GreeceContext(out BiomeSpecificAssets assets)) return;
            Managers managers = Managers.Inst;
            PoolManager poolManager = managers != null ? managers.pools : null;
            if (poolManager == null)
            {
                _allPoolsReady = false;
                return;
            }

            EnsureStates();
            ulong managerPointer = PointerOf(poolManager);
            bool managerChanged = managerPointer != _poolManagerPointer;
            if (managerChanged)
            {
                _poolManagerPointer = managerPointer;
                for (int i = 0; i < _poolStates.Length; i++) _poolStates[i].Ready = false;
            }
            if (!managerChanged && _allPoolsReady && !force) return;

            int now = Environment.TickCount;
            int registered = 0;
            bool allReady = true;
            for (int i = 0; i < CrossWorldMountCatalog.Definitions.Length; i++)
            {
                CrossWorldMountDefinition definition = CrossWorldMountCatalog.Definitions[i];
                PoolState state = _poolStates[i];
                if (!IsPoolDefinition(definition))
                {
                    state.Ready = true;
                    continue;
                }
                if (state.Ready && !force && !managerChanged) continue;
                if (!force && !managerChanged && state.NextRetryTick > now)
                {
                    allReady = false;
                    continue;
                }
                if (TryEnsureDefinitionPools(assets, poolManager, definition, state, now)) registered++;
                if (!state.Ready) allReady = false;
            }

            bool wasReady = _allPoolsReady;
            _allPoolsReady = allReady;
            if (registered > 0 || (allReady && !wasReady))
            {
                Log("skill pool registration: " + (allReady ? "all ready" : "firstGap=" + FirstNotReadyReason()));
            }
        }
        catch (Exception e)
        {
            _allPoolsReady = false;
            LogError("skill pool registration failed: " + e.GetType().Name + " " + e.Message);
        }
        finally
        {
            _inDependencyScope = false;
        }
    }

    /// <summary>沿 Steed prefab 的能力组件收集攻击预制体，再按原生池定义逐个注册。</summary>
    private static bool TryEnsureDefinitionPools(BiomeSpecificAssets assets, PoolManager poolManager,
        CrossWorldMountDefinition definition, PoolState state, int now)
    {
        try
        {
            state.Failure = null;
            Steed steed = assets.GetSteedByType((SteedType)definition.SteedTypeId);
            if (steed == null)
            {
                Fail(state, now, "steed type " + definition.SteedTypeId + " not registered");
                return false;
            }
            GameObject prefab = steed.gameObject;
            if (prefab == null)
            {
                Fail(state, now, "steed prefab gameObject null");
                return false;
            }

            var candidates = new List<GameObject>(2);
            try
            {
                foreach (SpitSteedAbility spit in prefab.GetComponentsInChildren<SpitSteedAbility>(true))
                {
                    if (spit != null && spit._spitPrefab != null) AddUnique(candidates, spit._spitPrefab);
                }
                foreach (KelpieSteedAbility kelpie in prefab.GetComponentsInChildren<KelpieSteedAbility>(true))
                {
                    if (kelpie == null) continue;
                    if (kelpie.summerAttackPrefab != null) AddUnique(candidates, kelpie.summerAttackPrefab);
                    if (kelpie.winterAttackPrefab != null) AddUnique(candidates, kelpie.winterAttackPrefab);
                }
            }
            catch (Exception e)
            {
                Fail(state, now, "ability scan failed: " + e.GetType().Name + " " + e.Message);
                return false;
            }
            // 运行期填充的数组（prefab 资产上可能为空）：有则并入，去重后仍以组件扫描为准。
            try
            {
                Il2CppReferenceArray<SteedAbility> abilities = steed.steedAbilities;
                if (abilities != null)
                {
                    for (int i = 0; i < abilities.Length; i++)
                    {
                        SteedAbility ability = abilities[i];
                        if (ability == null) continue;
                        SpitSteedAbility spit = ability.TryCast<SpitSteedAbility>();
                        if (spit != null && spit._spitPrefab != null) AddUnique(candidates, spit._spitPrefab);
                        KelpieSteedAbility kelpie = ability.TryCast<KelpieSteedAbility>();
                        if (kelpie != null)
                        {
                            if (kelpie.summerAttackPrefab != null) AddUnique(candidates, kelpie.summerAttackPrefab);
                            if (kelpie.winterAttackPrefab != null) AddUnique(candidates, kelpie.winterAttackPrefab);
                        }
                    }
                }
            }
            catch (Exception) { }

            if (candidates.Count == 0)
            {
                Fail(state, now, "no pressed-ability prefabs found on steed prefab");
                return false;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                GameObject attackPrefab = candidates[i];
                if (!TryFindNativePool(definition.PoolCollection, attackPrefab,
                        out bool sync, out short syncID, out int preload, out int capacity, out bool expendable))
                {
                    Fail(state, now, "native pool definition not found in '" + definition.PoolCollection
                        + "' for " + attackPrefab.name);
                    return false;
                }
                if (!EnsureRegisteredPool(poolManager, attackPrefab, sync, syncID, preload, capacity, expendable))
                {
                    Fail(state, now, "pool registration failed for " + attackPrefab.name);
                    return false;
                }
            }

            state.Ready = true;
            state.Failure = null;
            state.NextRetryTick = 0;
            Log("skill pools ready: def=" + definition.Id + " pools=" + candidates.Count
                + " collection=" + definition.PoolCollection);
            return true;
        }
        catch (Exception e)
        {
            Fail(state, now, e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    private static void AddUnique(List<GameObject> list, GameObject prefab)
    {
        ulong pointer = PointerOf(prefab);
        for (int i = 0; i < list.Count; i++)
        {
            if (PointerOf(list[i]) == pointer) return;
        }
        list.Add(prefab);
    }

    /// <summary>从原生 BiomeObjectPools 集合按 prefab 指针取回原生池定义（sync/syncID 等的唯一来源）。</summary>
    private static bool TryFindNativePool(string collectionName, GameObject prefab,
        out bool sync, out short syncID, out int preload, out int capacity, out bool expendable)
    {
        sync = false; syncID = 0; preload = 0; capacity = 0; expendable = false;
        try
        {
            if (_poolCollections == null) _poolCollections = Resources.LoadAll<BiomeObjectPools>("");
            Il2CppArrayBase<BiomeObjectPools> collections = _poolCollections;
            if (collections == null) return false;
            ulong pointer = PointerOf(prefab);
            for (int i = 0; i < collections.Length; i++)
            {
                BiomeObjectPools collection = collections[i];
                if (collection == null) continue;
                string name = null;
                try { name = collection.name; } catch (Exception) { }
                if (!string.Equals(name, collectionName, StringComparison.OrdinalIgnoreCase)) continue;
                Il2CppReferenceArray<Pool> pools = collection.biomeObjectPools;
                if (pools == null) continue;
                for (int j = 0; j < pools.Length; j++)
                {
                    Pool pool = pools[j];
                    if (pool == null || pool.prefab == null) continue;
                    if (PointerOf(pool.prefab) != pointer) continue;
                    sync = pool.sync;
                    syncID = pool.syncID;
                    preload = pool.preload;
                    capacity = pool.capacity;
                    expendable = pool.expendable;
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            LogOnce("native-pool-scan-" + e.GetType().Name, "native pool scan failed: " + e.Message);
        }
        return false;
    }

    /// <summary>
    /// 现有可靠注册路径（PatchRoles_Ninja/Castle 同款）：先查 static 池表，缺则 CreatePoolFor；
    /// 字段严格按原生定义覆写；三缓存登记；syncID 冲突或回读不符即失败（不覆盖他池）。
    /// </summary>
    private static bool EnsureRegisteredPool(PoolManager poolManager, GameObject prefab,
        bool sync, short syncID, int preload, int capacity, bool expendable)
    {
        try
        {
            if (sync && poolManager.cachedSyncIdPoolPairs != null
                && poolManager.cachedSyncIdPoolPairs.ContainsKey(syncID))
            {
                Pool byId = poolManager.cachedSyncIdPoolPairs[syncID];
                if (byId == null || byId.prefab == null || PointerOf(byId.prefab) != PointerOf(prefab))
                {
                    string other = byId != null && byId.prefab != null ? byId.prefab.name : "<null>";
                    LogError("refusing pool registration for " + prefab.name + ": syncID " + syncID
                        + " already used by " + other);
                    return false;
                }
            }

            Pool pool = Pool.GetPoolFromPrefabAsset(prefab);
            bool created = pool == null;
            if (created)
            {
                pool = poolManager.CreatePoolFor(prefab);
                if (pool == null)
                {
                    LogError("CreatePoolFor returned null for " + prefab.name);
                    return false;
                }
            }
            else if (pool.prefab == null || PointerOf(pool.prefab) != PointerOf(prefab))
            {
                LogError("existing pool for " + prefab.name + " reports a different prefab; refusing");
                return false;
            }

            pool.preload = preload;
            pool.sync = sync;
            pool.syncID = syncID;
            pool.capacity = capacity;
            pool.expendable = expendable;

            if (poolManager.cachedPools != null && !poolManager.cachedPools.Contains(pool))
                poolManager.cachedPools.Add(pool);
            if (poolManager.cachedNamePoolPairs != null)
                poolManager.cachedNamePoolPairs[prefab.name] = pool;
            if (sync && poolManager.cachedSyncIdPoolPairs != null)
                poolManager.cachedSyncIdPoolPairs[syncID] = pool;

            // 回读核对：static 池表 + syncID 字典 + 池字段三者与实际注册一致。
            Pool reread = Pool.GetPoolFromPrefabAsset(prefab);
            if (reread == null)
            {
                // 兼容 CreatePoolFor 只建池未挂 static 表的实现：调一次幂等的原生 Init 自愈
                // （2.1.0 反编译确认 Init 用 ContainsKey guard，重复调用不残留孤儿池）。
                try { pool.Init(prefab); } catch (Exception) { }
                reread = Pool.GetPoolFromPrefabAsset(prefab);
            }
            if (reread == null || PointerOf(reread.prefab) != PointerOf(prefab))
            {
                LogError("pool readback failed after registration for " + prefab.name);
                return false;
            }
            if (sync)
            {
                if (poolManager.cachedSyncIdPoolPairs == null
                    || !poolManager.cachedSyncIdPoolPairs.ContainsKey(syncID)
                    || PointerOf(poolManager.cachedSyncIdPoolPairs[syncID].prefab) != PointerOf(prefab)
                    || !reread.sync || reread.syncID != syncID)
                {
                    LogError("pool sync readback mismatch for " + prefab.name + " (syncID " + syncID + ")");
                    if (created) SafeDestroy(pool);
                    return false;
                }
            }
            if (created)
                Log("registered skill pool " + prefab.name + " (syncID=" + syncID + ")");
            return true;
        }
        catch (Exception e)
        {
            LogError("pool registration threw for " + prefab.name + ": " + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    private static void SafeDestroy(Pool pool)
    {
        try
        {
            if (pool != null && pool.gameObject != null) UnityEngine.Object.Destroy(pool.gameObject);
        }
        catch (Exception) { }
    }

    private static void Fail(PoolState state, int now, string reason)
    {
        state.Ready = false;
        state.Failure = reason;
        state.NextRetryTick = unchecked(now + RetryCooldownMs);
        if (!state.LoggedFailure)
        {
            state.LoggedFailure = true;
            LogError("skill pool not ready: " + reason);
        }
    }

    // ------------------------------------------------------------------ infrastructure

    private static bool GreeceContext(out BiomeSpecificAssets assets)
    {
        assets = null;
        try
        {
            if (!ModConfig.Enabled.Value) return false;
            BiomeHolder holder = BiomeHolder.Inst;
            if (holder == null || holder.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return false;
            assets = holder.curBiomeAssets;
            return assets != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void EnsureStates()
    {
        if (_steedStates != null && _poolStates != null) return;
        int definitionCount = CrossWorldMountCatalog.Definitions.Length;
        int aliasCount = 0;
        for (int i = 0; i < definitionCount; i++)
            aliasCount += CrossWorldMountCatalog.Definitions[i].Aliases.Length;

        var steeds = new SteedState[definitionCount + aliasCount];
        var pools = new PoolState[definitionCount];
        int cursor = 0;
        for (int i = 0; i < definitionCount; i++)
        {
            CrossWorldMountDefinition definition = CrossWorldMountCatalog.Definitions[i];
            steeds[cursor++] = new SteedState
            {
                TypeId = definition.SteedTypeId,
                Path = definition.SteedPrefabPath,
                Label = definition.Id,
                VariantOverride = definition.VariantOverride,
                Definition = definition,
            };
            pools[i] = new PoolState();
        }
        for (int i = 0; i < definitionCount; i++)
        {
            CrossWorldMountDefinition definition = CrossWorldMountCatalog.Definitions[i];
            for (int a = 0; a < definition.Aliases.Length; a++)
            {
                CrossWorldMountSteedAlias alias = definition.Aliases[a];
                steeds[cursor++] = new SteedState
                {
                    TypeId = alias.SteedTypeId,
                    Path = alias.PrefabPath,
                    Label = definition.Id + ".p2",
                    VariantOverride = definition.VariantOverride,
                    Definition = definition,
                };
            }
        }
        _steedStates = steeds;
        _poolStates = pools;
    }

    private static int IndexOfDefinition(CrossWorldMountDefinition definition)
    {
        for (int i = 0; i < CrossWorldMountCatalog.Definitions.Length; i++)
            if (ReferenceEquals(CrossWorldMountCatalog.Definitions[i], definition)) return i;
        return -1;
    }

    private static ulong PointerOf(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase value)
    {
        try { return value != null ? (ulong)value.Pointer.ToInt64() : 0UL; }
        catch (Exception) { return 0UL; }
    }

    /// <summary>
    /// landData 容器指针（真实 <c>Il2CppSystem.Collections.Generic.List&lt;T&gt;</c> 继承
    /// Il2CppObjectBase 有 .Pointer；离线桩提供同名 Pointer 属性保持同一访问面）。
    /// </summary>
    internal static ulong PointerOfList(Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> value)
    {
        try { return value != null ? (ulong)value.Pointer.ToInt64() : 0UL; }
        catch (Exception) { return 0UL; }
    }

    private static readonly HashSet<string> LoggedKeys = new HashSet<string>(StringComparer.Ordinal);

    private static void Log(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(LogPrefix + " " + message); }
        catch (Exception) { }
    }

    private static void LogOnce(string key, string message)
    {
        try
        {
            if (!LoggedKeys.Add(key)) return;
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(LogPrefix + " " + message);
        }
        catch (Exception) { }
    }

    private static void LogError(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogError(LogPrefix + " " + message); }
        catch (Exception) { }
    }
}
