using System;
using System.Collections.Generic;
using System.Reflection;

namespace Il2CppInterop.Runtime.Attributes { [AttributeUsage(AttributeTargets.Method)] public class HideFromIl2CppAttribute : Attribute { } }
namespace Il2CppInterop.Runtime.Injection
{
    public class RegisterTypeOptions { public Type[] Interfaces; }
    public static class ClassInjector
    {
        public static readonly HashSet<Type> Registered = new();
        public static bool IsTypeRegisteredInIl2Cpp(Type t) => Registered.Contains(t);
        public static void RegisterTypeInIl2Cpp(Type t, RegisterTypeOptions options = null) => Registered.Add(t);
    }
}
namespace Il2CppSystem
{
    public sealed class Action<T>
    {
        private System.Action<T> _action;
        public static explicit operator Action<T>(System.Action<T> action) => new() { _action = action };
        public void Invoke(T value) => _action(value);
    }
}
namespace UnityEngine
{
    public class Object
    {
        private static int _next = 100;
        public IntPtr Pointer { get; } = new(++_next);
        public int GetInstanceID() => (int)Pointer;
        public string name;
        public static void Destroy(Object obj) { if (obj is GameObject go) go.SetActive(false); }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject?.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T TryCast<T>() where T : class => this as T;
        public T Cast<T>() where T : class
        {
            if (typeof(T) == typeof(IPayableComponentOwner))
                return new OwnerBridge((KingdomEnhancedMod.HeavyShieldShopShellOwner)this) as T;
            return this as T;
        }
    }
    public class MonoBehaviour : Component
    {
        public MonoBehaviour() { }
        public MonoBehaviour(IntPtr pointer) { }
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }
    public class Transform : Component
    {
        public Transform parent;
        public readonly List<Transform> children = new();
        public Vector3 localPosition;
        public Vector3 localScale = new(1, 1, 1);
        public Vector3 position { get => parent == null ? localPosition : parent.position + localPosition;
            set => localPosition = parent == null ? value : value - parent.position; }
        public void SetParent(Transform next, bool keep)
        {
            var oldPosition = position; parent?.children.Remove(this); parent = next; parent?.children.Add(this);
            if (keep) position = oldPosition;
        }
        public bool IsChildOf(Transform other)
        {
            for (var p = parent; p != null; p = p.parent) if (p == other) return true;
            return false;
        }
    }
    public class GameObject : Object
    {
        public string tag;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public readonly Transform transform;
        private readonly List<Component> _components = new();
        public GameObject(string name) { this.name = name; transform = new() { gameObject = this }; _components.Add(transform); }
        public T AddComponent<T>() where T : Component
        {
            var ctor = typeof(T).GetConstructor(new[] { typeof(IntPtr) });
            var item = ctor == null ? (T)Activator.CreateInstance(typeof(T)) : (T)ctor.Invoke(new object[] { IntPtr.Zero });
            item.gameObject = this; _components.Add(item); return item;
        }
        public T GetComponent<T>() where T : Component => _components.Find(c => c is T) as T;
        public void SetActive(bool active)
        {
            bool old = activeInHierarchy; activeSelf = active;
            if (old && !active) DisableTree(this);
        }
        private static void DisableTree(GameObject go)
        {
            foreach (var c in go._components) c.GetType().GetMethod("OnDisable")?.Invoke(c, null);
            foreach (var child in go.transform.children.ToArray()) DisableTree(child.gameObject);
        }
    }
    public record struct Vector2(float x, float y) { public static Vector2 zero => new(0, 0); }
    public record struct Vector3(float x, float y, float z)
    {
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public struct Quaternion { public static Quaternion identity => default; }
    public enum FilterMode { Point }
    public enum TextureWrapMode { Clamp }
    public enum TextureFormat { RGBA32 }
    public enum SpriteMeshType { FullRect }
    public record struct Rect(float x, float y, float width, float height);
    public struct Color32 { public byte a; }
    public class Texture2D : Object
    {
        public int width, height, anisoLevel;
        public FilterMode filterMode; public TextureWrapMode wrapMode;
        public Texture2D(int width, int height, TextureFormat format, bool mipmap) { this.width = width; this.height = height; }
        // Pixel roundtrip is covered by the existing Pillow suite; this fake decodes only dimensions for borrowing tests.
        public Color32[] GetPixels32() { var pixels = new Color32[width*height]; pixels[0].a = 255; return pixels; }
    }
    public static class ImageConversion
    {
        public static bool LoadImage(Texture2D texture, byte[] bytes, bool nonReadable)
        {
            texture.width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4));
            texture.height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4)); return true;
        }
    }
    public class Sprite : Object
    {
        public Vector2 pivot; public Rect rect; public Texture2D texture; public float pixelsPerUnit;
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float ppu, uint extrude, SpriteMeshType type)
            => new() { texture = texture, rect = rect, pivot = pivot, pixelsPerUnit = ppu };
    }
    public class SpriteRenderer : Component
    {
        public Sprite sprite;
        public bool enabled = true, flipX;
        public int sortingOrder, sortingLayerID;
        public object sharedMaterial;
    }
    public class Rigidbody2D : Component { public bool isKinematic; public Vector2 velocity; public float angularVelocity; }
    public static class Time { public static float unscaledTime = 100, time = 100; public static int frameCount = 10; }
}

public enum CurrencyType { Coins, Gems }
public enum Side { Left = -1, Right = 1 }
public enum PickUpPolicy { Anybody }
public class LockIndicator { public enum LockReason { Invalid, NotLocked = 21 } }
public interface IPayableComponentOwner
{
    Il2CppSystem.Action<Player> OnPay { get; }
    bool CanPay(Player player);
    bool IsLocked(Player player, out LockIndicator.LockReason reason);
}
internal sealed class OwnerBridge : IPayableComponentOwner
{
    private readonly KingdomEnhancedMod.HeavyShieldShopShellOwner _owner;
    public OwnerBridge(KingdomEnhancedMod.HeavyShieldShopShellOwner owner) { _owner = owner; }
    public Il2CppSystem.Action<Player> OnPay => _owner.OnPay;
    public bool CanPay(Player p) => _owner.CanPay(p);
    public bool IsLocked(Player player, out LockIndicator.LockReason reason)
    {
        var pointer = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
        try { bool result = _owner.IsLocked(player, pointer); reason = (LockIndicator.LockReason)System.Runtime.InteropServices.Marshal.ReadInt32(pointer); return result; }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(pointer); }
    }
}
public class Currency { public CurrencyType CurrencyType; public Currency(CurrencyType kind) { CurrencyType = kind; } }
public class Player : UnityEngine.MonoBehaviour
{
    public enum PayState { None, Holding, Transaction, Completed, Cancelling }
    public bool hasLocalAuthority = true, DelayDrop, ThrowAfterDrop;
    public bool InsideStarted;
    public int CancelInsideStarted, DropInsideStarted;
    public PayableComponent selectedPayable, _completingPayable;
    public PayState _payState;
    public List<Currency> _floatingCurrency = new();
    public int CancelCalls, DropCalls, WalletAdds, Consumed;
    public void CancelTransaction() { CancelCalls++; if (InsideStarted) CancelInsideStarted++; _payState = PayState.Cancelling; }
    public void DropFloatingCurrency() { DropCalls++; if (InsideStarted) DropInsideStarted++; if (!DelayDrop) _floatingCurrency.Clear(); if (ThrowAfterDrop) throw new Exception("drop uncertain"); }
    public void DeselectPayable() { selectedPayable = null; }
}
public class PayableComponent : UnityEngine.MonoBehaviour
{
    public CRPCHeader parentHeaderRef;
    public bool forceBlockPayment, repositionInSplitScreen, glowOnSelect, needsNewNetId;
    public UnityEngine.Vector2 indicatorOffset, playerPayPointOffset, payablePlacementExclusionOffset;
    public float playerPayDistance, payablePlacementExclusionDistance;
    private int _price;
    private CurrencyType _currency;
    public int PriceWrites, CurrencyWrites;
    public int Price { get => _price; set { _price = Math.Max(1, value); PriceWrites++; } }
    public CurrencyType Currency { get => _currency; set { _currency = value; CurrencyWrites++; } }
    public int priceIncrease;
    public IPayableComponentOwner Owner;
    private readonly List<Il2CppSystem.Action<Player>> _started = new();
    public void Init(IPayableComponentOwner owner) { Owner = owner; Managers.Inst.payables.Items.Add(this); }
    public void add_OnTransactionStartedCallback(Il2CppSystem.Action<Player> action) => _started.Add(action);
    public void remove_OnTransactionStartedCallback(Il2CppSystem.Action<Player> action) => _started.Remove(action);
    public void Start(Player player)
    {
        player.selectedPayable = this; player._payState = Player.PayState.Transaction;
        player.InsideStarted = true;
        try { foreach (var a in _started) a.Invoke(player); }
        finally { player.InsideStarted = false; }
    }
    public void CompleteNative(Player player, bool cleanup = true)
    {
        player._completingPayable = this; player._payState = Player.PayState.Completed;
        if (Owner.CanPay(player)) Owner.OnPay.Invoke(player);
        if (cleanup) { player.Consumed += player._floatingCurrency.Count; player._floatingCurrency.Clear(); player._completingPayable = null; player._payState = Player.PayState.None; }
    }
}
public class PayableShop : PayableComponent { public enum ShopType { Bow, Hammer, Scythe } }
public class ShopTag : UnityEngine.Component { public PayableShop.ShopType type; }
public class PayableManager
{
    public readonly List<PayableComponent> Items = new();
    public PayableComponent[] AllPayables => Items.ToArray();
    public int ExclusionReads;
    public bool OverlapsAnyExclusions(float x, float halfWidth, bool useGhost) { ExclusionReads++; return false; }
}
public class Persistent : UnityEngine.Component { public bool persistObject = true; public string path = "ToolBow"; public bool ShouldPersist() => gameObject.activeInHierarchy && persistObject; }
public class DroppableTool : UnityEngine.Component
{
    public UnityEngine.GameObject dropper, friendlyClaimer, enemyClaimer;
    public PayableShop parentShopRef;
    public bool selfDestruct, pickedUp, Fake;
    public PickUpPolicy pickUpPolicy;
    public UnityEngine.Rigidbody2D _rigidbody;
    public void SetFake(bool fake) => Fake = fake;
}
public class Archer : UnityEngine.Component { }
public class Pool : UnityEngine.MonoBehaviour
{
    public UnityEngine.GameObject prefab;
    public readonly List<UnityEngine.GameObject> _activeCache = new();
    public static int SpawnCalls;
    public static bool ReturnNull, ThrowAfterSpawn;
    public static DroppableTool ReturnExisting;
    public static DroppableTool LastIssued;
    private static readonly Dictionary<UnityEngine.GameObject, Pool> Origin = new();
    public static T Spawn<T>(T prefab, UnityEngine.Vector3 position, UnityEngine.Quaternion rotation, UnityEngine.Transform parent, bool assert) where T : UnityEngine.Component
    {
        SpawnCalls++;
        if (ReturnExisting != null) return ReturnExisting as T;
        if (ReturnNull) return null;
        var go = new UnityEngine.GameObject("ToolBow (Clone)") { tag = "Bow" }; go.transform.SetParent(parent, false); go.transform.position = position;
        var bow = go.AddComponent<DroppableTool>(); go.AddComponent<Persistent>(); go.AddComponent<UnityEngine.SpriteRenderer>();
        bow._rigidbody = go.AddComponent<UnityEngine.Rigidbody2D>();
        var pool = Managers.Inst.pools.BowPool; pool._activeCache.Add(go); Origin[go] = pool; LastIssued = bow;
        if (ThrowAfterSpawn) throw new Exception("spawn side effect then throw");
        return bow as T;
    }
    public static Pool GetPoolByInstance(UnityEngine.GameObject go) => Origin.TryGetValue(go, out var pool) ? pool : null;
}
public class PoolManager : UnityEngine.Component
{
    public Pool BowPool;
    public Pool GetPoolByPrefabName(string name) => name == "ToolBow" ? BowPool : null;
}
public class Game { public enum State { Playing, Menu, Intro }; public State state = State.Playing; }
public class World { public UnityEngine.Transform gameLayer; }
public class Kingdom : UnityEngine.Component
{
    public bool HasBorderLoaded = true;
    public Player playerOne, playerTwo;
    public float GetBorderSideIntact(Side side) => side == Side.Left ? -20 : 20;
}
public class Managers
{
    public static Managers Inst;
    public Game game = new(); public World world = new(); public Kingdom kingdom; public PayableManager payables = new(); public PoolManager pools;
}
public static class NetworkBigBoss { public static bool IsOnline, HasWorldAuth = true; }
public static class IslandSaveData { public static bool isSavingGame; }
public class BiomeHolder { public static BiomeHolder Inst; public const int GreeceBiomeIndex = 8; public int BiomeIndex; }
public class CRPCStamp : UnityEngine.Component { }
public enum CRPCType { SemiStatic }
public class CRPCHeader : UnityEngine.Object { public UnityEngine.GameObject referencedGO; public int NetID; public List<int> RemoteMethodList = new() { 1, 2, 3 }; }
public class NetworkPostbox : UnityEngine.Object
{
    public static NetworkPostbox Instance;
    public readonly Dictionary<UnityEngine.GameObject, CRPCHeader> MasterSemiCRPCHLookup = new();
    public readonly Dictionary<int, CRPCHeader> SemiStaticObjects = new();
    private int _next;
    public CRPCHeader RegisterObject(UnityEngine.GameObject go, CRPCType type)
    {
        var header = new CRPCHeader { referencedGO = go, NetID = ++_next };
        MasterSemiCRPCHLookup.Add(go, header); SemiStaticObjects.Add(header.NetID, header);
        go.GetComponent<PayableComponent>().parentHeaderRef = header; return header;
    }
    public void DeregisterObject(UnityEngine.GameObject go, int id, CRPCType type) { MasterSemiCRPCHLookup.Remove(go); SemiStaticObjects.Remove(id); }
}

namespace KingdomEnhancedMod
{
    internal static class HeavyShieldQuota { internal enum Side { Left, Right } }
    internal static class HeavyShieldRuntime { internal static bool CarrierPreflightReady = true; }
    internal static class HeroShopPlacementNative
    {
        internal static int IntervalReads;
        internal static bool Find(PayableManager manager, float left, float right, float halfWidth, out float x)
            => HeroShopPlacement.Find(left, right, halfWidth, p => !manager.OverlapsAnyExclusions(p, halfWidth, false),
                () => { IntervalReads++; return new[] { new HeroShopPlacement.Interval(-1, 1) }; }, out x);
    }
    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new(); public Logger LogSource = new();
        public class Logger { public void LogInfo(string message) { } public void LogWarning(string message) { } }
    }
    internal static class HeavyShieldIdentity
    {
        internal static bool Ready = true, Unknown, Mold, LeftExtra, RightExtra;
        internal static HeavyShieldCommitResult GemResult = HeavyShieldCommitResult.Applied, BowResult = HeavyShieldCommitResult.Applied;
        internal static int Reserves, ReserveCalls, GemCommits, BowBinds, Cancels, UnknownCalls, SeenReceiptCount;
        internal static bool ReserveRefused, ReserveThrows, ReserveMalformed, ReserveThrowsAfterReservation;
        internal static string LastReason;
        internal static readonly Dictionary<Guid, HeavyShieldPurchaseLease> Pending = new();
        internal static readonly Dictionary<DroppableTool, HeavyShieldCareerHandle> Paid = new();
        internal static string StatusText => Unknown ? "unknown" : "ready";
        internal static bool CanReservePurchase(HeavyShieldPurchaseKind kind)
        {
            if (!Ready || Unknown || Pending.Count != 0 || SeenReceiptCount >= 4096) return false;
            return kind switch
            {
                HeavyShieldPurchaseKind.Mold => !Mold,
                HeavyShieldPurchaseKind.ExtraLeft => Mold && !LeftExtra,
                HeavyShieldPurchaseKind.ExtraRight => Mold && !RightExtra,
                _ => Mold && Paid.Count == 0
            };
        }
        internal static bool TryReservePurchase(HeavyShieldPurchaseKind kind, out HeavyShieldPurchaseLease lease)
        {
            ReserveCalls++; lease = default;
            if (ReserveRefused) return false;
            if (ReserveThrows) throw new Exception("reserve uncertain");
            if (!CanReservePurchase(kind)) return false;
            var side = kind is HeavyShieldPurchaseKind.ExtraRight or HeavyShieldPurchaseKind.ShieldRight
                ? HeavyShieldQuota.Side.Right : HeavyShieldQuota.Side.Left;
            var campaign = new HeavyShieldCampaignToken("campaign", new(1), new(2), 1, HeavyShieldIdentityPhase.Loaded);
            lease = new(Guid.NewGuid(), campaign, kind, side, 0, Managers.Inst.world.gameLayer.Pointer.ToInt64());
            Pending.Add(lease.Receipt, lease); Reserves++;
            if (ReserveThrowsAfterReservation) { Unknown = true; throw new Exception("partial reservation, A preserves unknown"); }
            if (ReserveMalformed) lease = lease with { Kind = kind == HeavyShieldPurchaseKind.Mold ? HeavyShieldPurchaseKind.ExtraRight : HeavyShieldPurchaseKind.Mold };
            return true;
        }
        internal static bool ValidatePurchase(in HeavyShieldPurchaseLease lease)
            => Ready && !Unknown && Pending.TryGetValue(lease.Receipt, out var value) && value == lease;
        internal static HeavyShieldCommitResult CompleteGemPayment(in HeavyShieldPurchaseLease lease)
        {
            GemCommits++;
            if (GemResult != HeavyShieldCommitResult.Applied) return GemResult;
            if (!ValidatePurchase(in lease)) return HeavyShieldCommitResult.Rejected;
            if (lease.Kind == HeavyShieldPurchaseKind.Mold) Mold = true;
            else if (lease.Kind == HeavyShieldPurchaseKind.ExtraLeft) LeftExtra = true;
            else if (lease.Kind == HeavyShieldPurchaseKind.ExtraRight) RightExtra = true;
            Pending.Remove(lease.Receipt); return HeavyShieldCommitResult.Applied;
        }
        internal static HeavyShieldCommitResult BindIssuedPaidBow(in HeavyShieldPurchaseLease lease, DroppableTool bow)
        {
            BowBinds++; if (BowResult != HeavyShieldCommitResult.Applied) return BowResult;
            if (!ValidatePurchase(in lease)) return HeavyShieldCommitResult.Rejected;
            var go = bow.gameObject;
            Paid.Add(bow, new(lease.Receipt, lease.Campaign.Guid, lease.Side, lease.Land, go.Pointer, go.GetInstanceID(), lease.World, 1));
            Pending.Remove(lease.Receipt); return HeavyShieldCommitResult.Applied;
        }
        internal static bool ConfirmUnpaidCancellation(in HeavyShieldPurchaseLease lease)
        { Cancels++; return Pending.Remove(lease.Receipt); }
        internal static void HoldUnknown(in HeavyShieldPurchaseLease lease, string reason)
        { Unknown = true; UnknownCalls++; LastReason = reason; }
        internal static bool TryGetPaidBow(DroppableTool bow, out HeavyShieldCareerHandle handle) => Paid.TryGetValue(bow, out handle);
        internal static bool ValidateCareer(in HeavyShieldCareerHandle handle) => Paid.ContainsValue(handle);
        internal static HeavyShieldQuotaView GetQuotaView() => new(Ready, Unknown, Mold, LeftExtra, RightExtra,
            LeftExtra ? 2 : 1, RightExtra ? 2 : 1, 0, 0, Paid.Count != 0 || Pending.Count != 0);
    }
}
