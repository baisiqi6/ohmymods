# Palace destination admission host regression

Runs the current `PalaceDestinationAdmission.NoteDestinationPrepare` and
`TryDescribeDestination` method bodies verbatim against controlled host boundaries. The build-time
extractor reads `il2cpp/Patch_PalaceDestinationSupport.cs`, copies the cache-field block and both
methods byte-for-byte into `obj/<Configuration>/<TargetFramework>/generated/`, and writes
`extraction.json` with the source SHA-256 and each extracted body SHA-256/length.

The extractor reads the production file **once** as raw bytes, strictly decodes that same in-memory
snapshot, and derives the cache fields, both method texts and every digest from it. It emits immutable
`PalaceAdmissionSourceIdentity` constants (full source including comments, field block, each method)
into the generated compilation shell. The runner compares three identities before `RunAll`: its
current-source recomputation, the disk `extraction.json`, and the constants actually compiled into
the runner. A mismatch between any of them is a hard failure (exit 2), never a warning.

Disk `generated` text is not accepted as proof of the executed IL. If the source or metadata is
regenerated without rebuilding the runner, the old runner's compiled constants reject it with exit 2.
Generated files stay under `obj/` and are not committed.

## Dependencies and commands

Requires only the .NET 8 SDK (target `net8.0`). On a host that has a newer SDK/runtime but no
`Microsoft.NETCore.App` 8.x runtime, export `DOTNET_ROLL_FORWARD=Major` before the commands below;
the project still compiles against the .NET 8 reference assemblies. Run sequentially from the
repository root:

```sh
dotnet build -c Release tests/palace-destination-admission/PalaceDestinationAdmission.Tests.csproj \
  -p:UseSharedCompilation=false -nodeReuse:false
dotnet run -c Release --no-build \
  --project tests/palace-destination-admission/PalaceDestinationAdmission.Tests.csproj
```

The project sets `UseSharedCompilation=false` and `NodeReuse=false`; the documented build switches
make the same policy explicit. A normal `dotnet build` of this project only writes its own `obj/` and
`bin/`; it never builds or writes the BepInEx plugin package. The extractor has its own small
`Extractor/` project and is invoked by the `ExtractAdmissionMethods` target before compilation.

`PASS` lines and the final JSON (case/assertion counts, source and method hashes, every result)
are the machine-readable evidence. Exit 0 means all cases passed; exit 1 means a case failed after a
matching compiled identity; exit 2 means current source, metadata and compiled-runner identity did
not all agree before any case ran.

## Boundaries

- Exact production text: `NoteDestinationPrepare` and `TryDescribeDestination` are extracted, not
  mirrored. The cache-field block (including the `_sidecarEdgesMin/Max = 0` reset lines in the method)
  is pinned by hash as well.
- Controlled plain stubs: `GlobalSaveData`/`Managers`/`Game` identity pointers, sidecar-read result
  and reason, semantic-owner result, campaign/island containers, and the saved-structure facts.
  No Unity, BepInEx, native call, real `ReadSidecar` file IO, save write, payment or config is used.
- The structural boundary is stubbed, so endpoint/Beach/Tile cases prove the gates inside the
  extracted `TryDescribeDestination`, not the real structure parser.
- This is not proof of Unity callback ordering, native first failure, Harmony installation, the
  complete `CanPay` path, payment/save/return acceptance or H3. It only proves the local H4
  classification contract for the extracted method chain.
