using System.Collections;

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
}
namespace BepInEx.Unity.IL2CPP.Utils.Collections
{
    public static class Extensions { public static IEnumerator WrapToIl2Cpp(this IEnumerator iterator) => iterator; }
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
        public GameObject() { Id = (int)Pointer; transform = new Transform { gameObject = this }; }
        public int GetInstanceID() => Id;
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; components.Add(c); return c; }
        public T GetComponent<T>() where T : class => components.OfType<T>().FirstOrDefault();
        public bool TryGetComponent<T>(out T component) where T : class { component = GetComponent<T>(); return component != null; }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public bool TryGetComponent<T>(out T component) where T : class => gameObject.TryGetComponent(out component);
    }
    public class MonoBehaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
        public Coroutine StartCoroutine(IEnumerator iterator) => Scheduler.Start(this, iterator);
        public void StopCoroutine(Coroutine coroutine) => Scheduler.StopSilently(coroutine);
        public void StopAllCoroutines() => Scheduler.StopOwnerSilently(this);
    }
    public class Transform : Component { public Vector3 position, localScale = Vector3.one; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y = 0, float z = 0) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new(1, 1, 1);
    }
    public static class Mathf
    {
        public static float Abs(float value) => MathF.Abs(value);
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Sign(float value) => value >= 0 ? 1 : -1;
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        public static bool Approximately(float a, float b) => MathF.Abs(a - b) < 0.00001f;
    }
    public static class Time { public static float time, deltaTime = .02f, timeScale = 1; public static int frameCount; }
    public struct Color { public static Color white => new(); }
    public class Animator : Component
    {
        public int TriggerCount;
        public static int StringToHash(string name) => name.GetHashCode();
        public void SetTrigger(int hash) => TriggerCount++;
    }
    public class TrailRenderer : Component { public bool enabled; }
    public class Collider2D : Component { }
    public static class LayerMask { public static int GetMask(params string[] names) => 1; }
    public static class Physics2D
    {
        public static int Scans;
        public static float LastRadius;
        public static readonly HashSet<Collider2D[]> Buffers = new();
        public static Collider2D[] Hits = Array.Empty<Collider2D>();
        public static int OverlapCircleNonAlloc(Vector3 position, float radius, Collider2D[] output, int mask)
        {
            Scans++;
            LastRadius = radius; Buffers.Add(output);
            int n = Math.Min(output.Length, Hits.Length);
            Array.Copy(Hits, output, n);
            return n;
        }
    }
    public class WaitForSeconds { public readonly float Seconds; public WaitForSeconds(float seconds) => Seconds = seconds; }
    public sealed class Coroutine
    {
        public IEnumerator Iterator;
        public MonoBehaviour Owner;
        public bool Active = true;
        public int LastFrame;
        public float ResumeAt;
    }
}

// Generic Unity-like scheduling only. This does not implement Samurai return decisions.
public static class Scheduler
{
    public static readonly List<UnityEngine.Coroutine> All = new();
    public static int Started;
    public static UnityEngine.Coroutine Start(UnityEngine.MonoBehaviour owner, IEnumerator iterator)
    {
        Started++;
        var routine = new UnityEngine.Coroutine { Owner = owner, Iterator = iterator };
        All.Add(routine);
        AdvanceOne(routine); // Unity starts immediately through the first yield.
        return routine;
    }
    public static void AdvanceOne(UnityEngine.Coroutine routine)
    {
        routine.LastFrame = UnityEngine.Time.frameCount;
        if (!routine.Iterator.MoveNext()) { routine.Active = false; return; }
        if (routine.Iterator.Current is UnityEngine.WaitForSeconds wait)
            routine.ResumeAt = UnityEngine.Time.time + wait.Seconds;
    }
    public static void Advance()
    {
        foreach (var routine in All.ToArray())
            if (routine.Active && routine.LastFrame != UnityEngine.Time.frameCount &&
                UnityEngine.Time.time >= routine.ResumeAt) AdvanceOne(routine);
    }
    public static void StopSilently(UnityEngine.Coroutine routine) { routine.Active = false; }
    public static void StopOwnerSilently(UnityEngine.MonoBehaviour owner)
    { foreach (var routine in All) if (ReferenceEquals(routine.Owner, owner)) routine.Active = false; }
    public static void Reset() { All.Clear(); Started = 0; }
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
public class Managers { public static Managers Inst = new(); public Kingdom kingdom = new(); }
public class Knight : UnityEngine.MonoBehaviour
{
    public static class State
    {
        public const int Stand = 0, GoToWall = 1, Assemble = 2, Charge = 3, GrabCoin = 4,
            GrabArmor = 5, InFormation = 6, MoveToEmbark = 7, MoveToPillar = 8, Stationary = 9;
    }
    public int Style = 2;
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
    public GoalMode goalMode;
    public UnityEngine.GameObject _goalObject;
    public float _goalPosition, _goalSpeed, _pauseTimeout;
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
            public void LogError(string message) => Errors.Add(message);
            public void LogWarning(string message) { }
            public void LogInfo(string message) { }
        }
    }
}
