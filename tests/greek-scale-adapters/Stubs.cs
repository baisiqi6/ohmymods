namespace HarmonyLib
{
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)] public class HarmonyPatch:Attribute {public HarmonyPatch(){}public HarmonyPatch(Type t){}public HarmonyPatch(Type t,string method){}}
 public class HarmonyPostfix:Attribute{} public class HarmonyPrefix:Attribute{}
}
namespace UnityEngine
{
 public class Object
 {
  static int next;public int Id=++next;public IntPtr Pointer;public Object(){Pointer=(IntPtr)Id;}public int GetInstanceID()=>Id;
 }
 public class Component:Object{public GameObject gameObject;public Transform transform=>gameObject.transform;}
 public class GameObject:Object
 {
  public string name="TestCoin";public Scene scene=new(){valid=true};public Transform transform;
  public GameObject(){transform=new Transform{gameObject=this};}
  public T AddComponent<T>()where T:Component,new()=>new T{gameObject=this};
 }
 public struct Scene{public bool valid;public bool IsValid()=>valid;}
 public struct Vector3
 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 one=>new(1,1,1);
  public float this[int i]{get=>i==0?x:i==1?y:z;set{if(i==0)x=value;else if(i==1)y=value;else z=value;}}
 }
 public class Transform:Component
 {
  Vector3 scale=Vector3.one;public Vector3 position;public int Writes;
  public Vector3 localScale{get=>scale;set{scale=value;Writes++;}}
 }
 public static class Mathf{public static bool Approximately(float a,float b)=>MathF.Abs(a-b)<.00001f;}
 public static class Time{public static int frameCount;}
}
namespace Coatsink.Common{public static class SingletonMonoBehaviour<T>where T:new(){public static T Inst=>typeof(T)==typeof(Managers)?(T)(object)Managers.Inst:new();}}
public class Mover:UnityEngine.Component{}
public class BiomeHolder{public static BiomeHolder Inst=new();public const int GreeceBiomeIndex=3;public int BiomeIndex=3;}
public static class BiomeData{public static BagCurrency Swap;public static T GetPrefabSwap<T>(T prefab)where T:BagCurrency=>Swap==null?prefab:(T)Swap;}
public class Managers{public static Managers Inst=new();public CurrencyManager currency=new();public Kingdom kingdom=new();}
public class Kingdom{public Player playerOne=new(),playerTwo=new();}
public class Player{public Wallet wallet=new();}
public class Wallet{public int TotalCapacity;}
public enum CurrencyType{Coins,Shades}
public enum CurrencyBagType{Bag,Hermes,EggBasket}
public class CurrencyConfig{public BagCurrency BagPrefab;}
public class CurrencyManager
{
 public Dictionary<CurrencyType,CurrencyConfig> Data=new();public bool Missing;
 public bool TryGetData(CurrencyType type,out CurrencyConfig config){config=null;return !Missing&&Data.TryGetValue(type,out config);}
}
public class CurrencyBag:UnityEngine.Component{public UnityEngine.Transform _container;public void RecalcPosition(){}public void Awake(){}}
public class BagCurrency:UnityEngine.Component{public CurrencyBag bag;public CurrencyType CurrencyType;public void ResetVisuals(bool backLayer,int nthCoin,bool stack=true){}}
public class CurrencyBagHandler
{
 public void OnGameStartHandler(){}public void ChangeCurrencyBag(CurrencyBagType type,int playerIndex){}public CurrencyBag SetCurrencyBag(CurrencyBagType type,int playerIndex)=>null;
}
namespace KingdomEnhancedMod
{
 public static class ModConfig{public static Setting Enabled=new();public class Setting{public bool Value=true;}}
 public class KingdomEnhancedPlugin
 {
  public static KingdomEnhancedPlugin Instance=new();public Logger LogSource=new();
  public class Logger{public void LogError(object value)=>throw new Exception("Unexpected adapter error: "+value);public void LogInfo(object value){}public void LogDebug(object value){}public void LogWarning(object value){}}
 }
}
