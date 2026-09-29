// 窄测试 stubs：只建模 DeadlandsFollowerCapture 触到的 Unity/游戏边界，并统计
// 读/写次数，用于断言「录制只读、停止后零读取、写盘失败不重试」等有界语义。
// 这些 stub 不冒充 IL2CPP detour、真实 Animator 相位或渲染结果。
using System;
using System.Collections.Generic;
using System.Threading;

namespace UnityEngine
{
    public static class Probe
    {
        public static int Reads, Writes;
        public static int ClipInfoReads;
        public static void Reset() { Reads = 0; Writes = 0; ClipInfoReads = 0; }
    }

    public class Object
    {
        static long _next = 1000;
        public IntPtr Pointer = (IntPtr)Interlocked.Increment(ref _next);
        public string name = "";
        public int GetInstanceID() => (int)Pointer;
        public static void Destroy(Object o) { if (o is Component c) c.gameObject?.Remove(c); }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject != null ? gameObject.transform : null;
        public T GetComponent<T>() where T : Component => gameObject != null ? gameObject.GetComponent<T>() : null;
        public T GetComponentInChildren<T>() where T : Component => gameObject != null ? gameObject.GetComponentInChildren<T>() : null;
    }

    public class MonoBehaviour : Component
    {
        public MonoBehaviour() { }
        public MonoBehaviour(IntPtr pointer) { Pointer = pointer; }
        public bool enabled = true;
    }

    public class GameObject : Object
    {
        readonly List<Component> _components = new List<Component>();
        public bool activeSelf = true;
        public Transform transform;

        public GameObject(string name = "go")
        {
            this.name = name;
            transform = new Transform { gameObject = this };
        }

        public bool activeInHierarchy { get { Probe.Reads++; return activeSelf; } }

        public GameObject AddChild(string childName)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            return go;
        }

        public T AddComponent<T>() where T : Component
        {
            var component = (T)Activator.CreateInstance(typeof(T), true);
            component.gameObject = this;
            _components.Add(component);
            return component;
        }

        public void Remove(Component component) => _components.Remove(component);

        public T GetComponent<T>() where T : Component
        {
            // 模型真实边界：未注册的注入类型泛型查询可能抛异常，生产必须先过 ClassInjector 核验。
            if (typeof(T) == typeof(DeadlandsAnimObserver)
                && !Il2CppInterop.Runtime.Injection.ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(T)))
                throw new InvalidOperationException("injected type not registered: " + typeof(T).Name);
            for (int i = 0; i < _components.Count; i++)
                if (_components[i] is T hit) return hit;
            return null;
        }

        public T GetComponentInChildren<T>() where T : Component
        {
            T own = GetComponent<T>();
            if (own != null) return own;
            foreach (Transform child in transform.Children)
            {
                T found = child.gameObject.GetComponentInChildren<T>();
                if (found != null) return found;
            }
            return null;
        }
    }

    public class Transform : Component
    {
        public Vector3 position;
        public Vector3 localScale = Vector3.one;
        public Transform parent;
        public readonly List<Transform> Children = new List<Transform>();

        public void SetParent(Transform target, bool worldPositionStays)
        {
            if (parent != null) parent.Children.Remove(this);
            parent = target;
            if (target != null) target.Children.Add(this);
        }

        public bool IsChildOf(Transform target)
        {
            Probe.Reads++;
            Transform current = this;
            while (current != null)
            {
                if (current == target) return true;
                current = current.parent;
            }
            return false;
        }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new Vector3(1f, 1f, 1f);
    }

    public static class Time
    {
        public static float time;
        public static float deltaTime = 0.02f;
        public static float realtimeSinceStartup;
        public static float timeScale = 1f;
        public static int frameCount;
    }

    public static class Mathf
    {
        public static float Abs(float value) => MathF.Abs(value);
    }

    public struct AnimatorStateInfo
    {
        public int fullPathHash;
        public int shortNameHash;
        public float normalizedTime;
        public float length;
        public float speed;
        public float speedMultiplier;
    }

    public struct AnimatorClipInfo
    {
        public AnimationClip clip;
        public float weight;
    }

    public class AnimationClip : Object
    {
        public float length;
        public AnimationClip(string name = "", float length = 1f) { this.name = name; this.length = length; }
    }

    public class RuntimeAnimatorController : Object
    {
        public RuntimeAnimatorController(string name = "") { this.name = name; }
    }

    public class Sprite : Object
    {
        public Sprite(string name = "") { this.name = name; }
    }

    public class SpriteRenderer : Component
    {
        Sprite _sprite;
        bool _enabled = true;
        public Sprite sprite { get { Probe.Reads++; return _sprite; } set { Probe.Writes++; _sprite = value; } }
        public bool enabled { get { Probe.Reads++; return _enabled; } set { Probe.Writes++; _enabled = value; } }
    }

    public class Animator : Component
    {
        AnimatorStateInfo _state;
        AnimatorStateInfo _next;
        bool _inTransition;
        float _speed = 1f;
        float _speedParam;
        RuntimeAnimatorController _controller;

        public AnimatorClipInfo[] Clips = Array.Empty<AnimatorClipInfo>();

        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer) { Probe.Reads++; return _state; }
        public AnimatorStateInfo GetNextAnimatorStateInfo(int layer) { Probe.Reads++; return _next; }
        public bool IsInTransition(int layer) { Probe.Reads++; return _inTransition; }
        public float GetFloat(int hash) { Probe.Reads++; return hash == StringToHash("Speed") ? _speedParam : 0f; }
        public AnimatorClipInfo[] GetCurrentAnimatorClipInfo(int layer) { Probe.Reads++; Probe.ClipInfoReads++; return Clips; }

        public float speed
        {
            get { Probe.Reads++; return _speed; }
            set { Probe.Writes++; _speed = value; }
        }

        bool _enabled = true;
        public bool enabled
        {
            get { Probe.Reads++; return _enabled; }
            set { Probe.Writes++; _enabled = value; }
        }

        public RuntimeAnimatorController runtimeAnimatorController
        {
            get { Probe.Reads++; return _controller; }
            set { Probe.Writes++; _controller = value; }
        }

        public static int StringToHash(string text) => text.GetHashCode();

        // ---- 测试装填（直接写后备字段，不计入读/写探针）----
        public void SetState(int fullHash, float normalizedTime, float length = 1f)
            => _state = new AnimatorStateInfo { fullPathHash = fullHash, shortNameHash = fullHash, normalizedTime = normalizedTime, length = length };
        public void SetStateEx(int fullHash, int shortNameHash, float normalizedTime, float length = 1f)
            => _state = new AnimatorStateInfo { fullPathHash = fullHash, shortNameHash = shortNameHash, normalizedTime = normalizedTime, length = length };
        public void SetStateSpeed(float speed, float speedMultiplier)
        {
            _state.speed = speed;
            _state.speedMultiplier = speedMultiplier;
        }
        public void SetNext(int fullHash, float normalizedTime)
            => _next = new AnimatorStateInfo { fullPathHash = fullHash, shortNameHash = fullHash, normalizedTime = normalizedTime };
        public void SetTransition(bool value) => _inTransition = value;
        public void SetSpeedParam(float value) => _speedParam = value;
        public void SetAnimatorSpeed(float value) => _speed = value;
        public void SetController(RuntimeAnimatorController value) => _controller = value;
    }
}

// ---- 游戏侧最小 stub（只覆盖 capture 触到的成员）----
public class Knight : UnityEngine.Component
{
    public int Style;
    public bool Qualified = true;
}

public class Damageable : UnityEngine.Component
{
    public bool isDead;
}

public class Archer : UnityEngine.Component
{
    public Knight _knight;
    public Mover _mover;
    public UnityEngine.Animator _animator;
    public Damageable _damageable;
    public bool Crossbow;
    public bool Norse;
}

public class Mover : UnityEngine.Component
{
}

// ---- 原生相机入口 stub（真实类型在全局命名空间；录制已不再触碰 Camera.main）----
public class MainCamera : UnityEngine.MonoBehaviour
{
}

public class CameraMarshaller
{
    static CameraMarshaller _inst;
    public static int LastRequestedId = -1;
    public MainCamera Primary;

    public static bool InstExists => _inst != null;
    public static CameraMarshaller Inst { get { UnityEngine.Probe.Reads++; return _inst; } }

    public MainCamera GetCamera(int id)
    {
        UnityEngine.Probe.Reads++;
        LastRequestedId = id;
        return id == 0 ? Primary : null;
    }

    public static void SetInst(CameraMarshaller value) => _inst = value;
    public static CameraMarshaller With(MainCamera primary) => new CameraMarshaller { Primary = primary };
}

public class Managers
{
    static Managers _inst;
    public static Managers Inst { get { UnityEngine.Probe.Reads++; return _inst; } set { _inst = value; } }
    public World world;
}

public class World : UnityEngine.MonoBehaviour
{
    UnityEngine.Transform _gameLayer;
    public UnityEngine.Transform gameLayer
    {
        get { UnityEngine.Probe.Reads++; return _gameLayer; }
        set { _gameLayer = value; }
    }
}

public class DeadlandsAnimObserver : UnityEngine.MonoBehaviour
{
    internal UnityEngine.Animator Animator = null;
    internal Knight Knight = null;
    internal Archer Archer = null;
    internal float OriginalSpeed = 1f;
    internal float BoostedSpeed = 2f;
    internal int PreTriggerStateHash = 0;
    internal int AttackStateHash = 0;
    internal bool Captured = false;
    internal bool Boosting = false;
}

namespace KingdomEnhancedMod
{
    internal static class UnitScanCache
    {
        public static Archer[] Archers = Array.Empty<Archer>();
        public static int GetArchersCalls;
        public static Archer[] GetArchers(float maxAgeSec = 3f)
        {
            GetArchersCalls++;
            return Archers;
        }
    }

    internal static class PatchRoles_KnightStyle
    {
        public static bool TryGetResolvedStyleIndex(Knight knight, out int styleIndex)
        {
            styleIndex = knight != null ? knight.Style : -1;
            return knight != null && knight.Qualified;
        }
    }

    internal static class PatchRoles_Crossbowman
    {
        public static bool IsCrossbowman(Archer archer) => archer != null && archer.Crossbow;
    }

    internal static class PatchRoles_NorseSquad
    {
        public static bool IsNorseArcherInstance(Archer archer) => archer != null && archer.Norse;
    }

    internal static class PatchRoles_DeadlandsPowers
    {
        internal const int DeadlandsStyleIndex = 1;
    }

    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
        public Logger LogSource = new Logger();

        public class Logger
        {
            public readonly List<string> Info = new List<string>();
            public readonly List<string> Errors = new List<string>();
            public void LogInfo(string message) => Info.Add(message);
            public void LogError(string message) => Errors.Add(message);
        }
    }
}

namespace BepInEx
{
    public static class Paths
    {
        public static string ConfigPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kec-follower-capture-tests");
    }
}

namespace Il2CppInterop.Runtime.Injection
{
    public static class ClassInjector
    {
        public static bool Registered = true;
        public static bool IsTypeRegisteredInIl2Cpp(Type type) => Registered;
        public static void RegisterTypeInIl2Cpp(Type type) { }
    }
}
