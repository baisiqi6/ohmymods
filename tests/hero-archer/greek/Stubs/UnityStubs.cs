// Unity/IL2CPP 边界 stub：只模拟本模块实际触碰的成员语义（含 Unity 对象相等语义、
// 固定缓冲 OverlapCircle、ContactFilter2D 字段）。生产源 PatchArcher_GreekImpact.cs
// 原样编译进本测试程序集。
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        private static int NextId;
        public bool Destroyed;
        public string name = "";
        public readonly int InstanceID;
        public readonly IntPtr Pointer;

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

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0f);
        public static float Distance(Vector2 a, Vector2 b) { float dx = a.x - b.x, dy = a.y - b.y; return MathF.Sqrt(dx * dx + dy * dy); }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0f);
    }

    public struct LayerMask
    {
        public int value;
        public static implicit operator int(LayerMask m) => m.value;
        public static implicit operator LayerMask(int v) => new LayerMask { value = v };
        public static int NameToLayer(string layerName) => KingdomEnhancedMod.Env.LayerIndex(layerName);
        public static int GetMask(params string[] layerNames)
        {
            int mask = 0;
            if (layerNames != null) foreach (string n in layerNames) { int i = KingdomEnhancedMod.Env.LayerIndex(n); if (i >= 0) mask |= 1 << i; }
            return mask;
        }
    }

    public struct ContactFilter2D
    {
        public bool useTriggers, useLayerMask, useDepth, useOutsideDepth, useNormalAngle, useOutsideNormalAngle;
        public LayerMask layerMask;
        public float minDepth, maxDepth, minNormalAngle, maxNormalAngle;
        public ContactFilter2D NoFilter()
        {
            useTriggers = true;
            return this;
        }
        public void SetLayerMask(LayerMask mask) { useLayerMask = true; layerMask = mask; }
        public bool isFiltering => useTriggers || useLayerMask;
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public bool enabled = true;
        public Transform transform => gameObject != null ? gameObject.transform : null;
        public bool isActiveAndEnabled => gameObject != null && gameObject.activeInHierarchy && enabled;
        public T GetComponent<T>() where T : class => gameObject != null ? gameObject.GetComponent<T>() : null;
        public T GetComponentInParent<T>() where T : class => gameObject != null ? gameObject.GetComponentInParent<T>() : null;
    }

    public class MonoBehaviour : Component { }

    public class Transform : Component
    {
        public Transform parent;
        public Vector3 position;
        internal Transform owner;
        public override string ToString() => "Transform#" + InstanceID;
    }

    public sealed class GameObject : Object
    {
        private readonly List<Component> components = new List<Component>();
        public readonly Transform transform;
        public bool activeSelf = true;
        public int layer;
        public string tag = "Untagged";
        public bool CompareTag(string other) => tag == other;

        public GameObject(string name = "go", int layer = 0)
        {
            this.name = name;
            this.layer = layer;
            transform = new Transform { gameObject = this, owner = null };
            components.Add(transform);
        }

        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);

        public T Add<T>(T component) where T : Component
        {
            component.gameObject = this;
            components.Add(component);
            return component;
        }

        public T AddComponent<T>() where T : Component, new() => Add(new T());

        public T GetComponent<T>() where T : class
        {
            foreach (Component c in components) if (c is T match) return match;
            return null;
        }

        /// <summary>与 Options 边界同一签名：找不到即 false，不抛。</summary>
        public bool TryGetComponent<T>(out T component) where T : class
        {
            component = GetComponent<T>();
            return component != null;
        }

        public T GetComponentInParent<T>() where T : class
        {
            GameObject go = this;
            while (go != null)
            {
                T match = go.GetComponent<T>();
                if (match != null) return match;
                go = go.transform.parent != null ? go.transform.parent.gameObject : null;
            }
            return null;
        }

        public bool IsChildOf(GameObject parent)
        {
            for (Transform t = transform.parent; t != null; t = t.parent) if (t.gameObject == parent) return true;
            return false;
        }
    }

    public class Collider2D : Component
    {
        public bool isTrigger;
        // 测试用形状：以 Center 为心、Extent 为半径的圆（用于验证「中心在圈外、身体边缘进圈」）
        public Vector2 Center;
        public float Extent;

        public bool OverlapsCircle(Vector2 point, float radius)
        {
            float dx = Center.x - point.x, dy = Center.y - point.y;
            float reach = radius + Extent;
            return dx * dx + dy * dy <= reach * reach;
        }

        /// <summary>圆形状的最近点：外部点 → 朝向该点的表面点；内部点 → 自身。</summary>
        public Vector2 ClosestPoint(Vector2 point)
        {
            float dx = point.x - Center.x, dy = point.y - Center.y;
            float length = MathF.Sqrt(dx * dx + dy * dy);
            if (length <= 1e-6f || length <= Extent) return point;
            float scale = Extent / length;
            return new Vector2(Center.x + dx * scale, Center.y + dy * scale);
        }
    }

    public class Rigidbody2D : Component { }
    public class SpriteRenderer : Component { }
    public class Animator : Component { }
    public class Coroutine { }

    public static class Mathf
    {
        public const float Deg2Rad = 0.0174532924f;
        public const float Rad2Deg = 57.29578f;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Abs(float value) => MathF.Abs(value);
        public static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);
        public static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);
        public static float Sqrt(float value) => MathF.Sqrt(value);
        public static float Cos(float value) => MathF.Cos(value);
        public static float Sin(float value) => MathF.Sin(value);
    }

    public static class Time
    {
        public static float time;
        public static float timeScale = 1f;
        public static int frameCount;
    }

    public static class Physics2D
    {
        public static int QueryCount;
        public static Vector2 LastPoint;
        public static float LastRadius;
        public static ContactFilter2D LastFilter;
        public static bool OverflowIgnored;

        public static int OverlapCircle(Vector2 point, float radius, ContactFilter2D contactFilter,
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Collider2D> results)
        {
            QueryCount++;
            LastPoint = point;
            LastRadius = radius;
            LastFilter = contactFilter;
            int written = 0;
            foreach (Collider2D collider in KingdomEnhancedMod.Env.Colliders)
            {
                if (collider == null || !collider.enabled || collider.gameObject == null || !collider.gameObject.activeInHierarchy) continue;
                if (contactFilter.useLayerMask && ((1 << collider.gameObject.layer) & contactFilter.layerMask.value) == 0) continue;
                if (!contactFilter.useTriggers && collider.isTrigger) continue;
                if (!collider.OverlapsCircle(point, radius)) continue;
                if (written < results.Length) results[written++] = collider;
                else { OverflowIgnored = true; break; }   // 真实 native 只写满缓冲容量即止
            }
            return written;
        }
    }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    /// <summary>固定长度缓冲（本模块只用 ctor(long)/Length/索引器）。</summary>
    public class Il2CppReferenceArray<T> where T : class
    {
        private readonly T[] items;
        public Il2CppReferenceArray(long length) { items = new T[length]; }
        public int Length => items.Length;
        public T this[int index]
        {
            get => items[index];
            set => items[index] = value;
        }
    }
}
