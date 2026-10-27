using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        private static int next=10;
        public readonly IntPtr Pointer=(IntPtr)(++next);
        public string name;
        public static GameObject Instantiate(GameObject source,Transform parent,bool worldPositionStays)
        {
            if (Resources.FailAfter-- == 0) throw new Exception("injected instantiate failure");
            var go=new GameObject(source.name); go.transform.SetParent(parent,false);
            if (source.GetComponent<Chest>()!=null) { if(!Resources.OmitChest)go.AddComponent<Chest>(); go.AddComponent<Collider2D>();go.AddComponent<FixedTransform>(); }
            var src=source.GetComponent<Persistent>(); if(src!=null) go.AddComponent<Persistent>().path=src.path;
            return go;
        }
        public static void Destroy(Object value) { if(value is GameObject go) { go.SetActive(false); go.transform.SetParent(null,false); GameObject.All.Remove(go); } }
        public int GetInstanceID() => Pointer.ToInt32();
    }
    public class Component:Object
    {
        public GameObject gameObject;
        public Transform transform=>gameObject.transform;
        public T GetComponent<T>() where T:Component=>gameObject.GetComponent<T>();
        public T[] GetComponentsInChildren<T>(bool inactive=false) where T:Component=>gameObject.GetComponentsInChildren<T>(inactive);
    }
    public class Behaviour:Component { public bool enabled=true; }
    public class Collider2D:Behaviour { public Bounds bounds; }
    public class BoxCollider2D:Collider2D { }
    public class Transform:Component
    {
        public Transform parent;
        public Vector3 localPosition,localScale=new Vector3(1,1,1);
        public Vector3 position { get {var p=parent==null?new Vector3():parent.position;return new Vector3(p.x+localPosition.x,p.y+localPosition.y,p.z+localPosition.z);}set{var p=parent==null?new Vector3():parent.position;localPosition=new Vector3(value.x-p.x,value.y-p.y,value.z-p.z);} }
        public Vector3 InverseTransformPoint(Vector3 world) {var p=position;return new Vector3(world.x-p.x,world.y-p.y,world.z-p.z);}
        internal readonly List<Transform> children=new();
        public void SetParent(Transform target,bool worldPositionStays) { parent?.children.Remove(this); parent=target; parent?.children.Add(this); gameObject.Refresh(); }
        public bool IsChildOf(Transform target) { for(var p=this;p!=null;p=p.parent) if(p==target)return true; return false; }
    }
    public class GameObject:Object
    {
        public static readonly List<GameObject> All=new();public static int Scans;
        private readonly List<Component> components=new();
        public readonly Transform transform;
        public bool activeSelf=true;
        public bool activeInHierarchy=>activeSelf && (transform.parent==null || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string name) { this.name=name;transform=new Transform{gameObject=this};components.Add(transform);All.Add(this); }
        public void SetActive(bool active) { activeSelf=active;Refresh(); }
        internal void Refresh() { var p=GetComponent<Persistent>(); if(p!=null && activeInHierarchy && p._persistInstance) IslandSaveData.registeredPersistents.Add(p); foreach(var t in transform.children)t.gameObject.Refresh(); }
        public T AddComponent<T>() where T:Component,new() { var c=new T{gameObject=this};components.Add(c);Refresh();return c; }
        public T GetComponent<T>() where T:Component=>components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool inactive=false) where T:Component { Scans++;return All.Where(g=>g.transform.IsChildOf(transform) && (inactive || g.activeInHierarchy)).SelectMany(g=>g.components.OfType<T>()).ToArray(); }
    }
    public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z=0){this.x=x;this.y=y;this.z=z;} }
    public struct Bounds { public Vector3 min,max; }
    public static class Time { public static float time=1,timeScale=1,unscaledTime=1; }
    public static class Resources { public static GameObject Prefab; public static int FailAfter=int.MaxValue; public static bool OmitChest; public static T Load<T>(string path) where T:Object => Prefab as T; }
}
public class Game:UnityEngine.Object { public enum State{Menu,Playing,Loading} public State state=State.Playing; public int currentLand=11; public LevelConfig currentLevelConfig=new(); }
public class LevelConfig:UnityEngine.Object { public float incomeMultiplier=1; }
public class Level:Component { public BoxCollider2D GroundCollider; public static bool isGenerating; public void OnLevelLoaded(bool fromSave){} }
public class World:UnityEngine.Object { public Transform gameLayer; public static BoxCollider2D GroundCollider; }
public class Managers { public static Managers Inst; public Game game;public World world;public Level level;public Kingdom kingdom;public PayableManager payables; }
public class Kingdom { public HashSet<Portal> AllPortals=new();public Player playerOne,playerTwo; }
public class Portal:Component { }
public class Player:Component { }
public class Payable:Component { }
public class PayableManager { public Payable[] AllPayables=Array.Empty<Payable>(); }
public static class NetworkBigBoss { public static bool IsOnline;public static bool HasWorldAuth=true; }
public class BiomeHolder { public static BiomeHolder Inst=new();public const int GreeceBiomeIndex=4;public int BiomeIndex=4; }
public class GlobalSaveData:UnityEngine.Object { public static string filename="global-v16";public static GlobalSaveData loaded;public static GlobalSaveData _loaded=>loaded;public PrefsSaveData prefs=new();public int currentCampaign,currentChallenge;public bool InChallenge;public List<CampaignSaveData> campaigns=new();public CampaignSaveData GetCurrentCampaign()=>campaigns[currentCampaign];
    public void SaveAsync(Il2CppSystem.Action<Coatsink.Common.SaveLoadResult> callback){}
    public class _Save_d__89:UnityEngine.Object { public int __1__state;public GlobalSaveData __4__this;public Return @return;public bool MoveNext()=>false; }
    public struct Return { public Coatsink.Common.SaveLoadResult value; }
}
public class CampaignSaveData:UnityEngine.Object
{
    public static CampaignSaveData current; public int CurrentLand=11,GetterCalls;
    public readonly Dictionary<int,IslandSaveData> islands=new(); public readonly List<IslandSaveData> _islands=new();
    public IslandSaveData GetIsland(int land) { GetterCalls++;while(_islands.Count<=land)_islands.Add(null);if(_islands[land]==null)_islands[land]=new IslandSaveData();islands[land]=_islands[land];return _islands[land]; }
}
public class PrefsSaveData:UnityEngine.Object
{
    public Dictionary<string,string> contents=new();public List<SrzEntry> srzEntries=new();public bool FailSet;
    public void SetString(string key,string value) { if(FailSet)throw new Exception("injected set failure");contents[key]=value; }
    public void PrepareBeforeSave(){}
    public void NativePrepare() { srzEntries=contents.Select(r=>new SrzEntry{key=r.Key,val=r.Value}).ToList(); }
    public class SrzEntry { public string key,val; }
}
public class IslandSaveData:UnityEngine.Object
{
    public static bool isSavingGame;public static IslandSaveData CurrentlySavingIsland;
    public static HashSet<Persistent> registeredPersistents=new();public static Dictionary<Persistent,string> idsByObject=new();public static Dictionary<string,Persistent> objectsByID=new();
    public static void Save(int campaign,int land,int challenge){}
    public void UpdateSavedWithRevisions(){}
    public int land;public List<ObjectData> objects=new();
    public class ObjectData { public string uniqueID,name,prefabPath;public Vector3 localPosition; }
    public void NativeCapture()
    {
        objects.Clear();
        foreach(var p in registeredPersistents.Where(p=>p.ShouldPersist()))
        { if(!idsByObject.TryGetValue(p,out var id))idsByObject[p]=id=p.gameObject.name+p.GetInstanceID(); objectsByID[id]=p;objects.Add(new ObjectData{uniqueID=id,name=p.gameObject.name,prefabPath=p.path,localPosition=p.transform.localPosition}); }
    }
}
public class Persistent:Component { public string path;public bool _persistInstance=true;public bool ShouldPersist()=>_persistInstance && gameObject.activeInHierarchy;public void RecalcCache(){} public void DontPersistInstance(bool children=false){_persistInstance=false;IslandSaveData.registeredPersistents.Remove(this);} }
public class FixedTransform:Behaviour
{
    public float y=.875f,z=1.5f;
    public void SetFixY(float value){y=value;Fix();}
    public void Fix(){var p=transform.localPosition;transform.localPosition=new Vector3(p.x,y,z);}
}
public enum CurrencyType{Coins,Gems}
public class Chest:Behaviour
{
    public class _SpillCurrency_d__26:UnityEngine.Object
    {
        public int __1__state; public Chest __4__this; public int Drops, NativeCalls, FadeCalls, Remaining; public bool ThrowNative;
        public bool MoveNext()
        {
            var call=KingdomEnhancedMod.MountIslandGemRewards.BeginSpill(this);
            try
            {
                NativeCalls++;
                if(ThrowNative)throw new Exception("injected native spill");
                bool result;
                if(__1__state==0) { __1__state=1;result=true; }
                else if(__1__state==1 || __1__state==2)
                {
                    if(__1__state==1) { Remaining=__4__this.currencyAmount;__4__this.currencyAmount=0;__4__this.GetComponent<Persistent>().DontPersistInstance(false); }
                    if(Remaining>0) { Drops++;Remaining--;__1__state=2; } else __1__state=3;
                    result=true;
                }
                else { FadeCalls++;__1__state=-1;result=false; }
                KingdomEnhancedMod.MountIslandGemRewards.EndSpill(call,ref result); return result;
            }
            catch { KingdomEnhancedMod.MountIslandGemRewards.FailSpill(call);throw; }
        }
    } public int currencyAmount=1;public bool isFixedAmount=true;public CurrencyType currencyType=CurrencyType.Gems; }
namespace KingdomEnhancedMod
{
    public class Setting { public bool Value=true; }
    public static class ModConfig { public static Setting Enabled=new(),CrossWorldMountsEnabled=new(); }
    internal static class ExtensionIslandRuntime
    {
        internal static bool BindingReady=true; internal static System.Action OnResolve; internal static int Calls;
        internal static bool TryResolveRawOwner(out GlobalSaveData global,out int slot,out CampaignSaveData owner)
        {
            Calls++;OnResolve?.Invoke(); global=GlobalSaveData.loaded;slot=global==null?-1:global.currentCampaign;owner=CampaignSaveData.current;
            return BindingReady && global!=null && slot>=0 && slot<global.campaigns.Count && global.campaigns[slot]==owner;
        }
    }
    public static class CoinCourierGround { public static bool Standable=true;public static bool TryStandable(float x,out float y){y=0;return Standable;} }
    public class KingdomEnhancedPlugin { public static KingdomEnhancedPlugin Instance=new();public Logger LogSource=new(); }
    public class Logger { public void LogWarning(string text){Console.WriteLine("DIAG "+text);} }
}

namespace Il2CppSystem { public class Action<T> { private readonly System.Action<T> action;public Action(System.Action<T> action){this.action=action;}public void Invoke(T value)=>action(value); } }
namespace Coatsink.Common { [Flags] public enum SaveLoadResult { Save=8,Failure=128 } }
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]public class HarmonyPatch:Attribute { public HarmonyPatch(Type type,string name){} public HarmonyPatch(Type type,string name,Type[] args){} }
    [AttributeUsage(AttributeTargets.Method)]public class HarmonyPrefix:Attribute{}
    [AttributeUsage(AttributeTargets.Method)]public class HarmonyPostfix:Attribute{}
    [AttributeUsage(AttributeTargets.Method)]public class HarmonyFinalizer:Attribute{}
}

namespace Il2CppInterop.Runtime.InteropTypes { public class Il2CppObjectBase { public T TryCast<T>() where T:class=>this as T; } }
public static class Filer { public static string folder="/test/save";public static object agent=new DotNetAgent{folder="/test/save"};public class DotNetAgent:Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase { public string folder; } }
namespace UnityEngine { public static class JsonUtility { public static string ToJson(object value,bool pretty)=>System.Text.Json.JsonSerializer.Serialize(value,new System.Text.Json.JsonSerializerOptions{IncludeFields=true}); } }
