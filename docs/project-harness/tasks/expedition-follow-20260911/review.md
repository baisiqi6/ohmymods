# Final independent expedition-follow review

Verdict: PASS. No remaining P0-P2 finding in reviewed final source.

SquadFollowGuard.cs SHA256: 282E5566C452373FD0ADE8EA491443D8DF74EE9A8C815592523082A1B397A752.

## Root-cause correction

The object-target SetGoal prefix now changes only the ref Formation offset and always permits the original native call. Object tracking and the original returned Wait remain intact. It no longer creates a static wall Position goal. Offset calculation accounts for actual signed facing and rejects non-finite/near-zero inputs.

Wall adjustment is restricted to ordinary native-follow defenders with their own knight goal, appropriate night state/position, and no conflicting mission. Generic wall mirroring excludes knight followers and unrelated Archer tasks. The parked sweep only reissues goals for genuine wall defenders. Day spread requires the named native Assemble state.

The existing three-second patrol reconciles owned offsets before normal day/night/config gates. Restoration checks the exact Object/Formation target, identity, written offset and speed; it does not compare dynamic _goalPosition. Higher-priority Archer tasks or external goal changes retain control. The leader may depart while its still-following archer restores normal spacing. Native/external pauses are preserved; ownership is retained while paused. Lost authority and departing worlds clear private receipts without client or old-world writes.

## Closed review findings

The earlier exact-reassertion P2 is closed: if an owned written tuple is reasserted after departure/daytime/config-off, a still-native-follow archer restores the original ref offset in the native call itself when safe. During pause it preserves the receipt for later reconciliation. Different external tuples and busy Archer tasks are not overwritten.

Lease.Key now supports Current/Forget using stored managed bookkeeping rather than requiring a live Mover.Pointer. A destroyed mover can therefore be recognized as the current receipt, fail Owns, and be removed, preventing the identified retained-reference leak.

Snapshot and restoration flags are released in finally. Receipts retire before restoring calls so callbacks cannot erase a newer receipt. Native call failure does not retain recursion protection. No new hook target, scene/resource scan, roster mutation, FSM jump, teleportation, Stop, or UnPause is introduced. Old static Position goals are not guessed or migrated; deployment requires restart, which reconstructs native follow from persisted knight links.

## Validation boundary

This final review independently reread both helper logic and the defense integration diff. The reassertion defect was identified by source review; by the time the independent tests ran, the operator had already applied the fix, so no old-version red test is claimed. Final independent tests pass 49/49, including charge/config-before-reassert, paused exact reassertion and destroyed-mover cleanup. The final test receipt records stable matching 282E5566 source hashes. Operator owns the actual dependency build and runtime acceptance. This reviewer did not run tests/game, deploy, or change canonical/game/source files. Only this report was written.

Real in-game arrival/following and 2.4 behavior-state execution still need controlled validation. Read-only metadata and managed tests cannot prove all native timing behavior. The revised goal remains dynamic even before the next three-second reconciliation, preventing the original permanent static-wall pin.
