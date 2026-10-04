# Ordinary deer population regression

Run from the repository root:

```sh
# PC host: links il2cpp/PatchWorld_DeerPopulation.cs with the global game types.
dotnet run -c Release --project tests/deer-population/Regression.csproj

# Typed ANDROID host: same shared source compiled with ANDROID (Il2Cpp.* aliases) and the
# Android platform config; reuses the same 27-case harness plus the Android-only cases.
dotnet run -c Release --project tests/deer-population/android-host/DeerPopulationAndroidHost.csproj
```

Current result: **PC 27 cases passed, 0 failed**; **typed ANDROID host 32 passed, 0 failed**
(the same 27 cases under the Android alias/config surface plus five platform cases: default-off
empty-owned-state zero-interop fast path — the double counts and rejects `controller.Pointer`,
the earliest native access in Begin — Greek 5 scope apply with captured log evidence,
opt-in-off owned-pending cleanup to its own bits only, owned-active reentrancy, and a negative
control where the enabled path with the throwing earliest interop is caught with one warning and
no field writes). The shared production file is linked directly in both projects — no copy of
the logic.

The single PopulationController.Update hook temporarily multiplies normal/default-winter/special-winter
density inputs by three and divides the actual update interval by three. On PC the eligibility gate is
`ModConfig.Enabled`; on Android it adds the default-off `ModConfig.DeerPopulationEnabled` opt-in, and a
disabled call with no owned lease state returns before reading `controller.Pointer` (zero interop —
the typed-host double counts and, with ThrowInterop, rejects `controller.Pointer` as well as the
prefab/enabled/useBiomeCritters and four density-field accessors, so the earliest native access is
covered). Eligibility requires world authority, Greek biome,
native `playingOrInMenuWithClient`, an active enabled controller in the current world gameLayer and
scene, `useBiomeCritters == false`, and a same-object Deer component on its prefab without Steed or
Hind; the Android build captures the same-invocation scope evidence (scopeGoId scoped to the existing
lease ObjectId, scene handle, actual IsChildOf result, and the actual deer/steed/hind component checks).
The new evidence fields add no extra native reads: each one reuses a value already read in that same
Eligible call, never re-running GetComponent/IsChildOf or re-taking the scene at log time; the
pre-existing `prefab.name` read in the log line and the PC LogApplied surface stay unchanged, and the
prefab creation origin remains Unknown.

The per-invocation state is a struct. Active tokens prevent same-controller nesting from multiplying
twice. Postfix and Finalizer restore only fields still equal to this invocation's applied values,
preserve native exception identity, and never write to a replaced controller/GameObject identity.
A cleanup failure retains only the failed field bits in a separate Pending dictionary; the next Begin
retries pending cleanup before any config/authority/eligibility checks (also while disabled) and never
borrows unresolved boosted values as a new baseline. Matching-token Finalizer can also retry pending
cleanup immediately. Stale finalizers cannot affect newer tokens. Normal successful calls allocate no
receipt object or pending record.

Coverage includes:

- Temporary inputs and exact restoration, three-times refill/cap decisions, native winter zero, special winter selection, minimum forest width, unchanged three-coin loot and deer scale.
- Mod/authority/biome gates, menu-with-client native eligibility, critter/Steed/Hind exclusion, current scene/layer membership and inactive controllers.
- Nested same/different controllers, native exceptions, independent external field writers, restoration after config/world/authority/active changes, exact identity replacement.
- Full prevalidation, NaN/infinity/overflow/underflow rejection, partial setter failures before or after a simulated commit, per-field cleanup failures, and continued native execution.
- Consecutive cleanup failure/recovery without 9x compounding, persistent failure, mod-off/client cleanup, pending external takeover, identity reuse and temporary identity-read failure, and old/same-token finalizers.
- One bounded application log with prefab and original/applied density/interval values (plus the Android scope evidence fields on the typed host).
- Android: default-off zero-interop fast exit (including `controller.Pointer`), Greek 5 apply with same-invocation evidence, opt-in-off owned cleanup, outer-lease reentrancy, and the enabled earliest-interop-failure negative control (one warning, no writes).

The decision simulation uses the audited native Update's elapsed-time, seasonal-density, minimum-region and `ceil(region.width * targetDensity)` rules. In an exactly representable fixture, nine seconds yields three baseline replenishment decisions versus nine adjusted decisions; a longer run stops at capacities 10 and 30. Both execute the same number of native-body simulations. The test never calls Pool or creates a creature. The production patch does not write `_targetDensity`, `_elapsedTime`, minimum region, serialized updateInterval, prefab, loot, scale, or network state.

These are managed boundary/decision tests, not real IL2CPP GC, detour, spawn, or multiplayer tests. Complete IL2CPP compilation, native review and controlled game acceptance are Operator-owned. The PC host was the original worker deliverable; the C8 Android port adds the typed ANDROID host and the Android-only conditional cases and links the same production file.
