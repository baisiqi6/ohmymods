# Managed regression tests

Install the .NET 8 SDK. No game installation, Unity assemblies, Python or PowerShell is required by these projects. Run the commands below sequentially from the repository root:

```sh
dotnet run -c Release --project tests/calendar/CalendarTests.csproj
dotnet run -c Release --project tests/crossbow-defense/Regression.csproj
dotnet run -c Release --project tests/knight-powers/Regression.csproj
dotnet run -c Release --project tests/samurai-retreat/Regression.csproj
dotnet run -c Release --project tests/farm-cats/Regression.csproj
dotnet run -c Release --project tests/fleet-greek-squads/Regression.csproj
dotnet run -c Release --project tests/expedition-follow/Regression.csproj
```

| Project | Coverage |
|---|---|
| `calendar` | Calendar arithmetic and display data |
| `crossbow-defense` | Defensive positions, tower range and ownership cleanup |
| `knight-powers` | Knight combat modifiers and native iterator cleanup |
| `samurai-retreat` | Native defensive retreat speed and ref argument propagation |
| `farm-cats` | Four-cat target, protected/native cats and partial retirement failures |
| `fleet-greek-squads` | Greek fleet reservations, candidate selection and deferred cleanup |
| `expedition-follow` | Dynamic Object following, wall offsets and task ownership |

Production helpers are linked directly from `il2cpp/`. Where a large integration file needs a small compilation shell, the .NET 8 `source-extractor` tool copies the selected methods unchanged during the build. Generated files stay under `obj/`; do not commit them. The extractor uses the invoking .NET host when available. Sequential runs avoid concurrent builds of the shared extraction tool.

`expedition-follow/LegacyDefenseSpacing.cs` intentionally preserves the old fixed-position prefix as a negative control. It reproduces the original bug alongside the current production helper.

The stubs model Unity and native API boundaries for deterministic managed logic tests. Passing them does not establish IL2CPP detour compatibility, actual native callback ordering, rendering, saves, multiplayer or in-game behavior; those require separate runtime validation.
