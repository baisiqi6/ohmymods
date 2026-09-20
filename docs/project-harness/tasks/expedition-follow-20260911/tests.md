# Independent expedition-follow regression

**PASS: 49 tests, 0 failures.** The final frozen-candidate run passed without fixture or expectation changes.

| Source | SHA256 before and after run |
|---|---|
| `../build/SquadFollowGuard.cs` | `282E5566C452373FD0ADE8EA491443D8DF74EE9A8C815592523082A1B397A752` |
| `../build/PatchWorld_DefenseSpacing.cs` | `840A009C51484D164F03667C13BCDD7B724B4B0E1B3106D8B99D8F04C254802C` |

Evidence: `final.log`, `final-receipt.json`, `extraction-receipt.json`. Reproduce with `./run.ps1`; it checks source stability across compilation and execution.

## What is real production code in these tests

The entire actual `SquadFollowGuard.cs` is compiled by MSBuild Compile Link. `NightFollowerAnchorPrefix` is extracted verbatim from the actual DefenseSpacing candidate; its method SHA256 is `A409A551B670018120EF10549F23F185A40BC697D3BF2775E54AC4A1497C8E04`. The generic Mover fixture calls that actual prefix, preserves its ref offset, and runs its own ordinary native-API model only if the prefix returns true. Its Object destination is derived from the current goal object's x plus offset times goal-facing scale on every read. No guard or receipt algorithm is copied into the fixture.

The old prefix is separately preserved verbatim as a causal negative control, with original full-source SHA256 `8DDDB1810793F9D748EB657EDE7B3CB1C01D2CA18E1A478755B8C7C58B0E0476` and method SHA256 `5441D7E5C18EFE810A18BEA31D28185C7ACA37A1C1535730B2F7EBAC15D8178F`. It reproduces the reported bug: the float SetGoal path freezes the wall position, returns false, and skips the native Object-follow Wait. Moving the knight afterward does not move that old Position destination. The extraction script keeps this baseline snapshot when canonical is subsequently updated.

## Coverage

- The original bug control plus corrected dynamic Object target and unchanged native Wait, goal reference, speed and OffsetMode.
- Right and left walls with both facing signs, existing style-dependent pullback, no nested native SetGoal call from AdjustAnchor, already-deep positions and leaders outside the wall band.
- Leader charge, pending charge, formation, embarkation/boat target, manual control, retreat and non-wall FSM exclusions; follower tower/formation/boat/manual/inert/grabbed/dead/non-follow2 exclusions.
- Dawn and config disable restoring the original owned dynamic offset; exact owned reassertion retaining the original baseline; a genuinely new native offset becoming the new baseline.
- Charge/config changes **before** an exact owned reassertion still release the old wall spacing. A paused exact reassertion preserves the receipt and original baseline until the pause ends. Destroyed movers are removed from both the ledger and temporary snapshot references.
- External mover pause and global timeScale pause retaining the receipt without clearing pauses; loss of authority retiring receipts without native goal writes.
- External Position goals, speed/offset changes, different owner/mover, or higher Archer task preventing stale restoration. Clear only drops bookkeeping.
- Native SetGoal callback replacing the goal remains intact after old cleanup. Native restoration failure releases the recursion guard, allowing a later owner's normal clamp.
- Knightless wall mirroring requires native wall state 8 and excludes manual/chase tasks; daytime knight spreading requires actual Assemble without expedition intent.

Haglet's `started` field is true by default in the running-state fixture, matching an active native follow routine. All scenarios assert zero scene scans, zero Stop calls and zero UnPause calls.

The initial 45-case run passed source `F7439FC69765294079210C10E67F04D1C4A9CD1FE475936BC1B59E11C9419B85`. Independent review then identified the departure-before-reassert ordering gap and destroyed-mover bookkeeping risk. Four targeted cases were added without changing the earlier assertions. The Operator fixed the source before those new cases ran; therefore `reassert-red.log` is a historical filename for a **passing 47-case run of the corrected source**, not evidence of an executed red run. The final 49-case receipt above is the acceptance result.

## Integration and limitations

The four DefenseSpacing entry gates and the existing supervisor's Clear/Reconcile calls are present in the reviewed candidate source (day Assemble, ordinary archer mirror, assigned wall follower sweep and knightless sweep). This small harness compiles the real GO-prefix body and real helper predicates, **not the entire 1,000-line DefenseSpacing class or its complete patrol coroutine**. Operator owns its full actual-reference build and integration review.

These tests validate managed eligibility, geometry, ownership and native-call delegation. They do not execute IL2CPP Harmony detours, actual native Wait scheduling, pathfinding or an in-game expedition. Controlled runtime acceptance must still confirm followers advance with their knight under the real game's callbacks. No heuristic migration of old arbitrary Position goals is tested or introduced.

The test author changed only this test directory. No canonical source, worker/build candidate, game, save, configuration or deployment was modified.
