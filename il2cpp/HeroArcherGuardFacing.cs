// 英雄弓箭手·夜间守墙朝向 slice（worker: hero-live-fixes-20260915 / guard；review 修订版）。
//
// 需求（用户）：夜战里英雄停在墙后守护位时应当**朝外**（面向自己那道墙/敌人），
// 而不是原地朝内；开火动作本身仍由原生瞄准方向决定。
//
// 现状（2.1 参考源 + actual 2.4 interop 方法表核对）：
//   * 自由弓手夜间走位由原生行为 goto==8（城墙态）驱动：`SetGoal(wallTargetPos, runSpeed)`；
//     2.4.0 把守位公式内联进协程（DefenseSpacing 实测：`wall - side * (_minDistanceFromWall +
//     _guardDepth * _unitSpacingAtWall + _guardRandomOffset)`），GetWallTargetPos 成死代码。
//   * 原生只在 Mover.Update 里按 `facingMode` 重写 localScale：Ahead 用“目标-当前”符号
//     （到位/过冲时可能朝内），Left/Right 是固定朝向，Target 指向 facingTarget。
//     正常进入塔位（EnterGuardSlot）时原生会用 SetDirection 固定朝外；英雄**永不进塔位**
//     （no-tower policy），到墙后没有任何一步写固定朝向，于是停在墙后时可能朝内。
//
// 本模块只做一件事：仅对「已购买且在位的英雄」，在**原生夜间**、原生行为处于**城墙态 goto==8**
// （或已知 8→1 停留且守位点未变）时，停在**该原生守位目标点**上、无射击/暂停/移动/任务时，
// 把原生 Mover 的朝向模式设为自己墙侧的方向；条件结束（天亮/离位/新目的地/被撤销/关功能/换生命）
// 时按 CAS 归还原模式（只从 Ahead 接管 ⇒ 归还 Ahead）。
//
// 守位租约（review 修订，替代“宽墙深带即证据”）：
//   * **学习只在 goto==8 + goalMode==Position**：此时 `_goalPosition` 就是原生下发的守位目标，
//     记录 `GuardGoalX/GuardSide`（深度带 [-8,48] 仅作学习期 sanity，不作为证据或到位判据）。
//   * goto==8 与 goto==1（已知 8→1 停留）都可持有；其它 goto 一律释放并作废租约。
//   * 租约存续条件：夜间 + 守位点未变（Position 目标仍等于 GuardGoalX，或已是 Off 静止态）。
//   * 持有条件（在此之上）：位置仍在 GuardGoalX（±0.1）、`!_movingToGoal.value`、无射击协程、
//     `_pauseTimeout<=0`、无狩猎目标、非骑士随从/编队/登船/塔位/玩家控制。
//   * 射击/移动等**挂起**不清租约（下一帧条件恢复即重新断言）；换世界/天亮/换目的地/身份变化才清。
//
// 写入责任（review 修订，写失败/读回失败绝不丢责任）：
//   * 写入前先登记 `Owned/Written/MoverPointer`，再调 setter；setter 抛错或读回抛错都**保留责任**，
//     交给后续 Tick 的 CAS 复核（字段仍是我们的值 → 归还；不是 → 释放）。
//   * 归还只在「读出字段 == 我们写入的值」时才写回 `Ahead`，且写回成功/读回确认后才清责任；
//     写回失败保留责任，下一帧重试（**停用状态也重试**）。
//   * 身份/生命**确认**变化（pointer 换主、life 明确不同、对象已销毁、mover 换过）→ 不写新所有者，
//     直接丢弃责任；读取异常（unknown interop access）→ 保留责任，绝不当作身份无效。
//   * 容量满时只淘汰“无责任”的最旧凭据；全是待恢复凭据时**跳过新登记**（绝不丢弃失败归还责任）。
//
// 边界（契约，越界即错）：
//   * 只写 Mover 朝向模式（SetFacingMode(mode, null)）——绝不手写 transform.localScale：
//     原生 Mover.Update/SetDirection 每帧按模式重写整组 scale（y 也会被重置）。
//   * 只从 Ahead 接管：Target 或相反固定侧（原生/第三方明确意图）绝不覆盖。
//   * 无 Harmony hook、无协程、无计时器、无扫描：Tick 由 operator 在 HeroArcherRuntime.Tick 顶部直调，
//     Evaluate 由既有 `if (state.Hero)` 块逐帧桥接，Restore(archer) 由运行时撤销/池复用路径直调。
//   * 不碰战斗/伤害/射程/箭矢/视觉/存档/配置；不新增 native 入口；异常一律 fail-closed。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class HeroArcherGuardFacing
{
    // ---- 数值（契约；取值依据见 docs/.../guard-result.md） ----
    /// <summary>原生到位 epsilon（与 2.1 ShouldGoToWall 的 0.1 同款；Mover 停止阈值 0.0625 更小）。</summary>
    private const float ArriveEpsilon = 0.1f;

    /// <summary>学习期 sanity 深度带（只防退化值；证据是 goto==8 + Position 目标，绝不是这个带）。</summary>
    private const float LearnDepthMin = -8f;
    private const float LearnDepthMax = 48f;

    /// <summary>凭据容量（英雄每侧至多 1 名；留裕度）。满时只淘汰无责任凭据，待恢复凭据绝不淘汰。</summary>
    private const int MaxReceipts = 4;

    /// <summary>诊断日志 key 上限（有界，绝不满键增长）。</summary>
    private const int MaxLogKeys = 32;

    // ---- 原生行为 goto 常量（CrossbowDefense 实锤 8=城墙态；SquadFollowGuard 实锤 2=跟随）----
    private const int NativeWallGoto = 8;   // 城墙/守位态：唯一可学习守位目标的证据
    private const int NativeIdleGoto = 1;   // 已知的 8→1 停留态：仅在守位点未变且有租约时允许

    private sealed class Receipt
    {
        internal Archer Ref;
        internal IntPtr Pointer;
        internal int GoId;
        internal int Life;
        /// <summary>我们写入的那个 Mover（换过就不归还、直接丢责任）。</summary>
        internal IntPtr MoverPointer;
        /// <summary>接管前的模式（只从 Ahead 接管，所以恒为 Ahead）。</summary>
        internal Mover.FacingMode Previous = Mover.FacingMode.Ahead;
        /// <summary>我们写入的模式。</summary>
        internal Mover.FacingMode Written = Mover.FacingMode.Ahead;
        /// <summary>我们**可能**已写入 Written：责任未清（写失败/读回失败也保留）。</summary>
        internal bool Owned;
        internal bool KeepGuardLease;
        /// <summary>已决定归还，正在重试（归还成功/确认非我所有才清）。</summary>
        internal bool ReleaseWanted;
        /// <summary>原生守位目标（由 goto==8 + Position 目标学习；NaN = 无租约）。</summary>
        internal float GuardGoalX = float.NaN;
        internal Side GuardSide = (Side)0;
        /// <summary>最近一次 Evaluate 的 Tick 序号（跨帧遗漏 ≥2 帧视作 hero 撤销并释放）。</summary>
        internal int Stamp;
    }

    private static readonly Dictionary<int, Receipt> Receipts = new Dictionary<int, Receipt>(MaxReceipts);
    private static readonly List<int> Scratch = new List<int>(MaxReceipts);
    private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);
    private static int _tickStamp;

    /// <summary>当前持有可能写入的凭据数（自检/测试用；只读）。</summary>
    internal static int OwnedCount
    {
        get
        {
            int owned = 0;
            foreach (KeyValuePair<int, Receipt> pair in Receipts) if (pair.Value.Owned) owned++;
            return owned;
        }
    }

    /// <summary>该 Archer 是否仍是本模块的朝向写入责任所有者（自检/测试用；只读）。</summary>
    internal static bool OwnsFacing(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return false;
            int goId = archer.gameObject.GetInstanceID();
            if (!Receipts.TryGetValue(goId, out Receipt receipt) || !receipt.Owned) return false;
            return receipt.Ref != null && receipt.Pointer == archer.Pointer;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>是否已请求归还但尚未清责任（自检/测试用；只读）。</summary>
    internal static bool ReleasePending(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return false;
            int goId = archer.gameObject.GetInstanceID();
            return Receipts.TryGetValue(goId, out Receipt receipt) && receipt.Owned && receipt.ReleaseWanted;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 每帧一次（operator 在 HeroArcherRuntime.Tick 顶部直调）：全局 gate + 归还“本帧没人评估”的凭据
    /// + 重试未清的归还责任（**停用状态也重试**）。不扫描、不发现 hero。
    /// </summary>
    internal static void Tick()
    {
        try
        {
            _tickStamp++;
            if (!HeroArcherRuntime.Enabled)
            {
                // 停用：全部凭据标记归还并立即尝试（失败保留责任）；RetryPending 只对失败者再试一次。
                RequestAllReleases("disabled");
                HeroArcherLiveDiagnostics.Clear();
            }
            else
            {
                PruneStale();
            }
            RetryPending();
            if (HeroArcherRuntime.Enabled) HeroArcherLiveDiagnostics.Tick();
        }
        catch (Exception e)
        {
            LogOnce("tick", e);
        }
    }

    /// <summary>
    /// 每帧对“当前在位英雄”评估一次（operator 在 HeroArcherRuntime.Tick 的 `if (state.Hero)` 块内调用）。
    /// 满足守位静止条件则保证朝外；条件不再成立则归还；身份/生命变化直接作废凭据（不写新所有者）。
    /// </summary>
    internal static void Evaluate(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            Mover mover = archer._mover;
            if (mover == null) return;

            if (!TryKey(archer, out int goId, out IntPtr pointer)) return;
            int life = SafeLife(archer);
            if (life <= 0) return;   // 未登记/未知生命：不接管

            Receipt receipt = GetOrCreate(goId, pointer, life, archer);
            if (receipt == null) return;
            receipt.Stamp = _tickStamp;

            // 英雄身份由 runtime 唯一裁决（购买/开关/单机 gate/资格/世界/life）。
            if (!HeroArcherRuntime.IsHero(archer))
            {
                Retire(receipt, "not-hero");
                return;
            }

            Mover.FacingMode want;
            if (!TryGetOutwardFacing(receipt, archer, mover, out want))
            {
                Suspend(receipt, "leave-guard");
                return;
            }

            Hold(receipt, archer, mover, want);
        }
        catch (Exception e)
        {
            LogOnce("evaluate", e);
        }
    }

    /// <summary>
    /// 运行时撤销/池复用/销毁路径的即时归还桥（operator 接线：RetireHero / ResetIdentityTracking /
    /// Forget）。立即 CAS 归还该 Archer 的朝向；失败保留责任由 Tick 重试，不写新所有者。
    /// </summary>
    internal static void Restore(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            if (!TryKey(archer, out int goId, out _)) return;
            if (!Receipts.TryGetValue(goId, out Receipt receipt)) return;
            if (receipt.Ref == null) receipt.Ref = archer;   // TryRestore 会按凭据自己的身份校验，绝不写新所有者
            receipt.KeepGuardLease = false;
            receipt.ReleaseWanted = true;
            if (receipt.Owned) TryRestore(receipt, "runtime-restore");
            else Drop(receipt);
        }
        catch (Exception e)
        {
            LogOnce("restore-bridge", e);
        }
    }

    /// <summary>全量归还（operator 接线到 HeroArcherRuntime.ClearInternal：停用/换世界的零残留路径）。
    /// pending-safe：归还失败（interop 异常）的凭据保留重试，绝不丢弃责任。</summary>
    internal static void Clear()
    {
        try
        {
            if (Receipts.Count > 0)
            {
                Scratch.Clear();
                foreach (KeyValuePair<int, Receipt> pair in Receipts) Scratch.Add(pair.Key);
                for (int i = 0; i < Scratch.Count; i++)
                {
                    if (!Receipts.TryGetValue(Scratch[i], out Receipt receipt)) continue;
                    receipt.KeepGuardLease = false;
            receipt.ReleaseWanted = true;
                    if (receipt.Owned) TryRestore(receipt, "clear");
                    else Drop(receipt);
                }
                Scratch.Clear();
            }
            HeroArcherLiveDiagnostics.Clear();
        }
        catch (Exception e)
        {
            LogOnce("clear", e);
        }
    }

    // ============================================================
    // 守位租约 + 静止判定（唯一判据；全部通过才输出朝向）
    // ============================================================

    private static bool TryGetOutwardFacing(Receipt receipt, Archer archer, Mover mover, out Mover.FacingMode want)
    {
        want = Mover.FacingMode.Ahead;
        try
        {
            if (!archer.gameObject.activeInHierarchy || !archer.enabled) return false;

            // 1) 实际原生夜间（同 NightVolley 读取口径；本机 2.4 实测 t≈20.0 仍 isNight=False）。
            Director director = Managers.Inst != null ? Managers.Inst.director : null;
            if (director == null || !director.IsNight)
            {
                ClearLease(receipt);            // 白天：释放并作废租约（下一夜重新学习）
                return false;
            }

            Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
            if (kingdom == null) return false;  // 世界未知：挂起（保留租约）

            if (!HeroArcherRuntime.IsHero(archer))
            {
                ClearLease(receipt);
                return false;
            }

            // 2) 任务/资格门（挂起：保留租约，条件恢复即重新断言）
            if (archer._knight != null || archer.GetFormation() != null) return false;
            if (archer.ShouldPlayerControl()) return false;
            if (archer.inGuardSlot || archer._guardSlot != null) return false;
            Embarkee embarkee = archer._embarkee;
            if (embarkee != null && (embarkee.IsEmbarked || embarkee.EmbarkableTarget != null)) return false;
            if (ShootRunning(archer)) return false;
            if (mover._pauseTimeout > 0f) return false;
            if (archer._huntingTarget != null) return false;

            // 3) goto 白名单：只有 8（城墙态，可学习守位目标）与 1（已知 8→1 停留）允许持有。
            if (!TryBehaviourGoto(archer, out int state)) return false;   // 未启动/未知：挂起
            if (state != NativeWallGoto && state != NativeIdleGoto)
            {
                ClearLease(receipt);
                return false;
            }

            // 4) 目标模式：Object 跟随（追人/上船/弱点）一律释放。
            if (mover.goalMode == Mover.GoalMode.Object)
            {
                ClearLease(receipt);
                return false;
            }

            float x = archer.transform.position.x;
            float goalX = mover._goalPosition;
            if (!float.IsFinite(x) || !float.IsFinite(goalX)) return false;

            if (state == NativeWallGoto && mover.goalMode == Mover.GoalMode.Position)
            {
                // 学习/刷新守位目标：唯一证据 = 原生城墙态 + 原生下发的 Position 目标。
                Side learnSide = ResolveSide(archer, kingdom, x);
                if (learnSide != Side.Left && learnSide != Side.Right) return false;
                float wall = kingdom.GetBorderSideIntact(learnSide);
                if (!float.IsFinite(wall)) return false;
                float depth = (wall - goalX) * (float)learnSide;
                if (depth < LearnDepthMin || depth > LearnDepthMax) return false;   // sanity only
                receipt.GuardGoalX = goalX;
                receipt.GuardSide = learnSide;
            }
            else if (!float.IsFinite(receipt.GuardGoalX))
            {
                return false;   // 还没有 goto==8 证据：不动作、不清租约（等城墙态）
            }

            // 5) 守位点未变：Position 模式下原生给了别的目的地 → 不再是原守位点。
            if (mover.goalMode == Mover.GoalMode.Position
                && Mathf.Abs(goalX - receipt.GuardGoalX) > ArriveEpsilon)
            {
                ClearLease(receipt);
                return false;
            }

            // 6) 仍停在该守位点上（挂起：保留租约，回到点上即恢复）。
            if (Mathf.Abs(x - receipt.GuardGoalX) > ArriveEpsilon) return false;
            if (MovingToGoal(mover)) return false;

            Side side = receipt.GuardSide;
            if (side != Side.Left && side != Side.Right) side = ResolveSide(archer, kingdom, x);
            if (side != Side.Left && side != Side.Right) return false;

            want = side == Side.Left ? Mover.FacingMode.Left : Mover.FacingMode.Right;
            return true;
        }
        catch (Exception e)
        {
            LogOnce("predicate", e);
            return false;
        }
    }

    private static void ClearLease(Receipt receipt)
    {
        receipt.GuardGoalX = float.NaN;
        receipt.GuardSide = (Side)0;
    }

    /// <summary>归属侧：优先原生 `_guardSide`，中性时按营火位置（同原生 EnterGuardSlot 的
    /// `x > campfirePosition` 方向语义）。</summary>
    private static Side ResolveSide(Archer archer, Kingdom kingdom, float x)
    {
        Side side = archer._guardSide;
        if (side == Side.Left || side == Side.Right) return side;
        return x >= kingdom.campfirePosition ? Side.Right : Side.Left;
    }

    /// <summary>原生行为 goto（未启动/缺失 → false = 未知，挂起不动作）。</summary>
    private static bool TryBehaviourGoto(Archer archer, out int state)
    {
        state = 0;
        if (archer.behaviour == null) return false;
        var haglet = archer.behaviour.Cast<Coatsink.Common.Haglet>();
        if (haglet == null || !haglet.started) return false;
        state = haglet.latestGoto;
        return true;
    }

    /// <summary>原生 shoot 协程是否在跑（射击+准备窗口；PatchArcher_Options 同款判定）。</summary>
    private static bool ShootRunning(Archer archer)
    {
        var shoot = archer.shoot;   // Haglet（PatchRoles_DeadlandsPowers 同款读取）
        if (shoot == null) return false;
        var haglet = shoot.Cast<Coatsink.Common.Haglet>();
        return haglet != null && haglet.started;
    }

    private static bool MovingToGoal(Mover mover)
    {
        var moving = mover._movingToGoal;   // HagletValue<bool>（CrossbowDefense 同款读取）
        return moving == null || moving.value;
    }

    // ============================================================
    // 写入 / 归还 / 责任
    // ============================================================

    /// <summary>条件不成立：有责任就归还（失败保留重试），无责任则保留凭据（守位租约仍在）。</summary>
    private static void Suspend(Receipt receipt, string reason)
    {
        if (!receipt.Owned) return;
        receipt.KeepGuardLease = true;
            receipt.ReleaseWanted = true;
        TryRestore(receipt, reason);
    }

    /// <summary>不再是在位英雄（撤销/失活）：归还并按需丢弃凭据（租约不再有意义）。</summary>
    private static void Retire(Receipt receipt, string reason)
    {
        if (!receipt.Owned)
        {
            Drop(receipt);
            return;
        }
        receipt.KeepGuardLease = false;
            receipt.ReleaseWanted = true;
        TryRestore(receipt, reason);
        if (!receipt.Owned) Drop(receipt);
    }

    private static void Hold(Receipt receipt, Archer archer, Mover mover, Mover.FacingMode want)
    {
        receipt.ReleaseWanted = false;
        if (!TryReadMode(mover, out Mover.FacingMode current)) return;   // 读失败：责任保留，下帧再试

        if (current == want)
        {
            // 现值已是目标朝向：只有确实由我们写入的那一个才继续持有责任，否则不认领。
            if (!(receipt.Owned && receipt.Written == want)) receipt.Owned = false;
            return;
        }

        if (current != Mover.FacingMode.Ahead)
        {
            // Target / 相反的固定侧：原生或第三方明确指定 → 绝不覆盖，只放弃责任。
            if (receipt.Owned)
            {
                receipt.Owned = false;
                LogOnce("foreign-release", null);
            }
            return;
        }

        // current == Ahead：接管，或原生重置回 Ahead 后重新断言。
        Write(receipt, archer, mover, want);
    }

    /// <summary>
    /// 写入：**先登记责任再调 setter**（setter 抛错也可能已生效）；读回失败同样保留责任，
    /// 由后续 Tick 的 CAS 复核（字段是我们的 → 归还；不是 → 释放）。
    /// </summary>
    private static bool Write(Receipt receipt, Archer archer, Mover mover, Mover.FacingMode want)
    {
        if (!TryPointer(mover, out IntPtr moverPointer)) return false;   // 未知：不写、不登记

        receipt.Ref = archer;
        receipt.MoverPointer = moverPointer;
        receipt.Previous = Mover.FacingMode.Ahead;   // 只在 current==Ahead 时写入
        receipt.Written = want;
        receipt.Owned = true;
        receipt.ReleaseWanted = false;

        try
        {
            mover.SetFacingMode(want, null);
        }
        catch (Exception e)
        {
            LogOnce("write-mode", e);
            return false;                      // 责任保留：下帧按字段现值 CAS 复核
        }

        if (!TryReadMode(mover, out Mover.FacingMode after)) return false;   // 读回失败：责任保留
        if (after == want) return true;                                      // 读回确认

        // 读回不是我们的值（写未生效或第三方抢先）：责任清空，不覆盖对方。
        receipt.Owned = false;
        return false;
    }

    /// <summary>
    /// CAS 归还：只在「字段现值 == 我们写入的值」且对象身份仍是同一 pointer/life/Mover 时写回 `Ahead`。
    /// 任何 step 的未知失败（interop 抛错）都**保留责任**；只有确认不可达/确认非我所有才清责任并丢弃。
    /// </summary>
    private static void TryRestore(Receipt receipt, string reason)
    {
        if (receipt == null) return;
        if (!receipt.Owned)
        {
            FinishRestore(receipt);
            return;
        }

        Archer archer = receipt.Ref;
        if (archer == null || archer.gameObject == null)
        {
            receipt.Owned = false;                 // 已销毁：没有字段可写
            Drop(receipt);
            return;
        }

        if (!TryPointer(archer, out IntPtr pointer)) return;                       // 未知：保留
        if (pointer != receipt.Pointer)
        {
            receipt.Owned = false;                 // 换主：旧对象不可达，绝不写新所有者
            Drop(receipt);
            return;
        }

        int life = SafeLife(archer);
        if (life <= 0) return;                                                      // 未知：保留
        if (life != receipt.Life)
        {
            receipt.Owned = false;                 // 明确换生命：不写新所有者
            Drop(receipt);
            return;
        }

        Mover mover;
        try
        {
            mover = archer._mover;
        }
        catch (Exception)
        {
            return;                                                               // 未知：保留
        }
        if (mover == null)
        {
            receipt.Owned = false;
            Drop(receipt);
            return;
        }
        if (!TryPointer(mover, out IntPtr moverPointer)) return;                   // 未知：保留
        if (moverPointer != receipt.MoverPointer)
        {
            receipt.Owned = false;                 // 换过 Mover：旧写入不可达
            Drop(receipt);
            return;
        }

        if (!TryReadMode(mover, out Mover.FacingMode current)) return;             // 读失败：保留
        if (current != receipt.Written)
        {
            receipt.Owned = false;                 // 字段不是我们的值（原生/第三方已改）
            LogOnce("release:" + reason, null);
            FinishRestore(receipt);
            return;
        }

        try
        {
            mover.SetFacingMode(receipt.Previous, null);
        }
        catch (Exception e)
        {
            LogOnce("restore", e);
            return;                                                              // 写失败：保留
        }

        if (!TryReadMode(mover, out Mover.FacingMode after)) return;               // 读回失败：保留（下帧 CAS 复核）
        if (after == receipt.Written) return; // No-op setter: restoration is still pending.
        receipt.Owned = false;
        if (after != receipt.Previous) LogOnce("restore-overwritten", null);       // 第三方已改：只记录
        LogOnce("release:" + reason, null);
        FinishRestore(receipt);
    }

    private static void FinishRestore(Receipt receipt)
    {
        receipt.Owned = false;
        receipt.ReleaseWanted = false;
        if (!receipt.KeepGuardLease) Drop(receipt);
    }

    internal static bool HasOutstanding(int goId)
        => Receipts.TryGetValue(goId, out Receipt receipt) && receipt.Owned;

    private static bool TryReadMode(Mover mover, out Mover.FacingMode mode)
    {
        mode = Mover.FacingMode.Ahead;
        try
        {
            mode = mover.facingMode;
            return true;
        }
        catch (Exception e)
        {
            LogOnce("read-mode", e);
            return false;
        }
    }

    private static bool TryPointer(UnityEngine.Object value, out IntPtr pointer)
    {
        pointer = IntPtr.Zero;
        try
        {
            pointer = value.Pointer;
            return pointer != IntPtr.Zero;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static int SafeLife(Archer archer)
    {
        try
        {
            return HeroArcherRuntime.CurrentActorLife(archer);
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private static bool TryKey(Archer archer, out int goId, out IntPtr pointer)
    {
        goId = 0;
        pointer = IntPtr.Zero;
        try
        {
            goId = archer.gameObject.GetInstanceID();
            pointer = archer.Pointer;
            return goId != 0 && pointer != IntPtr.Zero;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Receipt GetOrCreate(int goId, IntPtr pointer, int life, Archer archer)
    {
        if (Receipts.TryGetValue(goId, out Receipt existing))
        {
            if (existing.Pointer == pointer && existing.Life == life)
            {
                existing.Ref = archer;
                return existing;
            }
            // 身份确认变化（换主/池复用）：不写新所有者，直接作废旧凭据（原生 OnEnable 会重置模式）。
            LogOnce("identity-change", null);
            Drop(existing);
        }

        if (Receipts.Count >= MaxReceipts && !MakeRoom())
        {
            LogOnce("capacity", null);
            return null;   // 容量满且全是待恢复责任：跳过新登记，绝不淘汰失败归还凭据
        }

        Receipt receipt = new Receipt
        {
            Ref = archer,
            Pointer = pointer,
            GoId = goId,
            Life = life,
            Stamp = _tickStamp,
        };
        Receipts[goId] = receipt;
        return receipt;
    }

    /// <summary>只淘汰“无写入责任”的最旧凭据；返回是否腾出容量。</summary>
    private static bool MakeRoom()
    {
        int oldestKey = 0;
        int oldestStamp = int.MaxValue;
        foreach (KeyValuePair<int, Receipt> pair in Receipts)
        {
            if (pair.Value.Owned) continue;
            if (pair.Value.Stamp >= oldestStamp) continue;
            oldestStamp = pair.Value.Stamp;
            oldestKey = pair.Key;
        }
        if (oldestKey == 0 || !Receipts.TryGetValue(oldestKey, out Receipt oldest)) return false;
        Drop(oldest);
        return true;
    }

    private static void RequestAllReleases(string reason)
    {
        if (Receipts.Count == 0) return;
        Scratch.Clear();
        foreach (KeyValuePair<int, Receipt> pair in Receipts) Scratch.Add(pair.Key);
        for (int i = 0; i < Scratch.Count; i++)
        {
            if (!Receipts.TryGetValue(Scratch[i], out Receipt receipt)) continue;
            receipt.KeepGuardLease = false;
            receipt.ReleaseWanted = true;
            if (receipt.Owned) TryRestore(receipt, reason);
            else Drop(receipt);   // 停用：无责任的租约凭据直接丢弃
        }
        Scratch.Clear();
    }

    private static void RetryPending()
    {
        if (Receipts.Count == 0) return;
        bool any = false;
        foreach (KeyValuePair<int, Receipt> pair in Receipts)
        {
            if (pair.Value.Owned && pair.Value.ReleaseWanted) { any = true; break; }
        }
        if (!any) return;

        Scratch.Clear();
        foreach (KeyValuePair<int, Receipt> pair in Receipts) Scratch.Add(pair.Key);
        for (int i = 0; i < Scratch.Count; i++)
        {
            if (!Receipts.TryGetValue(Scratch[i], out Receipt receipt)) continue;
            if (receipt.Owned && receipt.ReleaseWanted) TryRestore(receipt, "retry");
        }
        Scratch.Clear();
    }

    private static void PruneStale()
    {
        if (Receipts.Count == 0) return;
        Scratch.Clear();
        foreach (KeyValuePair<int, Receipt> pair in Receipts) Scratch.Add(pair.Key);
        for (int i = 0; i < Scratch.Count; i++)
        {
            if (!Receipts.TryGetValue(Scratch[i], out Receipt receipt)) continue;
            // 本帧或上一帧评估过 → 留（本帧稍后的 Evaluate 会刷新 Stamp）。
            if (receipt.Stamp >= _tickStamp - 1) continue;
            // 连续 ≥2 帧没有 Evaluate（hero 被撤销/运行时剔除/未接线）：归还责任并释放凭据。
            receipt.KeepGuardLease = false;
            receipt.ReleaseWanted = true;
            if (receipt.Owned) TryRestore(receipt, "stale");
            else Drop(receipt);
        }
        Scratch.Clear();
    }

    private static void Drop(Receipt receipt)
    {
        if (receipt == null) return;
        if (Receipts.TryGetValue(receipt.GoId, out Receipt current) && ReferenceEquals(current, receipt))
            Receipts.Remove(receipt.GoId);
    }

    private static void LogOnce(string key, Exception e)
    {
        try
        {
            if (Logged.Count >= MaxLogKeys && !Logged.Contains(key)) return;
            if (!Logged.Add(key)) return;
            KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo(
                "[HeroGuardFacing/" + key + "]" + (e != null ? " " + e.GetType().Name + ": " + e.Message : string.Empty));
        }
        catch (Exception)
        {
        }
    }
}
