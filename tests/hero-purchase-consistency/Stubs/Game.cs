// Minimal stand-in for the game types the compiled production files reference. These are
// boundary stand-ins, not game behaviour evidence. Wallet capacity, native pay-state machine
// internals and native save orchestration remain owned by the real game.
using System;
using System.Collections.Generic;

public enum CurrencyType { Coins, Gems }
public enum Side { Left = -1, None = 0, Right = 1 }
public enum DamageSource { Arrow = 1, Knight = 2, Fire = 3 }

public static class Layers
{
    public static string Enemies = "Enemies";
}

public class LockIndicator
{
    public enum LockReason { Invalid = 0, NotLocked = 21 }
}

public interface IPayableComponentOwner
{
    Il2CppSystem.Action<Player> OnPay { get; }
    bool CanPay(Player player);
    bool IsLocked(Player player, out LockIndicator.LockReason reason);
}

public class Currency { }

public class Wallet : UnityEngine.Component
{
    public int Coins;
    /// <summary>Mod-visible refunds: production AddCurrency calls (OnPay failure path).</summary>
    public int AddCalls;
    /// <summary>Engine pickup credits: dropped scene coins walked over by the player.</summary>
    public int PickupCalls;

    // Wallet capacity is not modelled here; production only calls AddCurrency for the
    // documented refund path, which must restore the exact price.
    public void AddCurrency(CurrencyType type, int amount)
    {
        AddCalls++;
        if (type == CurrencyType.Coins) Coins += amount;
    }

    // Engine credit for picking dropped scene coins back up (not a mod wallet write).
    public void NativePickupCredit(CurrencyType type, int amount)
    {
        PickupCalls++;
        if (type == CurrencyType.Coins) Coins += amount;
    }
}

public class Player : UnityEngine.MonoBehaviour
{
    public enum PayState { None = 0, Holding = 1, Transaction = 2, Completed = 3, Cancelling = 4 }

    public bool hasLocalAuthority = true;
    public Wallet wallet;
    public PayState _payState;
    public PayableComponent _completingPayable;
    public PayableComponent selectedPayable;
    public readonly List<Currency> _floatingCurrency = new List<Currency>();
    /// <summary>Floating slots consumed by the native caller after a completed callback.</summary>
    public int ConsumedFloating;
    /// <summary>Coins currently lying in the scene after a native cancellation drop.</summary>
    public int DroppedFloating;
    /// <summary>Number of drop invocations that moved at least one coin to the scene.</summary>
    public int DropCalls;
    public int CancelCalls;

    // Real 2.4: a refused final CanPay cancels and then drops the floating coins back into the
    // scene. The drop itself never credits the wallet; the coins return to the wallet only when
    // the player picks them up (separate step, see NativePickupDropped).
    public void CancelTransaction() { CancelCalls++; _payState = PayState.Cancelling; }

    public void DropFloatingCurrency()
    {
        int dropped = _floatingCurrency.Count;
        _floatingCurrency.Clear();
        if (dropped > 0) { DroppedFloating += dropped; DropCalls++; }
    }

    // Separate native step, never folded into DropFloatingCurrency: the player walks over the
    // dropped coins and picks them back up. Callers must assert the pre-pickup state
    // (wallet minus the dropped coins, DroppedFloating = dropped) before invoking it.
    public void NativePickupDropped()
    {
        if (DroppedFloating <= 0 || wallet == null) return;
        wallet.NativePickupCredit(CurrencyType.Coins, DroppedFloating);
        DroppedFloating = 0;
    }

    public void DeselectPayable() { selectedPayable = null; }
}

public class CRPCHeader : UnityEngine.Object
{
    public short NetID;
    public int netID;
    public CRPCType HeaderType;
    public UnityEngine.GameObject referencedGO;
    public List<int> RemoteMethodList = new List<int>();
}

public enum CRPCType { Static = 0, Dynamic = 1, SemiStatic = 2 }

public class CRPCStamp : UnityEngine.Component { }

public class NetworkPostbox : UnityEngine.Object
{
    public static NetworkPostbox Instance;

    public readonly Dictionary<UnityEngine.GameObject, CRPCHeader> MasterSemiCRPCHLookup =
        new Dictionary<UnityEngine.GameObject, CRPCHeader>();
    public readonly Dictionary<short, CRPCHeader> SemiStaticObjects = new Dictionary<short, CRPCHeader>();

    private short _nextSemi = 100;

    public CRPCHeader RegisterObject(UnityEngine.GameObject owner, CRPCType type)
    {
        var header = new CRPCHeader
        {
            NetID = type == CRPCType.SemiStatic ? _nextSemi++ : (short)903,
            HeaderType = type,
            referencedGO = owner,
        };
        header.netID = header.NetID;
        header.RemoteMethodList.Add(0);
        header.RemoteMethodList.Add(0);
        header.RemoteMethodList.Add(0);
        var payable = owner.GetComponent<PayableComponent>();
        if (payable != null) payable.parentHeaderRef = header;
        if (type == CRPCType.SemiStatic)
        {
            MasterSemiCRPCHLookup[owner] = header;
            SemiStaticObjects[header.NetID] = header;
        }
        return header;
    }

    public void DeregisterObject(UnityEngine.GameObject owner, short netId, CRPCType type)
    {
        MasterSemiCRPCHLookup.Remove(owner);
        SemiStaticObjects.Remove(netId);
    }
}

public class PayableComponent : UnityEngine.MonoBehaviour
{
    public CRPCHeader parentHeaderRef;
    public bool forceBlockPayment, repositionInSplitScreen, glowOnSelect, needsNewNetId;
    public UnityEngine.Vector2 indicatorOffset, playerPayPointOffset, payablePlacementExclusionOffset;
    public float playerPayDistance, payablePlacementExclusionDistance;
    public float indicatorSpacing;
    public int Price, priceIncrease;
    public CurrencyType Currency;
    public IPayableComponentOwner Owner;

    private readonly List<Il2CppSystem.Action<Player>> _started = new List<Il2CppSystem.Action<Player>>();

    public void Init(IPayableComponentOwner owner) { Owner = owner; }
    public void add_OnTransactionStartedCallback(Il2CppSystem.Action<Player> callback) { if (callback != null) _started.Add(callback); }
    public void remove_OnTransactionStartedCallback(Il2CppSystem.Action<Player> callback) => _started.Remove(callback);

    // Native bridge: Payable.TransactionStarted dispatch (called by the game when a player selects).
    public void InvokeTransactionStarted(Player player)
    {
        for (int i = 0; i < _started.Count; i++) _started[i]?.Invoke(player);
    }

    public float PayableExclusionPoint() => transform != null ? transform.position.x : 0f;
}

public class PayableShop : PayableComponent
{
    public enum ShopType { Bow, Hammer, Scythe }
}

public class ShopTag : UnityEngine.Component
{
    public PayableShop.ShopType type;
}

public class ExclusionBlocker : UnityEngine.Component
{
    public float exclusionDistance;
    public float GetExclusionPoint() => transform != null ? transform.position.x : 0f;
}

public class PayableManager : UnityEngine.Object
{
    public readonly List<PayableComponent> Items = new List<PayableComponent>();
    public List<ExclusionBlocker> _allBlockers = new List<ExclusionBlocker>();
    public PayableComponent[] AllPayables => Items.ToArray();
    public int ExclusionReads;
    public bool OverlapsAnyExclusions(float x, float halfWidth, bool useGhost) { ExclusionReads++; return false; }
}

public class Kingdom : UnityEngine.Component
{
    public bool HasBorderLoaded = true;
    public Player playerOne;
    public Player playerTwo;
    public float GetBorderSideIntact(Side side) => side == Side.Left ? -20f : 20f;
}

public class Game
{
    public enum State { Playing = 0, Menu = 1, Intro = 2, Loss = 3 }
    public State state = State.Playing;
    public bool playingOrInMenuWithClient => state == State.Playing || state == State.Menu;
}

public class World : UnityEngine.Object
{
    public UnityEngine.Transform gameLayer;
}

public class Managers
{
    public static Managers Inst;
    public Game game;
    public World world;
    public Kingdom kingdom;
    public PayableManager payables;
}

public static class NetworkBigBoss
{
    public static bool IsOnline;
    public static bool HasWorldAuth = true;
}

public class BiomeHolder
{
    public static BiomeHolder Inst;
    public int BiomeIndex;
}

public class Persistent : UnityEngine.Component
{
    public bool persistObject = true;
    public bool ShouldPersist() => true;
}

public class Damageable : UnityEngine.Component
{
    public bool isDead;
    public bool enabled = true;
    public DeathEvent OnDeath;
    public bool IsDamagedBy(DamageSource source) => true;

    public void Kill()
    {
        isDead = true;
        OnDeath?.Invoke(gameObject);
    }

    public sealed class DeathEvent
    {
        internal Action<UnityEngine.GameObject> Action;
        public static implicit operator DeathEvent(Action<UnityEngine.GameObject> action) => new DeathEvent { Action = action };
        public static DeathEvent operator +(DeathEvent a, DeathEvent b) => new DeathEvent { Action = a?.Action + b?.Action };
        public static DeathEvent operator -(DeathEvent a, DeathEvent b) => new DeathEvent { Action = a?.Action - b?.Action };
        public void Invoke(UnityEngine.GameObject victim) => Action?.Invoke(victim);
    }
}

public class Character : UnityEngine.Component
{
    public bool inert;
    public bool grabbed;
    public Damageable _damageable;
    public Character ReplaceBy(string role) => this;
}

public interface IEmbarkeeOwner { IntPtr Pointer { get; } T TryCast<T>() where T : class; }

public class Archer : UnityEngine.Component, IEmbarkeeOwner
{
    public enum AttackMode { Melee = 0, Ranged = 1 }

    public Side side = Side.Right;
    public bool enabled = true;
    public bool harmless;
    public bool inGuardSlot;
    public GuardSlot _guardSlot;
    public AttackMode _attackMode = AttackMode.Ranged;
    public AttackMode _desiredAttackMode = AttackMode.Ranged;
    public Character _character;
    public Damageable _damageable;
    public Embarkee _embarkee;
    public float shootRange = 8f;
    public float walkSpeed = 4f;
    public float runSpeed = 6f;
    public UnityEngine.GameObject _shootingTarget;
    public bool PlayerControlled;
    public UnityEngine.GameObject GetGO => gameObject;

    public bool ShouldPlayerControl() => PlayerControlled;
    public bool IsAvailableForJob(UnityEngine.GameObject job) => true;
    public void AssignJob(UnityEngine.GameObject job) { }
    public void SetGuardSlot(GuardSlot slot) { _guardSlot = slot; }
    public void EnterGuardSlot(GuardSlot slot) { _guardSlot = slot; inGuardSlot = true; }
    public void ExitGuardSlot() { _guardSlot = null; inGuardSlot = false; }
}

public class WorkerOwner : UnityEngine.Component, IEmbarkeeOwner { }

public class GuardSlot : UnityEngine.Component
{
    public Archer archer;
}

public class Knight : UnityEngine.Component { }

/// <summary>A boarding slot (boat). Test-side only.</summary>
public class Embarkable : UnityEngine.Component
{
    public string kind = "boat";
}

/// <summary>
/// Registrar stand-in. Real 2.4 keeps per-type HashSets and rebuilds the candidate list when it
/// redistributes; this models only the bookkeeping the tests observe: registration membership,
/// disable/enable transitions and redistribution counts. Scoring, slot assignment and per-unit
/// preferences are not modelled.
/// </summary>
public static class EmbarkableSim
{
    public static readonly List<Embarkee> Registered = new List<Embarkee>();
    public static int RegisterEvents;
    public static int UnregisterEvents;
    public static int Redistributions;
    public static int InactiveRegisterSkips;
    /// <summary>Probe invoked at the start of the modeled native OnDisable path.</summary>
    public static Action<Embarkee> OnNativeDisableProbe;
    public static Action<Embarkee> OnNativeEnableProbe;

    public static void Reset()
    {
        Registered.Clear();
        RegisterEvents = 0; UnregisterEvents = 0; Redistributions = 0; InactiveRegisterSkips = 0;
        OnNativeDisableProbe = null;
        OnNativeEnableProbe = null;
    }

    public static void Register(Embarkee embarkee)
    {
        RegisterEvents++;
        if (!Registered.Contains(embarkee)) Registered.Add(embarkee);
        Redistributions++;
    }

    public static void Unregister(Embarkee embarkee)
    {
        if (Registered.Remove(embarkee)) UnregisterEvents++;
        Redistributions++;
    }

    public static bool IsRegistered(Embarkee embarkee) => Registered.Contains(embarkee);
}

/// <summary>
/// Embarkee stand-in. `enabled` models the native component state: disabling runs the native
/// OnDisable path (unregister from the registrar, clear the pending target for a not-embarked
/// unit, then redistribute), enabling registers immediately for an active object and is skipped
/// (deferred to activation) for an inactive one. IsTargetingEmbarkable/IsEmbarked mirror the
/// native getters over the test-visible fields.
/// </summary>
public class Embarkee : UnityEngine.Component
{
    public IEmbarkeeOwner _owner;
    public Embarkable EmbarkableTarget;
    public bool IsEmbarked;
    public bool IsTargetingEmbarkable => EmbarkableTarget != null;
    public bool CanEmbark => true;
    public int SetTargetCalls;
    public int EmbarkCalls;
    public int ClearTargetCalls;

    private bool _enabled = true;
    public bool enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (!value) SimulateNativeDisable();
            else SimulateNativeEnable();
        }
    }

    // Native OnDisable: the registrar unregisters the unit and the unregister path clears the
    // pending target (for a not-embarked unit) and redistributes. The probe lets tests observe
    // what is already published when the disable runs.
    private void SimulateNativeDisable()
    {
        EmbarkableSim.OnNativeDisableProbe?.Invoke(this);
        EmbarkableSim.Unregister(this);
        ClearEmbarkableTarget();   // native unregister calls ClearEmbarkableTarget unconditionally
    }

    // Native OnEnable: registration happens immediately on an active object; an inactive object
    // only registers when it is activated later.
    private void SimulateNativeEnable()
    {
        EmbarkableSim.OnNativeEnableProbe?.Invoke(this);
        if (gameObject == null || !gameObject.activeInHierarchy) { EmbarkableSim.InactiveRegisterSkips++; return; }
        EmbarkableSim.Register(this);
    }

    public void ClearEmbarkableTarget() { ClearTargetCalls++; EmbarkableTarget = null; }
    public void SetEmbarkableTarget(Embarkable target, int slot) { SetTargetCalls++; EmbarkableTarget = target; }
    public void Embark() { EmbarkCalls++; IsEmbarked = true; }
}

public class FriendlyTroll : UnityEngine.Component { }

public static class Pool
{
    public static void Despawn(UnityEngine.GameObject target, bool instant) { }
    public static void FastDespawn(UnityEngine.GameObject target, float delay, bool instant) { }
}

public class ProofDictionary : Dictionary<string,string>
{
    // One-shot synthetic read failure: the next TryGetValue throws, nothing else changes.
    public bool ThrowOnce;
    public new bool TryGetValue(string key, out string value)
    {
        if (ThrowOnce) { ThrowOnce=false; throw new InvalidOperationException("synthetic transient read failure"); }
        return base.TryGetValue(key,out value);
    }
}
public class PrefsSaveData : UnityEngine.Object
{
    public ProofDictionary contents = new ProofDictionary();
    public bool FailWrite;
    public void SetString(string key, string value)
    {
        if (FailWrite) throw new InvalidOperationException("synthetic prefs write failure");
        contents[key] = value;
    }
    public void PrepareBeforeSave() { }
}
public class GlobalSaveData : UnityEngine.Object
{
    public static GlobalSaveData loaded;
    public static GlobalSaveData _loaded { get => loaded; set => loaded = value; }
    public static string filename = "global-v35";
    public int currentCampaign;
    public int currentChallenge;
    public PrefsSaveData prefs = new PrefsSaveData();
    public Il2CppSystem.Collections.Generic.List<CampaignSaveData> campaigns = new();
    public Il2CppSystem.Collections.Generic.List<CampaignSaveData> challenges = new();
    public CampaignSaveData GetCurrentCampaign() => CampaignSaveData.current;
    public void SaveAsync(Il2CppSystem.Action<Coatsink.Common.SaveLoadResult> callback) { }
    public CampaignSaveData CreateNewCampaign() => new CampaignSaveData();
    public void TryDeleteCampaignAsync() { }
    public void DeleteChallenge(int challenge) { }
    public class _Save_d__89 : UnityEngine.Object
    {
        public int __1__state;
        public GlobalSaveData __4__this;
        public ReturnBox @return;
        public bool MoveNext() => false;
    }
    public class __TryDeleteCampaign_d__91 : UnityEngine.Object
    { public int __1__state; public bool MoveNext() => false; }
    public class __TryDeleteChallenge_d__94 : UnityEngine.Object
    { public int __1__state; public bool MoveNext() => false; }
    public struct ReturnBox { public Coatsink.Common.SaveLoadResult value; }
}

public class CarryForwardState
{
    public bool present;
}

public class CampaignSaveData : UnityEngine.Object
{
    public static CampaignSaveData current;
    public IslandSaveData CurrentIsland;
    public CarryForwardState carryForward = new CarryForwardState();
    public void ApplyToScene() { }
}

public class IslandSaveData : UnityEngine.Object
{
    public static IslandSaveData CurrentlySavingIsland;
    public static bool isSavingGame;

    public int land;
    public bool isNew = true;
    public double playTimeDays;
    public string Json = "{}";
    public Il2CppSystem.Collections.Generic.List<ObjectData> objects = new Il2CppSystem.Collections.Generic.List<ObjectData>();

    public static void Save(int campaign, int land, int challenge) { }
    public static string GetID(Persistent persistent) => persistent != null ? persistent.name : "";
    public static Persistent TryCreateOrFind(ObjectData data) => data != null ? data.Root : null;
    public bool TryPopObjectsToScene() => true;
    public void UpdateSavedWithRevisions() { }

    public class ObjectData : UnityEngine.Object
    {
        public string uniqueID;
        public Persistent Root;
        public Il2CppSystem.Collections.Generic.List<ComponentData> componentData2 = new Il2CppSystem.Collections.Generic.List<ComponentData>();

        public class ComponentData
        {
            public string name;
            public string type;
        }
    }
}
