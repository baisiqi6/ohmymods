# Expanded duplicate-tower cleanup design review

Scope: user explicitly authorized retaining original special/hermit towers and removing ordinary duplicate bases/towers at the same construction site, including ordinary towers mistakenly upgraded several levels. This review adds no approval request. Reviewer read source and actual E2.4 interop metadata only; no source/game/save/config change or game operation.

## Recommended eligibility

Use a separate same-site duplicate-ordinary-tower path, retaining the prior strict KEM empty-base visual-overlap cleanup for its original scope. Do not generalize visual overlap into permission to demolish any built normal neighbor.

- A protected special root is identified by actual TowerKnight, Ballista, FireTower, Baker, OilFireArcherTower or a recognized SpecialTowerRebuildMarker/type-bearing descendant; the root must be normalized to the actual building owner. WorkableBuilding alone is not a special-tower identifier because built ordinary towers also have it. Names alone are insufficient.
- Preserve every special root, including multiple special roots at the same coordinate. Never use one special tower as a reason to remove another.
- The candidate must be an actual ordinary Tower with valid level>=0, current active scene/layer, nonboat, Persistent and a valid exact SemiStatic network header. Reject a candidate with any protected special type/marker in its own structural hierarchy, and reject candidate/witness ancestor-descendant relationships. This prevents deleting a normal-looking component that belongs to the special root itself.
- Require a distinct live protected witness at the same finite x coordinate within a narrow site tolerance (suggested0.1). Revalidate both identities, their world/layer and that distance immediately before irreversible root mutation. A broad rendered-footprint overlap alone is insufficient for unmarked or built ordinary towers.
- Retain global enabled/current-world/active-layer/authority guards and the existing online skip. Do not add new multiplayer teardown behavior.
- Retain all payable/player protection, including selectedByP1/P2/interactingPlayer/PlayerSelecting and both players' selectedPayable/_completingPayable across related hierarchy. Unknown hierarchy queries preserve the tower. Checking only price or interactingPlayer is insufficient.
- Built ordinary towers normally carry WorkableBuilding and ConstructionBuildingComponent, so their mere presence must no longer prohibit this new path. Require known completed state: UnderConstruction/NeedsMoreWork false and no active scaffold linked to this building. Missing/inconsistent construction state or an active construction project remains protected. Never force-complete or refund a project to enlarge the deletion scope. A level0 object may follow the stricter existing empty-base guard.

## Native teardown facts and risk

The2.1 native reference shows Tower.DestroyTower iterates GuardSlots and calls Archer.ExitGuardSlot, then instantiates a fresh native tower-location prefab and registers it. Calling it here would recreate the duplicate base. Do not use Tower.DestroyTower, IDestructiblePlayerStructure.DestroyPlayerStructure, FakePay, PayableUpgrade.Pay or the special-tower rebuild path as a demolition shortcut.

GuardSlot.OnDestroy calls ExitGuardSlot and removes the slot from the kingdom only while the main scene is active. Depending solely on this late callback risks deleting or disabling child units before their native release finishes.

Archer.ExitGuardSlot first calls GuardSlot.ExitArcher, which can clear slot.archer/isArcherPresent and invoke leave/visibility callbacks. The remaining method restores targeting/physics/coin collection and then reparents to world.gameLayer; _guardSlot is cleared at the end. Thus a null slot.archer after a partial exception is NOT proof that the archer is safe.

Persistent.DontPersistInstance(true) recursively changes child Persistent components. If an archer remains under the ordinary tower, this can remove its persistence; Destroy(root) can also remove that actor. Keep all candidate archers as explicit saved references during the teardown attempt, even if their slot reference becomes null.

The special-tower rebuild code contains inventory guards for Ballista bolts, Fire fuel/projectiles and worker tasks. These are reasons to exclude special roots entirely, not teardown logic to transplant into ordinary-tower cleanup. Native WorkableBuilding.OnDestroy removes its work registration; do not manually clear worker actor sets. Conservatively wait for construction to finish; a transient unresolved worker/task state can be retained rather than forced.

## Minimal safe execution sequence

1. Build a bounded plan of candidate ordinary root plus preserved special witness; collect candidate-root GuardSlot descendants including inactive children. Validate all ownership, payment, completed-construction, world/layer, exact-header and same-site conditions. Do not mutate anything yet.
2. Snapshot each nonnull slot/Archer pair and de-duplicate archer pointers. Require the archer's actual GuardSlot/_guardSlot to be that slot and the slot to belong to the ordinary candidate. A stale slot that points to an archer now owned by another tower is not permission to release the other tower's unit. Inconsistent isArcherPresent without a resolvable archer should be treated as unknown.
3. While the tower and its header remain active/registered, call native Archer.ExitGuardSlot once per validated archer. It may use slot/parent sync metadata and mod exit hooks; do this before root deregistration, DontPersistInstance, SetActive(false), or destruction. Recheck authority/context between operations. On any error, stop demolishing this candidate; do not manually clear slot fields or force-reparent to simulate success.
4. Verify the saved archers are still live/current-world, no longer reference a slot under the duplicate root, and are no longer children of that root. Re-enumerate GuardSlots to ensure no remaining occupants and ensure no live Archer remains inside the doomed hierarchy. A failure after slot.archer became null must still block root demolition. Avoid touching any archer from the preserved special tower.
5. Re-run candidate/witness identity, same-site, payable, construction, actor-safety and authority gates. Native callbacks can change state; the earlier plan is not authority for the final mutation.
6. Retire only the ordinary root through the existing safe path: deregister its exact native header, DontPersistInstance for its now actor-free hierarchy, synchronously deactivate, then Destroy(root). Let native Tower.OnDisable, GuardSlot.OnDestroy, Persistent and WorkableBuilding callbacks perform their usual list cleanup. Do not manually mutate kingdom/slot/save collections or call the special-root rebuild hooks.
7. Count a removal and update occupancy only when actual root deactivation/destruction is confirmed. On partial native failure, retain unresolved active occupancy, stop further demolition in that pass and record the exact root/witness identities. Do not keep issuing destructive operations or invent network/persistence rollback. If release succeeded but root demolition was conservatively stopped, grounded archers with the original tower retained are preferable to deleting an uncertain actor.

## Actual2.4 API check

Read metadata from the independent E test copy's BepInEx/interop/Assembly-CSharp.dll with Mono.Cecil; no target code was executed. Confirmed public APIs/properties:

- Tower.level and OnDisable.
- GuardSlot.archer, isArcherPresent, ExitArcher and OnDestroy.
- Archer.ExitGuardSlot(), HasGuardSlot(), GuardSlot, _guardSlot and inGuardSlot.
- WorkableBuilding.UnderConstruction, _constructionBuilding and OnDestroy.
- ConstructionBuildingComponent.NeedsMoreWork, _scaffolding and OnDestroy; Scaffolding.Building.
- TowerKnight, Ballista, FireTower, Baker and OilFireArcherTower types. OilFireArcherTower is a sibling Workable, not a FireTower subtype; explicitly protect it.
- Persistent.DontPersistInstance(bool, List<GameObject>) and NetworkPostbox.GetHeaderFromObject(GameObject,bool)/DeregisterObject(CRPCHeader).

Native ordering above is grounded in2.1 reference behavior and should be treated as a conservative risk model, not proof that every2.4 machine-code branch is identical. Actual API availability is confirmed; managed integration tests and controlled runtime actor-survival/save checks remain needed.

## Smaller provenance option and verification

If an exact prior KEM site ledger is available for this campaign/island, it can narrow unmarked-upgraded cleanup to those recorded sites. Do not infer such provenance from rounded names or lose requested coverage merely because upgrading removed the marker. The user's same-site special-plus-ordinary authorization supports the narrow typed-witness rule without pretending every unmarked ordinary tower is mod-owned. A read-only preflight receipt should record candidate/witness types, names, pointers/native IDs, exact x/levels and occupants before teardown.

Tests should include all protected special types, level0 and higher unmarked ordinary duplicates, nearby nonduplicate towers outside tolerance, same-hierarchy special child, active scaffold and completed construction, payment/cancellation windows, archers already reassigned elsewhere, native exit throwing after slot clear but before reparent, repeat release/idempotence, retained special archers, authority/layer loss, and no replacement base spawn. Runtime acceptance must verify released archers survive and saved state contains the preserved special root without the ordinary duplicate. This design does not require modifying the save file directly.
