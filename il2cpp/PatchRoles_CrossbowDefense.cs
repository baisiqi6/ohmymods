using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 弩手守位深化 + 塔位射程增益 helper（crossbow-defense 定稿契约）。
/// 本类是纯 helper：不做 Mover Harmony 补丁（DefenseSpacing prefix 由 Operator
/// 接线调用 TryGetNightGoal）、不做全场景扫描/线程/资产/RPC，只暴露：
/// - TryGetNightGoal：判定某次夜间移动目标是否是"本弩手的城墙守位目标"，
///   是则给出确定性的墙内 4~7 步深化目标（中后排，压低抛物线）；
/// - TryPullBack：同资格判定下，用 _mover._goalPosition 做自检式纠偏
///   （IntegrityPass 5s 一拍调用，替代旧 Crossbowman.ApplyNightPullback）；
/// - ReconcileTowerRange：塔位弩手 towerShootRange ×1.5（12→18）幂等增益，
///   仅塔位时同步扫描器，离塔/关配置/丢标记精确还原基线；
/// - Remove / OnDisable：Strip 销毁标记前 / 插件停用时的显式还原清理。
///
/// 契约要点（TASK.md）：
/// - 地面 shoot12/伤害/冷却/弩矢一律不动；只改"站位目标 x"与 towerShootRange；
/// - 深化不承诺消灭高抛（只减少贴墙擦墙的被迫高抛），RESULT.md 有说明；
/// - 零 Random：目标深度用 GetInstanceID 无符号哈希确定性导出（仅权威端
///   下发目标，联机外观级分歧与 RecomputeOnLoad 同口径接受）；
/// - 资格判定宁可漏过不可误伤：夜间窗口、无骑士/无塔位/无编队/无玩家控制/
///   未上船待上船、原生 ShouldGoToWall、Haglet.latestGoto==8（原生城墙态）、
///   _guardSide 必须 Left/Right（绝不跨到对面墙）、目标 x 必须落在
///   [-2.5, max(12, 原生 guard-depth 公式+1)] 的墙深带内。
///
/// 原生名实锤（2.4，TASK.md 侦查）：Archer.ShouldGoToWall()/ShouldPlayerControl()/
/// SetGuardSlot(GuardSlot)/EnterGuardSlot(GuardSlot)/ExitGuardSlot()/OnDisable()；
/// Archer.behaviour 是 IHaglet（须 .Cast&lt;Coatsink.Common.Haglet&gt;()，同
/// FriendlyTroll 的 troll._behaviour 先例）；latestGoto 为 int，8=原生城墙态。
/// 上船判定：archer._embarkee.IsEmbarked / archer.EmbarkableTarget。
/// </summary>
internal static class PatchRoles_CrossbowDefense
{
    // ---- 数值定稿（契约，勿改） ----
    private const float NightStartHour = 17.5f;
    private const float NightEndHour = 5.5f;
    internal const float DepthMin = 4f;              // 地面守位深化带下限
    internal const float DepthMax = 7f;              // 上限=一般守区 0..7 的后沿
    private const float GoalEpsilon = 0.1f;          // 目标已在位（±0.1）则不再下发
    private const float GoalBandOutside = -2.5f;     // 墙外窄带下沿（同 DefenseSpacing 镜像带）
    private const float GroundShootRange = 12f;      // 带宽兜底：地面 shoot12 不改
    private const float TowerRangeMultiplier = 1.5f; // 塔位 12→18
    private const float TowerHeightY = 2.5f;         // 塔上高度（同 DefenseSpacing 两道防线）
    private const int NativeWallGotoState = 8;       // Haglet.latestGoto 城墙态

    // ---- 一次性日志（按 key 一次） ----
    private static bool _loggedPullback;
    private static bool _loggedTowerBoost;
    private static bool _loggedTowerRestore;

    private static readonly HashSet<string> LoggedErrors = new();
    private static void LogErrorOnce(string key, Exception e)
    {
        if (LoggedErrors.Add(key)) KingdomEnhancedPlugin.Instance?.LogSource.LogError("[CrossbowDefense/" + key + "] " + e);
    }

    // ============================================================
    // 1. TryGetNightGoal
    // ============================================================

    /// <summary>
    /// 判定 incomingGoal 是否是"该弩手的原生夜间城墙守位目标"，是则输出
    /// 确定性深化目标 x（墙内 4~7 步，按单位 instanceID 稳定导出），返回 true。
    /// 只判定与产出目标：不写 mover、不碰速度（调用方保持原生 speed 并自带
    /// 递归防护——DefenseSpacing prefix 用 _inSetGoalRedirect 同款手法）。
    /// 全部资格不满足、或目标已在 ±0.1 内，返回 false（target=0）。
    /// </summary>
    internal static bool TryGetNightGoal(Archer archer, float incomingGoal, out float target)
        => TryGetNightGoalCore(archer, incomingGoal, out target, false);

    private static bool TryGetNightGoalCore(Archer archer, float incomingGoal, out float target, bool allowUnchanged)
    {
        target = 0f;
        try
        {
            Side side;
            float wall;
            if (!QualifyNightDefender(archer, out side, out wall)) return false;

            float sign = (float)side;

            // 带宽闸（防误伤无关移动目标）：目标墙深必须在
            // [-2.5, max(12, 原生 guard-depth 公式+1)] 内——原生守位目标天然
            // 落带内（公式即 2.4 行为协程的内墙站位式，DefenseSpacing 实测），
            // 狩猎/追击/捡币等更远目标一律放行原生。
            float depth = (wall - incomingGoal) * sign;
            float nativeDepth = archer._minDistanceFromWall
                + archer._guardDepth * archer._unitSpacingAtWall
                + archer._guardRandomOffset;
            float allowed = Mathf.Max(GroundShootRange, nativeDepth + 1f);
            if (depth < GoalBandOutside || depth > allowed) return false;

            Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
            if (kingdom == null) return false;

            // 确定性目标深度：4..7，按 instanceID 无符号哈希稳定导出（零 Random；
            // 仅权威端 HasWorldAuth 下发，客户端外观分歧与既有设计同口径）。
            uint id = unchecked((uint)archer.gameObject.GetInstanceID());
            id ^= id >> 16;
            id = unchecked(id * 0x7feb352du);
            id ^= id >> 15;
            float targetDepth = DepthMin + (DepthMax - DepthMin) * (id & 0xffffu) / 65535f;

            // 领地钳制一：本侧最大纵深不越过两侧墙中点（绝不穿到对面半场）。
            Side other = side == Side.Left ? Side.Right : Side.Left;
            float otherWall = kingdom.GetBorderSideIntact(other);
            float mid = (wall + otherWall) * 0.5f;
            float maxByMid = (wall - mid) * sign;

            // 领地钳制二：窄领地不越过营火（kingdom.campfirePosition，
            // BankAssistants 同款访问）。营火在本侧更深才钳；钳后仍保底
            // 0.5 步（贴墙内侧），领地极窄时宁可浅也不出墙。
            float campDepth = (wall - kingdom.campfirePosition) * sign;
            float available = Mathf.Min(maxByMid, campDepth) - 0.1f;
            if (!float.IsFinite(available) || available < 0.5f) return false;
            targetDepth = Mathf.Min(targetDepth, available);

            float targetX = wall - sign * targetDepth;
            if (!allowUnchanged && Mathf.Abs(incomingGoal - targetX) <= GoalEpsilon) return false;

            target = targetX;
            return true;
        }
        catch (Exception e)
        {
            LogErrorOnce("night-goal", e);
            return false;
        }
    }

    /// <summary>
    /// 夜间守位资格全集（TryGetNightGoal/TryPullBack 共用）。全部通过时给出
    /// 归属侧与该侧完好在墙 x。任何一环不过即 false——宁可漏纠偏不误伤。
    /// </summary>
    private static bool QualifyNightDefender(Archer archer, out Side side, out float wall)
    {
        side = default;
        wall = 0f;
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return false;
        if (archer == null || archer.gameObject == null || !archer.gameObject.activeInHierarchy)
            return false;
        if (!PatchRoles_Crossbowman.IsCrossbowman(archer)) return false; // 内部含类型注册防御
        if (archer._knight != null) return false;                        // 骑士随从编队位原生管理
        if (archer.inGuardSlot || archer._guardSlot != null) return false; // 塔位固定岗
        if (archer.transform.position.y > TowerHeightY) return false;    // 塔上高度兜底
        if (archer.GetFormation() != null) return false;                 // 编队中（含盾墙）
        if (archer.ShouldPlayerControl()) return false;                  // 玩家控制单位
        if (archer._embarkee != null && archer._embarkee.IsEmbarked) return false; // 已上船
        if (archer._embarkee != null && archer._embarkee.EmbarkableTarget != null) return false; // 待上船
        if (!archer.ShouldGoToWall()) return false;                      // 非守家态

        // 夜间窗口（NightVolley/DefenseSpacing 同款）
        Director director = Managers.Inst != null ? Managers.Inst.director : null;
        if (director == null) return false;
        float t = director.currentTime;
        if (!(t >= NightStartHour || t <= NightEndHour)) return false;

        // 原生行为机确实处于城墙守位态（latestGoto==8）；behaviour 是 IHaglet，
        // 须 Cast（FriendlyTroll 的 troll._behaviour 同款手法）。
        if (archer.behaviour == null) return false;
        Coatsink.Common.Haglet haglet = archer.behaviour.Cast<Coatsink.Common.Haglet>();
        if (haglet == null || haglet.latestGoto != NativeWallGotoState) return false;

        // 归属侧：只认自己的 _guardSide（Left/Right），绝不按位置挑对面墙
        side = archer._guardSide;
        float sign = (float)side;
        if (side != Side.Left && side != Side.Right) return false;

        Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
        if (kingdom == null) return false;
        wall = kingdom.GetBorderSideIntact(side);
        return true;
    }

    // ============================================================
    // 2. TryPullBack（IntegrityPass 5s 一拍的守位深化纠偏）
    // ============================================================

    /// <summary>
    /// 自检式深化纠偏（替代旧 Crossbowman.ApplyNightPullback 的函数体）：
    /// 以当前 _mover._goalPosition 为 incoming goal 走同一套资格与带宽闸，
    /// 仅当「当前位置在 4..7 带外 且 在途目标不在 4..7 带内」时下发一次
    /// SetGoal(target, walkSpeed)——步行前往，绝不瞬移；在途目标已在带内
    /// 则跳过（防 5s 一拍反复重发抖动）。返回是否下发了目标。
    /// </summary>
    internal static bool TryPullBack(Archer archer)
    {
        try
        {
            if (archer == null || archer._mover == null) return false;
            float goalX = archer._mover._goalPosition;
            if (!TryGetNightGoalCore(archer, goalX, out float target, true)) return false;

            // 资格闸已过（side/wall 已知合法）：当前在途目标合法但不等于目标位。
            // 再查当前位置：已在 4..7 带内就不动（天然限流零写入）。
            Side side = archer._guardSide;
            float sign = (float)side;
            float wall = Managers.Inst.kingdom.GetBorderSideIntact(side);
            float posDepth = (wall - archer.transform.position.x) * sign;
            if (posDepth >= DepthMin && posDepth <= DepthMax) return false;
            if (Mathf.Abs(archer.transform.position.x - target) <= GoalEpsilon) return false;

            // 在途目标已指向带内（如上一拍已下发）也不重发（防 5s 抖动）。
            float goalDepth = (wall - goalX) * sign;
            if (archer._mover._movingToGoal != null && archer._mover._movingToGoal.value
                && (goalDepth >= DepthMin && goalDepth <= DepthMax
                    || Mathf.Abs(goalX - target) <= GoalEpsilon)) return false;

            if (!_loggedPullback)
            {
                _loggedPullback = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[CrossbowDefense] night pullback: x="
                    + archer.transform.position.x.ToString("F1")
                    + " -> " + target.ToString("F1") + " (walk, depth 4..7)");
            }
            // SetGoal(float,float) 走 DefenseSpacing prefix：该 mover 是 Archer、
            // 目标已在墙内带外窄带之外，不会被夜间镜像改写；递归防护由调用链
            // （prefix 的 _inSetGoalRedirect 同款语义）承担。
            archer._mover.SetGoal(target, archer.walkSpeed);
            return true;
        }
        catch (Exception e)
        {
            LogErrorOnce("pullback", e);
            return false;
        }
    }

    // ============================================================
    // 3. ReconcileTowerRange（塔位射程 ×1.5 幂等增益与还原）
    // ============================================================

    /// <summary>
    /// 塔位射程状态（per-archer，键=Archer 原生指针；Owner 校验防池指针复用）。
    /// Baseline 在首次接触（尚未放大）时从运行时实例捕获——绝不用已放大值
    /// 当基线（幂等 ×1.5 而非 ×1.5×1.5…）。
    /// </summary>
    private sealed class TowerState
    {
        internal Archer Owner;
        internal float Baseline;
    }

    // 按指针键控的塔位基线账本（数量=场上弩手，25% 上限，小字典不扫描）
    private static readonly Dictionary<IntPtr, TowerState> _towerStates =
        new Dictionary<IntPtr, TowerState>();

    /// <summary>
    /// 塔位射程对账（幂等，可无脑反复调用）：
    /// - 带标记 + 配置开：首见捕获基线 → towerShootRange=基线×1.5（12→18）；
    ///   仅当在塔（inGuardSlot 或 _guardSlot!=null）时把扫描器 range/rangeBehind
    ///   同步到放大值（地面绝不写扫描器——原生/Apply 各归各管）；
    /// - 丢标记 / 配置关：只在当前值仍等于"我们放的放大值"时还原基线与
    ///   扫描器（避免覆写游戏后续写入），随后移除账本项。
    /// 调用点（Operator 接线）：Apply 之后、5s IntegrityPass（配置关也要跑，
    /// 走还原分支）、SetGuardSlot/EnterGuardSlot/ExitGuardSlot postfix——
    /// 首次 EnterGuardSlot postfix 时原生刚把扫描器设到 12（基线），本方法
    /// 先捕获 12 再放 18，后续调用直接 18。不写金币/间隔/SO。
    /// </summary>
    internal static void ReconcileTowerRange(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null)
            {
                // 实例已亡：无从还原字段（对象已不在），只清账本防泄漏。
                // 池指针复用由 Owner 指针校验兜底。
                PruneDeadStates();
                return;
            }

            IntPtr key = archer.Pointer;
            TowerState state;
            _towerStates.TryGetValue(key, out state);
            if (state != null && (state.Owner == null || state.Owner.Pointer != key))
                state = null; // 指针复用残影：视为无状态

            bool marker = archer.gameObject.activeInHierarchy && PatchRoles_Crossbowman.IsCrossbowman(archer);
            bool onTower = archer.inGuardSlot || archer._guardSlot != null;

            if (marker && ModConfig.Enabled.Value)
            {
                if (state == null)
                {
                    state = new TowerState { Owner = archer, Baseline = archer.towerShootRange };
                    _towerStates[key] = state;
                }
                float boosted = state.Baseline * TowerRangeMultiplier;
                if (Mathf.Abs(archer.towerShootRange - boosted) > 0.01f)
                    archer.towerShootRange = boosted; // 幂等 + 自愈（原生重置路径）
                if (onTower) SyncTowerScanner(archer, boosted, true);
                if (!_loggedTowerBoost)
                {
                    _loggedTowerBoost = true;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        "[CrossbowDefense] tower range boosted: " + state.Baseline.ToString("F1")
                        + " -> " + boosted.ToString("F1"));
                }
                return;
            }

            // 丢标记 / 配置关：还原并销账（对已无状态者为 no-op）。
            if (state != null) RestoreTowerState(archer, state, onTower);
        }
        catch (Exception e)
        {
            LogErrorOnce("tower", e);
        }
    }

    /// <summary>
    /// 显式还原单个弩手的塔位射程并销账（Strip 销毁 marker 之前调用）。
    /// 只在当前值仍等于我们放的放大值时写回基线——绝不覆写游戏字段在
    /// 我们销账之后的任何写入。
    /// </summary>
    internal static void Remove(Archer archer)
    {
        try
        {
            if (archer == null) return;
            IntPtr key = archer.Pointer;
            if (!_towerStates.TryGetValue(key, out TowerState state)) return;
            if (state.Owner == null || state.Owner.Pointer != key) { _towerStates.Remove(key); return; }
            bool onTower = archer.inGuardSlot || archer._guardSlot != null;
            RestoreTowerState(archer, state, onTower);
        }
        catch (Exception e)
        {
            LogErrorOnce("remove", e);
        }
    }

    /// <summary>
    /// 还原：towerShootRange 只在仍等于我们的放大值时写回基线（防覆写游戏
    /// 后续合法写入）；在塔时扫描器同样只在仍等于放大值时还原基线（离塔后
    /// 扫描器归原生/Strip 的地面路径管，不碰）。还原后销账。
    /// </summary>
    private static void RestoreTowerState(Archer archer, TowerState state, bool onTower, bool quiet = false)
    {
        IntPtr key = archer.Pointer;
        float boosted = state.Baseline * TowerRangeMultiplier;
        if (Mathf.Abs(archer.towerShootRange - boosted) <= 0.01f)
            archer.towerShootRange = state.Baseline;
        // Native OnDisable can clear tower flags before this postfix runs.
        // Reclaim our scanner value on the ground too, preserving foreign values.
        SyncTowerScanner(archer, boosted, false, restoreTo: onTower ? state.Baseline : archer.shootRange);
        _towerStates.Remove(key);
        if (!quiet && !_loggedTowerRestore)
        {
            _loggedTowerRestore = true;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[CrossbowDefense] tower range restored to " + state.Baseline.ToString("F1"));
        }
    }

    /// <summary>
    /// 扫描器同步：boost=true 时设为 value（我们维护的塔位增益）；
    /// boost=false 时仅在仍等于 value（我们的增益还在）时还原 restoreTo
    /// （基线塔值）——外来源（火矢/其他系统）写过的扫描器绝不动。
    /// </summary>
    private static void SyncTowerScanner(Archer archer, float value, bool boost, float restoreTo = 0f)
    {
        Scanner scanner = archer._enemyScanner;
        if (scanner == null) return;
        if (boost)
        {
            if (scanner.range != value) scanner.range = value;
            if (scanner.rangeBehind != value) scanner.rangeBehind = value;
            return;
        }
        if (Mathf.Abs(scanner.range - value) <= 0.01f)
        {
            scanner.range = restoreTo;
        }
        if (Mathf.Abs(scanner.rangeBehind - value) <= 0.01f) scanner.rangeBehind = restoreTo;
    }

    /// <summary>
    /// 清理宿主已亡的账本项（Reconcile 空实例时顺带；对象已亡无从写字段，
    /// 池复用由 Owner 指针校验兜底，账本体量≤弩手数，无需周期任务）。
    /// </summary>
    private static void PruneDeadStates()
    {
        List<IntPtr> dead = null;
        foreach (KeyValuePair<IntPtr, TowerState> pair in _towerStates)
        {
            TowerState state = pair.Value;
            if (state == null || state.Owner == null || state.Owner.Pointer != pair.Key
                || state.Owner.gameObject == null)
            {
                dead ??= new List<IntPtr>();
                dead.Add(pair.Key);
            }
        }
        if (dead == null) return;
        for (int i = 0; i < dead.Count; i++) _towerStates.Remove(dead[i]);
    }
}

[HarmonyPatch(typeof(Archer), "SetGuardSlot")]
internal static class Archer_SetGuardSlot_CrossbowDefense_Patch
{
    [HarmonyPostfix] private static void Postfix(Archer __instance) => PatchRoles_CrossbowDefense.ReconcileTowerRange(__instance);
}
[HarmonyPatch(typeof(Archer), "EnterGuardSlot")]
internal static class Archer_EnterGuardSlot_CrossbowDefense_Patch
{
    [HarmonyPostfix] private static void Postfix(Archer __instance) => PatchRoles_CrossbowDefense.ReconcileTowerRange(__instance);
}
[HarmonyPatch(typeof(Archer), "ExitGuardSlot")]
internal static class Archer_ExitGuardSlot_CrossbowDefense_Patch
{
    [HarmonyPostfix] private static void Postfix(Archer __instance) => PatchRoles_CrossbowDefense.ReconcileTowerRange(__instance);
}
[HarmonyPatch(typeof(Archer), "OnDisable")]
internal static class Archer_OnDisable_CrossbowDefense_Patch
{
    [HarmonyPostfix] private static void Postfix(Archer __instance) => PatchRoles_CrossbowDefense.Remove(__instance);
}
