using System;
using System.Collections.Generic;

// ============================================================================
// 运行时替身：UnityEngine / Il2CppInterop / Il2CppSystem / HarmonyLib。
// 只覆盖 PatchDivine_FriendlyTroll.cs 编译所需 + 本套件行为断言所需的最小面。
// 语义照 Il2CppInterop 代理：Pointer 存在、销毁后 gameObject/transform/
// GetComponent 返回 null（不抛）。
// ============================================================================

namespace UnityEngine
{
    public class Object
    {
        private static int _nextPointer = 0x4000;

        internal GameObject Owner;
        internal bool Destroyed;

        public Object()
        {
            Pointer = new IntPtr(_nextPointer += 16);
        }

        protected Object(IntPtr pointer)
        {
            Pointer = pointer;
        }

        public string name = string.Empty;

        public IntPtr Pointer { get; }

        public GameObject gameObject
        {
            get
            {
                if (Destroyed || Owner == null || Owner.Destroyed) return null;
                return Owner;
            }
        }

        public Transform transform
        {
            get
            {
                GameObject go = gameObject;
                return go == null ? null : go.Transform;
            }
        }

        /// <summary>人工夹具（非原生语义）：模拟 InstanceID 被复用；0 = 使用 Pointer 派生值。</summary>
        public int InstanceIdOverride;

        public int GetInstanceID()
        {
            return InstanceIdOverride != 0 ? InstanceIdOverride : Pointer.ToInt32();
        }

        public void MarkDestroyed()
        {
            Destroyed = true;
        }

        public static bool operator ==(Object left, Object right)
        {
            return ReferenceEquals(left, right);
        }

        public static bool operator !=(Object left, Object right)
        {
            return !ReferenceEquals(left, right);
        }

        public override bool Equals(object other)
        {
            return ReferenceEquals(this, other);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }

    public class Component : Object
    {
        protected Component()
        {
        }

        protected Component(IntPtr pointer) : base(pointer)
        {
        }

        internal void Attach(GameObject owner)
        {
            Owner = owner;
        }

        public T GetComponent<T>() where T : Component
        {
            GameObject go = gameObject;
            return go == null ? null : go.GetComponent<T>();
        }
    }

    public class MonoBehaviour : Component
    {
        public bool enabled = true;

        protected MonoBehaviour()
        {
        }

        protected MonoBehaviour(IntPtr pointer) : base(pointer)
        {
        }
    }

    public sealed class Transform : Component
    {
        public Vector3 position;

        public void SetParent(Transform parent)
        {
        }
    }

    public sealed class GameObject : Object
    {
        private readonly List<Component> _components = new List<Component>();

        public bool activeInHierarchy = true;

        internal Transform Transform;

        public T AddComponent<T>() where T : Component
        {
            T component = Create<T>();
            component.Attach(this);
            _components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : Component
        {
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T match) return match;
            }
            return null;
        }

        /// <summary>测试专用：模拟换组件——仅从挂载表移除，旧实例仍存活（Owner 不变）。</summary>
        public void RemoveComponent<T>() where T : Component
        {
            _components.RemoveAll(component => component is T);
        }

        private static T Create<T>() where T : Component
        {
            // 与真实 interop 一致：既能造无参组件，也能造 (IntPtr) 组件
            // （FriendlyTrollPursuitCoordinator 是后者）。
            var parameterless = typeof(T).GetConstructor(Type.EmptyTypes);
            if (parameterless != null)
                return (T)parameterless.Invoke(null);
            var pointerCtor = typeof(T).GetConstructor(new[] { typeof(IntPtr) });
            return (T)pointerCtor.Invoke(new object[] { IntPtr.Zero });
        }
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    public static class Mathf
    {
        public static float Abs(float value)
        {
            return Math.Abs(value);
        }

        public static float Max(float left, float right)
        {
            return Math.Max(left, right);
        }
    }

    public static class Time
    {
        public static float time;
        public static float timeScale = 1f;
    }
}

namespace Il2CppInterop.Runtime
{
    /// <summary>真实 interop 同名异常替身：可确证句柄不可用（非业务失败）。</summary>
    public sealed class ObjectCollectedException : Exception
    {
        public ObjectCollectedException()
        {
        }

        public ObjectCollectedException(string message) : base(message)
        {
        }
    }

    /// <summary>interop 的 Cast&lt;T&gt; 扩展替身（生产用它把 _behaviour 收窄成 Haglet）。</summary>
    internal static class Il2CppObjectExtensions
    {
        internal static T Cast<T>(this UnityEngine.Object source) where T : UnityEngine.Object
        {
            return source as T;
        }
    }
}

namespace Il2CppInterop.Runtime.Injection
{
    /// <summary>类型注入替身：只记录，不注册（本套件不跑 Unity）。</summary>
    internal static class ClassInjector
    {
        internal static bool IsTypeRegisteredInIl2Cpp(Type type)
        {
            return false;
        }

        internal static void RegisterTypeInIl2Cpp(Type type)
        {
        }
    }
}

namespace Il2CppSystem.Collections.Generic
{
    /// <summary>Il2Cpp List 替身：Count + 索引器 + Add/Remove/Contains。</summary>
    public class List<T>
    {
        private readonly System.Collections.Generic.List<T> _items =
            new System.Collections.Generic.List<T>();

        public int Count
        {
            get { return _items.Count; }
        }

        public T this[int index]
        {
            get { return _items[index]; }
            set { _items[index] = value; }
        }

        public void Add(T item)
        {
            _items.Add(item);
        }

        public bool Remove(T item)
        {
            return _items.Remove(item);
        }

        public bool Contains(T item)
        {
            return _items.Contains(item);
        }

        public void Clear()
        {
            _items.Clear();
        }
    }
}

namespace HarmonyLib
{
    /// <summary>HarmonyX 特性替身：只要够编译；本套件不跑 Harmony 管线。</summary>
    public class HarmonyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : HarmonyAttribute
    {
        public HarmonyPatch()
        {
        }

        public HarmonyPatch(Type declaringType)
        {
            DeclaringType = declaringType;
        }

        public HarmonyPatch(Type declaringType, string methodName)
        {
            DeclaringType = declaringType;
            MethodName = methodName;
        }

        public Type DeclaringType { get; }

        public string MethodName { get; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPostfix : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPrefix : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyFinalizer : Attribute
    {
    }
}
