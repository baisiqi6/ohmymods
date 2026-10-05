using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomEnhancedMod;

// v2 island context resolution. The archive keys receipts by opaque epoch scopes; this class
// decides, for one stable context (save file + campaign/challenge + land), which epoch the
// currently loaded native island JSON belongs to, and which seats may be restored from it.
//
// Rules (all fail-closed, never a guessed owner, never a fabricated charge):
//  * A virgin generation outranks every paid match: the generation bridge owns that epoch.
//  * Matches are searched over this context's own epochs plus every scope no context owns yet.
//    A scope already owned by another context is never a migration source, and a scope can never
//    be migrated into a second context.
//  * Every snapshot carries its hash function (kind 1 = legacy raw JSON, kind 2 = clock-independent
//    fingerprint) and its v1 provenance, so a legacy snapshot is matched with the legacy hash and a
//    v2 snapshot with the fingerprint, and a legacy baseline can never be laundered into authority.
//  * A nonempty exact match beats an empty one. Two v2 claims for one native snapshot, or two
//    disagreeing paid histories, quarantine. A v2-authoritative empty state beats legacy paid
//    history (it must not resurrect a seat the v2 lineage already released).
//  * Legacy paid history is only claimed for this island when every receipt owner is exactly one
//    Character record of the loaded island; otherwise it quarantines unproven.
//  * While any unclaimed legacy paid history remains, no exact match means "unresolved": quota is
//    reserved and nothing is written, instead of claiming the island has no heroes.
internal static class HeroRecruitmentContexts
{
    internal sealed class Resolution
    {
        internal string Epoch;                     // resolved epoch scope; null = nothing proven yet
        internal string Kind = "none";             // bounded diagnostic label
        internal string MatchHash;                 // exact matched snapshot: its stored hash/kind/provenance drive the baseline
        internal int MatchHashKind;                // 0 = no exact match (a fresh kind-2 snapshot is written instead)
        internal bool MatchLegacy;
        internal bool Fresh;                       // no exact match: Epoch receives a new kind-2 snapshot
        internal List<HeroPurchaseReceipt> Seats = new();
        internal bool Unresolved;                  // fail closed: no charge, no baseline, no snapshot
        internal bool NewEpoch;                    // epoch must be appended to the context epoch list
    }

    private sealed class Match
    {
        internal string Scope;
        internal HeroRecruitmentSnapshot Snapshot;
        internal int Kind;
    }

    internal static Resolution Resolve(HeroRecruitmentArchive archive, string contextKey, IslandSaveData island, string rawJson, bool generationPending)
    {
        var result = new Resolution();
        if (archive == null || !HeroRecruitmentArchive.HashValid(contextKey)) return result;
        if (generationPending) { result.Kind = "generation-pending"; return result; }
        bool known = archive.TryGetContext(contextKey, out var context);
        bool unclaimedPaid = HasUnclaimedPaid(archive);
        if (rawJson == null)
        {
            // Runtime state without a native snapshot (no load this session): reserve what the
            // active epoch already claims, but never enable a charge from this path.
            if (!known) { result.Kind = "await-context"; return result; }
            result.Epoch = context.Active; result.Kind = "await-baseline";
            result.Seats = archive.LatestReservations(context.Active);
            result.Unresolved = result.Seats.Count > 0 || unclaimedPaid;
            return result;
        }
        var matches = Collect(archive, contextKey, known ? context : null, rawJson);
        var paid = matches.Where(x => x.Snapshot.Seats.Count > 0).ToList();
        var authoritative = matches.Where(x => x.Snapshot.Seats.Count == 0 && !x.Snapshot.LegacyV1).ToList();
        var legacyEmpty = matches.Where(x => x.Snapshot.Seats.Count == 0 && x.Snapshot.LegacyV1).ToList();
        if (paid.Count > 0 && !Agree(paid)) return Quarantine(result, archive, matches, "conflict");
        if (paid.Count > 0 && authoritative.Count > 0)
        {
            // The v2 lineage already released this native snapshot; legacy paid history must not
            // resurrect it. Two v2 claims for one snapshot cannot be adjudicated, so they quarantine.
            if (paid.Any(x => !x.Snapshot.LegacyV1)) return Quarantine(result, archive, matches, "conflict");
            return Restore(result, archive, context, authoritative, "authoritative-empty");
        }
        if (paid.Count > 0)
        {
            // A v2 paid match is authoritative by provenance; a purely legacy one may only be
            // claimed when every receipt owner is exactly one Character record of this island.
            var authoritativePaid = paid.Where(x => !x.Snapshot.LegacyV1).ToList();
            if (authoritativePaid.Count > 0) return Restore(result, archive, context, authoritativePaid, "paid");
            if (!NativeEvidence(paid[0].Snapshot.Seats, island))
                return Quarantine(result, archive, paid, "legacy-unproven");
            return Restore(result, archive, context, paid, "legacy-paid");
        }
        if (authoritative.Count > 0) return Restore(result, archive, context, authoritative, "authoritative-empty");
        if (legacyEmpty.Count > 0)
        {
            // A legacy empty baseline can be the v1 bug's fabricated zero. It never enables charges
            // while unclaimed legacy paid history could still describe this island.
            if (unclaimedPaid) return Quarantine(result, archive, matches, "legacy-pending");
            return Restore(result, archive, context, legacyEmpty, "legacy-empty");
        }
        if (unclaimedPaid)
        {
            // No proof for this native snapshot while unclaimed paid history remains: reserve and
            // write nothing. A fabricated empty baseline could mask a repurchased hero.
            // issue-150 v3: cross-island seats must not display as reserved here; an
            // indeterminate classification keeps the original full reservation.
            var clsQ = known ? ClassifyContext(archive, context, island) : null;
            List<HeroPurchaseReceipt> reserved;
            if (clsQ == null || (!clsQ.Indeterminate && clsQ.OwnerEpoch == null && clsQ.MixedEpoch == null))
                reserved = new List<HeroPurchaseReceipt>();            // nothing provable lives here
            else if (clsQ.Indeterminate)
                reserved = archive.LatestReservations(context.Active); // cannot judge: keep #85 shape
            else
                reserved = clsQ.OwnerEpoch != null ? clsQ.OwnerCarry : clsQ.MixedCarry;
            return Quarantine(result, archive, null, "unresolved", reserved);
        }
        if (known)
        {
            // Known context, never seen this native snapshot, nothing unclaimed: the context's
            // own claims are all that can be reserved, and an empty state may confirm normally.
            // issue-150 v3: classify every epoch (not only Active) against THIS island.
            var cls = ClassifyContext(archive, context, island);
            if (cls.Indeterminate)
            {
                result.Kind = "unknown"; result.Epoch = context.Active;
                result.Seats = archive.LatestReservations(context.Active);
                result.Unresolved = result.Seats.Count > 0;
                result.Fresh = result.Seats.Count == 0;
                return result;
            }
            if (cls.OwnerEpoch != null)
            {
                // An epoch whose whole walk is provably this island's: unchanged #85 protection,
                // anchored at that epoch (not at whichever sibling island moved Active).
                result.Kind = "unknown"; result.Epoch = cls.OwnerEpoch;
                result.Seats = cls.OwnerCarry;
                result.Unresolved = result.Seats.Count > 0;
                result.Fresh = result.Seats.Count == 0;
                return result;
            }
            if (cls.MixedEpoch != null)
            {
                // Partial evidence: keep every provable seat reserved and block writes.
                // Writing here would be ambiguous between this island and the epoch's other owner.
                result.Kind = "unknown-mixed"; result.Epoch = cls.MixedEpoch;
                result.Seats = cls.MixedCarry;
                result.Unresolved = true;
                return result;
            }
            if (cls.AnyWalkSeat)
            {
                // Every seat of every epoch is foreign to this island: a different island of
                // this shared context. Adopt a fresh epoch so this island's reads/writes never
                // touch (or truncate, or evict) the other islands' snapshots and baselines; the
                // old epochs stay claimed for their owners (exact-match restore still reaches
                // them, and their drift loads re-anchor via the owner branch above).
                if (context.Epochs.Count < HeroRecruitmentArchive.MaxEpochs)
                {
                    result.Kind = "foreign-epoch"; result.Epoch = HeroRecruitmentArchive.NewScope();
                    result.NewEpoch = true; result.Fresh = true;
                    return result;
                }
                // Epoch cap (review v2 P1-B): minting is refused by capacity, and a refused
                // Stage would veto the whole native save. Degrade to a write-locked unknown
                // instead — purchase blocked, nothing written, no global save refusal.
                result.Kind = "epoch-cap"; result.Epoch = context.Active;
                result.Unresolved = true;
                return result;
            }
            // No epoch of this context has any walk seat: the pre-fix behavior.
            result.Kind = "unknown"; result.Epoch = context.Active;
            result.Seats = archive.LatestReservations(context.Active);
            result.Unresolved = result.Seats.Count > 0;
            result.Fresh = result.Seats.Count == 0;
            return result;
        }
        result.Kind = "fresh"; result.Epoch = HeroRecruitmentArchive.NewScope();
        result.NewEpoch = true; result.Fresh = true;
        return result;
    }

    // Matches over this context's epochs first, then every scope no context owns yet.
    private static List<Match> Collect(HeroRecruitmentArchive archive, string contextKey, HeroRecruitmentContext context, string rawJson)
    {
        var result = new List<Match>();
        var scopes = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (context != null)
            foreach (string epoch in context.Epochs)
                if (seen.Add(epoch)) scopes.Add(epoch);
        foreach (string scope in archive.Scopes.Keys.OrderBy(x => x, StringComparer.Ordinal))
            if (!archive.ScopeClaimedBy(scope, contextKey) && seen.Add(scope)) scopes.Add(scope);
        foreach (string scope in scopes)
            for (int kind = HeroRecruitmentArchive.HashKindLegacy; kind <= HeroRecruitmentFingerprint.Kind; kind++)
            {
                if (!archive.ScopeHasKind(scope, kind)) continue;
                string hash;
                try { hash = HashOf(kind, rawJson, scope); }
                catch (Exception) { continue; } // unusable native JSON cannot match any stored snapshot
                if (archive.TryGet(scope, hash, out var snapshot)) result.Add(new Match { Scope = scope, Snapshot = snapshot, Kind = kind });
            }
        return result;
    }

    private static string HashOf(int kind, string rawJson, string scope)
        => kind == HeroRecruitmentFingerprint.Kind ? HeroRecruitmentFingerprint.Hash(rawJson, scope) : HeroRecruitmentArchive.Hash(rawJson, scope);

    private static Resolution Restore(Resolution result, HeroRecruitmentArchive archive, HeroRecruitmentContext context, List<Match> matches, string kind)
    {
        string epoch = Pick(archive, matches);
        var match = matches.First(x => x.Scope == epoch);
        result.Kind = kind; result.Epoch = epoch;
        result.MatchHash = match.Snapshot.Hash; result.MatchHashKind = match.Kind; result.MatchLegacy = match.Snapshot.LegacyV1;
        result.Seats = match.Snapshot.Seats.Select(x => x.Copy()).ToList();
        result.NewEpoch = context == null || !context.Epochs.Contains(epoch);
        return result;
    }

    private static Resolution Quarantine(Resolution result, HeroRecruitmentArchive archive, List<Match> matches, string kind, List<HeroPurchaseReceipt> seats = null)
    {
        result.Kind = kind; result.Unresolved = true;
        result.Epoch = null; result.MatchHash = null; result.MatchHashKind = 0; result.MatchLegacy = false;
        result.Seats = seats ?? Reserved(archive, matches != null ? matches.Select(x => x.Scope) : Enumerable.Empty<string>());
        return result;
    }

    private static bool Agree(List<Match> matches) => matches.All(x => HeroRecruitmentArchive.Same(matches[0].Snapshot.Seats, x.Snapshot.Seats));

    // Unclaimed = owned by no context at all. Owned-by-this-context epochs are handled by the
    // active epoch's own reservations instead of the blanket unresolved rule.
    private static bool HasUnclaimedPaid(HeroRecruitmentArchive archive)
    {
        foreach (var scope in archive.Scopes)
        {
            if (archive.ScopeClaimed(scope.Key)) continue;
            if (scope.Value.Any(snapshot => snapshot.Seats.Count > 0)) return true;
        }
        return false;
    }

    // Deterministic: authoritative (non-legacy) first, then a scope whose confirmed baseline pins
    // this snapshot, then ordinal order. Never "first/newest wins" on a conflicting history.
    private static string Pick(HeroRecruitmentArchive archive, List<Match> matches)
    {
        var ordered = matches.OrderBy(x => x.Snapshot.LegacyV1 ? 1 : 0).ThenBy(x => x.Scope, StringComparer.Ordinal).ToList();
        foreach (var match in ordered)
            if (archive.Baselines.TryGetValue(match.Scope, out string baseline) && baseline == match.Snapshot.Hash) return match.Scope;
        return ordered[0].Scope;
    }

    private static List<HeroPurchaseReceipt> Reserved(HeroRecruitmentArchive archive, IEnumerable<string> scopes)
    {
        var result = new List<HeroPurchaseReceipt>();
        foreach (string scope in scopes)
            foreach (var receipt in archive.LatestReservations(scope))
                if (!result.Any(x => x.Side == receipt.Side)) result.Add(receipt);
        return result;
    }

    // issue-150 v2: one pass over the loaded island's Character rows. Null (not empty) means
    // the island is unavailable or the interop walk failed: callers must treat that as
    // indeterminate and keep the #85 conservative behavior, never as "no evidence".
    private static Dictionary<string, int> CharacterRowCount(IslandSaveData island)
    {
        if (island == null) return null;
        try
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var record in island.objects)
            {
                if (record == null) continue;
                if (!HeroRecruitment.IsCharacterRecord(record)) continue;
                string id = record.uniqueID;
                if (string.IsNullOrEmpty(id) || id.Length > 256) continue;
                counts.TryGetValue(id, out int n);
                counts[id] = n + 1;
            }
            return counts;
        }
        catch { return null; }
    }

    // Every receipt owner must be one unique Character record of the loaded island: an empty or
    // missing native id cannot classify a legacy history as belonging to this island.
    private static bool NativeEvidence(IReadOnlyList<HeroPurchaseReceipt> seats, IslandSaveData island)
    {
        if (island == null || seats.Count == 0) return false;
        var counts = CharacterRowCount(island);
        if (counts == null) return false;
        foreach (var seat in seats)
        {
            if (seat.NativeId.Length == 0) return false;
            if (!counts.TryGetValue(seat.NativeId, out int n) || n != 1) return false;
        }
        return true;
    }

    // issue-150 v3: classify EVERY epoch of the context against the loaded island (the active
    // pointer alternates between sibling islands of one shared context, so an Active-only walk
    // reopens the #85 double-buy window on the non-active island — review v2 P0-A). Evidence =
    // exactly one live Character row here, judged on snapshot-original receipts (ids intact).
    // Empty-id seats cannot be classified and stay conservative (carried within their epoch).
    // Any failure is indeterminate: the caller keeps the pre-fix behavior instead of guessing.
    private sealed class ContextClass
    {
        internal string OwnerEpoch;                          // an epoch whose walk is fully evidenced here
        internal List<HeroPurchaseReceipt> OwnerCarry;
        internal string MixedEpoch;                           // an epoch with partial evidence here
        internal List<HeroPurchaseReceipt> MixedCarry;
        internal bool AnyWalkSeat;                            // some epoch has seats, none evidenced here
        internal bool Indeterminate;
    }

    private static ContextClass ClassifyContext(HeroRecruitmentArchive archive,
        HeroRecruitmentContext context, IslandSaveData island)
    {
        var cls = new ContextClass();
        if (context == null) return cls;
        if (island == null) { cls.Indeterminate = true; return cls; }
        var counts = CharacterRowCount(island);
        if (counts == null) { cls.Indeterminate = true; return cls; }
        foreach (string epoch in context.Epochs)
        {
            List<HeroPurchaseReceipt> walk;
            try { walk = archive.LatestWalkOriginals(epoch); }
            catch { cls.Indeterminate = true; return cls; }
            if (walk.Count == 0) continue;
            bool foreign = false, evidenced = false;
            var carry = new List<HeroPurchaseReceipt>();
            foreach (var seat in walk)
            {
                cls.AnyWalkSeat = true;
                if (seat.NativeId.Length == 0) { carry.Add(seat.Copy(true)); continue; }
                if (counts.TryGetValue(seat.NativeId, out int n) && n == 1)
                { carry.Add(seat.Copy(true)); evidenced = true; }
                else
                    foreign = true;
            }
            if (!foreign)
            {
                // This epoch's whole walk lives here: the island's own paid epoch.
                if (cls.OwnerEpoch == null) { cls.OwnerEpoch = epoch; cls.OwnerCarry = carry; }
            }
            else if (evidenced && cls.MixedEpoch == null)
            {
                cls.MixedEpoch = epoch; cls.MixedCarry = carry;
            }
        }
        return cls;
    }

    // issue-85: a completely unknown context whose loaded snapshot matches no stored snapshot,
    // while every unclaimed paid row names a native id that is not one of this island's complete
    // Character set, is a disjoint history: no old receipt can be correlated with any currently
    // loaded character. This judges nothing about the old rows themselves — no owner is declared
    // dead, no entitlement is abandoned, and a future exact native match still restores them; it
    // only lets the current island adopt a fresh empty epoch instead of staying unresolved
    // forever. Pure archive+snapshot check; the caller re-runs it on the fresh disk inside the
    // commit CAS and still requires the live shape/carry checks. A raw JSON parse failure is
    // never zero matches, and no native id is required to be globally unique across scopes (one
    // id may legitimately be referenced by several historical receipts of the same lineage).
    internal static bool DisjointHistory(HeroRecruitmentArchive archive, string contextKey, string rawJson, ISet<string> characterIds)
    {
        if (archive == null || rawJson == null || characterIds == null || !HeroRecruitmentArchive.HashValid(contextKey)) return false;
        if (archive.TryGetContext(contextKey, out _)) return false;
        try { HeroRecruitmentFingerprint.Hash(rawJson, contextKey); }
        catch { return false; }
        if (Collect(archive, contextKey, null, rawJson).Count > 0) return false;
        bool unclaimedPaid = false;
        foreach (var scope in archive.Scopes)
        {
            if (archive.ScopeClaimed(scope.Key)) continue;
            foreach (var snapshot in scope.Value)
            {
                if (snapshot.Seats.Count == 0) continue;
                unclaimedPaid = true;
                foreach (var seat in snapshot.Seats)
                {
                    string native = seat.NativeId;
                    if (string.IsNullOrEmpty(native) || native.Length > 256) return false;
                    if (characterIds.Contains(native)) return false;
                }
            }
        }
        return unclaimedPaid;
    }
}
