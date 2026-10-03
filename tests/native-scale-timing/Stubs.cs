// NativeScaleTiming 套件的边界替身：只满足真实 GreekScaleScope.cs 与 PatchRoles_Worker.cs
// 的编译与行为驱动（Unity 假 null 语义、scope 判定、日志缝、Mover 身份）。
// 没有 Unity 运行时，也没有游戏进程。
using System;
using System.Collections.Generic;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type) { }
        public HarmonyPatch(string name) { }
        public HarmonyPatch(Type type, string name) { }
        public HarmonyPatch(Type type, string name, Type[] args) { }
        public HarmonyPatch(string name, Type[] args) { }
    }

    public class HarmonyPostfix : Attribute { }
    public class HarmonyPrefix : Attribute { }
    public class HarmonyFinalizer : Attribute { }
    public class HarmonyPriority : Attribute { public HarmonyPriority(int priority) { } }
    public static class Priority { public const int Last = 0; }
}

namespace Il2CppInterop.Runtime.Injection
{
    public static class ClassInjector
    {
        public static bool IsTypeRegisteredInIl2Cpp(Type type) => true;
        public static void RegisterTypeInIl2Cpp(Type type) { }
    }
}

namespace UnityEngine
{
    public class Object
    {
        private static int _next;
        public IntPtr Pointer = new IntPtr(++_next);
        public int GetInstanceID() => (int)Pointer;
        public static void DontDestroyOnLoad(Object target) { }
        public static void Destroy(Object target) { }
        public static T[] FindObjectsOfType<T>() => throw new InvalidOperationException("Unexpected scene scan");
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public string tag => gameObject.tag;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public bool CompareTag(string value) => tag == value;
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }

    public class MonoBehaviour : Behaviour
    {
        public MonoBehaviour() { }
        public MonoBehaviour(IntPtr pointer) { Pointer = pointer; }
    }

    public class GameObject : Object
    {
        public string name;
        public string tag = "Untagged";
        public bool activeInHierarchy = true;
        public HideFlags hideFlags;
        public Scene scene = new Scene { valid = true };
        public Transform transform;
        private readonly List<Component> _components = new List<Component>();

        public GameObject(string name = "actor")
        {
            this.name = name;
            transform = new Transform { gameObject = this };
        }

        public T AddComponent<T>() where T : Component
        {
            var ctor = typeof(T).GetConstructor(new[] { typeof(IntPtr) });
            var component = (T)(ctor != null
                ? ctor.Invoke(new object[] { Pointer })
                : Activator.CreateInstance(typeof(T)));
            component.gameObject = this;
            _components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : Component
        {
            foreach (Component component in _components)
                if (component is T typed) return typed;
            return null;
        }
    }

    public enum HideFlags { HideAndDontSave }

    public struct Scene
    {
        public bool valid;
        public bool IsValid() => valid;
    }

    public struct Vector3
    {
        public float x, y, z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public float this[int index]
        {
            get => index == 0 ? x : index == 1 ? y : z;
            set
            {
                if (index == 0) x = value;
                else if (index == 1) y = value;
                else z = value;
            }
        }

        public override string ToString() => "(" + x + ", " + y + ", " + z + ")";
    }

    public class Transform : Component
    {
        public Vector3 localScale = new Vector3(1f, 1f, 1f);
        public bool IsChildOf(Transform parent) => true;
    }

    public class WaitForSeconds { public WaitForSeconds(float seconds) { } }

    public static class Time { public static int frameCount; public static float time; }

    public static class Resources
    {
        public static T[] LoadAll<T>(string path) => Array.Empty<T>();
    }
}

public class Mover : UnityEngine.Behaviour { public void Update() { } }

public class BiomeHolder
{
    public static BiomeHolder Inst = new BiomeHolder();
    public const int GreeceBiomeIndex = 5;
    public const int NorselandsBiomeIndex = 3;
    public int BiomeIndex = GreeceBiomeIndex;
}

public class Character : UnityEngine.Behaviour
{
    public void Promote(DroppableTool tool, IUnitController by) { }
}

public class DroppableTool : UnityEngine.Component { public bool pickedUp; }

public interface IUnitController { }

public class Holder : UnityEngine.Component
{
    public Dictionary<string, Character> tagCharacterPairs = new Dictionary<string, Character>();
    public void InitializeTagCharacterPairs() { }
}

public class Managers
{
    public static Managers Inst = new Managers();
    public Holder holder;
}

public class Worker : Character
{
    public NpcShieldUser npcShieldUser;
    public void OnEnable() { }
}

public class WarriorPeasant : Character { public void OnEnable() { } }

public class Peasant : Character { public void OnEnable() { } }

public class Deer : Character { public void OnEnable() { } }

public class Damageable : UnityEngine.Component { }

public class Shield : UnityEngine.Component { }

public class CRPCHeader { public int NetID; }

public static class NetworkBigBoss { public static bool HasWorldAuth = true; }

public class NpcShieldUser : UnityEngine.Behaviour
{
    public Character character;
    public Damageable damageable;
    public CRPCHeader parentHeaderRef;
    public int shieldEnabledRpcIndex;
    public UnityEngine.WaitForSeconds regenWait;
    public Shield shield;
    public void Awake() { }
    public void BeginRegisteringRPCs() { }
    public bool HasShield() => false;
    public void SetShieldEnabled(bool enabled, int value) { }
}

namespace KingdomEnhancedMod
{
    internal static class ModConfig
    {
        public sealed class Setting { public bool Value = true; }
        public static Setting Enabled = new Setting();
    }

    public sealed class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
        public LogSink LogSource = new LogSink();

        public sealed class LogSink
        {
            public void LogInfo(object message) { }
            public void LogWarning(object message) { }
            public void LogError(object message) =>
                throw new InvalidOperationException("Unexpected production error: " + message);
        }
    }

    public static class PatchRoles_Castle
    {
        public static void EnsurePoolForCharacter(string tag) { }
    }
}
