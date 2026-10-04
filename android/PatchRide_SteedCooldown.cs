// Android adaptation of il2cpp/PatchRide_SteedCooldown.cs. The desktop instanceID →
// (Native, LastApplied) cache and its SettingChanged scene scan are deliberately not carried
// over: neither their native origin nor their lifecycle was established for this build. The
// Android port scales the _cooldown that the four real consumers read at call time and
// restores that call's entering value afterwards.
using SummonGhostSteedAbility = Il2Cpp.SummonGhostSteedAbility;
using SteedAbility = Il2Cpp.SteedAbility;
using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod;

/// <summary>
/// 坐骑技能 CD 倍率（Android）。四个真实消费点各一个 prefix + 一个共享 Finalizer
/// （Operator 显式注册，本类无 [HarmonyPatch] 自动扫描）：
/// SteedAbility.Activate、BuffUnitsSteedAbility.Activate、GlideMovementSteedAbility.Activate、
/// SpeedBoostSteedAbility.Deactivate。每次非默认调用只借用当次：applied = 进入时当前
/// _cooldown × 倍率，调用结束后按 exact float 等值条件还回 original；不追溯已排程的
/// _nextActivationTime，不从 prefab 原生值猜测。Enabled 关闭或倍率 1 时不读/不写字段、
/// 不分配 __state、不置 buff owner。
/// </summary>
public static class PatchRide_SteedCooldown
{
    private enum Kind { Base, Buff, Glide, Speed }

    /// <summary>本次调用的归还凭据（引用类型；默认路径零分配）。第一笔字段写之前全部记入。</summary>
    internal sealed class Borrow
    {
        internal SteedAbility Ability;
        internal float Original;
        internal float Applied;
        internal bool IsBuff;
        internal IntPtr PreviousOwner;
    }

    // Buff 借用窗口的单一 owner 指针（只在已借窗口内非零；End 无条件还原上一个值）。base
    // 前缀只凭 __instance.Pointer == owner 判定"这是 Buff 窗口内的 base 消费"；owner 为零
    // 时短路，默认路径不访问代理。
    private static IntPtr buffOwner;

    internal static void BasePrefix(SteedAbility __instance, out Borrow __state)
        => Begin(__instance, Kind.Base, out __state);

    internal static void BuffPrefix(SteedAbility __instance, out Borrow __state)
        => Begin(__instance, Kind.Buff, out __state);

    internal static void GlidePrefix(SteedAbility __instance, out Borrow __state)
        => Begin(__instance, Kind.Glide, out __state);

    internal static void SpeedPrefix(SteedAbility __instance, out Borrow __state)
        => Begin(__instance, Kind.Speed, out __state);

    /// <summary>成功与原生异常路径唯一的一次归还；End 不抛后原样返回 native exception。</summary>
    internal static Exception Finalizer(Exception __exception, Borrow __state)
    {
        End(__state);
        return __exception;
    }

    /// <summary>
    /// 单一 prefix 错误边界：代理 null、Buff owner 判定（仅 base；owner 为零则短路不读代理，
    /// 判定先于配置读取）、直接配置读取、Ghost 仅 base 的 TryCast、凭据捕获与字段写。
    /// 任一步失败 → 可见 prefix failed 日志；已建 state 则仅一次 End 并清空 __state，
    /// Finalizer 无债可还、不重试。
    /// </summary>
    private static void Begin(SteedAbility ability, Kind kind, out Borrow state)
    {
        state = null;
        try
        {
            if (ability == null) return;
            if (kind == Kind.Base && buffOwner != IntPtr.Zero && buffOwner == ability.Pointer) return;

            if (!ModConfig.Enabled.Value) return;
            float multiplier = ModConfig.SteedCooldownMultiplier.Value;
            if (multiplier == 1f) return;

            // SummonGhost 由独立 profile 管理（PC 既有边界）：只在 base 入口用真实 interop
            // TryCast 判定（托管 is 对 base 代理不可靠）；其余三类由注册目标类型确定。
            if (kind == Kind.Base && ability.TryCast<SummonGhostSteedAbility>() != null) return;

            float original = ability._cooldown;
            float applied = original * multiplier;
            var borrow = new Borrow
            {
                Ability = ability,
                Original = original,
                Applied = applied,
                IsBuff = kind == Kind.Buff,
                // prevOwner 在 state 初始化时捕获：先于可抛的 buffOwner = ability.Pointer
                // 写入，写入失败时 End 的 finally 仍能还原外层合法 owner。
                PreviousOwner = buffOwner
            };
            state = borrow;
            if (borrow.IsBuff) buffOwner = ability.Pointer;
            ability._cooldown = applied;
        }
        catch (Exception exception)
        {
            LogErrorOnce("prefix failed", exception);
            if (state != null)
            {
                try { End(state); } finally { state = null; }
            }
        }
    }

    /// <summary>
    /// 归还：单一 try/catch/finally 包络全部 interop 访问——读取本次捕获 Ability 的
    /// _cooldown、exact float current == Applied 比较、至多一次写回 original 的 setter、
    /// 不同值保留的可见 Warning。异常只记 cleanup failed 日志，不重试、不补偿。finally
    /// 里 Buff owner 无条件还原 PreviousOwner（与字段归还成败解耦）。本方法不抛，保证
    /// Finalizer 原样返回传入的 native exception 对象。
    /// </summary>
    private static void End(Borrow state)
    {
        if (state == null) return;
        try
        {
            SteedAbility ability = state.Ability;
            float current = ability._cooldown;
            if (current == state.Applied)
            {
                ability._cooldown = state.Original;
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
        finally
        {
            if (state.IsBuff) buffOwner = state.PreviousOwner;
        }
    }

    private static readonly HashSet<string> LoggedErrors = new HashSet<string>();

    /// <summary>日志自身不外抛；同 key 只记一次，避免重复噪声。</summary>
    private static void LogErrorOnce(string key, Exception exception)
    {
        try
        {
            if (!LoggedErrors.Add(key)) return;
            MelonLoader.MelonLogger.Error("[SteedCooldown] " + key + ": " + exception);
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
            MelonLoader.MelonLogger.Warning("[SteedCooldown] restore skipped: current cooldown differs from applied (current="
                + current + " applied=" + state.Applied + " original=" + state.Original
                + " buff=" + state.IsBuff + "); kept current value");
        }
        catch (Exception)
        {
        }
    }
}
