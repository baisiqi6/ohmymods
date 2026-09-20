# Samurai return implementation

Only `PatchRoles_SamuraiPowerDash.cs` in this isolated directory is a candidate for integration. No canonical, game, configuration, save, Git, or test files were modified by this worker.

## Behavior

- Host/config/style 2 only. Valid free knight states: `Stand`, `GoToWall`, `Assemble`. Player control, retreat, charge, formation, pillar, embarkation, dead/inert/grabbed/stationary/harmless actors are excluded.
- A nearest active living archer owned by this knight more than 10 units away starts return before attack cooldown or enemy acquisition. Return completes at distance <= 4.
- One initial white-glow/trail/invulnerable burst: speed 18, destination at most 7 units away, duration at most .6 scaled seconds. The destination leaves 2.5 units of space on the approaching side of the follower.
- Longer gaps continue using the knight's native `_runSpeed`, without return damage or further invulnerable bursts in that episode. Dynamic run destination refreshes no more often than .2 seconds.
- Every episode has a hard 3-second scaled deadline. No reduction of follower distance by .1 units for .5 seconds cancels early. Failures wait 2 seconds and have at most three attempts for the same follower. Rejoining within 4, changing follower, or resetting actor lifecycle clears this allowance. Failed detached knights cannot use attack dashes during backoff/lockout.
- No live follower means no invented camp/wall destination; native behavior resumes. Active follower ownership/liveness is checked every Tick. Candidate selection uses the existing shared `GetArchers()` default 3-second cache and this consumer's .2-second selection timer.
- Paused time does not expire timers. No new dash starts while timeScale <= 0 or native Mover has an external pause.

## Ownership and cleanup

One `ActorState` owns at most one `MotionLease`, mutually excluding attack and return. Return uses a Tick state machine; attack retains a coroutine with its own lease. A retired coroutine's late finally cannot stop a new goal, alter new effects, reset cooldowns, or release a newer lease.

Native component identity compares Unity `Pointer`; managed `ReferenceEquals` is used only for the lease object. Before active writes, the implementation checks configuration, world authority, style, actor eligibility, current mover/damageable/trail, and the exact Position goal tuple it last issued. External goals, mover replacement, or external pause cause cancellation before the burst-to-run transition. Cleanup stops only the still-owned goal; it never restores a stale goal or unpauses another system.

Invulnerability and trail baselines belong to the lease, and are restored only if the written `true` is still present. The underlying booleans cannot identify another writer setting `true` to `true`; that limitation remains explicit. No ninja smoke assets, teleportation, native Dispose hooks, or new injected component types are introduced.

Existing Update and OnDisable hooks remain; Update cleanup no longer early-returns merely because config is disabled. The only added native hook is a narrow `Knight.ShouldSlash` prefix: suppress only during a currently valid, goal-owning return farther than 4 units. It does not skip Knight.Update.

Attack numbers remain cooldown 3 seconds, speed 18, maximum travel 7, invulnerability .6 seconds, hit radius 1.2 and original `_attackDamage`/`DamageSource.Knight`. Attack cleanup and ownership are strengthened, using the same eligible free states.

## Validation boundary

Independent `dispose_native_audit` runner: 48 passed, 0 failed after correcting three test timing assumptions against the original contract. Production source was not changed to satisfy those corrections. Tests include mutual exclusion, bounded effects, extended running, moving/dead/unowned followers, actor eligibility transitions, external goals/mover replacement, shared-cache scan cadence, old attack coroutine disposal after a new return, and finite failure behavior.

The passing source SHA256 is `4E69DF3FB7598D3E7F9B8966FDB7CF8CB65A5CF6B0346EE292C4C5A340E0C83E`. Between the initial test run and this run, worker self-review added exception-safe lease retirement in `Finish` and a fail-open exception boundary in `IsReturning`; it did not alter return timings or address those three test assumptions by changing production behavior.

Operator reported the isolated actual-dependency build passed with 0 warnings / 0 errors, three hooks bound, and 58 reachable Unity calls without unstripping failures. Reviewer is independently checking the source. Operator owns final native method-address checks, integration, deployment, and live acceptance. This worker has not launched the game or claimed runtime success.
