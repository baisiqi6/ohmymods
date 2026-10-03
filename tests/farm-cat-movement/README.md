# Farm cat movement regression host

This offline host links the complete production `FarmCatMovement.cs`, its Cat lifecycle/update callbacks, and `PatchWorld_FarmCats.cs`. It does not load Unity or call IL2CPP native APIs. The native order model follows the separately bound 2.4 Cat.Update → FSM.Update → StepCoroutine and same-state Reset/Start evidence; the geometry, ownership, cooldown, and displacement decisions are the actual production adapter.

The suite covers legal interval unions with gaps, intact wall margins, birth deferral, dynamic coin targets, four game seconds of accumulated absolute displacement, nonzero commanded body velocity while blocked, real walking/back-and-forth progress, pause/save/authority/life/world/PC exclusions, native cleanup/queue/Stop ordering without false Catch, cooldown getters/setters/partial commit/later native values, and synchronous scope/life/goal changes at commit boundaries. No source PC, position, body velocity, constraints, animation, or coin amount is written by the adapter.

`tests/farm-cats` retains its original 22 stocking/retirement/scale assertions. Its narrow birth stub deliberately isolates those old concerns; this suite exercises real production birth geometry. The original Program.cs assertions are unchanged.

Build using the existing SDK with `-p:BepInExPluginsPath=` and explicit BaseIntermediateOutputPath/OutputPath under the task evidence directory. Set DOTNET_GENERATE_ASPNET_CERTIFICATE=false and DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1. Run the saved Tests.dll with the existing .NET8 runtime. Actual public API compatibility is checked separately by the full .NET6 plugin build; `C-implementation/harmony-host` checks the byte-identical installed HarmonyX `__runOriginal` parameter on an independent .NET6 managed target.

These are deterministic offline regressions, not gameplay observations or a proof that every physical obstruction is fixed. A valid activity point does not prove path reachability; missing/unknown native ownership or geometry gives no native writes. Ambiguous partial cleanup/cooldown commits remain faulted until the current enable life ends.
