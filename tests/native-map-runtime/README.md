# Source-linked map host tests

`VariantProbe.csproj` compiles the actual map controller, art service, layout and catalogs against host-only Unity/IL2CPP adapters. It exercises cold B, distinct A/B overview/detail pairs, A→B→A, retained context data, readable complete pagination, real cloned pager button events, failed pair invalidation, and partial allocation failure. These are host contracts, not native IL2CPP or player acceptance.

```sh
DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_ROLL_FORWARD=Major dotnet run --project tests/native-map-runtime/VariantProbe.csproj -p:BepInExPluginsPath=
```

To exercise a physically missing B manifest resource, build the same project with `-p:IncludeCrescentResource=false -p:AssemblyName=VariantMissingB` into a separate output/intermediate directory and run its DLL with `--missing-b`. It must reject B while preserving a live A lease. `--callback-only` isolates the matching-fingerprint failure regression; it explicitly calls `TryReadLand` before consuming buffered `BuildRequests`.

Catalog pressure requests use current source membership/custom dimensions plus the historical `native-map-icons/fixtures/icon-rects.json` for native icon dimensions. Sixteen requests are a legacy pressure envelope; this is not a capture of the player's native array. No tests claim seasonal grants, current blockers, native clone/font rendering, or page retention after a later data refresh.

`RuntimeProbe.csproj` and its original S1–S15 scenarios predate the two-island overview/page policy. Necessary API adapters restore compilation, but the old suite is **not green** and still asserts world-wide 16-resource display and obsolete private field names. Do not report it as passed. The task record preserves a fresh-process baseline/current scenario matrix; private observation adapters confirm S11's 22 checks and S12's 15 checks with all nine live owners accounted for. The production implementation is never changed to satisfy obsolete observations.
