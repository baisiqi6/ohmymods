# Boat regression result

RESULT: **PASS — 91 passed, 0 failed**, process exit code 0. Final run UTC 2026-09-11T15:56:54Z.

The test project CompileLinks the actual `../build/FleetGreekSquads.cs`. `Extract.ps1` copies the complete, unchanged `TryBuildCandidates` and activation `Finalizer` methods from the actual `../build/PatchWorld_FleetBoatFormation.cs` into a compilation shell. No selection, reservation, score or cancellation algorithm is reproduced in the test code. Lightweight Unity/game/Harmony stubs supply fixture state and record native-style membership writes.

## Coverage

- All 40 combinations of 0–4 boats and 0–7 eligible Greek knights select `min(boats, knights)`.
- Real candidate snapshot checks boat side, scene ancestry, disabled/occupied/inaccessible boats, maximum four, duplicate numbers, and descending registration order.
- Knight side/style eligibility; another boat, main boat or formation cannot supply a supposedly free knight; own preassignment wins over lower-ID free knights; two players cannot reserve one knight or boat twice.
- Legacy nonGreek embarked passengers keep their original score and exclude their boat from new selection. New nonGreek Knight assignments receive 100000. Other unit types, main boats, client/disabled mode and prior native rejection retain original scores.
- Candidate probing bypasses only its own reservation gate and restores the gate afterward, including native false results.
- Pending plans survive the next frame before membership. Complete preserves actual membership and removes failed recruitment.
- External formation, main-boat retarget, charge, player control, puzzle task, death, side change, disabled/harmless state, CanEmbark loss and unassigned external FSM missions retire reservations. Native embark-target FSM movement remains valid.
- Prune and score callbacks do not mutate formation membership. Maintain performs deferred cancellation only for the still-owned, actually registered boat; external formations and replacement plans are protected.
- Unembarked plans expire after 60 seconds of `Time.time`; embarked plans survive that deadline.
- Authority loss, world changes, queued world changes, queued authority loss and Clear issue no old-world or nonauthority writes.
- ReleaseKnight clears a previous pooled lifetime and queues cancellation; a replacement pair survives the older queue entry. Clear drops both ledgers and queued writes.
- `_numSquads` values 0 and 2 are skipped. This is a runtime capacity guard, **not evidence that the actual prefab serializes 1**.

## Added final-review exception cases

The actual activation Finalizer method is now also extracted unchanged and compiled. Five additional cases pass:

- A simulated native ActivateFormation exception before Postfix releases an already-embarked pending candidate even after 1000 seconds, preserves the original exception object, and restores only the empty baseline.
- A native partial RegisterUnit membership with `_currentFormation == null` retires the plan in Finalizer and removes the actual membership later in Maintain, with no immediate writes.
- A different nonnull current formation prevents cancellation even if the original array still contains the boat.
- A valid fully registered embarked candidate survives Finalizer, but subsequently losing current membership retires it, proving Pending was ended.
- A successful native return leaves Finalizer inert.

The extraction shell stubs AllUnitsEmpty and baseline restoration, so these tests execute real Finalizer control flow and real FleetGreekSquads cleanup without claiming full formation array restoration or Harmony runtime execution.

## Added native embark transition cases

Two additional cases pass: a registered reservation that receives its own boat target and becomes stationary while IsEmbarked is still false survives both 0.5s and 1.0s maintenance without Unregister; after IsEmbarked becomes true it also survives the deadline. Stationary with no embark target still retires and cancels the owned ship normally. This models the native OnEmbarkStart-to-completion ordering without executing actual Unity tweens.

## Evidence and rerun

See `run.log` for individual cases and `hash-receipt.json` for exact source and fixture SHA256 values. Source hashes were checked before and after the run and did not change.

```
dotnet run --project Regression.csproj
```

Run from this boat-tests directory. The project runs Extract.ps1 before compilation. SDK used: .NET 6.0.427. Generated CandidateSource.cs and FinalizerSource.cs are included as an artifact so a first clean compilation also discovers it.

Final helper SHA256: `8457993075B76C089E211FC41028FDE0772A6DFFD002B3FB51E31838DEAE31AF`.

Final formation integration source SHA256: `EE3C98DA1AF02242BA2E6DC50522E6B5E6409FCDC66345775E7B18AD94E27178`.

Read-only wiring inspection additionally confirmed Maintain on the existing 0.5-second formation coordinator, Clear at DefenseSpacing.SupervisorRoutine world initialization, and ReleaseKnight in KnightStyle.OnKnightActivated. The entire lifecycle patches were not executed in this lightweight harness.

## Limits

This verifies real managed business logic against deterministic fixtures, not IL2CPP detour installation, native JobAssigner behavior, Unity coroutines, physics, animation, multiplayer transport or actual game prefab layout. No game was run. No canonical, game, save or config files were written. All test writes stayed in boat-tests.


