using System;using System.Collections.Generic;using System.Runtime.CompilerServices;
namespace Il2CppSystem.Collections.Generic {class List<T>:System.Collections.Generic.List<T>{}}
namespace SavedBoundsFence {
struct IntRange {public int min,max;}
class IslandSaveData {public static bool isSavingGame;public static IslandSaveData _currentlySavingIsland;public int land=11;public double playTimeDays=5;public int Min=-552,Max=519,Reads;public bool Throw;public Il2CppSystem.Collections.Generic.List<ObjectData> objects=new(){new ObjectData()};public class ObjectData {} public IntRange LevelEdges{get{Reads++;if(Throw)throw new InvalidOperationException();return new(){min=Min,max=Max};}}}
class GlobalSaveData {} class Level {} class Transform {} class World {}
class CampaignSaveData {public int CurrentLand=11;public Il2CppSystem.Collections.Generic.List<IslandSaveData> _islands=new();}
class Managers {public static Managers Inst=new();}
static class CrossWorldMountRuntime {public static bool GrantEnabled()=>Host.Grant;}
static class CrossWorldMountCatalog {public static object[] Definitions=new object[16];}
static class MountIslandSplitPolicy {public static bool IsExtensionLand(int l)=>l==11||l==13;}
static class ExtensionIslandRuntime {public static bool TryScope(out CampaignSaveData c){c=Host.Campaign;return Host.ScopeValid;}public static bool TryResolveRawOwner(out GlobalSaveData g,out int selected,out CampaignSaveData c){g=Host.Global;selected=0;c=Host.Campaign;return Host.ScopeValid;}}
static class MountIslandMarkerRuntime {public static bool TryReadAllMarkers(ulong g,CampaignSaveData c,int reign,ulong data,out int[][] m,out string why){m=new[]{Array.Empty<int>()};why="";Host.MarkerCalls++;if(Host.MarkerCalls==1)Host.AfterFirstMarkers?.Invoke();return Host.MarkersValid;}}
static partial class Actual {
private enum ScopeReadPhase{AfterNativePop,BeforeNativePop}
private static ulong PointerOf(object o)=>Host.Ptr(o);
private static void Log(string x){} private static void LogError(string x){} private static void LogOnce(string k,string x){}
private static bool TryReadSavedWorldSeed(IslandSaveData i,out int seed,out string why){seed=934126577;why="";return true;}
private static bool TryScanReceiptFacts(CampaignSaveData c,int land,out ReceiptFacts f,out string why){f=new(new bool[16],new bool[16],new bool[16],true);why="";return true;}
private static bool TryReadScope(ScopeReadPhase p,int selected,int land,IslandSaveData i,out MountIslandRecoveryScope s,out Level l,out Transform t,out World w,out string why){s=Host.Scope();l=new();t=new();w=new();why="stub-scope";return Host.ScopeValid;}
private static bool TryReadScope(int land,IslandSaveData i,out MountIslandRecoveryScope s,out Level l,out Transform t,out World w,out string why)=>TryReadScope(ScopeReadPhase.AfterNativePop,-1,land,i,out s,out l,out t,out w,out why);
private static bool TryReadScope(int selected,int land,IslandSaveData i,out MountIslandRecoveryScope s,out Level l,out Transform t,out World w,out string why)=>TryReadScope(ScopeReadPhase.AfterNativePop,selected,land,i,out s,out l,out t,out w,out why);
}
static class Host {public static bool ScopeValid=true,Grant=true,MarkersValid=true;public static int Seed,Ensure,MarkerCalls;public static Action AfterFirstMarkers;public static GlobalSaveData Global;public static CampaignSaveData Campaign;public static IslandSaveData Island;
public static ulong Ptr(object o)=>o==null?0:unchecked((ulong)RuntimeHelpers.GetHashCode(o)+1);
public static MountIslandRecoveryScope Scope()=>new(Ptr(Global),Ptr(Campaign),0,3,3,Ptr(Campaign._islands),11,5,-3026,"main",Seed,6,-3026,8,5,Ptr(Island),11);
public static void Reset(){ScopeValid=Grant=MarkersValid=true;Seed=Ensure=MarkerCalls=0;AfterFirstMarkers=null;Global=new();Campaign=new();Island=new();for(int i=0;i<12;i++)Campaign._islands.Add(i==11?Island:new(){land=i});}
public static MountIslandRecoveryToken Prepare(){var t=Actual.BeforePop(Island,true);Island.objects=null;Seed=934126577;return t;}
public static void Publish(MountIslandRecoveryToken t)=>Actual.AfterPop(Island,true,true,t);
}
}
