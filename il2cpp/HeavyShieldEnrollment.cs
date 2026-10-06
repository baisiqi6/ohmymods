using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

// First-connect baseline enrollment (issue 167). A campaign whose authoritative
// record proves no purchase may register the frozen snapshots of its already
// visited, still-missing islands once, at a verified native lifecycle boundary.
//
// This type owns only the bounded native _islands read and the frozen row list.
// Durability stays with HeavyShieldPersistence' existing Stage/Prepare chain:
// nothing here writes the staged document, the native key, or any Prepared value.
//
// Frozen rows carry the native member identity (Pointer + Slot) so the commit
// gate can re-read the bounded member table and prove every frozen member is
// still the same object at the same slot before any write. Snapshot hashes are
// re-computed for non-current members only: the pop consumes the current
// island's objects, so its hash cannot be recomputed at commit time.
internal static class HeavyShieldEnrollment
{
    // NoEnrollmentNeeded: nothing to register (no already-visited missing member).
    // Unavailable: members exist or cannot be proven absent while the freeze could
    // not complete; the caller must keep the first purchase closed.
    internal enum Outcome : byte { NoEnrollmentNeeded, Frozen, Unavailable }

    internal sealed class Row
    {
        internal int Land;
        internal int Slot;
        internal IntPtr Pointer;
        internal string SnapshotHash;
    }

    internal sealed class Candidate
    {
        internal readonly List<Row> Rows = new();
        internal bool IncludesCurrent;
    }

    // The current native island must appear exactly once in campaign._islands and
    // its slot must equal its own land. Placeholder slots keep land 0 and never
    // describe a real island, so only an exact slot==land match counts. The bound
    // is checked before any enumeration.
    internal static bool TryCurrentSlot(CampaignSaveData campaign, IslandSaveData current, out int slot)
    {
        slot = -1;
        if (campaign == null || current == null || current.land < 0
            || current.Pointer == IntPtr.Zero) return false;
        try
        {
            var islands = campaign._islands;
            if (islands == null) return false;
            int count = islands.Count;
            if (count <= 0 || count > HeavyShieldSaveSchema.MaxIslands) return false;
            int index = 0, seen = 0;
            foreach (var entry in islands)
            {
                if (entry != null && entry.Pointer != IntPtr.Zero
                    && entry.Pointer == current.Pointer) { seen++; slot = index; }
                index++;
            }
            return seen == 1 && slot == current.land;
        }
        catch { slot = -1; return false; }
    }

    // Freeze every already-visited, slot-exact, generation-complete island
    // snapshot that has no record in any authoritative copy. The read is bounded
    // by the schema island cap; any read difficulty reports Unavailable so the
    // caller keeps the first purchase closed instead of falling through.
    internal static Outcome TryFreezeRows(CampaignSaveData campaign, IslandSaveData current,
        string campaignGuid, int challenge, HeavyShieldSavedCampaign[] copies,
        out Candidate candidate, out string reason)
    {
        candidate = null;
        reason = null;
        if (campaign == null || current == null || string.IsNullOrEmpty(campaignGuid) || challenge < 0)
        { reason = "context"; return Outcome.Unavailable; }
        try
        {
            var islands = campaign._islands;
            if (islands == null) { reason = "table"; return Outcome.Unavailable; }
            int count = islands.Count;
            if (count <= 0) { reason = "empty"; return Outcome.NoEnrollmentNeeded; }
            if (count > HeavyShieldSaveSchema.MaxIslands) { reason = "capacity"; return Outcome.Unavailable; }
            if (!TryCurrentSlot(campaign, current, out _)) { reason = "current"; return Outcome.Unavailable; }
            var result = new Candidate();
            int index = 0;
            foreach (var entry in islands)
            {
                int slot = index++;
                if (entry == null || entry.Pointer == IntPtr.Zero) continue;
                // Placeholder/divergent slot: never a real island; never pre-occupied.
                if (slot != entry.land || entry.land < 0) continue;
                // Generation not complete: the existing generation boundary owns it.
                if (entry.isNew) continue;
                double played = entry.playTimeDays;
                if (double.IsNaN(played) || double.IsInfinity(played) || played <= 0.0) continue;
                // Known rows (any copy, any hash) stay untouched.
                if (HasKnownRow(copies, challenge, entry.land)) continue;
                if (entry.objects == null) { reason = "objects"; return Outcome.Unavailable; }
                string raw = JsonUtility.ToJson(entry, false);
                string hash = HeavyShieldSaveCodec.SnapshotHash(campaignGuid, challenge, entry.land, raw);
                if (hash == null) { reason = "hash"; return Outcome.Unavailable; }
                result.Rows.Add(new Row
                {
                    Land = entry.land, Slot = slot, Pointer = entry.Pointer, SnapshotHash = hash,
                });
                if (entry.Pointer == current.Pointer) result.IncludesCurrent = true;
            }
            if (result.Rows.Count == 0) { reason = "none"; return Outcome.NoEnrollmentNeeded; }
            candidate = result;
            return Outcome.Frozen;
        }
        catch { candidate = null; reason = "read"; return Outcome.Unavailable; }
    }

    // Commit gate: re-read the bounded member table once and prove every frozen
    // row is still the same native member at the same slot. Non-current members
    // must still carry the visited generation state and the frozen snapshot; the
    // current member's hash is deliberately not recomputed because the pop that
    // just ran consumes its objects table. Any mismatch aborts the whole event.
    internal static bool TryVerifyRows(CampaignSaveData campaign, IslandSaveData current,
        string campaignGuid, int challenge, List<Row> rows, out string reason)
    {
        reason = null;
        if (campaign == null || rows == null || rows.Count == 0) return false;
        for (int i = 0; i < rows.Count; i++)
            for (int j = i + 1; j < rows.Count; j++)
                if (rows[i].Pointer == rows[j].Pointer) { reason = "duplicate"; return false; }
        try
        {
            var islands = campaign._islands;
            if (islands == null) { reason = "table"; return false; }
            int count = islands.Count;
            if (count <= 0 || count > HeavyShieldSaveSchema.MaxIslands) { reason = "capacity"; return false; }
            var verified = new bool[rows.Count];
            int index = 0;
            foreach (var entry in islands)
            {
                int slot = index++;
                if (entry == null || entry.Pointer == IntPtr.Zero) continue;
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (entry.Pointer != row.Pointer) continue;
                    if (verified[i]) { reason = "duplicate"; return false; }
                    if (slot != row.Slot || entry.land != row.Land) { reason = "moved"; return false; }
                    bool isCurrent = current != null && entry.Pointer == current.Pointer;
                    if (!isCurrent)
                    {
                        if (entry.isNew) { reason = "new"; return false; }
                        double played = entry.playTimeDays;
                        if (double.IsNaN(played) || double.IsInfinity(played) || played <= 0.0)
                        { reason = "unvisited"; return false; }
                        if (entry.objects == null) { reason = "objects"; return false; }
                        string hash = HeavyShieldSaveCodec.SnapshotHash(campaignGuid, challenge,
                            entry.land, JsonUtility.ToJson(entry, false));
                        if (hash == null || hash != row.SnapshotHash) { reason = "swapped"; return false; }
                    }
                    verified[i] = true;
                }
            }
            foreach (bool ok in verified)
                if (!ok) { reason = "missing"; return false; }
            return true;
        }
        catch { reason = "read"; return false; }
    }

    private static bool HasKnownRow(HeavyShieldSavedCampaign[] copies, int challenge, int land)
    {
        if (copies == null) return false;
        for (int i = 0; i < copies.Length; i++)
        {
            var campaign = copies[i];
            if (campaign?.Islands == null) continue;
            foreach (var row in campaign.Islands)
                if (row != null && row.Challenge == challenge && row.Land == land) return true;
        }
        return false;
    }
}
