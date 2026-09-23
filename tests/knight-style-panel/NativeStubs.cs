// 测试用 native 边界 stub（knight-style-panel 套件）：只提供本套件编译的 5 个生产文件
// （KnightStylePanel / KnightIdentityRuntime / KnightIdentityArchive / KnightIdentityContext /
// KnightIdentityLoadSeed）触及的 Unity/IL2CPP/Harmony/BepInEx/game 表面，并 stub 面板的跨模块依赖
// （UnitScanCache / PopulationCounts / PatchRoles_KnightStyle / ImGuiCompat）以便在本套件里驱动 UI 会话逻辑。
// 生产源码不改写；本文件只在测试程序集里编译。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Il2CppSystem.Collections.Generic
{
    /// <summary>IL2CPP interop 的 List&lt;T&gt;：生产源码按该类型声明列表。</summary>
    internal class List<T> : System.Collections.Generic.List<T>
    {
    }
}

namespace UnityEngine
{
    /// <summary>UnityEngine.SceneManagement.Scene 的最小面（生产只比较 handle）。</summary>
    internal struct Scene
    {
        internal int handle;
    }

    internal class Object
    {
        internal IntPtr Pointer;
        internal string name = "Object";
        internal int InstanceId = 1;
        internal bool Destroyed;

        // 对齐真实 UnityEngine.Object.Destroy（面板嵌入预览加载失败的清理路径用）。
        public static void Destroy(Object obj)
        {
            if (!ReferenceEquals(obj, null)) obj.Destroyed = true;
        }

        public static bool operator ==(Object a, Object b)
        {
            bool aNull = ReferenceEquals(a, null) || a.Destroyed;
            bool bNull = ReferenceEquals(b, null) || b.Destroyed;
            if (aNull || bNull) return aNull && bNull;
            return ReferenceEquals(a, b);
        }

        public static bool operator !=(Object a, Object b)
        {
            return !(a == b);
        }

        public override bool Equals(object other)
        {
            return ReferenceEquals(this, other);
        }

        public override int GetHashCode()
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        }

        internal int GetInstanceID()
        {
            return Destroyed ? 0 : InstanceId;
        }

        internal void DestroyForTests()
        {
            Destroyed = true;
        }
    }

    /// <summary>UI 侧最小面：面板只创建 1x1 占位纹理与 24x24 预览（真实帧由 SetPreview 注入）。</summary>
    internal enum HideFlags
    {
        None = 0,
        HideAndDontSave = 61,
    }

    internal enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }
    internal enum TextureWrapMode { Repeat = 0, Clamp = 1 }
    internal enum TextureFormat { RGBA32 = 4 }
    internal static class ImageConversion
    {
        // 面板嵌入预览加载用；桩环境无真实解码器——返回 false 走占位回退。
        internal static bool LoadImage(Texture2D tex, byte[] data, bool markNonReadable) => false;
    }

    internal class Texture : Object
    {
        internal HideFlags hideFlags;
        internal FilterMode filterMode;
        internal TextureWrapMode wrapMode;
    }

    internal sealed class Texture2D : Texture
    {
        internal readonly int Width;
        internal readonly int Height;

        internal Texture2D(int width, int height)
        {
            Width = width;
            Height = height;
        }

        // 对齐真实 interop 的 4 参构造与 ImageConversion（面板嵌入预览加载路径用）。
        internal Texture2D(int width, int height, TextureFormat format, bool linear)
        {
            Width = width;
            Height = height;
        }

        internal void SetPixel(int x, int y, Color color)
        {
        }

        internal void Apply()
        {
        }
    }

    internal struct Rect
    {
        internal float x, y, width, height;

        internal Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }
    }

    internal struct Color
    {
        internal float r, g, b, a;

        internal Color(float r, float g, float b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            a = 1f;
        }

        internal Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }
    }

    internal sealed class GUIStyle
    {
    }

    internal sealed class GUIContent
    {
        internal static readonly GUIContent none = new GUIContent();
    }

    internal static class GUI
    {
        internal static bool enabled = true;
        internal static Color color = new Color(1f, 1f, 1f, 1f);

        internal static bool Button(Rect rect, string text, GUIStyle style)
        {
            return false; // 测试直接驱动 Refresh/Apply；按钮点击路径不在 stub 内模拟
        }

        internal static void Label(Rect rect, string text, GUIStyle style)
        {
        }

        internal static void Box(Rect rect, GUIContent content, GUIStyle style)
        {
        }

        // 对齐真实 interop 的 Box(Rect, string, GUIStyle) 重载（面板按 ModPanel 惯例传字符串）。
        internal static void Box(Rect rect, string text, GUIStyle style)
        {
        }
    }

    internal class Component : Object
    {
        internal GameObject gameObject;

        internal T GetComponent<T>() where T : Component
        {
            return gameObject != null ? gameObject.GetComponent<T>() : null;
        }
    }

    internal class MonoBehaviour : Component
    {
    }

    internal class Transform : Component
    {
        internal Transform parent;

        internal bool IsChildOf(Transform other)
        {
            if (other == null) return false;
            for (Transform current = this; current != null; current = current.parent)
            {
                if (ReferenceEquals(current, other)) return true;
            }
            return false;
        }
    }

    internal class GameObject : Object
    {
        internal bool activeInHierarchy = true;
        internal string tag = "Knight";
        internal Scene scene;
        internal Transform transform;
        private readonly Dictionary<Type, Component> _components = new Dictionary<Type, Component>();

        internal T GetComponent<T>() where T : Component
        {
            if (Destroyed) return null;
            return _components.TryGetValue(typeof(T), out Component component) ? (T)component : null;
        }

        internal T AddComponentForTests<T>(T component) where T : Component
        {
            component.gameObject = this;
            _components[typeof(T)] = component;
            return component;
        }

        internal bool CompareTag(string value)
        {
            if (Destroyed) throw new InvalidOperationException("destroyed game object");
            return string.Equals(tag, value, StringComparison.Ordinal);
        }
    }

    internal static class JsonUtility
    {
        internal static string ToJson(object value, bool prettyPrint)
        {
            if (value is KingdomEnhancedMod.IslandSaveData island) return island.Json();
            throw new NotSupportedException("stub JsonUtility: " + (value == null ? "null" : value.GetType().Name));
        }
    }

    internal static class Debug
    {
        internal static void LogError(string message)
        {
        }
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    internal sealed class HarmonyPatch : Attribute
    {
        internal HarmonyPatch(Type declaringType)
        {
        }

        internal HarmonyPatch(Type declaringType, string methodName)
        {
        }

        internal HarmonyPatch(Type declaringType, string methodName, Type[] argumentTypes)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class HarmonyPrefix : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class HarmonyPostfix : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class HarmonyFinalizer : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class HarmonyPriority : Attribute
    {
        internal readonly int Value;

        internal HarmonyPriority(int value)
        {
            Value = value;
        }
    }

    /// <summary>与游戏内 0Harmony 2.10.2 实测一致：First=800 / Normal=400 / Last=0，PriorityComparer 按数值降序。</summary>
    internal static class Priority
    {
        internal const int First = 800;
        internal const int Normal = 400;
        internal const int Last = 0;
    }
}

namespace BepInEx
{
    internal static class Paths
    {
        internal static string ConfigPath;
    }
}

namespace KingdomEnhancedMod
{
    internal sealed class LogSource
    {
        internal readonly List<string> Messages = new List<string>();

        internal void LogInfo(string message)
        {
            Messages.Add("INFO " + message);
        }

        internal void LogWarning(string message)
        {
            Messages.Add("WARN " + message);
        }

        internal void LogError(string message)
        {
            Messages.Add("ERROR " + message);
        }
    }

    internal sealed class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance;
        internal readonly LogSource LogSource = new LogSource();
    }

    internal class Persistent : UnityEngine.MonoBehaviour
    {
        internal string path;
    }

    /// <summary>原生 Knight 组件：与 Persistent 同 GameObject（Squire 也是 Knight 组件，靠 tag 区分）。</summary>
    internal class Knight : UnityEngine.MonoBehaviour
    {
        internal Damageable _damageable = new Damageable();
    }

    internal class Damageable : UnityEngine.MonoBehaviour
    {
        internal bool isDead;
    }

    /// <summary>Knight 序列化载荷（全局命名空间，FullName 即 "KnightData"）。</summary>
    internal class KnightData
    {
        internal int rank;
    }

    internal class World : UnityEngine.MonoBehaviour
    {
        internal UnityEngine.Transform gameLayer;
    }

    internal class Managers
    {
        internal static Managers Inst;
        internal World world;
    }

    internal static class NetworkBigBoss
    {
        internal static bool HasWorldAuth = true;
        internal static bool IsOnline;
    }

    internal class GlobalSaveData
    {
        internal static string filename;
        internal static GlobalSaveData loaded;
        internal int currentCampaign;
        internal int currentChallenge;
    }

    internal class CampaignSaveData
    {
        internal IntPtr Pointer = new IntPtr(0x666);
        internal void ApplyToScene() { }
        internal static CampaignSaveData current;
        internal IslandSaveData CurrentIsland;
    }

    /// <summary>原生岛存档 stub：字段名与 2.4 interop 一致（objects/land/realStartDateTime/static 状态）。</summary>
    internal class IslandSaveData
    {
        private static long nextIslandPointer;
        internal IntPtr Pointer { get; } = new IntPtr(System.Threading.Interlocked.Increment(ref nextIslandPointer));

        internal class ObjectData
        {
            private static long nextPointer;
            internal IntPtr Pointer { get; } = new IntPtr(System.Threading.Interlocked.Increment(ref nextPointer));

            internal class ComponentData
            {
                internal string name;
                internal string type;
                internal string data;
            }

            internal string name;
            internal string uniqueID;
            internal string prefabPath;
            internal Il2CppSystem.Collections.Generic.List<ComponentData> componentData2 =
                new Il2CppSystem.Collections.Generic.List<ComponentData>();

            internal void WriteJson(StringBuilder builder)
            {
                builder.Append("{\"name\":\"").Append(name).Append("\",\"uniqueID\":\"").Append(uniqueID).Append("\",\"components\":[");
                for (int i = 0; i < componentData2.Count; i++)
                {
                    if (i > 0) builder.Append(',');
                    ComponentData component = componentData2[i];
                    builder.Append("{\"name\":\"").Append(component.name).Append("\",\"type\":\"").Append(component.type)
                        .Append("\",\"data\":").Append(System.Text.Json.JsonSerializer.Serialize(component.data)).Append("}");
                }
                builder.Append("]}");
            }
        }

        internal int land;
        internal bool isNew;
        internal DateTime realStartDateTime = new DateTime(638000000000000000L, DateTimeKind.Utc);
        internal double playTimeDays;
        internal double lastPlayedTimeDays;
        internal double islandTimePlayed; // 原生私有 _islandTimePlayed
        internal int lastPlayedReign = -1;
        internal int biome;
        internal Il2CppSystem.Collections.Generic.List<ObjectData> objects;

        internal static IslandSaveData CurrentlySavingIsland;
        internal static IslandSaveData _currentlySavingIsland;
        internal static bool poppingObjectsToScene;
        internal static bool isSavingGame;
        internal static readonly Dictionary<Persistent, string> idsByObject = new Dictionary<Persistent, string>();
        internal static readonly Dictionary<string, Persistent> objectsByID = new Dictionary<string, Persistent>();

        /// <summary>原生 GetID：name-instanceID，并登记两张映射表。</summary>
        internal static string GetID(Persistent forObject)
        {
            if (idsByObject.TryGetValue(forObject, out string cached)) return cached;
            string text = forObject.name + "-" + forObject.gameObject.GetInstanceID().ToString(CultureInfo.InvariantCulture);
            objectsByID[text] = forObject;
            idsByObject[forObject] = text;
            return text;
        }

        /// <summary>原生静态入口存在性（补丁属性用 nameof 引用；测试不直接调用）。</summary>
        internal static void Save(int campaign, int land, int challenge)
        {
            throw new NotSupportedException("stub: 用 NativeSim.RunSave 驱动 bridge");
        }

        internal bool TryPopObjectsToScene()
        {
            throw new NotSupportedException("stub: 用 NativeSim.RunLoad 驱动 bridge");
        }

        internal static Persistent TryCreateOrFind(ObjectData objectData)
        {
            throw new NotSupportedException("stub: 用 NativeSim.RunLoad 驱动 bridge");
        }

        /// <summary>
        /// 只序列化原生 JsonUtility 实际持久化的字段（实测 2.4：realStartDateTime 不在岛 JSON 里，
        /// 每次读取都会重建；playTimeDays/lastPlayedTimeDays/_islandTimePlayed 是三个活时钟）。
        /// </summary>
        internal string Json()
        {
            StringBuilder builder = new StringBuilder(256);
            builder.Append("{\"playTimeDays\":").Append(playTimeDays.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"lastPlayedTimeDays\":").Append(lastPlayedTimeDays.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"_islandTimePlayed\":").Append(islandTimePlayed.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"lastPlayedReign\":").Append(lastPlayedReign.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"biome\":").Append(biome.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"land\":").Append(land.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"objects\":[");
            if (objects != null)
            {
                for (int i = 0; i < objects.Count; i++)
                {
                    if (i > 0) builder.Append(',');
                    objects[i].WriteJson(builder);
                }
            }
            builder.Append("]}");
            return builder.ToString();
        }
    }

    /// <summary>测试用骑士单位：Persistent+Knight 同 GameObject，默认挂在当前 world.gameLayer 下。</summary>
    internal sealed class KnightUnit
    {
        internal readonly UnityEngine.GameObject Go;
        internal readonly Persistent Persistent;
        internal readonly Knight Knight;
        internal readonly int InstanceId;

        internal KnightUnit(int instanceId, string name, string tag, IntPtr pointer, bool inWorld)
        {
            InstanceId = instanceId;
            Go = new UnityEngine.GameObject
            {
                name = name,
                tag = tag,
                InstanceId = instanceId,
                Pointer = pointer,
                transform = new UnityEngine.Transform { name = name },
            };
            Persistent = Go.AddComponentForTests(new Persistent { name = name, InstanceId = instanceId, Pointer = new IntPtr(instanceId * 64 + 32) });
            Knight = Go.AddComponentForTests(new Knight { name = "Knight", InstanceId = instanceId, Pointer = new IntPtr(instanceId * 64 + 48) });

            NativeSim.PlaceUnit(this, inWorld);
        }

        internal bool Destroyed
        {
            get { return Go.Destroyed; }
        }

        internal void DestroyForTests()
        {
            Go.DestroyForTests();
            Knight.DestroyForTests();
            Persistent.DestroyForTests();
        }

        internal IslandSaveData.ObjectData ToRecord()
        {
            IslandSaveData.ObjectData record = new IslandSaveData.ObjectData
            {
                name = Go.name,
                uniqueID = IslandSaveData.GetID(Persistent),
            };
            record.componentData2.Add(new IslandSaveData.ObjectData.ComponentData
            {
                name = "Knight",
                type = "KnightData", // 原生 GetType().FullName（全局命名空间）
                data = "{\"rank\":1}",
            });
            return record;
        }
    }

    internal static class NativeSim
    {
        internal static KnightUnit NewKnight(int instanceId, string tag = "Knight", string name = "Knight(Clone)")
        {
            return new KnightUnit(instanceId, name, tag, new IntPtr(instanceId * 64 + 16), true);
        }

        internal static KnightUnit NewDetachedKnight(int instanceId, string tag = "Knight")
        {
            return new KnightUnit(instanceId, "Knight(Clone)", tag, new IntPtr(instanceId * 64 + 16), false);
        }

        /// <summary>把单位放到当前 world 的 gameLayer 下并同步 scene（模拟层级在加载后补齐）。</summary>
        internal static void PlaceUnit(KnightUnit unit, bool inWorld)
        {
            UnityEngine.Transform layer = null;
            if (Managers.Inst != null && Managers.Inst.world != null)
            {
                layer = Managers.Inst.world.gameLayer;
                unit.Go.scene = Managers.Inst.world.gameObject.scene;
                if (layer == null)
                {
                    layer = new UnityEngine.Transform { name = "gameLayer", gameObject = Managers.Inst.world.gameObject };
                    Managers.Inst.world.gameLayer = layer;
                }
            }
            unit.Go.transform.parent = inWorld ? layer : null;
        }

        /// <summary>新建 world（独立 scene handle + gameLayer）。</summary>
        internal static void ResetWorld(int sceneHandle)
        {
            UnityEngine.GameObject worldGo = new UnityEngine.GameObject
            {
                name = "World",
                InstanceId = sceneHandle,
                Pointer = new IntPtr(sceneHandle),
            };
            worldGo.scene = new UnityEngine.Scene { handle = sceneHandle };
            UnityEngine.Transform worldRoot = new UnityEngine.Transform { name = "World", gameObject = worldGo };
            worldGo.transform = worldRoot;
            UnityEngine.Transform layer = new UnityEngine.Transform { name = "gameLayer", gameObject = worldGo, parent = worldRoot, Pointer = new IntPtr(sceneHandle + 1) };
            Managers.Inst = new Managers
            {
                world = new World { name = "World", gameObject = worldGo, gameLayer = layer, Pointer = new IntPtr(sceneHandle) },
            };
        }

        internal static void ResetAll()
        {
            KnightIdentityRuntime.ResetForTests();
            KnightStylePanel.ResetForTests();
            PatchRoles_KnightStyle.ResetForTests();
            UnitScanCache.ClearForTests();
            IslandSaveData.CurrentlySavingIsland = null;
            IslandSaveData._currentlySavingIsland = null;
            IslandSaveData.poppingObjectsToScene = false;
            IslandSaveData.isSavingGame = false;
            IslandSaveData.idsByObject.Clear();
            IslandSaveData.objectsByID.Clear();
            CampaignSaveData.current = null;
            GlobalSaveData.filename = null;
            GlobalSaveData.loaded = null;
            NetworkBigBoss.HasWorldAuth = true;
            NetworkBigBoss.IsOnline = false;
            KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
            ResetWorld(0x5151);
        }

        /// <summary>
        /// 模拟原生 IslandSaveData.Save(campaign, land, challenge) 的真实次序：
        /// Prefix(空 scope，CurrentlySavingIsland 仍为 null) → 主体设置岛/isSavingGame → 逐对象 GetID（后缀捕获）
        /// → finally 清岛 → Priority.Last 后缀写 sidecar → Finalizer。
        /// </summary>
        internal static void RunSave(IslandSaveData target, int campaign, int land, int challenge, IList<KnightUnit> units)
        {
            KnightIdentitySaveBridge.SaveCapture state = KnightIdentitySaveBridge.BeginCapture(campaign, land, challenge);
            try
            {
                IslandSaveData.isSavingGame = true;
                IslandSaveData.CurrentlySavingIsland = target;
                IslandSaveData._currentlySavingIsland = target;
                IslandSaveData.objectsByID.Clear();
                IslandSaveData.idsByObject.Clear();
                target.objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>();
                for (int i = 0; i < units.Count; i++)
                {
                    IslandSaveData.ObjectData record = units[i].ToRecord();
                    target.objects.Add(record);
                    KnightIdentitySaveBridge.HandleGetId(units[i].Persistent, record.uniqueID); // GetID 后缀
                }
            }
            finally
            {
                IslandSaveData.objectsByID.Clear();
                IslandSaveData.idsByObject.Clear();
                IslandSaveData.isSavingGame = false;
                IslandSaveData.CurrentlySavingIsland = null;
                IslandSaveData._currentlySavingIsland = null;
            }
            KnightIdentitySaveBridge.ApplyCapture(state); // Priority.Last 后缀
            KnightIdentitySaveBridge.EndCapture(null, state);
        }

        /// <summary>模拟原生 TryPopObjectsToScene：Prefix 开作用域，逐记录 Instantiate（OnEnable）+ TryCreateOrFind 后缀。</summary>
        internal static void RunLoad(IslandSaveData island, Func<int, string, KnightUnit> spawn)
        {
            KnightIdentityLoadBridge.LoadScope scope = KnightIdentityLoadBridge.Begin(island);
            try
            {
                for (int i = 0; i < island.objects.Count; i++)
                {
                    IslandSaveData.ObjectData record = island.objects[i];
                    KnightUnit unit = spawn(i, record.uniqueID);
                    KnightIdentityRuntime.OnEnable(unit.Knight); // Knight.OnEnable 后缀
                    KnightIdentityLoadBridge.HandleTryCreateOrFind(record, unit.Persistent); // TryCreateOrFind 后缀
                }
            }
            finally
            {
                KnightIdentityLoadBridge.End(null, scope);
            }
        }

        /// <summary>按内容深拷贝记录，模拟「从磁盘反序列化出同内容的岛」。</summary>
        internal static IslandSaveData CloneIsland(IslandSaveData source)
        {
            IslandSaveData clone = new IslandSaveData
            {
                land = source.land,
                isNew = source.isNew,
                realStartDateTime = source.realStartDateTime,
                playTimeDays = source.playTimeDays,
                lastPlayedTimeDays = source.lastPlayedTimeDays,
                islandTimePlayed = source.islandTimePlayed,
                lastPlayedReign = source.lastPlayedReign,
                biome = source.biome,
                objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>(),
            };
            for (int i = 0; i < source.objects.Count; i++)
            {
                IslandSaveData.ObjectData record = source.objects[i];
                IslandSaveData.ObjectData copy = new IslandSaveData.ObjectData
                {
                    name = record.name,
                    uniqueID = record.uniqueID,
                    prefabPath = record.prefabPath,
                };
                for (int c = 0; c < record.componentData2.Count; c++)
                {
                    IslandSaveData.ObjectData.ComponentData component = record.componentData2[c];
                    copy.componentData2.Add(new IslandSaveData.ObjectData.ComponentData
                    {
                        name = component.name,
                        type = component.type,
                        data = component.data,
                    });
                }
                clone.objects.Add(copy);
            }
            return clone;
        }
    }

    // ------------------------------------------------------------------ 面板跨模块依赖 stub

    /// <summary>共享扫描 stub：测试直接设置本拍骑士名单（面板只用 GetKnights(0f) 的返回值）。</summary>
    internal static class UnitScanCache
    {
        internal static Knight[] TestKnights = Array.Empty<Knight>();

        internal static Knight[] GetKnights(float maxAgeSec = 3f)
        {
            return TestKnights;
        }

        internal static void ClearForTests()
        {
            TestKnights = Array.Empty<Knight>();
        }
    }

    /// <summary>人数缓存 stub：面板只读 Knights 用于 HUD 口径脚注。</summary>
    internal static class PopulationCounts
    {
        internal static int Knights;
    }

    /// <summary>
    /// 风格模块 stub：面板只经 HasStylePool/ApplyPanelRestyle 两个接入点（与生产同形）。
    /// ApplyPanelRestyle 记录调用并把收据风格记入 live（模拟 ApplyKnightStyle 的表现结果），
    /// 供「自动分配后无人再显示待识别」的契约检查。
    /// </summary>
    internal static class PatchRoles_KnightStyle
    {
        internal static bool PoolReady = true;
        internal static readonly List<Knight> Restyled = new List<Knight>();
        private static readonly Dictionary<int, int> LiveStyles = new Dictionary<int, int>();

        internal static bool HasStylePool()
        {
            return PoolReady;
        }

        internal static void ApplyPanelRestyle(Knight knight)
        {
            if (knight == null) return;
            Restyled.Add(knight);
            if (KnightIdentityRuntime.TryGetReceipt(knight, out KnightIdentityReceipt receipt))
                LiveStyles[knight.gameObject.GetInstanceID()] = receipt.Style;
        }

        /// <summary>与生产 PatchRoles_KnightStyle.TryGetResolvedStyleIndex 同口径：有在场景格的骑士。</summary>
        internal static bool TryGetResolvedStyleIndex(Knight knight, out int styleIndex)
        {
            styleIndex = -1;
            if (knight == null) return false;
            return LiveStyles.TryGetValue(knight.gameObject.GetInstanceID(), out styleIndex);
        }

        /// <summary>尚无在场景格的骑士数（= HUD 的待识别口径，用于契约检查）。</summary>
        internal static int Unknown(IList<KnightUnit> units)
        {
            int unknown = 0;
            for (int i = 0; i < units.Count; i++)
            {
                if (!TryGetResolvedStyleIndex(units[i].Knight, out _)) unknown++;
            }
            return unknown;
        }

        internal static void ResetForTests()
        {
            PoolReady = true;
            Restyled.Clear();
            LiveStyles.Clear();
        }
    }

    /// <summary>GUI 纹理绘制 stub（生产 ImGuiCompat 在游戏内以 GUI.Box 背景绘制纹理）。</summary>
    internal static class ImGuiCompat
    {
        internal static void DrawTexture(UnityEngine.Rect rect, UnityEngine.Texture2D texture)
        {
        }
    }
}
