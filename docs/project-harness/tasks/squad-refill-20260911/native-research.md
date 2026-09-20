# Squad refill investigation — 2026-09-11

User symptom: Archer followers killed in battle are reportedly not replenished for non-Greek knight styles, while Greek works. This investigation is static and read-only. **No root cause specific to all non-Greek styles is established yet.** The strongest next action is bounded event sampling of the native refill/departure pipeline, not overriding recruitment eligibility.

## Actual 2.4 native findings

The actual E-game interop was parsed with Cecil. Native tokens were mapped to the actual GameAssembly codegen module table; small native code windows were decoded with bundled Iced. `native-addresses.json`, `native-disassembly.txt`, `input-hashes.json`, `inspect-native.ps1` and `map-native.py` reproduce the evidence without loading/executing game code. Pointer-slot uniqueness counts cover Assembly-CSharp.dll only and are not a guarantee that a new detour is safe in every runtime condition.

| Native method | RVA | Same-pointer slots |
|---|---|---:|
| Knight.Update | 0x5B7430 | 1 |
| Kingdom.FetchArchersForJob | 0x59DE90 | 1 |
| Archer.IsAvailableForJob | 0x4B2880 | 1 |
| Archer.AssignJob | 0x4AF340 | 1 |
| Archer.SetKnight | 0x4B5EF0 | 1 |
| Archer.RemoveFromKnight | 0x4B4D00 | 1 |
| Archer.OnDisable | 0x4B2C10 | 1 |
| Knight.OnDisable | 0x5B3500 | 1 |

Knight.Update still computes `_maxArchers - _archers.Count`. When positive and `Time.time - _lastArcherFetch > 10`, it calls `FetchArchersForJob(knight.gameObject, missing, 10f, null)`, then updates the timestamp. At VA 0x1805B74D5 it loads float from 0x1830A4BD8; the same value is compared against elapsed time and passed as maxRange at 0x1805B7514. The actual four bytes are `00 00 20 41`, float **10.0**. The call at 0x1805B752A targets Fetch RVA 0x59DE90.

This refill section precedes the Character.inert and FSM logic. Attack cooldown is decremented before it but does not gate refill. A player-control early return precedes refill; disabled Knight.Update, absent authority/MainScene lifecycle, exceptions, or an apparently full `_archers.Count` can prevent observable refill. A custom animation controller/FSM state alone is not a static explanation because the refill gate is before FSM.Update. No style check appears in this native refill branch.

Actual Archer.IsAvailableForJob still rejects: existing guard slot, existing knight, inactive object, grabbed character, embark target, formation, and (for a Knight job only) existing NpcShieldUser without a shield. At VA 0x1804B2968 the native code obtains the shield component; if present, it checks its shield-enabled state at 0x1804B299A and returns false when absent. There is no knight style branch in this eligibility routine.

Actual Archer.OnDisable still has world-authority/MainScene gates. For a non-null `_knight` at native field offset 0x1F0 it calls Knight.RemoveArcher at 0x1804B2E80, then clears the owner pointer at 0x1804B2E85. This agrees with the 2.1 reference. A death/demotion that never reaches OnDisable, or that throws/returns before this section, can leave a stale counted membership; that possibility needs an event trace and is not proven by the symptom.

## Reference and canonical source findings

- `game-source/Assembly-CSharp-2.1.0/Knight.cs:395`: Update refill is style-agnostic, ten-second interval and ten-unit search radius. Its `_archers` HashSet count is authoritative for missing slots; it does not count only living/active members.
- `Archer.cs:235`: OnDisable unregisters formation/guard, removes itself from its knight and Kingdom, clears `_knight`, then converts to hunter. `RemoveFromKnight:927` also removes both sides. `Knight.RemoveArcher:988` removes from HashSet. A stale list is possible only if this chain did not complete or was subsequently corrupted; it is not inherent to non-Greek controllers.
- `Kingdom.cs:2749`: Fetch scans the native Kingdom Archer set, uses IsAvailableForJob and optional validation, sorts candidates by distance, preserves the minFreeArchers budget, and only assigns candidates with `abs(dx) < maxRange`. A candidate at exactly 10 is outside the native refill radius. Accepted eligibility therefore does not imply an assignment: distance and free-reserve budget still apply.
- `Archer.isAvailable` differs from IsAvailableForJob. The former is used to reduce the free-reserve counter for some non-selected units. Report the actual raw budget/candidate counts; do not assume `minFreeArchers` is simply subtracted from all archers alive.
- Canonical Knight style application changes controller/scale and follower packages; reviewed style power Update hooks are void prefix/postfix hooks, not prefixes returning false to skip native Knight.Update. No reviewed style module assigns `_maxArchers`, `_lastArcherFetch` or clears native `_archers`.
- `PatchRoles_NorseSquad.HandleAssignJob`: only runs after native IsAvailableForJob/Fetch selection. It converts/links the selected Archer, or equips an already-Norse selected Archer. **It cannot rescue a shieldless Archer rejected before AssignJob.** This is a plausible Norse-specific bottleneck, but it does not explain Medieval/Deadlands/Shogun all failing while Greek works.
- `PatchRoles_Crossbowman` intentionally rejects marked independent crossbowmen for Knight jobs. Deadlands squad members have no marker by design, so a genuine marked/nonmarked count is needed before attributing the failure to that filter.
- Prior project notes warn that Il2Cpp HashSet enumeration is unreliable in the existing environment. Do not enumerate/rebuild Knight._archers as a speculative repair or scan the scene anew every frame. Reading Count and observing native add/remove events is enough for the first diagnostic.

## Minimal diagnostic contract

No new scene scan, resource load, automatic roster edit, forced SetKnight, free shield grant, maxRange change, minFreeArchers change or timer override.

1. Wrap native `Kingdom.FetchArchersForJob` with prefix/postfix/finalizer context limited to jobs that resolve to a Knight. Capture world/knight pointer, resolved style, requested count/maxRange, native before/after knight count, `_maxArchers`, `_lastArcherFetch`, minFreeArchers and the native available-cache final count. Record elapsed Time.time only as a scalar. A before/after summary should distinguish “no candidates”, “all beyond range”, “reserve exhausted”, and “selected but no net roster growth”.
2. While inside that Fetch call, observe the **existing** Archer.IsAvailableForJob invocations. Count true/false, candidate distance, crossbow marker and raw busy/guard/knight/grabbed/embark/formation/shield predicates. Capture both native result and final result after existing postfixes if feasible; never call IsAvailableForJob again from its own diagnostic hook. An eligibility true outside range must remain distinct from a native rejection.
3. Context must be stack-scoped or saved/restored with Harmony `__state`, then restored in a finalizer. Assignment/promotion can synchronously trigger OnDisable and Kingdom redistribution, creating nested fetches. A single static current-request overwritten by nested calls will misattribute evidence.
4. Observe actual AssignJob/SetKnight outcomes if a non-Greek request has accepted candidates but zero growth. Norse promotion may disable the original Archer and attach a replacement; capture old/new pointers and native count, not an assumption that the original object's `_knight` must become the target.
5. Add bounded Archer.OnDisable entry/exit evidence: original owner pointer/style, owner count before/after, active/dead flags, world-authority and main-scene state, and final `_knight`. Use its prefix `__state` to preserve the original owner because native clears `_knight`. A finalizer records a thrown exception and marks an incomplete native path. Nested redistribution remains attributed to its own Fetch context.
6. If affected knights never emit Fetch over >10 scaled seconds, add a throttled read-only Knight.Update observation of Count/max/timestamp/control/active/enabled/authority. This resolves “apparently full roster”, “native update not executing”, and “timer never eligible” before choosing a repair. Avoid expanding per-candidate logging when the native refill gate never runs.

Aggregate counters, cap per-episode sample pointers (e.g. 8), throttle identical reports and clear diagnostic state on world unload. Do not log every candidate every frame. Actor lifecycle identity should include native pointer, not only recycled Unity instance ID.

## Minimal repair after evidence

- Proven stale death membership: repair only the demonstrated missed removal event/owner pair, using native RemoveArcher/RemoveFromKnight semantics and lifecycle checks, not replacing every roster from a global scan.
- Proven no-shield rejection: address the narrow intended recruitment/registration timing for that eligible unit family, preserving shield readiness/network contracts. Do not globally return true from IsAvailableForJob.
- Proven free-reserve/range exhaustion: treat as native rule behavior first and explain it. Changing those rules requires a deliberate design decision; it is not justified by “non-Greek refill broken” alone.
- Proven successful selection but failed Norse conversion: fix that bounded AssignJob/promotion outcome; keep native fallback and prevent a disabled original Archer from being added.
- Proven native Update suppression/control issue: fix only the responsible custom hook or stale control ownership; do not add a parallel global recruitment loop.

Current recommendation: proceed with the event sampler. Static evidence rules out an obvious native style/FSM refill gate but does not identify which runtime branch is failing for the reported units.
