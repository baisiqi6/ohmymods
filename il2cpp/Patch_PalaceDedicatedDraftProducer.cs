// Offline/unwired producer for one exact caller-owned fresh Palace attempt.
// The caller owns private12/config/holder/seed/layout identity, the sole ref publication,
// exit of the former MapWidth writer, and failure throw + inactiveOwner cleanup.
// No layout/campaign/group/RNG/neutral-state mutation or stock fallback occurs here.
// Beach endpoints: the two canonical sources (BeachLeft/BeachRight_Blocks_Greece) are resolved
// by exact name, each exactly once, inside the same bounded layout.blocks candidate walk; any
// missing/duplicate/null/malformed candidate is an explicit failure — never a silent beachless
// success — and they are forwarded to PalaceDedicatedOwnedBlock only after every existing gate.
// The two exclusive beach clone handles flow back through out params (TryPrepare -> TryBuild ->
// PalaceDedicatedEndpoints) on success, failure, and exception alike, for Root-side single-writer
// cleanup; this producer never destroys or clears them.
using System;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class PalaceDedicatedDraftProducer
{
    private const int MaxEntries = 128;
    private const int MaxSourceCandidates = 256;
    private const string BeachLeftSourceName = "BeachLeft_Blocks_Greece";
    private const string BeachRightSourceName = "BeachRight_Blocks_Greece";

    internal static bool TryPrepare(GameObject inactiveOwner, LevelLayout layout,
        LevelBlock campSource, LevelBlock spawnSource, PalaceLayoutRecipe recipe,
        out Il2CppSystem.Collections.Generic.List<LevelBlock> finalDraft,
        out LevelBlock owned, out GameObject leftOwned, out GameObject rightOwned, out string reason)
    {
        finalDraft = null;
        owned = null;
        leftOwned = null;
        rightOwned = null;
        reason = "";
        try
        {
            if (!PalaceLayoutRecipe.IsKnown(recipe))
                return Reject("layout-recipe-unknown", out reason);
            if (inactiveOwner == null || layout == null || PointerOf(layout) == IntPtr.Zero)
                return Reject("owner-or-layout-null", out reason);
            IntPtr campPtr = PointerOf(campSource);
            IntPtr spawnPtr = PointerOf(spawnSource);
            if (campPtr == IntPtr.Zero || spawnPtr == IntPtr.Zero || campPtr == spawnPtr)
                return Reject("source-null-or-alias", out reason);

            // Actual provisional placements and their shared provenance are input facts,
            // not values to erase or replace to manufacture an empty ledger.
            var placements = layout.layout;
            if (placements == null) return Reject("placements-null", out reason);
            int placementCount = placements.Count;
            if (placementCount < 1 || placementCount > MaxEntries)
                return Reject("placement-count:" + placementCount, out reason);
            for (int i = 0; i < placementCount; i++)
            {
                var entry = placements[i];
                if (entry == null || PointerOf(entry.block) == IntPtr.Zero)
                    return Reject("placement-null-block:" + i, out reason);
                if (entry.fromSharedPool)
                    return Reject("placement-shared:" + i, out reason);
            }
            if (placements.Count != placementCount)
                return Reject("placement-count-drift", out reason);

            var candidates = layout.blocks;
            if (candidates == null) return Reject("source-candidates-null", out reason);
            int candidateCount = candidates.Count;
            if (candidateCount < 1 || candidateCount > MaxSourceCandidates)
                return Reject("source-candidate-count:" + candidateCount, out reason);
            int campMatches = 0, spawnMatches = 0;
            int beachLeftMatches = 0, beachRightMatches = 0;
            LevelBlock beachLeftSource = null, beachRightSource = null;
            for (int i = 0; i < candidateCount; i++)
            {
                LevelBlock candidate = candidates[i];
                IntPtr ptr = PointerOf(candidate);
                if (ptr == IntPtr.Zero) return Reject("source-candidate-null:" + i, out reason);
                if (ptr == campPtr) campMatches++;
                if (ptr == spawnPtr) spawnMatches++;
                // Same bounded walk, no second scan: exact-name resolution of the two
                // canonical beach endpoint sources among the validated candidates.
                string candidateName;
                try
                {
                    GameObject candidateGo = candidate.gameObject;
                    candidateName = candidateGo == null ? null : candidateGo.name;
                }
                catch (Exception e)
                {
                    return Reject("beach-candidate-name-unreadable:" + i + ":" + e.GetType().Name,
                        out reason);
                }
                if (candidateName == null)
                    return Reject("beach-candidate-gameobject-null:" + i, out reason);
                if (string.Equals(candidateName, BeachLeftSourceName, StringComparison.Ordinal))
                {
                    beachLeftMatches++;
                    beachLeftSource = candidate;
                }
                else if (string.Equals(candidateName, BeachRightSourceName, StringComparison.Ordinal))
                {
                    beachRightMatches++;
                    beachRightSource = candidate;
                }
            }
            if (candidates.Count != candidateCount)
                return Reject("source-candidate-count-drift", out reason);
            if (campMatches != 1 || spawnMatches != 1)
                return Reject("source-membership:camp=" + campMatches + ",spawn=" + spawnMatches,
                    out reason);
            // The dedicated recipe requires both canonical beach sources: missing or duplicated
            // names fail explicitly instead of producing a beachless island and reporting success.
            if (beachLeftMatches != 1 || beachRightMatches != 1)
                return Reject("beach-source-membership:left=" + beachLeftMatches
                    + ",right=" + beachRightMatches, out reason);
            IntPtr beachLeftPtr = PointerOf(beachLeftSource);
            IntPtr beachRightPtr = PointerOf(beachRightSource);
            if (beachLeftPtr == IntPtr.Zero || beachRightPtr == IntPtr.Zero)
                return Reject("beach-source-null", out reason);
            if (beachLeftPtr == beachRightPtr || beachLeftPtr == campPtr || beachLeftPtr == spawnPtr
                || beachRightPtr == campPtr || beachRightPtr == spawnPtr)
                return Reject("beach-source-alias", out reason);

            // Exactly one call. Current body review supports a newly produced dictionary
            // and source-list reads; unresolved generic callees and Native execution remain
            // acceptance boundaries, not silently assumed by this source-only helper.
            var usedSharedBlocks = layout.GetUsedSharedBlocks();
            if (usedSharedBlocks == null)
                return Reject("used-shared-blocks-null", out reason);
            int usedCount = usedSharedBlocks.Count;
            if (usedCount != 0)
                return Reject("used-shared-blocks-count:" + usedCount, out reason);

            // All input eligibility precedes this sole clone-construction call.
            // On failure, partial owned is kept for the caller's independent owner cleanup.
            if (!PalaceDedicatedOwnedBlock.TryBuild(inactiveOwner, campSource, spawnSource,
                    beachLeftSource, beachRightSource, recipe,
                    out owned, out leftOwned, out rightOwned, out string buildReason))
                return Reject("owned-build:" + buildReason, out reason);
            IntPtr ownedPtr = PointerOf(owned);
            if (ownedPtr == IntPtr.Zero || ownedPtr == campPtr || ownedPtr == spawnPtr)
                return Reject("owned-null-or-alias", out reason);
            if (!owned._dimensionsComputed || owned._width != recipe.Width
                || owned._left != recipe.Left || owned._right != recipe.Right)
                return Reject("owned-cached-geometry", out reason);

            // Stage and verify locally; only the caller subsequently publishes this list.
            var staged = new Il2CppSystem.Collections.Generic.List<LevelBlock>();
            staged.Add(owned);
            if (staged.Count != 1 || PointerOf(staged[0]) != ownedPtr)
                return Reject("final-draft-stage-readback", out reason);
            finalDraft = staged;
            reason = "prepared-unpublished";
            return true;
        }
        catch (Exception e)
        {
            // No retry, ledger repair, cleanup guess, or successful stock return.
            finalDraft = null;
            reason = "exception:" + e.GetType().Name + ":" + e.Message;
            return false;
        }
    }

    private static IntPtr PointerOf(Il2CppObjectBase value)
        => value == null ? IntPtr.Zero : value.Pointer;

    private static bool Reject(string failure, out string reason)
    {
        reason = failure;
        return false;
    }
}
