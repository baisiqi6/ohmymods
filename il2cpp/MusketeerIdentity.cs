using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

// Paid musketeer career identity. Ordinary units/guns have no seat quota: one career GUID owns
// exactly one exchangeable artifact at a time (native ToolBow row -> native Archer row -> the
// next dropped ToolBow) and never appears on two owners at once. Career.Kind is updated by every
// successful bind, so the live record always describes the object that currently carries it.
//
// The native world already knows the objects (a ToolBow is a ToolBow); only this sidecar knows
// which of them were paid for. So the runtime model is:
//  * a career is *bound* to one live root instance (validated by pointer + Unity instance id +
//    our own life counter + runtime world) or *reserved* (paid, current holder unproven);
//  * a bound career is released only when the native lifecycle proves it ended: a confirmed
//    immediate Pool.FastDespawn, a destroyed/replaced root, or an explicit transfer;
//  * plain native OnDisable / visual fallback is never a deletion;
//  * pool instances are reused, so every root carries a life; Pool.FastSpawn starts a new life
//    and Pool.FastDespawn (delay <= 0, confirmed inactive) ends one. Records bound to an older
//    life can never validate against the reused instance.
//
// A promotion owns a per-call PromotionState (Harmony __state): the career GUID and root life
// are snapshotted before the native Character.Promote call, other postfixes see
// GunPromotionInProgress until this call's finalizer, and a gun consumed by a confirmed pool
// despawn inside the call still transfers to the exact returned Archer. Nothing is ever
// fabricated for a failed native call, and a root reused mid-call never receives the career.
//
// StockSlot (0..StockSlots-1) is rack metadata for guns that still sit on the paid shop rack;
// it is cleared by every transfer and revoked by ReleaseStock / a Droppable.Drop of that gun,
// so a stolen or dropped gun is never rescued back onto the rack. It never touches identity.
internal static class MusketeerIdentity
{
    internal const int MaxRecords = MusketeerArchive.MaxRecords;
    internal const int StockSlots = MusketeerArchive.MaxStockSlots;
    private const int MaxLoggedKeys = 64;
    private const float SweepInterval = 2f;
    private const int MaxLogText = 200;
    // Failed rack restores are retried by the 2s sweep; at this many attempts (~60s) the claim
    // is given up (revoked, never the paid career) instead of blocking purchases forever.
    internal const int StockRestoreAbandonAttempts = 30;

    // Captured per native Pool.FastDespawn call; the postfix decides whether the recycling
    // actually completed (immediate, object gone/inactive) before ownership is released.
    internal struct PoolDespawnState
    {
        internal GameObject Root;
        internal IntPtr Pointer;
        internal bool Tracked;
        internal Career RestockCareer;
        internal long RestockLife, RestockWorld;
    }

    // Captured per native Character.Promote(DroppableTool, IUnitController) call. Harmony threads
    // one instance through prefix -> postfix -> finalizer, so nested promotions cannot overwrite
    // each other and the scope flag only clears in the matching finalizer.
    internal struct PromotionState
    {
        internal Career Career;
        internal IntPtr ToolPointer;
        internal long ToolLife;
        internal bool Engaged;
    }

    // One tracked root instance (pool entry). Life advances whenever the same address hosts a
    // new life: a confirmed despawn followed by Pool.FastSpawn, or a different GameObject
    // instance id at the same address. World is the gameLayer pointer observed when the life
    // was created; a binding from another runtime world can never validate.
    internal sealed class RootSlot
    {
        internal IntPtr Pointer;
        internal int GoId;
        internal long Life;
        internal bool Alive;
        internal long World;
    }

    internal sealed class Career
    {
        internal Guid Id;
        internal int Kind;
        internal string NativeId = "";     // witnessed native row id; only ever written by a live capture
        internal int StockSlot = MusketeerCareer.NoStockSlot;
        internal IslandState State;
        internal RootSlot Slot;            // null = reserved (no proven holder)
        internal long Life;
        internal int GoId;
        internal Character Character;      // kind unit
        internal Archer Archer;            // kind unit
        internal DroppableTool Tool;       // kind gun
    }

    // One pending rack-physics restoration: a proven stock gun whose freeze could not be applied
    // yet (missing body, guard blocked). It keeps its claim, blocks new charges and is retried by
    // the bounded reconciliation until it succeeds or the claim is disproven.
    internal sealed class StockRestore
    {
        internal Career Career;
        internal int Attempts;
    }

    // One stable island lineage (file + campaign/challenge + land) resolved to one epoch scope.
    internal sealed class IslandState
    {
        internal string ContextKey;
        internal string Epoch;
        internal string MatchKind = "none";
        internal string MatchHash;
        internal long World;
        internal bool Ready;
        // Session-only, coalesced defense event. Load binds wait for Ready; promotion binds wait
        // for a later frame and the promotion finalizer. No scene scan and no archive fields.
        internal int DefenseAttempts;
        internal int DefenseBindFrame;
        internal long DefenseWorld;
        internal bool ReadOnly;
        internal bool Unresolved;          // unproven native snapshot / load anomaly: preserve + block
        internal bool NewEpoch;
        internal bool HasBaseline;
        internal bool GenerationPending;
        internal IntPtr VirginIsland;      // once-guard for repeated ApplyToScene of one generation
        internal long VirginWorld;
        internal readonly List<Career> Careers = new();
        internal readonly List<StockRestore> StockRestores = new();
    }

    internal static readonly Dictionary<string, IslandState> Islands = new(StringComparer.Ordinal);
    private static readonly Dictionary<IntPtr, RootSlot> Roots = new();
    private static readonly Dictionary<IntPtr, Career> Bound = new();
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);
    private static IslandState _current;
    private static long _nextLife;
    private static float _nextSweep;
    private static int _lastTickFrame = -1;
    private static int _contextFrame = -1;
    private static string _contextKey;
    private static long _contextWorld;
    private static int _promotionDepth;
    private static bool _bindCanary;

    internal static IslandState Current => _current;
    // Session-only accounting evidence. Never serialized and never inferred from missing rows.
    private static readonly Dictionary<Career, long> RestockEnded = new();
    private static IslandState _restockEvidenceState;
    private static long _restockEvidenceWorld;
    private static void RestockEvidenceContext(IslandState state, long world)
    {
        if (ReferenceEquals(state, _restockEvidenceState) && world == _restockEvidenceWorld) return;
        RestockEnded.Clear(); _restockEvidenceState = state; _restockEvidenceWorld = world;
    }
    internal static event Action<Character> CareerChanged;
    internal static event Action<DroppableTool> GunChanged;
    private static void NotifyGunChanged(DroppableTool tool)
    {
        if (tool == null || GunChanged == null) return;
        foreach (Action<DroppableTool> listener in GunChanged.GetInvocationList())
            try { listener(tool); } catch { }
    }
    private static void NotifyCareerChanged(Character character)
    {
        if (character == null || CareerChanged == null) return;
        foreach (Action<Character> listener in CareerChanged.GetInvocationList())
            try { listener(character); } catch { }
    }

    // True from the prefix of a paid-gun Character.Promote call until that same call's finalizer:
    // every other prefix/postfix on the call sees it (their execution order does not matter) and
    // the native tool lifecycle may already be erased. Nested promotions keep it true until the
    // outermost one finishes.
    internal static bool GunPromotionInProgress => _promotionDepth > 0;

    internal static void Tick()
    {
        try
        {
            if (_lastTickFrame == Time.frameCount) return;
            _lastTickFrame = Time.frameCount;
            // Lost authority (online / no world / no biome) leaves the selected state untouched.
            if (!MusketeerAccess.TrackAllowed) return;
            if (!TryContext(out string contextKey, out long world)) return;
            var state = EnsureState(contextKey, world);
            if (state == null) return;
            // A stable save context can retain its state across runtime world replacement.
            // Pending work belongs to the bound root's world, not the mutable state.World.
            if (state.DefenseAttempts > 0 && state.DefenseWorld != world)
                state.DefenseAttempts = 0;
            state.World = world;
            _current = state;
            if (state.Ready && state.DefenseAttempts > 0 && !GunPromotionInProgress
                && (!MusketeerAccess.Enabled || MusketeerAccess.Playing)
                && Time.frameCount > state.DefenseBindFrame)
            {
                state.DefenseAttempts--;
                if (MusketeerDefense.RedistributeAfterBindings()) state.DefenseAttempts = 0;
            }
            if (Time.unscaledTime < _nextSweep) return;
            _nextSweep = Time.unscaledTime + SweepInterval;
            Sweep();
        }
        catch (Exception e) { Log("tick", e); }
    }

    // Registry-only queries. They expose a binding only while the identity is authorized
    // (offline world authority) and the owner still lives in the current world layer; an online
    // session or a foreign layer never leaks another world's bindings. No scene scan, no hashing,
    // no disk access.
    internal static bool IsMarked(GameObject root)
    {
        try
        {
            if (root == null) return false;
            var state = _current;
            if (state == null || !state.Ready) return false;
            if (!Bound.TryGetValue(root.Pointer, out var career) || career == null) return false;
            if (!ReferenceEquals(career.State, state)) return false;
            return Exposed(career);
        }
        catch { return false; }
    }

    internal static bool IsUnit(Archer actor)
    {
        try
        {
            if (actor == null || actor.gameObject == null) return false;
            var state = _current;
            if (state == null || !state.Ready) return false;
            if (!Bound.TryGetValue(actor.gameObject.Pointer, out var career) || career == null) return false;
            if (!ReferenceEquals(career.State, state) || career.Kind != MusketeerCareer.KindUnit) return false;
            if (career.Archer == null || career.Archer.Pointer != actor.Pointer) return false;
            return Exposed(career);
        }
        catch { return false; }
    }

    internal static bool IsGun(DroppableTool tool)
    {
        try
        {
            if (tool == null || tool.gameObject == null) return false;
            var state = _current;
            if (state == null || !state.Ready) return false;
            if (!Bound.TryGetValue(tool.gameObject.Pointer, out var career) || career == null) return false;
            if (!ReferenceEquals(career.State, state) || career.Kind != MusketeerCareer.KindGun) return false;
            if (career.Tool == null || career.Tool.Pointer != tool.Pointer) return false;
            return Exposed(career);
        }
        catch { return false; }
    }

    // Procurement reads only this bounded identity registry. Unknown archive/binding state
    // is never published as zero stock, and a dropped paid gun still covers one career.
    internal static bool TryGetRestockCounts(out int live, out int guns)
    {
        live = guns = 0;
        try
        {
            var state = _current;
            if (!MusketeerAccess.TrackAllowed || state == null || !state.Ready || state.ReadOnly
                || state.Unresolved || !state.HasBaseline || state.Epoch == null
                || state.StockRestores.Count != 0 || state.Careers.Count > MaxRecords
                || !TryContext(out var context, out var world, true)
                || context != state.ContextKey || world != state.World) return false;
            RestockEvidenceContext(state, world);
            var layer = Managers.Inst?.world?.gameLayer;
            if (layer == null || layer.gameObject == null || !layer.gameObject.activeInHierarchy) return false;
            int units = 0, stock = 0;
            foreach (var career in state.Careers)
            {
                // Unbound historical careers are reserved identity, not proof of death.
                if (career == null || !ReferenceEquals(career.State, state)) return false;
                if (!ValidBinding(career))
                {
                    if (career.Slot == null && RestockEnded.TryGetValue(career, out long endedWorld)
                        && endedWorld == world) continue;
                    return false;
                }
                // Unlike UI identity queries, procurement must distinguish a readable foreign/
                // inactive object from a failed native read. Do not call the swallowing Exposed
                // or InWorld helpers here: any access failure invalidates the entire snapshot.
                var root = GameObjectOf(career);
                if (root == null) return false;
                if (!root.activeInHierarchy) continue;
                if (root.scene.handle != layer.gameObject.scene.handle) continue;
                if (root.transform == null) return false;
                if (!root.transform.IsChildOf(layer)) continue;
                if (career.Kind == MusketeerCareer.KindUnit)
                {
                    var damageable = career.Character?._damageable;
                    if (damageable == null || career.Archer == null) return false;
                    if (!damageable.isDead) units++;
                }
                else if (career.Kind == MusketeerCareer.KindGun)
                {
                    var tool = career.Tool;
                    if (tool == null) return false;
                    if (!tool.pickedUp && tool.enemyClaimer == null) stock++;
                }
                else return false;
            }
            live = units; guns = stock;
            return true;
        }
        catch { live = guns = 0; return false; }
    }

    internal static void CopyUnits(List<Archer> destination)
    {
        try
        {
            if (destination == null) return;
            destination.Clear();
            var state = _current;
            if (state == null || !state.Ready || !MusketeerAccess.TrackAllowed) return;
            int added = 0;
            foreach (var career in state.Careers)
            {
                if (career.Kind != MusketeerCareer.KindUnit || career.Archer == null) continue;
                if (!ReferenceEquals(career.State, state) || !Exposed(career)) continue;
                destination.Add(career.Archer);
                if (++added >= MaxRecords) break;
            }
        }
        catch (Exception e) { Log("copy-units", e); }
    }

    internal static void CopyGuns(List<DroppableTool> destination)
    {
        try
        {
            if (destination == null) return;
            destination.Clear();
            var state = _current;
            if (state == null || !state.Ready || !MusketeerAccess.TrackAllowed) return;
            int added = 0;
            foreach (var career in state.Careers)
            {
                if (career.Kind != MusketeerCareer.KindGun || career.Tool == null) continue;
                if (!ReferenceEquals(career.State, state) || !Exposed(career)) continue;
                destination.Add(career.Tool);
                if (++added >= MaxRecords) break;
            }
        }
        catch (Exception e) { Log("copy-guns", e); }
    }

    // Shop gate: a proven context, a writable archive and a running game. The authoritative
    // disk check happens in TryRegisterPaidGun; ReadOnly/Unresolved mirror load/write failures.
    internal static bool CanPurchase
    {
        get
        {
            try
            {
                if (!MusketeerAccess.Playing) return false;
                var state = _current;
                if (state == null || !state.Ready || state.ReadOnly || state.Unresolved || !state.HasBaseline || state.Epoch == null) return false;
                if (state.StockRestores.Count > 0) return false;   // failed rack restore: retry before selling
                if (state.Careers.Count >= MaxRecords) return false;
                if (_contextKey == null || _contextKey != state.ContextKey) return false;
                return true;
            }
            catch { return false; }
        }
    }

    internal static bool HasUnresolved
    {
        get
        {
            try
            {
                var state = _current;
                return state != null && state.Ready && (state.Unresolved || state.StockRestores.Count > 0);
            }
            catch { return false; }
        }
    }

    internal static string StatusText
    {
        get
        {
            try
            {
                if (!MusketeerAccess.TrackAllowed) return "火铳手身份：联机或非权威世界，暂停识别";
                var state = _current;
                if (state == null || !state.Ready) return "火铳手身份：等待岛屿存档上下文";
                if (state.Unresolved) return "火铳手附加档历史待确认，暂停购买";
                if (state.StockRestores.Count > 0) return "火铳手货架枪物理待恢复，暂停购买";
                if (state.ReadOnly) return "火铳手附加档只读，暂停购买";
                if (!state.HasBaseline || state.Epoch == null) return "火铳手身份：等待原生存档加载确认";
                return "火铳手身份跟踪正常（已购 " + state.Careers.Count + " 名/把）";
            }
            catch { return "火铳手身份暂不可用"; }
        }
    }

    // The shop calls this once per native ToolBow it spawned, before the gun can be picked up.
    // stockSlot is the paid rack slot the shop is placing (0..StockSlots-1) or -1 for a plain
    // career registration. False means "despawn it and refund the normal transaction".
    internal static bool TryRegisterPaidGun(DroppableTool tool, int stockSlot = MusketeerCareer.NoStockSlot)
    {
        try
        {
            if (!MusketeerAccess.TrackAllowed) return false;
            if (tool == null || tool.gameObject == null) return false;
            if (stockSlot < MusketeerCareer.NoStockSlot || stockSlot >= StockSlots) return false;
            var state = _current;
            if (state == null || !state.Ready || state.ReadOnly || state.Unresolved || !state.HasBaseline || state.Epoch == null) return false;
            if (!MusketeerAccess.InWorld(tool)) return false;
            if (tool.gameObject.tag != "Bow") return false;
            // A registerable rack gun is one that is still uncollected and unclaimed; otherwise the
            // shop keeps it refundable instead of creating a claim it cannot prove.
            if (stockSlot != MusketeerCareer.NoStockSlot && CollectedOrClaimed(tool)) return false;
            if (Bound.TryGetValue(tool.gameObject.Pointer, out var existing) && existing != null && ReferenceEquals(existing.State, state))
                return existing.Kind == MusketeerCareer.KindGun && existing.Tool != null && existing.Tool.Pointer == tool.Pointer;
            if (state.Careers.Count >= MaxRecords) return false;
            // A rack slot can only hold one live paid gun; a released career keeps no claim.
            if (stockSlot != MusketeerCareer.NoStockSlot)
                foreach (var other in state.Careers)
                    if (other.Kind == MusketeerCareer.KindGun && other.Slot != null && other.StockSlot == stockSlot) return false;
            var disk = MusketeerPersistence.ReadArchive();
            if (!disk.Writable) { state.ReadOnly = true; Log("archive-readonly", null); return false; }
            if (disk.Archive != null)
            {
                if (!disk.Archive.Scopes.ContainsKey(state.Epoch) && disk.Archive.Scopes.Count >= MusketeerArchive.MaxScopes)
                { state.ReadOnly = true; Log("archive-scope-capacity", null); return false; }
                if (!disk.Archive.TryGetContext(state.ContextKey, out _) && disk.Archive.Contexts.Count >= MusketeerArchive.MaxContexts)
                { state.ReadOnly = true; Log("archive-context-capacity", null); return false; }
            }
            // Re-validate after the disk pass; the shop hands the gun on right after this call.
            if (!MusketeerAccess.InWorld(tool) || tool.gameObject.tag != "Bow") return false;
            var slot = RootOf(tool.gameObject, true);
            if (slot == null) return false;
            var career = new Career { Id = Guid.NewGuid(), State = state };
            if (!Bind(career, slot, null, null, tool, stockSlot)) return false;
            state.Careers.Add(career);
            Log(stockSlot == MusketeerCareer.NoStockSlot ? "gun-registered" : "stock-gun-registered", null);
            return true;
        }
        catch (Exception e) { Log("register", e); return false; }
    }

    // Rack slot of a live paid gun; -1 for anything else (unit career, reservation, untracked).
    internal static int StockSlot(DroppableTool tool)
    {
        try
        {
            if (tool == null || tool.gameObject == null) return MusketeerCareer.NoStockSlot;
            if (!TryGetBound(tool.gameObject, out var career) || career.Kind != MusketeerCareer.KindGun) return MusketeerCareer.NoStockSlot;
            return career.StockSlot;
        }
        catch { return MusketeerCareer.NoStockSlot; }
    }

    // Revokes the rack claim: a picked up / stolen / enemy-claimed / dropped gun must never be
    // rescued back onto the paid rack. Identity (career GUID) is untouched, and nothing about the
    // item itself is changed.
    internal static void ReleaseStock(DroppableTool tool)
    {
        try
        {
            if (tool == null || tool.gameObject == null) return;
            if (!TryGetBound(tool.gameObject, out var career) || career.Kind != MusketeerCareer.KindGun) return;
            if (career.StockSlot == MusketeerCareer.NoStockSlot) { DropPendingRestore(career); return; }
            career.StockSlot = MusketeerCareer.NoStockSlot;
            DropPendingRestore(career);
            Log("stock-released", null);
        }
        catch (Exception e) { Log("stock-release", e); }
    }

    // The exact rack proof shared by reconciliation, the authoritative save and the load restore:
    // the gun must still be the live bound owner inside the current world, uncollected and not
    // enemy-claimed. Only metadata decisions are derived from it.
    internal static bool StockClaimProven(Career career)
    {
        try
        {
            if (!InOwnWorld(career)) return false;
            return !CollectedOrClaimed(career.Tool);
        }
        catch { return false; }
    }

    // A gun claimed by a resident/enemy or already collected: never rack stock again. The native
    // pickup state is only read, never undone.
    private static bool CollectedOrClaimed(DroppableTool tool)
    {
        try
        {
            if (tool == null || tool.gameObject == null) return true;
            if (tool.pickedUp) return true;
            if (tool.enemyClaimer != null) return true;
            return false;
        }
        catch { return true; }
    }

    // Authorized, current-world, live-bound gun: the only situation in which rack metadata may be
    // decided. Online sessions and foreign layers are left completely untouched.
    private static bool InOwnWorld(Career career)
    {
        try
        {
            if (career == null || career.Kind != MusketeerCareer.KindGun) return false;
            if (career.Slot == null || !ValidBinding(career)) return false;
            if (!MusketeerAccess.TrackAllowed) return false;
            var tool = career.Tool;
            if (tool == null || tool.gameObject == null) return false;
            return MusketeerAccess.InWorld(tool);
        }
        catch { return false; }
    }

    internal static void RevokeStock(Career career, string logKey)
    {
        if (career == null || career.StockSlot == MusketeerCareer.NoStockSlot) return;
        career.StockSlot = MusketeerCareer.NoStockSlot;
        Log(logKey, null);
    }

    // Restores only the physics flags of one exactly proven rack gun: kinematic at rest. The
    // frozen native position is never touched and no other object is inspected. False means the
    // caller keeps responsibility (pending retry) - never a silent success. The single-argument
    // view keeps the load path's existing contract; the reconciliation uses the bucketed form.
    internal static bool TryRestoreStockPhysics(Career career) => TryRestoreStockPhysics(career, out _);

    /// <summary>Bucketed retry: failure names why the restore did not happen (not-in-world /
    /// body-missing / write-exception) so the bounded give-up log can say which surface to look
    /// at. An unprovable claim revokes itself here (stock-unproven) and is not a retry candidate.</summary>
    internal static bool TryRestoreStockPhysics(Career career, out string failure)
    {
        failure = null;
        try
        {
            if (career == null || career.StockSlot == MusketeerCareer.NoStockSlot) { failure = "not-in-world"; return false; }
            if (!InOwnWorld(career)) { failure = "not-in-world"; return false; }
            var tool = career.Tool;   // defensive depth only: InOwnWorld already proved the live binding
            if (tool == null || tool.gameObject == null) { failure = "not-in-world"; return false; }
            if (CollectedOrClaimed(tool)) { RevokeStock(career, "stock-unproven"); failure = "unproven"; return false; }
            var body = tool.GetComponent<Rigidbody2D>();
            if (body == null) { Log("stock-physics-missing-body", null); failure = "body-missing"; return false; }
            body.isKinematic = true;
            body.velocity = Vector2.zero;
            return true;
        }
        catch (Exception e) { Log("stock-physics", e); failure = "write-exception"; return false; }
    }

    // Reserved careers that still carry a rack claim while no live binding proves them (the H3'
    // signature): a release must clear StockSlot, and a load must bind the row, because every
    // rack read fails closed while such a claim exists. Shop diagnostics and tests read this
    // probe; it is pure registry state, never a scene read.
    internal static int ResidualStockClaims(IslandState state)
    {
        if (state == null) return 0;
        int count = 0;
        foreach (var career in state.Careers)
            if (career != null && career.Kind == MusketeerCareer.KindGun
                && career.StockSlot != MusketeerCareer.NoStockSlot && career.Slot == null) count++;
        return count;
    }

    // Failed rack restores stay owned: the claim is kept, charges are blocked and the bounded
    // reconciliation retries the same proven stock at the next Tick. After
    // StockRestoreAbandonAttempts failed retries the claim is given up instead of blocking
    // purchases forever (the paid career itself is never touched).
    internal static void MarkStockRestorePending(IslandState state, Career career)
    {
        if (state == null || career == null) return;
        foreach (var existing in state.StockRestores)
            if (ReferenceEquals(existing.Career, career)) return;
        if (state.StockRestores.Count >= MusketeerArchive.MaxStockSlots) return;
        state.StockRestores.Add(new StockRestore { Career = career });
        Log("stock-physics-pending", null);
    }

    private static void DropPendingRestore(Career career)
    {
        if (career == null || career.State == null) return;
        for (int i = career.State.StockRestores.Count - 1; i >= 0; i--)
            if (ReferenceEquals(career.State.StockRestores[i].Career, career)) career.State.StockRestores.RemoveAt(i);
    }

    // Failed-creation cleanup only: the shop despawns an unclaimed gun it just had registered.
    // A career with a witnessed native id, or one already transferred, is never forgotten.
    internal static void ForgetUnpaidGun(DroppableTool tool)
    {
        try
        {
            if (tool == null || tool.gameObject == null) return;
            if (!Bound.TryGetValue(tool.gameObject.Pointer, out var career) || career == null) return;
            if (career.Kind != MusketeerCareer.KindGun || career.Tool == null || career.Tool.Pointer != tool.Pointer) return;
            if (career.NativeId.Length > 0) return;
            career.State?.Careers.Remove(career);
            Release(career);
            Log("gun-forgotten", null);
        }
        catch (Exception e) { Log("forget", e); }
    }

    // ---- native lifecycle bridges (patched in this file, directly drivable in tests) ----

    // Prefix of Character.Promote(DroppableTool, IUnitController): snapshot the career GUID, the
    // exact gun root and its life before the native body runs its nested despawns. Unmarked tools
    // (plain bows, other mods' items) engage nothing and never touch the shared scope flag.
    internal static void OnGunPickupBegin(DroppableTool tool, out PromotionState state)
    {
        state = default;
        try
        {
            if (!MusketeerAccess.TrackAllowed) return;
            if (tool == null || tool.gameObject == null) return;
            if (!TryGetBound(tool.gameObject, out var career)) return;
            if (career.Kind != MusketeerCareer.KindGun) return;
            state = new PromotionState
            {
                Career = career,
                ToolPointer = tool.gameObject.Pointer,
                ToolLife = career.Life,
                Engaged = true
            };
            _promotionDepth++;
        }
        catch (Exception e) { state = default; Log("promote-begin", e); }
    }

    // Postfix: transfer the captured career onto the returned successor unit, never onto a
    // Peasant and never by guessing. A career still bound to the exact captured gun (or already
    // consumed by that gun's confirmed despawn inside the call) is eligible; a career that moved
    // to another owner is left alone, and a reused pool root is never re-marked.
    internal static void OnGunPickupEnd(Character result, PromotionState state)
    {
        if (!state.Engaged || state.Career == null) return;
        var career = state.Career;
        try
        {
            if (career.State == null || !career.State.Careers.Contains(career)) { Log("promote-detached", null); return; }
            if (career.Slot != null && (career.Slot.Pointer != state.ToolPointer || career.Life != state.ToolLife)) { Log("promote-owner-changed", null); return; }
            if (result == null || result.gameObject == null) { Release(career); Log("promote-lost", null); return; }
            var archer = result.GetComponent<Archer>();
            if (archer == null) { Release(career); Log("promote-not-archer", null); return; }
            var slot = RootOf(result.gameObject, true);
            if (!Bind(career, slot, result, archer, null, MusketeerCareer.NoStockSlot)) { Release(career); Log("promote-bind-failed", null); return; }
            Log("gun->unit", null);
        }
        catch (Exception e) { Release(career); Log("promote-end", e); }
    }

    // Finalizer: closes exactly the scope this call opened. The prefix/postfix never clear it.
    internal static void OnGunPickupAbort(PromotionState state)
    {
        if (!state.Engaged) return;
        if (_promotionDepth > 0) _promotionDepth--;
    }

    // Droppable.Drop long hook (the 7-parameter native overload, the one Character.DropItem calls
    // for the tool it spawned). Two exact decisions happen here, in this order:
    //  * a real drop of a tracked rack gun revokes its rack claim, so a gun knocked off / dropped
    //    outside creation is never restored as paid stock;
    //  * a drop whose native dropper is still a bound paid unit hands the career to the exact
    //    dropped tool (an ordinary gun, no rack claim) and ends the old unit identity.
    // Character.DropItem itself is deliberately not detoured (see the note where its patch used
    // to live): the HarmonyX IL2CPP trampoline cannot marshal its null Nullable<Vector2>.
    internal static void OnDroppableDrop(Droppable droppable, GameObject source = null)
    {
        try
        {
            if (droppable == null || droppable.gameObject == null) return;
            var tool = droppable.GetComponent<DroppableTool>();
            if (tool == null) return;
            ReleaseStock(tool);
            TryTransferUnitDrop(droppable, tool, source);
        }
        catch (Exception e) { Log("drop-stock", e); }
    }

    // Unit -> gun transfer, driven by the exact native drop call: dropper is the unit object the
    // native code passed to Droppable.Drop and droppable is the exact object that call drops.
    // Only a paid, still-bound unit career and a native Bow move; every other drop (no dropper,
    // foreign/plain unit, non-bow tool, object already carrying a career) is left untouched.
    private static void TryTransferUnitDrop(Droppable droppable, DroppableTool tool, GameObject dropper)
    {
        if (!MusketeerAccess.TrackAllowed) return;
        if (dropper == null) return;
        // The argument belongs to THIS call. Droppable.dropper can retain an older
        // owner when a subsequent native Drop passes null, so never infer from it.
        if (!MusketeerAccess.InWorld(tool)) return;
        if (Bound.ContainsKey(droppable.gameObject.Pointer)) return;
        if (!TryGetBound(dropper, out var career)) return;
        if (career.Kind != MusketeerCareer.KindUnit) return;
        if (droppable.gameObject.tag != "Bow") return;
        var slot = RootOf(droppable.gameObject, true);
        if (!Bind(career, slot, null, null, tool, MusketeerCareer.NoStockSlot)) { Release(career); Log("drop-bind-failed", null); return; }
        Log("unit->gun", null);
    }

    // Pool.FastSpawn postfix: a tracked root that was confirmed despawned starts a new life.
    // An already-active object (sync receipt path) is not a new life and is left untouched.
    internal static void OnPoolSpawn(GameObject root)
    {
        try
        {
            if (root == null) return;
            if (!Roots.TryGetValue(root.Pointer, out var slot) || slot == null || slot.Alive) return;
            int goId = SafeInstanceId(root);
            if (goId != 0) slot.GoId = goId;
            slot.Alive = true;
            slot.Life = ++_nextLife;
            slot.World = WorldKey();
        }
        catch (Exception e) { Log("pool-spawn", e); }
    }

    internal static void OnPoolDespawnBegin(GameObject root, float delay, out PoolDespawnState state)
    {
        state = default;
        try
        {
            if (delay > 0f) return;   // a delayed request only schedules; the immediate call is the boundary
            if (root == null) return;
            if (!Roots.TryGetValue(root.Pointer, out var slot) || slot == null || !slot.Alive) return;
            state = new() { Root = root, Pointer = root.Pointer, Tracked = true };
            if (!IslandSaveData.isSavingGame && MusketeerAccess.Playing
                && Bound.TryGetValue(root.Pointer, out var career) && career != null
                && ReferenceEquals(career.State, _current) && Exposed(career)
                && career.State.World == WorldKey())
            {
                RestockEvidenceContext(career.State, career.State.World);
                state.RestockCareer = career; state.RestockLife = career.Life;
                state.RestockWorld = career.State.World;
            }
        }
        catch (Exception e) { Log("pool-despawn-begin", e); }
    }

    internal static void OnPoolDespawnEnd(PoolDespawnState state)
    {
        try
        {
            if (!state.Tracked) return;
            bool gone;
            bool endObserved = false;
            try { gone = state.Root == null || !state.Root.activeInHierarchy; endObserved = true; }
            catch { gone = true; }
            if (!gone) return;   // native refused/aborted the recycling: ownership stays
            if (!Roots.TryGetValue(state.Pointer, out var slot) || slot == null) return;
            slot.Alive = false;
            if (Bound.TryGetValue(state.Pointer, out var career) && career != null)
            {
                if (endObserved && ReferenceEquals(state.RestockCareer, career) && career.Life == state.RestockLife
                    && ReferenceEquals(career.State, _current) && state.RestockWorld == WorldKey()
                    && !IslandSaveData.isSavingGame && MusketeerAccess.Playing)
                    RestockEnded[career] = state.RestockWorld;
                Release(career);
            }
        }
        catch (Exception e) { Log("pool-despawn-end", e); }
    }

    // ---- persistence-facing model (MusketeerPersistence owns the sidecar and captures) ----

    internal static IslandState EnsureState(string contextKey, long world)
    {
        if (contextKey == null) return null;
        if (Islands.TryGetValue(contextKey, out var state) && state != null) return state;
        if (Islands.Count >= MusketeerArchive.MaxContexts) return null;
        state = MusketeerPersistence.CreateState(contextKey, world, null, false);
        if (state == null) return null;
        Islands.Add(contextKey, state);
        return state;
    }

    internal static void InstallState(string contextKey, IslandState state)
    {
        if (contextKey == null || state == null) return;
        Islands[contextKey] = state;
        _current = state;
    }

    internal static void RemoveState(string contextKey)
    {
        if (contextKey != null) Islands.Remove(contextKey);
    }

    internal static void SetCurrent(IslandState state) => _current = state;

    internal static void InvalidateContextCache()
    {
        _contextFrame = -1; _lastTickFrame = -1;
    }

    // State-scoped binding lookup used by the save capture (which may run for a non-current
    // island); the current-state query paths keep their own readiness gate.
    internal static Career BoundAt(IslandState state, GameObject root)
    {
        try
        {
            if (state == null || root == null) return null;
            if (!Bound.TryGetValue(root.Pointer, out var career) || career == null) return null;
            if (!ReferenceEquals(career.State, state)) return null;
            return ValidBinding(career) ? career : null;
        }
        catch { return null; }
    }

    internal static Career AddCareer(IslandState state, MusketeerCareer record)
    {
        var career = new Career
        {
            Id = record.Id,
            Kind = record.Kind,
            NativeId = record.NativeId ?? "",
            StockSlot = record.StockSlot,
            State = state
        };
        state.Careers.Add(career);
        return career;
    }

    internal static Career FindByNativeId(IslandState state, string nativeId)
    {
        if (state == null || string.IsNullOrEmpty(nativeId)) return null;
        foreach (var career in state.Careers)
            if (career.NativeId == nativeId) return career;
        return null;
    }

    // Exact native id restore during the load; null/zero-id rows are never guessed an owner and
    // mismatching kinds are refused instead of re-labelled.
    internal static bool CaptureLoadRow(IslandState state, string nativeId, Persistent root, out string failure)
    {
        failure = null;
        if (state == null || root == null || string.IsNullOrEmpty(nativeId)) return false;
        var career = FindByNativeId(state, nativeId);
        if (career == null) return false;
        if (career.Slot != null) { failure = "row-bound-twice"; return false; }
        var character = root.GetComponent<Character>();
        if (character != null)
        {
            if (career.Kind != MusketeerCareer.KindUnit) { failure = "row-kind-unit"; return false; }
            var archer = root.GetComponent<Archer>();
            if (archer == null) { failure = "row-no-archer"; return false; }
            var slot = RootOf(root.gameObject, true);
            if (!Bind(career, slot, character, archer, null, MusketeerCareer.NoStockSlot)) { failure = "row-bind-unit"; return false; }
            LogBindCanary();
            return true;
        }
        var tool = root.GetComponent<DroppableTool>();
        if (tool != null)
        {
            if (career.Kind != MusketeerCareer.KindGun) { failure = "row-kind-gun"; return false; }
            var slot = RootOf(root.gameObject, true);
            if (!Bind(career, slot, null, null, tool, career.StockSlot)) { failure = "row-bind-gun"; return false; }
            LogBindCanary();
            return true;
        }
        failure = "row-no-identity";
        return false;
    }

    internal static bool TryGetBound(GameObject root, out Career career)
    {
        career = null;
        try
        {
            if (root == null || !MusketeerAccess.TrackAllowed) return false;
            var state = _current;
            if (state == null || !state.Ready) return false;
            if (!Bound.TryGetValue(root.Pointer, out career) || career == null) return false;
            if (!ReferenceEquals(career.State, state)) { career = null; return false; }
            if (!ActiveBinding(career)) { career = null; return false; }
            return true;
        }
        catch { career = null; return false; }
    }

    // The single binding point: one career, one root, one live identity. Kind is derived from the
    // bound components so a gun->unit->gun round trip always reports the truth, and a unit can
    // never carry rack metadata.
    internal static bool Bind(Career career, RootSlot slot, Character character, Archer archer, DroppableTool tool, int stockSlot)
    {
        if (career == null || slot == null || !slot.Alive) return false;
        if (Bound.TryGetValue(slot.Pointer, out var existing) && existing != null && !ReferenceEquals(existing, career)) return false;
        if (career.Slot != null) Release(career);
        career.Kind = character != null ? MusketeerCareer.KindUnit : MusketeerCareer.KindGun;
        career.StockSlot = career.Kind == MusketeerCareer.KindUnit ? MusketeerCareer.NoStockSlot : stockSlot;
        career.Slot = slot;
        career.Life = slot.Life;
        career.GoId = slot.GoId;
        career.Character = character;
        career.Archer = archer;
        career.Tool = tool;
        Bound[slot.Pointer] = career;
        RestockEnded.Remove(career);
        if (archer != null && career.State != null)
        {
            career.State.DefenseAttempts = 3;
            career.State.DefenseBindFrame = Time.frameCount;
            career.State.DefenseWorld = slot.World;
        }
        NotifyCareerChanged(character);
        NotifyGunChanged(tool);
        return true;
    }

    internal static void Release(Career career)
    {
        if (career == null) return;
        Character previous = career.Character;
        DroppableTool previousTool = career.Tool;
        // A released gun keeps no rack claim (H3'): the claim describes one live rack position, and
        // a reservation that still carried a slot made every later rack read fail closed for the
        // rest of the session (empty shelf, silent lock). Its pending physics restore goes with
        // it; the sweep's unproven-claim pass stays as the backstop for anything missed.
        if (career.Kind == MusketeerCareer.KindGun)
        {
            career.StockSlot = MusketeerCareer.NoStockSlot;
            DropPendingRestore(career);
        }
        if (career.Slot != null)
        {
            if (Bound.TryGetValue(career.Slot.Pointer, out var live) && ReferenceEquals(live, career)) Bound.Remove(career.Slot.Pointer);
            career.Slot = null;
        }
        career.Character = null; career.Archer = null; career.Tool = null;
        NotifyCareerChanged(previous);
        NotifyGunChanged(previousTool);
    }

    internal static void ReleaseAll(IslandState state)
    {
        if (state == null) return;
        foreach (var career in state.Careers) Release(career);
    }

    // Live binding validation without any active-state requirement: a temporarily hidden unit
    // keeps its career (native disable is not a deletion); only proven ends release it.
    internal static bool ValidBinding(Career career)
    {
        try
        {
            if (career == null || career.Slot == null || !career.Slot.Alive) return false;
            if (career.Slot.Life != career.Life || career.Slot.GoId != career.GoId) return false;
            if (career.Slot.World != 0 && career.State != null && career.State.World != 0 && career.Slot.World != career.State.World) return false;
            if (!Bound.TryGetValue(career.Slot.Pointer, out var live) || !ReferenceEquals(live, career)) return false;
            var go = GameObjectOf(career);
            if (go == null || go.Pointer != career.Slot.Pointer) return false;
            if (go.GetInstanceID() != career.GoId) return false;
            return true;
        }
        catch { return false; }
    }

    internal static RootSlot RootOf(GameObject root, bool create)
    {
        if (root == null) return null;
        IntPtr pointer = root.Pointer;
        int goId = SafeInstanceId(root);
        if (Roots.TryGetValue(pointer, out var slot) && slot != null)
        {
            if (goId != 0 && slot.GoId != goId)
            {
                // The address now hosts a different GameObject instance: the old life is over.
                if (Bound.TryGetValue(pointer, out var bound) && bound != null) Release(bound);
                slot.GoId = goId; slot.Alive = true; slot.Life = ++_nextLife; slot.World = WorldKey();
            }
            else if (!slot.Alive && create)
            {
                slot.Alive = true; slot.Life = ++_nextLife; slot.World = WorldKey();
            }
            return slot;
        }
        if (!create || Roots.Count >= MaxRecords) return null;
        slot = new RootSlot { Pointer = pointer, GoId = goId, Alive = true, Life = ++_nextLife, World = WorldKey() };
        Roots[pointer] = slot;
        return slot;
    }

    internal static bool TryContext(out string contextKey, out long world, bool fresh = false)
    {
        contextKey = null; world = 0;
        try
        {
            if (!MusketeerAccess.TrackAllowed) return false;
            if (!fresh && _contextFrame == Time.frameCount) { contextKey = _contextKey; world = _contextWorld; return contextKey != null && world != 0; }
            var global = GlobalSaveData.loaded;
            var campaign = CampaignSaveData.current;
            var island = campaign != null ? campaign.CurrentIsland : null;
            if (global == null || island == null) return false;
            world = WorldKey();
            bool valid = world != 0 && TryContextKey(global.currentCampaign, global.currentChallenge, island.land, out contextKey);
            if (valid) { _contextKey = contextKey; _contextWorld = world; _contextFrame = Time.frameCount; }
            return valid;
        }
        catch { return false; }
    }

    // Stable context identity for one island lineage; runtime DateTime ticks and instance ids
    // are deliberately absent (the game recreates them on every load).
    internal static bool TryContextKey(int campaign, int challenge, int land, out string contextKey)
    {
        contextKey = null;
        try
        {
            if (land < 0) return false;
            string file = GlobalSaveData.filename;
            if (string.IsNullOrEmpty(file) || file.Length > 256) return false;
            contextKey = MusketeerArchive.ContextKey(file, campaign, challenge, land);
            return true;
        }
        catch { return false; }
    }

    internal static long WorldKey()
    {
        try
        {
            var layer = Managers.Inst?.world?.gameLayer;
            return layer != null && layer.gameObject.activeInHierarchy ? layer.Pointer.ToInt64() : 0;
        }
        catch { return 0; }
    }

    internal static string DescribeForTests()
    {
        var state = _current;
        if (state == null) return "none";
        int bound = 0, reserved = 0, stock = 0;
        foreach (var career in state.Careers)
        {
            if (career.Slot != null)
            {
                bound++;
                if (career.Kind == MusketeerCareer.KindGun && career.StockSlot != MusketeerCareer.NoStockSlot) stock++;
            }
            else reserved++;
        }
        return "ctx=" + Short(state.ContextKey) + " epoch=" + Short(state.Epoch) + " kind=" + state.MatchKind
            + " records=" + state.Careers.Count + " bound=" + bound + " reserved=" + reserved + " stock=" + stock
            + " stockPending=" + state.StockRestores.Count
            + " baseline=" + state.HasBaseline + " unresolved=" + state.Unresolved + " readonly=" + state.ReadOnly;
    }

    // Bounded reconciliation, run from Tick every 2 seconds: release dead bindings, revoke rack
    // claims that a native pickup/enemy claim has invalidated, and retry failed rack physics
    // restores. It iterates only our own registry, never the scene.
    private static void Sweep()
    {
        foreach (var state in Islands.Values)
        {
            for (int i = state.Careers.Count - 1; i >= 0; i--)
            {
                var career = state.Careers[i];
                if (career.Slot != null && !ValidBinding(career)) Release(career);
            }
            for (int i = state.Careers.Count - 1; i >= 0; i--)
            {
                var career = state.Careers[i];
                if (career.StockSlot == MusketeerCareer.NoStockSlot) continue;
                if (!InOwnWorld(career)) continue;                       // never touch online/foreign state
                if (CollectedOrClaimed(career.Tool)) RevokeStock(career, "stock-unproven");
            }
            for (int i = state.StockRestores.Count - 1; i >= 0; i--)
            {
                var restore = state.StockRestores[i];
                var career = restore.Career;
                if (career == null || career.Slot == null || !ValidBinding(career) || career.StockSlot == MusketeerCareer.NoStockSlot)
                { state.StockRestores.RemoveAt(i); continue; }
                restore.Attempts++;
                if (TryRestoreStockPhysics(career, out string failure))
                { state.StockRestores.RemoveAt(i); Log("stock-physics-recovered", null); }
                else if (career.StockSlot == MusketeerCareer.NoStockSlot)
                { state.StockRestores.RemoveAt(i); }   // claim disproven inside the retry: nothing left to restore
                else if (restore.Attempts >= StockRestoreAbandonAttempts)
                {
                    // Bounded give-up: revoke only the rack claim so the shop sells again. The paid
                    // career, its gun and the binding stay untouched; the bucket names the cause.
                    state.StockRestores.RemoveAt(i);
                    RevokeStock(career, "stock-restore-abandoned-" + failure);
                }
                else if (restore.Attempts == 8) Log("stock-physics-retry", null);
            }
        }
        List<IntPtr> stale = null;
        foreach (var pair in Roots)
        {
            if (pair.Value.Alive || Bound.ContainsKey(pair.Key)) continue;
            (stale ??= new List<IntPtr>()).Add(pair.Key);
        }
        if (stale == null) return;
        foreach (var key in stale) Roots.Remove(key);
    }

    // Current-world, authorized exposure: the query APIs never leak a binding that belongs to an
    // online session, a foreign scene/layer, or an inactive (pooled) object.
    private static bool Exposed(Career career)
    {
        if (!MusketeerAccess.TrackAllowed) return false;
        if (!ActiveBinding(career)) return false;
        var go = GameObjectOf(career);
        return go != null && MusketeerAccess.InWorld(go);
    }

    private static bool ActiveBinding(Career career)
    {
        if (!ValidBinding(career)) return false;
        try
        {
            var go = GameObjectOf(career);
            return go != null && go.activeInHierarchy;
        }
        catch { return false; }
    }

    private static GameObject GameObjectOf(Career career)
    {
        if (career == null) return null;
        if (career.Kind == MusketeerCareer.KindUnit) return career.Character != null ? career.Character.gameObject : null;
        return career.Tool != null ? career.Tool.gameObject : null;
    }

    private static int SafeInstanceId(GameObject root)
    {
        try { return root.GetInstanceID(); } catch { return 0; }
    }

    private static void LogBindCanary()
    {
        if (_bindCanary) return;
        _bindCanary = true;
        Log("load-bind-first", null);
    }

    private static string Short(string value) => string.IsNullOrEmpty(value) || value.Length < 8 ? "" : value.Substring(0, 8);

    internal static void Log(string key, Exception error)
    {
        if (key == null || Logged.Count >= MaxLoggedKeys || !Logged.Add(key)) return;
        try
        {
            string text = key;
            if (error != null)
            {
                text += ": " + error.GetType().Name + ": " + error.Message;
                if (text.Length > MaxLogText) text = text.Substring(0, MaxLogText);
            }
            KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[Musketeer] " + text);
        }
        catch { }
    }
}

// Character.Promote(DroppableTool, IUnitController): the native boundary where a resident
// converts by picking up a tool. The prefix runs before any nested despawn, so the career GUID
// and root life are snapshotted by the exact gun instance; Harmony threads that per-call state
// into the postfix (transfer to the exact returned unit) and the finalizer (close this call's
// GunPromotionInProgress scope). Other patches on the same call see the flag until then,
// whatever their execution order.
[HarmonyPatch(typeof(Character), nameof(Character.Promote), new[] { typeof(DroppableTool), typeof(IUnitController) })]
internal static class Musketeer_GunPromotion_Patch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Before(DroppableTool tool, out MusketeerIdentity.PromotionState __state)
    { MusketeerIdentity.OnGunPickupBegin(tool, out __state); }

    [HarmonyPostfix, HarmonyPriority(Priority.First)]
    private static void After(Character __result, MusketeerIdentity.PromotionState __state)
    { MusketeerIdentity.OnGunPickupEnd(__result, __state); }

    [HarmonyFinalizer]
    private static Exception Finally(Exception __exception, MusketeerIdentity.PromotionState __state)
    {
        MusketeerIdentity.OnGunPickupAbort(__state);
        return __exception;
    }
}

// Character.DropItem is deliberately NOT detoured. Its only parameter is
// Il2CppSystem.Nullable<Vector2> and the native call sites pass an empty Nullable (Grab and
// the death path call DropItem(null)), which HarmonyX's IL2CPP detour wrapper cannot marshal:
// the copied interop stub converts the managed-null argument through
// Il2CppObjectBaseToPtrNotNull, which throws NullReferenceException before the native body runs,
// so the detour both logged NREs and silently swallowed every native bow drop. The unit -> gun
// transfer is observed at Droppable.Drop instead (the exact native call DropItem performs).

// Droppable.Drop: the exact 2.4 overload every real tool drop goes through (Character.DropItem
// calls it for the tool it spawned). Revokes the rack claim of a tracked gun, and transfers a
// paid unit career onto the exact dropped tool when the native dropper is that unit (the
// protected OnPrepareDrop is not distinguishable from other droppables anyway).
[HarmonyPatch(typeof(Droppable), "Drop", new[]
{
    typeof(GameObject), typeof(Vector2), typeof(Vector2), typeof(PickUpPolicy), typeof(bool), typeof(bool), typeof(bool)
})]
internal static class Musketeer_DroppedStock_Patch
{
    [HarmonyPostfix]
    private static void After(Droppable __instance, GameObject __0) { MusketeerIdentity.OnDroppableDrop(__instance, __0); }
}

// Pool.FastSpawn is the only path out of a pool: a tracked root that was confirmed despawned
// starts a new life. Pool.FastDespawn (immediate) is the only confirmed end boundary.
[HarmonyPatch(typeof(Pool), nameof(Pool.FastSpawn), new[] { typeof(Vector3), typeof(Quaternion), typeof(Transform), typeof(short), typeof(bool) })]
internal static class Musketeer_PoolSpawnLife_Patch
{
    [HarmonyPostfix]
    private static void After(GameObject __result) { MusketeerIdentity.OnPoolSpawn(__result); }
}

[HarmonyPatch(typeof(Pool), nameof(Pool.FastDespawn), new[] { typeof(GameObject), typeof(float), typeof(bool) })]
internal static class Musketeer_PoolDespawnLife_Patch
{
    [HarmonyPrefix]
    private static void Before(GameObject __0, float __1, out MusketeerIdentity.PoolDespawnState __state)
    { MusketeerIdentity.OnPoolDespawnBegin(__0, __1, out __state); }

    [HarmonyPostfix]
    private static void After(MusketeerIdentity.PoolDespawnState __state) { MusketeerIdentity.OnPoolDespawnEnd(__state); }
}
