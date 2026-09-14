using System;
using System.Collections.Generic;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 坐骑无限体力（2026-09-14 需求，可选功能，默认关）：本机有控制权的玩家当前所骑的
/// 具体坐骑永不耗尽，已疲惫的坐骑开启即能恢复；关掉后不干预、不回滚。
///
/// 挂钩点一：Player.UpdateActionState(int,bool,bool,bool,bool)（2.1.0 源码 Player.cs:1189；
/// 2.4 interop public unsafe void UpdateActionState；native rva 0x699eb0 / 4240B / 唯一）。
/// 它是**移动体力唯一的消耗与恢复路径**（技能旁路另有三处，见下）：
///   Stand  → Stamina += standStaminaRate * Time.deltaTime
///   Walk   → Stamina += walkStaminaRate  * Time.deltaTime
///   Run    → if (WellFedTimer &lt;= 0f &amp;&amp; !Player.DebugInfiniteStamina) Stamina += runStaminaRate * dt
///   Glide  → Stamina += glideStaminaRate * Time.deltaTime
/// 末尾统一 Mathf.Clamp01(Stamina)；Run 分支在 Stamina&lt;=0 时会走 reserveProbability 或
/// SetActionState(Walk)+BecomeTired()，TryToGallop 又被 IsTired 挡住。
///
/// 语义（本文件只碰下列字段，别的字段一律不写）：
/// 1) Prefix：把当前坐骑四个 staminaRate 里【负】的临时归 0（正速率是原生恢复，保持原样），
///    并在原生体执行前把 Stamina 拉满、_tiredTimer 清 0。
///    清 _tiredTimer 就是需求本身（"已疲惫的坐骑开启即能恢复"）：IsTired =&gt; _tiredTimer &gt; Time.time，
///    不清则原版 TryToGallop 永远只 Rear。关闭后不恢复旧疲劳（按需求从现体力自然消耗）。
/// 2) Postfix：原生跑完后（含末尾 Clamp01）若开关/世界/骑乘关系都还成立，再拉满一次 Stamina。
///    为什么还要 Postfix：极端 Time.deltaTime（掉帧、读盘停帧）下若只靠 Prefix 补满，原生仍
///    可能在同一帧内扣穿并走进枯竭/疲劳，因此 Prefix 已经把负速率当次归 0，Postfix 再兜一次。
/// 3) Finalizer：只归还借用的速率，绝不吞原生异常（return __exception），cleanup 自身异常也
///    不外抛；与 Postfix 共享同一个 __state 对象，只归还一次（见 Borrow）。
///
/// 归还用 CAS：仅当字段仍等于"我写进去的 0"时才写回原值。被别的逻辑改写过的（例如
/// GlideMovementSteedAbility 会写 glideStaminaRate）一律不硬覆盖。
///
/// 为什么不写全局 Player.DebugInfiniteStamina：那是原版调试开关，属进程级长期状态且被
/// Run 分支与 TryToGallop 之外的地方共用；本 patch 只动具体坐骑实例，且只在自己这一次
/// 调用期间持有（异常/开关变化/重入都会归还）。
///
/// 疲劳 puff 与联机同步沿用原版：Player.Update 每帧在 hasLocalAuthority 下按
/// `IsTired || Stamina &lt;= PuffThreshold` 自行翻转 PuffActive 并调 SendStaminaState()。
/// 本 patch 不发任何 RPC、不长期占任何全局 flag、不动 prefab/共享 asset。
///
/// 接管条件（从传入的 player 出发，不依赖坐骑反查）：本开关 + OptionalQoLScope.IsActive
/// （总开关 + 已在具体 biome，排除主菜单）+ player.hasLocalAuthority（不用
/// NetworkBigBoss.HasWorldAuth：客户端本机玩家 HasWorldAuth 为 false，用它会把联机本机玩家
/// 一并排掉）+ OptionalQoLScope.IsCurrent(player) + 确有坐骑、坐骑在当前层、且
/// Steed.Rider 就是该 player（双向匹配；排除"远端 player._steed 误指本地坐骑"这类串线）。
///
/// 挂钩点二~四（技能体力旁路，2.1 源码里除 UpdateActionState 外仅此三处直接写 Steed.Stamina，
/// 2.4 native 侧经 ability-audit 核实同样只有三处写 [+0x44c]）：
///   SteedAbility.cs:88（base Activate：Stamina -= _staminaCost）native 0x7961d0 / 272B / 唯一；
///     12 个派生类 Activate 都 call/jmp base（native 侧已核，无内联遗漏），故基类钩子全覆盖。
///   GlideMovementSteedAbility.cs:101（override Activate，独立实现不调 base；staminaWrites 命中）
///     native 0x77f2a0 / 784B / 唯一。
///   RunningAttackSteedAbility.cs:71（OnPushedObjects：Stamina += ±_attackStamina*）
///     native 0x794480 / 496B / 唯一。
/// 三处都在 `!Player.DebugInfiniteStamina` 之下；本功能不写那个全局开关，所以必须自己补回。
/// 这组入口只做"前后各补满一次"，不借用任何字段，因此不需要 finalizer：原生异常照常透传，
/// 一帧后的主钩子会自然恢复。Postfix 的意义是保证返回派生体/UpdateActionState 同步回调之前
/// 体力已经回到满，紧接的原生 Run 分支不会因这一笔技能消耗走进枯竭+BecomeTired。
///
/// root 需要做的事：无（ModConfig 已有 section "Player" / key "InfiniteSteedStamina" 默认 false
/// 与 F5 便捷页首张卡（该页共 4 项）；PatchAll 自动发现本文件 4 个标注类，无需 Tick、无需手动注册）。
/// </summary>
[HarmonyPatch(typeof(Player), nameof(Player.UpdateActionState))]
public static class PatchRide_InfiniteStamina
{
    /// <summary>本次调用借用的四个速率字段凭据（先记全凭据再写字段）。</summary>
    internal struct RateBorrow
    {
        internal bool RunOwned;
        internal float RunBefore;
        internal bool WalkOwned;
        internal float WalkBefore;
        internal bool StandOwned;
        internal float StandBefore;
        internal bool GlideOwned;
        internal float GlideBefore;
    }

    /// <summary>
    /// Harmony __state（引用类型，仅功能实际生效时 new，默认关 = 零分配）。
    /// 用 class 而不是 struct：Postfix 与 Finalizer 拿到的是同一个对象，Clean 标记保证
    /// "归还"只发生一次 —— 否则 Postfix 归还后若字段又被别人写成 0，按值持有的 Finalizer
    /// 会把它再覆盖回原值。内部状态即使被外部改写也不影响已归还的事实。
    /// </summary>
    internal sealed class Borrow
    {
        /// <summary>已执行过归还（置位在真正写字段之前）：后续 Postfix/Finalizer 直接跳过。</summary>
        internal bool Cleaned;
        internal Player Player;
        internal IntPtr PlayerPtr;
        internal Steed Steed;
        internal IntPtr SteedPtr;
        internal int SteedInstanceId;
        internal RateBorrow Rates;
    }

    /// <summary>技能入口的 __state：只固定本次 entry 的身份，不借用任何字段。</summary>
    internal sealed class AbilityBorrow
    {
        internal SteedAbility Ability;
        internal IntPtr AbilityPtr;
        internal Steed Steed;
        internal IntPtr SteedPtr;
        internal int SteedInstanceId;
        internal Player Rider;
        internal IntPtr RiderPtr;
    }

    // ------------------------------------------------------------------ hooks

    [HarmonyPrefix]
    private static void UpdateActionState_Prefix(Player __instance, out Borrow __state)
        => OnActionStateEnter(__instance, out __state);

    [HarmonyPostfix]
    private static void UpdateActionState_Postfix(Borrow __state)
        => OnActionStateExit(__state, true);

    [HarmonyFinalizer]
    private static Exception UpdateActionState_Finalizer(Exception __exception, Borrow __state)
    {
        OnActionStateExit(__state, false);
        return __exception;
    }

    // ------------------------------------------------------------ 移动体力路径

    /// <summary>
    /// 前缀：解析当前坐骑 → 建 state → 记凭据 → 当次归零负速率 → 拉满体力、清疲劳。
    /// 自身绝不外抛：拿不到身份凭据宁可不接管；写字段中途异常也只归还、外层照常执行原生。
    /// </summary>
    internal static void OnActionStateEnter(Player player, out Borrow state)
    {
        state = null;
        try
        {
            if (!TryResolveSteed(player, out Steed steed)) return;

            // 身份凭据必须最早固定：没有 InstanceID 就没有归还目标，一律不借用。
            int instanceId;
            try { instanceId = steed.gameObject.GetInstanceID(); }
            catch (Exception) { return; }

            var borrow = new Borrow
            {
                Player = player,
                PlayerPtr = player.Pointer,
                Steed = steed,
                SteedPtr = steed.Pointer,
                SteedInstanceId = instanceId
            };
            state = borrow;

            // 先读全、记全凭据（owned 一律在写 0 之前落账：setter 抛异常时结果未知，
            // 由归还时的 CAS 收尾），再写字段。
            float run = steed.runStaminaRate;
            if (run < 0f) { borrow.Rates.RunOwned = true; borrow.Rates.RunBefore = run; }
            float walk = steed.walkStaminaRate;
            if (walk < 0f) { borrow.Rates.WalkOwned = true; borrow.Rates.WalkBefore = walk; }
            float stand = steed.standStaminaRate;
            if (stand < 0f) { borrow.Rates.StandOwned = true; borrow.Rates.StandBefore = stand; }
            float glide = steed.glideStaminaRate;
            if (glide < 0f) { borrow.Rates.GlideOwned = true; borrow.Rates.GlideBefore = glide; }

            if (borrow.Rates.RunOwned) steed.runStaminaRate = 0f;
            if (borrow.Rates.WalkOwned) steed.walkStaminaRate = 0f;
            if (borrow.Rates.StandOwned) steed.standStaminaRate = 0f;
            if (borrow.Rates.GlideOwned) steed.glideStaminaRate = 0f;

            steed.Stamina = 1f;
            steed._tiredTimer = 0f;
        }
        catch (Exception exception)
        {
            // 已记凭据的字段在这里归还；归还与日志自身都不外抛，原生体继续执行。
            RestoreRates(state);
            state = null;                 // 已归还：Postfix/Finalizer 不再介入
            LogErrorOnce("prefix failed", exception);
        }
    }

    /// <summary>
    /// 后缀/终结器共用出口：refill 时在身份/骑乘关系/world/scene/开关都仍成立的前提下再
    /// 拉满一次；随后无条件归还借用的速率（即使开关已关、已换坐骑、已离开 world 也照还）。
    /// </summary>
    internal static void OnActionStateExit(Borrow state, bool refill)
    {
        if (state == null) return;
        if (refill)
        {
            try
            {
                if (StillRidingSameSteed(state)) state.Steed.Stamina = 1f;
            }
            catch (Exception exception)
            {
                LogErrorOnce("refill failed", exception);
            }
        }
        RestoreRates(state);
    }

    /// <summary>
    /// 接管条件（从传入的 player 出发）：本机对该 player 有控制权、player 在当前 world/scene、
    /// 它有具体坐骑、坐骑也在当前层、且 Steed.Rider 就是该 player。
    /// 不从坐骑反查 player：否则 remote._steed 若误指别台机器本地玩家的坐骑，会被误判为可改。
    /// </summary>
    private static bool TryResolveSteed(Player player, out Steed steed)
    {
        steed = null;
        if (player == null || !FeatureOn()) return false;
        if (!player.hasLocalAuthority) return false;
        if (!OptionalQoLScope.IsCurrent(player)) return false;
        Steed current = player._steed;
        if (current == null) return false;
        if (!OptionalQoLScope.IsCurrent(current)) return false;
        Player rider = current.Rider;
        if (rider == null || rider.Pointer != player.Pointer) return false;
        steed = current;
        return true;
    }

    /// <summary>
    /// Postfix 生效条件：仍是同一 player/同一坐骑实例（Pointer+InstanceID）、该 player 仍是 rider
    /// 且仍本机有控制权、仍是他的当前坐骑、仍在当前 world/scene、开关仍开。任一不成立就只归还、不补满。
    /// </summary>
    private static bool StillRidingSameSteed(Borrow state)
    {
        if (!FeatureOn()) return false;
        Player player = state.Player;
        if (player == null || player.Pointer != state.PlayerPtr) return false;
        if (!player.hasLocalAuthority) return false;
        Steed steed = state.Steed;
        if (!SameInstance(steed, state.SteedPtr, state.SteedInstanceId)) return false;
        Steed current = player._steed;
        if (current == null || current.Pointer != state.SteedPtr) return false;
        Player rider = steed.Rider;
        if (rider == null || rider.Pointer != state.PlayerPtr) return false;
        return OptionalQoLScope.IsCurrent(player) && OptionalQoLScope.IsCurrent(steed);
    }

    /// <summary>
    /// 归还本次写入的 0（CAS：字段已被别人改写就保留别人的值）。同一 state 只归还一次
    /// （先置 Cleaned 再写字段），因此 Postfix 之后的任何写入都不会被 Finalizer 覆盖；
    /// 归还只认捕获的坐骑身份，与"当前是否仍可接管"无关。每字段各自兜异常，绝不外抛。
    /// </summary>
    private static void RestoreRates(Borrow state)
    {
        if (state == null || state.Cleaned) return;
        state.Cleaned = true;

        Steed steed = state.Steed;
        // 身份核验（包括 Unity 空对象判定）失败时不让 cleanup 向外抛。
        try
        {
            if (steed == null) return;
            if (steed.Pointer != state.SteedPtr) return;
            if (steed.gameObject.GetInstanceID() != state.SteedInstanceId) return;
        }
        catch (Exception)
        {
            return;
        }

        RateBorrow rates = state.Rates;
        if (rates.RunOwned)
        {
            try { if (steed.runStaminaRate == 0f) steed.runStaminaRate = rates.RunBefore; }
            catch (Exception exception) { LogErrorOnce("rate restore failed (run)", exception); }
        }
        if (rates.WalkOwned)
        {
            try { if (steed.walkStaminaRate == 0f) steed.walkStaminaRate = rates.WalkBefore; }
            catch (Exception exception) { LogErrorOnce("rate restore failed (walk)", exception); }
        }
        if (rates.StandOwned)
        {
            try { if (steed.standStaminaRate == 0f) steed.standStaminaRate = rates.StandBefore; }
            catch (Exception exception) { LogErrorOnce("rate restore failed (stand)", exception); }
        }
        if (rates.GlideOwned)
        {
            try { if (steed.glideStaminaRate == 0f) steed.glideStaminaRate = rates.GlideBefore; }
            catch (Exception exception) { LogErrorOnce("rate restore failed (glide)", exception); }
        }
    }

    // ------------------------------------------------------------ 技能体力旁路

    /// <summary>
    /// 技能入口前缀：只在"该坐骑属于本机有控制权的当前 rider"时建 state 并补满一次。
    /// 不合格就不建 state —— 因此"进时 off、出时 on"不会在 Postfix 里无端介入。
    /// </summary>
    internal static void OnAbilityEnter(SteedAbility ability, out AbilityBorrow state)
    {
        state = null;
        try
        {
            if (!TryResolveAbilityTarget(ability, out Steed steed, out Player rider)) return;
            int instanceId;
            try { instanceId = steed.gameObject.GetInstanceID(); }
            catch (Exception) { return; }

            state = new AbilityBorrow
            {
                Ability = ability,
                AbilityPtr = ability.Pointer,
                Steed = steed,
                SteedPtr = steed.Pointer,
                SteedInstanceId = instanceId,
                Rider = rider,
                RiderPtr = rider.Pointer
            };
            steed.Stamina = 1f;
            steed._tiredTimer = 0f;
        }
        catch (Exception exception)
        {
            state = null;
            LogErrorOnce("ability entry refill failed", exception);
        }
    }

    /// <summary>
    /// 技能入口后缀：只对前缀捕获的同一 ability/同一坐骑实例、同一 rider 且其仍本机有控制权、
    /// 仍是他当前坐骑、仍在当前 world、开关仍开时补满。绝不重新解析 ability._steed
    /// （回调期间被换掉的 target 不归本次 entry 管）。
    /// </summary>
    internal static void OnAbilityExit(AbilityBorrow state)
    {
        if (state == null) return;
        try
        {
            if (!AbilityTargetStillValid(state)) return;
            state.Steed.Stamina = 1f;
            state.Steed._tiredTimer = 0f;
        }
        catch (Exception exception)
        {
            LogErrorOnce("ability entry refill failed", exception);
        }
    }

    /// <summary>技能入口接管条件：坐骑的 Rider 就是本机有控制权的当前 player（双向匹配），
    /// 且 ability/player/steed 都仍在当前 world/scene。不单独依赖 ability._rider 判权。</summary>
    private static bool TryResolveAbilityTarget(SteedAbility ability, out Steed steed, out Player rider)
    {
        steed = null;
        rider = null;
        if (ability == null || !FeatureOn()) return false;
        if (!OptionalQoLScope.IsCurrent(ability)) return false;
        Steed candidate = ability._steed;
        if (candidate == null) return false;
        Player owner = candidate.Rider;
        if (owner == null || !owner.hasLocalAuthority) return false;
        if (!OptionalQoLScope.IsCurrent(owner) || !OptionalQoLScope.IsCurrent(candidate)) return false;
        Steed current = owner._steed;
        if (current == null || current.Pointer != candidate.Pointer) return false;
        steed = candidate;
        rider = owner;
        return true;
    }

    private static bool AbilityTargetStillValid(AbilityBorrow state)
    {
        if (!FeatureOn()) return false;
        SteedAbility ability = state.Ability;
        if (ability == null || ability.Pointer != state.AbilityPtr) return false;
        Steed steed = state.Steed;
        if (!SameInstance(steed, state.SteedPtr, state.SteedInstanceId)) return false;

        Player rider = steed.Rider;
        if (rider == null || rider.Pointer != state.RiderPtr) return false;
        if (!rider.hasLocalAuthority) return false;
        Steed current = rider._steed;
        if (current == null || current.Pointer != state.SteedPtr) return false;

        return OptionalQoLScope.IsCurrent(ability)
            && OptionalQoLScope.IsCurrent(rider)
            && OptionalQoLScope.IsCurrent(steed);
    }

    // ------------------------------------------------------------------ 共用

    /// <summary>捕获的坐骑是否仍是同一个实例（Pointer + InstanceID 双核，防 wrapper 被换 identity）。</summary>
    private static bool SameInstance(Steed steed, IntPtr pointer, int instanceId)
    {
        if (steed == null || steed.Pointer != pointer) return false;
        try { return steed.gameObject.GetInstanceID() == instanceId; }
        catch (Exception) { return false; }
    }

    /// <summary>本开关 + 仓库统一的 OptionalQoL 作用域（总开关 + 已在具体 biome，排除主菜单）。</summary>
    private static bool FeatureOn()
    {
        try
        {
            return OptionalQoLScope.IsActive
                && ModConfig.InfiniteSteedStamina != null
                && ModConfig.InfiniteSteedStamina.Value;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static readonly HashSet<string> LoggedErrors = new HashSet<string>();

    /// <summary>日志本身也不外抛：cleanup 路径不允许因为日志失败打断原生。</summary>
    private static void LogErrorOnce(string key, Exception exception)
    {
        try
        {
            if (!LoggedErrors.Add(key)) return;
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                $"[RideInfiniteStamina] {key}: {exception}");
        }
        catch (Exception)
        {
        }
    }
}

/// <summary>
/// 技能体力入口 1/3：SteedAbility.Activate（2.4 interop: public unsafe virtual void Activate；
/// native 0x7961d0 / 272B / 唯一）。12 个派生类的 Activate 都是 call/jmp base（native 侧已核，
/// 无内联遗漏），故基类钩子覆盖它们全部。Postfix 在 base.Activate() 返回处执行 —— 派生体剩余
/// 部分与紧随其后的 UpdateActionState 同步回调都看到满体力，不会因这一笔 _staminaCost 走进
/// 枯竭+BecomeTired。不改写 _staminaCost/_cooldown/_duration，原生排程与副作用完全保留。
/// </summary>
[HarmonyPatch(typeof(SteedAbility), nameof(SteedAbility.Activate))]
internal static class PatchRide_InfiniteStaminaAbility
{
    [HarmonyPrefix]
    private static void Prefix(SteedAbility __instance, out PatchRide_InfiniteStamina.AbilityBorrow __state)
        => PatchRide_InfiniteStamina.OnAbilityEnter(__instance, out __state);

    [HarmonyPostfix]
    private static void Postfix(PatchRide_InfiniteStamina.AbilityBorrow __state)
        => PatchRide_InfiniteStamina.OnAbilityExit(__state);
}

/// <summary>
/// 技能体力入口 2/3：GlideMovementSteedAbility.Activate
/// （2.4 interop: public unsafe override void Activate；native 0x77f2a0 / 784B / 唯一；
/// 独立实现、不调用 base.Activate()）。原版先用 `_steed.Stamina &lt; _staminaCost` 判断，不达标就
/// Rear 并放弃滑翔：Prefix 补满让门槛不再由体力卡住，Postfix 把当次 `Stamina -= _staminaCost`
/// （或 WellFedTimer 扣减）补回。
/// </summary>
[HarmonyPatch(typeof(GlideMovementSteedAbility), nameof(GlideMovementSteedAbility.Activate))]
internal static class PatchRide_InfiniteStaminaGlide
{
    [HarmonyPrefix]
    private static void Prefix(SteedAbility __instance, out PatchRide_InfiniteStamina.AbilityBorrow __state)
        => PatchRide_InfiniteStamina.OnAbilityEnter(__instance, out __state);

    [HarmonyPostfix]
    private static void Postfix(PatchRide_InfiniteStamina.AbilityBorrow __state)
        => PatchRide_InfiniteStamina.OnAbilityExit(__state);
}

/// <summary>
/// 技能体力入口 3/3：RunningAttackSteedAbility.OnPushedObjects
/// （2.4 interop: public unsafe void OnPushedObjects；native 0x794480 / 496B / 唯一；
/// 2.1 源码为 private，内容不变）。原版按是否撞到层内目标
/// `Stamina += ±(_attackStaminaGain/_attackStaminaCost)`：撞到加、没撞到扣。前后补满保证连续
/// 冲撞不会把坐骑耗空；不改写 _attackStaminaCost/_attackStaminaGain/_attackWellFedGain，
/// 也不动 WellFedTimer。
/// </summary>
[HarmonyPatch(typeof(RunningAttackSteedAbility), nameof(RunningAttackSteedAbility.OnPushedObjects))]
internal static class PatchRide_InfiniteStaminaRunningAttack
{
    [HarmonyPrefix]
    private static void Prefix(SteedAbility __instance, out PatchRide_InfiniteStamina.AbilityBorrow __state)
        => PatchRide_InfiniteStamina.OnAbilityEnter(__instance, out __state);

    [HarmonyPostfix]
    private static void Postfix(PatchRide_InfiniteStamina.AbilityBorrow __state)
        => PatchRide_InfiniteStamina.OnAbilityExit(__state);
}
