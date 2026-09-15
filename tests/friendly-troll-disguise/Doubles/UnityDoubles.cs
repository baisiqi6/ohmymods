using System;
using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>
    /// 最小 Unity 替身。语义照 Il2CppInterop 代理：<c>Pointer</c> 存在、销毁后
    /// <c>gameObject</c>/<c>transform</c>/<c>GetComponent</c> 返回 null（不抛），
    /// <c>_mask.sprite</c> 之类的字段读取走属性 getter（与 interop 把字段生成为属性一致）。
    /// </summary>
    public class Object
    {
        private static int _nextPointer = 0x4000;

        internal GameObject Owner;
        internal bool Destroyed;

        public Object()
        {
            Pointer = new IntPtr(_nextPointer += 16);
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

        public int GetInstanceID()
        {
            return Pointer.ToInt32();
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
    }

    public sealed class Transform : Component
    {
        public Vector3 position;
        public Transform Parent;

        public void SetParent(Transform parent)
        {
            Parent = parent;
        }

        /// <summary>Unity 语义：自身也算子级（IsChildOf(self) == true）。</summary>
        public bool IsChildOf(Transform ancestor)
        {
            for (Transform current = this; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, ancestor)) return true;
            }
            return false;
        }
    }

    public sealed class Sprite : Object
    {
    }

    public sealed class SpriteRenderer : Component
    {
        public bool enabled = true;
        public Sprite sprite;
    }

    public sealed class GameObject : Object
    {
        private readonly List<Component> _components = new List<Component>();

        public bool activeInHierarchy = true;

        internal Transform Transform;

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T();
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
    }
}
