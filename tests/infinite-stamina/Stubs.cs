// 测试专用 stub —— 只提供生产文件 il2cpp/PatchRide_InfiniteStamina.cs 引用到的最小面。
// 形态对齐真实 interop（字段可读写、Stamina/Rider 是属性、Steed 是 Component），
// 但这不是游戏：生产代码只被编译进来，绝不反向依赖本文件。不要随 mod 发布。
using System;
using System.Collections.Generic;
using System.Threading;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute
    {
        public readonly Type Target;
        public readonly string MethodName;
        public HarmonyPatch(Type target) { Target = target; }
        public HarmonyPatch(Type target, string methodName) { Target = target; MethodName = methodName; }
    }

    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyFinalizer : Attribute { }
}

namespace UnityEngine
{
    public class Object
    {
        private static int _next;
        public readonly IntPtr Pointer;
        public Object() { Pointer = (IntPtr)Interlocked.Increment(ref _next); }
        public int GetInstanceID() => (int)Pointer;
    }

    public class GameObject : Object { public bool activeInHierarchy = true; }

    public class Component : Object { public GameObject gameObject = new GameObject(); }

    /// <summary>stub：测试直接设 time/deltaTime 来模拟帧间隔与已疲劳时长。</summary>
    public static class Time
    {
        public static float time = 100f;
        public static float deltaTime = 1f / 60f;
    }

    public static class Mathf
    {
        public static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);
    }

    /// <summary>stub：只供原生模拟里的 reserveProbability 判定（测试里恒不触发）。</summary>
    public static class Random { public static float value; }
}

/// <summary>
/// 游戏 Player 的最小 stub：字段名/属性名与 2.1 源码与 2.4 interop 一致，
/// UpdateActionState 按 2.1.0 源码 Player.cs:1189 的分支结构复刻（体力加减、末尾 Clamp01、
/// Run 的枯竭/疲劳路径、TryToGallop 的 IsTired 门），用于在托管侧观察 patch 行为。
/// </summary>
public class Player : UnityEngine.Component
{
    public enum ActionState { Stand, Walk, Run, Rear, Eat, Spit, Immobile, ManualAttack, Glide, Transformed }

    private static bool _debugInfiniteStamina;
    public static int DebugInfiniteStaminaWrites;
    /// <summary>真实游戏里的全局调试开关（静态）。生产 patch 不得写它，测试用写入计数守住。</summary>
    public static bool DebugInfiniteStamina
    {
        get => _debugInfiniteStamina;
        set { DebugInfiniteStaminaWrites++; _debugInfiniteStamina = value; }
    }

    public bool hasLocalAuthority = true;
    public int playerId;
    public ActionState actionState = ActionState.Stand;
    public Steed _steed;
    public int NativeCalls;

    private int _previousKeyDirection;
    private int _previousActiveDirection;

    /// <summary>测试专用：把 _previousKeyDirection 摆到指定方向，避免首次调用被判定为换向。</summary>
    public void PrimePreviousDirection(int direction) => _previousKeyDirection = direction;

    /// <summary>测试专用：上马（含 Steed.Rider 双向绑定，真实里由 Steed.SetMode(Player, this) 完成）。</summary>
    public void Ride(Steed steed)
    {
        _steed = steed;
        steed.Rider = this;
    }

    public void UpdateActionState(int direction, bool startSprint, bool stopSprint,
        bool sprintKeyPressed, bool sprintKeyDoubleTap)
    {
        NativeCalls++;
        if (actionState == ActionState.Transformed) return;
        if (_steed == null) return;
        float dt = UnityEngine.Time.deltaTime;

        if (actionState == ActionState.Stand)
        {
            _steed.Stamina += _steed.standStaminaRate * dt;
            _steed.MidCall?.Invoke();
            if (direction != 0)
            {
                if (startSprint) TryToGallop(direction);
                else actionState = ActionState.Walk;
            }
        }
        else if (actionState == ActionState.Walk)
        {
            _steed.Stamina += _steed.walkStaminaRate * dt;
            _steed.MidCall?.Invoke();
            if (direction == 0 || direction != _previousKeyDirection) actionState = ActionState.Stand;
            else if (startSprint) TryToGallop(direction);
        }
        else if (actionState == ActionState.Run)
        {
            if (_steed.WellFedTimer <= 0f && !DebugInfiniteStamina)
                _steed.Stamina += _steed.runStaminaRate * dt;
            _steed.MidCall?.Invoke();
            if (direction == 0 || direction != _previousKeyDirection) actionState = ActionState.Stand;
            else if (stopSprint) actionState = ActionState.Walk;
            else if (_steed.Stamina <= 0f)
            {
                if (UnityEngine.Random.value < _steed.reserveProbability) _steed.Stamina = _steed.reserveStamina;
                else { actionState = ActionState.Walk; _steed.BecomeTired(); }
            }
        }
        else if (actionState == ActionState.Glide)
        {
            _steed.Stamina += _steed.glideStaminaRate * dt;
            _steed.MidCall?.Invoke();
        }

        _steed.Stamina = UnityEngine.Mathf.Clamp01(_steed.Stamina);
        _previousKeyDirection = direction;
        if (direction != 0) _previousActiveDirection = direction;
    }

    private void TryToGallop(int direction)
    {
        if (_steed.IsTired && !DebugInfiniteStamina) { actionState = ActionState.Rear; return; }
        actionState = ActionState.Run;
    }
}

/// <summary>
/// 技能 stub：字段/方法与 2.1.0 源码 SteedAbility.cs 对齐（_steed/_staminaCost/_cooldown/_duration，
/// public virtual Activate）。Activate 复刻 `if (!Player.DebugInfiniteStamina) Stamina -= _staminaCost`
/// 与 `_nextActivationTime = Time.time + _cooldown` 排程。CD/时长/消耗字段记账，用于证明 patch 没碰它们。
/// </summary>
public class SteedAbility : UnityEngine.Component
{
    /// <summary>stub：技能对象默认算"在当前 world/scene"（真实里它挂在世界内的坐骑上）；
    /// 需要模拟旧 scene 时用 OptionalQoLScope.Current.Remove(ability)。</summary>
    public SteedAbility()
    {
        KingdomEnhancedMod.OptionalQoLScope.Current.Add(this);
    }

    public Steed _steed;
    public float _nextActivationTime = float.NegativeInfinity;
    public int ActivateCalls;
    public readonly List<(string Field, float Value)> Writes = new List<(string, float)>();

    private float _staminaCostValue = 0.25f;
    public float _staminaCost
    {
        get => _staminaCostValue;
        set { Record("_staminaCost", value); _staminaCostValue = value; }
    }

    private float _cooldownValue = 30f;
    public float _cooldown
    {
        get => _cooldownValue;
        set { Record("_cooldown", value); _cooldownValue = value; }
    }

    private float _durationValue = 2f;
    public float _duration
    {
        get => _durationValue;
        set { Record("_duration", value); _durationValue = value; }
    }

    public virtual void Activate()
    {
        ActivateCalls++;
        if (!Player.DebugInfiniteStamina) _steed.Stamina -= _staminaCost;
        _nextActivationTime = UnityEngine.Time.time + _cooldown;
        if (_steed != null) _steed.MidCall?.Invoke();   // 原生体内注入点（回调用例用）
    }

    private void Record(string field, float value)
    {
        if (Steed.RecordWrites) Writes.Add((field, value));
    }
}

/// <summary>stub：GlideMovementSteedAbility —— 独立 Activate（不调用 base.Activate），
/// 体力不足时 Rear 并放弃；否则扣 _staminaCost（WellFed 期间改为扣 WellFedTimer）。</summary>
public class GlideMovementSteedAbility : SteedAbility
{
    public float _staminaGlideRegen = -0.1f;
    public int RearCalls;

    public override void Activate()
    {
        ActivateCalls++;
        if (_nextActivationTime > UnityEngine.Time.time) return;
        if (_steed.Stamina < _staminaCost)
        {
            RearCalls++;
            _nextActivationTime = UnityEngine.Time.time + 3f;
            return;
        }
        _nextActivationTime = UnityEngine.Time.time + _cooldown;
        if (!Player.DebugInfiniteStamina)
        {
            if (_steed.WellFedTimer > 0f) _steed.WellFedTimer -= _staminaCost * 10f;
            else _steed.Stamina -= _staminaCost;
        }
        if (_steed != null) _steed.MidCall?.Invoke();   // 原生体内注入点（回调用例用）
    }
}

/// <summary>stub：RunningAttackSteedAbility.OnPushedObjects —— 撞到层内目标加体力，否则扣体力。</summary>
public class RunningAttackSteedAbility : SteedAbility
{
    public float _attackStaminaCost = 0.2f;
    public float _attackStaminaGain = 0.1f;
    public float _attackWellFedGain = 2f;
    public bool HitPushedLayer;
    public int PushCalls;

    public void OnPushedObjects()
    {
        PushCalls++;
        if (!Player.DebugInfiniteStamina)
            _steed.Stamina += (HitPushedLayer ? _attackStaminaGain : -_attackStaminaCost);
        if (_steed.WellFedTimer > 0f) _steed.WellFedTimer += _attackWellFedGain;
    }
}

/// <summary>
/// 游戏 Steed 的最小 stub：四个 staminaRate / Stamina / _tiredTimer / Rider / IsTired / BecomeTired
/// 与 2.1.0 Steed.cs 一致；每个写入都记账（仅在 Steed.RecordWrites 打开时记，即只在钩子执行期间），
/// 这样"patch 没碰哪个字段"是可证伪的。
/// </summary>
public class Steed : UnityEngine.Component
{
    /// <summary>由测试 harness 在调用 patch 钩子前后开关；原生体执行期间关闭，只记 patch 的写入。</summary>
    public static bool RecordWrites;

    public string Label = "";

    private float _stamina = 1f;
    private float _wellFed;
    private float _tired;
    private float _walk = -0.05f;
    private float _run = -0.4f;
    private float _stand = 0.3f;
    private float _glide = -0.6f;

    public readonly List<(string Field, float Value)> Writes = new List<(string, float)>();

    /// <summary>测试注入：指定字段的 setter 抛异常（验证 prefix 中途异常时其余字段仍归还）。</summary>
    public string ThrowOnWrite;

    public Player Rider { get; set; }

    /// <summary>原生执行期间的注入点：模拟外部逻辑写字段、换坐骑、重入调用（含技能回调）、抛异常。</summary>
    public Action MidCall;

    public float tiredDuration = 6f;
    public float reserveStamina = 0.5f;
    public float reserveProbability = 0f;
    public float PuffThreshold = 0.34f;
    public int BecomeTiredCalls;

    public float Stamina
    {
        get => _stamina;
        set { Guard("Stamina"); Record("Stamina", value); _stamina = value; }
    }

    public float WellFedTimer
    {
        get => _wellFed;
        set { Guard("WellFedTimer"); Record("WellFedTimer", value); _wellFed = value; }
    }

    public float walkStaminaRate
    {
        get => _walk;
        set { Guard("walkStaminaRate"); Record("walkStaminaRate", value); _walk = value; }
    }

    public float runStaminaRate
    {
        get => _run;
        set { Guard("runStaminaRate"); Record("runStaminaRate", value); _run = value; }
    }

    public float standStaminaRate
    {
        get => _stand;
        set { Guard("standStaminaRate"); Record("standStaminaRate", value); _stand = value; }
    }

    public float glideStaminaRate
    {
        get => _glide;
        set { Guard("glideStaminaRate"); Record("glideStaminaRate", value); _glide = value; }
    }

    public float _tiredTimer
    {
        get => _tired;
        set { Guard("_tiredTimer"); Record("_tiredTimer", value); _tired = value; }
    }

    private float _walkSpeed = 2f;
    private float _runSpeed = 4f;
    public float walkSpeed
    {
        get => _walkSpeed;
        set { Guard("walkSpeed"); Record("walkSpeed", value); _walkSpeed = value; }
    }
    public float runSpeed
    {
        get => _runSpeed;
        set { Guard("runSpeed"); Record("runSpeed", value); _runSpeed = value; }
    }

    public bool IsTired => _tired > UnityEngine.Time.time;

    public void BecomeTired()
    {
        BecomeTiredCalls++;
        _tiredTimer = UnityEngine.Time.time + tiredDuration;
    }

    public void ClearWrites() => Writes.Clear();

    private void Record(string field, float value)
    {
        if (RecordWrites) Writes.Add((field, value));
    }

    private void Guard(string field)
    {
        if (ThrowOnWrite == field) throw new InvalidOperationException("stub setter throws: " + field);
    }
}

/// <summary>stub：只为证明 patch 不咨询世界权威（客户端本机玩家 HasWorldAuth 为 false 仍要生效）。</summary>
public static class NetworkBigBoss { public static bool HasWorldAuth = true; }

namespace KingdomEnhancedMod
{
    /// <summary>stub：真实是 BepInEx ConfigEntry&lt;bool&gt;；生产代码只读 != null &amp;&amp; .Value。</summary>
    public static class ModConfig
    {
        public static BoolEntry InfiniteSteedStamina = new BoolEntry();
        public sealed class BoolEntry { public bool Value; }
    }

    /// <summary>
    /// stub：真实 OptionalQoLScope 依赖 BiomeHolder/Managers/world.gameLayer，这里用两个可控开关代替
    /// —— IsActive 模拟"总开关 + 已在具体 biome（主菜单为 false）"，Current 集合模拟
    /// "activeInHierarchy + 当前 world 层 + 当前 scene"。
    /// </summary>
    public static class OptionalQoLScope
    {
        public static bool IsActive = true;
        public static readonly HashSet<UnityEngine.Component> Current = new HashSet<UnityEngine.Component>();
        public static bool IsCurrent(UnityEngine.Component component)
            => component != null && Current.Contains(component);
    }

    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
        public Logger LogSource = new Logger();

        public sealed class Logger
        {
            public static readonly List<string> Errors = new List<string>();
            public void LogError(string message) => Errors.Add(message);
            public void LogWarning(string message) => Errors.Add(message);
            public void LogInfo(string message) { }
        }
    }
}
