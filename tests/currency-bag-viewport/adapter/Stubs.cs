namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)]
    public class HarmonyPatch:Attribute { public HarmonyPatch(){} public HarmonyPatch(Type t){} public HarmonyPatch(Type t,string method){} }
    public class HarmonyPostfix:Attribute{}
    public class HarmonyPrefix:Attribute{}
}
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public float this[int i]{get=>i==0?x:i==1?y:z;set{if(i==0)x=value;else if(i==1)y=value;else z=value;}}
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public struct Bounds { public Vector3 min,max; public Bounds(Vector3 min,Vector3 max){this.min=min;this.max=max;} }
    public struct Scene { public bool valid; public bool IsValid()=>valid; }
    public static class Mathf { public static bool Approximately(float a,float b)=>MathF.Abs(a-b)<.00001f; }
    public static class Time { public static int frameCount; }
    public class Transform
    {
        private Vector3 _position;
        public int Writes;
        public int Id = ++nextId;
        public IntPtr Pointer;
        public GameObject gameObject;
        public Vector3 localScale = new(1,1,1);
        public Vector3 position{get=>_position;set{_position=value;Writes++;}}
        public int GetInstanceID()=>Id;
        static int nextId;
    }
    public class GameObject
    {
        public bool activeInHierarchy = true;
        public string name = "CurrencyBag";
        public Scene scene = new(){valid=true};
        public int Id = ++nextId;
        public IntPtr Pointer;
        public int GetInstanceID()=>Id;
        static int nextId;
    }
    public class Sprite { }
    public class SpriteRenderer
    {
        public Sprite sprite=new();
        public bool enabled=true;
        public float alpha=1f;
        public Transform Owner;
        public Vector3 LocalMin,LocalMax;
        public bool EmptyWorldOrigin;
        public Bounds bounds=>EmptyWorldOrigin?new(new(0,0,0),new(0,0,0)):new(Owner.position+LocalMin,Owner.position+LocalMax);
    }
    public class Camera
    {
        public bool orthographic=true;
        public int pixelWidth=1280,pixelHeight=720;
        public float orthographicSize=2.8125f,aspect=16f/9f;
        public Vector3 Position=new(-20f,0f,-10f);
        public Vector3 WorldToViewportPoint(Vector3 p)=>new(.5f+(p.x-Position.x)/(2*orthographicSize*aspect),.5f+(p.y-Position.y)/(2*orthographicSize),p.z-Position.z);
        public Vector3 ViewportToWorldPoint(Vector3 p)=>new(Position.x+(p.x-.5f)*(2*orthographicSize*aspect),Position.y+(p.y-.5f)*(2*orthographicSize),Position.z+p.z);
    }
}
public class InterfaceCamera { public UnityEngine.Camera InterfaceCam=new(); }
public class Wallet { public int TotalCapacity; }
public class Player { public UnityEngine.GameObject gameObject = new(); public Wallet wallet = new(); }
public class CurrencyBag
{
    public enum FadeState { FadingIn,Opening,Open,Closing,FadingOut,Hidden }
    public static bool DebugHideCurrencyBag;
    public FadeState CurrentFadeState = FadeState.Hidden;
    public bool enabled = true, _showingCurrency;
    public Player player = new();
    public UnityEngine.GameObject gameObject = new();
    public InterfaceCamera CachedInterfaceCam=new();
    public UnityEngine.Transform transform=new();
    public UnityEngine.SpriteRenderer _front,_back,_closed;
    public UnityEngine.Transform _container = new();
    public void Awake(){}
    public void RecalcPosition(){}
    public void StartShow(){}
}
public class Component
{
    public UnityEngine.GameObject gameObject = new();
    public UnityEngine.Transform transform = new();
    public IntPtr Pointer;
    public int Id = ++nextId;
    public int GetInstanceID()=>Id;
    public T GetComponent<T>() where T:Component => null;
    static int nextId;
}
public class Mover : Component { public bool enabled=true; }
public class Embarkee : Component { public bool IsEmbarked,IsTargetingEmbarkable; }
public enum CurrencyType { Coins,Shades }
public enum CurrencyBagType { Bag,Hermes,EggBasket }
public class CurrencyConfig { public BagCurrency BagPrefab; }
public class CurrencyManager
{
    public Dictionary<CurrencyType,CurrencyConfig> Data=new();
    public bool TryGetData(CurrencyType type,out CurrencyConfig config)=>Data.TryGetValue(type,out config);
}
public class BagCurrency
{
    public CurrencyBag bag;
    public CurrencyType CurrencyType;
    public UnityEngine.Transform transform=new();
    public void ResetVisuals(bool backLayer,int nthCoin,bool stack=true){}
}
public class CurrencyBagHandler
{
    public void OnGameStartHandler(){}
    public void ChangeCurrencyBag(CurrencyBagType type,int playerIndex){}
    public CurrencyBag SetCurrencyBag(CurrencyBagType type,int playerIndex)=>null;
}
public class Managers { public static Managers Inst=new(); public CurrencyManager currency=new(); public Kingdom kingdom=new(); }
public class Kingdom { public Player playerOne=new(),playerTwo=new(); }
public static class BiomeData { public static BagCurrency Swap; public static T GetPrefabSwap<T>(T prefab) where T:BagCurrency=>Swap==null?prefab:(T)Swap; }
public class BiomeHolder { public static BiomeHolder Inst; public const int GreeceBiomeIndex=3; public int BiomeIndex=3; }
namespace Coatsink.Common { public static class SingletonMonoBehaviour<T> where T:new() { public static T Inst = new(); } }
namespace KingdomEnhancedMod
{
    internal static class ScaleRegistryHolder { internal static void RetryPendingCreation(){} }
    public static class ModConfig { public static Setting Enabled=new(); public class Setting { public bool Value=true; } }
    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance=new();
        public Logger LogSource=new();
        public class Logger { public void LogError(object v){} public void LogWarning(object v){} public void LogInfo(object v){} public void LogDebug(object v){} }
    }
}
