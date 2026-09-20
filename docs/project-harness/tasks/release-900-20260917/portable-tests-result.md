# Hero recruitment public test portability

Completed bounded privacy cleanup for v9. Only public code changes are `tests/hero-recruitment/Program.cs` and `tests/hero-recruitment/HeroRecruitmentTests.csproj`.

- Removed real-user-fixture-only cases `FixtureMigration`, `LatestFixture`, `StagedRecovery`, their calls, and their now-unused `Fixture` reader. Removed all four private `<None ... CopyToOutputDirectory>` fixture references. The absolute local recovery-candidate path and real-save owner/GUID/hash assertions are absent from the public test source.
- Retained all pre-existing synthetic cases, including stable context, fresh runtime date, legacy migration branches, epoch rollback, unknown snapshots, conflicts, authoritative empty states, context ownership/isolation, clock drift, failed load, and new/old reign behavior. Runtime and archive production sources remain linked unchanged.
- Preserved removed private case source and reference fragments as noncompiled text under this task's `private-tests/`; no original fixture/save/config file was edited or copied. **Exclude that whole private directory from the public source whitelist and archive.**

Validation: isolated build output `tests/hero-recruitment/bin/portable-900/` contains only executable/DLL/PDB/deps/runtimeconfig, no copied user fixtures. Build succeeded with 0 warnings / 0 errors, then direct execution passed **223 assertions**. `dotnet run` with overridden `OutputPath` initially attempted the default executable path; that launcher/path mismatch was resolved by invoking the independently built DLL directly, not by restoring private fixtures. The standard command `C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/hero-recruitment/HeroRecruitmentTests.csproj -c Release` also passed **223 assertions** with the final source.

This task did not modify IL2CPP production code, other tests, user data, Git state, publishing or installation. It does not resume or retry the previously rejected Hero/Crossbow diagnostic or gameplay changes.
