// archer-night-band 回归套件替身：只提供生产代码（PatchRoles_ArcherNightBand +
// 从 PatchWorld_DefenseSpacing.cs 抽取的 DayAssembleSpreadPrefix / MirrorNightArcherGoal /
// NightParkedFollowerSweep）实际读到的成员；Unity 语义按需最小化。
// 与 tests/samurai-night-formation、tests/crossbow-defense 的替身同款。
namespace HarmonyLib
{
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)]
 public class HarmonyPatch:Attribute{public Type Target;public string Name;public HarmonyPatch(Type t,string n){Target=t;Name=n;}public HarmonyPatch(Type t,string n,Type[] args){Target=t;Name=n;}}
 public class HarmonyPrefix:Attribute{}
}
namespace UnityEngine
{
 public class Object
 {
  static long next;public IntPtr Pointer=(IntPtr)Interlocked.Increment(ref next);public bool Destroyed;
  public int GetInstanceID()=>(int)Pointer;
  public static bool operator ==(Object a,Object b)=>ReferenceEquals(a,b)||((a is null||a.Destroyed)&&(b is null||b.Destroyed));public static bool operator !=(Object a,Object b)=>!(a==b);
  public override bool Equals(object o)=>ReferenceEquals(this,o);public override int GetHashCode()=>Pointer.GetHashCode();
  public static implicit operator bool(Object o)=>o!=null;
  public T Cast<T>() where T:class=>this as T;
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
 public static class Mathf{public static float Abs(float a)=>MathF.Abs(a);public static float Min(float a,float b)=>MathF.Min(a,b);public static float Max(float a,float b)=>MathF.Max(a,b);public static bool Approximately(float a,float b)=>MathF.Abs(a-b)<.00001f;public static float Sign(float a)=>a<0?-1:1;}
 // sweep 的生产式 Random.Range(DepthClampRange - 2f, DepthClampRange) 由 NextFraction
 // 取两界值，验证目标始终落在 ≤Cap 带内。
 public static class Random{public static float NextFraction=0f;public static float Range(float min,float max)=>min+(max-min)*NextFraction;}
}
public enum Side{Neutral=0,Left=-1,Right=1}
public class Character:UnityEngine.Component{public bool inert,grabbed,isStationary;}
public class Damageable:UnityEngine.Component{public bool isDead;}
public class Formation:UnityEngine.Component{public bool IsShieldWall;}
public class GuardSlot{}
public class Embarkee{public bool IsEmbarked,IsTargetingEmbarkable;public object EmbarkableTarget;}
public class Knight:UnityEngine.MonoBehaviour{public Side side=Side.Right;}
public class Archer:UnityEngine.MonoBehaviour
{
 public Side _guardSide=Side.Right;public Knight _knight;public bool inGuardSlot;public GuardSlot _guardSlot;
 public Character _character;public Damageable _damageable;public Embarkee _embarkee=new();public Mover _mover;
 public Formation Formation;public Coatsink.Common.Haglet behaviour=new(){latestGoto=8};
 public int _guardDepth;public float _minDistanceFromWall=1,_unitSpacingAtWall=1,_guardRandomOffset,walkSpeed=2,runSpeed=6,knightFollowDistance=1;
 // 身份/门旗（生产谓词在替身里按旗取值）。
 public bool Crossbow,Musketeer,Hero,WallDuty=true,ControlRequested;
 public bool ShouldPlayerControl()=>ControlRequested;
 public bool ShouldGoToWall()=>WallDuty;
 public Formation GetFormation()=>Formation;
}
public class Director{public float currentTime=20f;}
public class Kingdom:UnityEngine.MonoBehaviour
{
 public float Left=-100f,Right=100f;
 public float GetBorderSideIntact(Side side)=>side==Side.Left?Left:Right;
}
public class Managers{public static Managers Inst=new();public Kingdom kingdom=new GameObjectHolder().Kingdom;public Director director=new();private class GameObjectHolder{public Kingdom Kingdom=new UnityEngine.GameObject().AddComponent<Kingdom>();}}
public static class NetworkBigBoss{public static bool HasWorldAuth=true;}
public class Mover:UnityEngine.Component
{
 public enum OffsetMode{Distance,Formation,Strict}
 public delegate bool FloatPrefixDelegate(Mover mover,float goal,ref float speed);
 public static FloatPrefixDelegate FloatIntercept;
 public int NativeFloatGoals,ObjectGoals;public float Goal,Speed;
 public UnityEngine.GameObject ObjectGoal;public float ObjectSpeed,ObjectOffset;public OffsetMode ObjectMode;
 public void SetGoal(float goal,float speed)
 {
  if(FloatIntercept!=null&&!FloatIntercept(this,goal,ref speed))return; // 前缀改写：由嵌套调用落点
  NativeFloatGoals++;Goal=goal;Speed=speed;
 }
 public void SetGoal(UnityEngine.GameObject goal,float speed,float offset=0,OffsetMode mode=OffsetMode.Distance)
 { ObjectGoals++;ObjectGoal=goal;ObjectSpeed=speed;ObjectOffset=offset;ObjectMode=mode; }
}
namespace Coatsink.Common
{
 public class Haglet:UnityEngine.Object{public int latestGoto=2;public bool started=true;}
}
namespace KingdomEnhancedMod
{
 public static class ModConfig{public class Option{public bool Value=true;}public static Option Enabled=new();}
 // 身份谓词按替身旗取值（生产实现各有真实来源，本套件不重复它们）。
 public static class PatchRoles_Crossbowman{public static bool IsCrossbowman(Archer a)=>a!=null&&a.Crossbow;}
 public static class MusketeerIdentity{public static bool IsUnit(Archer a)=>a!=null&&a.Musketeer;}
 public static class HeroArcherRuntime{public static bool IsHero(Archer a)=>a!=null&&a.Hero;}
 // 弩手深化/拉回：本套件只验证"弩手分支仍归它们管"，用可观测计数器替身。
 public static class PatchRoles_CrossbowDefense
 {
  public static bool NightGoal=false;public static float NightGoalX;public static int Pullbacks;
  public static bool TryGetNightGoal(Archer a,float goal,out float target){target=NightGoal?NightGoalX:0f;return NightGoal;}
  public static bool TryPullBack(Archer a){Pullbacks++;return false;}
 }
 // 武士重定向：本套件只验证前缀分发，替身为恒假（samurai-retreat 同款签名锚）。
 public static class PatchRoles_SamuraiNightFormation{public static bool TryTakeRedirect(Knight k,Mover m,float goal,out float slot){slot=0f;return false;}}
 public static class SamuraiRetreatSpeed{public static void Adjust(Knight k,Mover m,ref float speed){}}
 // 自由守墙弓手谓词：本套件按可设旗替身（生产实现另由 expedition-follow 覆盖）。
 public static class SquadFollowGuard{public static bool WallArcher=true,WallFollower=true;internal static bool IsOrdinaryWallArcher(Archer a)=>WallArcher;internal static bool IsWallFollower(Archer a,Kingdom k)=>WallFollower;}
 public class KingdomEnhancedPlugin{public static KingdomEnhancedPlugin Instance=new();public Logger LogSource=new();public class Logger{public readonly List<string> Infos=new();public readonly List<string> Errors=new();public void LogInfo(string text)=>Infos.Add(text);public void LogWarning(string text)=>Infos.Add(text);public void LogError(string text)=>Errors.Add(text);}}
}
