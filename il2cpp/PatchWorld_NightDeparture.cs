using System;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 普通远距夜袭出发补偿（issue-94）：只 hook 两个已核的实际 2.4 独立长入口——
/// Director.ScheduleWaveToday(Wave,Side,float) RVA 0x4e57a0（1792 字节、sameSlots 1）与
/// EnemyManager.GetWaveTravelTime(Wave,float,int) RVA 0x500a20（272 字节、sameSlots 1）。
/// 短 getter（CurrentSeasonDay RVA 0x4e8140，sameSlots 3）只经 interop 调用、绝不 hook。
///
/// 接线：prefix 只建立本次调用的 ThreadStatic 短生命周期 scope；postfix 只在身份/上下文
/// 全部匹配时改写这一次 GetWaveTravelTime 返回值（原生仍选同一门、创建同一波与同一
/// OpenPortal/SpawnWave 事件，arrival 实参与波参数不动）。补偿量由
/// <see cref="NightDepartureTiming"/> 决定（最多 2 游戏小时，近门/黄昏/晚调用/次日白天
/// 原生计划一律保持）。不改事件表、速度、数量、晨退、时钟、血月、门状态或存档结构；
/// 无新增 Update/扫描/UI/配置项，沿用总开关 ModConfig.Enabled。
///
/// 作用域安全：每个 ScheduleWaveToday 都压 frame（不合格内层也压 disabled mask，不继承
/// 外层授权）；深度上限 16，超限整棵子树以 overflow 计数屏蔽。__state 携带递增 token 与
/// 前层 token，finalizer 只有当前 top/token 的合法清理才退本层（复制旧 state 重清弹不了
/// 别的层）；无论原生成功/异常都把 __exception 原样交回（不吞原生异常），清理幂等。
/// 每 scope 的匹配结果最多消费一次（错误 day/身份失配的查询不消费），scope 外绝不改值。
/// </summary>
[HarmonyPatch(typeof(Director), nameof(Director.ScheduleWaveToday))]
internal static class PatchWorld_NightDeparture
{
    [HarmonyPrefix]
    internal static void ScheduleWaveToday_Prefix(Director __instance, Wave __0, Side __1, float __2,
        ref NightDepartureScope.State __state)
    {
        __state = default;
        int slot = NightDepartureScope.Push(ref __state);
        if (slot < 0) return; // 深度超限：state 已记为 overflow mask，finalizer 按_token 退层
        try
        {
            NightDepartureScope.Arm(slot, __instance, __0, __1, __2);
        }
        catch (Exception e)
        {
            // 探测失败：frame 保持 disabled（原生结果不动），finalizer 仍会正常退层。
            NightDepartureScope.Disarm(slot);
            NightDepartureLog.ReadFailure(e);
        }
    }

    [HarmonyFinalizer]
    internal static Exception ScheduleWaveToday_Finalizer(Exception __exception,
        ref NightDepartureScope.State __state)
    {
        NightDepartureScope.Pop(ref __state);
        return __exception;
    }
}

[HarmonyPatch(typeof(EnemyManager), nameof(EnemyManager.GetWaveTravelTime))]
internal static class PatchWorld_NightDepartureTravel
{
    /// <summary>
    /// 只改写本次返回 T。__2（day 实参）必须与 scope 绑定的 CurrentDayForSpawning 一致：
    /// 原生当前未使用该参数不等于可以放弃这层调用隔离，错误日的查询保持原值且不消费
    /// 本 scope 的唯一一次资格。全量验证成功后才最后写 __result；任何异常保持原生结果。
    /// </summary>
    [HarmonyPostfix]
    internal static void GetWaveTravelTime_Postfix(EnemyManager __instance, Wave __0, float __1, int __2,
        ref float __result)
    {
        try
        {
            if (NightDepartureScope.TryAdjustTravel(__instance, __0, __result, __2, out float adjusted))
                __result = adjusted;
        }
        catch (Exception e)
        {
            // 自有读取/计算异常只留有界诊断，绝不改写原生返回、绝不外抛。
            NightDepartureLog.PostfixFailure(e);
        }
    }
}

/// <summary>
/// ThreadStatic 短生命周期 scope 栈：值类型 frame 定长数组（≤16）+ 递增 token，无逐调用
/// heap 分配、无全局注册表。Director/EnemyManager 可能在持久场景，因此按各自
/// gameObject.scene 独立绑定并复核（不要求持久 scene 等于 world 的 scene），
/// world/gameLayer 另按指针与其 scene 三重绑定，换岛/换场景即失配。
/// </summary>
internal static class NightDepartureScope
{
    internal const int MaxDepth = 16;
    private const int SideLeft = -1;
    private const int SideRight = 1;

    internal enum PushKind : byte { None = 0, Frame = 1, Overflow = 2 }

    /// <summary>本次调用的压栈凭据：Kind + 本层 token + 前层 token（值类型，随 finalizer 归还）。</summary>
    internal struct State
    {
        internal PushKind Kind;
        internal int Token;
        internal int PrevTopToken;
    }

    [ThreadStatic] private static Frame[] _stack;
    [ThreadStatic] private static int _depth;     // 有效 frame 数 [0, MaxDepth]
    [ThreadStatic] private static int _overflow;  // 深度超限的嵌套层数；>0 时整棵子树屏蔽
    [ThreadStatic] private static int _clock;     // 单调递增 token（int 回绕在 2^31 次压栈后才可能）
    [ThreadStatic] private static int _topToken;  // 当前最顶一层的 token（frame 或 overflow）

    private struct Frame
    {
        internal byte Armed;      // prefix 验证全部通过，等待本次匹配的 GetWaveTravelTime
        internal byte Consumed;   // 本 scope 的匹配结果已处理（无论是否提交补偿）
        internal int Token;
        internal IntPtr Director, Managers, Enemies, Wave, World, GameLayer;
        internal int SceneHandle, DirectorSceneHandle, EnemiesSceneHandle;
        internal int SideValue, SchedulerDay, SeasonDay, SpawnDay;
        internal float Arrival, EveningStart, NextDawnStart;
    }

    /// <summary>
    /// 压入一个默认 disabled 的 frame（返回槽位）；深度超限压 overflow mask（返回 -1）。
    /// state 记录本层与前层 token，Pop 据此做所有权校验。
    /// </summary>
    internal static int Push(ref State state)
    {
        int token = ++_clock;
        if (_overflow > 0 || _depth >= MaxDepth)
        {
            _overflow++;
            state.Kind = PushKind.Overflow;
            state.Token = token;
            state.PrevTopToken = _topToken;
            _topToken = token;
            return -1;
        }
        _stack ??= new Frame[MaxDepth];
        _stack[_depth] = default;
        _stack[_depth].Token = token; // 压栈即定 token：Arm 早退/异常不影响 finalizer 退层
        state.Kind = PushKind.Frame;
        state.Token = token;
        state.PrevTopToken = _topToken;
        _topToken = token;
        return _depth++;
    }

    /// <summary>填充 frame 资格。任何早退都保持 disabled（原生排期完全不受影响）。</summary>
    internal static void Arm(int slot, Director director, Wave wave, Side side, float arrival)
    {
#if ANDROID
        if (!ModConfig.Enabled.Value || !ModConfig.NightDepartureEnabled.Value) return;
#else
        if (!ModConfig.Enabled.Value) return;
#endif
        if (director == null || wave == null) return;
        if (director.Pointer == IntPtr.Zero || wave.Pointer == IntPtr.Zero) return;
        if (!NetworkBigBoss.HasWorldAuth) return;
        if (wave.type != WaveType.Regular) return;
        int sideValue = (int)side;
        if (sideValue != SideLeft && sideValue != SideRight) return;

        Managers managers = Managers.Inst;
        if (managers == null || managers.Pointer == IntPtr.Zero) return;
        Director activeDirector = managers.director;
        EnemyManager enemies = managers.enemies;
        World world = managers.world;
        if (enemies == null || world == null || activeDirector == null) return;
        if (enemies.Pointer == IntPtr.Zero || world.Pointer == IntPtr.Zero) return;
        // Managers 当前 director 必须就是本次调用者（帧绑定调用实例本身）。
        if (activeDirector.Pointer != director.Pointer) return;

        GameObject directorObject = director.gameObject;
        GameObject enemiesObject = enemies.gameObject;
        if (directorObject == null || enemiesObject == null) return;
        Transform gameLayer = world.gameLayer;
        if (gameLayer == null || gameLayer.Pointer == IntPtr.Zero) return;
        GameObject layerObject = gameLayer.gameObject;
        if (layerObject == null) return;
        // 持久场景身份独立绑定，不与 world 的 scene 互相比较。
        int directorScene = directorObject.scene.handle;
        int enemiesScene = enemiesObject.scene.handle;
        int worldScene = layerObject.scene.handle;

        int schedulerDay = director.CurrentSchedulerDay;
        int seasonDay = director.CurrentSeasonDay;
        int spawnDay = director.CurrentDayForSpawning;
        if (seasonDay == int.MaxValue) return; // seasonDay+1 会溢出：不补偿
        // E 取当日、D 取次日真实 profile（跨季节/周期日相变化各自按日查询），只读不缓存。
        TimesOfDay today = director.GetTimesOfDayForDay(seasonDay);
        TimesOfDay tomorrow = director.GetTimesOfDayForDay(seasonDay + 1);
        if (today == null || tomorrow == null) return;

        ref Frame frame = ref _stack[slot];
        frame.Armed = 1;
        frame.Consumed = 0;
        frame.Director = director.Pointer;
        frame.Managers = managers.Pointer;
        frame.Enemies = enemies.Pointer;
        frame.Wave = wave.Pointer;
        frame.World = world.Pointer;
        frame.GameLayer = gameLayer.Pointer;
        frame.SceneHandle = worldScene;
        frame.DirectorSceneHandle = directorScene;
        frame.EnemiesSceneHandle = enemiesScene;
        frame.SideValue = sideValue;
        frame.SchedulerDay = schedulerDay;
        frame.SeasonDay = seasonDay;
        frame.SpawnDay = spawnDay;
        frame.Arrival = arrival;
        frame.EveningStart = today.eveningStart;
        frame.NextDawnStart = tomorrow.dawnStart;
    }

    internal static void Disarm(int slot) => _stack[slot].Armed = 0;

    /// <summary>
    /// 按 __state 退层：仅当前 top/token 的合法清理能弹本层并恢复前层 token；
    /// 复制旧 state 重清（token 失配）与重复清理（Kind 归零）都不会误弹别的层。
    /// </summary>
    internal static void Pop(ref State state)
    {
        if (state.Kind == PushKind.Frame)
        {
            if (_depth > 0 && _topToken == state.Token && _stack[_depth - 1].Token == state.Token)
            {
                _depth--;
                _topToken = state.PrevTopToken;
            }
        }
        else if (state.Kind == PushKind.Overflow)
        {
            if (_overflow > 0 && _topToken == state.Token)
            {
                _overflow--;
                _topToken = state.PrevTopToken;
            }
        }
        state.Kind = PushKind.None;
    }

    /// <summary>
    /// 匹配最内层 scope 并在全部身份/上下文复核通过后给出补偿 T'。返回 false 一律保持原生值。
    /// currentTime 在提交前现读（不用 prefix 快照）：时钟前进由“出发不早于新 N”与日计数
    /// 复核共同保守拒绝，回退/非法值由 TryPlan 的值域门拒绝。
    /// </summary>
    internal static bool TryAdjustTravel(EnemyManager enemies, Wave wave, float nativeTravel, int targetDay,
        out float adjusted)
    {
        adjusted = 0f;
        // 深度溢出子树屏蔽（不暴露父层授权）；无活动 scope 时这是普通查询，保持原值。
        if (_overflow > 0 || _depth <= 0) return false;
        ref Frame frame = ref _stack[_depth - 1];
        if (frame.Armed == 0 || frame.Consumed == 1) return false;
        if (enemies == null || wave == null) return false;
        // 只有 manager 与 wave 身份、以及本次查询的 day 都对上才算本 scope 的结果；
        // 错误日的查询保持原值且不消费本 scope 的唯一一次资格。
        if (frame.Enemies != enemies.Pointer || frame.Wave != wave.Pointer) return false;
        if (targetDay != frame.SpawnDay) return false;

        // 身份匹配成功：本 scope 的这一份结果无论后续结论如何都只处理一次。
        frame.Consumed = 1;

        // 提交前再查：开关/权限/波型/当前 manager 仍是绑定实例。
#if ANDROID
        if (!ModConfig.Enabled.Value || !ModConfig.NightDepartureEnabled.Value || !NetworkBigBoss.HasWorldAuth) return false;
#else
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return false;
#endif
        if (wave.type != WaveType.Regular) return false;
        Managers managers = Managers.Inst;
        if (managers == null || managers.Pointer != frame.Managers) return false;
        EnemyManager currentEnemies = managers.enemies;
        if (currentEnemies == null || currentEnemies.Pointer != frame.Enemies
            || currentEnemies.Pointer != enemies.Pointer) return false;
        Director director = managers.director;
        if (director == null || director.Pointer != frame.Director) return false;
        World world = managers.world;
        if (world == null || world.Pointer != frame.World) return false;
        Transform gameLayer = world.gameLayer;
        if (gameLayer == null || gameLayer.Pointer != frame.GameLayer) return false;
        GameObject layerObject = gameLayer.gameObject;
        if (layerObject == null || layerObject.scene.handle != frame.SceneHandle) return false;
        GameObject directorObject = director.gameObject;
        if (directorObject == null || directorObject.scene.handle != frame.DirectorSceneHandle) return false;
        GameObject enemiesObject = enemies.gameObject;
        if (enemiesObject == null || enemiesObject.scene.handle != frame.EnemiesSceneHandle) return false;
        if (director.CurrentSchedulerDay != frame.SchedulerDay
            || director.CurrentSeasonDay != frame.SeasonDay
            || director.CurrentDayForSpawning != frame.SpawnDay) return false;
        float currentTime = director.currentTime;

        NightDepartureTiming.Plan? plan = NightDepartureTiming.TryPlan(
            frame.Arrival, nativeTravel, frame.EveningStart, frame.NextDawnStart, currentTime);
#if ANDROID
        NightDepartureLog.ObserveAndroid(frame.SchedulerDay, frame.SeasonDay, frame.SpawnDay,
            frame.EveningStart, frame.NextDawnStart, currentTime, nativeTravel, plan);
#endif
        if (plan == null) return false;

        adjusted = plan.Value.TravelWithMargin;
        NightDepartureLog.Compensated(frame.SchedulerDay, frame.SideValue, nativeTravel, plan.Value);
        return true;
    }

#if NIGHTDEPARTURE_TEST
    internal static int TestDepth => _depth;
    internal static int TestOverflow => _overflow;
    internal static void TestReset() { _stack = null; _depth = 0; _overflow = 0; _clock = 0; _topToken = 0; }
#endif
}

/// <summary>有界日志：每次成功补偿最多一条摘要；故障一次性告警；日志失败不影响已提交结果。</summary>
internal static class NightDepartureLog
{
    private static bool _readWarned;
    private static bool _postfixWarned;
#if ANDROID
    private static bool _androidObserved;

    // 首次自然匹配的有效 scope：沿既有提交点读取与决策，不另采样/轮询或写原生状态。
    internal static void ObserveAndroid(int schedulerDay, int seasonDay, int spawnDay,
        float evening, float dawn, float current, float nativeTravel, NightDepartureTiming.Plan? plan)
    {
        if (_androidObserved) return;
        _androidObserved = true;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "ANDROID_NIGHT_DEPARTURE_SCOPE day=" + schedulerDay.ToString(CultureInfo.InvariantCulture)
                + " profileDay=" + seasonDay.ToString(CultureInfo.InvariantCulture)
                + " spawnDay=" + spawnDay.ToString(CultureInfo.InvariantCulture)
                + " E=" + F(evening) + " D=" + F(dawn) + " current=" + F(current)
                + " nativeTravel=" + F(nativeTravel)
                + " decision=" + (plan == null ? "native-kept" : "compensated")
                + " extra=" + (plan == null ? "0.00" : F(plan.Value.ExtraHours)));
        }
        catch { } // 观察失败不影响已有规划返回与原生异常；无重试。
    }
#endif

    internal static void ReadFailure(Exception e)
    {
        if (_readWarned) return;
        _readWarned = true;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[NightDeparture] scope read failed; native scheduling kept: " + e.GetType().Name);
        }
        catch { }
    }

    internal static void PostfixFailure(Exception e)
    {
        if (_postfixWarned) return;
        _postfixWarned = true;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[NightDeparture] travel postfix failed; native value kept: " + e.GetType().Name);
        }
        catch { }
    }

    /// <summary>成功补偿摘要：day/side/nativeTravel/extra/原与新出发；说明 takes 含调度余量。</summary>
    internal static void Compensated(int day, int sideValue, float nativeTravel, NightDepartureTiming.Plan plan)
    {
        try
        {
            string side = sideValue < 0 ? "Left" : "Right";
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[NightDeparture] day=" + day.ToString(CultureInfo.InvariantCulture)
                + " side=" + side
                + " nativeTravel=" + F(nativeTravel)
                + " extra=" + F(plan.ExtraHours)
                + " spawn=" + F(plan.OriginalSpawn) + "->" + F(plan.AdjustedSpawn)
                + " | scheduled takes now includes the margin; march speed/route unchanged");
        }
        catch { }
    }

    private static string F(float value) => value.ToString("F2", CultureInfo.InvariantCulture);

#if NIGHTDEPARTURE_TEST
    internal static void TestReset() { _readWarned = false; _postfixWarned = false;
#if ANDROID
        _androidObserved = false;
#endif
    }
#endif
}
