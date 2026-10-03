using System.Collections;
using KingdomEnhancedMod;
namespace HarmonyLib
{
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)]public class HarmonyPatch:Attribute{public HarmonyPatch(Type type,string name){}public HarmonyPatch(Type type){} }
 public class HarmonyPostfix:Attribute{}public class HarmonyPrefix:Attribute{}
}
namespace UnityEngine
{
 public class Object
 {
  static long next;public IntPtr Pointer=(IntPtr)Interlocked.Increment(ref next);public bool Destroyed;public string name="Object";
  public int GetInstanceID()=>(int)Pointer;public T TryCast<T>() where T:class=>this as T;
  public static bool operator ==(Object a,Object b)=>ReferenceEquals(a,b)||((a is null||a.Destroyed)&&(b is null||b.Destroyed));public static bool operator !=(Object a,Object b)=>!(a==b);
  public override bool Equals(object obj)=>ReferenceEquals(this,obj);public override int GetHashCode()=>Pointer.GetHashCode();
  public static void Destroy(Object obj){obj.Destroyed=true;}
  public static T[] FindObjectsOfType<T>(){Scene.Scans++;if(typeof(T)==typeof(Cat))return Scene.Cats.Where(x=>x.gameObject.activeInHierarchy).Cast<T>().ToArray();if(typeof(T)==typeof(Farmhouse))return Scene.Farms.Where(x=>x.gameObject.activeInHierarchy).Cast<T>().ToArray();throw new Exception("unexpected scene scan");}
 }
 public class Component:Object{public GameObject gameObject;public Transform transform=>gameObject.transform;public bool enabled=true;public bool isActiveAndEnabled=>enabled&&gameObject.activeInHierarchy;public T GetComponent<T>() where T:class=>gameObject.GetComponent<T>();}
 public class MonoBehaviour:Component{public void StartCoroutine(IEnumerator routine){Scene.Routines.Add(routine);}}
 public class GameObject:Object
 {
  readonly List<Component> components=new();public Transform transform;public bool activeInHierarchy=true;public TestScene scene=new();
  public GameObject(){transform=new Transform{gameObject=this};}
  public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};components.Add(c);return c;}
  public T GetComponent<T>() where T:class=>components.OfType<T>().FirstOrDefault();public bool CompareTag(string tag)=>name==tag;public void SetActive(bool active){activeInHierarchy=active;}
 }
 public class Transform:Component
 {
  public Vector3 position,localScale=new(1,1,1);public Transform parent;public Vector3 lossyScale=>localScale;
  public bool IsChildOf(Transform ancestor){for(var t=this;t!=null;t=t.parent)if(t==ancestor)return true;return false;}
 }
 public class TestScene{public int handle=1;public bool Valid=true;public bool IsValid()=>Valid;}
 public struct Vector3{public float x,y,z;public Vector3(float x,float y=0,float z=0){this.x=x;this.y=y;this.z=z;}public static Vector3 one=>new(1,1,1);}
 public struct Vector2{public float x,y;public Vector2(float x,float y=0){this.x=x;this.y=y;}}
 public struct Bounds{public Vector3 extents;}
 public struct Rect{public float xMin,xMax;public Rect(float min,float max){xMin=min;xMax=max;}}
 public class Rigidbody2D:Component{public Vector2 velocity;public Vector2 linearVelocity=>velocity;}
 public class Collider2D:Component{public bool Throw;public Bounds Value=new(){extents=new(.25f)};public Bounds bounds{get{if(Throw)throw new Exception("bounds unreadable");return Value;}}}
 public static class Mathf{public static float Clamp(float value,float min,float max)=>value<min?min:value>max?max:value;public static bool Approximately(float a,float b)=>MathF.Abs(a-b)<.00001f;public static float Abs(float value)=>MathF.Abs(value);}
 public static class Time{public static float time=100,fixedTime=100,timeScale=1;}
 public static class Random{public static float Fraction=.5f;public static float Range(float min,float max)=>min+(max-min)*Fraction;}
 public struct Color{public static Color white=>new();}public struct Quaternion{public static Quaternion identity=>new();}public class WaitForSeconds{public WaitForSeconds(float value){}}
 public static class Resources{public static T Load<T>(string path)where T:class=>null;}
}
public enum Side{Left=-1,Right=1}
public class Game{public enum State{Playing,Menu,Loading}public State state=State.Playing;}
public class IslandSaveData{public static bool isSavingGame;public static IslandSaveData CurrentlySavingIsland;}
public class Managers{public static Managers Inst;public World world;public Kingdom kingdom;public Game game=new();public Holder holder=new();}
public class World:UnityEngine.MonoBehaviour{public UnityEngine.Transform gameLayer;public void OnLevelLoaded(){}}
public class Kingdom:UnityEngine.MonoBehaviour{public float Left=-30,Right=30;public float GetBorderSideIntact(Side side)=>side==Side.Left?Left:Right;}
public class Farmhouse:UnityEngine.MonoBehaviour{public List<Farmland> farmlands=new();}
public class Farmland:UnityEngine.Component{public Farmhouse farmhouse;}
public class GAPS:UnityEngine.Component{public UnityEngine.Rect Region;public bool Throw;public Action OnRead;public UnityEngine.Rect GetRegion(){OnRead?.Invoke();if(Throw)throw new Exception("GAPS unreadable");return Region;}}
public class Embarkable:UnityEngine.Object{}
public class Embarkee:UnityEngine.Component{public bool IsEmbarked,IsTargetingEmbarkable;public Embarkable EmbarkableTarget;}
public class Droppable:UnityEngine.Component{public bool pickedUp;public bool Subscribed;}
public class Mover:UnityEngine.Component
{
 public enum GoalMode{Off,Position,Object}public enum OffsetMode{Distance,Formation,Strict}
 public static float PositionEpsilon=.0625f;public GoalMode goalMode;public UnityEngine.GameObject _goalObject;public float _goalPosition,_goalSpeed,_goalOffset,_pauseTimeout;public OffsetMode _goalOffsetMode;
 public UnityEngine.Rigidbody2D rigidbody;public bool movingToGoal;public int SetCalls,Stops;public bool ThrowSet,ThrowStop;public Action OnSet;
 public bool IsPaused()=>_pauseTimeout>0;
 public object SetGoal(float goal,float speed){SetCalls++;OnSet?.Invoke();if(ThrowSet)throw new Exception("set failure");goalMode=GoalMode.Position;_goalPosition=goal;_goalSpeed=speed;_goalObject=null;movingToGoal=true;return new object();}
 public void ObjectGoal(UnityEngine.GameObject goal,float speed,float offset=.2f,OffsetMode mode=OffsetMode.Distance){goalMode=GoalMode.Object;_goalObject=goal;_goalSpeed=speed;_goalOffset=offset;_goalOffsetMode=mode;movingToGoal=true;}
 public void Stop(){Stops++;Trace.Add("Stop");if(ThrowStop)throw new Exception("stop failure");goalMode=GoalMode.Off;movingToGoal=false;}
 public static List<string> Trace=new();
}
// Native FSM order models the separately audited 2.4 methods, not the movement helper.
public class StateMachine:UnityEngine.Object
{
 public int Current=3,Previous=3,_queuedState;public bool _executeQueuedState,_shouldRunCallbacks=true;public UnityEngine.MonoBehaviour _owner;
 public Dictionary<int,State> _states=new();public bool ThrowQueue;public Action OnQueue;
 public class State:UnityEngine.Object{public bool IsRunning=true;public bool CoroutineExists=>CoroutineStack.Count>0;public Stack<Cat._FarmCatRoutine_d__74> CoroutineStack=new();public Field _sourcePC=new();}
 public class Field{public string Name="<>1__state";public NativeType DeclaringType=new();}
 public class NativeType{public string FullName="Cat+<FarmCatRoutine>d__74";}
 public void GoToState(int state){Mover.Trace.Add("Queue");OnQueue?.Invoke();if(ThrowQueue)throw new Exception("queue failure");_queuedState=state;_executeQueuedState=true;}
 public void Update()
 {
  if(!_executeQueuedState)return;
  var cat=(Cat)_owner;cat.OnChangeState(Current,_queuedState);
  _states[Current].IsRunning=false;Previous=Current;Current=_queuedState;
  var state=_states[Current];if(state._sourcePC!=null)state.CoroutineStack.Peek().__1__state=0;state.IsRunning=true;_executeQueuedState=false;
 }
 public void StepCoroutine(){if(Current==3&&_states[Current].IsRunning)_states[Current].CoroutineStack.Peek().MoveNext();}
}
public class Cat:UnityEngine.MonoBehaviour
{
 public enum State{Roaming=0,Fleeing=1,Following=2,FarmCating=3,Grabbed=4}
 public bool domesticated,playingWithCoin,Sleeping;public Farmhouse farmHouse;public Cat followingPlayer;public Mover mover;public StateMachine fsm;public Droppable playCoin;
 public bool ThrowReset,ThrowPlay;public Action OnReset,OnPlay;public int ResetCalls,PlayCalls,Catches;public Action NewActivity;
 public float PlayStamp,MouseStamp;public bool ThrowPlayGet,ThrowMouseGet,ThrowPlaySet,ThrowMouseSet,CommitPlayThrow,CommitMouseThrow;public int PlayWrites,MouseWrites;public Action OnPlayRead,OnMouseRead,OnPlayStamp,OnMouseStamp;
 public float nextPlayWithCoinTime{get{OnPlayRead?.Invoke();if(ThrowPlayGet)throw new Exception("play timer getter");return PlayStamp;}set{PlayWrites++;if(ThrowPlaySet)throw new Exception("play timer setter");PlayStamp=value;OnPlayStamp?.Invoke();if(CommitPlayThrow)throw new Exception("play committed then threw");}}
 public float nextCatchMouseTime{get{OnMouseRead?.Invoke();if(ThrowMouseGet)throw new Exception("mouse timer getter");return MouseStamp;}set{MouseWrites++;if(ThrowMouseSet)throw new Exception("mouse timer setter");MouseStamp=value;OnMouseStamp?.Invoke();if(CommitMouseThrow)throw new Exception("mouse committed then threw");}}
 public void SetColor(UnityEngine.Color color){}
 public void OnEnable(){}public void OnDisable(){}public void Update(){fsm.Update();fsm.StepCoroutine();}
 public void ResetPlayCoin(){ResetCalls++;Mover.Trace.Add("ResetCoin");OnReset?.Invoke();if(ThrowReset)throw new Exception("cleanup failure");if(playCoin!=null)playCoin.Subscribed=false;playCoin=null;}
 public void OnAnimPlayWithCoin(){PlayCalls++;Mover.Trace.Add("EndPlay");OnPlay?.Invoke();if(ThrowPlay)throw new Exception("end play failure");if(NetworkBigBoss.HasWorldAuth)playingWithCoin=false;}
 public void OnChangeState(int oldState,int newState){mover.Stop();if(oldState==3&&playCoin!=null){ResetPlayCoin();return;}if(oldState==3)Sleeping=false;}
 public class _FarmCatRoutine_d__74:UnityEngine.Object
 {
  public Cat __4__this;public int __1__state=2;
  public bool MoveNext()
  {
   if(__1__state==0){if(__4__this.NewActivity!=null)__4__this.NewActivity();else __4__this.mover.SetGoal(__4__this.farmHouse.transform.position.x+1,.6f);if(__1__state==0)__1__state=2;return true;}
   if((__1__state==1||__1__state==2||__1__state==4||__1__state==8)&&!__4__this.mover.movingToGoal){__4__this.Catches++;__1__state=3;}
   return true;
  }
 }
}
public static class NetworkBigBoss{public static bool HasWorldAuth=true,IsOnline;}
public class BiomeHolder{public static BiomeHolder Inst=new();public static int GreeceBiomeIndex=5,NorselandsBiomeIndex=3;public int BiomeIndex=5;public string[] biomePathStrings=new string[6];}
public class Holder{public Cat catPrefab;}
public class BiomeData:UnityEngine.Object{public BiomeAssets biomeSpecificAssets;public BiomeSwapData swapData;}
public class BiomeAssets{public List<Character> uniqueCharacters=new();}public class Character:UnityEngine.Component{}
public class BiomeSwapData{public List<PrefabSwapData> prefabSwapPool=new();}public class PrefabSwapData{public UnityEngine.GameObject swap;}
public static class Scene{public static List<Cat> Cats=new();public static List<Farmhouse> Farms=new();public static List<IEnumerator> Routines=new();public static int Scans;}
public static class Pool
{
 public static int Spawned;public static T SpawnOrInstantiate<T>(T prefab,UnityEngine.Vector3 position,UnityEngine.Quaternion rot,UnityEngine.Transform layer)where T:Cat
 {Spawned++;var cat=Fixture.MakeCat(null,position.x,layer);cat.transform.position=position;Scene.Cats.Add(cat);return (T)cat;}
 public static void DespawnOrDestroy(UnityEngine.GameObject go){go.SetActive(false);if(go.GetComponent<Cat>() is Cat c)Cat_OnDisable_FarmMovement_Patch.Prefix(c);}
}
namespace BepInEx.Unity.IL2CPP.Utils.Collections{public static class Extensions{public static IEnumerator WrapToIl2Cpp(this IEnumerator source)=>source;}}
namespace KingdomEnhancedMod
{
 public static class ModConfig{public class Setting{public bool Value=true;}public static Setting Enabled=new();}
 public class KingdomEnhancedPlugin{public static KingdomEnhancedPlugin Instance=new();public Log LogSource=new();public class Log{public bool Throw;public List<string> Lines=new();public void LogInfo(object text){if(Throw)throw new Exception("logger failure");Lines.Add(text.ToString());}public void LogWarning(object text)=>LogInfo(text);public void LogError(object text)=>LogInfo(text);}}
 public static class GreekScaleScope{public static void ApplyY(UnityEngine.Transform t,float y){t.localScale=new(t.localScale.x,y,t.localScale.z);}}
 public static class ScaleRegistryHolder{public static void Register(Mover mover,float y){}}
}
