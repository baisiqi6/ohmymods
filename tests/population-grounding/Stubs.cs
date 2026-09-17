namespace UnityEngine
{
    public class Object { static long counter; public IntPtr Pointer=(IntPtr)(++counter); public string name="object"; }
    public struct Scene { public int handle; }
    public struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;} }
    public struct Vector2 { public float x,y; }
    public struct Bounds { public Vector3 min,max; }
    public class Component:Object { public GameObject gameObject; public Transform transform=>gameObject.transform; }
    public class Transform:Component { public Vector3 position,localPosition,lossyScale=new(1,1,1); public Transform parent; public bool IsChildOf(Transform t)=>this==t || parent!=null&&parent.IsChildOf(t); }
    public class GameObject:Object
    {
        public bool activeInHierarchy=true,ThrowDetails;public Scene scene;public int layer;
        public Transform transform; public int DetailReads;
        public Rigidbody2D Body;public Collider2D[] Colliders=Array.Empty<Collider2D>();
        public GameObject(){transform=new(){gameObject=this};}
        public int GetInstanceID()=>(int)Pointer;
        public T GetComponent<T>() where T:class {DetailReads++;if(ThrowDetails)throw new Exception("native read");return Body as T;}
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T:class {DetailReads++;return Colliders as T[];}
    }
    public enum RigidbodyType2D {Dynamic,Kinematic,Static}
    public enum RigidbodyConstraints2D {None,FreezeRotation}
    public enum CollisionDetectionMode2D {Discrete,Continuous}
    public class Rigidbody2D:Component {public RigidbodyType2D bodyType;public bool simulated=true;public float gravityScale=1;public RigidbodyConstraints2D constraints;public CollisionDetectionMode2D collisionDetectionMode;public Vector2 velocity;}
    public class Collider2D:Component {public bool enabled=true,isTrigger;public Bounds bounds;}
    public class BoxCollider2D:Collider2D {}
    public static class Physics2D {public static int Reads;public static bool GetIgnoreLayerCollision(int a,int b) {if(a!=17||b!=0)throw new Exception("unrelated pair");Reads++;return false;}}
    public static class Time {public static float time,timeScale=1;}
}
public class World:UnityEngine.Object {public UnityEngine.Transform gameLayer;public static UnityEngine.BoxCollider2D GroundCollider;}
public class Managers {public static Managers Inst;public World world;}
public static class NetworkBigBoss {public static bool HasWorldAuth=true;}
public class Header {public int NetID;}
public class Beggar:UnityEngine.Component {public Header parentHeaderRef;public BeggarCamp camp;}
public class BeggarCamp:UnityEngine.Component {}
namespace KingdomEnhancedMod
{
    internal static class ModConfig {internal class Flag{public bool Value=true;}internal static Flag Enabled=new();}
    internal class KingdomEnhancedPlugin {internal static KingdomEnhancedPlugin Instance=new();internal Logger LogSource=new();}
    internal class Logger {internal List<string> Messages=new();internal bool Throw;internal void LogInfo(string x){if(Throw)throw new Exception("log");Messages.Add(x);}internal void LogWarning(string x){if(Throw)throw new Exception("log");Messages.Add(x);}}
}
