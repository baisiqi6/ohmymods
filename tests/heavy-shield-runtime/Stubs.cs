// Synthetic native boundary only: tests execute the linked production runtime/combat/visual/life code.
// These objects cannot qualify live game behavior or replace actual interop compilation.
using System.Reflection;
using KingdomEnhancedMod;
internal static class NativeBoundaryWrites { internal static int Count; }
namespace Il2CppInterop.Runtime.InteropTypes
{
    public class Il2CppObjectBase { private static int Next; public IntPtr Pointer = (IntPtr)(++Next);
        public T TryCast<T>() where T : class => this as T; }
}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppReferenceArray<T> { private T[] Rows; public Il2CppReferenceArray(T[] rows){Rows=rows;} public int Length=>Rows.Length;
        public T this[int i] {get=>Rows[i];set=>Rows[i]=value;} }
}
namespace Il2CppInterop.Runtime.Attributes { public class HideFromIl2CppAttribute : Attribute {} }
namespace Il2CppInterop.Runtime.Injection
{ public static class ClassInjector { public static bool IsTypeRegisteredInIl2Cpp(Type t)=>true; public static void RegisterTypeInIl2Cpp<T>(){} public static void RegisterTypeInIl2Cpp(Type t){} } }
namespace UnityEngine
{
    public class Object : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase { public virtual int GetInstanceID()=>Pointer.ToInt32();public static void Destroy(Object o) { if(o is GameObject g){g.SetActive(false);g.SendMessage("OnDestroy");} } }
    public class Component : Object { public GameObject gameObject; public Transform transform=>gameObject.transform;
        public T GetComponentInChildren<T>() where T:Component=>gameObject.GetComponent<T>(); public T GetComponent<T>() where T:Component=>gameObject.GetComponent<T>(); }
    public class Behaviour : Component { protected bool On=true;public int EnabledWrites;
        public virtual bool enabled {get=>On;set{NativeBoundaryWrites.Count++;EnabledWrites++;bool changed=On!=value;On=value;
            if(changed&&gameObject?.activeInHierarchy==true)GameObject.Message(this,value?"OnEnable":"OnDisable");}} }
    public class MonoBehaviour : Behaviour { public MonoBehaviour(){} public MonoBehaviour(IntPtr p){Pointer=p;} }
    public class GameObject : Object
    {
        private static int NextId; public static int LifeAdds; private int Id=++NextId; public string name; public int layer; public bool activeInHierarchy=true;
        public Transform transform; public Scene scene; public Dictionary<Type,Component> Components=new();
        public GameObject(string n="root"){NativeBoundaryWrites.Count++;name=n;transform=new Transform{gameObject=this};}
        public override int GetInstanceID()=>Id;
        public T AddComponent<T>() where T:Component
        { NativeBoundaryWrites.Count++;var ctor=typeof(T).GetConstructor(new[]{typeof(IntPtr)}); T c=(T)(ctor!=null?ctor.Invoke(new object[]{(IntPtr)(Id+10000)}):Activator.CreateInstance(typeof(T)));
          c.gameObject=this;Components[typeof(T)]=c;if(typeof(T)==typeof(CombatTargetLifeMarker))LifeAdds++;Message(c,"OnEnable");return c; }
        public T GetComponent<T>() where T:Component=>Components.Values.OfType<T>().FirstOrDefault();
        public void SetActive(bool active) {NativeBoundaryWrites.Count++;activeInHierarchy=active;foreach(var c in Components.Values.ToArray())if(c is not Behaviour b||b.enabled)Message(c,active?"OnEnable":"OnDisable");}
        public void SendMessage(string name){foreach(var c in Components.Values.ToArray())Message(c,name);}
        internal static void Message(Component c,string name)=>c.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)?.Invoke(c,null);
    }
    public class Transform : Component {public Vector3 position,localPosition;public Vector3 localScale=Vector3.one;public Quaternion localRotation;public Transform parent;
        public Vector3 lossyScale=>parent==null?localScale:new(localScale.x*parent.lossyScale.x,localScale.y*parent.lossyScale.y,localScale.z*parent.lossyScale.z);
        public void SetParent(Transform p,bool v){parent=p;} public bool IsChildOf(Transform t)=>this==t||parent!=null&&parent.IsChildOf(t); }
    public struct Scene {public int handle;}
    public struct Vector2 { public float x,y; public Vector2(float a,float b){x=a;y=b;} }
    public struct Vector3 { public float x,y,z;public Vector3(float a,float b,float c=0){x=a;y=b;z=c;}
        public static Vector3 zero=>new();public static Vector3 one=>new(1,1,1);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z); }
    public struct Quaternion { public static Quaternion identity=>new(); }
    public struct Color { public int Value; public Color(int n){Value=n;} public static bool operator ==(Color a,Color b)=>a.Value==b.Value;
        public static bool operator !=(Color a,Color b)=>a.Value!=b.Value;public override bool Equals(object o)=>o is Color c&&c==this;public override int GetHashCode()=>Value; }
    public class Sprite : Object {}
    public class RuntimeAnimatorController : Object {}
    public class Animator : Behaviour {public RuntimeAnimatorController runtimeAnimatorController=new();public float Speed;public bool ReadFails;public int SpeedReads;
        public float GetFloat(string name){SpeedReads++;if(ReadFails)throw new Exception("animation sample unavailable");if(name!="Speed")throw new Exception("unexpected animator parameter");return Speed;} }
    public class Material : Object {}
    public class MaterialPropertyBlock {}
    public class SpriteRenderer : Behaviour { private bool Off;public int VisibilityWrites;public bool forceRenderingOff {get=>Off;set{NativeBoundaryWrites.Count++;VisibilityWrites++;Off=value;}}
        public bool flipX,flipY;public int sortingLayerID,sortingOrder;public Color color;
        public Sprite sprite;public Material sharedMaterial=new(); public void GetPropertyBlock(MaterialPropertyBlock b){}public void SetPropertyBlock(MaterialPropertyBlock b){NativeBoundaryWrites.Count++;} }
    public class Rigidbody2D : Component {public bool VelocityReadFails;public int VelocityReads;private Vector2 Velocity;
        public Vector2 velocity {get{VelocityReads++;if(VelocityReadFails)throw new Exception("physical velocity unavailable");return Velocity;}set=>Velocity=value;} }
    public static class Time {public static float time,deltaTime=.2f,timeScale=1;}
}
namespace Coatsink.Common { public class IHaglet : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase {}
    public class Haglet : IHaglet {public bool stopped,completed,executing;} }
public enum Side {Left=-1,Right=1}
public enum EnemyType {TrollWeak,TrollMedium,ToughTroll,Archer=12,Boss=5}
[Flags] public enum DamageSource {Troll=1,Knight=2,Fire=512,GreedProjectile=32768,Boulder=16,Arrow=8}
public static class NetworkBigBoss { public static bool HasWorldAuth=true,IsOnline; }
public static class ProgramDirector {public static bool Main=true;public static bool IsMainSceneActive()=>Main;}
public class Game {public enum State {Playing,Other} public State state;}
public class World {public UnityEngine.Transform gameLayer=new UnityEngine.GameObject("world").transform;}
public class Managers { public static Managers Inst=new();public World world=new();public Game game=new();public TargetCacher targetCache=new();public Kingdom kingdom=new(); }
public class Kingdom {public float Home=5;public KeyValuePair<Side,float> GetGuardPosition(Side s)=>new(s,Home*(int)s);}
public class TargetCacher { public List<Damageable> _trollPriorityTargets=new();public bool FailDeregister;public int Writes;
    public void RegisterPriorityTarget(Damageable d){NativeBoundaryWrites.Count++;Writes++;if(!_trollPriorityTargets.Contains(d))_trollPriorityTargets.Add(d);}
    public void DeregisterPriorityTarget(Damageable d){NativeBoundaryWrites.Count++;Writes++;if(FailDeregister)throw new Exception("target release failed");_trollPriorityTargets.Remove(d);} }
public class Mover : UnityEngine.Behaviour
{ public enum GoalMode {Off,Position,Object}public enum OffsetMode{Distance,Formation,Strict}public enum FacingMode {Ahead,Left=-1,Right=1,Target=2}
    public int CommandWrites;public GoalMode goalMode;public float _goalPosition,_goalSpeed,_goalOffset;public UnityEngine.GameObject _goalObject,facingTarget;
    public OffsetMode _goalOffsetMode;public FacingMode facingMode;
    public float ActualSpeedValue;public bool ActualSpeedFails;public int ActualSpeedReads;
    public float ActualSpeed {get{ActualSpeedReads++;if(ActualSpeedFails)throw new Exception("actual speed unavailable");return ActualSpeedValue;}}
    public UnityEngine.Rigidbody2D Body;public bool BodyReadFails;public int BodyReads;
    public UnityEngine.Rigidbody2D rigidbody {get{BodyReads++;if(BodyReadFails)throw new Exception("body unavailable");return Body;}}
    public void SetGoalNoHaglet(float x,float s){NativeBoundaryWrites.Count++;CommandWrites++;_goalPosition=x;_goalSpeed=s;goalMode=GoalMode.Position;_goalObject=null;}
    public void SetFacingMode(FacingMode f,UnityEngine.GameObject g){NativeBoundaryWrites.Count++;CommandWrites++;facingMode=f;facingTarget=g;}
}
public class Persistent : UnityEngine.Behaviour {}
public class Embarkee : UnityEngine.Behaviour {public bool IsEmbarked,IsTargetingEmbarkable;public static Action MutationCheck;
    public override bool enabled {get=>base.enabled;set{MutationCheck?.Invoke();base.enabled=value;}} }
public class Peasant : UnityEngine.Component {}
public class Character : UnityEngine.Behaviour {public bool grabbed;public UnityEngine.Color outfitColor=new(1),outfitSecondaryColor=new(2);
    public static int Demotes;public Func<Character> DemoteResult;public Character Demote(){using var scope=HeavyShieldRuntime.BeginNativeDemote(this);
        Demotes++;var result=DemoteResult?.Invoke();HeavyShieldRuntime.ObserveNativeDemoteReturn(scope,result);return result;} }
public class Damageable : UnityEngine.Behaviour
{
    public bool invulnerable,isDead;public int Health=10,NativeHits;public bool Allowed=true;
    public bool IsDamagedBy(DamageSource d)=>Allowed;
    public void ReceiveDamage(int damage,UnityEngine.GameObject source,DamageSource kind)=>ReceiveDamage(damage,source,kind,new());
    public void ReceiveDamage(int damage,UnityEngine.GameObject source,DamageSource kind,UnityEngine.Vector2 push)
    {using var scope=HeavyShieldCombat.EnterDamage();try {if(!HeavyShieldCombat.TryBlock(this,damage,source,kind)&&!invulnerable&&Allowed){NativeHits++;Health-=damage;isDead=Health<=0;}}
        finally{HeavyShieldCombat.ObserveDamageEnd(this);} }
}
public class ArrowAttack : UnityEngine.Object {}
public class Scanner {public UnityEngine.GameObject[] Rows=Array.Empty<UnityEngine.GameObject>();public void Refresh(bool force){}
    public int GetAll(out Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.GameObject> rows){rows=new(Rows);return Rows.Length;} }
public class Archer : UnityEngine.Behaviour
{
    public enum AttackMode {Ranged,Melee,Shield}
    public Character _character;public Damageable _damageable;public Mover _mover;public Persistent persistent;
    public UnityEngine.SpriteRenderer _spriteRenderer;public UnityEngine.Animator _animator;public Embarkee Embarkee;
    public object _guardSlot,_knight,_currentFormation,_unitController;public Scanner _enemyScanner=new();public Side _guardSide;
    public int _guardDepth,_absoluteFaceIndex;public AttackMode _desiredAttackMode,_attackMode;
    public ArrowAttack ActiveArrowAttack=new();public float _cooldown,walkSpeed=1;
    public Coatsink.Common.IHaglet behaviour=new Coatsink.Common.Haglet(),shoot=new Coatsink.Common.Haglet(),attack=new Coatsink.Common.Haglet();
    public bool StopFails,JobExitFails;public int NativeDisables,NativeEnables,NativeJobExits;public Action NativeAfterDisable;public static Action MutationCheck;
    public override bool enabled
    {get=>base.enabled;set { MutationCheck?.Invoke();if(base.enabled==value)return;base.enabled=value;
        foreach(Coatsink.Common.Haglet h in new[]{behaviour,shoot,attack}){h.stopped=!value&&!StopFails;h.executing=false;}
        if(!value){NativeDisables++;if(!JobExitFails){NativeJobExits+=(_guardSlot!=null?1:0)+(_knight!=null?1:0)+(_currentFormation!=null?1:0);_guardSlot=null;_knight=null;_currentFormation=null;}
            Managers.Inst.targetCache.DeregisterPriorityTarget(_damageable);_guardSide=Side.Left;_guardDepth=0;_absoluteFaceIndex=0;
            _desiredAttackMode=AttackMode.Ranged;_attackMode=AttackMode.Ranged;_cooldown=0;_character.outfitColor=new(10);
            _animator.runtimeAnimatorController=new();ActiveArrowAttack=null;NativeAfterDisable?.Invoke();}
        else {NativeEnables++;Managers.Inst.targetCache.RegisterPriorityTarget(_damageable);}
    }}
}
public class Enemy : UnityEngine.Component {public EnemyType Type;}
public class Troll : Enemy {public DamageSource damageSource=DamageSource.Troll;public void TryDamage(Damageable d){}public void ApplyCollisionDamage(Damageable d){} }
public class GreedArcher : Enemy {}
public class Arrow : UnityEngine.Component {private UnityEngine.Rigidbody2D _body;public int BodyReads;public UnityEngine.Rigidbody2D _rigidbody {get{BodyReads++;return _body;}set=>_body=value;}public UnityEngine.GameObject archer;public DamageSource _damageSource=DamageSource.GreedProjectile;
    public void HitObject(UnityEngine.GameObject g,bool water){}public bool TryDamage(Damageable d)=>true;}
namespace HarmonyLib
{
    public class HarmonyPatch : Attribute {}
    public static class AccessTools {public static MethodInfo Method(Type t,string n,Type[] p)=>t.GetMethod(n,p);}
    public class Patch {public MethodInfo PatchMethod;}
    public class Patches {public List<Patch> Prefixes=new(),Finalizers=new(),Postfixes=new();}
    public class Harmony
    {public static bool Installed=true;public static Patches GetPatchInfo(MethodBase m)
        {if(!Installed||m==null)return null;Type t=m.DeclaringType==typeof(Damageable)?typeof(HeavyShieldReceiveDamage)
            :m.DeclaringType==typeof(Character)?typeof(HeavyShieldNativeDemote):m.DeclaringType==typeof(Troll)?(m.Name=="TryDamage"?typeof(HeavyShieldTrollIntent):typeof(HeavyShieldTrollImpact))
            :(m.Name=="HitObject"?typeof(HeavyShieldArrowHit):typeof(HeavyShieldArrowDamage));
            var p=new Patches();p.Prefixes.Add(new(){PatchMethod=t.GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static)});
            var f=t.GetMethod("Finalizer",BindingFlags.NonPublic|BindingFlags.Static);if(f!=null)p.Finalizers.Add(new(){PatchMethod=f});
            var after=t.GetMethod("Postfix",BindingFlags.NonPublic|BindingFlags.Static);if(after!=null)p.Postfixes.Add(new(){PatchMethod=after});return p;} }
}
namespace KingdomEnhancedMod
{
    internal class BoolConfig {internal bool Value=true;}
    internal static class ModConfig {internal static BoolConfig Enabled=new(),HeavyShieldEnabled=new();}
    internal class Logger {internal int Warnings;internal void LogWarning(string s){Warnings++;} }
    internal class KingdomEnhancedPlugin {internal static KingdomEnhancedPlugin Instance=new();internal Logger LogSource=new();}
    internal static class HeavyShieldArt {internal static bool Available=true;internal static bool TryGetSoldierSprite(int n,out UnityEngine.Sprite sprite){sprite=Available?new():null;return Available;} }
    internal static class HeroArcherRuntime {internal static bool IsHero(Archer a)=>false;}
    internal static class MusketeerRuntime {internal static bool IsMusketeer(Archer a)=>false;}
    internal static class HeavyShieldIdentity
    {internal static HashSet<HeavyShieldCareerHandle> Careers=new();internal static Dictionary<Guid,HeavyShieldSavedCombatState> Saved=new();
        internal static HashSet<Guid> Unresolved=new();internal static Dictionary<IntPtr,long> Lives=new();
        internal static Dictionary<Guid,HashSet<HeavyShieldExitKind>> ExitProofs=new();internal static List<HeavyShieldExitKind> ConfirmedKinds=new();internal static bool EconomyUnknown;
        internal static long Generation=1;internal static bool TryGetCampaign(out HeavyShieldCampaignToken token)
        {token=new("campaign",(IntPtr)10,(IntPtr)20,Generation,HeavyShieldIdentityPhase.Loaded);return true;}
        internal static int Exits,Proofs;internal static bool FailUpdate;internal static bool ValidateCareer(in HeavyShieldCareerHandle h)
            =>Careers.Contains(h)&&h.World==Managers.Inst.world.gameLayer.Pointer.ToInt64()&&Lives.TryGetValue(h.Root,out var life)&&life==h.Life;
        internal static bool ValidateCampaign(in HeavyShieldCampaignToken c)=>TryGetCampaign(out var current)&&current==c;
        internal static bool TryGetSoldier(Archer carrier,out HeavyShieldCareerHandle h)
        {h=default;if(carrier==null)return false;foreach(var c in Careers)if(c.Root==carrier.gameObject.Pointer&&ValidateCareer(c)&&!Unresolved.Contains(c.Receipt)){h=c;return true;}return false;}
        internal static void ObservePoolFreshLife(UnityEngine.GameObject root,bool fresh){if(fresh)Lives[root.Pointer]=Lives.TryGetValue(root.Pointer,out var n)?n+1:1;}
        internal static void ObservePoolDespawn(UnityEngine.GameObject root,float delay)
        {if(root==null||delay>0)return;foreach(var c in Careers)if(c.Root==root.Pointer&&ValidateCareer(c)&&!Unresolved.Contains(c.Receipt)){Unresolved.Add(c.Receipt);EconomyUnknown=true;return;}}
        internal static bool UpdateCombatState(in HeavyShieldCareerHandle h,in HeavyShieldSavedCombatState s){if(FailUpdate||!ValidateCareer(h)||Unresolved.Contains(h.Receipt))return false;Saved[h.Receipt]=s;return true;}
        internal static bool ObserveNativeExitProof(in HeavyShieldCareerHandle h,HeavyShieldExitKind k)
        {if(!ValidateCareer(h)||Unresolved.Contains(h.Receipt))return false;if(!ExitProofs.TryGetValue(h.Receipt,out var proof))ExitProofs[h.Receipt]=proof=new();proof.Add(k);Proofs++;return true;}
        internal static bool ConfirmExit(in HeavyShieldCareerHandle h,HeavyShieldExitKind k,Character successor)
        {if(!ValidateCareer(h)||Unresolved.Contains(h.Receipt)||!ExitProofs.TryGetValue(h.Receipt,out var proof)||!proof.Contains(k)
            ||(k==HeavyShieldExitKind.DemotedToPeasant&&(successor==null||successor.GetComponent<Peasant>()==null))
            ||(k==HeavyShieldExitKind.Dead&&successor!=null)||!Careers.Remove(h))return false;Exits++;ConfirmedKinds.Add(k);return true;} }
}

// Unexercised integration boundary APIs, so the real root Pool callbacks can be linked directly.
public class IslandSaveData {public class ObjectData{}}
public class PrefsSaveData {} public class GlobalSaveData {} public class CampaignSaveData {}
namespace KingdomEnhancedMod
{
    internal static class HeavyShieldShopShell {internal static void Tick(bool e){}internal static void CancelPendingTransactionsBeforeNativeSave(){}}
    internal static class HeavyShieldPersistence
    {
        internal class SaveCapture{} internal class LoadCapture{} internal class CreateCapture{} internal class GenerationCapture{} internal class PrepareCapture{}
        internal static void TickBinding(){}internal static SaveCapture BeginNativeIslandSave(int a,int b,int c)=>null;
        internal static void ObserveNativeId(SaveCapture c,Persistent p,string id){}internal static void ObserveNativeIslandCaptured(SaveCapture c,IslandSaveData i){}
        internal static void EndNativeIslandSave(SaveCapture c,bool n){}internal static LoadCapture BeginNativeIslandLoad(IslandSaveData i)=>null;
        internal static void ObserveNativeLoadRow(LoadCapture c,IslandSaveData.ObjectData row,Persistent p){}
        internal static CreateCapture BeginNativeLoadRow(LoadCapture c,IslandSaveData.ObjectData row)=>null;
        internal static void EndNativeLoadRow(CreateCapture c){}internal static void EndNativeIslandLoad(LoadCapture c,bool s){}
        internal static void PrepareNativePrefs(PrefsSaveData p){}internal static void BeforeCampaignMutation(GlobalSaveData g){}
        internal static PrepareCapture BeginNativePrefsPrepare(PrefsSaveData p)=>null;
        internal static void EndNativePrefsPrepare(PrepareCapture c,bool n){}
        internal static void AfterCampaignCreated(GlobalSaveData g,CampaignSaveData c){}internal static GenerationCapture BeginNativeGeneration(CampaignSaveData c)=>null;
        internal static void EndNativeGeneration(GenerationCapture c,CampaignSaveData s,bool n){}
    }
}
