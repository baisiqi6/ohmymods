using System;
using System.Collections.Generic;
using System.Linq;
#if !ANDROID
using ModDataPaths = BepInEx.Paths;
#endif
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

internal enum HeroShopSeatState { Unavailable, Available, Occupied, Reserved }

// Purchase ownership is independent of the enabled visual/combat state. Only a verified death
// releases a seat. Unproven pool/role/scene transitions reserve the seat without guessing an owner.
internal static class HeroRecruitment
{
    private const int MaxCandidates = 512;
    private sealed class Candidate
    {
        internal Character Character;
        internal Archer Actor => Character != null ? Character.GetComponent<Archer>() : null;
        internal IntPtr Pointer;
        internal IntPtr Root;
        internal int GoId;
        internal long Life;
        internal long World;
        internal float Seen;
        // Borrowed embarkee protection for this candidate life. It is released by an explicit
        // seat removal, by the recycle anchor, or when the object is provably gone; the record
        // itself is not retired while the claim is unsettled.
        internal EmbarkeeClaim Embarkee;
    }

    // One borrowed Embarkee component: exact identity (component pointer + owning root/goId +
    // candidate life) plus the enabled value read before our write. Owned = we wrote false and
    // still hold the responsibility to restore it; a state that was already disabled is never
    // claimed. The receipt stays on the candidate record, so seat removal alone does not end it.
    private sealed class EmbarkeeClaim
    {
        internal IntPtr Pointer;
        internal IntPtr Root;
        internal int GoId;
        internal long Life;
        internal bool OriginalEnabled;
        internal bool Owned;
        internal bool Busy;
        internal bool PendingReturn;
    }
    private sealed class Seat
    {
        internal HeroPurchaseReceipt Receipt;
        internal Candidate Owner;
        internal Damageable Damage;
        internal Damageable.DeathEvent Death;
    }
    private sealed class IslandState
    {
        internal string ContextKey;   // stable island identity (file + campaign/challenge + land)
        internal string Epoch;        // resolved epoch scope used for snapshots; null = nothing proven
        internal string MatchKind = "none";
        internal string MatchHash;    // exact matched snapshot: stored hash/kind/provenance drive the baseline
        internal int MatchHashKind;
        internal bool MatchLegacy;
        internal long World;
        internal readonly List<Seat> Seats = new(2);
        internal bool Ready;
        internal bool ReadOnly;
        internal bool Unresolved;     // fail closed: no charge, no baseline write, no snapshot
        internal bool NewEpoch;       // epoch must be appended to the context history
        internal IntPtr VirginIsland; // once-guard for repeated ApplyToScene of one generation
        internal long VirginWorld;
        internal bool HasBaseline;
        internal bool NativeAuthority;
        internal bool SessionRebind; // this re-entry kept the session's own receipts; bindings carry over
        internal bool HasSession;    // seat set came from a resolved snapshot; an empty set is evidence
        internal IntPtr SourceGlobal; // frozen exact source: Global object + native owner + island slot
        internal IntPtr SourceOwner;
        internal int SourceSlot = -1;
        internal int FallenSeatMask; // Session-only torn-flag feedback; never purchase authority.
    }
    private static readonly Dictionary<IntPtr, Candidate> Candidates = new();
    private static readonly Dictionary<string, IslandState> Islands = new(StringComparer.Ordinal);

    // ---- runtime state source identity --------------------------------------------------------
    // One runtime IslandState belongs to one exact source: the Global object it was created under,
    // the native campaign/challenge owner and the island slot. Numeric context keys are lookup
    // aliases only (they move with catalog slots and never prove ownership); MergeSessionRebind
    // may only use a state whose source matches exactly.
    private static bool SameSource(IslandState state, IntPtr global, IntPtr owner, int islandSlot)
        => state != null && state.SourceGlobal != IntPtr.Zero && state.SourceGlobal == global
            && state.SourceOwner == owner && state.SourceSlot == islandSlot;

    private static IslandState FindSourceState(IntPtr global, IntPtr owner, int islandSlot)
    {
        foreach (var pair in Islands)
            if (SameSource(pair.Value, global, owner, islandSlot)) return pair.Value;
        return null;
    }

    // Finds this exact source's state wherever it lives and installs it under the given lookup
    // alias (moving it when a catalog slot move left it under a stale key). The alias metadata
    // must follow the moved slot: the receipt/GUID/epoch identity is untouched, and the next
    // Tick sees the state under the current key instead of re-entering this migration every
    // frame (which also kept FindPurchase's fresh-key check and the per-frame seat queries
    // locked to a stale key).
    private static IslandState StateForSource(string alias, IntPtr global, IntPtr owner, int islandSlot)
    {
        var match = FindSourceState(global, owner, islandSlot);
        if (match == null) return null;
        RemoveState(match);
        InstallState(alias, match);
        match.ContextKey = alias;
        return match;
    }

    // Installs a state under its current lookup alias without discarding another live source's
    // evidence: an occupant from the same Global is kept under its own private alias. A parked
    // state from a replaced Global could never be looked up again and is dropped like before.
    private static void InstallState(string alias, IslandState state)
    {
        if (Islands.TryGetValue(alias, out var occupant) && !ReferenceEquals(occupant, state)
            && occupant.SourceOwner != IntPtr.Zero
            && occupant.SourceGlobal == GlobalSaveData._loaded?.Pointer)
            Islands["@" + occupant.SourceOwner.ToInt64() + ":" + occupant.SourceSlot] = occupant;
        Islands[alias] = state;
    }

    private static void RemoveState(IslandState state)
    {
        if (state == null) return;
        string location = null;
        foreach (var pair in Islands)
            if (ReferenceEquals(pair.Value, state)) { location = pair.Key; break; }
        if (location != null) Islands.Remove(location);
    }

    // The current source's exact identity, recomputed only when the loaded Global, its current
    // indices, the current island object, the save file or the catalog mapping generation change.
    private static IntPtr _sourceOwner;
    private static IntPtr _sourceGlobal, _sourceIsland;
    private static string _sourceFile;
    private static int _sourceCampaign, _sourceChallenge, _sourceSlot = -1, _sourceCatalog = -1;
    private static bool _sourceValid, _sourceProven;

    private static bool TrySourceIdentity(out IntPtr global, out IntPtr owner, out int islandSlot)
    {
        global = IntPtr.Zero; owner = IntPtr.Zero; islandSlot = -1;
        var value = GlobalSaveData._loaded;
        var campaign = CampaignSaveData.current;
        var island = campaign != null ? campaign.CurrentIsland : null;
        if (value == null || campaign == null || island == null) { _sourceValid = false; return false; }
        if (_sourceValid && _sourceGlobal == value.Pointer && _sourceIsland == island.Pointer
            && _sourceCampaign == value.currentCampaign && _sourceChallenge == value.currentChallenge
            && _sourceFile == GlobalSaveData.filename && _sourceCatalog == HeroNativeRights.CatalogGeneration)
        { global = _sourceGlobal; owner = _sourceOwner; islandSlot = _sourceSlot; return _sourceProven; }
        _sourceGlobal = value.Pointer; _sourceIsland = island.Pointer; _sourceCampaign = value.currentCampaign;
        _sourceChallenge = value.currentChallenge; _sourceFile = GlobalSaveData.filename;
        _sourceCatalog = HeroNativeRights.CatalogGeneration;
        // An unresolvable or divergent island slot proves no source identity: the slot stays -1
        // (never matching a real state) and the caller keeps its fail-closed path.
        bool slotOk = TryIslandSlot(campaign, island, out _sourceSlot);
        _sourceValid = true;
        _sourceProven = slotOk && HeroNativeRights.TrySourceIdentity(_sourceSlot, out _, out _sourceOwner);
        if (!_sourceProven) _sourceOwner = IntPtr.Zero;
        global = _sourceGlobal; owner = _sourceOwner; islandSlot = _sourceSlot;
        return _sourceProven;
    }
    private static readonly HashSet<IntPtr> BoundRoots = new();
    private static IslandState _current;
    private static long _nextLife;
    private static float _nextSweep;
    private static Candidate _cachedCandidate;
    private static int _cachedSide;
    private static float _candidateCacheUntil;
    private static int _lastTickFrame = -1;
    private static int _contextFrame = -1;
    private static string _contextKey;
    private static long _contextWorld;
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);
    private static LoadCapture _load;
    private static SaveCapture _save;
    internal static string ArchivePath => System.IO.Path.Combine(ModDataPaths.ConfigPath, "KingdomEnhancedMod", "ModSave", "hero-identities.v1.json");

    internal static void Observe(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            if (_current == null || !_current.Ready || !OptionalQoLScope.IsCurrent(archer)) return;
            GetCandidate(archer, true);
        }
        catch (Exception e) { Log("observe", e); }
    }

    // Temporary SetActive(false/true) is not a new purchased lifetime. Successful FastDespawn
    // already invalidates true recycling; preserve an unchanged live owner in the same world.
    internal static void OnEnable(Archer archer)
    {
        try
        {
            if (archer == null) return;
            var character = archer.GetComponent<Character>();
            if (character == null) return;
            IntPtr key = character.Pointer;
            if (Candidates.TryGetValue(key, out var old))
            {
                if (ValidOwner(old) && character.gameObject != null && old.Root == character.gameObject.Pointer
                    && old.GoId == character.gameObject.GetInstanceID() && old.World == WorldKey()
                    && character._damageable != null && !character._damageable.isDead)
                {
                    old.Seen = Time.time;
                    return;
                }
                // A new life never inherits the old receipt: release, never re-borrow, and keep
                // the record only while the release is still pending.
                UnbindSeatsFor(old);
                if (!RetireCandidate(old)) return;
            }
        }
        catch (Exception e) { Log("enable", e); }
    }

    internal static void OnPoolDespawn(GameObject root)
    {
        try
        {
            if (root == null) return;
            UnbindRoot(root.Pointer);
        }
        catch (Exception e) { Log("pool", e); }
    }

    private static void UnbindRoot(IntPtr root)
    {
        foreach (var island in Islands.Values)
            foreach (var seat in island.Seats)
                if (seat.Owner != null && seat.Owner.Root == root)
                {
                    var owner = seat.Owner;
                    Unbind(seat);
                    RetireCandidate(owner);
                }
    }

    internal static bool IsPurchased(Archer archer)
    {
        try
        {
            if (_current == null || !_current.Ready || archer == null || !OptionalQoLScope.IsCurrent(archer)) return false;
            foreach (var seat in _current.Seats)
                if (Matches(seat.Owner, archer) && !IsDead(seat)) return true;
        }
        catch (Exception e) { Log("identity", e); }
        return false;
    }

    internal static bool HasPurchasedCareer(Character character)
    {
        try
        {
            if (_current == null || !_current.Ready || _current.World != WorldKey() || character == null) return false;
            foreach (var seat in _current.Seats)
                if (Matches(seat.Owner, character) && !IsDead(seat)) return true;
        }
        catch { }
        return false;
    }

    internal static int SeatSide(Archer archer)
    {
        try
        {
            if (!IsPurchased(archer)) return 0;
            foreach (var seat in _current.Seats) if (Matches(seat.Owner, archer)) return seat.Receipt.Side;
        }
        catch { }
        return 0;
    }

    internal static bool CanPurchase
    {
        get { try { return FindPurchase(out _, out _); } catch { return false; } }
    }

    internal static bool HasFallenSeat(int side)
    {
        return (side == -1 || side == 1) && _current != null && _current.Ready
            && _contextFrame == Time.frameCount && _contextKey == _current.ContextKey
            && _contextWorld == _current.World
            && (_current.FallenSeatMask & (side < 0 ? 1 : 2)) != 0;
    }

    // Read-only presentation query. The panel calls Tick first, then the shop throttles these
    // per-side queries to 0.25 seconds. No scope hashing, file access, or purchase/cache mutation.
    internal static HeroShopSeatState GetShopSeatState(int side)
    {
        if (side != -1 && side != 1) return HeroShopSeatState.Unavailable;
        try
        {
            if (_load != null || _current == null || !_current.Ready || !HeroArcherNetwork.AllowsLocalHero
                || GlobalSaveData.loaded == null || CampaignSaveData.current?.CurrentIsland == null
                || _contextFrame != Time.frameCount || _contextKey != _current.ContextKey
                || _contextWorld != _current.World || WorldKey() != _current.World) return HeroShopSeatState.Unavailable;

            var purchased = _current.Seats.Find(x => x.Receipt.Side == side);
            if (purchased != null)
                return ValidOwner(purchased.Owner) && purchased.Owner.World == _current.World && !IsDead(purchased)
                    ? HeroShopSeatState.Occupied : HeroShopSeatState.Reserved;

            if (!HeroArcherRuntime.Enabled || !_current.HasBaseline || _current.ReadOnly || _current.Unresolved
                || _current.Seats.Any(x => !ValidOwner(x.Owner))) return HeroShopSeatState.Unavailable;
            foreach (var candidate in Candidates.Values)
            {
                if (candidate.World != _current.World || Time.time - candidate.Seen > 3f || !ValidOwner(candidate)
                    || _current.Seats.Any(x => ReferenceEquals(x.Owner, candidate))) continue;
                var archer = candidate.Actor;
                if (archer == null) continue;
                float nativeSide = (float)archer.side;
                int observedSide = nativeSide < 0 ? -1 : nativeSide > 0 ? 1 : 0;
                if (observedSide == side && HeroArcherRuntime.IsRecruitable(archer)) return HeroShopSeatState.Available;
            }
        }
        catch { }
        return HeroShopSeatState.Unavailable;
    }

    internal static string StatusText
    {
        get
        {
            try
            {
                if (!HeroArcherRuntime.Enabled) return "英雄商店已关闭或联机不可用";
                if (_current == null || !_current.Ready) return "等待岛屿存档上下文";
                if (_current.Seats.Any(x => x.Owner == null)) return "英雄身份待恢复，名额保留";
                if (_current.Unresolved) return "英雄附加档历史待确认，暂停招募";
                if (!_current.HasBaseline) return "等待原生存档加载确认";
                if (_current.ReadOnly) return "英雄附加档只读，暂停招募";
                if (_current.Seats.Count >= 2) return "左右英雄名额已满";
                return FindPurchase(out _, out _) ? "8 金币训练英雄弓手" : "等待空侧的普通弓箭手";
            }
            catch { return "英雄招募暂不可用"; }
        }
    }

    // Read-only diagnostics for the panel and tests: context, resolved epoch, match kind and seat
    // states. Bounded to the two owned seats; no archive access and no hashing.
    internal static string DescribeForTests()
    {
        var state = _current;
        if (state == null) return "none";
        var seats = new List<string>(2);
        foreach (var seat in state.Seats)
            seats.Add(seat.Receipt.Id.ToString("N").Substring(0, 8) + ":" + seat.Receipt.Side + ":"
                + (seat.Receipt.NativeId.Length > 0 ? "native" : "reserved") + ":" + (seat.Owner != null ? "bound" : "unbound")
                + (seat.Owner != null && seat.Owner.Embarkee != null && seat.Owner.Embarkee.Owned ? ":borrowed" : ""));
        return "ctx=" + Short(state.ContextKey) + " epoch=" + Short(state.Epoch) + " kind=" + state.MatchKind
            + " hk=" + state.MatchHashKind + " v1=" + state.MatchLegacy
            + " seats=" + state.Seats.Count + " claims=" + PendingClaims() + " baseline=" + state.HasBaseline + " unresolved=" + state.Unresolved
            + " readonly=" + state.ReadOnly + " [" + string.Join(",", seats) + "]";
    }

    // Read-only diagnostic: unsettled embarkee borrows across all island records.
    private static int PendingClaims()
    {
        int claims = 0;
        foreach (var candidate in Candidates.Values)
            if (candidate.Embarkee != null && candidate.Embarkee.Owned) claims++;
        return claims;
    }

    // The native shop owns payment. This method creates a receipt only after all current gates
    // and the archive's write compatibility have been checked; it never changes coins or saves.
    internal static bool TryPurchase(out string reason)
    {
        reason = "英雄招募暂不可用";
        Seat pendingSeat = null;
        IslandState purchaseIsland = null;
        bool tracked = false;
        try
        {
            Tick();
            if (!FindPurchase(out var candidate, out int side, true)) { reason = StatusText; return false; }
            var disk = HeroRecruitmentArchiveStore.Load(ArchivePath);
            if (!disk.Writable && !_current.NativeAuthority) { _current.ReadOnly = true; reason = "英雄附加档不可写，未招募"; return false; }
            if (disk.Writable && !_current.NativeAuthority && disk.Archive != null && _current.Epoch != null && !disk.Archive.Scopes.ContainsKey(_current.Epoch)
                && disk.Archive.Scopes.Count >= HeroRecruitmentArchive.MaxScopes)
            { _current.ReadOnly = true; reason = "英雄附加档容量已满"; return false; }
            if (disk.Writable && !_current.NativeAuthority && disk.Archive != null && !disk.Archive.TryGetContext(_current.ContextKey, out _)
                && disk.Archive.Contexts.Count >= HeroRecruitmentArchive.MaxContexts)
            { _current.ReadOnly = true; reason = "英雄附加档容量已满"; return false; }
            if (!FindPurchase(out var fresh, out int freshSide, true) || !ReferenceEquals(fresh, candidate) || freshSide != side) return false;
            var seat = new Seat { Receipt = new() { Id = Guid.NewGuid(), Side = side } };
            if (!HeroNativeRights.Track(seat.Receipt.Id)) { reason = "原生英雄权益存储不可用，未招募"; return false; }
            pendingSeat = seat; tracked = true;
            if (!Bind(seat, candidate)) { HeroNativeRights.Abandon(seat.Receipt.Id); reason = "无法确认英雄死亡监听，未招募"; return false; }
            purchaseIsland = _current;
            purchaseIsland.Seats.Add(seat);
            bool borrowed = BorrowEmbarkee(candidate);   // receipt is public before native OnDisable
            if (!borrowed || !ReferenceEquals(seat.Owner, candidate) || !purchaseIsland.Seats.Contains(seat))
            {
                purchaseIsland.Seats.Remove(seat); Unbind(seat); HeroNativeRights.Abandon(seat.Receipt.Id);
                reason = "登船保护初始化失败，未招募";
                return false;
            }
            bool activated = false;
            try { activated = HeroArcherRuntime.TryActivatePurchased(candidate.Actor) && ReferenceEquals(_current, purchaseIsland); }
            catch (Exception e) { Log("activation", e); }
            if (!activated)
            {
                purchaseIsland.Seats.Remove(seat); Unbind(seat); HeroNativeRights.Abandon(seat.Receipt.Id);
                reason = "英雄外观或战斗初始化失败，未招募";
                return false;
            }
            reason = side < 0 ? "左侧英雄弓手训练完成" : "右侧英雄弓手训练完成";
            purchaseIsland.FallenSeatMask &= ~(side < 0 ? 1 : 2);
            Log("purchased:" + seat.Receipt.Id.ToString("N"), null);
            return true;
        }
        catch (Exception e)
        {
            if (tracked)
            {
                purchaseIsland?.Seats.Remove(pendingSeat);
                if (pendingSeat != null) { Unbind(pendingSeat); HeroNativeRights.Abandon(pendingSeat.Receipt.Id); }
            }
            Log("purchase", e);
            return false;
        }
    }

    private static bool FindPurchase(out Candidate candidate, out int side, bool fresh = false)
    {
        candidate = null; side = 0;
        if (!HeroArcherRuntime.Enabled || _load != null || _current == null || !_current.Ready || !_current.HasBaseline || _current.ReadOnly || _current.Unresolved || _current.Seats.Count >= 2
            || _current.Seats.Any(x => x.Owner == null)) return false;
        if (!TryContext(out string contextKey, out long world, fresh) || contextKey != _current.ContextKey || world != _current.World) return false;
        if (!fresh && Time.unscaledTime < _candidateCacheUntil)
        {
            if (_cachedCandidate == null) return false;
            var cached = _cachedCandidate;
            var a = ValidOwner(cached) ? cached.Actor : null;
            if (cached.World == world && a != null && Time.time - cached.Seen <= 3f
                && !_current.Seats.Any(x => x.Receipt.Side == _cachedSide || ReferenceEquals(x.Owner, cached))
                && Math.Sign((float)a.side) == _cachedSide && HeroArcherRuntime.IsRecruitable(a))
            { candidate = cached; side = _cachedSide; return true; }
        }
        // Bounded cached registry only, stop at first eligible actor. Final payment always forces a fresh check.
        foreach (var entry in Candidates.Values)
        {
            if (entry.World != world || !ValidOwner(entry) || Time.time - entry.Seen > 3f) continue;
            var actor = entry.Actor;
            if (actor == null || !HeroArcherRuntime.IsRecruitable(actor)) continue;
            int s = (float)actor.side < 0 ? -1 : (float)actor.side > 0 ? 1 : 0;
            if (s == 0 || _current.Seats.Any(x => x.Receipt.Side == s || ReferenceEquals(x.Owner, entry))) continue;
            candidate = entry; side = s; break;
        }
        _cachedCandidate = candidate; _cachedSide = side; _candidateCacheUntil = Time.unscaledTime + 0.25f;
        return candidate != null;
    }

    internal static void Tick()
    {
        try
        {
            if (_load != null) return;
            if (_lastTickFrame == Time.frameCount) return;
            _lastTickFrame = Time.frameCount;
            if (!TryContext(out string contextKey, out long world)) return;
            bool sourceProven = TrySourceIdentity(out IntPtr srcGlobal, out IntPtr srcOwner, out int srcSlot);
            if (_current == null || _current.ContextKey != contextKey || _current.World != world
                || (sourceProven && !SameSource(_current, srcGlobal, srcOwner, srcSlot)))
            {
                _candidateCacheUntil = 0;
                IslandState next = sourceProven ? StateForSource(contextKey, srcGlobal, srcOwner, srcSlot) : null;
                if (next == null)
                {
                    if (Islands.Count >= HeroRecruitmentArchive.MaxContexts) return;
                    next = NewState(contextKey, world, null, null, false, null, false);
                    if (sourceProven) { next.SourceGlobal = srcGlobal; next.SourceOwner = srcOwner; next.SourceSlot = srcSlot; }
                    InstallState(contextKey, next);
                }
                next.World = world; _current = next;
            }
            if (Time.unscaledTime < _nextSweep) return;
            _nextSweep = Time.unscaledTime + 2f;
            // Only our two owned seats are inspected. A disappeared owner reserves its paid seat.
            foreach (var state in Islands.Values)
                for (int i = state.Seats.Count - 1; i >= 0; i--)
                {
                    var seat = state.Seats[i];
                    if (seat.Owner == null) continue;
                    if (!ValidOwner(seat.Owner)) { Unbind(seat); continue; }
                    // A seat that lost its borrow (e.g. a native disembark disabled then re-enabled
                    // the component) is re-borrowed on the current ready island only.
                    if (seat.Owner.Embarkee == null && state.Ready && ReferenceEquals(state, _current))
                        BorrowEmbarkee(seat.Owner);
                }
            var remove = new List<IntPtr>();
            foreach (var pair in Candidates)
            {
                var candidate = pair.Value;
                // A settled seat still needs its borrowed embarkee; an owner without a seat must
                // hand it back before the record can be retired.
                if (candidate.Embarkee != null && !HasSeat(candidate)) TryReturnEmbarkee(candidate);
                if (!ValidOwner(candidate) || (Time.time - candidate.Seen > 10f && !HasSeat(candidate))) remove.Add(pair.Key);
            }
            foreach (var key in remove)
                if (Candidates.TryGetValue(key, out var pending)) RetireCandidate(pending);
        }
        catch (Exception e) { Log("tick", e); }
    }

    private static bool HasSeat(Candidate owner) => Islands.Values.Any(x => x.Seats.Any(s => ReferenceEquals(s.Owner, owner)));

    private static Candidate GetCandidate(Archer actor, bool seen)
    {
        return actor != null ? GetCandidate(actor.GetComponent<Character>(), seen) : null;
    }

    private static Candidate GetCandidate(Character character, bool seen)
    {
        if (character == null || character.gameObject == null || character.Pointer == IntPtr.Zero) return null;
        if (Candidates.TryGetValue(character.Pointer, out var existing))
        {
            if (existing.Root == character.gameObject.Pointer && existing.GoId == character.gameObject.GetInstanceID()
                && ValidOwner(existing))
            { if (seen) existing.Seen = Time.time; return existing; }
            UnbindSeatsFor(existing);
            if (!RetireCandidate(existing)) return null;
        }
        if (Candidates.Count >= MaxCandidates) return null;
        var candidate = new Candidate
        {
            Character = character, Pointer = character.Pointer, Root = character.gameObject.Pointer,
            GoId = character.gameObject.GetInstanceID(),
            Life = ++_nextLife, World = WorldKey(), Seen = Time.time
        };
        Candidates[character.Pointer] = candidate;
        _candidateCacheUntil = 0;
        return candidate;
    }

    private static bool ValidOwner(Candidate owner)
    {
        try
        {
            return owner != null && owner.Character != null && owner.Character.gameObject != null
                && owner.Character.Pointer == owner.Pointer && owner.Character.gameObject.Pointer == owner.Root
                && owner.GoId != 0 && owner.Character.gameObject.GetInstanceID() == owner.GoId
                && Candidates.TryGetValue(owner.Pointer, out var live) && ReferenceEquals(live, owner) && live.Life == owner.Life;
        }
        catch { return false; }
    }
    private static bool Matches(Candidate owner, Archer actor) => ValidOwner(owner) && actor != null && actor.gameObject != null
        && owner.Root == actor.gameObject.Pointer && owner.GoId == actor.gameObject.GetInstanceID();
    private static bool Matches(Candidate owner, Character actor) => ValidOwner(owner) && actor != null && owner.Pointer == actor.Pointer
        && actor.gameObject != null && owner.Root == actor.gameObject.Pointer && owner.GoId == actor.gameObject.GetInstanceID();
    private static bool MatchesRoot(Candidate owner, Persistent root) => ValidOwner(owner) && root != null && root.gameObject != null
        && owner.Root == root.gameObject.Pointer && owner.GoId == root.gameObject.GetInstanceID();
    private static bool IsDead(Seat seat) { try { return seat.Damage == null || seat.Damage.isDead; } catch { return true; } }

    private static bool Bind(Seat seat, Candidate candidate, bool loading = false)
    {
        if (!ValidOwner(candidate)) return false;
        var damage = candidate.Character._damageable;
        if (damage == null || (!loading && damage.isDead)) return false;
        Unbind(seat);
        seat.Owner = candidate; seat.Damage = damage;
        seat.Death = (Action<GameObject>)(_ => OnDeath(seat, candidate));
        try { damage.OnDeath += seat.Death; BoundRoots.Add(candidate.Root); return true; }
        catch { Unbind(seat); return false; }
    }

    private static void OnDeath(Seat seat, Candidate owner)
    {
        try
        {
            if (!ReferenceEquals(seat.Owner, owner) || !ValidOwner(owner) || seat.Damage == null || !seat.Damage.isDead) return;
            // This is Damageable's terminal HP death event, not equipment loss/role callbacks.
            foreach (var island in Islands.Values)
                if (island.Ready && _load == null && island.Seats.Remove(seat))
                {
                    island.FallenSeatMask |= seat.Receipt.Side < 0 ? 1 : 2;
                    HeroNativeRights.Abandon(seat.Receipt.Id);
                    Unbind(seat); Log("death:" + seat.Receipt.Id.ToString("N"), null); break;
                }
        }
        catch (Exception e) { Log("death", e); }
    }

    private static void Unbind(Seat seat)
    {
        IntPtr root = seat.Owner != null ? seat.Owner.Root : IntPtr.Zero;
        Candidate owner = seat.Owner;
        try { if (seat.Damage != null && seat.Death != null) seat.Damage.OnDeath -= seat.Death; }
        catch (Exception e) { Log("unsubscribe", e); }
        seat.Owner = null; seat.Damage = null; seat.Death = null;
        // Visibility is removed first; only then may the borrowed embarkee be handed back (CAS).
        // The seat itself is excluded: its own owner reference must not keep the release pending.
        if (owner != null && !Islands.Values.Any(x => x.Seats.Any(s => !ReferenceEquals(s, seat) && ReferenceEquals(s.Owner, owner))))
            TryReturnEmbarkee(owner);
        if (root != IntPtr.Zero && !Islands.Values.Any(x => x.Seats.Any(s => s.Owner != null && s.Owner.Root == root))
            && !Candidates.Values.Any(x => x.Root == root && x.Embarkee != null && x.Embarkee.Owned)) BoundRoots.Remove(root);
    }

    // Read-only predicate for the boarding hooks (HeroBoardingPolicy): the embarkee belongs to a
    // unit that is a confirmed live purchase of this island/world. Only the registered candidate
    // identity can match, so a pooled or recycled wrapper never protects a bystander. This never
    // writes a field, never Ticks and never touches the registrar.
    internal static bool IsProtectedEmbarkeeCareer(Embarkee embarkee)
    {
        try
        {
            if (embarkee == null || embarkee.gameObject == null) return false;
            return HasPurchasedCareer(embarkee.gameObject.GetComponent<Character>());
        }
        catch { return false; }
    }

    // Borrow: disable the owner's Embarkee so the native registrar unregisters the unit and clears
    // any pending non-embarked target (actual 2.4: enabled=false runs OnDisable; the registrar's
    // unregister path clears the target and redistributes). The exact identity and the original
    // enabled value are recorded before the write. An already-embarked unit is never touched, a
    // state that was already disabled is never claimed, and the write is read back; a target the
    // native cleanup did not clear is reported instead of being silently assumed away.
    private static bool BorrowEmbarkee(Candidate owner)
    {
        if (!ValidOwner(owner)) return false;
        if (owner.Embarkee != null)
        {
            try
            {
                var held = owner.Embarkee;
                var current = owner.Character.gameObject.GetComponent<Embarkee>();
                var nativeOwner = current?._owner?.TryCast<UnityEngine.Component>();
                return !held.Busy && !held.PendingReturn && held.Life == owner.Life
                    && current != null && current.Pointer == held.Pointer && current.gameObject != null
                    && current.gameObject.Pointer == held.Root && nativeOwner != null
                    && nativeOwner.gameObject != null && nativeOwner.gameObject.Pointer == held.Root
                    && !current.enabled && !current.IsEmbarked && !current.IsTargetingEmbarkable
                    && current.EmbarkableTarget == null;
            }
            catch { return false; }
        }
        EmbarkeeClaim claim = null;
        bool ok = false;
        try
        {
            Character character = owner.Character;
            Embarkee embarkee = character.gameObject.GetComponent<Embarkee>();
            var nativeOwner = embarkee?._owner?.TryCast<UnityEngine.Component>();
            if (embarkee == null || embarkee.gameObject == null || embarkee.gameObject.Pointer != owner.Root
                || nativeOwner == null || nativeOwner.gameObject == null
                || nativeOwner.gameObject.Pointer != owner.Root || embarkee.IsEmbarked)
            { if (embarkee != null && embarkee.IsEmbarked) Log("borrow-skip-embarked:" + owner.Life, null); return false; }
            claim = new EmbarkeeClaim
            {
                Pointer = embarkee.Pointer, Root = owner.Root, GoId = owner.GoId, Life = owner.Life,
                OriginalEnabled = embarkee.enabled, Owned = false,
            };
            owner.Embarkee = claim;
            if (!claim.OriginalEnabled) return embarkee.EmbarkableTarget == null;
            claim.Owned = true;   // publish responsibility before the setter can invoke OnDisable
            claim.Busy = true;
            embarkee.enabled = false;
            ok = ValidOwner(owner) && ReferenceEquals(owner.Embarkee, claim) && HasSeat(owner)
                && !embarkee.enabled && !embarkee.IsEmbarked && !embarkee.IsTargetingEmbarkable
                && embarkee.EmbarkableTarget == null;
            if (!ok) claim.PendingReturn = true;
            return ok;
        }
        catch (Exception e) { Log("borrow", e); if (claim != null) claim.PendingReturn = true; return false; }
        finally
        {
            if (claim != null)
            {
                claim.Busy = false;
                if (claim.PendingReturn) TryReturnEmbarkee(owner);
            }
        }
    }

    // CAS release: only when we still own the disabled write, the same component/root/goId/life is
    // reachable and the current value is still ours, write the original value back. A destroyed
    // object, a replaced life or a third-party value drops the claim without writing. An active
    // object in a former/unknown world keeps the responsibility (its registrar is not touched from
    // this session); an inactive object may be released without triggering a registration.
    private static bool TryReturnEmbarkee(Candidate owner)
    {
        EmbarkeeClaim claim = owner?.Embarkee;
        if (claim == null) return true;
        if (claim.Busy) { claim.PendingReturn = true; return false; }
        if (!claim.Owned) { if (ReferenceEquals(owner.Embarkee, claim)) owner.Embarkee = null; return true; }
        try
        {
            Character character = owner.Character;
            if (character == null || character.gameObject == null
                || character.gameObject.Pointer != claim.Root || character.gameObject.GetInstanceID() != claim.GoId
                || owner.Life != claim.Life)
            { if (ReferenceEquals(owner.Embarkee, claim)) owner.Embarkee = null; return true; }
            Embarkee embarkee = character.gameObject.GetComponent<Embarkee>();
            if (embarkee == null || embarkee.gameObject == null || embarkee.Pointer != claim.Pointer)
                return false;   // a replaced live component is not proof the old write was restored
            bool inactive = !character.gameObject.activeInHierarchy;
            long world = WorldKey();
            if (!inactive && (world == 0 || world != owner.World)) return false;
            if (!embarkee.enabled)
            {
                claim.Busy = true;
                try { embarkee.enabled = claim.OriginalEnabled; }
                finally { claim.Busy = false; }
            }
            if (embarkee.enabled != claim.OriginalEnabled) return false;
            if (ReferenceEquals(owner.Embarkee, claim)) owner.Embarkee = null;
            return true;
        }
        catch { return false; }
    }

    // A candidate record is retired only after its claim is settled. Returns false while the
    // release is pending: the record stays so maintenance retries, which is what keeps a pooled
    // wrapper from inheriting a stale disabled write.
    private static bool RetireCandidate(Candidate owner)
    {
        if (owner == null) return true;
        if (!Candidates.TryGetValue(owner.Pointer, out var live) || !ReferenceEquals(live, owner)) return true;
        if (!TryReturnEmbarkee(owner) || owner.Embarkee != null) return false;
        if (!Candidates.TryGetValue(owner.Pointer, out live) || !ReferenceEquals(live, owner)) return true;
        Candidates.Remove(owner.Pointer);
        if (!Candidates.Values.Any(x => x.Root == owner.Root && x.Embarkee != null && x.Embarkee.Owned)
            && !Islands.Values.Any(x => x.Seats.Any(y => y.Owner != null && y.Owner.Root == owner.Root)))
            BoundRoots.Remove(owner.Root);
        return true;
    }

    private static void UnbindSeatsFor(Candidate owner)
    {
        foreach (var island in Islands.Values)
            for (int i = island.Seats.Count - 1; i >= 0; i--)
            {
                var seat = island.Seats[i];
                if (ReferenceEquals(seat.Owner, owner)) Unbind(seat);
            }
    }

    private static Candidate FindOwnerByRoot(IntPtr root)
    {
        foreach (var candidate in Candidates.Values)
            if (candidate.Root == root && (HasSeat(candidate) || candidate.Embarkee?.Owned == true)) return candidate;
        return null;
    }

    // Resolves the persistent context/epoch of one native island snapshot. Read-only here:
    // context registration and baselines are committed only by the load/virgin/generation writers.
    private static IslandState NewState(string contextKey, long world, IslandSaveData island, string rawJson,
        bool generationPending, IslandState previous, bool sessionQualified)
    {
        var disk = HeroRecruitmentArchiveStore.Load(ArchivePath);
        var state = new IslandState { ContextKey = contextKey, World = world, Ready = true, ReadOnly = !disk.Writable };
        // The source's own session seats (its current paid receipts, including ones this session
        // has not checkpointed yet) are the re-entry authority when the session was qualified;
        // otherwise the resolver's checkpoint safe path applies. Never a historical union.
        List<HeroPurchaseReceipt> session = null;
        if (sessionQualified && previous != null)
        {
            session = new List<HeroPurchaseReceipt>(previous.Seats.Count);
            foreach (var seat in previous.Seats) session.Add(seat.Receipt);
        }
        HeroRecruitmentContexts.Resolution resolution;
        // issue #153: the island's slot is the key identity; an island whose slot cannot be proven
        // (not in _islands, or a divergent land) must not reach the native resolver at all.
        int islandSlot = -1;
        if (island != null && !TryIslandSlot(CampaignSaveData.current, island, out islandSlot)) islandSlot = -1;
        if (!HeroNativeRights.Resolve(contextKey, GlobalSaveData._loaded.currentCampaign,
            GlobalSaveData._loaded.currentChallenge, islandSlot, island, rawJson, generationPending,
            out var native, out bool nativeKnown, out bool nativeBaseline))
        {
            state.ReadOnly = true; state.Unresolved = true; state.MatchKind = "native-unavailable";
            return state;
        }
        if (nativeKnown)
        {
            state.NativeAuthority = true;
            state.ReadOnly = false;
            resolution = native;
            if (island != null && islandSlot >= 0)
                state.SessionRebind = HeroNativeRights.MergeSessionRebind(GlobalSaveData._loaded.currentCampaign,
                    GlobalSaveData._loaded.currentChallenge, islandSlot, session, native);
        }
        else
        {
            if (disk.Archive == null) return state;
            resolution = HeroRecruitmentContexts.Resolve(disk.Archive, contextKey, island, rawJson, generationPending);
        }
        state.Epoch = resolution.Epoch;
        state.MatchKind = resolution.Kind;
        state.MatchHash = resolution.MatchHash;
        state.MatchHashKind = resolution.MatchHashKind;
        state.MatchLegacy = resolution.MatchLegacy;
        state.NewEpoch = resolution.NewEpoch;
        state.Unresolved = resolution.Unresolved;
        state.HasBaseline = state.NativeAuthority ? nativeBaseline : resolution.Epoch != null && disk.Archive.Baselines.ContainsKey(resolution.Epoch);
        foreach (var receipt in resolution.Seats) state.Seats.Add(new() { Receipt = receipt });
        // Only a snapshot whose seat set is actually known speaks as the session authority: an
        // unresolved or read-only resolution knows nothing, so its empty set is not evidence (and
        // must never be used to end an open capture responsibility).
        state.HasSession = island != null && !state.Unresolved && !state.ReadOnly;
        return state;
    }

    private static bool TryContext(out string contextKey, out long world, bool fresh = false)
    {
        contextKey = null; world = 0;
        try
        {
            if (!HeroArcherNetwork.AllowsLocalHero) return false;
            if (!fresh && _contextFrame == Time.frameCount) { contextKey = _contextKey; world = _contextWorld; return contextKey != null && world != 0; }
            var global = GlobalSaveData.loaded; var campaign = CampaignSaveData.current;
            var island = campaign != null ? campaign.CurrentIsland : null;
            if (global == null || island == null) return false;
            world = WorldKey();
            bool valid = world != 0 && TryIslandSlot(campaign, island, out int islandSlot)
                && TryContextKey(global.currentCampaign, global.currentChallenge, islandSlot, out contextKey);
            if (valid) { _contextKey = contextKey; _contextWorld = world; _contextFrame = Time.frameCount; }
            return valid;
        }
        catch { return false; }
    }

    // Bounded-logging wrapper over the archive's pure slot lookup: a campaign table that cannot
    // prove this island's slot (or proves a land/slot divergence) must never produce a key.
    private static bool TryIslandSlot(CampaignSaveData campaign, IslandSaveData island, out int islandSlot)
    {
        var lookup = HeroRecruitmentArchive.IslandSlot(campaign, island, out islandSlot);
        if (lookup == HeroRecruitmentArchive.IslandSlotLookup.NotFound) Log("island-slot-missing", null);
        else if (lookup == HeroRecruitmentArchive.IslandSlotLookup.Divergent) Log("island-identity-divergent", null);
        return lookup == HeroRecruitmentArchive.IslandSlotLookup.Ok;
    }

    // Stable context identity for one island lineage. The runtime creation ticks of the loaded
    // island are deliberately excluded: the game recreates them per load and only the stable
    // file/campaign/challenge/slot identity survives, which is what the v2 epochs are keyed on.
    private static bool TryContextKey(int campaign, int challenge, int islandSlot, out string contextKey)
    {
        contextKey = null;
        try
        {
            if (islandSlot < 0) return false;
            string file = GlobalSaveData.filename;
            if (string.IsNullOrEmpty(file) || file.Length > 256) return false;
            contextKey = HeroRecruitmentArchive.ContextKey(file, campaign, challenge, islandSlot);
            return true;
        }
        catch { return false; }
    }
    private static long WorldKey()
    {
        try { var layer = Managers.Inst?.world?.gameLayer; return layer != null && layer.gameObject.activeInHierarchy ? layer.Pointer.ToInt64() : 0; }
        catch { return 0; }
    }
    private static void Log(string key, Exception error)
    {
        if (Logged.Count >= 64 || !Logged.Add(key)) return;
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[HeroRecruitment] " + key + (error == null ? "" : ": " + error.GetType().Name)); } catch { }
    }

    // Existing long native save/load endpoints only; no native compressed-file changes.
    internal sealed class SaveCapture
    {
        internal SaveCapture Previous;
        internal int Campaign, Land, Challenge;
        internal IslandSaveData Island;
        internal string ContextKey;
        internal int IslandSlot = -1;   // frozen with the island, from campaign._islands
        internal IntPtr GlobalPointer, CampaignPointer;
        internal IntPtr SourcePointer;   // source identity frozen at Save entry, before any GetID
        internal long World;

        private readonly Dictionary<string, Seat> Owners = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, Candidate> Lives = new();
        internal bool Conflict;
        internal bool MarkerSeen;

        // Freezes the source identity at Save entry from the live Global and the save arguments,
        // before the native method can clear or rebuild the target island table: a failure before
        // the first successful GetID must still leave a responsibility for this exact source.
        // This freezes source ownership only; character rows are still proven by a full capture.
        internal void FreezeSource()
        {
            try
            {
                if (!HeroArcherNetwork.AllowsLocalHero || SourcePointer != IntPtr.Zero) return;
                var global = GlobalSaveData._loaded;
                if (global == null || global.Pointer == IntPtr.Zero) return;
                var owner = ResolveSaveOwner(global, Campaign, Challenge);
                if (owner == IntPtr.Zero) return;
                GlobalPointer = global.Pointer;
                SourcePointer = owner;
                CampaignPointer = owner;
            }
            catch (Exception e) { Log("save-freeze", e); }
        }

        // The save arguments identify the source object. The live current campaign/challenge is
        // taken only when the arguments match its indices; for non-current saves only the
        // campaign catalog index has verified indexing evidence here, so a save whose owner
        // cannot be identified freezes nothing rather than attaching itself to another owner.
        private static IntPtr ResolveSaveOwner(GlobalSaveData global, int campaign, int challenge)
        {
            var current = global.GetCurrentCampaign();
            if (current != null && current.Pointer != IntPtr.Zero
                && global.currentCampaign == campaign && global.currentChallenge == challenge)
                return current.Pointer;
            if (challenge == 0 && campaign >= 0 && global.campaigns != null && campaign < global.campaigns.Count)
            {
                var item = global.campaigns[campaign];
                if (item != null && item.Pointer != IntPtr.Zero) return item.Pointer;
            }
            return IntPtr.Zero;
        }

        internal void Capture(Persistent persistent, string id)
        {
            if (persistent == null || string.IsNullOrEmpty(id) || id.Length > 256) return;
            if (Island == null)
            {
                var island = IslandSaveData.CurrentlySavingIsland;
                var global = GlobalSaveData._loaded;
                var current = CampaignSaveData.current;
                if (island == null || !IslandSaveData.isSavingGame || (Land != -1 && island.land != Land)
                    || global == null || current == null || global.currentCampaign != Campaign
                    || global.currentChallenge != Challenge) return;
                if (!TryIslandSlot(current, island, out int islandSlot)
                    || !TryContextKey(Campaign, Challenge, islandSlot, out string contextKey)) return;
                Island = island; ContextKey = contextKey; IslandSlot = islandSlot;
                GlobalPointer = global.Pointer; CampaignPointer = current.Pointer;
                if (SourcePointer == IntPtr.Zero) SourcePointer = current.Pointer;
                World = WorldKey();
            }
            if (!Islands.TryGetValue(ContextKey, out var state)) return;
            var actor = persistent.GetComponent<Character>();
            if (actor == null) return;
            var seat = state.Seats.Find(x => Matches(x.Owner, actor));
            if (seat == null) return;
            if (Owners.TryGetValue(id, out var other) && !ReferenceEquals(other, seat)) { Conflict = true; return; }
            if (Owners.Any(x => ReferenceEquals(x.Value, seat) && x.Key != id)) { Conflict = true; return; }
            Owners[id] = seat; Lives[seat.Receipt.Id] = seat.Owner;
        }

        // A capture that would pair this island state with the context's owned rights takes a
        // validity responsibility: a failed attempt keeps the global save refused until the same
        // source completes a capture again. Nothing is reverted, no seat is guessed and no right
        // is deleted; the reserved placeholder keeps the quota for the unverified state.
        internal void Apply()
        {
            bool staged = false;
            try { staged = ApplyCore(); }
            catch (Exception e) { if (_current != null) _current.ReadOnly = true; Log("save", e); }
            // issue-150 v4 (review R3 P1-1): the capacity-degraded shape (epoch-cap) has empty
            // seats and zero writes by construction — there is nothing to mis-pair, so holding a
            // capture responsibility here protects nothing while its Prepare gate refuses the
            // whole native save. Complete it as a no-op success instead of NoteCaptureInvalid.
            if (!staged && ContextKey != null && Islands.TryGetValue(ContextKey, out var capState)
                && ReferenceEquals(capState, _current) && capState.MatchKind == "epoch-cap")
            {
                // issue #153 (review #2 P1-2b): the unresolved gate in ApplyCore also means a
                // responsibility recorded earlier for this exact source can never be re-verified
                // by a Stage — it would only keep refusing the global save. Release exactly that
                // source (frozen owner + slot); the land value is never used as a fallback key.
                var capOwner = SourcePointer != IntPtr.Zero ? SourcePointer : CampaignPointer;
                if (capOwner != IntPtr.Zero && IslandSlot >= 0)
                    HeroNativeRights.ReleaseEpochCapResponsibility(capOwner, IslandSlot);
                Log("save-epoch-cap-noop", null); return;
            }
            if (!staged) NoteCaptureInvalid();
        }

        private bool ApplyCore()
        {
            if (Conflict || !MarkerSeen || Island == null || !HeroArcherNetwork.AllowsLocalHero
                || GlobalSaveData._loaded == null || GlobalSaveData._loaded.Pointer != GlobalPointer
                || GlobalSaveData._loaded.currentCampaign != Campaign || GlobalSaveData._loaded.currentChallenge != Challenge
                || CampaignSaveData.current == null || CampaignSaveData.current.Pointer != CampaignPointer
                || World == 0 || WorldKey() != World
                || !Islands.TryGetValue(ContextKey, out var state) || !ReferenceEquals(state, _current)
                || state.World != World || !state.Ready) return false;
            // An unresolved origin must not generate fresh anonymous snapshots that could evict
            // its last provable source, and a context without a proven epoch must not be written at all.
            if (state.Unresolved || state.Epoch == null || state.Seats.Any(x => x.Owner == null)) { Log("save-preserve-unresolved", null); return false; }
            var rows = new List<HeroPurchaseReceipt>();
            foreach (var seat in state.Seats)
            {
                var copy = seat.Receipt.Copy(true);
                foreach (var pair in Owners)
                {
                    if (!ReferenceEquals(pair.Value, seat)) continue;
                    if (!Lives.TryGetValue(seat.Receipt.Id, out var life) || !ReferenceEquals(life, seat.Owner) || !ValidOwner(life)) return false;
                    int count = 0;
                    foreach (var record in Island.objects)
                        if (record != null && record.uniqueID == pair.Key && IsCharacterRecord(record)) count++;
                    if (count != 1) return false;
                    copy.NativeId = pair.Key;
                }
                // An active, confirmed paid life must be present as one unique native Character
                // row in this same normal capture. Old reserved seats with no owner remain valid.
                if (seat.Owner != null && copy.NativeId.Length == 0)
                { Log("save-paid-owner-missing-id", null); return false; }
                rows.Add(copy);
            }
            string json = JsonUtility.ToJson(Island, false);
            if (string.IsNullOrEmpty(json)) return false;
            string hash;
            try { hash = HeroRecruitmentFingerprint.Hash(json, state.Epoch); }
            catch (Exception e) { state.ReadOnly = true; Log("save-json", e); return false; }
            if (!HeroNativeRights.Stage(state.ContextKey, Campaign, Challenge, Island, hash, state.Epoch, rows, state.NewEpoch))
            { state.ReadOnly = true; Log("save-native-stage-failed", null); return false; }
            state.NativeAuthority = true;
            state.HasBaseline = true;
            state.ReadOnly = false;
            // The sidecar remains a historical mirror. Its failure cannot discard rights now
            // staged beside the wallet in the native Global save.
            if (!Commit(state, disk => disk.Archive.Record(state.Epoch, hash, rows, HeroRecruitmentFingerprint.Kind, false)))
                Log("save-sidecar-mirror-failed", null);
            Log("saved:" + state.Epoch.Substring(0, 8) + ":" + hash.Substring(0, 8), null);
            return true;
        }

        // Called when this capture did not complete. The responsibility is taken from the frozen
        // source identity this capture already verified (loaded Global object + campaign object +
        // island slot): it must exist even when the prefs cannot be read at this moment, and a
        // later healthy read only resolves its exact identity, never clears it. An island whose
        // slot cannot be proven carries no responsibility (issue #153: no slot, no custody — a
        // land value is never turned into another island's key).
        internal void NoteCaptureInvalid()
        {
            try
            {
                if (!HeroArcherNetwork.AllowsLocalHero) return;
                var owner = SourcePointer != IntPtr.Zero ? SourcePointer : CampaignPointer;
                if (GlobalPointer == IntPtr.Zero || owner == IntPtr.Zero) return;
                if (GlobalSaveData._loaded == null || GlobalSaveData._loaded.Pointer != GlobalPointer) return;
                HeroNativeRights.NoteCaptureInvalid(owner, ResolveInvalidSlot(owner));
            }
            catch (Exception e) { Log("save-capture-invalid", e); }
        }

        // A capture that failed before its first GetID never froze the island. The only slot
        // evidence then is the frozen owner's own current island — the same object the save
        // arguments describe (campaign/challenge already verified at FreezeSource/Capture, the
        // land cross-checked against the argument) — resolved through the campaign's _islands
        // table. The land value never becomes a key or a slot by itself: land 0 names no island.
        private int ResolveInvalidSlot(IntPtr owner)
        {
            if (IslandSlot >= 0) return IslandSlot;
            try
            {
                var campaign = CampaignSaveData.current;
                if (campaign == null || campaign.Pointer != owner) return -1;
                var island = campaign.CurrentIsland;
                if (island == null || Land < 0 || island.land != Land) return -1;
                return HeroRecruitmentArchive.IslandSlot(campaign, island, out int slot)
                    == HeroRecruitmentArchive.IslandSlotLookup.Ok ? slot : -1;
            }
            catch { return -1; }
        }
    }

    internal sealed class LoadCapture
    {
        internal LoadCapture Previous;
        private IslandState State;
        private IslandState Old;
        private IslandState OldCurrent;
        private string Hash;
        private int BaselineKind;
        private bool BaselineLegacy;
        private bool Confirmable;
        private bool GenerationPending;
        private readonly Dictionary<string, IntPtr> Frozen = new(StringComparer.Ordinal);
        private readonly HashSet<string> Seen = new(StringComparer.Ordinal);
        private SourceShape Source;
        private bool Nested;         // a nested load replaced runtime state under this capture
        internal bool Conflict;

        // issue-85: frozen facts of one native load. Only a load whose original resolution was
        // unresolved with no epoch/seat can ever adopt a disjoint history, so the freeze is
        // prepared for that shape only; it never writes and never mutates runtime state.
        private sealed class SourceShape
        {
            internal string Json;
            internal string ContextKey;
            internal long World;
            internal IntPtr Campaign;
            internal IntPtr Island;
            internal bool CarryFalse;
            internal bool Valid;
            // issue-85: the original native objects list, retained as the exact Interop wrapper for
            // the lifetime of this load only. Holding the wrapper keeps its own strong il2cpp GC
            // handle alive, which roots the native list and its entries even after
            // TryPopObjectsToScene disconnects island.objects (actual 2.4 cleanup 734990 stores null
            // into this+0x50 at 7349EE). End re-enumerates this list instead of the consumed field;
            // dropping the reference after End lets the wrapper be finalized. The wrapper owns its
            // GC handle: never call il2cpp_gchandle_new/free/Dispose or Unity asset retention flags.
            internal Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> Objects;
            internal readonly HashSet<string> CharacterIds = new(StringComparer.Ordinal);
            internal readonly List<Member> Members = new();
        }

        // One frozen native table member: object identity plus native id and Character
        // classification, so a replacement instance with the same id is still detected at End.
        private sealed class Member
        {
            internal IntPtr Pointer;
            internal string Id;
            internal bool Character;
        }

        // Freeze the native island facts the gate needs: every member's object identity, native
        // id and Character classification (compared order-insensitively at End), the raw JSON and
        // the complete Character id set. The original native list wrapper is retained as well, so
        // End can re-read the same table after native TryPopObjectsToScene consumed the field. A
        // null record, duplicate non-empty object id, missing/oversized/duplicate Character id or
        // a table without Character invalidates only the disjoint gate; it never turns the load
        // itself into a conflict. null componentData2 or null elements classify as non-Character
        // under the existing rule.
        private static SourceShape CaptureSource(IslandSaveData island, string json, string contextKey, long world, CampaignSaveData campaign)
        {
            var source = new SourceShape
            {
                Json = json, ContextKey = contextKey, World = world, Valid = island != null && campaign != null,
                Campaign = campaign != null ? campaign.Pointer : IntPtr.Zero,
                Island = island != null ? island.Pointer : IntPtr.Zero
            };
            if (!source.Valid) return source;
            try { source.CarryFalse = !campaign.carryForward.present; }
            catch { source.CarryFalse = false; }
            Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> objects;
            try { objects = island.objects; }
            catch { source.Valid = false; return source; }
            if (objects == null) { source.Valid = false; return source; }
            source.Objects = objects;
            try
            {
                int characters = 0;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var record in objects)
                {
                    if (record == null) { source.Valid = false; continue; }
                    bool character = IsCharacterRecord(record);
                    string id = record.uniqueID;
                    source.Members.Add(new Member { Pointer = record.Pointer, Id = id ?? string.Empty, Character = character });
                    if (string.IsNullOrEmpty(id))
                    {
                        if (character) source.Valid = false;
                        continue;
                    }
                    if (!ids.Add(id)) { source.Valid = false; continue; }
                    if (!character) continue;
                    if (id.Length > 256) { source.Valid = false; continue; }
                    source.CharacterIds.Add(id); characters++;
                }
                if (characters == 0) source.Valid = false;
            }
            catch { source.Valid = false; }
            return source;
        }

        // issue-85: End must not read the consumed island.objects field. Re-enumerate the retained
        // wrapper (the same native list Begin froze) and require the complete member pointer+id
        // multiset to be unchanged, ignoring native sorting: new/removed/replaced or null members,
        // changed ids and unreadable data reject. Classification may only be lost
        // (Character -> nonCharacter, demonstrated native ObjectData.SetDecay) and never gained.
        // A null island.objects field is the confirmed native consumption; a non-null field must
        // still be the same native list. End may legitimately see zero Characters at this point;
        // Begin already required at least one.
        private static bool TryRevalidateSource(SourceShape source, IslandSaveData island)
        {
            if (source == null || !source.Valid || source.Objects == null || island == null) return false;
            Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> field;
            try { field = island.objects; }
            catch { return false; }
            if (field != null && field.Pointer != source.Objects.Pointer) return false;
            try
            {
                var live = new List<Member>(source.Members.Count);
                foreach (var record in source.Objects)
                {
                    if (record == null) return false;
                    live.Add(new Member { Pointer = record.Pointer, Id = record.uniqueID ?? string.Empty, Character = IsCharacterRecord(record) });
                }
                return SameLifecycleShape(source.Members, live);
            }
            catch { return false; }
        }

        // Pointer-keyed multiset comparison of the retained table. Every Begin member must appear
        // exactly once (a duplicate live pointer can never consume its entry twice), ids must be
        // identical, and a non-Character may not become a Character.
        private static bool SameLifecycleShape(List<Member> frozen, List<Member> live)
        {
            if (frozen == null || live == null || frozen.Count != live.Count) return false;
            var byPointer = new Dictionary<IntPtr, Member>(frozen.Count);
            foreach (var member in frozen)
                if (byPointer.ContainsKey(member.Pointer)) return false; // duplicate native pointer: fail closed
                else byPointer.Add(member.Pointer, member);
            foreach (var member in live)
            {
                if (!byPointer.TryGetValue(member.Pointer, out var original)) return false;
                if (!string.Equals(original.Id, member.Id, StringComparison.Ordinal)) return false;
                if (!original.Character && member.Character) return false;
                byPointer.Remove(member.Pointer);
            }
            return byPointer.Count == 0;
        }

        // Transport data is count/type only; absence of native ids must never be inferred into it.
        // A missing or unreadable structure is not `present == false` and therefore never unlocks.
        private static bool TryReadCarryFalse(CampaignSaveData campaign)
        {
            try { return campaign != null && !campaign.carryForward.present; }
            catch { return false; }
        }

        // An unsaved purchase in this world must never be silently replaced by a fresh epoch.
        private static bool HasLivePurchase(IslandState state, long world)
            => state != null && state.Seats.Count > 0 && state.World == world;

        // issue-85: a successful load whose complete Character set is disjoint from every unknown
        // paid history row adopts one fresh epoch with a kind-2 empty baseline instead of staying
        // unresolved forever. Every precondition is re-verified at End against the frozen Begin
        // facts, and the archive-only gate is re-run on the fresh disk inside the commit CAS; the
        // unlock state is published only after the sidecar write was verified.
        private void TryAdoptDisjointHistory()
        {
            var source = Source;
            if (source == null || !source.Valid || source.World == 0 || Conflict || !State.Unresolved || State.Epoch != null || State.Seats.Count > 0) return;
            if (Previous != null || Nested || !HeroArcherNetwork.AllowsLocalHero) return;
            var campaign = CampaignSaveData.current;
            if (campaign == null || campaign.Pointer != source.Campaign || campaign.CurrentIsland == null
                || campaign.CurrentIsland.Pointer != source.Island) return;
            if (GlobalSaveData.loaded == null
                || !TryIslandSlot(campaign, campaign.CurrentIsland, out int adoptSlot)
                || !TryContextKey(GlobalSaveData.loaded.currentCampaign, GlobalSaveData.loaded.currentChallenge, adoptSlot, out string contextKey)
                || contextKey != source.ContextKey || WorldKey() != source.World) return;
            if (!source.CarryFalse || !TryReadCarryFalse(campaign)) return;
            if (HasLivePurchase(Old, source.World) || HasLivePurchase(OldCurrent, source.World)) return;
            if (!TryRevalidateSource(source, campaign.CurrentIsland)) { Log("disjoint-source-changed", null); return; }
            string scope = HeroRecruitmentArchive.NewScope();
            string hash;
            try { hash = HeroRecruitmentFingerprint.Hash(source.Json, scope); }
            catch (Exception e) { Log("disjoint-json", e); return; }
            var pending = new IslandState { ContextKey = source.ContextKey, Epoch = scope, NewEpoch = true, World = source.World, Ready = true };
            // The history decision keeps using the complete Begin Character set: an id that was a
            // Character at Begin must still block this adoption even if native decay erased its
            // descriptor, otherwise a consumed paid character could be re-charged.
            if (!Commit(pending, disk => HeroRecruitmentContexts.DisjointHistory(disk.Archive, source.ContextKey, source.Json, source.CharacterIds)
                    && disk.Archive.ConfirmBaseline(scope, hash, Array.Empty<HeroPurchaseReceipt>(), HeroRecruitmentFingerprint.Kind, false)))
            { State.ReadOnly = true; Log("disjoint-unconfirmed", null); return; }
            State.ReadOnly = false;
            State.Epoch = scope;
            State.MatchKind = "disjoint-history-fresh";
            State.MatchHash = hash;
            State.MatchHashKind = HeroRecruitmentFingerprint.Kind;
            State.MatchLegacy = false;
            State.Unresolved = false;
            State.HasBaseline = true;
            State.NewEpoch = false;
            BaselineKind = HeroRecruitmentFingerprint.Kind;
            BaselineLegacy = false;
            Log("disjoint-history-fresh:" + source.ContextKey.Substring(0, 8) + ":" + scope.Substring(0, 8), null);
        }

        internal void Begin(IslandSaveData island)
        {
            // A nested load replaces Islands/_current underneath every enclosing capture: mark all
            // ancestors before any early return so none of them may adopt a disjoint history.
            for (var parent = Previous; parent != null; parent = parent.Previous) parent.Nested = true;
            _contextFrame = -1; _lastTickFrame = -1;
            if (!HeroArcherNetwork.AllowsLocalHero || island == null || GlobalSaveData.loaded == null) return;
            if (!TryIslandSlot(CampaignSaveData.current, island, out int islandSlot)
                || !TryContextKey(GlobalSaveData.loaded.currentCampaign, GlobalSaveData.loaded.currentChallenge, islandSlot, out string contextKey)) return;
            string json = JsonUtility.ToJson(island, false);
            if (string.IsNullOrEmpty(json)) return;
            // A virgin island is recorded by the generation bridge after ApplyToScene succeeds;
            // this load must not pre-create its epoch or baseline.
            GenerationPending = IsVirgin(island);
            // The previous runtime state of this exact source is the session's own evidence (its
            // paid receipts and observed bindings): it is found by frozen Global/owner/slot, not by
            // the numeric alias (which a catalog slot move reuses for another live source).
            bool sourceProven = TrySourceIdentity(out IntPtr srcGlobal, out IntPtr srcOwner, out int srcSlot)
                && srcSlot == islandSlot;
            Islands.TryGetValue(contextKey, out Old); OldCurrent = _current;
            if (sourceProven && !SameSource(Old, srcGlobal, srcOwner, islandSlot))
                Old = FindSourceState(srcGlobal, srcOwner, islandSlot);
            bool sessionQualified = sourceProven && Old != null && Old.HasSession
                && SameSource(Old, srcGlobal, srcOwner, islandSlot);
            State = NewState(contextKey, WorldKey(), island, json, GenerationPending, Old, sessionQualified);
            if (sourceProven) { State.SourceGlobal = srcGlobal; State.SourceOwner = srcOwner; State.SourceSlot = islandSlot; }
            // Keep the exact matched snapshot's stored hash/kind/provenance instead of recomputing;
            // a state without an exact match writes its own kind-2 baseline.
            Hash = State.MatchHash; BaselineKind = State.MatchHashKind; BaselineLegacy = State.MatchLegacy;
            if (Hash == null && State.Epoch != null && !State.Unresolved && !GenerationPending)
            {
                try { Hash = HeroRecruitmentFingerprint.Hash(json, State.Epoch); BaselineKind = HeroRecruitmentFingerprint.Kind; BaselineLegacy = false; }
                catch (Exception e) { Hash = null; BaselineKind = 0; Log("load-json", e); }
            }
            // An exact saved receipt or a genuinely loaded source can become the confirmed baseline.
            // A mismatched reserved seat must keep its original provable source.
            Confirmable = Hash != null && State.Epoch != null && !State.Unresolved && !GenerationPending
                && (State.Seats.Count == 0 || State.Seats.All(x => x.Receipt.NativeId.Length > 0));
            State.Ready = false;
            if (Old == null && Islands.Count >= HeroRecruitmentArchive.MaxContexts) { State = null; return; }
            // Never assign purchases from an earlier unsaved session to this newly loaded snapshot.
            // The replaced state of this exact source is dropped wherever a slot move left it; any
            // other live source sitting on this alias is preserved under its own private key.
            RemoveState(Old);
            InstallState(contextKey, State); _current = State;
            if (State.SessionRebind) CarrySessionBindings(State, Old);
            foreach (var record in island.objects)
            {
                if (record == null || !IsCharacterRecord(record)) continue;
                string id = record.uniqueID;
                if (string.IsNullOrEmpty(id) || id.Length > 256 || Frozen.ContainsKey(id)) { Conflict = true; return; }
                Frozen.Add(id, record.Pointer);
            }
            // issue-85: freeze the source facts only for the exact shape the disjoint-history gate
            // could ever unlock (unresolved, no epoch, no seat). It never writes.
            if (State.Unresolved && State.Epoch == null && State.Seats.Count == 0 && !Conflict && !GenerationPending)
                Source = CaptureSource(island, json, contextKey, State.World, CampaignSaveData.current);
        }

        // A source re-entered under an open capture-failure responsibility keeps the bindings the
        // session had already observed: the carried seat reuses that exact candidate identity, so
        // the loaded island's own Character row can re-bind it. Seats without observed evidence
        // stay reserved (receipts preserved, no owner, no charge).
        private static void CarrySessionBindings(IslandState state, IslandState previous)
        {
            if (state == null || previous == null || ReferenceEquals(state, previous)) return;
            foreach (var oldSeat in previous.Seats)
            {
                if (!ValidOwner(oldSeat.Owner)) continue;
                var seat = state.Seats.Find(x => x.Receipt.Id == oldSeat.Receipt.Id && x.Receipt.Side == oldSeat.Receipt.Side);
                if (seat == null || seat.Owner != null) continue;
                if (state.Seats.Any(x => !ReferenceEquals(x, seat) && ReferenceEquals(x.Owner, oldSeat.Owner))) continue;
                Bind(seat, oldSeat.Owner, true);
            }
        }

        internal void Capture(IslandSaveData.ObjectData data, Persistent root)
        {
            if (State == null || data == null || root == null || Conflict) return;
            var seat = State.Seats.Find(x => x.Receipt.NativeId.Length > 0 && x.Receipt.NativeId == data.uniqueID);
            bool adopted = false;
            if (seat == null && State.SessionRebind)
            {
                // The session reliably observed this row's live object as the seat's owner. The id
                // only becomes this seat's native record after the same frozen unique-Character
                // check below, so no row is guessed.
                seat = State.Seats.Find(x => MatchesRoot(x.Owner, root));
                adopted = seat != null;
            }
            if (seat == null) return;
            if (!Frozen.TryGetValue(data.uniqueID, out IntPtr pointer) || pointer != data.Pointer) { Conflict = true; return; }
            var actor = root.GetComponent<Character>();
            if (actor == null) return; // Native decay/role differences reserve the seat.
            var candidate = GetCandidate(actor, false);
            if (candidate == null) return;
            if (adopted && !ReferenceEquals(seat.Owner, candidate)) { Conflict = true; return; }
            if (Seen.Contains(data.uniqueID))
            {
                if (!ReferenceEquals(seat.Owner, candidate)) Conflict = true;
                return;
            }
            if (State.Seats.Any(x => !ReferenceEquals(x, seat) && ReferenceEquals(x.Owner, candidate))) { Conflict = true; return; }
            Seen.Add(data.uniqueID);
            if (adopted) seat.Receipt.NativeId = data.uniqueID;
            Bind(seat, candidate, true);
        }

        internal void End(bool success)
        {
            try
            {
                _contextFrame = -1; _lastTickFrame = -1;
                if (State == null) return;
                if (!success)
                {
                    // The restored previous state is the owner of any carried bindings: restore it
                    // first so releasing this failed attempt's seats cannot hand back an embarkee
                    // that the previous state still owns. Seats this load bound on its own are
                    // still released below.
                    if (Old != null) Islands[State.ContextKey] = Old;
                    foreach (var seat in State.Seats) Unbind(seat);
                    if (Old == null) Islands.Remove(State.ContextKey);
                    _current = OldCurrent;
                    return;
                }
                if (Old != null) foreach (var seat in Old.Seats) Unbind(seat);
                if (Conflict) foreach (var seat in State.Seats) Unbind(seat);
                State.Ready = true;
                State.World = WorldKey();
                foreach (var seat in State.Seats) if (seat.Owner != null) seat.Owner.World = State.World;
                if (!Conflict && Confirmable && !State.NativeAuthority)
                {
                    var receipts = State.Seats.Select(x => x.Receipt.Copy()).ToArray();
                    if (Commit(State, disk => disk.Archive.ConfirmBaseline(State.Epoch, Hash, receipts, BaselineKind, BaselineLegacy))) State.HasBaseline = true;
                    else { State.ReadOnly = true; State.HasBaseline = false; Log("baseline-unconfirmed", null); }
                }
                if (!Conflict) TryAdoptDisjointHistory();
                // Only after the state is Ready and its world binding is confirmed may a loaded
                // seat borrow its embarkee; a failed load never reaches this block.
                if (!Conflict)
                    foreach (var seat in State.Seats)
                        if (seat.Owner != null) BorrowEmbarkee(seat.Owner);
                Log("loaded:" + State.MatchKind + ":ctx=" + State.ContextKey.Substring(0, 8) + ":epoch=" + Short(State.Epoch)
                    + ":hk=" + BaselineKind + ":v1=" + BaselineLegacy
                    + ":seats=" + State.Seats.Count + ":bound=" + State.Seats.Count(x => x.Owner != null), null);
            }
            finally
            {
                // issue-85: release this load's retained native list wrapper on every exit path
                // (failure, StateNull, conflict, exception, re-entry). Dropping the last managed
                // reference lets the wrapper finalize and free the GC handle that kept the native
                // list alive; the wrapper owns that handle, so never free/dispose it by hand here.
                Source = null;
            }
        }
    }

    internal static bool IsCharacterRecord(IslandSaveData.ObjectData data)
    {
        if (data?.componentData2 == null) return false;
        foreach (var component in data.componentData2)
            if (component != null && component.name == "Character" && component.type == "CharacterData") return true;
        return false;
    }

    // Native strong precondition for a fresh generation: brand-new island, no played day yet.
    private static bool IsVirgin(IslandSaveData island)
    {
        try { return island != null && island.isNew && island.playTimeDays == 0.0; }
        catch { return false; }
    }

    // Single writer for the sidecar: mutate the freshly loaded disk state, keep the context/epoch
    // registration in the same atomic compare-and-swap, and clear the new-epoch marker only after
    // the file was verified on disk. Any failure leaves the archive untouched and callers go read-only.
    private static bool Commit(IslandState state, Func<HeroRecruitmentArchiveStore.ReadResult, bool> mutate)
    {
        if (state == null || state.ContextKey == null || state.Epoch == null) return false;
        var disk = HeroRecruitmentArchiveStore.Load(ArchivePath);
        if (!disk.Writable || disk.Archive == null) return false;
        if (!mutate(disk)) return false;
        if (!disk.Archive.EnsureContext(state.ContextKey, state.Epoch, state.NewEpoch)) return false;
        if (!HeroRecruitmentArchiveStore.Save(ArchivePath, disk, disk.Archive)) return false;
        state.NewEpoch = false;
        return true;
    }

    private static string Short(string value) => string.IsNullOrEmpty(value) || value.Length < 8 ? "" : value.Substring(0, 8);

    // Brand-new islands do not necessarily run TryPopObjectsToScene. Freeze the native isNew
    // fact before ApplyToScene, then create a fresh epoch + empty baseline only after successful
    // generation of that same island in the same runtime world. Repeated ApplyToScene of an
    // already recorded generation is recognized by runtime world + island object identity.
    internal sealed class VirginCapture
    {
        private IntPtr CampaignPointer, IslandPointer;
        private string ContextKey;
        private long SourceWorld;
        private bool Done;
        internal void Begin(CampaignSaveData campaign)
        {
            if (Done || campaign == null || !HeroArcherNetwork.AllowsLocalHero || GlobalSaveData.loaded == null) return;
            var island = campaign.CurrentIsland;
            if (!IsVirgin(island)) return;
            if (!TryIslandSlot(campaign, island, out int islandSlot)
                || !TryContextKey(GlobalSaveData.loaded.currentCampaign, GlobalSaveData.loaded.currentChallenge, islandSlot, out string contextKey)) return;
            long world = WorldKey();
            if (world == 0) return;
            // Once-guard: this virgin generation already owns an epoch in this world.
            if (Islands.TryGetValue(contextKey, out var runtime) && runtime.VirginIsland == island.Pointer && runtime.VirginWorld == world) return;
            CampaignPointer = campaign.Pointer; IslandPointer = island.Pointer;
            ContextKey = contextKey; SourceWorld = world;
        }
        internal void Complete(CampaignSaveData campaign)
        {
            if (ContextKey == null || Done || campaign == null || campaign.Pointer != CampaignPointer
                || CampaignSaveData.current == null || CampaignSaveData.current.Pointer != CampaignPointer
                || !HeroArcherNetwork.AllowsLocalHero || GlobalSaveData.loaded == null) return;
            var island = campaign.CurrentIsland;
            // Failed generation, or a different island/campaign/world: alter no context and no data.
            if (island == null || island.Pointer != IslandPointer || !IsVirgin(island)) return;
            if (!TryIslandSlot(campaign, island, out int islandSlot)
                || !TryContextKey(GlobalSaveData.loaded.currentCampaign, GlobalSaveData.loaded.currentChallenge, islandSlot, out string contextKey) || contextKey != ContextKey) return;
            long world = WorldKey();
            if (world == 0 || world != SourceWorld) return;
            if (!Islands.ContainsKey(contextKey) && Islands.Count >= HeroRecruitmentArchive.MaxContexts) return;
            string json = JsonUtility.ToJson(island, false);
            if (string.IsNullOrEmpty(json)) return;
            // New generation: a fresh opaque epoch and an empty baseline, committed atomically with
            // the context mapping. Old epochs stay untouched for rollback; nothing is inherited.
            string scope = HeroRecruitmentArchive.NewScope();
            string hash;
            try { hash = HeroRecruitmentFingerprint.Hash(json, scope); }
            catch (Exception e) { Log("virgin-json", e); return; }
            var pending = new IslandState { ContextKey = contextKey, Epoch = scope, MatchKind = "new-generation", NewEpoch = true, World = world, Ready = true };
            if (!Commit(pending, disk => disk.Archive.ConfirmBaseline(scope, hash, Array.Empty<HeroPurchaseReceipt>(), HeroRecruitmentFingerprint.Kind, false)))
            { Log("virgin-baseline-unconfirmed", null); return; }
            Done = true;
            var state = NewState(contextKey, world, island, json, false, null, false);
            state.VirginIsland = island.Pointer; state.VirginWorld = world;
            if (HeroNativeRights.TrySourceIdentity(islandSlot, out var srcGlobal, out var srcOwner))
            { state.SourceGlobal = srcGlobal; state.SourceOwner = srcOwner; state.SourceSlot = islandSlot; }
            InstallState(contextKey, state); _current = state;
            _contextFrame = -1; _lastTickFrame = -1; _candidateCacheUntil = 0;
            Log("virgin-epoch:" + contextKey.Substring(0, 8) + ":" + scope.Substring(0, 8), null);
        }
    }

    [HarmonyPatch(typeof(CampaignSaveData), nameof(CampaignSaveData.ApplyToScene))]
    internal static class VirginPatch
    {
        [HarmonyPrefix] private static void Before(CampaignSaveData __instance, out VirginCapture __state)
        { __state = new(); try { __state.Begin(__instance); } catch (Exception e) { Log("virgin-before", e); } }
        [HarmonyPostfix] private static void After(CampaignSaveData __instance, VirginCapture __state)
        { try { __state?.Complete(__instance); } catch (Exception e) { Log("virgin-after", e); } }
    }

    // Real 2.4 Character.ReplaceBy(string): one long native implementation, returns the exact
    // successor before Demote/Promote despawns the old owner. Transfer only an already purchased
    // receipt; no global role/health changes, and no inference from position or native IDs.
    internal sealed class ReplacementCapture
    {
        private Seat Seat;
        private Candidate Owner;
        private IslandState Island;
        internal void Begin(Character character)
        {
            if (_current == null || _load != null || !HeroArcherNetwork.AllowsLocalHero) return;
            var seat = _current.Seats.Find(x => Matches(x.Owner, character));
            if (seat == null) return;
            Seat = seat; Owner = seat.Owner; Island = _current;
        }
        internal void Complete(Character successor)
        {
            if (Seat == null || successor == null || !ReferenceEquals(Seat.Owner, Owner)
                || !ValidOwner(Owner) || !Island.Seats.Contains(Seat)) return;
            if (!TryContext(out string contextKey, out long world) || contextKey != Island.ContextKey || world != Island.World
                || !OptionalQoLScope.IsCurrent(successor)) return;
            var next = GetCandidate(successor, false);
            if (next == null || Islands.Values.Any(x => x.Seats.Any(s => !ReferenceEquals(s, Seat) && ReferenceEquals(s.Owner, next)))) return;
            if (Bind(Seat, next))
            {
                BorrowEmbarkee(next);   // the successor is public (bound) before it is borrowed
                Log("role-transfer:" + Seat.Receipt.Id.ToString("N"), null);
            }
        }
    }

    [HarmonyPatch(typeof(Character), "ReplaceBy", new[] { typeof(string) })]
    internal static class ReplacementPatch
    {
        [HarmonyPrefix] private static void Before(Character __instance, out ReplacementCapture __state)
        { __state = new(); try { __state.Begin(__instance); } catch (Exception e) { Log("replace-begin", e); } }
        [HarmonyPostfix] private static void After(Character __result, ReplacementCapture __state)
        { try { __state?.Complete(__result); } catch (Exception e) { Log("replace-end", e); } }
    }

    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.Save), new[] { typeof(int), typeof(int), typeof(int) })]
    internal static class SavePatch
    {
        [HarmonyPrefix] private static void Before(int __0, int __1, int __2, out SaveCapture __state)
        {
            try { HeroShop.CancelPendingTransactions(); } catch (Exception e) { Log("save-cancel-payment", e); }
            __state = new() { Previous = _save, Campaign = __0, Land = __1, Challenge = __2 };
            __state.FreezeSource();
            _save = __state;
        }
        [HarmonyPostfix, HarmonyPriority(Priority.Last)] private static void After(SaveCapture __state)
        { try { __state?.Apply(); } catch (Exception e) { if (_current != null) _current.ReadOnly = true; Log("save", e); } }
        [HarmonyFinalizer] private static Exception Finally(Exception __exception, SaveCapture __state)
        {
            // A throwing island save never reached the normal tail: the same capture invalidity
            // responsibility applies as for an internally caught failure without a marker.
            if (__exception != null)
            {
                try { __state?.NoteCaptureInvalid(); } catch { }
            }
            if (ReferenceEquals(_save, __state)) _save = __state?.Previous;
            return __exception;
        }
    }
    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.UpdateSavedWithRevisions))]
    internal static class SaveMarkerPatch
    {
        [HarmonyPostfix] private static void After(IslandSaveData __instance)
        {
            if (_save?.Island != null && __instance != null && _save.Island.Pointer == __instance.Pointer)
                _save.MarkerSeen = true;
        }
    }
    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.GetID), new[] { typeof(Persistent) })]
    internal static class GetIdPatch
    {
        [HarmonyPostfix] private static void After(Persistent __0, string __result)
        { try { _save?.Capture(__0, __result); } catch (Exception e) { if (_save != null) _save.Conflict = true; Log("get-id", e); } }
    }
    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.TryPopObjectsToScene))]
    internal static class PopPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)] private static void Before(IslandSaveData __instance, out LoadCapture __state)
        {
            __state = new() { Previous = _load }; _load = __state;
            try { __state.Begin(__instance); } catch (Exception e) { __state.Conflict = true; Log("load", e); }
        }
        [HarmonyFinalizer] private static Exception Finally(Exception __exception, bool __result, LoadCapture __state)
        {
            try { __state?.End(__exception == null && __result); } catch (Exception e) { Log("load-end", e); }
            finally { if (ReferenceEquals(_load, __state)) _load = __state?.Previous; }
            return __exception;
        }
    }
    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.TryCreateOrFind))]
    internal static class CreatePatch
    {
        [HarmonyPostfix] private static void After(IslandSaveData.ObjectData __0, Persistent __result)
        { try { _load?.Capture(__0, __result); } catch (Exception e) { if (_load != null) _load.Conflict = true; Log("load-bind", e); } }
    }

    // Actual recycling entry, also reached by TryDespawn. Delayed requests merely schedule a
    // future operation and must keep ownership until the immediate execution call. The prefix
    // freezes the exact candidate record (identity + life + claim), not just the root pointer:
    // the postfix may then only clear that frozen life, and repeated/nested calls can never
    // reach a wrapper that was reused after this recycle.
    [HarmonyPatch(typeof(Pool), nameof(Pool.FastDespawn), new[] { typeof(GameObject), typeof(float), typeof(bool) })]
    internal static class DespawnPatch
    {
        internal struct Capture { internal GameObject Root; internal object Owner; internal long Life; internal object Claim; }
        [HarmonyPrefix] private static void Before(GameObject __0, float __1, out Capture __state)
        {
            __state = default;
            try
            {
                if (__1 > 0f || __0 == null || !BoundRoots.Contains(__0.Pointer)) return;
                var character = __0.GetComponent<Character>();
                if (character == null || !Candidates.TryGetValue(character.Pointer, out var owner)
                    || owner.Root != __0.Pointer || owner.GoId != __0.GetInstanceID()) return;
                __state = new() { Root = __0, Owner = owner, Life = owner.Life, Claim = owner.Embarkee };
            }
            catch (Exception e) { Log("pool-before", e); }
        }
        [HarmonyPostfix] private static void After(Capture __state)
        {
            try
            {
                Candidate owner = __state.Owner as Candidate;
                if (owner == null) return;
                if (__state.Root != null && __state.Root.activeInHierarchy) return;   // not provably despawned yet
                if (!Candidates.TryGetValue(owner.Pointer, out var live) || !ReferenceEquals(live, owner)
                    || owner.Life != __state.Life || !ReferenceEquals(owner.Embarkee, __state.Claim)) return;
                UnbindSeatsFor(owner);
                RetireCandidate(owner);
            }
            catch (Exception e) { Log("pool-after", e); }
        }
    }
}
