# Integrated service regression result

65 passed, 0 failed: all 42 original cases retained, plus 23 movement/Peasant cases. See `results.txt`. The net8 harness builds with 0 warnings and 0 errors; its final run compiles the linked production service directly from `../build/PatchEconomy_AutoRestock.cs`.

Tested source SHA256: `B7128DD3D1929C9C2F1044002B14B024427C413C8B60EE490C1BA86D02C95574`.

## Changes to the harness

- Copied old .cs/.csproj sources into this directory; linked production source remains unmodified by this worker.
- Added real X position movement to coordinator stub using passed scaled delta and speed, actual arrival tolerance, current banker/world/exact lease validation, safe home teleport records, fault/timeout injection, and max-lease measurements.
- Added Time.deltaTime/frame stepping, fifth-role configuration/count storage, IncomingCount, and typed-baker ClassifyShop contract. Ledger stub now also models the existing strict banker-identity spend gate.
- Adapted existing coin/finalization tests to first reach the counter using many actual frames. Their old timing assertions begin at actual arrival, preserving start delay/coin/final delay coverage. The coin cadence test probes 1–2ms either side of thresholds rather than relying on exact float equality after the longer timeline.
- Existing completion tests wait for departure/release where that was their original intent. No immediate-home expectation remains at native purchase.

## New coverage

Teleport to +/-5, variable elapsed deltas at 3.2, multiple frames without coins/debit, actual arrival then start delay, same-side approach/departure and actor Y/Z preservation; pause freezes both phases and timeout; paid walk at 1.6 to +/-4 before home; paid target/price changes; native/selection-cleanup faults without retry/refund; paid orders release budget/incoming reservations but retain assistant/shop slots; same shop blocked during departure; five-role fairness/max2 leases/full budget; invalid lease/world/movement exceptions; eight-second scaled approach/departure timeouts; assistant displacement; shop displacement during both approach and coin phases; exact banker identity after arrival; Peasant live+stock+incoming deficit math, incoming arrival before payment, bread-ready/recruitment/met status, and typed baker classification contract.

## Finding caught and resolved by parent

The added after-arrival banker-rebind regression initially failed: the service's coin-phase preflight checked in-world banker and the coordinator lease but did not compare the supplied banker to the coordinator's current banker. Movement and the real ledger already had strict checks. Parent added IsCurrentRestockBanker(banker) to OrderContextValid; all 65 tests then passed. No production changes were made by this worker during the regression task.

## Reproduce without restore/network

Use `C:/Users/ADMIN/dotnet8/dotnet.exe run --project peasant-bread-hud-20260912/service-tests/AutoRestockTests.csproj --no-restore` from the workspace, with DOTNET_CLI_HOME pointing to the task's `.dotnet-home`, DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1, and DOTNET_CLI_TELEMETRY_OPTOUT=1. Cached old service-test obj asset metadata was copied into this directory's obj; there was no network restore.

Limit: these are service behavior tests against game/coordinator/count/ledger stubs. They do not execute native IL2CPP animation/network synchronization or the production coordinator/count implementation. Parent owns those integration checks and game validation.
