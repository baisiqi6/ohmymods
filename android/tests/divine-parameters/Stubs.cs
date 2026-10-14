// Host-only boundary doubles. Prefix/finalizer/scope bodies are the actual linked product.
// Getters can throw and count native access, setters can fail before or after their store.
// Unity membership mirrors the current OptionalQoLScope contract; no damage/conversion
// algorithm is implemented here and none of these types ship in the Android product.
global using Il2Cpp;
using System;

namespace DivineParameterTests
{
    internal static class NativeMeter
    {
        internal static int Reads;
        internal static bool Throw;
        internal static void Touch()
        {
            Reads++;
            if (Throw) throw new InvalidOperationException("native access forbidden");
        }
    }

    public sealed class Slot<T>
    {
        public T Raw;
        public int Reads, Writes;
        public bool ThrowRead;
        public int FailBefore, FailAfter;
        public T Value
        {
            get
            {
                NativeMeter.Touch();
                Reads++;
                if (ThrowRead) throw new InvalidOperationException("field getter failed");
                return Raw;
            }
            set
            {
                NativeMeter.Touch();
                Writes++;
                if (Writes == FailBefore) throw new InvalidOperationException("field setter failed before store");
                Raw = value;
                if (Writes == FailAfter) throw new InvalidOperationException("field setter failed after store");
            }
        }
    }
}

namespace UnityEngine
{
    using DivineParameterTests;
    public class Object
    {
        public static bool operator ==(Object a, Object b)
        {
            NativeMeter.Touch();
            return ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static T[] FindObjectsOfType<T>() => throw new InvalidOperationException("scene scan forbidden");
    }
    public struct Scene { public int handle; }
    public class GameObject : Object
    {
        public bool Active = true;
        public int SceneHandle = 9;
        public bool activeInHierarchy { get { NativeMeter.Touch(); return Active; } }
        public Scene scene { get { NativeMeter.Touch(); return new Scene { handle = SceneHandle }; } }
    }
    public class Transform : Object
    {
        public GameObject GameObject = new GameObject();
        public Transform Parent;
        public GameObject gameObject { get { NativeMeter.Touch(); return GameObject; } }
        public bool IsChildOf(Transform parent) { NativeMeter.Touch(); return ReferenceEquals(Parent, parent); }
    }
    public class Component : Object
    {
        public GameObject GameObject = new GameObject();
        public Transform Transform = new Transform();
        public GameObject gameObject { get { NativeMeter.Touch(); return GameObject; } }
        public Transform transform { get { NativeMeter.Touch(); return Transform; } }
    }
}

namespace Il2Cpp
{
    using DivineParameterTests;
    using UnityEngine;
    public class Scanner
    {
        public float range = 3f, rangeBehind = 4f;
    }
    public class HermesStaff : Component
    {
        public readonly Slot<int> Max = new Slot<int> { Raw = 8 };
        public int _maximumConvertedTrolls { get => Max.Value; set => Max.Value = value; }
        public float _itemCooldown = 7f, _abilityRange = 5f, _nextActivationTime = 123f, _cooldownAddPerTroll = 2f;
        public Scanner _trollScanner = new Scanner();
        public void Awake() { }
        public void CanActivate() { }
        public void TriggerItemAbility() { }
        public class _StartAbilityRoutine_d__17
        {
            public int State;
            public HermesStaff Owner;
            public int StateReads, OwnerReads;
            public bool ThrowState, ThrowOwner;
            public int __1__state
            {
                get { NativeMeter.Touch(); StateReads++; if (ThrowState) throw new InvalidOperationException("state read failed"); return State; }
            }
            public HermesStaff __4__this
            {
                get { NativeMeter.Touch(); OwnerReads++; if (ThrowOwner) throw new InvalidOperationException("owner read failed"); return Owner; }
            }
        }
    }
    public class ArtemisArrow : Component
    {
        public readonly Slot<float> Max = new Slot<float> { Raw = 6f };
        public float _maxHitsPerArrow { get => Max.Value; set => Max.Value = value; }
        public float _range = 8f, _nextActivationTime = 87f;
        public object _scanner = new object();
    }
    public class FriendlyTroll : Component
    {
        public float RevertTime = 99f;
        public bool ShouldRevertToTroll() => true;
    }
    public static class NetworkBigBoss
    {
        public static bool Online, Auth = true;
        public static bool IsOnline { get { NativeMeter.Touch(); return Online; } }
        public static bool HasWorldAuth { get { NativeMeter.Touch(); return Auth; } }
    }
    public class BiomeHolder : Component
    {
        public static BiomeHolder Current = new BiomeHolder();
        public int Index;
        public static BiomeHolder Inst { get { NativeMeter.Touch(); return Current; } }
        public int BiomeIndex { get { NativeMeter.Touch(); return Index; } }
    }
    public class World { public Transform gameLayer = new Transform(); }
    public class Managers
    {
        public static Managers Current = new Managers { world = new World() };
        public World world;
        public static Managers Inst { get { NativeMeter.Touch(); return Current; } }
    }
}

namespace KingdomEnhancedMod
{
    public sealed class Entry<T>
    {
        public T Raw;
        public bool Throw;
        public T Value { get { if (Throw) throw new InvalidOperationException("cfg unavailable"); return Raw; } set => Raw = value; }
    }
    public static class ModConfig
    {
        public static Entry<bool> Enabled = new Entry<bool> { Raw = true };
        public static Entry<bool> DivineParametersEnabled = new Entry<bool> { Raw = true };
        public static Entry<float> StaffCooldownMultiplier = new Entry<float> { Raw = 1f };
    }
    public sealed class LogSource
    {
        public int Errors;
        public bool Throw;
        public void LogError(object message) { Errors++; if (Throw) throw new InvalidOperationException("logger failure"); }
    }
    public sealed class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
        public LogSource LogSource = new LogSource();
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type) { }
        public HarmonyPatch(Type type, string method) { }
        public HarmonyPatch(string method) { }
    }
    public sealed class HarmonyPrefix : Attribute { }
    public sealed class HarmonyPostfix : Attribute { }
}
