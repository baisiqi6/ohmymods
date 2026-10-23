# Palace layout recipe tests

Compiles `Patch_PalaceLayoutRecipe.cs` directly; no Unity, packages, IO or game runtime. Tests both immutable known identities, complete tile grids, pixel/world anchors, strict resolution and invalid inputs.

```sh
dotnet run --project tests/palace-layout-recipe/PalaceLayoutRecipe.Tests.csproj -c Release
```

For isolated output, build with `-o <private>/bin -p:BaseIntermediateOutputPath=<private>/obj/ -p:MSBuildProjectExtensionsPath=<private>/obj/`, then execute that output DLL. Project has no deployment target.
