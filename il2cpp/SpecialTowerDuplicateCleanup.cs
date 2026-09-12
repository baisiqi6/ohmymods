using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Same-site duplicate ordinary-tower cleanup (user-authorized).
///
/// Removes a built ordinary Tower that shares one construction site with an
/// independent, completed, typed special tower (TowerKnight/Ballista/FireTower/
/// Baker/OilFireArcherTower, or a SpecialTowerRebuildMarker-bearing root is
/// protected, never a witness-by-name). All special roots are always preserved;
/// ordinary towers merely visually overlapping are NOT eligible. Candidates and
/// witnesses both come from the operator-supplied occupancy list (WorldLoad
/// TowerSpots snapshot) — this helper never scans the full scene, mounts no
/// scene driver; the operator invokes <see cref="RemoveDuplicates"/>. A scoped
/// distribution prefix prevents synchronous restaffing during native release.
///
/// Teardown order per design review: full identity/payment/construction
/// precheck, GuardSlot/Archer pairing snapshot, native Archer.ExitGuardSlot per
/// validated occupant while the root is still active and registered, occupant
/// release verification, full revalidation, then exact-header DeregisterObject,
/// DontPersistInstance(true), SetActive(false), Destroy. Never Tower.DestroyTower
/// (it re-instantiates a fresh base). Any unknown state or partial native
/// failure retains the tower and stops the pass.
/// </summary>
internal static class SpecialTowerDuplicateCleanup
{
    private const float SiteToleranceX = 0.1f;
    private const float SiteToleranceY = 0.5f;

    private static void Log(string message) =>
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[TowerDupCleanup] " + message);
    private static void LogError(string message) =>
        KingdomEnhancedPlugin.Instance?.LogSource.LogError("[TowerDupCleanup] " + message);
    private static bool Same(UnityEngine.Object a, UnityEngine.Object b) =>
        a != null && b != null && a.Pointer == b.Pointer;

    /// <summary>Removes confirmed duplicate ordinary towers; returns the roots actually deactivated/destroyed.</summary>
    internal static List<GameObject> RemoveDuplicates(World world, Transform layer, IList<GameObject> occupied)
    {
        var removed = new List<GameObject>();
        try
        {
            if (occupied == null || occupied.Count == 0) return removed;
            if (!ContextValid(world, layer)) return removed;
            for (int i = 0; i < occupied.Count; i++)
            {
                GameObject candidate = occupied[i];
                if (!ContextValid(world, layer)) break;
                GameObject witness;
                try
                {
                    CRPCHeader plannedHeader;
                    if (!TryPlan(world, layer, occupied, candidate, out witness, out plannedHeader)) continue;
                    int done = TryRemoveScoped(world, layer, occupied, candidate, witness, plannedHeader);
                    if (done == 1 || done == -2) removed.Add(candidate);
                    if (done < 0) break; // partial native failure: stop the pass, retain the rest
                }
                catch (Exception e)
                {
                    LogError("candidate failed, retaining x=" + SafeX(candidate) + ": " + e);
                    break; // do not treat a swallowed exception as success or keep demolishing
                }
            }
            Log("removed=" + removed.Count);
        }
        catch (Exception e) { LogError("pass failed: " + e); }
        return removed;
    }

    private static string SafeX(GameObject go)
    {
        try { return go != null && go.transform != null ? go.transform.position.x.ToString("F2") : "<null>"; }
        catch { return "<error>"; }
    }

    // ---- context / identity gates (mirrors PatchWorld_TowerSpots, fail-closed) ----

    private static bool ContextValid(World world, Transform layer)
    {
        try
        {
            if (!ModConfig.Enabled.Value || NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth)
                return false;
            Managers managers = Managers.Inst;
            if (world == null || world.gameObject == null || !world.gameObject.activeInHierarchy
                || layer == null || managers == null || managers.game == null
                || managers.world == null || managers.kingdom == null
                || managers.game.state != Game.State.Playing
                || managers.world.Pointer != world.Pointer)
                return false;
            Transform current = managers.world.gameLayer;
            return current != null && current.Pointer == layer.Pointer
                && current.gameObject != null && current.gameObject.activeInHierarchy
                && NetworkPostbox.Instance != null;
        }
        catch { return false; }
    }

    private static bool IsOnBoat(Transform t)
    {
        try
        {
            Transform walker = t.parent;
            for (int depth = 0; depth < 4 && walker != null; depth++)
            {
                string n = walker.name;
                if (n != null && n.Contains("Boat")) return true;
                walker = walker.parent;
            }
        }
        catch { return true; } // undecidable parent chain = boat risk = retain
        return false;
    }

    private static bool IsInLayerScene(GameObject go, Transform layer)
    {
        try
        {
            return go != null && go.transform != null && layer != null && layer.gameObject != null
                && go.transform.IsChildOf(layer)
                && go.scene.handle == layer.gameObject.scene.handle;
        }
        catch { return false; }
    }

    private static bool IsSameHierarchy(GameObject a, GameObject b)
    {
        if (a == null || b == null) return false;
        if (a == b) return true;
        try
        {
            return a.transform.IsChildOf(b.transform) || b.transform.IsChildOf(a.transform);
        }
        catch { return true; } // undecidable = related = protected
    }

    private static bool HasSpecialComponent(GameObject root)
    {
        try
        {
            return root.GetComponentsInChildren<TowerKnight>(true).Length > 0
                || root.GetComponentsInChildren<Ballista>(true).Length > 0
                || root.GetComponentsInChildren<FireTower>(true).Length > 0
                || root.GetComponentsInChildren<Baker>(true).Length > 0
                || root.GetComponentsInChildren<OilFireArcherTower>(true).Length > 0
                || root.GetComponentsInChildren<SpecialTowerRebuildMarker>(true).Length > 0;
        }
        catch { throw; } // Unknown may protect a candidate, but can never authorize a witness.
    }

    private static bool ProtectedByAncestor(GameObject root, Transform layer)
    {
        for (Transform p = root.transform.parent; p != null && !Same(p, layer); p = p.parent)
            if (p.GetComponent<TowerKnight>() != null || p.GetComponent<Ballista>() != null
                || p.GetComponent<FireTower>() != null || p.GetComponent<Baker>() != null
                || p.GetComponent<OilFireArcherTower>() != null
                || p.GetComponent<SpecialTowerRebuildMarker>() != null) return true;
        return false;
    }

    internal static bool IsProtectedHierarchy(GameObject root, Transform layer)
    {
        try { return root == null || HasSpecialComponent(root) || ProtectedByAncestor(root, layer); }
        catch { return true; }
    }

    private static bool IsCompletedConstruction(GameObject root)
    {
        try
        {
            // Built ordinary towers normally carry WorkableBuilding + CBC: their mere
            // presence is expected. Completion must be explicit; unknown state retains.
            WorkableBuilding workable = root.GetComponent<WorkableBuilding>();
            ConstructionBuildingComponent cbc = root.GetComponent<ConstructionBuildingComponent>();
            Tower ordinary = root.GetComponent<Tower>();
            if (ordinary != null && ordinary.level == 0 && workable == null && cbc == null)
                return root.GetComponent<PayableUpgrade>() != null
                    && root.GetComponentsInChildren<Scaffolding>(true).Length == 0;
            if (workable == null || cbc == null || !Same(workable._constructionBuilding, cbc)) return false;
            if (workable.UnderConstruction) return false;
            if (cbc != null && cbc.NeedsMoreWork) return false;
            if (cbc != null && cbc._scaffolding != null) return false;
            return root.GetComponentsInChildren<Scaffolding>(true).Length == 0;
        }
        catch { return false; }
    }

    internal static bool IsPaymentClear(GameObject root)
    {
        try
        {
            foreach (Payable payable in root.GetComponentsInChildren<Payable>(true))
            if (payable != null
                && (payable.selectedByP1 || payable.selectedByP2
                    || payable.interactingPlayer != null
                    || payable.PlayerSelecting != null))
                return false;
            Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
            if (kingdom == null) return false;
            Player[] players = new Player[] { kingdom.playerOne, kingdom.playerTwo };
            for (int i = 0; i < players.Length; i++)
            {
                Player player = players[i];
                if (player == null) continue;
                Payable selected = player.selectedPayable;
                if (selected != null && IsSameHierarchy(selected.gameObject, root)) return false;
                Payable completing = player._completingPayable;
                if (completing != null && IsSameHierarchy(completing.gameObject, root)) return false;
            }
            return true;
        }
        catch { return false; } // possibly mid-payment = fail-closed
    }

    private static bool TryGetExactSemiStaticHeader(GameObject root, out CRPCHeader header)
    {
        header = null;
        try
        {
            header = NetworkPostbox.Instance.GetHeaderFromObject(root, true);
            return header != null && header.HeaderType == CRPCType.SemiStatic;
        }
        catch { return false; }
    }

    /// <summary>
    /// Full mutation-free eligibility plan for one candidate: ordinary identity,
    /// completion, payment, exact header, and a distinct live completed typed
    /// special witness at the same construction site (|dx|&lt;=0.1, |dy|&lt;=0.5).
    /// </summary>
    private static bool TryPlan(World world, Transform layer, IList<GameObject> occupied,
        GameObject candidate, out GameObject witness, out CRPCHeader header)
    {
        witness = null;
        header = null;
        if (!ContextValid(world, layer)) return false;
        if (candidate == null || candidate.transform == null || !candidate.activeInHierarchy) return false;
        if (!IsInLayerScene(candidate, layer) || IsOnBoat(candidate.transform)) return false;

        Tower tower = candidate.GetComponent<Tower>();
        Persistent persistent = candidate.GetComponent<Persistent>();
        if (tower == null || tower.level < 0 || persistent == null) return false;
        // Any special component/marker anywhere in the candidate's own hierarchy protects it.
        if (IsProtectedHierarchy(candidate, layer)) return false;
        if (!IsCompletedConstruction(candidate)) return false;
        if (HasAssociatedScaffolding(occupied, candidate)) return false;
        if (!IsPaymentClear(candidate)) return false;
        if (!TryGetExactSemiStaticHeader(candidate, out header)) return false;

        witness = FindSameSiteWitness(occupied, layer, candidate);
        return witness != null;
    }

    private static GameObject FindSameSiteWitness(IList<GameObject> occupied, Transform layer, GameObject candidate)
    {
        try
        {
            float x = candidate.transform.position.x, y = candidate.transform.position.y;
            if (!float.IsFinite(x) || !float.IsFinite(y)) return null;
            for (int i = 0; i < occupied.Count; i++)
            {
                GameObject other = occupied[i];
                if (other == null || other.transform == null || !other.activeInHierarchy) continue;
                if (Same(other, candidate)) continue;
                if (IsSameHierarchy(other, candidate)) continue; // ancestor/descendant never witnesses
                if (!IsInLayerScene(other, layer) || IsOnBoat(other.transform)) continue;
                // Real typed special identity, not a name guess.
                if (other.GetComponent<TowerKnight>() == null && other.GetComponent<Ballista>() == null
                    && other.GetComponent<FireTower>() == null && other.GetComponent<Baker>() == null
                    && other.GetComponent<OilFireArcherTower>() == null) continue;
                // Witness must itself be completed; we only read it, never mutate its data/children.
                if (!IsCompletedConstruction(other)) continue;
                if (HasAssociatedScaffolding(occupied, other)) continue;
                Vector3 p = other.transform.position;
                if (!float.IsFinite(p.x) || !float.IsFinite(p.y)) continue;
                if (Mathf.Abs(p.x - x) > SiteToleranceX) continue;
                if (Mathf.Abs(p.y - y) > SiteToleranceY) continue;
                return other;
            }
        }
        catch { return null; }
        return null;
    }

    internal static bool HasAssociatedScaffolding(IList<GameObject> occupied, GameObject root)
    {
        foreach (GameObject go in occupied)
        {
            if (go == null || !go.activeInHierarchy) continue;
            Scaffolding scaffold = go.GetComponent<Scaffolding>();
            if (scaffold != null && scaffold.Building != null
                && IsSameHierarchy(scaffold.Building, root)) return true;
        }
        return false;
    }

    /// <summary>Returns 1 removed, 0 retained-as-ineligible, -1 retained-with-stop (partial native failure).</summary>
    private static int TryRemove(World world, Transform layer, IList<GameObject> occupied,
        GameObject candidate, GameObject witness, CRPCHeader plannedHeader)
    {
        // 1) Re-run the full plan immediately before the first mutation.
        if (!TryPlan(world, layer, occupied, candidate, out GameObject currentWitness, out CRPCHeader currentHeader))
            return 0;
        if (!Same(currentWitness, witness) || currentHeader.Pointer != plannedHeader.Pointer) return 0;

        // 2) Snapshot GuardSlot/Archer pairs on the duplicate root (incl. inactive children).
        GuardSlot[] slots = candidate.GetComponentsInChildren<GuardSlot>(true);
        var archers = new List<Archer>();
        var archerGos = new List<GameObject>();
        var archerSlots = new List<GuardSlot>();
        foreach (GuardSlot slot in slots)
        {
            if (slot == null) continue;
            Archer archer = slot.archer;
            if (archer == null)
            {
                if (slot.isArcherPresent) return Retained(candidate, "slot-snapshot"); // present-but-unresolvable = unknown = stop
                continue;
            }
            // The archer must actually still reference this slot on this root; a stale
            // slot pointing at another tower's unit is not permission to release it.
            if (!Same(archer.GuardSlot, slot) || !Same(archer._guardSlot, slot)) return Retained(candidate, "slot-snapshot");
            if (Same(archer._guardSlot, slot) && !ContainsPointer(archers, archer))
            {
                archers.Add(archer);
                archerGos.Add(archer.gameObject);
                archerSlots.Add(slot);
            }
        }

        // 3) Release occupants natively while the root is still active and registered.
        for (int i = 0; i < archers.Count; i++)
        {
            if (!TryPlan(world, layer, occupied, candidate, out GameObject stepWitness, out CRPCHeader stepHeader)
                || !Same(stepWitness, witness) || stepHeader.Pointer != plannedHeader.Pointer) return Retained(candidate, "native-exit");
            Archer a = archers[i]; GuardSlot s = archerSlots[i];
            if (a == null || a.gameObject == null || !a.gameObject.activeInHierarchy
                || !IsInLayerScene(a.gameObject, layer) || !Same(a.gameObject, archerGos[i])
                || s == null || !s.transform.IsChildOf(candidate.transform)
                || !Same(s.archer, a) || !Same(a._guardSlot, s) || !Same(a.GuardSlot, s)) return Retained(candidate, "native-exit");
            CRPCHeader live = NetworkPostbox.Instance.GetHeaderFromObject(candidate, true);
            if (live == null || live.HeaderType != CRPCType.SemiStatic || live.Pointer != plannedHeader.Pointer) return Retained(candidate, "native-exit");
            try { archers[i].ExitGuardSlot(); }
            catch (Exception e) { LogError("ExitGuardSlot threw, retaining: " + e); return Retained(candidate, "native-exit"); }
        }

        // 4) Verify release. A null slot.archer alone is NOT proof of success.
        if (candidate == null || !candidate.activeInHierarchy) return Retained(candidate, "verify-exit/candidate-inactive");
        foreach (GameObject archerGo in archerGos)
        {
            if (archerGo == null || !archerGo.activeInHierarchy || !IsInLayerScene(archerGo, layer)) return Retained(candidate, "verify-exit/actor-inactive-or-layer");
            if (archerGo.transform != null && candidate.transform != null
                && archerGo.transform.IsChildOf(candidate.transform)) return Retained(candidate, "verify-exit/actor-parent");
        }
        foreach (Archer archer in archers)
        {
            if (archer == null) return Retained(candidate, "verify-exit/archer-missing");
            GuardSlot slot = archer.GuardSlot;
            if (slot != null && slot.gameObject != null && candidate != null
                && slot.transform != null && candidate.transform != null
                && slot.transform.IsChildOf(candidate.transform)) return Retained(candidate, "verify-exit/public-guard-reference");
            slot = archer._guardSlot;
            if (slot != null && slot.transform.IsChildOf(candidate.transform)) return Retained(candidate, "verify-exit/private-guard-reference");
        }
        GuardSlot[] remaining = candidate.GetComponentsInChildren<GuardSlot>(true);
        foreach (GuardSlot slot in remaining)
        {
            if (slot == null) continue;
            if (slot.isArcherPresent || slot.archer != null) return Retained(candidate, "verify-exit/slot-occupied");
        }
        if (candidate.GetComponentsInChildren<Archer>(true).Length != 0) return Retained(candidate, "verify-exit/child-archer-component");

        // 5) Re-run every gate after the native callbacks may have changed state.
        if (!TryPlan(world, layer, occupied, candidate, out GameObject reWitness, out CRPCHeader finalHeader))
            return Retained(candidate, "final-revalidate");
        if (!Same(reWitness, witness) || finalHeader.Pointer != plannedHeader.Pointer) return Retained(candidate, "final-revalidate");
        // Check again after the final native property queries before persistence mutation.
        if (candidate.GetComponentsInChildren<Archer>(true).Length != 0) return Retained(candidate, "final-revalidate");
        foreach (GuardSlot s in candidate.GetComponentsInChildren<GuardSlot>(true))
            if (s != null && (s.archer != null || s.isArcherPresent)) return Retained(candidate, "final-revalidate");

        // 6) Retire only the ordinary root: exact header deregistration, recursive
        //    dont-persist on the now actor-free hierarchy, synchronous deactivate, destroy.
        bool counted = false;
        try
        {
            NetworkPostbox.Instance.DeregisterObject(finalHeader);
            candidate.GetComponent<Persistent>().DontPersistInstance(true);
            candidate.SetActive(false);
            if (candidate.activeInHierarchy) return Retained(candidate, "retirement");
            counted = true;
            // Destroy is deferred: remove empty retired slots synchronously so a later
            // distribution in this frame cannot assign units to this inactive root.
            foreach (GuardSlot retiredSlot in remaining)
                if (retiredSlot != null) suppressionKingdom.RemoveGuardSlot(retiredSlot);
            UnityEngine.Object.Destroy(candidate);
        }
        catch (Exception e)
        {
            if (counted)
                try { UnityEngine.Object.Destroy(candidate); } catch { }
            LogError("retirement threw after mutations, " + (counted ? "deactivation confirmed" : "retaining") + ": " + e);
            try { if (candidate == null || !candidate.activeInHierarchy) return -2; } catch { }
            return Retained(candidate, "retirement");
        }
        Log("removed ordinary duplicate x=" + SafeX(candidate)
            + " witness=" + (witness != null ? witness.name : "<null>"));
        return 1;
    }

    // ---- synchronous Kingdom.DistributeTowerArchers suppression, scoped to one Kingdom instance ----
    // Native ExitGuardSlot -> GuardSlot.ExitArcher -> Kingdom.AddGuardSlot synchronously calls
    // DistributeTowerArchers, which refills each just-emptied tower slot before all archers
    // exit, so earlier actors reference the duplicate again at verify-exit. The scope below
    // suppresses that refill only for the exact Kingdom instance whose duplicate cleanup is
    // executing, only for the synchronous duration of one TryRemove, and always clears.

    private static Kingdom suppressionKingdom;
    private static bool cleanupActive;

    /// <summary>
    /// Harmony prefix body: skip DistributeTowerArchers ONLY while the exact Kingdom instance
    /// captured at scope entry is inside TryRemoveScoped. Returns true (fail-open, normal
    /// distribution) in every other case, including any undecidable check.
    /// </summary>
    internal static bool ShouldDistributeTowerArchers(Kingdom kingdom)
    {
        try
        {
            return !(cleanupActive && kingdom != null && suppressionKingdom != null
                && kingdom.Pointer == suppressionKingdom.Pointer);
        }
        catch { return true; }
    }

    /// <summary>
    /// Wraps the whole TryRemove core (precheck through final teardown) in the suppression
    /// scope. Refuses reentrant cleanup, captures Managers.Inst.kingdom at entry, and clears
    /// the scope in finally on every path including throws. No distribution is called during
    /// or after the scope: retired slots are removed synchronously after deactivation;
    /// end-of-frame slot.OnDestroy removal is idempotent. A retained candidate simply
    /// resumes normal distribution once the scope clears.
    /// </summary>
    private static int TryRemoveScoped(World world, Transform layer, IList<GameObject> occupied,
        GameObject candidate, GameObject witness, CRPCHeader plannedHeader)
    {
        if (cleanupActive)
        {
            LogError("reentrant cleanup refused, retaining x=" + SafeX(candidate));
            return -1;
        }
        Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
        if (kingdom == null) return Retained(candidate, "scope-kingdom");
        cleanupActive = true;
        suppressionKingdom = kingdom;
        try { return TryRemove(world, layer, occupied, candidate, witness, plannedHeader); }
        finally
        {
            suppressionKingdom = null;
            cleanupActive = false;
        }
    }

    private static int Retained(GameObject root, string stage)
    {
        try { Log("retained ordinary duplicate x=" + SafeX(root) + " stage=" + stage); }
        catch { }
        return -1;
    }

    private static bool ContainsPointer(List<Archer> list, Archer archer)
    {
        for (int i = 0; i < list.Count; i++)
            if (Same(list[i], archer)) return true;
        return false;
    }
}

/// <summary>
/// Prefix-only patch delegating to SpecialTowerDuplicateCleanup.ShouldDistributeTowerArchers.
/// Outside the active cleanup scope that helper always returns true, so the prefix is a
/// pass-through with near-zero overhead; it never alters distribution for any other
/// kingdom, frame, or code path.
/// </summary>
[HarmonyPatch(typeof(Kingdom), "DistributeTowerArchers")]
internal static class Kingdom_DistributeTowerArchers_CleanupScopePatch
{
    [HarmonyPrefix]
    private static bool Prefix(Kingdom __instance) =>
        SpecialTowerDuplicateCleanup.ShouldDistributeTowerArchers(__instance);
}
