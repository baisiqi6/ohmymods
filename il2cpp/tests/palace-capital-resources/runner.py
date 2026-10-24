#!/usr/bin/env python3
"""No-deploy CLR suite for the bounded Capital400 R12 resource package loader.
Usage: runner.py --output DIR --dotnet PATH --package DIR
All SDK home, obj, bin and temp state stay under DIR, outside the product tree.
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
parser.add_argument('--package', required=True)
args = parser.parse_args()

suite = pathlib.Path(__file__).resolve().parent
product = suite.parents[1]
output = pathlib.Path(args.output).resolve()
dotnet = str(pathlib.Path(args.dotnet).resolve())
package = pathlib.Path(args.package).resolve()
props = product / 'PalaceCapitalAssets.props'
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
    dotnet, 'build', str(suite / 'PalaceCapitalResources.Tests.csproj'),
    '-p:BepInExPluginsPath=',
    '-p:NoDeploy=true',
    '-p:BaseIntermediateOutputPath=' + str(output / 'obj') + '/',
    '-p:OutputPath=' + str(output / 'bin') + '/',
]
with (output / 'build.log').open('w') as log:
    build_exit = subprocess.run(build_command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode

run_exit = None
if build_exit == 0:
    with (output / 'run.log').open('w') as log:
        run_exit = subprocess.run(
            [dotnet, str(output / 'bin' / 'PalaceCapitalResources.Tests.dll'), str(package), str(props)],
            env=env, stdout=log, stderr=subprocess.STDOUT,
        ).returncode

sources = [
    product / 'Patch_PalaceCapitalResources.cs',
    product / 'PalaceCapitalAssets.props',
    suite / 'Program.cs.txt',
    suite / 'PalaceCapitalResources.Tests.csproj',
    suite / 'runner.py',
]
package_files = {}
for path in sorted(package.rglob('*')):
    if path.is_file():
        package_files[path.relative_to(package).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
receipt = {
    'build_exit': build_exit,
    'run_exit': run_exit,
    'command': build_command,
    'package': str(package),
    'package_file_count': len(package_files),
    'package_files': package_files,
    'source_sha256': {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in sources if path.exists()},
    'boundary': 'Pure BCL loader over the actual frozen Capital400 R12 package directory. '
                'No PNG decode, texture, Unity, Harmony, Native, runtime IO, static cache, '
                'fee gate, save, install or deployment authority.',
}
(output / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
print(json.dumps({'build_exit': build_exit, 'run_exit': run_exit}))
sys.exit(build_exit if build_exit else (run_exit if run_exit is not None else 1))
