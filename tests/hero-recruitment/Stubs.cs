using System;
using System.Collections.Generic;
namespace Il2CppSystem.Collections.Generic
{
    // Minimal test-only stand-in for the installed Il2CppInterop list wrapper that real 2.4
    // exposes as IslandSaveData.objects: native-style Pointer identity plus ordinary enumeration.
    // Production only reads it; the native lifecycle replaces the island field with null.
    public class List<T> : System.Collections.Generic.IEnumerable<T>
    {
        static long counter;
        public IntPtr Pointer=(IntPtr)(++counter);
        readonly System.Collections.Generic.List<T> items;
        public List(){items=new();}
        public List(System.Collections.Generic.IEnumerable<T> source){items=new(source);}
        public int Count=>items.Count;
        public T this[int index]{get=>items[index];set=>items[index]=value;}
        public void Add(T item)=>items.Add(item);
        public void Clear()=>items.Clear();
        public void Reverse()=>items.Reverse();
        public void RemoveAt(int index)=>items.RemoveAt(index);
        public System.Collections.Generic.IEnumerator<T> GetEnumerator()=>items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}
namespace Il2CppSystem
{
    // Delegate stand-in for the interop Action wrapper used by the native save gates.
    public delegate void Action<T>(T argument);
}
namespace Coatsink.Common
{
    [Flags]
    public enum SaveLoadResult { None = 0, Save = 1, Load = 2, Failure = 4 }
}
namespace BepInEx { public static class Paths { public static string ConfigPath; } }
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type t,string n){} public HarmonyPatch(Type t,string n,Type[] p){} }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix:Attribute{}
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix:Attribute{}
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyFinalizer:Attribute{}
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPriority:Attribute{public HarmonyPriority(int i){}}
    public static class Priority {public const int First=800,Last=0;}
}
namespace UnityEngine
{
    public class Object { static long counter; public IntPtr Pointer { get; } = new IntPtr(++counter); }
    public class GameObject:Object
    {
        static int nextId;public int InstanceId=++nextId;public int GetInstanceID()=>InstanceId;
        public bool activeInHierarchy=true; public bool Current=true;
        readonly Dictionary<Type,Component> components=new();
        public T Add<T>(T c) where T:Component { c.gameObject=this; components[typeof(T)]=c; return c; }
        public T GetComponent<T>() where T:Component => components.TryGetValue(typeof(T),out var c)?(T)c:null;
    }
    public class Component:Object { public GameObject gameObject; public T GetComponent<T>() where T:Component=>gameObject?.GetComponent<T>(); }
    public class Transform:Component{}
    public static class Time{public static float time=100,unscaledTime=100;public static int frameCount=1;}
    public static class JsonUtility{public static string ToJson(IslandSaveData island,bool pretty)=>island.Json;}
}
// issue #153: the embarkee surface production borrows/returns. Minimal storage semantics only —
// the registrar redistribution model lives in the hero-purchase-consistency harness.
public interface IEmbarkeeOwner { IntPtr Pointer { get; } T TryCast<T>() where T : class; }
public class Embarkable : UnityEngine.Component { }
public class Embarkee : UnityEngine.Component
{
    public IEmbarkeeOwner _owner;
    public Embarkable EmbarkableTarget;
    public bool IsEmbarked;
    public bool IsTargetingEmbarkable => EmbarkableTarget != null;
    public bool enabled = true;
}
public class Archer:UnityEngine.Component { public float side; public bool Recruitable=true; public Damageable _damageable=>GetComponent<Character>()?._damageable; }
public class Character:UnityEngine.Component, IEmbarkeeOwner
{
    public Damageable _damageable;
    public T TryCast<T>() where T:class => this as T;
}
public class Persistent:UnityEngine.Component{}
public class Damageable:UnityEngine.Component
{
    public bool isDead;
    public DeathEvent OnDeath;
    public sealed class DeathEvent
    {
        internal Action<UnityEngine.GameObject> Action;
        public static implicit operator DeathEvent(Action<UnityEngine.GameObject> action)=>new(){Action=action};
        public static DeathEvent operator +(DeathEvent a,DeathEvent b)=>new(){Action=a?.Action+b?.Action};
        public static DeathEvent operator -(DeathEvent a,DeathEvent b)=>new(){Action=a?.Action-b?.Action};
    }
    public void Die(){isDead=true; OnDeath?.Action(null);}
}
public class World{public UnityEngine.Transform gameLayer;}
public class Managers{public static Managers Inst;public World world;}
public class PrefsSaveData:UnityEngine.Object
{
    public readonly System.Collections.Generic.Dictionary<string,string> contents=new();
    public void SetString(string key,string value)=>contents[key]=value;
}
public class GlobalSaveData:UnityEngine.Object
{
    public static GlobalSaveData loaded;
    // Production reads both spellings for the same storage (issue #85 compatibility surface).
    public static GlobalSaveData _loaded{get=>loaded;set=>loaded=value;}
    public static string filename="global-v35";public int currentCampaign,currentChallenge;
    public PrefsSaveData prefs=new();
    public Il2CppSystem.Collections.Generic.List<CampaignSaveData> campaigns=new();
    public Il2CppSystem.Collections.Generic.List<CampaignSaveData> challenges=new();
    public CampaignSaveData GetCurrentCampaign()=>CampaignSaveData.current;
    public void SaveAsync(Il2CppSystem.Action<Coatsink.Common.SaveLoadResult> callback){}
    public CampaignSaveData CreateNewCampaign()=>new();
    public void TryDeleteCampaignAsync(){}
    public void DeleteChallenge(int challenge){}
    public class _Save_d__89:UnityEngine.Object
    {
        public int __1__state;public GlobalSaveData __4__this;public ReturnBox @return;
        public bool MoveNext()=>false;
    }
    public class __TryDeleteCampaign_d__91:UnityEngine.Object{public int __1__state;public bool MoveNext()=>false;}
    public class __TryDeleteChallenge_d__94:UnityEngine.Object{public int __1__state;public bool MoveNext()=>false;}
    public struct ReturnBox{public Coatsink.Common.SaveLoadResult value;}
}
// Verified real-2.4 read chain (issue-85): CampaignSaveData.current.carryForward.present.
// Carry-forward stores counts/types only, never Character NativeIds; only .present is contracted.
public class CarryForwardState{public bool present;}
public class CampaignSaveData:UnityEngine.Object
{
    public static CampaignSaveData current;public IslandSaveData CurrentIsland;public CarryForwardState carryForward=new();
    // issue #153: positional island identity (populated slot i carries land == i; never-visited
    // placeholders keep land = 0). Production only reads this table.
    public Il2CppSystem.Collections.Generic.List<IslandSaveData> _islands=new();
    public void ApplyToScene(){}
}
public class IslandSaveData:UnityEngine.Object
{
    public static IslandSaveData CurrentlySavingIsland; public static bool isSavingGame;
    public int land; public DateTime realStartDateTime=new(2026,9,1); public string Json="{}";
    public bool isNew=true;public double playTimeDays;
    // Actual 2.4 TryPopObjectsToScene sorts and redistributes decay, then its cleanup (RVA
    // 734990, field store at 7349EE) disconnects island.objects in a finally on every path.
    public Il2CppSystem.Collections.Generic.List<ObjectData> objects=new();
    public bool PopReturn=true;public bool PopThrows=false;public bool PopClearsObjects=true;
    public static void Save(int c,int l,int h){} public static string GetID(Persistent p)=>"";
    public void UpdateSavedWithRevisions(){}
    public bool TryPopObjectsToScene()
    {
        try
        {
            if(PopThrows)throw new InvalidOperationException("native pop failure");
            return PopReturn;
        }
        finally { if(PopClearsObjects)objects=null; }
    }
    public static Persistent TryCreateOrFind(ObjectData d)=>null;
    public class ObjectData:UnityEngine.Object
    {
        public string uniqueID;public List<ComponentData> componentData2=new();
        public class ComponentData{public string name,type;}
    }
}
public static class Pool{public static void Despawn(UnityEngine.GameObject o,bool n){} public static void FastDespawn(UnityEngine.GameObject o,float d,bool n){} }
namespace KingdomEnhancedMod
{
    internal static class HeroShop {internal static void CancelPendingTransactions(){}}
    internal static class HeroArcherRuntime
    {
        internal static bool Enabled=true,ActivateSuccess=true;
        internal static bool IsRecruitable(Archer a)=>a!=null&&a.Recruitable&&!a._damageable.isDead;
        internal static bool TryActivatePurchased(Archer a)=>ActivateSuccess;
    }
    internal static class HeroArcherNetwork{internal static bool AllowsLocalHero=true;}
    internal static class OptionalQoLScope{internal static bool IsCurrent(UnityEngine.Component c)=>c?.gameObject!=null&&c.gameObject.activeInHierarchy&&c.gameObject.Current;}
    internal class KingdomEnhancedPlugin { internal static KingdomEnhancedPlugin Instance=new();internal Logger LogSource=new(); }
    internal class Logger{internal void LogInfo(string s){}}
}
