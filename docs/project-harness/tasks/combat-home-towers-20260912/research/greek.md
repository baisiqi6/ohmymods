# Greek Fire at home: independent source/API investigation

## Findings

**Standing inside the wall is not, by itself, a trigger blocker in the current mod.** `il2cpp/PatchRoles_GreekFire.cs:62–71` already accepts a valid current `_shootingTarget` from any living, currently owned Archer follower, measured from that Archer with `ActiveArrowAttack.Range`. It does not require the target to also be within the knight's scanner range. `Follower` at lines 38–40 does not exclude a tower slot, formation, stationary character or height; it requires the actual `_knight.Pointer` identity, life/active state, character and embark readiness.

**The concrete detector gap is that the follower path reads only the cached shooting target.** If that target is absent, dead, invalid or stale while a different valid enemy is present in the same follower's existing native enemy scanner, the current HasEnemy ignores that evidence completely. The existing Knight scanner path cannot compensate when the knight is farther behind the front line. This gap is directly visible in the current mod source; whether it caused every user-observed missed cast is not established by the available runtime log.

**A normal Archer assigned to an arrow tower is no longer necessarily a knight follower.** In the 2.1 native reference, `Archer.SetGuardSlot` (Archer.cs:843–849) first calls `RemoveFromKnight(false)`, then assigns the slot and tower scanner range. `RemoveFromKnight` (929–934) removes the member from the knight and clears `_knight`. The behavior priority also chooses a tower job only when `!HasKnight() && HasGuardSlot()` (413). `EnterGuardSlot` alone (1294 onward) does not clear `_knight`, but the normal assignment path already did. This is reference-native behavior, not a reconstructed 2.4 method body; the 2.4 APIs are present as verified below.

Consequently, visually similar soldiers on a tower must not be treated as current squad members based on appearance, nearby wall, side or remembered knight. Wall-following squad archers with `_knight` intact should trigger their knight's ability. Archers whose tower assignment has cleared `_knight` should not trigger an arbitrary nearby Greek knight without a separately approved design change.

## Available runtime evidence

`tower-overlap-20260912/before-deployment-LogOutput.log` contains:

- Lines 64–66: three successful missing native fire-arrow data restorations to `Archer_Greece_FireArrowAttack`.
- Line 208: original `Buff_fire_attacks_hephaestus_anvil`, ID 5, original duration 3, clone duration 8, one matching asset.
- Lines 209, 210 and 225: three GreekFire casts, each with `recipients=5`, duration 8 and cooldown 15.

This demonstrates that the installed code successfully reached the native buff pipeline for whole squads at least three times. Five recipients is consistent with one knight plus four current followers, though this counter also includes recipients whose longer existing buff was preserved. These lines do not identify the trigger source, positions, guard slots or attack state, so they cannot prove the cast was specifically defensive or offensive, nor that all future casts worked.

The code caps cast diagnostics at three per process (`CastLogs < 3`), so missing later cast lines are not evidence that later casts stopped. The subsequent night-combat/frame-rate lines are temporal context only, not a per-cast attribution.

## Why target-only polling can miss an ongoing battle

The 2.1 Archer reference provides a concrete ordering model:

1. `ShouldShootEnemy` at 1055 first rejects cooldown, fleeing/mode transitions and other native conditions. Those early returns do not refresh or clear the old `_shootingTarget`.
2. At 1073, once re-selection proceeds, it clears `_shootingTarget`, iterates its own scanner results and writes a selected eligible target at 1091.
3. `Shoot` (1000–1049) retains the selected object across preparation and repeated shot intervals. A target can die before the next GreekFire poll; the subsequent cooldown can postpone native re-selection.

Thus it is inaccurate to say native code necessarily clears `_shootingTarget` after every arrow. It is nevertheless valid that null/dead/stale target data can coexist with other nearby shootable enemies. The mod's .25-second poll currently has no fallback for that state.

The native scanner itself has interval-limited Refresh. In the reference, GetAll returns the existing filtered buffer after refresh/active compaction, while GetClosest uses that same cache. Neither is a full scene FindObjects call. They may perform the scanner's normal local physics refresh when its own interval expires; do not describe scanner access as guaranteed zero work or a pure immutable read.

## Smallest justified correction

Keep the current knight `Eligible`, `KnightReady`, authority/pause, style, current squad identity, native Buffable owner/applicability, RPC indices, fire-resource/pool and cooldown guards. In particular, do not remove `_harmless` merely to force a defensive cast: the reference sets it during petrification together with inert state (Knight.cs:1759–1766) and clears it during recovery (1792–1797); ordinary wall defense does not set it in this reference.

Within the already-owned follower branch only:

1. Preserve the current valid `_shootingTarget` fast path.
2. If it does not provide a valid enemy, inspect that same follower's existing `_enemyScanner` for a valid current enemy.
3. Use a bounded iteration of `GetAll(out ...)` rather than only GetClosest if practical: the nearest scanner entry may be dead, ignored-invulnerable or otherwise invalid while another entry is shootable. GetAll is already an actual native selection API, and native buffers need not be sorted. Enforce a small explicit per-follower limit and validate returned count against array length; do not assume the 2.1 buffer's length of 12 is a universal 2.4 constant.
4. Keep the current target checks: active hierarchy, Enemies layer, enabled/living Damageable, Arrow damage compatibility, ignored-invulnerable exclusion and horizontal distance from the Archer within its actual `ActiveArrowAttack.Range`. Recheck scanner range/position where needed so an old scanner buffer cannot authorize a distant target after a range or pose change.
5. Continue using `UnitScanCache.GetArchers()` with its existing default three-second scene cache. Never reduce that shared age to .25 just because this consumer polls at .25. Do not retain the native scanner result array across the call.
6. Recipient selection remains the current owned living Archer snapshot, with per-recipient ownership/lifecycle and native readiness rechecks before application. A tower guard with `_knight == null`, another knight's follower or a same-side unrelated archer is neither trigger evidence nor a recipient.

Do not call `Archer.ShouldShootEnemy()` as an observational getter: its reference body mutates `_shootingTarget` and executes native selection policy. No new FireArrow/iterator hook is needed for this bounded scanner fallback. Do not replace `_shootingTarget`, force shooting, widen combat range, modify native scanner rates, or activate a buff by bypassing Buffable.

A fallback that only establishes a valid enemy in a current follower's combat range matches the existing skill's scanner-based trigger model; it does not prove an arrow was emitted that exact frame. If the intended product behavior instead requires an actual shot event, that needs a separately defined event contract, not a guessed deletion of eligibility checks.

## Actual installed 2.4 API verification

Read-only Mono.Cecil metadata inspection of the E-copy `BepInEx/interop/Assembly-CSharp.dll` confirmed:

| API | Verified form |
|---|---|
| `Archer._knight`, `_enemyScanner`, `_shootingTarget` | Public generated native-field getters; target is GameObject |
| `Archer.ActiveArrowAttack` | Public getter with normal native invocation |
| `Archer.harmless`, `_attackMode`, `_desiredAttackMode` | Public generated field getters |
| `Archer.shoot` | Public getter returning `Coatsink.Common.IHaglet` |
| `Archer.inGuardSlot`, `SetGuardSlot`, `RemoveFromKnight` | Public normal native-invocation wrappers |
| `Archer.ShouldShootEnemy` | Public normal native-invocation wrapper; existence does not make it read-only |
| `Scanner.GetAll` | `int GetAll(out Il2CppReferenceArray<GameObject>)`, public normal native invocation |
| `Scanner.GetClosest` | Both GameObject-returning and bool/out-GameObject forms exist |
| `Scanner.range`, `rangeBehind` | Public native-field getters |
| `ArrowAttack.Range` | Public normal native-invocation getter |
| `Knight._harmless`, `_enemyScanner` | Public native-field getters |

No Unstrip body appeared in the inspected accessors/methods. These were not called in the game. `Knight.holdFire` from the old reference was not found under the expected getter in the bounded 2.4 check; do not copy that old API into a new patch without a separate verification. The current mod does not require it.

## Candidate regression set

Positive cases:

- Knight scanner empty because knight is behind the wall; current owned living Archer's `_shootingTarget` is valid: the existing trigger must still work unchanged.
- Same setup with target null or already dead, but own follower scanner contains a valid in-range enemy: the new fallback triggers one squad cast.
- Own scanner's nearest entry is invalid but another bounded entry is valid: do not miss the battle by testing only GetClosest.
- A stationary wall-following Archer with intact ownership remains valid; do not accidentally import the Samurai movement guard that rejects stationary actors.

Negative/invariance cases:

- Same-side wall/tower Archer with `_knight == null`, or another knight's Archer with a valid target/scanner: no trigger for this knight and no buff granted to that unrelated actor.
- Native SetGuardSlot clears the old squad link: no historical-membership leakage. No forced reattachment to a knight.
- Null/dead/inactive/wildlife/out-of-range/Arrow-immune/ignored-invulnerable scanner entries alone do not trigger.
- Ownership changes during the native scan or during Buffable callbacks: no stale trigger evidence or stale recipient application. Arrays are bounded and cannot leak between different knights.
- Existing 8-second duration, 15-second cooldown, longer-buff preservation, config/authority/pause behavior and missing RPC/pool readiness remain unchanged.
- No new full-scene scan, no repeated resources load on each poll, no shader or stat mutation, and no native ShouldShootEnemy/FireArrow call from the detector.

If runtime evidence is needed, extend only the existing small successful-cast diagnostic budget with `trigger=knight|follower-target|follower-scanner` and the triggering follower identity/range. Do not infer failure from the deliberately exhausted three-line cast budget or add per-frame target dumps.

## Scope of this investigation

Current mod behavior is established from canonical source. Detailed native target/guard-slot ordering is from the 2.1 reference; installed 2.4 metadata confirms API shape, not identical native internals. The log proves successful prior squad casts but does not identify the user's missed defensive scenario. No source, game, save, configuration or deployment was changed, and no live game calls were made. Only this research note was written.
