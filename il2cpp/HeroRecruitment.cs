using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
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
        internal int FallenSeatMask; // Session-only torn-flag feedback; never purchase authority.
    }
    private static readonly Dictionary<IntPtr, Candidate> Candidates = new();
    private static readonly Dictionary<string, IslandState> Islands = new(StringComparer.Ordinal);
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
    internal static string ArchivePath => System.IO.Path.Combine(Paths.ConfigPath, "KingdomEnhancedMod", "ModSave", "hero-identities.v1.json");

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
                foreach (var island in Islands.Values)
                    foreach (var seat in island.Seats)
                        if (ReferenceEquals(seat.Owner, old)) Unbind(seat);
                Candidates.Remove(key);
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
                    Candidates.Remove(owner.Pointer);
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
                + (seat.Receipt.NativeId.Length > 0 ? "native" : "reserved") + ":" + (seat.Owner != null ? "bound" : "unbound"));
        return "ctx=" + Short(state.ContextKey) + " epoch=" + Short(state.Epoch) + " kind=" + state.MatchKind
            + " hk=" + state.MatchHashKind + " v1=" + state.MatchLegacy
            + " seats=" + state.Seats.Count + " baseline=" + state.HasBaseline + " unresolved=" + state.Unresolved
            + " readonly=" + state.ReadOnly + " [" + string.Join(",", seats) + "]";
    }

    // The native shop owns payment. This method creates a receipt only after all current gates
    // and the archive's write compatibility have been checked; it never changes coins or saves.
    internal static bool TryPurchase(out string reason)
    {
        reason = "英雄招募暂不可用";
        try
        {
            Tick();
            if (!FindPurchase(out var candidate, out int side, true)) { reason = StatusText; return false; }
            var disk = HeroRecruitmentArchiveStore.Load(ArchivePath);
            if (!disk.Writable) { _current.ReadOnly = true; reason = "英雄附加档不可写，未招募"; return false; }
            if (disk.Archive != null && _current.Epoch != null && !disk.Archive.Scopes.ContainsKey(_current.Epoch)
                && disk.Archive.Scopes.Count >= HeroRecruitmentArchive.MaxScopes)
            { _current.ReadOnly = true; reason = "英雄附加档容量已满"; return false; }
            if (disk.Archive != null && !disk.Archive.TryGetContext(_current.ContextKey, out _)
                && disk.Archive.Contexts.Count >= HeroRecruitmentArchive.MaxContexts)
            { _current.ReadOnly = true; reason = "英雄附加档容量已满"; return false; }
            if (!FindPurchase(out var fresh, out int freshSide, true) || !ReferenceEquals(fresh, candidate) || freshSide != side) return false;
            var seat = new Seat { Receipt = new() { Id = Guid.NewGuid(), Side = side } };
            if (!Bind(seat, candidate)) { reason = "无法确认英雄死亡监听，未招募"; return false; }
            var purchaseIsland = _current;
            purchaseIsland.Seats.Add(seat);
            bool activated = false;
            try { activated = HeroArcherRuntime.TryActivatePurchased(candidate.Actor) && ReferenceEquals(_current, purchaseIsland); }
            catch (Exception e) { Log("activation", e); }
            if (!activated)
            {
                purchaseIsland.Seats.Remove(seat); Unbind(seat);
                reason = "英雄外观或战斗初始化失败，未招募";
                return false;
            }
            reason = side < 0 ? "左侧英雄弓手训练完成" : "右侧英雄弓手训练完成";
            purchaseIsland.FallenSeatMask &= ~(side < 0 ? 1 : 2);
            Log("purchased:" + seat.Receipt.Id.ToString("N"), null);
            return true;
        }
        catch (Exception e) { Log("purchase", e); return false; }
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
            if (_current == null || _current.ContextKey != contextKey || _current.World != world)
            {
                _candidateCacheUntil = 0;
                if (!Islands.TryGetValue(contextKey, out var next))
                {
                    if (Islands.Count >= HeroRecruitmentArchive.MaxContexts) return;
                    next = NewState(contextKey, world, null, null, false); Islands.Add(contextKey, next);
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
                    if (!ValidOwner(seat.Owner)) Unbind(seat);
                }
            var remove = new List<IntPtr>();
            foreach (var pair in Candidates)
                if (!ValidOwner(pair.Value) || (Time.time - pair.Value.Seen > 10f && !HasSeat(pair.Value))) remove.Add(pair.Key);
            foreach (var key in remove) Candidates.Remove(key);
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
        if (Candidates.TryGetValue(character.Pointer, out var existing) && existing.Root == character.gameObject.Pointer
            && existing.GoId == character.gameObject.GetInstanceID() && ValidOwner(existing))
        { if (seen) existing.Seen = Time.time; return existing; }
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
                    Unbind(seat); Log("death:" + seat.Receipt.Id.ToString("N"), null); break;
                }
        }
        catch (Exception e) { Log("death", e); }
    }

    private static void Unbind(Seat seat)
    {
        IntPtr root = seat.Owner != null ? seat.Owner.Root : IntPtr.Zero;
        try { if (seat.Damage != null && seat.Death != null) seat.Damage.OnDeath -= seat.Death; }
        catch (Exception e) { Log("unsubscribe", e); }
        seat.Owner = null; seat.Damage = null; seat.Death = null;
        if (root != IntPtr.Zero && !Islands.Values.Any(x => x.Seats.Any(s => s.Owner != null && s.Owner.Root == root))) BoundRoots.Remove(root);
    }

    // Resolves the persistent context/epoch of one native island snapshot. Read-only here:
    // context registration and baselines are committed only by the load/virgin/generation writers.
    private static IslandState NewState(string contextKey, long world, IslandSaveData island, string rawJson, bool generationPending)
    {
        var disk = HeroRecruitmentArchiveStore.Load(ArchivePath);
        var state = new IslandState { ContextKey = contextKey, World = world, Ready = true, ReadOnly = !disk.Writable };
        if (disk.Archive == null) return state;
        var resolution = HeroRecruitmentContexts.Resolve(disk.Archive, contextKey, island, rawJson, generationPending);
        state.Epoch = resolution.Epoch;
        state.MatchKind = resolution.Kind;
        state.MatchHash = resolution.MatchHash;
        state.MatchHashKind = resolution.MatchHashKind;
        state.MatchLegacy = resolution.MatchLegacy;
        state.NewEpoch = resolution.NewEpoch;
        state.Unresolved = resolution.Unresolved;
        state.HasBaseline = resolution.Epoch != null && disk.Archive.Baselines.ContainsKey(resolution.Epoch);
        foreach (var receipt in resolution.Seats) state.Seats.Add(new() { Receipt = receipt });
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
            bool valid = world != 0 && TryContextKey(global.currentCampaign, global.currentChallenge, island.land, out contextKey);
            if (valid) { _contextKey = contextKey; _contextWorld = world; _contextFrame = Time.frameCount; }
            return valid;
        }
        catch { return false; }
    }

    // Stable context identity for one island lineage. The runtime creation ticks of the loaded
    // island are deliberately excluded: the game recreates them per load and only the stable
    // file/campaign/challenge/land identity survives, which is what the v2 epochs are keyed on.
    private static bool TryContextKey(int campaign, int challenge, int land, out string contextKey)
    {
        contextKey = null;
        try
        {
            if (land < 0) return false;
            string file = GlobalSaveData.filename;
            if (string.IsNullOrEmpty(file) || file.Length > 256) return false;
            contextKey = HeroRecruitmentArchive.ContextKey(file, campaign, challenge, land);
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
        private readonly Dictionary<string, Seat> Owners = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, Candidate> Lives = new();
        internal bool Conflict;
        internal void Capture(Persistent persistent, string id)
        {
            if (persistent == null || string.IsNullOrEmpty(id) || id.Length > 256) return;
            if (Island == null)
            {
                var island = IslandSaveData.CurrentlySavingIsland;
                if (island == null || !IslandSaveData.isSavingGame || (Land != -1 && island.land != Land)) return;
                if (!HeroRecruitment.TryContextKey(Campaign, Challenge, island.land, out string contextKey)) return;
                Island = island; ContextKey = contextKey;
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

        internal void Apply()
        {
            if (Conflict || Island == null || !HeroArcherNetwork.AllowsLocalHero || !Islands.TryGetValue(ContextKey, out var state) || !state.Ready) return;
            // An unresolved origin must not generate fresh anonymous snapshots that could evict
            // its last provable source, and a context without a proven epoch must not be written at all.
            if (state.Unresolved || state.Epoch == null || state.Seats.Any(x => x.Owner == null)) { Log("save-preserve-unresolved", null); return; }
            var rows = new List<HeroPurchaseReceipt>();
            foreach (var seat in state.Seats)
            {
                var copy = seat.Receipt.Copy(true);
                foreach (var pair in Owners)
                {
                    if (!ReferenceEquals(pair.Value, seat)) continue;
                    if (!Lives.TryGetValue(seat.Receipt.Id, out var life) || !ReferenceEquals(life, seat.Owner) || !ValidOwner(life)) return;
                    int count = 0;
                    foreach (var record in Island.objects)
                        if (record != null && record.uniqueID == pair.Key && IsCharacterRecord(record)) count++;
                    if (count != 1) return;
                    copy.NativeId = pair.Key;
                }
                rows.Add(copy);
            }
            string json = JsonUtility.ToJson(Island, false);
            if (string.IsNullOrEmpty(json)) return;
            string hash;
            try { hash = HeroRecruitmentFingerprint.Hash(json, state.Epoch); }
            catch (Exception e) { state.ReadOnly = true; Log("save-json", e); return; }
            if (!Commit(state, disk => disk.Archive.Record(state.Epoch, hash, rows, HeroRecruitmentFingerprint.Kind, false)))
            { state.ReadOnly = true; Log("save-readonly", null); return; }
            state.ReadOnly = false;
            Log("saved:" + state.Epoch.Substring(0, 8) + ":" + hash.Substring(0, 8), null);
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
        internal bool Conflict;
        internal void Begin(IslandSaveData island)
        {
            _contextFrame = -1; _lastTickFrame = -1;
            if (!HeroArcherNetwork.AllowsLocalHero || island == null || GlobalSaveData.loaded == null) return;
            if (!TryContextKey(GlobalSaveData.loaded.currentCampaign, GlobalSaveData.loaded.currentChallenge, island.land, out string contextKey)) return;
            string json = JsonUtility.ToJson(island, false);
            if (string.IsNullOrEmpty(json)) return;
            // A virgin island is recorded by the generation bridge after ApplyToScene succeeds;
            // this load must not pre-create its epoch or baseline.
            GenerationPending = IsVirgin(island);
            State = NewState(contextKey, WorldKey(), island, json, GenerationPending);
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
            Islands.TryGetValue(contextKey, out Old); OldCurrent = _current;
            if (Old == null && Islands.Count >= HeroRecruitmentArchive.MaxContexts) { State = null; return; }
            // Never assign purchases from an earlier unsaved session to this newly loaded snapshot.
            Islands[contextKey] = State; _current = State;
            foreach (var record in island.objects)
            {
                if (record == null || !IsCharacterRecord(record)) continue;
                string id = record.uniqueID;
                if (string.IsNullOrEmpty(id) || id.Length > 256 || Frozen.ContainsKey(id)) { Conflict = true; return; }
                Frozen.Add(id, record.Pointer);
            }
        }

        internal void Capture(IslandSaveData.ObjectData data, Persistent root)
        {
            if (State == null || data == null || root == null || Conflict) return;
            var seat = State.Seats.Find(x => x.Receipt.NativeId.Length > 0 && x.Receipt.NativeId == data.uniqueID);
            if (seat == null) return;
            if (!Frozen.TryGetValue(data.uniqueID, out IntPtr pointer) || pointer != data.Pointer) { Conflict = true; return; }
            var actor = root.GetComponent<Character>();
            if (actor == null) return; // Native decay/role differences reserve the seat.
            var candidate = GetCandidate(actor, false);
            if (candidate == null) return;
            if (Seen.Contains(data.uniqueID))
            {
                if (!ReferenceEquals(seat.Owner, candidate)) Conflict = true;
                return;
            }
            if (State.Seats.Any(x => !ReferenceEquals(x, seat) && ReferenceEquals(x.Owner, candidate))) { Conflict = true; return; }
            Seen.Add(data.uniqueID);
            Bind(seat, candidate, true);
        }

        internal void End(bool success)
        {
            _contextFrame = -1; _lastTickFrame = -1;
            if (State == null) return;
            if (!success)
            {
                foreach (var seat in State.Seats) Unbind(seat);
                if (Old != null) Islands[State.ContextKey] = Old; else Islands.Remove(State.ContextKey);
                _current = OldCurrent;
                return;
            }
            if (Old != null) foreach (var seat in Old.Seats) Unbind(seat);
            if (Conflict) foreach (var seat in State.Seats) Unbind(seat);
            State.Ready = true;
            State.World = WorldKey();
            foreach (var seat in State.Seats) if (seat.Owner != null) seat.Owner.World = State.World;
            if (!Conflict && Confirmable)
            {
                var receipts = State.Seats.Select(x => x.Receipt.Copy()).ToArray();
                if (Commit(State, disk => disk.Archive.ConfirmBaseline(State.Epoch, Hash, receipts, BaselineKind, BaselineLegacy))) State.HasBaseline = true;
                else { State.ReadOnly = true; State.HasBaseline = false; Log("baseline-unconfirmed", null); }
            }
            Log("loaded:" + State.MatchKind + ":ctx=" + State.ContextKey.Substring(0, 8) + ":epoch=" + Short(State.Epoch)
                + ":hk=" + BaselineKind + ":v1=" + BaselineLegacy
                + ":seats=" + State.Seats.Count + ":bound=" + State.Seats.Count(x => x.Owner != null), null);
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
            if (!TryContextKey(GlobalSaveData.loaded.currentCampaign, GlobalSaveData.loaded.currentChallenge, island.land, out string contextKey)) return;
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
            if (!TryContextKey(GlobalSaveData.loaded.currentCampaign, GlobalSaveData.loaded.currentChallenge, island.land, out string contextKey) || contextKey != ContextKey) return;
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
            var state = NewState(contextKey, world, island, json, false);
            state.VirginIsland = island.Pointer; state.VirginWorld = world;
            Islands[contextKey] = state; _current = state;
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
            if (Bind(Seat, next)) Log("role-transfer:" + Seat.Receipt.Id.ToString("N"), null);
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
            __state = new() { Previous = _save, Campaign = __0, Land = __1, Challenge = __2 }; _save = __state;
        }
        [HarmonyPostfix, HarmonyPriority(Priority.Last)] private static void After(SaveCapture __state)
        { try { __state?.Apply(); } catch (Exception e) { if (_current != null) _current.ReadOnly = true; Log("save", e); } }
        [HarmonyFinalizer] private static Exception Finally(Exception __exception, SaveCapture __state)
        { if (ReferenceEquals(_save, __state)) _save = __state?.Previous; return __exception; }
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
    // future operation and must keep ownership until the immediate execution call.
    [HarmonyPatch(typeof(Pool), nameof(Pool.FastDespawn), new[] { typeof(GameObject), typeof(float), typeof(bool) })]
    internal static class DespawnPatch
    {
        internal struct Capture { internal GameObject Root; internal IntPtr Pointer; }
        [HarmonyPrefix] private static void Before(GameObject __0, float __1, out Capture __state)
        {
            __state = default;
            try
            {
                if (__1 <= 0f && __0 != null && BoundRoots.Contains(__0.Pointer))
                    __state = new() { Root = __0, Pointer = __0.Pointer };
            }
            catch (Exception e) { Log("pool-before", e); }
        }
        [HarmonyPostfix] private static void After(Capture __state)
        {
            try
            {
                if (__state.Pointer != IntPtr.Zero && (__state.Root == null || !__state.Root.activeInHierarchy)) UnbindRoot(__state.Pointer);
            }
            catch (Exception e) { Log("pool-after", e); }
        }
    }
}
