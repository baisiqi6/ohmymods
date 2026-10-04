// Host-side typed stubs for the Shared fast-forest-recede source compiled with ANDROID and the
// real android/OptionalQoLScope.cs. The game types mirror the deployed Il2CppInterop 1.5.1
// namespace-prefix interop surface the two sources actually touch:
//   - Assembly-CSharp global types live under Il2Cpp.* (ForestItem, Forest, Managers, World,
//     BiomeHolder) and every field is exposed as a property with get_/set_ accessors, exactly
//     like the generated interop (controlsForestSize/removedByForest/removeDelay/_forest);
//   - UnityEngine MonoBehaviour/Component/GameObject/Transform/Scene and Random keep their
//     UnityEngine names.
// These are host doubles, not native evidence; they exist to execute the production control
// flow and boundary checks, not to fake game state.
#if !ANDROID
#error This typed alias host must compile the shared source with ANDROID defined.
#endif
global using Il2Cpp;

using System;
using System.Collections.Generic;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute
    {
        public readonly Type TargetType;
        public readonly string MethodName;
        public HarmonyPatch() { }
        public HarmonyPatch(Type type) { TargetType = type; }
        public HarmonyPatch(Type type, string name) { TargetType = type; MethodName = name; }
    }

    public sealed class HarmonyPrefix : Attribute { }
}

namespace UnityEngine
{
    public class Object
    {
        private static long _nextPointer;
        private static long _nextId;
        private readonly int _id;
        public IntPtr Pointer { get; set; }
        public bool Destroyed;
        public string name;

        public Object(string name = "HostObject")
        {
            Pointer = (IntPtr)System.Threading.Interlocked.Increment(ref _nextPointer);
            _id = (int)System.Threading.Interlocked.Increment(ref _nextId);
            this.name = name;
        }

        public int GetInstanceID() => _id;

        public static implicit operator bool(Object value) => value is not null && !value.Destroyed;
        public static bool operator ==(Object left, Object right) => ReferenceEquals(left, right)
            || ((left is null || left.Destroyed) && (right is null || right.Destroyed));
        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => _id;
    }

    public class GameObject : Object
    {
        private static int _nextScene = 1000;
        private readonly List<Component> _components = new();
        public Transform transform;
        public bool activeInHierarchy = true;
        public SceneManagement.Scene scene;

        public GameObject(string name = "HostObject") : base(name)
        {
            transform = new Transform { gameObject = this };
            scene.handle = ++_nextScene;
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            _components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : class => _components.Find(entry => entry is T) as T;
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    }

    public class MonoBehaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }

    public class Transform : Component
    {
        public Transform parent;
        public readonly List<Transform> Children = new();

        public void SetParent(Transform newParent)
        {
            if (parent != null) parent.Children.Remove(this);
            parent = newParent;
            if (newParent != null && !newParent.Children.Contains(this)) newParent.Children.Add(this);
        }

        // Unity semantics: true for the parent itself and any strict descendant.
        public bool IsChildOf(Transform candidate)
        {
            for (Transform current = this; current != null; current = current.parent)
            {
                if (ReferenceEquals(current, candidate)) return true;
            }
            return false;
        }
    }

    public static class Random
    {
        public static int Calls;
        public static float Unit = 0.5f;
        public static float Range(float min, float max)
        {
            Calls++;
            return min + (max - min) * Unit;
        }
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public int handle;
    }
}

namespace Il2Cpp
{
    public class Forest : UnityEngine.MonoBehaviour { }

    public class ForestItem : UnityEngine.MonoBehaviour
    {
        private bool _controlsForestSize;
        private bool _removedByForest;
        private float _removeDelay = 10f;
        private Forest _forestRef;

        public bool ThrowOnControlsForestSize;
        public bool ThrowOnRemovedByForest;
        public bool ThrowOnRemoveDelay;
        public bool ThrowOnForest;
        public int AccessCount;

        public bool controlsForestSize
        {
            get { AccessCount++; if (ThrowOnControlsForestSize) throw new InvalidOperationException("controlsForestSize read"); return _controlsForestSize; }
            set { _controlsForestSize = value; }
        }

        public bool removedByForest
        {
            get { AccessCount++; if (ThrowOnRemovedByForest) throw new InvalidOperationException("removedByForest read"); return _removedByForest; }
            set { _removedByForest = value; }
        }

        public float removeDelay
        {
            get { AccessCount++; if (ThrowOnRemoveDelay) throw new InvalidOperationException("removeDelay read"); return _removeDelay; }
            set { _removeDelay = value; }
        }

        public Forest _forest
        {
            get { AccessCount++; if (ThrowOnForest) throw new InvalidOperationException("_forest read"); return _forestRef; }
            set { _forestRef = value; }
        }

        // Native body double for the checks that the prefix must not replace it: the native
        // first MoveNext draws removeDelay * Random(0.5, 1.5) whenever the effective delay is
        // not > 0, and FadeAndRemove marks removedByForest before branching.
        public int FadeAndRemoveCalls;
        public float NativeWait = -1f;
        public bool NativeRemoved;

        public void FadeAndRemove(float delay = 0f)
        {
            FadeAndRemoveCalls++;
            NativeWait = delay > 0f ? delay : _removeDelay * UnityEngine.Random.Range(0.5f, 1.5f);
            _removedByForest = true;
            NativeRemoved = true;
        }
    }

    public class Managers
    {
        public static Managers Inst;
        public World world;
    }

    public class World
    {
        public UnityEngine.Transform gameLayer;
    }

    public class BiomeHolder
    {
        public static BiomeHolder Inst;
        public int BiomeIndex;
    }
}

namespace KingdomEnhancedMod
{
    internal static class ModConfig
    {
        internal sealed class Setting<T>
        {
            internal T Value;
            internal Setting(T value) { Value = value; }
        }

        internal static readonly Setting<bool> Enabled = new(true);
        internal static readonly Setting<bool> FastForestRecedeEnabled = new(false);
    }

    internal sealed class ManualLogSource
    {
        internal readonly List<string> Warnings = new();
        internal bool ThrowOnWarning;

        internal void LogWarning(string message)
        {
            Warnings.Add(message);
            if (ThrowOnWarning) throw new InvalidOperationException("log sink failed");
        }

        internal void LogInfo(string message) { }
    }

    internal sealed class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance;
        internal readonly ManualLogSource LogSource = new ManualLogSource();

        internal static void Initialize()
        {
            if (Instance != null) return;
            Instance = new KingdomEnhancedPlugin();
        }
    }
}
