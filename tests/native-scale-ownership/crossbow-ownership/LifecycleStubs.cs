using System.Collections;
namespace HarmonyLib
{
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)]public class HarmonyPatch:Attribute{public HarmonyPatch(){}public HarmonyPatch(Type t){}public HarmonyPatch(string n){}public HarmonyPatch(Type t,string n){}public HarmonyPatch(Type t,string n,Type[] args){}public HarmonyPatch(string n,Type[] args){} }
 public class HarmonyPostfix:Attribute{}public class HarmonyPrefix:Attribute{}public class HarmonyFinalizer:Attribute{}public class HarmonyPriority:Attribute{public HarmonyPriority(int p){}}public static class Priority{public const int Last=0;}
}
namespace Il2CppInterop.Runtime.Injection
{
 public static class ClassInjector
 {
  public static bool MarkerRegistered=true,FailMarkerRead;
  public static bool IsTypeRegisteredInIl2Cpp(Type t){if(t==typeof(KingdomEnhancedMod.CrossbowmanMarker)){if(FailMarkerRead)throw new InvalidOperationException("marker type unavailable");return MarkerRegistered;}return true;}
  public static void RegisterTypeInIl2Cpp(Type t){if(t==typeof(KingdomEnhancedMod.CrossbowmanMarker))MarkerRegistered=true;}
 }
}
namespace BepInEx.Unity.IL2CPP.Utils.Collections
{ public static class Extensions{public static IEnumerator WrapToIl2Cpp(this IEnumerator e)=>e;} }
namespace UnityEngine
{
 public class Object
 {
  static int next; public IntPtr Pointer;public int Id;public bool Destroyed,FailIdentity;public string name;
  public Object(){Id=++next;Pointer=(IntPtr)Id;}
  public int GetInstanceID(){if(FailIdentity)throw new InvalidOperationException("identity fault");return Id;}
  public static void DontDestroyOnLoad(Object o){}public static void Destroy(Object o){o.Destroyed=true;}
  public static T[] FindObjectsOfType<T>(){throw new Exception("Unexpected scene scan");}
 }
 public class Component:Object
 {
  public GameObject gameObject;public Transform transform=>gameObject.transform;public string tag=>gameObject.tag;
  public T GetComponent<T>()where T:Component=>gameObject.GetComponent<T>();public T GetComponentInChildren<T>()where T:Component=>GetComponent<T>();
  public bool CompareTag(string t)=>tag==t;
 }
 public class Behaviour:Component{public bool enabled=true;public bool isActiveAndEnabled=>enabled&&gameObject.activeInHierarchy;}
 public class MonoBehaviour:Behaviour{public MonoBehaviour(){}public MonoBehaviour(IntPtr p){Pointer=p;}public void StartCoroutine(IEnumerator e){} }
 public class GameObject:Object
 {
  public static int HolderCreates,HolderFailures,HolderNullResults;public Scene scene=new(){valid=true};public Transform transform;public bool activeInHierarchy=true;public string tag="Knight";public HideFlags hideFlags;
  readonly List<Component> components=new();public GameObject(string n="actor"){name=n;transform=new(){gameObject=this};}
  public Type FailComponentRead;public Component MarkerOverride;
  public T GetComponent<T>()where T:Component{if(FailComponentRead==typeof(T))throw new InvalidOperationException("component read failed");if(typeof(T)==typeof(KingdomEnhancedMod.CrossbowmanMarker)&&MarkerOverride!=null)return (T)MarkerOverride;foreach(var c in components)if(c is T t)return t;return null;}
  public T AddComponent<T>()where T:Component
  {
   if(typeof(T)==typeof(KingdomEnhancedMod.ScaleRegistryHolder)){HolderCreates++;if(HolderFailures-->0)throw new InvalidOperationException("AddComponent fault");if(HolderNullResults-->0)return null;}
   var ctor=typeof(T).GetConstructor(new[]{typeof(IntPtr)});
   var c=(T)(ctor!=null?ctor.Invoke(new object[]{(IntPtr)Id}):Activator.CreateInstance(typeof(T)));c.gameObject=this;components.Add(c);return c;
  }
  public void Remove(Component c)=>components.Remove(c);
 }
 public enum HideFlags{HideAndDontSave}
 public struct Scene{public bool valid;public bool IsValid()=>valid;}
 public struct Vector3
 {
  public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 one=>new(1,1,1);
  public float this[int a]{get=>a==0?x:a==1?y:z;set{if(a==0)x=value;else if(a==1)y=value;else z=value;}}
 }
 public struct Vector2{public float x,y;public Vector2(float a,float b){x=a;y=b;}}
 public class Transform:Component
 {
  Vector3 scale=Vector3.one;public Vector3 position;public int Writes,Reads;public bool FailWrite,CommitBeforeFailure;public int FailReads;public Action OnWrite;
  public Vector3 localScale{get{Reads++;if(FailReads-->0)throw new InvalidOperationException("read fault");return scale;}set{if(FailWrite){FailWrite=false;if(CommitBeforeFailure){scale=value;Writes++;}throw new InvalidOperationException("write fault");}scale=value;Writes++;OnWrite?.Invoke();}}
  public bool IsChildOf(Transform t)=>true;
 }
 public class RuntimeAnimatorController:Object{}
 public class Animator:Behaviour{public RuntimeAnimatorController runtimeAnimatorController;}
 public class SpriteRenderer:Behaviour{}
 public class WaitForSeconds{public WaitForSeconds(float t){}}
 public static class Time{public static int frameCount;public static float time;}
 public static class Resources{public static T[] FindObjectsOfTypeAll<T>()=>Array.Empty<T>();public static T[] LoadAll<T>(string s)=>Array.Empty<T>();public static T Load<T>(string s)=>default;}
 public static class Mathf{public static float Abs(float f)=>MathF.Abs(f);}
}
public class Mover:UnityEngine.Behaviour{public void Update(){} }
public class Embarkee:UnityEngine.Component{public bool IsEmbarked,IsTargetingEmbarkable;}
public class BiomeHolder
{
 public static BiomeHolder Inst=new();public const int GreeceBiomeIndex=5,DeadlandsBiomeIndex=2,BambooBiomeIndex=1,NorselandsBiomeIndex=3;public string[] biomePathStrings;
 int index=5;public bool FailRead;public int BiomeIndex{get{if(FailRead)throw new InvalidOperationException();return index;}set=>index=value;}public BiomeSwapData GetBiomeSwapDataForIndex(int i)=>null;
}
public class BiomeSwapData{public List<AnimatorSwapData> animatorSwapPool=new();public class AnimatorSwapData{public UnityEngine.RuntimeAnimatorController original,swap;} }
public class BiomeData:UnityEngine.Object{public static BiomeData Current=new();public BiomeSwapData swapData;public T GetAssetSwapForThis<T>(T t)=>t;}
public class GlobalSaveData{public static GlobalSaveData loaded;public int currentCampaign,currentChallenge;}
public class CampaignSaveData{public static CampaignSaveData current;public IslandSaveData CurrentIsland;public int CurrentLand,reign;}
public class IslandSaveData{public DateTime realStartDateTime;}
public class NetworkPostbox{public static NetworkPostbox Instance;public CRPCHeader GetHeaderFromDynamicObject(UnityEngine.GameObject go,bool v)=>null;}
public class CRPCHeader{public int NetID;}
public static class NetworkBigBoss{public static bool HasWorldAuth=true;}
public class Character:UnityEngine.Behaviour{public void Promote(){} }
public class DroppableTool:UnityEngine.Component{public bool pickedUp;}
public interface IUnitController{}
public class Holder:UnityEngine.Component{public Dictionary<string,Character> tagCharacterPairs=new();public void InitializeTagCharacterPairs(){} }
public class Managers{public static Managers Inst=new();public Holder holder;}
public class Worker:Character{public NpcShieldUser npcShieldUser;public void OnEnable(){} }
public class WarriorPeasant:Character{public void OnEnable(){} }
public class Peasant:Character{public void OnEnable(){} }
public class Deer:Character{public void OnEnable(){} }
public class Damageable:UnityEngine.Component{}
public class Shield:UnityEngine.Component{}
public class NpcShieldUser:UnityEngine.Behaviour
{ public Character character;public Damageable damageable;public CRPCHeader parentHeaderRef;public int shieldEnabledRpcIndex;public UnityEngine.WaitForSeconds regenWait;public Shield shield;public void Awake(){}public void BeginRegisteringRPCs(){}public bool HasShield()=>false;public void EquipShield(){}public void SetShieldEnabled(bool b,int i){} }
public class Knight:Character{public UnityEngine.Animator _animator;public Mover _mover;}
public class Archer:Character
{public Knight _knight;public Mover _mover;public UnityEngine.Animator _animator;public UnityEngine.RuntimeAnimatorController soldierAnimator;public bool Marker,Norse,Package,FailAttackRead; public float shootRange=8,towerShootRange=12; public bool inGuardSlot,_isWearingBannerColor; public GuardSlot _guardSlot; public Scanner _enemyScanner=new(); public ArrowAttack _arrowAttack,_fireArrowAttack;ArrowAttack attack;public ArrowAttack ActiveArrowAttack{get{if(FailAttackRead)throw new InvalidOperationException("attack getter unavailable");return attack;}set=>attack=value;} public UnityEngine.RuntimeAnimatorController hunterAnimator; public UnityEngine.Vector2 _shootIntervalRange=new(1,2),_shootIntervalRangeFormation=new(1,2); }
public class World:UnityEngine.MonoBehaviour{public void OnLevelLoaded(){} }
namespace KingdomEnhancedMod
{
 public static class ModConfig{public static Setting Enabled=new();public class Setting{public bool Value=true;} }
 public class KingdomEnhancedPlugin
 {public static KingdomEnhancedPlugin Instance=new();public Logger LogSource=new();public class Logger{public static readonly List<string> Errors=new();public void LogInfo(object o){}public void LogWarning(object o){}public void LogError(object o){Errors.Add(o.ToString());} } }
 public readonly record struct KnightIdentityReceipt(int Style);
 public static class KnightIdentityRuntime
 {
  public static void MarkPromoted(Knight k){}public static void Poll(){}public static void PrimeExisting(Knight[] k,Func<Knight,int> f){}public static bool TryGetReceipt(Knight k,out KnightIdentityReceipt r){r=new(0);return true;}
  public static void OnEnable(Knight k){}public static void AssignFirstSeenUniform(Knight[] k,List<int> a,Func<Knight,bool> f){}
  public static bool IsHostAuthority()=>true;
  public static bool TryResolve(Knight k,int old,uint hash,List<int> a,out int style){style=0;return true;}
 }
 public static class KnightIdentityLoadSeed{public static void Flush(){} }
 public static class KnightIdentityQuotaRecovery{public static void IntegrityPass(List<int> l){} }
 public static class KnightIdentityNetwork{public static void Sync(Knight[] k){} }
 public static class UnitScanCache{public static Knight[] GetKnights()=>Array.Empty<Knight>();public static Archer[] GetArchers()=>Array.Empty<Archer>();public static void InvalidateAll(){} }
 public static class FleetGreekSquads{public static void ReleaseKnight(Knight k){} }
 public static class SquadRosterSnapshot{public static void Observe(Knight[] k,Archer[] a){} }
 public static class PatchRoles_GreekFireAssets{public static void ResetWorld(){}public static void RestoreMissing(Archer a){} }
 public static class PatchRoles_NorseSquad{public static bool IsNorseArcherInstance(Archer a)=>a.Norse;public static void PatrolPass(){} }
 public static class PatchRoles_Castle{public static void EnsurePoolForCharacter(string s){} }
 public static class PatchRoles_Crossbowman
 {
  public static UnityEngine.RuntimeAnimatorController BaseSoldierAnimator=new(){name="native-soldier"};public static int ApplyCalls,RestoreCalls;
  public static ArrowAttack ProbeCrossbowAttack; public static bool IsCrossbowman(Archer a)=>CrossbowmanLifecycle.IsCrossbowman(a);
  public static void ApplySquadCrossbowPackage(Archer a){ApplyCalls++;a.Package=true;GreekScaleScope.ApplyY(a.transform,1.15f);ScaleRegistryHolder.Register(a._mover,1.15f);}
  public static void RestoreSquadCrossbowPackage(Archer a){RestoreCalls++;if(ProbeCrossbowAttack==null||a.ActiveArrowAttack==null||a.ActiveArrowAttack.Pointer!=ProbeCrossbowAttack.Pointer)return;a.Package=false;ScaleRegistryHolder.Unregister(a._mover);GreekScaleScope.Restore(a.transform);}
 }
 public static class DeadlandsFollowerCapture
 { public static void OnConvertBefore(Archer a,bool b){}public static void OnPostfixEntry(Archer a,bool b){}public static void OnStyleBefore(Archer a){}public static void OnStyleAfter(Archer a){}public static void OnControllerWriteBefore(Archer a){}public static void OnControllerWriteAfter(Archer a){}public static void OnControllerNoWrite(Archer a){} }
}

public class ArrowAttack:UnityEngine.Object {} public class Scanner {public float range,rangeBehind;} public class GuardSlot {}
namespace KingdomEnhancedMod {internal static class PatchRoles_CrossbowDefense {internal static void ReconcileTowerRange(Archer a){} internal static void Remove(Archer a){} internal static bool TryPullBack(Archer a)=>true;}}
