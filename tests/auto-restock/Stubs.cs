// Stubs.cs — fake game/native/config/coordinator/counts/ledger APIs for the
// AutoRestock behavior tests. These model only observable surface behavior,
// never the production algorithm under test.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
    }

    public struct Quaternion { public static Quaternion identity => default; }

    public static class Mathf
    {
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Abs(float v) => Math.Abs(v);
        public static float MoveTowards(float a,float b,float step) => Math.Abs(b-a)<=step ? b : a+Math.Sign(b-a)*step;
    }

    public static class Random
    {
        public static float Range(float a, float b) => (a + b) * 0.5f;
    }

    public static class Time
    {
        public static float time;
        public static float unscaledTime => time;
        public static float deltaTime;
        public static float timeScale = 1f;
    }

    public struct Scene { public int handle; }

    public class Component
    {
        public GameObject gameObject;
        public Transform transform;
    }

    public class Transform : Component
    {
        public IntPtr Pointer => gameObject?.Pointer ?? IntPtr.Zero;
        public Vector3 position;
        public Transform Parent;
            public bool IsChildOf(Transform t)
        {
            for (Transform c = this; c != null; c = c.Parent)
                if (ReferenceEquals(c, t)) return true;
            return false;
        }
    }

    public class GameObject
    {
        public readonly List<Component> Components = new();
        public T GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault();
        public string Name;
        public string Tag;
        public bool Active = true;
        public IntPtr Pointer;
        public int Id;
        public Scene Scene;
        public Transform Transform;
        public string name { get => Name; set => Name = value; }
        public bool CompareTag(string t) => Tag == t;
        public bool activeInHierarchy
        {
            get
            {
                for (Transform t = Transform; t != null; t = t.Parent)
                    if (!t.gameObject.Active) return false;
                return true;
            }
        }
        public Transform transform => Transform;
        public Scene scene => Scene;
        public int GetInstanceID() => Id;
        public void SetActive(bool v) => Active = v;
    }
}

// ---------------------------------------------------------------------------
// Game interop stubs (global namespace, as the game assembly would expose).
// ---------------------------------------------------------------------------
public enum CurrencyType { Coins, Jade }

public class Game
{
    public enum State { Menu, Playing, Paused }
    public State state = State.Playing;
}

public class World
{
    public Transform gameLayer;
    public IntPtr Pointer;
}

public class Player
{
    public IntPtr Pointer;
    public System.Collections.Generic.List<DroppableCurrency> _floatingCurrency = new();
    public Payable selectedPayable;
    public Payable _completingPayable;
}

// Payable models the native surface both shop and ammo targets expose to the
// service: price/currency/selection/network fields plus the single native
// TransactionComplete purchase path. PayableShop adds only the shop-only slots.
public class Payable : Component
{
    public bool enabled = true;
    private IntPtr _ptr;
    public IntPtr Pointer { get => gameObject?.Pointer ?? _ptr; set => _ptr = value; }
    public int Price = 1, priceIncrease;
    public CurrencyType Currency = CurrencyType.Coins;
    public bool forceBlockPayment;
    public bool selectedByP1, selectedByP2;
    public Player PlayerSelecting, interactingPlayer;
    public object parentHeaderRef = new object();
    public int _payRPCIndex = 1;
    public Func<Player, bool> CanPayF = _ => true;
    public int TransactionCompleteCalls;
    public float TransactionCompleteTime;
    public int BalanceAtPay = int.MinValue; // balance observed inside native purchase
    public int DeselectCalls;
    public bool DeselectThrows;
    public Action<Payable> TransactionCompleteF;

    public T TryCast<T>() where T : class => this as T;
    public virtual bool CanPay(Player p) => CanPayF(p);
    public Vector3 GetApproximateGameLayerPosition() => new Vector3(0f, 0f, 0f);
    public virtual void TransactionComplete()
    {
        TransactionCompleteCalls++;
        TransactionCompleteTime = Time.time;
        TransactionCompleteF?.Invoke(this);
    }
    public void Deselect(Player player)
    {
        DeselectCalls++;
        if (DeselectThrows) throw new InvalidOperationException("cleanup failed");
        selectedByP1 = selectedByP2 = false; PlayerSelecting = null;
    }
}

public class WorkableBuilding { public bool UnderConstruction; }

public class Droppable : Component
{
    public string tag;
}

public class DroppableCurrency : Droppable
{
    public bool FakeSet;
    public int MoveToCalls;
    public bool MoveToFails;
    public void MoveTo(Transform target, Vector3 offset, bool fake)
    {
        MoveToCalls++;
        FakeSet = true;
        if (MoveToFails) throw new InvalidOperationException("native MoveTo failed");
    }
    public void SetFake(bool f) { FakeSet = true; }
}

public class CurrencyManager
{
    public DroppableCurrency CoinPrefab;
    public T GetCurrencyTypePrefab<T>(CurrencyType type) where T : Droppable => CoinPrefab as T;
}

public class Managers
{
    public static Managers Inst;
    public World world;
    public Kingdom kingdom;
    public Game game;
    public Stats stats;
    public CurrencyManager currency;
}

public class Kingdom
{
    public bool isDaytime = true;
    public Player playerOne, playerTwo;
    public Func<Vector3, Player> CrownFinder = _ => new Player();
    public Player GetNearestPlayerWithCrown(Vector3 pos) => CrownFinder(pos);
}

public class Banker : Component { public int _stashedCoins; }

public class PayableShop : Payable
{
    public Droppable itemPrefab;
    public int maxItems = 99;
    public int _limitedNumItems = 99;
    public WorkableBuilding _workableBuilding = new WorkableBuilding();

    public int Items;                       // native stock
    public int GetItemCountCalls;
    public int GetItemCount() { GetItemCountCalls++; return Items; }
    public override void TransactionComplete()
    {
        base.TransactionComplete();
        Items++;
    }
}

public class PayableShopBaker : PayableShop { }

// Ammo targets: PayableWorkshopBarrel (role 6) and the FireTower's
// PayableComponent (role 7, same GameObject as its owner).
public interface IPayableComponentOwner { IntPtr Pointer { get; } }
public class FireTower : Component, IPayableComponentOwner
{
    public object _parentHeaderRef = new object(); public int _fireJarsActiveIndex = 1;
    public bool enabled = true;
    public IntPtr Pointer => gameObject?.Pointer ?? IntPtr.Zero;
    public int _maxFireJars = 9, _fireJarsActiveNum;
    public GameObject[] _fakeFireJars = Enumerable.Range(0,9).Select(_ => new GameObject()).ToArray();
}

public class PayableWorkshopBarrel : Payable { public RollableOilBarrel rollableBarrelPrefab = new() { gameObject = new GameObject() }; }
public class RollableOilBarrel : Component { }
public class BiomeData
{
    public static BiomeData Current = new();
    public static GameObject Swap;
    public T GetAssetSwapForThis<T>(T original) where T : class => Swap as T ?? original;
}

public class PayableComponent : Payable { public FireTower Owner; public IPayableComponentOwner _owner => Owner; }

public static class NetworkBigBoss
{
    public static bool HasWorldAuth = true;
    public static bool IsOnline = false;
    public static bool IsClientPresent = false;
    public static bool HasClientCaughtUp = true;
}

public static class Pool
{
    public static int SpawnCalls, DespawnCalls;
    public static bool SpawnThrows;
    public static bool MoveToFails;
    public static bool PoolAssetAvailable = true;
    public static readonly List<DroppableCurrency> Despawned = new();
    public static DroppableCurrency LastCoin;

    public static T Spawn<T>(T prefab, Vector3 pos, Quaternion rot, Transform parent = null, bool active = true) where T : Droppable
    {
        SpawnCalls++;
        if (SpawnThrows) throw new InvalidOperationException("pool exhausted");
        GameObject go = new GameObject { Name = "coin", Id = 900 + SpawnCalls, Pointer = (IntPtr)(0x500000 + SpawnCalls * 8) };
        Transform t = new Transform { position = pos, Parent = parent };
        t.gameObject = go;
        go.Transform = t;
        var coin = new DroppableCurrency { tag = "Coin", gameObject = go, MoveToFails = MoveToFails };
        coin.transform = t;
        LastCoin = coin;
        return (T)(Droppable)coin;
    }

    public static GameObject RequiredPoolPrefab;
    public static object GetPoolFromPrefabAsset(GameObject go) => PoolAssetAvailable && (RequiredPoolPrefab == null || RequiredPoolPrefab == go) ? new object() : null;
    public static void Despawn(GameObject go, bool destroy) { DespawnCalls++; }

    public static void ResetPool()
    {
        SpawnCalls = DespawnCalls = 0;
        SpawnThrows = MoveToFails = false;
        PoolAssetAvailable = true;
        RequiredPoolPrefab = null; BiomeData.Current = new(); BiomeData.Swap = null;
        Despawned.Clear();
        LastCoin = null;
    }
}

public class ManualLogSource
{
    public readonly List<string> Infos = new();
    public readonly List<string> Warnings = new();
    public void LogInfo(string m) => Infos.Add(m);
    public void LogWarning(string m) => Warnings.Add(m);
}

public class KingdomEnhancedPlugin
{
    public static KingdomEnhancedPlugin Instance = new();
    public ManualLogSource LogSource = new();
}

// ---------------------------------------------------------------------------
// Sibling mod-API stubs (namespace KingdomEnhancedMod).
// ---------------------------------------------------------------------------
namespace KingdomEnhancedMod
{
    // The bank scope itself is exercised by the direct bank regression suite.
    internal static class GreekBankScope
    {
        internal enum Scope { Unknown, Inactive, Active }
        internal static bool IsActive = true;
        internal static Scope Current() => IsActive ? Scope.Active : Scope.Inactive;
    }
    public class ConfigEntry<T> { public T Value; }

    public static class ModConfig
    {
        public static ConfigEntry<bool> MusketeerEnabled = new();
        public static ConfigEntry<bool> AutoRestockMusketeersEnabled = new();
        public static ConfigEntry<int> AutoRestockMusketeersTarget = new() { Value = 15 };
        public static ConfigEntry<bool> Enabled = new() { Value = true };
        public static ConfigEntry<bool> AutoRestockWorkersEnabled = new();
        public static ConfigEntry<bool> AutoRestockArchersEnabled = new();
        public static ConfigEntry<bool> AutoRestockNinjasEnabled = new();
        public static ConfigEntry<bool> AutoRestockBerserkersEnabled = new();
        public static ConfigEntry<bool> AutoRestockPeasantsEnabled = new();
        public static ConfigEntry<bool> AutoRestockFarmersEnabled = new();
        public static ConfigEntry<bool> AutoRestockCatapultBarrelsEnabled = new();
        public static ConfigEntry<bool> AutoRestockFireTowerAmmoEnabled = new();
        public static ConfigEntry<int> AutoRestockPeasantsTarget = new() { Value = 15 };
        public static ConfigEntry<int> AutoRestockWorkersTarget = new() { Value = 15 };
        public static ConfigEntry<int> AutoRestockArchersTarget = new() { Value = 15 };
        public static ConfigEntry<int> AutoRestockNinjasTarget = new() { Value = 15 };
        public static ConfigEntry<int> AutoRestockBerserkersTarget = new() { Value = 15 };
        public static ConfigEntry<int> AutoRestockFarmersTarget = new() { Value = 15 };
        public static ConfigEntry<int> AutoRestockCatapultBarrelsTarget = new() { Value = 15 };
        public static ConfigEntry<int> AutoRestockFireTowerAmmoTarget = new() { Value = 15 };

        public static void ResetConfig()
        {
            Enabled = new ConfigEntry<bool> { Value = true };
            MusketeerEnabled.Value = false; AutoRestockMusketeersEnabled.Value = false; AutoRestockMusketeersTarget.Value = 15;
            AutoRestockWorkersEnabled = new ConfigEntry<bool>();
            AutoRestockArchersEnabled = new ConfigEntry<bool>();
            AutoRestockNinjasEnabled = new ConfigEntry<bool>();
            AutoRestockBerserkersEnabled = new ConfigEntry<bool>();
            AutoRestockPeasantsEnabled = new ConfigEntry<bool>();
            AutoRestockFarmersEnabled = new ConfigEntry<bool>();
            AutoRestockCatapultBarrelsEnabled = new ConfigEntry<bool>();
            AutoRestockFireTowerAmmoEnabled = new ConfigEntry<bool>();
            AutoRestockPeasantsTarget = new ConfigEntry<int> { Value = 15 };
            AutoRestockWorkersTarget = new ConfigEntry<int> { Value = 15 };
            AutoRestockArchersTarget = new ConfigEntry<int> { Value = 15 };
            AutoRestockNinjasTarget = new ConfigEntry<int> { Value = 15 };
            AutoRestockBerserkersTarget = new ConfigEntry<int> { Value = 15 };
            AutoRestockFarmersTarget = new ConfigEntry<int> { Value = 15 };
            AutoRestockCatapultBarrelsTarget = new ConfigEntry<int> { Value = 15 };
            AutoRestockFireTowerAmmoTarget = new ConfigEntry<int> { Value = 15 };
        }
    }

    /// <summary>Fake counts cache. Tests mutate counts (which bump Version,
    /// mimicking counter events) and observe Refresh/Reset call patterns.
    /// Role 5 (Farmer) mirrors production identification: ShopScythe GO tag.</summary>
    internal static class AutoRestockCounts
    {
        internal static readonly string[] Tags = { "Hammer", "Bow", "Katana", "BerserkerTool", "Bread", "Scythe" };
        internal static readonly int[] Live = new int[6];
        internal static readonly int[] Stock = new int[6];
        internal static readonly int[] Incoming = new int[6];
        internal static bool RefreshResult = true;
        internal static long _version;
        internal static readonly List<PayableShop> _shops = new();
        internal static int RefreshCalls, ResetCalls, HookCalls;

        internal static long Version => _version;
        internal static List<PayableShop> Shops => _shops;
        internal static int LiveCount(int role) => (uint)role < Live.Length ? Live[role] : 0;
        internal static int StockCount(int role) => (uint)role < Stock.Length ? Stock[role] : 0;
        internal static int IncomingCount(int role) => role == 4 && (uint)role < Incoming.Length ? Incoming[role] : 0;
        internal static int ClassifyShop(PayableShop shop)
        {
            if(shop == null || shop.itemPrefab == null) return -1;
            for(int r=0;r<4;r++) if(shop.itemPrefab.tag == Tags[r]) return r;
            if(shop.gameObject.CompareTag("ShopScythe")) return 5;
            if(shop is PayableShopBaker) return 4;
            return -1;
        }
        internal static bool Refresh(Managers managers) { RefreshCalls++; return RefreshResult; }
        internal static void Reset() { ResetCalls++; }

        internal static void HookShopAddItem(PayableShop shop)
        {
            HookCalls++;
            int r=ClassifyShop(shop);
            if(r>=0) { Stock[r]++; _version++; }
        }

        internal static void SetLive(int role, int v) { Live[role] = v; _version++; }
        internal static void SetStock(int role, int v) { Stock[role] = v; _version++; }
        internal static void SetIncoming(int role, int v) { Incoming[role] = v; _version++; }

        internal static void ResetCounts()
        {
            Array.Clear(Live); Array.Clear(Stock); Array.Clear(Incoming);
            RefreshResult = true; _version = 0; _shops.Clear();
            RefreshCalls = ResetCalls = HookCalls = 0;
        }
    }

    /// <summary>Contract stub for the ammo worker (SiegeAmmoCounts). Cached Counts
    /// are only published by Refresh (mirroring the cached-target read model):
    /// native ammo changes made by a pay land in Native and become visible on the
    /// next successful refresh, which is why the final commit force-refreshes.</summary>
    internal static class SiegeAmmoCounts
    {
        internal static readonly int[] Native = new int[8];
        internal static readonly int[] Counts = new int[8];
        internal static readonly Dictionary<Payable,int> Local = new();
        internal static readonly List<Payable> Targets6 = new();
        internal static readonly List<Payable> Targets7 = new();
        internal static bool Ready = true, Unavailable = false, RefreshResult = true;
        internal static bool LastForce;
        internal static int RefreshCalls, ForceCalls, GetPayablesCalls;
        internal static long _version;

        internal static long Version => _version;
        internal static bool IsReady => Ready;
        internal static bool ClientUnavailable => Unavailable;
        internal static int Count(int role) => role == 6 || role == 7 ? Counts[role] : 0;
        internal static void GetPayables(int role, List<Payable> output)
        {
            GetPayablesCalls++;
            output.Clear(); // contract: clear/fill cached targets
            if(role == 6) output.AddRange(Targets6);
            else if(role == 7) output.AddRange(Targets7);
        }
        internal static int Classify(Payable payable)
        {
            if(payable is PayableWorkshopBarrel) return 6;
            if(payable is PayableComponent c && c.Owner != null
                && ReferenceEquals(c.Owner.gameObject, c.gameObject)) return 7;
            return -1;
        }
        internal static int CountAt(Payable payable)
            => payable != null && Local.TryGetValue(payable, out int v) ? v : 0;
        internal static bool Refresh(Managers managers, bool enabled, float now, bool force = false)
        {
            RefreshCalls++;
            LastForce = force;
            if(force) ForceCalls++;
            if(!enabled || !RefreshResult) return false;
            bool moved = false;
            for(int role=6;role<=7;role++)
                if(Counts[role] != Native[role]) { Counts[role] = Native[role]; moved = true; }
            if(moved) _version++;
            return true;
        }

        // Native-side writes (what the game itself would do) vs published cache.
        internal static void SetNative(int role, int v) { Native[role] = v; }
        internal static void SetCount(int role, int v) { Native[role] = v; Counts[role] = v; _version++; }
        internal static void SetLocal(Payable payable, int v) { Local[payable] = v; if (payable is PayableComponent c && c.Owner != null) c.Owner._fireJarsActiveNum = v; _version++; }

        internal static void ResetAmmo()
        {
            Array.Clear(Native); Array.Clear(Counts); Local.Clear();
            Targets6.Clear(); Targets7.Clear();
            Ready = true; Unavailable = false; RefreshResult = true; LastForce = false;
            RefreshCalls = ForceCalls = GetPayablesCalls = 0; _version = 0;
        }
    }

    /// <summary>Fake assistant coordinator; records reservations, releases
    /// (with the returnHome flag actually passed) and placements.</summary>
    internal static class BankAssistantCoordinator
    {
        internal sealed class Res { public int Index; public GameObject Actor; public bool Placed; }
        internal static readonly List<Res> Active = new();
        internal static readonly List<(int Index, bool ReturnHome)> Releases = new();
        internal static readonly List<float> ReserveTimes = new();
        internal static readonly Queue<GameObject> Actors = new();
        internal static int ReserveCalls, PlaceCalls;
        internal static bool FailReserve, FailPlace;
        internal static bool InvalidLease, FreezeMovement, MoveThrows;
        internal static Banker MainBanker;
        internal static int MoveCalls, HomeTeleports, MaxActive;
        internal static readonly Dictionary<GameObject, Vector3> Homes = new();
        internal static readonly List<(float Time,float X,float Speed)> Moves = new();

        internal static bool IsCurrentRestockBanker(Banker banker) => banker != null && ReferenceEquals(banker,MainBanker)
            && ModConfig.Enabled.Value && NetworkBigBoss.HasWorldAuth && Time.timeScale>0f
            && Managers.Inst?.game?.state == Game.State.Playing
            && banker.gameObject.activeInHierarchy && banker.transform.IsChildOf(Managers.Inst.world.gameLayer);

        internal static bool TryReserveForRestock(out int index, out GameObject actor)
        {
            ReserveCalls++;
            ReserveTimes.Add(Time.time);
            index = -1; actor = null;
            if (FailReserve || Actors.Count == 0 || Active.Count>=2 || !IsCurrentRestockBanker(MainBanker)) return false;
            if (NetworkBigBoss.IsOnline && !NetworkBigBoss.HasClientCaughtUp) return false;
            while (Actors.Count > 0 && !Actors.Peek().transform.IsChildOf(Managers.Inst.world.gameLayer)) Actors.Dequeue();
            if (Actors.Count == 0) return false;
            actor = Actors.Dequeue();
            if(!Homes.ContainsKey(actor)) Homes[actor]=actor.transform.position;
            index = ReserveCalls * 7 + Active.Count + 1;
            Active.Add(new Res { Index = index, Actor = actor });
            MaxActive=Math.Max(MaxActive,Active.Count);
            return true;
        }

        internal static bool RestockReservationValid(int index, GameObject actor)
        {
            if (actor == null || InvalidLease || !IsCurrentRestockBanker(MainBanker)
                || !actor.activeInHierarchy || !actor.transform.IsChildOf(Managers.Inst.world.gameLayer)
                || actor.scene.handle != Managers.Inst.world.gameLayer.gameObject.scene.handle
                || (NetworkBigBoss.IsOnline && !NetworkBigBoss.HasClientCaughtUp)) return false;
            foreach (Res r in Active)
                if (r.Index == index && ReferenceEquals(r.Actor, actor)) return true;
            return false;
        }

        internal static bool PlaceRestockAssistant(int index, GameObject actor, Vector3 position, float faceX)
        {
            PlaceCalls++;
            if (!RestockReservationValid(index, actor) || FailPlace
                || !float.IsFinite(position.x)) return false;
            actor.transform.position = position;
            foreach (Res r in Active) if (r.Index == index) r.Placed = true;
            return true;
        }

        internal static bool MoveRestockAssistant(int index,GameObject actor,float targetX,float speed,float delta,out bool arrived)
        {
            arrived=false; MoveCalls++;
            if(MoveThrows) throw new InvalidOperationException("move failed");
            if(!RestockReservationValid(index,actor) || !float.IsFinite(targetX) || !float.IsFinite(speed)
                || speed<=0 || !float.IsFinite(delta) || delta<0 || !float.IsFinite(speed*delta)) return false;
            var p=actor.transform.position;
            if(!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z)) return false;
            if(!FreezeMovement) p.x=Mathf.MoveTowards(p.x,targetX,speed*delta);
            actor.transform.position=p;
            arrived=Math.Abs(actor.transform.position.x-targetX)<=0.02f;
            Moves.Add((Time.time,p.x,speed));
            return RestockReservationValid(index,actor);
        }

        internal static void ReleaseRestockAssistant(int index, GameObject actor, bool returnHome)
        {
            Res r = Active.Find(x => x.Index == index && ReferenceEquals(x.Actor, actor));
            if (r != null)
            {
                if(returnHome && RestockReservationValid(index,actor)) { actor.transform.position=Homes[actor]; HomeTeleports++; }
                Active.Remove(r);
                Actors.Enqueue(actor); // freed assistant becomes idle again
            }
            Releases.Add((index, returnHome));
        }

        internal static int ReservedCount => Active.Count;

        internal static void ResetCoord()
        {
            Active.Clear(); Releases.Clear(); ReserveTimes.Clear(); Actors.Clear();
            ReserveCalls = PlaceCalls = 0; FailReserve = FailPlace = false;
            MoveCalls=HomeTeleports=MaxActive=0; InvalidLease=FreezeMovement=MoveThrows=false;
            MainBanker=null; Homes.Clear(); Moves.Clear();
        }
    }

    /// <summary>Fake ledger: the ONLY spend source. Debits banker._stashedCoins
    /// and records every attempt (amount + balance before).</summary>
    internal static class PatchEconomy_Banker
    {
        internal static int TrySpendCalls;
        internal static readonly List<int> SpendAmounts = new();
        internal static readonly List<(int Amount, int BalanceBefore)> Attempts = new();
        internal static bool FailNext;

        internal static bool TrySpendForAutoRestock(Banker banker, int amount)
        {
            TrySpendCalls++;
            Attempts.Add((amount, banker._stashedCoins));
            if (FailNext || !BankAssistantCoordinator.IsCurrentRestockBanker(banker)
                || amount <= 0 || amount > 200
                || banker._stashedCoins < amount) return false;
            banker._stashedCoins -= amount;
            SpendAmounts.Add(amount);
            return true;
        }

        internal static void ResetLedger()
        {
            TrySpendCalls = 0; SpendAmounts.Clear(); Attempts.Clear(); FailNext = false;
        }
    }
}

public enum Stat { CoinsSpent }
public class Stats { public int Calls; public float Spent; public bool Throws; public void Increment(Stat stat,float amount) { Calls++; if(Throws) throw new Exception("stats"); Spent+=amount; } }
