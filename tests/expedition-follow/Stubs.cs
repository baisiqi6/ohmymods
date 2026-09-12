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
  static long next;public IntPtr Pointer=(IntPtr)Interlocked.Increment(ref next);public bool Destroyed;public static int Forbidden;
  public int GetInstanceID()=>(int)Pointer;
  public static bool operator ==(Object a,Object b)=>ReferenceEquals(a,b)||((a is null||a.Destroyed)&&(b is null||b.Destroyed));public static bool operator !=(Object a,Object b)=>!(a==b);
  public override bool Equals(object o)=>ReferenceEquals(this,o);public override int GetHashCode()=>Pointer.GetHashCode();public static implicit operator bool(Object o)=>o!=null;
  public T Cast<T>() where T:class=>this as T;
  public static T[] FindObjectsOfType<T>(){Forbidden++;throw new Exception("Unexpected scene scan");}
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
 public class Transform:Component{public Vector3 position,localScale=Vector3.one;public Transform parent;public bool IsChildOf(Transform root){for(var t=this;t!=null;t=t.parent)if(t.Pointer==root.Pointer)return true;return false;}}
 public struct Vector3{public float x,y,z;public Vector3(float x,float y=0,float z=0){this.x=x;this.y=y;this.z=z;}public static Vector3 one=>new(1,1,1);}
 public static class Mathf{public static float Abs(float a)=>MathF.Abs(a);public static float Min(float a,float b)=>MathF.Min(a,b);public static float Max(float a,float b)=>MathF.Max(a,b);public static bool Approximately(float a,float b)=>MathF.Abs(a-b)<.00001f;public static float Clamp(float a,float min,float max)=>Math.Clamp(a,min,max);public static float Sign(float a)=>a<0?-1:1;}
 public static class Time{public static float time=100,timeScale=1;}
}
namespace Coatsink.Common
{
 public class IHaglet:UnityEngine.Object{}
 public class Haglet:IHaglet{public int latestGoto=2;public bool started=true;}
 public class Wait{public readonly object Identity=new();}
}
public enum Side{Left=-1,Right=1}
public class StateMachine{public int Current=Knight.State.Stand;}
public class Character:UnityEngine.Component{public bool inert,grabbed,isStationary;}
public class Damageable:UnityEngine.Component{public bool isDead;}
public class Formation{}
public class GuardSlot{}
public class Embarkee{public bool IsEmbarked,IsTargetingEmbarkable;public object EmbarkableTarget;}
public class Knight:UnityEngine.MonoBehaviour
{
 public static class State{public const int Stand=0,GoToWall=1,Assemble=2,Charge=3,InFormation=4,MoveToEmbark=5;}
 public int Style;public Side side=Side.Right;public bool isCharging,_shouldCharge,isRetreating,_beingControlled,ControlRequested;
 public StateMachine _fsm=new();public Character _character;public Damageable _damageable;public Mover _mover;
 public Embarkee _embarkee=new();public UnityEngine.GameObject helPuzzlePillar;public Formation Formation;
 public Formation GetFormation()=>Formation;public bool ShouldPlayerControl()=>ControlRequested;
}
public class Archer:UnityEngine.MonoBehaviour
{
 public Knight _knight;public Mover _mover;public Character _character;public Damageable _damageable;public Embarkee _embarkee=new();
 public Coatsink.Common.IHaglet behaviour=new Coatsink.Common.Haglet();public GuardSlot _guardSlot;public bool inGuardSlot,ControlRequested,_beingControlled,GoToWall=true;
 public Formation Formation;public Formation GetFormation()=>Formation;public bool ShouldPlayerControl()=>ControlRequested;public bool ShouldGoToWall()=>GoToWall;
 public bool IsGrabbed()=>_character?.grabbed??false;public bool HasEmbarkableTarget()=>_embarkee?.EmbarkableTarget!=null;public bool IsInFormation()=>Formation!=null;
}
public class Kingdom:UnityEngine.MonoBehaviour{public bool isDaytime,ThrowBorder;public float Left=-100,Right=100;public float GetBorderSideIntact(Side side)=>ThrowBorder?throw new InvalidOperationException("wall unavailable"):side==Side.Left?Left:Right;}
public class World:UnityEngine.MonoBehaviour{public UnityEngine.Transform gameLayer=new UnityEngine.GameObject().transform;}
public class Managers{public World world=new UnityEngine.GameObject().AddComponent<World>();public static Managers Inst=new();public Kingdom kingdom=new GameObjectHolder().Kingdom;private class GameObjectHolder{public Kingdom Kingdom=new UnityEngine.GameObject().AddComponent<Kingdom>();}}
public static class NetworkBigBoss{public static bool HasWorldAuth=true;}
public class Mover:UnityEngine.Component
{
 public delegate bool ObjectPrefixDelegate(Mover mover,UnityEngine.GameObject goal,float speed,ref float offset,OffsetMode mode);
 public static ObjectPrefixDelegate Intercept;
 public enum GoalMode{Off,Position,Object}public enum OffsetMode{Distance,Formation,Strict}
 public GoalMode goalMode;public UnityEngine.GameObject _goalObject;public float _goalPosition,_goalSpeed,_goalOffset,_pauseTimeout;public OffsetMode _goalOffsetMode;
 public float _moveSpeed;public bool movingToGoal;
 public int ObjectCalls,PositionCalls,Stops,Unpauses;public Action OnObjectGoal;public bool ThrowOnObjectGoal;
 public Coatsink.Common.Wait LastWait;
 public Coatsink.Common.Wait SetGoal(UnityEngine.GameObject goal,float speed,float offset=0,OffsetMode mode=OffsetMode.Distance)
 {if(Intercept!=null&&!Intercept(this,goal,speed,ref offset,mode))return null;ObjectCalls++;if(ThrowOnObjectGoal)throw new InvalidOperationException("Injected native goal failure");_goalObject=goal;_goalSpeed=speed;_goalOffset=offset;_goalOffsetMode=mode;goalMode=GoalMode.Object;LastWait=new();OnObjectGoal?.Invoke();return LastWait;}
 public Coatsink.Common.Wait SetGoal(float goal,float speed){PositionCalls++;_goalPosition=goal;_goalSpeed=speed;_goalObject=null;goalMode=GoalMode.Position;movingToGoal=true;return LastWait=new();}
 public void Stop(){Stops++;SetSpeed(0);}public void SetSpeed(float speed){goalMode=GoalMode.Off;movingToGoal=false;_moveSpeed=speed;}public void UnPause(){Unpauses++;_pauseTimeout=0;}
 public float DynamicDestination=>goalMode==GoalMode.Object?_goalObject.transform.position.x+_goalOffset*(_goalOffsetMode==OffsetMode.Formation?_goalObject.transform.localScale.x:1):_goalPosition;
}
namespace KingdomEnhancedMod
{
 public static class ModConfig{public class Option{public bool Value=true;}public static Option Enabled=new();}
 public static class PatchRoles_KnightStyle{public static float GetFollowerAnchorPullback(Knight k)=>k.Style==1?6.5f:4.2f;}
 public class KingdomEnhancedPlugin{public static KingdomEnhancedPlugin Instance=new();public Logger LogSource=new();public class Logger{public readonly List<string> Lines=new();public bool Throw;public void LogInfo(string text){if(Throw)throw new Exception("Logger failure");Lines.Add(text);}public void LogWarning(string text)=>LogInfo(text);public void LogError(string text)=>LogInfo(text);}}
}
