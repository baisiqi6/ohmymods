#!/usr/bin/env python3
"""Freeze the product source, clone the approved net8 interop wrapper, and compile only.
Usage: build-interop.py --output DIR --dotnet PATH [--wrapper PATH]
No game launch, install, deploy, profile or save access.
"""
import argparse
import hashlib
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
parser.add_argument('--dotnet', required=True)
parser.add_argument('--wrapper', default='/Users/Admin/projects/ohmymods/.local/tasks/palace400-runtime-20261023/phase-c/build/Latest-net8.0.csproj')
args = parser.parse_args()

suite = pathlib.Path(__file__).resolve().parent
product = suite.parents[1]
output = pathlib.Path(args.output).resolve()
dotnet = str(pathlib.Path(args.dotnet).resolve())
wrapper = pathlib.Path(args.wrapper).resolve()
frozen = output / 'frozen-source'
support = output / 'support'
output.mkdir(parents=True, exist_ok=True)
if product == output or product in output.parents:
    raise SystemExit('Build output must be outside the product source tree.')
if not wrapper.is_file():
    raise SystemExit('Approved wrapper is missing: ' + str(wrapper))

if frozen.exists():
    shutil.rmtree(frozen)
frozen.mkdir(parents=True)
support.mkdir(parents=True,exist_ok=True)
for path in sorted(product.iterdir()):
    if path.is_file() and (path.suffix == '.cs' or path.suffix == '.props'):
        shutil.copy2(path, frozen / path.name)
for directory in ('Assets', 'PalaceConfig'):
    source = product / directory
    if source.is_dir():
        shutil.copytree(source, frozen / directory)
plugin_info = pathlib.Path('/Users/Admin/projects/ohmymods/.local/tasks/palace400-runtime-20261023/a2-build/support/PluginInfo.cs')
if not plugin_info.is_file():
    raise SystemExit('PluginInfo source is missing: ' + str(plugin_info))
shutil.copy2(plugin_info, support / 'PluginInfo.cs')

text = wrapper.read_text()
text = text.replace(str(product), str(frozen))
text = text.replace(str(plugin_info), str(support / 'PluginInfo.cs'))
text = re.sub(r'<OutputPath>[^<]*</OutputPath>', '<OutputPath>' + str(output / 'bin') + '/</OutputPath>', text)
text = re.sub(r'<BaseIntermediateOutputPath>[^<]*</BaseIntermediateOutputPath>',
              '<BaseIntermediateOutputPath>' + str(output / 'obj') + '/</BaseIntermediateOutputPath>', text)
text = re.sub(r'<MSBuildProjectExtensionsPath>[^<]*</MSBuildProjectExtensionsPath>',
              '<MSBuildProjectExtensionsPath>' + str(output / 'obj') + '/</MSBuildProjectExtensionsPath>', text)
text = re.sub(r'<BepInExPluginsPath>[^<]*</BepInExPluginsPath>', '<BepInExPluginsPath></BepInExPluginsPath>', text)
if '<NoDeploy>true</NoDeploy>' not in text:
    text = text.replace('</PropertyGroup>', '<NoDeploy>true</NoDeploy></PropertyGroup>', 1)
project = output / 'frozen-interop.csproj'
project.write_text(text)

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
command = [
    dotnet, 'build', str(project),
    '-p:BepInExPluginsPath=',
    '-p:NoDeploy=true',
    '-p:BaseIntermediateOutputPath=' + str(output / 'obj') + '/',
    '-p:OutputPath=' + str(output / 'bin') + '/',
]
with (output / 'build.log').open('w') as log:
    build_exit = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
log_text = (output / 'build.log').read_text(errors='replace')
warnings = re.search(r'(\d+) Warning\(s\)', log_text)
errors = re.search(r'(\d+) Error\(s\)', log_text)
warning_count = int(warnings.group(1)) if warnings else None
error_count = int(errors.group(1)) if errors else None
dll = output / 'bin' / 'KingdomEnhancedMod.dll'
source_hashes = {}
for path in sorted(frozen.rglob('*')):
    if path.is_file() and (path.suffix == '.cs' or path.suffix == '.props'):
        source_hashes[path.relative_to(frozen).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
receipt = {
    'build_exit': build_exit,
    'warning_count': warning_count,
    'error_count': error_count,
    'command': command,
    'wrapper': str(wrapper),
    'wrapper_sha256': hashlib.sha256(wrapper.read_bytes()).hexdigest(),
    'frozen_source': str(frozen),
    'frozen_source_file_count': len(source_hashes),
    'frozen_source_sha256': source_hashes,
    'new_adapter_sha256': hashlib.sha256((product / 'Patch_PalaceCapitalScene.cs').read_bytes()).hexdigest(),
    's2a_resources_sha256': hashlib.sha256((product / 'Patch_PalaceCapitalResources.cs').read_bytes()).hexdigest(),
    'dll': str(dll),
    'dll_sha256': hashlib.sha256(dll.read_bytes()).hexdigest() if dll.is_file() else None,
    'deployed': False,
    'native_verified': False,
    'boundary': 'Actual Unity/BepInEx interop reference compile of the frozen product source. '
                'No game launch, install, plugin copy, profile, save, controller, Android build or Native acceptance.',
}
(output / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
print(json.dumps({'build_exit': build_exit, 'warnings': warning_count, 'errors': error_count}))
if build_exit != 0 or warning_count != 0 or error_count != 0 or not dll.is_file():
    sys.exit(1)
