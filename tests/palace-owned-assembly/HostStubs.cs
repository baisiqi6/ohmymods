using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace Il2CppInterop.Runtime.InteropTypes {
 public class Il2CppObjectBase {
  private static long next=16; private readonly IntPtr pointer=new(++next);
  public IntPtr Pointer => pointer;
  public IntPtr ObjectClass => Il2CppInterop.Runtime.IL2CPP.TypeId(GetType());
  public T TryCast<T>() where T:class => this as T;
 }
}
namespace Il2CppInterop.Runtime {
 public static class IL2CPP {
  static readonly Dictionary<Type,IntPtr> ids=new();static readonly Dictionary<IntPtr,string> names=new();
  public static IntPtr TypeId(Type t){if(!ids.TryGetValue(t,out var p)){p=new(ids.Count+1);ids[t]=p;names[p]=t.Name;}return p;}
  public static string il2cpp_class_get_name_(IntPtr p)=>names[p];
 }
}
namespace Il2CppSystem.Collections.Generic {public class List<T>:System.Collections.Generic.List<T>{}}
namespace UnityEngine {
 public struct Vector3 {public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;} public static Vector3 zero=>new(0,0,0);public static Vector3 one=>new(1,1,1);}
 public struct Vector2 {public float x,y;public Vector2(float a,float b){x=a;y=b;}}
 public struct Quaternion {public float x,y,z,w;public static Quaternion identity=>new(){w=1};}
 [Flags] public enum HideFlags {None=0,DontUnloadUnusedAsset=32}
 public class Object:Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase {
  internal bool destroyed;public string name="";public HideFlags hideFlags;
  public static int InstantiateCalls;public static int BlockCloneCalls;public static int ThrowAtCall;public static Action<GameObject> AfterClone;
  public static bool operator ==(Object a,Object b)=>ReferenceEquals(a,b)||((ReferenceEquals(a,null)||a.destroyed)&&(ReferenceEquals(b,null)||b.destroyed));
  public static bool operator !=(Object a,Object b)=>!(a==b);public override bool Equals(object o)=>ReferenceEquals(this,o);public override int GetHashCode()=>Pointer.GetHashCode();
  public static GameObject Instantiate(GameObject src,Transform parent,bool world){InstantiateCalls++;if(src.GetComponent<LevelBlock>()!=null)BlockCloneCalls++;if(ThrowAtCall==InstantiateCalls)throw new InvalidOperationException("injected-clone-failure");var c=src.Clone(true);c.transform.parent=parent;AfterClone?.Invoke(c);return c;}
  public static void DestroyImmediate(Object o){if(o is GameObject g){g.transform.parent=null;foreach(var c in g.transform.Children.ToArray())DestroyImmediate(c.gameObject);g.destroyed=true;}else if(o!=null)o.destroyed=true;}
 }
 public class Component:Object {public GameObject gameObject;public Transform transform=>gameObject.transform;public T GetComponent<T>() where T:Component=>gameObject.GetComponent<T>();}
 public class Transform:Component {
  private Transform owner;internal readonly System.Collections.Generic.List<Transform> Children=new();
  public Vector3 localPosition;public Vector3 localScale=Vector3.one;public Quaternion localRotation=Quaternion.identity;
  public Transform parent{get=>owner;set{owner?.Children.Remove(this);owner=value;owner?.Children.Add(this);}}
  public int childCount=>Children.Count;public Transform GetChild(int i)=>Children[i];
 }
 public class GameObject:Object {
  private readonly System.Collections.Generic.List<Component> components=new();public Transform transform;public bool activeSelf=true;public int layer;
  public GameObject(string n=""){name=n;transform=Add<Transform>();}
  public bool activeInHierarchy=>activeSelf&&(transform.parent==null||transform.parent.gameObject.activeInHierarchy);
  public void SetActive(bool b)=>activeSelf=b;
  public T Add<T>() where T:Component,new(){var c=new T(){gameObject=this};components.Add(c);return c;}
  public Component Add(Type t){var c=(Component)Activator.CreateInstance(t);c.gameObject=this;components.Add(c);return c;}
  public T GetComponent<T>() where T:Component=>components.OfType<T>().FirstOrDefault();
  public T[] GetComponents<T>() where T:Component=>components.OfType<T>().ToArray();
  internal GameObject Clone(bool suffix){var c=new GameObject(name+(suffix?"(Clone)":"")){activeSelf=activeSelf,hideFlags=hideFlags,layer=layer};
   c.transform.localPosition=transform.localPosition;c.transform.localScale=transform.localScale;c.transform.localRotation=transform.localRotation;
   foreach(var source in components.Where(x=>x is not Transform)){
    var target=c.Add(source.GetType());for(Type t=source.GetType();t!=typeof(Component);t=t.BaseType)
     foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))if(!f.IsInitOnly)f.SetValue(target,f.GetValue(source));
   }
   foreach(var child in transform.Children){var cc=child.gameObject.Clone(false);cc.transform.parent=c.transform;}return c;
  }
 }
 public class SpriteRenderer:Component{} public class Animator:Component{}
 public class BoxCollider2D:Component {public Vector2 offset,size;public bool enabled=true,isTrigger,usedByEffector;}
 public class PlatformEffector2D:Component {public bool enabled=true,useOneWay,useOneWayGrouping,useSideFriction,useSideBounce,useColliderMask;public float surfaceArc,sideArc,rotationalOffset;public int colliderMask;}
 public static class Debug {public static readonly System.Collections.Generic.List<string> Logs=new();public static void Log(object s)=>Logs.Add(s.ToString());public static void LogError(object s)=>Logs.Add(s.ToString());}
}
public enum LevelBlockGroup {None,StartingAreaTight,PlayerSpawn}
public enum ContentLayers {GameLayer,BackgroundLayer}
public class LevelBlock:UnityEngine.Component {
 public bool validated,needsUnlock,maximizeMutualDistance,absoluteCenter,isDangerSource,_dimensionsComputed;
 public string validationError,validationWarning;public int generationPriority,side,safetyPolicy,_width;public float minDistance,maxDistance,_left,_right;public object cameraBlockMarker;
 public LevelBlockGroup groupOne,groupTwo,groupThree;
 public void BuildIndex(){}public void ComputeDimensions(){var ts=transform.Children.SelectMany(x=>x.Children).Select(x=>x.gameObject.GetComponent<Tile>()).Where(x=>x!=null).ToArray();_left=ts.Min(x=>x.GetLeft());_right=ts.Max(x=>x.GetRight());_width=(int)(_right-_left);_dimensionsComputed=true;}
 public int GetWidth()=>_width;public float GetLeft()=>_left;
}
public class LevelLayout:Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase {
 public Il2CppSystem.Collections.Generic.List<Placement> layout=new();public Il2CppSystem.Collections.Generic.List<LevelBlock> blocks=new();public int SharedCount,SharedCalls;
 public Dictionary<int,int> GetUsedSharedBlocks(){SharedCalls++;return Enumerable.Range(0,SharedCount).ToDictionary(x=>x,x=>x);}
}
public class Placement {public LevelBlock block;public bool fromSharedPool;}
public class Persistent:UnityEngine.Component {public bool persistObject=true,persistInactive,serializeComponents;public int mode;public string path="";}
public class TileOverrideData {public int version;public string toggleName;public bool enable;}
public class Tile:UnityEngine.Component {
 public Il2CppSystem.Collections.Generic.List<TileOverrideData> versionOverrides=new();
 public float GetLeft()=>transform.localPosition.x;public float GetRight()=>transform.localPosition.x+4;
 public bool noGround,noSnow,scatterBackdropFar,scatterBackdropNear,scatterEggMound,scatterExtremeForegroundItems,scatterFarmhouse,scatterFireflies,scatterForegroundCloudLayer1,scatterForegroundCloudLayer2,scatterForegroundCloudLayer3,scatterForegroundCloudLayer4,scatterForegroundCloudLayer5,scatterForegroundCloudLayer6,scatterGlowingTree,scatterHermesShade,scatterHill,scatterHumanoidTrees,scatterLightBeamBG,scatterMoss,scatterRocks,scatterRoots,scatterShrub,scatterShrubBig,scatterShrubBigBG,scatterShrubBirds,scatterTower,scatterTree,scatterTreeBG,scatterTreeCurtain,scatterTreeCurtainFar,scatterTreeCurtainMid,scatterWall,scatterWaterReed,scatterWaterStatue;
}
public class RandomizeSprite:UnityEngine.Component {}
public class BiomeSpriteSwapper:UnityEngine.Component {}
public class FixedTransform:UnityEngine.Component {}
public class Grass:UnityEngine.Component {}
public class Campfire:UnityEngine.Component {}
public class Beggar:UnityEngine.Component {}
public class PrefabPlaceholder:UnityEngine.Component {}
public class WreckPlaceholder:UnityEngine.Component {}
public class BoatSailPosition:UnityEngine.Component {}
public class Ghost:UnityEngine.Component {}
public class InfoText:UnityEngine.Component {}
public class Tower:UnityEngine.Component {}
public class Beach:UnityEngine.Component {}
public class SpriteRendererFX:UnityEngine.Component {}
public class GAPS:UnityEngine.Component {}
public class PrefabID:UnityEngine.Component {}
public class FormationBlocker:UnityEngine.Component {}
