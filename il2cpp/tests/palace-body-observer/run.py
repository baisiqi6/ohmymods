#!/usr/bin/env python3
"""Build/run the palace-body-observer suites in isolated output. No game, plugin deploy or asset writes."""
import argparse
import hashlib
import json
import pathlib
import re
import subprocess
import sys

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
parser.add_argument('--dotnet', required=True)
args = parser.parse_args()

tests = pathlib.Path(__file__).resolve().parent
il2cpp = tests.parents[1]
out = pathlib.Path(args.output).resolve()
out.mkdir(parents=True, exist_ok=True)
if il2cpp == out or il2cpp in out.parents:
    raise SystemExit('Test output must be outside product source.')

observer_source = il2cpp / 'Patch_PalaceAdultBodyMetrics.cs'
worker_source = il2cpp / 'PatchRoles_Worker.cs'


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def extract_lateupdate_block(path):
    source = path.read_text(encoding='utf-8')
    declarations = list(re.finditer(r'private void LateUpdate\(\)', source))
    if len(declarations) != 2:
        raise SystemExit('expected guarded diagnostic+normal LateUpdate declarations, found %d' % len(declarations))
    guard_start = source.rindex('#if', 0, declarations[0].start())
    guard_end = source.index('#endif', declarations[-1].end())
    guard_end = source.index('\n', guard_end) + 1
    return source[guard_start:guard_end]


def project(name, files, defines):
    work = out / name
    work.mkdir(parents=True, exist_ok=True)
    includes = ''.join('<Compile Include="%s"/>' % f for f in files)
    text = (
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
        '<TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType>'
        '<ImplicitUsings>disable</ImplicitUsings><Nullable>disable</Nullable>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
        '<UseAppHost>false</UseAppHost><UseSharedCompilation>false</UseSharedCompilation>'
        '<NodeReuse>false</NodeReuse><NoWarn>0649;0169</NoWarn>'
        + ('<DefineConstants>%s</DefineConstants>' % defines if defines else '')
        + '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>')
    (work / 'Tests.csproj').write_text(text, encoding='utf-8')
    return work


def build_and_run(work, arguments):
    build = subprocess.run([args.dotnet, 'build', str(work / 'Tests.csproj'), '-c', 'Release'],
                           capture_output=True, text=True)
    (work / 'build.log').write_text(build.stdout + build.stderr, encoding='utf-8')
    if build.returncode != 0:
        raise SystemExit(build.stdout + build.stderr)
    run = subprocess.run([args.dotnet, str(work / 'bin/Release/net8.0/Tests.dll')] + arguments,
                         capture_output=True, text=True)
    tag = arguments[0] if arguments else 'run'
    (work / ('run-' + tag + '.log')).write_text(run.stdout + run.stderr, encoding='utf-8')
    print(work.name, (run.stdout + run.stderr).strip().splitlines()[-1] if (run.stdout + run.stderr).strip() else '')
    if run.returncode != 0:
        raise SystemExit(run.stdout + run.stderr)


lateupdate = extract_lateupdate_block(worker_source)

receipt = {
    'observerSource': str(observer_source),
    'observerSourceSHA256': sha256(observer_source),
    'workerSource': str(worker_source),
    'workerSourceSHA256': sha256(worker_source),
    'lateUpdateBlockSHA256': hashlib.sha256(lateupdate.encode('utf-8')).hexdigest(),
    'lateUpdateBlockChars': len(lateupdate),
    'suites': [],
}

observer_files = [tests / 'Program.cs.txt', tests / 'Host.cs.txt', observer_source]
observer_scenarios = {
    'default': ['default-perfect'],
    'diagnostic': [
        'diagnostic-fresh', 'diagnostic-online', 'diagnostic-saving', 'diagnostic-challenge',
        'diagnostic-wrong-land', 'diagnostic-owner-drift', 'diagnostic-agent-mismatch',
        'diagnostic-scene-drift', 'diagnostic-observe-before-arm', 'diagnostic-post-arm-campaign',
        'diagnostic-post-arm-agent', 'diagnostic-post-arm-land', 'diagnostic-camera-ambiguity',
        'diagnostic-camera-water-null', 'diagnostic-water-self', 'diagnostic-water-mismatch',
        'diagnostic-water-throws', 'diagnostic-material-missing-property',
    ],
}
for mode, scenarios in observer_scenarios.items():
    defines = 'PALACE_PAID_CAPTURE_DIAGNOSTICS' if mode == 'diagnostic' else ''
    work = project('observer-' + mode, observer_files, defines)
    for scenario in scenarios:
        build_and_run(work, [scenario] + (['dump'] if scenario == 'diagnostic-fresh' else []))
        receipt['suites'].append({'suite': 'observer-' + mode, 'scenario': scenario, 'result': 'pass'})

guard = '#if PALACE_PAID_CAPTURE_DIAGNOSTICS && !ANDROID'
observer_call = 'PalaceAdultBodyMetrics.ObserveAfterRegisteredY();'
maintain_call = 'GreekScaleScope.MaintainRegisteredY();'
assert lateupdate.count(guard) == 1 and '#else' in lateupdate and lateupdate.count('#endif') == 1, \
    'guarded diagnostic/normal split missing'
diagnostic_branch = lateupdate[:lateupdate.index('#else')]
normal_branch = lateupdate[lateupdate.index('#else'):lateupdate.index('#endif')]
assert diagnostic_branch.count(maintain_call) == 1 and diagnostic_branch.count(observer_call) == 1, \
    'diagnostic branch must maintain then observe exactly once'
assert diagnostic_branch.index(maintain_call) < diagnostic_branch.index(observer_call), \
    'observer must follow MaintainRegisteredY'
assert normal_branch.strip().endswith('private void LateUpdate() => GreekScaleScope.MaintainRegisteredY();'), \
    'normal branch must stay the original one-statement method'
assert 'finally' not in lateupdate, 'maintain call must not be wrapped in finally'

generated = out / 'worker-extract'
generated.mkdir(parents=True, exist_ok=True)
generated_source = generated / 'WorkerLateUpdate.g.cs'
generated_source.write_text(
    '#nullable disable\nusing System;\n\nnamespace KingdomEnhancedMod\n{\n'
    'internal sealed class ScaleRegistryHolderProbe\n{\n' + lateupdate + '\n}\n}\n',
    encoding='utf-8')


worker_files = [tests / 'WorkerOrder.cs.txt', generated_source]
worker_modes = {
    'default': '',
    'diagnostic': 'PALACE_PAID_CAPTURE_DIAGNOSTICS',
    'android-default': 'ANDROID',
    'android-diagnostic': 'ANDROID;PALACE_PAID_CAPTURE_DIAGNOSTICS',
}
for mode, defines in worker_modes.items():
    work = project('worker-' + mode, worker_files, defines)
    build_and_run(work, [mode])
    receipt['suites'].append({'suite': 'worker-' + mode, 'result': 'pass'})

receipt['boundary'] = (
    'Compiles the real Patch_PalaceAdultBodyMetrics.cs and the verbatim extracted LateUpdate method '
    'against explicit fake hosts. No Unity runtime, native call, normal save/Steam write, or deploy. '
    'Does not prove Native camera/water behaviour or a visible waterline.'
)
(out / 'receipt.json').write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print('observer+worker suites pass')
