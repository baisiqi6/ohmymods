# Independent Samurai retreat and farm-cat regression

**PASS: 63 tests, 0 failures** — 43 retreat-speed cases and 20 farm-cat cases, including four new partial-native-exception cases.

| Production candidate | SHA256 verified before and after final run |
|---|---|
| `../build/SamuraiRetreatSpeed.cs` | `3E5CC530619ECD0B601B09B15399EE2241BC02B3AC91503398CB2C6D9689B14D` |
| `../build/PatchWorld_DefenseSpacing.cs` | `E981D90B67BD1190D6919A123DB015BBF82F96E8792461404BB4D322DCD5FF17` |
| `../build/PatchWorld_FarmCats.cs` | `0A6C9435DAAC550CBA13939946FC401E5351ECB350EF4B398647B1E8CE2E8884` |

Current acceptance evidence: `partial-final-receipt.json`, `partial-final-retreat.log`, `partial-final-cats.log`. Reproduce with `./run.ps1`; it rebuilds actual linked sources and rejects source changes during verification. The earlier `final-*` files document the superseded 59-case run and do not establish the new exception behavior.

## Retreat speed — actual helper plus actual prefix wiring

The complete `SamuraiRetreatSpeed.cs` is compiled through MSBuild Compile Link. The actual `DayAssembleSpreadPrefix` dispatch method and its actual Harmony float-overload prefix are extracted verbatim from the current DefenseSpacing source. The two unrelated downstream day-spread/archer-mirror branch bodies are recording fixtures, so this verifies dispatch and ref-speed propagation without copying or testing the whole existing spacing algorithm.

- Only a resolved style-2 knight, native GoToWall state, isRetreating=true and incoming native `_retreatSpeed` receive exactly x3 in the local speed argument.
- Styles 0/1/3/4, unknown style, ordinary run 6, dash 18 and a different return-run speed 4 remain unchanged against a retreat baseline of 2.
- No repeated x3 stacking; already-adjusted speed stays adjusted once. `_retreatSpeed` is checked unchanged, including bit-preserving non-finite inputs.
- Zero/negative/non-finite inputs, invalid native baselines and multiplication overflow are not boosted.
- Other FSM states, formation, embarkation/targeting/boat target, manual control, charge/pending charge, death/inactive/inert/grabbed/stationary/pillar and mismatched or missing mover/actor are excluded.
- Config-off and non-authoritative clients do not modify arguments.
- The real Harmony speed parameter is `ref float`; the actual dispatcher routes it through the cached Knight branch and forwards the updated local value. Native goal, mode path and returned Wait remain intact. The cached Archer branch retains its input speed.

Extraction hashes (`retreat/extraction-receipt.json`):

- Dispatch method: `1357B3335F41E7E292CCF943D1AC311A24E90CBDB19BBF6E38D60F6D3A92A75B`
- Harmony hook: `9B892945CF70BBF0273C3D317472A0B8258FF7E47A10EEAD9058A0D44668664D`

## Farm cats — complete actual production file

The entire `PatchWorld_FarmCats.cs` is compiled through MSBuild Compile Link. Tests invoke its real StockFarmCats, retirement recheck and actual config-gated level-load hook. Generic scene/pool fixtures provide input actors and model immediate deactivation versus deferred Destroy; no counting, eligibility, retirement-selection or replenishment algorithm is reimplemented in tests.

- Six old mod-marked cats become four, with exactly two native retirement calls and no replacement spawn.
- Two native plus four mod cats become four while preserving both native cats. More than four native cats remain untouched; removable marked extras are retired without deleting native surplus.
- Exactly four are unchanged; an underfilled farmhouse is supplemented to four using the existing production spawn setup.
- Deferred native Destroy still retires only the two excess cats: pending-deletion actors are synchronously inactive before subsequent candidates are processed, preventing all six from being selected.
- Picked-up and player-following cats survive. If all candidates are protected they remain above target, without forced removal or extra spawning.
- Foreign farmhouse binding, unbound, undomesticated and unmarked cats fail the actual retirement recheck. Stocking does not cross farmhouse ownership.
- Injected native retirement failures leave the active population unchanged, report zero successful retirements and do not generate compensating cats.
- A first still-active, uncertain retirement failure stops further deletion for that farmhouse; the original failure case now also requires exactly one attempt rather than permitting retries of the other candidates.
- If Pool first deactivates the object and then throws, actual completed retirement is counted: exactly two cats are retired and four remain. The same exact-four assertion passes when deferred Destroy returns normally but SetActive(false)'s callback throws after deactivation.
- If native code queues deferred destruction and then throws while the cat is still active, only that farmhouse's deletion pass stops. An independent farmhouse still reaches exactly four; after the queued deletion completes, the uncertain farmhouse has five cats rather than falling below four, and no compensating cats are spawned.
- If the post-exception active state itself cannot be read, the actual retirement helper reports `uncertain=true` and does not claim success.
- Online play performs no changes or scene scans and does not consume the later eligible single-player guard. Wrong biome, missing authority and config-off remain gated. Repeating the same world/layer does not scan or retire again.

## Boundaries and ownership

These tests verify managed control flow, eligibility, arithmetic, ref argument propagation, count updates and delegation to native pool APIs. They do not execute IL2CPP Harmony detours, actual native defensive movement, native destruction/Persistent unregister callbacks, save reconstruction or rendering. Native failure fixtures now explicitly cover failure before mutation, failure after deactivation, failure after queuing a deferred deletion, and failure to observe the resulting state; they do not claim to exhaust every engine exception or prove the actual Persistent callback ordering.

The TryRetireFarmCat fixture driver was adapted to its new `out bool uncertain` parameter. Previous population assertions were retained; four targeted cases were added following independent review. No production source was changed by the test author. The DefenseSpacing file hash changed between runs, while the two extracted retreat-prefix method hashes above remained unchanged.

Operator owns actual-reference compilation, the complete DefenseSpacing integration, native API audit and controlled runtime acceptance. This test scope covers retreat speed and farm cats, not the separate fleet changes in the same task directory.

The independent test author modified only this test directory. No canonical/build/worker source, game, save, configuration or deployment was changed.
