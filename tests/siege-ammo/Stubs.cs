// Stubs.cs — fake game/native/Harmony APIs for the SiegeAmmoCounts regression suite.
// These model only the observable IL2CPP surface the production file compiles against:
// pointer/instance identities, Unity active-in-hierarchy semantics, native read counters,
// throw switches and interop collections. No production algorithm is mirrored here.
//
// Deliberately absent: ModConfig / GreekBankScope stubs. SiegeAmmoCounts.cs must compile
// and behave with no config or scope dependency (仅由 enabled 参数驱动) — the successful
// build of this project is that evidence.
using System;
using System.Collections.Generic;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatchAttribute : Attribute
    {
        public readonly Type TargetType;
        public readonly string MethodName;
        public HarmonyPatchAttribute(Type targetType, string methodName)
        {
            TargetType = targetType;
            MethodName = methodName;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPostfixAttribute : Attribute { }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    /// <summary>Interop array surface used by the production cache: Length + indexer.</summary>
    public class Il2CppReferenceArray<T>
    {
        private readonly T[] _items;
        public Il2CppReferenceArray(T[] items) { _items = items; }
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
    /// <summary>Il2CppHashSet surface used by the production cache (enumerate/add/remove).</summary>
    public class HashSet<T> : System.Collections.Generic.HashSet<T> { }
}

namespace UnityEngine
{
    public struct Scene { public int handle; }

    public class Object
    {
        private static long _pointer = 0x100000;
        private static int _id = 1000;
        public IntPtr Pointer = (IntPtr)(_pointer += 0x40);
        public int InstanceId = ++_id;
        public string name = "obj";
        public int GetInstanceID() => InstanceId;
        internal static IntPtr NextPointer() => (IntPtr)(_pointer += 0x40);
        internal static int NextId() => ++_id;
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform;
        public T GetComponent<T>() where T : Component
            => gameObject != null ? gameObject.GetComponent<T>() : null;
    }

    public class Transform : Component
    {
        public Transform parent;
        public bool IsChildOf(Transform t)
        {
            for (Transform cur = this; cur != null; cur = cur.parent)
                if (ReferenceEquals(cur, t)) return true;
            return false;
        }
    }

    public class GameObject : Object
    {
        public bool Active = true;
        public Scene scene;
        public Transform transform;
        internal readonly List<Component> components = new List<Component>();

        public bool activeInHierarchy
        {
            get
            {
                for (Transform t = transform; t != null; t = t.parent)
                    if (!t.gameObject.Active) return false;
                return true;
            }
        }

        public T GetComponent<T>() where T : Component
        {
            foreach (Component c in components) if (c is T typed) return typed;
            return null;
        }
    }
}

// ---------------------------------------------------------------------------
// Game interop stubs (global namespace, as the game assembly exposes them).
// ---------------------------------------------------------------------------

public class Game
{
    public enum State { Menu, Playing, Paused, NetworkClientPlaying }
    public State state = State.Playing;
}

public class World
{
    public UnityEngine.Transform gameLayer;
    public IntPtr Pointer;
}

public class Kingdom
{
    public IntPtr Pointer;
}

public class Payable : UnityEngine.Component { public T TryCast<T>() where T : class => this as T; }

public class PayableWorkshop : Payable
{
    public Catapult catapult;
}

public class PayableShop : Payable { }   // 真实商店类型；不属于弹药目标

public class Catapult : UnityEngine.Component
{
    private int _queued;
    public int QueuedReads;

    /// <summary>Native field read counter: only the 0.5s / force sampling window may read it.</summary>
    public int queuedOilBarrels
    {
        get { QueuedReads++; return _queued; }
        set { _queued = value; }
    }

    public int QueuedValue => _queued;          // 测试自用，不计读取
    public bool currentLaunchableIsOil;
    public bool _fireAttacks;
    public UnityEngine.GameObject _projectile;
    public UnityEngine.Transform spoon;
}

/// <summary>
/// 真实 interop 里该接口是 Il2CppObjectBase 派生类型（owner 判据用 .Pointer 精确比较）；
/// 本 stub 以带 Pointer 成员的接口等价建模。
/// </summary>
public interface IPayableComponentOwner
{
    IntPtr Pointer { get; }
}

public class PayableComponent : Payable
{
    public IPayableComponentOwner _owner;
}

/// <summary>装填/发射使用的油弹弹体（真实 Oil Barrel / Greek Fire Jar prefab 上的组件）。</summary>
public class OilBarrel : UnityEngine.Component { }

public class FireTower : UnityEngine.Component, IPayableComponentOwner
{
    IntPtr IPayableComponentOwner.Pointer => Pointer;
    private int _jars;
    public int JarReads;

    /// <summary>Native field read counter for the sampled jar count.</summary>
    public int _fireJarsActiveNum
    {
        get { JarReads++; return _jars; }
        set { _jars = value; }
    }

    public int JarsValue => _jars;              // 测试自用，不计读取
    public int _maxFireJars = 12;
    public PayableComponent _payableComponent;
    public UnityEngine.GameObject _projectile;
}

public class RollableOilBarrel : UnityEngine.Component
{
    public PayableWorkshopBarrel _parentWorkshop;
    public Catapult targetCatapult;
    public bool _delivered;
}

public class PayableWorkshopBarrel : Payable
{
    private PayableWorkshop _counterpart;
    public int CounterpartReads;
    public bool ThrowOnCounterpartRead;

    public PayableWorkshop catapultCounterpart
    {
        get
        {
            CounterpartReads++;
            if (ThrowOnCounterpartRead) throw new InvalidOperationException("counterpart unavailable");
            return _counterpart;
        }
        set { _counterpart = value; }
    }

    public Il2CppSystem.Collections.Generic.HashSet<RollableOilBarrel> _activeBarrels = new();
}

public class PayableManager
{
    private readonly List<Payable> _payables = new List<Payable>();

    /// <summary>AllPayables getter read counter: membership must be read only on bootstrap/dirty.</summary>
    public int Reads;
    public bool ThrowOnRead;

    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Payable> AllPayables
    {
        get
        {
            Reads++;
            if (ThrowOnRead) throw new InvalidOperationException("payable registry unavailable");
            return new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Payable>(_payables.ToArray());
        }
    }

    // 原生 Payable.OnEnable/OnDisable + mod Postfix 的等价面
    public void AddPayable(Payable payable)
    {
        if (payable == null || _payables.Contains(payable)) return;
        _payables.Add(payable);
        KingdomEnhancedMod.SiegeAmmoCounts.HookRegistryChanged();
    }

    public void RemovePayable(Payable payable)
    {
        if (!_payables.Remove(payable)) return;
        KingdomEnhancedMod.SiegeAmmoCounts.HookRegistryChanged();
    }

    /// <summary>只用于验证 Harmony 接线：绕过钩子直接改注册表（原生事件丢失场景）。</summary>
    public void SetRegistryDirectly(params Payable[] payables)
    {
        _payables.Clear();
        _payables.AddRange(payables);
    }

    public int RegisteredCount => _payables.Count;
}

public class Managers
{
    public Kingdom kingdom;
    public World world;
    public Game game;
    public PayableManager payables;
}

public static class NetworkBigBoss
{
    public static bool HasWorldAuth = true;
}

public class ManualLogSource
{
    public readonly List<string> Warnings = new List<string>();
    public readonly List<string> Infos = new List<string>();
    public void LogWarning(string message) => Warnings.Add(message);
    public void LogInfo(string message) => Infos.Add(message);
    public void LogError(string message) => Warnings.Add(message);
}

public class KingdomEnhancedPlugin
{
    public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
    public ManualLogSource LogSource = new ManualLogSource();
}

/// <summary>Test-side world builder: real pointer/instance identities like the interop objects.</summary>
public static class Fabric
{
    public static UnityEngine.GameObject NewGo(string name, UnityEngine.Transform parent, int scene)
    {
        var go = new UnityEngine.GameObject { name = name, scene = new UnityEngine.Scene { handle = scene } };
        var t = new UnityEngine.Transform { name = name, parent = parent, gameObject = go };
        t.transform = t;
        go.transform = t;
        return go;
    }

    public static T Attach<T>(UnityEngine.GameObject go, T component) where T : UnityEngine.Component
    {
        component.gameObject = go;
        component.transform = go.transform;
        go.components.Add(component);
        return component;
    }
}
