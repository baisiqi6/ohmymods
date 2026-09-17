using System;
using System.Collections.Generic;
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch:Attribute { public HarmonyPatch(Type t,string n,Type[] p){} }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix:Attribute{}
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix:Attribute{}
}
namespace UnityEngine
{
    public class Object { private static long next; public IntPtr Pointer=(IntPtr)(++next); }
    public class GameObject:Object
    {
        private static int nextId; public int Id=++nextId; public bool activeInHierarchy=true; public bool ThrowOnRead;
        private readonly Dictionary<Type,Component> components=new();
        public int GetInstanceID()=>Id;
        public T Add<T>(T value) where T:Component {value.gameObject=this;components[typeof(T)]=value;return value;}
        public T GetComponent<T>() where T:Component {if(ThrowOnRead)throw new Exception("unknown native component");return components.TryGetValue(typeof(T),out var c)?(T)c:null;}
    }
    public class Component:Object {public GameObject gameObject;}
    public static class Time {public static float unscaledTime=100;}
}
public class GuardSlot:UnityEngine.Component {public Archer archer;}
public class Knight:UnityEngine.Component{}
public class Archer:UnityEngine.Component
{
    public bool Purchased,inGuardSlot,ThrowOnPurchase;
    public GuardSlot _guardSlot;
    public int ExitCount;
    public Action NativeExit;
    public bool IsAvailableForJob(UnityEngine.GameObject job)=>true;
    public void AssignJob(UnityEngine.GameObject job){}
    public void SetGuardSlot(GuardSlot slot){}
    public void ExitGuardSlot(){ExitCount++;if(NativeExit!=null){NativeExit();return;}if(_guardSlot!=null)_guardSlot.archer=null;_guardSlot=null;inGuardSlot=false;}
}
namespace KingdomEnhancedMod
{
    internal static class HeroArcherRuntime {internal static bool Enabled=true;}
    internal static class HeroRecruitment {internal static bool IsPurchased(Archer a){if(a.ThrowOnPurchase)throw new Exception();return a.Purchased&&a.gameObject.activeInHierarchy;}}
    internal class KingdomEnhancedPlugin {internal static KingdomEnhancedPlugin Instance=new();internal Log LogSource=new();}
    internal class Log {internal void LogWarning(string message){}}
}
