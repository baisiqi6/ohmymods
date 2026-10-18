from pathlib import Path
import argparse,json,hashlib
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);a=p.parse_args();H=Path(__file__).resolve().parent;W=H.parents[2];S=W/'il2cpp/Patch_MountIslandRecovery.cs';s=S.read_text();plan=(W/'il2cpp/Patch_MountIslandRecoveryPlan.cs').read_text();O=a.output.resolve();O.mkdir(parents=True,exist_ok=True)
def extract(text,marker):
 st=text.index(marker);i=text.index('{',st)+1;n=1
 while n:
  if text[i]=='{':n+=1
  elif text[i]=='}':n-=1
  i+=1
 return text[st:i]
parts=[extract(s,m) for m in ['    private sealed class Capture','    private readonly struct ReceiptFacts','    internal static MountIslandRecoveryToken BeforePop(','    internal static void AfterPop(','    internal static void AfterLevelLoaded(','    private static bool TryBuildContext(','    private static bool TryReadSavedLevelEdges(','    private static bool TryMatchSavedLevelEdges(','    private static MountIslandRecoveryCell[] ReadCells(','    private static bool SameCells(','    private static bool SameMarkers(','    private static bool SameReceipts(','    private static bool SameOtherReceipts(']]
recover=extract(s,'    private static void TryRecover(');recover=recover[:recover.index('        // 原生依赖复用')]+ '        Host.Ensure++; // test sentinel at the first permitted side-effect boundary\n    }'
recheck=extract(s,'        private bool RecheckScope(')
outer='using System;using System.Collections.Generic;namespace SavedBoundsFence {static partial class Actual {private static readonly MountIslandRecoveryTokenSlot Tokens=new();\n'+'\n'.join(parts)+'\n'+recover+'\ninternal sealed class RecoveryHost {private readonly Capture _capture;internal RecoveryHost(object c){_capture=(Capture)c;}\n'+recheck+'\ninternal bool Check(out string why)=>RecheckScope(out why);}\ninternal static bool Recheck(MountIslandRecoveryToken t,out string why)=>new RecoveryHost(t.Payload).Check(out why);\ninternal static void Consume()=>AfterLevelLoaded(Managers.Inst,true,true);\n}\n'
outer+='static class MountIslandMarkerBatch {\n'+extract(plan,'    internal static bool SameSequence(')+'\n}\n'
for m in ['internal readonly struct MountIslandRecoveryCell','internal sealed class MountIslandRecoveryToken\n','internal sealed class MountIslandRecoveryTokenSlot','internal readonly struct MountIslandRecoveryScope']:outer+=extract(plan,m)+'\n'
a0=plan.index('    internal static bool IsOriginalCompletion(');b0=plan.index(';',a0)+1;outer+='static class MountIslandRecoveryPolicy {\n'+plan[a0:b0]+'\n}\n}\n';(O/'Actual.cs').write_text(outer)
(O/'Host.csproj').write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup><Compile Include="Actual.cs"/><Compile Include="{H}/FenceStubs.cs"/><Compile Include="{H}/FenceProgram.cs"/></ItemGroup></Project>')
(O/'extraction.json').write_text(json.dumps({'source':str(S),'sha256':hashlib.sha256(s.encode()).hexdigest(),'exactBodies':len(parts)+2,'cut':'TryRecover exact prefix ends before EnsureAll; sentinel only, never executes native business','stubBoundaries':'scope/native owner, markers/receipts/WorldSeed source enumeration and Unity; token/scope value logic actual source'},indent=2)+'\n')
