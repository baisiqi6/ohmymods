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
    public class Il2CppReferenceArray<T> : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase { private T[] Rows; public Il2CppReferenceArray(T[] rows){Rows=rows;} public int Length=>Rows.Length;
        public T this[int i] {get=>Rows[i];set=>Rows[i]=value;} }
}
namespace Il2CppInterop.Runtime.Attributes { public class HideFromIl2CppAttribute : Attribute {} }
// The ANDROID diagnostic in HeavyShieldRuntime's existing attach catch reads Signature; the host
// boundary only needs the exception shape, never a live missing ICall.
namespace Il2CppInterop.Runtime
{
    public class MissingIl2CppInternalCallException : Exception
    {
        public string Signature;
        public MissingIl2CppInternalCallException(string message) : base(message) { }
    }
}
namespace Il2CppInterop.Runtime.Injection
{ public static class ClassInjector { public static bool IsTypeRegisteredInIl2Cpp(Type t)=>true; public static void RegisterTypeInIl2Cpp<T>(){} public static void RegisterTypeInIl2Cpp(Type t){} } }
namespace UnityEngine
{
    public class Object : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase { public virtual int GetInstanceID()=>Pointer.ToInt32();public static void Destroy(Object o) { if(o is GameObject g){g.SetActive(false);g.SendMessage("OnDestroy");} } }
    public class Component : Object { public GameObject gameObject; public Transform transform=>gameObject.transform;
        public T GetComponentInChildren<T>() where T:Component=>gameObject.GetComponent<T>(); public T GetComponent<T>() where T:Component=>gameObject.GetComponent<T>(); }
    public class Behaviour : Component { protected bool On=true;public int EnabledWrites;public bool isActiveAndEnabled=>enabled&&gameObject!=null&&gameObject.activeInHierarchy;
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
    // Test geometry supports translation and signed/non-unit scale; rotations are
    // intentionally not simulated. World/local positions must stay consistent.
    public class Transform : Component {public Vector3 localPosition;public Vector3 localScale=Vector3.one;public Quaternion localRotation;public Transform parent;
        public Vector3 position {get=>parent==null?localPosition:parent.TransformPoint(localPosition);set=>localPosition=parent==null?value:parent.InverseTransformPoint(value);}
        public Vector3 lossyScale=>parent==null?localScale:new(localScale.x*parent.lossyScale.x,localScale.y*parent.lossyScale.y,localScale.z*parent.lossyScale.z);
        public Vector3 TransformPoint(Vector3 p){var w=position;var s=lossyScale;return new(w.x+p.x*s.x,w.y+p.y*s.y,w.z+p.z*s.z);}
        public Vector3 InverseTransformPoint(Vector3 p){var w=position;var s=lossyScale;return new((p.x-w.x)/s.x,(p.y-w.y)/s.y,(p.z-w.z)/s.z);}
        public void SetParent(Transform p,bool v){var w=position;parent=p;if(v)position=w;} public bool IsChildOf(Transform t)=>this==t||parent!=null&&parent.IsChildOf(t); }
    public struct Scene {public int handle;}
    public struct LayerMask { public int value; }
    public struct ContactFilter2D { public LayerMask layerMask; public bool useLayerMask,useTriggers,useDepth,useOutsideDepth,useNormalAngle,useOutsideNormalAngle; }
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
    // Synthetic renderer boundary: the real Renderer owns enabled/forceRenderingOff; SpriteRenderer
    // only adds sprite properties. Android 2.4 has no forceRenderingOff ICall, so the ANDROID stub
    // throws (like flipY) to prove the compat path never reads or writes it.
    public class Renderer : Behaviour { public int VisibilityWrites;
#if ANDROID
        public static int ForceOffReads,ForceOffWrites;
        public bool forceRenderingOff{get{ForceOffReads++;throw new Exception("Android 2.4 Renderer has no forceRenderingOff ICall");}
            set{ForceOffWrites++;throw new Exception("Android 2.4 Renderer has no forceRenderingOff ICall");}}
#else
        private bool Off;
        public bool forceRenderingOff {get=>Off;set{NativeBoundaryWrites.Count++;VisibilityWrites++;Off=value;}}
#endif
    }
    public class SpriteRenderer : Renderer { public bool flipX;
#if ANDROID
        public static int FlipYReads,FlipYWrites;
        public bool flipY{get{FlipYReads++;throw new Exception("Android 2.4 SpriteRenderer has no flipY getter");}set{FlipYWrites++;throw new Exception("Android 2.4 SpriteRenderer has no flipY setter");}}
#else
        public bool flipY;
#endif
        public int sortingLayerID,sortingOrder;public Color color;
        public Sprite sprite;public Material sharedMaterial=new(); public void GetPropertyBlock(MaterialPropertyBlock b){}public void SetPropertyBlock(MaterialPropertyBlock b){NativeBoundaryWrites.Count++;} }
    public class Rigidbody2D : Component {public bool VelocityReadFails;public int VelocityReads;private Vector2 Velocity;
        public Vector2 velocity {get{VelocityReads++;if(VelocityReadFails)throw new Exception("physical velocity unavailable");return Velocity;}set=>Velocity=value;} }
    public static class Time {public static float time,deltaTime=.2f,timeScale=1,unscaledTime;public static int frameCount {get;set;}public static double unscaledTimeAsDouble {get;set;}}
}
#if ANDROID
namespace Il2CppCoatsink.Common { public class IHaglet : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase {}
    public class Haglet : IHaglet {public bool stopped,completed,executing;} }
namespace Coatsink.Common { public class IHaglet : Il2CppCoatsink.Common.Haglet {}
    public class Haglet : IHaglet {} }
#else
namespace Coatsink.Common { public class IHaglet : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase {}
    public class Haglet : IHaglet {public bool stopped,completed,executing;} }
#endif
public enum Side {Left=-1,Right=1}
public enum EnemyType {TrollWeak,TrollMedium,ToughTroll,Archer=12,Boss=5}
[Flags] public enum DamageSource {Troll=1,Knight=2,Fire=512,GreedProjectile=32768,Boulder=16,Arrow=8}
public static class NetworkBigBoss { public static bool HasWorldAuth=true,IsOnline; }
public static class ProgramDirector {public static bool Main=true;public static bool IsMainSceneActive()=>Main;}
public class Game {public enum State {Playing,Other} public State state;}
public class World {public UnityEngine.Transform gameLayer=new UnityEngine.GameObject("world").transform;}
public class Managers { public static Managers Inst=new();public World world=new();public Game game=new();public TargetCacher targetCache=new();public Kingdom kingdom=new(); }
public class Kingdom {public Player playerOne,playerTwo;public float Home=5;public KeyValuePair<Side,float> GetGuardPosition(Side s)=>new(s,Home*(int)s);}
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
public class Scanner
{
    public UnityEngine.GameObject[] Rows=Array.Empty<UnityEngine.GameObject>();
    public UnityEngine.Transform observer;
    public float range=6,rangeBehind=6,_height=6,_interval=.5f,_lastRefresh;
    public bool _excludeDead; public string[] _tags; public object additionalRequirements;
    public UnityEngine.ContactFilter2D _contactFilter=new(){layerMask=new(){value=65536},useLayerMask=true,useTriggers=true};
    public int _numFiltered, Refreshes; public Action OnRefresh;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.GameObject> _filtered=new(new UnityEngine.GameObject[12]);
    public void Refresh(bool force)
    {
        if(!force && UnityEngine.Time.time-_lastRefresh<_interval)return;
        Refreshes++;_lastRefresh=UnityEngine.Time.time;_numFiltered=Math.Min(Rows.Length,12);
        for(int i=0;i<_numFiltered;i++)_filtered[i]=Rows[i]; // Deliberately retain the stale tail like native Scanner.
        OnRefresh?.Invoke();
    }
    public int GetAll(out Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.GameObject> rows)
    {
        Refresh(false);int n=0;for(int i=0;i<_numFiltered;i++)if(_filtered[i]!=null&&_filtered[i].activeInHierarchy)_filtered[n++]=_filtered[i];
        _numFiltered=n;rows=_filtered;return n;
    }
}
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
    public class HarmonyPatch : Attribute {public HarmonyPatch(){}public HarmonyPatch(Type type,string method){}}
    public class HarmonyPostfix : Attribute {}
    public static class AccessTools {public static MethodInfo Method(Type t,string n,Type[] p)=>t.GetMethod(n,p);}
    public class Patch {public MethodInfo PatchMethod;}
    // Deliberately no PatchManager type: the real helper must use the live compat query.
    public class PatchInfo { public Patch[] prefixes, finalizers, postfixes; }
    public class Patches
    {
        public System.Collections.ObjectModel.ReadOnlyCollection<Patch> Prefixes, Finalizers, Postfixes;
        public Patches(List<Patch> p, List<Patch> f, List<Patch> after)
        { Prefixes=p.AsReadOnly(); Finalizers=f.AsReadOnly(); Postfixes=after.AsReadOnly(); }
    }
    public class Harmony
    {public static bool Installed=true;public static Patches GetPatchInfo(MethodBase m)
        {if(!Installed||m==null)return null;Type t=m.DeclaringType==typeof(Damageable)?typeof(HeavyShieldReceiveDamage)
            :m.DeclaringType==typeof(Character)?typeof(HeavyShieldNativeDemote):m.DeclaringType==typeof(Troll)?(m.Name=="TryDamage"?typeof(HeavyShieldTrollIntent):typeof(HeavyShieldTrollImpact))
            :(m.Name=="HitObject"?typeof(HeavyShieldArrowHit):typeof(HeavyShieldArrowDamage));
            var p=new List<Patch>();p.Add(new(){PatchMethod=t.GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static)});
            var finals=new List<Patch>();var f=t.GetMethod("Finalizer",BindingFlags.NonPublic|BindingFlags.Static);if(f!=null)finals.Add(new(){PatchMethod=f});
            var posts=new List<Patch>();var after=t.GetMethod("Postfix",BindingFlags.NonPublic|BindingFlags.Static);if(after!=null)posts.Add(new(){PatchMethod=after});return new Patches(p,finals,posts);} }
}
namespace KingdomEnhancedMod
{
    internal class BoolConfig {internal bool Value=true;}
    internal static class ModConfig {internal static BoolConfig Enabled=new(),HeavyShieldEnabled=new();}
    internal class Logger {internal int Warnings,Infos;internal void LogWarning(string s){Warnings++;}internal void LogInfo(string s){Infos++;} }
    internal class KingdomEnhancedPlugin {internal static KingdomEnhancedPlugin Instance=new();internal Logger LogSource=new();}
    internal static class HeavyShieldArt {internal static bool Available=true;internal static bool TryGetSoldierSprite(int n,out UnityEngine.Sprite sprite){sprite=Available?new():null;return Available;} }
    internal static class HeroArcherRuntime {internal static bool IsHero(Archer a)=>false;}
    internal static class MusketeerRuntime {internal static bool IsMusketeer(Archer a)=>false;}
    internal static class HeavyShieldIdentity
    {internal sealed class Session { internal bool KeyReady=true,Unknown=false,StageFault=false; internal int Land=0; internal long World; }
        internal static Session Current=new();
        internal static HeavyShieldIdentityPhase CurrentPhase=HeavyShieldIdentityPhase.Loaded;
        internal static HashSet<HeavyShieldCareerHandle> Careers=new();internal static Dictionary<Guid,HeavyShieldSavedCombatState> Saved=new();
        internal static HashSet<Guid> Unresolved=new();internal static Dictionary<IntPtr,long> Lives=new();
        internal static Dictionary<Guid,HashSet<HeavyShieldExitKind>> ExitProofs=new();internal static List<HeavyShieldExitKind> ConfirmedKinds=new();internal static bool EconomyUnknown;
        internal static long Generation=1;internal static bool TryGetCampaign(out HeavyShieldCampaignToken token)
        {token=new("campaign",(IntPtr)10,(IntPtr)20,Generation,CurrentPhase);return true;}
        internal static bool ThrowRepairReadback;
        internal static bool TryGetCombatState(in HeavyShieldCareerHandle h,out HeavyShieldSavedCombatState state)
        {if(ThrowRepairReadback)throw new Exception("controlled state readback fault");return Saved.TryGetValue(h.Receipt,out state)&&ValidateCareer(h);}
        // Identity's real commit is tested in heavy-shield-identity. This controlled dependency
        // lets this suite exercise Runtime/Repair ordering and gates, not native identity proof.
        internal static bool TryCompleteRepair(in HeavyShieldCareerHandle h,in HeavyShieldRepairCompletion c,HeavyShieldBlockPolicy policy)
        {
            if(!ValidateCareer(h)||!Saved.TryGetValue(h.Receipt,out var old)||old.Durability!=c.ExpectedDurability
                ||old.PendingBreak||old.RetirementUnknown||!policy.TryRepair(c.ExpectedDurability))return false;
            Saved[h.Receipt]=new(40,false,false);return true;
        }
        internal static int Exits,Proofs;internal static bool FailUpdate;internal static bool ValidateCareer(in HeavyShieldCareerHandle h)
            =>Careers.Contains(h)&&h.World==Managers.Inst.world.gameLayer.Pointer.ToInt64()&&Lives.TryGetValue(h.Root,out var life)&&life==h.Life;
        internal static bool ValidateCampaign(in HeavyShieldCampaignToken c)=>TryGetCampaign(out var current)
            &&c.Guid==current.Guid&&c.Global==current.Global&&c.Campaign==current.Campaign&&c.OwnerGeneration==current.OwnerGeneration
            &&c.Phase is HeavyShieldIdentityPhase.Allocated or HeavyShieldIdentityPhase.Staged or HeavyShieldIdentityPhase.Loaded;
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
public class IslandSaveData {public static bool isSavingGame; public class ObjectData{}}
public class PrefsSaveData {} public class GlobalSaveData {} public class CampaignSaveData {}
namespace KingdomEnhancedMod
{
    internal static class HeavyShieldShopShell
    {
        internal static bool SiteEnabled; internal static HeavyShieldRepairSite Site;
        internal static bool TryGetRepairSite(out HeavyShieldRepairSite site){site=Site;return SiteEnabled;}
        internal static void Tick(bool e){} internal static void CancelPendingTransactionsBeforeNativeSave(){}
    }
    internal static class HeavyShieldPersistence
    {
        internal static bool SerializationInProgress; internal static Action PrepareFinalizerObserver;
        internal class SaveCapture{} internal class LoadCapture{} internal class CreateCapture{} internal class GenerationCapture{} internal class PrepareCapture{}
        internal static void TickBinding(){}internal static SaveCapture BeginNativeIslandSave(int a,int b,int c)=>null;
        internal static void ObserveNativeId(SaveCapture c,Persistent p,string id){}internal static void ObserveNativeIslandCaptured(SaveCapture c,IslandSaveData i){}
        internal static void EndNativeIslandSave(SaveCapture c,bool n){}internal static LoadCapture BeginNativeIslandLoad(IslandSaveData i)=>null;
        internal static void ObserveNativeLoadRow(LoadCapture c,IslandSaveData.ObjectData row,Persistent p){}
        internal static CreateCapture BeginNativeLoadRow(LoadCapture c,IslandSaveData.ObjectData row)=>null;
        internal static void EndNativeLoadRow(CreateCapture c){}internal static void EndNativeIslandLoad(LoadCapture c,bool s){}
        internal static void PrepareNativePrefs(PrefsSaveData p){}internal static void BeforeCampaignMutation(GlobalSaveData g){}
        internal static PrepareCapture BeginNativePrefsPrepare(PrefsSaveData p)=>null;
        internal static void EndNativePrefsPrepare(PrepareCapture c,bool n){SerializationInProgress=false;PrepareFinalizerObserver?.Invoke();}
        internal static void AfterCampaignCreated(GlobalSaveData g,CampaignSaveData c){}internal static GenerationCapture BeginNativeGeneration(CampaignSaveData c)=>null;
        internal static void EndNativeGeneration(GenerationCapture c,CampaignSaveData s,bool n){}
    }
}
