# Worker result — Greek fire trigger fix + same-site duplicate ordinary-tower cleanup helper

Date: 2026-09-12. Model requested: GLM-5.3 max. Actual model used: **GLM-5.3** (bigmodel/GLM-5.3, as reported by this agent's harness).

Files written (only the three allowed):

- `PatchRoles_GreekFire.cs` — edited (one bounded change vs canonical, verified by diff)
- `SpecialTowerDuplicateCleanup.cs` — new
- `RESULT.md` — this report

No changes to canonical/, game, save, config, git or network; no game launch, no deployment, no live native calls. Read-only references used: `il2cpp/` canonical mod (esp. `PatchWorld_TowerSpots.cs` teardown/guard patterns, `PatchWorld_SpecialTowerRebuild.cs` marker type), `game-source/Assembly-CSharp-2.1.0/` native reference, `research/greek.md`, `review/tower-design.md`, `research/same-site-save.json`.

## 1. Greek fire: bounded follower-scanner fallback (`PatchRoles_GreekFire.cs`)

Diff vs canonical is exactly one hunk in `HasEnemy` (lines 67–82). Changes:

- Follower fast path preserved unchanged: valid own `_shootingTarget` measured from that Archer within `ActiveArrowAttack.Range` still triggers as before, as does the knight `GetClosest` scanner path.
- New fallback, only after the fast path fails for a follower: read that same Archer's existing `_enemyScanner`, call `GetAll(out Il2CppReferenceArray<GameObject>)`, iterate `0..min(count, array.Length)` (no assumed buffer-size constant) and run each entry through the **original** `Target` validation (active hierarchy, Enemies layer, living `Damageable`, invulnerable-ignored exclusion, `IsDamagedBy(Arrow)`, horizontal distance from the Archer within its actual `ActiveArrowAttack.Range`). Any valid entry triggers.
- Deliberately not only `GetClosest`: the nearest buffered entry can be dead/ignored-invulnerable while another entry is shootable.
- The result array is a local, never retained across the call; no full-scene `Find`, no scanner refresh forcing, no `ShouldShootEnemy` call, no `_shootingTarget` writes, no native mutation. Recipients, guards (Eligible/KnightReady/authority/pause/Buff/RPC/pool), Duration 8 / Cooldown 15, `UnitScanCache` 3 s shared cache all untouched. Tower archers with `_knight == null` and other knights' followers remain excluded by the unchanged `Follower` check.

API form used, `Scanner.GetAll` returning `int` with an `Il2CppReferenceArray<GameObject>` out parameter, matches the 2.4 interop verification table in `research/greek.md` (verified 2.1 shape: `Scanner.cs:89`).

## 2. `SpecialTowerDuplicateCleanup.cs` (new helper)

`internal static class SpecialTowerDuplicateCleanup` with the single API `internal static List<GameObject> RemoveDuplicates(World world, Transform layer, IList<GameObject> occupied)`. It defines no hook, patch or driver — the operator wires it to the existing WorldLoad TowerSpots occupancy snapshot and reads the returned confirmed-removed roots. Scanning is limited to each candidate's own `GetComponentsInChildren` and the passed `occupied` list; no scene-wide scans, no team caches (report-first if ever needed).

Eligibility (all fail-closed, re-run immediately before the first mutation and again after occupant release):

- Global gates mirror `PatchWorld_TowerSpots`: `ModConfig.Enabled`, `!NetworkBigBoss.IsOnline`, `HasWorldAuth`, world active and identical by pointer, `game.state == Playing`, current `gameLayer` active and pointer-identical to the passed layer, `NetworkPostbox.Instance` present, non-boat (parent-name walk; undecidable ⇒ boat risk ⇒ retain).
- Ordinary identity: live active root in the layer scene, `Tower` component with `level >= 0`, `Persistent`, exact SemiStatic header via `GetHeaderFromObject(root, true)`.
- Special protection: any `TowerKnight`/`Ballista`/`FireTower`/`Baker`/`OilFireArcherTower`/`SpecialTowerRebuildMarker` anywhere in the candidate's own hierarchy (including inactive children) disqualifies it and every special root is only ever read, never a deletion target. Hierarchy queries that throw count as protected.
- Same-site witness: a distinct live active root from the occupancy list, not an ancestor/descendant of the candidate, same layer scene, real typed special component (not name-based), itself completed, horizontal `|dx| <= 0.1` and `|dy| <= 0.5`. This covers the three recorded pairs in `same-site-save.json` (KnightTower/KEM 156.64, Ballista/Tower4 166.66, Fire/Tower1 176.68, dx ≤ 4.6e-05). Visual-footprint overlap alone is never sufficient.
- Construction/payment: `WorkableBuilding.UnderConstruction` false, `ConstructionBuildingComponent.NeedsMoreWork` false, no `CBC._scaffolding`, no `Scaffolding` component in the hierarchy (mere presence of Workable/CBC on a built tower no longer blocks this path); `Payable.selectedByP1/selectedByP2/interactingPlayer/PlayerSelecting` all clear and neither player's `selectedPayable`/`_completingPayable` hierarchy-related. Unknown ⇒ retained.

Teardown sequence per `review/tower-design.md`:

1. Full plan revalidation, then snapshot `GetComponentsInChildren<GuardSlot>(true)` on the duplicate root; each slot's `archer` must point back via `Archer.GuardSlot`/`_guardSlot` to that same slot (stale slots pointing at another tower's unit are skipped); archer pointers de-duplicated. `isArcherPresent` without a resolvable archer is unknown ⇒ stop.
2. While the root is still active and its SemiStatic header registered, one native `Archer.ExitGuardSlot()` per validated archer; any throw retains the tower and stops the pass — a nulled `slot.archer` is never treated as success.
3. Release verification: each saved archer's GameObject still live/active, no longer a child of the duplicate root, its `GuardSlot` no longer under the root; re-enumerated slots show no remaining occupants (a newly present occupant ⇒ stop, never evict a newcomer).
4. Full gate re-run (world/layer, same-site witness identity, payment, construction, ordinary identity, exact header pointer equality with the planned header).
5. Only then: `DeregisterObject(finalHeader)` (exact header), `Persistent.DontPersistInstance(true)`, `SetActive(false)` (must actually be inactive to count), `Object.Destroy(root)`. `Tower.DestroyTower` is never called — it re-instantiates a fresh base.

Return value counts only roots actually deactivated/destroyed; a partial native failure retains unresolved state, stops the pass, logs the exact identities, and never cascades DontPersist/Destroy onto released archers. No refunds, no forced construction, no teleports, no worker-set mutation, no save edits.

Only public interop members are used (all verified in the evidence tables): `Tower.level`, `GuardSlot.archer/isArcherPresent`, `Archer.GuardSlot/_guardSlot/ExitGuardSlot`, `WorkableBuilding.UnderConstruction`, `ConstructionBuildingComponent.NeedsMoreWork/_scaffolding`, `Scaffolding`, `Persistent.DontPersistInstance(bool)`, `NetworkPostbox.GetHeaderFromObject(GameObject,bool)/DeregisterObject(CRPCHeader)`, `Player.selectedPayable/_completingPayable`, `Payable.selectedByP1/selectedByP2/interactingPlayer/PlayerSelecting`. No `GetField` private access. Header identity comparisons use interop `Pointer` equality (SemiStatic re-checked at every fetch).

## Not done / handed to operator

- Build, test wiring and deployment are explicitly out of scope here; the regression sets from `research/greek.md` §Candidate regression set and `review/tower-design.md` §Tests should be run by the operator's build/test pipeline.
- No runtime evidence was collected; all native-ordering statements trace to the 2.1 reference risk model in the review documents.
