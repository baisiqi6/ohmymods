# Independent combat visuals regression

**PASS: 170 tests, 0 failures.** All four production source hashes remained unchanged before and after the final compiled test run.

| Suite | Passed | Actual production source and SHA256 |
|---|---:|---|
| Motion and visual-token integration | 79 | `../build/PatchRoles_SamuraiPowerDash.cs` — `3D35E55C021A16AB2242E2A9E4911735585B0F4712590546A4A6E13DB4ECD715` |
| Greek Fire and native dependency restoration | 79 | `../build/PatchRoles_GreekFire.cs` — `3607BA796CF1D0D384124C9F075761C427EB1310DF3ABAD0F65BF1B9E907E1A4`; `../build/PatchRoles_GreekFireAssets.cs` — `958CE9EBC92774C42833DE7C7BE78647A486E8700E085B56413A615EF9A65117` |
| Actual afterimage helper | 12 | `../build/SamuraiDashVisuals.cs` — `EE2E76000CE579E459F101E63FFBB29B740ED59840562B187AB9C864DC44D58C` |

Evidence: `final-receipt.json`, `final-motion.log`, `final-greek.log`, `final-visuals.log`. Reproduce with `./run.ps1`. Each suite uses MSBuild Compile Link to the actual candidate source; source hashes changing during the aggregate run cause failure.

## Motion: 79 passing cases

The established 76-case motion/damage suite is reused, including forward and return burst damage, per-Damageable deduplication, reentrant native damage callbacks, frozen external goals, cleanup, cooldown/range/speed, burst-to-running transition, life-cycle replacement and old coroutine ownership. Its former native GlowCount observation now records the new visual helper's Begin calls; movement and damage assertions are unchanged. The actual OnDisable prefix and postfix both execute in the fixture.

Three additional tests check that a burst begins one visual token without calling the old timed GlowOverlay, the return's ordinary running phase ends its visual token exactly once, and OnDisable clears visuals before mover cleanup callbacks.

This suite uses a recording visual API stub to isolate motion from rendering. The real helper is independently compiled and tested below.

## Visual helper: 12 passing cases

- At most three ghost renderers plus one body-overlay renderer; an initial ghost makes short bursts observable.
- Own renderers receive numeric `_Overlay = Color.white` in MaterialPropertyBlocks; the original sprite color, material reference and property block remain unchanged. Source `.material` access, shared-material property writes, scene scans, prefab cloning and sorting-layer string setters are forbidden by the fixtures.
- Independent ghost world poses remain frozen as the owner moves; source flip, numeric sorting layer and signed facing scale are retained.
- Recency opacity .45/.25/.10 multiplied by remaining .2-second lifetime; fixed renderer/object reuse across many moving frames; no per-frame material allocation.
- End immediately hides the body overlay; the ghost tail disappears within .2 seconds. Stationary units do not replenish identical ghosts.
- Pause freezes ghost aging; Clear remains immediate while paused. Config/style loss and pooled owner disable clean up safely; late old-token End cannot stop the replacement dash.
- Injecting failure during the second helper renderer creation returns no token and destroys the partial ghost/root atomically.

The fixtures model generic transforms and render-state objects; no production visual decision algorithm is copied into them. They verify object topology and values handed to renderers, not GPU output.

## Greek Fire: 79 passing cases

The previous 70 cases still pass, covering native Buffable duration/cooldown, authority/style/life-cycle gates, pointer ownership, native callback reentry/departures/disable, longer buff preservation, read-only original assets, pool/RPC readiness, crossbow restoration order and scan throttling.

Nine new cases cover:

- Ready biome-swapped projectile pools even if the original prefab lacks a pool; rejection when only the unrelated original pool exists; unchanged original ArrowAttack/prefab pointers.
- Actual missing-fire dependency repair on a client without granting FireAttacks or touching shared resources.
- No field assignment while the effective native projectile pool is unavailable; Greek-only Ensure gating; world/biome mapping refresh for a new missing instance while previous non-null instances remain untouched.
- Unassigned client RestoreMissing preparing a nullable native field before the squad link arrives, without selecting ActiveArrowAttack or activating a buff; inapplicable recipients remain untouched and cause no asset load.

The prior three crossbow cases still compile the actual 59-line `ApplySquadCrossbowPackage` body verbatim from the current build candidate. `greek/crossbow-extraction.json` records full source SHA256 `4C8DB21A414CBA9A92126CEDAF0B20A6EDA8576AFE8DD043C49A6CEAF2DDBD68` and method SHA256 `314320E0D361495E219394DC847DBA0460FC8C9427BBB531954CFE4762EBF8B8`.

## Fixture correction and negative evidence

The first aggregate run passed motion 79/79 and visuals 11/11, with Greek 76/77. Its one failing pointer-alias case constructed two managed Knight objects with the same native pointer but different Style values. Real wrappers read the same native field. The fixture now sets the alias Style to the owner's Style while retaining distinct wrapper objects and the original pointer-ownership assertion. No production source or behavior expectation was changed for this fixture correction.

Before the swap fix, the three new swapped-prefab tests all failed as expected against the previous Greek implementation, while its prior 70 cases passed. This was development feedback, not final acceptance. The original afterimage worker file remained unbuildable and had shared-transform/lifetime defects; it was not used as the final production source.

## Boundaries

The independent test author modified only this test directory. No canonical source, worker source, game, save, configuration or deployment was changed.

These managed tests do not prove actual Unity shader silhouette brightness, native ICall resolution, live frame cost, remote-client visual signaling, native 8-second coroutine expiry or fire-arrow pool synchronization. They do not compile the entire KnightStyle conversion/patrol integration or the medieval slash renderer; Operator owns the full actual-reference build, transitive API audit, review of those integration points and controlled runtime acceptance. No remaining managed failure is known for the tested hashes.
