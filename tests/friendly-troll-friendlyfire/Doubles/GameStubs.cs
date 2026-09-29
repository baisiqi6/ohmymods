using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 游戏原生类型 + 协作 API 替身（只够编译 PatchDivine_FriendlyTroll.cs 与本套件断言）。
//
// 离线模型声明（不是实机原生伤害执行）：
//   Damageable.damagedBy 是伤害来源位掩码（[Flags]，位4=BoulderFriendly、
//   位8=BoulderEnemy、位1=近战），IsDamagedBy = !invulnerable && 命中位。
//   actual 2.4 Boulder.HitObject 顺序 = IsDamagedBy -> ReceiveDamage；
//   ReceiveDamage 替身只模拟受理后的扣血/事件，自身不做 damagedBy 资格门。
//   资格 pipeline 与拒收分类由 tests 的 BoulderPipeline 显式模拟。
//   其余替身签名按生产文件使用面写，未逐字段与真实 interop 对照。
// ============================================================================

[Flags]
public enum DamageSource
{
    None = 0,
    Troll = 1,
    Arrow = 2,
    BoulderFriendly = 4,
    BoulderEnemy = 8,
    Knight = 16,
    Ogre = 32,
    Bolt = 64,
    PlayerSteed = 128,
    Pike = 256,
    Fire = 512,
    Stealer = 1024,
    Boar = 2048,
    Trap = 4096,
    Crusher = 8192,
    Fleet = 16384,
    GreedProjectile = 32768,
    SerpentAttack = 65536
}

public enum EnemyType
{
    TrollWeak = 0,
    Squid = 1
}

public sealed class Damageable : MonoBehaviour
{
    /// <summary>原生 OnReceiveDamage 事件委托的 interop 包装替身（带 System.Action 双向隐式转换）。</summary>
    public sealed class DamageEvent
    {
        internal readonly System.Action<int, GameObject, DamageSource> Handler;

        internal DamageEvent(System.Action<int, GameObject, DamageSource> handler)
        {
            Handler = handler;
        }

        public static implicit operator DamageEvent(
            System.Action<int, GameObject, DamageSource> handler)
        {
            return handler == null ? null : new DamageEvent(handler);
        }

        public static implicit operator System.Action<int, GameObject, DamageSource>(
            DamageEvent wrapper)
        {
            return wrapper == null ? null : wrapper.Handler;
        }
    }

    public bool isDead;
    private bool _invulnerable;
    public bool isInvulnerableInitially;

    /// <summary>
    /// 顺序探针：解除无敌瞬间记录当时 mask——正常路径必须已不含位4；
    /// 撤位失败时本 setter 根本不应被调用（InvulnerableWrites 不增）。
    /// </summary>
    public bool invulnerable
    {
        get { return _invulnerable; }
        set
        {
            InvulnerableWrites++;
            if (!value) MaskWhenInvulnerabilityDisabled = damagedBy;
            _invulnerable = value;
        }
    }

    public int InvulnerableWrites;
    public DamageSource MaskWhenInvulnerabilityDisabled;
    public int hitPoints = 100;
    public CRPCHeader parentHeaderRef;
    public int _invulnerableIndex = -1;

    private DamageSource _damagedBy;

    /// <summary>
    /// 原生 interop 为 get/set 属性。写尝试计数与可控抛错仅作探针：
    /// 「已毁不写 / setter 失败不伪造回执」用例据此断言。
    /// </summary>
    public DamageSource damagedBy
    {
        get { return _damagedBy; }
        set
        {
            DamagedByWriteAttempts++;
            if (ThrowOnDamagedByWrite)
                throw new InvalidOperationException("interop damagedBy write boom");
            _damagedBy = value;
        }
    }

    public int DamagedByWriteAttempts;
    public bool ThrowOnDamagedByWrite;

    /// <summary>夹具播种：绕过写计数（断言只统计被测代码的写入尝试）。</summary>
    public void SeedDamagedBy(DamageSource value)
    {
        _damagedBy = value;
    }

    internal readonly List<DamageEvent> Handlers = new List<DamageEvent>();

    public int ReceiveDamageCalls;
    public int LastDamageAmount;
    public DamageSource LastDamageSource;
    public GameObject LastDamager;
    public readonly List<DamageSource> ReceivedSources = new List<DamageSource>();

    /// <summary>拒收分类观察袋：由 tests 的 BoulderPipeline 在 IsDamagedBy=false 时登记；本替身自身不写。</summary>
    public readonly List<DamageSource> RejectedSources = new List<DamageSource>();

    public void add_OnReceiveDamage(DamageEvent handler)
    {
        Handlers.Add(handler);
    }

    public void remove_OnReceiveDamage(DamageEvent handler)
    {
        Handlers.RemoveAll(entry => ReferenceEquals(entry, handler));
    }

    public bool IsDamagedBy(DamageSource source)
    {
        if (invulnerable) return false;
        return (damagedBy & source) != 0;
    }

    /// <summary>
    /// 受理后的最小伤害执行：扣 HP + 通知订阅者。
    /// 资格门不属于本方法：actual 2.4 由 Boulder.HitObject 先 IsDamagedBy 再提交
    /// ReceiveDamage；tests 用 BoulderPipeline 显式模拟该顺序（离线，不是实机执行）。
    /// </summary>
    public void ReceiveDamage(int amount, GameObject damager, DamageSource source)
    {
        ReceiveDamageCalls++;
        LastDamageAmount = amount;
        LastDamageSource = source;
        LastDamager = damager;
        ReceivedSources.Add(source);
        hitPoints -= amount;
        if (hitPoints <= 0)
        {
            hitPoints = 0;
            isDead = true;
        }

        for (int i = 0; i < Handlers.Count; i++)
        {
            Handlers[i].Handler?.Invoke(amount, damager, source);
        }
    }
}

public sealed class Squid : MonoBehaviour
{
    public void OnEnable()
    {
    }

    public void OnDisable()
    {
    }
}

public sealed class EnemyManager : MonoBehaviour
{
    public Il2CppSystem.Collections.Generic.List<Squid> AllEnemies =
        new Il2CppSystem.Collections.Generic.List<Squid>();
}

public sealed class TargetCacher : MonoBehaviour
{
    /// <summary>interop 委托包装替身（生产只比较 .Pointer，不 Invoke）。</summary>
    public sealed class SearchConditionDelegate
    {
        public IntPtr Pointer { get; } = new IntPtr(0x10000);
    }

    public Il2CppSystem.Collections.Generic.List<Damageable> _trollPriorityTargets =
        new Il2CppSystem.Collections.Generic.List<Damageable>();

    public Il2CppSystem.Collections.Generic.List<Damageable> _trollLowPriorityTargets =
        new Il2CppSystem.Collections.Generic.List<Damageable>();

    public void RegisterPriorityTarget(Damageable damageable)
    {
    }

    public void DeregisterPriorityTarget(Damageable damageable)
    {
    }

    public Damageable GetClosestPriorityTargetWithinRange(float pos, float range,
        SearchConditionDelegate conditionDelegate = null,
        SearchConditionDelegate ignoreDelegate = null)
    {
        return null;
    }
}

public sealed class Petrifiable : MonoBehaviour
{
    public bool IsPetrified;
}

public sealed class Mover : MonoBehaviour
{
    public bool movingToGoal;

    public bool IsPaused()
    {
        return false;
    }

    public void SetSpeed(float speed, int direction)
    {
    }
}

public sealed class Troll : MonoBehaviour
{
    public EnemyType Type;
    public bool shouldRetreat;
    public bool IsDespawning;
    public object loot;
    public object _unitController;
    public bool _shouldCharge;
    public Damageable damageable;
    public Petrifiable _petrifiable;
    public Coatsink.Common.Haglet _behaviour;
    public Mover _mover;
    public DamageSource damageSource = DamageSource.Troll;
    public float chargeRange;
    public float runSpeed;
    public float walkSpeed;
    public float _currentWalkSpeed;
    public int waveType;
    public float _waveTargetOffset;
    public TargetCacher.SearchConditionDelegate TargetPrioritySearchConditionDelegate;
    public TargetCacher.SearchConditionDelegate TargetIgnoreSearchConditionDelegate;

    public float GetWaveSpeedMultiplier(float x, int waveType, out float targetX,
        float waveTargetOffset)
    {
        targetX = x;
        return 1f;
    }

    public void OnEnable()
    {
    }

    public void OnDisable()
    {
    }

    public void ApplyData()
    {
    }

    public void HandleAuthorityChange(bool newAuthorityState)
    {
    }
}

public sealed class FriendlyTroll : MonoBehaviour
{
    public StateMachine _fsm;
    public float _runSpeed;
    public float _maxAttackDistance;
    public Damageable _target;

    public void Init()
    {
    }

    public void ApplyData()
    {
    }

    public void DeserializeFromData()
    {
    }

    public void ResetAndDespawn()
    {
    }
}

public sealed class StateMachine : MonoBehaviour
{
    public void StepCoroutine()
    {
    }
}

public sealed class EnemyBlueprint : MonoBehaviour
{
    public EnemyType type;

    public Enemy Instantiate()
    {
        return null;
    }
}

public sealed class Enemy : MonoBehaviour
{
}

public sealed class CRPCHeader
{
    public short NetID;
}

public sealed class GlobalSaveData
{
    public static GlobalSaveData loaded;
    public int currentCampaign;
    public int currentChallenge;
}

public sealed class CampaignSaveData
{
    public static CampaignSaveData current;
    public IslandSaveData CurrentIsland;
    public int CurrentLand;
    public int reign;
}

public sealed class IslandSaveData
{
    public DateTime realStartDateTime;
}

public sealed class NetworkPostbox
{
    public static NetworkPostbox Instance;
    public short NextNetId = 1;

    public CRPCHeader GetHeaderFromDynamicObject(GameObject gameObject, bool includeDynamic)
    {
        return null;
    }
}

public sealed class Game
{
    public enum State
    {
        Menu = 0,
        Playing = 1
    }

    public State state = State.Playing;
}

public sealed class World : MonoBehaviour
{
}

public sealed class Managers
{
    public static Managers Inst;
    public World world;
    public EnemyManager enemies;
    public Game game;
}

namespace Coatsink.Common
{
    public sealed class Haglet : UnityEngine.MonoBehaviour
    {
        public int latestGoto;
    }
}

// ============================================================================
// 协作 API 替身（真实实现在 canonical il2cpp/ 下；本套件只驱动用到的成员）。
// ============================================================================

namespace KingdomEnhancedMod
{
    /// <summary>BepInEx ConfigEntry&lt;bool&gt; 的最小形状（只用到 .Value）。</summary>
    public sealed class ConfigEntry<T>
    {
        public T Value;
    }

    public static class ModConfig
    {
        public static ConfigEntry<bool> Enabled = new ConfigEntry<bool> { Value = true };
    }

    public static class NetworkBigBoss
    {
        public static bool HasWorldAuth;
        public static bool IsClientPresent;
        public static bool HasClientCaughtUp;
    }

    internal sealed class LogSink
    {
        internal readonly List<string> Lines = new List<string>();

        /// <summary>人工故障注入：日志 sink 抛错，验证诊断异常不影响责任处置。</summary>
        public bool ThrowOnLogError;

        public void LogError(string message)
        {
            if (ThrowOnLogError) throw new InvalidOperationException("log sink boom");
            Lines.Add("E:" + message);
        }

        public void LogWarning(string message)
        {
            Lines.Add("W:" + message);
        }

        public void LogInfo(string message)
        {
            Lines.Add("I:" + message);
        }
    }

    internal sealed class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance;
        internal readonly LogSink LogSource = new LogSink();
    }

    /// <summary>
    /// FriendlyTrollDisguise 的布尔替身（该模块由 friendly-troll-disguise 套件
    /// 独立测试；本套件只关心它是否返回“受保护”，默认 false）。
    /// </summary>
    internal static class FriendlyTrollDisguise
    {
        internal static bool Protected;

        internal static bool IsProtected(FriendlyTroll troll)
        {
            return Protected && troll != null;
        }
    }
}
