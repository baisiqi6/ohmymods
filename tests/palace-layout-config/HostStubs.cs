using System;
namespace KingdomEnhancedMod;
internal sealed class BiomeHolder {}
internal sealed class LevelConfig {internal ulong Pointer=22;}
internal sealed class CampaignSaveData {}
internal static class GlobalSaveData {internal static object loaded=new();}
internal static class PalacePrivateConfigFactory {internal static string Hex(ulong v)=>v.ToString();}
internal sealed class PalacePrivateConfigCache {
 internal enum PendingKind{Empty,Occupied}
 internal bool IsStickyFailed=false;internal string FailureReason="";internal PendingKind PendingState=PendingKind.Empty;
 internal LevelConfig PublishedClone;internal ulong PublishedHolderPointer=7;internal int Allocations;
 internal bool TryGetClone(BiomeHolder holder,out LevelConfig clone,out string reason){reason="";if(PublishedClone==null){PublishedClone=new();Allocations++;}clone=PublishedClone;return true;}
}
internal sealed class PalaceTripRecord {internal int Version;internal string TripId;internal PalaceLayoutRecipe DestinationLayout;}
internal static class PalaceTripContext {internal enum ReadStatus{Ok,Invalid}internal static PalaceTripRecord Record;internal static bool ReadOk=true;internal static ReadStatus Read(object g,out PalaceTripRecord record,out string reason){record=Record;reason="";return ReadOk?ReadStatus.Ok:ReadStatus.Invalid;}}
internal static class PalaceDestinationAdmission {internal static bool SavedProof;internal static int ProofCalls;internal static bool TryResolveTripLayout(CampaignSaveData c,PalaceTripRecord t,out PalaceLayoutRecipe recipe,out string reason){ProofCalls++;recipe=SavedProof?(t.Version==3?PalaceLayoutRecipe.Known44:t.DestinationLayout):null;reason="strict-saved-boundary";return SavedProof;}}
internal static partial class PalaceLand12Runtime {private static string Hex(ulong v)=>v.ToString();internal static PalacePrivateConfigCache ConfigCache=new();internal static PalaceLayoutRecipe _publishedRecipe=null;internal sealed class ReloadHandoffToken{internal PalaceLayoutRecipe Recipe;internal string TripId;}internal static ReloadHandoffToken _handoff;}
