using System;
using System.Collections.Generic;
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
    public class Object { static long counter; public IntPtr Pointer = (IntPtr)(++counter); }
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
public class Archer:UnityEngine.Component { public float side; public bool Recruitable=true; public Damageable _damageable=>GetComponent<Character>()?._damageable; }
public class Character:UnityEngine.Component { public Damageable _damageable; }
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
public class GlobalSaveData{public static GlobalSaveData loaded;public static string filename="global-v35";public int currentCampaign,currentChallenge;}
public class CampaignSaveData:UnityEngine.Object{public static CampaignSaveData current;public IslandSaveData CurrentIsland;public void ApplyToScene(){}}
public class IslandSaveData:UnityEngine.Object
{
    public static IslandSaveData CurrentlySavingIsland; public static bool isSavingGame;
    public int land; public DateTime realStartDateTime=new(2026,9,1); public string Json="{}";
    public bool isNew=true;public double playTimeDays;
    public List<ObjectData> objects=new();
    public static void Save(int c,int l,int h){} public static string GetID(Persistent p)=>"";
    public bool TryPopObjectsToScene()=>true;public static Persistent TryCreateOrFind(ObjectData d)=>null;
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
