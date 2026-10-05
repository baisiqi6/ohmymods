// Host-side typed stubs for the shared il2cpp/PatchWorld_OptionalVegetation.cs compiled with
// ANDROID plus the real android/OptionalQoLScope.cs. The game types mirror the deployed
// Il2CppInterop 1.5.1 namespace-prefix interop surface the two sources actually touch:
//   - Assembly-CSharp global types live under Il2Cpp.* (World / Grass / Managers /
//     NetworkBigBoss / BiomeHolder) and every field is exposed as a property with
//     get_/set_ accessors exactly like the generated interop (thicketSpacing,
//     _grassWithThicket, _thicket, ...), with counters and throw switches so the tests can
//     prove which reads happen;
//   - UnityEngine MonoBehaviour/GameObject/Transform/Scene/SpriteRenderer/Color/Time/Mathf
//     keep their UnityEngine names; SpriteRendererFX exists only as a counted fake so the
//     Android build can be shown never to start it (the production FX branch is compiled out);
//   - Il2CppSystem.Collections.Generic.ICollection/HashSet are the host shapes used by the
//     World._grassWithThicket field (declared ICollection, runtime HashSet) together with the
//     Cast/TryCast extensions the shared source calls.
// These are host doubles, not native evidence; they execute the production control flow and
// boundary checks, they do not fake game state.
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
    public sealed class HarmonyPostfix : Attribute { }
    public sealed class HarmonyFinalizer : Attribute { }
}

namespace UnityEngine
{
    public class Object
    {
        private static long _nextPointer;
        private static long _nextId;
        private IntPtr _pointer;
        private int _id;

        public bool Destroyed;
        public string name;

        public Object(string name = "HostObject")
        {
            _pointer = (IntPtr)System.Threading.Interlocked.Increment(ref _nextPointer);
            _id = (int)System.Threading.Interlocked.Increment(ref _nextId);
            this.name = name;
        }

        public int PointerReads;
        public bool ThrowOnPointer;

        // Interop shape: Il2CppObjectBase.Pointer is a property, not a field.
        public IntPtr Pointer
        {
            get { PointerReads++; if (ThrowOnPointer) throw new InvalidOperationException("Pointer read"); return _pointer; }
            set { _pointer = value; }
        }

        public int GetInstanceID() => _id;

        // Host-only hook so a test can simulate pool reuse (same native object identity).
        public void OverrideInstanceID(int id) => _id = id;

        public static implicit operator bool(Object value) => value is not null && !value.Destroyed;
        public static bool operator ==(Object left, Object right) => ReferenceEquals(left, right)
            || ((left is null || left.Destroyed) && (right is null || right.Destroyed));
        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => _id;
    }

    public struct Vector3
    {
        public float x, y, z;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public class GameObject : Object
    {
        private static int _nextScene = 1000;
        public readonly List<Component> Components = new();
        public Transform transform;
        public bool activeInHierarchy = true;
        public SceneManagement.Scene scene;

        public GameObject(string name = "GameObject") : base(name)
        {
            transform = new Transform { gameObject = this };
            scene.handle = ++_nextScene;
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            Components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : class
        {
            foreach (Component component in Components)
                if (component is T typed) return typed;
            return null;
        }

        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class
        {
            var found = new List<T>();
            CollectInto(found, includeInactive);
            return found.ToArray();
        }

        private void CollectInto<T>(List<T> found, bool includeInactive) where T : class
        {
            if (includeInactive || activeInHierarchy)
                foreach (Component component in Components)
                    if (component is T typed) found.Add(typed);
            foreach (Transform child in transform.Children)
                child.gameObject.CollectInto(found, includeInactive);
        }
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
        public Vector3 position;

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
                if (ReferenceEquals(current, candidate)) return true;
            return false;
        }
    }

    public class SpriteRenderer : Component
    {
        private Color _color;
        public int ColorReads;
        public int ColorWrites;
        public bool ThrowOnColorRead;
        public bool ThrowOnColorWrite;      // before-write error: throws without landing
        public bool ThrowAfterColorWrite;   // write-then-throw error: lands its value, then throws
        public bool ThrowAfterColorWriteOnce;

        public Color color
        {
            get { ColorReads++; if (ThrowOnColorRead) throw new InvalidOperationException("color read"); return _color; }
            set
            {
                ColorWrites++;
                if (ThrowOnColorWrite) throw new InvalidOperationException("color write");
                _color = value;
                if (ThrowAfterColorWrite) throw new InvalidOperationException("color write landed then threw");
                if (ThrowAfterColorWriteOnce)
                {
                    ThrowAfterColorWriteOnce = false;
                    throw new InvalidOperationException("color write landed then threw");
                }
            }
        }

        // Host-only: inspect the stored color while a read fault is active (tests only).
        public Color PeekColor => _color;
    }

    // Counted fake only: the Android build must never call it (the production FX branch is
    // compiled out); the host attaches one so the tests can assert the counter stays zero.
    public class SpriteRendererFX : MonoBehaviour
    {
        public int FadeOutCalls;
        public void FadeOut(float seconds, int endAction) => FadeOutCalls++;
    }

    public static class Time
    {
        public static float time;
    }

    public static class Mathf
    {
        public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float Abs(float value) => Math.Abs(value);
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public int handle;
    }
}

namespace Il2CppSystem.Collections.Generic
{
    // Host shapes of the interop collection used by World._grassWithThicket: the field is
    // declared ICollection<Grass> and holds a HashSet<Grass> at runtime.
    public class ICollection<T> : List<T> { }
    public class HashSet<T> : ICollection<T> { }
}

// Global namespace: extension methods are in scope for every call site without an extra using.
internal static class NativeCastExtensions
{
    internal static T Cast<T>(this object value) where T : class => (T)value;
    internal static T TryCast<T>(this object value) where T : class => value as T;
}

internal static class GameObjectHostExtensions
{
    internal static List<UnityEngine.Color> SnapshotSpriteColors(this UnityEngine.GameObject root)
    {
        var colors = new List<UnityEngine.Color>();
        foreach (UnityEngine.SpriteRenderer sprite in root.GetComponentsInChildren<UnityEngine.SpriteRenderer>(true))
            colors.Add(sprite.color);
        return colors;
    }
}

namespace MelonLoader
{
    // Host double of the deployed MelonPreferences_Entry<bool> shape the real
    // android/MobilePlayerConfig.cs hands to the shared source; only Value is consumed.
    public sealed class MelonPreferences_Entry<T>
    {
        public T Value;
        public MelonPreferences_Entry(T value) { Value = value; }
    }
}

namespace Il2Cpp
{
    public class World : UnityEngine.Object
    {
        private float _spacing = 4f;
        private Il2CppSystem.Collections.Generic.ICollection<Grass> _members;
        public UnityEngine.Transform gameLayer;

        public int SpacingReads;
        public int SpacingWrites;
        public bool ThrowOnSpacingGet;
        public bool ThrowAfterSpacingSet;        // the interop setter lands its value, then throws
        public bool ThrowAfterSpacingSetOnce;    // same, but only the first throwing write

        public float thicketSpacing
        {
            get { SpacingReads++; if (ThrowOnSpacingGet) throw new InvalidOperationException("thicketSpacing read"); return _spacing; }
            set
            {
                SpacingWrites++;
                _spacing = value;
                if (ThrowAfterSpacingSet) throw new InvalidOperationException("thicketSpacing write");
                if (ThrowAfterSpacingSetOnce)
                {
                    ThrowAfterSpacingSetOnce = false;
                    throw new InvalidOperationException("thicketSpacing write");
                }
            }
        }

        public int GrassWithThicketReads;
        public bool ThrowOnGrassWithThicket;

        public Il2CppSystem.Collections.Generic.ICollection<Grass> _grassWithThicket
        {
            get { GrassWithThicketReads++; if (ThrowOnGrassWithThicket) throw new InvalidOperationException("_grassWithThicket read"); return _members; }
            set { _members = value; }
        }

        public void ResetCounters()
        {
            SpacingReads = 0;
            SpacingWrites = 0;
            GrassWithThicketReads = 0;
            PointerReads = 0;
        }

        // Native methods referenced by the production [HarmonyPatch(nameof(...))] attributes.
        // The host drives the wrapper Prefix/Postfix around them instead of executing the
        // native bodies (which would need a full biome/kingdom simulation).
        public bool CanSpawnThicket(Grass grass) => throw new NotSupportedException("native body is not executed in this host");
        public void AddThicket(Grass grass) => throw new NotSupportedException("native body is not executed in this host");
    }

    public class Grass : UnityEngine.MonoBehaviour
    {
        private UnityEngine.GameObject _thicketRef;
        public int ThicketReads;
        public bool ThrowOnThicketGet;
        public int RemoveCalls;
        public bool RefuseRemoval;

        // Snapshot of the thicket's sprite colors at the moment the native body really
        // hands the object back (used to prove the mod restored them before removal).
        public System.Collections.Generic.List<UnityEngine.Color> RemovalColorSnapshot;

        public UnityEngine.GameObject _thicket
        {
            get { ThicketReads++; if (ThrowOnThicketGet) throw new InvalidOperationException("_thicket read"); return _thicketRef; }
            set { _thicketRef = value; }
        }

        // Native body double: auth gate, parent/activeHierarchy refusal, pool hand-back.
        public void RemoveThicket()
        {
            RemoveCalls++;
            if (_thicketRef == null) return;
            if (!NetworkBigBoss.HasWorldAuth) return;
            UnityEngine.Transform parent = transform.parent;
            if (parent == null || !parent.gameObject.activeInHierarchy) return;
            if (RefuseRemoval) return;
            RemovalColorSnapshot = _thicketRef.SnapshotSpriteColors();
            _thicketRef = null; // Pool::DespawnOrDestroy + this._thicket = null
        }
    }

    public class Managers
    {
        public static Managers Inst;
        public World world;
    }

    public static class NetworkBigBoss
    {
        private static bool _hasWorldAuth = true;
        public static int AccessCount;
        public static bool ThrowOnAuth;

        public static bool HasWorldAuth
        {
            get { AccessCount++; if (ThrowOnAuth) throw new InvalidOperationException("HasWorldAuth read"); return _hasWorldAuth; }
            set { _hasWorldAuth = value; }
        }
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
        internal static MelonLoader.MelonPreferences_Entry<bool> DenseThicketsEnabled = new(false);
        internal static int SaveCalls;

        internal static void Save() => SaveCalls++;
    }

    internal sealed class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance;
        internal readonly Logger LogSource = new();
    }

    internal sealed class Logger
    {
        internal readonly List<string> Infos = new();
        internal readonly List<string> Warnings = new();

        internal void LogInfo(string message) => Infos.Add(message);
        internal void LogWarning(string message) => Warnings.Add(message);
        internal void LogDebug(string message) { }
        internal void LogError(object message) => Warnings.Add(message?.ToString() ?? "");
    }
}
