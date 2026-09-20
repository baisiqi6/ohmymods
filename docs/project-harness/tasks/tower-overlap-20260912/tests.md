# Independent tower-overlap regression

**PASS: 46 tests, 0 failures** against the complete actual Operator candidate `../build/PatchWorld_TowerSpots.cs`.

Final production SHA256 before and after execution:

`381D96464ED3330C4509580F562ED22CEBA3DA1B4E1CC35E512E9C71CF5C5B0C`

Evidence: `final.log`, `final-receipt.json`. Reproduce with `./run.ps1` or `dotnet run --project Regression.csproj`. Both default to the tested build candidate, not the rejected worker file. The runner rejects source changes during verification.

## Real source and fixture boundary

The test project compiles the **entire production file through MSBuild Compile Link**. No occupancy, deletion, safety, geometry or expansion decision algorithm is copied into the fixture. The runner invokes the actual ExpandTowerSpots pipeline; targeted lifecycle tests additionally invoke the actual snapshot builder, retirement method and ExpandSide after changing fixture state between those real phases.

Generic fixtures provide Unity objects/components, tag and component scene queries, layer hierarchy/scene handles, basic renderer Bounds and Rect overlap math, players/Payables, native header bookkeeping, persistence calls and object activation/destruction. Scaffolding.Building is modeled as its real GameObject property. Player._completingPayable is a property, matching the IL2CPP wrapper API; it is intentionally not a fake CLR field that would conceal the rejected reflection bug.

## Covered behavior

- The reported `156.6399536` KEM base versus `156.6399994` untagged WorkableBuilding, with no ordinary Tower component on the special building. The safe generated base retires; the existing structure remains.
- Ordinary built Towers, native unbuilt bases and non-center footprint intersections block overlapping generated bases. Cleanup uses the candidate's actual instance footprint instead of oversized prefab bounds.
- Active scaffolding preserves occupancy evidence for its explicitly linked inactive Building. Inactive pool buildings without that relationship, foreign scenes and boat structures are excluded. Deactivating the captured scaffolding or changing its Building reference invalidates the old relationship before deletion.
- Cleanup still runs at multiplier 1 and with fewer than two native references, while generation remains disabled in those modes. Safe generated peers deterministically retain the earlier x; exact-position peers retain exactly one.
- Native bases, built generated Towers, missing Persistent or PayableUpgrade, non-SemiStatic headers, selection flags, active partial-payment interaction, PlayerSelecting, construction, WorkableBuilding and Scaffolding components are protected.
- Player selected/completing Payables in the same hierarchy protect a candidate. Payment beginning after overlap proof is rechecked before the first network/persistence mutation.
- Mod-off, online and non-authoritative modes do not mutate. An inactive current game layer cannot be mutated even through a previously captured cleanup/expansion snapshot.
- Zero/NaN/infinite candidate renderer bounds do not authorize deletion. Invalid native runtime template bounds do not authorize generation. Unknown occupied bounds neither authorize old-base deletion nor get discarded to permit new spots.
- A valid native runtime template supports generation even when the asset prefab renderer reports zero bounds. Known special-building footprints block new candidates without globally disabling otherwise valid density.
- WorkableBuilding and Scaffolding scene collections are each queried once per full expansion pass. Scene-query failures fail closed. Retiring one root preserves an independent same-x building in both the root snapshot and distance list.
- Post-snapshot scene or IsChildOf failures remain unknown, preventing both deletion and generation. Hierarchy exceptions while checking a player's selected/completing child Payable conservatively protect the candidate.

## Rejected worker evidence

The original worker source SHA256 `211B6D76A2245F6E92DC0E2F4487E9D3B1005F9048009C0E26ED9FCBCEA49CBD` ran the initial 36 cases with **29 passes / 7 failures** (`worker-red.log`, `worker-red-receipt.json`). Failures reproduced lost inactive scaffold-building evidence, CLR-field reflection missing the real completing-payable property, unsafe cleanup on invalid candidate visual bounds, and generation despite invalid runtime-template/occupied bounds.

The Operator candidate passed those same assertions, then the ten targeted lifecycle/exception cases added after review. No existing protection or geometry assertion was weakened.

## Limits and ownership

Passing these tests verifies managed control flow and the values/operations delegated to Unity/native APIs. It does not prove actual Unity renderer AABB computation, native network/persistence callback ordering, real saved-scene reconstruction, or visual in-game spacing. The fixture's basic renderer bounds are deterministic geometry inputs, not a Unity graphics engine. Actual-reference build/API audit and controlled load/save/runtime acceptance remain the Operator's responsibility.

Only this test directory was modified by the test author. No canonical, worker/build production source, game, configuration, save, deployment or Git state was changed. No game was launched and no runtime success is claimed here.
