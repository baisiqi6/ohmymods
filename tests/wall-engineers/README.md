# wall-engineers tests (issue-86, r3)

Direct-linked regression harness for the wall-engineer slice.

- `WallEngineerTests.csproj` compiles the real production files
  `il2cpp/WallEngineerRules.cs`, `il2cpp/WallEngineerRuntime.cs`,
  `il2cpp/PatchWorld_WallEngineers.cs` (explicit `Link=` items; `EnableDefaultCompileItems=false`
  so the repo path and the `actual-interop/` subdirectory cannot leak in) plus `Stubs.cs` (tiny
  actual-named native surface) and `Program.cs`.
- `actual-interop/ActualInterop.csproj` compiles the same three production files against the real
  IL2CPP 2.4 interop assemblies (unique E: `IL2CPP_DEPS`) with shell types in `InteropChecks.cs`.
  `InteropChecks.Run(report)` prints the member-surface report (name strings only).

## Commands (root Operator)

```powershell
C:/Users/ADMIN/dotnet8/dotnet.exe build tests/wall-engineers/WallEngineerTests.csproj -c Debug
C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/wall-engineers/WallEngineerTests.csproj -c Debug
C:/Users/ADMIN/dotnet8/dotnet.exe build tests/wall-engineers/actual-interop/ActualInterop.csproj -c Debug
```

Exit code 0 = `ALL PASS <n> passed, 0 failed`.

## Covered (r5)

All r4 coverage plus: inactive (not destroyed) OnDisable still returns B and clamps; authority-loss
LoadEntered writes nothing and retries the retained cleanup once writable; truly destroyed instance
drops without writes; AddWork registered-then-throw retries via ReassignWork only; border
recalculation retry actually calls the native method again. Stub `Kingdom` models the actual
HashSet membership (`AddWork`/`RemoveWork`/`CheckWork`/`ReassignWork`, pre/post-register throws,
reassign counter).
