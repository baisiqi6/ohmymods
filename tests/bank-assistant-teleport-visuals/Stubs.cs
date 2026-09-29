// Minimal UnityEngine doubles for the bank-assistant teleport presentation suite.
// They model only the API surface BankAssistantTeleportVisuals.cs and CoinCourierTeleportFx.cs
// touch: no scene scans, no rendering, no real transform hierarchy math beyond parenting.
// Passing these tests does not establish IL2CPP compatibility or real rendering; the
// in-game check owns that.
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        public bool Alive = true;
        public string name = "";
        private static int _nextId;
        private static long _nextPointer;
        public readonly int Id;
        public IntPtr Pointer;

        public Object()
        {
            Id = ++_nextId;
            Pointer = (IntPtr)(0x20000000L + _nextPointer++ * 0x40L);
        }

        public int GetInstanceID() => Id;

        /// <summary>Test hook standing in for a destroyed/unusable native object (Unity fake-null).</summary>
        public bool ForceNull;

        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = (object)left == null;
            bool rightNull = (object)right == null;
            if (leftNull && rightNull) return true;
            if (rightNull) return !left.Alive || left.ForceNull;
            if (leftNull) return !right.Alive || right.ForceNull;
            return left.Id == right.Id && !left.ForceNull && !right.ForceNull;
        }

        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object other) => other is Object o && this == o;
        public override int GetHashCode() => Id;

        public static void Destroy(Object target)
        {
            if (target == null) return;
            target.Alive = false;
            if (target is GameObject gameObject)
            {
                gameObject.SetActive(false);
                foreach (var component in gameObject.Components) component.Alive = false;
            }
        }

        public static void DontDestroyOnLoad(Object target) { }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject != null ? gameObject.transform : null;
        public T GetComponent<T>() where T : Component => gameObject != null ? gameObject.GetComponent<T>() : null;
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
    }

    public class Transform : Component
    {
        public Transform parent;
        public Vector3 position;
        public Vector3 localPosition;
        public Vector3 localScale = Vector3.one;
        public readonly List<Transform> Children = new();

        public void SetParent(Transform value, bool worldPositionStays)
        {
            if (parent != null) parent.Children.Remove(this);
            parent = value;
            if (parent != null) parent.Children.Add(this);
        }
    }

    public class GameObject : Object
    {
        public static readonly List<GameObject> All = new();
        public readonly List<Component> Components = new();
        private readonly Transform _transform;
        private bool _active = true;

        public GameObject(string name = "object")
        {
            this.name = name;
            _transform = new Transform { gameObject = this };
            Components.Add(_transform);
            All.Add(this);
        }

        public Transform transform => _transform;
        public bool activeSelf => _active;
        public bool activeInHierarchy
        {
            get
            {
                if (!Alive || !_active) return false;
                for (Transform current = _transform; current != null; current = current.parent)
                {
                    GameObject owner = current.gameObject;
                    if (owner == null || !owner.Alive || !owner._active) return false;
                }
                return true;
            }
        }

        public void SetActive(bool value) => _active = value;

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            Components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault();
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y = 0f, float z = 0f) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float scale) => new Vector3(a.x * scale, a.y * scale, a.z * scale);
        public static bool operator ==(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public override bool Equals(object other) => other is Vector3 v && this == v;
        public override int GetHashCode() => HashCode.Combine(x, y, z);
        public override string ToString() => $"({x},{y},{z})";
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
    }

    public static class Time
    {
        public static float time;
        public static float deltaTime = 0.02f;
        public static int frameCount;
    }

    public class SpriteRenderer : Behaviour
    {
        public int sortingLayerID, sortingOrder;
    }

    public struct Keyframe
    {
        public float time, value, inTangent, outTangent;

        public Keyframe(float time, float value) : this(time, value, 0f, 0f) { }

        public Keyframe(float time, float value, float inTangent, float outTangent)
        {
            this.time = time;
            this.value = value;
            this.inTangent = inTangent;
            this.outTangent = outTangent;
        }
    }

    // Production only authors linear secant tangents; piecewise-linear evaluation is exact for
    // those keys and models the authored shape without importing Unity's curve editor.
    public class AnimationCurve
    {
        private readonly Keyframe[] _keys;

        public AnimationCurve(params Keyframe[] keys) => _keys = keys;

        public Keyframe[] keys => _keys;

        public float Evaluate(float time)
        {
            if (_keys == null || _keys.Length == 0) return 0f;
            if (!(time >= _keys[0].time)) return _keys[0].value;
            for (int i = 1; i < _keys.Length; i++)
            {
                if (time > _keys[i].time) continue;
                Keyframe a = _keys[i - 1];
                Keyframe b = _keys[i];
                float span = b.time - a.time;
                if (!(span > 0f)) return b.value;
                float t = (time - a.time) / span;
                return a.value + (b.value - a.value) * t;
            }
            return _keys[_keys.Length - 1].value;
        }
    }

    public class LineRenderer : Behaviour
    {
        public bool useWorldSpace, loop;
        public int positionCount;
        public int numCapVertices, numCornerVertices;
        public float startWidth, endWidth, widthMultiplier = 1f;
        public AnimationCurve widthCurve;
        public Color startColor, endColor;
        public Material sharedMaterial;
        public int sortingLayerID, sortingOrder;
        public readonly List<Vector3> Positions = new();

        /// <summary>Test hook: makes Begin's first geometry write throw (FX-owner warning path).</summary>
        public static bool FailSetPosition;

        public void SetPosition(int index, Vector3 value)
        {
            if (FailSetPosition) throw new InvalidOperationException("injected SetPosition failure");
            while (Positions.Count <= index) Positions.Add(default);
            Positions[index] = value;
        }
    }

    public class Shader : Object
    {
        public static Shader Find(string shaderName) => new Shader { name = shaderName };
    }

    public class Material : Object
    {
        public Material() { }
        public Material(Shader shader) { }
    }
}

// Game/mod surface used by the two production files (global namespace, like the game assembly).
public static class NetworkBigBoss
{
    public static bool IsOnline;
}

public class ManualLogSource
{
    public readonly List<string> Warnings = new();
    public void LogWarning(string message) => Warnings.Add(message);
}

public class KingdomEnhancedPlugin
{
    public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
    public ManualLogSource LogSource = new ManualLogSource();
}

namespace KingdomEnhancedMod
{
    /// <summary>窄 const stub：生产 BankAssistantTeleportVisuals 只读该值划分横/竖样式；
    /// 真实 owner 是 il2cpp/PatchEconomy_BankAssistants.cs（本套件不编译它）。</summary>
    internal static class PatchEconomy_BankAssistants
    {
        internal const int OriginalSlotCount = 4;
    }
}
