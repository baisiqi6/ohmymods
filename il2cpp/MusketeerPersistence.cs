using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

// Island sidecar bridge for paid musketeer careers. Four native boundaries are reused (all long
// methods already targeted elsewhere in this mod): IslandSaveData.Save/GetID,
// IslandSaveData.TryPopObjectsToScene/TryCreateOrFind and CampaignSaveData.ApplyToScene for a
// confirmed new generation. No mod component data ever enters the native compressed file; the
// sidecar is a separate compare-and-swap file with a durable backup.
//
// A record's persisted NativeId is only ever written from a capture witnessed during that same
// native save (GetID on the exact root that career is bound to). Unproven holders are persisted
// as reservations without an id: a stale id can never bind a paid career to a recycled pool
// instance (native ids are name + Unity instance id, which pool reuse recycles).
internal static class MusketeerPersistence
{
    private static SaveCapture _save;
    private static LoadCapture _load;

    internal static string ArchivePath => Path.Combine(Paths.ConfigPath, "KingdomEnhancedMod", "ModSave", "musketeer-identities.v2.json");

    internal static MusketeerArchiveStore.ReadResult ReadArchive() => MusketeerArchiveStore.Load(ArchivePath);

    internal sealed class Resolution
    {
        internal string Epoch;
        internal string Kind = "none";
        internal string MatchHash;
        internal bool Unresolved;
        internal bool NewEpoch;
        internal bool HasBaseline;
        internal bool GenerationPending;
        internal List<MusketeerCareer> Records = new();
    }

    // Fail-closed context/epoch resolution. Exact snapshot matches come only from this context's
    // own epochs; an unknown native snapshot with paid history is preserved as reservations and
    // blocks new charges instead of guessing an owner or writing a fabricated baseline.
    internal static Resolution Resolve(MusketeerArchive archive, string contextKey, string rawJson, bool generationPending)
    {
        var result = new Resolution();
        if (archive == null || !MusketeerArchive.HashValid(contextKey)) return result;
        if (generationPending) { result.Kind = "generation-pending"; result.GenerationPending = true; return result; }
        bool known = archive.TryGetContext(contextKey, out var context);
        if (rawJson == null)
        {
            if (!known) { result.Kind = "await-context"; return result; }
            result.Epoch = context.Active; result.Kind = "await-baseline";
            result.Records = archive.LatestReservations(context.Active);
            result.HasBaseline = archive.Baselines.ContainsKey(context.Active);
            result.Unresolved = result.Records.Count > 0;
            return result;
        }
        var matches = Collect(archive, context, rawJson);
        if (matches.Count > 0)
        {
            if (!Agree(matches)) return Quarantine(archive, matches, "conflict");
            var match = Pick(archive, matches);
            result.Kind = "exact"; result.Epoch = match.Scope;
            result.MatchHash = match.Snapshot.Hash;
            result.Records = match.Snapshot.Records.Select(x => x.Copy()).ToList();
            result.HasBaseline = archive.Baselines.TryGetValue(match.Scope, out string baseline) && baseline == match.Snapshot.Hash;
            result.NewEpoch = context == null || !context.Epochs.Contains(match.Scope);
            return result;
        }
        if (known)
        {
            var reserved = archive.LatestReservations(context.Active);
            if (reserved.Count > 0)
            {
                // Native advanced without a witnessed snapshot: preserve the paid history and
                // block charges; never assign it to a live object by position or by stale id.
                result.Kind = "unknown-paid"; result.Unresolved = true;
                result.Epoch = null; result.Records = reserved;
                return result;
            }
            // Nothing to protect: the active epoch may confirm this (empty) native snapshot.
            result.Kind = "unknown-empty"; result.Epoch = context.Active;
            result.HasBaseline = archive.Baselines.ContainsKey(context.Active);
            return result;
        }
        result.Kind = "fresh"; result.Epoch = MusketeerArchive.NewScope();
        result.NewEpoch = true;
        return result;
    }

    private sealed class Match
    {
        internal string Scope;
        internal MusketeerSnapshot Snapshot;
    }

    private static List<Match> Collect(MusketeerArchive archive, MusketeerContext context, string rawJson)
    {
        var result = new List<Match>();
        if (context == null) return result;
        foreach (string epoch in context.Epochs)
        {
            string hash;
            try { hash = MusketeerArchive.IslandHash(rawJson, epoch); }
            catch (Exception) { continue; }   // unusable native JSON cannot match any stored snapshot
            if (archive.TryGet(epoch, hash, out var snapshot)) result.Add(new Match { Scope = epoch, Snapshot = snapshot });
        }
        return result;
    }

    private static bool Agree(List<Match> matches) => matches.All(x => MusketeerArchive.Same(matches[0].Snapshot.Records, x.Snapshot.Records));

    private static Resolution Quarantine(MusketeerArchive archive, List<Match> matches, string kind)
    {
        var result = new Resolution { Kind = kind, Unresolved = true };
        foreach (var match in matches)
            foreach (var record in archive.LatestReservations(match.Scope))
                if (!result.Records.Exists(x => x.Id == record.Id)) result.Records.Add(record);
        return result;
    }

    // Baseline-pinned scope first (a rollback keeps pointing at the native snapshot it confirmed),
    // then ordinal order; never "newest wins" on a conflicting history.
    private static Match Pick(MusketeerArchive archive, List<Match> matches)
    {
        var ordered = matches.OrderBy(x => x.Scope, StringComparer.Ordinal).ToList();
        foreach (var match in ordered)
            if (archive.Baselines.TryGetValue(match.Scope, out string baseline) && baseline == match.Snapshot.Hash) return match;
        return ordered[0];
    }

    internal static MusketeerIdentity.IslandState CreateState(string contextKey, long world, string rawJson, bool generationPending)
    {
        var disk = ReadArchive();
        var state = new MusketeerIdentity.IslandState { ContextKey = contextKey, World = world, Ready = true, ReadOnly = !disk.Writable };
        if (disk.Archive == null) return state;
        var resolution = Resolve(disk.Archive, contextKey, rawJson, generationPending);
        state.Epoch = resolution.Epoch;
        state.MatchKind = resolution.Kind;
        state.MatchHash = resolution.MatchHash;
        state.NewEpoch = resolution.NewEpoch;
        state.Unresolved = resolution.Unresolved;
        state.HasBaseline = resolution.HasBaseline;
        state.GenerationPending = resolution.GenerationPending;
        foreach (var record in resolution.Records) MusketeerIdentity.AddCareer(state, record);
        return state;
    }

    // Single writer: mutate a freshly loaded disk state, ride the context/epoch registration in
    // the same compare-and-swap, and clear the new-epoch marker only after the file landed.
    internal static bool Commit(MusketeerIdentity.IslandState state, Func<MusketeerArchiveStore.ReadResult, bool> mutate)
    {
        if (state == null || state.ContextKey == null || state.Epoch == null) return false;
        var disk = ReadArchive();
        if (!disk.Writable || disk.Archive == null) return false;
        if (!mutate(disk)) return false;
        if (!disk.Archive.EnsureContext(state.ContextKey, state.Epoch, state.NewEpoch)) return false;
        if (!MusketeerArchiveStore.Save(ArchivePath, disk, disk.Archive)) return false;
        state.NewEpoch = false;
        return true;
    }

    private static bool IsVirgin(IslandSaveData island)
    {
        try { return island != null && island.isNew && island.playTimeDays == 0.0; }
        catch { return false; }
    }

    private static bool IsActive(MusketeerIdentity.Career career)
    {
        try
        {
            var go = career.Kind == MusketeerCareer.KindUnit
                ? (career.Character != null ? career.Character.gameObject : null)
                : (career.Tool != null ? career.Tool.gameObject : null);
            return go != null && go.activeInHierarchy;
        }
        catch { return false; }
    }

    private static string Short(string value) => string.IsNullOrEmpty(value) || value.Length < 8 ? "" : value.Substring(0, 8);

    // The native save freezes the island object rows; GetID is called once per persisted root.
    // We capture which career each exact root belongs to, then refuse the whole sidecar write if
    // any expected marked row is missing, duplicated or ambiguous.
    internal sealed class SaveCapture
    {
        internal SaveCapture Previous;
        internal int Campaign, Land, Challenge;
        internal IslandSaveData Island;
        internal string ContextKey;
        private readonly Dictionary<string, MusketeerIdentity.Career> Owners = new(StringComparer.Ordinal);
        private readonly Dictionary<MusketeerIdentity.Career, string> Ids = new();
        internal bool Conflict;

        internal void Capture(Persistent persistent, string id)
        {
            if (persistent == null || string.IsNullOrEmpty(id) || id.Length > MusketeerArchive.MaxNativeIdLength) return;
            if (Island == null)
            {
                var island = IslandSaveData.CurrentlySavingIsland;
                if (island == null || !IslandSaveData.isSavingGame || (Land != -1 && island.land != Land)) return;
                if (!MusketeerIdentity.TryContextKey(Campaign, Challenge, island.land, out string contextKey)) return;
                Island = island; ContextKey = contextKey;
            }
            if (!MusketeerIdentity.Islands.TryGetValue(ContextKey, out var state) || state == null || !state.Ready) return;
            var career = MusketeerIdentity.BoundAt(state, persistent.gameObject);
            if (career == null) return;
            if (Owners.TryGetValue(id, out var other) && !ReferenceEquals(other, career)) { Conflict = true; return; }
            if (Ids.TryGetValue(career, out string seen) && seen != id) { Conflict = true; return; }
            Owners[id] = career; Ids[career] = id;
        }

        internal void Apply()
        {
            if (Conflict || Island == null || !MusketeerAccess.TrackAllowed) return;
            if (!MusketeerIdentity.Islands.TryGetValue(ContextKey, out var state) || state == null || !state.Ready) return;
            // An unknown native snapshot may never generate a fresh snapshot: the previous provable
            // origin is preserved untouched. A resolved epoch may still be written while unresolved
            // (that is how a load anomaly is normalized into honest reservations and self-heals).
            if (state.Epoch == null || state.GenerationPending) { MusketeerIdentity.Log("save-preserve-unresolved", null); return; }
            try
            {
                var rows = new List<MusketeerCareer>(state.Careers.Count);
                var liveIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var career in state.Careers)
                {
                    if (!MusketeerIdentity.ValidBinding(career)) { rows.Add(Reserved(career)); continue; }
                    if (!Ids.TryGetValue(career, out string id))
                    {
                        // A temporarily hidden object is not persisted by the native save and is
                        // therefore not an expected row; an active one that missed the capture is.
                        if (IsActive(career)) { MusketeerIdentity.Log("save-missed-row", null); return; }
                        rows.Add(Reserved(career)); continue;
                    }
                    if (!liveIds.Add(id)) { MusketeerIdentity.Log("save-duplicate-id", null); return; }
                    // The authoritative save re-proves the rack claim: a collected or enemy-carried
                    // gun loses the claim before it can be written; the claim travels only with the
                    // witnessed row of a still-proven live gun.
                    if (career.StockSlot != MusketeerCareer.NoStockSlot && !MusketeerIdentity.StockClaimProven(career))
                        MusketeerIdentity.RevokeStock(career, "stock-unproven");
                    rows.Add(new MusketeerCareer { Id = career.Id, Kind = career.Kind, NativeId = id, StockSlot = career.StockSlot });
                }
                if (rows.Count > MusketeerArchive.MaxRecords || !MusketeerArchive.ValidRecords(rows)) { MusketeerIdentity.Log("save-invalid-rows", null); return; }
                foreach (string id in liveIds)
                {
                    int count = 0;
                    if (Island.objects != null)
                        foreach (var record in Island.objects)
                            if (record != null && record.uniqueID == id) count++;
                    if (count != 1) { MusketeerIdentity.Log("save-row-missing", null); return; }
                }
                string json = JsonUtility.ToJson(Island, false);
                if (string.IsNullOrEmpty(json)) { MusketeerIdentity.Log("save-json", null); return; }
                string hash = MusketeerArchive.IslandHash(json, state.Epoch);
                if (!Commit(state, disk => disk.Archive.Record(state.Epoch, hash, rows))) { state.ReadOnly = true; MusketeerIdentity.Log("save-readonly", null); return; }
                state.ReadOnly = false;
                MusketeerIdentity.Log("saved:records=" + rows.Count + ":bound=" + liveIds.Count, null);
            }
            catch (Exception e) { MusketeerIdentity.Log("save", e); }
        }

        private static MusketeerCareer Reserved(MusketeerIdentity.Career career)
            => new() { Id = career.Id, Kind = career.Kind, NativeId = "", StockSlot = MusketeerCareer.NoStockSlot };
    }

    // Load window: freeze the native rows of the island being popped, then bind each matched
    // snapshot record to the exact object that its witnessed native id created. Restoration
    // happens here, before the game is Playing, and never by ordinal position.
    internal sealed class LoadCapture
    {
        internal LoadCapture Previous;
        private MusketeerIdentity.IslandState State;
        private MusketeerIdentity.IslandState Old;
        private MusketeerIdentity.IslandState OldCurrent;
        private string Hash;
        private bool Confirmable;
        private bool GenerationPending;
        private readonly Dictionary<string, IntPtr> Frozen = new(StringComparer.Ordinal);
        private readonly List<MusketeerIdentity.Career> StockGuns = new();
        internal bool Conflict;
        private int Bound;
        private int Unbound;

        internal void Begin(IslandSaveData island)
        {
            MusketeerIdentity.InvalidateContextCache();
            if (!MusketeerAccess.TrackAllowed || island == null || GlobalSaveData.loaded == null) return;
            if (!MusketeerIdentity.TryContextKey(GlobalSaveData.loaded.currentCampaign, GlobalSaveData.loaded.currentChallenge, island.land, out string contextKey)) return;
            string json = JsonUtility.ToJson(island, false);
            if (string.IsNullOrEmpty(json)) return;
            // A virgin island is recorded by the generation bridge after ApplyToScene succeeds;
            // this load must not pre-create its epoch or baseline.
            GenerationPending = IsVirgin(island);
            var state = CreateState(contextKey, MusketeerIdentity.WorldKey(), json, GenerationPending);
            if (state == null) return;
            // Keep the exact matched snapshot's stored hash instead of recomputing; a state
            // without an exact match writes its own kind-2 baseline.
            Hash = state.MatchHash;
            if (Hash == null && state.Epoch != null && !state.Unresolved && !GenerationPending)
            {
                try { Hash = MusketeerArchive.IslandHash(json, state.Epoch); }
                catch (Exception e) { Hash = null; MusketeerIdentity.Log("load-json", e); }
            }
            Confirmable = Hash != null && state.Epoch != null && !state.Unresolved && !GenerationPending;
            state.Ready = false;
            MusketeerIdentity.Islands.TryGetValue(contextKey, out Old);
            OldCurrent = MusketeerIdentity.Current;
            if (Old == null && MusketeerIdentity.Islands.Count >= MusketeerArchive.MaxContexts) { State = null; return; }
            MusketeerIdentity.InstallState(contextKey, state);
            State = state;
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var career in state.Careers)
                if (career.NativeId.Length > 0) wanted.Add(career.NativeId);
            if (island.objects == null) return;
            foreach (var record in island.objects)
            {
                if (record == null) continue;
                string id = record.uniqueID;
                if (string.IsNullOrEmpty(id) || !wanted.Contains(id)) continue;
                if (Frozen.ContainsKey(id)) { Conflict = true; continue; }   // duplicate row id for one record
                Frozen.Add(id, record.Pointer);
            }
        }

        internal void Capture(IslandSaveData.ObjectData data, Persistent root)
        {
            if (State == null || data == null || root == null || Conflict) return;
            string id = data.uniqueID;
            if (string.IsNullOrEmpty(id)) return;
            var career = MusketeerIdentity.FindByNativeId(State, id);
            if (career == null) return;   // not a tracked row: ignore
            if (!Frozen.TryGetValue(id, out var pointer) || pointer != data.Pointer) { Conflict = true; return; }
            if (!MusketeerIdentity.CaptureLoadRow(State, id, root, out string failure))
            {
                // A witnessed row that cannot be identified as its recorded kind is a real
                // mismatch: never bind, and never confirm a baseline from it.
                if (failure != null) Conflict = true;
                Unbound++;
                return;
            }
            Bound++;
            if (career.Kind == MusketeerCareer.KindGun && career.StockSlot != MusketeerCareer.NoStockSlot) StockGuns.Add(career);
        }

        internal void End(bool success)
        {
            MusketeerIdentity.InvalidateContextCache();
            if (State == null) return;
            if (!success)
            {
                // A failed load restores the previous state and its bindings unmodified and never
                // writes an archive association.
                MusketeerIdentity.ReleaseAll(State);
                if (Old != null) MusketeerIdentity.InstallState(State.ContextKey, Old);
                else MusketeerIdentity.RemoveState(State.ContextKey);
                MusketeerIdentity.SetCurrent(OldCurrent);
                return;
            }
            if (Old != null) MusketeerIdentity.ReleaseAll(Old);
            if (Conflict) MusketeerIdentity.ReleaseAll(State);
            State.Ready = true;
            State.World = MusketeerIdentity.WorldKey();
            // A witnessed row of the loaded island that never produced its exact object (or any
            // conflict/kind mismatch/duplicate) leaves the picture partial: keep every paid career,
            // never inherit a baseline from it, and block new charges until a clean load.
            int pending = 0;
            foreach (var career in State.Careers)
                if (career.Slot == null && career.NativeId.Length > 0 && Frozen.ContainsKey(career.NativeId)) pending++;
            bool resolved = !Conflict && pending == 0;
            if (!resolved)
            {
                State.Unresolved = true;
                State.HasBaseline = false;
            }
            else if (Confirmable)
            {
                var rows = new List<MusketeerCareer>(State.Careers.Count);
                foreach (var career in State.Careers)
                    rows.Add(new MusketeerCareer { Id = career.Id, Kind = career.Kind, NativeId = career.NativeId, StockSlot = career.StockSlot });
                if (Commit(State, disk => disk.Archive.ConfirmBaseline(State.Epoch, Hash, rows))) State.HasBaseline = true;
                else { State.ReadOnly = true; State.HasBaseline = false; MusketeerIdentity.Log("baseline-unconfirmed", null); }
            }
            // Rack guns: only an exactly proven stock gun of a fully resolved load is restored, and
            // only its physics flags. A restore that cannot be applied right now stays owned as a
            // pending retry that blocks new charges (never silently dropped, never a lock once the
            // same proven stock freezes successfully at the next Tick).
            if (resolved && MusketeerAccess.TrackAllowed)
                foreach (var career in StockGuns)
                    if (!MusketeerIdentity.TryRestoreStockPhysics(career)
                        && career.StockSlot != MusketeerCareer.NoStockSlot && career.Slot != null)
                        MusketeerIdentity.MarkStockRestorePending(State, career);
            MusketeerIdentity.Log("loaded:" + State.MatchKind + ":records=" + State.Careers.Count
                + ":bound=" + Bound + ":unbound=" + Unbound + ":pending=" + pending + ":stock=" + StockGuns.Count
                + ":conflict=" + Conflict, null);
        }
    }

    // Brand-new islands do not necessarily run TryPopObjectsToScene. Freeze the native isNew
    // fact before ApplyToScene, then create a fresh epoch + empty baseline only after the same
    // island generated in the same runtime world.
    internal sealed class VirginCapture
    {
        private IntPtr CampaignPointer, IslandPointer;
        private string ContextKey;
        private long SourceWorld;
        private bool Done;

        internal void Begin(CampaignSaveData campaign)
        {
            if (Done || campaign == null || !MusketeerAccess.TrackAllowed || GlobalSaveData.loaded == null) return;
            var island = campaign.CurrentIsland;
            if (!IsVirgin(island)) return;
            if (!MusketeerIdentity.TryContextKey(GlobalSaveData.loaded.currentCampaign, GlobalSaveData.loaded.currentChallenge, island.land, out string contextKey)) return;
            long world = MusketeerIdentity.WorldKey();
            if (world == 0) return;
            // Once-guard: this virgin generation already owns an epoch in this world.
            if (MusketeerIdentity.Islands.TryGetValue(contextKey, out var runtime) && runtime != null
                && runtime.VirginIsland == island.Pointer && runtime.VirginWorld == world) return;
            CampaignPointer = campaign.Pointer; IslandPointer = island.Pointer;
            ContextKey = contextKey; SourceWorld = world;
        }

        internal void Complete(CampaignSaveData campaign)
        {
            if (ContextKey == null || Done || campaign == null || campaign.Pointer != CampaignPointer
                || CampaignSaveData.current == null || CampaignSaveData.current.Pointer != CampaignPointer
                || !MusketeerAccess.TrackAllowed || GlobalSaveData.loaded == null) return;
            var island = campaign.CurrentIsland;
            // Failed generation, or a different island/campaign/world: alter no context and no data.
            if (island == null || island.Pointer != IslandPointer || !IsVirgin(island)) return;
            if (!MusketeerIdentity.TryContextKey(GlobalSaveData.loaded.currentCampaign, GlobalSaveData.loaded.currentChallenge, island.land, out string contextKey) || contextKey != ContextKey) return;
            long world = MusketeerIdentity.WorldKey();
            if (world == 0 || world != SourceWorld) return;
            if (!MusketeerIdentity.Islands.ContainsKey(contextKey) && MusketeerIdentity.Islands.Count >= MusketeerArchive.MaxContexts) return;
            string json = JsonUtility.ToJson(island, false);
            if (string.IsNullOrEmpty(json)) return;
            // New generation: a fresh opaque epoch and an empty baseline, committed atomically
            // with the context mapping. Old epochs stay untouched for rollback; nothing is inherited.
            string scope = MusketeerArchive.NewScope();
            string hash;
            try { hash = MusketeerArchive.IslandHash(json, scope); }
            catch (Exception e) { MusketeerIdentity.Log("virgin-json", e); return; }
            var pending = new MusketeerIdentity.IslandState { ContextKey = contextKey, Epoch = scope, MatchKind = "new-generation", NewEpoch = true, World = world, Ready = true };
            if (!Commit(pending, disk => disk.Archive.ConfirmBaseline(scope, hash, Array.Empty<MusketeerCareer>())))
            { MusketeerIdentity.Log("virgin-baseline-unconfirmed", null); return; }
            Done = true;
            var state = CreateState(contextKey, world, json, false);
            state.VirginIsland = island.Pointer; state.VirginWorld = world;
            MusketeerIdentity.InstallState(contextKey, state);
            MusketeerIdentity.InvalidateContextCache();
            MusketeerIdentity.Log("virgin-epoch:" + Short(contextKey) + ":" + Short(scope), null);
        }
    }

    // Existing long native save/load endpoints only; no native compressed-file changes.
    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.Save), new[] { typeof(int), typeof(int), typeof(int) })]
    internal static class SavePatch
    {
        [HarmonyPrefix]
        private static void Before(int __0, int __1, int __2, out SaveCapture __state)
        { __state = new() { Previous = _save, Campaign = __0, Land = __1, Challenge = __2 }; _save = __state; }

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void After(SaveCapture __state) { try { __state?.Apply(); } catch (Exception e) { MusketeerIdentity.Log("save-after", e); } }

        [HarmonyFinalizer]
        private static Exception Finally(Exception __exception, SaveCapture __state)
        { if (ReferenceEquals(_save, __state)) _save = __state?.Previous; return __exception; }
    }

    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.GetID), new[] { typeof(Persistent) })]
    internal static class GetIdPatch
    {
        [HarmonyPostfix]
        private static void After(Persistent __0, string __result)
        {
            try { _save?.Capture(__0, __result); }
            catch (Exception e) { if (_save != null) _save.Conflict = true; MusketeerIdentity.Log("get-id", e); }
        }
    }

    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.TryPopObjectsToScene))]
    internal static class PopPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Before(IslandSaveData __instance, out LoadCapture __state)
        {
            __state = new() { Previous = _load }; _load = __state;
            try { __state.Begin(__instance); }
            catch (Exception e) { __state.Conflict = true; MusketeerIdentity.Log("load", e); }
        }

        [HarmonyFinalizer]
        private static Exception Finally(Exception __exception, bool __result, LoadCapture __state)
        {
            try { __state?.End(__exception == null && __result); }
            catch (Exception e) { MusketeerIdentity.Log("load-end", e); }
            finally { if (ReferenceEquals(_load, __state)) _load = __state?.Previous; }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.TryCreateOrFind))]
    internal static class CreatePatch
    {
        [HarmonyPostfix]
        private static void After(IslandSaveData.ObjectData __0, Persistent __result)
        {
            try { _load?.Capture(__0, __result); }
            catch (Exception e) { if (_load != null) _load.Conflict = true; MusketeerIdentity.Log("load-bind", e); }
        }
    }

    // CampaignSaveData.ApplyToScene mirrors the hero module's virgin generation bridge.
    [HarmonyPatch(typeof(CampaignSaveData), nameof(CampaignSaveData.ApplyToScene))]
    internal static class VirginPatch
    {
        [HarmonyPrefix]
        private static void Before(CampaignSaveData __instance, out VirginCapture __state)
        {
            __state = new();
            try { __state.Begin(__instance); }
            catch (Exception e) { MusketeerIdentity.Log("virgin-begin", e); }
        }

        [HarmonyPostfix]
        private static void After(CampaignSaveData __instance, VirginCapture __state)
        {
            try { __state?.Complete(__instance); }
            catch (Exception e) { MusketeerIdentity.Log("virgin-end", e); }
        }
    }
}
