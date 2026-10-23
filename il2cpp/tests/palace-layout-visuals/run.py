#!/usr/bin/env python3
"""Build actual V sources in isolated output directories. No game, plugin deploy or asset writes."""
import argparse,hashlib,json,pathlib,subprocess
p=argparse.ArgumentParser();p.add_argument('--output',required=True);p.add_argument('--dotnet',required=True);p.add_argument('--registry',required=True);a=p.parse_args()
tests=pathlib.Path(__file__).resolve().parent;il2cpp=tests.parents[1];out=pathlib.Path(a.output).resolve();out.mkdir(parents=True,exist_ok=True)
if il2cpp==out or il2cpp in out.parents:raise SystemExit('Test output must be outside product source.')
def product(name):return il2cpp/name
suites={
 'geometry':[tests/'GeometryProgram.cs.txt',product('Patch_PalaceScrollGeometry.cs'),product('Patch_PalaceLayoutRecipe.cs')],
 'actor':[tests/'ActorProgram.cs.txt',product('Patch_PalaceActorRegistry.cs')],
 'binding':[tests/'BindingProgram.cs.txt',tests/'BindingHost.cs.txt',product('Patch_PalaceScrollGeometry.cs'),product('Patch_PalaceScrollNativeBinding.cs'),product('Patch_PalaceLayoutRecipe.cs')],
 'arrival':[tests/'ArrivalProgram.cs.txt',tests/'ArrivalHost.cs.txt',product('Patch_PalaceArrivalPlacement.cs'),product('Patch_PalaceLayoutRecipe.cs')],
}
receipt={'registry':str(pathlib.Path(a.registry).resolve()),'registrySHA256':hashlib.sha256(pathlib.Path(a.registry).read_bytes()).hexdigest(),'suites':[],'sourceSHA256':{}}
for name,files in suites.items():
 work=out/name;work.mkdir(exist_ok=True)
 project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'+''.join('<Compile Include="'+str(f)+'"/>'for f in files)+'</ItemGroup></Project>'
 (work/'Tests.csproj').write_text(project)
 build=subprocess.run([a.dotnet,'build',str(work/'Tests.csproj')],capture_output=True,text=True);(work/'build.log').write_text(build.stdout+build.stderr)
 if build.returncode:raise SystemExit(build.stdout+build.stderr)
 command=[a.dotnet,str(work/'bin/Debug/net8.0/Tests.dll')]+([a.registry]if name=='actor'else[])
 run=subprocess.run(command,capture_output=True,text=True);(work/'run.log').write_text(run.stdout+run.stderr)
 receipt['suites'].append({'suite':name,'buildExit':build.returncode,'runExit':run.returncode,'result':run.stdout.strip()})
 for f in files:receipt['sourceSHA256'][str(f)]=hashlib.sha256(f.read_bytes()).hexdigest()
 print(name,run.stdout.strip())
 if run.returncode:raise SystemExit(run.stdout+run.stderr)
receipt['boundary']='Actor parser does not load pixels or promote resources. Binding/arrival compile whole production adapters against explicit host surfaces; no Unity physics, Harmony ordering or W authority proof.'
(out/'receipt.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n')
