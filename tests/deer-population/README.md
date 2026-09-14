# Ordinary deer population regression

Run from the repository root:

```sh
dotnet run -c Release --project tests/deer-population/Regression.csproj
```

The fixture directly links `il2cpp/PatchWorld_DeerPopulation.cs`. Final worker result: **27 cases passed, 0 failed**, using `C:/Users/ADMIN/dotnet8/dotnet.exe`.

The single PopulationController.Update hook temporarily multiplies normal/default-winter/special-winter density inputs by three and divides the actual update interval by three. Eligibility requires enabled mod, world authority, Greek biome, native `playingOrInMenuWithClient`, an active enabled controller in the current world gameLayer and scene, `useBiomeCritters == false`, and a same-object Deer component on its prefab without Steed or Hind. Operator native/asset inspection confirmed the ordinary deer forest controller and its current-gameLayer parenting.

The per-invocation state is a struct. Active tokens prevent same-controller nesting from multiplying twice. Postfix and Finalizer restore only fields still equal to this invocation's applied values, preserve native exception identity, and never write to a replaced controller/GameObject identity. A cleanup failure retains only the failed field bits in a separate Pending dictionary. The next Begin excludes active nesting first, retries pending cleanup before any config/authority/eligibility checks, and never borrows unresolved boosted values as a new baseline. Matching-token Finalizer can also retry pending cleanup immediately. Stale finalizers cannot affect newer tokens. Normal successful calls allocate no receipt object or pending record.

Coverage includes:

- Temporary inputs and exact restoration, three-times refill/cap decisions, native winter zero, special winter selection, minimum forest width, unchanged three-coin loot and deer scale.
- Mod/authority/biome gates, menu-with-client native eligibility, critter/Steed/Hind exclusion, current scene/layer membership and inactive controllers.
- Nested same/different controllers, native exceptions, independent external field writers, restoration after config/world/authority/active changes, exact identity replacement.
- Full prevalidation, NaN/infinity/overflow/underflow rejection, partial setter failures before or after a simulated commit, per-field cleanup failures, and continued native execution.
- Consecutive cleanup failure/recovery without 9x compounding, persistent failure, mod-off/client cleanup, pending external takeover, identity reuse and temporary identity-read failure, and old/same-token finalizers.
- One bounded application log with prefab and original/applied density/interval values.

The decision simulation uses the audited native Update's elapsed-time, seasonal-density, minimum-region and `ceil(region.width * targetDensity)` rules. In an exactly representable fixture, nine seconds yields three baseline replenishment decisions versus nine adjusted decisions; a longer run stops at capacities 10 and 30. Both execute the same number of native-body simulations. The test never calls Pool or creates a creature. The production patch does not write `_targetDensity`, `_elapsedTime`, minimum region, serialized updateInterval, prefab, loot, scale, or network state. Real integer capacities retain the native ceiling rule.

These are managed boundary/decision tests, not real IL2CPP GC, detour, spawn, or multiplayer tests. Complete IL2CPP compilation, native review and controlled game acceptance are Operator-owned. The worker only created this new production file and this test directory; it did not modify existing sources, version stamps, deployment, game state, configuration, saves, commits or remotes. The established built-in worker fallback was used; no external model/provider claim.

Final production SHA256 at worker handoff: `80FE4C2F13209B73244E17E114FF3A6E96FE9D7CC4A8B66C4CC440E54130BCE4`.
