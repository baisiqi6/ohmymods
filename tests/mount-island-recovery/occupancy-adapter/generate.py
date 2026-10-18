#!/usr/bin/env python3
"""Extract actual production methods and existing baseline; never writes in the source tree."""
from pathlib import Path
import argparse, hashlib, json
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);a=p.parse_args()
repo=Path(__file__).resolve().parents[3]; out=a.output.resolve();out.mkdir(parents=True,exist_ok=True)
def extract(s, marker):
 start=s.index(marker);i=s.index('{',start)+1;depth=1
 while depth:
  if s[i]=='{':depth+=1
  elif s[i]=='}':depth-=1
  i+=1
 return s[start:i]
def sha(s):return hashlib.sha256(s.encode()).hexdigest()
s=(repo/'il2cpp/Patch_MountIslandRecovery.cs').read_text();old=(repo/'tests/mount-island-recovery/scope-adapter/baseline/Patch_MountIslandRecovery.0919.cs').read_text();plan=(repo/'il2cpp/Patch_MountIslandRecoveryPlan.cs').read_text()
markers=['    private static bool CollectOccupancy(', '    private static bool TryCollectBounds(']
new_methods=[extract(s,m) for m in markers];old_methods=[extract(old,m) for m in markers]
helper='    private static bool TryCollectDirectColliderBounds('
if helper in s:new_methods.append(extract(s,helper))
actual='using System;using System.Collections.Generic;namespace KingdomEnhancedMod {\n'
for name,methods in [('Actual',new_methods),('Old',old_methods)]:actual+='static class '+name+' {\n'+'\n'.join(methods)+'\ninternal static bool Collect(Level l,Transform p,List<MountIslandRecoveryInterval> x,out string r)=>CollectOccupancy(l,p,x,out r);\n}\n'
actual+='}\n';(out/'Actual.cs').write_text(actual)
policy='\n'.join(extract(plan,m) for m in ['internal readonly struct MountIslandRecoveryInterval','internal readonly struct MountIslandRecoveryPlacementRequest','internal readonly struct MountIslandRecoveryPlacement\n','internal static class MountIslandRecoveryPlacementPolicy'])
(out/'Policy.cs').write_text('using System;using System.Collections.Generic;namespace KingdomEnhancedMod {\n'+policy+'\n}\n')
# Geometry stays byte-for-byte source-linked through Compile Include.
here=Path(__file__).resolve().parent
(out/'Host.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Actual.cs"/><Compile Include="Policy.cs"/><Compile Include="{here}/Program.cs"/><Compile Include="{here}/Stubs.cs"/><Compile Include="{repo}/il2cpp/Patch_MountIslandRecoveryGeometry.cs"/></ItemGroup></Project>''')
report={'productionSHA256':sha(s),'oldMethods':dict(zip(markers,map(sha,old_methods))),'currentMethods':dict(zip(markers+([helper] if helper in s else []),map(sha,new_methods))),'boundsUnchanged':new_methods[1]==old_methods[1],'initialCollectEqualsBaseline':new_methods[0]==old_methods[0],'extraction':'exact source substrings, no replacements','nativeBoundaries':'Unity component queries/active/Bounds and Level overlap stubbed; policy and geometry actual production'}
(out/'extraction.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report))
