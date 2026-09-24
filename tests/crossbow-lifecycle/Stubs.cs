// 弩手生命周期回归的边界替身（只替边界，不替被测逻辑）。
//
// 被测对象是真生产文件 il2cpp/CrossbowmanLifecycle.cs（由 csproj 直接编译进来，
// 未做任何修改）。本文件只提供它依赖的原生/宿主边界：
// - UnityEngine：Object/Component/GameObject/Transform/Animator/Vector2/Mathf —— 语义
//   按原生对齐（组件挂在 GameObject 上、AddComponent 走 IL2CPP 的 (IntPtr) 构造器约定、
//   activeInHierarchy 控制"池中/停用"、Destroy/DestroyImmediate 全部记账以便断言
//   生命周期一次都没销毁过东西）。
// - 游戏类型：Archer/Scanner/ArrowAttack/Mover/GuardSlot/BiomeData —— 只保留被测代码
//   真正读写的字段。
// - 宿主与联动边界：ModConfig / KingdomEnhancedPlugin / PatchRoles_CrossbowDefense
//   （CrossbowDefense 的真实逻辑由 tests/crossbow-defense 直链覆盖，这里用记账替身
//   验证调用契约）/ GreekScaleScope / ScaleRegistryHolder（缩放真实语义由
//   tests/greek-scale-scope 直链覆盖，这里按"只动 y / 只还原自有轴"的最小语义记账）。
using System;
using System.Collections.Generic;

namespace Il2CppInterop.Runtime.Injection
{
    /// <summary>ClassInjector 边界：只记录注册过的类型（生产代码用非泛型重载）。</summary>
    public static class ClassInjector
    {
        public static readonly HashSet<Type> Registered = new HashSet<Type>();

        public static bool IsTypeRegisteredInIl2Cpp(Type type) => Registered.Contains(type);

        public static void RegisterTypeInIl2Cpp(Type type) => Registered.Add(type);
    }
}

namespace UnityEngine
{
    public class Object
    {
        private static int _next = 0x1000;

        /// <summary>IL2CPP 身份：池复用保持同一实例同一指针（跨生命周期不变）。</summary>
        public IntPtr Pointer;

        public string name = string.Empty;

        /// <summary>全部 Destroy/DestroyImmediate 调用：生命周期必须零调用（测试全量断言）。</summary>
        public static readonly List<Object> DestroyCalls = new List<Object>();
        public static readonly List<Object> DestroyImmediateCalls = new List<Object>();

        public Object()
        {
            Pointer = new IntPtr(_next += 8);
        }

        public Object(IntPtr pointer)
        {
            Pointer = pointer == IntPtr.Zero ? new IntPtr(_next += 8) : pointer;
        }

        public static void Destroy(Object target) => DestroyCalls.Add(target);

        public static void DestroyImmediate(Object target) => DestroyImmediateCalls.Add(target);

        public static void ResetDestroyLog()
        {
            DestroyCalls.Clear();
            DestroyImmediateCalls.Clear();
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;

        public Component() { }

        public Component(IntPtr pointer) : base(pointer) { }

        public Transform transform => gameObject != null ? gameObject.transform : null;

        public T GetComponent<T>() where T : class => gameObject != null ? gameObject.GetComponent<T>() : null;

        /// <summary>测试模型：控制器/子组件都挂在根对象上（生产代码只取一个 Animator）。</summary>
        public T GetComponentInChildren<T>() where T : class => GetComponent<T>();
    }

    public class Behaviour : Component
    {
        public Behaviour() { }

        public Behaviour(IntPtr pointer) : base(pointer) { }
    }

    public class MonoBehaviour : Behaviour
    {
        public MonoBehaviour() { }

        public MonoBehaviour(IntPtr pointer) : base(pointer) { }
    }

    public class GameObject : Object
    {
        private readonly List<Component> _components = new List<Component>();

        /// <summary>原生语义：池 FastDespawn 只 SetActive(false)，不销毁。</summary>
        public bool activeInHierarchy = true;

        public Transform transform;

        public GameObject()
        {
            transform = new Transform();
            transform.gameObject = this;
        }

        public T AddComponent<T>() where T : Component
        {
            // IL2CPP 注入类型的托管包装构造器约定：(IntPtr)；纯托管替身类型没有该构造器时
            // 回落无参构造（测试夹具里的 Archer/Animator/Mover 等）。
            var type = typeof(T);
            var pointerCtor = type.GetConstructor(new[] { typeof(IntPtr) });
            var component = (T)(pointerCtor != null
                ? pointerCtor.Invoke(new object[] { IntPtr.Zero })
                : Activator.CreateInstance(type));
            component.gameObject = this;
            _components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : class
        {
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T match) return match;
            }
            return null;
        }

        /// <summary>测试专用：同一 GameObject 上的组件数（marker 必须复用而不是越挂越多）。</summary>
        public int CountComponents<T>() where T : class
        {
            int count = 0;
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T) count++;
            }
            return count;
        }
    }

    public class Transform : Component
    {
        public Vector3 localScale = new Vector3(1f, 1f, 1f);
        public Vector3 position;
        public Transform parent;
    }

    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public override string ToString() => "(" + x + ", " + y + ")";
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    public static class Mathf
    {
        public static float Abs(float value) => MathF.Abs(value);
    }

    public class RuntimeAnimatorController : Object
    {
        public RuntimeAnimatorController() { }

        public RuntimeAnimatorController(string n) => name = n;
    }

    public class Animator : Behaviour
    {
        public RuntimeAnimatorController runtimeAnimatorController;
    }
}

public class Scanner
{
    public float range = 12f;
    public float rangeBehind = 12f;
}

public class Mover : UnityEngine.Component
{
}

public class GuardSlot
{
}

/// <summary>原生 Archer 边界：只保留生命周期真正读写的字段（签名与 2.4 interop 一致）。</summary>
public class Archer : UnityEngine.Component
{
    public float shootRange = 8f;          // 原生基础弓 8
    public float towerShootRange = 12f;
    public bool inGuardSlot;
    public GuardSlot _guardSlot;
    public object _knight;
    public bool _isWearingBannerColor;
    public Scanner _enemyScanner = new Scanner();
    public ArrowAttack _arrowAttack;
    public ArrowAttack _fireArrowAttack;
    public ArrowAttack ActiveArrowAttack;
    public UnityEngine.RuntimeAnimatorController hunterAnimator;
    public UnityEngine.Vector2 _shootIntervalRange;
    public UnityEngine.Vector2 _shootIntervalRangeFormation;

    private UnityEngine.RuntimeAnimatorController _soldierAnimator;

    /// <summary>原生 prefab 字段（interop 为属性）：写计数供"生根一次/稳态零写入"断言。</summary>
    public UnityEngine.RuntimeAnimatorController soldierAnimator
    {
        get => _soldierAnimator;
        set
        {
            _soldierAnimator = value;
            SoldierAnimatorWrites++;
        }
    }

    public int SoldierAnimatorWrites;

    /// <summary>夹具专用：直接写 backing field（模拟 prefab 初值），不计入写计数。</summary>
    public void SeedSoldierAnimator(UnityEngine.RuntimeAnimatorController value) => _soldierAnimator = value;
}

public class ArrowAttack : UnityEngine.Object
{
    public ArrowAttack() { }

    public ArrowAttack(string n) => name = n;
}

/// <summary>BiomeData 边界：GetAssetSwapForThis 默认恒等；可按测试要求抛异常（swap 表未就绪）。</summary>
public class BiomeData
{
    public static BiomeData Current = new BiomeData();

    public bool SwapThrows;

    public T GetAssetSwapForThis<T>(T asset)
    {
        if (SwapThrows) throw new InvalidOperationException("swap table not ready");
        return asset;
    }
}

namespace KingdomEnhancedMod
{
    public static class ModConfig
    {
        public class BoolConfig
        {
            public bool Value = true;
        }

        public static BoolConfig Enabled = new BoolConfig();
    }

    /// <summary>
    /// CrossbowDefense 记账替身：真实守位/塔位逻辑由 tests/crossbow-defense 直链覆盖，
    /// 这里只验证生命周期对它的调用契约（Apply/巡检=ReconcileTowerRange，
    /// Strip=Remove，有效身份巡检=TryPullBack），并记录调用瞬间"读者看到的身份"
    /// ——Apply 必须先把 Active 提交好，塔位增益分支才吃得到（首次增强顺序回归）。
    /// </summary>
    internal static class PatchRoles_CrossbowDefense
    {
        internal static int ReconcileCalls;
        internal static int RemoveCalls;
        internal static int PullBackCalls;
        internal static bool ReconcileThrows;
        internal static bool ReconcileSawIdentity;

        internal static void ReconcileTowerRange(Archer archer)
        {
            ReconcileCalls++;
            ReconcileSawIdentity = CrossbowmanLifecycle.IsCrossbowman(archer);
            if (ReconcileThrows) throw new InvalidOperationException("tower reconcile failed");
        }

        internal static void Remove(Archer archer) => RemoveCalls++;

        internal static bool TryPullBack(Archer archer)
        {
            PullBackCalls++;
            return true;
        }

        internal static void Reset()
        {
            ReconcileCalls = 0;
            RemoveCalls = 0;
            PullBackCalls = 0;
            ReconcileThrows = false;
            ReconcileSawIdentity = false;
        }
    }

    /// <summary>
    /// 缩放边界替身（真实 scope 由 tests/greek-scale-scope 直链覆盖）：按最小自有轴语义
    /// 记账——首次 ApplyY 记录原 y，Restore 还原该轴。
    /// </summary>
    internal static class GreekScaleScope
    {
        private static readonly Dictionary<UnityEngine.Transform, float> Originals =
            new Dictionary<UnityEngine.Transform, float>();

        internal static int ApplyYCalls;

        internal static void ApplyY(UnityEngine.Transform target, float y)
        {
            ApplyYCalls++;
            if (!Originals.ContainsKey(target)) Originals[target] = target.localScale.y;
            var scale = target.localScale;
            scale.y = y;
            target.localScale = scale;
        }

        internal static void Restore(UnityEngine.Transform target)
        {
            if (!Originals.TryGetValue(target, out float original)) return;
            var scale = target.localScale;
            scale.y = original;
            target.localScale = scale;
            Originals.Remove(target);
        }

        internal static void Reset()
        {
            Originals.Clear();
            ApplyYCalls = 0;
        }
    }

    /// <summary>ScaleRegistry 记账替身（真实实现委托 GreekScaleScope），可注入失败覆盖异常路径。</summary>
    internal static class ScaleRegistryHolder
    {
        internal static int RegisterCalls;
        internal static int UnregisterCalls;
        internal static float LastRegisteredY;
        internal static int FailRegisterAfterCalls = -1;     // -1 = 不失败
        internal static int FailUnregisterAfterCalls = -1;

        internal static void Register(Mover mover, float y)
        {
            RegisterCalls++;
            if (FailRegisterAfterCalls >= 0 && RegisterCalls > FailRegisterAfterCalls)
                throw new InvalidOperationException("scale register failed");
            LastRegisteredY = y;
        }

        internal static void Unregister(Mover mover)
        {
            UnregisterCalls++;
            if (FailUnregisterAfterCalls >= 0 && UnregisterCalls > FailUnregisterAfterCalls)
                throw new InvalidOperationException("scale unregister failed");
        }

        internal static void Reset()
        {
            RegisterCalls = 0;
            UnregisterCalls = 0;
            LastRegisteredY = 0f;
            FailRegisterAfterCalls = -1;
            FailUnregisterAfterCalls = -1;
        }
    }

    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();

        public Logger LogSource = new Logger();

        public class Logger
        {
            public static readonly List<string> Errors = new List<string>();
            public static readonly List<string> Warnings = new List<string>();
            public static readonly List<string> Lines = new List<string>();

            public void LogError(string message) => Errors.Add(message);

            public void LogWarning(string message) => Warnings.Add(message);

            public void LogInfo(string message) => Lines.Add(message);
        }
    }
}
