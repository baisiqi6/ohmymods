# Independent squad-refill diagnostic regression

**PASS: 36 tests, 0 failures.** This suite directly compiles the two actual Operator candidate files using MSBuild Compile Link; it does not implement recruitment or copy the diagnostic algorithm.

| Candidate | SHA256 verified before and after run |
|---|---|
| `../build/PatchRoles_SquadRefillDiag.cs` | `4180F6C3246F178D354F12734088FF11C3CC8BA6CC8E4C7A35EF0ED3475A6731` |
| `../build/SquadRosterSnapshot.cs` | `90D6D750FD16E485D67D431F0261DD77EA513228D57D3774B0EF170CBEF4D98D` |

Evidence: `final.log`, `final-receipt.json`. Reproduce with `./run.ps1`; it rebuilds linked production sources and rejects changing source hashes.

## Tested behavior

31 event-diagnostic cases verify:

- Original job/request/range/validator arguments, native predicate result, and original exception identity are preserved. The finalizer is void and predicate observer uses Priority.Last.
- Accepted/rejected callback counters, nearest eligible candidate including out-of-range candidates, strict native `< maxRange`, occupied/guard/formation/embark/grab/shield/inactive overlapping categories, reserve decrement and effective reserve evidence, and native before/after count changes.
- Job-pointer filtering; unsampled nested calls mask outer counters; nested exceptions and unsampled root calls restore previous context. Repeated finalization with the cleared Harmony state cannot duplicate a completion line.
- Per-style 30-second and shared event 3-second gates; Fetch and Archer disable events share the same 30-line lifetime cap; no new actor reads past that cap.
- Config/authority/pause/known-style/style-range/active/request gates; unassigned and paused disable events remain silent.
- Diagnostic error output remains bounded. Native exception completion, count getter failure, pointer getter failure and logger failure cannot propagate or leak the active sample context.
- A supplied native validator is never invoked, and effective reserve is marked unknown rather than pretending its unobserved results are known.
- A 1,256-callback stream inspects only the first 256. After those callbacks the native job-pointer getter is configured to throw: the following 1,000 callbacks do not read it, while the summary explicitly marks truncation and unknown effective reserve.

Five roster-helper cases verify:

- Native occupancy versus living owned followers, dead follower exclusion, mismatch reporting and `far10` counts without changing the supplied roster.
- The 120-second gate and five-attempt lifetime cap, including no reads/errors after the cap.
- Pause/config/client suppression without consuming a snapshot.
- The 64-knight / 1,024-archer bounds and explicit truncation. A throwing count getter on knight 65 is never read.
- Even the diagnostic warning logger throwing during error reporting cannot escape into the existing caller.

## Fixture scope

Fixtures supply generic Unity object/component identity, Time, Harmony attributes, primitive native getter state, and captured logging. Native Fetch, AssignJob, IsAvailableForJob, scene query, and validator methods increment a forbidden-call counter and throw if the diagnostic invokes them; every test asserts zero forbidden calls. Native predicate outcomes are supplied by the test runner, not recomputed. The runner updates knight counts directly only to represent an already-completed native operation and then checks the diagnostic readout.

The fixture resets static diagnostic bookkeeping only between independent cases. Within each case, the real prefix/finalizer and helpers exclusively manage sampling state and budget. Reflection drives the actual Harmony-attributed hooks and preserves updated `__state` between calls. Tests do not simulate Unity rendering, IL2CPP detour ABI, native recruitment decisions, or real runtime frame cost.

## Status and limitations

There are no remaining managed test failures. The worker draft was initially unbuildable (private `TrySelect` called by another class); no behavioral acceptance claim is made for that draft. All final results apply only to the two hashes above.

This verifies the diagnostic's read-only control flow and bounded work, **not a fix for missing reinforcements or proof of the cause**. Operator still owns actual interop compilation, native hook-address checks, source integration and controlled runtime log acceptance. The test author changed only this test directory; no canonical source, worker source, game, save, configuration or deployment was modified.
