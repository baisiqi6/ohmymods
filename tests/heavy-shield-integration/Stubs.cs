using System;
using System.Collections.Generic;
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute { public HarmonyPatch(Type t,string name,Type[] args) { } }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPrefix : Attribute { }
}
namespace UnityEngine
{
    public class GameObject { public bool activeInHierarchy=true; }
}
public class Archer { public UnityEngine.GameObject gameObject=new(); }
public class Persistent { }
public class IslandSaveData { public class ObjectData { } }
public class PrefsSaveData { }
public class GlobalSaveData { }
public class CampaignSaveData { }
public class Embarkable { }
public class Embarkee { public UnityEngine.GameObject gameObject=new(); public void SetEmbarkableTarget(Embarkable target,int slot) { } public void Embark() { } }
namespace KingdomEnhancedMod
{
    internal static class ModConfig { internal class Flag { internal bool Value; } internal static Flag Enabled=new(){Value=true},HeavyShieldEnabled=new(){Value=true}; }
    internal class KingdomEnhancedPlugin { internal static KingdomEnhancedPlugin Instance=new(); internal Logger LogSource=new(); internal class Logger { internal int Calls; internal void LogWarning(string s) { Calls++; } } }
    internal static class HeavyShieldIdentity
    {
        internal static readonly HashSet<object> Roots=new(); internal static int Fresh,Despawn;
        internal static bool IsKnownCareerRoot(object root)=>root!=null&&Roots.Contains(root);
        internal static void ObservePoolFreshLife(object root,bool fresh) { if(fresh)Fresh++; }
        internal static void ObservePoolDespawn(object root,float delay) { Despawn++; }
    }
    internal static class HeavyShieldRuntime
    {
        internal static object HeldDemoteSource;
        internal static bool HoldsNativeDemoteProof(object root) => root != null && ReferenceEquals(root,HeldDemoteSource);
        internal static bool CarrierMutationInProgress; internal static int TickCalls,DeathCalls; internal static bool ThrowTick;
        internal static void Tick(){TickCalls++;if(ThrowTick)throw new Exception("runtime fault");}
        internal static void BeforeNativePoolDespawn(object root){DeathCalls++;}
    }
    internal static class HeavyShieldShopShell
    {
        internal static int TickCalls,CancelCalls; internal static bool ThrowCancel,LastEnabled;
        internal static void Tick(bool enabled){TickCalls++;LastEnabled=enabled;}
        internal static void CancelPendingTransactionsBeforeNativeSave(){CancelCalls++;if(ThrowCancel)throw new Exception("cancel fault");}
    }
    internal static class HeavyShieldPersistence
    {
        internal class SaveCapture { internal int Ids,Markers; }
        internal class LoadCapture { internal int Rows; }
        internal class CreateCapture { internal LoadCapture Parent; }
        internal class GenerationCapture { }
        internal class PrepareCapture { }
        internal static SaveCapture LastSave; internal static LoadCapture LastLoad;
        internal static int BindingCalls,PrepareCalls,EndPrepareCalls,EndSaveCalls,EndLoadCalls,EndRowCalls,EndGenCalls;
        internal static bool LastPrepareNormal,ThrowBeginPrepare,ThrowEndPrepare;
        internal static bool ThrowBeginSave,ThrowEndSave,ThrowBeginLoad,ThrowId,LastSaveNormal,LastLoadSuccess,ThrowBinding;
        internal static void TickBinding(){BindingCalls++;if(ThrowBinding)throw new Exception("binding fault");}
        internal static SaveCapture BeginNativeIslandSave(int slot,int land,int challenge){if(ThrowBeginSave)throw new Exception("begin fault");return LastSave=new();}
        internal static void ObserveNativeId(SaveCapture scope,Persistent root,string id){scope.Ids++;if(ThrowId)throw new Exception("id fault");}
        internal static void ObserveNativeIslandCaptured(SaveCapture scope,IslandSaveData island){scope.Markers++;}
        internal static void EndNativeIslandSave(SaveCapture scope,bool normal){EndSaveCalls++;LastSaveNormal=normal;if(ThrowEndSave)throw new Exception("end fault");}
        internal static LoadCapture BeginNativeIslandLoad(IslandSaveData island){if(ThrowBeginLoad)throw new Exception("load fault");return LastLoad=new();}
        internal static void ObserveNativeLoadRow(LoadCapture scope,IslandSaveData.ObjectData row,Persistent root){scope.Rows++;}
        internal static void EndNativeIslandLoad(LoadCapture scope,bool success){EndLoadCalls++;LastLoadSuccess=success;}
        internal static CreateCapture BeginNativeLoadRow(LoadCapture scope,IslandSaveData.ObjectData row)=>new(){Parent=scope};
        internal static void EndNativeLoadRow(CreateCapture scope){EndRowCalls++;}
        internal static PrepareCapture BeginNativePrefsPrepare(PrefsSaveData prefs){PrepareCalls++;if(ThrowBeginPrepare)throw new Exception("prepare begin fault");return new();}
        internal static void EndNativePrefsPrepare(PrepareCapture scope,bool normal){EndPrepareCalls++;LastPrepareNormal=normal;if(ThrowEndPrepare)throw new Exception("prepare end fault");}
        internal static void BeforeCampaignMutation(GlobalSaveData global){ }
        internal static void AfterCampaignCreated(GlobalSaveData global,CampaignSaveData campaign){ }
        internal static GenerationCapture BeginNativeGeneration(CampaignSaveData campaign)=>new();
        internal static void EndNativeGeneration(GenerationCapture scope,CampaignSaveData campaign,bool success){EndGenCalls++;}
    }
}
