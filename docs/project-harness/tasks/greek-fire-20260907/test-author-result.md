# Greek Fire independent managed regression

## Result

**PASS: 70 tests, 0 failures** for the Operator candidate:

- Source: `../greek-fire-build/PatchRoles_GreekFire.cs`
- SHA256 before and after the compiled run: `BF7254FDB0C51C031A9BE77D3B0FB70180240B342843864988590146551E1AAD`
- Evidence: `operator-first.log`, `operator-first-receipt.json`
- Reproduce: `powershell -File ./run.ps1 -RunName verification` (PowerShell 7 also supported).

The project compiles the **actual candidate file through MSBuild Compile Link**, not a copied implementation of its decision logic. Generic managed fixtures provide Unity object identities, object/component lifetimes, scaled Time, the enemy scanner, shared archer cache, original buff storage/cloning, Buffable expiry bookkeeping, receiver callbacks, RPC readiness fields, and fire projectile pool availability. Harmony attributes discover the actual Update and OnDisable hooks. Tests accept either a prefix or postfix lifecycle hook and reset production static fields to their original initialized values between isolated cases; within each case, cooldown/lifecycle state is untouched by the test.

## Coverage

The 67 Greek skill cases cover:

- Native knight and current-follower activation, 8-second expiry data and 15-second cooldown, ordinary guarding/charging/formation state changes.
- No actual enemy, wrong style, config off, missing authority, dead/inactive/inert/grabbed/harmless/embarked owners, and pause.
- Valid ranged follower targets, actual arrow range, dead/inactive/wildlife/ignored-invulnerable enemies, stale distant knight scanner targets, and the correct Knight versus Arrow damage source.
- Native pointer ownership across distinct managed wrappers, exclusion of foreign/dead/inactive followers, no invented squad radius, and newcomers waiting until the next cast.
- Preservation of longer/equal FireAttacks, extending shorter native effects without directly repeating the receiver callback, registered/applicable Buffable and RPC header/start/end readiness (offline and online), missing/unsynchronized fire pools.
- Clone caching, unchanged original duration/ID/type/gradient, unchanged buff storage, missing assets and transient asset-load exceptions retrying at five seconds without consuming the ordinary cooldown.
- Cooldown retention across config/style/authority transitions, native-callback reentry, departures/joining during the cast, owner disable/authority loss during callbacks, partial native failure after a successful recipient, and late cleanup for an old pooled pointer not removing the replacement cooldown.
- Old crossbow package restoration before native follower activation and only for eligible owned recipients, shared cache remaining at its default three seconds, bounded polling, and no squad scans during cooldown.

Three further cases compile `ApplySquadCrossbowPackage` **verbatim** from the actual Crossbow candidate. They verify that an active FireAttacks entry or a currently selected fire-arrow asset causes no crossbow/interval/scaling mutation, and normal post-expiry application doubles the interval once and remains idempotent. Only surrounding asset initialization, restore recording and scale registration are fixtures.

- Crossbow source SHA256: `4C8DB21A414CBA9A92126CEDAF0B20A6EDA8576AFE8DD043C49A6CEAF2DDBD68`
- Exact extracted 59-line method SHA256: `314320E0D361495E219394DC847DBA0460FC8C9427BBB531954CFE4762EBF8B8`
- Extraction evidence: `crossbow-extraction.json`; regenerate with `python extract_crossbow.py` if that source changes.

## Negative control

The completed ZCode candidate `../greek-fire-worker/PatchRoles_GreekFire.cs`, SHA256 `0C6EDB34D17FCD93106995D474A64B7FFA84F48607029B64768F33ED0A7C25AF`, produced **56 passes / 14 failures** under the 70-case suite (`worker-red.log`, `worker-red-receipt.json`). Failures independently reproduced missing authority/pause/target guards, permanent disable after transient load failure, callback ownership/lifecycle problems, repeated partial casts, stale-owner cooldown removal, wrong damage-source checking, and omitted crossbow restoration. The Operator candidate passes those unchanged behavior assertions.

An earlier development run of a still-changing worker file produced 39 passes / 3 failures from the initial 42 cases. It was not frozen with a source hash and is **not acceptance evidence**. `first-run.log` is a later incomplete fixture compilation attempt against the changing worker, also not an acceptance result.

## Boundaries

These tests verify managed control flow and the values/delegation handed to native Buffable; they do not prove the actual IL2CPP 2.4 coroutine wakes at exactly eight seconds, the cloned resource visually matches the anvil, the native fire-arrow pool works on both peers, or RPC visuals/end messages arrive correctly. Operator still owns actual interop build/hook checks, integration and controlled host/client runtime acceptance.

The test author changed only this test directory. No canonical source, worker source, game, configuration, save, deployment or Git state was modified.
