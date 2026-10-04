// 测试用 stub：只提供 MapWidthPlanner.cs / MapWidthTerrain.cs / PatchWorld_Level.cs 触及的
// Unity/IL2CPP/Harmony/游戏表面。地形对象的组件组合/持久化路径/池戳按 template-boundary.md 的
// level4 实测 17 个对象形状搭建（TerrainFactory），不用“只有 Tile”的假模板充当正常样本。
// 仅测试程序集编译。
//
// 双模式：PC（默认，无 ANDROID 定义）游戏类型 stub 位于全局命名空间（与 PC interop 一致）；
// Android 别名模式（-p:DefineConstants=ANDROID）把 stub 声明进 Il2Cpp 命名空间，生产源码的
// `using X = Il2Cpp.X;` 头别名绑定到这里，测试自身的裸名引用由下面的 global using 解析。
// 两种模式编译并执行同一份生产源码与同一套用例，不复制任何生产算法。
#if ANDROID
global using Il2Cpp;
#endif

using System;
using System.Collections.Generic;

namespace HarmonyLib
{
    internal class HarmonyPatch : Attribute
    {
        internal readonly Type TargetType;
        internal readonly string MethodName;
        internal HarmonyPatch(Type type, string methodName) { TargetType = type; MethodName = methodName; }
    }

    internal class HarmonyPrefix : Attribute { }
    internal class HarmonyPostfix : Attribute { }
    internal class HarmonyFinalizer : Attribute { }
}

namespace Il2CppSystem.Collections.Generic
{
    /// <summary>IL2CPP interop 的 List&lt;T&gt;：生产源码按该类型声明/构造列表。</summary>
    internal class List<T> : System.Collections.Generic.List<T>
    {
    }
}

/// <summary>
/// 测试侧“原生类名”注册表：stub 组件用运行时类型名模拟 IL2CPP 类名，
/// 供生产代码 IL2CPP.il2cpp_class_get_name_(component.ObjectClass) 路径使用。
/// </summary>
internal static class NativeClassNames
{
    private static readonly Dictionary<Type, IntPtr> Handles = new Dictionary<Type, IntPtr>();
    private static readonly Dictionary<IntPtr, string> Names = new Dictionary<IntPtr, string>();
    private static int _next = 1;

    internal static IntPtr HandleFor(Type type)
    {
        if (!Handles.TryGetValue(type, out IntPtr handle))
        {
            handle = new IntPtr(_next++);
            Handles[type] = handle;
            Names[handle] = type.Name;
        }
        return handle;
    }

    internal static string NameOf(IntPtr handle)
    {
        return Names.TryGetValue(handle, out string name) ? name : null;
    }
}

namespace Il2CppInterop.Runtime
{
    internal static class IL2CPP
    {
        internal static string il2cpp_class_get_name_(IntPtr klass)
        {
            return NativeClassNames.NameOf(klass);
        }
    }
}

namespace UnityEngine
{
    internal class Object
    {
        public string name = "Object";
    }

    internal struct Vector3
    {
        public float x;
        public float y;
        public float z;

        internal Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    internal class Component : Object
    {
        internal GameObject gameObject;

        /// <summary>Unity 语义：组件名来自所在 GameObject。</summary>
        internal new string name
        {
            get { return gameObject == null ? base.name : gameObject.name; }
            set { base.name = value; }
        }

        internal Transform transform
        {
            get { return gameObject == null ? null : gameObject.transform; }
        }

        /// <summary>模拟 Il2CppObjectBase.ObjectClass（stub 用类型的名字注册表）。</summary>
        internal IntPtr ObjectClass
        {
            get { return NativeClassNames.HandleFor(GetType()); }
        }

        internal T TryCast<T>() where T : class
        {
            return this as T;
        }

        internal T GetComponent<T>() where T : class
        {
            return gameObject == null ? null : gameObject.GetComponent<T>();
        }
    }

    internal class Behaviour : Component { }
    internal class MonoBehaviour : Behaviour { }
    internal class SpriteRenderer : Component { }

    internal class GameObject : Object
    {
        internal Transform transform;
        internal bool activeSelf = true;

        private readonly List<Component> _components = new List<Component>();

        internal void Attach(Component component)
        {
            component.gameObject = this;
            if (!_components.Contains(component)) _components.Add(component);
        }

        internal T GetComponent<T>() where T : class
        {
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T typed) return typed;
            }
            return null;
        }

        internal T[] GetComponents<T>() where T : class
        {
            var found = new List<T>();
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T typed) found.Add(typed);
            }
            return found.ToArray();
        }
    }

    internal class Transform : Component
    {
        internal Vector3 localPosition;

        private readonly List<Transform> _children = new List<Transform>();

        internal int childCount
        {
            get { return _children.Count; }
        }

        internal Transform GetChild(int index)
        {
            return _children[index];
        }

        internal void AddChild(Transform child)
        {
            _children.Add(child);
        }
    }
}

// ---- 游戏类型（PC interop 在 Assembly-CSharp 全局命名空间；Android 别名模式在 Il2Cpp.*，
//      由文件顶部说明的 #if ANDROID namespace 包装切换）----
#if ANDROID
namespace Il2Cpp
{
#endif

internal enum ContentLayers
{
    BackgroundFarLayer,
    BackgroundNearLayer,
    GameLayer,
    ForegroundFarLayer,
    ForegroundNearLayer,
    BackdropFarLayer,
    BackdropNearLayer,
}

internal enum LevelBlockGroup
{
    None = 0,
    Forest = 3,
    Clearing = 5,
}

internal struct IntRange
{
    public int min;
    public int max;
}

internal class LevelBlock : UnityEngine.MonoBehaviour
{
    public LevelBlockGroup groupOne;
    public LevelBlockGroup groupTwo;
    public LevelBlockGroup groupThree;
    public bool absoluteCenter;
    public UnityEngine.GameObject cameraBlockMarker;
    public bool isDangerSource;

    public int Width;

    public int GetWidth()
    {
        return Width;
    }
}

internal class Tile : UnityEngine.MonoBehaviour
{
    public bool noGround;
    public float Left;
    public float Right;

    public float GetLeft() { return Left; }
    public float GetRight() { return Right; }
    public float GetWidth() { return Right - Left; }
}

internal class Grass : UnityEngine.MonoBehaviour { }

internal class RandomizeSprite : UnityEngine.MonoBehaviour { }

internal class BiomeSpriteSwapper : UnityEngine.MonoBehaviour { }

internal class FixedTransform : UnityEngine.MonoBehaviour { }

internal class GAPS : UnityEngine.MonoBehaviour { }

internal class Persistent : UnityEngine.MonoBehaviour
{
    public bool persistObject;
    public string path;
}

internal class PoolStamper : UnityEngine.MonoBehaviour
{
    internal enum Pool
    {
        Beggar,
        Grass,
        Ghost,
    }

    public Pool targetPool;
}

internal class LevelLayout
{
    // 本次布局的可用模板（原生 LevelLayout.blocks / availableBlocks）。
    public Il2CppSystem.Collections.Generic.List<LevelBlock> blocks = new Il2CppSystem.Collections.Generic.List<LevelBlock>();

    // 用于 TotalWidth()：只读记录，与 GetBlocks 返回列表无关。
    public readonly List<LevelBlock> LayoutBlocks = new List<LevelBlock>();

    public int TotalWidth()
    {
        int total = 0;
        for (int i = 0; i < LayoutBlocks.Count; i++) total += LayoutBlocks[i].GetWidth();
        return total;
    }
}

internal class Level : UnityEngine.MonoBehaviour
{
    public IntRange _levelEdges;
}

#if ANDROID
}
#endif

namespace KingdomEnhancedMod
{
    internal sealed class ConfigEntry<T>
    {
        internal T Value;
        internal ConfigEntry(T value) { Value = value; }
    }

    internal static class ModConfig
    {
        internal static ConfigEntry<bool> Enabled = new ConfigEntry<bool>(true);
        internal static ConfigEntry<float> MapSizeMultiplier = new ConfigEntry<float>(2f);
    }

    internal sealed class ManualLogSource
    {
        internal readonly List<string> Info = new List<string>();
        internal readonly List<string> Warnings = new List<string>();

        internal void LogInfo(string message) { Info.Add(message); }
        internal void LogWarning(string message) { Warnings.Add(message); }
        internal void LogError(string message) { Warnings.Add("ERROR " + message); }
    }

    internal sealed class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance;
        internal readonly ManualLogSource LogSource = new ManualLogSource();
    }

    /// <summary>测试侧的统一入口：安装 stub 插件单例并读写日志。</summary>
    internal static class TestLog
    {
        internal static void Install()
        {
            KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
        }

        internal static void Clear()
        {
            KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
        }

        internal static List<string> Info
        {
            get { return KingdomEnhancedPlugin.Instance.LogSource.Info; }
        }

        internal static List<string> Warnings
        {
            get { return KingdomEnhancedPlugin.Instance.LogSource.Warnings; }
        }

        internal static bool InfoContains(string fragment)
        {
            for (int i = 0; i < Info.Count; i++)
            {
                if (Info[i].IndexOf(fragment, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        internal static bool WarningContains(string fragment)
        {
            for (int i = 0; i < Warnings.Count; i++)
            {
                if (Warnings[i].IndexOf(fragment, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }
    }
}

/// <summary>
/// 按 2.4 静态取证（template-boundary.md）搭建测试用原生块：
/// Tile 对象 = Transform+SpriteRenderer+RandomizeSprite+Tile+Persistent+BiomeSpriteSwapper+FixedTransform；
/// Grass 对象额外为 Grass+GAPS+PoolStamper(Grass) 而无 Tile/RandomizeSprite。均无子孙。
/// </summary>
internal static class TerrainFactory
{
    internal const string TileForestPath = "Prefabs/Environment/Tile Forest";
    internal const string TileClearingPath = "Prefabs/Environment/Tile Clearing";
    internal const string GrassPath = "Prefabs/Vegetation/Grass";

    internal static UnityEngine.GameObject Go(string name, UnityEngine.Transform parent = null, bool active = true,
        UnityEngine.Vector3 position = default(UnityEngine.Vector3))
    {
        var go = new UnityEngine.GameObject { name = name, activeSelf = active };
        var transform = new UnityEngine.Transform { gameObject = go, localPosition = position };
        go.transform = transform;
        go.Attach(transform);
        if (parent != null) parent.AddChild(transform);
        return go;
    }

    internal static UnityEngine.GameObject TileObject(UnityEngine.Transform parent, string name, float left,
        float width, string path = TileForestPath)
    {
        var go = Go(name, parent, true, new UnityEngine.Vector3(left, 0f, 0f));
        go.Attach(new UnityEngine.SpriteRenderer());
        go.Attach(new RandomizeSprite());
        go.Attach(new Tile { Left = left, Right = left + width });
        go.Attach(new Persistent { persistObject = true, path = path });
        go.Attach(new BiomeSpriteSwapper());
        go.Attach(new FixedTransform());
        return go;
    }

    internal static UnityEngine.GameObject GrassObject(UnityEngine.Transform parent, float y = 1f)
    {
        var go = Go("Grass", parent, true, new UnityEngine.Vector3(0f, y, 0f));
        go.Attach(new UnityEngine.SpriteRenderer());
        go.Attach(new Grass());
        go.Attach(new GAPS());
        go.Attach(new Persistent { persistObject = true, path = GrassPath });
        go.Attach(new FixedTransform());
        go.Attach(new BiomeSpriteSwapper());
        go.Attach(new PoolStamper { targetPool = PoolStamper.Pool.Grass });
        return go;
    }

    /// <summary>普通模板：Forest_Blocks 无 Grass；Clearing* 带 Grass，Tile 以 [-width/2, width/2) 居中铺满。</summary>
    internal static LevelBlock Terrain(string name, int width, LevelBlockGroup group)
    {
        var root = Go(name);
        var block = new LevelBlock { Width = width, groupOne = group };
        root.Attach(block);

        var layer = Go("GameLayer", root.transform);
        bool withGrass = name != "Forest_Blocks";
        if (withGrass) GrassObject(layer.transform);
        int tileCount = width / 4;
        for (int i = 0; i < tileCount; i++)
        {
            float left = -width / 2f + i * 4f;
            TileObject(layer.transform, "Tile " + i, left, 4f);
        }
        return block;
    }

    /// <summary>非地形功能块（无 Tile）：不能成为候选，也不能被复制。</summary>
    internal static LevelBlock Functional(string name, int width, LevelBlockGroup group = LevelBlockGroup.None)
    {
        var root = Go(name);
        var block = new LevelBlock { Width = width, groupOne = group };
        root.Attach(block);
        var layer = Go("GameLayer", root.transform);
        var prop = Go("Prop", layer.transform);
        prop.Attach(new UnityEngine.MonoBehaviour());
        return block;
    }

    internal static Il2CppSystem.Collections.Generic.List<LevelBlock> List(params LevelBlock[] items)
    {
        var list = new Il2CppSystem.Collections.Generic.List<LevelBlock>();
        for (int i = 0; i < items.Length; i++) list.Add(items[i]);
        return list;
    }

    /// <summary>测试定位 helper：取模板层里的第一个 Tile 对象（不是生产校验逻辑的镜像）。</summary>
    internal static UnityEngine.GameObject FirstTileObject(LevelBlock block)
    {
        var root = block.transform;
        for (int i = 0; i < root.childCount; i++)
        {
            var layer = root.GetChild(i);
            for (int c = 0; c < layer.childCount; c++)
            {
                var child = layer.GetChild(c);
                if (child.gameObject.GetComponent<Tile>() != null) return child.gameObject;
            }
        }
        return null;
    }

    internal static UnityEngine.GameObject GrassObjectOf(LevelBlock block)
    {
        var root = block.transform;
        for (int i = 0; i < root.childCount; i++)
        {
            var layer = root.GetChild(i);
            for (int c = 0; c < layer.childCount; c++)
            {
                var child = layer.GetChild(c);
                if (child.gameObject.GetComponent<Grass>() != null) return child.gameObject;
            }
        }
        return null;
    }

    internal static void MoveTile(UnityEngine.GameObject tileObject, float left, float width)
    {
        var tile = tileObject.GetComponent<Tile>();
        tile.Left = left;
        tile.Right = left + width;
        tileObject.transform.localPosition = new UnityEngine.Vector3(left, 0f, 0f);
    }
}
