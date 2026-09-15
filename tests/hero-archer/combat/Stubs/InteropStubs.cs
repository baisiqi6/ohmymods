using System;
using System.Collections.Generic;
using KingdomArcherOptions.Combat.Tests;

namespace Il2CppInterop.Runtime.InteropTypes
{
    /// <summary>IL2CPP wrapper base: identity is the native pointer (pool reuse keeps the same one).</summary>
    public abstract class Il2CppObjectBase
    {
        private static int _nextPointer = 0x1000;

        protected Il2CppObjectBase()
        {
            Pointer = new IntPtr(_nextPointer += 8);
        }

        public IntPtr Pointer { get; protected set; }

        /// <summary>Test-only: model a native address being reused by another object.</summary>
        public void SetPointerForTests(IntPtr pointer) => Pointer = pointer;

        /// <summary>Mirrors Il2CppInterop's managed-cast helper (reinterprets the same native object).</summary>
        public T Cast<T>() where T : Il2CppObjectBase => this as T;
    }
}

namespace Il2CppSystem.Collections.Generic
{
    /// <summary>Boundary stub for the il2cpp list type the native Pool keeps its instance cache in.</summary>
    public class List<T> : System.Collections.Generic.List<T>
    {
    }
}
