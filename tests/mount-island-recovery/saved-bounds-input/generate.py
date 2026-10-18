from pathlib import Path
import argparse,hashlib,json
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--source',type=Path);p.add_argument('--regression-only',action='store_true');a=p.parse_args();W=Path(__file__).resolve().parents[3];S=a.source or W/'il2cpp/Patch_MountIslandRecovery.cs';s=S.read_text();O=a.output.resolve();O.mkdir(parents=True,exist_ok=True)
def method(marker):
 st=s.index(marker);i=s.index('{',st)+1;n=1
 while n:
  if s[i]=='{':n+=1
  elif s[i]=='}':n-=1
  i+=1
 return s[st:i]
model=method('    private static bool TryBuildPlacementModel(');prefix=model[:model.index('            Tile[] tiles')];prefix+='            model=result;return true;\n        } catch(Exception e){reason=e.GetType().Name;return false;}\n    }'
new='int savedLevelMin' in prefix
methods=[]
for name in ['TryReadSavedLevelEdges','TryMatchSavedLevelEdges']:
 marker='    private static bool '+name+'('
 if marker in s:methods.append(method(marker))
helpers='\n'.join(methods)
wrap='TryBuildPlacementModel(l,new Transform(),new World(),'+('min,max,' if new else '')+'out var m,out reason)'
code='using System;using System.Collections.Generic;namespace SavedBoundsTests {static partial class Actual {\n'+prefix+'\n'+helpers+'\n internal static bool Model(Level l,int min,int max,out string reason)=>'+wrap+';\n'
if methods:code+='internal static bool Read(IslandSaveData i,out int min,out int max,out string why)=>TryReadSavedLevelEdges(i,out min,out max,out why);internal static bool Match(Capture c,out string why)=>TryMatchSavedLevelEdges(c,out why);\n'
code+='} }';(O/'Actual.cs').write_text(code)
(O/'Host.csproj').write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup><Compile Include="Actual.cs"/><Compile Include="{Path(__file__).resolve().parent}/{'RedProgram.cs' if a.regression_only else 'Program.cs'}"/></ItemGroup></Project>')
(O/'extraction.json').write_text(json.dumps({'source':str(S),'sha256':hashlib.sha256(s.encode()).hexdigest(),'modelPrefix':'exact production method through boundary guard; ends before Tile enumeration; Unity/native values stubbed','newInput':new,'helperBodies':len(methods)},indent=2)+'\n')
