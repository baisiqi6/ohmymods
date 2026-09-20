# Independent Samurai return tests

**48 passed, 0 failed** against the delegated replacement source `../samurai-return-fixed/PatchRoles_SamuraiPowerDash.cs`.

Evidence: `fixed-second-results.txt` and `fixed-second-receipt.json`. The production source SHA256 was identical before and after the run:

`4E69DF3FB7598D3E7F9B8966FDB7CF8CB65A5CF6B0346EE292C4C5A340E0C83E`

Only this test directory was written. No production/worker implementation, game configuration, save, Git state, or deployed binary was modified. No game or native IL2CPP assembly was executed. The implementation was never changed by the test author to satisfy a test.

## What the tests execute

The net8 console project links the actual replacement production source. Test code invokes the real Knight Update and OnDisable hooks and discovers the real ShouldSlash prefix via its Harmony target metadata. The stubs supply Unity components, scaled time, generic coroutine scheduling, simple movement, damage counters and shared-array access counters. They contain no Samurai return algorithm.

Unity-like coroutine starts execute through the first yield immediately. A silently canceled coroutine can later be disposed to test stale finally behavior. The replacement Return implementation uses Tick state, so its pool-reuse test exercises OnDisable/new-owner behavior directly; the retained production Attack coroutine is really started, retained and later disposed after a new Return owner starts.

## Covered outcomes

- No enemy and native/internal attack cooldown do not prevent returning when the squad gap exceeds 10.
- Exact 10 does not trigger, exact 4 finishes, and the intermediate 4..10 band keeps the active return without starting another attack or burst.
- A normal 10.8-unit gap closes to at most 4 within one protected white-trail burst. Larger gaps finish with ordinary `_runSpeed`, with invulnerability/trail restored after at most 0.6 seconds or seven units of burst travel (one simulation-step tolerance).
- Return performs zero hit-window scans and zero damage even when the environment supplies enemy colliders.
- Config, style, authority, manual/already-active control, retreat, embarking, formation, death, charge, inert/grabbed/stationary state and unrelated FSM tasks release owned motion/effects.
- Config-off OnDisable remains reachable. Both Return and Attack preserve initial invulnerability/trail values, and identifiable external true-to-false changes survive cleanup.
- External position/object targets, replaced Mover identity, and new targets installed precisely at the burst-to-run boundary are never stopped or overwritten.
- No follower invents no destination. Dead, detached, inactive or destroyed followers end the return; nearest valid owned followers are selected and ordinary-running targets track their changed positions.
- Stuck and receding-follower episodes end within the allowed budget and back off. Paused scaled time starts no new burst and consumes no active deadline; external Mover pauses are not cleared.
- Pool reuse and late old Attack coroutine Dispose cannot clear new Return protection or motion ownership.
- Whole follower-array lookups stay interval-based for both Return and Attack, rather than happening every active frame.

## Test evolution and limitations

The old no-return baseline produced 2/36 passes; its no-enemy case never issued a position goal. Initial ZCode implementation produced 20/44 passes and directly exposed repeated starts, missing interruption cleanup, overwritten external goals and stale Attack cleanup. These logs are retained separately.

The replacement initially passed 45/48 at SHA256 `7BE60636B31627C8D8752E68105B8FBDA462ED948C893D7F7A3B152774DB924B`. Three harness timing assumptions were then corrected: allow the entire roughly-three-second budget for a 25-unit return, check the moving follower's final station in the ordinary-running phase after the capped burst, and observe the first episode's timeout rather than a sample that can legally fall within a later retry. The implementation worker also updated production between the two runs; the successful result belongs specifically to the `4E69DF...` hash above. Both individual runs had stable before/after source hashes. The test author changed no production implementation.

The implementation worker identified its intervening changes as exception-safety only: lease retirement/owner release moved into Finish's finally, and IsReturning gained a guarded query. No timing or threshold constants were changed to satisfy the three timing corrections. This explanation is the worker's reported change scope; the successful test receipt remains the authoritative tested-source identity.

This is managed behavioral evidence. It does not validate real Unity acceleration/physics, native FSM ordering, IL2CPP/Harmony ABI or method-address safety, native object lifetime, multiplayer behavior, pool callbacks or actual visuals. Operator still must compile against actual E-game interop, verify new native members/hooks and perform controlled in-game validation. In particular, the stubs' simplified movement makes long-distance arrival timing illustrative rather than a measured game result.

Rerun:

```powershell
./run-tests.ps1 -ProductionSource '../samurai-return-fixed/PatchRoles_SamuraiPowerDash.cs' -Tag fixed-final
```
