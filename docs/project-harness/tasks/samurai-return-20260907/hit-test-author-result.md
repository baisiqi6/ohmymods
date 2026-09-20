# Independent forward/return burst damage regression

**76 passed, 0 failed** against `../samurai-return-damage-worker/PatchRoles_SamuraiPowerDash.cs`.

The stable tested production SHA256 is:

`48CF5C5859868ECC2C4403F9948B462B865D4DB2BD7D9A605FD9CB3184AB87AA`

See `damage-final-results.txt` and `damage-final-receipt.json`. Before/after production hashes match; the receipt also records test-program and stub hashes. `damage-first-results.txt` records an earlier 74/74 run against a different candidate hash, before the two explicit same-frame multi-target cases were added. It is candidate evidence, not a no-damage baseline.

## New user contract

The user changed return from non-damaging movement to normal damaging burst movement. The original `Program.cs`, original 48-case evidence and previous RESULT are retained as historical evidence. `DamageProgram.cs` retains those lifecycle/movement/cache scenarios, updating only their obsolete no-return-damage assumptions, and adds burst-specific cases.

Production is compiled via Compile link; tests invoke the actual hooks, Tick and retained Attack coroutine. No production implementation was changed by the test author.

## Verified outcomes

- Both forward and return entry frames damage **two distinct valid enemies on that same frame**; the change does not limit a burst to one enemy per frame.
- Damage amount equals the knight's `_attackDamage`, uses its game object as attacker and `DamageSource.Knight`, and both directions pass radius **1.2** to the shared physics-window interface.
- Multiple colliders for the same Damageable and repeat frames produce one hit per motion. Immune targets are filtered. A later independent return can hit a target that the earlier forward dash already hit.
- New targets supplied by the physics environment during an active burst can be hit; each motion reuses one collider buffer across those frames.
- A return can hit on its initial frame and on a still-valid arrival frame at distance 4. Once the return ends, a new target gets no late damage.
- No new damage occurs once the .6-second timeout or seven-unit travel boundary is reached. Paused bursts neither scan nor damage. Ordinary `_runSpeed` tail movement performs no hit scans or damage.
- For both forward and return: the first target's synchronous ReceiveDamage callback can invoke OnDisable, replace the Mover goal, remove authority, enter manual control or disable the config. The old frame stops before the second target and does not resume damaging it next tick.
- For both directions: ReceiveDamage can retire the old motion and start a new return. The old caller preserves the replacement goal, invulnerability, trail and active-return ownership.
- A synchronous Mover.Stop callback can retire the old motion and start a replacement within the same ActorState. The old Finish finally does not clear the replacement.
- The earlier threshold, hysteresis, movement bounds, finite retry, lifecycle, pause, follower invalidation, external ownership, pool-reuse and interval-based cache cases still pass under the new contract.

## Environment and limits

The shared stubs gained only environment instrumentation: ReceiveDamage callbacks/attribution, Mover.Stop callbacks, radius/buffer recording. They implement no production hit eligibility or dedup algorithm. Physics supplies an explicit collider list, so the tests verify which radius is requested and when damage is allowed, not real Unity collider geometry or performance. The callbacks execute synchronously to exercise actual production reentrancy paths.

The actual source was also read to confirm forward and return both call `HitScan(MotionLease)`; black-box results alone cannot prove that the implementation shares a helper.

No game/native IL2CPP code, configuration/save mutation, deployment, Git mutation or installation occurred. Actual interop compatibility, Harmony/native registration, game physics, multiplayer and in-game appearance still require operator validation.

Reproduce:

```powershell
./run-tests.ps1 -ProductionSource '../samurai-return-damage-worker/PatchRoles_SamuraiPowerDash.cs' -TestProgram DamageProgram.cs -Tag damage-final-rerun
```
