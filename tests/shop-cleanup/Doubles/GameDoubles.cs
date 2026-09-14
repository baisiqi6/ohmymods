// GameDoubles.cs — game-assembly surface + the deterministic world/ destruction simulation.
//
// Only the members the compiled production files (PatchRoles_Castle, ShopCleanupQueue) touch
// are modelled. The important fidelity points:
//
// - ShopPlanner.RemoveShop mirrors the VERIFIED 2.4 native contract (root + independent
//   reviewer, 0x762ef0 / 272 bytes / unique slot): remove from _shops, read ShopTag, and only
//   clear _placedShops[tag.type] when that slot still holds the same object (Unity ==).
//   The double-deregistration cases depend on this native identity gate.
// - PayableShop.OnDestroy mirrors 0x67a080 -> 0x67a19b calling ShopPlanner.RemoveShop, and it
//   runs from the deferred destruction flush (Unity frame end).
// - Object.Destroy is deferred; Object.DestroyImmediate does not exist in this project, so the
//   production cleanup cannot call the illegal immediate API without a compile error.
using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

public enum Side { Left = -1, Right = 1 }

public enum TechnologyAge { None, Wood, Stone, Iron }

public class Castle : Behaviour
{
    public enum Level { Castle1, Castle2, Castle3, Castle4, Castle5, Castle6 }

    public Level level = Level.Castle5;
    public int CatchupCalls, ReQueueCalls;

    public void CatchupToLevel() { CatchupCalls++; }
    public void ReQueueAllBuildings() { ReQueueCalls++; }
}

public class Kingdom : Component
{
    public Castle castle;
}

public class World : Component
{
    /// <summary>2.4: Transform World.gameLayer (the current level's game layer root).</summary>
    public Transform gameLayer;
}

public class Managers : Component
{
    public static Managers Inst;
    public World world;
    public Kingdom kingdom;
    public PoolManager pools;
    public Holder holder;
    public ShopPlanner shopPlanner;
}

public class Character : Behaviour { }

public class Holder : Component
{
    public Il2CppSystem.Collections.Generic.Dictionary<string, Character> tagCharacterPairs =
        new Il2CppSystem.Collections.Generic.Dictionary<string, Character>();

    public Character GetCharacterByTag(string tag)
        => tagCharacterPairs.TryGetValue(tag, out Character character) ? character : null;
}

public class Pool : Behaviour
{
    public GameObject prefab;
    public bool sync;
    public short syncID;

    private static readonly List<Pool> Registered = new List<Pool>();

    public static void Register(Pool pool) { Registered.Add(pool); }
    public static void ResetRegistry() { Registered.Clear(); }

    public static Pool GetPoolFromPrefabAsset(GameObject prefab)
    {
        if (prefab == null) return null;
        for (int i = 0; i < Registered.Count; i++)
            if (Registered[i].prefab == prefab) return Registered[i];
        return null;
    }
}

public class PoolManager : Behaviour
{
    public Il2CppSystem.Collections.Generic.List<Pool> cachedPools =
        new Il2CppSystem.Collections.Generic.List<Pool>();
    public Il2CppSystem.Collections.Generic.Dictionary<string, Pool> cachedNamePoolPairs =
        new Il2CppSystem.Collections.Generic.Dictionary<string, Pool>();
    public Il2CppSystem.Collections.Generic.Dictionary<short, Pool> cachedSyncIdPoolPairs =
        new Il2CppSystem.Collections.Generic.Dictionary<short, Pool>();

    public int CreatePoolCalls;

    public Pool CreatePoolFor(GameObject prefab)
    {
        CreatePoolCalls++;
        var pool = new Pool { prefab = prefab };
        Pool.Register(pool);
        return pool;
    }
}

public class Droppable : Behaviour { }

public class DroppableTool : Droppable { }

public class Payable : Component { }

/// <summary>
/// Shop component. <see cref="NativeOnDestroy"/> models the verified 2.4 native
/// PayableShop.OnDestroy (0x67a080): unbind the building callback, then call
/// ShopPlanner.RemoveShop(gameObject) at 0x67a19b. It is invoked by the frame-end
/// destruction flush, never by the production cleanup queue.
/// </summary>
public class PayableShop : Payable
{
    /// <summary>Real enum values: _placedShops is indexed by (int)ShopType.</summary>
    public enum ShopType
    {
        Bow = 0,
        Hammer = 1,
        Scythe = 2,
        WorkshopLeft = 3,
        Pike_OLDHANDLE = 4,
        Forge = 5,
        WorkshopRight = 6,
        NinjaLeft = 7,
        NinjaRight = 8,
        PikeLeft = 9,
        PikeRight = 10,
        ChangeRuler = 11,
        ShieldShopLeft = 12,
        ShieldShopRight = 13,
        ChangeItem = 14,
        Total = 15,
    }

    public Droppable itemPrefab;
    public int OnDestroyCalls;

    /// <summary>One-shot fault: OnDestroy throws before reaching RemoveShop (models the native
    /// _workableBuilding dereference failing first).</summary>
    public bool ThrowBeforeDeregister;

    public void NativeOnDestroy()
    {
        OnDestroyCalls++;
        if (ThrowBeforeDeregister)
        {
            ThrowBeforeDeregister = false;
            throw new InvalidOperationException("native PayableShop.OnDestroy fault before RemoveShop");
        }

        Managers managers = Managers.Inst;
        ShopPlanner planner = managers != null ? managers.shopPlanner : null;
        if (planner != null) planner.RemoveShop(gameObject);
    }
}

public class ShopTag : Component
{
    public PayableShop.ShopType type;
}

/// <summary>
/// ShopPlanner surface used by the Greece berserker/ninja shop code. RemoveShop/AddShop follow
/// the verified 2.4 native flow; queued placement is modelled as bookkeeping the tests assert on.
/// </summary>
public class ShopPlanner : Behaviour
{
    public const int SlotCount = 15;

    public Il2CppSystem.Collections.Generic.Dictionary<PayableShop.ShopType, GameObject> shopTypePrefabPairs =
        new Il2CppSystem.Collections.Generic.Dictionary<PayableShop.ShopType, GameObject>();
    public Il2CppReferenceArray<Payable> raisingShops = new Il2CppReferenceArray<Payable>(SlotCount);
    public Il2CppReferenceArray<GameObject> _placedShops = new Il2CppReferenceArray<GameObject>(SlotCount);
    public Il2CppSystem.Collections.Generic.List<GameObject> _shops =
        new Il2CppSystem.Collections.Generic.List<GameObject>();
    public Il2CppSystem.Collections.Generic.List<ShopPlaceQueueData> _queuedShopPlacements =
        new Il2CppSystem.Collections.Generic.List<ShopPlaceQueueData>();

    public sealed class ShopPlaceQueueData
    {
        public PayableShop.ShopType shopType;
        public Il2CppSystem.Nullable<Side> shopSide;
        public int numItems;
    }

    public int StartCalls, RemoveShopCalls, EffectiveDeregistrations, AddShopCalls, QueueCalls;
    public readonly List<PayableShop.ShopType> QueuedTypes = new List<PayableShop.ShopType>();
    public readonly Dictionary<PayableShop.ShopType, Side?> QueueSides = new Dictionary<PayableShop.ShopType, Side?>();

    /// <summary>Injection: RemoveShop throws BEFORE writing anything (attempt has no side effect).</summary>
    public Func<GameObject, Exception> RemoveShopFaultBeforeWrite;
    /// <summary>Injection: RemoveShop writes the deregistration and then throws.</summary>
    public Func<GameObject, Exception> RemoveShopFaultAfterWrite;

    /// <summary>Harmony patch target (ShopPlanner.Start).</summary>
    public void Start() { StartCalls++; }

    public bool HasPlacedShop(PayableShop.ShopType type) => HasPlacedShop(type, null);

    public bool HasPlacedShop(PayableShop.ShopType type, GameObject go)
    {
        int index = (int)type;
        if (index < 0 || index >= SlotCount) return false;
        return _placedShops[index] != null && (go == null || _placedShops[index] == go);
    }

    public bool IsPlacedOrQueued(PayableShop.ShopType type)
    {
        int index = (int)type;
        if (index < 0 || index >= SlotCount) return false;
        if (raisingShops[index] != null) return true;
        if (_placedShops[index] != null) return true;
        return QueuedTypes.Contains(type);
    }

    public void QueueNewShopForPlacement(PayableShop.ShopType type, Il2CppSystem.Nullable<Side> side)
    {
        QueueCalls++;
        if (!QueuedTypes.Contains(type)) QueuedTypes.Add(type);
        QueueSides[type] = side.HasValue ? (Side?)side.Value : null;
    }

    /// <summary>Native AddShop (PayloadShop.Awake): _shops.Add + SetPlacedShop(ShopTag.type, shop).</summary>
    public void AddShop(GameObject shop)
    {
        AddShopCalls++;
        if (shop == null) return;
        if (!_shops.Contains(shop)) _shops.Add(shop);

        ShopTag tag = shop.GetComponent<ShopTag>();
        if (tag == null) return;
        int index = (int)tag.type;
        if (index < 0 || index >= SlotCount) return;
        _placedShops[index] = shop;
    }

    /// <summary>
    /// Mirrors the actual 2.4 ShopPlanner.RemoveShop (0x762ef0, 272 bytes, one slot):
    /// _shops.Remove, then read ShopTag and clear _placedShops[tag.type] ONLY while that slot
    /// still holds this exact object (Unity ==). Otherwise it returns without touching the slot.
    /// </summary>
    public void RemoveShop(GameObject shop)
    {
        RemoveShopCalls++;
        if (shop == null) return;

        Func<GameObject, Exception> beforeWrite = RemoveShopFaultBeforeWrite;
        if (beforeWrite != null) throw beforeWrite(shop); // 完全没有副作用

        _shops.Remove(shop);

        ShopTag tag = shop.GetComponent<ShopTag>();
        if (tag != null)
        {
            int index = (int)tag.type;
            if (index >= 0 && index < SlotCount && _placedShops[index] == shop)
            {
                _placedShops[index] = null;
                EffectiveDeregistrations++;
            }
        }

        Func<GameObject, Exception> afterWrite = RemoveShopFaultAfterWrite;
        if (afterWrite != null) throw afterWrite(shop); // 副作用已经发生之后才抛
    }
}

public static class NetworkBigBoss
{
    public static bool HasWorldAuth = true;
    public static void Reset() { HasWorldAuth = true; }
}

public class BiomeHolder
{
    public static BiomeHolder Inst = new BiomeHolder();
    public const int GreeceBiomeIndex = 5;

    public int BiomeIndex = GreeceBiomeIndex;

    public static void Reset() { Inst = new BiomeHolder(); }
}

public static class Resources
{
    /// <summary>Only LoadAll&lt;DroppableTool&gt;("") is used by the compiled production code.</summary>
    public static Il2CppArrayBase<T> LoadAll<T>(string path) where T : UnityEngine.Object
    {
        if (typeof(T) == typeof(DroppableTool))
        {
            T[] items = new T[Sim.Tools.Count];
            for (int i = 0; i < Sim.Tools.Count; i++) items[i] = (T)(object)Sim.Tools[i];
            return new Il2CppArrayBase<T>(items);
        }
        return new Il2CppArrayBase<T>(0);
    }
}

/// <summary>
/// Deterministic stand-in for Unity's frame boundary, the current world, and the destruction
/// queue. Production code only ever sees the UnityEngine/game surface; the fixture drives frames
/// explicitly (Tick -> FlushDestruction -> Tick).
/// </summary>
public static class Sim
{
    public static int SceneHandle = 1;
    public static bool SceneValid = true;
    public static Scene CurrentScene => new Scene { handle = SceneHandle, valid = SceneValid };

    public static World World;
    public static ShopPlanner Planner;
    public static Castle Castle;

    public static readonly List<UnityEngine.Object> PendingDestroys = new List<UnityEngine.Object>();
    public static readonly List<Exception> OnDestroyFaults = new List<Exception>();
    public static readonly List<DroppableTool> Tools = new List<DroppableTool>();

    public static int DestroyCalls;
    /// <summary>One-shot fault: Object.Destroy throws and the object survives.</summary>
    public static Func<UnityEngine.Object, Exception> DestroyFault;

    public static void Reset()
    {
        // 上一个用例的对象随"旧世界卸载"一起消失：走真实生产路径把队列排空
        // （target == null → Complete），不做测试专用清理。
        KillAllObjects();
        Managers.Inst = null;
        DetachedManagers = null;
        KingdomEnhancedMod.ShopCleanupQueue.TickPendingCleanup();
        KingdomEnhancedMod.ShopCleanupQueue.TickPendingCleanup();

        World = null;
        Planner = null;
        Castle = null;
        SceneHandle = 1;
        SceneValid = true;
        PendingDestroys.Clear();
        OnDestroyFaults.Clear();
        Tools.Clear();
        DestroyCalls = 0;
        DestroyFault = null;
        UnityEngine.Time.unscaledTime = 0f;
        UnityEngine.Time.time = 0f;
        UnityEngine.Object.All.Clear();
        UnityEngine.GameObject.FindCalls = 0;
        UnityEngine.GameObject.SetActiveCalls = 0;
        UnityEngine.GameObject.SetActiveFault = null;
        UnityEngine.GameObject.SetActiveCallback = null;
        Pool.ResetRegistry();
        NetworkBigBoss.Reset();
        BiomeHolder.Reset();
        KingdomEnhancedMod.ModConfig.ResetConfig();
        KingdomEnhancedPlugin.ResetLog();
        KingdomEnhancedMod.PatchRoles_Ninja.ResetCounters();
        KingdomEnhancedMod.PatchDivine_GhostSquads.ResetCounters();
        KingdomEnhancedMod.PatchRoles_NorseSquad.ResetCounters();
    }

    /// <summary>Old world unloaded: every wrapper of the previous case becomes Unity fake-null.</summary>
    public static void KillAllObjects()
    {
        for (int i = 0; i < UnityEngine.Object.All.Count; i++) UnityEngine.Object.All[i].Alive = false;
        PendingDestroys.Clear();
    }

    /// <summary>Advance the clock the cleanup backoff reads (one call == one frame's worth of time).</summary>
    public static void Advance(float seconds)
    {
        UnityEngine.Time.unscaledTime += seconds;
        UnityEngine.Time.time += seconds;
    }

    /// <summary>Managers temporarily unreachable (loading/menu): the fixture can put them back.</summary>
    private static Managers DetachedManagers;

    public static void DetachManagers()
    {
        DetachedManagers = Managers.Inst;
        Managers.Inst = null;
    }

    public static void RestoreManagers()
    {
        Managers.Inst = DetachedManagers;
        DetachedManagers = null;
    }

    /// <summary>Mirrors the ShopPlanner.Start postfix that marks a planner ready.</summary>
    public static void MarkPlannerReady()
    {
        KingdomEnhancedMod.PatchRoles_Castle.RetryGreeceShopsAfterPlannerStart(Planner);
    }

    /// <summary>Builds a Greece world with a ready ShopPlanner (as ShopPlanner.Start would leave it).</summary>
    public static void Boot(Castle.Level castleLevel = Castle.Level.Castle5)
    {
        var managers = new Managers();
        Managers.Inst = managers;

        World = new World();
        World.gameLayer = new GameObject("GameLayer").transform;
        managers.world = World;

        Castle = new Castle { level = castleLevel };
        managers.kingdom = new Kingdom { castle = Castle };

        managers.pools = new PoolManager();
        managers.pools.gameObject = new GameObject("Pools");

        Planner = new ShopPlanner();
        Planner.gameObject = new GameObject("ShopPlanner");
        managers.shopPlanner = Planner;

        var holder = new Holder();
        holder.gameObject = new GameObject("Holder");
        managers.holder = holder;

        // Characters and tools already pooled: Ensure* early-returns instead of registering.
        string[] characterTags = { "Ninja", "Berserker", "BerserkerLeader", "Worker" };
        for (int i = 0; i < characterTags.Length; i++)
        {
            var characterGO = new GameObject("Char_" + characterTags[i]);
            var character = characterGO.AddComponent<Character>();
            holder.tagCharacterPairs.Add(characterTags[i], character);
            Pool.Register(new Pool { prefab = characterGO });
        }

        string[] toolNames = { "ToolNinja", "ToolBerserker" };
        for (int i = 0; i < toolNames.Length; i++)
        {
            var toolGO = new GameObject(toolNames[i]);
            Pool.Register(new Pool { prefab = toolGO });
            Tools.Add(toolGO.AddComponent<DroppableTool>());
        }

        // Ninja prefab mapping so the shared ensure path stays quiet.
        Planner.shopTypePrefabPairs.Add(PayableShop.ShopType.NinjaLeft, new GameObject("ShopNinjaLeft"));
        Planner.shopTypePrefabPairs.Add(PayableShop.ShopType.NinjaRight, new GameObject("ShopNinjaRight"));

        // Mirrors the ShopPlanner.Start postfix that marks the planner ready.
        KingdomEnhancedMod.PatchRoles_Castle.RetryGreeceShopsAfterPlannerStart(Planner);
    }

    /// <summary>World switch: a new scene handle (and layer); the old wrappers stay behind.</summary>
    public static void NewScene(bool newPlanner)
    {
        SceneHandle++;
        Managers managers = Managers.Inst;
        if (managers == null) return;

        var world = new World();
        world.gameLayer = new GameObject("GameLayer").transform;
        managers.world = world;
        World = world;

        if (!newPlanner) return;
        Planner = new ShopPlanner();
        Planner.gameObject = new GameObject("ShopPlanner");
        managers.shopPlanner = Planner;
        MarkPlannerReady();
    }

    /// <summary>
    /// Zero the planner bookkeeping so a case starts from a known slot/queue state (the boot
    /// path queues shops for empty slots exactly like the native planner would).
    /// </summary>
    public static void ClearPlacementBookkeeping()
    {
        if (Planner == null) return;
        Planner.QueuedTypes.Clear();
        Planner.QueueSides.Clear();
        Planner.QueueCalls = 0;
        Planner.RemoveShopCalls = 0;
        Planner.EffectiveDeregistrations = 0;
        Planner.AddShopCalls = 0;
    }

    internal static void EnqueueDestroy(UnityEngine.Object target)
    {
        DestroyCalls++;

        Func<UnityEngine.Object, Exception> fault = DestroyFault;
        if (fault != null) throw fault(target); // the object survives this attempt

        if (!PendingDestroys.Contains(target)) PendingDestroys.Add(target);
    }

    /// <summary>
    /// Unity frame end: run OnDestroy on every component of every queued object (oldest first)
    /// and turn the wrappers into Unity's fake-null.
    /// </summary>
    public static void FlushDestruction()
    {
        var batch = new List<UnityEngine.Object>(PendingDestroys);
        PendingDestroys.Clear();

        for (int i = 0; i < batch.Count; i++)
        {
            UnityEngine.Object target = batch[i];
            if (target == null || !target.Alive) continue;

            var go = target as GameObject;
            if (go == null)
            {
                NotifyDestroy(target);
                target.Alive = false;
                continue;
            }

            for (int c = 0; c < go.Components.Count; c++)
            {
                Component component = go.Components[c];
                if (!component.Alive) continue;
                NotifyDestroy(component);
                component.Alive = false;
            }
            NotifyDestroy(go);
            go.Alive = false;
        }
    }

    private static void NotifyDestroy(UnityEngine.Object target)
    {
        var shop = target as PayableShop;
        if (shop == null) return;
        try { shop.NativeOnDestroy(); }
        catch (Exception e) { OnDestroyFaults.Add(e); } // Unity logs OnDestroy exceptions and continues
    }
}
