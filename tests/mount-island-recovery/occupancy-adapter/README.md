# Occupancy adapter regression

`generate.py --output <private-output-directory>` extracts the **actual** current
`CollectOccupancy`, `TryCollectBounds`, `TryCollectDirectColliderBounds`, interval/
placement structures and placement policy. The older methods come from the existing
`scope-adapter/baseline/Patch_MountIslandRecovery.0919.cs`; they are not duplicated
in this adapter. `extraction.json` records method hashes and exact equality checks.
Production Geometry is compiled directly. Generated sources and build artifacts
belong in the caller's private output directory, never this source directory.

Run with a .NET SDK and net8 runtime (or `DOTNET_ROLL_FORWARD=Major`):

```sh
python3 generate.py --output /absolute/private/occupancy-output
dotnet run --project /absolute/private/occupancy-output/Host.csproj -c Release -- --old
dotnet run --project /absolute/private/occupancy-output/Host.csproj -c Release
```

The old run must exit 1 on the regression expectations, including the large
unmarked decorative parent and the gap between distant marked children. Current
must exit 0. Tests also cover solid leaves and descendants, individual direct
colliders, trigger/inactive/disabled colliders, LevelBlock recursion, unchanged
marked/Payable behavior, native tile/structure path skips, conservative support
Ground, and fail-closed unavailable/invalid direct collider data.

`Stubs.cs` models only Unity/native component queries, active state, world bounds
and Level overlap. It does not model Unity object lifetime, physics, scene
membership, IL2CPP marshalling, or game execution. These host tests establish the
source predicate behavior; they do not establish that a particular save reaches
occupancy or that recovery succeeds in a real game.
