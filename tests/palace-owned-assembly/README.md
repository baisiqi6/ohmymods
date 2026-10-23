# Palace owned assembly host tests

Directly compiles the full production `PalaceLayoutRecipe`, `PalaceDedicatedOwnedBlock`, `PalaceDedicatedDraftProducer` and `PalaceDedicatedEndpoints` sources. The host models GameObject/component hierarchy, cloning and source/owned object identities; it does not replace production assembly, validation, scatter clearing or endpoint algorithms.

```sh
dotnet run --project tests/palace-owned-assembly/PalaceOwnedAssembly.Tests.csproj -c Release
```

For isolated output, build with `-o <private>/bin -p:BaseIntermediateOutputPath=<private>/obj/ -p:MSBuildProjectExtensionsPath=<private>/obj/`, then execute that output DLL. No deployment target.

Coverage includes old44 and new400 assembly, exact source-derived25/114 output children, an alternate approved source receipt yielding115, unchanged32-child/256-node/depth8 source guards, nested33-child rejection,101tiles/extra known/unknown roots, all six spawn roots, canonical shores, shared provenance/alias/membership gates, source snapshots and retained partial handles on injected failures. Source snapshot collection in tests is deliberately independent of production source-eligibility budgets so malformed input can still be compared before/after.

`Build` uses reflection only to allow the exact same behavioral fixture to call the previous pre-recipe signature for baseline RED evidence. It supplies no alternate geometry implementation.

This is a CLR host test. Stub `ComputeDimensions` reads the modelled Tile extents; passing does not prove native BuildIndex/ComputeDimensions, CloneInto, UID allocation, object pools, detour safety, actual collider generation, save/load, payment or gameplay. Those remain separate integration/Native evidence.
