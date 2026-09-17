// Unity/IL2CPP 边界 stub（combat-damage slice）：只模拟 CombatDamage.cs / CombatTargetLife.cs 实际触碰的
// 成员语义（Unity 对象相等语义、GameObject 组件查询、自有 ClassInjector 注入组件的生命周期消息与 hideFlags）。
// 生产源 CombatDamage.cs / CombatTargetLife.cs 原样编译进本测试程序集。
using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    /// <summary>hideFlags 子集（测试用：任何写入都会被 Object.HideFlagsWrites 计数；生产不得写 flags）。</summary>
    public enum HideFlags { None = 0, DontSave = 52 }

    public class Object
    {
        private static int NextId;
        public bool Destroyed;
        public string name = "";
        public readonly int InstanceID;
        public readonly IntPtr Pointer;

        /// <summary>测试计数：任何 hideFlags 写入都会累加（生产 marker 不得写 flags）。</summary>
        internal static int HideFlagsWrites;
        private HideFlags _hideFlags;
        public HideFlags hideFlags
        {
            get => _hideFlags;
            set { _hideFlags = value; HideFlagsWrites++; }
        }

        public Object()
        {
            InstanceID = ++NextId;
            Pointer = new IntPtr(InstanceID * 16);
        }

        public bool ThrowIdRead;
        public int GetInstanceID() => ThrowIdRead ? throw new InvalidOperationException("id read failed") : InstanceID;
        public void DestroySelf() => Destroyed = true;

        private static bool Dead(Object o) => ReferenceEquals(o, null) || o.Destroyed;
        /// <summary>Unity 语义：已销毁对象与 null 相等；一侧销毁/一侧存活则不相等。</summary>
        public static bool operator ==(Object a, Object b)
        {
            bool aNull = ReferenceEquals(a, null), bNull = ReferenceEquals(b, null);
            if (aNull && bNull) return true;
            if (aNull) return b.Destroyed;
            if (bNull) return a.Destroyed;
            return a.InstanceID == b.InstanceID;
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => other is Object o && this == o;
        public override int GetHashCode() => InstanceID;
        public override string ToString() => GetType().Name + "#" + InstanceID + (Destroyed ? "(destroyed)" : "");
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public bool enabled = true;
        public T GetComponent<T>() where T : class => gameObject != null ? gameObject.GetComponent<T>() : null;
    }

    public class MonoBehaviour : Component
    {
        public MonoBehaviour() { }
        /// <summary>ClassInjector 注入类型要求可反射构造的 (IntPtr) ctor（生产 marker 用它）。</summary>
        public MonoBehaviour(IntPtr pointer) { }
    }

    public sealed class GameObject : Object
    {
        private readonly List<Component> components = new List<Component>();
        public bool activeSelf = true;

        /// <summary>测试钩子：为 true 时活性读取抛异常（模拟「未知」而非「已确证停用」）。</summary>
        public bool ThrowActiveRead;
        public bool activeInHierarchy
        {
            get
            {
                if (ThrowActiveRead) throw new InvalidOperationException("active read failed");
                return activeSelf;
            }
        }

        /// <summary>测试钩子：为 true 时本 GO 的 AddComponent 抛异常（模拟 marker 创建失败）。</summary>
        public bool RejectAdd;

        public GameObject(string name = "go")
        {
            this.name = name;
        }

        public T Add<T>(T component) where T : Component
        {
            component.gameObject = this;
            components.Add(component);
            return component;
        }

        /// <summary>真实运行时未注册的注入类型（以 IntPtr ctor 识别）AddComponent/GetComponent 会抛异常。</summary>
        public T AddComponent<T>() where T : Component
        {
            RequireInjectedRegistration(typeof(T));
            if (RejectAdd) throw new InvalidOperationException("AddComponent rejected: " + typeof(T).Name);
            T component = UnityLifecycle.Create<T>();
            Add(component);
            if (activeInHierarchy && component.enabled) UnityLifecycle.Invoke(component, "OnEnable");   // Unity：活动 GO 上的新组件立即 OnEnable
            return component;
        }

        /// <summary>Unity 语义子集：停用/启用触发生命周期消息；disabled 组件收不到 GO 消息。</summary>
        public void SetActive(bool value)
        {
            if (activeSelf == value) return;
            activeSelf = value;
            for (int i = 0; i < components.Count; i++)
            {
                if (!components[i].enabled) continue;                      // 已 disable 的组件不派发 On/Off
                UnityLifecycle.Invoke(components[i], value ? "OnEnable" : "OnDisable");
            }
        }

        public T GetComponent<T>() where T : class
        {
            if (Destroyed) return null;
            RequireInjectedRegistration(typeof(T));
            foreach (Component c in components) if (c is T match) return match;
            return null;
        }

        internal static void RequireInjectedRegistration(Type type)
        {
            if (type.GetConstructor(new[] { typeof(IntPtr) }) == null) return;
            if (!Il2CppInterop.Runtime.Injection.ClassInjector.IsTypeRegisteredInIl2Cpp(type))
            {
                throw new InvalidOperationException("injected type not registered: " + type.Name);
            }
        }
    }

    /// <summary>
    /// 最小 Unity 生命周期驱动（测试用）：按类型反射调用 void OnEnable/OnDisable/OnDestroy，
    /// 并按 ClassInjector 注入约定优先使用 (IntPtr) ctor 构造组件。
    /// </summary>
    internal static class UnityLifecycle
    {
        internal static T Create<T>() where T : Component
        {
            ConstructorInfo injected = typeof(T).GetConstructor(new[] { typeof(IntPtr) });
            return injected != null
                ? (T)injected.Invoke(new object[] { IntPtr.Zero })
                : (T)Activator.CreateInstance(typeof(T));
        }

        internal static void Invoke(Component component, string message)
        {
            if (component == null || component.Destroyed) return;
            MethodInfo method = component.GetType().GetMethod(message,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (method != null) method.Invoke(component, null);
        }
    }
}

namespace Il2CppInterop.Runtime.Injection
{
    /// <summary>
    /// ClassInjector 子集：只登记类型。真实运行时的约束由 GameObject.RequireInjectedRegistration 模拟
    /// （未注册的注入类型 GetComponent/AddComponent 抛异常），生产必须先注册再接触。
    /// </summary>
    public static class ClassInjector
    {
        private static readonly HashSet<Type> Types = new HashSet<Type>();

        /// <summary>测试计数/失败注入：生产每进程只应尝试注册一次。</summary>
        internal static int RegisterCalls;
        internal static bool FailRegistration;

        public static bool IsTypeRegisteredInIl2Cpp(Type type) => Types.Contains(type);

        public static void RegisterTypeInIl2Cpp(Type type)
        {
            RegisterCalls++;
            if (FailRegistration) throw new InvalidOperationException("class injector unavailable");
            Types.Add(type);
        }

        /// <summary>测试用：清掉注册状态（生产不暴露 reset；调用方须同时显式重置生产私有状态）。</summary>
        internal static void ResetForTests()
        {
            Types.Clear();
            RegisterCalls = 0;
            FailRegistration = false;
        }
    }
}

namespace Il2CppInterop.Runtime.Attributes
{
    /// <summary>
    /// HideFromIl2CppAttribute 子集：生产用它把非 Unity 消息辅助方法从注入面隐藏
    /// （如 marker 的 EnsureLife / 活性读取）；桩只需类型存在，反射测试据此核验标注。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class HideFromIl2CppAttribute : Attribute
    {
    }
}
