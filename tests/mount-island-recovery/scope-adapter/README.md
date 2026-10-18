# Recovery scope adapter

Requires .NET 8 and `python3` on PATH. Run from the repository root:

```sh
dotnet run --project tests/mount-island-recovery/scope-adapter/ScopeAdapter.csproj -c Release
```

Each build regenerates `ExtractedScope.cs` from the current production scope reader and the actual scope comparison in `Patch_MountIslandRecoveryPlan.cs`. The generated file is not tracked. The frozen `0919` source supplies the original failing reader; it is test input and is not compiled as a game patch.

The tests cover cache access before and after native initialization, default and invalid phases, two stable pre-pop reads, identity changes, getter evaluation order and checked call sites. They use host stubs and do not exercise the native ABI or launch the game.
