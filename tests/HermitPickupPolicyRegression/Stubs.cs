namespace HarmonyLib
{
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method)]
 public class HarmonyPatch:Attribute {public Type Target;public string Method;public HarmonyPatch(Type target,string method){Target=target;Method=method;}}
 public class HarmonyPostfix:Attribute{}
 public class HarmonyPrefix:Attribute{}
}
namespace UnityEngine
{
 public class Object
 {
  static int next;
  public int Id=Interlocked.Increment(ref next);
  public IntPtr Pointer {get;set;}
  public Object(){Pointer=(IntPtr)Id;}
  public int GetInstanceID()=>Id;
 }
 public class Component:Object {public GameObject gameObject;public Transform transform=>gameObject?.transform;}
 public struct Scene {public int handle;}
 public class GameObject:Object
 {
  public string name="unit";public bool activeInHierarchy=true;public Transform transform;public Scene scene=new(){handle=10};
  readonly List<Component> components=new();public bool ThrowComponent;
  public GameObject(){transform=new Transform{gameObject=this};}
  public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};components.Add(c);return c;}
  public T GetComponent<T>() where T:Component{if(ThrowComponent)throw new InvalidOperationException("injected component read");return components.OfType<T>().FirstOrDefault();}
 }
 public class Transform:Component {public Transform parent;public bool IsChildOf(Transform t){for(Transform current=this;current!=null;current=current.parent)if(current==t)return true;return false;}}
 public static class Time {public static float unscaledTime;}
}
public enum PickUpPolicy {Anybody=0,AnybodyExceptDropper=1,OnlyClaimer=2,AnyPlayer=3,Nobody=4,Blocked=5,EnemyOnly=6,WorkerOnly=7}
public class Droppable:UnityEngine.Component
{
 PickUpPolicy enemy;
 public int EnemyWrites,EnemyReads,GeneralWrites,OriginalWrites;
 public bool ThrowRead,ThrowWrite;
 public PickUpPolicy CurrentEnemyPolicy {get{EnemyReads++;if(ThrowRead)throw new InvalidOperationException("injected policy read");return enemy;}set{if(ThrowWrite)throw new InvalidOperationException("injected policy write");EnemyWrites++;enemy=value;}}
 PickUpPolicy original, general=PickUpPolicy.AnyPlayer;
 public PickUpPolicy _originalEnemyPolicy {get=>original;set{OriginalWrites++;original=value;}}
 public PickUpPolicy pickUpPolicy {get=>general;set{GeneralWrites++;general=value;}}
 public void NativePolicy(PickUpPolicy policy)=>enemy=policy;
 public void NativeOriginal(PickUpPolicy policy)=>original=policy;
 public void OnEnable(){}
 public void OnDisable()=>enemy=original; // Native reset, deliberately excluded from mod write count.
}
public class Hermit:UnityEngine.Component{}
public static class NetworkBigBoss {public static bool HasWorldAuth=true;}
public class World:UnityEngine.Object {public UnityEngine.Transform gameLayer=new UnityEngine.GameObject().transform;}
public class Game {public State state=State.Playing;public enum State {Playing,NetworkClientPlaying,Menu,Loading}}
public class Managers {public static Managers Inst;public World world=new();public Game game=new();}
namespace KingdomEnhancedMod
{
 public static class ModConfig {public class Flag {public bool Value=true;}public static Flag Enabled=new();public static Flag PetGuardEnabled=new();}
 public class KingdomEnhancedPlugin
 {
  public static KingdomEnhancedPlugin Instance=new();public Log LogSource=new();
  public class Log {public List<string> Info=new(),Warning=new();public void LogInfo(string text)=>Info.Add(text);public void LogWarning(string text)=>Warning.Add(text);}
 }
}
