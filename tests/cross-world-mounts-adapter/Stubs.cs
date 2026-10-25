using System;
using System.Collections.Generic;
using System.Linq;

// Adapter stubs for linking the production mount sources outside Unity.
// 只提供真实 interop 编译面 + 可观察行为（指针身份、Resources 缓存、池字典），
// 不模拟原生玩法；native gameplay 验收仍待实机。

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HarmonyPatch : Attribute
    {
        /// <summary>测试专用：记录目标声明（生产无此读取，仅用于接线断言）。</summary>
        public readonly Type Target;
        public readonly string Method;
        public HarmonyPatch(Type t, string n = null) { Target = t; Method = n; }
        public HarmonyPatch(Type t) { Target = t; }
    }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyFinalizer : Attribute { }
    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPriority : Attribute
    {
        public readonly int Value;
        public HarmonyPriority(int i) { Value = i; }
    }
    public static class Priority
    {
        public const int First = 800;
        public const int Normal = 400;
        public const int Last = 0;
    }
}

namespace Il2CppInterop.Runtime.InteropTypes
{
    public class Il2CppObjectBase
    {
        private static long _next;
        public IntPtr Pointer = (IntPtr)(++_next);
        public T TryCast<T>() where T : class => this as T;
    }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppArrayBase<T> : Il2CppObjectBase, IEnumerable<T>
    {
        protected T[] Data;
        public Il2CppArrayBase(T[] items) { Data = items ?? Array.Empty<T>(); }
        public int Length => Data.Length;
        public T this[int index]
        {
            get => Data[index];
            set => Data[index] = value;
        }
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Data).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => Data.GetEnumerator();
        public static implicit operator T[](Il2CppArrayBase<T> array) => array?.Data ?? Array.Empty<T>();
    }
    public class Il2CppReferenceArray<T> : Il2CppArrayBase<T>
    {
        public Il2CppReferenceArray(T[] items) : base(items) { }
        public Il2CppReferenceArray(int length) : base(new T[length]) { }
    }
    public class Il2CppStructArray<T> : Il2CppArrayBase<T>
    {
        public Il2CppStructArray() : base(Array.Empty<T>()) { }
        public Il2CppStructArray(T[] items) : base(items) { }
        public static implicit operator Il2CppStructArray<T>(T[] items) => new Il2CppStructArray<T>(items);
    }
}

namespace Il2CppSystem.Collections.Generic
{
    public class List<T> : System.Collections.Generic.List<T>
    {
        private static long _nextPointer;
        /// <summary>模拟 interop 容器身份（真实 List 继承 Il2CppObjectBase 的 .Pointer）。</summary>
        public IntPtr Pointer = (IntPtr)(++_nextPointer);
        public List() { }
        public List(int capacity) : base(capacity) { }
    }
    public class Dictionary<K, V> : System.Collections.Generic.Dictionary<K, V>
    {
        public Dictionary() { }
    }
}

namespace UnityEngine
{
    public class Object : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public string name;
        public static void DontDestroyOnLoad(Object o) { }
        public static void Destroy(Object o) { }
        public static T Instantiate<T>(T original, Transform parent) where T : Object
        {
            if (original is GameObject go)
            {
                var clone = new GameObject(go.name + "(Clone)");
                foreach (Object component in go.Components)
                {
                    if (component is LevelBlock block)
                    {
                        var copy = new LevelBlock { gameObject = clone, groupOne = block.groupOne, groupTwo = block.groupTwo };
                        clone.Components.Add(copy);
                    }
                }
                return clone as T;
            }
            return null;
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : class => gameObject != null ? gameObject.GetComponent<T>() : null;
    }

    public class GameObject : Object
    {
        public readonly List<Object> Components = new List<Object>();
        public readonly List<GameObject> Children = new List<GameObject>();
        public Transform transform;

        public GameObject(string n)
        {
            name = n;
            transform = new Transform { gameObject = this };
        }

        public void SetActive(bool value) { }

        public T GetComponent<T>() where T : class
        {
            for (int i = 0; i < Components.Count; i++)
                if (Components[i] is T typed && typed != null) return typed;
            return null;
        }

        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class
        {
            var found = new List<T>();
            Collect(this, found);
            return found.ToArray();
        }

        private static void Collect<T>(GameObject node, List<T> found) where T : class
        {
            for (int i = 0; i < node.Components.Count; i++)
                if (node.Components[i] is T typed && typed != null) found.Add(typed);
            for (int i = 0; i < node.Children.Count; i++) Collect(node.Children[i], found);
        }
    }

    public class Transform
    {
        public GameObject gameObject;
        public string name => gameObject.name;
        public Transform parent;
        public readonly List<Transform> children = new List<Transform>();
        public int childCount => children.Count;
        public Transform GetChild(int index) => children[index];
    }

    public static class Resources
    {
        public static readonly Dictionary<string, Object> Items = new Dictionary<string, Object>(StringComparer.OrdinalIgnoreCase);
        public static int Loads;

        public static T Load<T>(string path) where T : class
        {
            Loads++;
            return Items.TryGetValue(path, out Object value) ? value as T : null;
        }

        public static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<T> LoadAll<T>(string path) where T : class
        {
            Loads++;
            return new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<T>(Items.Values.OfType<T>().ToArray());
        }
    }
}

public enum LevelBlockGroup { }
public enum SteedType { }

public class LevelBlock : UnityEngine.Component
{
    public new string name => gameObject.name;
    public LevelBlockGroup groupOne, groupTwo;
    public void ComputeDimensions() { }
    public void BuildIndex() { }
    public int GetWidth() => 20;
    public void CloneInto(Level level, float posX) { }
}

public class Level : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase { }
public class LevelLayout { public void GetBlocks() { } }

/// <summary>测试专用：生产 Frame 记录的原生生成配置；adapter 走旧兼容 Open()，恒为 null。</summary>
public class LevelConfig : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase { }

/// <summary>测试专用：issue-200 fresh 票据的空实现（adapter 不接入 fresh 观察）。</summary>
public sealed class MountIslandFrameTicket { }

public class Steed : UnityEngine.Component
{
    public SteedType steedType;
    // 真实 Steed.steedAbilities 是运行时字段（序列化资源里为空）；组件扫描才是可靠路径。
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<SteedAbility> steedAbilities => null;
}

public class SteedSpawn : UnityEngine.Component
{
    /// <summary>测试专用：模拟原生 SpawnSteed 的最终生成（记录实际传入的 prefab）。</summary>
    public static Action<SteedSpawn, Steed> SpawnSteedHook;
    public Steed LastSpawned;
    /// <summary>actual Public Steed[]（offset 320）：Pay 动画分支直接取 [0] Instantiate（备用体）。</summary>
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Steed> steeds;
    /// <summary>actual 私有 steedPool（本修补不改它；测试用于断言未被触碰）。</summary>
    public Il2CppSystem.Collections.Generic.List<Steed> steedPool;
    /// <summary>测试专用：模拟 Pay 动画分支的备用体选择（原生取 steeds[0] Instantiate）。</summary>
    public Steed LastBackupPrefab;

    public void SpawnSteed(Steed steedPrefab)
    {
        LastSpawned = steedPrefab;
        SpawnSteedHook?.Invoke(this, steedPrefab);
    }

    /// <summary>测试专用：只复现原生 Pay 动画分支的备用体选择，不模拟付款/资格/状态。</summary>
    public void Pay()
    {
        LastBackupPrefab = steeds != null && steeds.Length > 0 ? steeds[0] : null;
    }
}

/// <summary>测试专用：生产覆盖路径调用 BiomeData.GetPrefabSwap（真实为静态泛型，miss 原样）。</summary>
public static class BiomeData
{
    public static Func<Steed, Steed> SteedSwap;
    public static T GetPrefabSwap<T>(T prefab)
    {
        if (prefab is Steed steed && SteedSwap != null) return (T)(object)SteedSwap(steed);
        return prefab;
    }
}

public class SteedAbility : UnityEngine.Component { }

public class SpitSteedAbility : SteedAbility
{
    public UnityEngine.GameObject _spitPrefab;
}

public class KelpieSteedAbility : SteedAbility
{
    public UnityEngine.GameObject summerAttackPrefab;
    public UnityEngine.GameObject winterAttackPrefab;
}

public class BiomeObjectPools : UnityEngine.Object
{
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Pool> biomeObjectPools;

    public BiomeObjectPools(string poolName, Pool[] pools)
    {
        name = poolName;
        biomeObjectPools = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Pool>(pools);
    }
}

public class BiomeSpecificAssets : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
{
    public Il2CppSystem.Collections.Generic.List<Steed> biomeSteeds = new Il2CppSystem.Collections.Generic.List<Steed>();
    public Il2CppSystem.Collections.Generic.Dictionary<SteedType, Steed> objectSteedTypePairs =
        new Il2CppSystem.Collections.Generic.Dictionary<SteedType, Steed>();
    public int InitializeAssetsCalls;

    public void InitializeAssets() { InitializeAssetsCalls++; }

    /// <summary>测试专用：模拟原生 BiomeData.GetPrefabSwap 的最终替换（默认恒等；返回不同实例即读回不同）。</summary>
    public static Func<Steed, Steed> PrefabSwap;

    /// <summary>测试专用：精确键缺失/Unity-null 时回退 Horse Regular 的累计次数（根因回归计数）。</summary>
    public static int SteedFallbacks;
    /// <summary>测试专用：精确键缺失/Unity-null 的 getter 读取累计次数（含无 Horse 可回退的容器缺失）。</summary>
    public static int SteedMissingKeyReads;

    public Steed GetSteedByType(SteedType type)
    {
        // 原生语义（native-stage1 @72c9d0）：只按精确键查 objectSteedTypePairs；缺键或 Unity-null
        // 时回退 type 8（Horse Regular）——不是 biomeSteeds 扫描；最终统一经过 prefab swap。
        Steed result = null;
        if (objectSteedTypePairs != null && objectSteedTypePairs.TryGetValue(type, out Steed pair) && pair != null)
            result = pair;
        else
        {
            SteedMissingKeyReads++;
            if (objectSteedTypePairs != null && objectSteedTypePairs.TryGetValue((SteedType)8, out Steed horse)
                && horse != null)
            {
                SteedFallbacks++;
                result = horse;
            }
        }
        return PrefabSwap != null ? PrefabSwap(result) : result;
    }
}

public class BiomeHolder
{
    public static BiomeHolder Inst = new BiomeHolder();
    public const int GreeceBiomeIndex = 5;
    public int BiomeIndex = GreeceBiomeIndex;
    public BiomeSpecificAssets curBiomeAssets = new BiomeSpecificAssets();
}

public static class NetworkBigBoss { public static bool IsOnline; }

/// <summary>诚实复现原生 ABI：ChallengeData 的 id/isSeasonalEvent 为公有字段（offset 0x18/0x38）。</summary>
public class ChallengeData
{
    public int id;
    public bool isSeasonalEvent;
}

/// <summary>诚实复现原生 ABI：Inst 为静态入口；ChallengeDataForID(int) 为**实例**方法（@0xa66af0）。</summary>
public class ChallengeHolder
{
    public static ChallengeHolder Inst;
    public Func<int, ChallengeData> DataProvider;
    public ChallengeData ChallengeDataForID(int id) => DataProvider != null ? DataProvider(id) : null;
}

/// <summary>诚实复现原生 ABI：IsEventActive(ChallengeData) 为**静态**方法（@0x8c1490，x64 rdi=data）。</summary>
public static class SeasonalEventManager
{
    public static Func<ChallengeData, bool> IsEventActiveProvider;
    public static bool IsEventActive(ChallengeData data)
        => IsEventActiveProvider != null && IsEventActiveProvider(data);
}

public class GlobalSaveData
{
    public static GlobalSaveData loaded = new GlobalSaveData();
    public bool InChallenge;
    /// <summary>actual GlobalSaveData.currentCampaign / currentChallenge（Int32）。</summary>
    public int currentCampaign;
    public int currentChallenge;
    /// <summary>测试专用：模拟原生 campaigns 列表中索引到的当前对象。</summary>
    public CampaignSaveData CurrentCampaignProvider;

    /// <summary>
    /// 诚实复现 actual @0x8a1400 的可观察语义：currentChallenge!=0 → FindCampaignForChallengeId
    /// （本候选 scope 已排除挑战，桩返回 null）；currentCampaign&lt;0 → null（菜单/无选中不回退）；
    /// 否则返回当前战役对象。
    /// </summary>
    public CampaignSaveData GetCurrentCampaign()
    {
        if (currentChallenge != 0) return null;
        if (currentCampaign < 0) return null;
        return CurrentCampaignProvider;
    }
}

public class IslandSaveData
{
    public static bool isSavingGame;
    public class ObjectData { public string prefabPath, name, hierarchyPath; }
    public Il2CppSystem.Collections.Generic.List<ObjectData> objects = new Il2CppSystem.Collections.Generic.List<ObjectData>();
}

public class CampaignSaveData : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
{
    public static CampaignSaveData current;
    public int currentLand;
    /// <summary>actual CampaignSaveData.reign（Int32 属性）。</summary>
    public int reign;
    private ReignInfo _currentReign = new ReignInfo();
    /// <summary>
    /// actual get_currentReign 每次 il2cpp_value_box 新包装（见 review/probes/interop/run.log），
    /// 但共享底层 landData 引用；桩保持同一可观察语义，防止身份判定回归到包装指针。
    /// </summary>
    public ReignInfo currentReign
    {
        get => new ReignInfo { landData = _currentReign.landData };
        set => _currentReign = value;
    }
    public Il2CppSystem.Collections.Generic.List<int> visitedIslands = new Il2CppSystem.Collections.Generic.List<int>();
    /// <summary>raw _islands 槽（issue-200 receipt 读取面）；按 land 索引。</summary>
    public Il2CppSystem.Collections.Generic.List<IslandSaveData> _islands =
        new Il2CppSystem.Collections.Generic.List<IslandSaveData>();
    /// <summary>测试专用：当前岛的 objects 记录容器（映射到 _islands[currentLand]）。</summary>
    public IslandSaveData island
    {
        get
        {
            int index = currentLand >= 0 ? currentLand : 0;
            while (_islands.Count <= index) _islands.Add(new IslandSaveData());
            return _islands[index];
        }
    }
    public IslandSaveData GetIsland(int index) => island;

    public class ReignInfo : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public Il2CppSystem.Collections.Generic.List<LandMapData> landData = new Il2CppSystem.Collections.Generic.List<LandMapData>();
    }

    public class LandMapData
    {
        public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<SteedType> steedSpawns =
            Array.Empty<SteedType>();
        public double lastPlayedTimeDays;
    }
}

public class Managers
{
    public static Managers Inst;
    public Game game;
    public PoolManager pools;
    public class Game { public int currentLand; }
}

public class Pool : UnityEngine.Component
{
    public static readonly Dictionary<UnityEngine.GameObject, Pool> PoolsByPrefab =
        new Dictionary<UnityEngine.GameObject, Pool>();
    public static int CreatePoolForCalls;
    public static int InitCalls;

    public UnityEngine.GameObject prefab;
    public int preload;
    public bool sync;
    public short syncID;
    public int capacity;
    public bool expendable;

    public static Pool GetPoolFromPrefabAsset(UnityEngine.GameObject prefabAsset)
    {
        if (prefabAsset == null) return null;
        return PoolsByPrefab.TryGetValue(prefabAsset, out Pool pool) ? pool : null;
    }

    public void Init(UnityEngine.GameObject newPrefab)
    {
        InitCalls++;
        prefab = newPrefab;
        PoolsByPrefab[newPrefab] = this;
    }
}

public class PoolManager : UnityEngine.Component
{
    public Il2CppSystem.Collections.Generic.List<Pool> cachedPools = new Il2CppSystem.Collections.Generic.List<Pool>();
    public Il2CppSystem.Collections.Generic.Dictionary<string, Pool> cachedNamePoolPairs =
        new Il2CppSystem.Collections.Generic.Dictionary<string, Pool>();
    public Il2CppSystem.Collections.Generic.Dictionary<int, Pool> cachedSyncIdPoolPairs =
        new Il2CppSystem.Collections.Generic.Dictionary<int, Pool>();

    public static bool CreatePoolForSkipsInit;   // 场景开关：模拟"只建池不挂 static 表"的实现
    public Pool CreatePoolFor(UnityEngine.GameObject prefab)
    {
        Pool.CreatePoolForCalls++;
        UnityEngine.GameObject host = new UnityEngine.GameObject("Pool " + prefab.name);
        Pool pool = new Pool { gameObject = host, prefab = prefab };
        host.Components.Add(pool);
        if (!CreatePoolForSkipsInit) pool.Init(prefab);  // 默认与原生一致：创建即注册 static 池表
        return pool;
    }

    public void InitPools() { }
}

namespace KingdomEnhancedMod
{
    public class Entry { public bool Value; public Entry(bool value) { Value = value; } }
    public static class ModConfig
    {
        public static Entry Enabled = new Entry(true);
        public static Entry CrossWorldMountsEnabled = new Entry(true);
    }
    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
        public Logger LogSource = new Logger();
    }
    public class Logger
    {
        public void LogInfo(string message) => Console.WriteLine("INFO  " + message);
        public void LogError(string message) => Console.WriteLine("ERROR " + message);
    }

    // ------------------------------------------------------------------ issue-200 编译粘连（不参与本任务断言）
    // Adapter 只 link 生产 4 源；Runtime/Data 对 issue-200 新类型的引用在此提供最小真实语义，
    // 让 dependency/pool 契约可编译运行。fresh ticket 恒 null（旧兼容 Open 路径），新授予门关闭。

    internal static class MountIslandSplitPolicy
    {
        internal const int PrimaryLand = 11;
        internal const int SecondaryLand = 13;
        internal static bool IsExtensionLand(int land) => land == PrimaryLand || land == SecondaryLand;
        /// <summary>adapter 只在原生岛（2/4/6）跑契约；真实映射目标 11/13 对这些岛等同 -1。</summary>
        internal static int GetTargetLand(string definitionId) => -1;
    }

    internal static class ExtensionIslandPlan
    {
        /// <summary>仅编译/旧 runtime 路径；dependency/pool 契约不经附加岛接缝。</summary>
        internal static int[] DistributeSeams(int innerSeamCount, int count)
        {
            var seams = new int[count > 0 ? count : 0];
            if (count <= 0 || innerSeamCount <= 0) return seams;
            for (int i = 0; i < count; i++)
                seams[i] = 1 + (int)((long)(i + 1) * innerSeamCount / (count + 1));
            return seams;
        }
    }

    internal readonly struct MountIslandCurrentTransitionTime
    {
        internal bool IsCurrent => false;
    }

    internal static class ExtensionIslandRuntime
    {
        internal static bool TryResolveRawOwner(out GlobalSaveData global, out int selected,
            out CampaignSaveData owner)
        {
            global = GlobalSaveData.loaded;
            owner = null;
            selected = -1;
            if (global == null) return false;
            selected = global.currentCampaign;
            if (selected < 0) return false;
            owner = global.CurrentCampaignProvider;
            return owner != null;
        }

        internal static bool TryEnsureExtensionLandDataSlots(CampaignSaveData campaign, int land)
        {
            if (campaign == null || !MountIslandSplitPolicy.IsExtensionLand(land)) return true;
            if (!TryResolveRawOwner(out _, out _, out CampaignSaveData owner)
                || !ReferenceEquals(owner, campaign) || campaign.currentLand != land) return false;
            CampaignSaveData.ReignInfo reign = campaign.currentReign;
            if (reign == null || reign.landData == null) return false;
            while (reign.landData.Count <= land) reign.landData.Add(new CampaignSaveData.LandMapData());
            return true;
        }
    }

    internal static class MountIslandFreshObservation
    {
        internal static MountIslandFrameTicket ClaimForFrame(CrossWorldMountRuntime.Frame frame) => null;
        internal static void OnFrameClosed(CrossWorldMountRuntime.Frame frame) { }
        internal static bool BeginCommitBatch(CrossWorldMountRuntime.Frame frame, out string reason)
        { reason = "adapter glue: no fresh ticket"; return true; }
        internal static bool IsCommitBindingAuthorized(CrossWorldMountRuntime.Frame frame, int land, out string reason)
        { reason = "adapter glue: no fresh ticket"; return false; }
        internal static bool EndCommitBatch(CrossWorldMountRuntime.Frame frame, out string reason)
        { reason = string.Empty; return true; }
        internal static bool IsPlacementAuthorized(CrossWorldMountRuntime.Frame frame, Level level, out string reason)
        { reason = "adapter glue: no fresh ticket"; return false; }
        internal static bool IsGrantAuthorized(CrossWorldMountRuntime.Frame frame, int land, out string reason)
        { reason = "adapter glue: no fresh ticket"; return false; }
        internal static bool TryGetCurrentTransitionTime(CrossWorldMountRuntime.Frame frame, int land,
            out MountIslandCurrentTransitionTime time)
        { time = default; return false; }
    }

    internal struct MountIslandBatchGrant
    {
        internal string DefinitionId;
        internal int Land;
        internal int SteedTypeId;
        internal bool Authorized;
        internal bool Placed;
    }

    internal struct MountIslandMarkerWrite
    {
        internal string DefinitionId;
        internal int Land;
        internal int SteedTypeId;
    }

    internal sealed class MountIslandCommitSession
    {
        private readonly bool _batchVerified;
        internal MountIslandCommitSession(bool batchVerified, string batchReason)
        { _batchVerified = batchVerified; BatchReason = batchReason ?? string.Empty; }
        internal bool BatchVerified => _batchVerified;
        internal string BatchReason { get; }

        internal bool PlanGrant(MountIslandBatchGrant grant, bool bindingOk, int frameLand,
            out MountIslandMarkerWrite write)
        {
            write = default;
            if (grant.Land != frameLand || !grant.Placed) return false;
            if (grant.Authorized && (!_batchVerified || !bindingOk)) return false;
            write = new MountIslandMarkerWrite
            { DefinitionId = grant.DefinitionId, Land = grant.Land, SteedTypeId = grant.SteedTypeId };
            return true;
        }
    }

    internal static class MountIslandGrantFacts
    {
        internal static bool AllowsGrantPublish(bool authorityCurrent, bool ownerCurrent)
            => authorityCurrent && ownerCurrent;
    }

    internal sealed class MountIslandGrantAuthority
    {
        internal sealed class Snapshot
        {
            private readonly ulong _campaignPointer;
            private readonly int _reignIndex;
            private readonly ulong _landDataPointer;
            private readonly Dictionary<string, bool> _receipts;

            internal Snapshot(ulong campaignPointer, int reignIndex, ulong landDataPointer,
                Dictionary<string, bool> receipts)
            {
                _campaignPointer = campaignPointer;
                _reignIndex = reignIndex;
                _landDataPointer = landDataPointer;
                _receipts = receipts;
            }

            internal bool ReceiptInCampaign(CrossWorldMountDefinition definition)
                => definition != null && _receipts.TryGetValue(definition.Id, out bool found) && found;

            internal bool MatchesCurrent(CampaignSaveData campaign)
                => MatchesOwner(campaign, _reignIndex, _landDataPointer);

            internal bool MatchesOwner(CampaignSaveData campaign, int reignIndex, ulong landDataPointer)
            {
                if (campaign == null || reignIndex < 0 || reignIndex != _reignIndex) return false;
                if (landDataPointer != _landDataPointer) return false;
                if ((ulong)campaign.Pointer.ToInt64() != _campaignPointer) return false;
                if (!ExtensionIslandRuntime.TryResolveRawOwner(out _, out _, out CampaignSaveData owner)
                    || !ReferenceEquals(owner, campaign)) return false;
                if (!ReferenceEquals(CampaignSaveData.current, campaign)) return false;
                CampaignSaveData.ReignInfo reign = campaign.currentReign;
                if (reign == null || reign.landData == null) return false;
                return CrossWorldMountDependencies.PointerOfList(reign.landData) == _landDataPointer;
            }
        }

        internal static bool TryCapture(CampaignSaveData campaign, CampaignSaveData.ReignInfo reign,
            Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData,
            out Snapshot snapshot)
        {
            snapshot = null;
            if (campaign == null || reign == null || landData == null) return false;
            Il2CppSystem.Collections.Generic.List<IslandSaveData> islands = campaign._islands;
            if (islands == null || !ExtensionIslandRuntime.TryResolveRawOwner(out _, out _, out CampaignSaveData owner))
                return false;
            if (!ReferenceEquals(owner, campaign) || campaign.currentLand < 0) return false;
            var receipts = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (int d = 0; d < CrossWorldMountCatalog.Definitions.Length; d++)
                receipts[CrossWorldMountCatalog.Definitions[d].Id] = false;
            for (int i = 0; i < islands.Count; i++)
            {
                IslandSaveData island = islands[i];
                if (island == null || island.objects == null) continue;
                for (int j = 0; j < island.objects.Count; j++)
                {
                    IslandSaveData.ObjectData record = island.objects[j];
                    if (record == null) continue;
                    for (int d = 0; d < CrossWorldMountCatalog.Definitions.Length; d++)
                    {
                        CrossWorldMountDefinition definition = CrossWorldMountCatalog.Definitions[d];
                        if (receipts[definition.Id]) continue;
                        if (definition.ReceiptMatches(record.prefabPath)
                            || definition.ReceiptMatches(record.name)
                            || definition.ReceiptMatches(record.hierarchyPath))
                            receipts[definition.Id] = true;
                    }
                }
            }
            snapshot = new Snapshot((ulong)campaign.Pointer.ToInt64(), campaign.reign,
                CrossWorldMountDependencies.PointerOfList(landData), receipts);
            return true;
        }

        internal static bool TryReadIslandReceipt(CampaignSaveData campaign, int land,
            CrossWorldMountDefinition definition, out bool found)
        {
            found = false;
            try
            {
                if (campaign == null || definition == null || land < 0) return false;
                Il2CppSystem.Collections.Generic.List<IslandSaveData> islands = campaign._islands;
                if (islands == null) return false;
                if (land >= islands.Count) return true;
                IslandSaveData island = islands[land];
                if (island == null || island.objects == null) return true;
                for (int i = 0; i < island.objects.Count; i++)
                {
                    IslandSaveData.ObjectData record = island.objects[i];
                    if (record == null) continue;
                    if (definition.ReceiptMatches(record.prefabPath)
                        || definition.ReceiptMatches(record.name)
                        || definition.ReceiptMatches(record.hierarchyPath))
                    {
                        found = true;
                        return true;
                    }
                }
                return true;
            }
            catch (Exception) { return false; }
        }
    }

    internal static class MountIslandMarkerRuntime
    {
        internal static bool TryWriteMarker(CampaignSaveData campaign, int reignIndex,
            ulong landDataPointer, CrossWorldMountDefinition definition, int land, out string reason)
        {
            reason = string.Empty;
            try
            {
                if (campaign == null || reignIndex < 0 || landDataPointer == 0UL) { reason = "owner unknown"; return false; }
                if (!ExtensionIslandRuntime.TryResolveRawOwner(out _, out _, out CampaignSaveData owner)
                    || !ReferenceEquals(owner, campaign)) { reason = "owner changed"; return false; }
                CampaignSaveData current = CampaignSaveData.current;
                if (!ReferenceEquals(current, campaign) || current.reign != reignIndex)
                { reason = "owner/container changed"; return false; }
                CampaignSaveData.ReignInfo reign = current.currentReign;
                Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData =
                    reign != null ? reign.landData : null;
                if (landData == null
                    || CrossWorldMountDependencies.PointerOfList(landData) != landDataPointer)
                { reason = "owner/container changed"; return false; }
                if (definition == null || land < 0 || land >= landData.Count)
                { reason = "landData unavailable"; return false; }
                CampaignSaveData.LandMapData entry = landData[land];
                if (entry == null) { reason = "landData unavailable"; return false; }
                int[] next = CrossWorldMountPolicy.AppendMarker(
                    CrossWorldMountRuntime.ToIntArray(entry.steedSpawns), definition.SteedTypeId);
                var typed = new SteedType[next.Length];
                for (int i = 0; i < next.Length; i++) typed[i] = (SteedType)next[i];
                entry.steedSpawns = typed;
                landData[land] = entry;
                CrossWorldMountDependencies.InvalidateGrantCache();
                reason = "marker written";
                return true;
            }
            catch (Exception e) { reason = "marker write failed: " + e.Message; return false; }
        }
    }
}
