# Shared bank regression fixtures

Run each project with .NET 8. All production Compile inputs reference this checkout. The economic suite is in tests/coin-courier-economy and the state suite in tests/shared-bank-state (net6; a net8-only developer machine can use DOTNET_ROLL_FORWARD=Major).

- capture/Capture.csproj: first-save preparation, temporary account read recovery, lifecycle changes and preservation of real capture faults.
- recovery/Recovery.csproj: existing shared balance with enhancement OFF/foreign biome, root ID, prime and callback failures.
- hotpath/HotPath.csproj: bounded current-account reads with a 64-account catalog.

These tests use native/Unity substitutes. They do not establish in-game Harmony invocation, disk IO, platform or multiplayer acceptance.
