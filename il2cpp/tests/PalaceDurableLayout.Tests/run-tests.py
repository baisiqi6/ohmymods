"""No-deploy CLR suite. Usage: python3 run-tests.py OUTPUT_DIR DOTNET_PATH."""
from pathlib import Path
import hashlib, json, os, subprocess, sys
suite = Path(__file__).resolve().parent
output = Path(sys.argv[1]).resolve()
output.mkdir(parents=True, exist_ok=True)
dotnet = str(Path(sys.argv[2]).resolve())
env = os.environ.copy()
env.update(DOTNET_CLI_HOME=str(output / 'dotnet-home'), DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_CLI_TELEMETRY_OPTOUT='1', MSBUILDDISABLENODEREUSE='1')
build = [dotnet, 'build', str(suite / 'PalaceDurableLayout.Tests.csproj'), '-p:BepInExPluginsPath=', '-p:BaseIntermediateOutputPath=' + str(output / 'obj') + '/', '-p:OutputPath=' + str(output / 'bin') + '/']
with (output / 'build.log').open('w') as log:
    build_code = subprocess.run(build, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
run_code = None
if build_code == 0:
    with (output / 'run.log').open('w') as log:
        run_code = subprocess.run([dotnet, str(output / 'bin/PalaceDurableLayout.Tests.dll')], env=env, stdout=log, stderr=subprocess.STDOUT).returncode
sources = [suite.parent.parent / name for name in ['Patch_PalacePaidJournal.cs', 'Patch_PalaceTripContext.cs', 'Patch_PalaceLayoutRecipe.cs', 'Patch_PalaceClockReceipt.cs', 'Patch_PalaceReturnAnchor.cs']]
receipt = dict(build_exit=build_code, run_exit=run_code, command=build, source_sha256={str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}, boundary='CLR codecs/state + owned temporary journal files only; native host methods throw and are not exercised')
(output / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
print(json.dumps(dict(build_exit=build_code, run_exit=run_code)))
sys.exit(build_code if build_code else run_code)
