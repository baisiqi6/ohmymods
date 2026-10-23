using System;
using System.Collections.Generic;
namespace UnityEngine {internal static class JsonUtility {internal static string ToJson(KingdomEnhancedMod.IslandSaveData island,bool pretty)=>island.Json;}}
namespace KingdomEnhancedMod {
internal class NativeObject {private static int next;internal IntPtr Pointer=new(++next);}
internal class NativeList<T>:List<T>{private static int next;internal IntPtr Pointer=new(++next);}
internal sealed class Game:NativeObject {internal int currentLand=8;}
internal sealed class Managers:NativeObject {internal static Managers Inst;internal Game game=new();}
internal sealed class GlobalSaveData:NativeObject {internal static GlobalSaveData loaded;internal static string filename="global-v35";internal CampaignSaveData GetCurrentCampaign()=>CampaignSaveData.current;}
internal sealed class CampaignSaveData:NativeObject {internal static CampaignSaveData current;internal CampaignSaveData(){current=this;}internal int reign=3,biomeIndex=5,realStartDateTime=262,CurrentLand=8;internal NativeList<IslandSaveData> _islands=new();}
internal struct IntRange {internal int min,max;}
internal sealed class IslandSaveData:NativeObject {
 internal static bool isSavingGame;internal static int MAX_BIOME_ISLANDS=14;internal static PropsTable fileProps;
 internal struct FilePropsKey {public int campaign,land,challengeId;}
 internal sealed class FileProps {internal string filename;}
 internal sealed class PropsTable:Dictionary<FilePropsKey,FileProps>{internal IntPtr Pointer=new(989);}
 internal IntRange _levelEdges;internal NativeList<object> objects=new();internal string Json;
}
internal static class ExtensionIslandRuntime {internal static bool Ready=true;internal const int RequiredFileCapacity=14;internal static bool IsFilePropsPrepared()=>Ready;}
internal static class Filer {internal static string folder;internal sealed class DotNetAgent {internal string folder;}}
internal sealed class PalaceSaveBinding {internal Filer.DotNetAgent Agent=new();internal string Folder,SemanticGuid="11111111-1111-1111-1111-111111111111";internal int SemanticSlot=0,SemanticBiome=5,SemanticReign=3,SemanticStart=262;}
internal static class PalaceSaveBindingSource {internal static PalaceSaveBinding Current;internal static bool Good=true;internal static bool CurrentMatches(out string reason){reason=Good?"ok":"binding-drift";return Good;}internal static bool TryGetSemanticOwner(out string guid,out int slot,out int biome,out int reign,out int start,out string reason){guid=Current.SemanticGuid;slot=Current.SemanticSlot;biome=Current.SemanticBiome;reign=Current.SemanticReign;start=Current.SemanticStart;return CurrentMatches(out reason);}}
internal static class PalaceRuntimeProfile {internal const string Id="test";}
internal enum PalaceDestinationSupport {Fresh,SupportedSaved,UnsupportedOrUnknown}
internal static class PalaceDedicatedEndpoints {internal const string BeachPrefabPath="Prefabs/Buildings and Interactive/greece/Beach_greece";internal const float OwnedBeachLeftX=-14.6875f,OwnedBeachRightX=14.6875f;}
internal static class PalaceLand12Runtime {internal static bool TryReadSavedCampaignBinding(GlobalSaveData g,CampaignSaveData c,out string guid,out int slot,out string reason){guid=PalaceSaveBindingSource.Current.SemanticGuid;slot=0;reason="";return true;}}
internal sealed class PalaceTripRecord {public int Version{get;set;}public string TripId{get;set;}public string CampaignGuid{get;set;}public int CampaignSlot{get;set;}public int Reign{get;set;}[System.Text.Json.Serialization.JsonIgnore]public PalaceLayoutRecipe DestinationLayout{get;set;}}
internal static class PalaceTripContext {internal static bool TryBinding(GlobalSaveData g,CampaignSaveData c,out string guid,out int slot,out string reason){guid=PalaceSaveBindingSource.Current.SemanticGuid;slot=0;reason="";return PalaceSaveBindingSource.Good;}internal static bool Validate(PalaceTripRecord trip,out string reason){reason="";return true;}internal static bool RecordMatches(CampaignSaveData c,string guid,int slot,PalaceTripRecord trip,out string reason){reason="";return trip.CampaignGuid==guid&&trip.CampaignSlot==slot&&trip.Reign==c.reign;}internal static bool TupleEquals(PalaceTripRecord a,PalaceTripRecord b)=>a.TripId==b.TripId&&a.Version==b.Version;internal static bool TryParse(string s,out PalaceTripRecord trip,out string reason){trip=System.Text.Json.JsonSerializer.Deserialize<PalaceTripRecord>(s);reason="";return true;}}
internal sealed class PalacePaidFileProof {internal int NativeRawResult=72,NativeVersion=16,SourceLand=8;internal PalacePaidScope Owner;internal string RawSaveSha256;}
internal static class PalaceCourtArea {
 internal static string Sidecar;
 internal sealed class CourtAreaMetadataRead {internal string CampaignGuid;internal int CampaignSlot,Reign,MetadataSchema;internal PalaceLayoutRecipe Recipe;}
 internal static string SidecarPathForAdmission()=>Sidecar;
 internal static CourtAreaMetadataRead ReadSidecar(out string reason){try{if(!PalaceLayoutMetadata.TryParse(System.IO.File.ReadAllText(Sidecar),out var m,out var r,out reason))return null;return new(){CampaignGuid=m.CampaignGuid,CampaignSlot=m.CampaignSlot,Reign=m.Reign,MetadataSchema=m.V,Recipe=r};}catch(System.IO.FileNotFoundException){reason="missing";return null;}}
}
internal static partial class PalaceDestinationAdmission {private static bool Approx(float a,float b)=>Math.Abs(a-b)<=.001f;internal static void RunResourcePrecheck(){}internal static bool TryGetResourcePrecheck(out string reason){reason="legacy-control";return true;}}
}
