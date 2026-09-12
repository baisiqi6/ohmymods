using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch:Attribute {public HarmonyPatch(Type t,string n){}}
 [AttributeUsage(AttributeTargets.Method)] public class HarmonyPostfix:Attribute {}
}
namespace UnityEngine {
 public class Object {static int next=1; public IntPtr Pointer=new IntPtr(next++); public int GetInstanceID()=>(int)Pointer; public T Cast<T>()=>(T)(object)this;}
 public class GameObject:Object {public bool activeInHierarchy=true; public readonly Dictionary<Type,Component> Parts=new(); public Transform transform; public GameObject(){transform=new Transform();transform.gameObject=this;} public T Add<T>() where T:Component,new(){var c=new T();c.gameObject=this;Parts[typeof(T)]=c;return c;}}
 public class Component:Object {public GameObject gameObject; public Transform transform=>this as Transform??gameObject?.transform; public T GetComponent<T>() where T:Component=>gameObject!=null&&gameObject.Parts.TryGetValue(typeof(T),out var c)?(T)c:null;}
 public class Behaviour:Component {public bool enabled=true;public bool isActiveAndEnabled=>enabled&&gameObject.activeInHierarchy;}
 public class Transform:Component {public Transform parent;public bool IsChildOf(Transform p){for(var t=this;t!=null;t=t.parent)if(t.Pointer==p.Pointer)return true;return false;}}
 public static class Time {public static float realtimeSinceStartup,time,unscaledTime;}
}
public enum Side {Left=-1,Right=1}
public class FSM {public int Current;}
public class Character {public bool inert,grabbed,isStationary;}
public class Damageable {public bool isDead;}
public class Embarkee:UnityEngine.Component {public Embarkable EmbarkableTarget;public bool CanEmbark=true,IsStowaway,IsEmbarked;}
public class Embarkable:UnityEngine.Component {}
public class Knight:UnityEngine.Behaviour {public int style=3;public Side side=Side.Right;public Damageable _damageable=new(); public Character _character=new();public Embarkee _embarkee; public FSM _fsm=new();public bool isCharging,_shouldCharge,_beingControlled,_harmless,controlled;public object helPuzzlePillar; public Formation formation;public Formation GetFormation()=>formation; public bool ShouldPlayerControl()=>controlled;public bool CanJoinFormation(Formation.FormationType t,Side s)=>true;public static class State {public const int Stand=0,GoToWall=1,Assemble=2;}}
public class FleetBoat:UnityEngine.Behaviour,Formation.IFormationUnit {public Embarkable _embarkable;public Formation _currentFormation;public Side Side=Side.Right;public int _numSquads=1,_boatNumber=1;public bool nativeCanJoin=true,IsAccessible=true;public bool HasFormation=>_currentFormation!=null;public FSM _fsm=new(); public bool CanJoinFormation(Formation.FormationType t,Side s)=>nativeCanJoin&&FleetGreekSquads.CanRecruitBoat(this,t,s); public static class State {public const int Idle=0,InFormation=1,WaitingForSquad=2,WaitingForSailAway=3,Attacking=4,ReturningToBase=5;public static bool CanJoinFormation(int s)=>s==Idle;}}
public class Player:UnityEngine.Behaviour {}
public class Kingdom {public List<FleetBoat> FleetBoats=new();}
public class Formation:UnityEngine.Behaviour {public interface IFormationUnit{} public enum FormationType{PlayerFormation,ActiveShieldWall} public readonly List<FleetBoat> members=new(); public int Writes; public bool IsInFormation(IFormationUnit b)=>members.Contains((FleetBoat)b); public void UnregisterUnit(IFormationUnit u){var b=(FleetBoat)u;Writes++;members.Remove(b);if(b._currentFormation==this)b._currentFormation=null;} }
public class EmbarkeeSlot {public Embarkable Embarkable;}
public class EmbarkableRegistrar {public List<Embarkee> _tempUnitCache=new();public List<EmbarkeeSlot> _tempSlotCache=new();public int CalculateEmbarkeeScore(int a,int b)=>0;}
public class World {public UnityEngine.Transform gameLayer;}
public class Managers {public static Managers Inst=new();public World world=new();}
public static class NetworkBigBoss {public static bool HasWorldAuth=true;}
namespace KingdomEnhancedMod {
 public class Flag {public bool Value=true;}
 public static class ModConfig {public static Flag Enabled=new();}
 public static class PatchRoles_KnightStyle {public static bool TryGetResolvedStyleIndex(Knight k,out int style){style=k?.style??-1;return style>=0;}}
 public static class UnitScanCache {public static List<Knight> Roster=new();public static List<Knight> GetKnights()=>Roster;}
 public class Log {public void LogWarning(string s){}public void LogInfo(string s){}}
 public class KingdomEnhancedPlugin {public static KingdomEnhancedPlugin Instance=new();public Log LogSource=new();}
}
