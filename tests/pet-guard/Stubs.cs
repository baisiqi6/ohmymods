// 直链生产文件 PatchRoles_PetGuard.cs + PatchRoles_Hermit.cs 的最小替身宇宙。
// 只包含两份生产文件真实读写的成员；被依赖的原生语义按已核对的 2.4 反编译最小复刻：
//   - Droppable.OnDisable 按 _originalEnemyPolicy 重置（生产侧依赖的原生重置）。
//   - Dog.SetupDog 把狗登记进 kingdom.dogs（Dog.cs:687）。
//   - Hermit OnEnable 把隐士登记进 kingdom.hermits（Hermit.cs:355）。
//   - SpawnNearP1 返回新实例，隐士实例沿用预制件携带的 HermitType。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class HarmonyPatch : Attribute
    {
        public Type Target;
        public string Method;
        public HarmonyPatch(Type target, string method) { Target = target; Method = method; }
    }
    public class HarmonyPostfix : Attribute { }
    public class HarmonyPrefix : Attribute { }
}

namespace Il2CppSystem
{
    public class Nullable<T>
    {
        public T Value;
        public bool HasValue;
        public Nullable(T value) { Value = value; HasValue = true; }
    }
}

namespace UnityEngine
{
    public class Object
    {
        static int next;
        public int Id = System.Threading.Interlocked.Increment(ref next);
        public IntPtr Pointer { get; set; }
        public Object() { Pointer = (IntPtr)Id; }
        public int GetInstanceID() => Id;
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject != null ? gameObject.transform : null;
        public bool CompareTag(string candidate) => gameObject != null && gameObject.tag == candidate;
        public T GetComponent<T>() where T : Component => gameObject != null ? gameObject.GetComponent<T>() : null;
    }

    public struct Scene { public int handle; }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public class GameObject : Object
    {
        public string name = "unit";
        public string tag = "";
        public bool activeInHierarchy = true;
        public Transform transform;
        public Scene scene = new Scene { handle = 10 };
        public bool ThrowComponent;
        readonly List<Component> components = new List<Component>();
        public GameObject() { transform = new Transform { gameObject = this }; }
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; components.Add(c); return c; }
        public T GetComponent<T>() where T : Component
        {
            if (ThrowComponent) throw new InvalidOperationException("injected component read");
            foreach (Component c in components)
                if (c is T match) return match;
            return null;
        }
        public bool CompareTag(string candidate) => tag == candidate;
    }

    public class Transform : Component
    {
        public Transform parent;
        public bool IsChildOf(Transform t)
        {
            for (Transform current = this; current != null; current = current.parent)
                if (current == t) return true;
            return false;
        }
    }

    public static class Time { public static float unscaledTime; }
}

public enum PickUpPolicy { Anybody = 0, AnybodyExceptDropper = 1, OnlyClaimer = 2, AnyPlayer = 3, Nobody = 4, Blocked = 5, EnemyOnly = 6, WorkerOnly = 7 }

public class Droppable : UnityEngine.Component
{
    PickUpPolicy enemy;
    public int EnemyWrites, EnemyReads, GeneralWrites, OriginalWrites;
    public bool ThrowRead, ThrowWrite;
    public PickUpPolicy CurrentEnemyPolicy
    {
        get { EnemyReads++; if (ThrowRead) throw new InvalidOperationException("injected policy read"); return enemy; }
        set { if (ThrowWrite) throw new InvalidOperationException("injected policy write"); EnemyWrites++; enemy = value; }
    }
    PickUpPolicy original, general = PickUpPolicy.AnyPlayer;
    public PickUpPolicy _originalEnemyPolicy { get => original; set { OriginalWrites++; original = value; } }
    public PickUpPolicy pickUpPolicy { get => general; set { GeneralWrites++; general = value; } }
    public void NativePolicy(PickUpPolicy policy) => enemy = policy;
    public void NativeOriginal(PickUpPolicy policy) => original = policy;
    public void OnEnable() { }
    public void OnDisable() => enemy = original; // Native reset, deliberately excluded from mod write count.
}

public class Boat : UnityEngine.Component { public UnityEngine.Transform body; }

public class Dog : UnityEngine.Component
{
    public int dogId = -1;
    public int DogId => dogId;
    public UnityEngine.Color color;
    public int SetupDogCalls;
    public int LastSetupDogId = -1;
    public void SetupDog(int id)
    {
        dogId = id; LastSetupDogId = id; SetupDogCalls++;
        Managers.Inst?.kingdom?.dogs?.Add(this); // 原生 Dog.cs:687
    }
    public enum DogType { Dog, WolfPup }
    public enum DogPosition { Locked, Roaming, PickedUp, Stolen }
    public struct DogStatus
    {
        public DogPosition position;
        public int land;
        public UnityEngine.Color color;
        public DogType type;
        public int preferedPlayer;
    }
}

public class Hermit : UnityEngine.Component
{
    public HermitType Type { get; set; }
    public enum HermitType { Horse, Horn, Ballista, Baker, Knight, Persephone, Fire, Total }
    public enum HermitPosition { GemLocked, CoinLocked, Roaming, PickedUp, Stolen, Passenger }
    public struct HermitStatus
    {
        public HermitPosition position;
        public int player;
        public int land;
    }
}

public class Player : UnityEngine.Component { }

public class Kingdom
{
    public List<Dog> dogs = new List<Dog>();
    public List<Hermit> hermits = new List<Hermit>();
    public Player playerOne = new Player();
}

public class Holder : UnityEngine.Component
{
    public Dog dogPrefab = new Dog();
    public Dog wolfPupPrefab = new Dog();
    public Hermit[] hermits = new Hermit[7];
    public Holder()
    {
        for (int i = 0; i < hermits.Length; i++) hermits[i] = new Hermit { Type = (Hermit.HermitType)i };
    }
}

public class CampaignSaveData
{
    public static CampaignSaveData current = new CampaignSaveData();
    public int CurrentLand { get; set; } = 9;
    public Dog.DogStatus dog0, dog1;
    public Hermit.HermitStatus[] hermitStatuses = new Hermit.HermitStatus[7];
    public readonly List<string> DogWrites = new List<string>();
    public readonly List<string> HermitWrites = new List<string>();
    public bool ThrowDogStatus;

    public Dog.DogStatus[] GetDogStatus()
    {
        if (ThrowDogStatus) throw new InvalidOperationException("injected GetDogStatus failure");
        return new[] { dog0, dog1 }; // 原生返回副本数组
    }

    public void SetDogStatus(Dog.DogPosition position, int land, Il2CppSystem.Nullable<UnityEngine.Color> color,
        Il2CppSystem.Nullable<Dog.DogType> type, int player, int dogID = 0)
    {
        DogWrites.Add(dogID + ":" + position + ":" + land + ":" + (color != null ? color.Value.r + "/" + color.Value.g + "/" + color.Value.b : "null")
            + ":" + (type != null ? type.Value.ToString() : "null") + ":" + player);
        Dog.DogStatus status = dogID == 0 ? dog0 : dog1;
        status.position = position;
        status.land = land;
        if (color != null) status.color = color.Value;
        if (type != null) status.type = type.Value;
        if (player != 0) status.preferedPlayer = player;
        if (dogID == 0) dog0 = status; else dog1 = status;
    }

    public Hermit.HermitStatus GetHermitStatus(Hermit.HermitType type) => hermitStatuses[(int)type];

    public void SetHermitStatus(Hermit.HermitType type, Hermit.HermitPosition position, int player = 0, int land = 0)
    {
        HermitWrites.Add((int)type + ":" + position + ":" + player + ":" + land);
        hermitStatuses[(int)type] = new Hermit.HermitStatus { position = position, player = player, land = land };
    }

    public enum CarryForwardToolType { None = 0 }

    public static readonly List<UnityEngine.Component> Spawns = new List<UnityEngine.Component>();
    public static readonly List<UnityEngine.Component> SpawnPrefabs = new List<UnityEngine.Component>();

    public static T SpawnNearP1<T>(T prefab, int amount = 1, CarryForwardToolType carryForwardTool = CarryForwardToolType.None)
        where T : UnityEngine.Component
    {
        T spawned = default;
        while (amount-- > 0)
        {
            spawned = Activator.CreateInstance<T>();
            if (spawned is Hermit hermit)
            {
                if (prefab is Hermit source) hermit.Type = source.Type; // 预制件携带自己的 HermitType
                Managers.Inst?.kingdom?.hermits?.Add(hermit);            // 原生 Hermit.cs:355
            }
            Spawns.Add(spawned);
            SpawnPrefabs.Add(prefab);
        }
        return spawned;
    }
}

public class World : UnityEngine.Object { public UnityEngine.Transform gameLayer = new UnityEngine.GameObject().transform; }
public class Game { public State state = State.Playing; public enum State { Playing, NetworkClientPlaying, Menu, Loading } }
public class Managers
{
    public static Managers Inst;
    public World world = new World();
    public Game game = new Game();
    public Kingdom kingdom = new Kingdom();
    public Holder holder = new Holder();
}
public static class NetworkBigBoss { public static bool HasWorldAuth = true; }

namespace KingdomEnhancedMod
{
    public static class ModConfig
    {
        public class Flag { public bool Value = true; }
        public static Flag Enabled = new Flag();
        public static Flag PetGuardEnabled = new Flag();
    }
    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
        public Log LogSource = new Log();
        public class Log
        {
            public List<string> Info = new List<string>(), Warning = new List<string>();
            public void LogInfo(string text) => Info.Add(text);
            public void LogWarning(string text) => Warning.Add(text);
            public void LogError(string text) => Warning.Add(text);
        }
    }
}
