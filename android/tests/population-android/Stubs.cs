// Typed boundary doubles for compiling the real shared sources
// (il2cpp/PatchRoles_BeggarCamp.cs, il2cpp/PatchPerformance_Population.cs,
// il2cpp/PopulationGrounding.cs) and the real android/MobilePlayerConfig.cs into this host
// scenario project. Every shape mirrors the actual interop surface the sources consume:
//   - HarmonyLib.HarmonyPatch/Prefix/Postfix attribute forms used by the patch shells;
//   - Il2CppInterop.Runtime.Injection.ClassInjector (IsTypeRegisteredInIl2Cpp /
//     RegisterTypeInIl2Cpp(Type)) exactly as the injected coordinator calls it;
//   - UnityEngine component model (GameObject.GetComponent<T>/AddComponent<T>,
//     Transform.position/localPosition/lossyScale/parent, Scene.handle, Time.time/
//     unscaledTime/timeScale, Mathf.Max/Clamp/Approximately/Abs) plus the read-only
//     diagnostic members PopulationGrounding reads (Rigidbody2D/Collider2D/Physics2D);
//   - Il2Cpp game API doubles named exactly like the Android interop types
//     (Managers/World/Kingdom/BeggarCamp/Beggar/CampaignSaveData/Pool/PoolManager/
//     NetworkBigBoss/Tutorial/Holder/Character/ParentHeader), with the same member names
//     the production sources use — no invented helper and no hidden real member;
//   - global aliases Managers/World/NetworkBigBoss/Pool, matching android/GlobalAliases.cs
//     (the other game types are file-local aliases inside the linked sources).
// The "native" side is only ever simulated inside these doubles: SpawnBeggar appends a new
// Beggar to Kingdom.Beggars (the roster the sources diff against) and the field setters count
// writes, so the suite's oracle is the roster + setter counts, never a copied ownership policy.
// A throwing mode makes every native access throw, so the OFF-path checks can prove zero
// interop reads. These are host doubles, not native evidence and not the real game types.
global using Managers = Il2Cpp.Managers;
global using World = Il2Cpp.World;
global using NetworkBigBoss = Il2Cpp.NetworkBigBoss;
global using Pool = Il2Cpp.Pool;

using System;
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type declaringType) { }
        public HarmonyPatch(string methodName) { }
        public HarmonyPatch(Type declaringType, string methodName) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfix : Attribute { }
}

namespace Il2CppInterop.Runtime.InteropTypes
{
    /// <summary>Il2CppObjectBase 的最小形状：Pointer 是可核验的原生句柄。</summary>
    public abstract class Il2CppObjectBase
    {
        public IntPtr Pointer { get; internal set; }
    }
}

namespace Il2CppInterop.Runtime.Injection
{
    /// <summary>真实 ClassInjector 的两个静态入口（生产源只调用这两个）。</summary>
    public static class ClassInjector
    {
        internal static int RegisterCalls;
        internal static readonly System.Collections.Generic.HashSet<Type> Registered = new System.Collections.Generic.HashSet<Type>();

        public static bool IsTypeRegisteredInIl2Cpp(Type type) => Registered.Contains(type);

        public static void RegisterTypeInIl2Cpp(Type type)
        {
            RegisterCalls++;
            Registered.Add(type);
        }
    }
}

namespace UnityEngine
{
    public class Object : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        internal int InstanceId;
        public int GetInstanceID() { Stubs.Touch(); return InstanceId; }
    }

    public class Component : Object
    {
        internal GameObject Go;
        public GameObject gameObject { get { Stubs.Touch(); return Go; } }
        public Transform transform { get { Stubs.Touch(); return Go == null ? null : Go.Tr; } }
        public T GetComponent<T>() where T : Component { Stubs.Touch(); return Go == null ? null : Go.GetComponent<T>(); }
    }

    public class Behaviour : Component
    {
        public bool enabled { get { Stubs.Touch(); return true; } set { Stubs.Touch(); } }
    }

    public class MonoBehaviour : Behaviour
    {
        protected MonoBehaviour() { }
        protected MonoBehaviour(IntPtr pointer) { Pointer = pointer; }
    }

    public sealed class GameObject : Object
    {
        internal readonly System.Collections.Generic.List<Component> Components = new System.Collections.Generic.List<Component>();
        internal Transform Tr;
        public string name { get { Stubs.Touch(); return Name; } }
        public int layer { get { Stubs.Touch(); return Layer; } }
        public bool activeInHierarchy { get { Stubs.Touch(); return Active; } }
        public UnityEngine.SceneManagement.Scene scene { get { Stubs.Touch(); return SceneOf; } }

        internal string Name = "object";
        internal int Layer;
        internal bool Active = true;
        internal Transform ParentOfTransform = null;
        internal UnityEngine.SceneManagement.Scene SceneOf;

        public GameObject()
        {
            InstanceId = Stubs.NextInstanceId();
            Tr = new Transform(this);
            Tr.Pointer = Stubs.NextPointer();
        }

        public Transform transform { get { Stubs.Touch(); return Tr; } }

        public T GetComponent<T>() where T : Component
        {
            Stubs.Touch();
            foreach (Component component in Components) if (component is T typed) return typed;
            return null;
        }

        public T AddComponent<T>() where T : Component
        {
            Stubs.Touch();
            if (Stubs.Throwing) throw new InvalidOperationException("native AddComponent");
            var added = (T)Activator.CreateInstance(typeof(T), System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
                null, new object[] { Stubs.NextPointer() }, null);
            Components.Add(added);
            added.Go = this;
            added.InstanceId = Stubs.NextInstanceId();
            return added;
        }

        internal readonly System.Collections.Generic.List<Component> Children = new System.Collections.Generic.List<Component>();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            Stubs.Touch();
            var found = new System.Collections.Generic.List<T>();
            foreach (Component child in Children) if (child is T typed) found.Add(typed);
            return found.ToArray();
        }
    }

    public sealed class Transform : Object
    {
        private readonly GameObject _owner;
        internal Transform(GameObject owner) { _owner = owner; }
        public GameObject gameObject { get { Stubs.Touch(); return _owner; } }
        public string name { get { Stubs.Touch(); return _owner.Name; } }
        internal int PositionWrites;
        public Vector3 position { get { Stubs.Touch(); return Position; } set { PositionWrites++; Stubs.Touch(); Position = value; } }
        public Vector3 localPosition { get { Stubs.Touch(); return Position; } }
        public Vector3 lossyScale { get { Stubs.Touch(); return Vector3.one; } }
        public Transform parent { get { Stubs.Touch(); return _owner.ParentOfTransform; } }
        public bool IsChildOf(Transform parent) { Stubs.Touch(); return _owner.ParentOfTransform == parent; }
        internal Vector3 Position;
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new Vector3(1f, 1f, 1f);
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Bounds
    {
        public Vector3 min, max;
        public Bounds(Vector3 min, Vector3 max) { this.min = min; this.max = max; }
    }

    public enum RigidbodyType2D { Dynamic, Kinematic, Static }
    public enum CollisionDetectionMode2D { Discrete, Continuous }
    public enum RigidbodyConstraints2D { None, FreezeRotation }

    /// <summary>
    /// body 形状 double：bodyType/gravityScale/velocity 是 actual metadata 有原生声明的成员（被调用即计数）；
    /// simulated/constraints/collisionDetectionMode 在 actual APK 里被 strip（get_simulated nativeMatches=0），
    /// 这里作为 throw+counter sentinel：一旦被 production 调用就计数并抛错，用来证明 0 调用。
    /// </summary>
    public class Rigidbody2D : Component
    {
        internal int VelocityGetterCalls;
        internal int SupportedGetterCalls;
        internal int StrippedGetterCalls;
        public RigidbodyType2D bodyType { get { SupportedGetterCalls++; Stubs.Touch(); return RigidbodyType2D.Dynamic; } }
        public bool simulated
        {
            get { StrippedGetterCalls++; Stubs.Touch(); throw new InvalidOperationException("stripped Rigidbody2D.get_simulated"); }
        }
        public float gravityScale { get { SupportedGetterCalls++; Stubs.Touch(); return 1f; } }
        public RigidbodyConstraints2D constraints
        {
            get { StrippedGetterCalls++; Stubs.Touch(); throw new InvalidOperationException("stripped Rigidbody2D.get_constraints"); }
        }
        public CollisionDetectionMode2D collisionDetectionMode
        {
            get { StrippedGetterCalls++; Stubs.Touch(); throw new InvalidOperationException("stripped Rigidbody2D.get_collisionDetectionMode"); }
        }
        public Vector2 velocity { get { VelocityGetterCalls++; Stubs.Touch(); return new Vector2(1.5f, -2.5f); } }
    }

    public class Collider2D : Behaviour
    {
        public bool isTrigger { get { Stubs.Touch(); return false; } }
        public Bounds bounds { get { Stubs.Touch(); return new Bounds(default, default); } }
    }

    /// <summary>GetIgnoreLayerCollision 公共 API 与其 _Internal callee 在 actual APK 都被 strip → throw+counter sentinel。</summary>
    public static class Physics2D
    {
        internal static int StrippedQueryCalls;
        public static bool GetIgnoreLayerCollision(int layer1, int layer2)
        {
            StrippedQueryCalls++;
            Stubs.Touch();
            throw new InvalidOperationException("stripped Physics2D.GetIgnoreLayerCollision");
        }
        internal static void ResetCounters() => StrippedQueryCalls = 0;
    }

    public static class Time
    {
        public static float time;
        public static float unscaledTime;
        public static float timeScale = 1f;
    }

    public static class Mathf
    {
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Abs(float value) => MathF.Abs(value);
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public static bool Approximately(float a, float b) => MathF.Abs(a - b) < 1e-5f;
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public int handle;
        public Scene(int handle) { this.handle = handle; }
    }
}

namespace KingdomEnhancedMod
{
    // Shape double of HoldBridges.cs (same as android/tests/EnemyStubs.cs): the linked
    // production sources only read Instance?.LogSource from their diagnostic paths.
    internal sealed class KingdomEnhancedPlugin
    {
        private static KingdomEnhancedPlugin instance;
        private readonly Logger logSource = new Logger();

        internal static KingdomEnhancedPlugin Instance => instance;

        internal Logger LogSource => logSource;

        internal static void Initialize()
        {
            if (instance != null) return;
            instance = new KingdomEnhancedPlugin();
        }
    }

    internal sealed class Logger
    {
        internal void LogWarning(string message) => MelonLoader.MelonLogger.Warning(message);
        internal void LogInfo(string message) => MelonLoader.MelonLogger.Msg(message);
        internal void LogError(object message) => MelonLoader.MelonLogger.Error(message.ToString());
    }
}

namespace Il2Cpp
{
    public sealed class ParentHeader
    {
        public int NetID { get { Stubs.Touch(); return Net; } set { Stubs.Touch(); Net = value; } }
        internal int Net;
    }

    public sealed class Character : UnityEngine.Component { }

    public class BeggarCamp : UnityEngine.Component
    {
        internal int MaxNative = 4;
        internal float IntervalNative = 125f;
        internal int MaxSetCalls;
        internal int IntervalSetCalls;
        internal int SpawnCalls;
        internal int SpawnDelta = 1;
        internal bool FailSpawn;
        internal bool FailIntervalWrite;
        internal bool FailMaxWrite;
        internal bool FailGetter;
        public ParentHeader parentHeaderRef;

        public int maxBeggars
        {
            get { Stubs.Touch(); if (FailGetter) throw new InvalidOperationException("native max getter"); return MaxNative; }
            set { Stubs.Touch(); if (FailMaxWrite) throw new InvalidOperationException("native max setter"); MaxSetCalls++; MaxNative = value; }
        }

        public float spawnInterval
        {
            get { Stubs.Touch(); return IntervalNative; }
            set { Stubs.Touch(); if (FailIntervalWrite) throw new InvalidOperationException("native interval setter"); IntervalSetCalls++; IntervalNative = value; }
        }

        /// <summary>模拟原生 SpawnBeggar：只在本 double 内生成新 Beggar 进 Kingdom.Beggars，并可注入故障/差量。</summary>
        public void SpawnBeggar()
        {
            Stubs.Touch();
            SpawnCalls++;
            if (FailSpawn) throw new InvalidOperationException("native SpawnBeggar");
            Managers managers = Managers.Inst;
            if (managers == null || managers.kingdom == null) return;
            for (int i = 0; i < SpawnDelta; i++) managers.kingdom.Beggars.Add(Stubs.NewBeggar(this));
        }

        public void Awake() { }
        public void OnDestroy() { }
    }

    public class Beggar : UnityEngine.Component
    {
        public ParentHeader parentHeaderRef;
        public bool settler { get { Stubs.Touch(); return SettlerFlag; } }
        public BeggarCamp camp { get { Stubs.Touch(); return CampRef; } }
        internal bool SettlerFlag = false;
        internal BeggarCamp CampRef;
        public void OnEnable() { }
        public void OnDisable() { }
    }

    public sealed class CampaignSaveData : UnityEngine.Object
    {
        internal static CampaignSaveData Current;
        public static CampaignSaveData current { get { Stubs.Touch(); if (Stubs.Throwing) throw new InvalidOperationException("native campaign"); return Current; } }
        public void ApplyToScene() { }
    }

    public sealed class Kingdom : UnityEngine.Object
    {
        public readonly System.Collections.Generic.List<Beggar> Beggars = new System.Collections.Generic.List<Beggar>();
        public readonly System.Collections.Generic.List<BeggarCamp> BeggarCamps = new System.Collections.Generic.List<BeggarCamp>();
    }

    public sealed class World : UnityEngine.Component
    {
        public Transform gameLayer { get { Stubs.Touch(); return Layer; } }
        internal Transform Layer;
        internal static Collider2D GroundStub;
        public static Collider2D GroundCollider { get { Stubs.Touch(); return GroundStub; } }
    }

    public sealed class Tutorial
    {
        public bool IsBeggarSpawnAllowed { get { Stubs.Touch(); return Allowed; } }
        internal bool Allowed = true;
    }

    public sealed class Holder : UnityEngine.Object
    {
        public Character GetCharacterByTag(string tag) { Stubs.Touch(); return BeggarPrefab; }
        internal Character BeggarPrefab;
    }

    public sealed class Pool : UnityEngine.Object
    {
        public bool sync { get { Stubs.Touch(); return Synced; } }
        public int syncID { get { Stubs.Touch(); return SyncId; } set { Stubs.Touch(); SyncId = value; } }
        internal bool Synced = true;
        internal int SyncId = 77;

        public static Pool GetPoolFromPrefabAsset(UnityEngine.GameObject prefab) { Stubs.Touch(); return Stubs.BeggarPool; }
    }

    public sealed class PoolManager : UnityEngine.Object
    {
        public System.Collections.Generic.Dictionary<int, Pool> cachedSyncIdPoolPairs = new System.Collections.Generic.Dictionary<int, Pool>();
    }

    public sealed class Managers
    {
        internal static Managers StubInstance;

        public static Managers Inst
        {
            get
            {
                Stubs.Touch();
                if (Stubs.Throwing) throw new InvalidOperationException("native Managers.Inst");
                return StubInstance;
            }
        }

        public Kingdom kingdom { get { Stubs.Touch(); return KingdomStub; } }
        public World world { get { Stubs.Touch(); return WorldStub; } }
        public Tutorial tutorial { get { Stubs.Touch(); return TutorialStub; } }
        public Holder holder { get { Stubs.Touch(); return HolderStub; } }
        public PoolManager pools { get { Stubs.Touch(); return PoolManagerStub; } }

        internal Kingdom KingdomStub;
        internal World WorldStub;
        internal Tutorial TutorialStub;
        internal Holder HolderStub;
        internal PoolManager PoolManagerStub;
    }

    public static class NetworkBigBoss
    {
        public static bool HasWorldAuth = true;
        public static bool IsOnline;
        public static bool IsClientPresent;
        public static bool HasClientCaughtUp = true;
    }
}

/// <summary>Interop 触达计数与场景夹具：throw 模式下任何 native 成员访问都抛 InvalidOperationException。</summary>
internal static class Stubs
{
    internal static int TouchCount;
    internal static bool Throwing;
    internal static Pool BeggarPool;
    private static int _instanceId, _pointer;

    internal static void Touch()
    {
        TouchCount++;
        if (Throwing) throw new InvalidOperationException("native read");
    }

    internal static int NextInstanceId() => ++_instanceId;
    internal static IntPtr NextPointer() => new IntPtr(++_pointer);

    internal static Il2Cpp.Beggar NewBeggar(Il2Cpp.BeggarCamp camp)
    {
        var beggar = new Il2Cpp.Beggar
        {
            InstanceId = NextInstanceId(),
            CampRef = camp
        };
        beggar.Pointer = NextPointer();
        // 原生生成的对象和营地同属当前场景（IsCurrentSceneBeggar 的真实判据）。
        beggar.Go = new UnityEngine.GameObject { Name = "beggar" };
        if (camp != null && camp.Go != null) beggar.Go.SceneOf = camp.Go.SceneOf;
        return beggar;
    }

    /// <summary>构造一个完整可用的场景夹具（世界/王国/帐篷/号角池/教程门），所有 id/pointer 唯一。</summary>
    internal static Il2Cpp.Managers NewScene(int sceneHandle, out Il2Cpp.BeggarCamp camp,
        out UnityEngine.Transform sceneRoot, out Il2Cpp.CampaignSaveData campaign)
    {
        var managers = new Il2Cpp.Managers();
        var kingdom = new Il2Cpp.Kingdom { InstanceId = NextInstanceId() };
        kingdom.Pointer = NextPointer();
        var world = new Il2Cpp.World { InstanceId = NextInstanceId() };
        world.Pointer = NextPointer();
        world.Go = new UnityEngine.GameObject { Name = "world" };
        var layerObject = new UnityEngine.GameObject { Name = "gameLayer" };
        UnityEngine.SceneManagement.Scene layerScene = default;
        layerScene.handle = sceneHandle;
        layerObject.SceneOf = layerScene;
        world.Layer = layerObject.Tr;
        sceneRoot = world.Layer;

        campaign = new Il2Cpp.CampaignSaveData { InstanceId = NextInstanceId() };
        campaign.Pointer = NextPointer();
        Il2Cpp.CampaignSaveData.Current = campaign;

        camp = new Il2Cpp.BeggarCamp { InstanceId = NextInstanceId() };
        camp.Pointer = NextPointer();
        camp.Go = new UnityEngine.GameObject { Name = "camp", Layer = sceneHandle };
        UnityEngine.SceneManagement.Scene campScene = default;
        campScene.handle = sceneHandle;
        camp.Go.SceneOf = campScene;
        kingdom.BeggarCamps.Add(camp);

        var prefab = new Il2Cpp.Character { InstanceId = NextInstanceId() };
        prefab.Go = new UnityEngine.GameObject { Name = "Beggar" };
        var holder = new Il2Cpp.Holder { InstanceId = NextInstanceId() };
        holder.BeggarPrefab = prefab;
        var pool = new Il2Cpp.Pool { InstanceId = NextInstanceId() };
        pool.Pointer = NextPointer();
        BeggarPool = pool;
        var poolManager = new Il2Cpp.PoolManager { InstanceId = NextInstanceId() };
        poolManager.cachedSyncIdPoolPairs[pool.syncID] = pool;

        managers.KingdomStub = kingdom;
        managers.WorldStub = world;
        managers.TutorialStub = new Il2Cpp.Tutorial();
        managers.HolderStub = holder;
        managers.PoolManagerStub = poolManager;
        Il2Cpp.Managers.StubInstance = managers;
        Il2Cpp.NetworkBigBoss.HasWorldAuth = true;
        Il2Cpp.NetworkBigBoss.IsOnline = false;
        Il2Cpp.NetworkBigBoss.IsClientPresent = false;
        Il2Cpp.NetworkBigBoss.HasClientCaughtUp = true;
        return managers;
    }

    internal static void ResetTouches() => TouchCount = 0;

    /// <summary>观察用例的逐步 reset：清注入的地面/计数器，保持其他 case 的空 body/ground 行为。</summary>
    internal static void ResetObservation()
    {
        Il2Cpp.World.GroundStub = null;
        UnityEngine.Physics2D.ResetCounters();
    }
}
