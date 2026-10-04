// Android adaptation of the Boat passenger-capacity policy shared through
// il2cpp/BoatCapacityProfile.cs. The desktop source keeps its Postfix+Finalizer
// pair; the Android port follows the Issue #127 single-cleanup form: one Prefix
// captures the four entering values, one Finalizer performs the single End.
// The native consumption window is Boat.OnEnable: it reads maxKnights, maxWorkers,
// the _archerPositions length, then (behind CanPikemenAndFarmersEmbarkMainBoat)
// maxPikemen/maxFarmers, and feeds each into Embarkable::RegisterUnitSlots. This
// patch borrows only that window; it never writes slots, the boat position, sailing,
// saving or the native gates, and OFF/newly-registered boats keep their slots.
using Boat = Il2Cpp.Boat;
using System;

namespace KingdomEnhancedMod;

/// <summary>
/// 主船乘员容量（Android）。Operator 显式注册（本类无 [HarmonyPatch] 自动扫描）：
/// `Boat.OnEnable()` 一个 Prefix + 一个 Finalizer。仅在 `Enabled && BoatCapacityEnabled`
/// （默认 OFF）时介入：先完整读取四个当前 max（不用 ctor 默认 3 冒充），全部成功后才建并
/// 发布 Borrow；每个字段先置 attempt 责任位再按共享 profile 写目标值。成功与原生异常路径
/// 都只由 Finalizer 执行一次 End；Prefix 自身写阶段失败则内联一次 End 并在 finally 清空
/// __state，Finalizer 无债可还、不重试。End 只处理 attempt 字段、逐字段独立 try/catch：
/// 读 current，等于该 profile 目标值才至多写回一次进入值；不同值保留并记 Warning（不识别
/// 写入者、同值外部写不可判来源）；单个归还失败只记 Error，不影响其它字段，不重试。
/// 不写 __runOriginal/result/slots/船位置/航海/存档/native gate，不热改已登记 slots，
/// 也不为登记早退或门变化加补偿。
/// </summary>
public static class PatchWorld_BoatCapacity
{
    /// <summary>本次调用的归还凭据（引用类型；默认路径零分配）。第一笔字段写之前全部记入。</summary>
    internal sealed class Borrow
    {
        internal Boat Target;
        internal int OriginalWorkers;
        internal int OriginalKnights;
        internal int OriginalPikemen;
        internal int OriginalFarmers;
        internal bool AttemptWorkers;
        internal bool AttemptKnights;
        internal bool AttemptPikemen;
        internal bool AttemptFarmers;
    }

    internal static void Prefix(Boat __instance, out Borrow __state)
    {
        __state = null;
        try
        {
            if (__instance == null) return;
            if (!ModConfig.Enabled.Value || !ModConfig.BoatCapacityEnabled.Value) return;

            // 四个 getter 全部成功后才建/发布 Borrow：任一失败 → 无 state、零 setter、
            // 零 cleanup，只留可见 error（catch 包络）。
            int originalWorkers = __instance.maxWorkers;
            int originalKnights = __instance.maxKnights;
            int originalPikemen = __instance.maxPikemen;
            int originalFarmers = __instance.maxFarmers;
            var borrow = new Borrow
            {
                Target = __instance,
                OriginalWorkers = originalWorkers,
                OriginalKnights = originalKnights,
                OriginalPikemen = originalPikemen,
                OriginalFarmers = originalFarmers
            };
            __state = borrow;

            // 每字段先置 attempt 责任位再写：setter 写前抛/写后抛都能确定责任，不猜成功。
            borrow.AttemptWorkers = true;
            __instance.maxWorkers = BoatCapacityProfile.Workers;
            borrow.AttemptKnights = true;
            __instance.maxKnights = BoatCapacityProfile.Knights;
            borrow.AttemptPikemen = true;
            __instance.maxPikemen = BoatCapacityProfile.Pikemen;
            borrow.AttemptFarmers = true;
            __instance.maxFarmers = BoatCapacityProfile.Farmers;
        }
        catch (Exception exception)
        {
            // Borrow 未发布 ⇒ 失败在读取阶段（无 state/零写/零 cleanup）；已发布 ⇒ 写阶段失败
            // （内联一次 End）。两类各自首个失败可见，同 key 去重避免重复噪声。
            LogErrorOnce(__state == null ? "prefix failed getters" : "prefix failed writes", exception);
            if (__state != null)
            {
                try { End(__state); } finally { __state = null; }
            }
        }
    }

    /// <summary>成功与原生异常路径唯一的一次归还；End 不抛后原样返回传入的 native exception。</summary>
    internal static Exception Finalizer(Exception __exception, Borrow __state)
    {
        End(__state);
        return __exception;
    }

    /// <summary>
    /// 归还：只处理 attempt 字段，逐字段独立 try/catch。读 current 与该字段 profile 目标值
    /// exact int 等值比较，相等才至多写回一次 Original；不同值保留并 Warning（不声明写入者
    /// 身份，同值外部写不可区分）。单字段失败只记 Error，其余字段照常处理、不重试该字段。
    /// 本方法不抛，保证 Finalizer 原样返回传入的原生 exception 对象。
    /// </summary>
    private static void End(Borrow state)
    {
        if (state == null) return;
        if (state.AttemptWorkers)
        {
            try
            {
                int current = state.Target.maxWorkers;
                if (current == BoatCapacityProfile.Workers) state.Target.maxWorkers = state.OriginalWorkers;
                else LogRestoreSkipped("maxWorkers", current, BoatCapacityProfile.Workers, state.OriginalWorkers);
            }
            catch (Exception exception)
            {
                LogErrorOnce("cleanup failed maxWorkers", exception);
            }
        }
        if (state.AttemptKnights)
        {
            try
            {
                int current = state.Target.maxKnights;
                if (current == BoatCapacityProfile.Knights) state.Target.maxKnights = state.OriginalKnights;
                else LogRestoreSkipped("maxKnights", current, BoatCapacityProfile.Knights, state.OriginalKnights);
            }
            catch (Exception exception)
            {
                LogErrorOnce("cleanup failed maxKnights", exception);
            }
        }
        if (state.AttemptPikemen)
        {
            try
            {
                int current = state.Target.maxPikemen;
                if (current == BoatCapacityProfile.Pikemen) state.Target.maxPikemen = state.OriginalPikemen;
                else LogRestoreSkipped("maxPikemen", current, BoatCapacityProfile.Pikemen, state.OriginalPikemen);
            }
            catch (Exception exception)
            {
                LogErrorOnce("cleanup failed maxPikemen", exception);
            }
        }
        if (state.AttemptFarmers)
        {
            try
            {
                int current = state.Target.maxFarmers;
                if (current == BoatCapacityProfile.Farmers) state.Target.maxFarmers = state.OriginalFarmers;
                else LogRestoreSkipped("maxFarmers", current, BoatCapacityProfile.Farmers, state.OriginalFarmers);
            }
            catch (Exception exception)
            {
                LogErrorOnce("cleanup failed maxFarmers", exception);
            }
        }
    }

    private static readonly System.Collections.Generic.HashSet<string> LoggedErrors = new System.Collections.Generic.HashSet<string>();

    /// <summary>日志自身不外抛；同 key 只记一次，避免重复噪声。</summary>
    private static void LogErrorOnce(string key, Exception exception)
    {
        try
        {
            if (!LoggedErrors.Add(key)) return;
            MelonLoader.MelonLogger.Error("[BoatCapacity] " + key + ": " + exception);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>不同值保留的可见日志；只说当前值不同，不声明写入者身份。</summary>
    private static void LogRestoreSkipped(string field, int current, int applied, int original)
    {
        try
        {
            MelonLoader.MelonLogger.Warning("[BoatCapacity] restore skipped field=" + field + ": current " + current
                + " differs from applied " + applied + " (original=" + original + "); kept current value");
        }
        catch (Exception)
        {
        }
    }
}
