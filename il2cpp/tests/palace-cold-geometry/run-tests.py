#!/usr/bin/env python3
"""No-deploy CLR suite: python3 run-tests.py OUTPUT_DIR DOTNET_PATH."""
from pathlib import Path
import hashlib, json, os, subprocess, sys

suite = Path(__file__).resolve().parent
output = Path(sys.argv[1]).resolve()
if output.is_relative_to(suite.parents[2]):
    raise SystemExit('Test output must be outside the product worktree; generated C# must not enter the official glob')
output.mkdir(parents=True, exist_ok=True)
dotnet = str(Path(sys.argv[2]).resolve())
env = os.environ.copy()
env.update(DOTNET_CLI_HOME=str(output / 'dotnet-home'), DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
           DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_CLI_TELEMETRY_OPTOUT='1',
           MSBUILDDISABLENODEREUSE='1')
build = [dotnet, 'build', str(suite / 'PalaceColdGeometry.Tests.csproj'),
         '-p:BepInExPluginsPath=', '-p:GeneratedDir=' + str(output / 'generated'),
         '-p:BaseIntermediateOutputPath=' + str(output / 'obj') + '/',
         '-p:OutputPath=' + str(output / 'bin') + '/']
with (output / 'build.log').open('w') as log:
    build_code = subprocess.run(build, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
run_code = None
if build_code == 0:
    with (output / 'run.log').open('w') as log:
        run_code = subprocess.run([dotnet, str(output / 'bin/PalaceColdGeometry.Tests.dll')],
                                  env=env, stdout=log, stderr=subprocess.STDOUT).returncode
sources = [suite.parents[1] / name for name in [
    'Patch_PalaceClockBootstrap.cs', 'Patch_PalaceCourtArea.cs', 'PalaceColdGeometry.cs',
    'Patch_PalaceLayoutRecipe.cs', 'Patch_PalaceLayoutMetadata.cs']]
receipt = dict(build_exit=build_code, run_exit=run_code, command=build,
               source_sha256={str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources},
               boundary='Actual extracted pre/post/finalizer and CourtArea proof methods run against host native-boundary values; no Unity serialization, native call or game acceptance is claimed')
receipt['test_sha256'] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in suite.iterdir() if p.is_file()}
(output / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
print(json.dumps(dict(build_exit=build_code, run_exit=run_code)))
sys.exit(build_code if build_code else run_code)
