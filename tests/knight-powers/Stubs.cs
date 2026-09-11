using System.Collections;
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method, AllowMultiple=true)] public class HarmonyPatch:Attribute { public HarmonyPatch(Type type,string name){} }
 public class HarmonyPrefix:Attribute{} public class HarmonyPostfix:Attribute{} public class HarmonyFinalizer:Attribute{}
}
namespace Il2CppInterop.Runtime.Injection { public static class ClassInjector { public static bool IsTypeRegisteredInIl2Cpp(Type t)=>true; public static void RegisterTypeInIl2Cpp(Type t){} } }
namespace BepInEx.Unity.IL2CPP.Utils.Collections { public static class Extensions { public static IEnumerator WrapToIl2Cpp(this IEnumerator e)=>e; } }
namespace UnityEngine {
 public class Object { static long next; public IntPtr Pointer {get;set;} = (IntPtr)Interlocked.Increment(ref next); public static void Destroy(Object o) { if(o is Component c) c.gameObject?.Remove(c); } }
 public class GameObject:Object {
  readonly List<Component> components=new(); public int Id {get;set;} public Transform transform; public int layer; public bool activeSelf=true;
  public GameObject(string name="unit"){Id=(int)Pointer; transform=new Transform {gameObject=this};}
  public int GetInstanceID()=>Id; public void SetActive(bool value)=>activeSelf=value;
  public T AddComponent<T>() where T:Component {var c=(T)(typeof(T).GetConstructor(Type.EmptyTypes)!=null ? Activator.CreateInstance(typeof(T)) : Activator.CreateInstance(typeof(T),(IntPtr)100000)); c.gameObject=this;components.Add(c);return c;}
  public T GetComponent<T>() where T:Component=>components.OfType<T>().FirstOrDefault(); public void Remove(Component c)=>components.Remove(c);
 }
 public class Component:Object { public GameObject gameObject; public Transform transform=>gameObject?.transform; public T GetComponent<T>() where T:Component=>gameObject.GetComponent<T>(); }
 public class MonoBehaviour:Component { public bool enabled=true; public MonoBehaviour(){} public MonoBehaviour(IntPtr ptr){} public void StartCoroutine(IEnumerator e){} }
 public class Transform:Component {public Vector3 localScale=Vector3.one,localPosition,position;public Quaternion localRotation;public Transform parent;public void SetParent(Transform t,bool world)=>parent=t;public bool IsChildOf(Transform t)=>parent==t;}
 public struct Vector2 {public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;} public static Vector2 operator*(Vector2 a,float n)=>new(a.x*n,a.y*n);}
 public struct Vector3 {public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 zero=>new(0,0,0); public static Vector3 one=>new(1,1,1);}
 public struct Quaternion {public static Quaternion identity=>new();}
 public struct Rect {public float xMin,yMin,xMax,yMax;public static Rect MinMaxRect(float x1,float y1,float x2,float y2)=>new(){xMin=x1,yMin=y1,xMax=x2,yMax=y2};}
 public struct Color {public Color(float r,float g,float b,float a){}}
 public static class Mathf {public const float PI=MathF.PI;public static float Sin(float a)=>MathF.Sin(a);public static float Lerp(float a,float b,float t)=>a+(b-a)*t;public static float Abs(float f)=>MathF.Abs(f);}
 public static class Time {public static float time,deltaTime=0.02f;}
 public class WaitForSeconds {public WaitForSeconds(float seconds){}}
 public class Shader {public static Shader Find(string s)=>new();} public class Material {public Material(Shader s){}}
 public class Renderer:Component {public bool enabled;public int sortingOrder,sortingLayerID;public string sortingLayerName;public Material material,sharedMaterial;}
 public class SpriteRenderer:Renderer{} public class TrailRenderer:Renderer{}
 public class LineRenderer:Renderer {public bool useWorldSpace,loop;public float widthMultiplier;public int numCapVertices,numCornerVertices,positionCount;public Color startColor,endColor;public Vector3[] positions;public void SetPositions(Vector3[] p)=>positions=p;}
 public struct AnimatorStateInfo {public int shortNameHash;public float normalizedTime;}
 public class Animator:Component {public float speed=1;public AnimatorStateInfo State;public AnimatorStateInfo GetCurrentAnimatorStateInfo(int i)=>State;public static int StringToHash(string s)=>s.GetHashCode();}
}
public class Wallet:UnityEngine.Component {
 int capacity, taxes,coins;public int CapacityWrites,TaxWrites,CoinWrites;
 public int TotalCapacity {get=>capacity;set{CapacityWrites++;capacity=value;}} public int payTaxesAbove {get=>taxes;set{TaxWrites++;taxes=value;}}
 public int Coins {get=>coins;set{CoinWrites++;coins=value;}} public bool CanGrabCoins=true;
 public void SetCurrency(int n)=>Coins=n;public void AddCurrency(int n)=>Coins+=n;public void NativeLoadCoins(int n)=>coins=n;
}
public class Knight:UnityEngine.Component {
 public int Style;public bool Qualified=true;public float _slashRange=2,_awarenessRange=10,_cooldown;public UnityEngine.Rect _hitBox=UnityEngine.Rect.MinMaxRect(-.5f,-1,2,1);
 public Wallet _originalWallet,_wallet;public int _statueBuffMaxCoins=7;public bool statueBuffActive,_harmless;
 public Scanner _enemyScanner;public PushablePusher _pusher;public Formation _currentFormation;public Damageable _enemy;public UnityEngine.TrailRenderer _trail;
 public Mover _mover;public UnityEngine.Animator _animator;
 public class _Slash_d__168:UnityEngine.Object {
  public Knight __4__this; public int __1__state; public float _elapsedTime_5__4;
  public Action NativeBody; public bool NativeResult=true; public int NativeCalls;
  // Simulate the native iterator's state write AFTER callbacks, including reentrant OnDisable.
  public bool NativeMoveNext(){NativeCalls++;if(__1__state==-1)return false;__1__state=-1;NativeBody?.Invoke();__1__state=NativeResult?1:-1;return NativeResult;}
 }
}
public class Archer:UnityEngine.Component {public Knight _knight;public Mover _mover;public UnityEngine.Animator _animator;public bool Controlled;public bool ShouldPlayerControl()=>Controlled;public float _cooldown,shootPrepTime=4;public UnityEngine.Vector2 _shootIntervalRange=new(2,6),_shootIntervalRangeFormation=new(4,8);public Coatsink.Common.Haglet shoot;public class _Shoot_d__225:UnityEngine.Object{public Archer __4__this;}}
public class Mover:UnityEngine.Component {public float _goalSpeed=2,_moveSpeed=4,_multiplier=1;public GoalMode goalMode;public enum GoalMode{Off,Move,Position,Object}}
public class Scanner:UnityEngine.Component {public float range;public UnityEngine.GameObject Closest;public UnityEngine.GameObject GetClosest()=>Closest;}
public class PushablePusher {public bool enabled;}
public class Formation {public float knightSlashRange;}
public enum DamageSource{Knight}
public class Damageable:UnityEngine.Component {public bool Vulnerable=true;public bool IsDamagedBy(DamageSource d)=>Vulnerable;}
public static class Statue {public enum Deity{Knight}public enum DeityStatus{Inactive,Activated}}
public class CampaignSaveData {public static CampaignSaveData current;public static Statue.DeityStatus Status;public static Statue.DeityStatus GetDeityStatus(Statue.Deity d,object o)=>Status;}
public static class NetworkBigBoss {public static bool HasWorldAuth=true;}
public class World:UnityEngine.MonoBehaviour {public void OnLevelLoaded(){}}
public static class AnimationSync {public static void SetAndSendAnimationTrigger(){}}
namespace KingdomEnhancedMod {
 public static class ModConfig {public static BoolConfig Enabled=new();public class BoolConfig{public bool Value=true;}}
 public static class PatchRoles_KnightStyle {public static bool TryGetResolvedStyleIndex(Knight k,out int style){style=k.Style;return k.Qualified;}}
 public static class UnitScanCache {public static Knight[] Knights=Array.Empty<Knight>();public static Knight[] GetKnights()=>Knights;}
 public class KingdomEnhancedPlugin {public static KingdomEnhancedPlugin Instance=new();public Logger LogSource=new();public class Logger{public static List<string> Errors=new();public void LogError(string s)=>Errors.Add(s);public void LogInfo(string s){}public void LogWarning(string s){}}}
}

namespace Coatsink.Common { public class Haglet:UnityEngine.Object {public bool started;public T Cast<T>() where T:class => this as T;} }
