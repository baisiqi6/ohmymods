using System.Threading;

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    /// <summary>真实 interop 的长度与单槽读写边界。</summary>
    public sealed class Il2CppReferenceArray<T>
    {
        public int Length { get; }
        private readonly T[] items;
        public T this[int index] { get => items[index]; set => items[index] = value; }
        public Il2CppReferenceArray(int length) { Length = length; items = new T[length]; }
    }
}

namespace UnityEngine
{
    /// <summary>UnityEngine.Object 的最小忠实替身：Pointer 身份、销毁语义、TryCast。</summary>
    public class Object
    {
        private static long next;
        public IntPtr Pointer { get; set; } = (IntPtr)Interlocked.Increment(ref next);
        public bool Destroyed;
        public T TryCast<T>() where T : class => this as T;
        public static implicit operator bool(Object obj) => obj is not null && !obj.Destroyed;
        public static bool operator ==(Object a, Object b)
            => ReferenceEquals(a, b) || ((a is null || a.Destroyed) && (b is null || b.Destroyed));
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => Pointer.GetHashCode();
    }

    public class GameObject : Object
    {
        private readonly List<Component> components = new();
        public bool activeInHierarchy = true;
        public T AddComponent<T>() where T : Component, new()
        { var component = new T { gameObject = this }; components.Add(component); return component; }
        public T GetComponent<T>() where T : class => components.OfType<T>().FirstOrDefault();
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    }

    public class MonoBehaviour : Component { public bool enabled = true; }
}

// 游戏侧类型（真实 interop 同在全局命名空间）。
public interface IPayableComponentOwner { IntPtr Pointer { get; } }

public class Payable : UnityEngine.MonoBehaviour { }

public class PayableComponent : Payable
{
    public IPayableComponentOwner _owner { get; set; }
}

public class FireTower : UnityEngine.MonoBehaviour, IPayableComponentOwner
{
    /// <summary>模拟原生对象已销毁：字段读取抛异常（真实 interop 行为）。</summary>
    public bool ThrowOnRead;

    private int maxFireJars, fireJarsActiveNum;
    private Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.GameObject> fakeFireJars;

    public int _maxFireJars
    {
        get => ThrowOnRead ? throw new NullReferenceException("destroyed FireTower") : maxFireJars;
        set => maxFireJars = value;
    }

    public int _fireJarsActiveNum
    {
        get => ThrowOnRead ? throw new NullReferenceException("destroyed FireTower") : fireJarsActiveNum;
        set => fireJarsActiveNum = value;
    }

    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.GameObject> _fakeFireJars
    {
        get => ThrowOnRead ? throw new NullReferenceException("destroyed FireTower") : fakeFireJars;
        set => fakeFireJars = value;
    }
}
