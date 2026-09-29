using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 武士燕返（2026-09-27 修订）：整次往返的唯一运动所有者。旧实现的每帧
/// SetGoalNoHaglet 重申协程已删除——本文件是唯一推进动作的代码路径，并且绝不写
/// goal/speed/Stop（因此原生 goal 所有权从不被夺取，结束时也没有可归还的旧目标）。
///
/// 结构：
/// - 触发（PatchRoles_SamuraiPowerDash）只读一次世界，冻结起点/方向；本文件把起点+
///   固定 7 格终点写进 token（homeX = 触发瞬间的实际刚体 x，root 与 body 不混用）。
/// - 一个按需注入到该骑士 GameObject 的 <see cref="SamuraiChoreoDriver"/> 提供与物理步
///   1:1 的 FixedUpdate；每步先验证生命周期，再读上一物理步的实际刚体位置，对已走过的
///   实际线段（上一观测点→当前实际点）做胶囊扫掠，然后向冻结端点提交一次
///   <c>Rigidbody2D.MovePosition</c>。没有在途提交时只做以实际点为中心的圆查询，绝不把
///   计划终点当作实际点，也绝不预伤未来路径。
/// - 到达以实际坐标判定（容差 0.0625，与原生 Mover.PositionEpsilon 同级）：出程实际到点
///   才转腿，返程实际到点才完成；腿窗口到期或连续无进展（受阻）都结束整次动作交还原生，
///   不用时限冒充到达、不强行第二腿（对旧 out-deadline 转身行为的有意修正）。
/// - Mover.Update 消费让位由 <see cref="Mover_Update_SamuraiChoreo_Patch"/> 的窄 prefix
///   承担：只有行程自己的 Mover 指针被跳过；Knight.Update 从不被压制。
///
/// 有意保留的既有契约：触发门/距离 7/速度 18/白色残影/姿态恢复保持不变；触发冷却自
/// 2026-09-28 起为 6 秒（锚点仍是触发帧），燕返命中为每腿具名常量 3 点且不读写原生
/// _attackDamage；PowerSlash 期间 Speed 冻结（跳过 Mover.Update 的结构后果）是已审接受
/// 偏差，不逐帧补写 Speed。
/// 异常/中断：死亡、抓取、停用、换世界、换 body/Mover、失权等硬失效立即退役并只归还自有
/// 状态；不调 ForceStop、不恢复旧 vx、不碰替换后的 body。菜单暂停（timeScale=0）不推进、
/// 不结算；外部 Mover.Pause 的原值自然计时且不夺走本动作的运动权限。
///
/// 尚未经现场验证（模型测试不等于物理/联机结论）：MovePosition 在真实动态刚体上的停靠与
/// 最大平移、碰撞阻挡形态、MovePosition 提交后同帧死亡/抓取的取消行为、多固定步/低帧率下
/// 的观感、联机同步。
/// </summary>
internal static class SamuraiChoreoMotion
{
    internal const float FixedDashDistance = 7f;
    internal const float HitRadius = 1.2f;
    // 燕返命中伤害（2026-09-28 用户裁定）：每腿每次有效命中 3 点，经原生 ReceiveDamage 正常
    // 伤害流程提交；本模块绝不读写原生 _attackDamage（普通斩击字段保持原生值）。
    private const int SweepDamage = 3;

    // 动作常量：速度 18 保持旧契约；腿窗口 1.2 s；到达容差与原生 PositionEpsilon 同级；
    // 受阻判据 = 连续 StallWindow 秒对目标无 >ProgressEpsilon 的进展。
    private const float DashSpeed = 18f;
    private const float LegWindow = 1.2f;
    private const float ArriveEpsilon = .0625f;
    private const float StallWindow = .5f, ProgressEpsilon = .02f;
    // 位移远大于一次提交步长=外部接管了 body：立即整程退役（不结算该段、不续程、不改 home/out、
    // 不碰外部速度/goal）。阈值保持是为了不把正常碰撞解算的小偏差误判为接管。
    private const float TeleportFactor = 3f;
    // 固定步停摆看门狗（取代旧协程死亡兜底）：Time.time（受 timeScale 缩放，菜单暂停冻结）
    // 超过该值没有任何成功推进即退役，防止效果永久滞留。
    private const float WatchdogBudget = 3f;
    private const float TurnLogEvery = 6f;
    private const int TurnLogBudget = 12, CloseLogBudget = 24;

    private static readonly Dictionary<int, Trip> TripsByOwner = new();
    private static readonly Dictionary<long, Trip> TripsByMover = new();
    private static readonly HashSet<string> Logged = new();
    private static readonly Dictionary<long, bool> PowerSlashByController = new();
    private static bool _driverRegistered;
    private static int _closeLogs, _turnLogs, _hitLayerMask;

    // 2026-09-25b 真实控制器姿态契约：PowerSlash 唯一出口是 Land 触发器（EventID 137525990）；
    // 两腿直接 Play 已验证的 fullPath 状态，终幕发 Land。短名哈希只用于触发器与状态比对，
    // Play 一律用 fullPath（跨层无歧义），不硬编码资源数值。
    private static readonly int PowerSlash = Animator.StringToHash("PowerSlash");
    private static readonly int PowerSlashFullPath = Animator.StringToHash("Base Layer.PowerSlash");
    private static readonly int Land = Animator.StringToHash("Land");

    /// <summary>一次行程的全部状态；退役（Closed）后任何旧路径都无权再写。</summary>
    internal sealed class Trip
    {
        internal Knight Owner;
        internal Mover Mover;
        internal Rigidbody2D Body;
        internal Damageable Damageable;
        internal TrailRenderer Trail;                       // 只读诊断
        internal SamuraiDashVisuals.Token Visual;
        internal SamuraiDashDiagnostics.Trace Diagnostics;
        internal Animator Animator;
        internal long ControllerKey;
        internal bool PoseKnown;
        internal int LastPlayFrame = -1;
        internal float StartedAt, LastStepAt, HomeX, OutX, TargetX;
        internal float LegStartedAt, LastProgressAt, LastDistance, NextTurnLogAt, TurnX;
        internal int Side;
        // World/gameLayer/scene identity captured at the trigger, plus the layer Transform itself:
        // the knight and body must actually remain inside that layer/scene (an island swap or a
        // re-parent out of it must retire the trip, not let it keep writing the old body).
        internal IntPtr WorldPtr, LayerPtr;
        internal int SceneHandle;
        internal Transform Layer;
        internal bool Outbound = true;
        internal string Phase = "out";
        // 运动推进：LastActual = 上一物理步结束后的实际位置；Pending = 已提交但尚未结算的
        // MovePosition。
        internal Vector2 LastActual;
        internal bool Pending;
        // 效果/朝向的值纪律快照。
        internal bool EffectsOwned, OldInvulnerable;
        internal bool FacingOwned;
        internal Mover.FacingMode FacingWritten;
        internal bool SaturationLogged, ParentHitLogged, ExternalMoveLogged;
        internal bool Closed;
        internal readonly HashSet<IntPtr> HitObjects = new();
        // 命中查询输出必须是常驻原生数组：managed Collider2D[] 会被隐式转换成临时副本，
        // 原生只写副本且无 copyback（已装旧 IL 证明），查询结果将全读为 null。每 Trip 只分配一次，
        // 查询与读取共用同一 wrapper 实例，退役即随之释放。
        internal readonly Il2CppReferenceArray<Collider2D> Colliders = new(64);
    }

    /// <summary>仅诊断用：当前在跑的行程数。</summary>
    internal static int ActiveCount => TripsByOwner.Count;

    /// <summary>身份校验后的活动行程（实例号复用/owner 不符时为 null）。</summary>
    internal static Trip ActiveTrip(Knight knight)
    {
        if (!TryGetRaw(knight, out Trip trip)) return null;
        return Same(trip.Owner, knight) ? trip : null;
    }

    /// <summary>ShouldSlash 门：无行程时不压制；读取异常时 fail-closed 压制。</summary>
    internal static bool IsChoreoActive(Knight knight)
    {
        try { return ActiveTrip(knight) != null; }
        catch { return true; }
    }

    /// <summary>是否存在该骑士任一活动行程（含尚未清理的复用实例号条目；Tick 据此先收尾）。</summary>
    internal static bool HasTrip(Knight knight)
    {
        try { return TryGetRaw(knight, out _); }
        catch { return true; }
    }

    /// <summary>
    /// Tick（Knight.Update postfix）每帧调用：硬失效立即退役；固定步停摆的看门狗在这里兜底。
    /// 菜单暂停冻结 Time.time，因此暂停永远不会误触发看门狗。
    /// </summary>
    internal static void PerFrame(Knight knight)
    {
        if (!TryGetRaw(knight, out Trip trip)) return;
        if (!Same(trip.Owner, knight)) { Close(trip, "hard-invalid:owner"); return; }
        string why = InvalidClause(trip, knight);
        if (why.Length != 0) { Close(trip, "hard-invalid:" + why); return; }
        if (Time.time - trip.LastStepAt >= WatchdogBudget) Close(trip, "tick-cap");
    }

    internal static void CloseActive(Knight knight, string reason)
    {
        if (TryGetRaw(knight, out Trip trip)) Close(trip, reason);
    }

    /// <summary>
    /// 开一次行程：冻结起点/固定终点，快照效果，开姿态权威，注册进两张 O(1) 表并激活驱动器。
    /// 任何一步失败都当场收尾，绝不留下无驱动的无敌效果。
    /// </summary>
    internal static bool TryBegin(Knight knight, int side)
    {
        Trip trip = null;
        try
        {
            if (knight == null || knight.gameObject == null) return false;
            Mover mover = knight._mover;
            if (mover == null) return false;
            Rigidbody2D body = mover.rigidbody;
            if (body == null) return false;
            Damageable damageable = knight._damageable;
            if (damageable == null || damageable.isDead) return false;
            if (TryGetRaw(knight, out Trip stale))
            {
                Close(stale, "started-over");
                if (TryGetRaw(knight, out _)) return false;
            }
            Vector2 start = body.position;
            float homeX = start.x;                              // 冻结的是实际刚体 x，不是 root
            Managers managers = Managers.Inst;
            World world = managers != null ? managers.world : null;
            Transform layer = world != null ? world.gameLayer : null;
            int sceneHandle = layer != null && layer.gameObject != null ? layer.gameObject.scene.handle : 0;
            // 与 InvalidClause 同一实现预检：出生即违约（演员/body 不在当前 gameLayer/场景）的
            // token 不开；读失败同样按违约处理。预检在创建驱动器之前，拒绝时不留组件。
            if (LayerIssue(knight.transform, layer, sceneHandle).Length != 0 ||
                LayerIssue(body.transform, layer, sceneHandle).Length != 0)
            {
                LogOnce("start-layer", null);
                return false;
            }
            if (!EnsureDriver(knight)) return false;
            trip = new Trip
            {
                Owner = knight, Mover = mover, Body = body, Damageable = damageable,
                StartedAt = Time.time, LastStepAt = Time.time,
                HomeX = homeX, OutX = homeX + side * FixedDashDistance, TargetX = homeX + side * FixedDashDistance,
                Side = side, LastActual = start, LegStartedAt = Time.time, LastProgressAt = Time.time,
                LastDistance = FixedDashDistance, Phase = "out", Outbound = true,
                WorldPtr = world != null ? world.Pointer : IntPtr.Zero,
                LayerPtr = layer != null ? layer.Pointer : IntPtr.Zero,
                SceneHandle = sceneHandle,
                Layer = layer
            };
            trip.EffectsOwned = true;
            trip.OldInvulnerable = damageable.invulnerable;
            damageable.invulnerable = true;
            trip.Trail = knight._trail;
            trip.Diagnostics = SamuraiDashDiagnostics.Begin(knight, false, trip.StartedAt);
            LogTrail(trip, "trail-state");                      // 只读诊断：本模块不写 trail
            trip.Visual = SamuraiDashVisuals.Begin(knight, trip.Diagnostics);
            TryOpenSlashPose(trip, knight);
            SlashLegStart(trip, knight);
            // 出程朝向：跳过 Mover.Update 后原生不再为本腿转向，这里沿用反斩同一值纪律
            // （只从 Ahead 接管，绝不覆盖外来固定朝向），SetDirection 写入方向后修复它抹掉的 y 缩放。
            RetargetFacing(trip, side < 0 ? Mover.FacingMode.Left : Mover.FacingMode.Right);
            try
            {
                Vector3 scale = knight.transform.localScale;
                trip.Mover.SetDirection(side);
                knight.transform.localScale = new Vector3(side * Mathf.Abs(scale.x), scale.y, scale.z);
            }
            catch (Exception e) { LogOnce("start-facing", e); }
            TripsByOwner[knight.gameObject.GetInstanceID()] = trip;
            TripsByMover[mover.Pointer.ToInt64()] = trip;
            return true;
        }
        catch (Exception e)
        {
            LogOnce("start", e);
            if (trip != null) Close(trip, "start-failed");
            return false;
        }
    }

    /// <summary>
    /// Mover.Update 消费门（prefix 查询）：只有行程自己的 Mover 指针让位，其余一律原样执行。
    /// 让位前先验证 token：已失效（换 body/world/抓取/死亡…）立即按身份收尾并放行原生消费，
    /// 绝不让一个退休 token 继续扣押 Mover.Update（旧异常/回调窗口也不例外）。
    /// </summary>
    internal static bool Yields(Mover mover)
    {
        if (mover == null || TripsByMover.Count == 0) return false;
        if (!TripsByMover.TryGetValue(mover.Pointer.ToInt64(), out Trip trip) || trip == null) return false;
        if (trip.Closed) return false;
        string why;
        try { why = InvalidClause(trip, trip.Owner); }
        catch { why = "invalid-read"; }
        if (why.Length != 0) { Close(trip, "hard-invalid:" + why); return false; }
        return true;
    }

    /// <summary>驱动器 FixedUpdate 的唯一入口（每物理步一次）。异常即按 token 身份退役该次行程。</summary>
    internal static void FixedStep(GameObject owner)
    {
        Trip trip = null;
        try
        {
            if (owner == null || TripsByOwner.Count == 0) return;
            if (!TripsByOwner.TryGetValue(owner.GetInstanceID(), out trip) || trip == null) return;
            Step(trip);
        }
        catch (Exception e)
        {
            LogOnce("fixed-step", e);
            try { Close(trip, "fixed-step-exception"); } catch { }
        }
    }

    /// <summary>
    /// 实际线段胶囊查询（契约钉死）：Horizontal、center=中点、size=(|b-a|+2r, 2r)、
    /// angle=atan2(dy,dx)·Rad2Deg；|b-a|=0 时是同半径圆（angle 记 0）。
    /// </summary>
    internal static int SweepSegment(Vector2 a, Vector2 b, Il2CppReferenceArray<Collider2D> buffer, int mask)
    {
        float dx = b.x - a.x, dy = b.y - a.y;
        Vector2 center = new Vector2((a.x + b.x) * .5f, (a.y + b.y) * .5f);
        float distance = Mathf.Sqrt(dx * dx + dy * dy);
        Vector2 size = new Vector2(distance + 2f * HitRadius, 2f * HitRadius);
        float angle = distance > .0001f ? Mathf.Atan2(dy, dx) * Mathf.Rad2Deg : 0f;
        return Physics2D.OverlapCapsuleNonAlloc(center, size, CapsuleDirection2D.Horizontal, angle, buffer, mask);
    }

    // ---------------------------------------------------------------------------------------
    // 固定步推进（唯一运动控制路径）
    // ---------------------------------------------------------------------------------------

    private static void Step(Trip trip)
    {
        if (trip == null || trip.Closed) return;
        Knight k = trip.Owner;
        string why = InvalidClause(trip, k);
        if (why.Length != 0) { Close(trip, "hard-invalid:" + why); return; }
        if (Time.timeScale <= 0) return;                       // 菜单暂停：不推进、不结算、不误完成
        Vector2 actual;
        try { actual = trip.Body.position; }
        catch (Exception e) { LogOnce("body", e); Close(trip, "body-read"); return; }

        if (trip.Pending)
        {
            // 结算上一提交实际走完的线段。远超提交步长的位移=外部接管 body：整程退役——
            // 不结算该段、不续程追旧端点、不改 home/out、不碰外部速度/goal（阈值保持，小碰撞
            // 偏差继续走正常流程）。
            trip.Pending = false;
            float dx = actual.x - trip.LastActual.x, dy = actual.y - trip.LastActual.y;
            float length = Mathf.Sqrt(dx * dx + dy * dy);
            if (length <= DashSpeed * Time.fixedDeltaTime * TeleportFactor + .25f)
                Sweep(trip, trip.LastActual, actual);
            else
            {
                if (!trip.ExternalMoveLogged)                  // 诊断 log-once：退出本身无条件
                {
                    trip.ExternalMoveLogged = true;
                    SamuraiDashDiagnostics.Write(trip.Diagnostics, "external-move",
                        "length=" + length.ToString("0.###") + " aborting-trip");
                }
                Close(trip, "external-displacement");
                return;
            }
            if (trip.Closed) return;
        }
        else
        {
            // 无在途提交（起手/提交尚未落地）：只做以实际点为中心的圆查询。
            Sweep(trip, actual, actual);
            if (trip.Closed) return;
        }
        // 伤害回调之后的统一权威门（零长度首扫的最后一个命中同样覆盖）：失效一律先收尾，
        // 绝不穿过它结算、转腿或提交——FixedUpdate 之后紧接着就是物理模拟。
        why = InvalidClause(trip, k);
        if (why.Length != 0) { Close(trip, "hard-invalid:" + why); return; }
        trip.LastActual = actual;

        float distance = Mathf.Abs(trip.TargetX - actual.x);
        if (distance < trip.LastDistance - ProgressEpsilon) trip.LastProgressAt = Time.time;
        else if (Time.time - trip.LastProgressAt >= StallWindow)
        {
            Close(trip, trip.Outbound ? "out-blocked" : "home-blocked");
            return;
        }
        trip.LastDistance = distance;
        if (Time.time - trip.LegStartedAt >= LegWindow)
        {
            Close(trip, trip.Outbound ? "out-window" : "home-window");
            return;
        }
        if (distance <= ArriveEpsilon)
        {
            if (!trip.Outbound) { Close(trip, "complete"); return; }
            Turn(trip, k, actual);
            if (trip.Closed) return;
        }
        Submit(trip, actual);
        // 只有完整走完的安全步骤才算“有推进”：持续异常的步骤不能靠续命骗过看门狗。
        trip.LastStepAt = Time.time;
    }

    /// <summary>向冻结端点提交下一步 MovePosition：x 用 MoveTowards 不越过终点，y 取当前实际值。</summary>
    private static void Submit(Trip trip, Vector2 actual)
    {
        float step = DashSpeed * Time.fixedDeltaTime;
        if (!(step > 0f)) return;
        float nextX = Mathf.MoveTowards(actual.x, trip.TargetX, step);
        try
        {
            trip.Body.MovePosition(new Vector2(nextX, actual.y));
            trip.Pending = true;
        }
        catch (Exception e) { LogOnce("move", e); Close(trip, "move-failed"); }
    }

    /// <summary>出程实际到点后的反斩：重播姿态、清本腿去重、转向回家并立即提交返程。</summary>
    private static void Turn(Trip trip, Knight k, Vector2 actual)
    {
        trip.Phase = "turn";
        SlashLegStart(trip, k);
        trip.HitObjects.Clear();                               // 反斩开自己的命中回合
        trip.Outbound = false;
        trip.TargetX = trip.HomeX;
        trip.TurnX = actual.x;
        trip.Phase = "home";
        trip.LegStartedAt = Time.time;
        trip.LastProgressAt = Time.time;
        trip.LastDistance = Mathf.Abs(trip.TargetX - actual.x);
        int direction = trip.TargetX < actual.x ? -1 : 1;
        RetargetFacing(trip, direction < 0 ? Mover.FacingMode.Left : Mover.FacingMode.Right);
        try
        {
            Vector3 scale = k.transform.localScale;
            trip.Mover.SetDirection(direction);
            k.transform.localScale = new Vector3(direction * Mathf.Abs(scale.x), scale.y, scale.z);
        }
        catch (Exception e) { LogOnce("turn-facing", e); }
        SamuraiDashDiagnostics.Write(trip.Diagnostics, "turn",
            "goal=" + trip.TargetX.ToString("0.##") + " facing=" + trip.FacingWritten);
        TurnLog(trip, k);
    }

    // ---------------------------------------------------------------------------------------
    // 实际线段命中（正常伤害流程）
    // ---------------------------------------------------------------------------------------

    private static void Sweep(Trip trip, Vector2 a, Vector2 b)
    {
        if (trip.Closed) return;
        if (_hitLayerMask == 0) _hitLayerMask = LayerMask.GetMask("Enemies", "Wildlife");
        int count = SweepSegment(a, b, trip.Colliders, _hitLayerMask);
        if (count >= trip.Colliders.Length && !trip.SaturationLogged)
        {
            trip.SaturationLogged = true;                      // 满缓冲明确记账，不静默冒充完整命中
            SamuraiDashDiagnostics.Write(trip.Diagnostics, "saturated", "buffer=" + trip.Colliders.Length);
        }
        Knight k = trip.Owner;
        for (int i = 0; i < count; i++)
        {
            // 每次伤害回调后都要重新验证：旧 token 绝不能继续伤害后续 collider。
            if (trip.Closed) return;
            string why = InvalidClause(trip, k);
            if (why.Length != 0) { Close(trip, "hard-invalid:" + why); return; }
            Collider2D hit = trip.Colliders[i];
            if (hit == null) continue;
            Damageable enemy = ResolveDamageable(trip, hit, k);
            if (enemy == null || !enemy.IsDamagedBy(DamageSource.Knight)) continue;
            if (!trip.HitObjects.Add(enemy.Pointer)) continue;  // 每腿每敌最多一次
            enemy.ReceiveDamage(SweepDamage, k.gameObject, DamageSource.Knight);
        }
    }

    /// <summary>
    /// Damageable 解析：同物体组件优先；为空才做有界父级上溯（最近者优先，最多 3 层），
    /// 并显式排除骑士自身；不取整棵父树的最高 Damageable。
    /// </summary>
    private static Damageable ResolveDamageable(Trip trip, Collider2D hit, Knight k)
    {
        Damageable direct = hit.GetComponent<Damageable>();
        if (direct != null) return Same(direct, k._damageable) ? null : direct;
        Transform t = hit.transform != null ? hit.transform.parent : null;
        for (int depth = 0; t != null && depth < 3; depth++, t = t.parent)
        {
            Damageable parent = t.GetComponent<Damageable>();
            if (parent == null) continue;
            if (Same(parent, k._damageable)) return null;
            if (!trip.ParentHitLogged)
            {
                trip.ParentHitLogged = true;
                SamuraiDashDiagnostics.Write(trip.Diagnostics, "hit-parent", "depth=" + depth);
            }
            return parent;
        }
        return null;
    }

    // ---------------------------------------------------------------------------------------
    // 生命周期、收尾与效果归还
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// 硬失效子集（与旧实现同一组判据）：只保留“动作已不可能存在/不可信”的条款；原生行为
    /// 旗标（retreating/charging/formation/FSM/手动控制）是墙外反应，不是失败。
    /// </summary>
    private static string InvalidClause(Trip trip, Knight k)
    {
        if (trip == null || trip.Closed) return "token";
        if (!Same(trip.Owner, k)) return "owner";
        if (k == null || k.gameObject == null) return "gone";
        if (!k.enabled) return "disabled";
        if (!k.gameObject.activeInHierarchy) return "inactive";
        if (!ModConfig.Enabled.Value) return "config";
        if (!NetworkBigBoss.HasWorldAuth) return "authority";
        if (!PatchRoles_KnightStyle.TryGetResolvedStyleIndex(k, out int style) || style != 2) return "style";
        if (!Same(k._mover, trip.Mover)) return "mover";
        Rigidbody2D body = trip.Mover.rigidbody;
        if (!Same(body, trip.Body)) return "body-replaced";
        if (!body.simulated) return "body-disabled";
        Managers managers = Managers.Inst;
        World world = managers != null ? managers.world : null;
        if (world == null || world.Pointer != trip.WorldPtr) return "world";
        Transform layer = world.gameLayer;
        if (layer == null || layer.Pointer != trip.LayerPtr) return "layer";
        if (layer.gameObject != null && layer.gameObject.scene.handle != trip.SceneHandle) return "scene";
        var embarkee = k._embarkee;
        if (embarkee != null && embarkee.IsEmbarked) return "embarked";
        var c = k._character;
        if (c == null) return "body";
        if (c.inert) return "inert";
        if (c.grabbed) return "grabbed";
        var d = k._damageable;
        if (d == null || d.isDead) return "dead";
        // 演员/body 必须仍实际属于捕获的 gameLayer/场景（IsChildOf 允许同层正常换父节点）：
        // 全局四元组不变但演员被移出层的窄缺口在这里收口。放最后，既有原因串优先级不漂移。
        string layerIssue = LayerIssue(k.transform, trip.Layer, trip.SceneHandle);
        if (layerIssue.Length != 0) return layerIssue;
        layerIssue = LayerIssue(trip.Body != null ? trip.Body.transform : null, trip.Layer, trip.SceneHandle);
        if (layerIssue.Length != 0) return layerIssue;
        return "";
    }

    /// <summary>
    /// 演员/body 是否仍在捕获的 gameLayer 与场景："" 通过；读失败、离开层级、换场景各自有名。
    /// IsChildOf 允许同层正常换父节点。TryBegin 预检与 InvalidClause 共用本实现，防止两道门漂移。
    /// </summary>
    private static string LayerIssue(Transform actor, Transform layer, int sceneHandle)
    {
        try
        {
            if (actor == null || layer == null || actor.gameObject == null) return "layer-unreadable";
            if (!actor.IsChildOf(layer)) return "left-layer";
            if (actor.gameObject.scene.handle != sceneHandle) return "left-scene";
            return "";
        }
        catch { return "layer-unreadable"; }
    }

    /// <summary>
    /// 唯一收尾（幂等）：先标记退役并退表（原生消费立即恢复），再按值归还自有状态——
    /// 效果只在仍带自有值时归还，朝向只在仍持有时归还，姿态只对同一 animator/controller
    /// 发一次 Land，complete 才清技能留下的水平速度。绝不 Stop/ForceStop、绝不恢复旧 goal。
    /// </summary>
    internal static void Close(Trip trip, string reason)
    {
        if (trip == null || trip.Closed) return;
        trip.Closed = true;
        try { Unmap(trip); } catch (Exception e) { LogOnce("unmap", e); }
        Knight k = trip.Owner;
        try { LogClose(trip, reason); } catch (Exception e) { LogOnce("close-log", e); }
        try { SamuraiDashVisuals.End(trip.Visual); } catch (Exception e) { LogOnce("visual-end", e); }
        try { RestoreEffects(trip); } catch (Exception e) { LogOnce("effects", e); }
        try { RestoreFacing(trip); } catch (Exception e) { LogOnce("facing", e); }
        try { if (SamePoseTarget(trip, k, out Animator animator)) animator.ResetTrigger(PowerSlash); }
        catch (Exception e) { LogOnce("reset-trigger", e); }
        try { if (k != null) LandSlashPose(trip, k); } catch (Exception e) { LogOnce("land-pose", e); }
        try { if (reason == "complete") ClearHorizontalVelocity(trip); } catch (Exception e) { LogOnce("velocity", e); }
        try { IdleDriver(k); } catch (Exception e) { LogOnce("driver-idle", e); }
    }

    private static void Unmap(Trip trip)
    {
        if (trip.Owner != null && trip.Owner.gameObject != null)
        {
            int id = trip.Owner.gameObject.GetInstanceID();
            if (TripsByOwner.TryGetValue(id, out Trip current) && ReferenceEquals(current, trip))
                TripsByOwner.Remove(id);
        }
        if (trip.Mover != null)
        {
            long pointer = trip.Mover.Pointer.ToInt64();
            if (TripsByMover.TryGetValue(pointer, out Trip current) && ReferenceEquals(current, trip))
                TripsByMover.Remove(pointer);
        }
    }

    private static void RestoreEffects(Trip trip)
    {
        if (!trip.EffectsOwned) return;
        trip.EffectsOwned = false;
        if (trip.Damageable != null && trip.Damageable.invulnerable)
            trip.Damageable.invulnerable = trip.OldInvulnerable;
        LogTrail(trip, "effects-restored");
    }

    /// <summary>正常 complete：清技能留下的水平速度，保留 vertical velocity；body 已被替换则不碰。</summary>
    private static void ClearHorizontalVelocity(Trip trip)
    {
        if (trip.Body == null || trip.Mover == null || trip.Owner == null) return;
        if (!Same(trip.Mover, trip.Owner._mover)) return;
        if (!Same(trip.Mover.rigidbody, trip.Body)) return;
        Vector2 velocity = trip.Body.linearVelocity;
        trip.Body.linearVelocity = new Vector2(0f, velocity.y);
    }

    // ---------------------------------------------------------------------------------------
    // 姿态权威（2026-09-25b 契约原样保留；只是 token 迁到本文件）
    // ---------------------------------------------------------------------------------------

    private static bool TryHasPowerSlash(Animator animator, out bool has)
    {
        try { has = animator.HasState(0, PowerSlashFullPath); return true; }
        catch (Exception e) { LogOnce("pose-probe", e); has = false; return false; }
    }

    private static bool TryOpenSlashPose(Trip trip, Knight k)
    {
        try
        {
            trip.Animator = k._animator;
            if (trip.Animator == null) return false;
            RuntimeAnimatorController controller = trip.Animator.runtimeAnimatorController;
            if (controller == null) return false;
            long key = controller.Pointer.ToInt64();
            trip.ControllerKey = key;
            if (!PowerSlashByController.TryGetValue(key, out bool known))
            {
                if (!TryHasPowerSlash(trip.Animator, out known)) return false;
                PowerSlashByController[key] = known;
            }
            if (!known) return false;
            trip.PoseKnown = true;
            return true;
        }
        catch (Exception e) { LogOnce("pose-open", e); return false; }
    }

    private static bool SamePoseTarget(Trip trip, Knight k, out Animator animator)
    {
        animator = null;
        if (trip == null || k == null) return false;
        try
        {
            animator = k._animator;
            if (animator == null || !Same(animator, trip.Animator) || !animator.isActiveAndEnabled) return false;
            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            return controller != null && controller.Pointer.ToInt64() == trip.ControllerKey;
        }
        catch (Exception e) { LogOnce("pose-authority", e); return false; }
    }

    private static bool PoseAuthority(Trip trip, Knight k, out Animator animator)
    {
        animator = null;
        return trip != null && trip.PoseKnown && SamePoseTarget(trip, k, out animator);
    }

    /// <summary>
    /// 单腿姿态起点：从 0 帧重播已验证状态（反斩可见重画，消除“滑回家”的残留姿势）；
    /// 先清 Land，避免吃掉唯一出口。未验证控制器退回触发器且永不发 Land；替换后的
    /// animator/controller 收不到任何旧行程的写入。
    /// </summary>
    private static void SlashLegStart(Trip trip, Knight k)
    {
        try
        {
            if (!SamePoseTarget(trip, k, out Animator animator)) return;
            animator.ResetTrigger(PowerSlash);
            if (trip.PoseKnown)
            {
                animator.ResetTrigger(Land);
                animator.Play(PowerSlashFullPath, 0, 0f);
                trip.LastPlayFrame = Time.frameCount;
            }
            else animator.SetTrigger(PowerSlash);
        }
        catch (Exception e) { LogOnce("pose-leg", e); }
    }

    /// <summary>
    /// 真控制器要求的收口：行程在 PowerSlash 中结束（完成或硬失效）必须发一次 Land，
    /// 否则姿势比动作活得更久。闸门保持诚实：同一可读 animator/controller、仍在该状态
    /// （或同帧刚 Play）、身体存活；死亡/别的状态/替换控制器绝不覆盖。
    /// </summary>
    private static void LandSlashPose(Trip trip, Knight k)
    {
        try
        {
            if (!PoseAuthority(trip, k, out Animator animator)) return;
            var d = k._damageable;
            if (d == null || d.isDead) return;
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            bool inState = info.shortNameHash == PowerSlash || info.fullPathHash == PowerSlashFullPath;
            if (!inState && trip.LastPlayFrame != Time.frameCount) return;
            animator.SetTrigger(Land);
        }
        catch (Exception e) { LogOnce("pose-land", e); }
    }

    // ---------------------------------------------------------------------------------------
    // 朝向值的归还（只处理自有状态）
    // ---------------------------------------------------------------------------------------

    private static void RestoreFacing(Trip trip)
    {
        if (!trip.FacingOwned) return;
        trip.FacingOwned = false;
        Mover mover = trip.Mover;
        if (mover == null) return;
        try { if (mover.facingMode == trip.FacingWritten) mover.SetFacingMode(Mover.FacingMode.Ahead, null); }
        catch (Exception e) { LogOnce("restore-facing", e); }
    }

    private static void RetargetFacing(Trip trip, Mover.FacingMode mode)
    {
        Mover mover = trip.Mover;
        if (mover == null) return;
        try
        {
            if (trip.FacingOwned)
            {
                if (mover.facingMode != trip.FacingWritten) { trip.FacingOwned = false; return; }
                mover.SetFacingMode(mode, null);
                trip.FacingWritten = mode;
                return;
            }
            if (mover.facingMode != Mover.FacingMode.Ahead) return;
            mover.SetFacingMode(mode, null);
            trip.FacingWritten = mode;
            trip.FacingOwned = true;
        }
        catch (Exception e) { LogOnce("retarget-facing", e); }
    }

    // ---------------------------------------------------------------------------------------
    // 注入驱动器与日志
    // ---------------------------------------------------------------------------------------

    private static bool EnsureDriver(Knight knight)
    {
        try
        {
            if (!_driverRegistered)
            {
                if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(SamuraiChoreoDriver)))
                    ClassInjector.RegisterTypeInIl2Cpp(typeof(SamuraiChoreoDriver));
                _driverRegistered = true;
            }
            SamuraiChoreoDriver driver = knight.gameObject.GetComponent<SamuraiChoreoDriver>();
            if (driver == null) driver = knight.gameObject.AddComponent<SamuraiChoreoDriver>();
            if (driver == null) { LogOnce("driver-null", null); return false; }
            driver.enabled = true;
            return true;
        }
        catch (Exception e) { LogOnce("driver", e); return false; }
    }

    private static void IdleDriver(Knight knight)
    {
        if (knight == null || knight.gameObject == null) return;
        SamuraiChoreoDriver driver = knight.gameObject.GetComponent<SamuraiChoreoDriver>();
        if (driver != null) driver.enabled = false;
    }

    private static void LogTrail(Trip trip, string eventName)
    {
        if (trip.Diagnostics == null) return;
        try
        {
            TrailRenderer trail = trip.Trail;
            string details = "elapsed=" + (Time.time - trip.StartedAt).ToString("0.###") + " present=" + (trail != null);
            if (trail != null)
                details += " active=" + trail.gameObject.activeInHierarchy + " enabled=" + trail.enabled
                    + " emitting=" + trail.emitting + " points=" + trail.positionCount
                    + " lifetime=" + trail.time + " width=" + trail.widthMultiplier
                    + " layer=" + trail.sortingLayerID + " order=" + trail.sortingOrder;
            SamuraiDashDiagnostics.Write(trip.Diagnostics, eventName, details);
        }
        catch (Exception e) { SamuraiDashDiagnostics.Write(trip.Diagnostics, eventName, "state-read-failed=" + e.GetType().Name); }
    }

    private static void LogClose(Trip trip, string reason)
    {
        if (reason == "complete" || _closeLogs >= CloseLogBudget) return;
        _closeLogs++;
        Knight k = trip.Owner;
        float endX = trip.Body != null ? trip.Body.position.x
            : k != null ? k.transform.position.x : 0f;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/choreo] knight=" +
            (k != null && k.gameObject != null ? k.gameObject.GetInstanceID() : 0) +
            " step=" + reason + " phase=" + trip.Phase + " elapsed=" +
            (Time.time - trip.StartedAt).ToString("0.###") +
            " home=" + trip.HomeX.ToString("0.##") +
            " outGoal=" + trip.OutX.ToString("0.##") +
            " turnX=" + trip.TurnX.ToString("0.##") +
            " endX=" + endX.ToString("0.##"));
    }

    private static void TurnLog(Trip trip, Knight k)
    {
        if (_turnLogs >= TurnLogBudget) return;
        if (Time.time < trip.NextTurnLogAt) return;
        trip.NextTurnLogAt = Time.time + TurnLogEvery;
        _turnLogs++;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDash/roundtrip-turn] x=" +
            k.transform.position.x.ToString("0.##") + " goal=" + trip.TargetX.ToString("0.##") + " deterministic");
    }

    internal static void LogPrefixFailure(Exception e) => LogOnce("mover-prefix", e);

    private static void LogOnce(string where, Exception e)
    {
        if (!Logged.Add(where)) return;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogError("[SamuraiDash/" + where + "] " + (e?.ToString() ?? "failed")); }
        catch { }
    }

    private static bool Same(UnityEngine.Object a, UnityEngine.Object b) =>
        a != null && b != null && a.Pointer == b.Pointer;

    private static bool TryGetRaw(Knight knight, out Trip trip)
    {
        trip = null;
        if (knight == null || knight.gameObject == null) return false;
        return TripsByOwner.TryGetValue(knight.gameObject.GetInstanceID(), out trip) && trip != null;
    }
}

/// <summary>
/// 固定物理步驱动器：行程开始时按需注入到该骑士的 GameObject（FixedUpdate 与物理步 1:1），
/// 空闲即禁用；不持有托管字段（状态全在静态注册表），避免注入类型上的托管实例字段。
/// IntPtr 构造是 ClassInjector 注入的要求（先例 HeroArcherVisualDriver）。
/// </summary>
internal sealed class SamuraiChoreoDriver : MonoBehaviour
{
    public SamuraiChoreoDriver(IntPtr ptr) : base(ptr) { }

    private void FixedUpdate() => SamuraiChoreoMotion.FixedStep(gameObject);
}

/// <summary>
/// 窄消费让位 prefix（2026-09-27 审查钉死）：只有当 __instance 指针属于当前行程才返回 false。
/// Harmony 语义（写准确，防未来误判）：
/// 1) 返回 false 会跳过【原方法，以及后续会影响原执行的 prefix】——执行顺序由 Harmony 排序
///    决定而非注册先后，简单只读 prefix 仍可能照常运行；本文件不对未知其它 patch 作全局保证。
/// 2) postfix/finalizer 不受跳过影响、照常执行——因此 PatchRoles_Worker 的
///    Mover_Update_Patch（postfix: GreekScaleScope.Maintain，未注册缩放的武士为 no-op）与
///    Mover_Update_DeadlandsSpeed_Patch 的 finalizer（其武士分支拒绝 → __state.Applied=false
///    时 no-op）都不会被本 prefix 的错误前提破坏。Priority.Last 保证 DeadlandsSpeed 的
///    prefix（void 返回，先跑）已经把 __state 判定完毕。
/// 被跳过的 Mover.Update 只回放它开头的两条计时语义（真实 2.4 反编译：_pauseTimeout 按
/// Time.deltaTime 衰减；_multiplierTimeout>0 时衰减，否则 _multiplier=1）——这是被跳过函数的
/// 时间账务，不是重申 goal，也不复制移动/朝向/动画逻辑。PowerSlash 期间 Speed 冻结（跳过
/// Mover.Update 的结构后果）是已审接受偏差，本 prefix 不补写 Speed。
/// </summary>
[HarmonyPatch(typeof(Mover), "Update")]
internal static class Mover_Update_SamuraiChoreo_Patch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static bool Prefix(Mover __instance)
    {
        try
        {
            if (!SamuraiChoreoMotion.Yields(__instance)) return true;
            if (__instance._pauseTimeout > 0f) __instance._pauseTimeout -= Time.deltaTime;
            if (__instance._multiplierTimeout > 0f) __instance._multiplierTimeout -= Time.deltaTime;
            else __instance._multiplier = 1f;
            return false;
        }
        catch (Exception e)
        {
            SamuraiChoreoMotion.LogPrefixFailure(e);
            return true;
        }
    }
}
