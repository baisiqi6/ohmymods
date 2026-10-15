# Heavy shield distance clock regression

Pure .NET 8 harness linking the production `il2cpp/Patch_HeavyShieldGait.cs` directly. Run from the repository root:

```sh
dotnet run --project tests/heavy-shield-gait/HeavyShieldGaitTests.csproj -c Release -p:BepInExPluginsPath=
```

Covers signed forward/reverse distance, blocked roots, equivalent distance at 30/60/120 sampling rates, six/eight-frame mapping, parent/facing changes, duplicate engine frames, pause/reentry/event fences, invalid numbers/strides, gap/jump boundaries, tiny progress and long wrapping. A warmed 20,000-iteration helper-only loop checks managed allocation; this does not benchmark Unity/interop or the whole visual pipeline.

The companion source-linked runtime suite moves the actual fixture carrier Transform before `HeavyShieldActorVisuals.Tick`, including repeated `HeavyShieldRuntime.Tick` presentation calls. The fixture models translation and signed/non-unit parent scales, but not rotation. Native platform behavior and player visual acceptance remain separate.

Visual cadence references: Walk 30, Run 48 and Advance 24 detail pixels/cycle, at 320/7 pixels per world unit. The selected-B art is preserved; these values are not measured source strides or a claim of exact planted-foot locking. Native Run/Advance calibration remains unverified. Character movement speed is unchanged; event/attack/idle clocks remain measured in seconds.
