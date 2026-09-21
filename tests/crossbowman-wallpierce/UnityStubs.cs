// 原生边界替身（Unity 侧）：只提供三个生产文件真正引用的 API 面，并把「可观察写入」
// 变成测试证据——Physics2D.IgnoreCollision 的每次调用（含 true/false 顺序）记账、
// Instantiate 深克隆（新 Pointer、组件重挂新 GO）、GameObject.SetActive 按组件序按名
// 派发 OnEnable/OnDisable（镜像 Unity 消息语义）。这不是 Unity：绝不用于游戏内验证；
// 真实 API 面由主 il2cpp Debug 构建（E 盘 2.4 interop）负责。

using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        private static int _nextPointer = 0x2000;
        private IntPtr _pointer;

        /// <summary>native 指针（真实 IL2CPP 只读；替身允许测试/克隆改写）。</summary>
        public virtual IntPtr Pointer
        {
            get { return _pointer; }
            set { _pointer = value; }
        }

        public string name = string.Empty;
        public bool Destroyed;

        internal static readonly List<Object> DestroyCalls = new List<Object>();

        public Object() { _pointer = FreshPointer(); }

        public Object(IntPtr pointer) { _pointer = pointer == IntPtr.Zero ? FreshPointer() : pointer; }

        internal static IntPtr FreshPointer() { return new IntPtr(_nextPointer += 8); }

        /// <summary>真实 Unity 在 Object 上；替身用唯一 Pointer 代_InstanceID 语义。</summary>
        public virtual int GetInstanceID() => (int)_pointer;

        public static void Destroy(Object target)
        {
            DestroyCalls.Add(target);
            if (target != null) target.Destroyed = true;
        }

        /// <summary>镜像 Unity.Instantiate 的最小语义：GO=深克隆（组件逐个 MemberwiseClone、
        /// 重挂新 GO、全部换新 Pointer），非 GO 对象=浅克隆 + 新 Pointer（克隆体与原件身份可区分）。</summary>
        public static T Instantiate<T>(T original) where T : Object
        {
            if (original == null) return null;
            if (original is GameObject go)
            {
                GameObject clone = new GameObject { name = go.name };
                for (int i = 0; i < go.Components.Count; i++)
                {
                    Component source = go.Components[i];
                    if (source == null) { clone.Components.Add(null); continue; }
                    Component copy = (Component)MemberwiseCloneOf(source);
                    copy.gameObject = clone;
                    copy.Pointer = FreshPointer();
                    clone.Components.Add(copy);
                }
                return (T)(object)clone;
            }
            T copyOfAsset = (T)MemberwiseCloneOf(original);
            copyOfAsset.Pointer = FreshPointer();
            return copyOfAsset;
        }

        private static object MemberwiseCloneOf(object target)
        {
            return typeof(object)
                .GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);
        }

        public static void DontDestroyOnLoad(Object target) { }

        // ---------- 场景替身：FindObjectsOfType 只认登记、未销毁、GO 活动的对象 ----------

        internal static readonly List<Object> AllObjects = new List<Object>();
        internal static bool FindObjectsOfTypeThrows;
        internal static int FindObjectsOfTypeCalls;

        public static T[] FindObjectsOfType<T>() where T : Object
        {
            FindObjectsOfTypeCalls++;
            if (FindObjectsOfTypeThrows) throw new InvalidOperationException("stub: FindObjectsOfType threw");
            List<T> found = new List<T>();
            for (int i = 0; i < AllObjects.Count; i++)
            {
                Object candidate = AllObjects[i];
                if (candidate == null || candidate.Destroyed) continue;
                Component component = candidate as Component;
                if (component != null)
                {
                    GameObject go = component.gameObject;
                    if (go == null || !go.activeSelf) continue;
                }
                if (candidate is T match) found.Add(match);
            }
            return found.ToArray();
        }

        internal static void ResetScene()
        {
            AllObjects.Clear();
            FindObjectsOfTypeThrows = false;
            FindObjectsOfTypeCalls = 0;
            DestroyCalls.Clear();
            _nextPointer = 0x2000;
        }
    }

    public class Component : Object
    {
        public Component() { }
        public Component(IntPtr pointer) : base(pointer) { }

        public GameObject gameObject { get; set; }

        public Transform transform => gameObject != null ? gameObject.transform : null;

        public T GetComponent<T>() where T : Component
        {
            return gameObject != null ? gameObject.GetComponent<T>() : null;
        }

        public T GetComponentInChildren<T>() where T : Component => GetComponent<T>();

        public T[] GetComponentsInChildren<T>() where T : Component
        {
            List<T> found = new List<T>();
            if (gameObject != null)
            {
                for (int i = 0; i < gameObject.Components.Count; i++)
                    if (gameObject.Components[i] is T match) found.Add(match);
            }
            return found.ToArray();
        }
    }

    public class MonoBehaviour : Component
    {
        public MonoBehaviour() { }
        public MonoBehaviour(IntPtr pointer) : base(pointer) { }
    }

    public class GameObject : Object
    {
        public int InstanceId;
        public Transform transform = new Transform();

        private bool _activeSelf = true;

        public bool activeSelf
        {
            get { return _activeSelf; }
            set { _activeSelf = value; }
        }

        /// <summary>替身无父子层级：activeInHierarchy == activeSelf（RecomputeOnLoad 读它）。</summary>
        public bool activeInHierarchy => _activeSelf;

        public readonly List<Component> Components = new List<Component>();

        /// <summary>true 时 GetComponent/TryGetComponent 抛异常（测试异常隔离）。</summary>
        public bool ComponentLookupThrows;

        public new int GetInstanceID() => InstanceId;

        public T GetComponent<T>() where T : Component
        {
            if (ComponentLookupThrows) throw new InvalidOperationException("stub: component lookup threw");
            for (int i = 0; i < Components.Count; i++)
                if (Components[i] is T match) return match;
            return null;
        }

        public bool TryGetComponent<T>(out T component) where T : Component
        {
            component = GetComponent<T>();
            return component != null;
        }

        public T AddComponent<T>() where T : Component
        {
            T component;
            if (typeof(T).GetConstructor(Type.EmptyTypes) != null) component = Activator.CreateInstance<T>();
            else component = (T)Activator.CreateInstance(typeof(T), new object[] { IntPtr.Zero });   // 注入组件惯例 (IntPtr) 构造器
            component.gameObject = this;
            component.Pointer = FreshPointer();
            Components.Add(component);
            return component;
        }

        /// <summary>镜像 Unity 消息分发：activeSelf 真切换时按组件顺序按名调用
        /// OnEnable/OnDisable（无参实例方法，含非公有）——组件序=AddComponent 顺序，
        /// 与生产克隆时序一致（Arrow 在前、后挂的穿墙件在后）。</summary>
        public void SetActive(bool value)
        {
            bool was = _activeSelf;
            _activeSelf = value;
            if (was == value) return;
            string message = value ? "OnEnable" : "OnDisable";
            for (int i = 0; i < Components.Count; i++)
            {
                Component component = Components[i];
                if (component == null) continue;
                MethodInfo handler = component.GetType().GetMethod(message,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (handler != null) handler.Invoke(component, null);
            }
        }
    }

    public class Transform
    {
        public Vector3 position;
    }

    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y) { this.x = x; this.y = y; }

        public static Vector2 operator *(Vector2 v, float f) => new Vector2(v.x * f, v.y * f);
        public static Vector2 operator *(float f, Vector2 v) => new Vector2(v.x * f, v.y * f);

        public float magnitude => (float)Math.Sqrt(x * (double)x + y * (double)y);

        public Vector2 normalized
        {
            get
            {
                float m = magnitude;
                return m > 0f ? new Vector2(x / m, y / m) : new Vector2(0f, 0f);
            }
        }

        public static Vector2 ClampMagnitude(Vector2 vector, float maxLength)
        {
            float m = vector.magnitude;
            if (m <= maxLength) return vector;
            return vector.normalized * maxLength;
        }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public static class Mathf
    {
        public static float Abs(float value) => Math.Abs(value);
        public static float Sqrt(float value) => (float)Math.Sqrt(value);
    }

    public static class Time
    {
        /// <summary>测试可推进的 unscaled 时间（墙快照 TTL 只看它）。</summary>
        public static float unscaledTime;
    }

    public static class Random
    {
        public static float value = 0.5f;
    }

    public class WaitForSeconds
    {
        public WaitForSeconds(float seconds) { }
    }

    public class Collider2D : Component
    {
        /// <summary>测试标签（生产代码读它会编不过真实 interop）。</summary>
        public string Label;
    }

    public class SpriteRenderer : Component
    {
        public Sprite sprite { get; set; }
    }

    public class Sprite : Object { }

    public class RuntimeAnimatorController : Object { }

    public class Animator : Component
    {
        public RuntimeAnimatorController runtimeAnimatorController;
    }

    public static class Resources
    {
        /// <summary>LoadAll 命中集（测试登记 Bolt 等）。</summary>
        internal static readonly List<Object> LoadAllRegistry = new List<Object>();
        /// <summary>FindObjectsOfTypeAll 命中集（测试登记控制器等）。</summary>
        internal static readonly List<Object> FindAllRegistry = new List<Object>();

        public static T[] LoadAll<T>(string path) where T : Object
        {
            List<T> found = new List<T>();
            for (int i = 0; i < LoadAllRegistry.Count; i++)
                if (LoadAllRegistry[i] is T match) found.Add(match);
            return found.ToArray();
        }

        public static T[] FindObjectsOfTypeAll<T>() where T : Object
        {
            List<T> found = new List<T>();
            for (int i = 0; i < FindAllRegistry.Count; i++)
                if (FindAllRegistry[i] is T match) found.Add(match);
            return found.ToArray();
        }

        internal static void Reset()
        {
            LoadAllRegistry.Clear();
            FindAllRegistry.Clear();
        }
    }

    /// <summary>Physics2D 替身：完整记录每次 IgnoreCollision 的 (箭碰撞体, 墙碰撞体, ignore)
    /// 序列，允许按对注入异常（单对失败隔离）。</summary>
    public static class Physics2D
    {
        public static Vector2 gravity = new Vector2(0f, -9.81f);

        internal struct IgnoreCall
        {
            internal Collider2D Arrow;
            internal Collider2D Wall;
            internal bool Ignore;
        }

        internal static readonly List<IgnoreCall> Calls = new List<IgnoreCall>();
        internal delegate void IgnoreHandler(Collider2D collider1, Collider2D collider2, bool ignore);
        internal static IgnoreHandler OnIgnore;
        internal static bool Throws;

        public static void IgnoreCollision(Collider2D collider1, Collider2D collider2, bool ignore)
        {
            if (collider1 == null || collider2 == null)
                throw new ArgumentNullException("stub: IgnoreCollision called with a null collider");
            if (Throws) throw new InvalidOperationException("stub: IgnoreCollision threw");
            if (OnIgnore != null) OnIgnore(collider1, collider2, ignore);
            Calls.Add(new IgnoreCall { Arrow = collider1, Wall = collider2, Ignore = ignore });
        }

        /// <summary>按箭一侧计数并过滤方向（true=穿透 / false=归还）。</summary>
        internal static int CountArrowPair(Collider2D arrow, bool ignore)
        {
            int n = 0;
            for (int i = 0; i < Calls.Count; i++)
                if (ReferenceEquals(Calls[i].Arrow, arrow) && Calls[i].Ignore == ignore) n++;
            return n;
        }

        internal static int CountWallPair(Collider2D wall, bool ignore)
        {
            int n = 0;
            for (int i = 0; i < Calls.Count; i++)
                if (ReferenceEquals(Calls[i].Wall, wall) && Calls[i].Ignore == ignore) n++;
            return n;
        }

        internal static void Reset()
        {
            Calls.Clear();
            OnIgnore = null;
            Throws = false;
            gravity = new Vector2(0f, -9.81f);
        }
    }
}

// Harmony 属性替身：只保证 [HarmonyPatch]/[Harmony*] 用法能编过（真实属性面由主 il2cpp
// 构建对 2.4 interop + 0Harmony 的编译门验证）。
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public readonly Type DeclaringType;
        public readonly string MethodName;

        public HarmonyPatch(Type declaringType, string methodName)
        {
            DeclaringType = declaringType;
            MethodName = methodName;
        }

        public HarmonyPatch(Type declaringType, string methodName, Type[] argumentTypes)
        {
            DeclaringType = declaringType;
            MethodName = methodName;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyFinalizer : Attribute { }
}

// ClassInjector 边界：只记录注册过的类型。
namespace Il2CppInterop.Runtime.Injection
{
    public static class ClassInjector
    {
        public static readonly System.Collections.Generic.HashSet<Type> Registered =
            new System.Collections.Generic.HashSet<Type>();

        public static bool IsTypeRegisteredInIl2Cpp(Type type) => Registered.Contains(type);

        public static void RegisterTypeInIl2Cpp<T>() => Registered.Add(typeof(T));
    }
}

// BepInEx 协程包装边界（SupervisorRoutine 宿主用；测试不执行协程）。
namespace BepInEx.Unity.IL2CPP.Utils.Collections
{
    public static class CollectionsExtensions
    {
        public static System.Collections.IEnumerator WrapToIl2Cpp(this System.Collections.IEnumerator routine) => routine;
    }
}
