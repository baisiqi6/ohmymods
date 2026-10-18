from pathlib import Path
import argparse,hashlib,json,re
p=argparse.ArgumentParser();p.add_argument('--baseline',type=Path,required=True);p.add_argument('--report',type=Path,required=True);a=p.parse_args();W=Path(__file__).resolve().parents[3];s=(W/'il2cpp/Patch_MountIslandRecovery.cs').read_text();old=a.baseline.read_text();sha=lambda t:hashlib.sha256(t.encode()).hexdigest();assert sha(old)=='a17f2b7ef22673db7d2890e13e2dca69754d477cf9fe0f9a4b0e5f548c23b4ec'
def extract(text,marker):
 st=text.index(marker);i=text.index('{',st)+1;d=1
 while d:
  if text[i]=='{':d+=1
  elif text[i]=='}':d-=1
  i+=1
 return text[st:i]
def errors(text):
 try:
  before=extract(text,'    private static bool TryBuildContext(');post=extract(text,'    internal static void AfterPop(');consume=extract(text,'    private static void TryRecover(');host=extract(text,'        private bool RecheckScope(');model=extract(text,'    private static bool TryBuildPlacementModel(');read=extract(text,'    private static bool TryReadSavedLevelEdges(')
  scopes=[m.start() for m in re.finditer(re.escape('TryReadScope(ScopeReadPhase.BeforeNativePop'),before)]
  checks=[len(scopes)==2,scopes[0]<before.index('TryReadSavedLevelEdges(island, out int savedLevelMin,')<before.index('MountIslandMarkerRuntime.TryReadAllMarkers('),before.index('TryReadSavedLevelEdges(island, out int savedLevelMinAgain,')<scopes[1], 'savedLevelMinAgain != savedLevelMin || savedLevelMaxAgain != savedLevelMax' in before,'SavedLevelMin = savedLevelMin,' in before and 'SavedLevelMax = savedLevelMax,' in before,post.index('IsAllowedSeedTransition(')<post.index('TryMatchSavedLevelEdges(capture, out reason)')<post.index('Tokens.PublishReady('),consume.index('TryMatchSavedLevelEdges(capture, out reason)')<consume.index('CrossWorldMountDependencies.EnsureAll('),host.endswith('return TryMatchSavedLevelEdges(_capture, out reason);\n        }'),'LevelMin = savedLevelMin,' in model and 'LevelMax = savedLevelMax,' in model,'level._levelEdges' not in model and 'worldBounds' not in model,read.count('island.LevelEdges')==1 and 'IntRange edges = island.LevelEdges;' in read]
  return [i for i,ok in enumerate(checks) if not ok]
 except (ValueError,IndexError):return ['missing callsite']
assert not errors(s),errors(s)
negatives={
 'post-check-removed':s.replace('|| !TryMatchSavedLevelEdges(capture, out reason)',''),
 'consume-check-removed':s.replace('if (!TryMatchSavedLevelEdges(capture, out reason))','if (false)'),
 'last-host-check-removed':s.replace('return TryMatchSavedLevelEdges(_capture, out reason);','return true;'),
 'AB-reversed':s.replace('savedLevelMinAgain != savedLevelMin || savedLevelMaxAgain != savedLevelMax','savedLevelMinAgain == savedLevelMin || savedLevelMaxAgain == savedLevelMax'),
 'AB-float-only':s.replace('savedLevelMinAgain != savedLevelMin || savedLevelMaxAgain != savedLevelMax','(float)savedLevelMinAgain != savedLevelMin || (float)savedLevelMaxAgain != savedLevelMax'),
 'model-old-cache':s.replace('LevelMin = savedLevelMin,','LevelMin = level._levelEdges.min,'),
 'second-getter-for-max':s.replace('max = edges.max;','max = island.LevelEdges.max;'),
 'capture-min-late-default':s.replace('SavedLevelMin = savedLevelMin,','SavedLevelMin = 0,')}
assert all(errors(x) for x in negatives.values())
frozen={}
for name in ['CollectOccupancy','TryCollectDirectColliderBounds','TryCollectBounds','TryTileInterval','AddStructure','TryPlanPlacements','ProbePlacement','SameOtherReceipts','TryReadSavedWorldSeed']:
 marker='    private static '+('bool '+name+'(')
 oldbody=extract(old,marker);body=extract(s,marker);assert body==oldbody,name;frozen[name]=sha(body)
# The entire original scope phase/overloads/full reader remains byte-for-byte unchanged.
start='    private enum ScopeReadPhase';end='    /// <summary>\n    /// 从同次 island.objects';newend='    // IslandSaveData owns'
assert old[old.index(start):old.index(end)].rstrip()==s[s.index(start):s.index(newend)].rstrip()
model=extract(s,'    private static bool TryBuildPlacementModel(');baseline=extract(old,'    private static bool TryBuildPlacementModel(');assert model[model.index('            Tile[] tiles'):]==baseline[baseline.index('            Tile[] tiles'):]
result={'productionSHA256':sha(s),'baselineSHA256':sha(old),'positiveWiring':True,'negativeMutationChecks':{k:True for k in negatives},'frozenMethods':frozen,'scopeReaderAndDefaultsUnchanged':True,'allPlacementAfterBoundaryGuardUnchanged':True,'noNativeOrProductWrites':True};a.report.write_text(json.dumps(result,indent=2)+'\n');print('SOURCE contract PASS: 8 wiring mutation negatives; original scope and placement tail preserved; frozen methods',len(frozen))
