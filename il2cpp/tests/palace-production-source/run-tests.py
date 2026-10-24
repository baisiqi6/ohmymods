#!/usr/bin/env python3
"""No-deploy CLR suite for the production source reader.
Usage: run-tests.py --output DIR --dotnet PATH --registry JSON --layout JSON
Build output and all SDK/temp state stay under DIR, outside the product tree.
"""
import argparse
import hashlib
import json
import os
import pathlib
import subprocess
import sys

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
parser.add_argument('--dotnet', required=True)
parser.add_argument('--registry', required=True)
parser.add_argument('--layout', required=True)
args = parser.parse_args()

suite = pathlib.Path(__file__).resolve().parent
product = suite.parents[1]
output = pathlib.Path(args.output).resolve()
dotnet = str(pathlib.Path(args.dotnet).resolve())
registry = pathlib.Path(args.registry).resolve()
layout = pathlib.Path(args.layout).resolve()
output.mkdir(parents=True, exist_ok=True)
if product == output or product in output.parents:
    raise SystemExit('Test output must be outside the product source tree.')

env = os.environ.copy()
env.update(
    DOTNET_CLI_HOME=str(output / 'dotnet-home'),
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
    dotnet, 'build', str(suite / 'PalaceProductionSource.Tests.csproj'),
    '-p:BepInExPluginsPath=',
    '-p:BaseIntermediateOutputPath=' + str(output / 'obj') + '/',
    '-p:OutputPath=' + str(output / 'bin') + '/',
]
with (output / 'build.log').open('w') as log:
    build_exit = subprocess.run(build_command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode

run_exit = None
if build_exit == 0:
    with (output / 'run.log').open('w') as log:
        run_exit = subprocess.run(
            [dotnet, str(output / 'bin' / 'PalaceProductionSource.Tests.dll'), str(registry), str(layout)],
            env=env, stdout=log, stderr=subprocess.STDOUT,
        ).returncode

sources = [
    product / 'Patch_PalaceActorRegistry.cs',
    product / 'Patch_PalaceProductionSource.cs',
    suite / 'Program.cs.txt',
    suite / 'PalaceProductionSource.Tests.csproj',
    suite / 'run-tests.py',
]
receipt = {
    'build_exit': build_exit,
    'run_exit': run_exit,
    'command': build_command,
    'registry': str(registry),
    'registry_sha256': hashlib.sha256(registry.read_bytes()).hexdigest(),
    'layout': str(layout),
    'layout_sha256': hashlib.sha256(layout.read_bytes()).hexdigest(),
    'source_sha256': {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in sources},
    'boundary': 'Pure CLR source reader over two explicit frozen-r1 JSON files; no PNG IO, texture, loader, '
                'Harmony, Native, sorting, fee or promotion authority. Test mutations are synthetic; the actual '
                'frozen-r1 candidate is passed by path and not embedded in source.',
}
(output / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
print(json.dumps({'build_exit': build_exit, 'run_exit': run_exit}))
sys.exit(build_exit if build_exit else (run_exit if run_exit is not None else 1))
