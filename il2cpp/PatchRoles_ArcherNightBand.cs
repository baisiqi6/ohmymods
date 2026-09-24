using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 自由弓箭手夜间射击带「安全走廊」（archer-night-band α′ + 安全走廊，
/// 2026-09-24/25 设计审查定稿）。
///
/// 用户缺口：夜间守墙的部分自由弓手站位异常——深位（depth > Cap）对贴墙敌人
/// 射程临界/超包络，被迫高抛（低解 ParabolaCast 之外从不做障碍检测）→ 弧线
/// 撞上自家高墙；浅位/贴墙（depth < Floor，击杀带 [0,3)）站进巨怪接触体积被
/// 击杀（掉弓变 peasant）。实机 2.4.0 把守位公式内联进行为协程：pos = wall −
/// side × (_minDistanceFromWall + _guardDepth × _unitSpacingAtWall +
/// _guardRandomOffset)（DepthClampPass/CrossbowDefense/HeroArcherGuardFacing
/// 三处生产实证；2.1.0 的 GetWallTargetPos 是死代码）。本模块把两类「原生守位
/// 目标」都改写到安全走廊内：
///   浅位（depth < Floor=3.0；含墙外近区 (−2.5,0) 与击杀带 [0,3)）
///     → wall − side × [Floor, Floor+散布) = [3,4)；
///   深位（depth > Cap=7.0）→ wall − side × [Cap−散布, Cap] = [6,7]。
/// 中带 [Floor, Cap] 放行；白天/其余任务/未过门者一律零变化。
///
/// 挂接（唯一入口，见 PatchWorld_DefenseSpacing.MirrorNightArcherGoal）：
/// Mover.SetGoal(float,float) 前缀的 unitType==2 分支——弩手排除位之后、
/// 夜间墙外镜像之前——调用 <see cref="TryTakeRedirect"/>；命中则由该前缀在
/// _inSetGoalRedirect 守卫内、以既有速度链重写目标（不新增 SetGoal patch 类）。
/// 原生 GetWallTargetPos/ShouldGoToWall 为内部直调（detour 死路），此入口与
/// 弩手深化/武士夜列队同点，是当日唯一实证可靠的重定向点。
///
/// 门序（便宜先行；任一不满足即放弃=原生行为零变化）：
///   主开关/世界权限 → director 夜窗 17.5/5.5 → 目标有限性 → 归属侧与深度预检
///   （三分支：中带 [Floor,Cap] 放行；< Floor 浅位候选；> Cap 深位候选）。
///   浅位候选——行为门先行且静默（ShouldGoToWall+latestGoto==8；浅目标海量
///   存在，非守位者不得占用失配遥测）→ 排除集 → 带闸下沿 −2.5 显式检查
///   → 改写 [3,4)。
///   深位候选——排除集 → ShouldGoToWall()/latestGoto==8（记失配）→ 带闸
///   上限 max(12, 原生深度式+1) → 改写 [6,7)。
///
/// 排除集（两分支共用；_knight 随从、塔位、塔上高度、编队、玩家控制、
/// inert/grabbed/stationary、登船、弩手[自有 4..7 深化带策略]、英雄[守位租约
/// 污染]、死亡）：火枪手自安全走廊起纳入（夜间守家=自由弓手语义，
/// MusketeerDefense 是昼间再平衡器不产目标；举旗编队者由 GetFormation 门排除）。
///
/// 契约（安全走廊绑定）：
/// - Cap=7.0 / Floor=3.0 单一共享常量：PatchWorld_DefenseSpacing.DepthClampRange
///   （深度钳制）、墙外窄带镜像目标（旧 0.5+|depth| ∈ 0.7..3.0 恰在新击杀带，
///   已抬到 ≥Floor）与 SquadFollowGuard 随从锚位天花板都引用本文件常量；Cap
///   取值与弩手 DepthMax=7f（一般守区 0..7 的后沿）同源同值——弩手文件不在
///   本任务改动范围，保持 7f 字面量，若后续允许变更应同样引用本常量。
/// - 零 Random：目标深度按 GetInstanceID 无符号哈希确定性导出（仅权威端
///   HasWorldAuth 下发目标，联机外观级分歧与既有设计同口径接受）。
/// - 零持久写：不碰 _guardDepth/_minDistanceFromWall/_guardPos 等任何字段，
///   只产出一个目标 x（由前缀写 Mover）。
/// - 仅主机；异常一律 fail-open 回原生。
/// - 稳态成本已证良性（原生协程 3s 节奏重发 no-op SetGoal 零物理写）；
///   state-8 滞留的 StateChangePenalty=5000 对掉落认领的影响=有界接受（v2 留档）。
///
/// 已知接受边界：改写后原生 at-post 判据仍读原生目标 → 协程循环重发原生
/// 目标 → 我们再改写为同一确定性位（稳态无振荡、无叠加）；关配置/丢权限/
/// 白天立即回原生。每夜活性遥测 ≤6 行（浅位/深位改写共享预算，行内含
/// band=shallow|deep）：首改写=门链+前缀守卫活性证明，首失配（本夜第一条
/// 「走廊候选但未改写」的拒绝）=门链区分度证明。
/// </summary>
internal static class PatchRoles_ArcherNightBand
{
    /// <summary>射击带深限（墙内侧步数）。单一共享常量：DefenseSpacing.DepthClampRange
    /// 与 SquadFollowGuard 随从锚位天花板都引用它（见类注）。</summary>
    internal const float Cap = 7f;

    /// <summary>安全走廊下沿（墙内侧步数）＝击杀带上沿：浅位改写带
    /// [Floor, Floor+散布) 的起点。单一共享常量：PatchWorld_DefenseSpacing
    /// 墙外窄带镜像目标（抬到 ≥Floor）与 lineup 浅位占用列（depth∈[0,Floor)）
    /// 都引用它（见类注）。</summary>
    internal const float Floor = 2f;   // 2026-09-25 用户裁定：3.0 太靠后留大空档，退到 2.0（击杀带下界 1.5 上留 0.5 裕量）

    /// <summary>深/浅位改写的确定性散布幅度：深位目标深度 ∈ [Cap−BandSpread, Cap]，
    /// 浅位 ∈ [Floor, Floor+BandSpread)（避免全部叠在同一平面；brief 定稿为
    /// "微偏移"语义，实机观感可调）。</summary>
    internal const float BandSpread = 1f;

    // ---- 门/带常量（与兄弟机制同款，取自生产实测口径） ----
    private const float NightStartHour = 17.5f;   // DepthClampPass/CrossbowDefense 夜窗
    private const float NightEndHour = 5.5f;
    private const float GroundShootRange = 12f;   // 带闸上限基数（弩手地面射程；保守大于弓 shootRange≈8）
    private const float TowerHeightY = 2.5f;      // 塔上高度（镜像/重定位同款两道防线）
    private const float CorridorOutsideEdge = -2.5f; // 浅位候选显式下沿（墙外；镜像窄带下界同源）
    private const int NativeWallGotoState = 8;    // Haglet.latestGoto 城墙态
    private const int NightLogBudget = 6;         // 活性遥测：每夜 ≤6 行（浅/深位共享预算，含首失配）

    private static int _nightLogs;
    private static bool _firstMismatchLogged;
    private static readonly HashSet<string> LoggedErrors = new();

    // ============================================================
    // 入口：自由弓手夜间守位目标 → 安全走廊（浅位 [3,4) / 深位 [6,7]）
    // ============================================================

    /// <summary>
    /// 判定 incomingGoal 是否是该自由弓手的「夜间原生守位目标在走廊外」，
    /// 是则给出确定性走廊内目标 x 并返回 true。只判定与产出目标：不写 mover、
    /// 不碰速度（调用方在 _inSetGoalRedirect 守卫内以既有链速度写回）。
    /// 全部资格不满足 / 已在中带 [Floor,Cap] → false（target=0）。
    /// </summary>
    internal static bool TryTakeRedirect(Archer archer, float goal, out float target)
    {
        target = 0f;
        try
        {
            if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return false; // 仅主机

            Director director = Managers.Inst != null ? Managers.Inst.director : null;
            if (director == null) return false;
            float t = director.currentTime;
            if (!(t >= NightStartHour || t <= NightEndHour))
            {
                _nightLogs = 0; // 白天窗口：每夜遥测预算重置 + 白天零变化
                _firstMismatchLogged = false;
                return false;
            }

            if (archer == null || archer.gameObject == null || !archer.gameObject.activeInHierarchy) return false;
            if (!float.IsFinite(goal)) return false;

            Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
            if (kingdom == null) return false;

            // 归属侧：优先自身 _guardSide（弩手先例：绝不跨到对面墙）；中性（实测
            // 常见）按目标 x 就近墙（NightParkedFollowerSweep 的位置判定同源）。
            Side side = archer._guardSide;
            if (side != Side.Left && side != Side.Right)
            {
                side = Mathf.Abs(goal - kingdom.GetBorderSideIntact(Side.Left))
                    <= Mathf.Abs(goal - kingdom.GetBorderSideIntact(Side.Right))
                    ? Side.Left : Side.Right;
            }
            float sign = (float)side;
            if (sign == 0f) return false;
            float wall = kingdom.GetBorderSideIntact(side);
            if (!float.IsFinite(wall)) return false;
            float depth = (wall - goal) * sign;
            if (!float.IsFinite(depth)) return false;

            // 深度预检（三分支，最便宜的行为前门）：
            //   中带 [Floor, Cap] 放行——已在健康射击带内，原生行为零变化；
            //   depth < Floor（击杀带 [0,3) 与墙外近区）→ 浅位拦截候选；
            //   depth > Cap → 深位拦截候选（α′ 原路径）。
            if (depth >= Floor && depth <= Cap) return false;
            bool shallow = depth < Floor;

            if (shallow)
            {
                // 浅位候选：行为门先行且静默——浅目标海量存在（狩猎/游荡/拾币/
                // 墙外任务），非守位者直接放行、不占用失配遥测（门序绑定）；
                // 守位态确认后才与深位分支共用排除集。
                if (!IsWallStateArcher(archer)) return false;
                if (IsExcluded(archer, side, depth)) return false;
                // 带闸下沿 −2.5 显式化：更靠外的浅目标属追猎/墙外任务，放行
                // 原生（与 DefenseSpacing 墙外镜像窄带下界同源）。
                if (depth < CorridorOutsideEdge) return Reject(side, depth, "band");

                // ---- 确定性改写：深度 ∈ [Floor, Floor+散布) = [3,4) ----
                float shallowDepth = Floor + BandSpread * Hash01(archer);
                float shallowX = wall - sign * shallowDepth;
                if (!float.IsFinite(shallowX)) return false;
                target = shallowX;
                LogRedirect(archer, side, goal, shallowX, shallowDepth, "shallow");
                return true;
            }

            // ---- 深位候选（depth > Cap；原门序：排除集 → 行为门 → 带闸） ----
            if (IsExcluded(archer, side, depth)) return false;

            // ---- 行为门（与 IsOrdinaryWallArcher 同源：守家态 + 原生城墙态） ----
            if (!archer.ShouldGoToWall()) return Reject(side, depth, "not-wall-duty");
            if (archer.behaviour == null) return Reject(side, depth, "no-behaviour");
            Coatsink.Common.Haglet haglet = archer.behaviour.Cast<Coatsink.Common.Haglet>();
            if (haglet == null || haglet.latestGoto != NativeWallGotoState)
                return Reject(side, depth, "state");

            // ---- 带闸：只认原生守位式量级的目标 ----
            // 带闸规格 [−2.5, max(12, 原生深度式+1)]；下沿由深度预检前置保证
            // （此处 depth > Cap ≥ −2.5），此处只执行上限——防狩猎/追击等远端误伤。
            float nativeDepth = archer._minDistanceFromWall
                + archer._guardDepth * archer._unitSpacingAtWall
                + archer._guardRandomOffset;
            float allowed = Mathf.Max(GroundShootRange, nativeDepth + 1f);
            if (!float.IsFinite(allowed) || depth > allowed)
                return Reject(side, depth, "band");

            // ---- 确定性改写：深度 ∈ [Cap−BandSpread, Cap] = [6,7] ----
            float targetDepth = Cap - BandSpread * Hash01(archer);
            float targetX = wall - sign * targetDepth;
            if (!float.IsFinite(targetX)) return false;
            target = targetX;
            LogRedirect(archer, side, goal, targetX, targetDepth, "deep");
            return true;
        }
        catch (Exception e)
        {
            LogErrorOnce("redirect", e);
            return false;
        }
    }

    /// <summary>浅位静默门：守位态（ShouldGoToWall + 原生城墙态 latestGoto==8）。
    /// 深位分支保留逐项 Reject 记因（not-wall-duty/no-behaviour/state）；浅位
    /// 非守位目标是常态而非近失，按门序绑定不予遥测。</summary>
    private static bool IsWallStateArcher(Archer archer)
    {
        if (!archer.ShouldGoToWall()) return false;
        if (archer.behaviour == null) return false;
        Coatsink.Common.Haglet haglet = archer.behaviour.Cast<Coatsink.Common.Haglet>();
        return haglet != null && haglet.latestGoto == NativeWallGotoState;
    }

    /// <summary>排除集（走廊两分支共用；任一命中=放行原生，仅本夜首条记失配）。
    /// 火枪手自安全走廊起纳入走廊（夜间守家=自由弓手语义；举旗编队者仍由
    /// GetFormation 门排除），不再有无条件 MusketeerIdentity.IsUnit 排除。</summary>
    private static bool IsExcluded(Archer archer, Side side, float depth)
    {
        if (archer._knight != null) { LogFirstMismatch(side, depth, "knight-follower"); return true; }
        if (archer.inGuardSlot || archer._guardSlot != null) { LogFirstMismatch(side, depth, "guard-slot"); return true; }
        if (archer.transform.position.y > TowerHeightY) { LogFirstMismatch(side, depth, "tower-height"); return true; }
        if (archer.GetFormation() != null) { LogFirstMismatch(side, depth, "formation"); return true; }
        if (archer.ShouldPlayerControl()) { LogFirstMismatch(side, depth, "player-control"); return true; }
        Character character = archer._character;
        if (character == null || character.inert || character.grabbed || character.isStationary)
            { LogFirstMismatch(side, depth, "busy"); return true; }
        Embarkee embarkee = archer._embarkee;
        if (embarkee != null && (embarkee.IsEmbarked || embarkee.IsTargetingEmbarkable
            || embarkee.EmbarkableTarget != null))
            { LogFirstMismatch(side, depth, "embark"); return true; }
        if (PatchRoles_Crossbowman.IsCrossbowman(archer)) { LogFirstMismatch(side, depth, "crossbowman"); return true; }
        if (HeroArcherRuntime.IsHero(archer)) { LogFirstMismatch(side, depth, "hero"); return true; }
        if (archer._damageable != null && archer._damageable.isDead) { LogFirstMismatch(side, depth, "dead"); return true; }
        return false;
    }

    /// <summary>确定性哈希 ∈ [0,1]（弩手深化同款手法；零 Random，instanceID 稳定）。</summary>
    private static float Hash01(Archer archer)
    {
        uint id = unchecked((uint)archer.gameObject.GetInstanceID());
        id ^= id >> 16;
        id = unchecked(id * 0x7feb352du);
        id ^= id >> 15;
        return (id & 0xffffu) / 65535f;
    }

    /// <summary>走廊候选被排除：放行原生，仅本夜首条记失配（门链区分度证明）。</summary>
    private static bool Reject(Side side, float depth, string reason)
    {
        LogFirstMismatch(side, depth, reason);
        return false;
    }

    // ============================================================
    // 每夜有界遥测（≤6 行；浅/深位改写共享预算）
    // ============================================================

    private static void LogRedirect(Archer archer, Side side, float goal, float target, float depth, string band)
    {
        if (_nightLogs >= NightLogBudget) return;
        _nightLogs++;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[ArcherNightBand/redirect] side=" + (int)side
            + " band=" + band
            + " x=" + archer.transform.position.x.ToString("F2")
            + " goal=" + goal.ToString("F2") + " -> " + target.ToString("F2")
            + " depth=" + depth.ToString("F2"));
    }

    private static void LogFirstMismatch(Side side, float depth, string reason)
    {
        if (_firstMismatchLogged || _nightLogs >= NightLogBudget) return;
        _firstMismatchLogged = true;
        _nightLogs++;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[ArcherNightBand/mismatch] side=" + (int)side
            + " depth=" + depth.ToString("F2") + " reason=" + reason);
    }

    private static void LogErrorOnce(string key, Exception e)
    {
        if (LoggedErrors.Add(key))
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[ArcherNightBand/" + key + "] " + e);
    }
}
