using System;
using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>
    /// 最小 Unity 对象替身。只模拟生产代码真正读取的成员：InstanceID、Pointer、
    /// gameObject 链、activeInHierarchy。销毁语义照抄 Unity/Il2CppInterop：
    /// 已销毁对象上的 <c>gameObject</c>/<c>GetComponent</c> 返回 null（不抛异常），
    /// 于是生产代码的 “null → 判定为死对象” 分支被真实走到。
    /// </summary>
    public class Object
    {
        private static int _nextInstanceId = 1;

        private readonly int _instanceId = _nextInstanceId++;
        internal bool _destroyed;

        public IntPtr Pointer { get; }

        public Object()
        {
            Pointer = new IntPtr(_instanceId * 16 + 0x1000);
        }

        public bool Destroyed => _destroyed;

        public int GetInstanceID()
        {
            if (_destroyed) throw new InvalidOperationException("destroyed object access");
            return _instanceId;
        }

        public void MarkDestroyed()
        {
            _destroyed = true;
        }

        public static bool operator ==(Object left, Object right) => ReferenceEquals(left, right);

        public static bool operator !=(Object left, Object right) => !ReferenceEquals(left, right);

        public override bool Equals(object obj) => ReferenceEquals(this, obj);

        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

        public override string ToString() => GetType().Name + "#" + _instanceId;
    }

    public class Component : Object
    {
        internal GameObject _owner;

        public GameObject gameObject => _owner == null || _owner.Destroyed ? null : _owner;

        public Transform transform => _owner == null || _owner.Destroyed ? null : _owner.transform;

        public T GetComponent<T>() where T : Component
        {
            GameObject owner = gameObject;
            return owner == null ? null : owner.GetComponent<T>();
        }
    }

    public class MonoBehaviour : Component
    {
    }

    public sealed class Transform : Component
    {
        internal readonly List<Transform> Children = new List<Transform>();

        public bool IsChildOf(Transform parent)
        {
            Transform current = this;
            while (current != null)
            {
                if (ReferenceEquals(current, parent)) return true;
                current = current._parent;
            }
            return false;
        }

        internal Transform _parent;

        public void SetParent(Transform parent)
        {
            _parent = parent;
        }
    }

    public sealed class GameObject : Object
    {
        private readonly List<Component> _components = new List<Component>();

        public bool activeInHierarchy = true;

        public GameObject(string name = "GameObject")
        {
            this.name = name;
            _transform = Attach(new Transform(), this);
        }

        public string name;

        private readonly Transform _transform;

        /// <summary>已销毁对象上访问组件返回 null（Unity/IL2CPP 语义），核心用它判定“真的没了”。</summary>
        public Transform transform => _destroyed ? null : _transform;

        public T AddComponent<T>(T component) where T : Component
        {
            return Attach(component, this);
        }

        public T GetComponent<T>() where T : Component
        {
            if (_destroyed) return null;
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T typed) return typed;
            }
            return null;
        }

        private static T Attach<T>(T component, GameObject owner) where T : Component
        {
            component._owner = owner;
            if (!(component is Transform))
            {
                owner._components.Add(component);
            }
            return component;
        }
    }

    /// <summary>
    /// Unity 的 <c>Time.time</c>：测试直接赋值以推进时间。带访问计数，
    /// 用于断言“回调线程没有触碰 Unity”。
    /// </summary>
    public static class Time
    {
        private static float _time;

        public static int AccessCount;

        public static float time
        {
            get
            {
                AccessCount++;
                return _time;
            }
            set
            {
                AccessCount++;
                _time = value;
            }
        }
    }

    /// <summary>
    /// Unity 的 <c>Random</c>：测试用固定序列驱动，保证 30% 命中/未命中与选择码可复现。
    /// </summary>
    public static class Random
    {
        private static readonly List<int> Sequence = new List<int>();
        private static int _cursor;

        public static int CallCount { get; private set; }

        public static void SetSequence(params int[] rawValues)
        {
            Sequence.Clear();
            _cursor = 0;
            CallCount = 0;
            if (rawValues != null) Sequence.AddRange(rawValues);
        }

        public static int Range(int minInclusive, int maxExclusive)
        {
            CallCount++;
            int span = maxExclusive - minInclusive;
            if (span <= 0) return minInclusive;
            int raw = _cursor < Sequence.Count ? Sequence[_cursor++] : 0;
            int value = raw % span;
            if (value < 0) value += span;
            return minInclusive + value;
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

    public struct Quaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;
    }
}
