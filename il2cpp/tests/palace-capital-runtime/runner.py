#!/usr/bin/env python3
"""No-deploy CLR suite for the pure Capital400 runtime policy/cache.
Usage: runner.py --output DIR --dotnet PATH --package DIR
All SDK/obj/bin/temp state stays under DIR, outside the product tree.
"""
import argparse
import hashlib
import json
import os
import pathlib
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
parser.add_argument('--dotnet', required=True)
parser.add_argument('--package', required=True)
args = parser.parse_args()

suite = pathlib.Path(__file__).resolve().parent
product = suite.parents[1]
output = pathlib.Path(args.output).resolve()
dotnet = str(pathlib.Path(args.dotnet))
package = pathlib.Path(args.package).resolve()
output.mkdir(parents=True, exist_ok=True)
if product == output or product in output.parents:
    raise SystemExit('Test output must be outside the product source tree.')

env = os.environ.copy()
env.update(
    DOTNET_CLI_HOME=str(output / 'dotnet-home'),
    NUGET_PACKAGES=str(output / 'nuget'),
    TMPDIR=str(output / 'tmp'),
    TMP=str(output / 'tmp'),
    TEMP=str(output / 'tmp'),
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
    DOTNET_GENERATE_ASPNET_CERTIFICATE='false',
    DOTNET_CLI_TELEMETRY_OPTOUT='1',
    MSBUILDDISABLENODEREUSE='1',
)
(output / 'tmp').mkdir(exist_ok=True)
build_command = [
    dotnet, 'build', str(suite / 'PalaceCapitalRuntime.Tests.csproj'),
    '-p:BepInExPluginsPath=',
    '-p:NoDeploy=true',
    '-p:BaseIntermediateOutputPath=' + str(output / 'obj') + '/',
    '-p:OutputPath=' + str(output / 'bin') + '/',
]
with (output / 'build.log').open('w') as log:
    build_exit = subprocess.run(build_command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode

runs = {}
if build_exit == 0:
    for mode in ('matrix', 'warm-fail', 'warm-ok', 'main'):
        with (output / ('run-' + mode + '.log')).open('w') as log:
            runs[mode] = subprocess.run(
                [dotnet, str(output / 'bin' / 'PalaceCapitalRuntime.Tests.dll'), str(package), mode],
                env=env, stdout=log, stderr=subprocess.STDOUT,
            ).returncode

scene_build_command = [
    dotnet, 'build', str(suite / 'PalaceCapitalRuntimeScene.Stub.csproj'),
    '-p:BepInExPluginsPath=',
    '-p:NoDeploy=true',
    '-p:BaseIntermediateOutputPath=' + str(output / 'scene-obj') + '/',
    '-p:OutputPath=' + str(output / 'scene-bin') + '/',
]
with (output / 'scene-build.log').open('w') as log:
    scene_build_exit = subprocess.run(scene_build_command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
if scene_build_exit == 0:
    for mode in ('success', 'unit-zero', 'camera', 'camera-forward', 'camera-clip', 'camera-near',
                 'ref-inactive', 'ref-disabled', 'ref-route', 'ref-parent',
                 'level-replaced', 'world-replaced', 'campaign-replaced',
                 'outer-success', 'outer-deactivate', 'outer-destroy', 'outer-already',
                 'rollback', 'retain'):
        with (output / ('run-scene-' + mode + '.log')).open('w') as log:
            runs['scene-' + mode] = subprocess.run(
                [dotnet, str(output / 'scene-bin' / 'PalaceCapitalRuntimeScene.Stub.dll'), str(package), mode],
                env=env, stdout=log, stderr=subprocess.STDOUT,
            ).returncode

sources = [
    product / 'Patch_PalaceCourtVisuals.cs',
    product / 'Patch_PalaceDestinationSupport.cs',
    product / 'PalaceAssets.props',
    product / 'Patch_PalaceCapitalRuntime.cs',
    product / 'Patch_PalaceCapitalPolicy.cs',
    product / 'Patch_PalaceCapitalResources.cs',
    product / 'Patch_PalaceProductionSource.cs',
    product / 'Patch_PalaceActorRegistry.cs',
    product / 'Patch_PalaceLayoutRecipe.cs',
    product / 'Patch_PalaceScrollGeometry.cs',
    product / 'PalaceResourceContract.cs',
    product / 'PalaceResourceHashGaps.cs',
    suite / 'Program.cs.txt',
    suite / 'SceneProgram.cs.txt',
    suite / 'Host.cs.txt',
    suite / 'PalaceCapitalRuntime.Tests.csproj',
    suite / 'PalaceCapitalRuntimeScene.Stub.csproj',
    suite / 'runner.py',
]
receipt = {
    'build_exit': build_exit,
    'scene_build_exit': scene_build_exit,
    'run_exits': runs,
    'command': build_command,
    'scene_command': scene_build_command,
    'package': str(package),
    'source_sha256': {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in sources if path.exists()},
    'boundary': 'Pure BCL policy/cache suite over the actual frozen Capital400 R12 package. '
                'The scene suite compiles the real runtime/policy/scene files against targeted Unity/owner stubs '
                '(Host.cs.txt) to exercise identity validation, menu freeze, drift cleanup and retention order. '
                'It is NOT Unity/Native/visual/screen evidence and the stubbed sort/store/camera do not prove Native behaviour.',
}
(output / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
ok = (build_exit == 0 and scene_build_exit == 0 and len(runs) == 23
      and all(code == 0 for code in runs.values()))
print(json.dumps({'ok': ok, 'build_exit': build_exit, 'scene_build_exit': scene_build_exit,
                  'run_exits': runs}, indent=2))
raise SystemExit(0 if ok else 1)
