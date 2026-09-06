namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)] public class HarmonyPatch:Attribute {public HarmonyPatch(Type t,string n){} public HarmonyPatch(Type t,string n,Type[] p){}}
 public class HarmonyPrefix:Attribute{} public class HarmonyPostfix:Attribute{} public class HarmonyFinalizer:Attribute{}
}
namespace UnityEngine {
 public class Object {static int next; public IntPtr Pointer=(IntPtr)Interlocked.Increment(ref next);public int GetInstanceID()=>(int)Pointer;public T Cast<T>() where T:class=>this as T;}
 public class GameObject:Object {public bool activeInHierarchy=true;public Transform transform=new();}
 public class Component:Object {public GameObject gameObject=new();public Transform transform=>gameObject.transform;}
 public class Transform {public Vector3 position;}
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
 public static class Mathf {public static float Abs(float v)=>MathF.Abs(v);public static float Min(float a,float b)=>MathF.Min(a,b);public static float Max(float a,float b)=>MathF.Max(a,b);public static float Clamp(float v,float lo,float hi)=>Math.Clamp(v,lo,hi);public static float Clamp01(float v)=>Math.Clamp(v,0,1);public static float Lerp(float a,float b,float t)=>a+(b-a)*t;public static bool Approximately(float a,float b)=>MathF.Abs(a-b)<.0001f;}
 public static class Time {public static float time;}
}
public enum Side{Left=-1,Neutral=0,Right=1}
public class GuardSlot{}
public class Knight{}
public class Formation{}
public class Embarkee{public bool IsEmbarked;public object EmbarkableTarget;}
public class Scanner {public float range=12,rangeBehind=12;}
public class Mover:UnityEngine.Component {public float _goalPosition;public BoolValue _movingToGoal=new();public GoalMode goalMode;public int Goals;public enum GoalMode {Off,Position,Object} public void SetGoal(float x,float speed){_goalPosition=x;goalMode=GoalMode.Position;_movingToGoal.value=true;Goals++;}}
public class BoolValue {public bool value;}
public class Archer:UnityEngine.Component {
 public bool Marked=true,inGuardSlot,Controlled,WallDuty=true;public Knight _knight;public GuardSlot _guardSlot;public Formation _currentFormation;public Side _guardSide=Side.Right;
 public int _guardDepth;public float _minDistanceFromWall=1,_unitSpacingAtWall=1,_guardRandomOffset,walkSpeed=2,shootRange=12,towerShootRange=12;
 public Scanner _enemyScanner=new();public Mover _mover=new();public Embarkee _embarkee=new();public Coatsink.Common.Haglet behaviour=new(){latestGoto=8};
 public bool ShouldPlayerControl()=>Controlled;public bool ShouldGoToWall()=>WallDuty;public Formation GetFormation()=>_currentFormation;
}
namespace Coatsink.Common{public class Haglet:UnityEngine.Object {public int latestGoto;}}
public class Director {public float currentTime=20;}
public class Kingdom {public float Left=-100,Right=100,campfirePosition;public float GetBorderSideIntact(Side side)=>side==Side.Left?Left:Right;}
public class Managers {public static Managers Inst=new();public Kingdom kingdom=new();public Director director=new();}
public static class NetworkBigBoss {public static bool HasWorldAuth=true;}
namespace KingdomEnhancedMod {
 public static class ModConfig {public static BoolConfig Enabled=new();public class BoolConfig{public bool Value=true;}}
 public static class PatchRoles_Crossbowman {public static bool IsCrossbowman(Archer a)=>a!=null&&a.Marked;}
 public class KingdomEnhancedPlugin {public static KingdomEnhancedPlugin Instance=new();public Logger LogSource=new();public class Logger {public static List<string> Errors=new();public void LogError(string s)=>Errors.Add(s);public void LogWarning(string s)=>Errors.Add(s);public void LogInfo(string s){}}}
}
