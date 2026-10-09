// Issue #194 Android 保护 host 的最小替身宇宙（仅 task-private 测试工程使用）。
//
// globalalias 模式（同 android/GlobalAliases.cs 的解析路径）：两份共享生产源里的裸游戏类型名
// 经下面这组 global using 解析到本工程的真实 doubles —— doubles 放在命名空间 Il2Cpp，
// 镜像 Il2CppInterop namespace-prefix 布局（Assembly-CSharp 全局类型 -> Il2Cpp.*，
// UnityEngine.* 保持原名）。生产侧实际 interop 构建由 android 工程另证一层；本工程只证
// ANDROID 预处理下的控制流。
//
// 最小替身边界（只含共享保护路径成员；有界 native 事实，不复制生产算法，不冒充 native）：
//   - Droppable.OnDisable 按 Droppable-OnDisable.asm.txt：先调用 +0x30 可选 callback
//     （0x2323670-0x2323688 blr），返回后才把 +0xf4 保存值写回 CurrentEnemyPolicy+0x4c
//     （0x232368c-0x232369c）。替身按同一顺序建模：callback 抛错时 reset 语句未到达，
//     不伪造 reset 成功；同段 +0x110 清零字段名称未绑定，不建模。
//   - Droppable.OnEnable（128B FDE）本体未读全：替身不建模任何启停重置，生产 postfix 只在其后。
//   - CurrentEnemyPolicy 的读/写注入是 host fault（不是 native 事实）：写未发生不记为成功写。
//   - 不提供召回 API/类型：没有 SpawnNearP1 / SetupDog / SetDogStatus / SetHermitStatus /
//     GetDogStatus / DogType / DogPosition / HermitType / HermitPosition / CampaignSaveData /
//     Holder / Kingdom / Il2CppSystem，误保留的召回代码在 ANDROID 预处理下直接编译失败。
//   - ModConfig / KingdomEnhancedPlugin 镜像 android/MobilePlayerConfig.cs 与 HoldBridges.cs
//     的真实形状（会话开关只读；Instance 未接线为 null；LogSource 提供 LogInfo/LogWarning）。
global using Il2Cpp;
// 显式 alias 集合与 android/GlobalAliases.cs 的既有条目一致；Dog/Hermit/Boat/Game/PickUpPolicy
// 在生产里没有显式 global alias，同样只靠 `global using Il2Cpp;` 的命名空间导入解析（本工程同款）。
global using Droppable = Il2Cpp.Droppable;
global using Managers = Il2Cpp.Managers;
global using NetworkBigBoss = Il2Cpp.NetworkBigBoss;
global using World = Il2Cpp.World;
using System;
using System.Collections.Generic;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class HarmonyPatch : Attribute
    {
        public Type Target;
        public string Method;
        public HarmonyPatch(Type target, string method) { Target = target; Method = method; }
    }
    public class HarmonyPostfix : Attribute { }
    public class HarmonyPrefix : Attribute { }
}

namespace UnityEngine
{
    public class Object
    {
        static int next;
        public int Id = System.Threading.Interlocked.Increment(ref next);
        public IntPtr Pointer { get; set; }
        public Object() { Pointer = (IntPtr)Id; }
        public int GetInstanceID() => Id;
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject != null ? gameObject.transform : null;
        public bool CompareTag(string candidate) => gameObject != null && gameObject.tag == candidate;
        public T GetComponent<T>() where T : Component => gameObject != null ? gameObject.GetComponent<T>() : null;
    }

    public struct Scene { public int handle; }

    public class GameObject : Object
    {
        public string name = "unit";
        public string tag = "";
        public bool activeInHierarchy = true;
        public Transform transform;
        public Scene scene = new Scene { handle = 10 };
        readonly List<Component> components = new List<Component>();
        public GameObject() { transform = new Transform { gameObject = this }; }
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; components.Add(c); return c; }
        public T GetComponent<T>() where T : Component
        {
            foreach (Component c in components)
                if (c is T match) return match;
            return null;
        }
        public bool CompareTag(string candidate) => tag == candidate;
    }

    public class Transform : Component
    {
        public Transform parent;
        public bool IsChildOf(Transform t)
        {
            for (Transform current = this; current != null; current = current.parent)
                if (current == t) return true;
            return false;
        }
    }

    public static class Time { public static float unscaledTime; }
}

namespace Il2Cpp
{
    public enum PickUpPolicy { Anybody = 0, AnybodyExceptDropper = 1, OnlyClaimer = 2, AnyPlayer = 3, Nobody = 4, Blocked = 5, EnemyOnly = 6, WorkerOnly = 7 }

    public class Droppable : UnityEngine.Component
    {
        PickUpPolicy enemy;
        public int EnemyWrites, EnemyReads, GeneralWrites, OriginalWrites, NativeResetCount;
        public bool ThrowRead, ThrowWrite; // host fault 注入，不是 native 事实
        public PickUpPolicy CurrentEnemyPolicy
        {
            get { EnemyReads++; if (ThrowRead) throw new InvalidOperationException("host-injected policy read fault"); return enemy; }
            set { if (ThrowWrite) throw new InvalidOperationException("host-injected policy write fault"); EnemyWrites++; enemy = value; }
        }
        PickUpPolicy original, general = PickUpPolicy.AnyPlayer;
        public PickUpPolicy _originalEnemyPolicy { get => original; set { OriginalWrites++; original = value; } }
        public PickUpPolicy pickUpPolicy { get => general; set { GeneralWrites++; general = value; } }
        // native +0x30 可选 callback 的最小替身（ASM 0x2323670-0x2323688）。
        public Action NativeDisableCallback;
        public void NativePolicy(PickUpPolicy policy) => enemy = policy;
        public void NativeOriginal(PickUpPolicy policy) => original = policy;
        public void OnEnable() { }
        public void OnDisable()
        {
            if (NativeDisableCallback != null) NativeDisableCallback(); // 先 callback；抛错则下面的 reset 未到达
            enemy = original;
            NativeResetCount++;
        }
    }

    public class Dog : UnityEngine.Component { }
    public class Hermit : UnityEngine.Component { }
    public class Boat : UnityEngine.Component { }

    public class Game
    {
        public State state = State.Playing;
        public enum State { Playing, NetworkClientPlaying, Menu, Loading }
    }

    public class World : UnityEngine.Object { public UnityEngine.Transform gameLayer = new UnityEngine.GameObject().transform; }

    public class Managers
    {
        public static Managers Inst;
        public World world = new World();
        public Game game = new Game();
    }

    public static class NetworkBigBoss { public static bool HasWorldAuth = true; }
}

namespace KingdomEnhancedMod
{
    // 镜像 android/MobilePlayerConfig.cs：会话总开关 Enabled 为只读 Setting<bool>(true)；
    // PetGuardEnabled 是 Initialize 时创建的条目（共享源只读 .Value，真实默认 false），
    // 未接线时为 null。
    internal static class ModConfig
    {
        internal sealed class Setting<T> { internal T Value; internal Setting(T value) { Value = value; } }
        internal static readonly Setting<bool> Enabled = new Setting<bool>(true);
        internal static Setting<bool> PetGuardEnabled;
    }

    // 镜像 android/HoldBridges.cs：Instance 只读单例（未接线为 null），LogSource 提供
    // LogInfo/LogWarning；ResetForTest 是替身专用（真实 Initialize 只在新实例时接线）。
    internal sealed class KingdomEnhancedPlugin
    {
        private static KingdomEnhancedPlugin instance;
        private readonly Logger logSource = new Logger();
        internal static KingdomEnhancedPlugin Instance => instance;
        internal Logger LogSource => logSource;
        internal static void ResetForTest() { instance = new KingdomEnhancedPlugin(); }
    }

    internal sealed class Logger
    {
        internal readonly List<string> Info = new List<string>();
        internal readonly List<string> Warnings = new List<string>();
        internal readonly List<string> Errors = new List<string>();
        internal void LogInfo(string message) => Info.Add(message);
        internal void LogWarning(string message) => Warnings.Add(message);
        internal void LogDebug(string message) { }
        internal void LogError(object message) => Errors.Add(message?.ToString() ?? "null");
    }
}
