namespace HarmonyLib
{
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)] public class HarmonyPatch:Attribute {public HarmonyPatch(){}public HarmonyPatch(Type t,string method){}}
 public class HarmonyPostfix:Attribute{}
}
namespace UnityEngine
{
 public class Object
 {
  static int next; public int Id=++next;public IntPtr Pointer;public bool Invalid;
  public Object(){Pointer=(IntPtr)Id;}
  public int GetInstanceID(){if(Invalid)throw new InvalidOperationException("destroyed");return Id;}
 }
 public class Component:Object {public GameObject gameObject; public Transform transform=>gameObject.transform;public T GetComponent<T>()where T:Component=>gameObject.GetComponent<T>();}
 public class GameObject:Object
 {
  public string name; public Scene scene=new(){valid=true};public Transform transform;readonly List<Component> components=new();
  public GameObject(string name="actor"){this.name=name;transform=new Transform{gameObject=this};}
  public T AddComponent<T>()where T:Component,new(){var c=new T{gameObject=this};components.Add(c);return c;}
  public T GetComponent<T>()where T:Component=>components.OfType<T>().FirstOrDefault();
 }
 public struct Scene {public bool valid;public bool IsValid()=>valid;}
 public struct Vector3
 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public float this[int i]{get=>i==0?x:i==1?y:z;set{if(i==0)x=value;else if(i==1)y=value;else z=value;}}
 }
 public class Transform:Component
 {
  Vector3 scale=new(1,1,1);public int Writes;public bool FailWrite,CommitBeforeFailure;public int FailReads;
  public Vector3 localScale{get{if(FailReads-->0)throw new InvalidOperationException("read fault");return scale;}set{if(FailWrite){FailWrite=false;if(CommitBeforeFailure){scale=value;Writes++;}throw new InvalidOperationException("write fault");}scale=value;Writes++;}}
 }
 public class Animator {public bool Fisher;public bool GetBool(int key)=>Fisher;}
 public static class Time{public static int frameCount;}
}
public class Mover:UnityEngine.Component{}
public class Ninja:UnityEngine.Component
{
 public const int APIsFisher=17;public bool _isFisher;public UnityEngine.Animator _animator=new();
 public void OnStyleSwap(){}public void SetAnimation(){}public void Persistent_IBehaviour_ApplyData(){}public void DeserializeFromData(){}
}
public class Banker:UnityEngine.Component {public Mover _mover;public void OnEnable(){}}
public class Dog:UnityEngine.Component{}
public class Hermit:UnityEngine.Component
{
 public enum HermitType{Baker,Horn,Horse,Ballista,Knight,Fire,Other}public HermitType Type;public Mover mover;public void OnEnable(){}public void OnDestroy(){}
}
public class BiomeHolder {public static BiomeHolder Inst=new();public const int GreeceBiomeIndex=3;int index=3;public bool FailRead;public int BiomeIndex{get{if(FailRead)throw new InvalidOperationException("biome read fault");return index;}set=>index=value;}}
public static class NetworkBigBoss{public static bool HasWorldAuth=true;}
namespace KingdomEnhancedMod
{
 public static class ModConfig{public static Setting Enabled=new();public class Setting{public bool Value=true;}}
 public class KingdomEnhancedPlugin{public static KingdomEnhancedPlugin Instance;public Logger LogSource=new();public class Logger{public void LogError(object message)=>throw new Exception("adapter error: "+message);public void LogWarning(object message){}public void LogInfo(object message){}}}
 public static class ScaleRegistryHolder
 {
  public static void Register(Mover m,float y)=>GreekScaleScope.Register(m,y);
  public static bool TryGet(Mover m,out float y)=>GreekScaleScope.TryGet(m,out y);
  public static void Unregister(Mover m)=>GreekScaleScope.Unregister(m);
 }
}
