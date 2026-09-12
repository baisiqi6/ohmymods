using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 死地骑士强化：死地风格（style==1）骑士及其随从（_knight 指向合格骑士，按队籍）
/// 攻击节奏 ×2、移动 ×1.5；独立弩手（_knight==null）不受影响。
/// 全部写入为每调用临时作用域（Harmony __state）或有界动画加速（注入观察者组件），
/// 无持久字段污染。玩法钩子门控 配置开 + 世界权威；恢复路径不受配置门。
/// 细节/限制见 DEADLANDS-RESULT.md。
/// </summary>
public static class PatchRoles_DeadlandsPowers
{
    internal const int DeadlandsStyleIndex = 1;
    internal const float MoveScale = 1.5f;
    private const float AnimCaptureSeconds = 0.3f;
    private const float AnimTimeoutSeconds = 2.5f;

    private static readonly int HashSlash = Animator.StringToHash("Slash");
    private static readonly int HashShoot = Animator.StringToHash("Shoot");
    private static readonly int HashShootPerfect = Animator.StringToHash("ShootPerfect");
    private static readonly int HashAttack = Animator.StringToHash("Attack");

    private static readonly HashSet<string> LoggedErrors = new();
    private static bool _loggedFirstBoost;

    internal static void LogOnce(string key, Exception e)
    {
        if (!LoggedErrors.Add(key)) return;
        KingdomEnhancedPlugin.Instance?.LogSource.LogError("[DeadlandsPowers/" + key + "] " + e);
    }

    internal static bool ConfigEnabled()
    {
        try { return ModConfig.Enabled != null && ModConfig.Enabled.Value; }
        catch { return false; }
    }

    internal static bool GameplayActive() => ConfigEnabled() && NetworkBigBoss.HasWorldAuth;

    // ============================================================
    // 资格判定（每调用重算）
    // ============================================================

    internal static bool IsDeadlandsKnight(Knight knight)
    {
        return knight != null
            && PatchRoles_KnightStyle.TryGetResolvedStyleIndex(knight, out int style)
            && style == DeadlandsStyleIndex;
    }

    internal static bool IsDeadlandsFollower(Archer archer)
    {
        if (archer == null) return false;
        Knight knight = archer._knight;
        return knight != null && IsDeadlandsKnight(knight);
    }

    // ============================================================
    // 单位注册表：仅 Knight/Archer（其 Update prefix 注册），键 = mover 指针 /
    // animator 所在 GO id；Mover/动画触发 O(1) 查找，无 GetComponent/场景扫描。
    // OnDisable 按指针比对移除（防 instanceID/指针复用误删）。
    // ============================================================

    internal sealed class UnitRef
    {
        internal Knight Knight;
        internal Archer Archer;
        internal IntPtr UnitPtr;
        internal IntPtr? MoverKey;
        internal int? AnimKey;
        internal IntPtr ShootKey;
        internal Coatsink.Common.Haglet Shoot;
    }

    private static readonly Dictionary<IntPtr, UnitRef> ByUnit = new();
    internal static readonly Dictionary<IntPtr, UnitRef> ByMover = new();
    internal static readonly Dictionary<int, UnitRef> ByAnimatorGo = new();

    private static void AttachKeys(UnitRef u, Mover mover, Animator animator)
    {
        if (mover != null)
        {
            IntPtr p = mover.Pointer;
            if (u.MoverKey != p)
            {
                if (u.MoverKey.HasValue
                    && ByMover.TryGetValue(u.MoverKey.Value, out UnitRef old) && old.UnitPtr == u.UnitPtr)
                    ByMover.Remove(u.MoverKey.Value);
                u.MoverKey = p;
            }
            if (!ByMover.TryGetValue(p, out UnitRef cur) || cur.UnitPtr != u.UnitPtr) ByMover[p] = u;
        }
        if (animator != null && animator.gameObject != null)
        {
            int id = animator.gameObject.GetInstanceID();
            if (u.AnimKey != id)
            {
                if (u.AnimKey.HasValue
                    && ByAnimatorGo.TryGetValue(u.AnimKey.Value, out UnitRef old) && old.UnitPtr == u.UnitPtr)
                    ByAnimatorGo.Remove(u.AnimKey.Value);
                u.AnimKey = id;
            }
            if (!ByAnimatorGo.TryGetValue(id, out UnitRef cur) || cur.UnitPtr != u.UnitPtr) ByAnimatorGo[id] = u;
        }
    }

    internal static void Register(Knight knight)
    {
        if (knight == null || knight.gameObject == null) return;
        IntPtr p = knight.Pointer;
        if (!ByUnit.TryGetValue(p, out UnitRef u) || u.Knight != knight)
        {
            u = new UnitRef { Knight = knight, UnitPtr = p };
            ByUnit[p] = u;
        }
        AttachKeys(u, knight._mover, knight._animator);
    }

    internal static void Register(Archer archer)
    {
        if (archer == null || archer.gameObject == null) return;
        IntPtr p = archer.Pointer;
        if (!ByUnit.TryGetValue(p, out UnitRef u) || u.Archer != archer)
        {
            u = new UnitRef { Archer = archer, UnitPtr = p };
            ByUnit[p] = u;
        }
        AttachKeys(u, archer._mover, archer._animator);
        var shoot = archer.shoot;
        IntPtr shootKey = shoot != null ? shoot.Pointer : IntPtr.Zero;
        if (u.ShootKey != shootKey)
        {
            u.ShootKey = shootKey;
            u.Shoot = shoot != null ? shoot.Cast<Coatsink.Common.Haglet>() : null;
        }
    }

    internal static bool IsShooting(Archer archer)
    {
        return ByUnit.TryGetValue(archer.Pointer, out UnitRef u) && u.Shoot != null && u.Shoot.started;
    }

    private static void Unregister(IntPtr unitPtr, GameObject go)
    {
        try
        {
            ForceRestoreObserver(go);
            if (!ByUnit.TryGetValue(unitPtr, out UnitRef u)) return;
            ByUnit.Remove(unitPtr);
            if (u.MoverKey.HasValue
                && ByMover.TryGetValue(u.MoverKey.Value, out UnitRef m) && m.UnitPtr == unitPtr)
                ByMover.Remove(u.MoverKey.Value);
            if (u.AnimKey.HasValue
                && ByAnimatorGo.TryGetValue(u.AnimKey.Value, out UnitRef a) && a.UnitPtr == unitPtr)
                ByAnimatorGo.Remove(u.AnimKey.Value);
        }
        catch (Exception e)
        {
            LogOnce("unregister failed", e);
        }
    }

    internal static void OnKnightDisabled(Knight knight)
    {
        if (knight == null) return;
        try
        {
            if (ActiveSlash.TryGetValue(knight.Pointer, out SlashLease lease)) RetireSlash(lease);
        }
        catch (Exception e) { LogOnce("slash guard cleanup failed", e); }
        if (knight._animator != null) ForceRestoreObserver(knight._animator.gameObject);
        Unregister(knight.Pointer, knight.gameObject);
    }

    internal static void OnArcherDisabled(Archer archer)
    {
        if (archer == null) return;
        if (archer._animator != null) ForceRestoreObserver(archer._animator.gameObject);
        Unregister(archer.Pointer, archer.gameObject);
    }

    // ============================================================
    // Slash 重入守卫：同一骑士同时只允许一个活跃 Slash 枚举器
    // （半窗后雕像/减冷却 buff 下两个 Slash 协程可能重叠，共享 _hitObjects）。
    // state==0 时注册；每次 MoveNext 更新 scaled Time.time 心跳。
    // 2 秒无心跳允许下一次 Slash 接管，但先终止旧枚举器；暂停不消耗租期。
    // 强引用 wrapper 持有 Il2CppInterop 的强 GC handle，不从裸指针重建对象。
    // false/异常/OnDisable 终止并移除，配置/风格/权威变化不提前撤销活跃守卫。
    // ============================================================

    internal const float SlashLeaseSeconds = 2f;

    internal sealed class SlashLease
    {
        internal readonly IntPtr KnightPtr;
        internal readonly Knight._Slash_d__168 Iterator;
        internal float Heartbeat;
        internal bool Retired;

        internal SlashLease(IntPtr knightPtr, Knight._Slash_d__168 iterator)
        {
            KnightPtr = knightPtr;
            Iterator = iterator;
            Heartbeat = Time.time;
        }
    }

    // 每骑士至多一个持久强引用；无扫描、墓碑或过期宽限期。
    internal static readonly Dictionary<IntPtr, SlashLease> ActiveSlash = new();

    internal static void RetireSlash(SlashLease lease)
    {
        // __state 持有当次 lease：原生 MoveNext 中重入 OnDisable 后，即使原生
        // 又写回 state=1，postfix 仍可看到 Retired 并再次强制终止。
        lease.Retired = true;
        lease.Iterator.__1__state = -1;
        if (ActiveSlash.TryGetValue(lease.KnightPtr, out SlashLease current)
            && ReferenceEquals(current, lease))
            ActiveSlash.Remove(lease.KnightPtr);
    }

    // ============================================================
    // 攻击动画 ×2（本地观感）：保存的正速 ×2（暂停 speed<=0 不动），仅在
    // 速度仍等于我们的提升值时恢复（外部冻结/buff 改速不被覆盖）。状态跟踪：
    ///  触发后有限窗口捕获新状态 hash，状态退出 / normalizedTime>=1 / 超时恢复。
    // 配置关/风格丢失/池禁用立即恢复。观察者为注入组件，只挂在被加速的 actor 上。
    // ============================================================

    private static bool _observerRegistered;

    private static void EnsureObserverRegistered()
    {
        if (_observerRegistered) return;
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(DeadlandsAnimObserver)))
            ClassInjector.RegisterTypeInIl2Cpp(typeof(DeadlandsAnimObserver));
        _observerRegistered = true;
    }

    internal static void ObserverTick(DeadlandsAnimObserver o)
    {
        try
        {
            Animator a = o.Animator;
            if (a == null || !ConfigEnabled()
                || (o.Knight != null && !IsDeadlandsKnight(o.Knight))
                || (o.Archer != null && !IsDeadlandsFollower(o.Archer))
                || Time.time >= o.Deadline)
            {
                RestoreObserver(o);
                return;
            }
            if (!o.Captured)
            {
                if (Time.time - o.TriggerTime > AnimCaptureSeconds)
                {
                    RestoreObserver(o); // 未切换状态：不白加速非攻击动画
                    return;
                }
                int h = a.GetCurrentAnimatorStateInfo(0).shortNameHash;
                if (h != o.PreTriggerStateHash)
                {
                    o.AttackStateHash = h;
                    o.Captured = true;
                }
                return;
            }
            AnimatorStateInfo st = a.GetCurrentAnimatorStateInfo(0);
            if (st.shortNameHash != o.AttackStateHash || st.normalizedTime >= 1f)
                RestoreObserver(o);
        }
        catch (Exception e)
        {
            RestoreObserver(o);
            LogOnce("observer tick failed", e);
        }
    }

    private static void RestoreObserver(DeadlandsAnimObserver o)
    {
        try
        {
            if (o != null && o.Animator != null && o.Animator.speed == o.BoostedSpeed)
                o.Animator.speed = o.OriginalSpeed;
            if (o != null)
            {
                o.Boosting = false;
                o.Captured = false;
                o.enabled = false;
            }
        }
        catch (Exception e)
        {
            LogOnce("observer restore failed", e);
        }
    }

    private static void ForceRestoreObserver(GameObject go)
    {
        if (go == null) return;
        try
        {
            EnsureObserverRegistered();
            DeadlandsAnimObserver o = go.GetComponent<DeadlandsAnimObserver>();
            if (o != null) RestoreObserver(o);
        }
        catch (Exception e)
        {
            LogOnce("observer force restore failed", e);
        }
    }

    /// <summary>加速入口（三个触发钩子共用）：Animator 正速 ×2；已加速时只刷新
    /// 捕获窗/截止（原速沿用首建时保存值）。非攻击触发 → 立即恢复。</summary>
    internal static void HandleAnimTrigger(Animator animator, int animTrigger, UnitRef owner)
    {
        if (!ConfigEnabled() || animator == null) return;
        try
        {
            bool isAttack = animTrigger == HashSlash || animTrigger == HashShoot
                || animTrigger == HashShootPerfect || animTrigger == HashAttack;
            if (!isAttack)
            {
                ForceRestoreObserver(animator.gameObject);
                return;
            }
            if (owner == null) return;
            if (owner.Knight != null && !IsDeadlandsKnight(owner.Knight)) return;
            if (owner.Archer != null && !IsDeadlandsFollower(owner.Archer)) return;

            float speed = animator.speed;
            if (speed <= 0f) return; // 暂停/冻结保持

            EnsureObserverRegistered();
            GameObject go = animator.gameObject;
            DeadlandsAnimObserver o = go.GetComponent<DeadlandsAnimObserver>();
            if (o == null)
            {
                o = go.AddComponent<DeadlandsAnimObserver>();
            }
            // A new native/buff speed supersedes the previous saved speed.
            // Rebase the next attack rather than overwriting it with our stale boost.
            if (o.Boosting && speed != o.BoostedSpeed) o.Boosting = false;
            if (!o.Boosting)
            {
                o.Animator = animator;
                o.Knight = owner.Knight;
                o.Archer = owner.Archer;
                o.OriginalSpeed = speed;
                o.BoostedSpeed = speed * 2f;
                if (!_loggedFirstBoost)
                {
                    _loggedFirstBoost = true;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        "[DeadlandsPowers] attack animation boost active (2x, bounded)");
                }
            }
            o.Boosting = true;
            o.enabled = true;
            o.Captured = false;
            o.AttackStateHash = 0;
            o.PreTriggerStateHash = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            o.TriggerTime = Time.time;
            o.Deadline = Time.time + AnimTimeoutSeconds;
            animator.speed = o.BoostedSpeed;
        }
        catch (Exception e)
        {
            LogOnce("anim trigger handling failed", e);
        }
    }

    // ============================================================
    // 临时作用域数据（Harmony __state）
    // ============================================================

    internal readonly struct MoverBoost
    {
        internal readonly bool Applied;
        internal readonly IntPtr MoverPtr;
        internal readonly float GoalSpeed;
        internal readonly float MoveSpeed;
        internal readonly Mover.GoalMode GoalMode;
        internal MoverBoost(bool applied, IntPtr moverPtr, float goalSpeed, float moveSpeed, Mover.GoalMode goalMode)
        { Applied = applied; MoverPtr = moverPtr; GoalSpeed = goalSpeed; MoveSpeed = moveSpeed; GoalMode = goalMode; }
    }

    internal readonly struct ShootBoost
    {
        internal readonly bool Applied;
        internal readonly IntPtr ArcherPtr;
        internal readonly float PrepTime;
        internal readonly Vector2 Interval;
        internal readonly Vector2 IntervalFormation;
        internal ShootBoost(bool applied, IntPtr archerPtr, float prepTime, Vector2 interval, Vector2 intervalFormation)
        { Applied = applied; ArcherPtr = archerPtr; PrepTime = prepTime; Interval = interval; IntervalFormation = intervalFormation; }
    }
}

/// <summary>攻击动画加速观察者（注入组件，仅挂在被加速的 actor 上）：
/// 每帧 O(1) 状态检查，状态退出/超时/资格丢失恢复原速并禁用；下次攻击复用。</summary>
public sealed class DeadlandsAnimObserver : MonoBehaviour
{
    internal Animator Animator;
    internal Knight Knight;
    internal Archer Archer;
    internal float OriginalSpeed;
    internal float BoostedSpeed;
    internal int PreTriggerStateHash;
    internal int AttackStateHash;
    internal bool Captured;
    internal bool Boosting;
    internal float TriggerTime;
    internal float Deadline;

    public DeadlandsAnimObserver(IntPtr pointer) : base(pointer) { }

    private void Update() => PatchRoles_DeadlandsPowers.ObserverTick(this);
}

// ============================================================
// Knight.Update prefix：注册 + _cooldown 额外衰减（原生减 1 份 + 本 1 份 = ×2；
// 先于原方法执行，避免减到本帧新建的冷却）
// ============================================================

[HarmonyPatch(typeof(Knight), "Update")]
public static class Knight_Update_DeadlandsCadence_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Knight __instance)
    {
        if (__instance == null || !PatchRoles_DeadlandsPowers.GameplayActive()) return;
        try
        {
            PatchRoles_DeadlandsPowers.Register(__instance);
            if (PatchRoles_DeadlandsPowers.IsDeadlandsKnight(__instance) && __instance._cooldown > 0f)
                __instance._cooldown -= Time.deltaTime;
        }
        catch (Exception e)
        {
            PatchRoles_DeadlandsPowers.LogOnce("knight update prefix failed", e);
        }
    }
}

// ============================================================
// Archer.Update prefix：注册 + 门控额外衰减——ShouldPlayerControl() 或
// shoot.started（协程运行期 _cooldown=5f 哨兵）时跳过；终段射击冷却只在
// 协程结束后享受 ×2 衰减
// ============================================================

[HarmonyPatch(typeof(Archer), "Update")]
public static class Archer_Update_DeadlandsCadence_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Archer __instance)
    {
        if (__instance == null || !PatchRoles_DeadlandsPowers.GameplayActive()) return;
        try
        {
            PatchRoles_DeadlandsPowers.Register(__instance);
            if (__instance._knight == null) return;
            if (!PatchRoles_DeadlandsPowers.IsDeadlandsFollower(__instance)) return;
            if (PlayerControlled(__instance)) return;
            if (PatchRoles_DeadlandsPowers.IsShooting(__instance)) return;
            if (__instance._cooldown > 0f)
                __instance._cooldown -= Time.deltaTime;
        }
        catch (Exception e)
        {
            PatchRoles_DeadlandsPowers.LogOnce("archer update prefix failed", e);
        }
    }

    private static bool PlayerControlled(Archer archer)
    {
        try { return archer.ShouldPlayerControl(); }
        catch (Exception e)
        {
            PatchRoles_DeadlandsPowers.LogOnce("player-control probe failed", e);
            return true; // 无法判定时保守跳过衰减
        }
    }
}

// ============================================================
// Archer._Shoot_d__225.MoveNext prefix+finalizer：调用期内 prep/interval ×0.5
// （构造原生 Wait 时生效）；finalizer 只还原自己的 __state（配置/风格变更/原生
// 异常都照样还原）。终段 _cooldown 不缩（Update ×2 衰减覆盖，避免双重计）
// ============================================================

[HarmonyPatch(typeof(Archer._Shoot_d__225), "MoveNext")]
public static class Archer_ShootCoroutine_DeadlandsInterval_Patch
{
    [HarmonyPrefix]
    private static void Prefix(ref PatchRoles_DeadlandsPowers.ShootBoost __state, Archer._Shoot_d__225 __instance)
    {
        __state = default;
        if (__instance == null || !PatchRoles_DeadlandsPowers.GameplayActive()) return;
        try
        {
            Archer archer = __instance.__4__this;
            if (archer == null || !PatchRoles_DeadlandsPowers.IsDeadlandsFollower(archer)) return;
            __state = new PatchRoles_DeadlandsPowers.ShootBoost(true, archer.Pointer,
                archer.shootPrepTime, archer._shootIntervalRange, archer._shootIntervalRangeFormation);
            archer.shootPrepTime *= 0.5f;
            archer._shootIntervalRange *= 0.5f;
            archer._shootIntervalRangeFormation *= 0.5f;
        }
        catch (Exception e)
        {
            PatchRoles_DeadlandsPowers.LogOnce("shoot prefix failed", e);
        }
    }

    [HarmonyFinalizer]
    private static void Finalizer(PatchRoles_DeadlandsPowers.ShootBoost __state, Exception __exception, Archer._Shoot_d__225 __instance)
    {
        if (!__state.Applied) return;
        try
        {
            Archer archer = __instance != null ? __instance.__4__this : null;
            if (archer != null && archer.Pointer == __state.ArcherPtr)
            {
                archer.shootPrepTime = __state.PrepTime;
                archer._shootIntervalRange = __state.Interval;
                archer._shootIntervalRangeFormation = __state.IntervalFormation;
            }
        }
        catch (Exception e)
        {
            PatchRoles_DeadlandsPowers.LogOnce("shoot finalizer failed", e);
        }
    }
}

// ============================================================
// Knight._Slash_d__168.MoveNext：
// prefix：已终止枚举器直接 false；state==0 注册/接管，活跃 owner 每调用心跳。
// 接管先将旧 owner 标为终止，迟到 MoveNext 不得恢复共享 _hitObjects 写入。
// postfix 用 __state 检查本次 lease 是否在原生调用中被禁用/接管。
// postfix：_elapsedTime_5__4 额外 += dt（命中窗口实际时长减半；伤害按
// _hitObjects 去重不变；精确 2x 受帧率/守卫门限制，有界）
// ============================================================

[HarmonyPatch(typeof(Knight._Slash_d__168), "MoveNext")]
public static class Knight_SlashCoroutine_Deadlands_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result, Knight._Slash_d__168 __instance,
        out PatchRoles_DeadlandsPowers.SlashLease __state)
    {
        __state = null;
        try
        {
            if (__instance == null) return true;
            if (__instance.__1__state == -1)
            {
                __result = false;
                return false;
            }
            Knight knight = __instance.__4__this;
            if (knight == null) return true;

            IntPtr knightPtr = knight.Pointer;
            if (PatchRoles_DeadlandsPowers.ActiveSlash.TryGetValue(knightPtr,
                out PatchRoles_DeadlandsPowers.SlashLease owner))
            {
                if (owner.Iterator.Pointer == __instance.Pointer)
                {
                    __state = owner;
                    if (owner.Retired)
                    {
                        __instance.__1__state = -1;
                        __result = false;
                        return false;
                    }
                    owner.Heartbeat = Time.time;
                    return true;
                }
                // 不接管或取消未注册、已经在运行的 vanilla 枚举器。
                if (__instance.__1__state != 0) return true;
                if (Time.time - owner.Heartbeat < PatchRoles_DeadlandsPowers.SlashLeaseSeconds)
                {
                    __result = false;
                    __instance.__1__state = -1;
                    if (!_loggedOverlap)
                    {
                        _loggedOverlap = true;
                        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                            "[DeadlandsPowers] overlapping Slash suppressed (shared _hitObjects guard)");
                    }
                    return false;
                }
                // 即使新枚举器已不合资格，也必须先永久终止过期的旧 owner。
                PatchRoles_DeadlandsPowers.RetireSlash(owner);
            }
            if (__instance.__1__state != 0) return true;
            if (PatchRoles_DeadlandsPowers.GameplayActive()
                && PatchRoles_DeadlandsPowers.IsDeadlandsKnight(knight))
            {
                __state = new PatchRoles_DeadlandsPowers.SlashLease(knightPtr, __instance);
                PatchRoles_DeadlandsPowers.ActiveSlash[knightPtr] = __state;
            }
            return true;
        }
        catch (Exception e)
        {
            PatchRoles_DeadlandsPowers.LogOnce("slash guard prefix failed", e);
            return true;
        }
    }

    private static bool _loggedOverlap;

    [HarmonyPostfix]
    private static void Postfix(ref bool __result, Knight._Slash_d__168 __instance,
        PatchRoles_DeadlandsPowers.SlashLease __state)
    {
        try
        {
            if (__state != null && __state.Retired)
            {
                __state.Iterator.__1__state = -1;
                __result = false;
                return;
            }
            if (!__result)
            {
                if (__state != null) PatchRoles_DeadlandsPowers.RetireSlash(__state);
                return;
            }
            if (__instance != null && PatchRoles_DeadlandsPowers.GameplayActive())
            {
                Knight knight = __instance.__4__this;
                if (knight != null && PatchRoles_DeadlandsPowers.IsDeadlandsKnight(knight))
                    __instance._elapsedTime_5__4 += Time.deltaTime;
            }
        }
        catch (Exception e)
        {
            PatchRoles_DeadlandsPowers.LogOnce("slash postfix failed", e);
        }
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception,
        PatchRoles_DeadlandsPowers.SlashLease __state)
    {
        if (__exception != null && __state != null)
        {
            try
            {
                PatchRoles_DeadlandsPowers.RetireSlash(__state);
            }
            catch { }
        }
        return __exception;
    }
}

// ============================================================
// Mover.Update prefix+finalizer：合格注册单位移动 ×1.5（__state 作用域）。
// 恢复条件：_goalSpeed 仍等于提升值（goal-reached 回调 SetGoal/SetSpeed 新值
// 不被覆盖）；goalMode 变化（SetSpeed→Off）时不除回 _moveSpeed。
// _multiplier 原生 buff 乘法天然复合，无漂移。
// ============================================================

[HarmonyPatch(typeof(Mover), "Update")]
public static class Mover_Update_DeadlandsSpeed_Patch
{
    [HarmonyPrefix]
    private static void Prefix(ref PatchRoles_DeadlandsPowers.MoverBoost __state, Mover __instance)
    {
        __state = default;
        // Resolve owned wall-follow offsets before this prefix temporarily boosts
        // goal speed. The guard compares the unmodified native writer tuple.
        SquadFollowGuard.BeforeMoverUpdate(__instance);
        if (__instance == null || !PatchRoles_DeadlandsPowers.GameplayActive()) return;
        try
        {
            if (!PatchRoles_DeadlandsPowers.ByMover.TryGetValue(__instance.Pointer,
                out PatchRoles_DeadlandsPowers.UnitRef unit)) return;
            if (unit.Knight != null)
            {
                if (!PatchRoles_DeadlandsPowers.IsDeadlandsKnight(unit.Knight)) return;
            }
            else if (unit.Archer == null || !PatchRoles_DeadlandsPowers.IsDeadlandsFollower(unit.Archer))
            {
                return;
            }
            __state = new PatchRoles_DeadlandsPowers.MoverBoost(true, __instance.Pointer,
                __instance._goalSpeed, __instance._moveSpeed, __instance.goalMode);
            __instance._goalSpeed *= PatchRoles_DeadlandsPowers.MoveScale;
            __instance._moveSpeed *= PatchRoles_DeadlandsPowers.MoveScale;
        }
        catch (Exception e)
        {
            PatchRoles_DeadlandsPowers.LogOnce("mover prefix failed", e);
        }
    }

    [HarmonyFinalizer]
    private static void Finalizer(PatchRoles_DeadlandsPowers.MoverBoost __state, Exception __exception, Mover __instance)
    {
        if (!__state.Applied) return;
        try
        {
            if (__instance == null || __instance.Pointer != __state.MoverPtr) return;
            if (__instance._goalSpeed != __state.GoalSpeed * PatchRoles_DeadlandsPowers.MoveScale)
                return; // 回调写了新目标速度：两个原值都不碰
            __instance._goalSpeed = __state.GoalSpeed;
            if (__instance.goalMode == __state.GoalMode)
                __instance._moveSpeed /= PatchRoles_DeadlandsPowers.MoveScale;
            else if (__instance._moveSpeed == __state.MoveSpeed * PatchRoles_DeadlandsPowers.MoveScale)
                __instance._moveSpeed = __state.MoveSpeed;
            // Missing goal objects switch goalMode to Off without writing speed.
            // Restore that unchanged temporary value, preserving distinct callback writes.
            // goalMode 变化（SetSpeed 等回调直写 _moveSpeed）：除回会污染，保持原样
        }
        catch (Exception e)
        {
            PatchRoles_DeadlandsPowers.LogOnce("mover finalizer failed", e);
        }
    }
}

// ============================================================
// 动画触发钩子 ×3：权威发送路径 SetAndSendAnimationTrigger（按 animator GO id
// O(1) 反查注册单位）；双端接收路径 Knight/Archer.SetAnimation(int)（不读
// ByteBuffer，只看 animCode）。非攻击触发 → 立即恢复。
// ============================================================

[HarmonyPatch(typeof(AnimationSync), nameof(AnimationSync.SetAndSendAnimationTrigger))]
public static class AnimationSync_Trigger_DeadlandsAnim_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Animator animator, int animTrigger)
    {
        if (!PatchRoles_DeadlandsPowers.ConfigEnabled() || animator == null) return;
        try
        {
            PatchRoles_DeadlandsPowers.UnitRef unit = null;
            if (animator.gameObject != null)
                PatchRoles_DeadlandsPowers.ByAnimatorGo.TryGetValue(
                    animator.gameObject.GetInstanceID(), out unit);
            PatchRoles_DeadlandsPowers.HandleAnimTrigger(animator, animTrigger, unit);
        }
        catch (Exception e)
        {
            PatchRoles_DeadlandsPowers.LogOnce("anim send hook failed", e);
        }
    }
}

[HarmonyPatch(typeof(Knight), "SetAnimation")]
public static class Knight_SetAnimation_DeadlandsAnim_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Knight __instance, int animCode)
    {
        if (__instance == null || !PatchRoles_DeadlandsPowers.ConfigEnabled()) return;
        try
        {
            var unit = new PatchRoles_DeadlandsPowers.UnitRef { Knight = __instance, UnitPtr = __instance.Pointer };
            PatchRoles_DeadlandsPowers.HandleAnimTrigger(__instance._animator, animCode, unit);
        }
        catch (Exception e)
        {
            PatchRoles_DeadlandsPowers.LogOnce("knight anim hook failed", e);
        }
    }
}

[HarmonyPatch(typeof(Archer), "SetAnimation")]
public static class Archer_SetAnimation_DeadlandsAnim_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Archer __instance, int animCode)
    {
        if (__instance == null || !PatchRoles_DeadlandsPowers.ConfigEnabled()) return;
        try
        {
            var unit = new PatchRoles_DeadlandsPowers.UnitRef { Archer = __instance, UnitPtr = __instance.Pointer };
            PatchRoles_DeadlandsPowers.HandleAnimTrigger(__instance._animator, animCode, unit);
        }
        catch (Exception e)
        {
            PatchRoles_DeadlandsPowers.LogOnce("archer anim hook failed", e);
        }
    }
}

// ============================================================
// 禁用/池复用清理：Slash 守卫、动画原速、注册表
// ============================================================

[HarmonyPatch(typeof(Knight), "OnDisable")]
public static class Knight_OnDisable_DeadlandsCleanup_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Knight __instance)
    {
        try { PatchRoles_DeadlandsPowers.OnKnightDisabled(__instance); }
        catch (Exception e) { PatchRoles_DeadlandsPowers.LogOnce("knight disable cleanup failed", e); }
    }
}

[HarmonyPatch(typeof(Archer), "OnDisable")]
public static class Archer_OnDisable_DeadlandsCleanup_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Archer __instance)
    {
        try { PatchRoles_DeadlandsPowers.OnArcherDisabled(__instance); }
        catch (Exception e) { PatchRoles_DeadlandsPowers.LogOnce("archer disable cleanup failed", e); }
    }
}
