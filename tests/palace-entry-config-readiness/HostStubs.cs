using System;
namespace KingdomEnhancedMod;
internal sealed class Game {internal IntPtr Pointer=new(123);internal int currentLand=8;}
internal sealed class Managers {internal Game game=new();}
internal static class GlobalSaveData {internal static object loaded=new();}
internal static class CampaignSaveData {internal static object current=new();}
internal static class ExtensionIslandRuntime {internal static bool Prepared=false;internal static bool IsFilePropsPrepared()=>Prepared;}
internal static class PalaceTripContext {internal static bool TryBinding(object g,object c,out string guid,out int slot,out string reason){guid="owner";slot=0;reason="";return true;}}
internal static class PalaceIslandTravel {internal static bool HasActiveJourney=false;}
internal static class PalaceVisibleEntry {internal static void Log(string s){}}
internal static partial class PalaceLand12Runtime {internal const int LandIndex=12;internal static bool Enabled=true,_generationGateReady=true;internal static int Attempts;internal static bool Prepared=false;internal static bool CanEnterPrepared(Game g,out string reason){reason="";return Prepared;}internal static bool PrepareForEntry(Game g,out string reason){Attempts++;reason="injected terminal failure";return false;}}
internal static partial class EntryConfigProbe {internal static IntPtr _configPreparationGame;internal static int Waits;private static void WaitFor(string reason){Waits++;}}
