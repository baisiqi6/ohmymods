// Minimal UnityEngine doubles for the bank-assistant atlas suite.
// They model only the API surface BankAssistantAtlasVisuals.cs / BankAssistantAtlasMetadata.cs /
// CharacterLeisureClock.cs touch: no scene scans, no prefab clones, no rendering.
// Counters exist to prove the visual layer writes nothing but the sprite (no enabled, no
// transform, no animator writes) and allocates nothing per frame. Passing these tests does not
// establish IL2CPP compatibility or real rendering; the HTML preview and the in-game pass own that.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEngine
{
    // Unity 原生对象生命周期的最小模型：
    // * 托管引用存在（C# 变量/数组项非 null）与原生对象存活（nativeAlive）分离；
    // * ==/!= 复刻 Unity 运算符语义：原生已销毁的代理与 C# null 判等（现场 "frame
    //   unavailable" 与该语义一致：缓存数组项仍在、原生对象已不可用；确切回收者未验证）；
    // * Resources.UnloadUnusedAssets 只放过带 DontUnloadUnusedAsset 位的对象（模型契约，
    //   不是对实机卸载扫描的测量）。
    public enum HideFlags
    {
        None = 0,
        HideInHierarchy = 1,
        HideInInspector = 2,
        DontSaveInEditor = 4,
        NotEditable = 8,
        DontSaveInBuild = 16,
        DontUnloadUnusedAsset = 32,
        DontSave = 52,
        HideAndDontSave = 61,
    }

    public class Object
    {
        public string name;
        public static int DestroyCalls;
        public bool nativeAlive = true;
        public HideFlags hideFlags = HideFlags.None;

        public static void Destroy(Object target)
        {
            if (target == null) return;   // 已销毁或 C# null：不重复计数
            DestroyCalls++;
            target.nativeAlive = false;
            if (target is GameObject gameObject)
            {
                gameObject.SetActive(false);
                foreach (var component in gameObject.Components.ToArray()) component.OnDestroyedHook();
            }
            else
            {
                target.OnDestroyedHook();
            }
        }

        // Unity 运算符语义：任一已销毁/为 null 时按 null 判等；两死相等、一死不等。
        public static bool operator ==(Object a, Object b)
        {
            bool aDead = a is null || !a.nativeAlive;
            bool bDead = b is null || !b.nativeAlive;
            if (aDead || bDead) return aDead && bDead;
            return ReferenceEquals(a, b);
        }

        public static bool operator !=(Object a, Object b) => !(a == b);

        public override bool Equals(object obj) => obj is Object other && this == other;
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

        protected virtual void OnDestroyedHook() { }

        public static void DontDestroyOnLoad(Object target) { }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
    }

    public class Behaviour : Component
    {
        public static int EnabledWrites;
        private bool _enabled = true;

        public bool enabled
        {
            get => _enabled;
            set
            {
                EnabledWrites++;
                _enabled = value;
            }
        }
    }

    public class GameObject : Object
    {
        public static int InstanceIdSeq = 1000;
        public static int AddComponentCalls;

        public readonly List<Component> Components = new();
        public readonly int InstanceId = ++InstanceIdSeq;
        public int InstanceIdOverride;
        public IntPtr Pointer;
        public Transform transform;
        private bool active = true;

        public GameObject(string name = "object")
        {
            this.name = name;
            Pointer = new IntPtr(InstanceId * 16);
            transform = new Transform { gameObject = this, name = "transform" };
            Components.Add(transform);
        }

        public bool activeSelf => active;
        public bool activeInHierarchy => active && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public void SetActive(bool value) => active = value;
        public int GetInstanceID() => InstanceIdOverride != 0 ? InstanceIdOverride : InstanceId;

        public T AddComponent<T>() where T : Component, new()
        {
            AddComponentCalls++;
            var component = new T { gameObject = this };
            Components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault();

        public static void ResetCounters()
        {
            InstanceIdSeq = 1000;
            AddComponentCalls = 0;
        }
    }

    public class Transform : Component
    {
        public static int PositionWrites, ScaleWrites;
        public Transform parent;
        public readonly List<Transform> Children = new();
        private Vector3 _localPosition;
        private Vector3 _localScale = Vector3.one;

        public Vector3 localPosition
        {
            get => _localPosition;
            set { PositionWrites++; _localPosition = value; }
        }

        public Vector3 localScale
        {
            get => _localScale;
            set { ScaleWrites++; _localScale = value; }
        }

        /// <summary>Read-only composed scale (production API the gait stride reads each frame).</summary>
        public Vector3 lossyScale
        {
            get
            {
                Vector3 s = _localScale;
                if (parent != null)
                {
                    Vector3 p = parent.lossyScale;
                    s = new Vector3(s.x * p.x, s.y * p.y, s.z * p.z);
                }
                return s;
            }
        }

        public Vector3 position
        {
            get => parent == null ? _localPosition : parent.position + _localPosition;
            set { PositionWrites++; _localPosition = parent == null ? value : value - parent.position; }
        }

        public void SetParent(Transform value, bool worldPositionStays)
        {
            if (parent != null) parent.Children.Remove(this);
            parent = value;
            if (parent != null) parent.Children.Add(this);
        }

        public static void ResetCounters() => PositionWrites = ScaleWrites = 0;
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y = 0f, float z = 0f) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public enum TextureFormat { RGBA32 = 4 }
    public enum FilterMode { Point = 0, Bilinear = 1 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1 }
    public enum SpriteMeshType { FullRect = 0, Tight = 1 }

    public static class Time
    {
        public static float deltaTime = 0.1f;
        public static float timeScale = 1f;
    }

    public class Texture2D : Object
    {
        public static int CreatedCount, DestroyedCount;
        public static readonly List<Texture2D> Created = new();
        public static byte FillAlpha;

        public int width, height;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public int anisoLevel;

        public Texture2D(int width, int height, TextureFormat format, bool mipChain)
        {
            this.width = width;
            this.height = height;
            CreatedCount++;
            Created.Add(this);
        }

        public Color32[] GetPixels32()
        {
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, FillAlpha);
            return pixels;
        }

        protected override void OnDestroyedHook() => DestroyedCount++;

        public static void ResetCounters()
        {
            CreatedCount = DestroyedCount = 0;
            Created.Clear();
        }
    }

    /// <summary>
    /// 未使用资源回收的测试模型：凡是无 DontUnloadUnusedAsset 保留位的 Texture2D / Sprite
    /// 一律销毁，返回本次销毁数。不模拟场景引用保护——本模型只检验"卸载扫描需按原生保留位
    /// 放过模块自有资源"的契约，不是对实机卸载者或托管根的测量（均未验证）。显式
    /// Object.Destroy 不受保留位影响。
    /// </summary>
    public static class Resources
    {
        public static int UnloadUnusedAssets()
        {
            int destroyed = 0;
            foreach (Texture2D texture in Texture2D.Created)
            {
                if (texture is null || !texture.nativeAlive) continue;
                if ((texture.hideFlags & HideFlags.DontUnloadUnusedAsset) != 0) continue;
                Object.Destroy(texture);
                destroyed++;
            }
            foreach (Sprite sprite in Sprite.Created)
            {
                if (sprite is null || !sprite.nativeAlive) continue;
                if ((sprite.hideFlags & HideFlags.DontUnloadUnusedAsset) != 0) continue;
                Object.Destroy(sprite);
                destroyed++;
            }
            return destroyed;
        }
    }

    public static class ImageConversion
    {
        public static bool Result;
        public static int SimulatedWidth = 1774, SimulatedHeight = 887;

        public static bool LoadImage(Texture2D texture, byte[] data, bool markNonReadable)
        {
            if (!Result) return false;
            texture.width = SimulatedWidth;
            texture.height = SimulatedHeight;
            return true;
        }

        public static void Reset()
        {
            Result = false;
            SimulatedWidth = 1774;
            SimulatedHeight = 887;
        }
    }

    public class Sprite : Object
    {
        public static int CreateCalls;
        public static int DestroyedCount;
        public static int ThrowAtCreateIndex = -1;

        public Texture2D texture;
        public Rect rect;
        public Vector2 pivot;
        public float pixelsPerUnit;
        public static List<Sprite> Created = new();

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit,
            uint extrude, SpriteMeshType meshType)
        {
            CreateCalls++;
            if (CreateCalls == ThrowAtCreateIndex)
                throw new InvalidOperationException("injected Sprite.Create failure");
            if (texture == null) return null;
            var sprite = new Sprite
            {
                texture = texture,
                rect = rect,
                pivot = pivot,
                pixelsPerUnit = pixelsPerUnit,
            };
            Created.Add(sprite);
            return sprite;
        }

        protected override void OnDestroyedHook() => DestroyedCount++;

        public static void ResetCounters()
        {
            CreateCalls = 0;
            DestroyedCount = 0;
            ThrowAtCreateIndex = -1;
            Created = new List<Sprite>();
        }
    }

    public class SpriteRenderer : Behaviour
    {
        public static int SpriteWrites;
        private Sprite _sprite;

        public Sprite sprite
        {
            get => _sprite;
            set
            {
                SpriteWrites++;
                _sprite = value;
            }
        }

        public static void ResetCounters() => SpriteWrites = 0;
    }

    public struct AnimatorStateInfo
    {
        public int shortNameHash;
        public float normalizedTime;
        public float length;
    }

    public class Animator : Behaviour
    {
        public static int SetFloatCalls, PlayCalls;
        /// <summary>Test seam: force GetFloat to throw so the gait read-failure path runs through
        /// the production outer Tick catch (no dedicated inner handler).</summary>
        public static bool GetFloatThrows;
        public AnimatorStateInfo State;
        public float Speed = float.NaN;

        public static int StringToHash(string name)
        {
            if (name == null) return 0;
            int hash = 17;
            for (int i = 0; i < name.Length; i++) hash = unchecked(hash * 31 + name[i]);
            return hash == 0 ? 1 : hash;
        }

        public float GetFloat(int id)
        {
            if (GetFloatThrows) throw new InvalidOperationException("injected GetFloat failure");
            return Speed;
        }

        public void SetFloat(int id, float value)
        {
            SetFloatCalls++;
            Speed = value;
        }

        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer) => State;

        public static void ResetCounters()
        {
            SetFloatCalls = PlayCalls = 0;
            GetFloatThrows = false;
        }
    }
}

// World/scope/clock surface used by BankAssistantAtlasVisuals (global namespace, like the game assembly).
public static class IslandSaveData
{
    public static bool isSavingGame;
}

public sealed class Game
{
    public enum State { Menu = 0, Playing = 1, NetworkClientPlaying = 2 }
    public State state = State.Playing;
}

/// <summary>Network authority double: production reads HasWorldAuth to pick the gait speed source
/// (authority Speed is game-sec; client Speed is |dx|/unscaledElapsed written by the PositionSync patch).</summary>
public static class NetworkBigBoss
{
    public static bool HasWorldAuth = true;
    public static bool IsOnline;
}

public sealed class Managers
{
    public static Managers Inst;
    public Game game;
}

public sealed class ManualLogSource
{
    public static readonly List<string> Warnings = new();
    public static readonly List<string> Infos = new();

    public void LogWarning(string message) { Warnings.Add(message ?? string.Empty); }
    public void LogInfo(string message) { Infos.Add(message ?? string.Empty); }

    public static void Reset()
    {
        Warnings.Clear();
        Infos.Clear();
    }
}

public sealed class KingdomEnhancedPlugin
{
    public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
    public ManualLogSource LogSource = new ManualLogSource();
}

namespace KingdomEnhancedMod
{
    /// <summary>只读 scope doubles：当前希腊作用域 + 当前 layer/场景/活动判定（层内布尔由测试设置）。</summary>
    public static class GreekBankScope
    {
        public static bool IsActive = true;
        public static bool LayerOk = true;

        public static bool IsInCurrentLayer(GameObject gameObject)
            => gameObject != null && gameObject.activeInHierarchy && LayerOk;
    }

    /// <summary>Production teleport-waiting predicate double (8 assistant slots): the gait clock
    /// freezes phase while a slot is in its hidden reveal window.</summary>
    public static class BankAssistantTeleportVisuals
    {
        public static readonly bool[] Waiting = new bool[8];

        public static bool IsWaiting(int index)
            => index >= 0 && index < Waiting.Length && Waiting[index];

        public static void ResetWaiting()
        {
            for (int i = 0; i < Waiting.Length; i++) Waiting[i] = false;
        }
    }
}
