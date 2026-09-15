// 直接源测试的最小 Unity/BepInEx 边界桩：只覆盖 HermesHeadwearDiagnostics 用到的成员。
// 语义照 Il2CppInterop 代理：字段生成为属性；对象销毁后 gameObject 返回 null。
// 渲染器/变换字段用属性并把读取计进计数器，用于断言“额度用尽/快照用尽的重复调用零原生读取”；
// `material`（会实例化的属性）单独计数，用于断言 helper 从不触碰它。
using System;
using System.Collections.Generic;
using System.Threading;

namespace UnityEngine
{
    public class Object
    {
        private static long _nextPointer = 4096;
        private IntPtr _pointer = (IntPtr)Interlocked.Increment(ref _nextPointer);

        internal static int PointerReads;

        public string name;

        public IntPtr Pointer
        {
            get { PointerReads++; return _pointer; }
            set { _pointer = value; }
        }
    }

    public class Component : Object
    {
        private GameObject _gameObject;

        internal static int ContextReads;

        public virtual GameObject gameObject
        {
            get { ContextReads++; return _gameObject; }
        }

        public Transform transform
        {
            get { ContextReads++; return _gameObject != null ? _gameObject.transform : null; }
        }

        public void Attach(GameObject owner)
        {
            _gameObject = owner;
        }

        public T GetComponent<T>() where T : Component
        {
            ContextReads++;
            return _gameObject != null ? _gameObject.GetComponent<T>() : null;
        }
    }

    public class MonoBehaviour : Component
    {
    }

    public class GameObject : Object
    {
        private static int _nextId = 100000;
        private readonly Transform _transform = new Transform();
        private readonly List<Component> _components = new List<Component>();

        internal static int InstanceIdReads;

        public int Id = ++_nextId;

        public bool activeSelf = true;
        public bool activeInHierarchy = true;
        public int layer;

        public GameObject()
        {
            _transform.Attach(this);
        }

        public Transform transform
        {
            get { return _transform; }
        }

        public int GetInstanceID()
        {
            InstanceIdReads++;
            return Id;
        }

        public void Add(Component component)
        {
            component.Attach(this);
            _components.Add(component);
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

    public class Transform : Component
    {
        internal static int FieldReads;

        private Vector3 _position;
        private Vector3 _lossyScale = new Vector3(1f, 1f, 1f);

        public bool ThrowOnPosition;

        public Transform parent;

        public Vector3 position
        {
            get { FieldReads++; if (ThrowOnPosition) throw new InvalidOperationException("position"); return _position; }
            set { _position = value; }
        }

        public Vector3 lossyScale
        {
            get { FieldReads++; return _lossyScale; }
            set { _lossyScale = value; }
        }
    }

    public class Sprite : Object
    {
    }

    public class Shader : Object
    {
    }

    public class Material : Object
    {
        public Shader shader;
    }

    public class SpriteRenderer : Component
    {
        internal static int FieldReads;
        internal static int Writes;
        internal static int MaterialAccesses;

        private bool _enabled = true;
        private Sprite _sprite;
        private Color _color = new Color(1f, 1f, 1f, 1f);
        private Material _sharedMaterial;
        private int _sortingLayerID;
        private int _sortingOrder;

        public bool ThrowOnSprite;
        public bool ThrowOnColor;
        public bool ThrowOnMaterial;
        public bool ThrowOnBounds;

        public bool enabled
        {
            get { FieldReads++; return _enabled; }
            set { Writes++; _enabled = value; }
        }

        public Sprite sprite
        {
            get { FieldReads++; if (ThrowOnSprite) throw new InvalidOperationException("sprite"); return _sprite; }
            set { Writes++; _sprite = value; }
        }

        public Color color
        {
            get { FieldReads++; if (ThrowOnColor) throw new InvalidOperationException("color"); return _color; }
            set { Writes++; _color = value; }
        }

        public Material sharedMaterial
        {
            get { FieldReads++; if (ThrowOnMaterial) throw new InvalidOperationException("sharedMaterial"); return _sharedMaterial; }
            set { Writes++; _sharedMaterial = value; }
        }

        /// <summary>会实例化材质副本的属性：诊断绝不能读它。</summary>
        public Material material
        {
            get { MaterialAccesses++; return _sharedMaterial; }
        }

        public int sortingLayerID
        {
            get { FieldReads++; return _sortingLayerID; }
            set { Writes++; _sortingLayerID = value; }
        }

        public int sortingOrder
        {
            get { FieldReads++; return _sortingOrder; }
            set { Writes++; _sortingOrder = value; }
        }

        public Bounds bounds
        {
            get
            {
                FieldReads++;
                if (ThrowOnBounds) throw new InvalidOperationException("bounds");
                return new Bounds(new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 0f));
            }
            set { Writes++; }
        }
    }

    public struct Vector3
    {
        public float x, y, z;

        public Vector3(float x, float y = 0f, float z = 0f)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    public struct Color
    {
        public float r, g, b, a;

        public Color(float r, float g, float b, float a = 1f)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }
    }

    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size)
        {
            this.center = center;
            this.size = size;
        }

        public Vector3 center { get; set; }
        public Vector3 size { get; set; }
    }
}

/// <summary>生产里 FriendlyTroll 是 Assembly-CSharp 的 IL2CPP 包装类型（全局命名空间）。</summary>
public class FriendlyTroll : UnityEngine.MonoBehaviour
{
    public UnityEngine.SpriteRenderer _mask;
    public UnityEngine.SpriteRenderer _maskPrefab;

    private int _maskIndexValue = -1;

    /// <summary>true = 模拟已销毁的包装对象：gameObject 返回 null（指针仍可读）。</summary>
    public bool Destroyed;

    public bool ThrowOnGameObject;
    public bool ThrowOnMaskIndex;

    public int _maskIndex
    {
        get { if (ThrowOnMaskIndex) throw new InvalidOperationException("maskIndex"); return _maskIndexValue; }
        set { _maskIndexValue = value; }
    }

    public override UnityEngine.GameObject gameObject
    {
        get
        {
            if (Destroyed) return null;
            if (ThrowOnGameObject) throw new InvalidOperationException("gameObject");
            return base.gameObject;
        }
    }
}

namespace KingdomEnhancedMod
{
    public sealed class LogSourceStub
    {
        public readonly List<string> Lines = new List<string>();
        public Action<string> OnInfo;

        public void LogInfo(string line)
        {
            OnInfo?.Invoke(line);
            Lines.Add(line);
        }
    }

    public sealed class PluginStub
    {
        public LogSourceStub LogSource = new LogSourceStub();
    }

    /// <summary>生产里是 BepInEx BasePlugin 单例；诊断只用 Instance?.LogSource.LogInfo(string)。</summary>
    public static class KingdomEnhancedPlugin
    {
        public static PluginStub Instance;
    }
}
