using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 共享伤害提交结果。
/// <see cref="Submitted"/> **只代表原生 <c>Damageable.ReceiveDamage</c> 正常返回**，
/// 不代表 HP 必减：原生可能因 invulnerable / 护盾 / 已结算 / 非 authority 等自行早退或部分处理，
/// 且伤害可能已被 OnPreReceiveDamage 改写。
/// </summary>
internal enum CombatDamageResult
{
    /// <summary>未提交：调用前提（世界 authority / 目标有效存活）不满足，原生入口未被调用。</summary>
    Skipped,
    /// <summary>已调用原生入口并正常返回（不代表 HP 一定减少）。</summary>
    Submitted,
    /// <summary>原生调用抛出异常，**可能已部分生效**。绝不重试。</summary>
    Faulted,
}

/// <summary>
/// 共享同步原生伤害提交 helper：自有伤害源（希腊火矢 AoE、火枪弹丸等）统一从这里提交，
/// 保证「同一条原生入口、同一组前提、同一套异常隔离」。
///
/// 边界（本文件只做这些）：
/// - 同步单次调用，最终只调用 <c>target.ReceiveDamage(damage, source, kind)</c>：
///   复用原生护盾 / OnPreReceiveDamage / 伤害事件链，绝不提前绕开、绝不写 HP 字段、
///   不新建延迟队列、不注册 hook、不改原生箭行为。
/// - 只读前提门（全部为保守早退，不改变原生方法内部语义）：
///   世界 authority、目标/其 GO 存在、GO activeInHierarchy、Damageable 组件 enabled、未死亡。
///   其中 authority 与 activeInHierarchy 原生 <c>ReceiveDamage</c> 自身也会检查。
/// - 异常隔离：原生回调抛出的异常一律在本 helper 内捕获 → <see cref="CombatDamageResult.Faulted"/>，
///   绝不重试可能已部分生效的调用；调用方由此可以继续处理后续目标。
/// - 报告限量：首次 + 每 256 次一条 warning（与 PatchArcher_GreekImpact 的计数日志同风格）。
/// </summary>
internal static class CombatDamage
{
    /// <summary>internal 供测试与实机核对；不逐次日志。</summary>
    internal static int StatSubmitted, StatSkipped, StatFaulted;

    private const int ReportEvery = 256;
    private static int FaultReports;

    /// <summary>
    /// 提交一次同步原生伤害。任何情况下不外抛（包含原生回调异常）。
    /// </summary>
    internal static CombatDamageResult Submit(Damageable target, int damage, GameObject source, DamageSource kind)
    {
        try
        {
            if (!NetworkBigBoss.HasWorldAuth) return Skip();
            if (target == null || target.gameObject == null) return Skip();
            if (!target.gameObject.activeInHierarchy) return Skip();
            if (!target.enabled) return Skip();
            if (target.isDead) return Skip();
            target.ReceiveDamage(damage, source, kind);
            StatSubmitted++;
            return CombatDamageResult.Submitted;
        }
        catch (Exception e)
        {
            // 回调异常：伤害可能已部分生效（回调可能已发奖励/护盾已扣），绝不重试。
            StatFaulted++;
            FaultReports++;
            if (FaultReports == 1 || (FaultReports % ReportEvery) == 0) Report(e);
            return CombatDamageResult.Faulted;
        }
    }

    private static CombatDamageResult Skip()
    {
        StatSkipped++;
        return CombatDamageResult.Skipped;
    }

    /// <summary>限量报告：日志自身异常绝不影响调用方。</summary>
    private static void Report(Exception e)
    {
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[CombatDamage] faulted count=" + FaultReports + " " + e.GetType().Name);
        }
        catch { }
    }
}
