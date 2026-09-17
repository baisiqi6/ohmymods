// 测试用 native 边界 stub：只提供 KnightIdentityRuntime.cs 触及的 Unity/IL2CPP/Harmony/BepInEx/game 表面，
// 并模拟原生 IslandSaveData 的 Save/GetID/TryPopObjectsToScene/TryCreateOrFind 调用次序与 world/scene 归属。
// 本文件复制自 tests/knight-identity-runtime/NativeStubs.cs（生产源码不改写），仅一处差异：
// IslandSaveData.ObjectData 增加模拟 Pointer 字段——真实 interop 里 ObjectData 继承 Il2CppObjectBase 带 Pointer，
// 而 KnightIdentityLoadSeed 必须核对记录的 native 指针。本文件只在测试程序集里编译。

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

    internal class Component : Object
    {
        private GameObject _gameObject;

        /// <summary>仅测试：模拟 interop 读异常（读异常不得被当成确证销毁）。</summary>
        internal bool ThrowOnGameObjectReadForTests;

        internal GameObject gameObject
        {
            get
            {
                if (ThrowOnGameObjectReadForTests) throw new InvalidOperationException("simulated interop gameObject read failure");
                return _gameObject;
            }
            set { _gameObject = value; }
        }

        internal T GetComponent<T>() where T : Component
        {
            return _gameObject != null ? _gameObject.GetComponent<T>() : null;
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
            internal IntPtr Pointer { get; private set; } = new IntPtr(System.Threading.Interlocked.Increment(ref nextPointer));

            /// <summary>仅测试：指定/清零 native 指针（真实 interop 里 Pointer 是 Il2CppObjectBase 的 native 指针）。</summary>
            internal void SetPointerForTests(long pointer)
            {
                Pointer = new IntPtr(pointer);
            }
            internal class ComponentData
            {
                internal string name;
                internal string type;
                internal string data;
            }

            internal string name;
            internal string uniqueID;
            internal string prefabPath;
            private Il2CppSystem.Collections.Generic.List<ComponentData> _componentData2 =
                new Il2CppSystem.Collections.Generic.List<ComponentData>();

            /// <summary>仅测试：模拟 interop 读异常（未知记录必须整批拒绝，不能当作非 Knight 忽略）。</summary>
            internal bool ThrowOnComponentsReadForTests;

            internal Il2CppSystem.Collections.Generic.List<ComponentData> componentData2
            {
                get
                {
                    if (ThrowOnComponentsReadForTests) throw new InvalidOperationException("simulated interop component read failure");
                    return _componentData2;
                }
                set { _componentData2 = value; }
            }

            internal void WriteJson(StringBuilder builder)
            {
                builder.Append("{\"name\":\"").Append(name).Append("\",\"uniqueID\":\"").Append(uniqueID).Append("\",\"components\":[");
                for (int i = 0; i < _componentData2.Count; i++)
                {
                    if (i > 0) builder.Append(',');
                    ComponentData component = _componentData2[i];
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
            Persistent = Go.AddComponentForTests(new Persistent { name = name, InstanceId = instanceId });
            Knight = Go.AddComponentForTests(new Knight { name = "Knight", InstanceId = instanceId });
            Persistent.Pointer = new IntPtr(instanceId * 64 + 32); // 真实 interop：组件也是带 native 指针的对象
            Knight.Pointer = new IntPtr(instanceId * 64 + 48);

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
                uniqueID = IslandSaveData.GetID(Persistent), // Pointer 由 stub 每实例唯一分配（真实 interop 为 native 指针）
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
        internal static void RunLoad(IslandSaveData island, Func<int, string, KnightUnit> spawn, Action<KnightUnit> afterInstantiate = null)
        {
            KnightIdentityLoadBridge.LoadScope scope = KnightIdentityLoadBridge.Begin(island);
            try
            {
                for (int i = 0; i < island.objects.Count; i++)
                {
                    IslandSaveData.ObjectData record = island.objects[i];
                    KnightUnit unit = spawn(i, record.uniqueID);
                    KnightIdentityRuntime.OnEnable(unit.Knight); // Knight.OnEnable 后缀
                    if (afterInstantiate != null) afterInstantiate(unit);
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
}
