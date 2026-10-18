# Saved boundary input regressions

The two generators extract current production source into an explicit private output directory. They never write generated files or build outputs in this test directory. Use the bundled .NET 10 SDK for the host projects; the product still builds separately for net6/net8.

`generate.py` extracts the exact placement method prefix through its boundary guard (before Tile enumeration), plus the exact saved-edge read/match helpers. Default `Program.cs` covers saved-valid/cache-zero, no Level cache read, invalid/reversed/float-collapsed bounds, one getter snapshot, primitive copying, exact raw-int drift despite equal floats, exceptions and no late grant. `--source <frozen-a17f-file> --regression-only` uses `RedProgram.cs` and must exit 1 on the original cache-zero regression; the same regression against current source succeeds. The provided baseline must match the independently pinned a17f source; no baseline is fabricated in this directory.

`generate_fences.py` extracts the actual BeforePop/TryBuildContext/AfterPop/AfterLevelLoaded, exact TryRecover prefix before EnsureAll, Host.RecheckScope and production token/scope/marker equality logic. Its tests cover A/B drift and exceptions, scope rejection, post cancellation without restart, consume-before-Ensure, marker short circuit, final activation/commit fence and absence of a Before capture. The only sentinel replaces execution *after* the exact consume prefix; it is not a fake successful transaction.

```sh
python3 generate.py --output /absolute/private/green
python3 generate_fences.py --output /absolute/private/fences
python3 generate.py --source /absolute/frozen/baseline-a17f.cs --regression-only --output /absolute/private/red
python3 check_contract.py --baseline /absolute/frozen/baseline-a17f.cs --report /absolute/private/source-contract.json
```

Build/run each generated Host.csproj in Release with NoDeploy=true, empty BepInExPluginsPath, certificate generation disabled, and private CLI home/NuGet/temp/obj/output paths. The exact commands and environment used for this change are recorded in the task's private `saved-bounds-fix` evidence. `check_contract.py` requires the exact a17f baseline hash, checks the actual wiring, rejects eight intentional source mutations, and pins unchanged occupancy, full scope reader/defaults and every placement step after the outer boundary guard.

Unity/native scope, raw owner, saved World seed, marker/receipt reads and native values are explicit host stubs. These tests prove source behavior and ordering; they do not prove IL2CPP ABI, real save preservation, placement availability or game recovery. The existing policy/transaction suite and 25 occupancy cases are separate regressions.
