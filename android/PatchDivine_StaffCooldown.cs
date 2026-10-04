// Android adaptation of the desktop PatchDivine_HermesStaff base-cooldown slice. The desktop
// Awake/CanActivate/TriggerItemAbility profile writes, the original-range dictionary
// (Dictionary<HermesStaff,float> OriginalAbilityRanges, used there for the ×2 range write) and
// the SettingChanged FindObjectsOfType scan are deliberately not carried over: the audited Android
// native consumption of _itemCooldown on a HermesStaff instance is the state-0 single pass of
// HermesStaff._StartAbilityRoutine_d__17.MoveNext (0x252a384, schedules
// Time.time + _itemCooldown + _cooldownAddPerTroll×min(scanHits,maxConverted)), and
// ItemOfPower.CanCancel is the only audited item-level boolean reader (52-byte compare
// remaining > _itemCooldown). The Android port borrows the call-time _itemCooldown for those two
// calls only: applied = entering value × multiplier, restored after the call under exact float
// equality. The per-target additive term, the min() truncation, the scanner/range and the
// already-scheduled _nextActivationTime are never touched; no 30-second prefab constant is
// assumed (the serialized value is unknown on this build).
using HermesStaff = Il2Cpp.HermesStaff;
using ItemOfPower = Il2Cpp.ItemOfPower;
using Routine = Il2Cpp.HermesStaff._StartAbilityRoutine_d__17;
using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod;

/// <summary>
/// 法杖（HermesStaff）基础冷却倍率（Android）。两个已审真实方法各一个 prefix + 一个共享
/// Finalizer（Operator 显式注册，本类无 [HarmonyPatch] 自动扫描）：
///   - HermesStaff._StartAbilityRoutine_d__17.MoveNext：仅 __1__state==0 的首次单趟调用借值，
///     排程读取的 _itemCooldown 即当次基础值 × 倍率；附加 per-target 时间、上限截断与扫描
///     不缩放。
///   - ItemOfPower.CanCancel：只有实际 TryCast 到 HermesStaff 的实例才借值，原生比较
///     remaining > 借用后的基础值，保持与 m=1 原生公式一致的附加项窗口（a > elapsed）；
///     其他 7 个 ItemOfPower 神器不介入。
/// 每次非默认调用只借用当次：applied = 进入时当前 _itemCooldown × 倍率，调用结束后按
/// exact float 等值条件还回 original；不追溯已排程的 _nextActivationTime，不假设 30 秒
/// prefab 值。Enabled 关闭或倍率 1 时在读取 state/owner/TryCast 之前返回：零 native 接触、
/// 不分配 __state；配置只在两 hook 的共享前置里读一次。
/// 已知边界（如实保留，不预建守卫）：已审回调链（OnCooldownStarted→CooldownRemaining→
/// SignalCooldownOver）读取的是同一借用窗口内的 m·B，其等值仍受 addTerm/两次 Time 读差值
/// 约束（条件式，不无条件断言）；Activate/c4 缓存链对已审 selected Hermes 虚路由不生效
/// （ItemBasedRulerAbility.Activate override 不调 base、不读 c4、不启动 Signal）；未知第三方
/// 订阅者若在 MoveNext 借用窗口内再次查询 CanCancel，该次判定会用到 applied×倍率 的阈值
/// （归还链仍闭合、字段无损）。出现实机征兆后再按当次责任窗口诊断，当前不建 owner/静态栈。
/// </summary>
public static class PatchDivine_StaffCooldown
{
    /// <summary>本次调用的归还凭据（引用类型；默认路径零分配）。第一笔字段写之前全部记入。</summary>
    internal sealed class Borrow
    {
        internal HermesStaff Staff;
        internal float Original;
        internal float Applied;
    }

    internal static void RoutinePrefix(Routine __instance, out Borrow __state)
    {
        __state = null;
        try
        {
            if (!TryGetFactor(out float multiplier)) return;
            if (__instance == null || __instance.__1__state != 0) return;
            Begin(__instance.__4__this, multiplier, out __state);
        }
        catch (Exception exception)
        {
            Fail(ref __state, exception);
        }
    }

    internal static void CanCancelPrefix(ItemOfPower __instance, out Borrow __state)
    {
        __state = null;
        try
        {
            if (!TryGetFactor(out float multiplier)) return;
            if (__instance == null) return;
            Begin(__instance.TryCast<HermesStaff>(), multiplier, out __state);
        }
        catch (Exception exception)
        {
            Fail(ref __state, exception);
        }
    }

    /// <summary>成功与原生异常路径唯一的一次归还；End 不抛后原样返回 native exception。</summary>
    internal static Exception Finalizer(Exception __exception, Borrow __state)
    {
        End(__state);
        return __exception;
    }

    /// <summary>
    /// 两 hook 共享的配置前置：Enabled/倍率在触碰任何 native state/proxy/字段之前读取一次；
    /// 会话关闭或倍率 1（含 cfg 载入后的 clamp 结果）返回 false，调用方保持零 native 接触。
    /// 配置读取异常上抛，由 prefix 的唯一错误边界处置。
    /// </summary>
    private static bool TryGetFactor(out float multiplier)
    {
        multiplier = 1f;
        if (!ModConfig.Enabled.Value) return false;
        multiplier = ModConfig.StaffCooldownMultiplier.Value;
        return multiplier != 1f;
    }

    /// <summary>
    /// 共享借用（factor 由调用方单次读出后传入，不重复读配置）：当次 original/applied 捕获、
    /// state 登记先于 setter 写入。读写异常上抛，由各 prefix 的唯一错误边界统一处置。
    /// </summary>
    private static void Begin(HermesStaff staff, float multiplier, out Borrow state)
    {
        state = null;
        if (staff == null) return;
        float original = staff._itemCooldown;
        var borrow = new Borrow { Staff = staff, Original = original, Applied = original * multiplier };
        state = borrow;
        staff._itemCooldown = borrow.Applied;
    }

    /// <summary>
    /// prefix 唯一错误边界：代理/目标/配置/字段读取与写入任一步失败 → 可见 prefix failed
    /// 日志；已建 state 则仅一次 End 并清空 __state（Finalizer 无债可还、不重试），未建
    /// state 时只记日志。
    /// </summary>
    private static void Fail(ref Borrow state, Exception exception)
    {
        LogErrorOnce("prefix failed", exception);
        if (state == null) return;
        try { End(state); } finally { state = null; }
    }

    /// <summary>
    /// 归还：单一 try/catch 包络全部 interop 访问——读取本次捕获 staff 的 _itemCooldown、
    /// exact float current == Applied 比较、至多一次写回 original 的 setter、不同值保留的
    /// 可见 Warning。异常只记一次 cleanup failed 日志，不重试、不补偿。本方法不抛，保证
    /// Finalizer 原样返回传入的 native exception 对象。
    /// </summary>
    private static void End(Borrow state)
    {
        if (state == null) return;
        try
        {
            HermesStaff staff = state.Staff;
            float current = staff._itemCooldown;
            if (current == state.Applied)
            {
                staff._itemCooldown = state.Original;
            }
            else
            {
                LogRestoreSkipped(state, current);
            }
        }
        catch (Exception exception)
        {
            LogErrorOnce("cleanup failed", exception);
        }
    }

    private static readonly HashSet<string> LoggedErrors = new HashSet<string>();

    /// <summary>日志走既有 HoldBridges.LogSource 桥；同 key 只记一次，日志自身不外抛。</summary>
    private static void LogErrorOnce(string key, Exception exception)
    {
        try
        {
            if (!LoggedErrors.Add(key)) return;
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[StaffCooldown] " + key + ": " + exception);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>不同值保留的可见日志；只说当前值不同，不声明写入者身份。</summary>
    private static void LogRestoreSkipped(Borrow state, float current)
    {
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[StaffCooldown] restore skipped: current cooldown differs from applied (current="
                + current + " applied=" + state.Applied + " original=" + state.Original + "); kept current value");
        }
        catch (Exception)
        {
        }
    }
}
