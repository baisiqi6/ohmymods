using System.Collections.Generic;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public readonly Type TargetType;
        public readonly string MethodName;
        public HarmonyPatch(Type type, string name) { TargetType = type; MethodName = name; }
    }
    public class HarmonyPrefix : Attribute { }
    public class HarmonyPostfix : Attribute { }
    // HarmonyX 的优先级常量（Last 最低）：测试断言生产 prefix 显式排在最后。
    public static class Priority { public const int Last = 0, LowerThanLow = 100, Low = 200, Normal = 400, First = 800; }
    public class HarmonyPriority : Attribute
    {
        public readonly int Info;
        public HarmonyPriority(int priority) { Info = priority; }
    }
}

namespace Il2CppInterop.Runtime.Injection
{
    // 测试替身只记录注册；AddComponent 由 GameObject 替身直接构造托管实例。
    public static class ClassInjector
    {
        public static readonly HashSet<Type> Registered = new();
        public static bool IsTypeRegisteredInIl2Cpp(Type type) => Registered.Contains(type);
        public static void RegisterTypeInIl2Cpp(Type type) => Registered.Add(type);
        public static void RegisterTypeInIl2Cpp<T>() => Registered.Add(typeof(T));
    }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    /// <summary>
    /// 真实 Il2CppReferenceArray&lt;T&gt; 的关键语义替身（测试托管内存，不触碰 native）：
    /// - 容量构造 (long) 会新分配 storage；
    /// - T[] 构造与 implicit(T[]) 都是【新实例复制】——绝不共享传入数组；
    /// - 原生写入（测试里的 Physics2D 查询）只写 wrapper 的 storage，源 managed 数组永远看不到，
    ///   这正是已装旧版命中缓冲静默丢结果的陷阱（counterexample 用例钉死它）。
    /// </summary>
    public class Il2CppReferenceArray<T>
    {
        public static int Conversions, Allocations;
        private readonly T[] storage;
        public Il2CppReferenceArray(long size) { Allocations++; storage = new T[(int)size]; }
        public Il2CppReferenceArray(T[] source)
        {
            Allocations++;
            storage = new T[source.Length];
            Array.Copy(source, storage, source.Length);
        }
        public long Length => storage.LongLength;
        public T this[int index] { get => storage[index]; set => storage[index] = value; }
        public static implicit operator Il2CppReferenceArray<T>(T[] array)
        {
            Conversions++;
            return array == null ? null : new Il2CppReferenceArray<T>(array);
        }
    }
}

namespace UnityEngine
{
    public class Object
    {
        private static long next;
        public IntPtr Pointer { get; set; } = (IntPtr)Interlocked.Increment(ref next);
        public bool Destroyed;
        public static implicit operator bool(Object obj) => obj is not null && !obj.Destroyed;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b) ||
            ((a is null || a.Destroyed) && (b is null || b.Destroyed));
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => Pointer.GetHashCode();
    }
    public class GameObject : Object
    {
        private readonly List<Component> components = new();
        public Transform transform;
        public bool activeInHierarchy = true, activeSelf = true;
        public int Id;
        public string tag = "Citizen";
        public Scene scene;
        public GameObject() { Id = (int)Pointer; transform = new Transform { gameObject = this }; }
        public int GetInstanceID() => Id;
        // 注入的生产组件只有 IntPtr 构造（ClassInjector 契约）：优先无参，失败则走 IntPtr 构造。
        public T AddComponent<T>() where T : Component
        {
            T component;
            try { component = (T)Activator.CreateInstance(typeof(T)); }
            catch (MissingMethodException) { component = (T)Activator.CreateInstance(typeof(T), new object[] { IntPtr.Zero }); }
            component.gameObject = this;
            components.Add(component);
            return component;
        }
        public T GetComponent<T>() where T : class => components.OfType<T>().FirstOrDefault();
        public bool TryGetComponent<T>(out T component) where T : class { component = GetComponent<T>(); return component != null; }
        public int ComponentCount => components.Count;
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        // 真实 Mover 继承 Component.rigidbody：可替换字段，测试用它模拟刚体被换掉。
        public Rigidbody2D rigidbody;
        public bool TryGetComponent<T>(out T component) where T : class => gameObject.TryGetComponent(out component);
    }
    public class MonoBehaviour : Component
    {
        public MonoBehaviour() { }
        // IL2CPP 注入要求：ClassInjector 注册的 MonoBehaviour 需可反射构造的 IntPtr 构造。
        public MonoBehaviour(IntPtr ptr) { }
        private bool enabledState = true;
        public virtual bool enabled { get => enabledState; set => enabledState = value; }
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }
    public struct Scene { public int handle; }
    public class Transform : Component
    {
        public Vector3 position, localScale = Vector3.one;
        public Transform parent;
        // 真实向上遍历语义（不是恒真替身）：IsChildOf 允许同层换父节点而不算离开层级。
        public bool IsChildOf(Transform target)
        {
            for (var t = this; t != null; t = t.parent) if (ReferenceEquals(t, target)) return true;
            return false;
        }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y = 0, float z = 0) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new(1, 1, 1);
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y = 0) { this.x = x; this.y = y; }
        public static Vector2 zero => new(0, 0);
    }
    public static class Mathf
    {
        public const float Rad2Deg = 57.29578f, Deg2Rad = .017453292f;
        public static float Abs(float value) => MathF.Abs(value);
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Sign(float value) => value >= 0 ? 1 : -1;
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        public static float Sqrt(float value) => MathF.Sqrt(value);
        public static float Atan2(float y, float x) => MathF.Atan2(y, x);
        public static bool Approximately(float a, float b) => MathF.Abs(a - b) < 0.00001f;
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (MathF.Abs(target - current) <= maxDelta) return target;
            return current + MathF.Sign(target - current) * maxDelta;
        }
    }
    public static class Time { public static float time, deltaTime = .02f, fixedDeltaTime = .02f, timeScale = 1; public static int frameCount; }
    public struct Color { public static Color white => new(); }
    // AnimatorStateInfo stand-in: the fields the stuck-pose probe reads.
    public struct AnimatorStateInfo
    {
        public int shortNameHash, fullPathHash;
        public float normalizedTime;
    }
    // Animator stand-in for the stuck-pose probe: state hash, transition flag, Speed parameter and
    // the trigger writes are all script-controlled, so the tests can pin the capture gates, the
    // repair ladder and the trigger hygiene. Play and the enable toggle restart the state the way
    // the engine does, unless a test hook says otherwise.
    public class Animator : MonoBehaviour
    {
        public int TriggerCount, ResetCount, PlayCalls, EnabledWrites;
        public int StateHash, FullPathHash;
        public float NormalizedTime, Speed;
        public bool InTransition;
        public readonly List<string> Ops = new();
        // Hash-level trigger records (2026-09-25b pose contract): "set"/"reset" ops alone
        // cannot tell Land from PowerSlash, so every write also lands here in order.
        public readonly List<int> SetTriggers = new();
        public readonly List<int> ResetTriggers = new();
        public Func<int, int, float, bool> OnPlay;      // return false → the replay does not take
        public Action<bool> OnEnabledWrite;
        public Func<int, int, bool> OnHasState;         // (layer, stateId) → false simulates a controller without the state
        private bool animatorEnabled = true;

        public override bool enabled
        {
            get => animatorEnabled;
            set { animatorEnabled = value; EnabledWrites++; OnEnabledWrite?.Invoke(value); }
        }
        public static int StringToHash(string name) => name.GetHashCode();
        public void SetTrigger(int hash) { TriggerCount++; Ops.Add("set"); SetTriggers.Add(hash); }
        public void ResetTrigger(int hash) { ResetCount++; Ops.Add("reset"); ResetTriggers.Add(hash); }
        public bool HasState(int layerIndex, int stateID) => OnHasState?.Invoke(layerIndex, stateID) ?? true;
        public float GetFloat(int id) => Speed;
        public int NextStateHash;
        public RuntimeAnimatorController runtimeAnimatorController = new();
        public AnimatorStateInfo GetNextAnimatorStateInfo(int layer) =>
            new() { shortNameHash = NextStateHash, fullPathHash = FullPathHash, normalizedTime = NormalizedTime };
        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer) =>
            new() { shortNameHash = StateHash, fullPathHash = FullPathHash, normalizedTime = NormalizedTime };
        public void Play(int stateNameHash, int layer, float normalizedTime)
        {
            PlayCalls++;
            Ops.Add("play");
            if (OnPlay != null && !OnPlay(stateNameHash, layer, normalizedTime)) return;
            FullPathHash = stateNameHash;
            StateHash = stateNameHash == StringToHash("Base Layer.PowerSlash")
                ? StringToHash("PowerSlash") : stateNameHash;
            NormalizedTime = normalizedTime;
        }
    }
    public class TrailRenderer : Component { public bool enabled, emitting; public int positionCount, sortingLayerID, sortingOrder; public float time, widthMultiplier; }
    // Distinct Pointer per instance by default: the shared-controller adoption is per real
    // controller instance, so tests must opt into sharing instead of sharing by accident.
    public class RuntimeAnimatorController
    {
        private static long _next = 1;
        public RuntimeAnimatorController() { PointerValue = _next++; }
        public long PointerValue;
        public IntPtr Pointer => new IntPtr(PointerValue);
    }
    public class Collider2D : Component { }
    // 两维刚体替身：position 与 transform 同存储（物理移动会同步 transform），MovePosition 只
    // 记录下一物理步目标，由 ApplyPhysics（测试的物理步）消费；Blocked 模拟真实碰撞阻挡。
    public class Rigidbody2D : Component
    {
        public int MoveCalls;
        public bool Blocked, HasPending, simulated = true;
        public Vector2 PendingTarget;
        public Vector2 linearVelocity;
        public float SpeedCap;   // >0：每次物理步最多移动 SpeedCap*dt（模拟缓慢但持续前进）
        public Vector2 position
        {
            get { var p = transform.position; return new Vector2(p.x, p.y); }
            set { transform.position = new Vector3(value.x, value.y, transform.position.z); }
        }
        public void MovePosition(Vector2 target) { MoveCalls++; PendingTarget = target; HasPending = true; }
        public void ApplyPhysics(float dt)
        {
            if (!HasPending || dt <= 0f) return;
            HasPending = false;
            if (Blocked) { linearVelocity = new Vector2(0f, linearVelocity.y); return; }
            Vector2 previous = position;
            float dx = PendingTarget.x - previous.x, dy = PendingTarget.y - previous.y;
            if (SpeedCap > 0f)
            {
                float max = SpeedCap * dt;
                float length = MathF.Sqrt(dx * dx + dy * dy);
                if (length > max && length > 0f) { dx *= max / length; dy *= max / length; }
            }
            position = new Vector2(previous.x + dx, previous.y + dy);
            // MovePosition 只设定平移目标；vertical velocity 保持既有值。
            linearVelocity = new Vector2(dx / dt, linearVelocity.y);
        }
    }
    public enum CapsuleDirection2D { Vertical, Horizontal }
    // Name-set aware: the samurai's target scan must ask for Enemies alone while the shared hit
    // sweep keeps Wildlife, so those names have to be distinguishable bits.
    public static class LayerMask
    {
        public static int GetMask(params string[] names)
        {
            int mask = 0;
            foreach (var name in names)
            {
                if (name == "Enemies") mask |= 1;
                else if (name == "Wildlife") mask |= 2;
                else mask |= 4;
            }
            return mask;
        }
    }
    // 真几何过滤替身：把每个 collider 的 transform 位置当命中点，按点-胶囊距离判定（与
    // Physics2D.OverlapCapsuleNonAlloc 的 Horizontal/angle/size 语义一致），并记录最近一次
    // 查询参数供几何断言。输出参数只提供真实 interop 的原生 wrapper 签名（无 CLR 数组重载），
    // 查询原地写 wrapper[i]，与生产常驻 buffer 读的是同一实例。
    public static class Physics2D
    {
        public static int CapsuleScans;
        public static bool ThrowOnQuery;
        public static Vector2 LastCapsuleCenter, LastCapsuleSize;
        public static float LastCapsuleAngle;
        public static CapsuleDirection2D LastCapsuleDirection;
        public static int LastCapsuleMask, LastBufferLength;
        public static readonly HashSet<Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Collider2D>> Buffers = new();
        public static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Collider2D> LastBuffer;
        public static Collider2D[] Hits = Array.Empty<Collider2D>();
        // 每个 collider 被某次查询结果包含的次数（每次 query 至多 +1）：用于证明“同一目标被多次
        // 扫掠覆盖而命中仍只有一次”（每腿去重），而不是扫掠根本没再见过它。
        public static readonly Dictionary<Collider2D, int> ReturnCounts = new();
        public static int ReturnedCount(Collider2D collider) => ReturnCounts.TryGetValue(collider, out int n) ? n : 0;
        public static void Reset()
        {
            CapsuleScans = 0; ThrowOnQuery = false; LastCapsuleCenter = new Vector2(); LastCapsuleSize = new Vector2();
            LastCapsuleAngle = 0; LastCapsuleMask = 0; LastBufferLength = 0; LastBuffer = null;
            Buffers.Clear(); Hits = Array.Empty<Collider2D>(); ReturnCounts.Clear();
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Collider2D>.Conversions = 0;
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Collider2D>.Allocations = 0;
        }
        public static int OverlapCapsuleNonAlloc(Vector2 center, Vector2 size, CapsuleDirection2D direction,
            float angle, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Collider2D> output, int mask)
        {
            CapsuleScans++;
            if (ThrowOnQuery) throw new InvalidOperationException("test query failure");
            LastCapsuleCenter = center; LastCapsuleSize = size; LastCapsuleAngle = angle;
            LastCapsuleDirection = direction; LastCapsuleMask = mask;
            LastBufferLength = (int)output.Length;
            Buffers.Add(output);
            LastBuffer = output;
            int n = 0;
            foreach (var hit in Hits)
            {
                if (hit == null || !Inside(center, size, angle, hit.transform.position)) continue;
                if (n >= output.Length) break;
                ReturnCounts[hit] = ReturnedCount(hit) + 1;
                output[n++] = hit;
            }
            return n;
        }
        private static bool Inside(Vector2 center, Vector2 size, float angleDeg, Vector3 point)
        {
            float radius = size.y * .5f;
            float half = MathF.Max(0f, size.x * .5f - radius);
            float rad = angleDeg * Mathf.Deg2Rad;
            float dx = point.x - center.x, dy = point.y - center.y;
            float lx = dx * MathF.Cos(rad) + dy * MathF.Sin(rad);
            float ly = -dx * MathF.Sin(rad) + dy * MathF.Cos(rad);
            float cx = Math.Clamp(lx, -half, half);
            float ex = lx - cx, ey = ly;
            return ex * ex + ey * ey <= radius * radius + 1e-4f;
        }
    }
}
public class SpriteRendererFX
{
    public int GlowCount;
    public float LastDuration;
    public void GlowOverlay(UnityEngine.Color color, float duration) { GlowCount++; LastDuration = duration; }
}
public class Character : UnityEngine.Component
{
    public bool inert, grabbed, isStationary;
    public SpriteRendererFX spriteFX = new();
}
public enum DamageSource { Knight }
public class Damageable : UnityEngine.Component
{
    public bool invulnerable, isDead;
    public bool CanBeDamaged = true;
    public int HitCount, TotalDamage;
    public UnityEngine.GameObject LastAttacker;
    public DamageSource LastSource;
    public Action<Damageable> OnReceiveDamage;
    public bool IsDamagedBy(DamageSource source) => CanBeDamaged && !isDead;
    public void ReceiveDamage(int amount, UnityEngine.GameObject attacker, DamageSource source)
    { HitCount++; TotalDamage += amount; LastAttacker = attacker; LastSource = source; OnReceiveDamage?.Invoke(this); }
}
public class Scanner
{
    public UnityEngine.GameObject Closest;
    public int Calls;
    public UnityEngine.GameObject GetClosest() { Calls++; return Closest; }

    // The mod's static self-scan stand-in. Targets are registered per observer instance, the way
    // the replaced native per-knight scanner instance behaved, and the x geometry mirrors the
    // game's own ComputeCorners (front = range, behind = rangeBehind, facing from the observer's
    // scale sign): "rangeBehind = -1 only sees ahead" is therefore a red test, not a comment.
    // The y axis is not modelled -- every fixture unit stands at y = 0.
    public static readonly Dictionary<int, UnityEngine.GameObject> ScanTargets = new();
    public static int ScanCalls;
    public static int LastLayers;
    public static float LastRange, LastRangeBehind, LastHeight;
    public static bool LastExcludeDead;
    public static UnityEngine.GameObject ScanClosest(UnityEngine.Transform observer, float range, int layers,
        string[] tags = null, float rangeBehind = -1f, float height = .5f, bool excludeDead = false)
    {
        ScanCalls++;
        LastLayers = layers; LastRange = range; LastRangeBehind = rangeBehind;
        LastHeight = height; LastExcludeDead = excludeDead;
        if (!ScanTargets.TryGetValue(observer.gameObject.GetInstanceID(), out var target) || target == null) return null;
        float facing = observer.localScale.x < 0 ? -1f : 1f;
        float dx = (target.transform.position.x - observer.position.x) * facing;
        return dx > range || dx < -rangeBehind ? null : target;
    }
}
public class Embarkee
{
    public bool IsEmbarked, IsTargetingEmbarkable;
    public object EmbarkableTarget;
}
public class Formation { }
// Minimal documented queue semantics, not the Samurai policy.
public class StateMachine
{
    public int Current, _queuedState, Requests;
    public bool _executeQueuedState;
    public IntPtr Pointer = (IntPtr)123;
    public Action<int> OnEnter;
    public void GoToState(int state) { _queuedState = state; _executeQueuedState = true; Requests++; }
    public void Update()
    {
        if (!_executeQueuedState) return;
        Current = _queuedState; _executeQueuedState = false; OnEnter?.Invoke(Current);
    }
}
public class Kingdom { public bool isDaytime = true; }
// 世界身份替身：真实 2.4 的 World.Pointer + world.gameLayer(+gameObject.scene.handle) 是
// 换岛/重挂的身份依据（见 AutoRestockCounts 的同款四元组校验）。
public class World : UnityEngine.Object
{
    public UnityEngine.GameObject gameObject = new();
    public UnityEngine.Transform gameLayer;
    public World() { gameLayer = new UnityEngine.GameObject().transform; }
}
public static class PatchRoles_SamuraiNightFormation
{
    // 固定 0 锚：本套件里随从几乎总在场；无随从分支的真实三级兜底链（槽位→守位锚→
    // 自身位置）由 tests/samurai-night-formation 的 HomeXOf 单测覆盖，Out 相位随从死亡
    // 判别用例也以这个 0 锚作为确定性的回家目标。
    public static float HomeXOf(Knight k) => 0f;
}
public class Managers { public static Managers Inst = new(); public Kingdom kingdom = new(); public World world = new(); }
// Same shape as the game's global enum: the enemy side is the unit's own half.
public enum Side { Left = -1, Right = 1 }
public class Knight : UnityEngine.MonoBehaviour
{
    public static class State
    {
        public const int Stand = 0, GoToWall = 1, Assemble = 2, Charge = 3, GrabCoin = 4,
            GrabArmor = 5, InFormation = 6, MoveToEmbark = 7, MoveToPillar = 8, Stationary = 9;
    }
    public int Style = 2;
    public Side side = Side.Right;
    public bool Qualified = true, ControlRequested, _beingControlled, isRetreating, isCharging, _shouldCharge, _harmless;
    public UnityEngine.GameObject helPuzzlePillar;
    public Embarkee _embarkee = new();
    public Formation Formation;
    public Character _character;
    public Damageable _damageable;
    public Mover _mover;
    public UnityEngine.Animator _animator;
    public UnityEngine.TrailRenderer _trail;
    public Scanner _enemyScanner = new();
    public StateMachine _fsm = new();
    public int _attackDamage = 2;
    public float _runSpeed = 6, _cooldown;
    public bool ShouldPlayerControl() => ControlRequested;
    public Formation GetFormation() => Formation;
}
public class Archer : UnityEngine.MonoBehaviour
{
    public Knight _knight;
    public Damageable _damageable;
    public Character _character;
}
public class Mover : UnityEngine.Component
{
    public enum GoalMode { Off, Position, Object }
    // Mirrors the game enum order (Ahead, Left = -1, Right = 1, Target).
    public enum FacingMode { Ahead, Left = -1, Right = 1, Target }
    public GoalMode goalMode;
    public FacingMode facingMode;
    public UnityEngine.GameObject facingTarget;
    public int FacingWrites;
    public UnityEngine.GameObject _goalObject;
    public float _goalPosition, _goalSpeed, _pauseTimeout;
    // 真实 2.4 Mover.Update 开头两条计时语义的账务字段（被跳过函数的时间账务）。
    public float _multiplier = 1f, _multiplierTimeout;
    public bool movingToGoal, Blocked;
    public int GoalWrites, StopCalls;
    public Action OnStop;
    public float InvulnerableDistance;
    public void SetGoal(float position, float speed) => SetGoalNoHaglet(position, speed);
    public void SetGoalNoHaglet(float position, float speed)
    {
        goalMode = GoalMode.Position; _goalObject = null; _goalPosition = position; _goalSpeed = speed;
        movingToGoal = true; GoalWrites++;
    }
    public void SetFacingMode(FacingMode mode, UnityEngine.GameObject target = null)
    { facingMode = mode; facingTarget = mode == FacingMode.Target ? target : null; FacingWrites++; }
    public int DirectionWrites;
    public void SetDirection(int direction)
    { DirectionWrites++; transform.localScale = new(direction, 1f, 1f); } // Mirrors native API; callers must preserve the owned Y scale.
    public void Stop() { StopCalls++; goalMode = GoalMode.Off; movingToGoal = false; _goalSpeed = 0; OnStop?.Invoke(); }
    public void Step(float dt)
    {
        if (_pauseTimeout > 0) { _pauseTimeout = MathF.Max(0, _pauseTimeout - dt); return; }
        if (Blocked || goalMode != GoalMode.Position || dt <= 0) return;
        float x = transform.position.x;
        float dx = Math.Clamp(_goalPosition - x, -_goalSpeed * dt, _goalSpeed * dt);
        transform.position = new(x + dx, transform.position.y, transform.position.z);
        if (gameObject.GetComponent<Damageable>()?.invulnerable == true) InvulnerableDistance += MathF.Abs(dx);
        if (MathF.Abs(_goalPosition - transform.position.x) <= .0625f) movingToGoal = false;
        // Native Mover leaves Position mode and the goal fields in place when arriving.
    }
}
public static class NetworkBigBoss { public static bool HasWorldAuth = true; }
namespace KingdomEnhancedMod
{
    public static class ModConfig
    { public class BoolValue { public bool Value = true; } public static BoolValue Enabled = new(); }
    public static class PatchRoles_KnightStyle
    { public static bool TryGetResolvedStyleIndex(Knight knight, out int style) { style = knight.Style; return knight.Qualified; } }
    public static class UnitScanCache
    {
        public static Archer[] Archers = Array.Empty<Archer>();
        public static int Calls;
        public static Archer[] GetArchers(float interval = 3f) { Calls++; return Archers; }
    }
    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new();
        public Logger LogSource = new();
        public class Logger
        {
            public readonly List<string> Errors = new();
            public readonly List<string> Infos = new();
            public void LogError(string message) => Errors.Add(message);
            public void LogWarning(string message) { }
            public void LogInfo(string message) => Infos.Add(message);
        }
    }
}
