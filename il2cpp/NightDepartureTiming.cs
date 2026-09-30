namespace KingdomEnhancedMod;

/// <summary>
/// 普通远距夜袭出发补偿的纯浮点策略（issue-94，用户授权“最多提前两个小时”）。
///
/// 本类只做算术：不读任何游戏/Unity/BepInEx 状态，输入由
/// <see cref="PatchWorld_NightDeparture"/> 的短生命周期 scope 验证后传入，任何门不满足
/// 都返回 null（保持原生 GetWaveTravelTime 结果）。契约：
/// - A 严格复用原生归一 <c>arrival &gt; 12f ? arrival : arrival + 24f</c>（跨午夜绝对小时，
///   不取模换日，不改 arrival 实参；实际 2.4 ScheduleWaveToday RVA 0x4e57a0 的
///   comiss 12f / addss 24f 同款）。
/// - “近门”阈值 T≤1 与补偿上限 2 小时是本策略自选的政策值，不是原生分类事实。
/// - E = 当日（排期日）真实 profile 的 eveningStart，D = 次日真实 profile 的 dawnStart
///   （跨季节须按各自日查询，见 scope：GetTimesOfDayForDay(seasonDay) 与 (+1)），
///   H = 24 + D，N = currentTime，L = max(E, N)。
/// - 保守双端窗口：原生计划 S0 = A - T 本身必须落在 E ≤ S0 &lt; H 且 S0 &gt; N；
///   原生更早（S0 &lt; E）、已过期（S0 ≤ N）或排进次日白天的计划一律原样保留，绝不延后。
/// - d = min(2, T-1, S0-L)，仅 d &gt; 0 时用 T' = T + d；在与原生一致的 float 语义下
///   复算 S = A - T'，逐门复查（含 0 &lt; T'-T ≤ 2、L ≤ S ≤ S0、S &lt; H），
///   舍入越界直接放弃本次补偿——不引入任何 epsilon 跨界。
/// </summary>
internal static class NightDepartureTiming
{
    /// <summary>单次补偿上限（游戏小时）。</summary>
    internal const float MaxExtraHours = 2f;

    /// <summary>“近门”阈值：原生行程估计不超过该值时不加余量。</summary>
    internal const float NearTravelHours = 1f;

    /// <summary>原生 ScheduleWaveToday 的到达归一：arrival&gt;12 保持，否则 +24（跨午夜绝对小时）。</summary>
    internal static float NormalizeArrival(float arrival) => arrival > 12f ? arrival : arrival + 24f;

    /// <summary>一次成功补偿的重算结果。TravelWithMargin 是“原生行程估计 + 调度余量”，不是真实行军速度变化。</summary>
    internal readonly struct Plan
    {
        internal Plan(float travelWithMargin, float extraHours, float originalSpawn, float adjustedSpawn)
        {
            TravelWithMargin = travelWithMargin;
            ExtraHours = extraHours;
            OriginalSpawn = originalSpawn;
            AdjustedSpawn = adjustedSpawn;
        }

        /// <summary>T' = T + d：写回 __result 的值；原生日志会把它当 takes。</summary>
        internal readonly float TravelWithMargin;
        /// <summary>d：本次新增的调度余量（0 &lt; d ≤ 2）。</summary>
        internal readonly float ExtraHours;
        /// <summary>S0 = A - T：原生原计划的出发时刻。</summary>
        internal readonly float OriginalSpawn;
        /// <summary>S = A - T'：补偿后的出发时刻。</summary>
        internal readonly float AdjustedSpawn;
    }

    /// <summary>
    /// 评估一次普通夜袭排期。arrival 为原生原始实参（本方法内部按原生规则归一），
    /// travel 为本次原生 GetWaveTravelTime 真实返回，eveningStart/nextDawnStart 为
    /// 当日与次日 profile 读取值，currentTime 为同一排期日的当前时刻。
    /// 返回 null 表示保持原生；返回 Plan 表示提交 T' = Plan.TravelWithMargin。
    /// </summary>
    internal static Plan? TryPlan(float arrival, float travel, float eveningStart, float nextDawnStart, float currentTime)
    {
        if (!IsFinite(arrival) || !IsFinite(travel) || !IsFinite(eveningStart)
            || !IsFinite(nextDawnStart) || !IsFinite(currentTime)) return null;
        if (!(arrival >= 0f) || !(arrival <= 24f)) return null;
        if (!(currentTime >= 0f) || !(currentTime <= 24f)) return null;
        if (!(travel >= 0f)) return null;
        // 日相窗口 0 <= D < E < 24（D 次日黎明、E 当日黄昏）。
        if (!(nextDawnStart >= 0f) || !(eveningStart > nextDawnStart) || !(eveningStart < 24f)) return null;

        float absoluteArrival = NormalizeArrival(arrival);
        float dawnBound = 24f + nextDawnStart;
        float latest = eveningStart >= currentTime ? eveningStart : currentTime; // L = max(E, N)
        float originalSpawn = absoluteArrival - travel;

        // 保守双端窗口 + 不过期：原生更早/过期/次日白天的计划原样保留，不人为延后。
        if (!(originalSpawn >= eveningStart) || !(originalSpawn < dawnBound)) return null;
        if (!(originalSpawn > currentTime)) return null;

        float byNear = travel - NearTravelHours;      // T-1：近门不补
        if (!(byNear > 0f)) return null;
        float byWindow = originalSpawn - latest;      // S0-L：不提前到黄昏前/现在之前
        float extra = byNear <= byWindow ? byNear : byWindow;
        if (extra > MaxExtraHours) extra = MaxExtraHours;
        if (!(extra > 0f)) return null;

        float travelWithMargin = travel + extra;
        float adjustedSpawn = absoluteArrival - travelWithMargin;

        // 原生 float 语义复算后逐门复查；舍入越界放弃本次补偿（不放宽 epsilon）。
        float applied = travelWithMargin - travel;
        if (!(applied > 0f) || !(applied <= MaxExtraHours)) return null;
        if (!(adjustedSpawn >= latest) || !(adjustedSpawn <= originalSpawn) || !(adjustedSpawn < dawnBound)) return null;

        return new Plan(travelWithMargin, extra, originalSpawn, adjustedSpawn);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
