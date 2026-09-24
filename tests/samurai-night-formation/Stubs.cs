// 替身风格与 tests/samurai-retreat 一致（该套件同样编译抽取出的 SetGoal 前缀）。
// 只提供生产代码实际读到的成员；Unity 语义按需最小化。
namespace HarmonyLib
{
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)]
 public class HarmonyPatch:Attribute{public Type Target;public string Name;public HarmonyPatch(Type t,string n){Target=t;Name=n;}public HarmonyPatch(Type t,string n,Type[] args){Target=t;Name=n;}}
 public class HarmonyPrefix:Attribute{}public class HarmonyPostfix:Attribute{}
}
namespace UnityEngine
{
 public class Object
 {
  static long next;public IntPtr Pointer=(IntPtr)Interlocked.Increment(ref next);public bool Destroyed;
  public int GetInstanceID()=>(int)Pointer;
  public static bool operator ==(Object a,Object b)=>ReferenceEquals(a,b)||((a is null||a.Destroyed)&&(b is null||b.Destroyed));public static bool operator !=(Object a,Object b)=>!(a==b);
  public override bool Equals(object o)=>ReferenceEquals(this,o);public override int GetHashCode()=>Pointer.GetHashCode();public static implicit operator bool(Object o)=>o!=null;
 }
 public class Component:Object{public GameObject gameObject;public Transform transform=>gameObject.transform;public bool enabled=true;public T GetComponent<T>() where T:class=>gameObject.GetComponent<T>();}
 public class MonoBehaviour:Component{}
 public class GameObject:Object
 {
  readonly List<Component> components=new();public bool activeSelf=true,activeInHierarchy=true;public Transform transform;
  public GameObject(){transform=new Transform{gameObject=this};}
  public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};components.Add(c);return c;}
  public T GetComponent<T>() where T:class=>components.OfType<T>().FirstOrDefault();
 }
 public class Transform:Component{public Vector3 position,localScale=Vector3.one;}
 public struct Vector3{public float x,y,z;public Vector3(float x,float y=0,float z=0){this.x=x;this.y=y;this.z=z;}public static Vector3 one=>new(1,1,1);}
 public static class Mathf{public static float Abs(float a)=>MathF.Abs(a);public static float Min(float a,float b)=>MathF.Min(a,b);public static float Max(float a,float b)=>MathF.Max(a,b);public static bool Approximately(float a,float b)=>MathF.Abs(a-b)<.00001f;public static float Clamp(float a,float min,float max)=>Math.Clamp(a,min,max);public static float Sign(float a)=>a<0?-1:1;}
 public static class Time{public static float time=100,timeScale=1;}
}
public enum Side{Left=-1,Right=1}
public class StateMachine{public int Current=Knight.State.Stand;}
public class Character:UnityEngine.Component{public bool inert,grabbed,isStationary;}
public class Damageable:UnityEngine.Component{public bool isDead;}
public class Formation{}
public class Embarkee{public bool IsEmbarked,IsTargetingEmbarkable;public object EmbarkableTarget;}
public class Knight:UnityEngine.MonoBehaviour
{
 public static class State{public const int Stand=0,GoToWall=1,Assemble=2,Charge=3,InFormation=4,MoveToEmbark=5;}
 public int Style;public bool KnownStyle=true;public int rank=1;public Side side=Side.Right;public float _distanceFromWall=1;
 public StateMachine _fsm=new();public Mover _mover;public Damageable _damageable;public Character _character;
 public Embarkee _embarkee=new();public UnityEngine.GameObject helPuzzlePillar;public Formation Formation;
 public float _retreatSpeed=2,_runSpeed=6;public bool isRetreating,isCharging,_shouldCharge,_beingControlled,ControlRequested;
 public bool NativeWallVerdict=true;
 public Formation GetFormation()=>Formation;public bool ShouldPlayerControl()=>ControlRequested;public bool ShouldGoToWall()=>NativeWallVerdict;
}
public class Archer:UnityEngine.MonoBehaviour{}
public class Kingdom:UnityEngine.MonoBehaviour
{
 public bool isDaytime;public float Left=-100,Right=100;public WorldEatingSerpent Serpent;
 public Func<Side,KeyValuePair<Side,float>> GuardResolver;
 public KeyValuePair<Side,float> GetGuardPosition(Side side)=>GuardResolver!=null?GuardResolver(side):new(side,side==Side.Left?Left:Right);
 public float GetBorderSideIntact(Side side)=>side==Side.Left?Left:Right;
}
public class Managers{public static Managers Inst=new();public Kingdom kingdom=new GameObjectHolder().Kingdom;private class GameObjectHolder{public Kingdom Kingdom=new UnityEngine.GameObject().AddComponent<Kingdom>();}}
public class WorldEatingSerpent{public float Position;public float AttackDistance=3;}
public static class NetworkBigBoss{public static bool HasWorldAuth=true;}
public class Mover:UnityEngine.Component
{
 public int PositionCalls,DirectionWrites;public float _goalPosition,_goalSpeed;public Action OnFloatGoal;
 public void SetGoal(float goal,float speed){PositionCalls++;_goalPosition=goal;_goalSpeed=speed;OnFloatGoal?.Invoke();}
 public void SetDirection(int direction){DirectionWrites++;transform.localScale=new(direction,1,1);}
}
namespace KingdomEnhancedMod
{
 public static class ModConfig{public class Option{public bool Value=true;}public static Option Enabled=new();}
 public static class PatchRoles_KnightStyle{public static bool TryGetResolvedStyleIndex(Knight k,out int style){style=k.Style;return k.KnownStyle;}}
 // The real lease probe lives in PatchRoles_SamuraiPowerDash and is covered by tests/samurai-motion.
 public static class PatchRoles_SamuraiPowerDash{public static bool MotionActive;internal static bool HasActiveMotion(Knight k)=>MotionActive;}
 public static class UnitScanCache{public static Knight[] Knights=Array.Empty<Knight>();public static int Calls;internal static Knight[] GetKnights(float maxAgeSec=3f){Calls++;return Knights;}}
 public class KingdomEnhancedPlugin{public static KingdomEnhancedPlugin Instance=new();public Logger LogSource=new();public class Logger{public readonly List<string> Infos=new();public readonly List<string> Errors=new();public void LogInfo(string text)=>Infos.Add(text);public void LogWarning(string text){}public void LogError(string text)=>Errors.Add(text);}}
}
