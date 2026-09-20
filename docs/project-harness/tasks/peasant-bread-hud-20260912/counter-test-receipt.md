# Counter regression receipt

The linked integrated `../build/AutoRestockCounts.cs` compiled with **0 warnings and 0 errors** and passed **81/81 cases**: the existing 51 regression cases plus 30 bread/peasant cases. Exact case names and outcomes are in `results.txt`; build output is in `build-results.txt`.

The old `_placedShops` fixtures now explicitly adapt assignments into the registered `shops` list; production uses only `shops`. Bread-specific fixtures add bakeries directly to `shops` while asserting that `_placedShops` remains empty. The former unknown-peasant test now exercises an unknown Beggar, preserving its original unknown-profession pending-drop contract after Peasant became a supported profession. Default legacy fixture masks keep Peasant disabled; new cases opt in explicitly.

New coverage includes actual Peasant/appearance-only variant versus employed and Beggar components, typed bakery identity and itemPrefab requirement, bakery discovery/dedup/multiple stock totals, current-layer/scene/active filtering, scoped AddShop/RemoveShop hooks and subscription removal, successful/false/duplicate/foreign-context bread hooks, stock-to-incoming accounting, same-frame grace, arbitrary-time native eating with no roster/stock scans, state/death/remove/layer/activity/identity retirement, settings/recovery reconstruction, unavailable-cache gating, world/reset clearing, and native promotion replacing incoming with live Peasant.

Tests invoke actual production Refresh/getters/classifier and hook methods. Stubs supply the native input surface, stock/event changes and identity/state fields; they do not compute the derived live/stock/incoming counters. They cannot prove in-game native timing or Harmony dispatch, which remain part of the parent's native audit and runtime acceptance.

No production defect was found by these cases. No production source, canonical source, deployment or game files were edited during this validation task.

Commands used with `DOTNET_CLI_HOME=<task>/.dotnet-home` and `DOTNET_CLI_TELEMETRY_OPTOUT=1`:

```powershell
C:/Users/ADMIN/dotnet8/dotnet.exe restore <task>/count-tests/CountTests.csproj --source <task>/count-tests
C:/Users/ADMIN/dotnet8/dotnet.exe build <task>/count-tests/CountTests.csproj --no-restore -v minimal
C:/Users/ADMIN/dotnet8/dotnet.exe run --project <task>/count-tests/CountTests.csproj --no-build --no-restore
```

Restore used the local test directory as its only package source and succeeded without network access or copied legacy obj metadata.
