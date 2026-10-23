from pathlib import Path
import sys, json, hashlib
w=Path(sys.argv[1]);output=Path(sys.argv[2]);output.mkdir(parents=True,exist_ok=True)
def extract(path, marker):
 s=(w/path).read_text();a=s.index(marker);b=s.index('{',a);eq=s.find('=>',a,b)
 if eq>=0:return s[a:s.index(';',eq)+1]
 depth=1;i=b+1
 while depth:
  depth+=(s[i]=='{')-(s[i]=='}');i+=1
 return s[a:i]
def method(path,marker):return extract(path,marker).replace('private static','internal static',1)
paid='Patch_PalacePaidResponsibility.cs';clock='Patch_PalaceClockBootstrap.cs';travel='Patch_PalaceIslandTravel.cs'
text='#nullable enable annotations\nusing System; namespace KingdomEnhancedMod;\ninternal static class PaidProbe {\n'+method(paid,'private static bool TripMatches(')+'\n}\n'
text+='internal static class ClockProbe {\n'+method(clock,'private static bool HandleMatches(')+'\n'+method(clock,'private static bool ReadTrip(')+'\n}\n'
text+='internal static class PalaceIslandTravel {\n'+extract(travel,'internal static bool SameAnchor(')+'\n}\n'
s=(w/paid).read_text();marker='internal bool MatchesDestination('
if marker in s:
 a=s.index('internal PalaceLayoutRecipe? DestinationLayout =>');getter=s[a:s.index(';',a)+1]
 text+='internal sealed partial class PalacePaidHandle {\n'+getter+'\n'+extract(paid,marker)+'\n}\n'
record=extract(travel,'var record = new PalaceTripRecord')+';'
text+='internal static partial class RecordProbe { private const int PalaceLand12=PalaceLand12Runtime.LandIndex; internal static PalaceTripRecord BuildRecord(Visit v, CampaignSaveData campaign,string guid,int slot) { int recordSource=v.Returning?v.TargetLand:v.SourceLand; '+record+' return record; } '+method(travel,'private static PalaceEntryAnchorData CloneAnchor(')+' }\n'
text+='internal static partial class BeginProbe {\n'+extract(paid,'internal static bool CanBeginCharge(')+'\n'+extract(paid,'internal static bool TryBeginCharge(')+'\n'+method(paid,'private static bool Apply(')+'\n'+method(paid,'private static bool TryVerifyTripDestination(')+'\n}\n'
text+='internal sealed partial class PaymentProbe {\n'+extract('Patch_PalaceEntryPayment.cs','private void OnTransactionStarted(').replace('private void OnTransactionStarted','internal void OnTransactionStarted',1)+'\n}\n'
text+='internal static partial class RecordProbe {\n'+method(travel,'private static bool VisitDestinationMatches(')+'\n'+method(travel,'private static bool RecordDestinationMatches(')+'\n'+method(travel,'private static bool VerifyWrittenDestination(')+'\n'+method(travel,'private static bool VerifyReturnSourceRecord(')+'\n'+method(travel,'private static bool SourceSaveProven(')+'\n'+method(travel,'private static void AdvanceGlobalSave(')+'\n}\n'
text+='internal static partial class StartPaidProbe {\n'+extract(travel,'public bool CanStartPaid(').replace('public bool','internal static bool',1)+'\n}\n'
(output/'Extracted.cs').write_text(text)
(output/'extraction.json').write_text(json.dumps({p:hashlib.sha256((w/p).read_bytes()).hexdigest() for p in [paid,clock,travel,'Patch_PalaceEntryPayment.cs']},indent=2))
