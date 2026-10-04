// Typed ANDROID host doubles for the shared il2cpp/PatchWorld_DeerPopulation.cs and the shared
// 27-case harness (../Program.cs). Game types live under namespace Il2Cpp (Il2CppInterop
// namespace-prefix mode); the UnityEngine/HarmonyLib doubles are the same managed shapes as the
// PC host. ModConfig mirrors the actual Android platform config surface: the session
// Setting<bool> master switch plus the default-off MelonPreferences_Entry<bool>
// DeerPopulationEnabled (value toggle only, no native apply at load). BiomeHolder's Greek
// value is the checked Android metadata literal 5 — the PC host's 3 stays a surrogate there.
// The PopulationController doubles model the four consumed native-private fields as public
// get/set properties (the interop promotes them) and count controller-level interop reads so
// the default-off fast path can be asserted as zero-interop. Doubles only model consumed
// shapes: host evidence, not Unity/Harmony/IL2CPP gameplay proof.
#if !ANDROID
#error The Android typed host compiles the shared deer source with ANDROID defined.
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace HarmonyLib
{
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method)]public class HarmonyPatch:Attribute {public Type Target;public string Method;public HarmonyPatch(Type type,string method){Target=type;Method=method;}}
 public class HarmonyPrefix:Attribute{}
 public class HarmonyPostfix:Attribute{}
 public class HarmonyFinalizer:Attribute{}
}
namespace UnityEngine
{
 public class Object
 {
  static int next;public int Id=Interlocked.Increment(ref next);public IntPtr Pointer {get;set;}public bool ThrowIdentity;
  public Object(){Pointer=(IntPtr)Id;}public int GetInstanceID(){if(ThrowIdentity)throw new InvalidOperationException("injected identity read");return Id;}
 }
 public class Component:Object {public GameObject gameObject;public Transform transform=>gameObject?.transform;}
 public struct Scene {public int handle;}
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
 public class GameObject:Object
 {
  public string name="Deer";public bool activeInHierarchy=true;public Scene scene=new(){handle=10};public Transform transform;
  readonly List<Component> components=new();public GameObject(){transform=new Transform{gameObject=this};}
  public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};components.Add(c);return c;}
  public T GetComponent<T>() where T:Component=>components.OfType<T>().FirstOrDefault();
 }
 public class Transform:Component {public Transform parent;public Vector3 localScale=new(1,.55f,1);public bool IsChildOf(Transform t){for(var p=this;p!=null;p=p.parent)if(p==t)return true;return false;}}
 public static class Time {public static float deltaTime=.25f;}
}
namespace MelonLoader
{
 // Minimal shape of the deployed MelonPreferences_Entry<T>: the only consumed surface is the
 // Value field (the shared deer source reads it through var; the real entry has no methods
 // used by this patch family).
 public sealed class MelonPreferences_Entry<T> {public T Value;public MelonPreferences_Entry(T value){Value=value;}}
}
namespace Il2Cpp
{
 public class Deer:UnityEngine.Component {int coins=3;public int CoinWrites;public int numCoinsDropped {get=>coins;set{CoinWrites++;coins=value;}}}
 public class Steed:UnityEngine.Component{}
 public class Hind:UnityEngine.Component{}
 public class World:UnityEngine.Object {public UnityEngine.Transform gameLayer=new UnityEngine.GameObject().transform;public bool IsWinter;}
 public class Game {public State state=State.Playing;public bool playingOrInMenuWithClient=true;public enum State {Playing,NetworkClientPlaying,Menu,Loading}}
 public class Managers {public static Managers Inst=new();public World world=new();public Game game=new();}
 public class BiomeHolder {public static BiomeHolder Inst=new();public const int GreeceBiomeIndex=5;public int BiomeIndex=GreeceBiomeIndex;}
 public static class NetworkBigBoss {public static bool HasWorldAuth=true;}
 public class PopulationController:UnityEngine.Component
 {
  readonly float[] fields={.125f,0f,.0625f,3f};
  public readonly int[] Writes=new int[4];
  public int FailWriteField=-1,FailReadField=-1;public bool CommitBeforeWriteFault;
  int interopReads;bool throwInterop;bool enabledField=true,useField;UnityEngine.GameObject prefabField;
  public int InteropReads=>interopReads;public bool ThrowInterop{get=>throwInterop;set=>throwInterop=value;}
  public void ResetInteropReads()=>interopReads=0;
  void Read(){interopReads++;if(throwInterop)throw new InvalidOperationException("injected interop read");}
  // The earliest native access in Begin is controller.Pointer; hide the base auto-property so the
  // typed host can count (and, with ThrowInterop, reject) that read exactly like the other native
  // members. The base storage stays the single value store.
  public new IntPtr Pointer {get{Read();return base.Pointer;}set{base.Pointer=value;}}
  public UnityEngine.GameObject prefab {get{Read();return prefabField;}set{prefabField=value;}}
  public bool enabled {get{Read();return enabledField;}set{enabledField=value;}}
  public bool useBiomeCritters {get{Read();return useField;}set{useField=value;}}
  public bool SpecialWinter;
  public float minimumRegionSize=20,updateInterval=3,RegionWidth=80;
  public Action NativeBody;public int NativeCalls,SpawnDecisions,Population;
  float elapsed,target;public int ElapsedPatchWrites,TargetPatchWrites;
  public float _elapsedTime {get=>elapsed;set{ElapsedPatchWrites++;elapsed=value;}}
  public float _targetDensity {get=>target;set{TargetPatchWrites++;target=value;}}
  float Get(int field){Read();if(FailReadField==field)throw new InvalidOperationException("injected read "+field);return fields[field];}
  void Set(int field,float value)
  {
   if(FailWriteField==field)
   {FailWriteField=-1;if(CommitBeforeWriteFault){Writes[field]++;fields[field]=value;}throw new InvalidOperationException("injected write "+field);}
   Writes[field]++;fields[field]=value;
  }
  public float density {get{Read();return Get(0);}set=>Set(0,value);}
  public float winterDensityDefault {get{Read();return Get(1);}set=>Set(1,value);}
  public float winterDensitySpecial {get{Read();return Get(2);}set=>Set(2,value);}
  public float _actualUpdateInterval {get{Read();return Get(3);}set=>Set(3,value);}
  public void NativeSet(int field,float value)=>fields[field]=value;
  public float NativeInspect(int field)=>fields[field];
  public void NativeScratch(float e,float t){elapsed=e;target=t;}
  public void Update(){NativeCalls++;NativeBody?.Invoke();}

  // Decision-only simulation of the audited native Update's elapsed/season/ceil algorithm.
  // It never invokes Pool or creates any creature, and is not an IL2CPP execution model.
  public void SimulateNativeDecisions()
  {
   if(!Managers.Inst.game.playingOrInMenuWithClient)return;
   elapsed+=UnityEngine.Time.deltaTime;
   if(elapsed<_actualUpdateInterval)return;
   elapsed=0;
   if(RegionWidth<=minimumRegionSize)return;
   target=Managers.Inst.world.IsWinter?(SpecialWinter?winterDensitySpecial:winterDensityDefault):density;
   if((int)MathF.Ceiling(RegionWidth*target)-Population>0){SpawnDecisions++;Population++;}
  }
 }
}
namespace KingdomEnhancedMod
{
 // Mirrors the actual Android platform config: session master switch (not persisted) plus the
 // fifteenth persisted entry, declared OFF, whose only host-visible behavior is the value
 // toggle consumed by the shared source gate (no apply callback, cf. InfiniteMoney).
 public static class ModConfig
 {
  public sealed class Setting<T> {public T Value;public Setting(T value){Value=value;}}
  public static Setting<bool> Enabled=new(true);
  public static MelonLoader.MelonPreferences_Entry<bool> DeerPopulationEnabled=new(false);
 }
 // Mirrors HoldBridges.cs Logger surface, with the Instance settable so the shared harness can
 // swap in a fresh log per case (production's Instance is wired once by Probe).
 public class KingdomEnhancedPlugin
 {
  public static KingdomEnhancedPlugin Instance=new();public Log LogSource=new();
  public class Log {public readonly List<string> Info=new(),Warning=new();public void LogInfo(string text)=>Info.Add(text);public void LogWarning(string text)=>Warning.Add(text);}
 }
}
