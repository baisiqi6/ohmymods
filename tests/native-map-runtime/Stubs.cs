// R3 实际入口 probe 的最小 Unity/Il2Cpp/Harmony/Game 桩。
// 目的：让**未修改的真实 src/MapMountIcons.cs + src/MapResourceIconPlan.cs** 原样编译进本 probe，
// 由真实 patch 入口方法体驱动控制流。桩只实现被真实源码引用到的成员；几何/生命周期用最小但仍
// 忠实的模型（层级 + 局部仿射 + sizeDelta→rect + 销毁语义 + Unity == 语义）。
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    using UnityEngine.SceneManagement;
    using Il2CppInterop.Runtime.InteropTypes.Arrays;

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 one => new Vector2(1f, 1f);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float s) => new Vector2(a.x * s, a.y * s);
        public static Vector2 operator /(Vector2 a, float s) => new Vector2(a.x / s, a.y / s);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0f);
        public override string ToString() => "(" + x + "," + y + ")";
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y) { this.x = x; this.y = y; z = 0f; }
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);
        public static Vector3 operator /(Vector3 a, float s) => new Vector3(a.x / s, a.y / s, a.z / s);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
    }

    public struct Quaternion
    {
        public static Quaternion identity => new Quaternion();
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height)
        { this.x = x; this.y = y; this.width = width; this.height = height; }
        public Rect(Vector2 position, Vector2 size) { x = position.x; y = position.y; width = size.x; height = size.y; }
        public float xMin => x;
        public float yMin => y;
        public float xMax => x + width;
        public float yMax => y + height;
        public Vector2 center => new Vector2(x + width * 0.5f, y + height * 0.5f);
        public Vector2 size => new Vector2(width, height);
        public Vector2 position => new Vector2(x, y);
    }

    public struct Bounds
    {
        public Vector3 center;
        public Vector3 size;
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
        public Vector3 extents => new Vector3(size.x * 0.5f, size.y * 0.5f, size.z * 0.5f);
        public Vector3 min => new Vector3(center.x - extents.x, center.y - extents.y, center.z - extents.z);
        public Vector3 max => new Vector3(center.x + extents.x, center.y + extents.y, center.z + extents.z);
    }

    public enum RenderMode { ScreenSpaceOverlay = 0, ScreenSpaceCamera = 1, WorldSpace = 2 }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public enum TextureFormat { RGBA32 = 4 }
    public enum FilterMode { Point = 0, Bilinear = 1 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1 }
    public enum SpriteMeshType { FullRect = 1 }

    /// <summary>岸线 art 管线所需的最小 Texture2D 桩（真实 ReadResource → LoadImage → GetPixels32 路径）。</summary>
    public class Texture2D : Object
    {
        public int width, height;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public int anisoLevel;
        internal Color32[] Pixels = Array.Empty<Color32>();
        public Texture2D(int w, int h, TextureFormat fmt, bool mipmap)
        {
            width = w;
            height = h;
            Pixels = new Color32[w * h];
        }
        public void SetPixels32(Color32[] pixels) { Pixels = (Color32[])pixels.Clone(); }
        public Color32[] GetPixels32() => Pixels;
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }
    }

    public static class ImageConversion
    {
        public static bool LoadImage(Texture2D tex, byte[] bytes, bool markNonReadable)
        {
            if (tex == null || bytes == null) return false;
            RuntimeProbe.Png.Decode(bytes, out int w, out int h, out Color32[] topDown);
            if (w <= 0 || h <= 0 || topDown == null) return false;
            // Unity 契约：LoadImage 之后 GetPixels32/SetPixels32 为 bottom-up 行序（index 0 = 图像左下）。
            // PNG 文件行序是 top-down：这里做**唯一一次**垂直翻转；生产 BuildPrep 不额外翻。
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                Array.Copy(topDown, y * w, pixels, (h - 1 - y) * w, w);
            }
            tex.width = w;
            tex.height = h;
            tex.Pixels = pixels;
            return true;
        }
    }

    public class Object : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public string name;
        private bool _destroyed;
        internal bool Destroyed => _destroyed;
        private static int _nextInstanceId;
        private readonly int _instanceId = System.Threading.Interlocked.Increment(ref _nextInstanceId);
        public int GetInstanceID() => _instanceId;

        internal void MarkDestroyed() { _destroyed = true; }

        public static void Destroy(Object obj)
        {
            if (obj == null) return;
            obj.MarkDestroyed();
            (obj as GameObject)?.DetachFromParent();
        }

        public static void DestroyImmediate(Object obj) => Destroy(obj);

        public static T Instantiate<T>(T original, Transform parent, bool worldPositionStays) where T : Component
        {
            if (original == null) return null;
            var src = original;
            string srcName = src.gameObject != null ? src.gameObject.name : src.name;
            var go = new GameObject((srcName ?? "") + "(Clone)");
            if (parent != null) go.transform.SetParent(parent, worldPositionStays);
            var clone = go.AddComponent<T>();
            clone.CopyFrom(src);
            // 真实 Instantiate 复制整个对象（含 RectTransform 几何）：icon clone 的 sizeDelta 来自 source prefab。
            RectTransform srcRect = src.gameObject.GetComponent<RectTransform>();
            RectTransform dstRect = go.GetComponent<RectTransform>();
            if (srcRect != null && dstRect != null)
            {
                dstRect.sizeDelta = srcRect.sizeDelta;
                dstRect.pivot = srcRect.pivot;
                dstRect.anchorMin = srcRect.anchorMin;
                dstRect.anchorMax = srcRect.anchorMax;
                dstRect.localScale = srcRect.localScale;
            }
            return clone;
        }

        public static bool operator ==(Object a, Object b)
        {
            bool aNull = a is null || a._destroyed;
            bool bNull = b is null || b._destroyed;
            if (aNull && bNull) return true;
            if (aNull || bNull) return false;
            return ReferenceEquals(a, b);
        }

        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }

    public class Component : Object
    {
        internal GameObject Owner;

        public GameObject gameObject => Owner;
        public Transform transform => Owner != null ? Owner.transform : null;

        public T GetComponent<T>() where T : Component => Owner != null ? Owner.GetComponent<T>() : null;
        public T GetComponentInParent<T>() where T : Component => Owner != null ? Owner.GetComponentInParent<T>() : null;
        public T GetComponentInChildren<T>() where T : Component => Owner != null ? Owner.GetComponentInChildren<T>() : null;
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
            => Owner != null ? Owner.GetComponentsInChildren<T>(includeInactive) : new T[0];

        internal virtual void CopyFrom(Component source) { }
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
    }

    public class MonoBehaviour : Behaviour { }

    public sealed class GameObject : Object
    {
        private readonly List<Component> _components = new List<Component>();
        private Transform _transform;

        public GameObject() : this("GameObject") { }

        public GameObject(string name)
        {
            this.name = name;
            _transform = new RectTransform { Owner = this, name = name };
            _components.Add(_transform);
        }

        public Transform transform => _transform;
        public bool activeSelf { get; private set; } = true;

        // R2 复核 fixture 仪器：scene 身份、GetComponent/扫描计数与可控读异常
        public Scene scene = new Scene(1);
        internal bool ThrowGet;
        internal int Gets;
        internal int Scans;
        internal int Adds;
        internal int ComponentCountForProbe => _components.Count;

        public bool activeInHierarchy
        {
            get
            {
                for (Transform t = _transform; t != null; t = t.parent)
                {
                    if (t.gameObject != null && !t.gameObject.activeSelf) return false;
                }
                return true;
            }
        }

        internal int SetActiveCalls;

        public void SetActive(bool value) { SetActiveCalls++; activeSelf = value; }

        internal void DetachFromParent() => _transform?.SetParent(null, false);

        public T AddComponent<T>() where T : Component
        {
            Adds++;
            if (typeof(T) == typeof(RectTransform)) return (T)(object)_transform;
            if (typeof(T) == typeof(CanvasGroup)) EntryProbe.ProbeHooks.CanvasGroupAdds++;
            var component = (T)Activator.CreateInstance(typeof(T));
            component.Owner = this;
            _components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : Component
        {
            Gets++;
            if (ThrowGet) throw new InvalidOperationException("GetComponent");
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T typed && !typed.Destroyed) return typed;
            }
            return null;
        }

        public T GetComponentInParent<T>() where T : Component
        {
            for (Transform t = _transform; t != null; t = t.parent)
            {
                T found = t.gameObject != null ? t.gameObject.GetComponent<T>() : null;
                if (found != null) return found;
            }
            return null;
        }

        public T GetComponentInChildren<T>() where T : Component
        {
            T found = GetComponent<T>();
            if (found != null) return found;
            for (int i = 0; i < _transform.childCount; i++)
            {
                T child = _transform.GetChild(i).gameObject.GetComponentInChildren<T>();
                if (child != null) return child;
            }
            return null;
        }

        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            Scans++;
            var list = new List<T>();
            Collect(list, includeInactive);
            return list.ToArray();
        }

        private void Collect<T>(List<T> into, bool includeInactive) where T : Component
        {
            if (!includeInactive && !activeSelf) return;
            T own = GetComponent<T>();
            if (own != null) into.Add(own);
            for (int i = 0; i < _transform.childCount; i++)
            {
                _transform.GetChild(i).gameObject.Collect(into, includeInactive);
            }
        }
    }

    public class Transform : Component
    {
        private readonly List<Transform> _children = new List<Transform>();

        public Vector3 localPosition;
        private Vector3 _localScale = Vector3.one;
        /// <summary>测试注入：几何 setter 故障（detail/overview 摆位失败路径）。</summary>
        internal bool ThrowOnScaleSet;
        public Vector3 localScale
        {
            get => _localScale;
            set
            {
                if (ThrowOnScaleSet) throw new InvalidOperationException("scale");
                _localScale = value;
            }
        }
        public Quaternion localRotation;
        public Transform parent { get; private set; }

        /// <summary>真实 Unity 语义：沿父链累乘 localScale（映射/形变判定用；翻转可由符号读出）。</summary>
        public Vector3 lossyScale
        {
            get
            {
                Vector3 scale = _localScale;
                for (Transform cursor = parent; cursor != null; cursor = cursor.parent)
                {
                    Vector3 p = cursor._localScale;
                    scale = new Vector3(scale.x * p.x, scale.y * p.y, scale.z * p.z);
                }
                return scale;
            }
        }

        public int childCount => _children.Count;
        public Transform GetChild(int index) => _children[index];

        public void SetParent(Transform newParent, bool worldPositionStays)
        {
            parent?._children.Remove(this);
            parent = newParent;
            newParent?._children.Add(this);
        }

        public void SetParent(Transform newParent) => SetParent(newParent, true);

        public void SetAsLastSibling() { }
        public int GetSiblingIndex() => 0;

        internal Vector3 WorldPosition
        {
            get { return parent == null ? localPosition : parent.WorldPosition + Mul(parent.WorldScale, localPosition); }
        }

        internal Vector3 WorldScale
        {
            get { return parent == null ? localScale : Mul(parent.WorldScale, localScale); }
        }

        private static Vector3 Mul(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);

        public Vector3 TransformPoint(Vector3 local)
        {
            Vector3 world = WorldPosition;
            Vector3 scale = WorldScale;
            return new Vector3(world.x + local.x * scale.x, world.y + local.y * scale.y, world.z + local.z * scale.z);
        }

        public Vector3 InverseTransformVector(Vector3 vector)
        {
            Vector3 scale = WorldScale;
            return new Vector3(
                scale.x == 0f ? 0f : vector.x / scale.x,
                scale.y == 0f ? 0f : vector.y / scale.y,
                scale.z == 0f ? 0f : vector.z / scale.z);
        }

        public Vector3 InverseTransformPoint(Vector3 world)
        {
            Vector3 origin = WorldPosition;
            Vector3 scale = WorldScale;
            return new Vector3(
                scale.x == 0f ? 0f : (world.x - origin.x) / scale.x,
                scale.y == 0f ? 0f : (world.y - origin.y) / scale.y,
                scale.z == 0f ? 0f : (world.z - origin.z) / scale.z);
        }
    }

    public class RectTransform : Transform
    {
        private Vector2 _sizeDelta;
        private Vector2 _anchoredPosition;

        public Vector2 anchorMin = new Vector2(0.5f, 0.5f);
        public Vector2 anchorMax = new Vector2(0.5f, 0.5f);
        public Vector2 pivot = new Vector2(0f, 0f);

        /// <summary>
        /// 真实 Unity 派生关系：直接写 localPosition（如 actual 锚点/轴心的 fixture 布局）时，
        /// anchoredPosition 需要按 anchor/pivot 反算（快照/恢复走 anchoredPosition 通道）。
        /// 世界 fixture 仍以 anchoredPosition 为准（下方属性同步 localPosition），语义不变。
        /// </summary>
        private bool _suppressDerive;

        /// 真实 Unity 锚点参考点（父 rect 本地：anchor 分率 * 父尺寸 + 父 rect.min；pivot 参与拉伸轴）。
        private Vector2 AnchorReference(RectTransform parentRect)
        {
            Rect r = parentRect.rect;
            return new Vector2(
                r.xMin + r.width * (anchorMin.x + (anchorMax.x - anchorMin.x) * pivot.x),
                r.yMin + r.height * (anchorMin.y + (anchorMax.y - anchorMin.y) * pivot.y));
        }

        public new Vector3 localPosition
        {
            get => base.localPosition;
            set
            {
                base.localPosition = value;
                if (_suppressDerive) return;   // 经 anchoredPosition 写入：不再反算
                if (parent is RectTransform parentRect)
                {
                    Vector2 refPoint = AnchorReference(parentRect);
                    _anchoredPosition = new Vector2(value.x - refPoint.x, value.y - refPoint.y);
                }
                else
                {
                    _anchoredPosition = new Vector2(value.x, value.y);
                }
            }
        }

        /// <summary>测试注入：几何 setter 故障（sizeDelta / anchoredPosition）。</summary>
        internal bool ThrowOnSizeDeltaSet;
        internal bool ThrowOnAnchoredSet;

        public Vector2 sizeDelta
        {
            get => _sizeDelta;
            set
            {
                if (ThrowOnSizeDeltaSet) throw new InvalidOperationException("size-delta");
                _sizeDelta = value;
            }
        }

        /// 世界 fixture 归一化语义：anchoredPosition 写入即 localPosition（本 stub 的历史约定，既有场景依赖）。
        /// 注意：对 anchor(0,0) 子节点位于 pivot≠0 父节点下的情形，真实 Unity 会再叠加“父 rect 原点 - 锚点参考”
        /// 常量；本 stub 不建模该常量。因此这类节点的探针断言必须扣除常量（见 runtime-probe 的 PlacementSpace）。
        public Vector2 anchoredPosition
        {
            get => _anchoredPosition;
            set
            {
                if (ThrowOnAnchoredSet) throw new InvalidOperationException("anchored");
                _anchoredPosition = value;
                _suppressDerive = true;
                try { localPosition = new Vector3(value.x, value.y, 0f); }
                finally { _suppressDerive = false; }
            }
        }

        public Rect rect => new Rect(-pivot.x * _sizeDelta.x, -pivot.y * _sizeDelta.y, _sizeDelta.x, _sizeDelta.y);
    }

    public class Canvas : Behaviour
    {
        public RenderMode renderMode = RenderMode.ScreenSpaceOverlay;
        public Camera worldCamera;
        public float scaleFactor = 1f;
        public Rect pixelRect = new Rect(0f, 0f, 1280f, 720f);
        public Canvas rootCanvas => this;
    }

    public class Camera : Behaviour
    {
        public Rect pixelRect = new Rect(0f, 0f, 1280f, 720f);
    }

    /// <summary>真实 CanvasGroup 语义桩：在销毁对象上读写抛异常；写计数供"only-changed"核验。</summary>
    public class CanvasGroup : Behaviour
    {
        private float _alpha = 1f;
        private bool _blocksRaycasts = true;
        private bool _interactable = true;
        private bool _ignoreParentGroups;

        internal bool ThrowAlpha;
        internal bool ThrowBlocks;
        internal bool ThrowInteract;

        /// <summary>本实例 alpha 写计数（"foreign 组未被动过"按实例核验，避免全局计数被自有组污染）。</summary>
        internal int AlphaWrites;

        public float alpha
        {
            get { ThrowIfDestroyed(); return _alpha; }
            set
            {
                ThrowIfDestroyed();
                if (ThrowAlpha) throw new InvalidOperationException("alpha");
                _alpha = value;
                AlphaWrites++;
                EntryProbe.ProbeHooks.CanvasGroupAlphaWrites++;
            }
        }

        public bool blocksRaycasts
        {
            get { ThrowIfDestroyed(); return _blocksRaycasts; }
            set
            {
                ThrowIfDestroyed();
                if (ThrowBlocks) throw new InvalidOperationException("blocks");
                _blocksRaycasts = value;
                EntryProbe.ProbeHooks.CanvasGroupBlocksWrites++;
            }
        }

        public bool interactable
        {
            get { ThrowIfDestroyed(); return _interactable; }
            set
            {
                ThrowIfDestroyed();
                if (ThrowInteract) throw new InvalidOperationException("interact");
                _interactable = value;
            }
        }

        public bool ignoreParentGroups
        {
            get { ThrowIfDestroyed(); return _ignoreParentGroups; }
            set { ThrowIfDestroyed(); _ignoreParentGroups = value; }
        }

        internal void ThrowIfDestroyed()
        {
            if (Destroyed) throw new InvalidOperationException("CanvasGroup destroyed");
        }

        // probe 观测销毁对象的最终字段（真实 Unity 语义下属性访问会抛，这里只在探针断言里用）
        internal float RawAlphaForProbe => _alpha;
        internal bool RawBlocksForProbe => _blocksRaycasts;
    }

    public static class Screen
    {
        public static int width = 1280;
        public static int height = 720;
        public static Rect safeArea = new Rect(0f, 0f, 1280f, 720f);
    }

    public static class Time
    {
        public static int frameCount;
    }

    public static class Mathf
    {
        public static float Abs(float v) => Math.Abs(v);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }

    public static class RectTransformUtility
    {
        public static Vector2 WorldToScreenPoint(Camera cam, Vector3 worldPoint)
        {
            float ox = cam != null ? cam.pixelRect.x : 0f;
            float oy = cam != null ? cam.pixelRect.y : 0f;
            return new Vector2(worldPoint.x - ox, worldPoint.y - oy);
        }

        public static bool ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam,
            out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            if (rect == null) return false;
            float ox = cam != null ? cam.pixelRect.x : 0f;
            float oy = cam != null ? cam.pixelRect.y : 0f;
            Vector3 world = new Vector3(screenPoint.x + ox, screenPoint.y + oy, 0f);
            Vector3 local = rect.InverseTransformPoint(world);
            localPoint = new Vector2(local.x, local.y);
            return true;
        }
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public int handle;
        public bool isValid;
        public bool isLoaded;
        public Scene(int handle) { this.handle = handle; isValid = true; isLoaded = true; }
        public bool IsValid() => isValid;
    }
}

namespace UnityEngine.UI
{
    using Il2CppInterop.Runtime.InteropTypes.Arrays;

    public class Graphic : Behaviour
    {
        public bool raycastTarget = true;
        public RectTransform rectTransform => gameObject != null ? gameObject.GetComponent<RectTransform>() : null;
    }

    public class MaskableGraphic : Graphic { }

    public class Sprite : Object
    {
        public Texture2D texture;
        private Rect _rect;
        private Vector2 _pivot;
        private float _pixelsPerUnit = 100f;
        private Il2CppStructArray<Vector2> _vertices;
        private Il2CppStructArray<ushort> _triangles;

        /// <summary>读故障注入：metadata（rect/pivot/ppu）抛。</summary>
        public bool ThrowOnMetadata;
        /// <summary>读故障注入：mesh（vertices/triangles，polygon 打包 sprite 在真实 Unity 会抛）抛。</summary>
        public bool ThrowOnMeshes;

        /// <summary>Sprite.rect（像素）。</summary>
        public Rect rect
        {
            get
            {
                if (ThrowOnMetadata) throw new InvalidOperationException("sprite-rect");
                return _rect;
            }
            set => _rect = value;
        }

        /// <summary>Sprite.pivot（像素，rect 相对）；native 岛图实测例 (42, 0)。</summary>
        public Vector2 pivot
        {
            get
            {
                if (ThrowOnMetadata) throw new InvalidOperationException("sprite-pivot");
                return _pivot;
            }
            set => _pivot = value;
        }

        /// <summary>Sprite.bounds（sprite 单位）：GetDrawingDimensions/GenerateSprite 归一用真实 bounds.size；
        /// 默认等于 (rect/ppu, pivot 归一) 的 mesh 盒，可由测试按真实字段覆盖。</summary>
        public Bounds bounds
        {
            get
            {
                if (ThrowOnMetadata) throw new InvalidOperationException("sprite-bounds");
                if (_boundsOverride.HasValue) return _boundsOverride.Value;
                // 默认按"整 rect 紧贴 mesh"的 Unity 约定：尺寸 rect/ppu，中心 (rect/2 - pivot)/ppu（pivot 在原点）。
                float w = _rect.width / _pixelsPerUnit, h = _rect.height / _pixelsPerUnit;
                return new Bounds(
                    new Vector3((_rect.width * 0.5f - _pivot.x) / _pixelsPerUnit,
                                (_rect.height * 0.5f - _pivot.y) / _pixelsPerUnit, 0f),
                    new Vector3(w, h, 0f));
            }
            set => _boundsOverride = value;
        }

        private Bounds? _boundsOverride;

        /// <summary>Sprite.pixelsPerUnit（Sprite.vertices 的单位换算；native 岛图实测 32）。</summary>
        public float pixelsPerUnit
        {
            get
            {
                if (ThrowOnMetadata) throw new InvalidOperationException("sprite-ppu");
                return _pixelsPerUnit;
            }
            set => _pixelsPerUnit = value;
        }

        /// <summary>Sprite.vertices（pivot 相对的 sprite 单位：px = v*ppu + pivot）。</summary>
        public Il2CppStructArray<Vector2> vertices
        {
            get
            {
                if (ThrowOnMeshes) throw new InvalidOperationException("sprite-vertices");
                return _vertices;
            }
            set => _vertices = value;
        }

        /// <summary>Sprite.triangles（triangle list）。</summary>
        public Il2CppStructArray<ushort> triangles
        {
            get
            {
                if (ThrowOnMeshes) throw new InvalidOperationException("sprite-triangles");
                return _triangles;
            }
            set => _triangles = value;
        }

        public static bool FailNextCreate;
        public static Sprite Create(Texture2D tex, Rect rect, Vector2 pivot, float ppu, uint ext, SpriteMeshType type)
        {
            if (FailNextCreate) { FailNextCreate = false; return null; }
            if (tex == null) return null;
            // Unity Sprite.Create 默认 FullRect：4 顶点全矩形 quad（与真实运行时 sprite 一致）。
            float l = -pivot.x / ppu, b = -pivot.y / ppu;
            float r = l + rect.width / ppu, t = b + rect.height / ppu;
            var quad = new Il2CppStructArray<Vector2>(4);
            quad[0] = new Vector2(l, b);
            quad[1] = new Vector2(l, t);
            quad[2] = new Vector2(r, t);
            quad[3] = new Vector2(r, b);
            var tris = new Il2CppStructArray<ushort>(6);
            tris[0] = 0; tris[1] = 1; tris[2] = 2; tris[3] = 2; tris[4] = 3; tris[5] = 0;
            return new Sprite
            {
                texture = tex, rect = rect, pivot = pivot, pixelsPerUnit = ppu,
                vertices = quad, triangles = tris,
            };
        }
    }

    public class Image : MaskableGraphic
    {
        private Sprite _sprite;
        public bool ThrowOnSet;
        /// <summary>写后抛：字段已写入再抛（真实 setter 非原子语义）。</summary>
        public bool ThrowAfterSet;
        /// <summary>读故障：sprite getter 抛（租约读回/快照路径）。</summary>
        public bool ThrowOnGet;
        /// <summary>Image.preserveAspect（native 岛图默认 false；映射测试可置 true）。</summary>
        public bool preserveAspect;
        /// <summary>Image.type（真实 native 岛图 = Simple；测试可设 Tiled/Filled 证明 fail-closed）。</summary>
        public Type type = Type.Simple;
        /// <summary>Image.useSpriteMesh（exact 岛形读取的前提；false = quad 路径 → 必须整体保留 native）。</summary>
        public bool useSpriteMesh = true;
        /// <summary>Image.overrideSprite（uGUI activeSprite = overrideSprite ?? sprite）。</summary>
        public Sprite overrideSprite;
        /// <summary>Image.GetPixelAdjustedRect 的桩（默认整 rect；测试可设驱动尺寸项）。</summary>
        public Rect adjustedRect;
        public bool useAdjustedRect;
        private RectTransform _rtCache;
        public Rect GetPixelAdjustedRect()
        {
            if (ThrowOnGet) throw new InvalidOperationException("get-adjusted-rect");
            if (useAdjustedRect) return adjustedRect;
            RectTransform rt = rectTransform;
            if (rt == null) return new Rect(0f, 0f, 0f, 0f);
            return new Rect(rt.rect.x, rt.rect.y, rt.rect.width, rt.rect.height);
        }
        public enum Type { Simple = 0, Sliced = 1, Tiled = 2, Filled = 3 }
        public Sprite sprite
        {
            get
            {
                if (ThrowOnGet) throw new InvalidOperationException("get-sprite");
                return _sprite;
            }
            set
            {
                if (ThrowOnSet) throw new InvalidOperationException("set-sprite");
                _sprite = value;
                if (ThrowAfterSet) throw new InvalidOperationException("set-sprite-after");
            }
        }
    }

    public class Text : MaskableGraphic
    {
        public string text = string.Empty;
    }

    public class RectMask2D : Behaviour { }

    public class Selectable : Behaviour { }

    public class Button : Selectable { }
}

namespace Il2CppInterop.Runtime.InteropTypes
{
    using System;

    public class Il2CppObjectBase
    {
        private static long _next;
        public readonly IntPtr Pointer;

        public Il2CppObjectBase() { Pointer = new IntPtr(System.Threading.Interlocked.Increment(ref _next)); }
    }

    public static class Il2CppObjectBaseExtensions
    {
        public static T TryCast<T>(this Il2CppObjectBase self) where T : class => self as T;
    }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    using System;
    using Il2CppInterop.Runtime.InteropTypes;

    public class Il2CppReferenceArray<T> : Il2CppObjectBase where T : class
    {
        private readonly T[] _items;
        public Il2CppReferenceArray(int length) { _items = new T[length]; }
        public Il2CppReferenceArray(T[] items) { _items = items; }
        public int Length => _items.Length;
        public T this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }
    }

    public class Il2CppStructArray<T> : Il2CppObjectBase
    {
        private readonly T[] _items;
        public Il2CppStructArray(int length) { _items = new T[length]; }
        public Il2CppStructArray(T[] items) { _items = items; }
        public int Length => _items.Length;
        public T this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }
    }
}

namespace Il2CppSystem.Collections.Generic
{
    public class List<T>
    {
        internal bool ThrowCount;
        internal bool ThrowItem;
        private readonly System.Collections.Generic.List<T> _items = new System.Collections.Generic.List<T>();
        public int Count
        {
            get
            {
                if (ThrowCount) throw new InvalidOperationException("count");
                return _items.Count;
            }
        }
        public T this[int index]
        {
            get
            {
                if (ThrowItem) throw new InvalidOperationException("item");
                return _items[index];
            }
            set => _items[index] = value;
        }
        public void Add(T item) => _items.Add(item);
        public void Clear() => _items.Clear();
    }
}

namespace HarmonyLib
{
    using System;

    [AttributeUsage(AttributeTargets.Class)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type declaringType) { }
        public HarmonyPatch(Type declaringType, string methodName) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }
}

namespace BepInEx.Configuration
{
    public class ConfigEntry<T>
    {
        public T Value;
        public ConfigEntry(T value) { Value = value; }
    }
}

namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        public readonly List<string> Lines = new List<string>();
        public void LogInfo(string message) => Lines.Add("INFO " + message);
        public void LogWarning(string message) => Lines.Add("WARN " + message);
        public void LogError(string message) => Lines.Add("ERROR " + message);
    }
}
