using System;
using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>
    /// Unity 对象边界桩：身份 = native 指针（池复用保持同一指针），销毁用 Unity 的 fake-null 语义建模
    /// （`obj == null` 为 true），生产代码里的每个 `== null` 守卫都被真实走到。
    /// </summary>
    public class Object
    {
        private static int _nextInstanceId = 100;
        private static int _nextPointer = 0x2000;

        public IntPtr Pointer = new IntPtr(_nextPointer += 8);
        private readonly int _instanceId = ++_nextInstanceId;
        private bool _destroyed;

        public void DestroyForTests() => _destroyed = true;

        /// <summary>临时身份读失败（不销毁）：模型瞬时 native 读异常。</summary>
        public bool ThrowOnInstanceId;

        /// <summary>销毁后读取身份：模型为原生 MissingReference 抛出（生产代码必须 try/catch）。</summary>
        public virtual int GetInstanceID()
        {
            if (_destroyed) throw new InvalidOperationException("simulated native read on a destroyed object");
            if (ThrowOnInstanceId) throw new InvalidOperationException("simulated transient native identity read failure");
            return _instanceId;
        }

        public static bool operator ==(Object a, Object b)
        {
            bool aNull = ReferenceEquals(a, null) || a._destroyed;
            bool bNull = ReferenceEquals(b, null) || b._destroyed;
            if (aNull || bNull) return aNull && bNull;
            return ReferenceEquals(a, b);
        }

        public static bool operator !=(Object a, Object b) => !(a == b);

        public override bool Equals(object other) => this == (other as Object);

        public override int GetHashCode() => Pointer.GetHashCode();
    }

    public struct Color
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static Color white => new Color(1f, 1f, 1f, 1f);

        public override string ToString() => "(" + r + "," + g + "," + b + "," + a + ")";
    }

    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public struct Scene
    {
        public int handle;
    }

    public static class Mathf
    {
        public static float Abs(float value) => Math.Abs(value);
    }

    /// <summary>仅 unscaledTime 被回执退避使用；测试直接可写。</summary>
    public static class Time
    {
        public static float unscaledTime;
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform;
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
    }

    public class MonoBehaviour : Behaviour
    {
    }

    public class Transform : Component
    {
        public Transform Parent;

        public bool IsChildOf(Transform parent)
        {
            for (Transform p = this; p != null; p = p.Parent) if (p == parent) return true;
            return false;
        }
    }

    public class GameObject : Object
    {
        private readonly List<Component> _components = new List<Component>();

        public bool activeSelf = true;
        public bool activeInHierarchy = true;
        public Scene scene;

        public void AddComponentForTests(Component component)
        {
            component.gameObject = this;
            _components.Add(component);
        }

        public T GetComponent<T>() where T : Component
        {
            for (int i = 0; i < _components.Count; i++)
                if (_components[i] is T match) return match;
            return null;
        }
    }

    /// <summary>实例色 + 可注入的读写失败（模型 IL2CPP trampoline 异常）。</summary>
    public class SpriteRenderer : Component
    {
        private Color _color = new Color(1f, 1f, 1f, 1f);

        public bool enabled = true;
        public bool ThrowOnColorRead;
        public bool ThrowOnColorWrite;
        /// <summary>写已落地但调用仍抛异常（模型"写异常回执必须保守保留"的现场）。</summary>
        public bool WriteLandsThenThrows;
        public int ColorWriteCount;
        public int ColorReadCount;

        public Color color
        {
            get
            {
                ColorReadCount++;
                if (ThrowOnColorRead) throw new InvalidOperationException("simulated native color read failure");
                return _color;
            }
            set
            {
                ColorWriteCount++;
                if (ThrowOnColorWrite)
                {
                    if (WriteLandsThenThrows) _color = value;
                    throw new InvalidOperationException("simulated native color write failure");
                }
                _color = value;
            }
        }
    }
}
