// Test-side stand-ins for the Il2CppInterop boundary. They exist only so the real production
// files can compile and run outside the game process; they are not game behaviour evidence.
using System;
using System.Collections.Generic;

namespace Il2CppInterop.Runtime.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public class HideFromIl2CppAttribute : Attribute { }
}

namespace Il2CppInterop.Runtime.Injection
{
    public sealed class RegisterTypeOptions
    {
        public Type[] Interfaces;
    }

    public static class ClassInjector
    {
        public static readonly HashSet<Type> Registered = new HashSet<Type>();
        public static bool IsTypeRegisteredInIl2Cpp(Type type) => Registered.Contains(type);
        public static void RegisterTypeInIl2Cpp(Type type, RegisterTypeOptions options = null) => Registered.Add(type);
    }
}

namespace Il2CppSystem
{
    // Real interop exposes il2cpp delegate types as managed delegates; the production code
    // casts method groups to this type and hands it to native callbacks.
    public delegate void Action<T>(T value);
}

namespace Il2CppSystem.Collections.Generic
{
    // Minimal stand-in for the interop list wrapper: native-style Pointer identity plus
    // ordinary enumeration. Production code only reads this type.
    public class List<T> : System.Collections.Generic.IEnumerable<T>
    {
        private static long _counter;
        public IntPtr Pointer = new IntPtr(++_counter);
        /// <summary>Test-only one-shot catalog read failure injection (transient IO model).</summary>
        public static int FailEnumerations;
        private readonly System.Collections.Generic.List<T> _items;

        public List() { _items = new System.Collections.Generic.List<T>(); }
        public List(System.Collections.Generic.IEnumerable<T> source) { _items = new System.Collections.Generic.List<T>(source); }
        public int Count => _items.Count;
        public T this[int index] { get => _items[index]; set => _items[index] = value; }
        public void Add(T item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public void Reverse() => _items.Reverse();
        public void RemoveAt(int index) => _items.RemoveAt(index);
        public System.Collections.Generic.IEnumerator<T> GetEnumerator()
        {
            if (FailEnumerations > 0) { FailEnumerations--; throw new InvalidOperationException("synthetic catalog read failure"); }
            return _items.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace Coatsink.Common
{
    [System.Flags] public enum SaveLoadResult { Save = 8, Failure = 128 }
}
